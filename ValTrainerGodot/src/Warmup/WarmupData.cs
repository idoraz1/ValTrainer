using System.Globalization;
using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Warmup;

/// <summary>Result of one Lock-In step. -1 = not available (JSON can't hold NaN).</summary>
public sealed class WarmupStepResult
{
    public string Mode = "";
    public float Seconds;
    public float Sens;
    public float OffsetPct;
    public bool Skipped;
    public int Score, Hits, Shots, Kills, Deaths;
    public float Accuracy = -1, AvgKillMs = -1;
    /// <summary>The drill's main number (see <see cref="WarmupScore.Def"/>), e.g. time to kill in ms or % on target.</summary>
    public float Value = -1;
    /// <summary>What <see cref="Value"/> is compared to, and where that comes from ("7d", "recent", "today" or "").</summary>
    public float Baseline = -1;
    public string BaselineKind = "";
    /// <summary>Performance as % of the baseline, oriented so higher = better (100 = your usual). -1 for adaptive steps.</summary>
    public float Index = -1;
    /// <summary>A few aim-coach numbers for the lock-in readout (-1 = n/a).</summary>
    public float TrackOn = -1, FlickTtk = -1;
    public string? TelemetryFile;
    /// <summary>Lock-In (v2): phase name ("Activation" …), the tier played (start tier for adaptive steps; -1 = unknown).</summary>
    public string Phase = "";
    public int Tier = -1;
    /// <summary>Adaptive step: the level held near the target hit rate (0 Rookie … 4 Pro), the run's hit rate (0..1) and
    /// trials; <see cref="PrevLevel"/> = mean level of this drill in the previous Lock-Ins (-1 = none).</summary>
    public bool Adaptive;
    public float Level = -1, HitRate = -1, PrevLevel = -1;
    public int Trials;

    [System.Text.Json.Serialization.JsonIgnore] public bool Shifted => MathF.Abs(OffsetPct) > 0.01f;
}

/// <summary>One finished Lock-In or older warm-up (warmups.json).</summary>
public sealed class WarmupRecord
{
    public DateTime When;
    public string Preset = "";
    public int Tier;
    public float BaseSens;
    public string Shift = "off";
    public int ShiftPct;
    public bool Completed;
    public float PlaySeconds;
    public List<WarmupStepResult> Steps = new();
    /// <summary>Mean index of the fixed-tier steps at the real sens that have a history baseline (-1 = none).</summary>
    public float Readiness = -1;
    public string ReadinessKind = "";
    /// <summary>Lock-In check-in (v2; 0 = not asked): energy 1 flat … 9 wired, confidence 1 shaky … 9 sure.</summary>
    public int Energy, Confidence;
    public bool Tilted;
    /// <summary>The process goal and cue word the player took into VALORANT ("" = none).</summary>
    public string Goal = "", Cue = "";
    /// <summary>The breathing guide ran (and for how long, s).</summary>
    public bool Breathed;
    public float BreathSeconds;
    /// <summary>Wall-clock length from the check-in to the hand-off (s; 0 = unknown).</summary>
    public float TotalSeconds;
    /// <summary>Personal match log (optional, see <see cref="LockInMatchLog"/>): the first match after this Lock-In,
    /// "W", "L", "D" or "" (not answered), and "felt locked in" 1–5 (0 = not answered). Local only.</summary>
    public string Match = "";
    public int Felt;
    /// <summary>The menu has asked about this Lock-In (it never asks twice).</summary>
    public bool MatchAsked;
}

/// <summary>Lock-In preferences and history (warmups.json in the data folder; never written by dev/automated runs).</summary>
public sealed class WarmupStore
{
    /// <summary>File format: 0/1 = warm-up (≤ 1.4), 2 = Lock-In. Loading an older file migrates it once. Fields added
    /// later with safe defaults (the match log: Match, Felt, MatchAsked) don't change the version.</summary>
    public int Version;
    public const int CurrentVersion = 2;
    public string Preset = WarmupPresets.DefaultKey;
    public string Shift = "off";
    public int ShiftPct = WarmupPresets.DefaultShiftPct;
    /// <summary>Last check-in, remembered for the next one.</summary>
    public int Energy = 5, Confidence = 5;
    public string Goal = "", Cue = "";
    /// <summary>Breathing guide length (s) when it runs.</summary>
    public int BreathSeconds = 90;
    public List<WarmupRecord> Warmups = new();

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Paths.DataDir, "warmups.json");
    [System.Text.Json.Serialization.JsonIgnore] bool readFailed;

    static WarmupStore? instance;
    public static WarmupStore I => instance ??= Load();

    static WarmupStore Load()
    {
        var s = LoadFile();
        foreach (var w in s.Warmups) w.Match ??= "";
        // Dev "--matchlog-demo [N]": add N made-up rated Lock-Ins (default 24) for the match-log readout.
        if (CmdLine.Dev && CmdLine.Has("--matchlog-demo")) LockInMatchLog.AddDemo(s, int.TryParse(CmdLine.After("--matchlog-demo"), out int n) ? n : 24);
        return s;
    }

    static WarmupStore LoadFile()
    {
        string path = FilePath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<WarmupStore>(File.ReadAllText(path), Json) ?? new WarmupStore();
                s.Warmups ??= new();
                s.Warmups.RemoveAll(w => w == null || w.Steps == null);
                s.Goal ??= "";
                s.Cue ??= "";
                if (s.Version < CurrentVersion) s.Migrate();
                return s;
            }
        }
        catch (Exception e) when (Paths.IsCorruptData(e)) { Paths.KeepCorrupt(path, e); }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); Lock-Ins won't be saved this session");
            return new WarmupStore { readFailed = true, Version = CurrentVersion };
        }
        return new WarmupStore { Version = CurrentVersion };
    }

    /// <summary>Warm-up → Lock-In (once): the sens shifter goes off (the research doesn't support shifting before ranked)
    /// and old preset keys map to the new ones. Records stay as they are.</summary>
    void Migrate()
    {
        Log.Info($"[lockin] warmups.json v{Version} → v{CurrentVersion}: sens shifter off (was {Shift} {ShiftPct}%), preset {Preset} → {WarmupPresets.MapLegacy(Preset)}");
        Shift = "off";
        ShiftPct = Math.Min(ShiftPct <= 0 ? WarmupPresets.DefaultShiftPct : ShiftPct, WarmupPresets.MaxShiftPct);
        Preset = WarmupPresets.MapLegacy(Preset);
        if (Preset.StartsWith(Agents.AgentRoutines.Prefix, StringComparison.OrdinalIgnoreCase)) Preset = WarmupPresets.DefaultKey;
        Version = CurrentVersion;
        Save();
    }

    public void Save()
    {
        if (readFailed || !Main.I.SavesData) return;
        if (Warmups.Count > 200) Warmups.RemoveRange(0, Warmups.Count - 200);
        try { Paths.TryWriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Error($"Saving Lock-Ins failed: {e.Message}"); }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public ShiftMode ShiftMode
    {
        get => Shift switch { "off" => ShiftMode.Off, "low" => ShiftMode.LowToNormal, _ => ShiftMode.HighToNormal };
        set => Shift = value switch { ShiftMode.Off => "off", ShiftMode.LowToNormal => "low", _ => "high" };
    }
}

/// <summary>How a step is scored and compared with the player's usual level.</summary>
public static class WarmupScore
{
    public enum Kind { ScorePerMin, Ttk, OnTarget, KillsPerMin }

    public static Kind Def(string mode) => mode switch
    {
        "tracking" => Kind.OnTarget,
        "flick" or "strafebots" or "peek" or "counterstrafe" or "spray_transfer" or "operator" or "reaction" => Kind.Ttk,
        "deathmatch" => Kind.KillsPerMin,
        _ => Kind.ScorePerMin,
    };

    public static bool LowerBetter(string mode) => Def(mode) == Kind.Ttk;

    public static string Label(string mode) => Def(mode) switch
    {
        Kind.Ttk => mode == "reaction" ? "reaction" : "time to kill",
        Kind.OnTarget => "on target",
        Kind.KillsPerMin => "kills / min",
        _ => "score / min",
    };

    public static string Format(string mode, float v)
    {
        if (v < 0) return "—";
        var inv = CultureInfo.InvariantCulture;
        return Def(mode) switch
        {
            Kind.Ttk => v.ToString("0", inv) + " ms",
            Kind.OnTarget => v.ToString("0", inv) + "%",
            Kind.KillsPerMin => v.ToString("0.0", inv),
            _ => v.ToString("#,0", inv),
        };
    }

    public static float Value(string mode, int score, float accuracy, float avgKillMs, int kills, float secs) => Def(mode) switch
    {
        Kind.OnTarget => accuracy > 0 ? accuracy * 100f : -1,
        Kind.Ttk => avgKillMs > 0 ? avgKillMs : -1,
        Kind.KillsPerMin => secs >= 3 ? kills * 60f / secs : -1,
        _ => secs >= 3 && score > 0 ? score * 60f / secs : -1,
    };

    /// <summary>Length of a stored run: the coach's run.secs if known, else the drill's normal length.</summary>
    static float SecsOf(RunRecord r)
    {
        if (r.Metrics != null && r.Metrics.TryGetValue("run.secs", out var s) && s > 1) return s;
        var p = WarmupDrills.Proto(r.Mode);
        return p != null && p.Timed ? p.Duration : 60f;
    }

    public static float Value(RunRecord r) => Value(r.Mode, r.Score, r.Accuracy, r.AvgKillMs, r.Kills, SecsOf(r));

    /// <summary>The player's usual level in this drill at this tier: the mean of normal (non-warm-up) runs from the last
    /// 7 days, or of the last 10 runs if there are fewer than 2 recent ones.</summary>
    public static (float Value, string Kind, int N) Baseline(string mode, int tier, DateTime now)
    {
        var runs = Main.I.Stats.Runs.ToArray()
            .Where(r => r != null && r.Mode == mode && r.Tier == tier && !r.Warmup)
            .Select(r => (r.When, V: Value(r))).Where(x => x.V > 0).OrderByDescending(x => x.When).ToList();
        var week = runs.Where(x => (now - x.When).TotalDays <= 7).ToList();
        if (week.Count >= 2) return (week.Average(x => x.V), "7d", week.Count);
        var recent = runs.Take(10).ToList();
        if (recent.Count >= 1) return (recent.Average(x => x.V), "recent", recent.Count);
        return (-1, "", 0);
    }

    public static float IndexOf(string mode, float value, float baseline)
    {
        if (value <= 0 || baseline <= 0) return -1;
        return 100f * (LowerBetter(mode) ? baseline / value : value / baseline);
    }

    public static string BaselineText(string kind) => kind switch
    {
        "7d" => "your 7-day average",
        "recent" => "your recent average",
        "today" => "your first run of it today",
        _ => "no history yet",
    };

    // ---------------- Lock-In ----------------

    /// <summary>An adaptive level as "2.4 · Veteran+" ("—" when unknown).</summary>
    public static string LevelText(float level) =>
        level < 0 ? "—" : $"{level.ToString("0.0", CultureInfo.InvariantCulture)} · {Difficulty.LevelName(level)}";

    /// <summary>Mean adaptive level of this drill over the last <paramref name="n"/> Lock-In steps that had one (-1 = none).</summary>
    public static float PreviousLevel(string mode, IEnumerable<WarmupRecord> records, int n = 5)
    {
        var lv = records.SelectMany(r => r.Steps).Where(s => s.Adaptive && !s.Skipped && s.Mode == mode && s.Level >= 0).Select(s => s.Level).TakeLast(n).ToList();
        return lv.Count > 0 ? lv.Average() : -1;
    }

    /// <summary>The verdict's direction, for the shape drawn next to it (colour alone isn't enough for colour-blind
    /// players): +1 above, 0 in, -1 below your usual range, -2 no comparison. Same ±6% band as <see cref="Verdict"/>.</summary>
    public static int VerdictDir(float readiness) => readiness < 0 ? -2 : readiness >= 106 ? 1 : readiness >= 94 ? 0 : -1;

    /// <summary>The neutral verdict on the hand-off (never "cold"): above / in / below your usual range (±6%).</summary>
    public static (string Head, string Sub, Godot.Color Col) Verdict(float readiness, string kind)
    {
        string usual = BaselineText(kind);
        var inv = CultureInfo.InvariantCulture;
        if (readiness < 0) return ("NO COMPARISON YET", "Play these drills normally a few times and the next Lock-In compares you with your usual level.", UI.UiTheme.Text);
        string pct = readiness.ToString("0", inv);
        if (readiness >= 106) return ("ABOVE YOUR USUAL RANGE", $"{pct}% of {usual}, over the drills you have history for.", UI.UiTheme.Good);
        if (readiness >= 94) return ("IN YOUR USUAL RANGE", $"{pct}% of {usual}, over the drills you have history for.", UI.UiTheme.Teal);
        return ("BELOW YOUR USUAL RANGE TODAY", $"{pct}% of {usual}. Warm-up scores vary a lot and say little about how your match will go.", UI.UiTheme.Text);
    }
}
