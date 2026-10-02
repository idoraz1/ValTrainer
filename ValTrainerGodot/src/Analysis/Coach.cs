using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>Skill areas the coach rates (each gets a rank tier estimate).</summary>
public enum Skill { Flicking, Precision, Tracking, Reaction, CrosshairPlacement, SprayControl, Movement, Utility }

public enum Severity { Tip, Minor, Major }

/// <summary>A detected problem with evidence, the reason it happens, how to fix it and which drills train it.</summary>
public sealed record Diagnosis(
    string Id, Skill Skill, Severity Severity,
    string Title,        // "Over-flicking: your flicks travel too far"
    string Evidence,     // "62% of your flicks ended beyond the target, by 2.1° on average (48 flicks)"
    string Why,          // why it happens
    string[] Fixes,      // concrete technique cues
    string[] Drills,     // ValTrainer mode keys to practise (e.g. "flick", "spider")
    int Samples);

/// <summary>Rank estimate for one skill. Tier is continuous: 0 = Rookie … 4 = Pro, -1 = below Rookie / not enough data.</summary>
public sealed record SkillRating(Skill Skill, float Tier, float Confidence, int Samples, string Headline);

/// <summary>Sensitivity recommendation (in Valorant's own sens units at the user's DPI).</summary>
public sealed record SensAdvice(
    float CurrentSens, int Dpi, float RecommendedSens, float CurrentCm360, float RecommendedCm360,
    string Direction,    // "lower" | "higher" | "keep"
    string Reason, float Confidence);

/// <summary>Everything the Skill Profile screen shows.</summary>
public sealed class SkillProfile
{
    public float OverallTier = -1;
    public float OverallConfidence;
    public List<SkillRating> Skills = new();
    public List<Diagnosis> Problems = new();
    public List<string> Strengths = new();
    public SensAdvice? Sens;
    public int RunsAnalyzed;
    public DateTime Updated = DateTime.Now;
}

/// <summary>Post-run review shown on the results screen.</summary>
public sealed class RunReview
{
    public string Summary = "";
    public List<Diagnosis> Issues = new();
    public List<string> Strengths = new();
    public Dictionary<string, float> Metrics = new();
}

/// <summary>
/// The aim coach (docs/coach_spec.md): per-run metrics from telemetry (A), rank estimates per skill (B),
/// prioritised problems with evidence / why / fixes / drills (C) and a sensitivity recommendation (D).
/// <para>Tiers are continuous on the spec's scale: 0 ≈ Iron–Bronze … 4 ≈ Radiant (see <see cref="RankName"/>);
/// a SkillRating.Tier of −1 means "not enough data". Confidence = n/(n + n_min): &lt; 0.5 Low, &lt; 0.75 Medium.</para>
/// Problems with Severity.Tip are low-confidence ("possible") findings.
/// </summary>
public static class Coach
{
    /// <summary>Per-run metrics (stored in the run's stats record and aggregated into the profile).</summary>
    public static Dictionary<string, float> Summarize(RunTelemetry t) => RunAnalysis.Run(t);

    /// <summary>What went wrong / right in this run and how to fix it.</summary>
    public static RunReview ReviewRun(RunTelemetry t, Dictionary<string, float> metrics, int tier)
    {
        var review = new RunReview { Metrics = metrics };
        if (t.Mode == SensFinderKey) return review; // the sens finder shows its own result
        if (metrics.Count == 0) { review.Summary = RunSummary.NoData(t.Mode); return review; }
        var a = Assess.Run(metrics, runLevel: true, tierPlayed: tier);
        review.Issues = a.Issues.Take(3).Select(i => i.D).ToList();
        review.Strengths = a.Strengths.Take(2).Select(s => s.Text).ToList();
        review.Summary = RunSummary.Write(t.Mode, tier, metrics, a);
        return review;
    }

    /// <summary>Aggregate profile across recent runs (stats records + recent telemetry files).</summary>
    public static SkillProfile BuildProfile(StatsStore stats, float currentSens, int dpi)
    {
        var runs = new List<SensAdvisor.RunInfo>();
        int analysed = 0;
        SensAdvisor.FinderResult? finder = null;
        // Own snapshot (callers may run this on a worker thread while the game appends runs), newest first so the
        // bounded telemetry re-analysis below goes to the runs that matter most (and still have their files).
        var all = stats.Runs.ToArray().Where(r => r != null).OrderByDescending(r => r.When).ToList();
        foreach (var r in all)
        {
            if (r.Mode == SensFinderKey)
            {
                // Its sens changes mid-run: no skill data, but remember the latest recommendation.
                if (finder == null && r.Metrics != null && r.Metrics.TryGetValue("sensfinder.sens", out var fs) && fs > 0)
                    finder = new SensAdvisor.FinderResult(r.When, fs, (int)r.Metrics.GetValueOrDefault("sensfinder.conf"));
                continue;
            }
            var m = r.Metrics;
            if ((m == null || m.Count == 0) && !string.IsNullOrEmpty(r.TelemetryFile) && analysed < 10 && File.Exists(r.TelemetryFile))
            {
                m = FromFile(r.TelemetryFile!); // bounded: ≈10–40 ms per file, cached
                analysed++;
            }
            if (m != null && m.Count > 0) runs.Add(new SensAdvisor.RunInfo(r.When, r.Sens, m));
        }
        var agg = Agg.Aggregate(runs.Select(r => (r.When, r.M)), DateTime.Now);
        int tierGuess = (int)MathF.Round((float)all.Take(20).Where(r => r.Tier >= 0).Select(r => r.Tier).DefaultIfEmpty(1).Average());
        var a = Assess.Run(agg, runLevel: false, tierPlayed: tierGuess);
        var p = new SkillProfile
        {
            OverallTier = a.Overall,
            OverallConfidence = a.OverallConf,
            Skills = a.Ratings.Values.OrderBy(s => s.Skill).ToList(),
            Problems = a.Issues.Select(i => i.D).ToList(),
            Strengths = a.Strengths.Where(s => s.Score >= 0.5f || a.Overall < 0).Take(4).Select(s => s.Text).ToList(),
            RunsAnalyzed = runs.Count,
            Updated = DateTime.Now,
        };
        p.Sens = SensAdvisor.Advise(runs, currentSens, dpi, a.RefTier, finder);
        return p;
    }

    const string SensFinderKey = "sensfinder";

    /// <summary>Rank name for a continuous tier (Iron &lt; 0 ≤ Bronze &lt; 0.5 ≤ Silver … 3.5 ≤ Radiant; −1 = "Unrated").</summary>
    public static string RankName(float tier) => Bench.Rank(tier);

    /// <summary>cm per 360° turn: 13063 / (sens · dpi).</summary>
    public static float Cm360(float sens, int dpi) => SensAdvisor.Cm360(sens, dpi);

    /// <summary>Plain-language name of a skill.</summary>
    public static string SkillName(Skill s) => s switch
    {
        Skill.CrosshairPlacement => "Crosshair placement",
        Skill.SprayControl => "Spray control",
        Skill.Precision => "Precision (micro-adjustment)",
        Skill.Utility => "Utility (flashes)",
        _ => s.ToString(),
    };

    // Telemetry re-analysis cache (runs saved before metrics existed). Keyed by file name + write time.
    static readonly Dictionary<string, Dictionary<string, float>> fileCache = new();

    static Dictionary<string, float>? FromFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            lock (fileCache)
                if (fileCache.TryGetValue(key, out var hit)) return hit;
            var t = RunTelemetry.Load(file);
            if (t == null) return null;
            var m = Summarize(t);
            lock (fileCache) fileCache[key] = m;
            return m;
        }
        catch { return null; }
    }
}
