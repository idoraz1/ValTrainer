using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Corrode, B Lobby → B Main → B Site, at true scale. Walls traced from the official minimap (valorant-api.com,
    /// 1 px = 0.1395 m; within ~0.3 m of its wall lines) and placed with its own transform; heights from reference screenshots
    /// and the minimap's shading (B Tower is the lighter, raised room).
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the B Main exit onto the site, between the
    /// Main pillar and the Elbow wall (the choke), -Z = north (B Link, Mid), +X = east (B Main, B Lobby, attacker spawn).
    /// Attackers spawn on the east side of Corrode, so you walk south down B Lobby, west along B Main, and turn into the
    /// pocket at its end (B Main box), which opens west onto the site. On site: the salt machine (a 2.4 m block with a 1.2 m
    /// step) at the north, the raised platform (+0.9 m) at the south, a crate against the Main pillar, B Link to the north,
    /// B Tower (+2 m, a covered room with a railing over the site, stairs down to the Elbow side) in the west, and B Elbow
    /// to the south-west. Cut-offs (B Link → Mid, B Arch → defender spawn, B Lobby → Mid and attacker spawn) are sealed.
    /// </summary>
    public static readonly MapSpot Corrode = new MapSpot
    {
        Key = "corrode", Map = "Corrode", Name = "B Main → B Site",
        Drill = "Go down B Lobby and along B Main, then take B Site from the Main pocket: clear Close left, Pillar, Default behind the machine, Platform, Back site, B Tower, the Tower railing, B Link, B Elbow and the Elbow corner",
        StartFeet = V(30.13f, 0, -27.34f), StartYaw = -150, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(136, 130, 120), C(172, 166, 154), C(178, 98, 56), C(150, 122, 92), C(118, 124, 130), C(186, 200, 214)),
        Look = CorrodeLook,
        Boxes = new[]
        {
            B(-34.32f, -0.1f, -32.37f, 34.88f, 0, 17.58f, Surf.Floor), // ground
            B(-34.32f, 0, -32.37f, 34.88f, 7, -32.09f), // px 250,594-746,596
            B(-34.32f, 0, -32.09f, -17.3f, 7, -16.18f), // px 250,596-372,710
            B(-9.49f, 0, -32.09f, 25.67f, 7, -31.53f), // px 428,596-680,600
            B(34.04f, 0, -32.09f, 34.88f, 7, -31.95f), // px 740,596-746,597
            B(34.6f, 0, -31.95f, 34.88f, 7, 17.58f), // px 744,597-746,952
            B(-8.93f, 0, -31.53f, 25.67f, 7, -30.97f), // px 432,600-680,604
            B(-8.37f, 0, -30.97f, 25.67f, 7, -30.41f), // px 436,604-680,608
            B(-7.81f, 0, -30.41f, 25.67f, 7, -29.86f), // px 440,608-680,612
            B(-7.25f, 0, -29.86f, 25.67f, 7, -29.3f), // px 444,612-680,616
            B(-6.98f, 0, -29.3f, 25.67f, 7, -29.02f), // px 446,616-680,618
            B(-6.7f, 0, -29.02f, 25.67f, 7, -20.23f), // px 448,618-680,681
            B(-17.3f, 0, -23.58f, -14.23f, 7, -16.18f), // px 372,657-394,710
            B(-9.77f, 0, -23.58f, -6.7f, 7, -20.23f), // px 426,657-448,681
            B(34.18f, 0, -23.58f, 34.6f, 7, 17.58f), // px 741,657-744,952
            B(-1.67f, 0, -20.23f, 1.67f, 7, -3.21f), // px 484,681-508,803
            B(25.25f, 0, -20.23f, 25.67f, 7, -16.18f), // px 677,681-680,710
            B(-34.32f, 0, -16.18f, -30.13f, 7, -4.88f), // px 250,710-280,791
            B(-23.58f, 0, -16.18f, -22.18f, 7, -12.14f), // px 327,710-337,739
            B(8.23f, 0, -11.58f, 17.86f, 7, -11.16f), // px 555,743-624,746
            B(12.28f, 0, -11.16f, 17.86f, 7, 17.58f), // px 584,746-624,952
            B(-25.67f, 0, -7.67f, -22.18f, 7, 7.67f), // px 312,771-337,881
            B(17.86f, 0, -7.67f, 34.18f, 7, 17.58f), // px 624,771-741,952
            B(-34.32f, 0, -4.88f, -34.04f, 7, 17.58f), // px 250,791-252,952
            B(-33.76f, 0, -4.88f, -30.13f, 7, -4.32f), // px 254,791-280,795
            B(-33.34f, 0, -4.32f, -30.13f, 7, -4.19f), // px 257,795-280,796
            B(-1.67f, 0, 3.35f, 1.67f, 7, 17.58f), // px 484,850-508,952
            B(-13.67f, 0, 4.32f, -10.18f, 7, 17.58f), // px 398,857-423,952
            B(-22.18f, 0, 7.25f, -18.28f, 7, 7.67f), // px 337,878-365,881
            B(-10.18f, 0, 8.23f, -1.67f, 7, 17.58f), // px 423,885-484,952
            B(1.67f, 0, 8.23f, 12.28f, 7, 17.58f), // px 508,885-584,952
            B(-34.04f, 0, 16.32f, -13.67f, 7, 17.58f), // px 252,943-398,952
            B(-30.13f, 0f, -16.18f, -23.52f, 2f, -7.67f, Surf.Elevated), // B Tower (+2 m)
            B(-23.58f, 0f, -12.14f, -22.18f, 2f, -7.67f, Surf.Elevated), // B Tower front
            B(-22.6f, 2f, -12.14f, -22.13f, 3f, -7.67f, Surf.Trim), // B Tower railing (look down onto the site)
            B(-30.13f, 0f, -7.67f, -25.61f, 1.6f, -6.84f, Surf.Elevated), // B Tower stairs (down to the Elbow side)
            B(-30.13f, 0f, -6.84f, -25.61f, 1.2f, -6f, Surf.Elevated), // B Tower stairs
            B(-30.13f, 0f, -6f, -25.61f, 0.8f, -5.16f, Surf.Elevated), // B Tower stairs
            B(-30.13f, 0f, -5.16f, -25.61f, 0.4f, -4.32f, Surf.Elevated), // B Tower stairs
            B(-30.13f, 5.2f, -16.18f, -22.18f, 5.5f, -7.67f, Surf.Trim), // B Tower roof
            B(-15.9f, 0f, -12.28f, -8.09f, 2.4f, -10.32f, Surf.Cover), // salt machine (high part)
            B(-15.07f, 0f, -10.38f, -8.93f, 1.2f, -8.51f, Surf.Cover), // salt machine (step)
            B(-18.28f, 0f, -6f, -5.72f, 0.9f, -2.51f, Surf.Elevated), // B platform (+0.9 m)
            B(-18.28f, 0f, -2.57f, -13.76f, 0.9f, -0.14f, Surf.Elevated), // B platform
            B(-9.96f, 0f, -2.57f, -5.72f, 0.9f, -0.14f, Surf.Elevated), // B platform
            B(-13.81f, 0f, -2.51f, -9.91f, 0.45f, -0.14f, Surf.Elevated), // B platform step
            B(-4.05f, 0f, -14.93f, -1.62f, 1.25f, -13.11f, Surf.Cover), // crate against the Main pillar (site side)
            B(1.62f, 0f, -6f, 4.05f, 1.25f, -4.05f, Surf.Cover), // B Main box
            B(1.62f, 1.25f, -5.72f, 3.07f, 2.2f, -4.32f, Surf.Cover), // B Main box (stacked)
            B(10.04f, 0f, 6f, 12.33f, 1.25f, 8.29f, Surf.Cover), // crate in the Main pocket
            B(-27.9f, 0f, 5.86f, -25.61f, 1.25f, 7.95f, Surf.Cover), // crate at B Elbow
            B(-24.97f, 0f, 13.95f, -22.88f, 1.25f, 16.38f, Surf.Cover), // crate at the back of B Elbow
            B(25.67f, 0f, -31.86f, 27.76f, 1.25f, -29.86f, Surf.Cover), // crate in B Lobby
            B(25.67f, 0f, -29.86f, 27.06f, 1f, -28.6f, Surf.Trim), // barrels in B Lobby
        },
        Enemies = new[]
        {
            E("Close left", -4.74f, 0, 6.42f, Stance.Crouch),
            E("Pillar", -2.79f, 0, -15.9f, Stance.Crouch),
            E("Default", -13.39f, 0, -13.67f, Stance.Crouch),
            E("Platform", -16.46f, 0.9f, -3.91f),
            E("Back site", -20.09f, 0, -14.51f),
            E("B Tower", -27.34f, 2, -11.72f),
            E("Tower railing", -23.44f, 2, -9.63f, Stance.Crouch),
            E("B Link", -12f, 0, -25.95f),
            E("B Elbow", -20.09f, 0, 11.02f),
            E("Elbow corner", -31.81f, 0, -1.53f),
        },
        Flashes = new[]
        {
            F("Pop over the Main exit", V(3.35f, 1.6f, 0), V(-0.84f, 3.2f, -0.56f)),
            F("Over the pillar onto site", V(3.91f, 1.6f, -2.79f), V(-7.81f, 5, -5.02f)),
            F("Lob onto the Tower", V(3.35f, 1.7f, 1.95f), V(-20.09f, 5.5f, -9.21f)),
            F("Curve into Elbow", V(2.79f, 1.6f, 2.79f), V(-16.18f, 3, 6.42f)),
        },
        // Standard B Main execute: B Tower (the high angle), B Link (the Mid side) and Elbow (the defender-spawn side).
        Smokes = new[] { new SmokeSpot("B Tower", V(-24.83f, 2, -9.91f)), new SmokeSpot("B Link", V(-12f, 0, -22.88f)), new SmokeSpot("Elbow", V(-18.97f, 0, 9.21f)) },
        // Spike: between the machine and the platform, open site by the pillar, under the Tower.
        PlantSpots = new[] { V(-12f, 0, -7.25f), V(-6.98f, 0, -7.53f), V(-17.02f, 0, -7.25f) },
        // Retakes: B Link from Mid, the back of B Elbow, and the Arch side under the Tower.
        RetakeEntries = new[] { V(-12f, 0, -29.86f), V(-27.34f, 0, 13.81f), V(-28.74f, 0, -2.79f) },
    };

    /// <summary>Mont-Saint-Michel turned salt mine: grey granite masonry, cobbled lanes, rusted orange machinery, metal decks,
    /// salt-white crust, an overcast Normandy sky and pale flats around.</summary>
    static World.Theme CorrodeLook(MapPalette p) => new()
    {
        Key = "corrode", Style = World.DecorStyle.Ascent, Hdri = World.Hdris.Overcast, SkyYaw = 200,
        SunColor = new Color(1f, 0.97f, 0.92f), SunEnergy = 1.5f, Ambient = 1.1f, Ground = C(150, 146, 136),
        SkyTint = new Color(0.96f, 0.99f, 1.03f), Saturation = 1.02f, Contrast = 1.05f, FogDensity = 0.002f,
        Floor = new World.Surface("cobblestone_floor_06", p.Floor, 0.85f, 2.2f, 0.55f, Normal: 0.9f, Macro: 0.12f, Grime: 0.12f),
        Wall = new World.Surface("sandstone_blocks_08", p.Wall, 0.8f, 2.4f, 1.1f, Normal: 1f, Macro: 0.1f, Grime: 0.15f),
        Base = new World.Surface("stone_tiles_03", C(214, 212, 204), 0.8f, 2.4f, 0.9f, Grime: 0.1f),
        Cap = new World.Surface("concrete_wall_008", C(150, 146, 140), 0.9f, 2.4f, 0.7f, Grime: 0f),
        Trim = new World.Surface("rusty_metal_sheet", p.Trim, 0.85f, 2f, 0.7f, Grime: 0.08f),
        Elevated = new World.Surface("metal_plate_02", p.Elevated, 0.85f, 2f, 0.7f, Metal: 0.5f, Grime: 0.1f),
        Cover = new World.Surface("rusty_metal_sheet", p.Cover, 0.75f, 2f, 0.75f, Grime: 0.12f),
        CoverFrame = new World.Surface("metal_plate_02", C(84, 88, 94), 0.85f, 1.6f, 0.6f, Metal: 0.6f, Grime: 0f),
        GroundSurf = new World.Surface("cobblestone_floor_06", C(150, 144, 134), 0.85f, 2.4f, 0.5f, Grime: 0f),
        Backdrop = new World.Surface("sandstone_blocks_08", Colors.White, 0.8f, 2.4f, 1.2f, Macro: 0.12f),
        Roof = new World.Surface("metal_plate_02", C(82, 86, 96), 0.85f, 2f, 0.6f, Metal: 0.3f, Grime: 0f),
        Hues = new[] { C(176, 172, 162), C(160, 156, 148), C(194, 188, 176), C(144, 142, 138), C(210, 206, 196), C(168, 150, 132) },
        MinH = 8f, MaxH = 16f, Mountains = true, MountainColor = new Color(0.78f, 0.78f, 0.76f),
    };
}
