using Godot;
using ValTrainer.Audio;
using ValTrainer.Game.Fx;

namespace ValTrainer.Game.Bots;

/// <summary>
/// An enemy agent in the world: an animated CC0 Quaternius character (random of 4 looks) holding an AK.
/// <list type="bullet">
/// <item>Scaled at runtime so the standing head-hitbox centre averages exactly <see cref="PlayerView.EyeHeight"/>;
/// crouched heads sit ~0.5 m lower (full Crouch_Idle depth).</item>
/// <item>Locomotion from <see cref="Velocity"/> relative to <see cref="FacingYaw"/>: idle / walk / run (time-scaled to the
/// ground speed), crouch idle / walk. No strafe clips exist, so the legs yaw toward the movement (±75°) while the
/// spine is counter-twisted so torso, rifle and head keep facing <see cref="FacingYaw"/>; backwards = clips played in
/// reverse. While running the hip bounce is damped (two-bone leg IK keeps the feet planted) and the torso is kept
/// upright, so the head stays within a few cm of eye level (crosshair-placement practice).</item>
/// <item>Upper body: armed Pistol_Idle stance, rifle in the right hand aimed along FacingYaw (and at the last shot's
/// target after firing); Hit_Head / Hit_Chest flinches on the spine; Death01 (falls away from the bullet), sink, free.</item>
/// <item>Procedural bone overrides run in <see cref="BotPoseModifier"/> (the skeleton's last modifier).</item>
/// <item>Hitboxes are rebuilt from bones every frame (head sphere, torso / arm / leg capsules) and ray-tested analytically.</item>
/// <item>VALORANT enemy look: screen-space outline + fresnel rim in the enemy colour (depth-tested — never through walls),
/// hit flash, muzzle flash + tracer, positional running footsteps.</item>
/// </list>
/// The public API is the contract the modes rely on; keep it stable.
/// </summary>
public partial class BotCharacter : Node3D
{
    public const float HeadRadius = 0.14f;
    public const int MaxHp = 150;

    public int Hp = MaxHp;
    public bool Dead => Hp <= 0;
    public float SpawnTime;
    /// <summary>World velocity (m/s) — set by the mode each frame; drives run/strafe animation and footsteps.</summary>
    public Vector3 Velocity;
    /// <summary>Facing in the game's yaw convention (degrees; 0 = -Z, + = right).</summary>
    public float FacingYaw { get => facing; set { facing = value; Rotation = new Vector3(0, Mathf.DegToRad(-value) + Mathf.Pi, 0); } }
    public bool Crouched;
    /// <summary>Optional world point the rifle should pitch toward (e.g. the player's eye while engaging); null = level.
    /// Yaw always follows <see cref="FacingYaw"/>. <see cref="ShootFx"/> also aims at its target for ~1 s.</summary>
    public Vector3? AimTarget;

    /// <summary>Head hitbox centre (world).</summary>
    public Vector3 Head => GlobalTransform * pts[PHead];
    public Vector3 Feet { get => GlobalPosition; set => GlobalPosition = value; }
    /// <summary>World position of the rifle's muzzle (falls back to the head).</summary>
    public Vector3 MuzzlePosition => muzzle != null && muzzle.IsInsideTree() ? muzzle.GlobalPosition : Head;
    /// <summary>Which of the 4 character looks this bot uses (see <see cref="BotAssets.Variants"/>).</summary>
    public int Variant => variant;

    // Hitbox radii (m).
    const float TorsoR = 0.17f, UpperArmR = 0.065f, LowerArmR = 0.055f, ThighR = 0.095f, ShinR = 0.075f, FootR = 0.06f;
    const float MaxLegYaw = 1.31f;    // 75°
    const float RunStepSpeed = 3.2f;  // faster than this = running = audible footsteps (walking / crouching is silent)
    const float StepInterval = 0.33f;

    // Hitbox points in this node's local space (= world metres, rotated by FacingYaw).
    const int PHead = 0, PHips = 1, PNeck = 2, PLUA = 3, PLLA = 4, PLH = 5, PRUA = 6, PRLA = 7, PRH = 8,
        PLUL = 9, PLLL = 10, PLF = 11, PLT = 12, PRUL = 13, PRLL = 14, PRF = 15, PRT = 16, PCount = 17;
    readonly Vector3[] pts = new Vector3[PCount];

    float facing;
    Color enemy;
    bool outlines;
    int variant;
    BotAssets.BodyCal? cal;

    Node3D pivot = null!, model = null!, gunMount = null!;
    Skeleton3D skel = null!;
    AnimationTree tree = null!;
    Transform3D skelInModel = Transform3D.Identity;
    Node3D? muzzle;
    OmniLight3D? flashLight;
    MeshInstance3D? flashQuad;
    AudioStreamPlayer3D? stepPlayer;
    readonly List<GeometryInstance3D> highlighted = new();

    // animation state
    float legYaw, moveW, gaitW, crouchW, dieW, recoil;
    bool backwards;
    float flash, lastFlash = -1f, fade = 1f, lastFade = 1f;
    float deathT = -1f, deathYaw;
    float aimPitch, aimW, aimHold;
    float flashT, stepT, flinchT;
    bool posed;

    static readonly StringName FlashParam = "hl_flash", FadeParam = "hl_fade", SeedParam = "seed";

    /// <summary>Load + calibrate one male and one female rig during the countdown so the first real spawn doesn't hitch.</summary>
    public static void Prewarm(Node3D parent, Color enemy, bool outlines, Vector3 hidden)
    {
        foreach (int v in new[] { 0, 2 })
        {
            var b = Create(enemy, outlines, v);
            parent.AddChild(b);
            b.Feet = hidden;
            b.GetTree().CreateTimer(0.3).Timeout += () => { if (IsInstanceValid(b)) b.QueueFree(); };
        }
    }

    public static BotCharacter Create(Color enemy, bool outlines, int variant = -1)
    {
        var b = new BotCharacter { enemy = enemy, outlines = outlines, variant = variant >= 0 ? variant : Random.Shared.Next(BotAssets.Variants.Length) };
        b.Name = "Bot";
        b.ProcessPriority = 100; // after GameSession / mode updates, so the pose matches this frame's Feet/Velocity
        b.Build();
        return b;
    }

    // ---------------------------------------------------------------- construction

    void Build()
    {
        pivot = new Node3D { Name = "Pivot" };
        AddChild(pivot);
        model = BotAssets.Scene(variant).Instantiate<Node3D>();
        pivot.AddChild(model);
        skel = model.GetNode<Skeleton3D>("%GeneralSkeleton");
        BotAssets.InitBones(skel);
        var armature = skel.GetParent() as Node3D;
        skelInModel = (armature != null && armature != model ? armature.Transform : Transform3D.Identity) * skel.Transform;

        tree = new AnimationTree
        {
            Name = "Anim",
            TreeRoot = BotAssets.Tree(skel),
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
        };
        tree.AddAnimationLibrary("ual", BotAssets.Library);
        tree.RootNode = new NodePath(".."); // the character scene root (owner of %GeneralSkeleton)
        model.AddChild(tree);

        // Rifle in the right hand, placed from the FINAL hand pose in the pose modifier (a BoneAttachment3D would not
        // see our spine overrides and would point the gun along the legs while strafing).
        gunMount = new Node3D { Name = "RightHandMount" };
        skel.AddChild(gunMount);
        var gun = BotAssets.Rifle.Instantiate<Node3D>();
        gun.Transform = BotAssets.RifleInHand;
        gunMount.AddChild(gun);

        // Materials: stencil-writing base + additive fresnel/flash overlay (+ screen-space outline pass).
        var overlay = BotAssets.Overlay(enemy, outlines);
        foreach (var n in skel.GetChildren()) if (n is MeshInstance3D mi) SetupMesh(mi, overlay, gun: false);
        SetupGunMeshes(gun, BotAssets.Overlay(enemy, outlines, rim: false));

        // Muzzle flash (after the gun materials so it doesn't get the enemy highlight).
        muzzle = gun.GetNodeOrNull<Node3D>("Muzzle");
        if (muzzle != null)
        {
            flashLight = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.4f), LightEnergy = 3.5f, OmniRange = 4.5f, ShadowEnabled = false, Visible = false };
            muzzle.AddChild(flashLight);
            flashQuad = new MeshInstance3D { Mesh = BotAssets.FlashMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, Position = new Vector3(0, 0, -0.06f) };
            muzzle.AddChild(flashQuad);
        }

        // Procedural overrides + final-pose capture run as the skeleton's last modifier.
        skel.AddChild(new BotPoseModifier { Name = "BotPose", Owner_ = this });

        stepPlayer = new AudioStreamPlayer3D
        {
            Name = "Steps",
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            MaxDistance = 0,
            PanningStrength = 0.85f,
            Position = new Vector3(0, 0.1f, 0),
        };
        AddChild(stepPlayer);
    }

    void SetupGunMeshes(Node n, ShaderMaterial overlay)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is MeshInstance3D mi) SetupMesh(mi, overlay, gun: true);
            SetupGunMeshes(c, overlay);
        }
    }

    void SetupMesh(MeshInstance3D mi, ShaderMaterial overlay, bool gun)
    {
        if (mi.Mesh == null) return;
        string n = mi.Name;
        bool tiny = n.Contains("Eye"); // eyes / eyebrows sit inside the head silhouette
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
        {
            var m = mi.GetActiveMaterial(i);
            if (m != null) mi.SetSurfaceOverrideMaterial(i, BotAssets.Base(m, outlines, gun));
        }
        if (tiny) return;
        mi.MaterialOverlay = overlay;
        highlighted.Add(mi);
    }

    public override void _Ready()
    {
        bool female = BotAssets.IsFemale(variant);
        cal = BotAssets.Cal[female ? 1 : 0] ??= Calibrate();
        model.Scale = Vector3.One * cal.Scale;
        crouchW = Crouched ? cal.CrouchAmount : 0f;
        ApplyParams(0f, 1f, 1f, 1f);
        tree.Active = true;
        tree.Advance(0);
        // Valid Head / hitboxes before the first skeleton update (modes read Head right after SpawnBot). Same
        // overrides as the pose modifier, undone afterwards so the mixer never sees modified bones.
        var q0 = skel.GetBonePoseRotation(BotAssets.BSpine);
        var q1 = skel.GetBonePoseRotation(BotAssets.BChest);
        var q2 = skel.GetBonePoseRotation(BotAssets.BUpperChest);
        ApplyOverrides(Crouched ? 1f : 0f);
        CapturePose();
        skel.SetBonePoseRotation(BotAssets.BSpine, q0);
        skel.SetBonePoseRotation(BotAssets.BChest, q1);
        skel.SetBonePoseRotation(BotAssets.BUpperChest, q2);
    }

    /// <summary>Measures this body (once per male/female rig): model scale so the standing head-hitbox centre is at
    /// eye height, and the crouch blend that puts the head 0.5 m lower.</summary>
    BotAssets.BodyCal Calibrate()
    {
        var c = new BotAssets.BodyCal();
        tree.Active = true;
        // Average head height over the idle loops (both Idle and Pistol_Idle breathe a little).
        float HeadY(float crouch)
        {
            tree.Set(BotAssets.P.Move, 0f);
            tree.Set(BotAssets.P.CrouchMove, 0f);
            tree.Set(BotAssets.P.Crouch, crouch);
            tree.Set(BotAssets.P.Upper, 1f);
            tree.Set(BotAssets.P.Die, 0f);
            float sum = 0f;
            const int n = 10;
            for (int i = 0; i < n; i++)
            {
                tree.Advance(i == 0 ? 0 : 0.25);
                // Measure with the procedural overrides (they level the rifle, which lifts a leaning crouch), then
                // undo them: the mixer must never see modified bones.
                var q0 = skel.GetBonePoseRotation(BotAssets.BSpine);
                var q1 = skel.GetBonePoseRotation(BotAssets.BChest);
                var q2 = skel.GetBonePoseRotation(BotAssets.BUpperChest);
                ApplyOverrides(crouch > 0f ? 1f : 0f);
                sum += (skelInModel * (skel.GetBoneGlobalPose(BotAssets.BHead) * BotAssets.HeadOffset)).Y;
                skel.SetBonePoseRotation(BotAssets.BSpine, q0);
                skel.SetBonePoseRotation(BotAssets.BChest, q1);
                skel.SetBonePoseRotation(BotAssets.BUpperChest, q2);
            }
            return sum / n;
        }
        float stand = HeadY(0f);
        c.HipsY = (skelInModel * skel.GetBoneGlobalPose(BotAssets.BHips).Origin).Y;
        c.Scale = stand > 0.5f ? PlayerView.EyeHeight / stand : 1f;
        float target = (PlayerView.EyeHeight - 0.5f) / c.Scale;
        if (HeadY(1f) >= target) c.CrouchAmount = 1f;
        else
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (HeadY(mid) > target) lo = mid; else hi = mid;
            }
            c.CrouchAmount = (lo + hi) * 0.5f;
        }
        GD.Print($"[BotCharacter] calibrated {(BotAssets.IsFemale(variant) ? "female" : "male")}: scale {c.Scale:0.000}, crouch {c.CrouchAmount:0.00}");
        return c;
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Ray against head sphere / torso / arm / leg capsules (world space). Returns the closest zone.</summary>
    public HitZone Raycast(Vector3 o, Vector3 d, out float dist)
    {
        dist = float.PositiveInfinity;
        if (Dead || !posed) return HitZone.None;
        var inv = GlobalTransform.AffineInverse();
        var lo = inv * o;
        var ld = (inv.Basis * d).Normalized();
        if (float.IsInfinity(BotHitMath.Sphere(lo, ld, (pts[PHips] + pts[PNeck]) * 0.5f, 1.35f))) return HitZone.None;

        var zone = HitZone.None;
        float t = BotHitMath.Sphere(lo, ld, pts[PHead], HeadRadius);
        if (t < dist) { dist = t; zone = HitZone.Head; }

        // Torso: hips → base of the neck (capsule ends are rounded, so pull them in by the radius).
        var axis = pts[PNeck] - pts[PHips];
        float len = axis.Length();
        var dir = len > 1e-4f ? axis / len : Vector3.Up;
        var ta = pts[PHips] + dir * Mathf.Min(TorsoR, len * 0.5f);
        var tb = pts[PNeck] - dir * Mathf.Min(TorsoR, len * 0.5f);
        Test(BotHitMath.Capsule(lo, ld, ta, tb, TorsoR), HitZone.Body, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PLUA], pts[PLLA], UpperArmR), HitZone.Body, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PLLA], pts[PLH], LowerArmR), HitZone.Body, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PRUA], pts[PRLA], UpperArmR), HitZone.Body, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PRLA], pts[PRH], LowerArmR), HitZone.Body, ref dist, ref zone);

        Test(BotHitMath.Capsule(lo, ld, pts[PLUL], pts[PLLL], ThighR), HitZone.Legs, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PLLL], pts[PLF], ShinR), HitZone.Legs, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PLF], pts[PLT], FootR), HitZone.Legs, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PRUL], pts[PRLL], ThighR), HitZone.Legs, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PRLL], pts[PRF], ShinR), HitZone.Legs, ref dist, ref zone);
        Test(BotHitMath.Capsule(lo, ld, pts[PRF], pts[PRT], FootR), HitZone.Legs, ref dist, ref zone);
        return zone;
    }

    static void Test(float t, HitZone z, ref float dist, ref HitZone zone)
    {
        if (t < dist) { dist = t; zone = z; }
    }

    public void OnHit(HitZone z)
    {
        flash = 1f;
        if (z != HitZone.Legs) flinchT = 0.4f;
        if (Dead || !IsInsideTree()) return;
        if (z == HitZone.Head) tree.Set(BotAssets.P.HitHead, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        else if (z == HitZone.Body) tree.Set(BotAssets.P.HitChest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    public void Die(Vector3 fromDir)
    {
        if (deathT >= 0) return;
        deathT = 0;
        if (Hp > 0) Hp = 0;
        // Death01 falls backwards: turn the body so it falls along the bullet's direction.
        var away = new Vector3(fromDir.X, 0, fromDir.Z);
        if (away.LengthSquared() < 1e-6f) away = -GlobalBasis.Z;
        var local = GlobalBasis.Inverse() * (-away.Normalized());
        deathYaw = Mathf.Atan2(local.X, local.Z);
        if (IsInsideTree()) tree.Set(BotAssets.P.DeathSeek, 0.0);
        stepPlayer?.Stop();
    }

    /// <summary>Muzzle flash + tracer toward <paramref name="target"/> when the bot fires.</summary>
    public void ShootFx(Vector3 target)
    {
        if (!IsInsideTree()) return;
        var from = MuzzlePosition;
        Effects.Tracer(from, target);
        recoil = 1f;
        var to = target - Head;
        aimPitch = Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
        aimHold = 1.2f;
        if (flashLight == null || flashQuad == null) return;
        flashT = 0.045f;
        flashLight.Visible = true;
        flashQuad.Visible = true;
        flashQuad.Scale = Vector3.One * (0.8f + 0.4f * Random.Shared.NextSingle());
        flashQuad.SetInstanceShaderParameter(SeedParam, Random.Shared.NextSingle());
    }

    // ---------------------------------------------------------------- per frame

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        if (Dead && deathT < 0) Die(-GlobalBasis.Z); // fall backwards in place

        // --- locomotion state from velocity relative to facing (model space: +Z = forward, +X = character's left)
        var hv = new Vector3(Velocity.X, 0, Velocity.Z);
        float speed = deathT >= 0 ? 0f : hv.Length();
        float targetYaw = 0f, dirSign = 1f;
        if (speed > 0.2f)
        {
            var lv = Basis.Inverse() * hv;
            float phi = Mathf.Atan2(lv.X, lv.Z);
            float aphi = Mathf.Abs(phi);
            if (backwards) { if (aphi < 1.40f) backwards = false; }   // < 80°
            else if (aphi > 1.75f) backwards = true;                  // > 100°
            float legPhi = backwards ? Mathf.Wrap(phi - Mathf.Pi, -Mathf.Pi, Mathf.Pi) : phi;
            targetYaw = Mathf.Clamp(legPhi, -MaxLegYaw, MaxLegYaw);
            dirSign = backwards ? -1f : 1f;
        }
        if (deathT >= 0) targetYaw = deathYaw;
        float k = 1f - Mathf.Exp(-(deathT >= 0 ? 14f : 10f) * dt);
        legYaw = Mathf.LerpAngle(legYaw, targetYaw, k);
        pivot.Rotation = new Vector3(0, legYaw, 0);

        moveW = Mathf.Lerp(moveW, Smooth(0.15f, 0.9f, speed), 1f - Mathf.Exp(-12f * dt));
        gaitW = Mathf.Lerp(gaitW, Smooth(1.6f, 3.4f, speed), 1f - Mathf.Exp(-10f * dt));
        crouchW = Mathf.Lerp(crouchW, Crouched ? cal!.CrouchAmount : 0f, 1f - Mathf.Exp(-10f * dt));
        if (deathT >= 0) dieW = Mathf.Min(1f, dieW + dt / 0.12f);

        float walkTs = Mathf.Clamp(speed / 1.05f, 0.7f, 2.0f) * dirSign;
        float runTs = Mathf.Clamp(speed / 5.9f, 0.62f, 1.25f) * dirSign;
        float cwalkTs = Mathf.Clamp(speed / 0.6f, 0.8f, 2.4f) * dirSign;
        ApplyParams(walkTs, runTs, cwalkTs, 1f);
        tree.Advance(dt);

        // --- effects
        recoil = Mathf.Max(0f, recoil - dt / 0.09f);
        if (AimTarget is { } at)
        {
            var to = at - Head;
            aimPitch = Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
            aimHold = Mathf.Max(aimHold, 0.1f);
        }
        aimHold -= dt;
        aimW = Mathf.MoveToward(aimW, aimHold > 0 ? 1f : 0f, dt / 0.25f);
        if (flashT > 0)
        {
            flashT -= dt;
            if (flashT <= 0 && flashLight != null && flashQuad != null) { flashLight.Visible = false; flashQuad.Visible = false; }
        }
        flash = Mathf.Max(0f, flash - dt / 0.1f);
        flinchT = Mathf.Max(0f, flinchT - dt);
        if (deathT >= 0) fade = Mathf.Max(0f, 1f - deathT / 0.45f);
        UpdateHighlight();
        Footsteps(dt, speed);

        if (deathT >= 0)
        {
            deathT += dt;
            if (deathT > 1.5f) model.Position = new Vector3(0, -0.75f * Smooth(1.5f, 2.3f, deathT), 0);
            if (deathT > 2.3f) Visible = false;
            if (deathT > 2.6f) QueueFree();
        }
    }

    void ApplyParams(float walkTs, float runTs, float cwalkTs, float upper)
    {
        tree.Set(BotAssets.P.WalkScale, walkTs);
        tree.Set(BotAssets.P.RunScale, runTs);
        tree.Set(BotAssets.P.CrouchWalkScale, cwalkTs);
        tree.Set(BotAssets.P.Gait, gaitW);
        tree.Set(BotAssets.P.Move, moveW);
        tree.Set(BotAssets.P.CrouchMove, moveW);
        tree.Set(BotAssets.P.Crouch, cal!.CrouchAmount > 0 ? crouchW : 0f);
        tree.Set(BotAssets.P.Upper, upper);
        tree.Set(BotAssets.P.Die, dieW);
    }

    /// <summary>Called by <see cref="BotPoseModifier"/> during the skeleton update, after the animation was applied.</summary>
    internal void OnSkeletonModify()
    {
        if (!IsInsideTree() || cal == null) return; // not set up yet (_Ready calibrates)
        ApplyOverrides(cal.CrouchAmount > 0 ? Mathf.Clamp(crouchW / cal.CrouchAmount, 0f, 1f) : 0f);
        CapturePose();
    }

    /// <summary>Bone overrides after the animation: legs yaw toward the movement, the spine untwists so chest, gun and
    /// head face FacingYaw; small recoil kick when firing.</summary>
    void ApplyOverrides(float crouchFrac)
    {
        float alive = 1f - dieW;
        if (alive > 0.001f)
        {
            // Jog_Fwd bounces the hips 24 cm per stride: damp it (legs re-solved so the feet stay planted) so a
            // running bot's head stays near eye level like VALORANT's.
            float runDamp = 0.8f * gaitW * moveW * (1f - crouchFrac) * alive;
            if (runDamp > 0.01f && cal != null) StabilizeHips(cal.HipsY * 0.99f, runDamp);

            // Keep the torso upright over the hips when standing / walking / running (Jog_Fwd leans ~20° and would
            // drop the head 15-20 cm below eye level, which defeats crosshair-placement practice). Crouch keeps its lean.
            // Let a hit flinch (Hit_Chest / Hit_Head on the spine) show instead of correcting it away.
            float hold = 1f - 0.85f * Smooth(0f, 0.12f, flinchT);
            float upright = (1f - crouchFrac) * alive * hold;
            if (upright > 0.001f)
            {
                var torso = skel.GetBoneGlobalPose(BotAssets.BNeck).Origin - skel.GetBoneGlobalPose(BotAssets.BSpine).Origin;
                if (torso.LengthSquared() > 1e-6f)
                {
                    var fix = new Quaternion(torso.Normalized(), Vector3.Up);
                    RotateBoneGlobal(BotAssets.BSpine, Quaternion.Identity.Slerp(fix, upright));
                }
            }

            float twist = -legYaw * alive;
            if (Mathf.Abs(twist) > 1e-4f)
            {
                RotateBone(BotAssets.BSpine, Vector3.Up, twist * 0.3f);
                RotateBone(BotAssets.BChest, Vector3.Up, twist * 0.3f);
                RotateBone(BotAssets.BUpperChest, Vector3.Up, twist * 0.4f);
            }

            // Aim: rotate the upper chest so the rifle points exactly along FacingYaw, level (or at the last shot's
            // target for a moment after firing, so bots on high/low ground aim at you).
            var legBasis = new Basis(Vector3.Up, legYaw);        // skeleton frame -> this node's frame
            var right = legBasis.Inverse() * Vector3.Right;       // facing frame's pitch axis, in skeleton space
            var hand = skel.GetBoneGlobalPose(BotAssets.BRHand).Basis.Orthonormalized() * BotAssets.RifleInHand.Basis;
            var barrel = (legBasis * -hand.Z).Normalized();
            float yawErr = Mathf.Atan2(barrel.X, barrel.Z);
            RotateBone(BotAssets.BUpperChest, Vector3.Up, Mathf.Clamp(-yawErr, -0.8f, 0.8f) * alive * hold);
            float pitchErr = aimPitch * aimW - Mathf.Asin(Mathf.Clamp(barrel.Y, -1f, 1f));
            // Rotating about +X tips +Z downwards, so raise the barrel with a negative angle; recoil kicks it up.
            RotateBone(BotAssets.BUpperChest, right, (-Mathf.Clamp(pitchErr, -0.6f, 0.6f) * hold - 0.06f * recoil) * alive);
        }
    }

    /// <summary>Hitbox points (this node's local space) and rifle mount from the current (final) skeleton pose.</summary>
    void CapturePose()
    {
        gunMount.Transform = skel.GetBoneGlobalPose(BotAssets.BRHand);

        var m = pivot.Transform * model.Transform * skelInModel;
        Vector3 B(int bone) => m * skel.GetBoneGlobalPose(bone).Origin;
        pts[PHead] = m * (skel.GetBoneGlobalPose(BotAssets.BHead) * BotAssets.HeadOffset);
        pts[PHips] = B(BotAssets.BHips);
        pts[PNeck] = B(BotAssets.BNeck);
        pts[PLUA] = B(BotAssets.BLUpperArm); pts[PLLA] = B(BotAssets.BLLowerArm); pts[PLH] = B(BotAssets.BLHand);
        pts[PRUA] = B(BotAssets.BRUpperArm); pts[PRLA] = B(BotAssets.BRLowerArm); pts[PRH] = B(BotAssets.BRHand);
        pts[PLUL] = B(BotAssets.BLUpperLeg); pts[PLLL] = B(BotAssets.BLLowerLeg); pts[PLF] = B(BotAssets.BLFoot); pts[PLT] = B(BotAssets.BLToes);
        pts[PRUL] = B(BotAssets.BRUpperLeg); pts[PRLL] = B(BotAssets.BRLowerLeg); pts[PRF] = B(BotAssets.BRFoot); pts[PRT] = B(BotAssets.BRToes);
        posed = true;
    }

    /// <summary>Moves the hips toward <paramref name="refY"/> (skeleton space) and re-solves both legs (two-bone IK)
    /// so each foot keeps its animated position and orientation.</summary>
    void StabilizeHips(float refY, float damp)
    {
        var hipsG = skel.GetBoneGlobalPose(BotAssets.BHips);
        float dy = (refY - hipsG.Origin.Y) * damp;
        if (Mathf.Abs(dy) < 0.002f) return;
        CaptureLeg(0, BotAssets.BLUpperLeg, BotAssets.BLLowerLeg, BotAssets.BLFoot);
        CaptureLeg(1, BotAssets.BRUpperLeg, BotAssets.BRLowerLeg, BotAssets.BRFoot);
        int parent = skel.GetBoneParent(BotAssets.BHips);
        var pb = parent >= 0 ? skel.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        skel.SetBonePosePosition(BotAssets.BHips, skel.GetBonePosePosition(BotAssets.BHips) + pb.Inverse() * new Vector3(0, dy, 0));
        SolveLeg(0, BotAssets.BLUpperLeg, BotAssets.BLLowerLeg, BotAssets.BLFoot);
        SolveLeg(1, BotAssets.BRUpperLeg, BotAssets.BRLowerLeg, BotAssets.BRFoot);
    }

    readonly Vector3[] legH = new Vector3[2], legK = new Vector3[2], legF = new Vector3[2];
    readonly Quaternion[] legFootRot = new Quaternion[2];

    void CaptureLeg(int i, int up, int low, int foot)
    {
        legH[i] = skel.GetBoneGlobalPose(up).Origin;
        legK[i] = skel.GetBoneGlobalPose(low).Origin;
        var f = skel.GetBoneGlobalPose(foot);
        legF[i] = f.Origin;
        legFootRot[i] = f.Basis.GetRotationQuaternion();
    }

    void SolveLeg(int i, int up, int low, int foot)
    {
        var h = skel.GetBoneGlobalPose(up).Origin;
        float a = (legK[i] - legH[i]).Length(), b = (legF[i] - legK[i]).Length();
        var toF = legF[i] - h;
        float len = toF.Length();
        if (a < 1e-3f || b < 1e-3f || len < 1e-3f) return;
        var dir = toF / len;
        float d = Mathf.Clamp(len, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        // Keep the knee in its animated bend plane.
        var off = legK[i] - legH[i];
        var bend = off - dir * off.Dot(dir);
        if (bend.LengthSquared() < 1e-8f) return;
        bend = bend.Normalized();
        float x = (a * a - b * b + d * d) / (2f * d);
        var knee = h + dir * x + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
        var kneeNow = skel.GetBoneGlobalPose(low).Origin;
        RotateBoneGlobal(up, new Quaternion((kneeNow - h).Normalized(), (knee - h).Normalized()));
        kneeNow = skel.GetBoneGlobalPose(low).Origin;
        var footNow = skel.GetBoneGlobalPose(foot).Origin;
        RotateBoneGlobal(low, new Quaternion((footNow - kneeNow).Normalized(), (legF[i] - kneeNow).Normalized()));
        var lowQ = skel.GetBoneGlobalPose(low).Basis.GetRotationQuaternion();
        skel.SetBonePoseRotation(foot, (lowQ.Inverse() * legFootRot[i]).Normalized());
    }

    /// <summary>Applies a rotation given in skeleton space to a bone (about its own joint).</summary>
    void RotateBoneGlobal(int bone, Quaternion r)
    {
        int parent = skel.GetBoneParent(bone);
        var pq = parent >= 0 ? skel.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion() : Quaternion.Identity;
        skel.SetBonePoseRotation(bone, (pq.Inverse() * r * pq * skel.GetBonePoseRotation(bone)).Normalized());
    }

    /// <summary>Rotates a bone by <paramref name="angle"/> about an axis given in skeleton space (applied after its parent).</summary>
    void RotateBone(int bone, Vector3 skelAxis, float angle)
    {
        int parent = skel.GetBoneParent(bone);
        var pq = parent >= 0 ? skel.GetBoneGlobalPose(parent).Basis.GetRotationQuaternion() : Quaternion.Identity;
        var axis = (pq.Inverse() * skelAxis).Normalized();
        skel.SetBonePoseRotation(bone, (new Quaternion(axis, angle) * skel.GetBonePoseRotation(bone)).Normalized());
    }

    void UpdateHighlight()
    {
        if (Mathf.IsEqualApprox(flash, lastFlash) && Mathf.IsEqualApprox(fade, lastFade)) return;
        bool fl = !Mathf.IsEqualApprox(flash, lastFlash), fa = !Mathf.IsEqualApprox(fade, lastFade);
        lastFlash = flash; lastFade = fade;
        foreach (var g in highlighted)
        {
            if (fl) g.SetInstanceShaderParameter(FlashParam, flash);
            if (fa) g.SetInstanceShaderParameter(FadeParam, fade);
        }
    }

    void Footsteps(float dt, float speed)
    {
        if (deathT >= 0 || Crouched || speed <= RunStepSpeed || stepPlayer == null) { stepT = 0.06f; return; }
        stepT -= dt;
        if (stepT > 0) return;
        stepT += StepInterval;
        var steps = BotAssets.Steps;
        var cam = GetViewport()?.GetCamera3D();
        if (steps.Length == 0 || cam == null) return;
        // VALORANT-like flat distance curve (same shape as Sfx.PlayAt): floor until ~35 m, then fade out.
        const float maxRange = 38f, floor = 0.3f;
        float d = GlobalPosition.DistanceTo(cam.GlobalPosition);
        if (d > maxRange) return;
        float kd = Mathf.Clamp((d - 2f) / (maxRange - 2f), 0f, 1f);
        float g = (floor + (1 - floor) * (1 - kd) * (1 - kd)) * (kd < 0.9f ? 1f : (1 - kd) / 0.1f);
        stepPlayer.Stream = steps[Random.Shared.Next(steps.Length)];
        stepPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * 1.1f * g));
        stepPlayer.PitchScale = 0.92f + 0.16f * Random.Shared.NextSingle();
        stepPlayer.Play();
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
