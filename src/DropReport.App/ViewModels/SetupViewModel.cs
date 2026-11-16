using System.Collections.ObjectModel;
using DropReport.App.Services;
using DropReport.GameData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DropReport.App.ViewModels;

/// <summary>First run (and "Update item names"): welcome -> real names (consent to downloads) -> import replays -> done.</summary>
public sealed partial class SetupViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly GameInstall _game = GameInstall.Find();
    public IReadOnlyList<Download> Downloads => GameDataBuilder.Downloads;
    public string GameText => _game == null ? "Fortnite wasn't found through the Epic Games Launcher. You can still use Drop Report; items will show their internal code names."
                                            : $"Found your game: {_game.Folder} ({_game.Build})";
    public bool GameFound => _game != null;
    public string ReplayFolderText => Directory.Exists(_main.Settings.ReplayFolder)
        ? $"Replays folder: {_main.Settings.ReplayFolder} ({Directory.GetFiles(_main.Settings.ReplayFolder, "*.replay").Length} replays)"
        : "Fortnite's replay folder wasn't found. Turn on replay recording in Fortnite (Settings > Game > Replays), play a match, then scan again.";
    public ObservableCollection<string> ExtraFolders => new(_main.Settings.ExtraFolders);

    [ObservableProperty] private int _step;
    [ObservableProperty] private string _progress = "";
    [ObservableProperty] private bool _working;
    [ObservableProperty] private string _error = "";
    public bool StepWelcome => Step == 0;
    public bool StepNames => Step == 1;
    public bool StepImport => Step == 2;
    public bool NamesUpToDate => new GameDataBuilder(AppData.GameData).IsUpToDate(_game);

    public SetupViewModel(MainViewModel main, int step = 0) { _main = main; _step = step; }

    partial void OnStepChanged(int value) { OnPropertyChanged(nameof(StepWelcome)); OnPropertyChanged(nameof(StepNames)); OnPropertyChanged(nameof(StepImport)); }

    [RelayCommand] private void Next() { Error = ""; Step = Step == 0 && (!GameFound || NamesUpToDate) ? 2 : Step + 1; }

    [RelayCommand]
    private async Task BuildNames()
    {
        if (_game == null || Working) return;
        Working = true; Error = "";
        try
        {
            var b = new GameDataBuilder(AppData.GameData);
            await b.BuildAsync(_game, new Progress<string>(p => Progress = p));
            _main.Library.ReloadNames();
            _main.Settings.GameDataDeclined = false; _main.Settings.Save();
            AppData.Log("item names built for " + _game.Build);
            Step = 2;
        }
        catch (Exception e) { Error = e.Message; AppData.Log("names build failed: " + e); }
        finally { Working = false; }
    }

    [RelayCommand] private void SkipNames() { _main.Settings.GameDataDeclined = true; _main.Settings.Save(); Step = 2; }

    public void AddExtraFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || _main.Settings.ExtraFolders.Contains(path)) return;
        _main.Settings.ExtraFolders.Add(path); _main.Settings.Save();
        OnPropertyChanged(nameof(ExtraFolders));
    }

    /// <summary>Offered on the last step: run in the tray from sign-in and show a card after each match.</summary>
    [ObservableProperty] private bool _runInBackground = true;

    [RelayCommand]
    private async Task Import()
    {
        if (Working) return;
        Working = true; Error = "";
        var st = _main.Settings;
        st.StartWithWindows = RunInBackground; st.WatchWhileOpen = true; st.PostMatchPopup = RunInBackground; st.Save();
        Autostart.Set(RunInBackground);
        _main.SetWatching(true);
        try
        {
            var r = await _main.Library.ScanAsync(new Progress<string>(p => Progress = p), reanalyzeAll: _main.Library.Matches.Count > 0);
            Progress = r.PausedForMatch ? "A match is being recorded; reading paused. It picks up when you scan again." : $"Read {r.Analyzed} matches" + (r.Failed > 0 ? $", {r.Failed} could not be read" : "");
            _main.FinishSetup();
        }
        catch (Exception e) { Error = e.Message; }
        finally { Working = false; }
    }
}
