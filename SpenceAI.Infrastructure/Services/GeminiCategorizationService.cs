using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Application.DTOs;

namespace SpenceAI.Infrastructure.Services;

public class GeminiCategorizationService : IAiCategorizationService
{
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public GeminiCategorizationService(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task<List<ExtractedExpenseDto>> CategorizeExpensesAsync(
        string rawText,
        List<string> existingCategories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);
        existingCategories ??= new List<string>();

        var apiKey = _configuration["GeminiSettings:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set GeminiSettings:ApiKey in application configuration.");
        }

        var categoriesJson = JsonSerializer.Serialize(existingCategories);
        var promptText = BuildSystemPrompt(rawText, categoriesJson);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = promptText }
                    }
                }
            }
        };

        var requestUri = $"{GeminiEndpoint}?key={Uri.EscapeDataString(apiKey)}";
        using var response = await _httpClient.PostAsJsonAsync(requestUri, payload);

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini API request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        var aiText = ExtractTextFromGeminiResponse(responseBody);
        if (string.IsNullOrWhiteSpace(aiText))
        {
            throw new InvalidOperationException("Gemini API returned an empty text response.");
        }

        try
        {
            var expenses = JsonSerializer.Deserialize<List<ExtractedExpenseDto>>(aiText, JsonOptions);
            return expenses ?? new List<ExtractedExpenseDto>();
        }
        catch (JsonException)
        {
            // Attempt a best-effort extraction of a JSON array from the AI text
            var jsonArray = TryExtractJsonArray(aiText);
            if (!string.IsNullOrWhiteSpace(jsonArray))
            {
                try
                {
                    var expenses = JsonSerializer.Deserialize<List<ExtractedExpenseDto>>(jsonArray, JsonOptions);
                    return expenses ?? new List<ExtractedExpenseDto>();
                }
                catch (JsonException ex2)
                {
                    throw new InvalidOperationException($"Failed to deserialize extracted JSON from Gemini response. Extracted JSON: {jsonArray}", ex2);
                }
            }

            throw new InvalidOperationException($"Failed to deserialize Gemini response into a list of expenses. Raw AI text: {aiText}");
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

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Gemini API response did not contain any candidates. Response: {responseBody}");
        }

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Gemini API candidate did not contain text parts. Response: {responseBody}");
        }

        var textBuilder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                textBuilder.Append(textElement.GetString());
            }
        }

        return textBuilder.ToString().Trim();
    }

    private static string TryExtractJsonArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }

        return string.Empty;
    }
}
