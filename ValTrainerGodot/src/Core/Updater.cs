using System.Text.Json;
using Godot;

namespace ValTrainer.Core;

/// <summary>
/// Automatic updates, Discord/Steam style: when <see cref="UpdateCheck"/> finds a newer release, its installer (installed
/// copies) or portable zip (portable copies) is downloaded in the background into the updates folder
/// (%LOCALAPPDATA%\ValTrainer\updates), resumably, and checked against the release's SHA256SUMS.txt. Then the menu
/// banner offers RESTART TO UPDATE; if the player doesn't click it, the update is applied on the next launch, before
/// the menu (never mid-drill). Installed copies run the release's Setup.exe silently (it closes ValTrainer, upgrades in
/// place and starts it again); portable copies swap ValTrainer.exe next to the running one and restart.
/// <para>Safety: nothing runs that doesn't match the SHA-256 published with the same release; a bad download is deleted
/// (at most 2 per version); an install that doesn't take is retried once, then the banner falls back to opening the
/// release page — never a loop, and the installed copy stays as it was. Skipped versions are never downloaded; "Check
/// for updates" off (or never turned on, see <see cref="AppSettings.AutoUpdates"/>) or "Download updates automatically"
/// off means nothing is downloaded. Copies that can't update
/// themselves (unknown install, read-only portable folder, all-users install for the automatic next-launch apply)
/// fall back to the banner / RESTART with a UAC note.</para>
/// <para>Dev: off in --dev runs unless <c>--update-allow-dev</c> together with <c>--data-dir</c> (the updates folder is
/// then &lt;data-dir&gt;\updates); <c>--update-source &lt;url&gt;</c> (fake release server), <c>--update-throttle
/// &lt;KB/s&gt;</c> (slow download) and <c>--update-apply-after &lt;s&gt;</c> (clicks RESTART TO UPDATE by itself) are
/// for the end-to-end tests.</para>
/// </summary>
public static partial class Updater
{
    public enum State
    {
        /// <summary>Automatic updates can't or mustn't run (see <see cref="Message"/>): banner only.</summary>
        Off,
        /// <summary>Nothing to do (up to date, or waiting for a retry).</summary>
        Idle,
        Downloading,
        Ready,
        /// <summary>Re-checking the file and starting the installer / swapping the exe; the app quits next.</summary>
        Applying,
        /// <summary>This version can't be installed automatically (bad downloads or failed installs): banner opens the release page.</summary>
        Failed,
    }

    const int MaxBadDownloads = 2, MaxAttempts = 2;
    /// <summary>An install started this recently may still be running: don't start another one at launch.</summary>
    const long RecentAttemptSec = 180;
    static readonly int[] RetryMinutes = { 1, 5, 15, 30, 60 };

    public static State Status { get; private set; } = State.Off;
    /// <summary>Why updates are off / why the last download failed (Settings → About).</summary>
    public static string? Message { get; private set; }
    /// <summary>Version being downloaded / ready / failed.</summary>
    public static string? Target => st.Version;
    /// <summary>Download progress 0..1 (Downloading).</summary>
    public static float Progress => job is { } j && j.Total > 0 ? Math.Clamp((float)Interlocked.Read(ref j.Received) / j.Total, 0f, 1f) : 0f;

    public static InstallKind Kind { get; private set; } = InstallKind.Unknown;
    /// <summary>All-users install: the installer needs administrator rights (Windows shows a UAC prompt).</summary>
    public static bool NeedsElevation => Kind == InstallKind.InstalledAdmin;

    static UpdaterState st = new();
    static string dir = "";
    static Job? job;
    static ApplyJob? applying;
    static DateTime retryAt = DateTime.MinValue;
    static int transientFailures;
    static bool recentAttempt, inited;
    static double devApplyAt = -1;
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    static AppSettings S => Main.I.Settings;

    /// <summary>Whether downloads may start (settings + build + dev rules); <see cref="Message"/> says why not.</summary>
    static bool Enabled => Status != State.Off && S.AutoUpdates && S.AutoDownloadUpdates;

    /// <summary>The version whose update is downloaded and verified (RESTART TO UPDATE), else null.</summary>
    public static string? ReadyVersion => Status == State.Ready && UpdateCheck.Offered == st.Version ? st.Version : null;

    /// <summary>Folder of the downloads and update.json (%LOCALAPPDATA%\ValTrainer\updates; dev: &lt;data-dir&gt;\updates).</summary>
    public static string Dir => dir;

    // ---------------------------------------------------------------- startup

    /// <summary>Main._Ready, after the settings are loaded and before <see cref="UpdateCheck.StartupCheck"/>: works out
    /// what kind of copy this is, finishes (or gives up on) an update started by a previous run and cleans up.</summary>
    public static void Init()
    {
        if (inited) return;
        inited = true;
        string? off = DetectKind();
        dir = ResolveDir();
        if (Kind == InstallKind.Portable) CleanPortableLeftovers();
        if (off == null && CmdLine.Dev && !(CmdLine.Has("--update-allow-dev") && Paths.DataDirOverridden))
            off = "off in --dev runs (test with --update-allow-dev and --data-dir)";
        if (off != null) { Set(State.Off, off); Log.Info($"Updater: {off}"); return; }

        st = LoadState();
        if (st.Version != null && SemVer.Compare(AppInfo.Version, st.Version) >= 0)
        {
            if (st.Attempts > 0) Log.Info($"Updater: updated to {AppInfo.Version} (from {st.From ?? "?"})");
            ResetState();
        }
        else if (st.Version != null && st.Attempts > 0)
        {
            long ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - st.LastAttemptUnix;
            recentAttempt = ago >= 0 && ago < RecentAttemptSec;
            if (!recentAttempt)
            {
                Log.Error($"Updater: the update to {st.Version} didn't install (attempt {st.Attempts} of {MaxAttempts}; see {Path.Combine(dir, InstallLogName(st.Version))})");
                if (st.Attempts >= MaxAttempts) GiveUp($"The update to {st.Version} didn't install");
            }
        }
        CleanOtherFiles();
        if (!st.GaveUp && st.Ready && !File.Exists(Path.Combine(dir, st.File ?? "")))
        {
            st.Ready = false; // deleted by hand / by a cleaner: download again
            SaveState();
        }
        Set(st.GaveUp ? State.Failed : st.Ready ? State.Ready : State.Idle, st.GaveUp ? st.LastError : null);
        if (CmdLine.DevAfter("--update-apply-after") is { } s && double.TryParse(s, out var sec)) devApplyAt = Math.Max(0, sec);
        Log.Info($"Updater: {KindName}, {(Status == State.Ready ? $"{st.Version} ready" : Status.ToString().ToLowerInvariant())}; updates folder {dir}");
    }

    static string ResolveDir()
    {
        if (Paths.DataDirOverridden) return Path.Combine(Paths.DataDir, "updates");
        string local = "";
        try { local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData); } catch { }
        return string.IsNullOrWhiteSpace(local) ? Path.Combine(Path.GetTempPath(), "ValTrainer-updates") : Path.Combine(local, "ValTrainer", "updates");
    }

    /// <summary>Should this launch install the downloaded update right away (before the menu)? Only for copies that can
    /// update without a prompt (per-user installs, writable portable copies), with automatic updates on, not for a
    /// skipped version, and not again while an install started moments ago may still be running.</summary>
    public static bool ShouldApplyAtStartup()
    {
        if (Status != State.Ready || recentAttempt || Kind is not (InstallKind.InstalledUser or InstallKind.Portable)) return false;
        if (!S.AutoUpdates || !S.AutoDownloadUpdates || st.Version == S.SkippedVersion) return false;
        if (DisplayServer.GetName() == "headless") return false;
        return SemVer.Compare(st.Version, AppInfo.Version) > 0;
    }

    // ---------------------------------------------------------------- reacting to the check / settings

    /// <summary>UpdateCheck has an answer (or the remembered one at startup): download the offered version if needed.</summary>
    public static void OnCheckResult()
    {
        if (Status is State.Off or State.Applying) return;
        string? target = UpdateCheck.Offered;
        if (target == null)
        {
            // nothing newer, or the player skipped it: drop a download of a version that's no longer wanted
            if (st.Version != null && (st.Version == S.SkippedVersion || SemVer.Compare(st.Version, AppInfo.Version) <= 0))
            {
                Cancel();
                ResetState();
                Set(State.Idle, null);
            }
            return;
        }
        if (st.Version != target)
        {
            if (!Enabled) return; // downloads off: nothing to track (an older ready update is never offered for it)
            // a newer release than the one we had (or the first one): start over for it
            Cancel();
            st = new UpdaterState { Version = target };
            SaveState();
            CleanOtherFiles();
            transientFailures = 0;
            retryAt = DateTime.MinValue;
            Set(State.Idle, null);
        }
        if (st.GaveUp) { Set(State.Failed, st.LastError); return; }
        if (st.Ready || job != null || !Enabled || DateTime.UtcNow < retryAt) return;
        StartDownload();
    }

    /// <summary>Settings → "Check for updates" / "Download updates automatically" changed, or a version was skipped.</summary>
    public static void SettingsChanged()
    {
        if (Status is State.Off or State.Applying) return;
        if (!S.AutoUpdates || !S.AutoDownloadUpdates || (st.Version != null && st.Version == S.SkippedVersion))
        {
            if (job != null)
            {
                Cancel();
                Set(State.Idle, null); // the partial file stays and resumes if it's turned back on
            }
            if (st.Version != null && st.Version == S.SkippedVersion) { ResetState(); Set(State.Idle, null); }
            return;
        }
        retryAt = DateTime.MinValue;
        OnCheckResult();
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Main thread, every frame (cheap): finishes downloads and applies, retries after failures.</summary>
    public static void Poll()
    {
        if (job is { Done: true } j) FinishDownload(j);
        else if (job is { } running && running.ProgressRev != running.SeenRev)
        {
            running.SeenRev = running.ProgressRev;
            UpdateCheck.Bump(); // progress for the banner / About (a few times a second)
        }
        if (applying is { Done: true } a) FinishApply(a);
        if (Status == State.Idle && job == null && retryAt != DateTime.MinValue && DateTime.UtcNow >= retryAt)
        {
            retryAt = DateTime.MinValue;
            OnCheckResult();
        }
        if (devApplyAt >= 0 && Status == State.Ready && clock.Elapsed.TotalSeconds >= devApplyAt)
        {
            devApplyAt = -1;
            Log.Info("Updater: --update-apply-after: RESTART TO UPDATE");
            RestartToUpdate();
        }
    }

    // ---------------------------------------------------------------- download

    static void StartDownload()
    {
        var assets = UpdateCheck.LatestAssets;
        bool portable = Kind == InstallKind.Portable;
        string? url = portable ? assets?.PortableUrl : assets?.SetupUrl;
        long size = portable ? assets?.PortableSize ?? 0 : assets?.SetupSize ?? 0;
        string? sums = assets?.SumsUrl;
        if (url == null || sums == null || !UpdateAssets.Trusted(url, AppInfo.GitHubRepo, UpdateCheck.Source)
            || !UpdateAssets.Trusted(sums, AppInfo.GitHubRepo, UpdateCheck.Source))
        {
            Set(State.Idle, $"No {(portable ? "portable zip" : "installer")} in release {st.Version}");
            return;
        }
        int throttle = int.TryParse(CmdLine.DevAfter("--update-throttle"), out var kb) && kb > 0 ? kb : 0;
        string asset = portable ? $"ValTrainer-{st.Version}-Portable.zip" : $"ValTrainer-{st.Version}-Setup.exe";
        job = new Job(st.Version!, url, size, sums, asset, portable, dir, AppInfo.Version, throttle * 1024L);
        Log.Info($"Updater: downloading {asset}{(throttle > 0 ? $" (throttled to {throttle} KB/s)" : "")}");
        Set(State.Downloading, null);
        job.Start();
    }

    static void Cancel()
    {
        if (job == null) return;
        job.Cancel();
        job = null;
    }

    static void FinishDownload(Job j)
    {
        job = null;
        if (j.Version != st.Version) return; // superseded
        switch (j.Outcome)
        {
            case Job.Result.Ok:
                st.Ready = true;
                st.File = j.ReadyFile;
                st.Sha256 = j.ReadySha256;
                st.LastError = null;
                SaveState();
                transientFailures = 0;
                Log.Info($"Updater: {st.Version} downloaded and verified ({st.File}, SHA-256 {st.Sha256})");
                Set(State.Ready, null);
                break;
            case Job.Result.BadFile:
                st.BadDownloads++;
                st.LastError = j.Error;
                Log.Error($"Updater: {j.Error} (bad download {st.BadDownloads} of {MaxBadDownloads}; the file was deleted)");
                if (st.BadDownloads >= MaxBadDownloads) GiveUp($"The download of {st.Version} was damaged");
                else { SaveState(); retryAt = DateTime.UtcNow.AddSeconds(5); Set(State.Idle, "Download damaged · trying again"); }
                break;
            case Job.Result.Cancelled:
                break;
            default:
                int m = RetryMinutes[Math.Min(transientFailures++, RetryMinutes.Length - 1)];
                retryAt = DateTime.UtcNow.AddMinutes(m);
                Log.Info($"Updater: download paused: {j.Error}; retrying in {m} min");
                Set(State.Idle, $"Download paused · retrying in {m} min");
                break;
        }
    }

    // ---------------------------------------------------------------- apply

    /// <summary>RESTART TO UPDATE (banner / About) and the next-launch apply: checks the file once more on a worker
    /// thread, then starts the installer (or swaps the portable exe) and quits. On failure the banner falls back to
    /// the release page.</summary>
    public static void RestartToUpdate()
    {
        if (Status != State.Ready || applying != null || st.File == null || st.Sha256 == null) return;
        Cancel();
        string file = Path.Combine(dir, st.File);
        applying = new ApplyJob(file, st.Sha256, Kind == InstallKind.Portable ? ExePath : null);
        Set(State.Applying, null);
        applying.Start();
    }

    static void FinishApply(ApplyJob a)
    {
        applying = null;
        if (!a.Ok)
        {
            st.Ready = false;
            st.BadDownloads++;
            TryDelete(a.File);
            Log.Error($"Updater: not installing {st.Version}: {a.Error}");
            if (st.BadDownloads >= MaxBadDownloads) GiveUp($"The update to {st.Version} was damaged");
            else { SaveState(); Set(State.Idle, "Update file damaged · downloading again"); retryAt = DateTime.UtcNow.AddSeconds(5); }
            return;
        }
        st.Attempts++;
        st.LastAttemptUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        st.From = AppInfo.Version;
        SaveState(); // before anything starts: the next launch must know an attempt was made
        string? err = Kind == InstallKind.Portable ? SwapPortable(a.StagedExe!) : LaunchInstaller(a.File);
        if (err != null)
        {
            Log.Error($"Updater: couldn't start the update to {st.Version}: {err}");
            st.LastError = err;
            if (st.Attempts >= MaxAttempts) GiveUp($"The update to {st.Version} couldn't start ({err})");
            else { SaveState(); Set(State.Ready, "Couldn't start the update"); }
            return;
        }
        Log.Info($"Updater: installing {st.Version} (attempt {st.Attempts}); quitting");
        Main.I.Quit(); // saves the settings
    }

    // ---------------------------------------------------------------- state

    static void GiveUp(string why)
    {
        st.GaveUp = true;
        st.Ready = false;
        st.LastError = why;
        SaveState();
        CleanOtherFiles(); // drops the downloaded file (the logs stay)
        Log.Error($"Updater: {why}; giving up on {st.Version}: the banner opens the release page instead");
        Set(State.Failed, why);
    }

    static void ResetState()
    {
        st = new UpdaterState();
        SaveState();
        CleanOtherFiles();
    }

    static void Set(State s, string? msg)
    {
        Status = s;
        Message = msg;
        UpdateCheck.Bump();
    }

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string StatePath => Path.Combine(dir, "update.json");

    static UpdaterState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var s = JsonSerializer.Deserialize<UpdaterState>(File.ReadAllText(StatePath), Json) ?? new UpdaterState();
                s.Version = SemVer.Normalize(s.Version);
                if (s.File != null && (s.File != Path.GetFileName(s.File) || s.File.Length == 0)) s.File = null; // names only
                if (s.File == null) s.Ready = false;
                return s;
            }
        }
        catch (Exception e) { Log.Error($"Updater: {StatePath} unreadable ({Log.Describe(e)}); starting over"); }
        return new UpdaterState();
    }

    static void SaveState()
    {
        if (st.Version == null && !File.Exists(StatePath)) return;
        Paths.TryWriteAllText(StatePath, JsonSerializer.Serialize(st, Json));
    }

    static string InstallLogName(string? v) => $"install-{v}.log";

    /// <summary>Deletes everything in the updates folder that doesn't belong to the current target (older versions,
    /// leftovers, the file of a version given up on); keeps the install logs of the current target.</summary>
    static void CleanOtherFiles()
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "update.json" };
            if (st.Version != null)
            {
                keep.Add(InstallLogName(st.Version));
                if (!st.GaveUp)
                {
                    if (st.File != null) keep.Add(st.File);
                    string asset = Kind == InstallKind.Portable ? $"ValTrainer-{st.Version}-Portable.zip" : $"ValTrainer-{st.Version}-Setup.exe";
                    keep.Add(asset + ".part");
                }
            }
            foreach (var f in Directory.EnumerateFiles(dir))
                if (!keep.Contains(Path.GetFileName(f))) TryDelete(f);
            if (st.Version == null) TryDelete(StatePath);
        }
        catch (Exception e) { Log.Error($"Updater: cleaning {dir} failed: {Log.Describe(e)}"); }
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) { Log.Info($"Updater: couldn't delete {path} ({Log.Describe(e)})"); }
    }

    /// <summary>What Settings → About shows under UPDATES.</summary>
    public static string? StatusLine => Status switch
    {
        State.Downloading => $"Downloading {st.Version}… {Progress * 100:0}%",
        State.Ready => $"{st.Version} is ready — restart to update",
        State.Applying => $"Installing {st.Version}…",
        State.Failed => "Update failed — open the release page",
        State.Idle when Message != null && UpdateCheck.Offered != null => Message,
        _ => null,
    };

    public static string KindName => Kind switch
    {
        InstallKind.InstalledUser => "installed for this user",
        InstallKind.InstalledAdmin => "installed for all users",
        InstallKind.Portable => "portable",
        InstallKind.Source => "run from the Godot editor",
        _ => "unknown copy",
    };
}

/// <summary>update.json in the updates folder: the update being downloaded / installed and how it went.</summary>
public sealed class UpdaterState
{
    public string? Version;         // target version
    public string? File;            // verified file in the updates folder (installer, or the portable ValTrainer.exe)
    public string? Sha256;          // its SHA-256 (re-checked right before it runs)
    public bool Ready;              // downloaded + verified
    public int BadDownloads;        // size / hash mismatches for this version
    public int Attempts;            // installs started for this version
    public long LastAttemptUnix;
    public string? From;            // version that started the last attempt
    public bool GaveUp;             // too many failures: banner only for this version
    public string? LastError;
}
