using System.Globalization;
using DropReport.Analysis;

namespace DropReport.App.ViewModels;

/// <summary>What goes on a share card. Your numbers only: other players are never named, and Epic's disclaimer is on every card.</summary>
public sealed record ShareCardViewModel(string Kicker, string Headline, List<Tile> Stats, List<string> Chips, string Footer, string FileStem)
{
    public bool HasChips => Chips.Count > 0;
    public bool HasFooter => !string.IsNullOrEmpty(Footer);
    public string Disclaimer => "Not official. Not endorsed by Epic Games.";
    public string Brand => "DROP REPORT";

    public static ShareCardViewModel ForMatch(MatchSummary m, IReadOnlyList<MatchSummary> all, Services.Settings s)
    {
        var ci = CultureInfo.CurrentCulture;
        var e = LogEntry.From(m);
        var stats = new List<Tile>
        {
            new("PLACE", e.Place, m.Placement == 1 ? "Victory Royale" : null),
            new("ELIMINATIONS", (m.Epic.Elims ?? 0).ToString(ci), $"{m.Fights.Knocks} knocks"),
            new("DAMAGE", (m.Epic.DamageToPlayers ?? 0).ToString("N0", ci), "to players"),
            new("CHESTS", Insights.TeamChests(m).ToString(ci), "with my team"),
        };
        return new ShareCardViewModel(e.Kicker, e.Headline, stats, Insights.Highlights(m, all), TokenLine(all, s), $"match-{m.Start:yyyy-MM-dd-HHmm}");
    }

    public static ShareCardViewModel ForSeason(IReadOnlyList<MatchSummary> ms, IReadOnlyList<MatchSummary> all, Services.Settings s, string seasonLabel)
    {
        var ci = CultureInfo.CurrentCulture;
        var st = SeasonStats.Compute(ms);
        var ws = Insights.Weapons(ms);
        var top = ws.OrderByDescending(w => w.Elims).FirstOrDefault();
        var spot = Insights.BestSpots(Insights.Spots(ms)).FirstOrDefault(p => p.Title == "Best for placement");
        var stats = new List<Tile>
        {
            new("ELIMINATIONS", st.Elims.ToString("N0", ci), $"{(ms.Count > 0 ? (double)st.Elims / ms.Count : 0):0.0} a match"),
            new("WINS", st.Wins.ToString(ci), st.AvgPlacement != null ? $"average place {st.AvgPlacement:0.0}" : null),
            new("MATCHES", st.Matches.ToString(ci), $"{st.Minutes:N0} minutes"),
            new("CHESTS", (st.ChestsMe + st.ChestsTeam).ToString("N0", ci), "opened with my team"),
        };
        var chips = new List<string>();
        if (top != null) chips.Add($"Top weapon: {top.Name}");
        if (spot != null) chips.Add($"Best drop: {spot.Name}");
        return new ShareCardViewModel($"{seasonLabel.ToUpperInvariant()} · {ms.Count} MATCHES", "My season so far", stats, chips, TokenLine(all, s), $"season-{DateTime.Now:yyyy-MM-dd}");
    }

    private static string TokenLine(IReadOnlyList<MatchSummary> all, Services.Settings s)
    {
        var season = all.Where(x => x.Season == Treasure.Season).ToList();
        if (season.Count == 0) return null;
        var t = Treasure.Build(season, new CountRule(s.CountRefills, s.CountBroken, s.CountTeam, s.CountUnseen), DateTime.UtcNow);
        return $"PIRATE TOKEN CHANCE  {(t.TokenChance * 100).ToString("0.00", CultureInfo.CurrentCulture)}%  ·  {t.Chests:N0} CHESTS ROLLED";
    }
}
