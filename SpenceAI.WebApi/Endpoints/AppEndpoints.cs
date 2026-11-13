using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Settings;

namespace SpenceAI.WebApi.Endpoints;

public record InsightRequest(DateOnly From, DateOnly To);

/// <summary>Imports, analytics, insights and settings.</summary>
public static class AppEndpoints
{
    public static void MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var imports = app.MapGroup("/api/imports").WithTags("Imports");
        imports.MapPost("/", Upload).DisableAntiforgery();
        imports.MapGet("/", (ImportService s, CancellationToken ct) => s.ListAsync(ct));
        imports.MapGet("/{id:guid}", (Guid id, ImportService s, CancellationToken ct) => s.GetAsync(id, ct));
        imports.MapPost("/{id:guid}/commit", (Guid id, CommitImportRequest req, ImportService s, CancellationToken ct) => s.CommitAsync(id, req, ct));
        imports.MapDelete("/{id:guid}", async (Guid id, ImportService s, CancellationToken ct) =>
        {
            await s.DiscardAsync(id, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/analytics/overview", (DateOnly from, DateOnly to, AnalyticsService s, CancellationToken ct) => s.GetOverviewAsync(from, to, ct))
            .WithTags("Analytics");
        app.MapPost("/api/insights", (InsightRequest req, InsightService s, CancellationToken ct) => s.GenerateAsync(req.From, req.To, ct))
            .WithTags("Analytics");

        var settings = app.MapGroup("/api/settings").WithTags("Settings");
        settings.MapGet("/", (SettingsService s, CancellationToken ct) => s.GetAsync(ct));
        settings.MapPut("/", (UpdateSettingsRequest req, SettingsService s, CancellationToken ct) => s.UpdateAsync(req, ct));
        settings.MapPost("/test", async (UpdateSettingsRequest req, SettingsService s, CancellationToken ct) =>
            Results.Ok(new { reply = await s.TestAsync(req, ct) }));
        settings.MapGet("/ollama-models", (string url, IChatClientFactory f, CancellationToken ct) => f.ListOllamaModelsAsync(url, ct));
    }

    private static async Task<IResult> Upload(IFormFile file, ImportService imports, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var batch = await imports.CreateAsync(file.FileName, file.Length, stream, ct);
        return Results.Accepted($"/api/imports/{batch.Id}", batch);
    }
}
