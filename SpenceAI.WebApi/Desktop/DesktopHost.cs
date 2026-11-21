using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using SpenceAI.Infrastructure;

namespace SpenceAI.WebApi.Desktop;

/// <summary>
/// Desktop mode for the downloadable app: a fixed local URL, the browser opens on start, one running copy,
/// logs in a file, a friendly message when startup fails, and (on Windows) a tray icon instead of a console.
/// </summary>
public static class DesktopHost
{
    public const string SettingsFile = "appsettings.Desktop.json";

    public static bool IsDesktop() => File.Exists(Path.Combine(AppContext.BaseDirectory, SettingsFile));

    /// <summary>"0.2.0" from the assembly (set by publish.sh), without the "+commit" suffix.</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    static string? logDirectory;

    public static Uri AppUri(IConfiguration config) =>
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
                // Double-clicking again opens it; a quiet start (Start with Windows) just leaves the running copy alone.
                if (config.GetValue("Desktop:OpenBrowser", true))
                {
                    Console.WriteLine($"Spence is already running at {uri}. Opening it.");
                    OpenBrowser(uri);
                }

                return true;
            }
        }
        catch
        {
        }

        return false; // Something else has the port: Kestrel fails and ReportCrash explains it.
    }

    /// <summary>Call right after the configuration is complete: file logs, crash message, update checks.</summary>
    public static void Configure(WebApplicationBuilder builder)
    {
        logDirectory = Path.Combine(DependencyInjection.ResolveDataDirectory(builder.Configuration), "logs");
        builder.Logging.AddProvider(new FileLoggerProvider(logDirectory));

        var port = AppUri(builder.Configuration).Port;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception, port);

        builder.Services.AddSingleton<UpdateChecker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateChecker>());
    }

    public static void Start(WebApplication app)
    {
        var uri = AppUri(app.Configuration);
        var storage = app.Services.GetRequiredService<StorageOptions>();
        var updates = app.Services.GetRequiredService<UpdateChecker>();
        var openBrowser = app.Configuration.GetValue("Desktop:OpenBrowser", true);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            app.Logger.LogInformation("Spence {Version} is running at {Url}", Version, uri);
            Console.WriteLine();
            Console.WriteLine($"Spence is running at {uri}");
            Console.WriteLine("Keep this window open while you use it. Close it to quit.");

            if (OperatingSystem.IsWindows() && app.Configuration.GetValue("Desktop:Tray", true))
            {
                StartTray(app, uri, storage, updates, showIntro: openBrowser);
            }

            if (openBrowser)
            {
                OpenBrowser(uri);
            }
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void StartTray(WebApplication app, Uri uri, StorageOptions storage, UpdateChecker updates, bool showIntro)
    {
        try
        {
            var tray = WindowsTray.Start(new TrayActions(
                Version: Version,
                Open: () => OpenBrowser(uri),
                OpenDataFolder: () => OpenFolder(storage.DataDirectory),
                IsAutostart: Autostart.IsEnabled,
                SetAutostart: Autostart.Set,
                Update: () => updates.Latest is { } u ? (u.Version, u.Url) : null,
                OpenUrl: OpenUrl,
                Quit: app.Lifetime.StopApplication,
                Log: message => app.Logger.LogWarning("Tray: {Message}", message)));

            app.Lifetime.ApplicationStopping.Register(tray.Close);
            updates.Found += u => tray.ShowBalloon($"Spence {u.Version} is out", "Click here to download it. Your data stays as it is.", u.Url);

            // Explain the tray once, the first time Spence starts by hand.
            var marker = Path.Combine(storage.DataDirectory, ".tray-intro-shown");
            if (showIntro && !File.Exists(marker))
            {
                tray.ShowBalloon("Spence is running", "It lives here in the tray. Click the icon to open it, right-click for more.", null);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "The tray icon couldn't be created");
        }
    }

    /// <summary>Last stop for a startup failure: log it, then tell the person what happened (there may be no console).</summary>
    static void ReportCrash(Exception? ex, int port)
    {
        var portTaken = false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            portTaken |= e.GetType().Name == "AddressInUseException";
        }

        var message = portTaken
            ? $"Spence couldn't start because another program is using port {port}.\n\nClose that program, or give Spence another port: create appsettings.Local.json next to Spence containing\n{{ \"Urls\": \"http://127.0.0.1:5300\" }}"
            : $"Spence couldn't start.\n\n{ex?.Message}";

        string? logFile = null;
        try
        {
            if (logDirectory != null)
            {
                Directory.CreateDirectory(logDirectory);
                logFile = Path.Combine(logDirectory, "crash.log");
                File.AppendAllText(logFile, $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} Spence {Version}\n{ex}\n\n");
                message += $"\n\nDetails were saved to {logFile}";
            }
        }
        catch
        {
        }

        Console.Error.WriteLine(message);
        if (OperatingSystem.IsWindows())
        {
            WindowsTray.MessageBox("Spence", message);
        }
    }

    public static void OpenBrowser(Uri uri) => OpenUrl(uri.ToString());

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Launch(url);
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path } });
            }
            catch
            {
            }
        }
        else
        {
            Launch(path);
        }
    }

    static void Launch(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { target } });
            }
        }
        catch
        {
            // No launcher (headless Linux): the URL is printed and logged.
        }
    }
}
