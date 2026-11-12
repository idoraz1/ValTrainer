using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>
/// The gun skin COLLECTION (research: skins-design-1.19.md §2.8, skins2-design-1.19.md §4.10): an overlay built in code like
/// <see cref="GunPicker"/>.
/// <list type="bullet">
/// <item>Left: the 20 buyable guns in buy-menu groups with the skin each wears (a chosen but locked skin shows in amber).</item>
/// <item>Centre: a live 3D preview (<see cref="SkinPreview"/>: TURNTABLE, IN HAND, SHOWCASE range where a dummy is shot so
/// bullet and kill effects, finishers and sounds can be seen and heard; INSPECT and RELOAD play the gun's inspect and a
/// reload, with the level's flourish and special reload); under it the line's name and tier, its LEVEL TRACK
/// (<see cref="SkinLevelTrack"/>: what each level adds, locked / unlocked with the requirement and progress from
/// <see cref="Progress.Status"/>), the variant swatches and EQUIP.</item>
/// <item>Right: the skin lines grouped by tier (Core, Signature, Apex) with our own tier pips.</item>
/// </list>
/// Clicking a line, a level or a variant previews it and, when it is unlocked, equips it on the selected gun at once (pick =
/// apply, like the gun picker); locked items preview but EQUIP shows how to unlock them. A level below the highest unlocked
/// one is stored explicitly (VALORANT's "equip a lower level"); otherwise the choice says "highest unlocked", so new unlocks
/// upgrade it. The footer applies the shown choice to every gun, resets the gun (back to the every-gun skin) or resets all.
/// Skins are cosmetic: the first-person gun only.
/// <para>Opened by the main menu's SKINS, the gun picker's footer, Settings → General → "Gun skins" and the drill preview (K).
/// Keys: ←/→ lines, ↑/↓ guns, 1–5 levels, V variants, Y inspect, R reload, Enter equips, Tab switches the view, Esc closes.
/// Dev: <c>--screen skins</c>
/// opens it over the menu; <c>--skins-gun &lt;Gun&gt;</c>, <c>--skins-skin &lt;line[/variant][@level]&gt;</c> (shows it; also
/// <c>--skins-line</c>, <c>--skins-level</c>, <c>--skins-variant</c>), <c>--skins-apply &lt;choice&gt;</c> (applies it after 3 s:
/// persistence tests), <c>--skins-view turntable|hand|showcase</c>, <c>--skins-action inspect|reload</c> (after 2 s:
/// screenshots).</para>
/// </summary>
public partial class SkinPicker : Control
{
    static readonly (WeaponClass Class, string Name)[] Groups =
    {
        (WeaponClass.Sidearm, L.N("SIDEARMS")), (WeaponClass.SMG, L.N("SMGS")), (WeaponClass.Shotgun, L.N("SHOTGUNS")),
        (WeaponClass.Rifle, L.N("RIFLES")), (WeaponClass.Sniper, L.N("SNIPERS")), (WeaponClass.Heavy, L.N("MACHINE GUNS")),
    };

    const int TileCols = 4;
    readonly float k;
    WeaponKind gun;
    /// <summary>The line tiles in order: factory (null) first, then the catalogue (grouped by tier).</summary>
    readonly List<SkinLine?> lines = SkinCatalog.Lines.Prepend(null).ToList();
    readonly List<SkinGunRow> rows = new();
    readonly List<SkinSwatch> tiles = new();
    readonly List<SkinSwatch> variantTiles = new();
    readonly List<SkinVariant?> variantOf = new();
    SkinPreview preview = null!;
    DrawBox info = null!;
    SkinLevelTrack track = null!;
    HBoxContainer variantBox = null!;
    DrawBox variantLabel = null!;
    VButton turnBtn = null!, handBtn = null!, showBtn = null!, inspectBtn = null!, reloadBtn = null!, resetGun = null!, resetAll = null!, applyAll = null!, equipBtn = null!;
    Label viewHint = null!;
    ScrollContainer? gunScroll, lineScroll;
    int focus;                 // the selected line tile
    int level = 1;             // the previewed level of the selected line
    SkinVariant? variant;      // the previewed variant (null = base palette)
    bool variantClicked;       // the status line explains the variant (it was the last thing clicked)
    int hovered = -1;          // line tile under the mouse (previews it)
    string? variantsFor = "?"; // the line the variant strip was built for
    string? confirm;           // "all" / "reset": the footer button waiting for a second click
    float confirmT;
    bool done;
    SkinPreview.View view = SkinPreview.View.Turntable;
    /// <summary>Called once when the overlay goes away.</summary>
    public Action? Closed;

    public SkinPicker(float k, WeaponKind gun)
    {
        this.k = k;
        this.gun = GunSkins.Skinnable(gun) ? gun : WeaponKind.Vandal;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 11; // above the gun picker when opened from its footer
    }

    /// <summary>Opens the picker over <paramref name="parent"/> on <paramref name="gun"/> (null: the preferred gun, else the
    /// Vandal), showing <paramref name="itemId"/> (an <see cref="ItemIds"/> id: an unlock toast's item) when given. One at a time.</summary>
    public static SkinPicker? Open(Node parent, float k, WeaponKind? gun = null, Action? closed = null, string? itemId = null)
    {
        if (parent.GetChildren().Any(c => c is SkinPicker sp && !sp.done)) return null;
        var p = new SkinPicker(k, gun ?? WeaponPicks.Preferred() ?? WeaponKind.Vandal) { Closed = closed, showItem = itemId };
        parent.AddChild(p);
        return p;
    }

    // ---------------- what is shown ----------------

    SkinLine? Selected => lines[focus];
    bool Hovering => hovered >= 0 && hovered != focus;
    SkinLine? ShownLine => lines[Hovering ? hovered : focus];

    /// <summary>The preview: the hovered line at its default, else the selected line at the picked level and variant.</summary>
    SkinState? ShownState
    {
        get
        {
            if (ShownLine is not { } l) return null;
            if (!Hovering) return new SkinState(l, Math.Clamp(level, 1, l.MaxLevel), variant);
            var (lv, v) = DefaultFor(l);
            return new SkinState(l, lv, v);
        }
    }

    /// <summary>What a line shows when it is picked: the gun's own choice of it (even a locked one), else its highest
    /// unlocked level in the base palette (level 1 when it is locked).</summary>
    (int Level, SkinVariant? Variant) DefaultFor(SkinLine l)
    {
        if (GunSkins.ChoiceFor(gun) is { } c && c.Line == l.Id)
        {
            var worn = GunSkins.StateFor(gun);
            int lv = c.Level > 0 ? c.Level : worn is { } w && w.Line.Id == l.Id ? w.Level : Math.Max(1, GunSkins.TopLevel(l));
            return (Math.Clamp(lv, 1, l.MaxLevel), l.Variant(c.Variant));
        }
        return (Math.Max(1, GunSkins.TopLevel(l)), null);
    }

    /// <summary>The selected line at the picked level and variant (what EQUIP / APPLY TO ALL wear); null = factory.</summary>
    SkinState? SelState => Selected is { } l ? new SkinState(l, Math.Clamp(level, 1, l.MaxLevel), variant) : null;

    static string? LockOf(SkinState? s) => s is { } x ? GunSkins.LockedItem(x.Line, x.Level, x.Variant) : null;

    /// <summary>The first locked item of what is shown (null: it can be equipped).</summary>
    string? ShownLock => LockOf(ShownState);
    /// <summary>The first locked item of the selection (null: EQUIP can wear it).</summary>
    string? SelLock => LockOf(SelState);

    /// <summary>The gun wears exactly <paramref name="s"/> (line, level, variant; null = factory).</summary>
    bool Worn(SkinState? s)
    {
        var w = GunSkins.StateFor(gun);
        if (s is not { } a) return w == null;
        return w is { } b && a.Line.Id == b.Line.Id && a.Level == b.Level && a.Variant?.Id == b.Variant?.Id;
    }

    int IndexOf(string? lineId) => Math.Max(0, lines.FindIndex(l => l?.Id == lineId));

    /// <summary>The tile of the gun's choice (a locked choice too: the picker opens on what the player picked).</summary>
    int ChosenIndex => IndexOf(GunSkins.ChoiceFor(gun)?.Line);

    static string ItemName(SkinLine l, string item)
    {
        if (!ItemIds.TryParse(item, out _, out int n, out var v)) return L.T(l.Name);
        if (v != null) return L.F($"{L.T(l.Variant(v)?.Name ?? v)} variant");
        return n > 1 ? L.F($"Level {n}") : L.T(l.Name);
    }

    // ---------------- layout ----------------

    public override void _Ready()
    {
        GunIcons.Warm();
        SkinSwatches.Warm(); // also compiles the skin shaders before a drill needs them
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.86f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var vs = GetViewportRect().Size;
        float w = Mathf.Min(1560 * k, vs.X - 40 * k);
        var panel = new VPanel
        {
            Title = "GUN SKINS", Caption = "COLLECTION · ORIGINAL FINISHES MADE FOR VALTRAINER · COSMETIC ONLY", K = k,
            Fill = new Color(0.07f, 0.11f, 0.15f, 0.985f), CustomMinimumSize = new Vector2(w, 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(12 * k));
        panel.AddChild(col);

        // body height: what the window leaves under the title and above the footer (design units)
        float bodyH = Mathf.Clamp(vs.Y / k - 250f, 360f, 660f);
        var body = new HBoxContainer { CustomMinimumSize = new Vector2(0, bodyH * k) };
        body.AddThemeConstantOverride("separation", (int)(16 * k));
        col.AddChild(body);

        BuildGuns(body, w, bodyH);
        BuildCentre(body);
        BuildLines(body, w, bodyH);
        BuildFooter(col);

        focus = ChosenIndex;
        (level, variant) = Selected is { } sl ? DefaultFor(sl) : (1, null);
        ShowItem();
        DevSetup();
        SetView(view);
        RefreshAll();
        Callable.From(ScrollToGun).CallDeferred();
        Callable.From(ScrollToTile).CallDeferred();
    }

    void BuildGuns(HBoxContainer body, float w, float bodyH)
    {
        float gunW = Mathf.Clamp(w / k * 0.18f, 220f, 280f);
        gunScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(gunW * k, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddChild(gunScroll);
        var gl = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gl.AddThemeConstantOverride("separation", (int)(2 * k));
        gunScroll.AddChild(gl);
        float rowH = Mathf.Clamp((bodyH - 6 * 24f) / 20f - 2f, 26f, 34f);
        foreach (var (cls, name) in Groups)
        {
            gl.AddChild(SectionHead(name, null));
            foreach (var g in Weapons.Buyable.Where(x => x.Class == cls))
            {
                var kind = g.Kind;
                var r = new SkinGunRow(g) { K = k, CustomMinimumSize = new Vector2(0, rowH * k), Selected = kind == gun };
                r.Pressed += () => SelectGun(kind);
                rows.Add(r);
                gl.AddChild(r);
            }
        }
    }

    /// <summary>A small section heading (buy-menu group, tier) with an optional tier's pips and rule.</summary>
    DrawBox SectionHead(string name, Edition? ed) => new()
    {
        CustomMinimumSize = new Vector2(0, 24 * k),
        OnDraw = d =>
        {
            int fs = UiTheme.Fs(12, k), rs = UiTheme.Fs(10.5f, k);
            float cy = d.Size.Y / 2 + 1 * k, x = 0;
            if (ed is { } e)
            {
                x = SkinIcons.Pips(d, e, new Vector2(1 * k, cy), 9 * k) + 7 * k;
                string ename = L.Upper(L.T(name));
                Gfx.TextRaw(d, UiTheme.HudWide, ename, x, Gfx.Mid(cy, fs), fs, SkinIcons.EditionColor(e));
                x += Gfx.RawW(UiTheme.HudWide, ename, fs) + 8 * k;
                Gfx.TextFit(d, UiTheme.HudWide, SkinCatalog.EditionRule(e), x, Gfx.Mid(cy, rs), rs, UiTheme.Faint, d.Size.X - x);
                return;
            }
            d.DrawRect(new Rect2(0, cy - 2 * k, 5 * k, 5 * k), UiTheme.Accent);
            Gfx.Text(d, UiTheme.HudWide, name, 12 * k, Gfx.Mid(cy + 2 * k, fs), fs, UiTheme.Dim);
        },
    };

    void BuildCentre(HBoxContainer body)
    {
        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", (int)(8 * k));
        body.AddChild(mid);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", (int)(6 * k));
        mid.AddChild(tabs);
        turnBtn = new VButton { Label = "TURNTABLE", Kind = VButton.Look.Select, K = k, FontPx = 14, Selected = true, CustomMinimumSize = new Vector2(124 * k, 34 * k) };
        handBtn = new VButton { Label = "IN HAND", Kind = VButton.Look.Select, K = k, FontPx = 14, CustomMinimumSize = new Vector2(112 * k, 34 * k) };
        showBtn = new VButton { Label = "SHOWCASE", Kind = VButton.Look.Select, K = k, FontPx = 14, CustomMinimumSize = new Vector2(118 * k, 34 * k) };
        turnBtn.TooltipText = L.T("The gun alone, turning (drag to rotate) · Tab");
        handBtn.TooltipText = L.T("The gun in your hands, as a drill shows it · Tab");
        showBtn.TooltipText = L.T("A dummy is shot and killed, so you see and hear the level's sounds, bullet and kill effects and finisher · Tab");
        turnBtn.Pressed += () => SetView(SkinPreview.View.Turntable);
        handBtn.Pressed += () => SetView(SkinPreview.View.InHand);
        showBtn.Pressed += () => SetView(SkinPreview.View.Showcase);
        tabs.AddChild(turnBtn);
        tabs.AddChild(handBtn);
        tabs.AddChild(showBtn);
        tabs.AddChild(viewHint = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = UiTheme.Label(UiTheme.Body, 13, k, UiTheme.Faint), MouseFilter = MouseFilterEnum.Ignore,
        });
        // actions on the in-hand gun (from the turntable they switch to IN HAND first)
        inspectBtn = new VButton { Label = "INSPECT", Kind = VButton.Look.Secondary, K = k, FontPx = 14, CustomMinimumSize = new Vector2(104 * k, 34 * k) };
        reloadBtn = new VButton { Label = "RELOAD", Kind = VButton.Look.Secondary, K = k, FontPx = 14, CustomMinimumSize = new Vector2(98 * k, 34 * k) };
        inspectBtn.TooltipText = L.T("Inspect the gun in your hands, with the skin's inspect flourish where it has one · Y");
        reloadBtn.TooltipText = L.T("Reload the gun in your hands, with the skin's special reload where it has one (same reload time) · R");
        inspectBtn.Pressed += DoInspect;
        reloadBtn.Pressed += DoReload;
        tabs.AddChild(inspectBtn);
        tabs.AddChild(reloadBtn);
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.07f, 0.1f, 0.13f), BorderColor = new Color(UiTheme.Text, 0.1f), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        mid.AddChild(frame);
        preview = new SkinPreview { K = k, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddChild(preview);
        info = new DrawBox { CustomMinimumSize = new Vector2(0, 56 * k), OnDraw = DrawInfo };
        mid.AddChild(info);
        track = new SkinLevelTrack { K = k, CustomMinimumSize = new Vector2(0, 122 * k), Picked = PickLevel };
        mid.AddChild(track);

        // variants + equip
        var vrow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 54 * k) };
        vrow.AddThemeConstantOverride("separation", (int)(8 * k));
        mid.AddChild(vrow);
        variantLabel = new DrawBox { CustomMinimumSize = new Vector2(92 * k, 0), OnDraw = DrawVariantLabel };
        vrow.AddChild(variantLabel);
        variantBox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        variantBox.AddThemeConstantOverride("separation", (int)(6 * k));
        vrow.AddChild(variantBox);
        equipBtn = new VButton { Label = "EQUIP", Kind = VButton.Look.Primary, K = k, FontPx = 17, SubPx = 11.5f, CustomMinimumSize = new Vector2(220 * k, 52 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        equipBtn.Pressed += Equip;
        vrow.AddChild(equipBtn);
    }

    void BuildLines(HBoxContainer body, float w, float bodyH)
    {
        float rightW = Mathf.Clamp(w / k * 0.25f, 320f, 400f);
        lineScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(rightW * k, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddChild(lineScroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", (int)(4 * k));
        lineScroll.AddChild(list);
        var editions = Enum.GetValues<Edition>();
        int tileRows = editions.Sum(e => (lines.Count(l => (l?.Edition ?? Edition.Core) == e) + TileCols - 1) / TileCols);
        float gap = 6f;
        float th = Mathf.Clamp((bodyH - editions.Length * 28f - (tileRows - editions.Length) * gap - 8f) / Mathf.Max(1, tileRows), 46f, 74f);
        float tw = (rightW - 12f - (TileCols - 1) * gap) / TileCols;
        foreach (var e in editions)
        {
            list.AddChild(SectionHead(SkinCatalog.EditionName(e), e));
            var grid = new GridContainer { Columns = TileCols };
            grid.AddThemeConstantOverride("h_separation", (int)(gap * k));
            grid.AddThemeConstantOverride("v_separation", (int)(gap * k));
            list.AddChild(grid);
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if ((l?.Edition ?? Edition.Core) != e) continue;
                int idx = i;
                var t = new SkinSwatch(l?.Look)
                {
                    K = k, CustomMinimumSize = new Vector2(tw * k, th * k), Title = l?.Name ?? L.N("Factory"), Edition = l?.Edition,
                    Levels = l?.MaxLevel ?? 0,
                };
                t.TooltipText = l == null ? L.T("Factory: the model's own colours")
                    : L.T(l.Name) + " · " + L.T(SkinCatalog.EditionName(l.Edition)) + "\n" + L.T(l.Blurb)
                      + (l.MaxLevel == 1 && l.FeaturesAt(1) != SkinFeature.None ? "\n" + SkinIcons.List(l.FeaturesAt(1)) : "");
                t.Pressed += () => PickLine(idx);
                t.MouseEntered += () => Hover(idx);
                t.MouseExited += () => { if (hovered == idx) Hover(-1); };
                tiles.Add(t);
                grid.AddChild(t);
            }
            // tiles is in tile order = lines order only if the catalogue is grouped by tier: it is (SkinCatalog.Lines)
        }
    }

    void BuildFooter(VBoxContainer col)
    {
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(foot);
        resetGun = new VButton { Label = "RESET GUN", Kind = VButton.Look.Secondary, K = k, FontPx = 16, CustomMinimumSize = new Vector2(150 * k, 48 * k) };
        resetGun.TooltipText = L.T("This gun goes back to the skin of every gun (factory if there is none)");
        resetGun.Pressed += () => { GunSkins.Set(gun, null); Refocus(); };
        foot.AddChild(resetGun);
        resetAll = new VButton { Label = "RESET ALL", Kind = VButton.Look.Secondary, K = k, FontPx = 16, CustomMinimumSize = new Vector2(150 * k, 48 * k) };
        resetAll.TooltipText = L.T("Every gun back to factory");
        resetAll.Pressed += () => Confirmed("reset", () => { GunSkins.ResetAll(); Refocus(); });
        foot.AddChild(resetAll);
        applyAll = new VButton { Label = "APPLY TO ALL GUNS", Kind = VButton.Look.Secondary, K = k, FontPx = 16, CustomMinimumSize = new Vector2(250 * k, 48 * k) };
        applyAll.TooltipText = L.T("The shown skin, level and variant on every gun (guns with a skin of their own get it too)");
        applyAll.Pressed += ApplyAll;
        foot.AddChild(applyAll);
        foot.AddChild(new Label
        {
            Text = L.T("Click equips · ←/→ skins · 1–5 levels · V variants · ↑/↓ guns · Y inspect · R reload · Tab view · Esc closes"), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, HorizontalAlignment = HorizontalAlignment.Right, ClipText = true,
            LabelSettings = UiTheme.Label(UiTheme.Body, 14, k, UiTheme.Faint), MouseFilter = MouseFilterEnum.Ignore,
        });
        var close = new VButton { Label = "DONE", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(170 * k, 48 * k) };
        close.Pressed += Close;
        foot.AddChild(close);
    }

    /// <summary>The item to show first (<see cref="Open"/>'s itemId): its line, level and variant, previewed (not equipped).</summary>
    string? showItem;

    void ShowItem()
    {
        if (!ItemIds.TryParse(showItem, out var line, out int n, out var v) || SkinCatalog.Get(line) is not { } l) return;
        focus = IndexOf(l.Id);
        (level, variant) = DefaultFor(l);
        if (n > 1) level = Math.Clamp(n, 1, l.MaxLevel);
        if (v != null) variant = l.Variant(v);
    }

    /// <summary>Dev: --skins-gun, --skins-skin / --skins-line / --skins-level / --skins-variant, --skins-view, --skins-apply
    /// (screenshots and the persistence test).</summary>
    void DevSetup()
    {
        if (!CmdLine.Dev) return;
        if (Weapons.Parse(CmdLine.After("--skins-gun")) is { } g && GunSkins.Skinnable(g)) gun = g;
        focus = ChosenIndex;
        (level, variant) = Selected is { } sl ? DefaultFor(sl) : (1, null);
        string? want = CmdLine.After("--skins-skin") ?? CmdLine.After("--skins-line");
        if (want == GunSkins.FactoryId) { focus = 0; level = 1; variant = null; }
        else if (SkinChoice.Parse(want) is { } c && SkinCatalog.Get(c.Line) is { } l)
        {
            focus = IndexOf(l.Id);
            (level, variant) = DefaultFor(l);
            if (c.Level > 0) level = c.Level;
            if (c.Variant != null) variant = l.Variant(c.Variant);
        }
        if (Selected is { } s)
        {
            if (int.TryParse(CmdLine.After("--skins-level"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
                level = Math.Clamp(n, 1, s.MaxLevel);
            if (CmdLine.After("--skins-variant") is { } vid) variant = s.Variant(vid);
        }
        view = CmdLine.After("--skins-view") switch { "hand" => SkinPreview.View.InHand, "showcase" => SkinPreview.View.Showcase, "turntable" => SkinPreview.View.Turntable, _ => view };
        if (CmdLine.After("--skins-action") is { } act)
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (done || !IsInstanceValid(this)) return;
                if (act == "inspect") DoInspect(); else if (act == "reload") DoReload();
            };
        if (CmdLine.After("--skins-apply") is { } aid && (aid == GunSkins.FactoryId || GunSkins.Line(aid) != null))
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (done || !IsInstanceValid(this)) return;
                if (aid == GunSkins.FactoryId) GunSkins.Set(gun, GunSkins.FactoryId);
                else GunSkins.Set(gun, aid); // a locked choice is stored too (it applies once unlocked)
                Refocus();
                Log.Info($"[skins] --skins-apply {aid} on {gun}: settings now GunSkins={(Main.I.Settings.GunSkins == null ? "null" : string.Join(",", Main.I.Settings.GunSkins.Select(e => $"{e.Key}:{e.Value}")))} GunSkinAll={Main.I.Settings.GunSkinAll ?? "null"}");
            };
    }

    void ScrollToGun()
    {
        if (gunScroll == null || !IsInstanceValid(gunScroll)) return;
        var r = rows.FirstOrDefault(x => x.Def.Kind == gun);
        if (r != null) gunScroll.EnsureControlVisible(r);
    }

    void ScrollToTile()
    {
        if (lineScroll == null || !IsInstanceValid(lineScroll) || focus < 0 || focus >= tiles.Count) return;
        lineScroll.EnsureControlVisible(tiles[focus]);
    }

    // ---------------- drawing ----------------

    /// <summary>The shown line: name, edition, where it stands on this gun, and its blurb.</summary>
    void DrawInfo(DrawBox d)
    {
        var l = ShownLine;
        var s = ShownState;
        var r = new Rect2(Vector2.Zero, d.Size);
        d.DrawRect(r, new Color(1, 1, 1, 0.03f));
        d.DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y), l != null ? SkinIcons.EditionColor(l.Edition) : UiTheme.Accent);
        int ns = UiTheme.Fs(24, k), ls = UiTheme.Fs(12, k), bs = UiTheme.Fs(14.5f, k);
        float x = 16 * k, cy = r.Size.Y / 2;
        // skin names are our own (like drill names): translated where shown; gun names stay English
        string head = L.Upper(L.T(l?.Name ?? "Factory"));
        Gfx.TextRaw(d, UiTheme.Display, head, x, Gfx.Mid(cy - 10 * k, ns), ns, UiTheme.Text);
        float hx = x + Gfx.RawW(UiTheme.Display, head, ns) + 14 * k;
        if (l != null)
        {
            hx += SkinIcons.Pips(d, l.Edition, new Vector2(hx, cy - 10 * k), 10 * k) + 7 * k;
            string ed = L.Upper(L.T(SkinCatalog.EditionName(l.Edition)));
            Gfx.TextRaw(d, UiTheme.HudWide, ed, hx, Gfx.Mid(cy - 10 * k, ls), ls, SkinIcons.EditionColor(l.Edition));
            hx += Gfx.RawW(UiTheme.HudWide, ed, ls);
        }
        bool locked = ShownLock != null, worn = Worn(ShownState);
        string meta = (l != null ? " · " : "") + L.F($"ON THE {GunPicker.GunName(gun).ToUpperInvariant()}");
        if (s is { } st && st.Line.MaxLevel > 1) meta += " · " + L.F($"LEVEL {st.Level}");
        if (s?.Variant is { } v) meta += " · " + L.Upper(L.T(v.Name));
        meta += " · " + (locked ? L.T("LOCKED") : worn ? L.T("EQUIPPED") : L.T("PREVIEW"));
        Gfx.TextFit(d, UiTheme.HudWide, L.Raw(meta), hx, Gfx.Mid(cy - 10 * k, ls), ls, locked ? UiTheme.Warn : worn ? UiTheme.Good : UiTheme.Dim, r.Size.X - hx - 100 * k);
        if (s?.Look.Animated == true)
        {
            string tag = L.N("ANIMATED"); // measured and drawn translated (TextW / Text)
            float tw = Gfx.TextW(UiTheme.HudWide, tag, ls) + 12 * k;
            var tr = new Rect2(r.Size.X - tw - 8 * k, cy - 10 * k - 9 * k, tw, 18 * k);
            d.DrawRect(tr, new Color(UiTheme.Warn, 0.12f));
            d.DrawRect(tr, new Color(UiTheme.Warn, 0.7f), false, 1f);
            Gfx.Text(d, UiTheme.HudWide, tag, tr.Position.X + 6 * k, Gfx.Mid(cy - 10 * k, ls), ls, UiTheme.Warn);
        }
        string blurb = l == null ? L.T("The gun as it comes: the model's own colours") : L.T(l.Blurb);
        var bc = UiTheme.Dim;
        // the gun's own choice is this line but (partly) locked: it is kept and shows less until unlocked
        if (l != null && GunSkins.ChoiceFor(gun)?.Line == l.Id && GunSkins.ShowsLess(gun))
        {
            string gn = GunPicker.GunName(gun);
            blurb = GunSkins.StateFor(gun) == null ? L.F($"Your choice for the {gn}: it shows Factory until it is unlocked")
                : L.F($"Your choice for the {gn}: it shows the highest unlocked level and palette until the rest is unlocked");
            bc = UiTheme.Warn;
        }
        if (GunSkins.DevSkin != null) blurb += $" · dev --skin {GunSkins.DevSkin} overrides every gun";
        Gfx.TextFit(d, UiTheme.Body, L.Raw(blurb), x, Gfx.Mid(cy + 14 * k, bs), bs, bc, r.Size.X - 32 * k);
    }

    void DrawVariantLabel(DrawBox d)
    {
        int fs = UiTheme.Fs(12, k), ss = UiTheme.Fs(11, k);
        float cy = d.Size.Y / 2;
        var l = Selected;
        bool any = l is { Variants.Count: > 0 };
        Gfx.Text(d, UiTheme.HudWide, "VARIANTS", 0, Gfx.Mid(cy - (any ? 8 : 0) * k, fs), fs, any ? UiTheme.Dim : UiTheme.Faint);
        if (l is { Variants.Count: > 0, Edition: Edition.Apex })
            Gfx.TextFit(d, UiTheme.Body, L.F($"after level {l.MaxLevel}"), 0, Gfx.Mid(cy + 9 * k, ss), ss, UiTheme.Faint, d.Size.X - 4 * k);
        else if (any)
            Gfx.TextFit(d, UiTheme.Body, "with the skin", 0, Gfx.Mid(cy + 9 * k, ss), ss, UiTheme.Faint, d.Size.X - 4 * k);
    }

    /// <summary>The variant strip of the selected line: the base palette, then each variant (rebuilt when the line changes).</summary>
    void BuildVariants()
    {
        var l = Selected;
        if (variantsFor == l?.Id) return;
        variantsFor = l?.Id;
        foreach (var c in variantBox.GetChildren()) c.QueueFree();
        variantTiles.Clear();
        variantOf.Clear();
        if (l == null || l.Variants.Count == 0)
        {
            variantBox.AddChild(new Label
            {
                Text = l == null ? "" : L.T("This skin has no variants"), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                LabelSettings = UiTheme.Label(UiTheme.Body, 13, k, UiTheme.Faint), MouseFilter = MouseFilterEnum.Ignore,
            });
            return;
        }
        float h = 52f, w = Mathf.Round(h * 1.75f);
        foreach (var v in l.Variants.Prepend(null))
        {
            var vv = v;
            var t = new SkinSwatch(l.LookAt(1, v)) { K = k, CustomMinimumSize = new Vector2(w * k, h * k), Title = v?.Name ?? (l.BaseName.Length > 0 ? l.BaseName : l.Name), Small = true };
            t.Pressed += () => PickVariant(vv);
            variantTiles.Add(t);
            variantOf.Add(v);
            variantBox.AddChild(t);
        }
    }

    // ---------------- actions ----------------

    void SelectGun(WeaponKind g)
    {
        if (!GunSkins.Skinnable(g)) return;
        gun = g;
        hovered = -1;
        Refocus();
        ScrollToGun();
    }

    void Hover(int i)
    {
        if (hovered == i) return;
        hovered = i;
        RefreshPreview();
    }

    /// <summary>A line tile: select it (its default level and variant) and, when unlocked, equip it on the selected gun unless
    /// the gun already wears it (then its level and variant stay). Factory equips factory (an explicit entry when every gun
    /// has a skin).</summary>
    void PickLine(int i)
    {
        if (done || i < 0 || i >= lines.Count) return;
        Select(i);
        var l = lines[i];
        if (l == null) GunSkins.Set(gun, GunSkins.FactoryId);
        else if (GunSkins.StateFor(gun)?.Line.Id != l.Id && SelLock == null) GunSkins.SetChoice(gun, GunSkins.ChoiceOf(l, level, variant));
        RefreshAll();
    }

    /// <summary>Selects tile <paramref name="i"/> without equipping (keyboard): its default level and variant.</summary>
    void Select(int i)
    {
        focus = Math.Clamp(i, 0, lines.Count - 1);
        hovered = -1;
        variantClicked = false;
        (level, variant) = Selected is { } l ? DefaultFor(l) : (1, null);
        if (Selected is { } sl) MarkSeen(sl);
    }

    /// <summary>A level node: preview it; equip it when it (and the shown variant) is unlocked.</summary>
    void PickLevel(int n)
    {
        if (Selected is not { } l) return;
        hovered = -1;
        level = Math.Clamp(n, 1, l.MaxLevel);
        variantClicked = false;
        if (SelLock == null) GunSkins.SetChoice(gun, GunSkins.ChoiceOf(l, level, variant));
        RefreshAll();
    }

    /// <summary>A variant swatch (null = the base palette): preview it; equip it when unlocked at the shown level.</summary>
    void PickVariant(SkinVariant? v)
    {
        if (Selected is not { } l) return;
        hovered = -1;
        variant = v;
        variantClicked = v != null;
        if (SelLock == null) GunSkins.SetChoice(gun, GunSkins.ChoiceOf(l, level, variant));
        RefreshAll();
    }

    /// <summary>EQUIP: wears what is shown on the selected gun; a locked item instead flashes how to unlock it.</summary>
    void Equip()
    {
        if (done) return;
        hovered = -1;
        if (Selected is not { } l) { GunSkins.Set(gun, GunSkins.FactoryId); RefreshAll(); return; }
        if (SelLock != null) { track.Flash = 0.8f; RefreshAll(); return; }
        GunSkins.SetChoice(gun, GunSkins.ChoiceOf(l, level, variant));
        RefreshAll();
    }

    void ApplyAll()
    {
        if (SelLock != null) { track.Flash = 0.8f; return; }
        string id = Selected is { } l ? GunSkins.ChoiceOf(l, level, variant).ToString() : GunSkins.FactoryId;
        int own = GunSkins.CustomCount();
        void Do() { GunSkins.SetAll(id); Refocus(); }
        if (own > 0) Confirmed("all", Do); else Do();
    }

    /// <summary>Runs <paramref name="act"/> on the second click within 4 s (the button asks first).</summary>
    void Confirmed(string what, Action act)
    {
        if (confirm == what) { confirm = null; act(); RefreshFooter(); return; }
        confirm = what;
        confirmT = 4f;
        RefreshFooter();
    }

    /// <summary>Back to what the selected gun wears (after a gun switch or a reset).</summary>
    void Refocus()
    {
        Select(ChosenIndex);
        RefreshAll();
        ScrollToTile();
    }

    void MarkSeen(SkinLine l)
    {
        foreach (var id in Progress.Unseen.ToList())
            if (ItemIds.TryParse(id, out var line, out _, out _) && line == l.Id) Progress.MarkSeen(id);
    }

    static bool HasUnseen(SkinLine? l) =>
        l != null && Progress.Unseen.Any(id => ItemIds.TryParse(id, out var line, out _, out _) && line == l.Id);

    /// <summary>INSPECT: the in-hand gun's inspect (the turntable switches to IN HAND first).</summary>
    void DoInspect()
    {
        if (done) return;
        if (view == SkinPreview.View.Turntable) SetView(SkinPreview.View.InHand);
        preview.Inspect();
    }

    /// <summary>RELOAD: one reload of the in-hand gun (the turntable switches to IN HAND first).</summary>
    void DoReload()
    {
        if (done) return;
        if (view == SkinPreview.View.Turntable) SetView(SkinPreview.View.InHand);
        preview.Reload();
    }

    void SetView(SkinPreview.View v)
    {
        view = v;
        turnBtn.SetSelected(v == SkinPreview.View.Turntable);
        handBtn.SetSelected(v == SkinPreview.View.InHand);
        showBtn.SetSelected(v == SkinPreview.View.Showcase);
        preview.SetView(v);
        viewHint.Text = v switch
        {
            SkinPreview.View.Turntable => L.T("Drag to rotate"),
            SkinPreview.View.InHand => L.T("As a drill shows it"),
            _ => L.T("A dummy at 5 m, shot every 1.2 s · with sound"),
        };
        RefreshPreview();
    }

    void RefreshAll()
    {
        foreach (var r in rows) { r.Selected = r.Def.Kind == gun; r.QueueRedraw(); }
        BuildVariants();
        RefreshTiles();
        RefreshPreview();
        RefreshFooter();
    }

    void RefreshTiles()
    {
        var worn = GunSkins.StateFor(gun);
        var chosen = GunSkins.ChoiceFor(gun);
        for (int i = 0; i < tiles.Count; i++)
        {
            var l = lines[i];
            var t = tiles[i];
            t.Focused = i == focus;
            t.Applied = l == null ? worn == null : worn?.Line.Id == l.Id;
            t.Chosen = l != null && chosen?.Line == l.Id && !t.Applied;
            t.Locked = l != null && GunSkins.TopLevel(l) == 0;
            t.Unseen = HasUnseen(l);
            // the tile wears the variant this gun has of it
            t.Skin = l == null ? null : worn is { } w && w.Line.Id == l.Id ? l.LookAt(1, w.Variant) : l.Look;
            t.QueueRedraw();
        }
        var sel = Selected;
        for (int i = 0; i < variantTiles.Count && sel != null; i++)
        {
            var v = variantOf[i];
            var t = variantTiles[i];
            string item = v == null ? ItemIds.Base(sel.Id) : ItemIds.Variant(sel.Id, v.Id);
            var st = Progress.Status(item);
            t.Focused = v?.Id == variant?.Id;
            t.Applied = worn is { } w && w.Line.Id == sel.Id && w.Variant?.Id == v?.Id;
            t.Locked = !st.Unlocked;
            t.TooltipText = L.T(v?.Name ?? (sel.BaseName.Length > 0 ? sel.BaseName : sel.Name)) + "\n" + (st.Unlocked ? L.T("Unlocked") : L.F($"Locked: {st.Summary}"));
            t.QueueRedraw();
        }
    }

    void RefreshPreview()
    {
        preview.Show(gun, ShownState);
        info.QueueRedraw();
        var sel = Selected;
        track.Line = sel;
        track.Shown = sel != null ? Math.Clamp(level, 1, sel.MaxLevel) : 1;
        var worn = GunSkins.StateFor(gun);
        track.Worn = sel != null && worn is { } w && w.Line.Id == sel.Id ? w.Level : 0;
        // the status line: the first locked item of what is shown, else the clicked variant, else the shown level
        string? lockItem = sel != null ? GunSkins.LockedItem(sel, track.Shown, variant) : null;
        string? focusItem = lockItem ?? (variantClicked && sel != null && variant != null ? ItemIds.Variant(sel.Id, variant.Id) : null);
        track.Focus = focusItem;
        track.FocusName = sel != null && focusItem != null ? ItemName(sel, focusItem) : null;
        track.QueueRedraw();
        RefreshEquip();
    }

    void RefreshEquip()
    {
        bool locked = SelLock != null;
        bool worn = Worn(SelState);
        equipBtn.Label = locked ? L.T("LOCKED") : worn ? L.T("EQUIPPED") : L.F($"EQUIP ON {GunPicker.GunName(gun).ToUpperInvariant()}");
        equipBtn.Sub = locked ? L.T("click: how to unlock") : null;
        equipBtn.Kind = locked || worn ? VButton.Look.Secondary : VButton.Look.Primary;
        equipBtn.Disabled = worn;
        equipBtn.QueueRedraw();
    }

    void RefreshFooter()
    {
        resetGun.Disabled = GunSkins.OwnId(gun) == null;
        bool any = Main.I?.Settings is { } s && (s.GunSkins != null || s.GunSkinAll != null);
        resetAll.Disabled = !any;
        applyAll.Disabled = SelLock != null;
        int own = GunSkins.CustomCount();
        resetAll.Label = confirm == "reset" ? "SURE? CLICK AGAIN" : "RESET ALL";
        applyAll.Label = confirm == "all" ? L.P(own, $"REPLACE {own} GUN'S SKIN?", $"REPLACE {own} PER-GUN SKINS?") : L.T("APPLY TO ALL GUNS");
        applyAll.Kind = confirm == "all" ? VButton.Look.Primary : VButton.Look.Secondary;
        resetAll.Kind = confirm == "reset" ? VButton.Look.Primary : VButton.Look.Secondary;
        foreach (var b in new[] { resetGun, resetAll, applyAll }) b.QueueRedraw();
    }

    void Close()
    {
        if (done) return;
        done = true;
        QueueFree();
        Closed?.Invoke();
    }

    /// <summary>Closes without a sound (the screen underneath is going away).</summary>
    public void Dismiss() => Close();

    public override void _EnterTree() => GunSkins.Changed += OnChanged;
    public override void _ExitTree() => GunSkins.Changed -= OnChanged;
    void OnChanged()
    {
        if (done || !IsInstanceValid(this) || info == null) return;
        foreach (var r in rows) r.QueueRedraw();
        RefreshTiles();
        RefreshPreview();
        RefreshFooter();
    }

    public override void _Input(InputEvent e)
    {
        if (done || e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Escape or Key.B or Key.K:
                if (!key.Echo) { UiTheme.ClickSound(); Close(); }
                break;
            case Key.Left or Key.A: MoveFocus(-1); break;
            case Key.Right or Key.D: MoveFocus(1); break;
            case Key.Up or Key.W: StepGun(-1); break;
            case Key.Down or Key.S: StepGun(1); break;
            case Key.Pageup: MoveFocus(-TileCols); break;
            case Key.Pagedown: MoveFocus(TileCols); break;
            case Key.Tab:
                if (!key.Echo) SetView(view switch { SkinPreview.View.Turntable => SkinPreview.View.InHand, SkinPreview.View.InHand => SkinPreview.View.Showcase, _ => SkinPreview.View.Turntable });
                break;
            case Key.Y:
                if (!key.Echo) { UiTheme.ClickSound(); DoInspect(); }
                break;
            case Key.R:
                if (!key.Echo) { UiTheme.ClickSound(); DoReload(); }
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                if (!key.Echo) { UiTheme.ClickSound(); Equip(); }
                break;
            case >= Key.Key1 and <= Key.Key5:
                if (!key.Echo && Selected is { } l && (int)(key.Keycode - Key.Key1) < l.MaxLevel)
                {
                    UiTheme.HoverSound();
                    hovered = -1;
                    level = (int)(key.Keycode - Key.Key1) + 1;
                    variantClicked = false;
                    RefreshAll(); // keys preview only; Enter equips
                }
                break;
            case Key.V:
                if (!key.Echo && Selected is { Variants.Count: > 0 } vl)
                {
                    UiTheme.HoverSound();
                    hovered = -1;
                    int i = variant == null ? 0 : vl.Variants.ToList().FindIndex(x => x.Id == variant.Id) + 1;
                    int n = (i + 1) % (vl.Variants.Count + 1);
                    variant = n == 0 ? null : vl.Variants[n - 1];
                    variantClicked = variant != null;
                    RefreshAll();
                }
                break;
            default: return; // other keys (F11 …) pass through
        }
        GetViewport().SetInputAsHandled(); // the screen underneath never sees the picker's keys
    }

    void MoveFocus(int d)
    {
        int n = Math.Clamp(focus + d, 0, lines.Count - 1);
        if (n == focus) return;
        UiTheme.HoverSound();
        Select(n);
        RefreshAll();
        ScrollToTile();
    }

    void StepGun(int d)
    {
        int i = rows.FindIndex(r => r.Def.Kind == gun);
        int n = Math.Clamp(i + d, 0, rows.Count - 1);
        if (n != i) { UiTheme.HoverSound(); SelectGun(rows[n].Def.Kind); }
    }

    public override void _Process(double delta)
    {
        if (confirm != null && (confirmT -= (float)delta) <= 0f) { confirm = null; RefreshFooter(); }
        if (SkinSwatches.Revision != swatchRev)
        {
            swatchRev = SkinSwatches.Revision;
            foreach (var s in tiles) s.QueueRedraw();
            foreach (var s in variantTiles) s.QueueRedraw();
        }
        if (GunIcons.Revision != iconRev) { iconRev = GunIcons.Revision; foreach (var r in rows) r.QueueRedraw(); }
    }
    int swatchRev = -1, iconRev = -1;
}

/// <summary>One gun in the skin picker's list: silhouette, name and the skin it wears (teal when it has its own; amber with a
/// padlock when its choice is locked and it shows less until unlocked).</summary>
public partial class SkinGunRow : BaseButton
{
    public readonly WeaponDef Def;
    public float K = 1f;
    public bool Selected;
    float hover;

    public SkinGunRow(WeaponDef def)
    {
        Def = def;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        TooltipText = L.F($"Skins for the {def.Name}");
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, pad = 10 * k;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Selected ? new Color(UiTheme.Text, 0.12f) : new Color(1, 1, 1, 0.025f + 0.05f * hover));
        DrawRect(new Rect2(0, 0, 3 * k, Size.Y), Selected ? UiTheme.Accent : new Color(UiTheme.Accent, 0.6f * hover));
        float iw = Mathf.Min(58 * k, Size.X * 0.24f);
        GunIcons.Draw(this, new Rect2(pad, cy - Size.Y * 0.32f, iw, Size.Y * 0.64f), Def.Kind, new Color(UiTheme.Text, Selected ? 0.95f : 0.7f));
        int ns = UiTheme.Fs(16, k), ss = UiTheme.Fs(12, k);
        float nx = pad + iw + 10 * k;
        Gfx.TextRaw(this, UiTheme.Display, Def.Name.ToUpperInvariant(), nx, Gfx.Mid(cy, ns), ns, Selected ? UiTheme.Text : UiTheme.Dim); // gun names stay English
        float nw = Gfx.RawW(UiTheme.Display, Def.Name.ToUpperInvariant(), ns);
        float room = Size.X - pad - nx - nw - 8 * k;
        if (GunSkins.ShowsLess(Def.Kind) && GunSkins.ChoiceFor(Def.Kind) is { } c)
        {
            // the choice is kept while locked: name it, with a padlock (it shows factory / less until unlocked)
            SkinIcons.Lock(this, new Vector2(Size.X - pad - 5 * k, cy), 11 * k, UiTheme.Warn);
            Gfx.TextFit(this, UiTheme.Body, GunSkins.NameOf(c.Line), nx + nw + 8 * k, Gfx.Mid(cy, ss), ss, UiTheme.Warn, room - 16 * k, HorizontalAlignment.Right);
            return;
        }
        var st = GunSkins.StateFor(Def.Kind);
        bool own = GunSkins.OwnId(Def.Kind) != null;
        string skin = st?.Line.Name ?? L.N("Factory"); // English: TextFit translates it
        if (st is { } s && s.Line.MaxLevel > 1) skin = L.T(skin) + " " + L.F($"L{s.Level}");
        Gfx.TextFit(this, UiTheme.Body, skin, nx + nw + 8 * k, Gfx.Mid(cy, ss), ss, own ? UiTheme.Teal : st == null ? UiTheme.Faint : UiTheme.Dim,
            room, HorizontalAlignment.Right);
    }
}

/// <summary>One swatch in the collection: a line tile (its rendered finish from <see cref="SkinSwatches"/>, name, tier colour,
/// padlock when locked, a tick when the selected gun wears it, a dot for new unlocks) or, <see cref="Small"/>, a variant
/// swatch.</summary>
public partial class SkinSwatch : BaseButton
{
    /// <summary>The look drawn (null = factory).</summary>
    public GunSkin? Skin;
    /// <summary>English name drawn in the strip (translated here).</summary>
    public string Title = "";
    public Edition? Edition;
    /// <summary>Levels of the line (2+ draws a small "L1–4" hint).</summary>
    public int Levels;
    public float K = 1f;
    /// <summary>Focused = selected / shown; Applied = worn by the selected gun; Chosen = the gun's (locked) choice;
    /// Unseen = a new unlock not looked at yet; Small = variant strip.</summary>
    public bool Focused, Applied, Chosen, Locked, Unseen, Small;
    float hover;

    public SkinSwatch(GunSkin? skin)
    {
        Skin = skin;
        Title = skin?.Name ?? L.N("Factory");
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Ready()
    {
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
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
        float nameH = Mathf.Max(18 * k, Size.Y * (Small ? 0.34f : 0.27f));
        var img = new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y - nameH));
        string id = Skin?.Id ?? GunSkins.FactoryId;
        if (SkinSwatches.Get(id) is { } tex)
        {
            // crop the render to the box's aspect (cover)
            var ts = tex.GetSize();
            float s = Mathf.Max(img.Size.X / ts.X, img.Size.Y / ts.Y);
            var src = new Rect2((ts - img.Size / s) / 2, img.Size / s);
            DrawTextureRectRegion(tex, img, src);
        }
        else SkinSwatches.DrawFlat(this, img, Skin);
        DrawRect(new Rect2(0, r.Size.Y - nameH, r.Size.X, nameH), new Color(0.05f, 0.08f, 0.11f, 0.95f).Lerp(new Color(0.2f, 0.24f, 0.28f), hover * 0.6f));
        if (Edition is { } ed) DrawRect(new Rect2(0, r.Size.Y - nameH, r.Size.X, Mathf.Max(1, 2 * k)), new Color(SkinIcons.EditionColor(ed), 0.75f));
        int fs = UiTheme.Fs(Small ? 12f : 12.5f, k);
        Gfx.TextFit(this, UiTheme.Hud, L.Upper(L.T(Title)), 6 * k, Gfx.Mid(r.Size.Y - nameH / 2, fs), fs,
            Applied || Focused ? UiTheme.Text : UiTheme.Dim, r.Size.X - (Applied || Chosen ? 24 : 10) * k);
        if (Locked)
        {
            // dimmed, with a padlock in the corner
            DrawRect(img, new Color(0.02f, 0.03f, 0.05f, 0.55f));
            SkinIcons.Lock(this, new Vector2(r.Size.X - 11 * k, 11 * k), 13 * k, UiTheme.Text);
        }
        if (Levels > 1)
        {
            int ls = UiTheme.Fs(9.5f, k);
            string lv = L.F($"{Levels} LV");
            float lw = Gfx.RawW(UiTheme.HudWide, lv, ls) + 8 * k;
            var lr = new Rect2(4 * k, 4 * k, lw, 14 * k);
            DrawRect(lr, new Color(0.03f, 0.05f, 0.07f, 0.75f));
            Gfx.TextRaw(this, UiTheme.HudWide, lv, lr.Position.X + 4 * k, Gfx.Mid(lr.Position.Y + 7 * k, ls), ls, Edition is { } e2 ? SkinIcons.EditionColor(e2) : UiTheme.Dim);
        }
        if (Unseen && !Locked) DrawCircle(new Vector2(r.Size.X - 9 * k, 9 * k), 4 * k, UiTheme.Accent);
        var edge = Focused ? UiTheme.Text : new Color(UiTheme.Text, 0.14f + 0.4f * hover);
        DrawRect(r, edge, false, Focused ? Mathf.Max(1, 2 * k) : 1f);
        if (Focused) Gfx.Brackets(this, r.Grow(-3 * k), 7 * k, UiTheme.Accent, Mathf.Max(1, 2 * k));
        var tc = new Vector2(r.Size.X - 12 * k, r.Size.Y - nameH / 2);
        if (Applied) SkinIcons.Tick(this, tc, 11 * k, UiTheme.Good);
        else if (Chosen) SkinIcons.Lock(this, tc, 10 * k, UiTheme.Warn);
    }
}
