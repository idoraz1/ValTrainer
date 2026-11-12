using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>
/// The skin collection's glyphs (skins3-contracts.md): tier pips (our own: one diamond Core, two Signature, three Apex; not
/// Riot's gem icons), one icon per kind of upgrade (animation, bullet FX, sounds, hit sounds, kill effect, inspect, special
/// reload, finisher, banner, counter; tracers and the equip flourish for older data), a padlock and a tick. All drawn with
/// <see cref="Gfx"/> primitives, no textures.
/// </summary>
public static class SkinIcons
{
    /// <summary>The kinds of upgrade a level can add, as the level track shows them (several features share one icon).</summary>
    public enum Kind { Vfx, Sound, Kill, Finisher, Banner, Counter, Tracer, Flourish, Bullet, HitSound, Inspect, Reload }

    public static Color EditionColor(Edition e) => e switch
    {
        Edition.Core => UiTheme.Dim, Edition.Signature => UiTheme.Teal, _ => UiTheme.Warn,
    };

    /// <summary>The icons of <paramref name="f"/>, in track order (no duplicates).</summary>
    public static IEnumerable<Kind> Kinds(SkinFeature f)
    {
        if ((f & (SkinFeature.Animated | SkinFeature.Reactive)) != 0) yield return Kind.Vfx;
        if ((f & SkinFeature.BulletFx) != 0) yield return Kind.Bullet;
        else if ((f & SkinFeature.Tracers) != 0) yield return Kind.Tracer;
        if ((f & SkinFeature.EquipFlourish) != 0) yield return Kind.Flourish;
        if ((f & (SkinFeature.Sounds | SkinFeature.KillSounds)) != 0) yield return Kind.Sound;
        if ((f & SkinFeature.HitSounds) != 0) yield return Kind.HitSound;
        if ((f & SkinFeature.KillFx) != 0) yield return Kind.Kill;
        if ((f & SkinFeature.Inspect) != 0) yield return Kind.Inspect;
        if ((f & SkinFeature.ReloadAnim) != 0) yield return Kind.Reload;
        if ((f & SkinFeature.Finisher) != 0) yield return Kind.Finisher;
        if ((f & SkinFeature.KillBanner) != 0) yield return Kind.Banner;
        if ((f & SkinFeature.KillCounter) != 0) yield return Kind.Counter;
    }

    /// <summary>What each feature is, for the level details and tooltips (English data: translate where shown).</summary>
    public static IEnumerable<string> Names(SkinFeature f)
    {
        foreach (var x in Enum.GetValues<SkinFeature>())
        {
            if (x == SkinFeature.None || (f & x) == 0) continue;
            yield return x switch
            {
                SkinFeature.Animated => L.N("Animated finish"),
                SkinFeature.Reactive => L.N("Glows when you fire"),
                SkinFeature.Sounds => L.N("Own fire, reload and equip sounds"),
                SkinFeature.KillFx => L.N("Kill effect"),
                SkinFeature.Finisher => L.N("Finisher on the round-winning kill"),
                SkinFeature.KillBanner => L.N("Own kill banner"),
                SkinFeature.KillSounds => L.N("Own kill sounds 1–5"),
                SkinFeature.KillCounter => L.N("Kill counter on the gun"),
                SkinFeature.Tracers => L.N("Tinted tracers"),
                SkinFeature.BulletFx => L.N("Own muzzle flash, tracers and impacts"),
                SkinFeature.Inspect => L.N("Inspect flourish"),
                SkinFeature.ReloadAnim => L.N("Special reload animation"),
                SkinFeature.HitSounds => L.N("Own hit and headshot sounds"),
                _ => L.N("Equip flourish"),
            };
        }
    }

    /// <summary>A short caps label per icon (English data: translate where shown), for the icon legends.</summary>
    public static string Label(Kind k) => k switch
    {
        Kind.Vfx => L.N("ANIMATED"), Kind.Sound => L.N("SOUNDS"), Kind.Kill => L.N("KILL EFFECT"), Kind.Finisher => L.N("FINISHER"),
        Kind.Banner => L.N("KILL BANNER"), Kind.Counter => L.N("KILL COUNTER"), Kind.Tracer => L.N("TRACERS"), Kind.Flourish => L.N("EQUIP FLOURISH"),
        Kind.Bullet => L.N("BULLET FX"), Kind.HitSound => L.N("HIT SOUNDS"), Kind.Inspect => L.N("INSPECT"), _ => L.N("SPECIAL RELOAD"),
    };

    /// <summary>The features as one translated list ("Animated finish · Glows when you fire").</summary>
    public static string List(SkinFeature f) => string.Join(" · ", Names(f).Select(L.T));

    /// <summary>Tier pips at <paramref name="left"/> (vertical centre), <paramref name="s"/> = pip height: one diamond for Core,
    /// two for Signature, three for Apex. Returns the width.</summary>
    public static float Pips(CanvasItem ci, Edition e, Vector2 left, float s, Color? color = null)
    {
        var col = color ?? EditionColor(e);
        float r = s * 0.5f;
        int n = (int)e + 1;
        for (int i = 0; i < n; i++)
        {
            var c = left + new Vector2(r * 0.8f + i * r * 1.45f, 0);
            Gfx.Diamond(ci, c, r * 0.72f, r, col);
        }
        return r * 1.6f + (n - 1) * r * 1.45f;
    }

    /// <summary>A five-point star.</summary>
    public static void Star(CanvasItem ci, Vector2 c, float r, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5, rr = i % 2 == 0 ? r : r * 0.45f;
            p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        ci.FillPoly(p, col);
    }

    /// <summary>A padlock centred on <paramref name="c"/>, <paramref name="s"/> tall.</summary>
    public static void Lock(CanvasItem ci, Vector2 c, float s, Color col)
    {
        float lw = Mathf.Max(1.4f, s * 0.13f);
        ci.DrawArc(c + new Vector2(0, -s * 0.12f), s * 0.27f, Mathf.Pi, Mathf.Tau, 10, col, lw, true);
        ci.DrawLine(c + new Vector2(-s * 0.27f, -s * 0.12f), c + new Vector2(-s * 0.27f, s * 0.02f), col, lw, true);
        ci.DrawLine(c + new Vector2(s * 0.27f, -s * 0.12f), c + new Vector2(s * 0.27f, s * 0.02f), col, lw, true);
        ci.DrawRect(new Rect2(c + new Vector2(-s * 0.4f, -s * 0.02f), new Vector2(s * 0.8f, s * 0.52f)), col);
    }

    /// <summary>A tick centred on <paramref name="c"/>, <paramref name="s"/> wide.</summary>
    public static void Tick(CanvasItem ci, Vector2 c, float s, Color col)
    {
        ci.DrawPolyline(new[] { c + new Vector2(-0.45f, 0) * s, c + new Vector2(-0.12f, 0.32f) * s, c + new Vector2(0.45f, -0.36f) * s }, col, Mathf.Max(1.5f, s * 0.16f), true);
    }

    /// <summary>One upgrade icon centred on <paramref name="c"/>, about <paramref name="s"/> across.</summary>
    public static void Draw(CanvasItem ci, Kind kind, Vector2 c, float s, Color col)
    {
        float h = s * 0.5f, lw = Mathf.Max(1.3f, s * 0.11f);
        Vector2 P(float x, float y) => c + new Vector2(x, y) * h;
        switch (kind)
        {
            case Kind.Vfx: // a four-point sparkle
                ci.FillPoly(new[] { P(0, -1), P(0.22f, -0.22f), P(1, 0), P(0.22f, 0.22f), P(0, 1), P(-0.22f, 0.22f), P(-1, 0), P(-0.22f, -0.22f) }, col);
                break;
            case Kind.Sound: // a speaker with two waves
                ci.FillPoly(new[] { P(-0.95f, -0.3f), P(-0.55f, -0.3f), P(-0.1f, -0.75f), P(-0.1f, 0.75f), P(-0.55f, 0.3f), P(-0.95f, 0.3f) }, col);
                ci.DrawArc(P(-0.1f, 0), h * 0.45f, -0.8f, 0.8f, 8, col, lw, true);
                ci.DrawArc(P(-0.1f, 0), h * 0.85f, -0.8f, 0.8f, 10, col, lw, true);
                break;
            case Kind.Kill: // a burst
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Pi / 4 + Mathf.Pi / 8;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    ci.DrawLine(c + d * h * 0.35f, c + d * h * (i % 2 == 0 ? 1f : 0.75f), col, lw, true);
                }
                break;
            case Kind.Finisher: // a diamond going off: a filled core, a frame and four sparks
                Gfx.Diamond(ci, c, h * 0.38f, h * 0.38f, col);
                Gfx.DiamondLine(ci, c, h * 0.7f, h * 0.7f, col, lw);
                foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                    ci.DrawLine(c + d * h * 0.5f, c + d * h * 0.8f, col, lw, true);
                break;
            case Kind.Banner: // a pennant
                ci.FillPoly(new[] { P(-0.75f, -0.8f), P(0.75f, -0.8f), P(0.75f, 0.85f), P(0f, 0.4f), P(-0.75f, 0.85f) }, col);
                break;
            case Kind.Counter: // a two-digit readout
            {
                var r = new Rect2(P(-1f, -0.62f), new Vector2(2f, 1.24f) * h);
                ci.DrawRect(r, col, false, lw);
                int fs = Math.Max(7, (int)(s * 0.62f));
                Gfx.TextRaw(ci, UiTheme.HudWide, "07", c.X - Gfx.RawW(UiTheme.HudWide, "07", fs) / 2, Gfx.Mid(c.Y, fs), fs, col);
                break;
            }
            case Kind.Tracer: // a dashed streak with a head
                for (int i = 0; i < 3; i++)
                {
                    float t0 = -1f + i * 0.62f;
                    ci.DrawLine(P(t0, -t0 * 0.55f), P(t0 + 0.38f, -(t0 + 0.38f) * 0.55f), col, lw, true);
                }
                ci.DrawCircle(P(0.82f, -0.45f), h * 0.2f, col);
                break;
            case Kind.Bullet: // a muzzle burst with a streak leaving it
                ci.FillPoly(new[] { P(-0.95f, 0.55f), P(-0.55f, 0.3f), P(-0.35f, 0.65f), P(-0.4f, 0.2f), P(-0.05f, 0.1f), P(-0.45f, 0f), P(-0.4f, -0.4f), P(-0.62f, -0.05f) }, col);
                ci.DrawLine(P(-0.1f, -0.05f), P(0.95f, -0.62f), col, lw * 1.2f, true);
                ci.DrawCircle(P(0.95f, -0.62f), h * 0.14f, col);
                break;
            case Kind.HitSound: // a hit marker with a sound wave
                foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                    ci.DrawLine(P(-0.35f, 0) + d * h * 0.18f, P(-0.35f, 0) + d * h * 0.55f, col, lw, true);
                ci.DrawArc(P(0.25f, 0), h * 0.4f, -0.9f, 0.9f, 8, col, lw, true);
                ci.DrawArc(P(0.25f, 0), h * 0.75f, -0.9f, 0.9f, 10, col, lw, true);
                break;
            case Kind.Inspect: // an eye
            {
                var eye = new Vector2[13];
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12f * Mathf.Tau, x = Mathf.Cos(t), y = Mathf.Sin(t) * 0.55f * (1f - 0.35f * x * x);
                    eye[i] = P(x * 0.95f, y);
                }
                ci.DrawPolyline(eye, col, lw, true);
                ci.DrawCircle(c, h * 0.3f, col);
                break;
            }
            case Kind.Reload: // a magazine with a curved arrow round it
            {
                ci.FillPoly(new[] { P(-0.55f, -0.75f), P(-0.05f, -0.75f), P(0.1f, 0.8f), P(-0.4f, 0.8f) }, col);
                ci.DrawArc(P(0.2f, 0), h * 0.7f, -Mathf.Pi * 0.45f, Mathf.Pi * 0.35f, 10, col, lw, true);
                var mtip = P(0.2f, 0) + new Vector2(Mathf.Cos(-Mathf.Pi * 0.45f), Mathf.Sin(-Mathf.Pi * 0.45f)) * h * 0.7f;
                ci.FillPoly(new[] { mtip + new Vector2(-0.35f, -0.1f) * h, mtip + new Vector2(0.3f, -0.3f) * h, mtip + new Vector2(0.1f, 0.3f) * h }, col);
                break;
            }
            default: // equip flourish: a turning arrow
                ci.DrawArc(c, h * 0.7f, -Mathf.Pi * 0.25f, Mathf.Pi * 1.25f, 14, col, lw, true);
                var tip = c + new Vector2(Mathf.Cos(-Mathf.Pi * 0.25f), Mathf.Sin(-Mathf.Pi * 0.25f)) * h * 0.7f;
                ci.FillPoly(new[] { tip + new Vector2(-0.45f, -0.35f) * h, tip + new Vector2(0.4f, 0.1f) * h, tip + new Vector2(-0.25f, 0.45f) * h }, col);
                break;
        }
    }
}

/// <summary>
/// The selected skin line's LEVEL TRACK (skins2-design-1.19.md §4.10): one node per level (number, label, the icons of what
/// it adds, ✓ / padlock / "wearing"), joined by a line that lights up as far as the levels are unlocked, and a status line
/// under it for the focused item (the shown level or variant): what it adds when unlocked, else the requirement text and
/// progress from <see cref="Progress.Status"/>. Clicking a node calls <see cref="Picked"/> (preview, and equip when unlocked);
/// hovering one shows its details as a tooltip. One-level lines (Core, Signature) and Factory show a legend instead.
/// </summary>
public partial class SkinLevelTrack : Control
{
    public float K = 1f;
    /// <summary>The line shown (null = Factory).</summary>
    public SkinLine? Line;
    /// <summary>The level shown in the preview (1..MaxLevel).</summary>
    public int Shown = 1;
    /// <summary>The level the selected gun wears of this line (0 = it doesn't wear it).</summary>
    public int Worn;
    /// <summary>The item whose status the bottom line shows (a level or variant id; null = the shown level).</summary>
    public string? Focus;
    /// <summary>What the focus item is, translated ("Level 3", "Teal variant"); null = derived from <see cref="Focus"/>.</summary>
    public string? FocusName;
    /// <summary>A node was clicked (level number).</summary>
    public Action<int>? Picked;
    /// <summary>Brief warn flash of the status line (EQUIP pressed on a locked item).</summary>
    public float Flash;
    int hover = -1;

    const float NodesH = 88f; // design units: nodes + labels + icons; the status line sits below

    public SkinLevelTrack()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Linear;
    }

    bool HasTrack => Line is { MaxLevel: > 1 };

    /// <summary>Node centres for the current width (empty for one-level lines).</summary>
    List<Vector2> Nodes()
    {
        var list = new List<Vector2>();
        if (Line is not { MaxLevel: > 1 } l) return list;
        float k = K, margin = 56 * k;
        float step = Mathf.Min((Size.X - 2 * margin) / (l.MaxLevel - 1), 190 * k);
        float x0 = margin;
        for (int i = 0; i < l.MaxLevel; i++) list.Add(new Vector2(x0 + i * step, 17 * k));
        return list;
    }

    int NodeAt(Vector2 p)
    {
        var nodes = Nodes();
        if (nodes.Count == 0 || p.Y > NodesH * K) return -1;
        float half = nodes.Count > 1 ? (nodes[1].X - nodes[0].X) / 2 : 60 * K;
        for (int i = 0; i < nodes.Count; i++) if (Mathf.Abs(p.X - nodes[i].X) <= half) return i;
        return -1;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = NodeAt(mm.Position);
            if (h != hover) { hover = h; MouseDefaultCursorShape = h >= 0 ? CursorShape.PointingHand : CursorShape.Arrow; if (h >= 0) UiTheme.HoverSound(); QueueRedraw(); }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && NodeAt(mb.Position) is var n and >= 0)
        {
            UiTheme.ClickSound();
            Picked?.Invoke(n + 1);
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && hover >= 0) { hover = -1; QueueRedraw(); }
        if (what == NotificationResized) QueueRedraw();
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (Line is not { } l || NodeAt(atPosition) is not (var i and >= 0)) return "";
        var lv = l.Levels[i];
        var st = Progress.Status(ItemIds.Level(l.Id, lv.N));
        string tip = L.F($"Level {lv.N}") + " · " + L.T(lv.Blurb);
        if (lv.Adds != SkinFeature.None) tip += "\n" + SkinIcons.List(lv.Adds);
        tip += "\n" + (st.Unlocked ? L.T("Unlocked") : L.F($"Locked: {st.Summary}"));
        return tip;
    }

    public override void _Process(double delta)
    {
        if (Flash > 0f) { Flash = Mathf.Max(0f, Flash - (float)delta); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(1, 1, 1, 0.025f));
        if (Line is not { } l) { DrawFactory(); return; }
        if (!HasTrack) DrawSingle(l);
        else DrawNodes(l);
        DrawStatus(l, new Rect2(0, NodesH * k, Size.X, Size.Y - NodesH * k));
    }

    void DrawFactory()
    {
        float k = K;
        int fs = UiTheme.Fs(14, k), hs = UiTheme.Fs(12, k);
        Gfx.Text(this, UiTheme.HudWide, "FACTORY · NO UPGRADES", 16 * k, Gfx.Mid(24 * k, hs), hs, UiTheme.Dim);
        Gfx.TextFit(this, UiTheme.Body, "Signature skins add sounds, bullet and kill effects and an inspect; Apex skins level up to a finisher. Pick one on the right.",
            16 * k, Gfx.Mid(50 * k, fs), fs, UiTheme.Faint, Size.X - 32 * k);
    }

    /// <summary>Core / Signature: one level, so no track. Core: the tier's rule in a line. Signature: a legend of everything
    /// the one level brings (icon + label).</summary>
    void DrawSingle(SkinLine l)
    {
        float k = K;
        int hs = UiTheme.Fs(12, k), fs = UiTheme.Fs(14, k);
        string head = l.Edition == Edition.Signature ? L.T("ONE LEVEL · EVERYTHING INCLUDED") : L.T("ONE LEVEL · A FINISH");
        float x = 16 * k;
        x += SkinIcons.Pips(this, l.Edition, new Vector2(x, 24 * k), 12 * k) + 10 * k;
        Gfx.TextRaw(this, UiTheme.HudWide, L.Upper(L.T(SkinCatalog.EditionName(l.Edition))) + " · " + head, x, Gfx.Mid(24 * k, hs), hs, UiTheme.Dim);
        if (l.Edition != Edition.Signature)
        {
            Gfx.TextFit(this, UiTheme.Body, L.T("Core skins are a finish only: no levels, sounds or effects. Free from the start."), 16 * k, Gfx.Mid(50 * k, fs), fs, UiTheme.Faint, Size.X - 32 * k);
            return;
        }
        // the legend: every feature of the level, icon + label, wrapping onto a second row when narrow
        bool unlocked = GunSkins.TopLevel(l) >= 1;
        var col = unlocked ? UiTheme.Text : UiTheme.Dim;
        int ls = UiTheme.Fs(11, k), ns = UiTheme.Fs(12, k);
        float lx = 16 * k, ly = 50 * k, isz = 14 * k;
        foreach (var kd in SkinIcons.Kinds(l.FeaturesAt(1)))
        {
            string lab = L.Upper(L.T(SkinIcons.Label(kd)));
            float w = isz + 6 * k + Gfx.RawW(UiTheme.HudWide, lab, ls) + 16 * k;
            if (lx + w > Size.X - 12 * k && lx > 16 * k) { lx = 16 * k; ly += 20 * k; }
            SkinIcons.Draw(this, kd, new Vector2(lx + isz / 2, ly), isz, unlocked ? UiTheme.Teal : UiTheme.Faint);
            Gfx.TextRaw(this, UiTheme.HudWide, lab, lx + isz + 6 * k, Gfx.Mid(ly, ls), ls, col);
            lx += w;
        }
        if (ly < 60 * k)
            Gfx.TextFit(this, UiTheme.Body, L.T("No animation, finisher or special reload: those come with Apex skins."), 16 * k, Gfx.Mid(74 * k, ns), ns, UiTheme.Faint, Size.X - 32 * k);
    }

    void DrawNodes(SkinLine l)
    {
        float k = K;
        var nodes = Nodes();
        int top = GunSkins.TopLevel(l);
        float lw = Mathf.Max(1.5f, 2.5f * k);
        // the joining line: lit up to the highest unlocked level
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            var a = nodes[i] + new Vector2(16 * k, 0);
            var b = nodes[i + 1] - new Vector2(16 * k, 0);
            bool lit = i + 2 <= top;
            if (lit) DrawLine(a, b, UiTheme.Good, lw, true);
            else
                for (float x = a.X; x < b.X; x += 9 * k) DrawLine(new Vector2(x, a.Y), new Vector2(Mathf.Min(x + 4.5f * k, b.X), a.Y), UiTheme.Faint, Mathf.Max(1f, 1.5f * k));
        }
        int ns = UiTheme.Fs(15, k), ls = UiTheme.Fs(11, k);
        float half = nodes.Count > 1 ? (nodes[1].X - nodes[0].X) / 2 : 80 * k;
        for (int i = 0; i < nodes.Count; i++)
        {
            var c = nodes[i];
            var lv = l.Levels[i];
            bool unlocked = lv.N <= top, shown = lv.N == Shown, worn = lv.N == Worn, hov = i == hover;
            float rr = 14 * k * (shown ? 1.12f : 1f);
            var ec = SkinIcons.EditionColor(l.Edition);
            if (worn) Gfx.DiamondLine(this, c, rr + 5 * k, rr + 5 * k, UiTheme.Accent, Mathf.Max(1.5f, 2 * k));
            if (unlocked) Gfx.Diamond(this, c, rr, rr, shown ? ec : new Color(ec, 0.35f + 0.25f * (hov ? 1 : 0)));
            else Gfx.Diamond(this, c, rr, rr, new Color(0.1f, 0.13f, 0.16f));
            Gfx.DiamondLine(this, c, rr, rr, unlocked ? ec : new Color(UiTheme.Text, shown || hov ? 0.6f : 0.3f), Mathf.Max(1f, 1.5f * k));
            string num = lv.N.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (unlocked) Gfx.TextRaw(this, UiTheme.Display, num, c.X - Gfx.RawW(UiTheme.Display, num, ns) / 2, Gfx.Mid(c.Y, ns), ns, shown ? new Color(0.05f, 0.07f, 0.09f) : UiTheme.Text);
            else SkinIcons.Lock(this, c, 13 * k, new Color(UiTheme.Text, shown || hov ? 0.85f : 0.5f));
            // label: the number is in the node, the label says what it adds; a long one ("SOUNDS + KILL EFFECT") breaks
            // after a "+" onto a second line
            string label = L.Upper(L.T(lv.Blurb));
            var lc = shown ? UiTheme.Text : unlocked ? UiTheme.Dim : UiTheme.Faint;
            float lwMax = half * 2 - 8 * k;
            var parts = new List<string> { label };
            if (Gfx.RawW(UiTheme.HudWide, label, ls) > lwMax && label.Contains(' '))
            {
                // two lines, broken at the space that leaves the longer line shortest (a break before "+" or "&" keeps
                // the sign at the start of the second line)
                int best = -1;
                float bestW = float.MaxValue;
                for (int b = label.IndexOf(' '); b >= 0; b = label.IndexOf(' ', b + 1))
                {
                    float w1 = Gfx.RawW(UiTheme.HudWide, label[..b], ls), w2 = Gfx.RawW(UiTheme.HudWide, label[(b + 1)..], ls);
                    // prefer breaking before a joining sign: a small bonus so "SOUNDS + BULLET & KILL EFFECTS" splits at a sign
                    float score = Mathf.Max(w1, w2) - (label[(b + 1)..].StartsWith('+') || label[(b + 1)..].StartsWith('&') ? 6 * k : 0);
                    if (score < bestW) { bestW = score; best = b; }
                }
                if (best > 0) parts = new List<string> { label[..best], label[(best + 1)..] };
            }
            for (int p = 0; p < parts.Count; p++)
            {
                float tw = Mathf.Min(Gfx.RawW(UiTheme.HudWide, parts[p], ls), lwMax);
                float ly = parts.Count == 1 ? 46 * k : (40 + p * 13) * k;
                Gfx.TextFit(this, UiTheme.HudWide, L.Raw(parts[p]), c.X - tw / 2, Gfx.Mid(ly, ls), ls, lc, lwMax);
            }
            // icons of what this level adds
            var kinds = SkinIcons.Kinds(lv.Adds).ToList();
            float isz = 13 * k, gap = 19 * k, iy = 72 * k;
            float ix = c.X - (kinds.Count - 1) * gap / 2;
            var icol = unlocked ? (shown ? UiTheme.Text : UiTheme.Dim) : UiTheme.Faint;
            if (kinds.Count == 0) Gfx.TextRaw(this, UiTheme.HudWide, "—", c.X - Gfx.RawW(UiTheme.HudWide, "—", ls) / 2, Gfx.Mid(iy, ls), ls, UiTheme.Faint);
            foreach (var kd in kinds) { SkinIcons.Draw(this, kd, new Vector2(ix, iy), isz, icol); ix += gap; }
            if (shown) Gfx.Brackets(this, new Rect2(c.X - half + 6 * k, 0, half * 2 - 12 * k, NodesH * k - 4 * k), 7 * k, UiTheme.Accent, Mathf.Max(1, 1.5f * k));
        }
    }

    /// <summary>The focused item: what it adds when unlocked, else how to unlock it with its progress bar.</summary>
    void DrawStatus(SkinLine l, Rect2 r)
    {
        float k = K;
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 1)), new Color(UiTheme.Text, 0.08f));
        string item = Focus ?? ItemIds.Level(l.Id, Shown);
        var st = Progress.Status(item);
        int fs = UiTheme.Fs(14, k);
        float cy = r.Position.Y + r.Size.Y / 2;
        float x = 16 * k;
        string what = FocusName ?? (l.MaxLevel > 1 ? L.F($"Level {Shown}") : L.T(l.Name));
        if (st.Unlocked)
        {
            SkinIcons.Tick(this, new Vector2(x + 7 * k, cy), 14 * k, UiTheme.Good);
            x += 22 * k;
            var adds = l.MaxLevel > 1 && Focus == null ? l.Levels[Math.Clamp(Shown, 1, l.MaxLevel) - 1].Adds : SkinFeature.None;
            string text = what + " · " + L.T("Unlocked") + (adds != SkinFeature.None ? " · " + SkinIcons.List(adds) : "");
            Gfx.TextFit(this, UiTheme.Body, L.Raw(text), x, Gfx.Mid(cy, fs), fs, UiTheme.Dim, r.Size.X - x - 12 * k);
            return;
        }
        var warn = Flash > 0f ? UiTheme.Accent : UiTheme.Warn;
        SkinIcons.Lock(this, new Vector2(x + 7 * k, cy), 15 * k, warn);
        x += 22 * k;
        bool bar = st.Blocker == null && st.Progress01 > 0f;
        float barW = bar ? Mathf.Min(170 * k, r.Size.X * 0.25f) : 0f;
        float ptW = bar && st.ProgressText.Length > 0 ? Gfx.RawW(UiTheme.Hud, st.ProgressText, UiTheme.Fs(13, k)) + 10 * k : 0f;
        // with a bar the progress text sits next to it; without one it is part of the line (Summary)
        string line = what + " · " + (bar ? st.Blocker ?? st.Text : st.Summary);
        Gfx.TextFit(this, UiTheme.Body, L.Raw(line), x, Gfx.Mid(cy, fs), fs, warn, r.Size.X - x - barW - ptW - 24 * k);
        if (!bar) return;
        int ps = UiTheme.Fs(13, k);
        float bx = r.Size.X - 12 * k - barW;
        if (ptW > 0) Gfx.TextRaw(this, UiTheme.Hud, st.ProgressText, bx - ptW, Gfx.Mid(cy, ps), ps, UiTheme.Dim);
        var br = new Rect2(bx, cy - 3 * k, barW, 6 * k);
        DrawRect(br, new Color(UiTheme.Text, 0.1f));
        DrawRect(new Rect2(br.Position, new Vector2(barW * Mathf.Clamp(st.Progress01, 0f, 1f), br.Size.Y)), UiTheme.Warn);
    }
}
