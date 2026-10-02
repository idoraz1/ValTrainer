using Godot;

namespace ValTrainer.Game.Fx;

public enum FlashPath { Curve, ThroughWall, Arc, Hawk, Bounce, Placed, Crawl }
public enum BlindGrade { Dodged, Partial, Flashed, NoLineOfSight }

/// <summary>
/// Per-agent flash data (official wiki / patch notes to 13.06; flight speeds estimated).
/// Fuse = release (or bounce / recast / wall contact) → detonation. Cue = synthesized sound key.
/// </summary>
public sealed record FlashAgent(string Agent, string Ability, float Fuse, float BlindMax, FlashPath Path, float Speed, Color Color, string Cue)
{
    public static readonly FlashAgent[] All =
    {
        new("Phoenix", "Curveball", 0.6f, 1.5f, FlashPath.Curve, 15f, Color.Color8(255, 150, 50), "phoenix_wind"),
        new("Breach", "Flashpoint", 0.5f, 2.25f, FlashPath.ThroughWall, 24f, Color.Color8(255, 190, 60), "breach_wind"),
        new("KAY/O", "FLASH/drive (underhand)", 1.0f, 2.25f, FlashPath.Arc, 7f, Color.Color8(90, 220, 255), "kayo_wind"),
        new("KAY/O", "FLASH/drive (overhand)", 1.6f, 2.25f, FlashPath.Arc, 12f, Color.Color8(90, 220, 255), "kayo_wind"),
        new("Skye", "Guiding Light", 0.3f, 2.25f, FlashPath.Hawk, 18f, Color.Color8(120, 230, 120), "skye_wind"),
        new("Yoru", "Blindside", 0.6f, 1.5f, FlashPath.Bounce, 20f, Color.Color8(90, 140, 255), "yoru_wind"),
        new("Vyse", "Arc Rose", 0.5f, 2.0f, FlashPath.Placed, 0f, Color.Color8(180, 90, 255), "vyse_wind"),
        new("Gekko", "Dizzy", 1.0f, 1.0f, FlashPath.Crawl, 6f, Color.Color8(200, 80, 220), "gekko_wind"),
    };
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
