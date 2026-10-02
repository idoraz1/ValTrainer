using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Dev tool: "&lt;godot&gt; --headless --path . -- --coach-analyze &lt;dir-with-.vtt-files&gt; [--brief] [--out file]"
/// prints, per telemetry file, the coach's metrics + run review (issues, summary, strengths), then a skill profile and
/// sens advice built from all files (via a temporary StatsStore), then quits.
/// "--coach-selftest" additionally runs synthetic event-only cases (reaction / movement / flash) the simulated
/// player can't produce.
/// </summary>
public static class CoachCli
{
    static readonly StringBuilder Out = new();

    static void P(string s = "") { GD.Print(s); Out.AppendLine(s); }
    static string F(FormattableString s) => FormattableString.Invariant(s);

    public static void Run(string[] args)
    {
        int i = Array.IndexOf(args, "--coach-analyze");
        var dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : RunTelemetry.Dir;
        bool brief = args.Contains("--brief");
        int oi = Array.IndexOf(args, "--out");
        string? outFile = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : null;

        // --fake-sessions N: spread the files over N sessions (1 h apart) to exercise the sens advice's session guardrail.
        int fsi = Array.IndexOf(args, "--fake-sessions");
        int fakeSessions = fsi >= 0 && fsi + 1 < args.Length && int.TryParse(args[fsi + 1], out var fsn) ? Math.Max(1, fsn) : 0;
        var store = new StatsStore();
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.vtt").OrderBy(x => x).ToList() : new List<string>();
        float sens = 0; int dpi = 800;
        foreach (var f in files)
        {
            var t = RunTelemetry.Load(f);
            if (t == null) { P($"{Path.GetFileName(f)}: unreadable"); continue; }
            var sw = Stopwatch.StartNew();
            var m = Coach.Summarize(t);
            var rv = Coach.ReviewRun(t, m, t.Tier);
            sw.Stop();
            sens = t.Sens; dpi = t.Dpi;
            // contiguous blocks of files → sessions, oldest block first
            var when = fakeSessions > 0 ? t.When.AddHours(-(fakeSessions - 1 - files.IndexOf(f) * fakeSessions / files.Count) * 1.0) : t.When;
            store.Runs.Add(new RunRecord { Mode = t.Mode, When = when, Tier = t.Tier, Sens = t.Sens, Metrics = m, TelemetryFile = f });
            var all = Assess.Run(m, true, t.Tier).Issues; // every triggered rule, not just the top 3
            P(F($"=== {Path.GetFileName(f)}  mode={t.Mode} tier={t.Tier} sens={t.Sens:0.###} dpi={t.Dpi} ({SensAdvisor.Cm360(t.Sens, t.Dpi):0.0} cm/360) frames={t.Frames.Count} shots={t.Shots.Count} events={t.Events.Count} dur={t.Duration:0.0}s analysed in {sw.ElapsedMilliseconds} ms"));
            P("  triggered: " + (all.Count == 0 ? "(none)" : string.Join(", ", all.Select(x => x.D.Id + (x.Possible ? "?" : "") + "/" + x.D.Severity))));
            if (!brief)
            {
                P("  metrics: " + string.Join("  ", m.OrderBy(k => k.Key).Select(k => F($"{k.Key}={k.Value:0.###}"))));
                foreach (var d in rv.Issues) PrintDiag(d);
            }
            else P("  key: " + KeyMetrics(m));
            P("  summary: " + rv.Summary);
            foreach (var s in rv.Strengths) P("  + " + s);
        }

        if (args.Contains("--coach-selftest")) SelfTest();

        // --coach-stats <stats.json>: build the profile from a (copied) stats file instead, exactly as the game does
        // (stored per-run metrics; telemetry paths are looked up next to it in "telemetry\"). Read-only.
        int si = Array.IndexOf(args, "--coach-stats");
        if (si >= 0 && si + 1 < args.Length && File.Exists(args[si + 1]))
        {
            var path = args[si + 1];
            var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            store = System.Text.Json.JsonSerializer.Deserialize<StatsStore>(File.ReadAllText(path), opts) ?? new StatsStore();
            store.Runs.RemoveAll(r => r == null);
            var tdir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "telemetry");
            foreach (var r in store.Runs)
                if (!string.IsNullOrEmpty(r.TelemetryFile)) r.TelemetryFile = Path.Combine(tdir, Path.GetFileName(r.TelemetryFile));
            var last = store.Runs.Where(r => r.Sens > 0).OrderBy(r => r.When).LastOrDefault();
            if (last != null) sens = last.Sens;
            P(F($"##### STATS {path}: {store.Runs.Count} runs, {store.Runs.Count(r => r.Metrics is { Count: > 0 })} with metrics"));
        }

        if (store.Runs.Count > 0)
        {
            var sw = Stopwatch.StartNew();
            var prof = Coach.BuildProfile(store, sens, dpi);
            sw.Stop();
            P();
            P(F($"##### PROFILE from {prof.RunsAnalyzed} runs (built in {sw.ElapsedMilliseconds} ms): overall {(prof.OverallTier < 0 ? "unrated" : $"{prof.OverallTier:0.00} {Coach.RankName(prof.OverallTier)}")}, evidence {prof.OverallConfidence:0.00}"));
            foreach (var s in prof.Skills)
                P(F($"  {Coach.SkillName(s.Skill),-30} tier {s.Tier,5:0.00} conf {s.Confidence:0.00} n={s.Samples,-5} {s.Headline}"));
            if (args.Contains("--explain"))
            {
                // How the numbers were reached: every benchmark component and the overall's robust mean + shrinkage.
                var agg0 = Agg.Aggregate(store.Runs.Where(r => r.Metrics is { Count: > 0 } && r.Mode != "sensfinder")
                    .Select(r => (r.When, RunAnalysis.Normalize(r.Mode, r.Tier, r.Metrics!))), DateTime.Now);
                var a0 = Assess.Run(agg0, false, 1);
                foreach (var line in a0.Trace) P("  · " + line);
                P("  agg: " + KeyMetrics(agg0));
            }
            P("  problems:");
            foreach (var d in prof.Problems) PrintDiag(d);
            P("  strengths:");
            foreach (var s in prof.Strengths) P("    + " + s);
            if (prof.Sens is { } a)
                P(F($"  sens: {a.Direction} {a.CurrentSens:0.000} -> {a.RecommendedSens:0.000} ({a.CurrentCm360:0.0} -> {a.RecommendedCm360:0.0} cm/360) conf {a.Confidence:0.00}: {a.Reason}"));
            // Raw sens signals for debugging (ignores the session / cool-down guardrails).
            var agg = Agg.Aggregate(store.Runs.Select(r => (r.When, r.Metrics!)), DateTime.Now);
            var (delta, h, c, used) = SensAdvisor.Step(agg, Assess.Run(agg, false, store.Runs[^1].Tier).RefTier);
            P(F($"  sens signals: Δ={delta:0.000} H={h:0.00} c̄={c:0.00} ") + string.Join(" ", used.Select(u => F($"{u.Key}={u.H:0.00}(c{u.C:0.00})"))));
        }
        if (outFile != null) { try { File.WriteAllText(outFile, Out.ToString()); } catch (Exception e) { GD.PrintErr(e.Message); } }
    }

    static void PrintDiag(Diagnosis d)
    {
        P($"    [{d.Severity}] {d.Id} ({d.Skill}): {d.Title}");
        P($"        evidence: {d.Evidence}");
        P($"        why: {d.Why}");
        P($"        fix: {string.Join(" | ", d.Fixes)}");
        P($"        drills: {string.Join(", ", d.Drills)}");
    }

    static string KeyMetrics(Dictionary<string, float> m)
    {
        string[] keys =
        {
            "flick.n", "flick.onset_ms", "flick.gain", "flick.overshoot_share", "flick.mean_overshoot_deg", "flick.end_err_pct", "flick.nc", "flick.tc_ms",
            "flick.dwell_ms", "flick.snap_share", "flick.premature_share", "flick.eta", "flick.eta_p", "flick.end_y_deg", "flick.ttk_hit_ms", "speed.tier", "head.fsh_pct",
            "track.ontarget", "track.lag_deg", "track.tau_ms", "track.jitter", "track.reacq_ms", "track.overrun_deg",
            "xhair.n", "xhair.err_deg", "xhair.pitch_deg", "xhair.h_deg", "spray.n", "spray.v", "spray.h", "spray.delay_ms", "react.ms",
        };
        return string.Join(" ", keys.Where(m.ContainsKey).Select(k => F($"{k}={m[k]:0.##}")));
    }

    /// <summary>Synthetic event-only runs for the rules the simulated player can't reach.</summary>
    static void SelfTest()
    {
        P();
        P("##### SELF-TEST (synthetic events)");
        var rng = new Random(7);
        float G(float mean, float sd) => mean + sd * (float)(Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()));
        RunTelemetry Mk(string mode)
        {
            var t = new RunTelemetry { Mode = mode, Tier = 1, Sens = 0.35f, Dpi = 800, Weapon = "Vandal", When = DateTime.Now };
            for (int k = 0; k < 2400; k++) t.Frames.Add(new FrameSample { T = k / 60f, Flags = (byte)(FrameFlags.Alive | FrameFlags.Accurate) });
            return t;
        }
        void Case(string name, RunTelemetry t, params string[] expect)
        {
            var m = Coach.Summarize(t);
            var ids = Assess.Run(m, true, t.Tier).Issues.Select(x => x.D.Id).ToList();
            bool ok = expect.All(ids.Contains) && (expect.Length > 0 || ids.Count == 0);
            P($"  {(ok ? "PASS" : "FAIL")} {name}: expected [{string.Join(",", expect)}] got [{string.Join(",", ids)}]  key: {KeyMetrics(m)} " +
              string.Join(" ", m.Where(k => k.Key.StartsWith("move.") || k.Key.StartsWith("flash.")).Select(k => F($"{k.Key}={k.Value:0.#}"))));
        }
        // Reaction: slow vs good.
        var slow = Mk("reaction"); for (int k = 0; k < 12; k++) slow.Events.Add(new TelemetryEvent(k * 3, "reaction", G(345, 30)));
        Case("slow reaction", slow, "SLOW_REACT");
        var fast = Mk("reaction"); for (int k = 0; k < 12; k++) fast.Events.Add(new TelemetryEvent(k * 3, "reaction", G(215, 20)));
        Case("good reaction", fast);
        // Movement: run-and-gun + no counter-strafing vs disciplined.
        var rg = Mk("counterstrafe");
        for (int k = 0; k < 40; k++)
        {
            rg.Events.Add(new TelemetryEvent(k + 0.5f, "stop", G(112, 6), rng.NextDouble() < 0.15 ? 1 : 0));
            rg.Shots.Add(new ShotSample { T = k + 0.45f, Zone = -1, Speed = rng.NextDouble() < 0.45 ? 4.2f : 0.4f, FocusRadius = 0.6f });
        }
        Case("run-and-gun", rg, "RUN_GUN", "CLUMSY_MOVE");
        var disc = Mk("counterstrafe");
        for (int k = 0; k < 40; k++)
        {
            disc.Events.Add(new TelemetryEvent(k + 0.5f, "stop", G(100, 4), rng.NextDouble() < 0.85 ? 1 : 0));
            disc.Shots.Add(new ShotSample { T = k + 0.75f, Zone = 0, Speed = 0.3f, FocusRadius = 0.6f });
        }
        Case("disciplined movement", disc);
        // Flash: late turns vs quick.
        var fl = Mk("flashmap"); for (int k = 0; k < 10; k++) fl.Events.Add(new TelemetryEvent(k * 8, "flash", 1, G(720, 60)));
        Case("late flash turns", fl, "FLASH_SLOW");
        var ff = Mk("flashmap"); for (int k = 0; k < 10; k++) ff.Events.Add(new TelemetryEvent(k * 8, "flash", 0, G(360, 40)));
        Case("quick flash turns", ff);
        // Crosshair placement from events alone.
        var xl = Mk("peek"); for (int k = 0; k < 20; k++) { float b = G(-3.4f, 0.6f), h = G(2.0f, 0.8f); xl.Events.Add(new TelemetryEvent(k * 2, "bot_seen", MathF.Sqrt(b * b + h * h), b)); }
        Case("low crosshair", xl, "XHAIR_LOW");

        // Sens advice guardrails on synthetic per-run metrics (refTier 2 bands: N_c 1.7, J 0.55, τ 160 ms).
        var now = DateTime.Now;
        Dictionary<string, float> Over() => new()
        {
            ["flick.n_off"] = 60, ["flick.overshoot_share"] = 0.9f, ["flick.n_hit"] = 50, ["flick.nc"] = 2.6f, ["flick.n_corr"] = 120, ["flick.corr_amp_r"] = 1.2f,
            ["track.secs"] = 70, ["track.reversals"] = 30, ["track.jitter"] = 0.85f, ["track.tau_ms"] = 150,
        };
        Dictionary<string, float> Neutral() => new()
        {
            ["flick.n_off"] = 60, ["flick.overshoot_share"] = 0.4f, ["flick.n_hit"] = 50, ["flick.nc"] = 1.7f, ["flick.n_corr"] = 80, ["flick.corr_amp_r"] = 1.2f,
            ["track.secs"] = 70, ["track.reversals"] = 30, ["track.jitter"] = 0.55f, ["track.tau_ms"] = 160,
        };
        List<SensAdvisor.RunInfo> S(float sens, int sessions, Func<Dictionary<string, float>> mk, int startHoursAgo) =>
            Enumerable.Range(0, sessions).Select(k => new SensAdvisor.RunInfo(now.AddHours(-startHoursAgo + k * 2), sens, mk())).ToList();
        void SensCase(string name, List<SensAdvisor.RunInfo> runs, float sens, string expect)
        {
            var a = SensAdvisor.Advise(runs, sens, 800, 2);
            P(F($"  {(a.Direction == expect ? "PASS" : "FAIL")} sens {name}: expected {expect} got {a.Direction} {a.CurrentSens:0.000} -> {a.RecommendedSens:0.000} ({a.CurrentCm360:0.0} -> {a.RecommendedCm360:0.0} cm): {a.Reason}"));
        }
        SensCase("consistent overshoot + many corrections + jitter, 3 sessions @50 cm", S(0.327f, 3, Over, 10), 0.327f, "lower");
        SensCase("neutral data @50 cm", S(0.327f, 3, Neutral, 10), 0.327f, "keep");
        SensCase("same signal but only one session (→ keep, leaning lower)", S(0.327f, 1, Over, 2), 0.327f, "keep");
        SensCase("neutral data @95 cm (outside 20–80)", S(0.172f, 3, Neutral, 10), 0.172f, "higher");
        SensCase("no data @95 cm", new List<SensAdvisor.RunInfo>(), 0.172f, "higher");
        SensCase("no data @50 cm", new List<SensAdvisor.RunInfo>(), 0.327f, "keep");
        SensCase("changed sens 2 sessions ago", S(0.25f, 3, Over, 20).Concat(S(0.327f, 2, Over, 6)).ToList(), 0.327f, "keep");

        // Rank calibration: rank-typical mechanics (docs/coach_spec.md "Calibration notes") must map to ordered,
        // realistic overall estimates — the median player near Gold 2, nobody pushed up by one standout skill.
        Dictionary<string, float> Arch(float speed, float endErr, float onset, float fsh, float tc, float nc, float trackT, float tau, float reacq,
            float react, float xErr, float pitch, float sv, float sh, float delay, float moving, float counter, float stop, float turn, float dodgeT) => new()
        {
            ["flick.n"] = 120, ["flick.n5"] = 120, ["speed.n"] = 120, ["speed.tier"] = speed, ["flick.end_err_pct"] = endErr, ["flick.onset_ms"] = onset,
            ["head.n_first"] = 80, ["head.n_hit"] = 60, ["head.fsh_pct"] = fsh, ["head.tc_ms"] = tc, ["head.nc"] = nc,
            ["track.secs"] = 180, ["track.reversals"] = 40, ["track.ontarget_tier"] = trackT, ["track.tau_ms"] = tau, ["track.reacq_ms"] = reacq,
            ["react.n"] = 20, ["react.ms"] = react,
            ["xhair.n"] = 60, ["xhair.nh"] = 60, ["xhair.err_deg"] = xErr, ["xhair.pitch_abs_deg"] = pitch, ["xhair.pitch_deg"] = -pitch,
            ["spray.n"] = 16, ["spray.v_abs"] = sv, ["spray.v"] = sv, ["spray.h"] = sh, ["spray.delay_ms"] = delay,
            ["move.shots"] = 80, ["move.stops"] = 60, ["move.moving_shot_pct"] = moving, ["move.counter_pct"] = counter, ["move.stop_ms"] = stop,
            ["flash.n"] = 16, ["flash.turn_n"] = 16, ["flash.turn_ms"] = turn, ["flash.dodge_tier"] = dodgeT,
        };
        var arch = new (string Name, float Lo, float Hi, Dictionary<string, float> M)[]
        {
            ("Iron", -0.5f, 0.35f, Arch(-0.3f, 23, 268, 58, 440, 1.8f, -0.3f, 235, 235, 262, 13.5f, 2.6f, 1.6f, 1.1f, 450, 27, 10, 127, 680, -0.3f)),
            ("Silver", 0.45f, 1.0f, Arch(0.75f, 17, 235, 67, 360, 1.3f, 0.7f, 200, 185, 238, 11.2f, 1.8f, 1.15f, 0.85f, 350, 13, 24, 118, 560, 0.7f)),
            ("Gold", 1.0f, 1.5f, Arch(1.25f, 13, 215, 72, 320, 1.0f, 1.25f, 185, 150, 226, 10.0f, 1.3f, 0.9f, 0.7f, 300, 8, 37, 113, 500, 1.25f)),
            ("Diamond", 1.85f, 2.45f, Arch(2.25f, 9.5f, 192, 79, 255, 0.75f, 2.2f, 155, 118, 207, 8.6f, 0.85f, 0.66f, 0.53f, 245, 3.8f, 58, 107, 415, 2.2f)),
            ("Immortal", 2.6f, 3.2f, Arch(3.2f, 6.8f, 171, 85, 205, 0.55f, 3.2f, 134, 92, 182, 7.0f, 0.5f, 0.45f, 0.4f, 190, 2, 76, 102, 338, 3.2f)),
        };
        float prev = float.NegativeInfinity;
        foreach (var (name, lo, hi, mm) in arch)
        {
            var a = Assess.Run(mm, false, 1);
            float t = a.Overall;
            bool ok = t >= lo && t <= hi && t > prev;
            prev = t;
            P(F($"  {(ok ? "PASS" : "FAIL")} calibration: typical {name} mechanics → overall {t:0.00} {Coach.RankName(t)} (raw mean {a.RawMean:0.00}, evidence {a.OverallConf:0.00}), expected {lo:0.00}–{hi:0.00}"));
        }
        _ = CultureInfo.InvariantCulture;
    }
}
