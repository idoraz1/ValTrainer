using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Warmup;

/// <summary>
/// Runs a warm-up: step card → drill (a normal <see cref="GameSession"/> with routine hooks: sens from the shifter, no
/// results screen) → next card … → summary. Leaving through the pause menu abandons it (the session's sens override
/// dies with the session, so the player's own sens is untouched).
/// </summary>
public sealed class WarmupRunner
{
    public static WarmupRunner? Current { get; private set; }

    public readonly WarmupPlan Plan;
    public readonly List<WarmupStepResult> Results = new();
    public readonly int Tier;
    readonly DateTime started = DateTime.Now;

    public int NextIndex => Results.Count;
    public PlannedStep? NextStep => NextIndex < Plan.Steps.Count ? Plan.Steps[NextIndex] : null;
    public WarmupStepResult? LastResult => Results.Count > 0 ? Results[^1] : null;

    /// <summary>Dev --wuquick: short steps and cards for automated runs.</summary>
    public static bool QuickDev => Main.I.Dev && CmdLine.Has("--wuquick");

    WarmupRunner(WarmupPlan plan)
    {
        Plan = plan;
        Tier = Main.I.Tier;
    }

    public static void Start(WarmupPlan plan)
    {
        Current = new WarmupRunner(plan);
        Log.Info($"[warmup] start {plan.Preset.Key}: {plan.Steps.Count} steps, {WarmupPlan.Clock(plan.PlaySeconds)} play, shifter {plan.ShiftLabel}, base sens {plan.BaseSens:0.000}, tier {Current.Tier}");
        Current.ShowCard();
    }

    /// <summary>Called when the player leaves to the menu (pause → Back to menu, card → Quit).</summary>
    public static void Abandon()
    {
        if (Current == null) return;
        Log.Info($"[warmup] abandoned after {Current.Results.Count(r => !r.Skipped)} of {Current.Plan.Steps.Count} steps; sens back to {Main.I.Sens:0.000}");
        Current = null;
    }

    /// <summary>Dev "--warmup quick|standard|pro" (with the saved/dev shifter settings) or "demo" (summary with made-up data).</summary>
    public static void StartDev(string arg)
    {
        if (arg.Equals("demo", StringComparison.OrdinalIgnoreCase)) { ShowDemo(); return; }
        var store = WarmupStore.I;
        var shift = CmdLine.After("--wushift")?.ToLowerInvariant() switch
        {
            "off" => ShiftMode.Off, "low" => ShiftMode.LowToNormal, "high" => ShiftMode.HighToNormal, _ => store.ShiftMode,
        };
        int pct = int.TryParse(CmdLine.After("--wupct"), out var p) ? Math.Clamp(p, 1, 60) : store.ShiftPct;
        Start(WarmupPlan.Build(WarmupPresets.Get(arg), shift, pct, Main.I.Sens, QuickDev));
    }

    void ShowCard()
    {
        if (Current != this) return;
        if (NextIndex >= Plan.Steps.Count) { Complete(true); return; }
        Main.I.ShowScreen(new WarmupCard(this));
    }

    /// <summary>Starts the next drill.</summary>
    public void StartStep()
    {
        if (Current != this || NextStep is not { } st) return;
        var make = WarmupDrills.Factory(st.Mode, st.Seconds, out float secs);
        if (make == null) { Log.Error($"[warmup] unknown drill '{st.Mode}', skipped"); Skip(); return; }
        bool quick = Plan.Quick;
        var session = new GameSession(make)
        {
            SessionSens = st.Shifted ? st.Sens : null,
            DurationOverride = quick ? st.Seconds : null,
            // Shifted-sens runs stay out of stats.json: the coach's sens advice would read them as a sens change.
            KeepInStats = !st.Shifted && !quick,
            OnFinished = OnStepDone,
        };
        Log.Info($"[warmup] step {st.Index + 1}/{Plan.Steps.Count} {st.Mode} {WarmupPlan.Clock(quick ? st.Seconds : secs)} sens {st.Sens:0.000} ({st.OffsetPct:+0.#;-0.#;0}%)");
        Main.I.ShowScreen(session);
        // Dev "--wuabandon N": during drill N, pause and leave to the menu like the pause menu's Back to menu (validation).
        if (Main.I.Dev && int.TryParse(CmdLine.After("--wuabandon"), out int ab) && ab == st.Index + 1)
            session.GetTree().CreateTimer(5.0).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(session) || !session.IsInsideTree()) return;
                Log.Info($"[warmup] dev abandon: CurrentSens in drill {session.CurrentSens:0.000}");
                session.Pause();
                Main.I.ShowMenu();
                Callable.From(() => Log.Info($"[warmup] after abandon: runner {(Current == null ? "cleared" : "STILL ACTIVE")}, player sens {Main.I.Sens:0.000}")).CallDeferred();
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
        };
        if (rec?.Metrics is { } mt)
        {
            if (mt.TryGetValue("track.ontarget", out var on)) r.TrackOn = on;
            if (mt.TryGetValue("flick.ttk_ms", out var ft)) r.FlickTtk = ft;
        }
        r.Value = WarmupScore.Value(st.Mode, r.Score, r.Accuracy, r.AvgKillMs, r.Kills, secs);
        Score(r);
        Results.Add(r);
        Log.Info($"[warmup] step {st.Index + 1} {st.Mode} done: CurrentSens {s.CurrentSens:0.000} (planned {st.Sens:0.000}, player {Main.I.Sens:0.000}) " +
                 $"{secs:0.0}s {WarmupScore.Label(st.Mode)} {WarmupScore.Format(st.Mode, r.Value)} index {(r.Index >= 0 ? $"{r.Index:0}% vs {r.BaselineKind}" : "n/a")}");
        Callable.From(ShowCard).CallDeferred();
    }

    /// <summary>Baseline + index: the player's usual level, or their first run of the drill in this warm-up.</summary>
    void Score(WarmupStepResult r)
    {
        var (b, kind, _) = WarmupScore.Baseline(r.Mode, Tier, started);
        if (b <= 0 && Results.FirstOrDefault(x => x.Mode == r.Mode && x.Value > 0) is { } first) { b = first.Value; kind = "today"; }
        if (b <= 0 && r.Value > 0) { b = r.Value; kind = "today"; }
        r.Baseline = b;
        r.BaselineKind = kind;
        r.Index = WarmupScore.IndexOf(r.Mode, r.Value, b);
    }

    public void Skip()
    {
        if (Current != this || NextStep is not { } st) return;
        Log.Info($"[warmup] step {st.Index + 1} {st.Mode} skipped");
        Results.Add(new WarmupStepResult { Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, OffsetPct = st.OffsetPct, Skipped = true });
        ShowCard();
    }

    public void FinishNow() => Complete(false);

    public void Quit()
    {
        Abandon();
        Main.I.ShowMenu();
    }

    void Complete(bool all)
    {
        if (Current != this) return;
        Current = null;
        if (Results.All(r => r.Skipped)) { Log.Info("[warmup] ended with no drills played"); Main.I.ShowMenu(); return; }
        var rec = MakeRecord(Plan, Tier, Results, started, all);
        var store = WarmupStore.I;
        var previous = store.Warmups.ToList();
        store.Warmups.Add(rec);
        store.Save();
        Log.Info($"[warmup] complete={all} steps {rec.Steps.Count(s => !s.Skipped)}/{Plan.Steps.Count} readiness {(rec.Readiness >= 0 ? $"{rec.Readiness:0}% ({rec.ReadinessKind})" : "n/a")}; sens back to {Main.I.Sens:0.000}");
        Main.I.ShowScreen(new WarmupSummary(rec, previous));
    }

    public static WarmupRecord MakeRecord(WarmupPlan plan, int tier, List<WarmupStepResult> steps, DateTime when, bool completed)
    {
        var rec = new WarmupRecord
        {
            When = when, Preset = plan.Preset.Key, Tier = tier, BaseSens = plan.BaseSens,
            Shift = plan.Shift switch { ShiftMode.Off => "off", ShiftMode.LowToNormal => "low", _ => "high" }, ShiftPct = plan.ShiftPct,
            Completed = completed, PlaySeconds = steps.Where(s => !s.Skipped).Sum(s => s.Seconds), Steps = steps.ToList(),
        };
        var real = steps.Where(s => !s.Skipped && !s.Shifted && s.Index >= 0 && s.BaselineKind is "7d" or "recent").ToList();
        if (real.Count > 0)
        {
            rec.Readiness = real.Average(s => s.Index);
            rec.ReadinessKind = real.All(s => s.BaselineKind == "7d") ? "7d" : "recent";
        }
        return rec;
    }

    // ---------------- dev demo ----------------

    /// <summary>Dev "--warmup demo": the summary screen with made-up results (layout checks without playing).</summary>
    static void ShowDemo()
    {
        var plan = WarmupPlan.Build(WarmupPresets.Get(CmdLine.After("--wupreset") ?? "standard"), ShiftMode.HighToNormal, WarmupPresets.DefaultShiftPct,
            Main.I.Sens > 0 ? Main.I.Sens : 0.4f, false);
        var rng = new Random(7);
        var steps = new List<WarmupStepResult>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var st = plan.Steps[i];
            float warm = 82 + 24 * (1 - MathF.Exp(-i / 2.2f)) + (float)rng.NextDouble() * 8 - 4; // a classic warm-up curve
            var kind = WarmupScore.Def(st.Mode);
            float baseV = kind switch { WarmupScore.Kind.Ttk => 620, WarmupScore.Kind.OnTarget => 52, WarmupScore.Kind.KillsPerMin => 4.2f, _ => 5200 };
            float v = kind == WarmupScore.Kind.Ttk ? baseV * 100 / warm : baseV * warm / 100;
            var r = new WarmupStepResult
            {
                Mode = st.Mode, Seconds = st.Seconds, Sens = st.Sens, OffsetPct = st.OffsetPct, Value = v, Baseline = baseV,
                BaselineKind = i == 1 ? "today" : "7d", Skipped = i == plan.Steps.Count - 3,
            };
            if (st.Mode == "tracking") r.TrackOn = v;
            if (st.Mode == "flick") r.FlickTtk = v;
            r.Index = WarmupScore.IndexOf(st.Mode, v, baseV);
            steps.Add(r);
        }
        var rec = MakeRecord(plan, Main.I.Tier, steps, DateTime.Now, true);
        var prev = Enumerable.Range(0, 5).Select(i => new WarmupRecord { When = DateTime.Now.AddDays(-5 + i), Readiness = 92 + i * 3, Preset = "quick" }).ToList();
        Main.I.ShowScreen(new WarmupSummary(rec, prev) { Demo = true });
    }
}
