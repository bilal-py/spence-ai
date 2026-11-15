using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SpenceAI.Application.Settings;

namespace SpenceAI.Application.Analytics;

public class InsightResult
{
    [Description("One sentence summarizing the period")]
    public required string Headline { get; set; }

    public required List<InsightItem> Items { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<InsightTone>))]
public enum InsightTone
{
    good,
    neutral,
    watch,
}

public class InsightItem
{
    [Description("Short title, under 8 words")]
    public required string Title { get; set; }

    [Description("1-2 sentences with concrete numbers")]
    public required string Detail { get; set; }

    public required InsightTone Tone { get; set; }
}

/// <summary>
/// Asks the LLM to interpret pre-computed aggregates. The model never sees raw transactions,
/// which keeps prompts small (fast on local models) and numbers accurate.
/// </summary>
public class InsightService(AnalyticsService analytics, SettingsService settings)
{
    public async Task<InsightResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var overview = await analytics.GetOverviewAsync(from, to, ct);
        if (overview.Count == 0)
        {
            return new InsightResult { Headline = "No transactions in this period yet.", Items = [] };
        }

        var currency = (await settings.GetEntityAsync(ct)).Currency;
        var connection = await settings.GetConnectionAsync(ct);
        var client = await settings.CreateChatClientAsync(ct);

        var facts = JsonSerializer.Serialize(new
        {
            currency,
            period = new { overview.From, overview.To },
            overview.Spent,
            overview.Income,
            overview.Net,
            previousPeriod = new { spent = overview.PreviousSpent, income = overview.PreviousIncome },
            overview.DailyAverage,
            categories = overview.ByCategory.Take(10).Select(c => new { c.Name, c.Total, c.PreviousTotal, c.Count, c.MonthlyBudget }),
            topMerchants = overview.TopMerchants,
            largest = overview.Largest.Select(l => new { l.Date, l.Description, l.Amount, l.Category }),
            recurring = overview.Recurring,
            last12Months = overview.Monthly,
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, """
                You are a sharp, friendly personal-finance analyst. You get pre-computed spending facts as JSON.
                Write 3 to 5 insights a person would act on: notable changes vs the previous period, categories over budget,
                unusual large purchases, recurring charges worth reviewing, and one concrete saving idea.
                Use only the numbers provided, never invent any. Format money with the given currency code.
                Be direct and specific. No generic advice.
                """),
            new(ChatRole.User, facts),
        };

        try
        {
            var response = await client.GetResponseAsync<InsightResult>(messages, new ChatOptions { Temperature = 0.3f }, cancellationToken: ct);
            return response.TryGetResult(out var result)
                ? result
                : throw new AppException("The AI model's answer couldn't be read. Try again.");
        }
        catch (Exception ex) when (ex is not AppException && !ct.IsCancellationRequested)
        {
            throw new AppException(SettingsService.DescribeAiFailure(connection, ex));
        }
    }
}
