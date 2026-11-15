using System.Text;
using SpenceAI.Application.Ask;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Quests;
using SpenceAI.Application.Settings;

namespace SpenceAI.WebApi.Endpoints;

/// <summary>Bills and subscriptions, streaks and side quests, and "ask your expenses". "today" comes from the browser.</summary>
public static class BillEndpoints
{
    private static DateOnly Today(DateOnly? today) => today ?? DateOnly.FromDateTime(DateTime.Now);

    public static void MapBillEndpoints(this IEndpointRouteBuilder app)
    {
        var bills = app.MapGroup("/api/bills").WithTags("Bills");
        bills.MapGet("/", (DateOnly? today, BillService s, CancellationToken ct) => s.GetOverviewAsync(Today(today), ct));
        bills.MapPost("/", (UpsertBillRequest req, DateOnly? today, BillService s, CancellationToken ct) => s.CreateAsync(req, Today(today), ct));
        bills.MapPut("/{id:int}", (int id, UpsertBillRequest req, DateOnly? today, BillService s, CancellationToken ct) => s.UpdateAsync(id, req, Today(today), ct));
        bills.MapDelete("/{id:int}", async (int id, BillService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        bills.MapPost("/{id:int}/paid", (int id, MarkPaidRequest req, BillService s, CancellationToken ct) => s.MarkPaidAsync(id, req, ct));
        bills.MapPost("/{id:int}/skip", (int id, SkipBillRequest req, BillService s, CancellationToken ct) => s.SkipAsync(id, req, ct));
        bills.MapDelete("/{id:int}/marks/{dueDate}", (int id, DateOnly dueDate, DateOnly? today, BillService s, CancellationToken ct) =>
            s.UnmarkAsync(id, dueDate, Today(today), ct));
        bills.MapPost("/{id:int}/review", (int id, ReviewBillRequest req, BillService s, CancellationToken ct) => s.ReviewAsync(id, req, ct));
        bills.MapPost("/suggestions/dismiss", async (DismissSuggestionRequest req, BillService s, CancellationToken ct) =>
        {
            await s.DismissSuggestionAsync(req.Pattern, ct);
            return Results.NoContent();
        });
        bills.MapGet("/calendar.ics", async (BillService s, SettingsService settings, CancellationToken ct) =>
        {
            var currency = (await settings.GetEntityAsync(ct)).Currency;
            var ics = await s.CalendarAsync(currency, ct);
            return Results.File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "spence-bills.ics");
        });

        app.MapGet("/api/quests", (DateOnly? today, QuestService s, CancellationToken ct) => s.GetAsync(Today(today), ct)).WithTags("Quests");

        app.MapPost("/api/ask", (AskRequest req, AskService s, CancellationToken ct) => s.AskAsync(req, ct)).WithTags("Ask");
    }
}
