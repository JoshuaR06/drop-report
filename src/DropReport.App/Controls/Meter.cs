using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DropReport.App.Controls;

/// <summary>A thin horizontal bar: Value from 0 to 1 on a hairline track. Fill defaults to the accent.</summary>
public sealed class Meter : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Meter, double>(nameof(Value));
    public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<Meter, IBrush>(nameof(Fill));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    static Meter() { AffectsRender<Meter>(ValueProperty, FillProperty); }
    public Meter() { Height = 6; }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        var rad = r.Height / 2;
        ctx.DrawRectangle(Res("GridBrush"), null, r, rad, rad);
        var v = Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 1);
        if (v <= 0) return;
        var w = Math.Max(r.Height, r.Width * v);   // a sliver stays visible as a round dot
        ctx.DrawRectangle(Fill ?? Res("AccentBrush"), null, new Rect(0, 0, w, r.Height), rad, rad);
    }
}
