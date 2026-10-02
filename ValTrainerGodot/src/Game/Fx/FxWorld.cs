using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Pooled bullet FX living under <see cref="Effects.Root"/>: tracer ribbons, impact flashes, Decal bullet
/// holes (cap 64, fade after ~6 s), one-shot GPUParticles3D spark/dust/body bursts. All nodes are created
/// up front (which also pre-compiles their pipelines) and recycled round-robin.
/// </summary>
public partial class FxWorld : Node3D
{
    const int Tracers = 32, Flashes = 16, Holes = 64, SparkEmitters = 10, DustEmitters = 10, BodyEmitters = 8;
    const float TracerSpeed = 420f, TracerFade = 0.06f, HoleLife = 6f, HoleFade = 1.2f, FlashLife = 0.06f;
    /// <summary>Bullet holes and particles only render on world layer 1 (never on the viewmodel).</summary>
    const uint WorldMask = 1u;

    sealed class TracerItem { public MeshInstance3D Node = null!; public float Len, Head, Fade; public bool Live; }
    sealed class FlashItem { public MeshInstance3D Node = null!; public float Age = 99f, Peak, Size; }
    sealed class HoleItem { public Decal Node = null!; public float Age = 99f; }

    readonly List<TracerItem> tracers = new();
    readonly List<FlashItem> flashes = new();
    readonly List<HoleItem> holes = new();
    readonly List<GpuParticles3D> sparks = new(), dust = new(), body = new();
    int nTracer, nFlash, nHole, nSpark, nDust, nBody;
    readonly Random rng = new();

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public override void _Ready()
    {
        Name = "FxWorld";
        for (int i = 0; i < Tracers; i++)
        {
            var m = new MeshInstance3D { Mesh = FxAssets.TracerMesh, Visible = false, ExtraCullMargin = 0.5f };
            Common(m);
            AddChild(m);
            tracers.Add(new TracerItem { Node = m });
        }
        for (int i = 0; i < Flashes; i++)
        {
            var m = new MeshInstance3D { Mesh = FxAssets.FlashQuad, Visible = false, ExtraCullMargin = 0.5f };
            Common(m);
            AddChild(m);
            flashes.Add(new FlashItem { Node = m });
        }
        var albedo = FxAssets.HoleAlbedo;
        var normal = FxAssets.HoleNormal;
        for (int i = 0; i < Holes; i++)
        {
            var d = new Decal
            {
                Size = new Vector3(0.085f, 0.08f, 0.085f),
                TextureAlbedo = albedo,
                TextureNormal = normal,
                AlbedoMix = 1f,
                NormalFade = 0.35f,
                UpperFade = 0.25f,
                LowerFade = 0.25f,
                CullMask = WorldMask,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 40f,
                DistanceFadeLength = 10f,
                Visible = false,
            };
            AddChild(d);
            holes.Add(new HoleItem { Node = d });
        }
        for (int i = 0; i < SparkEmitters; i++) sparks.Add(Emitter(FxAssets.SparkProc, FxAssets.SparkQuad, 16, 0.38f));
        for (int i = 0; i < DustEmitters; i++) dust.Add(Emitter(FxAssets.DustProc, FxAssets.DustQuad, 8, 1.0f));
        for (int i = 0; i < BodyEmitters; i++) body.Add(Emitter(FxAssets.BodyProc, FxAssets.SparkQuad, 34, 0.42f));

        // Pre-warm: one invisible burst of each emitter type far below the world.
        var hidden = new Transform3D(Basis.Identity, new Vector3(0, -200f, 0));
        Burst(sparks[0], hidden, new Color(0, 0, 0, 0), 0f, 1f);
        Burst(dust[0], hidden, new Color(0, 0, 0, 0), 0f, 1f);
        Burst(body[0], hidden, new Color(0, 0, 0, 0), 0f, 1f);
    }

    static void Common(GeometryInstance3D g)
    {
        g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        g.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
    }

    GpuParticles3D Emitter(ParticleProcessMaterial proc, Mesh draw, int amount, float life)
    {
        var p = new GpuParticles3D
        {
            ProcessMaterial = proc,
            DrawPass1 = draw,
            Amount = amount,
            Lifetime = life,
            OneShot = true,
            Explosiveness = 1f,
            Emitting = false,
            LocalCoords = false,
            FixedFps = 60,
            VisibilityAabb = new Aabb(new Vector3(-3, -3, -3), new Vector3(6, 6, 6)),
        };
        Common(p);
        AddChild(p);
        return p;
    }

    static void Burst(GpuParticles3D p, Transform3D xf, Color tint, float boost, float ratio, float size = 1f, float whiten = 0.35f)
    {
        p.GlobalTransform = xf;
        p.SetInstanceShaderParameter("tint", tint);
        p.SetInstanceShaderParameter("boost", boost);
        p.SetInstanceShaderParameter("size", size);
        p.SetInstanceShaderParameter("whiten", whiten);
        p.AmountRatio = ratio;
        p.Restart();
        p.Emitting = true;
    }

    /// <summary>Basis with +Y along <paramref name="n"/> and a random twist around it.</summary>
    Basis Around(Vector3 n)
    {
        n = n.LengthSquared() > 1e-6f ? n.Normalized() : Vector3.Up;
        var t = Mathf.Abs(n.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        var x = t.Cross(n).Normalized();
        var z = x.Cross(n);
        return new Basis(x, n, z) * new Basis(Vector3.Up, R(0, Mathf.Tau));
    }

    // ---------------- spawning ----------------

    public void Tracer(Vector3 from, Vector3 to, Color color)
    {
        float len = from.DistanceTo(to);
        if (len < 0.3f) return;
        var t = tracers[nTracer];
        nTracer = (nTracer + 1) % tracers.Count;
        var dir = (to - from) / len;
        var up = Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        var lb = Basis.LookingAt(dir, up);
        var b = new Basis(lb.X, lb.Y, lb.Z * len); // local Z scale = beam length
        t.Node.GlobalTransform = new Transform3D(b, from);
        t.Len = len; t.Head = 0f; t.Fade = 1f; t.Live = true;
        t.Node.SetInstanceShaderParameter("tint", color);
        t.Node.SetInstanceShaderParameter("head", 0f);
        t.Node.SetInstanceShaderParameter("fade", 1f);
        t.Node.Visible = true;
    }

    void Flash(Vector3 pos, Color tint, float size, float peak)
    {
        var f = flashes[nFlash];
        nFlash = (nFlash + 1) % flashes.Count;
        f.Age = 0f; f.Peak = peak; f.Size = size;
        f.Node.GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * size), pos);
        f.Node.SetInstanceShaderParameter("tint", tint);
        f.Node.SetInstanceShaderParameter("rot", R(0, Mathf.Tau));
        f.Node.SetInstanceShaderParameter("intensity", peak);
        f.Node.Visible = true;
    }

    public void Impact(Vector3 pos, Vector3 normal, Color surface)
    {
        normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Up;
        // Bullet hole.
        var h = holes[nHole];
        nHole = (nHole + 1) % holes.Count;
        h.Age = 0f;
        float s = R(0.85f, 1.15f);
        h.Node.Size = new Vector3(0.085f * s, 0.08f, 0.085f * s);
        h.Node.GlobalTransform = new Transform3D(Around(normal), pos);
        h.Node.Modulate = Colors.White;
        h.Node.Visible = true;

        // Sparks (warm), dust (surface tinted), quick flash.
        var xf = new Transform3D(Around(normal), pos + normal * 0.01f);
        Burst(sparks[nSpark], xf, new Color(1f, 0.62f, 0.28f), 1f, R(0.6f, 1f));
        nSpark = (nSpark + 1) % sparks.Count;
        var dustTint = surface.Lerp(new Color(0.86f, 0.83f, 0.78f), 0.5f);
        Burst(dust[nDust], xf, dustTint, 1f, 1f);
        nDust = (nDust + 1) % dust.Count;
        Flash(pos + normal * 0.03f, new Color(1f, 0.7f, 0.4f), R(0.1f, 0.16f), 1f);
    }

    public void BodyHit(Vector3 pos, Color enemy, bool headshot)
    {
        var cam = GetViewport()?.GetCamera3D();
        var toCam = cam != null ? (cam.GlobalPosition - pos).Normalized() : Vector3.Up;
        var xf = new Transform3D(Around(toCam), pos);
        var tint = enemy;
        Burst(body[nBody], xf, tint, headshot ? 0.9f : 0.55f, headshot ? 1f : 0.6f, headshot ? 1.7f : 1.3f, 0.06f);
        nBody = (nBody + 1) % body.Count;
        Flash(pos + toCam * 0.08f, tint, headshot ? 0.42f : 0.26f, headshot ? 1.3f : 0.8f);
    }

    // ---------------- update ----------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var t in tracers)
        {
            if (!t.Live) continue;
            t.Head += TracerSpeed * dt;
            if (t.Head > t.Len) t.Fade -= dt / TracerFade;
            if (t.Fade <= 0f) { t.Live = false; t.Node.Visible = false; continue; }
            t.Node.SetInstanceShaderParameter("head", Mathf.Min(t.Head, t.Len + 50f));
            t.Node.SetInstanceShaderParameter("fade", t.Fade);
        }
        foreach (var f in flashes)
        {
            if (f.Age > FlashLife) continue;
            f.Age += dt;
            if (f.Age > FlashLife) { f.Node.Visible = false; continue; }
            float k = 1f - f.Age / FlashLife;
            f.Node.SetInstanceShaderParameter("intensity", f.Peak * k * k);
            f.Node.Scale = Vector3.One * f.Size * (1f + 0.6f * (1f - k));
        }
        foreach (var h in holes)
        {
            if (h.Age > HoleLife) continue;
            h.Age += dt;
            if (h.Age > HoleLife) { h.Node.Visible = false; continue; }
            if (h.Age > HoleLife - HoleFade) h.Node.Modulate = new Color(1, 1, 1, (HoleLife - h.Age) / HoleFade);
        }
    }
}
