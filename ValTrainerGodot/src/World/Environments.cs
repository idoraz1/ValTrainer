using Godot;
using ValTrainer.Game;
using ValTrainer.Maps;

namespace ValTrainer.World;

/// <summary>
/// Builds the 3D world for a session: palette-tinted PBR boxes (merged per material), trims and wall props,
/// a backdrop outside the play space, HDRI sky, sun and the graphics quality preset.
/// Contract: returns the visual root (added under the session) and the solid boxes for collision/LOS —
/// exactly the map's BoxDefs (or the range walls + the mode's extra solids); decoration never adds blockers.
/// Dev switches (quality override, perf log, fixed camera): see <see cref="EnvDev"/>.
/// </summary>
public static class Environments
{
    public const float RangeHalfWidth = 40f, RangeBack = 12f, RangeFront = -80f;
    const float RangeWallHeight = 16f;

    public sealed record Built(Node3D Root, List<Box> Solid);

    static int Quality(int requested) => EnvDev.Quality(requested);

    /// <summary>The shooting range (player at the origin looking down -Z) plus any extra solids from the mode.</summary>
    public static Built BuildRange(IEnumerable<Box> extra, int quality)
    {
        quality = Quality(quality);
        SurfaceLib.Lite = quality == 0;
        var root = new Node3D { Name = "Range" };
        var floor = new Box(new Vector3(-RangeHalfWidth, -0.1f, RangeFront), new Vector3(RangeHalfWidth, 0, RangeBack));
        var walls = new List<Box>
        {
            new(new Vector3(-RangeHalfWidth, 0, RangeFront - 1), new Vector3(RangeHalfWidth, RangeWallHeight, RangeFront)),          // back wall
            new(new Vector3(-RangeHalfWidth, 0, RangeBack), new Vector3(RangeHalfWidth, RangeWallHeight, RangeBack + 1)),            // wall behind
            new(new Vector3(-RangeHalfWidth - 1, 0, RangeFront), new Vector3(-RangeHalfWidth, RangeWallHeight, RangeBack)),          // left
            new(new Vector3(RangeHalfWidth, 0, RangeFront), new Vector3(RangeHalfWidth + 1, RangeWallHeight, RangeBack)),            // right
        };
        var extras = extra.ToList();
        var solid = new List<Box> { floor };
        solid.AddRange(walls);
        solid.AddRange(extras);

        var th = Themes.Range;
        var wb = new WorldBuilder(th, root, solid, 1234);
        wb.Floor(floor);
        foreach (var w in walls) wb.Wall(w, decorate: true);
        foreach (var b in extras) wb.Crate(b, th.Cover, th.CoverFrame);
        wb.RangeDressing(RangeHalfWidth, RangeFront, RangeBack);
        wb.Backdrop(buildings: false);
        wb.Commit();
        Finish(root, th, quality, wb.Bounds);
        return new Built(root, solid);
    }

    public static Built BuildMap(MapSpot map, int quality)
    {
        quality = Quality(quality);
        SurfaceLib.Lite = quality == 0;
        var root = new Node3D { Name = map.Key };
        var solid = new List<Box>();
        foreach (var b in map.Boxes) solid.Add(b.Bounds);

        var th = Themes.For(map);
        var wb = new WorldBuilder(th, root, solid, map.Key.Aggregate(17, (h, ch) => h * 31 + ch) & 0xffff); // stable across runs
        foreach (var e in map.Enemies) wb.Avoid(e.Feet);
        foreach (var f in map.Flashes) { wb.Avoid(f.Pop); wb.Avoid(f.From); }
        wb.Avoid(map.Choke);

        var walls = new List<Box>();
        foreach (var b in map.Boxes)
        {
            switch (b.Surf)
            {
                case Surf.Floor: wb.Floor(b.Bounds); break;
                case Surf.Trim: wb.Trim(b.Bounds); break;
                case Surf.Cover: wb.Crate(b.Bounds, th.Cover, th.CoverFrame); break;
                case Surf.Elevated: wb.Elevated(b.Bounds); break;
                default: wb.Wall(b.Bounds); walls.Add(b.Bounds); break;
            }
        }
        if (!EnvDev.Has("nodecor")) wb.DecorateWalls(walls);
        // Compile the flash effect shaders under the floor, in front of the spawn, before the first throw.
        ValTrainer.Game.Fx.FlashOrb.Prewarm(root, map.StartFeet + new Vector3(0, -1.2f, -4f));
        wb.Backdrop(buildings: !EnvDev.Has("nodecor"));
        wb.Commit();
        Finish(root, th, quality, wb.Bounds);
        return new Built(root, solid);
    }

    static void Finish(Node3D root, Theme th, int quality, Aabb bounds)
    {
        WorldLighting.Build(root, th, quality, bounds, EnvDev.SkyYaw);
        if (EnvDev.Perf || EnvDev.Cam != null) root.AddChild(new EnvPerfProbe { Quality = quality, Log = EnvDev.Perf, Cam = EnvDev.Cam });
    }
}
