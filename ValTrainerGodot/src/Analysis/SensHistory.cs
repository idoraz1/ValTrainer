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
/// <para>Compare by setup (<see cref="By"/>, Discord suggestion): the same method with the runs bucketed by their DPI, by
/// their polling rate, or by the full setup (sens within 2% + DPI + polling rate). A run without the value (older runs
/// without a DPI, runs without a polling rate set) goes to one "unknown" bucket that is shown but never compared, so it
/// can't be named best and doesn't skew the groups.</para>
/// </summary>
public sealed class SensHistory
{
    public const float BucketTolerance = 0.02f;
    public const int MinRuns = 10, MinDrills = 3, MinGroupRuns = 3;
    public const float MinEffect = 0.25f;
    const float ZClamp = 3f;

    /// <summary>NoneKnown: no run has the compared value (e.g. no polling rate saved yet); OneSens: only one known value.</summary>
    public enum Verdict { NoRuns, OneSens, NotEnough, NoClearDifference, Best, NoneKnown }

    /// <summary>What the buckets are: the sens (in cm/360), the DPI, the polling rate, or the full setup.</summary>
    public enum By { Sens, Dpi, Polling, Setup }

    public sealed class Bucket
    {
        /// <summary>The sens (and its DPI, polling rate) most of this bucket's runs were played at. Dpi / PollingHz 0 = unknown.</summary>
        public float Sens; public int Dpi; public float Cm360; public int PollingHz;
        /// <summary>The "unknown" bucket (runs without the compared value): shown, never compared.</summary>
        public bool Unknown;
        /// <summary>The range of sens (and known DPIs) played in this bucket (a DPI or polling bucket mixes them).</summary>
        public float SensMin, SensMax; public int DpiMin, DpiMax;
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
    public By Grouping;
    /// <summary>Every sens (DPI, polling rate, setup) in the history, most comparable runs first, the unknown bucket last.</summary>
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

    /// <summary>Greedy cm/360 buckets over runs sorted by cm/360: each within 2% of its lowest value.</summary>
    static IEnumerable<List<Run>> CmBuckets(IEnumerable<Run> sorted)
    {
        List<Run>? cur = null;
        foreach (var x in sorted)
        {
            if (cur == null || x.Cm > cur[0].Cm * (1 + BucketTolerance))
            {
                if (cur != null) yield return cur;
                cur = new List<Run>();
            }
            cur.Add(x);
        }
        if (cur != null) yield return cur;
    }

    /// <param name="dpi">The current DPI (also used for runs from before 1.22 in the cm/360 of <see cref="By.Sens"/>).</param>
    /// <param name="pollingHz">The current polling rate (0 = not set): marks the "NOW" bucket when comparing by polling / setup.</param>
    public static SensHistory Analyze(IEnumerable<RunRecord> records, float currentSens, int dpi, int pollingHz = 0, By by = By.Sens)
    {
        dpi = dpi > 0 ? dpi : 800;
        var res = new SensHistory { Grouping = by };
        var runs = records.Where(Usable)
            .Select(r => new Run(r, SensAdvisor.Cm360(r.Sens, r.Dpi > 0 ? r.Dpi : dpi), GroupKey(r)))
            .Where(x => x.Cm > 0 && float.IsFinite(x.Cm)).OrderBy(x => x.Cm).ToList();
        if (runs.Count == 0) { res.Result = Verdict.NoRuns; return res; }

        // 1. buckets. Sens: greedy over sorted cm/360. DPI / polling: one per value. Setup: per DPI + polling rate,
        //    then greedy cm/360 inside. Runs without the value (0) share one "unknown" bucket.
        bool UnknownRun(Run x) => by switch
        {
            By.Dpi => x.R.Dpi <= 0,
            By.Polling => x.R.PollingHz <= 0,
            By.Setup => x.R.Dpi <= 0 || x.R.PollingHz <= 0,
            _ => false,
        };
        var known = runs.Where(x => !UnknownRun(x)).ToList();
        var members = new List<List<Run>>();
        switch (by)
        {
            case By.Dpi: members.AddRange(known.GroupBy(x => x.R.Dpi).OrderBy(g => g.Key).Select(g => g.ToList())); break;
            case By.Polling: members.AddRange(known.GroupBy(x => x.R.PollingHz).OrderBy(g => g.Key).Select(g => g.ToList())); break;
            case By.Setup:
                foreach (var g in known.GroupBy(x => (x.R.Dpi, x.R.PollingHz)).OrderBy(g => g.Key.Dpi).ThenBy(g => g.Key.PollingHz))
                    members.AddRange(CmBuckets(g)); // g keeps the cm/360 order
                break;
            default: members.AddRange(CmBuckets(known)); break;
        }
        int knownBuckets = members.Count;
        var unknownRuns = runs.Where(UnknownRun).ToList();
        if (unknownRuns.Count > 0) members.Add(unknownRuns);

        var bucketOf = new Dictionary<Run, Bucket>();
        float curCm = SensAdvisor.Cm360(currentSens, dpi);
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            bool unknown = i >= knownBuckets;
            var rep = m.GroupBy(x => (MathF.Round(x.R.Sens, 3), x.R.Dpi > 0 ? x.R.Dpi : dpi))
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(x => x.R.When)).First().Key;
            int repHz = m.GroupBy(x => x.R.PollingHz).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(x => x.R.When)).First().Key;
            var dpis = m.Where(x => x.R.Dpi > 0).Select(x => x.R.Dpi).DefaultIfEmpty(0).ToList();
            var b = new Bucket
            {
                Sens = rep.Item1, Dpi = rep.Item2, Cm360 = SensAdvisor.Cm360(rep.Item1, rep.Item2), PollingHz = repHz, Unknown = unknown,
                SensMin = m.Min(x => x.R.Sens), SensMax = m.Max(x => x.R.Sens), DpiMin = dpis.Min(), DpiMax = dpis.Max(),
                AllRuns = m.Count, AllDrills = m.Select(x => x.R.Mode).Distinct().Count(),
                First = m.Min(x => x.R.When), Last = m.Max(x => x.R.When),
            };
            if (unknown && by is By.Dpi or By.Setup) b.Dpi = 0;
            if (unknown && by is By.Polling or By.Setup) b.PollingHz = 0;
            // the current sens belongs to a bucket whose range (± the tolerance) holds it; by DPI / polling: the same value
            bool cmHolds = curCm > 0 && curCm <= m.Max(x => x.Cm) * (1 + BucketTolerance) && curCm >= m.Min(x => x.Cm) / (1 + BucketTolerance);
            b.Current = !unknown && by switch
            {
                By.Dpi => b.Dpi == dpi,
                By.Polling => pollingHz > 0 && b.PollingHz == pollingHz,
                By.Setup => cmHolds && b.Dpi == dpi && pollingHz > 0 && b.PollingHz == pollingHz,
                _ => cmHolds,
            };
            foreach (var x in m) bucketOf[x] = b;
            res.Buckets.Add(b);
        }
        // one "NOW" at most: the nearest bucket when the current sens touches two
        var cur = res.Buckets.Where(b => b.Current).OrderBy(b => MathF.Abs(b.Cm360 - curCm)).FirstOrDefault();
        foreach (var b in res.Buckets) b.Current = ReferenceEquals(b, cur);

        if (knownBuckets == 0) { res.Result = Verdict.NoneKnown; return res; }
        if (knownBuckets == 1) { res.Result = Verdict.OneSens; return res; }

        // 2. fair groups (drill + tier + gun) with runs at 2+ known buckets: z-scores and relative differences per run
        var acc = res.Buckets.ToDictionary(b => b, _ => (rel: new List<double>(), accPts: new List<double>(), kill: new List<double>()));
        foreach (var g in known.GroupBy(x => x.Group))
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
        res.Buckets = res.Buckets.OrderBy(b => b.Unknown).ThenByDescending(b => b.Runs).ThenByDescending(b => b.AllRuns).ThenByDescending(b => b.Last).ToList();

        // 3. verdict: the best bucket with enough runs must beat every other one with enough runs (never "unknown")
        var ok = res.Buckets.Where(b => b.Enough && !b.Unknown).OrderByDescending(b => b.Z).ToList();
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
