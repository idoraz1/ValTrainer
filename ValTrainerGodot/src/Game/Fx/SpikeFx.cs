using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// The planted spike: our own simple procedural device (no Riot model) — a dark hexagonal canister on a squat base with
/// four glowing strips and a small light that flash on every beep; the strips turn from red to cyan as it is defused.
/// <see cref="Detonate"/> spawns the blast: a fast-growing dark sphere with a bright rim that fades out.
/// </summary>
public partial class SpikeFx : Node3D
{
    static readonly Color Armed = new(1f, 0.22f, 0.2f), Defusing = new(0.25f, 0.9f, 1f);

    StandardMaterial3D glow = null!;
    OmniLight3D light = null!;
    float flash, defuse01 = -1f;
    public bool Dead { get; private set; }

    public static SpikeFx Create()
    {
        var s = new SpikeFx { Name = "Spike" };
        var shell = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.13f, 0.15f), Metallic = 0.6f, Roughness = 0.35f };
        var trim = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.52f, 0.48f), Metallic = 0.8f, Roughness = 0.3f };
        s.glow = new StandardMaterial3D
        {
            AlbedoColor = Armed, EmissionEnabled = true, Emission = Armed, EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Add(s, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.24f, Height = 0.08f, RadialSegments = 6 }, trim, new Vector3(0, 0.04f, 0));
        Add(s, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.16f, Height = 0.42f, RadialSegments = 6 }, shell, new Vector3(0, 0.29f, 0));
        Add(s, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.11f, Height = 0.1f, RadialSegments = 6 }, trim, new Vector3(0, 0.55f, 0));
        Add(s, new SphereMesh { Radius = 0.035f, Height = 0.07f }, s.glow, new Vector3(0, 0.62f, 0));
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2 + Mathf.Pi / 4;
            var strip = Add(s, new BoxMesh { Size = new Vector3(0.03f, 0.3f, 0.012f) }, s.glow, new Vector3(Mathf.Sin(a) * 0.135f, 0.3f, Mathf.Cos(a) * 0.135f));
            strip.Rotation = new Vector3(0, a, 0);
        }
        s.light = new OmniLight3D { LightColor = Armed, LightEnergy = 0.4f, OmniRange = 2.5f, Position = new Vector3(0, 0.6f, 0), ShadowEnabled = false };
        s.AddChild(s.light);
        return s;
    }

    static MeshInstance3D Add(Node3D parent, Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>Flash the lights (one beep).</summary>
    public void Beep() => flash = 1f;

    /// <summary>Defuse progress 0..1 (negative = nobody defusing).</summary>
    public void SetDefuse(float p01) => defuse01 = p01;

    /// <summary>Defused: lights go dim cyan for good.</summary>
    public void Disarm()
    {
        Dead = true;
        glow.AlbedoColor = glow.Emission = Defusing * 0.5f;
        glow.EmissionEnergyMultiplier = 0.6f;
        light.LightColor = Defusing;
        light.LightEnergy = 0.1f;
    }

    public override void _Process(double delta)
    {
        if (Dead) return;
        float dt = (float)delta;
        flash = Mathf.Max(0f, flash - dt * 9f);
        var c = defuse01 >= 0 ? Armed.Lerp(Defusing, Mathf.Clamp(defuse01, 0, 1)) : Armed;
        glow.AlbedoColor = c;
        glow.Emission = c;
        glow.EmissionEnergyMultiplier = 1.2f + 3f * flash;
        light.LightColor = c;
        light.LightEnergy = 0.25f + 1.6f * flash;
    }

    /// <summary>Spawns the detonation sphere at the spike (frees itself).</summary>
    public static void Detonate(Node3D parent, Vector3 at, float radius = 14f)
    {
        if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
        var blast = new SpikeBlast { Radius = radius };
        parent.AddChild(blast);
        blast.GlobalPosition = at + new Vector3(0, 0.4f, 0);
    }
}

/// <summary>The spike's detonation: a dark sphere that grows to <see cref="Radius"/> in 0.7 s, then fades.</summary>
public partial class SpikeBlast : MeshInstance3D
{
    public float Radius = 14f;
    float t;
    ShaderMaterial mat = null!;
    static Shader? shader;
    const string Code = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform float fade = 1.0;
void fragment() {
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.5);
	ALBEDO = mix(vec3(0.02, 0.0, 0.05), vec3(0.75, 0.35, 1.0), rim);
	ALPHA = clamp((0.55 + 0.45 * rim) * fade, 0.0, 1.0);
}";

    public override void _Ready()
    {
        shader ??= new Shader { Code = Code };
        mat = new ShaderMaterial { Shader = shader };
        Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 };
        MaterialOverride = mat;
        CastShadow = ShadowCastingSetting.Off;
        Scale = Vector3.One * 0.05f;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float grow = Mathf.Clamp(t / 0.7f, 0f, 1f);
        float r = Mathf.Max(0.05f, Radius * (1f - Mathf.Pow(1f - grow, 3f)));
        Scale = Vector3.One * r;
        mat.SetShaderParameter("fade", t < 1.2f ? 1f : Mathf.Max(0f, 1f - (t - 1.2f) / 1.0f));
        if (t > 2.3f) QueueFree();
    }
}
