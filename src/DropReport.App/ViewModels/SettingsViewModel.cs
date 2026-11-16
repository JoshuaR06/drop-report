using System.Collections.ObjectModel;
using System.Diagnostics;
using DropReport.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DropReport.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private Settings S => _main.Settings;

    [ObservableProperty] private string _replayFolder;
    [ObservableProperty] private bool _watchWhileOpen;
    [ObservableProperty] private bool _hideNames;
    [ObservableProperty] private bool _lightTheme;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _postMatchPopup;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _onlineOdds;
    [ObservableProperty] private bool _readGameLog;
    public string AutostartNote => Autostart.CanRegister ? "Starts quietly in the tray when you sign in to Windows. Turn off any time."
        : "This is a development build, which can't start with Windows. The installed app can.";
    [ObservableProperty] private string _message = "";
    public ObservableCollection<string> ExtraFolders { get; }
    public string NamesText => _main.Library.Names.IsEmpty ? "Not built: items show internal code names." : $"{_main.Library.Names.ById.Count:N0} items from game version {_main.Library.Names.Build}";
    public string DataFolder => AppData.Root;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _replayFolder = S.ReplayFolder; _watchWhileOpen = S.WatchWhileOpen; _hideNames = S.HideNames; _lightTheme = S.Theme == "Light";
        _startWithWindows = S.StartWithWindows && Autostart.IsOn; _closeToTray = S.CloseToTray; _postMatchPopup = S.PostMatchPopup;
        _checkForUpdates = S.CheckForUpdates; _onlineOdds = S.OnlineOdds; _readGameLog = S.ReadGameLog;
        ExtraFolders = new(S.ExtraFolders);
    }

    partial void OnReplayFolderChanged(string value) { S.ReplayFolder = value; S.Save(); }
    partial void OnWatchWhileOpenChanged(bool value) { S.WatchWhileOpen = value; S.Save(); _main.SetWatching(value); }
    partial void OnHideNamesChanged(bool value) { S.HideNames = value; S.Save(); }
    partial void OnStartWithWindowsChanged(bool value) { S.StartWithWindows = value; S.Save(); Autostart.Set(value); }
    partial void OnCloseToTrayChanged(bool value) { S.CloseToTray = value; S.Save(); }
    partial void OnPostMatchPopupChanged(bool value) { S.PostMatchPopup = value; S.Save(); }
    partial void OnCheckForUpdatesChanged(bool value) { S.CheckForUpdates = value; S.Save(); }
    partial void OnOnlineOddsChanged(bool value) { S.OnlineOdds = value; S.Save(); }
    partial void OnReadGameLogChanged(bool value) { S.ReadGameLog = value; S.Save(); _main.Library.LoadSummaries(); }
    partial void OnLightThemeChanged(bool value) { S.Theme = value ? "Light" : "Dark"; S.Save(); App.SetTheme(S.Theme); }

    public void AddExtraFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || S.ExtraFolders.Contains(path)) return;
        S.ExtraFolders.Add(path); S.Save(); ExtraFolders.Add(path);
        Message = "Folder added. Scan for new replays to import it.";
    }

    [RelayCommand] private void RemoveExtraFolder(string path) { S.ExtraFolders.Remove(path); S.Save(); ExtraFolders.Remove(path); }
    [RelayCommand] private void UpdateNames() => _main.CurrentPage = new SetupViewModel(_main, step: 1);

    [RelayCommand]
    private async Task RereadAll()
    {
        foreach (var f in Directory.GetFiles(AppData.Matches, "*.failed")) File.Delete(f);
        Message = "Re-reading every saved replay...";
        var r = await _main.Library.ScanAsync(new Progress<string>(p => Message = p), reanalyzeAll: true);
        Message = r.PausedForMatch ? "Paused: a match is being recorded." : $"Re-read {r.Analyzed} matches" + (r.Failed > 0 ? $", {r.Failed} could not be read" : "");
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppData.Root}\"") { UseShellExecute = true }); } catch { }
    }
}
