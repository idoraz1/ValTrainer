using System.Globalization;
using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>
/// Settings, two pages. GENERAL: trainer overrides on the left/middle (applied immediately and saved), everything
/// imported from Valorant (read-only) on the right with a re-import button. ABOUT: version, renderer/GPU, data and log
/// folders, update checks, bug reports and the open-source licenses. Esc / Back returns to the menu.
/// Narrow windows (4:3, 16:10) stack each row's label above its control so nothing gets cut off.
/// </summary>
public partial class SettingsScreen : ScreenBase
{
    public enum Page { General, About }

    /// <summary>Page the next settings screen opens on (dev: --screen about).</summary>
    public static Page StartPage = Page.General;

    Page page;
    bool stacked;
    int updateRev = -1;
    float copyFlash;

    public SettingsScreen()
    {
        page = StartPage;
        StartPage = Page.General;
    }

    static readonly string[] WindowModes = { "Fullscreen", "Windowed Fullscreen", "Windowed" };
    static readonly int[] FpsCaps = { 0, 60, 144, 165, 240, 360 };
    static readonly int[] Dpis = { 400, 800, 1000, 1200, 1600, 2000, 3200 };
    static readonly string[] Qualities = { "Competitive (Low)", "Medium", "High", "Ultra" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string AaName(int v) => v switch { -1 => "Default", 0 => "None", 1 => "MSAA 2x", 2 => "MSAA 4x", 3 => "FXAA", _ => v.ToString() };
    static string Q(int v) => v switch { -1 => "Default", 0 => "Low", 1 => "Medium", 2 => "High", 3 => "Ultra", _ => v.ToString() };
    static string B(bool? b) => b == null ? "Default" : b.Value ? "On" : "Off";
    static int Wrap(int v, int min, int max) => v < min ? max : v > max ? min : v;

    readonly List<Action> refreshers = new();
    float reimportFlash;
    DrawBox? reimportNote;

    static Main App => Main.I;
    static AppSettings St => Main.I.Settings;

    protected override void Build()
    {
        refreshers.Clear();
        float k = K;
        // Column content width (3 panels, ratios 1 : 1 : 1.05, minus padding and scrollbar) in 1080p design units:
        // below ~480 the label + 250-wide selector side by side would cut labels off, so stack them.
        float vw = GetViewportRect().Size.X;
        stacked = ((vw - 140 * k) / 3.05f - 54 * k) / k < 480;
        var root = Margins(VBox(18 * k), 48 * k, 30 * k, 48 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var title = VBox(2 * k);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.AddChild(Lbl("SETTINGS", UiTheme.Display, 64, UiTheme.Text));
        var sub = Lbl(page == Page.General ? "CHANGES APPLY IMMEDIATELY AND ARE SAVED · ANYTHING LEFT ON \"VALORANT\" FOLLOWS YOUR GAME"
            : "VERSION · UPDATES · FOLDERS · OPEN-SOURCE LICENSES", UiTheme.HudWide, 15, UiTheme.Dim);
        sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; // never pushes the tabs / BACK off screen
        sub.ClipText = true;
        title.AddChild(sub);
        head.AddChild(title);
        var tabs = HBox(6 * k);
        tabs.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foreach (var (pg, name) in new[] { (Page.General, "GENERAL"), (Page.About, "ABOUT") })
        {
            var b = new VButton { Kind = VButton.Look.Select, K = k, Label = name, FontPx = 18, Selected = page == pg, CustomMinimumSize = new Vector2(150 * k, 46 * k) };
            b.Pressed += () => { if (page != pg) { page = pg; Rebuild(); } };
            tabs.AddChild(b);
        }
        head.AddChild(tabs);
        head.AddChild(Spacer(10 * k, 0));
        var back = Btn("BACK", VButton.Look.Primary, Main.I.ShowMenu, 180, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        var cols = HBox(22 * k);
        cols.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(cols);

        if (page == Page.About)
        {
            BuildAbout(cols);
            col.AddChild(Lbl("Esc — back to menu   ·   ValTrainer is free software (GPL-3.0)   ·   fan-made, not affiliated with or endorsed by Riot Games",
                UiTheme.Body, 13, UiTheme.Faint));
        }
        else
        {
            BuildAim(Column(cols, "AIM & ACCOUNT", null, 1f));
            BuildVideo(Column(cols, "VIDEO & GAMEPLAY", null, 1f));
            BuildImported(cols);
            col.AddChild(Lbl("Esc — back to menu   ·   F11 — toggle windowed / borderless   ·   Shift-click the custom sensitivity arrows for ±0.001",
                UiTheme.Body, 13, UiTheme.Faint));
        }
        updateRev = UpdateCheck.Revision;
        RefreshAll();
    }

    protected override void Tick(float dt)
    {
        if (reimportFlash > 0) { reimportFlash -= dt; reimportNote?.QueueRedraw(); }
        if (copyFlash > 0) { copyFlash -= dt; RefreshAll(); }
        if (updateRev != UpdateCheck.Revision) { updateRev = UpdateCheck.Revision; RefreshAll(); }
    }

    VBoxContainer Column(HBoxContainer cols, string title, string? caption, float ratio)
    {
        var p = new VPanel { Title = title, Caption = caption, K = K, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio };
        cols.AddChild(p);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        p.AddChild(scroll);
        var v = VBox(4 * K);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);
        return v;
    }

    void RefreshAll()
    {
        foreach (var r in refreshers) r();
    }

    void Changed()
    {
        St.Save();
        RefreshAll();
    }

    // ---------------- left column ----------------

    void BuildAim(VBoxContainer v)
    {
        float k = K;
        v.AddChild(new SectionLabel { Text = "VALORANT ACCOUNT", K = k });
        v.AddChild(Selector("Account", () =>
            {
                if (App.Accounts.Count == 0) return "None found";
                var a = CurrentAccount();
                return $"{a.ShortId}… · {a.Region} · {(a.Sens?.ToString("0.###", Inv) ?? "?")}";
            },
            d =>
            {
                if (App.Accounts.Count == 0) return;
                int idx = Math.Max(0, App.Accounts.IndexOf(CurrentAccount()));
                St.AccountId = App.Accounts[(idx + d + App.Accounts.Count) % App.Accounts.Count].Id;
                App.Reimport();
                Sfx.I?.Play("kill1", 0.4f);
            },
            enabled: () => App.Accounts.Count > 1,
            note: () => App.Accounts.Count == 0 ? "No Valorant config folder found on this PC"
                : $"{App.Accounts.Count} found{(CurrentAccount().IsLastUsed ? " · this one was used last" : "")}"));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "SENSITIVITY", K = k });
        v.AddChild(Segmented("Sensitivity source", new[] { "VALORANT", "CUSTOM" }, () => St.UseSensOverride ? 1 : 0, i =>
        {
            bool custom = i == 1;
            if (custom && !St.UseSensOverride) St.SensOverride = App.Valorant.Sensitivity;
            St.UseSensOverride = custom;
        }));
        v.AddChild(Selector("Custom sensitivity", () => St.UseSensOverride ? St.SensOverride.ToString("0.000", Inv) : $"{App.Valorant.Sensitivity.ToString("0.###", Inv)} (Valorant)",
            d =>
            {
                float step = Input.IsKeyPressed(Key.Shift) ? 0.001f : 0.01f;
                St.SensOverride = MathF.Max(0.001f, MathF.Round((St.SensOverride + d * step) * 1000f) / 1000f);
            },
            enabled: () => St.UseSensOverride,
            note: () => St.UseSensOverride ? "±0.01 per click · Shift ±0.001" : "Switch the source to Custom to edit"));
        v.AddChild(Selector("Mouse DPI", () => St.Dpi.ToString(),
            d => St.Dpi = Dpis[Wrap(Array.IndexOf(Dpis, St.Dpi) + d, 0, Dpis.Length - 1)],
            note: () => "Only for showing eDPI and cm/360"));
        v.AddChild(SensSummary());

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "ENEMIES", K = k });
        v.AddChild(Selector("Enemy highlight",
            () => St.EnemyColorOverride < 0 ? $"Valorant ({UiTheme.EnemyColorNames[Math.Clamp(App.Valorant.EnemyHighlight, 0, 3)]})" : UiTheme.EnemyColorNames[St.EnemyColorOverride],
            d => St.EnemyColorOverride = Wrap(St.EnemyColorOverride + d, -1, 3),
            swatch: () => App.EnemyColor));
        v.AddChild(Segmented("Enemy outlines + fresnel", new[] { "OFF", "ON" }, () => St.Outlines ? 1 : 0, i => St.Outlines = i == 1,
            note: () => "Valorant: \"Hide outlines and fresnel\" off = ON"));
        v.AddChild(Segmented("Aim drill targets", new[] { "VALORANT", "CLASSIC" }, () => St.TargetStyle == Target.StyleClassic ? 1 : 0,
            i => St.TargetStyle = i == 1 ? Target.StyleClassic : Target.StyleValorant,
            note: () => St.TargetStyle == Target.StyleClassic ? "Solid spheres in the highlight colour" : "Dark body + highlight outline, like VALORANT enemies"));

        // Crosshair: VALORANT's imported profile, or ValTrainer's own code: the Crosshair Finder's result or a pasted code
        // (ValTrainer only; VALORANT is never changed).
        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "CROSSHAIR", K = k });
        v.AddChild(Segmented("Crosshair", new[] { "VALORANT", "CUSTOM" }, () => St.UseFinderCrosshair && CrosshairCode.Finder != null ? 1 : 0,
            i => St.UseFinderCrosshair = i == 1 && CrosshairCode.Finder != null,
            note: () => CrosshairCode.Finder is not { } f ? "Run the Crosshair Finder (Coach) or paste a code below for a second option"
                : St.UseFinderCrosshair ? $"{f.Name} · ValTrainer only" : "Your imported VALORANT crosshair"));
        v.AddChild(NoteRow(() => !St.UseFinderCrosshair && App.Valorant.Found ? App.Valorant.CrosshairNote : null));
        v.AddChild(new CrosshairPreview { K = k, BoxHeight = 120, Backdrop = CrosshairBackdrop });
        v.AddChild(CrosshairHintRow());
        v.AddChild(CodeRow("Code of the crosshair ValTrainer uses now", () => CrosshairCode.EffectiveCode));
        if (!string.IsNullOrWhiteSpace(St.LastFinderCrosshairCode))
            v.AddChild(CodeRow("Last Crosshair Finder result", () => St.LastFinderCrosshairCode ?? ""));
        v.AddChild(PasteRow());
    }

    /// <summary>A warning line (hidden while <paramref name="text"/> returns null).</summary>
    Control NoteRow(Func<string?> text)
    {
        float k = K;
        var row = HBox(8 * k);
        var mark = Lbl("!", UiTheme.Body, 16, UiTheme.Warn);
        mark.CustomMinimumSize = new Vector2(18 * k, 0);
        mark.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var lbl = Lbl("", UiTheme.Body, 14, UiTheme.Text);
        lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(Spacer(2 * k, 0));
        row.AddChild(mark);
        row.AddChild(lbl);
        void Refresh()
        {
            var t = text();
            row.Visible = t != null;
            lbl.Text = t ?? "";
        }
        Refresh();
        refreshers.Add(Refresh);
        return row;
    }

    /// <summary>"Paste a crosshair code": uses a VALORANT code from the clipboard as ValTrainer's CUSTOM crosshair (for
    /// accounts whose profiles VALORANT keeps online, or to try a pro's crosshair). VALORANT itself is never changed.</summary>
    Control PasteRow()
    {
        float k = K;
        var row = HBox(12 * k);
        row.CustomMinimumSize = new Vector2(0, 50 * k);
        var txt = new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                float cy = d.Size.Y / 2;
                int ls = UiTheme.Fs(14, k);
                Gfx.TextFit(d, UiTheme.Body, "Use a crosshair code", 10 * k, Gfx.Mid(cy - 9 * k, ls), ls, UiTheme.Dim, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, "Copy it in VALORANT (Export Profile Code), then PASTE here", 10 * k, Gfx.Mid(cy + 10 * k, ls), ls, UiTheme.Text, d.Size.X - 10 * k);
            },
        };
        row.AddChild(txt);
        VButton? paste = null;
        void Flash(string label)
        {
            paste!.Label = label;
            paste.QueueRedraw();
            GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(paste)) { paste.Label = "PASTE"; paste.QueueRedraw(); } };
        }
        paste = Btn("PASTE", VButton.Look.Secondary, () =>
        {
            string code = (DisplayServer.ClipboardGet() ?? "").Trim();
            if (CrosshairCode.Decode(code) == null) { Flash("NOT A CODE"); return; }
            St.FinderCrosshairCode = code;
            St.FinderCrosshairTab = null; // a pasted code is the whole profile
            St.UseFinderCrosshair = true;
            Flash("USED ✓");
            Changed();
        }, 110, 36, 16);
        paste.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        paste.TooltipText = "Uses the crosshair code on your clipboard in ValTrainer (Crosshair → CUSTOM). VALORANT isn't changed.";
        row.AddChild(paste);
        return row;
    }

    /// <summary>Range wall with a dummy head under the crosshair (the preview's backdrop).</summary>
    void CrosshairBackdrop(DrawBox d)
    {
        float k = K;
        var s = d.Size;
        var c = (s / 2).Floor();
        float floorY = c.Y + 52 * k;
        Gfx.VGradient(d, new Rect2(0, 0, s.X, floorY), Color.Color8(96, 112, 126), Color.Color8(74, 88, 100));
        Gfx.VGradient(d, new Rect2(0, floorY, s.X, s.Y - floorY), Color.Color8(58, 66, 76), Color.Color8(40, 46, 54));
        var enemy = App.EnemyColor;
        var body = Color.Color8(120, 128, 136);
        float hr = 8 * k, ow = 2f * Mathf.Max(1, k);
        var torso = new Rect2(c.X - 20 * k, c.Y + hr + 4 * k, 40 * k, s.Y - (c.Y + hr + 4 * k));
        d.DrawRect(torso.Grow(ow), enemy);
        d.DrawCircle(c, hr + ow, enemy);
        d.DrawRect(torso, body);
        d.DrawCircle(c, hr, body.Lightened(0.08f));
        Gfx.Brackets(d, new Rect2(Vector2.Zero, s).Grow(-1), 12 * k, new Color(UiTheme.Text, 0.35f), Mathf.Max(1, 2 * k));
    }

    /// <summary>Visibility hint for the imported VALORANT crosshair (same check as the Lock-In setup check); refreshed on re-import.</summary>
    Control CrosshairHintRow()
    {
        float k = K;
        var row = HBox(8 * k);
        var mark = Lbl("", UiTheme.Body, 16, UiTheme.Good);
        mark.CustomMinimumSize = new Vector2(18 * k, 0);
        mark.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var text = Lbl("", UiTheme.Body, 14, UiTheme.Dim);
        text.MouseFilter = MouseFilterEnum.Pass;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(Spacer(2 * k, 0));
        row.AddChild(mark);
        row.AddChild(text);
        void Refresh()
        {
            var hint = Modes.CrosshairHint.ForImported();
            row.Visible = hint != null;
            if (hint is not { } h) return;
            mark.Text = h.Ok ? "✓" : "!";
            mark.LabelSettings.FontColor = h.Ok ? UiTheme.Good : UiTheme.Warn;
            text.Text = h.Ok ? "Your VALORANT crosshair stands out on common map colours." : h.Long;
            text.TooltipText = h.Ok ? h.Long : "";
            text.LabelSettings.FontColor = h.Ok ? UiTheme.Dim : UiTheme.Text;
        }
        Refresh();
        refreshers.Add(Refresh);
        return row;
    }

    /// <summary>A VALORANT crosshair code with a COPY button (paste it in VALORANT → Settings → Crosshair → Import Profile Code).</summary>
    Control CodeRow(string label, Func<string> code)
    {
        float k = K;
        var row = HBox(12 * k);
        row.CustomMinimumSize = new Vector2(0, 50 * k);
        // The whole code, wrapped anywhere (a split profile's code is long): what you see is what COPY copies.
        var txt = VBox(2 * k);
        txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        txt.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var head = Lbl(label, UiTheme.Body, 14, UiTheme.Dim);
        var body = Lbl(code(), UiTheme.Body, 13, UiTheme.Text);
        body.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        body.MouseFilter = MouseFilterEnum.Pass;
        txt.AddChild(head);
        txt.AddChild(body);
        refreshers.Add(() => { var c = code(); if (body.Text != c) body.Text = c; });
        var pad = Margins(txt, 10 * k, 0, 0, 0);
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(pad);
        VButton? copy = null;
        copy = Btn("COPY", VButton.Look.Secondary, () =>
        {
            DisplayServer.ClipboardSet(code());
            copy!.Label = "COPIED ✓";
            copy.QueueRedraw();
            GetTree().CreateTimer(2.0).Timeout += () => { if (IsInstanceValid(copy)) { copy.Label = "COPY"; copy.QueueRedraw(); } };
        }, 110, 36, 16);
        copy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        copy.TooltipText = "Copy, then paste it in VALORANT: Settings → Crosshair → Import Profile Code";
        row.AddChild(copy);
        return row;
    }

    static ValorantAccount CurrentAccount() => App.Accounts.FirstOrDefault(x => x.Id == App.Valorant.AccountId) ?? App.Accounts[0];

    Control SensSummary()
    {
        float k = K;
        var d = new DrawBox { CustomMinimumSize = new Vector2(0, 84 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        d.OnDraw = b =>
        {
            var r = new Rect2(0, 6 * k, b.Size.X, b.Size.Y - 12 * k);
            Gfx.Plate(b, r, 10 * k, new Color(1, 1, 1, 0.03f), new Color(UiTheme.Text, 0.08f));
            float sens = App.Sens;
            (string L, string V)[] items =
            {
                ("SENSITIVITY", sens.ToString("0.###", Inv)),
                ("eDPI", (sens * St.Dpi).ToString("0", Inv)),
                ("CM / 360°", PlayerView.Cm360(sens, St.Dpi).ToString("0.0", Inv)),
            };
            float cw = r.Size.X / items.Length;
            int ls = UiTheme.Fs(11, k), vs = UiTheme.Fs(30, k);
            for (int i = 0; i < items.Length; i++)
            {
                float cx = r.Position.X + cw * (i + 0.5f);
                Gfx.TextC(b, UiTheme.Display, items[i].V, cx, r.Position.Y + 42 * k, vs, i == 0 && St.UseSensOverride ? UiTheme.Warn : UiTheme.Text);
                Gfx.TextC(b, UiTheme.HudWide, items[i].L, cx, r.Position.Y + 62 * k, ls, UiTheme.Dim);
                if (i > 0) b.DrawRect(new Rect2(r.Position.X + cw * i, r.Position.Y + 14 * k, 1, r.Size.Y - 28 * k), new Color(UiTheme.Text, 0.08f));
            }
        };
        refreshers.Add(d.QueueRedraw);
        return d;
    }

    // ---------------- middle column ----------------

    void BuildVideo(VBoxContainer v)
    {
        float k = K;
        v.AddChild(new SectionLabel { Text = "DISPLAY", K = k });
        v.AddChild(Selector("Display mode",
            () => St.WindowModeOverride < 0 ? $"Valorant ({WindowModes[Math.Clamp(App.Valorant.WindowMode, 0, 2)]})" : WindowModes[St.WindowModeOverride],
            d => { St.WindowModeOverride = Wrap(St.WindowModeOverride + d, -1, 2); App.ApplyDisplay(); }));
        int monCount = Math.Max(1, DisplayServer.GetScreenCount());
        string MonName(int i) { var s = DisplayServer.ScreenGetSize(i); return $"#{i + 1} · {s.X}×{s.Y}"; }
        v.AddChild(Selector("Monitor",
            () => St.MonitorOverride < 0 ? $"Valorant ({MonName(Math.Clamp(App.Valorant.MonitorIndex, 0, monCount - 1))})" : MonName(Math.Min(St.MonitorOverride, monCount - 1)),
            d => { St.MonitorOverride = Wrap(St.MonitorOverride + d, -1, monCount - 1); App.ApplyDisplay(); },
            enabled: () => monCount > 1 || St.MonitorOverride >= 0));
        v.AddChild(Selector("VSync",
            () => St.VSyncOverride < 0 ? $"Valorant ({(App.Valorant.VSync ? "On" : "Off")})" : St.VSyncOverride == 1 ? "On" : "Off",
            d => { St.VSyncOverride = Wrap(St.VSyncOverride + d, -1, 1); App.ApplyFps(); }));
        v.AddChild(Selector("FPS cap",
            () => St.FpsCapOverride < 0 ? $"Valorant ({(App.Valorant.FrameRateLimit <= 0 ? "Unlimited" : App.Valorant.FrameRateLimit.ToString("0", Inv))})"
                : St.FpsCapOverride == 0 ? "Unlimited" : St.FpsCapOverride.ToString(),
            d =>
            {
                int fi = St.FpsCapOverride < 0 ? -1 : Array.IndexOf(FpsCaps, St.FpsCapOverride);
                fi = Wrap(fi + d, -1, FpsCaps.Length - 1);
                St.FpsCapOverride = fi < 0 ? -1 : FpsCaps[fi];
                App.ApplyFps();
            }));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "GRAPHICS", K = k });
        v.AddChild(Selector("Graphics quality", () => Qualities[Math.Clamp(St.Quality, 0, 3)],
            d => St.Quality = Wrap(St.Quality + d, 0, 3),
            note: () => "Applies on next drill"));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "GAMEPLAY", K = k });
        v.AddChild(Selector("Difficulty tier", () => { var t = Difficulty.Get(St.Tier); return $"{t.Name} · {t.Ranks}"; },
            d => St.Tier = Wrap(St.Tier + d, 0, 4),
            emblem: () => St.Tier,
            note: () => $"Bots react in ~{Difficulty.Get(St.Tier).Bot.ReactMs:0} ms"));
        v.AddChild(Segmented("Viewmodel (first-person gun)", new[] { "OFF", "ON" }, () => St.ViewModel ? 1 : 0, i => St.ViewModel = i == 1,
            note: () => "Applies on next drill"));
        v.AddChild(Segmented("Weapon hand", new[] { "RIGHT", "LEFT" }, () => St.LeftHandedWeapon ? 1 : 0, i => St.LeftHandedWeapon = i == 1,
            note: () => Main.I.Valorant.Found
                ? $"Applies on next drill · VALORANT uses {(Main.I.Valorant.LeftHanded ? "left" : "right")}"
                : "Applies on next drill"));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "AUDIO", K = k });
        v.AddChild(Selector("Volume", () => $"{St.Volume * 100:0}%",
            d =>
            {
                St.Volume = Math.Clamp(MathF.Round((St.Volume + d * 0.05f) * 20f) / 20f, 0f, 1f);
                Sfx.Volume = St.Volume;
                Sfx.I?.Play("body");
            },
            bar: () => St.Volume));
    }

    // ---------------- right column (read-only) ----------------

    void BuildImported(HBoxContainer cols)
    {
        float k = K;
        var p = new VPanel { Title = "IMPORTED FROM VALORANT", Caption = "read-only", K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.05f };
        cols.AddChild(p);
        var outer = VBox(12 * k);
        p.AddChild(outer);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var v = VBox(0);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);

        int stripe = 0;
        var vp = () => App.Valorant;
        InfoRow Info(string label, Func<string> value, Func<Color>? color = null, Func<Color?>? swatch = null)
        {
            var row = new InfoRow { K = k, H = 31, Label = label, Value = value, ValueColor = color, Swatch = swatch, Stripe = stripe++ % 2 == 0 };
            refreshers.Add(row.QueueRedraw);
            v.AddChild(row);
            return row;
        }
        // Keybind rows: hovering one shows every imported bind (both slots) in a tooltip.
        void Keys(string label, Func<string> value)
        {
            var row = Info(label, value);
            row.MouseFilter = MouseFilterEnum.Pass;
            string Tip() => "VALORANT keybinds (primary · secondary)\n" + vp().Binds.ListText();
            row.TooltipText = Tip();
            refreshers.Add(() => row.TooltipText = Tip());
        }

        Info("Source", () => vp().Found ? vp().Source : "Not found — Valorant defaults", () => vp().Found ? UiTheme.Good : UiTheme.Warn);
        Info("Sensitivity", () => vp().SensFromFile ? vp().Sensitivity.ToString("0.####", Inv) : $"{vp().Sensitivity.ToString(Inv)} (default)");
        Info("Scoped / ADS multiplier", () => $"{vp().ZoomedSensMult.ToString("0.###", Inv)} / {vp().AdsSensMult.ToString("0.###", Inv)}");
        Info("Sniper scope input", () => vp().HoldToScope ? "Hold" : "Toggle");
        Keys("Move (forward left back right)", () => string.Join(" ", new[] { GameAction.MoveForward, GameAction.StrafeLeft, GameAction.MoveBack, GameAction.StrafeRight }.Select(a => vp().Binds.Short(a))));
        Keys("Walk / crouch / jump", () => $"{vp().Binds.Text(GameAction.Walk)} / {vp().Binds.Text(GameAction.Crouch)} / {vp().Binds.Text(GameAction.Jump)}");
        Keys("Fire / alt fire", () => $"{vp().Binds.Text(GameAction.Fire)} / {vp().Binds.Text(GameAction.AltFire)}");
        Keys("Reload / use / plant-defuse", () => $"{vp().Binds.Text(GameAction.Reload)} / {vp().Binds.Text(GameAction.Use)} / {vp().Binds.Text(GameAction.UseSpike)}");
        Keys("Abilities C Q E X", () => string.Join(" ", Enumerable.Range(0, 4).Select(i => vp().AbilityBindText(i))));
        Info("Resolution", () => $"{vp().ResX} × {vp().ResY}{(vp().Letterbox ? " (letterbox)" : "")}");
        Info("Display mode / monitor", () => $"{WindowModes[Math.Clamp(vp().WindowMode, 0, 2)]} / #{vp().MonitorIndex + 1}");
        Info("VSync / FPS limit", () => $"{(vp().VSync ? "On" : "Off")} / {(vp().FrameRateLimit <= 0 ? "Unlimited" : vp().FrameRateLimit.ToString("0", Inv))}");
        Info("Material / Texture / Detail", () => $"{Q(vp().MaterialQuality)} / {Q(vp().TextureQuality)} / {Q(vp().DetailQuality)}");
        Info("UI quality / Anisotropic", () => $"{Q(vp().UIQuality)} / {(vp().Anisotropic < 0 ? "Default" : vp().Anisotropic + "x")}");
        Info("Anti-aliasing", () => AaName(vp().AntiAliasing) + (vp().WantsMsaa ? " → MSAA 4x" : ""));
        Info("Improve clarity / Bloom", () => $"{B(vp().ImproveClarity)} / {Q(vp().BloomQuality)}");
        Info("NVIDIA Reflex", () => vp().Reflex switch { -1 => "Default", 0 => "Off", 1 => "On", 2 => "On + Boost", _ => vp().Reflex.ToString() });
        Info("Enemy highlight", () => UiTheme.EnemyColorNames[Math.Clamp(vp().EnemyHighlight, 0, 3)], () => UiTheme.EnemyColors[Math.Clamp(vp().EnemyHighlight, 0, 3)],
            () => UiTheme.EnemyColors[Math.Clamp(vp().EnemyHighlight, 0, 3)]);
        Info("Show FPS counter", () => vp().ShowFps ? "On" : "Off");
        Info("Crosshair profile", () => vp().Crosshair.Name);

        var bottom = HBox(14 * k);
        var re = Btn("RE-IMPORT FROM VALORANT", VButton.Look.Secondary, () =>
        {
            App.Reimport();
            App.ApplyFps();
            reimportFlash = 2.5f;
            Sfx.I?.Play("kill1", 0.5f);
            RefreshAll();
        }, 300, 50, 18);
        bottom.AddChild(re);
        reimportNote = new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                if (reimportFlash <= 0) return;
                int fs = UiTheme.Fs(15, k);
                Gfx.Text(d, UiTheme.Body, $"Re-imported · {App.Accounts.Count} account(s)", 0, Gfx.Mid(d.Size.Y / 2, fs), fs,
                    new Color(UiTheme.Good, Mathf.Min(1f, reimportFlash)));
            },
        };
        bottom.AddChild(reimportNote);
        outer.AddChild(bottom);
    }

    // ---------------- ABOUT page ----------------

    void BuildAbout(HBoxContainer cols)
    {
        float k = K;
        var v = Column(cols, "VALTRAINER", $"version {AppInfo.Version}", 1f);

        v.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 86 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(54, k), vs = UiTheme.Fs(18, k), ss = UiTheme.Fs(14, k);
                float y = 50 * k, x = 10 * k;
                Gfx.Text(d, UiTheme.Display, "VAL", x, y, fs, UiTheme.Accent);
                x += Gfx.TextW(UiTheme.Display, "VAL", fs) + 1 * k;
                Gfx.Text(d, UiTheme.Display, "TRAINER", x, y, fs, UiTheme.Text);
                x += Gfx.TextW(UiTheme.Display, "TRAINER", fs) + 14 * k;
                Gfx.Text(d, UiTheme.HudWide, "v" + AppInfo.Version, x, y, vs, UiTheme.Dim);
                Gfx.TextFit(d, UiTheme.Body, "Free, fan-made aim trainer · open source (GPL-3.0) · not affiliated with Riot Games",
                    10 * k, 76 * k, ss, UiTheme.Dim, d.Size.X - 10 * k);
            },
        });

        int stripe = 0;
        void Info(string label, Func<string> value, Func<Color>? color = null)
        {
            var row = new InfoRow { K = k, H = 31, Label = label, Value = value, ValueColor = color, Stripe = stripe++ % 2 == 0 };
            refreshers.Add(row.QueueRedraw);
            v.AddChild(row);
        }

        v.AddChild(new SectionLabel { Text = "THIS PC", K = k });
        Info("Version", () => AppInfo.Version + (App.Dev ? " (dev run)" : ""));
        Info("Renderer", () => SysInfo.RendererName, () => SysInfo.IsCompatibility ? UiTheme.Warn : UiTheme.Text);
        Info("Graphics card", () => SysInfo.GpuName.Length > 0 ? SysInfo.GpuName : "Unknown");
        Info("Graphics quality", () => Qualities[Math.Clamp(St.Quality, 0, 3)] + (St.AutoQualityReason != null ? $" · auto ({St.AutoQualityReason})" : ""));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "FOLDERS", K = k });
        v.AddChild(FolderRow("Settings, stats & recordings", Paths.DataDir));
        if (Paths.DataDirProblem is { } problem) v.AddChild(Lbl(problem, UiTheme.Body, 13, UiTheme.Warn));
        v.AddChild(FolderRow("Logs (attach godot.log to bug reports)", Paths.LogDir));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "UPDATES", K = k });
        // Opt-in (see UpdatesPrompt): ON here is the same yes as ENABLE UPDATES on the first-launch prompt.
        v.AddChild(Segmented("Check for updates", new[] { "OFF", "ON" }, () => St.AutoUpdates ? 1 : 0,
            i =>
            {
                St.CheckUpdates = i == 1;
                St.UpdatesConsent = i == 1;
                UpdatesPrompt.Pending = false;
                UpdateCheck.EnabledChanged();
            },
            note: () => !AppInfo.HasRepo ? "Not available: this build has no GitHub page set"
                : St.AutoUpdates ? "At startup and every 6 hours, asks GitHub whether a newer release is out (nothing about you is sent)"
                : "Off: ValTrainer makes no network requests. ON asks GitHub at startup and every 6 hours; CHECK NOW asks once"));
        var auto = Segmented("Download updates automatically", new[] { "OFF", "ON" }, () => St.AutoDownloadUpdates ? 1 : 0,
            i => { St.AutoDownloadUpdates = i == 1; Updater.SettingsChanged(); }, note: AutoUpdateNote);
        auto.Enabled = () => St.AutoUpdates && Updater.Status != Updater.State.Off;
        v.AddChild(auto);
        var upd = HBox(12 * k);
        upd.CustomMinimumSize = new Vector2(0, 52 * k);
        var check = Btn("CHECK NOW", VButton.Look.Secondary, UpdateCheck.CheckNow, 160, 40, 17);
        check.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        upd.AddChild(Spacer(4 * k, 0));
        upd.AddChild(check);
        var get = Btn("DOWNLOAD", VButton.Look.Primary, () =>
        {
            if (Updater.ReadyVersion != null) Updater.RestartToUpdate();
            else AppInfo.OpenGitHub(UpdateCheck.LatestUrl ?? AppInfo.LatestReleaseUrl);
        }, 210, 40, 17);
        get.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        upd.AddChild(get);
        var status = new DrawBox { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        status.OnDraw = d =>
        {
            int fs = UiTheme.Fs(14, k);
            var (txt, col) = UpdateStatus();
            Gfx.TextFit(d, UiTheme.Body, txt, 0, Gfx.Mid(d.Size.Y / 2, fs), fs, col, d.Size.X);
        };
        upd.AddChild(status);
        v.AddChild(upd);
        refreshers.Add(() =>
        {
            check.Disabled = !UpdateCheck.CanCheck || UpdateCheck.Status == UpdateCheck.State.Checking;
            check.TooltipText = UpdateCheck.CanCheck ? "Ask GitHub once now whether a newer release is out" : UpdateCheck.Message ?? "";
            status.TooltipText = UpdateCheck.Status == UpdateCheck.State.Failed ? UpdateCheck.Message ?? "" : "";
            bool ready = Updater.ReadyVersion != null;
            get.Visible = ready || (UpdateCheck.NewerAvailable && AppInfo.HasRepo && Updater.Status != Updater.State.Applying);
            get.Label = ready ? "RESTART TO UPDATE" : $"GET {UpdateCheck.Latest}";
            get.TooltipText = ready ? "Installs the update and restarts ValTrainer" + (Updater.NeedsElevation ? " (Windows asks for permission)" : "")
                : "Opens the release page in your browser";
            auto.Refresh();
            get.QueueRedraw();
            check.QueueRedraw();
            status.QueueRedraw();
        });

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "HELP & FEEDBACK", K = k });
        var help = HBox(10 * k);
        help.CustomMinimumSize = new Vector2(0, 52 * k);
        help.AddChild(Spacer(4 * k, 0));
        if (AppInfo.HasRepo)
        {
            var bug = Btn("REPORT A BUG", VButton.Look.Secondary, () => AppInfo.OpenGitHub(AppInfo.NewIssueUrl), 170, 40, 17);
            bug.TooltipText = "Opens GitHub's issue form in your browser";
            bug.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            help.AddChild(bug);
            var site = Btn("GITHUB PAGE", VButton.Look.Secondary, () => AppInfo.OpenGitHub(AppInfo.RepoUrl), 160, 40, 17);
            site.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            help.AddChild(site);
        }
        var copy = Btn("COPY SYSTEM INFO", VButton.Look.Secondary, () =>
        {
            DisplayServer.ClipboardSet(SystemInfoText());
            copyFlash = 2.5f;
        }, 200, 40, 17);
        copy.TooltipText = "Version, renderer, GPU and folders, for a bug report";
        copy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        help.AddChild(copy);
        var copied = new DrawBox { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copied.OnDraw = d =>
        {
            if (copyFlash <= 0) return;
            int fs = UiTheme.Fs(14, k);
            Gfx.TextFit(d, UiTheme.Body, "Copied", 0, Gfx.Mid(d.Size.Y / 2, fs), fs, new Color(UiTheme.Good, Mathf.Min(1f, copyFlash)), d.Size.X);
        };
        refreshers.Add(copied.QueueRedraw);
        help.AddChild(copied);
        v.AddChild(help);

        BuildLicenses(cols);
    }

    /// <summary>Settings → About note under "Download updates automatically": how this copy updates (or why it can't).</summary>
    static string AutoUpdateNote()
    {
        if (Updater.Status == Updater.State.Off) return "Not available: " + (Updater.Message ?? "this copy can't update itself");
        return Updater.Kind switch
        {
            InstallKind.Portable => "Downloads new versions in the background; ValTrainer.exe is replaced when you restart",
            InstallKind.InstalledAdmin => "Downloads new versions in the background; RESTART TO UPDATE installs them (Windows asks for permission)",
            _ => "Downloads new versions in the background; they install when you restart ValTrainer",
        };
    }

    (string, Color) UpdateStatus()
    {
        if (UpdateCheck.Status != UpdateCheck.State.Checking && Updater.StatusLine is { } line && UpdateCheck.Offered != null)
            return (line, Updater.Status switch
            {
                Updater.State.Ready => UiTheme.Good,
                Updater.State.Failed => UiTheme.Warn,
                Updater.State.Idle => UiTheme.Warn,
                _ => UiTheme.Dim,
            });
        return CheckStatus();
    }

    (string, Color) CheckStatus() => UpdateCheck.Status switch
    {
        UpdateCheck.State.Checking => ("Checking GitHub…", UiTheme.Dim),
        UpdateCheck.State.Available => ($"ValTrainer {UpdateCheck.Latest} is available", UiTheme.Good),
        UpdateCheck.State.UpToDate => ("You have the latest version" + (St.LastUpdateCheck > 0 ? $" · checked {Ago(St.LastUpdateCheck)}" : ""), UiTheme.Dim),
        UpdateCheck.State.Failed => (string.IsNullOrWhiteSpace(UpdateCheck.Message) ? "Couldn't check for updates" : $"Couldn't check for updates: {UpdateCheck.Message}", UiTheme.Warn),
        UpdateCheck.State.Off => (UpdateCheck.Message ?? "Off", UiTheme.Faint),
        _ => ("", UiTheme.Dim),
    };

    static string Ago(long unix)
    {
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    static string SystemInfoText() =>
        $"ValTrainer {AppInfo.Version}{(App.Dev ? " (dev)" : "")}\n" +
        $"Renderer: {SysInfo.Summary}\n" +
        $"Graphics quality: {Qualities[Math.Clamp(St.Quality, 0, 3)]}{(St.AutoQualityReason != null ? $" (auto: {St.AutoQualityReason})" : "")}\n" +
        $"VALORANT settings: {(App.Valorant.Found ? "imported" : App.Valorant.Status.ToString())}\n" +
        $"Data folder: {Paths.DataDir}{(Paths.DataDirProblem != null ? $" [{Paths.DataDirProblem}]" : "")}\n" +
        $"Logs: {Paths.LogDir}";

    /// <summary>"label / path ............ [OPEN]" (opens the folder in Explorer).</summary>
    Control FolderRow(string label, string path)
    {
        float k = K;
        var row = HBox(12 * k);
        row.CustomMinimumSize = new Vector2(0, 50 * k);
        var txt = new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                float cy = d.Size.Y / 2;
                int ls = UiTheme.Fs(14, k), ps = UiTheme.Fs(14, k);
                Gfx.TextFit(d, UiTheme.Body, label, 10 * k, Gfx.Mid(cy - 9 * k, ls), ls, UiTheme.Dim, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, path, 10 * k, Gfx.Mid(cy + 10 * k, ps), ps, UiTheme.Text, d.Size.X - 10 * k);
            },
        };
        row.AddChild(txt);
        var open = Btn("OPEN", VButton.Look.Secondary, () => AppInfo.OpenFolder(path), 96, 36, 16);
        open.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        open.TooltipText = path;
        row.AddChild(open);
        return row;
    }

    void BuildLicenses(HBoxContainer cols)
    {
        float k = K;
        var p = new VPanel { Title = "LICENSES", Caption = "open-source notices", K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f };
        cols.AddChild(p);
        var v = VBox(10 * k);
        p.AddChild(v);

        var te = new TextEdit
        {
            Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsVertical = SizeFlags.ExpandFill,
            ContextMenuEnabled = false, HighlightCurrentLine = false, DeselectOnFocusLossEnabled = true, DragAndDropSelectionEnabled = false,
            MiddleMousePasteEnabled = false, VirtualKeyboardEnabled = false, CaretBlink = false, ScrollSmooth = true,
            Text = AboutLicenses.Text,
        };
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.06f, 0.09f, 0.55f), BorderColor = new Color(UiTheme.Text, 0.08f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14 * k, ContentMarginRight = 10 * k, ContentMarginTop = 10 * k, ContentMarginBottom = 10 * k,
        };
        te.AddThemeStyleboxOverride("normal", bg);
        te.AddThemeStyleboxOverride("read_only", bg);
        te.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        te.AddThemeFontOverride("font", UiTheme.Body);
        te.AddThemeFontSizeOverride("font_size", UiTheme.Fs(13.5f, k));
        var fc = new Color(0.8f, 0.79f, 0.76f);
        te.AddThemeColorOverride("font_readonly_color", fc);
        te.AddThemeColorOverride("font_color", fc);
        te.AddThemeColorOverride("selection_color", new Color(UiTheme.Accent, 0.35f));
        te.AddThemeConstantOverride("line_spacing", (int)(3 * k));

        var chips = HBox(6 * k);
        var buttons = new List<VButton>();
        foreach (var (part, name) in new[]
                 {
                     (AboutLicenses.Part.ValTrainer, "VALTRAINER"), (AboutLicenses.Part.Godot, "GODOT"),
                     (AboutLicenses.Part.GodotThirdParty, "ENGINE PARTS"), (AboutLicenses.Part.DotNet, ".NET"), (AboutLicenses.Part.Assets, "ASSETS"),
                 })
        {
            var b = new VButton { Kind = VButton.Look.Select, K = k, Label = name, FontPx = 14, Selected = part == AboutLicenses.Part.ValTrainer,
                CustomMinimumSize = new Vector2(0, 32 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            b.Pressed += () =>
            {
                te.SetLineAsFirstVisible(AboutLicenses.LineOf(part));
                foreach (var o in buttons) o.SetSelected(o == b);
            };
            buttons.Add(b);
            chips.AddChild(b);
        }
        v.AddChild(chips);
        v.AddChild(te);
    }

    // ---------------- row builders ----------------

    SettingRow Selector(string label, Func<string> value, Action<int> step, Func<bool>? enabled = null, Func<string?>? note = null,
        Func<Color?>? swatch = null, Func<float>? bar = null, Func<int>? emblem = null)
    {
        float k = K;
        var row = new SettingRow { K = k, Label = label, Note = note, Enabled = enabled, Stacked = stacked };
        var sel = HBox(6 * k);
        sel.CustomMinimumSize = new Vector2(stacked ? 0 : 250 * k, 0);
        sel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sel.SizeFlagsStretchRatio = 1.15f;
        var l = new VButton { Kind = VButton.Look.Arrow, Dir = -1, K = k, CustomMinimumSize = new Vector2(36 * k, 36 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var r = new VButton { Kind = VButton.Look.Arrow, Dir = 1, K = k, CustomMinimumSize = new Vector2(36 * k, 36 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var field = new ValueField { K = k, Value = value, Swatch = swatch, Bar = bar, Emblem = emblem, CustomMinimumSize = new Vector2(0, 36 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        void Do(int d)
        {
            if (enabled != null && !enabled()) return;
            step(d);
            Changed();
        }
        l.Pressed += () => Do(-1);
        r.Pressed += () => Do(1);
        field.Pressed += () => Do(1);
        sel.AddChild(l); sel.AddChild(field); sel.AddChild(r);
        row.AddChild(sel);
        refreshers.Add(() =>
        {
            bool on = enabled?.Invoke() ?? true;
            l.Disabled = r.Disabled = field.Disabled = !on;
            field.QueueRedraw();
            row.Refresh();
        });
        return row;
    }

    SettingRow Segmented(string label, string[] options, Func<int> get, Action<int> set, Func<string?>? note = null)
    {
        float k = K;
        var row = new SettingRow { K = k, Label = label, Note = note, Stacked = stacked };
        var seg = HBox(6 * k);
        seg.CustomMinimumSize = new Vector2(stacked ? 0 : 250 * k, 0);
        seg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        seg.SizeFlagsStretchRatio = 1.15f;
        var buttons = new List<VButton>();
        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            var b = new VButton { Kind = VButton.Look.Select, K = k, Label = options[i], FontPx = 16, CustomMinimumSize = new Vector2(0, 36 * k),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            b.Pressed += () => { set(idx); Changed(); };
            buttons.Add(b);
            seg.AddChild(b);
        }
        row.AddChild(seg);
        refreshers.Add(() =>
        {
            int cur = get();
            for (int i = 0; i < buttons.Count; i++) buttons[i].SetSelected(i == cur);
            row.Refresh();
        });
        return row;
    }
}

/// <summary>One settings row: label (+ optional note) on the left, the control (selector / segmented) on the right —
/// or, when <see cref="Stacked"/> (narrow windows), the label above a full-width control. Labels reserve their full
/// width; a note that doesn't fit wraps onto a second line.</summary>
public partial class SettingRow : BoxContainer
{
    public string Label = "";
    public Func<string?>? Note;
    public Func<bool>? Enabled;
    public float K = 1f;
    public bool Stacked;
    float hover;
    bool twoLines;
    readonly DrawBox text = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    public SettingRow() => MouseFilter = MouseFilterEnum.Pass;

    public override void _Ready()
    {
        Vertical = Stacked;
        AddThemeConstantOverride("separation", (int)((Stacked ? 2 : 12) * K));
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.OnDraw = DrawText;
        if (!Stacked)
        {
            int ls = UiTheme.Fs(16, K);
            text.CustomMinimumSize = new Vector2(12 * K + Gfx.TextW(UiTheme.Body, Label, ls) + 6 * K, 0);
        }
        text.Resized += UpdateHeight;
        AddChild(text);
        MoveChild(text, 0);
        UpdateHeight();
    }

    public void Refresh()
    {
        UpdateHeight();
        text.QueueRedraw();
    }

    /// <summary>Row height: one or two note lines; stacked rows add the control below the text.</summary>
    void UpdateHeight()
    {
        float k = K;
        string? note = Note?.Invoke();
        twoLines = note != null && text.Size.X > 0 && NoteLines(note, UiTheme.Fs(12.5f, k), text.Size.X - 12 * k).Length > 1;
        if (Stacked)
        {
            text.CustomMinimumSize = new Vector2(0, (note == null ? 28 : twoLines ? 52 : 40) * k);
            CustomMinimumSize = new Vector2(0, text.CustomMinimumSize.Y + 46 * k);
        }
        else CustomMinimumSize = new Vector2(0, (twoLines ? 62 : 52) * k);
    }

    /// <summary>Word wrap into at most two balanced lines (no one-word orphan; the second is ellipsized if still too long).</summary>
    static string[] NoteLines(string note, int size, float w)
    {
        if (Gfx.TextW(UiTheme.Body, note, size) <= w) return new[] { note };
        var words = note.Split(' ');
        int best = 0;
        float bestW = float.MaxValue;
        for (int n = 1; n < words.Length; n++)
        {
            float w1 = Gfx.TextW(UiTheme.Body, string.Join(' ', words, 0, n), size);
            if (w1 > w) break;
            float m = Mathf.Max(w1, Gfx.TextW(UiTheme.Body, string.Join(' ', words, n, words.Length - n), size));
            if (m < bestW) { bestW = m; best = n; }
        }
        if (best == 0) return new[] { note };
        return new[] { string.Join(' ', words, 0, best), string.Join(' ', words, best, words.Length - best) };
    }

    public override void _Process(double delta)
    {
        float t = GetGlobalRect().HasPoint(GetGlobalMousePosition()) ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (hover <= 0.01f) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.035f * hover));
        DrawRect(new Rect2(0, 0, 2 * K, Size.Y), new Color(UiTheme.Accent, hover));
    }

    void DrawText(DrawBox d)
    {
        float k = K, cy = d.Size.Y / 2;
        bool on = Enabled?.Invoke() ?? true;
        string? note = Note?.Invoke();
        int ls = UiTheme.Fs(16, k), ns = UiTheme.Fs(12.5f, k);
        float x = 12 * k, w = d.Size.X - x;
        var lc = on ? UiTheme.Text : UiTheme.Dim;
        if (note == null)
        {
            Gfx.TextFit(d, UiTheme.Body, Label, x, Gfx.Mid(cy, ls), ls, lc, w);
            return;
        }
        var lines = NoteLines(note, ns, w);
        if (lines.Length == 1)
        {
            Gfx.TextFit(d, UiTheme.Body, Label, x, Gfx.Mid(cy - 9 * k, ls), ls, lc, w);
            Gfx.TextFit(d, UiTheme.Body, note, x, Gfx.Mid(cy + 11 * k, ns), ns, UiTheme.Faint, w);
        }
        else
        {
            Gfx.TextFit(d, UiTheme.Body, Label, x, Gfx.Mid(cy - 15 * k, ls), ls, lc, w);
            Gfx.TextFit(d, UiTheme.Body, lines[0], x, Gfx.Mid(cy + 4 * k, ns), ns, UiTheme.Faint, w);
            Gfx.TextFit(d, UiTheme.Body, lines[1], x, Gfx.Mid(cy + 18 * k, ns), ns, UiTheme.Faint, w);
        }
    }
}

/// <summary>The value box between the &lt; &gt; arrows (click = next). Can show a colour swatch, a fill bar or a rank emblem.</summary>
public partial class ValueField : BaseButton
{
    public Func<string> Value = () => "";
    public Func<Color?>? Swatch;
    public Func<float>? Bar;
    public Func<int>? Emblem;
    public float K = 1f;
    float hover;

    public ValueField()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready() => Pressed += UiTheme.ClickSound;

    public override void _Process(double delta)
    {
        float t = IsHovered() && !Disabled ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, a = Disabled ? 0.45f : 1f;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0.04f, 0.07f, 0.1f, 0.8f).Lerp(new Color(1, 1, 1, 0.08f), hover));
        DrawRect(new Rect2(0, r.Size.Y - 1, r.Size.X, 1), new Color(UiTheme.Text, 0.25f * a));
        if (Bar != null)
        {
            float f = Mathf.Clamp(Bar(), 0, 1);
            DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X * f, 3 * k), UiTheme.Accent);
        }
        string v = Value();
        int fs = UiTheme.Fs(15, k);
        float tw = Gfx.TextW(UiTheme.Body, v, fs);
        float extra = 0;
        Color? sw = Swatch?.Invoke();
        int em = Emblem?.Invoke() ?? -99;
        if (sw != null || em >= -1) extra = 22 * k;
        float maxW = r.Size.X - 16 * k - extra;
        float total = Mathf.Min(tw, maxW) + extra;
        float x = (r.Size.X - total) / 2;
        float cy = r.Size.Y / 2;
        if (sw is { } c)
        {
            float s = 12 * k;
            DrawRect(new Rect2(x, cy - s / 2, s, s), c);
            DrawRect(new Rect2(x, cy - s / 2, s, s), new Color(1, 1, 1, 0.4f), false, 1f);
        }
        else if (em >= -1) RankEmblem.Draw(this, new Vector2(x + 8 * k, cy), 9 * k, em, a);
        Gfx.TextFit(this, UiTheme.Body, v, x + extra, Gfx.Mid(cy, fs), fs, new Color(UiTheme.Text, a), maxW);
    }
}
