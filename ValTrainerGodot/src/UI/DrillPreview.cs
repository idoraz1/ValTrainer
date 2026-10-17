using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// Popup shown when a drill card in the menu is clicked, instead of starting the drill right away (drills on a VALORANT map and the
/// Agents screen's drills still start directly). Left: a live
/// demo — the drill itself, played by the simulated player in a sub-viewport (<see cref="GameSession.Demo"/>: muted, no
/// input, nothing saved, loops). Right: what the drill is, how long it runs, the weapon and map, which coach skills it
/// feeds, your record at the chosen tier (best, badge, runs, recent scores) and the tier picker. START (Space / Enter) plays
/// it, FREE PLAY (F) plays it without a clock or score (drills the simulated player can play), BACK (Esc) closes. Drills that need a person (Reaction Test, Sens Finder) show a note instead of a demo.
/// <para>Dev: <c>--preview &lt;drill key&gt;</c> opens it over the menu (screenshots; <c>--preview-answer start|free|back</c> presses
/// that button after 3 s); <c>--preview-check</c> runs every
/// drill's demo headless for a few seconds and checks the simulated player plays it and nothing is saved.</para>
/// </summary>
public partial class DrillPreview : Control
{
    /// <summary>A demo loops after this long (shorter than most drills: the loop shows the start again).</summary>
    const float DemoSeconds = 24f;

    readonly float k;
    readonly Func<TrainingMode> make;
    readonly TrainingMode info;
    readonly string? title, sub;
    readonly Action onStart, onClose;
    readonly Action? onFreePlay;

    SubViewport? vp;
    GameSession? demo;
    Control? demoBox;
    DrawBox record = null!, tierInfo = null!;
    InfoRow? whereRow;
    Maps.MapSpot? shownMap;
    readonly List<VButton> tierButtons = new();
    bool done;

    /// <param name="title">Card title instead of the drill's name (Agents screen); <paramref name="sub"/>: its second line.</param>
    /// <param name="onStart">Starts the drill (after the popup and its demo are gone).</param>
    /// <param name="onFreePlay">Starts it in free play (no FREE PLAY button when null or the drill needs a person).</param>
    public DrillPreview(float k, Func<TrainingMode> make, TrainingMode info, Action onStart, Action onClose, string? title = null, string? sub = null,
        Action? onFreePlay = null)
    {
        this.onFreePlay = CanDemo(info) ? onFreePlay : null;
        this.k = k;
        this.make = make;
        this.info = info;
        this.onStart = onStart;
        this.onClose = onClose;
        this.title = title;
        this.sub = sub;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the screen underneath doesn't get clicks
    }

    /// <summary>Opens the preview over <paramref name="parent"/> (one at a time).</summary>
    public static void Open(Control parent, float k, Func<TrainingMode> make, Action start, string? title = null, string? sub = null, Action? freePlay = null)
    {
        if (parent.GetChildren().Any(c => c is DrillPreview)) return;
        TrainingMode info;
        try { info = make(); }
        catch (Exception e) { Log.Error($"[preview] can't build the drill: {e.Message}"); start(); return; }
        if (!info.HasPreview) { start(); return; } // drills on a VALORANT map go straight in
        parent.AddChild(new DrillPreview(k, make, info, start, () => { }, title, sub, freePlay));
    }

    /// <summary>Whether a drill can be demoed by the simulated player (2D drills and the Sens Finder need a person).</summary>
    public static bool CanDemo(TrainingMode m) => m.PreviewDemo;

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.78f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vs = GetViewportRect().Size;
        float w = Mathf.Min(1480 * k, vs.X - 70 * k);
        var panel = new VPanel
        {
            Title = (title ?? info.Name).ToUpperInvariant(), Caption = Caption(), K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.98f),
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

        if (sub != null) right.AddChild(Text(sub, 16, UiTheme.Warn));
        right.AddChild(Text(info.Description, 17, UiTheme.Text));
        if (info.MadeBy is { } by) right.AddChild(Text($"Made by the community: @{by}", 15, UiTheme.Teal));
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * k), MouseFilter = MouseFilterEnum.Ignore });

        int stripe = 0;
        void Row(string label, string value) =>
            right.AddChild(new InfoRow { K = k, H = 32, Label = label, Value = () => value, Stripe = stripe++ % 2 == 0 });
        Row("Length", info.Timed ? $"{info.Duration:0} s" : "Until you finish it");
        if (!info.Is2D && info.Weapon != WeaponKind.None) Row("Weapon", WeaponName(info.Weapon));
        // the map the demo is playing (a random map pick changes per run); RANDOM tag when the map picker is on random
        whereRow = new InfoRow
        {
            K = k, H = 32, Label = "Where", Stripe = stripe++ % 2 == 0,
            Value = () => (demo?.Mode?.Map ?? info.Map) is { } m ? $"{Cap(m.Map)} · {m.Name}" : info.Is2D ? "On screen" : "Shooting range",
            Tag = () => info.Map != null && Main.I.MapKey == "random" ? "RANDOM" : null,
        };
        right.AddChild(whereRow);
        Row("Trains", Trains(info));

        right.AddChild(new SectionLabel { Text = "YOUR RECORD", K = k, CustomMinimumSize = new Vector2(0, 34 * k) });
        record = new DrawBox { CustomMinimumSize = new Vector2(0, 112 * k), OnDraw = DrawRecord };
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
                Label = t.Name.ToUpperInvariant(), Kind = VButton.Look.Select, K = k, FontPx = 13, Selected = Main.I.Tier == id,
                CustomMinimumSize = new Vector2(0, 40 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = $"{t.Name} · {t.Ranks}",
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
                Gfx.Text(d, UiTheme.Body, $"{t.Name} · {t.Ranks}", 2 * k, Gfx.Mid(d.Size.Y / 2, fs), fs, UiTheme.TierColor(t.Id));
            },
        };
        right.AddChild(tierInfo);

        // ---- footer ----
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        col.AddChild(foot);
        var hint = Text(CanDemo(info) ? $"Preview: the built-in simulated player, muted. Space / Enter starts,{(onFreePlay != null ? " F free play," : "")} Esc goes back." : "Space / Enter starts, Esc goes back.", 14, UiTheme.Faint);
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foot.AddChild(hint);
        var back = new VButton { Label = "BACK", Kind = VButton.Look.Secondary, K = k, FontPx = 18, CustomMinimumSize = new Vector2(160 * k, 54 * k) };
        back.Pressed += Close;
        foot.AddChild(back);
        if (onFreePlay != null)
        {
            var free = new VButton { Label = "FREE PLAY", Kind = VButton.Look.Secondary, K = k, FontPx = 18, CustomMinimumSize = new Vector2(190 * k, 54 * k) };
            free.TooltipText = "Play it with no clock and no score, and nothing saved. It starts over when the time is up; Esc → menu to stop.";
            free.Pressed += () => Start(true);
            foot.AddChild(free);
        }
        var start = new VButton { Label = "START", Kind = VButton.Look.Primary, K = k, FontPx = 22, CustomMinimumSize = new Vector2(260 * k, 54 * k) };
        start.Pressed += () => Start();
        foot.AddChild(start);

        // Dev: "--preview-answer start|back" presses that button after 3 s (flow tests)
        if (CmdLine.DevAfter("--preview-answer")?.ToLowerInvariant() is { } ans)
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || done) return;
                Log.Info($"[preview] --preview-answer {ans}");
                if (ans == "start") Start(); else if (ans == "free" && onFreePlay != null) Start(true); else Close();
            };
    }

    string Caption()
    {
        string cat = info.Category.ToUpperInvariant();
        return info.MadeBy != null ? $"{cat} · COMMUNITY" : cat;
    }

    // ---------------- demo ----------------

    void BuildDemo(Control box, float w, float h)
    {
        if (!CanDemo(info))
        {
            box.AddChild(new DrawBox
            {
                Size = new Vector2(w, h),
                OnDraw = d =>
                {
                    var r = new Rect2(Vector2.Zero, d.Size);
                    Gfx.VGradient(d, r, Color.Color8(22, 34, 46), Color.Color8(12, 20, 28));
                    Gfx.Brackets(d, r.Grow(-1), 18 * k, new Color(UiTheme.Text, 0.3f), Mathf.Max(1, 2 * k));
                    int fs = UiTheme.Fs(22, k), bs = UiTheme.Fs(16, k);
                    var c = r.GetCenter();
                    Gfx.TextC(d, UiTheme.DisplayWide, "NO PREVIEW FOR THIS ONE", c.X, Gfx.Mid(c.Y - 18 * k, fs), fs, UiTheme.Text);
                    Gfx.TextC(d, UiTheme.Body, "It runs on your own picks and reactions. Press START to try it.", c.X, Gfx.Mid(c.Y + 20 * k, bs), bs, UiTheme.Dim);
                },
            });
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
                string tag = "PREVIEW · SIMULATED PLAYER";
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
            demo = new GameSession(make) { Demo = true, DurationOverride = info.Timed ? Mathf.Min(info.Duration, DemoSeconds) : null };
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
        var runs = Main.I.Stats.For(info.Key).Where(r => r.Tier == tier && !r.Warmup).OrderBy(r => r.When).ToList();
        int best = runs.Count > 0 ? runs.Max(r => r.Score) : -1;
        int badge = Main.I.Stats.BestBadge(info.Key);
        float y = 4 * k;
        int ls = UiTheme.Fs(13, k), vs = UiTheme.Fs(30, k), ss = UiTheme.Fs(14, k);
        var inv = CultureInfo.InvariantCulture;
        Gfx.Text(d, UiTheme.HudWide, $"BEST · {Difficulty.Get(tier).Name.ToUpperInvariant()}", 2 * k, y + ls, ls, UiTheme.Dim);
        if (best >= 0)
        {
            Gfx.Text(d, UiTheme.Display, best.ToString("#,0", inv), 2 * k, y + ls + 8 * k + vs * 0.8f, vs, UiTheme.Good);
            string meta = $"{runs.Count} run{(runs.Count == 1 ? "" : "s")} at this tier · last {Ago(runs[^1].When)}";
            Gfx.TextFit(d, UiTheme.Body, meta, 2 * k, y + ls + vs + 22 * k, ss, UiTheme.Dim, d.Size.X * 0.55f);
        }
        else
        {
            Gfx.Text(d, UiTheme.Display, "—", 2 * k, y + ls + 8 * k + vs * 0.8f, vs, UiTheme.Faint);
            Gfx.TextFit(d, UiTheme.Body, "Not played at this tier yet", 2 * k, y + ls + vs + 22 * k, ss, UiTheme.Dim, d.Size.X * 0.55f);
        }
        // best badge ever (any tier) + the last scores at this tier
        float bx = d.Size.X * 0.6f;
        if (badge >= -1 && Main.I.Stats.For(info.Key).Any())
        {
            RankEmblem.Draw(d, new Vector2(bx + 16 * k, y + 26 * k), 16 * k, badge, badge >= 0 ? 1f : 0.55f);
            int ts = UiTheme.Fs(10, k);
            Gfx.Text(d, UiTheme.HudWide, "BEST BADGE", bx + 40 * k, Gfx.Mid(y + 8 * k, ts), ts, UiTheme.Faint);
            var bt = badge >= 0 ? Difficulty.Get(badge) : null;
            Gfx.TextFit(d, UiTheme.Body, bt?.Name ?? "Below Rookie", bx + 40 * k, Gfx.Mid(y + 25 * k, ss), ss, bt != null ? UiTheme.TierColor(bt.Id) : UiTheme.Dim, d.Size.X - bx - 40 * k);
            if (bt != null) Gfx.TextFit(d, UiTheme.Body, bt.Ranks, bx + 40 * k, Gfx.Mid(y + 42 * k, ts + 2), ts + 2, UiTheme.Dim, d.Size.X - bx - 40 * k);
        }
        if (runs.Count >= 2)
            Sparkline.Draw(d, new Rect2(bx, y + 58 * k, d.Size.X - bx - 4 * k, 40 * k), runs.TakeLast(12).Select(r => (float)r.Score).ToList(), k, UiTheme.Teal);
    }

    static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalMinutes < 60) return "just now";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 2) return "yesterday";
        return $"{(int)d.TotalDays} days ago";
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

    /// <summary>Coach skills this drill produces data for (the coach's own drill lists), or the category.</summary>
    static string Trains(TrainingMode m)
    {
        string key = m.Key.Split(':')[0];
        var skills = Enum.GetValues<Skill>().Where(s => CoachSkills.Drills(s).Contains(key)).Select(Coach.SkillName).ToList();
        return skills.Count > 0 ? string.Join(", ", skills) : m.Category switch
        {
            "Agents" => "Agent abilities + aim",
            "Map" => "Map play: angles, clearing, crosshair placement",
            "Movement" => "Movement: peeks and stops",
            "Coach" => "Your sens / crosshair",
            _ => m.Category,
        };
    }

    static string WeaponName(WeaponKind w) => w switch { WeaponKind.TourDeForce => "Tour de Force", _ => w.ToString() };
    static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

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
        if (free && onFreePlay != null) onFreePlay(); else onStart();
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
        if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Close(); }
        else if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); Start(); }
        else if (key.Keycode == Key.F && onFreePlay != null) { GetViewport().SetInputAsHandled(); UiTheme.ClickSound(); Start(true); }
    }

    public override void _ExitTree() => StopDemo(true);

    public override void _Process(double delta)
    {
        // a looping demo of a random-map drill moves to another map: keep "Where" in step
        var m = demo != null && IsInstanceValid(demo) ? demo.Mode?.Map : null;
        if (m != null && m != shownMap) { shownMap = m; whereRow?.QueueRedraw(); }
    }
}

/// <summary>
/// Dev self-test <c>--preview-check</c> (works with --headless --fixed-fps 60): plays every menu drill's preview demo for a
/// few seconds each in a sub-viewport, then checks that the demo ran,
/// reports whether the simulated player fired, and that nothing reached the stats or the "last drill" of bug reports.
/// Prints "[preview-check] RESULT: PASS|FAIL" and quits with 0 / 1.
/// </summary>
public partial class PreviewCheck : Node
{
    /// <summary>5 s per drill at --fixed-fps 60 (dev: --preview-check-secs N; --preview-check-only key,key).</summary>
    static readonly int FramesPerDrill = (int.TryParse(CmdLine.DevAfter("--preview-check-secs"), out var sec) ? sec : 5) * 60;
    List<string> keys = new();
    int idx = -1, frames, fails, idle, runsBefore;
    string lastKeyBefore = "";
    SubViewport? vp;
    GameSession? s;

    public override void _Ready()
    {
        // the menu's drills that open a preview with a demo (drills on a VALORANT map and the Agents screen's drills have none)
        keys = ModeRegistry.All.Select(f => f()).Where(m => m.HasPreview && m.PreviewDemo && m.Category != "Agents").Select(m => m.Key).Distinct().ToList();
        if (CmdLine.DevAfter("--preview-check-only")?.Split(',') is { } only) keys = only.ToList(); // any drill key (agent drills too)
        runsBefore = Main.I.Stats.Runs.Count;
        lastKeyBefore = Main.I.LastModeKey;
        GD.Print($"[preview-check] {keys.Count} drills, {FramesPerDrill} frames each");
        Next();
    }

    public override void _Process(double delta)
    {
        if (s == null) return;
        if (++frames < FramesPerDrill) return;
        var t = s.Telemetry;
        bool ran = t.Frames.Count > 0;
        if (!ran) fails++;
        if (t.Shots.Count == 0) idle++;
        float first = t.Shots.Count > 0 ? t.Shots[0].T : -1;
        GD.Print($"[preview-check] {(ran ? "ok  " : "FAIL")} {keys[idx]}: {t.Frames.Count} frames, {t.Shots.Count} shots" +
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
        if (s!.Weapon is { Sniper: true } && t.Shots.Count > 0)
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
        if (++idx >= keys.Count) { Done(); return; }
        if (ModeRegistry.Find(keys[idx]) is not { } make) { fails++; GD.Print($"[preview-check] FAIL {keys[idx]}: unknown drill"); Next(); return; }
        vp = new SubViewport { OwnWorld3D = true, HandleInputLocally = false, GuiDisableInput = true, Size = new Vector2I(640, 360) };
        AddChild(vp);
        s = new GameSession(make) { Demo = true, DurationOverride = 24f };
        vp.AddChild(s);
    }

    void Done()
    {
        if (Main.I.Stats.Runs.Count != runsBefore) { fails++; GD.Print("[preview-check] FAIL a demo added a run to the stats"); }
        if (Main.I.LastModeKey != lastKeyBefore) { fails++; GD.Print("[preview-check] FAIL a demo changed the bug reports' last drill"); }
        GD.Print($"[preview-check] RESULT: {(fails == 0 ? "PASS" : "FAIL")} ({keys.Count} drills, {fails} failed, {idle} without shots in {FramesPerDrill / 60} s)");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
