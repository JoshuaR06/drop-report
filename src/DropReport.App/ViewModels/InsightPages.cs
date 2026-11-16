using System.Globalization;
using Avalonia.Media.Imaging;
using DropReport.Analysis;
using DropReport.App.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    [ObservableProperty] private List<CountLine> _reveals = new();
    [ObservableProperty] private string _revealsNote = "";

    /// <summary>What a treasure can be, straight from the game's prize table (v43.10).</summary>
    public List<CountLine> PrizeTable { get; } = new()
    {
        new("Pirate Shop Access", "Legendary", "Supreme · only from chests picked at the start of the match, one per match at most", "1 in 137,500"),
        new("Gold Outfit", "Legendary", "Supreme · a different one each week", "1 in 1,562,500"),
        new("Vehicle and Gold Car Decal", "Epic", "Major · weekly", "1 in 7,813"),
        new("Pickaxe or Back Bling", "Epic", "Major · weekly", "1 in 568"),
        new("Jam Track", "Epic", "Major · weekly", "1 in 391"),
        new("Loading Screen", "Epic", "Major · weekly", "1 in 272"),
        new("Sprite Gizmo", "Rare", "Standard · straight into your inventory: Extraction Accelerator, Relic Extractor, Sprite Locator, Lucky Locator", "1 in 127"),
    };

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

        // treasure reveals: chests that turned out to hold a treasure, in view of your replay
        var rv = ms.Where(m => m.Reveals != null).SelectMany(m => m.Reveals.Select(x => (m, x))).OrderByDescending(p => p.m.Start).ThenByDescending(p => p.x.T).ToList();
        Reveals = rv.Select(p => new CountLine($"{p.m.Start:ddd d MMM, h:mm tt} · {p.m.Mode}", p.x.Who == "me" ? "Legendary" : null, Treasure.Describe(p.x), p.x.Who == "me" ? "YOU" : p.x.Who == "teammate" ? "TEAM" : "")).ToList();
        RevealsNote = rv.Count == 0
            ? "None yet. When a chest holding a treasure is opened or broken in view of your replay, the game swaps in a treasure chest, and it shows up here."
            : $"{rv.Count} in {ms.Count} matches. A reveal is the game swapping a chest for its treasure chest; the replay usually doesn't carry which prize (that's settled on Epic's servers).";

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

// ---------------------------------------------------------------- Home: the latest match, the session, form, records
/// <summary>One square in the form guide: placement, colored by how it went.</summary>
public sealed record FormSquare(string Place, string Tip, bool IsWin, bool IsTop10, MatchSummary Match);

public sealed record RecordCard(string Title, string Value, string Detail, MatchSummary Match);

public sealed partial class HomeViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private string _dateline = "";
    [ObservableProperty] private bool _hasMatch;
    [ObservableProperty] private string _latestKicker = "";
    [ObservableProperty] private string _latestHeadline = "No matches yet";
    [ObservableProperty] private string _latestDek = "Play a match with replays on. Drop Report reads it the moment it ends.";
    [ObservableProperty] private List<VersusLine> _latestStats = new();
    [ObservableProperty] private List<string> _latestRecords = new();
    [ObservableProperty] private string _sessionTitle = "";
    [ObservableProperty] private List<Tile> _session = new();
    [ObservableProperty] private List<FormSquare> _form = new();
    [ObservableProperty] private List<VersusLine> _formStats = new();
    [ObservableProperty] private List<RecordCard> _records = new();
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _tokenDek = "";
    [ObservableProperty] private string _boost = "";
    [ObservableProperty] private List<NoteLine> _tips = new();
    private MatchSummary _latest;

    public HomeViewModel(MainViewModel main) { Main = main; Refresh(); }

    [RelayCommand] private void OpenLatest() { if (_latest != null) Main.OpenMatch(_latest); }
    [RelayCommand] private async Task ShareLatest() { if (_latest != null) await Main.ShareAsync(ShareCardViewModel.ForMatch(_latest, Main.Library.Matches, Main.Settings)); }
    [RelayCommand] private void Open(MatchSummary m) { if (m != null) Main.OpenMatch(m); }
    [RelayCommand] private void Go(string key) => Main.Navigate(key);

    public void Refresh()
    {
        var ci = CultureInfo.CurrentCulture;
        var all = Main.Library.Matches;   // newest first
        Dateline = $"HOME · {DateTime.Now.ToString("dddd d MMMM", ci).ToUpperInvariant()}";
        HasMatch = all.Count > 0;
        if (!HasMatch) return;
        var m = _latest = all[0];
        var e = LogEntry.From(m);
        LatestKicker = "LATEST MATCH · " + e.Kicker;
        LatestHeadline = e.Headline; LatestDek = e.Dek;
        LatestStats = Insights.VsUsual(m, all).Select(VersusLine.From).ToList();
        LatestRecords = Insights.Highlights(m, all);

        // the session: matches strung together with gaps under 90 minutes, ending with the latest
        var session = new List<MatchSummary> { m };
        for (var i = 1; i < all.Count && all[i].Start != null && session[^1].Start != null && (session[^1].Start - all[i].Start)?.TotalMinutes < 90 + session[^1].Minutes; i++)
            session.Add(all[i]);
        var day = m.Start?.Date == DateTime.Today ? "TODAY'S SESSION" : $"LAST SESSION · {m.Start?.ToString("ddd d MMM", ci).ToUpperInvariant()}";
        SessionTitle = day;
        var placed = session.Where(x => x.Placement is > 0).ToList();
        Session = new()
        {
            new Tile("MATCHES", session.Count.ToString(ci), $"{session.Sum(x => x.Minutes):0} minutes"),
            new Tile("WINS", session.Count(x => x.Placement == 1).ToString(ci), placed.Count > 0 ? $"best {Headlines.Ordinal(placed.Min(x => x.Placement.Value))}" : null),
            new Tile("ELIMINATIONS", session.Sum(x => x.Epic.Elims ?? 0).ToString(ci), $"{session.Sum(x => x.Fights.Knocks)} knocks"),
            new Tile("DAMAGE", session.Sum(x => x.Epic.DamageToPlayers ?? 0).ToString("N0", ci), "to players"),
            new Tile("CHESTS", session.Sum(Insights.TeamChests).ToString("N0", ci), "you and your team"),
        };

        // form guide: the last 10, oldest to newest, plus last-10 against the rest of this season
        var last10 = all.Take(10).ToList();
        Form = last10.AsEnumerable().Reverse().Select(x => new FormSquare(x.Placement is int p ? p.ToString(ci) : "–",
            $"{LogEntry.From(x).Headline} · {x.Mode} · {x.Start?.ToString("ddd h:mm tt", ci)}", x.Placement == 1, x.Placement is <= 10, x)).ToList();
        var rest = all.Skip(10).Where(x => x.Season == m.Season).ToList();
        VersusLine Avg(string label, Func<MatchSummary, double?> f, string fmt, bool lowerBetter = false)
        {
            var a = last10.Select(f).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty().Average();
            var value = a.ToString(fmt, ci);
            var r = rest.Select(f).Where(v => v != null).Select(v => v!.Value).ToList();
            if (r.Count < 5) return new VersusLine(label, value, "", false, false, true);
            var d = a - r.Average(); var same = Math.Abs(d) <= Math.Max(0.1, Math.Abs(r.Average()) * 0.06);
            var better = lowerBetter ? d < 0 : d > 0;
            var delta = same ? "steady" : $"{(d > 0 ? "▲" : "▼")} {Math.Abs(d).ToString(fmt, ci)} vs the rest of the season";
            return new VersusLine(label, value, delta, !same && better, !same && !better, same);
        }
        FormStats = new()
        {
            Avg("AVG PLACE", x => x.Placement, "0.0", lowerBetter: true),
            Avg("ELIMS A MATCH", x => x.Epic.Elims, "0.0"),
            Avg("DAMAGE A MATCH", x => x.Epic.DamageToPlayers, "N0"),
            Avg("CHESTS A MATCH", x => Insights.TeamChests(x), "0.0"),
        };

        // personal records, all time
        RecordCard Rec(string title, Func<MatchSummary, double> f, Func<MatchSummary, string> value)
        {
            var best = all.OrderByDescending(f).ThenBy(x => x.Start).First();
            return new RecordCard(title, value(best), $"{best.Start?.ToString("d MMM", ci)} · {best.Mode} · {LogEntry.From(best).Headline}", best);
        }
        Records = new()
        {
            Rec("MOST ELIMINATIONS", x => x.Epic.Elims ?? 0, x => (x.Epic.Elims ?? 0).ToString(ci)),
            Rec("MOST DAMAGE", x => x.Epic.DamageToPlayers ?? 0, x => (x.Epic.DamageToPlayers ?? 0).ToString("N0", ci)),
            Rec("MOST CHESTS", x => Insights.TeamChests(x), x => Insights.TeamChests(x).ToString(ci)),
            Rec("MOST REAL PLAYERS", x => x.Fights.ElimsPlayers, x => x.Fights.ElimsPlayers.ToString(ci)),
        };

        // the pirate token at a glance (treasure season only)
        var s = Main.Settings;
        var season = all.Where(x => x.Season == Treasure.Season).ToList();
        var t = Treasure.Build(season, new CountRule(s.CountRefills, s.CountBroken, s.CountTeam), DateTime.UtcNow);
        Token = (t.TokenChance * 100).ToString("0.00", ci) + "%";
        TokenDek = t.DaysLeft > 0 ? $"{(t.ProjectedChance * 100).ToString("0.0", ci)}% by 5 Dec at your pace · {t.Chests:N0} chests so far" : "The token season is over";
        var live = Treasure.SessionAt(DateTime.UtcNow); var next = live ?? Treasure.NextSession(DateTime.UtcNow);
        Boost = next == null ? "" : live != null ? $"Treasure Hours on now: {next.Multiplier:0.#}x token odds until {next.EndUtc.ToLocalTime():h:mm tt}"
            : $"Next Treasure Hours: {next.StartUtc.ToLocalTime():ddd d MMM, h:mm tt} ({next.Multiplier:0.#}x)";

        Tips = Insights.Notes(all.Where(x => x.Season == m.Season).ToList()).OrderBy(n => n.Kind == "weakness" ? 0 : n.Kind == "tip" ? 1 : 2).Take(3)
            .Select(n => new NoteLine(n.Kind, n.Headline, n.Detail)).ToList();
    }
}