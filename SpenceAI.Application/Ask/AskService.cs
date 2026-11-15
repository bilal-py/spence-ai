using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Savings;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;

namespace SpenceAI.Application.Ask;

/// <param name="Role">"user" | "assistant".</param>
public record AskMessage(string Role, string Text);
public record AskRequest(IReadOnlyList<AskMessage> Messages, DateOnly? Today);
/// <param name="Lookups">What Spence looked up to answer, in plain words, so the answer can be trusted.</param>
public record AskResponse(string Reply, IReadOnlyList<string> Lookups);

/// <summary>
/// "Ask your expenses": the model answers by calling a few read-only query functions over the local data (totals, lists of at most
/// 25 rows, bills, pots). It can't write anything, and it only sees what a question needs.
/// </summary>
public class AskService(IAppDbContext db, SettingsService settings, BillService bills, SavingsService savings)
{
    public const int MaxMessages = 12;
    public const int MaxRows = 25;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AskResponse> AskAsync(AskRequest req, CancellationToken ct)
    {
        var today = req.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var history = (req.Messages ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Text)).TakeLast(MaxMessages).ToList();
        if (history.Count == 0 || history[^1].Role != "user") throw new AppException("Ask a question first.");
        if (history.Any(m => m.Text.Length > 2000)) throw new AppException("Keep each message under 2,000 characters.");

        var currency = (await settings.GetEntityAsync(ct)).Currency;
        var connection = await settings.GetConnectionAsync(ct);
        var inner = await settings.CreateChatClientAsync(ct);
        var client = new FunctionInvokingChatClient(inner) { MaximumIterationsPerRequest = 6 };

        var lookups = new List<string>();
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var tools = Tools(today, categories, lookups);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $"""
                You are Spence, a friendly, sharp money buddy inside a personal expense tracker. Today is {today:yyyy-MM-dd} ({today:dddd}).
                Money is in {currency}. Answer questions about the user's own spending, income, bills and savings pots.
                Always call a tool to get numbers; never guess or invent figures. Work out date ranges yourself
                ("last month", "since June", "this year") and pass them as yyyy-MM-dd. Spending means expenses minus refunds;
                transfers (card bill payments, moving money to savings) are not spending.
                Category names: {string.Join(", ", categories.Select(c => c.Name))}.
                Reply in 1 to 4 short sentences or a tight list, with concrete numbers formatted as money. Casual and warm, no lectures.
                If the data can't answer the question, say so plainly.
                """),
        };
        messages.AddRange(history.Select(m => new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Text)));

        try
        {
            var response = await client.GetResponseAsync(messages, new ChatOptions { Tools = tools, Temperature = 0.2f }, ct);
            var text = response.Text.Trim();
            if (text.Length == 0) throw new AppException("The AI model didn't answer. Try asking another way, or use a larger model.");
            return new AskResponse(text, lookups.Distinct().ToList());
        }
        catch (Exception ex) when (ex is not AppException && !ct.IsCancellationRequested)
        {
            throw new AppException(SettingsService.DescribeAiFailure(connection, ex));
        }
    }

    private List<AITool> Tools(DateOnly today, List<Category> categories, List<string> lookups) =>
    [
        AIFunctionFactory.Create(
            async ([Description("Start date, yyyy-MM-dd")] string from,
                   [Description("End date, yyyy-MM-dd (inclusive)")] string to,
                   [Description("Optional category name to filter by")] string? category,
                   [Description("Optional text the merchant or description must contain, e.g. 'swiggy'")] string? merchant) =>
            {
                var (f, t) = Range(from, to, today);
                lookups.Add(Describe("Totals", f, t, category, merchant));
                var rows = await Load(f, t, category, merchant, categories);
                var spending = rows.Where(x => x.Type is TransactionType.Expense or TransactionType.Refund).ToList();
                var months = t.DayNumber - f.DayNumber > 40;
                return Serialize(new
                {
                    from = f, to = t, filters = new { category, merchant },
                    spent = AnalyticsService.NetSpent(spending),
                    purchases = rows.Count(x => x.Type == TransactionType.Expense),
                    refunds = rows.Where(x => x.Type == TransactionType.Refund).Sum(x => x.Amount),
                    income = rows.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount),
                    byMonth = months
                        ? spending.GroupBy(x => $"{x.Date:yyyy-MM}").OrderBy(g => g.Key).Select(g => new { month = g.Key, spent = AnalyticsService.NetSpent(g) })
                        : null,
                    byCategory = category is null
                        ? spending.GroupBy(x => x.Category?.Name ?? "Uncategorized").Select(g => new { category = g.Key, spent = AnalyticsService.NetSpent(g), count = g.Count() })
                            .Where(x => x.spent != 0).OrderByDescending(x => x.spent).Take(10)
                        : null,
                    topMerchants = spending.GroupBy(x => x.Merchant ?? MerchantKey.From(x.Description)).Select(g => new { merchant = g.Key, spent = AnalyticsService.NetSpent(g), count = g.Count() })
                        .Where(x => x.spent > 0).OrderByDescending(x => x.spent).Take(6),
                });
            },
            "spending_summary",
            "Totals for a date range: net spending, number of purchases, refunds, income, plus a split by category, by month and top merchants. Filter by category and/or merchant text."),

        AIFunctionFactory.Create(
            async ([Description("Start date, yyyy-MM-dd")] string from,
                   [Description("End date, yyyy-MM-dd (inclusive)")] string to,
                   [Description("Optional category name")] string? category,
                   [Description("Optional text the merchant or description must contain")] string? search,
                   [Description("Optional minimum amount")] decimal? minAmount,
                   [Description("'largest' or 'recent'")] string? sort,
                   [Description("How many rows, at most 25")] int? limit) =>
            {
                var (f, t) = Range(from, to, today);
                lookups.Add(Describe(sort == "largest" ? "Largest transactions" : "Transactions", f, t, category, search));
                var rows = (await Load(f, t, category, search, categories)).Where(x => minAmount is null || x.Amount >= minAmount);
                rows = sort == "largest" ? rows.OrderByDescending(x => x.Amount) : rows.OrderByDescending(x => x.Date);
                var take = Math.Clamp(limit ?? 10, 1, MaxRows);
                var list = rows.ToList();
                return Serialize(new
                {
                    total = list.Count,
                    shown = list.Take(take).Select(x => new
                    {
                        date = x.Date, description = x.Merchant ?? x.Description, amount = x.Amount, type = x.Type.ToString(), category = x.Category?.Name,
                    }),
                });
            },
            "list_transactions",
            "Individual transactions in a date range (at most 25), newest or largest first, optionally filtered by category, text and minimum amount."),

        AIFunctionFactory.Create(
            () =>
            {
                lookups.Add("Categories and budgets");
                return Serialize(categories.Select(c => new { c.Name, kind = c.Kind.ToString(), monthlyBudget = c.MonthlyBudget }));
            },
            "list_categories",
            "All categories with their kind (Expense/Income) and monthly budget, if any."),

        AIFunctionFactory.Create(
            async () =>
            {
                lookups.Add("Bills and safe-to-spend");
                var o = await bills.GetOverviewAsync(today, CancellationToken.None);
                return Serialize(new
                {
                    monthlyTotal = o.MonthlyTotal,
                    subscriptionsPerYear = o.SubscriptionsYearly,
                    thisMonth = o.ThisMonth,
                    safeToSpend = o.SafeToSpend,
                    bills = o.Bills.Select(b => new
                    {
                        b.Name, b.Amount, cadence = b.Interval == 1 ? b.Cadence.ToString() : $"every {b.Interval} {b.Cadence}", b.IsSubscription,
                        b.Autopay, cancelled = b.CancelledOn is not null, paused = b.IsPaused, next = b.Next is null ? null : new { b.Next.DueDate, b.Next.Status },
                        b.YearlyCost, priceChange = b.PriceChange,
                    }),
                });
            },
            "bills_overview",
            "The user's bills and subscriptions: amounts, next due dates and status, monthly total, yearly subscription cost and how much is safe to spend this month."),

        AIFunctionFactory.Create(
            async () =>
            {
                lookups.Add("Savings pots");
                var pots = await savings.ListAsync(today, CancellationToken.None);
                return Serialize(pots.Where(p => !p.IsArchived).Select(p => new
                {
                    p.Name, p.Saved, p.TargetAmount, p.Deadline, pace = p.Pace is null ? null : new { p.Pace.Status, p.Pace.PerMonth, p.Pace.DaysLeft },
                }));
            },
            "savings_pots",
            "Savings goals: saved so far, target, deadline and whether they're on pace."),
    ];

    private async Task<List<Transaction>> Load(DateOnly from, DateOnly to, string? category, string? text, List<Category> categories)
    {
        var query = db.Transactions.AsNoTracking().Include(t => t.Category).Where(t => t.Date >= from && t.Date <= to);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var ids = categories.Where(c => c.Name.Contains(category.Trim(), StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToList();
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }
        var rows = await query.ToListAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            rows = rows.Where(t => (t.Merchant ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                                   t.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    private static (DateOnly From, DateOnly To) Range(string from, string to, DateOnly today)
    {
        var f = Parse(from) ?? today.AddDays(-30);
        var t = Parse(to) ?? today;
        if (t < f) (f, t) = (t, f);
        if (t.DayNumber - f.DayNumber > 3660) f = t.AddYears(-10);
        return (f, t);

        static DateOnly? Parse(string? s) =>
            DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string Describe(string what, DateOnly from, DateOnly to, string? category, string? text)
    {
        var parts = new List<string> { $"{what}, {from:d MMM yyyy} to {to:d MMM yyyy}" };
        if (!string.IsNullOrWhiteSpace(category)) parts.Add($"in {category.Trim()}");
        if (!string.IsNullOrWhiteSpace(text)) parts.Add($"matching “{text.Trim()}”");
        return string.Join(' ', parts);
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
