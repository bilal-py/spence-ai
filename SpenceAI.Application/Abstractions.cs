using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SpenceAI.Domain;

namespace SpenceAI.Application;

public interface IAppDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<Category> Categories { get; }
    DbSet<MerchantRule> MerchantRules { get; }
    DbSet<Account> Accounts { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<AppSettings> Settings { get; }
    DbSet<SavingsPot> SavingsPots { get; }
    DbSet<PotContribution> PotContributions { get; }
    DbSet<PotChallenge> PotChallenges { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Encrypts secrets (API keys) at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string? Unprotect(string? protectedValue);
}

public record AiConnection(AiProvider Provider, string Model, string OllamaUrl, string? ApiKey);

/// <summary>Builds an <see cref="IChatClient"/> for the configured provider.</summary>
public interface IChatClientFactory
{
    IChatClient Create(AiConnection connection);
    Task<IReadOnlyList<string>> ListOllamaModelsAsync(string ollamaUrl, CancellationToken ct);
}

/// <summary>Turns a PDF into page texts. Throws an <see cref="AppException"/> with a <c>pdf_password_*</c> code for locked files.</summary>
public interface IPdfTextExtractor
{
    IReadOnlyList<string> ExtractPages(Stream pdf, string? password = null);
}

/// <summary>Holds uploaded files until the background worker has processed them.</summary>
public interface IImportFileStore
{
    Task SaveAsync(Guid batchId, Stream content, CancellationToken ct);
    Task<byte[]> ReadAsync(Guid batchId, CancellationToken ct);
    void Delete(Guid batchId);
}

public interface IImportQueue
{
    ValueTask EnqueueAsync(Guid batchId, CancellationToken ct = default);
}

/// <summary>
/// An error caused by bad input or state, safe to show to the user (mapped to HTTP 400/404).
/// <paramref name="code"/> is an optional machine-readable reason the client can react to (sent as ProblemDetails "code").
/// </summary>
public class AppException(string message, int statusCode = 400, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
}

public static class ErrorCodes
{
    public const string PdfPasswordRequired = "pdf_password_required";
    public const string PdfPasswordIncorrect = "pdf_password_incorrect";
}
