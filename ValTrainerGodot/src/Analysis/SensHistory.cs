using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>
/// "Your sens history" (Coach → Sensitivity; Discord suggestion): which of the sensitivities in the player's own run
/// history they score best with. Method (docs/coach_spec.md, "Sens history"):
/// <list type="number">
/// <item>Comparable runs only: every stats.json run is already a real run (no unlimited ammo, no shortened or adaptive
/// runs). On top of that: no Sens Finder / Crosshair Finder runs (the sens or crosshair changes mid-run), no warm-up
/// steps, no runs without a sens or with a zero score.</item>
/// <item>Sens buckets: runs are compared in cm/360 (the run's own DPI, or the current DPI for runs from before 1.22);
/// values within <see cref="BucketTolerance"/> (2%) of the bucket's lowest value are one sens.</item>
/// <item>Fair groups: a run is only compared with the player's own runs of the same drill, difficulty tier and gun.
/// Groups need <see cref="MinGroupRuns"/> runs, a spread of scores and runs at two or more sens; other runs don't count.
/// Each run's score becomes a z-score within its group (clamped to ±3), so every drill uses its own scale and
/// averaging the z-scores of a bucket weights drills by their run count.</item>
/// <item>A bucket "has enough" with <see cref="MinRuns"/> comparable runs over <see cref="MinDrills"/> drills. A best sens
/// is named only when it beats every other bucket that has enough with a 95% Welch interval of the z difference above 0
/// and a difference of at least <see cref="MinEffect"/> standard deviations; else "no clear difference".</item>
/// </list>
/// The shown "+8% vs your average" is the bucket's mean score relative to each group's mean score; accuracy (points)
/// and kill time (%) the same way, for runs that have them. Pure data: the UI words it (ProfileScreen).
/// </summary>
public sealed class SensHistory
{
    public const float BucketTolerance = 0.02f;
    public const int MinRuns = 10, MinDrills = 3, MinGroupRuns = 3;
    public const float MinEffect = 0.25f;
    const float ZClamp = 3f;

    public enum Verdict { NoRuns, OneSens, NotEnough, NoClearDifference, Best }

    public sealed class Bucket
    {
        /// <summary>The sens (and its DPI) most of this bucket's runs were played at.</summary>
        public float Sens; public int Dpi; public float Cm360;
        /// <summary>All usable runs at this sens, and those that could be compared (in a fair group).</summary>
        public int AllRuns, AllDrills, Runs, Drills;
        public float Z = float.NaN, ZVar = float.NaN;
        /// <summary>Score vs the group average (0.08 = +8%), accuracy in points (+3.1), kill time in % (−0.06 = 6% faster); NaN = no data.</summary>
        public float Rel = float.NaN, AccPts = float.NaN, KillRel = float.NaN;
        public bool Current;
        public DateTime First, Last;
        public bool Enough => Runs >= MinRuns && Drills >= MinDrills;
        public int NeedRuns => Math.Max(0, MinRuns - Runs);
        public int NeedDrills => Math.Max(0, MinDrills - Drills);
        /// <summary>Short of runs or drills at this sens itself (not only of drills shared with another sens): the
        /// player has to play more at it. False = it has plenty, the other sens needs to cover the same drills.</summary>
        public bool OwnShortfall => AllRuns < MinRuns || AllDrills < MinDrills;
        internal readonly List<double> Zs = new();
        internal readonly HashSet<string> DrillKeys = new();
    }

    public Verdict Result;
    /// <summary>Every sens in the history, most comparable runs first.</summary>
    public List<Bucket> Buckets = new();
    public Bucket? Top, RunnerUp;
    /// <summary>Top − runner-up in z (standard deviations of the player's own scores) and its 95% Welch interval.</summary>
    public float Diff, Lo, Hi;
    /// <summary>The top sens is also the one played most recently (practice may explain part of the gain).</summary>
    public bool TopIsLatest;
    public int ComparedRuns, Groups;

    record Run(RunRecord R, float Cm, string Group);

    public static bool Usable(RunRecord r) =>
        r != null && r.Sens > 0 && r.Score > 0 && !r.Warmup && !string.IsNullOrEmpty(r.Mode)
        && r.Mode is not ("sensfinder" or "xhairfinder")
        && !(r.Metrics?.ContainsKey("adapt.level") ?? false);

    static string GroupKey(RunRecord r)
    {
        string gun = string.IsNullOrWhiteSpace(r.Weapon) ? Modes.WeaponRating.DefaultGun(r.Mode).ToString() : r.Weapon!;
        return r.Mode + "|" + r.Tier + "|" + gun.ToLowerInvariant();
    }

    public static SensHistory Analyze(IEnumerable<RunRecord> records, float currentSens, int dpi)
    {
        dpi = dpi > 0 ? dpi : 800;
        var res = new SensHistory();
        var runs = records.Where(Usable)
            .Select(r => new Run(r, SensAdvisor.Cm360(r.Sens, r.Dpi > 0 ? r.Dpi : dpi), GroupKey(r)))
            .Where(x => x.Cm > 0 && float.IsFinite(x.Cm)).OrderBy(x => x.Cm).ToList();
        if (runs.Count == 0) { res.Result = Verdict.NoRuns; return res; }

        // 1. buckets: greedy over sorted cm/360, each within 2% of its lowest value
        var bucketOf = new Dictionary<Run, Bucket>();
        var members = new List<List<Run>>();
        foreach (var x in runs)
        {
            if (members.Count == 0 || x.Cm > members[^1][0].Cm * (1 + BucketTolerance)) members.Add(new List<Run>());
            members[^1].Add(x);
        }
        float curCm = SensAdvisor.Cm360(currentSens, dpi);
        foreach (var m in members)
        {
            var rep = m.GroupBy(x => (MathF.Round(x.R.Sens, 3), x.R.Dpi > 0 ? x.R.Dpi : dpi))
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(x => x.R.When)).First().Key;
            var b = new Bucket
            {
                Sens = rep.Item1, Dpi = rep.Item2, Cm360 = SensAdvisor.Cm360(rep.Item1, rep.Item2), AllRuns = m.Count, AllDrills = m.Select(x => x.R.Mode).Distinct().Count(),
                First = m.Min(x => x.R.When), Last = m.Max(x => x.R.When),
            };
            // the current sens belongs to a bucket whose range (± the tolerance) holds it
            b.Current = curCm > 0 && curCm <= m[^1].Cm * (1 + BucketTolerance) && curCm >= m[0].Cm / (1 + BucketTolerance);
            foreach (var x in m) bucketOf[x] = b;
            res.Buckets.Add(b);
        }
        // one "NOW" at most: the nearest bucket when the current sens touches two
        var cur = res.Buckets.Where(b => b.Current).OrderBy(b => MathF.Abs(b.Cm360 - curCm)).FirstOrDefault();
        foreach (var b in res.Buckets) b.Current = ReferenceEquals(b, cur);

        if (res.Buckets.Count == 1) { res.Result = Verdict.OneSens; return res; }

        // 2. fair groups (drill + tier + gun) with runs at 2+ sens: z-scores and relative differences per run
        var acc = res.Buckets.ToDictionary(b => b, _ => (rel: new List<double>(), accPts: new List<double>(), kill: new List<double>()));
        foreach (var g in runs.GroupBy(x => x.Group))
        {
            var list = g.ToList();
            if (list.Count < MinGroupRuns || list.Select(x => bucketOf[x]).Distinct().Count() < 2) continue;
            double mean = list.Average(x => (double)x.R.Score);
            double sd = Math.Sqrt(list.Sum(x => Sq(x.R.Score - mean)) / (list.Count - 1));
            if (sd <= 1e-9 || mean <= 0) continue;
            var accRuns = list.Where(x => x.R.Accuracy >= 0 && x.R.Shots > 0).ToList();
            double accMean = accRuns.Count >= 2 ? accRuns.Average(x => (double)x.R.Accuracy) : double.NaN;
            var killRuns = list.Where(x => x.R.AvgKillMs > 0).ToList();
            double killMean = killRuns.Count >= 2 ? killRuns.Average(x => (double)x.R.AvgKillMs) : double.NaN;
            res.Groups++;
            foreach (var x in list)
            {
                var b = bucketOf[x];
                b.Zs.Add(Math.Clamp((x.R.Score - mean) / sd, -ZClamp, ZClamp));
                b.DrillKeys.Add(x.R.Mode);
                var a = acc[b];
                a.rel.Add(x.R.Score / mean - 1);
                if (!double.IsNaN(accMean) && x.R.Accuracy >= 0 && x.R.Shots > 0) a.accPts.Add((x.R.Accuracy - accMean) * 100);
                if (!double.IsNaN(killMean) && killMean > 0 && x.R.AvgKillMs > 0) a.kill.Add(x.R.AvgKillMs / killMean - 1);
                res.ComparedRuns++;
            }
        }
        foreach (var b in res.Buckets)
        {
            b.Runs = b.Zs.Count;
            b.Drills = b.DrillKeys.Count;
            if (b.Runs == 0) continue;
            double zm = b.Zs.Average();
            b.Z = (float)zm;
            b.ZVar = b.Runs > 1 ? (float)(b.Zs.Sum(z => Sq(z - zm)) / (b.Runs - 1)) : float.NaN;
            var a = acc[b];
            b.Rel = (float)a.rel.Average();
            if (a.accPts.Count > 0) b.AccPts = (float)a.accPts.Average();
            if (a.kill.Count > 0) b.KillRel = (float)a.kill.Average();
        }
        res.Buckets = res.Buckets.OrderByDescending(b => b.Runs).ThenByDescending(b => b.AllRuns).ThenByDescending(b => b.Last).ToList();

        // 3. verdict: the best bucket with enough runs must beat every other one with enough runs
        var ok = res.Buckets.Where(b => b.Enough).OrderByDescending(b => b.Z).ToList();
        if (ok.Count < 2) { res.Result = Verdict.NotEnough; return res; }
        res.Top = ok[0];
        res.RunnerUp = ok[1];
        (res.Diff, res.Lo, res.Hi) = Welch(ok[0], ok[1]);
        bool clear = ok.Skip(1).All(o => Welch(ok[0], o) is var w && w.Lo > 0 && w.Diff >= MinEffect);
        res.Result = clear ? Verdict.Best : Verdict.NoClearDifference;
        res.TopIsLatest = ok.All(o => ReferenceEquals(o, ok[0]) || Mid(ok[0]) > Mid(o));
        return res;
    }

    static double Sq(double x) => x * x;

    static long Mid(Bucket b) => (b.First.Ticks / 2) + (b.Last.Ticks / 2);

    /// <summary>a − b in mean z and its 95% Welch–Satterthwaite interval.</summary>
    public static (float Diff, float Lo, float Hi) Welch(Bucket a, Bucket b)
    {
        double va = double.IsNaN(a.ZVar) ? 1 : a.ZVar, vb = double.IsNaN(b.ZVar) ? 1 : b.ZVar;
        double sa = va / a.Runs, sb = vb / b.Runs, se = Math.Sqrt(Math.Max(sa + sb, 1e-6));
        double df = Sq(sa + sb) / Math.Max(1e-12, Sq(sa) / Math.Max(1, a.Runs - 1) + Sq(sb) / Math.Max(1, b.Runs - 1));
        double t = TCrit975(df), d = a.Z - b.Z;
        return ((float)d, (float)(d - t * se), (float)(d + t * se));
    }

    /// <summary>Two-sided 95% critical value of Student's t (Cornish–Fisher expansion, within 0.5% for df ≥ 3).</summary>
    public static double TCrit975(double df)
    {
        df = Math.Max(1, df);
        const double z = 1.959964;
        double z3 = z * z * z, z5 = z3 * z * z, z7 = z5 * z * z;
        return z + (z3 + z) / (4 * df) + (5 * z5 + 16 * z3 + 3 * z) / (96 * df * df)
               + (3 * z7 + 19 * z5 + 17 * z3 - 15 * z) / (384 * df * df * df);
    }
}
