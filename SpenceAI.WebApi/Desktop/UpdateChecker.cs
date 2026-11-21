using System.Net.Http.Headers;
using System.Text.Json;

namespace SpenceAI.WebApi.Desktop;

public sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Asks GitHub for the latest release once a day (desktop mode only). Sends nothing about the user:
/// a plain GET with a generic User-Agent. Turn it off with "Desktop:CheckForUpdates": false.
/// </summary>
public sealed class UpdateChecker(IHttpClientFactory httpFactory, IConfiguration config, ILogger<UpdateChecker> logger) : BackgroundService
{
    public UpdateInfo? Latest { get; private set; }

    /// <summary>Raised once per newly found version.</summary>
    public event Action<UpdateInfo>? Found;

    public bool Enabled => config.GetValue("Desktop:CheckForUpdates", true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); // Let startup finish first.
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    async Task CheckAsync(CancellationToken ct)
    {
        var repo = config["Desktop:UpdateRepo"] ?? "bilal-py/spence-ai";
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Spence-Desktop", DesktopHost.Version));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{repo}/releases/latest";
            var latest = tag.TrimStart('v', 'V');

            if (IsNewer(latest, DesktopHost.Version) && Latest?.Version != latest)
            {
                Latest = new UpdateInfo(latest, url);
                logger.LogInformation("Update available: {Latest} (running {Current})", latest, DesktopHost.Version);
                Found?.Invoke(Latest);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Update check failed"); // Offline is normal; stay quiet.
        }
    }

    /// <summary>Compares "1.2.3" parts; anything after "-" (pre-release) is ignored.</summary>
    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.Split('-')[0], out var a) && Version.TryParse(current.Split('-')[0], out var b) && a > b;
}
