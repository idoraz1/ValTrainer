using Godot;
using ValTrainer.Core;
using ValTrainer.World;
using Theme = ValTrainer.World.Theme;

namespace ValTrainer.UI;

/// <summary>
/// The room theme picker (Settings → Graphics → "Room theme"): one card per <see cref="RoomThemes"/> theme with a painted
/// preview of the room (sky, back wall, wainscot, floor, markings and a target in your enemy colour), its name and line,
/// and FREE / EQUIPPED / the level that unlocks it. Locked cards are dimmed with a padlock. Click / Enter equips (the next
/// training room uses it); arrows move; Esc / DONE closes. Dev: <c>--settings-themes</c> opens it on the Settings screen.
/// </summary>
public partial class ThemePicker : Control
{
    readonly float k;
    readonly Action? changed;
    readonly List<ThemeCard> cards = new();
    int focus;
    bool done;

    ThemePicker(float k, Action? changed)
    {
        this.k = k;
        this.changed = changed;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 10;
    }

    public static ThemePicker? Open(Node parent, float k, Action? changed = null)
    {
        if (parent.GetChildren().Any(c => c is ThemePicker tp && !tp.done)) return null;
        var p = new ThemePicker(k, changed);
        parent.AddChild(p);
        return p;
    }

    static AppSettings St => Main.I.Settings;

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.82f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var vs = GetViewportRect().Size;
        float w = Mathf.Min(1400 * k, vs.X - 40 * k);
        var panel = new VPanel
        {
            Title = "ROOM THEME", Caption = L.N("TRAINING ROOMS · AIM, AIM+ AND MOVEMENT DRILLS · MAPS KEEP THEIR OWN LOOK"), K = k,
            Fill = new Color(0.07f, 0.11f, 0.15f, 0.985f), CustomMinimumSize = new Vector2(w, 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(12 * k));
        panel.AddChild(col);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", (int)(12 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(12 * k));
        col.AddChild(grid);
        float cardH = Mathf.Clamp((vs.Y / k - 330f) / 2f, 170f, 250f);
        var chosen = RoomThemes.Chosen;
        foreach (var t in RoomThemes.All)
        {
            var card = new ThemeCard(t)
            {
                K = k, Unlocked = RoomThemes.IsUnlocked(t), Equipped = t == chosen,
                CustomMinimumSize = new Vector2(0, cardH * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = L.T(t.Name) + " · " + L.T(t.Blurb),
            };
            int i = cards.Count;
            card.Pressed += () => { Focus(i); Pick(t); };
            card.MouseEntered += () => Focus(i);
            cards.Add(card);
            grid.AddChild(card);
            if (t == chosen) focus = i;
        }
        cards[focus].Focused = true;

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(foot);
        var hint = new Label
        {
            Text = L.T("Level up by training to unlock more themes · cosmetic only, applies on the next drill"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            LabelSettings = UiTheme.Label(UiTheme.Body, 14, k, UiTheme.Faint), MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
        };
        foot.AddChild(hint);
        var close = new VButton { Label = "DONE", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(180 * k, 50 * k) };
        close.Pressed += Close;
        foot.AddChild(close);
    }

    void Focus(int i)
    {
        i = Math.Clamp(i, 0, cards.Count - 1);
        if (i == focus && cards[i].Focused) return;
        cards[focus].Focused = false;
        cards[focus].QueueRedraw();
        focus = i;
        cards[focus].Focused = true;
        cards[focus].QueueRedraw();
    }

    /// <summary>Equips <paramref name="t"/> (saved; the next training room is built with it). Locked themes do nothing.</summary>
    public void Pick(RoomTheme t)
    {
        if (done) return;
        if (!RoomThemes.IsUnlocked(t)) { UiTheme.HoverSound(); return; }
        St.RoomTheme = t.Key;
        St.Save();
        foreach (var c in cards) { c.Equipped = c.Room == t; c.QueueRedraw(); }
        changed?.Invoke();
    }

    void Close()
    {
        if (done) return;
        done = true;
        QueueFree();
        changed?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        if (done || e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Escape:
                if (!key.Echo) { UiTheme.ClickSound(); Close(); }
                break;
            case Key.Left or Key.A: Focus(focus - 1); break;
            case Key.Right or Key.D: Focus(focus + 1); break;
            case Key.Up or Key.W: Focus(focus - 4); break;
            case Key.Down or Key.S: Focus(focus + 4); break;
            case Key.Enter or Key.KpEnter or Key.Space:
                if (key.Echo) break;
                if (cards[focus].Unlocked) UiTheme.ClickSound();
                Pick(cards[focus].Room);
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled(); // the settings screen underneath never sees the picker's keys
    }
}

/// <summary>One theme: a painted preview of the room, the name and line, FREE / EQUIPPED / the unlock level.</summary>
public partial class ThemeCard : BaseButton
{
    public readonly RoomTheme Room;
    public float K = 1f;
    public bool Unlocked = true, Equipped, Focused;
    float hover;

    public ThemeCard(RoomTheme theme)
    {
        Room = theme;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready() => MouseEntered += () => { if (Unlocked) UiTheme.HoverSound(); };

    public override void _Process(double delta)
    {
        float t = IsHovered() || Focused ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        var fill = new Color(0.06f, 0.1f, 0.14f, 0.82f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.12f), hover * (Unlocked ? 1f : 0.4f));
        Gfx.Plate(this, r, 10 * k, fill, new Color(Equipped ? UiTheme.Accent : UiTheme.Text, Equipped ? 0.95f : 0.12f + 0.3f * hover), Equipped ? Mathf.Max(1, 2 * k) : 1f);
        if (Focused) Gfx.Brackets(this, r.Grow(-3 * k), 8 * k, new Color(UiTheme.Text, 0.9f), Mathf.Max(1, 2 * k));

        float pad = 10 * k;
        var pr = new Rect2(pad, pad, r.Size.X - pad * 2, r.Size.Y - 66 * k - pad);
        DrawPreview(this, pr, Room.Look, Main.I?.EnemyColor ?? new Color(1f, 0.2f, 0.2f));
        if (!Unlocked)
        {
            DrawRect(pr, new Color(0.02f, 0.04f, 0.06f, 0.55f));
            SkinIcons.Lock(this, pr.GetCenter() + new Vector2(0, -6 * k), 30 * k, new Color(UiTheme.Text, 0.9f));
        }

        int ns = UiTheme.Fs(20, k), bs = UiTheme.Fs(12.5f, k), ts = UiTheme.Fs(12, k);
        float ny = pr.End.Y + 22 * k;
        string tag = Equipped ? L.T("EQUIPPED") : !Unlocked ? L.F($"LEVEL {Room.Level}") : Room.Free ? L.T("FREE") : L.T("UNLOCKED");
        var tagCol = Equipped ? UiTheme.Accent : !Unlocked ? UiTheme.Warn : UiTheme.Good;
        float tw = Gfx.TextW(UiTheme.HudWide, tag, ts);
        Gfx.TextFit(this, UiTheme.Display, L.Upper(L.T(Room.Name)), pad, Gfx.Mid(ny, ns), ns, Unlocked ? UiTheme.Text : UiTheme.Dim, r.Size.X - pad * 3 - tw);
        Gfx.TextR(this, UiTheme.HudWide, tag, r.Size.X - pad, Gfx.Mid(ny, ts), ts, tagCol);
        string line = Unlocked ? L.T(Room.Blurb) : L.F($"Reach level {Room.Level} to unlock");
        Gfx.TextFit(this, UiTheme.Body, line, pad, Gfx.Mid(ny + 24 * k, bs), bs, Unlocked ? UiTheme.Dim : UiTheme.Faint, r.Size.X - pad * 2);
    }

    /// <summary>Approximate colour of each sky panorama straight up / at the horizon, before the theme's grade.</summary>
    static (Color Top, Color Horizon) SkyBase(string hdri) => hdri switch
    {
        Hdris.Overcast => (new Color(0.62f, 0.64f, 0.67f), new Color(0.8f, 0.81f, 0.83f)),
        Hdris.Qwantani => (new Color(0.45f, 0.58f, 0.78f), new Color(0.86f, 0.8f, 0.72f)),
        _ => (new Color(0.24f, 0.4f, 0.7f), new Color(0.66f, 0.74f, 0.86f)),
    };

    static Color Grade(Color c, Theme th)
    {
        float l = c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;
        var s = new Color(l, l, l).Lerp(c, th.SkySaturation);
        float e = Mathf.Clamp(th.SkyEnergy, 0f, 1.2f);
        return new Color(Mathf.Clamp(s.R * th.SkyTint.R * e, 0, 1), Mathf.Clamp(s.G * th.SkyTint.G * e, 0, 1), Mathf.Clamp(s.B * th.SkyTint.B * e, 0, 1));
    }

    /// <summary>A flat painting of the room as seen from the spawn: sky, the far wall with its wainscot and stripe, the
    /// side walls in perspective, the floor with its distance lines (and the neon grid), one target in the enemy colour.</summary>
    public static void DrawPreview(CanvasItem ci, Rect2 r, Theme th, Color enemy)
    {
        var (top, hor) = SkyBase(th.Hdri);
        Color skyTop = Grade(top, th), skyHor = Grade(hor, th);
        float x0 = r.Position.X, y0 = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        float horizon = y0 + h * 0.5f;
        // sky: vertical gradient
        const int Bands = 10;
        for (int i = 0; i < Bands; i++)
        {
            float a = i / (float)Bands, b = (i + 1) / (float)Bands;
            ci.DrawRect(new Rect2(x0, y0 + (horizon - y0) * a, w, (horizon - y0) * (b - a) + 1), skyTop.Lerp(skyHor, a));
        }
        // floor
        ci.DrawRect(new Rect2(x0, horizon, w, y0 + h - horizon), th.Floor.Tint);
        // far wall (centre) and side walls (trapezoids)
        float fw = w * 0.42f, fx = x0 + (w - fw) / 2, wallTop = horizon - h * 0.22f, baseTop = horizon - h * 0.03f;
        ci.DrawRect(new Rect2(fx, wallTop, fw, horizon - wallTop), th.Wall.Tint);
        var shade = th.Wall.Tint.Darkened(0.28f);
        ci.DrawColoredPolygon(new[] { new Vector2(x0, y0 + h * 0.1f), new Vector2(fx, wallTop), new Vector2(fx, horizon), new Vector2(x0, horizon + h * 0.04f) }, shade);
        ci.DrawColoredPolygon(new[] { new Vector2(x0 + w, y0 + h * 0.1f), new Vector2(fx + fw, wallTop), new Vector2(fx + fw, horizon), new Vector2(x0 + w, horizon + h * 0.04f) }, th.Wall.Tint.Darkened(0.12f));
        ci.DrawRect(new Rect2(fx, baseTop, fw, horizon - baseTop), th.Base.Tint);
        ci.DrawColoredPolygon(new[] { new Vector2(x0, horizon - h * 0.02f), new Vector2(fx, baseTop), new Vector2(fx, horizon), new Vector2(x0, horizon + h * 0.04f) }, th.Base.Tint);
        ci.DrawColoredPolygon(new[] { new Vector2(x0 + w, horizon - h * 0.02f), new Vector2(fx + fw, baseTop), new Vector2(fx + fw, horizon), new Vector2(x0 + w, horizon + h * 0.04f) }, th.Base.Tint);
        // back-wall stripe and floor distance lines
        float lw = Mathf.Max(1f, h * 0.012f);
        ci.DrawRect(new Rect2(fx, wallTop + (horizon - wallTop) * 0.45f, fw, lw), th.Marking);
        for (int i = 1; i <= 3; i++)
        {
            float y = horizon + (y0 + h - horizon) * (i * i) / 12f;
            ci.DrawRect(new Rect2(x0, y, w, lw * (1 + i * 0.4f)), th.Marking);
        }
        if (th.Grid is { } grid)
        {
            var vp = new Vector2(x0 + w / 2, horizon);
            for (int i = -5; i <= 5; i++) ci.DrawLine(vp, new Vector2(x0 + w / 2 + i * w * 0.1f, y0 + h), grid, lw); // stays inside the preview
            for (int i = 0; i <= 4; i++) ci.DrawLine(new Vector2(fx + fw * i / 4f, wallTop), new Vector2(fx + fw * i / 4f, baseTop), grid, lw);
        }
        // a target in front of the far wall
        var tc = new Vector2(fx + fw * 0.68f, wallTop + (horizon - wallTop) * 0.62f);
        float tr = Mathf.Max(4f, h * 0.07f);
        ci.DrawCircle(tc, tr, enemy);
        ci.DrawCircle(tc, tr - Mathf.Max(1.5f, tr * 0.22f), RoomThemes.BodyOf(th));
    }
}
