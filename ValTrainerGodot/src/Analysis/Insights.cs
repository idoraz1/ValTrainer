using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// One engagement of a run (a target or a bot from appearance to hit/kill/expiry) with its timeline, so the Rewind
/// screen can jump to it and the coach can point at it. Times are seconds since the run started (same clock as
/// <see cref="FrameSample.T"/>); NaN = not found. Angles in degrees.
/// </summary>
public sealed class TrialInfo
{
    public int Id;                      // FocusId of the target / bot
    public bool Bot, Static, Predictable, Hit, Head, HasPrimary;
    public float T0;                    // target appeared / bot first seen
    public float TOnset = float.NaN;    // the primary flick starts (reaction = TOnset − T0)
    public float TPrimEnd = float.NaN;  // the primary flick ends (flick time = TPrimEnd − TOnset)
    public float TOnTarget = float.NaN; // the crosshair settles on the target for good (settle = TOnTarget − TPrimEnd)
    public float TShot = float.NaN;     // first shot of this engagement
    public float THit = float.NaN;      // first hit (click delay = THit − TOnTarget)
    public float TEnd;                  // engagement over (hit, kill, expiry or lost sight)
    public float AmpDeg, RadiusDeg;     // flick amplitude and target angular radius
    public float DirDeg = float.NaN;    // flick direction on screen: 0 = right, 90 = up, 180 = left, 270 = down
    public float Gain = float.NaN;      // primary end distance / needed distance (> 1 overshoot)
    public bool Over, Under;            // primary ended beyond / short of the target (static targets only)
    public int Corrections;             // corrective submovements before the hit
    public bool HasFirstShot, FirstHit;
    public float FirstShotErrR = float.NaN; // first bullet's distance from the target centre, in target radii
}

/// <summary>
/// A moment worth showing in the Rewind, linked to a coach problem. <see cref="Code"/> is a <see cref="Diagnosis.Id"/>
/// (e.g. "OVERFLICK", "SLOW_START", "XHAIR_LOW", "SLOW_REACQ") or "MISS" / "KILL" / "DEATH" for plain events.
/// <see cref="Trial"/> is an index into <see cref="RunInsights.Trials"/> (−1 = not tied to an engagement).
/// Built on worker threads: no Godot objects in the builder.
/// </summary>
public sealed record Moment(float T0, float T1, string Code, string Label, int Trial = -1);

/// <summary>Medians of the time-to-hit split (ms), over engagements that ended in a hit. NaN = no data.</summary>
public sealed class TtkBreakdown
{
    public int N;
    public float ReactMs = float.NaN, FlickMs = float.NaN, SettleMs = float.NaN, ClickMs = float.NaN;
    public float TotalMs = float.NaN;
}

/// <summary>One bullet relative to its target, in target radii (+X = right of centre, +Y = above).</summary>
public readonly record struct ScatterShot(float X, float Y, sbyte Zone, float T, bool Moving)
{
    public bool Hit => Zone >= 0;
}

/// <summary>
/// Offline "what went wrong in this run" data for the Rewind screen and the coach's "Show me" links. Built from one
/// run's telemetry only (no network, no stored history). Sector 0 = flicks to the right, counter-clockwise in 45°
/// steps (2 = up, 4 = left, 6 = down).
/// </summary>
public sealed class RunInsights
{
    public RunTelemetry Run = null!;
    public readonly List<TrialInfo> Trials = new();
    public readonly List<Moment> Moments = new();
    public TtkBreakdown Ttk = new();
    public readonly int[] SectorN = new int[8], SectorOver = new int[8], SectorUnder = new int[8];
    public readonly float[] SectorGain = Enumerable.Repeat(float.NaN, 8).ToArray();
    public readonly List<ScatterShot> Scatter = new();
    public int Shots, FirstShots, FirstHits, MovingShots;
    public float FirstBulletAcc => FirstShots > 0 ? (float)FirstHits / FirstShots : float.NaN;
    public float MovingShotPct => Shots > 0 ? 100f * MovingShots / Shots : float.NaN;

    public IEnumerable<Moment> For(string code) => Moments.Where(m => m.Code == code);

    /// <summary>Analyses one run. Never throws: a run without usable frames gives empty insights.</summary>
    public static RunInsights Build(RunTelemetry t)
    {
        var ins = new RunInsights { Run = t };
        // Implemented in InsightsBuilder (analysis work package).
        try { InsightsBuilder.Fill(ins); } catch (Exception ex) { Godot.GD.PushWarning($"Insights failed: {ex.Message}"); }
        return ins;
    }
}

static partial class InsightsBuilder
{
    static partial void FillImpl(RunInsights ins);
    public static void Fill(RunInsights ins) => FillImpl(ins);
}
