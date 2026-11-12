using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Popup shown when a drill card in the menu is clicked, instead of starting the drill right away (drills on a VALORANT map
/// without a gun choice and the Agents screen's drills still start directly). Left: a live
/// demo — the drill itself, played by the simulated player in a sub-viewport (<see cref="GameSession.Demo"/>: muted, no
/// input, nothing saved, loops). Right: what the drill is, how long it runs, the weapon and map, which coach skills it
/// feeds, your record at the chosen tier and gun (best, badge, runs, recent scores) and the tier picker. START (Space / Enter) plays
/// it, FREE PLAY (F) plays it without a clock or score (drills the simulated player can play), BACK (Esc) closes. Drills that need a
/// person (Reaction Test, Sens Finder) and drills on a VALORANT map show a note instead of a demo.
/// <para>Drills that offer a gun choice (<see cref="TrainingMode.WeaponChoice"/>) have an interactive WEAPON row: clicking it or
/// B opens the <see cref="GunPicker"/>; a pick restarts the demo with that gun, is remembered for the drill and is what START /
/// FREE PLAY play. K opens the <see cref="SkinPicker"/> on the shown gun; the demo re-skins live.</para>
/// <para>Dev: <c>--preview &lt;drill key&gt;</c> opens it over the menu (screenshots; <c>--preview-answer start|free|back</c> presses
/// that button after 3 s; <c>--preview-answer gun:Phantom</c> opens the gun picker after 3 s and picks that gun 2.5 s later;
/// <c>--preview-answer skins</c> opens the skin picker on the shown gun, as K does);
/// <c>--preview-check</c> runs every drill's demo headless for a few seconds and checks the simulated player plays it and
/// nothing is saved (<c>--preview-gun Phantom</c>: with that gun wherever the drill offers it; <c>all</c>: each drill once per
/// gun class it offers).</para>
/// </summary>
public partial class DrillPreview : Control
{
    /// <summary>A demo loops after this long (shorter than most drills: the loop shows the start again).</summary>
    const float DemoSeconds = 24f;

    readonly float k;
    readonly Func<TrainingMode> make;
    readonly TrainingMode info;
    readonly string? title, sub;
    readonly Action<WeaponKind?> onStart;
    readonly Action onClose;
    readonly Action<WeaponKind?>? onFreePlay;

    SubViewport? vp;
    GameSession? demo;
    Control? demoBox;
    DrawBox record = null!, tierInfo = null!;
    InfoRow? whereRow;
    GunRow? gunRow;
    GunPicker? picker;
    DrawBox? noDemoBox;
    Maps.MapSpot? shownMap;
    readonly List<VButton> tierButtons = new();
    bool done;
    /// <summary>The gun shown and played: the drill's resolved pick (per-drill pick → preferred gun → default).</summary>
    WeaponKind gun;

    /// <summary>The drill offers a gun choice: interactive WEAPON row, B opens the picker.</summary>
    bool CanPick => info.WeaponChoice is { Count: > 0 } && !info.Is2D && info.Weapon != WeaponKind.None;
    /// <summary>The explicit pick for sessions started from here (null = the drill's own gun).</summary>
    WeaponKind? PickArg => CanPick ? gun : null;

    /// <param name="title">Card title instead of the drill's name (Agents screen); <paramref name="sub"/>: its second line.</param>
    /// <param name="onStart">Starts the drill with the picked gun (null = its own) after the popup and its demo are gone.</param>
    /// <param name="onFreePlay">Starts it in free play (no FREE PLAY button when null or the drill needs a person).</param>
    public DrillPreview(float k, Func<TrainingMode> make, TrainingMode info, Action<WeaponKind?> onStart, Action onClose, string? title = null, string? sub = null,
        Action<WeaponKind?>? onFreePlay = null)
    {
        this.onFreePlay = CanDemo(info) ? onFreePlay : null;
        this.k = k;
        this.make = make;
        this.info = info;
        this.onStart = onStart;
        this.onClose = onClose;
        this.title = title;
        this.sub = sub;
        gun = CanPick ? WeaponPicks.Resolve(info) ?? info.Weapon : info.Weapon;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the screen underneath doesn't get clicks
    }

    /// <summary>Opens the preview over <paramref name="parent"/> (one at a time). <paramref name="start"/> /
    /// <paramref name="freePlay"/> get the picked gun (null = the drill resolves its own).</summary>
    public static void Open(Control parent, float k, Func<TrainingMode> make, Action<WeaponKind?> start, string? title = null, string? sub = null,
        Action<WeaponKind?>? freePlay = null)
    {
        if (parent.GetChildren().Any(c => c is DrillPreview)) return;
        TrainingMode info;
        try { info = make(); }
        catch (Exception e) { Log.Error($"[preview] can't build the drill: {e.Message}"); start(null); return; }
        if (!info.HasPreview) { start(null); return; } // drills on a VALORANT map without a gun choice go straight in
        // BACK: the cards' PBs follow a gun picked in the preview
        parent.AddChild(new DrillPreview(k, make, info, start, () => RefreshCards(parent), title, sub, freePlay));
    }

    static void RefreshCards(Node n)
    {
        if (!IsInstanceValid(n)) return;
        foreach (var c in n.GetChildren())
        {
            if (c is ModeCard card) card.Refresh();
            else RefreshCards(c);
        }
    }

    /// <summary>Whether a drill can be demoed by the simulated player (2D drills and the Sens Finder need a person).</summary>
    public static bool CanDemo(TrainingMode m) => m.PreviewDemo;

    public override void _Ready()
    {
        if (CanPick) GunIcons.Warm(); // the WEAPON row and the picker show silhouettes rendered from the gun models
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.78f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vs = GetViewportRect().Size;
        float w = Mathf.Min(1480 * k, vs.X - 70 * k);
        var panel = new VPanel
        {
            Title = L.Upper(title != null ? L.T(title) : info.DisplayName), Caption = Caption(), K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.98f),
            CustomMinimumSize = new Vector2(w, 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(14 * k));
        panel.AddChild(col);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", (int)(24 * k));
        col.AddChild(body);

        // ---- left: the live demo (16:9) ----
        float rightW = 470 * k;
        float demoW = w - 2 * 22 * k - rightW - 24 * k;
        float demoH = Mathf.Min(demoW * 9 / 16, vs.Y - 330 * k);
        demoW = demoH * 16 / 9;
        demoBox = new Control { CustomMinimumSize = new Vector2(demoW, demoH), MouseFilter = MouseFilterEnum.Ignore };
        body.AddChild(demoBox);
        BuildDemo(demoBox, demoW, demoH);

        // ---- right: about the drill + your record ----
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(rightW, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", (int)(6 * k));
        body.AddChild(right);

        if (sub != null) right.AddChild(Text(L.T(sub), 16, UiTheme.Warn));
        right.AddChild(Text(info.DisplayDescription, 17, UiTheme.Text));
        if (info.MadeBy is { } by) right.AddChild(Text(L.F($"Made by the community: @{by}"), 15, UiTheme.Teal));
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * k), MouseFilter = MouseFilterEnum.Ignore });

        int stripe = 0;
        void Row(string label, string value) =>
            right.AddChild(new InfoRow { K = k, H = 32, Label = label, Value = () => value, Stripe = stripe++ % 2 == 0 });
        Row("Length", info.Timed ? L.F($"{info.Duration:0} s") : L.N("Until you finish it"));
        if (CanPick)
        {
            gunRow = new GunRow { K = k, H = 40, Label = "Weapon", Gun = () => gun, Tag = GunTag, Stripe = stripe++ % 2 == 0, TooltipText = L.T("Pick the gun for this drill (B) · K: its skin") };
            gunRow.Pressed += OpenPicker;
            right.AddChild(gunRow);
        }
        else if (!info.Is2D && info.Weapon != WeaponKind.None) Row("Weapon", L.Raw(WeaponName(info.Weapon))); // gun names stay English
        // the map the demo is playing (a random map pick changes per run); RANDOM tag when the map picker is on random
        whereRow = new InfoRow
        {
            K = k, H = 32, Label = "Where", Stripe = stripe++ % 2 == 0,
            // map and spot names are VALORANT's (never translated)
            Value = () => (demo?.Mode?.Map ?? info.Map) is { } m ? L.Raw($"{Cap(m.Map)} · {m.Name}") : info.Is2D ? L.N("On screen") : L.N("Shooting range"),
            Tag = () => info.Map != null && Main.I.MapKey == "random" ? L.N("RANDOM") : null,
        };
        right.AddChild(whereRow);
        Row("Trains", Trains(info));

        right.AddChild(new SectionLabel { Text = "YOUR RECORD", K = k, CustomMinimumSize = new Vector2(0, 34 * k) });
        record = new DrawBox { CustomMinimumSize = new Vector2(0, (CanPick ? 120 : 112) * k), OnDraw = DrawRecord };
        right.AddChild(record);

        right.AddChild(new SectionLabel { Text = "DIFFICULTY", Note = "bots and targets scale with it", K = k, CustomMinimumSize = new Vector2(0, 34 * k) });
        var tiers = new HBoxContainer();
        tiers.AddThemeConstantOverride("separation", (int)(6 * k));
        right.AddChild(tiers);
        foreach (var t in Difficulty.Tiers)
        {
            int id = t.Id;
            var b = new VButton
            {
                Label = L.Upper(L.T(t.Name)), Kind = VButton.Look.Select, K = k, FontPx = 13, Selected = Main.I.Tier == id,
                CustomMinimumSize = new Vector2(0, 40 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = L.T(t.Name) + " · " + L.T(t.Ranks),
            };
            b.Pressed += () => SelectTier(id);
            tierButtons.Add(b);
            tiers.AddChild(b);
        }
        tierInfo = new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 26 * k),
            OnDraw = d =>
            {
                var t = Difficulty.Get(Main.I.Tier);
                int fs = UiTheme.Fs(14, k);
                Gfx.Text(d, UiTheme.Body, L.T(t.Name) + " · " + L.T(t.Ranks), 2 * k, Gfx.Mid(d.Size.Y / 2, fs), fs, UiTheme.TierColor(t.Id));
            },
        };
        right.AddChild(tierInfo);

        // ---- footer ----
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(foot);
        // the in-drill buy menu (GameSession, BuySession.cs) changes the gun mid-run with VALORANT's buy-menu bind
        string buy = Main.I.Valorant.Binds.Short(Valorant.GameAction.BuyMenu);
        // key names (B, the buy-menu bind) stay as they are; the comparisons are on key names, never on translated text
        var keyParts = new List<string> { L.T("Space / Enter starts") };
        if (onFreePlay != null) keyParts.Add(L.T("F free play"));
        if (CanPick) keyParts.Add(buy == "B" ? L.T("B picks the gun, also mid-drill") : buy == "—" ? L.T("B picks the gun") : L.F($"B picks the gun ({buy} mid-drill)"));
        if (CanSkin) keyParts.Add(L.T("K skins"));
        keyParts.Add(L.T("Esc goes back"));
        string keys = string.Join(", ", keyParts) + ".";
        var hint = Text(CanDemo(info) ? L.F($"Preview: the built-in simulated player, muted. {keys}") : keys, 14, UiTheme.Faint);
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foot.AddChild(hint);
        var back = new VButton { Label = "BACK", Kind = VButton.Look.Secondary, K = k, FontPx = 18, CustomMinimumSize = new Vector2(160 * k, 54 * k) };
        back.Pressed += Close;
        foot.AddChild(back);
        if (onFreePlay != null)
        {
            var free = new VButton { Label = "FREE PLAY", Kind = VButton.Look.Secondary, K = k, FontPx = 18, CustomMinimumSize = new Vector2(190 * k, 54 * k) };
            free.TooltipText = L.T("Play it with no clock and no score, and nothing saved. It starts over when the time is up; Esc → menu to stop.");
            free.Pressed += () => Start(true);
            foot.AddChild(free);
        }
        var start = new VButton { Label = "START", Kind = VButton.Look.Primary, K = k, FontPx = 22, CustomMinimumSize = new Vector2(260 * k, 54 * k) };
        start.Pressed += () => Start();
        foot.AddChild(start);

        // Dev: "--preview-answer start|free|back" presses that button after 3 s (flow tests); "gun:Phantom" opens the gun
        // picker after 3 s with the focus on that gun and picks it 2.5 s later (screenshots of both)
        if (CmdLine.DevAfter("--preview-answer")?.ToLowerInvariant() is { } ans)
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || done) return;
                Log.Info($"[preview] --preview-answer {ans}");
                if (ans.StartsWith("gun:")) DevPickGun(ans[4..]);
                else if (ans == "skins") OpenSkins();
                else if (ans == "start") Start(); else if (ans == "free" && onFreePlay != null) Start(true); else Close();
            };
    }

    void DevPickGun(string name)
    {
        if (Weapons.Parse(name) is not { } g) { Log.Error($"[preview] --preview-answer gun:{name}: not a gun"); return; }
        if (!CanPick) { Log.Info($"[preview] {info.Key} offers no gun choice (locked to the {WeaponName(info.Weapon)})"); return; }
        OpenPicker();
        picker?.FocusGun(g);
        GetTree().CreateTimer(2.5).Timeout += () =>
        {
            if (!IsInstanceValid(this) || done || picker == null || !IsInstanceValid(picker)) return;
            picker.Pick(g);
            Log.Info($"[preview] picked {g}: {info.Key} now plays the {WeaponName(gun)}");
        };
    }

    // ---------------- gun ----------------

    /// <summary>Where the shown gun comes from: the drill's default, the player's pick for it or their preferred gun. English
    /// keys (GunRow colours by them; it translates when drawing).</summary>
    string GunTag()
    {
        var choice = info.WeaponChoice;
        if (choice != null && WeaponPicks.PickFor(info.Key) is { } own && own == gun && choice.Contains(own)) return own == info.Weapon ? L.N("DRILL DEFAULT") : L.N("YOUR PICK");
        if (choice != null && WeaponPicks.Preferred() is { } pref && pref == gun && choice.Contains(pref) && pref != info.Weapon) return L.N("PREFERRED");
        return gun == info.Weapon ? L.N("DRILL DEFAULT") : L.N("YOUR PICK");
    }

    void OpenPicker()
    {
        if (!CanPick || done || (picker != null && IsInstanceValid(picker))) return;
        picker = GunPicker.OpenForDrill(this, k, info, gun, SetGun, title);
        if (picker != null) picker.Closed += () => picker = null;
    }

    /// <summary>The drill shows a gun that can wear a skin: K opens the skin picker on it.</summary>
    bool CanSkin => !info.Is2D && info.Weapon != WeaponKind.None && Game.Weapon.GunSkins.Skinnable(gun);

    /// <summary>K: the skin picker on the shown gun; the live demo re-skins as skins are applied (Viewmodel.Reskin).</summary>
    void OpenSkins()
    {
        if (!CanSkin || done || (skinPicker != null && IsInstanceValid(skinPicker))) return;
        skinPicker = SkinPicker.Open(this, k, gun, () => skinPicker = null);
    }
    SkinPicker? skinPicker;

    void SetGun(WeaponKind g)
    {
        bool changed = g != gun;
        gun = g;
        gunRow?.QueueRedraw();
        noDemoBox?.QueueRedraw();
        record.QueueRedraw();
        if (changed) StartDemo(); // the demo plays the new gun
    }

    string Caption()
    {
        string cat = L.Upper(info.DisplayCategory);
        return info.MadeBy != null ? cat + " · " + L.T("COMMUNITY") : cat;
    }

    // ---------------- demo ----------------

    void BuildDemo(Control box, float w, float h)
    {
        if (!CanDemo(info))
        {
            bool onMap = info.Map != null;
            noDemoBox = new DrawBox
            {
                Size = new Vector2(w, h), TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                OnDraw = d =>
                {
                    var r = new Rect2(Vector2.Zero, d.Size);
                    Gfx.VGradient(d, r, Color.Color8(22, 34, 46), Color.Color8(12, 20, 28));
                    Gfx.Brackets(d, r.Grow(-1), 18 * k, new Color(UiTheme.Text, 0.3f), Mathf.Max(1, 2 * k));
                    int fs = UiTheme.Fs(22, k), bs = UiTheme.Fs(16, k);
                    var c = r.GetCenter();
                    float cy = CanPick ? c.Y - 70 * k : c.Y;
                    // centred, kept inside the box (translations run longer; English fits as designed)
                    float tw = r.Size.X - 24 * k;
                    Gfx.TextFit(d, UiTheme.DisplayWide, "NO PREVIEW FOR THIS ONE", 12 * k, Gfx.Mid(cy - 18 * k, fs), fs, UiTheme.Text, tw, HorizontalAlignment.Center);
                    Gfx.TextFit(d, UiTheme.Body, onMap ? "Map drills start straight on the map, so there is no live preview."
                        : "It runs on your own picks and reactions. Press START to try it.", 12 * k, Gfx.Mid(cy + 20 * k, bs), bs, UiTheme.Dim, tw, HorizontalAlignment.Center);
                    if (!CanPick) return;
                    // the gun you'll play, big
                    var ir = new Rect2(c.X - 210 * k, c.Y + 6 * k, 420 * k, 110 * k);
                    GunIcons.Draw(d, ir, gun, new Color(UiTheme.Text, 0.9f));
                    int ls = UiTheme.Fs(13, k);
                    Gfx.TextC(d, UiTheme.HudWide, L.F($"YOUR GUN · {GunPicker.GunName(gun).ToUpperInvariant()} · B TO CHANGE"), c.X, Gfx.Mid(ir.End.Y + 24 * k, ls), ls, UiTheme.Dim);
                },
            };
            box.AddChild(noDemoBox);
            return;
        }
        var container = new SubViewportContainer { Stretch = true, Size = new Vector2(w, h), MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(container);
        vp = new SubViewport
        {
            OwnWorld3D = true, HandleInputLocally = false, GuiDisableInput = true, AudioListenerEnable3D = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I((int)w, (int)h),
        };
        container.AddChild(vp);
        StartDemo();
        // label + frame over the demo
        box.AddChild(new DrawBox
        {
            Size = new Vector2(w, h),
            OnDraw = d =>
            {
                var r = new Rect2(Vector2.Zero, d.Size);
                d.DrawRect(r, new Color(UiTheme.Text, 0.14f), false, 1f);
                int ts = UiTheme.Fs(12, k);
                string tag = L.N("PREVIEW · SIMULATED PLAYER");
                float tw = Gfx.TextW(UiTheme.HudWide, tag, ts) + 20 * k;
                var tr = new Rect2(d.Size.X - tw - 12 * k, 12 * k, tw, 26 * k); // top right: clear of every drill's HUD
                d.DrawRect(tr, new Color(0.04f, 0.07f, 0.1f, 0.78f));
                d.DrawRect(new Rect2(tr.Position, new Vector2(3 * k, tr.Size.Y)), UiTheme.Accent);
                Gfx.Text(d, UiTheme.HudWide, tag, tr.Position.X + 11 * k, Gfx.Mid(tr.GetCenter().Y, ts), ts, UiTheme.Text);
            },
        });
    }

    void StartDemo()
    {
        if (vp == null) return;
        StopDemo(false);
        try
        {
            demo = new GameSession(make) { Demo = true, DurationOverride = info.Timed ? Mathf.Min(info.Duration, DemoSeconds) : null, WeaponPick = PickArg };
            vp.AddChild(demo);
        }
        catch (Exception e)
        {
            Log.Error($"[preview] demo of {info.Key} failed: {e.Message}");
            StopDemo(false);
        }
    }

    /// <summary>Stops the demo right away (it must not tick once more after a real session starts: effects are shared).</summary>
    void StopDemo(bool freeViewport)
    {
        if (demo != null && IsInstanceValid(demo))
        {
            demo.ProcessMode = ProcessModeEnum.Disabled;
            demo.GetParent()?.RemoveChild(demo);
            demo.QueueFree();
            Mover.KeyOverride = null; // scripted demo keys (jump peeks, agent walks) must never outlive the demo
        }
        demo = null;
        if (freeViewport && vp != null && IsInstanceValid(vp))
        {
            vp.GetParent()?.RemoveChild(vp);
            vp.QueueFree();
            vp = null;
        }
    }

    // ---------------- right column ----------------

    void DrawRecord(DrawBox d)
    {
        int tier = Main.I.Tier;
        var stats = Main.I.Stats;
        // this gun's runs (old runs without a gun count as the drill's default)
        var runs = stats.ForGun(info.Key, gun, info.Weapon).Where(r => r.Tier == tier && !r.Warmup).OrderBy(r => r.When).ToList();
        int best = runs.Count > 0 ? runs.Max(r => r.Score) : -1;
        int badge = stats.BestBadge(info);
        bool rated = GunRating.IsRated(info, gun);
        float y = 4 * k;
        int ls = UiTheme.Fs(13, k), vs = UiTheme.Fs(30, k), ss = UiTheme.Fs(14, k);
        var inv = CultureInfo.InvariantCulture;
        string head = L.F($"BEST · {L.Upper(L.T(Difficulty.Get(tier).Name))}") + (CanPick ? $" · {GunPicker.GunName(gun).ToUpperInvariant()}" : "");
        Gfx.TextFit(d, UiTheme.HudWide, head, 2 * k, y + ls, ls, UiTheme.Dim, d.Size.X * 0.57f);
        if (best >= 0)
        {
            Gfx.Text(d, UiTheme.Display, best.ToString("#,0", inv), 2 * k, y + ls + 8 * k + vs * 0.8f, vs, UiTheme.Good);
            string ago = Ago(runs[^1].When);
            string meta = L.P(runs.Count, $"{runs.Count} run at this tier · last {ago}", $"{runs.Count} runs at this tier · last {ago}");
            Gfx.TextFit(d, UiTheme.Body, meta, 2 * k, y + ls + vs + 22 * k, ss, UiTheme.Dim, d.Size.X * 0.57f);
        }
        else
        {
            Gfx.Text(d, UiTheme.Display, "—", 2 * k, y + ls + 8 * k + vs * 0.8f, vs, UiTheme.Faint);
            Gfx.TextFit(d, UiTheme.Body, CanPick ? L.F($"No {GunPicker.GunName(gun)} runs at this tier yet") : L.T("Not played at this tier yet"),
                2 * k, y + ls + vs + 22 * k, ss, UiTheme.Dim, d.Size.X * 0.57f);
        }
        // another gun with a higher best at this tier: a faint line under it
        if (CanPick)
        {
            var other = stats.For(info.Key).Where(r => r.Tier == tier && !r.Warmup)
                .Select(r => (Gun: StatsStore.GunOf(r, info.Weapon), r.Score)).Where(x => x.Gun != gun && x.Gun != WeaponKind.None)
                .OrderByDescending(x => x.Score).FirstOrDefault();
            if (other.Score > best && other.Gun != WeaponKind.None)
                Gfx.TextFit(d, UiTheme.Body, L.F($"{GunPicker.GunName(other.Gun)} best {other.Score.ToString("#,0", inv)}"), 2 * k, y + ls + vs + 42 * k, UiTheme.Fs(13, k),
                    UiTheme.Faint, d.Size.X * 0.57f);
        }
        // best badge ever (any tier, rated guns only) + the last scores at this tier
        float bx = d.Size.X * 0.6f;
        if (badge >= -1 && stats.For(info.Key).Any())
        {
            RankEmblem.Draw(d, new Vector2(bx + 16 * k, y + 26 * k), 16 * k, badge, badge >= 0 ? 1f : 0.55f);
            int ts = UiTheme.Fs(10, k);
            Gfx.Text(d, UiTheme.HudWide, "BEST BADGE", bx + 40 * k, Gfx.Mid(y + 8 * k, ts), ts, UiTheme.Faint);
            var bt = badge >= 0 ? Difficulty.Get(badge) : null;
            Gfx.TextFit(d, UiTheme.Body, bt != null ? L.T(bt.Name) : L.T("Below Rookie"), bx + 40 * k, Gfx.Mid(y + 25 * k, ss), ss, bt != null ? UiTheme.TierColor(bt.Id) : UiTheme.Dim, d.Size.X - bx - 40 * k);
            if (!rated) Gfx.TextFit(d, UiTheme.Body, L.F($"Rated: {GunRating.RatedShort(info)}"), bx + 40 * k, Gfx.Mid(y + 42 * k, ts + 2), ts + 2, UiTheme.Warn, d.Size.X - bx - 40 * k);
            else if (bt != null) Gfx.TextFit(d, UiTheme.Body, bt.Ranks, bx + 40 * k, Gfx.Mid(y + 42 * k, ts + 2), ts + 2, UiTheme.Dim, d.Size.X - bx - 40 * k);
        }
        else if (!rated)
        {
            int ts = UiTheme.Fs(12, k);
            Gfx.TextFit(d, UiTheme.Body, L.F($"Unrated gun: badges count with {GunRating.DisplayRatedText(info)}"), bx, Gfx.Mid(y + 25 * k, ts), ts, UiTheme.Warn, d.Size.X - bx);
        }
        if (runs.Count >= 2)
            Sparkline.Draw(d, new Rect2(bx, y + 58 * k, d.Size.X - bx - 4 * k, 40 * k), runs.TakeLast(12).Select(r => (float)r.Score).ToList(), k, UiTheme.Teal);
    }

    static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalMinutes < 60) return L.T("just now");
        if (d.TotalHours < 24) return L.F($"{(int)d.TotalHours} h ago");
        if (d.TotalDays < 2) return L.T("yesterday");
        return L.F($"{(int)d.TotalDays} days ago");
    }

    void SelectTier(int id)
    {
        if (Main.I.Tier == id) return;
        Main.I.Settings.Tier = id;
        Main.I.Settings.Save();
        for (int i = 0; i < tierButtons.Count; i++) { tierButtons[i].Selected = i == id; tierButtons[i].QueueRedraw(); }
        record.QueueRedraw();
        tierInfo.QueueRedraw();
        StartDemo(); // the demo plays the new tier's bots / targets
    }

    /// <summary>Coach skills this drill produces data for (the coach's own drill lists), or the category. Translated (the
    /// switch is on the English category key).</summary>
    static string Trains(TrainingMode m)
    {
        string key = m.Key.Split(':')[0];
        var skills = Enum.GetValues<Skill>().Where(s => CoachSkills.Drills(s).Contains(key)).Select(s => L.T(Coach.SkillName(s))).ToList();
        return skills.Count > 0 ? string.Join(", ", skills) : m.Category switch
        {
            "Agents" => L.T("Agent abilities + aim"),
            "Map" => L.T("Map play: angles, clearing, crosshair placement"),
            "Movement" => L.T("Movement: peeks and stops"),
            "Coach" => L.T("Your sens / crosshair"),
            _ => m.DisplayCategory,
        };
    }

    static string WeaponName(WeaponKind w) => GunPicker.GunName(w);
    static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    /// <summary>A wrapping Godot Label (not a sink: <paramref name="s"/> must already be translated).</summary>
    Label Text(string s, float px, Color c) => new()
    {
        Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        LabelSettings = UiTheme.Label(UiTheme.Body, px, k, c), MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(40 * k, 0),
    };

    // ---------------- actions ----------------

    void Start(bool free = false)
    {
        if (done) return;
        done = true;
        StopDemo(true);
        QueueFree();
        if (free && onFreePlay != null) onFreePlay(PickArg); else onStart(PickArg);
    }

    void Close()
    {
        if (done) return;
        done = true;
        UiTheme.ClickSound();
        StopDemo(true);
        QueueFree();
        onClose();
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (picker != null && IsInstanceValid(picker)) return; // the gun picker has the keys
        if (skinPicker != null && IsInstanceValid(skinPicker)) return; // so has the skin picker
        if (key.Keycode == Key.K && CanSkin) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); OpenSkins(); }
        else if (key.Keycode == Key.B && CanPick) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); OpenPicker(); }
        else if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Close(); }
        else if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); Start(); }
        else if (key.Keycode == Key.F && onFreePlay != null) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); Start(true); }
    }

    public override void _ExitTree() => StopDemo(true);

    public override void _Process(double delta)
    {
        // a looping demo of a random-map drill moves to another map: keep "Where" in step
        var m = demo != null && IsInstanceValid(demo) ? demo.Mode?.Map : null;
        if (m != null && m != shownMap) { shownMap = m; whereRow?.QueueRedraw(); }
        if (noDemoBox != null && GunIcons.Revision != iconRev) { iconRev = GunIcons.Revision; noDemoBox.QueueRedraw(); }
    }
    int iconRev = -1;
}


/// <summary>
/// Dev self-test <c>--preview-check</c> (works with --headless --fixed-fps 60): plays every menu drill's preview demo for a
/// few seconds each in a sub-viewport, then checks that the demo ran,
/// reports whether the simulated player fired, and that nothing reached the stats or the "last drill" of bug reports.
/// <c>--preview-gun Phantom</c> demos every drill that offers the Phantom with it (and checks the demo really holds it;
/// drills without it play their own gun, as in the game); <c>--preview-gun all</c> demos each drill once per gun class it
/// offers (the class's first gun it allows), so pellets, bursts and spin-up get a run too.
/// Prints "[preview-check] RESULT: PASS|FAIL" and quits with 0 / 1.
/// </summary>
public partial class PreviewCheck : Node
{
    /// <summary>5 s per drill at --fixed-fps 60 (dev: --preview-check-secs N; --preview-check-only key,key).</summary>
    static readonly int FramesPerDrill = (int.TryParse(CmdLine.DevAfter("--preview-check-secs"), out var sec) ? sec : 5) * 60;
    readonly List<(string Key, WeaponKind? Gun)> runs = new();
    int idx = -1, frames, fails, idle, runsBefore, withGun;
    string? lastKeyBefore;
    SubViewport? vp;
    GameSession? s;

    public override void _Ready()
    {
        // the menu's drills that open a preview with a demo (drills on a VALORANT map and the Agents screen's drills have none)
        var keys = ModeRegistry.All.Select(f => f()).Where(m => m.HasPreview && m.PreviewDemo && m.Category != "Agents").Select(m => m.Key).Distinct().ToList();
        string? only = CmdLine.DevAfter("--preview-check-only");
        if (!string.IsNullOrEmpty(only)) keys = only.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(); // any drill key (agent drills too)
        string? gunArg = CmdLine.DevAfter("--preview-gun");
        var gun = Weapons.Parse(gunArg);
        if (gunArg != null && gun == null && !gunArg.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print($"[preview-check] FAIL --preview-gun {gunArg}: not a gun (a name like Phantom, or all)");
            fails++;
        }
        foreach (var key in keys)
        {
            if (gunArg != null && gunArg.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                // one run per gun class the drill offers (its own gun for a locked drill)
                var choice = ModeRegistry.Find(key) is { } mk ? SafeMake(mk)?.WeaponChoice : null;
                if (choice is not { Count: > 0 }) { runs.Add((key, null)); continue; }
                foreach (var cls in Enum.GetValues<WeaponClass>())
                    if (Weapons.Buyable.FirstOrDefault(w => w.Class == cls && choice.Contains(w.Kind)) is { } first) runs.Add((key, first.Kind));
            }
            else runs.Add((key, gun));
        }
        runsBefore = Main.I.Stats.Runs.Count;
        lastKeyBefore = Main.I.LastModeKey;
        GD.Print($"[preview-check] {runs.Count} demos of {keys.Count} drills, {FramesPerDrill} frames each{(gunArg != null ? $", --preview-gun {gunArg}" : "")}");
        Next();
    }

    static TrainingMode? SafeMake(Func<TrainingMode> make)
    {
        try { return make(); } catch { return null; }
    }

    public override void _Process(double delta)
    {
        if (s == null) return;
        if (++frames < FramesPerDrill) return;
        var t = s.Telemetry;
        var (key, asked) = runs[idx];
        bool ran = t.Frames.Count > 0;
        if (!ran) fails++;
        if (t.Shots.Count == 0) idle++;
        // the asked gun must be the one the demo holds wherever the drill offers it
        var active = s.Mode.ActiveWeapon;
        bool offered = asked is { } a && s.Mode.WeaponChoice?.Contains(a) == true;
        bool gunOk = !offered || (active == asked && s.Weapon?.Kind == asked && t.Weapon == asked.ToString());
        if (!gunOk) fails++;
        if (offered && gunOk) withGun++;
        string gunText = asked == null ? "" : offered ? $" [{active}{(gunOk ? "" : $" FAIL: asked {asked}, holds {s.Weapon?.Kind}")}]"
            : active == asked ? $" [{active}, its own gun]" : $" [{active}: no {asked} here]";
        float first = t.Shots.Count > 0 ? t.Shots[0].T : -1;
        GD.Print($"[preview-check] {(ran && gunOk ? "ok  " : "FAIL")} {key}{gunText}: {t.Frames.Count} frames, {t.Shots.Count} shots" +
                 (first >= 0 ? $", first at {first:0.0} s" : " (no shots yet)") + $", {s.Mode.Kills} kills" + Details(t));
        Next();
    }

    /// <summary>What the drill's own events say about the demo: spray lengths, flash outcomes, scoped shots.</summary>
    string Details(RunTelemetry t)
    {
        var parts = new List<string>();
        var sprays = t.Events.Where(e => e.Kind == "spray_end").Select(e => (int)e.A).ToList();
        if (sprays.Count > 0) parts.Add($"sprays of {string.Join("/", sprays)} bullets (needs {(s!.Mode as SprayControlMode)?.Required})");
        var flashes = t.Events.Where(e => e.Kind == "flash").ToList();
        if (flashes.Count > 0) parts.Add($"flashes: {flashes.Count(e => e.A == 0)} dodged, {flashes.Count(e => e.A == 1)} partial, {flashes.Count(e => e.A == 2)} flashed");
        if (s!.Weapon is { Scope: true } && t.Shots.Count > 0)
        {
            // the frame before the shot: the Operator unscopes the moment it fires
            int scoped = t.Shots.Count(sh => t.Frames.LastOrDefault(f => f.T < sh.T) is { } f && (f.Flags & (byte)FrameFlags.Ads) != 0);
            parts.Add($"{scoped}/{t.Shots.Count} shots scoped");
        }
        return parts.Count > 0 ? " | " + string.Join("; ", parts) : "";
    }

    void Next()
    {
        if (s != null) { s.ProcessMode = ProcessModeEnum.Disabled; s.QueueFree(); s = null; }
        if (vp != null) { vp.QueueFree(); vp = null; }
        frames = 0;
        if (++idx >= runs.Count) { Done(); return; }
        var (key, gun) = runs[idx];
        if (ModeRegistry.Find(key) is not { } make) { fails++; GD.Print($"[preview-check] FAIL {key}: unknown drill"); Next(); return; }
        vp = new SubViewport { OwnWorld3D = true, HandleInputLocally = false, GuiDisableInput = true, Size = new Vector2I(640, 360) };
        AddChild(vp);
        s = new GameSession(make) { Demo = true, DurationOverride = 24f, WeaponPick = gun }; // as the drill preview starts it
        vp.AddChild(s);
    }

    void Done()
    {
        if (Main.I.Stats.Runs.Count != runsBefore) { fails++; GD.Print("[preview-check] FAIL a demo added a run to the stats"); }
        if (Main.I.LastModeKey != lastKeyBefore) { fails++; GD.Print("[preview-check] FAIL a demo changed the bug reports' last drill"); }
        string gunNote = runs.Any(r => r.Gun != null) ? $", {withGun} with the asked gun" : "";
        GD.Print($"[preview-check] RESULT: {(fails == 0 ? "PASS" : "FAIL")} ({runs.Count} demos, {fails} failed, {idle} without shots in {FramesPerDrill / 60} s{gunNote})");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
