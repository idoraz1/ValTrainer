using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Procedural visuals of the duelist drills (own shaders via <see cref="UtilLook"/>; no imported art): Raze's Blast Pack,
/// soul / energy orbs, Yoru's rift tether, Waylay's beacon, and short glowing bursts (blasts, teleports, dashes).
/// Every visual is a plain node under the session's world; the drills free them when a round ends.
/// </summary>
public partial class DuelistVisual : Node3D
{
    ShaderMaterial? glowMat;
    MeshInstance3D? glow;
    OmniLight3D? light;
    float baseSize, pulseHz, blinkHz, age;
    float lightEnergy;

    /// <summary>A glowing orb / device: core sphere (optional), additive halo, small light.</summary>
    public static DuelistVisual Make(IGame g, Vector3 at, Color c, float size, bool core = true, bool ring = false, float pulseHz = 0f,
        float blinkHz = 0f, float light = 1.2f)
    {
        var v = new DuelistVisual { Name = "DuelistVisual", baseSize = size, pulseHz = pulseHz, blinkHz = blinkHz, lightEnergy = light };
        g.World.AddChild(v);
        v.GlobalPosition = at;
        if (core) UtilLook.Core(v, new SphereMesh { Radius = size * 0.18f, Height = size * 0.36f, RadialSegments = 16, Rings = 8 }, c, 3f);
        var (n, m) = UtilLook.Glow(v, c, size, 2.2f, ring);
        v.glow = n; v.glowMat = m;
        if (light > 0) v.light = UtilLook.Light(v, c, light, Mathf.Max(2f, size * 4f));
        return v;
    }

    /// <summary>Raze's Blast Pack: a small dark box with an orange core and a blinking light.</summary>
    public static DuelistVisual Satchel(IGame g, Vector3 at)
    {
        var c = new Color(1f, 0.55f, 0.15f);
        var v = Make(g, at, c, 0.5f, core: false, blinkHz: 4f, light: 0.8f);
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.14f, 0.16f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.17f, 0.15f), Roughness = 0.6f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        v.AddChild(body);
        UtilLook.Core(v, new BoxMesh { Size = new Vector3(0.08f, 0.16f, 0.08f) }, c, 4f);
        return v;
    }

    /// <summary>Particle trail behind a moving device (tether, satchel in flight).</summary>
    public void AddTrail(Color head, Color tail, float size = 0.25f) => UtilLook.Trail(this, head, tail, size, 0.6f, 40);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        age += dt;
        if (glow != null && pulseHz > 0) glow.Scale = Vector3.One * baseSize * (1f + 0.18f * Mathf.Sin(age * pulseHz * Mathf.Tau));
        if (blinkHz > 0)
        {
            bool on = Mathf.PosMod(age * blinkHz, 1f) < 0.5f;
            glowMat?.SetShaderParameter("fade", on ? 1f : 0.25f);
            if (light != null) light.LightEnergy = on ? lightEnergy : lightEnergy * 0.2f;
        }
    }

    /// <summary>Faster blinking (an armed / about to blow satchel, a fading orb).</summary>
    public void SetBlink(float hz) => blinkHz = hz;

    public void Remove() { if (IsInstanceValid(this)) QueueFree(); }
}

/// <summary>A short glowing flash that grows and fades (blast, teleport arrival, dash puff).</summary>
public partial class DuelistBurst : Node3D
{
    ShaderMaterial mat = null!;
    MeshInstance3D glow = null!;
    OmniLight3D? light;
    float t, life, size, energy;

    public static void Spawn(IGame g, Vector3 at, Color c, float size, float life = 0.45f, float light = 3f)
    {
        if (g.World == null || !GodotObject.IsInstanceValid(g.World)) return;
        var b = new DuelistBurst { Name = "DuelistBurst", life = life, size = size, energy = light };
        g.World.AddChild(b);
        b.GlobalPosition = at;
        (b.glow, b.mat) = UtilLook.Glow(b, c, size * 0.4f, 3f);
        if (light > 0) b.light = UtilLook.Light(b, c, light, size * 3f);
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float k = Mathf.Clamp(t / life, 0f, 1f);
        glow.Scale = Vector3.One * size * (0.4f + 0.8f * Mathf.Sqrt(k));
        mat.SetShaderParameter("fade", 1f - k);
        if (light != null) light.LightEnergy = energy * (1f - k);
        if (k >= 1f) QueueFree();
    }
}
