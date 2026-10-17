using Godot;

namespace ValTrainer.Game.Fx;

public enum FlashPath { Curve, ThroughWall, Arc, Hawk, Bounce, Placed, Dizzy }
public enum BlindGrade { Dodged, Partial, Flashed, NoLineOfSight }

/// <summary>
/// Per-agent flash data (VALORANT wiki / patch notes to 13.08; research notes in the support folder's
/// research\flashes.md). Fuse = release (or first bounce / recast / wall exit / reactivation) → detonation; for KAY/O's
/// overhand it is the latest pop (a bounce starts a 0.8 s windup, never past 1.6 s). Speed in m/s (Phoenix's isn't
/// published: estimate). Cue = the older synthesized windup sound (other drills); Slug = the per-agent sound set
/// (&lt;slug&gt;_throw / _flight / _windup / _pop, see Sfx). Gekko's Dizzy isn't a flash: see <see cref="DizzySpec"/>.
/// </summary>
public sealed record FlashAgent(string Agent, string Ability, float Fuse, float BlindMax, FlashPath Path, float Speed, Color Color, string Cue)
{
    public static readonly FlashAgent[] All =
    {
        new("Phoenix", "Curveball", 0.6f, 1.5f, FlashPath.Curve, 15f, Color.Color8(255, 150, 50), "phoenix_wind"),
        new("Breach", "Flashpoint", 0.5f, 2.25f, FlashPath.ThroughWall, 24f, Color.Color8(255, 190, 60), "breach_wind"),
        new("KAY/O", "FLASH/drive (underhand)", 1.0f, 2.25f, FlashPath.Arc, 6.3f, Color.Color8(90, 220, 255), "kayo_wind"),
        new("KAY/O", "FLASH/drive (overhand)", 1.6f, 2.25f, FlashPath.Arc, 18f, Color.Color8(90, 220, 255), "kayo_wind"),
        new("Skye", "Guiding Light", 0.3f, 2.25f, FlashPath.Hawk, 18f, Color.Color8(120, 230, 120), "skye_wind"),
        new("Yoru", "Blindside", 0.6f, 1.5f, FlashPath.Bounce, 29f, Color.Color8(90, 140, 255), "yoru_wind"),
        new("Vyse", "Arc Rose", 0.5f, 2.0f, FlashPath.Placed, 0f, Color.Color8(180, 90, 255), "vyse_wind"),
        new("Gekko", "Dizzy", 1.0f, 1.0f, FlashPath.Dizzy, 18f, Color.Color8(29, 199, 254), "gekko_wind"),
    };

    /// <summary>Sound-set prefix: phoenix, breach, kayo, skye, yoru, vyse, dizzy.</summary>
    public string Slug => Agent switch { "KAY/O" => "kayo", "Gekko" => "dizzy", _ => Agent.ToLowerInvariant() };

    /// <summary>Length of the audible / visible windup before the pop (its riser ends on the pop): the whole Curveball
    /// flight, KAY/O's last 0.3 s (0.8 s after a bounce), Skye's recast, Yoru after the bounce, Breach / Vyse's charge.</summary>
    public float WindupSeconds => Agent switch { "KAY/O" => 0.3f, "Skye" => 0.3f, _ => Fuse };

    /// <summary>Destructible while winding up: Vyse's Arc Rose (20 HP, shootable once its windup starts, patch 11.08).</summary>
    public bool Shootable => Agent == "Vyse";
}

/// <summary>
/// Gekko's Dizzy (patch 13.08): thrown at 18 m/s with gravity ×0.3, forms after ~0.65 s (wiki estimate), then is active
/// for 1.0 s (9.08) while she slows to a hover. She fires 0.35 s after she sees an enemy within 45 m (7.12): one plasma
/// blast per enemy, 100 m/s, 2.5 m splash. A hit blinds for 1 s + a 1 s fade, whichever way you face. 20 HP, so any
/// bullet kills her, and a dead Dizzy never fires. Her drift speed while active isn't published (estimate).
/// </summary>
public static class DizzySpec
{
    public const float Speed = 18f, Gravity = 9.81f * 0.3f, FormAt = 0.65f, Active = 1.0f, LockDelay = 0.35f;
    public const float Range = 45f, PlasmaSpeed = 100f, Splash = 2.5f, BlindFull = 1.0f, Hp = 20f, Radius = 0.3f;
    public const float DriftStart = 3f; // m/s at forming, easing to a hover over the active second [estimate]
}

/// <summary>
/// Valorant blind rule: what matters is where the pop is ON SCREEN (horizontal half-FOV 51.5°, vertical
/// ≈35.4° at 16:9), not a cone around the crosshair; distance weakens it; facing away still gives a short
/// minimum blind; no line of sight = no blind. The falloff-curve shape and off-screen minimum are estimates.
/// </summary>
public static class BlindModel
{
    public const float OffscreenMinimum = 0.25f;
    public const float FadeTime = 1.0f;
    const float OrbSize = 0.08f;

    /// <summary>0 = screen centre, 1 = screen edge, &gt;1 off screen (behind = large).</summary>
    public static float Eccentricity(PlayerView v, Vector3 point, float aspect)
    {
        var fwd = v.Forward;
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd);
        var d = point - v.Eye;
        float z = d.Dot(fwd);
        if (z <= 0.01f) return 9f;
        float tanH = Mathf.Tan(Mathf.DegToRad(v.HFov / 2f)), tanV = tanH / aspect;
        float sx = Mathf.Abs(d.Dot(right) / z) / tanH;
        float sy = Mathf.Abs(d.Dot(up) / z) / tanV;
        return Mathf.Max(sx, sy) - OrbSize;
    }

    /// <summary>Where a world point lands on screen in UV (0..1 inside; outside when off-screen or behind, pushed out
    /// along its direction from the screen centre), for the blind's star-burst.</summary>
    public static Vector2 ScreenUv(PlayerView v, Vector3 point, float aspect)
    {
        var fwd = v.Forward;
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd);
        var d = point - v.Eye;
        float x = d.Dot(right), y = d.Dot(up), z = d.Dot(fwd);
        float tanH = Mathf.Tan(Mathf.DegToRad(v.HFov / 2f)), tanV = tanH / aspect;
        if (z <= 0.05f)
        {
            // behind: well outside the screen on the side it lies (straight behind = below)
            var dir = new Vector2(x, -y);
            dir = dir.LengthSquared() < 1e-6f ? new Vector2(0, 1) : dir.Normalized();
            return new Vector2(0.5f, 0.5f) + dir * 1.5f;
        }
        return new Vector2(0.5f + 0.5f * x / (z * tanH), 0.5f - 0.5f * y / (z * tanV));
    }

    public static (float seconds, BlindGrade grade) Evaluate(float blindMax, PlayerView v, Vector3 pop, bool lineOfSight, float aspect)
    {
        if (!lineOfSight) return (0f, BlindGrade.NoLineOfSight);
        float e = Eccentricity(v, pop, aspect);
        float dist = v.Eye.DistanceTo(pop);
        float fD = dist <= 12f ? 1f : dist >= 50f ? 0f : dist <= 35f ? 1f - 0.6f * (dist - 12f) / 23f : 0.4f * (50f - dist) / 15f;
        if (fD <= 0f) return (0f, BlindGrade.Dodged);
        if (e > 1f) return (OffscreenMinimum, BlindGrade.Dodged);
        float fA = e <= 0.5f ? 1f : 1f - (e - 0.5f);
        float s = Mathf.Max(OffscreenMinimum, blindMax * fA * fD);
        return (s, e <= 0.5f ? BlindGrade.Flashed : BlindGrade.Partial);
    }
}
