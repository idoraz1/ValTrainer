using Godot;

namespace ValTrainer.Maps;

/// <summary>
/// True-scale blockouts of four iconic attack routes (one partial file per map), traced from the official
/// minimaps and callout coordinates (valorant-api.com). Each file says whether it is authored in the final
/// in-game frame or in Unreal's mirrored frame (then flipped once with <see cref="MapSpot.Mirrored"/>).
/// </summary>
public static partial class MapSpots
{
    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b, 255);
    static BoxDef B(float x0, float y0, float z0, float x1, float y1, float z1, Surf s = Surf.Wall) =>
        new(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), s);
    static EnemySpot E(string n, float x, float y, float z, Stance s = Stance.Stand) => new(n, new Vector3(x, y, z), s);
    static FlashSpot F(string n, Vector3 from, Vector3 pop) => new(n, from, pop);
    static Vector3 V(float x, float y, float z) => new(x, y, z);

    // Lazy: the map fields live in other partial files (MapSpots.<Map>.cs), whose initialisation order is unspecified.
    static MapSpot[]? all;
    public static MapSpot[] All => all ??= new[]
    {
        Ascent, Bind, Breeze, Haven, Icebox, Split,
    };

    public static MapSpot ByKey(string? key) => All.FirstOrDefault(m => m.Key == key) ?? Ascent;
}
