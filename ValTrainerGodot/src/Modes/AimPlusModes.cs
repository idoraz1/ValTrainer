using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// Tier parameters of the v1.4 aim drills: Microshot, Target Switch, Pop-up Reflex, and Long-range Taps
/// (the drills live in AimPlus*.cs, their badge thresholds in <see cref="Difficulty"/>). Values per tier:
/// Rookie, Regular, Veteran, Elite, Pro. Calibration notes:
/// <list type="bullet">
/// <item>Heads are 0.14 m like everywhere else: 0.80° angular radius at 10 m, 0.40° at 20 m, 0.20° at 40 m.</item>
/// <item>Times follow a reaction + Fitts model per rank (flick onset 265 → 155 ms, micro-adjust 420 → 165 ms, see
/// docs/coach_spec.md) cross-checked with Aim Lab / KovaaK's numbers: Aim Lab Reflexshot targets live 1 s and
/// Microflex 0.5 s; Diamond players average ≈ 350–375 ms per target in Aim Lab Microshot including the predictable
/// centre returns (≈ 600 ms on the random targets).</item>
/// <item>Pop-up windows run ≈ 700 ms (Rookie) → 320 ms (Pro), plus 12 ms per degree of flick so far pop-ups stay
/// reachable (a Radiant needs ≈ 340 ms for a 5° pop-up, ≈ 430 ms for a 25° one): each rank then hits ≈ 60–75% at its
/// own tier.</item>
/// </list>
/// </summary>
public static class AimPlusTiers
{
    // ---------------- Microshot ----------------
    /// <summary>Distance of the heads (m).</summary>
    public static (float Min, float Max) MicroDistance(int t) => t switch
    { 0 => (10f, 12f), 1 => (11f, 14f), 2 => (12f, 16f), 3 => (14f, 18f), _ => (16f, 20f) };
    /// <summary>Angle between a new head and the previous one (deg).</summary>
    public static (float Min, float Max) MicroOffset(int t) => t switch
    { 0 => (1.5f, 3.5f), 1 => (1.5f, 4f), 2 => (2f, 4.5f), 3 => (2f, 5f), _ => (2.5f, 6f) };
    /// <summary>Seconds before an unhit head moves on (0 = never).</summary>
    public static float MicroLifetime(int t) => Difficulty.T(t, 0f, 0f, 1.6f, 1.3f, 1.1f);
    /// <summary>The heads stay in this patch of the wall (deg from straight ahead): microshots, not flicks.</summary>
    public const float MicroYaw = 16f, MicroPitchMin = -3f, MicroPitchMax = 8f;

    // ---------------- Target Switch ----------------
    public static (float Min, float Max) SwitchDistance(int t) => t switch
    { 0 => (8f, 12f), 1 => (9f, 14f), 2 => (10f, 16f), 3 => (11f, 18f), _ => (12f, 20f) };
    /// <summary>The three agents spread over ± this yaw (deg).</summary>
    public static float SwitchSpread(int t) => Difficulty.T(t, 22f, 26f, 30f, 34f, 38f);
    /// <summary>Strafe speed (m/s): walking at Rookie, jogging at Regular, full running speed above.</summary>
    public static float SwitchSpeed(int t) => Difficulty.T(t, 2.97f, 4.0f, 5.4f, 5.4f, 5.4f);
    public const int SwitchAlive = 3;
    /// <summary>A killed agent is replaced this long after the kill (s).</summary>
    public const float SwitchRespawn = 0.5f;

    // ---------------- Pop-up Reflex ----------------
    /// <summary>How long a head stays up (s) before the flick allowance (≈ 700 ms Rookie → 320 ms Pro).</summary>
    public static float PopupWindow(int t) => Difficulty.T(t, 0.70f, 0.59f, 0.49f, 0.40f, 0.32f);
    /// <summary>Extra time per degree between the crosshair and the pop-up (s/deg).</summary>
    public const float PopupPerDeg = 0.012f;
    public static (float Min, float Max) PopupDistance(int t) => t switch
    { 0 => (8f, 10f), 1 => (9f, 11f), 2 => (10f, 12.5f), 3 => (11f, 14f), _ => (12f, 15f) };
    /// <summary>Pop-ups appear over ± this yaw (deg) and between these pitches: most of the screen while looking ahead.</summary>
    public static float PopupYaw(int t) => Difficulty.T(t, 18f, 22f, 26f, 30f, 34f);
    public static (float Min, float Max) PopupPitch(int t) => t switch
    { 0 => (-4f, 8f), 1 => (-4f, 9f), 2 => (-5f, 10f), 3 => (-5f, 11f), _ => (-6f, 12f) };
    /// <summary>Nearest a pop-up appears to the crosshair (deg).</summary>
    public const float PopupMinAngle = 5f;
    /// <summary>Random pause between pop-ups (s) so they can't be timed.</summary>
    public const float PopupGapMin = 0.45f, PopupGapMax = 1.2f;

    // ---------------- Long-range Taps ----------------
    public static (float Min, float Max) LongDistance(int t) => t switch
    { 0 => (30f, 36f), 1 => (32f, 40f), 2 => (34f, 44f), 3 => (36f, 48f), _ => (38f, 50f) };
    public static float LongSpread(int t) => Difficulty.T(t, 10f, 13f, 16f, 20f, 24f);
    /// <summary>Share of agents that strafe / peek from cover (the rest stand still).</summary>
    public static float LongStrafeShare(int t) => Difficulty.T(t, 0f, 0.25f, 0.35f, 0.40f, 0.40f);
    public static float LongPeekShare(int t) => Difficulty.T(t, 0f, 0f, 0.20f, 0.35f, 0.45f);
    public static float LongStrafeSpeed(int t) => Difficulty.T(t, 2.0f, 2.5f, 2.97f, 5.4f, 5.4f);
    /// <summary>How long a standing / strafing agent stays before it leaves (s).</summary>
    public static float LongLifetime(int t) => Difficulty.T(t, 4.5f, 3.8f, 3.2f, 2.7f, 2.3f);
    /// <summary>How long a peeker holds the angle (s).</summary>
    public static (float Min, float Max) LongPeekHold(int t) => t switch
    { 0 or 1 => (1.3f, 1.8f), 2 => (1.0f, 1.5f), 3 => (0.8f, 1.3f), _ => (0.6f, 1.0f) };
    /// <summary>A shot counts as "reset" when the recoil has less than this left to recover (s).</summary>
    public const float LongResetSlack = 0.05f;


    // ---------------- coach ----------------
    /// <summary>Event and Long-range Taps record when an agent first comes into view (A = crosshair error
    /// in deg, B = pitch error, + = crosshair too high — the same values as bot_seen). Their spawn points are random, so
    /// only the vertical part is crosshair placement: switch this to "bot_seen" once both keys are in
    /// RunAnalysis.HorizExcluded (and SeenBotModes); as bot_seen today they would rate random spawns as pre-aim.</summary>
    public const string SeenEvent = "bot_seen";
}

/// <summary>Angle helpers shared by the v1.4 aim drills (degrees; yaw 0 = -Z, + = right; pitch + = up).</summary>
static class AimPlusMath
{
    public static (float Yaw, float Pitch) Angles(Vector3 from, Vector3 to)
    {
        var d = (to - from).Normalized();
        return (Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1))));
    }

    /// <summary>Crosshair minus point (deg): +x = crosshair right of it, +y = crosshair above it.</summary>
    public static Vector2 CrossMinus(PlayerView v, Vector3 p)
    {
        var (y, pi) = Angles(v.Eye, p);
        return new Vector2(Mathf.Wrap(v.ViewYaw - y, -180f, 180f), v.ViewPitch - pi);
    }

    /// <summary>Angular radius (deg) of a sphere of radius <paramref name="r"/> at distance <paramref name="d"/>.</summary>
    public static float RadiusDeg(float r, float d) => Mathf.RadToDeg(Mathf.Atan2(r, Mathf.Max(0.1f, d)));

    /// <summary>Facing yaw for an agent at <paramref name="feet"/> to look at the player at the origin.</summary>
    public static float FaceOrigin(Vector3 feet) => Mathf.RadToDeg(Mathf.Atan2(-feet.X, feet.Z));

    /// <summary>Crosshair error to a head (deg) and its vertical part (+ = crosshair too high), as recorded by bot_seen.</summary>
    public static (float Err, float Pitch) SeenError(PlayerView v, Vector3 head)
    {
        var d = (head - v.Eye).Normalized();
        float pitchT = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        return (v.AngleTo(head), v.ViewPitch - pitchT);
    }
}
