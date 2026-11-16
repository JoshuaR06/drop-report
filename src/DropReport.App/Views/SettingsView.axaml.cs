using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DropReport.App.ViewModels;

namespace DropReport.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async Task<string> PickFolder(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return null;
        var r = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    private async void OnBrowseReplay(object sender, RoutedEventArgs e)
    {
        if (await PickFolder("Fortnite replay folder") is string p && DataContext is SettingsViewModel vm) vm.ReplayFolder = p;
    }

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        if (await PickFolder("Folder with old replays") is string p && DataContext is SettingsViewModel vm) vm.AddExtraFolder(p);
    }
}
