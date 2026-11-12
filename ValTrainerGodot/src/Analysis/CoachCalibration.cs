using System.Globalization;
using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Analysis;

/// <summary>
/// The player's own VALORANT rank, entered on the coach screen (optional). It is only ever compared with the estimate
/// (<see cref="CoachCalibration.Gap"/>: is the aim ahead of or behind the rank, and what to train) and put in the
/// anonymous calibration line; it is never fed back into the estimate (that would just echo the entered rank and hide
/// the gap). Stored in "coach.json" in the data folder; dev runs never write it (<see cref="AppSettings.ReadOnly"/>) and
/// can preset it with "--dev --actual-rank G2".
/// </summary>
public static class ActualRank
{
    /// <summary>0…23 = Iron 1 … Immortal 3, 24 = Radiant; −1 = not set / "don't say".</summary>
    public const int None = -1, Radiant = 24;
    /// <summary>After this many days (about one act) the screen asks whether the rank is still right.</summary>
    public const int StaleDays = 60;

    static readonly string[] Codes = { "I", "B", "S", "G", "P", "D", "A", "IM" };

    sealed class Stored
    {
        public int ActualRank { get; set; } = None;
        public string ActualRankDate { get; set; } = "";
    }

    static Stored? cur;
    static readonly object Gate = new();

    static string FilePath => Path.Combine(Paths.DataDir, "coach.json");

    static Stored Cur
    {
        get
        {
            lock (Gate)
            {
                if (cur != null) return cur;
                cur = new Stored();
                if (CmdLine.DevAfter("--actual-rank") is { } dev)
                {
                    cur.ActualRank = Parse(dev) ?? None;
                    cur.ActualRankDate = DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return cur;
                }
                try
                {
                    if (File.Exists(FilePath)) cur = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) ?? cur;
                }
                catch (Exception e) { Log.Info($"[coach] couldn't read coach.json: {e.Message}"); }
                if (cur.ActualRank < None || cur.ActualRank > Radiant) cur.ActualRank = None;
                return cur;
            }
        }
    }

    /// <summary>The entered rank (−1 = not set).</summary>
    public static int Value => Cur.ActualRank;
    public static bool IsSet => Value >= 0;

    /// <summary>The day the rank was entered (null when not set or unknown).</summary>
    public static DateTime? SetOn =>
        IsSet && DateTime.TryParseExact(Cur.ActualRankDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static bool Stale => SetOn is { } d && (DateTime.Today - d).TotalDays > StaleDays;

    /// <summary>Saves a new rank (or <see cref="None"/> to clear it) with today's date. Main thread.</summary>
    public static void Set(int rank)
    {
        rank = rank < 0 ? None : Math.Min(rank, Radiant);
        lock (Gate)
        {
            var s = Cur;
            s.ActualRank = rank;
            s.ActualRankDate = rank < 0 ? "" : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (AppSettings.ReadOnly) return; // dev / automated runs never write the player's data
        try
        {
            var json = JsonSerializer.Serialize(Cur, new JsonSerializerOptions { WriteIndented = true });
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) { Log.Info($"[coach] couldn't save coach.json: {e.Message}"); }
    }

    /// <summary>Rank group 0…8 (Iron … Radiant) and division 1…3 (0 for Radiant).</summary>
    public static (int Group, int Division) Split(int rank) => rank >= Radiant ? (8, 0) : (rank / 3, rank % 3 + 1);

    public static int Make(int group, int division) => group >= 8 ? Radiant : Math.Clamp(group, 0, 7) * 3 + Math.Clamp(division, 1, 3) - 1;

    /// <summary>The middle of the rank's division on the coach's tier scale (Gold 2 → 1.25, Radiant → 3.75).</summary>
    public static float Tier(int rank)
    {
        if (rank < 0) return float.NaN;
        if (rank >= Radiant) return 3.75f;
        var (g, d) = Split(rank);
        float lo = g == 0 ? -0.5f : (g - 1) * 0.5f;
        return lo + (d - 0.5f) * (0.5f / 3f);
    }

    /// <summary>"G2", "IM3", "R" (language-neutral, for the calibration line); "none" when not set.</summary>
    public static string Code(int rank)
    {
        if (rank < 0) return "none";
        if (rank >= Radiant) return "R";
        var (g, d) = Split(rank);
        return Codes[g] + d.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Parses <see cref="Code"/> output (case-insensitive) or a plain number 0…24.</summary>
    public static int? Parse(string? s)
    {
        s = s?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n is >= 0 and <= Radiant ? n : null;
        if (s is "R" or "RAD" or "RADIANT") return Radiant;
        for (int g = Codes.Length - 1; g >= 0; g--) // "IM" before "I"
            if (s.StartsWith(Codes[g], StringComparison.Ordinal) && int.TryParse(s[Codes[g].Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d is >= 1 and <= 3)
                return Make(g, d);
        return null;
    }
}

/// <summary>
/// The "your actual rank" gap view and the anonymous calibration line (docs/research/coach-calibration-1.19.md §5.7).
/// </summary>
public static class CoachCalibration
{
    public enum GapKind { AimAhead, RankAhead, Matches }

    /// <summary>The gap (estimate − entered rank, in tiers) counts as "ahead" / "behind" from this far (1.5 divisions).</summary>
    public const float GapThreshold = 0.25f;

    /// <param name="Tiers">estimate − entered rank's division middle (+ = aim reads higher than the rank).</param>
    /// <param name="Divisions">|Tiers| in divisions, rounded.</param>
    /// <param name="Weakest">The lowest-rated skill with at least medium confidence (what to train first), if any.</param>
    public sealed record GapResult(GapKind Kind, float Tiers, int Divisions, Skill? Weakest);

    /// <summary>The gap between the estimate and the entered rank, or null when either is missing.</summary>
    public static GapResult? Gap(SkillProfile p, int rank)
    {
        if (rank < 0 || p.OverallTier < -0.5f) return null;
        float g = p.OverallTier - ActualRank.Tier(rank);
        var kind = g >= GapThreshold ? GapKind.AimAhead : g <= -GapThreshold ? GapKind.RankAhead : GapKind.Matches;
        var weakest = p.Skills.Where(s => s.Tier > -0.9f && s.Confidence >= 0.5f).OrderBy(s => s.Tier).FirstOrDefault()?.Skill;
        return new GapResult(kind, g, (int)MathF.Round(MathF.Abs(g) / (0.5f / 3f)), weakest);
    }

    /// <summary>Short caps headline of the gap, in the UI language ("AIM READS 2 DIVISIONS AHEAD OF YOUR RANK").</summary>
    public static string Headline(GapResult r) => r.Kind switch
    {
        GapKind.AimAhead => L.P(r.Divisions, $"AIM READS {r.Divisions} DIVISION AHEAD OF YOUR RANK", $"AIM READS {r.Divisions} DIVISIONS AHEAD OF YOUR RANK"),
        GapKind.RankAhead => L.P(r.Divisions, $"YOUR RANK IS {r.Divisions} DIVISION AHEAD OF YOUR AIM", $"YOUR RANK IS {r.Divisions} DIVISIONS AHEAD OF YOUR AIM"),
        _ => L.T("YOUR AIM MATCHES YOUR RANK"),
    };

    /// <summary>What to do about the gap, in the UI language.</summary>
    public static string Advice(GapResult r)
    {
        string? skill = r.Weakest is { } s ? L.T(Coach.SkillName(s)) : null;
        return r.Kind switch
        {
            GapKind.AimAhead => L.T("Decisions, utility and teamplay are likely what holds you back, not aim. Review your deaths, not just drills."),
            GapKind.RankAhead => skill != null ? L.F($"Aim training is your biggest lever. Start with your weakest skill: {skill}.")
                                               : L.T("Aim training is your biggest lever."),
            _ => skill != null ? L.F($"To climb, train your weakest skill first: {skill}.") : L.T("To climb, train your weakest skill first."),
        };
    }

    static readonly (Skill S, string K)[] Keys =
    {
        (Skill.Flicking, "fl"), (Skill.Precision, "pr"), (Skill.Tracking, "tr"), (Skill.Reaction, "re"),
        (Skill.CrosshairPlacement, "xh"), (Skill.SprayControl, "sp"), (Skill.Movement, "mv"), (Skill.Utility, "ut"),
    };

    /// <summary>
    /// One line players can paste on Discord to help fit the tables: entered rank, skill tiers and confidences, the drill
    /// mix behind placement and movement, the estimate before and after the shrink, calibration and app version. No
    /// names, paths, sens or dates beyond the day the rank was entered. Example:
    /// <c>vtcal1 rank=G2 set=2026-11-02 runs=128 E=0.89 T=fl1.12,pr0.01,… c=fl.90,… xh=deathmatch:372,siteclear:24
    /// mv=counterstrafe:326,deathmatch:195 raw=1.38 est=1.18 cal=2 v=1.19.0</c>
    /// </summary>
    public static string Line(SkillProfile p, int rank, DateTime? setOn)
    {
        static string F(FormattableString s) => FormattableString.Invariant(s);
        static string Src(List<(string Mode, float N)> src) => src.Count == 0 ? "-" : string.Join(",", src.Take(6).Select(x => F($"{x.Mode}:{x.N:0}")));
        var rated = Keys.Select(k => (k.K, R: p.Skills.FirstOrDefault(s => s.Skill == k.S))).Where(x => x.R != null && x.R.Tier > -0.9f && x.R.Confidence > 0).ToList();
        string tiers = rated.Count == 0 ? "-" : string.Join(",", rated.Select(x => F($"{x.K}{x.R!.Tier:0.00}")));
        string confs = rated.Count == 0 ? "-" : string.Join(",", rated.Select(x => x.K + F($"{x.R!.Confidence:0.00}").TrimStart('0')));
        string set = rank >= 0 && setOn is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-";
        string est = p.OverallTier >= -0.5f ? F($"{p.OverallTier:0.00}") : "-";
        string raw = float.IsNaN(p.RawMean) ? "-" : F($"{p.RawMean:0.00}");
        return F($"vtcal1 rank={ActualRank.Code(rank)} set={set} runs={p.RunsAnalyzed} E={p.OverallConfidence:0.00} T={tiers} c={confs} xh={Src(p.XhairSources)} mv={Src(p.MoveSources)} raw={raw} est={est} cal={p.Calibration} v={AppInfo.Version}");
    }
}
