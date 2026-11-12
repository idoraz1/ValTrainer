using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>
/// Unlock and level-up toasts (progression-design §10.4): queued, one at a time, 4 s each (hover holds it), top-right of
/// the results screen or a menu. Never during a drill or a Lock-In (held until after its hand-off) or over the reveal.
/// Three or more unlocks at once collapse into "3 UNLOCKS · VIEW". Click: the skin picker; EQUIP puts the skin on every gun
/// without its own skin. On the results screen they only show (the results own the clicks).
/// <para>One instance, a CanvasLayer under Main, created on first use (<see cref="Ensure"/>). Dev:
/// <c>--progress-demo-unlock id[,id…]</c> queues toasts for those items (<c>level</c> = a level-up toast).</para>
/// </summary>
public partial class ProgressToasts : CanvasLayer
{
    sealed record Entry(List<string> Items, int Level);

    static ProgressToasts? I;
    static bool devQueued;
    readonly List<Entry> queue = new();
    UnlockToast? shown;

    public static void Ensure()
    {
        if (I != null && IsInstanceValid(I)) return;
        I = new ProgressToasts { Layer = 60, Name = "ProgressToasts" };
        Callable.From(() => { if (I != null && IsInstanceValid(I) && !I.IsInsideTree()) Main.I.AddChild(I); }).CallDeferred();
        if (!devQueued && CmdLine.DevAfter("--progress-demo-unlock") is { } ids)
        {
            devQueued = true;
            var items = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (items.Remove("level")) I.queue.Add(new Entry(new(), Progress.Level));
            foreach (var id in items) I.queue.Add(new Entry(new() { id.ToLowerInvariant() }, 0));
        }
    }

    public override void _EnterTree()
    {
        Progress.Unlocked += OnUnlocked;
        Progress.LevelUp += OnLevelUp;
    }

    public override void _ExitTree()
    {
        Progress.Unlocked -= OnUnlocked;
        Progress.LevelUp -= OnLevelUp;
        if (I == this) I = null;
    }

    void OnUnlocked(UnlockInfo u)
    {
        // the reveal shows retro unlocks itself
        if (u.Source == "retro") return;
        queue.Add(new Entry(new() { u.ItemId }, 0));
    }

    void OnLevelUp(int from, int to)
    {
        // the results screen's XP row flashes level-ups itself
        if (CurrentScreen() is GameSession) return;
        queue.RemoveAll(e => e.Level > 0);
        queue.Add(new Entry(new(), to));
    }

    /// <summary>Main's current screen (the newest screen-like child).</summary>
    static Node? CurrentScreen()
    {
        Node? found = null;
        foreach (var c in Main.I.GetChildren())
            if (c is GameSession or ScreenBase && !c.IsQueuedForDeletion()) found = c;
        return found;
    }

    /// <summary>Whether a toast may show now (results, menus; never in a drill, a Lock-In or over the reveal).</summary>
    static bool Allowed(out bool interactive)
    {
        interactive = false;
        // a Lock-In holds them until after its hand-off (Progress.ToastsHeld: its steps credited XP)
        if (Warmup.WarmupRunner.Current != null || Progress.ToastsHeld && CurrentScreen() is GameSession || ProgressReveal.IsOpen) return false;
        switch (CurrentScreen())
        {
            case GameSession gs: return gs.State == GameSession.St.Results && !gs.Demo;
            case Warmup.LockInHandoff: return false;
            case ScreenBase: interactive = true; return true;
            default: return false;
        }
    }

    public override void _Process(double delta)
    {
        bool ok = Allowed(out bool interactive);
        if (shown != null && IsInstanceValid(shown))
        {
            shown.Visible = ok;
            shown.Interactive = interactive;
            if (ok) shown.Advance((float)delta);
            if (shown.Finished) { shown.QueueFree(); shown = null; }
            return;
        }
        shown = null;
        if (!ok || queue.Count == 0) return;
        // three or more unlocks waiting: one "N UNLOCKS" toast
        var unlocks = queue.Where(e => e.Level == 0).ToList();
        Entry next;
        if (unlocks.Count >= 3)
        {
            next = new Entry(unlocks.SelectMany(e => e.Items).Distinct().ToList(), 0);
            queue.RemoveAll(e => e.Level == 0);
        }
        else { next = queue[0]; queue.RemoveAt(0); }
        var vp = GetViewport().GetVisibleRect().Size;
        float k = UiTheme.Fit(vp);
        shown = new UnlockToast(next.Items, next.Level, k) { Interactive = interactive };
        AddChild(shown);
    }

    /// <summary>The toast's click / VIEW: the skin picker over the current screen, on <paramref name="item"/> when given.</summary>
    internal static void OpenPicker(float k, string? item = null)
    {
        if (CurrentScreen() is ScreenBase sb) SkinPicker.Open(sb, k, itemId: item);
    }
}

/// <summary>One toast: tier-coloured frame, swatch, "UNLOCKED · SIGNATURE", the name, EQUIP / LATER (or VIEW for a group).</summary>
public partial class UnlockToast : Control
{
    const float Life = 4f, In = 0.25f, Out = 0.3f;
    readonly List<string> items;
    readonly int level;
    readonly float k;
    float t;
    bool closing;
    float closeT;
    public bool Interactive = true;
    public bool Finished => closing && closeT >= Out;
    readonly List<VButton> buttons = new();

    public UnlockToast(List<string> items, int level, float k)
    {
        this.items = items;
        this.level = level;
        this.k = k;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    bool Group => items.Count > 1;

    public override void _Ready()
    {
        float w = 440 * k, h = 120 * k;
        // under the menu's header / a screen's heading and BACK button; at the very top over the results
        var screen = Main.I.GetChildren().LastOrDefault(c => c is GameSession or ScreenBase && !c.IsQueuedForDeletion());
        AnchorLeft = 1; AnchorRight = 1;
        OffsetLeft = -w - 28 * k; OffsetRight = -28 * k;
        OffsetTop = (screen is MenuScreen ? 182 : screen is GameSession ? 28 : 104) * k; OffsetBottom = OffsetTop + h;
        if (level == 0 && items.Count > 0)
        {
            if (Group) AddButton(L.N("VIEW"), VButton.Look.Primary, () => { foreach (var i in items) Progress.MarkSeen(i); ProgressToasts.OpenPicker(k); Close(); });
            else
            {
                AddButton(L.N("EQUIP"), VButton.Look.Primary, Equip);
                AddButton(L.N("LATER"), VButton.Look.Ghost, Close);
            }
        }
        SkinSwatches.Warm();
        string tip = level > 0 ? L.F($"Level {level}") : string.Join("\n", items.Select(ProgressText.ItemName));
        TooltipText = tip;
        UiTheme.ClickSound();
        Audio.Sfx.I?.Play("go", 0.25f, 1.4f);
    }

    void AddButton(string label, VButton.Look look, Action a)
    {
        var b = new VButton { Label = label, Kind = look, K = k, FontPx = 14, CustomMinimumSize = new Vector2(88 * k, 30 * k), Size = new Vector2(88 * k, 30 * k) };
        if (look == VButton.Look.Ghost) b.TitleFont = UiTheme.HudWide;
        b.Pressed += a;
        AddChild(b);
        buttons.Add(b);
        float x = 440 * k - 14 * k;
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            var bb = buttons[i];
            float bw = Mathf.Max(88 * k, bb.GetCombinedMinimumSize().X);
            x -= bw;
            bb.Position = new Vector2(x, 120 * k - 30 * k - 12 * k);
            bb.Size = new Vector2(bw, 30 * k);
            x -= 8 * k;
        }
    }

    void Equip()
    {
        var id = items[0];
        if (ItemIds.TryParse(id, out var line, out _, out var variant) && SkinCatalog.Get(line) != null)
            GunSkins.SetDefault(new SkinChoice(line, 0, variant).ToString());
        Progress.MarkSeen(id);
        Close();
    }

    void Close()
    {
        if (closing) return;
        closing = true;
        closeT = 0;
    }

    public void Advance(float dt)
    {
        foreach (var b in buttons) b.Visible = Interactive;
        MouseFilter = Interactive ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        t += dt;
        if (closing) closeT += dt;
        else if (t > Life && !GetGlobalRect().HasPoint(GetGlobalMousePosition())) Close();
        Modulate = new Color(1, 1, 1, closing ? 1 - Mathf.Clamp(closeT / Out, 0, 1) : Mathf.Clamp(t / In, 0, 1));
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!Interactive || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        AcceptEvent();
        foreach (var i in items) Progress.MarkSeen(i);
        if (items.Count > 0) ProgressToasts.OpenPicker(k, items.Count == 1 ? items[0] : null);
        else Main.I.ShowProgress();
        Close();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        float slide = (1 - Gfx.EaseOut(Mathf.Clamp(t / In, 0, 1))) * 40 * k;
        DrawSetTransform(new Vector2(slide, 0), 0, Vector2.One);
        Color col = level > 0 ? ProgressText.LevelColor(level) : Group ? UiTheme.Text : ProgressText.ItemColor(items[0]);
        Gfx.Plate(this, r, 12 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), new Color(col, 0.7f));
        DrawRect(new Rect2(0, 0, 4 * k, r.Size.Y - 8 * k), col);
        float x = 18 * k;
        int cs = UiTheme.Fs(12, k), ns = UiTheme.Fs(24, k), ss = UiTheme.Fs(13, k);
        if (level > 0)
        {
            ProgressDraw.LevelEmblem(this, new Vector2(x + 34 * k, r.Size.Y / 2), 32 * k, level, k, 1f, 0.6f + 0.4f * Mathf.Sin(t * 4));
            x += 84 * k;
            Gfx.Text(this, UiTheme.HudWide, "LEVEL UP", x, 34 * k, cs, col);
            Gfx.TextRaw(this, UiTheme.Display, L.F($"LEVEL {level}"), x, 66 * k, ns, UiTheme.Text);
            var next = Progress.NextUnlocks(1);
            if (next.Count > 0)
                Gfx.TextFit(this, UiTheme.Body, L.F($"Next: {ProgressText.ItemName(next[0].ItemId)}"), x, 90 * k, ss, UiTheme.Dim, r.Size.X - x - 14 * k);
        }
        else if (Group)
        {
            int n = items.Count;
            for (int i = 0; i < Math.Min(3, n); i++)
                ProgressDraw.Swatch(this, new Rect2(x + i * 14 * k, 26 * k + i * 6 * k, 74 * k, 50 * k), items[i], k, false);
            x += 74 * k + 2 * 14 * k + 16 * k;
            Gfx.Text(this, UiTheme.HudWide, "NEW SKINS", x, 32 * k, cs, UiTheme.Good);
            Gfx.TextRaw(this, UiTheme.Display, L.P(n, $"{n} UNLOCK", $"{n} UNLOCKS"), x, 62 * k, ns, UiTheme.Text);
            Gfx.TextFit(this, UiTheme.Body, string.Join(" · ", items.Take(4).Select(ProgressText.ItemName)), x, 86 * k, ss, UiTheme.Dim,
                r.Size.X - x - 14 * k - (Interactive ? 100 * k : 0));
        }
        else
        {
            var id = items[0];
            ProgressDraw.Swatch(this, new Rect2(x, 24 * k, 108 * k, 72 * k), id, k, false);
            x += 108 * k + 16 * k;
            string ed = ProgressText.EditionCaps(id);
            string cap = ed.Length > 0 ? L.T("UNLOCKED") + " · " + ed : L.T("UNLOCKED");
            Gfx.TextFit(this, UiTheme.HudWide, cap, x, 32 * k, cs, col, r.Size.X - x - 14 * k);
            Gfx.TextFit(this, UiTheme.Display, L.Upper(ProgressText.ItemName(id)), x, 62 * k, ns, UiTheme.Text, r.Size.X - x - 14 * k);
            if (!Interactive) Gfx.TextFit(this, UiTheme.Body, "Equip it from SKINS in the menu", x, 88 * k, ss, UiTheme.Dim, r.Size.X - x - 14 * k);
        }
        // time left
        if (!closing) DrawRect(new Rect2(4 * k, r.Size.Y - 2 * k, (r.Size.X - 4 * k) * Mathf.Clamp(1 - t / Life, 0, 1), 2 * k), new Color(col, 0.5f));
        DrawSetTransformMatrix(Transform2D.Identity);
    }
}

/// <summary>
/// The one-time reveal on the menu (progression-design §7): "YOUR TRAINING COUNTS", counters rolling up (runs, time,
/// personal bests, badge rank-ups, Lock-Ins), the level bar filling with a soft tick per level, the stars, then the unlocked
/// skins flipping in by tier. New players get the short welcome version. Esc closes; a click jumps to the end.
/// Shown after What's new and the updates prompt, before Support (<see cref="MenuScreen"/>). Dev:
/// <c>--progress-reveal demo|welcome</c> (made-up data / the welcome version; never saved).
/// </summary>
public partial class ProgressReveal : Control
{
    public static bool IsOpen { get; private set; }
    static bool devShown;

    readonly float k;
    readonly Action onClose;
    readonly bool welcome;
    readonly int runs, pbs, badgeUps, lockIns, level, stars, xp;
    readonly long secs;
    readonly List<string> lines;
    float t;
    int lastTick = 1;
    const float End = 7.6f;

    /// <summary>The dev flag's variant ("demo" / "welcome") until it was closed once; null otherwise.</summary>
    public static string? DevVariant() => devShown || CmdLine.DevAfter("--progress-reveal") is not { } v ? null : v.ToLowerInvariant();

    /// <summary>The dev reveal was closed: not again this session.</summary>
    public static void DevDone() => devShown = CmdLine.DevAfter("--progress-reveal") != null || devShown;

    /// <summary>The core has a reveal to show (Retro done and not shown yet; saved in progress.json once shown).</summary>
    public static bool Due => Progress.Reveal != null;

    public ProgressReveal(float k, Action onClose, string? dev = null)
    {
        this.k = k;
        this.onClose = onClose;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (dev == "demo")
        {
            runs = 187; secs = 12720; pbs = 34; badgeUps = 5; lockIns = 8; level = 19; stars = 12; xp = 26029;
            lines = Progress.Lines.Take(14).Select(l => l.Line).ToList();
        }
        else
        {
            var d = Progress.Data;
            var r = Progress.Reveal;
            welcome = dev == "welcome" || (r?.Welcome ?? (d.Retro?.Runs ?? 0) == 0);
            runs = r?.Runs ?? d.Counters.Runs;
            secs = r?.ActiveSecs ?? d.Counters.ActiveSecs;
            pbs = r?.Pbs ?? d.Counters.Pbs; badgeUps = r?.BadgeUps ?? d.Counters.BadgeUps; lockIns = r?.LockIns ?? d.Counters.LockIns;
            level = Progress.Level; stars = Progress.Stars; xp = Progress.Xp;
            lines = Progress.Lines.Where(l => Progress.IsUnlocked(l.Line)).Select(l => l.Line).ToList();
            if (welcome) { level = Math.Max(1, level); xp = Math.Max(xp, 250); }
        }
        // grouped by tier: Core, Signature, Apex (the richest flip in last)
        lines = lines.OrderBy(l => SkinCatalog.Get(l)?.Edition ?? Edition.Core).ToList();
    }

    public override void _EnterTree() => IsOpen = true;
    public override void _ExitTree() => IsOpen = false;

    public override void _Ready()
    {
        SkinSwatches.Warm();
        AddChild(new Backdrop());
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.55f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var draw = new DrawBox { OnDraw = DrawReveal };
        draw.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(draw);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", (int)(16 * k));
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        row.OffsetTop = -112 * k; row.OffsetBottom = -56 * k;
        if (welcome)
        {
            // the short version: the buttons right under its content
            row.AnchorTop = 0.5f; row.AnchorBottom = 0.5f;
            row.OffsetTop = 230 * k; row.OffsetBottom = 286 * k;
        }
        var open = new VButton { Label = "OPEN COLLECTION", Kind = VButton.Look.Secondary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(280 * k, 56 * k) };
        open.Pressed += () => { Close(); if (GetParent() is Node p) SkinPicker.Open(p, k); };
        row.AddChild(open);
        var go = new VButton { Label = "CONTINUE", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(240 * k, 56 * k) };
        go.Pressed += Close;
        row.AddChild(go);
        AddChild(row);
        Audio.Sfx.I?.Play("go", 0.3f, 1.1f);
    }

    void Close()
    {
        if (IsQueuedForDeletion()) return;
        Progress.MarkRetroShown(); // saved: never again, also after a restart
        IsOpen = false;
        QueueFree();
        onClose();
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        // a soft tick per level while the bar fills
        int shownLevel = LevelAt(t);
        if (shownLevel > lastTick && t < End) { lastTick = shownLevel; Audio.Sfx.I?.Play("tick", 0.18f, 1.2f + 0.02f * Math.Min(shownLevel, 30)); }
        foreach (var c in GetChildren()) if (c is CanvasItem ci) ci.QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { AcceptEvent(); if (t < End) { t = End; lastTick = level; } }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Escape or Key.Enter or Key.KpEnter or Key.Space)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode != Key.Escape && t < End) { t = End; lastTick = level; return; }
            Close();
        }
    }

    // timeline (s): counters 0.5–1.8, level 1.9–4.4, stars 4.5–5.4, skins 5.5–7.2
    static float P(float t, float a, float b) => Mathf.Clamp((t - a) / (b - a), 0, 1);
    int LevelAt(float tt) => welcome ? level : 1 + (int)((level - 1) * Gfx.EaseOut(P(tt, 1.9f, 4.4f)));

    void DrawReveal(DrawBox d)
    {
        var vs = d.Size;
        float cx = vs.X / 2, w = Mathf.Min(1180 * k, vs.X - 80 * k), x0 = cx - w / 2;
        float y = Mathf.Max(40 * k, vs.Y / 2 - (welcome ? 300 : 430) * k);
        float a = P(t, 0, 0.4f);
        int ts = UiTheme.Fs(78, k), ss = UiTheme.Fs(16, k);
        Gfx.TextC(d, UiTheme.HudWide, welcome ? "PROGRESSION IS HERE" : "VALTRAINER 1.19 · PROGRESSION", cx, y + 20 * k, ss, new Color(UiTheme.Accent, a));
        string title = welcome ? L.T("WELCOME") : L.T("YOUR TRAINING COUNTS");
        Gfx.TextFit(d, UiTheme.Display, title, x0, y + 96 * k, ts, new Color(UiTheme.Text, a), w, HorizontalAlignment.Center);
        string sub = welcome
            ? L.T("+250 XP to start. Your first skin is ready. Every run earns XP; real improvement earns stars")
            : L.T("Everything you played before 1.19 was credited with the same rules as from now on");
        Gfx.TextFit(d, UiTheme.Body, sub, x0, y + 132 * k, UiTheme.Fs(17, k), new Color(UiTheme.Dim, a), w, HorizontalAlignment.Center);
        y += 170 * k;

        if (!welcome)
        {
            // counters
            var cells = new (string Label, string Value)[]
            {
                (L.T("RUNS"), ProgressText.Num((int)(runs * Gfx.EaseOut(P(t, 0.5f, 1.6f))))),
                (L.T("TRAINED"), ProgressText.Duration((long)(secs * Gfx.EaseOut(P(t, 0.6f, 1.7f))))),
                (L.T("PERSONAL BESTS"), ProgressText.Num((int)(pbs * Gfx.EaseOut(P(t, 0.7f, 1.8f))))),
                (L.T("BADGE RANK-UPS"), ProgressText.Num((int)(badgeUps * Gfx.EaseOut(P(t, 0.8f, 1.8f))))),
                (L.T("LOCK-INS"), ProgressText.Num((int)(lockIns * Gfx.EaseOut(P(t, 0.9f, 1.8f))))),
            };
            float gap = 12 * k, cw = (w - gap * (cells.Length - 1)) / cells.Length, ch = 96 * k;
            for (int i = 0; i < cells.Length; i++)
            {
                float ca = P(t, 0.4f + i * 0.08f, 0.7f + i * 0.08f);
                var r = new Rect2(x0 + i * (cw + gap), y, cw, ch);
                Gfx.Plate(d, r, 10 * k, new Color(0.07f, 0.11f, 0.15f, 0.95f * ca), new Color(UiTheme.Text, 0.12f * ca));
                d.DrawRect(new Rect2(r.Position, new Vector2(28 * k, 2 * k)), new Color(UiTheme.Accent, ca));
                int vsz = UiTheme.Fs(40, k), lsz = UiTheme.Fs(12, k);
                Gfx.TextFit(d, UiTheme.Display, cells[i].Value, r.Position.X + 10 * k, r.Position.Y + 54 * k, vsz, new Color(UiTheme.Text, ca), cw - 20 * k, HorizontalAlignment.Center);
                Gfx.TextFit(d, UiTheme.HudWide, cells[i].Label, r.Position.X + 10 * k, r.Position.Y + 80 * k, lsz, new Color(UiTheme.Dim, ca), cw - 20 * k, HorizontalAlignment.Center);
            }
            y += ch + 34 * k;
        }

        // level
        float la = welcome ? a : P(t, 1.8f, 2.1f);
        int lv = LevelAt(t);
        float er = 38 * k;
        ProgressDraw.LevelEmblem(d, new Vector2(x0 + er, y + er), er, lv, k, la, welcome ? 0.5f : P(t, 4.3f, 4.6f) * (1 - P(t, 4.6f, 5.6f)));
        float bx = x0 + er * 2 + 24 * k, bw = w - (bx - x0);
        int ls = UiTheme.Fs(13, k);
        Gfx.Text(d, UiTheme.HudWide, "ACCOUNT LEVEL", bx, y + 22 * k, ls, new Color(UiTheme.Dim, la));
        string lvText = welcome ? L.F($"LEVEL {level} · {ProgressText.Num(xp)} / {ProgressText.Num(XpCurve.CostOf(level))} XP") : L.F($"LEVEL {lv}");
        Gfx.TextR(d, UiTheme.Display, lvText, x0 + w, y + 26 * k, UiTheme.Fs(26, k), new Color(UiTheme.Text, la));
        float frac = welcome ? Mathf.Clamp(xp / (float)XpCurve.CostOf(1), 0, 1) * Gfx.EaseOut(P(t, 0.5f, 1.5f)) : Gfx.EaseOut(P(t, 1.9f, 4.4f));
        ProgressDraw.XpBar(d, new Rect2(bx, y + 40 * k, bw, 14 * k), frac, 0, ProgressText.LevelColor(lv), la);
        if (!welcome)
        {
            // decade marks along the bar
            for (int m = 10; m < level; m += 10)
            {
                float mx = bx + bw * (m - 1) / Mathf.Max(1, level - 1);
                d.DrawRect(new Rect2(mx, y + 36 * k, 1, 22 * k), new Color(UiTheme.Text, 0.3f * la));
            }
            Gfx.Text(d, UiTheme.Hud, L.F($"{ProgressText.Num(xp)} XP"), bx, y + 76 * k, ls, new Color(UiTheme.Dim, la));
        }
        y += er * 2 + 28 * k;

        if (!welcome)
        {
            // stars
            float sa = P(t, 4.4f, 4.7f);
            int st = (int)(stars * Gfx.EaseOut(P(t, 4.5f, 5.4f)));
            ProgressDraw.Star(d, new Vector2(x0 + er, y + 18 * k), 22 * k, new Color(ProgressText.StarColor, sa));
            Gfx.TextRaw(d, UiTheme.Display, st.ToString(System.Globalization.CultureInfo.InvariantCulture), x0 + er * 2 + 24 * k, y + 34 * k, UiTheme.Fs(44, k), new Color(ProgressText.StarColor, sa));
            float sx = x0 + er * 2 + 24 * k + Gfx.RawW(UiTheme.Display, stars.ToString(System.Globalization.CultureInfo.InvariantCulture), UiTheme.Fs(44, k)) + 18 * k;
            Gfx.TextFit(d, UiTheme.Body, L.T("Stars come only from real improvement: real personal bests, badge rank-ups and confirmed coach rank-ups"),
                sx, y + 28 * k, UiTheme.Fs(16, k), new Color(UiTheme.Dim, sa), x0 + w - sx);
            y += 64 * k;
        }

        // skins
        float ka = welcome ? P(t, 0.8f, 1.2f) : P(t, 5.4f, 5.7f);
        int n = welcome ? Math.Min(1, lines.Count) : lines.Count;
        string head = welcome ? L.T("YOUR FIRST SKIN") : L.P(n, $"{n} SKIN UNLOCKED", $"{n} SKINS UNLOCKED");
        float hw = Gfx.RawW(UiTheme.DisplayWide, head, UiTheme.Fs(18, k)) + 32 * k;
        d.DrawRect(new Rect2(x0, y + 12 * k, w / 2 - hw / 2, 1), new Color(UiTheme.Text, 0.1f * ka));
        d.DrawRect(new Rect2(cx + hw / 2, y + 12 * k, w / 2 - hw / 2, 1), new Color(UiTheme.Text, 0.1f * ka));
        ProgressDraw.TextCRaw(d, UiTheme.DisplayWide, head, cx, Gfx.Mid(y + 12 * k, UiTheme.Fs(18, k)), UiTheme.Fs(18, k), new Color(UiTheme.Text, ka));
        y += 40 * k;
        if (n == 0) return;
        int perRow = welcome ? 1 : Math.Clamp((int)(w / (138 * k)), 4, 9), rows = welcome ? 1 : Math.Min(2, (n + perRow - 1) / perRow);
        int max = welcome ? 1 : perRow * rows, show = Math.Min(n, max);
        float sw = welcome ? 220 * k : (w - (perRow - 1) * 12 * k) / perRow, sh = sw * 0.62f;
        int lastRow = Math.Min(perRow, show - (rows - 1) * perRow);
        for (int i = 0; i < show; i++)
        {
            int ri = i / perRow, ci = i % perRow;
            int inRow = ri == rows - 1 ? lastRow : perRow;
            float rowW = inRow * sw + (inRow - 1) * 12 * k;
            float sx = cx - rowW / 2 + ci * (sw + 12 * k), sy = y + ri * (sh + 34 * k);
            float flip = welcome ? Gfx.EaseOutBack(P(t, 0.9f, 1.4f)) : Gfx.EaseOutBack(P(t, 5.5f + i * 0.08f, 5.8f + i * 0.08f));
            if (flip <= 0.01f) continue;
            string id = lines[i];
            float fw = sw * Mathf.Clamp(flip, 0, 1.1f);
            var sr = new Rect2(sx + (sw - fw) / 2, sy, fw, sh);
            ProgressDraw.Swatch(d, sr, id, k, false);
            if (flip > 0.6f)
            {
                var col = ProgressText.ItemColor(id);
                int ns2 = UiTheme.Fs(13, k);
                Gfx.TextFit(d, UiTheme.Hud, L.Upper(ProgressText.ItemName(id)), sx, sy + sh + 18 * k, ns2, UiTheme.Text, sw, HorizontalAlignment.Center);
                d.DrawRect(new Rect2(sx, sy + sh + 24 * k, sw, 2 * k), new Color(col, 0.8f));
            }
        }
        if (n > show)
            Gfx.TextC(d, UiTheme.HudWide, L.F($"+{n - show} MORE IN YOUR COLLECTION"), cx, y + rows * (sh + 34 * k) + 6 * k, UiTheme.Fs(13, k), new Color(UiTheme.Dim, ka));
    }
}
