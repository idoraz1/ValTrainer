using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// One line's Bullet FX (skins3-contracts §2, <see cref="SkinFeature.BulletFx"/>; recipe id = line id): the muzzle flash
/// (the factory flash re-tinted and scaled, plus the line's own overlay shape), the tracer style and the impact on world
/// surfaces (the factory bullet hole + the line's particles and flash). Colours come from the line's palette made
/// enemy-safe (<see cref="KillFxWorld.SafeColors"/>). Bot hits keep the factory enemy-coloured feedback.
/// </summary>
/// <param name="Id">The line id.</param>
/// <param name="Kind">Shader kind (bulletfx_*.gdshader): 1 glasswork … 10 orrery.</param>
public sealed record BulletFxRecipe(string Id, int Kind)
{
    /// <summary>The factory flash's size under the overlay (0..1: a smaller factory flash leaves room for the line's shape).</summary>
    public float FactoryScale { get; init; } = 0.75f;
    /// <summary>The overlay's size relative to the factory star.</summary>
    public float MuzzleSize { get; init; } = 1.3f;
    /// <summary>Tracer ribbon width / the factory width (the decoration's room; the core stays factory-thin).</summary>
    public float TracerWidth { get; init; } = 1.5f;
    /// <summary>How long the trail lingers after the head reached the hit (s; factory 0.06).</summary>
    public float TracerLinger { get; init; } = 0.07f;
    /// <summary>Main impact particles: shape (skinfx_particle), amount, lifetime, process material.</summary>
    public int ShapeA { get; init; }
    public int CountA { get; init; } = 8;
    public float LifeA { get; init; } = 0.5f;
    public Func<ParticleProcessMaterial>? ProcA { get; init; }
    /// <summary>Optional second impact burst (embers, sparks, mist).</summary>
    public int ShapeB { get; init; }
    public int CountB { get; init; }
    public float LifeB { get; init; } = 0.5f;
    public Func<ParticleProcessMaterial>? ProcB { get; init; }
    /// <summary>Particles of burst A take the accent as their main colour.</summary>
    public bool AccentA { get; init; }
    /// <summary>Impact flash size (m).</summary>
    public float FlashSize { get; init; } = 0.22f;
}

/// <summary>The 10 Bullet FX recipes and their shared resources.</summary>
public static class BulletFx
{
    const string Dir = "res://assets/shaders/skins/";
    static readonly int G = KillFxRecipes.Shape.Glass, Gr = KillFxRecipes.Shape.Grit, St = KillFxRecipes.Shape.Streak,
        Cr = KillFxRecipes.Shape.Crystal, Dr = KillFxRecipes.Shape.Droplet, Sm = KillFxRecipes.Shape.Steam,
        Em = KillFxRecipes.Shape.Ember, Ch = KillFxRecipes.Shape.Chip, Mo = KillFxRecipes.Shape.Mote;

    /// <summary>Dev "--bulletfx-hold": the muzzle overlay stays lit and tracers linger (screenshots).</summary>
    public static readonly bool DevHold = CmdLine.Dev && CmdLine.Has("--bulletfx-hold");

    static readonly Dictionary<string, BulletFxRecipe> recipes = new()
    {
        // glass shards and a prism flash; a stained-glass rod tracer
        ["glasswork"] = new("glasswork", 1)
        {
            TracerWidth = 1.6f, ShapeA = G, CountA = 9, LifeA = 0.55f, ProcA = () => P.Shards, ShapeB = Mo, CountB = 5, ProcB = () => P.Sparkle,
            FlashSize = 0.26f,
        },
        // forked crackle tracer, blue-white sparks and a crackle flash
        ["thunderhead"] = new("thunderhead", 2)
        {
            TracerWidth = 7f, FactoryScale = 0.6f, ShapeA = St, CountA = 12, LifeA = 0.3f, ProcA = () => P.FastSparks, AccentA = true,
            FlashSize = 0.3f,
        },
        // rock grit + embers, a dull red ember trail
        ["basalt"] = new("basalt", 3)
        {
            TracerWidth = 2.6f, TracerLinger = 0.1f, ShapeA = Gr, CountA = 9, LifeA = 0.6f, ProcA = () => P.Grit,
            ShapeB = Em, CountB = 5, LifeB = 0.7f, ProcB = () => P.Embers, FlashSize = 0.2f,
        },
        // green electric snap: sine trace, snap sparks, scope-ring flash
        ["oscilloscope"] = new("oscilloscope", 4)
        {
            TracerWidth = 5f, FactoryScale = 0.55f, ShapeA = St, CountA = 9, LifeA = 0.25f, ProcA = () => P.Snap, FlashSize = 0.26f,
        },
        // livery stripes: twin-stripe tracer, livery chips and a little tyre smoke
        ["rally"] = new("rally", 5)
        {
            TracerWidth = 3.6f, ShapeA = Ch, CountA = 7, LifeA = 0.6f, ProcA = () => P.Chips, ShapeB = Sm, CountB = 3, LifeB = 0.6f,
            ProcB = () => P.Puff, FlashSize = 0.24f,
        },
        // icy burst: snowflake flash, ice crystals and frost mist, a glinting lingering trail
        ["frostbite"] = new("frostbite", 6)
        {
            TracerWidth = 2.4f, TracerLinger = 0.14f, ShapeA = Cr, CountA = 9, LifeA = 0.55f, ProcA = () => P.Crystals, AccentA = true,
            ShapeB = Mo, CountB = 6, LifeB = 0.6f, ProcB = () => P.Sparkle, FlashSize = 0.28f,
        },
        // amber droplets and a hex flash; a beaded honey tracer
        ["hive"] = new("hive", 7)
        {
            TracerWidth = 2.4f, ShapeA = Dr, CountA = 8, LifeA = 0.55f, ProcA = () => P.Drops, FlashSize = 0.24f,
        },
        // spark spray at the muzzle, a white-hot tracer with spark dashes, a molten splash and sparks
        ["foundry"] = new("foundry", 8)
        {
            TracerWidth = 2.6f, TracerLinger = 0.09f, ShapeA = Dr, CountA = 8, LifeA = 0.5f, ProcA = () => P.Molten,
            ShapeB = St, CountB = 9, LifeB = 0.35f, ProcB = () => P.FastSparks, FlashSize = 0.28f,
        },
        // a ribbon of northern light, glitter on impact
        ["aurora"] = new("aurora", 9)
        {
            TracerWidth = 6f, TracerLinger = 0.12f, ShapeA = Mo, CountA = 11, LifeA = 0.7f, ProcA = () => P.Glitter,
            ShapeB = Cr, CountB = 4, LifeB = 0.5f, ProcB = () => P.Crystals, FlashSize = 0.3f,
        },
        // a thin gold line with an orbiting mote; gold motes and a ring flash
        ["orrery"] = new("orrery", 10)
        {
            TracerWidth = 5f, FactoryScale = 0.6f, ShapeA = Mo, CountA = 8, LifeA = 0.55f, ProcA = () => P.Gold, FlashSize = 0.28f,
        },
    };

    /// <summary>Every recipe id (the Signature and Apex line ids).</summary>
    public static IEnumerable<string> Ids => recipes.Keys;

    /// <summary>The recipe for a line id (null = unknown: factory bullet FX).</summary>
    public static BulletFxRecipe? Get(string? id) => id != null && recipes.TryGetValue(id, out var r) ? r : null;

    static ShaderMaterial? tracerMat, flashMat;
    static ArrayMesh? tracerMesh;
    static QuadMesh? flashQuad;

    internal static ShaderMaterial TracerMat => tracerMat ??= new ShaderMaterial { Shader = GD.Load<Shader>(Dir + "bulletfx_tracer.gdshader") };
    internal static ShaderMaterial FlashMat => flashMat ??= new ShaderMaterial { Shader = GD.Load<Shader>(Dir + "bulletfx_flash.gdshader") };
    internal static Shader MuzzleShader => GD.Load<Shader>(Dir + "bulletfx_muzzle.gdshader");

    /// <summary>Unit ribbon (like the factory tracer): x ∈ [-0.5, 0.5], z from 0 to -1 (scaled to the beam length).</summary>
    internal static ArrayMesh TracerMesh => tracerMesh ??= BuildTracer();
    internal static QuadMesh FlashQuad => flashQuad ??= new QuadMesh { Size = Vector2.One, Material = FlashMat };

    static ArrayMesh BuildTracer()
    {
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, -1), new Vector3(0.5f, 0, -1) };
        arr[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        arr[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 1, 2, 3 };
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        m.SurfaceSetMaterial(0, TracerMat);
        return m;
    }

    /// <summary>Impact particle process materials (emitter +Y = the surface normal). Small, short, few.</summary>
    internal static class P
    {
        static ParticleProcessMaterial? shards, sparkle, fastSparks, grit, embers, snap, chips, puff, crystals, drops, molten, glitter, gold;

        static ParticleProcessMaterial Make(float spread, float v0, float v1, float grav, float s0, float s1, bool align = false, bool spin = false,
            float damp = 1f, float rise = 0f) => new()
        {
            Direction = Vector3.Up, Spread = spread, InitialVelocityMin = v0, InitialVelocityMax = v1,
            Gravity = new Vector3(0, grav + rise, 0), DampingMin = damp, DampingMax = damp * 1.8f, ScaleMin = s0, ScaleMax = s1,
            ParticleFlagAlignY = align, AngleMin = 0f, AngleMax = spin ? 360f : 0f,
            AngularVelocityMin = spin ? -540f : 0f, AngularVelocityMax = spin ? 540f : 0f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.015f, LifetimeRandomness = 0.4f,
        };

        public static ParticleProcessMaterial Shards => shards ??= Make(70f, 1.2f, 3f, -9.8f, 0.03f, 0.055f, spin: true);
        public static ParticleProcessMaterial Sparkle => sparkle ??= Make(80f, 0.2f, 0.8f, 0f, 0.015f, 0.03f, damp: 2f, rise: 0.3f);
        public static ParticleProcessMaterial FastSparks => fastSparks ??= Make(60f, 3f, 7.5f, -9.8f, 0.035f, 0.06f, align: true, damp: 2f);
        public static ParticleProcessMaterial Grit => grit ??= Make(55f, 1.2f, 3.2f, -9.8f, 0.025f, 0.045f, spin: true);
        public static ParticleProcessMaterial Embers => embers ??= Make(45f, 0.4f, 1.4f, 0f, 0.015f, 0.025f, damp: 1.5f, rise: 0.8f);
        public static ParticleProcessMaterial Snap => snap ??= Make(88f, 2.5f, 5f, 0f, 0.03f, 0.05f, align: true, damp: 8f);
        public static ParticleProcessMaterial Chips => chips ??= Make(60f, 1.4f, 3f, -6f, 0.02f, 0.035f, spin: true, damp: 1.5f);
        public static ParticleProcessMaterial Puff => puff ??= Make(30f, 0.3f, 0.8f, 0f, 0.1f, 0.16f, damp: 3f, rise: 0.4f);
        public static ParticleProcessMaterial Crystals => crystals ??= Make(65f, 1f, 2.6f, -7f, 0.03f, 0.05f, spin: true);
        public static ParticleProcessMaterial Drops => drops ??= Make(55f, 1f, 2.4f, -9.8f, 0.025f, 0.04f, align: true);
        public static ParticleProcessMaterial Molten => molten ??= Make(50f, 1.4f, 3.2f, -9.8f, 0.025f, 0.045f, align: true);
        public static ParticleProcessMaterial Glitter => glitter ??= Make(85f, 0.3f, 1.2f, 0f, 0.015f, 0.03f, damp: 2f, rise: 0.25f);
        public static ParticleProcessMaterial Gold => gold ??= Make(80f, 0.6f, 1.8f, 0f, 0.018f, 0.032f, damp: 3f, rise: 0.2f);
    }
}

/// <summary>
/// The session's pooled Bullet FX in the world (tracers, impact particles and flashes), under the run's world node like
/// <see cref="Fx.FxWorld"/>. Built once per run (nodes created up front; a recipe change only swaps process materials and
/// re-warms), so a shot only sets transforms / instance uniforms and restarts at most two small emitters: the per-shot cost
/// is the factory's. <see cref="Active"/> is false for factory guns, which keep the factory path untouched.
/// </summary>
public partial class BulletFxWorld : Node3D
{
    const int TracerCount = 32, FlashCount = 16, EmitCount = 12;
    const float TracerSpeed = 420f, FlashLife = 0.09f;
    const uint WorldMask = 1u;

    sealed class TracerItem { public MeshInstance3D Node = null!; public float Len, Head, Fade; public bool Live; }
    sealed class FlashItem { public MeshInstance3D Node = null!; public float Age = 99f, Size; }

    readonly List<TracerItem> tracers = new();
    readonly List<FlashItem> flashes = new();
    readonly List<GpuParticles3D> emA = new(), emB = new();
    int nTracer, nFlash, nA, nB;
    bool built;
    readonly Random rng = new();
    Color c1, c2, c3;

    /// <summary>The worn line's recipe (null = factory: <see cref="Active"/> false).</summary>
    public BulletFxRecipe? Recipe { get; private set; }
    public bool Active => Recipe != null;
    /// <summary>The recipe's enemy-safe colours (main, accent, alt).</summary>
    public (Color Main, Color Accent, Color Alt) Colors3 => (c1, c2, c3);

    public static BulletFxWorld Create(Node3D root)
    {
        var w = new BulletFxWorld { Name = "BulletFxWorld" };
        root.AddChild(w);
        return w;
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>The gun in hand changed: its line's recipe and colours (null state / no BulletFx = factory).</summary>
    public void Setup(SkinState? state, Color enemy)
    {
        var r = BulletFx.Get(state?.BulletFx);
        if (r == null) { Recipe = null; return; }
        var (m, a, alt) = KillFxWorld.SafeColors(state!.Value.Palette, enemy);
        if (r == Recipe && m == c1 && a == c2 && alt == c3) return;
        Recipe = r;
        c1 = m; c2 = a; c3 = alt;
        if (!built) Build();
        Apply(emA, r.ProcA, r.ShapeA, r.CountA, r.LifeA, r.AccentA);
        Apply(emB, r.ProcB, r.ShapeB, r.CountB, r.LifeB, false);
        foreach (var t in tracers)
        {
            t.Node.SetInstanceShaderParameter("kind", r.Kind);
            t.Node.SetInstanceShaderParameter("wmul", r.TracerWidth);
            t.Node.SetInstanceShaderParameter("tint", c1);
            t.Node.SetInstanceShaderParameter("tint2", c2);
            t.Node.SetInstanceShaderParameter("tint3", c3);
        }
        foreach (var f in flashes)
        {
            f.Node.SetInstanceShaderParameter("kind", r.Kind);
            f.Node.SetInstanceShaderParameter("tint", c1);
            f.Node.SetInstanceShaderParameter("tint2", c2);
            f.Node.SetInstanceShaderParameter("tint3", c3);
        }
        Prewarm();
    }

    void Build()
    {
        built = true;
        for (int i = 0; i < TracerCount; i++)
        {
            var m = new MeshInstance3D { Mesh = BulletFx.TracerMesh, Visible = false, ExtraCullMargin = 0.5f, Layers = WorldMask };
            Common(m);
            AddChild(m);
            tracers.Add(new TracerItem { Node = m });
        }
        for (int i = 0; i < FlashCount; i++)
        {
            var m = new MeshInstance3D { Mesh = BulletFx.FlashQuad, Visible = false, ExtraCullMargin = 0.5f, Layers = WorldMask };
            Common(m);
            AddChild(m);
            flashes.Add(new FlashItem { Node = m });
        }
        for (int i = 0; i < EmitCount; i++) { emA.Add(Emitter()); emB.Add(Emitter()); }
    }

    GpuParticles3D Emitter()
    {
        var p = new GpuParticles3D
        {
            DrawPass1 = KillFxRecipes.Assets.ParticleQuad, Amount = 8, Lifetime = 0.5f, OneShot = true, Explosiveness = 1f, Emitting = false,
            LocalCoords = false, FixedFps = 60, Visible = false, Layers = WorldMask,
            VisibilityAabb = new Aabb(new Vector3(-2, -2, -2), new Vector3(4, 4, 4)),
        };
        Common(p);
        AddChild(p);
        return p;
    }

    static void Common(GeometryInstance3D g)
    {
        g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        g.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
    }

    void Apply(List<GpuParticles3D> list, Func<ParticleProcessMaterial>? proc, int shape, int count, float life, bool accent)
    {
        foreach (var e in list)
        {
            e.Emitting = false;
            e.Visible = false;
            if (proc == null || count <= 0) { e.ProcessMaterial = null; continue; }
            e.ProcessMaterial = proc();
            e.Amount = count;
            e.Lifetime = life;
            e.SetInstanceShaderParameter("shape", shape);
            e.SetInstanceShaderParameter("tint", accent ? c2 : c1);
            e.SetInstanceShaderParameter("tint2", accent ? c1 : c2);
            e.SetInstanceShaderParameter("tint3", c3);
            e.SetInstanceShaderParameter("fade", 1f);
        }
    }

    /// <summary>One invisible shot far below the world: compiles the pipelines before the first real shot.</summary>
    void Prewarm()
    {
        var hidden = new Vector3(0, -200f, 0);
        Tracer(hidden, hidden + new Vector3(0, 0, -5f));
        Impact(hidden, Vector3.Up, hole: false);
    }

    /// <summary>The player's tracer from the muzzle to where the bullet stopped.</summary>
    public void Tracer(Vector3 from, Vector3 to)
    {
        if (Recipe is not { } r) return;
        float len = from.DistanceTo(to);
        if (len < 0.3f) return;
        var t = tracers[nTracer];
        nTracer = (nTracer + 1) % tracers.Count;
        var dir = (to - from) / len;
        var up = Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        var lb = Basis.LookingAt(dir, up);
        t.Node.GlobalTransform = new Transform3D(new Basis(lb.X, lb.Y, lb.Z * len), from);
        t.Len = len; t.Head = 0f; t.Fade = 1f; t.Live = true;
        t.Node.SetInstanceShaderParameter("head", 0f);
        t.Node.SetInstanceShaderParameter("fade", 1f);
        t.Node.SetInstanceShaderParameter("len", len);
        t.Node.SetInstanceShaderParameter("seed", R(0f, 100f));
        t.Node.Visible = true;
    }

    /// <summary>A wall / floor hit: the factory bullet hole, the line's particles and its impact flash.</summary>
    public void Impact(Vector3 pos, Vector3 normal, bool hole = true)
    {
        if (Recipe is not { } r) return;
        normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Up;
        if (hole) Fx.Effects.Hole(pos, normal);
        var xf = new Transform3D(Around(normal), pos + normal * 0.01f);
        if (r.ProcA != null && r.CountA > 0) { Burst(emA[nA], xf); nA = (nA + 1) % emA.Count; }
        if (r.ProcB != null && r.CountB > 0) { Burst(emB[nB], xf); nB = (nB + 1) % emB.Count; }
        var f = flashes[nFlash];
        nFlash = (nFlash + 1) % flashes.Count;
        f.Age = 0f;
        f.Size = r.FlashSize * R(0.85f, 1.15f);
        f.Node.GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * f.Size), pos + normal * 0.04f);
        f.Node.SetInstanceShaderParameter("rot", R(0, Mathf.Tau));
        f.Node.SetInstanceShaderParameter("seed", R(0, 100));
        f.Node.SetInstanceShaderParameter("intensity", 1f);
        f.Node.SetInstanceShaderParameter("k", 0f);
        f.Node.Visible = true;
    }

    static void Burst(GpuParticles3D p, Transform3D xf)
    {
        p.GlobalTransform = xf;
        p.Visible = true;
        p.Restart();
        p.Emitting = true;
    }

    Basis Around(Vector3 n)
    {
        var t = Mathf.Abs(n.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        var x = t.Cross(n).Normalized();
        var z = x.Cross(n);
        return new Basis(x, n, z) * new Basis(Vector3.Up, R(0, Mathf.Tau));
    }

    public override void _Process(double delta)
    {
        if (!built) return;
        float dt = (float)delta;
        float linger = (Recipe?.TracerLinger ?? 0.06f) * (BulletFx.DevHold ? 8f : 1f);
        foreach (var t in tracers)
        {
            if (!t.Live) continue;
            t.Head += TracerSpeed * dt;
            if (t.Head > t.Len) t.Fade -= dt / linger;
            if (t.Fade <= 0f) { t.Live = false; t.Node.Visible = false; continue; }
            t.Node.SetInstanceShaderParameter("head", Mathf.Min(t.Head, t.Len + 50f));
            t.Node.SetInstanceShaderParameter("fade", t.Fade);
        }
        foreach (var f in flashes)
        {
            if (f.Age > FlashLife) continue;
            f.Age += dt;
            if (f.Age > FlashLife) { f.Node.Visible = false; continue; }
            float k = f.Age / FlashLife;
            f.Node.SetInstanceShaderParameter("intensity", (1f - k) * (1f - k));
            f.Node.SetInstanceShaderParameter("k", k);
            f.Node.Scale = Vector3.One * f.Size * (1f + 0.5f * k);
        }
    }

    /// <summary>Tracers / flashes on screen now (tests, bench).</summary>
    public int LiveCount { get { int n = 0; foreach (var t in tracers) if (t.Live) n++; foreach (var f in flashes) if (f.Age <= FlashLife) n++; return n; } }
}

/// <summary>
/// The line's muzzle overlay on the gun in hand: a child of the viewmodel's <see cref="MuzzleFlash"/> drawn with the
/// viewmodel projection (bulletfx_muzzle.gdshader). It re-tints and resizes the factory flash under it once
/// (<c>MuzzleFlash.SetSkinTint</c>), then on each shot draws the line's shape for ~60 ms; <see cref="Pulse"/> is the gun-side
/// kill pulse (~0.35 s). Small and at the barrel tip: never near the crosshair.
/// </summary>
public partial class SkinMuzzle : Node3D
{
    const float FireLife = 0.06f, PulseLife = 0.35f;
    MeshInstance3D fire = null!, pulse = null!;
    ShaderMaterial fm = null!, pm = null!;
    MuzzleFlash flash = null!;
    Viewmodel vm = null!;
    float ft = 99f, pt = 99f, focal = -1f, size = 0.16f;
    BulletFxRecipe recipe = null!;
    readonly Random rng = new();

    /// <summary>The viewmodel this overlay belongs to (a new viewmodel needs a new overlay).</summary>
    public Viewmodel ForViewmodel => vm;

    /// <summary>Puts the overlay on <paramref name="viewmodel"/>'s muzzle flash (null when it has none yet).</summary>
    public static SkinMuzzle? Attach(Viewmodel viewmodel, BulletFxRecipe r, (Color Main, Color Accent, Color Alt) c)
    {
        if (!GodotObject.IsInstanceValid(viewmodel) || viewmodel.Flash is not { } f || !GodotObject.IsInstanceValid(f)) return null;
        var m = new SkinMuzzle { Name = "SkinMuzzle", flash = f, vm = viewmodel };
        m.fm = new ShaderMaterial { Shader = BulletFx.MuzzleShader };
        m.pm = new ShaderMaterial { Shader = BulletFx.MuzzleShader };
        m.pm.SetShaderParameter("mode", 1);
        m.pm.SetShaderParameter("energy", 1.6f);
        m.fire = m.Quad(m.fm);
        m.pulse = m.Quad(m.pm);
        f.AddChild(m);
        m.Configure(r, c);
        return m;
    }

    MeshInstance3D Quad(ShaderMaterial mat)
    {
        var q = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = mat, Layers = Viewmodel.VisualLayer, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled, ExtraCullMargin = 2f,
        };
        AddChild(q);
        return q;
    }

    /// <summary>A recipe / colour change on the same viewmodel.</summary>
    public void Configure(BulletFxRecipe r, (Color Main, Color Accent, Color Alt) c)
    {
        recipe = r;
        foreach (var m in new[] { fm, pm })
        {
            m.SetShaderParameter("kind", r.Kind);
            m.SetShaderParameter("col_main", c.Main);
            m.SetShaderParameter("col_accent", c.Accent);
            m.SetShaderParameter("col_alt", c.Alt);
            m.SetShaderParameter("vm_depth", vm.Materials.Depth);
        }
        flash.SetSkinTint(c.Accent.Lerp(Colors.White, 0.55f), c.Main, r.FactoryScale);
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void SyncFocal()
    {
        float f = vm.Materials.Focal;
        if (f == focal) return;
        focal = f;
        fm.SetShaderParameter("vm_focal", f);
        pm.SetShaderParameter("vm_focal", f);
    }

    /// <summary>A shot (after the factory flash fired: its star size sets the overlay's).</summary>
    public void Fire()
    {
        SyncFocal();
        size = Mathf.Max(flash.StarSize, 0.02f) / Mathf.Max(recipe.FactoryScale, 0.1f) * recipe.MuzzleSize;
        fire.Scale = Vector3.One * size;
        fm.SetShaderParameter("rot", R(0, Mathf.Tau));
        fm.SetShaderParameter("seed", R(0, 100));
        ft = 0f;
        fire.Visible = true;
    }

    /// <summary>The gun-side kill pulse (Settings → Kill effects not Off).</summary>
    public void Pulse()
    {
        SyncFocal();
        pulse.Scale = Vector3.One * Mathf.Max(size, 0.08f) * 0.9f;
        pm.SetShaderParameter("seed", R(0, 100));
        pt = 0f;
        pulse.Visible = true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (ft <= FireLife)
        {
            ft += dt;
            if (BulletFx.DevHold && ft > FireLife) ft = 0.01f; // dev: keep it lit for screenshots
            float k = Mathf.Clamp(ft / FireLife, 0f, 1f);
            fm.SetShaderParameter("k", k);
            fm.SetShaderParameter("intensity", k < 0.25f ? 1f : 1f - (k - 0.25f) / 0.75f);
            if (ft > FireLife) fire.Visible = false;
        }
        if (pt <= PulseLife)
        {
            pt += dt;
            float k = Mathf.Clamp(pt / PulseLife, 0f, 1f);
            pm.SetShaderParameter("k", k);
            pm.SetShaderParameter("intensity", 1f - k * k);
            if (pt > PulseLife) pulse.Visible = false;
        }
    }
}

/// <summary>
/// Dev helpers for the skins-3 effects while the catalogue may not list the new lines yet (the parallel builders merge
/// later): a synthetic <see cref="SkinState"/> built from <see cref="SkinLooks"/> with the contract's levels (§2) and every
/// recipe id = the line id. Used only for dev <c>--skin</c> of a line the catalogue lacks, the benches and the tests.
/// </summary>
internal static class SkinFxDev
{
    static readonly Dictionary<string, SkinLine> lines = new();

    /// <summary>A Signature / Apex line at a level: the catalogue's when it has the line, else a synthetic one.</summary>
    public static SkinState? State(string id, int level, string? variant = null)
    {
        var line = SkinCatalog.Get(id) is { } l && l.Levels.Any(v => v.BulletFx != null || v.KillFx == id) ? l : Synthetic(id);
        if (line == null) return null;
        return new SkinState(line, Math.Clamp(level, 1, line.MaxLevel), line.Variant(variant));
    }

    /// <summary>Dev <c>--skin line[/variant][@level]</c> naming a skins-3 line the catalogue doesn't know (yet).</summary>
    public static SkinState? FromDevSkin()
    {
        if (!CmdLine.Dev || GunSkins.DevSkin is not { Length: > 0 } s) return null;
        s = s.Trim().ToLowerInvariant();
        int level = 99;
        int at = s.IndexOf('@');
        if (at >= 0) { if (!int.TryParse(s[(at + 1)..], out level)) level = 99; s = s[..at]; }
        int sep = s.IndexOfAny(new[] { '/', '.' });
        string id = sep < 0 ? s : s[..sep];
        string? variant = sep < 0 ? null : s[(sep + 1)..];
        if (SkinCatalog.Get(id) != null) return null; // the catalogue has it: GunSkins resolves it
        return Synthetic(id) is { } line ? new SkinState(line, Math.Clamp(level, 1, line.MaxLevel), line.Variant(variant)) : null;
    }

    static SkinLine? Synthetic(string id)
    {
        if (lines.TryGetValue(id, out var cached)) return cached;
        bool apex = SkinLooks.Apex.Contains(id);
        if (!apex && !SkinLooks.Signature.Contains(id)) return null;
        const SkinFeature sig = SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.HitSounds | SkinFeature.BulletFx | SkinFeature.KillFx
            | SkinFeature.Inspect | SkinFeature.KillBanner;
        SkinLevel[] levels = apex
            ? new[]
            {
                new SkinLevel(1, SkinFeature.None, "Base"),
                new SkinLevel(2, SkinFeature.Animated | SkinFeature.Reactive, "Animated") { Look = s => s with { Anim = 0.3f, React = 1f } },
                new SkinLevel(3, SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.HitSounds | SkinFeature.BulletFx | SkinFeature.KillFx, "Sounds + effects")
                    { Sounds = id, BulletFx = id, KillFx = id },
                new SkinLevel(4, SkinFeature.Inspect | SkinFeature.ReloadAnim | SkinFeature.KillBanner, "Inspect + reload")
                    { Inspect = id, ReloadAnim = id, Banner = id },
                new SkinLevel(5, SkinFeature.Finisher | SkinFeature.KillCounter, "Finisher") { Finisher = id },
            }
            : new[] { new SkinLevel(1, sig, "Base") { Sounds = id, BulletFx = id, KillFx = id, Inspect = id, Banner = id } };
        var line = new SkinLine(id, id, "", default, SkinLooks.Look(id), levels, SkinLooks.Variants(id), SkinLooks.Palette(id));
        lines[id] = line;
        return line;
    }
}
