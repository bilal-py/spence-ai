using System.ClientModel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using SpenceAI.Application;
using SpenceAI.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace SpenceAI.Infrastructure;

/// <summary>Data Protection keys live in the local data directory (and are DPAPI-encrypted on Windows).</summary>
public class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SpenceAI.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys were lost or rotated: treat as "no key saved" so the user is asked to re-enter it.
            return null;
        }
    }
}

/// <summary>
/// Ollama loads models with a small context (often 4096 tokens) and, when a prompt doesn't fit, silently drops its
/// beginning: the instructions and the first part of the statement. This asks for a context big enough for a
/// statement chunk, the prompt and the extracted JSON, and turns a cut-off reply into a clear error.
/// </summary>
public sealed class OllamaContextClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <summary>~9k chars of statement (~3.5k tokens) + instructions (~1k) + JSON for ~50 rows (~5k), with room to spare.</summary>
    public const int ContextTokens = 16_384;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new ChatOptions();
        options.AddOllamaOption(OllamaSharp.Models.OllamaOption.NumCtx, ContextTokens);

        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        if (response.FinishReason == ChatFinishReason.Length)
        {
            throw new AppException("The AI model ran out of room before finishing this statement. Try a model with a larger context window.");
        }

        return response;
    }
}

public class ChatClientFactory(IHttpClientFactory httpFactory) : IChatClientFactory
{
    private static readonly Uri GeminiOpenAiEndpoint = new("https://generativelanguage.googleapis.com/v1beta/openai/");

    public IChatClient Create(AiConnection c) => c.Provider switch
    {
        AiProvider.Ollama => new OllamaContextClient(new OllamaApiClient(OllamaHttp(c.OllamaUrl), c.Model)),
        AiProvider.OpenAI => new OpenAIClient(new ApiKeyCredential(c.ApiKey!))
            .GetChatClient(c.Model).AsIChatClient(),
        // Gemini exposes an OpenAI-compatible endpoint, including JSON-schema structured output.
        AiProvider.Gemini => new OpenAIClient(new ApiKeyCredential(c.ApiKey!), new OpenAIClientOptions { Endpoint = GeminiOpenAiEndpoint })
            .GetChatClient(c.Model).AsIChatClient(),
        _ => throw new AppException($"Unsupported AI provider {c.Provider}."),
    };

    public async Task<IReadOnlyList<string>> ListOllamaModelsAsync(string ollamaUrl, CancellationToken ct)
    {
        try
        {
            var client = new OllamaApiClient(OllamaHttp(ollamaUrl));
            var models = await client.ListLocalModelsAsync(ct);
            return models.Select(m => m.Name).Order().ToList();
        }
        catch (HttpRequestException)
        {
            throw new AppException($"Couldn't reach Ollama at {ollamaUrl}. Start Ollama and try again.");
        }
    }

    private HttpClient OllamaHttp(string url)
    {
        // Local models can take minutes on a long statement chunk.
        var http = httpFactory.CreateClient("ollama");
        http.BaseAddress = new Uri(url);
        http.Timeout = TimeSpan.FromMinutes(10);
        return http;
    }
}

public class PdfTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<string> ExtractPages(Stream pdf, string? password = null)
    {
        try
        {
            var options = string.IsNullOrEmpty(password) ? new ParsingOptions() : new ParsingOptions { Password = password };
            using var document = PdfDocument.Open(pdf, options);
            // Layout-aware text keeps table rows on one line, which helps the model pair dates and amounts.
            return document.GetPages()
                .Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p))
                .ToList();
        }
        catch (Exception ex) when (ex is not AppException)
        {
            if (ex is PdfDocumentEncryptedException || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                throw string.IsNullOrEmpty(password)
                    ? new AppException("This PDF is password-protected. Enter its password to read it.", 400, ErrorCodes.PdfPasswordRequired)
                    : new AppException("That password didn't open the PDF. Try again.", 400, ErrorCodes.PdfPasswordIncorrect);
            }

            throw new AppException("This file couldn't be read as a PDF.");
        }
    }
}

public class ImportFileStore(StorageOptions options) : IImportFileStore
{
    private string PathFor(Guid id) => Path.Combine(options.ImportsDirectory, $"{id:N}.bin");

    public async Task SaveAsync(Guid batchId, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(options.ImportsDirectory);
        await using var file = File.Create(PathFor(batchId));
        await content.CopyToAsync(file, ct);
    }

    public Task<byte[]> ReadAsync(Guid batchId, CancellationToken ct) => File.ReadAllBytesAsync(PathFor(batchId), ct);

    public void Delete(Guid batchId)
    {
        var path = PathFor(batchId);
        if (File.Exists(path)) File.Delete(path);
    }
}

public class StorageOptions
{
    public required string DataDirectory { get; init; }
    public string DatabasePath => Path.Combine(DataDirectory, "spence.db");
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");
    public string ImportsDirectory => Path.Combine(DataDirectory, "imports");
}
