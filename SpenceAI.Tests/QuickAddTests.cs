using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SpenceAI.Application;
using SpenceAI.Application.Categories;
using SpenceAI.Application.QuickAdd;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

public class QuickAddParserTests
{
    // A Saturday.
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static readonly Account[] Accounts =
    [
        new() { Id = 1, Name = "HDFC Savings", Kind = AccountKind.Bank },
        new() { Id = 2, Name = "Amex Card", Kind = AccountKind.Card },
        new() { Id = 3, Name = "Cash", Kind = AccountKind.Cash },
    ];

    private static ParsedLine One(string text) => Assert.Single(QuickAddParser.Parse(text, Today, Accounts));

    [Fact]
    public void Reads_amount_merchant_and_account_word()
    {
        var r = One("chai 40 upi");
        Assert.Equal(40m, r.Amount);
        Assert.Equal("Chai", r.Merchant);
        Assert.Equal(TransactionType.Expense, r.Type);
        Assert.Equal(1, r.AccountId); // UPI → the bank account
        Assert.Equal(Today, r.Date);
        Assert.Empty(r.Unsure);
    }

    [Theory]
    [InlineData("uber 250 yesterday", 2026, 11, 13)]
    [InlineData("uber 250 day before yesterday", 2026, 11, 12)]
    [InlineData("uber 250 mon", 2026, 11, 9)]
    [InlineData("uber 250 sat", 2026, 11, 14)]
    [InlineData("uber 250 last sat", 2026, 11, 7)]
    [InlineData("uber 250 on 12 nov", 2026, 11, 12)]
    [InlineData("uber 250 nov 3rd", 2026, 11, 3)]
    [InlineData("uber 250 28 dec", 2025, 12, 28)]
    [InlineData("uber 250 2026-10-01", 2026, 10, 1)]
    public void Reads_date_words(string text, int y, int m, int d)
    {
        var r = One(text);
        Assert.Equal(new DateOnly(y, m, d), r.Date);
        Assert.Equal(250m, r.Amount);
        Assert.Equal("Uber", r.Merchant);
    }

    [Theory]
    [InlineData("₹499 amazon", 499)]
    [InlineData("amazon rs.1,250", 1250)]
    [InlineData("amazon 1.2k", 1200)]
    [InlineData("amazon 2.5L", 250000)]
    [InlineData("amazon 250/-", 250)]
    [InlineData("amazon 99.50", 99.5)]
    public void Reads_amount_formats(string text, decimal expected) => Assert.Equal(expected, One(text).Amount);

    [Fact]
    public void Picks_the_largest_number_and_flags_it()
    {
        var r = One("2 samosa 30");
        Assert.Equal(30m, r.Amount);
        Assert.Equal("2 Samosa", r.Merchant);
        Assert.Contains("amount", r.Unsure);
    }

    [Fact]
    public void Type_words()
    {
        var salary = One("got 80k salary");
        Assert.Equal(TransactionType.Income, salary.Type);
        Assert.Equal(80_000m, salary.Amount);
        Assert.Equal("Salary", salary.Merchant);

        var refund = One("amazon refund 499");
        Assert.Equal(TransactionType.Refund, refund.Type);
        Assert.Equal("Amazon", refund.Merchant);

        var bill = One("paid amex card bill 20000");
        Assert.Equal(TransactionType.Transfer, bill.Type);
        Assert.Equal("Card Bill", bill.Merchant);
        Assert.Equal(2, bill.AccountId);
    }

    [Fact]
    public void Splits_entries_but_not_thousands()
    {
        var rows = QuickAddParser.Parse("chai 40, auto 120; rent 1,200 cash", Today, Accounts);
        Assert.Equal(["Chai", "Auto", "Rent"], rows.Select(r => r.Merchant));
        Assert.Equal([40m, 120m, 1200m], rows.Select(r => r.Amount!.Value));
        Assert.Equal(3, rows[2].AccountId);
    }

    [Fact]
    public void Missing_amount_is_flagged()
    {
        var r = One("lunch with team");
        Assert.Null(r.Amount);
        Assert.Contains("amount", r.Unsure);
        Assert.Equal("Lunch Team", r.Merchant);
    }

    [Fact]
    public void Month_like_words_stay_in_the_merchant()
    {
        var r = One("decathlon 1500 3 march");
        Assert.Equal("Decathlon", r.Merchant);
        Assert.Equal(new DateOnly(2026, 3, 3), r.Date);
    }
}

public sealed class QuickAddServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ScriptedChat _ai = new();
    private readonly QuickAddService _service;

    public QuickAddServiceTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        CategoryTemplates.SeedAsync(_db).GetAwaiter().GetResult();
        var settings = new SettingsService(_db, new Plain(), new Factory(_ai));
        _service = new QuickAddService(_db, settings, NullLogger<QuickAddService>.Instance);
    }

    private int CategoryId(string name) => _db.Categories.Single(c => c.Name == name).Id;

    [Fact]
    public async Task Categories_come_from_rules_then_names_then_keywords()
    {
        _db.MerchantRules.Add(new MerchantRule { Pattern = "CHAI", Direction = MoneyDirection.Out, CategoryId = CategoryId("Groceries") });
        await _db.SaveChangesAsync();

        var rows = await _service.ParseAsync(new QuickAddParseRequest("chai 40, fuel 500, uber 250, dentist 900", new DateOnly(2026, 11, 14)), default);

        Assert.Equal(CategoryId("Groceries"), rows[0].CategoryId);
        Assert.Equal("rule", rows[0].CategorySource);
        Assert.Equal("CHAI", rows[0].MatchedRule);
        Assert.Equal((CategoryId("Fuel"), "name"), (rows[1].CategoryId!.Value, rows[1].CategorySource));
        Assert.Equal((CategoryId("Transport"), "keyword"), (rows[2].CategoryId!.Value, rows[2].CategorySource));
        Assert.Null(rows[3].CategoryId);
    }

    [Fact]
    public async Task A_rule_type_applies_unless_the_line_says_otherwise()
    {
        _db.MerchantRules.Add(new MerchantRule { Pattern = "CRED", Direction = MoneyDirection.Out, Type = TransactionType.Transfer });
        await _db.SaveChangesAsync();

        var rows = await _service.ParseAsync(new QuickAddParseRequest("cred 5000, cred refund 50"), default);

        Assert.Equal(TransactionType.Transfer, rows[0].Type);
        Assert.Null(rows[0].CategoryId);
        Assert.Equal(TransactionType.Refund, rows[1].Type);
    }

    [Fact]
    public async Task Ai_suggestions_map_to_existing_categories_of_the_right_kind()
    {
        _ai.Json = """{"guesses":[{"number":1,"category":"Health"},{"number":2,"category":"Made up"},{"number":3,"category":"Health"}]}""";

        var result = await _service.SuggestAsync(new QuickAddSuggestRequest(
        [
            new("Dentist", TransactionType.Expense),
            new("Something", TransactionType.Expense),
            new("Side gig", TransactionType.Income),
        ]), default);

        Assert.Null(result.Error);
        Assert.Equal([CategoryId("Health"), null, null], result.CategoryIds);
    }

    [Fact]
    public async Task Ai_failure_is_reported_not_thrown()
    {
        _ai.Error = new HttpRequestException("connection refused");

        var result = await _service.SuggestAsync(new QuickAddSuggestRequest([new("Dentist", TransactionType.Expense)]), default);

        Assert.Equal([null], result.CategoryIds);
        Assert.NotNull(result.Error);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class ScriptedChat : IChatClient
    {
        public string Json { get; set; } = """{"guesses":[]}""";
        public Exception? Error { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Error is not null ? Task.FromException<ChatResponse>(Error) : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Json)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class Factory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(AiConnection connection) => client;
        public Task<IReadOnlyList<string>> ListOllamaModelsAsync(string ollamaUrl, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class Plain : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string? protectedValue) => protectedValue;
    }
}
