using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Abyss, B Lobby → B Main → B Site, at true scale (the layout after patch 11.08's B rework). Walls, void edges and
    /// props were traced from the official minimap (valorant-api.com, 1 px = 0.1206 m) with its own transform; heights are
    /// estimates from the minimap's shading and gameplay footage (B Tower +2.8 m, B Nest +1.5 m, the site platform +1 m).
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the B Main exit onto the site (the choke),
    /// -Z = north (B Link, Mid), +X = east (B Main, B Lobby, attacker spawn), so you walk west (-X) from the lobby.
    /// Abyss has no outer walls: the whole map is a rock slab standing 20 m above the ground plane (the walkable floor is
    /// at Y = 20), its open edges have only a knee-high lip, and landing down in the void kills (<see cref="MapSpot.VoidBelow"/>;
    /// a 20 m drop is also lethal fall damage). B Danger is the narrow ledge on the pillar across the void gap south of B Main:
    /// jump the 2.4 m gap from B Main, walk the ledge, jump the 2.9 m gap onto the site. Cut-offs where the real map continues
    /// (B Lobby → attacker spawn, the Mid hallway into B Main, B Link → Mid) are sealed with walls.
    /// </summary>
    public static readonly MapSpot Abyss = new MapSpot
    {
        Key = "abyss", Map = "Abyss", Name = "B Main → B Site",
        Drill = "Push B Lobby into B Main and out onto B Site: clear the corner crate, Boxes, Cubby, the platform, Back site, B Link, the ramp and B Tower, and don't fall off",
        StartFeet = V(47.02f, 20, -12.78f), StartYaw = -90, Choke = V(0, 20, 0),
        Palette = new MapPalette(C(108, 110, 106), C(118, 124, 122), C(62, 90, 98), C(96, 110, 92), C(112, 110, 104), C(150, 172, 196)),
        VoidBelow = 10,
        Look = AbyssLook,
        Boxes = new[]
        {
            // --- traced: the rock slab (floor, 8 m thick, ends at the void), raised areas, walls and lips on the void edges
            B(-24.84f, 12, -21.94f, 50.64f, 20, -9.89f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-24.84f, 12, -9.89f, 42.2f, 20, -5.06f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-24.84f, 12, -5.06f, 28.45f, 20, -4.58f, Surf.Floor), // rock slab (floor; ends at the void)
            B(33.76f, 12, -5.06f, 42.2f, 20, -4.58f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-24.84f, 12, -4.58f, -20.01f, 20, -2.65f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-19.05f, 12, -4.58f, 28.45f, 20, 3.62f, Surf.Floor), // rock slab (floor; ends at the void)
            B(34, 12, -4.58f, 42.2f, 20, -4.34f, Surf.Floor), // rock slab (floor; ends at the void)
            B(39.06f, 12, -4.34f, 42.2f, 20, -4.1f, Surf.Floor), // rock slab (floor; ends at the void)
            B(39.3f, 12, -4.1f, 42.2f, 20, -3.86f, Surf.Floor), // rock slab (floor; ends at the void)
            B(39.79f, 12, -3.86f, 42.2f, 20, -3.62f, Surf.Floor), // rock slab (floor; ends at the void)
            B(40.03f, 12, -3.62f, 42.2f, 20, -3.38f, Surf.Floor), // rock slab (floor; ends at the void)
            B(40.27f, 12, -3.38f, 42.2f, 20, -3.13f, Surf.Floor), // rock slab (floor; ends at the void)
            B(40.51f, 12, -3.13f, 42.2f, 20, -2.89f, Surf.Floor), // rock slab (floor; ends at the void)
            B(40.75f, 12, -2.89f, 42.2f, 20, -2.65f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-24.84f, 12, -2.65f, -20.25f, 20, -2.41f, Surf.Floor), // rock slab (floor; ends at the void)
            B(40.99f, 12, -2.65f, 42.2f, 20, -2.41f, Surf.Floor), // rock slab (floor; ends at the void)
            B(41.23f, 12, -2.41f, 42.2f, 20, -2.17f, Surf.Floor), // rock slab (floor; ends at the void)
            B(41.47f, 12, -2.17f, 42.2f, 20, -1.93f, Surf.Floor), // rock slab (floor; ends at the void)
            B(41.71f, 12, -1.93f, 42.2f, 20, -1.69f, Surf.Floor), // rock slab (floor; ends at the void)
            B(41.96f, 12, -1.69f, 42.2f, 20, -1.45f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-19.05f, 12, 3.62f, 5.55f, 20, 12.54f, Surf.Floor), // rock slab (floor; ends at the void)
            B(16.88f, 12, 3.62f, 28.45f, 20, 8.44f, Surf.Floor), // rock slab (floor; ends at the void)
            B(7.23f, 12, 5.3f, 15.19f, 20, 13.02f, Surf.Floor), // rock slab (floor; ends at the void)
            B(16.88f, 12, 8.44f, 23.63f, 20, 15.19f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-18.81f, 12, 12.54f, 5.55f, 20, 12.78f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-18.57f, 12, 12.78f, 5.55f, 20, 13.02f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-17.84f, 12, 13.02f, 5.55f, 20, 13.26f, Surf.Floor), // rock slab (floor; ends at the void)
            B(7.47f, 12, 13.02f, 15.19f, 20, 13.26f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-17.6f, 12, 13.26f, 5.55f, 20, 13.5f, Surf.Floor), // rock slab (floor; ends at the void)
            B(7.96f, 12, 13.26f, 14.95f, 20, 14.47f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-17.12f, 12, 13.5f, 5.55f, 20, 13.74f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-16.88f, 12, 13.74f, 5.55f, 20, 15.19f, Surf.Floor), // rock slab (floor; ends at the void)
            B(-16.64f, 12, 15.19f, 5.55f, 20, 15.43f, Surf.Floor), // rock slab (floor; ends at the void)
            B(17.12f, 12, 15.19f, 23.39f, 20, 15.43f, Surf.Floor), // rock slab (floor; ends at the void)
            B(32.07f, 20, -8.68f, 32.79f, 20.3f, -5.3f, Surf.Elevated), // raised +0.3 m
            B(-24.35f, 20, -17.6f, -20.5f, 20.35f, -16.64f, Surf.Elevated), // raised +0.35 m
            B(31.11f, 20, -8.68f, 32.07f, 20.6f, -5.3f, Surf.Elevated), // raised +0.6 m
            B(-24.35f, 20, -16.64f, -20.5f, 20.7f, -15.67f, Surf.Elevated), // raised +0.7 m
            B(30.14f, 20, -8.68f, 31.11f, 20.9f, -5.3f, Surf.Elevated), // raised +0.9 m
            B(-24.35f, 20, -15.67f, -20.5f, 21.05f, -14.71f, Surf.Elevated), // raised +1.05 m
            B(29.42f, 20, -8.68f, 30.14f, 21.2f, -5.3f, Surf.Elevated), // raised +1.2 m
            B(-24.35f, 20, -14.71f, -20.5f, 21.4f, -13.74f, Surf.Elevated), // raised +1.4 m
            B(25.56f, 20, -9.89f, 27.25f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(25.32f, 20, -9.65f, 25.56f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(25.08f, 20, -8.92f, 25.32f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(24.35f, 20, -8.68f, 25.08f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(27.25f, 20, -8.68f, 29.42f, 21.5f, -5.3f, Surf.Elevated), // raised +1.5 m
            B(27.25f, 20, -5.3f, 28.21f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(23.15f, 20, 2.17f, 24.35f, 21.5f, 8.2f, Surf.Elevated), // raised +1.5 m
            B(-24.35f, 20, -13.74f, -20.5f, 21.75f, -12.78f, Surf.Elevated), // raised +1.75 m
            B(-24.35f, 20, -12.78f, -20.5f, 22.1f, -11.82f, Surf.Elevated), // raised +2.1 m
            B(-24.35f, 20, -11.82f, -20.5f, 22.45f, -10.85f, Surf.Elevated), // raised +2.45 m
            B(-24.35f, 20, -10.85f, -20.5f, 22.8f, -2.89f, Surf.Elevated), // raised +2.8 m
            B(-20.5f, 20, -9.89f, -16.16f, 22.8f, -4.82f, Surf.Elevated), // raised +2.8 m
            B(-18.57f, 20, -4.82f, -16.16f, 22.8f, 12.54f, Surf.Elevated), // raised +2.8 m
            B(-20.25f, 20, -4.58f, -20.01f, 22.8f, -2.65f, Surf.Elevated), // raised +2.8 m
            B(-19.05f, 20, -4.58f, -18.81f, 22.8f, 12.54f, Surf.Elevated), // raised +2.8 m
            B(-24.84f, 20, -2.65f, -20.25f, 22.8f, -2.41f, Surf.Elevated), // raised +2.8 m
            B(-18.08f, 20, 12.54f, -16.16f, 22.8f, 12.78f, Surf.Elevated), // raised +2.8 m
            B(-17.6f, 20, 12.78f, -16.16f, 22.8f, 13.02f, Surf.Elevated), // raised +2.8 m
            B(-17.12f, 20, 13.02f, -16.16f, 22.8f, 13.26f, Surf.Elevated), // raised +2.8 m
            B(-16.88f, 20, 13.74f, -16.16f, 22.8f, 15.19f, Surf.Elevated), // raised +2.8 m
            B(-24.11f, 20, -21.94f, 21.7f, 26.5f, -21.7f), // wall
            B(21.94f, 20, -21.94f, 50.64f, 26.5f, -21.46f), // wall
            B(0.24f, 20, -21.7f, 15.43f, 26.5f, -3.13f), // wall
            B(22.18f, 20, -21.46f, 50.64f, 26.5f, -21.22f), // wall
            B(22.42f, 20, -21.22f, 50.64f, 26.5f, -20.74f), // wall
            B(22.67f, 20, -20.74f, 50.64f, 26.5f, -20.5f), // wall
            B(22.91f, 20, -20.5f, 50.64f, 26.5f, -20.01f), // wall
            B(23.15f, 20, -20.01f, 50.64f, 26.5f, -19.77f), // wall
            B(24.35f, 20, -19.77f, 50.64f, 26.5f, -19.53f), // wall
            B(27.97f, 20, -19.53f, 50.64f, 26.5f, -16.4f), // wall
            B(-20.5f, 20, -18.81f, -5.79f, 26.5f, -12.78f), // wall
            B(-5.79f, 20, -18.57f, -5.55f, 26.5f, -12.78f), // wall
            B(27.97f, 20, -16.4f, 40.75f, 26.5f, -16.16f), // wall
            B(50.4f, 20, -16.4f, 50.64f, 26.5f, -10.61f), // wall
            B(27.97f, 20, -16.16f, 40.51f, 26.5f, -15.91f), // wall
            B(27.97f, 20, -15.91f, 40.27f, 26.5f, -15.67f), // wall
            B(-20.5f, 20, -12.78f, -12.54f, 26.5f, -12.54f), // wall
            B(-20.5f, 20, -12.54f, -12.78f, 26.5f, -12.3f), // wall
            B(-20.5f, 20, -12.3f, -13.02f, 26.5f, -12.06f), // wall
            B(-20.5f, 20, -12.06f, -13.26f, 26.5f, -11.82f), // wall
            B(-20.5f, 20, -11.82f, -13.5f, 26.5f, -11.57f), // wall
            B(-20.5f, 20, -11.57f, -13.74f, 26.5f, -11.33f), // wall
            B(-20.5f, 20, -11.33f, -13.99f, 26.5f, -11.09f), // wall
            B(-20.5f, 20, -11.09f, -14.23f, 26.5f, -10.85f), // wall
            B(-20.5f, 20, -10.85f, -14.47f, 26.5f, -10.61f), // wall
            B(-20.5f, 20, -10.61f, -14.71f, 26.5f, -10.37f), // wall
            B(23.39f, 20, -9.89f, 25.56f, 26.5f, -9.65f), // wall
            B(27.25f, 20, -9.89f, 34.24f, 26.5f, -8.68f), // wall
            B(23.15f, 20, -9.65f, 25.32f, 26.5f, -9.16f), // wall
            B(23.15f, 20, -9.16f, 23.87f, 26.5f, 2.17f), // wall
            B(24.11f, 20, -9.16f, 25.32f, 26.5f, -8.92f), // wall
            B(23.87f, 20, -8.92f, 25.08f, 26.5f, -8.68f), // wall
            B(23.87f, 20, -8.68f, 24.35f, 26.5f, 2.17f), // wall
            B(15.43f, 20, -6.99f, 17.36f, 26.5f, -3.13f), // wall
            B(34, 20, -5.55f, 34.24f, 26.5f, -5.3f), // wall
            B(0.48f, 20, -3.13f, 17.12f, 26.5f, -2.89f), // wall
            B(7.23f, 20, 5.3f, 15.19f, 26.5f, 12.54f), // wall
            B(7.23f, 20, 12.54f, 8.2f, 26.5f, 13.02f), // wall
            B(14.95f, 20, 12.54f, 15.19f, 26.5f, 13.02f), // wall
            B(-16.4f, 20, 13.5f, -14.71f, 26.5f, 13.74f), // wall
            B(-14.95f, 20, 13.74f, -14.71f, 26.5f, 14.71f), // wall
            B(-24.84f, 20, -21.94f, -24.11f, 20.45f, -21.7f, Surf.Trim), // lip on the void edge
            B(-24.84f, 20, -21.7f, -24.35f, 20.45f, -2.65f, Surf.Trim), // lip on the void edge
            B(50.4f, 20, -10.61f, 50.64f, 20.45f, -9.89f, Surf.Trim), // lip on the void edge
            B(28.21f, 20, -5.3f, 34.24f, 20.45f, -5.06f, Surf.Trim), // lip on the void edge
            B(28.21f, 20, -5.06f, 28.45f, 20.45f, 8.44f, Surf.Trim), // lip on the void edge
            B(34, 20, -5.06f, 34.24f, 20.45f, -4.58f, Surf.Trim), // lip on the void edge
            B(-20.5f, 20, -4.82f, -18.57f, 20.45f, -4.58f, Surf.Trim), // lip on the void edge
            B(34.24f, 20, -4.82f, 39.06f, 20.45f, -4.58f, Surf.Trim), // lip on the void edge
            B(-20.5f, 20, -4.58f, -20.25f, 20.45f, -2.65f, Surf.Trim), // lip on the void edge
            B(-18.81f, 20, -4.58f, -18.57f, 20.45f, 12.78f, Surf.Trim), // lip on the void edge
            B(39.06f, 20, -4.58f, 39.3f, 20.45f, -4.34f, Surf.Trim), // lip on the void edge
            B(39.3f, 20, -4.34f, 39.54f, 20.45f, -4.1f, Surf.Trim), // lip on the void edge
            B(39.54f, 20, -4.1f, 39.79f, 20.45f, -3.86f, Surf.Trim), // lip on the void edge
            B(39.79f, 20, -3.86f, 40.03f, 20.45f, -3.62f, Surf.Trim), // lip on the void edge
            B(40.03f, 20, -3.62f, 40.27f, 20.45f, -3.38f, Surf.Trim), // lip on the void edge
            B(40.27f, 20, -3.38f, 40.51f, 20.45f, -3.13f, Surf.Trim), // lip on the void edge
            B(40.51f, 20, -3.13f, 40.75f, 20.45f, -2.89f, Surf.Trim), // lip on the void edge
            B(-24.35f, 20, -2.89f, -20.5f, 20.45f, -2.65f, Surf.Trim), // lip on the void edge
            B(40.75f, 20, -2.89f, 40.99f, 20.45f, -2.65f, Surf.Trim), // lip on the void edge
            B(40.99f, 20, -2.65f, 41.23f, 20.45f, -2.41f, Surf.Trim), // lip on the void edge
            B(41.23f, 20, -2.41f, 41.47f, 20.45f, -2.17f, Surf.Trim), // lip on the void edge
            B(41.47f, 20, -2.17f, 41.71f, 20.45f, -1.93f, Surf.Trim), // lip on the void edge
            B(41.71f, 20, -1.93f, 41.96f, 20.45f, -1.69f, Surf.Trim), // lip on the void edge
            B(41.96f, 20, -1.69f, 42.2f, 20.45f, -1.45f, Surf.Trim), // lip on the void edge
            B(23.15f, 20, 8.2f, 28.21f, 20.45f, 8.44f, Surf.Trim), // lip on the void edge
            B(23.15f, 20, 8.44f, 23.39f, 20.45f, 15.19f, Surf.Trim), // lip on the void edge
            B(-18.57f, 20, 12.54f, -18.08f, 20.45f, 12.78f, Surf.Trim), // lip on the void edge
            B(-18.33f, 20, 12.78f, -17.6f, 20.45f, 13.02f, Surf.Trim), // lip on the void edge
            B(-17.84f, 20, 13.02f, -17.12f, 20.45f, 13.26f, Surf.Trim), // lip on the void edge
            B(7.47f, 20, 13.02f, 7.96f, 20.45f, 13.26f, Surf.Trim), // lip on the void edge
            B(-17.36f, 20, 13.26f, -16.64f, 20.45f, 13.5f, Surf.Trim), // lip on the void edge
            B(-16.88f, 20, 13.5f, -16.4f, 20.45f, 13.74f, Surf.Trim), // lip on the void edge
            B(-14.95f, 20, 14.71f, -14.71f, 20.45f, 15.19f, Surf.Trim), // lip on the void edge
            B(-14.47f, 20, 14.95f, 5.3f, 20.45f, 15.19f, Surf.Trim), // lip on the void edge
            B(17.36f, 20, 14.95f, 23.15f, 20.45f, 15.19f, Surf.Trim), // lip on the void edge
            // --- props (outlines from the minimap, heights estimated)
            B(-10.61f, 20, 6.99f, 1.21f, 21, 10.85f, Surf.Elevated), // B site platform (climb with a crouch-jump)
            B(-8.56f, 20, 10.79f, -1.69f, 20.5f, 12.06f, Surf.Elevated), // platform: lower step
            B(-16.1f, 20, 8.2f, -10.55f, 21, 10.01f, Surf.Elevated), // platform: walkway to the foot of B Tower
            B(-6.75f, 20, 3.01f, -4.7f, 22.2f, 7.05f, Surf.Cover), // tall cover on the platform's north side
            B(-8.62f, 20, 5.18f, -6.69f, 21.2f, 7.05f, Surf.Cover), // crate beside it
            B(-9.52f, 20, -8.44f, -7.35f, 21.6f, -6.27f, Surf.Cover), // Boxes: high crate
            B(-10.37f, 20, -6.33f, -8.2f, 21, -4.1f, Surf.Cover), // Boxes: low crate
            B(-1.69f, 20, -5.18f, 0.42f, 21.4f, -3.01f, Surf.Cover), // crate on the corner at the B Main exit
            B(21.22f, 20, 4.34f, 23.21f, 21, 6.15f, Surf.Cover), // crate under B Nest
            B(24.35f, 20, -19.77f, 28.21f, 22, -15.79f, Surf.Cover), // big crate in the lobby corner
            B(40.87f, 20, -16.16f, 44.13f, 21.4f, -12.78f, Surf.Cover), // lobby crate
        },
        Enemies = new[]
        {
            E("Close left", -2.89f, 20, -3.86f, Stance.Crouch),
            E("Boxes", -11.45f, 20, -5.3f, Stance.Crouch),
            E("Cubby", -3.62f, 20, 5.3f, Stance.Crouch),
            E("Platform", -3.74f, 21, 8.92f),
            E("Back site", -12.18f, 20, 13.02f),
            E("B Tower", -17.48f, 22.8f, -1.21f),
            E("Tower corner", -21.22f, 22.8f, -6.27f, Stance.Crouch),
            E("B Link", -2.53f, 20, -12.9f),
            E("Ramp", -22.79f, 21.75f, -13.5f),
        },
        Flashes = new[]
        {
            F("Pop over the B Main exit", V(1.69f, 21.6f, -0.48f), V(-2.53f, 23.2f, -1.45f)),
            F("Lob over the platform", V(1.57f, 21.6f, -1.45f), V(-5.55f, 24.6f, 2.77f)),
            F("Deep pop toward B Tower", V(1.69f, 21.6f, 0.48f), V(-10.97f, 25, 2.77f)),
            F("Curve right toward Boxes", V(1.45f, 21.6f, -0.24f), V(-8.56f, 24.2f, -3.62f)),
        },
        // Standard B execute: B Link (the Mid / defender side), B Tower, and Back site behind the platform.
        Smokes = new[]
        {
            new SmokeSpot("B Link", V(-2.53f, 20, -11.09f)),
            new SmokeSpot("B Tower", V(-17.6f, 22.8f, -0.24f)),
            new SmokeSpot("Back site", V(-6.75f, 20, 13.62f)),
        },
        // Spike: in front of the platform, Default by Boxes, back site on the edge.
        PlantSpots = new[] { V(-4.34f, 20, 2.17f), V(-12.78f, 20, 2.77f), V(-0.72f, 20, 13.02f) },
        // Retakes: B Link from Mid, the bottom of the B Tower ramp, and the top of B Tower.
        RetakeEntries = new[] { V(-2.53f, 20, -20.5f), V(-22.79f, 20, -20.5f), V(-21.22f, 22.8f, -8.08f) },
    };

    /// <summary>Ancient fortress over a chasm: grey stone with moss, dark teal metal, an overcast sky, cold light and fog,
    /// no buildings around (only distant mountains), and a near-black ground far below.</summary>
    static World.Theme AbyssLook(MapPalette p) => new()
    {
        Key = "abyss", Style = World.DecorStyle.Ascent, Hdri = World.Hdris.Overcast, SkyYaw = -60,
        SkyTint = new Color(0.9f, 0.96f, 1.04f), SkySaturation = 0.8f,
        SunColor = new Color(0.92f, 0.96f, 1f), SunEnergy = 1.5f, Ambient = 1.05f, Exposure = 1.0f,
        Saturation = 1.0f, Contrast = 1.08f, FogDensity = 0.006f, Ground = C(18, 22, 26),
        Floor = new World.Surface("stone_tiles_03", p.Floor, 0.85f, 2.4f, 0.9f, Normal: 1.1f, Macro: 0.16f, Grime: 0.2f),
        Wall = new World.Surface("concrete_wall_008", p.Wall, 0.95f, 2.6f, 1.1f, Normal: 1.2f, Macro: 0.14f, Grime: 0.3f),
        Base = new World.Surface("cobblestone_floor_06", C(92, 98, 88), 0.85f, 2.2f, 1f, Grime: 0.3f),
        Cap = new World.Surface("metal_plate_02", p.Trim, 0.85f, 1.8f, 0.6f, Metal: 0.6f, Grime: 0.05f),
        Trim = new World.Surface("stone_tiles_03", C(120, 123, 120), 0.85f, 1.6f, 0.9f, Grime: 0.2f),
        Elevated = new World.Surface("stone_tiles_03", p.Elevated, 0.85f, 2f, 0.9f, Grime: 0.15f),
        Cover = new World.Surface("metal_plate_02", p.Cover, 0.85f, 1.6f, 0.8f, Metal: 0.4f, Grime: 0.15f),
        CoverFrame = new World.Surface("metal_plate_02", p.Trim, 0.85f, 1.6f, 0.6f, Metal: 0.6f, Grime: 0f),
        GroundSurf = new World.Surface("cobblestone_floor_06", C(26, 30, 34), 1f, 6f, 0.4f, Grime: 0f),
        Backdrop = new World.Surface("concrete_wall_008", Colors.White, 0.9f, 2.6f, 1f, Macro: 0.12f),
        Roof = new World.Surface("metal_plate_02", C(70, 92, 98), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        Hues = new[] { C(150, 154, 150), C(132, 138, 134) },
        Buildings = false, Mountains = true, MountainColor = new Color(0.3f, 0.34f, 0.36f),
    };

}
