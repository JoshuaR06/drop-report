using Avalonia;

namespace DropReport.App;

internal static class Program
{
    /// <summary>Command-line extras: --tray (start hidden), --shot &lt;page&gt; &lt;out.png&gt;, --popup-shot &lt;out.png&gt;, --scan.</summary>
    public static string[] Args { get; private set; } = Array.Empty<string>();

    /// <summary>Set by a second launch: the running copy shows its window.</summary>
    public static EventWaitHandle ShowSignal { get; private set; }

    private static Mutex _single;

    /// <summary>A one-shot tool run (--shot, --scan, ...): no watcher, no tray, no single-instance lock.</summary>
    public static bool IsTool { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.AppData.Log("crash: " + e.ExceptionObject);

        // one Drop Report at a time (tools runs like --shot/--scan are exempt): a second launch wakes the first and exits
        var tool = IsTool = args.Any(a => a is "--shot" or "--scan" or "--popup-shot" or "--card-shot");
        if (!tool)
        {
            _single = new Mutex(true, @"Local\DropReport.Single", out var first);
            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DropReport.Show");
            if (!first) { ShowSignal.Set(); return; }
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(_single);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
