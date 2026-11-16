using Avalonia;

namespace BootyBoard.App;

internal static class Program
{
    /// <summary>Command-line extras (used for docs/tests): --shot &lt;page&gt; &lt;out.png&gt; renders a page and exits.</summary>
    public static string[] Args { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.AppData.Log("crash: " + e.ExceptionObject);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
