using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Application.DTOs;

namespace SpenceAI.Infrastructure.Services;

public class OllamaService : IAiEngineService
{
    private const string OllamaEndpoint = "http://localhost:11434/api/generate";
    private const string OllamaModel = "llama3.2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<ExtractedExpenseDto>> CategorizeExpensesAsync(string rawText, List<string> existingCategories, string apiKey)
    {
        // Note: Ollama running locally does not require an API key, but we keep the parameter for interface consistency.
        ArgumentNullException.ThrowIfNull(rawText);
        existingCategories ??= new List<string>();

        var categoriesJson = JsonSerializer.Serialize(existingCategories);
        var promptText = BuildSystemPrompt(rawText, categoriesJson);

        var payload = new
        {
            model = OllamaModel,
            prompt = promptText,
            stream = false,
            format = "json"
        };

        using var response = await _httpClient.PostAsJsonAsync(OllamaEndpoint, payload);

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama request failed: {Status} {Body}", (int)response.StatusCode, responseBody);
            throw new HttpRequestException($"Ollama API request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        // Ollama returns a JSON object with a "response" field containing the generated text.
        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody, JsonOptions);
        if (ollamaResponse == null || string.IsNullOrWhiteSpace(ollamaResponse.Response))
            throw new InvalidOperationException("Ollama API returned an empty text response.");

        var aiText = ollamaResponse.Response.Trim();

        try
        {
            var expenses = JsonSerializer.Deserialize<List<ExtractedExpenseDto>>(aiText, JsonOptions);
            return expenses ?? new List<ExtractedExpenseDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Ollama AI text. Raw AI text: {AiText}", aiText);
            throw new InvalidOperationException("Failed to deserialize Ollama response into a list of expenses.", ex);
        }
    }

    private static string BuildSystemPrompt(string rawText, string categoriesJson)
    {
        // Notice how the schema example uses REALISTIC data, not generic "string" types.
        // This anchors the LLM so it knows exactly what the output should look like.
        return $$"""
        You are a highly precise headless data extraction API. Your ONLY job is to extract credit card transactions from the provided text and output a JSON array.

        <rules>
        1. Extract the Description, Amount, and Date for every transaction.
        2. Assign a CategoryName. Prefer these existing categories: {{categoriesJson}}
        3. If no existing category fits, create a concise new one and set IsNewCategory to true. Otherwise, false.
        4. Amount must be a positive or negative decimal number. Remove all currency symbols.
        5. You MUST respond with ONLY a valid JSON array. No explanations, no markdown code blocks, no titles.
        </rules>

        <schema>
        [
          {
            "Description": "AMAZON PAY IN FLIGHTS",
            "Amount": 14877.00,
            "Date": "2026-04-18T00:00:00",
            "CategoryName": "Travel",
            "IsNewCategory": false
          }
        ]
        </schema>

        <data_to_process>
        {{rawText}}
        </data_to_process>
        """;
    }

    private class OllamaResponse
    {
        public string? Response { get; set; }
    }
}