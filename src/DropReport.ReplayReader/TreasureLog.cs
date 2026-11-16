using System.Text.RegularExpressions;

namespace DropReport.Analysis;

/// <summary>
/// Which matches earned a treasure, from Fortnite's own log. The game only adds its post-match "Treasures Found" screen
/// when the player's prizes for that match aren't empty (BP_DelilahTreasureHunt_UIDirectorComponent.HasFoundAnyTreasure),
/// and the log records it: <see cref="Marker"/>. So a marker after a match = at least one treasure earned (not which).
/// No marker proves nothing if the post-match screens were skipped, so matches without one are only "none recorded"
/// when the log covered the whole match.
/// Privacy: Fortnite's log holds Epic names and account ids. Only the timestamp of the marker line and the time span each
/// log covers are kept; no other line is stored. Read after a match only, never during one.
/// </summary>
public sealed class TreasureLog
{
    public const string Marker = "Adding scene: UIScene_DelilahTreasureHunt_TreasuresFound_PostMatch";
    /// <summary>Seen: the screen appears up to about a minute before the replay ends (the replay runs until you leave).</summary>
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(3), After = TimeSpan.FromMinutes(5);

    public sealed record Span(DateTime From, DateTime To);

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
        foreach (var line in lines)
        {
            if (StampOf(line) is not DateTime t) continue;
            first ??= t; last = t;
            if (line.Contains(Marker, StringComparison.Ordinal) && !Screens.Contains(t)) Screens.Add(t);
        }
        if (first is DateTime f && last is DateTime l)
        {
            if (Covered.Any(c => c.From <= f && c.To >= l)) { Screens.Sort(); return; }   // seen already (a log renamed to a backup)
            Covered.RemoveAll(c => c.From >= f && c.To <= l);                            // the same log, read again after it grew
            Covered.Add(new Span(f, l));
        }
        Screens.Sort();
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
