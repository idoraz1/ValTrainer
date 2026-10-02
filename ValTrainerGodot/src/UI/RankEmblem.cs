using Godot;

namespace ValTrainer.UI;

/// <summary>
/// Original geometric tier emblems (deliberately NOT Riot's rank icons): Rookie = grey-brown shield with one
/// chevron, Regular = silver shield with gold chevrons, Veteran = teal cut gem, Elite = green winged gem with a
/// red core, Pro = radiant gold star. Below Rookie = hollow grey diamond. All drawn with polygons, no textures.
/// </summary>
public static class RankEmblem
{
    static readonly Vector2[] Shield =
    {
        new(0, -0.86f), new(0.72f, -0.52f), new(0.72f, 0.2f), new(0, 0.92f), new(-0.72f, 0.2f), new(-0.72f, -0.52f),
    };
    static readonly Vector2[] Gem = { new(0, -0.96f), new(0.7f, -0.12f), new(0, 0.96f), new(-0.7f, -0.12f) };
    static readonly Vector2[] GemInner = { new(0, -0.6f), new(0.42f, -0.1f), new(0, 0.6f), new(-0.42f, -0.1f) };
    static readonly Vector2[] GemFacet = { new(0, -0.6f), new(-0.42f, -0.1f), new(0, -0.1f) };
    static readonly Vector2[] SpikeL = { new(-0.6f, -0.3f), new(-1.0f, -0.12f), new(-0.6f, 0.06f) };
    static readonly Vector2[] SpikeR = { new(0.6f, -0.3f), new(1.0f, -0.12f), new(0.6f, 0.06f) };
    static readonly Vector2[] EliteBody = { new(0, -1f), new(0.46f, -0.05f), new(0, 0.96f), new(-0.46f, -0.05f) };
    static readonly Vector2[] WingL = { new(-0.2f, -0.28f), new(-1.0f, -0.62f), new(-0.62f, 0.05f), new(-0.3f, 0.32f) };
    static readonly Vector2[] WingR = { new(0.2f, -0.28f), new(1.0f, -0.62f), new(0.62f, 0.05f), new(0.3f, 0.32f) };
    static readonly Vector2[] EliteCore = { new(0, -0.5f), new(0.22f, -0.04f), new(0, 0.48f), new(-0.22f, -0.04f) };

    static Color A(Color c, float a) => new(c.R, c.G, c.B, c.A * a);

    static void Fill(CanvasItem ci, Vector2 c, float r, ReadOnlySpan<Vector2> unit, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[unit.Length];
        for (int i = 0; i < unit.Length; i++) p[i] = c + unit[i] * r;
        ci.FillPoly(p, col);
    }

    static void Outline(CanvasItem ci, Vector2 c, float r, ReadOnlySpan<Vector2> unit, Color col, float w)
    {
        Span<Vector2> p = stackalloc Vector2[unit.Length + 1];
        for (int i = 0; i < unit.Length; i++) p[i] = c + unit[i] * r;
        p[unit.Length] = p[0];
        ci.Polyline(p, col, w, true);
    }

    static void Star(CanvasItem ci, Vector2 c, float outer, float inner, int points, Color col, float rot = 0f)
    {
        Span<Vector2> p = stackalloc Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float ang = -Mathf.Pi / 2 + rot + i * Mathf.Pi / points;
            p[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (i % 2 == 0 ? outer : inner);
        }
        ci.FillPoly(p, col);
    }

    /// <summary>Draws the emblem for <paramref name="tier"/> (-1 = below Rookie) centred on <paramref name="c"/>, radius <paramref name="r"/>.</summary>
    public static void Draw(CanvasItem ci, Vector2 c, float r, int tier, float alpha = 1f)
    {
        float lw = Mathf.Max(1f, r * 0.09f);
        var col = UiTheme.TierColor(tier);
        var dark = new Color(0.06f, 0.09f, 0.12f);
        switch (tier)
        {
            case < 0:
                Gfx.DiamondLine(ci, c, r * 0.62f, r * 0.86f, A(col, 0.85f * alpha), lw);
                ci.DrawRect(new Rect2(c.X - r * 0.22f, c.Y - lw / 2, r * 0.44f, lw), A(col, 0.85f * alpha));
                break;

            case 0: // Rookie — grey-brown shield, one chevron
                Fill(ci, c, r, Shield, A(dark.Lerp(col, 0.32f), alpha));
                Outline(ci, c, r, Shield, A(col, alpha), lw);
                Gfx.ChevronDown(ci, c + new Vector2(0, r * 0.08f), r * 0.78f, r * 0.46f, r * 0.17f, A(col.Lightened(0.25f), alpha));
                break;

            case 1: // Regular — silver shield, two gold chevrons
            {
                var gold = Color.Color8(232, 192, 88);
                Fill(ci, c, r, Shield, A(dark.Lerp(col, 0.4f), alpha));
                Outline(ci, c, r, Shield, A(col, alpha), lw);
                Gfx.ChevronDown(ci, c + new Vector2(0, -r * 0.14f), r * 0.78f, r * 0.42f, r * 0.15f, A(gold, alpha));
                Gfx.ChevronDown(ci, c + new Vector2(0, r * 0.24f), r * 0.78f, r * 0.42f, r * 0.15f, A(gold, alpha));
                break;
            }

            case 2: // Veteran — teal cut gem with side spikes
                Fill(ci, c, r, SpikeL, A(col, alpha));
                Fill(ci, c, r, SpikeR, A(col, alpha));
                Fill(ci, c, r, Gem, A(dark.Lerp(col, 0.38f), alpha));
                Fill(ci, c, r, GemInner, A(col, alpha));
                Fill(ci, c, r, GemFacet, A(col.Lightened(0.55f), alpha));
                Outline(ci, c, r, Gem, A(col.Lightened(0.2f), alpha), lw);
                break;

            case 3: // Elite — green winged gem, red core
                Fill(ci, c, r, WingL, A(col.Darkened(0.15f), alpha));
                Fill(ci, c, r, WingR, A(col.Darkened(0.15f), alpha));
                Fill(ci, c, r, EliteBody, A(dark.Lerp(col, 0.45f), alpha));
                Outline(ci, c, r, EliteBody, A(col.Lightened(0.25f), alpha), lw);
                Fill(ci, c, r, EliteCore, A(UiTheme.Accent, alpha));
                Gfx.Diamond(ci, c + new Vector2(0, -r * 0.16f), r * 0.07f, r * 0.12f, A(UiTheme.Text, 0.85f * alpha));
                break;

            default: // Pro — radiant gold star
                ci.DrawCircle(c, r * 1.02f, A(col, 0.12f * alpha));
                Star(ci, c, r * 0.98f, r * 0.5f, 4, A(col.Darkened(0.2f), alpha), Mathf.Pi / 4);
                Star(ci, c, r, r * 0.4f, 4, A(col, alpha));
                Star(ci, c, r * 0.55f, r * 0.24f, 4, A(col.Lightened(0.55f), alpha), Mathf.Pi / 4);
                Gfx.Diamond(ci, c, r * 0.14f, r * 0.2f, A(Colors.White, 0.95f * alpha));
                break;
        }
    }
}
