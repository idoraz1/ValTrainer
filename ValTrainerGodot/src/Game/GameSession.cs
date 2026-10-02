using Godot;
using ValTrainer.Analysis;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Game.Weapon;
using ValTrainer.Modes;
using ValTrainer.UI;
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
    public int Tier => Main.I.Tier;
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
    float feetY, fallSpeed;
    bool firePressedEdge, fireHeld, adsHeld, wasReloading;
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
        crosshair = new CrosshairView { Profile = Main.I.Valorant.Crosshair };
        layer.AddChild(crosshair);
        Blind = new BlindOverlay();
        layer.AddChild(Blind);
        hud = new Hud(this);
        layer.AddChild(hud);

        Restart();
    }

    public void Restart()
    {
        envRoot?.QueueFree();
        World?.QueueFree();
        viewmodel?.QueueFree();
        cam?.QueueFree();

        Mode = factory();
        TrialSens = null;
        Mover = new Mover();
        Player.Reset();
        Now = 0;
        Record = null;
        Review = null;
        frameDx = frameDy = 0;
        lastStopCount = 0;
        viewmodel = null;
        ResetTransientInput();
        wasReloading = false;
        BannerTime = 0;
        AdsBlend = 0;
        View.Zoom = 1;
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

        weaponDef = Weapons.Get(Mode.Weapon);
        Gun = weaponDef != null ? new WeaponController(weaponDef) : null;
        if (weaponDef != null && !Mode.Is2D && Main.I.Settings.ViewModel)
        {
            viewmodel = Viewmodel.Create(weaponDef.Kind, Main.I.Valorant.LeftHanded);
            cam.AddChild(viewmodel);
            Gun!.Fired += () => viewmodel?.OnFire();
        }

        GetViewport().Disable3D = Mode.Is2D;
        Telemetry = new RunTelemetry
        {
            Mode = Mode.Key, Tier = Tier, Sens = Main.I.Sens, Dpi = Main.I.Settings.Dpi,
            Weapon = Mode.Weapon.ToString(), Map = Mode.Map?.Key ?? "range", When = DateTime.Now,
            ZoomSensMult = Mode.Weapon == WeaponKind.Operator ? Main.I.Valorant.ZoomedSensMult : Main.I.Valorant.AdsSensMult,
        };
        Mode.Begin(this);
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

    void ResetTransientInput() { fireHeld = firePressedEdge = adsHeld = false; }

    /// <summary>Freeze bots, effects and timers while paused / on the results screen.</summary>
    void SetWorldRunning(bool on)
    {
        if (World != null && IsInstanceValid(World)) World.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion m && State is St.Countdown or St.Running && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Raw counts (Godot uses Windows raw input while captured). ScreenRelative ignores stretch scaling.
            float mult = weaponDef?.Kind == WeaponKind.Operator ? Main.I.Valorant.ZoomedSensMult : Main.I.Valorant.AdsSensMult;
            View.Look(m.ScreenRelative, CurrentSens, mult);
            frameDx += m.ScreenRelative.X;
            frameDy += m.ScreenRelative.Y;
            UpdateCamera();
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && !fireHeld) firePressedEdge = true;
                fireHeld = mb.Pressed;
            }
            if (mb.ButtonIndex == MouseButton.Right) OnAdsButton(mb.Pressed);
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.Escape)
            {
                if (State is St.Countdown or St.Running) Pause();
                else if (State == St.Paused) Resume();
                else if (State == St.Results) Main.I.ShowMenu();
            }
            else if (k.Keycode == Key.R && State == St.Results) Restart();
            else if (k.Keycode == Key.R && State == St.Running && Gun != null && !Mode.InfiniteAmmo) Gun.StartReload(Mode.InfiniteReserve);
        }
    }

    void OnAdsButton(bool pressed)
    {
        if (Gun == null || weaponDef!.Zoom <= 1f) return;
        bool hold = weaponDef.Kind != WeaponKind.Operator || Main.I.Valorant.HoldToScope;
        if (!pressed) { if (hold) adsHeld = false; return; }   // releases always count, even while paused
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
                firePressedEdge = false;
                frameDx = frameDy = 0; // looking around during the countdown isn't part of the first recorded frame
                break;
            case St.Running:
                RunFrame(dt);
                break;
        }

        UpdateCamera();
        crosshair.Showing = State == St.Results || Mode.Is2D ? CrosshairView.Mode.Hidden
            : Gun?.Scoped == true && weaponDef!.Kind == WeaponKind.Operator ? CrosshairView.Mode.Sniper
            : Gun?.Scoped == true ? CrosshairView.Mode.Ads : CrosshairView.Mode.Primary;
        crosshair.MoveError = Mode.Movement ? Mover.MoveError : 0f;
        crosshair.FiringErrorPx = Gun != null && weaponDef != null ? DegToPx(Mathf.Max(0, Gun.SpreadNow - weaponDef.FirstShotHip)) : 0;
        if (viewmodel != null)
        {
            viewmodel.Visible = !(Gun?.Scoped == true && weaponDef!.Kind == WeaponKind.Operator) && State != St.Results;
            viewmodel.SetAds(AdsBlend);
            viewmodel.SetReload(Gun != null && Gun.Reloading ? 1f - Gun.ReloadLeft / weaponDef!.Reload : -1f);
            viewmodel.SetMotion(Mover.Speed, Mover.Crouching);
        }
    }

    // Dev-only (--dev --autofire): hold the trigger 2.6 s, release 1.2 s, repeat — for scripted tests of sprays.
    static readonly bool DevAutoFire = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--autofire");
    static readonly string? SimProfile = ArgAfter("--simaim");
    /// <summary>Dev-only "--duration N" shortens timed runs for automated tests.</summary>
    float RunDuration => Main.I.Dev && float.TryParse(ArgAfter("--duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : Mode.Duration;
    ValTrainer.Analysis.SimAim? Sim => Main.I.Dev && SimProfile != null ? sim ??= new ValTrainer.Analysis.SimAim(SimProfile) : null;
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
            float mult = weaponDef?.Kind == WeaponKind.Operator ? Main.I.Valorant.ZoomedSensMult : Main.I.Valorant.AdsSensMult;
            View.Look(counts, CurrentSens, mult);
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

        if (Mode.Movement) MovePlayer(dt);

        // Weapon
        if (Gun != null && !Mode.Is2D)
        {
            Gun.Scoped = adsHeld && !Gun.Reloading && weaponDef!.Zoom > 1f && (weaponDef.Kind != WeaponKind.Operator || Gun.EquipLeft <= 0);
            Mover.Ads = Gun.Scoped;
            AdsBlend = Mathf.MoveToward(AdsBlend, Gun.Scoped ? 1f : 0f, dt / 0.12f);
            View.Zoom = Gun.Scoped ? weaponDef!.Zoom : 1f;
        }

        Mode.Update(dt);

        if (Gun != null && !Mode.Is2D)
        {
            bool wasScoped = Gun.Scoped;
            var shots = Gun.Tick(Now, dt, View, Mover, Rng, fireHeld, firePressedEdge, Mode.InfiniteAmmo, Mode.InfiniteReserve, Mode.Movement);
            foreach (var (dir, accurate) in shots) Bullet(dir, accurate);
            if (shots.Count > 0)
            {
                string snd = weaponDef!.Kind switch { WeaponKind.Operator => "operator", WeaponKind.Phantom => "phantom", WeaponKind.Sheriff => "sheriff", _ => "vandal" };
                Sfx.I.Play(snd, 0.55f, 1f + ((float)Rng.NextDouble() - 0.5f) * 0.05f);
                if (weaponDef.Kind == WeaponKind.Operator) GetTree().CreateTimer(0.45).Timeout += () => Sfx.I.Play("bolt", 0.7f);
            }
            if (Gun.Reloading && !wasReloading) Sfx.I.Play(weaponDef!.Kind == WeaponKind.Sheriff ? "reload_pistol" : "reload_rifle", 0.6f, weaponDef.Kind == WeaponKind.Operator ? 0.75f : 1f);
            wasReloading = Gun.Reloading;
            if (wasScoped && !Gun.Scoped && weaponDef!.Kind == WeaponKind.Operator) adsHeld = false;
        }
        else if (Mode.Is2D && firePressedEdge)
        {
            Mode.OnBullet(View.Eye, View.Forward, true);
        }
        firePressedEdge = false;

        RecordFrame();
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
        var delta = Mover.Update(dt, View.Yaw, Main.I.Valorant);
        var feet = new Vector3(View.Eye.X, feetY, View.Eye.Z);
        Collision.MoveAndSlide(ref feet, ref Mover.Vel, delta, solid);
        float ground = Collision.Ground(feet, solid);
        if (ground >= feetY - 0.001f) { feetY = ground; fallSpeed = 0; }
        else { fallSpeed += Collision.Gravity * dt; feetY = Mathf.Max(ground, feetY - fallSpeed * dt); }
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
        Record = Mode.MakeRecord(app.Sens);
        // Aim coach: metrics for this run + a review of what went wrong and how to fix it.
        Telemetry.Duration = Now;
        try
        {
            Record.Metrics = Coach.Summarize(Telemetry);
            Review = Coach.ReviewRun(Telemetry, Record.Metrics, Tier);
        }
        catch (Exception ex) { GD.PushWarning($"Coach failed: {ex.Message}"); }
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
        if (!app.SavesData) return; // automated/dev runs never touch the user's stats
        app.Stats.Runs.Add(Record);
        app.Stats.Save();
    }

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
        if (Player.Dead) return;
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
        feetY = feet.Y;
        fallSpeed = 0;
        View.Eye = feet + new Vector3(0, PlayerView.EyeHeight, 0);
        View.Yaw = yaw;
        View.Pitch = 0;
        View.RecoilPitch = View.RecoilYaw = 0;
        Gun?.ResetRecoil();
        Mover.ResetMotion();
        Player.Reset();
        UpdateCamera();
    }

    public bool LineOfSight(Vector3 a, Vector3 b) => !Collision.Blocked(a, b, solid);
    public bool TriggerHeld => fireHeld && State == St.Running;
    public bool FirePressed => firePressedEdge && State == St.Running;
    public float? TrialSens { get; set; }
    public float CurrentSens => TrialSens ?? Main.I.Sens;
    /// <summary>Distance to the first wall along the bullet currently being processed.</summary>
    public float BulletWallDist { get; private set; } = float.PositiveInfinity;
    public string MapKey => Main.I.MapKey;
}
