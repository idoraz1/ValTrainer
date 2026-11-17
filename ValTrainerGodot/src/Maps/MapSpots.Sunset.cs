using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Sunset, A Lobby → A Main → A Site, at true scale. Walls and props were traced from the official minimap
    /// (valorant-api.com, 1 px = 0.1252 m) with its own transform; heights are estimates from the minimap's shading and
    /// gameplay footage (the A Link ledge and its steps +1 m, crates 1–2 m, walls 6 m).
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the A Main mouth onto the site, at the end of
    /// the rounded planter wall (the choke), -Z = north (A Alley, defender spawn), +X = east (the site, Back site, Elbow).
    /// You start in A Lobby, walk north up A Main past the Elbow corner, then turn west around the rounded wall into the
    /// site. The site is east of the choke: the diagonal Site box in the middle, Default on its north edge with A Alley
    /// behind it, A Link and its raised ledge on the west side, Back site and the east corridor to the right.
    /// Cut-offs where the real map continues (A Lobby → attacker spawn and Mid Tiles, A Link → Mid, A Alley → defender
    /// spawn) are sealed with walls.
    /// </summary>
    public static readonly MapSpot Sunset = new MapSpot
    {
        Key = "sunset", Map = "Sunset", Name = "A Main → A Site",
        Drill = "Walk up A Main from A Lobby and swing onto A Site: clear Close right, the Site box, Default, A Alley, A Link and its ledge, Back site and the east corridor",
        StartFeet = V(13.15f, 0, 24.79f), StartYaw = 0, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(176, 164, 150), C(232, 186, 150), C(184, 96, 64), C(96, 146, 142), C(160, 148, 136), C(246, 184, 132)),
        Look = SunsetLook,
        Boxes = new[]
        {
            B(-4.76f, -0.1f, -34.05f, 35.81f, 0, 32.55f, Surf.Floor), // ground
            B(-5.26f, 0, -34.55f, -4.76f, 6, 33.05f), // perimeter: west (A Link → Mid, Lobby → Mid Tiles)
            B(35.81f, 0, -34.55f, 36.31f, 6, 33.05f), // perimeter: east
            B(-4.76f, 0, -34.55f, 35.81f, 6, -34.05f), // perimeter: north (A Alley → defender spawn)
            B(-4.76f, 0, 32.55f, 35.81f, 6, 33.05f), // perimeter: south (A Lobby → attacker spawn)
            // --- traced: walls (6 m) and the A Link ledge with its steps
            B(-2.75f, 0, -19.03f, 5.26f, 0.33f, -17.78f, Surf.Elevated), // raised +0.33 m
            B(-2.75f, 0, -17.78f, 5.26f, 0.67f, -16.28f, Surf.Elevated), // raised +0.67 m
            B(-2.75f, 0, -16.28f, 5.26f, 1, -15.02f, Surf.Elevated), // raised +1 m
            B(-2.75f, 0, -15.02f, -0.5f, 1, -5.76f, Surf.Elevated), // raised +1 m
            B(-4.76f, 0, -34.05f, 35.81f, 6, -33.8f), // wall
            B(-4.76f, 0, -33.8f, -4.51f, 6, 13.77f), // wall
            B(23.29f, 0, -33.8f, 35.81f, 6, -21.78f), // wall
            B(-4.51f, 0, -29.8f, 11.52f, 6, -25.79f), // wall
            B(19.03f, 0, -29.8f, 23.29f, 6, -25.79f), // wall
            B(11.52f, 0, -29.55f, 11.77f, 6, -25.79f), // wall
            B(18.78f, 0, -29.55f, 19.03f, 6, -25.79f), // wall
            B(1.25f, 0, -25.79f, 11.27f, 6, -25.54f), // wall
            B(20.28f, 0, -25.79f, 23.29f, 6, -25.04f), // wall
            B(1.5f, 0, -25.54f, 11.27f, 6, -25.29f), // wall
            B(1.75f, 0, -25.29f, 11.27f, 6, -25.04f), // wall
            B(2, 0, -25.04f, 11.27f, 6, -24.79f), // wall
            B(20.53f, 0, -25.04f, 23.29f, 6, -24.79f), // wall
            B(2.25f, 0, -24.79f, 11.27f, 6, -24.54f), // wall
            B(20.78f, 0, -24.79f, 23.29f, 6, -24.54f), // wall
            B(2.5f, 0, -24.54f, 11.27f, 6, -24.29f), // wall
            B(21.03f, 0, -24.54f, 23.29f, 6, -24.29f), // wall
            B(2.75f, 0, -24.29f, 11.27f, 6, -24.04f), // wall
            B(21.28f, 0, -24.29f, 23.29f, 6, -24.04f), // wall
            B(3, 0, -24.04f, 11.27f, 6, -23.79f), // wall
            B(21.53f, 0, -24.04f, 23.29f, 6, -23.79f), // wall
            B(3.26f, 0, -23.79f, 11.27f, 6, -23.54f), // wall
            B(21.78f, 0, -23.79f, 23.29f, 6, -23.54f), // wall
            B(3.51f, 0, -23.54f, 11.27f, 6, -23.29f), // wall
            B(22.04f, 0, -23.54f, 23.29f, 6, -23.29f), // wall
            B(3.76f, 0, -23.29f, 11.27f, 6, -23.04f), // wall
            B(22.29f, 0, -23.29f, 23.29f, 6, -23.04f), // wall
            B(4.01f, 0, -23.04f, 11.27f, 6, -22.79f), // wall
            B(22.54f, 0, -23.04f, 23.29f, 6, -22.79f), // wall
            B(4.26f, 0, -22.79f, 11.27f, 6, -22.54f), // wall
            B(4.51f, 0, -22.54f, 11.27f, 6, -22.29f), // wall
            B(4.76f, 0, -22.29f, 11.27f, 6, -22.04f), // wall
            B(5.01f, 0, -22.04f, 11.27f, 6, -21.78f), // wall
            B(5.26f, 0, -21.78f, 11.27f, 6, -17.78f), // wall
            B(23.29f, 0, -21.78f, 27.29f, 6, -21.28f), // wall
            B(35.31f, 0, -21.78f, 35.81f, 6, 32.55f), // wall
            B(-4.51f, 0, -17.78f, -2.75f, 6, 13.77f), // wall
            B(5.26f, 0, -17.78f, 9.26f, 6, -9.77f), // wall
            B(3.26f, 0, -14.77f, 5.26f, 6, -12.77f), // wall
            B(23.29f, 0, -14.27f, 27.29f, 6, 2.25f), // wall
            B(31.3f, 0, -9.77f, 35.31f, 6, -5.76f), // wall
            B(4.51f, 0, -1.75f, 23.29f, 6, 2.25f), // wall
            B(4.01f, 0, -1.5f, 4.51f, 6, 2), // wall
            B(3.76f, 0, -1.25f, 4.01f, 6, 1.75f), // wall
            B(3.51f, 0, -1, 3.76f, 6, 1.5f), // wall
            B(3.26f, 0, -0.5f, 3.51f, 6, 1), // wall
            B(17.28f, 0, 6.26f, 21.28f, 6, 32.55f), // wall
            B(21.28f, 0, 8.76f, 21.53f, 6, 32.55f), // wall
            B(21.53f, 0, 9.01f, 21.78f, 6, 32.55f), // wall
            B(21.78f, 0, 9.26f, 22.04f, 6, 32.55f), // wall
            B(22.04f, 0, 9.52f, 22.29f, 6, 32.55f), // wall
            B(22.29f, 0, 9.77f, 22.54f, 6, 32.55f), // wall
            B(22.54f, 0, 10.02f, 22.79f, 6, 32.55f), // wall
            B(5.26f, 0, 10.27f, 9.26f, 6, 18.28f), // wall
            B(22.79f, 0, 10.27f, 35.31f, 6, 32.55f), // wall
            B(-2.75f, 0, 10.77f, 5.26f, 6, 13.77f), // wall
            B(-4.76f, 0, 14.02f, -4.51f, 6, 18.28f), // wall
            B(1.25f, 0, 26.29f, 17.28f, 6, 32.55f), // wall
            B(-4.76f, 0, 32.3f, 1.25f, 6, 32.55f), // wall
            // --- props (outlines from the minimap, heights estimated)
            B(15.65f, 0, -7.39f, 18.47f, 2, -4.51f, Surf.Cover), // Site box: big crate (the diagonal box, south-west half)
            B(17.84f, 0, -9.64f, 20.66f, 1.3f, -6.82f, Surf.Cover), // Site box: second crate (north-east half)
            B(9.14f, 0, -18.78f, 15.4f, 1.2f, -17.03f, Surf.Cover), // Default box on the north edge of the site
            B(9.39f, 0, -32.8f, 11.77f, 1.6f, -29.55f, Surf.Cover), // Alley crate
            B(6.13f, 0, -3.76f, 8.39f, 1.3f, -1.75f, Surf.Cover), // crate in the site's south-west corner
            B(2.25f, 0, 1, 4.51f, 1.1f, 3.38f, Surf.Cover), // crate at the rounded wall's end
            B(6.13f, 0, 18.28f, 8.39f, 1.4f, 20.28f, Surf.Cover), // lobby crate
            B(33.3f, 0, -5.76f, 35.49f, 1.5f, -2.25f, Surf.Cover), // Back site boxes
        },
        Enemies = new[]
        {
            E("Close right", 7.26f, 0, -4.76f, Stance.Crouch),
            E("Site box", 21.78f, 0, -7.76f, Stance.Crouch),
            E("Default", 12.27f, 0, -19.66f),
            E("A Alley", 15.65f, 0, -27.79f),
            E("A Link", 3.13f, 1, -15.9f),
            E("Link ledge", -1.63f, 1, -10.27f, Stance.Crouch),
            E("Back site", 30.67f, 0, -16.53f),
            E("East corridor", 31.05f, 0, -2.75f),
        },
        Flashes = new[]
        {
            F("Pop over the planter wall", V(0.25f, 1.6f, 2), V(1.88f, 3.2f, -2.75f)),
            F("Lob onto the site", V(-0.63f, 1.6f, 2.25f), V(10.02f, 4.6f, -7.76f)),
            F("Deep pop toward Default", V(0.63f, 1.6f, 2), V(17.53f, 5, -12.77f)),
            F("Left toward the ledge", V(-0.25f, 1.6f, 1.75f), V(-1.25f, 3.4f, -5.26f)),
        },
        // Standard A Main execute: A Alley, A Link and Back site.
        Smokes = new[]
        {
            new SmokeSpot("A Alley", V(14.4f, 0, -25.92f)),
            new SmokeSpot("A Link", V(4.38f, 0, -12.77f)),
            new SmokeSpot("Back site", V(30.05f, 0, -10.27f)),
        },
        // Spike: in front of the Site box, behind it toward the planter, and on the north side by Default.
        PlantSpots = new[] { V(13.77f, 0, -7.76f), V(21.28f, 0, -2.75f), V(17.53f, 0, -14.65f) },
        // Retakes: A Alley from the defender spawn, A Link from Mid, and the north end of the east corridor.
        RetakeEntries = new[] { V(16.28f, 0, -32.8f), V(-2.5f, 0, -24.04f), V(31.93f, 0, -20.28f) },
    };

    /// <summary>Los Angeles at golden hour: warm peach and pink stucco, terracotta trims, teal accents, pale concrete,
    /// a low orange sun and a warm sky over low houses.</summary>
    static World.Theme SunsetLook(MapPalette p) => new()
    {
        Key = "sunset", Style = World.DecorStyle.Ascent, Hdri = World.Hdris.Qwantani, SkyYaw = 200,
        SkyTint = new Color(1.06f, 0.96f, 0.9f), SkySaturation = 1.2f,
        SunColor = new Color(1f, 0.82f, 0.62f), SunEnergy = 1.9f, Ambient = 1.0f, Exposure = 1.02f,
        Saturation = 1.16f, Contrast = 1.05f, FogDensity = 0.0011f, Ground = C(150, 130, 112),
        Floor = new World.Surface("concrete_floor_worn_02", p.Floor, 0.9f, 2.4f, 0.6f, Normal: 0.8f, Macro: 0.12f, Grime: 0.12f),
        Wall = new World.Surface("plastered_wall_02", p.Wall, 0.9f, 2.2f, 1.0f, Normal: 0.9f, Macro: 0.1f),
        Base = new World.Surface("concrete_wall_008", C(170, 160, 150), 0.8f, 2.2f, 0.8f, Grime: 0.2f),
        Cap = new World.Surface("red_sandstone_wall", p.Trim, 0.85f, 1.6f, 0.7f, Grime: 0f),
        Trim = new World.Surface("red_sandstone_wall", p.Trim, 0.8f, 1.8f, 0.8f),
        Elevated = new World.Surface("concrete_floor_worn_02", p.Elevated, 0.85f, 2f, 0.7f, Grime: 0.1f),
        Cover = new World.Surface("wood_planks", p.Cover, 0.8f, 1.5f, 0.75f, Grime: 0.1f),
        CoverFrame = new World.Surface("wood_planks", C(110, 80, 58), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new World.Surface("concrete_floor_worn_02", C(184, 170, 154), 0.9f, 2.6f, 0.5f, Grime: 0f),
        Backdrop = new World.Surface("plastered_wall_02", Colors.White, 0.9f, 2.2f, 1.1f, Macro: 0.1f),
        Roof = new World.Surface("red_sandstone_wall", C(170, 92, 66), 0.9f, 1.8f, 0.7f, Grime: 0f),
        Hues = new[] { C(240, 196, 168), C(232, 170, 160), C(244, 222, 190), C(160, 206, 200), C(236, 200, 140), C(220, 160, 190) },
        MinH = 6.5f, MaxH = 11f, Mountains = true, MountainColor = new Color(0.62f, 0.5f, 0.5f),
    };
}
