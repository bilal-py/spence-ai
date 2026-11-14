using Microsoft.EntityFrameworkCore;
using SpenceAI.Domain;

namespace SpenceAI.Application.Analytics;

public record WrappedSlice(string Name, string Color, string Icon, decimal Total, double Share);
public record WrappedMerchant(string Name, decimal Total, int Visits);
public record WrappedDay(DateOnly Date, decimal Total, int Count);
public record WrappedPurchase(DateOnly Date, string Description, decimal Amount, string? Category);

/// <summary>
/// One month's recap. Only computed facts: no AI, and no time-of-day cards (transactions store dates, not times).
/// </summary>
public record Wrapped(
    string Month,
    DateOnly From,
    DateOnly To,
    int Count,
    decimal Spent,
    decimal PreviousSpent,
    decimal Income,
    /// <summary>(income − spending) / income, when there was income.</summary>
    double? SavingsRate,
    IReadOnlyList<WrappedSlice> Categories,
    WrappedMerchant? TopMerchant,
    WrappedMerchant? Regular,
    WrappedDay? BiggestDay,
    WrappedPurchase? Largest,
    int NoSpendDays,
    int LongestNoSpendStreak,
    int DaysCovered,
    string? BusiestWeekday,
    double BusiestWeekdayShare,
    int NewMerchants,
    IReadOnlyList<string> NewMerchantNames,
    int BudgetsKept,
    int BudgetsTotal,
    decimal SavedToPots);

public class WrappedService(IAppDbContext db)
{
    public async Task<Wrapped> GetAsync(int year, int month, DateOnly today, CancellationToken ct)
    {
        if (month is < 1 or > 12 || year is < 1900 or > 9999) throw new AppException("Pick a month like 2026-10.");
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var prevFrom = from.AddMonths(-1);
        var historyFrom = from.AddMonths(-12);

        var all = await db.Transactions.AsNoTracking().Include(t => t.Category)
            .Where(t => t.Date >= historyFrom && t.Date <= to)
            .ToListAsync(ct);
        var current = all.Where(t => t.Date >= from).ToList();
        var spending = current.Where(t => t.Type is TransactionType.Expense or TransactionType.Refund).ToList();
        var expenses = current.Where(t => t.Type == TransactionType.Expense).ToList();

        var spent = AnalyticsService.NetSpent(spending);
        var previousSpent = AnalyticsService.NetSpent(all.Where(t => t.Date >= prevFrom && t.Date < from && t.Type is TransactionType.Expense or TransactionType.Refund));
        var income = current.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);

        var categories = spending.GroupBy(t => t.CategoryId)
            .Select(g =>
            {
                var c = g.First().Category;
                var total = AnalyticsService.NetSpent(g);
                return new WrappedSlice(c?.Name ?? "Uncategorized", c?.Color ?? "#9a9a92", c?.Icon ?? "circle-help", total, spent > 0 ? (double)(total / spent) : 0);
            })
            .Where(s => s.Total > 0)
            .OrderByDescending(s => s.Total)
            .Take(5)
            .ToList();

        var merchants = expenses
            .GroupBy(t => MerchantKey.From(t.Merchant ?? t.Description))
            .Where(g => g.Key.Length > 0)
            .Select(g => (Key: g.Key, M: new WrappedMerchant(g.First().Merchant ?? g.Key, g.Sum(t => t.Amount), g.Count())))
            .ToList();
        var topMerchant = merchants.OrderByDescending(m => m.M.Total).Select(m => m.M).FirstOrDefault();
        var regular = merchants.Where(m => m.M.Visits >= 2).OrderByDescending(m => m.M.Visits).ThenByDescending(m => m.M.Total).Select(m => m.M).FirstOrDefault();

        var biggestDay = expenses.GroupBy(t => t.Date)
            .Select(g => new WrappedDay(g.Key, g.Sum(t => t.Amount), g.Count()))
            .OrderByDescending(d => d.Total)
            .FirstOrDefault();
        var largest = expenses.OrderByDescending(t => t.Amount)
            .Select(t => new WrappedPurchase(t.Date, t.Merchant ?? t.Description, t.Amount, t.Category?.Name))
            .FirstOrDefault();

        // No-spend days only count as far as the data goes: a week not imported yet isn't a week of saving.
        var lastData = current.Count > 0 ? current.Max(t => t.Date) : (DateOnly?)null;
        var coveredTo = lastData is { } l ? Min(Min(to, today), l) : from.AddDays(-1);
        var spendDays = expenses.Select(t => t.Date).ToHashSet();
        int noSpend = 0, streak = 0, longest = 0;
        for (var d = from; d <= coveredTo; d = d.AddDays(1))
        {
            if (spendDays.Contains(d)) { streak = 0; continue; }
            noSpend++;
            longest = Math.Max(longest, ++streak);
        }
        var daysCovered = Math.Max(0, coveredTo.DayNumber - from.DayNumber + 1);

        var byWeekday = spending.GroupBy(t => t.Date.DayOfWeek).Select(g => (Day: g.Key, Total: AnalyticsService.NetSpent(g))).OrderByDescending(x => x.Total).FirstOrDefault();
        string? busiest = spent > 0 && byWeekday.Total > 0 ? byWeekday.Day.ToString() : null;

        var seenBefore = all.Where(t => t.Date < from).Select(t => MerchantKey.From(t.Merchant ?? t.Description)).ToHashSet();
        var hasHistory = all.Any(t => t.Date < from);
        var newMerchants = hasHistory ? merchants.Where(m => !seenBefore.Contains(m.Key)).OrderByDescending(m => m.M.Total).Select(m => m.M.Name).ToList() : [];

        var budgeted = await db.Categories.AsNoTracking().Where(c => c.MonthlyBudget != null && c.Kind == TransactionType.Expense).ToListAsync(ct);
        var kept = budgeted.Count(c => AnalyticsService.NetSpent(spending.Where(t => t.CategoryId == c.Id)) <= c.MonthlyBudget!.Value);

        var potContributions = await db.PotContributions.AsNoTracking().Where(c => c.Date >= from && c.Date <= to).Select(c => c.Amount).ToListAsync(ct);
        var linkPatterns = await db.SavingsPots.AsNoTracking().Where(p => p.LinkPattern != null).Select(p => new { p.LinkPattern, p.StartDate }).ToListAsync(ct);
        var linked = current.Where(t => t.Type == TransactionType.Transfer && linkPatterns.Any(p => t.Date >= p.StartDate &&
                ((t.Merchant?.Contains(p.LinkPattern!, StringComparison.OrdinalIgnoreCase) ?? false) || t.Description.Contains(p.LinkPattern!, StringComparison.OrdinalIgnoreCase))))
            .Sum(t => t.Amount);

        return new Wrapped($"{from:yyyy-MM}", from, to, current.Count, spent, previousSpent, income,
            income > 0 ? (double)((income - spent) / income) : null,
            categories, topMerchant, regular, biggestDay, largest,
            noSpend, longest, daysCovered, busiest,
            spent > 0 && busiest is not null ? (double)(byWeekday.Total / spent) : 0,
            newMerchants.Count, newMerchants.Take(3).ToList(),
            kept, budgeted.Count, potContributions.Sum() + linked);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
