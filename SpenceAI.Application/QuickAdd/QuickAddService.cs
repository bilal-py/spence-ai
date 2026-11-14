using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;

namespace SpenceAI.Application.QuickAdd;

public record QuickAddParseRequest(string Text, DateOnly? Today = null);

/// <summary>How the category was chosen: "rule" (one you taught), "name" (the line names a category), "keyword" (a common merchant), or null.</summary>
public record QuickAddRow(
    string Text,
    DateOnly Date,
    decimal? Amount,
    TransactionType Type,
    string Merchant,
    int? CategoryId,
    string? CategorySource,
    int? AccountId,
    string? MatchedRule,
    IReadOnlyList<string> Unsure);

public record QuickAddSuggestItem(string Merchant, TransactionType Type);
public record QuickAddSuggestRequest(IReadOnlyList<QuickAddSuggestItem> Items);

/// <summary>One category id (or null) per requested item, in order. <paramref name="Error"/> says why the AI couldn't help.</summary>
public record QuickAddSuggestion(IReadOnlyList<int?> CategoryIds, string? Error);

/// <summary>
/// Quick add: plain rules first (amount, date, type, account, learned merchant rules, category names, common merchants),
/// and the AI only for the categories still unknown, with nothing but the merchant names as input.
/// </summary>
public class QuickAddService(IAppDbContext db, SettingsService settings, ILogger<QuickAddService> logger)
{
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Common merchants and words → the starter category they usually belong to. Used only if that category exists.</summary>
    private static readonly Dictionary<string, string> Keywords = BuildKeywords(new()
    {
        ["Dining"] = "chai tea coffee lunch dinner breakfast snacks snack swiggy zomato restaurant cafe pizza burger samosa starbucks dominos kfc mcdonalds biryani juice",
        ["Groceries"] = "groceries grocery vegetables veggies sabzi fruits milk eggs bread bigbasket blinkit zepto dmart instamart kirana",
        ["Transport"] = "uber ola auto rickshaw cab taxi metro bus rapido parking toll",
        ["Fuel"] = "petrol diesel fuel cng",
        ["Shopping"] = "amazon flipkart myntra ajio meesho clothes shoes nykaa",
        ["Bills & utilities"] = "electricity recharge wifi internet broadband dth airtel jio",
        ["Housing"] = "rent maintenance",
        ["Health"] = "medicine medicines pharmacy doctor hospital apollo pharmeasy gym",
        ["Entertainment"] = "movie movies cinema pvr inox concert bookmyshow",
        ["Subscriptions"] = "netflix spotify hotstar youtube icloud",
        ["Travel"] = "flight flights hotel airbnb makemytrip goibibo irctc",
        ["Education"] = "books book course tuition udemy",
        ["Personal care"] = "haircut salon spa barber",
        ["Gifts & donations"] = "gift gifts donation charity",
        ["Salary"] = "salary paycheck",
    });

    public async Task<IReadOnlyList<QuickAddRow>> ParseAsync(QuickAddParseRequest request, CancellationToken ct)
    {
        if (request.Text is { Length: > 2000 }) throw new AppException("That's a lot for quick add. Try fewer than 20 entries at once.");

        var today = request.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var rules = await db.MerchantRules.AsNoTracking().ToListAsync(ct);
        var byName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var rows = new List<QuickAddRow>();
        foreach (var line in QuickAddParser.Parse(request.Text ?? "", today, accounts))
        {
            var type = line.Type;
            int? categoryId = null;
            string? source = null;

            var rule = line.Merchant.Length > 0 ? RuleBook.Match(rules, line.Merchant, line.Merchant, RuleBook.DirectionOf(type)) : null;
            if (rule is not null)
            {
                if (!line.TypeStated && rule.Type is { } ruleType) type = ruleType;
                if (rule.CategoryId is { } id)
                {
                    categoryId = id;
                    source = "rule";
                }
            }

            if (categoryId is null && FromWords(line.Merchant, type, categories, byName) is { } guessed)
            {
                (categoryId, source) = guessed;
            }

            if (type == TransactionType.Transfer)
            {
                categoryId = null;
                source = null;
            }

            rows.Add(new QuickAddRow(line.Text, line.Date, line.Amount, type, line.Merchant, categoryId, source, line.AccountId, rule?.Pattern, line.Unsure));
        }

        return rows;
    }

    /// <summary>
    /// Asks the AI for categories of merchants the rules couldn't place. Never throws for AI problems: the confirm card
    /// still works without it, so the reason is returned for display instead.
    /// </summary>
    public async Task<QuickAddSuggestion> SuggestAsync(QuickAddSuggestRequest request, CancellationToken ct)
    {
        var items = request.Items.Take(QuickAddParser.MaxRows).ToList();
        if (items.Count == 0) return new QuickAddSuggestion([], null);

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var byName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        AiConnection? connection = null;
        try
        {
            connection = await settings.GetConnectionAsync(ct);
            var client = await settings.CreateChatClientAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AiTimeout);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, Prompt(categories)),
                new(ChatRole.User, string.Join("\n", items.Select((it, i) => $"{i + 1}. {it.Merchant} ({Describe(it.Type)})"))),
            };
            var response = await client.GetResponseAsync<CategoryGuesses>(messages, new ChatOptions { Temperature = 0 }, cancellationToken: timeout.Token);
            if (!response.TryGetResult(out var result)) return new QuickAddSuggestion(items.Select(_ => (int?)null).ToList(), "The AI didn't return categories.");

            var ids = items.Select((it, i) =>
            {
                var name = result.Guesses.FirstOrDefault(g => g.Number == i + 1)?.Category ?? (i < result.Guesses.Count ? result.Guesses[i].Category : null);
                if (name is null || !byName.TryGetValue(name.Trim(), out var c)) return (int?)null;
                var wantIncome = it.Type == TransactionType.Income;
                return (c.Kind == TransactionType.Income) == wantIncome ? c.Id : null;
            }).ToList();
            return new QuickAddSuggestion(ids, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Quick add category suggestion failed");
            var reason = ex is AppException ? ex.Message
                : ex is OperationCanceledException ? "The AI took too long, so pick a category yourself."
                : connection is null ? ex.Message
                : SettingsService.DescribeAiFailure(connection, ex);
            return new QuickAddSuggestion(items.Select(_ => (int?)null).ToList(), reason);
        }
    }

    // ---------- Rules without AI ----------

    private static (int Id, string Source)? FromWords(string merchant, TransactionType type, List<Category> categories, Dictionary<string, Category> byName)
    {
        if (merchant.Length == 0) return null;
        var wantIncome = type == TransactionType.Income;
        bool Fits(Category c) => (c.Kind == TransactionType.Income) == wantIncome;

        // The line names a category: "groceries 800", "fuel 500".
        if (byName.TryGetValue(merchant, out var exact) && Fits(exact)) return (exact.Id, "name");
        var words = merchant.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var named = categories.FirstOrDefault(c => Fits(c) && !c.Name.Contains(' ') && words.Contains(c.Name.ToLowerInvariant()));
        if (named is not null) return (named.Id, "name");

        foreach (var w in words)
        {
            if (Keywords.TryGetValue(w, out var categoryName) && byName.TryGetValue(categoryName, out var c) && Fits(c)) return (c.Id, "keyword");
        }

        return null;
    }

    private static Dictionary<string, string> BuildKeywords(Dictionary<string, string> byCategory)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, words) in byCategory)
        {
            foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map.TryAdd(w, category);
        }

        return map;
    }

    // ---------- AI ----------

    private static string Describe(TransactionType type) => type switch
    {
        TransactionType.Income => "money received",
        TransactionType.Refund => "refund",
        _ => "spending",
    };

    private static string Prompt(List<Category> categories)
    {
        var spending = string.Join(", ", categories.Where(c => c.Kind != TransactionType.Income).Select(c => c.Name));
        var income = string.Join(", ", categories.Where(c => c.Kind == TransactionType.Income).Select(c => c.Name));
        return $"""
            You categorize short personal expense notes typed by the user, like "Chai" or "Uber" or "Dentist".
            For each numbered item, pick the best category, spelled exactly as listed:
            - spending and refund: one of {spending}
            - money received: one of {income}
            If none fits well, use null. Return one guess per item, with the item's number.
            """;
    }

    public class CategoryGuesses
    {
        public required List<CategoryGuess> Guesses { get; set; }
    }

    public class CategoryGuess
    {
        [Description("The item's number")]
        public required int Number { get; set; }

        [Description("One of the listed category names, or null")]
        public required string? Category { get; set; }
    }
}
