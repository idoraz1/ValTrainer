using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Warmup;

/// <summary>One calibration drill in the ready check: today's latest level and the player's usual band for it.</summary>
public sealed record ReadyDrill(string Mode, float Level, float Mean, float Band, int N)
{
    public float Low => Mean - Band;
    public float High => Mean + Band;
    /// <summary>At or above the lower edge of the usual band (above the band counts too: the warm-up has done its job).</summary>
    public bool InBand => Level >= Low;
}

/// <summary>The outcome of a ready check: whether to offer "skip to deathmatch", why (for the log), the drills compared
/// and which steps the offer would skip (<see cref="FirstSkip"/> up to, not including, the deathmatch at <see cref="DmIndex"/>).</summary>
public sealed class ReadyOffer
{
    public bool Offer;
    public string Reason = "";
    public float PlayedSeconds;
    public readonly List<ReadyDrill> Drills = new();
    public int FirstSkip = -1, DmIndex = -1;
    public int SkipCount => DmIndex - FirstSkip;
}

/// <summary>
/// "Stop when you're ready" (research: warm-up gains level off after 3–5 min of specific practice, and day-to-day levels
/// are very stable). On the cards of the mechanics phase, once the calibration drills are done, the player may skip the
/// rest of the mechanics and go straight to the deathmatch when ALL of these hold:
/// <list type="bullet">
/// <item>at least <see cref="MinPlayMinutes"/> min of drills played today (wall-clock pauses don't count);</item>
/// <item>every adaptive drill so far has a level (none skipped, none with too few shots);</item>
/// <item>for each adaptive drill, today's latest level is at or above the lower edge of the player's usual band:
/// mean of that drill's level in the last <see cref="HistoryWindow"/> Lock-Ins ± a typical error (SD of those levels,
/// at least <see cref="BandFloor"/> and at most <see cref="BandCap"/> level);</item>
/// <item>each of those drills has at least <see cref="MinHistory"/> previous Lock-Ins (fewer: no band, no offer).</item>
/// </list>
/// It is only ever an offer (a secondary button): never automatic, never forced, and the deathmatch always stays the finish.
/// </summary>
public static class LockInReady
{
    // Thresholds (a judgement call; keep them here). The level scale is 0 Rookie … 4 Pro; the staircase's smallest step is
    // 0.2 level, so a band narrower than ~0.3 would be mostly measurement noise.
    public const float MinPlayMinutes = 4f;
    public const int MinHistory = 3;
    public const int HistoryWindow = 10;
    public const float BandFloor = 0.3f, BandCap = 0.75f;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The level a Lock-In record held in this drill: its last adaptive, played step of that drill with a level
    /// (-1 = none). Standard plays flick twice; the later one is the warmed-up level, same as today's "latest".</summary>
    static float LevelIn(WarmupRecord r, string mode) =>
        r.Steps.LastOrDefault(s => s.Adaptive && !s.Skipped && s.Mode == mode && s.Level >= 0)?.Level ?? -1;

    /// <summary>Usual band for a drill from previous Lock-Ins: (mean, half-width, count). Count &lt; <see cref="MinHistory"/>
    /// means there's no band yet.</summary>
    public static (float Mean, float Band, int N) UsualBand(string mode, IEnumerable<WarmupRecord> history)
    {
        var lv = history.Select(r => LevelIn(r, mode)).Where(l => l >= 0).TakeLast(HistoryWindow).ToList();
        if (lv.Count == 0) return (-1, 0, 0);
        float mean = lv.Average();
        float sd = lv.Count > 1 ? MathF.Sqrt(lv.Sum(l => (l - mean) * (l - mean)) / (lv.Count - 1)) : 0;
        return (mean, Math.Clamp(sd, BandFloor, BandCap), lv.Count);
    }

    /// <summary>The ready check for the card before step <c>results.Count</c>. Returns null when the card isn't in the
    /// mechanics phase after calibration (nothing to decide, nothing to log).</summary>
    public static ReadyOffer? Check(WarmupPlan plan, IReadOnlyList<WarmupStepResult> results, IEnumerable<WarmupRecord> history)
    {
        int next = results.Count;
        if (next >= plan.Steps.Count || plan.Steps[next].Phase != Phase.Mechanics) return null;
        if (plan.Steps.Skip(next).Any(s => s.Phase is Phase.Activation or Phase.Calibration)) return null;
        int dm = plan.Steps.FindIndex(next, s => s.Phase == Phase.Deathmatch);
        var o = new ReadyOffer { FirstSkip = next, DmIndex = dm, PlayedSeconds = results.Where(r => !r.Skipped).Sum(r => r.Seconds) };
        if (dm < 0) return Not(o, "no deathmatch in this routine");

        // Quick dev runs play ~8 s instead of 60 s per drill: scale the minimum the same way.
        float minSecs = MinPlayMinutes * 60f * (plan.Quick ? WarmupPlan.QuickStep / 60f : 1f);
        if (o.PlayedSeconds < minSecs) return Not(o, $"played {WarmupPlan.Clock(o.PlayedSeconds)} < {WarmupPlan.Clock(minSecs)}");

        var adaptive = plan.Steps.Take(next).Where(s => s.Adaptive).ToList();
        if (adaptive.Count == 0) return Not(o, "no adaptive drills");
        foreach (var st in adaptive)
        {
            var r = results[st.Index];
            if (r.Skipped || !r.Adaptive || r.Level < 0) return Not(o, $"step {st.Index + 1} {st.Mode} has no level ({(r.Skipped ? "skipped" : "too few trials")})");
        }

        var hist = history.ToList();
        foreach (var mode in adaptive.Select(s => s.Mode).Distinct())
        {
            float level = adaptive.Where(s => s.Mode == mode).Select(s => results[s.Index].Level).Last();
            var (mean, band, n) = UsualBand(mode, hist);
            o.Drills.Add(new ReadyDrill(mode, level, mean, band, n));
        }
        if (o.Drills.FirstOrDefault(d => d.N < MinHistory) is { } few) return Not(o, $"{few.Mode}: {few.N} previous Lock-Ins (< {MinHistory})");
        if (o.Drills.FirstOrDefault(d => !d.InBand) is { } low) return Not(o, $"{low.Mode} below usual band");
        o.Offer = true;
        o.Reason = "all calibration drills at the usual level";
        return o;
    }

    static ReadyOffer Not(ReadyOffer o, string why) { o.Offer = false; o.Reason = why; return o; }

    /// <summary>"flick 2.40 (usual 2.10 ± 0.30, n 5) in band; …" for the log.</summary>
    public static string Describe(ReadyOffer o) => o.Drills.Count == 0 ? "" : string.Join("; ", o.Drills.Select(d =>
        $"{d.Mode} {d.Level.ToString("0.00", Inv)} (usual {(d.N > 0 ? $"{d.Mean.ToString("0.00", Inv)} ± {d.Band.ToString("0.00", Inv)}" : "none")}, n {d.N})" +
        (d.N < MinHistory ? "" : d.Level > d.High ? " above" : d.InBand ? " in band" : " below")));

    /// <summary>Dev "--lockin-ready [level]": five made-up previous Lock-Ins with every adaptive drill of the plan around
    /// <paramref name="level"/> (default 0.5, so a simulated player is at or above it), only for the ready check.</summary>
    public static List<WarmupRecord> FakeHistory(WarmupPlan plan, float level)
    {
        float[] jitter = { -0.2f, 0.1f, 0f, 0.2f, -0.1f };
        var modes = plan.Steps.Where(s => s.Adaptive).Select(s => s.Mode).Distinct().ToList();
        return jitter.Select((j, i) => new WarmupRecord
        {
            When = DateTime.Now.AddDays(i - jitter.Length), Preset = plan.Preset.Key, Completed = true,
            Steps = modes.Select(m => new WarmupStepResult
            {
                Mode = m, Adaptive = true, Level = Math.Clamp(level + j, 0, 4), HitRate = 0.8f, Trials = 80, Phase = Phase.Calibration.ToString(),
            }).ToList(),
        }).ToList();
    }
}
