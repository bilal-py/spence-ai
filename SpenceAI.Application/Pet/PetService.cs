using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Savings;
using SpenceAI.Domain;

namespace SpenceAI.Application.Pet;

/// <summary>
/// One nudge. <see cref="Text"/> may contain "{amount}", which the client fills in with <see cref="Amount"/> in the user's currency.
/// </summary>
public record Nudge(string Key, string Text, decimal? Amount, string? Link);

/// <summary>Mood: "sleepy" | "cheerful" | "content" | "watchful" | "proud".</summary>
public record PetState(bool Enabled, string Name, string Mood, string MoodLine, Nudge? Nudge);

public record PetSettingsRequest(bool Enabled, string Name);
public record PetDayRequest(DateOnly? Today);

/// <summary>
/// Sprig, the mascot. Its mood follows how the month is pacing against your budgets (or your usual month), and it offers at most
/// one short nudge a day. Plain rules, so it works without AI. It points at things you can do; it never scolds.
/// </summary>
public class PetService(IAppDbContext db, SavingsService savings)
{
    public async Task<PetState> GetAsync(DateOnly today, CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
        if (!settings.PetEnabled) return new PetState(false, settings.PetName, "content", "", null);

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var history = await db.Transactions.AsNoTracking().Include(t => t.Category)
            .Where(t => t.Date >= monthStart.AddMonths(-13) && t.Date <= today)
            .ToListAsync(ct);
        var pots = await savings.ListAsync(today, ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);

        var (mood, line) = Mood(settings.PetName, history, categories, pots, today);
        var nudge = settings.PetNudgeDismissedOn == today ? null : PickNudge(history, categories, pots, today);
        return new PetState(true, settings.PetName, mood, line, nudge);
    }

    public async Task DismissAsync(DateOnly today, CancellationToken ct)
    {
        var settings = await db.Settings.FirstOrDefaultAsync(ct) ?? throw new AppException("Settings are missing.", 500);
        settings.PetNudgeDismissedOn = today;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PetState> UpdateSettingsAsync(PetSettingsRequest req, DateOnly today, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 30) throw new AppException("Give your pet a name of 1 to 30 characters.");
        var settings = await db.Settings.FirstOrDefaultAsync(ct) ?? throw new AppException("Settings are missing.", 500);
        settings.PetEnabled = req.Enabled;
        settings.PetName = name;
        await db.SaveChangesAsync(ct);
        return await GetAsync(today, ct);
    }

    public static (string Mood, string Line) Mood(string name, List<Transaction> history, List<Category> categories, List<PotView> pots, DateOnly today)
    {
        if (!history.Any(t => t.Date > today.AddDays(-30)))
        {
            return ("sleepy", $"{name} is napping. Add or import a few transactions and it'll wake up.");
        }

        var done = pots.FirstOrDefault(p => !p.IsArchived && p.Saved >= p.TargetAmount && p.TargetAmount > 0);
        if (done is not null) return ("proud", $"{done.Name} reached its goal. {name} is doing a little dance.");
        if (pots.Any(p => p.Challenges.Any(c => c.Status == "won")))
        {
            return ("proud", $"You won a challenge. {name} is very proud.");
        }

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysLeft = daysInMonth - today.Day;
        var spent = AnalyticsService.NetSpent(history.Where(t => t.Date >= monthStart));
        var baseline = Baseline(history, categories, monthStart);
        if (baseline is null or <= 0) return ("content", $"{name} is getting to know your month.");

        var expected = baseline.Value * today.Day / daysInMonth;
        var ratio = expected > 0 ? spent / expected : 1;
        return ratio switch
        {
            < 0.85m => ("cheerful", "Spending is running lighter than usual this month."),
            <= 1.1m => ("content", "This month is right on its usual pace."),
            _ when daysLeft >= 7 => ("watchful", "This month is moving a little quicker than usual. Plenty of month left to even it out."),
            _ => ("watchful", "A busier month than usual. Next month is a fresh start."),
        };
    }

    /// <summary>The month's yardstick: the total of your budgets, or else your average over the last three months with data.</summary>
    public static decimal? Baseline(List<Transaction> history, List<Category> categories, DateOnly monthStart)
    {
        var budgets = categories.Where(c => c.Kind == TransactionType.Expense && c.MonthlyBudget is not null).Sum(c => c.MonthlyBudget!.Value);
        if (budgets > 0) return budgets;

        var months = Enumerable.Range(1, 3).Select(i => monthStart.AddMonths(-i))
            .Select(m => history.Where(t => t.Date >= m && t.Date < m.AddMonths(1)).ToList())
            .Where(list => list.Count > 0)
            .Select(AnalyticsService.NetSpent)
            .ToList();
        return months.Count > 0 ? months.Average() : null;
    }

    /// <summary>The most useful single nudge for today, or null when there's nothing worth saying.</summary>
    public static Nudge? PickNudge(List<Transaction> history, List<Category> categories, List<PotView> pots, DateOnly today)
    {
        var active = pots.Where(p => !p.IsArchived).ToList();

        foreach (var pot in active)
        {
            var won = pot.Challenges.FirstOrDefault(c => c.Status == "won");
            if (won is not null) return new Nudge("challenge-won", $"You won “{won.Title}”. Move {{amount}} into {pot.Name}?", won.Reward, "/pots");
        }

        foreach (var pot in active)
        {
            var ready = pot.Available.OrderByDescending(a => a.Amount).FirstOrDefault();
            if (ready is not null)
            {
                var what = ready.Source switch
                {
                    PotSource.RoundUp => "in round-ups",
                    PotSource.Leftover => "of leftover budget",
                    _ => "in no-spend bonuses",
                };
                return new Nudge($"claim-{ready.Source}", $"{{amount}} {what} is ready to move into {pot.Name}.", ready.Amount, "/pots");
            }
        }

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysLeft = DateTime.DaysInMonth(today.Year, today.Month) - today.Day;
        if (daysLeft >= 5)
        {
            var thisMonth = history.Where(t => t.Date >= monthStart).ToList();
            var tight = categories
                .Where(c => c.Kind == TransactionType.Expense && c.MonthlyBudget is > 0)
                .Select(c => (Category: c, Used: AnalyticsService.NetSpent(thisMonth.Where(t => t.CategoryId == c.Id)) / c.MonthlyBudget!.Value))
                .Where(x => x.Used is >= 0.8m and < 1m)
                .OrderByDescending(x => x.Used)
                .FirstOrDefault();
            if (tight.Category is not null)
            {
                return new Nudge("budget-close", $"Heads-up: {tight.Category.Name} has used {tight.Used:P0} of its budget, with {daysLeft} days to go.", null,
                    $"/transactions?category={tight.Category.Id}");
            }
        }

        var due = AnalyticsService.FindRecurring(history.Where(t => t.Type == TransactionType.Expense), today)
            .Select(r => (r, Next: r.LastDate.AddMonths(1)))
            .Where(x => x.Next >= today && x.Next <= today.AddDays(3))
            .OrderBy(x => x.Next)
            .FirstOrDefault();
        if (due.r is not null)
        {
            var when = due.Next == today ? "today" : due.Next == today.AddDays(1) ? "tomorrow" : $"around {due.Next:ddd d MMM}";
            return new Nudge("recurring-due", $"{due.r.Merchant} usually charges {{amount}} {when}.", due.r.TypicalAmount, null);
        }

        var lastEntry = history.Count > 0 ? history.Max(t => t.Date) : (DateOnly?)null;
        if (lastEntry is { } last && today.DayNumber - last.DayNumber >= 7)
        {
            return new Nudge("quiet", $"Nothing logged since {last:d MMM}. Press N to quick-add anything you remember, or import a statement.", null, "/import");
        }

        if (lastEntry is not null)
        {
            var spendDays = history.Where(t => t.Type == TransactionType.Expense).Select(t => t.Date).ToHashSet();
            var streak = 0;
            for (var d = today.AddDays(-1); d >= lastEntry.Value.AddDays(-60) && d <= lastEntry && !spendDays.Contains(d); d = d.AddDays(-1)) streak++;
            if (streak >= 2) return new Nudge("no-spend-streak", $"{streak} no-spend days in a row. Nice rhythm.", null, null);
        }

        var behind = active.Where(p => p.Pace?.Status == "behind").OrderBy(p => p.Pace!.DaysLeft).FirstOrDefault();
        if (behind is not null)
        {
            return new Nudge("pot-pace", $"{behind.Name} needs about {{amount}} a week to land by {behind.Deadline:d MMM}.", behind.Pace!.PerWeek, "/pots");
        }

        return null;
    }
}
