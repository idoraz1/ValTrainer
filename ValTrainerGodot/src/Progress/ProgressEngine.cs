using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>
/// The progression rules over one <see cref="ProgressFile"/> (progression-design §3–§7): run XP, bonuses, stars, the
/// weekly streak, Lock-Ins, coach confirmation (CoachTrack.cs), challenges (Challenges.cs) and the retro credit
/// (Retro.cs). <see cref="Progress"/> owns the live instance; self-tests and the dev simulator make their own. The live
/// game and the retro replay call the same <see cref="ScoreRun"/>, so retro equals live.
/// </summary>
internal sealed partial class ProgressEngine
{
    public ProgressFile F;
    /// <summary>The stats history seen so far (PB / badge detection, stats-derived requirement terms).</summary>
    public readonly RunIndex Index = new();
    /// <summary>Daily / weekly challenges (off in the retro replay and the retro-equals-live test).</summary>
    public bool ChallengesOn = true;
    /// <summary>XP and stars per source since this engine was made (retro report, sim).</summary>
    public readonly Dictionary<string, int> XpBy = new(StringComparer.Ordinal), StarsBy = new(StringComparer.Ordinal);
    /// <summary>Unlocks recorded but not yet announced (the facade raises <see cref="Progress.Unlocked"/>).</summary>
    public readonly List<UnlockInfo> PendingUnlocks = new();
    /// <summary>Live: stats runs already fed into <see cref="Index"/>.</summary>
    int statsSeen;

    public ProgressEngine(ProgressFile f) { F = f; }

    // ---------------- bookkeeping ----------------

    void Grant(List<XpPart>? parts, string label, int xp, string? group = null)
    {
        if (xp == 0) return;
        F.Xp = Math.Max(0, F.Xp + xp);
        parts?.Add(new XpPart(label, xp));
        string g = group ?? label;
        XpBy[g] = (XpBy.TryGetValue(g, out var v) ? v : 0) + xp;
    }

    void Star(int n, string group, List<string>? reasons, Func<string>? reason)
    {
        if (n <= 0) return;
        F.Stars += n;
        StarsBy[group] = (StarsBy.TryGetValue(group, out var v) ? v : 0) + n;
        if (reasons != null && reason != null) reasons.Add(reason());
    }

    public static float Bar(int xp)
    {
        int l = XpCurve.LevelOf(xp);
        return (xp - XpCurve.TotalFor(l)) / (float)XpCurve.CostOf(l);
    }

    /// <summary>The day key of a moment; never earlier than the last training day (the clock going back changes nothing).</summary>
    public string DayKeyFor(DateTime when)
    {
        string k = ProgressTime.DayKey(when);
        return F.LastActiveDay != null && string.CompareOrdinal(k, F.LastActiveDay) < 0 ? F.LastActiveDay : k;
    }

    DayInfo DayOf(string key)
    {
        F.Days ??= new();
        if (F.Days.TryGetValue(key, out var d)) return d;
        F.Days[key] = d = new DayInfo();
        foreach (var old in F.Days.Keys.Where(k => ProgressTime.DaysBetween(k, key) > 13).ToList()) F.Days.Remove(old);
        return d;
    }

    static bool Trained(DayInfo d) => d.Drills is { Count: > 0 };

    /// <summary>Training days in the week of <paramref name="dayKey"/> (from the last 14 days kept).</summary>
    public int TrainingDaysInWeek(string dayKey)
    {
        if (F.Days == null || ProgressTime.ParseDay(dayKey) is not { } day) return 0;
        string wk = ProgressTime.WeekKeyOfDay(day);
        return F.Days.Count(e => Trained(e.Value) && ProgressTime.ParseDay(e.Key) is { } d && ProgressTime.WeekKeyOfDay(d) == wk);
    }

    /// <summary>Rested pool: +300 per day without a run since the last training day (cap 1,500). Forward gaps only.</summary>
    void AccrueRested(string dayKey)
    {
        if (F.LastActiveDay != null)
        {
            int gap = ProgressTime.DaysBetween(F.LastActiveDay, dayKey) - 1;
            if (gap > 0) F.Rested = Math.Min(XpRules.RestedCap, F.Rested + XpRules.RestedPerDay * gap);
        }
        if (F.LastActiveDay == null || string.CompareOrdinal(dayKey, F.LastActiveDay) > 0) F.LastActiveDay = dayKey;
    }

    /// <summary>Weekly streak: the week of <paramref name="dayKey"/> counts once it has 2 training days.</summary>
    void StreakDay(string dayKey, List<XpPart>? parts)
    {
        var s = F.Streak ??= new StreakInfo();
        if (ProgressTime.ParseDay(dayKey) is not { } day) return;
        string wk = ProgressTime.WeekKeyOfDay(day);
        if (s.LastWeek == wk || TrainingDaysInWeek(dayKey) < 2) return;
        int gap = s.LastWeek == null ? 0 : ProgressTime.WeeksBetween(s.LastWeek, wk) - 1;
        if (gap < 0) return; // the clock went back: nothing changes
        s.RestUsed = false;
        if (gap > 0)
        {
            if (s.Tokens >= gap) { s.Tokens -= gap; s.RestUsed = true; }
            else s.Weeks = 0; // restarts quietly; Best is kept
        }
        s.Weeks++;
        s.Counted++;
        s.Best = Math.Max(s.Best, s.Weeks);
        s.LastWeek = wk;
        if (s.Counted % 4 == 0) s.Tokens = Math.Min(2, s.Tokens + 1);
        foreach (var (w, xp) in XpRules.StreakMilestones)
            if (s.Weeks >= w && s.Milestone < w) { s.Milestone = w; Grant(parts, L.N("Streak"), xp); }
    }

    static string TierLabel(int tier) => Difficulty.Get(tier).Name; // "Regular" (English data; the UI translates and upper-cases chips)

    static string DrillName(string mode) => DrillGuns.Proto(mode)?.DisplayName ?? mode;

    // ---------------- runs ----------------

    /// <summary>
    /// Credits one finished run (live: <see cref="Progress.OnRunFinished"/>; retro / catch-up: <see cref="Retro"/>). XP per
    /// §3, PB and badge stars per §4.1, "with it" counters, challenge ticks (live). <paramref name="replay"/>: no
    /// challenges, no texts.
    /// </summary>
    public RunXp ScoreRun(RunFacts run, DateTime when, bool replay)
    {
        var rec = run.Record;
        int xp0 = F.Xp, stars0 = F.Stars;
        var parts = replay ? null : new List<XpPart>();
        var reasons = replay ? null : new List<string>();
        var ticks = new List<ChallengeTick>();
        int unlocks0 = PendingUnlocks.Count;
        string mode = rec.Mode ?? "";
        string dayKey = DayKeyFor(when);
        var d = DayOf(dayKey);
        bool live = !replay && ChallengesOn;
        if (live) EnsureChallenges(when);

        if (run.Finder)
        {
            d.Finders ??= new();
            if (!d.Finders.Contains(mode)) { d.Finders.Add(mode); Grant(parts, L.N("Finder"), XpRules.FinderXp); }
            if (mode == XpRules.SensFinderKey) F.SensFinder = true;
            if (run.Stored) Index.Add(rec);
        }
        else
        {
            float active = Math.Clamp(run.ActiveSecs, 0f, XpRules.MaxActiveSecs);
            float e = XpRules.Engagement(rec, active);
            if (e > 0)
            {
                bool first = !Trained(d);
                if (first) AccrueRested(dayKey);
                d.Drills ??= new();
                int nth = (d.Drills.TryGetValue(mode, out var c) ? c : 0) + 1;
                d.Drills[mode] = nth;
                d.Secs += active;
                if (first) StreakDay(dayKey, parts);
                float t = XpRules.TierFactor(rec.Tier);
                float v = run.Adaptive || Exempt(mode) ? 1f : XpRules.Variety(nth);
                int raw = XpRules.RunXp(active, e, t, v);
                int kept = XpRules.Taper(d.RunXp, raw);
                d.RunXp += raw;
                int basePart = (int)MathF.Round(kept / t);
                Grant(parts, L.N("Run"), basePart);
                if (kept - basePart > 0) Grant(parts, TierLabel(rec.Tier), kept - basePart, "TIER");
                if (first) Grant(parts, L.N("First today"), XpRules.FirstOfDay);
                int rest = Math.Min(F.Rested, kept);
                if (rest > 0) { F.Rested -= rest; Grant(parts, L.N("Rested"), rest); }
                F.Counters.Runs++;
                F.Counters.ActiveSecs += (long)MathF.Round(active);
            }
            bool pb = false, realPb = false, badgeUp = false;
            if (run.Stored && !run.Adaptive)
            {
                var (n0, best0) = Index.At(rec);
                int bb0 = Index.BestBadge(mode);
                bool rated = DrillGuns.IsRated(rec);
                pb = n0 >= 1 && rec.Score > best0 && rec.Score > 0;
                if (pb)
                {
                    F.Counters.Pbs++;
                    if (d.PbXp < XpRules.PbXpPerDay) { d.PbXp++; Grant(parts, L.N("PB"), XpRules.PbXp); }
                    float gain = best0 > 0 ? (rec.Score - best0) / (float)best0 : 0f;
                    realPb = rated && n0 >= XpRules.RealPbRuns && best0 > 0 && gain >= XpRules.RealPbGain - 1e-6f && rec.Tier >= bb0 - 1;
                    if (realPb)
                    {
                        d.PbDrills ??= new();
                        if (!d.PbDrills.Contains(mode) && d.PbStars < XpRules.PbStarsPerDay)
                        {
                            d.PbDrills.Add(mode);
                            d.PbStars++;
                            F.Counters.PbStars++;
                            Star(1, "PB", reasons, () => L.F($"REAL PB +{gain * 100f:0.0}% · {DrillName(mode)}"));
                        }
                    }
                }
                badgeUp = rated && rec.Badge >= 0 && bb0 >= 0 && rec.Badge > bb0;
                if (badgeUp)
                {
                    F.Counters.BadgeUps++;
                    Grant(parts, L.N("Badge up"), XpRules.BadgeUpXp);
                    Star(1, "BADGE UP", reasons, () => L.F($"BADGE RANK-UP · {DrillName(mode)}"));
                }
                Index.Add(rec);
            }
            if (e > 0 && run.SkinLine is { } line && line != ItemIds.Factory && ItemIds.IsToken(line))
            {
                F.With ??= new();
                if (!F.With.TryGetValue(line, out var w)) F.With[line] = w = new WithLine();
                w.Runs++;
                if (realPb) w.Pb++;
                if (badgeUp) w.BadgeUp++;
            }
            if (live && e > 0) TickRun(run, dayKey, d, active, pb, badgeUp, parts, reasons, ticks);
        }
        if (run.Stored && !replay && (F.CreditedThrough == null || rec.When > F.CreditedThrough)) F.CreditedThrough = rec.When;
        if (!replay) CheckUnlocks("run", when);
        int lv0 = XpCurve.LevelOf(xp0);
        return new RunXp(F.Xp - xp0, (IReadOnlyList<XpPart>?)parts ?? Array.Empty<XpPart>(), F.Stars - stars0,
                         (IReadOnlyList<string>?)reasons ?? Array.Empty<string>(), lv0, XpCurve.LevelOf(F.Xp), Bar(xp0), Bar(F.Xp), ticks,
                         PendingUnlocks.Skip(unlocks0).Select(u => u.ItemId).ToList());
    }

    /// <summary>Live: feed stats runs the index hasn't seen (except <paramref name="current"/>), rebuilding when the list
    /// shrank (stats replaced). Call before scoring a live run.</summary>
    public void SyncIndex(IReadOnlyList<RunRecord> runs, RunRecord? current)
    {
        if (runs.Count < statsSeen) { Index.Clear(); statsSeen = 0; }
        for (int i = statsSeen; i < runs.Count; i++)
            if (runs[i] != null && !ReferenceEquals(runs[i], current)) Index.Add(runs[i]);
        statsSeen = runs.Count;
    }

    /// <summary>After a live run the current record (if stored) is already counted in <see cref="statsSeen"/>.</summary>
    public void MarkIndexed(int statsCount) => statsSeen = statsCount;

    // ---------------- Lock-In ----------------

    /// <summary>A Lock-In finished: the completion bonus (Completed only), the counter and the Lock-In challenges. Step time
    /// XP came with each step's run (live) or comes from <see cref="Retro"/>.</summary>
    public int LockIn(Warmup.WarmupRecord rec, DateTime when, bool replay, List<XpPart>? parts, List<ChallengeTick>? ticks)
    {
        int xp0 = F.Xp;
        if (rec.Completed)
        {
            F.Counters.LockIns++;
            Grant(parts, L.N("Lock-In"), XpRules.LockInBonus(rec.Preset));
        }
        if (F.LockInsThrough == null || rec.When > F.LockInsThrough) F.LockInsThrough = rec.When;
        if (!replay && ChallengesOn)
        {
            EnsureChallenges(when);
            if (rec.Completed) TickLockIn(parts, ticks);
        }
        if (!replay) CheckUnlocks("lockin", when);
        return F.Xp - xp0;
    }

    // ---------------- unlocks ----------------

    /// <summary>The numbers requirements are checked against: progress.json plus the stats history.</summary>
    public ProgressFacts Facts() => new()
    {
        Level = XpCurve.LevelOf(F.Xp), Stars = F.Stars,
        OverallUps = F.Coach?.OverallUps ?? 0, SkillUps = F.Coach?.SkillUps ?? 0,
        CoachPlaced = F.Coach is { } c && (c.Ever || c.PlacedOn != null),
        BadgeUps = F.Counters.BadgeUps, PbStars = F.Counters.PbStars, LockIns = F.Counters.LockIns,
        StreakWeeks = F.Streak?.Weeks ?? 0, WeeklyImprove = F.Counters.WeeklyImprove,
        BadgeDrills = Index.BadgeDrills, BestBadge = Index.BestBadgeAny, BenchDrillsAtLeast = Index.BenchAtLeast(),
        SensFinderDone = F.SensFinder || Index.SensFinder,
        With = F.With ?? new(),
    };

    /// <summary>Whether an item counts as owned for the structural rules (recorded, or free).</summary>
    bool Owned(string id) => F.Unlocked.ContainsKey(id) || Progress.IsFreeItem(id);

    /// <summary>Records every item whose requirement (and structural prerequisite) is met now. Permanent: nothing is ever
    /// removed. Free items are not recorded (they are always unlocked).</summary>
    public List<string> CheckUnlocks(string source, DateTime when)
    {
        var got = new List<string>();
        var facts = Facts();
        foreach (var id in Progress.AllItems)
        {
            if (F.Unlocked.ContainsKey(id) || Progress.IsFreeItem(id)) continue;
            if (!ItemIds.TryParse(id, out var line, out int level, out var variant) || Progress.LineOf(line) is not { } l) continue;
            string need = variant != null ? ItemIds.Level(line, l.VariantNeedsLevel) : level > 1 ? ItemIds.Level(line, level - 1) : "";
            if (need.Length > 0 && !Owned(need)) continue;
            if (!Progress.RequirementOf(id).Met(facts)) continue;
            F.Unlocked[id] = when;
            if (!F.Unseen.Contains(id)) F.Unseen.Add(id);
            got.Add(id);
            PendingUnlocks.Add(new UnlockInfo(id, source, when));
        }
        return got;
    }
}
