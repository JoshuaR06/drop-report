using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BootyBoard.App.ViewModels;

public sealed record LootLine(string Name, string Rarity, string Taken);
public sealed record ChestLine(double T, string Time, string What, string Who, List<LootLine> Loot);
public sealed record FightLine(double T, string Time, string Text, string Detail, bool IsDeath);
public sealed record DamageLine(string Weapon, string Rarity, string Hits, string Hs, string Damage, string Detail);

public sealed partial class MatchDetailViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    public MatchSummary Summary { get; }
    public MapData Map { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Dek { get; }
    public double Duration { get; }
    public List<Tile> Tiles { get; }
    public List<ChestLine> Chests { get; }
    public List<FightLine> Fights { get; }
    public List<DamageLine> Damage { get; }
    public string DamageNote { get; }
    public bool HasMap => Map != null && Map.Tracks.Count > 0;
    /// <summary>The game's map image, only for matches from the season it belongs to.</summary>
    public Avalonia.Media.Imaging.Bitmap MapImage { get; }
    public MapGeo Geo { get; }
    public string MapNote { get; }

    private static (string path, DateTime stamp, Avalonia.Media.Imaging.Bitmap bmp, MapGeo geo) _overlay;
    internal static (Avalonia.Media.Imaging.Bitmap, MapGeo) LoadOverlay()
    {
        var png = Path.Combine(Services.AppData.GameData, "map.png");
        var info = Path.Combine(Services.AppData.GameData, "mapinfo.json");
        if (!File.Exists(png) || !File.Exists(info)) return (null, null);
        var stamp = File.GetLastWriteTimeUtc(png);
        if (_overlay.path != png || _overlay.stamp != stamp)
        {
            try { _overlay = (png, stamp, new Avalonia.Media.Imaging.Bitmap(png), MapGeo.Load(info)); } catch { return (null, null); }
        }
        return (_overlay.bmp, _overlay.geo);
    }

    [ObservableProperty] private double _time;
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private double _speed = 4;
    [ObservableProperty] private bool _follow = true;
    public string ClockText => $"{Fmt.Clock(Time)} / {Fmt.Clock(Duration)}";
    public string PlayText => Playing ? "Pause" : "Play";

    public MatchDetailViewModel(MainViewModel main, MatchSummary m)
    {
        _main = main; Summary = m; Map = main.Library.LoadMap(m);
        var (img, geo) = LoadOverlay();
        if (img != null && geo != null && geo.Season == m.Season) { MapImage = img; Geo = geo; }
        MapNote = MapImage != null ? ""
                : geo == null ? "No map image yet: update item names in Settings to add it"
                : $"No map image for game v{m.Season} (the saved map is from v{geo.Season})";
        bool hide = main.Settings.HideNames;
        static string Word(string who) => who == "npc" ? "NPC" : who;
        string Nm(string name, string who) => hide || string.IsNullOrEmpty(name) || name == "?" ? Word(who) : name;
        var entry = LogEntry.From(m);
        Title = entry.Headline;
        Subtitle = $"{entry.Kicker} · {m.Minutes:0} MIN · GAME V{m.Season}";
        Dek = entry.Dek;
        Duration = Map?.End ?? m.Minutes * 60;
        int team = m.Chests.Team.Sum(t => t.Opened + t.Broken);
        Tiles = new()
        {
            new Tile("PLACE", entry.Place, m.Placement == 1 ? "Victory Royale" : null),
            new Tile("ELIMINATIONS", Fmt.N(m.Epic.Elims), $"{m.Fights.Knocks} knocks"),
            new Tile("DAMAGE", Fmt.N(m.Epic.DamageToPlayers), "to players"),
            new Tile("ACCURACY", m.Epic.Accuracy != null ? Fmt.Pct(m.Epic.Accuracy * 100) : "–", $"{m.Damage.Headshots} headshots"),
            new Tile("CHESTS", (m.Chests.Opened + m.Chests.Broken + team).ToString(), $"you {m.Chests.Opened + m.Chests.Broken} · team {team}"),
        };
        var take = new Dictionary<string, string> { ["me"] = "", ["teammate"] = "teammate took", ["other"] = "enemy took", ["nobody"] = "left" };
        Chests = (Map?.Chests ?? new()).Where(c => c.Who == "me" || c.Who == "teammate").Select(c => new ChestLine(c.T, Fmt.Clock(c.T),
            $"{c.Chest} {c.Kind}", c.Who == "me" ? "" : "teammate", c.Loot.Select(l => new LootLine(l.Name, l.Rarity, take.GetValueOrDefault(l.TakenBy ?? "", ""))).ToList())).ToList();
        Fights = m.Fights.Mine.Concat(m.Fights.Deaths).OrderBy(f => f.T).Select(f => f.Victim == "me"
            ? new FightLine(f.T, Fmt.Clock(f.T), f.Kind == "knock" ? "You were knocked" : "You were eliminated", $"by {Nm(f.ByName, f.By)}{(hide ? "" : $" ({Word(f.By)})")} · {f.Cause}{(f.Dist != null ? $" · {f.Dist} m" : "")}", true)
            : new FightLine(f.T, Fmt.Clock(f.T), $"{(f.Kind == "knock" ? "Knocked" : f.GunType == 50 ? "Eliminated (team wiped)" : "Eliminated")} {Word(f.Victim)}",
                            $"{Nm(f.VictimName, f.Victim)} · {(f.Weapon ?? f.Cause)}{(f.WeaponRarity != null ? $" ({f.WeaponRarity})" : "")}{(f.Dist != null ? $" · {f.Dist} m" : "")}" +
                            (f.Outcome != null && f.Outcome != "finished by me" ? " · " + f.Outcome : ""), false)).ToList();
        Damage = m.Damage.PerWeapon.Select(w => new DamageLine(w.Weapon, w.Rarity, w.BlankHits > 0 ? w.BlankHits.ToString() : w.Hits.ToString(),
            w.BlankHits > 0 ? "–" : Fmt.Pct(w.HeadshotPct), w.BlankHits > 0 ? "–" : Fmt.N(w.Damage),
            w.BlankHits > 0 ? "hits, no damage recorded" : $"players {Fmt.N(w.ToPlayers)} · bots {Fmt.N(w.ToBots)} · max {w.BiggestHit:0}")).ToList();
        DamageNote = $"{Fmt.N(m.Damage.Recorded)} damage recorded over {m.Damage.Hits} hits; Epic's match total is {Fmt.N(m.Epic.DamageToPlayers)}. Slingshot and melee-item hits aren't fully in replays.";
        var first = Map?.Chests.FirstOrDefault(c => c.Who == "me");
        Time = first != null ? Math.Max(0, first.T - 20) : 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => { Time = Math.Min(Duration, Time + Speed * 0.033); if (Time >= Duration) Playing = false; };
    }

    partial void OnTimeChanged(double value) => OnPropertyChanged(nameof(ClockText));
    partial void OnPlayingChanged(bool value) { if (value) _timer.Start(); else _timer.Stop(); OnPropertyChanged(nameof(PlayText)); }

    [RelayCommand] private void PlayPause() { if (Time >= Duration) Time = 0; Playing = !Playing; }
    [RelayCommand] private void SetSpeed(string s) => Speed = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    [RelayCommand] private void Jump(double t) { Playing = false; Time = Math.Max(0, t - 3); }
    [RelayCommand] private void Back() { Playing = false; _main.Back(); }
}
