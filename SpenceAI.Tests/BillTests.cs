using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Categories;
using SpenceAI.Application.QuickAdd;
using SpenceAI.Application.Quests;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

public sealed class BillTests : IDisposable
{
    // A Saturday, mid-month.
    private static readonly DateOnly Today = new(2026, 11, 14);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly BillService _bills;

    public BillTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        CategoryTemplates.SeedAsync(_db).GetAwaiter().GetResult();
        _db.Settings.Add(new AppSettings());
        _db.SaveChanges();
        _bills = new BillService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private int CategoryId(string name) => _db.Categories.Single(c => c.Name == name).Id;

    private Transaction Spend(DateOnly date, decimal amount, string description, string? category = null, TransactionType type = TransactionType.Expense)
    {
        var tx = new Transaction
        {
            Date = date, Amount = amount, Type = type, Description = description,
            CategoryId = category is null ? null : CategoryId(category),
        };
        _db.Transactions.Add(tx);
        _db.SaveChanges();
        return tx;
    }

    private static UpsertBillRequest Bill(string name = "Netflix", decimal amount = 649, DateOnly? first = null, BillCadence cadence = BillCadence.Monthly,
        int interval = 1, DateOnly? ends = null, string? pattern = null, bool varies = false, TransactionType type = TransactionType.Expense, bool subscription = true,
        int? category = null) =>
        new(name, amount, varies, type, cadence, interval, first ?? new DateOnly(2026, 8, 5), ends, pattern, category, null, false, subscription, 3, false, null);

    [Fact]
    public void Due_dates_follow_the_first_date_and_keep_month_ends()
    {
        var bill = new Bill { FirstDueDate = new DateOnly(2026, 1, 31), Cadence = BillCadence.Monthly, Interval = 1 };
        var dates = BillService.Occurrences(bill, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)).ToList();
        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], dates);

        var weekly = new Bill { FirstDueDate = new DateOnly(2026, 11, 2), Cadence = BillCadence.Weekly, Interval = 2, EndsOn = new DateOnly(2026, 11, 30) };
        Assert.Equal(3, BillService.Occurrences(weekly, DateOnly.MinValue, DateOnly.MaxValue).Count());
    }

    [Fact]
    public async Task Imported_charges_tick_off_due_dates_and_a_missed_one_shows_as_late()
    {
        Spend(new DateOnly(2026, 8, 5), 649, "NETFLIX.COM 4412");
        Spend(new DateOnly(2026, 9, 7), 649, "POS NETFLIX COM MUMBAI");
        // October missed; something else imported later, so the data covers it.
        Spend(new DateOnly(2026, 11, 13), 120, "Chai point");
        await _bills.CreateAsync(Bill(), Today, default);

        var overview = await _bills.GetOverviewAsync(Today, default);
        var bill = Assert.Single(overview.Bills);
        var byDate = bill.Recent.ToDictionary(o => o.DueDate, o => o.Status);
        Assert.Equal("paid", byDate[new DateOnly(2026, 8, 5)]);
        Assert.Equal("paid", byDate[new DateOnly(2026, 9, 5)]);
        Assert.Equal("late", byDate[new DateOnly(2026, 10, 5)]);
        Assert.Equal("late", byDate[new DateOnly(2026, 11, 5)]);
        Assert.Equal(new DateOnly(2026, 11, 5), bill.Next?.DueDate); // the recent miss comes first
        Assert.Equal(0, bill.OnTimeStreak);
        Assert.Contains(overview.DueSoon, d => d.DueDate == new DateOnly(2026, 11, 5) && d.Status == "late");
    }

    [Fact]
    public async Task Mark_paid_logs_a_transaction_and_undo_removes_it()
    {
        var created = await _bills.CreateAsync(Bill(name: "Rent", amount: 15000, first: new DateOnly(2026, 11, 1), subscription: false), Today, default);
        var paid = await _bills.MarkPaidAsync(created.Id, new MarkPaidRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null, Today), default);

        var occurrence = paid.Recent.Single(o => o.DueDate == new DateOnly(2026, 11, 1));
        Assert.Equal("paid", occurrence.Status);
        Assert.True(occurrence.ByHand);
        var tx = await _db.Transactions.SingleAsync();
        Assert.Equal(15000, tx.Amount);
        Assert.Equal("Rent", tx.Description);

        var undone = await _bills.UnmarkAsync(created.Id, new DateOnly(2026, 11, 1), Today, default);
        Assert.Empty(_db.Transactions);
        Assert.NotEqual("paid", undone.Recent.Single(o => o.DueDate == new DateOnly(2026, 11, 1)).Status);

        await Assert.ThrowsAsync<AppException>(() => _bills.MarkPaidAsync(created.Id, new MarkPaidRequest(new DateOnly(2026, 11, 2), null, null, Today), default));
    }

    [Fact]
    public async Task Deleting_the_logged_transaction_unpays_the_bill()
    {
        var created = await _bills.CreateAsync(Bill(name: "Rent", amount: 15000, first: new DateOnly(2026, 11, 1)), Today, default);
        await _bills.MarkPaidAsync(created.Id, new MarkPaidRequest(new DateOnly(2026, 11, 1), null, null, Today), default);
        _db.ChangeTracker.Clear();
        _db.Transactions.Remove(await _db.Transactions.SingleAsync());
        await _db.SaveChangesAsync();

        Assert.Empty(_db.BillMarks);
    }

    [Fact]
    public async Task A_price_rise_is_flagged_and_still_counts_as_paid()
    {
        Spend(new DateOnly(2026, 10, 5), 649, "Netflix");
        Spend(new DateOnly(2026, 11, 5), 799, "Netflix");
        var bill = await _bills.CreateAsync(Bill(first: new DateOnly(2026, 10, 5)), Today, default);

        Assert.Equal("paid", bill.Recent.Single(o => o.DueDate == new DateOnly(2026, 11, 5)).Status);
        Assert.NotNull(bill.PriceChange);
        Assert.Equal(649, bill.PriceChange.From);
        Assert.Equal(799, bill.PriceChange.To);
        Assert.Equal(2, bill.OnTimeStreak);
    }

    [Fact]
    public async Task Card_bill_transfers_match_and_bank_names_count()
    {
        Spend(new DateOnly(2026, 11, 3), 23450, "HDFC CREDIT CARD PAYMENT", type: TransactionType.Transfer);
        var bill = await _bills.CreateAsync(Bill(name: "HDFC card", amount: 20000, first: new DateOnly(2026, 11, 5), pattern: "HDFC", varies: true,
            type: TransactionType.Transfer, subscription: false), Today, default);
        Assert.Equal("HDFC", bill.MatchPattern);
        Assert.Equal("paid", bill.Recent.Single(o => o.DueDate == new DateOnly(2026, 11, 5)).Status);
    }

    [Fact]
    public async Task Repeat_charges_become_suggestions_until_tracked_or_dismissed()
    {
        foreach (var m in new[] { 8, 9, 10, 11 }) Spend(new DateOnly(2026, m, 3), 119, "SPOTIFY INDIA", "Subscriptions");
        var suggestion = Assert.Single((await _bills.GetOverviewAsync(Today, default)).Suggestions);
        Assert.Equal("SPOTIFY INDIA", suggestion.Pattern);
        Assert.Equal(new DateOnly(2026, 12, 3), suggestion.NextDue);
        Assert.Equal(new DateOnly(2026, 8, 3), suggestion.FirstDue);
        Assert.True(suggestion.LooksLikeSubscription);

        await _bills.DismissSuggestionAsync(suggestion.Pattern, default);
        Assert.Empty((await _bills.GetOverviewAsync(Today, default)).Suggestions);
    }

    [Fact]
    public async Task Safe_to_spend_takes_out_spending_and_unpaid_expense_bills()
    {
        (await _db.Categories.SingleAsync(c => c.Name == "Dining")).MonthlyBudget = 10000;
        (await _db.Categories.SingleAsync(c => c.Name == "Health")).MonthlyBudget = 2000;
        await _db.SaveChangesAsync();
        Spend(new DateOnly(2026, 11, 2), 3000, "Dinner", "Dining");
        Spend(new DateOnly(2026, 11, 1), 15000, "Rent", "Housing"); // no budget: not counted against the budgets
        await _bills.CreateAsync(Bill(name: "Gym", amount: 1500, first: new DateOnly(2026, 11, 20), subscription: false, category: CategoryId("Health")), Today, default);
        await _bills.CreateAsync(Bill(name: "SIP", amount: 5000, first: new DateOnly(2026, 11, 25), type: TransactionType.Transfer, subscription: false), Today, default);

        var safe = (await _bills.GetOverviewAsync(Today, default)).SafeToSpend;
        Assert.NotNull(safe);
        Assert.Equal(12000 - 3000 - 1500, safe.Amount); // the SIP is a transfer, not spending
        Assert.Equal(17, safe.DaysLeft);
        Assert.Equal("budgets", safe.BaselineSource);
    }

    [Fact]
    public async Task Cancelled_subscriptions_flag_charges_after_cancelling()
    {
        var bill = await _bills.CreateAsync(Bill(first: new DateOnly(2026, 10, 5)), Today, default);
        await _bills.ReviewAsync(bill.Id, new ReviewBillRequest("cancel", new DateOnly(2026, 10, 20)), default);
        Spend(new DateOnly(2026, 11, 5), 649, "Netflix");

        var overview = await _bills.GetOverviewAsync(Today, default);
        var view = Assert.Single(overview.Bills);
        Assert.Equal(new DateOnly(2026, 11, 5), view.ChargedAfterCancel);
        Assert.Null(view.Next);
        Assert.Equal(649 * 12, overview.CancelledYearly);
    }

    [Fact]
    public async Task Calendar_file_has_a_repeating_event_with_an_alarm()
    {
        await _bills.CreateAsync(Bill(name: "Rent", amount: 15000, first: new DateOnly(2026, 11, 1), subscription: false), Today, default);
        var ics = await _bills.CalendarAsync("INR", default);
        Assert.Contains("RRULE:FREQ=MONTHLY;INTERVAL=1", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261101", ics);
        Assert.Contains("SUMMARY:Rent 15\\,000 INR", ics);
        Assert.Contains("BEGIN:VALARM", ics);
    }

    [Fact]
    public async Task Quests_record_completions_once_and_build_levels()
    {
        var quests = new QuestService(_db);
        Spend(Today, 50, "Chai");
        var first = await quests.GetAsync(Today, default);
        Assert.Equal(QuestService.QuestsPerWeek, first.Quests.Count);
        Assert.Equal(first.JustCompleted.Count, first.Quests.Count(q => q.Done));

        var second = await quests.GetAsync(Today, default);
        Assert.Empty(second.JustCompleted);
        Assert.Equal(first.Level.Xp, second.Level.Xp);

        Assert.Equal(1, QuestService.Level(0).Number);
        Assert.Equal(2, QuestService.Level(60).Number);
        Assert.Equal(7, QuestService.Level(1000).Number);
        Assert.Equal(8, QuestService.Level(1400).Number);
    }

    [Fact]
    public void Check_in_streak_counts_back_from_today_or_yesterday()
    {
        var logged = new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-3), Today.AddDays(-10) };
        var data = new QuestService.QuestData([], [], [], [], logged, null, false);
        var streak = QuestService.Streaks(Today, data).Single(s => s.Key == "check-in");
        Assert.Equal(3, streak.Current);
        Assert.Equal(3, streak.Best);
    }
}

public class QuickAddRepeatTests
{
    // A Saturday.
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static ParsedLine One(string text) => Assert.Single(QuickAddParser.Parse(text, Today, []));

    [Theory]
    [InlineData("netflix 649 monthly on 5th", BillCadence.Monthly, 1, 2026, 12, 5)]
    [InlineData("rent 15k monthly on the 20th", BillCadence.Monthly, 1, 2026, 11, 20)]
    [InlineData("gym 1500 every month", BillCadence.Monthly, 1, 2026, 11, 14)]
    [InlineData("insurance 12000 every 3 months on 1st", BillCadence.Monthly, 3, 2026, 12, 1)]
    [InlineData("cleaner 500 weekly on monday", BillCadence.Weekly, 1, 2026, 11, 16)]
    [InlineData("domain 899 yearly", BillCadence.Yearly, 1, 2026, 11, 14)]
    public void Reads_repeats_and_the_next_due_date(string text, BillCadence cadence, int interval, int y, int m, int d)
    {
        var r = One(text);
        Assert.NotNull(r.Repeat);
        Assert.Equal(cadence, r.Repeat.Cadence);
        Assert.Equal(interval, r.Repeat.Interval);
        Assert.Equal(new DateOnly(y, m, d), r.Repeat.NextDue);
        Assert.Empty(r.Unsure);
    }

    [Fact]
    public void Leaves_ordinary_lines_alone()
    {
        Assert.Null(One("netflix 649").Repeat);
        Assert.Equal("Netflix", One("netflix 649 monthly on 5th").Merchant);
    }
}
