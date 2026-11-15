using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Pet;
using SpenceAI.Domain;

namespace SpenceAI.Application.Quests;

/// <param name="Detail">May contain "{amount}", which the client fills in with <paramref name="Amount"/> in the user's currency.</param>
public record QuestView(string Key, string Title, string Detail, int Progress, int Goal, int Xp, bool Done, decimal? Amount = null);

/// <summary>Unit: "days" | "weeks" | "bills". <see cref="Alive"/> = the streak can still grow today (it isn't broken).</summary>
public record StreakView(string Key, string Title, int Current, int Best, string Unit, bool Alive);

public record LevelView(int Number, string Title, int Xp, int LevelStartXp, int NextLevelXp);

/// <param name="JustCompleted">Quests seen done for the first time by this request, so the client can celebrate once.</param>
public record QuestsState(DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<QuestView> Quests, IReadOnlyList<StreakView> Streaks,
    LevelView Level, IReadOnlyList<string> Unlocked, IReadOnlyList<QuestView> JustCompleted);

/// <summary>
/// Streaks and three small side quests a week, all worked out from your own data with plain rules. A quest that's done is recorded
/// (it keeps its XP even if a later edit would undo it), and XP adds up to levels that unlock outfits for the pet.
/// </summary>
public class QuestService(IAppDbContext db)
{
    public const int QuestsPerWeek = 3;

    /// <summary>XP needed to reach each level (index 0 = level 1).</summary>
    private static readonly int[] LevelXp = [0, 60, 150, 280, 450, 700, 1000];
    private static readonly string[] LevelTitles = ["Seedling", "Sprout", "Budding", "Leafy", "Blooming", "Evergreen", "Old oak"];

    /// <summary>Pet outfits and the level that unlocks each.</summary>
    public static readonly (string Name, int Level)[] Accessories = [("flower", 2), ("bowtie", 3), ("shades", 4), ("crown", 5)];

    /// <summary>Categories you can't sensibly skip, so "go without" quests never pick them.</summary>
    private static readonly HashSet<string> Essentials = new(StringComparer.OrdinalIgnoreCase)
    {
        "Groceries", "Bills & utilities", "Housing", "Health", "Fuel", "Transport", "Education", "Fees & charges", "Subscriptions",
    };

    public static DateOnly WeekStartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public async Task<QuestsState> GetAsync(DateOnly today, CancellationToken ct)
    {
        var week = WeekStartOf(today);
        var data = await LoadAsync(today, ct);
        var saved = await db.QuestCompletions.ToListAsync(ct);

        // Settle last week too, in case the app wasn't opened before it ended.
        var justCompleted = new List<QuestView>();
        var added = false;
        var levelBefore = Level(saved.Sum(s => s.Xp)).Number;
        foreach (var (start, asOf) in new[] { (week.AddDays(-7), week.AddDays(-1)), (week, today) })
        {
            foreach (var q in Quests(start, asOf, data).Where(q => q.Done))
            {
                if (saved.Any(s => s.WeekStart == start && s.Key == q.Key)) continue;
                var completion = new QuestCompletion { WeekStart = start, Key = q.Key, Xp = q.Xp };
                db.QuestCompletions.Add(completion);
                saved.Add(completion);
                added = true;
                if (start == week) justCompleted.Add(q);
            }
        }
        if (added)
        {
            // A level that unlocks an outfit puts it on the pet straight away ("auto" = best unlocked), unless they chose none.
            var levelAfter = Level(saved.Sum(s => s.Xp)).Number;
            if (Accessories.Any(a => a.Level > levelBefore && a.Level <= levelAfter)
                && await db.Settings.FirstOrDefaultAsync(ct) is { } settings && settings.PetAccessory != "none")
            {
                settings.PetAccessory = null;
            }
            await db.SaveChangesAsync(ct);
        }

        var quests = Quests(week, today, data)
            .Select(q => saved.Any(s => s.WeekStart == week && s.Key == q.Key) ? q with { Done = true, Progress = q.Goal } : q)
            .ToList();

        var xp = saved.Sum(s => s.Xp);
        var level = Level(xp);
        return new QuestsState(week, week.AddDays(6), quests, Streaks(today, data), level,
            Accessories.Where(a => level.Number >= a.Level).Select(a => a.Name).ToList(), justCompleted);
    }

    public async Task<int> TotalXpAsync(CancellationToken ct) => await db.QuestCompletions.SumAsync(c => c.Xp, ct);

    public static LevelView Level(int xp)
    {
        var index = 0;
        while (index + 1 < LevelXp.Length && xp >= LevelXp[index + 1]) index++;
        var extra = 0;
        // Past the last named level, every 400 XP is another level.
        if (index == LevelXp.Length - 1) extra = (xp - LevelXp[^1]) / 400;
        var number = index + 1 + extra;
        var start = index == LevelXp.Length - 1 ? LevelXp[^1] + extra * 400 : LevelXp[index];
        var next = index + 1 < LevelXp.Length ? LevelXp[index + 1] : start + 400;
        return new LevelView(number, LevelTitles[index], xp, start, next);
    }

    // ---------------- Data ----------------

    public sealed record QuestData(
        List<Transaction> Transactions,
        List<Category> Categories,
        List<PotContribution> Contributions,
        List<Bill> Bills,
        HashSet<DateOnly> LoggedDays,
        DateOnly? LastData,
        bool HasPots);

    private async Task<QuestData> LoadAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-400);
        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Category).Where(t => t.Date >= from).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var contributions = await db.PotContributions.AsNoTracking().Where(c => c.Date >= from).ToListAsync(ct);
        var bills = await db.Bills.AsNoTracking().Include(b => b.Marks).ToListAsync(ct);

        // "Logged" = the day you added something to Spence (by hand, quick add or an import), in this machine's time zone.
        var createdFrom = DateTime.UtcNow.AddDays(-400);
        var created = await db.Transactions.AsNoTracking().Where(t => t.CreatedAt >= createdFrom).Select(t => t.CreatedAt).ToListAsync(ct);
        var logged = created.Select(c => DateOnly.FromDateTime(DateTime.SpecifyKind(c, DateTimeKind.Utc).ToLocalTime())).ToHashSet();

        var lastData = transactions.Count > 0 ? transactions.Max(t => t.Date) : (DateOnly?)null;
        var hasPots = await db.SavingsPots.AnyAsync(p => !p.IsArchived, ct);
        return new QuestData(transactions, categories, contributions, bills, logged, lastData, hasPots);
    }

    // ---------------- Quests ----------------

    /// <summary>The week's quests, chosen from those that fit your data, the same three all week.</summary>
    public static List<QuestView> Quests(DateOnly weekStart, DateOnly asOf, QuestData data)
    {
        var weekEnd = weekStart.AddDays(6);
        var until = asOf < weekEnd ? asOf : weekEnd;
        var days = Enumerable.Range(0, Math.Max(0, until.DayNumber - weekStart.DayNumber + 1)).Select(weekStart.AddDays).ToList();
        // Only days the data covers count for "no spending" quests, so a gap in imports isn't mistaken for saving.
        var covered = days.Where(d => data.LastData is { } last && d <= last).ToList();
        var inWeek = data.Transactions.Where(t => t.Date >= weekStart && t.Date <= weekEnd).ToList();
        var spendDays = inWeek.Where(t => t.Type == TransactionType.Expense).Select(t => t.Date).ToHashSet();

        var candidates = new List<QuestView>();

        var logged = days.Count(data.LoggedDays.Contains);
        candidates.Add(new QuestView("log-5", "Check in on 5 days", "Add or import something on five different days this week.", Math.Min(logged, 5), 5, 30, logged >= 5));

        var noSpend = covered.Count(d => !spendDays.Contains(d));
        candidates.Add(new QuestView("no-spend-2", "Two no-spend days", "Two days this week with no spending at all.", Math.Min(noSpend, 2), 2, 40, noSpend >= 2));

        var weekExpenses = inWeek.Where(t => t.Type == TransactionType.Expense && t.Date <= until).ToList();
        var sorted = weekExpenses.Count(t => t.CategoryId is not null);
        candidates.Add(new QuestView("tidy", "Everything sorted", "Every expense this week has a category. Untidy ones are under Transactions.",
            sorted, Math.Max(1, weekExpenses.Count), 25, weekExpenses.Count > 0 && sorted == weekExpenses.Count));

        // Go three days in a row without your most frequent treat category from the four weeks before.
        var habit = data.Transactions
            .Where(t => t.Type == TransactionType.Expense && t.Date >= weekStart.AddDays(-28) && t.Date < weekStart && t.Category is not null && !Essentials.Contains(t.Category.Name))
            .GroupBy(t => t.Category!)
            .Where(g => g.Count() >= 3)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (habit is not null)
        {
            var hits = inWeek.Where(t => t.Type == TransactionType.Expense && t.CategoryId == habit.Id).Select(t => t.Date).ToHashSet();
            int run = 0, best = 0;
            foreach (var d in covered)
            {
                run = hits.Contains(d) ? 0 : run + 1;
                best = Math.Max(best, run);
            }
            candidates.Add(new QuestView($"skip-{habit.Id}", $"Three days without {habit.Name}", $"Your most frequent treat lately. Three days in a row without it.",
                Math.Min(best, 3), 3, 40, best >= 3));
        }

        // Stay under an even daily share of your usual month on three days.
        var baseline = PetService.Baseline(data.Transactions, data.Categories, new DateOnly(weekStart.Year, weekStart.Month, 1));
        if (baseline is > 0)
        {
            var perDay = baseline.Value / DateTime.DaysInMonth(weekStart.Year, weekStart.Month);
            var light = covered.Count(d => AnalyticsService.NetSpent(inWeek.Where(t => t.Date == d)) <= perDay);
            candidates.Add(new QuestView("light-3", "Three light days", "Three days this week under your usual daily spend of {amount}.",
                Math.Min(light, 3), 3, 35, light >= 3, decimal.Round(perDay, 0)));
        }

        if (data.HasPots)
        {
            var potIn = data.Contributions.Any(c => c.Amount > 0 && c.Date >= weekStart && c.Date <= until);
            candidates.Add(new QuestView("pot-in", "Feed a pot", "Move any amount into a savings pot this week.", potIn ? 1 : 0, 1, 30, potIn));
        }

        var activeBills = data.Bills.Where(b => !b.IsPaused && b.CancelledOn is null).ToList();
        var dueThisWeek = activeBills.SelectMany(b => BillService.Occurrences(b, weekStart, weekEnd).Select(d => (Bill: b, Due: d))).ToList();
        if (dueThisWeek.Count > 0)
        {
            var onTime = 0;
            foreach (var group in dueThisWeek.GroupBy(x => x.Bill))
            {
                var occurrences = BillService.Resolve(group.Key, data.Transactions, asOf, data.LastData);
                onTime += occurrences.Count(o => o.DueDate >= weekStart && o.DueDate <= weekEnd &&
                    (o.Status == "skipped" || (o.Status == "paid" && o.PaidOn <= o.DueDate.AddDays(1))));
            }
            candidates.Add(new QuestView("bills-on-time", "Bills on time", $"Pay the {dueThisWeek.Count} {(dueThisWeek.Count == 1 ? "bill" : "bills")} due this week on time.",
                Math.Min(onTime, dueThisWeek.Count), dueThisWeek.Count, 30, onTime >= dueThisWeek.Count));
        }

        if (activeBills.Any(b => b.IsSubscription))
        {
            var reviewed = data.Bills.Any(b => b.IsSubscription && b.LastReviewedOn is { } r && r >= weekStart && r <= weekEnd);
            candidates.Add(new QuestView("review-subs", "Subscription check", "Swipe through your subscriptions: keep or cancel each one.", reviewed ? 1 : 0, 1, 25, reviewed));
        }

        // A stable shuffle per week: same three all week, different mix next week.
        return candidates
            .OrderBy(q => StableHash($"{weekStart:yyyyMMdd}:{(q.Key.StartsWith("skip-") ? "skip" : q.Key)}"))
            .Take(QuestsPerWeek)
            .ToList();
    }

    private static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s) h = (h ^ ch) * 16777619u;
        return h;
    }

    // ---------------- Streaks ----------------

    public static List<StreakView> Streaks(DateOnly today, QuestData data)
    {
        var result = new List<StreakView>();

        // Check-ins: days in a row you added something. Today not done yet doesn't break it.
        {
            var current = 0;
            var d = data.LoggedDays.Contains(today) ? today : today.AddDays(-1);
            while (data.LoggedDays.Contains(d)) { current++; d = d.AddDays(-1); }
            result.Add(new StreakView("check-in", "Check-in streak", current, Math.Max(current, LongestRun(data.LoggedDays)), "days", current > 0));
        }

        // No-spend days, up to the latest day your data covers.
        if (data.LastData is { } last)
        {
            var spendDays = data.Transactions.Where(t => t.Type == TransactionType.Expense).Select(t => t.Date).ToHashSet();
            var end = last < today ? last : today;
            var current = 0;
            for (var d = end; d > end.AddDays(-365) && !spendDays.Contains(d); d = d.AddDays(-1)) current++;
            var first = data.Transactions.Min(t => t.Date);
            var clean = Enumerable.Range(0, end.DayNumber - first.DayNumber + 1).Select(first.AddDays).Where(d => !spendDays.Contains(d)).ToHashSet();
            result.Add(new StreakView("no-spend", "No-spend days", current, Math.Max(current, LongestRun(clean)), "days", current > 0));
        }

        // Weeks in a row under your usual weekly spend (completed weeks only).
        {
            var thisWeek = WeekStartOf(today);
            var weeks = new List<bool>();
            for (var w = thisWeek.AddDays(-7); w >= thisWeek.AddDays(-7 * 52); w = w.AddDays(-7))
            {
                var inWeek = data.Transactions.Where(t => t.Date >= w && t.Date <= w.AddDays(6)).ToList();
                if (inWeek.Count == 0) break;
                var baseline = PetService.Baseline(data.Transactions, data.Categories, new DateOnly(w.Year, w.Month, 1));
                if (baseline is null or <= 0) break;
                weeks.Add(AnalyticsService.NetSpent(inWeek) <= baseline.Value * 12 / 52);
            }
            var current = weeks.TakeWhile(x => x).Count();
            int run = 0, best = 0;
            foreach (var ok in weeks) { run = ok ? run + 1 : 0; best = Math.Max(best, run); }
            if (weeks.Count > 0) result.Add(new StreakView("light-weeks", "Light weeks", current, best, "weeks", current > 0));
        }

        // Bills paid on time, across all bills, most recent first.
        {
            var occurrences = data.Bills.Where(b => !b.IsPaused)
                .SelectMany(b => BillService.Resolve(b, data.Transactions, today, data.LastData))
                .Where(o => o.DueDate <= today && o.Status is not ("skipped" or "unconfirmed" or "due"))
                .OrderByDescending(o => o.DueDate)
                .ToList();
            if (occurrences.Count > 0)
            {
                var flags = occurrences.Select(o => o.Status == "paid" && o.PaidOn <= o.DueDate.AddDays(1)).ToList();
                var current = flags.TakeWhile(x => x).Count();
                int run = 0, best = 0;
                foreach (var ok in flags) { run = ok ? run + 1 : 0; best = Math.Max(best, run); }
                result.Add(new StreakView("bills", "Bills on time", current, best, "bills", current > 0));
            }
        }

        return result;
    }

    private static int LongestRun(IEnumerable<DateOnly> days)
    {
        int best = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in days.Order())
        {
            run = prev is { } p && d.DayNumber == p.DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }
        return best;
    }
}
