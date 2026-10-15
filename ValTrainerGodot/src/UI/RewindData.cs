using System.Runtime.CompilerServices;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Game;

namespace ValTrainer.UI;

/// <summary>
/// Data side of the Rewind screen: insights built once per run on a worker thread (cached per telemetry object), the
/// coach's "Show me" search through recent saved runs, and plain-English names / colours for moment codes.
/// </summary>
public static class RewindData
{
    static readonly ConditionalWeakTable<RunTelemetry, Task<RunInsights>> cache = new();

    /// <summary>Insights of one run, built once on a worker thread. Never faults (a failed build gives empty insights).</summary>
    public static Task<RunInsights> Insights(RunTelemetry t)
    {
        lock (cache)
        {
            if (!cache.TryGetValue(t, out var task))
            {
                task = Task.Run(() =>
                {
                    try { return RunInsights.Build(t); }
                    catch { return new RunInsights { Run = t }; }
                });
                cache.Add(t, task);
            }
            return task;
        }
    }

    /// <summary>The insights if they are ready, else null (and starts building them).</summary>
    public static RunInsights? Ready(RunTelemetry t)
    {
        var task = Insights(t);
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    /// <summary>The run has something to rewind: frames with a target in them.</summary>
    public static bool CanRewind(RunTelemetry? t)
    {
        if (t == null || t.Frames.Count < 2) return false;
        foreach (var f in t.Frames) if (f.FocusId != 0) return true;
        return false;
    }

    // ---------------- "Show me" search ----------------

    /// <summary>Codes with moments, per saved run (file + write time), so repeated searches don't re-analyse.</summary>
    static readonly Dictionary<string, HashSet<string>> codesByFile = new();

    /// <summary>
    /// Newest saved run (at most <paramref name="maxFiles"/> newest files) whose insights have moments for
    /// <paramref name="code"/>. Runs on a worker thread; null when none has one.
    /// </summary>
    public static Task<(RunTelemetry Run, RunInsights Ins)?> FindExample(string code, int maxFiles = 15) => Task.Run(() =>
    {
        // the newest runs, then older runs the recordings cleanup kept as examples of this problem
        var candidates = RunTelemetry.RecentFiles(maxFiles).Concat(Game.RecordingStore.FilesShowing(code)).Distinct().ToList();
        foreach (var file in candidates)
        {
            try
            {
                string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
                HashSet<string>? codes;
                lock (codesByFile) codesByFile.TryGetValue(key, out codes);
                if (codes != null && !codes.Contains(code)) continue;
                var run = RunTelemetry.Load(file);
                if (run != null) run.SourceFile ??= file; // its .vtr sidecar (3D replay) is loaded only for the run that is shown
                if (run == null || !CanRewind(run)) { lock (codesByFile) codesByFile[key] = new(); continue; }
                var ins = RunInsights.Build(run);
                lock (codesByFile) codesByFile[key] = ins.Moments.Select(m => m.Code).ToHashSet();
                if (ins.Moments.Any(m => m.Code == code)) return ((RunTelemetry, RunInsights)?)(run, ins);
            }
            catch (Exception ex) { GD.PushWarning($"Rewind search skipped {Path.GetFileName(file)}: {ex.Message}"); }
        }
        return null;
    });

    // ---------------- names ----------------

    /// <summary>Short plain-English name of a moment code (plural, for "Show me" / headers).</summary>
    public static string CodeName(string code) => code switch
    {
        "MISS" => "Misses",
        "KILL" => "Kills",
        "DEATH" => "Deaths",
        "OVERFLICK" => "Overshot flicks",
        "UNDERFLICK" => "Short flicks",
        "SLOW_REACT" => "Slow reactions",
        "SLOW_START" => "Slow starts",
        "SLOW_REACQ" => "Slow re-aims",
        "SLOW_MICRO" => "Slow corrections",
        "NO_MICRO" => "Shots straight off the flick",
        "PREMATURE" => "Shots before you were on target",
        "HESITATE" => "Hesitations",
        "JITTER" => "Shaky aim",
        "CURVED" => "Curved flicks",
        "XHAIR_LOW" => "Crosshair too low",
        "XHAIR_WIDE" => "Crosshair off the angle",
        "TRACK_BEHIND" => "Trailing the target",
        "TRACK_OVERRUN" => "Running past the target",
        "RUN_GUN" => "Shots while moving",
        "CLUMSY_MOVE" => "Clumsy stops",
        "SPRAY_VERT" => "Spray drifting up",
        "SPRAY_HORIZ" => "Spray drifting sideways",
        "FLASH_SLOW" => "Late flash turns",
        _ => Pretty(code),
    };

    static string Pretty(string code)
    {
        var s = code.Replace('_', ' ').ToLowerInvariant();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static readonly Color MissColor = new(1f, 0.36f, 0.36f);
    public static readonly Color HitColor = new(0.36f, 0.92f, 0.55f);

    /// <summary>Marker colour of a moment code: kills green, deaths red, misses soft red, coach problems amber.</summary>
    public static Color CodeColor(string code) => code switch
    {
        "KILL" => UiTheme.Good,
        "DEATH" => UiTheme.Accent,
        "MISS" => new Color(1f, 0.5f, 0.45f),
        _ => UiTheme.Warn,
    };

    /// <summary>Screen direction of an 8-way flick sector (0 = right, counter-clockwise).</summary>
    public static string SectorName(int s) => (s & 7) switch
    {
        0 => "right", 1 => "up-right", 2 => "up", 3 => "up-left", 4 => "left", 5 => "down-left", 6 => "down", _ => "down-right",
    };

    /// <summary>Drill name for a mode key ("flashpeek:skye" → its base drill's name).</summary>
    public static string ModeName(string key)
    {
        if (CoachData.HasDrill(key)) return CoachData.DrillName(key);
        int c = key.IndexOf(':');
        return c > 0 && CoachData.HasDrill(key[..c]) ? CoachData.DrillName(key[..c]) : key;
    }
}
