using System.Globalization;
using System.Text.RegularExpressions;

namespace SpenceAI.Application.Imports;

public enum AmountEvidence
{
    /// <summary>The amount isn't printed anywhere in the statement text: the model invented, merged or misread it.</summary>
    NotFound,
    Found,
    /// <summary>The amount is printed marked as a credit ("+ ₹4.00", "1,960.00 Cr") on this transaction's line.</summary>
    FoundAsCredit,
}

/// <summary>
/// Checks the model's amounts against the statement text it read. Models sometimes merge lines
/// (404 + 4 waiver → 408) or ignore a "+" sign; the printed text is the ground truth for both.
/// </summary>
public static partial class AmountCheck
{
    // A number not glued to other digits: 4, 4.00, 1960.00, 1,960.00, 1,23,456.78.
    [GeneratedRegex(@"(?<![\d.,])(\d[\d,]*)(?:\.(\d{1,2}))?(?![\d])")]
    private static partial Regex Number();

    // "+" then at most a short currency marker. PDFs often extract ₹ as an odd glyph, so any few non-digits are allowed.
    [GeneratedRegex(@"\+\s*[^\d\s+]{0,4}\.?\s*$")]
    private static partial Regex PlusBefore();

    [GeneratedRegex(@"^\s*(?:CR|Cr|cr)\b")]
    private static partial Regex CreditAfter();

    /// <param name="description">The transaction's printed description. When given, the sign is read from the lines
    /// that mention it, so the same amount elsewhere on the statement (a "4" in a table) doesn't interfere.</param>
    public static AmountEvidence Check(string text, decimal amount, string? description = null)
    {
        var target = decimal.Round(Math.Abs(amount), 2);
        var occurrences = new List<(bool Credit, bool OnDescriptionLine)>();
        var tokens = MerchantKey.From(description, maxTokens: 3).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (Match m in Number().Matches(text))
        {
            var digits = m.Groups[1].Value.Replace(",", "");
            if (digits.Length == 0 || digits.Length > 15) continue;
            var raw = m.Groups[2].Success ? $"{digits}.{m.Groups[2].Value}" : digits;
            if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value != target) continue;

            var before = text[Math.Max(0, m.Index - 12)..m.Index];
            var after = text[(m.Index + m.Length)..Math.Min(text.Length, m.Index + m.Length + 6)];
            var credit = PlusBefore().IsMatch(before) || CreditAfter().IsMatch(after);

            var lineStart = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var lineEnd = text.IndexOf('\n', m.Index);
            var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)].ToUpperInvariant();
            var onDescriptionLine = tokens.Length > 0 && tokens.All(line.Contains);

            occurrences.Add((credit, onDescriptionLine));
        }

        if (occurrences.Count == 0) return AmountEvidence.NotFound;

        var relevant = occurrences.Any(o => o.OnDescriptionLine) ? occurrences.Where(o => o.OnDescriptionLine).ToList() : occurrences;
        return relevant.All(o => o.Credit) ? AmountEvidence.FoundAsCredit : AmountEvidence.Found;
    }
}
