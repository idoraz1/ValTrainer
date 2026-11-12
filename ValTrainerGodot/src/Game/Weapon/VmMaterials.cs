using Godot;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Materials for one viewmodel. Everything uses viewmodel.gdshader (own FOV + depth squeeze → no wall
/// clipping); the gun's flat FBX colours are re-mapped to PBR-ish metal / polymer settings by surface name.
/// All ShaderMaterials of one viewmodel are tracked so the FOV and the muzzle-flash fill can be updated.
/// <para>Gun skins (<see cref="GunSkins"/>): with <see cref="Skin"/> set, surfaces are mapped to a role by FBX name
/// (<see cref="RoleOf"/>). Solid skins use the same shader with other numbers; patterned ones use viewmodel_skin.gdshader
/// (viewmodel_skin_lite on Low quality). Without a skin (factory) the code path is exactly the one before skins.</para>
/// </summary>
public sealed class VmMaterials
{
    public const string ShaderPath = "res://assets/shaders/weapon/viewmodel.gdshader";
    public const string FlashShaderPath = "res://assets/shaders/weapon/viewmodel_flash.gdshader";
    public const string SkinShaderPath = "res://assets/shaders/weapon/viewmodel_skin.gdshader";
    public const string SkinLiteShaderPath = "res://assets/shaders/weapon/viewmodel_skin_lite.gdshader";
    public const string TexShaderPath = "res://assets/shaders/weapon/viewmodel_tex.gdshader";

    static Shader? shader, flashShader, skinShader, skinLiteShader, texShader;
    public static Shader TexShader => texShader ??= GD.Load<Shader>(TexShaderPath);
    public static Shader Shader => shader ??= GD.Load<Shader>(ShaderPath);
    public static Shader FlashShader => flashShader ??= GD.Load<Shader>(FlashShaderPath);
    public static Shader SkinShader => skinShader ??= GD.Load<Shader>(SkinShaderPath);
    public static Shader SkinLiteShader => skinLiteShader ??= GD.Load<Shader>(SkinLiteShaderPath);

    /// <summary>What a gun surface is, from its FBX material name (skins-design-1.19.md §2.1).</summary>
    public enum Role
    {
        /// <summary>Polymer and wood furniture: the skin's pattern.</summary>
        Body,
        /// <summary>Receiver metal: a solid finish, or the pattern (<see cref="GunSkin.PatternOnTrim"/>).</summary>
        Trim,
        /// <summary>Barrel, bolt, iron sights, rails: factory unless the skin sets <see cref="GunSkin.Hardware"/>.</summary>
        Hardware,
        /// <summary>Scope lenses: never skinned.</summary>
        Glass,
    }

    public static Role RoleOf(string fbxName) => fbxName.ToLowerInvariant() switch
    {
        "black" or "black2" or "green" or "wood" or "darkwood" or "main" or "maindark" => Role.Body,
        "metal" or "lightmetal" or "mainlight" => Role.Hardware,
        "glass" => Role.Glass,
        _ => Role.Trim, // darkmetal, grey and names of future models
    };

    /// <summary>Gold finish (Chamber's Headhunter / Tour De Force on the Sheriff / Operator model).</summary>
    public bool Gold;
    /// <summary>Skinned: the receiver shell (<c>darkmetal</c>, <c>grey</c>) takes the Body role (<see cref="VmPose.ReceiverIsBody"/>).</summary>
    public bool ReceiverIsBody;
    /// <summary>The gun's skin; null = factory. Ignored for <see cref="Gold"/>.</summary>
    public GunSkin? Skin;
    /// <summary>Low quality: the cheaper skin shader (one noise octave, no sparkle or animation).</summary>
    public bool Lite = (Main.I?.Settings?.Quality ?? 1) <= 0;
    /// <summary>Fraction of the depth range the viewmodel may use (1 = normal depth: the skin picker's preview).</summary>
    public float Depth = 0.01f;
    readonly List<ShaderMaterial> lit = new();
    readonly List<ShaderMaterial> all = new();
    /// <summary>Skin-shader materials whose look has level effects (<see cref="NeedsDynamics"/>): the only ones that get
    /// <see cref="SetSkinDynamics"/> / <see cref="SetCounter"/> (factory and Select skins never do).</summary>
    readonly List<ShaderMaterial> dyn = new();
    float focal = 1.428f, flash;
    float dFire = 9f, dKill = 9f, dSlide, dAds;
    Vector3 cVal;
    float cFlash;
    bool cOn;

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
        if (Depth != 0.01f) m.SetShaderParameter("vm_depth", Depth);
        lit.Add(m); all.Add(m);
        return m;
    }

    /// <summary>A modelled skin's surface (viewmodel_tex.gdshader): the model's own base colour and emissive textures.
    /// <paramref name="anim"/> &gt; 0 makes the emissive parts flow (Apex level 2); <paramref name="react"/> / <paramref name="killGlow"/>
    /// pulse them on shots / kills.</summary>
    public ShaderMaterial MakeTextured(Texture2D albedoTex, Texture2D? emissionTex, float metallic, float roughness, float anim = 0f, float react = 0f, float killGlow = 0f, float rim = 0.15f,
        Texture2D? normalTex = null, Texture2D? mrTex = null)
    {
        var m = new ShaderMaterial { Shader = TexShader };
        if (normalTex != null) { m.SetShaderParameter("normal_tex", normalTex); m.SetShaderParameter("use_normal", 1f); }
        if (mrTex != null) { m.SetShaderParameter("mr_tex", mrTex); m.SetShaderParameter("use_mr", 1f); }
        m.SetShaderParameter("albedo", Colors.White);
        m.SetShaderParameter("albedo_tex", albedoTex);
        if (emissionTex != null) m.SetShaderParameter("emission_tex", emissionTex);
        m.SetShaderParameter("tex_glow", emissionTex != null ? 2.5f : 0f);
        m.SetShaderParameter("metallic", metallic);
        m.SetShaderParameter("roughness", roughness);
        m.SetShaderParameter("specular", 0.5f);
        m.SetShaderParameter("rim", rim);
        m.SetShaderParameter("anim", Lite ? 0f : anim);
        m.SetShaderParameter("vm_focal", focal);
        if (Depth != 0.01f) m.SetShaderParameter("vm_depth", Depth);
        if (react > 0f || killGlow > 0f)
        {
            m.SetShaderParameter("react", react);
            m.SetShaderParameter("kill_glow", killGlow);
            m.SetShaderParameter("fire_t", dFire);
            m.SetShaderParameter("kill_t", dKill);
            m.SetShaderParameter("vm_ads", dAds);
            dyn.Add(m);
        }
        lit.Add(m); all.Add(m);
        return m;
    }

    /// <summary>Dev test of a modelled skin (<c>--vm-model foundry|aurora|orrery</c>, <c>-anim</c> suffix = level 2 motion): see
    /// VmPose.ModelTest.</summary>
    public static readonly string? DevModel = ValTrainer.Core.CmdLine.DevAfter("--vm-model");
    /// <summary><see cref="DevModel"/> without the <c>-anim</c> suffix (the model's folder under assets/weapons).</summary>
    public static string? DevModelName => DevModel?.Replace("-anim", "").ToLowerInvariant();

    ShaderMaterial Make(SkinFinish f, float wear, float rim = 0.18f) => Make(f.Color, f.Metallic, f.Roughness, f.Specular, wear, rim);

    /// <summary>A patterned skin material (viewmodel_skin.gdshader). The per-surface parameters (skin_xform, gun_min/max,
    /// skin_seed) are set by the viewmodel; the defaults are gun space = mesh space.</summary>
    public ShaderMaterial MakeSkin(GunSkin skin, float rim = 0.18f)
    {
        var m = new ShaderMaterial { Shader = Lite ? SkinLiteShader : SkinShader };
        var a = skin.A;
        var b = skin.B ?? a;
        var c = skin.C ?? b;
        var d = skin.D ?? c;
        m.SetShaderParameter("skin_pattern", (int)skin.Pattern);
        m.SetShaderParameter("col_a", a.Color);
        m.SetShaderParameter("col_b", b.Color);
        m.SetShaderParameter("col_c", c.Color);
        m.SetShaderParameter("col_d", d.Color);
        m.SetShaderParameter("mr_a", new Vector3(a.Metallic, a.Roughness, a.Specular));
        m.SetShaderParameter("mr_b", new Vector3(b.Metallic, b.Roughness, b.Specular));
        m.SetShaderParameter("mr_c", new Vector3(c.Metallic, c.Roughness, c.Specular));
        m.SetShaderParameter("skin_scale", skin.Scale);
        m.SetShaderParameter("skin_mid", skin.Mid);
        m.SetShaderParameter("glow_color", skin.Glow ?? Colors.Black);
        m.SetShaderParameter("glow_energy", skin.GlowEnergy);
        m.SetShaderParameter("anim", skin.Anim);
        m.SetShaderParameter("detail", skin.Wear);
        m.SetShaderParameter("detail_scale", 90f);
        m.SetShaderParameter("rim", rim);
        m.SetShaderParameter("vm_focal", focal);
        if (Depth != 0.01f) m.SetShaderParameter("vm_depth", Depth);
        // skins 2: pattern variation and level effects (all 0 = the level-1 look)
        if (skin.Sub != 0) m.SetShaderParameter("skin_sub", skin.Sub);
        if (NeedsDynamics(skin))
        {
            m.SetShaderParameter("react", skin.React);
            m.SetShaderParameter("kill_glow", skin.KillGlow);
            m.SetShaderParameter("fire_t", dFire);
            m.SetShaderParameter("kill_t", dKill);
            m.SetShaderParameter("kill_slide", dSlide);
            m.SetShaderParameter("vm_ads", dAds);
            dyn.Add(m);
        }
        lit.Add(m); all.Add(m);
        return m;
    }

    /// <summary>Whether a look has per-frame level effects (fire / kill pulses, the kill slide, the counter).</summary>
    public static bool NeedsDynamics(GunSkin skin) => skin.React > 0f || skin.KillGlow > 0f || (skin.Animated && skin.Pattern is SkinPattern.Orrery);

    /// <summary>Whether <paramref name="m"/> uses a skin shader (it then needs the per-surface gun-space parameters).</summary>
    public static bool IsSkin(ShaderMaterial m) => m.Shader == skinShader || m.Shader == skinLiteShader;

    /// <summary>The material of one surface of <paramref name="skin"/> by role, or null = factory for that surface.</summary>
    public ShaderMaterial? ForSkinSurface(GunSkin skin, Role role) => role switch
    {
        Role.Glass => null,
        Role.Hardware => skin.PatternOnHardware ? MakeSkin(skin) : skin.Hardware is { } h ? Make(h, skin.Wear * 0.8f) : null,
        Role.Trim => skin.Patterned && skin.PatternOnTrim ? MakeSkin(skin) : Make(skin.Trim ?? skin.A, skin.Wear * 0.8f),
        _ => skin.Patterned ? MakeSkin(skin) : Make(skin.A, skin.Wear),
    };

    /// <summary>Stops updating these materials (a re-skinned gun drops its old surfaces).</summary>
    public void Forget(IEnumerable<ShaderMaterial> mats)
    {
        var set = mats.ToHashSet();
        lit.RemoveAll(set.Contains);
        all.RemoveAll(set.Contains);
        dyn.RemoveAll(set.Contains);
        counterMats.RemoveWhere(set.Contains);
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
        else if (Skin != null && ForSkinSurface(Skin, ReceiverIsBody && k is "darkmetal" or "grey" or "metal" ? Role.Body : RoleOf(k)) is { } skinned) return skinned;
        return k switch
        {
            "glass" => Make(new Color(0.02f, 0.05f, 0.11f), 0.1f, 0.05f, 1.0f, 0f, 0.6f),
            "green" => Make(src.Darkened(0.1f), 0.0f, 0.55f, 0.45f, 0.55f, 0.12f),
            "black" => Make(new Color(0.055f, 0.057f, 0.062f), 0.0f, 0.42f, 0.5f, 0.6f, 0.2f),
            "black2" => Make(new Color(0.035f, 0.036f, 0.04f), 0.0f, 0.4f, 0.5f, 0.6f, 0.2f),
            // Wood furniture (stocks, grips, pumps of the 1.18 guns): unvarnished, no metal, a little grain from the detail noise.
            "wood" => Make(src.Lightened(0.06f), 0.0f, 0.62f, 0.4f, 0.7f, 0.12f),
            "darkwood" => Make(src, 0.0f, 0.58f, 0.4f, 0.7f, 0.12f),
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

    /// <summary>Whether any material takes the skin level effects (the viewmodel skips the per-frame work otherwise).</summary>
    public bool HasDynamics => dyn.Count > 0;

    /// <summary>Skin level effects (skins2-design §4.4): seconds since the last shot and the last player kill (both clamped,
    /// so an idle gun sets nothing), the eased kill count (Strata's band slide, Escapement's wheel, Calibrated's scale) and the
    /// eased ADS weight (reactive glow and the counter fade out when aiming). Only skin materials with level effects get them.</summary>
    public void SetSkinDynamics(float fireT, float killT, float killSlide, float ads)
    {
        if (dyn.Count == 0) return;
        fireT = Mathf.Min(fireT, 1f);
        killT = Mathf.Min(killT, 1f);
        if (fireT != dFire) { dFire = fireT; foreach (var m in dyn) m.SetShaderParameter("fire_t", fireT); }
        if (killT != dKill) { dKill = killT; foreach (var m in dyn) m.SetShaderParameter("kill_t", killT); }
        if (Mathf.Abs(killSlide - dSlide) > 1e-4f) { dSlide = killSlide; foreach (var m in dyn) m.SetShaderParameter("kill_slide", killSlide); }
        if (Mathf.Abs(ads - dAds) > 0.002f || (ads == 0f) != (dAds == 0f)) { dAds = ads; foreach (var m in dyn) m.SetShaderParameter("vm_ads", ads); }
    }

    /// <summary>Gun-space display rectangle of the kill counter (z0, y0, z1, y1) and the camera-facing side (-1 = -x): set before
    /// <see cref="MakeCounterPlate"/>.</summary>
    public Vector4 CounterRect = Vector4.Zero;
    public float CounterSide = -1f;

    /// <summary>The material of the kill counter's display module (a small plate on the receiver side, Viewmodel): the skin's
    /// trim as a plain finish with the counter drawn on its camera-facing face. The only material that draws the digits.</summary>
    public ShaderMaterial MakeCounterPlate(GunSkin skin)
    {
        var plate = skin with
        {
            Pattern = SkinPattern.Solid, A = skin.Trim ?? SkinFinish.Of("#111315", 0.5f, 0.4f), B = null, C = null, D = null,
            Wear = 0.1f, React = 0f, KillGlow = 0f, Anim = 0f,
        };
        var m = MakeSkin(plate);
        m.SetShaderParameter("fire_t", dFire);
        m.SetShaderParameter("kill_t", dKill);
        m.SetShaderParameter("vm_ads", dAds);
        if (!dyn.Contains(m)) dyn.Add(m);
        ApplyCounter(m);
        return m;
    }

    /// <summary>The kill counter (skins with <see cref="GunSkin.Counter"/>): two glyphs' 7-segment masks, the "+" over 99, the
    /// increment flash; <paramref name="on"/> = false hides it (Settings → Kill counter off).</summary>
    public void SetCounter(bool on, Vector3 glyphs, float flashK)
    {
        if (on == cOn && glyphs == cVal && Mathf.Abs(flashK - cFlash) < 0.01f) return;
        cOn = on; cVal = glyphs; cFlash = flashK;
        foreach (var m in dyn) if (IsCounter(m)) ApplyCounter(m);
    }

    readonly HashSet<ShaderMaterial> counterMats = new();
    bool IsCounter(ShaderMaterial m) => counterMats.Contains(m);

    void ApplyCounter(ShaderMaterial m)
    {
        counterMats.Add(m);
        m.SetShaderParameter("counter_on", cOn ? 1f : 0f);
        m.SetShaderParameter("counter_rect", CounterRect);
        m.SetShaderParameter("counter_side", CounterSide);
        m.SetShaderParameter("counter_val", cVal);
        m.SetShaderParameter("counter_flash", cFlash);
    }
}
