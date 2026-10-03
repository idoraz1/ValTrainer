using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Breeze, A Lobby → A Main (the Chop Shop) → A Site, true scale, current layout (the January 2026 rework: one A Main
    /// through the shop, the diagonal Yellow container at back site, the back-site wall cutout, Halls closed). Walls are traced
    /// from the official minimap through the valorant-api.com transform (1 px = 13.95 cm); heights follow reference screenshots.
    /// Authored directly in the game's own (unmirrored) frame, no Mirrored(): origin = the A Main exit onto the site (the choke),
    /// -Z = north (into the site, toward Back Site and defender spawn), +X = east.
    /// Levels: A Lobby sand 0 m; up the Chop Shop stairs, A Main, A Site and Mid Doors are +1.5 m; the Pool around the two
    /// pyramids (Shrimp west, Crab east) is 0.3 m lower; A Bridge is a +5.5 m walkway at the back left, reached by A Ramp,
    /// with the spawn passage running under its deck. Cut-offs (Lobby → attacker spawn, Mid, defender spawn, the Bridge's
    /// west end) are sealed with walls. Diagonal walls and the Yellow container are 0.5 m saw-tooth steps.
    /// </summary>
    public static readonly MapSpot Breeze = new MapSpot
    {
        Key = "breeze", Map = "Breeze", Name = "A Main → A Site",
        Drill = "Push A Lobby up the Chop Shop into A Main and out onto A Site: clear Boxes, Shrimp, Pool, Crab, Back Site, Yellow, Under Bridge, Bridge (high), Ramp and Mid Doors",
        StartFeet = V(-22.46f, 0, 20.65f), StartYaw = 25, Choke = V(0, 1.5f, 0),
        Palette = new MapPalette(C(210, 196, 168), C(228, 182, 154), C(236, 150, 44), C(196, 152, 104), C(118, 134, 142), C(140, 200, 240)),
        Look = BreezeLook,
        Boxes = new[]
        {
            // --- ground: A Lobby sand
            B(-35.3f, -0.1f, 3.9f, -16.45f, 0, 26.35f, Surf.Floor), // A Lobby sand (ground level 0)
            // --- site level slab (A Main, A Site, Halls); the walls stand on it
            B(-46.2f, 0, -61.45f, 18.3f, 1.5f, -24.15f, Surf.Floor), // A Main / A Site level (+1.5 m), walls stand on it
            B(-46.2f, 0, -24.15f, -15.05f, 1.5f, 3.9f, Surf.Floor), B(8.95f, 0, -24.15f, 18.3f, 1.5f, 26.4f, Surf.Floor),
            B(-15.05f, 0, -14.25f, 8.95f, 1.5f, 26.4f, Surf.Floor), B(-46.2f, 0, 3.9f, -35.3f, 1.5f, 26.4f, Surf.Floor),
            B(-16.45f, 0, 3.9f, -15.05f, 1.5f, 26.4f, Surf.Floor), B(-35.3f, 0, 26.35f, -16.45f, 1.5f, 26.4f, Surf.Floor),
            // --- Pool
            B(-15.05f, 0, -24.15f, 8.95f, 1.2f, -14.25f, Surf.Elevated), // the Pool around the pyramids (0.3 m below the site)
            // --- walls and buildings traced from the minimap (complement of the playable space; diagonals as 0.5 m saw-tooth)
            B(-26.8f, 1.5f, -61.45f, -11.05f, 10.5f, -59.45f), B(-26.8f, 1.5f, -59.45f, -24.4f, 10.5f, -53.7f),
            B(-13.55f, 1.5f, -59.45f, -11.05f, 10.5f, -43.1f), B(-24.4f, 1.5f, -56.8f, -24.15f, 10.5f, -53.7f),
            B(-24.15f, 1.5f, -56.35f, -23.7f, 10.5f, -53.7f), B(-33.35f, 1.5f, -55.9f, -26.8f, 10.5f, -53.7f),
            B(-23.7f, 1.5f, -55.9f, -23.2f, 10.5f, -53.7f), B(-33.75f, 1.5f, -55.45f, -33.35f, 10.5f, -37.35f),
            B(-23.2f, 1.5f, -55.45f, -22.75f, 10.5f, -53.7f), B(-34.2f, 1.5f, -55.05f, -33.75f, 10.5f, -37.35f),
            B(-22.75f, 1.5f, -55.05f, -22.25f, 10.5f, -53.7f), B(-22.25f, 1.5f, -54.6f, -21.8f, 10.5f, -53.7f),
            B(-35.3f, 1.5f, -54.15f, -34.2f, 10.5f, -37.35f), B(-21.8f, 1.5f, -54.15f, -21.3f, 10.5f, -53.7f),
            B(-33.35f, 1.5f, -53.7f, -32.35f, 10.5f, -37.35f), B(-28.45f, 1.5f, -49.4f, -21.75f, 10.5f, -48.75f),
            B(-28.45f, 1.5f, -48.75f, -22.2f, 10.5f, -48.3f), B(-28.45f, 1.5f, -48.3f, -22.65f, 10.5f, -47.85f),
            B(-28.45f, 1.5f, -47.85f, -23.1f, 10.5f, -47.4f), B(-28.45f, 1.5f, -47.4f, -23.55f, 10.5f, -47),
            B(-28.45f, 1.5f, -47, -23.95f, 10.5f, -46.55f), B(-28.45f, 1.5f, -46.55f, -24.4f, 10.5f, -38.1f),
            B(-11.05f, 1.5f, -46.3f, -4.9f, 10.5f, -43.1f), B(-4.9f, 1.5f, -46.3f, -1.9f, 6, -42.85f),
            B(-1.9f, 1.5f, -43.1f, -1.4f, 6, -39.85f), B(-4.4f, 1.5f, -42.85f, -1.9f, 6, -42.35f),
            B(-1.4f, 1.5f, -42.85f, -0.9f, 6, -39.35f), B(-3.9f, 1.5f, -42.35f, -1.9f, 6, -41.85f),
            B(-0.9f, 1.5f, -42.35f, -0.4f, 6, -38.85f), B(-3.4f, 1.5f, -41.85f, -1.9f, 6, -41.35f),
            B(-0.4f, 1.5f, -41.85f, 0.1f, 6, -38.35f), B(-2.9f, 1.5f, -41.35f, -1.9f, 6, -40.85f),
            B(0.1f, 1.5f, -41.35f, 0.6f, 6, -37.85f), B(-2.4f, 1.5f, -40.85f, -1.9f, 6, -40.35f),
            B(0.6f, 1.5f, -40.85f, 1.1f, 6, -37.35f), B(1.1f, 1.5f, -40.35f, 1.6f, 6, -36.85f),
            B(1.6f, 1.5f, -39.85f, 2.1f, 6, -36.35f), B(2.1f, 1.5f, -39.35f, 2.6f, 6, -35.85f),
            B(2.6f, 1.5f, -38.85f, 2.95f, 6, -35.35f), B(2.95f, 1.5f, -38.35f, 3.1f, 6, -35.35f),
            B(3.1f, 1.5f, -38.1f, 3.6f, 6, -34.85f), B(3.6f, 1.5f, -37.85f, 4.1f, 6, -34.35f),
            B(-35.3f, 1.5f, -37.35f, -32.35f, 9, -33.6f), B(4.1f, 1.5f, -37.35f, 4.6f, 6, -33.85f),
            B(4.6f, 1.5f, -36.85f, 6.4f, 6, -31.55f), B(6.4f, 1.5f, -34.85f, 8.1f, 6, -31.55f),
            B(-32.35f, 1.5f, -34.2f, -24.4f, 9, -31.55f), B(-34.2f, 1.5f, -33.6f, -32.35f, 9, -31.55f),
            B(8.1f, 1.5f, -33.6f, 18.3f, 6, -31.55f), B(-26.8f, 1.5f, -31.55f, -24.4f, 9, -23.15f),
            B(16.3f, 1.5f, -31.55f, 18.3f, 6, -16.95f), B(-46.2f, 1.5f, -27.2f, -26.8f, 9, -23.15f),
            B(-24.4f, 1.5f, -27.2f, -20.35f, 9, -22.2f), B(-46.2f, 1.5f, -23.15f, -44.2f, 9, -3.95f),
            B(-38.5f, 1.5f, -23.15f, -36.15f, 9, -21.6f), B(-24.4f, 1.5f, -22.2f, -20.6f, 9, -21.75f),
            B(-24.4f, 1.5f, -21.75f, -21.1f, 9, -21.55f), B(-38.35f, 1.5f, -21.6f, -37.1f, 9, -20.1f),
            B(-24, 1.5f, -21.55f, -21.1f, 9, -21.3f), B(-24, 1.5f, -21.3f, -21.55f, 9, -21.1f),
            B(-23.6f, 1.5f, -21.1f, -21.55f, 9, -20.85f), B(-23.6f, 1.5f, -20.85f, -22.05f, 9, -20.65f),
            B(-23.15f, 1.5f, -20.65f, -22.05f, 9, -20.4f), B(-23.15f, 1.5f, -20.4f, -22.5f, 9, -20.2f),
            B(-22.75f, 1.5f, -20.2f, -22.5f, 9, -19.95f), B(13.4f, 1.5f, -19.1f, 16.3f, 6, -12.6f),
            B(12.9f, 1.5f, -18.4f, 13.4f, 6, -9.7f), B(-35.7f, 1.5f, -17.85f, -34.2f, 9, -12.6f),
            B(-26.8f, 1.5f, -17.85f, -25.25f, 9, -7.25f), B(12.45f, 1.5f, -16.95f, 12.9f, 6, -9.7f),
            B(-36.25f, 1.5f, -16.45f, -35.7f, 9, -12.6f), B(-34.2f, 1.5f, -16.45f, -33.75f, 9, -12.6f),
            B(-27.2f, 1.5f, -16.45f, -26.8f, 9, -12.6f), B(-25.25f, 1.5f, -16.45f, -24.55f, 9, -6.8f),
            B(11.95f, 1.5f, -15.5f, 12.45f, 6, -8.25f), B(-40.45f, 1.5f, -15.2f, -36.25f, 9, -12.6f),
            B(-33.75f, 1.5f, -15.2f, -27.2f, 9, -12.6f), B(-24.55f, 1.5f, -15.2f, -24.4f, 9, -6.8f),
            B(11.45f, 1.5f, -14.05f, 11.95f, 6, -5.4f), B(-40.45f, 1.5f, -12.6f, -38.5f, 9, -3.95f),
            B(11, 1.5f, -12.6f, 11.45f, 6, -5.4f), B(10.5f, 1.5f, -11.15f, 11, 6, -3.95f),
            B(10, 1.5f, -9.7f, 10.5f, 6, -3.2f), B(-24.4f, 1.5f, -9.05f, -9.9f, 9, -6.8f),
            B(9.55f, 1.5f, -8.25f, 10, 6, -3.2f), B(-44.2f, 1.5f, -7.25f, -40.45f, 9, -3.95f),
            B(-11.95f, 1.5f, -6.8f, -9.9f, 9, 1.25f), B(9.05f, 1.5f, -6.8f, 9.55f, 6, -3.2f),
            B(8.55f, 1.5f, -5.4f, 9.05f, 6, -3.2f), B(8.1f, 1.5f, -3.95f, 8.55f, 6, -3.2f),
            B(2.95f, 1.5f, -3.2f, 10.5f, 9, -1.25f), B(-18.55f, 1.5f, -1.25f, -11.95f, 9, 1.25f),
            B(-9.9f, 1.5f, -1.25f, -2.95f, 9, 1.25f), B(2.95f, 1.5f, -1.25f, 8.1f, 9, 2.8f),
            B(-18.55f, 1.5f, 1.25f, -16.45f, 9, 3.9f), B(8.1f, 1.5f, 1.25f, 8.55f, 9, 11.25f),
            B(-34.2f, 1.5f, 2.8f, -23.95f, 9, 3.9f), B(6.4f, 1.5f, 2.8f, 8.1f, 9, 12.35f),
            B(-35.3f, 0, 3.9f, -22.65f, 9, 5.7f), B(-18.55f, 0, 3.9f, -16.45f, 9, 10.05f),
            B(-35.3f, 0, 5.7f, -33.35f, 9, 26.35f), B(-24.55f, 0, 5.7f, -21.8f, 9, 9),
            B(-21.8f, 0, 8.65f, -21.75f, 9, 12.7f), B(-18.7f, 0, 8.65f, -18.55f, 9, 11.45f),
            B(-24.05f, 0, 9, -21.8f, 9, 9.75f), B(-21.75f, 0, 9, -21.1f, 9, 12.7f),
            B(-19.75f, 0, 9, -18.7f, 9, 11.45f), B(-23.55f, 0, 9.75f, -21.8f, 9, 10.5f),
            B(-21.1f, 0, 9.75f, -21.05f, 9, 13.45f), B(-20.15f, 0, 9.75f, -19.75f, 9, 12.35f),
            B(-21.05f, 0, 10.05f, -20.8f, 9, 13.55f), B(-20.35f, 0, 10.05f, -20.15f, 9, 12.85f),
            B(-18.55f, 0, 10.05f, -16.8f, 9, 10.5f), B(-20.8f, 0, 10.2f, -20.35f, 9, 12.85f),
            B(-10.6f, 1.5f, 10.2f, 6.4f, 9, 12.35f), B(-23.05f, 0, 10.5f, -21.8f, 9, 11.25f),
            B(-18.55f, 0, 10.5f, -17.45f, 9, 10.95f), B(-18.55f, 0, 10.95f, -18.15f, 9, 11.45f),
            B(-22.55f, 0, 11.25f, -21.8f, 9, 11.95f), B(-19.75f, 0, 11.45f, -18.8f, 9, 11.9f),
            B(-19.75f, 0, 11.9f, -19.5f, 9, 12.35f), B(-22.05f, 0, 11.95f, -21.8f, 9, 12.7f),
            B(-10.6f, 1.5f, 12.35f, -4.9f, 9, 15.2f), B(-21.55f, 0, 12.7f, -21.1f, 9, 13.45f),
            B(-20.8f, 0, 12.85f, -20.5f, 9, 13.1f), B(-11.05f, 1.5f, 12.95f, -10.6f, 9, 15.65f),
            B(-11.95f, 1.5f, 13.4f, -11.05f, 9, 16.1f), B(-12.8f, 1.5f, 13.85f, -11.95f, 9, 16.55f),
            B(-13.7f, 1.5f, 14.3f, -12.8f, 9, 16.8f), B(-14.6f, 1.5f, 14.75f, -13.7f, 9, 17.6f),
            B(-15.45f, 1.5f, 15.2f, -14.6f, 9, 17.6f), B(-10.6f, 1.5f, 15.2f, -9.9f, 9, 15.5f),
            B(-16.35f, 1.5f, 15.65f, -15.45f, 9, 26.35f), B(-17.25f, 0, 16.1f, -16.45f, 9, 26.35f),
            B(-16.45f, 1.5f, 16.1f, -16.35f, 9, 26.35f), B(-18.1f, 0, 16.55f, -17.25f, 9, 18),
            B(-13.7f, 1.5f, 16.8f, -13.55f, 9, 17), B(-18.7f, 0, 17, -18.1f, 9, 17.6f),
            B(-18.85f, 0, 17.15f, -18.7f, 9, 17.6f), B(-18.55f, 0, 17.6f, -18.1f, 9, 18),
            B(-15.45f, 1.5f, 17.6f, -15.05f, 9, 18), B(-17.45f, 0, 18, -17.25f, 9, 26.35f),
            B(-33.35f, 0, 24.4f, -17.45f, 9, 26.35f),
            // --- stairs, A Bridge, pyramids, Yellow, cover, ceilings
            B(-18.85f, 0, 9.5f, -18.35f, 0.3f, 16.95f, Surf.Floor), // Chop Shop stairs (A Lobby up to A Main)
            B(-18.35f, 0, 9.5f, -17.9f, 0.6f, 16.95f, Surf.Floor), B(-17.9f, 0, 9.5f, -17.4f, 0.9f, 16.95f, Surf.Floor),
            B(-17.4f, 0, 9.5f, -16.95f, 1.2f, 16.95f, Surf.Floor), B(-16.95f, 0, 9.5f, -16.45f, 1.5f, 16.95f, Surf.Floor),
            B(-32.35f, 1.5f, -42.5f, -28.45f, 1.83f, -41.85f, Surf.Floor), // A Ramp up to A Bridge
            B(-32.35f, 1.5f, -43.1f, -28.45f, 2.17f, -42.5f, Surf.Floor), B(-32.35f, 1.5f, -43.75f, -28.45f, 2.5f, -43.1f, Surf.Floor),
            B(-32.35f, 1.5f, -44.35f, -28.45f, 2.83f, -43.75f, Surf.Floor), B(-32.35f, 1.5f, -45, -28.45f, 3.17f, -44.35f, Surf.Floor),
            B(-32.35f, 1.5f, -45.6f, -28.45f, 3.5f, -45, Surf.Floor), B(-32.35f, 1.5f, -46.25f, -28.45f, 3.83f, -45.6f, Surf.Floor),
            B(-32.35f, 1.5f, -46.9f, -28.45f, 4.17f, -46.25f, Surf.Floor), B(-32.35f, 1.5f, -47.5f, -28.45f, 4.5f, -46.9f, Surf.Floor),
            B(-32.35f, 1.5f, -48.15f, -28.45f, 4.83f, -47.5f, Surf.Floor), B(-32.35f, 1.5f, -48.75f, -28.45f, 5.17f, -48.15f, Surf.Floor),
            B(-32.35f, 1.5f, -49.4f, -28.45f, 5.5f, -48.75f, Surf.Floor),
            B(-32.35f, 1.5f, -53.7f, -19.1f, 5.2f, -49.4f), // under the Bridge walkway
            B(-32.35f, 5.2f, -53.7f, -19.1f, 5.5f, -49.4f, Surf.Floor), // A Bridge walkway (+5.5 m)
            B(-19.1f, 5.2f, -53.7f, -13.55f, 5.5f, -49.4f, Surf.Elevated), // A Bridge deck over the spawn passage
            B(-21.75f, 5.5f, -49.55f, -13.55f, 6.5f, -49.4f, Surf.Elevated), // Bridge railing, site side
            B(-21.35f, 5.5f, -53.7f, -13.55f, 6.5f, -53.55f, Surf.Elevated), // Bridge railing, spawn side
            B(-14.1f, 1.2f, -23.15f, -6, 1.55f, -15.2f, Surf.Elevated), // Shrimp pyramid: plinth, body, tiers, cap
            B(-13.35f, 1.55f, -22.4f, -6.75f, 3.55f, -15.95f, Surf.Elevated), B(-13.05f, 3.55f, -22.1f, -7.05f, 3.93f, -16.25f, Surf.Elevated),
            B(-12.75f, 3.93f, -21.8f, -7.35f, 4.31f, -16.55f, Surf.Elevated), B(-12.45f, 4.31f, -21.5f, -7.65f, 4.69f, -16.85f, Surf.Elevated),
            B(-12.15f, 4.69f, -21.2f, -7.95f, 5.07f, -17.15f, Surf.Elevated), B(-11.85f, 5.07f, -20.9f, -8.25f, 5.45f, -17.45f, Surf.Elevated),
            B(-11.55f, 5.45f, -20.6f, -8.55f, 5.83f, -17.75f, Surf.Elevated), B(-11.1f, 5.83f, -20.15f, -9, 6.3f, -18.2f, Surf.Elevated),
            B(0, 1.2f, -23.15f, 7.95f, 1.55f, -15.2f, Surf.Elevated), // Crab pyramid: plinth, body, tiers, cap
            B(0.75f, 1.55f, -22.4f, 7.2f, 3.55f, -15.95f, Surf.Elevated), B(1.05f, 3.55f, -22.1f, 6.9f, 3.93f, -16.25f, Surf.Elevated),
            B(1.35f, 3.93f, -21.8f, 6.6f, 4.31f, -16.55f, Surf.Elevated), B(1.65f, 4.31f, -21.5f, 6.3f, 4.69f, -16.85f, Surf.Elevated),
            B(1.95f, 4.69f, -21.2f, 6, 5.07f, -17.15f, Surf.Elevated), B(2.25f, 5.07f, -20.9f, 5.7f, 5.45f, -17.45f, Surf.Elevated),
            B(2.55f, 5.45f, -20.6f, 5.4f, 5.83f, -17.75f, Surf.Elevated), B(3, 5.83f, -20.15f, 4.95f, 6.3f, -18.2f, Surf.Elevated),
            B(-6, 1.2f, -20.1f, 0, 2.1f, -18.3f, Surf.Elevated), // generator between the pyramids
            B(-11.35f, 1.5f, -36.7f, -10.85f, 4.1f, -36.2f, Surf.Trim), // Yellow: the diagonal container at back site
            B(-11.85f, 1.5f, -36.2f, -10.35f, 4.1f, -35.75f, Surf.Trim), B(-12.3f, 1.5f, -35.75f, -9.9f, 4.1f, -35.25f, Surf.Trim),
            B(-12.8f, 1.5f, -35.25f, -9.4f, 4.1f, -34.75f, Surf.Trim), B(-13.3f, 1.5f, -34.75f, -9.45f, 4.1f, -34.3f, Surf.Trim),
            B(-13.75f, 1.5f, -34.3f, -9.95f, 4.1f, -33.8f, Surf.Trim), B(-14.25f, 1.5f, -33.8f, -10.45f, 4.1f, -33.3f, Surf.Trim),
            B(-14.75f, 1.5f, -33.3f, -10.95f, 4.1f, -32.8f, Surf.Trim), B(-15.25f, 1.5f, -32.8f, -11.4f, 4.1f, -32.35f, Surf.Trim),
            B(-15.7f, 1.5f, -32.35f, -11.9f, 4.1f, -31.85f, Surf.Trim), B(-16.2f, 1.5f, -31.85f, -12.4f, 4.1f, -31.35f, Surf.Trim),
            B(-16.25f, 1.5f, -31.35f, -12.85f, 4.1f, -30.9f, Surf.Trim), B(-15.8f, 1.5f, -30.9f, -13.35f, 4.1f, -30.4f, Surf.Trim),
            B(-15.3f, 1.5f, -30.4f, -13.85f, 4.1f, -29.9f, Surf.Trim), B(-14.8f, 1.5f, -29.9f, -14.3f, 4.1f, -29.4f, Surf.Trim),
            B(3.05f, 1.5f, -31, 4.65f, 2.6f, -29.4f, Surf.Cover), // box in the Back Site cutout
            B(-12, 1.5f, -11.3f, -10.05f, 3.1f, -9.2f, Surf.Cover), // crate stack by the A Main exit
            B(-10.05f, 1.5f, -10.2f, -9.05f, 2.3f, -9.2f, Surf.Cover), B(-9.9f, 1.5f, -9.05f, -8.1f, 2.6f, -7.4f, Surf.Cover),
            B(-10.6f, 1.5f, 6.3f, -8.65f, 2.7f, 10.2f, Surf.Cover), // the A Main box
            B(-26.95f, 0, 5.7f, -24.55f, 1.3f, 8.1f, Surf.Cover), // A Lobby crate
            B(-16.45f, 5.7f, 1.25f, 6.4f, 6, 10.2f, Surf.Elevated), // A Main (shop) ceiling
            B(-21.05f, 5.7f, 9.5f, -10.6f, 6, 13.8f, Surf.Elevated), // shop passage ceiling
            B(-20.95f, 3.2f, 13.8f, -20.55f, 9, 14.3f), // over the Chop Shop door
            B(-20.55f, 5.7f, 13.8f, -10.6f, 6, 14.3f, Surf.Elevated), B(-20.65f, 3.2f, 14.3f, -20.25f, 9, 14.75f),
            B(-20.25f, 5.7f, 14.3f, -10.6f, 6, 14.75f, Surf.Elevated), B(-20.3f, 3.2f, 14.75f, -19.9f, 9, 15.25f),
            B(-19.9f, 5.7f, 14.75f, -10.6f, 6, 15.25f, Surf.Elevated), B(-20, 3.2f, 15.25f, -19.6f, 9, 15.7f),
            B(-19.6f, 5.7f, 15.25f, -10.6f, 6, 15.7f, Surf.Elevated), B(-19.7f, 3.2f, 15.7f, -19.3f, 9, 16.2f),
            B(-19.3f, 5.7f, 15.7f, -10.6f, 6, 16.2f, Surf.Elevated), B(-19.35f, 3.2f, 16.2f, -18.95f, 9, 16.7f),
            B(-18.95f, 5.7f, 16.2f, -10.6f, 6, 16.7f, Surf.Elevated), B(-19.05f, 3.2f, 16.7f, -18.65f, 9, 17.15f),
            B(-18.65f, 5.7f, 16.7f, -10.6f, 6, 17.15f, Surf.Elevated),
            B(-2.95f, 4.9f, -1.25f, 2.95f, 9, 1.25f), // over the A Main exit
        },
        Enemies = new[]
        {
            E("Shrimp", -11.58f, 1.5f, -13.95f),
            E("Pool", -3.07f, 1.2f, -16.74f, Stance.Crouch),
            E("Crab", 9.35f, 1.5f, -13.95f),
            E("Boxes", -7.11f, 1.5f, -5.58f, Stance.Crouch),
            E("Back Site", 5.72f, 1.5f, -27.9f, Stance.Crouch),
            E("Yellow", -11.3f, 1.5f, -38.23f),
            E("Under Bridge", -16.88f, 1.5f, -46.32f),
            E("Bridge", -22.46f, 5.5f, -51.34f),
            E("Ramp", -27.48f, 1.5f, -36.27f),
            E("Mid Doors", -29.16f, 1.5f, -19.81f),
        },
        Flashes = new[]
        {
            F("Pop over the A Main exit", V(0, 3.1f, 3.91f), V(0, 4.2f, -3.63f)),
            F("High pop over the Pool", V(-0.98f, 3.1f, 5.3f), V(-3.07f, 6.4f, -12.14f)),
            F("Curve left toward Shrimp", V(1.53f, 3.1f, 2.79f), V(-7.11f, 3.6f, -6.14f)),
            F("Curve right toward Crab", V(0.7f, 3.1f, 2.51f), V(3.77f, 3.6f, -5.86f)),
            F("Deep pop between the pyramids", V(-0.98f, 3.1f, 3.91f), V(-3.07f, 5.4f, -19.81f)),
        },
        PlantSpots = new[] { V(-3.07f, 1.2f, -16.46f), V(11.86f, 1.5f, -21.62f), V(-3.07f, 1.2f, -22.32f) },
        RetakeEntries = new[] { V(-23.02f, 1.5f, -58.31f), V(-30.83f, 5.5f, -51.9f), V(-42.41f, 1.5f, -9.21f) },
        Smokes = new[] { new SmokeSpot("Bridge", V(-17.44f, 1.5f, -44.92f)), new SmokeSpot("Mid Doors", V(-23.3f, 1.5f, -17.86f)), new SmokeSpot("Back Site", V(-2.93f, 1.5f, -28.88f)) },
    };

    /// <summary>Tropical island: bright sun, sandy concrete, peach plaster with teal bands, grey stone, an orange container,
    /// pale teal-grey metal pyramids and pool, rusty corrugated roofs and green hills around.</summary>
    static World.Theme BreezeLook(MapPalette p) => new()
    {
        Key = "breeze", Style = World.DecorStyle.Ascent, Hdri = World.Hdris.Kloofendal, SkyYaw = 35,
        SunColor = new Color(1f, 0.96f, 0.88f), SunEnergy = 2.0f, Ambient = 1.05f, Exposure = 1.04f,
        Saturation = 1.18f, Contrast = 1.05f, FogDensity = 0.0008f, Ground = C(206, 188, 150),
        SkyTint = new Color(0.97f, 1f, 1.03f),
        Floor = new World.Surface("concrete_floor_worn_02", p.Floor, 0.9f, 2.4f, 0.55f, Normal: 0.8f, Macro: 0.12f, Grime: 0.12f),
        Wall = new World.Surface("painted_plaster_wall", p.Wall, 0.88f, 2.2f, 1.1f, Normal: 0.9f, Macro: 0.08f),
        Base = new World.Surface("sandstone_blocks_08", C(178, 176, 166), 0.7f, 2.4f, 0.9f, Rough: 0.2f, Grime: 0.25f),
        Cap = new World.Surface("wood_planks", C(56, 158, 160), 0.9f, 1.5f, 0.6f, Grime: 0f),
        Trim = new World.Surface("rusty_metal_sheet", p.Trim, 0.85f, 2f, 0.7f, Grime: 0.08f),
        Elevated = new World.Surface("metal_plate_02", p.Elevated, 0.85f, 2f, 0.7f, Metal: 0.5f, Grime: 0.05f),
        Cover = new World.Surface("wood_planks", p.Cover, 0.75f, 1.5f, 0.7f, Grime: 0.1f),
        CoverFrame = new World.Surface("wood_planks", C(120, 88, 60), 0.85f, 1.5f, 0.7f, Grime: 0f),
        GroundSurf = new World.Surface("concrete_floor_worn_02", C(214, 196, 160), 0.9f, 2.6f, 0.45f, Grime: 0f),
        Backdrop = new World.Surface("painted_plaster_wall", Colors.White, 0.88f, 2.2f, 1.1f, Macro: 0.1f),
        Roof = new World.Surface("rusty_metal_sheet", C(170, 96, 60), 0.9f, 2f, 0.7f, Grime: 0f),
        Hues = new[] { C(232, 186, 160), C(150, 200, 196), C(236, 226, 200), C(170, 196, 214), C(226, 206, 150), C(214, 150, 126) },
        MinH = 7.5f, MaxH = 13f, Mountains = true, MountainColor = new Color(0.34f, 0.48f, 0.38f),
    };
}
