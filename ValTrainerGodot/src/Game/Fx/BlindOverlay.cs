using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Full-screen flash white-out: full strength for the blind time (with the patch-5.07 indicator — the periphery
/// starts clearing from the screen edges inward, showing when it will end), then a fade (≈1 s for a full flash,
/// shorter for brief minimum blinds) in which the edges recover first. Look: hot, very slightly warm white.
/// </summary>
public partial class BlindOverlay : Control
{
    float full, total, fade = BlindModel.FadeTime, t = 1e9f;
    bool wasActive;
    ShaderMaterial? mat;

    public bool Active => t < full + fade;
    public bool FullyBlind => t < full;

    const string Code = @"
shader_type canvas_item;
uniform float strength = 1.0;
uniform float edge = 0.0;
uniform float fading = 0.0;
uniform float aspect = 1.7778;
void fragment() {
	vec2 p = (SCREEN_UV - 0.5) * vec2(aspect, 1.0);
	float r = length(p) / (0.5 * length(vec2(aspect, 1.0)));
	float radius = mix(1.25, 0.1, edge);
	float outside = smoothstep(radius - 0.2, radius + 0.02, r);
	float a = mix(0.975, 0.8, outside);
	a = mix(a, a * (1.0 - 0.6 * smoothstep(0.1, 1.0, r)), fading);
	vec3 c = mix(vec3(1.0, 0.988, 0.96), vec3(1.0), smoothstep(0.75, 0.0, r));
	c = mix(c, c * vec3(0.9, 0.91, 0.93), outside);
	COLOR = vec4(c, clamp(a * strength, 0.0, 1.0));
}";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        Material = mat;
    }

    public void Apply(float seconds)
    {
        if (seconds <= 0) return;
        if (!Active || seconds > full - t)
        {
            full = seconds; total = seconds; t = 0;
            fade = Mathf.Min(BlindModel.FadeTime, 0.3f + seconds * 0.5f);
        }
    }

    public void Reset() { t = 1e9f; QueueRedraw(); }

    public override void _Process(double delta)
    {
        t += (float)delta;
        bool active = Active;
        if (active || wasActive) QueueRedraw();
        wasActive = active;
    }

    public override void _Draw()
    {
        if (!Active) return;
        var size = GetViewportRect().Size;
        float strength, edge, fading;
        if (t < full)
        {
            strength = 1f;
            edge = Mathf.Clamp(t / Mathf.Max(0.01f, total), 0f, 1f);
            fading = 0f;
        }
        else
        {
            float a = 1f - (t - full) / fade;
            strength = a * a * (3f - 2f * a);   // smooth ease-out
            edge = 1f;
            fading = 1f;
        }
        if (mat != null)
        {
            mat.SetShaderParameter("strength", strength);
            mat.SetShaderParameter("edge", edge);
            mat.SetShaderParameter("fading", fading);
            mat.SetShaderParameter("aspect", size.X / Mathf.Max(1f, size.Y));
            DrawRect(new Rect2(Vector2.Zero, size), Colors.White);
        }
        else DrawRect(new Rect2(Vector2.Zero, size), new Color(1, 1, 1, 0.95f * strength));
    }
}
