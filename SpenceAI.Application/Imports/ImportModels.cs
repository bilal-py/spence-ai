using System.ComponentModel;
using System.Text.Json.Serialization;
using SpenceAI.Domain;

namespace SpenceAI.Application.Imports;

// ---- What the LLM returns (structured output) ----
// Every property is `required` so the generated JSON schema lists them as required. Without that,
// constrained decoders (Ollama) accept "{}" and small models happily return it.

public class ExtractionResult
{
    public required List<ExtractedTransaction> Transactions { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ExtractedKind>))]
public enum ExtractedKind
{
    expense,
    income,
    transfer,
    refund,
}

public class ExtractedTransaction
{
    [Description("Transaction date as yyyy-MM-dd")]
    public required string Date { get; set; }

    [Description("The date exactly as printed in the source, e.g. '03/11/2026' or '18 Apr'")]
    public required string DatePrinted { get; set; }

    [Description("The amount exactly as printed on this one line, as a positive number without currency symbols. Never add or combine amounts from different lines.")]
    public required decimal Amount { get; set; }

    public required ExtractedKind Type { get; set; }

    [Description("Description as printed on the statement")]
    public required string Description { get; set; }

    [Description("Clean merchant or payee name, e.g. 'Uber' or 'Amazon'")]
    public required string? Merchant { get; set; }

    [Description("One of the provided category names, or a short new name if none fits")]
    public required string? Category { get; set; }
}

/// <summary>An extracted transaction plus the statement text it came from (null for images), used to check its amount.</summary>
public record ExtractedWithSource(ExtractedTransaction Item, string? SourceText);

// ---- What the review screen works with ----

public record StagedRow(
    int Index,
    DateOnly Date,
    decimal Amount,
    TransactionType Type,
    string Description,
    string? Merchant,
    int? CategoryId,
    string? NewCategoryName,
    bool IsDuplicate,
    bool Include,
    string? MatchedRule,
    string? Warning = null,
    string? DuplicateOf = null,
    // Which way the money moved, as read from the statement (before any rule). Rules are learned per direction.
    MoneyDirection? Direction = null);

public record ImportBatchDto(
    Guid Id,
    string FileName,
    TransactionSource Source,
    ImportStatus Status,
    string? Error,
    int TransactionCount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<StagedRow>? Rows);

public record CommitRow(
    DateOnly Date,
    decimal Amount,
    TransactionType Type,
    string Description,
    string? Merchant,
    int? CategoryId,
    string? NewCategoryName,
    // The staged row this came from, so a correction is compared with the right suggestion.
    int? Index = null,
    // False means "just this once": the row is saved as edited, but no rule is learned from it.
    bool Remember = true);

public record CommitImportRequest(IReadOnlyList<CommitRow> Rows);
