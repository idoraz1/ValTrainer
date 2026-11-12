using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// First-person weapon + gloved arms, parented to the camera.
/// <para>No-clip / own FOV: every viewmodel surface uses viewmodel.gdshader, which re-projects the mesh with
/// <see cref="AppSettings.ViewmodelFov"/> (vertical degrees) and squeezes its depth next to the near plane, so it
/// never goes through walls and keeps its size when the world camera zooms. It is lit by the scene normally
/// (directional light + sky ambient + muzzle-flash omni light), casts no shadows and sits on its own visual
/// layer (<see cref="VisualLayer"/>) so bullet-hole decals never project onto it.</para>
/// <para>Animation is procedural: recoil spring, idle breathing, mouse-look lag, movement bob, crouch, ADS,
/// reload (tilt, mag out/in with a spare magazine, or rounds pushed into a loading port for guns without one), bolt
/// cycle (Operator, Marshal), pump (Bucky), equip raise, shell ejection, and the weapon inspect (<see cref="Inspect"/>,
/// <see cref="VmInspect"/>) with the skin flourishes: the inspect flourish (<see cref="InspectFx"/>) and the Apex special
/// reload (<see cref="ReloadFx"/>, a restyled curve on the factory reload's own progress, so it takes exactly as long).</para>
/// </summary>
public partial class Viewmodel : Node3D
{
    /// <summary>Visual layer 20 — viewmodel only (decals use cull mask 1, so they skip it).</summary>
    public const uint VisualLayer = 1u << 19;

    WeaponKind kind;
    /// <summary>The gun actually held (<see cref="kind"/> is the gun whose model and pose it uses: <see cref="WeaponLooks"/>).</summary>
    WeaponKind actual;
    WeaponLook look = null!;
    bool left;
    VmPose pose = null!;
    VmMaterials mats = new();
    Node3D pivot = null!, gun = null!;
    Node3D? muzzle;
    MeshInstance3D gripHand = null!, supportHand = null!, spareMag = null!;
    MuzzleFlash flash = null!;
    readonly List<ShaderMaterial> gunMats = new();
    Transform3D gripRest, supportRest, boltPose, aimRest = Transform3D.Identity;
    readonly List<MeshInstance3D> modelMeshes = new();
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
    float boltT = -1f, pumpT = -1f, pumpPull;
    bool magHidden, reloadBolted;
    Transform3D supportHome; // supportRest, slid back while the pump is worked

    // skin level effects (skins2-design §4.4, §4.7, §4.8): seconds since the last shot / player kill, the eased kill count,
    // the kill counter's value and "GG" hold, and the Escapement equip flourish
    float fireT = 9f, killT = 9f, slide, slideTarget, counterFlashT = 9f, ggT = 9f;
    int counterKills;
    bool ggIn, flourish;
    MeshInstance3D? counterPlate;

    sealed class Shell { public MeshInstance3D Node = null!; public Vector3 Vel, AngVel; public float Age = 99f; }
    readonly List<Shell> shells = new();
    int nextShell;

    public static Viewmodel Create(WeaponKind kind, bool leftHanded) => Create(kind, leftHanded, false, null);

    /// <summary>A viewmodel wearing <paramref name="skin"/> (null = factory) whatever the settings say (the skin picker's
    /// IN HAND preview).</summary>
    public static Viewmodel CreateWithSkin(WeaponKind kind, bool leftHanded, GunSkin? skin) => Create(kind, leftHanded, true, skin);

    /// <summary>A viewmodel wearing a skin line at a level and variant (null = factory) whatever the settings say: like
    /// <see cref="CreateWithSkin"/>, plus the level's non-material features (the equip flourish).</summary>
    public static Viewmodel CreateWithSkinState(WeaponKind kind, bool leftHanded, SkinState? state) => Create(kind, leftHanded, true, state?.Look, state);

    static Viewmodel Create(WeaponKind kind, bool leftHanded, bool overrideSkin, GunSkin? skin, SkinState? state = null)
    {
        VmTest.Init();
        if (VmTest.Kind is { } k) kind = k;
        if (VmTest.ForceLeft is { } l) leftHanded = l;
        if (VmTest.DevState is { } ds && !overrideSkin) { overrideSkin = true; skin = ds.Look; state = ds; } // dev --vmskin
        var look = WeaponLooks.For(kind);
        var v = new Viewmodel { kind = look.Base, actual = kind, look = look, left = leftHanded, Name = "Viewmodel", overrideSkin = overrideSkin, skinOverride = skin, stateOverride = state };
        v.Build();
        return v;
    }

    /// <summary>The skin was set by the creator (the skin picker's preview), not by the settings.</summary>
    bool overrideSkin;
    GunSkin? skinOverride;
    SkinState? stateOverride;
    ShaderMaterial? spareMagSkin;
    Aabb gunBounds;

    /// <summary>The skin this gun wears now: none for Chamber's gold guns (<see cref="GunSkins.For"/>).</summary>
    GunSkin? ResolveSkin() => look.Gold ? null : overrideSkin ? skinOverride : GunSkins.For(actual);

    /// <summary>Whether the worn skin level has the equip flourish (Escapement L3+).</summary>
    bool ResolveFlourish() => ResolveState()?.Has(SkinFeature.EquipFlourish) == true;

    /// <summary>The skin line / level this gun wears now (null = factory, Chamber's gold guns).</summary>
    SkinState? ResolveState() => look.Gold ? null : overrideSkin ? stateOverride : GunSkins.StateFor(actual);

    // ---------------- public API ----------------

    /// <summary>A shot was fired (recoil kick + muzzle flash + shell).</summary>
    public void OnFire()
    {
        CancelInspect(fast: true); // a shot always wins: the gun is back on target in ~0.07 s (the shot itself never waits)
        flash.Fire();
        fireT = 0f; // reactive skins (fire_t)
        float s = Mathf.Lerp(1f, 0.38f, adsK);
        float w = pose.KickFreq * 2.1f;
        kPosV += new Vector3(R(-0.12f, 0.12f), R(0.15f, 0.35f), 1f) * pose.KickBack * w * s;
        kRotV += new Vector3(pose.KickPitch * R(0.85f, 1.1f), pose.KickPitch * R(-0.25f, 0.25f), pose.KickPitch * R(-0.4f, 0.4f)) * w * Mathf.Lerp(1f, 0.25f, adsK);
        if (look.BoltCycle) boltT = 0f;
        else if (look.Pump) pumpT = 0f;
        else if (pose.Shells) EjectShell();
    }

    /// <summary>The player's bullet killed a bot with this gun (skins2-design §4.4). Call it on every tracked player kill
    /// whatever the skin (factory: nothing happens); the look decides what plays: the gun-side kill pulse
    /// (<see cref="GunSkin.KillGlow"/>, 0.35 s, only where the pattern already glows, off the sight picture, faded at ADS)
    /// and the eased kill slide of animated looks (Strata's layers move one band, Escapement's balance wheel turns once,
    /// Calibrated's scale slides 1 cm). Nothing with Settings → Kill effects Off. Cosmetic only.</summary>
    public void OnPlayerKill()
    {
        if ((Main.I?.Settings?.SkinKillFx ?? 2) <= 0) return;
        killT = 0f;
        slideTarget += 1f;
        if (slideTarget >= 1024f) { slideTarget -= 1024f; slide -= 1024f; } // keep the float small, easing on
    }

    /// <summary>The kill counter (skins with <see cref="GunSkin.Counter"/>: Phosphor Trace L5): <paramref name="kills"/> shows as
    /// two 7-segment digits (99 and a "+" above that); <paramref name="gg"/> shows "GG" while true and for at least 2 s after it
    /// turns true (after a finisher). An increment flickers the digits for 0.3 s. Safe to call every frame (GameSession sends
    /// <c>Mode.Kills</c> when it changes); hidden here with Settings → Kill counter off (callers send kills ≥ 0, never -1). Without a call it reads 0.</summary>
    public void SetKillCount(int kills, bool gg = false)
    {
        kills = Math.Max(0, kills);
        if (kills > counterKills) counterFlashT = 0f;
        counterKills = kills;
        if (gg && !ggIn) ggT = 0f;
        ggIn = gg;
    }

    // 7-segment masks (bits a..g = 0..6) of 0..9, and "G"
    static readonly int[] Seg = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
    const int SegG = 0x3D;

    void UpdateSkinDynamics(float dt)
    {
        if (!mats.HasDynamics) return;
        fireT = Mathf.Min(fireT + dt, 9f);
        killT = Mathf.Min(killT + dt, 9f);
        counterFlashT = Mathf.Min(counterFlashT + dt, 9f);
        ggT = Mathf.Min(ggT + dt, 9f);
        // each kill eases the slide on by one in about 0.4 s
        slide = slideTarget - (slideTarget - slide) * Mathf.Exp(-9f * dt);
        if (slideTarget - slide < 1e-3f) slide = slideTarget;
        mats.SetSkinDynamics(fireT, killT, slide, adsK);
        if (mats.Skin is { Counter: true })
        {
            bool on = Main.I?.Settings?.SkinKillCounter ?? true;
            if (counterPlate != null && counterPlate.Visible != on) counterPlate.Visible = on;
            Vector3 glyphs;
            if (ggIn || ggT < 2f) glyphs = new Vector3(SegG, SegG, 0f);
            else
            {
                int n = Math.Min(counterKills, 99);
                glyphs = new Vector3(n >= 10 ? Seg[n / 10] : 0, Seg[n % 10], counterKills > 99 ? 1f : 0f);
            }
            // an increment: three quick flickers over 0.3 s
            float fk = counterFlashT < 0.3f ? (1f - counterFlashT / 0.3f) * (0.5f + 0.5f * Mathf.Cos(counterFlashT * Mathf.Tau * 10f)) : 0f;
            mats.SetCounter(on, glyphs, fk);
        }
    }

    /// <summary>0 = hip, 1 = fully aimed down sights / scoped.</summary>
    public void SetAds(float t)
    {
        adsIn = Mathf.Clamp(t, 0f, 1f);
        if (adsIn > 0.001f) CancelInspect();
    }
    /// <summary>Reload progress 0..1, or -1 when not reloading.</summary>
    public void SetReload(float progress)
    {
        reloadIn = progress;
        if (progress >= 0f) CancelInspect();
    }

    // ---------------- inspect (skins3-contracts §4) ----------------

    VmInspect inspect = VmInspect.Rifle;
    /// <summary>Seconds into the inspect (-1 = none), its blend weight, and the cancel's decay rate (0 = playing).</summary>
    float inspT = -1f, inspW, inspFade;
    Vector3 inspCentre;
    InspectFx? inspFx;
    ReloadFx? reloadFx;
    bool reloadFxOn;
    SkinMod mod = SkinMod.None, applied = SkinMod.None;

    /// <summary>Skin flourishes (inspect flourish, special reload) may play: false in drills without skin effects (the
    /// measuring tools, drill previews). The session sets it; the look itself always shows. The plain inspect motion and
    /// the factory reload are not affected.</summary>
    public bool SkinFx { get; set; } = true;

    /// <summary>Inspects started on this viewmodel (dev tests).</summary>
    internal int InspectCount { get; private set; }

    /// <summary>
    /// Starts the weapon inspect (+ the skin's inspect flourish and its sound, <see cref="InspectFx"/>) if the gun is idle:
    /// not reloading, not aiming, fully raised, no bolt / pump cycle, visible, not already inspecting. Works for every gun,
    /// factory included, and on the skin picker's preview. Any action cancels it (<see cref="CancelInspect"/>): a shot
    /// (<see cref="OnFire"/>), aiming (<see cref="SetAds"/>), a reload (<see cref="SetReload"/>), a re-equip; the session also
    /// cancels it on death, jumping and leaving the run, and a weapon switch builds a new viewmodel. Cosmetic: the gun's
    /// state and timing never change, so it can never delay a shot.
    /// </summary>
    public void Inspect()
    {
        if (!CanInspect) return;
        inspT = 0f; inspW = 1f; inspFade = 0f;
        InspectCount++;
        if (SkinFx) inspFx?.Start(inspect.Duration);
    }

    /// <summary>True while the inspect plays (false as soon as it is cancelled, while it blends out).</summary>
    public bool Inspecting => inspT >= 0f && inspFade == 0f;

    /// <summary>Whether <see cref="Inspect"/> would start now.</summary>
    public bool CanInspect => inspT < 0f && Visible && IsInsideTree() && reloadIn < 0f && (VmTest.Reload ?? -1f) < 0f && adsIn < 0.01f && adsK < 0.05f
        && (VmTest.Ads ?? 0f) < 0.01f && equipT >= pose.Equip && boltT < 0f && pumpT < 0f && VmTest.View == null;

    /// <summary>Stops the inspect: it blends back to the hip pose in about 0.07 s (<paramref name="fast"/>: a shot) or 0.15 s,
    /// and its flourish fades with it. No-op when not inspecting.</summary>
    public void CancelInspect(bool fast = false)
    {
        if (inspT < 0f) return;
        if (inspFade == 0f) inspFx?.Stop();
        inspFade = Mathf.Max(inspFade, fast ? 45f : 22f);
    }

    /// <summary>The inspect's current blend weight (0 = none, 1 = fully in the inspect pose), dev tests.</summary>
    internal float InspectWeight => inspT < 0f ? 0f : inspW;

    void EndInspect()
    {
        inspT = -1f; inspW = 0f; inspFade = 0f;
        inspFx?.Stop();
    }

    /// <summary>The inspect applied to the pivot transform (right-handed layout): rotation about the gun's centre in gun space,
    /// then the camera-space offset, both scaled by the blend weight.</summary>
    Transform3D InspectPose(float dt, Transform3D t)
    {
        if (inspT < 0f) return t;
        if (inspFade > 0f)
        {
            inspW *= Mathf.Exp(-inspFade * dt);
            if (inspW < 0.01f) { EndInspect(); return t; }
        }
        else
        {
            inspT += dt;
            if (inspT >= inspect.Duration) { EndInspect(); Jolt(0.15f); return t; } // set down: a small settle
        }
        var (off, rot) = inspect.Sample(inspT);
        off *= inspW; rot *= inspW;
        var r = new Basis(Vector3.Up, Mathf.DegToRad(rot.Y)) * new Basis(Vector3.Right, Mathf.DegToRad(rot.X)) * new Basis(Vector3.Back, Mathf.DegToRad(rot.Z));
        var res = t * new Transform3D(r, inspCentre - r * inspCentre);
        res.Origin += off;
        return res;
    }

    /// <summary>Sidearms: the support hand lets go and drops out of view during the inspect (it keeps its place relative to
    /// the un-inspected gun, lowered), then comes back.</summary>
    void InspectHand(Transform3D rest, Transform3D full)
    {
        if (inspT < 0f) return;
        float h = inspect.HandAway(inspT) * inspW;
        if (h <= 0.001f) return;
        var restX = X(rest);
        var dropped = new Transform3D(Basis.Identity, XV(new Vector3(0.05f, -0.42f, 0.2f))) * restX * supportHand.Transform; // fully out of view
        var local = X(full).AffineInverse() * dropped;
        supportHand.Transform = supportHand.Transform.InterpolateWith(local, h);
    }

    /// <summary>(Re)creates the skin flourishes for the worn state (Build, Reskin).</summary>
    void SetupSkinFx()
    {
        inspFx?.Free(); inspFx = null;
        reloadFx?.Free(); reloadFx = null;
        reloadFxOn = false;
        surfacePoints = null;
        var st = ResolveState();
        fxKey = FxKey();
        inspFx = InspectFx.For(this, st);
        reloadFx = ReloadFx.For(this, st);
        if (inspT >= 0f && inspFade == 0f && SkinFx) inspFx?.Start(inspect.Duration); // a live re-skin mid-inspect
    }

    string fxKey = "";
    string FxKey() => ResolveState() is { } s ? $"{s.Line.Id}/{s.Variant?.Id}@{s.Level}:{s.Inspect}:{s.ReloadAnim}" : "";

    /// <summary>Applies the flourishes' material requests (<see cref="SkinMod"/>), only when they change.</summary>
    void ApplySkinMod()
    {
        static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.004f;
        static bool NearC(Color a, Color b) => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.006f;
        bool same = Near(mod.Glow, applied.Glow) && NearC(mod.Emit, applied.Emit) && NearC(mod.MagEmit, applied.MagEmit) && Near(mod.InspectK, applied.InspectK)
            && Near(mod.InspectT, applied.InspectT) && Near(mod.ReloadK, applied.ReloadK) && Near(mod.ReloadP, applied.ReloadP);
        bool restore = mod.Equals(SkinMod.None) && !applied.Equals(SkinMod.None); // back to the exact look
        if (same && !restore) return;
        foreach (var m in gunMats) ApplyMod(m, false);
        if (spareMagSkin != null) ApplyMod(spareMagSkin, true);
        else if (magBodyMat != null) ApplyMod(magBodyMat, true);
        applied = mod;
    }

    readonly Dictionary<ShaderMaterial, float> baseGlow = new();
    ShaderMaterial? magBodyMat;

    void ApplyMod(ShaderMaterial m, bool mag)
    {
        if (VmMaterials.IsSkin(m))
        {
            if (!baseGlow.TryGetValue(m, out var g)) baseGlow[m] = g = m.GetShaderParameter("glow_energy").AsSingle();
            m.SetShaderParameter("glow_energy", g * (1f + mod.Glow));
            if (HasUniform(m.Shader, "inspect_k"))
            {
                m.SetShaderParameter("inspect_k", mod.InspectK);
                m.SetShaderParameter("inspect_t", Mathf.Min(mod.InspectT, 9f));
            }
            if (HasUniform(m.Shader, "reload_k"))
            {
                m.SetShaderParameter("reload_k", mod.ReloadK);
                m.SetShaderParameter("reload_p", mod.ReloadP);
            }
        }
        bool tex = m.Shader == VmMaterials.TexShader;
        if (tex)
        {
            // Modelled skins: the flourish brightens only the model's own glowing parts (its emissive map), and the
            // whole-surface tint is kept faint so the textured gun keeps its colours.
            if (!baseGlow.TryGetValue(m, out var g)) baseGlow[m] = g = m.GetShaderParameter("tex_glow").AsSingle();
            m.SetShaderParameter("tex_glow", g * (1f + mod.Glow));
        }
        var e = (mod.Emit + (mag ? mod.MagEmit : Colors.Black)) * (tex ? 0.2f : 1f);
        float en = Mathf.Max(e.R, Mathf.Max(e.G, e.B));
        m.SetShaderParameter("emission_color", en > 0f ? new Color(e.R / en, e.G / en, e.B / en) : Colors.Black);
        m.SetShaderParameter("emission_energy", en);
    }

    static readonly Dictionary<Shader, HashSet<string>> uniforms = new();
    static bool HasUniform(Shader s, string name)
    {
        if (!uniforms.TryGetValue(s, out var set))
        {
            set = new HashSet<string>();
            foreach (var u in s.GetShaderUniformList()) set.Add(u.AsGodotDictionary()["name"].AsString());
            uniforms[s] = set;
        }
        return set.Contains(name);
    }

    List<(Vector3 P, Vector3 N)>? surfacePoints;

    /// <summary>Points on the gun model's surface (gun space) with their normals, sorted by z: where the flourishes put
    /// particles. At most ~700, from the model's vertices.</summary>
    internal IReadOnlyList<(Vector3 P, Vector3 N)> SurfacePoints
    {
        get
        {
            if (surfacePoints != null) return surfacePoints;
            var all = new List<(Vector3, Vector3)>();
            var toGun = gun.GlobalTransformOrLocal().AffineInverse();
            foreach (var mi in modelMeshes)
            {
                var xf = toGun * mi.GlobalTransformOrLocal();
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(s);
                    var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var nv = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    for (int i = 0; i < v.Length; i++)
                        all.Add((xf * v[i], nv != null && i < nv.Length ? (xf.Basis * nv[i]).Normalized() : Vector3.Up));
                }
            }
            int step = Math.Max(1, all.Count / 700);
            surfacePoints = all.Where((_, i) => i % step == 0).OrderBy(p => p.Item1.Z).ToList();
            return surfacePoints;
        }
    }

    internal Aabb GunBounds => gunBounds;
    /// <summary>Gun space → this node's space (camera space): the pivot's transform (the gun sits at the pivot).</summary>
    internal Transform3D GunToViewmodel => pivot.Transform * gun.Transform;
    internal float Focal => mats.Focal;
    /// <summary>Gun-space x of the side the camera sees: -1 right-handed, +1 left-handed.</summary>
    internal float CameraSide => left ? 1f : -1f;
    internal float MagHalfExtent => pose.MagHalf.Y;
    /// <summary>The worn skin's special reload style (dev tests: <see cref="ReloadStyle.Factory"/> for the factory reload).</summary>
    internal ReloadStyle ReloadStyleNow => SkinFx && reloadFx != null ? reloadFx.Style : ReloadStyle.Factory;
    /// <summary>Whether the viewmodel is drawing a reload (it follows the session's reload progress exactly).</summary>
    internal bool ReloadDrawn => (VmTest.Reload ?? reloadIn) >= 0f;
    /// <summary>The special reload's effects are running (dev tests).</summary>
    internal bool ReloadFxActive => reloadFx?.Active == true;
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

    /// <summary>
    /// Pixel offset of the gun's hologram sight from the screen centre (<see cref="UI.AdsSight"/>): a point 0.5 m ahead of
    /// the eye, carried by the gun, projected with the kick, sway, bob and breathing minus without them. The sight therefore
    /// rides the gun's motion like VALORANT's (Headhunter ADS Fire: about 0.02–0.03 H per shot).
    /// </summary>
    public Vector2 SightScreenOffset()
    {
        var anchor = new Vector3(0f, 0f, -0.5f);
        var now = pivot.Transform * (aimRest.AffineInverse() * anchor);
        return ToScreenPx(now) - ToScreenPx(anchor);
    }

    /// <summary>A viewmodel-space point (camera space: this node sits at the camera) to pixels from the screen centre,
    /// with the viewmodel's own projection.</summary>
    Vector2 ToScreenPx(Vector3 p)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float z = Mathf.Max(0.01f, -p.Z);
        float nx = mats.Focal / (size.X / size.Y) * p.X / z, ny = mats.Focal * p.Y / z;
        return new Vector2(nx * size.X / 2f, -ny * size.Y / 2f);
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
        pose = look.Pose;
        lastFov = Main.I?.Settings?.ViewmodelFov ?? 70f;
        mats.SetFov(lastFov);
        mats.Gold = look.Gold; // Chamber's custom guns: gold finish on the family model
        mats.Skin = ResolveSkin(); // gun skins: cosmetic, the player's gun only (bots stay factory)
        // Modelled Apex skins (Vandal / Phantom: Foundry, Aurora, Orrery): their own gun model and hold.
        modelName = look.Gold ? null : ModelFor(actual, mats.Skin);
        if (modelName != null && VmMaterials.DevModelName == null) pose = VmPose.ModelTest(modelName);
        SetTexSkin(mats.Skin);
        mats.CounterSide = left ? 1f : -1f; // the kill counter sits on the side facing the camera
        flourish = ResolveFlourish();

        pivot = new Node3D { Name = "Pivot" };
        AddChild(pivot);
        gun = GD.Load<PackedScene>(pose.Scene).Instantiate<Node3D>();
        gun.Name = "Gun";
        pivot.AddChild(gun);
        muzzle = gun.GetNodeOrNull<Node3D>("Muzzle");
        // Modelled skins: the model's own magazine copy ("Mag", origin at the magazine well) becomes the reload magazine,
        // so it is taken out of the gun before the gun is materialed and measured (the body keeps its magazine: the
        // reload clip box hides it while the spare one is out).
        var modelMag = gun.FindChild("Mag", true, false) as MeshInstance3D;
        modelMag?.GetParent().RemoveChild(modelMag);
        Modelled = gun.HasMeta("textured") && (bool)gun.GetMeta("textured");
        modelMeshes.AddRange(FindMeshes(gun)); // the gun model only (hands, magazine and flash are added below)

        // Re-material the gun (flat FBX colours → metal / polymer, or the skin).
        foreach (var mi in FindMeshes(gun)) Configure(mi);
        gunBounds = MaterialGun(gun, mats, pose, actual, gunMats);
        counterPlate = AddCounterPlate(gun, modelMeshes, mats, pose, gunBounds, actual);

        // Gloves + sleeves.
        var glove = mats.Make(new Color(0.05f, 0.052f, 0.058f), 0f, 0.8f, 0.4f, 0.7f, 0.25f, 420f);
        var pad = mats.Make(new Color(0.13f, 0.135f, 0.14f), 0.1f, 0.5f, 0.5f, 0.4f, 0.2f, 200f);
        var sleeve = mats.Make(new Color(0.085f, 0.1f, 0.125f), 0f, 0.85f, 0.35f, 0.6f, 0.2f, 260f);
        var accent = mats.Make(new Color(0.55f, 0.13f, 0.12f), 0f, 0.6f, 0.5f, 0.3f, 0.15f, 200f);
        var se = pose.Support.ElbowLocal;
        gripHand = new MeshInstance3D { Name = "GripHand", Mesh = VmHands.Build(pose.Grip.Shape, left, pose.Grip.ElbowLocal, glove, pad, sleeve, accent) };
        supportHand = new MeshInstance3D { Name = "SupportHand", Mesh = VmHands.Build(pose.Support.Shape, !left, new Vector3(-se.X, se.Y, se.Z), glove, pad, sleeve, accent) };
        gripRest = X(pose.Grip.Xform);
        supportRest = supportHome = X(pose.Support.Xform);
        boltPose = pose.Bolt != null ? X(pose.Bolt.Xform) : gripRest;
        gripHand.Transform = gripRest;
        supportHand.Transform = supportRest;
        foreach (var h in new[] { gripHand, supportHand }) { Configure(h); gun.AddChild(h); h.Visible = !VmTest.NoHands; }

        // Spare magazine for the reload.
        var magBody = magBodyMat = mats.Make(new Color(0.07f, 0.072f, 0.078f), 0.2f, 0.5f, 0.5f, 0.5f, 0.15f);
        var magPlate = mats.Make(new Color(0.16f, 0.16f, 0.17f), 0.7f, 0.4f);
        hasModelMag = modelMag?.Mesh != null;
        spareMag = new MeshInstance3D { Name = "SpareMag", Mesh = hasModelMag ? modelMag!.Mesh : VmHands.Magazine(pose.MagHalf, pose.MagCurve, magBody, magPlate), Visible = false };
        if (hasModelMag)
            for (int i = 0; i < spareMag.Mesh.GetSurfaceCount(); i++)
                if ((modelMag!.GetActiveMaterial(i) ?? spareMag.Mesh.SurfaceGetMaterial(i)) is BaseMaterial3D tb && tb.AlbedoTexture != null)
                    spareMag.SetSurfaceOverrideMaterial(i, TexturedSurface(mats, tb));
        modelMag?.QueueFree();
        Configure(spareMag);
        gun.AddChild(spareMag);
        SkinSpareMag();

        // Muzzle flash.
        flash = MuzzleFlash.Create(mats, pose.FlashScale, VisualLayer, look.Suppressed);
        flash.Hold = VmTest.HoldFlash;
        (muzzle ?? gun).AddChild(flash);

        // Shell pool.
        if (pose.Shells || pose.Bolt != null || look.Pump) // bolt and pump guns eject on the bolt / pump cycle
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

        inspect = VmInspect.For(actual);
        var gc = gunBounds.GetCenter();
        inspCentre = new Vector3(0f, gc.Y, gc.Z);
        SetupSkinFx();

        if (VmTest.Enabled) VmTest.Attach(this);
        UpdatePose(0f);
    }

    /// <summary>
    /// Re-materials every surface of a gun model (the model and its Attach_* accessories) through <paramref name="mats"/>
    /// (its <see cref="VmMaterials.Skin"/> included) and adds the materials to <paramref name="into"/>. Each surface gets
    /// the reload clip box (the magazine hides while it is out) in its own mesh space; skin materials also get the mesh →
    /// gun transform, the gun's bounds and a per-gun seed, so patterns are in gun-space metres and run on across the
    /// attachments. Returns the gun-space bounds. Also used by the skin picker's turntable (<see cref="UI.SkinPreview"/>).
    /// </summary>
    internal static Aabb MaterialGun(Node3D gun, VmMaterials mats, VmPose pose, WeaponKind actual, List<ShaderMaterial> into, IEnumerable<MeshInstance3D>? model = null)
    {
        mats.ReceiverIsBody = pose.ReceiverIsBody; // SMG models: the receiver wears the skin's pattern
        // (A curved magazine sweeps forward, or backward behind a bullpup's grip: the clip box follows it.)
        var clipMin = pose.MagWell + new Vector3(-0.06f, -0.4f, -pose.MagHalf.Z - 0.012f - (pose.MagCurve > 1f ? 0.07f : 0f));
        var clipMax = pose.MagWell + new Vector3(0.06f, -0.004f, pose.MagHalf.Z + 0.012f + (pose.MagCurve < -1f ? 0.07f : 0f));
        // the model only: hands, the spare magazine, the flash and the counter module are children of the gun node too
        var meshes = (model ?? FindMeshes(gun)).ToList();
        bool textured = gun.HasMeta("textured") && (bool)gun.GetMeta("textured");
        var toGun = gun.GlobalTransformOrLocal().AffineInverse();
        Aabb? bounds = null;
        foreach (var mi in meshes)
        {
            var b = toGun * mi.GlobalTransformOrLocal() * mi.Mesh.GetAabb();
            bounds = bounds is { } bb ? bb.Merge(b) : b;
        }
        var gb = bounds ?? new Aabb(new Vector3(-0.1f, -0.1f, -0.6f), new Vector3(0.2f, 0.2f, 0.9f));
        foreach (var mi in meshes)
        {
            var meshToGun = toGun * mi.GlobalTransformOrLocal();
            var toMesh = meshToGun.AffineInverse();
            var bb = new Aabb(toMesh * clipMin, Vector3.Zero).Expand(toMesh * clipMax)
                .Expand(toMesh * new Vector3(clipMin.X, clipMin.Y, clipMax.Z)).Expand(toMesh * new Vector3(clipMax.X, clipMax.Y, clipMin.Z));
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.Mesh.SurfaceGetMaterial(i);
                string name = src?.ResourceName ?? "";
                var col = src is BaseMaterial3D bm ? bm.AlbedoColor : new Color(0.2f, 0.2f, 0.2f);
                // A modelled skin (.glb with UVs, scene metadata "textured"): its own textures in the viewmodel projection.
                var m = textured && src is BaseMaterial3D tb && tb.AlbedoTexture != null ? TexturedSurface(mats, tb) : mats.ForGunSurface(name, col);
                m.SetShaderParameter("clip_min", bb.Position);
                m.SetShaderParameter("clip_max", bb.End);
                if (VmMaterials.IsSkin(m)) SetSkinSpace(m, meshToGun, gb, actual, pose);
                into.Add(m);
                mi.SetSurfaceOverrideMaterial(i, m);
            }
        }
        return gb;
    }

    /// <summary>
    /// The kill counter's display module (skins with <see cref="GunSkin.Counter"/>): a small plate (≈2 × 3.5 cm) mounted flush
    /// on the receiver side that faces the camera (<see cref="VmMaterials.CounterSide"/>), between the magazine well (or
    /// loading port) and the grip (the gun-space origin), above the grip hand and below the sight picture (the top 22 % of the
    /// gun). It sits 1.5 mm proud of the outermost model vertex under it, so it reads as an add-on whatever the surface's role
    /// is. Its material is the only one that draws the digits. Null for every other skin. Also usable by a preview.
    /// </summary>
    internal static MeshInstance3D? AddCounterPlate(Node3D gun, IEnumerable<MeshInstance3D> model, VmMaterials mats, VmPose pose, Aabb gb, WeaponKind actual)
    {
        if (mats.Gold || mats.Skin is not { Counter: true } skin) return null;
        float side = mats.CounterSide;
        float h = Mathf.Clamp(gb.Size.Y * 0.08f, 0.015f, 0.022f), w = h * 1.75f;
        var anchor = ReceiverAnchor(gb, pose);
        float zc = anchor.X, yc = anchor.Y;
        // guns with a low magazine well (Stinger, Spectre, the sidearms): lift the plate clear of the grip hand's trigger
        // finger (y ≈ 0–2 cm), still under the sight picture
        float clear = 0.022f + h * 0.5f;
        if (yc < clear) yc = Mathf.Max(yc, Mathf.Min(clear, gb.Position.Y + gb.Size.Y * 0.78f - 0.011f - h * 0.5f));
        // sidearms: the support thumb lies along the frame, so the module goes up on the slide side (it fades when aiming)
        bool sidearm = Weapons.Get(actual)?.Class == WeaponClass.Sidearm;
        if (sidearm) yc = Mathf.Max(yc, gb.Position.Y + gb.Size.Y * 0.86f - 0.011f - h * 0.5f);
        // the outermost surface under the plate, on the camera side
        var toGun = gun.GlobalTransformOrLocal().AffineInverse();
        float face = float.MinValue;
        foreach (var mi in model)
        {
            var xf = toGun * mi.GlobalTransformOrLocal();
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                foreach (var v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var g = xf * v;
                    if (Mathf.Abs(g.Z - zc) < w * 0.5f + 0.012f && Mathf.Abs(g.Y - yc) < h * 0.5f + 0.01f) face = Mathf.Max(face, g.X * side);
                }
        }
        if (face == float.MinValue) face = side > 0f ? gb.End.X : -gb.Position.X;
        const float t = 0.005f; // plate thickness: sunk into the gun, 1.5 mm proud
        // sidearms are held far out and seen almost from behind at hip, so their side face is edge-on: the module is turned
        // 40° toward the camera (rear edge out). The digits follow the turned face: the display rect spans its length along z.
        float turn = sidearm ? Mathf.DegToRad(40f) : 0f;
        float cosT = Mathf.Cos(turn);
        var plate = new MeshInstance3D
        {
            Name = "KillCounter",
            Mesh = new BoxMesh { Size = new Vector3(t, h + 0.004f, w + 0.004f) },
            Position = new Vector3(side * (face + 0.0015f - t * 0.5f + (w * 0.5f + 0.002f) * Mathf.Sin(turn) * 0.5f), yc, zc),
        };
        if (turn > 0f) plate.Basis = new Basis(Vector3.Up, -side * turn);
        Configure(plate);
        gun.AddChild(plate);
        mats.CounterRect = new Vector4(zc - w * 0.5f * cosT, yc - h * 0.5f, zc + w * 0.5f * cosT, yc + h * 0.5f);
        var m = mats.MakeCounterPlate(skin);
        SetSkinSpace(m, plate.Transform, gb, actual, pose);
        plate.SetSurfaceOverrideMaterial(0, m);
        return plate;
    }

    static void SetSkinSpace(ShaderMaterial m, Transform3D meshToGun, Aabb gunBounds, WeaponKind actual, VmPose pose)
    {
        m.SetShaderParameter("skin_xform", new Projection(meshToGun));
        m.SetShaderParameter("gun_min", gunBounds.Position);
        m.SetShaderParameter("gun_max", gunBounds.End);
        m.SetShaderParameter("skin_seed", (int)actual * 0.37f);
        m.SetShaderParameter("recv", ReceiverAnchor(gunBounds, pose));
    }

    /// <summary>A point on the receiver side between the magazine well (or loading port) and the grip, below the sight picture:
    /// where the skins' receiver motifs sit (Escapement's balance wheel, Calibrated's stamp) and the kill counter's display.</summary>
    static Vector2 ReceiverAnchor(Aabb gb, VmPose pose) =>
        new(pose.MagWell.Z * 0.4f, Mathf.Min(pose.MagWell.Y + 0.015f, gb.Position.Y + gb.Size.Y * 0.78f - 0.011f));

    /// <summary>The reload's spare magazine wears the skin's Body finish (factory: its own dark polymer, untouched).</summary>
    void SkinSpareMag()
    {
        if (hasModelMag) return; // a modelled skin's magazine already wears the model's own textures
        if (spareMagSkin != null) { mats.Forget(new[] { spareMagSkin }); spareMagSkin = null; }
        if (mats.Skin is { } skin && !mats.Gold)
        {
            spareMagSkin = mats.ForSkinSurface(skin, VmMaterials.Role.Body);
            if (spareMagSkin != null && VmMaterials.IsSkin(spareMagSkin)) SetSkinSpace(spareMagSkin, Transform3D.Identity, gunBounds, actual, pose);
        }
        spareMag.SetSurfaceOverrideMaterial(0, spareMagSkin);
    }

    /// <summary>Puts on the skin the gun should wear now (the skin picker changed it): only the gun's surfaces and the
    /// spare magazine are re-materialed; pose, animation and reload state carry on.</summary>
    public void Reskin()
    {
        var skin = ResolveSkin();
        bool lite = (Main.I?.Settings?.Quality ?? 1) <= 0;
        flourish = ResolveFlourish();
        // a modelled skin on or off (or another one): a different gun model, so rebuild the whole viewmodel
        if (gun != null && IsInstanceValid(gun) && !look.Gold && ModelFor(actual, skin) != modelName && VmMaterials.DevModelName == null) { Rebuild(actual); return; }
        if (modelName != null && skin != mats.Skin && gun != null && IsInstanceValid(gun)) { Rebuild(actual); return; } // another level of the same model
        if ((skin == mats.Skin && lite == mats.Lite) || gun == null || !IsInstanceValid(gun))
        {
            if (gun != null && IsInstanceValid(gun) && FxKey() != fxKey) SetupSkinFx(); // same look, another level's flourishes
            return;
        }
        mats.Forget(gunMats);
        gunMats.Clear();
        mats.Skin = skin;
        mats.Lite = lite;
        gunBounds = MaterialGun(gun, mats, pose, actual, gunMats, modelMeshes);
        if (counterPlate != null) { mats.Forget(counterPlate.GetSurfaceOverrideMaterial(0) is ShaderMaterial pm ? new[] { pm } : Array.Empty<ShaderMaterial>()); counterPlate.QueueFree(); }
        counterPlate = AddCounterPlate(gun, modelMeshes, mats, pose, gunBounds, actual);
        SkinSpareMag();
        if (magHidden) SetMagHidden(true);
        baseGlow.Clear();
        applied = SkinMod.None; // fresh materials
        SetupSkinFx();
    }

    /// <summary>The skin picker's IN HAND preview: wear <paramref name="skin"/> (null = factory) from now on.</summary>
    public void SetSkinOverride(GunSkin? skin)
    {
        overrideSkin = true;
        skinOverride = skin;
        stateOverride = null;
        Reskin();
    }

    /// <summary>The skin picker's IN HAND / SHOWCASE preview: wear a line at a level and variant (null = factory) from now on,
    /// with the level's non-material features (the equip flourish: <see cref="Reequip"/> shows it).</summary>
    public void SetSkinStateOverride(SkinState? state)
    {
        overrideSkin = true;
        skinOverride = state?.Look;
        stateOverride = state;
        Reskin();
    }

    /// <summary>Plays the equip raise again (the picker's preview of an equip flourish). Cosmetic: the session's equip timer
    /// (<c>Gun.EquipLeft</c>) is not involved.</summary>
    public void Reequip() { CancelInspect(); equipT = 0f; }

    public override void _EnterTree() => GunSkins.Changed += Reskin;
    public override void _ExitTree() => GunSkins.Changed -= Reskin;

    /// <summary>A modelled skin's surface (viewmodel_tex.gdshader) from the model's imported material: base colour, emissive
    /// mask, normal and metallic-roughness maps; the dev <c>-anim</c> suffix turns on the level-2 motion and pulses.</summary>
    static ShaderMaterial TexturedSurface(VmMaterials mats, BaseMaterial3D tb)
    {
        var d = texDyn; // level 2 motion, fire-reactive pulse, kill pulse of the worn level (SetTexSkin)
        var em = tb.EmissionEnabled ? tb.EmissionTexture : null;
        var nrm = tb.NormalEnabled ? tb.NormalTexture : null;
        var m = tb.RoughnessTexture != null
            ? mats.MakeTextured(tb.AlbedoTexture, em, 0.5f, 0.5f, d.X, d.Y, d.Z, normalTex: nrm, mrTex: tb.RoughnessTexture)
            : mats.MakeTextured(tb.AlbedoTexture, em, 0.55f, 0.55f, d.X, d.Y, d.Z, normalTex: nrm);
        if (texSkin != null) ModelVariant(m, texSkin);
        return m;
    }

    /// <summary>What the next textured surfaces wear (Build, the picker's turntable): the level's motion / pulses and the
    /// variant's colours. Main thread only.</summary>
    internal static void SetTexSkin(GunSkin? s)
    {
        texSkin = s;
        texDyn = VmMaterials.DevModel?.EndsWith("-anim") == true ? Vector3.One
            : s != null ? new Vector3(s.Anim > 0f ? 1f : 0f, s.React, s.KillGlow) : Vector3.Zero;
    }
    static GunSkin? texSkin;

    /// <summary>A variant of a modelled skin: the model's textures re-coloured by hue to the variant's colours (the shader's
    /// remap_hue), so every variant keeps the model. Foundry: the molten glow → Glow (Verdigris also tints the iron);
    /// Aurora: the green light → B, the violet → C, the ice → A; Orrery: brass → A, navy enamel → B, the gold vents → Glow.</summary>
    static void ModelVariant(ShaderMaterial m, GunSkin s)
    {
        int slash = s.Id.IndexOf('/');
        if (slash < 0) return; // the base look: the model as made
        string line = s.Id[..slash], v = s.Id[(slash + 1)..];
        static Vector4 From(float hue, float half, float minSat, bool glow = false) => new(hue, half, minSat, glow ? 1f : 0f);
        static Color To(Color c, float amount) => new(c.R, c.G, c.B, amount);
        Color glow = s.Glow ?? Colors.White;
        switch (line)
        {
            case "foundry":
                m.SetShaderParameter("em_from_a", From(0.07f, 0.12f, 0.2f, true)); m.SetShaderParameter("em_to_a", To(glow, 1f));
                if (v == "verdigris") { m.SetShaderParameter("alb_from_a", From(0f, 0.5f, 0f)); m.SetShaderParameter("alb_to_a", To(s.A.Color, 0.55f)); }
                else if (v == "whitehot") { m.SetShaderParameter("alb_from_a", From(0.07f, 0.12f, 0.35f, true)); m.SetShaderParameter("alb_to_a", To(glow, 0.9f)); }
                else { m.SetShaderParameter("alb_from_a", From(0.07f, 0.12f, 0.35f)); m.SetShaderParameter("alb_to_a", To(glow, 0.8f)); }
                break;
            case "aurora":
                m.SetShaderParameter("em_from_a", From(0.40f, 0.14f, 0.2f, true)); m.SetShaderParameter("em_to_a", To((s.B ?? s.A).Color, 1f));
                m.SetShaderParameter("em_from_b", From(0.78f, 0.12f, 0.2f, true)); m.SetShaderParameter("em_to_b", To((s.C ?? s.B ?? s.A).Color, 1f));
                m.SetShaderParameter("alb_from_a", From(0.40f, 0.14f, 0.3f)); m.SetShaderParameter("alb_to_a", To((s.B ?? s.A).Color, 0.9f));
                m.SetShaderParameter("alb_from_b", From(0.78f, 0.12f, 0.3f)); m.SetShaderParameter("alb_to_b", To((s.C ?? s.B ?? s.A).Color, 0.9f));
                break;
            case "orrery":
                m.SetShaderParameter("alb_from_a", From(0.12f, 0.07f, 0.25f)); m.SetShaderParameter("alb_to_a", To(s.A.Color, 1f));
                m.SetShaderParameter("alb_from_b", From(0.62f, 0.09f, 0.2f)); m.SetShaderParameter("alb_to_b", To((s.B ?? s.A).Color, 1f));
                m.SetShaderParameter("em_from_a", From(0.12f, 0.08f, 0.15f, true)); m.SetShaderParameter("em_to_a", To(glow, 1f));
                break;
        }
    }

    /// <summary>The modelled skin of a gun + look ("vandalfoundry", "phantomorrery", ...), or null = the family model with the
    /// procedural skin shader. Only the Apex lines on the Vandal and Phantom have their own models (v1.19).</summary>
    internal static string? ModelFor(WeaponKind k, GunSkin? s)
    {
        if (s == null || k is not (WeaponKind.Vandal or WeaponKind.Phantom)) return null;
        string line = s.Id.Split('/')[0]; // a variant ("foundry/blueflame") wears its line's model, re-coloured (ModelVariant)
        return line is "foundry" or "aurora" or "orrery" ? $"{k.ToString().ToLowerInvariant()}{line}" : null;
    }

    string? modelName;
    static Vector3 texDyn; // set by Build before MaterialGun (main thread only)

    bool hasModelMag;
    /// <summary>The gun is a modelled skin (textured .glb): skin flourishes keep their lights faint, the model carries the colour.</summary>
    internal bool Modelled { get; private set; }

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
        UpdateSkinDynamics(dt);
        UpdateSkinFx(dt);
    }

    /// <summary>The skin flourishes (inspect flourish, special reload): particles, light and the material requests.</summary>
    void UpdateSkinFx(float dt)
    {
        mod = SkinMod.None;
        if (!Visible && inspT >= 0f) CancelInspect(fast: true); // put away (ability, scope overlay, results)
        inspFx?.Tick(dt, inspT >= 0f ? inspW : 0f, ref mod);
        if (reloadFx != null)
        {
            float p = VmTest.Reload ?? reloadIn;
            bool magOut = magHidden || (look.NoMag && p >= 0.2f && p < 0.74f);
            var at = look.NoMag ? supportHand.Transform.Origin : spareMag.Visible ? spareMag.Transform.Origin : X(new Transform3D(Basis.Identity, pose.MagWell)).Origin;
            reloadFx.Tick(dt, reloadFxOn ? p : -1f, at, magOut, ref mod);
        }
        ApplySkinMod();
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

        var (hipPos, hipRot) = VmTest.HipOverride is { } ho ? (ho.Pos, ho.Rot) : (pose.HipPos, pose.HipRot);
        var pos = hipPos.Lerp(pose.AdsPos, adsK);
        var rot = hipRot.Lerp(pose.AdsRot, adsK);
        aimRest = X(new Transform3D(VmMesh.Euler(rot.X, rot.Y, rot.Z), pos)); // before kick, sway and bob (SightScreenOffset)

        // Crouch: a little lower and canted in.
        pos += new Vector3(-0.006f, -0.013f, 0.004f) * crouchK * hip;
        rot.Z += 3.5f * crouchK * hip;

        // Idle breathing.
        float br = 1f - 0.85f * adsK;
        pos += new Vector3(Mathf.Sin(time * 0.83f) * 0.0011f, Mathf.Sin(time * 1.7f) * 0.0016f, 0f) * br;
        rot += new Vector3(Mathf.Sin(time * 1.7f + 0.6f) * 0.22f, Mathf.Sin(time * 0.71f) * 0.12f, Mathf.Sin(time * 0.6f) * 0.25f) * br;

        // Movement bob, paced by the footsteps like VALORANT's calm gun sway: bobPhase runs π per step
        // (FootstepFx.StepInterval: 0.33 s at 5.4 m/s), so the side-to-side swing (sin φ) is one cycle per two steps
        // (≈1.5 Hz at full run) and the dip (1 − cos 2φ) is one per step (≈3 Hz), lowest as each foot lands. The old bob
        // ran at 3.6 / 7.2 Hz, twice a footstep's pace, which made running look light and twitchy. None when still.
        float targetAmp = Mathf.Clamp(speed / Mover.RunSpeed, 0f, 1.3f);
        bobAmp = Mathf.Lerp(bobAmp, targetAmp, 1f - Mathf.Exp(-8f * dt));
        if (speed > 0.05f) bobPhase += dt * Mathf.Pi / Fx.FootstepFx.StepInterval(speed);
        float ba = bobAmp * (1f - 0.75f * adsK);
        float sp = Mathf.Sin(bobPhase);
        pos += new Vector3(sp * 0.0055f, -(1f - Mathf.Cos(2f * bobPhase)) * 0.5f * 0.004f - 0.004f, 0.006f) * ba;
        rot += new Vector3(Mathf.Sin(2f * bobPhase + 0.4f) * 0.4f, sp * 0.4f, sp * 0.8f) * ba;

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

        // Pump, reload and bolt.
        rot += PumpOffset(dt);
        var (rPos, rRot) = ReloadOffset(reload, dt);
        pos += rPos; rot += rRot;
        var (bRot, bHand) = BoltOffset(dt);
        rot += bRot;

        // Equip raise (+ a skin's equip flourish: a quarter roll on the same curve, so it ends exactly with the raise).
        pos += new Vector3(0.03f, -0.2f, 0.05f) * eo;
        rot += new Vector3(-55f, 10f, 30f) * eo;
        if (flourish) rot.Z += 90f * eo;

        var t = new Transform3D(VmMesh.Euler(rot.X, rot.Y, rot.Z), pos);
        var tRest = t;
        t = InspectPose(dt, t);
        if (VmTest.View is { } view) t = tRest = VmTest.ViewXform(view, pose);
        pivot.Transform = X(t);
        InspectHand(tRest, t);

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
        // the Apex special reload: same progress timeline (so exactly the factory duration), its own curve and effects
        if (active && !reloadFxOn) { reloadFxOn = true; if (SkinFx) reloadFx?.Begin(); }
        else if (!active && reloadFxOn) { reloadFxOn = false; reloadFx?.End(); }
        var st = ReloadStyleNow;
        if (!active)
        {
            reloadBolted = false;
            loadsDone = 0;
            if (magHidden) SetMagHidden(false);
            spareMag.Visible = false;
            supportHand.Transform = supportHome.InterpolateWith(supportHand.Transform, reloadW);
            return (Vector3.Zero, Vector3.Zero);
        }

        float env = Smooth(Mathf.Clamp(p / 0.13f, 0f, 1f)) * (1f - Smooth(Mathf.Clamp((p - 0.82f) / 0.15f, 0f, 1f)));
        bool pistol = look.Pistol;
        var rPos = (pistol ? new Vector3(-0.025f, 0.03f, 0.03f) : new Vector3(-0.035f, 0.012f, 0.03f)) * env;
        var rRot = (pistol ? new Vector3(18f, 10f, -18f) : new Vector3(10f, 6f, -30f)) * st.Tilt * env + st.TiltAdd * (pistol ? 0.6f : 1f) * env;
        if (look.NoMag) return LoadRounds(p, rPos, rRot);

        // Magazine path in gun space: at the well → pulled down and away → back → inserted. (The progress points where the
        // magazine leaves and seats never depend on the style: the special reload keeps the factory timing.)
        var well = pose.MagWell;
        float sk = pistol ? 0.6f : 1f;
        var away = well + new Vector3(-0.08f, -0.42f, 0.12f) + st.Away * sk;
        float out1 = Smooth(Mathf.Clamp((p - 0.20f) / 0.16f, 0f, 1f));
        float in1 = Smooth(Mathf.Clamp((p - 0.46f) / 0.16f, 0f, 1f));
        float m = out1 * (1f - in1);
        var magPos = well.Lerp(away, m) + st.Arc * sk * Mathf.Sin(Mathf.Pi * m);
        bool magOut = p >= 0.20f && p < 0.62f;
        if (magOut != magHidden)
        {
            SetMagHidden(magOut);
            if (!magOut && p >= 0.62f) { Jolt(st.SeatJolt); if (SkinFx && reloadFxOn) reloadFx?.OnSeat(X(new Transform3D(Basis.Identity, well)).Origin); }
        }
        spareMag.Visible = magOut;
        spareMag.Transform = X(new Transform3D(VmMesh.Euler(-25f * m, st.MagSpin * m, 15f * m + st.MagRoll * m), magPos));

        // Support hand: rest → on the mag (0.06..0.18) → follows the mag → back to rest (0.64..0.78).
        var handOnMag = X(new Transform3D(VmMesh.Look(new Vector3(0.9f, 0.1f, -0.5f), new Vector3(-0.3f, -1f, 0.1f)),
            magPos + new Vector3(-0.045f, -0.035f, 0.045f)));
        float toMag = Smooth(Mathf.Clamp((p - 0.06f) / 0.12f, 0f, 1f));
        float back = Smooth(Mathf.Clamp((p - 0.64f) / 0.14f, 0f, 1f));
        var hand = supportHome.InterpolateWith(handOnMag, toMag * (1f - back));
        supportHand.Transform = supportHome.InterpolateWith(hand, reloadW);

        CycleAfterReload(p);
        return (rPos * reloadW, rRot * reloadW);
    }

    /// <summary>Bolt (Operator, Marshal) or pump (Bucky) worked once at the end of the reload.</summary>
    void CycleAfterReload(float p)
    {
        if (p <= 0.8f || reloadBolted) return;
        if (look.BoltCycle) { reloadBolted = true; boltT = 0.15f; }
        else if (look.Pump) { reloadBolted = true; pumpT = 0.15f; }
    }

    /// <summary>Reload without a magazine (revolver, shotguns, Marshal): the support hand goes to the loading port
    /// (<see cref="VmPose.MagWell"/>) and pushes <see cref="VmPose.Loads"/> rounds in, one trip each, then returns. Nothing
    /// is clipped and no spare magazine is drawn.</summary>
    (Vector3, Vector3) LoadRounds(float p, Vector3 rPos, Vector3 rRot)
    {
        spareMag.Visible = false;
        int n = Math.Max(1, pose.Loads);
        const float a = 0.2f, b = 0.74f;
        float local = Mathf.Clamp((p - a) / (b - a), 0f, 0.9999f) * n;
        float ph = local - Mathf.Floor(local);
        float trip = p < a || p > b ? 0f : Mathf.Sin(ph * Mathf.Pi); // 0 = at the port, 1 = away fetching the next round
        var atPort = pose.MagWell + new Vector3(-0.045f, -0.035f, 0.045f) + new Vector3(-0.04f, -0.07f, 0.05f) * trip;
        var handAtPort = X(new Transform3D(VmMesh.Look(new Vector3(0.9f, 0.25f, -0.5f), new Vector3(-0.3f, -1f, 0.1f)), atPort));
        float to = Smooth(Mathf.Clamp((p - 0.06f) / 0.12f, 0f, 1f));
        float back = Smooth(Mathf.Clamp((p - 0.76f) / 0.12f, 0f, 1f));
        int done = p < a ? 0 : Math.Min(n, (int)local + (ph > 0.5f ? 1 : 0));
        if (done != loadsDone)
        {
            if (done > loadsDone && trip < 0.2f)
            {
                Jolt(0.35f * ReloadStyleNow.SeatJolt);
                if (SkinFx && reloadFxOn) reloadFx?.OnSeat(X(new Transform3D(Basis.Identity, pose.MagWell)).Origin);
            }
            loadsDone = done;
        }
        var hand = supportHome.InterpolateWith(handAtPort, to * (1f - back));
        supportHand.Transform = supportHome.InterpolateWith(hand, reloadW);
        if (p > 0.78f && !loadsClosed) { loadsClosed = true; Jolt(); }
        if (p < 0.5f) loadsClosed = false;
        CycleAfterReload(p);
        return (rPos * reloadW, rRot * reloadW);
    }
    int loadsDone;
    bool loadsClosed;

    void Jolt(float k = 1f)
    {
        kPosV += new Vector3(0f, 0.012f, -0.004f) * pose.KickFreq * 2f * k;
        kRotV += new Vector3(-2.5f, 0f, 1.5f) * pose.KickFreq * 2f * k;
    }

    // ---------------- pump ----------------

    /// <summary>Pump after a Bucky shot, timed with its sound (<see cref="WeaponLooks.BoltDelay"/>): the support hand slides
    /// back <see cref="VmPose.PumpTravel"/> (the spent shell flies out at the back) and forward again; the gun dips a little.
    /// Returns the pivot rotation; moves <see cref="supportHome"/>.</summary>
    Vector3 PumpOffset(float dt)
    {
        if (pumpT < 0f) { supportHome = supportRest; return Vector3.Zero; }
        float prev = pumpT;
        pumpT += dt;
        const float t0 = 0.28f, t1 = 0.45f, t2 = 0.6f;
        if (prev < t1 && pumpT >= t1) EjectShell();
        if (pumpT > t2 + 0.08f) { pumpT = -1f; supportHome = supportRest; return Vector3.Zero; }
        pumpPull = Smooth(Mathf.Clamp((pumpT - t0) / (t1 - t0), 0f, 1f)) * (1f - Smooth(Mathf.Clamp((pumpT - t1) / (t2 - t1), 0f, 1f)));
        var t = supportRest;
        t.Origin += XV(new Vector3(0f, -0.004f, pose.PumpTravel)) * pumpPull;
        supportHome = t;
        return new Vector3(2.5f, 1f, -3f) * pumpPull;
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

    /// <summary>Dev (VmTest --vmcycle): drop the current model, hands and effects and build <paramref name="k"/> in place,
    /// with the equip raise from the start.</summary>
    internal void Rebuild(WeaponKind k)
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        shells.Clear(); gunMats.Clear(); modelMeshes.Clear(); nextShell = 0;
        mats = new VmMaterials();
        spareMagSkin = null;
        counterPlate = null;
        look = WeaponLooks.For(k); kind = look.Base; actual = k;
        equipT = 0f; boltT = pumpT = -1f; reloadW = 0f; magHidden = reloadBolted = loadsClosed = false; loadsDone = 0;
        inspT = -1f; inspW = inspFade = 0f; inspFx = null; reloadFx = null; reloadFxOn = false; baseGlow.Clear();
        mod = applied = SkinMod.None; magBodyMat = null;
        kPos = kPosV = kRot = kRotV = Vector3.Zero;
        Build();
    }

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
    /// <summary>The gun's on-screen top (its highest projected vertex, hands excluded) as a fraction of the screen height,
    /// with the horizontal position of that vertex and the silhouette's width within 0.03 H below it (fractions of H),
    /// for the ADS pose check (VmTest). Also returns the eased ADS weight the pose was drawn with.</summary>
    internal (float TopY, float TopX, float Width, float Ads) SightTop()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var toCam = ((Node3D)GetParent()).GlobalTransform.AffineInverse();
        var pts = new List<Vector2>();
        foreach (var mi in modelMeshes)
        {
            var xf = toCam * mi.GlobalTransform;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                foreach (var v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var c = ToScreenPx(xf * v) + size / 2;
                    pts.Add(new Vector2(c.X / size.Y, c.Y / size.Y));
                }
        }
        if (pts.Count == 0) return (float.NaN, float.NaN, 0f, adsK);
        var top = pts.MinBy(p => p.Y);
        var band = pts.Where(p => p.Y <= top.Y + 0.03f).ToList();
        return (top.Y, top.X, band.Max(p => p.X) - band.Min(p => p.X), adsK);
    }

    /// <summary>Gun-space top silhouette of the model (dev): highest vertex per 2 cm of length, as (z, y) in metres.</summary>
    internal IEnumerable<(float Z, float Y, float X)> GunProfile()
    {
        var toGun = gun.GlobalTransform.AffineInverse();
        var best = new SortedDictionary<int, Vector3>();
        foreach (var mi in modelMeshes)
        {
            var xf = toGun * mi.GlobalTransform;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                foreach (var v in mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var g = xf * v;
                    int bin = Mathf.FloorToInt(g.Z / 0.02f);
                    if (!best.TryGetValue(bin, out var b) || g.Y > b.Y) best[bin] = g;
                }
        }
        return best.Values.Select(v => (v.Z, v.Y, v.X));
    }

    internal MuzzleFlash Flash => flash;
    internal bool LeftLayout => left;
    internal VmMaterials Materials => mats;
    internal WeaponKind Kind => kind;
    /// <summary>The gun actually held (<see cref="Kind"/> is the one whose model it borrows).</summary>
    internal WeaponKind Held => actual;
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
