using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>The kinds of requirement term (progression-design §9 grammar, plus the skins doc's challenges).</summary>
public enum ReqKind
{
    /// <summary><c>free</c>: always met.</summary>
    Free,
    /// <summary><c>level:N</c>: account level ≥ N.</summary>
    Level,
    /// <summary><c>stars:N</c>: stars ≥ N.</summary>
    Stars,
    /// <summary><c>rankup:overall:N</c>: confirmed overall coach rank-ups ≥ N.</summary>
    RankupOverall,
    /// <summary><c>rankup:skill:N</c>: confirmed skill rank-ups ≥ N.</summary>
    RankupSkill,
    /// <summary><c>coach:placed</c>: a first confirmed overall coach rank.</summary>
    CoachPlaced,
    /// <summary><c>badgeups:N</c>: badge rank-ups ≥ N.</summary>
    BadgeUps,
    /// <summary><c>pbstars:N</c>: real personal bests (the starred kind) ≥ N.</summary>
    PbStars,
    /// <summary><c>lockins:N</c>: completed Lock-Ins ≥ N.</summary>
    LockIns,
    /// <summary><c>streak:N</c>: weekly training streak ≥ N weeks.</summary>
    Streak,
    /// <summary><c>drills:N</c>: distinct drills with an earned badge ≥ N.</summary>
    Drills,
    /// <summary><c>badge:&lt;tier&gt;</c> (rookie … pro): a badge of that tier or better in any drill.</summary>
    Badge,
    /// <summary><c>with:&lt;line&gt;:runs:N</c>: runs with that skin line on the gun.</summary>
    WithRuns,
    /// <summary><c>with:&lt;line&gt;:pb</c>: a real PB with that line on the gun.</summary>
    WithPb,
    /// <summary><c>with:&lt;line&gt;:badgeup</c>: a badge rank-up with that line on the gun.</summary>
    WithBadgeUp,
    /// <summary><c>weeklyimprove:N</c>: weekly "beat your baseline" challenges completed.</summary>
    WeeklyImprove,
    /// <summary><c>bench:&lt;rank&gt;:N</c> (iron … radiant, Bench.RankNames): that benchmark rank or better in N drills.</summary>
    Bench,
    /// <summary><c>finder:sens</c>: the Sens Finder completed once.</summary>
    FinderSens,
    /// <summary>Anything the parser didn't understand: never met (logged once).</summary>
    Unknown,
}

/// <summary>One requirement term. <see cref="Arg"/>: the line id (with:*), tier index (badge) or rank index (bench) as text;
/// <see cref="N"/>: the count needed.</summary>
public sealed record ReqTerm(ReqKind Kind, string Source, string? Arg = null, int N = 1)
{
    static readonly string[] Tiers = { "rookie", "regular", "veteran", "elite", "pro" };
    static readonly string[] Ranks = { "iron", "bronze", "silver", "gold", "platinum", "diamond", "ascendant", "immortal", "radiant" };
    static readonly HashSet<string> warned = new();

    /// <summary>Parses one term ("level:12"). Unknown or malformed terms give <see cref="ReqKind.Unknown"/> (never met).</summary>
    public static ReqTerm Parse(string s)
    {
        s = s.Trim().ToLowerInvariant();
        var p = s.Split(':');
        static bool Num(string x, out int n) => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out n);
        ReqTerm? t = p switch
        {
            ["free"] => new(ReqKind.Free, s, N: 0),
            ["level", var n] when Num(n, out var v) => new(ReqKind.Level, s, N: v),
            ["stars", var n] when Num(n, out var v) => new(ReqKind.Stars, s, N: v),
            ["rankup", "overall", var n] when Num(n, out var v) => new(ReqKind.RankupOverall, s, N: v),
            ["rankup", "skill", var n] when Num(n, out var v) => new(ReqKind.RankupSkill, s, N: v),
            ["coach", "placed"] => new(ReqKind.CoachPlaced, s),
            ["badgeups", var n] when Num(n, out var v) => new(ReqKind.BadgeUps, s, N: v),
            ["pbstars", var n] when Num(n, out var v) => new(ReqKind.PbStars, s, N: v),
            ["lockins", var n] when Num(n, out var v) => new(ReqKind.LockIns, s, N: v),
            ["streak", var n] when Num(n, out var v) => new(ReqKind.Streak, s, N: v),
            ["drills", var n] when Num(n, out var v) => new(ReqKind.Drills, s, N: v),
            ["weeklyimprove", var n] when Num(n, out var v) => new(ReqKind.WeeklyImprove, s, N: v),
            ["badge", var tier] when Array.IndexOf(Tiers, tier) >= 0 => new(ReqKind.Badge, s, Array.IndexOf(Tiers, tier).ToString(CultureInfo.InvariantCulture)),
            ["with", var line, "runs", var n] when ItemIds.IsToken(line) && Num(n, out var v) => new(ReqKind.WithRuns, s, line, v),
            ["with", var line, "pb"] when ItemIds.IsToken(line) => new(ReqKind.WithPb, s, line),
            ["with", var line, "badgeup"] when ItemIds.IsToken(line) => new(ReqKind.WithBadgeUp, s, line),
            ["bench", var rank, var n] when Array.IndexOf(Ranks, rank) >= 0 && Num(n, out var v) => new(ReqKind.Bench, s, Array.IndexOf(Ranks, rank).ToString(CultureInfo.InvariantCulture), v),
            ["finder", "sens"] => new(ReqKind.FinderSens, s),
            _ => null,
        };
        if (t != null) return t;
        lock (warned) if (warned.Add(s)) Log.Info($"[progress] unknown requirement term '{s}' (locked)");
        return new ReqTerm(ReqKind.Unknown, s);
    }

    int ArgInt => int.TryParse(Arg, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>What the player has and needs for this term (met when Have ≥ Need).</summary>
    public (int Have, int Need) Measure(ProgressFacts f) => Kind switch
    {
        ReqKind.Free => (1, 1),
        ReqKind.Level => (f.Level, N),
        ReqKind.Stars => (f.Stars, N),
        ReqKind.RankupOverall => (f.OverallUps, N),
        ReqKind.RankupSkill => (f.SkillUps, N),
        ReqKind.CoachPlaced => (f.CoachPlaced ? 1 : 0, 1),
        ReqKind.BadgeUps => (f.BadgeUps, N),
        ReqKind.PbStars => (f.PbStars, N),
        ReqKind.LockIns => (f.LockIns, N),
        ReqKind.Streak => (f.StreakWeeks, N),
        ReqKind.Drills => (f.BadgeDrills, N),
        ReqKind.Badge => (f.BestBadge >= ArgInt ? 1 : 0, 1),
        ReqKind.WithRuns => (f.With.TryGetValue(Arg!, out var w) ? w.Runs : 0, N),
        ReqKind.WithPb => (f.With.TryGetValue(Arg!, out var w) ? Math.Min(1, w.Pb) : 0, 1),
        ReqKind.WithBadgeUp => (f.With.TryGetValue(Arg!, out var w) ? Math.Min(1, w.BadgeUp) : 0, 1),
        ReqKind.WeeklyImprove => (f.WeeklyImprove, N),
        ReqKind.Bench => (ArgInt < f.BenchDrillsAtLeast.Length ? f.BenchDrillsAtLeast[ArgInt] : 0, N),
        ReqKind.FinderSens => (f.SensFinderDone ? 1 : 0, 1),
        _ => (0, 1),
    };

    public bool Met(ProgressFacts f) { var (h, n) = Measure(f); return h >= n; }

    /// <summary>0..1 towards this term.</summary>
    public float Fraction(ProgressFacts f) { var (h, n) = Measure(f); return n <= 0 ? 1f : Math.Clamp(h / (float)n, 0f, 1f); }

    /// <summary>The requirement as a sentence, translated now (call at draw time, never in a static initializer).</summary>
    public string Text()
    {
        int n = N;
        return Kind switch
        {
            ReqKind.Free => L.T("Free"),
            ReqKind.Level => L.F($"Reach level {n}"),
            ReqKind.Stars => L.P(n, $"Earn {n} star", $"Earn {n} stars"),
            ReqKind.RankupOverall => L.P(n, $"{n} confirmed coach rank-up", $"{n} confirmed coach rank-ups"),
            ReqKind.RankupSkill => L.P(n, $"{n} confirmed skill rank-up", $"{n} confirmed skill rank-ups"),
            ReqKind.CoachPlaced => L.T("Get a confirmed coach rank"),
            ReqKind.BadgeUps => L.P(n, $"{n} badge rank-up", $"{n} badge rank-ups"),
            ReqKind.PbStars => L.P(n, $"{n} real personal best", $"{n} real personal bests"),
            ReqKind.LockIns => L.P(n, $"Complete {n} Lock-In", $"Complete {n} Lock-Ins"),
            ReqKind.Streak => L.P(n, $"Train {n} week in a row", $"Train {n} weeks in a row"),
            ReqKind.Drills => L.P(n, $"Earn a badge in {n} drill", $"Earn a badge in {n} drills"),
            ReqKind.Badge => L.F($"Earn a {L.T(Core.Difficulty.Get(ArgInt).Name)} badge"),
            ReqKind.WithRuns => L.P(n, $"Play {n} run with this skin", $"Play {n} runs with this skin"),
            ReqKind.WithPb => L.T("Set a real personal best with this skin"),
            ReqKind.WithBadgeUp => L.T("Rank up a badge with this skin"),
            ReqKind.WeeklyImprove => L.P(n, $"Beat your baseline in {n} weekly challenge", $"Beat your baseline in {n} weekly challenges"),
            ReqKind.Bench => L.P(n, $"Reach {L.T(RankName)} in {n} benchmark drill", $"Reach {L.T(RankName)} in {n} benchmark drills"),
            ReqKind.FinderSens => L.T("Complete the Sens Finder"),
            _ => L.T("Not available yet"),
        };
    }

    string RankName => Analysis.Bench.RankNames[Math.Clamp(ArgInt, 0, Analysis.Bench.RankNames.Length - 1)];

    /// <summary>Short progress text: "Level 9 / 12", "3 / 25".</summary>
    public string ProgressText(ProgressFacts f)
    {
        var (h, n) = Measure(f);
        return Kind == ReqKind.Level ? L.F($"Level {h} / {n}") : L.Raw($"{Math.Min(h, n).ToString(CultureInfo.InvariantCulture)} / {n.ToString(CultureInfo.InvariantCulture)}");
    }
}

/// <summary>A parsed requirement expression tree: a leaf term, all-of (<c>+</c>) or any-of (<c>|</c>).</summary>
public abstract record ReqNode
{
    public abstract bool Met(ProgressFacts f);
    /// <summary>0..1: all-of = the mean of its parts, any-of = the best part.</summary>
    public abstract float Fraction(ProgressFacts f);
    public abstract string Text(bool nested = false);
    /// <summary>The first term that is not met (depth first; for any-of: its closest option), or null when met.</summary>
    public abstract ReqTerm? FirstUnmet(ProgressFacts f);
    public abstract IEnumerable<ReqTerm> Terms();
}

public sealed record ReqLeaf(ReqTerm Term) : ReqNode
{
    public override bool Met(ProgressFacts f) => Term.Met(f);
    public override float Fraction(ProgressFacts f) => Term.Fraction(f);
    public override string Text(bool nested = false) => Term.Text();
    public override ReqTerm? FirstUnmet(ProgressFacts f) => Term.Met(f) ? null : Term;
    public override IEnumerable<ReqTerm> Terms() { yield return Term; }
}

public sealed record ReqAll(IReadOnlyList<ReqNode> Parts) : ReqNode
{
    public override bool Met(ProgressFacts f) => Parts.All(p => p.Met(f));
    public override float Fraction(ProgressFacts f) => Parts.Count == 0 ? 1f : Parts.Average(p => p.Fraction(f));
    public override string Text(bool nested = false) => string.Join(" + ", Parts.Select(p => p.Text(true)));
    public override ReqTerm? FirstUnmet(ProgressFacts f) => Parts.Select(p => p.FirstUnmet(f)).FirstOrDefault(t => t != null);
    public override IEnumerable<ReqTerm> Terms() => Parts.SelectMany(p => p.Terms());
}

public sealed record ReqAny(IReadOnlyList<ReqNode> Parts) : ReqNode
{
    public override bool Met(ProgressFacts f) => Parts.Any(p => p.Met(f));
    public override float Fraction(ProgressFacts f) => Parts.Count == 0 ? 0f : Parts.Max(p => p.Fraction(f));
    public override string Text(bool nested = false)
    {
        string s = string.Join($" {L.T("or")} ", Parts.Select(p => p.Text(true)));
        return nested ? $"({s})" : s;
    }
    public override ReqTerm? FirstUnmet(ProgressFacts f) =>
        Met(f) ? null : Parts.OrderByDescending(p => p.Fraction(f)).Select(p => p.FirstUnmet(f)).FirstOrDefault(t => t != null);
    public override IEnumerable<ReqTerm> Terms() => Parts.SelectMany(p => p.Terms());
}

/// <summary>
/// A requirement: the source string from <see cref="UnlockTable"/> and its parsed tree.
/// <para>Grammar: terms (<see cref="ReqKind"/>) joined by <c>+</c> (all of) and <c>|</c> (any of); <c>+</c> binds tighter than
/// <c>|</c>; parentheses group. Example: <c>level:30+stars:25+(rankup:overall:1|rankup:skill:3)</c>. Malformed input never
/// throws: bad terms become <see cref="ReqKind.Unknown"/> (never met), an empty string means "free".</para>
/// </summary>
public sealed record Requirement(string Source, ReqNode Root)
{
    public static readonly Requirement Free = Parse("free");
    public static readonly Requirement Never = new("", new ReqLeaf(new ReqTerm(ReqKind.Unknown, "")));

    public bool Met(ProgressFacts f) => Root.Met(f);
    public float Fraction(ProgressFacts f) => Root.Fraction(f);
    /// <summary>The whole requirement as text, translated now ("Reach level 15 + Earn 3 stars").</summary>
    public string Text() => Root.Text();

    public static Requirement Parse(string? source)
    {
        string s = (source ?? "").Trim();
        if (s.Length == 0) s = "free";
        int i = 0;
        var node = ParseAny(s, ref i);
        return new Requirement(s, node);
    }

    static ReqNode ParseAny(string s, ref int i)
    {
        var parts = new List<ReqNode> { ParseAll(s, ref i) };
        while (i < s.Length && s[i] == '|') { i++; parts.Add(ParseAll(s, ref i)); }
        return parts.Count == 1 ? parts[0] : new ReqAny(parts);
    }

    static ReqNode ParseAll(string s, ref int i)
    {
        var parts = new List<ReqNode> { ParseAtom(s, ref i) };
        while (i < s.Length && s[i] == '+') { i++; parts.Add(ParseAtom(s, ref i)); }
        return parts.Count == 1 ? parts[0] : new ReqAll(parts);
    }

    static ReqNode ParseAtom(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var inner = ParseAny(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            else return new ReqLeaf(ReqTerm.Parse("(unclosed")); // malformed: never met
            while (i < s.Length && s[i] == ' ') i++;
            return inner;
        }
        int start = i;
        while (i < s.Length && s[i] is not ('+' or '|' or '(' or ')')) i++;
        if (i < s.Length && s[i] == '(') { i = s.Length; return new ReqLeaf(ReqTerm.Parse("(misplaced")); }
        return new ReqLeaf(ReqTerm.Parse(s[start..i]));
    }
}

/// <summary>Per-line "with it" counters (runs, real PBs, badge rank-ups while that line was on the gun).</summary>
public sealed class WithLine : ProgressJson
{
    public int Runs, Pb, BadgeUp;
}

/// <summary>
/// The numbers requirements are checked against: a snapshot built by <see cref="Progress"/> from <see cref="ProgressFile"/>
/// (and, for the Progress core agent to add, from stats: best badge, badge drills, benchmark ranks, Sens Finder).
/// Stub: everything is zero / level 1 except what progress.json and the dev flags set.
/// </summary>
public sealed class ProgressFacts
{
    public int Level = 1, Stars, OverallUps, SkillUps, BadgeUps, PbStars, LockIns, StreakWeeks, BadgeDrills, WeeklyImprove;
    public bool CoachPlaced, SensFinderDone;
    /// <summary>Best badge tier ever earned (0 Rookie … 4 Pro), -1 = none.</summary>
    public int BestBadge = -1;
    /// <summary>[rank index] = drills whose benchmark rank is at least that rank (Bench.RankNames, 9 entries).</summary>
    public int[] BenchDrillsAtLeast = new int[9];
    public Dictionary<string, WithLine> With = new();
}
