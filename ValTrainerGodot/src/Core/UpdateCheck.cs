using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ValTrainer.Core;

/// <summary>
/// "Is there a newer ValTrainer?" — asks GitHub's REST API for the latest published release
/// (GET /repos/{owner}/{repo}/releases/latest, which never returns drafts or prereleases) at startup when the last
/// answer is more than 6 hours old and again every 6 hours while the app runs (≤ 4 requests a day; GitHub allows 60 an
/// hour), on a worker thread with a 5 s timeout. Offline, rate-limited (HTTP 403/429), no releases yet (404) or any
/// other failure just means "no banner" (and another try in an hour): nothing blocks or throws on the main thread.
/// The answer includes the release's downloads, which <see cref="Updater"/> fetches in the background.
/// <para>Skipped while <see cref="AppInfo.GitHubRepo"/> is the placeholder, when the player turned it off in Settings
/// and in --dev runs, unless <c>--dev --update-test &lt;version&gt;</c> fakes a release (no network) or
/// <c>--dev --update-source &lt;url&gt;</c> asks a local fake release server instead of GitHub (updater tests).</para>
/// <para>Main thread: <see cref="StartupCheck"/> once, <see cref="Poll"/> every frame (cheap) to pick up the answer;
/// the UI watches <see cref="Revision"/>.</para>
/// </summary>
public static class UpdateCheck
{
    public enum State { Idle, Off, Checking, UpToDate, Available, Failed }

    static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    public static State Status { get; private set; } = State.Idle;
    /// <summary>Why the last check failed / why checks are off (for Settings → About).</summary>
    public static string? Message { get; private set; }
    /// <summary>Newest release known (from this session's check or the last one saved in settings).</summary>
    public static string? Latest { get; private set; }
    public static string? LatestUrl { get; private set; }
    /// <summary>The downloads of <see cref="Latest"/> (null when unknown, e.g. --update-test).</summary>
    public static UpdateAssets? LatestAssets { get; private set; }
    /// <summary>Bumps whenever any of the above changes (or the <see cref="Updater"/> state does), so screens can refresh.</summary>
    public static int Revision { get; private set; }
    /// <summary>The banner's ✕ / LATER (this session only; "Skip this version" is saved).</summary>
    public static bool BannerDismissed;

    static Task<Result>? pending;
    static string? fake;
    /// <summary>Dev: --update-source URL that replaces GitHub's API (a local fake release).</summary>
    public static string? Source { get; private set; }
    static DateTime nextCheckUtc = DateTime.MaxValue;

    readonly record struct Result(bool Ok, string? Version, string? Url, UpdateAssets? Assets, string? Error);

    static AppSettings St => Main.I.Settings;

    /// <summary>True when <see cref="Latest"/> is newer than this build.</summary>
    public static bool NewerAvailable => Latest != null && SemVer.Compare(Latest, AppInfo.Version) > 0;

    /// <summary>The newer version that wasn't skipped (checks on), whether or not the banner was dismissed.</summary>
    public static string? Offered =>
        NewerAvailable && (St.CheckUpdates || fake != null) && Latest != St.SkippedVersion ? Latest : null;

    /// <summary>The version the menu banner should offer, or null (nothing newer, skipped, dismissed or checks off).</summary>
    public static string? BannerVersion => BannerDismissed ? null : Offered;

    /// <summary>Whether "Check now" can run (not for the placeholder repo, not in --dev without --update-test).</summary>
    public static bool CanCheck => Blocked() == null;

    /// <summary>Why update checks can't run in this build/session, or null when they can.</summary>
    static string? Blocked()
    {
        if (fake != null || Source != null) return null;
        if (!AppInfo.HasRepo) return "No GitHub repository is set for this build";
        if (CmdLine.Dev) return "Off in --dev runs (test with --update-test <version> or --update-source <url>)";
        return null;
    }

    /// <summary>Called once from Main._Ready: shows the remembered result and checks again if the last answer is
    /// more than 6 hours old.</summary>
    public static void StartupCheck()
    {
        fake = SemVer.Normalize(CmdLine.DevAfter("--update-test"));
        var src = CmdLine.DevAfter("--update-source");
        Source = src != null && Uri.TryCreate(src, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? src : null;
        if (fake == null)
        {
            // what the last check found (the banner keeps showing until the player updates or skips it)
            Latest = SemVer.Normalize(St.LatestVersion);
            LatestUrl = St.LatestUrl;
            LatestAssets = St.LatestAssets;
        }
        if (!St.CheckUpdates) { Set(State.Off, "Turned off"); return; }
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool due = fake != null || Source != null || St.LastUpdateCheck <= 0 || now - St.LastUpdateCheck >= (long)Interval.TotalSeconds
                   || St.LastUpdateCheck > now + 3600; // clock moved back: don't wait forever
        if (due) Begin();
        else
        {
            nextCheckUtc = DateTimeOffset.FromUnixTimeSeconds(St.LastUpdateCheck).UtcDateTime + Interval;
            Set(NewerAvailable ? State.Available : State.UpToDate, null);
            Updater.OnCheckResult();
        }
    }

    /// <summary>Settings → "Check now": ignores the 6-hour limit (still never in --dev or for the placeholder repo).</summary>
    public static void CheckNow()
    {
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        BannerDismissed = false;
        Begin();
    }

    /// <summary>Settings toggle changed.</summary>
    public static void EnabledChanged()
    {
        Updater.SettingsChanged();
        if (!St.CheckUpdates) { Set(State.Off, "Turned off"); return; }
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        if (Status is State.Off or State.Idle) StartupCheck();
    }

    public static void SkipVersion(string v)
    {
        St.SkippedVersion = v;
        St.Save();
        Updater.SettingsChanged();
        Revision++;
    }

    /// <summary>Lets the <see cref="Updater"/> tell the screens that its state changed.</summary>
    internal static void Bump() => Revision++;

    static void Begin()
    {
        if (pending != null) return;
        nextCheckUtc = DateTime.MaxValue;
        Set(State.Checking, null);
        if (fake != null)
        {
            // dev: pretend GitHub answered with this version (no network, nothing to download)
            pending = Task.FromResult(new Result(true, fake, AppInfo.HasRepo ? AppInfo.LatestReleaseUrl : null, null, null));
            return;
        }
        string ver = AppInfo.Version, api = Source ?? AppInfo.LatestReleaseApi, repo = AppInfo.GitHubRepo;
        bool local = Source != null;
        pending = Task.Run(() => Fetch(ver, api, repo, local));
    }

    /// <summary>Main thread, every frame: applies a finished check (saves the result) and starts the 6-hourly re-check.</summary>
    public static void Poll()
    {
        if (pending == null)
        {
            if (DateTime.UtcNow >= nextCheckUtc && St.CheckUpdates && Blocked() == null && fake == null) Begin();
            return;
        }
        if (pending is not { IsCompleted: true } t) return;
        pending = null;
        var r = t.IsCompletedSuccessfully ? t.Result : new Result(false, null, null, null, t.Exception?.GetBaseException().Message ?? "cancelled");
        if (!r.Ok)
        {
            Log.Info($"Update check failed: {r.Error}");
            if (fake == null) nextCheckUtc = DateTime.UtcNow + RetryAfterFailure;
            Set(State.Failed, r.Error);
            Updater.OnCheckResult(); // a download that was already known can still go on
            return;
        }
        Latest = r.Version;
        LatestUrl = r.Url;
        LatestAssets = r.Assets;
        if (fake == null)
        {
            nextCheckUtc = DateTime.UtcNow + Interval;
            St.LastUpdateCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            St.LatestVersion = r.Version;
            St.LatestUrl = r.Url;
            St.LatestAssets = r.Assets;
            St.Save(); // no-op in --dev (AppSettings.ReadOnly) unless --data-dir … --write-data
        }
        Log.Info($"Update check: latest release {r.Version ?? "none"}, this is {AppInfo.Version}");
        Set(NewerAvailable ? State.Available : State.UpToDate, null);
        Updater.OnCheckResult();
    }

    static void Set(State s, string? msg)
    {
        Status = s;
        Message = msg;
        Revision++;
    }

    /// <summary>Worker thread. Never throws.</summary>
    static async Task<Result> Fetch(string version, string api, string repo, bool local)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ValTrainer/{version}");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await http.GetAsync(api).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return new Result(true, null, null, null, null); // no release yet
            if (!resp.IsSuccessStatusCode)
            {
                int code = (int)resp.StatusCode;
                return new Result(false, null, null, null, code is 403 or 429 ? $"GitHub rate limit reached (HTTP {code}), try again later" : $"GitHub answered HTTP {code}");
            }
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
            if (Flag("draft") || Flag("prerelease")) return new Result(true, null, null, null, null);
            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            var v = SemVer.Normalize(tag);
            if (v == null) return new Result(false, null, null, null, $"Latest release tag \"{tag}\" isn't a version number");
            if (url == null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = null;
            return new Result(true, v, url, ReadAssets(root, v, repo, local ? api : null), null);
        }
        catch (TaskCanceledException) { return new Result(false, null, null, null, "No answer from GitHub within 5 s (offline?)"); }
        catch (HttpRequestException e) { return new Result(false, null, null, null, $"Can't reach GitHub ({e.Message})"); }
        catch (Exception e) { return new Result(false, null, null, null, e.Message); }
    }

    /// <summary>The release's installer, portable zip and SHA256SUMS.txt. Only download links of this project's own
    /// GitHub releases are accepted (a dev --update-source may point at its own host).</summary>
    static UpdateAssets? ReadAssets(JsonElement root, string version, string repo, string? localSource)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var a = new UpdateAssets();
        foreach (var e in assets.EnumerateArray())
        {
            string? name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? dl = e.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            long size = e.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
            if (name == null || dl == null || !UpdateAssets.Trusted(dl, repo, localSource)) continue;
            if (name.Equals($"ValTrainer-{version}-Setup.exe", StringComparison.OrdinalIgnoreCase)) { a.SetupUrl = dl; a.SetupSize = size; }
            else if (name.Equals($"ValTrainer-{version}-Portable.zip", StringComparison.OrdinalIgnoreCase)) { a.PortableUrl = dl; a.PortableSize = size; }
            else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) a.SumsUrl = dl;
        }
        return a.SumsUrl != null && (a.SetupUrl != null || a.PortableUrl != null) ? a : null;
    }
}

/// <summary>Download links of a release (saved in settings so a download can resume after a restart without asking
/// GitHub again).</summary>
public sealed class UpdateAssets
{
    public string? SetupUrl;
    public long SetupSize;
    public string? PortableUrl;
    public long PortableSize;
    public string? SumsUrl;

    /// <summary>https://github.com/&lt;repo&gt;/releases/download/… only (GitHub then redirects to its download host);
    /// a dev --update-source may also serve its files from its own scheme + host + port.</summary>
    public static bool Trusted(string url, string repo, string? localSource)
    {
        if (url.StartsWith($"https://github.com/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return true;
        if (localSource == null || !Uri.TryCreate(localSource, UriKind.Absolute, out var src) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme == src.Scheme && string.Equals(u.Host, src.Host, StringComparison.OrdinalIgnoreCase) && u.Port == src.Port;
    }
}
