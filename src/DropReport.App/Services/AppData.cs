using System.Text.Json;

namespace DropReport.App.Services;

/// <summary>Where Drop Report keeps its data: %LOCALAPPDATA%\DropReport. Nothing leaves this PC.</summary>
public static class AppData
{
    public static string Root { get; } = MoveOldFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropReport"));

    /// <summary>The app was called Booty Board until 16 Nov 2026: carry its data folder over once (same drive, so a move).</summary>
    private static string MoveOldFolder(string root)
    {
        var old = Path.Combine(Path.GetDirectoryName(root)!, "BootyBoard");
        try
        {
            if (!Directory.Exists(root) && Directory.Exists(old))
            {
                Directory.Move(old, root);
                var log = Path.Combine(root, "bootyboard.log");
                if (File.Exists(log)) File.Move(log, Path.Combine(root, "dropreport.log"));
            }
        }
        catch { /* a locked file in the old folder: start fresh, the old data stays where it was */ }
        return root;
    }
    public static string Matches => Dir("matches");
    public static string Archive => Dir("archive");
    public static string GameData => Dir("gamedata");
    public static string ItemTable => Path.Combine(GameData, "itemnames.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "dropreport.log");
    public static string FortniteDemos { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
    public static string FortniteLogs { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Logs");
    /// <summary>Times of Fortnite's post-match Treasures Found screen (timestamps only; see TreasureLog).</summary>
    public static string TreasureLogFile => Path.Combine(Root, "treasure-log.json");
    /// <summary>Spots where rolling chests have been opened in your matches (for estimating teammates' unseen searches).</summary>
    public static string ChestSpotsFile => Path.Combine(Root, "chest-spots.json");

    private static string Dir(string name) { var d = Path.Combine(Root, name); Directory.CreateDirectory(d); return d; }

    private static readonly object LogLock = new();
    /// <summary>Where a log line goes when dropreport.log can't be written (so a failing write is never silent again).</summary>
    public static string FallbackLogFile => Path.Combine(Path.GetTempPath(), "DropReport-fallback.log");

    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}";
        lock (LogLock)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try { File.AppendAllText(LogFile, line); return; }
                catch (Exception e) when (attempt < 2) { _ = e; Thread.Sleep(50); }   // brief sharing clash: try again
                catch (Exception e)
                {
                    try { File.AppendAllText(FallbackLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [could not write {LogFile}: {e.GetType().Name}: {e.Message}] {msg}{Environment.NewLine}"); }
                    catch { }
                }
            }
        }
    }
}

public sealed class Settings
{
    public string ReplayFolder { get; set; } = AppData.FortniteDemos;
    /// <summary>Extra folders to import old replays from (e.g. an archive you kept yourself).</summary>
    public List<string> ExtraFolders { get; set; } = new();
    /// <summary>Watch for new replays while the app is running (in a window or the tray).</summary>
    public bool WatchWhileOpen { get; set; } = true;
    /// <summary>Launch quietly into the tray at sign-in (a per-user Run entry; see Autostart).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Closing the window keeps Drop Report running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Show a match report card in the corner when a new match is read.</summary>
    public bool PostMatchPopup { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool OnlineOdds { get; set; } = true;
    public bool HideNames { get; set; }
    public string Theme { get; set; } = "Dark";
    // Treasure Hunt counting rules (nobody outside Epic knows these for sure; the user picks)
    public bool CountRefills { get; set; } = true;
    public bool CountBroken { get; set; } = true;
    public bool CountTeam { get; set; } = true;
    /// <summary>With CountTeam: add teammates' chest searches out of the replay's view (estimated, TeamEstimate).</summary>
    public bool CountUnseen { get; set; } = true;
    /// <summary>After a match, look in Fortnite's log for its "Treasures Found" screen (only that line's time is kept).</summary>
    public bool ReadGameLog { get; set; } = true;
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
