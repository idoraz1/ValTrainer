using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Progression;

/// <summary>How <see cref="ProgressStore.Load"/> went.</summary>
public enum ProgressLoad
{
    /// <summary>No progress.json: first 1.19 launch (the Progress core runs the retro credit).</summary>
    Missing,
    Loaded,
    /// <summary>Corrupt: kept as progress.json.corrupt; start fresh and re-derive (retro).</summary>
    Corrupt,
    /// <summary>Exists but couldn't be opened (locked, no permission): run in memory, never write this session.</summary>
    Unreadable,
    /// <summary>Written by a newer build (MinReader &gt; <see cref="ProgressStore.CurrentVersion"/>): known fields loaded, never written.</summary>
    Newer,
}

/// <summary>
/// progress.json load / save (the StatsStore pattern, progression-design §11). <b>Dev runs never write</b>: saving needs
/// <c>Main.SavesData</c> (normal play, or <c>--dev --data-dir … --write-data</c>), like stats.json and settings.json.
/// <para><see cref="Progress"/> saves after the startup credit and after each run, Lock-In, coach sample, reroll, seen
/// change and the reveal. A corrupt file is kept as progress.json.corrupt and re-derived from the history (retro credit).</para>
/// </summary>
public static class ProgressStore
{
    public const int CurrentVersion = 1;
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public static string FilePath => Path.Combine(Paths.DataDir, "progress.json");

    /// <summary>Set when the file was unreadable or newer: nothing is written this session.</summary>
    public static bool ReadOnly { get; private set; }

    /// <summary>Whether <see cref="Save"/> would write now.</summary>
    public static bool CanWrite => !ReadOnly && Main.I != null && Main.I.SavesData;

    public static ProgressFile Load(out ProgressLoad result)
    {
        string path = FilePath;
        try
        {
            if (!File.Exists(path)) { result = ProgressLoad.Missing; return new ProgressFile { CreatedWith = AppInfo.Version }; }
            var f = Parse(File.ReadAllText(path));
            if (f.MinReader > CurrentVersion)
            {
                ReadOnly = true;
                Log.Info($"[progress] {path} was written by a newer ValTrainer (format {f.Version}); reading it without saving");
                result = ProgressLoad.Newer;
                return f;
            }
            result = ProgressLoad.Loaded;
            return f;
        }
        catch (Exception e) when (Paths.IsCorruptData(e))
        {
            Paths.KeepCorrupt(path, e);
            result = ProgressLoad.Corrupt;
        }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); progress runs in memory and the file is left alone");
            ReadOnly = true;
            result = ProgressLoad.Unreadable;
        }
        return new ProgressFile { CreatedWith = AppInfo.Version };
    }

    /// <summary>Deserialises and repairs nulls (a hand-edited file never crashes). Throws on malformed JSON (corrupt).</summary>
    public static ProgressFile Parse(string json)
    {
        var f = JsonSerializer.Deserialize<ProgressFile>(json, Json) ?? new ProgressFile();
        f.Counters ??= new();
        f.Unlocked ??= new();
        f.Unseen ??= new();
        f.Salt ??= Guid.NewGuid().ToString("N");
        return f;
    }

    public static string Serialize(ProgressFile f) => JsonSerializer.Serialize(f, Json);

    /// <summary>Atomic write (tmp + move). Never throws; a no-op unless <see cref="CanWrite"/>.</summary>
    public static void Save(ProgressFile f)
    {
        if (!CanWrite) return;
        f.Version = CurrentVersion;
        try { Paths.TryWriteAllText(FilePath, Serialize(f)); }
        catch (Exception e) { Log.Error($"Saving progress failed: {e.Message}"); }
    }
}
