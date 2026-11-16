using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DropReport.App.Controls;

/// <summary>
/// Chance of at least one token against chests opened: 1 - (1 - p)^n. Solid and shaded up to where you are, dashed on to
/// where your pace takes you by the season's end, with both points marked. Hover shows the chance at any chest count.
/// </summary>
public sealed class ChanceCurve : Control
{
    public static readonly StyledProperty<double> PerChestProperty = AvaloniaProperty.Register<ChanceCurve, double>(nameof(PerChest), 1 / 137_500.0);
    public static readonly StyledProperty<double> CurrentProperty = AvaloniaProperty.Register<ChanceCurve, double>(nameof(Current));
    public static readonly StyledProperty<double> CurrentChanceProperty = AvaloniaProperty.Register<ChanceCurve, double>(nameof(CurrentChance));
    public static readonly StyledProperty<double> ProjectedProperty = AvaloniaProperty.Register<ChanceCurve, double>(nameof(Projected));
    public static readonly StyledProperty<double> ProjectedChanceProperty = AvaloniaProperty.Register<ChanceCurve, double>(nameof(ProjectedChance));
    public static readonly StyledProperty<string> ProjectedLabelProperty = AvaloniaProperty.Register<ChanceCurve, string>(nameof(ProjectedLabel), "At your pace");

    public double PerChest { get => GetValue(PerChestProperty); set => SetValue(PerChestProperty, value); }
    public double Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public double CurrentChance { get => GetValue(CurrentChanceProperty); set => SetValue(CurrentChanceProperty, value); }
    public double Projected { get => GetValue(ProjectedProperty); set => SetValue(ProjectedProperty, value); }
    public double ProjectedChance { get => GetValue(ProjectedChanceProperty); set => SetValue(ProjectedChanceProperty, value); }
    public string ProjectedLabel { get => GetValue(ProjectedLabelProperty); set => SetValue(ProjectedLabelProperty, value); }

    private double? _hoverX;
    private const double L = 46, R = 16, T = 34, B = 26;

    static ChanceCurve()
    {
        AffectsRender<ChanceCurve>(PerChestProperty, CurrentProperty, CurrentChanceProperty, ProjectedProperty, ProjectedChanceProperty, ProjectedLabelProperty);
    }

    public ChanceCurve() { MinHeight = 220; Background = Brushes.Transparent; }

    public static readonly StyledProperty<IBrush> BackgroundProperty = Border.BackgroundProperty.AddOwner<ChanceCurve>();
    public IBrush Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
    private Typeface Face(bool display = false) =>
        new(this.TryFindResource(display ? "DisplayFont" : "TextFont", ActualThemeVariant, out var f) && f is FontFamily ff ? ff : FontFamily.Default,
            FontStyle.Normal, display ? FontWeight.SemiBold : FontWeight.Normal);

    private (double xMax, double yMax) Range()
    {
        var xMax = Math.Max(Math.Max(Projected, Current) * 1.12, 1000);
        var yAtMax = 1 - Math.Pow(1 - PerChest, xMax);
        double[] steps = { 0.005, 0.01, 0.02, 0.025, 0.05, 0.1, 0.2, 0.25, 0.5, 1 };
        var yMax = steps.First(s => s >= yAtMax * 1.08);
        return (xMax, yMax);
    }

    private static double NiceStep(double range, int target)
    {
        var raw = range / target; var mag = Math.Pow(10, Math.Floor(Math.Log10(raw))); var n = raw / mag;
        return (n < 1.5 ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10) * mag;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(Background, new Rect(Bounds.Size));
        if (w < 120 || h < 100) return;
        var (xMax, yMax) = Range();
        double X(double n) => L + (w - L - R) * n / xMax;
        double Y(double c) => T + (h - T - B) * (1 - c / yMax);
        double C(double n) => 1 - Math.Pow(1 - PerChest, n);
        var muted = Res("MutedBrush"); var ink = Res("InkBrush"); var accent = Res("AccentBrush");
        var grid = new Pen(Res("GridBrush"), 1); var axis = new Pen(Res("LineBrush"), 1);
        var ci = CultureInfo.CurrentCulture;
        FormattedText Txt(string s, double size, IBrush b, bool display = false) => new(s, ci, FlowDirection.LeftToRight, Face(display), size, b);

        // grid + y ticks (percent)
        var ys = NiceStep(yMax, 4);
        for (double c = 0; c <= yMax + 1e-9; c += ys)
        {
            ctx.DrawLine(c == 0 ? axis : grid, new Point(L, Y(c)), new Point(w - R, Y(c)));
            var t = Txt((c * 100).ToString(c * 100 < 1 && c > 0 ? "0.#" : "0.#", ci) + "%", 11, muted);
            ctx.DrawText(t, new Point(L - 8 - t.Width, Y(c) - t.Height / 2));
        }
        // x ticks (chests)
        var xs = NiceStep(xMax, 5);
        for (double n = 0; n <= xMax; n += xs)
        {
            var t = Txt(n >= 1000 ? (n / 1000).ToString("0.#", ci) + "k" : n.ToString("0", ci), 11, muted);
            ctx.DrawText(t, new Point(X(n) - t.Width / 2, h - B + 6));
        }

        // shaded area up to you, solid line to you, dashed to the projection
        var cur = Math.Clamp(Current, 0, xMax);
        if (cur > 0 && accent is ISolidColorBrush ab)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(X(0), Y(0)), true);
                for (var i = 0; i <= 60; i++) { var n = cur * i / 60; g.LineTo(new Point(X(n), Y(C(n)))); }
                g.LineTo(new Point(X(cur), Y(0)));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(ab.Color, 0.18), null, area);
        }
        void Curve(double from, double to, Pen pen)
        {
            if (to <= from) return;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(from), Y(C(from))), false);
                for (var i = 1; i <= 80; i++) { var n = from + (to - from) * i / 80; g.LineTo(new Point(X(n), Y(C(n)))); }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        Curve(0, cur, new Pen(accent, 2.5));
        Curve(cur, Math.Min(Math.Max(Projected, cur), xMax), new Pen(accent, 2, new DashStyle(new double[] { 3, 3 }, 0)));
        Curve(Math.Min(Math.Max(Projected, cur), xMax), xMax, new Pen(Res("LineBrush"), 1.5));

        // markers + callouts
        var page = Res("PageBrush");
        // labels sit above-left of the projection and below-right of "you": the curve rises to the right, so both stay clear of it
        void Marker(double n, double c, bool filled, string top, string bottom, bool above)
        {
            var p = new Point(X(n), Y(c));
            ctx.DrawLine(new Pen(Res("LineBrush"), 1, new DashStyle(new double[] { 2, 3 }, 0)), p, new Point(p.X, Y(0)));
            ctx.DrawEllipse(filled ? accent : page, new Pen(filled ? page : accent, 2.5), p, 6, 6);
            var a = Txt(top, 18, ink, display: true); var b = Txt(bottom, 11, muted);
            var tw = Math.Max(a.Width, b.Width);
            var x = Math.Clamp(above ? p.X - 10 - tw : p.X + 12, L + 4, w - R - tw);
            var y = above ? Math.Max(2, p.Y - a.Height - b.Height - 8) : Math.Min(Y(0) - a.Height - b.Height - 4, p.Y + 10);
            ctx.DrawText(a, new Point(x, y)); ctx.DrawText(b, new Point(x, y + a.Height));
        }
        if (Projected > cur + 1) Marker(Math.Min(Projected, xMax), ProjectedChance, false, (ProjectedChance * 100).ToString("0.0", ci) + "%", ProjectedLabel ?? "", true);
        Marker(cur, CurrentChance, true, (CurrentChance * 100).ToString("0.00", ci) + "%", $"you now, {Current.ToString("N0", ci)} chests", true);

        // hover crosshair
        if (_hoverX is double hx && hx >= L && hx <= w - R)
        {
            var n = (hx - L) / (w - L - R) * xMax;
            ctx.DrawLine(new Pen(Res("LineBrush"), 1), new Point(hx, T), new Point(hx, Y(0)));
            ctx.DrawEllipse(ink, null, new Point(hx, Y(C(n))), 3.5, 3.5);
            var tip = Txt($"{n.ToString("N0", ci)} chests: {(C(n) * 100).ToString("0.00", ci)}%", 12, ink);
            var tx = Math.Clamp(hx + 10, L, w - R - tip.Width - 12);
            ctx.DrawRectangle(Res("NavBrush"), new Pen(Res("LineBrush"), 1), new Rect(tx - 6, T - 30, tip.Width + 12, tip.Height + 8), 3, 3);
            ctx.DrawText(tip, new Point(tx, T - 26));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); _hoverX = e.GetPosition(this).X; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hoverX = null; InvalidateVisual(); }
}
