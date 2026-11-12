using System.Globalization;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Warmup;

namespace ValTrainer.Progression;

/// <summary>
/// <c>--dev --headless --progress-test</c> (progression-design §12): curve, time keys, run XP, stars, coach confirmation,
/// requirements, never-revoke, persistence, challenges, streak and retro-equals-live. Prints "[progress-test] PASS/FAIL …"
/// and a RESULT line; exit code = failures. Works on its own engines and a swapped-in stats list, so the real data dir is
/// never read for the cases and nothing is ever written outside a temp folder.
/// </summary>
public static class ProgressCheck
{
    static int pass, fail;

    public static int Run()
    {
        pass = fail = 0;
        var app = Main.I;
        var keepStats = app.Stats;
        bool devAll = Progress.DevUnlockAll;
        Progress.DevUnlockAll = false;
        string root = Path.Combine(Path.GetTempPath(), "vt-progress-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Curve();
            Time();
            RunXpRules();
            Stars();
            CoachConfirm();
            Requirements();
            NeverRevoke();
            Challenges();
            ShortRuns();
            Streak();
            RetroEqualsLive();
            Persistence(root);
        }
        catch (Exception e) { Fail("unexpected exception", e.ToString()); }
        finally
        {
            app.Stats = keepStats;
            Progress.DevUnlockAll = devAll;
            Paths.SetDataDirForTests(null);
            try { Directory.Delete(root, true); } catch { }
        }
        Say($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed)");
        return fail;
    }

    // ---------------- helpers ----------------

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { pass++; Say($"PASS {name}"); }
        else Fail(name, detail);
    }

    static void Fail(string name, string detail) { fail++; Say($"FAIL {name}: {detail}"); }
    static void Say(string s) => Godot.GD.Print("[progress-test] " + s);
    static string Inv(FormattableString s) => FormattableString.Invariant(s);

    /// <summary>A fresh engine (no challenges unless asked) over a fresh, empty stats list swapped into Main.</summary>
    static (ProgressEngine E, StatsStore S) Fresh(bool challenges = false)
    {
        var s = new StatsStore();
        Main.I.Stats = s;
        return (new ProgressEngine(new ProgressFile { Salt = "test" }) { ChallengesOn = challenges }, s);
    }

    static RunRecord Rec(string mode, DateTime when, int score = 10000, int tier = 1, int badge = -1, int shots = 70, float acc = 0.5f, string? weapon = null) =>
        new() { Mode = mode, When = when, Score = score, Tier = tier, Badge = badge, Shots = shots, Hits = (int)(shots * Math.Max(0, acc)), Kills = 20, Accuracy = acc, Sens = 0.4f, Weapon = weapon };

    static RunXp Play(ProgressEngine e, StatsStore s, RunRecord r, float active = 54f, bool stored = true, bool adaptive = false, bool lockIn = false, bool finder = false)
    {
        if (stored) s.Runs.Add(r);
        e.SyncIndex(s.Runs, r);
        var x = e.ScoreRun(new RunFacts(r, stored, active, null, lockIn, adaptive, finder), r.When, replay: false);
        e.MarkIndexed(s.Runs.Count);
        return x;
    }

    static int Part(RunXp x, string label) => x.Parts.Where(p => p.Label == label).Sum(p => p.Xp);
    static int RunPart(RunXp x) => x.Parts.Where(p => p.Label is not ("First today" or "Rested" or "PB" or "Badge up" or "Streak" or "Daily" or "Weekly" or "All dailies")).Sum(p => p.Xp);

    static readonly DateTime T0 = new(2026, 11, 2, 18, 0, 0); // Monday

    // ---------------- curve and time ----------------

    static void Curve()
    {
        bool mono = Enumerable.Range(1, 80).All(n => XpCurve.TotalFor(n + 1) > XpCurve.TotalFor(n));
        bool inverse = Enumerable.Range(1, 80).All(n => XpCurve.LevelOf(XpCurve.TotalFor(n)) == n && (n == 1 || XpCurve.LevelOf(XpCurve.TotalFor(n) - 1) == n - 1));
        Check("curve: monotonic, LevelOf inverts TotalFor, 2,500 per level from 21, levels 2/3/12/20/30 at 500/1,100/11,000/26,600/51,500",
            mono && inverse && XpCurve.CostOf(21) == 2500 && XpCurve.CostOf(60) == 2500 && XpCurve.TotalFor(2) == 500 && XpCurve.TotalFor(3) == 1100
            && XpCurve.TotalFor(12) == 11000 && XpCurve.TotalFor(20) == 26600 && XpCurve.TotalFor(30) == 51500);
        Check("XP bar: 0 at a level start, 0.5 halfway", ProgressEngine.Bar(500) == 0f && MathF.Abs(ProgressEngine.Bar(800) - 0.5f) < 1e-4f);
    }

    static void Time()
    {
        Check("day starts 04:00: 03:59 is still yesterday, 04:00 today", ProgressTime.DayKey(new DateTime(2026, 11, 7, 3, 59, 0)) == "2026-11-06"
            && ProgressTime.DayKey(new DateTime(2026, 11, 7, 4, 0, 0)) == "2026-11-07" && ProgressTime.DayKey(new DateTime(2026, 11, 7, 23, 59, 0)) == "2026-11-07");
        Check("ISO weeks over New Year (2026 has 53 weeks; Monday 04:00 boundary)", ProgressTime.WeekKey(new DateTime(2027, 1, 1, 12, 0, 0)) == "2026-W53"
            && ProgressTime.WeekKey(new DateTime(2027, 1, 4, 3, 0, 0)) == "2026-W53" && ProgressTime.WeekKey(new DateTime(2027, 1, 4, 5, 0, 0)) == "2027-W01"
            && ProgressTime.WeeksBetween("2026-W52", "2027-W01") == 2 && ProgressTime.WeeksBetween("2026-W53", "2027-W01") == 1,
            ProgressTime.WeekKey(new DateTime(2027, 1, 1, 12, 0, 0)));
        // DST (EU 2026-03-29, US 2026-03-08 and 2026-11-01): day keys are wall-clock dates, a 23 h / 25 h day is still one day
        Check("DST days are one day each; days between keys count calendar days",
            ProgressTime.DayKey(new DateTime(2026, 3, 29, 3, 30, 0)) == "2026-03-28" && ProgressTime.DayKey(new DateTime(2026, 3, 29, 4, 30, 0)) == "2026-03-29"
            && ProgressTime.DaysBetween("2026-03-28", "2026-03-30") == 2 && ProgressTime.DaysBetween("2026-10-31", "2026-11-02") == 2
            && ProgressTime.DaysBetween("2026-12-31", "2027-01-01") == 1);
        Check("resets: next day 04:00, next Monday 04:00", ProgressTime.NextDayReset(new DateTime(2026, 11, 7, 2, 0, 0)) == new DateTime(2026, 11, 7, 4, 0, 0)
            && ProgressTime.NextDayReset(new DateTime(2026, 11, 7, 5, 0, 0)) == new DateTime(2026, 11, 8, 4, 0, 0)
            && ProgressTime.NextWeekReset(new DateTime(2026, 11, 7, 12, 0, 0)) == new DateTime(2026, 11, 9, 4, 0, 0)
            && ProgressTime.NextWeekReset(new DateTime(2026, 11, 9, 3, 0, 0)) == new DateTime(2026, 11, 9, 4, 0, 0));
        Check("stable seeds (challenges never reroll on restart)", ProgressTime.Seed("2026-11-07|abc") == ProgressTime.Seed("2026-11-07|abc")
            && ProgressTime.Seed("2026-11-07|abc") != ProgressTime.Seed("2026-11-08|abc") && ProgressTime.Seed("a") == (int)(0xE40C292Cu & 0x7fffffff));
    }

    // ---------------- run XP ----------------

    static void RunXpRules()
    {
        var (e, s) = Fresh();
        var idleRec = Rec("gridshot", T0, score: 0, shots: 0);
        idleRec.Kills = 0;
        var idle = Play(e, s, idleRec, active: 10, stored: false);
        Check("idle run (no shots, kills or score, < 15 s input) = 0 XP and doesn't use the first-run bonus", idle.Total == 0 && e.F.Days!.Values.All(d => d.Drills == null));
        var first = Play(e, s, Rec("gridshot", T0.AddMinutes(1)));
        Check("typical 60-s run at Regular (54 s active) ≈ 99 XP + first run of the day 150", RunPart(first) == 99 && Part(first, "First today") == 150 && first.Total == 249,
            string.Join(",", first.Parts.Select(p => $"{p.Label}={p.Xp}")));
        var xs = new List<int>();
        for (int i = 2; i <= 8; i++) xs.Add(RunPart(Play(e, s, Rec("gridshot", T0.AddMinutes(i)))));
        Check("variety: runs 1–3 ×1, 4–6 ×0.75, 7+ ×0.5 of the same drill", xs.SequenceEqual(new[] { 99, 99, 74, 74, 74, 50, 50 }), string.Join(",", xs));
        Check("tier factor 1.0 … 1.4 (old builds 1.0)", XpRules.TierFactor(0) == 1f && MathF.Abs(XpRules.TierFactor(4) - 1.4f) < 1e-5f && XpRules.TierFactor(-1) == 1f);
        e.F.Coach = new CoachTrackInfo { FocusDrills = new() { "gridshot" } };
        Check("the coach's focus drill is exempt from the decay", RunPart(Play(e, s, Rec("gridshot", T0.AddMinutes(9)))) == 99);
        e.F.Coach = null;
        var spam = Play(e, s, Rec("tracking", T0.AddMinutes(10), shots: 120, acc: 0.05f));
        Check("spray-and-pray (60+ shots, < 10 % accuracy) = half", RunPart(spam) == 50, RunPart(spam).ToString(CultureInfo.InvariantCulture));
        var dm = Play(e, s, Rec("deathmatch", T0.AddMinutes(11)), active: 600);
        Check("5-minute cap per run (600 s active counts 300 s)", RunPart(dm) == 550, RunPart(dm).ToString(CultureInfo.InvariantCulture));
        Check("taper: run XP beyond 2,500 a day counts half", XpRules.Taper(2400, 200) == 150 && XpRules.Taper(3000, 100) == 50 && XpRules.Taper(0, 300) == 300);
        // a 04:00 day: 23:00 and 02:00 are the same day, 05:00 is the next
        var (e2, s2) = Fresh();
        var a = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 3, 23, 0, 0)));
        var b = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 4, 2, 0, 0)));
        var c = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 4, 5, 0, 0)));
        Check("first run of the day once per 04:00 day", Part(a, "First today") == 150 && Part(b, "First today") == 0 && Part(c, "First today") == 150);
        // rested: 3 days away = 900, consumed as +100 % of run XP; 10 days away caps at 1,500
        var r1 = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 8, 18, 0, 0)));
        Check("rested: +300 per day away (3 days = 900), paid as +100 % of run XP", Part(r1, "Rested") == RunPart(r1) && e2.F.Rested == 900 - RunPart(r1),
            Inv($"{Part(r1, "Rested")} {e2.F.Rested}"));
        int pool0 = e2.F.Rested;
        var r2 = Play(e2, s2, Rec("tracking", new DateTime(2026, 11, 8, 18, 5, 0)));
        Check("rested drains run by run", e2.F.Rested == pool0 - Part(r2, "Rested") && Part(r2, "Rested") > 0);
        Play(e2, s2, Rec("tracking", new DateTime(2026, 11, 20, 18, 0, 0)));
        Check("rested pool caps at 1,500", e2.F.Rested <= XpRules.RestedCap && e2.F.Rested >= XpRules.RestedCap - 200, e2.F.Rested.ToString(CultureInfo.InvariantCulture));
        var back = Play(e2, s2, Rec("spider", new DateTime(2026, 11, 10, 18, 0, 0)));
        Check("clock going back: no new day, no first-run bonus, no rested", Part(back, "First today") == 0 && Part(back, "Rested") <= RunPart(back) && e2.F.LastActiveDay == "2026-11-20");
        var f1 = Play(e2, s2, Rec(XpRules.SensFinderKey, new DateTime(2026, 11, 20, 19, 0, 0)), finder: true);
        var f2 = Play(e2, s2, Rec(XpRules.SensFinderKey, new DateTime(2026, 11, 20, 19, 30, 0)), finder: true);
        var f3 = Play(e2, s2, Rec(XpRules.XhairFinderKey, new DateTime(2026, 11, 20, 19, 40, 0)), finder: true);
        Check("finders: a flat 50 XP once a day each; the Sens Finder counts for finder:sens", f1.Total == 50 && f2.Total == 0 && f3.Total == 50 && e2.Facts().SensFinderDone);
        var shifted = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 20, 20, 0, 0), score: 999999), stored: false, lockIn: true);
        var adaptive = Play(e2, s2, Rec("flick", new DateTime(2026, 11, 20, 20, 2, 0), score: 999999), stored: false, adaptive: true, lockIn: true);
        Check("sens-shifted / adaptive steps: time XP, no PB, badge or star", RunPart(shifted) > 0 && RunPart(adaptive) == 99 && Part(shifted, "PB") + Part(adaptive, "PB") == 0
            && shifted.StarsGained + adaptive.StarsGained == 0);
        Check("Lock-In bonus: quick/short 150, standard 300, pro 400", XpRules.LockInBonus("quick") == 150 && XpRules.LockInBonus("short") == 150
            && XpRules.LockInBonus("standard") == 300 && XpRules.LockInBonus("training") == 300 && XpRules.LockInBonus("pro") == 400);
        var lw = new WarmupRecord { When = T0, Preset = "standard", Completed = true, Tier = 1 };
        int before = e2.F.Xp;
        e2.LockIn(lw, T0, replay: true, null, null);
        var lw2 = new WarmupRecord { When = T0.AddHours(1), Preset = "pro", Completed = false, Tier = 1 };
        e2.LockIn(lw2, T0, replay: true, null, null);
        Check("only a completed Lock-In pays its bonus and counts", e2.F.Xp - before == 300 && e2.F.Counters.LockIns == 1);
        var steps = ProgressEngine.UnstoredSteps(new WarmupRecord
        {
            When = T0, Tier = 2, Steps = new()
            {
                new WarmupStepResult { Mode = "flick", Seconds = 60 },
                new WarmupStepResult { Mode = "microshot", Seconds = 60, Adaptive = true, Shots = 30 },
                new WarmupStepResult { Mode = "tracking", Seconds = 60, Adaptive = true, Skipped = true },
            },
        }).ToList();
        Check("retro Lock-In steps: only adaptive / shifted, not skipped, at 0.9 × their time", steps.Count == 1 && steps[0].Run.Adaptive && MathF.Abs(steps[0].Run.ActiveSecs - 54f) < 1e-3f
            && steps[0].Run.Record.Tier == 2 && steps[0].When == T0.AddSeconds(120));
    }

    // ---------------- stars ----------------

    static void Stars()
    {
        var (e, s) = Fresh();
        var t = T0;
        RunXp P(int score, int tier = 1, int badge = -1, string mode = "gridshot", string? weapon = null) { t = t.AddMinutes(2); return Play(e, s, Rec(mode, t, score, tier, badge, weapon: weapon)); }
        P(10000); P(10000); P(10000);
        var p1 = P(10100); // +1 %: a PB, not a real one
        Check("PB +50 XP (not the first run there), no star under +2 %", Part(p1, "PB") == 50 && p1.StarsGained == 0 && e.F.Counters.Pbs == 1);
        var p2 = P(10400); // +3 % with 4 earlier runs
        Check("real PB (≥ 3 earlier runs, ≥ +2 %) = 1 star", p2.StarsGained == 1 && e.F.Counters.PbStars == 1 && p2.StarReasons.Count == 1);
        var p3 = P(11000);
        Check("max 1 PB star per drill per day", p3.StarsGained == 0 && Part(p3, "PB") == 50);
        var (e2, s2) = Fresh();
        var t2 = T0;
        int stars = 0;
        foreach (var m in new[] { "flick", "tracking", "spider", "popup" })
        {
            for (int i = 0; i < 3; i++) { t2 = t2.AddMinutes(1); Play(e2, s2, Rec(m, t2, 10000)); }
            t2 = t2.AddMinutes(1);
            stars += Play(e2, s2, Rec(m, t2, 11000)).StarsGained;
        }
        Check("max 3 PB stars a day", stars == 3, stars.ToString(CultureInfo.InvariantCulture));
        int pbXpDay = e2.F.Days![ProgressTime.DayKey(t2)].PbXp;
        Check("PB XP up to 5 a day", pbXpDay == 4 && XpRules.PbXpPerDay == 5);
        // tier rule: a Veteran badge in the drill → Rookie PBs give XP but no star; Regular ones still do
        var (e3, s3) = Fresh();
        var t3 = T0;
        RunXp Q(int score, int tier, int badge = -1) { t3 = t3.AddMinutes(1); return Play(e3, s3, Rec("microshot", t3, score, tier, badge)); }
        Q(5000, 2, badge: 2);
        Q(1000, 0); Q(1000, 0); Q(1000, 0);
        var low = Q(2000, 0);
        Check("PB star needs a tier ≥ best badge − 1 (Rookie with a Veteran badge: XP, no star)", Part(low, "PB") == 50 && low.StarsGained == 0);
        Q(3000, 1); Q(3000, 1); Q(3000, 1);
        Check("… Regular with a Veteran badge still earns it", Q(4000, 1).StarsGained == 1);
        // badge rank-ups
        var (e4, s4) = Fresh();
        var t4 = T0;
        RunXp B(int badge) { t4 = t4.AddMinutes(1); return Play(e4, s4, Rec("counterstrafe", t4, 100 + badge, 1, badge)); }
        var b0 = B(-1); var b1 = B(1); var b2 = B(1); var b3 = B(2);
        Check("badge rank-up: none for a drill's first badge, +100 XP and 1 star for a higher one", b1.StarsGained == 0 && Part(b1, "Badge up") == 0
            && Part(b2, "Badge up") == 0 && Part(b3, "Badge up") == 100 && e4.F.Counters.BadgeUps == 1 && b3.StarReasons.Count == 1);
        var f = e4.Facts();
        Check("stats-derived facts: best badge, badge drills, benchmark ranks", f.BestBadge == 2 && f.BadgeDrills == 1 && f.BenchDrillsAtLeast[4] == 1 && f.BenchDrillsAtLeast[5] == 0);
        // unrated gun: score and PB, no star
        var probe = Rec("gridshot", T0, weapon: "Odin");
        if (!DrillGuns.IsRated(probe))
        {
            var (e5, s5) = Fresh();
            var t5 = T0;
            for (int i = 0; i < 3; i++) { t5 = t5.AddMinutes(1); Play(e5, s5, Rec("gridshot", t5, 10000, weapon: "Odin")); }
            var u = Play(e5, s5, Rec("gridshot", t5.AddMinutes(1), 12000, weapon: "Odin"));
            Check("unrated gun: PB XP, no star", Part(u, "PB") == 50 && u.StarsGained == 0);
        }
        else Say("SKIP unrated-gun star check: the Odin is rated in Gridshot");
    }

    // ---------------- coach ----------------

    static SkillProfile Profile(float overall, float precision, float crosshair, float movement, float conf = 0.9f) => new()
    {
        OverallTier = overall, OverallConfidence = conf, RunsAnalyzed = 50,
        Skills = new()
        {
            new SkillRating(Skill.Precision, precision, conf, 50, ""), new SkillRating(Skill.CrosshairPlacement, crosshair, conf, 50, ""),
            new SkillRating(Skill.Movement, movement, conf, 50, ""),
        },
    };

    static void CoachConfirm()
    {
        // The developer's real daily estimates (progression-design §1.5): overall, precision, crosshair, movement.
        var days = new (string D, float O, float P, float C, float M)[]
        {
            ("2026-10-02", 1.58f, 0.75f, 2.44f, 1.79f), ("2026-10-03", 1.79f, 2.11f, 2.84f, 1.89f), ("2026-10-04", 1.64f, 0.94f, 3.50f, 2.43f),
            ("2026-10-05", 1.68f, 0.80f, 3.38f, 2.52f), ("2026-10-07", 1.65f, 0.59f, 3.16f, 2.42f), ("2026-10-09", 1.63f, 0.53f, 3.52f, 2.40f),
            ("2026-10-11", 2.11f, 2.50f, 3.56f, 2.54f), ("2026-10-13", 2.09f, 2.50f, 3.56f, 2.48f), ("2026-10-15", 1.60f, 0.01f, 3.46f, 2.48f),
            ("2026-10-31", 1.58f, 0.01f, 2.72f, 2.64f),
        };
        var (e, _) = Fresh();
        int xpPlaced = -1;
        foreach (var d in days)
        {
            var when = ProgressTime.ParseDay(d.D)!.Value.AddHours(20);
            e.OnProfile(Profile(d.O, d.P, d.C, d.M), when, null, null);
            if (d.D == "2026-10-02") e.OnProfile(Profile(3.9f, 3.9f, 3.9f, 3.9f, 0.5f), when.AddHours(1), null, null); // low confidence: replaces the day, never counts
            if (d.D == "2026-10-02") e.OnProfile(Profile(d.O, d.P, d.C, d.M), when.AddHours(2), null, null);
            if (d.D == "2026-10-04") xpPlaced = e.F.Xp;
        }
        var c = e.F.Coach!;
        Check("placement after 3 valid days: Platinum overall, +300 XP, no stars, coach:placed", c.PlacedOn == "2026-10-04" && xpPlaced == 300 && c.Placed![ProgressEngine.OverallTrack] == 4
            && e.Facts().CoachPlaced, Inv($"{c.PlacedOn} {xpPlaced}"));
        Check("the 2-day Diamond spike gives no overall rank-up", c.OverallUps == 0 && c.High![ProgressEngine.OverallTrack] == 4);
        Check("Precision swinging 0.01 ↔ 2.50 gives no rank-up", c.High!["Precision"] == c.Placed!["Precision"]);
        Check("sustained Movement 2.4–2.6 confirms Diamond (+1 skill rank-up); Crosshair 3.2–3.6 confirms Immortal",
            c.High!["Movement"] == 5 && c.Placed!["Movement"] == 4 && c.High["CrosshairPlacement"] == 7 && c.SkillUps == 2 && e.F.Stars == 4
            && e.F.Xp == 300 + 2 * XpRules.SkillRankUpXp, Inv($"M {c.Placed!["Movement"]}->{c.High["Movement"]} C {c.Placed["CrosshairPlacement"]}->{c.High["CrosshairPlacement"]} ups {c.SkillUps} stars {e.F.Stars} xp {e.F.Xp}"));
        // a placement window can't produce a rank-up: 3 days place, high days right after need 5 days after the placement day
        var (e2, _) = Fresh();
        for (int i = 0; i < 3; i++) e2.OnProfile(Profile(1.0f, 1, 1, 1), T0.AddDays(i), null, null);
        for (int i = 3; i < 7; i++) e2.OnProfile(Profile(3.0f, 1, 1, 1), T0.AddDays(i), null, null);
        Check("4 high days after the placement: no rank-up yet (5 needed)", e2.F.Coach!.OverallUps == 0);
        e2.OnProfile(Profile(3.0f, 1, 1, 1), T0.AddDays(7), null, null);
        Check("… the 5th confirms it: +1,000 XP and 5 stars", e2.F.Coach!.OverallUps == 1 && e2.F.Stars == 5 && e2.F.Xp == 300 + 1000);
        var (e5, _) = Fresh();
        for (int i = 0; i < 3; i++) e5.OnProfile(Profile(1.0f, 1, 1, 1), T0.AddDays(i), null, null);
        for (int i = 3; i < 9; i++) e5.OnProfile(Profile(3.5f, 3.5f, 3.5f, 3.5f, 0.6f), T0.AddDays(i), null, null);
        for (int i = 9; i < 15; i++) e5.OnProfile(Profile(-1f, 1, 1, 1), T0.AddDays(i), null, null);
        Check("low-confidence and unrated (tier < 0) days are ignored", e5.F.Coach!.OverallUps == 0 && e5.F.Coach.SkillUps == 0 && e5.F.Stars == 0);
        // recalibration: re-place silently, keep everything earned
        int xp = e2.F.Xp, st = e2.F.Stars, ups = e2.F.Coach!.OverallUps;
        e2.F.Unlocked["carbon"] = T0;
        e2.F.Coach!.Calib = Bench.Calibration + 1; // samples from another scale
        for (int i = 10; i < 13; i++) e2.OnProfile(Profile(0.6f, 1, 1, 1), T0.AddDays(i), null, null);
        var c2 = e2.F.Coach!;
        Check("a Bench.Calibration change re-places with no XP or stars; rank-ups and unlocks stay", c2.Calib == Bench.Calibration && c2.Recalibrated && c2.Placed![ProgressEngine.OverallTrack] == 2
            && e2.F.Xp == xp && e2.F.Stars == st && c2.OverallUps == ups && e2.F.Unlocked.ContainsKey("carbon") && e2.Facts().CoachPlaced,
            Inv($"xp {e2.F.Xp}/{xp} stars {e2.F.Stars}/{st} placed {c2.Placed?.GetValueOrDefault(ProgressEngine.OverallTrack)}"));
        // retro: the first real profile becomes the placement (one sample)
        var (e3, _) = Fresh();
        e3.Replay(Array.Empty<RunRecord>(), Array.Empty<WarmupRecord>(), T0, retro: true);
        e3.OnProfile(Profile(1.6f, 1, 1, 1), T0, null, null);
        Check("retro: the current estimate becomes the placement (+300), no back-dated rank-ups", e3.F.Coach!.PlacedOn != null && e3.F.Xp == XpRules.Welcome + 300 && e3.F.Stars == 0
            && e3.F.Retro!.Xp == e3.F.Xp);
        var (e4, _) = Fresh();
        e4.F.Coach = new CoachTrackInfo { RetroPending = true };
        e4.OnProfile(Profile(1.6f, 1, 1, 1, 0.5f), T0, null, null);
        Check("retro placement needs HIGH confidence (else normal 3-day placement)", e4.F.Coach!.PlacedOn == null && !e4.F.Coach.RetroPending);
    }

    // ---------------- requirements ----------------

    static void Requirements()
    {
        var f = new ProgressFacts { Level = 30, Stars = 25, SkillUps = 3 };
        Check("grammar: +, |, parentheses; unknown terms never met", Requirement.Parse("level:30+stars:25+(rankup:overall:1|rankup:skill:3)").Met(f)
            && !Requirement.Parse("level:30+(stars:26|coach:placed)").Met(f) && Requirement.Parse("level:31|stars:25+level:30").Met(f)
            && !Requirement.Parse("nonsense:3").Met(f) && !Requirement.Parse("level:1+nonsense").Met(f));
        var g = new ProgressFacts
        {
            BestBadge = 2, BadgeDrills = 4, BenchDrillsAtLeast = new[] { 4, 4, 4, 4, 2, 2, 0, 0, 0 }, SensFinderDone = true, LockIns = 10, StreakWeeks = 4,
            WeeklyImprove = 3, PbStars = 7, BadgeUps = 5, CoachPlaced = true, With = new() { ["carbon"] = new WithLine { Runs = 12, Pb = 1, BadgeUp = 0 } },
        };
        Check("every term evaluates: badge, drills, bench, finder:sens, lockins, streak, weeklyimprove, pbstars, badgeups, coach:placed, with:*",
            Requirement.Parse("badge:veteran+drills:4+bench:gold:4+bench:diamond:2+finder:sens+lockins:10+streak:4+weeklyimprove:3+pbstars:7+badgeups:5+coach:placed+with:carbon:runs:10+with:carbon:pb").Met(g)
            && !Requirement.Parse("badge:elite").Met(g) && !Requirement.Parse("bench:ascendant:1").Met(g) && !Requirement.Parse("with:carbon:badgeup").Met(g)
            && !Requirement.Parse("with:other:runs:1").Met(g));
        // tier defaults for more lines than the table plans
        var extra = Enumerable.Range(0, 5).Select(i => new UnlockLine($"apex{i}", Edition.Apex, i,
                new[] { UnlockSlot.Base, UnlockSlot.Vfx, UnlockSlot.Kill, UnlockSlot.Vfx, UnlockSlot.Finisher }, new[] { "a", "b", "c" }))
            .Append(new UnlockLine("sig8", Edition.Signature, 7, new[] { UnlockSlot.Base }, Array.Empty<string>()))
            .Append(new UnlockLine("core9", Edition.Core, 8, new[] { UnlockSlot.Base }, Array.Empty<string>())).ToList();
        var t = UnlockTable.Build(extra);
        Check("tier defaults continue for extra lines (Apex #4–#5, Signature #8, Core #9)", t["apex0"] == "level:14" && t["apex3"] == "level:44+stars:30"
            && t["apex3/5"] == "level:56+stars:46+(rankup:overall:4|rankup:skill:9)" && t["apex0/4"] == "level:24+stars:10+badgeups:2" && t["apex0/a"] == "with:apex0:runs:25"
            && t["sig8"] == "level:31+stars:20+(rankup:overall:1|rankup:skill:2)" && t["core9"] == "free" && t.Count == 5 * 8 + 2
            && t.Values.All(v => Requirement.Parse(v).Root.Terms().All(x => x.Kind != ReqKind.Unknown)), string.Join(" ", t.Take(3)));
        // pacing: Signature lines come one after another; each Apex level is harder than the one below; L5 needs a rank-up
        var real = UnlockTable.Build(Progress.Lines);
        int LevelOf(string id) => Requirement.Parse(real[id]).Root.Terms().Where(x => x.Kind == ReqKind.Level).Select(x => x.N).DefaultIfEmpty(0).Max();
        var sigs = Progress.Lines.Where(l => l.Edition == Edition.Signature).ToList();
        var apex = Progress.Lines.Where(l => l.Edition == Edition.Apex).ToList();
        Check("table: Core free, 7 Signature lines at rising levels, 3 Apex lines whose levels rise and whose L5 needs a confirmed rank-up",
            Progress.Lines.Where(l => l.Edition == Edition.Core).All(l => real[l.Line] == "free") && sigs.Count == 7
            && sigs.Zip(sigs.Skip(1)).All(p => LevelOf(p.Second.Line) > LevelOf(p.First.Line)) && apex.Count == 3
            && apex.All(a => Enumerable.Range(2, a.MaxLevel - 1).All(n => LevelOf(ItemIds.Level(a.Line, n)) > LevelOf(ItemIds.Level(a.Line, n - 1)))
                && Requirement.Parse(real[ItemIds.Level(a.Line, 5)]).Root.Terms().Any(x => x.Kind is ReqKind.RankupOverall or ReqKind.RankupSkill))
            && Progress.Lines.All(l => Enumerable.Range(1, l.MaxLevel).All(n => real.ContainsKey(ItemIds.Level(l.Line, n))) && l.Variants.All(v => real.ContainsKey(ItemIds.Variant(l.Line, v)))),
            string.Join(" ", sigs.Select(l => LevelOf(l.Line))));
        // the playtest: "the developer's badges already meet its requirements". A developer-shaped history (retro, no rank-up
        // confirmed yet) with every badge and benchmark maxed must not open the top of the table.
        var dev = new ProgressFacts
        {
            Level = 19, Stars = 12, PbStars = 7, BadgeUps = 5, LockIns = 8, CoachPlaced = true, BestBadge = 4, BadgeDrills = 26,
            BenchDrillsAtLeast = new[] { 26, 26, 26, 26, 26, 20, 12, 6, 2 }, SensFinderDone = true, StreakWeeks = 3, WeeklyImprove = 2,
        };
        var devMet = real.Where(x => Requirement.Parse(x.Value).Met(dev)).Select(x => x.Key).ToHashSet();
        bool devTop = devMet.Any(id => ItemIds.TryParse(id, out var ln, out int lv, out var vv) && Progress.LineOf(ln) is { } pl
            && (pl.Edition == Edition.Apex && (lv >= 4 || vv != null) || pl.Edition == Edition.Signature && pl.Order == sigs.Count - 1));
        Check("developer-shaped facts (level 19, 12 stars, Pro badges, every benchmark): Signature #1-#5 and Apex #1 L1-L2, nothing at Apex L4/L5, variants or the last Signature",
            !devTop && sigs.Take(5).All(l => devMet.Contains(l.Line)) && !devMet.Contains(sigs[5].Line) && devMet.Contains(apex[0].Line)
            && devMet.Contains(ItemIds.Level(apex[0].Line, 2)) && !devMet.Contains(ItemIds.Level(apex[0].Line, 3)) && !devMet.Contains(apex[1].Line),
            string.Join(",", devMet.OrderBy(x => x)));
        // a grinder who never improves: XP for days, no stars or rank-ups
        var grind = new ProgressFacts { Level = 80, BadgeDrills = 10, BestBadge = 1, CoachPlaced = true, LockIns = 40, StreakWeeks = 20 };
        var grindMet = real.Where(x => Requirement.Parse(x.Value).Met(grind)).Select(x => x.Key).ToList();
        Check("a grinder at level 80 with no stars or rank-ups gets Signature #1 and the Apex looks/animation, no kill effect past Signature #1 and no finisher",
            grindMet.All(id => ItemIds.TryParse(id, out var ln, out int lv, out var vv) && Progress.LineOf(ln) is { } pl
                && (pl.Edition == Edition.Core || pl.Edition == Edition.Signature && pl.Order == 0 || pl.Edition == Edition.Apex && vv == null && lv <= 2 && pl.Order == 0 || pl.Edition == Edition.Apex && vv == null && lv == 2))
            && grindMet.Contains(sigs[0].Line) && grindMet.Contains(apex[0].Line), string.Join(",", grindMet));
        // upgrade slots need the base (structural), checked by the engine when recording unlocks
        var (e, _) = Fresh();
        var lines = Progress.Lines.ToList();
        var first = lines.First(l => l.Edition == Edition.Apex);
        e.F.Xp = XpCurve.TotalFor(40);
        e.F.Stars = 20;
        e.CheckUnlocks("dev", T0);
        bool ordered = e.F.Unlocked.ContainsKey(first.Line) && e.F.Unlocked.ContainsKey(ItemIds.Level(first.Line, 2)) && e.F.Unlocked.ContainsKey(ItemIds.Level(first.Line, 3));
        var (e2, _) = Fresh();
        e2.F.Stars = 99; // stars alone: kill effects need their line's base and levels below
        e2.CheckUnlocks("dev", T0);
        Check("upgrades need their base: levels unlock in order, never a level without the one below", ordered
            && e.F.Unlocked.Keys.Where(id => ItemIds.TryParse(id, out _, out int lv, out var v) && v == null && lv > 1)
                .All(id => ItemIds.TryParse(id, out var ln, out int lv, out _) && e.F.Unlocked.ContainsKey(ItemIds.Level(ln, lv - 1)))
            && !e2.F.Unlocked.Keys.Any(id => Progress.LineOfItem(id) is { Edition: Edition.Apex }));
        Check("no Apex finisher, Apex variant or last Signature at level 40 + 20 stars without rank-ups",
            !e.F.Unlocked.Keys.Any(id => ItemIds.TryParse(id, out var ln, out int lv, out var vv) && Progress.LineOf(ln) is { } pl
                && (pl.Edition == Edition.Apex && (lv == 5 || vv != null) || pl.Edition == Edition.Signature && pl.Order == sigs.Count - 1)),
            string.Join(",", e.F.Unlocked.Keys));
    }

    static void NeverRevoke()
    {
        var (e, _) = Fresh();
        var ultra = Progress.Lines.LastOrDefault(l => l.Edition == Edition.Apex);
        if (ultra == null) { Say("SKIP never-revoke: no Apex line"); return; }
        e.F.Unlocked[ultra.Line] = T0; // a file from a more generous table: level 1, 0 stars
        var old = Progress.SwapEngineForTests(e);
        try
        {
            Check("never revoke: an unlock the current table wouldn't grant stays unlocked", Progress.IsUnlocked(ultra.Line) && Progress.Status(ultra.Line).Unlocked
                && !Progress.IsUnlocked(ItemIds.Level(ultra.Line, 2)));
            e.CheckUnlocks("run", T0);
            Check("… and checking unlocks never removes it", e.F.Unlocked.ContainsKey(ultra.Line));
        }
        finally { Progress.SwapEngineForTests(old); }
    }

    // ---------------- challenges ----------------

    static void Challenges()
    {
        var (e, s) = Fresh(challenges: true);
        var history = new List<RunRecord>();
        int tier = Math.Clamp(Main.I.Settings.Tier, 0, 4);
        for (int i = 0; i < 12; i++) history.Add(Rec(i % 2 == 0 ? "gridshot" : "flick", T0.AddDays(-5).AddMinutes(i * 3), 40000 + 100 * i, tier, badge: 1));
        s.Runs.AddRange(history);
        e.SyncIndex(s.Runs, null);
        var now = T0;
        e.EnsureChallenges(now);
        var e2 = new ProgressEngine(new ProgressFile { Salt = "test" });
        e2.EnsureChallenges(now);
        string Sig(ChallengeSet? set) => string.Join(";", set!.Items.Select(i => Inv($"{i.Kind}:{i.Drill}:{i.Baseline:0.#}:{i.Target}")));
        Check("challenges are deterministic for a salt and day (3 daily, 3 weekly)", Sig(e.F.Daily) == Sig(e2.F.Daily) && Sig(e.F.Weekly) == Sig(e2.F.Weekly)
            && e.F.Daily!.Items.Count == 3 && e.F.Weekly!.Items.Count == 3, Sig(e.F.Daily));
        bool noFinder = true;
        var e3 = new ProgressEngine(new ProgressFile { Salt = "finders" });
        for (int d = 0; d < 60; d++)
        {
            e3.EnsureChallenges(now.AddDays(d));
            noFinder &= e3.F.Daily!.Items.Concat(e3.F.Weekly!.Items).All(i => !XpRules.IsFinder(i.Drill));
            for (int k = 0; k < 3; k++) e3.Reroll(k, now.AddDays(d));
            noFinder &= e3.F.Daily!.Items.All(i => !XpRules.IsFinder(i.Drill));
        }
        Check("never the Sens or Crosshair Finder (60 days, with rerolls)", noFinder);
        // restart (reload) keeps the set; the clock going back keeps the newer set
        string sig = Sig(e.F.Daily);
        e.EnsureChallenges(now.AddHours(-30));
        Check("the clock going back keeps the newer daily set", Sig(e.F.Daily) == sig && e.F.Daily!.Key == ProgressTime.DayKey(now));
        // relative targets: beat your 7-day average (frozen when made)
        var (b, kind, _) = WarmupScore.Baseline("gridshot", tier, now);
        var key = ProgressTime.DayKey(now);
        e.F.Daily = new ChallengeSet
        {
            Key = key, Items = new()
            {
                new ChallengeItem { Kind = "focus_runs", Drill = "flick", Target = 3, Xp = 100 },
                new ChallengeItem { Kind = "match_avg", Drill = "gridshot", Baseline = b, Target = 1, Tier = tier, Xp = 100 },
                new ChallengeItem { Kind = "variety", Target = 3, Xp = 100 },
            },
        };
        Check("beat-your-average target = the 7-day baseline", kind == "7d" && b > 0);
        float secs = XpRules.SecsOf(Rec("gridshot", now));
        int beating = (int)(b * secs / 60f * 1.05f) + 1;
        var lockStep = Play(e, s, Rec("gridshot", now.AddMinutes(1), beating, tier), lockIn: true);
        Check("a Lock-In step never counts for a score target", e.F.Daily.Items[1].Value == 0 && Part(lockStep, "Daily") == 0);
        var real = Play(e, s, Rec("gridshot", now.AddMinutes(2), beating, tier));
        Check("a real run beating the average completes it (+100) and ticks", e.F.Daily.Items[1].Done && Part(real, "Daily") == 100 && real.Challenges.Any(c => c.Done),
            Inv($"{beating} vs {b:0} per min"));
        Play(e, s, Rec("flick", now.AddMinutes(3), 100, tier));
        Play(e, s, Rec("flick", now.AddMinutes(4), 100, tier));
        Play(e, s, Rec("spider", now.AddMinutes(4.5), 100, tier));
        var third = Play(e, s, Rec("flick", now.AddMinutes(5), 100, tier));
        Check("focus 3 runs and 3 different drills: both done, +100 each and +100 for all three", e.F.Daily.Items.All(i => i.Done) && e.XpBy.GetValueOrDefault("All dailies") == 100
            && e.F.Counters.Dailies == 3 && Part(third, "Daily") == 100, string.Join(",", third.Parts.Select(p => $"{p.Label}={p.Xp}")));
        var fin = Play(e, s, Rec(XpRules.SensFinderKey, now.AddMinutes(6)), finder: true);
        Check("finder runs never tick challenges", fin.Challenges.Count == 0);
        // focus drills are exempt from the variety decay while their challenge is open
        var (e4, s4) = Fresh(challenges: true);
        e4.F.Daily = new ChallengeSet { Key = key, Items = new() { new ChallengeItem { Kind = "focus_runs", Drill = "popup", Target = 9, Xp = 100 } } };
        e4.F.Weekly = new ChallengeSet { Key = ProgressTime.WeekKey(now), Items = new() };
        int last = 0;
        for (int i = 0; i < 8; i++) last = RunPart(Play(e4, s4, Rec("popup", now.AddMinutes(i), 100, 1)));
        Check("an open challenge's drill is exempt from the decay", last == 99, last.ToString(CultureInfo.InvariantCulture));
        // reroll: one a day
        var (e5, _) = Fresh(challenges: true);
        e5.EnsureChallenges(now);
        bool r1 = e5.Reroll(2, now), r2 = e5.Reroll(1, now);
        Check("one reroll a day (Rerolls = left)", r1 && !r2 && e5.F.Daily!.Rerolls == 0);
        e5.EnsureChallenges(now.AddDays(1));
        Check("rerolls reset with the day", e5.F.Daily!.Rerolls == 1);
        // weekly improvement: +5 % over the frozen baseline gives the star
        var (e6, s6) = Fresh(challenges: true);
        e6.F.Daily = new ChallengeSet { Key = key, Items = new() };
        e6.F.Weekly = new ChallengeSet { Key = ProgressTime.WeekKey(now), Items = new() { new ChallengeItem { Kind = "improve", Drill = "gridshot", Baseline = 1000, Target = 1, Tier = 1, Xp = 500, Stars = 1 } } };
        var w1 = Play(e6, s6, Rec("gridshot", now, (int)(1040 * secs / 60f), 1));
        var w2 = Play(e6, s6, Rec("gridshot", now.AddMinutes(1), (int)(1060 * secs / 60f) + 1, 1));
        Check("weekly improvement: +4 % no, +6 % done: +500 XP, 1 star, weeklyimprove", w1.StarsGained == 0 && Part(w2, "Weekly") == 500 && w2.StarsGained >= 1
            && e6.F.Counters.WeeklyImprove == 1);
        // titles are text for every kind
        var kinds = new[] { "focus_runs", "match_avg", "badge", "accuracy", "variety", "time", "fresh", "map_agent", "lockin", "days", "improve", "lockins", "badgeups", "skill_time", "pbs" };
        Check("every challenge kind has a title", kinds.All(k => Progress.ChallengeTitle(new ChallengeItem { Kind = k, Drill = "flick", Target = 3, Baseline = 1, Why = "Movement" }) is { Length: > 3 } t && t != "Challenge"));
    }

    // ---------------- real runs only ----------------

    /// <summary>A shortened run (dev --duration 6, a quick Lock-In step) is no real run: no PB, no PB XP, no star, no
    /// improvement or score challenge, and its score never becomes the best to beat.</summary>
    static void ShortRuns()
    {
        Check("real run rule: a 6-s run of a 60-s drill is not real; full length, untimed (completed) are; adaptive and kept-out are not",
            !XpRules.RealRun(true, false, true, 60f, 6f) && XpRules.RealRun(true, false, true, 60f, 60f) && XpRules.RealRun(true, false, false, 60f, 6f)
            && !XpRules.RealRun(true, true, true, 60f, 60f) && !XpRules.RealRun(false, false, true, 60f, 60f));
        var now = T0.AddDays(3);
        var (e, s) = Fresh(challenges: true);
        e.F.Daily = new ChallengeSet { Key = ProgressTime.DayKey(now), Items = new() { new ChallengeItem { Kind = "match_avg", Drill = "gridshot", Baseline = 1500, Target = 1, Tier = 1, Xp = 100 } } };
        e.F.Weekly = new ChallengeSet { Key = ProgressTime.WeekKey(now), Items = new() { new ChallengeItem { Kind = "improve", Drill = "gridshot", Baseline = 1000, Target = 1, Tier = 1, Xp = 500, Stars = 1 } } };
        for (int i = 0; i < 3; i++) Play(e, s, Rec("gridshot", now.AddMinutes(i), 1000, 1)); // under both targets
        int pbs0 = e.F.Counters.Pbs, stars0 = e.F.Stars;
        // a 6-s run: 1,800 points = 18,000 a minute, far over every baseline; GameSession passes Stored = RealRun(...) = false
        var shortRec = Rec("gridshot", now.AddMinutes(5), 1800, 1);
        shortRec.Metrics = new() { ["run.secs"] = 6f };
        var sh = Play(e, s, shortRec, active: 6f, stored: XpRules.RealRun(true, false, true, 60f, 6f));
        Check("a 6-s run: no PB, PB XP, star, weekly improvement or beat-your-average", sh.StarsGained == 0 && e.F.Stars == stars0 && e.F.Counters.Pbs == pbs0
            && Part(sh, "PB") == 0 && Part(sh, "Weekly") == 0 && Part(sh, "Daily") == 0 && !e.F.Weekly.Items[0].Done && !e.F.Daily.Items[0].Done,
            string.Join(",", sh.Parts.Select(p => $"{p.Label}={p.Xp}")) + Inv($" stars {sh.StarsGained}"));
        var full = Play(e, s, Rec("gridshot", now.AddMinutes(7), 1100, 1));
        Check("its score is not the best to beat: the next full run at +10 % is a real PB with its star", Part(full, "PB") == XpRules.PbXp && e.F.Counters.PbStars >= 1,
            string.Join(",", full.Parts.Select(p => $"{p.Label}={p.Xp}")));
    }

    // ---------------- streak ----------------

    static void Streak()
    {
        var (e, s) = Fresh();
        var mon = new DateTime(2026, 11, 2, 18, 0, 0);
        void Week(int w, int days) { for (int d = 0; d < days; d++) Play(e, s, Rec("flick", mon.AddDays(7 * w + d))); }
        Week(0, 1);
        Check("a week counts with 2 training days (1 is not enough)", (e.F.Streak?.Weeks ?? 0) == 0);
        Week(0, 2);
        int xp0 = e.F.Xp;
        Week(1, 2);
        Check("2 weeks in a row = streak 2 and the first milestone (+500)", e.F.Streak!.Weeks == 2 && e.F.Streak.Milestone == 2 && e.F.Xp - xp0 >= 500);
        Week(3, 2); // week 2 missed, no token
        Check("a missed week without a token restarts quietly; best kept", e.F.Streak.Weeks == 1 && e.F.Streak.Best == 2 && !e.F.Streak.RestUsed);
        Week(4, 2); Week(5, 2); Week(6, 2);
        Check("a rest token every 4 counted weeks (max 2)", e.F.Streak.Tokens == 1 && e.F.Streak.Counted == 6, Inv($"{e.F.Streak.Tokens} {e.F.Streak.Counted}"));
        Week(8, 2); // week 7 missed: the token covers it
        Check("a missed week uses a token automatically: streak safe", e.F.Streak.Weeks == 5 && e.F.Streak.Tokens == 0 && e.F.Streak.RestUsed);
        int milestoneXp = e.XpBy.GetValueOrDefault("Streak");
        Week(9, 2); Week(10, 2);
        Check("milestones pay once (2 / 4 / 8 …)", e.F.Streak.Milestone == 4 && e.XpBy["Streak"] == 500 + 750 && milestoneXp == 1250);
    }

    // ---------------- retro equals live ----------------

    static List<RunRecord> History(int n, int[]? perDay, int seed)
    {
        var rng = new Random(seed);
        var modes = new[] { "gridshot", "flick", "tracking", "strafebots", "spider", "counterstrafe", "microshot", "deathmatch", XpRules.SensFinderKey };
        var list = new List<RunRecord>();
        var day = new DateTime(2026, 10, 1, 19, 0, 0);
        int d = 0, k = 0;
        var best = new Dictionary<string, int>();
        while (list.Count < n)
        {
            int today = perDay != null ? perDay[d % perDay.Length] : 5 + rng.Next(20);
            for (int i = 0; i < today && list.Count < n; i++)
            {
                string m = modes[rng.Next(modes.Length)];
                int tier = rng.Next(10) == 0 ? -1 : rng.Next(3);
                int score = 5000 + rng.Next(6000) + 40 * k++;
                var r = Rec(m, day.AddMinutes(i * 2), score, tier, rng.Next(4) == 0 ? -1 : rng.Next(4), shots: rng.Next(12) == 0 ? 0 : 60, acc: 0.4f);
                if (rng.Next(25) == 0) { r.Score = 0; r.Kills = 0; }
                r.Warmup = rng.Next(5) == 0;
                if (rng.Next(3) == 0) r.Metrics = new() { ["run.secs"] = 30 + rng.Next(240) };
                list.Add(r);
            }
            day = day.AddDays(1 + (rng.Next(4) == 0 ? rng.Next(5) : 0));
            d++;
        }
        return list;
    }

    static void RetroEqualsLive()
    {
        foreach (var (name, runs) in new[] { ("generated 500-run history", History(500, null, 7)), ("developer-shaped history (187 runs, 12 days)", History(187, new[] { 33, 61, 18, 25, 14, 7, 5, 7, 4, 11, 1, 1 }, 11)) })
        {
            var (retro, _) = Fresh();
            retro.Replay(runs, Array.Empty<WarmupRecord>(), runs[^1].When, retro: false);
            var (live, s) = Fresh();
            foreach (var r in runs) Play(live, s, r, XpRules.RetroActive(r), finder: XpRules.IsFinder(r.Mode), lockIn: r.Warmup);
            Check($"retro equals live: {name}", retro.F.Xp == live.F.Xp && retro.F.Stars == live.F.Stars && retro.F.Counters.Pbs == live.F.Counters.Pbs
                && retro.F.Counters.BadgeUps == live.F.Counters.BadgeUps && retro.F.Rested == live.F.Rested && (retro.F.Streak?.Weeks ?? 0) == (live.F.Streak?.Weeks ?? 0)
                && retro.F.Unlocked.Keys.OrderBy(x => x).SequenceEqual(live.F.Unlocked.Keys.OrderBy(x => x)),
                Inv($"xp {retro.F.Xp}/{live.F.Xp} stars {retro.F.Stars}/{live.F.Stars} pbs {retro.F.Counters.Pbs}/{live.F.Counters.Pbs}"));
            Say(Inv($"  {name}: level {XpCurve.LevelOf(retro.F.Xp)}, {retro.F.Xp} XP, {retro.F.Stars} stars ({retro.F.Counters.PbStars} PB + {retro.F.Counters.BadgeUps} badge), {retro.F.Unlocked.Count} unlocks"));
        }
        // catch-up: runs after CreditedThrough only, once
        var (e, _) = Fresh();
        var h = History(120, null, 3);
        e.Replay(h.Take(80).ToList(), Array.Empty<WarmupRecord>(), h[79].When, retro: true);
        int xp80 = e.F.Xp;
        e.Replay(h, Array.Empty<WarmupRecord>(), h[^1].When, retro: false);
        int xpAll = e.F.Xp;
        e.Replay(h, Array.Empty<WarmupRecord>(), h[^1].When, retro: false);
        var (all, _) = Fresh();
        all.Replay(h, Array.Empty<WarmupRecord>(), h[^1].When, retro: true);
        Check("catch-up credits only runs after CreditedThrough, once, and lands where a full replay does", xpAll > xp80 && e.F.Xp == xpAll && xpAll == all.F.Xp && e.F.Stars == all.F.Stars,
            Inv($"{xp80} {xpAll} {e.F.Xp} {all.F.Xp}"));
        Check("retro credit records the reveal (runs, XP, stars, unlocks) and the welcome bonus", all.F.Retro is { Done: true, Shown: false, Runs: 120 } ri && ri.Xp == all.F.Xp
            && all.XpBy["Welcome"] == XpRules.Welcome && (ri.Unlocks?.Count ?? 0) == all.F.Unlocked.Count);
    }

    // ---------------- persistence ----------------

    static void Persistence(string root)
    {
        const string json = "{\"Version\":1,\"MinReader\":1,\"Xp\":700,\"Future\":{\"a\":1},\"Coach\":{\"Calib\":1,\"Shiny\":true,\"Days\":[{\"D\":\"2026-11-01\",\"O\":[1.5,0.9],\"X\":2}]},"
                          + "\"Daily\":{\"Key\":\"2026-11-07\",\"Items\":[{\"Kind\":\"focus_runs\",\"Drill\":\"flick\",\"Target\":3,\"New\":1}]},\"Unlocked\":{\"carbon\":\"2026-11-07T18:00:00\"}}";
        var f = ProgressStore.Parse(json);
        string back = ProgressStore.Serialize(f);
        Check("JSON round trip keeps unknown fields at every depth", f.Xp == 700 && back.Contains("\"Future\"") && back.Contains("\"Shiny\"") && back.Contains("\"X\"")
            && back.Contains("\"New\"") && ProgressStore.Parse(back).Coach!.Days![0].O![0] == 1.5f);
        Check("dev runs never write (SavesData false → CanWrite false)", !Main.I.SavesData && !ProgressStore.CanWrite);
        Directory.CreateDirectory(root);
        Paths.SetDataDirForTests(root);
        ProgressStore.Save(new ProgressFile { Xp = 5 });
        Check("Save is a no-op in a dev run (no file)", !File.Exists(ProgressStore.FilePath));
        File.WriteAllText(ProgressStore.FilePath, "{\"Xp\": 5, \"Unlocked\": {");
        var c = ProgressStore.Load(out var rc);
        Check("corrupt → kept as progress.json.corrupt, fresh file (re-derived by the retro credit)", rc == ProgressLoad.Corrupt && File.Exists(ProgressStore.FilePath + ".corrupt") && c.Xp == 0);
        File.WriteAllText(ProgressStore.FilePath, "{\"Version\":3,\"MinReader\":2,\"Xp\":9000,\"Unlocked\":{\"carbon\":\"2026-11-07T18:00:00\"}}");
        var n = ProgressStore.Load(out var rn);
        Check("newer MinReader → loaded read-only (never saved)", rn == ProgressLoad.Newer && n.Xp == 9000 && n.Unlocked.ContainsKey("carbon") && ProgressStore.ReadOnly && !ProgressStore.CanWrite);
        using (new FileStream(ProgressStore.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ProgressStore.Load(out var ru);
            Check("unreadable (locked) → in memory, nothing written", ru == ProgressLoad.Unreadable && !ProgressStore.CanWrite);
        }
    }
}
