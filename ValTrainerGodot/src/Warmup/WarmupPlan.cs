using System.Globalization;
using ValTrainer.Core;
using ValTrainer.Modes;

namespace ValTrainer.Warmup;

/// <summary>Sens shifter direction: start above or below the player's sens and step back to it. Lock-In keeps it off;
/// only the Training day preset offers it (first phase only, at most 10%).</summary>
public enum ShiftMode { Off, HighToNormal, LowToNormal }

/// <summary>Lock-In phases, in order (timeline, cards and summary). Check-in and Lock in aren't drills.</summary>
public enum Phase { CheckIn, Activation, Calibration, Mechanics, Deathmatch, LockIn }

public static class Phases
{
    public static string Name(Phase p) => p switch
    {
        Phase.CheckIn => "CHECK-IN",
        Phase.Activation => "ACTIVATION",
        Phase.Calibration => "CALIBRATION",
        Phase.Mechanics => "MECHANICS",
        Phase.Deathmatch => "DEATHMATCH",
        _ => "LOCK IN",
    };

    /// <summary>One short line per phase (setup timeline).</summary>
    public static string Blurb(Phase p) => p switch
    {
        Phase.CheckIn => "how you feel",
        Phase.Activation => "easy start, one tier down",
        Phase.Calibration => "adapts to ~80% hits",
        Phase.Mechanics => "bots and movement",
        Phase.Deathmatch => "last minute easier",
        _ => "goal and cue",
    };
}

/// <summary>One drill of a preset: mode key, planned seconds (the drill's own length except deathmatch), a focus cue, its
/// phase, and the routine tweaks: <paramref name="Adaptive"/> (staircase near 80% hits, when the drill supports it),
/// <paramref name="TierOffset"/> (activation drills: −1) and <paramref name="EaseLast"/> (deathmatch: easier last N s).</summary>
public sealed record WarmupStepDef(string Mode, float Seconds, string Cue, Phase Phase = Phase.Mechanics, bool Adaptive = false, int TierOffset = 0, float EaseLast = 0);

/// <summary>A Lock-In routine. The first <see cref="ShiftSteps"/> drills form the optional sens-shift phase (Training day
/// only; 0 = no shifter); everything else is played at the player's real sens.</summary>
public sealed record WarmupPreset(string Key, string Name, string Blurb, int ShiftSteps, WarmupStepDef[] Steps)
{
    /// <summary>Extra label on the setup screen (e.g. "for practice days, not right before ranked").</summary>
    public string Note { get; init; } = "";
    public bool AllowShift => ShiftSteps > 0;
}

/// <summary>The built-in Lock-In routines (evidence summary on the setup screen).
/// Simple to game-like: an easy activation start one tier down, adaptive calibration drills near 80% hits, bots and
/// movement at the player's tier, and a deathmatch whose last minute is easier so the session ends on success.
/// Every non-deathmatch drill runs at its normal 60 s.</summary>
public static class WarmupPresets
{
    // Cues point at the target or the crosshair (external focus), never at the hand or wrist.
    public const string CueTrack = "Keep the crosshair on the target's head. Smooth, no rushing.";
    const string CueGrid = "Find a rhythm. Land on each target, then click.";
    const string CueSpider = "Out to the target, back to the centre. Land on it, then click.";
    public const string CueFlick = "One motion to the head. Stop on it, then click.";
    const string CueFlick2 = "Crisp flicks at your real speed. Land on the head first.";
    const string CueBots = "Crosshair at head height. Let it meet the target, then shoot.";
    const string CueCounter = "Come to a full stop, then shoot. Moving shots miss.";
    const string CuePeek = "Crosshair on the edge at head height before they appear.";
    const string CueDuel = "Swing wide with the crosshair at head height. Stop, then shoot.";
    const string CueSpray = "Pull the crosshair down against the pattern. The first bullets matter most.";
    public const string CueDm = "Play it like the first round: crosshair at head height, one angle at a time.";

    static WarmupStepDef Act(string mode, string cue) => new(mode, 60, cue, Phase.Activation, TierOffset: -1);
    static WarmupStepDef Cal(string mode, string cue) => new(mode, 60, cue, Phase.Calibration, Adaptive: true);
    static WarmupStepDef Mech(string mode, string cue) => new(mode, 60, cue, Phase.Mechanics);
    static WarmupStepDef Dm(float secs, float ease) => new("deathmatch", secs, CueDm, Phase.Deathmatch, EaseLast: ease);

    public const string DefaultKey = "standard";

    public static readonly WarmupPreset[] All =
    {
        new("short", "Short", "A quick Lock-In when time is tight", 0, new[]
        {
            Act("tracking", CueTrack),
            Cal("flick", CueFlick),
            Cal("spider", CueSpider),
            Mech("strafebots", CueBots),
            Dm(120, 45),
        }),
        new("standard", "Lock-In", "The full Lock-In before ranked", 0, new[]
        {
            Act("tracking", CueTrack),
            Act("gridshot", CueGrid),
            Cal("flick", CueFlick),
            Cal("spider", CueSpider),
            Cal("flick", CueFlick2),
            Mech("strafebots", CueBots),
            Mech("counterstrafe", CueCounter),
            Dm(180, 60),
        }),
        new("training", "Training day", "Every skill, then a long deathmatch", 3, new[]
        {
            Act("tracking", CueTrack),
            Act("gridshot", CueGrid),
            Act("tracking", CueTrack),
            Cal("spider", CueSpider),
            Cal("flick", CueFlick),
            Cal("spider", CueSpider),
            Cal("flick", CueFlick2),
            Mech("strafebots", CueBots),
            Mech("spray_vandal", CueSpray),
            Mech("counterstrafe", CueCounter),
            Mech("peek", CuePeek),
            Mech("peekduel", CueDuel),
            Dm(300, 60),
        }) { Note = "for practice days, not right before ranked" },
    };

    /// <summary>Preset keys from before Lock-In (warm-up history): Quick → Short, Pro → Training day.</summary>
    public static string MapLegacy(string? key) => key?.ToLowerInvariant() switch
    {
        null or "" => DefaultKey,
        "quick" => "short",
        "pro" => "training",
        _ => key,
    };

    public static WarmupPreset Get(string? key) =>
        All.FirstOrDefault(p => p.Key.Equals(MapLegacy(key), StringComparison.OrdinalIgnoreCase))
        ?? Agents.AgentRoutines.Find(key) // agent Lock-Ins ("agent:jett") from the Agents screen
        ?? All.First(p => p.Key == DefaultKey);

    /// <summary>Start offsets the shifter offers (%): the research doesn't support more than about 10% before play.</summary>
    public static readonly int[] ShiftOptions = { 5, 10 };
    public const int DefaultShiftPct = 10, MaxShiftPct = 10;
}

/// <summary>Drill names, short labels and factories (deathmatch gets the planned length when its mode supports one).</summary>
public static class WarmupDrills
{
    static readonly Dictionary<string, TrainingMode?> protos = new();

    public static TrainingMode? Proto(string key)
    {
        if (!protos.TryGetValue(key, out var m))
        {
            try { m = ModeRegistry.Find(key)?.Invoke(); } catch { m = null; }
            protos[key] = m;
        }
        return m;
    }

    public static string Name(string key) => Proto(key)?.Name ?? key;
    public static string Category(string key) => Proto(key)?.Category ?? "Aim";
    public static bool CanAdapt(string key) => Proto(key)?.SupportsAdaptive ?? false;

    public static string Short(string key) => key switch
    {
        "tracking" => "TRACK",
        "gridshot" => "GRID",
        "spider" => "SPIDER",
        "flick" => "FLICK",
        "strafebots" => "BOTS",
        "counterstrafe" => "C-STRAFE",
        "peek" => "PEEK",
        "peekduel" => "DUELS",
        "spray_vandal" => "SPRAY",
        "deathmatch" => "DM",
        "reaction" => "REACT",
        _ => Agents.AgentRoutines.ShortLabel(key) ?? Name(key).ToUpperInvariant(),
    };

    /// <summary>A factory for the drill. A mode with a "duration…" constructor parameter (deathmatch) is built with the
    /// planned length; other modes keep their own length (their scores stay comparable with normal runs).
    /// <paramref name="seconds"/> returns the length the drill will actually run.</summary>
    public static Func<TrainingMode>? Factory(string key, float planned, out float seconds)
    {
        seconds = planned;
        var reg = ModeRegistry.Find(key);
        if (reg == null) return null;
        TrainingMode proto;
        try { proto = reg(); } catch { return null; }
        var type = proto.GetType();
        foreach (var c in type.GetConstructors())
        {
            var ps = c.GetParameters();
            if (ps.Length == 0 || !(ps[0].Name ?? "").Contains("duration", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ps.Skip(1).All(p => p.IsOptional)) continue;
            var pt = ps[0].ParameterType;
            if (pt != typeof(float) && pt != typeof(double) && pt != typeof(int)) continue;
            var args = new object?[ps.Length];
            args[0] = Convert.ChangeType(planned, pt, CultureInfo.InvariantCulture);
            for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
            try
            {
                var test = (TrainingMode)c.Invoke(args);
                seconds = test.Timed ? test.Duration : planned;
                return () => (TrainingMode)c.Invoke(args);
            }
            catch { break; }
        }
        seconds = proto.Timed ? proto.Duration : planned;
        return reg;
    }
}

/// <summary>One step of a built plan: drill, length, phase, tier, the sensitivity it is played at and the routine tweaks.</summary>
public sealed class PlannedStep
{
    public int Index;
    public string Mode = "";
    public float Seconds;
    public float Sens;
    /// <summary>Sens offset from the player's sens in % (0 = real sens).</summary>
    public float OffsetPct;
    public bool Shifted => MathF.Abs(OffsetPct) > 0.01f;
    public string Cue = "";
    public Phase Phase = Phase.Mechanics;
    /// <summary>Tier this drill plays at (the player's tier + the step's offset, 0..4). Adaptive drills only start here.</summary>
    public int Tier;
    public int TierOffset;
    /// <summary>The drill adapts its difficulty (only when the preset asks for it and the drill supports it).</summary>
    public bool Adaptive;
    /// <summary>Deathmatch finisher: the bots get easier for the last N seconds (0 = off).</summary>
    public float EaseLast;
    public string Name => WarmupDrills.Name(Mode);
    public string Short => WarmupDrills.Short(Mode);
    /// <summary>The run is played one tier below the player's (activation).</summary>
    public bool Easier => Tier < Main.I.Tier;
}

/// <summary>A preset + sens shifter settings turned into concrete steps.</summary>
public sealed class WarmupPlan
{
    public WarmupPreset Preset = WarmupPresets.All[0];
    public ShiftMode Shift = ShiftMode.Off;
    public int ShiftPct = WarmupPresets.DefaultShiftPct;
    public float BaseSens;
    public int Tier;
    /// <summary>Dev --wuquick: every step ~8 s (deathmatch 12 s) so the simulated player runs the whole routine fast.</summary>
    public bool Quick;
    public List<PlannedStep> Steps = new();

    public const float QuickStep = 8f, QuickDm = 12f, QuickEase = 5f;
    /// <summary>Countdown + step card overhead per step (s), for the time estimate.</summary>
    public const float StepOverhead = 10f;
    /// <summary>Check-in and the goal card (s), for the time estimate. Breathing isn't counted (most players skip it).</summary>
    public const float FrameSeconds = 30f;
    /// <summary>Wall-clock minutes: a soft reminder at 15, "you're ready, go queue" at 20.</summary>
    public const float RemindMinutes = 15f, ReadyMinutes = 20f;

    public float PlaySeconds => Steps.Sum(s => s.Seconds);
    public float EstimatedSeconds => PlaySeconds + Steps.Count * StepOverhead + FrameSeconds;
    public int ShiftedCount => Steps.Count(s => s.Shifted);
    public float ShiftedSeconds => Steps.Where(s => s.Shifted).Sum(s => s.Seconds);
    public float RealSeconds => PlaySeconds - ShiftedSeconds;

    /// <summary>
    /// The sens shifter (Training day only): the first N drills of the activation phase step linearly from ±start% back
    /// to the real sens (e.g. +10% → +5% → 0), so the calibration and everything after it are at the real sens.
    /// </summary>
    public static float[] Offsets(WarmupPreset preset, ShiftMode mode, int startPct)
    {
        var o = new float[preset.Steps.Length];
        if (mode == ShiftMode.Off || startPct <= 0 || !preset.AllowShift) return o;
        int lead = preset.Steps.TakeWhile(d => d.Phase == Phase.Activation).Count();
        int n = Math.Min(preset.ShiftSteps, lead);
        if (n == 0) return o;
        float sign = mode == ShiftMode.LowToNormal ? -1f : 1f, pct = Math.Min(startPct, WarmupPresets.MaxShiftPct);
        for (int i = 0; i < n; i++) o[i] = n == 1 ? sign * pct : sign * pct * (n - 1 - i) / (n - 1);
        return o;
    }

    public static float SensAt(float baseSens, float offsetPct) => MathF.Round(baseSens * (1f + offsetPct / 100f) * 1000f) / 1000f;

    public static WarmupPlan Build(WarmupPreset preset, ShiftMode shift, int shiftPct, float baseSens, bool quick)
    {
        var plan = new WarmupPlan
        {
            Preset = preset, Shift = baseSens > 0 && preset.AllowShift ? shift : ShiftMode.Off, ShiftPct = Math.Min(shiftPct, WarmupPresets.MaxShiftPct),
            BaseSens = baseSens, Quick = quick, Tier = Main.I.Tier,
        };
        var off = Offsets(preset, plan.Shift, plan.ShiftPct);
        for (int i = 0; i < preset.Steps.Length; i++)
        {
            var d = preset.Steps[i];
            WarmupDrills.Factory(d.Mode, d.Seconds, out float secs);
            if (quick) secs = d.Mode == "deathmatch" ? QuickDm : QuickStep;
            plan.Steps.Add(new PlannedStep
            {
                Index = i, Mode = d.Mode, Seconds = secs, OffsetPct = off[i], Sens = off[i] == 0 ? baseSens : SensAt(baseSens, off[i]), Cue = d.Cue,
                Phase = d.Phase, TierOffset = d.TierOffset, Tier = Math.Clamp(plan.Tier + d.TierOffset, 0, 4),
                Adaptive = d.Adaptive && WarmupDrills.CanAdapt(d.Mode),
                EaseLast = d.EaseLast <= 0 ? 0 : quick ? Math.Min(d.EaseLast, QuickEase) : Math.Min(d.EaseLast, secs),
            });
        }
        return plan;
    }

    /// <summary>Phases with drills, in order, with their play time (setup timeline).</summary>
    public IEnumerable<(Phase Phase, float Seconds, int Drills)> PhaseSpans() =>
        Steps.GroupBy(s => s.Phase).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(s => s.Seconds), g.Count()));

    public static string Clock(float secs)
    {
        int s = Math.Max(0, (int)MathF.Round(secs));
        return $"{s / 60}:{s % 60:00}";
    }

    public static string Minutes(float secs) => $"{Math.Max(1, (int)MathF.Round(secs / 60f))} MIN";

    public string ShiftLabel => Shift switch
    {
        ShiftMode.HighToNormal => $"+{ShiftPct}% → your sens",
        ShiftMode.LowToNormal => $"−{ShiftPct}% → your sens",
        _ => "off",
    };
}
