using Godot;

namespace ValTrainer.Game;

/// <summary>A spherical aim-trainer target with a glossy enemy-colored material and a soft rim.</summary>
public partial class Target : Node3D
{
    public float Radius { get; private set; }
    public float SpawnTime;
    MeshInstance3D mesh = null!;
    StandardMaterial3D mat = null!;
    float flash;
    Color baseColor;

    public static Target Create(float radius, Color color)
    {
        var t = new Target { Radius = radius, baseColor = color };
        t.mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.35f,
            Metallic = 0.0f,
            RimEnabled = true,
            Rim = 0.6f,
            RimTint = 0.4f,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.25f,
        };
        t.mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = t.mat,
            Scale = Vector3.One * radius,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        t.AddChild(t.mesh);
        return t;
    }

    public void SetRadius(float r)
    {
        Radius = r;
        mesh.Scale = Vector3.One * r;
    }

    /// <summary>Ray-sphere test in world space; returns distance or +inf.</summary>
    public float Ray(Vector3 o, Vector3 d)
    {
        var c = GlobalPosition;
        var oc = o - c;
        float b = oc.Dot(d), cc = oc.Dot(oc) - Radius * Radius, h = b * b - cc;
        if (h < 0) return float.PositiveInfinity;
        float t = -b - Mathf.Sqrt(h);
        return t >= 0 ? t : float.PositiveInfinity;
    }

    public void Hit() => flash = 0.08f;

    /// <summary>Highlight while being tracked (tracking modes).</summary>
    public void SetHot(bool hot) => mat.EmissionEnergyMultiplier = hot ? 1.2f : 0.25f;

    public override void _Process(double delta)
    {
        if (flash <= 0) return;
        flash -= (float)delta;
        mat.AlbedoColor = flash > 0 ? Colors.White : baseColor;
    }
}
