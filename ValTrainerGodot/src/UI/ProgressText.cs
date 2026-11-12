using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;
using ValTrainer.Modes;
using ValTrainer.Progression;

namespace ValTrainer.UI;

/// <summary>
/// What the progression UI shows, read from the public <see cref="Progress"/> API (progression-design-1.19.md §10; the
/// contract: docs/research/skins2-contracts.md). Challenge titles, "why" lines and progress text come from the core
/// (<see cref="Progress.ChallengeTitle"/>, <see cref="Progress.ChallengeWhy"/>, <see cref="Progress.ChallengeProgress"/>).
/// <para>Dev (screenshots, never saved): <c>--progress-demo</c> fills challenges, streak and coach status with made-up
/// data when progress.json has none; see also <see cref="ProgressToasts"/> and <see cref="ProgressReveal"/>.</para>
/// </summary>
public sealed class ProgressView
{
    public sealed record Challenge(int Slot, string Kind, string Title, string? Why, int Value, int Target, bool Done, int Xp, int Stars, string? Drill,
                                   string? Prog = null);

    public readonly List<Challenge> Daily = new(), Weekly = new();
    public int RerollsLeft;
    public int StreakWeeks, StreakBest, StreakTokens;
    public int Rested;
    /// <summary>Overall coach rank index (CoachRank.Names) at placement / confirmed high, -1 = not placed.</summary>
    public int CoachPlaced = -1, CoachHigh = -1;
    public int OverallUps, SkillUps;
    /// <summary>Valid coach sample days in the current calibration (placement needs 3).</summary>
    public int CoachDays;
    /// <summary>The coach calibration changed since the samples were taken: re-placing.</summary>
    public bool Recalibrated;
    public bool Demo;

    public int DailyDone => Daily.Count(c => c.Done);

    public static bool DevDemo => CmdLine.Dev && CmdLine.Has("--progress-demo");

    /// <summary>A snapshot for drawing (cheap: a few list reads). Translated now: build it at build / draw time.</summary>
    public static ProgressView Now()
    {
        var d = Progress.Data;
        var v = new ProgressView { Rested = Progress.Rested };
        var daily = Progress.Daily; // makes today's / this week's challenges on first use
        for (int i = 0; i < daily.Count; i++) v.Daily.Add(From(i, daily[i]));
        var weekly = Progress.Weekly;
        for (int i = 0; i < weekly.Count; i++) v.Weekly.Add(From(i, weekly[i]));
        v.RerollsLeft = Progress.RerollsLeft;
        v.StreakWeeks = Progress.StreakWeeks; v.StreakBest = Math.Max(Progress.StreakBest, Progress.StreakWeeks); v.StreakTokens = Progress.StreakTokens;
        v.Recalibrated = Progress.CoachRecalibrated;
        var (rank, days, _) = Progress.CoachTrack();
        if (rank >= 0)
        {
            v.CoachHigh = rank;
            v.CoachPlaced = d.Coach?.Placed != null && d.Coach.Placed.TryGetValue("overall", out var p) ? p : rank;
        }
        else v.CoachDays = days;
        if (d.Coach is { } c) { v.OverallUps = c.OverallUps; v.SkillUps = c.SkillUps; }
        if (DevDemo && v.Daily.Count == 0 && v.Weekly.Count == 0) v.FillDemo();
        return v;
    }

    static Challenge From(int slot, ChallengeItem it) =>
        new(slot, it.Kind, Progress.ChallengeTitle(it), Progress.ChallengeWhy(it) is { Length: > 0 } why ? why : null,
            (int)MathF.Floor(it.Value), Math.Max(1, (int)MathF.Ceiling(it.Target)), it.Done, it.Xp, it.Stars, it.Drill, Progress.ChallengeProgress(it));

    /// <summary>Dev <c>--progress-demo</c>: plausible challenges, streak and coach status (nothing is stored).</summary>
    void FillDemo()
    {
        Demo = true;
        // made-up items through the same text as real ones (dev only: the "why" lines are English data)
        Challenge C(int slot, string kind, string? drill, float target, float value, int xp, int stars = 0, string? why = null, float baseline = 0)
        {
            var it = new ChallengeItem { Kind = kind, Drill = drill, Target = target, Value = value, Done = value >= target, Xp = xp, Stars = stars, Baseline = baseline, WhyText = why };
            return From(slot, it);
        }
        Daily.Add(C(0, "focus_runs", "flick", 3, 1, 100, why: "slow micro-adjustments"));
        Daily.Add(C(1, "match_avg", "tracking", 1, 0, 100, baseline: 12400));
        Daily.Add(C(2, "variety", null, 3, 3, 100));
        RerollsLeft = 1;
        Weekly.Add(C(0, "days", null, 3, 2, 500));
        Weekly.Add(C(1, "improve", "flick", 1, 0, 500, 1));
        Weekly.Add(C(2, "pbs", null, 3, 1, 500));
        StreakWeeks = 3; StreakBest = 5; StreakTokens = 1;
        if (Rested == 0) Rested = 600;
        CoachPlaced = 4; CoachHigh = 4; SkillUps = 2; CoachDays = 6;
    }
}

/// <summary>Display text and formatting for progression (translated where called; never in static initializers).</summary>
public static class ProgressText
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Dictionary<string, string?> drillNames = new();
    static string drillLang = "";

    public static string Num(int n) => n.ToString("#,0", Inv);

    /// <summary>A drill's display name (translated), null for unknown keys.</summary>
    public static string? DrillName(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!ReferenceEquals(drillLang, L.Lang)) { drillNames.Clear(); drillLang = L.Lang; }
        if (drillNames.TryGetValue(key, out var n)) return n;
        n = ModeRegistry.Find(key) is { } make ? make().DisplayName : null;
        drillNames[key] = n;
        return n;
    }

    /// <summary>Local 04:00 day boundary (design §5): time until the next daily / weekly (Monday 04:00) reset.</summary>
    public static TimeSpan ResetIn(bool weekly, DateTime now)
    {
        var next = now.Date.AddHours(4);
        if (next <= now) next = next.AddDays(1);
        if (weekly) while (next.DayOfWeek != DayOfWeek.Monday) next = next.AddDays(1);
        return next - now;
    }

    public static string ResetText(bool weekly)
    {
        var t = ResetIn(weekly, DateTime.Now);
        int d = (int)t.TotalDays, h = t.Hours, m = Math.Max(1, t.Minutes);
        return d > 0 ? L.F($"Resets in {d} d {h} h") : h > 0 ? L.F($"Resets in {h} h {m} min") : L.F($"Resets in {m} min");
    }

    public static Color EditionColor(Edition e) => e switch
    {
        Edition.Core => Color.Color8(110, 168, 228),
        Edition.Signature => Color.Color8(52, 196, 158),
        _ => Color.Color8(250, 210, 96),
    };

    /// <summary>The level emblem's border colour: a new one every 10 levels.</summary>
    public static Color LevelColor(int level) => (level / 10) switch
    {
        0 => Color.Color8(176, 186, 196),
        1 => Color.Color8(74, 198, 208),
        2 => Color.Color8(196, 142, 250),
        3 => Color.Color8(250, 210, 96),
        4 => Color.Color8(255, 96, 112),
        _ => Color.Color8(255, 240, 200),
    };

    public static readonly Color StarColor = Color.Color8(255, 206, 84);
    /// <summary>Rested XP on the bar (a second colour).</summary>
    public static readonly Color RestedColor = Color.Color8(120, 150, 255);

    public static SkinLine? LineOf(string itemId) => ItemIds.TryParse(itemId, out var line, out _, out _) ? SkinCatalog.Get(line) : null;

    /// <summary>"Carbon Weave", "Foundry · Level 3", "Foundry · Blue Flame" (translated).</summary>
    public static string ItemName(string itemId)
    {
        if (!ItemIds.TryParse(itemId, out var line, out int level, out var variant)) return L.Raw(itemId);
        var l = SkinCatalog.Get(line);
        string name = l != null ? L.T(l.Name) : L.Raw(line);
        if (variant != null) return name + " · " + (l?.Variant(variant) is { } v ? L.T(v.Name) : L.Raw(variant));
        return level > 1 ? name + " · " + L.F($"Level {level}") : name;
    }

    /// <summary>Short chip text for the level track: the base name, "Lv 3" or the variant name.</summary>
    public static string ItemShort(string itemId)
    {
        if (!ItemIds.TryParse(itemId, out var line, out int level, out var variant)) return L.Raw(itemId);
        var l = SkinCatalog.Get(line);
        string name = l != null ? L.T(l.Name) : L.Raw(line);
        if (variant != null) return l?.Variant(variant) is { } v ? L.T(v.Name) : L.Raw(variant);
        return level > 1 ? name + " " + L.F($"Lv {level}") : name;
    }

    /// <summary>"SIGNATURE" (the tier, translated, caps); "" for unknown items.</summary>
    public static string EditionCaps(string itemId) => LineOf(itemId) is { } l ? L.Upper(L.T(SkinCatalog.EditionName(l.Edition))) : "";

    public static Color ItemColor(string itemId) => LineOf(itemId) is { } l ? EditionColor(l.Edition) : UiTheme.Dim;

    /// <summary>Items whose requirement asks for exactly level <paramref name="level"/> (the level track's rewards), with
    /// the stars they also need (0 = none). Cached per catalogue (requirements don't change at runtime).</summary>
    public static IReadOnlyList<(string Id, int Stars)> RewardsAt(int level)
    {
        if (byLevel == null)
        {
            byLevel = new Dictionary<int, List<(string, int)>>();
            foreach (var id in Progress.AllItems)
            {
                var terms = Progress.RequirementOf(id).Root.Terms().ToList();
                var lv = terms.FirstOrDefault(t => t.Kind == ReqKind.Level);
                if (lv == null) continue;
                int stars = terms.Where(t => t.Kind == ReqKind.Stars).Select(t => t.N).DefaultIfEmpty(0).Max();
                if (!byLevel.TryGetValue(lv.N, out var list)) byLevel[lv.N] = list = new();
                list.Add((id, stars));
            }
        }
        return byLevel.TryGetValue(level, out var r) ? r : (IReadOnlyList<(string, int)>)Array.Empty<(string, int)>();
    }
    static Dictionary<int, List<(string, int)>>? byLevel;

    /// <summary>A play-time total: "3 h 32 min" / "45 min".</summary>
    public static string Duration(long secs)
    {
        long m = Math.Max(0, secs) / 60;
        return m >= 60 ? L.F($"{m / 60} h {m % 60} min") : L.F($"{m} min");
    }

    /// <summary>"XP" chip label for a run breakdown part (English data from the core, translated here, caps).</summary>
    public static string PartLabel(string english) => L.Upper(L.T(english));
}
