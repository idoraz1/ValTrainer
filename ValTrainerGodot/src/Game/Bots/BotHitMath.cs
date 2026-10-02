using Godot;

namespace ValTrainer.Game.Bots;

/// <summary>Analytic ray tests for bone-driven hitboxes (no physics engine). <c>d</c> must be normalized.</summary>
internal static class BotHitMath
{
    /// <summary>Distance to the first hit of a ray with a sphere, or +inf. A ray starting inside hits at 0.</summary>
    public static float Sphere(Vector3 o, Vector3 d, Vector3 c, float r)
    {
        var oc = o - c;
        float b = oc.Dot(d), cc = oc.Dot(oc) - r * r;
        if (cc <= 0f) return 0f;
        if (b > 0f) return float.PositiveInfinity;
        float h = b * b - cc;
        if (h < 0f) return float.PositiveInfinity;
        return -b - MathF.Sqrt(h);
    }

    /// <summary>Distance to the first hit of a ray with a capsule (segment a–b, radius r), or +inf.</summary>
    public static float Capsule(Vector3 o, Vector3 d, Vector3 a, Vector3 b, float r)
    {
        var ba = b - a;
        var oa = o - a;
        float baba = ba.Dot(ba);
        if (baba < 1e-10f) return Sphere(o, d, a, r);
        float bard = ba.Dot(d), baoa = ba.Dot(oa), rdoa = d.Dot(oa), oaoa = oa.Dot(oa);
        float qa = baba - bard * bard;
        float best = float.PositiveInfinity;
        if (qa > 1e-9f)
        {
            float qb = baba * rdoa - baoa * bard;
            float qc = baba * oaoa - baoa * baoa - r * r * baba;
            float h = qb * qb - qa * qc;
            if (h < 0f) return float.PositiveInfinity;
            float t = (-qb - MathF.Sqrt(h)) / qa;
            float y = baoa + t * bard;
            if (y > 0f && y < baba) return t >= 0f ? t : (qc <= 0f ? 0f : float.PositiveInfinity);
        }
        // End caps (also covers rays parallel to the axis).
        float t0 = Sphere(o, d, a, r), t1 = Sphere(o, d, b, r);
        best = MathF.Min(t0, t1);
        return best;
    }
}
