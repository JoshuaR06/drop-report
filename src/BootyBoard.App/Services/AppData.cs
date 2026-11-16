using System.Text.Json;

namespace BootyBoard.App.Services;

/// <summary>Where Booty Board keeps its data: %LOCALAPPDATA%\BootyBoard. Nothing leaves this PC.</summary>
public static class AppData
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootyBoard");
    public static string Matches => Dir("matches");
    public static string Archive => Dir("archive");
    public static string GameData => Dir("gamedata");
    public static string ItemTable => Path.Combine(GameData, "itemnames.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "bootyboard.log");
    public static string FortniteDemos { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    private static string Dir(string name) { var d = Path.Combine(Root, name); Directory.CreateDirectory(d); return d; }

    private static readonly object LogLock = new();
    public static void Log(string msg)
    {
        try { lock (LogLock) File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); } catch { }
    }
}

public sealed class Settings
{
    public string ReplayFolder { get; set; } = AppData.FortniteDemos;
    /// <summary>Extra folders to import old replays from (e.g. an archive you kept yourself).</summary>
    public List<string> ExtraFolders { get; set; } = new();
    /// <summary>Watch for new replays while the app is open. Off by default; the app never starts with Windows.</summary>
    public bool WatchWhileOpen { get; set; }
    public bool HideNames { get; set; }
    public string Theme { get; set; } = "Dark";
    public bool SetupDone { get; set; }
    public bool GameDataDeclined { get; set; }

    public static Settings Load()
    {
        try { if (File.Exists(AppData.SettingsFile)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppData.SettingsFile)) ?? new(); } catch { }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(AppData.Root); File.WriteAllText(AppData.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
}
