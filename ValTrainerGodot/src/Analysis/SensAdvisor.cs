using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Spec D: sensitivity recommendation from aim signals (H &gt; 0 = sens too high), with guardrails: per-signal
/// confidence, ±18% max step, session consistency, 20–80 cm/360 preferred band (15–100 hard), and a cool-down
/// of 5 sessions after a sens change.
/// </summary>
static class SensAdvisor
{
    public const double CmK = 360.0 / PlayerView.DegPerCount * 2.54; // cm/360 = CmK / (sens · dpi) ≈ 13063
    const double SessionGapHours = 0.75;

    public static float Cm360(float sens, int dpi) => sens <= 0 || dpi <= 0 ? 0 : (float)(CmK / (sens * dpi));
    public static float SensFor(float cm, int dpi) => (float)(CmK / (cm * dpi));

    /// <summary>Share of pros (prosettings.net, median eDPI 240, lognormal σ ≈ 0.38) with a LOWER eDPI than this.</summary>
    public static float ProPercentile(float edpi) => edpi <= 0 ? 0 : (float)(100 * Dsp.Phi(Math.Log(edpi / 240.0) / 0.38));

    public sealed record Signal(string Key, float H, float W, float C, string Words);

    static float V(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) ? v : float.NaN;
    static float N(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) && !float.IsNaN(v) ? v : 0;
    static string F(FormattableString s) => FormattableString.Invariant(s);
    static float Clip(float x) => Math.Clamp(x, -1f, 1f);
    static float Conf(float n, float nMin) => n <= 0 ? 0 : n / (n + nMin);

    /// <summary>The H₁…H₅ signals that pass their confidence gate (c ≥ 0.5) and applicability conditions.</summary>
    public static List<Signal> Signals(Dictionary<string, float> m, float refTier)
    {
        var s = new List<Signal>();
        float bandNc = Bench.ValueAt(refTier, Bench.Nc), bandJ = Bench.ValueAt(refTier, Bench.Jitter), bandTau = Bench.ValueAt(refTier, Bench.TauMs);

        float O = V(m, "flick.overshoot_share"), cO = Conf(N(m, "flick.n_off"), 30);
        if (!float.IsNaN(O) && cO >= 0.5f)
            s.Add(new Signal("H1", Clip((O - 0.40f) / 0.25f), 0.30f, cO,
                O >= 0.4f ? F($"{O * 100:0}% of your off-target flicks overshoot")
                : O < 0.005f ? "none of your off-target flicks overshoot (they all stop short)"
                : F($"only {O * 100:0}% of your off-target flicks overshoot (most stop short)")));

        bool head = N(m, "head.n_hit") >= 5;
        float nc = head ? V(m, "head.nc") : V(m, "flick.nc"), cNc = Conf(head ? N(m, "head.n_hit") : N(m, "flick.n_hit"), 30);
        float amp = V(m, "flick.corr_amp_r");
        if (!float.IsNaN(nc) && cNc >= 0.5f && amp < 2f)
            s.Add(new Signal("H2", Clip((nc - bandNc) / 0.8f), 0.20f, cNc,
                nc >= bandNc ? F($"you make {nc:0.0} small corrections per flick (typical {bandNc:0.0})") : F($"you need few corrections per flick ({nc:0.0})")));

        float J = V(m, "track.jitter"), cT = Math.Min(Conf(N(m, "track.secs"), 60), Conf(N(m, "track.reversals"), 10));
        if (!float.IsNaN(J) && cT >= 0.5f)
            s.Add(new Signal("H3", Clip((J - bandJ) / bandJ), 0.20f, cT,
                J > bandJ ? F($"your tracking is shaky (jitter {J:0.00} vs {bandJ:0.00})") : F($"your tracking is smooth (jitter {J:0.00})")));

        float gs = V(m, "flick.gain_small"), gl = V(m, "flick.gain_large");
        float cG = Math.Min(Conf(N(m, "flick.n_small"), 30), Conf(N(m, "flick.n_large"), 15));
        if (!float.IsNaN(gs) && !float.IsNaN(gl) && gl < 0.90f && cG >= 0.5f)
            s.Add(new Signal("H4", Clip(-(gs - gl - 0.05f) / 0.10f), 0.20f, cG,
                gs - gl > 0.05f ? F($"wide flicks (25°+) fall short ({gl * 100:0}% of the distance vs {gs * 100:0}% on small ones)") : F($"wide and small flicks land alike ({gl * 100:0}% vs {gs * 100:0}%)")));

        float tau = V(m, "track.tau_ms");
        if (!float.IsNaN(tau) && !float.IsNaN(J) && J <= bandJ && cT >= 0.5f)
            s.Add(new Signal("H5", Clip(-(tau - bandTau) / 80f), 0.10f, cT,
                tau > bandTau ? F($"your tracking lags the target ({tau:0} ms vs {bandTau:0} ms)") : F($"your tracking reacts quickly ({tau:0} ms)")));
        return s;
    }

    /// <summary>Δ (log step, S′ = S·e^Δ) from a metric dictionary; NaN when no signal is usable.</summary>
    public static (float Delta, float H, float CBar, List<Signal> Used) Step(Dictionary<string, float> m, float refTier)
    {
        var sig = Signals(m, refTier);
        if (sig.Count == 0) return (float.NaN, 0, 0, sig);
        float h = sig.Sum(x => x.W * x.H), c = sig.Average(x => x.C);
        return (-0.18f * h * c, h, c, sig);
    }

    public sealed record RunInfo(DateTime When, float Sens, Dictionary<string, float> M);
    /// <summary>Latest Sens Finder recommendation (confidence 0 low, 1 medium, 2 high).</summary>
    public sealed record FinderResult(DateTime When, float Sens, int Confidence);

    public static SensAdvice Advise(List<RunInfo> runs, float currentSens, int dpi, float refTier, FinderResult? finder = null)
    {
        var a = AdviseCore(runs, currentSens, dpi, refTier);
        if (finder == null || currentSens <= 0) return a;
        string label = finder.Confidence >= 2 ? "high" : finder.Confidence == 1 ? "medium" : "low";
        string note = MathF.Abs(finder.Sens - currentSens) / currentSens <= 0.03f
            ? F($" You're on your Sens Finder result ({finder.When:MMM d}).")
            : F($" Your Sens Finder run on {finder.When:MMM d} suggested {finder.Sens:0.000} ({Cm360(finder.Sens, a.Dpi):0.0} cm/360, {label} confidence).");
        return a with { Reason = a.Reason + note };
    }

    static SensAdvice AdviseCore(List<RunInfo> runs, float currentSens, int dpi, float refTier)
    {
        dpi = dpi > 0 ? dpi : 800;
        float cm = Cm360(currentSens, dpi);
        SensAdvice Keep(string reason, float conf = 0) => new(currentSens, dpi, currentSens, cm, cm, "keep", reason, conf);
        if (currentSens <= 0) return Keep("Set your sensitivity first.");
        string where = Where(currentSens, dpi);

        // Only runs played at the current sens say anything about it.
        bool Same(float s) => s > 0 && MathF.Abs(s - currentSens) / currentSens < 0.02f;
        var ordered = runs.OrderBy(r => r.When).ToList();
        var mine = ordered.Where(r => Same(r.Sens)).ToList();
        // Cool-down: after a change, wait ≥ 5 sessions before re-evaluating.
        int lastOther = ordered.FindLastIndex(r => r.Sens > 0 && !Same(r.Sens));
        if (lastOther >= 0)
        {
            var since = ordered.Skip(lastOther + 1).Where(r => Same(r.Sens)).ToList();
            int sessions = Sessions(since).Count;
            if (sessions < 5)
                return Keep(sessions == 0
                    ? F($"You've just switched to {currentSens:0.###} — play at least 5 sessions with it before re-evaluating. {where}")
                    : F($"You switched to {currentSens:0.###} {sessions} session{(sessions == 1 ? "" : "s")} ago — give it at least 5 sessions before re-evaluating. {where}"), 0);
            mine = since;
        }

        var agg = Agg.Aggregate(mine.Select(r => (r.When, r.M)), DateTime.Now);
        var (delta, h, cbar, used) = Step(agg, refTier);
        bool noData = float.IsNaN(delta);
        if (noData && cm >= 20 && cm <= 80) return Keep("Need more data: " + Missing(agg) + ". " + where);
        if (noData) { delta = 0; h = 0; cbar = 0; }

        // Session consistency: the same sign of Δ in at least 2 of the last 3 sessions.
        var sessDeltas = Sessions(mine).Select(s => Step(Agg.Aggregate(s.Select(r => (r.When, r.M)), DateTime.Now), refTier).Delta)
            .Where(d => !float.IsNaN(d) && MathF.Abs(d) >= 0.01f).TakeLast(3).ToList();

        // Out-of-band nudge toward 20–80 cm/360 unless the data strongly disagrees.
        float nudge = 0;
        if (cm < 20 && !(cbar >= 0.75f && delta > 0)) nudge = -0.15f;
        if (cm > 80 && !(cbar >= 0.75f && delta < 0)) nudge = 0.15f;
        float total = delta + nudge;
        // The consistency rule guards the data-driven part; a pure "back into the band" nudge doesn't need it.
        bool dataDriven = MathF.Abs(delta) >= 0.05f;

        var top = used.OrderByDescending(x => MathF.Abs(x.W * x.H)).Take(2).Select(x => x.Words).ToList();
        string why = top.Count == 0 ? "" : " Signals: " + string.Join("; ", top) + ".";
        string label = noData || cbar < 0.5f ? "Low" : cbar < 0.75f ? "Medium" : "High";
        if (noData) cbar = 0.3f; // the nudge alone: a cautious, low-confidence suggestion

        if (MathF.Abs(total) < 0.05f) return Keep(F($"Keep your sens — your aim data doesn't point either way.{why} {where} Confidence: {label}."), cbar);

        // Bounds: stay inside 20–80 cm/360 unless the evidence is strong (and never move further out of it); never outside 15–100.
        float cmNew = cm * MathF.Exp(-total);
        bool strong = MathF.Abs(h) >= 0.6f && cbar >= 0.8f;
        if (!strong) cmNew = Math.Clamp(cmNew, Math.Min(20, cm), Math.Max(80, cm));
        cmNew = Math.Clamp(cmNew, 15, 100);
        float sNew = MathF.Round(SensFor(cmNew, dpi), 3);
        cmNew = Cm360(sNew, dpi);
        float pct = 100 * (sNew / currentSens - 1);
        // A change under 1% (e.g. cut short by the band edge, or by rounding to 3 decimals) isn't worth making.
        if (MathF.Abs(pct) < 1f) return Keep(F($"Keep your sens.{why} {where} Confidence: {label}."), cbar);
        string dir = sNew < currentSens ? "lower" : "higher";

        int agree = sessDeltas.Count(d => MathF.Sign(d) == MathF.Sign(total));
        if (dataDriven && agree < 2)
        {
            string lean = F($"Leaning {dir}: {sNew:0.000} ({pct:+0;-0}%, {cmNew:0.0} cm/360).{why}");
            return new SensAdvice(currentSens, dpi, currentSens, cm, cm, "keep",
                sessDeltas.Count < 2
                    ? $"{lean} Confirm it over at least one more session before changing. {where} Confidence: Low."
                    : $"{lean} Your recent sessions disagree, so keep it for now. {where} Confidence: Low.",
                cbar * 0.5f);
        }
        string nudgeTxt = nudge != 0 ? F($" Your sens is outside the 20–80 cm/360 band where aiming is most consistent, so this also nudges you toward it.") : "";
        return new SensAdvice(currentSens, dpi, sNew, cm, cmNew, dir,
            F($"Set VALORANT sensitivity to {sNew:0.000} (was {currentSens:0.000}, {pct:+0;-0}%): eDPI {sNew * dpi:0}, {cmNew:0.0} cm/360, higher than {ProPercentile(sNew * dpi):0}% of pros' sens.{why}{nudgeTxt} Confidence: {label}."),
            cbar);
    }

    static string Where(float sens, int dpi)
    {
        float edpi = sens * dpi;
        return F($"Now: eDPI {edpi:0}, {Cm360(sens, dpi):0.0} cm/360 (pro median 240 eDPI ≈ 54 cm; your sens is higher than {ProPercentile(edpi):0}% of pros').");
    }

    static string Missing(Dictionary<string, float> m)
    {
        var need = new List<string>();
        float off = N(m, "flick.n_off");
        if (off < 30) need.Add(F($"Head Flicks or Spidershot (≈{Math.Max(10, (30 - off) * 2):0} more flicks)"));
        float secs = N(m, "track.secs");
        if (secs < 60) need.Add(F($"Strafe Tracking (≈{Math.Max(20, 60 - secs):0} more seconds)"));
        return need.Count == 0 ? "play a few more aim drills" : "play " + string.Join(" and ", need);
    }

    static List<List<RunInfo>> Sessions(List<RunInfo> runs)
    {
        var res = new List<List<RunInfo>>();
        foreach (var r in runs.OrderBy(r => r.When))
        {
            if (res.Count == 0 || (r.When - res[^1][^1].When).TotalHours > SessionGapHours) res.Add(new List<RunInfo>());
            res[^1].Add(r);
        }
        return res;
    }
}
