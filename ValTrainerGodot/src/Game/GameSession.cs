using Godot;
using ValTrainer.Analysis;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Game.Replay;
using ValTrainer.Game.Weapon;
using ValTrainer.Modes;
using ValTrainer.UI;
using ValTrainer.Valorant;
using ValTrainer.World;

namespace ValTrainer.Game;

/// <summary>
/// One play session: builds the environment, camera, weapon and HUD; runs the countdown → running →
/// results state machine; applies Valorant mouse math, movement, weapon and the mode's logic.
/// </summary>
public partial class GameSession : Node3D, IGame
{
    /// <summary>Buying: the in-drill buy menu is open (frozen like Paused, without the pause menu; BuySession.cs).</summary>
    public enum St { Countdown, Running, Paused, Results, Buying }

    readonly Func<TrainingMode> factory;
    public TrainingMode Mode { get; private set; } = null!;
    public St State { get; private set; }
    St resumeTo;

    // IGame
    public PlayerView View { get; } = new();
    public Mover Mover { get; private set; } = new();
    public PlayerState Player { get; } = new();
    public int Tier => Math.Clamp(TierOverride ?? Main.I.Tier, 0, 4);
    public Color Enemy => Main.I.EnemyColor;
    public Random Rng { get; } = new();
    public float Now { get; private set; }
    public Node3D World { get; private set; } = null!;
    public IReadOnlyList<Box> Solid => solid;
    public WeaponDef? Weapon => weaponDef;
    public BlindOverlay Blind { get; private set; } = null!;

    List<Box> solid = new();
    WeaponDef? weaponDef;
    public WeaponController? Gun { get; private set; }
    Viewmodel? viewmodel;
    Camera3D cam = null!;
    Node3D envRoot = null!;
    public float Countdown { get; private set; }
    float feetY; // player body feet height; jumping / falling state lives in Mover (MovementBody.cs)
    bool firePressedEdge, fireHeld, fireTap, adsHeld, wasReloading;
    /// <summary>This frame's alt-fire press for a gun whose right click shoots (Classic blast, Bucky air burst; AdsSession.cs).</summary>
    bool altFireEdge;
    public RunRecord? Record { get; private set; }
    /// <summary>Aim-coach review of the run that just finished (results screen).</summary>
    public RunReview? Review { get; private set; }
    /// <summary>Everything recorded during the current run (frames, shots, events).</summary>
    public RunTelemetry Telemetry { get; private set; } = new();
    float frameDx, frameDy;
    int lastStopCount;
    static readonly string? TelemetryOut = ArgAfter("--telemetry-out");

    static string? ArgAfter(string flag)
    {
        var a = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        int i = Array.IndexOf(a, flag);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    public int PrevBest { get; private set; }
    public float AdsBlend;

    Hud hud = null!;
    CrosshairView crosshair = null!;
    public string BannerText = "";
    public float BannerTime;
    public Color BannerColor;

    public GameSession(Func<TrainingMode> factory) => this.factory = factory;

    /// <summary>Drill preview (DrillPreview): the simulated player plays the drill in a sub-viewport. No input, no mouse
    /// capture, no sound, no stats / telemetry / Rewind, and the run loops instead of showing results. Set before the
    /// session enters the tree.</summary>
    public bool Demo { get; init; }
    /// <summary>Free play (the drill preview's FREE PLAY): no clock and no score on the HUD, nothing saved (stats, telemetry,
    /// Rewind), and when the drill's time is up it starts over at once (no countdown, no results). Esc → pause → menu ends it.</summary>
    public bool FreePlay { get; init; }
    /// <summary>The gun picked for this session (drill preview, START / FREE PLAY after a pick); null = resolve it at every
    /// Restart (per-drill pick → preferred gun → the drill's default, <see cref="WeaponPicks.Resolve"/>). Only a gun in the
    /// drill's <see cref="TrainingMode.WeaponChoice"/> takes effect (<see cref="TrainingMode.ActiveWeapon"/>). Settable: a
    /// new pick applies at the next <see cref="Restart"/>.</summary>
    public WeaponKind? WeaponPick { get; set; }
    /// <summary>Dev-only "--gun Name": every session except the drill preview plays that gun, even in drills that are locked
    /// to their own (tests of a gun before any drill offers it). Drills without a gun (Strafe Tracking, Reaction Test, Sens
    /// Finder) stay gunless.</summary>
    static readonly WeaponKind? DevGun = Weapons.Parse(CmdLine.DevAfter("--gun"));
    /// <summary>Free play: the next Restart is the automatic one at the end of the drill (skips the countdown).</summary>
    bool freePlayLoop;
    /// <summary>The preview's simulated player: the coach's "good" player with the steady settings of the promo clips (less
    /// tremor and jitter). In the peek drills it also holds its angle instead of pre-aiming at bots hidden behind cover;
    /// elsewhere (deathmatch, map and agent drills) it has to turn to find them.</summary>
    string DemoProfile => "good:tremor=0.4,jitter=2" + (DemoHoldDrills.Contains(Mode.Key) ? ",hold=1" : "")
        // spray drills: hold each spray past the tier's minimum, so it counts and shows the hit banner
        + (Mode is SprayControlMode spray && weaponDef is { Rps: > 0 } wd
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $",burst={(spray.Required + 2) / wd.MaxRps:0.00}") : "");
    static readonly HashSet<string> DemoHoldDrills = new() { "peek", "peek_mix", "peekduel", "operator", "jumppeek", "jigglepeek" };

    /// <summary>Counter-Strafe preview: strafe A / D, and when the bot shows, tap the opposite key (the counter-strafe the
    /// drill teaches) so the simulated player shoots standing still.</summary>
    bool DemoStrafeKey(InputBinding k)
    {
        var v = Main.I.Valorant;
        if (Mode.Focus == null)
        {
            demoSeenAt = -1;
            demoRight = (int)(Now / 0.9f) % 2 == 0;
            return k == (demoRight ? v.KeyRight : v.KeyLeft);
        }
        if (demoSeenAt < 0) demoSeenAt = Now;
        return Now - demoSeenAt < 0.07f && k == (demoRight ? v.KeyLeft : v.KeyRight);
    }
    float demoSeenAt = -1;
    bool demoRight;

    /// <summary>Site Clear preview: walk the bot navigation path from the spawn through the choke towards the site (keys
    /// steered relative to where the simulated player looks), and stand still to shoot while a defender is in sight.</summary>
    bool DemoWalkKey(InputBinding k)
    {
        if (Mode.Map is not { } spot || Player.Dead || Mode.Focus != null) return false;
        var feet = new Vector3(View.Eye.X, feetY, View.Eye.Z);
        if (demoPath == null)
        {
            demoPathTask ??= Task.Run(() =>
            {
                var nav = BotNav.For($"{spot.Key}:{solid.Count}", solid, spot.StartFeet);
                var site = spot.Enemies.Length > 0 ? spot.Enemies.Aggregate(Vector3.Zero, (a, e) => a + e.Feet) / spot.Enemies.Length : spot.Choke;
                var path = nav.FindPath(spot.StartFeet, spot.Choke) ?? new List<Vector3> { spot.Choke };
                path.AddRange(nav.FindPath(spot.Choke, site) ?? new List<Vector3> { site });
                return path;
            });
            if (!demoPathTask.IsCompleted) return false;
            demoPath = demoPathTask.Result;
            demoIdx = 0;
        }
        while (demoIdx < demoPath.Count - 1 && Flat(demoPath[demoIdx] - feet) < 0.7f) demoIdx++;
        var to = demoPath[demoIdx] - feet;
        if (Flat(to) < 0.6f) return false; // arrived: hold the site
        float rel = Mathf.DegToRad(Mathf.Wrap(Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z)) - View.Yaw, -180f, 180f));
        float fwd = Mathf.Cos(rel), side = Mathf.Sin(rel);
        var v = Main.I.Valorant;
        return (k == v.KeyForward && fwd > 0.38f) || (k == v.KeyBack && fwd < -0.38f)
            || (k == v.KeyRight && side > 0.38f) || (k == v.KeyLeft && side < -0.38f);
    }
    static float Flat(Vector3 d) => new Vector2(d.X, d.Z).Length();

    /// <summary>Dev (map fixes): how wide the clear opening around a kill line is, probing parallel lines every 5 cm to
    /// the sides and up / down. A real angle is open well beyond a head's width; a hairline gap between boxes is not.</summary>
    string DevOpening(Vector3 a, Vector3 b)
    {
        var dir = (b - a).Normalized();
        var side = dir.Cross(Vector3.Up).Normalized();
        var up = side.Cross(dir).Normalized();
        float Span(Vector3 axis)
        {
            float lo = 0, hi = 0;
            for (float o = 0.05f; o <= 1.0f; o += 0.05f) { if (Collision.Blocked(a + axis * o, b + axis * o, solid)) break; hi = o; }
            for (float o = 0.05f; o <= 1.0f; o += 0.05f) { if (Collision.Blocked(a - axis * o, b - axis * o, solid)) break; lo = o; }
            return lo + hi;
        }
        float w = Span(side), h = Span(up);
        string flag = w < 0.3f || h < 0.3f ? "  <<< NARROW GAP" : "";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"| opening {w:0.00} m wide, {h:0.00} m tall{flag}");
    }

    /// <summary>Flash Dodge preview: on the flash cue, react, turn the pop well off-screen, wait for it to pop, turn back.
    /// About one flash in five is left on purpose, so both outcomes show. True while it owns the view (no aiming).</summary>
    bool DemoFlashTurn(FlashDodgeMode fd)
    {
        if (fd.FlashIncoming is { } pop)
        {
            if (demoFlashAt < 0)
            {
                demoFlashAt = Now;
                demoDodge = Rng.NextDouble() < 0.8;
                demoTurnFrom = View.Yaw;
                var d = pop - View.Eye;
                float popYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
                float off = Mathf.Wrap(popYaw - View.Yaw, -180f, 180f);
                demoTurnBy = (off >= 0 ? -1 : 1) * (115f + (float)Rng.NextDouble() * 25f); // away from the flash
                demoBackAt = -1;
            }
        }
        else if (demoFlashAt >= 0 && demoBackAt < 0) demoBackAt = Now + 0.15f; // popped: turn back shortly after
        if (demoFlashAt < 0) return false;
        if (!demoDodge)
        {
            if (demoBackAt >= 0 && Now > demoBackAt + 0.4f) demoFlashAt = -1; // this one was taken on purpose
            return false;
        }
        if (demoBackAt >= 0 && Now >= demoBackAt + 0.28f) { demoFlashAt = -1; return false; } // back on the angle: the sim aims again
        float t = Now - demoFlashAt - 0.24f; // reaction to the audio cue
        float away = demoBackAt < 0 || Now < demoBackAt ? Mathf.Clamp(t / 0.16f, 0, 1) : 1 - Mathf.Clamp((Now - demoBackAt) / 0.28f, 0, 1);
        float e = 1 - (1 - away) * (1 - away);
        View.Yaw = demoTurnFrom + demoTurnBy * e;
        UpdateCamera();
        return true;
    }
    float demoFlashAt = -1, demoBackAt = -1, demoTurnFrom, demoTurnBy;
    bool demoMagEmpty;
    bool demoDodge;
    List<Vector3>? demoPath;
    Task<List<Vector3>>? demoPathTask;
    int demoIdx;

    static GameSession? active;

    public override void _Ready()
    {
        if (!Demo) active = this; // a preview never owns the shared session state (3D toggle, time scale)
        // HUD layer (crosshair under the blind overlay, HUD on top)
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        hudLayer = layer; // the buy menu opens on it, over the HUD
        adsSight = new AdsSight(); // the gun's hologram sight outline, under the crosshair (AdsSession.cs)
        layer.AddChild(adsSight);
        crosshair = new CrosshairView { Profile = Main.I.Valorant.Crosshair };
        layer.AddChild(crosshair);
        Blind = new BlindOverlay();
        layer.AddChild(Blind);
        hud = new Hud(this);
        layer.AddChild(hud);

        if (Main.I.Dev && OnFinished == null && !Demo) DevRoutineArgs();
        Restart();
    }

    /// <summary>
    /// Dev-only Lock-In hooks for single drills (a routine sets these itself): "--tier-override N" plays at tier N,
    /// "--adaptive [start level]" runs the adaptive staircase (start = the session tier), "--ease S" eases the bots for
    /// the last S seconds.
    /// </summary>
    void DevRoutineArgs()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var num = System.Globalization.NumberStyles.Float;
        if (int.TryParse(ArgAfter("--tier-override"), out var to)) TierOverride = Math.Clamp(to, 0, 4);
        if (OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--adaptive"))
            Adaptive = new Staircase(float.TryParse(ArgAfter("--adaptive"), num, inv, out var sl) ? sl : Tier);
        if (float.TryParse(ArgAfter("--ease"), num, inv, out var ease)) EaseLastSeconds = ease;
        if (TierOverride != null || Adaptive != null || EaseLastSeconds > 0)
            Log.Info($"[dev] session tier {Tier}{(Adaptive != null ? $", adaptive from {Adaptive.Level:0.0}" : "")}{(EaseLastSeconds > 0 ? $", ease last {EaseLastSeconds:0} s" : "")}");
    }

    public void Restart()
    {
        envRoot?.QueueFree();
        World?.QueueFree();
        viewmodel?.QueueFree();
        cam?.QueueFree();

        Mode = factory();
        nextLifeGun = null; gunHeldFor.Clear(); // a restart starts with the picked gun (BuySession.cs)
        Mode.Pick = WeaponPick ?? WeaponPicks.Resolve(Mode); // before Attach: the drill reads ActiveWeapon from Setup on
        if (!Demo && DevGun is { } devGun) { Mode.Pick = devGun; Mode.ForcePick = true; }
        if (!Demo) Main.I.LastModeKey = Mode.Key; // bug reports say which drill ran last
        Mode.Attach(this); // ExtraSolids / Map below already follow the session tier
        if (Adaptive is { Trials: > 0 } stair) Adaptive = stair.Fresh(); // a restarted run adapts from the start level again
        TrialSens = SessionSens; // null unless a routine (warm-up sens shifter) set one
        TrialCrosshair = null;
        ForcedScope = null;
        Mover = new Mover();
        WireMover(); // movement events / dev scripts for the new run (MovementSession.cs)
        Player.Reset();
        Now = 0;
        smokes.Clear();
        Record = null;
        Review = null;
        frameDx = frameDy = 0;
        lastStopCount = 0;
        viewmodel = null;
        ResetTransientInput();
        wasReloading = false;
        BannerTime = 0;
        AdsBlend = 0;
        abilityInHand = false; // agent drills: the gun is in hand (AbilitySession.cs)
        View.Zoom = View.VisualZoom = 1;
        View.RecoilPitch = View.RecoilYaw = 0;

        // The range's extra boxes are listed once: the Rewind records exactly what was built (some depend on the tier).
        var extras = Mode.Map == null ? Mode.ExtraSolids.ToList() : new List<Box>();
        var built = Mode.Map != null
            ? Environments.BuildMap(Mode.Map, Main.I.Settings.Quality)
            : Environments.BuildRange(extras, Main.I.Settings.Quality);
        envRoot = built.Root;
        AddChild(envRoot);
        solid = built.Solid;

        World = new Node3D { Name = "ModeWorld" };
        AddChild(World);
        Effects.Root = World;
        Effects.Prewarm();
        if (Mode is BotMode) BotCharacter.Prewarm(World, Enemy, Main.I.Settings.Outlines, Mode.StartFeet + new Vector3(0, -3f, -4f));

        cam = new Camera3D { Fov = PlayerView.HipHFov, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.03f, Far = 400f, Current = true };
        AddChild(cam);

        Equip(Weapons.Get(Mode.ActiveWeapon));
        string? askedGun = CmdLine.DevAfter("--gun");
        if (Main.I.Dev && !Demo && (Mode.ActiveWeapon != Mode.Weapon || askedGun != null))
            Log.Info($"[gun] {Mode.Key}: {Mode.ActiveWeapon} active (drill default {Mode.Weapon}{(Mode.ForcePick ? ", dev --gun" : "")})"
                + (askedGun != null && DevGun == null ? $"; --gun {askedGun} is not a gun" : ""));

        GetViewport().Disable3D = Mode.Is2D;
        Telemetry = new RunTelemetry
        {
            Mode = Mode.Key, Tier = Tier, Sens = SessionSens ?? Main.I.Sens, Dpi = Main.I.Settings.Dpi,
            Weapon = Mode.ActiveWeapon.ToString(), Map = Mode.Map?.Key ?? "range", When = DateTime.Now,
            ZoomSensMult = ZoomSensMultFor(Weapons.Get(Mode.ActiveWeapon)),
        };
        replay = Demo || FreePlay ? null : StartReplay(extras); // before Begin: it spawns the first bots / targets
        Mode.Begin(this);
        if (Main.I.Dev && !Demo) DevAdsArgs(); // --force-ads (AdsSession.cs)
        if (Demo && Mode.Key == "counterstrafe") Mover.KeyOverride = DemoStrafeKey;
        if (Demo && Mode.Key == "siteclear") { demoPath = null; demoPathTask = null; Mover.KeyOverride = DemoWalkKey; }
        Respawn(Mode.StartFeet, Mode.StartYaw);
        Blind.Reset();
        bool straightIn = Demo || freePlayLoop; // the preview and free play's own loop get straight to it (no countdown splash)
        freePlayLoop = false;
        Countdown = straightIn ? 0f : 3f;
        State = straightIn ? St.Running : St.Countdown;
        LockMouse();
        UpdateCamera();
    }

    // ---------------- input ----------------

    public void LockMouse()
    {
        // Captured mode reads raw device counts on Windows; there is no warp spike to skip.
        if (!Main.I.Dev && !Demo) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void ResetTransientInput() { fireHeld = firePressedEdge = fireTap = adsHeld = altFireEdge = scope2 = false; Keybinds.EndFrame(); }

    /// <summary>Freeze bots, effects and timers while paused / on the results screen.</summary>
    void SetWorldRunning(bool on)
    {
        if (World != null && IsInstanceValid(World)) World.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public override void _Input(InputEvent e)
    {
        if (Demo) return; // the preview popup owns the keyboard and mouse
        if (State == St.Buying) return; // the buy menu (GunPicker) owns them: its Esc / bind closes it, never pauses
        Keybinds.Observe(e); // wheel notches count as "held" for one frame (wheel-bound jump / crouch / abilities)
        if (e is InputEventMouseMotion m)
        {
            if (State is St.Countdown or St.Running && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                // Raw counts (Godot uses Windows raw input while captured). ScreenRelative ignores stretch scaling.
                View.Look(m.ScreenRelative, CurrentSens, ZoomSensMultFor(weaponDef));
                frameDx += m.ScreenRelative.X;
                frameDy += m.ScreenRelative.Y;
                UpdateCamera();
            }
            return;
        }
        // Esc always pauses / resumes / leaves the results (never a VALORANT bind); R restarts from the results screen.
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.Escape)
            {
                if (BuyJustClosed) return; // this Esc closed the buy menu
                if (State is St.Countdown or St.Running) Pause();
                else if (State == St.Paused) Resume();
                else if (State == St.Results) Main.I.ShowMenu();
                return;
            }
            if (k.Keycode == Key.R && State == St.Results) { Restart(); return; }
        }
        // VALORANT's buy-menu bind (B by default): the gun picker over the drill, also from the pause menu (BuySession.cs)
        if (State is St.Countdown or St.Running or St.Paused && !BuyJustClosed && Main.I.Valorant.Binds.Pressed(e, GameAction.BuyMenu)
            && !(State == St.Paused && GetViewport().GuiGetFocusOwner() is LineEdit)) // typing a sensitivity on the pause menu
        {
            OpenBuyMenu();
            return;
        }
        OnBindEvent(e, Main.I.Valorant.Binds);
    }

    /// <summary>
    /// The player's VALORANT binds for fire, alt fire (ADS / scope), reload and jump: keys, mouse buttons or wheel notches,
    /// both bind slots. Each action checks the event on its own, so one key bound to several actions triggers all of them.
    /// </summary>
    void OnBindEvent(InputEvent e, Keybinds binds)
    {
        bool wheel = e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight };
        int fire = binds.Match(e, GameAction.Fire);
        if (fire > 0)
        {
            if (!fireHeld) firePressedEdge = true;
            if (wheel) fireTap = true; else fireHeld = true; // a wheel notch is a one-frame trigger pull
        }
        else if (fire < 0) fireHeld = binds.IsDown(GameAction.Fire, real: true); // the other bind may still be held

        int ads = binds.Match(e, GameAction.AltFire);
        if (ads != 0) OnAdsButton(ads > 0, wheel, binds);

        if (binds.Pressed(e, GameAction.Reload) && State == St.Running && Gun != null && !Mode.InfiniteAmmo)
            Gun.StartReload(Mode.InfiniteReserve);
        // Wheel notches have no hold: queue the jump (key / mouse-button jumps are polled by the Mover).
        if (wheel && State == St.Running && binds.Pressed(e, GameAction.Jump)) Mover.QueueJump();
    }

    public void Pause()
    {
        if (State == St.Buying)
        {
            DismissBuyMenu(); // back to the state it was opened from
            if (State is not (St.Countdown or St.Running)) return; // opened from the pause menu: still paused
        }
        ResetTransientInput(); // releases can be missed while paused / unfocused
        SetWorldRunning(false);
        resumeTo = State;
        State = St.Paused;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _ExitTree()
    {
        // A results-screen "Practise" button starts the next session before this one is freed: leave its state alone.
        if (active != this) return;
        active = null;
        if (GetViewport() is { } vp) vp.Disable3D = false;
        Engine.TimeScale = 1; // dev --sftimescale, if the drill was left mid-run
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && State is St.Countdown or St.Running && !Main.I.Dev && !Demo) Pause();
    }

    public void Resume()
    {
        State = resumeTo;
        ResetTransientInput(); // a stale click must never fire on resume
        SetWorldRunning(true);
        LockMouse();
    }

    // ---------------- frame ----------------

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);
        if (BannerTime > 0) BannerTime -= dt;

        switch (State)
        {
            case St.Countdown:
                int before = (int)Mathf.Ceil(Countdown);
                Countdown -= dt;
                if ((int)Mathf.Ceil(Countdown) != before) Play2D(Countdown <= 0 ? "go" : "tick");
                if (Countdown <= 0) State = St.Running;
                firePressedEdge = fireTap = false;
                frameDx = frameDy = 0; // looking around during the countdown isn't part of the first recorded frame
                break;
            case St.Running:
                RunFrame(dt);
                break;
        }
        if (Main.I.Dev && !Demo) DevKeybindTick(); // --keybind-test (KeybindTest.cs)
        if (Main.I.Dev && !Demo) DevBuyTick((float)delta); // --buy-test (BuySession.cs)

        UpdateCamera();
        UpdateAdsVisuals(); // crosshair mode, scope, sight outline, viewmodel aim pose (AdsSession.cs)
        crosshair.Profile = TrialCrosshair ?? ValTrainer.Valorant.CrosshairCode.Effective;
        crosshair.MoveError = Mode.Movement ? Mover.MoveError : 0f;
        // Firing error beyond the first shot's: the aimed first-shot error while aimed (CurrentError already subtracts it).
        crosshair.FiringErrorPx = Gun != null && weaponDef != null
            ? DegToPx(Mathf.Max(0, Gun.SpreadNow - (Gun.Scoped ? weaponDef.FirstShotAds : weaponDef.FirstShotHip))) : 0;
        // VALORANT rests firing-error lines at the weapon's minimum error unless the profile overrides it with its offset ("m").
        crosshair.MinErrorPx = Gun != null && weaponDef != null ? DegToPx(Gun.Scoped ? weaponDef.FirstShotAds : weaponDef.FirstShotHip) : 0;
        if (viewmodel != null)
        {
            viewmodel.SetReload(Gun?.ReloadProgress ?? -1f); // partial / shell-by-shell reloads run their own length
            viewmodel.SetMotion(Mover.Speed, Mover.Crouching);
        }
        Keybinds.EndFrame(); // wheel notches seen before this frame were polled by it
    }

    // Dev-only (--dev --autofire): hold the trigger 2.6 s, release 1.2 s, repeat — for scripted tests of sprays.
    static readonly bool DevAutoFire = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--autofire");
    static readonly string? SimProfile = ArgAfter("--simaim");
    /// <summary>Dev-only "--duration N" shortens timed runs for automated tests.</summary>
    float RunDuration => DurationOverride ?? (Main.I.Dev && float.TryParse(ArgAfter("--duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : Mode.Duration);
    ValTrainer.Analysis.SimAim? Sim => Demo ? sim ??= new ValTrainer.Analysis.SimAim(DemoProfile, Mode.Key, Mode.SimKind, weaponDef) { UseAlt = true }
        : Main.I.Dev && SimProfile != null ? sim ??= new ValTrainer.Analysis.SimAim(SimProfile, Mode.Key, Mode.SimKind, weaponDef) { UseAlt = true } : null;
    bool simAltWas, spinWas;
    float spinHeldFor;
    ValTrainer.Analysis.SimAim? sim;

    /// <summary>Dev-only "--pause-at N": opens the pause menu N seconds into the run (layout screenshots).</summary>
    static readonly float? DevPauseAt = float.TryParse(ArgAfter("--pause-at"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pa) ? pa : null;
    bool devPaused;

    void RunFrame(float dt)
    {
        Now += dt;
        TrackGunTime(dt);
        if (Main.I.Dev && !Demo && DevPauseAt is { } pauseAt && Now >= pauseAt && !devPaused) { devPaused = true; Pause(); return; }
        if (Demo && Mode is FlashDodgeMode flashDrill && DemoFlashTurn(flashDrill)) { fireHeld = false; }
        else if (Sim is { } simAim)
        {
            // Simulated player: the drill preview, or dev runs (validation of the aim coach / sens finder without a human).
            simAim.Gun = weaponDef; // the pick can change between runs (the sim is built once per session)
            var (counts, held, pressed) = simAim.Next(dt, Now, View, Mode.Focus, CurrentSens);
            View.Look(counts, CurrentSens, ZoomSensMultFor(weaponDef));
            frameDx += counts.X; frameDy += counts.Y;
            if (pressed && !fireHeld) firePressedEdge = true;
            fireHeld = held;
            if (simAim.UseAlt && weaponDef is { Scope: false } w)
            {
                // Right click: aimed bursts (Stinger, Bulldog) hold the aim; the Classic / Bucky alt fires on the press
                if (w.AltFire is AltFireKind.Shotgun or AltFireKind.AirBurst) { if (simAim.AltHeld && !simAltWas) altFireEdge = true; }
                else adsHeld = simAim.AltHeld;
                simAltWas = simAim.AltHeld;
            }
            if (Demo && weaponDef is { Scope: true } && Gun != null)
            {
                // Operator preview: scope in whenever the gun is ready, and only fire scoped (like a player would)
                adsHeld = !Gun.Reloading && Gun.EquipLeft <= 0;
                if (!Gun.Scoped) { fireHeld = false; firePressedEdge = false; }
            }
            // Spray preview: every spray starts on a full magazine (the drill counts sprays of its tier's length only), and a
            // spray that empties the magazine ends there (no auto-reload cutting into the next spray)
            if (Demo && Mode is SprayControlMode && Gun != null)
            {
                if (Gun.Ammo <= 0) demoMagEmpty = true;
                if (!held) demoMagEmpty = false; // the simulated player let go: the next spray may start
                if (demoMagEmpty) { fireHeld = false; firePressedEdge = false; }
                if (!fireHeld && !Gun.Reloading && Gun.Ammo < weaponDef!.Mag) Gun.Ammo = weaponDef.Mag;
            }
        }
        else if (Main.I.Dev && DevAutoFire && !Demo)
        {
            bool hold = Now % 3.8f < 2.6f;
            if (hold && !fireHeld) firePressedEdge = true;
            fireHeld = hold;
        }
        Mode.TickBanner(dt);

        if (Mode.Movement && !Player.Dead) MovePlayer(dt); // dead = waiting to respawn (no moving or shooting)

        // Weapon
        if (Gun != null && !Mode.Is2D) UpdateAds(dt); // aim / scope, raise and zoom (AdsSession.cs)

        Mode.Update(dt);

        if (Gun != null && !Mode.Is2D)
        {
            bool wasScoped = Gun.Scoped;
            bool gunInHand = !Player.Dead && !abilityInHand; // an ability in hand owns mouse 1 (AbilitySession.cs)
            var shots = Gun.Tick(Now, dt, View, Mover, Rng, (fireHeld || fireTap) && gunInHand, firePressedEdge && gunInHand, Mode.InfiniteAmmo, Mode.InfiniteReserve, Mode.Movement,
                altFireEdge && gunInHand);
            Volleys(shots);
            if (DevGunLog && !Demo && shots.Count > 0) GunLog(shots);
            var look = WeaponLooks.For(weaponDef!.Kind); // shot / bolt / reload sounds
            if (shots.Count > 0)
            {
                Play2D(look.ShotSound, 0.55f * look.Volume, 1f + ((float)Rng.NextDouble() - 0.5f) * 0.05f);
                if (look.BoltSound is { } bolt) GetTree().CreateTimer(WeaponLooks.BoltDelay).Timeout += () => Play2D(bolt, 0.7f);
            }
            if (look.SpinUpSound is { } spinUp)
            {
                // Odin: the barrel winds up when you hold fire from the hip (aiming skips the spin-up) and winds down after
                bool spinTrig = (fireHeld || fireTap) && gunInHand && Gun.Ammo > 0 && !Gun.Reloading && !Gun.Scoped;
                if (spinTrig && !spinWas) Play2D(spinUp, 0.5f);
                if (spinTrig) spinHeldFor += dt;
                else { if (spinWas && spinHeldFor > 0.3f && look.SpinDownSound is { } spinDown) Play2D(spinDown, 0.4f); spinHeldFor = 0f; }
                spinWas = spinTrig;
            }
            if (Gun.Reloading && !wasReloading) Play2D(look.ReloadSound, 0.6f, look.ReloadPitch);
            wasReloading = Gun.Reloading;
            if (wasScoped && !Gun.Scoped && weaponDef!.Scope) adsHeld = false;
        }
        else if (Mode.Is2D && firePressedEdge)
        {
            Mode.OnBullet(View.Eye, View.Forward, true);
        }
        firePressedEdge = false;
        fireTap = false;
        altFireEdge = false;

        RecordFrame();
        if (Main.I.Dev && Adaptive != null && Mode.SupportsAdaptive && (int)(Now / 10f) != (int)((Now - dt) / 10f))
            Log.Info($"[adapt] t={Now:0} {Adaptive}"); // dev: the staircase every 10 s
        if ((Mode.Timed && Now >= RunDuration) || Mode.Done) Finish();
    }

    /// <summary>Angular offset (deg) of a world point from the crosshair: +yaw = right, +pitch = above.</summary>
    (float Yaw, float Pitch) OffsetFromCrosshair(Vector3 p)
    {
        var d = (p - View.Eye).Normalized();
        float yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        return (Mathf.Wrap(yaw - View.ViewYaw, -180f, 180f), pitch - View.ViewPitch);
    }

    float AngularRadius(Vector3 p, float r) => Mathf.RadToDeg(Mathf.Atan2(r, Mathf.Max(0.1f, View.Eye.DistanceTo(p))));

    void RecordFrame()
    {
        var f = new FrameSample { T = Now, Yaw = View.ViewYaw, Pitch = View.ViewPitch, Dx = frameDx, Dy = frameDy, Speed = Mover.Speed };
        frameDx = frameDy = 0;
        if (!Mode.Is2D && Mode.Focus is { } focus)
        {
            var (fy, fp) = OffsetFromCrosshair(focus.Point);
            f.FocusId = focus.Id; f.FocusYaw = fy; f.FocusPitch = fp; f.FocusRadius = AngularRadius(focus.Point, focus.Radius);
        }
        byte flags = 0;
        if (fireHeld) flags |= (byte)FrameFlags.Firing;
        if (Gun?.Scoped == true) flags |= (byte)FrameFlags.Ads;
        if (!Mode.Movement || Mover.Accurate) flags |= (byte)FrameFlags.Accurate;
        if (Mover.Crouching) flags |= (byte)FrameFlags.Crouch;
        if (Blind.FullyBlind) flags |= (byte)FrameFlags.Blind;
        if (!Player.Dead) flags |= (byte)FrameFlags.Alive;
        if (!Mover.Grounded) flags |= (byte)FrameFlags.Airborne;
        f.Flags = flags;
        Telemetry.Frames.Add(f);
        replay?.Frame(this, Now, viewmodel != null && viewmodel.Visible); // 3D Rewind (60 Hz, never throws)

        // Stops (counter-strafe vs release) for the movement coach.
        if (Mover.StopTimesMs.Count > lastStopCount)
        {
            lastStopCount = Mover.StopTimesMs.Count;
            Event("stop", Mover.LastStopMs, Mover.LastStopWasCounter ? 1 : 0);
        }
    }

    public void Event(string kind, float a = 0, float b = 0) => Telemetry.Events.Add(new TelemetryEvent(Now, kind, a, b));

    /// <summary>Dev-only "--gun-log": a log line per volley (pellets, cone, ammo, fire rate and spin-up, burst, scope) for
    /// gshot / headless checks of the gun mechanics, e.g. <c>--gun Odin --autofire --gun-log</c> shows the fire-rate ramp.</summary>
    static readonly bool DevGunLog = CmdLine.Dev && CmdLine.Has("--gun-log");

    void GunLog(IReadOnlyList<Shot> shots)
    {
        var g = Gun!;
        int pellets = shots.Count(s => s.Volley == shots[^1].Volley);
        Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[gun] t={Now:0.000} {g.Def.Name} volley #{g.Volleys}: {(pellets > 1 ? $"{pellets} pellets, cone {g.LastConeDeg:0.00}°, " : "")}error {g.LastShotError:0.00}°, "
            + $"ammo {g.Ammo}/{g.Def.Mag}, {g.CurrentRps:0.00} rps{(g.Def.SpinUpTime > 0f ? $" (spin {g.Spin:0.00})" : "")}"
            + $"{(g.Bursting ? " burst" : "")}{(g.Scoped ? $" scoped {View.Zoom:0.##}x" : "")}; drill shots {Mode.Shots}, hits {Mode.Hits}"));
    }

    /// <summary>Most tracers drawn for one pellet volley (every pellet still hits and leaves its impact).</summary>
    const int VolleyTracers = 4;

    /// <summary>
    /// Resolves this frame's shots. A single bullet goes straight to <see cref="Bullet"/>. A pellet volley (one trigger pull
    /// of a shotgun, the Classic's blast, the Bucky's air burst) is normalised without touching any drill: every pellet
    /// reaches the drill's OnBullet (so each deals its own damage and can kill), but the drill's Shots / Hits / HeadHits
    /// grow by at most one, its score keeps the hits' points (or one miss's penalty when nothing was hit), and
    /// LastShotZone, the telemetry shot and the Rewind's PlayerShot are the volley's (best zone, pellet 0's aim). Each hit
    /// sound plays once per volley, and at most <see cref="VolleyTracers"/> tracers are drawn.
    /// </summary>
    void Volleys(IReadOnlyList<Shot> shots)
    {
        for (int i = 0; i < shots.Count;)
        {
            int j = i + 1;
            while (j < shots.Count && shots[j].Volley == shots[i].Volley) j++;
            if (j - i == 1 && shots[i].StartAt <= 0f) Bullet(shots[i].Dir, shots[i].Accurate, maxRange: shots[i].MaxRange);
            else Volley(shots, i, j);
            i = j;
        }
    }

    void Volley(IReadOnlyList<Shot> shots, int from, int to)
    {
        int s0 = Mode.Shots, h0 = Mode.Hits, hh0 = Mode.HeadHits, score0 = Mode.Score;
        int hitScore = 0, missScore = 0, recorded = -1, tracers = 0;
        var best = HitZone.None;
        bool anyHit = false, canisterStopped = false;
        var eye = View.Eye;
        var centre = shots[from].Dir;
        volleySounds = new HashSet<string>();
        try
        {
            for (int k = from; k < to; k++)
            {
                var p = shots[k];
                Vector3? origin = null;
                if (p.StartAt > 0f)
                {
                    if (canisterStopped) break; // the canister hit something before it split: no pellets
                    origin = eye + centre * p.StartAt;
                }
                int hits = Mode.Hits, score = Mode.Score;
                bool record = p.Pellet == 0;
                var (hit, wall) = Bullet(p.Dir, p.Accurate, origin, p.MaxRange, record, tracer: tracers++ < VolleyTracers);
                if (record) recorded = Telemetry.Shots.Count - 1;
                bool pelletHit = Mode.Hits > hits || Mode.LastShotZone != HitZone.None;
                if (pelletHit) hitScore += Mode.Score - score;
                else if (record) missScore = Mode.Score - score;
                anyHit |= pelletHit || !float.IsPositiveInfinity(hit);
                if (Mode.LastShotZone != HitZone.None && (best == HitZone.None || Mode.LastShotZone < best)) best = Mode.LastShotZone;
                // Bucky air burst: pellet 0 is the canister, which flies only to the split point.
                if (k == from && to - from > 1 && shots[from + 1].StartAt > 0f)
                    canisterStopped = !float.IsPositiveInfinity(hit) || wall < p.MaxRange;
            }
        }
        finally { volleySounds = null; }
        bool volleyHit = Mode.Hits > h0;
        Mode.Shots = s0 + (Mode.Shots > s0 ? 1 : 0);
        Mode.Hits = h0 + (volleyHit ? 1 : 0);
        Mode.HeadHits = hh0 + (Mode.HeadHits > hh0 ? 1 : 0);
        Mode.Score = score0 + (volleyHit || best != HitZone.None ? hitScore : missScore);
        Mode.LastShotZone = best;
        if (recorded >= 0 && recorded < Telemetry.Shots.Count)
        {
            var shot = Telemetry.Shots[recorded];
            shot.Zone = (sbyte)(best != HitZone.None ? (int)best : anyHit ? 1 : -1);
            Telemetry.Shots[recorded] = shot;
        }
    }

    /// <summary>One bullet or pellet. <paramref name="from"/>: where it starts (null = the eye; the Bucky's air-burst
    /// pellets start at the split point). <paramref name="maxRange"/> (0 = none) caps its reach like a wall, so the drills'
    /// own wall checks (<see cref="BulletWallDist"/>) stop it there. <paramref name="record"/>: write the telemetry shot and
    /// the Rewind's PlayerShot (pellet 0 of a volley only). Returns the drill's hit distance and the wall distance.</summary>
    (float Hit, float Wall) Bullet(Vector3 dir, bool accurate, Vector3? from = null, float maxRange = 0f, bool record = true, bool tracer = true)
    {
        // Snapshot the focus BEFORE the mode reacts (a hit may respawn the target).
        var focus = Mode.Focus;
        Mode.LastShotZone = HitZone.None;
        var eye = View.Eye;
        var origin = from ?? eye;
        float wall = Collision.FirstHit(origin, dir, solid, out int wi);
        float reach = maxRange > 0f ? Mathf.Min(wall, maxRange) : wall;
        BulletWallDist = reach;
        float hitMode = Mode.OnBullet(origin, dir, accurate);
        if (record)
        {
            var shot = new ShotSample
            {
                T = Now, Yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z)), Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dir.Y, -1, 1))),
                Zone = (sbyte)(Mode.LastShotZone != HitZone.None ? (int)Mode.LastShotZone : float.IsPositiveInfinity(hitMode) ? -1 : 1),
                Speed = Mover.Speed, Accurate = accurate,
            };
            if (focus is { } fc)
            {
                var d = (fc.Point - eye).Normalized();
                float fyaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), fpitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
                shot.FocusId = fc.Id;
                shot.ErrYaw = Mathf.Wrap(shot.Yaw - fyaw, -180f, 180f);
                shot.ErrPitch = shot.Pitch - fpitch;
                shot.FocusRadius = AngularRadius(fc.Point, fc.Radius);
            }
            Telemetry.Shots.Add(shot);
        }
        float end = Mathf.Min(Mathf.Min(hitMode, reach), 80f);
        if (tracer)
        {
            var muzzle = from ?? viewmodel?.MuzzleWorld ?? eye + dir * 0.6f;
            Effects.Tracer(muzzle, origin + dir * end);
        }
        if (hitMode < reach && hitMode < 80f && Mode is not BotMode) Effects.BodyHit(origin + dir * hitMode, Enemy, false);
        var wallNormal = Vector3.Zero;
        if (wall < hitMode && wall < 80f && wall <= reach)
        {
            var p = origin + dir * wall;
            wallNormal = solid[wi].NormalAt(p);
            Effects.Impact(p, wallNormal, new Color(0.5f, 0.5f, 0.5f));
        }
        if (record) replay?.PlayerShot(Now, Telemetry.Shots.Count - 1, origin + dir * end, wallNormal);
        return (hitMode, wall);
    }

    void MovePlayer(float dt)
    {
        // Keys → velocity (ground / air control), jump, crouch-jump, wall sliding, gravity, ceiling, landing (MovementBody.cs).
        var feet = new Vector3(View.Eye.X, feetY, View.Eye.Z);
        Mover.Step(dt, View.Yaw, Main.I.Valorant, ref feet, solid);
        feetY = feet.Y;
        View.Eye = new Vector3(feet.X, feetY + Mover.EyeHeight, feet.Z);
        OwnFootsteps(dt); // running footsteps (MovementSession.cs)
    }

    void UpdateCamera()
    {
        if (cam == null) return;
        cam.Position = View.Eye;
        cam.RotationDegrees = new Vector3(View.ViewPitch, -View.ViewYaw, 0);
        cam.Fov = View.HFov;
    }

    float DegToPx(float deg)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float vfov = 2f * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(View.HFov / 2f)) / (size.X / size.Y));
        return size.Y / 2f * Mathf.Tan(Mathf.DegToRad(deg)) / Mathf.Tan(vfov / 2f);
    }

    void Finish()
    {
        if (Demo) { Restart(); return; } // the preview loops: no results, no stats, no telemetry
        if (FreePlay) { freePlayLoop = true; Restart(); return; } // free play keeps going: no results, nothing saved
        State = St.Results;
        ResetTransientInput();
        SetWorldRunning(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var app = Main.I;
        SettleRunGun(); // a match that changed guns is saved under the gun held longest (BuySession.cs)
        PrevBest = app.Stats.Best(Mode, Tier, Mode.ActiveWeapon);
        Record = Mode.MakeRecord(SessionSens ?? app.Sens);
        Record.Warmup = OnFinished != null;
        // Aim coach: metrics for this run + a review of what went wrong and how to fix it.
        Telemetry.Duration = Now;
        Telemetry.Replay = replay?.Finish(Now); // the 3D Rewind's data: in memory now, saved next to the .vtt below
        replay = null;
        try
        {
            Record.Metrics = Coach.Summarize(Telemetry);
            Review = Coach.ReviewRun(Telemetry, Record.Metrics, Tier);
        }
        catch (Exception ex) { GD.PushWarning($"Coach failed: {ex.Message}"); }
        bool adaptive = Mode.SupportsAdaptive && Adaptive != null;
        if (adaptive)
        {
            // Lock-In calibration drills: the level held at the target hit rate (Threshold; the final level if too few trials).
            var st = Adaptive!;
            Record.Metrics ??= new();
            Record.Metrics["adapt.level"] = st.Threshold >= 0 ? st.Threshold : st.Level;
            Record.Metrics["adapt.hitrate"] = st.HitRate;
            Record.Metrics["adapt.trials"] = st.Trials;
            Log.Info($"[adapt] {Mode.Key}: start {st.StartLevel:0.00} -> {st}");
        }
        if (app.SavesData)
        {
            // Written on a worker: ≈1.5 MB for 60 s at 600 FPS, ≈20 MB for a full Sens Finder run. This run's telemetry
            // is never touched again (Restart makes a new one) and the record already carries its metrics.
            var tel = Telemetry;
            try
            {
                string dir = RunTelemetry.Dir;
                Record.TelemetryFile = tel.FileIn(dir);
                Task.Run(() => { try { tel.Save(dir); } catch (Exception e) { Log.Error($"Saving telemetry failed: {e.Message}"); } });
            }
            catch { }
        }
        else if (TelemetryOut != null) { try { Telemetry.Save(TelemetryOut, keep: 500); } catch { } }
        // Automated/dev runs never touch the user's stats; adaptive runs' scores aren't comparable with fixed-tier runs.
        if (app.SavesData && KeepInStats && !adaptive)
        {
            app.Stats.Runs.Add(Record);
            app.Stats.Save();
        }
        if (OnFinished is { } done) { OnFinished = null; done(this); } // routine: next step instead of the results screen
    }

    // ---- routine hooks (warm-up); set before the session enters the tree ----
    /// <summary>Run length in seconds instead of the mode's own (also set by the dev --duration switch).</summary>
    public float? DurationOverride { get; set; }
    /// <summary>Sensitivity applied at every (re)start of this session (warm-up sens shifter); null = the player's sens.</summary>
    public float? SessionSens { get; set; }
    /// <summary>False keeps the run out of stats.json (e.g. played at a shifted sens). Telemetry is still written.</summary>
    public bool KeepInStats { get; set; } = true;
    /// <summary>Routine mode: called once when the run ends (after it is recorded) instead of leaving the results screen up.</summary>
    public Action<GameSession>? OnFinished { get; set; }
    /// <summary>Routine: play this run at another tier (e.g. Main.I.Tier - 1 for the easy activation drills). null = Main.I.Tier.</summary>
    public int? TierOverride { get; set; }
    /// <summary>Routine: adapt difficulty to keep the hit rate near <see cref="Staircase.TargetHitRate"/> (null = normal
    /// fixed-tier run). Only modes with <see cref="TrainingMode.SupportsAdaptive"/> use it. Set a fresh one per run.</summary>
    public Staircase? Adaptive { get; set; }
    /// <summary>Routine: for the last N seconds of a timed run, bots get easier (deathmatch finisher). 0 = off.</summary>
    public float EaseLastSeconds { get; set; }
    /// <summary>Length of this timed run in seconds (the mode's own, a routine override or the dev --duration).</summary>
    public float RunLength => RunDuration;

    // ---------------- IGame ----------------

    public Target SpawnTarget(float radius)
    {
        var t = Target.Create(radius, Enemy);
        t.SpawnTime = Now;
        World.AddChild(t);
        replay?.TrackTarget(t, Now);
        return t;
    }

    public BotCharacter SpawnBot(Vector3 feet, bool crouched = false)
    {
        var b = BotCharacter.Create(Enemy, Main.I.Settings.Outlines);
        b.Crouched = crouched;
        b.SpawnTime = Now;
        World.AddChild(b);
        b.Feet = feet;
        replay?.TrackBot(b, Now);
        return b;
    }

    // ---------------- 3D Rewind recording (Game/Replay) ----------------

    /// <summary>The current run's Rewind recorder; null when the run writes no telemetry or has no 3D replay.</summary>
    ReplayRecorder? replay;

    /// <summary>A recorder for every 3D run (kept in memory for the results-screen Rewind; saved next to the .vtt only when the
    /// run saves telemetry), unless the drill has no 3D replay (2D drills, Sens / Crosshair Finder). <paramref name="extras"/>: the range boxes exactly as built.</summary>
    ReplayRecorder? StartReplay(List<Box> extras)
    {
        try
        {
            var support = ReplayFile.SupportFor(Mode.Key);
            if (Mode.Is2D || support == ReplaySupport.None) return null;
            var st = Main.I.Settings;
            var h = new ReplayHeader
            {
                Mode = Mode.Key, MapKey = Mode.Map?.Key ?? "range",
                MapHash = Mode.Map != null ? ReplayFile.MapHashOf(Mode.Map) : ReplayFile.MapHashOf(extras),
                ExtraSolids = extras.Select(b => new[] { b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z }).ToArray(),
                Weapon = Mode.ActiveWeapon.ToString(), EnemyColor = Enemy.ToHtml(), Outlines = st.Outlines, TargetStyle = st.TargetStyle,
                Viewmodel = st.ViewModel, LeftHanded = st.LeftHandedWeapon, Support = support, AppVersion = AppInfo.Version,
            };
            return new ReplayRecorder(h, () => Now);
        }
        catch (Exception e) { GD.PushWarning($"Rewind recording off: {e.Message}"); return null; }
    }

    public FlashOrb SpawnOrb(Color color)
    {
        var o = FlashOrb.Create(color);
        World.AddChild(o);
        return o;
    }

    public void Despawn(Node node) { if (IsInstanceValid(node)) node.QueueFree(); }

    public void Sound(string name, float vol = 1f, float pitch = 1f)
    {
        if (volleySounds != null && !volleySounds.Add(name)) return; // a pellet volley's hits: each sound once, not 15 stacked
        Play2D(name, vol, pitch);
    }
    /// <summary>Sounds the drill played during the pellet volley being resolved (null outside a volley).</summary>
    HashSet<string>? volleySounds;

    public void SoundAt(string name, Vector3 pos, float maxRange = 40f, float floor = 0.45f, float vol = 1f)
    {
        if (!Demo) Sfx.I.PlayAt(name, pos, View.Eye, maxRange, floor, vol);
    }

    /// <summary>Every game sound goes through here: a preview is silent (the menu's own clicks still play).</summary>
    void Play2D(string name, float vol = 1f, float pitch = 1f)
    {
        if (!Demo) Sfx.I.Play(name, vol, pitch);
    }

    public void DamagePlayer(float dmg, Vector3 from)
    {
        if (Player.Dead || Now < Player.ProtectedUntil) return;
        if (Player.Absorb?.Invoke(dmg, from) == true) return; // agent drills: a shield / dodge took this hit
        Player.Damage(dmg, from, Now);
        Play2D("damage", 0.8f);
        if (Player.Dead)
        {
            Event("died");
            if (Main.I.Dev) Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[died] {Mode.Key} {Mode.Map?.Key} t={Now:0.0} eye=({View.Eye.X:0.00},{View.Eye.Y:0.00},{View.Eye.Z:0.00}) yaw={View.Yaw:0} shot from ({from.X:0.00},{from.Y:0.00},{from.Z:0.00}) {DevOpening(from, View.Eye)}"));
            Play2D("death");
            Mode.OnPlayerDied();
        }
    }

    public void Banner(string text, Color color)
    {
        BannerText = text;
        BannerColor = color;
        BannerTime = 1.4f;
    }

    /// <summary>Dev-only "--campos x,y,z,yaw[,pitch]" (world metres, degrees): every respawn puts the player there instead —
    /// screenshots of exact map spots (map fixes).</summary>
    static readonly float[]? DevCamPos = ArgAfter("--campos")?.Split(',')
        .Select(s => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN)
        .ToArray() is { Length: >= 4 } cp && cp.All(float.IsFinite) ? cp : null;

    public void Respawn(Vector3 feet, float yaw)
    {
        if (Main.I.Dev && !Demo && DevCamPos is { } cp) { feet = new Vector3(cp[0], cp[1], cp[2]); yaw = cp[3]; }
        Mover.ResetMotion();
        Mover.ResetBody();         // standing, no crouch-jump tuck
        Mover.Place(ref feet, solid); // onto the floor under the spawn (or falling from rest if there's none)
        feetY = feet.Y;
        View.Eye = feet + new Vector3(0, Mover.EyeHeight, 0);
        View.Yaw = yaw;
        View.Pitch = Main.I.Dev && !Demo && DevCamPos is { Length: >= 5 } cpp ? cpp[4] : 0;
        View.RecoilPitch = View.RecoilYaw = 0;
        Gun?.ResetRecoil();
        Player.Reset();
        UpdateCamera();
    }

    public bool LineOfSight(Vector3 a, Vector3 b) => (smokes.Count == 0 || !SmokeVolume.Blocks(smokes, a, b, Now)) && !Collision.Blocked(a, b, solid);
    // Controller smokes / walls: vision blockers for LineOfSight (bullets ignore them). Restart() clears them.
    readonly List<SmokeVolume> smokes = new();
    public IReadOnlyList<SmokeVolume> Smokes => smokes;
    public void AddSmoke(SmokeVolume smoke) => smokes.Add(smoke);
    public void ClearSmokes() => smokes.Clear();
    public bool TriggerHeld => (fireHeld || fireTap) && State == St.Running;
    public bool FirePressed => firePressedEdge && State == St.Running;
    public float? TrialSens { get; set; }
    public float CurrentSens => TrialSens ?? Main.I.Sens;
    public ValTrainer.Valorant.CrosshairSettings? TrialCrosshair { get; set; }
    /// <summary>Distance to the first wall along the bullet currently being processed.</summary>
    public float BulletWallDist { get; private set; } = float.PositiveInfinity;
    public string MapKey => Main.I.MapKey;
}
