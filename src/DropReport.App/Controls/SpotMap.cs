using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DropReport.App.Controls;

/// <summary>One landing spot on the map: bubble area = drops, fill strength = how well you place there (Score 0..1, 1 best).</summary>
public sealed record SpotDot(string Name, double X, double Y, int Matches, double Score, string Line);

/// <summary>The island zoomed to where you land, a bubble per landing spot. Hover a bubble for its numbers.</summary>
public sealed class SpotMap : Control
{
    public static readonly StyledProperty<Bitmap> MapImageProperty = AvaloniaProperty.Register<SpotMap, Bitmap>(nameof(MapImage));
    public static readonly StyledProperty<MapGeo> GeoProperty = AvaloniaProperty.Register<SpotMap, MapGeo>(nameof(Geo));
    public static readonly StyledProperty<IReadOnlyList<SpotDot>> SpotsProperty = AvaloniaProperty.Register<SpotMap, IReadOnlyList<SpotDot>>(nameof(Spots));
    public Bitmap MapImage { get => GetValue(MapImageProperty); set => SetValue(MapImageProperty, value); }
    public MapGeo Geo { get => GetValue(GeoProperty); set => SetValue(GeoProperty, value); }
    public IReadOnlyList<SpotDot> Spots { get => GetValue(SpotsProperty); set => SetValue(SpotsProperty, value); }

    private int _hover = -1;
    private readonly List<(Point c, double r)> _hits = new();

    static SpotMap() { AffectsRender<SpotMap>(MapImageProperty, GeoProperty, SpotsProperty); }
    public SpotMap() { ClipToBounds = true; MinHeight = 320; }

    private IBrush Res(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
    private Typeface Face(bool display) =>
        new(this.TryFindResource(display ? "DisplayFont" : "TextFont", ActualThemeVariant, out var f) && f is FontFamily ff ? ff : FontFamily.Default,
            FontStyle.Normal, display ? FontWeight.Bold : FontWeight.Normal);

    public override void Render(DrawingContext ctx)
    {
        _hits.Clear();
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(Res("CardBrush"), new Rect(Bounds.Size));
        var spots = Spots ?? Array.Empty<SpotDot>();
        var geo = Geo;
        if (geo == null || spots.Count == 0) return;

        // zoom to the spots: their bounding box in image space, padded, made to the control's aspect
        var uv = spots.Select(s => geo.ToImage(s.X, s.Y)).ToList();
        double u0 = uv.Min(p => p.u), u1 = uv.Max(p => p.u), v0 = uv.Min(p => p.v), v1 = uv.Max(p => p.v);
        double pad = 0.06, span = Math.Max(Math.Max(u1 - u0, v1 - v0) + 2 * pad, 0.25);
        double aspect = w / h;
        double sw = aspect >= 1 ? span * aspect : span, sh = aspect >= 1 ? span : span / aspect;
        double cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
        var view = new Rect(cu - sw / 2, cv - sh / 2, sw, sh);
        Point P(double u, double v) => new((u - view.X) / view.Width * w, (v - view.Y) / view.Height * h);

        if (MapImage is { } img)
        {
            var px = img.PixelSize;
            var src = new Rect(view.X * px.Width, view.Y * px.Height, view.Width * px.Width, view.Height * px.Height);
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                ctx.DrawImage(img, src, new Rect(0, 0, w, h));
            if (Res("PageBrush") is ISolidColorBrush pb) ctx.FillRectangle(new SolidColorBrush(pb.Color, 0.45), new Rect(Bounds.Size));
        }

        var accent = Res("AccentBrush") as ISolidColorBrush;
        var ink = Res("InkBrush"); var page = Res("PageBrush");
        var ci = CultureInfo.CurrentCulture;
        var maxN = spots.Max(s => s.Matches);
        // biggest first so small bubbles stay on top
        foreach (var i in Enumerable.Range(0, spots.Count).OrderByDescending(i => spots[i].Matches))
        {
            var s = spots[i]; var c = P(uv[i].u, uv[i].v);
            var r = 7 + 22 * Math.Sqrt((double)s.Matches / maxN);
            IBrush fill = accent == null ? Brushes.Orange : new SolidColorBrush(accent.Color, 0.25 + 0.7 * s.Score);
            ctx.DrawEllipse(fill, new Pen(i == _hover ? ink : page, i == _hover ? 2.5 : 2), c, r, r);
            var n = new FormattedText(s.Matches.ToString(ci), ci, FlowDirection.LeftToRight, Face(true), 13, page);
            if (r >= 11) ctx.DrawText(n, new Point(c.X - n.Width / 2, c.Y - n.Height / 2));
            var label = new FormattedText(s.Name.ToUpperInvariant(), ci, FlowDirection.LeftToRight, Face(true), 13, ink);
            ctx.DrawText(label, new Point(c.X + r + 6, c.Y - label.Height / 2));
            _hits.Add((c, r));   // draw order; _order maps it back to the spot index
        }
        _order = Enumerable.Range(0, spots.Count).OrderByDescending(i => spots[i].Matches).ToList();

        if (_hover >= 0 && _hover < spots.Count)
        {
            var s = spots[_hover]; var c = P(uv[_hover].u, uv[_hover].v);
            var t = new FormattedText(s.Line, ci, FlowDirection.LeftToRight, Face(false), 12, ink);
            var x = Math.Clamp(c.X - t.Width / 2, 6, w - t.Width - 12); var y = Math.Max(6, c.Y - 36 - t.Height);
            ctx.DrawRectangle(Res("NavBrush"), new Pen(Res("LineBrush"), 1), new Rect(x - 6, y - 4, t.Width + 12, t.Height + 8), 3, 3);
            ctx.DrawText(t, new Point(x, y));
        }
    }

    private List<int> _order = new();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var hit = -1;
        for (var k = _order.Count - 1; k >= 0; k--)   // topmost (smallest, drawn last) first
        {
            var (c, r) = _hits[k];
            if (Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2) <= r * r) { hit = _order[k]; break; }
        }
        if (hit != _hover) { _hover = hit; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = -1; InvalidateVisual(); }
}
