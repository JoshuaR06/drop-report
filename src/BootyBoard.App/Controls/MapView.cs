using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BootyBoard.App.Controls;

/// <summary>
/// Playable match map: your and your teammates' trails, others while near you, storm, chests (with a loot callout for a
/// few seconds after each search), knocks/elims. No map image (positions only). Fortnite +X is north, +Y is east.
/// Drag to pan, wheel to zoom; follows you until you drag.
/// </summary>
public sealed class MapView : Control
{
    public static readonly StyledProperty<MapData> MapProperty = AvaloniaProperty.Register<MapView, MapData>(nameof(Map));
    public static readonly StyledProperty<double> TimeProperty = AvaloniaProperty.Register<MapView, double>(nameof(Time));
    public static readonly StyledProperty<bool> FollowProperty = AvaloniaProperty.Register<MapView, bool>(nameof(Follow), true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public MapData Map { get => GetValue(MapProperty); set => SetValue(MapProperty, value); }
    public double Time { get => GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public bool Follow { get => GetValue(FollowProperty); set => SetValue(FollowProperty, value); }

    private double _cx, _cy, _scale = 1000.0 / 30000;   // ~300 m across to start
    private Point? _drag;

    static MapView() { AffectsRender<MapView>(MapProperty, TimeProperty, FollowProperty); }
    public MapView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
    private Point S(double x, double y) => new(Bounds.Width / 2 + (y - _cx) * _scale, Bounds.Height / 2 - (x - _cy) * _scale);

    private static int Idx(Track tr, double t)
    {
        int lo = 0, hi = tr.T.Length - 1;
        if (hi < 0) return -1;
        while (hi - lo > 1) { var m = (lo + hi) / 2; if (tr.T[m] < t) lo = m; else hi = m; }
        return Math.Abs(tr.T[lo] - t) < Math.Abs(tr.T[hi] - t) ? lo : hi;
    }
    private static (double x, double y)? PosAt(Track tr, double t, double maxGap)
    {
        var i = Idx(tr, t); if (i < 0 || Math.Abs(tr.T[i] - t) > maxGap) return null;
        return (tr.X[i], tr.Y[i]);
    }

    public override void Render(DrawingContext ctx)
    {
        var m = Map; double T = Time;
        ctx.FillRectangle(Res("CardBrush"), new Rect(Bounds.Size));
        if (m == null) return;
        var mine = m.Tracks.Where(t => t.Role == 0).ToList();
        if (Follow) foreach (var tr in mine) if (PosAt(tr, T, 5) is { } q) { _cx = q.y; _cy = q.x; break; }

        // grid every 100 m
        var grid = new Pen(Res("GridBrush"), 1);
        const double step = 10000;
        if (_scale * step > 12)
        {
            double x0 = _cy - Bounds.Height / 2 / _scale, x1 = _cy + Bounds.Height / 2 / _scale, y0 = _cx - Bounds.Width / 2 / _scale, y1 = _cx + Bounds.Width / 2 / _scale;
            for (var y = Math.Ceiling(y0 / step) * step; y < y1; y += step) ctx.DrawLine(grid, new Point(S(0, y).X, 0), new Point(S(0, y).X, Bounds.Height));
            for (var x = Math.Ceiling(x0 / step) * step; x < x1; x += step) ctx.DrawLine(grid, new Point(0, S(x, 0).Y), new Point(Bounds.Width, S(x, 0).Y));
        }
        // storm: current circle, next one dashed while shrinking
        var z = m.Zones.LastOrDefault(s => s.Start - 60 <= T);
        if (z != null)
        {
            var storm = Res("MapStormBrush");
            (double x, double y, double r) cur = T >= z.End || z.Lx == null ? (z.X, z.Y, z.R)
                : T < z.Start ? (z.Lx.Value, z.Ly.Value, z.Lr)
                : Lerp(z, (T - z.Start) / Math.Max(1, z.End - z.Start));
            ctx.DrawEllipse(null, new Pen(storm, 3), S(cur.x, cur.y), cur.r * _scale, cur.r * _scale);
            if (T < z.End) ctx.DrawEllipse(null, new Pen(storm, 1.5) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) }, S(z.X, z.Y), z.R * _scale, z.R * _scale);
        }
        // trails
        void Trail(Track tr, double upto, IBrush brush, double width, double alpha)
        {
            var pen = new Pen(brush, width) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };
            using var _ = ctx.PushOpacity(alpha);
            Point? last = null; double lastT = 0;
            for (var i = 0; i < tr.T.Length && tr.T[i] <= upto; i++)
            {
                var p = S(tr.X[i], tr.Y[i]);
                if (last != null && tr.T[i] - lastT <= 10) ctx.DrawLine(pen, last.Value, p);   // gap > 10 s: respawn/teleport
                last = p; lastT = tr.T[i];
            }
        }
        var meB = Res("MapMeBrush"); var mateB = Res("MapMateBrush"); var otherB = Res("MapOtherBrush");
        foreach (var tr in m.Tracks.Where(t => t.Role == 1)) Trail(tr, T, mateB, 2, 0.7);
        foreach (var tr in mine) { Trail(tr, m.End, meB, 1.5, 0.15); Trail(tr, T, meB, 3, 0.95); }
        foreach (var tr in m.Tracks.Where(t => t.Role == 2)) if (PosAt(tr, T, 2) is { } q) ctx.DrawEllipse(otherB, null, S(q.x, q.y), 4, 4);

        // chests so far; loot callout for 4 s after my searches
        var tf = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        foreach (var c in m.Chests.Where(c => c.T <= T && c.X != null))
        {
            var col = Res(c.Kind == "broken" ? "MapBrokenBrush" : "MapOpenedBrush");
            var p = S(c.Cx ?? c.X.Value, c.Cy ?? c.Y.Value);
            bool fresh = T - c.T < 4;
            if (c.Who == "me") ctx.DrawEllipse(col, null, p, fresh ? 10 : 6, fresh ? 10 : 6);
            else ctx.DrawEllipse(null, new Pen(col, 2), p, fresh ? 8 : 5, fresh ? 8 : 5);   // teammate's chest: ring
            if (fresh && c.Who == "me" && c.Loot.Count > 0)
            {
                double ly = p.Y - 8 - 20 * (c.Loot.Count - 1), lx = p.X + 16;
                foreach (var l in c.Loot)
                {
                    ctx.FillRectangle(ViewModels.RarityBrushConverter.Brush(l.Rarity ?? "Common"), new Rect(lx, ly - 9, 10, 10));
                    var ft = new FormattedText(l.Name + (l.TakenBy == "me" ? "" : $"  ({(l.TakenBy == "nobody" ? "left" : l.TakenBy + " took")})"), CultureInfo.CurrentCulture,
                                               FlowDirection.LeftToRight, tf, 13, Res(l.TakenBy == "me" ? "InkBrush" : "MutedBrush"));
                    ctx.DrawText(ft, new Point(lx + 16, ly - ft.Height + 3));
                    ly += 20;
                }
            }
        }
        // knocks / elims so far
        var kill = Res("MapKillBrush");
        foreach (var k in m.Kills.Where(k => k.T <= T))
        {
            var p = S(k.X, k.Y); double r = k.ByMe ? 8 : 4;
            var pen = new Pen(k.ByMe || k.MeDied ? kill : otherB, k.ByMe ? 3 : 1.5);
            ctx.DrawLine(pen, new Point(p.X - r, p.Y - r), new Point(p.X + r, p.Y + r));
            ctx.DrawLine(pen, new Point(p.X + r, p.Y - r), new Point(p.X - r, p.Y + r));
        }
        // me + teammates now
        foreach (var tr in m.Tracks.Where(t => t.Role == 1)) if (PosAt(tr, T, 3) is { } q) ctx.DrawEllipse(mateB, new Pen(Res("CardBrush"), 2), S(q.x, q.y), 8, 8);
        foreach (var tr in mine)
            if (PosAt(tr, T, 3) is { } q)
            {
                ctx.DrawEllipse(meB, new Pen(Res("CardBrush"), 2), S(q.x, q.y), 9, 9);
                ctx.DrawEllipse(null, new Pen(meB, 2), S(q.x, q.y), 14, 14);
            }
    }

    private static (double x, double y, double r) Lerp(Zone z, double f) =>
        (z.Lx.Value + (z.X - z.Lx.Value) * f, z.Ly.Value + (z.Y - z.Ly.Value) * f, z.Lr + (z.R - z.Lr) * f);

    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); _drag = e.GetPosition(this); Follow = false; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); _drag = null; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        _cx -= (p.X - d.X) / _scale; _cy += (p.Y - d.Y) / _scale; _drag = p;
        InvalidateVisual();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _scale *= e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
        _scale = Math.Clamp(_scale, 0.0005, 0.5);
        InvalidateVisual(); e.Handled = true;
    }

    /// <summary>Zoom out to show the whole match.</summary>
    public void Fit()
    {
        var pts = Map?.Tracks.Where(t => t.Role < 2).SelectMany(t => t.X.Zip(t.Y)).ToList();
        if (pts == null || pts.Count == 0) return;
        double minX = pts.Min(p => p.First), maxX = pts.Max(p => p.First), minY = pts.Min(p => p.Second), maxY = pts.Max(p => p.Second);
        _cx = (minY + maxY) / 2; _cy = (minX + maxX) / 2;
        _scale = Math.Min(Bounds.Width, Bounds.Height) / (Math.Max(Math.Max(maxX - minX, maxY - minY), 20000) * 1.1);
        Follow = false; InvalidateVisual();
    }
}
