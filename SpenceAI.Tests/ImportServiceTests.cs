using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Categories;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;
using SpenceAI.Infrastructure;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

/// <summary>End-to-end import flow against a real (in-memory) SQLite DB with a scripted fake LLM.</summary>
public sealed class ImportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly FakeChatClient _ai = new();
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "spence-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ImportService _imports;

    public ImportServiceTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        CategoryTemplates.SeedAsync(_db).GetAwaiter().GetResult();

        var settings = new SettingsService(_db, new PlainProtector(), new FakeFactory(_ai));
        _imports = new ImportService(_db, new ImportFileStore(new StorageOptions { DataDirectory = _dataDir }), new NoopQueue(),
            new PdfTextExtractor(), new TransactionExtractor(), settings, NullLogger<ImportService>.Instance);
    }

    [Fact]
    public async Task Csv_import_is_staged_matched_and_committed()
    {
        _ai.Reply(
            new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 42.10, Type = "expense", Description = "WHOLE FOODS #123", Merchant = "Whole Foods", Category = "Groceries" },
            new { Date = "2026-05-03", DatePrinted = "2026-05-03", Amount = 3000, Type = "income", Description = "ACME PAYROLL", Merchant = "Acme", Category = "Salary" },
            new { Date = "2026-05-04", DatePrinted = "2026-05-04", Amount = 18, Type = "expense", Description = "CLIMBING GYM", Merchant = "Boulder Co", Category = "Sports" });

        var batch = await RunImportAsync();

        Assert.Equal(ImportStatus.ReadyForReview, batch.Status);
        Assert.Equal(3, batch.Rows!.Count);
        var groceries = await _db.Categories.SingleAsync(c => c.Name == "Groceries");
        Assert.Equal(groceries.Id, batch.Rows[0].CategoryId);
        Assert.Equal(TransactionType.Income, batch.Rows[1].Type);
        Assert.Null(batch.Rows[2].CategoryId);
        Assert.Equal("Sports", batch.Rows[2].NewCategoryName);

        var committed = await _imports.CommitAsync(batch.Id, new CommitImportRequest(batch.Rows.Select(ToCommit).ToList()), default);

        Assert.Equal(ImportStatus.Committed, committed.Status);
        Assert.Equal(3, await _db.Transactions.CountAsync());
        Assert.True(await _db.Categories.AnyAsync(c => c.Name == "Sports"));
    }

    [Fact]
    public async Task Committed_rows_are_tagged_with_the_chosen_account()
    {
        var card = new Account { Name = "Amex", Kind = AccountKind.Card };
        _db.Accounts.Add(card);
        await _db.SaveChangesAsync();
        _ai.Reply(new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 42.10, Type = "expense", Description = "WHOLE FOODS #123", Merchant = "Whole Foods", Category = "Groceries" });

        var batch = await RunImportAsync();
        await _imports.CommitAsync(batch.Id, new CommitImportRequest(batch.Rows!.Select(ToCommit).ToList(), card.Id), default);

        Assert.Equal(card.Id, (await _db.Transactions.SingleAsync()).AccountId);
        await Assert.ThrowsAsync<AppException>(async () =>
        {
            _ai.Reply(new { Date = "2026-05-09", DatePrinted = "2026-05-09", Amount = 5, Type = "expense", Description = "X", Merchant = "X", Category = "Groceries" });
            var next = await RunImportAsync();
            await _imports.CommitAsync(next.Id, new CommitImportRequest(next.Rows!.Select(ToCommit).ToList(), 999), default);
        });
    }

    [Fact]
    public async Task Reimporting_the_same_rows_flags_duplicates()
    {
        object row = new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 42.10, Type = "expense", Description = "WHOLE FOODS #123", Merchant = "Whole Foods", Category = "Groceries" };
        _ai.Reply(row);
        var first = await RunImportAsync();
        await _imports.CommitAsync(first.Id, new CommitImportRequest(first.Rows!.Select(ToCommit).ToList()), default);

        _ai.Reply(row);
        var second = await RunImportAsync();

        var staged = Assert.Single(second.Rows!);
        Assert.True(staged.IsDuplicate);
        Assert.False(staged.Include);
    }

    [Fact]
    public async Task Duplicates_ignore_type_say_what_they_match_and_match_one_to_one()
    {
        object waiver = new { Date = "2026-10-22", DatePrinted = "22/10/2026", Amount = 4, Type = "expense", Description = "PETRO SURCHARGE WAIVER", Merchant = (string?)null, Category = "Fuel" };
        _ai.Reply(waiver);
        var first = await RunImportAsync();
        await _imports.CommitAsync(first.Id, new CommitImportRequest(first.Rows!.Select(ToCommit).ToList()), default);

        // Same statement again, now read as a refund, with a second identical waiver on the same day.
        object asRefund = new { Date = "2026-10-22", DatePrinted = "22/10/2026", Amount = 4, Type = "refund", Description = "PETRO SURCHARGE WAIVER", Merchant = (string?)null, Category = "Fuel" };
        _ai.Reply(asRefund, asRefund);
        var second = await RunImportAsync();

        Assert.True(second.Rows![0].IsDuplicate);
        Assert.StartsWith("Already saved from an import", second.Rows[0].DuplicateOf);
        Assert.False(second.Rows[1].IsDuplicate);
    }

    [Fact]
    public async Task Changing_a_suggested_category_creates_a_rule_used_next_time()
    {
        var dining = await _db.Categories.SingleAsync(c => c.Name == "Dining");
        _ai.Reply(new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 9.5, Type = "expense", Description = "BLUE BOTTLE 22", Merchant = "Blue Bottle", Category = "Groceries" });
        var first = await RunImportAsync();
        var corrected = first.Rows!.Select(r => ToCommit(r) with { CategoryId = dining.Id }).ToList();
        await _imports.CommitAsync(first.Id, new CommitImportRequest(corrected), default);

        var rule = Assert.Single(await _db.MerchantRules.ToListAsync());
        Assert.Equal("BLUE BOTTLE", rule.Pattern);

        _ai.Reply(new { Date = "2026-05-09", DatePrinted = "2026-05-09", Amount = 7, Type = "expense", Description = "BLUE BOTTLE 22", Merchant = "Blue Bottle", Category = "Groceries" });
        var second = await RunImportAsync();

        Assert.Equal(dining.Id, second.Rows![0].CategoryId);
        Assert.Equal("BLUE BOTTLE", second.Rows[0].MatchedRule);
    }

    [Fact]
    public async Task Undoing_a_committed_import_removes_its_transactions()
    {
        _ai.Reply(new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 5, Type = "expense", Description = "Coffee", Merchant = "Cafe", Category = "Dining" });
        var batch = await RunImportAsync();
        await _imports.CommitAsync(batch.Id, new CommitImportRequest(batch.Rows!.Select(ToCommit).ToList()), default);

        await _imports.DiscardAsync(batch.Id, default);

        Assert.Equal(0, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Ai_failure_marks_the_import_failed_with_a_message()
    {
        _ai.Fail(new HttpRequestException("connection refused"));
        var batch = await RunImportAsync();

        Assert.Equal(ImportStatus.Failed, batch.Status);
        Assert.Contains("Ollama", batch.Error);
    }

    [Fact]
    public async Task Amounts_not_printed_on_the_statement_are_flagged_and_unticked()
    {
        // The model merged "404.00" and its "+ 4.00" waiver into 408.
        _ai.Reply(new { Date = "2026-11-03", DatePrinted = "03/11/2026", Amount = 408, Type = "expense", Description = "SUPREME PETROLEUM", Merchant = "Supreme Petroleum", Category = "Fuel" });

        var batch = await RunImportAsync("03/11/2026 SUPREME PETROLEUMKANPUR NAGAR ₹ 404.00\n03/11/2026 PETRO SURCHARGE WAIVER + ₹ 4.00\n");

        var row = Assert.Single(batch.Rows!);
        Assert.NotNull(row.Warning);
        Assert.False(row.Include);
    }

    [Fact]
    public async Task Amounts_printed_with_a_plus_become_refunds_that_reduce_spending()
    {
        _ai.Reply(
            new { Date = "2026-11-13", DatePrinted = "13/11/2026", Amount = 404, Type = "expense", Description = "SUPREME PETROLEUM", Merchant = "Supreme Petroleum", Category = "Fuel" },
            new { Date = "2026-11-13", DatePrinted = "13/11/2026", Amount = 4, Type = "expense", Description = "PETRO SURCHARGE WAIVER", Merchant = (string?)null, Category = "Fuel" });

        // 13/11 is unambiguous (day-first). An ambiguous date like 03/11 falls back to machine culture,
        // which is month-first on the CI runner and would put these rows in March.
        var batch = await RunImportAsync("13/11/2026 SUPREME PETROLEUMKANPUR NAGAR ₹ 404.00\n13/11/2026 PETRO SURCHARGE WAIVER + ₹ 4.00\n");

        Assert.Equal(TransactionType.Expense, batch.Rows![0].Type);
        Assert.Equal(TransactionType.Refund, batch.Rows[1].Type);
        Assert.All(batch.Rows, r => Assert.Null(r.Warning));

        await _imports.CommitAsync(batch.Id, new CommitImportRequest(batch.Rows.Select(ToCommit).ToList()), default);
        var overview = await new AnalyticsService(_db).GetOverviewAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), default);
        Assert.Equal(400m, overview.Spent);
        Assert.Equal(400m, Assert.Single(overview.ByCategory).Total);
        Assert.Equal(0m, overview.Income);
    }

    [Fact]
    public async Task Marking_a_row_as_transfer_is_learned_for_that_merchant()
    {
        _ai.Reply(new { Date = "2026-10-27", DatePrinted = "27/10/2026", Amount = 1960, Type = "income", Description = "BPPY PYMT AZ0163001139525x8Frx", Merchant = "BPPY", Category = "Other income" });
        var first = await RunImportAsync();
        Assert.Equal(TransactionType.Income, first.Rows![0].Type);
        var asTransfer = ToCommit(first.Rows[0]) with { Type = TransactionType.Transfer, CategoryId = null, NewCategoryName = null };
        await _imports.CommitAsync(first.Id, new CommitImportRequest([asTransfer]), default);

        var rule = Assert.Single(await _db.MerchantRules.ToListAsync());
        Assert.Equal(TransactionType.Transfer, rule.Type);
        Assert.Null(rule.CategoryId);

        _ai.Reply(new { Date = "2026-11-27", DatePrinted = "27/11/2026", Amount = 2500, Type = "income", Description = "BPPY PYMT ZZ99", Merchant = "BPPY", Category = "Other income" });
        var second = await RunImportAsync();

        Assert.Equal(TransactionType.Transfer, second.Rows![0].Type);
        Assert.Null(second.Rows[0].CategoryId);
    }

    private static object Upi(string date, decimal amount, string type, string handle, string category) =>
        new { Date = date, DatePrinted = date, Amount = amount, Type = type, Description = $"UPI/Amazon Pay/{handle}@apl/Request fr/AXIS BANK/6300{amount:0}", Merchant = "Amazon Pay", Category = category };

    [Fact]
    public async Task Overriding_a_rule_on_one_row_adds_a_specific_exception_instead_of_breaking_the_rule()
    {
        var shopping = await _db.Categories.SingleAsync(c => c.Name == "Shopping");
        _db.MerchantRules.Add(new MerchantRule { Pattern = "AMAZON PAY", Direction = MoneyDirection.Out, CategoryId = shopping.Id });
        await _db.SaveChangesAsync();

        _ai.Reply(Upi("2026-05-02", 499, "expense", "amazon", "Shopping"), Upi("2026-05-05", 12000, "expense", "amazonpaybill", "Shopping"));
        var first = await RunImportAsync();
        Assert.All(first.Rows!, r => Assert.Equal("AMAZON PAY", r.MatchedRule));

        // The user marks only the card bill payment as a transfer.
        var commit = first.Rows!.Select(r => r.Amount == 12000
            ? ToCommit(r) with { Type = TransactionType.Transfer, CategoryId = null }
            : ToCommit(r)).ToList();
        await _imports.CommitAsync(first.Id, new CommitImportRequest(commit), default);

        var rules = await _db.MerchantRules.OrderBy(r => r.Pattern).ToListAsync();
        Assert.Equal(["AMAZON PAY", "AMAZON PAY AMAZONPAYBILL"], rules.Select(r => r.Pattern));
        Assert.Equal(shopping.Id, rules[0].CategoryId);
        Assert.Equal(TransactionType.Transfer, rules[1].Type);

        _ai.Reply(Upi("2026-06-02", 899, "expense", "amazon", "Shopping"), Upi("2026-06-05", 9000, "expense", "amazonpaybill", "Shopping"));
        var second = await RunImportAsync();
        Assert.Equal(TransactionType.Expense, second.Rows![0].Type);
        Assert.Equal(shopping.Id, second.Rows[0].CategoryId);
        Assert.Equal(TransactionType.Transfer, second.Rows[1].Type);
        Assert.Equal("AMAZON PAY AMAZONPAYBILL", second.Rows[1].MatchedRule);
    }

    [Fact]
    public async Task Type_corrections_are_learned_per_direction()
    {
        // Amazon credits are refunds, not income; Amazon purchases must stay expenses.
        _ai.Reply(new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 300, Type = "income", Description = "AMAZON RETAIL", Merchant = "Amazon", Category = "Other income" });
        var first = await RunImportAsync();
        var shopping = await _db.Categories.SingleAsync(c => c.Name == "Shopping");
        await _imports.CommitAsync(first.Id, new CommitImportRequest([ToCommit(first.Rows![0]) with { Type = TransactionType.Refund, CategoryId = shopping.Id }]), default);

        var rule = Assert.Single(await _db.MerchantRules.ToListAsync());
        Assert.Equal(MoneyDirection.In, rule.Direction);
        Assert.Equal(TransactionType.Refund, rule.Type);

        _ai.Reply(
            new { Date = "2026-06-02", DatePrinted = "2026-06-02", Amount = 150, Type = "income", Description = "AMAZON RETAIL", Merchant = "Amazon", Category = "Other income" },
            new { Date = "2026-06-03", DatePrinted = "2026-06-03", Amount = 999, Type = "expense", Description = "AMAZON RETAIL", Merchant = "Amazon", Category = "Shopping" });
        var second = await RunImportAsync();
        Assert.Equal(TransactionType.Refund, second.Rows![0].Type);
        Assert.Equal(shopping.Id, second.Rows[0].CategoryId);
        Assert.Equal(TransactionType.Expense, second.Rows[1].Type);
        Assert.Null(second.Rows[1].MatchedRule);
    }

    [Fact]
    public async Task Just_this_once_saves_the_edit_without_learning_a_rule()
    {
        var dining = await _db.Categories.SingleAsync(c => c.Name == "Dining");
        _ai.Reply(new { Date = "2026-05-02", DatePrinted = "2026-05-02", Amount = 9.5, Type = "expense", Description = "BLUE BOTTLE 22", Merchant = "Blue Bottle", Category = "Groceries" });
        var batch = await RunImportAsync();
        await _imports.CommitAsync(batch.Id, new CommitImportRequest([ToCommit(batch.Rows![0]) with { CategoryId = dining.Id, Remember = false }]), default);

        Assert.Empty(await _db.MerchantRules.ToListAsync());
        Assert.Equal(dining.Id, (await _db.Transactions.SingleAsync()).CategoryId);
    }

    [Fact]
    public void Rule_hints_only_cover_merchants_on_the_statement()
    {
        var rules = new[]
        {
            new MerchantRule { Pattern = "AMAZON PAY", Direction = MoneyDirection.Out, Type = TransactionType.Transfer },
            new MerchantRule { Pattern = "SWIGGY", Direction = MoneyDirection.Out, Category = new Category { Name = "Dining" } },
        };

        var hint = Assert.Single(RuleBook.HintsFor("03/11 UPI/Amazon Pay/x@apl 1,200.00", rules));
        Assert.Equal("AMAZON PAY (money out) -> transfer", hint);
    }

    [Fact]
    public async Task Broken_pdf_is_rejected_at_upload_without_a_password_code()
    {
        var pdf = new MemoryStream("%PDF-1.4 not really"u8.ToArray());
        var error = await Assert.ThrowsAsync<AppException>(() => _imports.CreateAsync("statement.pdf", pdf.Length, pdf, default));
        Assert.Null(error.Code);
    }

    /// <param name="statement">The file's text. Defaults to a CSV that prints every amount the fake AI returns.</param>
    private async Task<ImportBatchDto> RunImportAsync(string? statement = null)
    {
        statement ??= "Date,Description,Amount\n2026-05-02,whatever,1\n" + string.Concat(_ai.Amounts.Select(a => $"2026-05-02,row,{a}\n"));
        var csv = new MemoryStream(Encoding.UTF8.GetBytes(statement));
        var created = await _imports.CreateAsync("statement.csv", csv.Length, csv, default);
        await _imports.ProcessAsync(created.Id, default);
        _db.ChangeTracker.Clear();
        return await _imports.GetAsync(created.Id, default);
    }

    private static CommitRow ToCommit(StagedRow r) =>
        new(r.Date, r.Amount, r.Type, r.Description, r.Merchant, r.CategoryId, r.NewCategoryName, r.Index);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    // ---- Fakes ----

    private sealed class FakeChatClient : IChatClient
    {
        private string _json = """{"transactions":[]}""";
        private Exception? _error;

        /// <summary>The amounts in the current reply, as JSON numbers, so the fake statement can print them.</summary>
        public List<string> Amounts { get; } = [];

        public void Reply(params object[] transactions)
        {
            _error = null;
            _json = JsonSerializer.Serialize(new { transactions });
            using var doc = JsonDocument.Parse(_json);
            Amounts.Clear();
            Amounts.AddRange(doc.RootElement.GetProperty("transactions").EnumerateArray().Select(t => t.GetProperty("Amount").GetRawText()));
        }

        public void Fail(Exception error) => _error = error;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            _error is not null ? Task.FromException<ChatResponse>(_error) : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _json)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(AiConnection connection) => client;
        public Task<IReadOnlyList<string>> ListOllamaModelsAsync(string ollamaUrl, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string? protectedValue) => protectedValue;
    }

    private sealed class NoopQueue : IImportQueue
    {
        public ValueTask EnqueueAsync(Guid batchId, CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
