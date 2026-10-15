using System.Globalization;
using System.Text.Json;
using ValTrainer.Analysis;
using ValTrainer.Core;

namespace ValTrainer.Game;

/// <summary>
/// Keeps the Rewind recordings folder (telemetry\*.vtt + their 3D .vtr sidecars) small and useful. Kept:
/// <list type="bullet">
/// <item>the <see cref="KeepRecent"/> newest runs;</item>
/// <item>older runs that still show a problem the coach currently reports — up to <see cref="KeepPerProblem"/> per
/// problem, newest first, so "Show me" keeps working;</item>
/// </list>
/// everything else is deleted: runs older than <see cref="MaxAgeDays"/> days, and examples of problems the coach no
/// longer reports (fixed). Which problems a run shows is cached in recordings.json next to the files, so each run is
/// analysed once. Runs on a worker after the coach profile is rebuilt; only for real (non-dev) data.
/// </summary>
public static class RecordingStore
{
    public const int KeepRecent = 20, KeepPerProblem = 3, MaxAgeDays = 30;
    const int MaxAnalysedPerPass = 25;   // older runs not yet in the index, analysed per pass (bounded work)
    static readonly HashSet<string> EventCodes = new() { "KILL", "DEATH", "MISS" };
    static readonly object gate = new();

    sealed class Index
    {
        public Dictionary<string, string[]> Runs { get; set; } = new();
    }

    public static string Dir => RunTelemetry.Dir;
    static string IndexFile(string dir) => Path.Combine(dir, "recordings.json");

    /// <summary>Applies the keep rules with the coach's current problem ids. Never throws.</summary>
    public static void Maintain(IEnumerable<string> activeProblems, string? dir = null)
    {
        dir ??= Dir;
        lock (gate)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var active = new HashSet<string>(activeProblems);
                var idx = LoadIndex(dir);
                var files = Directory.GetFiles(dir, "*.vtt").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
                var now = DateTime.Now;
                var perProblem = new Dictionary<string, int>();
                int analysed = 0, deleted = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    string f = files[i], name = Path.GetFileName(f);
                    bool tooOld = (now - When(f)).TotalDays > MaxAgeDays;
                    if (i < KeepRecent && !tooOld) { Codes(idx, f, ref analysed); continue; }
                    bool keep = false;
                    if (!tooOld && active.Count > 0)
                    {
                        var codes = Codes(idx, f, ref analysed);
                        if (codes == null) keep = true; // not analysed yet this pass: decide next time
                        else
                            foreach (var c in codes)
                            {
                                if (!active.Contains(c)) continue;
                                perProblem.TryGetValue(c, out int n);
                                if (n < KeepPerProblem) { perProblem[c] = n + 1; keep = true; }
                            }
                    }
                    if (keep) continue;
                    Delete(f);
                    idx.Runs.Remove(name);
                    deleted++;
                }
                // Forget index entries of files that are gone; drop 3D sidecars whose run is gone.
                foreach (var name in idx.Runs.Keys.ToList())
                    if (!File.Exists(Path.Combine(dir, name))) idx.Runs.Remove(name);
                foreach (var vtr in Directory.GetFiles(dir, "*.vtr"))
                    if (!File.Exists(Path.ChangeExtension(vtr, ".vtt"))) TryDelete(vtr);
                SaveIndex(dir, idx);
                if (deleted > 0) Log.Info($"[recordings] deleted {deleted} old run recording(s); {files.Count - deleted} kept");
            }
            catch (Exception e) { Log.Error($"[recordings] cleanup failed: {e.Message}"); }
        }
    }

    /// <summary>Kept runs known (from the index) to show problem <paramref name="code"/>, newest first — the examples
    /// kept for "Show me" beyond the most recent runs.</summary>
    public static List<string> FilesShowing(string code, string? dir = null)
    {
        dir ??= Dir;
        lock (gate)
        {
            try
            {
                return LoadIndex(dir).Runs.Where(r => r.Value.Contains(code)).Select(r => Path.Combine(dir, r.Key))
                    .Where(File.Exists).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
            }
            catch { return new List<string>(); }
        }
    }

    /// <summary>Number of recorded runs and their total size on disk (runs + 3D sidecars).</summary>
    public static (int Runs, long Bytes) Usage(string? dir = null)
    {
        dir ??= Dir;
        try
        {
            if (!Directory.Exists(dir)) return (0, 0);
            var di = new DirectoryInfo(dir);
            long bytes = di.GetFiles("*.vtt").Sum(f => f.Length) + di.GetFiles("*.vtr").Sum(f => f.Length);
            return (di.GetFiles("*.vtt").Length, bytes);
        }
        catch { return (0, 0); }
    }

    /// <summary>Deletes every recording (the stats and the coach's stored results are kept). Never throws.</summary>
    public static void DeleteAll(string? dir = null)
    {
        dir ??= Dir;
        lock (gate)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.vtt")) Delete(f);
                foreach (var f in Directory.GetFiles(dir, "*.vtr")) TryDelete(f);
                TryDelete(IndexFile(dir));
                Log.Info("[recordings] all run recordings deleted by the player");
            }
            catch (Exception e) { Log.Error($"[recordings] delete all failed: {e.Message}"); }
        }
    }

    /// <summary>Coach problem codes this run shows (cached); null when the run couldn't be analysed this pass.</summary>
    static string[]? Codes(Index idx, string file, ref int analysed)
    {
        string name = Path.GetFileName(file);
        if (idx.Runs.TryGetValue(name, out var c)) return c;
        if (analysed >= MaxAnalysedPerPass) return null;
        analysed++;
        string[] codes = Array.Empty<string>();
        try
        {
            if (RunTelemetry.Load(file) is { } run)
                codes = RunInsights.Build(run).Moments.Select(m => m.Code).Where(x => !EventCodes.Contains(x)).Distinct().ToArray();
        }
        catch { }
        idx.Runs[name] = codes;
        return codes;
    }

    /// <summary>When the run was recorded: from the file name (yyyyMMdd_HHmmss_mode.vtt), else the file time.</summary>
    static DateTime When(string file)
    {
        var n = Path.GetFileName(file);
        if (n.Length >= 15 && DateTime.TryParseExact(n[..15], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
        try { return File.GetLastWriteTime(file); } catch { return DateTime.Now; }
    }

    static void Delete(string vtt)
    {
        TryDelete(vtt);
        TryDelete(Replay.ReplayFile.SidecarOf(vtt));
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    static Index LoadIndex(string dir)
    {
        try
        {
            var f = IndexFile(dir);
            if (File.Exists(f)) return JsonSerializer.Deserialize<Index>(File.ReadAllText(f)) ?? new Index();
        }
        catch { }
        return new Index();
    }

    static void SaveIndex(string dir, Index idx)
    {
        // tmp + swap (not Paths.TryWriteAllText: a failure here mustn't show up as a stats-save warning)
        string f = IndexFile(dir), tmp = f + ".tmp";
        try { File.WriteAllText(tmp, JsonSerializer.Serialize(idx)); File.Move(tmp, f, overwrite: true); }
        catch { TryDelete(tmp); }
    }
}
