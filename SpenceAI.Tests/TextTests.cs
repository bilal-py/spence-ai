using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Imports;
using SpenceAI.Domain;

namespace SpenceAI.Tests;

public class MerchantKeyTests
{
    [Theory]
    [InlineData("POS 4411 UBER *TRIP HELP.UBER.COM", "UBER TRIP")]
    [InlineData("Uber", "UBER")]
    [InlineData("AMAZON PAY IN FLIGHTS", "AMAZON PAY")]
    [InlineData("UPI/123456/SWIGGY LTD", "SWIGGY")]
    [InlineData("UPI/Amazon Pay/amazonpaybill@apl/Request fr/AXIS BANK/630049704981/APL01a17bf913c3631a4f34aae44976a", "AMAZON PAY")]
    [InlineData("HDFC BANK", "HDFC BANK")]
    [InlineData("NEFT-HDFC0001234-ACME PAYROLL", "ACME PAYROLL")]
    [InlineData("1234 5678", "")]
    [InlineData(null, "")]
    public void Normalizes_noisy_descriptions(string? input, string expected) =>
        Assert.Equal(expected, MerchantKey.From(input));

    [Fact]
    public void Upi_handle_name_is_kept_and_ids_bank_and_rail_words_dropped() =>
        Assert.Equal("AMAZON PAY AMAZONPAYBILL",
            MerchantKey.From("UPI/Amazon Pay/amazonpaybill@apl/Request fr/AXIS BANK/630049704981/APL01a17bf913c3631a4f34aae44976a", maxTokens: 4));

    [Fact]
    public void Matches_exact_or_prefix_on_word_boundary()
    {
        Assert.True(MerchantKey.Matches("UBER TRIP", "UBER"));
        Assert.True(MerchantKey.Matches("UBER", "UBER"));
        Assert.False(MerchantKey.Matches("UBEREATS", "UBER"));
        Assert.False(MerchantKey.Matches("", "UBER"));
    }
}

public class FingerprintTests
{
    [Fact]
    public void Same_transaction_with_cosmetic_differences_matches()
    {
        var a = TransactionFingerprint.Compute(new DateOnly(2026, 4, 18), 14.5m, TransactionType.Expense, "UBER *TRIP 8812");
        var b = TransactionFingerprint.Compute(new DateOnly(2026, 4, 18), 14.50m, TransactionType.Expense, "Uber trip");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_amount_or_day_differs()
    {
        var a = TransactionFingerprint.Compute(new DateOnly(2026, 4, 18), 14.5m, TransactionType.Expense, "Uber");
        Assert.NotEqual(a, TransactionFingerprint.Compute(new DateOnly(2026, 4, 19), 14.5m, TransactionType.Expense, "Uber"));
        Assert.NotEqual(a, TransactionFingerprint.Compute(new DateOnly(2026, 4, 18), 14.6m, TransactionType.Expense, "Uber"));
    }
}

public class ExtractorHelperTests
{
    [Fact]
    public void Csv_chunks_repeat_the_header()
    {
        var csv = "Date,Description,Amount\n" + string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"2026-01-01,Coffee shop number {i},4.50"));
        var chunks = TransactionExtractor.ChunkCsv(csv);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith("Date,Description,Amount\n", c));
        Assert.All(chunks, c => Assert.True(c.Length <= TransactionExtractor.MaxChunkChars + 100));
        Assert.Equal(2000, chunks.Sum(c => c.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1));
    }

    [Fact]
    public void Pages_are_grouped_under_the_limit()
    {
        var pages = Enumerable.Range(0, 10).Select(i => string.Join('\n', Enumerable.Repeat($"page {i} line of text", 100))).ToList();
        var chunks = TransactionExtractor.ChunkPages(pages);
        Assert.All(chunks, c => Assert.True(c.Length <= TransactionExtractor.MaxChunkChars));
        Assert.Contains("page 9", chunks[^1]);
    }

    [Theory]
    [InlineData("2026-04-18")]
    [InlineData("2026-04-18T00:00:00")]
    [InlineData("18/04/2026")]
    public void Parses_common_date_formats(string value)
    {
        Assert.True(TransactionExtractor.TryParseDate(value, out var date));
        Assert.Equal(new DateOnly(2026, 4, 18), date);
    }

    private static ExtractedTransaction Tx(string printed, string modelDate = "1900-01-01") => new()
    {
        Date = modelDate, DatePrinted = printed, Amount = 1, Type = ExtractedKind.expense, Description = "x", Merchant = null, Category = null,
    };

    [Fact]
    public void Ambiguous_dates_use_the_tightest_reading()
    {
        // 03/11..07/11: day-first spans 4 days in November, month-first spans 4 months.
        var dates = TransactionExtractor.ResolveDates([Tx("03/11/2026"), Tx("05/11/2026"), Tx("07/11/2026")], cultureDayFirst: false);
        Assert.Equal(new DateOnly(2026, 11, 3), dates[0]);
        Assert.Equal(new DateOnly(2026, 11, 7), dates[2]);
    }

    [Fact]
    public void A_part_above_12_settles_the_order_for_the_whole_statement()
    {
        var dates = TransactionExtractor.ResolveDates([Tx("04/05/2026"), Tx("04/25/2026")], cultureDayFirst: true);
        Assert.Equal(new DateOnly(2026, 4, 5), dates[0]);
        Assert.Equal(new DateOnly(2026, 4, 25), dates[1]);
    }

    [Fact]
    public void Non_numeric_printed_dates_keep_the_models_date()
    {
        var dates = TransactionExtractor.ResolveDates([Tx("18 Apr", "2026-04-18"), Tx("garbage", "not a date")]);
        Assert.Equal(new DateOnly(2026, 4, 18), dates[0]);
        Assert.Null(dates[1]);
    }

    [Theory]
    [InlineData(ExtractedKind.expense, TransactionType.Expense)]
    [InlineData(ExtractedKind.income, TransactionType.Income)]
    [InlineData(ExtractedKind.transfer, TransactionType.Transfer)]
    [InlineData(ExtractedKind.refund, TransactionType.Refund)]
    public void Maps_types(ExtractedKind value, TransactionType expected) =>
        Assert.Equal(expected, TransactionExtractor.ParseType(value));
}

public class AmountCheckTests
{
    [Theory]
    [InlineData("SUPREME PETROLEUM ₹ 404.00", 404, AmountEvidence.Found)]
    [InlineData("PETRO SURCHARGE WAIVER + ₹ 4.00", 4, AmountEvidence.FoundAsCredit)]
    [InlineData("PAYMENT RECEIVED 1,960.00 Cr", 1960, AmountEvidence.FoundAsCredit)]
    [InlineData("REFUND +Rs. 1,23,456.78", 123456.78, AmountEvidence.FoundAsCredit)]
    [InlineData("SUPREME PETROLEUM ₹ 404.00", 408, AmountEvidence.NotFound)]
    [InlineData("FEE 4.06", 4, AmountEvidence.NotFound)]
    [InlineData("A ₹ 4.00\nB + ₹ 4.00", 4, AmountEvidence.Found)]
    public void Reads_the_printed_amount_and_sign(string text, double amount, AmountEvidence expected) =>
        Assert.Equal(expected, AmountCheck.Check(text, (decimal)amount));

    [Theory]
    [InlineData("PETRO SURCHARGE WAIVER +  4.00")] // rupee sign extracted as a private-use glyph
    [InlineData("PETRO SURCHARGE WAIVER +` 4.00")]
    [InlineData("PETRO SURCHARGE WAIVER +4.00")]
    public void Reads_a_plus_whatever_the_currency_glyph(string text) =>
        Assert.Equal(AmountEvidence.FoundAsCredit, AmountCheck.Check(text, 4m));

    [Fact]
    public void Reads_the_sign_from_the_descriptions_own_line()
    {
        const string statement = "Reward points earned 4.00\n03/11/2026 SUPREME PETROLEUMKANPUR NAGAR ₹ 404.00\n03/11/2026 PETRO SURCHARGE WAIVER + ₹ 4.00\n";
        Assert.Equal(AmountEvidence.FoundAsCredit, AmountCheck.Check(statement, 4m, "PETRO SURCHARGE WAIVER"));
        Assert.Equal(AmountEvidence.Found, AmountCheck.Check(statement, 404m, "SUPREME PETROLEUMKANPUR NAGAR"));
    }
}

public class RecurringTests
{
    [Fact]
    public void Detects_monthly_subscription_and_ignores_one_offs()
    {
        var asOf = new DateOnly(2026, 6, 20);
        var txns = new List<Transaction>();
        for (var m = 1; m <= 6; m++)
        {
            txns.Add(new Transaction { Date = new DateOnly(2026, m, 3), Amount = 15.49m, Merchant = "Netflix", Description = "NETFLIX.COM" });
        }
        txns.Add(new Transaction { Date = new DateOnly(2026, 5, 10), Amount = 300m, Merchant = "IKEA", Description = "IKEA" });
        txns.Add(new Transaction { Date = new DateOnly(2026, 6, 11), Amount = 20m, Merchant = "IKEA", Description = "IKEA" });
        // Weekly groceries with varying amounts: frequent, not a subscription.
        for (var w = 0; w < 24; w++)
        {
            txns.Add(new Transaction { Date = new DateOnly(2026, 1, 4).AddDays(w * 7), Amount = 60m + w % 4 * 3, Merchant = "Whole Foods", Description = "WHOLEFDS" });
        }
        // A café visited a few times with a coincidentally similar amount.
        foreach (var (m, amt) in new[] { (3, 12m), (4, 30m), (4, 12.5m), (5, 9m), (5, 12.2m), (6, 41m) })
        {
            txns.Add(new Transaction { Date = new DateOnly(2026, m, 15), Amount = amt, Merchant = "Blue Bottle", Description = "BLUE BOTTLE" });
        }

        var recurring = AnalyticsService.FindRecurring(txns, asOf);

        var netflix = Assert.Single(recurring);
        Assert.Equal("Netflix", netflix.Merchant);
        Assert.Equal(15.49m, netflix.TypicalAmount);
        Assert.Equal(6, netflix.Months);
    }
}
