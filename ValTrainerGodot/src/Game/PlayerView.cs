using Godot;

namespace ValTrainer.Game;

/// <summary>
/// First-person view math using Valorant's exact numbers: 0.07° of yaw per mouse count × sensitivity,
/// 103° horizontal FOV. Zoomed sens is scaled by multiplier / zoom. Yaw 0 looks down -Z, positive yaw turns right.
/// </summary>
public sealed class PlayerView
{
    public const float HipHFov = 103f;
    public const float DegPerCount = 0.07f;
    public const float EyeHeight = 1.65f; // eye level == bot head centre

    public Vector3 Eye = new(0, EyeHeight, 0);
    public float Yaw, Pitch;
    public float Zoom = 1f;

    /// <summary>Recoil currently applied to the view (degrees). Added to the look angles.</summary>
    public float RecoilPitch, RecoilYaw;

    public void Look(Vector2 counts, float sens, float zoomMult)
    {
        float k = DegPerCount * sens * (Zoom > 1f ? zoomMult / Zoom : 1f);
        if (!float.IsFinite(counts.X * k) || !float.IsFinite(counts.Y * k)) return; // a NaN view would stick for good
        Yaw += counts.X * k;
        Pitch = Mathf.Clamp(Pitch - counts.Y * k, -89f, 89f);
        if (Yaw > 180f) Yaw -= 360f;
        if (Yaw < -180f) Yaw += 360f;
    }

    public float ViewYaw => Yaw + RecoilYaw;
    public float ViewPitch => Mathf.Clamp(Pitch + RecoilPitch, -89f, 89f);
    public Vector3 Forward => Dir(ViewYaw, ViewPitch);

    public static Vector3 Dir(float yawDeg, float pitchDeg)
    {
        float y = Mathf.DegToRad(yawDeg), p = Mathf.DegToRad(pitchDeg);
        return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
    }

    /// <summary>Horizontal FOV at the current zoom (degrees).</summary>
    public float HFov => Mathf.RadToDeg(2f * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(HipHFov / 2f)) / Zoom));

    public float AngleTo(Vector3 target)
    {
        var d = (target - Eye).Normalized();
        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(d.Dot(Forward), -1f, 1f)));
    }

    /// <summary>Random direction within <paramref name="deg"/> of <paramref name="dir"/>.
    /// Moving shots are uniform in the cone; standing shots bunch toward the centre.</summary>
    public static Vector3 Spread(Vector3 dir, float deg, Random rng, bool uniform)
    {
        if (deg <= 0) return dir;
        var right = dir.Cross(Vector3.Up).Normalized();
        if (right.LengthSquared() < 1e-6f) right = Vector3.Right;
        var up = right.Cross(dir);
        float u = (float)rng.NextDouble();
        float a = (float)(rng.NextDouble() * Math.PI * 2), r = Mathf.Tan(Mathf.DegToRad(deg)) * (uniform ? Mathf.Sqrt(u) : u * u);
        return (dir + right * Mathf.Cos(a) * r + up * Mathf.Sin(a) * r).Normalized();
    }

    public static float Cm360(float sens, int dpi) => sens <= 0 || dpi <= 0 ? 0 : 360f / (DegPerCount * sens) / dpi * 2.54f;
}
