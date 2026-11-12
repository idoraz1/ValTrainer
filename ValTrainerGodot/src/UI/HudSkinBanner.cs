using Godot;

namespace ValTrainer.UI;

/// <summary>
/// The skin kill banners (skins 3; style id = line id): the HUD multi-kill banner restyled per line. Glasswork's rose window,
/// Thunderhead's storm cloud, Basalt's hex columns, Oscilloscope's scope screen, Rally's roundel, Frostbite's ice crystal,
/// Hive's honeycomb, Foundry's molten ingot, Aurora's curtain, Orrery's orbits. Each fills one step per streak kill (all at
/// 5). Same place, timing (1.4 s) and pop as the factory banner, our own shapes, no text; drawn only right after one of the
/// skin's own kills (<see cref="Game.GameSession.SkinBanner"/>).
/// </summary>
public partial class Hud
{
    void DrawSkinKillBanner(Vector2 size, float k, int count, float t, string style, Color main0, Color accent0) =>
        DrawSkinBanner(this, new Vector2(size.X / 2, size.Y - 262 * k), k, count, t, style, main0, accent0);

    /// <summary>
    /// Draws a skin kill banner on any canvas item (the HUD, the skin picker's SHOWCASE): <paramref name="centre"/> in the
    /// item's own coordinates, <paramref name="k"/> = UI scale (1 at 1080p; the banner is about 140·k px wide), <paramref name="count"/>
    /// = the streak (1–5), <paramref name="t"/> = seconds left of its 1.4 s life, <paramref name="style"/> = the line's
    /// <c>SkinState.Banner</c> id (the line id), colours = the line's (or variant's) palette Main / Accent. Resets the item's
    /// draw transform to identity afterwards.
    /// </summary>
    public static void DrawSkinBanner(CanvasItem ci, Vector2 centre, float k, int count, float t, string style, Color main0, Color accent0)
    {
        const float life = 1.4f;
        float age = life - t;
        float pop = age < 0.14f ? 1.15f - 0.15f * (age / 0.14f) : 1f;
        float alpha = Mathf.Clamp(t / 0.35f, 0, 1);
        float r = 36 * k;
        count = Math.Clamp(count, 1, 5);
        var b = new BannerPaint
        {
            Ci = ci, R = r, K = k, Age = age, Count = count, Alpha = alpha,
            Main = new Color(count >= 5 ? accent0 : main0, alpha),
            Accent = new Color(accent0, alpha),
            Dim = new Color(main0, 0.25f * alpha),
            Plate = new Color(0.05f, 0.07f, 0.1f, 0.88f * alpha),
            Core = new Color(1, 1, 1, 0.92f * alpha),
            Dark = new Color(0.02f, 0.02f, 0.03f, 0.92f * alpha),
        };
        ci.DrawSetTransform(centre, 0, new Vector2(pop, pop));
        switch (style)
        {
            case "glasswork": BannerGlass(b); break;
            case "thunderhead": BannerStorm(b); break;
            case "basalt": BannerBasalt(b); break;
            case "oscilloscope": BannerScope(b); break;
            case "rally": BannerRally(b); break;
            case "frostbite": BannerFrost(b); break;
            case "hive": BannerHive(b); break;
            case "foundry": BannerFoundry(b); break;
            case "aurora": BannerAurora(b); break;
            case "orrery": BannerOrrery(b); break;
            default: BannerPlain(b); break;
        }
        ci.DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>What every banner style draws with (all colours already carry the banner's fade).</summary>
    struct BannerPaint
    {
        public CanvasItem Ci;
        public float R, K, Age, Alpha;
        public int Count;
        public Color Main, Accent, Dim, Plate, Core, Dark;
        public readonly bool Lit(int i, int of) => i < Mathf.CeilToInt(Count * of / 5f);
        /// <summary>A ring burst at the pop (the first 0.45 s).</summary>
        public readonly void Burst(float scale = 1.15f)
        {
            if (Age >= 0.45f) return;
            float g = Age / 0.45f;
            Ci.DrawArc(Vector2.Zero, R * (scale + g * 1.3f), 0, Mathf.Tau, 48, new Color(Main, (1 - g) * 0.5f * Main.A), 3 * K, true);
        }
    }

    static void Pips(float y, float k, int count, Action<Vector2> one)
    {
        for (int i = 0; i < count; i++) one(new Vector2((i - (count - 1) / 2f) * 22 * k, y));
    }

    static void Ngon(Span<Vector2> p, Vector2 c, float rad, float rot)
    {
        for (int i = 0; i < p.Length; i++)
        {
            float a = rot + i * Mathf.Tau / p.Length;
            p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
        }
    }

    static void HexFill(CanvasItem ci, Vector2 c, float rad, Color col, float rot = 0)
    {
        Span<Vector2> p = stackalloc Vector2[6];
        Ngon(p, c, rad, rot);
        ci.FillPoly(p, col);
    }

    static void HexLine(CanvasItem ci, Vector2 c, float rad, Color col, float w, float rot = 0)
    {
        Span<Vector2> p = stackalloc Vector2[7];
        Ngon(p[..6], c, rad, rot);
        p[6] = p[0];
        ci.Polyline(p, col, w, true);
    }

    static Vector2 Dir(float a) => new(Mathf.Cos(a), Mathf.Sin(a));

    /// <summary>Glasswork: a rose window: ten jewel panes in lead came round a bright centre, two panes per streak kill.</summary>
    static void BannerGlass(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R;
        b.Burst();
        ci.DrawCircle(Vector2.Zero, r * 1.08f, b.Dark);
        Span<Vector2> pane = stackalloc Vector2[6];
        for (int i = 0; i < 10; i++)
        {
            float a0 = i * Mathf.Tau / 10 - Mathf.Pi / 2, a1 = a0 + Mathf.Tau / 10;
            for (int j = 0; j < 3; j++) pane[j] = Dir(Mathf.Lerp(a0, a1, j / 2f) + (j == 1 ? 0 : (j == 0 ? 0.04f : -0.04f))) * r * 0.98f;
            for (int j = 0; j < 3; j++) pane[3 + j] = Dir(Mathf.Lerp(a1, a0, j / 2f) + (j == 1 ? 0 : (j == 0 ? -0.06f : 0.06f))) * r * 0.42f;
            var col = b.Lit(i, 10) ? (i % 3 == 0 ? b.Main : i % 3 == 1 ? b.Accent : b.Main.Lerp(b.Accent, 0.5f)) : b.Dim;
            ci.FillPoly(pane, col);
        }
        ci.DrawArc(Vector2.Zero, r * 1.02f, 0, Mathf.Tau, 48, b.Accent, 3 * b.K, true);
        ci.DrawCircle(Vector2.Zero, r * 0.32f, b.Count >= 5 ? b.Core : b.Main);
        ci.DrawArc(Vector2.Zero, r * 0.36f, 0, Mathf.Tau, 32, b.Dark, 2.5f * b.K, true);
        Pips(r + 20 * b.K, b.K, b.Count, p =>
        {
            Span<Vector2> d = stackalloc Vector2[4];
            Ngon(d, p, 7 * b.K, 0);
            ci.FillPoly(d, b.Main);
        });
    }

    /// <summary>Thunderhead: a storm cloud with one bolt per streak kill under it.</summary>
    static void BannerStorm(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K;
        b.Burst();
        // bolts first (they hang under the cloud)
        Span<Vector2> bolt = stackalloc Vector2[5];
        for (int i = 0; i < b.Count; i++)
        {
            float x = (i - (b.Count - 1) / 2f) * r * 0.42f;
            bool flick = b.Age < 0.3f && ((int)(b.Age * 30) + i) % 3 == 0;
            bolt[0] = new Vector2(x, r * 0.1f);
            bolt[1] = new Vector2(x - r * 0.16f, r * 0.55f);
            bolt[2] = new Vector2(x + r * 0.06f, r * 0.55f);
            bolt[3] = new Vector2(x - r * 0.1f, r * 1.05f);
            bolt[4] = bolt[3];
            ci.Polyline(bolt[..4], new Color(b.Main, b.Main.A * 0.35f), 7 * k, true);
            ci.Polyline(bolt[..4], flick ? b.Core : b.Accent, 2.5f * k, true);
        }
        var cloud = b.Count >= 5 ? new Color(0.22f, 0.27f, 0.36f, 0.95f * b.Alpha) : new Color(0.16f, 0.19f, 0.25f, 0.92f * b.Alpha);
        ci.DrawCircle(new Vector2(-r * 0.55f, 0), r * 0.48f, cloud);
        ci.DrawCircle(new Vector2(r * 0.55f, r * 0.02f), r * 0.44f, cloud);
        ci.DrawCircle(new Vector2(0, -r * 0.28f), r * 0.62f, cloud);
        ci.DrawRect(new Rect2(-r * 0.95f, -r * 0.05f, r * 1.9f, r * 0.42f), cloud);
        ci.DrawArc(new Vector2(0, -r * 0.28f), r * 0.62f, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f, 24, b.Main, 2.2f * k, true);
        ci.DrawLine(new Vector2(-r * 0.95f, r * 0.37f), new Vector2(r * 0.95f, r * 0.37f), b.Main, 2.2f * k, true);
    }

    /// <summary>Basalt: a charcoal block of seven hex column tops; red magma seams open round one more column per kill.</summary>
    static void BannerBasalt(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K, c = r * 0.38f;
        b.Burst();
        HexFill(ci, Vector2.Zero, r * 1.12f, b.Dark, Mathf.Pi / 6);
        Span<Vector2> cells = stackalloc Vector2[7];
        cells[0] = Vector2.Zero;
        for (int i = 0; i < 6; i++) cells[i + 1] = Dir(i * Mathf.Tau / 6) * c * 1.75f;
        for (int i = 0; i < 7; i++)
        {
            bool lit = b.Lit(i, 7);
            HexFill(ci, cells[i], c * 0.9f, new Color(0.17f, 0.17f, 0.19f, 0.95f * b.Alpha), Mathf.Pi / 6);
            if (lit)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(b.Age * 14 + i);
                HexLine(ci, cells[i], c * 0.92f, new Color(b.Main, b.Main.A * pulse), 2.4f * k, Mathf.Pi / 6);
            }
            else HexLine(ci, cells[i], c * 0.92f, b.Dim, 1.2f * k, Mathf.Pi / 6);
        }
        HexLine(ci, Vector2.Zero, r * 1.12f, b.Main, 2.5f * k, Mathf.Pi / 6);
        Pips(r + 22 * k, k, b.Count, p => HexFill(ci, p, 7 * k, b.Main, Mathf.Pi / 6));
    }

    /// <summary>Oscilloscope: a scope screen: one harmonic per streak kill, a Lissajous figure at 5.</summary>
    static void BannerScope(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K, w = r * 1.55f, h = r * 1.05f;
        var rect = new Rect2(-w, -h, 2 * w, 2 * h);
        ci.DrawRect(rect, new Color(0.02f, 0.05f, 0.035f, 0.9f * b.Alpha));
        var grid = new Color(b.Main, 0.18f * b.Alpha);
        for (int i = 1; i < 8; i++) ci.DrawLine(new Vector2(-w + i * w / 4, -h), new Vector2(-w + i * w / 4, h), grid, 1f * k);
        for (int i = 1; i < 6; i++) ci.DrawLine(new Vector2(-w, -h + i * h / 3), new Vector2(w, -h + i * h / 3), grid, 1f * k);
        Span<Vector2> tr = stackalloc Vector2[64];
        float ph = b.Age * 9f;
        for (int i = 0; i < tr.Length; i++)
        {
            float u = i / (tr.Length - 1f);
            Vector2 v;
            if (b.Count >= 5)
            {
                float a = u * Mathf.Tau;
                v = new Vector2(Mathf.Sin(3 * a + ph * 0.3f) * w * 0.82f, Mathf.Sin(2 * a) * h * 0.78f);
            }
            else
            {
                float x = u * 2 - 1, y = 0, norm = 0;
                for (int n = 1; n <= b.Count; n++) { y += Mathf.Sin(n * (x * Mathf.Pi * 1.5f + ph)) / n; norm += 1f / n; }
                v = new Vector2(x * w * 0.92f, -y / norm * h * 0.75f);
            }
            tr[i] = v;
        }
        ci.Polyline(tr, new Color(b.Main, b.Main.A * 0.35f), 6 * k, true);
        ci.Polyline(tr, b.Main, 2.2f * k, true);
        ci.DrawRect(rect, b.Main, false, 2.5f * k);
        // two small dials under the screen
        for (int i = -1; i <= 1; i += 2)
        {
            var dc = new Vector2(i * w * 0.55f, h + 12 * k);
            ci.DrawCircle(dc, 6 * k, b.Plate);
            ci.DrawArc(dc, 6 * k, 0, Mathf.Tau, 16, b.Main, 1.5f * k, true);
            ci.DrawLine(dc, dc + Dir(-Mathf.Pi / 2 + i * (0.3f + b.Count * 0.25f)) * 5 * k, b.Main, 1.5f * k, true);
        }
    }

    /// <summary>Rally: a motorsport roundel with the livery stripes and a chequered band; one chequer row per kill.</summary>
    static void BannerRally(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K;
        b.Burst();
        var white = new Color(0.94f, 0.94f, 0.92f, 0.95f * b.Alpha);
        // chequered band behind the roundel: 5 rows, one lit per kill
        float bw = r * 2.9f, rowH = r * 0.2f;
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 14; col++)
            {
                var cell = new Rect2(-bw / 2 + col * bw / 14, -rowH * 2.5f + row * rowH, bw / 14, rowH);
                bool on = (row + col) % 2 == 0;
                ci.DrawRect(cell, row < b.Count ? (on ? white : b.Dark) : (on ? b.Dim : new Color(0, 0, 0, 0.4f * b.Alpha)));
            }
        ci.DrawCircle(Vector2.Zero, r * 0.95f, white);
        // two diagonal livery stripes clipped by the roundel (drawn as thick lines inside it)
        float s = r * 0.62f;
        ci.DrawLine(new Vector2(-s, s * 0.55f), new Vector2(s * 0.55f, -s), b.Main, 7 * k, true);
        ci.DrawLine(new Vector2(-s * 0.55f, s), new Vector2(s, -s * 0.55f), b.Accent.Lerp(new Color(0.78f, 0.14f, 0.17f, b.Alpha), 0.85f), 7 * k, true);
        ci.DrawArc(Vector2.Zero, r * 0.95f, 0, Mathf.Tau, 48, b.Dark, 4 * k, true);
        ci.DrawArc(Vector2.Zero, r * 1.02f, 0, Mathf.Tau, 48, b.Main, 2.5f * k, true);
    }

    /// <summary>Frostbite: a six-armed ice crystal whose arms grow with the streak (full with branches at 5).</summary>
    static void BannerFrost(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K;
        b.Burst();
        HexFill(ci, Vector2.Zero, r * 1.1f, new Color(0.06f, 0.1f, 0.15f, 0.9f * b.Alpha));
        HexLine(ci, Vector2.Zero, r * 1.1f, new Color(b.Accent, 0.5f * b.Alpha), 2 * k);
        float grow = Mathf.Clamp(b.Age / 0.25f, 0, 1);
        float len = r * (0.35f + 0.6f * b.Count / 5f) * (0.6f + 0.4f * grow);
        for (int i = 0; i < 6; i++)
        {
            var d = Dir(i * Mathf.Tau / 6 - Mathf.Pi / 2);
            ci.DrawLine(Vector2.Zero, d * len, new Color(b.Main, b.Main.A * 0.35f), 6 * k, true);
            ci.DrawLine(Vector2.Zero, d * len, b.Main, 2.5f * k, true);
            int branches = b.Count >= 5 ? 3 : Math.Max(1, b.Count / 2);
            for (int j = 1; j <= branches; j++)
            {
                var p0 = d * len * (j / (branches + 1f) + 0.1f);
                float bl = len * 0.28f * (1f - j * 0.18f);
                ci.DrawLine(p0, p0 + d.Rotated(0.9f) * bl, b.Accent, 1.8f * k, true);
                ci.DrawLine(p0, p0 + d.Rotated(-0.9f) * bl, b.Accent, 1.8f * k, true);
            }
        }
        HexFill(ci, Vector2.Zero, r * 0.14f, b.Core);
        Pips(r + 20 * k, k, b.Count, p => { ci.DrawLine(p - new Vector2(0, 7 * k), p + new Vector2(0, 7 * k), b.Main, 2 * k); ci.DrawLine(p - new Vector2(6 * k, 3.5f * k), p + new Vector2(6 * k, 3.5f * k), b.Main, 2 * k); ci.DrawLine(p - new Vector2(6 * k, -3.5f * k), p + new Vector2(6 * k, -3.5f * k), b.Main, 2 * k); });
    }

    /// <summary>Hive: seven honeycomb cells that fill with amber, one more per kill (all and dripping at 5).</summary>
    static void BannerHive(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K, c = r * 0.4f;
        b.Burst();
        Span<Vector2> cells = stackalloc Vector2[7];
        cells[0] = Vector2.Zero;
        for (int i = 0; i < 6; i++) cells[i + 1] = Dir(i * Mathf.Tau / 6 + Mathf.Pi / 6) * c * 1.75f;
        var wax = new Color(0.28f, 0.17f, 0.06f, 0.95f * b.Alpha);
        for (int i = 0; i < 7; i++)
        {
            HexFill(ci, cells[i], c * 0.98f, wax);
            if (b.Lit(i, 7))
            {
                float fill = i == Mathf.CeilToInt(b.Count * 7 / 5f) - 1 ? Mathf.Clamp(b.Age / 0.3f, 0.2f, 1f) : 1f;
                HexFill(ci, cells[i], c * 0.8f * fill, b.Main);
                HexFill(ci, cells[i] - new Vector2(c * 0.22f, c * 0.22f), c * 0.16f * fill, new Color(b.Accent, b.Accent.A * 0.8f));
            }
            else HexFill(ci, cells[i], c * 0.8f, b.Dim);
            HexLine(ci, cells[i], c * 0.98f, new Color(0.55f, 0.36f, 0.12f, b.Alpha), 2 * k);
        }
        if (b.Count >= 5)
        {
            float dy = Mathf.Clamp(b.Age / 0.8f, 0, 1) * r * 0.6f;
            ci.DrawCircle(cells[0] + new Vector2(0, c * 2.6f + dy), 4 * k, b.Main);
        }
    }

    /// <summary>Foundry: a cast-iron ingot with molten cracks, one more crack per kill; white-hot and sparking at 5.</summary>
    static void BannerFoundry(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K;
        b.Burst(1.25f);
        Span<Vector2> ingot = stackalloc Vector2[4];
        ingot[0] = new Vector2(-r * 1.25f, r * 0.6f); ingot[1] = new Vector2(r * 1.25f, r * 0.6f);
        ingot[2] = new Vector2(r * 0.95f, -r * 0.6f); ingot[3] = new Vector2(-r * 0.95f, -r * 0.6f);
        ci.FillPoly(ingot, new Color(0.16f, 0.13f, 0.11f, 0.95f * b.Alpha));
        // a glow pool in the middle that grows with the streak
        ci.DrawCircle(new Vector2(0, 0), r * (0.18f + 0.08f * b.Count), new Color(b.Main, b.Main.A * 0.5f));
        ci.DrawCircle(new Vector2(0, 0), r * (0.1f + 0.04f * b.Count), b.Count >= 5 ? b.Core : b.Accent);
        Span<Vector2> crack = stackalloc Vector2[4];
        for (int i = 0; i < b.Count; i++)
        {
            float a = -Mathf.Pi / 2 + (i - 2) * 0.62f + 0.15f;
            var d = Dir(a);
            crack[0] = d * r * 0.18f;
            crack[1] = d.Rotated(0.25f) * r * 0.45f;
            crack[2] = d.Rotated(-0.15f) * r * 0.7f;
            crack[3] = d.Rotated(0.1f) * r * 0.95f;
            for (int j = 0; j < 4; j++) crack[j] = new Vector2(crack[j].X * 1.2f, Mathf.Clamp(crack[j].Y, -r * 0.55f, r * 0.55f));
            ci.Polyline(crack, new Color(b.Main, b.Main.A * 0.4f), 6 * k, true);
            ci.Polyline(crack, b.Main, 2.2f * k, true);
        }
        var outline = new Vector2[] { ingot[0], ingot[1], ingot[2], ingot[3], ingot[0] };
        ci.DrawPolyline(outline, new Color(0.45f, 0.36f, 0.3f, b.Alpha), 2.5f * k, true);
        if (b.Count >= 5 && b.Age < 0.6f)
            for (int i = 0; i < 9; i++)
            {
                var d = Dir(-Mathf.Pi / 2 + (i - 4) * 0.3f);
                float g = b.Age / 0.6f;
                ci.DrawLine(d * r * (0.6f + g * 0.9f), d * r * (0.75f + g * 1.0f), new Color(b.Accent, (1 - g) * b.Alpha), 2 * k, true);
            }
        Pips(r + 18 * k, k, b.Count, p => ci.DrawRect(new Rect2(p - new Vector2(7 * k, 3 * k), new Vector2(14 * k, 6 * k)), b.Main));
    }

    /// <summary>Aurora: a dark sky panel where a curtain of light grows one band per kill (green into violet).</summary>
    static void BannerAurora(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K, w = r * 1.55f, h = r * 1.0f;
        b.Burst(1.4f);
        var rect = new Rect2(-w, -h, 2 * w, 2 * h);
        ci.DrawRect(rect, new Color(0.03f, 0.05f, 0.1f, 0.9f * b.Alpha));
        int bands = 4 + b.Count * 4;
        float bwid = 2 * w / 24f;
        for (int i = 0; i < 24; i++)
        {
            float x = -w + (i + 0.5f) * bwid;
            float wave = Mathf.Sin(i * 0.55f + b.Age * 4f) * h * 0.18f;
            float top = -h * (0.15f + 0.55f * (0.5f + 0.5f * Mathf.Sin(i * 1.3f + b.Age * 2.5f)));
            float bottom = h * 0.55f + wave;
            bool lit = Mathf.Abs(i - 11.5f) * 2 < bands;
            var green = lit ? b.Main : b.Dim;
            var violet = lit ? new Color(b.Accent, b.Accent.A * 0.8f) : new Color(b.Dim, b.Dim.A * 0.5f);
            ci.DrawLine(new Vector2(x, bottom), new Vector2(x, (bottom + top) / 2 + wave * 0.5f), green, bwid * 0.75f);
            ci.DrawLine(new Vector2(x, (bottom + top) / 2 + wave * 0.5f), new Vector2(x, top + wave), violet, bwid * 0.6f);
        }
        ci.DrawRect(rect, new Color(b.Main, 0.7f * b.Alpha), false, 2 * k);
        Pips(h + 16 * k, k, b.Count, p =>
        {
            Span<Vector2> d = stackalloc Vector2[4];
            Ngon(d, p, 6 * k, 0);
            ci.FillPoly(d, b.Main);
        });
    }

    /// <summary>Orrery: concentric orbits round a sun, one planet per kill; at 5 the planets line up.</summary>
    static void BannerOrrery(BannerPaint b)
    {
        var ci = b.Ci;
        float r = b.R, k = b.K;
        b.Burst();
        ci.DrawCircle(Vector2.Zero, r * 1.12f, new Color(0.06f, 0.09f, 0.2f, 0.92f * b.Alpha));
        for (int i = 0; i < 72; i += 6)
        {
            var d = Dir(i * Mathf.Tau / 72);
            ci.DrawLine(d * r * 1.02f, d * r * 1.1f, new Color(b.Main, 0.6f * b.Alpha), 1.2f * k, true);
        }
        ci.DrawArc(Vector2.Zero, r * 1.12f, 0, Mathf.Tau, 64, b.Main, 2.5f * k, true);
        float align = b.Count >= 5 ? Mathf.Clamp(b.Age / 0.5f, 0, 1) : 0f;
        for (int i = 0; i < 5; i++)
        {
            float rr = r * (0.3f + i * 0.17f);
            ci.DrawArc(Vector2.Zero, rr, 0, Mathf.Tau, 48, i < b.Count ? new Color(b.Main, 0.7f * b.Alpha) : b.Dim, 1.2f * k, true);
            if (i >= b.Count) continue;
            float a = i * 1.9f + b.Age * (1.6f - i * 0.25f);
            a = Mathf.LerpAngle(a, -Mathf.Pi / 4, align);
            ci.DrawCircle(Dir(a) * rr, (3.2f + i * 0.5f) * k, i == b.Count - 1 ? b.Accent : b.Main);
        }
        ci.DrawCircle(Vector2.Zero, r * 0.14f, b.Count >= 5 ? b.Core : b.Accent);
        if (align > 0f) ci.DrawLine(Vector2.Zero, Dir(-Mathf.Pi / 4) * r * 1.1f, new Color(b.Core, b.Core.A * align * 0.7f), 2 * k, true);
    }

    /// <summary>Any other id: a plain ring with pips (never shown for a known line).</summary>
    static void BannerPlain(BannerPaint b)
    {
        b.Burst();
        b.Ci.DrawCircle(Vector2.Zero, b.R, b.Plate);
        b.Ci.DrawArc(Vector2.Zero, b.R, 0, Mathf.Tau, 48, b.Main, 3 * b.K, true);
        b.Ci.DrawCircle(Vector2.Zero, b.R * 0.4f, b.Main);
        var ci = b.Ci;
        var main = b.Main;
        Pips(b.R + 20 * b.K, b.K, b.Count, p => ci.DrawCircle(p, 6 * b.K, main));
    }
}
