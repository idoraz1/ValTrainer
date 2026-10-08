using System.Globalization;
using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>Draws the imported Valorant crosshair pixel-for-pixel (sizes in screen pixels, like the game), and the
/// sniper scopes (procedural, no game assets).</summary>
public partial class CrosshairView : Control
{
    public CrosshairSettings? Profile;
    public enum Mode { Primary, Ads, Sniper, Hidden }
    /// <summary>Which scope <see cref="Mode.Sniper"/> draws: the Operator / Marshal / Outlaw eyepiece or Chamber's
    /// Tour de Force hologram octagon.</summary>
    public enum SniperScope { Operator, Marshal, Outlaw, TourDeForce }
    public Mode Showing = Mode.Primary;
    /// <summary>0..1 movement inaccuracy (spreads lines with "movement error").</summary>
    public float MoveError;
    /// <summary>Current firing error in pixels beyond the weapon's first-shot error (spreads lines with "firing error").</summary>
    public float FiringErrorPx;
    /// <summary>The weapon's minimum (first-shot) error in pixels. Without "Override Firing Error Offset With Crosshair
    /// Offset" (code "m") VALORANT rests the firing-error lines this much further out; 0 = the offset alone.</summary>
    public float MinErrorPx;
    /// <summary>Sniper scope overlay progress, 0 (not up) → 1 (fully up): it fades in and grows from 0.8 to its full
    /// radius (ads-model.md §4.5). The game sets it from the raise; 1 = instant scope.</summary>
    public float ScopeBlend = 1f;
    public SniperScope ScopeStyle = SniperScope.Operator;
    /// <summary>Preview mode (menu): centre on this control's own rect instead of the screen. Sizes stay in real
    /// screen pixels (scaled by the window height only when the profile uses "scale to resolution"). A preview in
    /// <see cref="Mode.Sniper"/> draws the scope inside its own rect.</summary>
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
        float k = Profile.ScaleToResolution ? size.Y / 1080f : 1f;
        if (Showing == Mode.Sniper)
        {
            var area = LocalCenter ? new Rect2(Vector2.Zero, Size) : new Rect2(Vector2.Zero, size);
            DrawScope(this, area, ScopeStyle, ScopeBlend, Profile.SniperDotFor());
            return;
        }
        var style = Profile.StyleFor(Showing);
        var sec = Profile.SectionFor(Showing);
        DrawStyle(this, style, centre.Floor(), k, MoveError, FiringErrorPx,
            restErrorPx: sec.OverrideFiringOffset ? 0f : MinErrorPx, fade: Profile.Extras.Primary.Fade);
    }

    /// <summary>Draws one crosshair style centred on <paramref name="centre"/> of any canvas (HUD overlays, previews).
    /// <paramref name="k"/> scales every size (1 = real screen pixels; larger = magnified preview).
    /// <para>Like VALORANT at 1080p: each line is a rectangle <c>thickness</c> px across, centred on the screen centre
    /// (a pixel corner), from <c>offset</c> to <c>offset + length</c> px out; the dot is a <c>size</c> px square. Very
    /// short, thick inner lines therefore close into a ring or a box, and offset 0 joins the arms into a closed plus.
    /// Each part (dot, inner lines, outer lines, and each one's outline) is filled as the union of its rectangles, so
    /// arms that overlap at offset 0 don't blend twice at reduced opacity.</para>
    /// <para><paramref name="restErrorPx"/> moves firing-error lines out at rest (the weapon's minimum error when the
    /// profile doesn't override it); <paramref name="fade"/> ("Fade Crosshair With Firing Error") fades firing-error
    /// lines as they spread.</para></summary>
    public static void DrawStyle(CanvasItem ci, CrosshairStyle style, Vector2 centre, float k = 1f, float moveError = 0f, float firingErrorPx = 0f,
        float restErrorPx = 0f, bool fade = false)
    {
        if (style.Hide) return;
        int cx = (int)centre.X, cy = (int)centre.Y;
        var parts = PartRects(style, cx, cy, k, moveError, firingErrorPx, restErrorPx, fade);
        if (style.HasOutline && style.OutlineThickness > 0 && style.OutlineOpacity > 0)
        {
            float o = Mathf.Round(style.OutlineThickness * k);
            foreach (var (rects, op) in parts)
                Fill(ci, rects.Select(r => r.Grow(o)), new Color(style.OutlineColor, style.OutlineOpacity * Mathf.Min(1f, op * 1.5f)));
        }
        foreach (var (rects, op) in parts) Fill(ci, rects, new Color(style.Color, op));
    }

    /// <summary>The fill rectangles of each visible part (centre dot, inner lines, outer lines) with its opacity.</summary>
    public static List<(List<Rect2> Rects, float Opacity)> PartRects(CrosshairStyle s, int cx, int cy, float k = 1f, float moveError = 0f, float firingErrorPx = 0f,
        float restErrorPx = 0f, bool fade = false)
    {
        var parts = new List<(List<Rect2>, float)>();
        if (s.CenterDot && s.CenterDotOpacity > 0)
        {
            float d = Mathf.Max(1, Mathf.Round(s.CenterDotSize * k));
            parts.Add((new List<Rect2> { new(cx - d / 2, cy - d / 2, d, d) }, s.CenterDotOpacity));
        }
        foreach (var l in new[] { s.Inner, s.Outer })
        {
            if (Lines(l, cx, cy, k, moveError, firingErrorPx, restErrorPx) is not { } r) continue;
            float op = l.Opacity;
            // Fade With Firing Error (exact curve unpublished): firing-error lines lose up to 65 % opacity by ~30 px of spread.
            if (fade && l.ShowShootingError) op *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp(firingErrorPx * l.FiringErrorScale / 30f, 0f, 1f));
            parts.Add((r, op));
        }
        return parts;
    }

    static List<Rect2>? Lines(CrosshairLines l, int cx, int cy, float k, float moveError, float firingErrorPx, float restErrorPx)
    {
        if (!l.Show || l.Opacity <= 0 || l.Thickness <= 0) return null;
        float len = Mathf.Round(l.Length * k);
        // Unlinked lengths: the vertical arms have their own length, so "0 horizontal, 4 vertical" is a valid "|" crosshair.
        float lenV = Mathf.Round((l.AllowVertScaling ? l.LengthVertical : l.Length) * k);
        if (len <= 0 && lenV <= 0) return null;
        float t = Mathf.Max(1, Mathf.Round(l.Thickness * k));
        float spread = (l.ShowMovementError ? moveError * 14f * l.MovementErrorScale : 0f)
                     + (l.ShowShootingError ? (firingErrorPx + restErrorPx) * l.FiringErrorScale : 0f);
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

    // =====================================================================================
    // Sniper scopes (ads-model.md §4.5; measured from VALORANT footage, drawn procedurally)
    // =====================================================================================

    /// <summary>The scope centre dot (VALORANT's "sniper centre dot"; radius 2.5 px per size step, unverified in game).</summary>
    public static void DrawSniperDot(CanvasItem ci, Vector2 c, SniperDotStyle dot, float alpha = 1f)
    {
        if (!dot.Show || dot.Opacity <= 0 || alpha <= 0) return;
        float r = Mathf.Max(1.5f, dot.Size * 2.5f);
        ci.DrawCircle(c, r, new Color(dot.Color, dot.Opacity * alpha));
    }

    /// <summary>Draws a sniper scope over <paramref name="area"/> (the screen, or a preview box) at overlay progress
    /// <paramref name="t"/> (0..1), with the profile's centre dot on top.</summary>
    public static void DrawScope(CanvasItem ci, Rect2 area, SniperScope kind, float t, SniperDotStyle dot)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        if (t <= 0f) return;
        var c = (area.Position + area.Size / 2).Floor();
        if (kind == SniperScope.TourDeForce) DrawTourDeForce(ci, area, c, t);
        else DrawEyepiece(ci, area, c, t, kind);
        DrawSniperDot(ci, c, dot, t);
    }

    /// <summary>Operator / Marshal / Outlaw: a round glass of diameter 1.12 H (clipped top and bottom) in a near-black,
    /// shaded housing; a thin full cross with thick outer posts and stadia ticks below the centre.</summary>
    static void DrawEyepiece(CanvasItem ci, Rect2 area, Vector2 c, float t, SniperScope kind)
    {
        float h = area.Size.Y;
        float full = 0.56f * h;
        float R = full * Mathf.Lerp(0.8f, 1f, t);
        float far = (area.Size / 2).Length() + 4f;
        const int seg = 128;

        // Housing to the edges, then the shaded bezel (#1E1F21 → base) and a highlight ring on the glass edge.
        var housing = new Color(0x0B / 255f, 0x0B / 255f, 0x0C / 255f, t);
        Annulus(ci, c, R, far, housing, seg);
        float bezel = 0.035f * h;
        var bezelCol = new Color(0x1E / 255f, 0x1F / 255f, 0x21 / 255f);
        const int steps = 8;
        for (int i = 0; i < steps; i++)
        {
            float a = i / (float)steps, b = (i + 1) / (float)steps;
            var col = bezelCol.Lerp(housing, a * a);
            Annulus(ci, c, R + bezel * a, R + bezel * b + 0.5f, new Color(col, t), seg);
        }
        float hi = Mathf.Max(1.5f, 0.008f * h);
        Annulus(ci, c, R, R + hi, new Color(0x3A / 255f, 0x3B / 255f, 0x3E / 255f, t), seg);
        // Faint darkening just inside the glass edge (the lens rim).
        float rim = 0.06f * h;
        for (int i = 0; i < steps; i++)
        {
            float a = i / (float)steps;
            Annulus(ci, c, R - rim * (a + 1f / steps), R - rim * a, new Color(0, 0, 0, 0.35f * (1f - a) * (1f - a) * t), seg);
        }

        // Reticle.
        var ink = new Color(0, 0, 0, 0.85f * t);
        float thin = Mathf.Max(1f, Mathf.Round(0.0011f * h));
        float post = Mathf.Max(3f, Mathf.Round(0.0045f * h));
        float half = Mathf.Floor(thin / 2), halfP = Mathf.Floor(post / 2);
        float yTop = Mathf.Max(area.Position.Y, c.Y - R), yBot = Mathf.Min(area.End.Y, c.Y + R);
        ci.DrawRect(new Rect2(c.X - R, c.Y - half, 2 * R, thin), ink);
        ci.DrawRect(new Rect2(c.X - half, yTop, thin, c.Y - half - yTop), ink);
        ci.DrawRect(new Rect2(c.X - half, c.Y - half + thin, thin, yBot - (c.Y - half + thin)), ink);
        float p0 = 0.70f * R;
        ci.DrawRect(new Rect2(c.X - R, c.Y - halfP, R - p0, post), ink);
        ci.DrawRect(new Rect2(c.X + p0, c.Y - halfP, R - p0, post), ink);
        if (c.Y + p0 < yBot) ci.DrawRect(new Rect2(c.X - halfP, c.Y + p0, post, yBot - c.Y - p0), ink);
        // Stadia ticks below the centre (the Outlaw's are smaller).
        float tk = kind == SniperScope.Outlaw ? 0.75f : 1f;
        foreach (var (y, w) in new[] { (0.33f, 0.11f), (0.48f, 0.185f), (0.63f, 0.235f) })
            ci.DrawRect(new Rect2(c.X - w * R * tk, Mathf.Round(c.Y + y * R) - half, 2 * w * R * tk, thin), ink);
    }

    /// <summary>Chamber's Tour de Force: no mask, a glowing orange octagon of 0.74 W × 0.80 H, the outside darkened a little.</summary>
    static void DrawTourDeForce(CanvasItem ci, Rect2 area, Vector2 c, float t)
    {
        float W = area.Size.X, H = area.Size.Y;
        float s = Mathf.Lerp(0.9f, 1f, t);
        float hw = 0.37f * W * s, hh = 0.40f * H * s, ch = 0.13f * H * s;
        float x0 = c.X - hw, x1 = c.X + hw, y0 = c.Y - hh, y1 = c.Y + hh;
        var oct = new[]
        {
            new Vector2(x0 + ch, y0), new Vector2(x1 - ch, y0), new Vector2(x1, y0 + ch), new Vector2(x1, y1 - ch),
            new Vector2(x1 - ch, y1), new Vector2(x0 + ch, y1), new Vector2(x0, y1 - ch), new Vector2(x0, y0 + ch),
        };
        // Darken outside the octagon: four bands and the four corner triangles.
        var dim = new Color(0, 0, 0, 0.18f * t);
        var a = area;
        ci.DrawRect(new Rect2(a.Position.X, a.Position.Y, W, y0 - a.Position.Y), dim);
        ci.DrawRect(new Rect2(a.Position.X, y1, W, a.End.Y - y1), dim);
        ci.DrawRect(new Rect2(a.Position.X, y0, x0 - a.Position.X, y1 - y0), dim);
        ci.DrawRect(new Rect2(x1, y0, a.End.X - x1, y1 - y0), dim);
        ci.DrawColoredPolygon(new[] { new Vector2(x0, y0), oct[0], oct[7] }, dim);
        ci.DrawColoredPolygon(new[] { new Vector2(x1, y0), oct[2], oct[1] }, dim);
        ci.DrawColoredPolygon(new[] { new Vector2(x1, y1), oct[4], oct[3] }, dim);
        ci.DrawColoredPolygon(new[] { new Vector2(x0, y1), oct[6], oct[5] }, dim);

        var loop = oct.Append(oct[0]).ToArray();
        float stroke = Mathf.Max(3f, 0.006f * H);
        ci.DrawPolyline(loop, new Color(1f, 0x6A / 255f, 0f, 0.12f * t), stroke * 4f, true);
        ci.DrawPolyline(loop, new Color(1f, 0x6A / 255f, 0f, 0.35f * t), stroke * 2f, true);
        ci.DrawPolyline(loop, new Color(1f, 0xC2 / 255f, 0x1A / 255f, t), stroke, true);

        // Thin double lines inside the top and bottom edges, and small 3×4 dot grids at the four mid-points.
        var accent = new Color(1f, 0x5A / 255f, 0x1F / 255f, 0.6f * t);
        float inset = 0.03f * H, gap = Mathf.Max(3f, 0.006f * H), lw = Mathf.Max(1f, 0.0012f * H);
        foreach (float yy in new[] { y0 + inset, y0 + inset + gap, y1 - inset, y1 - inset - gap })
            ci.DrawLine(new Vector2(x0 + ch + inset, yy), new Vector2(x1 - ch - inset, yy), accent, lw);
        float dr = Mathf.Max(1f, 0.0016f * H), dg = Mathf.Max(4f, 0.008f * H);
        void Grid(Vector2 at, bool tall)
        {
            int cols = tall ? 3 : 4, rows = tall ? 4 : 3;
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    ci.DrawCircle(at + new Vector2((i - (cols - 1) / 2f) * dg, (j - (rows - 1) / 2f) * dg), dr, accent);
        }
        float gi = 0.045f * H;
        Grid(new Vector2(x0 + gi, c.Y), true);
        Grid(new Vector2(x1 - gi, c.Y), true);
        Grid(new Vector2(c.X, y0 + gi + gap * 2), false);
        Grid(new Vector2(c.X, y1 - gi - gap * 2), false);
    }

    /// <summary>A filled ring between radii <paramref name="r0"/> and <paramref name="r1"/> (quads; no holes needed).</summary>
    static void Annulus(CanvasItem ci, Vector2 c, float r0, float r1, Color col, int seg)
    {
        if (r1 <= r0 || col.A <= 0) return;
        r0 = Mathf.Max(0, r0);
        var cols = new[] { col, col, col, col };
        for (int i = 0; i < seg; i++)
        {
            var d0 = Vector2.FromAngle(i / (float)seg * Mathf.Tau);
            var d1 = Vector2.FromAngle((i + 1) / (float)seg * Mathf.Tau);
            ci.DrawPrimitive(new[] { c + d0 * r0, c + d1 * r0, c + d1 * r1, c + d0 * r1 }, cols, null);
        }
    }
}

/// <summary>
/// The player's crosshair at real pixel size with PRIMARY / ADS / SNIPER tabs (menu, Settings → Crosshair), so a split
/// crosshair can be checked: each tab shows what VALORANT draws there (<see cref="CrosshairSettings.StyleFor"/>,
/// <see cref="CrosshairSettings.SniperDotFor"/>) with a one-line caption that says whether the tab is the player's own.
/// </summary>
public partial class CrosshairPreview : VBoxContainer
{
    public float K = 1f;
    public Func<CrosshairSettings> Source = () => CrosshairCode.Effective;
    /// <summary>Backdrop behind the crosshair (null = a plain range wall).</summary>
    public Action<DrawBox>? Backdrop;
    public float BoxHeight = 130;
    /// <summary>Hide the tabs unless the profile has its own ADS crosshair or sniper dot (tight layouts).</summary>
    public bool TabsOnlyWhenSplit;
    public bool Caption = true;

    /// <summary>The tab last picked (shared by every preview). Dev-only "--xhair-tab primary|ads|sniper" picks the first one.</summary>
    static CrosshairView.Mode tab = Core.CmdLine.DevAfter("--xhair-tab")?.ToLowerInvariant() switch
    {
        "ads" => CrosshairView.Mode.Ads,
        "sniper" => CrosshairView.Mode.Sniper,
        _ => CrosshairView.Mode.Primary,
    };
    /// <summary>Dev-only "--xhair-scope operator|marshal|outlaw|tourdeforce": the scope the SNIPER tab previews.</summary>
    static readonly CrosshairView.SniperScope previewScope =
        Enum.TryParse<CrosshairView.SniperScope>(Core.CmdLine.DevAfter("--xhair-scope"), true, out var sc) ? sc : CrosshairView.SniperScope.Operator;
    readonly List<(VButton B, CrosshairView.Mode M)> chips = new();
    HBoxContainer row = null!;
    CrosshairView view = null!;
    Label caption = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeConstantOverride("separation", (int)(6 * K));
        row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)(6 * K));
        foreach (var (label, m) in new[] { ("PRIMARY", CrosshairView.Mode.Primary), ("ADS", CrosshairView.Mode.Ads), ("SNIPER", CrosshairView.Mode.Sniper) })
        {
            var b = new VButton { Kind = VButton.Look.Select, K = K, Label = label, FontPx = 14, CustomMinimumSize = new Vector2(0, 30 * K),
                SizeFlagsHorizontal = SizeFlags.ExpandFill };
            b.Pressed += () => { tab = m; Refresh(); };
            chips.Add((b, m));
            row.AddChild(b);
        }
        AddChild(row);
        var box = new DrawBox
        {
            SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, BoxHeight * K), ClipContents = true,
            OnDraw = d => (Backdrop ?? Wall)(d),
        };
        AddChild(box);
        view = new CrosshairView { LocalCenter = true, ScopeStyle = previewScope };
        box.AddChild(view);
        view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        caption = new Label
        {
            LabelSettings = UiTheme.Label(UiTheme.Body, 13, K, UiTheme.Dim), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = Caption, MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(caption);
        Refresh();
        // Dev-only "--xhair-scroll": scroll the enclosing list to its end (screenshots of Settings → Crosshair).
        if (Core.CmdLine.Dev && Core.CmdLine.Has("--xhair-scroll"))
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                for (Node? n = GetParent(); n != null; n = n.GetParent())
                    if (n is ScrollContainer sc) { sc.ScrollVertical = (int)sc.GetVScrollBar().MaxValue; break; }
            };
    }

    public override void _Process(double delta) => Refresh();

    void Refresh()
    {
        var p = Source();
        view.Profile = p;
        row.Visible = !TabsOnlyWhenSplit || p.AdsIsOwn || p.SniperIsOwn;
        var m = row.Visible ? tab : CrosshairView.Mode.Primary;
        view.Showing = m;
        foreach (var (b, cm) in chips) b.SetSelected(cm == m);
        if (Caption)
        {
            string text = Describe(p, m);
            if (caption.Text != text) caption.Text = text;
        }
    }

    /// <summary>What VALORANT draws in this tab, in one line.</summary>
    public static string Describe(CrosshairSettings p, CrosshairView.Mode m)
    {
        switch (m)
        {
            case CrosshairView.Mode.Ads:
                if (p.AdsIsOwn) return $"Aiming down sights (Vandal, Phantom, Headhunter): your own ADS crosshair, {Colour(p.Ads.Color)}.";
                return !p.UseAdvancedOptions && !p.UsePrimaryForAds
                    ? "Aiming down sights: your primary. Use Advanced Options is off, so VALORANT ignores the ADS tab."
                    : "Aiming down sights: your primary (Copy Primary Crosshair is on).";
            case CrosshairView.Mode.Sniper:
                var d = p.SniperDotFor();
                if (!p.SniperIsOwn) return "Scoped (Operator, Tour de Force): VALORANT's default red centre dot (Use Advanced Options is off).";
                return d.Show
                    ? $"Scoped (Operator, Tour de Force): your centre dot, {Colour(d.Color)}, size {d.Size.ToString("0.###", CultureInfo.InvariantCulture)}, opacity {d.Opacity.ToString("0.##", CultureInfo.InvariantCulture)}."
                    : "Scoped (Operator, Tour de Force): no centre dot (turned off in your Sniper tab).";
            default:
                string split = p.AdsIsOwn ? " Your ADS crosshair is different: see ADS." : p.SniperIsOwn ? " Your sniper dot is your own: see SNIPER." : "";
                return $"Hip fire with every gun: {Colour(p.Primary.Color)}.{split}";
        }
    }

    static string Colour(Color c) => CrosshairCode.ColorName(c).ToLowerInvariant() is var n && n.StartsWith('#') ? n.ToUpperInvariant() : n;

    static void Wall(DrawBox d)
    {
        var s = d.Size;
        Gfx.VGradient(d, new Rect2(0, 0, s.X, s.Y * 0.72f), Color.Color8(96, 112, 126), Color.Color8(74, 88, 100));
        Gfx.VGradient(d, new Rect2(0, s.Y * 0.72f, s.X, s.Y * 0.28f), Color.Color8(58, 66, 76), Color.Color8(40, 46, 54));
    }
}
