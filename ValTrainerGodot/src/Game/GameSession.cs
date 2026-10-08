using Godot;
using ValTrainer.Analysis;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
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
    public enum St { Countdown, Running, Paused, Results }

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

    static GameSession? active;

    public override void _Ready()
    {
        active = this;
        // HUD layer (crosshair under the blind overlay, HUD on top)
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        adsSight = new AdsSight(); // the gun's hologram sight outline, under the crosshair (AdsSession.cs)
        layer.AddChild(adsSight);
        crosshair = new CrosshairView { Profile = Main.I.Valorant.Crosshair };
        layer.AddChild(crosshair);
        Blind = new BlindOverlay();
        layer.AddChild(Blind);
        hud = new Hud(this);
        layer.AddChild(hud);

        if (Main.I.Dev && OnFinished == null) DevRoutineArgs();
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

        var built = Mode.Map != null
            ? Environments.BuildMap(Mode.Map, Main.I.Settings.Quality)
            : Environments.BuildRange(Mode.ExtraSolids, Main.I.Settings.Quality);
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

        Equip(Weapons.Get(Mode.Weapon));

        GetViewport().Disable3D = Mode.Is2D;
        Telemetry = new RunTelemetry
        {
            Mode = Mode.Key, Tier = Tier, Sens = SessionSens ?? Main.I.Sens, Dpi = Main.I.Settings.Dpi,
            Weapon = Mode.Weapon.ToString(), Map = Mode.Map?.Key ?? "range", When = DateTime.Now,
            ZoomSensMult = ZoomSensMultFor(Weapons.Get(Mode.Weapon)),
        };
        Mode.Begin(this);
        if (Main.I.Dev) DevAdsArgs(); // --force-ads (AdsSession.cs)
        Respawn(Mode.StartFeet, Mode.StartYaw);
        Blind.Reset();
        Countdown = 3f;
        State = St.Countdown;
        LockMouse();
        UpdateCamera();
    }

    // ---------------- input ----------------

    public void LockMouse()
    {
        // Captured mode reads raw device counts on Windows; there is no warp spike to skip.
        if (!Main.I.Dev) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void ResetTransientInput() { fireHeld = firePressedEdge = fireTap = adsHeld = false; Keybinds.EndFrame(); }

    /// <summary>Freeze bots, effects and timers while paused / on the results screen.</summary>
    void SetWorldRunning(bool on)
    {
        if (World != null && IsInstanceValid(World)) World.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public override void _Input(InputEvent e)
    {
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
                if (State is St.Countdown or St.Running) Pause();
                else if (State == St.Paused) Resume();
                else if (State == St.Results) Main.I.ShowMenu();
                return;
            }
            if (k.Keycode == Key.R && State == St.Results) { Restart(); return; }
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

    void OnAdsButton(bool pressed, bool wheel, Keybinds binds)
    {
        if (Gun == null || weaponDef!.Zoom <= 1f) return;
        // VALORANT: rifles aim down sights while held; snipers follow "Sniper scope input" (hold / toggle). A wheel notch
        // can't be held, so it always toggles.
        bool hold = (!weaponDef.Sniper || Main.I.Valorant.HoldToScope) && !wheel;
        if (!pressed) { if (hold) adsHeld = binds.IsDown(GameAction.AltFire, real: true); return; } // releases always count, even while paused
        if (State == St.Running) adsHeld = hold || !adsHeld;
    }

    public void Pause()
    {
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
        if (what == NotificationApplicationFocusOut && State is St.Countdown or St.Running && !Main.I.Dev) Pause();
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
                if ((int)Mathf.Ceil(Countdown) != before) Sfx.I.Play(Countdown <= 0 ? "go" : "tick");
                if (Countdown <= 0) State = St.Running;
                firePressedEdge = fireTap = false;
                frameDx = frameDy = 0; // looking around during the countdown isn't part of the first recorded frame
                break;
            case St.Running:
                RunFrame(dt);
                break;
        }
        if (Main.I.Dev) DevKeybindTick(); // --keybind-test (KeybindTest.cs)

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
            viewmodel.SetReload(Gun != null && Gun.Reloading ? 1f - Gun.ReloadLeft / weaponDef!.Reload : -1f);
            viewmodel.SetMotion(Mover.Speed, Mover.Crouching);
        }
        Keybinds.EndFrame(); // wheel notches seen before this frame were polled by it
    }

    // Dev-only (--dev --autofire): hold the trigger 2.6 s, release 1.2 s, repeat — for scripted tests of sprays.
    static readonly bool DevAutoFire = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--autofire");
    static readonly string? SimProfile = ArgAfter("--simaim");
    /// <summary>Dev-only "--duration N" shortens timed runs for automated tests.</summary>
    float RunDuration => DurationOverride ?? (Main.I.Dev && float.TryParse(ArgAfter("--duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : Mode.Duration);
    ValTrainer.Analysis.SimAim? Sim => Main.I.Dev && SimProfile != null ? sim ??= new ValTrainer.Analysis.SimAim(SimProfile, Mode.Key, Mode.SimKind) : null;
    ValTrainer.Analysis.SimAim? sim;

    /// <summary>Dev-only "--pause-at N": opens the pause menu N seconds into the run (layout screenshots).</summary>
    static readonly float? DevPauseAt = float.TryParse(ArgAfter("--pause-at"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pa) ? pa : null;
    bool devPaused;

    void RunFrame(float dt)
    {
        Now += dt;
        if (Main.I.Dev && DevPauseAt is { } pauseAt && Now >= pauseAt && !devPaused) { devPaused = true; Pause(); return; }
        if (Main.I.Dev && Sim != null)
        {
            // Dev-only simulated player (validation of the aim coach / sens finder without a human).
            var (counts, held, pressed) = Sim.Next(dt, Now, View, Mode.Focus, CurrentSens);
            View.Look(counts, CurrentSens, ZoomSensMultFor(weaponDef));
            frameDx += counts.X; frameDy += counts.Y;
            if (pressed && !fireHeld) firePressedEdge = true;
            fireHeld = held;
        }
        else if (Main.I.Dev && DevAutoFire)
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
            var shots = Gun.Tick(Now, dt, View, Mover, Rng, (fireHeld || fireTap) && gunInHand, firePressedEdge && gunInHand, Mode.InfiniteAmmo, Mode.InfiniteReserve, Mode.Movement);
            foreach (var (dir, accurate) in shots) Bullet(dir, accurate);
            if (shots.Count > 0)
            {
                string snd = weaponDef!.Family switch { WeaponKind.Operator => "operator", WeaponKind.Phantom => "phantom", WeaponKind.Sheriff => "sheriff", _ => "vandal" };
                Sfx.I.Play(snd, 0.55f, 1f + ((float)Rng.NextDouble() - 0.5f) * 0.05f);
                if (weaponDef.Kind == WeaponKind.Operator) GetTree().CreateTimer(0.45).Timeout += () => Sfx.I.Play("bolt", 0.7f);
            }
            if (Gun.Reloading && !wasReloading) Sfx.I.Play(weaponDef!.Kind == WeaponKind.Sheriff ? "reload_pistol" : "reload_rifle", 0.6f, weaponDef.Kind == WeaponKind.Operator ? 0.75f : 1f);
            wasReloading = Gun.Reloading;
            if (wasScoped && !Gun.Scoped && weaponDef!.Sniper) adsHeld = false;
        }
        else if (Mode.Is2D && firePressedEdge)
        {
            Mode.OnBullet(View.Eye, View.Forward, true);
        }
        firePressedEdge = false;
        fireTap = false;

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

        // Stops (counter-strafe vs release) for the movement coach.
        if (Mover.StopTimesMs.Count > lastStopCount)
        {
            lastStopCount = Mover.StopTimesMs.Count;
            Event("stop", Mover.LastStopMs, Mover.LastStopWasCounter ? 1 : 0);
        }
    }

    public void Event(string kind, float a = 0, float b = 0) => Telemetry.Events.Add(new TelemetryEvent(Now, kind, a, b));

    void Bullet(Vector3 dir, bool accurate)
    {
        // Snapshot the focus BEFORE the mode reacts (a hit may respawn the target).
        var focus = Mode.Focus;
        Mode.LastShotZone = HitZone.None;
        var eye = View.Eye;
        float wall = Collision.FirstHit(eye, dir, solid, out int wi);
        BulletWallDist = wall;
        float hitMode = Mode.OnBullet(eye, dir, accurate);
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
        float end = Mathf.Min(Mathf.Min(hitMode, wall), 80f);
        var muzzle = viewmodel?.MuzzleWorld ?? eye + dir * 0.6f;
        Effects.Tracer(muzzle, eye + dir * end);
        if (hitMode < wall && hitMode < 80f && Mode is not BotMode) Effects.BodyHit(eye + dir * hitMode, Enemy, false);
        if (wall < hitMode && wall < 80f)
        {
            var p = eye + dir * wall;
            Effects.Impact(p, solid[wi].NormalAt(p), new Color(0.5f, 0.5f, 0.5f));
        }
    }

    void MovePlayer(float dt)
    {
        // Keys → velocity (ground / air control), jump, crouch-jump, wall sliding, gravity, ceiling, landing (MovementBody.cs).
        var feet = new Vector3(View.Eye.X, feetY, View.Eye.Z);
        Mover.Step(dt, View.Yaw, Main.I.Valorant, ref feet, solid);
        feetY = feet.Y;
        View.Eye = new Vector3(feet.X, feetY + Mover.EyeHeight, feet.Z);
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
        State = St.Results;
        ResetTransientInput();
        SetWorldRunning(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var app = Main.I;
        PrevBest = app.Stats.Best(Mode.Key, Tier);
        Record = Mode.MakeRecord(SessionSens ?? app.Sens);
        Record.Warmup = OnFinished != null;
        // Aim coach: metrics for this run + a review of what went wrong and how to fix it.
        Telemetry.Duration = Now;
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
        return t;
    }

    public BotCharacter SpawnBot(Vector3 feet, bool crouched = false)
    {
        var b = BotCharacter.Create(Enemy, Main.I.Settings.Outlines);
        b.Crouched = crouched;
        b.SpawnTime = Now;
        World.AddChild(b);
        b.Feet = feet;
        return b;
    }

    public FlashOrb SpawnOrb(Color color)
    {
        var o = FlashOrb.Create(color);
        World.AddChild(o);
        return o;
    }

    public void Despawn(Node node) { if (IsInstanceValid(node)) node.QueueFree(); }

    public void Sound(string name, float vol = 1f, float pitch = 1f) => Sfx.I.Play(name, vol, pitch);

    public void SoundAt(string name, Vector3 pos, float maxRange = 40f, float floor = 0.45f, float vol = 1f) =>
        Sfx.I.PlayAt(name, pos, View.Eye, maxRange, floor, vol);

    public void DamagePlayer(float dmg, Vector3 from)
    {
        if (Player.Dead || Now < Player.ProtectedUntil) return;
        if (Player.Absorb?.Invoke(dmg, from) == true) return; // agent drills: a shield / dodge took this hit
        Player.Damage(dmg, from, Now);
        Sfx.I.Play("damage", 0.8f);
        if (Player.Dead)
        {
            Event("died");
            Sfx.I.Play("death");
            Mode.OnPlayerDied();
        }
    }

    public void Banner(string text, Color color)
    {
        BannerText = text;
        BannerColor = color;
        BannerTime = 1.4f;
    }

    public void Respawn(Vector3 feet, float yaw)
    {
        Mover.ResetMotion();
        Mover.ResetBody();         // standing, no crouch-jump tuck
        Mover.Place(ref feet, solid); // onto the floor under the spawn (or falling from rest if there's none)
        feetY = feet.Y;
        View.Eye = feet + new Vector3(0, Mover.EyeHeight, 0);
        View.Yaw = yaw;
        View.Pitch = 0;
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
