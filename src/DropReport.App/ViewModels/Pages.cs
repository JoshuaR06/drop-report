using System.Diagnostics;
using DropReport.Analysis;
using DropReport.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DropReport.App.ViewModels;

// ---------------------------------------------------------------- shared: a match as a log entry
/// <param name="Kicker">e.g. "SAT 15 NOV · 2:39 PM · DUO · 2ND"</param>
public sealed record LogEntry(MatchSummary Summary, string Kicker, string Headline, string Dek, string Day, string Month, string Time,
                              string Place, string Elims, string Chests, string Damage)
{
    public static LogEntry From(MatchSummary m)
    {
        var (head, dek) = Headlines.For(m);
        var d = m.Start;
        var place = m.Placement is int p ? Headlines.Ordinal(p) : "–";
        var kicker = string.Join(" · ", new[] { d?.ToString("ddd d MMM").ToUpperInvariant(), d?.ToString("h:mm tt"), m.Mode.ToUpperInvariant(),
                                               m.Special?.ToUpperInvariant(), m.Placement != null ? place.ToUpperInvariant() : null }.Where(s => !string.IsNullOrEmpty(s)));
        var chests = m.Chests.Opened + m.Chests.Broken + m.Chests.Team.Sum(t => t.Opened + t.Broken);
        return new LogEntry(m, kicker, head, dek, d?.ToString("dd") ?? "", d?.ToString("MMM").ToUpperInvariant() ?? "", d?.ToString("h:mm tt") ?? "",
                            place, Fmt.N(m.Epic.Elims), chests.ToString(), Fmt.N(m.Epic.DamageToPlayers));
    }
}

// ---------------------------------------------------------------- The Ledger (season)
public sealed partial class SeasonViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private string _dateline = "";
    [ObservableProperty] private string _hero = "0";
    [ObservableProperty] private string _heroHeadline = "";
    [ObservableProperty] private string _heroDek = "";
    [ObservableProperty] private List<Tile> _treasure = new();
    [ObservableProperty] private List<Tile> _match = new();
    [ObservableProperty] private List<ColumnItem> _chestChart = new();
    [ObservableProperty] private List<ColumnItem> _elimChart = new();
    [ObservableProperty] private List<LogEntry> _dispatches = new();
    [ObservableProperty] private bool _empty;
    [ObservableProperty] private List<NoteLine> _notes = new();

    public SeasonViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    [RelayCommand] private void Open(LogEntry e) { if (e != null) Main.OpenMatch(e.Summary); }
    [RelayCommand] private void AllMatches() => Main.Navigate("matches");

    public void Refresh()
    {
        var ms = Main.Filter.Apply(Main.Library.Matches);
        Empty = ms.Count == 0;
        var s = SeasonStats.Compute(ms);
        Dateline = Datelines.For(Main, "SEASON REPORT", ms) + (ms.Count == 0 ? "" : $", {Fmt.Day(ms[0].Start).ToUpperInvariant()} TO {Fmt.Day(ms[^1].Start).ToUpperInvariant()}");
        Hero = Fmt.N(s.Elims);
        HeroHeadline = ms.Count == 0 ? "No matches yet" : s.Elims == 1 ? "elimination this season" : "eliminations this season";
        HeroDek = ms.Count == 0 ? "Play a match with replays on, then scan for new replays." :
            $"{Fmt.N1((double)s.Elims / ms.Count)} a match, {Fmt.N(s.Knocks)} knocks, {Fmt.N(s.Wins)} {(s.Wins == 1 ? "win" : "wins")}. " +
            $"Your team opened {Fmt.N(s.ChestsMe + s.ChestsTeam)} chests ({Fmt.N(s.ChestsMe)} of them yours).";
        Tile Ct(string label, string type) { var a = s.Chest(type, false); var b = s.Chest(type, true); return new Tile(label, Fmt.N(a + b), $"you {a} · team {b}"); }
        Treasure = new()
        {
            Ct("RARE CHESTS", "Rare Chest"), Ct("SPRITE CHESTS", "Sprite Chest"), Ct("BURIED CHESTS", "Buried chest"), Ct("EXTRACTION CRATES", "Extraction crate"),
            new Tile("VAULT KEYS", Fmt.N(s.SeasonItemCount("vault key")), "picked up"),
            new Tile("MAPS & KEYS", Fmt.N(s.SeasonItemCount("treasure key", "treasure map")), "treasure, picked up"),
            new Tile("SPRITE & EXTRACTION", Fmt.N(s.SeasonItemCount("sprite locator", "extraction")), "items picked up"),
        };
        Match = new()
        {
            new Tile("MATCHES", Fmt.N(s.Matches), $"{Fmt.N(s.Minutes)} minutes played"),
            new Tile("WINS", Fmt.N(s.Wins), s.AvgPlacement != null ? $"average place {Fmt.N1(s.AvgPlacement)}" : null),
            new Tile("ELIMINATIONS", Fmt.N(s.Elims), $"{Fmt.N1(s.Matches > 0 ? (double)s.Elims / s.Matches : 0)} a match"),
            new Tile("KNOCKS", Fmt.N(s.Knocks), $"you finished {s.KnocksIFinished}"),
            new Tile("PLAYERS · AI", $"{s.ElimsPlayers} · {s.ElimsBots}", "eliminated"),
            new Tile("DAMAGE", Fmt.N(s.DamagePerMatch), "to players, per match"),
            new Tile("HEADSHOTS", Fmt.Pct(s.Headshots, s.Hits), $"of {Fmt.N(s.Hits)} recorded hits"),
            new Tile("ACCURACY", s.Accuracy != null ? Fmt.Pct(s.Accuracy * 100) : "–", "match average"),
        };
        ChestChart = ms.Select(m => new ColumnItem(Fmt.Day(m.Start), new double[] { m.Chests.Opened + m.Chests.Broken, m.Chests.Team.Sum(t => t.Opened + t.Broken) },
            $"{Fmt.When(m.Start)} · {m.Mode}{(m.Placement != null ? " · " + Headlines.Ordinal(m.Placement.Value) : "")}\nyou {m.Chests.Opened + m.Chests.Broken} · team {m.Chests.Team.Sum(t => t.Opened + t.Broken)}")).ToList();
        ElimChart = ms.Select(m => new ColumnItem(Fmt.Day(m.Start), new double[] { m.Fights.ElimsPlayers, m.Fights.ElimsBots },
            $"{Fmt.When(m.Start)} · {m.Mode}{(m.Placement != null ? " · " + Headlines.Ordinal(m.Placement.Value) : "")}\n{m.Fights.ElimsPlayers} players · {m.Fights.ElimsBots} AI · {m.Fights.Knocks} knocks")).ToList();
        Dispatches = ms.AsEnumerable().Reverse().Take(4).Select(LogEntry.From).ToList();
        Notes = Insights.Notes(ms).Select(n => new NoteLine(n.Kind, n.Headline, n.Detail)).ToList();
    }
}

// ---------------------------------------------------------------- The Logbook (matches)
public sealed partial class MatchesViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private List<LogEntry> _rows = new();
    [ObservableProperty] private LogEntry _selected;
    [ObservableProperty] private string _dateline = "";

    public MatchesViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }

    public void Refresh()
    {
        var ms = Main.Filter.Apply(Main.Library.Matches);
        Rows = ms.AsEnumerable().Reverse().Select(LogEntry.From).ToList();
        Dateline = $"MATCH LOG · {ms.Count} {(ms.Count == 1 ? "MATCH" : "MATCHES")}";
    }

    partial void OnSelectedChanged(LogEntry value) { if (value != null) { Main.OpenMatch(value.Summary); Selected = null; } }
}

// ---------------------------------------------------------------- shared: the kicker line over each page
internal static class Datelines
{
    public static string For(MainViewModel main, string page, IReadOnlyList<MatchSummary> ms)
    {
        var season = main.Filter.Season?.Value;
        return string.Join(" · ", new[] { page, season != null ? $"SEASON {season}" : "ALL SEASONS", main.Filter.Mode?.Value?.ToUpperInvariant(),
            ms.Count == 0 ? null : $"{ms.Count} {(ms.Count == 1 ? "MATCH" : "MATCHES")}" }.Where(x => x != null));
    }
}

// ---------------------------------------------------------------- Weapons
public sealed record WeaponLine(string Name, string Rarity, string Elims, string Knocks, string DamagePerMatch, string Hits, string Hs, string Range, string Matches);

public sealed partial class WeaponsViewModel : ViewModelBase, IRefreshable
{
    public MainViewModel Main { get; }
    [ObservableProperty] private List<WeaponLine> _rows = new();
    [ObservableProperty] private List<CountLine> _deaths = new();
    [ObservableProperty] private string _deathSummary = "";
    [ObservableProperty] private string _dateline = "";
    [ObservableProperty] private List<PickCard> _picks = new();
    [ObservableProperty] private List<MeterLine> _classes = new();
    [ObservableProperty] private bool _showAll;
    [ObservableProperty] private string _tableNote = "";
    public WeaponsViewModel(MainViewModel main) { Main = main; main.Filter.Changed += Refresh; Refresh(); }
    partial void OnShowAllChanged(bool value) => Refresh();

    /// <summary>Used enough to judge: 3+ knocks/elims, or 30+ recorded hits.</summary>
    static bool Enough(Insights.WeaponScore w) => w.Fights >= 3 || w.Hits >= 30;

    public void Refresh()
    {
        var ms = Main.Filter.Apply(Main.Library.Matches);
        var s = SeasonStats.Compute(ms);
        Dateline = Datelines.For(Main, "WEAPONS", ms);
        var ws = Insights.Weapons(ms);
        Picks = Insights.BestWeapons(ws).Select(p => new PickCard(p.Title.ToUpperInvariant(), p.Name, p.Value, p.Detail, p.Rarity)).ToList();
        var classes = Insights.Classes(ms);
        var maxC = Math.Max(1, classes.Max(c => (int?)(c.Elims + c.Knocks)) ?? 1);
        Classes = classes.Select(c => new MeterLine(c.Class switch { "SMG" => "SMGs", "melee" => "Melee", _ => char.ToUpperInvariant(c.Class[0]) + c.Class[1..] + "s" },
            $"{c.Share:P0}", (double)(c.Elims + c.Knocks) / maxC,
            $"{c.Elims} elims · {c.Knocks} knocks" + (c.MedianDist != null ? $" · typically {c.MedianDist:0} m" : ""))).ToList();
        var shown = ShowAll ? ws : ws.Where(Enough).ToList();
        Rows = shown.OrderByDescending(w => w.Elims + w.Knocks).ThenByDescending(w => w.Damage)
            .Select(w => new WeaponLine(w.Name, w.Rarity, w.Elims.ToString(), w.Knocks.ToString(),
                w.Hits > 0 ? Fmt.N(w.DamagePerMatch) : "–", w.Hits > 0 ? w.Hits.ToString() : "–", Fmt.Pct(w.HeadshotPct),
                w.MedianDist != null ? $"{w.MedianDist:0} m" : "–", w.Matches.ToString())).ToList();
        var hidden = ws.Count - ws.Count(Enough);
        TableNote = ShowAll || hidden == 0 ? "" : $"{hidden} weapons you barely used are hidden.";
        Deaths = s.DeathCauses.Take(8).Select(d => new CountLine(char.ToUpperInvariant(d.Cause[0]) + d.Cause[1..], null,
            $"by {d.By}" + (d.MedianDist != null ? $" · typically {d.MedianDist:0} m" : ""), d.Times.ToString())).ToList();
        DeathSummary = s.Deaths == 0 ? "No deaths recorded" : $"{s.Deaths} deaths in {s.Matches} matches. Knocked first {s.KnockedFirst} times, bled out {s.BledOut}.";
    }
}

public sealed record CountLine(string Name, string Rarity, string Note, string Count);
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