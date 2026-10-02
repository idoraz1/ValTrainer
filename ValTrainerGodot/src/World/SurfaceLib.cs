using Godot;

namespace ValTrainer.World;

/// <summary>
/// A world surface look: one Poly Haven PBR set (res://assets/textures/&lt;Tex&gt;) re-coloured to a palette tint.
/// Value-equal records double as the material cache key.
/// </summary>
/// <param name="Tile">Metres covered by one texture repeat (UVs are world metres).</param>
/// <param name="TintAmount">0 = raw texture colour, 1 = texture detail around the tint colour.</param>
/// <param name="Contrast">Texture detail contrast around its mean (lower = cleaner, more stylized).</param>
public sealed record Surface(string Tex, Color Tint, float TintAmount = 0.8f, float Tile = 2f, float Contrast = 1f,
    float Rough = 0f, float Metal = 1f, float Normal = 1f, float Macro = 0.1f, float Grime = 0.22f, float RoughScale = 1f)
{
    public Surface With(Color tint) => this with { Tint = tint };
}

/// <summary>Creates and caches every world material once (shader materials share one shader + textures).</summary>
public static class SurfaceLib
{
    public const string ShaderDir = "res://assets/shaders/world/";

    /// <summary>Average albedo (sRGB), roughness and metallic of each set, measured offline. The shader swaps the
    /// mean colour for the tint; the Low preset uses the mean roughness/metal instead of sampling the ARM map.</summary>
    static readonly Dictionary<string, (Color Mean, float Rough, float Metal)> Stats = new()
    {
        ["clay_plaster"] = (new Color(0.448f, 0.353f, 0.235f), 0.92f, 0f),
        ["cobblestone_floor_06"] = (new Color(0.314f, 0.275f, 0.238f), 0.80f, 0f),
        ["concrete_floor_worn_02"] = (new Color(0.487f, 0.421f, 0.337f), 0.77f, 0f),
        ["concrete_wall_008"] = (new Color(0.552f, 0.524f, 0.438f), 0.74f, 0f),
        ["metal_plate_02"] = (new Color(0.331f, 0.286f, 0.243f), 0.66f, 0.91f),
        ["painted_plaster_wall"] = (new Color(0.670f, 0.639f, 0.626f), 0.92f, 0f),
        ["plastered_wall_02"] = (new Color(0.731f, 0.695f, 0.641f), 0.91f, 0f),
        ["red_sandstone_wall"] = (new Color(0.557f, 0.496f, 0.426f), 0.84f, 0f),
        ["rusty_metal_sheet"] = (new Color(0.468f, 0.433f, 0.337f), 0.82f, 0f),
        ["sandstone_blocks_08"] = (new Color(0.648f, 0.574f, 0.457f), 0.51f, 0f),
        ["square_tiles"] = (new Color(0.417f, 0.380f, 0.341f), 0.83f, 0f),
        ["stone_tiles_03"] = (new Color(0.526f, 0.480f, 0.389f), 0.86f, 0f),
        ["wood_floor_worn"] = (new Color(0.540f, 0.348f, 0.185f), 0.47f, 0f),
        ["wood_planks"] = (new Color(0.475f, 0.348f, 0.225f), 0.86f, 0f),
    };

    /// <summary>Use the cheap shader variant (Low preset). Set before building a world.</summary>
    public static bool Lite;

    static Shader? surfaceShader, liteShader, skyShader;
    static Texture2D? macro;
    static readonly Dictionary<string, Texture2D?> textures = new();
    static readonly Dictionary<(Surface, bool), Material> surfaces = new();
    static readonly Dictionary<(Color, float, float, float), StandardMaterial3D> flats = new();

    public static Shader SkyShader => skyShader ??= GD.Load<Shader>(ShaderDir + "sky.gdshader");

    static Texture2D? Tex(string path)
    {
        if (textures.TryGetValue(path, out var t)) return t;
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (t == null) GD.PushWarning($"[world] missing texture {path}");
        textures[path] = t;
        return t;
    }

    /// <summary>Fallback for the macro-variation texture if it has not been imported yet (no variation).</summary>
    static Texture2D NeutralGrey() => ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgb8).Also(i => i.Fill(new Color(0.5f, 0.5f, 0.5f))));

    static Shader LiteShader()
    {
        // Dev A/B: --envtweak fulllite = the lite variant keeping normal maps, Burley diffuse and specular.
        if (!EnvDev.Has("fulllite")) return GD.Load<Shader>(ShaderDir + "surface_lite.gdshader");
        return new Shader { Code = $"shader_type spatial;\n#define LITE\n#include \"{ShaderDir}surface.gdshaderinc\"\n" };
    }

    /// <summary>Textured PBR material for box geometry with world-metre UVs (cached per surface and variant).</summary>
    public static Material Get(Surface s)
    {
        bool lite = Lite;
        if (surfaces.TryGetValue((s, lite), out var cached)) return cached;
        var shader = lite ? liteShader ??= LiteShader()
                          : surfaceShader ??= GD.Load<Shader>(ShaderDir + "surface.gdshader");
        string dir = $"res://assets/textures/{s.Tex}/{s.Tex}";
        var albedo = Tex($"{dir}_diff_2k.jpg");
        var stats = Stats.TryGetValue(s.Tex, out var st) ? st : (new Color(0.5f, 0.5f, 0.5f), 0.85f, 0f);
        Material m;
        if (shader == null || albedo == null || EnvDev.Has("flat"))
        {
            m = new StandardMaterial3D { AlbedoColor = s.Tint, Roughness = 0.85f };
        }
        else
        {
            var sm = new ShaderMaterial { Shader = shader };
            sm.SetShaderParameter("tex_albedo", albedo);
            if (!lite || EnvDev.Has("fulllite")) sm.SetShaderParameter("tex_normal", Tex($"{dir}_nor_gl_2k.jpg"));
            if (!lite)
            {
                macro ??= Tex("res://assets/materials/macro_noise.png") ?? NeutralGrey();
                sm.SetShaderParameter("tex_arm", Tex($"{dir}_arm_2k.jpg"));
                sm.SetShaderParameter("tex_macro", macro);
            }
            sm.SetShaderParameter("tint", s.Tint);
            sm.SetShaderParameter("tex_mean", stats.Item1);
            sm.SetShaderParameter("rough_mean", stats.Item2);
            sm.SetShaderParameter("metal_mean", stats.Item3);
            sm.SetShaderParameter("tint_amount", s.TintAmount);
            sm.SetShaderParameter("contrast", s.Contrast);
            sm.SetShaderParameter("tile_m", s.Tile);
            sm.SetShaderParameter("normal_depth", s.Normal);
            sm.SetShaderParameter("rough_offset", s.Rough);
            sm.SetShaderParameter("rough_scale", s.RoughScale);
            sm.SetShaderParameter("metal_scale", s.Metal);
            sm.SetShaderParameter("macro_strength", s.Macro);
            sm.SetShaderParameter("grime_strength", s.Grime);
            m = sm;
        }
        surfaces[(s, lite)] = m;
        return m;
    }

    /// <summary>Untextured painted/metal material (small trims, frames, window glass).</summary>
    public static StandardMaterial3D Flat(Color c, float rough = 0.75f, float metal = 0f) => Cached(c, rough, metal, 0f);

    /// <summary>Emissive material (neon, lamp glass, markings that should stay readable in shadow).</summary>
    public static StandardMaterial3D Glow(Color c, float energy) => Cached(c, 0.5f, 0f, energy);

    static StandardMaterial3D Cached(Color c, float rough, float metal, float glow)
    {
        var key = (c, rough, metal, glow);
        if (flats.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough, Metallic = metal, MetallicSpecular = 0.5f };
        if (glow > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = c;
            m.EmissionEnergyMultiplier = glow;
        }
        flats[key] = m;
        return m;
    }
}

static class ImageExt
{
    public static Image Also(this Image img, Action<Image> f) { f(img); return img; }
}
