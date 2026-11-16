using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BootyBoard.App.ViewModels;

namespace BootyBoard.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var r = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder with old replays" });
        if (r.Count > 0 && r[0].TryGetLocalPath() is string p && DataContext is SetupViewModel vm) vm.AddExtraFolder(p);
    }
}
