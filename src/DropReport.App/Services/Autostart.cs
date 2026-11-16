using Microsoft.Win32;

namespace DropReport.App.Services;

/// <summary>
/// "Start with Windows": a per-user Run entry (HKCU, no admin) that launches Drop Report quietly into the tray.
/// Turning it off removes the entry. Nothing else is touched.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DropReport";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    /// <summary>A development build (bin\Debug) needs the SDK environment to start, so it is never registered.</summary>
    public static bool CanRegister => Environment.ProcessPath is { } p && !p.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    public static bool IsOn
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(Name) is string; } catch { return false; } }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && CanRegister) k.SetValue(Name, Command);
            else if (!on) k.DeleteValue(Name, false);
        }
        catch (Exception e) { AppData.Log("autostart: " + e.Message); }
    }

    /// <summary>Keeps the entry pointing at this exe if the user moved it.</summary>
    public static void Sync(bool wanted)
    {
        if (!wanted || !CanRegister) return;
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); if (k?.GetValue(Name) as string != Command) Set(true); } catch { }
    }
}
