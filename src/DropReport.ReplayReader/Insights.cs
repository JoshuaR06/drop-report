namespace DropReport.Analysis;

/// <summary>
/// Turns season totals into things worth knowing: which weapons you're actually best with, where to drop, and what's
/// working or not. Every ranking needs a minimum sample, so one lucky match can't top a list.
/// </summary>
public static class Insights
{
    // ------------------------------------------------------------------ weapons
    public sealed record WeaponScore(string Name, string Rarity, string Class, int Elims, int Knocks, int PlayerElims, double Damage, int Hits,
                                     int Headshots, int Matches, double? MedianDist)
    {
        public int Fights => Elims + Knocks;
        public double? HeadshotPct => Hits > 0 ? 100.0 * Headshots / Hits : null;
        public double? DamagePerMatch => Hits > 0 && Matches > 0 ? Damage / Matches : null;
        public double ElimsPerMatch => Matches > 0 ? (double)Elims / Matches : 0;
    }

    /// <summary>A headline pick: "Most eliminations: Hammer Assault Rifle, 29".</summary>
    public sealed record Pick(string Title, string Name, string Rarity, string Value, string Detail);

    public sealed record ClassRow(string Class, int Elims, int Knocks, double Share, double? MedianDist);

    static double? Median(IEnumerable<double> v)
    {
        var a = v.OrderBy(x => x).ToList();
        return a.Count == 0 ? null : a.Count % 2 == 1 ? a[a.Count / 2] : (a[a.Count / 2 - 1] + a[a.Count / 2]) / 2;
    }

    public static List<WeaponScore> Weapons(IReadOnlyCollection<MatchSummary> ms)
    {
        var s = SeasonStats.Compute(ms);
        var mine = ms.SelectMany(m => m.Fights.Mine).ToList();
        return s.Weapons.Select(w =>
        {
            var f = mine.Where(x => (x.Weapon ?? x.Cause) == w.Name).ToList();
            var cls = f.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            return new WeaponScore(w.Name, w.BestRarity, cls, w.Elims, w.Knocks, f.Count(x => x.Credit && x.Victim == "player"),
                w.Damage, w.Hits, w.Headshots, w.Matches, Median(f.Where(x => x.Dist != null).Select(x => x.Dist.Value)));
        }).ToList();
    }

    public static List<Pick> BestWeapons(IReadOnlyList<WeaponScore> ws)
    {
        var picks = new List<Pick>();
        void Add(string title, IEnumerable<WeaponScore> pool, Func<WeaponScore, double> by, Func<WeaponScore, string> value, Func<WeaponScore, string> detail)
        {
            var w = pool.OrderByDescending(by).FirstOrDefault();
            if (w != null) picks.Add(new Pick(title, w.Name, w.Rarity, value(w), detail(w)));
        }
        static string Ms(int n) => n == 1 ? "1 match" : $"{n} matches";
        Add("Most eliminations", ws.Where(w => w.Fights >= 3), w => w.Elims + w.Knocks / 100.0, w => $"{w.Elims}",
            w => $"{w.Knocks} knocks in {Ms(w.Matches)}");
        Add("Most damage a match", ws.Where(w => w.Hits >= 30 && w.Matches >= 3), w => w.DamagePerMatch ?? 0, w => $"{w.DamagePerMatch:N0}",
            w => $"{w.Damage:N0} recorded over {Ms(w.Matches)}");
        Add("Best aim", ws.Where(w => w.Hits >= 40), w => w.HeadshotPct ?? 0, w => $"{w.HeadshotPct:0}%",
            w => $"headshots, {w.Headshots} of {w.Hits} hits");
        Add("Up close", ws.Where(w => w.Fights >= 3 && w.MedianDist is <= 12), w => w.Fights, w => $"{w.Fights}",
            w => $"knocks and elims, typically at {w.MedianDist:0} m");
        Add("At range", ws.Where(w => w.Fights >= 3 && w.MedianDist is >= 35), w => w.Fights, w => $"{w.Fights}",
            w => $"knocks and elims, typically at {w.MedianDist:0} m");
        Add("Against real players", ws.Where(w => w.PlayerElims >= 2), w => w.PlayerElims, w => $"{w.PlayerElims}",
            w => "player eliminations (not bots)");
        return picks;
    }

    /// <summary>Your knocks and elims by weapon type (from Epic's elimination events, so complete).</summary>
    public static List<ClassRow> Classes(IReadOnlyCollection<MatchSummary> ms)
    {
        var f = ms.SelectMany(m => m.Fights.Mine).Where(x => x.Cause is not ("team wiped" or "fall" or "bled out") && !x.Cause.StartsWith("cause ")).ToList();
        var total = Math.Max(1, f.Count);
        return f.GroupBy(ClassOf).Select(g => new ClassRow(g.Key, g.Count(x => x.Credit), g.Count(x => x.Kind == "knock"),
            (double)g.Count() / total, Median(g.Where(x => x.Dist != null).Select(x => x.Dist.Value))))
            .OrderByDescending(c => c.Elims + c.Knocks).ToList();
    }

    /// <summary>Epic files slingshots under pistols; the weapon name tells them apart.</summary>
    public static string ClassOf(FightEvent f) => f.Weapon != null && f.Weapon.Contains("Slingshot", StringComparison.OrdinalIgnoreCase) ? "slingshot" : f.Cause;

    // ------------------------------------------------------------------ drop spots
    public sealed record SpotRow(string Spot, int Matches, double AvgPlace, int Wins, int Top10, double ElimsPerMatch, double ChestsPerMatch, int EarlyDeaths)
    {
        public double Top10Rate => Matches > 0 ? (double)Top10 / Matches : 0;
        public double EarlyDeathRate => Matches > 0 ? (double)EarlyDeaths / Matches : 0;
    }

    /// <summary>Died within 4 minutes of jumping: the drop went wrong.</summary>
    public static bool EarlyDeath(MatchSummary m) => m.Death != null && m.Death.T - m.BusJump < 240 && m.Placement is not 1;

    public static int TeamChests(MatchSummary m) => m.Chests.Opened + m.Chests.Broken + m.Chests.Team.Sum(t => t.Opened + t.Broken);

    public static List<SpotRow> Spots(IReadOnlyCollection<MatchSummary> ms) =>
        ms.Where(m => m.Landing != null && m.Placement is > 0).GroupBy(m => m.Landing).Select(g => new SpotRow(g.Key, g.Count(),
            g.Average(m => m.Placement.Value), g.Count(m => m.Placement == 1), g.Count(m => m.Placement <= 10),
            g.Average(m => (double)(m.Epic.Elims ?? 0)), g.Average(m => (double)TeamChests(m)), g.Count(EarlyDeath)))
          .OrderByDescending(s => s.Matches).ThenBy(s => s.AvgPlace).ToList();

    /// <summary>A spot's average pulled toward your overall average by `prior` imaginary average drops, so 5 drops
    /// can't beat 11 on a rounding error (a standard "shrinkage" estimate).</summary>
    static double Shrunk(double avg, int n, double overall, double prior = 3) => (avg * n + overall * prior) / (n + prior);

    /// <summary>Best spot for placement / loot / fights, among spots with at least minMatches drops.</summary>
    public static List<Pick> BestSpots(IReadOnlyList<SpotRow> spots, int minMatches = 3)
    {
        var pool = spots.Where(s => s.Matches >= minMatches).ToList();
        var picks = new List<Pick>();
        if (pool.Count == 0) return picks;
        double All(Func<SpotRow, double> f) => spots.Sum(s => f(s) * s.Matches) / spots.Sum(s => s.Matches);
        var (aPlace, aChests, aElims) = (All(s => s.AvgPlace), All(s => s.ChestsPerMatch), All(s => s.ElimsPerMatch));
        var place = pool.OrderBy(s => Shrunk(s.AvgPlace, s.Matches, aPlace)).First();
        picks.Add(new Pick("Best for placement", place.Spot, null, $"{place.AvgPlace:0.#}", $"average place over {place.Matches} drops, {place.Top10} top 10s"));
        var loot = pool.OrderByDescending(s => Shrunk(s.ChestsPerMatch, s.Matches, aChests)).First();
        picks.Add(new Pick("Best for chests", loot.Spot, null, $"{loot.ChestsPerMatch:0.#}", $"team chests a match over {loot.Matches} drops"));
        var fights = pool.OrderByDescending(s => Shrunk(s.ElimsPerMatch, s.Matches, aElims)).First();
        picks.Add(new Pick("Best for fights", fights.Spot, null, $"{fights.ElimsPerMatch:0.#}", $"eliminations a match over {fights.Matches} drops"));
        var risky = pool.OrderByDescending(s => s.EarlyDeathRate).First();
        if (risky.EarlyDeaths >= 2)
            picks.Add(new Pick("Riskiest drop", risky.Spot, null, $"{risky.EarlyDeathRate:P0}", $"of drops ended in the first 4 minutes ({risky.EarlyDeaths} of {risky.Matches})"));
        return picks;
    }

    // ------------------------------------------------------------------ one match against your usual
    /// <param name="Better">true = better than usual, false = worse, null = about the same or no history.</param>
    public sealed record Versus(string Label, string Value, string Delta, bool? Better);

    /// <summary>This match next to your average in the same mode and season (up to the last 30 others; any season if this
    /// season has fewer than 5). Placement: lower is better.</summary>
    public static List<Versus> VsUsual(MatchSummary m, IEnumerable<MatchSummary> all)
    {
        var sameMode = all.Where(o => o.File != m.File && o.Mode == m.Mode).OrderByDescending(o => o.Start).ToList();
        var thisSeason = sameMode.Where(o => o.Season == m.Season).ToList();
        var usual = (thisSeason.Count >= 5 ? thisSeason : sameMode).Take(30).ToList();
        Versus One(string label, double? mine, IEnumerable<double?> theirs, string fmt, bool lowerBetter = false, double same = 0.1)
        {
            var v = theirs.Where(x => x != null).Select(x => x!.Value).ToList();
            var value = mine == null ? "–" : mine.Value.ToString(fmt);
            if (mine == null || v.Count < 3) return new(label, value, "", null);
            var avg = v.Average(); var d = mine.Value - avg;
            if (Math.Abs(d) <= Math.Max(same, Math.Abs(avg) * 0.08)) return new(label, value, $"your usual ({avg.ToString(fmt == "N0" ? "N0" : "0.#")})", null);
            var better = lowerBetter ? d < 0 : d > 0;
            return new(label, value, $"{(d > 0 ? "+" : "−")}{Math.Abs(d).ToString(fmt == "N0" ? "N0" : "0.#")} vs your usual", better);
        }
        return new()
        {
            One("PLACE", m.Placement, usual.Select(o => (double?)o.Placement), "0", lowerBetter: true, same: 1),
            One("ELIMINATIONS", m.Epic.Elims, usual.Select(o => (double?)o.Epic.Elims), "0"),
            One("DAMAGE", m.Epic.DamageToPlayers, usual.Select(o => (double?)o.Epic.DamageToPlayers), "N0", same: 50),
            One("CHESTS", TeamChests(m), usual.Select(o => (double?)TeamChests(o)), "0", same: 1),
        };
    }

    /// <summary>Personal records this match set, against every match before it (any mode).</summary>
    public static List<string> Records(MatchSummary m, IEnumerable<MatchSummary> all)
    {
        var before = all.Where(o => o.File != m.File && (o.Start == null || m.Start == null || o.Start < m.Start)).ToList();
        var r = new List<string>();
        if (before.Count < 5) return r;
        if ((m.Epic.Elims ?? 0) > 0 && (m.Epic.Elims ?? 0) > before.Max(o => o.Epic.Elims ?? 0)) r.Add($"New record: {m.Epic.Elims} eliminations in a match");
        if ((m.Epic.DamageToPlayers ?? 0) > before.Max(o => o.Epic.DamageToPlayers ?? 0)) r.Add($"New record: {m.Epic.DamageToPlayers:N0} damage in a match");
        if (TeamChests(m) > before.Max(TeamChests)) r.Add($"New record: {TeamChests(m)} chests in a match");
        if (m.Placement == 1 && !before.Any(o => o.Placement == 1 && o.Season == m.Season)) r.Add($"First Victory Royale of season {m.Season}");
        if (m.Fights.ElimsPlayers > 0 && m.Fights.ElimsPlayers > before.Max(o => o.Fights.ElimsPlayers)) r.Add($"New record: {m.Fights.ElimsPlayers} real players eliminated");
        return r;
    }

    /// <summary>Records plus moments worth a chip: a treasure reveal you or your team set off.</summary>
    public static List<string> Highlights(MatchSummary m, IEnumerable<MatchSummary> all)
    {
        var r = new List<string>();
        foreach (var t in (m.Reveals ?? new()).Where(x => x.Who is "me" or "teammate"))
            r.Add(t.Prize != null ? $"Treasure: {t.Prize}" : t.Who == "me" ? (t.Broken ? "Gold treasure chest! (you broke it)" : "Gold treasure chest!") : "Your team found a gold treasure chest");
        r.AddRange(Records(m, all));
        return r;
    }

    // ------------------------------------------------------------------ strengths and weaknesses
    /// <param name="Kind">strength, weakness or tip</param>
    public sealed record Note(string Kind, string Headline, string Detail);

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
    static string Plural(string cls) => cls switch { "SMG" => "SMGs", "melee" => "melee weapons", "pistol" => "pistols and slingshots", _ => cls.EndsWith("s") ? cls : cls + "s" };

    public static List<Note> Notes(IReadOnlyCollection<MatchSummary> ms)
    {
        var notes = new List<Note>();
        if (ms.Count < 5) return notes;
        var s = SeasonStats.Compute(ms);

        // what wins your fights
        var classes = Classes(ms);
        if (classes.OrderByDescending(c => c.Elims + c.Knocks).ThenBy(c => c.Class, StringComparer.Ordinal).FirstOrDefault() is { } top && top.Elims + top.Knocks >= 5)
            notes.Add(new Note("strength", $"{Cap(top.Class == "pistol" ? "pistols" : Plural(top.Class))} win your fights",
                $"{top.Share:P0} of your knocks and eliminations{(top.MedianDist != null ? $", typically at {top.MedianDist:0} m" : "")}."));

        // finishing knocks
        if (s.Knocks >= 10)
        {
            var rate = (double)s.KnocksIFinished / s.Knocks;
            notes.Add(rate >= 0.75
                ? new Note("strength", "You finish what you start", $"You finished {rate:P0} of your own knocks ({s.KnocksIFinished} of {s.Knocks}).")
                : new Note("weakness", "Knocks are getting away", $"You finished only {rate:P0} of your knocks ({s.KnocksIFinished} of {s.Knocks}). Teammates or revives took the rest."));
        }

        // what ends you
        var deaths = ms.Where(m => m.Death != null).Select(m => m.Death).ToList();
        if (deaths.Count >= 5)
        {
            var g = deaths.Where(d => !d.Cause.StartsWith("cause ")).GroupBy(d => d.Cause).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First();
            var med = Median(g.Where(d => d.Dist != null).Select(d => d.Dist.Value));
            if (g.Count() >= 3)
                notes.Add(new Note("weakness", $"{Cap(Plural(g.Key))} end you most",
                    $"{g.Count()} of your {deaths.Count} deaths{(med != null ? $", usually from about {med:0} m" : "")}."));
        }

        // drops
        var early = ms.Count(EarlyDeath);
        if (early >= 3 && (double)early / ms.Count >= 0.2)
            notes.Add(new Note("weakness", "Early fights are costing you", $"{early} of {ms.Count} matches ended within 4 minutes of the jump."));
        var spots = BestSpots(Spots(ms));
        if (spots.FirstOrDefault(p => p.Title == "Best for placement") is { } best)
            notes.Add(new Note("tip", $"Drop {best.Name} for wins", $"Average place {best.Value} there, your best spot with 3 or more drops."));

        // who you're eliminating
        if (s.ElimsPlayers + s.ElimsBots >= 10)
        {
            var bots = (double)s.ElimsBots / (s.ElimsPlayers + s.ElimsBots);
            if (bots >= 0.6)
                notes.Add(new Note("tip", "Most of your eliminations are bots", $"{bots:P0} of them. Real-player eliminations: {s.ElimsPlayers}."));
            else
                notes.Add(new Note("strength", "You beat real players", $"{1 - bots:P0} of your eliminations are real players ({s.ElimsPlayers})."));
        }

        // closing out games
        var placed = ms.Where(m => m.Placement is > 0).ToList();
        if (placed.Count >= 5)
        {
            var top10 = (double)placed.Count(m => m.Placement <= 10) / placed.Count;
            if (top10 >= 0.5) notes.Add(new Note("strength", "You make it to the end", $"Top 10 in {top10:P0} of matches, {s.Wins} {(s.Wins == 1 ? "win" : "wins")}."));
        }
        return notes;
    }
}
