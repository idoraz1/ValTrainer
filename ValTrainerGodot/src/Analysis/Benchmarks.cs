using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>
/// Rank benchmarks (spec B): typical values per tier b0…b4 (Iron–Bronze, Silver–Gold, Plat–Dia, Asc–Imm, Radiant)
/// and piecewise-linear tier scoring. Values marked † in the spec are estimates to tune on playtest data.
/// </summary>
static class Bench
{
    public static readonly float[] OnsetMs = { 330, 295, 265, 240, 220 };
    public static readonly float[] EndErrPct = { 22, 16, 12, 9, 7 };
    public static readonly float[] FirstShotHitPct = { 62, 70, 78, 85, 90 };
    public static readonly float[] TcMs = { 420, 330, 260, 205, 165 };
    public static readonly float[] Nc = { 2.4f, 2.0f, 1.7f, 1.4f, 1.2f };
    public static readonly float[] TrackOnPct = { 25, 35, 45, 55, 65 };   // vs a Veteran-tier target
    public static readonly float[] TauMs = { 230, 190, 160, 135, 115 };
    public static readonly float[] ReacqMs = { 480, 400, 330, 270, 220 };
    public static readonly float[] Jitter = { 0.9f, 0.7f, 0.55f, 0.45f, 0.35f };
    public static readonly float[] ReactMs = { 300, 265, 230, 200, 175 };
    public static readonly float[] XhairErrDeg = { 9, 6, 3.5f, 1.8f, 0.8f };
    public static readonly float[] PitchErrDeg = { -3.0f, -1.8f, -1.0f, -0.5f, -0.25f };
    public static readonly float[] SprayV = { 1.8f, 1.2f, 0.8f, 0.5f, 0.3f };
    public static readonly float[] SprayH = { 1.2f, 0.9f, 0.65f, 0.45f, 0.3f };
    public static readonly float[] CompDelayMs = { 450, 330, 250, 190, 150 };
    public static readonly float[] MovingShotPct = { 35, 22, 12, 6, 3 };
    public static readonly float[] CounterPct = { 10, 30, 55, 75, 90 };
    public static readonly float[] StopMs = { 260, 220, 190, 170, 160 };
    public static readonly float[] FlashTurnMs = { 650, 520, 420, 340, 280 };
    /// <summary>† not in the spec table: Fitts movement time (onset → hit) at ID 4.7 bits.</summary>
    public static readonly float[] MtRefMs = { 720, 600, 500, 420, 360 };
    /// <summary>† not in the spec table: flash outcome score (dodged = 100, partial = 50, flashed = 0).</summary>
    public static readonly float[] FlashDodgePct = { 30, 45, 60, 75, 88 };

    /// <summary>
    /// Our stop event measures key release → accurate speed (Riot's ≈105 ms floor), the spec's table is the full
    /// stop (≈160 ms floor): add this before scoring / thresholds.
    /// </summary>
    public const float StopOffsetMs = 55f;

    /// <summary>T(x) = i + (x − bᵢ)/(bᵢ₊₁ − bᵢ), extrapolated up to 0.5 beyond either end, clamped to [−0.5, 4.5].</summary>
    public static float TierOf(float x, float[] b)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) return float.NaN;
        float dir = MathF.Sign(b[^1] - b[0]);
        if ((x - b[0]) * dir <= 0) return Math.Max(-0.5f, (x - b[0]) / (b[1] - b[0]));
        if ((x - b[^1]) * dir >= 0) return Math.Min(4.5f, b.Length - 1 + (x - b[^1]) / (b[^1] - b[^2]));
        for (int i = 0; i < b.Length - 1; i++)
        {
            float lo = b[i], hi = b[i + 1];
            if ((x - lo) * dir >= 0 && (x - hi) * dir <= 0) return i + (x - lo) / (hi - lo);
        }
        return float.NaN;
    }

    /// <summary>Benchmark value at a continuous tier (clamped to 0…4).</summary>
    public static float ValueAt(float tier, float[] b)
    {
        if (float.IsNaN(tier)) tier = 2;
        tier = Math.Clamp(tier, 0, b.Length - 1);
        int i = Math.Min((int)tier, b.Length - 2);
        return b[i] + (b[i + 1] - b[i]) * (tier - i);
    }

    public static readonly string[] RankNames = { "Iron", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Ascendant", "Immortal", "Radiant" };

    /// <summary>Iron &lt; 0 ≤ Bronze &lt; 0.5 ≤ Silver &lt; 1 ≤ Gold &lt; 1.5 ≤ Platinum &lt; 2 ≤ Diamond &lt; 2.5 ≤ Ascendant &lt; 3 ≤ Immortal &lt; 3.5 ≤ Radiant.</summary>
    public static string Rank(float t)
    {
        if (float.IsNaN(t) || t < -0.75f) return "Unrated";
        if (t < 0) return "Iron";
        int i = Math.Min(8, 1 + (int)(t / 0.5f));
        return RankNames[i];
    }

    public static string TierName(int tier) => Difficulty.Get(tier).Name;
}
