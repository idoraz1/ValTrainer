using Godot;
using ValTrainer.Core;
using ValTrainer.Progression;

namespace ValTrainer.World;

/// <summary>One look for the training rooms (suggested on Discord: "dark mode, my eyes are burning", then "themes like
/// KovaaK's that unlock as you level up").</summary>
/// <param name="Key">Stable id, stored in <see cref="AppSettings.RoomTheme"/>.</param>
/// <param name="Name">English display name (L.N: translated where shown).</param>
/// <param name="Blurb">One English line for the picker.</param>
/// <param name="Level">Account level that unlocks it; 1 = free from the start.</param>
/// <param name="Look">The room's surfaces, sky, sun and markings.</param>
public sealed record RoomTheme(string Key, string Name, string Blurb, int Level, Theme Look)
{
    public bool Free => Level <= 1;
}

/// <summary>
/// Environment themes for the training rooms: every drill that is not played on a map (aim, Aim+, movement and finder
/// rooms) is built by <see cref="Environments.BuildRange"/> with <see cref="Current"/>. Maps keep their own look.
/// <para>Contrast rule (the same as <see cref="Themes.Range"/>, checked by the culture self-test): the target body stands out
/// against the main wall (WCAG ratio ≥ <see cref="MinBodyWall"/>), and every surface behind targets is either far from the
/// body or far from every enemy highlight colour (≥ <see cref="MinEdge"/>), so a target always has a luminance edge. Dark
/// rooms therefore use a light target body (<see cref="Theme.TargetBody"/>) instead of the dark default.</para>
/// <para>Unlocks: DEFAULT and DARK are free; the others need an account level (the same levels as the gun skins'
/// progression, <see cref="Progress.Level"/>). A supporter code or the dev <c>--progress-unlock-all</c> unlocks all.
/// A locked choice is kept (it shows DEFAULT until it unlocks). Dev: <c>--roomtheme key</c> forces a theme (screenshots).</para>
/// </summary>
public static class RoomThemes
{
    public const string DefaultKey = "default";
    public const float MinBodyWall = 4.5f, MinEdge = 3f;

    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);

    static Surface Tint(Surface s, Color c) => s with { Tint = c };

    /// <summary>Calm dark grey, a night sky, a dim cool moon instead of the sun: the free fix for glare.</summary>
    static Theme Dark()
    {
        var r = Themes.Range;
        return r with
        {
            Key = "range_dark",
            SkyTint = new Color(0.42f, 0.48f, 0.66f), SkySaturation = 0.5f, SkyEnergy = 0.2f, Ground = C(22, 23, 27), SunDisc = 0.12f,
            SunColor = new Color(0.74f, 0.8f, 0.95f), SunEnergy = 0.9f, Ambient = 1f, AmbientColor = new Color(0.42f, 0.45f, 0.52f), Saturation = 1f, Contrast = 1f,
            FogColor = new Color(0.07f, 0.08f, 0.1f),
            Floor = Tint(r.Floor, C(46, 48, 54)), Wall = Tint(r.Wall, C(56, 60, 68)), Base = Tint(r.Base, C(24, 25, 30)),
            Cap = Tint(r.Cap, C(20, 21, 25)), Trim = Tint(r.Trim, C(20, 21, 25)), Elevated = Tint(r.Elevated, C(54, 57, 64)),
            Cover = Tint(r.Cover, C(30, 32, 38)), CoverFrame = Tint(r.CoverFrame, C(18, 19, 23)), GroundSurf = Tint(r.GroundSurf, C(28, 29, 32)),
            Roof = Tint(r.Roof, C(24, 26, 30)), MountainColor = new Color(0.07f, 0.08f, 0.1f),
            Marking = new Color(0.6f, 0.3f, 0.12f), Tick = new Color(0.55f, 0.47f, 0.18f), Lane = new Color(0.3f, 0.32f, 0.36f),
            Pad = new Color(0.1f, 0.1f, 0.12f), Flood = new Color(0.55f, 0.6f, 0.7f), FloodGlow = 0.45f,
            TargetBody = C(168, 174, 186),
        };
    }

    /// <summary>Deep navy walls under a blue night sky, cool moonlight and soft cyan markings.</summary>
    static Theme Midnight()
    {
        var r = Themes.Range;
        return r with
        {
            Key = "range_midnight",
            SkyTint = new Color(0.25f, 0.36f, 0.9f), SkySaturation = 0.6f, SkyEnergy = 0.26f, Ground = C(14, 18, 34), SunDisc = 0.25f,
            SunColor = new Color(0.62f, 0.72f, 1f), SunEnergy = 1f, Ambient = 1f, AmbientColor = new Color(0.3f, 0.37f, 0.56f), Saturation = 1.05f, Contrast = 1f,
            FogColor = new Color(0.05f, 0.07f, 0.16f),
            Floor = Tint(r.Floor, C(24, 30, 52)), Wall = Tint(r.Wall, C(30, 40, 70)), Base = Tint(r.Base, C(14, 18, 32)),
            Cap = Tint(r.Cap, C(16, 20, 34)), Trim = Tint(r.Trim, C(16, 20, 34)), Elevated = Tint(r.Elevated, C(40, 50, 80)),
            Cover = Tint(r.Cover, C(20, 26, 46)), CoverFrame = Tint(r.CoverFrame, C(12, 15, 26)), GroundSurf = Tint(r.GroundSurf, C(18, 22, 38)),
            Roof = Tint(r.Roof, C(18, 22, 36)), MountainColor = new Color(0.05f, 0.07f, 0.15f),
            Marking = new Color(0.2f, 0.55f, 0.85f), Tick = new Color(0.45f, 0.7f, 0.95f), Lane = new Color(0.25f, 0.32f, 0.5f),
            Pad = new Color(0.07f, 0.09f, 0.16f), Flood = new Color(0.5f, 0.62f, 1f), FloodGlow = 0.6f,
            TargetBody = C(160, 170, 190),
        };
    }

    /// <summary>Flat pale panels, black wainscot and cover, a grey overcast sky and a desaturated grade: only the targets carry colour.</summary>
    static Theme HighContrast()
    {
        var r = Themes.Range;
        Surface Clean(Surface s, Color c) => s with { Tint = c, TintAmount = 1f, Contrast = 0.25f, Macro = 0f, Grime = 0f };
        return r with
        {
            Key = "range_contrast", Hdri = Hdris.Overcast,
            SkyTint = new Color(0.9f, 0.92f, 0.95f), SkySaturation = 0f, SkyEnergy = 0.75f, Ground = C(60, 60, 62), SunDisc = 0f,
            SunColor = new Color(1f, 1f, 1f), SunEnergy = 1.1f, Saturation = 0.85f, Contrast = 1.08f, FogDensity = 0f,
            Floor = Clean(r.Floor, C(190, 192, 196)), Wall = Clean(r.Wall, C(205, 208, 214)), Base = Clean(r.Base, C(16, 16, 18)),
            Cap = Clean(r.Cap, C(14, 14, 16)), Trim = Clean(r.Trim, C(14, 14, 16)), Elevated = Clean(r.Elevated, C(120, 122, 126)),
            Cover = Clean(r.Cover, C(16, 16, 18)), CoverFrame = Clean(r.CoverFrame, C(10, 10, 12)), GroundSurf = Clean(r.GroundSurf, C(150, 150, 152)),
            MountainColor = new Color(0.6f, 0.6f, 0.62f),
            Marking = new Color(0.12f, 0.12f, 0.14f), Tick = new Color(0.3f, 0.3f, 0.32f), Lane = new Color(0.4f, 0.4f, 0.42f),
            Pad = new Color(0.08f, 0.08f, 0.09f), FloodGlow = 0.8f,
            TargetBody = C(20, 20, 22),
        };
    }

    /// <summary>Board-formed grey concrete under a soft overcast sky: no hard sun, no glare.</summary>
    static Theme Concrete()
    {
        var r = Themes.Range;
        return r with
        {
            Key = "range_concrete", Hdri = Hdris.Overcast,
            SkyTint = new Color(0.86f, 0.88f, 0.92f), SkySaturation = 0.6f, SkyEnergy = 0.7f, Ground = C(70, 70, 72), SunDisc = 0.3f,
            SunColor = new Color(0.96f, 0.96f, 0.98f), SunEnergy = 1.0f, Saturation = 0.95f, Contrast = 1.04f,
            FogColor = new Color(0.55f, 0.57f, 0.6f),
            Floor = Tint(r.Floor, C(126, 126, 124)), Wall = new Surface("concrete_wall_008", C(150, 150, 146), 0.85f, 2.7f, 1.1f, Macro: 0.1f, Grime: 0.15f),
            Base = Tint(r.Base, C(34, 36, 40)), Cap = Tint(r.Cap, C(40, 42, 46)), Trim = Tint(r.Trim, C(40, 42, 46)),
            Elevated = Tint(r.Elevated, C(96, 98, 102)), Cover = Tint(r.Cover, C(36, 38, 42)), CoverFrame = Tint(r.CoverFrame, C(30, 32, 36)),
            GroundSurf = Tint(r.GroundSurf, C(96, 96, 96)), MountainColor = new Color(0.42f, 0.44f, 0.47f),
            Marking = new Color(0.9f, 0.72f, 0.2f), Tick = new Color(0.9f, 0.9f, 0.88f), Lane = new Color(0.78f, 0.78f, 0.76f),
            Pad = new Color(0.14f, 0.15f, 0.16f),
        };
    }

    /// <summary>Peach walls and a warm late-afternoon sky with a low orange sun and plum shadows.</summary>
    static Theme Sunset()
    {
        var r = Themes.Range;
        return r with
        {
            Key = "range_sunset", Hdri = Hdris.Qwantani, SkyYaw = 30,
            SkyTint = new Color(1.12f, 0.74f, 0.6f), SkySaturation = 1.3f, SkyEnergy = 0.9f, Ground = C(70, 44, 44), SunDisc = 0.9f,
            SunColor = new Color(1f, 0.68f, 0.44f), SunEnergy = 1.7f, Saturation = 1.1f,
            FogColor = new Color(0.85f, 0.56f, 0.45f),
            Floor = Tint(r.Floor, C(150, 118, 98)), Wall = Tint(r.Wall, C(210, 170, 140)), Base = Tint(r.Base, C(36, 26, 28)),
            Cap = Tint(r.Cap, C(44, 30, 34)), Trim = Tint(r.Trim, C(44, 30, 34)), Elevated = Tint(r.Elevated, C(120, 90, 84)),
            Cover = Tint(r.Cover, C(40, 28, 34)), CoverFrame = Tint(r.CoverFrame, C(30, 22, 26)), GroundSurf = Tint(r.GroundSurf, C(120, 92, 80)),
            Roof = Tint(r.Roof, C(80, 50, 50)), MountainColor = new Color(0.42f, 0.28f, 0.36f),
            Marking = new Color(0.98f, 0.82f, 0.5f), Tick = new Color(1f, 0.92f, 0.7f), Lane = new Color(0.95f, 0.86f, 0.78f),
            Pad = new Color(0.16f, 0.11f, 0.12f), Flood = new Color(1f, 0.85f, 0.7f),
        };
    }

    /// <summary>Sandstone blocks, a sandy floor, dark timber wainscot and a hot pale sky over desert hills.</summary>
    static Theme Desert()
    {
        var r = Themes.Range;
        return r with
        {
            Key = "range_desert", SkyYaw = 25,
            SkyTint = new Color(1.02f, 0.96f, 0.86f), SkySaturation = 0.85f, SkyEnergy = 1f, Ground = C(150, 122, 90),
            SunColor = new Color(1f, 0.9f, 0.74f), SunEnergy = 1.7f, Saturation = 1.08f,
            FogColor = new Color(0.8f, 0.74f, 0.64f),
            Floor = new Surface("clay_plaster", C(186, 160, 120), 0.85f, 3f, 1f, Macro: 0.14f, Grime: 0f),
            Wall = new Surface("sandstone_blocks_08", C(214, 190, 150), 0.85f, 2.6f, 1f, Rough: 0.2f, Macro: 0.1f, Grime: 0.12f),
            Base = new Surface("wood_planks", C(44, 32, 24), 0.85f, 1.5f, 0.8f, Grime: 0.1f),
            Cap = new Surface("wood_planks", C(48, 34, 26), 0.85f, 1.5f, 0.7f, Grime: 0f),
            Trim = new Surface("wood_planks", C(48, 34, 26), 0.85f, 1.5f, 0.7f),
            Elevated = new Surface("sandstone_blocks_08", C(170, 146, 112), 0.85f, 2.4f, 0.9f),
            Cover = new Surface("wood_planks", C(52, 38, 28), 0.8f, 1.5f, 0.8f, Grime: 0.1f),
            CoverFrame = new Surface("wood_planks", C(40, 28, 20), 0.85f, 1.5f, 0.7f, Grime: 0f),
            GroundSurf = new Surface("clay_plaster", C(176, 146, 108), 0.85f, 3f, 0.6f, Grime: 0f),
            MountainColor = new Color(0.66f, 0.52f, 0.38f),
            Marking = new Color(0.62f, 0.2f, 0.1f), Tick = new Color(0.3f, 0.22f, 0.16f), Lane = new Color(0.36f, 0.26f, 0.18f),
            Pad = new Color(0.22f, 0.16f, 0.12f), Flood = new Color(1f, 0.92f, 0.78f),
        };
    }

    /// <summary>Near-black room with a glowing cyan grid on the floor and walls under a dark violet sky.</summary>
    static Theme Neon()
    {
        var r = Themes.Range;
        var cyan = new Color(0.1f, 0.82f, 1f);
        return r with
        {
            Key = "range_neon",
            SkyTint = new Color(0.36f, 0.22f, 0.7f), SkySaturation = 0.4f, SkyEnergy = 0.08f, Ground = C(8, 8, 12), SunDisc = 0f,
            SunColor = new Color(0.7f, 0.74f, 1f), SunEnergy = 0.6f, Ambient = 1f, AmbientColor = new Color(0.34f, 0.34f, 0.44f), Saturation = 1.05f, Contrast = 1f,
            FogColor = new Color(0.03f, 0.02f, 0.06f),
            Floor = Tint(r.Floor, C(12, 12, 18)), Wall = Tint(r.Wall, C(14, 14, 20)), Base = Tint(r.Base, C(8, 8, 12)),
            Cap = Tint(r.Cap, C(10, 10, 14)), Trim = Tint(r.Trim, C(10, 10, 14)), Elevated = Tint(r.Elevated, C(24, 24, 34)),
            Cover = Tint(r.Cover, C(18, 18, 26)), CoverFrame = Tint(r.CoverFrame, C(8, 8, 12)), GroundSurf = Tint(r.GroundSurf, C(8, 8, 12)),
            Roof = Tint(r.Roof, C(10, 10, 14)), MountainColor = new Color(0.03f, 0.02f, 0.06f),
            Marking = cyan, Tick = cyan, Lane = new Color(0.08f, 0.45f, 0.6f), MarkingGlow = 1.6f,
            Pad = new Color(0.03f, 0.03f, 0.05f), Flood = cyan, FloodGlow = 1.2f,
            Grid = cyan, GridGlow = 1.8f,
            TargetBody = C(150, 156, 168),
        };
    }

    static List<RoomTheme>? all;

    /// <summary>Every theme in picker order (free first, then by unlock level).</summary>
    public static IReadOnlyList<RoomTheme> All => all ??= new()
    {
        new(DefaultKey, L.N("Default"), L.N("The classic bright range"), 1, Themes.Range),
        new("dark", L.N("Dark"), L.N("Dark grey walls, a night sky, low glare"), 1, Dark()),
        new("contrast", L.N("High Contrast"), L.N("Flat pale walls, black cover: targets pop"), 3, HighContrast()),
        new("midnight", L.N("Midnight"), L.N("Deep blue walls under a night sky"), 5, Midnight()),
        new("concrete", L.N("Concrete"), L.N("Grey concrete under a soft overcast sky"), 8, Concrete()),
        new("sunset", L.N("Sunset"), L.N("Warm walls and a low orange sun"), 11, Sunset()),
        new("desert", L.N("Desert"), L.N("Sandstone blocks and a hot pale sky"), 15, Desert()),
        new("neon", L.N("Neon Grid"), L.N("Near-black room with a glowing grid"), 20, Neon()),
    };

    public static RoomTheme? Get(string? key) => All.FirstOrDefault(t => t.Key == key);

    public static bool IsUnlocked(RoomTheme t) =>
        t.Free || Progress.DevUnlockAll || Supporter.Active || Progress.Level >= t.Level;

    /// <summary>The theme picked in Settings (may be locked: then <see cref="Current"/> shows DEFAULT).</summary>
    public static RoomTheme Chosen => Get(Main.I?.Settings?.RoomTheme) ?? All[0];

    /// <summary>The theme the next training room is built with: the dev <c>--roomtheme</c>, else the chosen one when unlocked.</summary>
    public static RoomTheme Current
    {
        get
        {
            if (EnvDev.RoomTheme is { } dev && Get(dev) is { } d) return d;
            var c = Chosen;
            return IsUnlocked(c) ? c : All[0];
        }
    }

    /// <summary>Themes whose unlock level is in (<paramref name="from"/>, <paramref name="to"/>]: the level-up toast names them.</summary>
    public static IEnumerable<RoomTheme> UnlockedBetween(int from, int to) => All.Where(t => !t.Free && t.Level > from && t.Level <= to);

    // ---------------- contrast (self-test, picker) ----------------

    /// <summary>Relative luminance of an sRGB colour (WCAG 2).</summary>
    public static float Luminance(Color c)
    {
        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Lin(c.R) + 0.7152f * Lin(c.G) + 0.0722f * Lin(c.B);
    }

    /// <summary>WCAG contrast ratio (1..21).</summary>
    public static float Contrast(Color a, Color b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
    }

    /// <summary>The surfaces a target can appear in front of.</summary>
    public static IEnumerable<(string Name, Color Tint)> Backdrops(Theme th) => new[]
    {
        ("wall", th.Wall.Tint), ("floor", th.Floor.Tint), ("wainscot", th.Base.Tint), ("cover", th.Cover.Tint),
    };

    /// <summary>The target body colour of a theme (the dark default unless it sets one).</summary>
    public static Color BodyOf(Theme th) => th.TargetBody ?? Game.Target.DefaultBody;

    /// <summary>The weakest edge of a VALORANT-look target in this theme over every backdrop and enemy colour: per backdrop
    /// the better of body-vs-backdrop and outline-vs-backdrop. Returns the ratio and where it is.</summary>
    public static (float Ratio, string Where) WeakestEdge(Theme th, IEnumerable<Color> enemies)
    {
        var body = BodyOf(th);
        float worst = float.MaxValue; string where = "";
        foreach (var (name, tint) in Backdrops(th))
            foreach (var e in enemies)
            {
                float r = Mathf.Max(Contrast(body, tint), Contrast(e, tint));
                if (r < worst) { worst = r; where = $"{name} vs {e.ToHtml(false)}"; }
            }
        return (worst, where);
    }
}
