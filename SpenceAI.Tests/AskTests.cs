using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SpenceAI.Application;
using SpenceAI.Application.Ask;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Categories;
using SpenceAI.Application.Savings;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

public sealed class AskTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ToolCallingChat _ai = new();
    private readonly AskService _ask;

    public AskTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        CategoryTemplates.SeedAsync(_db).GetAwaiter().GetResult();
        _db.Settings.Add(new AppSettings());
        _db.SaveChanges();
        var settings = new SettingsService(_db, new Plain(), new Factory(_ai));
        _ask = new AskService(_db, settings, new BillService(_db), new SavingsService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task The_model_answers_from_a_tool_call_and_the_lookup_is_reported()
    {
        var dining = _db.Categories.Single(c => c.Name == "Dining").Id;
        _db.Transactions.AddRange(
            new Transaction { Date = new DateOnly(2026, 11, 2), Amount = 400, Type = TransactionType.Expense, Description = "Swiggy", CategoryId = dining },
            new Transaction { Date = new DateOnly(2026, 11, 9), Amount = 250, Type = TransactionType.Expense, Description = "Zomato", CategoryId = dining },
            new Transaction { Date = new DateOnly(2026, 11, 10), Amount = 50, Type = TransactionType.Refund, Description = "Zomato refund", CategoryId = dining },
            new Transaction { Date = new DateOnly(2026, 10, 9), Amount = 999, Type = TransactionType.Expense, Description = "Old dinner", CategoryId = dining });
        _db.SaveChanges();

        var reply = await _ask.AskAsync(new AskRequest([new AskMessage("user", "How much on dining this month?")], Today), default);

        Assert.Equal(2, _ai.Calls);
        Assert.Contains("\"spent\":600", _ai.ToolResult); // 400 + 250 - 50; October isn't in range
        Assert.Equal("You spent 600 on dining.", reply.Reply);
        Assert.Equal("Totals, 1 Nov 2026 to 14 Nov 2026 in Dining", Assert.Single(reply.Lookups));
    }

    [Fact]
    public async Task A_question_is_required()
    {
        await Assert.ThrowsAsync<AppException>(() => _ask.AskAsync(new AskRequest([], Today), default));
        await Assert.ThrowsAsync<AppException>(() => _ask.AskAsync(new AskRequest([new AskMessage("assistant", "Hi")], Today), default));
    }

    /// <summary>First call asks for spending_summary; second call (with the tool result) answers.</summary>
    private sealed class ToolCallingChat : IChatClient
    {
        public int Calls { get; private set; }
        public string ToolResult { get; private set; } = "";

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var list = messages.ToList();
            var result = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
            if (result is null)
            {
                Assert.Contains(options!.Tools!, t => t.Name == "spending_summary");
                var call = new FunctionCallContent("call-1", "spending_summary", new Dictionary<string, object?>
                {
                    ["from"] = "2026-11-01", ["to"] = "2026-11-14", ["category"] = "Dining", ["merchant"] = null,
                });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
            }

            ToolResult = result.Result?.ToString() ?? "";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "You spent 600 on dining.")));
        }

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
