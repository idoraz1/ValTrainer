using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>
/// "--coach-selftest" (also "--senshistory-selftest"): synthetic run histories for <see cref="SensHistory"/>: a clear
/// winner, no real difference, too few runs, one sens (with tiny jitter and a DPI change that keeps cm/360), excluded
/// runs, unfair groups, per-drill score scales and the t critical value. Prints "  PASS …" / "  FAIL …" lines.
/// </summary>
static class SensHistoryCheck
{
    static readonly DateTime T0 = new(2026, 10, 1, 18, 0, 0);

    /// <summary>n runs per drill at a sens, scores ~ N(mean·(1+effect), sd) with a fixed seed.</summary>
    static IEnumerable<RunRecord> Runs(Random rng, float sens, float effect, int perDrill, string[] drills, int dayOffset,
        int tier = 2, int dpi = 800, float accBoost = 0, float killBoost = 0, int hz = 0)
    {
        int i = 0;
        foreach (var d in drills)
        {
            (float mean, float sd) = d switch { "gridshot" => (52000f, 3500f), "flick" => (31000f, 2600f), "spider" => (40000f, 3000f), "tracking" => (7200f, 600f), _ => (500f, 40f) };
            for (int n = 0; n < perDrill; n++)
            {
                yield return new RunRecord
                {
                    Mode = d, When = T0.AddDays(dayOffset).AddMinutes(i++ * 5), Tier = tier, Sens = sens, Dpi = dpi, PollingHz = hz,
                    Score = (int)(mean * (1 + effect) + sd * Gauss(rng)), Shots = 90, Hits = 60,
                    Accuracy = 0.7f + accBoost + 0.03f * Gauss(rng), AvgKillMs = 600 * (1 + killBoost) + 30 * Gauss(rng), HeadshotPct = -1,
                };
            }
        }
    }

    static float Gauss(Random r) => (float)(Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()));

    public static void Run(Action<string> P)
    {
        P("");
        P("##### SENS HISTORY SELF-TEST (which sens you score best with)");
        void Ok(bool ok, string what) => P($"  {(ok ? "PASS" : "FAIL")} sens history {what}");
        string[] four = { "gridshot", "flick", "spider", "tracking" };

        // 1. a clear winner: +0.9 SD at 0.40 over 24 runs vs 24 runs at 0.35
        var rng = new Random(11);
        var runs = Runs(rng, 0.35f, 0, 6, four, 0).Concat(Runs(rng, 0.40f, 0.06f, 6, four, 3, accBoost: 0.03f, killBoost: -0.05f)).ToList();
        var h = SensHistory.Analyze(runs, 0.35f, 800);
        Ok(h.Result == SensHistory.Verdict.Best && h.Top?.Sens == 0.40f && h.Lo > 0 && h.Buckets.Count == 2,
            FormattableString.Invariant($"clear winner: {h.Result}, top {h.Top?.Sens:0.000}, z diff {h.Diff:0.00} [{h.Lo:0.00}, {h.Hi:0.00}]"));
        Ok(h.Top is { } t && t.Rel > 0.02f && t.AccPts > 1f && t.KillRel < -0.01f && t.Runs == 24 && t.Drills == 4,
            FormattableString.Invariant($"winner's numbers: {h.Top?.Rel * 100:+0.0;-0.0}% score, {h.Top?.AccPts:+0.0;-0.0} pts accuracy, {h.Top?.KillRel * 100:+0.0;-0.0}% kill time (vs the average of both)"));
        Ok(h.Buckets.Count(b => b.Current) == 1 && h.Buckets.Single(b => b.Current).Sens == 0.35f && h.TopIsLatest, "current sens marked once; the winner is the latest");

        // 2. no real difference: same distribution at both sens (several seeds: never a "best")
        int falseBest = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var r = new Random(seed);
            var same = Runs(r, 0.35f, 0, 4, four, 0).Concat(Runs(r, 0.45f, 0, 4, four, 3)).ToList();
            if (SensHistory.Analyze(same, 0.45f, 800).Result == SensHistory.Verdict.Best) falseBest++;
        }
        Ok(falseBest <= 2, $"no real difference: {falseBest} of 20 seeded histories named a best (expected about 1 at 95%)");

        // 3. too few runs at the new sens
        rng = new Random(3);
        runs = Runs(rng, 0.35f, 0, 5, four, 0).Concat(Runs(rng, 0.42f, 0.1f, 2, new[] { "gridshot", "flick" }, 3)).ToList();
        h = SensHistory.Analyze(runs, 0.42f, 800);
        var nb = h.Buckets.FirstOrDefault(b => MathF.Abs(b.Sens - 0.42f) < 1e-4f);
        Ok(h.Result == SensHistory.Verdict.NotEnough && nb is { Runs: 4, Drills: 2, NeedRuns: 6, NeedDrills: 1, OwnShortfall: true }
           && h.Buckets.Any(b => b.Sens == 0.35f && !b.OwnShortfall && b.Drills == 2),
            $"not enough: {h.Result}, 0.420 has {nb?.Runs} runs in {nb?.Drills} drills, needs {nb?.NeedRuns} more runs / {nb?.NeedDrills} drill; 0.350 only waits for shared drills");

        // 4. one sens: 0.350 and 0.352 (0.6% apart) are one bucket; 0.4 @ 800 DPI = 0.8 @ 400 DPI
        rng = new Random(5);
        runs = Runs(rng, 0.35f, 0, 3, four, 0).Concat(Runs(rng, 0.352f, 0, 3, four, 1)).ToList();
        Ok(SensHistory.Analyze(runs, 0.35f, 800).Result == SensHistory.Verdict.OneSens, "0.350 and 0.352 are the same sens");
        runs = Runs(rng, 0.4f, 0, 3, four, 0, dpi: 800).Concat(Runs(rng, 0.8f, 0, 3, four, 1, dpi: 400)).ToList();
        h = SensHistory.Analyze(runs, 0.8f, 400);
        Ok(h.Result == SensHistory.Verdict.OneSens && h.Buckets[0].Current, "a DPI change with the same cm/360 is the same sens (compared in cm/360)");
        runs = Runs(rng, 0.4f, 0, 3, four, 0, dpi: 0).Concat(Runs(rng, 0.4f, 0, 3, four, 1, dpi: 1600)).ToList();
        Ok(SensHistory.Analyze(runs, 0.4f, 800).Buckets.Count == 2, "same sens at a doubled DPI is a different sens; runs without a DPI use the current one");

        // 5. excluded runs: warm-up steps, Sens Finder, Lock-In calibration (adaptive), zero scores
        rng = new Random(9);
        var excluded = Runs(rng, 0.5f, 0, 3, four, 0).ToList();
        excluded.ForEach(r => r.Warmup = true);
        excluded.Add(new RunRecord { Mode = "sensfinder", Sens = 0.6f, Score = 100, When = T0 });
        excluded.Add(new RunRecord { Mode = "flick", Sens = 0.6f, Score = 100, When = T0, Metrics = new() { ["adapt.level"] = 1 } });
        excluded.Add(new RunRecord { Mode = "flick", Sens = 0.6f, Score = 0, When = T0 });
        Ok(SensHistory.Analyze(excluded, 0.5f, 800).Result == SensHistory.Verdict.NoRuns, "warm-up, Sens Finder, adaptive and zero-score runs don't count");

        // 6. unfair groups: each sens at its own tier (or gun) is never compared
        rng = new Random(4);
        runs = Runs(rng, 0.35f, 0, 5, four, 0, tier: 1).Concat(Runs(rng, 0.45f, 0.3f, 5, four, 3, tier: 3)).ToList();
        h = SensHistory.Analyze(runs, 0.45f, 800);
        Ok(h.Result == SensHistory.Verdict.NotEnough && h.ComparedRuns == 0, $"different tiers aren't compared: {h.Result}, {h.ComparedRuns} runs compared");
        rng = new Random(4);
        runs = Runs(rng, 0.35f, 0, 5, new[] { "flick" }, 0).Concat(Runs(rng, 0.45f, 0.3f, 5, new[] { "flick" }, 3)).ToList();
        runs.Where(r => r.Sens == 0.45f).ToList().ForEach(r => r.Weapon = "Sheriff");
        Ok(SensHistory.Analyze(runs, 0.45f, 800).ComparedRuns == 0, "different guns aren't compared");

        // 7. per-drill scales: a big-score drill can't outvote the others (z-scores, not raw points)
        //    three small-scale drills (~500 points) 1 SD better at 0.45; Gridshot (~52,000 points) 0.3 SD (≈1,000 points) better at 0.35
        rng = new Random(8);
        string[] small = { "reaction", "microshot", "sound" };
        runs = Runs(rng, 0.35f, 0, 8, small, 0).Concat(Runs(rng, 0.45f, 0.08f, 8, small, 3))
            .Concat(Runs(rng, 0.35f, 0.01f, 8, new[] { "gridshot" }, 0)).Concat(Runs(rng, 0.45f, -0.01f, 8, new[] { "gridshot" }, 3)).ToList();
        h = SensHistory.Analyze(runs, 0.45f, 800);
        Ok(h.Result == SensHistory.Verdict.Best && h.Top?.Sens == 0.45f, $"three small-score drills better at 0.450 beat one big-score drill better at 0.350: {h.Result} {h.Top?.Sens}");

        // 9. compare by DPI: 0.4 @ 800 and 0.2 @ 1600 are one sens (same cm/360) but two DPIs; 1600 clearly better
        var B = SensHistory.By.Dpi;
        rng = new Random(21);
        runs = Runs(rng, 0.4f, 0, 6, four, 0, dpi: 800).Concat(Runs(rng, 0.2f, 0.06f, 6, four, 3, dpi: 1600)).ToList();
        h = SensHistory.Analyze(runs, 0.2f, 1600, 0, B);
        Ok(SensHistory.Analyze(runs, 0.2f, 1600).Result == SensHistory.Verdict.OneSens && h.Result == SensHistory.Verdict.Best
           && h.Top is { Dpi: 1600, Current: true, Unknown: false } && h.Buckets.Count == 2,
            $"by DPI: same cm/360 is one sens, but 1600 DPI beats 800 DPI by DPI: {h.Result}, top {h.Top?.Dpi} DPI");
        // runs from before 1.22 (no DPI) are an unknown bucket: shown, never compared or best, even when they score higher
        rng = new Random(22);
        runs = Runs(rng, 0.4f, 0.3f, 6, four, 0, dpi: 0).Concat(Runs(rng, 0.4f, 0, 6, four, 3, dpi: 800)).ToList();
        h = SensHistory.Analyze(runs, 0.4f, 800, 0, B);
        Ok(h.Result == SensHistory.Verdict.OneSens && h.Buckets.Count == 2 && h.Buckets[^1] is { Unknown: true, Runs: 0, AllRuns: 24, Dpi: 0 },
            $"by DPI: runs without a DPI are one unknown bucket, not compared: {h.Result}, unknown has {h.Buckets[^1].AllRuns} runs, {h.Buckets[^1].Runs} compared");
        h = SensHistory.Analyze(runs.Where(r => r.Dpi == 0), 0.4f, 800, 0, B);
        Ok(h.Result == SensHistory.Verdict.NoneKnown && h.Top == null, $"by DPI: only runs without a DPI: {h.Result}");

        // 10. compare by polling rate: 1000 Hz vs 8000 Hz (8000 clearly better), with many better-scoring runs of unknown polling
        B = SensHistory.By.Polling;
        rng = new Random(23);
        runs = Runs(rng, 0.4f, 0, 6, four, 0, hz: 1000).Concat(Runs(rng, 0.4f, 0.06f, 6, four, 3, hz: 8000))
            .Concat(Runs(rng, 0.4f, 0.4f, 8, four, 6, hz: 0)).ToList();
        h = SensHistory.Analyze(runs, 0.4f, 800, 1000, B);
        var unk = h.Buckets.SingleOrDefault(b => b.Unknown);
        Ok(h.Result == SensHistory.Verdict.Best && h.Top is { PollingHz: 8000, Unknown: false } && unk is { Runs: 0, AllRuns: 32, PollingHz: 0, Current: false }
           && h.Buckets.Single(b => b.Current).PollingHz == 1000 && ReferenceEquals(h.Buckets[^1], unk),
            $"by polling: 8000 Hz beats 1000 Hz; the higher-scoring unknown-polling runs are never best: {h.Result}, top {h.Top?.PollingHz} Hz, unknown {unk?.AllRuns} runs / {unk?.Runs} compared");
        Ok(h.ComparedRuns == 48, $"by polling: unknown runs don't enter the fair groups ({h.ComparedRuns} of 48 known runs compared)");
        // unknown vs one known rate: still nothing to compare (never "best" against unknown)
        h = SensHistory.Analyze(runs.Where(r => r.PollingHz != 8000), 0.4f, 800, 1000, B);
        Ok(h.Result == SensHistory.Verdict.OneSens && h.Top == null, $"by polling: one known rate + unknown runs is one rate: {h.Result}");
        h = SensHistory.Analyze(runs.Where(r => r.PollingHz == 0), 0.4f, 800, 0, B);
        Ok(h.Result == SensHistory.Verdict.NoneKnown && h.Buckets.Count == 1 && !h.Buckets[0].Current, $"by polling: no polling saved: {h.Result}");
        // equal skill at two rates: rarely a "best"
        falseBest = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var r = new Random(100 + seed);
            var same = Runs(r, 0.4f, 0, 4, four, 0, hz: 1000).Concat(Runs(r, 0.4f, 0, 4, four, 3, hz: 4000)).Concat(Runs(r, 0.4f, 0.3f, 4, four, 5)).ToList();
            if (SensHistory.Analyze(same, 0.4f, 800, 4000, B).Result == SensHistory.Verdict.Best) falseBest++;
        }
        Ok(falseBest <= 2, $"by polling: no real difference: {falseBest} of 20 seeded histories named a best");

        // 11. full setup: sens + DPI + polling. Same sens and DPI at 1000 / 4000 Hz are two setups; 0.2 @ 1600 is a third
        B = SensHistory.By.Setup;
        rng = new Random(24);
        runs = Runs(rng, 0.4f, 0, 5, four, 0, dpi: 800, hz: 1000).Concat(Runs(rng, 0.4f, 0, 5, four, 2, dpi: 800, hz: 4000))
            .Concat(Runs(rng, 0.2f, 0.08f, 5, four, 4, dpi: 1600, hz: 1000)).Concat(Runs(rng, 0.402f, 0, 2, four, 6, dpi: 800, hz: 1000))
            .Concat(Runs(rng, 0.4f, 0.4f, 5, four, 8, dpi: 800, hz: 0)).ToList();
        h = SensHistory.Analyze(runs, 0.4f, 800, 4000, B);
        var setups = h.Buckets.Where(b => !b.Unknown).ToList();
        Ok(setups.Count == 3 && h.Buckets.Count(b => b.Unknown) == 1 && setups.Single(b => b.PollingHz == 1000 && b.Dpi == 800).AllRuns == 28,
            $"full setup: 3 setups (0.400 and 0.402 at 800 DPI, 1000 Hz are one) + unknown: {string.Join(", ", h.Buckets.Select(b => FormattableString.Invariant($"{b.Sens:0.000}/{b.Dpi}/{b.PollingHz}{(b.Unknown ? "?" : "")} {b.AllRuns}")))}");
        Ok(h.Result == SensHistory.Verdict.Best && h.Top is { Dpi: 1600, PollingHz: 1000, Unknown: false }
           && h.Buckets.Single(b => b.Current) is { Dpi: 800, PollingHz: 4000 },
            $"full setup: 0.200 · 1600 DPI · 1000 Hz is best, current is 800 DPI · 4000 Hz: {h.Result} top {h.Top?.Sens:0.000}/{h.Top?.Dpi}/{h.Top?.PollingHz}");
        // sens mode ignores polling (the same runs are 1 sens in cm/360... plus the 0.2 @ 1600 one: all 0.4@800-equivalent)
        Ok(SensHistory.Analyze(runs, 0.4f, 800, 4000).Result == SensHistory.Verdict.OneSens, "full setup data by sens: one sens in cm/360");

        // 12. run records: PollingHz saved only when set; old records without it load as 0 (unknown)
        var jo = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        string j0 = System.Text.Json.JsonSerializer.Serialize(new RunRecord { Mode = "flick", Sens = 0.4f, Dpi = 800 }, jo);
        string j1 = System.Text.Json.JsonSerializer.Serialize(new RunRecord { Mode = "flick", Sens = 0.4f, Dpi = 800, PollingHz = 4000 }, jo);
        var back = System.Text.Json.JsonSerializer.Deserialize<RunRecord>(j1, jo);
        var old = System.Text.Json.JsonSerializer.Deserialize<RunRecord>("{\"Mode\":\"flick\",\"Score\":100,\"Sens\":0.4}", jo);
        Ok(!j0.Contains("PollingHz") && j1.Contains("\"PollingHz\":4000") && back?.PollingHz == 4000 && old is { PollingHz: 0, Dpi: 0 },
            "run records: polling rate saved only when set, old records load as unknown");

        // 13. mouse setup input: typed DPI, DPI / polling presets step and wrap (typed values step to their neighbours)
        Ok(MouseSetup.TryParseDpi(" 1600 ", out int dp) && dp == 1600 && !MouseSetup.TryParseDpi("abc", out _) && !MouseSetup.TryParseDpi("0", out _)
           && !MouseSetup.TryParseDpi("99999", out _) && MouseSetup.StepDpi(800, 1) == 1000 && MouseSetup.StepDpi(1100, 1) == 1200
           && MouseSetup.StepDpi(1100, -1) == 1000 && MouseSetup.StepDpi(3200, 1) == 400 && MouseSetup.StepDpi(400, -1) == 3200,
            "DPI: typed values, presets step and wrap");
        Ok(MouseSetup.StepPolling(0, 1) == 125 && MouseSetup.StepPolling(8000, 1) == 0 && MouseSetup.StepPolling(0, -1) == 8000
           && MouseSetup.StepPolling(1000, 1) == 2000 && MouseSetup.StepPolling(300, -1) == 250,
            "polling rate: presets step through 'not set' and wrap");

        // 8. statistics helpers
        double t10 = SensHistory.TCrit975(10), t30 = SensHistory.TCrit975(30), t5 = SensHistory.TCrit975(5);
        Ok(Math.Abs(t10 - 2.228) < 0.01 && Math.Abs(t30 - 2.042) < 0.01 && Math.Abs(t5 - 2.571) < 0.03,
            FormattableString.Invariant($"t critical values: df 5 {t5:0.000} (2.571), 10 {t10:0.000} (2.228), 30 {t30:0.000} (2.042)"));
    }
}
