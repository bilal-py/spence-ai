using Microsoft.EntityFrameworkCore;
using SpenceAI.Domain;

namespace SpenceAI.Application.Analytics;

public record CategoryTotal(int? CategoryId, string Name, string Color, string Icon, decimal Total, int Count, decimal PreviousTotal, decimal? MonthlyBudget);
public record DailyPoint(DateOnly Date, decimal Spent);
public record MonthlyPoint(string Month, decimal Spent, decimal Income);
public record MerchantTotal(string Name, decimal Total, int Count);
public record LargeTransaction(int Id, DateOnly Date, string Description, decimal Amount, string? Category, string? Color);
public record RecurringPayment(string Merchant, decimal TypicalAmount, int Months, DateOnly LastDate, string? Category);

public record Overview(
    DateOnly From,
    DateOnly To,
    decimal Spent,
    decimal Income,
    decimal Net,
    int Count,
    decimal PreviousSpent,
    decimal PreviousIncome,
    decimal DailyAverage,
    IReadOnlyList<CategoryTotal> ByCategory,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<MonthlyPoint> Monthly,
    IReadOnlyList<MerchantTotal> TopMerchants,
    IReadOnlyList<LargeTransaction> Largest,
    IReadOnlyList<RecurringPayment> Recurring);

/// <summary>
/// All dashboard numbers. Single-user local data is small, so we load the window once and aggregate in memory
/// (this also sidesteps SQLite's lack of native decimal aggregation).
/// </summary>
public class AnalyticsService(IAppDbContext db)
{
    public async Task<Overview> GetOverviewAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) (from, to) = (to, from);

        var days = to.DayNumber - from.DayNumber + 1;
        var prevFrom = from.AddDays(-days);
        var prevTo = from.AddDays(-1);
        var trendFrom = new DateOnly(to.Year, to.Month, 1).AddMonths(-11);
        var loadFrom = prevFrom < trendFrom ? prevFrom : trendFrom;

        var all = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Date >= loadFrom && t.Date <= to)
            .ToListAsync(ct);

        var current = all.Where(t => t.Date >= from && t.Date <= to).ToList();
        var previous = all.Where(t => t.Date >= prevFrom && t.Date <= prevTo).ToList();
        var expenses = current.Where(t => t.Type == TransactionType.Expense).ToList();
        var prevExpenses = previous.Where(t => t.Type == TransactionType.Expense).ToList();

        var spent = expenses.Sum(t => t.Amount);
        var income = current.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);

        var prevByCategory = prevExpenses.GroupBy(t => t.CategoryId).ToDictionary(g => g.Key ?? 0, g => g.Sum(t => t.Amount));
        var byCategory = expenses
            .GroupBy(t => t.CategoryId)
            .Select(g =>
            {
                var c = g.First().Category;
                return new CategoryTotal(g.Key, c?.Name ?? "Uncategorized", c?.Color ?? "#9a9a92", c?.Icon ?? "circle-help",
                    g.Sum(t => t.Amount), g.Count(), prevByCategory.GetValueOrDefault(g.Key ?? 0), c?.MonthlyBudget);
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        var dailyLookup = expenses.GroupBy(t => t.Date).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var daily = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new DailyPoint(d, dailyLookup.GetValueOrDefault(d)))
            .ToList();

        var monthly = Enumerable.Range(0, 12)
            .Select(i => trendFrom.AddMonths(i))
            .Select(m =>
            {
                var inMonth = all.Where(t => t.Date.Year == m.Year && t.Date.Month == m.Month).ToList();
                return new MonthlyPoint($"{m:yyyy-MM}",
                    inMonth.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                    inMonth.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount));
            })
            .ToList();

        var topMerchants = expenses
            .GroupBy(t => MerchantKey.From(t.Merchant ?? t.Description))
            .Where(g => g.Key.Length > 0)
            .Select(g => new MerchantTotal(g.First().Merchant ?? ToTitle(g.Key), g.Sum(t => t.Amount), g.Count()))
            .OrderByDescending(m => m.Total)
            .Take(8)
            .ToList();

        var largest = expenses
            .OrderByDescending(t => t.Amount)
            .Take(5)
            .Select(t => new LargeTransaction(t.Id, t.Date, t.Merchant ?? t.Description, t.Amount, t.Category?.Name, t.Category?.Color))
            .ToList();

        return new Overview(from, to, spent, income, income - spent, current.Count,
            prevExpenses.Sum(t => t.Amount),
            previous.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
            days > 0 ? decimal.Round(spent / days, 2) : 0,
            byCategory, daily, monthly, topMerchants, largest,
            FindRecurring(all.Where(t => t.Type == TransactionType.Expense), to));
    }

    /// <summary>
    /// A subscription / recurring bill: the same merchant charged about once a month, in at least 3 months,
    /// with nearly the same amount each time (most charges within 10% of the median), and seen in the last ~45 days.
    /// Frequent shops (groceries, rides, cafés) fail the "about once a month" and "nearly the same amount" tests.
    /// </summary>
    public static List<RecurringPayment> FindRecurring(IEnumerable<Transaction> expenses, DateOnly asOf)
    {
        var result = new List<RecurringPayment>();

        foreach (var group in expenses.GroupBy(t => MerchantKey.From(t.Merchant ?? t.Description)).Where(g => g.Key.Length > 0))
        {
            var amounts = group.Select(t => t.Amount).Order().ToList();
            var median = amounts[amounts.Count / 2];
            var similar = group.Where(t => median > 0 && Math.Abs(t.Amount - median) / median <= 0.10m).ToList();
            var months = similar.Select(t => (t.Date.Year, t.Date.Month)).Distinct().Count();
            var last = similar.Count > 0 ? similar.Max(t => t.Date) : default;

            var mostlySameAmount = similar.Count >= group.Count() * 0.75;
            var aboutMonthly = similar.Count <= months + 1;

            if (months >= 3 && mostlySameAmount && aboutMonthly && last >= asOf.AddDays(-45))
            {
                var sample = similar.OrderByDescending(t => t.Date).First();
                result.Add(new RecurringPayment(sample.Merchant ?? ToTitle(group.Key), median, months, last, sample.Category?.Name));
            }
        }

        return result.OrderByDescending(r => r.TypicalAmount).ToList();
    }

    private static string ToTitle(string key) =>
        string.Join(' ', key.Split(' ').Select(w => w.Length > 1 ? w[0] + w[1..].ToLowerInvariant() : w));
}
