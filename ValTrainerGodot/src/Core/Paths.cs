using System.Text;
using Godot;

namespace ValTrainer.Core;

/// <summary>
/// Where ValTrainer keeps its files and where it looks for Valorant's. Everything here survives odd PCs: missing or
/// redirected (OneDrive) AppData, read-only folders and non-ASCII user names (all paths are .NET UTF-16 strings).
/// Dev-only overrides (need --dev): <c>--data-dir &lt;path&gt;</c> for settings/stats/telemetry and
/// <c>--valorant-dir &lt;path&gt;</c> for Valorant's Saved\Config folder.
/// </summary>
public static class Paths
{
    static string? dataDir;

    /// <summary>Settings, stats and telemetry (normally %APPDATA%\ValTrainer). Never throws; created on first use.</summary>
    public static string DataDir => dataDir ??= ResolveDataDir();

    /// <summary>True when a dev run redirected <see cref="DataDir"/> with --data-dir.</summary>
    public static bool DataDirOverridden { get; private set; }

    /// <summary>Set when the normal data folder couldn't be created and a fallback is used (or nothing worked).</summary>
    public static string? DataDirProblem { get; private set; }

    /// <summary>The last failed save (settings, stats or telemetry), for the UI; null when the last save worked.</summary>
    public static string? LastWriteError { get; private set; }

    static string ResolveDataDir()
    {
        var over = CmdLine.DevAfter("--data-dir");
        if (!string.IsNullOrWhiteSpace(over))
        {
            DataDirOverridden = true;
            var full = Path.GetFullPath(over);
            if (!TryCreate(full, out var err)) DataDirProblem = $"Can't create {full}: {err}";
            return full;
        }

        // Roaming AppData is the normal home. GetFolderPath returns "" when Windows can't resolve the folder (e.g. a
        // redirected AppData that's offline); then fall back to local AppData, Godot's user dir and finally %TEMP%.
        var candidates = new List<string>();
        void Add(Func<string> f, bool appendName = true)
        {
            try { var b = f(); if (!string.IsNullOrWhiteSpace(b)) candidates.Add(appendName ? Path.Combine(b, "ValTrainer") : b); } catch { }
        }
        Add(() => System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        Add(() => System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        Add(() => OS.GetUserDataDir(), appendName: false);
        Add(Path.GetTempPath);

        string? firstErr = null;
        foreach (var dir in candidates)
        {
            if (TryCreate(dir, out var err))
            {
                if (dir != candidates[0])
                {
                    DataDirProblem = $"Couldn't use {candidates[0]} ({firstErr}); saving to {dir} instead";
                    Log.Error(DataDirProblem);
                }
                return dir;
            }
            firstErr ??= err;
        }
        DataDirProblem = $"No writable data folder ({firstErr}); settings and stats won't be saved";
        Log.Error(DataDirProblem);
        return candidates.Count > 0 ? candidates[0] : Path.Combine(Path.GetTempPath(), "ValTrainer");
    }

    static bool TryCreate(string dir, out string? error)
    {
        try { Directory.CreateDirectory(dir); error = null; return true; }
        catch (Exception e) { error = e.Message; return false; }
    }

    /// <summary>A sub-folder of <see cref="DataDir"/>, created if possible (never throws).</summary>
    public static string DataSubDir(string name)
    {
        var d = Path.Combine(DataDir, name);
        try { Directory.CreateDirectory(d); } catch { /* the write that needs it reports the error */ }
        return d;
    }

    /// <summary>Writes a file so a crash or full disk mid-write can't leave a half-written (corrupt) file behind:
    /// write "name.tmp", then swap it in. Returns false (and logs, and sets <see cref="LastWriteError"/>) on failure.</summary>
    public static bool TryWriteAllText(string path, string text)
    {
        string tmp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
            LastWriteError = null;
            return true;
        }
        catch (Exception e)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            LastWriteError = $"Couldn't save {Path.GetFileName(path)}: {e.Message}";
            Log.Error(LastWriteError);
            return false;
        }
    }

    /// <summary>True when a load failed because of the file's contents (bad JSON, wrong types, binary junk), as opposed
    /// to not being able to open it (locked, no permission) — only then is it safe to replace the file.</summary>
    public static bool IsCorruptData(Exception e) =>
        e is System.Text.Json.JsonException or FormatException or InvalidOperationException or NotSupportedException or ArgumentException
        or OverflowException or DecoderFallbackException;

    /// <summary>Keeps a copy of a file that failed to load ("stats.json.corrupt") before it gets overwritten.</summary>
    public static void KeepCorrupt(string path, Exception e)
    {
        Log.Error($"{path} couldn't be read ({e.Message}); starting fresh, the old file is kept as {Path.GetFileName(path)}.corrupt");
        try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
    }

    /// <summary>For self-tests: use this folder for data (null = back to the normal one).</summary>
    internal static void SetDataDirForTests(string? dir)
    {
        DataDirProblem = null;
        LastWriteError = null;
        dataDir = dir;
        if (dir != null) TryCreate(dir, out _);
    }

    // ---------------- Valorant ----------------

    static string? valorantOverride;
    internal static void SetValorantDirForTests(string? dir) => valorantOverride = dir;

    /// <summary>Valorant's per-user config root: %LOCALAPPDATA%\VALORANT\Saved\Config (dev: --valorant-dir).
    /// Empty when Windows can't resolve local AppData (treated as "Valorant not found").</summary>
    public static string ValorantConfigDir
    {
        get
        {
            if (valorantOverride != null) return valorantOverride;
            var over = CmdLine.DevAfter("--valorant-dir");
            if (!string.IsNullOrWhiteSpace(over)) return Path.GetFullPath(over);
            string local;
            try { local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData); } catch { local = ""; }
            return string.IsNullOrWhiteSpace(local) ? "" : Path.Combine(local, "VALORANT", "Saved", "Config");
        }
    }

    // ---------------- logs ----------------

    /// <summary>Folder of Godot's log files (godot.log + the last few rotated ones), e.g.
    /// %APPDATA%\Godot\app_userdata\ValTrainer\logs. Main thread only (uses the Godot API).</summary>
    public static string LogDir
    {
        get
        {
            try
            {
                var setting = ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log").AsString();
                return ProjectSettings.GlobalizePath(setting.GetBaseDir()).Replace('/', '\\');
            }
            catch { return Path.Combine(OS.GetUserDataDir(), "logs"); }
        }
    }

    /// <summary>The current log file (godot.log in <see cref="LogDir"/>).</summary>
    public static string LogFile => Path.Combine(LogDir, "godot.log");
}
