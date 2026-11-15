using System.Globalization;
using System.Text.RegularExpressions;
using SpenceAI.Domain;

namespace SpenceAI.Application.QuickAdd;

/// <summary>One entry of quick-add text, read with plain rules (no AI).</summary>
/// <param name="TypeStated">A word in the line said the type ("got", "refund", "card bill"), so a rule mustn't override it.</param>
/// <param name="Unsure">Fields the user should double-check: "amount", "merchant", "account".</param>
/// <param name="Repeat">Set when the line says it repeats ("monthly on 5th"): it becomes a bill instead of a transaction.</param>
public record ParsedLine(
    string Text,
    DateOnly Date,
    decimal? Amount,
    TransactionType Type,
    bool TypeStated,
    string Merchant,
    int? AccountId,
    IReadOnlyList<string> Unsure,
    Repeat? Repeat = null);

/// <summary>How a quick-add line repeats, and its next due date (today or later).</summary>
public record Repeat(BillCadence Cadence, int Interval, DateOnly NextDue);

/// <summary>
/// Reads lines like "chai 40 upi", "uber 250 yesterday", "got 80k salary" or "amazon refund 499 on 12 nov".
/// Several entries can go on one line, separated by commas, semicolons or new lines ("chai 40, auto 120").
/// </summary>
public static partial class QuickAddParser
{
    public const int MaxRows = 20;

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tues"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thurs"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
    };

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Words that say money came in and aren't worth keeping in the description.</summary>
    private static readonly HashSet<string> IncomeVerbs = new(StringComparer.OrdinalIgnoreCase) { "got", "received", "receive", "recd", "credited", "earned" };

    /// <summary>Income words that also name what the money was, so they stay as the description.</summary>
    private static readonly HashSet<string> IncomeNouns = new(StringComparer.OrdinalIgnoreCase) { "salary", "bonus", "dividend", "stipend", "paycheck" };

    private static readonly HashSet<string> RefundWords = new(StringComparer.OrdinalIgnoreCase) { "refund", "refunded", "cashback", "reversal", "reversed", "returned" };

    private static readonly HashSet<string> TransferWords = new(StringComparer.OrdinalIgnoreCase) { "transfer", "transferred", "moved" };

    private static readonly Dictionary<string, AccountKind> KindWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cash"] = AccountKind.Cash,
        ["card"] = AccountKind.Card, ["cc"] = AccountKind.Card, ["credit"] = AccountKind.Card, ["debit"] = AccountKind.Card,
        ["upi"] = AccountKind.Bank, ["bank"] = AccountKind.Bank, ["neft"] = AccountKind.Bank, ["imps"] = AccountKind.Bank,
        ["gpay"] = AccountKind.Wallet, ["phonepe"] = AccountKind.Wallet, ["paytm"] = AccountKind.Wallet, ["wallet"] = AccountKind.Wallet,
    };

    /// <summary>Generic words in account names that shouldn't pick an account on their own.</summary>
    private static readonly HashSet<string> GenericAccountWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank", "card", "account", "credit", "debit", "savings", "salary", "current", "wallet", "cash", "my",
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "paid", "pay", "spent", "spend", "bought", "buy", "for", "on", "at", "to", "from", "via", "using", "with", "by",
        "rs", "rs.", "inr", "rupees", "bucks", "a", "an", "the", "last",
    };

    public static List<ParsedLine> Parse(string text, DateOnly today, IReadOnlyCollection<Account> accounts)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var active = accounts.Where(a => !a.IsArchived).ToList();
        return Separator().Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Take(MaxRows)
            .Select(s => ParseOne(s, today, active))
            .ToList();
    }

    private static ParsedLine ParseOne(string text, DateOnly today, List<Account> accounts)
    {
        var tokens = text.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
        var used = new bool[tokens.Count];
        var unsure = new List<string>();

        var repeat = ReadRepeat(tokens, used, today);
        var date = ReadDate(tokens, used, today);
        if (repeat is not null && repeat.NextDue == today && date != today)
        {
            // "gym 1500 monthly 3 nov": the date given is a past due date; repeat it forward.
            var next = date;
            for (var n = 1; next < today && n < 1000; n++) next = Step(date, repeat.Cadence, repeat.Interval * n);
            repeat = repeat with { NextDue = next };
        }

        // Type words.
        TransactionType? type = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            var w = Word(tokens[i]);
            var next = i + 1 < tokens.Count ? Word(tokens[i + 1]) : "";
            if ((w is "card" or "cc" or "credit") && next is "bill" or "payment")
            {
                type = TransactionType.Transfer; // "card bill" stays as the description
                i++;
            }
            else if (IncomeVerbs.Contains(w)) { type ??= TransactionType.Income; used[i] = true; }
            else if (IncomeNouns.Contains(w)) type ??= TransactionType.Income;
            else if (RefundWords.Contains(w)) { type = TransactionType.Refund; used[i] = true; }
            else if (TransferWords.Contains(w)) { type = TransactionType.Transfer; used[i] = true; }
        }

        // Amount: prefer one with a currency mark, then the largest number ("2 samosa 30" is 30, "500 for 2 tickets" is 500).
        var amounts = new List<(int Index, decimal Value, bool Marked)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!used[i] && TryAmount(tokens[i], out var value, out var marked)) amounts.Add((i, value, marked));
        }

        decimal? amount = null;
        if (amounts.Count > 0)
        {
            var pick = amounts.OrderByDescending(a => a.Marked).ThenByDescending(a => a.Value).First();
            amount = pick.Value;
            used[pick.Index] = true;
            if (amounts.Count > 1 && !pick.Marked) unsure.Add("amount");
        }
        else unsure.Add("amount");

        // Account: a word from an account's name ("hdfc"), else a kind word ("cash", "card", "upi").
        int? accountId = null;
        for (var i = 0; i < tokens.Count && accountId is null; i++)
        {
            if (used[i]) continue;
            var w = Word(tokens[i]);
            if (w.Length < 3 || GenericAccountWords.Contains(w)) continue;
            var byName = accounts.Where(a => NameWords(a.Name).Contains(w)).ToList();
            if (byName.Count == 0) continue;
            accountId = byName[0].Id;
            used[i] = true;
            if (byName.Count > 1) unsure.Add("account");
        }

        var kindWordSeen = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            var w = Word(tokens[i]);
            if (!KindWords.TryGetValue(w, out var kind)) continue;
            var next = i + 1 < tokens.Count ? Word(tokens[i + 1]) : "";
            if (next is "bill" or "payment") continue;
            used[i] = true;
            if (accountId is not null || kindWordSeen) continue;
            kindWordSeen = true;

            // UPI apps sit on a bank account, so fall back to the bank when there's no wallet.
            var ofKind = accounts.Where(a => a.Kind == kind).ToList();
            if (ofKind.Count == 0 && kind == AccountKind.Wallet) ofKind = accounts.Where(a => a.Kind == AccountKind.Bank).ToList();
            if (ofKind.Count == 0 && w == "upi") ofKind = accounts.Where(a => a.Kind == AccountKind.Wallet).ToList();
            if (ofKind.Count > 0)
            {
                accountId = ofKind[0].Id;
                if (ofKind.Count > 1) unsure.Add("account");
            }
        }

        // What's left is the merchant. Filler words go, unless nothing else would be left.
        var rest = Enumerable.Range(0, tokens.Count).Where(i => !used[i]).Select(i => tokens[i]).ToList();
        var meaningful = rest.Where(t => !Fillers.Contains(Word(t))).ToList();
        var merchant = string.Join(' ', (meaningful.Count > 0 ? meaningful : rest).Select(Tidy)).Trim();

        var resolvedType = type ?? TransactionType.Expense;
        if (merchant.Length == 0)
        {
            merchant = resolvedType switch
            {
                TransactionType.Income => "Income",
                TransactionType.Transfer => "Transfer",
                TransactionType.Refund => "Refund",
                _ => "",
            };
            if (merchant.Length == 0) unsure.Add("merchant");
        }

        if (repeat is not null && resolvedType is not (TransactionType.Expense or TransactionType.Transfer)) repeat = null;
        return new ParsedLine(text, repeat?.NextDue ?? date, amount, resolvedType, type is not null, merchant, accountId, unsure, repeat);
    }

    // ---------- Repeats ("monthly on 5th", "every 2 weeks", "yearly") ----------

    private static Repeat? ReadRepeat(List<string> tokens, bool[] used, DateOnly today)
    {
        BillCadence? cadence = null;
        var interval = 1;
        for (var i = 0; i < tokens.Count && cadence is null; i++)
        {
            var w = Word(tokens[i]);
            var next = i + 1 < tokens.Count ? Word(tokens[i + 1]) : "";
            var after = i + 2 < tokens.Count ? Word(tokens[i + 2]) : "";
            (BillCadence, int)? found = w switch
            {
                "monthly" or "/month" or "/mo" => (BillCadence.Monthly, 1),
                "weekly" or "/week" or "/wk" => (BillCadence.Weekly, 1),
                "fortnightly" or "biweekly" => (BillCadence.Weekly, 2),
                "quarterly" => (BillCadence.Monthly, 3),
                "yearly" or "annually" or "annual" or "/year" or "/yr" => (BillCadence.Yearly, 1),
                _ => null,
            };
            if (found is not null)
            {
                used[i] = true;
                (cadence, interval) = found.Value;
                break;
            }

            if (w is "every" or "each" or "per")
            {
                var n = int.TryParse(next, out var parsed) && parsed is > 0 and <= 52 ? parsed : (int?)null;
                var unit = n is null ? next : after;
                BillCadence? c = unit switch
                {
                    "month" or "months" or "mo" => BillCadence.Monthly,
                    "week" or "weeks" or "wk" => BillCadence.Weekly,
                    "year" or "years" or "yr" => BillCadence.Yearly,
                    _ => null,
                };
                if (c is null) continue;
                cadence = c;
                interval = n ?? 1;
                used[i] = used[i + 1] = true;
                if (n is not null) used[i + 2] = true;
            }
        }
        if (cadence is null) return null;

        // "on 5th", "on the 5th", "5th": the day of the month it's due.
        var nextDue = today;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            var w = Word(tokens[i]);
            var prev = i > 0 ? Word(tokens[i - 1]) : "";
            var prev2 = i > 1 ? Word(tokens[i - 2]) : "";
            var ordinal = OrdinalDay().IsMatch(w);
            var afterOn = prev == "on" || (prev == "the" && prev2 == "on");

            if (cadence == BillCadence.Weekly && Weekdays.TryGetValue(w, out var weekday))
            {
                used[i] = true;
                if (prev == "on") used[i - 1] = true;
                nextDue = today.AddDays(((int)weekday - (int)today.DayOfWeek + 7) % 7);
                break;
            }

            if (cadence != BillCadence.Weekly && (ordinal || afterOn) && DayOf(tokens[i]) is int day && MonthOf(i + 1 < tokens.Count ? Word(tokens[i + 1]) : "") == 0)
            {
                used[i] = true;
                if (prev == "on") used[i - 1] = true;
                if (prev == "the") { used[i - 1] = true; if (prev2 == "on") used[i - 2] = true; }
                var thisMonth = new DateOnly(today.Year, today.Month, Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month)));
                nextDue = thisMonth >= today ? thisMonth : AtDay(today.AddMonths(1), day);
                break;
            }
        }

        return new Repeat(cadence.Value, interval, nextDue);
    }

    private static DateOnly AtDay(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));

    private static DateOnly Step(DateOnly from, BillCadence cadence, int n) => cadence switch
    {
        BillCadence.Weekly => from.AddDays(7 * n),
        BillCadence.Yearly => from.AddYears(n),
        _ => from.AddMonths(n),
    };

    // ---------- Dates ----------

    private static DateOnly ReadDate(List<string> tokens, bool[] used, DateOnly today)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var w = Word(tokens[i]);
            var next = i + 1 < tokens.Count ? Word(tokens[i + 1]) : "";
            var prev = i > 0 ? Word(tokens[i - 1]) : "";

            if (w == "day" && next == "before" && i + 2 < tokens.Count && Word(tokens[i + 2]) is "yesterday" or "yday")
            {
                used[i] = used[i + 1] = used[i + 2] = true;
                return today.AddDays(-2);
            }

            if (w is "today" or "tod" or "tdy")
            {
                used[i] = true;
                return today;
            }

            if (w is "yesterday" or "yday" or "yest" or "ystd")
            {
                used[i] = true;
                return today.AddDays(-1);
            }

            if (Weekdays.TryGetValue(w, out var weekday))
            {
                used[i] = true;
                var back = ((int)today.DayOfWeek - (int)weekday + 7) % 7;
                if (back == 0 && prev == "last") back = 7;
                if (prev == "last") used[i - 1] = true;
                return today.AddDays(-back);
            }

            if (DateOnly.TryParseExact(tokens[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            {
                used[i] = true;
                return iso;
            }

            // "12 nov", "12th nov", "nov 12"
            var month = MonthOf(w);
            if (month > 0)
            {
                int? day = null;
                if (i > 0 && !used[i - 1] && DayOf(tokens[i - 1]) is int before) { day = before; used[i - 1] = true; }
                else if (i + 1 < tokens.Count && DayOf(tokens[i + 1]) is int after) { day = after; used[i + 1] = true; }
                if (day is null) continue;

                used[i] = true;
                var year = today.Year;
                var safeDay = Math.Min(day.Value, DateTime.DaysInMonth(year, month));
                var d = new DateOnly(year, month, safeDay);
                // A date later than today means last year ("28 dec" typed in January).
                if (d > today) d = new DateOnly(year - 1, month, Math.Min(day.Value, DateTime.DaysInMonth(year - 1, month)));
                return d;
            }
        }

        return today;
    }

    private static int MonthOf(string w)
    {
        if (w.Length < 3) return 0;
        var i = Array.IndexOf(Months, w[..3]);
        if (i < 0) return 0;
        // Accept "nov", "november", "sept", but not words that merely start like a month ("market", "decathlon").
        var full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(i + 1).ToLowerInvariant();
        return w.Length == 3 || full.StartsWith(w, StringComparison.Ordinal) || w == "sept" ? i + 1 : 0;
    }

    private static int? DayOf(string token)
    {
        var m = DayToken().Match(token);
        return m.Success && int.TryParse(m.Groups[1].Value, out var d) && d is >= 1 and <= 31 ? d : null;
    }

    // ---------- Amounts ----------

    /// <summary>Reads "40", "1,200", "₹499", "rs.50", "1.2k", "80k", "2.5L", "250/-".</summary>
    public static bool TryAmount(string token, out decimal value, out bool marked)
    {
        value = 0;
        marked = false;
        var m = AmountToken().Match(token);
        if (!m.Success) return false;

        var number = m.Groups["n"].Value.Replace(",", "");
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n <= 0) return false;

        var multiplier = m.Groups["mult"].Value.ToLowerInvariant() switch
        {
            "k" => 1_000m,
            "l" or "lac" or "lakh" or "lakhs" => 100_000m,
            "cr" => 10_000_000m,
            _ => 1m,
        };

        value = decimal.Round(n * multiplier, 2);
        marked = m.Groups["pre"].Success || m.Groups["post"].Success;
        return true;
    }

    // ---------- Helpers ----------

    private static string Word(string token) => token.Trim('.', ',', '!', '?', ':', ';', '(', ')').ToLowerInvariant();

    private static HashSet<string> NameWords(string name) =>
        name.ToLowerInvariant().Split((char[])[' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Capitalizes words typed in lowercase ("chai" → "Chai"), leaving "KFC" or "iPhone" as typed.</summary>
    private static string Tidy(string token)
    {
        var t = token.Trim(',', ';', '!', '?');
        return t.Length > 0 && t.All(c => !char.IsLetter(c) || char.IsLower(c)) ? char.ToUpperInvariant(t[0]) + t[1..] : t;
    }

    /// <summary>Commas split entries, except inside numbers ("1,200").</summary>
    [GeneratedRegex(@"[;\n]|,(?!\d)")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^(?<pre>₹|rs\.?|inr|\$|€|£)?(?<n>\d{1,3}(?:,\d{2,3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)(?<mult>k|l|lac|lakhs?|cr)?(?<post>/-|rs|inr)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountToken();

    [GeneratedRegex(@"^(\d{1,2})(?:st|nd|rd|th)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DayToken();

    [GeneratedRegex(@"^\d{1,2}(?:st|nd|rd|th)$", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalDay();
}
