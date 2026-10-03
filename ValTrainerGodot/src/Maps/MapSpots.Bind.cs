using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Bind, B Short / Hookah into B Site, at true scale. Wall faces are traced from the official minimap
    /// (valorant-api.com, 1 px = 16.55 cm) and line up with it to within ~0.3 m; heights are estimates.
    /// Authored directly in the in-game frame (no Mirrored()): origin = centre of the Hookah window,
    /// -Z = north (toward B Hall), +X = east (toward the Hookah corridor and B Short).
    /// Attackers come up B Short, turn west into Hookah, climb its stairs to the window platform (1.6 m)
    /// and drop through the window onto site. Elevator sits mid-site, Cubby in the east wall, B Hall
    /// behind the divider wall to the north, Elbow and the Elbow arch to the west, Garden and the top of
    /// B Long (with its teleporter) to the south-west.
    /// </summary>
    public static readonly MapSpot Bind = new MapSpot
    {
        Key = "bind", Map = "Bind", Name = "Hookah → B Site",
        Drill = "Climb Hookah, swing the window and drop in: clear Under Window, Elevator, Cubby, Hall, Back Site, Elbow and Garden",
        StartFeet = V(14, 0, 14), StartYaw = -90, Choke = V(0, 1.6f, 0),
        Palette = new MapPalette(C(178, 150, 112), C(222, 190, 142), C(178, 98, 62), C(170, 124, 82), C(44, 128, 134), C(165, 195, 220)),
        Boxes = new[]
        {
            // Ground
            B(-33.4f, -0.1f, -31.4f, 31, 0, 27.8f, Surf.Floor),
            // B Hall: north wall, west wall, door frame toward Defender Spawn, closed beyond
            B(-20.2f, 0, -31.4f, 12.5f, 6.5f, -30.1f), B(-20.2f, 0, -31.4f, -19, 6.5f, -19), B(8.1f, 0, -30.1f, 9.1f, 6.5f, -29.45f),
            B(8.1f, 0, -25, 9.1f, 6.5f, -24.15f), B(12.5f, 0, -31.4f, 13.3f, 6.5f, -24.15f),
            // Site/Hall divider (thin wall), its leg down to the Elbow block, lintel over the Hall doorway
            B(-15.05f, 0, -24.15f, 2.3f, 6, -23.2f), B(-15.05f, 0, -24.15f, -13.9f, 6, -13.4f), B(2.3f, 3.6f, -24.15f, 5.8f, 6, -23.2f, Surf.Trim),
            // Upper Elbow north wall, Elbow west wall
            B(-30.4f, 0, -20.2f, -19, 6.5f, -19), B(-30.4f, 0, -20.2f, -29.3f, 6.5f, -4.15f),
            // Elbow block (between Elbow and site) and the Elbow archway
            B(-23, 0, -13.4f, -13.9f, 6, -8.95f), B(-29.3f, 0, -13.5f, -28.2f, 3.6f, -12.8f, Surf.Trim),
            B(-24.4f, 0, -13.5f, -23, 3.6f, -12.8f, Surf.Trim), B(-28.2f, 2.9f, -13.5f, -24.4f, 3.6f, -12.8f, Surf.Trim),
            // South of lower Elbow: solid block to B Long top
            B(-30.4f, 0, -4.15f, -20.35f, 6.5f, 6.6f),
            // East of site: Hall doorway jamb block, Cubby back wall, Hookah-side wall
            B(5.8f, 0, -24.15f, 12.5f, 6.5f, -17.2f), B(8.1f, 0, -17.2f, 12.5f, 6.5f, -9.95f), B(4.15f, 0, -9.95f, 12.5f, 6.5f, 0.35f),
            // Hookah facade (north wall, split so no fake windows are hung next to the real one) with the window: sill + tiled cap (2.25 m), lintel
            B(-6, 0, -0.35f, -4, 5.5f, 0.35f), B(-4, 0, -0.35f, -2, 5.5f, 0.35f), B(2, 0, -0.35f, 4.15f, 5.5f, 0.35f),
            B(-2, 0, -0.35f, 2, 2.05f, 0.35f), B(-2.05f, 2.05f, -0.4f, 2.05f, 2.25f, 0.4f, Surf.Trim),
            B(-2, 4.3f, -0.35f, 2, 5.5f, 0.35f, Surf.Trim),
            // Diagonal wall: site front -> Garden entrance (stepped)
            B(-6.15f, 0, -0.35f, -3.95f, 5.5f, 0.4f), B(-6.66f, 0, 0.4f, -3.95f, 5.5f, 1.15f), B(-7.17f, 0, 1.15f, -3.95f, 5.5f, 1.9f),
            B(-7.68f, 0, 1.9f, -3.95f, 5.5f, 2.65f), B(-8.19f, 0, 2.65f, -3.95f, 5.5f, 3.4f), B(-8.7f, 0, 3.4f, -3.95f, 5.5f, 4.15f),
            // Garden entrance east jamb, Garden/Hookah thick wall, Hookah pillar, back wall
            B(-9.95f, 0, 4.15f, -3.95f, 5.5f, 4.95f), B(-7.1f, 0, 4.95f, -3.95f, 5.5f, 22), B(-3.95f, 0, 8.6f, -2, 5.5f, 12.6f),
            B(-3.95f, 0, 21, 4.15f, 5.5f, 22),
            // Hookah east wall south of the doorway, corridor north wall
            B(4.15f, 0, 15.05f, 8.75f, 5.5f, 25), B(8.75f, 0, 15.05f, 11.1f, 5.5f, 15.9f), B(4.15f, 0, 0.35f, 11.1f, 5.5f, 10.9f),
            B(11.1f, 0, 0.35f, 12.5f, 5.5f, 9.1f),
            // Hookah roof over the back room (stairs and window platform left open for light)
            B(-3.95f, 4.8f, 8.6f, 4.15f, 5.1f, 21),
            // Hookah stairs up to the window platform (1.6 m)
            B(-3.95f, 0, 0.35f, 4.15f, 0.267f, 12.2f, Surf.Floor), B(-3.95f, 0, 0.35f, 4.15f, 0.533f, 10.6f, Surf.Floor),
            B(-3.95f, 0, 0.35f, 4.15f, 0.8f, 9, Surf.Floor), B(-3.95f, 0, 0.35f, 4.15f, 1.067f, 7.4f, Surf.Floor),
            B(-3.95f, 0, 0.35f, 4.15f, 1.333f, 5.8f, Surf.Floor), B(-3.95f, 0, 0.35f, 4.15f, 1.6f, 4.2f, Surf.Floor),
            // Hookah cover: crate stack by the stairs, back-corner crate
            B(1.7f, 0, 5.2f, 4.15f, 2.6f, 8, Surf.Cover), B(-3.95f, 0, 18.4f, -1.8f, 1.1f, 21, Surf.Cover),
            // Dome wall over the B Short landing, TP-room jamb
            B(12.5f, 0, -17.2f, 31, 6.5f, 7.7f), B(12.5f, 0, 7.7f, 13.9f, 5.5f, 8.2f), B(16.2f, 0, 7.7f, 17.6f, 5.5f, 8.2f),
            B(17.6f, 0, 7.7f, 19.05f, 5.5f, 9.1f), B(19.05f, 0, 7.7f, 19.7f, 5.5f, 10.75f),
            // Teleporter exit room (A Short -> B): walls with cut corners, exit pad
            B(19.7f, 0, 7.7f, 31, 5.5f, 7.95f), B(27.2f, 0, 7.95f, 31, 5.5f, 9.5f), B(28.7f, 0, 9.5f, 31, 5.5f, 11.1f),
            B(30.3f, 0, 11.1f, 31, 5.5f, 14.9f), B(28.6f, 0, 14.9f, 31, 5.5f, 16.6f), B(27, 0, 16.6f, 31, 5.5f, 18.2f),
            B(19.05f, 0, 15.55f, 19.7f, 5.5f, 18.2f), B(19.05f, 0, 18.2f, 31, 5.5f, 27.8f), B(28.9f, 0, 11.6f, 30.3f, 0.12f, 14.4f, Surf.Elevated),
            // B Short: west wall, closed off below the landing, crate by the TP-room wall
            B(3.2f, 0, 25, 8.75f, 5.5f, 27.8f), B(3.2f, 0, 26, 19.05f, 5.5f, 27.8f), B(17.2f, 0, 17.2f, 19.05f, 1.1f, 18.7f, Surf.Cover),
            // Garden north wall (site-front side) and the block behind it, arch over the Garden entrance
            B(-20.35f, 0, 4.3f, -14.55f, 5, 4.95f), B(-20.35f, 0, 4.95f, -17.05f, 5, 6.6f), B(-14.55f, 3.4f, 4.3f, -9.95f, 5, 4.95f, Surf.Trim),
            // Garden west pillars and arch (B Long opening), cut corners, south block
            B(-18.05f, 0, 6.6f, -17.05f, 5, 7.95f), B(-18.05f, 0, 11.9f, -17.05f, 5, 13.1f), B(-18.05f, 0, 13.1f, -16.1f, 5, 15),
            B(-16.1f, 0, 14.05f, -15.2f, 5, 15), B(-8, 0, 13.1f, -7.1f, 5, 15), B(-8.95f, 0, 14.05f, -8, 5, 15), B(-18.05f, 0, 15, -7.1f, 5, 27.8f),
            B(-18.05f, 3.4f, 7.95f, -17.05f, 5, 11.9f, Surf.Trim),
            // B Long top: west wall with the teleporter alcove, south wall, B Long walls, closed below
            B(-30.4f, 0, 6.6f, -29.3f, 6.5f, 9.6f), B(-30.4f, 0, 13.4f, -29.3f, 6.5f, 16.05f), B(-33.4f, 0, 8.8f, -29.3f, 6.5f, 9.6f),
            B(-33.4f, 0, 13.4f, -30.4f, 6.5f, 14.2f), B(-33.4f, 0, 9.6f, -32.9f, 6.5f, 13.4f),
            B(-32.9f, 0, 9.6f, -30.4f, 0.12f, 13.4f, Surf.Elevated), B(-30.4f, 0, 16.05f, -24.15f, 6.5f, 17), B(-25.2f, 0, 17, -24.15f, 6.5f, 27.8f),
            B(-25.2f, 0, 26.8f, -18.05f, 6.5f, 27.8f),
            // B Site: Elevator (container) with its low box (south, plant side) and the north box
            B(-9.15f, 0, -15.4f, 0.25f, 2.4f, -11.5f, Surf.Cover), B(-6.9f, 0, -11.5f, -2.1f, 1.1f, -9.6f, Surf.Cover),
            B(-2.15f, 0, -17.45f, 0, 1.25f, -15.4f, Surf.Cover),
            // Site corner box (Elbow side), box under Hookah window, box at the Garden wall
            B(-13.9f, 0, -11.05f, -12, 1.1f, -8.95f, Surf.Cover), B(2.15f, 0, -2.15f, 4.15f, 1.05f, -0.35f, Surf.Cover),
            B(-20.35f, 0, -4, -18.1f, 1.1f, 0, Surf.Cover),
        },
        Enemies = new[]
        {
            E("Cubby", 5, 0, -14.5f, Stance.Crouch),
            E("Elevator", 1.3f, 0, -14),
            E("Back Elevator", -4.5f, 0, -17.6f),
            E("Hall Door", 4, 0, -22.7f),
            E("Hall", -6, 0, -27.5f),
            E("Elbow", -24, 0, -7.5f),
            E("Back Site", -13, 0, -12.4f, Stance.Crouch),
            E("Garden", -12.5f, 0, 9),
            E("Under Window", 3.2f, 0, -3.4f, Stance.Crouch),
            E("Elbow (deep)", -22, 0, -16.5f),
        },
        Flashes = new[]
        {
            F("Hookah window pop", V(0.5f, 3.1f, 3), V(0, 3.4f, -3.5f)),
            F("Window curve left", V(1.5f, 3.1f, 2.5f), V(-4.5f, 3.2f, -4)),
            F("Window curve right", V(-1.5f, 3.1f, 2.5f), V(3, 3.4f, -5)),
            F("High pop out of the window", V(0, 3.1f, 1.5f), V(-2, 6, -7)),
            F("Deep pop over Elevator", V(1, 3.1f, 1.5f), V(-3, 5.2f, -10)),
        },
        // Standard Hookah execute: Elbow (where it opens onto site) and Hall (the doorway to defender spawn), then Garden.
        Smokes = new[]
        {
            new SmokeSpot("Elbow", V(-21.6f, 0, -6.6f)),
            new SmokeSpot("Hall", V(3.8f, 0, -21.6f)),
            new SmokeSpot("Garden", V(-12.2f, 0, 6.8f)),
        },
        // Spike: default by the low box in front of Elevator, behind Elevator (Hall side), open under the Hookah window.
        PlantSpots = new[] { V(-4.5f, 0, -8.6f), V(-6.5f, 0, -17.3f), V(-1f, 0, -6f) },
        // Retakes: the B Hall door from defender spawn, upper Elbow, and the top of B Long (via Garden).
        RetakeEntries = new[] { V(11.8f, 0, -25.6f), V(-26f, 0, -17f), V(-26.5f, 0, 12f) },
    };
}
