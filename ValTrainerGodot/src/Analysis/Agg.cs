namespace ValTrainer.Analysis;

/// <summary>
/// How per-run metrics combine across runs: counts are summed, averages are weighted by their own sample count
/// × a 14-day half-life recency weight, using only the newest runs that make up the last 300 events per family
/// (spec B "Data window").
/// </summary>
static class Agg
{
    public const int WindowEvents = 300;
    public const double HalfLifeDays = 14;

    static readonly HashSet<string> Sums = new()
    {
        "run.secs",
        "flick.n", "flick.n5", "flick.n_prim", "flick.n_still", "flick.n_low", "flick.n_over", "flick.n_under", "flick.n_off", "flick.n_small",
        "flick.n_large", "flick.anticip", "flick.onset_n", "flick.n_hit", "flick.n_corr", "flick.misses", "flick.premature",
        "flick.snap", "flick.n_first", "flick.first_hits",
        "head.n_first", "head.first_hits", "head.n_hit",
        "speed.n",
        "track.secs", "track.reversals",
        "xhair.n", "xhair.nh", "xhair.below", "xhair.on_head",
        "move.stops", "move.counter", "move.shots", "move.moving", "move.early",
        "spray.n", "spray.delay_n",
        "react.n", "react.early",
        "flash.n", "flash.turn_n",
    };

    /// <summary>Weight (sample-count) key of each averaged metric. Unlisted averages use the family's count key.</summary>
    static readonly Dictionary<string, string> Weight = new()
    {
        ["flick.mt_ms"] = "flick.n_prim", ["flick.peak_dps"] = "flick.n_prim", ["flick.peak_cms"] = "flick.n_prim",
        ["flick.end_err_pct"] = "flick.n_prim", ["flick.gain"] = "flick.n_still", ["flick.perp_deg"] = "flick.n_prim",
        ["flick.curv"] = "flick.n_prim", ["flick.eta_p"] = "flick.n_prim", ["flick.end_y_deg"] = "flick.n_prim", ["flick.low_share"] = "flick.n_prim",
        ["flick.mean_overshoot_deg"] = "flick.n_over", ["flick.mean_overshoot_pct"] = "flick.n_over",
        ["flick.mean_undershoot_deg"] = "flick.n_under",
        ["flick.overshoot_share"] = "flick.n_off", ["flick.under_share"] = "flick.n_off",
        ["flick.gain_small"] = "flick.n_small", ["flick.gain_large"] = "flick.n_large",
        ["flick.onset_ms"] = "flick.onset_n",
        ["flick.nc"] = "flick.n_hit", ["flick.tc_ms"] = "flick.n_hit", ["flick.dwell_ms"] = "flick.n_hit", ["flick.eta"] = "flick.n_hit",
        ["flick.ttk_ms"] = "flick.n_hit", ["flick.mt_ref_ms"] = "flick.n_hit", ["flick.fitts_b"] = "flick.n_hit",
        ["flick.corr_amp_deg"] = "flick.n_corr", ["flick.corr_amp_r"] = "flick.n_corr",
        ["flick.premature_dps"] = "flick.premature", ["flick.snap_err_deg"] = "flick.snap",
        ["flick.premature_share"] = "flick.misses", ["flick.snap_share"] = "flick.misses",
        ["flick.shot_err_deg"] = "flick.n_first", ["flick.shot_err_r"] = "flick.n_first", ["flick.precision_deg"] = "flick.n_first",
        ["flick.fsh_pct"] = "flick.n_first", ["flick.ttk_hit_ms"] = "speed.n",
        ["head.fsh_pct"] = "head.n_first", ["head.tc_ms"] = "head.n_hit", ["head.nc"] = "head.n_hit",
        ["speed.tier"] = "speed.n", ["speed.score_min"] = "speed.n",
        ["track.reacq_ms"] = "track.reversals", ["track.overrun_deg"] = "track.reversals", ["track.overrun_r"] = "track.reversals", ["track.overrun_excess_deg"] = "track.reversals",
        ["xhair.below_pct"] = "xhair.n", ["xhair.on_head_pct"] = "xhair.nh", ["xhair.err_deg"] = "xhair.nh", ["xhair.h_deg"] = "xhair.nh",
        ["move.moving_speed"] = "move.moving", ["move.stop_ms"] = "move.stops", ["move.counter_pct"] = "move.stops",
        ["move.moving_shot_pct"] = "move.shots", ["move.early_shot_pct"] = "move.shots",
        ["spray.delay_ms"] = "spray.delay_n",
        ["flash.turn_ms"] = "flash.turn_n",
    };

    static readonly Dictionary<string, string> FamilyCount = new()
    {
        ["flick"] = "flick.n", ["head"] = "head.n_first", ["speed"] = "speed.n", ["track"] = "track.secs", ["xhair"] = "xhair.n",
        ["move"] = "move.shots", ["spray"] = "spray.n", ["react"] = "react.n", ["flash"] = "flash.n", ["run"] = "run.secs",
    };

    static string Family(string key) { int i = key.IndexOf('.'); return i < 0 ? key : key[..i]; }

    static float FamilyN(Dictionary<string, float> m, string fam)
    {
        if (fam == "move") return Get(m, "move.shots") + Get(m, "move.stops");
        return FamilyCount.TryGetValue(fam, out var k) ? Get(m, k) : 0;
    }

    static float Get(Dictionary<string, float> m, string k) => m.TryGetValue(k, out var v) && !float.IsNaN(v) ? v : 0;

    /// <summary>Shares/percentages recomputed from counts (per run).</summary>
    public static void Derive(Dictionary<string, float> m)
    {
        Ratio(m, "flick.overshoot_share", "flick.n_over", "flick.n_off", 1);
        Ratio(m, "flick.under_share", "flick.n_under", "flick.n_off", 1);
        Ratio(m, "flick.premature_share", "flick.premature", "flick.misses", 1);
        Ratio(m, "flick.snap_share", "flick.snap", "flick.misses", 1);
        Ratio(m, "flick.low_share", "flick.n_low", "flick.n_prim", 1);
        Ratio(m, "flick.fsh_pct", "flick.first_hits", "flick.n_first", 100);
        Ratio(m, "head.fsh_pct", "head.first_hits", "head.n_first", 100);
        Ratio(m, "xhair.below_pct", "xhair.below", "xhair.n", 100);
        Ratio(m, "xhair.on_head_pct", "xhair.on_head", "xhair.nh", 100);
        Ratio(m, "move.moving_shot_pct", "move.moving", "move.shots", 100);
        Ratio(m, "move.early_shot_pct", "move.early", "move.shots", 100);
        Ratio(m, "move.counter_pct", "move.counter", "move.stops", 100);
    }

    static void Ratio(Dictionary<string, float> m, string key, string num, string den, float scale)
    {
        float d = Get(m, den);
        if (d > 0) m[key] = scale * Get(m, num) / d;
    }

    /// <summary>Aggregates per-run metric dictionaries (any order) into one profile dictionary.</summary>
    public static Dictionary<string, float> Aggregate(IEnumerable<(DateTime When, Dictionary<string, float> M)> runs, DateTime now)
    {
        var list = runs.Where(r => r.M != null && r.M.Count > 0).OrderByDescending(r => r.When).ToList();
        var outp = new Dictionary<string, float>();
        var fams = list.SelectMany(r => r.M.Keys).Select(Family).Distinct().ToList();
        foreach (var fam in fams)
        {
            // Newest runs first until the family's last 300 events are covered.
            var used = new List<(double Rw, Dictionary<string, float> M)>();
            float cum = 0;
            foreach (var r in list)
            {
                float n = FamilyN(r.M, fam);
                if (n <= 0 && !r.M.Keys.Any(k => Family(k) == fam)) continue;
                double age = Math.Max(0, (now - r.When).TotalDays);
                used.Add((Math.Pow(0.5, age / HalfLifeDays), r.M));
                cum += n;
                if (cum >= WindowEvents) break;
            }
            var keys = used.SelectMany(u => u.M.Keys).Where(k => Family(k) == fam).Distinct();
            foreach (var key in keys)
            {
                if (Sums.Contains(key)) { outp[key] = used.Sum(u => Get(u.M, key)); continue; }
                string wk = Weight.TryGetValue(key, out var w) ? w : FamilyCount.GetValueOrDefault(fam, "");
                double sw = 0, s = 0;
                foreach (var (rw, m) in used)
                {
                    if (!m.TryGetValue(key, out var v) || float.IsNaN(v)) continue;
                    double wt = (wk.Length > 0 ? Math.Max(0, Get(m, wk)) : 1) * rw;
                    if (wk == "move.shots" && wt == 0) wt = rw; // stop-only runs
                    if (wk == "flick.n_still" && !m.ContainsKey(wk)) wt = Math.Max(0, Get(m, "flick.n_prim")) * rw; // runs analysed before n_still existed
                    sw += wt; s += wt * v;
                }
                if (sw > 0) outp[key] = (float)(s / sw);
            }
        }
        return outp;
    }
}
