using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>Sens Finder protocol parameters (docs/coach_spec.md §E). <see cref="Quick"/> is the dev/test configuration.</summary>
public sealed class SensFinderConfig
{
    /// <summary>Multiplicative half-widths per step: L = C / hw, H = C · hw (h = ln hw).</summary>
    public float[] HalfWidths = { 1.40f, 1.25f, 1.15f, 1.09f, 1.05f };
    /// <summary>Unscored flicks at the start of every block (on top of the scored set, so both settings get the identical set).</summary>
    public int WarmupFlicks = 2;
    public int MicroFlicks = 4, MediumFlicks = 4, LargeFlicks = 4;
    public float FlickTimeout = 2.0f;
    /// <summary>Blank gap between a hit and the next target (lets the hand settle so movement onset is clean).</summary>
    public float Foreperiod = 0.3f;
    public float TrackReady = 1.0f, TrackSec = 10f, TrackWarmup = 2f, TrackWindow = 2f;
    public float RestSec = 5f;
    public int BlocksPerStage = 4;
    public int MaxTies = 3;
    public float MinAccuracy = 0.5f;
    /// <summary>Head: r = 0.5° at 16 m.</summary>
    public const float FlickDist = 16f, TrackDist = 11f;
    public static readonly float FlickRadius = FlickDist * Mathf.Tan(Mathf.DegToRad(0.5f));
    public const int TrackTier = 2; // Veteran strafe script

    public int Steps => HalfWidths.Length;
    public int ScoredFlicks => MicroFlicks + MediumFlicks + LargeFlicks;
    public int FlicksPerBlock => WarmupFlicks + ScoredFlicks;

    /// <summary>Rough wall-clock estimate of a full run (all steps + confirmation), minutes.</summary>
    public float EstimatedMinutes => (Steps + 1) * BlocksPerStage * (RestSec + FlicksPerBlock * (Foreperiod + 0.95f) + TrackReady + TrackSec) / 60f;

    public static SensFinderConfig Full() => new();

    /// <summary>Dev/test (--sfquick): 3 steps, short blocks.</summary>
    public static SensFinderConfig Quick() => new()
    {
        HalfWidths = new[] { 1.40f, 1.25f, 1.15f },
        WarmupFlicks = 1, MicroFlicks = 2, MediumFlicks = 2, LargeFlicks = 2,
        TrackReady = 0.6f, TrackSec = 5f, TrackWarmup = 1f, TrackWindow = 1.33f,
        RestSec = 1.5f,
    };
}

/// <summary>One flick of the standard set: amplitude (deg) and direction elevation |φ| (deg, 0 = horizontal).
/// The left/right and up/down signs are chosen per presentation (mirroring).</summary>
public readonly record struct SfFlick(float Amp, float Elev)
{
    /// <summary>The step's flick set: micro 3–8°, medium 8–20°, large 20–45° (stratified within each class).</summary>
    public static List<SfFlick> MakeSet(SensFinderConfig cfg, Random rng)
    {
        var list = new List<SfFlick>();
        void Class(int n, float lo, float hi, float maxElev)
        {
            for (int i = 0; i < n; i++)
            {
                float a = lo + (hi - lo) * (i + (float)rng.NextDouble()) / n;
                list.Add(new SfFlick(a, (float)rng.NextDouble() * maxElev));
            }
        }
        Class(cfg.MicroFlicks, 3f, 8f, 40f);
        Class(cfg.MediumFlicks, 8f, 20f, 35f);
        Class(cfg.LargeFlicks, 20f, 45f, 20f);
        return list;
    }
}

/// <summary>
/// A seeded A-D strafe trajectory (same rules as <see cref="TrackingMode"/> at the given tier: random direction changes,
/// stops, short crouches, 34 m/s² decel), precomputed at 240 Hz so both settings see exactly the same motion.
/// </summary>
public sealed class SfStrafeScript
{
    const float Hz = 240f;
    readonly float[] x;
    readonly bool[] crouch;

    public SfStrafeScript(int tier, float duration, int seed)
    {
        var rng = new Random(seed);
        int n = (int)(duration * Hz) + 4;
        x = new float[n];
        crouch = new bool[n];
        float px = 0, vx = 0, targetVx = 0, nextTurn = 0, crouchT = 0, dt = 1f / Hz;
        for (int i = 0; i < n; i++)
        {
            x[i] = px;
            crouch[i] = crouchT > 0;
            nextTurn -= dt;
            if (nextTurn <= 0)
            {
                var (a, b) = Difficulty.TurnInterval(tier);
                float speed = Difficulty.TrackSpeed(tier), stop = Difficulty.StopChance(tier);
                targetVx = rng.NextDouble() < stop ? 0 : (rng.Next(2) == 0 ? -1 : 1) * speed;
                if (rng.NextDouble() < stop * 0.5) crouchT = 0.5f;
                nextTurn = a + (float)rng.NextDouble() * (b - a);
            }
            vx = Mathf.MoveToward(vx, targetVx, Difficulty.StrafeDecel * dt);
            px += vx * dt;
            if (Mathf.Abs(px) > 6) { px = Mathf.Sign(px) * 6; targetVx = -targetVx; vx = 0; }
            crouchT -= dt;
        }
    }

    /// <summary>Lateral offset (m) and crouch at time t (s).</summary>
    public (float X, bool Crouch) At(float t)
    {
        float f = Mathf.Clamp(t * Hz, 0, x.Length - 1.001f);
        int i = (int)f;
        return (Mathf.Lerp(x[i], x[i + 1], f - i), crouch[i]);
    }
}

/// <summary>One block (one setting): trial-level scores.</summary>
public sealed class SfBlock
{
    public int Cond;            // 0 = lower sens (L) / new C in confirmation, 1 = higher sens (H) / current S
    public float Sens;
    public char Label;          // what the player sees: 'A' or 'B'
    public bool Mirror;         // tracking script mirrored
    public readonly List<double> FlickZ = new();      // ln(ID / MT') per scored flick
    public readonly List<double> TrackLogit = new();  // logit(on-target) per scored tracking window
    public readonly List<float> FlickMt = new();      // MT' (s), for logs
    public float TrackOn, TrackTotal;                 // scored tracking time on target / total (s)
    public int Hits, Attempts, Timeouts;
    public double Score;        // standardized composite (filled when the stage is scored)
}

/// <summary>A step of the search (L vs H) or the final confirmation (new C vs current S): 4 blocks ABBA / BAAB.</summary>
public sealed class SfStage
{
    public bool Confirm;
    public int Step;            // 0-based step index (confirmation: -1)
    public float Centre, HalfWidth; // C and the multiplicative half-width (1.40, 1.25, …)
    public readonly float[] Sens = new float[2];
    public readonly char[] Label = new char[2];
    public int[] Order = new int[4]; // condition per block
    public readonly List<SfBlock> Blocks = new();
    public List<SfFlick> Flicks = new();
    public SfStrafeScript Script = null!;
    public SfComparison? Result;
    public int Winner = -1;     // 0 / 1 / -1 tie
    public float NewCentre;

    public float Accuracy
    {
        get
        {
            int h = Blocks.Sum(b => b.Hits), a = Blocks.Sum(b => b.Attempts);
            return a == 0 ? 1f : (float)h / a;
        }
    }
}

/// <summary>Outcome of comparing condition 0 vs condition 1 (d = mean0 − mean1, in within-stage SD units).</summary>
public sealed record SfComparison(double D, double Se, double T, double DFlick, double SeFlick, double DTrack, double SeTrack, int NFlick0, int NFlick1, int NTrack0, int NTrack1);

public static class SfStats
{
    public const double WFlick = 0.65, WTrack = 0.35;

    public static double Mean(IReadOnlyList<double> v) => v.Count == 0 ? 0 : v.Average();

    /// <summary>Sample variance (n − 1).</summary>
    public static double Var(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        double m = Mean(v), s = 0;
        foreach (var x in v) s += (x - m) * (x - m);
        return s / (v.Count - 1);
    }

    /// <summary>Welch: difference of means and its standard error.</summary>
    public static (double D, double Se) Welch(IReadOnlyList<double> a, IReadOnlyList<double> b) =>
        (Mean(a) - Mean(b), Math.Sqrt(Var(a) / Math.Max(1, a.Count) + Var(b) / Math.Max(1, b.Count)));

    public static double Logit(double p)
    {
        p = Math.Clamp(p, 0.02, 0.98);
        return Math.Log(p / (1 - p));
    }

    /// <summary>Fitts index of difficulty (bits) for amplitude A and target angular radius r.</summary>
    public static double Id(double amp, double r) => Math.Log2(amp / (2 * r) + 1);

    /// <summary>
    /// Scores a stage: flick z and tracking logits are standardized within the stage (pooled over both conditions),
    /// combined 0.65 / 0.35, and compared with Welch's SE on trial-level scores
    /// (SE² = 0.65²·SE²_flick + 0.35²·SE²_track). Also fills each block's composite score.
    /// </summary>
    public static SfComparison Compare(IReadOnlyList<SfBlock> blocks)
    {
        var (fm, fs) = Moments(blocks.SelectMany(b => b.FlickZ));
        var (tm, ts) = Moments(blocks.SelectMany(b => b.TrackLogit));
        List<double> F(int c) => blocks.Where(b => b.Cond == c).SelectMany(b => b.FlickZ).Select(z => (z - fm) / fs).ToList();
        List<double> T(int c) => blocks.Where(b => b.Cond == c).SelectMany(b => b.TrackLogit).Select(z => (z - tm) / ts).ToList();
        var f0 = F(0); var f1 = F(1); var t0 = T(0); var t1 = T(1);
        bool hasF = f0.Count >= 2 && f1.Count >= 2, hasT = t0.Count >= 2 && t1.Count >= 2;
        double wf = hasF ? WFlick : 0, wt = hasT ? WTrack : 0, sum = wf + wt;
        if (sum <= 0) return new SfComparison(0, 0, 0, 0, 0, 0, 0, f0.Count, f1.Count, t0.Count, t1.Count);
        wf /= sum; wt /= sum;
        var (df, sf) = hasF ? Welch(f0, f1) : (0, 0);
        var (dt, st) = hasT ? Welch(t0, t1) : (0, 0);
        double d = wf * df + wt * dt, se = Math.Sqrt(wf * wf * sf * sf + wt * wt * st * st);
        double t = se > 1e-9 ? d / se : 0;
        foreach (var b in blocks)
        {
            double bf = b.FlickZ.Count > 0 ? b.FlickZ.Average(z => (z - fm) / fs) : 0;
            double bt = b.TrackLogit.Count > 0 ? b.TrackLogit.Average(z => (z - tm) / ts) : 0;
            b.Score = wf * bf + wt * bt;
        }
        return new SfComparison(d, se, t, df, sf, dt, st, f0.Count, f1.Count, t0.Count, t1.Count);
    }

    static (double Mean, double Sd) Moments(IEnumerable<double> v)
    {
        var a = v.ToList();
        double sd = Math.Sqrt(Var(a));
        return (Mean(a), sd > 1e-9 ? sd : 1);
    }
}

/// <summary>Valorant sensitivity ↔ cm/360 at a DPI (0.07° per count × sens).</summary>
public static class SfUnits
{
    public static float Cm360(float sens, int dpi) => PlayerView.Cm360(sens, dpi);
    public static float SensForCm(float cm, int dpi) => cm <= 0 || dpi <= 0 ? 0 : 360f / PlayerView.DegPerCount / dpi * 2.54f / cm;
    public static float ClampCm(float sens, int dpi, float lo, float hi)
    {
        float cm = Cm360(sens, dpi);
        return cm < lo ? SensForCm(lo, dpi) : cm > hi ? SensForCm(hi, dpi) : sens;
    }
}
