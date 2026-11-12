using System.Globalization;
using ValTrainer.Core;
using ValTrainer.UI;
using ValTrainer.Warmup;

namespace ValTrainer.Progression;

/// <summary>
/// Daily and weekly challenges (progression-design §5). Generated deterministically from the day / week key and the
/// install salt (a restart never rerolls), reset at 04:00 local (weekly: Monday 04:00), no backlog, nothing to claim.
/// Daily: A coach focus · B quality vs yourself · C habit / variety (+100 XP each, +100 for all three, one reroll a day).
/// Weekly: W1 train on 3 days · W2 beat your 7-day baseline by 5 % in the focus drill (+1★) · W3 rotating (+500 XP each).
/// Targets are relative to the player's own history. Finders never count; Lock-In steps never count for score targets.
/// <para>Kinds (stable ids, stored): daily <c>focus_runs</c>, <c>match_avg</c>, <c>badge</c>, <c>accuracy</c>,
/// <c>variety</c>, <c>time</c> (minutes), <c>fresh</c>, <c>map_agent</c>, <c>lockin</c>; weekly <c>days</c>, <c>improve</c>,
/// <c>lockins</c>, <c>badgeups</c>, <c>skill_time</c> (Drill = the skill name), <c>pbs</c>. Value / Target = progress
/// (Target 1 = a yes/no challenge: match_avg / badge / accuracy / improve keep the number to reach in Baseline: the
/// average, the badge tier index, the accuracy %, the frozen baseline). The daily set's <c>Rerolls</c> = rerolls LEFT.</para>
/// </summary>
internal sealed partial class ProgressEngine
{
    public static readonly HashSet<string> DrillKinds = new(StringComparer.Ordinal) { "focus_runs", "match_avg", "accuracy", "fresh", "improve" };

    // ---------------- drill pools ----------------

    static List<(string Key, string Category)>? drills;

    /// <summary>Every playable drill key with its category (no finders, coach tools or Valorant-only screens).</summary>
    static List<(string Key, string Category)> Drills => drills ??= Modes.ModeRegistry.All
        .Select(f => { try { return f(); } catch { return null; } }).Where(m => m != null)
        .Select(m => (m!.Key, m.Category)).Where(x => !XpRules.IsFinder(x.Key) && x.Category is not ("Coach" or "Other" or "Valorant"))
        .GroupBy(x => x.Key).Select(g => g.First()).ToList();

    static bool ValidDrill(string key) => !XpRules.IsFinder(key) && Modes.ModeRegistry.Find(key) != null;

    static string CategoryOf(string mode) => DrillGuns.Proto(mode)?.Category ?? "";

    /// <summary>Drills that train a coach skill (weekly "train {skill} drills" challenge).</summary>
    public static IReadOnlyList<string> SkillDrills(string? skill) => skill switch
    {
        "Flicking" => new[] { "flick", "spider", "popup" },
        "Precision" => new[] { "microshot", "longtaps", "flick" },
        "Tracking" => new[] { "tracking", "strafebots" },
        "Reaction" => new[] { "reaction", "popup" },
        "CrosshairPlacement" => new[] { "peek", "peekduel", "jigglepeek" },
        "SprayControl" => new[] { "spray_vandal", "spray_phantom", "spray_transfer" },
        "Movement" => new[] { "counterstrafe", "strafeduel", "jumppeek" },
        "Utility" => new[] { "flashmap", "sound" },
        _ => Array.Empty<string>(),
    };

    static IReadOnlyList<RunRecord> History => Main.I?.Stats?.Runs ?? (IReadOnlyList<RunRecord>)Array.Empty<RunRecord>();

    static int PlayerTier => Math.Clamp(Main.I?.Settings?.Tier ?? 1, 0, 4);

    /// <summary>Drills not played within <paramref name="days"/> days (or never).</summary>
    static List<string> NotPlayedSince(DateTime now, int days)
    {
        var recent = new HashSet<string>(History.Where(r => r != null && (now - r.When).TotalDays <= days).Select(r => r.Mode), StringComparer.Ordinal);
        return Drills.Select(d => d.Key).Where(k => !recent.Contains(k)).ToList();
    }

    // ---------------- generation ----------------

    /// <summary>Makes today's / this week's sets when their key moved forward (never backward: the clock going back
    /// keeps the newer set).</summary>
    public void EnsureChallenges(DateTime now)
    {
        string dk = ProgressTime.DayKey(now);
        if (F.Daily?.Key == null || string.CompareOrdinal(dk, F.Daily.Key) > 0) F.Daily = MakeDaily(dk, now);
        string wk = ProgressTime.WeekKey(now);
        if (F.Weekly?.Key == null || string.CompareOrdinal(wk, F.Weekly.Key) > 0) F.Weekly = MakeWeekly(wk, now);
    }

    Random Rng(string key, string extra = "") => new(ProgressTime.Seed(key + "|" + F.Salt + extra));

    ChallengeSet MakeDaily(string dayKey, DateTime now)
    {
        var set = new ChallengeSet { Key = dayKey, Rerolls = 1 }; // Rerolls = rerolls LEFT today
        set.Items.Add(FocusItem(Rng(dayKey, "a"), now, null));
        set.Items.Add(QualityItem(Rng(dayKey, "b"), now, null));
        set.Items.Add(HabitItem(Rng(dayKey, "c"), now, dayKey, null));
        return set;
    }

    /// <summary>Slot A: the coach's focus drill (its top problem), else the first starter drill still short of its target,
    /// else a drill not played in 7 days.</summary>
    ChallengeItem FocusItem(Random rng, DateTime now, ChallengeItem? not)
    {
        var pool = new List<(string Drill, string Why, string? WhyText)>();
        var c = F.Coach;
        if (c?.FocusDrills != null) pool.AddRange(c.FocusDrills.Where(ValidDrill).Select(d => (d, c.FocusWhy ?? "coach", c.FocusWhyText)));
        foreach (var (key, target, _) in CoachData.Starter)
            if (ValidDrill(key) && CoachData.CoachedRuns(key) < target) pool.Add((key, "starter", null));
        if (pool.Count == 0)
        {
            var fresh = NotPlayedSince(now, 7);
            if (fresh.Count > 0) pool.Add((fresh[rng.Next(fresh.Count)], "fresh", null));
        }
        if (pool.Count == 0) pool.Add(("flick", "fresh", null));
        var pick = pool.FirstOrDefault(p => p.Drill != not?.Drill);
        if (pick.Drill == null) pick = pool[0];
        return new ChallengeItem { Kind = "focus_runs", Drill = pick.Drill, Target = 3, Xp = XpRules.DailyXp, Why = pick.Why, WhyText = pick.WhyText };
    }

    /// <summary>Slot B: quality against yourself (your 7-day average, your typical badge, your 7-day accuracy).</summary>
    ChallengeItem QualityItem(Random rng, DateTime now, ChallengeItem? not)
    {
        int tier = PlayerTier;
        var options = new List<ChallengeItem>();
        var week = History.Where(r => r != null && !r.Warmup && !XpRules.IsFinder(r.Mode) && r.Tier == tier && (now - r.When).TotalDays <= 7).ToList();
        var byUse = week.GroupBy(r => r.Mode).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var g in byUse.Take(4))
        {
            var (b, kind, _) = WarmupScore.Baseline(g.Key, tier, now);
            if (kind == "7d" && b > 0)
            {
                options.Add(new ChallengeItem { Kind = "match_avg", Drill = g.Key, Baseline = b, Target = 1, Tier = tier, Xp = XpRules.DailyXp });
                break;
            }
        }
        var badges = History.Where(r => r != null && r.Badge >= 0).TakeLast(20).Select(r => (float)r.Badge).ToList();
        int typical = badges.Count == 0 ? 0 : (int)Math.Floor(Median(badges));
        options.Add(new ChallengeItem { Kind = "badge", Baseline = typical, Target = 1, Xp = XpRules.DailyXp });
        foreach (var g in byUse)
        {
            var acc = g.Where(r => r.Accuracy >= 0).Select(r => r.Accuracy).ToList();
            if (acc.Count < 2) continue;
            int pct = (int)Math.Floor(acc.Average() * 100f);
            if (pct < 1) continue;
            options.Add(new ChallengeItem { Kind = "accuracy", Drill = g.Key, Baseline = pct, Target = 1, Tier = tier, Xp = XpRules.DailyXp });
            break;
        }
        if (not != null && options.Count > 1) options.RemoveAll(o => o.Kind == not.Kind);
        return options[rng.Next(options.Count)];
    }

    /// <summary>Slot C: habit / variety.</summary>
    ChallengeItem HabitItem(Random rng, DateTime now, string dayKey, ChallengeItem? not)
    {
        var options = new List<ChallengeItem>
        {
            new() { Kind = "variety", Target = 3, Xp = XpRules.DailyXp },
            new() { Kind = "time", Target = 10, Xp = XpRules.DailyXp },
        };
        var fresh = NotPlayedSince(now, 14);
        if (fresh.Count > 0 && History.Count > 0) options.Add(new() { Kind = "fresh", Drill = fresh[rng.Next(fresh.Count)], Target = 1, Xp = XpRules.DailyXp });
        if (Drills.Any(d => d.Category is "Map" or "Agents")) options.Add(new() { Kind = "map_agent", Target = 1, Xp = XpRules.DailyXp });
        int dayIndex = ProgressTime.ParseDay(dayKey) is { } day ? (int)(day - new DateTime(2026, 1, 1)).TotalDays : 0;
        if (F.Counters.LockIns > 0 && ((dayIndex % 4) + 4) % 4 == 0) options.Add(new() { Kind = "lockin", Target = 1, Xp = XpRules.DailyXp });
        if (not != null && options.Count > 1) options.RemoveAll(o => o.Kind == not.Kind && o.Drill == not.Drill);
        return options[rng.Next(options.Count)];
    }

    ChallengeSet MakeWeekly(string weekKey, DateTime now)
    {
        var set = new ChallengeSet { Key = weekKey };
        set.Items.Add(new ChallengeItem { Kind = "days", Target = 3, Xp = XpRules.WeeklyXp });
        int tier = PlayerTier;
        var focus = FocusItem(Rng(weekKey, "w2"), now, null);
        var (b, kind, _) = focus.Drill != null ? WarmupScore.Baseline(focus.Drill, tier, now) : (-1f, "", 0);
        set.Items.Add(new ChallengeItem
        {
            Kind = "improve", Drill = focus.Drill, Baseline = kind is "7d" or "recent" && b > 0 ? b : 0, Target = 1, Tier = tier,
            Xp = XpRules.WeeklyXp, Stars = 1, Why = focus.Why, WhyText = focus.WhyText,
        });
        int week = ProgressTime.WeekMonday(weekKey) is { } m ? ISOWeek.GetWeekOfYear(m) : 0;
        var rot = new List<ChallengeItem>();
        if (F.Counters.LockIns > 0) rot.Add(new ChallengeItem { Kind = "lockins", Target = 2, Xp = XpRules.WeeklyXp });
        rot.Add(new ChallengeItem { Kind = "badgeups", Target = 2, Xp = XpRules.WeeklyXp });
        if (SkillDrills(F.Coach?.Weakest).Count > 0) rot.Add(new ChallengeItem { Kind = "skill_time", Why = F.Coach!.Weakest, Drill = F.Coach.Weakest, Target = 20, Xp = XpRules.WeeklyXp });
        rot.Add(new ChallengeItem { Kind = "pbs", Target = 3, Xp = XpRules.WeeklyXp });
        set.Items.Add(rot[week % rot.Count]);
        return set;
    }

    /// <summary>Replaces daily slot <paramref name="slot"/> (0–2) with another challenge of the same slot. One a day; a
    /// finished challenge can't be rerolled.</summary>
    public bool Reroll(int slot, DateTime now)
    {
        EnsureChallenges(now);
        var set = F.Daily!;
        if (set.Rerolls <= 0 || slot < 0 || slot >= set.Items.Count || set.Items[slot].Done) return false;
        var old = set.Items[slot];
        var rng = Rng(set.Key!, "r" + slot.ToString(CultureInfo.InvariantCulture));
        var item = slot switch { 0 => FocusItem(rng, now, old), 1 => QualityItem(rng, now, old), _ => HabitItem(rng, now, set.Key!, old) };
        if (item.Kind == old.Kind && item.Drill == old.Drill && item.Baseline == old.Baseline) return false;
        set.Items[slot] = item;
        set.Rerolls--;
        return true;
    }

    /// <summary>Open challenge drills are exempt from the variety decay (focused practice is good), and so is the coach's
    /// focus drill.</summary>
    bool Exempt(string mode)
    {
        if (F.Coach?.FocusDrills is { Count: > 0 } fd && fd[0] == mode) return true;
        foreach (var set in new[] { F.Daily, F.Weekly })
            if (set != null && set.Items.Any(i => !i.Done && i.Drill == mode && DrillKinds.Contains(i.Kind))) return true;
        return false;
    }

    // ---------------- ticking ----------------

    void Done(ChallengeItem i, bool daily, List<XpPart>? parts, List<string>? reasons)
    {
        i.Done = true;
        i.Value = Math.Max(i.Value, i.Target);
        Grant(parts, daily ? L.N("Daily") : L.N("Weekly"), i.Xp);
        if (daily) F.Counters.Dailies++; else F.Counters.Weeklies++;
        if (i.Stars > 0)
        {
            if (i.Kind == "improve") F.Counters.WeeklyImprove++;
            Star(i.Stars, "WEEKLY", reasons, () => L.T("WEEKLY IMPROVEMENT"));
        }
        if (daily && F.Daily != null && F.Daily.Items.All(x => x.Done)) Grant(parts, L.N("All dailies"), XpRules.AllDailiesXp);
    }

    void TickRun(RunFacts run, string dayKey, DayInfo d, float active, bool pb, bool badgeUp, List<XpPart>? parts, List<string>? reasons, List<ChallengeTick> ticks)
    {
        var rec = run.Record;
        string mode = rec.Mode ?? "";
        bool scored = run.Stored && !run.LockInStep && !run.Adaptive; // score targets: real fixed-tier runs only
        float mins = active / 60f;
        foreach (var (set, daily) in new[] { (F.Daily, true), (F.Weekly, false) })
        {
            if (set == null) continue;
            foreach (var i in set.Items)
            {
                if (i.Done) continue;
                float before = i.Value;
                switch (i.Kind)
                {
                    case "focus_runs": if (mode == i.Drill) i.Value++; break;
                    case "match_avg":
                        if (scored && mode == i.Drill && rec.Tier == i.Tier && WarmupScore.IndexOf(mode, WarmupScore.Value(rec), i.Baseline) >= 100f) i.Value = 1;
                        break;
                    case "badge": if (scored && rec.Badge >= 0 && rec.Badge >= i.Baseline) i.Value = 1; break;
                    case "accuracy": if (scored && mode == i.Drill && rec.Accuracy >= 0 && rec.Accuracy * 100f >= i.Baseline) i.Value = 1; break;
                    case "variety": i.Value = d.Drills?.Count ?? 0; break;
                    case "time": i.Value += mins; break;
                    case "fresh": if (mode == i.Drill) i.Value = 1; break;
                    case "map_agent": if (CategoryOf(mode) is "Map" or "Agents") i.Value = 1; break;
                    case "days": i.Value = TrainingDaysInWeek(dayKey); break;
                    case "improve":
                        if (scored && mode == i.Drill && rec.Tier == i.Tier && WarmupScore.Value(rec) is var v && v > 0)
                        {
                            if (i.Baseline <= 0) i.Baseline = v; // no history: this week's first run is the baseline
                            else if (WarmupScore.IndexOf(mode, v, i.Baseline) >= 105f) i.Value = 1;
                        }
                        break;
                    case "badgeups": if (badgeUp) i.Value++; break;
                    case "skill_time": if (SkillDrills(i.Drill).Contains(mode)) i.Value += mins; break;
                    case "pbs": if (pb && !run.LockInStep) i.Value++; break;
                }
                bool done = i.Value >= i.Target - 1e-4f;
                if (done) Done(i, daily, parts, reasons);
                if (i.Value != before || done) ticks.Add(new ChallengeTick(Progress.ChallengeTitle(i), (int)Math.Floor(i.Value + 1e-4f), (int)i.Target, i.Done));
            }
        }
    }

    void TickLockIn(List<XpPart>? parts, List<ChallengeTick>? ticks)
    {
        foreach (var (set, daily) in new[] { (F.Daily, true), (F.Weekly, false) })
        {
            if (set == null) continue;
            foreach (var i in set.Items.Where(i => !i.Done && i.Kind is "lockin" or "lockins"))
            {
                i.Value++;
                if (i.Value >= i.Target) Done(i, daily, parts, null);
                ticks?.Add(new ChallengeTick(Progress.ChallengeTitle(i), (int)i.Value, (int)i.Target, i.Done));
            }
        }
    }
}
