using Godot;
using ValTrainer.Audio;

namespace ValTrainer.UI;

/// <summary>Valorant brand colors (#FF4655 / #0F1923 / #ECE8E1) and fonts (Windows' Bahnschrift ≈ DIN).</summary>
public static class UiTheme
{
    public static readonly Color Bg = Color.Color8(15, 25, 35);
    public static readonly Color Panel = Color.Color8(24, 36, 48);
    public static readonly Color Panel2 = Color.Color8(34, 48, 62);
    public static readonly Color Border = Color.Color8(62, 78, 94);
    public static readonly Color Accent = Color.Color8(255, 70, 85);
    public static readonly Color AccentHover = Color.Color8(255, 105, 118);
    public static readonly Color AccentDark = Color.Color8(190, 44, 58);
    public static readonly Color Text = Color.Color8(236, 232, 225);
    public static readonly Color Dim = Color.Color8(139, 151, 143);
    public static readonly Color Faint = Color.Color8(92, 104, 112);
    public static readonly Color Good = Color.Color8(80, 220, 160);
    public static readonly Color Warn = Color.Color8(250, 196, 70);
    /// <summary>VALORANT's ally/team teal (top bar score box).</summary>
    public static readonly Color Teal = Color.Color8(56, 196, 164);
    /// <summary>Translucent navy used for HUD plates.</summary>
    public static readonly Color Plate = new(15 / 255f, 25 / 255f, 35 / 255f, 0.72f);

    public static readonly Color[] EnemyColors =
    {
        Color.Color8(250, 50, 50), Color.Color8(250, 235, 40), Color.Color8(235, 225, 60), Color.Color8(190, 70, 255),
    };
    public static readonly string[] EnemyColorNames = { "Red", "Yellow (Deuteranopia)", "Yellow (Protanopia)", "Purple (Tritanopia)" };

    public static Font Body = null!;     // Segoe UI semibold — has every glyph (→ — …)
    public static Font Hud = null!;      // Bahnschrift — DIN-like HUD face
    public static Font Display = null!;  // Bahnschrift SemiBold Condensed — big numbers / titles
    /// <summary>Letter-spaced variants for small uppercase labels (VALORANT's tracked-out captions).</summary>
    public static Font HudWide = null!, DisplayWide = null!;

    public static void Init()
    {
        Body = Sys(new[] { "Segoe UI" }, 600);
        Hud = Sys(new[] { "Bahnschrift", "Segoe UI" }, 400);
        Display = Sys(new[] { "Bahnschrift SemiBold Condensed", "Bahnschrift", "Segoe UI" }, 700);
        HudWide = new FontVariation { BaseFont = Sys(new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI" }, 600), SpacingGlyph = 2 };
        DisplayWide = new FontVariation { BaseFont = Display, SpacingGlyph = 2 };
    }

    static Font Sys(string[] names, int weight) => new SystemFont
    {
        FontNames = names,
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
        GenerateMipmaps = true,
    };

    /// <summary>UI scale relative to 1080p.</summary>
    public static float S(Vector2 size) => size.Y / 1080f;

    /// <summary>Menu scale: 1080p-relative but never wider than a 1600-unit design (keeps 4:3 / 5:4 windows from overflowing).</summary>
    public static float Fit(Vector2 size) => Mathf.Min(size.Y / 1080f, size.X / 1600f);

    /// <summary>Scaled font size with a readable floor.</summary>
    public static int Fs(float px, float k) => Math.Max(10, (int)MathF.Round(px * k));

    // ---------------- rank tier colors ----------------

    static readonly Color[] TierCols =
    {
        Color.Color8(160, 138, 116), // Rookie   — grey-brown
        Color.Color8(206, 212, 220), // Regular  — silver (gold trim)
        Color.Color8(46, 200, 186),  // Veteran  — teal diamond
        Color.Color8(70, 222, 140),  // Elite    — green (red core)
        Color.Color8(255, 206, 84),  // Pro      — gold
    };
    public static readonly Color NoTier = Color.Color8(100, 112, 122);

    public static Color TierColor(int tier) => tier < 0 ? NoTier : TierCols[Math.Min(tier, TierCols.Length - 1)];

    // ---------------- sounds ----------------

    public static void HoverSound() => Sfx.I?.Play("tick", 0.12f, 1.7f);
    public static void ClickSound() => Sfx.I?.Play("tick", 0.3f);

    // ---------------- Godot theme (tooltips, scrollbars) ----------------

    public static Theme MakeTheme(float k)
    {
        var th = new Theme { DefaultFont = Body, DefaultFontSize = Fs(16, k) };
        var tip = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.1f, 0.14f, 0.97f),
            BorderColor = new Color(Text, 0.25f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 12 * k, ContentMarginRight = 12 * k, ContentMarginTop = 7 * k, ContentMarginBottom = 7 * k,
        };
        th.SetStylebox("panel", "TooltipPanel", tip);
        th.SetColor("font_color", "TooltipLabel", Text);
        th.SetFont("font", "TooltipLabel", Body);
        th.SetFontSize("font_size", "TooltipLabel", Fs(15, k));

        float bw = Mathf.Max(4, 6 * k);
        var track = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.04f), ContentMarginLeft = bw / 2, ContentMarginRight = bw / 2 };
        StyleBoxFlat Grab(Color c) => new() { BgColor = c, ContentMarginLeft = bw / 2, ContentMarginRight = bw / 2 };
        th.SetStylebox("scroll", "VScrollBar", track);
        th.SetStylebox("grabber", "VScrollBar", Grab(new Color(Text, 0.22f)));
        th.SetStylebox("grabber_highlight", "VScrollBar", Grab(new Color(Text, 0.45f)));
        th.SetStylebox("grabber_pressed", "VScrollBar", Grab(Accent));
        return th;
    }

    public static LabelSettings Label(Font f, float px, float k, Color c) => new() { Font = f, FontSize = Fs(px, k), FontColor = c };
}

/// <summary>Allocation-free drawing helpers shared by every screen (angular panels, text alignment, chevrons).</summary>
public static class Gfx
{
    // Span overloads of the CanvasItem polygon calls have no optional parameters; these keep call sites short.
    public static void FillPoly(this CanvasItem ci, ReadOnlySpan<Vector2> p, Color c) => ci.DrawColoredPolygon(p, c, ReadOnlySpan<Vector2>.Empty, null);
    public static void GradPoly(this CanvasItem ci, ReadOnlySpan<Vector2> p, ReadOnlySpan<Color> c) => ci.DrawPolygon(p, c, ReadOnlySpan<Vector2>.Empty, null);
    public static void Polyline(this CanvasItem ci, ReadOnlySpan<Vector2> p, Color c, float w, bool aa = false) => ci.DrawPolyline(p, c, w, aa);

    public static float TextW(Font f, string t, int size) => f.GetStringSize(t, HorizontalAlignment.Left, -1, size).X;

    /// <summary>Baseline that vertically centres capital letters on <paramref name="cy"/>.</summary>
    public static float Mid(float cy, int size) => cy + size * 0.36f;

    public static void Text(CanvasItem ci, Font f, string t, float x, float baseline, int size, Color c) =>
        ci.DrawString(f, new Vector2(x, baseline), t, HorizontalAlignment.Left, -1, size, c);

    public static float TextC(CanvasItem ci, Font f, string t, float cx, float baseline, int size, Color c)
    {
        float w = TextW(f, t, size);
        ci.DrawString(f, new Vector2(cx - w / 2, baseline), t, HorizontalAlignment.Left, -1, size, c);
        return w;
    }

    public static float TextR(CanvasItem ci, Font f, string t, float right, float baseline, int size, Color c)
    {
        float w = TextW(f, t, size);
        ci.DrawString(f, new Vector2(right - w, baseline), t, HorizontalAlignment.Left, -1, size, c);
        return w;
    }

    /// <summary>Text clipped with an ellipsis to <paramref name="maxW"/>.</summary>
    public static void TextFit(CanvasItem ci, Font f, string t, float x, float baseline, int size, Color c, float maxW, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        float w = TextW(f, t, size);
        if (w > maxW)
        {
            int n = t.Length;
            while (n > 1 && TextW(f, t[..n] + "…", size) > maxW) n--;
            t = t[..n].TrimEnd() + "…";
            w = TextW(f, t, size);
        }
        float px = align switch { HorizontalAlignment.Center => x + (maxW - w) / 2, HorizontalAlignment.Right => x + maxW - w, _ => x };
        ci.DrawString(f, new Vector2(px, baseline), t, HorizontalAlignment.Left, -1, size, c);
    }

    /// <summary>VALORANT-style plate: rectangle with the top-right and bottom-left corners cut off.</summary>
    public static void Plate(CanvasItem ci, Rect2 r, float cut, Color fill, Color border, float bw = 1f)
    {
        if (cut < 1f)
        {
            if (fill.A > 0) ci.DrawRect(r, fill);
            if (border.A > 0) ci.DrawRect(r, border, false, bw);
            return;
        }
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        cut = Mathf.Min(cut, Mathf.Min(r.Size.X, r.Size.Y) * 0.45f);
        Span<Vector2> p = stackalloc Vector2[7];
        p[0] = new(x0, y0); p[1] = new(x1 - cut, y0); p[2] = new(x1, y0 + cut); p[3] = new(x1, y1);
        p[4] = new(x0 + cut, y1); p[5] = new(x0, y1 - cut); p[6] = p[0];
        if (fill.A > 0) ci.FillPoly(p[..6], fill);
        if (border.A > 0) ci.Polyline(p, border, bw);
    }

    /// <summary>Horizontal parallelogram (one slanted side) for the top score boxes.</summary>
    public static void Slant(CanvasItem ci, Rect2 r, float slant, bool slantLeft, Color fill)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        Span<Vector2> p = stackalloc Vector2[4];
        if (slantLeft) { p[0] = new(x0 + slant, y0); p[1] = new(x1, y0); p[2] = new(x1, y1); p[3] = new(x0, y1); }
        else { p[0] = new(x0, y0); p[1] = new(x1 - slant, y0); p[2] = new(x1, y1); p[3] = new(x0, y1); }
        ci.FillPoly(p, fill);
    }

    /// <summary>Corner brackets (the little L marks VALORANT puts around focused elements).</summary>
    public static void Brackets(CanvasItem ci, Rect2 r, float len, Color c, float w)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        ci.DrawRect(new Rect2(x0, y0, len, w), c); ci.DrawRect(new Rect2(x0, y0, w, len), c);
        ci.DrawRect(new Rect2(x1 - len, y0, len, w), c); ci.DrawRect(new Rect2(x1 - w, y0, w, len), c);
        ci.DrawRect(new Rect2(x0, y1 - w, len, w), c); ci.DrawRect(new Rect2(x0, y1 - len, w, len), c);
        ci.DrawRect(new Rect2(x1 - len, y1 - w, len, w), c); ci.DrawRect(new Rect2(x1 - w, y1 - len, w, len), c);
    }

    /// <summary>Filled chevron arrow pointing left (dir -1) or right (dir +1).</summary>
    public static void Chevron(CanvasItem ci, Vector2 c, float h, int dir, Color col, float thick)
    {
        float w = h * 0.5f;
        Span<Vector2> p = stackalloc Vector2[6];
        float d = dir;
        p[0] = new(c.X - d * w / 2, c.Y - h / 2);
        p[1] = new(c.X - d * w / 2 + d * thick, c.Y - h / 2);
        p[2] = new(c.X + d * w / 2 + d * thick, c.Y);
        p[3] = new(c.X - d * w / 2 + d * thick, c.Y + h / 2);
        p[4] = new(c.X - d * w / 2, c.Y + h / 2);
        p[5] = new(c.X + d * w / 2, c.Y);
        ci.FillPoly(p, col);
    }

    /// <summary>Downward chevron "V" (kill-banner counters, rank pips).</summary>
    public static void ChevronDown(CanvasItem ci, Vector2 c, float w, float h, float thick, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[6];
        p[0] = new(c.X - w / 2, c.Y - h / 2);
        p[1] = new(c.X, c.Y + h / 2 - thick);
        p[2] = new(c.X + w / 2, c.Y - h / 2);
        p[3] = new(c.X + w / 2, c.Y - h / 2 + thick);
        p[4] = new(c.X, c.Y + h / 2);
        p[5] = new(c.X - w / 2, c.Y - h / 2 + thick);
        ci.FillPoly(p, col);
    }

    public static void Diamond(CanvasItem ci, Vector2 c, float rx, float ry, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[4];
        p[0] = new(c.X, c.Y - ry); p[1] = new(c.X + rx, c.Y); p[2] = new(c.X, c.Y + ry); p[3] = new(c.X - rx, c.Y);
        ci.FillPoly(p, col);
    }

    public static void DiamondLine(CanvasItem ci, Vector2 c, float rx, float ry, Color col, float w)
    {
        Span<Vector2> p = stackalloc Vector2[5];
        p[0] = new(c.X, c.Y - ry); p[1] = new(c.X + rx, c.Y); p[2] = new(c.X, c.Y + ry); p[3] = new(c.X - rx, c.Y); p[4] = p[0];
        ci.Polyline(p, col, w, true);
    }

    /// <summary>Vertical gradient rectangle.</summary>
    public static void VGradient(CanvasItem ci, Rect2 r, Color top, Color bottom)
    {
        Span<Vector2> p = stackalloc Vector2[4];
        Span<Color> c = stackalloc Color[4];
        p[0] = r.Position; p[1] = new(r.End.X, r.Position.Y); p[2] = r.End; p[3] = new(r.Position.X, r.End.Y);
        c[0] = top; c[1] = top; c[2] = bottom; c[3] = bottom;
        ci.GradPoly(p, c);
    }

    /// <summary>Horizontal gradient rectangle.</summary>
    public static void HGradient(CanvasItem ci, Rect2 r, Color left, Color right)
    {
        Span<Vector2> p = stackalloc Vector2[4];
        Span<Color> c = stackalloc Color[4];
        p[0] = r.Position; p[1] = new(r.End.X, r.Position.Y); p[2] = r.End; p[3] = new(r.Position.X, r.End.Y);
        c[0] = left; c[1] = right; c[2] = right; c[3] = left;
        ci.GradPoly(p, c);
    }

    /// <summary>Red (or any colour) glow along the four screen edges, fading inward.</summary>
    public static void EdgeVignette(CanvasItem ci, Vector2 size, float depth, Color c)
    {
        var clear = new Color(c, 0);
        Span<Vector2> p = stackalloc Vector2[4];
        Span<Color> col = stackalloc Color[4];
        // top
        p[0] = new(0, 0); p[1] = new(size.X, 0); p[2] = new(size.X - depth, depth); p[3] = new(depth, depth);
        col[0] = c; col[1] = c; col[2] = clear; col[3] = clear;
        ci.GradPoly(p, col);
        // bottom
        p[0] = new(0, size.Y); p[1] = new(depth, size.Y - depth); p[2] = new(size.X - depth, size.Y - depth); p[3] = new(size.X, size.Y);
        col[0] = c; col[1] = clear; col[2] = clear; col[3] = c;
        ci.GradPoly(p, col);
        // left
        p[0] = new(0, 0); p[1] = new(depth, depth); p[2] = new(depth, size.Y - depth); p[3] = new(0, size.Y);
        col[0] = c; col[1] = clear; col[2] = clear; col[3] = c;
        ci.GradPoly(p, col);
        // right
        p[0] = new(size.X, 0); p[1] = new(size.X, size.Y); p[2] = new(size.X - depth, size.Y - depth); p[3] = new(size.X - depth, depth);
        col[0] = c; col[1] = c; col[2] = clear; col[3] = clear;
        ci.GradPoly(p, col);
    }

    public static float EaseOutBack(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    public static float EaseOut(float t) { t = Mathf.Clamp(t, 0, 1); return 1 - (1 - t) * (1 - t); }
}
