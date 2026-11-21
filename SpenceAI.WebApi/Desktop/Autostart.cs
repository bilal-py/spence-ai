using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SpenceAI.WebApi.Desktop;

/// <summary>"Start with Windows": a per-user Run entry that starts Spence quietly in the tray (no browser).</summary>
[SupportedOSPlatform("windows")]
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Spence";

    static string Command => $"\"{Environment.ProcessPath}\" --Desktop:OpenBrowser=false";

    /// <summary>On only when the entry points at this copy; after the folder moves it reads as off until turned on again.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string value && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(Name, Command);
        }
        else
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
