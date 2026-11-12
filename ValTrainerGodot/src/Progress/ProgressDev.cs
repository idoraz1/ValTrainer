using System.Globalization;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>
/// Headless progression tools (all need --dev; none of them writes anything):
/// <list type="bullet">
/// <item><c>--progress-test</c>: the self-test (<see cref="ProgressCheck"/>), exit code = failures.</item>
/// <item><c>--progress-sim casual|regular|grinder [--sim-days N]</c>: simulates a player through the live rules and prints
/// level, stars and unlocks per day, with the pacing asserts (a first-session unlock; regular: an Apex skin within 14 days;
/// never an Apex finisher or the last Signature without a coach rank-up). Exit code = failed asserts.</item>
/// <item><c>--progress-report</c>: the data dir's credit (what <see cref="Progress.Init"/> just replayed in memory), broken
/// down by source, then the current coach profile as the placement. Use it on a COPY of a data folder:
/// <c>--dev --data-dir C:\tmp\vt-copy --progress-report</c> (never pass --write-data for someone's real data).</item>
/// </list>
/// </summary>
public static class ProgressDev
{
    public static bool Requested => CmdLine.Dev && (CmdLine.Has("--progress-test") || CmdLine.Has("--progress-sim") || CmdLine.Has("--progress-report"));

    public static int Run()
    {
        try
        {
            if (CmdLine.Has("--progress-test")) return ProgressCheck.Run();
            if (CmdLine.Has("--progress-sim"))
            {
                int days = int.TryParse(CmdLine.After("--sim-days"), NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 365) : 28;
                string kind = CmdLine.After("--progress-sim") is { } k && !k.StartsWith("--", StringComparison.Ordinal) ? k.ToLowerInvariant() : "all";
                return kind == "all" ? Sim("casual", days) + Sim("regular", days) + Sim("grinder", days) : Sim(kind, days);
            }
            return Report();
        }
        catch (Exception e)
        {
            Say("FAIL unexpected exception: " + e);
            return 1;
        }
    }

    static void Say(string s) => Godot.GD.Print("[progress] " + s);

    static string Inv(FormattableString s) => FormattableString.Invariant(s);

    // ---------------- report ----------------

    static int Report()
    {
        var e = Progress.Engine;
        var f = e.F;
        Say(Inv($"data dir {Paths.DataDir}; progress.json {Progress.LoadResult}; {Main.I.Stats.Runs.Count} stats runs, {Warmup.WarmupStore.I.Warmups.Count} Lock-Ins"));
        PrintState("after the history replay", e);
        var profile = Analysis.Coach.BuildProfile(new StatsStore { Runs = Main.I.Stats.Runs.ToList() }, Main.I.Sens, Main.I.Settings.Dpi);
        Say(Inv($"coach profile now: overall {profile.OverallTier:0.00} (confidence {profile.OverallConfidence:0.00}), {profile.Skills.Count} skills, {profile.Problems.Count} problems"));
        e.OnProfile(profile, Progress.Now, null, null);
        PrintState("after the coach placement (the first real profile)", e);
        var r = f.Retro;
        if (r != null) Say(Inv($"reveal: {r.Runs} runs · {r.ActiveSecs / 3600}h {r.ActiveSecs % 3600 / 60:00}m · {r.Pbs} PBs · {r.BadgeUps} badge rank-ups · {r.LockIns} Lock-Ins → level {XpCurve.LevelOf(r.Xp)}, {r.Stars} stars, {r.Unlocks?.Count ?? 0} unlocks"));
        return 0;
    }

    static void PrintState(string when, ProgressEngine e)
    {
        var f = e.F;
        int lv = XpCurve.LevelOf(f.Xp);
        Say(Inv($"--- {when}: level {lv} ({f.Xp:#,0} XP, {XpCurve.TotalFor(lv + 1) - f.Xp:#,0} to level {lv + 1}), {f.Stars} stars, rested pool {f.Rested}"));
        Say("    XP by source: " + string.Join(" · ", e.XpBy.OrderByDescending(x => x.Value).Select(x => Inv($"{x.Key} {x.Value:#,0}"))));
        Say("    stars by source: " + (e.StarsBy.Count == 0 ? "none" : string.Join(" · ", e.StarsBy.Select(x => Inv($"{x.Key} {x.Value}")))));
        var c = f.Counters;
        Say(Inv($"    counters: runs {c.Runs}, active {c.ActiveSecs / 60} min, PBs {c.Pbs}, real PB stars {c.PbStars}, badge-ups {c.BadgeUps}, Lock-Ins {c.LockIns}"));
        Say(Inv($"    streak {f.Streak?.Weeks ?? 0} weeks (best {f.Streak?.Best ?? 0}, tokens {f.Streak?.Tokens ?? 0}); coach placed {(f.Coach?.PlacedOn ?? "no")} ") +
            (f.Coach?.Placed is { Count: > 0 } pl ? string.Join(", ", pl.Select(p => Inv($"{p.Key} {UI.CoachRank.Names[Math.Clamp(p.Value, 0, 8)]}"))) : ""));
        var owned = f.Unlocked.Keys.ToList();
        var lines = owned.Select(id => ItemIds.TryParse(id, out var l, out _, out _) ? l : id).Distinct().ToList();
        var free = Progress.AllItems.Where(Progress.IsFreeItem).ToList();
        Say(Inv($"    unlocked: {owned.Count} items in {lines.Count} lines (+ {free.Count} free Core lines): {string.Join(", ", owned)}"));
        var next = Progress.AllItems.Where(id => !f.Unlocked.ContainsKey(id) && !Progress.IsFreeItem(id))
            .Select(id => (id, req: Progress.RequirementOf(id))).Take(6).Select(x => $"{x.id} [{x.req.Source}]");
        Say("    next in table: " + string.Join(", ", next));
    }

    // ---------------- pacing simulator ----------------

    static readonly string[] SimDrills = { "gridshot", "flick", "tracking", "strafebots", "spider", "microshot", "peek", "counterstrafe", "popup", "longtaps" };

    /// <summary>Feeds a made-up run through the live path (stats grow like in the game).</summary>
    internal static RunXp LiveRun(ProgressEngine e, StatsStore stats, RunRecord rec, float active, DateTime when, bool lockInStep = false)
    {
        stats.Runs.Add(rec);
        e.SyncIndex(stats.Runs, rec);
        var r = e.ScoreRun(new RunFacts(rec, true, active, null, lockInStep, false, XpRules.IsFinder(rec.Mode)), when, replay: false);
        e.MarkIndexed(stats.Runs.Count);
        return r;
    }

    static int Sim(string kind, int days)
    {
        (int runs, DayOfWeek[] on) plan = kind switch
        {
            "casual" => (8, new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday }),
            "grinder" => (40, new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }),
            _ => (15, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday }),
        };
        var app = Main.I;
        var keepStats = app.Stats;
        var stats = new StatsStore();
        app.Stats = stats;
        var e = new ProgressEngine(new ProgressFile { Salt = "sim-" + kind });
        var old = Progress.SwapEngineForTests(e);
        int fails = 0;
        try
        {
            var rng = new Random(ProgressTime.Seed(kind));
            var start = new DateTime(2026, 11, 2, 18, 0, 0); // a Monday evening
            e.Replay(stats.Runs, Array.Empty<Warmup.WarmupRecord>(), start, retro: true);
            int tier = Math.Clamp(app.Settings.Tier, 0, 4);
            var count = new Dictionary<string, int>();
            int apexDay = -1, firstUnlockSession = -1, session = 0;
            Say($"=== sim {kind}: {plan.runs} runs × {plan.on.Length} days/week, {days} days, tier {Difficulty.Get(tier).Name}");
            for (int day = 0; day < days; day++)
            {
                var date = start.AddDays(day);
                if (!plan.on.Contains(date.DayOfWeek)) continue;
                e.EnsureChallenges(date);
                var focus = e.F.Daily!.Items.FirstOrDefault(i => i.Kind == "focus_runs")?.Drill ?? "flick";
                for (int i = 0; i < plan.runs; i++)
                {
                    string mode = i < 3 ? focus : SimDrills[(i + day) % SimDrills.Length];
                    int n = count[mode] = (count.TryGetValue(mode, out var c) ? c : 0) + 1;
                    float skill = 1f + 0.004f * n;           // slow real improvement
                    float noise = 0.92f + 0.16f * (float)rng.NextDouble();
                    int score = (int)(10000 * skill * noise);
                    var when = date.AddMinutes(i * 1.5);
                    var rec = new RunRecord
                    {
                        Mode = mode, When = when, Score = score, Accuracy = 0.55f, Hits = 40, Shots = 70, Kills = 30, Tier = tier,
                        Badge = skill * noise > 1.12f ? Math.Min(4, tier + 1) : tier, Sens = 0.4f,
                    };
                    LiveRun(e, stats, rec, 54f, when);
                }
                int unlocked = e.F.Unlocked.Count;
                session++;
                if (firstUnlockSession < 0 && unlocked > 0) firstUnlockSession = session;
                if (apexDay < 0 && e.F.Unlocked.Keys.Any(id => Progress.LineOfItem(id) is { Edition: Edition.Apex })) apexDay = day + 1;
                Say(Inv($"day {day + 1,3} {date:ddd}: level {XpCurve.LevelOf(e.F.Xp),3}  {e.F.Xp,7:#,0} XP  {e.F.Stars,3} stars  {unlocked,3} unlocks  streak {e.F.Streak?.Weeks ?? 0}w  dailies {e.F.Counters.Dailies} weeklies {e.F.Counters.Weeklies}"));
            }
            // the sim feeds no coach profiles, so no rank-up: the rank-up gates must hold
            int lastSig = Progress.Lines.Count(l => l.Edition == Edition.Signature) - 1;
            bool top = e.F.Unlocked.Keys.Any(id => ItemIds.TryParse(id, out var ln, out int lv, out var vv) && Progress.LineOf(ln) is { } pl
                && (pl.Edition == Edition.Apex && (lv == 5 || vv != null) || pl.Edition == Edition.Signature && pl.Order == lastSig));
            void Assert(string name, bool ok) { if (!ok) fails++; Say($"{(ok ? "PASS" : "FAIL")} {kind}: {name}"); }
            Assert("something unlocks in the first session", firstUnlockSession == 1);
            if (kind == "regular" && days >= 14) Assert(Inv($"an Apex skin within 14 days (day {apexDay})"), apexDay is > 0 and <= 14);
            Assert("no Apex finisher or variant, nor the last Signature, without a coach rank-up", !top);
        }
        finally
        {
            Progress.SwapEngineForTests(old);
            app.Stats = keepStats;
        }
        return fails;
    }
}
