using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// The "box" VALORANT shows while aiming down sights with a rifle, SMG or Chamber's Headhunter: the gun's hologram sight,
/// a thin glowing octagon (a rectangle with chamfered corners) floating around the crosshair (research: ads-model.md §1.1,
/// §4.4). Procedural only: no texture, no fill, no vignette. Drawn under the crosshair; it rides the gun's kick and sway
/// (<see cref="Offset"/>) and fades / scales in over the end of the raise (<see cref="Blend"/>).
/// </summary>
public partial class AdsSight : Control
{
    /// <summary>One gun's sight outline. Sizes are fractions of the viewport height.</summary>
    public sealed record SightLook(float W, float H, float Chamfer, float Stroke, Color Col, float Glow, float CenterDy);

    /// <summary>Default skins (Vandal, Phantom): neutral near-white at 80 % (reads on every map; the exact default colour
    /// isn't visible in the evidence). Measured from VALORANT footage: about 0.20 × 0.145 H.</summary>
    public static readonly SightLook Vandal = new(0.20f, 0.145f, 0.022f, 0.0028f, new Color(0.93f, 0.96f, 1f, 0.80f), 0.35f, 0f);
    public static readonly SightLook Phantom = Vandal with { H = 0.14f, Chamfer = 0.030f };
    /// <summary>Chamber's Headhunter: a larger orange octagon, 0.33 × 0.235 H, slightly below the centre (Headhunter ADS.png).</summary>
    public static readonly SightLook Headhunter = new(0.33f, 0.235f, 0.040f, 0.0035f, new Color(1f, 0.66f, 0.18f, 0.95f), 0.6f, 0.005f);

    /// <summary>The outline of a gun that aims down sights (null: no ADS, or a scope).</summary>
    public static SightLook? LookFor(WeaponKind k) => k switch
    {
        WeaponKind.Vandal => Vandal,
        WeaponKind.Phantom => Phantom,
        WeaponKind.Headhunter => Headhunter,
        _ => null,
    };

    /// <summary>The outline to draw (null = none).</summary>
    public SightLook? Look;
    /// <summary>Eased raise progress 0 (hip) .. 1 (aimed); the outline fades in from <see cref="FadeFrom"/>.</summary>
    public float Blend;
    /// <summary>Pixel offset from the screen centre: the gun's kick / sway (clamped to ±<see cref="MaxOffset"/> H).</summary>
    public Vector2 Offset;

    public const float FadeFrom = 0.55f, MaxOffset = 0.03f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Look is not { } l) return;
        float f = Mathf.SmoothStep(FadeFrom, 1f, Blend);
        if (f <= 0.001f) return;
        var size = GetViewportRect().Size;
        float h = size.Y, lim = MaxOffset * h;
        var off = new Vector2(Mathf.Clamp(Offset.X, -lim, lim), Mathf.Clamp(Offset.Y, -lim, lim));
        var c = size / 2 + off + new Vector2(0, l.CenterDy * h);
        float scale = Mathf.Lerp(1.12f, 1f, f);
        var pts = Octagon(c, l.W * h / 2 * scale, l.H * h / 2 * scale, l.Chamfer * h * scale);
        float stroke = Mathf.Max(2f, l.Stroke * h);
        float a = l.Col.A * f;
        // Glow: two soft passes under the line, then the line itself.
        DrawPolyline(pts, new Color(l.Col, a * l.Glow * 0.12f), stroke * 6f, true);
        DrawPolyline(pts, new Color(l.Col, a * l.Glow * 0.35f), stroke * 3f, true);
        DrawPolyline(pts, new Color(l.Col, a), stroke, true);
    }

    /// <summary>Closed octagon centred on <paramref name="c"/>: half sizes <paramref name="a"/> × <paramref name="b"/>, corners
    /// cut by <paramref name="cut"/>. It starts and ends mid top edge, so the open seam of the polyline sits on a straight
    /// line instead of a corner.</summary>
    public static Vector2[] Octagon(Vector2 c, float a, float b, float cut)
    {
        cut = Mathf.Min(cut, Mathf.Min(a, b));
        var p = new[]
        {
            new Vector2(0, -b), new Vector2(a - cut, -b), new Vector2(a, -b + cut), new Vector2(a, b - cut),
            new Vector2(a - cut, b), new Vector2(-a + cut, b), new Vector2(-a, b - cut), new Vector2(-a, -b + cut),
            new Vector2(-a + cut, -b), new Vector2(0, -b),
        };
        for (int i = 0; i < p.Length; i++) p[i] += c;
        return p;
    }
}
