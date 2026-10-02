using Godot;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Muzzle flash at the gun's Muzzle marker: a camera-facing procedural star (random rotation/size),
/// three crossed side-flame quads along the barrel, and a ~35 ms OmniLight3D burst that lights the
/// gun, the hands and the nearby world. Drawn with the viewmodel projection so it sits exactly on the
/// barrel tip at any zoom. Materials are created once per viewmodel; firing only changes uniforms.
/// </summary>
public partial class MuzzleFlash : Node3D
{
    const float Life = 0.05f, LightLife = 0.035f;

    MeshInstance3D star = null!, side = null!;
    ShaderMaterial starMat = null!, sideMat = null!;
    OmniLight3D light = null!;
    float t = 99f, size = 0.11f, baseScale = 1f, energy = 3f, lastK = -1f;
    readonly Random rng = new();

    /// <summary>Keep the flash fully visible (dev screenshots).</summary>
    public bool Hold;
    /// <summary>Current flash strength 0..1 (drives the warm fill on the viewmodel).</summary>
    public float Fill { get; private set; }

    static ArrayMesh? flameMesh;

    public static MuzzleFlash Create(VmMaterials mats, float scale, uint layer)
    {
        var f = new MuzzleFlash { Name = "MuzzleFlash", baseScale = scale };
        f.starMat = mats.MakeFlash(0);
        f.sideMat = mats.MakeFlash(1);
        f.star = new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = f.starMat };
        f.side = new MeshInstance3D { Mesh = flameMesh ??= BuildFlame(), MaterialOverride = f.sideMat };
        foreach (var m in new[] { f.star, f.side })
        {
            m.Layers = layer;
            m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            m.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            m.ExtraCullMargin = 2f;
            f.AddChild(m);
        }
        f.light = new OmniLight3D
        {
            LightColor = new Color(1f, 0.68f, 0.36f),
            LightEnergy = 0f,
            OmniRange = 5f,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            LightSpecular = 0.6f,
            Position = new Vector3(0, 0.02f, -0.05f),
            Visible = false,
        };
        f.AddChild(f.light);
        f.energy = 1.7f * Mathf.Sqrt(scale);
        return f;
    }

    /// <summary>Three quads crossing on the barrel axis, from the muzzle (UV.y 0) forward to the tip (UV.y 1).</summary>
    static ArrayMesh BuildFlame()
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
        for (int k = 0; k < 3; k++)
        {
            var rot = new Basis(Vector3.Back, k * Mathf.Pi / 3f);
            int b = v.Count;
            v.Add(rot * new Vector3(-0.5f, 0, 0)); uv.Add(new Vector2(0, 0));
            v.Add(rot * new Vector3(0.5f, 0, 0)); uv.Add(new Vector2(1, 0));
            v.Add(rot * new Vector3(-0.5f, 0, -1)); uv.Add(new Vector2(0, 1));
            v.Add(rot * new Vector3(0.5f, 0, -1)); uv.Add(new Vector2(1, 1));
            idx.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arr[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public void Fire()
    {
        t = 0f;
        size = 0.16f * baseScale * R(0.8f, 1.2f);
        star.Scale = Vector3.One * size;
        starMat.SetShaderParameter("rot", R(0, Mathf.Tau));
        starMat.SetShaderParameter("seed", R(0, 100));
        sideMat.SetShaderParameter("seed", R(0, 100));
        float w = 0.1f * baseScale * R(0.8f, 1.15f);
        side.Scale = new Vector3(w, w, 0.28f * baseScale * R(0.7f, 1.25f));
        side.Rotation = new Vector3(0, 0, R(0, Mathf.Pi));
        light.Visible = true;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float k = Hold ? 1f : t < 0.018f ? 1f : Mathf.Max(0f, 1f - (t - 0.018f) / (Life - 0.018f));
        float li = Hold ? 1f : t < LightLife ? 1f - t / LightLife * 0.6f : 0f;
        Fill = li;
        if (k != lastK)
        {
            lastK = k;
            starMat.SetShaderParameter("intensity", k);
            sideMat.SetShaderParameter("intensity", k);
            star.Visible = side.Visible = k > 0.001f;
        }
        bool lit = li > 0.001f;
        if (lit) light.LightEnergy = energy * li;
        if (light.Visible != lit) light.Visible = lit;
        if (Hold && t > 0.06f) Fire(); // flicker while held (screenshots)
    }
}
