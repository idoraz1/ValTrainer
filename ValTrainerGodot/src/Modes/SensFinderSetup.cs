using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Sens Finder start screen (players found the T / P keys hard to follow): shootable cards in front of the player,
/// like the Crosshair Finder's. Each choice shows what it means:
/// <list type="bullet">
/// <item>Aiming style: WRIST / HYBRID / ARM, each an animated top-down arm on a mousepad (what moves, where it pivots,
/// how far the mouse travels) and the cm/360 range it searches.</item>
/// <item>Mousepad: NOT SURE / SMALL / MEDIUM / LARGE / EXTENDED, each pad drawn to scale next to the same keyboard and
/// mouse, so the player can compare with their desk.</item>
/// <item>MICRO TEST on/off, SHORT TEST on/off, START, and COMPARE 2 SENS (the two-sens comparison, SensFinderCompare.cs).</item>
/// </list>
/// Shooting a card picks it (remembered in the settings); M, Q, T, P, [ and ] still work as shortcuts.
/// </summary>
public sealed partial class SensFinderMode
{
    const float CardDist = 6f;
    Vector3? cardEye;
    /// <summary>The pad cards (cm of usable width; 0 = not sure).</summary>
    static readonly int[] PadCards = { 0, 25, 32, 45, 90 };
    const int CPad0 = 3, CMicro = 8, CStart = 9, CShort = 10, CCompare = 11, CardCount = 12;

    /// <summary>Card i as (x, y, w, h) in units of the distance (tan of the angle) from the starting view.</summary>
    static (float X, float Y, float W, float H) SetupLayout(int i) => i switch
    {
        < 3 => ((i - 1) * 0.50f, 0.09f, 0.46f, 0.40f),
        < CMicro => ((i - CPad0 - 2) * 0.30f, -0.30f, 0.28f, 0.30f),
        CMicro => (-0.555f, -0.55f, 0.34f, 0.11f),
        CShort => (-0.185f, -0.55f, 0.34f, 0.11f),
        CStart => (0.185f, -0.55f, 0.34f, 0.11f),
        _ => (0.555f, -0.55f, 0.34f, 0.11f),
    };

    (Vector3 C, Vector3 R, Vector3 U) SetupQuad(int i) => CardQuad(SetupLayout(i));

    /// <summary>A card (x, y, w, h in units of the distance from the starting view) as a quad in front of the player.</summary>
    (Vector3 C, Vector3 R, Vector3 U) CardQuad((float X, float Y, float W, float H) l)
    {
        var eye = cardEye ?? G.View.Eye;
        var fwd = PlayerView.Dir(0, 0);
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd).Normalized();
        return (eye + (fwd + right * l.X + up * l.Y) * CardDist, right * (l.W / 2 * CardDist), up * (l.H / 2 * CardDist));
    }

    /// <summary>The card the ray points at, or -1.</summary>
    int SetupHit(Vector3 o, Vector3 d) => CardHit(o, d, CardCount, SetupQuad);

    /// <summary>Which of <paramref name="count"/> cards the ray points at, or -1.</summary>
    static int CardHit(Vector3 o, Vector3 d, int count, Func<int, (Vector3 C, Vector3 R, Vector3 U)> quad)
    {
        var n = PlayerView.Dir(0, 0);
        float den = d.Dot(n);
        if (den <= 1e-4f) return -1;
        for (int i = 0; i < count; i++)
        {
            var (c, r, u) = quad(i);
            var p = o + d * ((c - o).Dot(n) / den) - c;
            if (Mathf.Abs(p.Dot(r.Normalized())) <= r.Length() && Mathf.Abs(p.Dot(u.Normalized())) <= u.Length()) return i;
        }
        return -1;
    }

    /// <summary>Intro frame: a shot on a card picks it.</summary>
    void SetupShot(bool pressed)
    {
        cardEye ??= G.View.Eye;
        if (!pressed || phaseT < 0.6f) return;
        int i = SetupHit(G.View.Eye, G.View.Forward);
        if (i < 0) return;
        if (i < 3) ChangeRange((SfStyle)i, range.PadCm);
        else if (i < CMicro) ChangeRange(range.Style, PadCards[i - CPad0]);
        else if (i == CMicro) ToggleMicro();
        else if (i == CShort) ToggleShort();
        else if (i == CCompare) { G.Sound("head", 0.6f, 1.1f); OpenCompare(); return; }
        else { G.Sound("head", 0.8f); BeginStage(0); return; }
        G.Sound("head", 0.5f, 1.2f);
    }

    void ToggleMicro()
    {
        cfg.Micro = !cfg.Micro;
        Main.I.Settings.SensFinderMicro = cfg.Micro;
        Main.I.Settings.Save();
        G.Sound("tick", 0.3f, 1.2f);
    }

    void ToggleShort()
    {
        if (Main.I.Dev && Args.Contains("--sfquick")) return;
        bool micro = cfg.Micro, toShort = !cfg.IsShort;
        cfg = toShort ? SensFinderConfig.Short() : SensFinderConfig.Full();
        cfg.Micro = micro;
        Main.I.Settings.SensFinderShort = toShort;
        Main.I.Settings.Save();
        G.Sound("tick", 0.3f, 1.2f);
    }

    // =====================================================================================
    // Drawing
    // =====================================================================================

    void DrawSetup(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.6f);
        float cx = size.X / 2, pw = Mathf.Min(size.X - 40 * k, 1240 * k), x = cx - pw / 2 + 36 * k, w = pw - 72 * k;
        var top = new Rect2(cx - pw / 2, 66 * k, pw, 190 * k);
        Gfx.Plate(c, top, 20 * k, new Color(0.06f, 0.1f, 0.14f, 0.96f), new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(top.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
        float y = top.Position.Y + 52 * k;
        Gfx.Text(c, UiTheme.Display, "SENS FINDER", x, y, UiTheme.Fs(44, k), UiTheme.Text);
        Gfx.TextR(c, UiTheme.HudWide, L.F($"BLIND A/B TEST · ABOUT {Mathf.Max(1, Mathf.RoundToInt(cfg.EstimatedMinutes))} MINUTES"), top.End.X - 36 * k, y - 8 * k, UiTheme.Fs(14, k), UiTheme.Dim);
        y += 32 * k;
        Gfx.TextFit(c, UiTheme.Body, L.N("Short rounds with two hidden settings, A and B. Don't guess which is which: just aim as well as you can."),
            x, y, UiTheme.Fs(18, k), UiTheme.Text, w);
        y += 28 * k;
        Gfx.TextFit(c, UiTheme.Body, L.F($"Starting from your current sens {current:0.000} ({Cm(current):0.0} cm/360 at {dpi} DPI)."), x, y, UiTheme.Fs(16, k), UiTheme.Dim, w);
        y += 26 * k;
        Gfx.TextFit(c, UiTheme.Body, range.Strict
                ? (range.PadCm > 0 ? L.F($"Searching {range.Lo:0}–{range.Hi:0} cm/360 · {L.T(SfRange.StyleName(range.Style))} aiming · {range.PadCm} cm pad")
                    : L.F($"Searching {range.Lo:0}–{range.Hi:0} cm/360 · {L.T(SfRange.StyleName(range.Style))} aiming"))
                : L.N("Searching around your current sens. Pick how you aim and your mousepad below to narrow it down."),
            x, y, UiTheme.Fs(16, k), range.Strict ? UiTheme.Good : UiTheme.Dim, w);
        y += 30 * k;
        Gfx.Text(c, UiTheme.HudWide, "SHOOT A CARD TO CHOOSE · SHOOT START WHEN YOU'RE READY", x, y, UiTheme.Fs(15, k), UiTheme.Accent);

        var cam = c.GetViewport().GetCamera3D();
        if (cam == null) return;
        int hover = SetupHit(G.View.Eye, G.View.Forward);
        var rects = CardRects(cam, CardCount, SetupQuad);
        // row labels above the first card of each row
        if (rects[0] is { } r0) Gfx.Text(c, UiTheme.HudWide, "HOW DO YOU AIM?", r0.Position.X, r0.Position.Y - 10 * k, UiTheme.Fs(14, k), UiTheme.Text);
        if (rects[CPad0] is { } rp) Gfx.Text(c, UiTheme.HudWide, "YOUR MOUSEPAD (THE WIDTH YOUR MOUSE CAN REALLY USE)", rp.Position.X, rp.Position.Y - 10 * k, UiTheme.Fs(14, k), UiTheme.Text);
        for (int i = 0; i < CardCount; i++)
        {
            if (rects[i] is not { } r) continue;
            bool hov = hover == i;
            if (i < 3) DrawStyleCard(c, r, k, (SfStyle)i, range.Style == (SfStyle)i, hov);
            else if (i < CMicro) DrawPadCard(c, r, k, PadCards[i - CPad0], range.PadCm == PadCards[i - CPad0], hov);
            else if (i == CStart) DrawButtonCard(c, r, k, "START", null, true, hov, UiTheme.Accent);
            else if (i == CCompare) DrawButtonCard(c, r, k, L.T("COMPARE 2 SENS"), L.F($"which of two sens suits you · about {Mathf.Max(1, Mathf.RoundToInt(cfg.CompareMinutes))} min"), true, hov, UiTheme.Teal);
            else if (i == CMicro) DrawButtonCard(c, r, k, cfg.Micro ? L.T("MICRO TEST: ON") : L.T("MICRO TEST: OFF"), L.T("tiny heads next to your crosshair"), cfg.Micro, hov, UiTheme.Good);
            else DrawButtonCard(c, r, k, cfg.IsShort ? L.T("SHORT TEST: ON") : L.T("SHORT TEST: OFF"), L.T("about half the time"), cfg.IsShort, hov, UiTheme.Good);
        }
        // a custom pad width ([ and ]) that isn't one of the cards
        if (range.PadCm > 0 && Array.IndexOf(PadCards, range.PadCm) < 0 && rects[CPad0 + 4] is { } rl)
            Gfx.TextR(c, UiTheme.Body, L.F($"Custom: {range.PadCm} cm ([ and ] to adjust)"), rl.End.X, rl.Position.Y - 10 * k, UiTheme.Fs(14, k), UiTheme.Good);
    }

    /// <summary>The cards' screen rectangles (null = behind the camera).</summary>
    static Rect2?[] CardRects(Camera3D cam, int count, Func<int, (Vector3 C, Vector3 R, Vector3 U)> quad)
    {
        var rects = new Rect2?[count];
        for (int i = 0; i < count; i++)
        {
            var (qc, qr, qu) = quad(i);
            var corners = new[] { qc - qr - qu, qc + qr - qu, qc + qr + qu, qc - qr + qu };
            if (corners.Any(cam.IsPositionBehind)) continue;
            var pts = corners.Select(p => cam.UnprojectPosition(p)).ToArray();
            var min = pts.Aggregate((a, b) => a.Min(b));
            var max = pts.Aggregate((a, b) => a.Max(b));
            rects[i] = new Rect2(min.Round(), (max - min).Round());
        }
        return rects;
    }

    static void CardFrame(CanvasItem c, Rect2 r, float k, bool selected, bool hover)
    {
        Gfx.Plate(c, r, 12 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), selected ? new Color(UiTheme.Good, 0.9f) : new Color(UiTheme.Text, 0.14f));
        if (selected) c.DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 4 * k), UiTheme.Good);
        if (hover) c.DrawRect(r.Grow(4 * k), UiTheme.Accent, false, 3 * k);
    }

    void DrawButtonCard(CanvasItem c, Rect2 r, float k, string title, string? sub, bool on, bool hover, Color col)
    {
        CardFrame(c, r, k, false, hover);
        float midY = r.Position.Y + r.Size.Y / 2;
        if (sub == null)
        {
            Gfx.TextC(c, UiTheme.Display, title, r.GetCenter().X, Gfx.Mid(midY, UiTheme.Fs(34, k)), UiTheme.Fs(34, k), hover ? col : new Color(col, 0.85f));
            return;
        }
        Gfx.TextC(c, UiTheme.HudWide, title, r.GetCenter().X, midY - 2 * k, UiTheme.Fs(15, k), on ? col : UiTheme.Dim);
        Gfx.TextC(c, UiTheme.Body, sub, r.GetCenter().X, midY + 18 * k, UiTheme.Fs(12, k), UiTheme.Faint);
    }

    /// <summary>An aiming style: a top-down arm on a mousepad, animated (what moves and where it pivots), and its range.</summary>
    void DrawStyleCard(CanvasItem c, Rect2 r, float k, SfStyle s, bool selected, bool hover)
    {
        CardFrame(c, r, k, selected, hover);
        float pad = 12 * k, x = r.Position.X + pad, w = r.Size.X - 2 * pad;
        string title = s switch { SfStyle.Wrist => L.T("WRIST"), SfStyle.Arm => L.T("ARM"), _ => L.T("HYBRID") };
        string what = s switch
        {
            SfStyle.Wrist => L.T("Arm stays still, the wrist turns the mouse."),
            SfStyle.Arm => L.T("The whole forearm slides across the pad."),
            _ => L.T("Arm for big turns, wrist for fine aim."),
        };
        var rg = SfRange.For(s, 0);
        string span = s == SfStyle.Hybrid ? L.T("Searches around your current sens") : L.F($"Fast to slow: {rg.Lo:0}–{rg.Hi:0} cm/360");
        Gfx.Text(c, UiTheme.Display, title, x, r.Position.Y + 36 * k, UiTheme.Fs(30, k), selected ? UiTheme.Good : UiTheme.Text);
        if (selected) Gfx.TextR(c, UiTheme.HudWide, "SELECTED", r.End.X - pad, r.Position.Y + 30 * k, UiTheme.Fs(11, k), UiTheme.Good);
        var ill = new Rect2(x, r.Position.Y + 48 * k, w, r.Size.Y - 48 * k - 62 * k);
        DrawArm(c, ill, k, s);
        Gfx.TextFit(c, UiTheme.Body, what, x, r.End.Y - 36 * k, UiTheme.Fs(14, k), UiTheme.Text, w);
        Gfx.TextFit(c, UiTheme.Body, span, x, r.End.Y - 14 * k, UiTheme.Fs(13, k), UiTheme.Dim, w);
    }

    /// <summary>Top-down arm: forearm from the elbow (bottom) to the wrist, the hand to the mouse. The motion loops; a
    /// dashed path shows where the mouse travels and a ring marks the pivot.</summary>
    static void DrawArm(CanvasItem c, Rect2 ill, float k, SfStyle s)
    {
        var padR = new Rect2(ill.Position.X + ill.Size.X * 0.06f, ill.Position.Y + ill.Size.Y * 0.04f, ill.Size.X * 0.88f, ill.Size.Y * 0.62f);
        Gfx.Plate(c, padR, 8 * k, new Color(0.16f, 0.2f, 0.25f, 1f), new Color(1, 1, 1, 0.08f));
        float lf = ill.Size.Y * 0.62f, lh = ill.Size.Y * 0.2f;
        var elbow0 = new Vector2(ill.GetCenter().X, ill.End.Y - 2 * k);
        float t = Time.GetTicksMsec() / 1000f;
        // where the mouse is for a motion value m in [-1, 1]
        (Vector2 E, Vector2 W, Vector2 M) Pose(float m)
        {
            float fore = 0f, hand = 0f;
            var e = elbow0;
            switch (s)
            {
                case SfStyle.Wrist: hand = m * 38f; break;
                case SfStyle.Hybrid: fore = m * 14f; hand = m * 22f; break;
                default: e = elbow0 + new Vector2(m * ill.Size.X * 0.3f, 0); fore = m * 6f; hand = m * 4f; break;
            }
            var wr = e + Vector2.Up.Rotated(Mathf.DegToRad(fore)) * lf;
            var mo = wr + Vector2.Up.Rotated(Mathf.DegToRad(fore + hand)) * lh;
            return (e, wr, mo);
        }
        // the path of the mouse
        var path = new Vector2[25];
        for (int i = 0; i < path.Length; i++) path[i] = Pose(-1f + 2f * i / (path.Length - 1)).M;
        for (int i = 0; i < path.Length - 1; i += 2) c.DrawLine(path[i], path[i + 1], new Color(UiTheme.Accent, 0.9f), 2.5f * k, true);
        Arrow(c, path[^2], path[^1], k); Arrow(c, path[1], path[0], k);
        float mNow = Mathf.Sin(t * 2.4f);
        var (eN, wN, mN) = Pose(mNow);
        var skin = new Color(0.86f, 0.72f, 0.62f);
        var sleeve = new Color(0.35f, 0.42f, 0.55f);
        c.DrawLine(eN, wN, sleeve, 16 * k, true);
        c.DrawLine(wN, mN, skin, 12 * k, true);
        // the mouse
        var dir = (mN - wN).Normalized();
        var perp = new Vector2(-dir.Y, dir.X);
        float mw = 9 * k, ml = 15 * k;
        var mouse = new[] { mN - perp * mw + dir * ml * 0.2f, mN + perp * mw + dir * ml * 0.2f, mN + perp * mw * 0.8f + dir * ml, mN - perp * mw * 0.8f + dir * ml };
        c.DrawColoredPolygon(mouse, new Color(0.08f, 0.09f, 0.11f));
        c.DrawPolyline(new[] { mouse[0], mouse[1], mouse[2], mouse[3], mouse[0] }, new Color(1, 1, 1, 0.5f), 1.2f * k, true);
        // the pivot
        var pivot = s switch { SfStyle.Wrist => wN, SfStyle.Hybrid => eN, _ => (Vector2?)null };
        if (pivot is { } pv) c.DrawArc(pv, 9 * k, 0, Mathf.Tau, 24, UiTheme.Warn, 2.5f * k, true);
        string pivotText = s switch { SfStyle.Wrist => L.T("pivot: wrist"), SfStyle.Hybrid => L.T("pivot: elbow + wrist"), _ => L.T("the whole arm moves") };
        Gfx.Text(c, UiTheme.Body, pivotText, padR.Position.X + 8 * k, padR.Position.Y + 16 * k, UiTheme.Fs(12, k), UiTheme.Warn); // top-left of the pad: clear of the arm and the pivot ring
    }

    static void Arrow(CanvasItem c, Vector2 from, Vector2 to, float k)
    {
        var d = (to - from).Normalized();
        var p = new Vector2(-d.Y, d.X);
        c.DrawColoredPolygon(new[] { to + d * 6 * k, to - d * 4 * k + p * 5 * k, to - d * 4 * k - p * 5 * k }, UiTheme.Accent);
    }

    /// <summary>A mousepad drawn to scale (same scale on every card) under a 44 cm keyboard, with a 6 × 12 cm mouse.</summary>
    void DrawPadCard(CanvasItem c, Rect2 r, float k, int padCm, bool selected, bool hover)
    {
        CardFrame(c, r, k, selected, hover);
        float pad = 10 * k, x = r.Position.X + pad, w = r.Size.X - 2 * pad;
        string title = padCm switch { 0 => L.T("NOT SURE"), 25 => L.T("SMALL"), 32 => L.T("MEDIUM"), 45 => L.T("LARGE"), _ => L.T("EXTENDED") };
        Gfx.Text(c, UiTheme.Display, title, x, r.Position.Y + 30 * k, UiTheme.Fs(22, k), selected ? UiTheme.Good : UiTheme.Text);
        string sub = padCm switch
        {
            0 => L.T("Skip: search around your sens"),
            25 => L.T("25 cm wide · a basic pad"),
            32 => L.T("32 cm wide · most gaming pads"),
            45 => L.T("45 cm wide · a large pad"),
            _ => L.T("90 cm wide · a desk mat"),
        };
        Gfx.TextFit(c, UiTheme.Body, sub, x, r.End.Y - 12 * k, UiTheme.Fs(12, k), UiTheme.Dim, w);
        var area = new Rect2(x, r.Position.Y + 40 * k, w, r.Size.Y - 40 * k - 30 * k);
        if (padCm == 0)
        {
            Gfx.TextC(c, UiTheme.Display, "?", area.GetCenter().X, Gfx.Mid(area.GetCenter().Y, UiTheme.Fs(64, k)), UiTheme.Fs(64, k), UiTheme.Faint);
            return;
        }
        // one scale for every card: a 92 cm span fits the card's width
        float s = area.Size.X / 92f;
        float padW = padCm * s, padH = (padCm >= 90 ? 40f : padCm * 0.82f) * s;
        float kbW = 44f * s, kbH = 14f * s;
        float baseY = area.End.Y;
        // the pad sits at the bottom right (where a right-handed mouse goes); the extended mat sits under the keyboard
        var padRect = padCm >= 90
            ? new Rect2(area.GetCenter().X - padW / 2, baseY - padH, padW, padH)
            : new Rect2(area.End.X - padW, baseY - padH, padW, padH);
        c.DrawRect(padRect, new Color(0.18f, 0.25f, 0.33f));
        c.DrawRect(padRect, new Color(UiTheme.Accent, 0.6f), false, 1.5f * k);
        var kb = padCm >= 90
            ? new Rect2(padRect.Position.X + 4 * s, padRect.Position.Y + 8 * s, kbW, kbH)
            : new Rect2(padRect.Position.X - kbW - 3 * s, baseY - padH * 0.6f - kbH / 2, kbW, kbH);
        c.DrawRect(kb, new Color(0.08f, 0.09f, 0.11f));
        c.DrawRect(kb, new Color(1, 1, 1, 0.25f), false, 1f * k);
        var mouse = new Rect2(padRect.GetCenter().X + (padCm >= 90 ? padW * 0.25f : 0) - 3 * s, padRect.GetCenter().Y - 6 * s, 6 * s, 12 * s);
        c.DrawRect(mouse, new Color(0.9f, 0.9f, 0.92f));
        Gfx.Text(c, UiTheme.Body, L.T("keyboard"), kb.Position.X, kb.Position.Y - 3 * k, UiTheme.Fs(10, k), UiTheme.Faint);
    }
}
