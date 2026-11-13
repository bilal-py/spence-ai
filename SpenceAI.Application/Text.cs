using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpenceAI.Domain;

namespace SpenceAI.Application;

/// <summary>
/// Reduces noisy bank descriptions ("POS 4411 UBER *TRIP HELP.UBER.COM") to a short, stable key ("UBER TRIP")
/// used for merchant rules, merchant grouping and recurring-payment detection.
/// </summary>
public static class MerchantKey
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "POS", "PURCHASE", "DEBIT", "CREDIT", "CARD", "VISA", "MASTERCARD", "MC", "TXN", "TRANSACTION",
        "ONLINE", "INTL", "INT", "REF", "NO", "ID", "THE", "AT", "WWW", "COM", "IN", "LTD", "INC", "LLC",
        "PVT", "PRIVATE", "LIMITED", "UPI", "IMPS", "NEFT", "ACH", "DR", "CR",
    };

    public static string From(string? merchantOrDescription, int maxTokens = 2)
    {
        if (string.IsNullOrWhiteSpace(merchantOrDescription)) return string.Empty;

        var cleaned = new StringBuilder(merchantOrDescription.Length);
        foreach (var ch in merchantOrDescription.ToUpperInvariant())
        {
            cleaned.Append(char.IsLetter(ch) ? ch : ' ');
        }

        var tokens = cleaned.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !Noise.Contains(t))
            .Take(maxTokens);

        return string.Join(' ', tokens);
    }

    public static bool Matches(string key, string pattern) =>
        key.Length > 0 && pattern.Length > 0 &&
        (key == pattern || key.StartsWith(pattern + " ", StringComparison.Ordinal));
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
