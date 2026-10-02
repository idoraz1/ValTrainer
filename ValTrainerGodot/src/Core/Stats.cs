using System.Text.Json;

namespace ValTrainer.Core;

public sealed class RunRecord
{
    public string Mode = "";
    public DateTime When;
    public int Score;
    public float Accuracy;      // 0..1, -1 when not applicable
    public int Hits, Shots, Kills;
    public float AvgKillMs;     // avg time per kill / reaction time, 0 when not applicable
    public float HeadshotPct;   // 0..1, -1 when not applicable
    public float Sens;
    public int Tier = -1;       // difficulty tier played (-1 = old Easy/Normal/Hard build)
    public int Badge = -1;      // rank badge earned (-1 = below Rookie / not rated)
    public string Map = "";
    /// <summary>Aim-coach metrics for this run (see Analysis.Coach.Summarize).</summary>
    public Dictionary<string, float>? Metrics;
    /// <summary>Saved telemetry file (frames/shots/events), if kept.</summary>
    public string? TelemetryFile;
}

public sealed class StatsStore
{
    public List<RunRecord> Runs = new();

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Paths.DataDir, "stats.json");

    public static StatsStore Load()
    {
        string path = FilePath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<StatsStore>(File.ReadAllText(path), Json) ?? new StatsStore();
                s.Runs ??= new();
                s.Runs.RemoveAll(r => r == null);
                return s;
            }
        }
        catch (Exception e) when (Paths.IsCorruptData(e)) { Paths.KeepCorrupt(path, e); }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); this session's runs won't be saved so the file isn't overwritten");
            return new StatsStore { readFailed = true };
        }
        return new StatsStore();
    }

    /// <summary>stats.json exists but couldn't be opened: never overwrite it (that would wipe the history).</summary>
    [System.Text.Json.Serialization.JsonIgnore] bool readFailed;

    /// <summary>Never throws: an unwritable data folder is logged and reported via <see cref="Paths.LastWriteError"/>.</summary>
    public void Save()
    {
        if (readFailed) return;
        try { Paths.TryWriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Error($"Saving stats failed: {e.Message}"); }
    }

    public IEnumerable<RunRecord> For(string mode) => Runs.Where(r => r.Mode == mode);

    public int Best(string mode) => For(mode).Select(r => r.Score).DefaultIfEmpty(-1).Max();

    /// <summary>Personal best at a specific tier (scores across tiers aren't comparable).</summary>
    public int Best(string mode, int tier) => For(mode).Where(r => r.Tier == tier).Select(r => r.Score).DefaultIfEmpty(-1).Max();

    /// <summary>Best rank badge ever earned in this mode.</summary>
    public int BestBadge(string mode) => For(mode).Select(r => r.Badge).DefaultIfEmpty(-1).Max();
}
