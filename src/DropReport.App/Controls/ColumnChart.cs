using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DropReport.App.ViewModels;

namespace DropReport.App.Controls;

/// <summary>
/// Column chart, stacked when items carry two values. Dataviz rules: bars capped at 24 px, 4 px rounded data-end and
/// square baseline, 2 px surface gap between stacked segments, hairline grid, clean ticks, hover wash + tooltip.
/// Series colors come from the theme (Series1Brush, Series2Brush: validated CVD-safe pair).
/// </summary>
public sealed class ColumnChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ColumnItem>> ItemsProperty = AvaloniaProperty.Register<ColumnChart, IReadOnlyList<ColumnItem>>(nameof(Items));
    public IReadOnlyList<ColumnItem> Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    private int _hover = -1;
    private const double L = 34, B = 22, T = 8;

    static ColumnChart() { AffectsRender<ColumnChart>(ItemsProperty); }

    public ColumnChart() { ClipToBounds = false; MinHeight = 180; }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext ctx)
    {
        var items = Items ?? Array.Empty<ColumnItem>();
        double w = Bounds.Width, h = Bounds.Height;
        var muted = Res("MutedBrush");
        var grid = new Pen(Res("GridBrush"), 1);
        var axis = new Pen(Res("LineBrush"), 1);
        double max = Math.Max(1, items.Count == 0 ? 1 : items.Max(i => i.Values.Sum()));
        double step = max <= 5 ? 1 : max <= 12 ? 2 : max <= 30 ? 5 : max <= 60 ? 10 : max <= 150 ? 25 : 50;
        double top = Math.Ceiling(max / step) * step;
        double Y(double v) => T + (h - T - B) * (1 - v / top);
        var tf = new Typeface(FontFamily.Default);
        for (double v = 0; v <= top + 0.001; v += step)
        {
            ctx.DrawLine(v == 0 ? axis : grid, new Point(L, Y(v)), new Point(w, Y(v)));
            var ft = new FormattedText(v.ToString("0", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 11, muted);
            ctx.DrawText(ft, new Point(L - 6 - ft.Width, Y(v) - ft.Height / 2));
        }
        if (items.Count == 0) return;
        double band = (w - L) / items.Count, bw = Math.Max(2, Math.Min(24, band * 0.7));
        var series = new[] { Res("Series1Brush"), Res("Series2Brush") };
        for (var i = 0; i < items.Count; i++)
        {
            if (i == _hover) ctx.FillRectangle(Res("WashBrush"), new Rect(L + band * i, T, band, h - T - B));
            double x = L + band * i + (band - bw) / 2, baseV = 0;
            var vals = items[i].Values;
            for (var s = 0; s < vals.Length; s++)
            {
                var v = vals[s]; if (v <= 0) continue;
                double y0 = Y(baseV), y1 = Y(baseV + v);
                double hh = Math.Max(0, y0 - y1 - (baseV > 0 ? 2 : 0));              // 2 px surface gap above the lower segment
                bool isTop = vals.Skip(s + 1).All(x2 => x2 <= 0);
                double r = isTop ? Math.Min(4, Math.Min(hh, bw / 2)) : 0;
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(x, y1 + hh), true);
                    gc.LineTo(new Point(x, y1 + r));
                    gc.QuadraticBezierTo(new Point(x, y1), new Point(x + r, y1));
                    gc.LineTo(new Point(x + bw - r, y1));
                    gc.QuadraticBezierTo(new Point(x + bw, y1), new Point(x + bw, y1 + r));
                    gc.LineTo(new Point(x + bw, y1 + hh));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(series[Math.Min(s, series.Length - 1)], null, g);
                baseV += v;
            }
        }
        foreach (var i in new[] { 0, (items.Count - 1) / 2, items.Count - 1 }.Distinct())
        {
            var ft = new FormattedText(items[i].Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 11, muted);
            ctx.DrawText(ft, new Point(Math.Clamp(L + band * i + band / 2 - ft.Width / 2, 0, w - ft.Width), h - B + 5));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var items = Items; if (items == null || items.Count == 0) return;
        var p = e.GetPosition(this);
        var band = (Bounds.Width - L) / items.Count;
        var i = p.X < L ? -1 : Math.Min(items.Count - 1, (int)((p.X - L) / band));
        if (i == _hover) return;
        _hover = i; InvalidateVisual();
        ToolTip.SetTip(this, i >= 0 ? items[i].Tip : null);
        ToolTip.SetIsOpen(this, i >= 0);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1; InvalidateVisual(); ToolTip.SetIsOpen(this, false);
    }
}
