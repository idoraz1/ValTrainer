using Godot;

namespace ValTrainer.Game;

/// <summary>Axis-aligned box in world meters (map blockouts are made of these).</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    /// <summary>Slab ray test. Returns distance along a normalized direction, or +inf.</summary>
    public float Ray(Vector3 o, Vector3 d)
    {
        float tmin = 0f, tmax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            float oa = o[a], da = d[a], lo = Min[a], hi = Max[a];
            if (Mathf.Abs(da) < 1e-8f)
            {
                if (oa < lo || oa > hi) return float.PositiveInfinity;
                continue;
            }
            float inv = 1f / da, t1 = (lo - oa) * inv, t2 = (hi - oa) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1);
            tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return float.PositiveInfinity;
        }
        return tmin;
    }

    /// <summary>Outward normal of the face a point on the surface lies on.</summary>
    public Vector3 NormalAt(Vector3 p)
    {
        var c = Center; var h = Size * 0.5f; var d = p - c;
        var n = new Vector3(d.X / Mathf.Max(h.X, 1e-4f), d.Y / Mathf.Max(h.Y, 1e-4f), d.Z / Mathf.Max(h.Z, 1e-4f));
        var a = n.Abs();
        if (a.X >= a.Y && a.X >= a.Z) return new Vector3(Mathf.Sign(n.X), 0, 0);
        if (a.Y >= a.Z) return new Vector3(0, Mathf.Sign(n.Y), 0);
        return new Vector3(0, 0, Mathf.Sign(n.Z));
    }
}

/// <summary>
/// Player-vs-world collision (ported from the tuned raylib build): upright box (radius × height) on its
/// feet position, axis-separated resolution so you slide along walls, step-up to <see cref="StepHeight"/>.
/// Jumping (see <see cref="Mover"/>): airborne bodies use a tiny step (<see cref="AirStep"/>: no climbing in the air), a
/// shorter body while crouch-jumping, and <see cref="Ceiling"/> stops a rising head under slabs.
/// </summary>
public static class Collision
{
    public const float PlayerRadius = 0.34f;
    public const float PlayerHeight = 1.8f;
    public const float StepHeight = 0.7f;
    /// <summary>Step allowance while airborne: you land on ledges your feet clear, you never climb them mid-air.</summary>
    public const float AirStep = 0.05f;
    /// <summary>Gravity for jumps and falls (m/s²), see <see cref="MovementTuning.Gravity"/>.</summary>
    public const float Gravity = MovementTuning.Gravity;

    public static void MoveAndSlide(ref Vector3 feet, ref Vector3 vel, Vector3 delta, IReadOnlyList<Box> world) =>
        MoveAndSlide(ref feet, ref vel, delta, world, StepHeight, PlayerHeight);

    /// <summary>Horizontal move with wall sliding. Boxes whose top is within <paramref name="step"/> of the feet don't block
    /// (step-up / landing on them is the vertical step's job); boxes starting at or above <paramref name="height"/> are overhead.</summary>
    public static void MoveAndSlide(ref Vector3 feet, ref Vector3 vel, Vector3 delta, IReadOnlyList<Box> world, float step, float height)
    {
        feet.X += delta.X;
        if (Resolve(ref feet, world, axisX: true, delta.X, step, height)) vel.X = 0;
        feet.Z += delta.Z;
        if (Resolve(ref feet, world, axisX: false, delta.Z, step, height)) vel.Z = 0;
    }

    static bool Resolve(ref Vector3 feet, IReadOnlyList<Box> world, bool axisX, float moved, float step, float height)
    {
        bool hit = false;
        foreach (var b in world)
        {
            if (b.Max.Y <= feet.Y + step || b.Min.Y >= feet.Y + height) continue;
            float minX = b.Min.X - PlayerRadius, maxX = b.Max.X + PlayerRadius;
            float minZ = b.Min.Z - PlayerRadius, maxZ = b.Max.Z + PlayerRadius;
            if (feet.X <= minX || feet.X >= maxX || feet.Z <= minZ || feet.Z >= maxZ) continue;
            hit = true;
            if (axisX)
            {
                float prev = feet.X - moved;
                feet.X = prev <= minX ? minX : prev >= maxX ? maxX : (feet.X - minX < maxX - feet.X ? minX : maxX);
            }
            else
            {
                float prev = feet.Z - moved;
                feet.Z = prev <= minZ ? minZ : prev >= maxZ ? maxZ : (feet.Z - minZ < maxZ - feet.Z ? minZ : maxZ);
            }
        }
        return hit;
    }

    /// <summary>Highest walkable surface under the player's footprint (0 = ground plane).</summary>
    public static float Ground(Vector3 feet, IReadOnlyList<Box> world) => Ground(feet, world, StepHeight);

    /// <summary>Highest surface under the footprint whose top is at most <paramref name="step"/> above the feet (0 = ground plane).</summary>
    public static float Ground(Vector3 feet, IReadOnlyList<Box> world, float step)
    {
        float best = 0f;
        const float r = PlayerRadius - 0.05f;
        foreach (var b in world)
        {
            if (b.Max.Y > feet.Y + step) continue;
            if (feet.X + r < b.Min.X || feet.X - r > b.Max.X || feet.Z + r < b.Min.Z || feet.Z - r > b.Max.Z) continue;
            best = Mathf.Max(best, b.Max.Y);
        }
        return best;
    }

    /// <summary>Lowest box bottom over the player's footprint at or above <paramref name="headY"/> (+inf = open sky).
    /// A rising head stops there (jumping under a slab, a door frame or a ledge).</summary>
    public static float Ceiling(Vector3 feet, IReadOnlyList<Box> world, float headY)
    {
        float best = float.PositiveInfinity;
        const float r = PlayerRadius;
        foreach (var b in world)
        {
            if (b.Min.Y < headY - 0.01f || b.Min.Y >= best) continue;
            if (feet.X + r <= b.Min.X || feet.X - r >= b.Max.X || feet.Z + r <= b.Min.Z || feet.Z - r >= b.Max.Z) continue;
            best = b.Min.Y;
        }
        return best;
    }

    /// <summary>Distance to the first box hit along a normalized ray, or +inf.</summary>
    public static float FirstHit(Vector3 o, Vector3 d, IReadOnlyList<Box> world, out int index)
    {
        float best = float.PositiveInfinity;
        index = -1;
        for (int i = 0; i < world.Count; i++)
        {
            float t = world[i].Ray(o, d);
            if (t < best) { best = t; index = i; }
        }
        return best;
    }

    public static float FirstHit(Vector3 o, Vector3 d, IReadOnlyList<Box> world) => FirstHit(o, d, world, out _);

    /// <summary>True if the segment a→b passes through any box.</summary>
    public static bool Blocked(Vector3 a, Vector3 b, IReadOnlyList<Box> world)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return false;
        return FirstHit(a, d / len, world) < len;
    }

    /// <summary>Does a standing body at these feet overlap any wall?</summary>
    public static bool Overlaps(Vector3 feet, IReadOnlyList<Box> world, float r = 0.3f)
    {
        foreach (var b in world)
            if (b.Max.Y > feet.Y + StepHeight && b.Min.Y < feet.Y + PlayerHeight &&
                feet.X > b.Min.X - r && feet.X < b.Max.X + r && feet.Z > b.Min.Z - r && feet.Z < b.Max.Z + r)
                return true;
        return false;
    }
}
