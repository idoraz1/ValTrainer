using System.Globalization;
using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Warmup;

/// <summary>Result of one warm-up step. -1 = not available (JSON can't hold NaN).</summary>
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
    /// <summary>Performance as % of the baseline, oriented so higher = better (100 = your usual).</summary>
    public float Index = -1;
    /// <summary>A few aim-coach numbers for the lock-in readout (-1 = n/a).</summary>
    public float TrackOn = -1, FlickTtk = -1;
    public string? TelemetryFile;

    [System.Text.Json.Serialization.JsonIgnore] public bool Shifted => MathF.Abs(OffsetPct) > 0.01f;
}

/// <summary>One finished warm-up (warmups.json).</summary>
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
    /// <summary>Mean index of the steps at the real sens that have a history baseline (-1 = none): "how warmed up".</summary>
    public float Readiness = -1;
    public string ReadinessKind = "";
}

/// <summary>Warm-up preferences and history (warmups.json in the data folder; never written by dev/automated runs).</summary>
public sealed class WarmupStore
{
    public string Preset = "quick";
    public string Shift = "high";
    public int ShiftPct = WarmupPresets.DefaultShiftPct;
    public List<WarmupRecord> Warmups = new();

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Paths.DataDir, "warmups.json");
    [System.Text.Json.Serialization.JsonIgnore] bool readFailed;

    static WarmupStore? instance;
    public static WarmupStore I => instance ??= Load();

    static WarmupStore Load()
    {
        string path = FilePath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<WarmupStore>(File.ReadAllText(path), Json) ?? new WarmupStore();
                s.Warmups ??= new();
                s.Warmups.RemoveAll(w => w == null || w.Steps == null);
                return s;
            }
        }
        catch (Exception e) when (Paths.IsCorruptData(e)) { Paths.KeepCorrupt(path, e); }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); warm-ups won't be saved this session");
            return new WarmupStore { readFailed = true };
        }
        return new WarmupStore();
    }

    public void Save()
    {
        if (readFailed || !Main.I.SavesData) return;
        if (Warmups.Count > 200) Warmups.RemoveRange(0, Warmups.Count - 200);
        try { Paths.TryWriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Error($"Saving warm-ups failed: {e.Message}"); }
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
}
