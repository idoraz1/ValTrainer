using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Screen fog while the camera is inside a smoke (or a gas / water wall): blurs the frame and pulls it toward the smoke's
/// colour, more the deeper you are. Sits under the HUD (canvas layer 4) and is hidden — no screen copy — when you're out.
/// </summary>
public partial class SmokeScreenFog : CanvasLayer
{
    IGame g = null!;
    ColorRect rect = null!;
    ShaderMaterial mat = null!;
    float shown;
    /// <summary>Tint per smoke (the volume's own look); unknown smokes use grey.</summary>
    public readonly Dictionary<SmokeVolume, Color> Tints = new();

    public static SmokeScreenFog Create(IGame g)
    {
        var f = new SmokeScreenFog { g = g, Layer = 4, Name = "SmokeFog" };
        f.mat = new ShaderMaterial { Shader = SmokeAssets.ScreenShader };
        f.mat.SetShaderParameter("noise_tex", SmokeAssets.Noise);
        f.rect = new ColorRect { Material = f.mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Color = Colors.White };
        f.rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        f.AddChild(f.rect);
        return f;
    }

    public override void _Process(double delta)
    {
        float now = g.Now, best = 0f;
        var tint = new Color(0.5f, 0.5f, 0.5f);
        var eye = g.View.Eye;
        foreach (var s in g.Smokes)
        {
            float d = s.Depth(eye, now);
            if (d > best) { best = d; tint = Tints.TryGetValue(s, out var c) ? c : tint; }
        }
        // ease toward the target so crossing the edge doesn't pop
        float target = Mathf.Clamp(best * 1.6f, 0f, 1f);
        shown = Mathf.MoveToward(shown, target, (float)delta * 3.5f);
        rect.Visible = shown > 0.01f;
        if (!rect.Visible) return;
        var size = rect.GetViewportRect().Size;
        mat.SetShaderParameter("amount", shown);
        mat.SetShaderParameter("tint", tint);
        mat.SetShaderParameter("aspect", size.X / Mathf.Max(1f, size.Y));
    }
}
