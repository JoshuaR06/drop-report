namespace DropReport.Analysis;

/// <summary>A reward any chest can roll this season. OneIn = Epic's published per-chest odds (Fortnite Status, 7 Nov 2026).
/// Weekly rewards rotate each treasure-hunt week (Sunday 14:00 UTC).</summary>
public sealed record ChestReward(string Name, double OneIn, bool Weekly, bool Boosted);

/// <summary>A Treasure Hours session: the token's odds are multiplied for chests opened inside it.
/// Confirmed = Epic's own figure; otherwise the multiplier is what guides reported for that date.</summary>
public sealed record BoostSession(DateTime StartUtc, DateTime EndUtc, double Multiplier, bool Confirmed);

/// <summary>Which chest searches you believe roll for the rewards. Nobody outside Epic knows for sure whether refilled or
/// broken chests count, or whether a teammate's find shares the token, so these are the user's choice.</summary>
/// <param name="Unseen">With Team: also count teammates' chest searches out of the replay's view (estimated, TeamEstimate).</param>
public sealed record CountRule(bool Refills, bool Broken, bool Team, bool Unseen = true)
{
    /// <summary>Default: everything the game's logic lets roll. Opening or breaking a chest requests a roll, and a refill
    /// resets the chest's roll flag (B_BuildingContainerOverride_TreasureHunt), so refilled and broken chests count.</summary>
    public static readonly CountRule Default = new(Refills: true, Broken: true, Team: true);
}

public static class Treasure
{
    public const string Token = "Pirate Shop Access";
    /// <summary>The treasure season (Fortnite Treasure Hunters, game version 43).</summary>
    public const int Season = 43;

    public static IReadOnlyList<ChestReward> Rewards { get; private set; } = new ChestReward[]
    {
        new(Token, 137_500, Weekly: false, Boosted: true),
        new("Gold outfit", 1_562_500, Weekly: true, Boosted: false),
        new("Vehicle and decal", 7_813, Weekly: true, Boosted: false),
        new("Pickaxe or back bling", 568, Weekly: true, Boosted: false),
        new("Jam track", 391, Weekly: true, Boosted: false),
        new("Loading screen", 272, Weekly: true, Boosted: false),
        new("Any Gizmo", 127, Weekly: false, Boosted: false),
    };

    /// <summary>The treasure hunt's last week ends 5 Dec 2026 14:00 UTC (Epic's live config, DT_DelilahTreasureHuntFrontend_Weeks
    /// hotfix seen 15 Nov).</summary>
    public static DateTime SeasonEndUtc { get; private set; } = new(2026, 12, 5, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>Treasure-hunt weeks run Sunday 14:00 UTC to Sunday 14:00 UTC (same live config). The weekly prizes rotate then.</summary>
    public static DateTime WeekStartUtc(DateTime nowUtc)
    {
        var d = nowUtc.Date.AddHours(14);
        while (d.DayOfWeek != DayOfWeek.Sunday || d > nowUtc) d = d.AddDays(-1);
        return d;
    }

    // Saturdays, 2-4 PM and 9-11 PM US Eastern (EST = UTC-5 in November). 7 Nov: 1.5x then 2x (Epic). Later dates: 1.5x as reported.
    public static IReadOnlyList<BoostSession> Sessions { get; private set; } = BuildSessions();

    /// <summary>Where the odds and schedule came from ("built in" or the date of the downloaded file).</summary>
    public static string Source { get; private set; } = "built in";

    /// <summary>The published odds file (data/treasure.json in the repo): lets odds and Treasure Hours be corrected
    /// without a new release.</summary>
    public sealed record Data(int Season, DateTime SeasonEndUtc, List<ChestReward> Rewards, List<BoostSession> Sessions, string Updated);

    /// <summary>Use a downloaded odds file. Ignored unless it is for this season and looks sane (token first, odds > 1).</summary>
    public static bool Apply(Data d)
    {
        if (d == null || d.Season != Season || d.Rewards is not { Count: > 0 } || d.Rewards[0].Name != Token || d.Rewards.Any(r => r.OneIn <= 1)
            || d.Sessions == null || d.Sessions.Any(s => s.EndUtc <= s.StartUtc || s.Multiplier < 1)) return false;
        Rewards = d.Rewards; Sessions = d.Sessions.OrderBy(s => s.StartUtc).ToList(); SeasonEndUtc = d.SeasonEndUtc;
        Source = "updated " + d.Updated;
        return true;
    }

    /// <summary>The built-in values as an odds file (what data/treasure.json starts from).</summary>
    public static Data BuiltIn() => new(Season, SeasonEndUtc, Rewards.ToList(), Sessions.ToList(), "2026-11-16");

    private static List<BoostSession> BuildSessions()
    {
        var list = new List<BoostSession>();
        foreach (var day in new[] { 7, 14, 21, 28, 35 })   // 35 Nov = 5 Dec
        {
            var d = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1);
            list.Add(new BoostSession(d.AddHours(19), d.AddHours(21), 1.5, Confirmed: day == 7));
            list.Add(new BoostSession(d.AddHours(26), d.AddHours(28), day == 7 ? 2.0 : 1.5, Confirmed: day == 7));
        }
        return list;
    }

    /// <summary>The token multiplier for a chest opened at this moment (1 outside Treasure Hours).</summary>
    public static BoostSession SessionAt(DateTime utc) => Sessions.FirstOrDefault(s => utc >= s.StartUtc && utc < s.EndUtc);

    /// <summary>The next Treasure Hours session that hasn't ended yet, or null.</summary>
    public static BoostSession NextSession(DateTime nowUtc) => Sessions.FirstOrDefault(s => s.EndUtc > nowUtc && s.StartUtc < SeasonEndUtc);

    /// <summary>Prize id -> name, from the game's prize table (DT_DelilahTreasureHunt_Prizes, v43.10). Rows 14-33 are the
    /// later weeks' rotations of the same five cosmetic kinds.</summary>
    public static string PrizeName(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id.Contains("9f73f94d", StringComparison.OrdinalIgnoreCase)) return Token;
        var m = System.Text.RegularExpressions.Regex.Match(id, @"dpid_item(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return id;
        var n = int.Parse(m.Groups[1].Value);
        return n switch
        {
            1 => "Gold Outfit", 2 => "Pickaxe", 3 => "Jam Track", 4 => "Loading Screen", 5 => "Gold Car Decal", 6 => Token,
            9 => "Sprite Gizmo: Extraction Accelerator", 10 => "Sprite Gizmo: Portable Extractor", 11 => "Sprite Gizmo: Golden Firework",
            12 => "Sprite Gizmo: Sprite Locator", 13 => "Sprite Gizmo: Lucky Locator",
            >= 14 and <= 33 => new[] { "Gold Outfit", "Back Bling", "Jam Track", "Loading Screen", "Gold Car Decal" }[(n - 14) % 5],
            _ => id,
        };
    }

    /// <summary>One sentence per reveal. The game spawns its gold treasure chest only for Major or Supreme prizes (weekly
    /// cosmetics, Gold Outfit, Pirate Shop Access): B_TreasureHuntChestRoll clears the chest class for tier Standard (Gizmos).</summary>
    public static string Describe(TreasureReveal r, double busJump = 0)
    {
        var who = r.Who switch { "me" => "you", "teammate" => "a teammate", "other" => "another player", _ => "someone" };
        var clock = $"{(int)(r.T / 60)}:{(int)(r.T % 60):00}";
        var chest = r.Chest.ToLowerInvariant().Contains("chest") ? r.Chest.ToLowerInvariant() : "chest";
        return $"At {clock} {who} {(r.Broken ? "broke" : "opened")} a {chest} and the game's gold treasure chest appeared. The game only shows it for a Major or Supreme prize: a weekly cosmetic, a Gold Outfit or Pirate Shop Access (Gizmo wins skip it). " +
               (r.Prize != null ? $"Prize: {r.Prize}." : "The replay doesn't carry which prize" + (r.Broken ? ", or whether breaking the chest still granted it." : "."));
    }

    /// <summary>What set off a treasure earned at time t: the nearest chest search or break by you or a teammate in the 2.5 s
    /// before (a gold treasure chest adds about 2 s of animation; other prizes land within about half a second). Seen in 15 of
    /// 15 matches checked on 16 Nov 2026; no chest in view = "unseen" (a teammate out of the replay's range).</summary>
    public static TreasureMoment Attribute(double t, IEnumerable<ChestEvent> chests, IEnumerable<TreasureReveal> reveals)
    {
        // a gold treasure chest just before: its opener is the one (the chest animation runs about 2 s before the prize lands)
        var rv = reveals.Where(r => r.Who is "me" or "teammate" && r.T <= t + 0.3 && r.T >= t - 3.5).OrderBy(r => t - r.T).FirstOrDefault();
        if (rv != null)
        {
            var src = chests.Where(c => c.Who == rv.Who && Math.Abs(c.T - rv.T) <= 1.0).OrderBy(c => Math.Abs(c.T - rv.T)).FirstOrDefault();
            return new TreasureMoment(t, rv.Who, rv.Broken ? "broken" : "opened", src?.Chest ?? rv.Chest, true, src?.Refill ?? false);
        }
        var e = chests.Where(c => c.Who is "me" or "teammate" && c.Kind is "opened" or "broken" && c.T <= t + 0.3 && c.T >= t - 2.5)
                      .OrderBy(c => Math.Abs(t - c.T)).FirstOrDefault();
        var gold = reveals.Any(r => r.T <= t + 0.3 && r.T >= t - 3.5);
        return e == null ? new TreasureMoment(t, "unseen", null, null, gold, false) : new TreasureMoment(t, e.Who, e.Kind, e.Chest, gold, e.Refill);
    }

    /// <summary>One sentence for a treasure earned.</summary>
    public static string Describe(TreasureMoment p)
    {
        var clock = $"{(int)(p.T / 60)}:{(int)(p.T % 60):00}";
        var chest = p.Chest == null ? "chest" : p.Chest.ToLowerInvariant().Contains("chest") ? p.Chest.ToLowerInvariant() : "chest";
        var how = p.Trigger switch
        {
            "me" => $"you {(p.Kind == "broken" ? "broke" : "opened")} a {(p.Refill ? "refilled " : "")}{chest}",
            "teammate" => $"a teammate {(p.Kind == "broken" ? "broke" : "opened")} a {(p.Refill ? "refilled " : "")}{chest} (their chest, your treasure)",
            "teammate-far" => "a teammate out of view searched a chest (their chest, your treasure; the replay saw them searching, not the chest)",
            _ => "no chest was in view (likely a teammate's chest out of range)",
        };
        return $"Treasure earned at {clock}: {how}." + (p.Gold ? " The gold treasure chest appeared, so it was a Major or Supreme prize (a weekly cosmetic, the Gold Outfit or Pirate Shop Access)." : "");
    }

    public static bool IsRewardChest(string type) => type != null && type.Contains("chest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chest searches in one match that roll for rewards under this rule.</summary>
    public static int Eligible(MatchSummary m, CountRule rule)
    {
        int Count(IEnumerable<ChestTypeCount> rows) => rows.Where(r => IsRewardChest(r.Type)).Sum(r =>
            (r.Opened - (rule.Refills ? 0 : r.OpenedRefill)) + (rule.Broken ? r.Broken - (rule.Refills ? 0 : r.BrokenRefill) : 0));
        return Count(m.Chests.ByType) + (rule.Team ? Count(m.Chests.Team) + (rule.Unseen ? m.TeamEstimated : 0) : 0);
    }

    /// <summary>Chance of at least one hit in a set of rolls with per-roll chance p_i: 1 - prod(1 - p_i).</summary>
    public static double AtLeastOne(IEnumerable<double> perRoll) => 1 - Math.Exp(perRoll.Sum(p => Math.Log(1 - Math.Min(p, 1))));

    /// <summary>Same, for n rolls at the same chance.</summary>
    public static double AtLeastOne(double n, double p) => n <= 0 ? 0 : 1 - Math.Exp(n * Math.Log(1 - p));

    /// <summary>Rolls needed for a given chance of at least one hit.</summary>
    public static double RollsFor(double chance, double p) => Math.Log(1 - chance) / Math.Log(1 - p);

    public sealed record MatchRolls(MatchSummary Match, int Chests, double Multiplier);

    /// <summary>Everything the Treasure Hunt page shows, for the matches given (already filtered to this season).</summary>
    public sealed record Report(
        int Chests, int BoostedChests, double TokenChance, double TokenRolls,   // TokenRolls = chests weighted by boost
        int Matches, double ChestsPerMatch, double ChestsPerDay, int DaysLeft,
        double ProjectedChests, double ProjectedChance, IReadOnlyList<(double Chance, double Chests)> Milestones,
        IReadOnlyList<(ChestReward Reward, double Chance, double Rolls)> Others, double ChestsPerWeek, int ChestsThisWeek, List<MatchRolls> PerMatch,
        int Refills, int Broken, int TeamChests, int TeamEstimated = 0);


    public static Report Build(IReadOnlyCollection<MatchSummary> ms, CountRule rule, DateTime nowUtc)
    {
        var p = 1.0 / Rewards[0].OneIn;
        var per = ms.Where(m => m.Start != null).OrderBy(m => m.Start).Select(m =>
        {
            var utc = DateTime.SpecifyKind(m.Start.Value, DateTimeKind.Local).ToUniversalTime();
            return new MatchRolls(m, Eligible(m, rule), SessionAt(utc)?.Multiplier ?? 1.0);
        }).ToList();
        var chests = per.Sum(x => x.Chests);
        var rolls = per.Sum(x => x.Chests * x.Multiplier);
        var chance = AtLeastOne(per.Select(x => Enumerable.Repeat(p * x.Multiplier, x.Chests)).SelectMany(x => x));

        // pace: eligible chests per calendar day across the days you actually played this season
        var first = per.Count > 0 ? DateTime.SpecifyKind(per[0].Match.Start.Value, DateTimeKind.Local).ToUniversalTime() : nowUtc;
        var spanDays = Math.Max(1.0, (nowUtc - first).TotalDays);
        var perDay = chests / spanDays;
        var daysLeft = Math.Max(0, (int)Math.Ceiling((SeasonEndUtc - nowUtc).TotalDays));
        var projected = chests + perDay * daysLeft;
        var projectedChance = 1 - (1 - chance) * Math.Exp(perDay * daysLeft * Math.Log(1 - p));

        var milestones = new[] { 0.10, 0.25, 0.50 }.Select(c => (c, RollsFor(c, p))).ToList();
        // weekly rewards: this week's chests (the week resets Sunday 14:00 UTC); the rest: across the season so far
        var perWeek = perDay * 7;
        var weekStart = WeekStartUtc(nowUtc);
        var thisWeek = per.Where(x => DateTime.SpecifyKind(x.Match.Start.Value, DateTimeKind.Local).ToUniversalTime() >= weekStart).Sum(x => x.Chests);
        var others = Rewards.Skip(1).Select(r => (r, r.Weekly ? AtLeastOne(thisWeek, 1.0 / r.OneIn) : AtLeastOne(chests, 1.0 / r.OneIn), r.Weekly ? thisWeek : (double)chests)).ToList();

        int Sum(Func<ChestTypeCount, int> f, bool team) => ms.Sum(m => (team ? m.Chests.Team : m.Chests.ByType).Where(r => IsRewardChest(r.Type)).Sum(f));
        return new Report(chests, per.Where(x => x.Multiplier > 1).Sum(x => x.Chests), chance, rolls,
            per.Count, per.Count > 0 ? (double)chests / per.Count : 0, perDay, daysLeft, projected, projectedChance, milestones, others, perWeek, thisWeek, per,
            Sum(r => r.OpenedRefill + r.BrokenRefill, false) + Sum(r => r.OpenedRefill + r.BrokenRefill, true),
            Sum(r => r.Broken, false) + Sum(r => r.Broken, true), Sum(r => r.Opened + r.Broken, true),
            rule.Team && rule.Unseen ? ms.Sum(m => m.TeamEstimated) : 0);
    }
}
