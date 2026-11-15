using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Analytics;
using SpenceAI.Domain;

namespace SpenceAI.Application.Savings;

public record UpsertPotRequest(
    string Name,
    decimal TargetAmount,
    DateOnly? StartDate,
    DateOnly? Deadline,
    string Color,
    string Icon,
    string? LinkPattern,
    int? RoundUpTo,
    bool CollectLeftover,
    decimal? NoSpendBonus,
    bool IsArchived = false);

public record AddContributionRequest(decimal Amount, DateOnly? Date, string? Note);
public record ClaimRequest(PotSource Source, DateOnly? Today);
public record AddChallengeRequest(ChallengeKind Kind, int? CategoryId, int? Target, DateOnly StartDate, DateOnly EndDate, decimal Reward);

/// <summary>"ahead" | "on-track" | "behind" | "done" | "overdue".</summary>
public record PotPace(int DaysLeft, decimal Remaining, decimal PerMonth, decimal PerWeek, decimal Expected, string Status);

/// <summary>Money waiting to be moved into a pot. Claiming it adds a contribution and moves the "claimed through" marker.</summary>
public record Claimable(PotSource Source, decimal Amount, string Detail);

public record PotEntry(DateOnly Date, decimal Amount, PotSource Source, string? Note, int? ContributionId, int? TransactionId);

/// <summary>Status: "active" | "won" | "lost" | "claimed". Progress/Goal are days (clean days, or no-spend days).</summary>
public record ChallengeView(int Id, ChallengeKind Kind, string Title, int? CategoryId, int? Target, DateOnly StartDate, DateOnly EndDate,
    decimal Reward, string Status, int Progress, int Goal);

public record PotView(
    int Id,
    string Name,
    string Color,
    string Icon,
    decimal TargetAmount,
    DateOnly StartDate,
    DateOnly? Deadline,
    string? LinkPattern,
    int? RoundUpTo,
    bool CollectLeftover,
    decimal? NoSpendBonus,
    bool IsArchived,
    decimal Saved,
    double Progress,
    IReadOnlyDictionary<PotSource, decimal> BySource,
    PotPace? Pace,
    IReadOnlyList<Claimable> Available,
    IReadOnlyList<ChallengeView> Challenges,
    IReadOnlyList<PotEntry> History);

/// <summary>Savings pots: goals, pace, and the money sources that feed them. Everything is computed from local data, no AI.</summary>
public partial class SavingsService(IAppDbContext db)
{
    public static readonly int[] RoundUpSteps = [5, 10, 20, 50, 100, 500, 1000];

    public async Task<List<PotView>> ListAsync(DateOnly today, CancellationToken ct)
    {
        var pots = await db.SavingsPots.AsNoTracking()
            .Include(p => p.Contributions)
            .Include(p => p.Challenges).ThenInclude(c => c.Category)
            .OrderBy(p => p.IsArchived).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);
        if (pots.Count == 0) return [];

        var data = await LoadAsync(pots, today, ct);
        return pots.Select(p => Build(p, data, today)).ToList();
    }

    public async Task<PotView> GetAsync(int id, DateOnly today, CancellationToken ct)
    {
        var pot = await db.SavingsPots.AsNoTracking()
            .Include(p => p.Contributions)
            .Include(p => p.Challenges).ThenInclude(c => c.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        return Build(pot, await LoadAsync([pot], today, ct), today);
    }

    public async Task<PotView> CreateAsync(UpsertPotRequest req, DateOnly today, CancellationToken ct)
    {
        var pot = new SavingsPot { StartDate = req.StartDate ?? today };
        await ApplyAsync(pot, req, ct);
        db.SavingsPots.Add(pot);
        await db.SaveChangesAsync(ct);
        return await GetAsync(pot.Id, today, ct);
    }

    public async Task<PotView> UpdateAsync(int id, UpsertPotRequest req, DateOnly today, CancellationToken ct)
    {
        var pot = await db.SavingsPots.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        if (req.StartDate is { } start) pot.StartDate = start;
        await ApplyAsync(pot, req, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(pot.Id, today, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var pot = await db.SavingsPots.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        db.SavingsPots.Remove(pot);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Money in (positive) or out (negative), by hand.</summary>
    public async Task<PotView> AddContributionAsync(int id, AddContributionRequest req, DateOnly today, CancellationToken ct)
    {
        if (req.Amount == 0 || Math.Abs(req.Amount) > 1_000_000_000m) throw new AppException("Enter an amount to add or take out.");
        var pot = await db.SavingsPots.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        db.PotContributions.Add(new PotContribution
        {
            PotId = pot.Id,
            Date = req.Date ?? today,
            Amount = decimal.Round(req.Amount, 2),
            Source = PotSource.Manual,
            Note = Trim(req.Note, 200),
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task<PotView> DeleteContributionAsync(int id, int contributionId, DateOnly today, CancellationToken ct)
    {
        var c = await db.PotContributions.FirstOrDefaultAsync(x => x.Id == contributionId && x.PotId == id, ct)
            ?? throw new AppException("That entry no longer exists.", 404);
        db.PotContributions.Remove(c);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    /// <summary>Moves what's available from a source (round-ups, leftover budget, no-spend days) into the pot.</summary>
    public async Task<PotView> ClaimAsync(int id, ClaimRequest req, CancellationToken ct)
    {
        var today = req.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var pot = await db.SavingsPots.Include(p => p.Contributions).Include(p => p.Challenges)
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        var data = await LoadAsync([pot], today, ct);

        var (amount, through, detail) = req.Source switch
        {
            PotSource.RoundUp => RoundUps(pot, data, today),
            PotSource.Leftover => Leftover(pot, data, today),
            PotSource.NoSpend => NoSpend(pot, data, today),
            _ => throw new AppException("Only round-ups, leftover budget and no-spend days can be claimed."),
        };
        if (amount <= 0 || through is null) throw new AppException("Nothing to move in right now.");

        db.PotContributions.Add(new PotContribution { PotId = pot.Id, Date = today, Amount = amount, Source = req.Source, Note = detail });
        switch (req.Source)
        {
            case PotSource.RoundUp: pot.RoundUpsClaimedThrough = through; break;
            case PotSource.Leftover: pot.LeftoverClaimedThrough = through; break;
            case PotSource.NoSpend: pot.NoSpendClaimedThrough = through; break;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task<PotView> AddChallengeAsync(int id, AddChallengeRequest req, DateOnly today, CancellationToken ct)
    {
        var pot = await db.SavingsPots.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        if (req.EndDate < req.StartDate) throw new AppException("The challenge has to end after it starts.");
        if (req.EndDate.DayNumber - req.StartDate.DayNumber > 366) throw new AppException("Keep challenges to a year or less.");
        if (req.Reward <= 0 || req.Reward > 1_000_000_000m) throw new AppException("Set a reward to move into the pot when you win.");

        switch (req.Kind)
        {
            case ChallengeKind.SkipCategory:
                if (req.CategoryId is not { } catId || !await db.Categories.AnyAsync(c => c.Id == catId && c.Kind == TransactionType.Expense, ct))
                {
                    throw new AppException("Pick a spending category to skip.");
                }
                break;
            case ChallengeKind.NoSpendDays:
                var days = req.EndDate.DayNumber - req.StartDate.DayNumber + 1;
                if (req.Target is not { } t || t < 1 || t > days) throw new AppException($"Aim for between 1 and {days} no-spend days.");
                break;
        }

        db.PotChallenges.Add(new PotChallenge
        {
            PotId = pot.Id,
            Kind = req.Kind,
            CategoryId = req.Kind == ChallengeKind.SkipCategory ? req.CategoryId : null,
            Target = req.Kind == ChallengeKind.NoSpendDays ? req.Target : null,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Reward = decimal.Round(req.Reward, 2),
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task<PotView> DeleteChallengeAsync(int id, int challengeId, DateOnly today, CancellationToken ct)
    {
        var c = await db.PotChallenges.FirstOrDefaultAsync(x => x.Id == challengeId && x.PotId == id, ct)
            ?? throw new AppException("That challenge no longer exists.", 404);
        db.PotChallenges.Remove(c);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    /// <summary>A won challenge's reward goes into the pot, once.</summary>
    public async Task<PotView> ClaimChallengeAsync(int id, int challengeId, DateOnly today, CancellationToken ct)
    {
        var pot = await db.SavingsPots.Include(p => p.Challenges).ThenInclude(c => c.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        var challenge = pot.Challenges.FirstOrDefault(c => c.Id == challengeId) ?? throw new AppException("That challenge no longer exists.", 404);
        var view = Evaluate(challenge, await LoadAsync([pot], today, ct), today);
        if (view.Status != "won") throw new AppException(view.Status == "claimed" ? "You already moved this reward in." : "Win the challenge first.");

        challenge.ClaimedAt = DateTime.UtcNow;
        db.PotContributions.Add(new PotContribution { PotId = pot.Id, Date = today, Amount = challenge.Reward, Source = PotSource.Challenge, Note = view.Title });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    // ---------- Computation ----------

    /// <summary>The transactions every pot calculation needs, loaded once.</summary>
    public sealed record Data(List<Transaction> Transactions, List<Category> Budgeted, DateOnly? LastDataDate)
    {
        public HashSet<DateOnly>? SpendDays { get; set; }
    }

    private async Task<Data> LoadAsync(IReadOnlyCollection<SavingsPot> pots, DateOnly today, CancellationToken ct)
    {
        var from = pots.Min(p => new[] { p.StartDate }.Concat(p.Challenges.Select(c => c.StartDate)).Min());
        from = new DateOnly(from.Year, from.Month, 1);
        var txs = await db.Transactions.AsNoTracking().Where(t => t.Date >= from && t.Date <= today).ToListAsync(ct);
        var budgeted = await db.Categories.AsNoTracking().Where(c => c.MonthlyBudget != null && c.Kind == TransactionType.Expense).ToListAsync(ct);
        var last = await db.Transactions.Where(t => t.Date <= today).OrderByDescending(t => t.Date).Select(t => (DateOnly?)t.Date).FirstOrDefaultAsync(ct);
        return new Data(txs, budgeted, last);
    }

    public static PotView Build(SavingsPot pot, Data data, DateOnly today)
    {
        var linked = LinkedTransfers(pot, data).ToList();
        var bySource = pot.Contributions.GroupBy(c => c.Source).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        if (linked.Count > 0) bySource[PotSource.Transfer] = linked.Sum(t => t.Amount);
        var saved = bySource.Values.Sum();

        var available = new List<Claimable>();
        if (!pot.IsArchived)
        {
            void Offer(PotSource source, (decimal Amount, DateOnly? Through, string Detail) x)
            {
                if (x.Amount > 0 && x.Through is not null) available.Add(new Claimable(source, x.Amount, x.Detail));
            }
            if (pot.RoundUpTo is not null) Offer(PotSource.RoundUp, RoundUps(pot, data, today));
            if (pot.CollectLeftover) Offer(PotSource.Leftover, Leftover(pot, data, today));
            if (pot.NoSpendBonus is not null) Offer(PotSource.NoSpend, NoSpend(pot, data, today));
        }

        var history = pot.Contributions
            .Select(c => new PotEntry(c.Date, c.Amount, c.Source, c.Note, c.Id, null))
            .Concat(linked.Select(t => new PotEntry(t.Date, t.Amount, PotSource.Transfer, t.Merchant ?? t.Description, null, t.Id)))
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.ContributionId ?? 0)
            .Take(40)
            .ToList();

        return new PotView(pot.Id, pot.Name, pot.Color, pot.Icon, pot.TargetAmount, pot.StartDate, pot.Deadline, pot.LinkPattern, pot.RoundUpTo,
            pot.CollectLeftover, pot.NoSpendBonus, pot.IsArchived, saved,
            pot.TargetAmount > 0 ? (double)Math.Clamp(saved / pot.TargetAmount, 0, 1) : 0,
            bySource, Pace(pot, saved, today), available,
            pot.Challenges.OrderByDescending(c => c.StartDate).Select(c => Evaluate(c, data, today)).ToList(),
            history);
    }

    public static PotPace? Pace(SavingsPot pot, decimal saved, DateOnly today)
    {
        var remaining = Math.Max(0, pot.TargetAmount - saved);
        if (pot.Deadline is not { } deadline)
        {
            return remaining == 0 ? new PotPace(0, 0, 0, 0, pot.TargetAmount, "done") : null;
        }

        var daysLeft = deadline.DayNumber - today.DayNumber;
        var total = Math.Max(1, deadline.DayNumber - pot.StartDate.DayNumber);
        var elapsed = Math.Clamp(today.DayNumber - pot.StartDate.DayNumber, 0, total);
        var expected = decimal.Round(pot.TargetAmount * elapsed / total, 2);

        if (remaining == 0) return new PotPace(Math.Max(0, daysLeft), 0, 0, 0, expected, "done");
        if (daysLeft <= 0) return new PotPace(0, remaining, remaining, remaining, pot.TargetAmount, "overdue");

        var perWeek = decimal.Round(remaining / Math.Max(1m, daysLeft / 7m), 2);
        var perMonth = decimal.Round(remaining / Math.Max(1m, daysLeft / 30.44m), 2);
        var status = saved >= expected * 1.1m && saved > 0 ? "ahead" : saved >= expected * 0.95m ? "on-track" : "behind";
        return new PotPace(daysLeft, remaining, perMonth, perWeek, expected, status);
    }

    private static IEnumerable<Transaction> LinkedTransfers(SavingsPot pot, Data data)
    {
        if (string.IsNullOrWhiteSpace(pot.LinkPattern)) return [];
        var pattern = pot.LinkPattern.Trim();
        return data.Transactions.Where(t => t.Type == TransactionType.Transfer && t.Date >= pot.StartDate &&
            ((t.Merchant?.Contains(pattern, StringComparison.OrdinalIgnoreCase) ?? false) || t.Description.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Spare change from rounding each expense up to the pot's step, since the last claim.</summary>
    public static (decimal Amount, DateOnly? Through, string Detail) RoundUps(SavingsPot pot, Data data, DateOnly today)
    {
        if (pot.RoundUpTo is not { } step || step <= 0) return (0, null, "");
        var from = Later(pot.StartDate, pot.RoundUpsClaimedThrough?.AddDays(1));
        var expenses = data.Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= from && t.Date <= today).ToList();
        var total = expenses.Sum(t => Math.Ceiling(t.Amount / step) * step - t.Amount);
        return (decimal.Round(total, 2), today, $"{expenses.Count} purchases rounded up to {step}");
    }

    /// <summary>What was left of the monthly budgets in each finished month since the last claim (months with data only).</summary>
    public static (decimal Amount, DateOnly? Through, string Detail) Leftover(SavingsPot pot, Data data, DateOnly today)
    {
        if (data.Budgeted.Count == 0) return (0, null, "");
        var lastFinished = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var month = new DateOnly(pot.StartDate.Year, pot.StartDate.Month, 1);
        month = Later(month, pot.LeftoverClaimedThrough?.AddDays(1));

        decimal total = 0;
        var months = new List<string>();
        for (; month.AddMonths(1).AddDays(-1) <= lastFinished; month = month.AddMonths(1))
        {
            var end = month.AddMonths(1).AddDays(-1);
            var inMonth = data.Transactions.Where(t => t.Date >= month && t.Date <= end).ToList();
            if (inMonth.Count == 0) continue;
            var budget = data.Budgeted.Sum(c => c.MonthlyBudget!.Value);
            var ids = data.Budgeted.Select(c => c.Id).ToHashSet();
            var spent = AnalyticsService.NetSpent(inMonth.Where(t => t.CategoryId is { } id && ids.Contains(id)));
            var left = budget - spent;
            if (left > 0)
            {
                total += left;
                months.Add($"{month:MMM}");
            }
        }

        return (decimal.Round(total, 2), months.Count > 0 ? lastFinished : null, $"Budget left in {string.Join(", ", months)}");
    }

    /// <summary>A bonus per day without spending, up to yesterday and only as far as the data goes (no imports ≠ no spending).</summary>
    public static (decimal Amount, DateOnly? Through, string Detail) NoSpend(SavingsPot pot, Data data, DateOnly today)
    {
        if (pot.NoSpendBonus is not { } bonus || bonus <= 0 || data.LastDataDate is not { } last) return (0, null, "");
        var from = Later(pot.StartDate, pot.NoSpendClaimedThrough?.AddDays(1));
        var to = Earlier(today.AddDays(-1), last);
        if (to < from) return (0, null, "");

        var days = CountNoSpendDays(data, from, to);
        return (days * bonus, days > 0 ? to : null, days == 1 ? "1 no-spend day" : $"{days} no-spend days");
    }

    public static ChallengeView Evaluate(PotChallenge c, Data data, DateOnly today)
    {
        var length = c.EndDate.DayNumber - c.StartDate.DayNumber + 1;
        var covered = data.LastDataDate is { } last ? Earlier(Earlier(today, last), c.EndDate) : c.StartDate.AddDays(-1);

        if (c.Kind == ChallengeKind.SkipCategory)
        {
            var name = c.Category?.Name ?? "a category";
            var title = $"Skip {name} until {c.EndDate:d MMM}";
            var slips = data.Transactions.Any(t => t.Type == TransactionType.Expense && t.CategoryId == c.CategoryId && t.Date >= c.StartDate && t.Date <= c.EndDate);
            var clean = Math.Clamp(covered.DayNumber - c.StartDate.DayNumber + 1, 0, length);
            var status = c.ClaimedAt is not null ? "claimed" : slips ? "lost" : clean >= length ? "won" : "active";
            return new ChallengeView(c.Id, c.Kind, title, c.CategoryId, null, c.StartDate, c.EndDate, c.Reward, status, slips ? 0 : clean, length);
        }
        else
        {
            var goal = c.Target ?? 1;
            var title = $"{goal} no-spend days by {c.EndDate:d MMM}";
            var countTo = Earlier(covered, today.AddDays(-1));
            var count = countTo >= c.StartDate ? CountNoSpendDays(data, c.StartDate, countTo) : 0;
            var status = c.ClaimedAt is not null ? "claimed" : count >= goal ? "won" : today > c.EndDate && covered >= c.EndDate ? "lost" : "active";
            return new ChallengeView(c.Id, c.Kind, title, null, goal, c.StartDate, c.EndDate, c.Reward, status, Math.Min(count, goal), goal);
        }
    }

    private static int CountNoSpendDays(Data data, DateOnly from, DateOnly to)
    {
        data.SpendDays ??= data.Transactions.Where(t => t.Type == TransactionType.Expense).Select(t => t.Date).ToHashSet();
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (!data.SpendDays.Contains(d)) count++;
        }
        return count;
    }

    // ---------- Validation ----------

    private async Task ApplyAsync(SavingsPot pot, UpsertPotRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new AppException("Pot names need 1 to 60 characters.");
        if (req.TargetAmount <= 0 || req.TargetAmount > 1_000_000_000_000m) throw new AppException("Set a goal amount above zero.");
        if (req.Deadline is { } deadline && deadline <= pot.StartDate) throw new AppException("The deadline has to be after the start date.");
        if (!HexColor().IsMatch(req.Color ?? "")) throw new AppException("Pick a color like #3f8f5f.");
        if (req.RoundUpTo is { } step && !RoundUpSteps.Contains(step)) throw new AppException($"Round up to one of {string.Join(", ", RoundUpSteps)}.");
        if (req.NoSpendBonus is { } bonus && (bonus <= 0 || bonus > 1_000_000m)) throw new AppException("The no-spend bonus should be above zero.");

        pot.Name = name;
        pot.TargetAmount = decimal.Round(req.TargetAmount, 2);
        pot.Deadline = req.Deadline;
        pot.Color = req.Color!;
        pot.Icon = Trim(req.Icon, 40) ?? "piggy-bank";
        pot.LinkPattern = Trim(req.LinkPattern, 60);
        pot.RoundUpTo = req.RoundUpTo;
        pot.CollectLeftover = req.CollectLeftover;
        pot.NoSpendBonus = req.NoSpendBonus is { } b2 ? decimal.Round(b2, 2) : null;
        pot.IsArchived = req.IsArchived;

        // Round-ups and leftover budget are the same money whichever pot shows them, so only one pot collects each.
        if (pot.RoundUpTo is not null)
        {
            await db.SavingsPots.Where(p => p.Id != pot.Id && p.RoundUpTo != null).ExecuteUpdateAsync(s => s.SetProperty(p => p.RoundUpTo, (int?)null), ct);
        }
        if (pot.CollectLeftover)
        {
            await db.SavingsPots.Where(p => p.Id != pot.Id && p.CollectLeftover).ExecuteUpdateAsync(s => s.SetProperty(p => p.CollectLeftover, false), ct);
        }
    }

    private static DateOnly Later(DateOnly a, DateOnly? b) => b is { } x && x > a ? x : a;
    private static DateOnly Earlier(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string? Trim(string? s, int max)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }

    private static AppException NotFound() => new("Savings pot not found.", 404);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
