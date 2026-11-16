namespace DropReport.Analysis;

/// <summary>A reward any chest can roll this season. OneIn = Epic's published per-chest odds (Fortnite Status, 7 Nov 2026).
/// Weekly rewards rotate each treasure-hunt week (Sunday 14:00 UTC).</summary>
public sealed record ChestReward(string Name, double OneIn, bool Weekly, bool Boosted);

/// <summary>A Treasure Hours session: the token's odds are multiplied for chests opened inside it.
/// Confirmed = Epic's own figure; otherwise the multiplier is what guides reported for that date.</summary>
public sealed record BoostSession(DateTime StartUtc, DateTime EndUtc, double Multiplier, bool Confirmed);

/// <summary>Which chest searches you believe roll for the rewards. Nobody outside Epic knows for sure whether refilled or
/// broken chests count, or whether a teammate's find shares the token, so these are the user's choice.</summary>
public sealed record CountRule(bool Refills, bool Broken, bool Team)
{
    /// <summary>Default: fresh chests only (refilled ones don't roll), broken chests count, team chests count.</summary>
    public static readonly CountRule Default = new(Refills: false, Broken: true, Team: true);
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

    /// <summary>"At 10:33 a treasure-hunt chest appeared where you broke a chest." States only what the replay shows.</summary>
    public static string Describe(TreasureReveal r, double busJump = 0)
    {
        var who = r.Who switch { "me" => "you", "teammate" => "a teammate", "other" => "another player", _ => "someone" };
        var clock = $"{(int)(r.T / 60)}:{(int)(r.T % 60):00}";
        var chest = r.Chest.ToLowerInvariant().Contains("chest") ? r.Chest.ToLowerInvariant() : "chest";
        return $"At {clock} the game's treasure chest appeared as {who} {(r.Broken ? "broke" : "opened")} a {chest}. " +
               (r.Prize != null ? $"Prize: {r.Prize}." : "The replay doesn't carry the prize; your post-match \"Treasures found\" screen shows whether anything was won.");
    }

    public static bool IsRewardChest(string type) => type != null && type.Contains("chest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chest searches in one match that roll for rewards under this rule.</summary>
    public static int Eligible(MatchSummary m, CountRule rule)
    {
        int Count(IEnumerable<ChestTypeCount> rows) => rows.Where(r => IsRewardChest(r.Type)).Sum(r =>
            (r.Opened - (rule.Refills ? 0 : r.OpenedRefill)) + (rule.Broken ? r.Broken - (rule.Refills ? 0 : r.BrokenRefill) : 0));
        return Count(m.Chests.ByType) + (rule.Team ? Count(m.Chests.Team) : 0);
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
        int Refills, int Broken, int TeamChests);


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
            Sum(r => r.Broken, false) + Sum(r => r.Broken, true), Sum(r => r.Opened + r.Broken, true));
    }
}
