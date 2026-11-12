using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// The gun picker (research: weapons-design-1.18.md §3.3): a buy-menu-style overlay with VALORANT's six columns (sidearms,
/// SMGs, shotguns, rifles, snipers, machine guns). Each tile shows the gun's silhouette (<see cref="GunIcons"/>, drawn by
/// the game itself, no Riot art), its name, price and one stat line. Guns the drill doesn't offer are greyed with a short
/// reason. Click / Enter picks (and closes); arrows move the focus; B / Esc close without a change. The footer resets to
/// the drill's default and (drill previews) can make the pick the preferred gun of every drill.
/// <para>Opened by the drill preview's WEAPON row (B), Settings → "Preferred gun", the Agents page's GUN chip and, in a
/// running drill, VALORANT's buy-menu bind (GameSession, BuySession.cs). Dev:
/// <c>--preview-answer gun:Phantom</c> opens it from the preview and picks that gun (screenshots, flow tests).</para>
/// </summary>
public partial class GunPicker : Control
{
    public enum Purpose
    {
        /// <summary>One drill: the footer resets to its default; "Use for every drill" sets the preferred gun too.</summary>
        Drill,
        /// <summary>Settings: the preferred gun of every drill (the footer = each drill's own default).</summary>
        Preferred,
        /// <summary>Several drills at once (an agent's page).</summary>
        Group,
    }

    static readonly (WeaponClass Class, string Name)[] Columns =
    {
        (WeaponClass.Sidearm, L.N("SIDEARMS")), (WeaponClass.SMG, L.N("SMGS")), (WeaponClass.Shotgun, L.N("SHOTGUNS")),
        (WeaponClass.Rifle, L.N("RIFLES")), (WeaponClass.Sniper, L.N("SNIPERS")), (WeaponClass.Heavy, L.N("MACHINE GUNS")),
    };

    readonly float k;
    readonly string caption;
    readonly Purpose purpose;
    readonly WeaponKind defaultGun;
    readonly WeaponKind? current;
    readonly IReadOnlyCollection<WeaponKind>? allowed;
    readonly Func<WeaponKind, string?> reason;
    readonly Func<WeaponKind, string?>? note;
    readonly Action<WeaponKind?, bool> onPick;
    readonly bool everyDrillStart;
    /// <summary>Called once when the overlay goes away (picked or closed).</summary>
    public Action? Closed;
    /// <summary>Another input that closes the picker like B / Esc (the in-drill buy menu: the player's buy-menu bind, which
    /// may be a mouse button). Set before the picker enters the tree.</summary>
    public Func<InputEvent, bool>? CloseOn;
    /// <summary>The footer's key help instead of the default (set before the picker enters the tree).</summary>
    public string? KeysHint;

    readonly List<List<GunTile>> grid = new();
    int fc, fr; // focused column / row
    DrawBox? detail;
    VButton? everyBtn;
    bool everyDrill, done;

    /// <param name="defaultGun">The drill's default (Purpose.Drill) or None (each drill's own default).</param>
    /// <param name="current">The gun shown as equipped; null = the default.</param>
    /// <param name="allowed">The guns that can be picked; null = every buyable gun.</param>
    /// <param name="reason">Why a gun that isn't allowed is greyed out.</param>
    /// <param name="note">A line about an allowed gun (e.g. "unrated here").</param>
    /// <param name="onPick">The picked gun (null = the default) and whether "Use for every drill" is ticked.</param>
    /// <param name="everyDrill">Purpose.Drill: the starting state of "Use for every drill".</param>
    public GunPicker(float k, string caption, Purpose purpose, WeaponKind defaultGun, WeaponKind? current, IReadOnlyCollection<WeaponKind>? allowed,
        Func<WeaponKind, string?> reason, Action<WeaponKind?, bool> onPick, Func<WeaponKind, string?>? note = null, bool everyDrill = false)
    {
        this.k = k;
        this.caption = caption;
        this.purpose = purpose;
        this.defaultGun = defaultGun;
        this.current = current;
        this.allowed = allowed;
        this.reason = reason;
        this.note = note;
        this.onPick = onPick;
        everyDrillStart = this.everyDrill = everyDrill;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 10;
    }

    bool Allowed(WeaponKind g) => allowed == null || allowed.Contains(g);
    WeaponKind Shown => current ?? defaultGun;

    // ---------------- opening helpers ----------------

    /// <summary>Why <paramref name="g"/> isn't offered by drill <paramref name="m"/> (shown on its greyed tile). English
    /// (L.N): translated where it is drawn.</summary>
    public static string ReasonFor(TrainingMode m, WeaponKind g)
    {
        var choice = m.WeaponChoice;
        var def = Weapons.Get(g);
        if (choice == null) return L.F($"This drill is about the {GunRating.GunName(m.Weapon)}");
        if (choice.All(x => Weapons.Get(x)?.Class == WeaponClass.Sniper)) return L.N("This drill is for snipers");
        if (def is { Pellets: > 1 } && choice.All(x => (Weapons.Get(x)?.Pellets ?? 1) <= 1)) return L.N("Pellets don't fit target drills");
        if (def is { Auto: false } && choice.All(x => Weapons.Get(x)?.Auto == true)) return L.N("Full-auto guns only: this drill is about spraying");
        return L.N("Not offered in this drill");
    }

    /// <summary>The picker for one drill: remembers the pick for it (<see cref="WeaponPicks.Remember"/>), and with "Use for
    /// every drill" ticked makes it the preferred gun. <paramref name="applied"/> gets the gun to play now.
    /// <paramref name="configure"/> runs before the picker enters the tree (<see cref="CloseOn"/>, <see cref="KeysHint"/>).</summary>
    public static GunPicker? OpenForDrill(Node parent, float k, TrainingMode m, WeaponKind shown, Action<WeaponKind> applied, string? title = null,
        Action<GunPicker>? configure = null)
    {
        if (m.WeaponChoice is not { Count: > 0 } choice || HasOpen(parent)) return null;
        var pref = WeaponPicks.Preferred();
        var p = new GunPicker(k, L.Upper(title != null ? L.T(title) : m.DisplayName), Purpose.Drill, m.Weapon, shown, choice, g => ReasonFor(m, g),
            (g, every) =>
            {
                var gun = g ?? m.Weapon;
                if (every)
                {
                    // the preferred gun for every drill; this drill then simply follows it
                    WeaponPicks.SetPreferred(gun);
                    WeaponPicks.Remember(m.Key, null);
                }
                else
                {
                    if (WeaponPicks.Preferred() is { } pf && pf == gun) WeaponPicks.Remember(m.Key, null); // already what this drill resolves to
                    else if (gun == m.Weapon)
                        // back to the default: an explicit pick only when a preferred gun would otherwise win here
                        WeaponPicks.Remember(m.Key, WeaponPicks.Preferred() is { } pr && choice.Contains(pr) ? gun : null);
                    else WeaponPicks.Remember(m.Key, gun);
                }
                applied(gun);
            },
            g => GunRating.IsRated(m, g) ? null : L.F($"Unrated here: a score and a PB with it, badges only with {GunRating.DisplayRatedText(m)}"),
            everyDrill: pref != null && pref == shown);
        configure?.Invoke(p);
        parent.AddChild(p);
        return p;
    }

    /// <summary>Settings: the preferred gun of every drill (null = each drill's own default).</summary>
    public static GunPicker? OpenPreferred(Node parent, float k, Action changed)
    {
        if (HasOpen(parent)) return null;
        var p = new GunPicker(k, L.N("PREFERRED GUN · EVERY DRILL THAT OFFERS A CHOICE"), Purpose.Preferred, WeaponKind.None, WeaponPicks.Preferred(), null,
            _ => null, (g, _) => { WeaponPicks.SetPreferred(g); changed(); },
            note: _ => L.N("Drills that don't offer this gun keep their own"));
        parent.AddChild(p);
        return p;
    }

    /// <summary>Several drills at once (an agent's page): the pick is remembered for each drill that offers it.</summary>
    public static GunPicker? OpenForGroup(Node parent, float k, string title, IReadOnlyList<TrainingMode> drills, WeaponKind? shown, Action changed)
    {
        var withChoice = drills.Where(d => d.WeaponChoice is { Count: > 0 }).ToList();
        if (withChoice.Count == 0 || HasOpen(parent)) return null;
        var union = withChoice.SelectMany(d => d.WeaponChoice!).ToHashSet();
        var p = new GunPicker(k, L.Upper(L.T(title)), Purpose.Group, WeaponKind.None, shown, union, _ => L.N("None of these drills offers it"),
            (g, _) =>
            {
                foreach (var d in withChoice) WeaponPicks.Remember(d.Key, g is { } gg && d.WeaponChoice!.Contains(gg) ? gg : null);
                changed();
            },
            note: g => withChoice.All(d => d.WeaponChoice!.Contains(g)) ? null : L.N("Some of these drills don't offer it: they keep their own"));
        parent.AddChild(p);
        return p;
    }

    static bool HasOpen(Node parent) => parent.GetChildren().Any(c => c is GunPicker gp && !gp.done);

    /// <summary>"Spectre", "Tour De Force".</summary>
    public static string GunName(WeaponKind g) => GunRating.GunName(g);

    // ---------------- layout ----------------

    public override void _Ready()
    {
        GunIcons.Warm();
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.82f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var vs = GetViewportRect().Size;
        float w = Mathf.Min(1560 * k, vs.X - 40 * k);
        var panel = new VPanel
        {
            Title = purpose == Purpose.Preferred ? "PREFERRED GUN" : "CHOOSE YOUR GUN", Caption = caption, K = k,
            Fill = new Color(0.07f, 0.11f, 0.15f, 0.985f), CustomMinimumSize = new Vector2(w, 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(12 * k));
        panel.AddChild(col);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(cols);
        // tiles: ~100 design units tall; shorter if a very wide, flat window leaves less height
        float tileH = Mathf.Clamp((vs.Y / k - 420f) / 6f - 8f, 72f, 100f);
        foreach (var (cls, name) in Columns)
        {
            var guns = Weapons.Buyable.Where(g => g.Class == cls).ToList();
            var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            v.AddThemeConstantOverride("separation", (int)(8 * k));
            cols.AddChild(v);
            int n = guns.Count, on = guns.Count(g => Allowed(g.Kind));
            v.AddChild(new DrawBox
            {
                CustomMinimumSize = new Vector2(0, 30 * k),
                OnDraw = d =>
                {
                    int fs = UiTheme.Fs(14, k), ns = UiTheme.Fs(11, k);
                    d.DrawRect(new Rect2(0, d.Size.Y / 2 - 3 * k, 6 * k, 6 * k), on > 0 ? UiTheme.Accent : UiTheme.Faint);
                    Gfx.Text(d, UiTheme.HudWide, name, 13 * k, Gfx.Mid(d.Size.Y / 2, fs), fs, on > 0 ? UiTheme.Text : UiTheme.Faint);
                    if (allowed != null && on < n) Gfx.TextR(d, UiTheme.HudWide, on == 0 ? L.N("LOCKED") : $"{on}/{n}", d.Size.X, Gfx.Mid(d.Size.Y / 2, ns), ns, UiTheme.Faint);
                    d.DrawRect(new Rect2(0, d.Size.Y - 1, d.Size.X, 1), new Color(UiTheme.Text, 0.1f));
                },
            });
            var tiles = new List<GunTile>();
            foreach (var g in guns)
            {
                var kind = g.Kind;
                var t = new GunTile(g)
                {
                    K = k, Enabled = Allowed(kind), Equipped = kind == Shown, IsDefault = kind == defaultGun,
                    CustomMinimumSize = new Vector2(0, tileH * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    TooltipText = Allowed(kind) ? L.F($"{g.Name} · {L.T(ClassName(g.Class))} · {Price(g.Cost)} credits") : g.Name + ": " + L.T(reason(kind)),
                };
                int ci = grid.Count, ri = tiles.Count;
                t.Pressed += () => { Focus(ci, ri); Pick(kind); };
                t.MouseEntered += () => Focus(ci, ri);
                tiles.Add(t);
                v.AddChild(t);
            }
            grid.Add(tiles);
        }
        // start on the equipped gun
        for (int c = 0; c < grid.Count; c++)
            for (int r = 0; r < grid[c].Count; r++)
                if (grid[c][r].Def.Kind == Shown) { fc = c; fr = r; }
        grid[fc][fr].Focused = true;

        detail = new DrawBox { CustomMinimumSize = new Vector2(0, 58 * k), OnDraw = DrawDetail };
        col.AddChild(detail);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(foot);
        string defLabel = purpose == Purpose.Drill ? L.F($"DRILL DEFAULT ({GunName(defaultGun).ToUpperInvariant()})") : L.N("EACH DRILL'S DEFAULT");
        var def = new VButton
        {
            Label = defLabel, Kind = VButton.Look.Secondary, K = k, FontPx = 17, CustomMinimumSize = new Vector2(Mathf.Max(260, 40 + defLabel.Length * 9.5f) * k, 50 * k),
            TooltipText = L.T(purpose == Purpose.Drill ? "Play this drill with its own gun again" : "No preferred gun: every drill plays its own unless you pick one for it"),
        };
        def.Pressed += () => Pick(null);
        foot.AddChild(def);
        if (purpose == Purpose.Drill)
        {
            everyBtn = new VButton
            {
                Label = EveryLabel(), Kind = VButton.Look.Select, K = k, FontPx = 15, Selected = everyDrill, CustomMinimumSize = new Vector2(250 * k, 50 * k),
                TooltipText = L.T("Make this gun your preferred gun: every drill that offers it starts with it (a pick you make for one drill still wins there)"),
            };
            everyBtn.Pressed += () =>
            {
                everyDrill = !everyDrill;
                everyBtn.Selected = everyDrill;
                everyBtn.Label = EveryLabel();
                everyBtn.QueueRedraw();
            };
            foot.AddChild(everyBtn);
        }
        // gun skins for the focused gun (not in the in-drill buy menu, which runs its own state machine)
        if (CloseOn == null)
        {
            var skins = new VButton { Label = "SKINS", Kind = VButton.Look.Secondary, K = k, FontPx = 17, CustomMinimumSize = new Vector2(130 * k, 50 * k) };
            skins.TooltipText = L.T("Gun skins for the highlighted gun: original finishes, cosmetic only");
            skins.Pressed += () => { if (GetParent() is { } p && grid.Count > 0) SkinPicker.Open(p, k, grid[fc][fr].Def.Kind); };
            foot.AddChild(skins);
        }
        var hint = new Label
        {
            Text = KeysHint != null ? L.T(KeysHint) : L.T("Click or Enter picks · arrows move · B / Esc closes"), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = UiTheme.Label(UiTheme.Body, 14, k, UiTheme.Faint), MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
        };
        foot.AddChild(hint);
        var close = new VButton { Label = "DONE", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(180 * k, 50 * k) };
        close.Pressed += () => Close(true);
        foot.AddChild(close);
    }

    string EveryLabel() => (everyDrill ? "✓  " : "") + L.T("USE FOR EVERY DRILL");

    static string ClassName(WeaponClass c) => c switch
    {
        WeaponClass.Sidearm => L.N("Sidearm"), WeaponClass.SMG => L.N("SMG"), WeaponClass.Shotgun => L.N("Shotgun"),
        WeaponClass.Rifle => L.N("Rifle"), WeaponClass.Sniper => L.N("Sniper"), _ => L.N("Machine gun"),
    };

    internal static string Price(int cost) => cost.ToString("#,0", CultureInfo.InvariantCulture);

    void DrawDetail(DrawBox d)
    {
        if (grid.Count == 0) return;
        var g = grid[fc][fr].Def;
        bool on = Allowed(g.Kind);
        var r = new Rect2(Vector2.Zero, d.Size);
        d.DrawRect(r, new Color(1, 1, 1, 0.03f));
        d.DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y), on ? UiTheme.Accent : UiTheme.Faint);
        int ns = UiTheme.Fs(24, k), ls = UiTheme.Fs(12, k), bs = UiTheme.Fs(14.5f, k);
        float x = 16 * k, cy = r.Size.Y / 2;
        string head = g.Name.ToUpperInvariant(); // gun names stay English (as in VALORANT)
        Gfx.TextRaw(d, UiTheme.Display, head, x, Gfx.Mid(cy - 10 * k, ns), ns, on ? UiTheme.Text : UiTheme.Dim);
        float hx = x + Gfx.RawW(UiTheme.Display, head, ns) + 14 * k;
        string meta = L.Upper(L.T(ClassName(g.Class))) + " · " + L.F($"{Price(g.Cost)} CREDITS") + (g.Kind == defaultGun ? " · " + L.T("DRILL DEFAULT") : "");
        string? line = on ? note?.Invoke(g.Kind) : reason(g.Kind);
        string stats = StatLine(g);
        float lw = line != null ? Gfx.TextW(UiTheme.Body, line, bs) : 0;
        // translations run longer: the class / price line keeps clear of the note on the right (English draws as designed)
        float metaMax = L.IsEnglish ? float.MaxValue : r.Size.X - 16 * k - (line != null ? Mathf.Min(lw, r.Size.X * 0.45f) + 14 * k : 0) - hx;
        Gfx.TextFit(d, UiTheme.HudWide, meta, hx, Gfx.Mid(cy - 10 * k, ls), ls, UiTheme.Dim, Mathf.Max(20 * k, metaMax));
        if (line != null)
        {
            Gfx.TextFit(d, UiTheme.Body, line, r.Size.X - 16 * k - Mathf.Min(lw, r.Size.X * 0.45f), Gfx.Mid(cy - 10 * k, bs), bs, on ? UiTheme.Warn : UiTheme.Accent,
                r.Size.X * 0.45f);
        }
        Gfx.TextFit(d, UiTheme.Body, stats, x, Gfx.Mid(cy + 14 * k, bs), bs, on ? UiTheme.Dim : UiTheme.Faint, r.Size.X - 32 * k);
    }

    /// <summary>"11 rounds/s · 30-round magazine · 2.5 s reload · head 156 / body 39 / legs 33 up close · silenced".</summary>
    static string StatLine(WeaponDef g)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string>
        {
            g.Auto ? L.F($"{g.MaxRps.ToString("0.##", inv)} rounds/s") : L.F($"{g.MaxRps.ToString("0.##", inv)} shots/s (semi-auto)"),
            L.F($"{g.Mag}-round magazine"),
            L.F($"{g.Reload.ToString("0.##", inv)} s reload"),
            g.Pellets > 1
                ? L.F($"{g.Pellets} pellets of {g.DamageBody[0].ToString("0.#", inv)} body damage")
                : g.DamageRangeEnd.Length > 1
                    ? L.F($"head {g.DamageHead[0].ToString("0.#", inv)} / body {g.DamageBody[0].ToString("0.#", inv)} / legs {g.DamageLegs[0].ToString("0.#", inv)} up close")
                    : L.F($"head {g.DamageHead[0].ToString("0.#", inv)} / body {g.DamageBody[0].ToString("0.#", inv)} / legs {g.DamageLegs[0].ToString("0.#", inv)}"),
        };
        if (g.Scope) parts.Add(L.T("scope"));
        if (g.Silenced) parts.Add(L.T("silenced"));
        if (g.SpinUpTime > 0) parts.Add(L.T("spins up"));
        if (g.BurstCount > 1) parts.Add(L.F($"{g.BurstCount}-round bursts aimed"));
        return string.Join(" · ", parts);
    }

    // ---------------- input ----------------

    void Focus(int c, int r)
    {
        if (grid.Count == 0) return;
        c = Math.Clamp(c, 0, grid.Count - 1);
        r = Math.Clamp(r, 0, grid[c].Count - 1);
        if (c == fc && r == fr && grid[c][r].Focused) return;
        grid[fc][fr].Focused = false;
        grid[fc][fr].QueueRedraw();
        fc = c; fr = r;
        grid[fc][fr].Focused = true;
        grid[fc][fr].QueueRedraw();
        detail?.QueueRedraw();
    }

    /// <summary>Focuses a gun (dev answers, keyboard).</summary>
    public void FocusGun(WeaponKind g)
    {
        for (int c = 0; c < grid.Count; c++)
            for (int r = 0; r < grid[c].Count; r++)
                if (grid[c][r].Def.Kind == g) Focus(c, r);
    }

    /// <summary>Picks a gun (null = the default) and closes. A gun that isn't allowed does nothing.</summary>
    public void Pick(WeaponKind? g)
    {
        if (done) return;
        if (g is { } gg && !Allowed(gg)) { UiTheme.HoverSound(); return; }
        try { onPick(g, everyDrill); } catch (Exception e) { Log.Error($"[gunpicker] pick failed: {e.Message}"); }
        Close(false);
    }

    void Close(bool commitToggle)
    {
        if (done) return;
        // DONE / B / Esc without a new gun: a changed "Use for every drill" still applies to the equipped gun
        if (commitToggle && purpose == Purpose.Drill && everyDrill != everyDrillStart)
        {
            // ticked: the equipped gun becomes the preferred gun; unticked: no preferred gun, and this drill keeps its gun
            if (!everyDrill) WeaponPicks.SetPreferred(null);
            try { onPick(current, everyDrill); } catch (Exception e) { Log.Error($"[gunpicker] {e.Message}"); }
        }
        done = true;
        QueueFree();
        Closed?.Invoke();
    }

    /// <summary>Closes without a pick and without applying a changed "Use for every drill" (the screen underneath is going
    /// away or pausing).</summary>
    public void Dismiss() => Close(false);

    public override void _Input(InputEvent e)
    {
        if (done) return;
        if (CloseOn != null && CloseOn(e))
        {
            UiTheme.ClickSound();
            GetViewport().SetInputAsHandled(); // before Close: the drill underneath resumes and must not see this press
            Close(true);
            return;
        }
        if (e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Escape or Key.B:
                if (!key.Echo) { UiTheme.ClickSound(); Close(true); }
                break;
            case Key.Left or Key.A: Focus(fc - 1, fr); break;
            case Key.Right or Key.D: Focus(fc + 1, fr); break;
            case Key.Up or Key.W: Focus(fc, fr - 1); break;
            case Key.Down or Key.S: Focus(fc, fr + 1); break;
            case Key.Enter or Key.KpEnter or Key.Space:
                if (key.Echo) break;
                if (Allowed(grid[fc][fr].Def.Kind)) UiTheme.ClickSound();
                Pick(grid[fc][fr].Def.Kind);
                break;
            default: return; // other keys (F11 …) pass through
        }
        GetViewport().SetInputAsHandled(); // the screen underneath never sees the picker's keys
    }

    public override void _Process(double delta)
    {
        if (GunIcons.Revision == iconRev) return;
        iconRev = GunIcons.Revision;
        foreach (var c in grid) foreach (var t in c) t.QueueRedraw();
    }
    int iconRev = -1;
}

/// <summary>One gun in the picker: silhouette, name, price, a stat line; greyed when the drill doesn't offer it.</summary>
public partial class GunTile : BaseButton
{
    public readonly WeaponDef Def;
    public float K = 1f;
    public bool Enabled = true, Equipped, IsDefault, Focused;
    float hover;

    public GunTile(WeaponDef def)
    {
        Def = def;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Ready() => MouseEntered += () => { if (Enabled) UiTheme.HoverSound(); };

    public override void _Process(double delta)
    {
        float t = IsHovered() || Focused ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        float a = Enabled ? 1f : 0.4f;
        var fill = new Color(0.06f, 0.1f, 0.14f, 0.82f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.12f), hover * (Enabled ? 1f : 0.4f));
        Gfx.Plate(this, r, 10 * k, fill, new Color(Equipped ? UiTheme.Accent : UiTheme.Text, Equipped ? 0.95f : (0.12f + 0.3f * hover) * a), Equipped ? Mathf.Max(1, 2 * k) : 1f);
        if (Focused) Gfx.Brackets(this, r.Grow(-3 * k), 8 * k, new Color(UiTheme.Text, 0.9f), Mathf.Max(1, 2 * k));

        // silhouette (top), name + price, stat line
        float pad = 10 * k;
        var ir = new Rect2(pad, 7 * k, r.Size.X - pad * 2, r.Size.Y * 0.5f - 4 * k);
        var col = Enabled ? (Equipped ? UiTheme.Text : new Color(UiTheme.Text, 0.82f + 0.18f * hover)) : new Color(UiTheme.Faint, 0.7f);
        GunIcons.Draw(this, ir, Def.Kind, col);

        int ns = UiTheme.Fs(18, k), ps = UiTheme.Fs(13, k), ss = UiTheme.Fs(11, k);
        float ny = r.Size.Y * 0.5f + 14 * k;
        string price = GunPicker.Price(Def.Cost);
        float pw = Gfx.TextW(UiTheme.Hud, price, ps);
        Gfx.TextFit(this, UiTheme.Display, L.Raw(Def.Name.ToUpperInvariant()), pad, Gfx.Mid(ny, ns), ns, new Color(Enabled ? UiTheme.Text : UiTheme.Dim, a), r.Size.X - pad * 3 - pw - 14 * k);
        Gfx.TextR(this, UiTheme.Hud, price, r.Size.X - pad, Gfx.Mid(ny, ps), ps, new Color(UiTheme.Dim, a));
        CreditGlyph(this, new Vector2(r.Size.X - pad - pw - 9 * k, ny), 5 * k, new Color(UiTheme.Dim, a));

        string tag = Equipped ? L.N("EQUIPPED") : IsDefault ? L.N("DEFAULT") : "";
        float sy = ny + 17 * k;
        if (r.Size.Y > 80 * k)
        {
            string line = Line(Def);
            float tw = tag.Length > 0 ? Gfx.TextW(UiTheme.HudWide, tag, ss) + 10 * k : 0;
            Gfx.TextFit(this, UiTheme.Body, line, pad, Gfx.Mid(sy, ss), ss, new Color(UiTheme.Faint, Enabled ? 1f : 0.7f), r.Size.X - pad * 2 - tw);
            if (tag.Length > 0) Gfx.TextR(this, UiTheme.HudWide, tag, r.Size.X - pad, Gfx.Mid(sy, ss), ss, Equipped ? UiTheme.Accent : new Color(UiTheme.Dim, a));
        }
        else if (Equipped) DrawRect(new Rect2(r.Size.X - 18 * k, 6 * k, 10 * k, 3 * k), UiTheme.Accent); // short tiles: a red tick
    }

    static string Line(WeaponDef g)
    {
        var inv = CultureInfo.InvariantCulture;
        string dmg = g.Pellets > 1 ? $"{g.Pellets}×{g.DamageBody[0].ToString("0", inv)}" : $"{g.DamageHead[0].ToString("0", inv)}/{g.DamageBody[0].ToString("0", inv)}";
        return L.F($"{g.MaxRps.ToString("0.##", inv)}/s · {g.Mag} rds · {dmg}");
    }

    /// <summary>A small credits mark (a ring with a cut), not VALORANT's icon.</summary>
    internal static void CreditGlyph(CanvasItem ci, Vector2 c, float r, Color col)
    {
        ci.DrawArc(c - new Vector2(0, r * 0.75f), r, 0.5f, Mathf.Tau - 0.5f, 16, col, Mathf.Max(1f, r * 0.38f), true);
    }
}

/// <summary>
/// The drill preview's WEAPON row: "Weapon · [YOUR PICK] [silhouette] Phantom [CHANGE · B]". Clicking it (or B) opens the
/// gun picker.
/// </summary>
public partial class GunRow : BaseButton
{
    public string Label = L.N("Weapon");
    public Func<WeaponKind> Gun = () => WeaponKind.Vandal;
    public Func<string?>? Tag;
    public float K = 1f, H = 40f;
    public bool Stripe;
    float hover;

    public GunRow()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        TooltipText = L.T("Pick the gun for this drill (B)");
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, H * K);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseEntered += UiTheme.HoverSound;
        Pressed += UiTheme.ClickSound;
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
        if (GunIcons.Revision != iconRev) { iconRev = GunIcons.Revision; QueueRedraw(); }
    }
    int iconRev = -1;

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, pad = 10 * k;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(1, 1, 1, (Stripe ? 0.025f : 0f) + 0.045f * hover));
        DrawRect(new Rect2(0, 0, 2 * k, Size.Y), new Color(UiTheme.Accent, 0.35f + 0.65f * hover));
        int ls = UiTheme.Fs(15, k), vs = UiTheme.Fs(17, k), cs = UiTheme.Fs(11.5f, k);
        Gfx.Text(this, UiTheme.Body, Label, pad, Gfx.Mid(cy, ls), ls, UiTheme.Dim);

        // right to left: the CHANGE · B chip, the gun's name, its silhouette, the tag
        string chip = L.N("CHANGE · B");
        float cw = Gfx.TextW(UiTheme.HudWide, chip, cs) + 18 * k;
        var cr = new Rect2(Size.X - pad - cw, cy - 12 * k, cw, 24 * k);
        DrawRect(cr, new Color(UiTheme.Accent, 0.12f + 0.5f * hover));
        DrawRect(cr, new Color(UiTheme.Accent, 0.8f), false, 1f);
        Gfx.Text(this, UiTheme.HudWide, chip, cr.Position.X + 9 * k, Gfx.Mid(cy, cs), cs, UiTheme.Text);
        var gun = Gun();
        string name = L.Raw(GunPicker.GunName(gun)); // gun names stay English
        float nw = Gfx.TextW(UiTheme.Body, name, vs);
        float nx = cr.Position.X - 12 * k - nw;
        Gfx.Text(this, UiTheme.Body, name, nx, Gfx.Mid(cy, vs), vs, UiTheme.Text);
        float iw = 64 * k;
        float lw = pad + Gfx.TextW(UiTheme.Body, Label, ls) + 12 * k;
        string? tag = Tag?.Invoke();
        int ts = UiTheme.Fs(10.5f, k);
        float tw = tag != null ? Gfx.TextW(UiTheme.HudWide, tag, ts) + 12 * k : 0;
        // narrow column: the tag says more than the silhouette, so the silhouette goes first
        bool icon = nx - 10 * k - iw - (tag != null ? tw + 8 * k : 0) > lw;
        var ir = new Rect2(nx - 10 * k - iw, cy - 11 * k, iw, 22 * k);
        if (icon) GunIcons.Draw(this, ir, gun, new Color(UiTheme.Text, 0.85f));
        if (tag != null)
        {
            var tr = new Rect2((icon ? ir.Position.X : nx) - 8 * k - tw, cy - 9 * k, tw, 18 * k);
            if (tr.Position.X > lw)
            {
                // Tag returns the English key (DrillPreview.GunTag, L.N); Gfx.Text translates it when drawn
                var tc = tag == "YOUR PICK" ? UiTheme.Teal : tag == "PREFERRED" ? UiTheme.Warn : UiTheme.Dim;
                DrawRect(tr, new Color(tc, 0.12f));
                DrawRect(tr, new Color(tc, 0.7f), false, 1f);
                Gfx.Text(this, UiTheme.HudWide, tag, tr.Position.X + 6 * k, Gfx.Mid(cy, ts), ts, tc);
            }
        }
    }
}

/// <summary>
/// Gun silhouettes for the picker, drawn by the game itself (no Riot art). A gun with its own model gets a silhouette
/// rendered from that model once (an orthographic side view in a hidden sub-viewport, flat white, cached as a texture,
/// tinted when drawn). Every other gun (one that still borrows another gun's model, or when nothing can be rendered, as
/// under --headless) gets a flat vector icon drawn from a few parts per gun: stock, receiver, barrel, magazine, optic.
/// </summary>
public static class GunIcons
{
    static readonly Dictionary<string, Texture2D?> rendered = new();
    static readonly HashSet<string> pending = new();
    /// <summary>Bumped whenever a rendered silhouette arrives (widgets redraw).</summary>
    public static int Revision { get; private set; }

    /// <summary>The model scene of <paramref name="k"/> if the gun has its own (not one it borrows from another gun).</summary>
    static string? OwnScene(WeaponKind k)
    {
        string scene;
        try { scene = VmPose.For(k).Scene; } catch { return null; }
        if (string.IsNullOrEmpty(scene)) return null;
        if (WeaponLooks.For(k).Base == k) return scene;
        foreach (var w in Weapons.All)
            if (w.Kind != k && VmPose.For(w.Kind).Scene == scene) return null;
        return scene;
    }

    /// <summary>Starts rendering the silhouettes not made yet (once per model; nothing under --headless).</summary>
    public static void Warm()
    {
        if (DisplayServer.GetName() == "headless" || Main.I == null) return;
        foreach (var w in Weapons.All)
        {
            if (OwnScene(w.Kind) is not { } scene || rendered.ContainsKey(scene) || !pending.Add(scene)) continue;
            Render(scene);
        }
    }

    static async void Render(string scene)
    {
        SubViewport? vp = null;
        Texture2D? tex = null;
        try
        {
            if (ResourceLoader.Load<PackedScene>(scene) is not { } packed) return;
            vp = new SubViewport
            {
                OwnWorld3D = true, TransparentBg = true, Size = new Vector2I(768, 288), Msaa3D = Viewport.Msaa.Msaa4X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = false, GuiDisableInput = true, AudioListenerEnable3D = false,
            };
            Main.I.AddChild(vp);
            var gun = packed.Instantiate<Node3D>();
            vp.AddChild(gun);
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.White };
            Aabb? box = null;
            foreach (var mi in Meshes(gun))
            {
                mi.MaterialOverride = mat;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                var b = mi.GlobalTransform * mi.GetAabb();
                box = box is { } bb ? bb.Merge(b) : b;
            }
            if (box is not { } bounds || bounds.Size.Z < 1e-3f) return;
            // side view from the gun's right (+X): the barrel (-Z) points right on screen
            var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 0.01f, Far = 20f };
            vp.AddChild(cam);
            var c = bounds.GetCenter();
            float aspect = 768f / 288f;
            cam.Size = Mathf.Max(bounds.Size.Y, bounds.Size.Z / aspect) * 1.08f;
            cam.LookAtFromPosition(c + new Vector3(bounds.Size.X + 3f, 0, 0), c, Vector3.Up);
            cam.Current = true;
            for (int i = 0; i < 3; i++) await Main.I.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = vp.GetTexture()?.GetImage();
            if (img == null || img.IsEmpty()) return;
            var used = img.GetUsedRect();
            if (used.Size.X < 8 || used.Size.Y < 4) return;
            img = img.GetRegion(used);
            img.GenerateMipmaps();
            tex = ImageTexture.CreateFromImage(img);
        }
        catch (Exception e) { Log.Error($"[gunicons] {scene}: {e.Message}"); }
        finally
        {
            rendered[scene] = tex;
            pending.Remove(scene);
            if (vp != null && GodotObject.IsInstanceValid(vp)) vp.QueueFree();
            if (tex != null) Revision++;
        }
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var m in Meshes(c)) yield return m;
    }

    /// <summary>Relative size in the tile: pistols fill its height, SMGs are shorter than rifles.</summary>
    static float Len(WeaponKind k) => k switch
    {
        WeaponKind.Stinger => 0.74f, WeaponKind.Spectre => 0.8f, WeaponKind.Judge => 0.9f, WeaponKind.Bulldog => 0.9f,
        WeaponKind.Phantom or WeaponKind.Vandal => 0.95f,
        _ => 1f,
    };

    static bool Pistol(WeaponKind k) => Weapons.Get(k)?.Class == WeaponClass.Sidearm;

    /// <summary>Draws <paramref name="k"/>'s silhouette fitted into <paramref name="r"/> (muzzle right) in <paramref name="c"/>.</summary>
    public static void Draw(CanvasItem ci, Rect2 r, WeaponKind k, Color c)
    {
        if (OwnScene(k) is { } scene && rendered.TryGetValue(scene, out var tex) && tex != null)
        {
            float len = Pistol(k) ? 0.9f : Len(k);
            var ts = tex.GetSize();
            float s = Mathf.Min(r.Size.X * len / ts.X, r.Size.Y * (Pistol(k) ? 1f : len) / ts.Y);
            var size = ts * s;
            ci.DrawTextureRect(tex, new Rect2(r.GetCenter() - size / 2, size), false, c);
            return;
        }
        DrawVector(ci, r, k, c);
    }

    // ---------------- vector icons ----------------

    /// <summary>Maps the icon's unit coordinates (x 0 = butt … 1 = muzzle, y down) into the target rect.</summary>
    readonly struct Pen
    {
        readonly CanvasItem ci;
        readonly Vector2 o;
        readonly float s;
        readonly Color c;
        public Pen(CanvasItem ci, Vector2 o, float s, Color c) { this.ci = ci; this.o = o; this.s = s; this.c = c; }
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        public void Rect(float x0, float y0, float x1, float y1) => ci.DrawRect(new Rect2(P(x0, y0), new Vector2(x1 - x0, y1 - y0) * s), c);
        public void Poly(params float[] xy)
        {
            var p = new Vector2[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = P(xy[2 * i], xy[2 * i + 1]);
            ci.DrawColoredPolygon(p, c);
        }
        public void Circle(float x, float y, float r) => ci.DrawCircle(P(x, y), r * s, c);
        public void Line(float x0, float y0, float x1, float y1, float w) => ci.DrawLine(P(x0, y0), P(x1, y1), c, Mathf.Max(1f, w * s), true);
    }

    static void DrawVector(CanvasItem ci, Rect2 r, WeaponKind k, Color c)
    {
        bool pistol = Pistol(k);
        // icon box in unit coordinates (long guns: 1 × 0.32 with the optic above y = 0; pistols: 1 × 0.8)
        float bw = pistol ? (k == WeaponKind.Ghost ? 1.42f : 1f) : 1f;
        float bh = pistol ? (k == WeaponKind.Frenzy ? 0.98f : 0.8f) : 0.33f;
        float y0 = pistol ? 0f : -0.03f;
        float len = pistol ? 0.9f : Len(k);
        float s = Mathf.Min(r.Size.X / bw, r.Size.Y / bh) * len;
        var o = r.GetCenter() - new Vector2(bw, bh) * s / 2 - new Vector2(0, y0 * s);
        var p = new Pen(ci, o, s, c);
        if (pistol) DrawPistol(p, k); else DrawLong(p, k);
    }

    static void DrawPistol(Pen p, WeaponKind k)
    {
        // grip + trigger guard, common to every sidearm
        void Grip(float x = 0f)
        {
            p.Poly(0.10f + x, 0.30f, 0.36f + x, 0.30f, 0.30f + x, 0.78f, 0.03f + x, 0.74f);
            p.Line(0.36f + x, 0.40f, 0.50f + x, 0.40f, 0.035f);
            p.Line(0.50f + x, 0.40f, 0.52f + x, 0.30f, 0.035f);
        }
        switch (k)
        {
            case WeaponKind.Sheriff:
                p.Rect(0.42f, 0.10f, 1.0f, 0.21f);   // barrel
                p.Rect(0.42f, 0.07f, 0.95f, 0.10f);  // top rib
                p.Rect(0.14f, 0.06f, 0.46f, 0.33f);  // frame + cylinder
                p.Poly(0.08f, 0.02f, 0.18f, 0.06f, 0.16f, 0.12f);  // hammer
                Grip(0.02f);
                break;
            case WeaponKind.Shorty:
                p.Rect(0.20f, 0.06f, 1.0f, 0.15f);
                p.Rect(0.20f, 0.17f, 1.0f, 0.26f);
                p.Rect(0.10f, 0.05f, 0.36f, 0.30f);
                Grip();
                break;
            case WeaponKind.Frenzy:
                p.Rect(0.0f, 0.08f, 0.90f, 0.28f);
                p.Rect(0.12f, 0.28f, 0.68f, 0.36f);
                Grip();
                p.Rect(0.06f, 0.74f, 0.26f, 0.98f); // long magazine below the grip
                break;
            case WeaponKind.Ghost:
                p.Rect(0.0f, 0.08f, 0.90f, 0.27f);
                p.Rect(0.12f, 0.27f, 0.68f, 0.35f);
                p.Rect(0.88f, 0.11f, 1.42f, 0.24f); // silencer
                Grip();
                break;
            case WeaponKind.Bandit:
                p.Rect(0.0f, 0.06f, 0.86f, 0.30f);
                p.Rect(0.86f, 0.10f, 0.98f, 0.26f); // compensator
                p.Rect(0.12f, 0.30f, 0.66f, 0.37f);
                Grip();
                break;
            default: // Classic, Headhunter
                p.Rect(0.0f, 0.08f, 0.92f, 0.28f);
                p.Rect(0.12f, 0.28f, 0.70f, 0.36f);
                Grip();
                break;
        }
    }

    static void DrawLong(Pen p, WeaponKind k)
    {
        // parts per gun: stock (0 none, 1 solid, 2 skeleton), magazine (0 none, 1 straight, 2 curved, 3 drum, 4 box,
        // 5 under-barrel tube), optic (0 none, 1 red dot, 2 scope, 3 big scope), silencer, bipod, pump, handguard end
        (int Stock, int Mag, int Optic, bool Sil, bool Bipod, bool Pump, float Hg, float Thick) part = k switch
        {
            WeaponKind.Stinger => (2, 1, 1, false, false, false, 0.74f, 0f),
            WeaponKind.Spectre => (2, 2, 1, true, false, false, 0.70f, 0f),
            WeaponKind.Bucky => (1, 5, 0, false, false, true, 0.84f, 0f),
            WeaponKind.Judge => (1, 4, 1, false, false, false, 0.80f, 0.01f),
            WeaponKind.Bulldog => (1, 1, 1, false, false, false, 0.80f, 0f),
            WeaponKind.Guardian => (1, 1, 2, false, false, false, 0.82f, 0f),
            WeaponKind.Phantom => (1, 1, 1, true, false, false, 0.74f, 0f),
            WeaponKind.Vandal => (1, 2, 0, false, false, false, 0.80f, 0f),
            WeaponKind.Warden => (1, 1, 2, false, false, false, 0.84f, 0f),
            WeaponKind.Marshal => (1, 1, 2, false, false, false, 0.78f, -0.005f),
            WeaponKind.Outlaw => (1, 0, 2, false, false, false, 0.74f, 0f),
            WeaponKind.Operator or WeaponKind.TourDeForce => (1, 1, 3, false, true, false, 0.78f, 0.005f),
            WeaponKind.Ares => (1, 4, 0, false, true, false, 0.84f, 0.015f),
            WeaponKind.Odin => (1, 4, 1, false, true, false, 0.86f, 0.025f),
            _ => (1, 1, 0, false, false, false, 0.80f, 0f),
        };
        float t = part.Thick;
        // stock
        if (part.Stock == 1) p.Poly(0.0f, 0.07f, 0.21f, 0.07f, 0.21f, 0.145f, 0.035f, 0.215f, 0.0f, 0.215f);
        else if (part.Stock == 2)
        {
            p.Line(0.04f, 0.08f, 0.21f, 0.08f, 0.022f);
            p.Line(0.04f, 0.08f, 0.04f, 0.19f, 0.03f);
            p.Line(0.04f, 0.19f, 0.21f, 0.13f, 0.02f);
        }
        // receiver, handguard, barrel
        p.Rect(0.20f, 0.06f - t, 0.58f, 0.155f + t);
        p.Rect(0.57f, 0.07f - t, part.Hg, 0.135f + t);
        if (part.Sil) p.Rect(part.Hg - 0.01f, 0.075f, 1.0f, 0.13f);
        else
        {
            p.Rect(part.Hg - 0.01f, 0.09f, 1.0f, 0.112f);
            p.Rect(0.975f, 0.08f, 1.0f, 0.122f); // muzzle device
        }
        // grip + trigger guard
        p.Poly(0.31f, 0.15f, 0.37f, 0.15f, 0.345f, 0.27f, 0.285f, 0.265f);
        p.Line(0.37f, 0.19f, 0.42f, 0.19f, 0.012f);
        // magazine
        switch (part.Mag)
        {
            case 1: p.Poly(0.43f, 0.15f, 0.49f, 0.15f, 0.50f, 0.285f, 0.44f, 0.29f); break;
            case 2: p.Poly(0.43f, 0.15f, 0.49f, 0.15f, 0.535f, 0.29f, 0.475f, 0.305f); break;
            case 3: p.Circle(0.47f, 0.21f, 0.06f); break;
            case 4: p.Rect(0.40f, 0.15f, 0.53f, 0.255f); break;
            case 5: p.Rect(0.57f, 0.135f, 0.86f, 0.16f); break;
        }
        if (part.Pump) p.Rect(0.60f, 0.125f, 0.73f, 0.18f);
        // optic
        switch (part.Optic)
        {
            case 1:
                p.Rect(0.35f, 0.045f, 0.46f, 0.062f);
                p.Rect(0.37f, 0.015f, 0.44f, 0.05f);
                break;
            case 2:
            case 3:
                float h = part.Optic == 3 ? 0.012f : 0f;
                p.Rect(0.28f, -0.002f - h, 0.53f, 0.04f + h);
                p.Rect(0.50f, -0.012f - h, 0.57f, 0.05f + h);
                p.Rect(0.33f, 0.035f, 0.35f, 0.065f);
                p.Rect(0.46f, 0.035f, 0.48f, 0.065f);
                break;
        }
        if (part.Bipod)
        {
            p.Line(part.Hg - 0.03f, 0.13f, part.Hg - 0.07f, 0.29f, 0.012f);
            p.Line(part.Hg - 0.03f, 0.13f, part.Hg + 0.01f, 0.29f, 0.012f);
        }
    }
}
