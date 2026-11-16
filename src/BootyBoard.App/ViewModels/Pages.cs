using System.Diagnostics;
using BootyBoard.Analysis;
using BootyBoard.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BootyBoard.App.ViewModels;

// ---------------------------------------------------------------- Season (home)
public sealed partial class SeasonViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private string _hero = "0";
    [ObservableProperty] private string _heroLabel = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private List<Tile> _treasure = new();
    [ObservableProperty] private List<Tile> _match = new();
    [ObservableProperty] private List<ColumnItem> _chestChart = new();
    [ObservableProperty] private List<ColumnItem> _elimChart = new();
    [ObservableProperty] private bool _empty;

    public SeasonViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    public void Refresh()
    {
        var ms = Main.Filter.Apply(Main.Library.Matches);
        Empty = ms.Count == 0;
        var s = SeasonStats.Compute(ms);
        Subtitle = ms.Count == 0 ? "No matches for this filter yet" : $"{ms.Count} matches · {Fmt.When(ms[0].Start)} to {Fmt.When(ms[^1].Start)}";
        Hero = Fmt.N(s.ChestsMe + s.ChestsTeam);
        HeroLabel = $"opened by your team · you {Fmt.N(s.ChestsMe)}, teammates {Fmt.N(s.ChestsTeam)} · {Fmt.N(s.BrokenMe + s.BrokenTeam)} more broken · {Fmt.N1(ms.Count > 0 ? (double)(s.ChestsMe + s.ChestsTeam) / ms.Count : 0)} per match";
        Tile Ct(string label, string type) { var a = s.Chest(type, false); var b = s.Chest(type, true); return new Tile(label, Fmt.N(a + b), $"you {a} · team {b}"); }
        Treasure = new()
        {
            Ct("Rare Chests", "Rare Chest"), Ct("Sprite Chests", "Sprite Chest"), Ct("Buried chests", "Buried chest"), Ct("Extraction crates", "Extraction crate"),
            new Tile("Vault keys found", Fmt.N(s.SeasonItemCount("vault key"))),
            new Tile("Treasure keys & maps", Fmt.N(s.SeasonItemCount("treasure key", "treasure map"))),
            new Tile("Sprite & extraction items", Fmt.N(s.SeasonItemCount("sprite locator", "extraction"))),
        };
        Match = new()
        {
            new Tile("Matches", Fmt.N(s.Matches), $"{Fmt.N(s.Minutes)} min played"),
            new Tile("Wins", Fmt.N(s.Wins), s.AvgPlacement != null ? $"avg place {Fmt.N1(s.AvgPlacement)}" : null),
            new Tile("Eliminations", Fmt.N(s.Elims), $"{Fmt.N1(s.Matches > 0 ? (double)s.Elims / s.Matches : 0)} per match (Epic's count)"),
            new Tile("Knocks", Fmt.N(s.Knocks), $"you finished {s.KnocksIFinished}"),
            new Tile("Players vs bots", $"{s.ElimsPlayers} / {s.ElimsBots}", "elims on players / bots"),
            new Tile("Damage to players", Fmt.N(s.DamagePerMatch), "per match (Epic)"),
            new Tile("Headshot rate", Fmt.Pct(s.Headshots, s.Hits), $"{s.Headshots} of {s.Hits} recorded hits"),
            new Tile("Accuracy", s.Accuracy != null ? Fmt.Pct(s.Accuracy * 100) : "–", "Epic, match average"),
        };
        ChestChart = ms.Select(m => new ColumnItem(Fmt.Day(m.Start), new double[] { m.Chests.Opened + m.Chests.Broken, m.Chests.Team.Sum(t => t.Opened + t.Broken) },
            $"{Fmt.When(m.Start)} · {m.Mode}{(m.Placement != null ? " · #" + m.Placement : "")}\nyou {m.Chests.Opened + m.Chests.Broken} · teammates {m.Chests.Team.Sum(t => t.Opened + t.Broken)}")).ToList();
        ElimChart = ms.Select(m => new ColumnItem(Fmt.Day(m.Start), new double[] { m.Fights.ElimsPlayers, m.Fights.ElimsBots },
            $"{Fmt.When(m.Start)} · {m.Mode}{(m.Placement != null ? " · #" + m.Placement : "")}\n{m.Fights.ElimsPlayers} players · {m.Fights.ElimsBots} bots · {m.Fights.Knocks} knocks")).ToList();
    }
}

// ---------------------------------------------------------------- Matches
public sealed record MatchRow(MatchSummary Summary, string When, string Mode, string Place, string Elims, string Knocks, string Damage,
                              string Chests, string RareSprite, string DownTo);

public sealed partial class MatchesViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private List<MatchRow> _rows = new();
    [ObservableProperty] private MatchRow _selected;

    public MatchesViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    public void Refresh()
    {
        Rows = Main.Filter.Apply(Main.Library.Matches).AsEnumerable().Reverse().Select(m =>
        {
            int T(List<ChestTypeCount> l, string type) => l.Where(c => c.Type == type).Sum(c => c.Opened + c.Broken);
            var down = m.Death == null ? (m.Placement == 1 ? "won" : "–") : m.Death.By == "me" || m.Death.Cause == "bled out" ? "bled out" : $"{m.Death.Cause} ({m.Death.By})";
            return new MatchRow(m, Fmt.When(m.Start), m.Mode + (m.Special != null ? " · " + m.Special : ""), m.Placement != null ? "#" + m.Placement : "–",
                Fmt.N(m.Epic.Elims), m.Fights.Knocks.ToString(), Fmt.N(m.Epic.DamageToPlayers),
                $"{m.Chests.Opened + m.Chests.Broken} / {m.Chests.Team.Sum(t => t.Opened + t.Broken)}",
                $"{T(m.Chests.ByType, "Rare Chest") + T(m.Chests.Team, "Rare Chest")} / {T(m.Chests.ByType, "Sprite Chest") + T(m.Chests.Team, "Sprite Chest")}", down);
        }).ToList();
    }

    partial void OnSelectedChanged(MatchRow value) { if (value != null) Main.OpenMatch(value.Summary); }
}

// ---------------------------------------------------------------- Weapons
public sealed record WeaponLine(string Name, string Rarity, string Elims, string Knocks, string Damage, string Hits, string Hs, string Matches);

public sealed partial class WeaponsViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private List<WeaponLine> _rows = new();
    [ObservableProperty] private List<CountLine> _deaths = new();
    [ObservableProperty] private string _deathSummary = "";
    public WeaponsViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }
    public void Refresh()
    {
        var s = SeasonStats.Compute(Main.Filter.Apply(Main.Library.Matches));
        Rows = s.Weapons.Select(w => new WeaponLine(w.Name, w.BestRarity, w.Elims.ToString(), w.Knocks.ToString(),
            w.Hits > 0 ? Fmt.N(w.Damage) : "–", w.Hits > 0 ? w.Hits.ToString() : "–", Fmt.Pct(w.HeadshotPct), w.Matches.ToString())).ToList();
        Deaths = s.DeathCauses.Select(d => new CountLine(d.Cause, null, $"by {d.By}" + (d.MedianDist != null ? $" · median {d.MedianDist} m" : ""), d.Times.ToString())).ToList();
        DeathSummary = s.Deaths == 0 ? "No deaths recorded" : $"{s.Deaths} deaths in {s.Matches} matches · knocked first {s.KnockedFirst} · bled out {s.BledOut}";
    }
}

// ---------------------------------------------------------------- Loot & treasure
public sealed record CountLine(string Name, string Rarity, string Note, string Count);

public sealed partial class LootViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private List<CountLine> _chestTypes = new();
    [ObservableProperty] private List<CountLine> _seasonItems = new();
    [ObservableProperty] private List<CountLine> _loot = new();


    public LootViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    public void Refresh()
    {
        var s = SeasonStats.Compute(Main.Filter.Apply(Main.Library.Matches));
        ChestTypes = s.ChestTypes.Select(c => new CountLine(c.Type, null, $"you {c.Me} · team {c.Team}", (c.Me + c.Team).ToString())).ToList();
        SeasonItems = s.SeasonItems.Select(c => new CountLine(c.Name, c.Rarity, c.Kind, c.Count.ToString())).ToList();
        Loot = s.Loot.Take(25).Select(c => new CountLine(c.Name, c.Rarity, c.Rarity, c.Count.ToString())).ToList();
    }
}

// ---------------------------------------------------------------- About
public sealed partial class AboutViewModel : ViewModelBase
{
    public string Version => "Version " + AppInfo.Version;
    public string Disclaimer => AppInfo.EpicDisclaimer;
    public bool HasDonation => !string.IsNullOrEmpty(AppInfo.DonationUrl);
    public bool HasRepo => !string.IsNullOrEmpty(AppInfo.RepoUrl);

    [RelayCommand] private void Donate() => Open(AppInfo.DonationUrl);
    [RelayCommand] private void Repo() => Open(AppInfo.RepoUrl);
    [RelayCommand]
    private void Licenses()
    {
        // embedded so it works from the single-file exe; written next to the user's data and opened
        var asm = typeof(AboutViewModel).Assembly;
        var text = "";
        foreach (var name in new[] { "THIRD-PARTY-NOTICES.md", "LICENSE" })
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s != null) text += new StreamReader(s).ReadToEnd() + "\n\n";
        }
        var f = Path.Combine(AppData.Root, "LICENSES.txt");
        try { File.WriteAllText(f, text); Open(f); } catch (Exception e) { AppData.Log("licenses: " + e.Message); }
    }

    public static void Open(string target)
    {
        if (string.IsNullOrEmpty(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception e) { AppData.Log("open failed: " + e.Message); }
    }
}
