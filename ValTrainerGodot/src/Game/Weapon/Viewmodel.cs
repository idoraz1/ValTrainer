using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// First-person weapon + gloved arms, parented to the camera.
/// <para>No-clip / own FOV: every viewmodel surface uses viewmodel.gdshader, which re-projects the mesh with
/// <see cref="AppSettings.ViewmodelFov"/> (vertical degrees) and squeezes its depth next to the near plane, so it
/// never goes through walls and keeps its size when the world camera zooms. It is lit by the scene normally
/// (directional light + sky ambient + muzzle-flash omni light), casts no shadows and sits on its own visual
/// layer (<see cref="VisualLayer"/>) so bullet-hole decals never project onto it.</para>
/// <para>Animation is procedural: recoil spring, idle breathing, mouse-look lag, movement bob, crouch, ADS,
/// reload (tilt, mag out/in with a spare magazine), Operator bolt cycle, equip raise, shell ejection.</para>
/// </summary>
public partial class Viewmodel : Node3D
{
    /// <summary>Visual layer 20 — viewmodel only (decals use cull mask 1, so they skip it).</summary>
    public const uint VisualLayer = 1u << 19;

    WeaponKind kind;
    /// <summary>The gun actually held: Chamber's guns use their family's model (<see cref="WeaponDef.Family"/>) in gold.</summary>
    WeaponKind actual;
    bool left;
    VmPose pose = null!;
    readonly VmMaterials mats = new();
    Node3D pivot = null!, gun = null!;
    Node3D? muzzle;
    MeshInstance3D gripHand = null!, supportHand = null!, spareMag = null!;
    MuzzleFlash flash = null!;
    readonly List<ShaderMaterial> gunMats = new();
    Transform3D gripRest, supportRest, boltPose;
    readonly Random rng = new();

    // inputs from the session
    float adsIn, reloadIn = -1f, speedIn;
    bool crouchIn;

    // animation state
    float time, equipT, bobPhase, bobAmp, crouchK, adsK, reloadW, lastFov = -1f;
    Vector3 kPos, kPosV, kRot, kRotV;
    Vector2 sway, swayV;
    float lastYaw, lastPitch;
    bool haveLook;
    float boltT = -1f;
    bool magHidden, reloadBolted;

    sealed class Shell { public MeshInstance3D Node = null!; public Vector3 Vel, AngVel; public float Age = 99f; }
    readonly List<Shell> shells = new();
    int nextShell;

    public static Viewmodel Create(WeaponKind kind, bool leftHanded)
    {
        VmTest.Init();
        if (VmTest.Kind is { } k) kind = k;
        if (VmTest.ForceLeft is { } l) leftHanded = l;
        var v = new Viewmodel { kind = Weapons.FamilyOf(kind), actual = kind, left = leftHanded, Name = "Viewmodel" };
        v.Build();
        return v;
    }

    // ---------------- public API ----------------

    /// <summary>A shot was fired (recoil kick + muzzle flash + shell).</summary>
    public void OnFire()
    {
        flash.Fire();
        float s = Mathf.Lerp(1f, 0.38f, adsK);
        float w = pose.KickFreq * 2.1f;
        kPosV += new Vector3(R(-0.12f, 0.12f), R(0.15f, 0.35f), 1f) * pose.KickBack * w * s;
        kRotV += new Vector3(pose.KickPitch * R(0.85f, 1.1f), pose.KickPitch * R(-0.25f, 0.25f), pose.KickPitch * R(-0.4f, 0.4f)) * w * Mathf.Lerp(1f, 0.25f, adsK);
        if (actual == WeaponKind.Operator) boltT = 0f;
        else if (pose.Shells) EjectShell();
    }

    /// <summary>0 = hip, 1 = fully aimed down sights / scoped.</summary>
    public void SetAds(float t) => adsIn = Mathf.Clamp(t, 0f, 1f);
    /// <summary>Reload progress 0..1, or -1 when not reloading.</summary>
    public void SetReload(float progress) => reloadIn = progress;
    /// <summary>Movement for bob/sway.</summary>
    public void SetMotion(float speed, bool crouching) { speedIn = speed; crouchIn = crouching; }

    /// <summary>
    /// World-space point for tracers: the muzzle's screen position under the viewmodel projection, pulled
    /// to the matching world point for the main camera (so the tracer starts exactly at the drawn barrel tip,
    /// even when zoomed).
    /// </summary>
    public Vector3 MuzzleWorld
    {
        get
        {
            var cam = GetParent() as Camera3D ?? GetViewport()?.GetCamera3D();
            if (muzzle == null || cam == null) return GlobalPosition;
            var ct = cam.GlobalTransform;
            var p = ct.AffineInverse() * muzzle.GlobalPosition;
            float s = mats.Focal / MainFocalY(cam);
            return ct * new Vector3(p.X * s, p.Y * s, p.Z);
        }
    }

    float MainFocalY(Camera3D cam)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float aspect = size.X / Mathf.Max(1f, size.Y);
        float f = 1f / Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2f);
        return cam.KeepAspect == Camera3D.KeepAspectEnum.Width ? f * aspect : f;
    }

    // ---------------- build ----------------

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    Transform3D X(Transform3D t) => left ? VmMesh.Mirror(t) : t;
    Vector3 XV(Vector3 v) => left ? new Vector3(-v.X, v.Y, v.Z) : v;

    static void Configure(GeometryInstance3D g)
    {
        g.Layers = VisualLayer;
        g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        g.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        g.ExtraCullMargin = 2f;
    }

    void Build()
    {
        pose = VmPose.For(kind);
        lastFov = Main.I?.Settings?.ViewmodelFov ?? 70f;
        mats.SetFov(lastFov);
        mats.Gold = actual != kind; // Chamber's custom guns: gold finish on the family model

        pivot = new Node3D { Name = "Pivot" };
        AddChild(pivot);
        gun = GD.Load<PackedScene>(pose.Scene).Instantiate<Node3D>();
        gun.Name = "Gun";
        pivot.AddChild(gun);
        muzzle = gun.GetNodeOrNull<Node3D>("Muzzle");

        // Re-material the gun (flat FBX colours → metal / polymer).
        var clipMin = pose.MagWell + new Vector3(-0.06f, -0.4f, -pose.MagHalf.Z - 0.012f - (pose.MagCurve > 1f ? 0.07f : 0f));
        var clipMax = pose.MagWell + new Vector3(0.06f, -0.004f, pose.MagHalf.Z + 0.012f);
        foreach (var mi in FindMeshes(gun))
        {
            Configure(mi);
            var toMesh = (gun.GlobalTransformOrLocal().AffineInverse() * mi.GlobalTransformOrLocal()).AffineInverse();
            var bb = new Aabb(toMesh * clipMin, Vector3.Zero).Expand(toMesh * clipMax)
                .Expand(toMesh * new Vector3(clipMin.X, clipMin.Y, clipMax.Z)).Expand(toMesh * new Vector3(clipMax.X, clipMax.Y, clipMin.Z));
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.Mesh.SurfaceGetMaterial(i);
                string name = src?.ResourceName ?? "";
                var col = src is BaseMaterial3D bm ? bm.AlbedoColor : new Color(0.2f, 0.2f, 0.2f);
                var m = mats.ForGunSurface(name, col);
                m.SetShaderParameter("clip_min", bb.Position);
                m.SetShaderParameter("clip_max", bb.End);
                gunMats.Add(m);
                mi.SetSurfaceOverrideMaterial(i, m);
            }
        }

        // Gloves + sleeves.
        var glove = mats.Make(new Color(0.05f, 0.052f, 0.058f), 0f, 0.8f, 0.4f, 0.7f, 0.25f, 420f);
        var pad = mats.Make(new Color(0.13f, 0.135f, 0.14f), 0.1f, 0.5f, 0.5f, 0.4f, 0.2f, 200f);
        var sleeve = mats.Make(new Color(0.085f, 0.1f, 0.125f), 0f, 0.85f, 0.35f, 0.6f, 0.2f, 260f);
        var accent = mats.Make(new Color(0.55f, 0.13f, 0.12f), 0f, 0.6f, 0.5f, 0.3f, 0.15f, 200f);
        var se = pose.Support.ElbowLocal;
        gripHand = new MeshInstance3D { Name = "GripHand", Mesh = VmHands.Build(pose.Grip.Shape, left, pose.Grip.ElbowLocal, glove, pad, sleeve, accent) };
        supportHand = new MeshInstance3D { Name = "SupportHand", Mesh = VmHands.Build(pose.Support.Shape, !left, new Vector3(-se.X, se.Y, se.Z), glove, pad, sleeve, accent) };
        gripRest = X(pose.Grip.Xform);
        supportRest = X(pose.Support.Xform);
        boltPose = pose.Bolt != null ? X(pose.Bolt.Xform) : gripRest;
        gripHand.Transform = gripRest;
        supportHand.Transform = supportRest;
        foreach (var h in new[] { gripHand, supportHand }) { Configure(h); gun.AddChild(h); h.Visible = !VmTest.NoHands; }

        // Spare magazine for the reload.
        var magBody = mats.Make(new Color(0.07f, 0.072f, 0.078f), 0.2f, 0.5f, 0.5f, 0.5f, 0.15f);
        var magPlate = mats.Make(new Color(0.16f, 0.16f, 0.17f), 0.7f, 0.4f);
        spareMag = new MeshInstance3D { Name = "SpareMag", Mesh = VmHands.Magazine(pose.MagHalf, pose.MagCurve, magBody, magPlate), Visible = false };
        Configure(spareMag);
        gun.AddChild(spareMag);

        // Muzzle flash.
        flash = MuzzleFlash.Create(mats, pose.FlashScale, VisualLayer);
        flash.Hold = VmTest.HoldFlash;
        (muzzle ?? gun).AddChild(flash);

        // Shell pool.
        if (pose.Shells || kind == WeaponKind.Operator)
        {
            var brass = mats.Make(new Color(0.8f, 0.6f, 0.28f), 1f, 0.3f, 0.5f, 0.2f, 0.1f);
            var primer = mats.Make(new Color(0.6f, 0.5f, 0.4f), 1f, 0.35f);
            var mesh = VmHands.Shell(pose.ShellLen * 0.12f, pose.ShellLen * 0.85f, brass, primer);
            for (int i = 0; i < 6; i++)
            {
                var n = new MeshInstance3D { Mesh = mesh, Visible = false };
                Configure(n);
                AddChild(n);
                shells.Add(new Shell { Node = n });
            }
        }

        if (VmTest.Enabled) VmTest.Attach(this);
        UpdatePose(0f);
    }

    static IEnumerable<MeshInstance3D> FindMeshes(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var m in FindMeshes(c)) yield return m;
    }

    // ---------------- animation ----------------

    public override void _Ready() => Fx.Effects.Prewarm();

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);
        time += dt;
        if (VmTest.Enabled) VmTest.Tick(this, dt);

        float fov = Main.I?.Settings?.ViewmodelFov ?? 70f;
        if (fov != lastFov) { lastFov = fov; mats.SetFov(fov); }

        UpdatePose(dt);
        UpdateShells(dt);
        mats.SetFlash(flash.Fill * (Visible ? 1f : 0f));
    }

    void UpdatePose(float dt)
    {
        float ads = VmTest.Ads ?? adsIn;
        float reload = VmTest.Reload ?? reloadIn;
        float speed = VmTest.Speed ?? speedIn;
        bool crouch = VmTest.Crouch || crouchIn;

        equipT += dt;
        float e = Mathf.Clamp(equipT / pose.Equip, 0f, 1f);
        float eo = Mathf.Pow(1f - e, 3f);
        adsK = Smooth(ads);
        crouchK = Mathf.Lerp(crouchK, crouch ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
        float hip = 1f - adsK;

        // Headhunter is the only aimed pistol: sit its sights a little lower so the slide doesn't hide a head-height target.
        var adsPos = actual == WeaponKind.Headhunter ? pose.AdsPos + new Vector3(0f, -0.032f, 0.02f) : pose.AdsPos;
        var pos = pose.HipPos.Lerp(adsPos, adsK);
        var rot = pose.HipRot.Lerp(pose.AdsRot, adsK);

        // Crouch: a little lower and canted in.
        pos += new Vector3(-0.006f, -0.013f, 0.004f) * crouchK * hip;
        rot.Z += 3.5f * crouchK * hip;

        // Idle breathing.
        float br = 1f - 0.85f * adsK;
        pos += new Vector3(Mathf.Sin(time * 0.83f) * 0.0011f, Mathf.Sin(time * 1.7f) * 0.0016f, 0f) * br;
        rot += new Vector3(Mathf.Sin(time * 1.7f + 0.6f) * 0.22f, Mathf.Sin(time * 0.71f) * 0.12f, Mathf.Sin(time * 0.6f) * 0.25f) * br;

        // Movement bob (distance-driven, none when still).
        float targetAmp = Mathf.Clamp(speed / 5.4f, 0f, 1.3f);
        bobAmp = Mathf.Lerp(bobAmp, targetAmp, 1f - Mathf.Exp(-8f * dt));
        bobPhase += dt * speed * 4.2f;
        float ba = bobAmp * (1f - 0.75f * adsK);
        float sp = Mathf.Sin(bobPhase);
        pos += new Vector3(sp * 0.0075f, -(1f - Mathf.Cos(2f * bobPhase)) * 0.5f * 0.0055f - 0.004f, 0.006f) * ba;
        rot += new Vector3(Mathf.Sin(2f * bobPhase + 0.4f) * 0.5f, sp * 0.5f, sp * 1.1f) * ba;

        // Mouse-look lag.
        UpdateLookSway(dt);
        float sw = 1f - 0.7f * adsK;
        rot += new Vector3(sway.Y, sway.X, sway.X * 0.6f) * sw;
        pos += new Vector3(-sway.X * 0.0011f, sway.Y * 0.0008f, 0f) * sw;

        // Recoil spring (sub-stepped for stability at low FPS).
        float w = pose.KickFreq, z = 0.62f;
        float rest = dt;
        while (rest > 0f)
        {
            float h = Mathf.Min(rest, 1f / 240f);
            rest -= h;
            kPosV += (-w * w * kPos - 2f * z * w * kPosV) * h; kPos += kPosV * h;
            kRotV += (-w * w * kRot - 2f * z * w * kRotV) * h; kRot += kRotV * h;
        }
        pos += kPos;
        rot += kRot;

        // Reload and bolt.
        var (rPos, rRot) = ReloadOffset(reload, dt);
        pos += rPos; rot += rRot;
        var (bRot, bHand) = BoltOffset(dt);
        rot += bRot;

        // Equip raise.
        pos += new Vector3(0.03f, -0.2f, 0.05f) * eo;
        rot += new Vector3(-55f, 10f, 30f) * eo;

        var t = new Transform3D(VmMesh.Euler(rot.X, rot.Y, rot.Z), pos);
        if (VmTest.View is { } view) t = VmTest.ViewXform(view, kind);
        pivot.Transform = X(t);

        // Hands: trigger hand may be on the bolt.
        gripHand.Transform = bHand > 0f ? gripRest.InterpolateWith(BoltHand(), bHand) : gripRest;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    void UpdateLookSway(float dt)
    {
        float yaw, pitch;
        if (GetParent()?.GetParent() is GameSession gs) { yaw = gs.View.Yaw; pitch = gs.View.Pitch; }
        else if (GetParent() is Node3D p) { var r = p.GlobalRotationDegrees; yaw = -r.Y; pitch = r.X; }
        else return;
        if (VmTest.Look) { yaw = 25f * Mathf.Sin(time * 2.2f); pitch = 6f * Mathf.Sin(time * 1.3f); }
        if (!haveLook || dt <= 0f) { lastYaw = yaw; lastPitch = pitch; haveLook = true; return; }
        float dy = Mathf.Wrap(yaw - lastYaw, -180f, 180f), dp = pitch - lastPitch;
        lastYaw = yaw; lastPitch = pitch;
        if (Mathf.Abs(dy) > 40f || Mathf.Abs(dp) > 40f) { dy = 0; dp = 0; } // teleport / respawn
        var target = new Vector2(Mathf.Clamp(dy / dt * 0.007f, -2.5f, 2.5f), Mathf.Clamp(-dp / dt * 0.007f, -1.8f, 1.8f));
        const float ws = 16f;
        swayV += ((target - sway) * ws * ws - 2f * ws * swayV) * dt;
        sway += swayV * dt;
    }

    // ---------------- reload ----------------

    /// <summary>Returns the pivot offset for the reload and animates the support hand + spare magazine.</summary>
    (Vector3, Vector3) ReloadOffset(float p, float dt)
    {
        bool active = p >= 0f;
        reloadW = Mathf.Lerp(reloadW, active ? 1f : 0f, 1f - Mathf.Exp(-14f * dt));
        if (!active)
        {
            reloadBolted = false;
            if (magHidden) SetMagHidden(false);
            spareMag.Visible = false;
            supportHand.Transform = supportRest.InterpolateWith(supportHand.Transform, reloadW);
            return (Vector3.Zero, Vector3.Zero);
        }

        float env = Smooth(Mathf.Clamp(p / 0.13f, 0f, 1f)) * (1f - Smooth(Mathf.Clamp((p - 0.82f) / 0.15f, 0f, 1f)));
        bool pistol = kind == WeaponKind.Sheriff;
        var rPos = (pistol ? new Vector3(-0.025f, 0.03f, 0.03f) : new Vector3(-0.035f, 0.012f, 0.03f)) * env;
        var rRot = (pistol ? new Vector3(18f, 10f, -18f) : new Vector3(10f, 6f, -30f)) * env;

        // Magazine path in gun space: at the well → pulled down and away → back → inserted.
        var well = pose.MagWell;
        var away = well + new Vector3(-0.08f, -0.42f, 0.12f);
        float out1 = Smooth(Mathf.Clamp((p - 0.20f) / 0.16f, 0f, 1f));
        float in1 = Smooth(Mathf.Clamp((p - 0.46f) / 0.16f, 0f, 1f));
        float m = out1 * (1f - in1);
        var magPos = well.Lerp(away, m);
        bool magOut = p >= 0.20f && p < 0.62f;
        if (magOut != magHidden) { SetMagHidden(magOut); if (!magOut && p >= 0.62f) Jolt(); }
        spareMag.Visible = magOut;
        spareMag.Transform = X(new Transform3D(VmMesh.Euler(-25f * m, 0f, 15f * m), magPos));

        // Support hand: rest → on the mag (0.06..0.18) → follows the mag → back to rest (0.64..0.78).
        var handOnMag = X(new Transform3D(VmMesh.Look(new Vector3(0.9f, 0.1f, -0.5f), new Vector3(-0.3f, -1f, 0.1f)),
            magPos + new Vector3(-0.045f, -0.035f, 0.045f)));
        float toMag = Smooth(Mathf.Clamp((p - 0.06f) / 0.12f, 0f, 1f));
        float back = Smooth(Mathf.Clamp((p - 0.64f) / 0.14f, 0f, 1f));
        var hand = supportRest.InterpolateWith(handOnMag, toMag * (1f - back));
        supportHand.Transform = supportRest.InterpolateWith(hand, reloadW);

        // Operator: cycle the bolt at the end of the reload.
        if (actual == WeaponKind.Operator && p > 0.8f && !reloadBolted) { reloadBolted = true; boltT = 0.15f; }
        return (rPos * reloadW, rRot * reloadW);
    }

    void Jolt()
    {
        kPosV += new Vector3(0f, 0.012f, -0.004f) * pose.KickFreq * 2f;
        kRotV += new Vector3(-2.5f, 0f, 1.5f) * pose.KickFreq * 2f;
    }

    void SetMagHidden(bool hide)
    {
        magHidden = hide;
        foreach (var m in gunMats) m.SetShaderParameter("clip_on", hide ? 1f : 0f);
    }

    // ---------------- Operator bolt ----------------

    /// <summary>Bolt cycle after an Operator shot: roll, hand to the bolt, pull, push, back.</summary>
    (Vector3, float) BoltOffset(float dt)
    {
        if (boltT < 0f || pose.Bolt == null) return (Vector3.Zero, 0f);
        float prev = boltT;
        boltT += dt;
        const float t0 = 0.22f, t1 = 0.36f, t2 = 0.5f, t3 = 0.64f, t4 = 0.82f;
        if (prev < t2 && boltT >= t2) EjectShell();
        if (boltT > t4 + 0.05f) { boltT = -1f; return (Vector3.Zero, 0f); }
        float roll = Smooth(Mathf.Clamp((boltT - t0 + 0.06f) / 0.14f, 0f, 1f)) * (1f - Smooth(Mathf.Clamp((boltT - t3) / (t4 - t3), 0f, 1f)));
        float hand = Smooth(Mathf.Clamp((boltT - t0) / (t1 - t0) * 1.4f, 0f, 1f)) * (1f - Smooth(Mathf.Clamp((boltT - t3) / (t4 - t3), 0f, 1f)));
        float pull = Smooth(Mathf.Clamp((boltT - t1) / (t2 - t1), 0f, 1f)) * (1f - Smooth(Mathf.Clamp((boltT - t2) / (t3 - t2), 0f, 1f)));
        boltPull = pull;
        return (new Vector3(4f, -3f, 16f) * roll, hand);
    }

    float boltPull;

    Transform3D BoltHand()
    {
        var t = boltPose;
        t.Origin += XV(new Vector3(0f, 0f, 0.075f)) * boltPull;
        return t;
    }

    // ---------------- shells ----------------

    void EjectShell()
    {
        if (shells.Count == 0) return;
        var s = shells[nextShell];
        nextShell = (nextShell + 1) % shells.Count;
        var pt = pivot.Transform;
        s.Node.Transform = new Transform3D(pt.Basis * VmMesh.Euler(0, R(70f, 110f) * (left ? -1 : 1), 0), pt * XV(pose.Eject));
        s.Vel = pt.Basis * XV(new Vector3(R(1.3f, 1.8f), R(1.1f, 1.6f), R(0.1f, 0.6f)));
        s.AngVel = new Vector3(R(-20, 20), R(-30, 30), R(-15, 15));
        s.Age = 0f;
        s.Node.Visible = true;
    }

    void UpdateShells(float dt)
    {
        foreach (var s in shells)
        {
            if (s.Age > 0.6f) { if (s.Node.Visible) s.Node.Visible = false; continue; }
            s.Age += dt;
            s.Vel += new Vector3(0, -9.8f, 0) * dt;
            var t = s.Node.Transform;
            t.Origin += s.Vel * dt;
            t.Basis = (new Basis(Quaternion.FromEuler(s.AngVel * dt)) * t.Basis).Orthonormalized();
            s.Node.Transform = t;
        }
    }

    // ---------------- dev ----------------

    internal Node3D GunNode => gun;
    /// <summary>Screen position of the drawn muzzle (viewmodel projection), for the alignment check.</summary>
    internal Vector2 MuzzleScreen()
    {
        var cam = (Camera3D)GetParent();
        var p = cam.GlobalTransform.AffineInverse() * muzzle!.GlobalPosition;
        var size = GetViewport().GetVisibleRect().Size;
        float aspect = size.X / size.Y;
        float nx = mats.Focal / aspect * p.X / -p.Z, ny = mats.Focal * p.Y / -p.Z;
        return new Vector2((nx + 1f) / 2f * size.X, (1f - ny) / 2f * size.Y);
    }
    internal MuzzleFlash Flash => flash;
    internal bool LeftLayout => left;
    internal VmMaterials Materials => mats;
    internal WeaponKind Kind => kind;
}

static class Node3DExt
{
    /// <summary>Global transform if inside the tree, otherwise the composed local chain.</summary>
    public static Transform3D GlobalTransformOrLocal(this Node3D n)
    {
        if (n.IsInsideTree()) return n.GlobalTransform;
        var t = n.Transform;
        var p = n.GetParent() as Node3D;
        while (p != null) { t = p.Transform * t; p = p.GetParent() as Node3D; }
        return t;
    }
}
