using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>Per-agent look of a smoke (all colours are our own picks that only evoke the agent's theme).</summary>
public sealed record SmokeLook(
    Color Light, Color Dark, Color Rim, Color Lead,
    float Density = 3.2f, float Edge = 0.9f, float Swirl = 0.07f,
    float Sparkle = 0f, float Ripple = 0f, float Pulse = 0f,
    float Brightness = 1f, bool Water = false);

/// <summary>Shared smoke resources: shaders, the tileable 3D noise, meshes, scene lighting and the quality step count.</summary>
public static class SmokeAssets
{
    const string Dir = "res://assets/shaders/smoke/";
    static Shader? volume, wall, screen, xray, ring;
    static ImageTexture3D? noise;
    static SphereMesh? sphere, smallSphere;
    static QuadMesh? quad;

    public static Shader VolumeShader => volume ??= GD.Load<Shader>(Dir + "smoke_volume.gdshader");
    public static Shader WallShader => wall ??= GD.Load<Shader>(Dir + "smoke_wall.gdshader");
    public static Shader ScreenShader => screen ??= GD.Load<Shader>(Dir + "smoke_screen.gdshader");
    public static Shader XrayShader => xray ??= GD.Load<Shader>(Dir + "smoke_xray.gdshader");
    public static Shader RingShader => ring ??= GD.Load<Shader>(Dir + "smoke_ring.gdshader");
    public static SphereMesh Sphere => sphere ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 40, Rings = 20 };
    public static SphereMesh SmallSphere => smallSphere ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 };
    public static QuadMesh Quad => quad ??= new QuadMesh { Size = Vector2.One };

    /// <summary>Ray-march steps per graphics quality (Low … Ultra).</summary>
    public static int Steps(int quality) => quality switch { <= 0 => 14, 1 => 20, 2 => 26, _ => 34 };

    // Scene light for the fake volume lighting (direction TO the sun, colour × energy); refreshed per session.
    public static Vector3 SunDir = new Vector3(0.35f, 0.85f, 0.25f).Normalized();
    public static Color SunColor = new(1f, 0.95f, 0.88f);
    public static float SunEnergy = 1f;

    /// <summary>Reads the sun of the session's world (call once the environment is built).</summary>
    public static void ReadLighting(Node root)
    {
        foreach (var n in root.FindChildren("*", "DirectionalLight3D", true, false))
        {
            if (n is not DirectionalLight3D l) continue;
            SunDir = l.GlobalTransform.Basis.Z.Normalized();
            SunColor = l.LightColor;
            SunEnergy = Mathf.Clamp(l.LightEnergy / 2f, 0.5f, 1.4f);
            return;
        }
    }

    /// <summary>64³ tileable fbm value noise (built once, ≈40 ms).</summary>
    public static ImageTexture3D Noise => noise ??= BuildNoise(64);

    static ImageTexture3D BuildNoise(int n)
    {
        var rng = new Random(20261002);
        int[] periods = { 4, 8, 16, 32 };
        float[] amps = { 0.5f, 0.27f, 0.15f, 0.08f };
        var lattices = periods.Select(p => Enumerable.Range(0, p * p * p).Select(_ => (float)rng.NextDouble()).ToArray()).ToArray();
        var vals = new float[n * n * n];
        for (int o = 0; o < periods.Length; o++)
        {
            int P = periods[o];
            float s = n / (float)P, amp = amps[o];
            var L = lattices[o];
            for (int z = 0; z < n; z++)
            {
                float fz = z / s; int z0 = (int)fz % P, z1 = (z0 + 1) % P; float tz = Smooth(fz - Mathf.Floor(fz));
                for (int y = 0; y < n; y++)
                {
                    float fy = y / s; int y0 = (int)fy % P, y1 = (y0 + 1) % P; float ty = Smooth(fy - Mathf.Floor(fy));
                    for (int x = 0; x < n; x++)
                    {
                        float fx = x / s; int x0 = (int)fx % P, x1 = (x0 + 1) % P; float tx = Smooth(fx - Mathf.Floor(fx));
                        float c00 = Mathf.Lerp(L[(z0 * P + y0) * P + x0], L[(z0 * P + y0) * P + x1], tx);
                        float c10 = Mathf.Lerp(L[(z0 * P + y1) * P + x0], L[(z0 * P + y1) * P + x1], tx);
                        float c01 = Mathf.Lerp(L[(z1 * P + y0) * P + x0], L[(z1 * P + y0) * P + x1], tx);
                        float c11 = Mathf.Lerp(L[(z1 * P + y1) * P + x0], L[(z1 * P + y1) * P + x1], tx);
                        vals[(z * n + y) * n + x] += amp * Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
                    }
                }
            }
        }
        float lo = vals.Min(), hi = vals.Max();
        var imgs = new Godot.Collections.Array<Image>();
        var bytes = new byte[n * n];
        for (int z = 0; z < n; z++)
        {
            for (int i = 0; i < n * n; i++)
            {
                float v = (vals[z * n * n + i] - lo) / Mathf.Max(1e-5f, hi - lo);
                v = Mathf.Clamp((v - 0.5f) * 1.35f + 0.5f, 0f, 1f); // a little more contrast for puffier billows
                bytes[i] = (byte)(v * 255f);
            }
            imgs.Add(Image.CreateFromData(n, n, false, Image.Format.L8, bytes));
        }
        var tex = new ImageTexture3D();
        tex.Create(Image.Format.L8, n, n, n, false, imgs);
        return tex;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}

/// <summary>
/// The look of one <see cref="SmokeVolume"/>: a ray-marched smoke sphere (<c>smoke_volume.gdshader</c>) or a gas / water
/// wall (<c>smoke_wall.gdshader</c>), plus optional deploy visuals — a projectile flying or dropping to the landing point,
/// a landing ring on the floor while it deploys. Follows the volume's size over time (session clock) and frees itself when
/// the smoke is gone.
/// </summary>
public partial class SmokeFx : Node3D
{
    public SmokeVolume Volume = null!;
    IGame g = null!;
    SmokeLook look = null!;
    float seed;

    MeshInstance3D? vol;
    ShaderMaterial? volMat;
    MeshInstance3D? wallNode;
    ArrayMesh? wallMesh;
    ShaderMaterial[] wallMats = Array.Empty<ShaderMaterial>();
    float wallKey = -1f;

    MeshInstance3D? orb, ringNode;
    ShaderMaterial? orbMat, ringMat;
    OmniLight3D? orbLight;
    Vector3 leadFrom, leadTo;
    float leadStart, leadEnd;
    bool leadDrop;
    float ringUntil = -1f;
    /// <summary>Keep the node (and its emitter children) after the smoke is gone (Viper's orb can be re-activated).</summary>
    public bool Persistent;

    public static SmokeFx Sphere(IGame g, SmokeVolume v, SmokeLook look, int quality)
    {
        var fx = new SmokeFx { Volume = v, g = g, look = look, Name = "Smoke", seed = (float)g.Rng.NextDouble() * 10f };
        fx.volMat = new ShaderMaterial { Shader = SmokeAssets.VolumeShader };
        var m = fx.volMat;
        m.SetShaderParameter("noise_tex", SmokeAssets.Noise);
        m.SetShaderParameter("light_color", look.Light);
        m.SetShaderParameter("dark_color", look.Dark);
        m.SetShaderParameter("rim_color", look.Rim);
        m.SetShaderParameter("density", look.Density);
        m.SetShaderParameter("edge", look.Edge);
        m.SetShaderParameter("swirl", look.Swirl);
        m.SetShaderParameter("sparkle", look.Sparkle);
        m.SetShaderParameter("ripple", look.Ripple);
        m.SetShaderParameter("pulse", look.Pulse);
        m.SetShaderParameter("brightness", look.Brightness);
        m.SetShaderParameter("seed", fx.seed);
        m.SetShaderParameter("steps", SmokeAssets.Steps(quality));
        m.SetShaderParameter("sun_dir", SmokeAssets.SunDir);
        m.SetShaderParameter("sun_color", SmokeAssets.SunColor);
        m.SetShaderParameter("sun_energy", SmokeAssets.SunEnergy);
        fx.vol = new MeshInstance3D
        {
            Mesh = SmokeAssets.Sphere, MaterialOverride = m, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 2f,
        };
        fx.AddChild(fx.vol);
        fx.Position = v.Center;
        return fx;
    }

    public static SmokeFx Wall(IGame g, SmokeVolume v, SmokeLook look)
    {
        var fx = new SmokeFx { Volume = v, g = g, look = look, Name = "SmokeWall", seed = (float)g.Rng.NextDouble() * 10f };
        fx.wallMesh = new ArrayMesh();
        fx.wallNode = new MeshInstance3D { Mesh = fx.wallMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4f };
        fx.AddChild(fx.wallNode);
        fx.wallMats = new ShaderMaterial[3];
        for (int i = 0; i < 3; i++)
        {
            var m = new ShaderMaterial { Shader = SmokeAssets.WallShader };
            m.SetShaderParameter("noise_tex", SmokeAssets.Noise);
            m.SetShaderParameter("light_color", look.Light);
            m.SetShaderParameter("dark_color", look.Dark);
            m.SetShaderParameter("rim_color", look.Rim);
            m.SetShaderParameter("water", look.Water ? 1f : 0f);
            m.SetShaderParameter("flow", look.Water ? 0.35f : 0.03f);
            m.SetShaderParameter("rise", look.Water ? 0.05f : 0.22f);
            m.SetShaderParameter("opacity", look.Water ? 0.66f : 0.62f);
            m.SetShaderParameter("brightness", look.Brightness);
            m.SetShaderParameter("seed", fx.seed + i * 0.31f);
            fx.wallMats[i] = m;
        }
        return fx;
    }

    /// <summary>A glowing projectile from <paramref name="from"/> to <paramref name="to"/> between two session times
    /// (<paramref name="drop"/>: accelerates like a falling canister).</summary>
    public SmokeFx WithLead(Vector3 from, Vector3 to, float start, float end, bool drop = false, float size = 0.16f)
    {
        leadFrom = from; leadTo = to; leadStart = start; leadEnd = Mathf.Max(start + 0.01f, end); leadDrop = drop;
        orbMat = new ShaderMaterial { Shader = SmokeAssets.RingShader };
        orbMat.SetShaderParameter("kind", 3);
        orbMat.SetShaderParameter("color", look.Lead);
        orbMat.SetShaderParameter("energy", 2.4f);
        orb = new MeshInstance3D { Mesh = SmokeAssets.SmallSphere, MaterialOverride = orbMat, Scale = Vector3.One * size, Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(orb);
        orbLight = new OmniLight3D { LightColor = look.Lead, LightEnergy = 1.6f, OmniRange = 4f, ShadowEnabled = false, Visible = false, TopLevel = true };
        AddChild(orbLight);
        return this;
    }

    /// <summary>The landing ring on the floor at <paramref name="ground"/> until the smoke has formed.</summary>
    public SmokeFx WithRing(Vector3 ground, float radius)
    {
        ringMat = new ShaderMaterial { Shader = SmokeAssets.RingShader };
        ringMat.SetShaderParameter("kind", 1);
        ringMat.SetShaderParameter("color", look.Rim);
        ringMat.SetShaderParameter("energy", 1.2f);
        ringNode = new MeshInstance3D
        {
            Mesh = SmokeAssets.Quad, MaterialOverride = ringMat, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = ground + new Vector3(0, 0.04f, 0), RotationDegrees = new Vector3(-90, 0, 0), Scale = new Vector3(radius * 2f, radius * 2f, 1f),
        };
        AddChild(ringNode);
        ringUntil = Volume.StartTime + Volume.FormTime * 0.6f;
        return this;
    }

    public override void _Process(double delta) => Tick(g.Now);

    public void Tick(float now)
    {
        var v = Volume;
        if (vol != null && volMat != null)
        {
            float r = v.RadiusAt(now);
            bool show = r > 0.05f;
            vol.Visible = show;
            if (show)
            {
                Position = v.Center;
                vol.Scale = Vector3.One * (r + look.Edge * 0.7f);
                volMat.SetShaderParameter("radius", r);
                // expiry warning: the smoke thins in pulses over its last 1.5 s (finite smokes only)
                float left = v.End - now;
                float thin = float.IsInfinity(v.Duration) || left > 1.5f || left < 0 ? 0f : 0.5f + 0.5f * Mathf.Sin(now * 14f);
                volMat.SetShaderParameter("thin", thin * 0.6f);
            }
        }
        if (wallNode != null) UpdateWall(now);
        if (orb != null)
        {
            bool on = now >= leadStart && now < leadEnd;
            orb.Visible = on;
            orbLight!.Visible = on;
            if (on)
            {
                float t = (now - leadStart) / (leadEnd - leadStart);
                if (leadDrop) t = t * t;
                var p = leadFrom.Lerp(leadTo, t);
                orb.GlobalPosition = p;
                orbLight.GlobalPosition = p;
            }
        }
        if (ringNode != null && ringMat != null)
        {
            bool on = now < ringUntil;
            ringNode.Visible = on;
            if (on) ringMat.SetShaderParameter("progress", Mathf.Clamp(1f - (v.StartTime - now) / 1.2f, 0f, 1f));
        }
        if (!Persistent && v.Gone(now) && now > leadEnd) QueueFree();
    }

    void UpdateWall(float now)
    {
        var v = Volume;
        int n = v.Points.Length;
        if (n < 2) return;
        float key = 0f;
        var h = new float[n];
        for (int i = 0; i < n; i++) { h[i] = v.Height * v.ColumnScale(i, now); key += h[i] * (i + 1); }
        float fade = Mathf.Clamp(v.Scale(now), 0f, 1f);
        foreach (var m in wallMats) m.SetShaderParameter("fade", fade);
        if (Mathf.Abs(key - wallKey) < 0.01f) return;
        wallKey = key;
        wallMesh!.ClearSurfaces();
        if (key <= 0.01f) return;
        float[] offs = { -0.4f, 0f, 0.4f };
        for (int sIdx = 0; sIdx < offs.Length; sIdx++)
        {
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var idx = new List<int>((n - 1) * 6);
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                var p = v.Points[i];
                var dir = (i + 1 < n ? v.Points[i + 1] - p : p - v.Points[i - 1]);
                dir.Y = 0;
                var nrm = new Vector3(-dir.Z, 0, dir.X).Normalized() * offs[sIdx];
                if (i > 0) along += new Vector2(p.X - v.Points[i - 1].X, p.Z - v.Points[i - 1].Z).Length();
                verts[i * 2] = p + nrm + new Vector3(0, -0.3f, 0);
                verts[i * 2 + 1] = p + nrm + new Vector3(0, Mathf.Max(0.01f, h[i]), 0);
                uvs[i * 2] = new Vector2(along, 0f);
                uvs[i * 2 + 1] = new Vector2(along, 1f);
                if (i + 1 < n) { int a = i * 2; idx.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
            }
            var arr = new Godot.Collections.Array();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = verts;
            arr[(int)Mesh.ArrayType.TexUV] = uvs;
            arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
            wallMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            wallMesh.SurfaceSetMaterial(sIdx, wallMats[sIdx]);
        }
    }

    /// <summary>Compiles the smoke shaders before the first smoke appears (invisible sphere + wall in front of the spawn).</summary>
    public static void Prewarm(IGame g, Node3D parent, Vector3 at, SmokeLook look, int quality)
    {
        var v = SmokeVolume.Sphere(at, 1f, g.Now, 0.8f, 0.01f, 0.01f);
        var fx = Sphere(g, v, look, quality);
        fx.volMat!.SetShaderParameter("fade", 0f);
        parent.AddChild(fx);
        var w = SmokeVolume.Wall(new[] { at, at + new Vector3(1, 0, 0) }, 1f, g.Now, 0.8f, 0f, 0.01f, 0.01f);
        var wf = Wall(g, w, look);
        foreach (var m in wf.wallMats) m.SetShaderParameter("opacity", 0.0f);
        parent.AddChild(wf);
    }
}
