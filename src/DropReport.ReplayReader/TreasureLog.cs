using System.Text.RegularExpressions;

namespace DropReport.Analysis;

/// <summary>
/// Which matches earned a treasure, from Fortnite's own log. The game only adds its post-match "Treasures Found" screen
/// when the player's prizes for that match aren't empty (BP_DelilahTreasureHunt_UIDirectorComponent.HasFoundAnyTreasure),
/// and the log records it: <see cref="Marker"/>. So a marker after a match = at least one treasure earned (not which).
/// No marker proves nothing if the post-match screens were skipped, so matches without one are only "none recorded"
/// when the log covered the whole match.
/// Also from the same log, numbers only: the in-match "reward won" popup (once about a minute in, then once per treasure;
/// counts a second treasure the replay can't), the profiler's worst ping jitter per match, and the load-screen time.
/// Privacy: Fortnite's log holds Epic names and account ids. Only timestamps and those numbers are kept; no line is stored.
/// Read after a match only, never during one.
/// </summary>
public sealed class TreasureLog
{
    public const string Marker = "Adding scene: UIScene_DelilahTreasureHunt_TreasuresFound_PostMatch";
    /// <summary>Seen: the screen appears up to about a minute before the replay ends (the replay runs until you leave).</summary>
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(3), After = TimeSpan.FromMinutes(5);

    public sealed record Span(DateTime From, DateTime To);
    public sealed record Sample(DateTime T, double V);

    /// <summary>Bump when Add starts collecting something new: every log still on disk is read again.</summary>
    public const int CurrentFormat = 3;
    public int Format { get; set; }
    public const string PopupMarker = "] does not want to show content";
    public const string PopupWidget = "WBP_DelilahTreasureHunt_RewardWon_C";
    /// <summary>UTC times a match's treasure UI was set up ("Adding scene: UIScene_DelilahTreasureHunt_RewardWon"): one per match,
    /// at load. Bounds which popups belong to which match.</summary>
    public List<DateTime> Starts { get; set; } = new();
    public const string StartMarker = "Adding scene: UIScene_DelilahTreasureHunt_RewardWon";
    /// <summary>UTC times the in-match reward popup was asked to show.</summary>
    public List<DateTime> Popups { get; set; } = new();
    /// <summary>Worst ping jitter (ms) per profiler run: the value only climbs during a match, so each peak is one match.</summary>
    public List<Sample> Jitter { get; set; } = new();
    /// <summary>Load-screen seconds, at the time each load finished.</summary>
    public List<Sample> Loads { get; set; } = new();

    /// <summary>UTC times the Treasures Found screen was shown.</summary>
    public List<DateTime> Screens { get; set; } = new();
    /// <summary>UTC spans the logs read so far covered.</summary>
    public List<Span> Covered { get; set; } = new();
    /// <summary>Log files already read: name -> "length:last write" (so unchanged backups aren't read again).</summary>
    public Dictionary<string, string> Read { get; set; } = new();

    private static readonly Regex Stamp = new(@"^\[(\d{4})\.(\d\d)\.(\d\d)-(\d\d)\.(\d\d)\.(\d\d)", RegexOptions.Compiled);

    /// <summary>Log lines start with a UTC stamp like [2026.11.14-17.48.19:009].</summary>
    public static DateTime? StampOf(string line)
    {
        var m = Stamp.Match(line);
        if (!m.Success) return null;
        int G(int i) => int.Parse(m.Groups[i].Value);
        try { return new DateTime(G(1), G(2), G(3), G(4), G(5), G(6), DateTimeKind.Utc); } catch { return null; }
    }

    /// <summary>Takes in one log's lines: keeps the marker times and the span the log covers, nothing else.</summary>
    public void Add(IEnumerable<string> lines)
    {
        DateTime? first = null, last = null;
        Sample run = null;
        void EndRun() { if (run != null && !Jitter.Any(j => j.T == run.T)) Jitter.Add(run); run = null; }
        foreach (var line in lines)
        {
            if (StampOf(line) is not DateTime t) continue;
            first ??= t; last = t;
            if (line.Contains(Marker, StringComparison.Ordinal) && !Screens.Contains(t)) Screens.Add(t);
            else if (line.Contains(PopupWidget, StringComparison.Ordinal) && line.Contains(PopupMarker, StringComparison.Ordinal) && !Popups.Contains(t)) Popups.Add(t);
            else if (line.Contains(StartMarker, StringComparison.Ordinal) && !Starts.Contains(t)) Starts.Add(t);
            else if (line.Contains("LogCsvProfiler", StringComparison.Ordinal))
            {
                if (Metric(line, "worstperiodofpingvariancems") is double j)
                {
                    if (run != null && j < run.V - 0.01) EndRun();   // fell back: a new match started
                    run = new Sample(t, Math.Round(j, 1));
                }
                else if (Metric(line, "maploadscreenduration") is double ld && !Loads.Any(x => x.T == t)) Loads.Add(new Sample(t, Math.Round(ld, 1)));
            }
        }
        EndRun();
        if (first is DateTime f && last is DateTime l && !Covered.Any(c => c.From <= f && c.To >= l))
        {
            Covered.RemoveAll(c => c.From >= f && c.To <= l);                            // the same log, read again after it grew
            Covered.Add(new Span(f, l));
        }
        Screens.Sort(); Popups.Sort(); Starts.Sort(); Jitter.Sort((a, b) => a.T.CompareTo(b.T)); Loads.Sort((a, b) => a.T.CompareTo(b.T));
    }

    private static double? Metric(string line, string key)
    {
        var i = line.IndexOf(key + "=\"", StringComparison.Ordinal);
        if (i < 0) return null;
        var s = line.Substring(i + key.Length + 2); var e = s.IndexOf('"');
        return e > 0 && double.TryParse(s[..e], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Reads one Fortnite log file if it changed since last time. Shares the file so Fortnite can keep writing it.</summary>
    public bool AddFile(string path)
    {
        var fi = new FileInfo(path);
        var key = $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
        if (Read.TryGetValue(fi.Name, out var k) && k == key) return false;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        Add(Lines(sr));
        Read[fi.Name] = key;
        return true;

        static IEnumerable<string> Lines(StreamReader sr) { while (sr.ReadLine() is { } l) yield return l; }
    }

    private static (DateTime start, DateTime end)? Window(MatchSummary m)
    {
        if (m.Start is not DateTime s0) return null;
        var start = s0.Kind == DateTimeKind.Utc ? s0 : s0.ToUniversalTime();
        return (start, start.AddMinutes(m.Minutes));
    }

    /// <summary>Treasures this match by the reward popup: every match shows it once about a minute in (or not at all), and
    /// once more per treasure. So 2 popups = 1 treasure, 3 = 2. One popup can't be told apart: returns 0.</summary>
    public int PopupTreasures(MatchSummary m)
    {
        if (Window(m) is not { } w) return 0;
        var (start, end) = w;
        // this match's treasure UI set-up (at load), up to the next match's; else the replay's own span
        var from = Starts.Where(s => Math.Abs((s - start).TotalSeconds) <= 120).Select(s => (DateTime?)s).FirstOrDefault();
        if (from is not DateTime f) return 0;   // no set-up line for this match: can't tell its popups apart
        var to = Starts.Where(s => s > f).Select(s => (DateTime?)s).FirstOrDefault() ?? end + After;
        if (to > end + After) to = end + After;
        var n = Popups.Count(t => t >= f && t < to);
        return Math.Max(0, n - 1);
    }

    /// <summary>Worst ping jitter (ms) in this match, from the game's profiler; null when the log doesn't cover it.</summary>
    public double? JitterMs(MatchSummary m) =>
        Window(m) is { } w ? Jitter.Where(j => j.T >= w.start && j.T <= w.end + TimeSpan.FromMinutes(1)).Select(j => (double?)j.V).Max() : null;

    /// <summary>The load screen into this match (seconds).</summary>
    public double? LoadSeconds(MatchSummary m) =>
        Window(m) is { } w ? Loads.Where(x => x.T >= w.start - TimeSpan.FromSeconds(60) && x.T <= w.start + TimeSpan.FromSeconds(90)).Select(x => (double?)x.V).FirstOrDefault() : null;

    /// <summary>true = the Treasures Found screen followed this match; false = the log covered the match and it didn't;
    /// null = no log for that time (Fortnite keeps only a few days of logs).</summary>
    public bool? Earned(MatchSummary m)
    {
        if (m.Start is not DateTime s0) return null;
        var start = s0.Kind == DateTimeKind.Utc ? s0 : s0.ToUniversalTime();
        var end = start.AddMinutes(m.Minutes);
        if (Screens.Any(t => t >= end - Before && t <= end + After)) return true;
        return Covered.Any(c => c.From <= start && c.To >= end + After) ? false : null;
    }
}
