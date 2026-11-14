using SpenceAI.Application;
using SpenceAI.Infrastructure;

namespace SpenceAI.WebApi.Endpoints;

/// <summary>Backups of the whole data folder: list, create, download, inspect and restore.</summary>
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/backup").WithTags("Backup");

        g.MapGet("/", (BackupService s, CancellationToken ct) => s.GetStatusAsync(ct));
        g.MapPut("/settings", (UpdateBackupSettingsRequest req, BackupService s, CancellationToken ct) => s.UpdateSettingsAsync(req, ct));
        g.MapPost("/", (BackupService s, CancellationToken ct) => s.CreateAsync(BackupKind.Manual, ct));
        g.MapGet("/files/{fileName}", (string fileName, BackupService s) =>
            Results.File(s.PathOf(fileName), "application/zip", fileName));

        // Upload a .spence file: inspect shows what it holds, restore replaces the current data with it.
        g.MapPost("/inspect", async (IFormFile file, BackupService s, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return await s.InspectAsync(stream, ct);
        }).DisableAntiforgery();
        g.MapPost("/restore", async (IFormFile file, BackupService s, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return await s.RestoreAsync(stream, ct);
        }).DisableAntiforgery();

        // Restore one of the backups in the backups folder (e.g. undo a restore with its "before-restore" backup).
        g.MapPost("/files/{fileName}/restore", async (string fileName, BackupService s, CancellationToken ct) =>
        {
            await using var stream = File.OpenRead(s.PathOf(fileName));
            return await s.RestoreAsync(stream, ct);
        });
    }
}
