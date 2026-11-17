using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Summit (patch 13.00, June 2026: a Radiant training academy in the mountains of Zhangjiajie, China), B Lobby → B Main →
    /// B Site, at true scale. Walls and props were traced from the official minimap (valorant-api.com, 1 px = 0.130 m,
    /// within ~0.3 m of its wall lines) and placed with its own transform. Heights are estimates: the minimap only shows
    /// that B Tower is high ground (about 4 m here) and that B Main has a slightly raised walkway along its south wall.
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the B Main exit into the site (the choke),
    /// -Z = north (B Tower, B Gym, the defender side), +X = east (B Link, Mid).
    /// You walk north up B Lobby, turn west along B Main and come out of its exit onto the site. B Tower is a balcony
    /// 4 m up on the site's north side (defenders reach it by a rope from B Gym, not modelled; the silent drop to B Drop
    /// on its west side is a plain 4 m edge), with the raised B Wall over its opening (Summit's droppable wall is shown
    /// open, as at round start). B Drop opens onto the site's north-west corner, B Gym's door is north-east, a pillar
    /// with a cubby facing the site stands in the middle, and B Link leads east toward Mid. Cut-offs where the real map
    /// goes on (Gym → defender spawn, B Trophy and B Link → Mid, B Lobby → Mid Tiles and the attacker spawn) are sealed.
    /// </summary>
    public static readonly MapSpot Summit = new MapSpot
    {
        Key = "summit", Map = "Summit", Name = "B Main → B Site",
        Drill = "Push up B Lobby, through B Main and out onto B Site: clear Close box, Close left, Boxes, Corner, the pillar cubby, Tower (high), B Drop, Wall box, Gym door, Behind pillar and B Link",
        StartFeet = V(18.88f, 0, 36.2f), StartYaw = 0, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(150, 148, 140), C(234, 228, 214), C(52, 150, 138), C(156, 118, 82), C(128, 138, 132), C(232, 206, 150)),
        Boxes = new[]
        {
            B(-11.46f, -0.1f, -41.15f, 26.82f, 0, 41.15f, Surf.Floor), // ground
            B(-11.46f, 0, -41.15f, 10.42f, 8, -32.29f), // north block (behind Tower and B Drop)
            B(10.42f, 0, -41.15f, 26.82f, 8, -40.49f), // Gym north wall (cut: Gym continues east to defender spawn)
            B(10.42f, 0, -40.49f, 13.41f, 8, -39.32f), // Gym north-west corner (diagonal, step 1)
            B(10.42f, 0, -39.32f, 12.24f, 8, -38.15f), // Gym north-west corner (step 2)
            B(10.42f, 0, -38.15f, 11.07f, 8, -36.98f), // Gym north-west corner (step 3)
            B(26.69f, 0, -40.49f, 26.82f, 8, -27.08f), // Gym cut-off (continues east toward Trophy / defender spawn)
            B(-11.46f, 0, -32.29f, -11.07f, 8, -19.53f), // B Drop west wall
            B(-11.46f, 0, -19.53f, -5.08f, 8, -18.49f), // B Drop south wall
            B(-5.86f, 0, -22.14f, -5.08f, 8, -19.53f), // B Drop stub (the drop opens onto the site north of it)
            B(-11.46f, 0, -18.49f, -9.11f, 8, -3.65f), // site west wall
            B(-11.46f, 0, -3.65f, -8.2f, 8, -2.6f), // site south-west corner (diagonal, step 1)
            B(-11.46f, 0, -2.6f, -7.16f, 8, -1.69f), // site south-west corner (step 2)
            B(-11.46f, 0, -1.69f, -6.25f, 8, -0.78f), // site south-west corner (step 3)
            B(-11.46f, 0, -0.78f, -6.25f, 8, 41.15f), // west of B Main
            B(-6.25f, 0, -0.78f, -3.26f, 8, 2.6f), // B Main exit, left jamb
            B(3.26f, 0, -0.13f, 7.29f, 8, 2.6f), // B Main exit, right jamb
            B(3.26f, 0, 2.6f, 5.86f, 8, 5f), // right jamb (Main side; 0.3 m deeper than the minimap so Close left can't thread a 10 cm gap past both jambs into Main)
            B(7.29f, 0, -2.08f, 22.27f, 8, 2.6f), // block between the site and B Main
            B(18.23f, 0, 2.6f, 22.27f, 8, 3.65f), // B Main north-east corner (diagonal, step 1)
            B(19.53f, 0, 3.65f, 22.27f, 8, 4.82f), // B Main north-east corner (step 2)
            B(20.83f, 0, 4.82f, 22.27f, 8, 5.99f), // B Main north-east corner (step 3)
            B(21.35f, 0, -6.12f, 26.82f, 8, 2.6f), // B Link south wall
            B(22.27f, 0, 2.6f, 26.82f, 8, 24.74f), // B Lobby east wall (Mid Tiles behind)
            B(26.04f, 0, 24.74f, 26.82f, 8, 41.15f), // B Lobby cut-off (toward Mid Tiles and the attacker spawn)
            B(-6.25f, 0, 13.8f, 11.33f, 8, 41.15f), // south of B Main
            B(11.33f, 0, 10.03f, 14.84f, 8, 19.53f), // B Lobby pillar
            B(11.33f, 0, 19.53f, 11.85f, 8, 41.15f), // B Lobby west wall
            B(11.85f, 0, 32.81f, 12.37f, 8, 41.15f), // B Lobby south-west curve (step 1)
            B(12.37f, 0, 34.9f, 13.15f, 8, 41.15f), // B Lobby curve (step 2)
            B(13.15f, 0, 36.33f, 14.19f, 8, 41.15f), // B Lobby curve (step 3)
            B(14.19f, 0, 37.5f, 15.76f, 8, 41.15f), // B Lobby curve (step 4)
            B(15.76f, 0, 38.8f, 17.58f, 8, 41.15f), // B Lobby curve (step 5)
            B(17.58f, 0, 39.97f, 26.04f, 8, 41.15f), // B Lobby south cut-off (the lobby loops on toward the attacker spawn)
            B(-6.9f, 0, -28.13f, -1.04f, 8, -25.39f), // site north wall, west of the B Wall
            B(5.21f, 0, -27.08f, 13.67f, 8, -25.39f), // site north wall, east of the B Wall
            B(17.84f, 0, -29.17f, 18.88f, 8, -25.39f), // Gym door east jamb
            B(18.88f, 0, -27.08f, 26.82f, 8, -25.39f), // Gym south wall
            B(20.83f, 0, -25.39f, 23.96f, 8, -10.42f), // wall between the site and B Trophy
            B(23.96f, 0, -25.39f, 26.82f, 8, -10.42f), // B Trophy (sealed: it opens toward B Link further east)
            B(18.36f, 0, -15.49f, 20.83f, 8, -10.42f), // Trophy wall corner
            B(26.69f, 0, -10.42f, 26.82f, 8, -6.12f), // B Link cut-off (continues east to Mid Bottom)
            B(5.08f, 0, -18.1f, 11.59f, 8, -16.41f), // site pillar (north arm)
            B(7.29f, 0, -16.41f, 11.59f, 8, -9.38f), // site pillar
            B(5.08f, 0, -10.94f, 7.29f, 8, -9.38f), // site pillar (south arm): the cubby between the arms faces the site
            B(-3.12f, 0, -32.29f, 10.42f, 4, -26.95f, Surf.Elevated), // B Tower (high ground over the site; rope up from B Gym on its east edge)
            B(-1.04f, 0, -26.95f, 5.21f, 4, -25.39f, Surf.Elevated), // B Tower balcony (where the B Wall drops)
            B(-1.04f, 4, -25.57f, 5.21f, 4.85f, -25.39f, Surf.Trim), // B Tower balcony rail
            B(-1.04f, 7.2f, -27.08f, 5.21f, 8, -25.39f, Surf.Trim), // B Wall (raised: open at round start)
            B(5.99f, 0, -25.45f, 7.94f, 1.2f, -23.44f, Surf.Cover), // crate under the B Wall (6 cm into the wall: no slit)
            B(-9.17f, 0, -18.55f, -7.03f, 2.3f, -16.28f, Surf.Cover), // double crate in the site's north-west corner (into the walls)
            B(-7.09f, 0, -18.55f, -5.99f, 1.1f, -17.32f, Surf.Cover), // small crate beside it
            B(-3.12f, 0, -12.89f, -1.04f, 1.2f, -10.94f, Surf.Cover), // site boxes (two crates set at an angle in-game)
            B(-4.43f, 0, -11.33f, -2.6f, 1.2f, -9.11f, Surf.Cover), // site boxes
            B(7.03f, 0, -4.17f, 8.98f, 2.3f, -2.02f, Surf.Cover), // close box (double crate right of the Main exit, 6 cm into the wall)
            B(14.78f, 0, 12.63f, 16.93f, 1.2f, 14.71f, Surf.Cover), // crate in B Lobby (against the pillar)
            B(-6.25f, 0, 11.72f, 11.33f, 0.45f, 13.8f, Surf.Elevated), // raised walkway along B Main's south wall
        },
        Enemies = new[]
        {
            E("Close box", 10.16f, 0, -3.39f, Stance.Crouch),
            E("Close left", -7.94f, 0, -4.95f),
            E("Boxes", -4.3f, 0, -13.02f, Stance.Crouch),
            E("Corner", -4.56f, 0, -17.19f),
            E("Pillar cubby", 6.12f, 0, -13.67f),
            E("Tower", 1.95f, 4, -26.43f),
            E("B Drop", -8.46f, 0, -23.7f),
            E("Wall box", 9.24f, 0, -24.22f, Stance.Crouch),
            E("Gym door", 15.63f, 0, -26.6f),
            E("Behind pillar", 14.97f, 0, -17.19f),
            E("B Link", 22.79f, 0, -8.07f),
        },
        Flashes = new[]
        {
            F("Pop over the Main exit", V(0.2f, 1.6f, 3.4f), V(0.3f, 3.2f, -1.8f)),
            F("Right, over the close box", V(1.4f, 1.6f, 3.2f), V(5.6f, 3.4f, -5.2f)),
            F("Deep lob toward Tower", V(-0.4f, 1.7f, 3.8f), V(0.8f, 4.8f, -14f)),
            F("Left corner pop", V(-1.2f, 1.6f, 3.4f), V(-4.2f, 3.0f, -3.2f)),
        },
        // Standard B execute: smoke Tower (the high ground), the Gym door and B Link, the defenders' ways in.
        Smokes = new[]
        {
            new SmokeSpot("Tower", V(1.95f, 4, -27.6f)),
            new SmokeSpot("Gym", V(15.76f, 0, -27.34f)),
            new SmokeSpot("B Link", V(22.79f, 0, -8.07f)),
        },
        // Spike: open site in front of Tower, the north-west corner by the crates, by the pillar near the Main exit.
        PlantSpots = new[] { V(0.39f, 0, -19.53f), V(-5.21f, 0, -14.97f), V(3.26f, 0, -6.51f) },
        // Retakes: B Gym (two ways round its door), and B Link from Mid.
        RetakeEntries = new[] { V(16.28f, 0, -35.81f), V(22.5f, 0, -31f), V(25.5f, 0, -8.2f) },
        Look = SummitLook,
    };

    /// <summary>Mountain academy: white plaster halls with jade-green tile trims and friezes, grey stone pavers, timber
    /// crates, a gold-hazed sky and green karst peaks around (the art is ValTrainer's own procedural set).</summary>
    static World.Theme SummitLook(MapPalette p) => new()
    {
        Key = "summit", Style = World.DecorStyle.Haven, Hdri = World.Hdris.Qwantani, SkyYaw = 200,
        SkyTint = new Color(1.04f, 0.98f, 0.9f), SkySaturation = 1.05f,
        SunColor = new Color(1f, 0.92f, 0.8f), SunEnergy = 1.75f, Ambient = 1.05f, Ground = C(110, 124, 104),
        Saturation = 1.1f, Contrast = 1.04f, FogDensity = 0.0016f,
        Floor = new World.Surface("stone_tiles_03", p.Floor, 0.9f, 2.4f, 0.75f, Macro: 0.12f),
        Wall = new World.Surface("painted_plaster_wall", p.Wall, 0.85f, 2.2f, 1.3f, Normal: 0.9f, Macro: 0.08f),
        Base = new World.Surface("stone_tiles_03", C(118, 120, 116), 0.85f, 1.6f, 0.8f, Grime: 0.2f),
        Cap = new World.Surface("square_tiles", p.Trim, 0.9f, 1.2f, 0.8f, Grime: 0f),
        Trim = new World.Surface("square_tiles", C(47, 135, 124), 0.85f, 1.2f, 0.8f),
        Elevated = new World.Surface("stone_tiles_03", p.Elevated, 0.85f, 2f, 0.7f),
        Cover = new World.Surface("wood_planks", p.Cover, 0.75f, 1.5f, 0.75f, Grime: 0.1f),
        CoverFrame = new World.Surface("wood_planks", C(84, 60, 42), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new World.Surface("stone_tiles_03", C(120, 124, 116), 0.85f, 2.4f, 0.5f, Grime: 0f),
        Backdrop = new World.Surface("painted_plaster_wall", Colors.White, 0.85f, 2.2f, 1.2f, Macro: 0.1f),
        Roof = new World.Surface("square_tiles", C(46, 140, 128), 0.9f, 1.2f, 0.7f, Grime: 0f),
        Accent = new Color(0.14f, 0.5f, 0.45f), RoofTint = new Color(0.16f, 0.5f, 0.46f),
        Hues = new[] { C(240, 236, 226), C(230, 224, 210), C(244, 240, 232), C(222, 214, 200) },
        MinH = 8f, MaxH = 15f, Mountains = true, MountainColor = new Color(0.4f, 0.52f, 0.4f),
    };
}
