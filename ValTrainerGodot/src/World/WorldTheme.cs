using Godot;
using ValTrainer.Maps;

namespace ValTrainer.World;

public enum DecorStyle { Range, Ascent, Bind, Haven, Split }

/// <summary>Everything that gives a map its look: surfaces (palette-tinted PBR sets), sky, sun and grade.</summary>
public sealed class Theme
{
    public required string Key { get; init; }
    public DecorStyle Style { get; init; }

    // ---- sky / light ----
    public string Hdri { get; init; } = Hdris.Kloofendal;
    /// <summary>Sky yaw (degrees). The sun follows the HDRI's sun so shadows match the sky.</summary>
    public float SkyYaw { get; init; }
    public Color SkyTint { get; init; } = Colors.White;
    public float SkySaturation { get; init; } = 1.1f;
    public float SkyEnergy { get; init; } = 1f;
    public Color Ground { get; init; } = new(0.36f, 0.33f, 0.29f);
    public Color SunColor { get; init; } = new(1f, 0.95f, 0.86f);
    public float SunEnergy { get; init; } = 2.2f;
    public float Ambient { get; init; } = 1f;
    public float Exposure { get; init; } = 1f;
    public float Saturation { get; init; } = 1.12f;
    public float Contrast { get; init; } = 1.04f;
    public float FogDensity { get; init; } = 0.0012f;

    // ---- surfaces ----
    public required Surface Floor { get; init; }
    public required Surface Wall { get; init; }
    public required Surface Trim { get; init; }
    public required Surface Cover { get; init; }
    public required Surface CoverFrame { get; init; }
    public required Surface Elevated { get; init; }
    public required Surface Base { get; init; }
    public required Surface Cap { get; init; }
    public required Surface GroundSurf { get; init; }
    public float BaseH { get; init; } = 0.8f;
    public float CapH { get; init; } = 0.22f;

    // ---- backdrop (outside the play space) ----
    public required Surface Backdrop { get; init; }
    public required Surface Roof { get; init; }
    public Color[] Hues { get; init; } = { Colors.White };
    public float MinH { get; init; } = 7f;
    public float MaxH { get; init; } = 13f;
    public bool Mountains { get; init; }
    public Color MountainColor { get; init; } = new(0.42f, 0.5f, 0.6f);
}

public static class Hdris
{
    public const string Kloofendal = "kloofendal_48d_partly_cloudy_puresky_2k";
    public const string Qwantani = "qwantani_late_afternoon_puresky_2k";
    public const string Overcast = "overcast_soil_puresky_2k";

    /// <summary>Direction toward the sun in each (unrotated) panorama — measured from the brightest texel.</summary>
    public static Vector3 Sun(string hdri) => hdri switch
    {
        Qwantani => new Vector3(-0.557f, 0.327f, 0.763f),
        Overcast => new Vector3(0.169f, 0.570f, 0.804f),
        _ => new Vector3(-0.377f, 0.742f, 0.555f),
    };
}

public static class Themes
{
    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);
    static Color Scale(Color c, float k) => new(c.R * k, c.G * k, c.B * k);

    public static Theme For(MapSpot m) => m.Look != null ? m.Look(m.Palette) : m.Key switch
    {
        "bind" => Bind(m.Palette),
        "haven" => Haven(m.Palette),
        "split" => Split(m.Palette),
        _ => Ascent(m.Palette),
    };

    /// <summary>Venice-like: warm stucco, stone bases, terracotta copings, cobbled streets, pale crates.</summary>
    static Theme Ascent(MapPalette p) => new()
    {
        Key = "ascent", Style = DecorStyle.Ascent, SkyYaw = 0,
        SunColor = new Color(1f, 0.93f, 0.82f), SunEnergy = 1.7f, Ground = C(120, 108, 92),
        Floor = new Surface("cobblestone_floor_06", Scale(p.Floor, 1.3f), 0.85f, 2.2f, 0.55f, Normal: 0.9f, Macro: 0.12f),
        Wall = new Surface("plastered_wall_02", p.Wall, 0.9f, 2.6f, 1.25f, Normal: 0.8f, Macro: 0.1f),
        Base = new Surface("sandstone_blocks_08", C(200, 186, 160), 0.6f, 2.4f, 0.9f, Rough: 0.2f, Grime: 0.3f),
        Cap = new Surface("red_sandstone_wall", p.Trim, 0.85f, 1.6f, 0.7f),
        Trim = new Surface("red_sandstone_wall", p.Trim, 0.8f, 1.8f, 0.8f),
        Elevated = new Surface("square_tiles", Scale(p.Elevated, 1.05f), 0.8f, 1.6f, 0.6f),
        Cover = new Surface("wood_planks", p.Cover, 0.75f, 1.5f, 0.7f, Rough: 0.05f, Grime: 0.1f),
        CoverFrame = new Surface("wood_planks", C(150, 120, 88), 0.8f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new Surface("cobblestone_floor_06", C(130, 118, 102), 0.8f, 2.2f, 0.5f, Macro: 0.15f, Grime: 0f),
        Backdrop = new Surface("plastered_wall_02", Colors.White, 0.9f, 2.6f, 1.4f, Macro: 0.12f),
        Roof = new Surface("red_sandstone_wall", C(176, 92, 62), 0.9f, 1.4f, 0.6f, Grime: 0f),
        Hues = new[] { C(236, 214, 176), C(226, 186, 140), C(232, 170, 140), C(214, 200, 176), C(240, 224, 196), C(206, 160, 110) },
        MinH = 7.5f, MaxH = 14f,
    };

    /// <summary>Moroccan: sandy clay plaster, sandstone pavers, terracotta trims and teal tile bands.</summary>
    static Theme Bind(MapPalette p) => new()
    {
        Key = "bind", Style = DecorStyle.Bind, SkyYaw = 25,
        SunColor = new Color(1f, 0.88f, 0.72f), SunEnergy = 1.75f, Ground = C(150, 122, 90),
        SkyTint = new Color(1f, 0.97f, 0.92f), Saturation = 1.14f,
        Floor = new Surface("sandstone_blocks_08", C(172, 152, 124), 0.85f, 2.6f, 1.35f, Rough: 0.25f, Normal: 1.3f, Macro: 0.16f),
        Wall = new Surface("clay_plaster", p.Wall, 0.9f, 2.4f, 1.25f, Normal: 1.1f, Macro: 0.1f),
        Base = new Surface("square_tiles", p.Elevated, 0.85f, 1.2f, 0.8f, Rough: -0.25f, Grime: 0.12f),
        Cap = new Surface("red_sandstone_wall", p.Trim, 0.85f, 1.6f, 0.7f),
        Trim = new Surface("red_sandstone_wall", p.Trim, 0.8f, 1.8f, 0.8f),
        Elevated = new Surface("square_tiles", p.Elevated, 0.8f, 1.2f, 0.8f),
        Cover = new Surface("wood_planks", Scale(p.Cover, 0.92f), 0.7f, 1.5f, 0.75f, Grime: 0.1f),
        CoverFrame = new Surface("wood_planks", C(130, 92, 62), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new Surface("sandstone_blocks_08", C(176, 146, 108), 0.85f, 3f, 0.6f, Grime: 0f),
        Backdrop = new Surface("clay_plaster", Colors.White, 0.9f, 2.4f, 1.6f, Macro: 0.14f),
        Roof = new Surface("clay_plaster", C(190, 160, 120), 0.9f, 2.4f, 1.2f, Grime: 0f),
        Hues = new[] { C(222, 190, 142), C(206, 170, 124), C(232, 206, 160), C(214, 160, 116), C(240, 222, 188) },
        MinH = 7f, MaxH = 12f, Mountains = true, MountainColor = new Color(0.62f, 0.56f, 0.5f),
    };

    /// <summary>Bhutanese monastery: whitewash, dark timber, red and gold bands, slate pavers.</summary>
    static Theme Haven(MapPalette p) => new()
    {
        Key = "haven", Style = DecorStyle.Haven, SkyYaw = -20,
        SunColor = new Color(1f, 0.96f, 0.9f), SunEnergy = 1.6f, Ground = C(96, 98, 92), Saturation = 1.1f,
        Floor = new Surface("red_sandstone_wall", Scale(p.Floor, 1.08f), 0.9f, 2.0f, 0.85f, Macro: 0.12f),
        Wall = new Surface("painted_plaster_wall", p.Wall, 0.85f, 2.2f, 1.5f, Normal: 0.9f, Macro: 0.08f),
        Base = new Surface("red_sandstone_wall", C(120, 116, 110), 0.85f, 1.6f, 0.8f, Grime: 0.25f),
        Cap = new Surface("wood_planks", p.Trim, 0.85f, 1.5f, 0.6f, Grime: 0f),
        Trim = new Surface("wood_planks", Scale(p.Trim, 0.9f), 0.85f, 1.5f, 0.7f),
        Elevated = new Surface("wood_floor_worn", p.Elevated, 0.7f, 2f, 0.7f),
        Cover = new Surface("wood_planks", p.Cover, 0.75f, 1.5f, 0.75f, Grime: 0.1f),
        CoverFrame = new Surface("wood_planks", C(82, 52, 38), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new Surface("red_sandstone_wall", C(110, 108, 102), 0.85f, 2f, 0.5f, Grime: 0f),
        Backdrop = new Surface("painted_plaster_wall", Colors.White, 0.85f, 2.2f, 1.2f, Macro: 0.1f),
        Roof = new Surface("wood_planks", C(110, 50, 38), 0.9f, 1.5f, 0.6f, Grime: 0f),
        Hues = new[] { C(236, 230, 218), C(228, 220, 204), C(240, 236, 226), C(220, 210, 192) },
        MinH = 7.5f, MaxH = 13f, Mountains = true, MountainColor = new Color(0.4f, 0.48f, 0.56f),
    };

    /// <summary>Tokyo: board-formed concrete, steel trims, rusted metal crates, pink/cyan neon.</summary>
    static Theme Split(MapPalette p) => new()
    {
        Key = "split", Style = DecorStyle.Split, SkyYaw = 55,
        SunColor = new Color(1f, 0.96f, 0.92f), SunEnergy = 1.6f, Ground = C(80, 82, 86),
        SkyTint = new Color(0.96f, 0.98f, 1.03f), Saturation = 1.1f,
        Floor = new Surface("concrete_floor_worn_02", Scale(p.Floor, 1.5f), 0.9f, 2.5f, 0.8f, Macro: 0.14f),
        Wall = new Surface("concrete_wall_008", p.Wall, 0.9f, 2.7f, 1.2f, Macro: 0.1f),
        Base = new Surface("metal_plate_02", C(70, 74, 80), 0.85f, 1.6f, 0.7f, Metal: 0.7f, Grime: 0.15f),
        Cap = new Surface("metal_plate_02", p.Trim, 0.85f, 1.6f, 0.6f, Metal: 0.6f, Grime: 0f),
        Trim = new Surface("metal_plate_02", p.Trim, 0.85f, 2f, 0.7f, Metal: 0.6f),
        Elevated = new Surface("metal_plate_02", C(96, 100, 108), 0.85f, 2f, 0.7f, Metal: 0.6f),
        Cover = new Surface("rusty_metal_sheet", p.Cover, 0.6f, 2f, 0.8f, Grime: 0.1f),
        CoverFrame = new Surface("metal_plate_02", C(64, 70, 78), 0.85f, 1.6f, 0.7f, Metal: 0.6f, Grime: 0f),
        GroundSurf = new Surface("concrete_floor_worn_02", C(84, 86, 90), 0.85f, 2.5f, 0.6f, Grime: 0f),
        Backdrop = new Surface("concrete_wall_008", Colors.White, 0.9f, 2.7f, 1f, Macro: 0.12f),
        Roof = new Surface("metal_plate_02", C(70, 74, 82), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        Hues = new[] { C(176, 180, 186), C(150, 156, 166), C(196, 196, 192), C(132, 140, 152), C(186, 176, 166) },
        MinH = 12f, MaxH = 30f,
    };

    /// <summary>VALORANT-style range: clean light panels, dark wainscot, orange markings.
    /// Contrast rule for the aim targets (dark body + enemy-colour outline): surfaces behind targets are either
    /// light (floor, upper walls: the dark body gives the edge) or deep dark (wainscot, cover panels: the outline gives
    /// it), never mid-dark, where red and purple outlines (luminance ≈ 0.2–0.3) would be near-isoluminant.</summary>
    public static readonly Theme Range = new()
    {
        Key = "range", Style = DecorStyle.Range, SkyYaw = 0,
        SunColor = new Color(1f, 0.96f, 0.9f), SunEnergy = 1.6f, Ground = C(92, 96, 100), FogDensity = 0.0007f,
        Floor = new Surface("concrete_floor_worn_02", C(134, 137, 142), 0.9f, 3f, 0.8f, Macro: 0.12f, Grime: 0f),
        Wall = new Surface("painted_plaster_wall", C(176, 182, 192), 0.9f, 2.2f, 1.2f, Macro: 0.06f),
        Base = new Surface("concrete_wall_008", C(30, 32, 38), 0.9f, 2.7f, 1f, Grime: 0.1f),
        Cap = new Surface("metal_plate_02", C(36, 38, 44), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        Trim = new Surface("metal_plate_02", C(36, 38, 44), 0.85f, 2f, 0.6f, Metal: 0.5f),
        Elevated = new Surface("metal_plate_02", C(90, 94, 102), 0.85f, 2f, 0.6f, Metal: 0.5f),
        Cover = new Surface("painted_plaster_wall", C(34, 37, 44), 0.95f, 2f, 1.2f, Grime: 0.15f),
        CoverFrame = new Surface("metal_plate_02", C(28, 30, 36), 0.85f, 1.6f, 0.6f, Metal: 0.5f, Grime: 0f),
        GroundSurf = new Surface("concrete_floor_worn_02", C(110, 112, 114), 0.85f, 3f, 0.5f, Grime: 0f),
        Backdrop = new Surface("concrete_wall_008", Colors.White, 0.9f, 2.7f, 1f),
        Roof = new Surface("metal_plate_02", C(70, 74, 82), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        BaseH = 1.4f, CapH = 0.35f, Mountains = true, MountainColor = new Color(0.44f, 0.52f, 0.62f),
    };
}
