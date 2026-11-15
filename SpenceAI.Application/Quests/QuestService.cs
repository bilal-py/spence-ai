using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Pet;
using SpenceAI.Application.Savings;
using SpenceAI.Domain;

namespace SpenceAI.Application.Quests;

/// <param name="Detail">May contain "{amount}", which the client fills in with <paramref name="Amount"/> in the user's currency.</param>
public record QuestView(string Key, string Title, string Detail, int Progress, int Goal, int Xp, bool Done, decimal? Amount = null);

/// <summary>Unit: "days" | "weeks" | "bills". <see cref="Alive"/> = the streak can still grow today (it isn't broken).</summary>
/// <param name="Since">First day of the current run, so a streak bonus is paid once per run.</param>
/// <param name="NextMilestone">The next length that pays a streak bonus, worth <paramref name="NextMilestoneXp"/>.</param>
public record StreakView(string Key, string Title, int Current, int Best, string Unit, bool Alive,
    DateOnly? Since = null, int? NextMilestone = null, int? NextMilestoneXp = null);

/// <param name="Month">Any day in the Wrapped month that was opened.</param>
public record WrappedSeenRequest(DateOnly Month, DateOnly? Today);

public record LevelView(int Number, string Title, int Xp, int LevelStartXp, int NextLevelXp);

/// <param name="Bonus">A harder fourth quest, offered once the week's three are done.</param>
/// <param name="Monthly">This month's quests.</param>
/// <param name="Achievements">One-time goals, earned or not (Done = earned).</param>
/// <param name="JustCompleted">Anything seen done for the first time by this request (quests, bonuses, streak bonuses, achievements), so the client can celebrate once.</param>
public record QuestsState(DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<QuestView> Quests, QuestView? Bonus, IReadOnlyList<QuestView> Monthly,
    IReadOnlyList<QuestView> Achievements, IReadOnlyList<StreakView> Streaks, LevelView Level, IReadOnlyList<string> Unlocked, IReadOnlyList<QuestView> JustCompleted);

/// <summary>
/// Streaks, three small side quests a week (plus a bonus once they're done), monthly quests, streak bonuses and one-time achievements,
/// all worked out from your own data with plain rules. Anything done is recorded in <see cref="QuestCompletion"/> (it keeps its XP even if
/// a later edit would undo it), and XP adds up to levels that unlock outfits for the pet.
/// </summary>
/// <remarks>
/// One table holds every kind, told apart by the key: weekly keys have no colon and use the week's Monday; "month:x" uses the 1st of the
/// month; "streak:kind:n" uses the first day of the run (one bonus per run); "ach:x" uses <see cref="Lifetime"/> (once ever).
/// </remarks>
public class QuestService(IAppDbContext db, SavingsService savings)
{
    public const int QuestsPerWeek = 3;

    /// <summary>The WeekStart stored for achievements, which are earned once ever.</summary>
    public static readonly DateOnly Lifetime = DateOnly.MinValue;

    /// <summary>XP needed to reach each level (index 0 = level 1).</summary>
    private static readonly int[] LevelXp = [0, 60, 150, 280, 450, 700, 1000];
    private static readonly string[] LevelTitles = ["Seedling", "Sprout", "Budding", "Leafy", "Blooming", "Evergreen", "Old oak"];

    /// <summary>Pet outfits and the level that unlocks each.</summary>
    public static readonly (string Name, int Level)[] Accessories = [("flower", 2), ("bowtie", 3), ("shades", 4), ("crown", 5), ("scarf", 6), ("acorn", 7)];

    /// <summary>Streak lengths that pay a bonus, once per run.</summary>
    private static readonly Dictionary<string, (int At, int Xp)[]> Milestones = new()
    {
        ["check-in"] = [(7, 20), (14, 40), (30, 80)],
        ["no-spend"] = [(3, 20), (5, 40), (7, 80)],
        ["light-weeks"] = [(2, 25), (4, 50), (8, 100)],
        ["bills"] = [(5, 20), (10, 40), (20, 80)],
    };

    /// <summary>Categories you can't sensibly skip, so "go without" quests never pick them.</summary>
    private static readonly HashSet<string> Essentials = new(StringComparer.OrdinalIgnoreCase)
    {
        "Groceries", "Bills & utilities", "Housing", "Health", "Fuel", "Transport", "Education", "Fees & charges", "Subscriptions",
    };

    public static DateOnly WeekStartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static DateOnly MonthStartOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Weekly quest keys never contain a colon; everything else does.</summary>
    private static int WeeklyDone(List<QuestCompletion> saved, DateOnly week) =>
        saved.Count(s => s.WeekStart == week && !s.Key.Contains(':') && !s.Key.StartsWith("bonus-"));

    public async Task<QuestsState> GetAsync(DateOnly today, CancellationToken ct)
    {
        var week = WeekStartOf(today);
        var month = MonthStartOf(today);
        var data = await LoadAsync(today, ct);
        var saved = await db.QuestCompletions.ToListAsync(ct);
        var levelBefore = Level(saved.Sum(s => s.Xp)).Number;
        var justCompleted = new List<QuestView>();
        var added = false;

        bool Has(DateOnly start, string key) => saved.Any(s => s.WeekStart == start && s.Key == key);
        void Record(DateOnly start, QuestView? q, bool announce)
        {
            if (q is not { Done: true } || Has(start, q.Key)) return;
            var completion = new QuestCompletion { WeekStart = start, Key = q.Key, Xp = q.Xp };
            db.QuestCompletions.Add(completion);
            saved.Add(completion);
            added = true;
            if (announce) justCompleted.Add(q);
        }

        // Settle last week and last month too, in case the app wasn't opened before they ended.
        foreach (var (start, asOf) in new[] { (week.AddDays(-7), week.AddDays(-1)), (week, today) })
        {
            foreach (var q in Quests(start, asOf, data)) Record(start, q, start == week);
            if (WeeklyDone(saved, start) >= QuestsPerWeek) Record(start, Bonus(start, asOf, data), start == week);
        }
        foreach (var (start, asOf) in new[] { (month.AddMonths(-1), month.AddDays(-1)), (month, today) })
        {
            foreach (var q in Monthly(start, asOf, data)) Record(start, q, start == month);
        }
        var streaks = Streaks(today, data);
        foreach (var (since, q) in StreakBonuses(streaks)) Record(since, q, true);
        // Last, so "a lighter month" sees a monthly quest settled just above.
        foreach (var q in Achievements(data, streaks, saved).ToList()) Record(Lifetime, q, true);

        if (added) await SaveAsync(levelBefore, saved, ct);

        QuestView Settled(DateOnly start, QuestView q) => Has(start, q.Key) ? q with { Done = true, Progress = q.Goal } : q;
        var quests = Quests(week, today, data).Select(q => Settled(week, q)).ToList();
        var bonus = WeeklyDone(saved, week) >= QuestsPerWeek ? Settled(week, Bonus(week, today, data)) : null;
        var monthly = Monthly(month, today, data).Select(q => Settled(month, q)).ToList();
        var achievements = Achievements(data, streaks, saved).Select(q => Settled(Lifetime, q))
            .OrderByDescending(q => q.Done).ThenByDescending(q => (double)q.Progress / Math.Max(1, q.Goal)).ToList();

        var level = Level(saved.Sum(s => s.Xp));
        return new QuestsState(week, week.AddDays(6), quests, bonus, monthly, achievements, streaks, level,
            Accessories.Where(a => level.Number >= a.Level).Select(a => a.Name).ToList(), justCompleted);
    }

    /// <summary>
    /// The monthly "look back" quest: opening last month's Wrapped during this month. Returns the quest if this call completed it, else null.
    /// </summary>
    public async Task<QuestView?> WrappedSeenAsync(DateOnly wrappedMonth, DateOnly today, CancellationToken ct)
    {
        var month = MonthStartOf(today);
        if (MonthStartOf(wrappedMonth) != month.AddMonths(-1)) return null;
        var saved = await db.QuestCompletions.ToListAsync(ct);
        if (saved.Any(s => s.WeekStart == month && s.Key == WrappedKey)) return null;
        if (!await db.Transactions.AnyAsync(t => t.Date >= month.AddMonths(-1) && t.Date < month, ct)) return null;

        var quest = WrappedQuest(month) with { Done = true, Progress = 1 };
        var levelBefore = Level(saved.Sum(s => s.Xp)).Number;
        var completion = new QuestCompletion { WeekStart = month, Key = quest.Key, Xp = quest.Xp };
        db.QuestCompletions.Add(completion);
        saved.Add(completion);
        await SaveAsync(levelBefore, saved, ct);
        return quest;
    }

    /// <summary>Saves new completions. A level that unlocks an outfit puts it on the pet straight away ("auto"), unless they chose none.</summary>
    private async Task SaveAsync(int levelBefore, List<QuestCompletion> saved, CancellationToken ct)
    {
        var levelAfter = Level(saved.Sum(s => s.Xp)).Number;
        if (Accessories.Any(a => a.Level > levelBefore && a.Level <= levelAfter)
            && await db.Settings.FirstOrDefaultAsync(ct) is { } settings && settings.PetAccessory != "none")
        {
            settings.PetAccessory = null;
        }
        await db.SaveChangesAsync(ct);
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
        bool HasPots,
        List<SavingsPot>? Pots = null,
        AllTime? Totals = null);

    /// <summary>All-time counts for achievements.</summary>
    public sealed record AllTime(int Transactions, int CommittedImports, int Rules, int BudgetedCategories, int ActiveBills,
        bool SubscriptionCut, bool ChallengeWon, bool PotFilled);

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
        var pots = await db.SavingsPots.AsNoTracking().ToListAsync(ct);
        var totals = new AllTime(
            Transactions: await db.Transactions.CountAsync(ct),
            CommittedImports: await db.ImportBatches.CountAsync(b => b.Status == ImportStatus.Committed, ct),
            Rules: await db.MerchantRules.CountAsync(ct),
            BudgetedCategories: categories.Count(c => c.MonthlyBudget > 0),
            ActiveBills: bills.Count(b => b.CancelledOn is null),
            SubscriptionCut: bills.Any(b => b.IsSubscription && b.CancelledOn is not null),
            ChallengeWon: await db.PotChallenges.AnyAsync(c => c.ClaimedAt != null, ct),
            PotFilled: pots.Count > 0 && (await savings.ListAsync(today, ct)).Any(p => p.Progress >= 1));
        return new QuestData(transactions, categories, contributions, bills, logged, lastData, pots.Any(p => !p.IsArchived), pots, totals);
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

    /// <summary>Days of the week so far, and those the data covers (for "no spending" counts).</summary>
    private static (List<DateOnly> Days, List<DateOnly> Covered) WeekDays(DateOnly weekStart, DateOnly asOf, QuestData data)
    {
        var weekEnd = weekStart.AddDays(6);
        var until = asOf < weekEnd ? asOf : weekEnd;
        var days = Enumerable.Range(0, Math.Max(0, until.DayNumber - weekStart.DayNumber + 1)).Select(weekStart.AddDays).ToList();
        return (days, days.Where(d => data.LastData is { } last && d <= last).ToList());
    }

    /// <summary>A harder fourth quest for a week whose three are done. Same pick all week.</summary>
    public static QuestView Bonus(DateOnly weekStart, DateOnly asOf, QuestData data)
    {
        var (days, covered) = WeekDays(weekStart, asOf, data);
        var inWeek = data.Transactions.Where(t => t.Date >= weekStart && t.Date <= weekStart.AddDays(6)).ToList();
        var spendDays = inWeek.Where(t => t.Type == TransactionType.Expense).Select(t => t.Date).ToHashSet();

        var logged = days.Count(data.LoggedDays.Contains);
        var candidates = new List<QuestView>
        {
            new("bonus-log-7", "Bonus: every single day", "Check in on all seven days this week.", Math.Min(logged, 7), 7, 50, logged >= 7),
        };
        var noSpend = covered.Count(d => !spendDays.Contains(d));
        candidates.Add(new QuestView("bonus-no-spend-4", "Bonus: four no-spend days", "Four days this week with no spending at all.", Math.Min(noSpend, 4), 4, 60, noSpend >= 4));

        var baseline = PetService.Baseline(data.Transactions, data.Categories, MonthStartOf(weekStart));
        if (baseline is > 0)
        {
            var perDay = baseline.Value / DateTime.DaysInMonth(weekStart.Year, weekStart.Month);
            var light = covered.Count(d => AnalyticsService.NetSpent(inWeek.Where(t => t.Date == d)) <= perDay);
            candidates.Add(new QuestView("bonus-light-5", "Bonus: five light days", "Five days this week under your usual daily spend of {amount}.",
                Math.Min(light, 5), 5, 55, light >= 5, decimal.Round(perDay, 0)));
        }
        return candidates.OrderBy(q => StableHash($"{weekStart:yyyyMMdd}:{q.Key}")).First();
    }

    // ---------------- Monthly ----------------

    private const string WrappedKey = "month:wrapped";

    private static QuestView WrappedQuest(DateOnly monthStart)
    {
        var last = monthStart.AddMonths(-1).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);
        return new QuestView(WrappedKey, $"Look back at {last}", $"Open your {last} Wrapped.", 0, 1, 20, false);
    }

    /// <summary>
    /// The month's quests. "Look back" is completed by opening Wrapped (<see cref="WrappedSeenAsync"/>); "a lighter month" can only
    /// finish once the month is over and the data reaches its end, so an import gap isn't mistaken for saving.
    /// </summary>
    public static List<QuestView> Monthly(DateOnly monthStart, DateOnly asOf, QuestData data)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var until = asOf < monthEnd ? asOf : monthEnd;
        var inMonth = data.Transactions.Where(t => t.Date >= monthStart && t.Date <= until).ToList();
        var quests = new List<QuestView>();

        if (data.Transactions.Any(t => t.Date >= monthStart.AddMonths(-1) && t.Date < monthStart)) quests.Add(WrappedQuest(monthStart));

        var baseline = PetService.Baseline(data.Transactions, data.Categories, monthStart);
        if (baseline is > 0)
        {
            var under = AnalyticsService.NetSpent(inMonth) <= baseline.Value;
            var over = asOf >= monthEnd && data.LastData is { } last && last >= monthEnd.AddDays(-3);
            var length = monthEnd.Day;
            var elapsed = Math.Max(0, until.DayNumber - monthStart.DayNumber + 1);
            quests.Add(new QuestView("month:lighter", "A lighter month", "Finish the month under your usual {amount}.",
                under ? Math.Min(elapsed, length) : 0, length, 80, over && under, decimal.Round(baseline.Value, 0)));
        }

        var due = data.Bills.Where(b => !b.IsPaused && b.CancelledOn is null)
            .SelectMany(b => BillService.Occurrences(b, monthStart, monthEnd).Select(d => (Bill: b, Due: d))).ToList();
        if (due.Count > 0)
        {
            var onTime = 0;
            foreach (var group in due.GroupBy(x => x.Bill))
            {
                onTime += BillService.Resolve(group.Key, data.Transactions, asOf, data.LastData).Count(o => o.DueDate >= monthStart && o.DueDate <= monthEnd &&
                    (o.Status == "skipped" || (o.Status == "paid" && o.PaidOn <= o.DueDate.AddDays(1))));
            }
            quests.Add(new QuestView("month:bills", "No late bills", $"Pay all {due.Count} {(due.Count == 1 ? "bill" : "bills")} due this month on time.",
                Math.Min(onTime, due.Count), due.Count, 50, onTime >= due.Count));
        }

        var pots = (data.Pots ?? []).Where(p => !p.IsArchived && p.StartDate <= monthEnd).ToList();
        if (pots.Count > 0)
        {
            var fed = pots.Count(p =>
                data.Contributions.Any(c => c.PotId == p.Id && c.Amount > 0 && c.Date >= monthStart && c.Date <= until) ||
                (!string.IsNullOrWhiteSpace(p.LinkPattern) && inMonth.Any(t => t.Type == TransactionType.Transfer &&
                    ((t.Merchant?.Contains(p.LinkPattern.Trim(), StringComparison.OrdinalIgnoreCase) ?? false) ||
                     t.Description.Contains(p.LinkPattern.Trim(), StringComparison.OrdinalIgnoreCase)))));
            quests.Add(new QuestView("month:pots", pots.Count == 1 ? "Feed your pot" : "Feed every pot",
                pots.Count == 1 ? "Put something into your savings pot this month." : $"Put something into each of your {pots.Count} pots this month.",
                fed, pots.Count, 40, fed >= pots.Count));
        }
        return quests;
    }

    // ---------------- Streak bonuses and achievements ----------------

    /// <summary>Streak lengths reached in the current runs. Keyed by the run's first day, so each pays once per run.</summary>
    public static IEnumerable<(DateOnly Since, QuestView Quest)> StreakBonuses(IEnumerable<StreakView> streaks)
    {
        foreach (var s in streaks)
        {
            if (s.Since is not { } since || !Milestones.TryGetValue(s.Key, out var steps)) continue;
            foreach (var (at, xp) in steps.Where(m => s.Current >= m.At))
            {
                var title = s.Key switch
                {
                    "check-in" => $"{at}-day check-in streak",
                    "no-spend" => $"{at} no-spend days in a row",
                    "light-weeks" => $"{at} light weeks in a row",
                    _ => $"{at} bills on time in a row",
                };
                yield return (since, new QuestView($"streak:{s.Key}:{at}", title, "Streak bonus.", at, at, xp, true));
            }
        }
    }

    /// <summary>One-time goals. Done = reached now; the caller marks those already earned.</summary>
    public static List<QuestView> Achievements(QuestData data, IReadOnlyList<StreakView> streaks, IReadOnlyList<QuestCompletion> saved)
    {
        var t = data.Totals ?? new AllTime(0, 0, 0, 0, 0, false, false, false);
        var bestCheckIn = streaks.FirstOrDefault(s => s.Key == "check-in")?.Best ?? 0;
        QuestView Count(string key, string title, string detail, int have, int goal, int xp) =>
            new($"ach:{key}", title, detail, Math.Min(have, goal), goal, xp, have >= goal);
        QuestView Flag(string key, string title, string detail, bool done, int xp) => Count(key, title, detail, done ? 1 : 0, 1, xp);
        return
        [
            Flag("first-import", "First statement", "Import a statement and save it.", t.CommittedImports > 0, 30),
            Count("entries-100", "A hundred entries", "Have 100 transactions in Spence.", t.Transactions, 100, 40),
            Count("bills-3", "Bill keeper", "Set up 3 bills or subscriptions.", t.ActiveBills, 3, 30),
            Count("budgets-5", "Budgeter", "Give 5 categories a monthly budget.", t.BudgetedCategories, 5, 40),
            Count("rules-10", "Good teacher", "Spence has learned 10 merchant rules from your fixes.", t.Rules, 10, 30),
            Flag("sub-cut", "Snip", "Cut a subscription in the subscription check.", t.SubscriptionCut, 40),
            Flag("challenge", "Challenger", "Win a savings pot challenge.", t.ChallengeWon, 50),
            Flag("pot-filled", "Goal!", "Fill a savings pot all the way.", t.PotFilled, 100),
            Count("check-in-30", "Regular", "Check in 30 days in a row.", bestCheckIn, 30, 100),
            Flag("lighter-month", "Lighter month", "Finish a month under your usual spend.", saved.Any(s => s.Key == "month:lighter"), 60),
        ];
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
            result.Add(new StreakView("check-in", "Check-in streak", current, Math.Max(current, LongestRun(data.LoggedDays)), "days", current > 0, current > 0 ? d.AddDays(1) : null));
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
            result.Add(new StreakView("no-spend", "No-spend days", current, Math.Max(current, LongestRun(clean)), "days", current > 0,
                current > 0 ? end.AddDays(1 - current) : null));
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
            if (weeks.Count > 0) result.Add(new StreakView("light-weeks", "Light weeks", current, best, "weeks", current > 0,
                current > 0 ? thisWeek.AddDays(-7 * current) : null));
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
                result.Add(new StreakView("bills", "Bills on time", current, best, "bills", current > 0,
                    current > 0 ? occurrences[current - 1].DueDate : null));
            }
        }

        // The next length that pays a streak bonus, so the UI can show what's coming.
        return result.Select(s => Milestones.TryGetValue(s.Key, out var steps) && steps.FirstOrDefault(m => m.At > s.Current) is { At: > 0 } next
            ? s with { NextMilestone = next.At, NextMilestoneXp = next.Xp }
            : s).ToList();
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
