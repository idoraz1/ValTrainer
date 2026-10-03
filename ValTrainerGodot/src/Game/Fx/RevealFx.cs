using Godot;
using ValTrainer.Game.Bots;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Recon reveal look: a through-walls pass (assets/shaders/characters/reveal_xray.gdshader) chained in front of each
/// enemy's highlight overlay. <see cref="Attach"/> once per bot (it then costs nothing while hidden), <see cref="Set"/>
/// to show / fade it.
/// </summary>
public static class RevealFx
{
    const string ShaderPath = "res://assets/shaders/characters/reveal_xray.gdshader";
    static Shader? shader;
    static readonly Dictionary<Material, ShaderMaterial> heads = new();
    static readonly StringName RevealParam = "hl_reveal";

    static Shader Xray => shader ??= GD.Load<Shader>(ShaderPath);

    /// <summary>Chains the reveal pass onto this bot's highlighted meshes (idempotent).</summary>
    public static void Attach(BotCharacter b, Color c)
    {
        if (!GodotObject.IsInstanceValid(b)) return;
        foreach (var g in b.HighlightMeshes)
        {
            if (!GodotObject.IsInstanceValid(g)) continue;
            var ov = g.MaterialOverlay;
            if (ov == null || ov is ShaderMaterial { Shader: var s } && s == Xray) continue;
            if (!heads.TryGetValue(ov, out var head))
            {
                head = new ShaderMaterial { Shader = Xray, NextPass = ov };
                head.SetShaderParameter("reveal_color", c);
                heads[ov] = head;
            }
            g.MaterialOverlay = head;
            g.SetInstanceShaderParameter(RevealParam, 0f);
        }
    }

    /// <summary>0 = hidden, 1 = fully revealed.</summary>
    public static void Set(BotCharacter b, float amount)
    {
        if (!GodotObject.IsInstanceValid(b)) return;
        foreach (var g in b.HighlightMeshes)
            if (GodotObject.IsInstanceValid(g)) g.SetInstanceShaderParameter(RevealParam, Mathf.Clamp(amount, 0f, 1f));
    }
}

/// <summary>
/// Fade's Terror Trail: a dark smoky ribbon on the ground from where the Haunt landed to a marked enemy (12 s).
/// Depth-tested (it's on the floor), drawn with its own flowing shader.
/// </summary>
public partial class TerrorTrail : MeshInstance3D
{
    static Shader? shader;
    const string Code = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform float fade = 1.0;
uniform vec3 edge : source_color = vec3(0.3, 0.9, 0.85);
void fragment() {
	float x = abs(UV.x - 0.5) * 2.0;
	float n = 0.5 + 0.5 * sin(UV.y * 2.4 - TIME * 5.0 + sin(UV.y * 6.0 + TIME) * 1.5);
	float a = (1.0 - smoothstep(0.3, 1.0, x)) * (0.55 + 0.45 * n) * fade;
	ALBEDO = mix(vec3(0.03, 0.01, 0.06), edge, smoothstep(0.55, 0.95, x) * 0.55 + 0.18 * n);
	ALPHA = clamp(a * 0.9, 0.0, 1.0);
}";

    public const float Life = 12f;
    float t;
    ShaderMaterial mat = null!;

    public static TerrorTrail? Spawn(IGame g, Vector3 from, Vector3 to)
    {
        float len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        if (len < 0.5f) return null;
        shader ??= new Shader { Code = Code };
        var im = new ImmediateMesh();
        var side = new Vector3(-(to.Z - from.Z), 0, to.X - from.X).Normalized() * 0.2f;
        int n = Math.Max(2, (int)(len / 0.4f) + 1);
        im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)(n - 1);
            var p = from.Lerp(to, k);
            float refY = Mathf.Lerp(from.Y, to.Y, k) + 0.5f;
            p.Y = Collision.Ground(new Vector3(p.X, refY, p.Z), g.Solid) + 0.035f;
            float along = k * len;
            // Thin at both ends.
            float w = Mathf.Clamp(Mathf.Min(along, len - along) / 1.2f, 0.25f, 1f);
            im.SurfaceSetUV(new Vector2(0f, along));
            im.SurfaceAddVertex(p - side * w);
            im.SurfaceSetUV(new Vector2(1f, along));
            im.SurfaceAddVertex(p + side * w);
        }
        im.SurfaceEnd();
        var trail = new TerrorTrail { Name = "TerrorTrail", Mesh = im, CastShadow = ShadowCastingSetting.Off, ExtraCullMargin = 4f };
        trail.mat = new ShaderMaterial { Shader = shader };
        trail.MaterialOverride = trail.mat;
        g.World.AddChild(trail);
        return trail;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float fadeIn = Mathf.Clamp(t / 0.3f, 0f, 1f), fadeOut = Mathf.Clamp((Life - t) / 1.2f, 0f, 1f);
        mat.SetShaderParameter("fade", fadeIn * fadeOut);
        if (t >= Life) QueueFree();
    }
}
