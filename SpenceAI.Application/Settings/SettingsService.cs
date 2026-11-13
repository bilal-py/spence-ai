using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SpenceAI.Domain;

namespace SpenceAI.Application.Settings;

public record SettingsDto(AiProvider AiProvider, string AiModel, string OllamaUrl, bool HasApiKey, string Currency);

/// <param name="ApiKey">null keeps the stored key, "" clears it, anything else replaces it.</param>
public record UpdateSettingsRequest(AiProvider AiProvider, string AiModel, string OllamaUrl, string? ApiKey, string Currency);

public class SettingsService(IAppDbContext db, ISecretProtector protector, IChatClientFactory chatFactory)
{
    public async Task<AppSettings> GetEntityAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        settings = new AppSettings();
        db.Settings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default) => ToDto(await GetEntityAsync(ct));

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        Validate(request.AiModel, request.OllamaUrl, request.Currency);

        var settings = await GetEntityAsync(ct);
        settings.AiProvider = request.AiProvider;
        settings.AiModel = request.AiModel.Trim();
        settings.OllamaUrl = request.OllamaUrl.Trim().TrimEnd('/');
        settings.Currency = request.Currency.Trim().ToUpperInvariant();

        if (request.ApiKey is not null)
        {
            settings.ProtectedApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
                ? null
                : protector.Protect(request.ApiKey.Trim());
        }

        await db.SaveChangesAsync(ct);
        return ToDto(settings);
    }

    /// <summary>The connection currently saved in settings.</summary>
    public async Task<AiConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var s = await GetEntityAsync(ct);
        return new AiConnection(s.AiProvider, s.AiModel, s.OllamaUrl, protector.Unprotect(s.ProtectedApiKey));
    }

    public async Task<IChatClient> CreateChatClientAsync(CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct);
        EnsureUsable(connection);
        return chatFactory.Create(connection);
    }

    /// <summary>Sends a tiny prompt with the given (possibly unsaved) settings to check they work.</summary>
    public async Task<string> TestAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        Validate(request.AiModel, request.OllamaUrl, request.Currency);

        var apiKey = request.ApiKey;
        if (apiKey is null)
        {
            var saved = await GetEntityAsync(ct);
            apiKey = protector.Unprotect(saved.ProtectedApiKey);
        }

        var connection = new AiConnection(request.AiProvider, request.AiModel.Trim(), request.OllamaUrl.Trim(), apiKey);
        EnsureUsable(connection);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            var client = chatFactory.Create(connection);
            var response = await client.GetResponseAsync("Reply with the single word: ready", cancellationToken: timeout.Token);
            return response.Text.Trim();
        }
        catch (Exception ex) when (ex is not AppException && !ct.IsCancellationRequested)
        {
            throw new AppException(DescribeAiFailure(connection, ex));
        }
    }

    public static string DescribeAiFailure(AiConnection connection, Exception ex)
    {
        if (connection.Provider == AiProvider.Ollama && ex is HttpRequestException { StatusCode: null })
        {
            return $"Couldn't reach Ollama at {connection.OllamaUrl}. Start Ollama and try again.";
        }

        if (ex is OperationCanceledException)
        {
            return "The AI model took too long to answer. Try a smaller model or a cloud provider.";
        }

        return $"{connection.Provider} returned an error: {ex.Message}";
    }

    private static void EnsureUsable(AiConnection connection)
    {
        if (connection.Provider != AiProvider.Ollama && string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new AppException($"Add your {connection.Provider} API key in Settings first.");
        }
    }

    private static void Validate(string model, string ollamaUrl, string currency)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new AppException("Choose a model.");
        if (!Uri.TryCreate(ollamaUrl, UriKind.Absolute, out _)) throw new AppException("Ollama URL must be a full URL, like http://localhost:11434.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new AppException("Currency must be a 3-letter code, like USD.");
    }

    private static SettingsDto ToDto(AppSettings s) =>
        new(s.AiProvider, s.AiModel, s.OllamaUrl, !string.IsNullOrEmpty(s.ProtectedApiKey), s.Currency);
}
