using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Split, A Main → A Site, true scale (post-6.0 layout). Walls are traced from the official minimap through the
    /// valorant-api.com transform (1 unit = 1 cm); heights follow the in-game callout markers and reference screenshots.
    /// Authored directly in the game's own (unmirrored) frame, so no Mirrored(): origin = the A Main exit,
    /// -Z = west (into the site, towards Screens), +X = north (the sunken plant area is on your right, Rafters and
    /// Heaven on your left, A Ramps behind-left).
    /// Levels: A Site plant area and A Back 0 m; the walkway along the site (A Main, Hell, Elbow, Screens) 1.4 m;
    /// Rafters ledge and the Heaven window bay 4.0 m; Heaven / A Tower 4.8 m, reached by A Ramps from A Main.
    /// Diagonal walls (the sloped back-site wall, the A Main corner, the Heaven wedge) are 1 m saw-tooth steps.
    /// </summary>
    public static readonly MapSpot Split = new MapSpot
    {
        Key = "split", Map = "Split", Name = "A Main → A Site",
        Drill = "Exit A Main (Ramps up to Heaven on your left) and clear Hell, Rafters, Heaven, Elbow, Screens, then the site below: Default, Terminal, Back Site, A Back",
        StartFeet = V(9.39f, 1.4f, 17.53f), StartYaw = -90, Choke = V(0f, 1.4f, 0f),
        Palette = new MapPalette(C(86, 87, 86), C(198, 190, 178), C(92, 118, 124), C(168, 128, 86), C(120, 112, 104), C(150, 182, 210)),
        Boxes = new[]
        {
            // --- outer walls and building blocks (traced from the minimap; diagonals as 1 m saw-tooth steps)
            B(-22.16f, 0f, -33.68f, 22.91f, 8.5f, -32.68f), B(8.14f, 0f, -32.68f, 22.91f, 8.5f, -22.91f),
            B(15.15f, 0f, -22.91f, 22.91f, 8.5f, -18.4f), B(22.41f, 0f, -18.4f, 22.91f, 8.5f, 24.92f),
            B(21.91f, 0f, -13.9f, 22.41f, 8.5f, 0.88f), B(20.41f, 0f, 5.13f, 22.41f, 8.5f, 24.92f),
            B(13.9f, 0f, 5.38f, 20.41f, 8.5f, 24.92f), B(3.13f, 0f, 4.88f, 19.41f, 8.5f, 5.38f),
            B(3.13f, 0f, 3.88f, 18.4f, 8.5f, 4.88f), B(3.13f, 0f, 2.88f, 17.4f, 8.5f, 3.88f),
            B(3.13f, 0f, 1.88f, 15.4f, 8.5f, 2.88f), B(3.13f, 0f, 0.88f, 14.4f, 8.5f, 1.88f),
            B(3.13f, 0f, 5.38f, 13.9f, 8.5f, 8.39f), B(-22.16f, 0f, 24.16f, 13.9f, 8.5f, 24.92f),
            B(3.13f, 0f, -0.13f, 13.4f, 8.5f, 0.88f), B(3.63f, 0f, -19.16f, 11.89f, 8.5f, -12.9f),
            B(3.88f, 0f, -19.41f, 11.64f, 8.5f, -19.16f), B(3.13f, 0f, -0.38f, 11.64f, 8.5f, -0.13f),
            B(3.38f, 0f, -25.42f, 8.14f, 8.5f, -22.91f), B(-22.16f, 0f, 23.16f, 6.64f, 8.5f, 24.16f),
            B(4.13f, 0f, 19.16f, 6.13f, 8.5f, 23.16f), B(3.13f, 0f, 8.39f, 5.88f, 8.5f, 12.4f),
            B(3.13f, 0f, 20.16f, 4.13f, 8.5f, 23.16f), B(2.88f, 0f, -25.42f, 3.38f, 8.5f, -24.67f),
            B(2.13f, 0f, 21.16f, 3.13f, 8.5f, 23.16f), B(2.63f, 0f, -25.42f, 2.88f, 8.5f, -24.92f),
            B(1.13f, 0f, 22.16f, 2.13f, 8.5f, 23.16f), B(-22.16f, 0f, 22.66f, 0.63f, 8.5f, 23.16f),
            B(-7.89f, 0f, -25.42f, -0.88f, 8.5f, -24.92f), B(-7.89f, 0f, -24.92f, -1.13f, 8.5f, -24.67f),
            B(-11.39f, 0f, 3.63f, -1.38f, 8.5f, 7.14f), B(-7.89f, 0f, -24.67f, -1.63f, 8.5f, -21.16f),
            B(-22.16f, 0f, -21.16f, -2.13f, 8.5f, -19.41f), B(-8.89f, 0f, -19.41f, -2.13f, 8.5f, -19.16f),
            B(-8.89f, 0f, -19.16f, -5.38f, 8.5f, -13.9f), B(-8.64f, 0f, -13.9f, -5.38f, 8.5f, -13.65f),
            B(-12.65f, 0f, 7.14f, -5.38f, 8.5f, 11.64f), B(-9.89f, 0f, 16.15f, -5.38f, 8.5f, 22.66f),
            B(-11.39f, 0f, -2.88f, -7.14f, 8.5f, 3.63f), B(-7.89f, 0f, -28.17f, -7.39f, 8.5f, -25.42f),
            B(-8.64f, 0f, -7.64f, -7.64f, 8.5f, -2.88f), B(-22.16f, 0f, -21.41f, -7.89f, 8.5f, -21.16f),
            B(-10.64f, 0f, -6.64f, -8.64f, 8.5f, -2.88f), B(-10.14f, 0f, 16.4f, -9.89f, 8.5f, 22.66f),
            B(-17.65f, 0f, 20.16f, -10.14f, 8.5f, 22.66f), B(-11.64f, 0f, -4.63f, -10.64f, 8.5f, -2.88f),
            B(-12.14f, 0f, -2.88f, -11.39f, 8.5f, -0.38f), B(-12.4f, 0f, 4.88f, -11.39f, 8.5f, 7.14f),
            B(-12.4f, 0f, -2.63f, -12.14f, 8.5f, -0.38f), B(-12.65f, 0f, -2.13f, -12.4f, 8.5f, -0.38f),
            B(-12.65f, 0f, 5.63f, -12.4f, 8.5f, 7.14f), B(-12.9f, 0f, -1.63f, -12.65f, 8.5f, -0.38f),
            B(-13.15f, 0f, -1.38f, -12.9f, 8.5f, -0.38f), B(-22.16f, 0f, -32.68f, -16.15f, 8.5f, -25.92f),
            B(-22.16f, 0f, 20.41f, -17.65f, 8.5f, 22.66f), B(-22.16f, 0f, 19.66f, -18.66f, 8.5f, 20.41f),
            B(-22.16f, 0f, 18.66f, -19.66f, 8.5f, 19.66f), B(-22.16f, 0f, -10.89f, -20.41f, 8.5f, -4.38f),
            B(-22.16f, 0f, 17.65f, -20.66f, 8.5f, 18.66f), B(-22.16f, 0f, -25.92f, -21.41f, 8.5f, -21.41f),
            B(-22.16f, 0f, -19.41f, -21.41f, 8.5f, -10.89f), B(-22.16f, 0f, -4.38f, -21.41f, 8.5f, 17.65f),
            // floor top 0.20 m
            B(7.89f, 0f, -12.9f, 8.39f, 0.2f, -0.38f, Surf.Floor),
            B(7.39f, 0f, -22.91f, 7.89f, 0.2f, -19.41f, Surf.Floor),
            // floor top 0.40 m
            B(7.39f, 0f, -12.9f, 7.89f, 0.4f, -0.38f, Surf.Floor),
            B(6.89f, 0f, -22.91f, 7.39f, 0.4f, -19.41f, Surf.Floor),
            // floor top 0.60 m
            B(6.89f, 0f, -12.9f, 7.39f, 0.6f, -0.38f, Surf.Floor),
            B(6.39f, 0f, -22.91f, 6.89f, 0.6f, -19.41f, Surf.Floor),
            // floor top 0.80 m
            B(6.39f, 0f, -12.9f, 6.89f, 0.8f, -0.38f, Surf.Floor),
            B(5.88f, 0f, -22.91f, 6.39f, 0.8f, -19.41f, Surf.Floor),
            // floor top 1.00 m
            B(5.88f, 0f, -12.9f, 6.39f, 1f, -0.38f, Surf.Floor),
            B(5.38f, 0f, -22.91f, 5.88f, 1f, -19.41f, Surf.Floor),
            // floor top 1.20 m
            B(5.38f, 0f, -12.9f, 5.88f, 1.2f, -0.38f, Surf.Floor),
            B(4.88f, 0f, -22.91f, 5.38f, 1.2f, -19.41f, Surf.Floor),
            // floor top 1.40 m
            B(-22.16f, 0f, 21.91f, 13.9f, 1.4f, 24.92f, Surf.Floor),
            B(-9.39f, 0f, 0.13f, 12.14f, 1.4f, 21.91f, Surf.Floor),
            B(-22.16f, 0f, -33.68f, 8.14f, 1.4f, -23.16f, Surf.Floor),
            B(-5.13f, 0f, -12.9f, 5.38f, 1.4f, -0.38f, Surf.Floor),
            B(-22.16f, 0f, -22.91f, 4.88f, 1.4f, -19.41f, Surf.Floor),
            B(-5.13f, 0f, -19.41f, 3.88f, 1.4f, -12.9f, Surf.Floor),
            B(-22.16f, 0f, -23.16f, 3.38f, 1.4f, -22.91f, Surf.Floor),
            B(-11.39f, 0f, -0.38f, 3.13f, 1.4f, 0.13f, Surf.Floor),
            B(-9.14f, 0f, -21.41f, -5.13f, 1.4f, -13.9f, Surf.Floor),
            B(-11.64f, 0f, -3.13f, -5.13f, 1.4f, -0.38f, Surf.Floor),
            B(-11.39f, 0f, 11.89f, -9.39f, 1.4f, 16.15f, Surf.Floor),
            B(-22.16f, 0f, 13.9f, -11.39f, 1.4f, 16.15f, Surf.Floor),
            B(-22.16f, 0f, 16.15f, -12.65f, 1.4f, 24.92f, Surf.Floor),
            // floor top 1.60 m
            B(12.14f, 0f, 19.66f, 13.9f, 1.6f, 21.91f, Surf.Floor),
            B(-12.65f, 0f, 16.15f, -12.14f, 1.6f, 20.16f, Surf.Floor),
            // floor top 1.68 m
            B(-21.41f, 0f, 12.65f, -11.39f, 1.68f, 13.9f, Surf.Elevated),
            // floor top 1.80 m
            B(12.14f, 0f, 17.4f, 13.9f, 1.8f, 19.66f, Surf.Floor),
            B(-12.14f, 0f, 16.15f, -11.39f, 1.8f, 20.16f, Surf.Floor),
            // floor top 1.97 m
            B(-22.16f, 0f, 11.64f, -9.39f, 1.97f, 11.89f, Surf.Elevated),
            B(-21.41f, 0f, 11.89f, -11.39f, 1.97f, 12.65f, Surf.Elevated),
            // floor top 2.00 m
            B(12.14f, 0f, 15.15f, 13.9f, 2f, 17.4f, Surf.Floor),
            B(-11.39f, 0f, 16.15f, -10.89f, 2f, 20.16f, Surf.Floor),
            // floor top 2.20 m
            B(12.14f, 0f, 12.9f, 13.9f, 2.2f, 15.15f, Surf.Floor),
            B(-10.89f, 0f, 16.15f, -9.89f, 2.2f, 20.16f, Surf.Floor),
            // floor top 2.25 m
            B(-21.41f, 0f, 10.39f, -12.65f, 2.25f, 11.64f, Surf.Elevated),
            // floor top 2.40 m
            B(12.14f, 0f, 10.64f, 13.9f, 2.4f, 12.9f, Surf.Floor),
            // floor top 2.53 m
            B(-21.41f, 0f, 9.39f, -12.65f, 2.53f, 10.39f, Surf.Elevated),
            // floor top 2.60 m
            B(12.14f, 0f, 8.39f, 13.9f, 2.6f, 10.64f, Surf.Floor),
            // floor top 2.82 m
            B(-21.41f, 0f, 8.14f, -12.65f, 2.82f, 9.39f, Surf.Elevated),
            // floor top 3.10 m
            B(-21.41f, 0f, 6.89f, -12.65f, 3.1f, 8.14f, Surf.Elevated),
            // floor top 3.38 m
            B(-21.41f, 0f, 5.88f, -12.65f, 3.38f, 6.89f, Surf.Elevated),
            // floor top 3.67 m
            B(-22.16f, 0f, 4.63f, -11.39f, 3.67f, 5.63f, Surf.Elevated),
            B(-21.41f, 0f, 5.63f, -12.65f, 3.67f, 5.88f, Surf.Elevated),
            // floor top 3.95 m
            B(-21.41f, 0f, 3.63f, -11.39f, 3.95f, 4.63f, Surf.Elevated),
            // floor top 4.00 m
            B(-5.38f, 0f, -13.9f, -5.13f, 4f, -3.13f),
            B(-10.89f, 0f, -13.65f, -5.38f, 4f, -3.38f),
            B(-7.64f, 0f, -3.38f, -5.38f, 4f, -3.13f),
            B(-10.89f, 0f, -13.9f, -8.64f, 4f, -13.65f),
            B(-10.89f, 0f, -19.41f, -9.14f, 4f, -13.9f),
            // floor top 4.20 m
            B(-11.39f, 0f, -19.41f, -10.89f, 4.2f, -4.63f, Surf.Elevated),
            // floor top 4.23 m
            B(-21.41f, 0f, 2.38f, -11.39f, 4.23f, 3.63f, Surf.Elevated),
            // floor top 4.40 m
            B(-11.89f, 0f, -19.41f, -11.39f, 4.4f, -4.63f, Surf.Elevated),
            B(-11.89f, 0f, -4.38f, -11.39f, 4.4f, 0.13f, Surf.Elevated),
            B(-11.89f, 0f, -4.63f, -11.64f, 4.4f, -4.38f, Surf.Elevated),
            // floor top 4.52 m
            B(-21.41f, 0f, 1.38f, -11.39f, 4.52f, 2.38f, Surf.Elevated),
            // floor top 4.60 m
            B(-12.4f, 0f, -19.41f, -11.89f, 4.6f, -2.88f, Surf.Elevated),
            B(-12.4f, 0f, -0.38f, -11.89f, 4.6f, 0.13f, Surf.Elevated),
            B(-12.4f, 0f, -2.88f, -12.14f, 4.6f, -0.38f, Surf.Elevated),
            // floor top 4.80 m
            B(-21.41f, 0f, 0.13f, -11.39f, 4.8f, 1.38f, Surf.Elevated),
            B(-22.16f, 0f, -19.41f, -12.4f, 4.8f, -0.63f, Surf.Elevated),
            B(-22.16f, 0f, -0.38f, -12.4f, 4.8f, 0.13f, Surf.Elevated),
            B(-22.16f, 0f, -0.63f, -13.15f, 4.8f, -0.38f, Surf.Elevated),
            // --- props, cover, Heaven window
            B(14.52f, 0f, -8.51f, 16.4f, 2.5f, -4.63f, Surf.Trim),  // Terminal (holo map box) - Default plant
            B(10.02f, 0f, -12.77f, 12.02f, 2.4f, -10.64f),  // white ivy pillar by the K-building door
            B(-5.13f, 1.35f, -5.63f, -3.13f, 3f, -3.63f),  // flowerpot under Rafters (raised in 6.0)
            B(-5.26f, 1.35f, 7.26f, -3.13f, 2.55f, 11.27f, Surf.Cover),  // A Main boost box (halved in 6.0)
            B(-21.28f, 3.3f, 6.39f, -18.78f, 5f, 8.39f, Surf.Cover),  // Ramps crate (tall)
            B(-21.28f, 3.6f, 5.38f, -19.78f, 4.6f, 6.39f, Surf.Cover),  // Ramps crate (small)
            B(-20.16f, 4.75f, -11.14f, -18.28f, 6.3f, -9.64f, Surf.Cover),  // Heaven crate
            B(-20.16f, 4.75f, -9.64f, -18.53f, 5.8f, -8.14f, Surf.Cover),  // Heaven crate (low)
            B(-9.27f, 0f, -13.65f, -8.76f, 8.5f, -12.27f),  // wall stub west of the Heaven window
            B(-9.27f, 6.1f, -12.27f, -8.76f, 8.5f, -9.27f, Surf.Trim),  // lintel over the Heaven window
            B(7.76f, -0.1f, -23.04f, 22.41f, 0f, 5.26f, Surf.Floor),  // site floor slab (A Site / A Back level 0)
            B(-21.53f, 0f, -0.63f, -17.65f, 8.5f, -0.19f),  // thin wall between Heaven and the top of Ramps
        },
        Enemies = new[]
        {
            E("Default", 14.15f, 0f, -9.64f, Stance.Crouch),
            E("Terminal", 14.02f, 0f, -2.88f),
            E("Site", 9.14f, 0f, -10.39f),
            E("Back Site", 21.03f, 0f, 3.76f, Stance.Crouch),
            E("A Back", 13.65f, 0f, -21.16f),
            E("Elbow", 2.13f, 1.4f, -20.91f),
            E("Screens", 1.38f, 1.4f, -28.42f),
            E("Hell", -5.13f, 1.4f, -1.38f, Stance.Crouch),
            E("Rafters", -6.01f, 4f, -7.76f),
            E("Heaven", -10.14f, 4f, -10.77f),
        },
        Flashes = new[]
        {
            F("Main pop over the exit", V(0.38f, 2.9f, 6.89f), V(3.63f, 5f, -1.63f)),
            F("Over the wall onto site", V(1.25f, 2.9f, 9.39f), V(9.39f, 4.6f, -2.88f)),
            F("Heaven / Rafters flash", V(-0.38f, 2.9f, 5.63f), V(-2.5f, 5.8f, -6.26f)),
            F("Back site flash", V(1.63f, 2.9f, 7.51f), V(15.15f, 3.8f, -0.38f)),
        },
        // Standard A Main execute: Heaven, Screens (it also covers the Elbow peek), then Rafters.
        Smokes = new[]
        {
            new SmokeSpot("Heaven", V(-10.1f, 4f, -10.6f)),
            new SmokeSpot("Screens", V(1.8f, 1.4f, -24.6f)),
            new SmokeSpot("Rafters", V(-6.2f, 4f, -6.4f)),
        },
        // Spike: default by the box south of the holo map, open site, back site.
        PlantSpots = new[] { V(15.4f, 0, -9.7f), V(9.8f, 0, -6.5f), V(19.6f, 0, 1.5f) },
        // Retakes: deep Screens, Screens from the west, and A Back from defender spawn.
        RetakeEntries = new[] { V(1.5f, 1.4f, -30.5f), V(-14f, 1.4f, -24.5f), V(9.5f, 0, -21.5f) },
    };
}
