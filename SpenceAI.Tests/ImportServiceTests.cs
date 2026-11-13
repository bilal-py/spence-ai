using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SpenceAI.Application;
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

    private async Task<ImportBatchDto> RunImportAsync()
    {
        var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n2026-05-02,whatever,1\n"));
        var created = await _imports.CreateAsync("statement.csv", csv.Length, csv, default);
        await _imports.ProcessAsync(created.Id, default);
        _db.ChangeTracker.Clear();
        return await _imports.GetAsync(created.Id, default);
    }

    private static CommitRow ToCommit(StagedRow r) =>
        new(r.Date, r.Amount, r.Type, r.Description, r.Merchant, r.CategoryId, r.NewCategoryName);

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

        public void Reply(params object[] transactions)
        {
            _error = null;
            _json = JsonSerializer.Serialize(new { transactions });
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
