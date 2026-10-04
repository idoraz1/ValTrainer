using Godot;
using ValTrainer.Game.Fx;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>How a controller puts its smokes down.</summary>
public enum SmokePlacing
{
    /// <summary>Tactical map: mark spots, confirm, the smokes drop from the sky (Brimstone, Clove, Miks).</summary>
    Map,
    /// <summary>Aimed marker pushed out / pulled in along the crosshair, through walls (Omen, Harbor's Cove).</summary>
    Aim,
    /// <summary>Stars placed on the map in Astral Form, later turned into smokes by looking at them (Astra).</summary>
    Stars,
    /// <summary>Thrown emitter switched on with fuel (Viper's Poison Cloud).</summary>
    Throw,
}

/// <summary>Second, wall-shaped vision blocker of the kit.</summary>
public enum WallKind { None, ToxicScreen, HighTide }

/// <summary>
/// One controller's smoke kit for the Smoke Execute drill. Numbers are from the VALORANT wiki ability pages (patch 12.x,
/// October 2026): radius, duration, charges, cast range, deploy time. Descriptions are our own words.
/// </summary>
public sealed record ControllerKit
{
    public required string Key { get; init; }
    public required string Agent { get; init; }
    public required string Ability { get; init; }
    public required SmokePlacing Placing { get; init; }
    /// <summary>Smokes per execute (Astra: Nebula charges).</summary>
    public int Charges { get; init; } = 2;
    public float Radius { get; init; } = 4.1f;
    /// <summary>Smoke life in seconds (+inf = fuel-limited).</summary>
    public float Duration { get; init; } = 15f;
    /// <summary>Cast range from the player in metres (0 = anywhere on the map).</summary>
    public float Range { get; init; } = 55f;
    /// <summary>Seconds from launch until the smoke starts forming.</summary>
    public float Deploy { get; init; } = 1f;
    public float FormTime { get; init; } = 0.6f;
    /// <summary>VALORANT ability slot of the smoke (Ability2 = E, Ability1 = Q …; the player's binds).</summary>
    public GameAction SmokeKey { get; init; } = GameAction.Ability2;
    public WallKind Wall { get; init; } = WallKind.None;
    public string WallAbility { get; init; } = "";
    public GameAction WallKey { get; init; } = GameAction.Ability1;
    /// <summary>Astra: stars per round.</summary>
    public int Stars { get; init; }
    public required SmokeLook Look { get; init; }
    public SmokeLook? WallLook { get; init; }
    /// <summary>One-line placement help (HUD).</summary>
    public required string HowTo { get; init; }
    /// <summary>Short kit summary for the plan screen.</summary>
    public required string Summary { get; init; }

    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);

    public static readonly ControllerKit[] All =
    {
        new()
        {
            Key = "brimstone", Agent = "Brimstone", Ability = "Sky Smoke", Placing = SmokePlacing.Map,
            Charges = 3, Radius = 4.15f, Duration = 19.25f, Range = 55f, Deploy = 1f,
            Look = new SmokeLook(C(214, 210, 204), C(98, 94, 92), C(255, 196, 150), C(255, 160, 90), Density: 3.4f),
            HowTo = "Tactical map: CLICK to mark up to 3 smokes (click a mark to remove it) · RIGHT-CLICK to launch",
            Summary = "3 smokes · 4.15 m · 19 s · 55 m from you · dropped from the sky",
        },
        new()
        {
            Key = "omen", Agent = "Omen", Ability = "Dark Cover", Placing = SmokePlacing.Aim,
            Charges = 2, Radius = 4.1f, Duration = 15f, Range = 80f, Deploy = 1f,
            Look = new SmokeLook(C(92, 70, 132), C(14, 9, 26), C(150, 105, 255), C(170, 120, 255), Density: 3.8f, Swirl: 0.1f),
            HowTo = "Aim the marker (it goes through walls): HOLD CLICK pushes it out, HOLD RIGHT-CLICK pulls it in · E throws · R overhead view",
            Summary = "2 smokes · 4.1 m · 15 s · up to 80 m, aimed through walls",
        },
        new()
        {
            Key = "astra", Agent = "Astra", Ability = "Nebula", Placing = SmokePlacing.Stars,
            Charges = 2, Stars = 5, Radius = 4.75f, Duration = 14.25f, Range = 0f, Deploy = 0.75f, SmokeKey = GameAction.Ability2,
            Look = new SmokeLook(C(150, 92, 206), C(34, 16, 62), C(246, 196, 255), C(236, 190, 255), Density: 3.4f, Sparkle: 1f, Swirl: 0.05f),
            HowTo = "Astral Form: CLICK to place stars (click one to take it back) · RIGHT-CLICK or X to return · then LOOK AT A STAR + E = Nebula",
            Summary = "5 stars, 2 Nebulas · 4.75 m · 14 s · anywhere on the map",
        },
        new()
        {
            Key = "clove", Agent = "Clove", Ability = "Ruse", Placing = SmokePlacing.Map,
            Charges = 2, Radius = 4f, Duration = 14f, Range = 60f, Deploy = 1f, FormTime = 0.5f,
            Look = new SmokeLook(C(232, 158, 226), C(92, 48, 104), C(130, 210, 255), C(255, 170, 240), Density: 3.3f),
            HowTo = "Battlefield view: CLICK to mark 2 clouds (click a mark to remove it) · RIGHT-CLICK to launch",
            Summary = "2 smokes · 4 m · 14 s · 60 m from you",
        },
        new()
        {
            Key = "viper", Agent = "Viper", Ability = "Poison Cloud", Placing = SmokePlacing.Throw,
            Charges = 1, Radius = 4.5f, Duration = float.PositiveInfinity, Range = 0f, Deploy = 0.75f, FormTime = 1f, SmokeKey = GameAction.Ability1,
            Wall = WallKind.ToxicScreen, WallAbility = "Toxic Screen", WallKey = GameAction.Ability2,
            Look = new SmokeLook(C(120, 196, 92), C(22, 64, 22), C(196, 255, 110), C(170, 255, 90), Density: 3.4f, Swirl: 0.09f),
            WallLook = new SmokeLook(C(118, 196, 90), C(24, 70, 22), C(200, 255, 120), C(170, 255, 90)),
            HowTo = "Q orb: CLICK throws, RIGHT-CLICK lobs · E wall: CLICK fires it along your aim (through walls) · Q / E again = gas on/off (fuel)",
            Summary = "Poison Cloud 4.5 m + Toxic Screen 60 m · fuel 100: one 12 s, both 8 s",
        },
        new()
        {
            Key = "harbor", Agent = "Harbor", Ability = "Cove", Placing = SmokePlacing.Aim,
            Charges = 1, Radius = 4.6f, Duration = 19.25f, Range = 80f, Deploy = 1f,
            Wall = WallKind.HighTide, WallAbility = "High Tide", WallKey = GameAction.Ability1,
            Look = new SmokeLook(C(86, 170, 196), C(10, 58, 82), C(196, 246, 255), C(150, 230, 255), Density: 3.1f, Ripple: 1f, Swirl: 0.12f, Water: true),
            WallLook = new SmokeLook(C(92, 176, 204), C(12, 62, 88), C(220, 250, 255), C(150, 230, 255), Water: true),
            HowTo = "E Cove: aim the marker (HOLD CLICK out / RIGHT-CLICK in), E throws · Q High Tide: CLICK sends the wave, HOLD CLICK steers it, RIGHT-CLICK stops it",
            Summary = "Cove 4.6 m · 19 s + High Tide wall 60 m · 15 s",
        },
        new()
        {
            Key = "miks", Agent = "Miks", Ability = "Waveform", Placing = SmokePlacing.Map,
            Charges = 2, Radius = 4.72f, Duration = 16.75f, Range = 55.5f, Deploy = 1f, FormTime = 0.5f,
            Look = new SmokeLook(C(104, 214, 186), C(12, 76, 78), C(150, 255, 214), C(140, 255, 210), Density: 3.3f, Pulse: 1f),
            HowTo = "Map targeter: CLICK to mark 2 smokes (click a mark to remove it) · RIGHT-CLICK to launch",
            Summary = "2 smokes · 4.72 m · 17 s · 55 m from you",
        },
    };

    public static ControllerKit For(string key) => All.FirstOrDefault(k => k.Key == key) ?? All.First(k => k.Key == "omen");

    /// <summary>Spheres this kit can put down in one execute (Viper / Harbor also have a wall).</summary>
    public int Spheres => Charges;
    public bool HasWall => Wall != WallKind.None;
}
