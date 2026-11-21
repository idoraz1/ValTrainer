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
        int tier = 2, int dpi = 800, float accBoost = 0, float killBoost = 0)
    {
        int i = 0;
        foreach (var d in drills)
        {
            (float mean, float sd) = d switch { "gridshot" => (52000f, 3500f), "flick" => (31000f, 2600f), "spider" => (40000f, 3000f), "tracking" => (7200f, 600f), _ => (500f, 40f) };
            for (int n = 0; n < perDrill; n++)
            {
                yield return new RunRecord
                {
                    Mode = d, When = T0.AddDays(dayOffset).AddMinutes(i++ * 5), Tier = tier, Sens = sens, Dpi = dpi,
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

        // 8. statistics helpers
        double t10 = SensHistory.TCrit975(10), t30 = SensHistory.TCrit975(30), t5 = SensHistory.TCrit975(5);
        Ok(Math.Abs(t10 - 2.228) < 0.01 && Math.Abs(t30 - 2.042) < 0.01 && Math.Abs(t5 - 2.571) < 0.03,
            FormattableString.Invariant($"t critical values: df 5 {t5:0.000} (2.571), 10 {t10:0.000} (2.228), 30 {t30:0.000} (2.042)"));
    }
}
