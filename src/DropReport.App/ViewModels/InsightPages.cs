using System.Globalization;
using Avalonia.Media.Imaging;
using DropReport.Analysis;
using DropReport.App.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DropReport.App.ViewModels;

/// <summary>A headline card: kicker title, the name, a big value and one line of context. Rarity colors the card's bar.</summary>
public sealed record PickCard(string Title, string Name, string Value, string Detail, string Rarity);

public sealed record MeterLine(string Name, string Value, double Fill, string Detail, string Rarity = null);

/// <summary>Strength / weakness / tip on the Season page.</summary>
public sealed record NoteLine(string Kind, string Headline, string Detail)
{
    public bool IsStrength => Kind == "strength";
    public bool IsWeakness => Kind == "weakness";
    public bool IsTip => Kind == "tip";
    public string Label => Kind switch { "strength" => "STRENGTH", "weakness" => "WEAKNESS", _ => "TIP" };
}

// ---------------------------------------------------------------- Treasure Hunt (season 43 token + chest rewards)
public sealed partial class TreasureViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private bool _countRefills;
    [ObservableProperty] private bool _countBroken;
    [ObservableProperty] private bool _countTeam;

    [ObservableProperty] private string _dateline = "";
    [ObservableProperty] private string _hero = "0";
    [ObservableProperty] private string _heroDek = "";
    [ObservableProperty] private string _projectedText = "";
    [ObservableProperty] private double _current;
    [ObservableProperty] private double _currentChance;
    [ObservableProperty] private double _projected;
    [ObservableProperty] private double _projectedChance;
    [ObservableProperty] private string _projectedLabel = "";
    [ObservableProperty] private List<Tile> _tiles = new();
    [ObservableProperty] private List<Tile> _milestones = new();
    [ObservableProperty] private string _boostTitle = "";
    [ObservableProperty] private string _boostDetail = "";
    [ObservableProperty] private bool _boostLive;
    [ObservableProperty] private List<MeterLine> _rewards = new();
    [ObservableProperty] private List<MeterLine> _chestTypes = new();
    [ObservableProperty] private List<CountLine> _seasonItems = new();
    [ObservableProperty] private List<CountLine> _loot = new();
    [ObservableProperty] private bool _empty;

    public double PerChest => 1 / Treasure.Rewards[0].OneIn;

    public TreasureViewModel(MainViewModel main)
    {
        Main = main;
        _countRefills = main.Settings.CountRefills; _countBroken = main.Settings.CountBroken; _countTeam = main.Settings.CountTeam;
        Refresh();
    }

    partial void OnCountRefillsChanged(bool value) { Main.Settings.CountRefills = value; Main.Settings.Save(); Refresh(); }
    partial void OnCountBrokenChanged(bool value) { Main.Settings.CountBroken = value; Main.Settings.Save(); Refresh(); }
    partial void OnCountTeamChanged(bool value) { Main.Settings.CountTeam = value; Main.Settings.Save(); Refresh(); }

    public void Refresh()
    {
        var ci = CultureInfo.CurrentCulture;
        var ms = Main.Library.Matches.Where(m => m.Season == Treasure.Season).ToList();
        Empty = ms.Count == 0;
        var now = DateTime.UtcNow;
        var r = Treasure.Build(ms, new CountRule(CountRefills, CountBroken, CountTeam), now);
        Dateline = $"TREASURE HUNT · SEASON {Treasure.Season} · ALL MODES · {r.Matches} MATCHES · {r.DaysLeft} DAYS LEFT";

        Hero = (r.TokenChance * 100).ToString("0.00", ci) + "%";
        HeroDek = $"Every chest is a 1 in {Treasure.Rewards[0].OneIn:N0} roll (Epic's published odds). " +
                  $"{r.Chests:N0} of your {(CountTeam ? "team's " : "")}chests have rolled so far" +
                  (r.BoostedChests > 0 ? $", {r.BoostedChests:N0} of them during Treasure Hours." : ".");
        ProjectedText = r.DaysLeft > 0
            ? $"{(r.ProjectedChance * 100).ToString("0.0", ci)}% by 5 Dec at your pace"
            : "The token season is over";
        Current = r.Chests; CurrentChance = r.TokenChance; Projected = r.ProjectedChests; ProjectedChance = r.ProjectedChance;
        ProjectedLabel = $"5 Dec at your pace, {r.ProjectedChests:N0} chests";

        Tiles = new()
        {
            new Tile("ROLLING CHESTS", r.Chests.ToString("N0", ci), CountTeam ? "you and your teammates" : "yours only"),
            new Tile("A MATCH", r.ChestsPerMatch.ToString("0.0", ci), "rolling chests"),
            new Tile("A DAY", r.ChestsPerDay.ToString("N0", ci), "since your first match"),
            new Tile("TREASURE HOURS", r.BoostedChests.ToString("N0", ci), "chests at boosted odds"),
            new Tile("REFILLED", r.Refills.ToString("N0", ci), CountRefills ? "counted" : "not counted"),
        };
        var p = PerChest;
        Milestones = r.Milestones.Select(m =>
        {
            var more = Math.Max(0, m.Chests - r.Chests);
            var days = r.ChestsPerDay > 0 ? more / r.ChestsPerDay : double.PositiveInfinity;
            var note = more <= 0 ? "reached" : days <= r.DaysLeft ? $"about {Math.Ceiling(days):0} days at your pace" : $"{more:N0} more: past the season at your pace";
            return new Tile($"{m.Chance:P0} CHANCE", m.Chests.ToString("N0", ci), note);
        }).ToList();

        var live = Treasure.SessionAt(now);
        var next = live ?? Treasure.NextSession(now);
        BoostLive = live != null;
        if (next == null) { BoostTitle = "No more Treasure Hours this season"; BoostDetail = ""; }
        else
        {
            var s = next.StartUtc.ToLocalTime(); var e = next.EndUtc.ToLocalTime();
            BoostTitle = live != null ? $"Treasure Hours: on now, until {e:h:mm tt}" : $"Next Treasure Hours: {s:ddd d MMM}, {s:h:mm}–{e:h:mm tt}";
            BoostDetail = $"Token odds {next.Multiplier:0.#}x ({(next.Confirmed ? "Epic's figure" : "as reported; Epic hasn't confirmed this date")}). " +
                          "Other rewards keep their normal odds. Matches started in a session count as boosted.";
        }

        Rewards = r.Others.Select(o => new MeterLine(o.Reward.Name, (o.Chance * 100).ToString(o.Chance >= 0.995 ? "0" : o.Chance < 0.1 ? "0.0" : "0", ci) + "%", o.Chance,
            $"1 in {o.Reward.OneIn:N0} per chest · " + (o.Reward.Weekly ? $"chance each week at {o.Rolls:N0} chests a week" : $"chance over your {o.Rolls:N0} chests"))).ToList();

        // what the chests were
        var s2 = SeasonStats.Compute(ms);
        var maxType = Math.Max(1, s2.ChestTypes.Max(c => (int?)(c.Me + c.Team)) ?? 1);
        ChestTypes = s2.ChestTypes.Select(c => new MeterLine(c.Type, (c.Me + c.Team).ToString("N0", ci), (double)(c.Me + c.Team) / maxType,
            $"you {c.Me:N0} · team {c.Team:N0}" + (Treasure.IsRewardChest(c.Type) ? "" : " · doesn't roll"))).ToList();
        SeasonItems = s2.SeasonItems.Select(c => new CountLine(c.Name, c.Rarity, c.Kind, c.Count.ToString(ci))).ToList();
        Loot = s2.Loot.Take(12).Select(c => new CountLine(c.Name, c.Rarity, c.Rarity, c.Count.ToString(ci))).ToList();
    }
}

// ---------------------------------------------------------------- Drop spots
public sealed record SpotLine(string Spot, string Drops, string Place, string Top10, string Elims, string Chests, string Early, double PlaceFill, double Top10Fill);

public sealed partial class SpotsViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private string _dateline = "";
    [ObservableProperty] private List<PickCard> _picks = new();
    [ObservableProperty] private List<SpotLine> _rows = new();
    [ObservableProperty] private List<SpotDot> _dots = new();
    [ObservableProperty] private Bitmap _mapImage;
    [ObservableProperty] private MapGeo _geo;
    [ObservableProperty] private string _note = "";

    public SpotsViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    public void Refresh()
    {
        var ci = CultureInfo.CurrentCulture;
        var ms = Main.Filter.Apply(Main.Library.Matches);
        Dateline = Datelines.For(Main, "DROP SPOTS", ms);
        var spots = Insights.Spots(ms);
        Picks = Insights.BestSpots(spots).Select(p => new PickCard(p.Title.ToUpperInvariant(), p.Name, p.Value, p.Detail, null)).ToList();
        double best = spots.Count > 0 ? spots.Min(s => s.AvgPlace) : 1, worst = spots.Count > 0 ? spots.Max(s => s.AvgPlace) : 1;
        double Score(double place) => worst - best < 0.01 ? 1 : (worst - place) / (worst - best);
        Rows = spots.Select(s => new SpotLine(s.Spot, s.Matches.ToString(ci), s.AvgPlace.ToString("0.0", ci), s.Top10Rate.ToString("P0", ci),
            s.ElimsPerMatch.ToString("0.0", ci), s.ChestsPerMatch.ToString("0.0", ci), s.EarlyDeaths == 0 ? "–" : s.EarlyDeaths.ToString(ci),
            Score(s.AvgPlace), s.Top10Rate)).ToList();

        var (img, geo) = MatchDetailViewModel.LoadOverlay();
        MapImage = img; Geo = geo;
        Dots = geo == null ? new() : spots.Select(s => (s, poi: geo.Pois.FirstOrDefault(p => p.Name == s.Spot))).Where(x => x.poi != null)
            .Select(x => new SpotDot(x.s.Spot, x.poi.X, x.poi.Y, x.s.Matches, Score(x.s.AvgPlace),
                $"{x.s.Spot}: {x.s.Matches} drops · average place {x.s.AvgPlace:0.0} · {x.s.ElimsPerMatch:0.0} elims · {x.s.ChestsPerMatch:0.0} chests")).ToList();
        var noSpot = ms.Count(m => m.Landing == null);
        Note = "Where you touched down: the named place within 300 m of the moment you slowed from gliding to running. " +
               "Picks need 3 or more drops, and a spot's averages are pulled toward your overall average until it has enough drops to stand on its own." +
               (noSpot > 0 ? $" {noSpot} {(noSpot == 1 ? "match" : "matches")} landed away from any named place or had no map data." : "");
    }
}
