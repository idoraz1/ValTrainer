using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Haven, C Lobby → C Long → C Site, at true scale. Walls and props were traced from the official minimap
    /// (valorant-api.com, 1 px = 0.13 m) and placed with its own transform; heights follow in-game screenshots.
    /// Authored directly in the final frame (no mirroring): Z = along C Long (attackers walk −Z, the Long exit
    /// into the site is the origin), +X = the attacker's right (Cubby, Connector, Garage, Logs side).
    /// Cut-offs where the real map continues (Link → B / defender spawn, Garage doors → Mid, Lobby → attacker
    /// spawn) are sealed with walls.
    /// </summary>
    public static readonly MapSpot Haven = new MapSpot
    {
        Key = "haven", Map = "Haven", Name = "C Long → C Site",
        Drill = "C Long into C Site: check Cubby, then clear Close right, Close left, Default, Plat, Logs, Back site, Connector, Garage, C Link and C Window",
        StartFeet = V(16.47f, 0, 36.46f), StartYaw = -34.5f, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(132, 126, 114), C(228, 220, 202), C(116, 66, 46), C(176, 174, 164), C(146, 142, 134), C(160, 188, 212)),
        Boxes = new[]
        {
            B(-11.13f, -0.1f, -32.42f, 34.83f, 0, 39.97f, Surf.Floor), // ground
            B(27.86f, 0, -32.42f, 34.83f, 7, -23.24f), // block NW of Link
            B(9.44f, 0, -32.42f, 27.86f, 7, -31.38f), // Link west wall (cut: Link continues west to defender spawn)
            B(34.05f, 0, -23.24f, 34.83f, 7, -15.04f), // Link north cap (cut: Link continues to B)
            B(23.63f, 0, -15.04f, 34.83f, 7, -14.58f), // wall Link | C Window corridor
            B(20.51f, 0, -15.04f, 21.55f, 7, -14.32f), // stub south of C Window corridor entrance
            B(15.76f, 0, -26.04f, 19.79f, 7, -11.98f), // block between Link and site (south face backs the Logs)
            B(19.79f, 0, -26.04f, 20.64f, 7, -21.09f), // block corner
            B(19.79f, 0, -16.15f, 20.51f, 7, -11.98f), // block corner
            B(-11.13f, 0, -32.42f, 9.44f, 7, -26.3f), // west of site
            B(20.51f, 0, -14.58f, 33.85f, 0.4f, -10.09f, Surf.Elevated), // C Window corridor floor
            B(21.55f, 0, -15.04f, 23.63f, 0.2f, -14.32f, Surf.Elevated), // C Window step
            B(33.85f, 0, -14.58f, 34.83f, 7, -10.09f), // C Window north cap
            B(30.34f, 0, -10.09f, 34.83f, 7, -9.64f), // Window wall N
            B(25.91f, 0, -10.09f, 30.34f, 1.3f, -9.64f), // C Window sill
            B(25.91f, 2.6f, -10.09f, 30.34f, 7, -9.64f), // C Window lintel
            B(20.51f, 0, -10.09f, 25.91f, 7, -9.64f), // Window wall S
            B(20.51f, 3.45f, -14.58f, 33.85f, 3.75f, -10.09f, Surf.Trim), // C Window ceiling
            B(33.85f, 0, -9.64f, 34.83f, 7, 5.99f), // Garage north wall
            B(30.79f, 0, 5.08f, 33.85f, 7, 5.99f), // Garage east wall N
            B(25.59f, 0, 5.08f, 30.79f, 3.6f, 5.99f, Surf.Trim), // Garage doors (closed, to Mid)
            B(25.59f, 3.6f, 5.08f, 30.79f, 7, 5.99f), // Garage door lintel
            B(22.53f, 0, 5.08f, 25.59f, 7, 5.99f), // Garage east wall S
            B(26.11f, 0, -9.64f, 30.14f, 1, -8.2f, Surf.Trim), // counter under C Window
            B(31.64f, 0, -1.43f, 33.79f, 2.2f, 0.2f, Surf.Cover), // Garage crate stack
            B(22.53f, 4.3f, -9.64f, 33.85f, 4.6f, 5.08f, Surf.Trim), // Garage ceiling
            B(12.76f, 0, -11.98f, 20.51f, 7, -7.1f), // block between Logs corner and Connector
            B(20.51f, 0, -9.64f, 22.53f, 7, -7.1f), // Garage SW wall
            B(19.92f, 0, -3.19f, 22.53f, 7, 20.83f), // Garage south wall / Mid
            B(12.76f, 0, -3.19f, 19.92f, 7, 0.13f), // Connector east wall
            B(2.8f, 0, 0.13f, 19.92f, 7, 8.72f), // Long north wall (west of Cubby)
            B(7.88f, 0, 8.72f, 19.92f, 7, 16.15f), // Cubby back wall
            B(2.8f, 0, 16.15f, 19.92f, 7, 20.77f), // block east of Cubby
            B(4.62f, 0, 8.72f, 7.88f, 0.45f, 16.15f, Surf.Elevated), // Cubby landing
            B(2.8f, 0, 8.72f, 4.62f, 0.45f, 13.93f, Surf.Elevated), // Cubby landing front
            B(3.71f, 0, 13.93f, 4.62f, 0.3f, 16.15f, Surf.Elevated), // Cubby step 2
            B(2.8f, 0, 13.93f, 3.71f, 0.15f, 16.15f, Surf.Elevated), // Cubby step 1
            B(19.92f, 0, 20.77f, 28.06f, 7, 31.84f), // block north of Lobby
            B(27.54f, 0, 31.84f, 28.32f, 7, 39.97f), // Lobby north cap (cut: path to attacker spawn)
            B(7.55f, 0, 39.19f, 27.54f, 7, 39.97f), // Lobby east wall
            B(-11.13f, 0, 33.72f, 7.55f, 7, 39.97f), // south-east block
            B(-11.13f, 0, 32.1f, 9.7f, 7, 33.72f), // Long east wall
            B(9.7f, 0, 27.93f, 10.61f, 7, 33.72f), // wall Lobby | Long (east of gate)
            B(9.7f, 0, 20.77f, 10.61f, 7, 24.02f), // gate west jamb
            B(7.88f, 0, 20.77f, 9.7f, 7, 22.85f), // block corner by the gate
            B(9.7f, 3.1f, 24.02f, 10.61f, 3.5f, 27.93f, Surf.Trim), // gate beam
            B(9.7f, 3.5f, 24.02f, 10.61f, 7, 27.93f), // wall over the gate
            B(10.61f, 0, 20.96f, 12.43f, 1.25f, 22.92f, Surf.Cover), // Lobby crate
            B(10.87f, 1.25f, 21.22f, 12.17f, 2.2f, 22.66f, Surf.Cover), // Lobby crate (stacked)
            B(12.43f, 0, 20.96f, 13.61f, 0.75f, 21.87f, Surf.Cover), // Lobby small crate
            B(19.92f, 0, 32.03f, 21.68f, 1.25f, 33.72f, Surf.Cover), // Lobby corner crate
            B(6.58f, 0, 20.83f, 7.88f, 1.3f, 22.66f, Surf.Cover), // crates at the corner by the gate
            B(5.79f, 0, 20.83f, 6.58f, 0.8f, 21.87f, Surf.Trim), // stone pallet
            B(-6.58f, 0, 20.05f, -4.88f, 0.6f, 32.1f, Surf.Trim), // flower bed (left side of Long)
            B(-11.13f, 0, 0.13f, -2.73f, 7, 9.9f), // Long south wall (site end)
            B(-11.13f, 0, 9.9f, -2.02f, 7, 17.77f), // Long south wall (blue house)
            B(-11.13f, 0, 17.77f, -5.99f, 7, 20.83f), // Long south wall
            B(-11.13f, 0, 20.83f, -6.58f, 7, 32.1f), // Long south wall (east)
            B(-11.13f, 0, -11.98f, -8.07f, 7, 0.13f), // site south wall (right of Plat)
            B(-11.13f, 0, -26.3f, -10.55f, 7, -11.98f), // site back wall
            B(-10.55f, 0, -18.16f, -4.43f, 1, -12.04f, Surf.Elevated), // Plat
            B(-10.55f, 0, -19.99f, -9.37f, 0.5f, -18.16f, Surf.Cover), // step crate to Plat
            B(1.63f, 0, -16.08f, 9.51f, 2.5f, -14.13f, Surf.Cover), // big box (C Box)
            B(7.75f, 0, -14.13f, 9.51f, 1.25f, -12.04f, Surf.Cover), // single radianite crate
            B(13.74f, 0, -18.1f, 15.69f, 1.3f, -16.02f, Surf.Trim), // Logs (pile 1)
            B(13.74f, 0, -16.02f, 15.69f, 1.05f, -12.04f, Surf.Trim), // Logs (pile 2)
            B(-1.5f, 0, -26.3f, 0.52f, 2.5f, -24.22f, Surf.Cover), // back-site double crate
            B(4.69f, 0, -1.95f, 6.58f, 1.25f, 0.13f, Surf.Cover), // crate right of the Long exit
            B(9.57f, 0, -31.12f, 11.52f, 1.25f, -29.17f, Surf.Cover), // crate at Link exit
        },
        Enemies = new[]
        {
            E("Cubby", 6.45f, 0.45f, 11.98f),
            E("Close right", 5.66f, 0, -2.99f, Stance.Crouch),
            E("Close left", -6.58f, 0, -2.6f),
            E("Default", 5.14f, 0, -17.06f, Stance.Crouch),
            E("Plat", -6.97f, 1, -15.36f),
            E("Logs", 14.39f, 0, -19.4f),
            E("Back site", 3.32f, 0, -24.22f),
            E("Connector", 14.26f, 0, -5.08f),
            E("Garage", 26.89f, 0, -2.6f),
            E("C Link", 13.22f, 0, -28.65f),
            E("C Window", 28.19f, 0.4f, -12.37f, Stance.Crouch),
        },
        Flashes = new[]
        {
            F("Long exit pop", V(-0.07f, 1.6f, 3.65f), V(0.2f, 3.2f, -1.3f)),
            F("Over the right wall", V(1.24f, 1.6f, 3.26f), V(6.97f, 5, -2.6f)),
            F("Deep site lob", V(-0.59f, 1.6f, 4.17f), V(1.76f, 4.6f, -8.85f)),
            F("Left corner pop", V(-1.37f, 1.6f, 3.65f), V(-4.49f, 3.2f, -2.21f)),
        },
        // Standard C Long execute: C Link (the defender-spawn side) and Garage (the Connector mouth), then Logs.
        Smokes = new[]
        {
            new SmokeSpot("C Link", V(12.4f, 0, -27.4f)),
            new SmokeSpot("Garage", V(15.6f, 0, -5.2f)),
            new SmokeSpot("Logs", V(13.2f, 0, -19.6f)),
        },
        // Spike: default behind the big C box, in front of the box, back site by Plat.
        PlantSpots = new[] { V(5.5f, 0, -17.4f), V(4.5f, 0, -12.6f), V(-3f, 0, -21.5f) },
        // Retakes: C Link from the B / defender-spawn side, Garage from Mid, and the C Window corridor.
        RetakeEntries = new[] { V(31f, 0, -19.5f), V(29f, 0, 2.5f), V(32.5f, 0.4f, -12.3f) },
    };
}
