namespace DropReport.Analysis;

/// <summary>Totals across a set of matches (one season / one mode), for the Season, Weapons and Loot pages.</summary>
public sealed class SeasonStats
{
    public sealed record WeaponRow(string Name, string BestRarity, int Elims, int Knocks, double Damage, int Hits, int Headshots, int Matches)
    {
        public double? HeadshotPct => Hits > 0 ? 100.0 * Headshots / Hits : null;
    }
    public sealed record DeathRow(string Cause, string By, int Times, double? MedianDist);
    public sealed record CountRow(string Name, string Rarity, string Kind, int Count);
    public sealed record ChestRow(string Type, int Me, int Team);

    public int Matches { get; init; }
    public double Minutes { get; init; }
    public int Wins { get; init; }
    public double? AvgPlacement { get; init; }
    public int ChestsMe { get; init; }
    public int ChestsTeam { get; init; }
    public int BrokenMe { get; init; }
    public int BrokenTeam { get; init; }
    public List<ChestRow> ChestTypes { get; init; } = new();
    public int Elims { get; init; }
    public int Knocks { get; init; }
    public int KnocksIFinished { get; init; }
    public int ElimsPlayers { get; init; }
    public int ElimsBots { get; init; }
    public double? DamagePerMatch { get; init; }
    public int Hits { get; init; }
    public int Headshots { get; init; }
    public double? Accuracy { get; init; }
    public int Deaths { get; init; }
    public int KnockedFirst { get; init; }
    public int BledOut { get; init; }
    public List<WeaponRow> Weapons { get; init; } = new();
    public List<DeathRow> DeathCauses { get; init; } = new();
    public List<CountRow> SeasonItems { get; init; } = new();
    public List<CountRow> Loot { get; init; } = new();

    public int Chest(string type, bool team) => ChestTypes.Where(c => c.Type == type).Sum(c => team ? c.Team : c.Me);
    public int SeasonItemCount(params string[] kinds) => SeasonItems.Where(i => kinds.Contains(i.Kind)).Sum(i => i.Count);

    public static SeasonStats Compute(IReadOnlyCollection<MatchSummary> ms)
    {
        var places = ms.Where(m => m.Placement is > 0).Select(m => m.Placement.Value).ToList();
        var types = ms.SelectMany(m => m.Chests.ByType.Select(t => (t.Type, me: t.Opened + t.Broken, team: 0))
                     .Concat(m.Chests.Team.Select(t => (t.Type, me: 0, team: t.Opened + t.Broken))))
                     .GroupBy(x => x.Type).Select(g => new ChestRow(g.Key, g.Sum(x => x.me), g.Sum(x => x.team))).OrderByDescending(c => c.Me + c.Team).ToList();

        // weapons: knocks/elims from every match; damage/headshots from recorded hits
        var w = new Dictionary<string, (HashSet<string> rar, int elims, int knocks, double dmg, int hits, int hs, HashSet<string> matches)>();
        void Add(string name, string rarity, string match, int elims = 0, int knocks = 0, double dmg = 0, int hits = 0, int hs = 0)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!w.TryGetValue(name, out var o)) o = (new(), 0, 0, 0, 0, 0, new());
            if (rarity != null) o.rar.Add(rarity);
            o.matches.Add(match);
            w[name] = (o.rar, o.elims + elims, o.knocks + knocks, o.dmg + dmg, o.hits + hits, o.hs + hs, o.matches);
        }
        foreach (var m in ms)
        {
            foreach (var f in m.Fights.Mine) Add(f.Weapon ?? f.Cause, f.WeaponRarity, m.File, elims: f.Credit ? 1 : 0, knocks: f.Kind == "knock" ? 1 : 0);
            foreach (var p in m.Damage.PerWeapon.Where(p => p.Hits > 0)) Add(p.Weapon, p.Rarity, m.File, dmg: p.Damage, hits: p.Hits, hs: p.Headshots);
        }
        string Best(HashSet<string> r) => r.OrderByDescending(x => Array.IndexOf(Rules.RarityOrder, x)).FirstOrDefault();
        var weapons = w.Where(kv => kv.Key != "Pickaxe" && kv.Value.elims + kv.Value.knocks + kv.Value.hits > 0)
            .Select(kv => new WeaponRow(kv.Key, Best(kv.Value.rar), kv.Value.elims, kv.Value.knocks, Math.Round(kv.Value.dmg), kv.Value.hits, kv.Value.hs, kv.Value.matches.Count))
            .OrderByDescending(x => x.Elims + x.Knocks).ThenByDescending(x => x.Damage).ToList();

        var deaths = ms.Where(m => m.Death != null).Select(m => m.Death).ToList();
        static double? Median(List<double> v) { if (v.Count == 0) return null; v.Sort(); return v[v.Count / 2]; }
        var causes = deaths.GroupBy(d => (d.Cause, by: d.By == "me" ? "nobody (bled out)" : d.By))
            .Select(g => new DeathRow(g.Key.Cause, g.Key.by, g.Count(), Median(g.Where(d => d.Dist != null).Select(d => d.Dist.Value).ToList())))
            .OrderByDescending(d => d.Times).ToList();

        var season = ms.SelectMany(m => m.PickedUp).GroupBy(p => (p.Name, p.Rarity, p.Season))
            .Select(g => new CountRow(g.Key.Name, g.Key.Rarity, g.Key.Season, g.Count())).OrderByDescending(c => c.Count).ToList();
        var loot = ms.SelectMany(m => m.Chests.Taken).GroupBy(t => (t.Name, t.Rarity))
            .Select(g => new CountRow(g.Key.Name, g.Key.Rarity, null, g.Count())).OrderByDescending(c => c.Count).ToList();

        return new SeasonStats
        {
            Matches = ms.Count, Minutes = ms.Sum(m => m.Minutes), Wins = ms.Count(m => m.Placement == 1),
            AvgPlacement = places.Count > 0 ? places.Average() : null,
            ChestsMe = ms.Sum(m => m.Chests.Opened), BrokenMe = ms.Sum(m => m.Chests.Broken),
            ChestsTeam = ms.Sum(m => m.Chests.Team.Sum(t => t.Opened)), BrokenTeam = ms.Sum(m => m.Chests.Team.Sum(t => t.Broken)),
            ChestTypes = types,
            Elims = ms.Sum(m => m.Epic.Elims ?? 0), Knocks = ms.Sum(m => m.Fights.Knocks), KnocksIFinished = ms.Sum(m => m.Fights.MyKnocksIFinished),
            ElimsPlayers = ms.Sum(m => m.Fights.ElimsPlayers), ElimsBots = ms.Sum(m => m.Fights.ElimsBots),
            DamagePerMatch = ms.Count > 0 ? ms.Average(m => (double)(m.Epic.DamageToPlayers ?? 0)) : null,
            Hits = ms.Sum(m => m.Damage.Hits), Headshots = ms.Sum(m => m.Damage.Headshots),
            Accuracy = ms.Any(m => m.Epic.Accuracy != null) ? ms.Where(m => m.Epic.Accuracy != null).Average(m => m.Epic.Accuracy.Value) : null,
            Deaths = deaths.Count, KnockedFirst = deaths.Count(d => d.KnockedFirst), BledOut = deaths.Count(d => d.BledOut),
            Weapons = weapons, DeathCauses = causes, SeasonItems = season, Loot = loot,
        };
    }
}
