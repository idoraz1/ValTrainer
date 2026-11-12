using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>Drawing helpers shared by the progression widgets: level emblem, XP bar, star, skin swatch, small chips.</summary>
public static class ProgressDraw
{
    /// <summary>Five-point star (our own glyph: the UI fonts may not have ★).</summary>
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

    /// <summary>The account level: a flat-topped hexagon with the number; its border colour changes every 10 levels.</summary>
    public static void LevelEmblem(CanvasItem ci, Vector2 c, float r, int level, float k, float alpha = 1f, float glow = 0f)
    {
        var col = ProgressText.LevelColor(level);
        Span<Vector2> p = stackalloc Vector2[7];
        for (int i = 0; i < 6; i++) p[i] = c + new Vector2(Mathf.Cos(i * Mathf.Pi / 3), Mathf.Sin(i * Mathf.Pi / 3)) * r;
        p[6] = p[0];
        if (glow > 0.01f) ci.DrawCircle(c, r * 1.35f, new Color(col, 0.25f * glow * alpha));
        ci.FillPoly(p[..6], new Color(0.07f, 0.11f, 0.15f, 0.95f * alpha));
        ci.Polyline(p, new Color(col, alpha), Mathf.Max(1.5f, r * 0.13f), true);
        if (level >= 10)
        {
            // decade marks: one small tick per 10 levels (up to 5) under the number
            int ticks = Math.Min(5, level / 10);
            float tw = r * 0.16f, gap = r * 0.1f, tx = c.X - (ticks * tw + (ticks - 1) * gap) / 2;
            for (int i = 0; i < ticks; i++) ci.DrawRect(new Rect2(tx + i * (tw + gap), c.Y + r * 0.56f, tw, Mathf.Max(1, r * 0.08f)), new Color(col, alpha));
        }
        string n = ProgressText.Num(level);
        int fs = UiTheme.Fs(r * (n.Length >= 3 ? 0.8f : 1.0f), 1f);
        float w = Gfx.RawW(UiTheme.Display, n, fs);
        ci.DrawString(UiTheme.Display, new Vector2(c.X - w / 2, Gfx.Mid(c.Y - r * 0.04f, fs)), n, HorizontalAlignment.Left, -1, fs, new Color(UiTheme.Text, alpha));
    }

    /// <summary>XP bar: track, XP fill, rested XP as a second colour ahead of the fill, thin frame.</summary>
    public static void XpBar(CanvasItem ci, Rect2 r, float frac, float restedFrac, Color fill, float alpha = 1f)
    {
        frac = Mathf.Clamp(frac, 0, 1);
        ci.DrawRect(r, new Color(1, 1, 1, 0.07f * alpha));
        if (restedFrac > 0)
        {
            float rw = Mathf.Min(r.Size.X * (1 - frac), r.Size.X * restedFrac);
            if (rw > 0.5f) ci.DrawRect(new Rect2(r.Position.X + r.Size.X * frac, r.Position.Y, rw, r.Size.Y), new Color(ProgressText.RestedColor, 0.35f * alpha));
        }
        // (a sliver narrower than a pixel would be a degenerate polygon)
        if (r.Size.X * frac >= 1f) Gfx.HGradient(ci, new Rect2(r.Position, new Vector2(r.Size.X * frac, r.Size.Y)), new Color(fill.Darkened(0.25f), alpha), new Color(fill, alpha));
        ci.DrawRect(r, new Color(UiTheme.Text, 0.12f * alpha), false, 1f);
    }

    /// <summary>A plain progress bar (challenges, unlocks).</summary>
    public static void Bar(CanvasItem ci, Rect2 r, float frac, Color fill, float alpha = 1f)
    {
        ci.DrawRect(r, new Color(1, 1, 1, 0.07f * alpha));
        frac = Mathf.Clamp(frac, 0, 1);
        if (frac > 0) ci.DrawRect(new Rect2(r.Position, new Vector2(Mathf.Max(1, r.Size.X * frac), r.Size.Y)), new Color(fill, alpha));
    }

    /// <summary>The skin's rendered swatch (or its flat colours), dimmed with a padlock when <paramref name="locked"/>.</summary>
    public static void Swatch(CanvasItem ci, Rect2 r, string itemId, float k, bool locked)
    {
        var line = ProgressText.LineOf(itemId);
        string look = line == null ? GunSkins.FactoryId
            : ItemIds.TryParse(itemId, out var l, out _, out var v) && v != null ? ItemIds.Variant(l, v) : line.Id;
        var tex = SkinSwatches.Get(look) ?? SkinSwatches.Get(line?.Id ?? GunSkins.FactoryId);
        if (tex != null)
        {
            var ts = tex.GetSize();
            float s = Mathf.Max(r.Size.X / ts.X, r.Size.Y / ts.Y);
            ci.DrawTextureRectRegion(tex, r, new Rect2((ts - r.Size / s) / 2, r.Size / s));
        }
        else SkinSwatches.DrawFlat(ci, r, line?.Look);
        var col = ProgressText.ItemColor(itemId);
        ci.DrawRect(r, new Color(col, 0.8f), false, Mathf.Max(1, 1.5f * k));
        if (!locked) return;
        ci.DrawRect(r, new Color(0.02f, 0.03f, 0.05f, 0.55f));
        Padlock(ci, r.GetCenter(), k, UiTheme.Text);
    }

    public static void Padlock(CanvasItem ci, Vector2 c, float k, Color col)
    {
        float lw = Mathf.Max(1.5f, 1.6f * k);
        ci.DrawArc(c + new Vector2(0, -2 * k), 3.6f * k, Mathf.Pi, Mathf.Tau, 10, col, lw, true);
        ci.DrawRect(new Rect2(c + new Vector2(-5.5f, -2) * k, new Vector2(11, 8) * k), col);
    }

    /// <summary>A check mark (done).</summary>
    public static void Tick(CanvasItem ci, Vector2 c, float k, Color col)
    {
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = c + new Vector2(-6, 0) * k; p[1] = c + new Vector2(-2, 4.5f) * k; p[2] = c + new Vector2(6, -5) * k;
        ci.Polyline(p, col, Mathf.Max(1.5f, 2.4f * k), true);
    }

    /// <summary>Outlined chip; returns its width. <paramref name="text"/> is drawn as given (translate before).</summary>
    public static float Chip(CanvasItem ci, string text, float x, float cy, float k, Color c, int fs, bool filled = false, float maxW = float.MaxValue)
    {
        float w = Mathf.Min(Gfx.RawW(UiTheme.HudWide, text, fs) + 14 * k, maxW);
        var r = new Rect2(x, cy - 11 * k, w, 22 * k);
        ci.DrawRect(r, new Color(c, filled ? 0.85f : 0.12f));
        if (!filled) ci.DrawRect(r, new Color(c, 0.6f), false, 1f);
        if (Gfx.RawW(UiTheme.HudWide, text, fs) <= w - 12 * k) Gfx.TextRaw(ci, UiTheme.HudWide, text, x + 7 * k, Gfx.Mid(cy, fs), fs, filled ? UiTheme.Bg : c);
        else Gfx.TextFit(ci, UiTheme.HudWide, text, x + 7 * k, Gfx.Mid(cy, fs), fs, filled ? UiTheme.Bg : c, w - 12 * k);
        return w;
    }

    /// <summary>Centred text drawn exactly as given (numbers, already translated text): no lookup.</summary>
    public static void TextCRaw(CanvasItem ci, Font f, string t, float cx, float baseline, int size, Color c) =>
        ci.DrawString(f, new Vector2(cx - Gfx.RawW(f, t, size) / 2, baseline), t, HorizontalAlignment.Left, -1, size, c);
}

/// <summary>
/// The main menu's account strip (progression-design §10.1), in the header's status row left of the coach hint:
/// <c>[level] LV 14  [XP bar + rested]  1,240 / 1,800  ★ 9  ·  DAILY 2/3</c>. Click: the Progress screen; tooltip: the next
/// three unlocks. Redraws on <see cref="Progress.Changed"/>.
/// </summary>
public partial class AccountStrip : BaseButton
{
    public float K = 1f;
    /// <summary>XP bar width in design units (the strip's width follows it).</summary>
    public float BarW = 200f;
    float hover;

    public AccountStrip()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += () => { UiTheme.ClickSound(); Main.I.ShowProgress(); };
        UpdateTip();
    }

    public override void _EnterTree() => Progress.Changed += OnChanged;
    public override void _ExitTree() => Progress.Changed -= OnChanged;
    void OnChanged() { UpdateTip(); QueueRedraw(); }

    void UpdateTip()
    {
        var next = Progress.NextUnlocks(3);
        var lines = new List<string> { L.T("Progress: level, stars, daily challenges and skin unlocks") };
        if (next.Count > 0)
        {
            lines.Add("");
            lines.Add(L.T("Next unlocks:"));
            foreach (var s in next) lines.Add("· " + ProgressText.ItemName(s.ItemId) + " — " + s.Summary);
        }
        TooltipText = string.Join("\n", lines);
    }

    /// <summary>The width the strip needs (design: emblem, LV, bar, numbers, stars, daily).</summary>
    public float Need()
    {
        float k = K;
        int ls = UiTheme.Fs(12, k), ns = UiTheme.Fs(13, k);
        var v = ProgressView.Now();
        float w = 22 * k + 8 * k + Gfx.RawW(UiTheme.HudWide, LvText(), ls) + 10 * k + BarW * k + 10 * k
                  + Gfx.RawW(UiTheme.Hud, XpText(), ns) + 16 * k + 14 * k + Gfx.RawW(UiTheme.HudWide, Progress.Stars.ToString(), ls) + 12 * k;
        if (v.Daily.Count > 0) w += Gfx.RawW(UiTheme.HudWide, DailyText(v), ls) + 18 * k;
        return w + 12 * k;
    }

    static string LvText() => L.F($"LV {Progress.Level}");
    static string XpText() => ProgressText.Num(Progress.XpIntoLevel) + " / " + ProgressText.Num(Progress.XpForLevel);
    static string DailyText(ProgressView v) => L.F($"DAILY {v.DailyDone}/{v.Daily.Count}");

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, x = 0;
        int ls = UiTheme.Fs(12, k), ns = UiTheme.Fs(13, k);
        int level = Progress.Level;
        ProgressDraw.LevelEmblem(this, new Vector2(x + 11 * k, cy), 11 * k, level, k);
        x += 22 * k + 8 * k;
        string lv = LvText();
        Gfx.TextRaw(this, UiTheme.HudWide, lv, x, Gfx.Mid(cy, ls), ls, ProgressText.LevelColor(level).Lerp(UiTheme.Text, 0.4f + 0.4f * hover));
        x += Gfx.RawW(UiTheme.HudWide, lv, ls) + 10 * k;
        float need = Mathf.Max(1, Progress.XpForLevel);
        var bar = new Rect2(x, cy - 3 * k, BarW * k, 6 * k);
        ProgressDraw.XpBar(this, bar, Progress.XpIntoLevel / need, Progress.Rested / need, UiTheme.Accent.Lerp(UiTheme.AccentHover, hover));
        x = bar.End.X + 10 * k;
        string xp = XpText();
        Gfx.TextRaw(this, UiTheme.Hud, xp, x, Gfx.Mid(cy, ns), ns, UiTheme.Dim);
        x += Gfx.RawW(UiTheme.Hud, xp, ns) + 16 * k;
        ProgressDraw.Star(this, new Vector2(x + 6 * k, cy), 7 * k, ProgressText.StarColor);
        x += 14 * k;
        string st = Progress.Stars.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Gfx.TextRaw(this, UiTheme.HudWide, st, x, Gfx.Mid(cy, ls), ls, ProgressText.StarColor);
        x += Gfx.RawW(UiTheme.HudWide, st, ls) + 12 * k;
        var v = ProgressView.Now();
        if (v.Daily.Count > 0)
        {
            DrawCircle(new Vector2(x, cy), 1.6f * k, UiTheme.Faint);
            x += 8 * k;
            string dt = DailyText(v);
            bool all = v.DailyDone == v.Daily.Count;
            Gfx.TextFit(this, UiTheme.HudWide, dt, x, Gfx.Mid(cy, ls), ls, all ? UiTheme.Good : UiTheme.Text, Mathf.Max(10, Size.X - x));
            x += Gfx.RawW(UiTheme.HudWide, dt, ls);
        }
        if (hover > 0.01f) DrawRect(new Rect2(0, Size.Y - 1, Mathf.Min(Size.X, x), 1), new Color(UiTheme.Accent, hover));
    }
}

/// <summary>A small dot in the top-right corner of its parent (the menu's SKINS button) while there are unseen unlocks.</summary>
public partial class UnseenDot : Control
{
    public float K = 1f;
    public UnseenDot()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    public override void _EnterTree() => Progress.Changed += QueueRedraw;
    public override void _ExitTree() => Progress.Changed -= QueueRedraw;
    public override void _Draw()
    {
        int n = Progress.Unseen.Count;
        if (n == 0) return;
        var c = new Vector2(Size.X - 7 * K, 7 * K);
        DrawCircle(c, 6 * K, UiTheme.Bg);
        DrawCircle(c, 4.5f * K, UiTheme.Accent);
    }
}

/// <summary>
/// One challenge card on the Progress screen: title, why it was picked, progress bar with "2 / 3", the reward, and (as
/// child buttons, laid out by the card) PLAY (starts the drill) and REROLL (dailies, once a day).
/// </summary>
public partial class ChallengeCard : Control
{
    public float K = 1f;
    public ProgressView.Challenge C = null!;
    public bool Weekly;
    VButton? play, reroll;
    public Action? OnPlay, OnReroll;
    public bool CanReroll;

    public ChallengeCard() => MouseFilter = MouseFilterEnum.Pass;

    public override void _Ready()
    {
        float k = K;
        if (OnPlay != null && !C.Done)
        {
            play = new VButton { Label = "PLAY", Kind = VButton.Look.Primary, K = k, FontPx = 16, CustomMinimumSize = new Vector2(96 * k, 34 * k) };
            play.Pressed += () => OnPlay?.Invoke();
            AddChild(play);
        }
        if (OnReroll != null && !C.Done)
        {
            reroll = new VButton { Label = "REROLL", Kind = VButton.Look.Ghost, K = k, FontPx = 14, TitleFont = UiTheme.HudWide, CustomMinimumSize = new Vector2(84 * k, 34 * k) };
            reroll.Disabled = !CanReroll;
            reroll.TooltipText = CanReroll ? L.T("Swap this challenge for another one (once a day)") : L.T("No reroll left today");
            reroll.Pressed += () => OnReroll?.Invoke();
            AddChild(reroll);
        }
        Layout();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { Layout(); QueueRedraw(); }
    }

    float ButtonsW => (play != null ? play.GetCombinedMinimumSize().X + 10 * K : 0) + (reroll != null ? reroll.GetCombinedMinimumSize().X + 8 * K : 0);

    void Layout()
    {
        float k = K, x = Size.X - 16 * k, cy = Size.Y / 2;
        if (play != null)
        {
            var s = play.GetCombinedMinimumSize();
            x -= s.X;
            play.Position = new Vector2(x, cy - s.Y / 2);
            play.Size = s;
            x -= 10 * k;
        }
        if (reroll != null)
        {
            var s = reroll.GetCombinedMinimumSize();
            x -= s.X;
            reroll.Position = new Vector2(x, cy - s.Y / 2);
            reroll.Size = s;
        }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        bool done = C.Done;
        var accent = done ? UiTheme.Good : Weekly ? ProgressText.StarColor : UiTheme.Accent;
        Gfx.Plate(this, r, 10 * k, new Color(0.08f, 0.12f, 0.16f, done ? 0.6f : 0.9f), new Color(UiTheme.Text, 0.1f));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y), new Color(accent, done ? 0.6f : 1f));
        float x = 18 * k, right = r.Size.X - 16 * k - ButtonsW - 12 * k;
        // reward (right of the text block)
        int rs = UiTheme.Fs(13, k);
        string reward = L.F($"+{C.Xp} XP");
        float rw = Gfx.RawW(UiTheme.HudWide, reward, rs);
        float starW = C.Stars > 0 ? 30 * k : 0;
        float rx = right - rw - starW;
        Gfx.TextRaw(this, UiTheme.HudWide, reward, rx, Gfx.Mid(r.Size.Y * 0.3f, rs), rs, done ? UiTheme.Good : UiTheme.Text);
        if (C.Stars > 0)
        {
            ProgressDraw.Star(this, new Vector2(rx + rw + 12 * k, r.Size.Y * 0.3f), 7 * k, ProgressText.StarColor);
            Gfx.TextRaw(this, UiTheme.HudWide, "+" + C.Stars, rx + rw + 20 * k, Gfx.Mid(r.Size.Y * 0.3f, rs), rs, ProgressText.StarColor);
        }
        float textW = rx - x - 16 * k;
        int ts = UiTheme.Fs(18, k), ws = UiTheme.Fs(13, k);
        float titleY = C.Why != null ? r.Size.Y * 0.3f : r.Size.Y * 0.36f;
        Gfx.TextFit(this, UiTheme.Body, C.Title, x, Gfx.Mid(titleY, ts), ts, done ? UiTheme.Dim : UiTheme.Text, textW);
        if (C.Why != null) Gfx.TextFit(this, UiTheme.Body, C.Why, x, Gfx.Mid(r.Size.Y * 0.55f, ws), ws, UiTheme.Dim, textW);
        // progress
        float by = r.Size.Y * 0.8f;
        string prog = done ? L.T("DONE") : C.Prog ?? ProgressText.Num(Math.Min(C.Value, C.Target)) + " / " + ProgressText.Num(C.Target);
        int ps = UiTheme.Fs(12, k);
        float pw = Mathf.Max(Gfx.RawW(UiTheme.HudWide, prog, ps), 40 * k);
        var bar = new Rect2(x, by - 2.5f * k, Mathf.Max(20 * k, right - x - pw - 12 * k), 5 * k);
        ProgressDraw.Bar(this, bar, done ? 1f : C.Value / (float)Math.Max(1, C.Target), accent);
        if (done) ProgressDraw.Tick(this, new Vector2(bar.End.X + 14 * k, by), k, UiTheme.Good);
        Gfx.TextRaw(this, UiTheme.HudWide, prog, bar.End.X + (done ? 26 : 12) * k, Gfx.Mid(by, ps), ps, done ? UiTheme.Good : UiTheme.Dim);
    }
}

/// <summary>One "next unlock" row: swatch, name, tier, the requirement and a progress bar.</summary>
public partial class UnlockRow : BaseButton
{
    public float K = 1f;
    public UnlockStatus S = null!;
    float hover;

    public UnlockRow()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        TooltipText = ProgressText.ItemName(S.ItemId) + "\n" + S.Text + (S.ProgressText.Length > 0 ? "\n" + S.ProgressText : "");
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        var col = ProgressText.ItemColor(S.ItemId);
        DrawRect(r, new Color(0.08f, 0.12f, 0.16f, 0.9f).Lerp(new Color(0.16f, 0.2f, 0.25f, 0.95f), hover));
        DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y), col);
        float sw = r.Size.Y - 16 * k;
        var sr = new Rect2(12 * k, 8 * k, sw * 1.5f, sw);
        ProgressDraw.Swatch(this, sr, S.ItemId, k, !S.Unlocked);
        float x = sr.End.X + 14 * k, w = r.Size.X - x - 12 * k;
        int ns = UiTheme.Fs(17, k), es = UiTheme.Fs(11, k), qs = UiTheme.Fs(13, k);
        float y0 = r.Size.Y * 0.24f;
        string ed = ProgressText.EditionCaps(S.ItemId);
        float ew = ed.Length > 0 ? Gfx.RawW(UiTheme.HudWide, ed, es) : 0;
        Gfx.TextFit(this, UiTheme.Display, L.Upper(ProgressText.ItemName(S.ItemId)), x, Gfx.Mid(y0, ns), ns, UiTheme.Text, w - ew - 10 * k);
        if (ed.Length > 0) Gfx.TextR(this, UiTheme.HudWide, ed, r.Size.X - 12 * k, Gfx.Mid(y0, es), es, col);
        string req = S.Blocker ?? S.Text;
        Gfx.TextFit(this, UiTheme.Body, req, x, Gfx.Mid(r.Size.Y * 0.5f, qs), qs, UiTheme.Dim, w);
        float by = r.Size.Y * 0.77f;
        string pt = S.ProgressText;
        int ps = UiTheme.Fs(11.5f, k);
        float pw = pt.Length > 0 ? Gfx.RawW(UiTheme.HudWide, pt, ps) + 10 * k : 0;
        ProgressDraw.Bar(this, new Rect2(x, by - 2 * k, Mathf.Max(10 * k, w - pw), 4 * k), S.Progress01, col);
        if (pt.Length > 0) Gfx.TextR(this, UiTheme.HudWide, pt, r.Size.X - 12 * k, Gfx.Mid(by, ps), ps, UiTheme.Dim);
    }
}

/// <summary>
/// The level track (battle-pass view, progression-design §10.2): levels N−2 … N+8 as nodes on a line filled up to the
/// current XP, each with its reward chips (skin swatch + name; ★ when stars are needed too; NEW BORDER every 10 levels).
/// Scroll with the mouse wheel or the arrow buttons the screen puts beside it.
/// </summary>
public partial class LevelTrack : Control
{
    public float K = 1f;
    public int First;
    public const int Count = 11;
    float hoverX = -1;

    public LevelTrack()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public override void _Ready() => First = Math.Max(1, Progress.Level - 2);

    public void Shift(int d)
    {
        int f = Math.Max(1, First + d);
        if (f == First) return;
        First = f;
        UiTheme.ClickSound();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelLeft) { Shift(-1); AcceptEvent(); }
            else if (mb.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelRight) { Shift(1); AcceptEvent(); }
        }
        else if (e is InputEventMouseMotion mm) { hoverX = mm.Position.X; UpdateTip(); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
        if (what == NotificationMouseExit) { hoverX = -1; QueueRedraw(); }
    }

    float Step => Size.X / Count;

    /// <summary>"★ 3" badge whose top-right corner is <paramref name="topRight"/> (stars also needed).</summary>
    void StarBadge(Vector2 topRight, int stars, int fs, float k)
    {
        float w = StarBadgeW(stars, fs, k);
        var r = new Rect2(topRight.X - w, topRight.Y, w, 18 * k);
        DrawRect(r, new Color(0.04f, 0.06f, 0.09f, 0.92f));
        DrawRect(r, new Color(ProgressText.StarColor, 0.7f), false, 1f);
        ProgressDraw.Star(this, new Vector2(r.Position.X + 9 * k, r.GetCenter().Y), 5.5f * k, ProgressText.StarColor);
        Gfx.TextRaw(this, UiTheme.HudWide, stars.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Position.X + 17 * k, Gfx.Mid(r.GetCenter().Y, fs), fs, ProgressText.StarColor);
    }

    static float StarBadgeW(int stars, int fs, float k) => 22 * k + Gfx.RawW(UiTheme.HudWide, stars.ToString(System.Globalization.CultureInfo.InvariantCulture), fs);

    void UpdateTip()
    {
        int i = (int)(hoverX / Step);
        int level = First + Math.Clamp(i, 0, Count - 1);
        var rewards = ProgressText.RewardsAt(level);
        var lines = new List<string> { L.F($"Level {level}") + (level <= Progress.Level ? " ✓" : "") };
        foreach (var (id, stars) in rewards)
            lines.Add("· " + ProgressText.ItemName(id) + (stars > 0 ? "  " + L.P(stars, $"+ {stars} star", $"+ {stars} stars") : "")
                      + (Progress.IsUnlocked(id) ? "  ✓" : ""));
        if (level % 10 == 0) lines.Add("· " + L.T("A new level border"));
        if (rewards.Count == 0 && level % 10 != 0) lines.Add(L.T("Level-up XP keeps your bar moving toward the next skin"));
        string tip = string.Join("\n", lines);
        if (TooltipText != tip) TooltipText = tip;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float k = K, step = Step, lineY = 26 * k;
        int cur = Progress.Level;
        float need = Mathf.Max(1, Progress.XpForLevel), into = Progress.XpIntoLevel / need;
        // the line: done part red, the current level's progress, the rest dim
        DrawRect(new Rect2(0, lineY - 1.5f * k, Size.X, 3 * k), new Color(1, 1, 1, 0.08f));
        float doneX = (cur - First) * step + step / 2 + into * step;
        if (doneX > 0) DrawRect(new Rect2(0, lineY - 1.5f * k, Mathf.Min(Size.X, doneX), 3 * k), UiTheme.Accent);
        int ls = UiTheme.Fs(12, k), cs = UiTheme.Fs(13, k);
        for (int i = 0; i < Count; i++)
        {
            int level = First + i;
            float cx = i * step + step / 2;
            bool reached = level <= cur, isCur = level == cur;
            if (hoverX >= i * step && hoverX < (i + 1) * step) DrawRect(new Rect2(i * step + 2 * k, 0, step - 4 * k, Size.Y), new Color(1, 1, 1, 0.03f));
            if (isCur || level % 10 == 0) ProgressDraw.LevelEmblem(this, new Vector2(cx, lineY), (isCur ? 17 : 13) * k, level, k, reached ? 1f : 0.6f, isCur ? 1f : 0f);
            else
            {
                Gfx.Diamond(this, new Vector2(cx, lineY), 7 * k, 7 * k, reached ? UiTheme.Accent : UiTheme.Panel2);
                Gfx.DiamondLine(this, new Vector2(cx, lineY), 7 * k, 7 * k, reached ? UiTheme.Accent : UiTheme.Border, 1f);
                ProgressDraw.TextCRaw(this, UiTheme.HudWide, ProgressText.Num(level), cx, lineY - 14 * k, ls, reached ? UiTheme.Text : UiTheme.Dim);
            }
            // rewards: up to two chips under the node
            var rewards = ProgressText.RewardsAt(level);
            float cy = lineY + 34 * k, cw = step - 14 * k, ch = Mathf.Max(30 * k, (Size.Y - cy - 6 * k - (rewards.Count > 1 ? 6 * k : 0)) / Math.Max(1, Math.Min(2, rewards.Count)));
            int shown = 0;
            foreach (var (id, stars) in rewards)
            {
                if (shown == 2) break;
                var box = new Rect2(cx - cw / 2, cy + shown * (ch + 6 * k), cw, ch);
                bool unlocked = Progress.IsUnlocked(id);
                var col = ProgressText.ItemColor(id);
                DrawRect(box, new Color(0.08f, 0.12f, 0.16f, reached ? 0.95f : 0.75f));
                DrawRect(new Rect2(box.Position, new Vector2(box.Size.X, 2 * k)), new Color(col, reached ? 1f : 0.6f));
                string name = ProgressText.ItemShort(id);
                int ss = UiTheme.Fs(11, k);
                if (box.Size.Y >= 52 * k)
                {
                    // tall chip: the swatch across the top, the name under it (full width)
                    var sr = new Rect2(box.Position + new Vector2(4 * k, 6 * k), new Vector2(box.Size.X - 8 * k, box.Size.Y - 30 * k));
                    ProgressDraw.Swatch(this, sr, id, k, !unlocked);
                    Gfx.TextFit(this, UiTheme.Hud, name, box.Position.X + 5 * k, Gfx.Mid(box.End.Y - 12 * k, cs), cs, unlocked ? UiTheme.Text : UiTheme.Dim,
                        box.Size.X - 10 * k, HorizontalAlignment.Center);
                    if (stars > 0) StarBadge(new Vector2(sr.End.X, sr.Position.Y), stars, ss, k);
                }
                else
                {
                    // two rewards: swatch left; the line's name over its level (or variant) and the stars needed, so the
                    // full name fits the narrow chip
                    float sh = box.Size.Y - 8 * k;
                    var sr = new Rect2(box.Position + new Vector2(4 * k, 5 * k), new Vector2(Mathf.Min(sh * 1.3f, box.Size.X * 0.3f), sh - 2 * k));
                    ProgressDraw.Swatch(this, sr, id, k, !unlocked);
                    float tx = sr.End.X + 6 * k, tw = box.End.X - tx - 4 * k, mid = box.GetCenter().Y;
                    ItemIds.TryParse(id, out _, out int lv, out var vid);
                    string top = ProgressText.LineOf(id) is { } ln ? L.T(ln.Name) : name;
                    string tag = vid != null ? L.Upper(name) : lv > 1 ? L.F($"LV {lv}") : "";
                    int ns = UiTheme.Fs(12, k), ts = UiTheme.Fs(10, k);
                    Gfx.TextFit(this, UiTheme.Hud, top, tx, Gfx.Mid(mid - 5.5f * k, ns), ns, unlocked ? UiTheme.Text : UiTheme.Dim, tw);
                    string st = stars > 0 ? stars.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                    float starW = stars > 0 ? 12 * k + Gfx.RawW(UiTheme.HudWide, st, ts) : 0;
                    float x2 = tx;
                    if (tag.Length > 0)
                    {
                        float tagW = Mathf.Min(Gfx.RawW(UiTheme.HudWide, tag, ts), tw - starW - 6 * k);
                        Gfx.TextFit(this, UiTheme.HudWide, tag, tx, Gfx.Mid(mid + 7 * k, ts), ts, UiTheme.Dim, tagW);
                        x2 += tagW + 6 * k;
                    }
                    if (stars > 0)
                    {
                        ProgressDraw.Star(this, new Vector2(x2 + 4 * k, mid + 7 * k), 4.5f * k, ProgressText.StarColor);
                        Gfx.TextRaw(this, UiTheme.HudWide, st, x2 + 10 * k, Gfx.Mid(mid + 7 * k, ts), ts, ProgressText.StarColor);
                    }
                }
                shown++;
            }
            if (rewards.Count > 2)
                ProgressDraw.TextCRaw(this, UiTheme.HudWide, "+" + (rewards.Count - 2), cx + cw / 2 - 10 * k, Size.Y - 4 * k, UiTheme.Fs(10, k), UiTheme.Dim);
            if (rewards.Count == 0)
            {
                string t = level % 10 == 0 ? L.T("NEW BORDER") : "·";
                ProgressDraw.TextCRaw(this, UiTheme.HudWide, t, cx, cy + 22 * k, UiTheme.Fs(11, k), level % 10 == 0 ? ProgressText.LevelColor(level) : UiTheme.Faint);
            }
        }
    }
}

/// <summary>
/// The results screen's XP row (progression-design §10.3), drawn by <see cref="Hud"/> in one 44-px row above the buttons:
/// <c>+186 XP  [LV 14 bar animating BarBefore → BarAfter]  RUN 99 · FIRST TODAY +150 · ★ +1 REAL PB  ·  Head Flicks 2/3</c>.
/// A level-up flashes "LEVEL 15" on the bar for 1.2 s (with a sound). Hover: the full breakdown (tooltip).
/// </summary>
public sealed class ResultsXpRow
{
    readonly RunXp run;
    readonly string total, tip;
    readonly List<(string Text, Color Col, bool Star)> chips = new();
    bool levelSound;

    /// <summary>Builds the row's text once, in the UI language (the results don't change while they are up).</summary>
    public ResultsXpRow(RunXp x)
    {
        run = x;
        total = L.F($"+{x.Total} XP");
        if (x.StarsGained > 0)
            chips.Add(("+" + x.StarsGained + (x.StarReasons.Count > 0 ? " " + L.Upper(x.StarReasons[0]) : ""), ProgressText.StarColor, true));
        foreach (var p in x.Parts) chips.Add((ProgressText.PartLabel(p.Label) + " +" + ProgressText.Num(p.Xp), UiTheme.Dim, false));
        foreach (var c in x.Challenges) chips.Add(($"{c.Title} {Math.Min(c.Value, c.Target)}/{c.Target}", c.Done ? UiTheme.Good : UiTheme.Text, false));
        var lines = new List<string> { total };
        foreach (var p in x.Parts) lines.Add($"· {L.T(p.Label)}  +{ProgressText.Num(p.Xp)}");
        foreach (var s in x.StarReasons) lines.Add("★ " + s);
        foreach (var c in x.Challenges) lines.Add((c.Done ? "✓ " : "· ") + $"{c.Title} {Math.Min(c.Value, c.Target)}/{c.Target}");
        if (x.LevelAfter > x.LevelBefore) lines.Add(L.F($"Level {x.LevelAfter} reached"));
        tip = string.Join("\n", lines);
    }

    /// <summary>Dev <c>--progress-demo-run [levelup]</c>: a made-up breakdown on the results screen (the core's real one
    /// wins when present).</summary>
    public static RunXp? Demo()
    {
        if (!CmdLine.Dev || !CmdLine.Has("--progress-demo-run")) return null;
        bool up = CmdLine.After("--progress-demo-run") == "levelup";
        int lv = Progress.Level;
        float before = up ? 0.82f : Math.Clamp(Progress.XpIntoLevel / (float)Math.Max(1, Progress.XpForLevel), 0, 0.7f);
        var parts = new List<XpPart> { new(L.N("Run"), 99), new(L.N("Veteran"), 20), new(L.N("First today"), 150), new(L.N("PB"), 50) };
        if (up) parts.Add(new(L.N("Rested"), 99));
        // dev demo: the star reason and tick titles are English data (the core sends translated ones)
        return new RunXp(parts.Sum(p => p.Xp), parts, 1, new[] { "Real PB (+4.1%)" }, lv, up ? lv + 1 : lv, before, up ? 0.12f : before + 0.18f,
            new[] { new ChallengeTick(ProgressText.DrillName("flick") ?? "Flick", 2, 3, false), new ChallengeTick("Different drills", 3, 3, true) },
            Array.Empty<string>());
    }

    public string Tooltip() => tip;

    /// <summary>Draws the row in <paramref name="r"/> (absolute), <paramref name="t"/> seconds after the results appeared.</summary>
    public void Draw(CanvasItem d, Rect2 r, float k, float t)
    {
        var xr = run;
        float cy = r.GetCenter().Y, x0 = r.Position.X, x1 = r.End.X;
        d.DrawRect(r, new Color(1, 1, 1, 0.025f));
        d.DrawRect(new Rect2(r.Position, new Vector2(3 * k, r.Size.Y)), UiTheme.Accent);
        // total (counts up)
        int ts = UiTheme.Fs(26, k);
        float appear = Mathf.Clamp((t - 0.35f) / 0.5f, 0, 1);
        string shown = appear >= 1 ? total : L.F($"+{(int)(xr.Total * Gfx.EaseOut(appear))} XP");
        Gfx.TextRaw(d, UiTheme.Display, shown, x0 + 14 * k, Gfx.Mid(cy, ts), ts, UiTheme.Text);
        float x = x0 + 14 * k + Gfx.RawW(UiTheme.Display, total, ts) + 18 * k;
        // level + bar: fills BarBefore → BarAfter; a level-up fills to the end, flashes, then goes on from 0
        bool up = xr.LevelAfter > xr.LevelBefore;
        float frac;
        int level = xr.LevelBefore;
        float flash = 0;
        if (!up) frac = Mathf.Lerp(xr.BarBefore, xr.BarAfter, Gfx.EaseOut(Mathf.Clamp((t - 0.6f) / 0.9f, 0, 1)));
        else
        {
            const float tUp = 1.05f; // the bar reaches the end
            if (t < tUp) frac = Mathf.Lerp(xr.BarBefore, 1f, Mathf.Clamp((t - 0.6f) / 0.45f, 0, 1));
            else
            {
                level = xr.LevelAfter;
                flash = Mathf.Clamp(1f - (t - tUp) / 1.2f, 0, 1);
                if (!levelSound) { levelSound = true; Audio.Sfx.I?.Play("go", 0.35f, 1.25f); }
                frac = Mathf.Lerp(0f, xr.BarAfter, Gfx.EaseOut(Mathf.Clamp((t - tUp - 0.5f) / 0.6f, 0, 1)));
            }
        }
        ProgressDraw.LevelEmblem(d, new Vector2(x + 14 * k, cy), 14 * k, level, k, 1f, flash);
        x += 36 * k;
        var bar = new Rect2(x, cy - 4 * k, 190 * k, 8 * k);
        ProgressDraw.XpBar(d, bar, frac, 0, UiTheme.Accent.Lerp(ProgressText.LevelColor(level), flash));
        if (flash > 0)
        {
            int fs = UiTheme.Fs(15, k);
            string lv = L.F($"LEVEL {xr.LevelAfter}");
            float w = Gfx.RawW(UiTheme.DisplayWide, lv, fs) + 16 * k;
            var fr = new Rect2(bar.GetCenter().X - w / 2, cy - 12 * k, w, 24 * k);
            float a = Mathf.Min(1, flash * 3);
            d.DrawRect(fr, new Color(ProgressText.LevelColor(level), 0.9f * a));
            Gfx.TextRaw(d, UiTheme.DisplayWide, lv, fr.Position.X + 8 * k, Gfx.Mid(cy, fs), fs, new Color(UiTheme.Bg, a));
        }
        x = bar.End.X + 18 * k;
        // chips: star, parts, challenges (whatever fits; the tooltip has everything)
        int cs = UiTheme.Fs(11.5f, k);
        float chipA = Mathf.Clamp((t - 0.9f) / 0.4f, 0, 1);
        if (chipA <= 0) return;
        float limit = x1 - 12 * k;
        for (int i = 0; i < chips.Count; i++)
        {
            var (text, col, star) = chips[i];
            float need = Gfx.RawW(UiTheme.HudWide, text, cs) + 14 * k + (star ? 14 * k : 0);
            if (x + need > limit - (i < chips.Count - 1 ? 44 * k : 0))
            {
                ProgressDraw.Chip(d, "+" + (chips.Count - i), x, cy, k, new Color(UiTheme.Faint, chipA), cs);
                break;
            }
            var c = new Color(col, col.A * chipA);
            if (star)
            {
                var rr = new Rect2(x, cy - 11 * k, need, 22 * k);
                d.DrawRect(rr, new Color(ProgressText.StarColor, 0.16f * chipA));
                d.DrawRect(rr, new Color(ProgressText.StarColor, 0.8f * chipA), false, 1f);
                ProgressDraw.Star(d, new Vector2(x + 11 * k, cy), 6 * k, c);
                Gfx.TextRaw(d, UiTheme.HudWide, text, x + 20 * k, Gfx.Mid(cy, cs), cs, c);
            }
            else ProgressDraw.Chip(d, text, x, cy, k, c, cs);
            x += need + 8 * k;
        }
    }
}
