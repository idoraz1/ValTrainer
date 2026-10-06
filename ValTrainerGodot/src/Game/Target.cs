using Godot;
using ValTrainer.Game.Bots;

namespace ValTrainer.Game;

/// <summary>
/// A spherical aim-trainer target. Two looks (Settings → Enemies → Target look, <see cref="Core.AppSettings.TargetStyle"/>):
/// <list type="bullet">
/// <item>"valorant" (default): a dark neutral body with the enemy-colour fresnel rim and a crisp enemy-colour outline,
/// the same signature as VALORANT enemies and the bots, so the target reads by its luminance edge on any background
/// (assets/shaders/characters/target_valorant.gdshader; ray-traced, so the silhouette is exactly the hit sphere and
/// the outline sits inside it).</item>
/// <item>"classic": a glossy sphere filled with the enemy colour and a soft rim.</item>
/// </list>
/// </summary>
public partial class Target : Node3D
{
    public const string StyleValorant = "valorant", StyleClassic = "classic";

    public float Radius { get; private set; }
    public float SpawnTime;
    MeshInstance3D mesh = null!;
    StandardMaterial3D? mat;      // classic
    ShaderMaterial? vmat;         // valorant
    float flash;
    bool hot;
    Color baseColor;

    static Shader? valorantShader;
    static readonly StringName FlashParam = "flash", HotParam = "hot";

    /// <summary>Dark neutral body (sRGB ≈ 44,46,52 albedo → about L* 30 lit in the Range).</summary>
    static readonly Color Body = Color.Color8(44, 46, 52);

    public static Target Create(float radius, Color color) =>
        Create(radius, color, Main.I?.Settings.TargetStyle != StyleClassic);

    public static Target Create(float radius, Color color, bool valorantLook)
    {
        var t = new Target { Radius = radius, baseColor = color };
        Material m;
        if (valorantLook)
        {
            valorantShader ??= GD.Load<Shader>("res://assets/shaders/characters/target_valorant.gdshader");
            t.vmat = new ShaderMaterial { Shader = valorantShader };
            t.vmat.SetShaderParameter("enemy_color", color);
            t.vmat.SetShaderParameter("body_color", Body);
            t.vmat.SetShaderParameter("rim_strength", BotAssets.RimStrength);
            m = t.vmat;
        }
        else
        {
            m = t.mat = new StandardMaterial3D
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
        }
        t.mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = m,
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
    public void SetHot(bool on)
    {
        if (on == hot) return;
        hot = on;
        if (mat != null) mat.EmissionEnergyMultiplier = on ? 1.2f : 0.25f;
        vmat?.SetShaderParameter(HotParam, on ? 1f : 0f);
    }

    public override void _Process(double delta)
    {
        if (flash <= 0) return;
        flash -= (float)delta;
        if (mat != null) mat.AlbedoColor = flash > 0 ? Colors.White : baseColor;
        vmat?.SetShaderParameter(FlashParam, flash > 0 ? 1f : 0f);
    }
}
