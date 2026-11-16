using System.Collections.ObjectModel;
using Avalonia.Threading;
using DropReport.App.Services;
using DropReport.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DropReport.App.ViewModels;

public sealed record NavItem(string Key, string Title, string Sub);

/// <summary>Season + mode filter shared by the stats pages.</summary>
public sealed partial class FilterState : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Option<int?>> _seasons = new();
    [ObservableProperty] private Option<int?> _season;
    public Option<string>[] Modes { get; } = { new("All modes", null), new("Solo", "Solo"), new("Duo", "Duo"), new("Squad", "Squad") };
    [ObservableProperty] private Option<string> _mode;
    public event Action Changed;

    public FilterState() { _mode = Modes[0]; }
    partial void OnSeasonChanged(Option<int?> value) => Changed?.Invoke();
    partial void OnModeChanged(Option<string> value) => Changed?.Invoke();

    public void Refresh(IEnumerable<MatchSummary> all)
    {
        var keep = Season?.Value;
        var seasons = all.Select(m => m.Season).Where(s => s > 0).Distinct().OrderByDescending(s => s).ToList();
        var opts = seasons.Select((s, i) => new Option<int?>(i == 0 ? $"This season (v{s})" : $"Season v{s}", s)).Append(new Option<int?>("All seasons", null)).ToList();
        Seasons = new ObservableCollection<Option<int?>>(opts);
        Season = opts.FirstOrDefault(o => o.Value == keep && keep != null) ?? opts.First();
    }

    public List<MatchSummary> Apply(IEnumerable<MatchSummary> all) =>
        all.Where(m => (Season?.Value == null || m.Season == Season.Value) && (Mode?.Value == null || m.Mode == Mode.Value))
           .OrderBy(m => m.Start ?? DateTime.MinValue).ToList();
}

public sealed partial class MainViewModel : ViewModelBase
{
    public Settings Settings { get; } = Settings.Load();
    public Library Library { get; }
    public FilterState Filter { get; } = new();
    public ObservableCollection<NavItem> Nav { get; } = new()
    {
        new("home", "Home", "Your latest match and form"), new("season", "The Season", "Totals and scouting report"), new("treasure", "Treasure Hunt", "Token odds and chest rewards"),
        new("weapons", "Weapons", "What you're best with"), new("spots", "Drop Spots", "Where to land"),
        new("matches", "Match Log", "Every match, replayable"),
        new("settings", "Settings", "Folders, theme, privacy"), new("about", "About", "Safety, credits, support"),
    };

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _watching;
    /// <summary>One line across the top of every page when something needs you (game update, new version).</summary>
    [ObservableProperty] private string _banner;
    [ObservableProperty] private string _bannerAction;
    private Action _bannerDo;

    private readonly Dictionary<string, ViewModelBase> _pages = new();
    private DispatcherTimer _watchTimer;

    public MainViewModel()
    {
        Library = new Library(Settings);
        Library.LoadSummaries();
        Filter.Refresh(Library.Matches);
        Library.Changed += () => Dispatcher.UIThread.Post(() => { Filter.Refresh(Library.Matches); RefreshPages(); UpdateStatus(); });
        UpdateStatus();
        if (!Settings.SetupDone) CurrentPage = new SetupViewModel(this);
        else SelectedNav = Nav[0];
        SetWatching(Settings.WatchWhileOpen && !Program.IsTool);
        Online.LoadCachedOdds();
        CheckGameVersion();
        if (!Program.IsTool) _ = CheckOnlineAsync();
    }

    [RelayCommand] private void DoBanner() { var a = _bannerDo; Banner = null; a?.Invoke(); }
    [RelayCommand] private void DismissBanner() => Banner = null;

    private void ShowBanner(string text, string action, Action act) { Banner = text; BannerAction = action; _bannerDo = act; }

    /// <summary>Fortnite updated since item names were built: new items would show code names until refreshed.</summary>
    public void CheckGameVersion()
    {
        try
        {
            var game = DropReport.GameData.GameInstall.Find();
            var built = Library.Names.Build;
            if (game?.Build != null && !string.IsNullOrEmpty(built) && !string.Equals(game.Build, built, StringComparison.OrdinalIgnoreCase))
                ShowBanner($"Fortnite updated to {Short(game.Build)}. Refresh item names so new items show their real names.", "Refresh names",
                    () => CurrentPage = new SetupViewModel(this, step: 1));
        }
        catch (Exception e) { AppData.Log("game version check: " + e.Message); }
        static string Short(string b) => System.Text.RegularExpressions.Regex.Match(b, @"\d+\.\d+") is { Success: true } x ? "v" + x.Value : b;
    }

    private async Task CheckOnlineAsync()
    {
        if (Settings.OnlineOdds && await Online.RefreshOddsAsync()) RefreshPages();
        if (Settings.CheckForUpdates && await Online.CheckForUpdateAsync() is { } up && Banner == null)
            ShowBanner($"Drop Report {up.Version} is out.", "Get it", () => AboutViewModel.Open(up.Url));
    }

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value == null) return;
        if (!_pages.TryGetValue(value.Key, out var page))
            _pages[value.Key] = page = value.Key switch
            {
                "home" => new HomeViewModel(this), "season" => new SeasonViewModel(this), "matches" => new MatchesViewModel(this), "weapons" => new WeaponsViewModel(this),
                "treasure" => new TreasureViewModel(this), "spots" => new SpotsViewModel(this), "settings" => new SettingsViewModel(this), _ => new AboutViewModel(),
            };
        CurrentPage = page;
    }

    public void Navigate(string key)
    {
        if (key == "setup") { CurrentPage = new SetupViewModel(this); return; }
        if (key.StartsWith("match:"))   // match:<part of the replay file name> (screenshots)
        {
            var hit = Library.Matches.FirstOrDefault(x => x.File.Contains(key[6..], StringComparison.OrdinalIgnoreCase));
            if (hit != null) OpenMatch(hit);
            return;
        }
        if (key == "match")
        {
            var pick = Library.Matches.FirstOrDefault(x => x.Season == Filter.Season?.Value) ?? Library.Matches.FirstOrDefault();
            if (pick != null) OpenMatch(pick);
            return;
        }
        SelectedNav = Nav.FirstOrDefault(n => n.Key == key) ?? Nav[0];
    }

    public void OpenMatch(MatchSummary m)
    {
        CurrentPage = new MatchDetailViewModel(this, m);
#pragma warning disable MVVMTK0034   // set the field so the nav highlight moves without swapping the page back
        _selectedNav = Nav.First(n => n.Key == "matches"); OnPropertyChanged(nameof(SelectedNav));
#pragma warning restore MVVMTK0034
    }
    public void Back() => CurrentPage = _pages.GetValueOrDefault("matches") ?? new MatchesViewModel(this);

    public void FinishSetup()
    {
        Settings.SetupDone = true; Settings.Save();
        SelectedNav = null; SelectedNav = Nav[0];
    }

    private void RefreshPages()
    {
        foreach (var p in _pages.Values.OfType<IRefreshable>()) p.Refresh();
    }

    private void UpdateStatus()
    {
        if (Busy) return;
        var n = Library.Matches.Count;
        Status = n == 0 ? "No matches yet" : $"{n} matches · last {Fmt.When(Library.Matches[0].Start)}";
    }

    /// <summary>A match finished in the last few hours was just read (the tray pop-up listens).</summary>
    public event Action<MatchSummary> NewMatch;
    /// <summary>Make a share card, save + copy it, and say where it went.</summary>
    public async Task ShareAsync(ShareCardViewModel card)
    {
        Status = "Making your card...";
        var path = await ShareCards.MakeAsync(card);
        Status = path == null ? "Couldn't make the card (see the log)" : "Card copied: paste it anywhere. Also saved to Pictures\\Drop Report.";
    }

    /// <summary>Brings the main window forward (set by the app; the pop-up and tray use it).</summary>
    public Action ShowWindow { get; set; }

    [RelayCommand]
    private async Task Scan()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var known = Library.Matches.Select(x => x.File).ToHashSet();
            var r = await Library.ScanAsync(new Progress<string>(s => Status = s));
            var fresh = Library.Matches.Where(x => !known.Contains(x.File) && x.Start > DateTime.Now.AddHours(-3)).OrderByDescending(x => x.Start).FirstOrDefault();
            if (fresh != null && known.Count > 0) NewMatch?.Invoke(fresh);
            Busy = false;
            UpdateStatus();
            if (r.PausedForMatch) Status = "Match in progress, reading paused";
            else if (r.Analyzed + r.Archived > 0) Status = $"Added {r.Analyzed} matches" + (r.Failed > 0 ? $" ({r.Failed} could not be read)" : "");
        }
        catch (Exception e) { Busy = false; Status = "Scan failed: " + e.Message; AppData.Log("scan failed: " + e); }
    }

    public void SetWatching(bool on)
    {
        Watching = on;
        _watchTimer?.Stop();
        if (!on) return;
        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _watchTimer.Tick += async (_, _) => await Scan();
        _watchTimer.Start();
        _ = Scan();
    }
}

public interface IRefreshable { void Refresh(); }
