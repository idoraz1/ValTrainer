using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Ascent, A Main → A Site, at true scale. Traced from the official minimap (valorant-api.com, 1 px = 0.14 m) and
    /// checked against in-game screenshots; heights from the callout elevations (Main/Tree 0, site +1 m, Heaven +4 m).
    /// Authored directly in the game's frame (no mirroring): north = -Z, east = +X, origin = the site-side mouth of the
    /// A Main arch (A Peek). You walk up A Main (Wine straight ahead), turn left through the arch onto the cobbled
    /// main side of the site, and climb the stairs between two grass slopes to the tiled site: Generator on the left,
    /// Tetris in the middle, the tall Pillar crate on the right, Heaven (A Rafters) along the back over Hell, the
    /// A door to Tree on the left. Heaven is reached by the Heaven stairs from the Garden corridor (Tree → Garden).
    /// </summary>
    public static readonly MapSpot Ascent = new MapSpot
    {
        Key = "ascent", Map = "Ascent", Name = "A Main → A Site",
        Drill = "Clear Wine, Boxes, A Switch/Tree, Generator, Tetris, Pillar, Back Site, Hell, Heaven and Heaven Pillar",
        StartFeet = V(8.02f, 0, 18.76f), StartYaw = 0, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(124, 114, 102), C(208, 184, 150), C(172, 96, 66), C(204, 170, 124), C(180, 172, 160), C(160, 190, 220)),
        Boxes = new[]
        {
            B(-42.76f, -0.1f, -14.86f, 14.72f, 0, 32.3f, Surf.Floor), // ground (cobbles)
            B(-28.6f, 0, -9.77f, -9.77f, 1, 9.91f, Surf.Elevated), // A Site floor (+1 m)
            B(-10.74f, 1, -9.77f, -9.77f, 1.3f, -2.37f, Surf.Trim), // curb, north planter
            B(-10.74f, 1, 2.93f, -9.77f, 1.3f, 9.91f, Surf.Trim), // curb, south planter
            B(-9.77f, 0, -2.37f, -9.11f, 0.86f, 2.93f, Surf.Elevated), // site stairs
            B(-9.11f, 0, -2.37f, -8.46f, 0.71f, 2.93f, Surf.Elevated),
            B(-8.46f, 0, -2.37f, -7.81f, 0.57f, 2.93f, Surf.Elevated),
            B(-7.81f, 0, -2.37f, -7.16f, 0.43f, 2.93f, Surf.Elevated),
            B(-7.16f, 0, -2.37f, -6.51f, 0.29f, 2.93f, Surf.Elevated),
            B(-6.51f, 0, -2.37f, -5.86f, 0.14f, 2.93f, Surf.Elevated),
            B(-9.77f, -0.1f, -9.77f, -8.46f, 0.75f, -2.37f, Surf.Floor), // north grass slope
            B(-8.46f, -0.1f, -9.77f, -7.16f, 0.5f, -2.37f, Surf.Floor),
            B(-7.16f, -0.1f, -9.77f, -5.86f, 0.25f, -2.37f, Surf.Floor),
            B(-9.77f, -0.1f, 2.93f, -8.46f, 0.75f, 9.91f, Surf.Floor), // south grass slope
            B(-8.46f, -0.1f, 2.93f, -7.16f, 0.5f, 9.91f, Surf.Floor),
            B(-7.16f, -0.1f, 2.93f, -5.86f, 0.25f, 9.91f, Surf.Floor),
            B(-18.69f, 1, 5.02f, -12.83f, 3.4f, 7.11f, Surf.Cover), // Generator
            B(-17.3f, 1, -4.88f, -15.35f, 2.2f, -2.79f, Surf.Cover), // Tetris, beige box
            B(-17.86f, 1, -2.79f, -15.9f, 2.5f, -1.12f, Surf.Cover), // Tetris, green box
            B(-12.83f, 1, -9.77f, -10.74f, 3.7f, -7.81f, Surf.Cover), // Pillar (tall crate)
            B(-22.74f, 1, -9.77f, -21.07f, 2.6f, -7.81f, Surf.Cover), // back-site crates
            B(-22.74f, 1, -7.81f, -22.04f, 2, -6.84f, Surf.Cover),
            B(-3.77f, 0, -9.77f, 0, 2.3f, -7.81f, Surf.Cover), // boxes by the arch (tall)
            B(-3.77f, 0, -7.81f, 0, 1.2f, -5.86f, Surf.Cover), // boxes by the arch (low)
            B(5.16f, 0, -14.23f, 6.14f, 1, -13.25f, Surf.Cover), // Wine barrels
            B(0, 4.5f, -2.37f, 2.58f, 6, 2.37f), // A Main arch (A Peek) vault, site side
            B(2.58f, 4.5f, -2.37f, 5.16f, 8.5f, 2.37f), // A Main arch (A Peek) vault, main side
            B(-6.7f, 3.2f, 11.72f, -3.07f, 7, 13.25f), // over the A door (Tree)
            B(-10.04f, 3.2f, 18, -8.79f, 7, 20.93f), // over the Tree-Garden door
            B(-8.79f, 0, 28.04f, -6.98f, 0.7f, 29.85f, Surf.Trim), // Tree planter
            B(-8.16f, 0.7f, 28.67f, -7.6f, 3.6f, 29.23f, Surf.Trim), // Tree trunk
            B(-20.79f, 0, 17.16f, -19.81f, 1.1f, 18.28f, Surf.Trim), // stone planter under Window
            B(-24.83f, 0, 25.95f, -22.88f, 1.3f, 27.9f, Surf.Cover), // crate
            B(-28.6f, 3.75f, -9.77f, -23.02f, 4.05f, 2.93f, Surf.Elevated), // Heaven floor
            B(-28.6f, 3.75f, 2.93f, -23.58f, 4.05f, 23.02f, Surf.Elevated), // Heaven floor, south walkway
            B(-40.74f, 3.75f, 11.16f, -28.6f, 4.05f, 15.9f, Surf.Elevated), // Heaven corridor to the stairs
            B(-40.74f, 3.75f, 15.9f, -35.85f, 4.05f, 16.88f, Surf.Elevated), // stairs landing
            B(-22.74f, 3.75f, 17.16f, -21.07f, 4.05f, 21.62f, Surf.Elevated), // Window room
            B(-23.58f, 3.75f, 18.55f, -22.74f, 4.05f, 20.37f, Surf.Elevated), // Window room door
            B(-28.6f, 1, -9.77f, -23.02f, 3.75f, -5.3f), // under Heaven (north)
            B(-28.6f, 1, -5.3f, -28.04f, 3.75f, 3.77f), // Hell back wall
            B(-23.44f, 1, -5.3f, -23.02f, 3.75f, -2.37f), // Hell front (north of the opening)
            B(-28.6f, 1, 3.77f, -23.58f, 3.75f, 9.91f), // under Heaven (south)
            B(-23.58f, 1, 2.93f, -22.74f, 6.6f, 9.91f), // scaffold / plywood wall behind Generator
            B(-28.6f, 0, 9.91f, -23.58f, 3.75f, 23.02f),
            B(-40.74f, 0, 11.16f, -28.6f, 3.75f, 15.9f),
            B(-40.74f, 0, 15.9f, -35.85f, 3.75f, 16.88f),
            B(-22.74f, 0, 17.16f, -21.07f, 3.75f, 21.62f),
            B(-23.58f, 0, 18.55f, -22.74f, 3.75f, 20.37f),
            B(-23.23f, 4.05f, -9.77f, -23.02f, 4.9f, 2.93f, Surf.Trim), // Heaven railing
            B(-25.81f, 4.05f, -9.77f, -23.72f, 6, -7.81f, Surf.Cover), // Heaven Pillar crate
            B(-21.07f, 4.05f, 17.16f, -20.79f, 7, 18.55f), // Window wall
            B(-21.07f, 4.05f, 19.95f, -20.79f, 7, 21.62f),
            B(-21.07f, 4.05f, 18.55f, -20.79f, 5, 19.95f), // window sill
            B(-21.07f, 6.2f, 18.55f, -20.79f, 7, 19.95f), // window head
            B(-22.74f, 4.05f, 21.62f, -20.79f, 7, 21.9f),
            B(-40.74f, 0, 16.88f, -35.85f, 3.76f, 17.58f, Surf.Elevated), // Heaven stairs
            B(-40.74f, 0, 17.58f, -35.85f, 3.47f, 18.28f, Surf.Elevated),
            B(-40.74f, 0, 18.28f, -35.85f, 3.18f, 18.97f, Surf.Elevated),
            B(-40.74f, 0, 18.97f, -35.85f, 2.89f, 19.67f, Surf.Elevated),
            B(-40.74f, 0, 19.67f, -35.85f, 2.6f, 20.37f, Surf.Elevated),
            B(-40.74f, 0, 20.37f, -35.85f, 2.31f, 21.07f, Surf.Elevated),
            B(-40.74f, 0, 21.07f, -35.85f, 2.02f, 21.76f, Surf.Elevated),
            B(-40.74f, 0, 21.76f, -35.85f, 1.74f, 22.46f, Surf.Elevated),
            B(-40.74f, 0, 22.46f, -35.85f, 1.45f, 23.16f, Surf.Elevated),
            B(-40.74f, 0, 23.16f, -35.85f, 1.16f, 23.86f, Surf.Elevated),
            B(-40.74f, 0, 23.86f, -35.85f, 0.87f, 24.55f, Surf.Elevated),
            B(-40.74f, 0, 24.55f, -35.85f, 0.58f, 25.25f, Surf.Elevated),
            B(-40.74f, 0, 25.25f, -35.85f, 0.29f, 25.95f, Surf.Elevated),
            // Buildings around the playable space (traced as the complement of the walkable area).
            B(-42.76f, 0, -14.86f, 5.16f, 10, -9.77f),
            B(5.16f, 0, -14.86f, 11.02f, 8, -14.23f),
            B(11.02f, 0, -14.86f, 14.72f, 9, 15.07f),
            B(-42.76f, 0, -9.77f, -28.6f, 8.5f, 11.16f),
            B(0, 0, -9.77f, 2.58f, 6, -2.37f),
            B(2.58f, 0, -9.77f, 5.16f, 8.5f, -2.37f),
            B(9.07f, 0, -7.95f, 11.02f, 7, -4.74f),
            B(0, 0, 2.37f, 2.58f, 6, 14.93f),
            B(2.58f, 0, 2.37f, 5.16f, 8.5f, 14.93f),
            B(-23.58f, 0, 9.91f, -9.77f, 7, 15.35f),
            B(-42.76f, 0, 11.16f, -40.74f, 8.5f, 25.95f),
            B(-9.77f, 0, 11.72f, -6.7f, 7, 13.25f),
            B(-3.07f, 0, 11.72f, 0, 7, 13.25f),
            B(-9.77f, 0, 13.25f, -8.79f, 7, 18),
            B(-0.98f, 0, 13.25f, 0, 7, 32.3f),
            B(0, 0, 14.93f, 2.23f, 7, 32.3f),
            B(12.28f, 0, 15.07f, 14.72f, 9, 32.3f),
            B(-23.58f, 0, 15.35f, -18.69f, 7, 17.02f),
            B(-15.76f, 0, 15.35f, -9.77f, 7, 17.02f),
            B(-35.85f, 0, 15.9f, -28.6f, 8.5f, 25.95f),
            B(-23.58f, 0, 17.02f, -21.07f, 7, 17.16f),
            B(-10.04f, 0, 17.02f, -9.77f, 7, 18),
            B(-23.58f, 0, 17.16f, -22.74f, 7, 18.55f),
            B(11.02f, 0, 18.42f, 12.28f, 9, 32.3f),
            B(2.23f, 0, 19.81f, 11.02f, 7, 32.3f),
            B(-23.58f, 0, 20.37f, -22.74f, 7, 25.95f),
            B(-10.04f, 0, 20.93f, -8.79f, 7, 32.3f),
            B(-18, 0, 21.76f, -10.04f, 7, 32.3f),
            B(-28.6f, 0, 23.02f, -23.58f, 7, 25.95f),
            B(-42.76f, 0, 25.95f, -42.55f, 8.5f, 32.3f),
            B(-8.79f, 0, 29.85f, -0.98f, 7, 32.3f),
            B(-42.55f, 0, 31.67f, -28.6f, 8.5f, 32.3f),
            B(-28.6f, 0, 31.67f, -18, 7, 32.3f),
        },
        Enemies = new[]
        {
            E("Generator", -13.46f, 1, 4.26f),
            E("Behind Gen", -20.02f, 1, 6.35f, Stance.Crouch),
            E("Tetris", -18.9f, 1, -1.88f, Stance.Crouch),
            E("Pillar", -13.6f, 1, -8.58f),
            E("Back Site", -20.72f, 1, -6.07f, Stance.Crouch),
            E("Hell", -24.76f, 1, 0.21f),
            E("Heaven", -24.2f, 4.05f, -0.77f),
            E("Heaven Pillar", -26.72f, 4.05f, -8.72f),
            E("Tree", -4.95f, 0, 14.16f),
            E("A Link", -5.79f, 0, 16.95f),
            E("A Switch", -1.88f, 0, 10.95f, Stance.Crouch),
            E("Boxes", -5.09f, 0, -7.18f),
            E("Wine", 9.97f, 0, -11.37f, Stance.Crouch),
        },
        Flashes = new[]
        {
            F("Pop through the A Main arch", V(6.77f, 1.6f, 0.21f), V(-2.16f, 2.4f, -0.07f)),
            F("High pop over the stairs", V(5.93f, 1.7f, -0.63f), V(-6.07f, 3.6f, -0.77f)),
            F("Curve around A Peek, left side", V(5.93f, 1.6f, 1.33f), V(-1.88f, 2.2f, 1.88f)),
            F("Deep pop toward Tree / A Switch", V(1.74f, 1.7f, -1.46f), V(-2.72f, 2.6f, 5.51f)),
            F("Pop right toward Boxes / Pillar", V(1.46f, 1.7f, -0.77f), V(-2.72f, 2.6f, -4.53f)),
        },
        // Standard A Main execute: Heaven (balcony over Hell) and Tree (the A door), then Generator for a third smoke.
        Smokes = new[]
        {
            new SmokeSpot("Heaven", V(-25.2f, 4.05f, -2.4f)),
            new SmokeSpot("Tree", V(-4.9f, 0, 12.5f)),
            new SmokeSpot("Generator", V(-15.6f, 1, 3.4f)),
        },
        // Spike: open site between Tetris and Generator, safe behind Generator, back site by Pillar.
        PlantSpots = new[] { V(-16.5f, 1, 1.0f), V(-15.6f, 1, 8.6f), V(-19.8f, 1, -8.6f) },
        // Retakes come from the defender side through Garden / the foot of the Heaven stairs and through A Link → Tree
        // (Heaven itself only drops onto the site, which the bots can't path).
        RetakeEntries = new[] { V(-38.5f, 0, 29.5f), V(-25f, 0, 29f), V(-5f, 0, 26.5f) },
    };
}
