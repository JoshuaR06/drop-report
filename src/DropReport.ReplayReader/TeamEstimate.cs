using System.Text.Json;

namespace DropReport.Analysis;

/// <summary>
/// Teammates' chest searches the replay can't show. Your replay only carries chest openings within about 250 m of you
/// (the server only sends what's near you), but every teammate's pawn carries bStartedInteractSearch at any distance.
/// A search counts as a chest search when it lasted a chest's search time and was at a spot where a chest has been
/// opened in your matches. Checked on 19 Nov 2026 against nearby teammate searches (where the replay does show the
/// chest): 84% found, 81% of finds real, total 860 vs 880 (about 2% low), typical error about 1.3 searches a match.
/// </summary>
public static class TeamEstimate
{
    /// <summary>Chest searches take 0.63-0.87 s (10th-90th percentile); shorter ones are cancelled or other containers.</summary>
    public const double MinSeconds = 0.6;
    /// <summary>How close to a known chest spot the searcher stands (90% of real chest searches are within 3.2 m).</summary>
    public const double SpotRadiusM = 3.0;
    /// <summary>Beyond this from you the replay may not show the chest, so a search there with no chest event is "unseen".</summary>
    public const double OutOfViewM = 200;

    /// <summary>Teammates' searches the replay showed no chest for, out of your view.</summary>
    /// <param name="myPos">Your position at a time (null when unknown, e.g. after you were eliminated: the replay then
    /// follows a teammate, so searches near them are shown and nothing is counted as unseen).</param>
    public static List<SearchMark> Unseen(IEnumerable<SearchStart> starts, IReadOnlyList<ChestEvent> shown, Func<double, (double x, double y)?> myPos, double after)
    {
        var list = new List<SearchMark>();
        foreach (var s in starts.Where(s => s.Role == 1 && s.T >= after && !double.IsNaN(s.X) && (s.Seconds ?? 0.8) >= MinSeconds))
        {
            if (shown.Any(c => Math.Abs(c.T - s.T) <= 4 && Dist(c.Cx ?? c.X, c.Cy ?? c.Y, s.X, s.Y) <= 6)) continue;
            if (myPos(s.T) is not { } me || Dist(me.x, me.y, s.X, s.Y) <= OutOfViewM) continue;
            list.Add(new SearchMark(Math.Round(s.T, 1), Math.Round(s.X), Math.Round(s.Y)));
        }
        return list;
    }

    private static double Dist(double? ax, double? ay, double bx, double by) =>
        ax is double x && ay is double y ? Math.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by)) / 100 : double.MaxValue;
}

/// <summary>Spots where a rolling chest has been opened in your matches (the map is the same all season). Grows with
/// every match, so estimates get better the more you play.</summary>
public sealed class ChestSpots
{
    public int Season { get; set; }
    public List<double[]> Spots { get; set; } = new();   // [x, y] in game units (cm)
    private Dictionary<(int, int), List<double[]>> _grid;

    public int Count => Spots.Count;

    /// <summary>Add the chest positions from one match's map (rolling chest kinds only: ammo boxes and crates never roll).</summary>
    public bool AddFrom(MapData map)
    {
        var added = false;
        foreach (var c in map.Chests.Where(c => Treasure.IsRewardChest(c.Chest)))
            if ((c.Cx ?? c.X) is double x && (c.Cy ?? c.Y) is double y && Nearest(x, y) > 1.5) { Add(x, y); added = true; }
        return added;
    }

    /// <summary>Unseen searches at a known chest spot.</summary>
    public int Estimate(IEnumerable<SearchMark> unseen) => unseen?.Count(s => Nearest(s.X, s.Y) <= TeamEstimate.SpotRadiusM) ?? 0;

    /// <summary>Distance in metres to the nearest known spot (double.MaxValue if none within 20 m).</summary>
    public double Nearest(double x, double y)
    {
        Index();
        var best = double.MaxValue;
        var (gx, gy) = Cell(x, y);
        for (var a = -1; a <= 1; a++)
            for (var b = -1; b <= 1; b++)
                if (_grid.TryGetValue((gx + a, gy + b), out var l))
                    foreach (var s in l) best = Math.Min(best, Math.Sqrt((s[0] - x) * (s[0] - x) + (s[1] - y) * (s[1] - y)) / 100);
        return best;
    }

    private void Add(double x, double y)
    {
        var s = new[] { Math.Round(x), Math.Round(y) };
        Spots.Add(s); Index();
        var k = Cell(x, y);
        if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new();
        l.Add(s);
    }

    private static (int, int) Cell(double x, double y) => ((int)Math.Floor(x / 2000), (int)Math.Floor(y / 2000));   // 20 m cells

    private void Index()
    {
        if (_grid != null) return;
        _grid = new();
        foreach (var s in Spots)
        {
            var k = Cell(s[0], s[1]);
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new();
            l.Add(s);
        }
    }

    public static ChestSpots Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<ChestSpots>(File.ReadAllText(path)) ?? new(); } catch { }
        return new();
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));
}
