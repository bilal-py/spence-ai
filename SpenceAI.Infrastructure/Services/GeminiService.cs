using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Application.DTOs;

namespace SpenceAI.Infrastructure.Services;

public class GeminiService : IAiEngineService
{
    private const string GeminiEndpointTemplate =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(HttpClient httpClient, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<ExtractedExpenseDto>> CategorizeExpensesAsync(string rawText, List<string> existingCategories, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        existingCategories ??= new List<string>();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is required for Gemini requests.");

        var categoriesJson = JsonSerializer.Serialize(existingCategories);
        var promptText = BuildSystemPrompt(rawText, categoriesJson);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = promptText } }
                }
            }
        };

        var requestUri = $"{GeminiEndpointTemplate}?key={Uri.EscapeDataString(apiKey)}";
        using var response = await _httpClient.PostAsJsonAsync(requestUri, payload);

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini request failed: {Status} {Body}", (int)response.StatusCode, responseBody);
            throw new HttpRequestException($"Gemini API request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        var aiText = ExtractTextFromGeminiResponse(responseBody);
        if (string.IsNullOrWhiteSpace(aiText))
            throw new InvalidOperationException("Gemini API returned an empty text response.");

        try
        {
            var expenses = JsonSerializer.Deserialize<List<ExtractedExpenseDto>>(aiText, JsonOptions);
            return expenses ?? new List<ExtractedExpenseDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Gemini AI text. Raw AI text: {AiText}", aiText);
            throw new InvalidOperationException("Failed to deserialize Gemini response into a list of expenses.", ex);
        }
    }

    private static string BuildSystemPrompt(string rawText, string categoriesJson)
    {
        const string schemaExample = """
            [
              {
                "Description": "string",
                "Amount": 0.0,
                "Date": "2024-01-01T00:00:00",
                "CategoryName": "string",
                "IsNewCategory": false
              }
            ]
            """;

        return $"""
            You are a financial data extraction assistant. Extract every transaction from the source text below and assign each one a category.

            Existing categories (prefer these when a transaction clearly fits):
            {categoriesJson}

            Rules:
            - Extract Description, Amount, Date, CategoryName, and IsNewCategory for each transaction.
            - Use an existing category name exactly when it fits; set IsNewCategory to false.
            - If no existing category fits, choose a clear, concise new category name and set IsNewCategory to true.
            - Amount must be a decimal number without currency symbols.
            - Date must be ISO 8601 (for example, 2024-03-15 or 2024-03-15T00:00:00).
            - Return ONLY a raw JSON array. Each object must match this schema:
            {schemaExample}
            - Do NOT wrap the JSON in markdown code blocks. Do NOT include json code fences or triple backticks or any other formatting.
            - Do NOT include any explanatory text before or after the JSON array.

            Source text:
            {rawText}
            """;
    }

    private static string ExtractTextFromGeminiResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException($"Gemini API response did not contain any candidates. Response: {responseBody}");

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Gemini API candidate did not contain text parts. Response: {responseBody}");
        }

        var textBuilder = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                textBuilder.Append(textElement.GetString());
            }
        }

        return textBuilder.ToString().Trim();
    }
}
