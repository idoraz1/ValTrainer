using System.Globalization;
using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Weapon;
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
    public enum Page { General, Keybinds, About }

    /// <summary>Page the next settings screen opens on (dev: --screen about / --screen keybinds).</summary>
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

    // English data (also logic keys / the bug-report payload): translated where shown
    static readonly string[] WindowModes = { L.N("Fullscreen"), L.N("Windowed Fullscreen"), L.N("Windowed") };
    static readonly int[] FpsCaps = { 0, 60, 144, 165, 240, 360 };
    static readonly int[] Dpis = { 400, 800, 1000, 1200, 1600, 2000, 3200 };
    internal static readonly string[] Qualities ={ L.N("Competitive (Low)"), L.N("Medium"), L.N("High"), L.N("Ultra") };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string AaName(int v) => v switch { -1 => L.N("Default"), 0 => L.N("None"), 1 => "MSAA 2x", 2 => "MSAA 4x", 3 => "FXAA", _ => v.ToString() };
    static string Q(int v) => v switch { -1 => L.N("Default"), 0 => L.N("Low"), 1 => L.N("Medium"), 2 => L.N("High"), 3 => L.N("Ultra"), _ => v.ToString() };
    static string B(bool? b) => b == null ? L.N("Default") : b.Value ? L.N("On") : L.N("Off");
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
        var sub = Lbl(page switch
            {
                Page.General => L.N("CHANGES APPLY IMMEDIATELY AND ARE SAVED · ANYTHING LEFT ON \"VALORANT\" FOLLOWS YOUR GAME"),
                Page.Keybinds => L.N("IMPORTED FROM VALORANT · CHANGE ANY BIND FOR VALTRAINER · BINDS YOU DON'T CHANGE FOLLOW YOUR GAME"),
                _ => L.N("VERSION · UPDATES · FOLDERS · OPEN-SOURCE LICENSES"),
            }, UiTheme.HudWide, 15, UiTheme.Dim);
        sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; // never pushes the tabs / BACK off screen
        sub.ClipText = true;
        title.AddChild(sub);
        head.AddChild(title);
        var tabs = HBox(6 * k);
        tabs.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foreach (var (pg, name) in new[] { (Page.General, L.N("GENERAL")), (Page.Keybinds, L.N("KEYBINDS")), (Page.About, L.N("ABOUT")) })
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
        else if (page == Page.Keybinds)
        {
            BuildKeybinds(cols); // SettingsKeybinds.cs
            col.AddChild(Lbl("Click a bind, then press a key or mouse button   ·   Esc cancels   ·   Backspace / Delete — back to VALORANT's   ·   right-click — unbind",
                UiTheme.Body, 13, UiTheme.Faint));
        }
        else
        {
            BuildAim(Column(cols, L.N("AIM & ACCOUNT"), null, 1f));
            BuildVideo(Column(cols, L.N("VIDEO & GAMEPLAY"), null, 1f));
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
        TickKeybinds(dt);
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
        // UI language: Auto (VALORANT's → Windows' → English) or a fixed one; applies at once (the screen rebuilds).
        v.AddChild(new SectionLabel { Text = "LANGUAGE", K = k });
        v.AddChild(Selector("Language", LanguageValue, StepLanguage, note: LanguageNote));
        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "VALORANT ACCOUNT", K = k });
        v.AddChild(Selector("Account", () =>
            {
                if (App.Accounts.Count == 0) return L.N("None found");
                var a = CurrentAccount();
                return L.Raw($"{a.ShortId}… · {a.Region} · {(a.Sens?.ToString("0.###", Inv) ?? "?")}");
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
            note: () =>
            {
                int n = App.Accounts.Count;
                return n == 0 ? L.N("No Valorant config folder found on this PC")
                    : CurrentAccount().IsLastUsed ? L.P(n, $"{n} found · this one was used last", $"{n} found · this one was used last")
                    : L.P(n, $"{n} found", $"{n} found");
            }));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "SENSITIVITY", K = k });
        v.AddChild(Segmented("Sensitivity source", new[] { "VALORANT", L.N("CUSTOM") }, () => St.UseSensOverride ? 1 : 0, i =>
        {
            bool custom = i == 1;
            if (custom && !St.UseSensOverride) St.SensOverride = App.Valorant.Sensitivity;
            St.UseSensOverride = custom;
        }));
        v.AddChild(SensRow());
        v.AddChild(Selector("Mouse DPI", () => St.Dpi.ToString(),
            d => St.Dpi = Dpis[Wrap(Array.IndexOf(Dpis, St.Dpi) + d, 0, Dpis.Length - 1)],
            note: () => L.N("Only for showing eDPI and cm/360")));
        v.AddChild(SensSummary());

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "ENEMIES", K = k });
        v.AddChild(Selector("Enemy highlight",
            () => St.EnemyColorOverride < 0 ? L.F($"Valorant ({L.T(UiTheme.EnemyColorNames[Math.Clamp(App.Valorant.EnemyHighlight, 0, 3)])})") : UiTheme.EnemyColorNames[St.EnemyColorOverride],
            d => St.EnemyColorOverride = Wrap(St.EnemyColorOverride + d, -1, 3),
            swatch: () => App.EnemyColor));
        v.AddChild(Segmented("Enemy outlines + fresnel", new[] { L.N("OFF"), L.N("ON") }, () => St.Outlines ? 1 : 0, i => St.Outlines = i == 1,
            note: () => L.N("Valorant: \"Hide outlines and fresnel\" off = ON")));
        v.AddChild(Segmented("Aim drill targets", new[] { "VALORANT", L.N("CLASSIC") }, () => St.TargetStyle == Target.StyleClassic ? 1 : 0,
            i => St.TargetStyle = i == 1 ? Target.StyleClassic : Target.StyleValorant,
            note: () => St.TargetStyle == Target.StyleClassic ? L.N("Solid spheres in the highlight colour") : L.N("Dark body + highlight outline, like VALORANT enemies")));

        // Crosshair: the switcher (every VALORANT profile on this PC, the Crosshair Finder's result, codes saved in
        // ValTrainer; SettingsCrosshair.cs). ValTrainer only: VALORANT is never changed.
        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "CROSSHAIR", K = k });
        v.AddChild(Selector("Crosshair", () => L.Raw(CrosshairList.CurrentEntry.Name), d => CrosshairList.Step(d),
            enabled: () => CrosshairList.All.Count > 1,
            note: () => CrosshairNote(CrosshairList.CurrentEntry),
            swatch: () => CrosshairList.CurrentEntry.Profile.Primary.Color));
        v.AddChild(NoteRow(() => CrosshairList.CurrentEntry.Source == CrosshairSource.Valorant && App.Valorant.Found ? App.Valorant.CrosshairNote : null));
        v.AddChild(CrosshairInfoRow());
        v.AddChild(new CrosshairPreview { K = k, BoxHeight = 120, Backdrop = CrosshairBackdrop });
        v.AddChild(CrosshairHintRow());
        v.AddChild(CodeRow("Code of the crosshair ValTrainer uses now", () => CrosshairCode.EffectiveCode));
        if (!string.IsNullOrWhiteSpace(St.LastFinderCrosshairCode))
            v.AddChild(CodeRow("Last Crosshair Finder result", () => St.LastFinderCrosshairCode ?? ""));
        v.AddChild(PasteRow());
        v.AddChild(SaveCurrentRow());
        v.AddChild(SavedCrosshairsList());
    }

    /// <summary>Picker order: Auto, then every shipped language (endonyms).</summary>
    static readonly string?[] LanguageOptions = new string?[] { null }.Concat(L.Shipped.Select(l => l.Code)).ToArray();

    static string LanguageValue()
    {
        if (L.Forced is { } forced) return L.Raw($"{L.Info(forced)?.Name ?? forced} (dev --lang)");
        if (St.Language is { } code) return L.Raw(L.Info(code)?.Name ?? code);
        string name = L.Info(L.AutoCode)?.Name ?? L.AutoCode;
        return L.AutoSource is { } src ? L.F($"Auto · {src}: {name}") : L.F($"Auto · {name}");
    }

    void StepLanguage(int d)
    {
        int i = Array.IndexOf(LanguageOptions, St.Language);
        St.Language = LanguageOptions[Wrap(i + d, 0, LanguageOptions.Length - 1)];
        if (St.Language == null) St.LanguageNotice = L.AutoCode; // picked by hand: no "now in …" notice later
        L.Set(St.Language); // the screen rebuilds in the new language (ScreenBase listens to L.Changed)
    }

    static string LanguageNote()
    {
        if (L.IsEnglish) return L.N("Auto follows your VALORANT language (then Windows')");
        if (L.Lang == L.PseudoCode) return L.N("Dev pseudo-language: plain English = not translatable yet");
        return L.Status switch
        {
            "machine" => L.N("Machine translation · help improve it: translations folder on ValTrainer's GitHub"),
            "" => L.N("Not translated yet: shows English · help translate it on ValTrainer's GitHub"),
            _ => L.N("Translated by the community · fixes welcome on ValTrainer's GitHub"),
        };
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
            lbl.Text = L.T(t) ?? "";
        }
        Refresh();
        refreshers.Add(Refresh);
        return row;
    }

    /// <summary>"Paste a crosshair code": adds a VALORANT code from the clipboard to the saved crosshairs and uses it (for
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
                Gfx.TextFit(d, UiTheme.Body, "Add a crosshair code to your list", 10 * k, Gfx.Mid(cy - 9 * k, ls), ls, UiTheme.Dim, d.Size.X - 10 * k);
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
            // saved to the list (a second code no longer replaces the first) and used; the same crosshair isn't saved twice
            CrosshairList.Add(DisplayServer.ClipboardGet(), null, out var msg);
            Flash(msg == "SAVED ✓" ? L.N("USED ✓") : msg); // msg is English (compared here); the button translates it
            Changed();
        }, 150, 36, 16); // as wide as SAVE CURRENT: "ALREADY SAVED ✓" fits
        paste.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        paste.TooltipText = L.T("Saves the crosshair code on your clipboard to SAVED CROSSHAIRS and uses it in ValTrainer. VALORANT isn't changed.");
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
            text.Text = h.Ok ? L.T("Your VALORANT crosshair stands out on common map colours.") : L.T(h.Long);
            text.TooltipText = h.Ok ? L.T(h.Long) : "";
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
        var body = Lbl(L.Raw(code()), UiTheme.Body, 13, UiTheme.Text); // a crosshair code: never looked up
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
        copy.TooltipText = L.T("Copy, then paste it in VALORANT: Settings → Crosshair → Import Profile Code");
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
                (L.N("SENSITIVITY"), sens.ToString("0.###", Inv)),
                ("eDPI", (sens * St.Dpi).ToString("0", Inv)),
                (L.N("CM / 360°"), PlayerView.Cm360(sens, St.Dpi).ToString("0.0", Inv)),
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
            () => St.WindowModeOverride < 0 ? L.F($"Valorant ({L.T(WindowModes[Math.Clamp(App.Valorant.WindowMode, 0, 2)])})") : WindowModes[St.WindowModeOverride],
            d => { St.WindowModeOverride = Wrap(St.WindowModeOverride + d, -1, 2); App.ApplyDisplay(); }));
        int monCount = Math.Max(1, DisplayServer.GetScreenCount());
        string MonName(int i) { var s = DisplayServer.ScreenGetSize(i); return $"#{i + 1} · {s.X}×{s.Y}"; }
        v.AddChild(Selector("Monitor",
            () => St.MonitorOverride < 0 ? L.F($"Valorant ({MonName(Math.Clamp(App.Valorant.MonitorIndex, 0, monCount - 1))})") : MonName(Math.Min(St.MonitorOverride, monCount - 1)),
            d => { St.MonitorOverride = Wrap(St.MonitorOverride + d, -1, monCount - 1); App.ApplyDisplay(); },
            enabled: () => monCount > 1 || St.MonitorOverride >= 0));
        v.AddChild(Selector("VSync",
            () => St.VSyncOverride < 0 ? L.F($"Valorant ({L.T(App.Valorant.VSync ? "On" : "Off")})") : St.VSyncOverride == 1 ? L.N("On") : L.N("Off"),
            d => { St.VSyncOverride = Wrap(St.VSyncOverride + d, -1, 1); App.ApplyFps(); }));
        v.AddChild(Selector("FPS cap",
            () => St.FpsCapOverride < 0 ? L.F($"Valorant ({(App.Valorant.FrameRateLimit <= 0 ? L.T("Unlimited") : App.Valorant.FrameRateLimit.ToString("0", Inv))})")
                : St.FpsCapOverride == 0 ? L.N("Unlimited") : St.FpsCapOverride.ToString(),
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
            note: () => L.N("Applies on next drill")));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "GAMEPLAY", K = k });
        v.AddChild(Selector("Difficulty tier", () => { var t = Difficulty.Get(St.Tier); return L.T(t.Name) + " · " + L.T(t.Ranks); },
            d => St.Tier = Wrap(St.Tier + d, 0, 4),
            emblem: () => St.Tier,
            note: () => L.F($"Bots react in ~{Difficulty.Get(St.Tier).Bot.ReactMs:0} ms")));
        v.AddChild(Segmented("Viewmodel (first-person gun)", new[] { L.N("OFF"), L.N("ON") }, () => St.ViewModel ? 1 : 0, i => St.ViewModel = i == 1,
            note: () => L.N("Applies on next drill")));
        v.AddChild(Segmented("Weapon hand", new[] { L.N("RIGHT"), L.N("LEFT") }, () => St.LeftHandedWeapon ? 1 : 0, i => St.LeftHandedWeapon = i == 1,
            note: () => !Main.I.Valorant.Found ? L.N("Applies on next drill")
                : Main.I.Valorant.LeftHanded ? L.N("Applies on next drill · VALORANT uses left") : L.N("Applies on next drill · VALORANT uses right")));
        // the gun every drill that offers a choice starts with (a pick made for one drill still wins there); the arrows step
        // through the buy menu, the value opens the gun picker
        var gunOptions = new List<WeaponKind?> { null };
        gunOptions.AddRange(Weapons.Buyable.Select(w => (WeaponKind?)w.Kind));
        v.AddChild(Selector("Preferred gun",
            () => WeaponPicks.Preferred() is { } pg ? L.Raw(GunPicker.GunName(pg)) : L.N("Each drill's default"),
            d =>
            {
                int i = gunOptions.IndexOf(WeaponPicks.Preferred());
                WeaponPicks.SetPreferred(gunOptions[Wrap(i + d, 0, gunOptions.Count - 1)]);
            },
            note: () => WeaponPicks.Preferred() == null ? L.N("Click to pick from the buy menu · B in a drill's preview") : L.N("For drills that offer it · your pick for a drill still wins"),
            onField: () => GunPicker.OpenPreferred(this, K, RefreshAll)));
        // gun skins (cosmetic): the arrows step the skin of every gun (guns with their own keep it), the value opens the picker
        var skinIds = new List<string?> { null };
        skinIds.AddRange(GunSkins.All.Where(s => GunSkins.IsUnlocked(s.Id)).Select(s => (string?)s.Id)); // the arrows skip locked lines
        v.AddChild(Selector("Gun skins", () => GunSkins.Summary(),
            d =>
            {
                int i = skinIds.IndexOf(GunSkins.Get(St.GunSkinAll)?.Id);
                GunSkins.SetDefault(skinIds[Wrap(i + d, 0, skinIds.Count - 1)]);
            },
            note: () => GunSkins.CustomCount() > 0 ? L.N("Arrows: every other gun · click to pick per gun") : L.N("Click to pick per gun, with a 3D preview · cosmetic only"),
            onField: () => SkinPicker.Open(this, K, closed: RefreshAll)));

        // skin effects (skins2-design §4.9): what upgraded skins may add on top of the factory gun; cosmetic only
        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "SKIN EFFECTS", Note = "Cosmetic only · never in Sens / Crosshair Finder", K = k });
        var offOn = new[] { L.N("OFF"), L.N("ON") };
        v.AddChild(Segmented("Skin sounds", offOn, () => St.SkinSounds ? 1 : 0, i => St.SkinSounds = i == 1,
            note: () => St.SkinSounds ? L.N("The skin's own shots, hits and kill sounds") : L.N("Factory sounds only")));
        v.AddChild(Segmented("Classic hit sounds", offOn, () => St.ClassicHitSounds ? 1 : 0, i => St.ClassicHitSounds = i == 1,
            note: () => St.ClassicHitSounds ? L.N("Factory body and headshot sounds on every skin") : L.N("Skins with their own hit sounds use them")));
        v.AddChild(Segmented("Kill effects", new[] { L.N("OFF"), L.N("GUN ONLY"), L.N("FULL") }, () => Math.Clamp(St.SkinKillFx, 0, 2), i => St.SkinKillFx = i,
            note: () => St.SkinKillFx switch { 0 => L.N("No kill effects"), 1 => L.N("A glow on your gun only"), _ => L.N("On your gun and where the bot falls") }));
        v.AddChild(Segmented("Finishers", offOn, () => St.SkinFinishers ? 1 : 0, i => St.SkinFinishers = i == 1,
            note: () => L.N("On the kill that wins the round")));
        v.AddChild(Segmented("Kill counter", offOn, () => St.SkinKillCounter ? 1 : 0, i => St.SkinKillCounter = i == 1,
            note: () => L.N("Digits on the gun of skins that have one")));
        v.AddChild(Selector("Skin effects volume", () => $"{St.SkinFxVolume * 100:0}%",
            d =>
            {
                St.SkinFxVolume = Math.Clamp(MathF.Round((St.SkinFxVolume + d * 0.05f) * 20f) / 20f, 0f, 1f);
                Sfx.I?.Play("kill1", 0.9f * St.SkinFxVolume);
            },
            note: () => L.N("Gameplay sounds always play at full volume"),
            bar: () => St.SkinFxVolume));
        // Dev-only: "--settings-skins" scrolls this column down to SKIN EFFECTS (screenshots).
        if (CmdLine.Dev && CmdLine.Has("--settings-skins") && v.GetParent() is ScrollContainer ssc)
            GetTree().CreateTimer(0.5).Timeout += () => { if (IsInstanceValid(ssc)) ssc.ScrollVertical = (int)ssc.GetVScrollBar().MaxValue; };

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
        // Keybind rows (VALORANT's binds, without ValTrainer's custom ones): hovering one shows every imported bind (both
        // slots) in a tooltip. Settings → KEYBINDS changes them for ValTrainer.
        void Keys(string label, Func<string> value)
        {
            var row = Info(label, value);
            row.MouseFilter = MouseFilterEnum.Pass;
            // like Keybinds.ListText, with the action names translated (key names stay as they are)
            string Tip() => L.T("VALORANT keybinds (primary · secondary)") + "\n" +
                            string.Join("\n", Keybinds.All.Select(a => $"{L.T(Keybinds.Label(a))}: {vp().Imported.Text(a)}")) +
                            (CustomBinds.Count(St.CustomBinds) > 0 ? "\n\n" + L.T("ValTrainer uses your custom binds on top of these (KEYBINDS tab)") : "");
            row.TooltipText = Tip();
            refreshers.Add(() => row.TooltipText = Tip());
        }

        // labels: L.N (the row translates them); "a / b" values: each part translated, key names never looked up
        Info(L.N("Source"), () => vp().Found ? vp().SourceText() : L.N("Not found — Valorant defaults"), () => vp().Found ? UiTheme.Good : UiTheme.Warn);
        Info(L.N("Sensitivity"), () => vp().SensFromFile ? vp().Sensitivity.ToString("0.####", Inv) : L.F($"{vp().Sensitivity.ToString(Inv)} (default)"));
        Info(L.N("Scoped / ADS multiplier"), () => $"{vp().ZoomedSensMult.ToString("0.###", Inv)} / {vp().AdsSensMult.ToString("0.###", Inv)}");
        Info(L.N("Sniper scope input"), () => vp().HoldToScope ? L.N("Hold") : L.N("Toggle"));
        Keys(L.N("Move (forward left back right)"), () => L.Raw(string.Join(" ", new[] { GameAction.MoveForward, GameAction.StrafeLeft, GameAction.MoveBack, GameAction.StrafeRight }.Select(a => vp().Imported.Short(a)))));
        Keys(L.N("Walk / crouch / jump"), () => L.Raw($"{vp().Imported.Text(GameAction.Walk)} / {vp().Imported.Text(GameAction.Crouch)} / {vp().Imported.Text(GameAction.Jump)}"));
        Keys(L.N("Fire / alt fire"), () => L.Raw($"{vp().Imported.Text(GameAction.Fire)} / {vp().Imported.Text(GameAction.AltFire)}"));
        Keys(L.N("Reload / use / plant-defuse"), () => L.Raw($"{vp().Imported.Text(GameAction.Reload)} / {vp().Imported.Text(GameAction.Use)} / {vp().Imported.Text(GameAction.UseSpike)}"));
        Keys(L.N("Abilities C Q E X"), () => L.Raw(string.Join(" ", Enumerable.Range(0, 4).Select(i => vp().Imported.Short(Keybinds.AbilityAction(i))))));
        Info(L.N("ValTrainer custom keybinds"), () => CustomBinds.Count(St.CustomBinds) is var n && n > 0 ? L.F($"{n} (KEYBINDS tab)") : L.N("None"), () => CustomBinds.Count(St.CustomBinds) > 0 ? UiTheme.Warn : UiTheme.Text);
        Info(L.N("Resolution"), () => vp().Letterbox ? L.F($"{vp().ResX} × {vp().ResY} (letterbox)") : $"{vp().ResX} × {vp().ResY}");
        Info(L.N("Display mode / monitor"), () => $"{L.T(WindowModes[Math.Clamp(vp().WindowMode, 0, 2)])} / #{vp().MonitorIndex + 1}");
        Info(L.N("VSync / FPS limit"), () => $"{L.T(vp().VSync ? "On" : "Off")} / {(vp().FrameRateLimit <= 0 ? L.T("Unlimited") : vp().FrameRateLimit.ToString("0", Inv))}");
        Info(L.N("Material / Texture / Detail"), () => $"{L.T(Q(vp().MaterialQuality))} / {L.T(Q(vp().TextureQuality))} / {L.T(Q(vp().DetailQuality))}");
        Info(L.N("UI quality / Anisotropic"), () => $"{L.T(Q(vp().UIQuality))} / {(vp().Anisotropic < 0 ? L.T("Default") : vp().Anisotropic + "x")}");
        Info(L.N("Anti-aliasing"), () => L.T(AaName(vp().AntiAliasing)) + (vp().WantsMsaa ? " → MSAA 4x" : ""));
        Info(L.N("Improve clarity / Bloom"), () => $"{L.T(B(vp().ImproveClarity))} / {L.T(Q(vp().BloomQuality))}");
        Info(L.N("NVIDIA Reflex"), () => vp().Reflex switch { -1 => L.N("Default"), 0 => L.N("Off"), 1 => L.N("On"), 2 => L.N("On + Boost"), _ => vp().Reflex.ToString() });
        Info(L.N("Enemy highlight"), () => UiTheme.EnemyColorNames[Math.Clamp(vp().EnemyHighlight, 0, 3)], () => UiTheme.EnemyColors[Math.Clamp(vp().EnemyHighlight, 0, 3)],
            () => UiTheme.EnemyColors[Math.Clamp(vp().EnemyHighlight, 0, 3)]);
        Info(L.N("Show FPS counter"), () => vp().ShowFps ? L.N("On") : L.N("Off"));
        Info(L.N("Crosshair profile"), () => L.Raw(vp().Crosshair.Name));

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
                int n = App.Accounts.Count;
                Gfx.Text(d, UiTheme.Body, L.P(n, $"Re-imported · {n} account", $"Re-imported · {n} accounts"), 0, Gfx.Mid(d.Size.Y / 2, fs), fs,
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
        var v = Column(cols, L.Raw("VALTRAINER"), L.F($"version {AppInfo.Version}"), 1f);

        v.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 86 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(54, k), vs = UiTheme.Fs(18, k), ss = UiTheme.Fs(14, k);
                float y = 50 * k, x = 10 * k;
                // the logo: never translated
                Gfx.TextRaw(d, UiTheme.Display, "VAL", x, y, fs, UiTheme.Accent);
                x += Gfx.RawW(UiTheme.Display, "VAL", fs) + 1 * k;
                Gfx.TextRaw(d, UiTheme.Display, "TRAINER", x, y, fs, UiTheme.Text);
                x += Gfx.RawW(UiTheme.Display, "TRAINER", fs) + 14 * k;
                Gfx.TextRaw(d, UiTheme.HudWide, "v" + AppInfo.Version, x, y, vs, UiTheme.Dim);
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
        Info(L.N("Version"), () => App.Dev ? L.F($"{AppInfo.Version} (dev run)") : AppInfo.Version);
        Info(L.N("Renderer"), () => SysInfo.RendererName, () => SysInfo.IsCompatibility ? UiTheme.Warn : UiTheme.Text);
        Info(L.N("Graphics card"), () => SysInfo.GpuName.Length > 0 ? L.Raw(SysInfo.GpuName) : L.N("Unknown"));
        Info(L.N("Graphics quality"), () => St.AutoQualityReason is { } why ? L.F($"{L.T(Qualities[Math.Clamp(St.Quality, 0, 3)])} · auto ({L.T(why)})") : Qualities[Math.Clamp(St.Quality, 0, 3)]);

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "FOLDERS", K = k });
        v.AddChild(FolderRow("Settings, stats & recordings", Paths.DataDir));
        if (Paths.DataDirProblemText is { } problem) v.AddChild(Lbl(problem, UiTheme.Body, 13, UiTheme.Warn));
        v.AddChild(RecordingsRow());
        v.AddChild(FolderRow("Logs (attach godot.log to bug reports)", Paths.LogDir));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "UPDATES", K = k });
        // Opt-in (see UpdatesPrompt): ON here is the same yes as ENABLE UPDATES on the first-launch prompt.
        v.AddChild(Segmented("Check for updates", new[] { L.N("OFF"), L.N("ON") }, () => St.AutoUpdates ? 1 : 0,
            i =>
            {
                St.CheckUpdates = i == 1;
                St.UpdatesConsent = i == 1;
                UpdatesPrompt.Pending = false;
                UpdateCheck.EnabledChanged();
            },
            note: () => !AppInfo.HasRepo ? L.N("Not available: this build has no GitHub page set")
                : St.AutoUpdates ? L.N("At startup and every 6 hours, asks GitHub whether a newer release is out (nothing about you is sent)")
                : L.N("Off: ValTrainer makes no network requests. ON asks GitHub at startup and every 6 hours; CHECK NOW asks once")));
        var auto = Segmented("Download updates automatically", new[] { L.N("OFF"), L.N("ON") }, () => St.AutoDownloadUpdates ? 1 : 0,
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
            check.TooltipText = UpdateCheck.CanCheck ? L.T("Ask GitHub once now whether a newer release is out") : UpdateCheck.DisplayMessage ?? "";
            status.TooltipText = UpdateCheck.Status == UpdateCheck.State.Failed ? UpdateCheck.DisplayMessage ?? "" : "";
            bool ready = Updater.ReadyVersion != null;
            get.Visible = ready || (UpdateCheck.NewerAvailable && AppInfo.HasRepo && Updater.Status != Updater.State.Applying);
            get.Label = ready ? L.N("RESTART TO UPDATE") : L.F($"GET {UpdateCheck.Latest}");
            get.TooltipText = !ready ? L.T("Opens the release page in your browser")
                : Updater.NeedsElevation ? L.T("Installs the update and restarts ValTrainer (Windows asks for permission)")
                : L.T("Installs the update and restarts ValTrainer");
            auto.Refresh();
            get.QueueRedraw();
            check.QueueRedraw();
            status.QueueRedraw();
        });

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "SUPPORT", K = k });
        v.AddChild(Segmented("Support reminders", new[] { L.N("OFF"), L.N("ON") }, () => St.SupportReminders ? 1 : 0,
            i => { St.SupportReminders = i == 1; SupportPrompt.Pending = false; },
            note: () => St.SupportReminders ? L.N("Now and then the menu shows the Buy Me a Coffee / Ko-fi panel (at most every 14 days)")
                : L.N("Off: the panel only opens from the menu's SUPPORT VALTRAINER button")));
        var jars = HBox(12 * k);
        jars.CustomMinimumSize = new Vector2(0, 52 * k);
        jars.AddChild(Spacer(4 * k, 0));
        foreach (var (label, url) in new[] { (L.N("BUY ME A COFFEE"), AppInfo.BuyMeACoffeeUrl), ("KO-FI", AppInfo.KofiUrl) })
        {
            var b = Btn(label, VButton.Look.Secondary, () => AppInfo.OpenSupport(url), label.Length > 6 ? 210 : 120, 40, 17);
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            b.TooltipText = L.F($"Opens {url.Replace("https://", "")} in your browser");
            jars.AddChild(b);
        }
        v.AddChild(jars);
        v.AddChild(SupporterRow());
        v.AddChild(claimRow);
        // Dev-only: "--settings-support" scrolls this column down to SUPPORT (screenshots).
        if (CmdLine.Dev && CmdLine.Has("--settings-support") && v.GetParent() is ScrollContainer sup)
            GetTree().CreateTimer(0.5).Timeout += () => { if (IsInstanceValid(sup)) sup.ScrollVertical = (int)(sup.GetVScrollBar().MaxValue * 0.62); };

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "HELP & FEEDBACK", K = k });
        // flows onto a second row when the column is too narrow for all five buttons
        var help = new HFlowContainer();
        help.AddThemeConstantOverride("h_separation", (int)(10 * k));
        help.AddThemeConstantOverride("v_separation", (int)(8 * k));
        help.CustomMinimumSize = new Vector2(0, 52 * k);
        var discord = Btn("DISCORD", VButton.Look.Secondary, () => AppInfo.OpenSupport(AppInfo.DiscordUrl), 130, 40, 17);
        discord.TooltipText = L.T("Opens the ValTrainer Discord invite: help, bug reports, suggestions and voice chats");
        discord.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        help.AddChild(discord);
        var bug = Btn("REPORT A BUG", VButton.Look.Secondary, () => ShowBugReport(), 170, 40, 17);
        bug.TooltipText = L.T("Describe the problem and send it to #bug-reports on the ValTrainer Discord. You see everything before it's sent");
        bug.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        help.AddChild(bug);
        var idea = Btn("SUGGEST A FEATURE", VButton.Look.Secondary, () => ShowBugReport(true), 210, 40, 17);
        idea.TooltipText = L.T("Send an idea for a drill, an agent, a map or a setting to #suggestions on the ValTrainer Discord");
        idea.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        help.AddChild(idea);
        if (AppInfo.HasRepo)
        {
            var site = Btn("GITHUB PAGE", VButton.Look.Secondary, () => AppInfo.OpenGitHub(AppInfo.RepoUrl), 160, 40, 17);
            site.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            help.AddChild(site);
        }
        var copy = Btn("COPY SYSTEM INFO", VButton.Look.Secondary, () =>
        {
            DisplayServer.ClipboardSet(SystemInfoText());
            copyFlash = 2.5f;
        }, 200, 40, 17);
        copy.TooltipText = L.T("Version, renderer, GPU and folders, for a bug report");
        copy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        help.AddChild(copy);
        var copied = new DrawBox { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(80 * k, 40 * k) };
        copied.OnDraw = d =>
        {
            if (copyFlash <= 0) return;
            int fs = UiTheme.Fs(14, k);
            Gfx.TextFit(d, UiTheme.Body, "Copied", 0, Gfx.Mid(d.Size.Y / 2, fs), fs, new Color(UiTheme.Good, Mathf.Min(1f, copyFlash)), d.Size.X);
        };
        refreshers.Add(copied.QueueRedraw);
        help.AddChild(copied);
        v.AddChild(Margins(help, 16 * k, 0, 0, 0)); // lines up with BUY ME A COFFEE
        // Dev-only: "--about-bottom" scrolls the About column down to HELP & FEEDBACK (screenshots).
        if (CmdLine.Dev && CmdLine.Has("--about-bottom") && v.GetParent() is ScrollContainer sc)
            GetTree().CreateTimer(0.5).Timeout += () => { if (IsInstanceValid(sc)) sc.ScrollVertical = (int)sc.GetVScrollBar().MaxValue; };

        BuildLicenses(cols);
    }

    /// <summary>HELP &amp; FEEDBACK → REPORT A BUG (it also offers GitHub) or SUGGEST A FEATURE: the form over this screen.</summary>
    void ShowBugReport(bool idea = false)
    {
        if (GetChildren().Any(c => c is BugReportPanel)) return;
        AddChild(new BugReportPanel(K, "settings", () => { }, idea));
    }

    /// <summary>Settings → About note under "Download updates automatically": how this copy updates (or why it can't).</summary>
    static string AutoUpdateNote()
    {
        if (Updater.Status == Updater.State.Off)
            return Updater.DisplayMessage is { } why ? L.F($"Not available: {why}") : L.N("Not available: this copy can't update itself");
        return Updater.Kind switch
        {
            InstallKind.Portable => L.N("Downloads new versions in the background; ValTrainer.exe is replaced when you restart"),
            InstallKind.InstalledAdmin => L.N("Downloads new versions in the background; RESTART TO UPDATE installs them (Windows asks for permission)"),
            _ => L.N("Downloads new versions in the background; they install when you restart ValTrainer"),
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
        UpdateCheck.State.Checking => (L.N("Checking GitHub…"), UiTheme.Dim),
        UpdateCheck.State.Available => (L.F($"ValTrainer {UpdateCheck.Latest} is available"), UiTheme.Good),
        UpdateCheck.State.UpToDate => (St.LastUpdateCheck > 0 ? L.F($"You have the latest version · checked {Ago(St.LastUpdateCheck)}") : L.N("You have the latest version"), UiTheme.Dim),
        UpdateCheck.State.Failed => (string.IsNullOrWhiteSpace(UpdateCheck.Message) ? L.N("Couldn't check for updates") : L.F($"Couldn't check for updates: {UpdateCheck.DisplayMessage}"), UiTheme.Warn),
        UpdateCheck.State.Off => (UpdateCheck.DisplayMessage ?? L.N("Off"), UiTheme.Faint),
        _ => ("", UiTheme.Dim),
    };

    /// <summary>"just now" / "5 min ago" / "3 h ago" / "2 d ago", translated (it goes into a translated sentence).</summary>
    static string Ago(long unix)
    {
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (d.TotalMinutes < 1) return L.T("just now");
        if (d.TotalHours < 1) return L.F($"{(int)d.TotalMinutes} min ago");
        if (d.TotalDays < 1) return L.F($"{(int)d.TotalHours} h ago");
        return L.F($"{(int)d.TotalDays} d ago");
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
                Gfx.TextFit(d, UiTheme.Body, L.Raw(path), 10 * k, Gfx.Mid(cy + 10 * k, ps), ps, UiTheme.Text, d.Size.X - 10 * k);
            },
        };
        row.AddChild(txt);
        var open = Btn("OPEN", VButton.Look.Secondary, () => AppInfo.OpenFolder(path), 96, 36, 16);
        open.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        open.TooltipText = path;
        row.AddChild(open);
        return row;
    }

    /// <summary>"Sensitivity": arrows (±0.01, Shift ±0.001) around a box you click and type an exact value into (Enter).
    /// Typing or an arrow switches the source to Custom, starting from VALORANT's sensitivity.</summary>
    SettingRow SensRow()
    {
        float k = K;
        var row = new SettingRow
        {
            K = k, Label = "Sensitivity", Stacked = stacked,
            Note = () => St.UseSensOverride ? L.N("Custom · type a value or use the arrows (Shift ±0.001)") : L.N("From VALORANT · type a value or use the arrows to set your own"),
        };
        var sel = HBox(6 * k);
        sel.CustomMinimumSize = new Vector2(stacked ? 0 : 250 * k, 0);
        sel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sel.SizeFlagsStretchRatio = 1.15f;
        var l = new VButton { Kind = VButton.Look.Arrow, Dir = -1, K = k, CustomMinimumSize = new Vector2(36 * k, 36 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var r = new VButton { Kind = VButton.Look.Arrow, Dir = 1, K = k, CustomMinimumSize = new Vector2(36 * k, 36 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var edit = new LineEdit
        {
            MaxLength = 8, Alignment = HorizontalAlignment.Center, SelectAllOnFocus = true, CustomMinimumSize = new Vector2(0, 36 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = L.T("Click, type your sensitivity (e.g. 0.35), press Enter"),
        };
        StyleEdit(edit, k, 18);
        string Shown() => App.Sens.ToString("0.000", Inv);
        edit.Text = Shown();
        void Step(int d)
        {
            if (!St.UseSensOverride) { St.UseSensOverride = true; St.SensOverride = App.Valorant.Sensitivity; }
            float step = Input.IsKeyPressed(Key.Shift) ? 0.001f : 0.01f;
            St.SensOverride = Math.Clamp(MathF.Round((St.SensOverride + d * step) * 1000f) / 1000f, SensInput.Min, SensInput.Max);
            Changed();
        }
        void Apply()
        {
            if (SensInput.TryParse(edit.Text, out var sens))
            {
                St.UseSensOverride = true;
                St.SensOverride = sens;
                Changed();
            }
            edit.Text = Shown(); // invalid input: back to the current value
        }
        l.Pressed += () => Step(-1);
        r.Pressed += () => Step(1);
        edit.TextSubmitted += _ => { Apply(); edit.ReleaseFocus(); };
        edit.FocusExited += Apply;
        sel.AddChild(l); sel.AddChild(edit); sel.AddChild(r);
        row.AddChild(sel);
        refreshers.Add(() =>
        {
            if (!edit.HasFocus() && edit.Text != Shown()) edit.Text = Shown();
            edit.AddThemeColorOverride("font_color", St.UseSensOverride ? UiTheme.Warn : UiTheme.Text);
            row.Refresh();
        });
        return row;
    }

    /// <summary>Supporter code (Core/Supporter.cs): paste, APPLY; a valid code unlocks every skin, now and in future versions.
    /// Checked offline; the code is kept in the settings file.</summary>
    SettingRow SupporterRow()
    {
        float k = K;
        var row = new SettingRow
        {
            K = k, Label = "Supporter code", Stacked = stacked,
            Note = () => Supporter.Current is { } sp ? L.F($"Thank you, {sp.Name}! Every skin is unlocked, new ones too")
                : string.IsNullOrWhiteSpace(St.SupporterCode) ? L.N("Supporters get a code that unlocks every skin, new ones too: paste it here, or use Get my code below")
                : L.N("This code isn't valid: paste the whole code"),
        };
        var box = HBox(8 * k);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.SizeFlagsStretchRatio = 1.15f;
        var edit = new LineEdit
        {
            MaxLength = 200, PlaceholderText = "VT-…", SelectAllOnFocus = true, CustomMinimumSize = new Vector2(stacked ? 0 : 220 * k, 36 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = L.T("Paste your supporter code and press APPLY"),
        };
        StyleEdit(edit, k, 15);
        edit.Text = St.SupporterCode ?? "";
        void Apply()
        {
            string t = edit.Text.Trim();
            St.SupporterCode = t.Length == 0 ? null : t;
            St.Save();
            ValTrainer.Progression.Progress.RaiseChanged(); // menu strip, progress screen
            GunSkins.NotifyChanged();     // pickers redraw, the gun in hand re-skins
            RefreshAll();
        }
        var apply = Btn(L.N("APPLY"), VButton.Look.Secondary, Apply, 110, 36, 16);
        apply.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        edit.TextSubmitted += _ => { Apply(); edit.ReleaseFocus(); };
        box.AddChild(edit); box.AddChild(apply);
        row.AddChild(box);
        refreshers.Add(() =>
        {
            edit.AddThemeColorOverride("font_color", Supporter.Active ? UiTheme.Good : string.IsNullOrWhiteSpace(St.SupporterCode) ? UiTheme.Text : UiTheme.Warn);
            row.Refresh();
        });
        // GET MY CODE: the email the donation was made with → the code (one request, only on the button)
        claimRow = new SettingRow
        {
            K = k, Label = "Get my code", Stacked = stacked,
            Note = () => claimNote ?? L.N("Donated on Ko-fi or Buy Me a Coffee? Enter the email you paid with"),
        };
        var cbox = HBox(8 * k);
        cbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cbox.SizeFlagsStretchRatio = 1.15f;
        var mail = new LineEdit
        {
            MaxLength = 254, PlaceholderText = "you@example.com", CustomMinimumSize = new Vector2(stacked ? 0 : 220 * k, 36 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = L.T("The email you donated with. ValTrainer asks the supporter server once, when you press GET"),
        };
        StyleEdit(mail, k, 15);
        async void Claim()
        {
            if (claiming || string.IsNullOrWhiteSpace(mail.Text)) return;
            claiming = true;
            claimNote = L.N("Looking up your donation…");
            RefreshAll();
            var (code, err) = await Supporter.Claim(mail.Text);
            claiming = false;
            if (!IsInstanceValid(this)) return;
            if (code != null)
            {
                edit.Text = code;
                Apply();
                claimNote = L.N("Found it! Your code is filled in above and applied");
            }
            else claimNote = err switch
            {
                "rate_limited" => L.N("Too many tries: wait an hour and try again"),
                "bad_email" => L.N("That doesn't look like an email address"),
                "network" => L.N("Couldn't reach the supporter server: check your connection and try again"),
                _ => L.N("No donation found for this email yet. Just donated? Try again in a minute, or ask on Discord"),
            };
            RefreshAll();
        }
        var get = Btn(L.N("GET"), VButton.Look.Secondary, Claim, 110, 36, 16);
        get.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        mail.TextSubmitted += _ => { Claim(); mail.ReleaseFocus(); };
        cbox.AddChild(mail);
        cbox.AddChild(get);
        claimRow.AddChild(cbox);
        refreshers.Add(() => { claimRow.Visible = !Supporter.Active; get.Disabled = claiming; claimRow.Refresh(); });
        // Dev-only: "--supporter-claim <email>" types the email and presses GET after 1 s (screenshots of the live claim).
        if (CmdLine.DevAfter("--supporter-claim") is { } devMail)
            GetTree().CreateTimer(1.0).Timeout += () => { if (IsInstanceValid(mail)) { mail.Text = devMail; Claim(); } };
        return row;
    }

    SettingRow claimRow = null!;
    string? claimNote;
    bool claiming;

    /// <summary>Text box look shared with the rest of the settings (dark field, accent border while typing).</summary>
    internal static void StyleEdit(LineEdit e, float k, int fontPx = 16, Font? font = null)
    {
        StyleBoxFlat Box(Color border, bool fill) => new()
        {
            BgColor = new Color(0.06f, 0.1f, 0.14f, 0.85f), DrawCenter = fill, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8 * k, ContentMarginRight = 8 * k, ContentMarginTop = 4 * k, ContentMarginBottom = 4 * k,
        };
        e.AddThemeStyleboxOverride("normal", Box(new Color(UiTheme.Text, 0.2f), true));
        e.AddThemeStyleboxOverride("focus", Box(UiTheme.Accent, false));
        e.AddThemeFontOverride("font", font ?? UiTheme.Body);
        e.AddThemeFontSizeOverride("font_size", UiTheme.Fs(fontPx, k));
        e.AddThemeColorOverride("font_color", UiTheme.Text);
        e.AddThemeColorOverride("font_placeholder_color", UiTheme.Faint);
        e.AddThemeColorOverride("caret_color", UiTheme.Accent);
        e.AddThemeColorOverride("selection_color", new Color(UiTheme.Accent, 0.35f));
    }

    /// <summary>Rewind recordings: folder, size, what's kept automatically, OPEN and DELETE ALL (click twice).</summary>
    Control RecordingsRow()
    {
        float k = K;
        string dir = RecordingStore.Dir;
        var usage = RecordingStore.Usage(dir);
        var row = HBox(12 * k);
        row.CustomMinimumSize = new Vector2(0, 92 * k);
        var txt = new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                float cy = d.Size.Y / 2;
                int ls = UiTheme.Fs(14, k), ns = UiTheme.Fs(12, k);
                string mb = (usage.Bytes / 1048576.0).ToString(usage.Bytes < 10 * 1048576L ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture);
                Gfx.TextFit(d, UiTheme.Body, L.P(usage.Runs, $"Rewind recordings · {usage.Runs} run · {mb} MB", $"Rewind recordings · {usage.Runs} runs · {mb} MB"), 10 * k, Gfx.Mid(cy - 29 * k, ls), ls, UiTheme.Dim, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, L.Raw(dir), 10 * k, Gfx.Mid(cy - 10 * k, ls), ls, UiTheme.Text, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, L.F($"Kept: your last {RecordingStore.KeepRecent} runs + examples of problems the coach still sees."), 10 * k, Gfx.Mid(cy + 9 * k, ns), ns, UiTheme.Faint, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, L.F($"Deleted automatically: problems you fixed, and runs older than {RecordingStore.MaxAgeDays} days."), 10 * k, Gfx.Mid(cy + 25 * k, ns), ns, UiTheme.Faint, d.Size.X - 10 * k);
            },
        };
        row.AddChild(txt);
        var open = Btn("OPEN", VButton.Look.Secondary, () => AppInfo.OpenFolder(dir), 96, 36, 16);
        open.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        open.TooltipText = dir + "\n" + L.T("These are ValTrainer recordings (.vtt run data + .vtr 3D replay), played back inside ValTrainer's Rewind — not video files.");
        row.AddChild(open);
        VButton? del = null;
        ulong armedAt = 0;
        del = Btn("DELETE ALL", VButton.Look.Ghost, () =>
        {
            ulong now = Time.GetTicksMsec();
            if (armedAt == 0 || now - armedAt > 4000)
            {
                // first click arms it: "CONFIRM?" for 4 s
                armedAt = now;
                del!.Label = "CONFIRM?";
                del.QueueRedraw();
                GetTree().CreateTimer(4.0).Timeout += () =>
                {
                    if (!IsInstanceValid(del) || armedAt == 0 || Time.GetTicksMsec() - armedAt < 3900) return;
                    armedAt = 0; del.Label = "DELETE ALL"; del.QueueRedraw();
                };
                return;
            }
            armedAt = 0;
            if (!App.Dev) RecordingStore.DeleteAll(dir); // dev runs never touch the player's data
            usage = RecordingStore.Usage(dir);
            del!.Label = "DELETE ALL";
            del.QueueRedraw();
            txt.QueueRedraw();
        }, 132, 36, 16);
        del.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        del.TooltipText = L.T("Deletes every Rewind recording. Your stats, scores and coach results are kept.");
        row.AddChild(del);
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
                     // brand names stay as they are
                     (AboutLicenses.Part.ValTrainer, L.Raw("VALTRAINER")), (AboutLicenses.Part.Godot, L.Raw("GODOT")),
                     (AboutLicenses.Part.GodotThirdParty, L.N("ENGINE PARTS")), (AboutLicenses.Part.DotNet, L.Raw(".NET")), (AboutLicenses.Part.Assets, L.N("ASSETS")),
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
        Func<Color?>? swatch = null, Func<float>? bar = null, Func<int>? emblem = null, Action? onField = null)
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
        field.Pressed += () => { if (onField != null) { if (enabled == null || enabled()) onField(); } else Do(1); };
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
        string? note = L.T(Note?.Invoke()); // translated before it is measured and split into lines
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
        string? note = L.T(Note?.Invoke());
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
