using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Categories;
using SpenceAI.Application.Pet;
using SpenceAI.Application.Savings;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

public sealed class SavingsTests : IDisposable
{
    // A Saturday, mid-month.
    private static readonly DateOnly Today = new(2026, 11, 14);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly SavingsService _savings;

    public SavingsTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        CategoryTemplates.SeedAsync(_db).GetAwaiter().GetResult();
        _db.Settings.Add(new AppSettings());
        _db.SaveChanges();
        _savings = new SavingsService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private int CategoryId(string name) => _db.Categories.Single(c => c.Name == name).Id;

    private void Spend(DateOnly date, decimal amount, string merchant = "Shop", string? category = null, TransactionType type = TransactionType.Expense)
    {
        _db.Transactions.Add(new Transaction
        {
            Date = date, Amount = amount, Type = type, Description = merchant, Merchant = merchant,
            CategoryId = category is null ? null : CategoryId(category),
        });
        _db.SaveChanges();
    }

    private static UpsertPotRequest Pot(string name = "Goa trip", decimal target = 30000, DateOnly? start = null, DateOnly? deadline = null,
        string? link = null, int? roundUp = null, bool leftover = false, decimal? noSpend = null) =>
        new(name, target, start ?? new DateOnly(2026, 11, 1), deadline, "#1e5a46", "plane", link, roundUp, leftover, noSpend);

    [Fact]
    public async Task Pace_spreads_what_is_left_over_the_time_left()
    {
        var pot = await _savings.CreateAsync(Pot(target: 12000, start: new DateOnly(2026, 11, 1), deadline: new DateOnly(2027, 5, 1)), Today, default);
        pot = await _savings.AddContributionAsync(pot.Id, new AddContributionRequest(500, Today, "Birthday money"), Today, default);

        Assert.Equal(500, pot.Saved);
        Assert.NotNull(pot.Pace);
        Assert.Equal(11500, pot.Pace.Remaining);
        Assert.Equal("behind", pot.Pace.Status); // ~7% of the way through the time, 4% of the money
        Assert.True(pot.Pace.PerMonth is > 2000 and < 2200, $"{pot.Pace.PerMonth}"); // 11500 over 168 days

        pot = await _savings.AddContributionAsync(pot.Id, new AddContributionRequest(11500, Today, null), Today, default);
        Assert.Equal("done", pot.Pace!.Status);
        Assert.Equal(1.0, pot.Progress);
    }

    [Fact]
    public async Task Linked_transfers_count_live_from_the_start_date()
    {
        Spend(new DateOnly(2026, 10, 20), 5000, "RD instalment HDFC", type: TransactionType.Transfer); // before the pot started
        Spend(new DateOnly(2026, 11, 5), 5000, "RD instalment HDFC", type: TransactionType.Transfer);
        Spend(new DateOnly(2026, 11, 6), 5000, "Card bill", type: TransactionType.Transfer);
        var pot = await _savings.CreateAsync(Pot(link: "rd instalment"), Today, default);

        Assert.Equal(5000, pot.Saved);
        Assert.Equal(5000, pot.BySource[PotSource.Transfer]);
        var entry = Assert.Single(pot.History);
        Assert.Equal(PotSource.Transfer, entry.Source);
        Assert.NotNull(entry.TransactionId);
    }

    [Fact]
    public async Task Round_ups_are_claimed_once()
    {
        Spend(new DateOnly(2026, 11, 3), 42);   // +8
        Spend(new DateOnly(2026, 11, 4), 199);  // +1
        Spend(new DateOnly(2026, 11, 5), 300);  // +0
        var pot = await _savings.CreateAsync(Pot(roundUp: 10), Today, default);

        var offer = Assert.Single(pot.Available);
        Assert.Equal(PotSource.RoundUp, offer.Source);
        Assert.Equal(9, offer.Amount);

        pot = await _savings.ClaimAsync(pot.Id, new ClaimRequest(PotSource.RoundUp, Today), default);
        Assert.Equal(9, pot.Saved);
        Assert.Empty(pot.Available);
        await Assert.ThrowsAsync<AppException>(() => _savings.ClaimAsync(pot.Id, new ClaimRequest(PotSource.RoundUp, Today), default));
    }

    [Fact]
    public async Task Only_one_pot_collects_round_ups()
    {
        var first = await _savings.CreateAsync(Pot("First", roundUp: 10), Today, default);
        await _savings.CreateAsync(Pot("Second", roundUp: 50), Today, default);

        var pots = await _savings.ListAsync(Today, default);
        Assert.Null(pots.Single(p => p.Id == first.Id).RoundUpTo);
        Assert.Equal(50, pots.Single(p => p.Name == "Second").RoundUpTo);
    }

    [Fact]
    public async Task No_spend_days_only_count_as_far_as_the_data_goes()
    {
        Spend(new DateOnly(2026, 11, 2), 100);
        Spend(new DateOnly(2026, 11, 6), 0.5m, "Bank fee"); // last data: the 6th, so the 7th–13th are unknown, not "no spend"
        var pot = await _savings.CreateAsync(Pot(start: new DateOnly(2026, 11, 1), noSpend: 50), Today, default);

        var offer = Assert.Single(pot.Available);
        Assert.Equal(PotSource.NoSpend, offer.Source);
        Assert.Equal(4 * 50, offer.Amount); // 1, 3, 4, 5
    }

    [Fact]
    public async Task Leftover_budget_is_offered_for_finished_months()
    {
        var dining = await _db.Categories.SingleAsync(c => c.Name == "Dining");
        dining.MonthlyBudget = 5000;
        await _db.SaveChangesAsync();
        Spend(new DateOnly(2026, 10, 10), 3200, "Cafe", "Dining");
        Spend(new DateOnly(2026, 11, 10), 100, "Cafe", "Dining"); // November isn't over, so it doesn't count yet

        var pot = await _savings.CreateAsync(Pot(start: new DateOnly(2026, 10, 1), leftover: true), Today, default);
        var offer = Assert.Single(pot.Available);
        Assert.Equal(1800, offer.Amount);

        pot = await _savings.ClaimAsync(pot.Id, new ClaimRequest(PotSource.Leftover, Today), default);
        Assert.Equal(1800, pot.Saved);
        Assert.Empty(pot.Available);
    }

    [Fact]
    public async Task Skip_category_challenge_is_won_only_when_the_data_covers_the_end()
    {
        var pot = await _savings.CreateAsync(Pot(), Today, default);
        pot = await _savings.AddChallengeAsync(pot.Id,
            new AddChallengeRequest(ChallengeKind.SkipCategory, CategoryId("Dining"), null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), 700), Today, default);
        Assert.Equal("active", Assert.Single(pot.Challenges).Status);

        Spend(new DateOnly(2026, 11, 8), 40, "Bus");
        pot = await _savings.GetAsync(pot.Id, Today, default);
        var won = Assert.Single(pot.Challenges);
        Assert.Equal("won", won.Status);

        pot = await _savings.ClaimChallengeAsync(pot.Id, won.Id, Today, default);
        Assert.Equal(700, pot.Saved);
        Assert.Equal("claimed", Assert.Single(pot.Challenges).Status);
        await Assert.ThrowsAsync<AppException>(() => _savings.ClaimChallengeAsync(pot.Id, won.Id, Today, default));
    }

    [Fact]
    public async Task Spending_in_the_category_loses_the_challenge()
    {
        Spend(new DateOnly(2026, 11, 3), 250, "Pizza", "Dining");
        var pot = await _savings.CreateAsync(Pot(), Today, default);
        pot = await _savings.AddChallengeAsync(pot.Id,
            new AddChallengeRequest(ChallengeKind.SkipCategory, CategoryId("Dining"), null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 700), Today, default);
        Assert.Equal("lost", Assert.Single(pot.Challenges).Status);
    }

    [Fact]
    public async Task Pet_nudges_about_a_won_challenge_first_and_respects_dismissal()
    {
        var pet = new PetService(_db, _savings, new SpenceAI.Application.Bills.BillService(_db), new SpenceAI.Application.Quests.QuestService(_db));
        Spend(new DateOnly(2026, 11, 13), 40, "Bus");
        var pot = await _savings.CreateAsync(Pot(), Today, default);
        await _savings.AddChallengeAsync(pot.Id,
            new AddChallengeRequest(ChallengeKind.NoSpendDays, null, 3, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 300), Today, default);

        var state = await pet.GetAsync(Today, default);
        Assert.Equal("proud", state.Mood);
        Assert.Equal("challenge-won", state.Nudge?.Key);
        Assert.Equal(300, state.Nudge?.Amount);

        await pet.DismissAsync(Today, default);
        Assert.Null((await pet.GetAsync(Today, default)).Nudge);
        Assert.NotNull((await pet.GetAsync(Today.AddDays(1), default)).Nudge);
    }

    [Fact]
    public void Pet_mood_follows_the_pace_against_budgets()
    {
        var categories = new List<Category> { new() { Id = 1, Name = "Dining", Kind = TransactionType.Expense, MonthlyBudget = 3000 } };
        Transaction Tx(decimal amount) => new() { Date = new DateOnly(2026, 11, 10), Amount = amount, Type = TransactionType.Expense, CategoryId = 1 };

        // 14 of 30 days in: the pace for a 3000 month is 1400.
        Assert.Equal("cheerful", PetService.Mood("Sprig", [Tx(600)], categories, [], Today).Mood);
        Assert.Equal("content", PetService.Mood("Sprig", [Tx(1400)], categories, [], Today).Mood);
        Assert.Equal("watchful", PetService.Mood("Sprig", [Tx(2500)], categories, [], Today).Mood);
        Assert.Equal("sleepy", PetService.Mood("Sprig", [], categories, [], Today).Mood);
    }

    [Fact]
    public async Task Wrapped_counts_no_spend_days_streaks_and_new_merchants()
    {
        Spend(new DateOnly(2026, 9, 5), 100, "Old Cafe", "Dining");
        Spend(new DateOnly(2026, 10, 1), 300, "Old Cafe", "Dining");
        Spend(new DateOnly(2026, 10, 2), 200, "Old Cafe", "Dining");
        Spend(new DateOnly(2026, 10, 9), 2000, "New Gym", "Health");
        Spend(new DateOnly(2026, 10, 31), 50, "Old Cafe", "Dining");
        var wrapped = await new WrappedService(_db).GetAsync(2026, 10, Today, default);

        Assert.Equal(2550, wrapped.Spent);
        Assert.Equal(100, wrapped.PreviousSpent);
        Assert.Equal(31, wrapped.DaysCovered);
        Assert.Equal(27, wrapped.NoSpendDays);
        Assert.Equal(21, wrapped.LongestNoSpendStreak); // 10th–30th
        Assert.Equal("New Gym", wrapped.TopMerchant?.Name);
        Assert.Equal("Old Cafe", wrapped.Regular?.Name);
        Assert.Equal(["New Gym"], wrapped.NewMerchantNames);
        Assert.Equal(new DateOnly(2026, 10, 9), wrapped.BiggestDay?.Date);
    }
}
