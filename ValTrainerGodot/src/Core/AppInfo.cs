using Godot;

namespace ValTrainer.Core;

/// <summary>
/// Who and what this build is: version (single source of truth: <c>application/config/version</c> in project.godot,
/// which also feeds the exe's file/product version at export) and where it lives on GitHub.
/// </summary>
public static class AppInfo
{
    public const string Name = "ValTrainer";
    public const string Publisher = "ValTrainer contributors";

    /// <summary>"owner/repo" on GitHub. Update checks, "Report a bug" and the installer's links stay off while this
    /// still holds the "OWNER" placeholder (tools\build-release.ps1 reads it from here too).</summary>
    public const string GitHubRepo = "idoraz1/ValTrainer";

    /// <summary>False while <see cref="GitHubRepo"/> is the placeholder: everything that would link there is hidden.</summary>
    public static bool HasRepo =>
        !GitHubRepo.Contains("OWNER", StringComparison.Ordinal) && GitHubRepo.Count(c => c == '/') == 1
        && !GitHubRepo.StartsWith('/') && !GitHubRepo.EndsWith('/');

    static string? version;

    /// <summary>SemVer of this build, e.g. "1.0.0" or "1.1.0-beta.1" (first read on the main thread, cached).</summary>
    public static string Version => version ??= ReadVersion();

    static string ReadVersion()
    {
        try
        {
            var v = ProjectSettings.GetSetting("application/config/version", "").AsString().Trim();
            return v.Length > 0 ? v : "0.0.0";
        }
        catch { return "0.0.0"; }
    }

    public static string RepoUrl => $"https://github.com/{GitHubRepo}";
    public static string ReleasesUrl => RepoUrl + "/releases";
    public static string LatestReleaseUrl => RepoUrl + "/releases/latest";
    /// <summary>The issue-template chooser (bug report / feature request).</summary>
    public static string NewIssueUrl => RepoUrl + "/issues/new/choose";
    /// <summary>The project's website (downloads).</summary>
    public const string Website = "https://valtrainer.github.io/";
    /// <summary>GitHub REST: newest published release (drafts and prereleases are excluded by GitHub).</summary>
    public static string LatestReleaseApi => $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

    /// <summary>Opens a GitHub page of this project in the browser. Only https://github.com/ links are ever opened
    /// (release URLs come from the network); returns false for anything else or while the repo is the placeholder.</summary>
    public static bool OpenGitHub(string? url)
    {
        if (!HasRepo || url == null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return false;
        try { return OS.ShellOpen(url) == Error.Ok; }
        catch (Exception e) { Log.Error($"Couldn't open {url}: {e.Message}"); return false; }
    }

    /// <summary>Tip jars (Support panel, Settings → About). Opened in the browser only when the player clicks.</summary>
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/idoraz1";
    public const string KofiUrl = "https://ko-fi.com/idoraz1";
    /// <summary>The ValTrainer Discord (release news, bug reports, suggestions, help, voice chats by rank).</summary>
    public const string DiscordUrl = "https://discord.gg/Y6UKUX89C8";

    /// <summary>Opens one of the support pages or the Discord invite in the browser (nothing else; nothing is sent by
    /// ValTrainer itself).</summary>
    public static bool OpenSupport(string url)
    {
        if (url != BuyMeACoffeeUrl && url != KofiUrl && url != DiscordUrl) return false;
        try { return OS.ShellOpen(url) == Error.Ok; }
        catch (Exception e) { Log.Error($"Couldn't open {url}: {e.Message}"); return false; }
    }

    /// <summary>Shows a local folder in Explorer (created first if missing).</summary>
    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            OS.ShellShowInFileManager(dir, true);
        }
        catch (Exception e) { Log.Error($"Couldn't open {dir}: {e.Message}"); }
    }
}

/// <summary>
/// Semantic Versioning 2.0 precedence (https://semver.org/#spec-item-11): major.minor.patch numerically, then a
/// release beats any prerelease of it (1.1.0 &gt; 1.1.0-rc.1), prerelease identifiers compared one by one (numeric
/// ones numerically and below alphanumeric ones; a longer list wins a tie). Build metadata (+abc) is ignored.
/// A leading "v" (git tags) is accepted.
/// </summary>
public readonly struct SemVer : IComparable<SemVer>
{
    public readonly int Major, Minor, Patch;
    readonly string[]? pre;
    /// <summary>Prerelease identifiers ("beta", "1" for 1.1.0-beta.1); empty for a release.</summary>
    public string[] Pre => pre ?? Array.Empty<string>();

    public SemVer(int major, int minor, int patch, string[]? pre = null)
    {
        Major = major; Minor = minor; Patch = patch; this.pre = pre;
    }

    public bool IsPrerelease => Pre.Length > 0;

    public static bool TryParse(string? s, out SemVer v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string[] pre = Array.Empty<string>();
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.');
            s = s[..dash];
            if (pre.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
        }
        var core = s.Split('.');
        if (core.Length is < 1 or > 3) return false;
        var n = new int[3];
        for (int i = 0; i < core.Length; i++)
            if (core[i].Length == 0 || !core[i].All(char.IsAsciiDigit) || !int.TryParse(core[i], out n[i])) return false;
        v = new SemVer(n[0], n[1], n[2], pre);
        return true;
    }

    public int CompareTo(SemVer o)
    {
        int c = Major.CompareTo(o.Major);
        if (c == 0) c = Minor.CompareTo(o.Minor);
        if (c == 0) c = Patch.CompareTo(o.Patch);
        if (c != 0) return c;
        var a = Pre;
        var b = o.Pre;
        if (a.Length == 0 || b.Length == 0) return b.Length.CompareTo(a.Length); // no prerelease ranks higher
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            if (an && bn)
            {
                // compare as big numbers without overflow: longer (without leading zeros) is bigger
                string x = a[i].TrimStart('0'), y = b[i].TrimStart('0');
                c = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
            }
            else if (an != bn) c = an ? -1 : 1;
            else c = string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return Math.Sign(c);
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Compares two version strings; unparseable ones sort below every valid version (equal to each other).</summary>
    public static int Compare(string? a, string? b)
    {
        bool okA = TryParse(a, out var va), okB = TryParse(b, out var vb);
        if (okA && okB) return va.CompareTo(vb);
        return okA.CompareTo(okB);
    }

    /// <summary>"v1.2.0" → "1.2.0" (null when not a version).</summary>
    public static string? Normalize(string? s) => TryParse(s, out var v) ? v.ToString() : null;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + string.Join('.', Pre) : "");
}
