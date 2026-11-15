using SpenceAI.Domain;

namespace SpenceAI.Application.Imports;

/// <summary>
/// How learned <see cref="MerchantRule"/>s are matched and phrased. Each row has two keys:
/// a general one (the merchant, 2 words: "AMAZON PAY") that rules are learned on first, and a specific one
/// (the printed description, 4 words: "AMAZON PAY BILLPAYMENT") used for exceptions to a general rule.
/// </summary>
public static class RuleBook
{
    public const int SpecificWords = 4;

    public static MoneyDirection? DirectionOf(TransactionType type) => type switch
    {
        TransactionType.Expense => MoneyDirection.Out,
        TransactionType.Income or TransactionType.Refund => MoneyDirection.In,
        _ => null,
    };

    public static (string General, string Specific) Keys(string? merchant, string description) =>
        (MerchantKey.From(string.IsNullOrWhiteSpace(merchant) ? description : merchant),
         MerchantKey.From(description, SpecificWords));

    /// <summary>The longest matching pattern wins; a rule for the row's direction beats a direction-less one.</summary>
    /// <param name="direction">Null when unknown (e.g. the AI said "transfer"); then rules of either direction match.</param>
    public static MerchantRule? Match(IEnumerable<MerchantRule> rules, string? merchant, string description, MoneyDirection? direction)
    {
        var (general, specific) = Keys(merchant, description);
        return rules
            .Where(r => r.Direction is null || direction is null || r.Direction == direction)
            .Where(r => MerchantKey.Matches(general, r.Pattern) || MerchantKey.Matches(specific, r.Pattern))
            .OrderByDescending(r => MerchantKey.WordCount(r.Pattern))
            .ThenByDescending(r => r.Direction is not null)
            .FirstOrDefault();
    }

    /// <summary>
    /// The user's past corrections for merchants that appear in <paramref name="statementText"/>, phrased for the prompt.
    /// Only rules whose words are already in the text are included, so nothing leaves the machine that the
    /// statement itself wasn't already sending.
    /// </summary>
    public static IReadOnlyList<string> HintsFor(string statementText, IEnumerable<MerchantRule> rules, int max = 20)
    {
        var words = MerchantKey.Tokens(statementText).ToHashSet(StringComparer.Ordinal);
        return rules
            .Where(r => r.Pattern.Length > 0 && r.Pattern.Split(' ').All(words.Contains))
            .OrderByDescending(r => r.UpdatedAt)
            .Take(max)
            .Select(Describe)
            .ToList();
    }

    public static string Describe(MerchantRule r)
    {
        var direction = r.Direction switch { MoneyDirection.Out => " (money out)", MoneyDirection.In => " (money in)", _ => "" };
        var outcome = r.Type == TransactionType.Transfer
            ? "transfer"
            : string.Join(", ", new[] { r.Type?.ToString().ToLowerInvariant(), r.Category?.Name }.Where(s => s is not null));
        return $"{r.Pattern}{direction} -> {(outcome.Length > 0 ? outcome : "as the statement says")}";
    }
}
