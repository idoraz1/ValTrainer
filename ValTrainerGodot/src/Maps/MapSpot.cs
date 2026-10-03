using Godot;
using ValTrainer.Game;

namespace ValTrainer.Maps;

public enum Surf { Floor, Wall, Trim, Cover, Elevated }
public enum Stance { Stand, Crouch }

/// <summary>Per-surface tint colors that evoke the real map (textures come from the environment builder).</summary>
public sealed record MapPalette(Color Floor, Color Wall, Color Trim, Color Cover, Color Elevated, Color Sky);

public sealed record BoxDef(Vector3 Min, Vector3 Max, Surf Surf = Surf.Wall)
{
    public Box Bounds => new(Min, Max);
}

/// <summary>Where a defender holds. <c>Feet</c> includes floor height (heaven spots are elevated).</summary>
public sealed record EnemySpot(string Name, Vector3 Feet, Stance Stance);

/// <summary>An attacker utility lineup: thrown from <c>From</c>, pops at <c>Pop</c>.</summary>
public sealed record FlashSpot(string Name, Vector3 From, Vector3 Pop);

/// <summary>A standard controller smoke for this site's execute: smoke centre (floor level) that cuts a main defender sightline.</summary>
public sealed record SmokeSpot(string Name, Vector3 Center);

/// <summary>
/// A simplified blockout of one famous Valorant location. Distances come from the in-game callout
/// coordinates (valorant-api.com); wall shapes are approximations. Coordinates: meters, player looks
/// down -Z at yaw 0, +X is right. <c>Choke</c> is the doorway attackers come through.
/// </summary>
public sealed class MapSpot
{
    public required string Key { get; init; }
    public required string Map { get; init; }
    public required string Name { get; init; }
    public required string Drill { get; init; }
    public required Vector3 StartFeet { get; init; }
    public float StartYaw { get; init; }
    public required Vector3 Choke { get; init; }
    public required MapPalette Palette { get; init; }
    public required BoxDef[] Boxes { get; init; }
    public required EnemySpot[] Enemies { get; init; }
    public required FlashSpot[] Flashes { get; init; }

    /// <summary>Where the spike is usually planted on this site (feet positions on walkable floor). Used by the post-plant
    /// and retake drills. Empty = the drills derive a spot from the site-level defender spots.</summary>
    public Vector3[] PlantSpots { get; init; } = Array.Empty<Vector3>();
    /// <summary>Where retaking defenders enter the site from (their spawn side): walkable floor points inside the closed area,
    /// out of sight of the site. Empty = the drills derive them (far defender spots / navigation grid).</summary>
    public Vector3[] RetakeEntries { get; init; } = Array.Empty<Vector3>();
    /// <summary>The standard controller smokes for this attack (e.g. Ascent A: Heaven, Tree). Empty = the smoke drill derives them.</summary>
    public SmokeSpot[] Smokes { get; init; } = Array.Empty<SmokeSpot>();
    /// <summary>The map's look (surfaces, sky, sun). Null = the built-in theme for this key (Ascent / Bind / Haven / Split).
    /// New maps define their theme here, next to their geometry, so every map lives in its own file.</summary>
    public Func<MapPalette, World.Theme>? Look { get; init; }

    Box[]? solid;
    public IReadOnlyList<Box> Solid => solid ??= Boxes.Select(b => b.Bounds).ToArray();

    /// <summary>Converting Unreal's left-handed coordinates mirrors the layout; flip X back so sides match the game.</summary>
    public MapSpot Mirrored()
    {
        static Vector3 F(Vector3 v) => new(-v.X, v.Y, v.Z);
        return new MapSpot
        {
            Key = Key, Map = Map, Name = Name, Drill = Drill,
            StartFeet = F(StartFeet), StartYaw = -StartYaw, Choke = F(Choke), Palette = Palette,
            Boxes = Boxes.Select(b => b with { Min = new(-b.Max.X, b.Min.Y, b.Min.Z), Max = new(-b.Min.X, b.Max.Y, b.Max.Z) }).ToArray(),
            Enemies = Enemies.Select(e => e with { Feet = F(e.Feet) }).ToArray(),
            Flashes = Flashes.Select(f => f with { From = F(f.From), Pop = F(f.Pop) }).ToArray(),
            PlantSpots = PlantSpots.Select(F).ToArray(),
            RetakeEntries = RetakeEntries.Select(F).ToArray(),
            Smokes = Smokes.Select(s => s with { Center = F(s.Center) }).ToArray(),
            Look = Look,
        };
    }

    public bool LineOfSight(Vector3 a, Vector3 b) => !Collision.Blocked(a, b, Solid);
}
