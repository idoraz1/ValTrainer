using Godot;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Materials for one viewmodel. Everything uses viewmodel.gdshader (own FOV + depth squeeze → no wall
/// clipping); the gun's flat FBX colours are re-mapped to PBR-ish metal / polymer settings by surface name.
/// All ShaderMaterials of one viewmodel are tracked so the FOV and the muzzle-flash fill can be updated.
/// </summary>
public sealed class VmMaterials
{
    public const string ShaderPath = "res://assets/shaders/weapon/viewmodel.gdshader";
    public const string FlashShaderPath = "res://assets/shaders/weapon/viewmodel_flash.gdshader";

    static Shader? shader, flashShader;
    public static Shader Shader => shader ??= GD.Load<Shader>(ShaderPath);
    public static Shader FlashShader => flashShader ??= GD.Load<Shader>(FlashShaderPath);

    /// <summary>Gold finish (Chamber's Headhunter / Tour De Force on the Sheriff / Operator model).</summary>
    public bool Gold;
    readonly List<ShaderMaterial> lit = new();
    readonly List<ShaderMaterial> all = new();
    float focal = 1.428f, flash;

    public ShaderMaterial Make(Color albedo, float metallic, float roughness, float specular = 0.5f, float detail = 0f, float rim = 0.15f, float detailScale = 90f)
    {
        var m = new ShaderMaterial { Shader = Shader };
        m.SetShaderParameter("albedo", albedo);
        m.SetShaderParameter("metallic", metallic);
        m.SetShaderParameter("roughness", roughness);
        m.SetShaderParameter("specular", specular);
        m.SetShaderParameter("detail", detail);
        m.SetShaderParameter("detail_scale", detailScale);
        m.SetShaderParameter("rim", rim);
        m.SetShaderParameter("vm_focal", focal);
        lit.Add(m); all.Add(m);
        return m;
    }

    public ShaderMaterial MakeFlash(int mode)
    {
        var m = new ShaderMaterial { Shader = FlashShader };
        m.SetShaderParameter("mode", mode);
        m.SetShaderParameter("vm_focal", focal);
        m.SetShaderParameter("intensity", 0f);
        all.Add(m);
        return m;
    }

    /// <summary>Gun surface by FBX material name (see assets/MANIFEST.md §5).</summary>
    public ShaderMaterial ForGunSurface(string name, Color src)
    {
        string k = name.ToLowerInvariant();
        if (Gold)
        {
            // Chamber's custom guns: polished gold metal parts, warm dark-bronze polymer (own tint, no imported art).
            var gold = new Color(0.83f, 0.62f, 0.25f);
            var bronze = new Color(0.16f, 0.12f, 0.08f);
            switch (k)
            {
                case "main": case "mainlight": case "metal": case "lightmetal": case "grey":
                    return Make(gold.Lerp(src, 0.15f), 0.95f, 0.26f, 0.6f, 0.35f, 0.25f);
                case "darkmetal": case "maindark":
                    return Make(gold.Darkened(0.45f), 0.9f, 0.32f, 0.55f, 0.45f, 0.22f);
                case "black": case "green":
                    return Make(bronze, 0.2f, 0.4f, 0.5f, 0.55f, 0.2f);
            }
        }
        return k switch
        {
            "glass" => Make(new Color(0.02f, 0.05f, 0.11f), 0.1f, 0.05f, 1.0f, 0f, 0.6f),
            "green" => Make(src.Darkened(0.1f), 0.0f, 0.55f, 0.45f, 0.55f, 0.12f),
            "black" => Make(new Color(0.055f, 0.057f, 0.062f), 0.0f, 0.42f, 0.5f, 0.6f, 0.2f),
            "main" => Make(src.Darkened(0.1f), 0.6f, 0.36f, 0.5f, 0.5f, 0.2f),
            "maindark" => Make(src.Darkened(0.35f), 0.1f, 0.45f, 0.5f, 0.55f, 0.18f),
            "mainlight" => Make(src.Lightened(0.12f), 0.85f, 0.34f, 0.5f, 0.5f, 0.2f),
            "darkmetal" => Make(src.Lightened(0.08f), 0.88f, 0.3f, 0.5f, 0.6f, 0.2f),
            "metal" => Make(src.Lightened(0.25f), 0.95f, 0.24f, 0.5f, 0.55f, 0.2f),
            "lightmetal" => Make(src.Lightened(0.3f), 0.95f, 0.22f, 0.5f, 0.45f, 0.2f),
            "grey" => Make(src, 0.7f, 0.32f, 0.5f, 0.4f, 0.15f),
            _ => Make(src, 0.4f, 0.4f, 0.5f, 0.4f, 0.15f),
        };
    }

    /// <summary>Viewmodel vertical FOV in degrees.</summary>
    public void SetFov(float vfovDeg)
    {
        float f = 1f / Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(vfovDeg, 40f, 110f)) / 2f);
        if (Mathf.IsEqualApprox(f, focal)) return;
        focal = f;
        foreach (var m in all) m.SetShaderParameter("vm_focal", f);
    }

    public float Focal => focal;

    /// <summary>Warm fill from the muzzle flash (0..1).</summary>
    public void SetFlash(float f)
    {
        if (Mathf.Abs(f - flash) < 0.004f) return;
        flash = f;
        foreach (var m in lit) m.SetShaderParameter("flash", f);
    }
}
