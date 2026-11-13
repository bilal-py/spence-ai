using System.ClientModel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using SpenceAI.Application;
using SpenceAI.Domain;
using UglyToad.PdfPig;

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

public class ChatClientFactory(IHttpClientFactory httpFactory) : IChatClientFactory
{
    private static readonly Uri GeminiOpenAiEndpoint = new("https://generativelanguage.googleapis.com/v1beta/openai/");

    public IChatClient Create(AiConnection c) => c.Provider switch
    {
        AiProvider.Ollama => new OllamaApiClient(OllamaHttp(c.OllamaUrl), c.Model),
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
    public IReadOnlyList<string> ExtractPages(Stream pdf)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            // Layout-aware text keeps table rows on one line, which helps the model pair dates and amounts.
            return document.GetPages()
                .Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p))
                .ToList();
        }
        catch (Exception ex) when (ex is not AppException)
        {
            throw new AppException(ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "This PDF is password-protected. Save an unlocked copy and upload that."
                : "This file couldn't be read as a PDF.");
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
