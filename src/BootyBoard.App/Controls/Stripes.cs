using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BootyBoard.App.Controls;

/// <summary>Broadcast-style diagonal hatching behind a hero band. Purely decorative, very low contrast, fades out to the left.</summary>
public sealed class Stripes : Control
{
    public Stripes() { IsHitTestVisible = false; ClipToBounds = true; }

    public override void Render(DrawingContext ctx)
    {
        if (!this.TryFindResource("WatermarkBrush", ActualThemeVariant, out var v) || v is not ISolidColorBrush b) return;
        double w = Bounds.Width, h = Bounds.Height;
        const double gap = 14;
        for (double x = -h; x < w; x += gap)
        {
            // fade in from the left third so the hero number stays clean
            var k = Math.Clamp((x + h / 2) / (w * 0.6), 0, 1);
            var pen = new Pen(new SolidColorBrush(b.Color, b.Opacity * k * 2.2), 2);
            ctx.DrawLine(pen, new Point(x, h), new Point(x + h, 0));
        }
    }
}
