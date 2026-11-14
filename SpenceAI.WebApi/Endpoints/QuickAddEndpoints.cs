using SpenceAI.Application.QuickAdd;

namespace SpenceAI.WebApi.Endpoints;

/// <summary>Quick add reads a typed line into rows to confirm; saving goes through POST /api/transactions/batch.</summary>
public static class QuickAddEndpoints
{
    public static void MapQuickAddEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/quick-add").WithTags("Quick add");
        g.MapPost("/parse", (QuickAddParseRequest req, QuickAddService s, CancellationToken ct) => s.ParseAsync(req, ct));
        g.MapPost("/suggest", (QuickAddSuggestRequest req, QuickAddService s, CancellationToken ct) => s.SuggestAsync(req, ct));
    }
}
