using ValTrainer.Core;
using ValTrainer.Warmup;

namespace ValTrainer.Progression;

/// <summary>
/// Retroactive credit (progression-design §7): the whole stats.json + warmups.json history replayed through the same
/// per-run rules the live game uses (<see cref="ScoreRun"/>), in time order: every stats run (time from run.secs or
/// the drill's length × 0.9), the Lock-In steps that never reached stats (adaptive / sens-shifted: time XP), and each
/// Lock-In's completion bonus. No challenges and no back-dated coach rank-ups: the first real coach profile afterwards
/// becomes the placement. The weekly streak is derived from the run dates. The same path credits runs and Lock-Ins newer
/// than <see cref="ProgressFile.CreditedThrough"/> / <see cref="ProgressFile.LockInsThrough"/> on every launch (played
/// on another build).
/// </summary>
internal sealed partial class ProgressEngine
{
    /// <summary>A run of the stats history as the run hook would have seen it (no telemetry: 0.9 × its length is active).</summary>
    public static RunFacts RetroFacts(RunRecord r) =>
        new(r, true, XpRules.RetroActive(r), null, r.Warmup, false, XpRules.IsFinder(r.Mode));

    /// <summary>The Lock-In steps that are not in stats.json (adaptive or sens-shifted, not skipped) as runs, timed from the
    /// routine's start.</summary>
    public static IEnumerable<(DateTime When, RunFacts Run)> UnstoredSteps(WarmupRecord w)
    {
        var t = w.When;
        foreach (var s in w.Steps)
        {
            if (s == null || s.Skipped) continue;
            t = t.AddSeconds(Math.Max(0, s.Seconds));
            if (!s.Adaptive && !s.Shifted) continue;
            var rec = new RunRecord
            {
                Mode = s.Mode ?? "", When = t, Score = s.Score, Accuracy = s.Accuracy, Hits = s.Hits, Shots = s.Shots, Kills = s.Kills,
                AvgKillMs = s.AvgKillMs, Sens = s.Sens, Tier = s.Tier >= 0 ? s.Tier : w.Tier, Warmup = true,
            };
            yield return (t, new RunFacts(rec, false, XpRules.RetroActiveShare * Math.Max(0, s.Seconds), null, true, s.Adaptive, false));
        }
    }

    /// <summary>Replays <paramref name="runs"/> (stats) and <paramref name="lockins"/> (warmups) newer than the credited
    /// marks, in time order. <paramref name="retro"/>: the first-launch credit (welcome bonus, reveal data, coach
    /// placement from the next profile). Returns the stats runs credited.</summary>
    public int Replay(IReadOnlyList<RunRecord> runs, IReadOnlyList<WarmupRecord> lockins, DateTime now, bool retro)
    {
        int xp0 = F.Xp;
        var c0 = F.Counters;
        long secs0 = c0.ActiveSecs;
        int pbs0 = c0.Pbs, ups0 = c0.BadgeUps, li0 = c0.LockIns;
        if (retro) Grant(null, L.N("Welcome"), XpRules.Welcome);
        var since = F.CreditedThrough;
        var lockSince = F.LockInsThrough;
        // Index: rebuilt; every stats run up to the credited mark is history already (catch-up keeps PB detection right).
        Index.Clear();
        var events = new List<(DateTime When, int Order, int Seq, Action Do)>();
        int seq = 0, credited = 0;
        foreach (var r in runs)
        {
            if (r == null) continue;
            if (since != null && r.When <= since) { Index.Add(r); continue; }
            credited++;
            var rr = r;
            events.Add((r.When, 0, seq++, () => ScoreRun(RetroFacts(rr), rr.When, replay: true)));
        }
        foreach (var w in lockins)
        {
            if (w == null || (lockSince != null && w.When <= lockSince)) continue;
            foreach (var (t, run) in UnstoredSteps(w)) events.Add((t, 1, seq++, () => ScoreRun(run, t, replay: true)));
            var ww = w;
            var end = w.When.AddSeconds(Math.Max(w.PlaySeconds, w.TotalSeconds));
            events.Add((end, 2, seq++, () => LockIn(ww, end, replay: true, null, null)));
        }
        foreach (var e in events.OrderBy(e => e.When).ThenBy(e => e.Seq)) e.Do();
        var newest = runs.Where(r => r != null).Select(r => (DateTime?)r.When).DefaultIfEmpty(null).Max();
        if (newest != null && (F.CreditedThrough == null || newest > F.CreditedThrough)) F.CreditedThrough = newest;
        var newestLock = lockins.Where(w => w != null).Select(w => (DateTime?)w.When).DefaultIfEmpty(null).Max();
        if (newestLock != null && (F.LockInsThrough == null || newestLock > F.LockInsThrough)) F.LockInsThrough = newestLock;
        var got = CheckUnlocks(retro ? "retro" : "run", now);
        if (retro)
        {
            (F.Coach ??= new CoachTrackInfo()).RetroPending = true;
            F.Retro = new RetroInfo
            {
                Done = true, Shown = false, When = now, Runs = credited, Xp = F.Xp, Stars = F.Stars,
                ActiveSecs = F.Counters.ActiveSecs - secs0, Pbs = F.Counters.Pbs - pbs0, BadgeUps = F.Counters.BadgeUps - ups0,
                LockIns = F.Counters.LockIns - li0, Unlocks = got,
            };
        }
        if (credited > 0 || F.Xp != xp0) Log.Info($"[progress] {(retro ? "retro credit" : "catch-up")}: {credited} runs, +{F.Xp - xp0} XP, level {XpCurve.LevelOf(F.Xp)}, {F.Stars} stars, {got.Count} unlocks");
        return credited;
    }
}
