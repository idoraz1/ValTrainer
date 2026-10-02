using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ValTrainer.Core;

/// <summary>
/// "Is there a newer ValTrainer?" — asks GitHub's REST API for the latest published release
/// (GET /repos/{owner}/{repo}/releases/latest, which never returns drafts or prereleases) at most once a day, on a
/// worker thread with a 5 s timeout. Offline, rate-limited (HTTP 403/429), no releases yet (404) or any other
/// failure just means "no banner": nothing blocks or throws on the main thread.
/// <para>Skipped while <see cref="AppInfo.GitHubRepo"/> is the placeholder, when the player turned it off in Settings
/// and in --dev runs, unless <c>--dev --update-test &lt;version&gt;</c> fakes a release (no network).</para>
/// <para>Main thread: <see cref="StartupCheck"/> once, <see cref="Poll"/> every frame (cheap) to pick up the answer;
/// the UI watches <see cref="Revision"/>.</para>
/// </summary>
public static class UpdateCheck
{
    public enum State { Idle, Off, Checking, UpToDate, Available, Failed }

    static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public static State Status { get; private set; } = State.Idle;
    /// <summary>Why the last check failed / why checks are off (for Settings → About).</summary>
    public static string? Message { get; private set; }
    /// <summary>Newest release known (from this session's check or the last one saved in settings).</summary>
    public static string? Latest { get; private set; }
    public static string? LatestUrl { get; private set; }
    /// <summary>Bumps whenever any of the above changes, so screens can refresh.</summary>
    public static int Revision { get; private set; }
    /// <summary>The banner's ✕ (this session only; "Skip this version" is saved).</summary>
    public static bool BannerDismissed;

    static Task<Result>? pending;
    static string? fake;

    readonly record struct Result(bool Ok, string? Version, string? Url, string? Error);

    static AppSettings St => Main.I.Settings;

    /// <summary>True when <see cref="Latest"/> is newer than this build.</summary>
    public static bool NewerAvailable => Latest != null && SemVer.Compare(Latest, AppInfo.Version) > 0;

    /// <summary>The version the menu banner should offer, or null (nothing newer, skipped, dismissed or checks off).</summary>
    public static string? BannerVersion =>
        NewerAvailable && !BannerDismissed && (St.CheckUpdates || fake != null) && Latest != St.SkippedVersion ? Latest : null;

    /// <summary>Whether "Check now" can run (not for the placeholder repo, not in --dev without --update-test).</summary>
    public static bool CanCheck => Blocked() == null;

    /// <summary>Why update checks can't run in this build/session, or null when they can.</summary>
    static string? Blocked()
    {
        if (fake != null) return null;
        if (!AppInfo.HasRepo) return "No GitHub repository is set for this build";
        if (CmdLine.Dev) return "Off in --dev runs (test with --update-test <version>)";
        return null;
    }

    /// <summary>Called once from Main._Ready: shows the remembered result and checks again if the last answer is
    /// more than a day old.</summary>
    public static void StartupCheck()
    {
        fake = SemVer.Normalize(CmdLine.DevAfter("--update-test"));
        if (fake == null)
        {
            // what the last check found (the banner keeps showing until the player updates or skips it)
            Latest = SemVer.Normalize(St.LatestVersion);
            LatestUrl = St.LatestUrl;
        }
        if (!St.CheckUpdates) { Set(State.Off, "Turned off"); return; }
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool due = fake != null || St.LastUpdateCheck <= 0 || now - St.LastUpdateCheck >= (long)Interval.TotalSeconds
                   || St.LastUpdateCheck > now + 3600; // clock moved back: don't wait forever
        if (due) Begin();
        else Set(NewerAvailable ? State.Available : State.UpToDate, null);
    }

    /// <summary>Settings → "Check now": ignores the once-a-day limit (still never in --dev or for the placeholder repo).</summary>
    public static void CheckNow()
    {
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        BannerDismissed = false;
        Begin();
    }

    /// <summary>Settings toggle changed.</summary>
    public static void EnabledChanged()
    {
        if (!St.CheckUpdates) { Set(State.Off, "Turned off"); return; }
        if (Blocked() is { } why) { Set(State.Off, why); return; }
        if (Status is State.Off or State.Idle) StartupCheck();
    }

    public static void SkipVersion(string v)
    {
        St.SkippedVersion = v;
        St.Save();
        Revision++;
    }

    static void Begin()
    {
        if (pending != null) return;
        Set(State.Checking, null);
        if (fake != null)
        {
            // dev: pretend GitHub answered with this version (no network)
            pending = Task.FromResult(new Result(true, fake, AppInfo.HasRepo ? AppInfo.LatestReleaseUrl : null, null));
            return;
        }
        string ver = AppInfo.Version, api = AppInfo.LatestReleaseApi;
        pending = Task.Run(() => Fetch(ver, api));
    }

    /// <summary>Main thread, every frame: applies a finished check (saves the result).</summary>
    public static void Poll()
    {
        if (pending is not { IsCompleted: true } t) return;
        pending = null;
        var r = t.IsCompletedSuccessfully ? t.Result : new Result(false, null, null, t.Exception?.GetBaseException().Message ?? "cancelled");
        if (!r.Ok)
        {
            Log.Info($"Update check failed: {r.Error}");
            Set(State.Failed, r.Error);
            return;
        }
        Latest = r.Version;
        LatestUrl = r.Url;
        if (fake == null)
        {
            St.LastUpdateCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            St.LatestVersion = r.Version;
            St.LatestUrl = r.Url;
            St.Save(); // no-op in --dev (AppSettings.ReadOnly)
        }
        Log.Info($"Update check: latest release {r.Version ?? "none"}, this is {AppInfo.Version}");
        Set(NewerAvailable ? State.Available : State.UpToDate, null);
    }

    static void Set(State s, string? msg)
    {
        Status = s;
        Message = msg;
        Revision++;
    }

    /// <summary>Worker thread. Never throws.</summary>
    static async Task<Result> Fetch(string version, string api)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ValTrainer/{version}");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await http.GetAsync(api).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return new Result(true, null, null, null); // no release yet
            if (!resp.IsSuccessStatusCode)
            {
                int code = (int)resp.StatusCode;
                return new Result(false, null, null, code is 403 or 429 ? $"GitHub rate limit reached (HTTP {code}), try again later" : $"GitHub answered HTTP {code}");
            }
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
            if (Flag("draft") || Flag("prerelease")) return new Result(true, null, null, null);
            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            var v = SemVer.Normalize(tag);
            if (v == null) return new Result(false, null, null, $"Latest release tag \"{tag}\" isn't a version number");
            if (url == null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = null;
            return new Result(true, v, url, null);
        }
        catch (TaskCanceledException) { return new Result(false, null, null, "No answer from GitHub within 5 s (offline?)"); }
        catch (HttpRequestException e) { return new Result(false, null, null, $"Can't reach GitHub ({e.Message})"); }
        catch (Exception e) { return new Result(false, null, null, e.Message); }
    }
}
