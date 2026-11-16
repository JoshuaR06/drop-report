namespace BootyBoard.Analysis;

/// <summary>A reward any chest can roll this season. OneIn = Epic's published per-chest odds (Fortnite Status, 7 Nov 2026).
/// Weekly rewards rotate each week; once you have one you can't get the same one again.</summary>
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

    public static readonly IReadOnlyList<ChestReward> Rewards = new ChestReward[]
    {
        new(Token, 137_500, Weekly: false, Boosted: true),
        new("Gold outfit", 1_562_500, Weekly: true, Boosted: false),
        new("Vehicle and decal", 7_813, Weekly: true, Boosted: false),
        new("Pickaxe or back bling", 568, Weekly: true, Boosted: false),
        new("Jam track", 391, Weekly: true, Boosted: false),
        new("Loading screen", 272, Weekly: true, Boosted: false),
        new("Any Gizmo", 127, Weekly: false, Boosted: false),
    };

    /// <summary>The token can be found until the season ends on 5 Dec 2026 (taken as the end of that day, US Eastern).</summary>
    public static readonly DateTime SeasonEndUtc = new(2026, 12, 6, 5, 0, 0, DateTimeKind.Utc);

    // Saturdays, 2-4 PM and 9-11 PM US Eastern (EST = UTC-5 in November). 7 Nov: 1.5x then 2x (Epic). Later dates: 1.5x as reported.
    public static readonly IReadOnlyList<BoostSession> Sessions = BuildSessions();

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
    public static BoostSession NextSession(DateTime nowUtc) => Sessions.FirstOrDefault(s => s.EndUtc > nowUtc);

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
        IReadOnlyList<(ChestReward Reward, double Chance, double Rolls)> Others, double ChestsPerWeek, List<MatchRolls> PerMatch,
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
        // weekly rewards: the chance per week at your pace (a fresh one each week); the rest: across the season so far
        var perWeek = perDay * 7;
        var others = Rewards.Skip(1).Select(r => (r, r.Weekly ? AtLeastOne(perWeek, 1.0 / r.OneIn) : AtLeastOne(chests, 1.0 / r.OneIn), r.Weekly ? perWeek : (double)chests)).ToList();

        int Sum(Func<ChestTypeCount, int> f, bool team) => ms.Sum(m => (team ? m.Chests.Team : m.Chests.ByType).Where(r => IsRewardChest(r.Type)).Sum(f));
        return new Report(chests, per.Where(x => x.Multiplier > 1).Sum(x => x.Chests), chance, rolls,
            per.Count, per.Count > 0 ? (double)chests / per.Count : 0, perDay, daysLeft, projected, projectedChance, milestones, others, perWeek, per,
            Sum(r => r.OpenedRefill + r.BrokenRefill, false) + Sum(r => r.OpenedRefill + r.BrokenRefill, true),
            Sum(r => r.Broken, false) + Sum(r => r.Broken, true), Sum(r => r.Opened + r.Broken, true));
    }
}
