using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;

namespace ValTrainer.Warmup;

/// <summary>What the player said at the check-in (and took into VALORANT).</summary>
public sealed class CheckIn
{
    /// <summary>1 flat … 9 wired; 1 shaky … 9 sure.</summary>
    public int Energy = 5, Confidence = 5;
    public bool Tilted;
    public string Goal = "", Cue = "";
    /// <summary>Breathing guide length at the end (s); 0 = off.</summary>
    public int BreathSeconds;
}

/// <summary>
/// Runs a Lock-In: check-in → step card → drill (a normal <see cref="GameSession"/> with routine hooks: tier offset,
/// adaptive difficulty, easier last minute, optional shifted sens, no results screen) → next card … → breathing (only
/// if the player wants it) → hand-off. Leaving through the pause menu abandons it (the session's sens override dies
/// with the session, so the player's own sens is untouched).
/// </summary>
public sealed class WarmupRunner
{
    public static WarmupRunner? Current { get; private set; }

    public readonly WarmupPlan Plan;
    public readonly List<WarmupStepResult> Results = new();
    public readonly int Tier;
    public CheckIn Check = new();
    readonly DateTime started = DateTime.Now;
    bool checkedIn, breathed;
    float breathSecs;

    public int NextIndex => Results.Count;
    public PlannedStep? NextStep => NextIndex < Plan.Steps.Count ? Plan.Steps[NextIndex] : null;
    public WarmupStepResult? LastResult => Results.Count > 0 ? Results[^1] : null;

    /// <summary>Dev --wuquick: short steps, cards and breathing for automated runs.</summary>
    public static bool QuickDev => Main.I.Dev && CmdLine.Has("--wuquick");

    /// <summary>Minutes since the check-in (dev "--wuelapsed N" adds N minutes to test the 15 / 20 minute reminders).</summary>
    public float ElapsedMinutes =>
        (float)(DateTime.Now - started).TotalMinutes + (Main.I.Dev && float.TryParse(CmdLine.After("--wuelapsed"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 0);

    WarmupRunner(WarmupPlan plan)
    {
        Plan = plan;
        Tier = plan.Tier;
    }

    public static void Start(WarmupPlan plan)
    {
        Current = new WarmupRunner(plan);
        Log.Info($"[lockin] start {plan.Preset.Key}: {plan.Steps.Count} steps, {WarmupPlan.Clock(plan.PlaySeconds)} play, " +
                 $"{plan.Steps.Count(s => s.Adaptive)} adaptive, {plan.Steps.Count(s => s.Easier)} easier, shifter {plan.ShiftLabel}, base sens {plan.BaseSens:0.000}, tier {Current.Tier}");
        if (DevCheckIn() is { } c) Current.CheckInDone(c);
        else Main.I.ShowScreen(new LockInCheckIn(Current));
    }

    /// <summary>Dev "--lockin-checkin E,C": skip the check-in UI with energy E and confidence C (goal and cue: the saved
    /// ones or the first suggestions; breathing on when E ≥ 7, as in the real check-in).</summary>
    static CheckIn? DevCheckIn()
    {
        if (!Main.I.Dev || CmdLine.After("--lockin-checkin") is not { } arg) return null;
        var p = arg.Split(',');
        int e = p.Length > 0 && int.TryParse(p[0], out var ev) ? Math.Clamp(ev, 1, 9) : 5;
        int c = p.Length > 1 && int.TryParse(p[1], out var cv) ? Math.Clamp(cv, 1, 9) : 5;
        var store = WarmupStore.I;
        return new CheckIn
        {
            Energy = e, Confidence = c, Tilted = CmdLine.Has("--lockin-tilted"),
            Goal = store.Goal.Length > 0 ? store.Goal : LockInText.Goals[0],
            Cue = store.Cue.Length > 0 ? store.Cue : LockInText.Cues[0],
            BreathSeconds = LockInText.WantsBreathing(e) ? store.BreathSeconds : 0,
        };
    }

    /// <summary>Called when the player leaves to the menu (pause → Back to menu, card → Quit).</summary>
    public static void Abandon()
    {
        if (Current == null) return;
        Log.Info($"[lockin] abandoned after {Current.Results.Count(r => !r.Skipped)} of {Current.Plan.Steps.Count} steps; sens back to {Main.I.Sens:0.000}");
        Current = null;
    }

    /// <summary>Dev "--warmup short|standard|training" (alias --lockin) with the saved/dev shifter settings, or a demo screen
    /// with made-up data: "demo" (hand-off), "demo-details" (the details page), "demo-breath" (breathing guide).
    /// "--lockin-ready [level]" fakes previous Lock-Ins for the ready check; "--lockin-ready-take" takes its offer.</summary>
    public static void StartDev(string arg)
    {
        if (arg.StartsWith("demo", StringComparison.OrdinalIgnoreCase)) { ShowDemo(arg.ToLowerInvariant()); return; }
        var store = WarmupStore.I;
        var shift = CmdLine.After("--wushift")?.ToLowerInvariant() switch
        {
            "off" => ShiftMode.Off, "low" => ShiftMode.LowToNormal, "high" => ShiftMode.HighToNormal, _ => store.ShiftMode,
        };
        int pct = int.TryParse(CmdLine.After("--wupct"), out var p) ? Math.Clamp(p, 1, WarmupPresets.MaxShiftPct) : store.ShiftPct;
        Start(WarmupPlan.Build(WarmupPresets.Get(arg), shift, pct, Main.I.Sens, QuickDev));
    }

    /// <summary>The check-in is done (or skipped by the dev switch): remember it and show the first card.</summary>
    public void CheckInDone(CheckIn c)
    {
        if (Current != this || checkedIn) return;
        checkedIn = true;
        Check = c;
        var store = WarmupStore.I;
        store.Energy = c.Energy;
        store.Confidence = c.Confidence;
        store.Goal = c.Goal;
        store.Cue = c.Cue;
        if (c.BreathSeconds > 0) store.BreathSeconds = c.BreathSeconds;
        store.Save();
        Log.Info($"[lockin] check-in: energy {c.Energy} confidence {c.Confidence}{(c.Tilted ? " tilted" : "")} goal \"{c.Goal}\" cue \"{c.Cue}\" " +
                 $"breathing {(c.BreathSeconds > 0 ? c.BreathSeconds + " s" : "off")}");
        // Dev "--wustart N": jump to drill N (earlier drills count as played without a result; card layout checks).
        if (Main.I.Dev && int.TryParse(CmdLine.After("--wustart"), out int from))
            foreach (var st in Plan.Steps.Take(Math.Clamp(from - 1, 0, Plan.Steps.Count - 1)))
                Results.Add(new WarmupStepResult { Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, Phase = st.Phase.ToString(), Tier = st.Tier });
        ShowCard();
    }

    void ShowCard()
    {
        if (Current != this) return;
        if (NextIndex >= Plan.Steps.Count) { Finish(true); return; }
        Main.I.ShowScreen(new WarmupCard(this));
    }

    // ---------------- ready check ("stop when you're ready", see LockInReady) ----------------

    ReadyOffer? ready;
    int readyFor = -1;

    /// <summary>Previous Lock-Ins for the ready check (dev "--lockin-ready [level]" uses made-up ones instead, so the
    /// offer can be tested without real history; nothing is saved).</summary>
    IEnumerable<WarmupRecord> ReadyHistory()
    {
        if (!Main.I.Dev || !CmdLine.Has("--lockin-ready")) return WarmupStore.I.Warmups;
        float lv = float.TryParse(CmdLine.After("--lockin-ready"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var l) ? Math.Clamp(l, 0, 4) : 0.5f;
        return LockInReady.FakeHistory(Plan, lv);
    }

    /// <summary>The ready check for the next card (computed and logged once per card). Null outside the mechanics phase.</summary>
    public ReadyOffer? ReadyCheck()
    {
        if (readyFor == NextIndex) return ready;
        readyFor = NextIndex;
        ready = LockInReady.Check(Plan, Results, ReadyHistory());
        if (ready != null)
            Log.Info($"[lockin] ready check before step {NextIndex + 1}: {(ready.Offer ? "offer skip to deathmatch" : "no offer")} ({ready.Reason}); " +
                     $"played {WarmupPlan.Clock(ready.PlayedSeconds)}{(ready.Drills.Count > 0 ? "; " + LockInReady.Describe(ready) : "")}");
        return ready;
    }

    bool ReadyOffered => readyFor == NextIndex && ready is { Offer: true };

    /// <summary>The player took the offer: the rest of the mechanics is recorded as skipped and the deathmatch comes next.</summary>
    public void SkipToDeathmatch()
    {
        if (Current != this || !ReadyOffered || ready is not { } o) return;
        Log.Info($"[lockin] ready: player chose skip to deathmatch; steps {o.FirstSkip + 1}-{o.DmIndex} recorded as skipped");
        while (NextIndex < o.DmIndex && NextStep is { } st)
            Results.Add(new WarmupStepResult
            {
                Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, OffsetPct = st.OffsetPct, Skipped = true, Phase = st.Phase.ToString(), Tier = st.Tier,
                Adaptive = st.Adaptive,
            });
        ShowCard();
    }

    /// <summary>Where an adaptive drill starts: the player's previous level in it (mean of the last 3, at most one tier
    /// away from today's tier), else today's tier.</summary>
    float StartLevel(PlannedStep st)
    {
        var history = WarmupStore.I.Warmups.Concat(new[] { new WarmupRecord { Steps = Results } });
        float prev = WarmupScore.PreviousLevel(st.Mode, history, 3);
        return prev < 0 ? Tier : Math.Clamp(prev, Tier - 1, Tier + 1);
    }

    /// <summary>Starts the next drill.</summary>
    public void StartStep()
    {
        if (Current != this || NextStep is not { } st) return;
        if (ReadyOffered) Log.Info($"[lockin] ready: offer not taken, playing step {st.Index + 1} {st.Mode}");
        var make = WarmupDrills.Factory(st.Mode, st.Seconds, out float secs);
        if (make == null) { Log.Error($"[lockin] unknown drill '{st.Mode}', skipped"); Skip(); return; }
        bool quick = Plan.Quick;
        var session = new GameSession(make)
        {
            SessionSens = st.Shifted ? st.Sens : null,
            DurationOverride = quick ? st.Seconds : null,
            TierOverride = st.Tier,
            Adaptive = st.Adaptive ? new Staircase(StartLevel(st)) : null,
            EaseLastSeconds = st.EaseLast,
            // Shifted-sens runs stay out of stats.json (the coach's sens advice would read them as a sens change), and so do
            // adaptive runs (their difficulty moves, so their scores aren't comparable with normal runs).
            KeepInStats = !st.Shifted && !st.Adaptive && !quick,
            OnFinished = OnStepDone,
        };
        Log.Info($"[lockin] step {st.Index + 1}/{Plan.Steps.Count} {st.Mode} {Phases.Name(st.Phase)} {WarmupPlan.Clock(quick ? st.Seconds : secs)} tier {st.Tier}" +
                 $"{(st.Adaptive ? $" adaptive from {session.Adaptive!.Level:0.00}" : "")}{(st.EaseLast > 0 ? $" easier last {st.EaseLast:0}s" : "")} " +
                 $"sens {st.Sens:0.000} ({st.OffsetPct:+0.#;-0.#;0}%)");
        Main.I.ShowScreen(session);
        // Dev "--wuabandon N": during drill N, pause and leave to the menu like the pause menu's Back to menu (validation).
        if (Main.I.Dev && int.TryParse(CmdLine.After("--wuabandon"), out int ab) && ab == st.Index + 1)
            session.GetTree().CreateTimer(5.0).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(session) || !session.IsInsideTree()) return;
                Log.Info($"[lockin] dev abandon: CurrentSens in drill {session.CurrentSens:0.000}");
                session.Pause();
                Main.I.ShowMenu();
                Callable.From(() => Log.Info($"[lockin] after abandon: runner {(Current == null ? "cleared" : "STILL ACTIVE")}, player sens {Main.I.Sens:0.000}")).CallDeferred();
                if (CmdLine.Has("--wuquit")) Main.I.GetTree().CreateTimer(3.0).Timeout += () => Main.I.GetTree().Quit();
            };
    }

    void OnStepDone(GameSession s)
    {
        if (Current != this || NextStep is not { } st) return;
        var rec = s.Record;
        var m = s.Mode;
        float secs = s.Telemetry.Duration > 0 ? s.Telemetry.Duration : s.Now;
        var r = new WarmupStepResult
        {
            Mode = st.Mode, Seconds = secs, Sens = s.CurrentSens, OffsetPct = st.OffsetPct,
            Score = m.Score, Hits = m.Hits, Shots = m.Shots, Kills = m.Kills, Deaths = m.Deaths,
            Accuracy = m.Shots > 0 || st.Mode == "tracking" ? m.Accuracy : -1,
            AvgKillMs = m.KillTimes.Count > 0 ? m.KillTimes.Average() : -1,
            TelemetryFile = rec?.TelemetryFile,
            Phase = st.Phase.ToString(), Tier = st.Tier,
        };
        if (rec?.Metrics is { } mt)
        {
            if (mt.TryGetValue("track.ontarget", out var on)) r.TrackOn = on;
            if (mt.TryGetValue("flick.ttk_ms", out var ft)) r.FlickTtk = ft;
            if (mt.TryGetValue("adapt.level", out var lv)) { r.Adaptive = true; r.Level = lv; }
            if (mt.TryGetValue("adapt.hitrate", out var hr)) r.HitRate = hr;
            if (mt.TryGetValue("adapt.trials", out var tr)) r.Trials = (int)tr;
        }
        if (s.Adaptive is { } a && a.Trials > 0)
        {
            r.Adaptive = true;
            if (r.Level < 0) r.Level = a.Threshold >= 0 ? a.Threshold : a.Level;
            if (r.HitRate < 0) r.HitRate = a.HitRate;
            if (r.Trials == 0) r.Trials = a.Trials;
        }
        else if (st.Adaptive && s.Adaptive != null) r.Adaptive = true; // no trials (e.g. nothing shot): no level
        r.Value = WarmupScore.Value(st.Mode, r.Score, r.Accuracy, r.AvgKillMs, r.Kills, secs);
        Score(r);
        Results.Add(r);
        string vs = r.Adaptive ? $"level {WarmupScore.LevelText(r.Level)} at {r.HitRate * 100:0}% hits over {r.Trials} trials (previous {(r.PrevLevel >= 0 ? r.PrevLevel.ToString("0.0") : "none")})"
            : $"index {(r.Index >= 0 ? $"{r.Index:0}% vs {r.BaselineKind}" : "n/a")}";
        Log.Info($"[lockin] step {st.Index + 1} {st.Mode} done: CurrentSens {s.CurrentSens:0.000} (planned {st.Sens:0.000}, player {Main.I.Sens:0.000}) tier {s.Tier} " +
                 $"{secs:0.0}s {WarmupScore.Label(st.Mode)} {WarmupScore.Format(st.Mode, r.Value)} {vs}");
        Callable.From(ShowCard).CallDeferred();
    }

    /// <summary>Baseline + index: the player's usual level at the tier played, or their first run of the drill in this
    /// Lock-In. Adaptive steps compare their level with previous Lock-Ins instead (their scores aren't comparable).</summary>
    void Score(WarmupStepResult r)
    {
        if (r.Adaptive)
        {
            r.PrevLevel = WarmupScore.PreviousLevel(r.Mode, WarmupStore.I.Warmups);
            return;
        }
        var (b, kind, _) = WarmupScore.Baseline(r.Mode, r.Tier >= 0 ? r.Tier : Tier, started);
        if (b <= 0 && Results.FirstOrDefault(x => x.Mode == r.Mode && !x.Adaptive && x.Tier == r.Tier && x.Value > 0) is { } first) { b = first.Value; kind = "today"; }
        if (b <= 0 && r.Value > 0) { b = r.Value; kind = "today"; }
        r.Baseline = b;
        r.BaselineKind = kind;
        r.Index = WarmupScore.IndexOf(r.Mode, r.Value, b);
    }

    public void Skip()
    {
        if (Current != this || NextStep is not { } st) return;
        Log.Info($"[lockin] step {st.Index + 1} {st.Mode} skipped{(ReadyOffered ? " (ready offer not taken)" : "")}");
        Results.Add(new WarmupStepResult
        {
            Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, OffsetPct = st.OffsetPct, Skipped = true, Phase = st.Phase.ToString(), Tier = st.Tier,
            Adaptive = st.Adaptive,
        });
        ShowCard();
    }

    public void FinishNow() => Finish(false);

    public void Quit()
    {
        Abandon();
        Main.I.ShowMenu();
    }

    /// <summary>After the last drill (or FINISH NOW): the breathing guide if the player wanted it, then the hand-off.</summary>
    void Finish(bool all)
    {
        if (Current != this) return;
        if (Results.All(r => r.Skipped)) { Current = null; Log.Info("[lockin] ended with no drills played"); Main.I.ShowMenu(); return; }
        if (Check.BreathSeconds > 0 && !breathed)
        {
            Main.I.ShowScreen(new LockInBreath(Check.BreathSeconds, secs => { breathed = true; breathSecs = secs; Complete(all); }, () => Complete(all)));
            return;
        }
        Complete(all);
    }

    void Complete(bool all)
    {
        if (Current != this) return;
        Current = null;
        var rec = MakeRecord(Plan, Tier, Results, started, all);
        rec.Energy = Check.Energy;
        rec.Confidence = Check.Confidence;
        rec.Tilted = Check.Tilted;
        rec.Goal = Check.Goal;
        rec.Cue = Check.Cue;
        rec.Breathed = breathed;
        rec.BreathSeconds = breathSecs;
        rec.TotalSeconds = (float)(DateTime.Now - started).TotalSeconds;
        var store = WarmupStore.I;
        var previous = store.Warmups.ToList();
        store.Warmups.Add(rec);
        store.Save();
        Log.Info($"[lockin] complete={all} steps {rec.Steps.Count(s => !s.Skipped)}/{Plan.Steps.Count} readiness {(rec.Readiness >= 0 ? $"{rec.Readiness:0}% ({rec.ReadinessKind})" : "n/a")} " +
                 $"breathed {(breathed ? $"{breathSecs:0}s" : "no")} total {WarmupPlan.Clock(rec.TotalSeconds)}; sens back to {Main.I.Sens:0.000}");
        Main.I.ShowScreen(new LockInHandoff(rec, previous));
    }

    public static WarmupRecord MakeRecord(WarmupPlan plan, int tier, List<WarmupStepResult> steps, DateTime when, bool completed)
    {
        var rec = new WarmupRecord
        {
            When = when, Preset = plan.Preset.Key, Tier = tier, BaseSens = plan.BaseSens,
            Shift = plan.Shift switch { ShiftMode.Off => "off", ShiftMode.LowToNormal => "low", _ => "high" }, ShiftPct = plan.ShiftPct,
            Completed = completed, PlaySeconds = steps.Where(s => !s.Skipped).Sum(s => s.Seconds), Steps = steps.ToList(),
        };
        var real = steps.Where(s => !s.Skipped && !s.Shifted && !s.Adaptive && s.Index >= 0 && s.BaselineKind is "7d" or "recent").ToList();
        if (real.Count > 0)
        {
            rec.Readiness = real.Average(s => s.Index);
            rec.ReadinessKind = real.All(s => s.BaselineKind == "7d") ? "7d" : "recent";
        }
        return rec;
    }

    // ---------------- dev demo ----------------

    /// <summary>Dev "--warmup demo…": the hand-off / details / breathing screens with made-up results (layout checks
    /// without playing). "--wupreset" picks the routine, "--wudemo-index N" the mean index (default ~102).</summary>
    static void ShowDemo(string which)
    {
        if (which == "demo-breath")
        {
            Main.I.ShowScreen(new LockInBreath(QuickDev ? 12 : 90, _ => Main.I.ShowMenu(), Main.I.ShowMenu));
            return;
        }
        var plan = WarmupPlan.Build(WarmupPresets.Get(CmdLine.After("--wupreset") ?? WarmupPresets.DefaultKey), ShiftMode.Off, WarmupPresets.DefaultShiftPct,
            Main.I.Sens > 0 ? Main.I.Sens : 0.4f, false);
        float target = float.TryParse(CmdLine.After("--wudemo-index"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ti) ? ti : 102;
        var rng = new Random(7);
        var steps = new List<WarmupStepResult>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var st = plan.Steps[i];
            float warm = target - 14 * MathF.Exp(-i / 2.2f) + (float)rng.NextDouble() * 8 - 4; // a classic warm-up curve
            var kind = WarmupScore.Def(st.Mode);
            float baseV = kind switch { WarmupScore.Kind.Ttk => 620, WarmupScore.Kind.OnTarget => 52, WarmupScore.Kind.KillsPerMin => 4.2f, _ => 5200 };
            float v = kind == WarmupScore.Kind.Ttk ? baseV * 100 / warm : baseV * warm / 100;
            var r = new WarmupStepResult
            {
                Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, OffsetPct = st.OffsetPct, Value = v, Phase = st.Phase.ToString(), Tier = st.Tier,
            };
            if (st.Phase == Phase.Calibration)
            {
                r.Adaptive = true;
                r.Level = Math.Clamp(plan.Tier + (float)rng.NextDouble() * 1.2f - 0.4f, 0, 4);
                r.HitRate = 0.76f + (float)rng.NextDouble() * 0.08f;
                r.Trials = 60 + rng.Next(40);
                r.PrevLevel = i == 2 ? -1 : r.Level - 0.3f + (float)rng.NextDouble() * 0.4f;
            }
            else
            {
                r.Baseline = baseV;
                r.BaselineKind = i == 1 ? "today" : "7d";
                r.Index = WarmupScore.IndexOf(st.Mode, v, baseV);
            }
            if (st.Mode == "tracking") r.TrackOn = v;
            if (st.Mode == "flick") r.FlickTtk = v;
            steps.Add(r);
        }
        var rec = MakeRecord(plan, Main.I.Tier, steps, DateTime.Now, true);
        rec.Energy = 7; rec.Confidence = 6; rec.Goal = LockInText.Goals[0]; rec.Cue = LockInText.Cues[0];
        rec.Breathed = true; rec.BreathSeconds = 90; rec.TotalSeconds = plan.EstimatedSeconds + 95;
        var prev = Enumerable.Range(0, 5).Select(i => new WarmupRecord { When = DateTime.Now.AddDays(-5 + i), Readiness = 92 + i * 3, Preset = "standard" }).ToList();
        if (which == "demo-details") Main.I.ShowScreen(new WarmupSummary(rec, prev) { Demo = true });
        else Main.I.ShowScreen(new LockInHandoff(rec, prev) { Demo = true });
    }
}
