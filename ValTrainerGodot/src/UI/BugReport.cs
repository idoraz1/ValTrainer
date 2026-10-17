using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// "Report a bug": what the in-app form sends. The player's text plus a snapshot of system info, context, the settings
/// that matter for bugs and the end of the current godot.log (plus the end of the previous session's log when that
/// session crashed, see <see cref="ReadLogs"/>), all passed through <see cref="Redactor"/> (Windows user
/// name, profile and AppData paths, PC name, account ids, e-mail addresses). Nothing is sent until the player presses
/// SEND, and then exactly what the panel's "What gets sent" preview shows: one HTTPS POST of <see cref="ToJson"/> to
/// <see cref="AppInfo.BugReportUrl"/> (the relay in tools/bug-relay posts it to #bug-reports on the ValTrainer Discord).
/// <para>JSON contract (schema 1, the relay validates it): <c>schema</c>; <c>app</c> {version, install
/// (installer|portable|dev), os, cpu, ram_gb (number), gpu, gpu_type, renderer, driver, display, window_mode, fps_cap,
/// graphics}; <c>context</c> {screen, mode, uptime_s (number)}; <c>settings</c> (≤ 30 string pairs, key ≤ 40, value
/// ≤ 120); <c>report</c> {title, what, steps, expected, contact}; <c>log_tail</c> (≤ 16000 chars). The whole body
/// stays ≤ 32 KB (the log tail is trimmed first).</para>
/// <para>Dev: <c>--bugreport-test</c> runs <see cref="SelfTest"/> (works with --headless).</para>
/// </summary>
public static class BugReport
{
    public const int TitleMin = 3, TitleMax = 100, WhatMin = 10, WhatMax = 1500, StepsMax = 1500, ExpectedMax = 1000, ContactMax = 40;
    public const int MaxSettings = 30, MaxKey = 40, MaxValue = 120, LogLines = 150, LogMaxChars = 16000, MaxBodyBytes = 32 * 1024;

    /// <summary>What the player typed (trimmed before validation and sending).</summary>
    public sealed record Fields(string Title, string What, string Steps, string Expected, string Contact);

    /// <summary>Everything collected automatically, already redacted. Taken once when the panel opens, so the preview is
    /// exactly what gets sent.</summary>
    public sealed class Snapshot
    {
        public List<KeyValuePair<string, string>> App = new();
        public double RamGb;
        public string Screen = "", Mode = "";
        public long UptimeS;
        public List<KeyValuePair<string, string>> Settings = new();
        public string LogTail = "";
        public int LogLineCount;
        /// <summary>The log tail starts with the end of the previous session's log (it didn't close normally).</summary>
        public bool LogHasCrash;
    }

    /// <summary>Why the report can't be sent yet (null = it can).</summary>
    public static string? Problem(Fields f)
    {
        int t = f.Title.Trim().Length, w = f.What.Trim().Length;
        if (t < TitleMin && w < WhatMin) return $"Add a title ({TitleMin}+ characters) and say what happened ({WhatMin}+ characters)";
        if (t < TitleMin) return $"The title needs at least {TitleMin} characters";
        if (t > TitleMax) return $"The title can have at most {TitleMax} characters";
        if (w < WhatMin) return $"Say what happened in at least {WhatMin} characters";
        if (w > WhatMax) return $"\"What happened?\" can have at most {WhatMax} characters";
        return null;
    }

    // ---------------- collecting ----------------

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Main thread. <paramref name="screen"/> is where the form was opened ("menu", "settings").</summary>
    public static Snapshot Collect(string screen, Redactor? redactor = null)
    {
        var r = redactor ?? Redactor.Default;
        var s = new Snapshot { Screen = screen, Mode = Main.I?.LastModeKey ?? "none", UptimeS = (long)(Time.GetTicksMsec() / 1000) };
        void App(string key, string value) => s.App.Add(new(key, Clip(r.Text(value), MaxValue)));
        App("version", AppInfo.Version);
        App("install", InstallType());
        App("os", Safe(() => $"{OS.GetName()} {OS.GetVersionAlias()}"));
        App("cpu", Safe(() => $"{OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)"));
        try { long b = OS.GetMemoryInfo()["physical"].AsInt64(); s.RamGb = b > 0 ? Math.Round(b / 1073741824.0, 1) : 0; } catch { }
        App("gpu", SysInfo.GpuName.Length > 0 ? SysInfo.GpuName : "unknown");
        App("gpu_type", SysInfo.AdapterType switch
        {
            RenderingDevice.DeviceType.DiscreteGpu => "discrete",
            RenderingDevice.DeviceType.IntegratedGpu => "integrated",
            RenderingDevice.DeviceType.VirtualGpu => "virtual",
            RenderingDevice.DeviceType.Cpu => "cpu",
            _ => "other",
        });
        App("renderer", SysInfo.RendererName);
        App("driver", Driver);
        App("display", Safe(Display));
        App("window_mode", Safe(WindowMode));
        App("fps_cap", Engine.MaxFps <= 0 ? "unlimited" : Engine.MaxFps.ToString(Inv));
        App("graphics", Main.I is { } m ? SettingsScreen.Qualities[Math.Clamp(m.Settings.Quality, 0, 3)] : "unknown");
        if (Main.I != null) s.Settings = CapSettings(SettingsPairs(), r);
        (s.LogTail, s.LogLineCount, s.LogHasCrash) = ReadLogs(Paths.LogDir, r);
        return s;
    }

    /// <summary>"installer" (Setup, also older Setups without install.ini), "portable" (the single-file exe) or "dev"
    /// (--dev runs and the Godot editor), from the updater's detection (<see cref="Updater.Kind"/>).</summary>
    internal static string InstallType() =>
        CmdLine.Dev || Updater.Kind == InstallKind.Source ? "dev" : Updater.Kind == InstallKind.Portable ? "portable" : "installer";

    static string Display()
    {
        int sc = DisplayServer.WindowGetCurrentScreen();
        var size = DisplayServer.ScreenGetSize(sc);
        float hz = DisplayServer.ScreenGetRefreshRate(sc);
        if (size.X <= 0) return "unknown";
        return $"{size.X}x{size.Y}" + (hz > 0 ? $" @ {Mathf.RoundToInt(hz)} Hz" : "");
    }

    static string WindowMode()
    {
        var size = DisplayServer.WindowGetSize();
        return DisplayServer.WindowGetMode() switch
        {
            DisplayServer.WindowMode.ExclusiveFullscreen => "fullscreen",
            DisplayServer.WindowMode.Fullscreen => "windowed fullscreen",
            DisplayServer.WindowMode.Maximized => $"windowed (maximized) {size.X}x{size.Y}",
            var w => $"{w.ToString().ToLowerInvariant()} {size.X}x{size.Y}",
        };
    }

    static string? driver;

    /// <summary>GPU driver provider and version plus the graphics API version, e.g. "NVIDIA 32.0.15.8208 · API 1.4.312".
    /// The driver comes from Windows' display adapter entries (read-only registry, matched by the GPU name): Godot's
    /// own OS.GetVideoAdapterDriverInfo blocks for seconds and comes back empty on some PCs.</summary>
    public static string Driver => driver ??= ReadDriver();

    static string ReadDriver()
    {
        string api = Safe(RenderingServer.GetVideoAdapterApiVersion), gpu = SysInfo.GpuName, drv = "";
        if (OperatingSystem.IsWindows() && gpu.Length > 0)
            try
            {
                using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                foreach (var name in cls?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    if (!name.All(char.IsAsciiDigit)) continue; // "0000", "0001" …; skip "Properties" (no access)
                    try
                    {
                        using var key = cls!.OpenSubKey(name);
                        // Godot may drop the vendor: "Quadro P2000" vs Windows' "NVIDIA Quadro P2000"
                        if (key?.GetValue("DriverDesc") is string desc && desc.Trim().Length > 0
                            && (desc.Contains(gpu.Trim(), StringComparison.OrdinalIgnoreCase) || gpu.Contains(desc.Trim(), StringComparison.OrdinalIgnoreCase))
                            && key.GetValue("DriverVersion") is string ver)
                        {
                            drv = $"{key.GetValue("ProviderName") as string} {ver}".Trim();
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        return drv.Length > 0 ? $"{drv} · API {api}" : api.Length > 0 ? $"API {api}" : "unknown";
    }

    /// <summary>The settings that help with bug reports, in order of importance (capped at <see cref="MaxSettings"/>).</summary>
    static List<KeyValuePair<string, string>> SettingsPairs()
    {
        var app = Main.I;
        var st = app.Settings;
        var v = app.Valorant;
        var list = new List<KeyValuePair<string, string>>();
        void Add(string key, string value) => list.Add(new(key, value));
        string F(float x) => x.ToString("0.###", Inv);
        string Src(int over, string val, string custom) => over >= 0 ? "custom: " + custom : "VALORANT: " + val;
        string[] modes = { "fullscreen", "windowed fullscreen", "windowed" };
        string Mode(int m) => modes[Math.Clamp(m, 0, 2)];

        Add("sens", F(app.Sens));
        Add("sens_source", st.UseSensOverride ? "custom" : v.SensFromFile ? "VALORANT" : "VALORANT default (not read)");
        Add("valorant_sens", F(v.Sensitivity));
        Add("scoped_sens_multiplier", F(v.ZoomedSensMult));
        Add("ads_sens_multiplier", F(v.AdsSensMult));
        Add("hold_to_scope", v.HoldToScope ? "on" : "off");
        Add("dpi", $"{st.Dpi} (as set in ValTrainer, eDPI {F(app.Sens * st.Dpi)})");
        Add("viewmodel_fov", F(st.ViewmodelFov));
        Add("fps_cap", Src(st.FpsCapOverride, v.FrameRateLimit <= 0 ? "unlimited" : F(v.FrameRateLimit), st.FpsCapOverride == 0 ? "unlimited" : st.FpsCapOverride.ToString(Inv)));
        Add("vsync", Src(st.VSyncOverride, v.VSync ? "on" : "off", st.VSyncOverride == 1 ? "on" : "off"));
        Add("window_mode_setting", Src(st.WindowModeOverride, Mode(v.WindowMode), Mode(st.WindowModeOverride)));
        Add("monitor", st.MonitorOverride >= 0 ? $"custom: {st.MonitorOverride}" : $"VALORANT: {v.MonitorIndex}");
        Add("graphics_quality", SettingsScreen.Qualities[Math.Clamp(st.Quality, 0, 3)] + (st.AutoQualityReason != null ? $" (auto: {st.AutoQualityReason})" : ""));
        Add("renderer_choice", RendererArgs());
        Add("rendering_method", SysInfo.RenderingMethod + " / " + SysInfo.DriverName);
        Add("tier", Difficulty.Get(app.Tier).Name);
        Add("map", app.MapKey);
        Add("crosshair_source", st.UseFinderCrosshair && st.FinderCrosshairCode != null
            ? "custom code" + (st.FinderCrosshairTab != null ? $" ({st.FinderCrosshairTab} tab)" : "") : "VALORANT");
        Add("valorant_import", v.Found ? "imported" : v.Status.ToString());
        Add("valorant_resolution", $"{v.ResX}x{v.ResY}");
        Add("valorant_anti_aliasing", SettingsScreen.AaName(v.AntiAliasing));
        Add("enemy_color", UiTheme.EnemyColorNames[Math.Clamp(app.EnemyColorIndex, 0, UiTheme.EnemyColorNames.Length - 1)]);
        Add("targets", $"{st.TargetStyle}, outlines {(st.Outlines ? "on" : "off")}");
        Add("viewmodel", (st.ViewModel ? "on" : "off") + (st.LeftHandedWeapon ? ", left-handed" : ""));
        Add("volume", F(st.Volume));
        Add("update_checks", st.UpdatesConsent == null ? "not asked yet" : st.AutoUpdates ? "on" : "off");
        Add("updater", $"{Updater.Status}, {Updater.KindName}" + (Updater.Message is { } msg ? $" ({msg})" : ""));
        Add("system_culture", Boot.SystemCulture.Length > 0 ? Boot.SystemCulture : "invariant");
        Add("data_folder", Paths.DataDirOverridden ? "custom (--data-dir)" : Paths.DataDirProblem ?? "default");
        Add("last_save_error", Paths.LastWriteError ?? "none");
        return list;
    }

    /// <summary>The renderer the player forced on the command line ("ValTrainer (safe graphics)" shortcut and the
    /// like), or "default".</summary>
    static string RendererArgs()
    {
        var parts = new List<string>();
        foreach (var flag in new[] { "--rendering-method", "--rendering-driver" })
            if (CmdLine.After(flag) is { } val) parts.Add($"{flag} {val}");
        return parts.Count > 0 ? string.Join(' ', parts) : "default";
    }

    static List<KeyValuePair<string, string>> CapSettings(IEnumerable<KeyValuePair<string, string>> pairs, Redactor r) =>
        pairs.Take(MaxSettings).Select(p => new KeyValuePair<string, string>(Clip(p.Key, MaxKey), Clip(r.Text(p.Value), MaxValue))).ToList();

    /// <summary>What <see cref="Main"/> logs as its last line on a normal exit (a crash never logs it).</summary>
    public const string CleanExit = "Closed normally";
    const string CleanExitLine = "[ValTrainer] " + CleanExit;
    /// <summary>Share of the log budget for the crashed session when both logs are sent.</summary>
    const int PrevLines = 60, PrevChars = 6500;

    /// <summary>The log part of the report, redacted: the last <see cref="LogLines"/> lines of godot.log (at most
    /// <see cref="LogMaxChars"/>). After a crash Godot renames the crashed session's log to godot&lt;date&gt;.log when the
    /// game starts again, so if the newest of those doesn't end with the <see cref="CleanExit"/> line, its last
    /// <see cref="PrevLines"/> lines go first (under a header line) and this session's get the rest of the budget.</summary>
    public static (string Text, int Lines, bool Crash) ReadLogs(string dir, Redactor r)
    {
        var (cur, curErr) = ReadEnd(Path.Combine(dir, "godot.log"));
        if (cur == null) return ($"(couldn't read the log: {r.Text(curErr!)})", 1, false);
        string? prevPath = null;
        try
        {
            prevPath = new DirectoryInfo(dir).GetFiles("godot*.log")
                .Where(f => !f.Name.Equals("godot.log", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch { }
        string? prev = prevPath != null ? ReadEnd(prevPath).Text : null;
        if (prev == null || prev.Contains(CleanExitLine, StringComparison.Ordinal)) { var (t, n) = TailOf(cur, r); return (t, n, false); }
        var (pt, pn) = TailOf(prev, r, PrevLines, PrevChars);
        string head = $"--- previous session ({Path.GetFileName(prevPath)}): it didn't close normally, its last {pn} lines ---";
        const string mid = "--- this session (godot.log) ---";
        var (ct, cn) = TailOf(cur, r, LogLines - pn - 2, LogMaxChars - pt.Length - head.Length - mid.Length - 3);
        return ($"{head}\n{pt}\n{mid}\n{ct}", pn + cn + 2, true);
    }

    /// <summary>The last 256 KB of a log file. Opened shared: Godot keeps writing to the current one.</summary>
    static (string? Text, string? Error) ReadEnd(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int window = 256 * 1024;
            if (fs.Length > window) fs.Seek(-window, SeekOrigin.End);
            using var rd = new StreamReader(fs, Encoding.UTF8);
            return (rd.ReadToEnd(), null);
        }
        catch (Exception e) { return (null, Log.Describe(e)); }
    }

    static (string, int) TailOf(string raw, Redactor r, int maxLines = LogLines, int maxChars = LogMaxChars)
    {
        var lines = raw.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var tail = lines.Skip(Math.Max(0, lines.Length - maxLines)).Select(l => r.Text(l.TrimEnd())).ToList();
        // keep the newest lines: drop the oldest until it fits
        int total = tail.Sum(l => l.Length + 1);
        int skip = 0;
        while (skip < tail.Count - 1 && total > maxChars) total -= tail[skip++].Length + 1;
        tail = tail.Skip(skip).ToList();
        string text = string.Join('\n', tail);
        if (text.Length > maxChars) text = text[^maxChars..]; // one giant line
        return (text, tail.Count);
    }

    // ---------------- output ----------------

    static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

    /// <summary>The POST body (UTF-8 JSON, ≤ <see cref="MaxBodyBytes"/>: the oldest log lines go first).</summary>
    public static string ToJson(Snapshot s, Fields f)
    {
        string log = s.LogTail;
        while (true)
        {
            string json = Build(s, f, log).ToJsonString(JsonOpts);
            int bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes <= MaxBodyBytes || log.Length == 0) return json;
            // drop at least the overflow (bytes ≥ chars) from the front, then up to the next line start
            int cut = Math.Min(log.Length, Math.Max(256, bytes - MaxBodyBytes));
            int nl = log.IndexOf('\n', cut);
            log = nl < 0 ? "" : log[(nl + 1)..];
        }
    }

    static JsonObject Build(Snapshot s, Fields f, string log)
    {
        var app = new JsonObject();
        foreach (var (k, v) in s.App)
        {
            app[k] = v;
            if (k == "cpu") app["ram_gb"] = s.RamGb; // contract order: … cpu, ram_gb, gpu …
        }
        var settings = new JsonObject();
        foreach (var (k, v) in s.Settings) settings[k] = v;
        var r = Redactor.Default;
        return new JsonObject
        {
            ["schema"] = 1,
            ["app"] = app,
            ["context"] = new JsonObject { ["screen"] = s.Screen, ["mode"] = s.Mode, ["uptime_s"] = s.UptimeS },
            ["settings"] = settings,
            ["report"] = new JsonObject
            {
                // the player's own words: only file paths are redacted (a Discord name may well match the Windows one)
                ["title"] = Clip(r.Paths(f.Title.Trim()), TitleMax),
                ["what"] = Clip(r.Paths(f.What.Trim()), WhatMax),
                ["steps"] = Clip(r.Paths(f.Steps.Trim()), StepsMax),
                ["expected"] = Clip(r.Paths(f.Expected.Trim()), ExpectedMax),
                ["contact"] = Clip(f.Contact.Trim(), ContactMax),
            },
            ["log_tail"] = log,
        };
    }

    // ---------------- feature suggestions (POST /suggest → #suggestions) ----------------

    /// <summary>Suggestion categories: relay key and button label (the relay maps each key to a #suggestions tag).</summary>
    public static readonly (string Key, string Label)[] IdeaCategories =
        { ("drill", "NEW DRILL"), ("agent", "AGENT"), ("map", "MAP"), ("coach", "COACH"), ("ui", "UI / SETTINGS"), ("other", "OTHER") };

    /// <summary>The relay's suggestion endpoint: the bug-report URL with /report → /suggest.</summary>
    public static string SuggestUrl(string reportUrl) =>
        reportUrl.EndsWith("/report", StringComparison.Ordinal) ? reportUrl[..^"/report".Length] + "/suggest" : reportUrl;

    /// <summary>Suggestion JSON (schema 1): app {version, install} and idea {title, what, why, category, contact}. Nothing
    /// about the PC, no settings, no log.</summary>
    public static string IdeaJson(string title, string what, string why, string category, string contact)
    {
        var r = Redactor.Default;
        return new JsonObject
        {
            ["schema"] = 1,
            ["app"] = new JsonObject { ["version"] = AppInfo.Version, ["install"] = InstallType() },
            ["idea"] = new JsonObject
            {
                ["title"] = Clip(r.Paths(title.Trim()), TitleMax),
                ["what"] = Clip(r.Paths(what.Trim()), WhatMax),
                ["why"] = Clip(r.Paths(why.Trim()), StepsMax),
                ["category"] = IdeaCategories.Any(c => c.Key == category) ? category : "",
                ["contact"] = Clip(contact.Trim(), ContactMax),
            },
        }.ToJsonString(JsonOpts);
    }

    /// <summary>Why the suggestion can't be sent yet (null = it can). Same limits as a bug report's title / "what".</summary>
    public static string? IdeaProblem(string title, string what)
    {
        int t = title.Trim().Length, w = what.Trim().Length;
        if (t < TitleMin && w < WhatMin) return $"Add a title ({TitleMin}+ characters) and describe your idea ({WhatMin}+ characters)";
        if (t < TitleMin) return $"The title needs at least {TitleMin} characters";
        if (t > TitleMax) return $"The title can have at most {TitleMax} characters";
        if (w < WhatMin) return $"Describe your idea in at least {WhatMin} characters";
        if (w > WhatMax) return $"The idea can have at most {WhatMax} characters";
        return null;
    }

    /// <summary>A suggestion as plain text (COPY IDEA, or pasting it in #suggestions by hand).</summary>
    public static string IdeaText(string title, string what, string why, string category, string contact)
    {
        var sb = new StringBuilder();
        sb.Append($"**{title.Trim()}**\n\n{what.Trim()}\n");
        if (why.Trim().Length > 0) sb.Append($"\nWhy: {why.Trim()}\n");
        if (IdeaCategories.FirstOrDefault(c => c.Key == category).Label is { } cat) sb.Append($"\nCategory: {cat}\n");
        if (contact.Trim().Length > 0) sb.Append($"Discord: {contact.Trim()}\n");
        sb.Append($"ValTrainer {AppInfo.Version} ({InstallType()})\n");
        return sb.ToString();
    }

    /// <summary>The "What gets sent" preview: everything collected automatically, exactly as it's sent.</summary>
    public static string PreviewText(Snapshot s)
    {
        var sb = new StringBuilder();
        sb.Append("SYSTEM\n");
        foreach (var (k, v) in s.App)
        {
            sb.Append($"  {k}: {v}\n");
            if (k == "cpu") sb.Append($"  ram_gb: {s.RamGb.ToString("0.#", Inv)}\n");
        }
        sb.Append($"\nCONTEXT\n  screen: {s.Screen}\n  mode: {s.Mode}\n  uptime_s: {s.UptimeS}\n");
        sb.Append("\nSETTINGS\n");
        foreach (var (k, v) in s.Settings) sb.Append($"  {k}: {v}\n");
        sb.Append(s.LogHasCrash ? $"\nLOG ({s.LogLineCount} lines: the end of the previous session, which didn't close normally, then godot.log)\n"
            : $"\nLOG (last {s.LogLineCount} lines of godot.log)\n");
        sb.Append(s.LogTail);
        return sb.ToString();
    }

    /// <summary>COPY REPORT: the same report as readable text (Discord, GitHub, anywhere).</summary>
    public static string CopyText(Snapshot s, Fields f)
    {
        var r = Redactor.Default;
        var sb = new StringBuilder();
        sb.Append($"ValTrainer bug report: {r.Paths(f.Title.Trim())}\n\n");
        void Part(string head, string body)
        {
            if (body.Trim().Length > 0) sb.Append($"{head}\n{r.Paths(body.Trim())}\n\n");
        }
        Part("What happened?", f.What);
        Part("Steps to reproduce", f.Steps);
        Part("What did you expect?", f.Expected);
        if (f.Contact.Trim().Length > 0) sb.Append($"Discord: {f.Contact.Trim()}\n\n");
        sb.Append("```\n").Append(PreviewText(s)).Append("\n```\n");
        return sb.ToString();
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch { return ""; }
    }

    // ---------------- self-test ----------------

    static int pass, fail;

    /// <summary>Dev: <c>--dev --bugreport-test</c> (works with --headless). Redaction with a made-up user, the JSON's
    /// shape and limits against the contract, and the 32 KB cap with an oversized log. Prints "[bugreport-test] …"
    /// lines; returns the number of failures.</summary>
    public static int SelfTest()
    {
        pass = fail = 0;
        try
        {
            RedactionChecks();
            JsonChecks();
            IdeaChecks();
            CrashLogChecks();
        }
        catch (Exception e) { Check("unexpected exception", false, e.ToString()); }
        Say($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed)");
        return fail;
    }

    /// <summary>The crashed session's log is attached only when it lacks the clean-exit line; limits still hold.</summary>
    static void CrashLogChecks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vt_bugreport_test_" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string Lines(string tag, int n) => string.Join('\n', Enumerable.Range(0, n).Select(i => $"{tag} line {i} " + new string('x', 80)));
            File.WriteAllText(Path.Combine(dir, "godot.log"), Lines("NOW", 400) + "\nNOW NEWEST");
            var old = Path.Combine(dir, "godot2026-10-01T10.00.00.log");
            File.WriteAllText(old, Lines("OLD", 50) + "\nOLD CRASH");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            var prev = Path.Combine(dir, "godot2026-10-17T20.00.00.log");
            File.WriteAllText(prev, Lines("PREV", 300) + "\n[ValTrainer] Unhandled exception (fatal): boom");
            File.SetLastWriteTimeUtc(prev, DateTime.UtcNow.AddMinutes(-5));
            var (t, n, crash) = ReadLogs(dir, Redactor.Default);
            Check("crash log: previous session attached", crash && t.Contains("boom") && t.Contains("didn't close normally") && !t.Contains("OLD"), t[..Math.Min(300, t.Length)]);
            Check("crash log: this session's newest line kept", t.EndsWith("NOW NEWEST") && t.IndexOf("boom") < t.IndexOf("--- this session"));
            Check($"crash log: within limits ({n} lines, {t.Length} chars)", n <= LogLines && t.Length <= LogMaxChars && t.Split('\n').Length == n);
            File.AppendAllText(prev, "\n" + CleanExitLine + "\n");
            var (t2, n2, crash2) = ReadLogs(dir, Redactor.Default);
            Check("crash log: clean exit -> godot.log only", !crash2 && !t2.Contains("PREV") && t2.EndsWith("NOW NEWEST") && n2 <= LogLines);
            File.Delete(prev); File.Delete(old);
            Check("crash log: first session ever", ReadLogs(dir, Redactor.Default) is { Crash: false } first && first.Text.EndsWith("NOW NEWEST"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void IdeaChecks()
    {
        Check("idea: suggest URL", SuggestUrl("https://x.workers.dev/report") == "https://x.workers.dev/suggest" && SuggestUrl("") == "");
        Check("idea: needs title + idea", IdeaProblem("ab", "short") != null && IdeaProblem("Crouch drill", "too short") != null && IdeaProblem("Crouch drill", "Bots that crouch-spam.") == null);
        using var doc = JsonDocument.Parse(IdeaJson(" Crouch drill ", "Bots that crouch-spam.", "", "cheats", "zoe"));
        var idea = doc.RootElement.GetProperty("idea");
        Check("idea: trimmed title", idea.GetProperty("title").GetString() == "Crouch drill");
        Check("idea: unknown category dropped", idea.GetProperty("category").GetString() == "");
        Check("idea: no PC info / log", !doc.RootElement.TryGetProperty("log_tail", out _) && !doc.RootElement.GetProperty("app").TryGetProperty("gpu", out _));
        string text = IdeaText("Crouch drill", "Bots that crouch-spam.", "", "drill", "");
        Check("idea: text has category, no empty fields", text.Contains("Category: NEW DRILL") && !text.Contains("Why:") && !text.Contains("Discord:"), text);
    }

    static void RedactionChecks()
    {
        var fake = new Redactor("Zoë Tester", "DESKTOP-7QX2LB", "CORP", new[]
        {
            (@"C:\Users\Zoë Tester\AppData\Local\Temp", "%TEMP%"),
            (@"C:\Users\Zoë Tester\AppData\Local", "%LOCALAPPDATA%"),
            (@"C:\Users\Zoë Tester\AppData\Roaming", "%APPDATA%"),
            (@"C:\Users\Zoë Tester", "%USERPROFILE%"),
        });
        string input = string.Join('\n',
            @"logs C:\Users\Zoë Tester\AppData\Roaming\Godot\app_userdata\ValTrainer\logs",
            "data c:/users/zoë tester/appdata/local/VALORANT/Saved/Config/0123abcd-5678-9abc-def0-123456789abc-eu/x.ini",
            @"json C:\\Users\\Zoë Tester\\Documents\\a.txt",
            @"temp C:\Users\Zoë Tester\AppData\Local\Temp\vt",
            @"other D:\Games\Zoë Tester\ValTrainer.exe",
            "hello Zoë Tester on DESKTOP-7QX2LB (desktop-7qx2lb) in CORP, mail zoe.t@example.com");
        string o = fake.Text(input);
        Check("redact: no user name left", !o.Contains("Zoë", StringComparison.OrdinalIgnoreCase) && !o.Contains("Tester", StringComparison.OrdinalIgnoreCase), o);
        Check("redact: no PC name left", !o.Contains("7QX2LB", StringComparison.OrdinalIgnoreCase), o);
        Check(@"redact: %APPDATA%\Godot", o.Contains(@"%APPDATA%\Godot\app_userdata"), o);
        Check("redact: forward slashes + case", o.Contains("%LOCALAPPDATA%/VALORANT/Saved/Config/<id>-eu/x.ini"), o);
        Check(@"redact: doubled backslashes", o.Contains(@"%USERPROFILE%\\Documents"), o);
        Check(@"redact: %TEMP% before %LOCALAPPDATA%", o.Contains(@"%TEMP%\vt"), o);
        Check(@"redact: user name elsewhere in a path", o.Contains(@"D:\Games\<user>\ValTrainer.exe"), o);
        Check("redact: names, domain, e-mail", o.Contains("hello <user> on <pc> (<pc>) in <domain>, mail <email>"), o);
        Check("redact: report text keeps names", fake.Paths("Zoë Tester: crash in C:\\Users\\Zoë Tester\\x") == @"Zoë Tester: crash in %USERPROFILE%\x");

        var shortName = new Redactor("Al", "PC", null, new[] { (@"C:\Users\Al", "%USERPROFILE%") });
        string so = shortName.Text(@"C:\Users\Al\x D:\Al\y Always ALPS PC-side");
        Check("redact: short names only in paths", so == @"%USERPROFILE%\x D:\<user>\y Always ALPS PC-side", so);

        // the real one: this PC's user name, PC name and profile must not survive
        var real = Redactor.Default;
        string user = System.Environment.UserName, pc = System.Environment.MachineName;
        string prof = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string ro = real.Text($"{prof}\\x {prof.Replace('\\', '/')}/y {user} @ {pc}");
        Check("redact: this PC", (user.Length < 3 || !ro.Contains(user, StringComparison.OrdinalIgnoreCase))
            && (pc.Length < 3 || !ro.Contains(pc, StringComparison.OrdinalIgnoreCase)) && (prof.Length == 0 || !ro.Contains(prof, StringComparison.OrdinalIgnoreCase)), ro);
    }

    static void JsonChecks()
    {
        var f = new Fields("Game froze", "The game froze after the countdown.", "1. Start Gridshot", "", "zoe#1234");
        Check("validate: ok", Problem(f) == null, Problem(f) ?? "");
        Check("validate: short title", Problem(f with { Title = " ab " }) != null);
        Check("validate: long title", Problem(f with { Title = new string('x', TitleMax + 1) }) != null);
        Check("validate: short what", Problem(f with { What = "broken" }) != null);

        Say($"driver: {Driver}");
        var s = Collect("menu");
        string json = ToJson(s, f with { Title = "Crash when " + new string('x', 200) }); // clipped to TitleMax
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Check("json: schema 1", root.GetProperty("schema").GetInt32() == 1);
            var app = root.GetProperty("app");
            string[] appKeys = { "version", "install", "os", "cpu", "ram_gb", "gpu", "gpu_type", "renderer", "driver", "display", "window_mode", "fps_cap", "graphics" };
            Check("json: app keys in order", app.EnumerateObject().Select(p => p.Name).SequenceEqual(appKeys), string.Join(",", app.EnumerateObject().Select(p => p.Name)));
            Check("json: app types", app.EnumerateObject().All(p => p.Value.ValueKind == (p.Name == "ram_gb" ? JsonValueKind.Number : JsonValueKind.String)));
            Check("json: install", app.GetProperty("install").GetString() is "installer" or "portable" or "dev", app.GetProperty("install").GetString() ?? "");
            var ctx = root.GetProperty("context");
            Check("json: context", ctx.GetProperty("screen").GetString() == "menu" && ctx.GetProperty("mode").ValueKind == JsonValueKind.String
                && ctx.GetProperty("uptime_s").ValueKind == JsonValueKind.Number);
            var set = root.GetProperty("settings").EnumerateObject().ToList();
            Check($"json: {set.Count} settings within limits", set.Count is > 10 and <= MaxSettings
                && set.All(p => p.Name.Length <= MaxKey && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Length <= MaxValue));
            var rep = root.GetProperty("report");
            Check("json: report", rep.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "title", "what", "steps", "expected", "contact" })
                && rep.GetProperty("title").GetString()!.Length == TitleMax);
            Check("json: log_tail", root.GetProperty("log_tail").ValueKind == JsonValueKind.String && root.GetProperty("log_tail").GetString()!.Length <= LogMaxChars);
        }
        string user = System.Environment.UserName, pc = System.Environment.MachineName;
        Check("json: no user / PC name", (user.Length < 3 || !json.Contains(user, StringComparison.OrdinalIgnoreCase)) && (pc.Length < 3 || !json.Contains(pc, StringComparison.OrdinalIgnoreCase)));

        // oversized: 3-byte characters in the log and the fields at their maximum must still fit in 32 KB, newest log line kept
        var big = new StringBuilder();
        for (int i = 0; i < 400; i++) big.Append($"line {i} 日本語のログ ç ").Append(new string('é', 60)).Append('\n');
        big.Append("NEWEST LINE");
        (s.LogTail, s.LogLineCount) = TailOf(big.ToString(), Redactor.Default);
        var full = new Fields(new string('t', TitleMax), new string('ü', WhatMax), new string('ß', StepsMax), new string('ö', ExpectedMax), new string('d', ContactMax));
        string bigJson = ToJson(s, full);
        int bytes = Encoding.UTF8.GetByteCount(bigJson);
        using (var doc = JsonDocument.Parse(bigJson))
        {
            string tail = doc.RootElement.GetProperty("log_tail").GetString()!;
            Check($"json: capped at 32 KB ({bytes} bytes, log {tail.Length} chars)", bytes <= MaxBodyBytes && tail.EndsWith("NEWEST LINE") && tail.Length > 1000);
        }
        Check("tail: at most 150 lines / 16000 chars", s.LogLineCount <= LogLines && s.LogTail.Length <= LogMaxChars, $"{s.LogLineCount} lines, {s.LogTail.Length} chars");
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        Say($"{(ok ? "PASS" : "FAIL")} {name}{(ok || detail.Length == 0 ? "" : " :: " + detail)}");
    }

    static void Say(string s) => GD.Print("[bugreport-test] " + s);
}

/// <summary>
/// Removes personal details from text that leaves the PC: the Windows profile, AppData and temp folders become
/// %USERPROFILE% / %APPDATA% / %LOCALAPPDATA% / %TEMP% (either slash, any case), and the user name, PC name and
/// domain become &lt;user&gt; / &lt;pc&gt; / &lt;domain&gt; (names shorter than 3 characters only inside paths, so
/// "Al" doesn't eat "Always"). GUIDs (VALORANT account folders) become &lt;id&gt; and e-mail addresses &lt;email&gt;.
/// </summary>
public sealed class Redactor
{
    readonly List<(Regex Re, string With)> paths = new(), names = new();
    static readonly Regex Guid = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    static Redactor? def;

    /// <summary>For this PC's user.</summary>
    public static Redactor Default => def ??= FromEnvironment();

    public Redactor(string? user, string? machine, string? domain, IEnumerable<(string Path, string Placeholder)> folders)
    {
        // longest folder first: %TEMP% and %LOCALAPPDATA% live inside %USERPROFILE%
        foreach (var (p, with) in folders.Where(f => !string.IsNullOrWhiteSpace(f.Path)).OrderByDescending(f => f.Path.Length))
        {
            var parts = p.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue; // never a bare drive
            paths.Add((new Regex(string.Join(@"[\\/]+", parts.Select(Regex.Escape)) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase), with));
        }
        void Name(string? n, string with)
        {
            if (string.IsNullOrWhiteSpace(n)) return;
            string e = Regex.Escape(n.Trim());
            names.Add((new Regex(n.Trim().Length >= 3 ? $@"(?<![\p{{L}}\p{{N}}_]){e}(?![\p{{L}}\p{{N}}_])" : $@"(?<=[\\/]){e}(?=[\\/]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline), with));
        }
        Name(user, "<user>");
        Name(machine, "<pc>");
        if (!string.IsNullOrWhiteSpace(domain) && !string.Equals(domain, machine, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(domain, "WORKGROUP", StringComparison.OrdinalIgnoreCase)) Name(domain, "<domain>");
    }

    static Redactor FromEnvironment()
    {
        string Env(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
        string Folder(System.Environment.SpecialFolder sf) => Env(() => System.Environment.GetFolderPath(sf));
        var folders = new List<(string, string)>
        {
            (Env(() => Path.GetTempPath()), "%TEMP%"),
            (Folder(System.Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Env(() => System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ""), "%LOCALAPPDATA%"),
            (Folder(System.Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Env(() => System.Environment.GetEnvironmentVariable("APPDATA") ?? ""), "%APPDATA%"),
            (Folder(System.Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Env(() => System.Environment.GetEnvironmentVariable("USERPROFILE") ?? ""), "%USERPROFILE%"),
        };
        return new Redactor(Env(() => System.Environment.UserName), Env(() => System.Environment.MachineName),
            Env(() => System.Environment.UserDomainName), folders.Distinct());
    }

    /// <summary>Folders only (the player's own text in the report).</summary>
    public string Paths(string s)
    {
        foreach (var (re, with) in paths) s = re.Replace(s, with);
        return s;
    }

    /// <summary>Folders, names, account ids and e-mail addresses (system info, settings, log).</summary>
    public string Text(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Paths(s);
        s = Email.Replace(s, "<email>");
        foreach (var (re, with) in names) s = re.Replace(s, with);
        return Guid.Replace(s, "<id>");
    }
}

/// <summary>
/// Modal "REPORT A BUG" form (menu header, Settings → About), or with <c>idea</c> the "SUGGEST A FEATURE" form (title,
/// idea, why, category, Discord name → POST /suggest → #suggestions; <c>--screen suggest</c> in dev). Bug report: title, what happened, steps, expected, Discord name, and
/// on the right the "What gets sent" preview. SEND posts the report (see <see cref="BugReport"/>); COPY REPORT copies it
/// as text; OPEN ON GITHUB opens the issue chooser. Without a relay URL in this build SEND becomes COPY REPORT &amp; OPEN
/// DISCORD. One report per minute per session. Esc closes.
/// <para>Dev: plain <c>--dev</c> never sends: SEND prints the JSON to the log ("Dev: not sent"). <c>--report-url
/// &lt;url&gt;</c> sends there instead (http only to localhost / 127.0.0.1; <c>off</c> shows the build-without-relay
/// buttons). <c>--screen bugreport</c> opens the panel over the menu, <c>--bugreport-demo</c> fills in a sample report
/// and <c>--bugreport-answer send|cancel</c> presses that button after 2 s.</para>
/// </summary>
public partial class BugReportPanel : Control
{
    const double TimeoutSec = 15, MinGapSec = 60;

    /// <summary>When this session last got an answer from the relay (one report per <see cref="MinGapSec"/>).</summary>
    static DateTime lastSend = DateTime.MinValue, lastIdea = DateTime.MinValue;

    enum State { Editing, Sending, Sent, Failed, DryRun }

    readonly float k;
    readonly string screen;
    readonly Action onClose;
    /// <summary>"SUGGEST A FEATURE" instead of "REPORT A BUG": posts to #suggestions (POST /suggest), no PC info / log.</summary>
    readonly bool idea;
    string category = "";
    readonly List<VButton> categoryButtons = new();
    BugReport.Snapshot snap = null!;
    State state = State.Editing;
    string? message;
    Color messageColor = UiTheme.Dim;
    float copiedFlash;

    LineEdit titleEdit = null!, contactEdit = null!;
    TextEdit whatEdit = null!, stepsEdit = null!, expectedEdit = null!, preview = null!;
    VButton send = null!, discord = null!, cancel = null!, myPost = null!;
    string? postId;
    VButton? copyBtn, ghBtn;
    Label status = null!;
    readonly List<DrawBox> counters = new();
    HttpRequest? http;

    /// <param name="screen">Where it was opened, for the report's context ("menu", "settings").</param>
    public BugReportPanel(float k, string screen, Action onClose, bool idea = false)
    {
        this.k = k;
        this.screen = screen;
        this.onClose = onClose;
        this.idea = idea;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the screen underneath doesn't get clicks
    }

    static string? DevUrl => CmdLine.DevAfter("--report-url");
    /// <summary>Plain --dev: SEND only prints the JSON.</summary>
    static bool DryRun => CmdLine.Dev && DevUrl == null;
    static string ReportUrl => CmdLine.Dev ? (DevUrl is { } u && u != "off" ? u : "") : AppInfo.BugReportUrl;
    string Url => idea ? BugReport.SuggestUrl(ReportUrl) : ReportUrl;
    /// <summary>HTTPS only (dev: also plain http to this PC, for a local relay).</summary>
    bool HasRelay => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || (CmdLine.Dev && (Url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) || Url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)));

    BugReport.Fields Fields => new(titleEdit.Text, whatEdit.Text, stepsEdit.Text, idea ? "" : expectedEdit.Text, contactEdit.Text);
    string? Problem() => idea ? BugReport.IdeaProblem(titleEdit.Text, whatEdit.Text) : BugReport.Problem(Fields);
    string CopyText() => idea ? BugReport.IdeaText(titleEdit.Text, whatEdit.Text, stepsEdit.Text, category, contactEdit.Text) : BugReport.CopyText(snap, Fields);
    string Channel => idea ? "#suggestions" : "#bug-reports";

    public override void _Ready()
    {
        snap = BugReport.Collect(screen);
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vs = GetViewportRect().Size;
        var panel = new VPanel
        {
            Title = idea ? "SUGGEST A FEATURE" : "REPORT A BUG", Caption = idea ? "posted in #suggestions on Discord" : "posted in #bug-reports on Discord",
            K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.97f),
            CustomMinimumSize = new Vector2(Mathf.Min((idea ? 1000 : 1400) * k, vs.X - 80 * k), 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(10 * k));
        panel.AddChild(col);

        col.AddChild(Text(idea
            ? "Your idea is posted in #suggestions on the ValTrainer Discord, where other players can see it and react. Join the server to follow it: we reply in your post and mark it Added when an update adds it."
            : "Your report is posted in #bug-reports on the ValTrainer Discord, where server members can see it. Join the server to follow it: we reply in your post and mark it Fixed when an update fixes it. Personal details like your Windows user name are removed.", 15, UiTheme.Dim));

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", (int)(22 * k));
        col.AddChild(body);
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", (int)(4 * k));
        body.AddChild(form);

        if (idea) BuildIdeaForm(form);
        else BuildBugForm(form, body);

        status = Text("", 15, UiTheme.Dim);
        status.CustomMinimumSize = new Vector2(40 * k, 24 * k);
        col.AddChild(status);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(12 * k));
        send = Button("SEND", VButton.Look.Primary, 170, Send);
        if (!DryRun && !HasRelay)
        {
            send.Label = "COPY REPORT & OPEN DISCORD";
            send.CustomMinimumSize = new Vector2(310 * k, 48 * k);
        }
        if (!DryRun && !HasRelay && idea) send.Label = "COPY IDEA & OPEN DISCORD";
        send.TooltipText = DryRun ? "Dev run: prints the JSON to the log instead of sending it"
            : idea ? (HasRelay ? "Posts your idea to #suggestions on the ValTrainer Discord"
                : "This build can't send ideas itself: copies it, then opens the Discord invite so you can paste it in #suggestions")
            : HasRelay ? "Posts the report and the info on the right to #bug-reports on the ValTrainer Discord"
            : "This build can't send reports itself: copies the report, then opens the Discord invite so you can paste it in #bug-reports";
        foot.AddChild(send);
        var copy = copyBtn = Button(idea ? "COPY IDEA" : "COPY REPORT", VButton.Look.Secondary, 170, Copy);
        copy.TooltipText = idea ? "Copies your idea as text, to paste anywhere" : "Copies the report and the info on the right as text, to paste anywhere";
        foot.AddChild(copy);
        discord = Button("OPEN DISCORD", VButton.Look.Secondary, 170, () => OpenDiscord());
        discord.TooltipText = "Opens the ValTrainer Discord invite in your browser";
        discord.Visible = false;
        foot.AddChild(discord);
        // After SEND: the player's own post (they need to be in the server to see it).
        myPost = Button(idea ? "VIEW MY IDEA" : "VIEW MY REPORT", VButton.Look.Secondary, 190, () =>
        {
            if (postId == null) return;
            if (Main.I.Dev) Log.Info($"Bug report: dev run, would open the post {postId}");
            else AppInfo.OpenDiscordPost(postId);
        });
        myPost.TooltipText = $"Opens your {(idea ? "idea" : "report")} on the ValTrainer Discord (join the server first to see it)";
        myPost.Visible = false;
        foot.AddChild(myPost);
        if (AppInfo.HasRepo && !idea)
        {
            var gh = ghBtn = Button("OPEN ON GITHUB", VButton.Look.Secondary, 190, () =>
            {
                if (Main.I.Dev) Log.Info($"Bug report: dev run, would open {AppInfo.NewIssueUrl}");
                else AppInfo.OpenGitHub(AppInfo.NewIssueUrl);
            });
            gh.TooltipText = "Opens GitHub's issue form in your browser instead (needs a GitHub account; attach godot.log there)";
            foot.AddChild(gh);
        }
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        cancel = Button("CANCEL", VButton.Look.Secondary, 150, Close);
        foot.AddChild(cancel);
        col.AddChild(foot);

        if (CmdLine.Dev && CmdLine.Has("--bugreport-demo") && idea)
        {
            titleEdit.Text = "Crouch-spam strafe drill";
            whatEdit.Text = "A drill where bots crouch-spam while strafing, like low-elo duels, so I can practice not over-flicking.";
            stepsEdit.Text = "I keep whiffing on players who crouch mid-fight.";
            contactEdit.Text = "zoe_aims";
            PickCategory("drill");
        }
        else if (CmdLine.Dev && CmdLine.Has("--bugreport-demo"))
        {
            titleEdit.Text = "Crosshair disappears after pausing";
            whatEdit.Text = "After I pause Gridshot with Esc and resume, the crosshair is gone until I restart the drill.";
            stepsEdit.Text = "1. Start Gridshot\n2. Press Esc, then Resume";
            expectedEdit.Text = "The crosshair stays visible.";
            contactEdit.Text = "zoe_aims";
        }
        Refresh();
        Callable.From(() => { if (IsInstanceValid(titleEdit)) titleEdit.GrabFocus(); }).CallDeferred();

        // Dev: "--bugreport-answer send|cancel" presses that button after 2 s (automated tests)
        if (CmdLine.DevAfter("--bugreport-answer")?.ToLowerInvariant() is { } ans)
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree()) return;
                Log.Info($"Bug report: --bugreport-answer {ans}");
                if (ans == "send") Send();
                else Close();
            };
    }

    /// <summary>Feature suggestion: title, idea, why, a category and the Discord name. Nothing about the PC is sent.</summary>
    void BuildIdeaForm(VBoxContainer form)
    {
        titleEdit = Line("Short title, e.g. \"Drill with crouch-spamming bots\"", BugReport.TitleMax);
        Head(form, "SHORT TITLE", true, null, () => titleEdit.Text.Trim().Length, BugReport.TitleMin, BugReport.TitleMax);
        form.AddChild(titleEdit);
        whatEdit = Area("What should ValTrainer have? A drill, a setting, a map spot…", BugReport.WhatMax, 124);
        Head(form, "WHAT'S YOUR IDEA?", true, null, () => whatEdit.Text.Trim().Length, BugReport.WhatMin, BugReport.WhatMax);
        form.AddChild(whatEdit);
        stepsEdit = Area("What would it help you practice or fix?", BugReport.StepsMax, 88);
        Head(form, "WHY WOULD IT HELP?", false, "optional", () => stepsEdit.Text.Trim().Length, 0, BugReport.StepsMax);
        form.AddChild(stepsEdit);

        form.AddChild(new SectionLabel { Text = "CATEGORY", Note = "optional", K = k, CustomMinimumSize = new Vector2(0, 26 * k) });
        var cats = new HFlowContainer();
        cats.AddThemeConstantOverride("h_separation", (int)(8 * k));
        cats.AddThemeConstantOverride("v_separation", (int)(8 * k));
        foreach (var (key, label) in BugReport.IdeaCategories)
        {
            var b = new VButton { Label = label, Kind = VButton.Look.Secondary, K = k, FontPx = 15, CustomMinimumSize = new Vector2(128 * k, 40 * k) };
            b.Pressed += () => PickCategory(category == key ? "" : key);
            categoryButtons.Add(b);
            cats.AddChild(b);
        }
        form.AddChild(cats);

        contactEdit = Line("Your Discord username", BugReport.ContactMax);
        Head(form, "DISCORD USERNAME", false, "optional, so we can reply", () => contactEdit.Text.Trim().Length, 0, BugReport.ContactMax);
        form.AddChild(contactEdit);
        form.AddChild(Text($"Sent with it: ValTrainer {AppInfo.Version} ({BugReport.InstallType()}). Nothing about your PC.", 13, UiTheme.Faint));
    }

    /// <summary>Selects one category ("" = none); the picked button turns Primary.</summary>
    void PickCategory(string key)
    {
        category = key;
        for (int i = 0; i < categoryButtons.Count; i++)
        {
            categoryButtons[i].Kind = BugReport.IdeaCategories[i].Key == key ? VButton.Look.Primary : VButton.Look.Secondary;
            categoryButtons[i].QueueRedraw();
        }
        Edited();
    }

    /// <summary>Bug report: the player's fields on the left, everything sent automatically on the right.</summary>
    void BuildBugForm(VBoxContainer form, HBoxContainer body)
    {
        titleEdit = Line("Short title, e.g. \"Crosshair is missing in Gridshot\"", BugReport.TitleMax);
        Head(form, "SHORT TITLE", true, null, () => titleEdit.Text.Trim().Length, BugReport.TitleMin, BugReport.TitleMax);
        form.AddChild(titleEdit);
        whatEdit = Area("What went wrong? Error messages, what you saw on screen…", BugReport.WhatMax, 124);
        Head(form, "WHAT HAPPENED?", true, null, () => whatEdit.Text.Trim().Length, BugReport.WhatMin, BugReport.WhatMax);
        form.AddChild(whatEdit);
        stepsEdit = Area("1. Open …  2. Click …  3. …", BugReport.StepsMax, 88);
        Head(form, "STEPS TO REPRODUCE", false, "optional", () => stepsEdit.Text.Trim().Length, 0, BugReport.StepsMax);
        form.AddChild(stepsEdit);
        expectedEdit = Area("What should have happened instead?", BugReport.ExpectedMax, 64);
        Head(form, "WHAT DID YOU EXPECT?", false, "optional", () => expectedEdit.Text.Trim().Length, 0, BugReport.ExpectedMax);
        form.AddChild(expectedEdit);
        contactEdit = Line("Your Discord username", BugReport.ContactMax);
        Head(form, "DISCORD USERNAME", false, "optional, so we can reply", () => contactEdit.Text.Trim().Length, 0, BugReport.ContactMax);
        form.AddChild(contactEdit);

        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        side.AddThemeConstantOverride("separation", (int)(4 * k));
        body.AddChild(side);
        side.AddChild(new SectionLabel { Text = "WHAT GETS SENT", Note = "with your text above, nothing else", K = k, CustomMinimumSize = new Vector2(0, 24 * k) });
        preview = new TextEdit
        {
            Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsVertical = SizeFlags.ExpandFill,
            ContextMenuEnabled = false, HighlightCurrentLine = false, DeselectOnFocusLossEnabled = true, DragAndDropSelectionEnabled = false,
            MiddleMousePasteEnabled = false, VirtualKeyboardEnabled = false, CaretBlink = false, FocusMode = FocusModeEnum.Click,
            Text = BugReport.PreviewText(snap),
        };
        Style(preview, 12.5f);
        preview.AddThemeColorOverride("font_readonly_color", new Color(0.8f, 0.79f, 0.76f));
        side.AddChild(preview);
    }

    public override void _Process(double delta)
    {
        if (copiedFlash > 0 && (copiedFlash -= (float)delta) <= 0 && state is State.Editing) { message = null; Refresh(); }
    }

    // ---------------- actions ----------------

    void Send()
    {
        if (state == State.Sending) return;
        if (Problem() is { } problem) { Say(problem, UiTheme.Warn); return; }
        if (!DryRun && !HasRelay)
        {
            DisplayServer.ClipboardSet(CopyText());
            OpenDiscord();
            Say($"Copied. Paste it in {Channel} on the ValTrainer Discord (Ctrl+V).", UiTheme.Good);
            discord.Visible = true;
            return;
        }
        double wait = MinGapSec - (DateTime.UtcNow - (idea ? lastIdea : lastSend)).TotalSeconds;
        if (wait > 0) { Say($"You just sent {(idea ? "a suggestion" : "a report")}. You can send another in {Math.Ceiling(wait):0} s.", UiTheme.Warn); return; }
        string json = idea ? BugReport.IdeaJson(titleEdit.Text, whatEdit.Text, stepsEdit.Text, category, contactEdit.Text) : BugReport.ToJson(snap, Fields);
        if (DryRun)
        {
            GD.Print("[bug-report] dev dry run, not sent (" + Encoding.UTF8.GetByteCount(json) + " bytes):\n" + json);
            state = State.DryRun;
            Say("Dev: not sent. The JSON is in the log (add --report-url <url> to send it).", UiTheme.Warn);
            // dev screenshots of the "sent" state without posting anything
            if (CmdLine.Has("--bugreport-fake-sent")) Sent(Encoding.UTF8.GetBytes("{\"ok\":true,\"id\":\"1560414622480334859\"}"));
            return;
        }
        http = new HttpRequest { Timeout = TimeoutSec };
        AddChild(http);
        http.RequestCompleted += Done;
        var err = http.Request(Url, new[] { "Content-Type: application/json", $"X-ValTrainer-Client: ValTrainer/{AppInfo.Version}" }, Godot.HttpClient.Method.Post, json);
        if (err != Error.Ok)
        {
            Fail($"couldn't start the request: {err}");
            return;
        }
        Log.Info($"Bug report: sending {Encoding.UTF8.GetByteCount(json)} bytes to {HostOf(Url)}");
        state = State.Sending;
        message = "Sending…";
        messageColor = UiTheme.Dim;
        Refresh();
    }

    void Done(long result, long code, string[] headers, byte[] body)
    {
        http?.QueueFree();
        http = null;
        if (!IsInsideTree()) return;
        if (result != (long)HttpRequest.Result.Success)
        {
            Fail((HttpRequest.Result)result switch
            {
                HttpRequest.Result.Timeout => "no answer within 15 s",
                HttpRequest.Result.CantResolve or HttpRequest.Result.CantConnect or HttpRequest.Result.ConnectionError => "no connection",
                HttpRequest.Result.TlsHandshakeError => "secure connection failed",
                var r => r.ToString(),
            });
            return;
        }
        if (idea) lastIdea = DateTime.UtcNow; else lastSend = DateTime.UtcNow; // the relay answered: counts as a send either way
        Log.Info($"Bug report: relay answered HTTP {code}");
        if (code is >= 200 and < 300) { Sent(body); return; }
        if (code == 429)
        {
            state = State.Failed;
            int min = RetryMinutes(headers, body);
            Say(idea ? $"Too many ideas, try again in {min} min. You can also COPY IDEA and post it in #suggestions."
                : $"Too many reports, try again in {min} min. You can also COPY REPORT and post it in #bug-reports.", UiTheme.Warn);
            return;
        }
        Fail($"HTTP {code}");
    }

    /// <summary>The relay posted it: invite the player to the Discord to follow their report (and link the post itself).</summary>
    void Sent(byte[] body)
    {
        state = State.Sent;
        postId = PostId(body);
        discord.Label = "JOIN DISCORD";
        discord.Kind = VButton.Look.Primary;
        discord.Visible = true;
        myPost.Visible = postId != null;
        if (copyBtn != null) copyBtn.Visible = false; // sent: what matters now is following it on Discord
        if (ghBtn != null) ghBtn.Visible = false;
        Say(idea ? "Sent, thanks! Join the ValTrainer Discord to follow it: we reply in your post, and it's marked Added when an update adds it."
            : "Sent, thanks! Join the ValTrainer Discord to follow it: we reply in your post, and it's marked Fixed when an update fixes it.", UiTheme.Good);
    }

    /// <summary>The new post's thread id from the relay's {"ok":true,"id":"..."}; null if missing or not numeric.</summary>
    static string? PostId(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: >= 15 and <= 25 } s && s.All(char.IsAsciiDigit)) return s;
        }
        catch { }
        return null;
    }

    /// <summary>Retry-After (seconds) or {"retry_after": seconds} from the relay, in whole minutes (at least 1).</summary>
    static int RetryMinutes(string[] headers, byte[] body)
    {
        double sec = -1;
        foreach (var h in headers)
            if (h.StartsWith("Retry-After:", StringComparison.OrdinalIgnoreCase) && double.TryParse(h[12..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) sec = v;
        if (sec < 0)
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("retry_after", out var ra) && ra.TryGetDouble(out var v)) sec = v;
            }
            catch { }
        return sec < 0 ? 10 : Math.Max(1, (int)Math.Ceiling(sec / 60));
    }

    void Fail(string why)
    {
        Log.Error($"Bug report: couldn't send ({why})");
        state = State.Failed;
        Say($"Couldn't send ({why}). {(idea ? "COPY IDEA" : "COPY REPORT")} and post it in {Channel} on Discord instead.", UiTheme.Warn);
    }

    void Copy()
    {
        DisplayServer.ClipboardSet(CopyText());
        copiedFlash = 4f;
        Say(state is State.Failed ? $"Copied. Paste it in {Channel} on the ValTrainer Discord (Ctrl+V)." : "Copied. Paste it anywhere (Ctrl+V).", UiTheme.Good);
    }

    void OpenDiscord()
    {
        if (Main.I.Dev) Log.Info($"Bug report: dev run, would open {AppInfo.DiscordUrl}");
        else if (!AppInfo.OpenSupport(AppInfo.DiscordUrl)) Log.Error($"Bug report: couldn't open {AppInfo.DiscordUrl}");
    }

    void Say(string text, Color c)
    {
        message = text;
        messageColor = c;
        Refresh();
    }

    /// <summary>Buttons, counters and the status line for the current state.</summary>
    void Refresh()
    {
        var problem = Problem();
        send.Disabled = state is State.Sending or State.Sent || problem != null;
        if (state == State.Sending) send.Label = "SENDING…";
        else if (state == State.Sent) send.Label = "SENT";
        else if (DryRun || HasRelay) send.Label = "SEND";
        discord.Visible = discord.Visible || state is State.Sent or State.Failed;
        cancel.Label = state == State.Sent ? "CLOSE" : "CANCEL";
        status.Text = message ?? problem ?? (DryRun ? $"Dev run: SEND prints the {(idea ? "idea" : "report")} to the log instead of sending it." : "");
        status.LabelSettings = UiTheme.Label(UiTheme.Body, 15, k, message != null ? messageColor : UiTheme.Faint);
        foreach (var c in counters) c.QueueRedraw();
        send.QueueRedraw();
        cancel.QueueRedraw();
    }

    void Edited()
    {
        // typing after a result starts over (a failed or dry-run send can be retried)
        if (state is State.Failed or State.DryRun) state = State.Editing;
        if (state == State.Editing) message = null;
        Refresh();
    }

    bool closed;

    void Close()
    {
        if (closed || !IsInsideTree()) return;
        closed = true;
        if (state == State.Sending) Log.Info("Bug report: closed while sending (cancelled)");
        UiTheme.ClickSound();
        QueueFree();
        onClose();
    }

    // ---------------- building blocks ----------------

    static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

    /// <summary>"WHAT HAPPENED? *  ……  12 / 1500": red star for required fields, the counter turns yellow below the
    /// minimum and red at the maximum.</summary>
    void Head(VBoxContainer parent, string label, bool required, string? note, Func<int> count, int min, int max)
    {
        var d = new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 26 * k),
            OnDraw = b =>
            {
                float cy = b.Size.Y / 2 + 2 * k;
                int fs = UiTheme.Fs(13, k), ns = UiTheme.Fs(13, k);
                Gfx.Text(b, UiTheme.HudWide, label, 0, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
                float x = Gfx.TextW(UiTheme.HudWide, label, fs) + 6 * k;
                if (required) Gfx.Text(b, UiTheme.Body, "*", x, Gfx.Mid(cy, fs), fs, UiTheme.Accent);
                else if (note != null) Gfx.Text(b, UiTheme.Body, note, x + 4 * k, Gfx.Mid(cy, ns), ns, UiTheme.Faint);
                int n = count();
                var c = n >= max ? UiTheme.Accent : required && n < min ? UiTheme.Warn : UiTheme.Faint;
                if (n == 0 && required) c = UiTheme.Faint;
                Gfx.TextR(b, UiTheme.Body, $"{n} / {max}", b.Size.X, Gfx.Mid(cy, ns), ns, c);
            },
        };
        counters.Add(d);
        parent.AddChild(d);
    }

    LineEdit Line(string placeholder, int max)
    {
        var e = new LineEdit
        {
            PlaceholderText = placeholder, MaxLength = max, CustomMinimumSize = new Vector2(0, 42 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ContextMenuEnabled = true,
        };
        Style(e, 16);
        e.TextChanged += _ => Edited();
        return e;
    }

    TextEdit Area(string placeholder, int max, float h)
    {
        var e = new TextEdit
        {
            PlaceholderText = placeholder, WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, h * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, HighlightCurrentLine = false, CaretBlink = true,
        };
        Style(e, 15);
        e.TextChanged += () =>
        {
            if (e.Text.Length > max)
            {
                e.Text = e.Text[..max]; // TextEdit has no MaxLength: cut pastes / typing at the limit
                e.SetCaretLine(e.GetLineCount() - 1);
                e.SetCaretColumn(e.GetLine(e.GetLineCount() - 1).Length);
            }
            Edited();
        };
        return e;
    }

    /// <summary>Dark field with a thin border that turns red while focused (same look as the Lock-In fields).</summary>
    void Style(Control e, float px)
    {
        StyleBoxFlat Box(Color border, bool fill) => new()
        {
            BgColor = new Color(0.03f, 0.06f, 0.09f, 0.7f), DrawCenter = fill, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 12 * k, ContentMarginRight = 8 * k, ContentMarginTop = 6 * k, ContentMarginBottom = 6 * k,
        };
        e.AddThemeStyleboxOverride("normal", Box(new Color(UiTheme.Text, 0.16f), true));
        e.AddThemeStyleboxOverride("read_only", Box(new Color(UiTheme.Text, 0.08f), true));
        e.AddThemeStyleboxOverride("focus", Box(UiTheme.Accent, false));
        e.AddThemeFontOverride("font", UiTheme.Body);
        e.AddThemeFontSizeOverride("font_size", UiTheme.Fs(px, k));
        e.AddThemeColorOverride("font_color", UiTheme.Text);
        e.AddThemeColorOverride("font_placeholder_color", UiTheme.Faint);
        e.AddThemeColorOverride("caret_color", UiTheme.Accent);
        e.AddThemeColorOverride("selection_color", new Color(UiTheme.Accent, 0.35f));
        if (e is TextEdit) e.AddThemeConstantOverride("line_spacing", (int)(2 * k));
    }

    VButton Button(string label, VButton.Look look, float w, Action onClick)
    {
        var b = new VButton { Label = label, Kind = look, K = k, FontPx = 18, CustomMinimumSize = new Vector2(w * k, 48 * k) };
        b.Pressed += onClick;
        return b;
    }

    Label Text(string s, float px, Color c) => new()
    {
        Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        LabelSettings = UiTheme.Label(UiTheme.Body, px, k, c), MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(40 * k, 0),
    };

    /// <summary>Esc = CANCEL (also while typing); Tab / Shift+Tab move between the fields (TextEdit would insert a tab).</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        if (key.Keycode == Key.Escape && !key.Echo)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
        else if (key.Keycode == Key.Tab && GetViewport().GuiGetFocusOwner() is TextEdit { Editable: true } te && IsAncestorOf(te))
        {
            GetViewport().SetInputAsHandled();
            (key.ShiftPressed ? te.FindPrevValidFocus() : te.FindNextValidFocus())?.GrabFocus();
        }
    }
}
