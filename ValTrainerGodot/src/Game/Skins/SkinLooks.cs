using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// Skins 3 (docs/research/skins3-contracts.md): the level-1 viewmodel look, effect palette and variants of each of the 15
/// lines, by line id, and the Apex level-2 look transform (<see cref="Animated"/>). <b>Owner: Skins visuals agent</b> (with
/// GunSkins.cs's <see cref="SkinPattern"/> and the viewmodel shader). The catalogue (<see cref="SkinCatalog"/>, Skins
/// data/UI agent) reads looks only from here and sets names/blurbs itself (<c>Look(id) with { Name = …, Blurb = … }</c>), so
/// the two agents never edit the same lines.
/// <para>Every look is our own procedural shader finish (no textures, no Riot art, no AI-made asset): the Signature and
/// Apex patterns 17–26 are drawn in viewmodel.gdshaderinc after the concept art in
/// Desktop\ValTrainer-support\higgsfield\concepts-v1 (reference only, never shipped). The glow energies are set so the
/// patterns read at a glance on bright maps too (skins-2 playtest: too faint). A pattern's colour slots (A..D, Glow) are
/// documented on its <see cref="SkinPattern"/> member.</para>
/// </summary>
public static class SkinLooks
{
    static SkinFinish F(string hex, float metallic, float roughness, float specular = 0.5f) => SkinFinish.Of(hex, metallic, roughness, specular);
    static FxPalette Pal(string main, string accent, string alt) => FxPalette.Of(main, accent, alt);

    /// <summary>The 15 line ids in catalogue order: Core (5), Signature (7), Apex (3).</summary>
    public static readonly string[] Core = { "carbon", "desertcamo", "anodized", "damascus", "twotone" };
    public static readonly string[] Signature = { "glasswork", "thunderhead", "basalt", "oscilloscope", "rally", "frostbite", "hive" };
    public static readonly string[] Apex = { "foundry", "aurora", "orrery" };

    static readonly Dictionary<string, GunSkin> looks = new()
    {
        // ---- Core: free, static, look only ----
        ["carbon"] = new("carbon", "", "", SkinPattern.Carbon, F("#0D0E10", 0.2f, 0.2f, 0.65f), F("#3A3D44", 0.25f, 0.18f, 0.65f),
            Trim: F("#0E0F10", 0.6f, 0.35f), Scale: 0.006f, Wear: 0.1f) { PatternOnTrim = true, PatternOnHardware = true },
        ["desertcamo"] = new("desertcamo", "", "", SkinPattern.Blot, F("#CDB386", 0f, 0.7f), F("#9C7F55", 0f, 0.7f), F("#5E4830", 0f, 0.7f),
            Scale: 0.06f, Wear: 0.45f) { PatternOnTrim = true, PatternOnHardware = true },
        ["anodized"] = new("anodized", "", "", SkinPattern.Brushed, F("#1A8C8C", 0.85f, 0.3f), F("#106A6E", 0.85f, 0.34f),
            Scale: 0.09f, Wear: 0.15f) { PatternOnTrim = true, PatternOnHardware = true },
        ["damascus"] = new("damascus", "", "", SkinPattern.Damascus, F("#7A7D82", 0.9f, 0.3f), F("#34363A", 0.9f, 0.36f),
            Scale: 0.05f, Wear: 0.2f) { PatternOnTrim = true, PatternOnHardware = true },
        // Two-tone: light sand furniture (the body surfaces) on a dark polymer receiver
        ["twotone"] = new("twotone", "", "", SkinPattern.Solid, F("#CDBF9F", 0f, 0.6f),
            Trim: F("#1E2024", 0.1f, 0.5f), Hardware: F("#141518", 0.6f, 0.45f), Wear: 0.35f),

        // ---- Signature: static look + sounds, bullet FX, kill FX, inspect flourish ----
        ["glasswork"] = new("glasswork", "", "", SkinPattern.Glass, F("#B0B4BA", 0.9f, 0.22f), F("#000000", 0f, 0.05f, 0.9f),
            Scale: 0.016f, Wear: 0.05f, Glow: new Color("#FFFFFF"), GlowEnergy: 1.5f) { PatternOnTrim = true, PatternOnHardware = true },
        ["thunderhead"] = new("thunderhead", "", "", SkinPattern.Storm, F("#5A78A8", 0.4f, 0.38f), F("#C4D2E6", 0.3f, 0.42f), F("#1A2442", 0.3f, 0.45f),
            Scale: 0.035f, Wear: 0.15f, Glow: new Color("#7FB8FF"), GlowEnergy: 1.2f) { PatternOnTrim = true, PatternOnHardware = true },
        ["basalt"] = new("basalt", "", "", SkinPattern.Basalt, F("#4A4C50", 0f, 0.85f), F("#2C2E31", 0f, 0.85f), F("#3A0A04", 0f, 0.6f),
            Scale: 0.03f, Wear: 0.3f, Glow: new Color("#E8340E"), GlowEnergy: 1.1f) { PatternOnTrim = true, PatternOnHardware = true },
        ["oscilloscope"] = new("oscilloscope", "", "", SkinPattern.Scope, F("#3A3F3C", 0.75f, 0.42f), F("#050B07", 0f, 0.12f, 0.6f), F("#E8DFC4", 0f, 0.5f),
            Hardware: F("#1C1E1D", 0.7f, 0.4f), Wear: 0.2f, Glow: new Color("#3CFF6A"), GlowEnergy: 1.0f) { PatternOnTrim = true },
        ["rally"] = new("rally", "", "", SkinPattern.Livery, F("#F2F2EE", 0.05f, 0.22f, 0.7f), F("#D2202A", 0.05f, 0.22f, 0.7f),
            F("#1E4FC8", 0.05f, 0.22f, 0.7f), F("#0E0F11", 0.2f, 0.3f), Wear: 0.08f) { PatternOnTrim = true, PatternOnHardware = true },
        ["frostbite"] = new("frostbite", "", "", SkinPattern.Frost, F("#6F96B8", 0.6f, 0.3f), F("#F2F8FF", 0f, 0.75f),
            Scale: 0.016f, Wear: 0.15f, Glow: new Color("#6FD4FF"), GlowEnergy: 0.9f) { PatternOnTrim = true, PatternOnHardware = true },
        ["hive"] = new("hive", "", "", SkinPattern.Comb, F("#E08010", 0f, 0.15f, 0.7f), F("#7A5228", 0.85f, 0.38f), F("#5E2A04", 0f, 0.2f, 0.7f),
            Scale: 0.02f, Wear: 0.15f, Glow: new Color("#FFA828"), GlowEnergy: 1.1f) { PatternOnTrim = true, PatternOnHardware = true },

        // ---- Apex: animated from level 2 (Animated), everything up to the finisher and the kill counter (L5) ----
        ["foundry"] = new("foundry", "", "", SkinPattern.Molten, F("#2E2A28", 0.65f, 0.7f), F("#5A3E2E", 0.45f, 0.72f), F("#5A1A08", 0.3f, 0.7f),
            Scale: 0.05f, Wear: 0.25f, Glow: new Color("#FF6A12"), GlowEnergy: 1.6f) { PatternOnTrim = true, PatternOnHardware = true },
        ["aurora"] = new("aurora", "", "", SkinPattern.Aurora, F("#141A33", 0.3f, 0.25f), F("#2CFF9A", 0f, 0.3f), F("#9A3CFF", 0f, 0.3f),
            F("#7F92C0", 0f, 0.6f), Wear: 0.08f, Glow: new Color("#3CFFA0"), GlowEnergy: 1.3f) { PatternOnTrim = true, PatternOnHardware = true },
        ["orrery"] = new("orrery", "", "", SkinPattern.Orrery, F("#C8A050", 0.9f, 0.28f), F("#13214A", 0.2f, 0.2f, 0.7f), F("#F0A040", 0.3f, 0.4f),
            F("#6A8ACC", 0.3f, 0.4f), Trim: F("#B8913E", 0.9f, 0.3f), Hardware: F("#8C6A2A", 0.9f, 0.32f), Wear: 0.1f,
            Glow: new Color("#FFD27A"), GlowEnergy: 0.6f),
    };

    static readonly Dictionary<string, FxPalette> palettes = new()
    {
        ["glasswork"] = Pal("#E8B04A", "#FFF0C8", "#7FB0FF"),
        ["thunderhead"] = Pal("#9FD3FF", "#FFFFFF", "#C9CDD1"),
        ["basalt"] = Pal("#D2401E", "#FFB08A", "#C9C2B0"),
        ["oscilloscope"] = Pal("#41FF8A", "#B8FFD2", "#CFE8FF"),
        ["rally"] = Pal("#1F4FB5", "#FFFFFF", "#C8242B"),
        ["frostbite"] = Pal("#BFE6FF", "#FFFFFF", "#7FB0FF"),
        ["hive"] = Pal("#FFB347", "#FFE0A0", "#DDE6EA"),
        ["foundry"] = Pal("#FF7A1A", "#FFD08A", "#9FD3FF"),
        ["aurora"] = Pal("#7CFFC4", "#C9A8FF", "#FFFFFF"),
        ["orrery"] = Pal("#E8C46A", "#FFF1CF", "#9DB7E8"),
    };

    static readonly Dictionary<string, SkinVariant[]> variants = new()
    {
        ["foundry"] = new SkinVariant[]
        {
            new("blueflame", L.N("Blue Flame"), s => s with { A = F("#2E3036", 0.65f, 0.7f), B = F("#4A4E58", 0.5f, 0.7f), C = F("#0E1E4A", 0.3f, 0.7f),
                Glow = new Color("#3A8CFF") }, Pal("#4FA8FF", "#CFE8FF", "#FF7A1A")),
            new("whitehot", L.N("White Hot"), s => s with { A = F("#2A2624", 0.65f, 0.7f), C = F("#6A2A0A", 0.3f, 0.7f),
                Glow = new Color("#FFE6B8"), GlowEnergy = 2.0f }, Pal("#FFF2D6", "#FFFFFF", "#FF7A1A")),
            new("verdigris", L.N("Verdigris"), s => s with { A = F("#2F4A42", 0.5f, 0.6f), B = F("#9A6038", 0.8f, 0.4f), C = F("#0E3A2A", 0.3f, 0.6f),
                Glow = new Color("#4CFFB0") }, Pal("#5CFFB0", "#CFFFE6", "#FF7A1A")),
        },
        ["aurora"] = new SkinVariant[]
        {
            new("crimson", L.N("Crimson"), s => s with { A = F("#1E1226", 0.3f, 0.25f), B = F("#FF2A50", 0f, 0.3f), C = F("#FF8A2A", 0f, 0.3f),
                Glow = new Color("#FF4A6A") }, Pal("#FF5A7A", "#FFC8D4", "#7CFFC4")),
            new("gold", L.N("Gold"), s => s with { A = F("#1A1626", 0.3f, 0.25f), B = F("#FFC21A", 0f, 0.3f), C = F("#FF5A8A", 0f, 0.3f),
                Glow = new Color("#FFC83A") }, Pal("#FFC85A", "#FFF0C8", "#7CFFC4")),
            new("ice", L.N("Ice"), s => s with { A = F("#0E1428", 0.3f, 0.25f), B = F("#A8F0FF", 0f, 0.3f), C = F("#4A6CFF", 0f, 0.3f),
                Glow = new Color("#BFF0FF") }, Pal("#BFE6FF", "#FFFFFF", "#7CFFC4")),
        },
        ["orrery"] = new SkinVariant[]
        {
            new("silver", L.N("Silver & Black"), s => s with { A = F("#C4C8CE", 0.9f, 0.26f), B = F("#111317", 0.2f, 0.2f, 0.7f), C = F("#E8E8F0", 0.3f, 0.4f),
                D = F("#8CA8D0", 0.3f, 0.4f), Trim = F("#B8BCC2", 0.9f, 0.28f), Hardware = F("#5A5E64", 0.9f, 0.3f), Glow = new Color("#E6ECFF") },
                Pal("#DDE6EA", "#FFFFFF", "#E8C46A")),
            new("rosegold", L.N("Rose Gold"), s => s with { A = F("#D4927A", 0.9f, 0.28f), B = F("#2A1426", 0.2f, 0.2f, 0.7f), C = F("#FFC8A0", 0.3f, 0.4f),
                Trim = F("#C08A72", 0.9f, 0.3f), Hardware = F("#8C5E4C", 0.9f, 0.32f), Glow = new Color("#FFC0A8") },
                Pal("#F0B49A", "#FFE6DA", "#9DB7E8")),
            new("bronze", L.N("Bronze & Green"), s => s with { A = F("#A07840", 0.9f, 0.3f), B = F("#0F2E20", 0.2f, 0.2f, 0.7f), D = F("#7CC890", 0.3f, 0.4f),
                Trim = F("#8C6A3A", 0.9f, 0.32f), Hardware = F("#6A4E2A", 0.9f, 0.34f), Glow = new Color("#E8D088") },
                Pal("#C9A66A", "#F0DDB8", "#7CFFC4")),
        },
    };

    static readonly Dictionary<string, string> baseNames = new()
    {
        ["foundry"] = L.N("Molten"), ["aurora"] = L.N("Borealis"), ["orrery"] = L.N("Brass & Navy"),
    };

    /// <summary>The level-1 look of a line (Name/Blurb empty: the catalogue fills them). Unknown id → a plain dark look.</summary>
    public static GunSkin Look(string id) => looks.TryGetValue(id, out var g) ? g : new GunSkin(id, "", "", SkinPattern.Solid, F("#2B2E33", 0f, 0.55f));

    /// <summary>The Apex level-2 look transform (<c>SkinFeature.Animated | Reactive</c>): turns on the pattern's motion
    /// (<see cref="GunSkin.Anim"/>: Foundry's flowing melt and embers, Aurora's drifting ribbons, Orrery's orbiting planets,
    /// precessing rings and ticking dial) and the fire-reactive pulse (<see cref="GunSkin.React"/>). Static on Low quality
    /// (the pulse stays).</summary>
    public static Func<GunSkin, GunSkin> Animated(string id) => id switch
    {
        "foundry" => s => s with { Anim = 0.35f, React = 1f },
        "aurora" => s => s with { Anim = 0.3f, React = 1f },
        "orrery" => s => s with { Anim = 0.25f, React = 1f },
        _ => s => s with { Anim = 0.3f, React = 1f },
    };

    /// <summary>The line's effect palette (kill FX, bullet FX, banner). Core lines: derived from the look.</summary>
    public static FxPalette Palette(string id)
    {
        if (palettes.TryGetValue(id, out var p)) return p;
        var s = Look(id);
        return new FxPalette(s.A.Color, (s.B ?? s.A).Color, (s.C ?? s.B ?? s.A).Color);
    }

    /// <summary>The line's variants (Apex: 3 each; Core / Signature: none).</summary>
    public static IReadOnlyList<SkinVariant> Variants(string id) => variants.TryGetValue(id, out var v) ? v : Array.Empty<SkinVariant>();

    /// <summary>Display name of the base palette in the variant swatches (English, L.N); "" when the line has no variants.</summary>
    public static string BaseName(string id) => baseNames.TryGetValue(id, out var n) ? n : "";
}
