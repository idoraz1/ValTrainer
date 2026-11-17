using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Lotus, A Lobby → A Root → A Main → A Site, at true scale. Walls traced from the official minimap (valorant-api.com,
    /// 1 px = 0.1356 m; within ~0.3 m of its wall lines) and placed with its own transform; heights from reference screenshots
    /// and the minimap's shading (A Top and the Drop ledge are the lighter, raised areas).
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the A Main mouth onto the site, between the
    /// Stairs wall and the Tree block (the choke), -Z = north (A Top, Drop, defender spawn), +X = east (Hut, Tree, the cliff).
    /// You walk up out of A Lobby over the roots (A Root) into A Main, past the Rubble. From Main, the left lane runs north
    /// past the Stairs room onto the site; the right lane goes through the A Door into the Tree room, which opens onto the
    /// back of the site. The rotating A Door is static here, in its open position (the leaf lies along the passage, so both
    /// halves are walkable). The breakable wall to A Link is intact (sealed). On site: the Hut (closed, with a cubby on its
    /// north side), A Stairs climbing west toward defender spawn, A Top (+2.4 m) with steps down to the site, and the Drop
    /// ledge behind a railing whose open east end is the silent drop. Cut-offs (A Link → B, Stairs and Top → defender
    /// spawn, Lobby → attacker spawn) are sealed with walls.
    /// </summary>
    public static readonly MapSpot Lotus = new MapSpot
    {
        Key = "lotus", Map = "Lotus", Name = "A Main → A Site",
        Drill = "Climb A Root into A Main, then take A Site through Main or the rotating A Door: clear Close right, the Stairs room, Stairs, Top, Drop, Hut, Default, Back site, Site left and the Tree room",
        StartFeet = V(-15.33f, 0, 46.11f), StartYaw = -10, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(168, 146, 118), C(196, 150, 116), C(54, 142, 140), C(150, 104, 66), C(150, 134, 112), C(176, 204, 214)),
        Look = LotusLook,
        Boxes = new[]
        {
            B(-21.02f, -0.1f, -27.4f, 21.29f, 0, 50.73f, Surf.Floor), // ground
            B(-18.04f, 0, 5.7f, -17.23f, 4, 9.09f, Surf.Trim), // A Link breakable wall (intact; cut: A Link → B)
            B(-18.04f, 4, 5.7f, -17.23f, 7, 9.09f), // A Link breakable wall (intact; cut: A Link → B)
            B(-16.68f, 0, -20.07f, -16f, 7, -13.56f), // A Stairs exit (cut: defender spawn)
            B(-9.49f, 0, -27.13f, -8.82f, 7, -20.07f), // A Top west end (cut: defender spawn)
            B(-21.02f, 0, -27.4f, 21.29f, 7, -27.13f), // px 648,208-960,210
            B(-21.02f, 0, -27.13f, -9.49f, 7, -20.07f), // px 648,210-733,262
            B(-8.82f, 0, -27.13f, 21.29f, 7, -26.58f), // px 738,210-960,214
            B(20.34f, 0, -26.58f, 21.29f, 7, 50.73f), // px 953,214-960,784
            B(-8.82f, 0, -20.62f, -1.76f, 7, -19.12f), // px 738,258-790,269
            B(5.7f, 0f, -20.62f, 16.82f, 2.4f, -19.12f), // Drop ledge face + railing (2.4 m ledge, 1 m railing)
            B(5.7f, 2.4f, -20.62f, 16.82f, 3.4f, -19.12f, Surf.Trim), // Drop ledge face + railing (2.4 m ledge, 1 m railing)
            B(-21.02f, 0, -20.07f, -16.68f, 7, 5.7f), // px 648,262-680,452
            B(-16f, 0, -20.07f, -8.82f, 7, -19.12f), // px 685,262-738,269
            B(-2.31f, 0, -19.12f, -1.76f, 7, -11.39f), // px 786,269-790,326
            B(-16f, 0, -13.97f, -8.82f, 7, -11.26f), // px 685,307-738,327
            B(-16.68f, 0, -13.56f, -16f, 7, 5.97f), // px 680,310-685,454
            B(-1.76f, 0, -13.02f, 1.22f, 7, -11.26f), // px 790,314-812,327
            B(-2.17f, 0, -11.39f, -1.76f, 7, -11.26f), // px 787,326-790,327
            B(-16f, 0, -11.26f, -9.09f, 7, -10.85f), // px 685,327-736,330
            B(-1.22f, 0, -11.26f, 1.22f, 7, -2.98f), // px 794,327-812,388
            B(-16f, 0, -10.85f, -9.36f, 7, -10.31f), // px 685,330-734,334
            B(13.29f, 0f, -10.44f, 13.83f, 3.4f, -4.75f), // Hut east wall
            B(-16f, 0, -10.31f, -9.63f, 7, -10.04f), // px 685,334-732,336
            B(6.24f, 0f, -10.44f, 6.78f, 3.4f, -4.75f), // Hut west wall
            B(-16f, 0, -10.04f, -9.77f, 7, -5.15f), // px 685,336-731,372
            B(6.78f, 0f, -8.54f, 13.29f, 3.4f, -4.75f), // Hut (closed room; its cubby faces Drop)
            B(-9.77f, 0, -7.6f, -9.22f, 7, -5.43f), // px 731,354-735,370
            B(-9.22f, 0, -7.05f, -8.95f, 7, -5.97f), // px 735,358-737,366
            B(-8.95f, 0, -6.65f, -8.82f, 7, -5.97f), // px 737,361-738,366
            B(-4.34f, 0f, -6.24f, -1.22f, 1.1f, -5.97f, Surf.Trim), // low wall at the Stairs corner
            B(-16f, 0, -5.15f, -9.9f, 7, -4.88f), // px 685,372-730,374
            B(-16f, 0, -4.88f, -10.44f, 7, -4.34f), // px 685,374-726,378
            B(-16f, 0, -4.34f, -10.99f, 7, -3.8f), // px 685,378-722,382
            B(-16f, 0, -3.8f, -11.53f, 7, -3.26f), // px 685,382-718,386
            B(-16f, 0, -3.26f, -12.07f, 7, -3.12f), // px 685,386-714,387
            B(-16f, 0, -3.12f, -12.21f, 7, 5.97f), // px 685,387-713,454
            B(-1.36f, 0, 2.98f, 14.65f, 7, 4.07f), // px 793,432-911,440
            B(-1.49f, 0, 3.12f, -1.36f, 7, 8.54f), // px 792,433-793,473
            B(14.65f, 0, 3.12f, 14.78f, 7, 3.93f), // px 911,433-912,439
            B(-1.36f, 0, 4.07f, 3.26f, 7, 8.54f), // px 793,440-827,473
            B(-12.21f, 0, 4.48f, -10.31f, 7, 5.97f), // px 713,443-727,454
            B(-21.02f, 0, 5.7f, -18.04f, 7, 28.21f), // px 648,452-670,618
            B(-17.23f, 0, 5.7f, -16.68f, 7, 5.97f), // px 676,452-680,454
            B(3.26f, 0, 7.05f, 4.75f, 7, 8.54f), // px 827,462-838,473
            B(4.75f, 0, 7.32f, 5.02f, 7, 8.41f), // px 838,464-840,472
            B(2.71f, 0, 8.54f, 3.26f, 7, 16.14f), // px 823,473-827,529
            B(-18.04f, 0, 9.09f, -17.36f, 7, 27.8f), // px 670,477-675,615
            B(19.8f, 0, 9.49f, 20.34f, 7, 50.73f), // px 949,480-953,784
            B(19.26f, 0, 10.04f, 19.8f, 7, 50.73f), // px 945,484-949,784
            B(18.72f, 0, 10.58f, 19.26f, 7, 50.73f), // px 941,488-945,784
            B(18.17f, 0, 11.12f, 18.72f, 7, 50.73f), // px 937,492-941,784
            B(17.63f, 0, 11.66f, 18.17f, 7, 50.73f), // px 933,496-937,784
            B(17.09f, 0, 12.21f, 17.63f, 7, 50.73f), // px 929,500-933,784
            B(16.55f, 0, 12.75f, 17.09f, 7, 50.73f), // px 925,504-929,784
            B(16f, 0, 13.29f, 16.55f, 7, 50.73f), // px 921,508-925,784
            B(15.46f, 0, 13.83f, 16f, 7, 50.73f), // px 917,512-921,784
            B(14.92f, 0, 14.38f, 15.46f, 7, 50.73f), // px 913,516-917,784
            B(14.38f, 0, 14.92f, 14.92f, 7, 50.73f), // px 909,520-913,784
            B(9.9f, 0, 15.33f, 13.7f, 7, 50.73f), // px 876,523-904,784
            B(13.64f, 0, 15.19f, 14.38f, 7, 50.73f), // px 907,522-909,784
            B(9.77f, 0, 15.46f, 9.9f, 7, 50.73f), // px 875,524-876,784
            B(13.7f, 0, 15.46f, 14.11f, 7, 50.73f), // px 904,524-907,784
            B(2.85f, 0, 16.14f, 3.12f, 7, 16.28f), // px 824,529-826,530
            B(-17.36f, 0, 18.85f, -7.87f, 7, 19.53f), // px 675,549-745,554
            B(-17.36f, 0, 19.53f, -9.77f, 7, 19.8f), // px 675,554-731,556
            B(-17.36f, 0, 19.8f, -9.9f, 7, 20.34f), // px 675,556-730,560
            B(-17.36f, 0, 20.34f, -10.04f, 7, 20.89f), // px 675,560-729,564
            B(-17.36f, 0, 20.89f, -10.17f, 7, 21.29f), // px 675,564-728,567
            B(-17.36f, 0, 21.29f, -10.31f, 7, 21.84f), // px 675,567-727,571
            B(-17.36f, 0, 21.84f, -10.44f, 7, 22.38f), // px 675,571-726,575
            B(-17.36f, 0, 22.38f, -10.71f, 7, 22.65f), // px 675,575-724,577
            B(-17.36f, 0, 22.65f, -11.26f, 7, 23.19f), // px 675,577-720,581
            B(-17.36f, 0, 23.19f, -11.8f, 7, 23.74f), // px 675,581-716,585
            B(-17.36f, 0, 23.74f, -12.34f, 7, 24.28f), // px 675,585-712,589
            B(-17.36f, 0, 24.28f, -12.75f, 7, 24.82f), // px 675,589-709,593
            B(-17.36f, 0, 24.82f, -13.16f, 7, 25.23f), // px 675,593-706,596
            B(-17.36f, 0, 25.23f, -13.7f, 7, 25.77f), // px 675,596-702,600
            B(-17.36f, 0, 25.77f, -14.38f, 7, 26.18f), // px 675,600-697,603
            B(-17.36f, 0, 26.18f, -14.78f, 7, 26.45f), // px 675,603-694,605
            B(-17.36f, 0, 26.45f, -15.6f, 7, 26.99f), // px 675,605-688,609
            B(-17.36f, 0, 26.99f, -16.14f, 7, 27.26f), // px 675,609-684,611
            B(-3.12f, 0, 26.99f, -0.95f, 7, 29.16f), // px 780,609-796,625
            B(-17.36f, 0, 27.26f, -16.82f, 7, 27.67f), // px 675,611-679,614
            B(-17.36f, 0, 27.67f, -17.23f, 7, 27.8f), // px 675,614-676,615
            B(-18.04f, 0, 27.8f, -17.63f, 7, 28.08f), // px 670,615-673,617
            B(-21.02f, 0, 28.21f, -18.58f, 7, 28.48f), // px 648,618-666,620
            B(-21.02f, 0, 28.48f, -19.12f, 7, 28.75f), // px 648,620-662,622
            B(-21.02f, 0, 28.75f, -19.8f, 7, 29.02f), // px 648,622-657,624
            B(-21.02f, 0, 29.02f, -20.34f, 7, 29.16f), // px 648,624-653,625
            B(-21.02f, 0, 29.16f, -20.48f, 7, 38.25f), // px 648,625-652,692
            B(-15.87f, 0, 36.76f, 9.77f, 7, 37.98f), // px 686,681-875,690
            B(-9.36f, 0, 37.98f, 9.77f, 7, 50.73f), // px 734,690-875,784
            B(-21.02f, 0, 38.25f, -20.62f, 7, 38.38f), // px 648,692-651,693
            B(-21.02f, 0, 38.38f, -20.75f, 7, 50.73f), // px 648,693-650,784
            B(-20.75f, 0, 50.45f, -9.36f, 7, 50.73f), // px 650,782-734,784
            B(-8.82f, 0f, -26.58f, 5.7f, 2.4f, -20.62f, Surf.Elevated), // A Top (+2.4 m)
            B(5.7f, 0f, -26.58f, 20.34f, 2.4f, -20.62f, Surf.Elevated), // A Drop ledge (+2.4 m; the gap at its east end is the silent drop)
            B(-1.76f, 0f, -20.62f, 3.93f, 2.1f, -19.8f, Surf.Elevated), // A Top steps down to the site
            B(-1.76f, 0f, -19.8f, 3.93f, 1.8f, -18.99f, Surf.Elevated), // A Top steps
            B(-1.76f, 0f, -18.99f, 3.66f, 1.5f, -18.17f, Surf.Elevated), // A Top steps
            B(-1.76f, 0f, -18.17f, 3.26f, 1.2f, -17.36f, Surf.Elevated), // A Top steps
            B(-1.76f, 0f, -17.36f, 2.85f, 0.9f, -16.55f, Surf.Elevated), // A Top steps
            B(-1.76f, 0f, -16.55f, 2.44f, 0.6f, -15.73f, Surf.Elevated), // A Top steps
            B(-1.76f, 0f, -15.73f, 2.03f, 0.3f, -14.92f, Surf.Elevated), // A Top steps
            B(-8.95f, 0f, -19.12f, -7.73f, 0.25f, -13.97f, Surf.Elevated), // A Stairs (up toward defender spawn)
            B(-10.04f, 0f, -19.12f, -8.95f, 0.5f, -13.97f, Surf.Elevated), // A Stairs
            B(-11.12f, 0f, -19.12f, -10.04f, 0.75f, -13.97f, Surf.Elevated), // A Stairs
            B(-12.21f, 0f, -19.12f, -11.12f, 1f, -13.97f, Surf.Elevated), // A Stairs
            B(-13.29f, 0f, -19.12f, -12.21f, 1.25f, -13.97f, Surf.Elevated), // A Stairs
            B(-16f, 0f, -19.12f, -13.29f, 1.5f, -13.97f, Surf.Elevated), // A Stairs landing
            B(6.24f, 3.4f, -10.44f, 13.83f, 3.75f, -4.75f, Surf.Trim), // Hut roof
            B(8.95f, 0f, -4.8f, 10.99f, 1.25f, -2.58f, Surf.Cover), // default box against the Hut
            B(15.33f, 0f, -19.18f, 17.09f, 1.25f, -16.95f, Surf.Cover), // double box under Drop
            B(15.46f, 1.25f, -19.18f, 16.95f, 2.2f, -17.9f, Surf.Cover), // double box (stacked)
            B(-3.66f, 2.4f, -22.92f, -1.63f, 3.5f, -20.56f, Surf.Cover), // box on A Top
            B(-10.36f, 0f, 4.2f, -8.27f, 1.1f, 6.1f, Surf.Cover), // box at the Stairs room exit
            B(-10.58f, 0f, 16.68f, -9.22f, 1.25f, 18.91f, Surf.Cover), // crate in A Main (north-west corner)
            B(-9.28f, 0f, 16.68f, -7.73f, 2.2f, 18.91f, Surf.Cover), // crate stack in A Main
            B(-1f, 0f, 26.72f, 6.37f, 0.6f, 28.89f, Surf.Elevated), // A Rubble (low pile)
            B(-0.14f, 0f, 26.99f, 5.02f, 1.1f, 28.62f, Surf.Elevated), // A Rubble (top)
            B(-16.14f, 0f, 25.5f, -10.99f, 0.3f, 30.11f, Surf.Elevated), // A Root (root mound)
            B(-13.97f, 0f, 23.6f, -9.36f, 0.55f, 27.67f, Surf.Elevated), // A Root (root mound, top)
            B(9.36f, 0f, 9.63f, 10.85f, 6.5f, 11.12f, Surf.Cover), // A Tree (trunk)
            B(6.51f, 0f, 14.78f, 6.85f, 3.6f, 16.68f, Surf.Trim), // A Door: rotating door, static in its open position (leaf along the passage)
            B(6.24f, 0f, 15.46f, 7.19f, 3.6f, 16f, Surf.Trim), // A Door pivot
            B(3.2f, 3.6f, 15.05f, 9.96f, 7f, 16.41f), // A Door lintel
        },
        Enemies = new[]
        {
            E("Close right", 2.58f, 0, 1.9f, Stance.Crouch),
            E("Stairs room", -7.87f, 0, -8.14f),
            E("Stairs", -14.92f, 1.5f, -16.55f),
            E("Top", 1.22f, 2.4f, -23.33f),
            E("Drop", 13.83f, 2.4f, -23.6f, Stance.Crouch),
            E("Hut", 10.04f, 0, -9.49f, Stance.Crouch),
            E("Default", 11.94f, 0, -3.39f, Stance.Crouch),
            E("Back site", 18.99f, 0, -14.24f),
            E("Site left", 7.73f, 0, -16f),
            E("Tree", 13.83f, 0, 12.21f),
            E("Tree corner", 18.04f, 0, 8.14f, Stance.Crouch),
        },
        Flashes = new[]
        {
            F("Pop over the Main exit", V(-1.76f, 1.6f, 2.44f), V(0.41f, 3.2f, -1.63f)),
            F("Over the Stairs wall", V(-2.58f, 1.6f, 2.98f), V(6.37f, 5, -6.78f)),
            F("Lob over the Hut", V(-2.85f, 1.7f, 4.07f), V(11.12f, 5.5f, -10.85f)),
            F("Through the Door into Tree", V(6.51f, 1.6f, 18.72f), V(8.82f, 2.6f, 11.12f)),
        },
        // Standard A Main execute: Stairs (defender-spawn side), the foot of Top, and Tree.
        Smokes = new[] { new SmokeSpot("Stairs", V(-8.27f, 0, -14.92f)), new SmokeSpot("Top", V(4.48f, 0, -16f)), new SmokeSpot("Tree", V(11.8f, 0, 12.21f)) },
        // Spike: east of the Hut, between the Hut and Top, back site under Drop.
        PlantSpots = new[] { V(16.55f, 0, -8.14f), V(10.44f, 0, -14.92f), V(17.9f, 0, -12.5f) },
        // Retakes: down A Stairs, from A Top's west end, and from the back of the Drop ledge (behind its railing).
        RetakeEntries = new[] { V(-15.05f, 1.5f, -16.55f), V(-7.19f, 2.4f, -24.14f), V(7.5f, 2.4f, -25.8f) },
    };

    /// <summary>Temple ruins in the Western Ghats: warm laterite and sandstone, teal-painted wood and tile bands, carved stone
    /// floors, dark timber crates, a soft hazy sun and green hills.</summary>
    static World.Theme LotusLook(MapPalette p) => new()
    {
        Key = "lotus", Style = World.DecorStyle.Bind, Hdri = World.Hdris.Kloofendal, SkyYaw = 70,
        SunColor = new Color(1f, 0.92f, 0.8f), SunEnergy = 1.75f, Ambient = 1.05f, Ground = C(132, 118, 96),
        SkyTint = new Color(0.98f, 1f, 1f), Saturation = 1.12f, FogDensity = 0.0016f,
        Floor = new World.Surface("stone_tiles_03", p.Floor, 0.85f, 2.4f, 0.8f, Normal: 0.9f, Macro: 0.12f, Grime: 0.15f),
        Wall = new World.Surface("clay_plaster", p.Wall, 0.9f, 2.4f, 1.2f, Normal: 1f, Macro: 0.1f, Grime: 0.15f),
        Base = new World.Surface("sandstone_blocks_08", C(150, 126, 100), 0.7f, 2.4f, 0.9f, Rough: 0.2f, Grime: 0.3f),
        Cap = new World.Surface("red_sandstone_wall", C(160, 94, 66), 0.85f, 1.6f, 0.7f),
        Trim = new World.Surface("wood_planks", p.Trim, 0.85f, 1.5f, 0.7f, Grime: 0.08f),
        Elevated = new World.Surface("sandstone_blocks_08", p.Elevated, 0.8f, 2.4f, 0.8f, Grime: 0.15f),
        Cover = new World.Surface("wood_planks", p.Cover, 0.75f, 1.5f, 0.75f, Grime: 0.1f),
        CoverFrame = new World.Surface("wood_planks", C(86, 60, 40), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new World.Surface("stone_tiles_03", C(150, 134, 110), 0.85f, 2.6f, 0.6f, Grime: 0f),
        Backdrop = new World.Surface("clay_plaster", Colors.White, 0.9f, 2.4f, 1.4f, Macro: 0.12f),
        Roof = new World.Surface("red_sandstone_wall", C(150, 86, 60), 0.9f, 1.6f, 0.6f, Grime: 0f),
        Hues = new[] { C(206, 160, 122), C(190, 140, 104), C(218, 186, 146), C(176, 128, 98), C(160, 176, 150), C(214, 196, 166) },
        MinH = 7.5f, MaxH = 13f, Mountains = true, MountainColor = new Color(0.3f, 0.44f, 0.34f),
    };
}
