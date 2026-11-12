using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;

namespace ValTrainer.UI;

/// <summary>
/// The skin picker's live 3D preview (research: skins-design-1.19.md §2.8, skins2-design-1.19.md §4.10): a sub-viewport
/// with its own world, a studio sky for ambient light and reflections (metal skins aren't black), a key and a rim light and
/// AgX tonemapping like the game.
/// <para>TURNTABLE: the gun's model, re-materialed through the same <see cref="VmMaterials"/> path as the viewmodel, turns on a
/// turntable (drag to rotate; it resumes after 2 s). IN HAND: a real <see cref="Viewmodel"/> wearing the skin under a 70°
/// camera, so idle breathing and the hands are exactly what a drill shows. SHOWCASE: the IN HAND gun on a small range with a
/// dummy bot 5 m ahead (the design's 7 m reads too small in the preview) that is shot every 1.2 s and killed on the second
/// shot, so the shown level's kill effect, finisher, kill sounds, sound layers and kill counter play (through the same
/// <see cref="KillFxWorld"/> / <see cref="SkinAudio"/> APIs a drill uses, but with the preview's own world: no GameSession,
/// no <c>Effects.Root</c>, nothing recorded). Settings → Skin effects apply here too. One preview at a time; freed with the picker.</para>
/// <para><see cref="Inspect"/> and <see cref="Reload"/> (the picker's INSPECT / RELOAD buttons) play the gun's inspect
/// (<see cref="Viewmodel.Inspect"/>, with the level's flourish) and a reload of exactly the gun's reload time (the level's
/// special reload where it has one) on the in-hand gun; from the turntable they switch to IN HAND first.</para>
/// </summary>
public partial class SkinPreview : SubViewportContainer
{
    public enum View { Turntable, InHand, Showcase }

    SubViewport vp = null!;
    Camera3D cam = null!;
    Node3D? pivot, gunNode;
    Viewmodel? hand;
    WeaponKind gun = WeaponKind.Vandal;
    GunSkin? skin;
    SkinState? state;
    View view = View.Turntable;
    float yaw = -0.35f, idle = 99f;
    bool dragging, built;
    DrawBox overlay = null!;
    /// <summary>Picker-scale (design units → pixels) for the overlay text.</summary>
    public float K = 1f;

    public SkinPreview()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Drag;
    }

    public WeaponKind Gun => gun;
    public View Mode => view;

    public override void _Ready()
    {
        vp = new SubViewport
        {
            OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            HandleInputLocally = false, GuiDisableInput = true, AudioListenerEnable3D = false,
        };
        AddChild(vp);
        Studio(vp);
        cam = new Camera3D { Current = true, Near = 0.02f, Far = 60f };
        vp.AddChild(cam);
        // the showcase's captions and kill banner, drawn over the 3D view
        overlay = new DrawBox { OnDraw = DrawOverlay, MouseFilter = MouseFilterEnum.Ignore };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
        built = true;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (view == View.Showcase) SkinAudio.StopAll();
    }

    /// <summary>The studio: a soft grey sky for ambient light and reflections, a key light, a rim light, AgX.</summary>
    internal static void Studio(Node parent)
    {
        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.36f, 0.42f, 0.5f), SkyHorizonColor = new Color(0.62f, 0.64f, 0.66f),
            GroundBottomColor = new Color(0.1f, 0.11f, 0.12f), GroundHorizonColor = new Color(0.42f, 0.42f, 0.42f),
            SunAngleMax = 30f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.07f, 0.1f, 0.13f),
            Sky = new Sky { SkyMaterial = skyMat, RadianceSize = Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.1f,
            TonemapAgxWhite = 10f,
            TonemapAgxContrast = 1.3f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env });
        var key = new DirectionalLight3D { LightEnergy = 1.6f, LightColor = new Color(1f, 0.97f, 0.92f), SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        parent.AddChild(key);
        key.Basis = Basis.LookingAt(new Vector3(-0.5f, -0.7f, -0.55f).Normalized(), Vector3.Up);
        var rimLight = new DirectionalLight3D { LightEnergy = 0.7f, LightColor = new Color(0.75f, 0.85f, 1f), SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        parent.AddChild(rimLight);
        rimLight.Basis = Basis.LookingAt(new Vector3(0.6f, -0.2f, 0.8f).Normalized(), Vector3.Up);
    }

    /// <summary>Shows <paramref name="g"/> wearing <paramref name="s"/> (null = factory): its look at that level and variant,
    /// and in SHOWCASE its effects.</summary>
    public void Show(WeaponKind g, SkinState? s)
    {
        bool gunChanged = g != gun;
        bool stateChanged = !Same(state, s);
        gun = g;
        state = s;
        skin = s?.Look;
        if (!built) return;
        if (gunChanged || (view == View.Turntable && gunNode == null) || (view != View.Turntable && hand == null)) { Rebuild(); return; }
        if (view != View.Turntable)
        {
            hand!.SetSkinStateOverride(s);
            if (stateChanged && s is { } fs && fs.Has(SkinFeature.EquipFlourish)) hand.Reequip(); // show the equip roll
        }
        else if (turnPose?.Scene != (Viewmodel.ModelFor(gun, skin) is { } m && !WeaponLooks.For(gun).Gold ? VmPose.ModelTest(m) : WeaponLooks.For(gun).Pose).Scene)
        {
            Rebuild(); // a modelled skin on or off, or another one: another gun model
            return;
        }
        else Remat();
        if (view == View.Showcase && stateChanged) ShowcaseState();
    }

    static bool Same(SkinState? a, SkinState? b) =>
        a is { } x ? b is { } y && x.Line.Id == y.Line.Id && x.Level == y.Level && x.Variant?.Id == y.Variant?.Id : b == null;

    public void SetView(View v)
    {
        if (v == view) return;
        if (view == View.Showcase) SkinAudio.StopAll();
        view = v;
        if (built) Rebuild();
    }

    void Rebuild()
    {
        reloadT = -1f;
        pivot?.QueueFree(); pivot = null; gunNode = null;
        if (hand != null) { hand.QueueFree(); hand = null; }
        TearDownShowcase();
        overlay.QueueRedraw();
        if (view != View.Turntable)
        {
            cam.Fov = 70f;
            cam.Transform = view == View.Showcase ? new Transform3D(Basis.Identity, new Vector3(0, EyeH, 0)) : Transform3D.Identity;
            hand = Viewmodel.CreateWithSkinState(gun, Main.I?.Settings?.LeftHandedWeapon ?? false, state); // the level's equip roll too
            cam.AddChild(hand);
            raisedAt = clockAll + (Weapons.Get(gun)?.Equip ?? 0.75f);
            if (view == View.Showcase) BuildShowcase();
            return;
        }
        // TURNTABLE: the model alone, centred, side on, turning (a modelled Apex skin: its own model, like in hand)
        var pose = Viewmodel.ModelFor(gun, skin) is { } model && !WeaponLooks.For(gun).Gold ? VmPose.ModelTest(model) : WeaponLooks.For(gun).Pose;
        pivot = new Node3D { Name = "Pivot" };
        vp.AddChild(pivot);
        gunNode = GD.Load<PackedScene>(pose.Scene).Instantiate<Node3D>();
        if (gunNode.FindChild("Mag", true, false) is Node mag) { mag.GetParent().RemoveChild(mag); mag.QueueFree(); } // the reload's copy
        pivot.AddChild(gunNode);
        turnPose = pose;
        cam.Fov = 40f;
        var bounds = Remat();
        foreach (var n in gunNode.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        gunNode.Position = -bounds.GetCenter();
        inspectBounds = bounds;
        Fit();
        ApplyYaw();
    }

    Aabb inspectBounds;
    VmPose? turnPose;
    Vector2 fittedFor;

    /// <summary>TURNTABLE: camera distance so the gun's length (and height) fills the view with a margin.</summary>
    void Fit()
    {
        fittedFor = Size;
        if (view != View.Turntable || pivot == null) return;
        float aspect = Mathf.Max(1f, Size.X) / Mathf.Max(1f, Size.Y);
        float half = Mathf.Max(inspectBounds.Size.Z / aspect, inspectBounds.Size.Y * 1.6f) * 0.58f;
        float dist = half / Mathf.Tan(Mathf.DegToRad(cam.Fov / 2f));
        cam.Transform = new Transform3D(Basis.Identity, new Vector3(0, 0.02f, dist));
        cam.LookAt(Vector3.Zero, Vector3.Up);
    }

    /// <summary>TURNTABLE: the model's surfaces through a fresh <see cref="VmMaterials"/> wearing the current skin.</summary>
    Aabb Remat()
    {
        if (gunNode == null) return default;
        var mats = new VmMaterials { Gold = WeaponLooks.For(gun).Gold, Skin = WeaponLooks.For(gun).Gold ? null : skin, Depth = 1f };
        mats.SetFov(cam.Fov);
        Viewmodel.SetTexSkin(mats.Skin);
        return Viewmodel.MaterialGun(gunNode, mats, turnPose ?? WeaponLooks.For(gun).Pose, gun, new List<ShaderMaterial>());
    }

    void ApplyYaw()
    {
        // side on from the gun's right: the barrel (-Z) points right on screen; a slight tilt shows the top
        if (pivot != null) pivot.Basis = new Basis(Vector3.Right, 0.12f) * new Basis(Vector3.Up, -Mathf.Pi / 2 + yaw);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) { dragging = mb.Pressed; idle = 0f; AcceptEvent(); }
        else if (e is InputEventMouseMotion mm && dragging && view == View.Turntable)
        {
            yaw += mm.Relative.X * 0.01f;
            idle = 0f;
            ApplyYaw();
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        float dt0 = Mathf.Min((float)delta, 0.1f);
        clockAll += dt0;
        ActionStep(dt0);
        if (view == View.Showcase) { ShowcaseStep(dt0); return; }
        if (view != View.Turntable || pivot == null) return;
        if (Size != fittedFor) Fit();
        idle += (float)delta;
        if (dragging || idle < 2f) return;
        // 12 degrees a second, swinging around the side view (where a skin shows) rather than a full turn
        yaw += Mathf.DegToRad(12f) * (float)delta * swing;
        if (yaw > 0.9f) { yaw = 0.9f; swing = -1f; }
        else if (yaw < -0.9f) { yaw = -0.9f; swing = 1f; }
        ApplyYaw();
    }
    float swing = 1f;

    // ---------------- INSPECT / RELOAD ----------------

    float clockAll, raisedAt, reloadT = -1f, reloadLen;
    bool inspectQueued;

    /// <summary>True while a preview reload plays (the showcase holds its fire).</summary>
    public bool Reloading => reloadT >= 0f;
    /// <summary>The in-hand gun is inspecting (the showcase holds its fire, which would cancel it).</summary>
    public bool Inspecting => hand?.Inspecting == true || inspectQueued;

    /// <summary>The picker's INSPECT: the in-hand gun's inspect (<see cref="Viewmodel.Inspect"/>; the skin's flourish when the
    /// level has one). From the turntable the caller switches to IN HAND first; while the gun is still being raised the
    /// inspect waits for it. Cancels a preview reload.</summary>
    public void Inspect()
    {
        if (hand == null) return;
        if (reloadT >= 0f) { reloadT = -1f; hand.SetReload(-1f); }
        inspectQueued = true;
    }

    /// <summary>The picker's RELOAD: one reload of the in-hand gun, exactly the gun's reload time, with the factory reload
    /// sound and the skin's reload layer (and the level's special reload animation, which the viewmodel plays from the same
    /// reload progress). Ammo and timing are the game's own; nothing else changes.</summary>
    public void Reload()
    {
        if (hand == null || reloadT >= 0f) return;
        inspectQueued = false;
        var def = Weapons.Get(gun);
        reloadLen = Mathf.Max(0.3f, def?.Reload ?? 2f);
        reloadT = 0f;
        var look = WeaponLooks.For(gun);
        Sfx.I?.Play(look.ReloadSound, 0.6f, look.ReloadPitch);
        SkinAudio.PlayReloadLayer(state, gun, look.ReloadPitch);
        hand.SetReload(0f);
    }

    void ActionStep(float dt)
    {
        if (hand == null) { inspectQueued = false; reloadT = -1f; return; }
        if (inspectQueued && clockAll >= raisedAt)
        {
            inspectQueued = false;
            hand.Inspect();
        }
        if (reloadT < 0f) return;
        reloadT += dt;
        if (reloadT >= reloadLen) { reloadT = -1f; hand.SetReload(-1f); }
        else hand.SetReload(reloadT / reloadLen);
    }

    // ---------------- SHOWCASE ----------------

    const float EyeH = 1.6f, DummyDist = 5f, ShotGap = 1.2f;
    Node3D? stage;
    KillFxWorld? killFx;
    BotCharacter? dummy;
    MeshInstance3D? tracer;
    StandardMaterial3D? tracerMat;
    float clock, nextShot, respawnAt, boltAt = -1f, tracerT, ggT, bannerT, camYaw;
    int shotsLeft, kills, bannerN, side = 1;
    bool finisherPlayed;

    /// <summary>The range: a floor, a lit spot where the dummy stands, the kill-effect world and a tracer.</summary>
    void BuildShowcase()
    {
        stage = new Node3D { Name = "Showcase" };
        vp.AddChild(stage);
        SetFog(true);
        var floorMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.135f, 0.15f), Roughness = 0.95f };
        stage.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(160, 160) }, MaterialOverride = floorMat, Position = new Vector3(0, 0, -40),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var padMat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.22f, 0.24f), Roughness = 0.9f };
        foreach (int s in new[] { -1, 1 })
            stage.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 0.01f, RadialSegments = 40 }, MaterialOverride = padMat,
                Position = DummySpot(s) + new Vector3(0, 0.005f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        tracerMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(1f, 0.82f, 0.5f, 0f),
        };
        tracer = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.012f, 1f) }, MaterialOverride = tracerMat, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        stage.AddChild(tracer);
        killFx = KillFxWorld.Create(stage, state, showcase: true); // pools built whatever the settings (toggled while open)
        clock = 0f;
        kills = 0;
        respawnAt = 0.35f; // let the view settle first
        dummy = null;
        ShowcaseState();
    }

    /// <summary>Two spots, left and right of centre, used in turn: the last dummy sinks on its own while the next one stands.</summary>
    static Vector3 DummySpot(int side) => new(side * 0.9f, 0, -DummyDist);

    /// <summary>The shown level changed: new effect pools, sound set preloaded, the counter starts over.</summary>
    void ShowcaseState()
    {
        if (view != View.Showcase) return;
        SkinAudio.StopAll();
        killFx?.ClearAll();
        killFx?.Setup(state);
        SkinAudio.Preload(state);
        SkinAudio.PlayEquip(state, gun);
        kills = 0;
        ggT = 0f;
        hand?.SetKillCount(0);
        overlay.QueueRedraw();
    }

    /// <summary>SHOWCASE: depth fog into the background colour, so the floor fades out instead of ending at a hard edge.</summary>
    void SetFog(bool on)
    {
        if (vp.GetChildren().OfType<WorldEnvironment>().FirstOrDefault()?.Environment is not { } env) return;
        env.FogEnabled = on;
        if (!on) return;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        env.FogLightColor = env.BackgroundColor;
        env.FogLightEnergy = 1f;
        env.FogDensity = 1f;
        env.FogDepthBegin = 9f;
        env.FogDepthEnd = 30f;
        env.FogSkyAffect = 0f;
    }

    void TearDownShowcase()
    {
        if (stage == null) return;
        SetFog(false);
        SkinAudio.StopAll();
        stage.QueueFree(); // the dummy, the kill-effect world and the tracer are its children
        stage = null; killFx = null; dummy = null; tracer = null;
    }

    void SpawnDummy()
    {
        side = -side;
        var spot = DummySpot(side);
        killFx?.ClearNear(spot);
        var enemy = Main.I?.EnemyColor ?? UiTheme.EnemyColors[0];
        dummy = BotCharacter.Create(enemy, Main.I?.Settings?.Outlines ?? true);
        dummy.QuietSteps = true;
        stage!.AddChild(dummy);
        dummy.Feet = spot;
        dummy.FacingYaw = 180f; // facing the camera
        shotsLeft = 2;
        nextShot = clock + 0.5f;
        finisherPlayed = false;
    }

    void ShowcaseStep(float dt)
    {
        if (stage == null || hand == null) return;
        clock += dt;
        if ((dummy == null || dummy.Dead) && clock >= respawnAt) SpawnDummy();
        // the camera turns a little toward the dummy, the way a player's aim would
        float want = dummy != null && IsInstanceValid(dummy) && !dummy.Dead ? Mathf.Atan2(-dummy.Feet.X, DummyDist) : camYaw;
        camYaw = Mathf.Lerp(camYaw, want, 1f - Mathf.Exp(-8f * dt));
        cam.Transform = new Transform3D(new Basis(Vector3.Up, camYaw) * new Basis(Vector3.Right, -0.03f), new Vector3(0, EyeH, 0));
        bool busy = Reloading || Inspecting; // a shot would cancel the inspect; no firing mid-reload
        if (busy && clock >= nextShot - 0.2f) nextShot = clock + 0.4f;
        if (!busy && dummy != null && IsInstanceValid(dummy) && !dummy.Dead && shotsLeft > 0 && clock >= nextShot) Shoot();
        if (boltAt >= 0 && clock >= boltAt)
        {
            boltAt = -1f;
            if (WeaponLooks.For(gun).BoltSound is { } bolt) Sfx.I?.Play(bolt, 0.7f);
            SkinAudio.PlayBoltLayer(state, gun);
        }
        if (tracer != null && tracerT > 0f)
        {
            tracerT -= dt;
            tracerMat!.AlbedoColor = tracerMat.AlbedoColor with { A = Mathf.Clamp(tracerT / 0.08f, 0f, 1f) * 0.9f };
            if (tracerT <= 0f) tracer.Visible = false;
        }
        ggT = Mathf.Max(0f, ggT - dt);
        float bannerWas = bannerT;
        bannerT = Mathf.Max(0f, bannerT - dt);
        if (state is { } st && st.Has(SkinFeature.KillCounter) && (Main.I?.Settings?.SkinKillCounter ?? true)) hand.SetKillCount(kills, ggT > 0f);
        if (bannerT > 0f || bannerWas > 0f) overlay.QueueRedraw();
    }

    /// <summary>One shot at the dummy: the factory shot sound plus the skin's layer at the same pitch, the gun's kick and a
    /// tracer; the second shot is a headshot that kills.</summary>
    void Shoot()
    {
        var look = WeaponLooks.For(gun);
        float pitch = 1f + (Random.Shared.NextSingle() - 0.5f) * 0.05f;
        hand!.OnFire();
        Sfx.I?.Play(look.ShotSound, 0.55f * look.Volume, pitch);
        SkinAudio.PlayFireLayer(state, gun, pitch);
        if (look.BoltSound != null) boltAt = clock + WeaponLooks.BoltDelay;
        shotsLeft--;
        var bot = dummy!;
        bool kill = shotsLeft <= 0;
        var zone = kill ? HitZone.Head : HitZone.Body;
        var target = kill ? bot.Head : bot.Head - new Vector3(0, 0.45f, 0);
        var from = hand.MuzzleWorld;
        Tracer(from, target, state is { } ts && ts.Tracer is { } tc ? tc : new Color(1f, 0.82f, 0.5f));
        bot.OnHit(zone);
        if (!SkinAudio.PlayHit(state, kill, kill)) Sfx.I?.Play(kill ? "head" : "body", kill ? 1f : 0.8f);
        if (!kill) { nextShot = clock + ShotGap; return; }
        bot.Hp = 0;
        bot.Die((target - from).Normalized());
        Kill(bot, zone, from.DistanceTo(target));
    }

    void Tracer(Vector3 from, Vector3 to, Color c)
    {
        if (tracer == null || tracerMat == null) return;
        var d = to - from;
        float len = d.Length();
        if (len < 0.1f) return;
        tracer.Visible = true;
        tracer.Transform = new Transform3D(Basis.LookingAt(d / len, Vector3.Up).Scaled(new Vector3(1, 1, len)), from + d / 2);
        tracerMat.AlbedoColor = c with { A = 0.9f };
        tracerT = 0.08f;
    }

    /// <summary>The kill, as a drill's KillTracker would play it: the kill sound (the skin's own escalating one, else factory),
    /// the gun-side pulse and world kill effect (Settings → Kill effects), and the finisher (Settings → Finishers): each
    /// dummy is the last enemy of its "round", so a level with a finisher plays the full one.</summary>
    void Kill(BotCharacter bot, HitZone zone, float dist)
    {
        kills++;
        int n = (kills - 1) % 5 + 1;
        if (!SkinAudio.PlayKillSound(state, n, 0.9f)) Sfx.I?.Play("kill" + n, 0.9f);
        bannerN = n;
        bannerT = 1.4f;
        var info = new KillInfo(bot, bot.Feet, bot.Head - new Vector3(0, 0.35f, 0), zone, dist, gun, clock)
            { Drill = KillDrill.Round, RoundWinning = true, LifeKills = kills };
        var enemy = Main.I?.EnemyColor ?? UiTheme.EnemyColors[0];
        if (state is { } st)
        {
            var mode = KillFxWorld.Mode;
            if (st.Has(SkinFeature.KillFx) && mode != SkinKillFxMode.Off)
            {
                hand!.OnPlayerKill();
                if (mode == SkinKillFxMode.Full)
                {
                    killFx?.Kill(info, st, enemy);
                    SkinAudio.PlayKillEffect(st);
                }
            }
            if (st.Has(SkinFeature.KillCounter) && (Main.I?.Settings?.SkinKillCounter ?? true)) SkinAudio.PlayCounterTick(st);
            if (st.Has(SkinFeature.Finisher) && KillFxWorld.FinishersOn)
            {
                killFx?.Finisher(new FinisherCue(info, false, FinisherReason.RoundWon), st, enemy);
                SkinAudio.PlayFinisher(st, false);
                finisherPlayed = true;
                ggT = 2f;
            }
        }
        // the next dummy stands up once the effects are over (a finisher takes up to 2 s)
        respawnAt = clock + (finisherPlayed ? 2.3f : 0.9f);
        overlay.QueueRedraw();
    }

    /// <summary>SHOWCASE captions: what the shown level plays here (and what Settings turned off), and the skin's kill banner
    /// (<see cref="Hud.DrawSkinBanner"/>, the HUD's own drawing) for levels with one.</summary>
    void DrawOverlay(DrawBox d)
    {
        if (view != View.Showcase) return;
        float k = K;
        int fs = UiTheme.Fs(12, k);
        var plays = new List<string>();
        var off = new List<string>();
        var st = state;
        var set = Main.I?.Settings;
        void Add(SkinFeature f, string name, bool on) { if (st is { } s && s.Has(f)) (on ? plays : off).Add(name); }
        Add(SkinFeature.Sounds, L.N("Sound layers"), set?.SkinSounds ?? true);
        Add(SkinFeature.HitSounds, L.N("Hit sounds"), set?.SkinSounds ?? true);
        Add(SkinFeature.BulletFx, L.N("Bullet FX"), true);
        Add(SkinFeature.KillFx, L.N("Kill effect"), KillFxWorld.Mode != SkinKillFxMode.Off);
        Add(SkinFeature.Finisher, L.N("Finisher"), KillFxWorld.FinishersOn);
        Add(SkinFeature.KillCounter, L.N("Kill counter"), set?.SkinKillCounter ?? true);
        string head = plays.Count > 0 ? string.Join(" · ", plays.Select(p => L.Upper(L.T(p)))) : st == null ? L.T("FACTORY SOUNDS") : L.T("NO EFFECTS AT THIS LEVEL");
        float x = 12 * k, y = 18 * k;
        Gfx.TextRaw(d, UiTheme.HudWide, head, x, Gfx.Mid(y, fs), fs, plays.Count > 0 ? UiTheme.Good : UiTheme.Dim);
        if (off.Count > 0)
            Gfx.TextRaw(d, UiTheme.Body, L.F($"Off in Settings: {string.Join(", ", off.Select(L.T))}"), x, Gfx.Mid(y + 18 * k, fs), fs, UiTheme.Warn);
        if (bannerT <= 0f || st is not { Banner: { } style } bs) return;
        // the kill banner as the HUD draws it, in the line's (or variant's) palette; smaller, the preview is a small view
        float bk = k * 0.7f;
        Hud.DrawSkinBanner(d, new Vector2(d.Size.X / 2, d.Size.Y - 72 * bk), bk, bannerN, bannerT, style, bs.Palette.Main, bs.Palette.Accent);
    }
}

/// <summary>
/// Skin swatches for the picker: each line's base look and each variant (id <c>line/variant</c>) rendered once on a small
/// bevelled block (the Body finish through the real skin shader, studio light), cached as a texture, the same way
/// <see cref="GunIcons"/> renders silhouettes. Rendering also compiles the skin shaders before a drill needs them. Under
/// --headless, or until a render arrives, a swatch is drawn flat from the skin's colours (<see cref="DrawFlat"/>).
/// </summary>
public static class SkinSwatches
{
    static readonly Dictionary<string, Texture2D?> rendered = new();
    static bool started;
    /// <summary>Bumped whenever a rendered swatch arrives (widgets redraw).</summary>
    public static int Revision { get; private set; }

    public static Texture2D? Get(string id) => rendered.TryGetValue(id, out var t) ? t : null;

    /// <summary>Starts rendering every swatch not made yet (once per run; nothing under --headless).</summary>
    public static void Warm()
    {
        if (started || DisplayServer.GetName() == "headless" || Main.I == null) return;
        started = true;
        RenderAll();
    }

    static async void RenderAll()
    {
        SubViewport? vp = null;
        try
        {
            vp = new SubViewport
            {
                OwnWorld3D = true, Size = new Vector2I(256, 160), Msaa3D = Viewport.Msaa.Msaa4X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = false, GuiDisableInput = true, AudioListenerEnable3D = false,
            };
            Main.I.AddChild(vp);
            SkinPreview.Studio(vp);
            var cam = new Camera3D { Fov = 40f, Near = 0.02f, Far = 5f, Current = true };
            vp.AddChild(cam);
            cam.Position = new Vector3(0.11f, 0.075f, 0.13f);
            cam.LookAt(new Vector3(0, -0.004f, 0), Vector3.Up);
            var size = new Vector3(0.07f, 0.05f, 0.16f);
            var box = new MeshInstance3D { Mesh = Bevel(size), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            vp.AddChild(box);
            // factory and every line's base look first (the line tiles), then every variant (the variant strip)
            var looks = GunSkins.All.Prepend(null).Concat(SkinCatalog.Lines.SelectMany(l => l.Variants.Select(v => (GunSkin?)l.LookAt(1, v))));
            foreach (var skin in looks)
            {
                var mats = new VmMaterials { Skin = skin, Depth = 1f };
                mats.SetFov(cam.Fov);
                var m = skin != null ? mats.ForSkinSurface(skin, VmMaterials.Role.Body)! : mats.ForGunSurface("black", new Color(0.05f, 0.05f, 0.05f));
                if (VmMaterials.IsSkin(m))
                {
                    m.SetShaderParameter("gun_min", -size / 2 + new Vector3(0, 0, -0.35f)); // as if the block were a gun's grip area
                    m.SetShaderParameter("gun_max", size / 2 + new Vector3(0, 0, 0.25f));
                }
                box.MaterialOverride = m;
                for (int i = 0; i < 3; i++) await Main.I.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!GodotObject.IsInstanceValid(vp)) return;
                var img = vp.GetTexture()?.GetImage();
                string id = skin?.Id ?? GunSkins.FactoryId;
                if (img != null && !img.IsEmpty())
                {
                    img.GenerateMipmaps();
                    rendered[id] = ImageTexture.CreateFromImage(img);
                    Revision++;
                }
            }
        }
        catch (Exception e) { Log.Error($"[skins] swatches: {e.Message}"); }
        finally
        {
            if (vp != null && GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
    }

    /// <summary>A box with chamfered edges (flat-shaded like the gun models).</summary>
    static Mesh Bevel(Vector3 size)
    {
        float c = 0.006f;
        var h = size / 2;
        // 24 corner points of a chamfered box: each corner of the box split into three, one per face it touches
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d)
        {
            var n = (b - a).Cross(cc - a).Normalized();
            st.SetNormal(n); st.AddVertex(a); st.SetNormal(n); st.AddVertex(b); st.SetNormal(n); st.AddVertex(cc);
            st.SetNormal(n); st.AddVertex(a); st.SetNormal(n); st.AddVertex(cc); st.SetNormal(n); st.AddVertex(d);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 cc)
        {
            var n = (b - a).Cross(cc - a).Normalized();
            st.SetNormal(n); st.AddVertex(a); st.SetNormal(n); st.AddVertex(b); st.SetNormal(n); st.AddVertex(cc);
        }
        Vector3 P(int sx, int sy, int sz, int axis)
        {
            // corner (sx, sy, sz) pulled in by c on the two axes other than axis
            var p = new Vector3(sx * h.X, sy * h.Y, sz * h.Z);
            if (axis != 0) p.X -= sx * c;
            if (axis != 1) p.Y -= sy * c;
            if (axis != 2) p.Z -= sz * c;
            return p;
        }
        int[] s = { -1, 1 };
        // faces
        foreach (int sx in s) Quad(P(sx, -1, -1, 0), P(sx, 1, -1, 0), P(sx, 1, 1, 0), P(sx, -1, 1, 0));
        foreach (int sy in s) Quad(P(-1, sy, -1, 1), P(-1, sy, 1, 1), P(1, sy, 1, 1), P(1, sy, -1, 1));
        foreach (int sz in s) Quad(P(-1, -1, sz, 2), P(1, -1, sz, 2), P(1, 1, sz, 2), P(-1, 1, sz, 2));
        // edge chamfers and corners
        foreach (int sx in s) foreach (int sy in s) Quad(P(sx, sy, -1, 0), P(sx, sy, -1, 1), P(sx, sy, 1, 1), P(sx, sy, 1, 0));
        foreach (int sx in s) foreach (int sz in s) Quad(P(sx, -1, sz, 0), P(sx, -1, sz, 2), P(sx, 1, sz, 2), P(sx, 1, sz, 0));
        foreach (int sy in s) foreach (int sz in s) Quad(P(-1, sy, sz, 1), P(-1, sy, sz, 2), P(1, sy, sz, 2), P(1, sy, sz, 1));
        foreach (int sx in s) foreach (int sy in s) foreach (int sz in s) Tri(P(sx, sy, sz, 0), P(sx, sy, sz, 1), P(sx, sy, sz, 2));
        // the winding of every face above is fixed up by making each triangle face away from the centre
        var arrays = st.CommitToArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var norms = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            var ctr = (verts[i] + verts[i + 1] + verts[i + 2]) / 3f;
            var n = (verts[i + 1] - verts[i]).Cross(verts[i + 2] - verts[i]).Normalized();
            if (n.Dot(ctr) > 0) { (verts[i + 1], verts[i + 2]) = (verts[i + 2], verts[i + 1]); n = -n; }
            norms[i] = norms[i + 1] = norms[i + 2] = -n; // Godot's front faces wind clockwise
        }
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = norms;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Flat stand-in: the skin's colours as diagonal bands (headless runs, or before the render arrives).</summary>
    public static void DrawFlat(CanvasItem ci, Rect2 r, GunSkin? skin)
    {
        if (skin == null) { ci.DrawRect(r, new Color(0.1f, 0.105f, 0.11f)); ci.DrawRect(new Rect2(r.Position + new Vector2(0, r.Size.Y * 0.62f), new Vector2(r.Size.X, r.Size.Y * 0.38f)), new Color(0.3f, 0.31f, 0.33f)); return; }
        var cols = new List<Color> { skin.A.Color };
        if (skin.B != null) cols.Add(skin.B.Color);
        if (skin.C != null) cols.Add(skin.C.Color);
        if (skin.D != null) cols.Add(skin.D.Color);
        float w = r.Size.X / cols.Count;
        for (int i = 0; i < cols.Count; i++) ci.DrawRect(new Rect2(r.Position.X + i * w, r.Position.Y, w + 1, r.Size.Y), cols[i]);
        if (skin.Trim != null) ci.DrawRect(new Rect2(r.Position.X, r.End.Y - r.Size.Y * 0.2f, r.Size.X, r.Size.Y * 0.2f), skin.Trim.Color);
    }
}
