using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using DropReport.App.Services;
using DropReport.App.ViewModels;
using DropReport.App.Views;

namespace DropReport.App;

public partial class App : Application
{
    private MainWindow _win;
    /// <summary>The main window (for the clipboard), even while hidden in the tray.</summary>
    public static Window MainWindow { get; private set; }
    private MainViewModel _vm;
    private bool _quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _vm = new MainViewModel();
            var a = Program.Args;
            RequestedThemeVariant = vm.Settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var win = _win = new MainWindow { DataContext = vm };
            MainWindow = win;
            vm.ShowWindow = ShowMain;
            var tool = a.Contains("--scan") || a.Contains("--shot") || a.Contains("--popup-shot") || a.Contains("--card-shot");

            if (tool) desktop.MainWindow = win;
            else
            {
                // the tray keeps Drop Report alive between matches; closing the window just hides it (when CloseToTray is on)
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SetUpTray(desktop);
                win.Closing += (_, e) =>
                {
                    if (_quitting || !vm.Settings.CloseToTray) { Quit(desktop); return; }
                    e.Cancel = true; win.Hide();
                };
                if (!a.Contains("--tray")) desktop.MainWindow = win;   // --tray: start hidden (sign-in launch)
                vm.NewMatch += m => { if (vm.Settings.PostMatchPopup) ShowPopup(m); };
                Autostart.Sync(vm.Settings.StartWithWindows);
                // a second launch signals us to come forward
                new Thread(() => { while (Program.ShowSignal?.WaitOne() == true) Dispatcher.UIThread.Post(ShowMain); }) { IsBackground = true }.Start();
            }

            // --scan: one archive + analyze pass, log the result, exit (tests the watcher path without clicking)
            if (a.Contains("--scan"))
            {
                win.Opened += async (_, _) =>
                {
                    var r = await vm.Library.ScanAsync(new Progress<string>(s => AppData.Log("scan: " + s)));
                    AppData.Log($"scan result: archived {r.Archived}, analyzed {r.Analyzed}, failed {r.Failed}, paused {r.PausedForMatch}, matches {vm.Library.Matches.Count}");
                    desktop.Shutdown();
                };
            }
            // --shot <page> <out.png> [light] [@900]: render a page to a PNG and exit (README screenshots, layout checks)
            if (a.Contains("hidenames")) vm.Settings.HideNames = true;   // screenshots for the README: never show other players
            var i = Array.IndexOf(a, "--shot");
            if (i >= 0 && i + 2 < a.Length)
            {
                if (a.Contains("light")) RequestedThemeVariant = ThemeVariant.Light;
                win.Opened += async (_, _) =>
                {
                    vm.Navigate(a[i + 1]);
                    await Task.Delay(400);
                    var scroll = a.FirstOrDefault(x => x.StartsWith('@'));
                    if (scroll != null && double.TryParse(scroll[1..], out var y))
                        foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<ScrollViewer>()
                                     .OrderByDescending(s => s.Extent.Height).Take(1))   // the page, not the nav list
                            sv.Offset = new Vector(0, y);
                    await Task.Delay(1100);
                    Render(win, a[i + 2]);
                    desktop.Shutdown();
                };
            }
            // --card-shot <match|season> <out.png>: make a share card (as the Share button does), copy it to out.png, exit
            var k = Array.IndexOf(a, "--card-shot");
            if (k >= 0 && k + 2 < a.Length && vm.Library.Matches.Count > 0)
            {
                win.Opened += async (_, _) =>
                {
                    var all = vm.Library.Matches;
                    var card = a[k + 1] == "season"
                        ? ShareCardViewModel.ForSeason(all.Where(x => x.Season == all[0].Season).ToList(), all, vm.Settings, $"Season {all[0].Season}")
                        : ShareCardViewModel.ForMatch(all[0], all, vm.Settings);
                    var path = await ShareCards.MakeAsync(card);
                    if (path != null) File.Copy(path, a[k + 2], true);
                    desktop.Shutdown();
                };
            }
            // --popup-shot <out.png>: show the post-match card for the newest match, render it, exit
            var j = Array.IndexOf(a, "--popup-shot");
            if (j >= 0 && j + 1 < a.Length && vm.Library.Matches.Count > 0)
            {
                win.Opened += async (_, _) =>
                {
                    var p = ShowPopup(vm.Library.Matches[0]);
                    await Task.Delay(1400);
                    Render(p, a[j + 1]);
                    desktop.Shutdown();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void Render(Visual v, string path) => Dispatcher.UIThread.Invoke(() =>
    {
        var size = new PixelSize(Math.Max(1, (int)v.Bounds.Width), Math.Max(1, (int)v.Bounds.Height));
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(v);
        bmp.Save(path);
    });

    private PostMatchWindow ShowPopup(MatchSummary m)
    {
        var w = new PostMatchWindow { DataContext = new PostMatchViewModel(_vm, m) };
        w.Show();
        return w;
    }

    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        using var icon = AssetLoader.Open(new Uri("avares://DropReport/Assets/dropreport.ico"));
        var menu = new NativeMenu();
        NativeMenuItem Item(string text, Action act) { var it = new NativeMenuItem(text); it.Click += (_, _) => act(); menu.Items.Add(it); return it; }
        Item("Open Drop Report", ShowMain);
        Item("Last match report", () => { if (_vm.Library.Matches.Count > 0) ShowPopup(_vm.Library.Matches[0]); });
        Item("Scan for new replays", () => _vm.ScanCommand.Execute(null));
        menu.Items.Add(new NativeMenuItemSeparator());
        Item("Quit", () => Quit(desktop));
        var tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Drop Report", Menu = menu };
        tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }

    private void ShowMain()
    {
        if (_win == null) return;
        _win.Show();
        if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
        _win.Activate();
    }

    private void Quit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _quitting = true;
        TrayIcon.SetIcons(this, new TrayIcons());
        desktop.Shutdown();
    }

    public static void SetTheme(string theme) =>
        Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
}
