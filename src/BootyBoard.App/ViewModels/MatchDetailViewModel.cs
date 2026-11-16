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
    public double Duration { get; }
    public List<Tile> Tiles { get; }
    public List<ChestLine> Chests { get; }
    public List<FightLine> Fights { get; }
    public List<DamageLine> Damage { get; }
    public string DamageNote { get; }
    public bool HasMap => Map != null && Map.Tracks.Count > 0;

    [ObservableProperty] private double _time;
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private double _speed = 4;
    [ObservableProperty] private bool _follow = true;
    public string ClockText => $"{Fmt.Clock(Time)} / {Fmt.Clock(Duration)}";
    public string PlayText => Playing ? "Pause" : "Play";

    public MatchDetailViewModel(MainViewModel main, MatchSummary m)
    {
        _main = main; Summary = m; Map = main.Library.LoadMap(m);
        bool hide = main.Settings.HideNames;
        string Nm(string name, string who) => hide || string.IsNullOrEmpty(name) || name == "?" ? who : name;
        Title = $"{m.Mode}{(m.Special != null ? " · " + m.Special : "")} · {(m.Placement != null ? "#" + m.Placement : "no placement")}";
        Subtitle = $"{Fmt.When(m.Start)} · {m.Minutes:0} min · game v{m.Season}";
        Duration = Map?.End ?? m.Minutes * 60;
        int team = m.Chests.Team.Sum(t => t.Opened + t.Broken);
        Tiles = new()
        {
            new Tile("Chests", (m.Chests.Opened + m.Chests.Broken + team).ToString(), $"you {m.Chests.Opened + m.Chests.Broken} · team {team}"),
            new Tile("Elims", Fmt.N(m.Epic.Elims), $"{m.Fights.Knocks} knocks"),
            new Tile("Damage", Fmt.N(m.Epic.DamageToPlayers), "to players (Epic)"),
            new Tile("Accuracy", m.Epic.Accuracy != null ? Fmt.Pct(m.Epic.Accuracy * 100) : "–", $"{m.Damage.Headshots} headshots"),
        };
        var take = new Dictionary<string, string> { ["me"] = "", ["teammate"] = "teammate took", ["other"] = "enemy took", ["nobody"] = "left" };
        Chests = (Map?.Chests ?? new()).Where(c => c.Who == "me" || c.Who == "teammate").Select(c => new ChestLine(c.T, Fmt.Clock(c.T),
            $"{c.Chest} {c.Kind}", c.Who == "me" ? "" : "teammate", c.Loot.Select(l => new LootLine(l.Name, l.Rarity, take.GetValueOrDefault(l.TakenBy ?? "", ""))).ToList())).ToList();
        Fights = m.Fights.Mine.Concat(m.Fights.Deaths).OrderBy(f => f.T).Select(f => f.Victim == "me"
            ? new FightLine(f.T, Fmt.Clock(f.T), f.Kind == "knock" ? "You were knocked" : "You were eliminated", $"by {Nm(f.ByName, f.By)} · {f.Cause}{(f.Dist != null ? $" · {f.Dist} m" : "")}", true)
            : new FightLine(f.T, Fmt.Clock(f.T), $"{(f.Kind == "knock" ? "Knocked" : f.GunType == 50 ? "Eliminated (team wiped)" : "Eliminated")} {f.Victim}",
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
