using System.Globalization;
using DropReport.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DropReport.App.ViewModels;

/// <summary>One figure on the card, with the flags the view colors by.</summary>
public sealed record VersusLine(string Label, string Value, string Delta, bool IsBetter, bool IsWorse, bool IsSame)
{
    public static VersusLine From(Insights.Versus v) => new(v.Label,
        v.Label == "PLACE" && int.TryParse(v.Value, out var p) ? Headlines.Ordinal(p) : v.Value, v.Delta, v.Better == true, v.Better == false, v.Better == null);
}

/// <summary>The corner card after a match: the headline, four numbers against your usual, records, and the token.</summary>
public sealed partial class PostMatchViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    public MatchSummary Match { get; }
    public string Kicker { get; }
    public string Headline { get; }
    public string Dek { get; }
    public List<VersusLine> Stats { get; }
    public List<string> Records { get; }
    public bool HasRecords => Records.Count > 0;
    public string TokenLine { get; }
    public bool HasToken => TokenLine != null;

    public event Action CloseRequested;

    public PostMatchViewModel(MainViewModel main, MatchSummary m)
    {
        _main = main; Match = m;
        var e = LogEntry.From(m);
        Kicker = "MATCH REPORT · " + e.Kicker;
        Headline = e.Headline; Dek = e.Dek;
        var all = main.Library.Matches;
        Stats = Insights.VsUsual(m, all).Select(VersusLine.From).ToList();
        Records = Insights.Highlights(m, all);
        if (m.Season == Treasure.Season)
        {
            var s = main.Settings;
            var rule = new CountRule(s.CountRefills, s.CountBroken, s.CountTeam);
            var season = all.Where(x => x.Season == Treasure.Season).ToList();
            var now = Treasure.Build(season, rule, DateTime.UtcNow);
            var before = Treasure.Build(season.Where(x => x.File != m.File).ToList(), rule, DateTime.UtcNow);
            var ci = CultureInfo.CurrentCulture;
            TokenLine = $"Pirate token: {(now.TokenChance * 100).ToString("0.00", ci)}% chance so far (+{((now.TokenChance - before.TokenChance) * 100).ToString("0.000", ci)} points from {Treasure.Eligible(m, rule)} chests)";
        }
    }

    [RelayCommand] private void Open() { _main.ShowWindow?.Invoke(); _main.OpenMatch(Match); CloseRequested?.Invoke(); }
    [RelayCommand] private void Close() => CloseRequested?.Invoke();
}
