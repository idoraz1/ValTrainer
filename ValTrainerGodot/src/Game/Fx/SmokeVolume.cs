using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>Shape of a controller vision blocker.</summary>
public enum SmokeShape { Sphere, Wall }

/// <summary>
/// A controller vision blocker as the game logic sees it (the look is <see cref="SmokeFx"/>). The session's
/// <see cref="IGame.LineOfSight"/> treats every live one as opaque, so bots can't see through it either way; bullets are
/// not affected (they only test the map boxes).
/// <list type="bullet">
/// <item>Sphere smokes block a sight line once more than <see cref="SeeThrough"/> metres of it lie inside smoke (summed
/// over overlapping smokes): two players inside the same smoke see each other only up close, and someone inside sees out
/// only from the very edge.</item>
/// <item>Walls (Toxic Screen, High Tide) are vertical curtains along a polyline of ground points: any crossing below the
/// top blocks. Each column can start rising at its own time (High Tide rises from the caster outward).</item>
/// </list>
/// Size follows the session clock: grows over <see cref="FormTime"/> from <see cref="StartTime"/>, holds, and shrinks
/// over <see cref="FadeTime"/> after <see cref="Duration"/> (or when switched off early with <see cref="DownAt"/>).
/// </summary>
public sealed class SmokeVolume
{
    /// <summary>Metres of smoke a sight line may cross before it is blocked.</summary>
    public const float SeeThrough = 1.5f;

    public SmokeShape Shape = SmokeShape.Sphere;
    /// <summary>Sphere centre (world).</summary>
    public Vector3 Center;
    /// <summary>Full sphere radius (m).</summary>
    public float Radius = 4.1f;
    /// <summary>Session time at which the smoke starts forming (it can be in the future: a deploying smoke blocks nothing yet).</summary>
    public float StartTime;
    public float FormTime = 0.6f;
    /// <summary>Life from <see cref="StartTime"/> to the start of the fade (+inf = until <see cref="DownAt"/>).</summary>
    public float Duration = 15f;
    public float FadeTime = 0.5f;
    /// <summary>Session time it was switched off early (Viper toggles, fuel out). +inf = never.</summary>
    public float DownAt = float.PositiveInfinity;
    /// <summary>Who made it (logs, replay): e.g. "Omen · Dark Cover".</summary>
    public string Label = "";

    // ---- walls ----
    /// <summary>Wall ground points (y = the floor under each emitter).</summary>
    public Vector3[] Points = Array.Empty<Vector3>();
    /// <summary>Per point: session time its column starts rising.</summary>
    public float[] RiseAt = Array.Empty<float>();
    public float Height = 4f;

    public static SmokeVolume Sphere(Vector3 center, float radius, float start, float duration, float form = 0.6f, float fade = 0.5f, string label = "") =>
        new() { Shape = SmokeShape.Sphere, Center = center, Radius = radius, StartTime = start, Duration = duration, FormTime = form, FadeTime = fade, Label = label };

    /// <summary>A wall along <paramref name="points"/>; column i starts rising at <c>start + i·riseStep</c>.</summary>
    public static SmokeVolume Wall(IReadOnlyList<Vector3> points, float height, float start, float duration, float riseStep = 0f, float form = 0.5f, float fade = 0.8f, string label = "")
    {
        var v = new SmokeVolume
        {
            Shape = SmokeShape.Wall, Points = points.ToArray(), Height = height, StartTime = start, Duration = duration,
            FormTime = form, FadeTime = fade, Label = label,
        };
        v.RiseAt = new float[v.Points.Length];
        for (int i = 0; i < v.RiseAt.Length; i++) v.RiseAt[i] = start + i * riseStep;
        if (v.Points.Length > 0)
        {
            var c = Vector3.Zero;
            foreach (var p in v.Points) c += p;
            v.Center = c / v.Points.Length;
        }
        return v;
    }

    /// <summary>When the fade starts.</summary>
    public float End => Mathf.Min(StartTime + Duration, DownAt);
    public bool Gone(float now) => now >= End + FadeTime;
    public bool Up(float now) => Scale(now) > 0.5f;

    static float EaseOut(float x) { x = Mathf.Clamp(x, 0f, 1f); float u = 1f - x; return 1f - u * u * u; }

    float Shrink(float now) => now <= End ? 1f : 1f - Mathf.Clamp((now - End) / Mathf.Max(0.01f, FadeTime), 0f, 1f);

    /// <summary>0 → 1 → 0 over the smoke's life (spheres; for walls the whole-wall fade).</summary>
    public float Scale(float now)
    {
        if (now < StartTime) return 0f;
        float grow = Shape == SmokeShape.Sphere ? EaseOut((now - StartTime) / Mathf.Max(0.01f, FormTime)) : 1f;
        return grow * Shrink(now);
    }

    public float RadiusAt(float now) => Radius * Scale(now);

    /// <summary>Height fraction (0..1) of wall column <paramref name="i"/>.</summary>
    public float ColumnScale(int i, float now)
    {
        if (i < 0 || i >= RiseAt.Length || now < RiseAt[i]) return 0f;
        return EaseOut((now - RiseAt[i]) / Mathf.Max(0.01f, FormTime)) * Shrink(now);
    }

    /// <summary>Length of the segment a→b inside a sphere.</summary>
    public static float Chord(Vector3 a, Vector3 b, Vector3 c, float r)
    {
        if (r <= 0f) return 0f;
        var d = b - a;
        float len = d.Length();
        if (len < 1e-5f) return 0f;
        var u = d / len;
        var oc = a - c;
        float bq = oc.Dot(u), cq = oc.LengthSquared() - r * r;
        float h = bq * bq - cq;
        if (h <= 0f) return 0f;
        h = Mathf.Sqrt(h);
        float t0 = Mathf.Max(-bq - h, 0f), t1 = Mathf.Min(-bq + h, len);
        return Mathf.Max(0f, t1 - t0);
    }

    /// <summary>Metres of a→b inside this smoke (spheres), or +inf when a wall cuts it.</summary>
    public float Inside(Vector3 a, Vector3 b, float now, bool full = false)
    {
        if (Shape == SmokeShape.Sphere)
        {
            float r = full ? Radius : RadiusAt(now);
            return r < 0.3f ? 0f : Chord(a, b, Center, r);
        }
        return WallCuts(a, b, now, full) ? float.PositiveInfinity : 0f;
    }

    /// <summary>Does this smoke alone block a→b?</summary>
    public bool Blocks(Vector3 a, Vector3 b, float now, bool full = false) => Inside(a, b, now, full) > SeeThrough;

    /// <summary>Is the sight line a→b blocked by these smokes (sphere chords add up)?</summary>
    public static bool Blocks(IReadOnlyList<SmokeVolume> smokes, Vector3 a, Vector3 b, float now, bool full = false)
    {
        float sum = 0f;
        for (int i = 0; i < smokes.Count; i++)
        {
            sum += smokes[i].Inside(a, b, now, full);
            if (sum > SeeThrough) return true;
        }
        return false;
    }

    /// <summary>How deep (0 = outside, 1 = centre) a point is inside the smoke; walls count their curtain's thickness.</summary>
    public float Depth(Vector3 p, float now)
    {
        if (Shape == SmokeShape.Sphere)
        {
            float r = RadiusAt(now);
            if (r < 0.3f) return 0f;
            float d = p.DistanceTo(Center);
            return d >= r ? 0f : 1f - d / r;
        }
        const float half = 0.9f;
        float best = 0f;
        for (int i = 0; i + 1 < Points.Length; i++)
        {
            float h = Height * Mathf.Min(ColumnScale(i, now), ColumnScale(i + 1, now));
            if (h < 0.3f) continue;
            var a = new Vector2(Points[i].X, Points[i].Z);
            var b = new Vector2(Points[i + 1].X, Points[i + 1].Z);
            var q = new Vector2(p.X, p.Z);
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float dist = q.DistanceTo(a + ab * t);
            float ground = Mathf.Lerp(Points[i].Y, Points[i + 1].Y, t);
            if (dist < half && p.Y < ground + h && p.Y > ground - 1f) best = Mathf.Max(best, 1f - dist / half);
        }
        return best;
    }

    /// <summary>Does the segment cross the wall curtain below its (current) top?</summary>
    bool WallCuts(Vector3 a, Vector3 b, float now, bool full)
    {
        if (Points.Length < 2 || (!full && Shrink(now) <= 0f)) return false;
        var a2 = new Vector2(a.X, a.Z);
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        for (int i = 0; i + 1 < Points.Length; i++)
        {
            float h0 = full ? Height : Height * ColumnScale(i, now), h1 = full ? Height : Height * ColumnScale(i + 1, now);
            if (h0 <= 0.05f && h1 <= 0.05f) continue;
            var p = new Vector2(Points[i].X, Points[i].Z);
            var e = new Vector2(Points[i + 1].X - Points[i].X, Points[i + 1].Z - Points[i].Z);
            float den = d.X * e.Y - d.Y * e.X;
            if (Mathf.Abs(den) < 1e-6f) continue; // parallel
            var w = p - a2;
            float s = (w.X * e.Y - w.Y * e.X) / den;   // along a→b
            float t = (w.X * d.Y - w.Y * d.X) / den;   // along the wall segment
            if (s < 0f || s > 1f || t < 0f || t > 1f) continue;
            float y = Mathf.Lerp(a.Y, b.Y, s);
            float ground = Mathf.Lerp(Points[i].Y, Points[i + 1].Y, t);
            if (y >= ground - 1.5f && y <= ground + Mathf.Lerp(h0, h1, t)) return true;
        }
        return false;
    }
}
