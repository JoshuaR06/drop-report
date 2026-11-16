using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DropReport.App.ViewModels;
using DropReport.App.Views;

namespace DropReport.App.Services;

/// <summary>Renders a share card to a 1600x900 PNG in Pictures\Drop Report and puts the image on the clipboard.</summary>
public static class ShareCards
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Drop Report");

    /// <returns>The saved file, or null if it failed (logged).</returns>
    public static async Task<string> MakeAsync(ShareCardViewModel card)
    {
        try
        {
            var view = new ShareCardView { DataContext = card };
            // a borderless window off-screen gives the card the app's styles and fonts while it lays out
            var host = new Window
            {
                Content = view, SizeToContent = SizeToContent.WidthAndHeight, WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false, ShowActivated = false, Position = new PixelPoint(-30000, -30000),
            };
            host.Show();
            await Task.Delay(350);
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"drop-report-{card.FileStem}.png");
            using (var bmp = new RenderTargetBitmap(new PixelSize(1600, 900), new Vector(96 * 1600.0 / 1200, 96 * 1600.0 / 1200)))
            {
                bmp.Render(view);
                bmp.Save(path);
            }
            host.Close();
            try
            {
                var top = App.MainWindow;
                if (top?.Clipboard is { } clip) await clip.SetValueAsync(DataFormat.Bitmap, new Bitmap(path));
            }
            catch (Exception e) { AppData.Log("share card clipboard: " + e.Message); }
            return path;
        }
        catch (Exception e) { AppData.Log("share card: " + e); return null; }
    }
}
