using Godot;

namespace ValTrainer.Maps;

/// <summary>
/// Blockouts of four iconic attack routes. Callout distances are from Valorant's own map coordinates;
/// walls/boxes are approximations. All are authored in the raw (mirrored) frame and flipped once.
/// </summary>
public static class MapSpots
{
    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b, 255);
    static BoxDef B(float x0, float y0, float z0, float x1, float y1, float z1, Surf s = Surf.Wall) =>
        new(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), s);
    static EnemySpot E(string n, float x, float y, float z, Stance s = Stance.Stand) => new(n, new Vector3(x, y, z), s);
    static FlashSpot F(string n, Vector3 from, Vector3 pop) => new(n, from, pop);
    static Vector3 V(float x, float y, float z) => new(x, y, z);

    public static readonly MapSpot Ascent = new MapSpot
    {
        Key = "ascent", Map = "Ascent", Name = "A Main → A Site",
        Drill = "Clear Generator, Wine, Dice, Tree, Heaven/Hell and back site",
        StartFeet = V(0, 0, 8), StartYaw = 0, Choke = V(0, 0, -5.5f),
        Palette = new MapPalette(C(128, 116, 100), C(196, 172, 138), C(170, 92, 64), C(226, 214, 190), C(170, 92, 64), C(150, 175, 200)),
        Boxes = new[]
        {
            B(-29, -0.1f, -41, 23, 0, 13, Surf.Floor),
            B(-3, 0, -6, -2, 4.5f, 13), B(2, 0, -4, 3, 4.5f, 13), B(-3, 0, 12, 3, 4.5f, 13),
            B(-28, 0, -7, -19, 5, -6), B(-15.5f, 0, -7, -3, 5, -6),
            B(-19.5f, 0, -6, -19, 4, 0), B(-15.5f, 0, -6, -15, 4, 0),
            B(3, 0, -5, 23, 5, -4), B(22, 0, -41, 23, 5, -5),
            B(-29, 0, -18, -28, 5, -7), B(-29, 0, -41, -28, 5, -22), B(-29, 0, -41, 23, 8, -40),
            B(-6.5f, 0, -12.5f, -2.5f, 2.4f, -9.5f, Surf.Cover),           // Generator
            B(-1.5f, 0, -22, 1.5f, 1.1f, -19, Surf.Cover),                 // Dice low
            B(0, 0, -24, 2, 2.2f, -22, Surf.Cover),                        // Dice tall
            B(14, 0, -13, 17, 2.2f, -10, Surf.Cover),                      // Wine
            B(-13, 3.4f, -40, -1, 3.8f, -35, Surf.Elevated),               // Heaven slab
            B(-13, 3.8f, -35.3f, -1, 4.8f, -35, Surf.Trim),                // Heaven rail
            B(-13, 0, -35.4f, -12.4f, 3.4f, -34.8f, Surf.Trim), B(-1.6f, 0, -35.4f, -1, 3.4f, -34.8f, Surf.Trim),
            B(-17, 0, -40, -13, 1.7f, -37, Surf.Trim), B(-17, 1.7f, -40, -13, 3.4f, -38.5f, Surf.Trim),
            B(5, 0, -32, 7.5f, 1.1f, -29.5f, Surf.Cover), B(14, 0, -30, 16, 5, -28),
            B(-30, 0, -22, -29, 5, -18), B(-19.5f, 0, 0, -15, 4, 1),        // close Garden / Tree passages
        },
        Enemies = new[]
        {
            E("Generator", -4.5f, 0, -14), E("Wine", 15.5f, 0, -14, Stance.Crouch), E("Dice", 1, 0, -25.5f),
            E("Tree", -17, 0, -8), E("Heaven", -7, 3.8f, -37.5f), E("Hell", -6, 0, -37.5f, Stance.Crouch),
            E("Back site", 6, 0, -33.5f, Stance.Crouch), E("Garden", -27, 0, -20),
        },
        Flashes = new[]
        {
            F("Pop over main lip", V(0, 1.6f, -2), V(-2, 4, -11)),
            F("Wine bounce", V(1, 1.6f, -2), V(4, 3, -8)),
            F("Main exit pop", V(0, 1.6f, 0), V(0, 3, -7)),
        },
    }.Mirrored();

    public static readonly MapSpot Bind = new MapSpot
    {
        Key = "bind", Map = "Bind", Name = "Hookah → B Site",
        Drill = "Peek through Hookah window, drop out the door, clear Default, Garden, Elbow, Hall",
        StartFeet = V(0, 1.3f, 0), StartYaw = 0, Choke = V(3, 0.65f, -8),
        Palette = new MapPalette(C(170, 140, 104), C(214, 180, 132), C(184, 110, 72), C(232, 224, 206), C(60, 140, 150), C(160, 190, 215)),
        Boxes = new[]
        {
            B(-26, -0.1f, -43, 16, 0, -6, Surf.Floor),
            B(-4, 0, -7, 4, 1.3f, 3, Surf.Floor),                           // Hookah floor (extends under door frame)
            B(-4.5f, 1.3f, 2.5f, 4.5f, 4.5f, 3), B(-4.5f, 1.3f, -7, -4, 4.5f, 3), B(4, 1.3f, -7, 4.5f, 4.5f, 3),
            B(-4, 0, -7, -1.2f, 4.5f, -6), B(-1.2f, 0, -7, 1.2f, 2.3f, -6, Surf.Trim), B(-1.2f, 3.5f, -7, 4, 4.5f, -6),
            B(1.2f, 0, -7, 2.2f, 3.5f, -6),
            B(2.2f, 0, -8, 4, 0.65f, -7, Surf.Trim),                         // exit step
            B(-26, 0, -7, -15, 5, -6), B(-10, 0, -7, -4, 4.5f, -6),
            B(-27, 0, -43, -26, 5, -6), B(15, 0, -43, 16, 5, -6),
            B(-26, 0, -43, 1, 6, -42), B(6, 0, -43, 16, 6, -42),
            B(-3, 0, -24, 1, 2.4f, -20, Surf.Cover), B(-5, 0, -22, -3, 1.1f, -20, Surf.Cover),
            B(8, 0, -36, 11, 2.4f, -33, Surf.Cover), B(-14, 0, -30, -12, 1.8f, -27, Surf.Cover),
            B(6, 0, -42, 8, 5, -38),
            B(4.5f, 0, -7, 15, 5, -6), B(-15, 0, -7, -10, 5, -6), B(1, 0, -44, 6, 6, -43), // close the map
        },
        Enemies = new[]
        {
            E("Default", -1, 0, -25.5f, Stance.Crouch), E("Close right", 12, 0, -12), E("Garden", -12.5f, 0, -8),
            E("Elbow", -20, 0, -31), E("Cubby", 9.5f, 0, -37.5f, Stance.Crouch), E("Hall", 3.5f, 0, -40),
        },
        Flashes = new[]
        {
            F("Through window", V(0, 2.9f, -4), V(0, 3.2f, -10)),
            F("Over the door", V(3, 2.9f, -4), V(3, 3.5f, -9)),
        },
    }.Mirrored();

    public static readonly MapSpot Haven = new MapSpot
    {
        Key = "haven", Map = "Haven", Name = "C Long → C Site",
        Drill = "Long-range duels: Logs, Garage door, back site, close-left crate",
        StartFeet = V(0, 0, 3), StartYaw = 0, Choke = V(0, 0, -18.5f),
        Palette = new MapPalette(C(110, 108, 104), C(220, 212, 196), C(120, 64, 44), C(156, 120, 90), C(206, 164, 72), C(160, 185, 205)),
        Boxes = new[]
        {
            B(-13, -0.1f, -49, 30, 0, 6, Surf.Floor),
            B(-5, 0, -18, -4, 5, 6), B(-5, 0, 5, 6, 5, 6), B(5, 0, -7, 6, 5, 6), B(5, 0, -18, 6, 5, -12),
            B(9, 0, -11, 10, 5, -7), B(5, 0, -12, 10, 5, -11), B(5, 0, -7, 10, 5, -6),          // Cubby recess
            B(-13, 0, -19, -4, 6, -18), B(6, 0, -19, 25, 6, -18),
            B(-13, 0, -49, -12, 6, -18), B(24, 0, -27, 25, 6, -19), B(24, 0, -49, 25, 6, -33),
            B(25, 0, -34, 30, 5, -33, Surf.Trim), B(25, 0, -27, 30, 5, -26, Surf.Trim),
            B(-13, 0, -49, 16, 7, -48), B(21, 0, -49, 25, 7, -48),
            B(-3, 0, -34, 3, 1.3f, -29, Surf.Cover),                        // Logs
            B(-10, 0, -25, -7, 2.4f, -22, Surf.Cover),                      // close-left crate
            B(6, 0, -44, 9, 2.6f, -41, Surf.Cover), B(19, 0, -38, 21, 4, -36, Surf.Trim),
            B(30, 0, -34, 31, 5, -26), B(16, 0, -50, 21, 7, -49),          // close Garage / Link
        },
        Enemies = new[]
        {
            E("Close-left crate", -9, 0, -26.5f), E("Close right", 22, 0, -21), E("Logs", 0, 0, -35.5f, Stance.Crouch),
            E("Garage door", 23, 0, -30), E("Back site", 2, 0, -46), E("Back stack", 7.5f, 0, -45.5f, Stance.Crouch),
        },
        Flashes = new[]
        {
            F("Long exit pop", V(0, 1.6f, -14), V(0, 4, -22)),
            F("Left wall bounce", V(-2, 1.6f, -14), V(-8, 3, -21)),
        },
    }.Mirrored();

    public static readonly MapSpot Split = new MapSpot
    {
        Key = "split", Map = "Split", Name = "A Main → A Site",
        Drill = "Drop out of main and clear Elbow, Heaven, Hell, Default, Tower, Screens",
        StartFeet = V(0, 1.0f, 8), StartYaw = 0, Choke = V(0, 1.0f, -9.5f),
        Palette = new MapPalette(C(72, 74, 80), C(150, 152, 156), C(70, 96, 120), C(160, 120, 80), C(232, 64, 140), C(120, 140, 165)),
        Boxes = new[]
        {
            B(-24, -0.1f, -46, 14, 0, -9, Surf.Floor),
            B(-3, 0, -9, 3, 1.0f, 14, Surf.Floor),                           // raised main
            B(-4, 0, -9, -3, 5.5f, 14), B(3, 0, -9, 4, 5.5f, 14), B(-4, 0, 13, 4, 5.5f, 14),
            B(-24, 0, -10, -4, 6, -9), B(4, 0, -10, 14, 6, -9),
            B(-25, 0, -46, -24, 6, -10), B(13, 0, -46, 14, 6, -10),
            B(-25, 0, -47, -14, 8, -46), B(-10, 0, -47, 14, 8, -46),
            B(-16, 4.0f, -24, -6.3f, 4.4f, -12, Surf.Elevated), B(-6.3f, 4.4f, -24, -6, 5.4f, -12, Surf.Trim),
            B(-7, 0, -13, -6.3f, 4, -12.3f, Surf.Trim), B(-7, 0, -24, -6.3f, 4, -23.3f, Surf.Trim),
            B(-24, 0, -24, -16, 4.0f, -16),                                  // Tower
            B(-1, 0, -25, 2, 1.4f, -21, Surf.Cover), B(2, 0, -24, 4, 2.6f, -22, Surf.Cover),
            B(8, 0, -15, 11, 1.6f, -12, Surf.Cover), B(5, 0, -38, 8, 2.2f, -35, Surf.Cover),
            B(-13, 0, -40, -12, 6, -30),
            B(-14, 0, -48, -10, 8, -47),                                     // close Screens
        },
        Enemies = new[]
        {
            E("Elbow", 10, 0, -16.5f, Stance.Crouch), E("Heaven", -9, 4.4f, -18), E("Hell", -10, 0, -20),
            E("Default", 0.5f, 0, -26.5f, Stance.Crouch), E("Tower", -20, 4.0f, -20), E("Back site", 6.5f, 0, -39.5f),
            E("Screens", -12, 0, -43),
        },
        Flashes = new[]
        {
            F("Heaven / Default pop", V(0, 2.6f, -4), V(-2, 4.5f, -12)),
            F("Elbow bounce", V(1, 2.6f, -4), V(8, 3, -12)),
        },
    }.Mirrored();

    public static readonly MapSpot[] All = { Ascent, Bind, Haven, Split };

    public static MapSpot ByKey(string? key) => All.FirstOrDefault(m => m.Key == key) ?? Ascent;
}
