using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Shared, create-once FX resources: shaders, materials, meshes, particle process materials and the
/// procedural bullet-hole textures. Everything per-shot is driven by instance uniforms / node transforms,
/// so firing never creates materials or compiles shaders.
/// </summary>
static class FxAssets
{
    const string Dir = "res://assets/shaders/weapon/";

    static ShaderMaterial? tracerMat, sparkMat, dustMat, flashMat;
    static ArrayMesh? tracerMesh;
    static QuadMesh? sparkQuad, dustQuad, flashQuad;
    static ParticleProcessMaterial? sparkProc, bodyProc, dustProc;
    static ImageTexture? holeAlbedo, holeNormal;

    public static ShaderMaterial TracerMat => tracerMat ??= Mat("fx_tracer.gdshader");
    public static ShaderMaterial SparkMat => sparkMat ??= Mat("fx_spark.gdshader");
    public static ShaderMaterial DustMat => dustMat ??= Mat("fx_dust.gdshader");
    public static ShaderMaterial FlashMat => flashMat ??= Mat("fx_flash.gdshader");

    static ShaderMaterial Mat(string file) => new() { Shader = GD.Load<Shader>(Dir + file) };

    /// <summary>Unit ribbon: x ∈ [-0.5, 0.5], z from 0 to -1 (scaled to the beam length).</summary>
    public static ArrayMesh TracerMesh => tracerMesh ??= BuildTracer();

    public static QuadMesh SparkQuad => sparkQuad ??= new QuadMesh { Size = Vector2.One, Material = SparkMat };
    public static QuadMesh DustQuad => dustQuad ??= new QuadMesh { Size = Vector2.One, Material = DustMat };
    public static QuadMesh FlashQuad => flashQuad ??= new QuadMesh { Size = Vector2.One, Material = FlashMat };

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

    static CurveTexture Curve(params Vector2[] pts)
    {
        var c = new Curve();
        foreach (var p in pts) c.AddPoint(p);
        return new CurveTexture { Curve = c };
    }

    /// <summary>Wall-impact sparks: fast, gravity, velocity-aligned (emitter +Y = surface normal).</summary>
    public static ParticleProcessMaterial SparkProc => sparkProc ??= new ParticleProcessMaterial
    {
        Direction = Vector3.Up,
        Spread = 62f,
        InitialVelocityMin = 2.5f,
        InitialVelocityMax = 7.5f,
        Gravity = new Vector3(0, -9.8f, 0),
        DampingMin = 1.5f,
        DampingMax = 3f,
        ScaleMin = 0.5f,
        ScaleMax = 1.25f,
        ScaleCurve = Curve(new Vector2(0, 1), new Vector2(1, 0.25f)),
        ParticleFlagAlignY = true,
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.01f,
        LifetimeRandomness = 0.5f,
    };

    /// <summary>Body-hit burst: enemy-coloured, a bit wider and slower.</summary>
    public static ParticleProcessMaterial BodyProc => bodyProc ??= new ParticleProcessMaterial
    {
        Direction = Vector3.Up,
        Spread = 80f,
        InitialVelocityMin = 1.8f,
        InitialVelocityMax = 5.5f,
        Gravity = new Vector3(0, -6f, 0),
        DampingMin = 3f,
        DampingMax = 5f,
        ScaleMin = 0.6f,
        ScaleMax = 1.3f,
        ScaleCurve = Curve(new Vector2(0, 1), new Vector2(1, 0.3f)),
        ParticleFlagAlignY = true,
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.04f,
        LifetimeRandomness = 0.4f,
    };

    /// <summary>Dust puff: slow, drifting off the wall, growing and fading.</summary>
    public static ParticleProcessMaterial DustProc => dustProc ??= new ParticleProcessMaterial
    {
        Direction = Vector3.Up,
        Spread = 35f,
        InitialVelocityMin = 0.4f,
        InitialVelocityMax = 1.6f,
        Gravity = new Vector3(0, -0.6f, 0),
        DampingMin = 2.5f,
        DampingMax = 4f,
        ScaleMin = 0.12f,
        ScaleMax = 0.24f,
        ScaleCurve = Curve(new Vector2(0, 0.45f), new Vector2(0.4f, 1.1f), new Vector2(1, 1.6f)),
        AngleMin = 0f,
        AngleMax = 360f,
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.015f,
        LifetimeRandomness = 0.35f,
    };

    // ---------------- procedural bullet hole ----------------

    public static ImageTexture HoleAlbedo { get { if (holeAlbedo == null) BuildHole(); return holeAlbedo!; } }
    public static ImageTexture HoleNormal { get { if (holeNormal == null) BuildHole(); return holeNormal!; } }

    static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / (float)uint.MaxValue; }

    /// <summary>1D periodic value noise over the angle (n cells per turn).</summary>
    static float AngNoise(float ang, int n, int seed)
    {
        float t = (ang / Mathf.Tau + 0.5f) * n;
        int i = (int)Mathf.Floor(t);
        float f = t - i;
        f = f * f * (3 - 2 * f);
        return Mathf.Lerp(Hash(((i % n) + n) % n, seed), Hash((((i + 1) % n) + n) % n, seed), f);
    }

    static void BuildHole()
    {
        const int N = 128;
        var alb = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var h = new float[N, N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float n1 = AngNoise(a, 9, 1), n2 = AngNoise(a, 23, 2), n3 = AngNoise(a, 5, 3);
                float r0 = 0.15f + 0.04f * n1;                 // hole
                float r1 = 0.36f + 0.09f * n2 + 0.04f * n1;   // crater / chipped rim
                float r2 = 0.62f + 0.3f * n3 * n2;            // dust / scorch
                float grain = Hash(x, y + 999);
                Color c; float hh;
                if (r < r0) { c = new Color(0.015f, 0.014f, 0.013f, 1f); hh = -1f; }
                else if (r < r1)
                {
                    float k = (r - r0) / (r1 - r0);
                    float g = Mathf.Lerp(0.07f, 0.42f, k * k) + (grain - 0.5f) * 0.08f;
                    c = new Color(g, g * 0.97f, g * 0.93f, 1f);
                    hh = Mathf.Lerp(-0.9f, 0.25f, k);
                }
                else if (r < r2)
                {
                    float k = (r - r1) / (r2 - r1);
                    float lite = (1 - k) * (0.5f + 0.5f * n2);
                    float g = 0.62f + (grain - 0.5f) * 0.1f;
                    float alpha = Mathf.Clamp(lite * 0.85f, 0f, 1f);
                    // dark scorch speckle further out
                    if (grain > 0.82f && k > 0.3f) { g = 0.12f; alpha = 0.35f * (1 - k); }
                    c = new Color(g, g * 0.98f, g * 0.95f, alpha);
                    hh = 0.25f * (1 - k);
                }
                else
                {
                    float k = Mathf.Clamp((r - r2) / 0.25f, 0, 1);
                    c = new Color(0.1f, 0.1f, 0.1f, grain > 0.9f ? 0.25f * (1 - k) : 0f);
                    hh = 0;
                }
                // radial cracks
                float crack = Mathf.Abs(Mathf.Sin(a * 7f + n3 * 4f));
                if (r > r1 * 0.9f && r < r2 * 1.15f && crack < 0.05f * (1.2f - r)) { c = new Color(0.05f, 0.05f, 0.05f, Mathf.Max(c.A, 0.7f)); hh -= 0.2f; }
                if (r > 0.98f) c.A = 0f;
                alb.SetPixel(x, y, c);
                h[x, y] = hh;
            }
        var nor = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = h[Math.Min(x + 1, N - 1), y] - h[Math.Max(x - 1, 0), y];
                float dy = h[x, Math.Min(y + 1, N - 1)] - h[x, Math.Max(y - 1, 0)];
                var n = new Vector3(-dx * 2.2f, dy * 2.2f, 1f).Normalized();
                nor.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1f));
            }
        alb.GenerateMipmaps();
        nor.GenerateMipmaps();
        holeAlbedo = ImageTexture.CreateFromImage(alb);
        holeNormal = ImageTexture.CreateFromImage(nor);
    }
}
