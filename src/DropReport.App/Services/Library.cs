using System.Text.Json;
using DropReport.Analysis;

namespace DropReport.App.Services;

/// <summary>
/// The match library: copies finished replays into the archive (Fortnite keeps only the last 100) and analyzes any
/// without a summary. Ban-safe: reads replay FILES only, after Fortnite has closed them. CPU-safe: analysis runs on a
/// below-normal-priority thread and never while a match is being recorded.
/// </summary>
public sealed class Library
{
    private readonly Settings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ItemTable Names { get; private set; } = ItemTable.Empty;
    public List<MatchSummary> Matches { get; private set; } = new();
    public TreasureLog TreasureLog { get; private set; } = LoadTreasureLog();
    public event Action Changed;

    public Library(Settings settings) { _settings = settings; ReloadNames(); }

    public void ReloadNames() => Names = ItemTable.Load(AppData.ItemTable);

    public void LoadSummaries()
    {
        var list = new List<MatchSummary>();
        foreach (var f in Directory.GetFiles(AppData.Matches, "*.summary.json"))
            try { var s = JsonSerializer.Deserialize<MatchSummary>(File.ReadAllText(f), Json.Options); if (s != null) list.Add(s); } catch { }
        var tl = _settings.ReadGameLog ? TreasureLog : null;
        Matches = list.Select(m => tl == null ? m : m with { TreasureEarned = tl.Earned(m) }).OrderByDescending(m => m.Start ?? DateTime.MinValue).ToList();
        Changed?.Invoke();
    }

    public MapData LoadMap(MatchSummary m)
    {
        var f = Path.Combine(AppData.Matches, Path.GetFileNameWithoutExtension(m.File) + ".map.json");
        try { return File.Exists(f) ? JsonSerializer.Deserialize<MapData>(File.ReadAllText(f), Json.Options) : null; } catch { return null; }
    }

    /// <summary>Finished = Fortnite no longer holds it open for writing. Opening for read without sharing writes fails
    /// instantly on the replay being recorded, and does not disturb Fortnite.</summary>
    public static bool IsFinished(string path)
    {
        try { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; } catch { return false; }
    }

    public bool MatchInProgress() =>
        Directory.Exists(_settings.ReplayFolder) && Directory.GetFiles(_settings.ReplayFolder, "*.replay").Any(f => !IsFinished(f));

    public sealed record ScanResult(int Archived, int Analyzed, int Failed, bool PausedForMatch);

    /// <param name="reanalyzeAll">Re-read every archived replay (after a names update or an app update).</param>
    public async Task<ScanResult> ScanAsync(IProgress<string> progress = null, bool reanalyzeAll = false, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return new ScanResult(0, 0, 0, false);   // a scan is already running
        try
        {
            return await RunLowPriority(() => Scan(progress, reanalyzeAll, ct));
        }
        finally { _gate.Release(); LoadSummaries(); }
    }

    private ScanResult Scan(IProgress<string> progress, bool reanalyzeAll, CancellationToken ct)
    {
        int archived = 0, analyzed = 0, failed = 0;
        // 1) archive finished replays from the replay folder and any extra folders
        foreach (var folder in new[] { _settings.ReplayFolder }.Concat(_settings.ExtraFolders).Where(Directory.Exists))
            foreach (var f in Directory.GetFiles(folder, "*.replay"))
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(AppData.Archive, Path.GetFileName(f));
                var len = new FileInfo(f).Length;
                if (File.Exists(dest) && new FileInfo(dest).Length == len) continue;
                if (!IsFinished(f)) continue;
                try { File.Copy(f, dest + ".part", true); File.Move(dest + ".part", dest, true); archived++; }
                catch (Exception e) { AppData.Log($"archive failed {f}: {e.Message}"); }
            }
        if (archived > 0) { AppData.Log($"archived {archived} replays"); progress?.Report($"Saved {archived} new replays"); }

        // 2) analyze archived replays without a summary (newest first); pause as soon as a match starts
        var stale = Matches.Where(m => m.Version < MatchAnalyzer.SummaryVersion).Select(m => Path.GetFileNameWithoutExtension(m.File)).ToHashSet();
        var todo = Directory.GetFiles(AppData.Archive, "*.replay").OrderByDescending(File.GetLastWriteTime).Where(f =>
        {
            var b = Path.GetFileNameWithoutExtension(f);
            return reanalyzeAll || stale.Contains(b) || !File.Exists(Path.Combine(AppData.Matches, b + ".summary.json")) && !File.Exists(Path.Combine(AppData.Matches, b + ".failed"));
        }).ToList();
        for (var i = 0; i < todo.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (MatchInProgress()) { AppData.Log("match started, pausing analysis"); return new ScanResult(archived, analyzed, failed, true); }
            var f = todo[i]; var b = Path.GetFileNameWithoutExtension(f);
            progress?.Report($"Reading match {i + 1} of {todo.Count}...");
            try
            {
                var res = MatchAnalyzer.Analyze(f, Names, geo: MapGeo.Load(Path.Combine(AppData.GameData, "mapinfo.json")));
                File.WriteAllText(Path.Combine(AppData.Matches, b + ".map.json"), JsonSerializer.Serialize(res.Map, Json.Options));
                File.WriteAllText(Path.Combine(AppData.Matches, b + ".summary.json"), JsonSerializer.Serialize(res.Summary, Json.Options));
                File.Delete(Path.Combine(AppData.Matches, b + ".failed"));
                analyzed++;
            }
            catch (Exception e)
            {
                // marked so it is not retried every pass (Settings > Re-read all clears it)
                File.WriteAllText(Path.Combine(AppData.Matches, b + ".failed"), $"{e.GetType().Name}: {e.Message}");
                AppData.Log($"analysis failed {b}: {e.Message}");
                failed++;
            }
        }
        if (analyzed + failed > 0) AppData.Log($"analyzed {analyzed}, failed {failed}");
        ReadGameLogs();
        return new ScanResult(archived, analyzed, failed, false);
    }

    private static TreasureLog LoadTreasureLog()
    {
        try { if (File.Exists(AppData.TreasureLogFile)) return JsonSerializer.Deserialize<TreasureLog>(File.ReadAllText(AppData.TreasureLogFile)) ?? new(); } catch { }
        return new();
    }

    /// <summary>Fortnite's log: which matches showed the Treasures Found screen. Only between matches (never during one),
    /// and only the marker's time is kept (the log also holds Epic names, which are never read into the app).</summary>
    private void ReadGameLogs()
    {
        if (!_settings.ReadGameLog || !Directory.Exists(AppData.FortniteLogs) || MatchInProgress()) return;
        var changed = false;
        foreach (var f in Directory.GetFiles(AppData.FortniteLogs, "FortniteGame*.log"))
            try { changed |= TreasureLog.AddFile(f); } catch (Exception e) { AppData.Log($"game log not read {Path.GetFileName(f)}: {e.Message}"); }
        if (!changed) return;
        try { File.WriteAllText(AppData.TreasureLogFile, JsonSerializer.Serialize(TreasureLog)); } catch { }
        AppData.Log($"game log: {TreasureLog.Screens.Count} treasure screens on record");
    }

    private static Task<T> RunLowPriority<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var th = new Thread(() => { try { tcs.SetResult(work()); } catch (Exception e) { tcs.SetException(e); } })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "DropReport analysis" };
        th.Start();
        return tcs.Task;
    }
}
