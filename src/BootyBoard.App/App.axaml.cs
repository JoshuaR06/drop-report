using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using BootyBoard.App.ViewModels;
using BootyBoard.App.Views;

namespace BootyBoard.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var a = Program.Args;
            RequestedThemeVariant = vm.Settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var win = new MainWindow { DataContext = vm };
            desktop.MainWindow = win;

            // --scan: one archive + analyze pass, log the result, exit (tests the watcher path without clicking)
            if (a.Contains("--scan"))
            {
                win.Opened += async (_, _) =>
                {
                    var r = await vm.Library.ScanAsync(new Progress<string>(s => Services.AppData.Log("scan: " + s)));
                    Services.AppData.Log($"scan result: archived {r.Archived}, analyzed {r.Analyzed}, failed {r.Failed}, paused {r.PausedForMatch}, matches {vm.Library.Matches.Count}");
                    desktop.Shutdown();
                };
            }
            // --shot <page> <out.png> [light]: render a page to a PNG and exit (README screenshots, layout checks)
            var i = Array.IndexOf(a, "--shot");
            if (i >= 0 && i + 2 < a.Length)
            {
                if (a.Contains("light")) RequestedThemeVariant = ThemeVariant.Light;
                win.Opened += async (_, _) =>
                {
                    vm.Navigate(a[i + 1]);
                    await Task.Delay(400);
                    // "@900": scroll the page down 900 px first (to check the lower sections)
                    var scroll = a.FirstOrDefault(x => x.StartsWith('@'));
                    if (scroll != null && double.TryParse(scroll[1..], out var y))
                        foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<ScrollViewer>()
                                     .OrderByDescending(s => s.Extent.Height).Take(1))   // the page, not the nav list
                            sv.Offset = new Vector(0, y);
                    await Task.Delay(1100);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var size = new PixelSize((int)win.Bounds.Width, (int)win.Bounds.Height);
                        using var bmp = new RenderTargetBitmap(size);
                        bmp.Render(win);
                        bmp.Save(a[i + 2]);
                    });
                    desktop.Shutdown();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void SetTheme(string theme) =>
        Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
}
