using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Pet;
using SpenceAI.Application.Savings;

namespace SpenceAI.WebApi.Endpoints;

/// <summary>Savings pots, the monthly Wrapped recap and the pet. "today" comes from the browser so dates follow the user's calendar.</summary>
public static class SavingsEndpoints
{
    private static DateOnly Today(DateOnly? today) => today ?? DateOnly.FromDateTime(DateTime.Now);

    public static void MapSavingsEndpoints(this IEndpointRouteBuilder app)
    {
        var pots = app.MapGroup("/api/pots").WithTags("Savings pots");
        pots.MapGet("/", (DateOnly? today, SavingsService s, CancellationToken ct) => s.ListAsync(Today(today), ct));
        pots.MapPost("/", (UpsertPotRequest req, DateOnly? today, SavingsService s, CancellationToken ct) => s.CreateAsync(req, Today(today), ct));
        pots.MapPut("/{id:int}", (int id, UpsertPotRequest req, DateOnly? today, SavingsService s, CancellationToken ct) => s.UpdateAsync(id, req, Today(today), ct));
        pots.MapDelete("/{id:int}", async (int id, SavingsService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        pots.MapPost("/{id:int}/contributions", (int id, AddContributionRequest req, DateOnly? today, SavingsService s, CancellationToken ct) =>
            s.AddContributionAsync(id, req, Today(today), ct));
        pots.MapDelete("/{id:int}/contributions/{entryId:int}", (int id, int entryId, DateOnly? today, SavingsService s, CancellationToken ct) =>
            s.DeleteContributionAsync(id, entryId, Today(today), ct));
        pots.MapPost("/{id:int}/claim", (int id, ClaimRequest req, SavingsService s, CancellationToken ct) => s.ClaimAsync(id, req, ct));
        pots.MapPost("/{id:int}/challenges", (int id, AddChallengeRequest req, DateOnly? today, SavingsService s, CancellationToken ct) =>
            s.AddChallengeAsync(id, req, Today(today), ct));
        pots.MapDelete("/{id:int}/challenges/{challengeId:int}", (int id, int challengeId, DateOnly? today, SavingsService s, CancellationToken ct) =>
            s.DeleteChallengeAsync(id, challengeId, Today(today), ct));
        pots.MapPost("/{id:int}/challenges/{challengeId:int}/claim", (int id, int challengeId, DateOnly? today, SavingsService s, CancellationToken ct) =>
            s.ClaimChallengeAsync(id, challengeId, Today(today), ct));

        app.MapGet("/api/analytics/wrapped", (string month, DateOnly? today, WrappedService s, CancellationToken ct) =>
        {
            var parts = month.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)) throw new AppException("Pick a month like 2026-10.");
            return s.GetAsync(y, m, Today(today), ct);
        }).WithTags("Analytics");

        var pet = app.MapGroup("/api/pet").WithTags("Pet");
        pet.MapGet("/", (DateOnly? today, PetService s, CancellationToken ct) => s.GetAsync(Today(today), ct));
        pet.MapPost("/dismiss", async (PetDayRequest req, PetService s, CancellationToken ct) =>
        {
            await s.DismissAsync(Today(req.Today), ct);
            return Results.NoContent();
        });
        pet.MapPut("/settings", (PetSettingsRequest req, DateOnly? today, PetService s, CancellationToken ct) => s.UpdateSettingsAsync(req, Today(today), ct));
    }
}
