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
/// player can't produce. "--insights" adds each run's Rewind insights (time-to-hit split, flick sectors, shots, moment
/// counts with examples); "--insights-trials" also lists every engagement's timeline.
/// "--replay-check" checks each run's 3D Rewind sidecar (.vtr): present, 60 Hz, one PlayerShot per shot, BotDie ≥ kills,
/// monotonic time, size. "--replay-selftest" (also part of "--coach-selftest") round-trips a synthetic recording.
/// </summary>
public static class CoachCli
{
    static readonly StringBuilder Out = new();

    static void P(string s = "") { GD.Print(s); Out.AppendLine(s); }
    static bool trialDump, replayDump;
    static string F(FormattableString s) => FormattableString.Invariant(s);

    public static void Run(string[] args)
    {
        int i = Array.IndexOf(args, "--coach-analyze");
        var dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : RunTelemetry.Dir;
        bool brief = args.Contains("--brief");
        bool insights = args.Contains("--insights") || args.Contains("--insights-trials");
        trialDump = args.Contains("--insights-trials");
        bool replayCheck = args.Contains("--replay-check") || args.Contains("--replay-dump");
        replayDump = args.Contains("--replay-dump");
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
            if (insights) PrintInsights(t);
            if (replayCheck) ReplayCheck(f, t);
        }

        if (args.Contains("--coach-selftest")) SelfTest();
        if (args.Contains("--coach-selftest") || args.Contains("--replay-selftest")) ReplaySelfTest();
        if (args.Contains("--coach-selftest") || args.Contains("--recordings-selftest")) RecordingsSelfTest();
        if (args.Contains("--coach-selftest")) FeedSelfTest();

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

    /// <summary>"--insights": the Rewind data of one run (engagements, time-to-hit split, flick sectors, shots, moments).</summary>
    static void PrintInsights(RunTelemetry t)
    {
        var sw = Stopwatch.StartNew();
        // Not RunInsights.Build: that swallows failures (the game must never crash on them), the check must see them.
        var ins = new RunInsights { Run = t };
        try { InsightsBuilder.Fill(ins); }
        catch (Exception e) { P($"  insights Exception: {e}"); return; }
        sw.Stop();
        var tr = ins.Trials;
        P(F($"  insights ({sw.ElapsedMilliseconds} ms): trials={tr.Count} flicks={tr.Count(x => x.HasPrimary)} hits={tr.Count(x => x.Hit)} shots={ins.Shots} scatter={ins.Scatter.Count}") +
          F($" first-bullet={ins.FirstBulletAcc * 100:0.#}% ({ins.FirstHits}/{ins.FirstShots}) moving-shots={ins.MovingShotPct:0.#}%"));
        var k = ins.Ttk;
        float sum = k.ReactMs + k.FlickMs + k.SettleMs + k.ClickMs;
        // Per engagement the split is valid only if appear ≤ onset ≤ flick end ≤ on target ≤ hit (the medians above
        // needn't add up exactly: they're medians of a mixed sample).
        var full = tr.Where(x => x.Hit && !float.IsNaN(x.TOnset) && !float.IsNaN(x.TPrimEnd) && !float.IsNaN(x.TOnTarget) && !float.IsNaN(x.THit)).ToList();
        int unordered = full.Count(x => !(x.T0 <= x.TOnset + 1e-4f && x.TOnset <= x.TPrimEnd + 1e-4f && x.TPrimEnd <= x.TOnTarget + 1e-4f && x.TOnTarget <= x.THit + 1e-4f));
        P(F($"  ttk: n={k.N} react={k.ReactMs:0} flick={k.FlickMs:0} settle={k.SettleMs:0} click={k.ClickMs:0} sum={sum:0} total={k.TotalMs:0} trials={full.Count} unordered={unordered}"));
        string[] dirs = { "R", "UR", "U", "UL", "L", "DL", "D", "DR" };
        P("  sectors: " + string.Join("  ", Enumerable.Range(0, 8).Where(s => ins.SectorN[s] > 0)
            .Select(s => F($"{dirs[s]} n={ins.SectorN[s]} over={ins.SectorOver[s]} under={ins.SectorUnder[s]} gain={ins.SectorGain[s]:0.00}"))));
        var groups = ins.Moments.GroupBy(m => m.Code).OrderByDescending(g => g.Count()).ToList();
        P("  moment-counts: " + (groups.Count == 0 ? "(none)" : string.Join(" ", groups.Select(g => $"{g.Key}={g.Count()}"))));
        foreach (var g in groups)
            P($"    {g.Key}: " + string.Join(" | ", g.Take(3).Select(m => F($"[{m.T0:0.00}-{m.T1:0.00}s{(m.Trial >= 0 ? $" #{m.Trial}" : "")}] {m.Label}"))));
        if (!trialDump) return;
        for (int i = 0; i < tr.Count; i++)
        {
            var x = tr[i];
            P(F($"    #{i} id={x.Id}{(x.Bot ? " bot" : "")}{(x.Hit ? " hit" : "")} t0={x.T0:0.000} onset={x.TOnset - x.T0:+0.000} prim={x.TPrimEnd - x.T0:+0.000} on={x.TOnTarget - x.T0:+0.000} shot={x.TShot - x.T0:+0.000} hit={x.THit - x.T0:+0.000} end={x.TEnd - x.T0:+0.000}") +
              F($" A={x.AmpDeg:0.0} r={x.RadiusDeg:0.00} dir={x.DirDeg:0} gain={x.Gain:0.00}{(x.Over ? " over" : x.Under ? " under" : "")} nc={x.Corrections} first={(x.HasFirstShot ? (x.FirstHit ? "hit" : "miss") : "-")} errR={x.FirstShotErrR:0.0}"));
        }
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

        // Rewind insights: per-instance moments on the same synthetic runs; malformed runs must not throw.
        void InsCase(string name, RunTelemetry t, string code, int min, int max)
        {
            var ins = new RunInsights { Run = t };
            string err = "";
            try { InsightsBuilder.Fill(ins); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            int n = ins.Moments.Count(m => m.Code == code);
            bool sorted = ins.Moments.Zip(ins.Moments.Skip(1)).All(z => z.First.T0 <= z.Second.T0);
            bool ok = err.Length == 0 && n >= min && n <= max && sorted;
            P(F($"  {(ok ? "PASS" : "FAIL")} insights {name}: {code}={n} (expected {min}–{max}), {ins.Moments.Count} moments{(sorted ? "" : " NOT SORTED")}{(err.Length > 0 ? " threw " + err : "")}"));
        }
        InsCase("slow reaction", slow, "SLOW_REACT", 9, 12);
        InsCase("good reaction", fast, "SLOW_REACT", 0, 1);
        InsCase("run-and-gun", rg, "RUN_GUN", 5, 40);
        InsCase("disciplined movement", disc, "RUN_GUN", 0, 0);
        InsCase("late flash turns", fl, "FLASH_SLOW", 10, 10);
        InsCase("quick flash turns", ff, "FLASH_SLOW", 0, 0);
        InsCase("low crosshair", xl, "XHAIR_LOW", 15, 20);
        InsCase("no frames", new RunTelemetry { Mode = "flick" }, "MISS", 0, 0);
        var bad = Mk("flick");
        for (int k = 0; k < 50; k++) bad.Frames[k] = new FrameSample { T = k % 7 == 0 ? float.NaN : k / 60f, Yaw = float.PositiveInfinity, FocusId = k % 3, FocusRadius = float.NaN };
        bad.Shots.Add(new ShotSample { T = float.NaN, Zone = -1, FocusId = 1, FocusRadius = 0 });
        bad.Shots.Add(new ShotSample { T = 1, Zone = -1, FocusId = 1, FocusRadius = 0.5f, ErrYaw = float.NaN });
        bad.Events.Add(new TelemetryEvent(float.NaN, "kill", float.NaN, 1));
        bad.Events.Add(new TelemetryEvent(2, "bot_seen", float.NaN, float.NaN));
        InsCase("corrupt run", bad, "MISS", 0, 1);

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

    // ------------------------------------------------------------------ 3D Rewind (.vtr)

    /// <summary>"--replay-check": one line per run, "  replay-check: PASS|FAIL|SKIP …" (plus size / rate for the report).</summary>
    static void ReplayCheck(string vtt, RunTelemetry t)
    {
        var support = Game.Replay.ReplayFile.SupportFor(t.Mode);
        var path = Game.Replay.ReplayFile.SidecarOf(vtt);
        if (support == Game.Replay.ReplaySupport.None)
        {
            P(File.Exists(path) ? "  replay-check: FAIL sidecar written for a drill without 3D replay" : "  replay-check: SKIP (no 3D replay for this drill)");
            return;
        }
        if (!File.Exists(path)) { P("  replay-check: FAIL no .vtr sidecar"); return; }
        long bytes = new FileInfo(path).Length;
        var rec = Game.Replay.ReplayFile.For(t);
        if (rec == null) { P(F($"  replay-check: FAIL sidecar unreadable ({bytes} B)")); return; }
        var bad = new List<string>();
        float dur = Math.Max(0.001f, t.Duration);
        if (rec.Header.Mode != t.Mode) bad.Add($"header mode {rec.Header.Mode} != {t.Mode}");
        // 60 Hz player track (cuts add a few; the first sample is at the first frame).
        float rate = rec.Player.Count / dur;
        if (rate < 54 || rate > 66) bad.Add(F($"player rate {rate:0.0}/s"));
        // Monotonic time everywhere.
        static bool Mono(IEnumerable<float> ts) { float p = float.NegativeInfinity; foreach (var x in ts) { if (!(x >= p)) return false; p = x; } return true; }
        if (!Mono(rec.Player.Select(s => s.T))) bad.Add("player time not monotonic");
        if (!Mono(rec.Events.Select(e => e.T))) bad.Add("event time not monotonic");
        int bots = 0, tgts = 0, samples = 0;
        foreach (var e in rec.Entities)
        {
            if (e.Kind == Game.Replay.EntKind.Bot) bots++; else tgts++;
            samples += e.Bot.Count + e.Tgt.Count;
            bool mono = e.Kind == Game.Replay.EntKind.Bot ? Mono(e.Bot.Select(s => s.T)) : Mono(e.Tgt.Select(s => s.T));
            if (!mono) bad.Add($"entity {e.Id} time not monotonic");
            if (e.EndT < e.SpawnT) bad.Add($"entity {e.Id} ends before it spawns");
        }
        // One PlayerShot event per telemetry shot, indices 0..n-1.
        var shotIdx = rec.Events.Where(e => e.Kind == Game.Replay.RxKind.PlayerShot).Select(e => e.Arg).ToList();
        if (shotIdx.Count != t.Shots.Count || !shotIdx.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, t.Shots.Count)))
            bad.Add($"{shotIdx.Count} PlayerShot events for {t.Shots.Count} shots");
        int kills = t.Events.Count(e => e.Kind == "kill");
        int dies = rec.Events.Count(e => e.Kind == Game.Replay.RxKind.BotDie);
        if (bots > 0 && dies < kills) bad.Add($"{dies} BotDie events < {kills} kills");
        // Size: under 0.5 MB per 120 s.
        double perMin = bytes * 60.0 / dur;
        if (bytes * 120.0 / dur > 512 * 1024) bad.Add(F($"too big: {bytes / 1024.0:0.0} KB for {dur:0.0} s"));
        string info = F($"{bytes / 1024.0:0.0} KB for {dur:0.0} s ({perMin / 1024.0:0.0} KB/min), player {rate:0.0}/s, {bots} bots, {tgts} targets, {samples} entity samples, {rec.Events.Count} events");
        P(bad.Count == 0 ? "  replay-check: PASS " + info : "  replay-check: FAIL " + string.Join("; ", bad) + " | " + info);
        if (!replayDump) return;
        // With --replay-dump: a peek at the tracks (first / middle / last sample, cuts, event kinds).
        static string V(Vector3 v) => FormattableString.Invariant($"({v.X:0.00},{v.Y:0.00},{v.Z:0.00})");
        var h = rec.Header;
        P(F($"    header: map={h.MapKey} hash={h.MapHash:x8} extras={h.ExtraSolids.Length} weapon={h.Weapon} enemy={h.EnemyColor} support={h.Support} vm={h.Viewmodel} app={h.AppVersion}"));
        if (rec.Player.Count > 0)
            foreach (var s in new[] { rec.Player[0], rec.Player[rec.Player.Count / 2], rec.Player[^1] })
                P(F($"    player t={s.T:0.000} eye={V(s.Eye)} zoom={s.VisualZoom:0.00} ads={s.AdsBlend:0.00} reload={s.Reload:0.00} blind={s.Blind:0.00} focus={V(s.Focus)} bits={s.Bits}"));
        P($"    player cuts={rec.Player.Count(s => (s.Bits & Game.Replay.PBits.Cut) != 0)}; events: " +
          string.Join(" ", rec.Events.GroupBy(e => e.Kind).Select(g => $"{g.Key}={g.Count()}")));
        foreach (var e in rec.Entities.Take(4))
        {
            P(F($"    ent {e.Id} {e.Kind} v{e.Variant} [{e.SpawnT:0.00}, {e.EndT:0.00}] n={e.Bot.Count + e.Tgt.Count} cuts={e.Bot.Count(s => (s.Bits & Game.Replay.BBits.Cut) != 0) + e.Tgt.Count(s => (s.Bits & Game.Replay.TBits.Cut) != 0)}"));
            foreach (var s in e.Bot.Take(1).Concat(e.Bot.TakeLast(1)))
                P(F($"      t={s.T:0.000} feet={V(s.Feet)} vel={V(s.Vel)} facing={Mathf.RadToDeg(s.Facing):0.0}° pitch={Mathf.RadToDeg(s.AimPitch):0.0}° {s.Bits}"));
            foreach (var s in e.Tgt.Take(1).Concat(e.Tgt.TakeLast(1)))
                P(F($"      t={s.T:0.000} pos={V(s.Pos)} r={s.Radius:0.000} {s.Bits}"));
        }
    }

    /// <summary>"--replay-selftest": a synthetic recording survives save → load within the quantisation error; damaged
    /// files load as null.</summary>
    /// <summary>Stored runs saved by older versions are repaired by <see cref="RunAnalysis.Normalize"/>: Site Anchor's
    /// flashes and Sound Lock's sight-to-hit "reaction" no longer feed Utility / Reaction, and agent / map drills' crosshair
    /// placement recorded before the 60° gate is dropped, while gated runs and Flash Dodge keep their numbers.</summary>
    static void FeedSelfTest()
    {
        P();
        P("##### COACH FEED SELF-TEST (stored runs of older versions)");
        void Ok(bool ok, string what) => P($"  {(ok ? "PASS" : "FAIL")} feed {what}");
        var flash = new Dictionary<string, float> { ["flash.n"] = 6, ["flash.dodge_pct"] = 10, ["flash.flashed_pct"] = 90, ["xhair.n"] = 3, ["xhair.gated"] = 1 };
        var a = RunAnalysis.Normalize("anchor:killjoy", 1, flash);
        Ok(!a.Keys.Any(k => k.StartsWith("flash.")) && a.ContainsKey("xhair.n"), "Site Anchor's flashes don't rate Utility");
        var f = RunAnalysis.Normalize("flashmap", 1, flash);
        Ok(f.ContainsKey("flash.dodge_tier"), "Flash Dodge still rates Utility");
        var s = RunAnalysis.Normalize("sound", 1, new Dictionary<string, float> { ["react.n"] = 3, ["react.ms"] = 1050 });
        Ok(!s.ContainsKey("react.ms"), "Sound Lock's sight-to-hit time doesn't rate Reaction");
        var old = new Dictionary<string, float> { ["xhair.n"] = 4, ["xhair.err_deg"] = 95, ["xhair.pitch_deg"] = -3, ["flick.n"] = 4 };
        var o = RunAnalysis.Normalize("smokeexec:omen", 1, old);
        Ok(!o.Keys.Any(k => k.StartsWith("xhair.")) && o.ContainsKey("flick.n"), "ungated crosshair placement of an old agent-drill run is dropped (flicks kept)");
        var g = new Dictionary<string, float>(old) { ["xhair.gated"] = 1, ["xhair.err_deg"] = 20 };
        Ok(RunAnalysis.Normalize("smokeexec:omen", 1, g).ContainsKey("xhair.err_deg"), "gated runs keep their crosshair placement");
        Ok(RunAnalysis.Normalize("deathmatch", 1, old).ContainsKey("xhair.err_deg"), "Deathmatch (always gated) keeps its crosshair placement");
    }

    /// <summary>"--recordings-selftest": the Rewind recordings keep rules (RecordingStore) on a throwaway temp folder:
    /// newest 20 kept, then ≤ 3 examples per problem the coach still sees, fixed problems / older than 30 days / orphan
    /// 3D sidecars deleted. Problem codes are pre-seeded in recordings.json, so the files can be empty.</summary>
    static void RecordingsSelfTest()
    {
        P();
        P("##### RECORDINGS SELF-TEST (keep rules)");
        void Ok(bool ok, string what) => P($"  {(ok ? "PASS" : "FAIL")} recordings {what}");
        string dir = Path.Combine(Path.GetTempPath(), "vt-recordings-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var codes = new Dictionary<string, string[]>();
            string Make(DateTime when, string mode, params string[] c)
            {
                string name = when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + mode + ".vtt";
                File.WriteAllText(Path.Combine(dir, name), "");
                File.WriteAllText(Path.Combine(dir, Path.ChangeExtension(name, ".vtr")), "");
                codes[name] = c;
                return name;
            }
            var now = DateTime.Now;
            var recent = Enumerable.Range(0, 22).Select(i => Make(now.AddMinutes(-10 * (i + 1)), "flick")).ToList();      // 22 newest, no problems
            var over = Enumerable.Range(0, 5).Select(i => Make(now.AddDays(-10).AddMinutes(-i), "flick", "OVERFLICK")).ToList(); // still a problem
            var fixedP = Enumerable.Range(0, 3).Select(i => Make(now.AddDays(-12).AddMinutes(-i), "peek", "XHAIR_LOW")).ToList(); // fixed
            var ancient = Make(now.AddDays(-40), "flick", "OVERFLICK");                                                   // too old
            File.WriteAllText(Path.Combine(dir, "20200101_000000_orphan.vtr"), "");                                        // run gone
            File.WriteAllText(Path.Combine(dir, "recordings.json"), System.Text.Json.JsonSerializer.Serialize(new { Runs = codes }));

            Game.RecordingStore.Maintain(new[] { "OVERFLICK", "SLOW_MICRO" }, dir);

            bool Has(string n) => File.Exists(Path.Combine(dir, n));
            bool HasVtr(string n) => File.Exists(Path.Combine(dir, Path.ChangeExtension(n, ".vtr")));
            Ok(recent.Take(20).All(Has) && recent.Take(20).All(HasVtr), "newest 20 runs (and their 3D sidecars) kept");
            Ok(!Has(recent[20]) && !Has(recent[21]) && !HasVtr(recent[20]), "older runs with no current problem deleted (with sidecars)");
            Ok(over.Take(3).All(Has) && !Has(over[3]) && !Has(over[4]), "3 newest examples of a current problem kept, the rest deleted");
            Ok(!fixedP.Any(Has), "examples of a fixed problem deleted");
            Ok(!Has(ancient), $"runs older than {Game.RecordingStore.MaxAgeDays} days deleted");
            Ok(!File.Exists(Path.Combine(dir, "20200101_000000_orphan.vtr")), "orphan 3D sidecar deleted");
            var (runs, _) = Game.RecordingStore.Usage(dir);
            Ok(runs == 23, $"usage counts the kept runs ({runs})");
            Game.RecordingStore.DeleteAll(dir);
            Ok(Directory.GetFiles(dir).Length == 0, "delete all empties the folder");
        }
        catch (Exception e) { Ok(false, $"threw: {e.Message}"); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void ReplaySelfTest()
    {
        P();
        P("##### REPLAY SELF-TEST (.vtr round trip)");
        void Ok(bool ok, string what) => P($"  {(ok ? "PASS" : "FAIL")} replay {what}");
        var rng = new Random(11);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Vector3 V(float s) => new(R(-s, s), R(0, s / 4), R(-s, s));

        var rec = new Game.Replay.ReplayRecording { Duration = 20.5f };
        rec.Header = new Game.Replay.ReplayHeader
        {
            Mode = "deathmatch", MapKey = "ascent_a", MapHash = 0xDEADBEEF, Weapon = "Vandal", EnemyColor = "ff3355ff", TargetStyle = "valorant",
            AppVersion = "9.9.9-test", ExtraSolids = new[] { new[] { -2.6f, 0f, -9.9f, 2.6f, 4.4f, -9.75f }, new[] { 0.1f, 0.2f, 0.3f, 1.1f, 2.2f, 3.3f } },
            Outlines = true, Viewmodel = true, LeftHanded = true, Support = Game.Replay.ReplaySupport.Full,
        };
        float tt = 0;
        var eye = new Vector3(0, 1.6f, 0);
        for (int i = 0; i < 1230; i++)
        {
            tt += R(0.010f, 0.020f);
            eye += new Vector3(R(-0.08f, 0.08f), R(-0.01f, 0.01f), R(-0.08f, 0.08f));
            if (i == 600) eye += new Vector3(12, 0, -7); // respawn jump
            rec.Player.Add(new Game.Replay.PlayerSample
            {
                T = tt, Eye = eye, Focus = V(40), VisualZoom = i % 50 < 20 ? 1f + R(0, 1.5f) : 1f, AdsBlend = R(0, 1), Reload = R(0, 1), Blind = R(0, 1),
                Bits = (Game.Replay.PBits)rng.Next(64),
            });
        }
        for (int k = 0; k < 6; k++)
        {
            var e = new Game.Replay.EntityTrack { Id = (ushort)(k + 1), Kind = k < 4 ? Game.Replay.EntKind.Bot : Game.Replay.EntKind.Target, Variant = (byte)(k % 4), SpawnT = k, EndT = k == 0 ? float.PositiveInfinity : k + 10.25f };
            float et = k;
            for (int i = 0; i < 400; i++)
            {
                et += 1 / 60f;
                if (e.Kind == Game.Replay.EntKind.Bot)
                    e.Bot.Add(new Game.Replay.BotSample { T = et, Feet = V(60), Vel = V(7), Facing = R(-MathF.PI, MathF.PI), AimPitch = R(-1.4f, 1.4f), Bits = (Game.Replay.BBits)rng.Next(32) });
                else
                    e.Tgt.Add(new Game.Replay.TargetSample { T = et, Pos = V(30), Radius = R(0.05f, 0.6f), Bits = (Game.Replay.TBits)rng.Next(16) });
            }
            rec.Entities.Add(e);
        }
        for (int i = 0; i < 300; i++)
        {
            var kind = (Game.Replay.RxKind)(i % 5);
            rec.Events.Add(new Game.Replay.ReplayEvent(i * 0.05f, kind, (ushort)(i % 7), i % 3 == 0 ? Vector3.Zero : V(50), i % 2 == 0 ? Vector3.Up : Vector3.Zero, i % 4 == 0 ? 0 : rng.Next(-5, 5000)));
        }

        var dir = Path.Combine(Path.GetTempPath(), "vt-replay-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "run.vtr");
            Game.Replay.ReplayFile.Save(rec, path);
            long size = new FileInfo(path).Length;
            var back = Game.Replay.ReplayFile.Load(path);
            Ok(back != null, F($"save → load ({size / 1024.0:0.0} KB, {rec.Player.Count} player samples, {rec.Entities.Sum(e => e.Bot.Count + e.Tgt.Count)} entity samples, {rec.Events.Count} events)"));
            if (back == null) return;

            var h = back.Header; var h0 = rec.Header;
            bool hdr = h.Mode == h0.Mode && h.MapKey == h0.MapKey && h.MapHash == h0.MapHash && h.Weapon == h0.Weapon && h.EnemyColor == h0.EnemyColor
                && h.TargetStyle == h0.TargetStyle && h.AppVersion == h0.AppVersion && h.Outlines == h0.Outlines && h.Viewmodel == h0.Viewmodel
                && h.LeftHanded == h0.LeftHanded && h.Support == h0.Support && h.ExtraSolids.Length == h0.ExtraSolids.Length
                && h.ExtraSolids.Zip(h0.ExtraSolids).All(z => z.First.SequenceEqual(z.Second)) && back.Duration == rec.Duration;
            Ok(hdr, "header and duration exact (ExtraSolids bit-exact)");

            const float Tq = 0.5e-4f + 2e-6f;
            static float D(Vector3 a, Vector3 b) => MathF.Max(MathF.Abs(a.X - b.X), MathF.Max(MathF.Abs(a.Y - b.Y), MathF.Abs(a.Z - b.Z)));
            float eT = 0, eEye = 0, eFoc = 0, eZoom = 0, e8 = 0; bool bits = back.Player.Count == rec.Player.Count;
            for (int i = 0; bits && i < rec.Player.Count; i++)
            {
                var a = rec.Player[i]; var b = back.Player[i];
                eT = MathF.Max(eT, MathF.Abs(a.T - b.T)); eEye = MathF.Max(eEye, D(a.Eye, b.Eye)); eFoc = MathF.Max(eFoc, D(a.Focus, b.Focus));
                eZoom = MathF.Max(eZoom, MathF.Abs(a.VisualZoom - b.VisualZoom));
                e8 = MathF.Max(e8, MathF.Max(MathF.Abs(a.AdsBlend - b.AdsBlend), MathF.Max(MathF.Abs(a.Reload - b.Reload), MathF.Abs(a.Blind - b.Blind))));
                bits &= a.Bits == b.Bits;
            }
            Ok(bits && eT <= Tq && eEye <= 0.0005f + 1e-5f && eFoc <= 0.005f + 1e-5f && eZoom <= 0.0005f + 1e-6f && e8 <= 0.5f / 255 + 1e-6f,
               F($"player within quantisation: T {eT * 1000:0.000} ms, eye {eEye * 1000:0.00} mm, focus {eFoc * 100:0.00} cm, zoom {eZoom:0.0000}, 8-bit {e8 * 255:0.00}/255"));

            float bT = 0, bFeet = 0, bVel = 0, bFace = 0, bPitch = 0, gPos = 0, gRad = 0; bool ents = back.Entities.Count == rec.Entities.Count;
            for (int k = 0; ents && k < rec.Entities.Count; k++)
            {
                var a = rec.Entities[k]; var b = back.Entities[k];
                ents &= a.Id == b.Id && a.Kind == b.Kind && a.Variant == b.Variant && a.SpawnT == b.SpawnT && a.EndT.Equals(b.EndT)
                        && a.Bot.Count == b.Bot.Count && a.Tgt.Count == b.Tgt.Count;
                for (int i = 0; ents && i < a.Bot.Count; i++)
                {
                    var x = a.Bot[i]; var y = b.Bot[i];
                    bT = MathF.Max(bT, MathF.Abs(x.T - y.T)); bFeet = MathF.Max(bFeet, D(x.Feet, y.Feet)); bVel = MathF.Max(bVel, D(x.Vel, y.Vel));
                    bFace = MathF.Max(bFace, MathF.Abs(Mathf.AngleDifference(x.Facing, y.Facing))); bPitch = MathF.Max(bPitch, MathF.Abs(x.AimPitch - y.AimPitch));
                    ents &= x.Bits == y.Bits;
                }
                for (int i = 0; ents && i < a.Tgt.Count; i++)
                {
                    var x = a.Tgt[i]; var y = b.Tgt[i];
                    bT = MathF.Max(bT, MathF.Abs(x.T - y.T)); gPos = MathF.Max(gPos, D(x.Pos, y.Pos)); gRad = MathF.Max(gRad, MathF.Abs(x.Radius - y.Radius));
                    ents &= x.Bits == y.Bits;
                }
            }
            Ok(ents && bT <= Tq && bFeet <= 0.005f + 2e-5f && bVel <= 0.005f + 1e-5f && bFace <= MathF.PI / 65536 + 1e-5f && bPitch <= 0.0005f + 1e-6f
               && gPos <= 0.0005f + 1e-5f && gRad <= 0.0005f + 1e-6f,
               F($"entities within quantisation: T {bT * 1000:0.000} ms, feet {bFeet * 100:0.00} cm, vel {bVel * 100:0.00} cm/s, facing {Mathf.RadToDeg(bFace):0.0000}°, pitch {bPitch * 1000:0.00} mrad, target {gPos * 1000:0.00} mm r {gRad * 1000:0.00} mm"));
            Ok(back.Events.SequenceEqual(rec.Events), $"events exact ({back.Events.Count})");

            // Damaged files: truncated anywhere, wrong magic / version, missing → null (never throws).
            var bytes = File.ReadAllBytes(path);
            bool allNull = true;
            foreach (int len in new[] { 0, 3, 8, 20, bytes.Length / 4, bytes.Length / 2, bytes.Length - 10, bytes.Length - 5 })
            {
                var cut = Path.Combine(dir, $"cut{len}.vtr");
                File.WriteAllBytes(cut, bytes[..Math.Max(0, len)]);
                if (Game.Replay.ReplayFile.Load(cut) != null) { allNull = false; P($"    truncated to {len} B loaded"); }
            }
            Ok(allNull, F($"truncated files load as null ({bytes.Length} B file)"));
            var wrong = (byte[])bytes.Clone(); wrong[4] = 99;
            File.WriteAllBytes(Path.Combine(dir, "ver.vtr"), wrong);
            var magic = (byte[])bytes.Clone(); magic[0] ^= 0xff;
            File.WriteAllBytes(Path.Combine(dir, "magic.vtr"), magic);
            Ok(Game.Replay.ReplayFile.Load(Path.Combine(dir, "ver.vtr")) == null && Game.Replay.ReplayFile.Load(Path.Combine(dir, "magic.vtr")) == null
               && Game.Replay.ReplayFile.Load(Path.Combine(dir, "missing.vtr")) == null, "unknown version / bad magic / missing file load as null");

            // Interpolation never crosses a cut; empty recording round-trips.
            var p0 = new Game.Replay.ReplayRecording();
            p0.Player.Add(new Game.Replay.PlayerSample { T = 1, Eye = Vector3.Zero, VisualZoom = 1 });
            p0.Player.Add(new Game.Replay.PlayerSample { T = 2, Eye = new Vector3(10, 0, 0), VisualZoom = 1, Bits = Game.Replay.PBits.Cut });
            var empty = Game.Replay.ReplayFile.Read(new MemoryStream(Game.Replay.ReplayFile.Encode(new Game.Replay.ReplayRecording())));
            Ok(p0.PlayerAt(1.5f).Eye == Vector3.Zero && empty != null && empty.Player.Count == 0 && empty.Entities.Count == 0, "cut holds the previous sample; empty recording round-trips");

            // Support table.
            var S = Game.Replay.ReplayFile.SupportFor;
            bool sup = S("flick") == Game.Replay.ReplaySupport.Full && S("deathmatch") == Game.Replay.ReplaySupport.Full && S("peek_mix") == Game.Replay.ReplaySupport.Full
                && S("flashpeek:skye") == Game.Replay.ReplaySupport.Partial && S("anchor:killjoy") == Game.Replay.ReplaySupport.Partial
                && S("retake") == Game.Replay.ReplaySupport.Partial && S("something_new") == Game.Replay.ReplaySupport.Partial
                && S("reaction") == Game.Replay.ReplaySupport.None && S("sensfinder") == Game.Replay.ReplaySupport.None && S("xhairfinder") == Game.Replay.ReplaySupport.None;
            Ok(sup, "support table");
        }
        catch (Exception e) { Ok(false, $"threw {e.GetType().Name}: {e.Message}"); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
