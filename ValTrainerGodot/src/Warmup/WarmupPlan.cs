using System.Globalization;
using ValTrainer.Core;
using ValTrainer.Modes;

namespace ValTrainer.Warmup;

/// <summary>Sens shifter direction: start above (default) or below the player's sens and step back to it.</summary>
public enum ShiftMode { Off, HighToNormal, LowToNormal }

/// <summary>One drill of a preset: mode key, planned seconds (the drill's own length except deathmatch) and a focus cue.</summary>
public sealed record WarmupStepDef(string Mode, float Seconds, string Cue);

/// <summary>A warm-up routine. The first <see cref="ShiftSteps"/> drills form the sens-shift phase; the rest are always
/// played at the player's real sens (re-calibration first, then bots, then a deathmatch).</summary>
public sealed record WarmupPreset(string Key, string Name, string Blurb, int ShiftSteps, WarmupStepDef[] Steps);

/// <summary>The built-in routines (research notes in docs and the warm-up screen).
/// Order follows what pros and coaches describe: smooth and easy first (tracking, big targets), then precise flicks and
/// target switching, then game-like bots and movement, and a deathmatch last ("play to warm up, not to win").
/// Every non-deathmatch drill runs at its normal 60 s so its score still counts towards personal bests.</summary>
public static class WarmupPresets
{
    const string CueTrack = "Smooth and relaxed: light grip, keep the crosshair glued to the target. No rushing.";
    const string CueGrid = "Big targets: find a rhythm. Clean clicks over speed.";
    const string CueSpider = "Out and back: flick to the target, return to centre. Stop on the target, then click.";
    const string CueFlick = "One motion to the head and stop. Click when you're on it; no spraying, no micro-adjust chains.";
    const string CueRecal = "Back to your real sens. Expect to fall short on the first flicks. Let your arm re-calibrate, stay smooth.";
    const string CueFlickReal = "Your real sens: crisp single flicks. This is the speed you'll play at.";
    const string CueBots = "Strafing bots: let the crosshair follow, shoot when it lines up with the head.";
    const string CueCounter = "Tap the opposite key to stop dead, then shoot. Accuracy needs you standing still.";
    const string CuePeek = "Pre-aim the edge at head height. Shoot as they appear.";
    const string CueDuel = "Wide swing, stop, shoot. Crosshair at head height before you peek.";
    const string CueSpray = "Pull down and slightly against the pattern. First bullets matter most.";
    const string CueDm = "Play to warm up, not to win: crosshair placement, first-bullet accuracy, calm resets.";

    public static readonly WarmupPreset[] All =
    {
        new("quick", "Quick", "Short warm-up before a match", 3, new WarmupStepDef[]
        {
            new("tracking", 60, CueTrack),
            new("gridshot", 60, CueGrid),
            new("flick", 60, CueFlick),
            new("tracking", 60, CueRecal),
            new("strafebots", 60, CueBots),
            new("deathmatch", 120, CueDm),
        }),
        new("standard", "Standard", "Full warm-up: aim, bots, movement and a deathmatch", 4, new WarmupStepDef[]
        {
            new("tracking", 60, CueTrack),
            new("gridshot", 60, CueGrid),
            new("spider", 60, CueSpider),
            new("flick", 60, CueFlick),
            new("tracking", 60, CueRecal),
            new("flick", 60, CueFlickReal),
            new("strafebots", 60, CueBots),
            new("counterstrafe", 60, CueCounter),
            new("peek", 60, CuePeek),
            new("deathmatch", 240, CueDm),
        }),
        new("pro", "Pro", "Long pro-style session: every skill, then a full deathmatch", 5, new WarmupStepDef[]
        {
            new("tracking", 60, CueTrack),
            new("gridshot", 60, CueGrid),
            new("tracking", 60, CueTrack),
            new("spider", 60, CueSpider),
            new("flick", 60, CueFlick),
            new("tracking", 60, CueRecal),
            new("flick", 60, CueFlickReal),
            new("spider", 60, CueSpider),
            new("strafebots", 60, CueBots),
            new("spray_vandal", 60, CueSpray),
            new("counterstrafe", 60, CueCounter),
            new("peek", 60, CuePeek),
            new("peekduel", 60, CueDuel),
            new("strafebots", 60, CueBots),
            new("deathmatch", 480, CueDm),
        }),
    };

    public static WarmupPreset Get(string? key) =>
        All.FirstOrDefault(p => p.Key.Equals(key ?? "", StringComparison.OrdinalIgnoreCase))
        ?? Agents.AgentRoutines.Find(key) // agent warm-ups ("agent:jett") from the Agents screen
        ?? All[0];

    /// <summary>Start offsets the shifter offers (%); the default is the middle one.</summary>
    public static readonly int[] ShiftOptions = { 10, 20, 35 };
    public const int DefaultShiftPct = 20;
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

/// <summary>One step of a built plan: drill, length and the sensitivity it is played at.</summary>
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
    public string Name => WarmupDrills.Name(Mode);
    public string Short => WarmupDrills.Short(Mode);
}

/// <summary>A preset + sens shifter settings turned into concrete steps.</summary>
public sealed class WarmupPlan
{
    public WarmupPreset Preset = WarmupPresets.All[0];
    public ShiftMode Shift = ShiftMode.HighToNormal;
    public int ShiftPct = WarmupPresets.DefaultShiftPct;
    public float BaseSens;
    /// <summary>Dev --wuquick: every step ~8 s (deathmatch 12 s) so the simulated player runs the whole routine fast.</summary>
    public bool Quick;
    public List<PlannedStep> Steps = new();

    public const float QuickStep = 8f, QuickDm = 12f;
    /// <summary>Countdown + step card overhead per step (s), for the time estimate.</summary>
    public const float StepOverhead = 10f;

    public float PlaySeconds => Steps.Sum(s => s.Seconds);
    public float EstimatedSeconds => PlaySeconds + Steps.Count * StepOverhead;
    public int ShiftedCount => Steps.Count(s => s.Shifted);
    public float ShiftedSeconds => Steps.Where(s => s.Shifted).Sum(s => s.Seconds);
    public float RealSeconds => PlaySeconds - ShiftedSeconds;

    /// <summary>
    /// The sens shifter protocol: the first N drills (N = preset.ShiftSteps) start at ±start% and step back linearly
    /// (e.g. +20% → +13% → +7%), then everything else, and so the final calibration, is at the real sens.
    /// </summary>
    public static float[] Offsets(int steps, int shiftSteps, ShiftMode mode, int startPct)
    {
        var o = new float[steps];
        if (mode == ShiftMode.Off || startPct <= 0) return o;
        int n = Math.Clamp(shiftSteps, 0, Math.Max(0, steps - 2)); // always leave at least two drills at the real sens
        float sign = mode == ShiftMode.LowToNormal ? -1f : 1f;
        for (int i = 0; i < n; i++) o[i] = sign * startPct * (n - i) / n;
        return o;
    }

    public static float SensAt(float baseSens, float offsetPct) => MathF.Round(baseSens * (1f + offsetPct / 100f) * 1000f) / 1000f;

    public static WarmupPlan Build(WarmupPreset preset, ShiftMode shift, int shiftPct, float baseSens, bool quick)
    {
        var plan = new WarmupPlan { Preset = preset, Shift = baseSens > 0 ? shift : ShiftMode.Off, ShiftPct = shiftPct, BaseSens = baseSens, Quick = quick };
        var off = Offsets(preset.Steps.Length, preset.ShiftSteps, plan.Shift, shiftPct);
        for (int i = 0; i < preset.Steps.Length; i++)
        {
            var d = preset.Steps[i];
            WarmupDrills.Factory(d.Mode, d.Seconds, out float secs);
            if (quick) secs = d.Mode == "deathmatch" ? QuickDm : QuickStep;
            plan.Steps.Add(new PlannedStep
            {
                Index = i, Mode = d.Mode, Seconds = secs, OffsetPct = off[i], Sens = off[i] == 0 ? baseSens : SensAt(baseSens, off[i]), Cue = d.Cue,
            });
        }
        return plan;
    }

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
