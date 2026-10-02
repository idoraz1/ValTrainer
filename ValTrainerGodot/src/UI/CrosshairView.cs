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
        if (style.Hide) return;
        if (style.HasOutline && style.OutlineThickness > 0 && style.OutlineOpacity > 0) Parts(style, cx, cy, k, true);
        Parts(style, cx, cy, k, false);
    }

    void Parts(CrosshairStyle s, int cx, int cy, float k, bool outline)
    {
        float o = outline ? Mathf.Round(s.OutlineThickness * k) : 0;
        Color Tint(float opacity) => outline
            ? new Color(s.OutlineColor, s.OutlineOpacity * Mathf.Min(1f, opacity * 1.5f))
            : new Color(s.Color, opacity);
        if (s.CenterDot && s.CenterDotOpacity > 0)
        {
            float d = Mathf.Max(1, Mathf.Round(s.CenterDotSize * k));
            Rect(cx - d / 2, cy - d / 2, d, d, o, Tint(s.CenterDotOpacity));
        }
        Lines(s.Inner, cx, cy, k, o, Tint(s.Inner.Opacity));
        Lines(s.Outer, cx, cy, k, o, Tint(s.Outer.Opacity));
    }

    void Lines(CrosshairLines l, int cx, int cy, float k, float o, Color c)
    {
        if (!l.Show || l.Opacity <= 0 || l.Thickness <= 0 || l.Length <= 0) return;
        float t = Mathf.Max(1, Mathf.Round(l.Thickness * k));
        float len = Mathf.Round(l.Length * k);
        float lenV = Mathf.Round((l.AllowVertScaling ? l.LengthVertical : l.Length) * k);
        float spread = (l.ShowMovementError ? MoveError * 14f * l.MovementErrorScale : 0f)
                     + (l.ShowShootingError ? FiringErrorPx * l.FiringErrorScale : 0f);
        float off = Mathf.Round((l.Offset + spread) * k);
        float half = Mathf.Floor(t / 2);
        Rect(cx + off, cy - half, len, t, o, c);
        Rect(cx - off - len, cy - half, len, t, o, c);
        Rect(cx - half, cy - off - lenV, t, lenV, o, c);
        Rect(cx - half, cy + off, t, lenV, o, c);
    }

    void Rect(float x, float y, float w, float h, float grow, Color c) =>
        DrawRect(new Rect2(x - grow, y - grow, w + grow * 2, h + grow * 2), c);

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
