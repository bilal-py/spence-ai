using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Pet;
using SpenceAI.Domain;

namespace SpenceAI.Application.Bills;

public record UpsertBillRequest(
    string Name,
    decimal Amount,
    bool AmountVaries,
    TransactionType Type,
    BillCadence Cadence,
    int Interval,
    DateOnly FirstDueDate,
    DateOnly? EndsOn,
    string? MatchPattern,
    int? CategoryId,
    int? AccountId,
    bool Autopay,
    bool IsSubscription,
    int RemindDaysBefore,
    bool IsPaused,
    string? Notes);

/// <param name="Amount">Defaults to the bill's amount.</param>
/// <param name="PaidOn">Defaults to today.</param>
public record MarkPaidRequest(DateOnly DueDate, DateOnly? PaidOn, decimal? Amount, DateOnly? Today);
public record SkipBillRequest(DateOnly DueDate, DateOnly? Today);
/// <param name="Decision">"keep" | "cancel" | "restore".</param>
public record ReviewBillRequest(string Decision, DateOnly? Today);
public record DismissSuggestionRequest(string Pattern);

/// <summary>Status: "paid" | "skipped" | "upcoming" | "due" | "late" | "unconfirmed" (past due, but no transactions imported that far yet).</summary>
public record OccurrenceView(DateOnly DueDate, string Status, decimal Amount, decimal? PaidAmount, DateOnly? PaidOn, int? TransactionId, bool ByHand);

/// <summary>The latest charge differs from the bill's amount.</summary>
public record PriceChange(decimal From, decimal To, DateOnly Since);

public record BillView(
    int Id,
    string Name,
    decimal Amount,
    bool AmountVaries,
    TransactionType Type,
    BillCadence Cadence,
    int Interval,
    DateOnly FirstDueDate,
    DateOnly? EndsOn,
    string MatchPattern,
    int? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    string? CategoryIcon,
    int? AccountId,
    string? AccountName,
    bool Autopay,
    bool IsSubscription,
    int RemindDaysBefore,
    bool IsPaused,
    DateOnly? CancelledOn,
    DateOnly? LastReviewedOn,
    string? Notes,
    decimal MonthlyCost,
    decimal YearlyCost,
    OccurrenceView? Next,
    IReadOnlyList<OccurrenceView> Recent,
    PriceChange? PriceChange,
    int? PaymentsLeft,
    DateOnly? ChargedAfterCancel,
    int OnTimeStreak);

/// <param name="FirstDue">The earliest due date the past charges cover, so a new bill starts with its history already ticked off.</param>
public record BillSuggestion(string Name, string Pattern, decimal Amount, TransactionType Type, int? CategoryId, string? CategoryName,
    DateOnly NextDue, DateOnly FirstDue, int Months, bool LooksLikeSubscription);

public record CalendarItem(int BillId, string Name, DateOnly DueDate, decimal Amount, string Status, string? Color, string? Icon, bool Autopay);

public record BillsMonth(decimal Total, decimal Paid, decimal Remaining, int Count, int PaidCount);

/// <summary>
/// What's left to spend this month. With budgets: what the budgeted categories have left, minus their bills still due.
/// Without: the usual month minus everything spent so far, minus every expense bill still due. <see cref="BaselineSource"/>: "budgets" | "average".
/// </summary>
public record SafeToSpend(decimal Amount, decimal PerDay, int DaysLeft, decimal Baseline, string BaselineSource, decimal Spent, decimal BillsDue);

public record BillsOverview(
    IReadOnlyList<BillView> Bills,
    BillsMonth ThisMonth,
    decimal MonthlyTotal,
    decimal SubscriptionsYearly,
    decimal CancelledYearly,
    IReadOnlyList<CalendarItem> Calendar,
    IReadOnlyList<CalendarItem> DueSoon,
    IReadOnlyList<BillSuggestion> Suggestions,
    SafeToSpend? SafeToSpend);

/// <summary>
/// Bills and subscriptions. A due date counts as paid when a transaction matching the bill's words lands near it, so importing a
/// statement ticks bills off by itself; "Mark paid" logs the transaction for you. Everything is plain rules, no AI.
/// </summary>
public class BillService(IAppDbContext db)
{
    private static readonly string[] SubscriptionWords =
    [
        "NETFLIX", "SPOTIFY", "PRIME", "HOTSTAR", "YOUTUBE", "APPLE", "ICLOUD", "GOOGLE", "CHATGPT", "OPENAI", "ANTHROPIC", "CLAUDE",
        "ADOBE", "NOTION", "CANVA", "DISNEY", "ZEE", "SONYLIV", "JIOCINEMA", "AUDIBLE", "KINDLE", "MICROSOFT", "XBOX", "PLAYSTATION",
        "GYM", "CULT", "SWIGGY ONE", "ZOMATO GOLD", "LINKEDIN", "DROPBOX", "GITHUB", "DUOLINGO", "HBO", "HULU", "PATREON",
    ];

    // ---------------- Reading ----------------

    public async Task<BillsOverview> GetOverviewAsync(DateOnly today, CancellationToken ct)
    {
        var bills = await LoadBillsAsync(ct);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var historyFrom = monthStart.AddMonths(-13);
        var earliest = bills.Count > 0 ? Min(bills.Min(b => b.FirstDueDate).AddDays(-31), historyFrom) : historyFrom;

        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Category)
            .Where(t => t.Date >= earliest)
            .ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
        var lastData = transactions.Count > 0 ? transactions.Max(t => t.Date) : (DateOnly?)null;

        var views = bills.Select(b => Build(b, transactions, today, lastData)).ToList();
        var active = views.Where(v => !v.IsPaused && v.CancelledOn is null).ToList();

        var calendar = new List<CalendarItem>();
        foreach (var bill in bills.Where(b => !b.IsPaused && b.CancelledOn is null))
        {
            var occurrences = Resolve(bill, transactions, today, lastData);
            var view = views.First(v => v.Id == bill.Id);
            calendar.AddRange(occurrences
                .Where(o => o.DueDate >= monthStart && o.DueDate <= today.AddDays(45))
                .Select(o => new CalendarItem(bill.Id, bill.Name, o.DueDate, o.PaidAmount ?? o.Amount, o.Status, view.CategoryColor, view.CategoryIcon, bill.Autopay)));
        }
        calendar = calendar.OrderBy(c => c.DueDate).ThenBy(c => c.Name).ToList();

        var thisMonth = calendar.Where(c => c.DueDate.Year == today.Year && c.DueDate.Month == today.Month && c.Status != "skipped").ToList();
        var month = new BillsMonth(
            thisMonth.Sum(c => c.Amount),
            thisMonth.Where(c => c.Status == "paid").Sum(c => c.Amount),
            thisMonth.Where(c => c.Status != "paid").Sum(c => c.Amount),
            thisMonth.Count,
            thisMonth.Count(c => c.Status == "paid"));

        var dueSoon = DueSoon(calendar, today);

        var expenseBillsDue = thisMonth
            .Select(c => (Item: c, Bill: bills.First(b => b.Id == c.BillId)))
            .Where(x => x.Item.Status != "paid" && x.Bill.Type == TransactionType.Expense)
            .Select(x => (x.Bill.CategoryId, x.Item.Amount))
            .ToList();

        return new BillsOverview(
            views,
            month,
            decimal.Round(active.Sum(v => v.MonthlyCost), 2),
            decimal.Round(active.Where(v => v.IsSubscription).Sum(v => v.YearlyCost), 2),
            decimal.Round(views.Where(v => v.CancelledOn is not null).Sum(v => v.YearlyCost), 2),
            calendar,
            dueSoon,
            Suggest(bills, transactions, categories, settings, today),
            ComputeSafeToSpend(transactions, categories, today, expenseBillsDue));
    }

    /// <summary>Unpaid due dates worth a reminder: due in the next week, or missed in the last two weeks. Autopay bills only when they're late.</summary>
    public static List<CalendarItem> DueSoon(IEnumerable<CalendarItem> calendar, DateOnly today) =>
        calendar
            .Where(c => c.Status == "late" ? c.DueDate >= today.AddDays(-14)
                : (c.Status is "due" or "upcoming") && c.DueDate <= today.AddDays(7) && !(c.Autopay && c.Status == "upcoming"))
            .OrderBy(c => c.DueDate)
            .ToList();

    public async Task<BillView> GetAsync(int id, DateOnly today, CancellationToken ct)
    {
        var bill = await db.Bills.AsNoTracking().Include(b => b.Category).Include(b => b.Account).Include(b => b.Marks)
            .FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        var from = bill.FirstDueDate.AddDays(-31);
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.Date >= from).ToListAsync(ct);
        var lastData = await db.Transactions.AnyAsync(ct) ? await db.Transactions.MaxAsync(t => t.Date, ct) : (DateOnly?)null;
        return Build(bill, transactions, today, lastData);
    }

    private Task<List<Bill>> LoadBillsAsync(CancellationToken ct) =>
        db.Bills.AsNoTracking().Include(b => b.Category).Include(b => b.Account).Include(b => b.Marks)
            .OrderBy(b => b.CancelledOn != null).ThenBy(b => b.FirstDueDate.Day).ThenBy(b => b.Name)
            .ToListAsync(ct);

    // ---------------- Writing ----------------

    public async Task<BillView> CreateAsync(UpsertBillRequest req, DateOnly today, CancellationToken ct)
    {
        var bill = new Bill();
        await ApplyAsync(bill, req, ct);
        db.Bills.Add(bill);
        await db.SaveChangesAsync(ct);
        return await GetAsync(bill.Id, today, ct);
    }

    public async Task<BillView> UpdateAsync(int id, UpsertBillRequest req, DateOnly today, CancellationToken ct)
    {
        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        await ApplyAsync(bill, req, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        db.Bills.Remove(bill);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Logs the payment as a transaction and ties it to the due date. Undoing it deletes that transaction.</summary>
    public async Task<BillView> MarkPaidAsync(int id, MarkPaidRequest req, CancellationToken ct)
    {
        var today = req.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var bill = await db.Bills.Include(b => b.Marks).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        EnsureDueDate(bill, req.DueDate);
        var amount = decimal.Round(req.Amount ?? bill.Amount, 2);
        if (amount <= 0) throw new AppException("Enter the amount you paid.");

        var paidOn = req.PaidOn ?? today;
        var tx = new Transaction
        {
            Date = paidOn,
            Amount = amount,
            Type = bill.Type,
            Description = bill.Name,
            Merchant = bill.Name,
            CategoryId = bill.Type == TransactionType.Expense ? bill.CategoryId : null,
            AccountId = bill.AccountId,
            Source = TransactionSource.Manual,
            Notes = $"Bill due {req.DueDate:d MMM yyyy}",
        };
        tx.Fingerprint = TransactionFingerprint.Compute(tx.Date, tx.Amount, tx.Type, tx.Description);

        RemoveMark(bill, req.DueDate);
        db.Transactions.Add(tx);
        db.BillMarks.Add(new BillMark { Bill = bill, DueDate = req.DueDate, Kind = BillMarkKind.Paid, Transaction = tx });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task<BillView> SkipAsync(int id, SkipBillRequest req, CancellationToken ct)
    {
        var today = req.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var bill = await db.Bills.Include(b => b.Marks).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        EnsureDueDate(bill, req.DueDate);
        RemoveMark(bill, req.DueDate);
        db.BillMarks.Add(new BillMark { BillId = bill.Id, DueDate = req.DueDate, Kind = BillMarkKind.Skipped });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    /// <summary>Removes what the user said about a due date. A "paid" mark takes the transaction it logged with it.</summary>
    public async Task<BillView> UnmarkAsync(int id, DateOnly dueDate, DateOnly today, CancellationToken ct)
    {
        var mark = await db.BillMarks.Include(m => m.Transaction).FirstOrDefaultAsync(m => m.BillId == id && m.DueDate == dueDate, ct)
            ?? throw new AppException("Nothing to undo for that date.", 404);
        if (mark.Transaction is not null) db.Transactions.Remove(mark.Transaction);
        db.BillMarks.Remove(mark);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task<BillView> ReviewAsync(int id, ReviewBillRequest req, CancellationToken ct)
    {
        var today = req.Today ?? DateOnly.FromDateTime(DateTime.Now);
        var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        switch (req.Decision)
        {
            case "keep":
                bill.LastReviewedOn = today;
                break;
            case "cancel":
                bill.LastReviewedOn = today;
                bill.CancelledOn = today;
                break;
            case "restore":
                bill.CancelledOn = null;
                break;
            default:
                throw new AppException("Choose keep, cancel or restore.");
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, today, ct);
    }

    public async Task DismissSuggestionAsync(string pattern, CancellationToken ct)
    {
        var key = NormalizePattern(pattern);
        if (key.Length == 0) return;
        var settings = await db.Settings.FirstOrDefaultAsync(ct) ?? throw new AppException("Settings are missing.", 500);
        var list = (settings.DismissedBillSuggestions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!list.Contains(key)) list.Add(key);
        // Keep the newest entries if the list ever outgrows the column.
        while (string.Join('\n', list).Length > 3900) list.RemoveAt(0);
        settings.DismissedBillSuggestions = string.Join('\n', list);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Bill bill, UpsertBillRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new AppException("Give the bill a name of 1 to 60 characters.");
        if (req.Amount <= 0 || req.Amount > 1_000_000_000m) throw new AppException("Enter the bill's amount.");
        if (req.Type is not (TransactionType.Expense or TransactionType.Transfer)) throw new AppException("A bill is an expense or a transfer.");
        if (req.Interval is < 1 or > 52) throw new AppException("Repeat every 1 to 52 periods.");
        if (req.EndsOn is { } end && end < req.FirstDueDate) throw new AppException("The last payment can't be before the first.");
        if (req.RemindDaysBefore is < 0 or > 30) throw new AppException("Remind 0 to 30 days before.");
        if (req.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct)) throw new AppException("That category no longer exists.");
        if (req.AccountId is { } aid && !await db.Accounts.AnyAsync(a => a.Id == aid, ct)) throw new AppException("That account no longer exists.");

        var pattern = NormalizePattern(string.IsNullOrWhiteSpace(req.MatchPattern) ? name : req.MatchPattern);
        if (pattern.Length == 0) throw new AppException("Add a word that appears on the bill's transactions, like “Netflix”.");

        bill.Name = name;
        bill.Amount = decimal.Round(req.Amount, 2);
        bill.AmountVaries = req.AmountVaries;
        bill.Type = req.Type;
        bill.Cadence = req.Cadence;
        bill.Interval = req.Interval;
        bill.FirstDueDate = req.FirstDueDate;
        bill.EndsOn = req.EndsOn;
        bill.MatchPattern = pattern;
        bill.CategoryId = req.Type == TransactionType.Expense ? req.CategoryId : null;
        bill.AccountId = req.AccountId;
        bill.Autopay = req.Autopay;
        bill.IsSubscription = req.IsSubscription;
        bill.RemindDaysBefore = req.RemindDaysBefore;
        bill.IsPaused = req.IsPaused;
        bill.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim()[..Math.Min(500, req.Notes.Trim().Length)];
    }

    private static void EnsureDueDate(Bill bill, DateOnly dueDate)
    {
        if (!Occurrences(bill, dueDate, dueDate).Any()) throw new AppException($"{bill.Name} isn't due on {dueDate:d MMM yyyy}.");
    }

    private void RemoveMark(Bill bill, DateOnly dueDate)
    {
        var existing = bill.Marks.FirstOrDefault(m => m.DueDate == dueDate);
        if (existing is null) return;
        if (existing.TransactionId is { } txId)
        {
            var tx = db.Transactions.Local.FirstOrDefault(t => t.Id == txId) ?? db.Transactions.Find(txId);
            if (tx is not null) db.Transactions.Remove(tx);
        }
        db.BillMarks.Remove(existing);
    }

    // ---------------- Schedule and matching ----------------

    /// <summary>The n-th due date (0 = the first). Computed from the first date each time, so a bill due on the 31st stays on month ends.</summary>
    public static DateOnly DueAt(Bill bill, int n) => bill.Cadence switch
    {
        BillCadence.Weekly => bill.FirstDueDate.AddDays(7 * bill.Interval * n),
        BillCadence.Yearly => bill.FirstDueDate.AddYears(bill.Interval * n),
        _ => bill.FirstDueDate.AddMonths(bill.Interval * n),
    };

    public static IEnumerable<DateOnly> Occurrences(Bill bill, DateOnly from, DateOnly to)
    {
        for (var n = 0; n < 5000; n++)
        {
            var due = DueAt(bill, n);
            if (due > to || (bill.EndsOn is { } end && due > end)) yield break;
            if (due >= from) yield return due;
        }
    }

    /// <summary>How far from the due date a payment still counts for it: (days before, days after).</summary>
    public static (int Before, int After) Window(Bill bill) => bill.Cadence switch
    {
        BillCadence.Weekly => (2, 3),
        BillCadence.Yearly => (30, 30),
        _ => (10, 10),
    };

    /// <summary>The bill's words are all on the transaction (merchant or description). Bank names count here, unlike in merchant keys.</summary>
    public static bool Matches(string pattern, Transaction t)
    {
        var words = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        var have = Words(t.Merchant);
        have.UnionWith(Words(t.Description));
        return words.All(have.Contains);
    }

    private static HashSet<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToUpperInvariant()) sb.Append(char.IsLetter(ch) ? ch : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    public static string NormalizePattern(string? text) => string.Join(' ', MerchantKey.Tokens(text).Take(4));

    /// <summary>Every due date from the first through ~6 weeks ahead, each with its status and the payment that settled it.</summary>
    public static List<OccurrenceView> Resolve(Bill bill, IReadOnlyList<Transaction> transactions, DateOnly today, DateOnly? lastData)
    {
        var (before, after) = Window(bill);
        var byHand = bill.Marks.ToDictionary(m => m.DueDate);
        var handTx = bill.Marks.Where(m => m.TransactionId is not null).Select(m => m.TransactionId!.Value).ToHashSet();
        var txById = transactions.ToDictionary(t => t.Id);

        var pool = transactions
            .Where(t => t.Type is TransactionType.Expense or TransactionType.Transfer && !handTx.Contains(t.Id) && Matches(bill.MatchPattern, t))
            .Where(t => bill.AmountVaries || (t.Amount >= bill.Amount * 0.5m && t.Amount <= bill.Amount * 2m))
            .OrderBy(t => t.Date)
            .ToList();
        var used = new HashSet<int>();

        var result = new List<OccurrenceView>();
        foreach (var due in Occurrences(bill, bill.FirstDueDate, today.AddDays(45)))
        {
            if (byHand.TryGetValue(due, out var mark))
            {
                if (mark.Kind == BillMarkKind.Skipped)
                {
                    result.Add(new OccurrenceView(due, "skipped", bill.Amount, null, null, null, true));
                    continue;
                }
                var paid = mark.TransactionId is { } id ? txById.GetValueOrDefault(id) : null;
                result.Add(new OccurrenceView(due, "paid", bill.Amount, paid?.Amount ?? bill.Amount, paid?.Date ?? due, mark.TransactionId, true));
                continue;
            }

            var match = pool
                .Where(t => !used.Contains(t.Id) && t.Date >= due.AddDays(-before) && t.Date <= due.AddDays(after))
                .OrderBy(t => Math.Abs(t.Date.DayNumber - due.DayNumber))
                .FirstOrDefault();
            if (match is not null)
            {
                used.Add(match.Id);
                result.Add(new OccurrenceView(due, "paid", bill.Amount, match.Amount, match.Date, match.Id, false));
                continue;
            }

            string status;
            if (due > today) status = due.DayNumber - today.DayNumber <= bill.RemindDaysBefore ? "due" : "upcoming";
            else if (due == today) status = "due";
            else status = lastData is { } last && last >= due.AddDays(3) ? "late" : "unconfirmed";
            result.Add(new OccurrenceView(due, status, bill.Amount, null, null, null, false));
        }

        return result;
    }

    public static BillView Build(Bill bill, IReadOnlyList<Transaction> transactions, DateOnly today, DateOnly? lastData)
    {
        var occurrences = Resolve(bill, transactions, today, lastData);
        var inactive = bill.IsPaused || bill.CancelledOn is not null;

        // Next thing to act on: a recent missed payment first, then the next unpaid one.
        var next = inactive ? null
            : occurrences.FirstOrDefault(o => (o.Status is "late" or "unconfirmed") && o.DueDate >= today.AddDays(-14))
              ?? occurrences.FirstOrDefault(o => o.DueDate >= today && o.Status is "due" or "upcoming");

        var recent = occurrences.Where(o => o.DueDate <= today.AddDays(31)).TakeLast(8).Reverse().ToList();

        var payments = occurrences.Where(o => o.Status == "paid" && o.PaidAmount is not null && o.PaidOn is not null).ToList();
        PriceChange? priceChange = null;
        var lastPaid = payments.LastOrDefault();
        if (!bill.AmountVaries && lastPaid is not null && lastPaid.PaidOn >= today.AddDays(-75) && bill.Amount > 0 &&
            Math.Abs(lastPaid.PaidAmount!.Value - bill.Amount) / bill.Amount > 0.02m)
        {
            priceChange = new PriceChange(bill.Amount, lastPaid.PaidAmount.Value, lastPaid.PaidOn!.Value);
        }

        int? paymentsLeft = bill.EndsOn is null ? null : occurrences.Count(o => o.DueDate >= today && o.Status != "paid") +
            Occurrences(bill, today.AddDays(46), bill.EndsOn.Value).Count();

        DateOnly? afterCancel = null;
        if (bill.CancelledOn is { } cancelled)
        {
            afterCancel = transactions
                .Where(t => t.Date > cancelled.AddDays(3) && t.Type == TransactionType.Expense && Matches(bill.MatchPattern, t))
                .Select(t => (DateOnly?)t.Date)
                .Max();
        }

        var streak = 0;
        foreach (var o in occurrences.Where(o => o.DueDate <= today).Reverse())
        {
            if (o.Status is "skipped" or "unconfirmed") continue;
            if (o.Status == "paid" && o.PaidOn <= o.DueDate.AddDays(1)) streak++;
            else if (o.Status != "due") break;
        }

        var monthly = MonthlyCost(bill);
        return new BillView(bill.Id, bill.Name, bill.Amount, bill.AmountVaries, bill.Type, bill.Cadence, bill.Interval, bill.FirstDueDate,
            bill.EndsOn, bill.MatchPattern, bill.CategoryId, bill.Category?.Name, bill.Category?.Color, bill.Category?.Icon,
            bill.AccountId, bill.Account?.Name, bill.Autopay, bill.IsSubscription, bill.RemindDaysBefore, bill.IsPaused, bill.CancelledOn,
            bill.LastReviewedOn, bill.Notes, monthly, decimal.Round(monthly * 12, 2), next, recent, priceChange, paymentsLeft, afterCancel, streak);
    }

    public static decimal MonthlyCost(Bill bill) => decimal.Round(bill.Cadence switch
    {
        BillCadence.Weekly => bill.Amount * 52m / 12m / bill.Interval,
        BillCadence.Yearly => bill.Amount / 12m / bill.Interval,
        _ => bill.Amount / bill.Interval,
    }, 2);

    // ---------------- Safe to spend ----------------

    /// <param name="unpaidBills">Expense bills still due this month: their category and amount.</param>
    public static SafeToSpend? ComputeSafeToSpend(List<Transaction> history, List<Category> categories, DateOnly today,
        IReadOnlyList<(int? CategoryId, decimal Amount)> unpaidBills)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var baseline = PetService.Baseline(history, categories, monthStart);
        if (baseline is null or <= 0) return null;

        // Budgets only speak for their own categories, so rent with no budget doesn't eat into the dining budget.
        var budgeted = categories.Where(c => c.Kind == TransactionType.Expense && c.MonthlyBudget is > 0).Select(c => c.Id).ToHashSet();
        bool Counts(int? categoryId) => budgeted.Count == 0 || (categoryId is { } id && budgeted.Contains(id));

        var spent = AnalyticsService.NetSpent(history.Where(t => t.Date >= monthStart && t.Date <= today && Counts(t.CategoryId)));
        var billsDue = unpaidBills.Where(b => Counts(b.CategoryId)).Sum(b => b.Amount);
        var daysLeft = DateTime.DaysInMonth(today.Year, today.Month) - today.Day + 1;
        var amount = decimal.Round(baseline.Value - spent - billsDue, 2);
        return new SafeToSpend(amount, decimal.Round(Math.Max(0, amount) / daysLeft, 2), daysLeft, decimal.Round(baseline.Value, 2),
            budgeted.Count > 0 ? "budgets" : "average", spent, billsDue);
    }

    // ---------------- Suggestions from repeat charges ----------------

    public static List<BillSuggestion> Suggest(List<Bill> bills, List<Transaction> transactions, List<Category> categories, AppSettings settings, DateOnly today)
    {
        var dismissed = (settings.DismissedBillSuggestions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var recent = transactions.Where(t => t.Date >= today.AddMonths(-13) && t.Date <= today).ToList();
        var found = AnalyticsService.FindRecurring(recent.Where(t => t.Type == TransactionType.Expense), today).Select(r => (r, TransactionType.Expense))
            .Concat(AnalyticsService.FindRecurring(recent.Where(t => t.Type == TransactionType.Transfer), today).Select(r => (r, TransactionType.Transfer)));

        var result = new List<BillSuggestion>();
        foreach (var (r, type) in found)
        {
            var pattern = NormalizePattern(MerchantKey.From(r.Merchant));
            if (pattern.Length == 0 || dismissed.Contains(pattern)) continue;
            var probe = new Transaction { Merchant = r.Merchant, Description = r.Merchant };
            if (bills.Any(b => Matches(b.MatchPattern, probe))) continue;

            var next = r.LastDate.AddMonths(1);
            while (next < today.AddDays(-3)) next = next.AddMonths(1);
            var earliest = recent
                .Where(t => t.Type == type && Matches(pattern, t) && Math.Abs(t.Amount - r.TypicalAmount) <= r.TypicalAmount * 0.1m)
                .Min(t => (DateOnly?)t.Date) ?? r.LastDate;
            var back = 0;
            while (back < 24 && next.AddMonths(-(back + 1)) >= earliest.AddDays(-10)) back++;
            var first = next.AddMonths(-back);

            var category = categories.FirstOrDefault(c => c.Name == r.Category);
            var subscription = category?.Name is "Subscriptions" or "Entertainment" ||
                SubscriptionWords.Any(w => (" " + pattern + " ").Contains(" " + w + " ", StringComparison.Ordinal));
            result.Add(new BillSuggestion(r.Merchant, pattern, r.TypicalAmount, type, type == TransactionType.Expense ? category?.Id : null,
                category?.Name, next, first, r.Months, subscription));
        }
        return result.OrderBy(s => s.NextDue).ToList();
    }

    // ---------------- Calendar file ----------------

    /// <summary>An iCalendar file with one repeating all-day event per active bill and an alarm the bill's reminder days before.</summary>
    public async Task<string> CalendarAsync(string currency, CancellationToken ct)
    {
        var bills = await LoadBillsAsync(ct);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Spence//Bills//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Spence bills\r\n");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var b in bills.Where(b => !b.IsPaused && b.CancelledOn is null))
        {
            var freq = b.Cadence switch { BillCadence.Weekly => "WEEKLY", BillCadence.Yearly => "YEARLY", _ => "MONTHLY" };
            var rule = $"FREQ={freq};INTERVAL={b.Interval}";
            if (b.Cadence == BillCadence.Monthly && b.FirstDueDate.Day > 28) rule += ";BYMONTHDAY=-1"; // month ends, as Spence does
            if (b.EndsOn is { } end) rule += $";UNTIL={end:yyyyMMdd}";
            var amount = b.Amount.ToString("#,0.##", CultureInfo.InvariantCulture);
            var summary = $"{b.Name} {(b.AmountVaries ? "~" : "")}{amount} {currency}{(b.Autopay ? " (autopay)" : "")}";

            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:spence-bill-{b.Id}@spence.local\r\n");
            sb.Append($"DTSTAMP:{stamp}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{b.FirstDueDate:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{b.FirstDueDate.AddDays(1):yyyyMMdd}\r\n");
            sb.Append($"RRULE:{rule}\r\n");
            sb.Append($"SUMMARY:{Escape(summary)}\r\n");
            if (b.Notes is not null) sb.Append($"DESCRIPTION:{Escape(b.Notes)}\r\n");
            sb.Append("TRANSP:TRANSPARENT\r\n");
            if (!b.Autopay || b.RemindDaysBefore > 0)
            {
                sb.Append("BEGIN:VALARM\r\nACTION:DISPLAY\r\n");
                sb.Append($"DESCRIPTION:{Escape($"{b.Name} is due")}\r\n");
                // All-day events start at midnight; nudge at 9am on the reminder day.
                var hours = b.RemindDaysBefore * 24 - 9;
                sb.Append(hours >= 0 ? $"TRIGGER:-PT{hours}H\r\n" : "TRIGGER:PT9H\r\n");
                sb.Append("END:VALARM\r\n");
            }
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static AppException NotFound() => new("That bill no longer exists.", 404);
}
