using System.Diagnostics;
using System.Net.Sockets;

namespace SpenceAI.WebApi;

/// <summary>Desktop mode for the downloadable app: one fixed local URL, open the browser, one running copy.</summary>
public static class DesktopHost
{
    public const string SettingsFile = "appsettings.Desktop.json";

    public static bool IsDesktop() => File.Exists(Path.Combine(AppContext.BaseDirectory, SettingsFile));

    static Uri AppUri(IConfiguration config) =>
        new((config["Urls"] ?? "http://127.0.0.1:5234").Split(';')[0].Replace("0.0.0.0", "127.0.0.1").Replace("*", "127.0.0.1"));

    /// <summary>If the port is already taken by a running Spence, open it in the browser and report true.</summary>
    public static async Task<bool> TryReuseRunningAsync(IConfiguration config)
    {
        var uri = AppUri(config);
        try
        {
            using var probe = new TcpClient();
            await probe.ConnectAsync(uri.Host, uri.Port).WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false; // Nothing listening: start normally.
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var health = await http.GetAsync(new Uri(uri, "api/health"));
            if (health.IsSuccessStatusCode)
            {
                Console.WriteLine($"Spence is already running at {uri}. Opening it.");
                OpenBrowser(uri);
                return true;
            }
        }
        catch
        {
        }

        Console.Error.WriteLine($"Port {uri.Port} is in use by another program. Set \"Urls\" in appsettings.Local.json next to Spence to use another port.");
        return false; // Let Kestrel fail with its own message.
    }

    public static void OpenBrowserWhenStarted(WebApplication app)
    {
        if (!app.Configuration.GetValue("Desktop:OpenBrowser", true))
        {
            return;
        }

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var uri = AppUri(app.Configuration);
            Console.WriteLine();
            Console.WriteLine($"Spence is running at {uri}");
            Console.WriteLine("Keep this window open while you use it. Close it to quit.");
            OpenBrowser(uri);
        });
    }

    static void OpenBrowser(Uri uri)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", uri.ToString());
            }
            else
            {
                Process.Start("xdg-open", uri.ToString());
            }
        }
        catch
        {
            // No browser launcher (headless Linux): the URL is printed above.
        }
    }
}
