using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>
/// Local day and week keys (progression-design §5): a day starts at <b>04:00</b> local (late-night sessions don't split at
/// midnight), a week on Monday 04:00 (ISO weeks: "2026-W45"). Keys sort as text, so "later" is a string comparison.
/// </summary>
public static class ProgressTime
{
    public const int DayStartHour = 4;

    /// <summary>The training day a moment belongs to (its date; 03:59 still counts as the day before).</summary>
    public static DateTime Day(DateTime t) => t.AddHours(-DayStartHour).Date;

    public static string DayKey(DateTime t) => Key(Day(t));
    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string WeekKey(DateTime t) => WeekKeyOfDay(Day(t));
    public static string WeekKeyOfDay(DateTime day) =>
        string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(day):0000}-W{ISOWeek.GetWeekOfYear(day):00}");

    public static DateTime? ParseDay(string? key) =>
        DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Monday of an ISO week key, null when malformed.</summary>
    public static DateTime? WeekMonday(string? key)
    {
        if (key == null || key.Length != 8 || key[4] != '-' || key[5] != 'W') return null;
        if (!int.TryParse(key[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(key[6..], NumberStyles.None, CultureInfo.InvariantCulture, out int w) || w < 1 || w > 53) return null;
        try { return ISOWeek.ToDateTime(y, w, DayOfWeek.Monday); } catch { return null; }
    }

    /// <summary>Whole days from day key <paramref name="a"/> to <paramref name="b"/> (0 when either is malformed).</summary>
    public static int DaysBetween(string? a, string? b) =>
        ParseDay(a) is { } x && ParseDay(b) is { } y ? (int)Math.Round((y - x).TotalDays) : 0;

    /// <summary>Whole weeks from week key <paramref name="a"/> to <paramref name="b"/> (0 when either is malformed).</summary>
    public static int WeeksBetween(string? a, string? b) =>
        WeekMonday(a) is { } x && WeekMonday(b) is { } y ? (int)Math.Round((y - x).TotalDays / 7) : 0;

    /// <summary>The next daily reset (04:00 local) after <paramref name="now"/>.</summary>
    public static DateTime NextDayReset(DateTime now) => Day(now).AddDays(1).AddHours(DayStartHour);

    /// <summary>The next weekly reset (Monday 04:00 local) after <paramref name="now"/>.</summary>
    public static DateTime NextWeekReset(DateTime now)
    {
        var d = Day(now);
        int toMonday = ((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(toMonday == 0 ? 7 : toMonday).AddHours(DayStartHour);
    }

    /// <summary>Stable 32-bit FNV-1a (string.GetHashCode is randomised per process: challenges must not reroll on restart).</summary>
    public static int Seed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}

/// <summary>
/// The per-run XP rules of progression-design §3 (pure functions; the engine applies them).
/// </summary>
public static class XpRules
{
    public const int PerMinute = 100;
    public const float MaxActiveSecs = 300f;
    public const int DailySoftCap = 2500;
    public const int FirstOfDay = 150;
    public const int RestedPerDay = 300, RestedCap = 1500;
    public const int PbXp = 50, PbXpPerDay = 5;
    public const int BadgeUpXp = 100;
    public const int FinderXp = 50;
    public const int Welcome = 250;
    public const int DailyXp = 100, AllDailiesXp = 100, WeeklyXp = 500;
    public const int PlacementXp = 300, SkillRankUpXp = 300, OverallRankUpXp = 1000;
    public const int SkillRankUpStars = 2, OverallRankUpStars = 5;
    public const int PbStarsPerDay = 3;
    /// <summary>Real PB: at least this many earlier runs at the drill + tier + gun, and this much better.</summary>
    public const int RealPbRuns = 3;
    public const float RealPbGain = 0.02f;
    /// <summary>Retro: share of a run's length counted as active (no telemetry for old runs).</summary>
    public const float RetroActiveShare = 0.9f;

    /// <summary>
    /// A real run: the one rule for what counts in stats.json and therefore for "real improvement" (PBs, PB XP, PB / badge
    /// stars, the PB index, score-target challenges such as <c>improve</c> and <c>match_avg</c>): kept in stats
    /// (<c>KeepInStats</c>: not sens-shifted), a fixed-tier run (not adaptive), and played at the drill's own full length
    /// (<paramref name="playedSecs"/> = the session's run length) or, for untimed drills, completed by the drill's own rules.
    /// A shortened timed run (a quick Lock-In step, dev <c>--duration</c>) is not real: its per-minute score isn't comparable.
    /// </summary>
    public static bool RealRun(bool keepInStats, bool adaptive, bool timed, float drillSecs, float playedSecs) =>
        keepInStats && !adaptive && (!timed || playedSecs >= drillSecs - 1e-3f);

    public const string SensFinderKey = "sensfinder", XhairFinderKey = "xhairfinder";
    public static bool IsFinder(string? mode) => mode is SensFinderKey or XhairFinderKey or "crosshairfinder";

    /// <summary>1.0 Rookie · 1.1 Regular · 1.2 Veteran · 1.3 Elite · 1.4 Pro (tier −1 / none: 1.0).</summary>
    public static float TierFactor(int tier) => tier is >= 0 and <= 4 ? 1f + 0.1f * tier : 1f;

    /// <summary>n-th run of a drill today: 1–3 → 1.0 · 4–6 → 0.75 · 7+ → 0.5.</summary>
    public static float Variety(int nth) => nth <= 3 ? 1f : nth <= 6 ? 0.75f : 0.5f;

    /// <summary>0 idle (no shots, kills or score and under 15 active seconds) · 0.5 spray-and-pray · else 1.</summary>
    public static float Engagement(RunRecord r, float activeSecs)
    {
        if (r.Shots == 0 && r.Kills == 0 && r.Score <= 0 && activeSecs < 15f) return 0f;
        if (r.Shots >= 60 && r.Accuracy >= 0 && r.Accuracy < 0.10f) return 0.5f;
        return 1f;
    }

    /// <summary>Run XP before the daily taper.</summary>
    public static int RunXp(float activeSecs, float e, float t, float v) =>
        (int)MathF.Round(PerMinute * Math.Clamp(activeSecs, 0f, MaxActiveSecs) / 60f * e * t * v);

    /// <summary>The part of <paramref name="runXp"/> kept after the taper (beyond <see cref="DailySoftCap"/> run XP in a day
    /// it counts half), given the day's run XP before it.</summary>
    public static int Taper(int dayRunXpBefore, int runXp)
    {
        int over = Math.Max(0, dayRunXpBefore + runXp - DailySoftCap) - Math.Max(0, dayRunXpBefore - DailySoftCap);
        return runXp - over / 2;
    }

    /// <summary>Completion bonus of a Lock-In preset (quick/short +150 · pro +400 · others +300).</summary>
    public static int LockInBonus(string? preset) => (preset ?? "").ToLowerInvariant() switch
    {
        "quick" or "short" => 150,
        "pro" => 400,
        _ => 300,
    };

    /// <summary>Weekly streak milestones (weeks → XP).</summary>
    public static readonly (int Weeks, int Xp)[] StreakMilestones = { (2, 500), (4, 750), (8, 1000), (12, 1250), (26, 1500), (52, 2000) };

    /// <summary>Retro length of a stored run: the coach's run.secs, else the drill's normal length (60 s when unknown).</summary>
    public static float SecsOf(RunRecord r)
    {
        if (r.Metrics != null && r.Metrics.TryGetValue("run.secs", out var s) && s > 1) return s;
        var p = DrillGuns.Proto(r.Mode ?? "");
        return p != null && p.Timed ? p.Duration : 60f;
    }

    /// <summary>Active seconds of a run without telemetry (retro / catch-up).</summary>
    public static float RetroActive(RunRecord r) => RetroActiveShare * SecsOf(r);

    /// <summary>Active seconds of a run from its telemetry: 1-s buckets with mouse movement, firing or key-driven movement
    /// faster than 0.5 m/s.</summary>
    public static float ActiveSeconds(Game.RunTelemetry? t)
    {
        if (t == null || t.Frames.Count == 0) return 0f;
        var buckets = new HashSet<int>();
        foreach (var f in t.Frames)
        {
            if (f.T < 0) continue;
            if (MathF.Abs(f.Dx) + MathF.Abs(f.Dy) > 0 || f.Has(Game.FrameFlags.Firing) || f.Speed > 0.5f) buckets.Add((int)f.T);
        }
        return buckets.Count;
    }
}

/// <summary>
/// What the stats history says, for PB / badge detection and the stats-derived requirement terms (badge:, drills:,
/// bench:, finder:sens). Fed in time order by the retro replay and incrementally by live runs, so both see the same
/// history. Main thread (reads drill defaults through <see cref="DrillGuns"/>).
/// </summary>
internal sealed class RunIndex
{
    readonly Dictionary<string, (int N, int Best)> at = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> badge = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> lastPlayed = new(StringComparer.Ordinal);
    public bool SensFinder;
    public int Count;
    /// <summary>Badge tier → the benchmark rank it proves (<c>bench:&lt;rank&gt;:N</c>): the bottom of the tier's rank range
    /// (Rookie = Iron–Bronze → Iron, Regular → Silver, Veteran → Platinum, Elite → Ascendant, Pro → Radiant), so a
    /// challenge's rank is reached, never just touched.</summary>
    public static readonly int[] BadgeRank = { 0, 2, 4, 6, 8 };

    public void Clear() { at.Clear(); badge.Clear(); lastPlayed.Clear(); SensFinder = false; Count = 0; }

    static string Key(RunRecord r) => string.Create(CultureInfo.InvariantCulture, $"{r.Mode}|{r.Tier}|{DrillGuns.GunOf(r)}");

    /// <summary>Earlier runs and the best score at this run's drill + tier + gun (-1 = none).</summary>
    public (int N, int Best) At(RunRecord r) => at.TryGetValue(Key(r), out var v) ? v : (0, -1);

    /// <summary>Best rated badge earned in a drill (-1 = none).</summary>
    public int BestBadge(string mode) => badge.TryGetValue(mode, out var b) ? b : -1;

    public DateTime? LastPlayed(string mode) => lastPlayed.TryGetValue(mode, out var t) ? t : null;

    public void Add(RunRecord r)
    {
        string mode = r.Mode ?? "";
        var k = Key(r);
        var (n, best) = at.TryGetValue(k, out var v) ? v : (0, -1);
        at[k] = (n + 1, Math.Max(best, r.Score));
        if (r.Badge >= 0 && r.Badge > BestBadge(mode) && DrillGuns.IsRated(r)) badge[mode] = Math.Min(r.Badge, 4);
        if (!lastPlayed.TryGetValue(mode, out var lp) || r.When > lp) lastPlayed[mode] = r.When;
        if (mode == XpRules.SensFinderKey) SensFinder = true;
        Count++;
    }

    public int BestBadgeAny => badge.Count == 0 ? -1 : badge.Values.Max();
    public int BadgeDrills => badge.Values.Count(b => b >= 0);

    public int[] BenchAtLeast()
    {
        var a = new int[9];
        foreach (var b in badge.Values)
        {
            if (b < 0) continue;
            int top = BadgeRank[Math.Clamp(b, 0, 4)];
            for (int r = 0; r <= top; r++) a[r]++;
        }
        return a;
    }
}
