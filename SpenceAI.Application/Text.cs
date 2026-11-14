using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpenceAI.Domain;

namespace SpenceAI.Application;

/// <summary>
/// Reduces noisy bank descriptions ("POS 4411 UBER *TRIP HELP.UBER.COM") to a short, stable key ("UBER TRIP")
/// used for merchant rules, merchant grouping and recurring-payment detection.
/// </summary>
/// <remarks>
/// Reference numbers, card numbers and hashes (any token with 3+ digits) are dropped, and so are payment-rail words
/// (UPI, NEFT, "Request fr"). A UPI handle keeps only its name ("amazonpaybill@apl" → AMAZONPAYBILL), since that
/// names the payee more precisely than anything else on the line. Bank names are dropped too, unless nothing else is left.
/// </remarks>
public static class MerchantKey
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "POS", "PURCHASE", "DEBIT", "CREDIT", "CARD", "VISA", "MASTERCARD", "MC", "TXN", "TRANSACTION",
        "ONLINE", "INTL", "INT", "REF", "NO", "ID", "THE", "AT", "WWW", "COM", "IN", "LTD", "INC", "LLC",
        "PVT", "PRIVATE", "LIMITED", "UPI", "IMPS", "NEFT", "RTGS", "ACH", "DR", "CR", "RRN", "UTR",
        "REQUEST", "FR", "FROM", "TO", "VIA", "BY", "FOR", "OF", "AND", "SENT", "COLLECT",
    };

    /// <summary>Dropped only when other words remain, so "HDFC BANK" alone still has a key.</summary>
    private static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    {
        "BANK", "AXIS", "HDFC", "ICICI", "SBI", "KOTAK", "IDFC", "PNB", "CANARA", "INDUSIND", "RBL", "HSBC", "CITI", "CITIBANK", "BARCLAYS",
    };

    private static readonly char[] Separators = [' ', '\t', '\n', '\r', '/', '\\', '|', ',', ';', ':', '*', '#', '(', ')', '[', ']', '_', '-'];

    public static string From(string? merchantOrDescription, int maxTokens = 2) =>
        string.Join(' ', Tokens(merchantOrDescription).Take(maxTokens));

    /// <summary>All meaningful words of a description, in order, without repeats.</summary>
    public static List<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var words = new List<string>();
        foreach (var raw in text.ToUpperInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw;
            var at = part.IndexOf('@');
            if (at >= 0) part = part[..at]; // UPI handle: keep the name, drop the bank's suffix
            if (part.Count(char.IsDigit) >= 3) continue; // reference numbers, card numbers, hashes

            var letters = new StringBuilder(part.Length);
            foreach (var ch in part) letters.Append(char.IsLetter(ch) ? ch : ' ');
            foreach (var word in letters.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length > 1 && !Noise.Contains(word) && !words.Contains(word)) words.Add(word);
            }
        }

        var strong = words.Where(w => !Weak.Contains(w)).ToList();
        return strong.Count > 0 ? strong : words;
    }

    /// <summary>True when <paramref name="key"/> equals <paramref name="pattern"/> or starts with it on a word boundary.</summary>
    public static bool Matches(string key, string pattern) =>
        key.Length > 0 && pattern.Length > 0 &&
        (key == pattern || key.StartsWith(pattern + " ", StringComparison.Ordinal));

    public static int WordCount(string pattern) => pattern.Count(c => c == ' ') + 1;
}

public static class TransactionFingerprint
{
    public static string Compute(DateOnly date, decimal amount, TransactionType type, string description)
    {
        var normalized = MerchantKey.From(description, maxTokens: 6);
        var raw = string.Create(CultureInfo.InvariantCulture, $"{date:yyyyMMdd}|{decimal.Round(amount, 2):0.00}|{type}|{normalized}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 12);
    }
}
