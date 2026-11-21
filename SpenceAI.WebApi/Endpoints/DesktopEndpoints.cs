using SpenceAI.Infrastructure;
using SpenceAI.WebApi.Desktop;

namespace SpenceAI.WebApi.Endpoints;

public sealed record AppInfo(string Version, bool Desktop, string DataDirectory, string? LatestVersion, string? UpdateUrl);

public static class DesktopEndpoints
{
    public static void MapDesktopEndpoints(this IEndpointRouteBuilder app, bool desktop)
    {
        app.MapGet("/api/app/info", (StorageOptions storage, IServiceProvider services) =>
        {
            var latest = services.GetService<UpdateChecker>()?.Latest;
            return new AppInfo(DesktopHost.Version, desktop, storage.DataDirectory, latest?.Version, latest?.Url);
        }).WithTags("App");

        // Opens a folder on the machine the API runs on, so it only exists in desktop mode (API and browser on one computer).
        if (desktop)
        {
            app.MapPost("/api/app/open-data-folder", (StorageOptions storage) =>
            {
                DesktopHost.OpenFolder(storage.DataDirectory);
                return Results.NoContent();
            }).WithTags("App");
        }
    }
}
