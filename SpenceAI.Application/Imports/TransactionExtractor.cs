using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using SpenceAI.Domain;

namespace SpenceAI.Application.Imports;

/// <summary>Uses the configured LLM to pull transactions out of statement text or a receipt image.</summary>
public class TransactionExtractor
{
    /// <summary>Keeps each request well inside small local models' context windows.</summary>
    public const int MaxChunkChars = 9_000;

    private static readonly ChatOptions Options = new() { Temperature = 0 };

    /// <param name="rules">The user's learned rules. Those for merchants in a chunk are added to its prompt as hints.</param>
    public async Task<List<ExtractedWithSource>> FromTextChunksAsync(
        IChatClient client, IReadOnlyList<string> chunks, IReadOnlyCollection<Category> categories, CancellationToken ct,
        IReadOnlyCollection<MerchantRule>? rules = null)
    {
        var all = new List<ExtractedWithSource>();
        foreach (var chunk in chunks)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt(categories, rules is null ? [] : RuleBook.HintsFor(chunk, rules))),
                new(ChatRole.User, $"<statement>\n{chunk}\n</statement>"),
            };
            all.AddRange((await AskAsync(client, messages, ct)).Select(t => new ExtractedWithSource(t, chunk)));
        }

        return all;
    }

    public async Task<List<ExtractedWithSource>> FromImageAsync(
        IChatClient client, byte[] image, string mediaType, IReadOnlyCollection<Category> categories, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt(categories)),
            new(ChatRole.User,
            [
                new TextContent(
                    "This image is a receipt, bill or statement. Extract its transactions. " +
                    "For a receipt, return one transaction for the grand total, using the store as merchant."),
                new DataContent(image, mediaType),
            ]),
        };
        return (await AskAsync(client, messages, ct)).Select(t => new ExtractedWithSource(t, null)).ToList();
    }

    private static async Task<List<ExtractedTransaction>> AskAsync(IChatClient client, List<ChatMessage> messages, CancellationToken ct)
    {
        var response = await client.GetResponseAsync<ExtractionResult>(messages, Options, cancellationToken: ct);
        if (!response.TryGetResult(out var result))
        {
            throw new AppException("The AI model returned something that isn't a transaction list. Try again or switch to a stronger model.");
        }

        return result.Transactions;
    }

    private static string SystemPrompt(IReadOnlyCollection<Category> categories, IReadOnlyList<string>? hints = null)
    {
        var corrections = hints is { Count: > 0 }
            ? "\n\nThe user has corrected these merchants before. Follow them for matching lines:\n" + string.Join("\n", hints.Select(h => "- " + h))
            : "";
        var spending = string.Join(", ", categories.Where(c => c.Kind != TransactionType.Income).Select(c => c.Name));
        var income = string.Join(", ", categories.Where(c => c.Kind == TransactionType.Income).Select(c => c.Name));
        return $"""
            You extract financial transactions from bank statements, credit card statements, CSV exports and receipts.

            Rules:
            - One transaction per printed transaction line. Never merge lines, never add or subtract amounts from different lines,
              even when two lines belong together (e.g. a fuel purchase and its surcharge waiver are two separate transactions).
            - Skip opening/closing balances, totals, subtotals, minimum due, interest-rate tables, reward points and marketing text.
            - Date: yyyy-MM-dd. If the year isn't printed next to the transaction, infer it from the statement period.
            - Amount: exactly as printed on that line, as a positive number without currency symbols or thousands separators.
            - Direction: an amount with a leading "+", a trailing "CR"/"Cr", or in a Credit / Deposit column is money coming in.
              Everything else on a credit card statement is money going out.
            - Type:
              - "expense": purchases, bills, fees, charges, withdrawals.
              - "refund": money given back for earlier spending: refunds, reversals, cashback, chargebacks, fee or surcharge waivers.
              - "income": salary, interest earned, dividends, money received from other people.
              - "transfer": paying a credit card bill or moving money between the user's own accounts. On a card statement this is the
                payment received line (e.g. "PAYMENT THANK YOU", "AUTOPAY", "BPPY PYMT", "BBPS PAYMENT", "BILLDESK", "NEFT PAYMENT RECEIVED").
                On a bank statement it's the card bill payment (e.g. "CC PAYMENT", "CREDIT CARD BILL", a card number, "CRED") or a
                transfer to the user's own account.
            - Merchant: a short, clean name (e.g. "UBER *TRIP HELP.UBER.COM" -> "Uber").
            - Category, spelled exactly as listed:
              - expense and refund: one of {spending}. A refund takes the category of the spending it reverses
                (a fuel surcharge waiver is Fuel).
              - income: one of {income}.
              - transfer: null.
              Only if nothing fits, propose a short new category name (1-2 words, sentence case).
            - If there are no transactions, return an empty list.
            """ + corrections;
    }

    // ---- Chunking helpers ----

    /// <summary>Groups PDF pages into chunks below <see cref="MaxChunkChars"/>, splitting oversized pages by line.</summary>
    public static List<string> ChunkPages(IEnumerable<string> pages)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var line in pages.SelectMany(p => p.Split('\n').Append(string.Empty)))
        {
            if (current.Length + line.Length + 1 > MaxChunkChars && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            current.Append(line).Append('\n');
        }

        if (current.ToString().Trim().Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    /// <summary>Splits CSV text into chunks, repeating the header row in each so the model keeps the column meaning.</summary>
    public static List<string> ChunkCsv(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];

        var header = lines[0];
        var chunks = new List<string>();
        var current = new StringBuilder(header).Append('\n');

        foreach (var line in lines.Skip(1))
        {
            if (current.Length + line.Length + 1 > MaxChunkChars)
            {
                chunks.Add(current.ToString());
                current.Clear().Append(header).Append('\n');
            }

            current.Append(line).Append('\n');
        }

        if (current.Length > header.Length + 1) chunks.Add(current.ToString());
        return chunks;
    }

    private static readonly System.Text.RegularExpressions.Regex NumericDate =
        new(@"^\s*(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2}|\d{4})\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// LLMs guess the day/month order of dates like 03/11/2026. This picks one order for the whole statement:
    /// any part above 12 decides it; otherwise the reading whose dates sit closest together wins (a statement
    /// covers about a month); otherwise this machine's regional format decides. Non-numeric dates keep the model's value.
    /// </summary>
    public static List<DateOnly?> ResolveDates(IReadOnlyList<ExtractedTransaction> items, bool? cultureDayFirst = null)
    {
        var numeric = items
            .Select(i => NumericDate.Match(i.DatePrinted ?? string.Empty))
            .Select(m => m.Success
                ? (A: int.Parse(m.Groups[1].Value), B: int.Parse(m.Groups[2].Value), Y: NormalizeYear(m.Groups[3].Value))
                : ((int A, int B, int Y)?)null)
            .ToList();

        var parts = numeric.Where(n => n is not null).Select(n => n!.Value).ToList();
        bool dayFirst;
        if (parts.Any(p => p.A > 12)) dayFirst = true;
        else if (parts.Any(p => p.B > 12)) dayFirst = false;
        else
        {
            var spanDayFirst = Span(parts.Select(p => Make(p.Y, p.B, p.A)));
            var spanMonthFirst = Span(parts.Select(p => Make(p.Y, p.A, p.B)));
            dayFirst = spanDayFirst != spanMonthFirst
                ? spanDayFirst < spanMonthFirst
                : cultureDayFirst ?? !CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith('M');
        }

        return items.Select((item, i) =>
        {
            if (numeric[i] is { } p)
            {
                var d = dayFirst ? Make(p.Y, p.B, p.A) : Make(p.Y, p.A, p.B);
                if (d is not null) return d;
            }

            return TryParseDate(item.Date, out var parsed) ? parsed : (DateOnly?)null;
        }).ToList();

        static int NormalizeYear(string y) => y.Length == 2 ? 2000 + int.Parse(y) : int.Parse(y);

        static DateOnly? Make(int year, int month, int day) =>
            month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

        static int Span(IEnumerable<DateOnly?> dates)
        {
            var list = dates.ToList();
            if (list.Count == 0 || list.Any(d => d is null)) return int.MaxValue;
            return list.Max(d => d!.Value.DayNumber) - list.Min(d => d!.Value.DayNumber);
        }
    }

    public static bool TryParseDate(string value, out DateOnly date)
    {
        string[] formats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy/MM/dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy"];
        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        return false;
    }

    public static TransactionType ParseType(ExtractedKind kind) => kind switch
    {
        ExtractedKind.income => TransactionType.Income,
        ExtractedKind.transfer => TransactionType.Transfer,
        ExtractedKind.refund => TransactionType.Refund,
        _ => TransactionType.Expense,
    };
}
