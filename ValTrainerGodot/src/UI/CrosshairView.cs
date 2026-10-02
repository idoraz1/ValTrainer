using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>Draws the imported Valorant crosshair pixel-for-pixel (sizes in screen pixels, like the game).</summary>
public partial class CrosshairView : Control
{
    public CrosshairSettings? Profile;
    public enum Mode { Primary, Ads, Sniper, Hidden }
    public Mode Showing = Mode.Primary;
    /// <summary>0..1 movement inaccuracy (spreads lines with "movement error").</summary>
    public float MoveError;
    /// <summary>Current firing error in degrees beyond first-shot error (spreads lines with "firing error").</summary>
    public float FiringErrorPx;
    /// <summary>Preview mode (menu): centre on this control's own rect instead of the screen. Sizes stay in real
    /// screen pixels (scaled by the window height only when the profile uses "scale to resolution").</summary>
    public bool LocalCenter;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (!LocalCenter) SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Profile == null || Showing == Mode.Hidden) return;
        var size = GetViewportRect().Size;
        var centre = LocalCenter ? Size / 2 : size / 2;
        int cx = (int)centre.X, cy = (int)centre.Y;
        float k = Profile.ScaleToResolution ? size.Y / 1080f : 1f;
        if (Showing == Mode.Sniper) { if (!LocalCenter) DrawScope(size); SniperDot(cx, cy); return; }
        var style = Showing == Mode.Ads && !Profile.UsePrimaryForAds ? Profile.Ads : Profile.Primary;
        DrawStyle(this, style, new Vector2(cx, cy), k, MoveError, FiringErrorPx);
    }

    /// <summary>Draws one crosshair style centred on <paramref name="centre"/> of any canvas (HUD overlays, previews).
    /// <paramref name="k"/> scales every size (1 = real screen pixels; larger = magnified preview).
    /// <para>Like VALORANT at 1080p: each line is a rectangle <c>thickness</c> px across, centred on the screen centre
    /// (a pixel corner), from <c>offset</c> to <c>offset + length</c> px out; the dot is a <c>size</c> px square. Very
    /// short, thick inner lines therefore close into a ring or a box, and offset 0 joins the arms into a closed plus.
    /// Each part (dot, inner lines, outer lines, and each one's outline) is filled as the union of its rectangles, so
    /// arms that overlap at offset 0 don't blend twice at reduced opacity.</para></summary>
    public static void DrawStyle(CanvasItem ci, CrosshairStyle style, Vector2 centre, float k = 1f, float moveError = 0f, float firingErrorPx = 0f)
    {
        if (style.Hide) return;
        int cx = (int)centre.X, cy = (int)centre.Y;
        var parts = PartRects(style, cx, cy, k, moveError, firingErrorPx);
        if (style.HasOutline && style.OutlineThickness > 0 && style.OutlineOpacity > 0)
        {
            float o = Mathf.Round(style.OutlineThickness * k);
            foreach (var (rects, op) in parts)
                Fill(ci, rects.Select(r => r.Grow(o)), new Color(style.OutlineColor, style.OutlineOpacity * Mathf.Min(1f, op * 1.5f)));
        }
        foreach (var (rects, op) in parts) Fill(ci, rects, new Color(style.Color, op));
    }

    /// <summary>The fill rectangles of each visible part (centre dot, inner lines, outer lines) with its opacity.</summary>
    public static List<(List<Rect2> Rects, float Opacity)> PartRects(CrosshairStyle s, int cx, int cy, float k = 1f, float moveError = 0f, float firingErrorPx = 0f)
    {
        var parts = new List<(List<Rect2>, float)>();
        if (s.CenterDot && s.CenterDotOpacity > 0)
        {
            float d = Mathf.Max(1, Mathf.Round(s.CenterDotSize * k));
            parts.Add((new List<Rect2> { new(cx - d / 2, cy - d / 2, d, d) }, s.CenterDotOpacity));
        }
        foreach (var l in new[] { s.Inner, s.Outer })
            if (Lines(l, cx, cy, k, moveError, firingErrorPx) is { } r) parts.Add((r, l.Opacity));
        return parts;
    }

    static List<Rect2>? Lines(CrosshairLines l, int cx, int cy, float k, float moveError, float firingErrorPx)
    {
        if (!l.Show || l.Opacity <= 0 || l.Thickness <= 0 || l.Length <= 0) return null;
        float t = Mathf.Max(1, Mathf.Round(l.Thickness * k));
        float len = Mathf.Round(l.Length * k);
        float lenV = Mathf.Round((l.AllowVertScaling ? l.LengthVertical : l.Length) * k);
        float spread = (l.ShowMovementError ? moveError * 14f * l.MovementErrorScale : 0f)
                     + (l.ShowShootingError ? firingErrorPx * l.FiringErrorScale : 0f);
        float off = Mathf.Round((l.Offset + spread) * k);
        float half = Mathf.Floor(t / 2);
        var r = new List<Rect2>(4);
        if (len > 0)
        {
            r.Add(new Rect2(cx + off, cy - half, len, t));
            r.Add(new Rect2(cx - off - len, cy - half, len, t));
        }
        if (lenV > 0)
        {
            r.Add(new Rect2(cx - half, cy - off - lenV, t, lenV));
            r.Add(new Rect2(cx - half, cy + off, t, lenV));
        }
        return r.Count > 0 ? r : null;
    }

    static void Fill(CanvasItem ci, IEnumerable<Rect2> rects, Color c)
    {
        foreach (var r in Union(rects)) ci.DrawRect(r, c);
    }

    /// <summary>Non-overlapping rectangles covering the union of <paramref name="rects"/> (horizontal bands).</summary>
    public static List<Rect2> Union(IEnumerable<Rect2> rects)
    {
        var list = rects.Where(r => r.Size.X > 0 && r.Size.Y > 0).ToList();
        if (list.Count <= 1) return list;
        var ys = list.SelectMany(r => new[] { r.Position.Y, r.End.Y }).Distinct().OrderBy(y => y).ToList();
        var bands = new List<Rect2>();
        for (int i = 0; i + 1 < ys.Count; i++)
        {
            float y0 = ys[i], y1 = ys[i + 1];
            float a = 0, b = 0;
            bool open = false;
            foreach (var r in list.Where(r => r.Position.Y <= y0 && r.End.Y >= y1).OrderBy(r => r.Position.X))
            {
                if (open && r.Position.X <= b) { b = Mathf.Max(b, r.End.X); continue; }
                if (open) bands.Add(new Rect2(a, y0, b - a, y1 - y0));
                a = r.Position.X; b = r.End.X; open = true;
            }
            if (open) bands.Add(new Rect2(a, y0, b - a, y1 - y0));
        }
        return bands;
    }

    /// <summary>The style's fill pixels at real size (no outline) on a (2·<paramref name="r"/>)² grid centred on the
    /// screen centre, as rows of '#' / '.' (pixel-centre coverage, like the rasterizer). For the self-test.</summary>
    public static string[] Raster(CrosshairStyle s, int r = 8)
    {
        var rows = new char[2 * r][];
        for (int y = 0; y < 2 * r; y++) rows[y] = Enumerable.Repeat('.', 2 * r).ToArray();
        foreach (var (rects, _) in PartRects(s, r, r))
            foreach (var q in rects)
                for (int y = 0; y < 2 * r; y++)
                    for (int x = 0; x < 2 * r; x++)
                        if (x + 0.5f >= q.Position.X && x + 0.5f < q.End.X && y + 0.5f >= q.Position.Y && y + 0.5f < q.End.Y) rows[y][x] = '#';
        return rows.Select(c => new string(c)).ToArray();
    }

    void SniperDot(int cx, int cy)
    {
        if (!Profile!.SniperDot) return;
        float r = Mathf.Max(1.5f, Profile.SniperDotSize * 2.5f);
        DrawCircle(new Vector2(cx, cy), r, new Color(Profile.SniperDotColor, Profile.SniperDotOpacity));
    }

    void DrawScope(Vector2 size)
    {
        var c = size / 2;
        float r = size.Y * 0.47f;
        // Black mask outside the scope circle.
        var pts = new List<Vector2>();
        for (int i = 0; i <= 96; i++) pts.Add(c + Vector2.FromAngle(i / 96f * Mathf.Tau) * r);
        DrawRect(new Rect2(0, 0, c.X - r, size.Y), Colors.Black);
        DrawRect(new Rect2(c.X + r, 0, size.X - c.X - r, size.Y), Colors.Black);
        for (int i = 0; i < 96; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            float topA = a.Y < c.Y ? 0 : size.Y; // fill from the ring to the screen edge
            DrawColoredPolygon(new[] { a, b, new Vector2(b.X, topA), new Vector2(a.X, topA) }, Colors.Black);
        }
        DrawArc(c, r, 0, Mathf.Tau, 96, new Color(0.08f, 0.08f, 0.08f), 4f);
        var line = new Color(0, 0, 0, 0.8f);
        DrawLine(new Vector2(c.X - r, c.Y), new Vector2(c.X - 18, c.Y), line, 1.5f);
        DrawLine(new Vector2(c.X + 18, c.Y), new Vector2(c.X + r, c.Y), line, 1.5f);
        DrawLine(new Vector2(c.X, c.Y + 18), new Vector2(c.X, c.Y + r), line, 1.5f);
    }
}
