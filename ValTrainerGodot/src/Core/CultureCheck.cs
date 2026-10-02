using System.Globalization;
using System.Text;
using System.Text.Json;
using ValTrainer.Game;
using ValTrainer.Valorant;

namespace ValTrainer.Core;

/// <summary>
/// Dev self-test: <c>--dev --culture-test [--culture de-DE]</c> (works with --headless). Feeds Valorant-style ini
/// files, settings, stats and telemetry through the real loaders inside a throw-away folder with a non-ASCII name and
/// checks every number. Parser checks must pass in ANY culture (they use the invariant culture explicitly); display
/// checks need the app-wide invariant culture from <see cref="Boot"/>, so with --culture they only report what a
/// comma-decimal PC would see without it. Never touches the user's real data or Valorant folders. Prints
/// "[culture-test] …" lines; the process exit code is the number of failures (0 = pass).
/// </summary>
public static class CultureCheck
{
    static int pass, fail;

    public static int Run()
    {
        pass = fail = 0;
        var culture = CultureInfo.CurrentCulture;
        bool fixActive = culture.Name == "";
        Say($"system culture '{Boot.SystemCulture}', app culture '{(culture.Name == "" ? "invariant" : culture.Name)}' " +
            $"(decimal '{culture.NumberFormat.NumberDecimalSeparator}'), worker-thread culture '{Task.Run(() => CultureInfo.CurrentCulture.Name).Result}'");

        string root = Path.Combine(Path.GetTempPath(), "vt culture test ç עברית", Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            ValorantChecks(root);
            CorruptChecks(root);
            DataChecks(root);
            DisplayChecks(fixActive);
        }
        catch (Exception e) { Fail("unexpected exception", e.ToString()); }
        finally
        {
            Paths.SetValorantDirForTests(null);
            Paths.SetDataDirForTests(null);
            try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { }
        }
        Say($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed) culture={(culture.Name == "" ? "invariant" : culture.Name)}");
        return fail;
    }

    // ---------------- Valorant ini import ----------------

    static void ValorantChecks(string root)
    {
        string val = Path.Combine(root, "VALORANT", "Saved", "Config");
        Paths.SetValorantDirForTests(val);

        // Not installed / empty folder.
        var none = ValorantImporter.FindAccounts();
        Check("no VALORANT folder -> NotInstalled", none.Count == 0 && ValorantImporter.LastScan == ValorantStatus.NotInstalled, ValorantImporter.LastScan.ToString());
        var p0 = ValorantImporter.Load(null);
        Check("no VALORANT folder -> defaults + message", !p0.Found && p0.NotFoundMessage != null && p0.Sensitivity == 1f, p0.NotFoundMessage ?? "null");
        Directory.CreateDirectory(val);
        ValorantImporter.FindAccounts();
        Check("empty config folder -> NoAccounts", ValorantImporter.LastScan == ValorantStatus.NoAccounts, ValorantImporter.LastScan.ToString());

        // A realistic account (values exactly as Valorant writes them: '.' decimals, UE prefixes, quoted JSON).
        string acc = Path.Combine(val, "0123abcd-5678-9abc-def0-123456789abc-eu");
        Write(Path.Combine(val, "WindowsClient", "RiotLocalMachine.ini"), "[UserInfo]\r\nLastKnownUser=0123abcd-5678-9abc-def0-123456789abc\r\n");
        string xhJson = "{\"currentProfile\":0,\"profiles\":[{\"profileName\":\"Pro\",\"primary\":{\"color\":{\"r\":0,\"g\":255,\"b\":255,\"a\":255}," +
                        "\"bDisplayCenterDot\":true,\"centerDotSize\":1.5,\"centerDotOpacity\":0.75,\"outlineOpacity\":0.35," +
                        "\"innerLines\":{\"lineThickness\":2,\"lineLength\":4.5,\"lineOffset\":2.25,\"opacity\":0.8}}}]}";
        string xhUe = "\"" + xhJson.Replace("\"", "\\\"") + "\"";
        Write(Path.Combine(acc, "Windows", "RiotUserSettings.ini"),
            "[/Script/ShooterGame.ShooterGameUserSettings]\r\n" +
            "EAresFloatSettingName::MouseSensitivity=0.352000\r\n" +
            "EAresFloatSettingName::MouseSensitivityZoomed=0.875000\r\n" +
            "EAresFloatSettingName::MouseSensitivityADS=1.250000\r\n" +
            "EAresBoolSettingName::HoldInputForSniperScopes=True\r\n" +
            "EAresIntSettingName::ColorBlindMode=0\r\n" +
            "EAresEnumSettingName::EnemyHighlightColor=3\r\n" +
            "EAresStringSettingName::SavedCrosshairProfileData=" + xhUe + "\r\n");
        Write(Path.Combine(acc, "WindowsClient", "GameUserSettings.ini"),
            "[/Script/ShooterGame.ShooterGameUserSettings]\r\nResolutionSizeX=1680\r\nResolutionSizeY=1050\r\nFullscreenMode=2\r\n" +
            "FrameRateLimit=144.500000\r\nbUseVSync=False\r\n");

        var accounts = ValorantImporter.FindAccounts();
        Check("account found", accounts.Count == 1 && ValorantImporter.LastScan == ValorantStatus.Ok, $"{accounts.Count} {ValorantImporter.LastScan}");
        Check("account marked last used", accounts.Count == 1 && accounts[0].IsLastUsed);
        Check("account list sens", accounts.Count == 1 && accounts[0].Sens is { } ls && Near(ls, 0.352f), accounts.FirstOrDefault()?.Sens?.ToString(CultureInfo.InvariantCulture) ?? "null");
        var p = ValorantImporter.Load(accounts.FirstOrDefault());
        Check("profile found", p.Found && p.Status == ValorantStatus.Ok && p.NotFoundMessage == null);
        Check("sensitivity 0.352", Near(p.Sensitivity, 0.352f) && p.SensFromFile, F(p.Sensitivity));
        Check("scoped mult 0.875", Near(p.ZoomedSensMult, 0.875f), F(p.ZoomedSensMult));
        Check("ADS mult 1.25", Near(p.AdsSensMult, 1.25f), F(p.AdsSensMult));
        Check("hold to scope", p.HoldToScope);
        Check("enemy highlight 3", p.EnemyHighlight == 3, p.EnemyHighlight.ToString(CultureInfo.InvariantCulture));
        Check("resolution 1680x1050", p.ResX == 1680 && p.ResY == 1050, $"{p.ResX}x{p.ResY}");
        Check("frame limit 144.5", Near(p.FrameRateLimit, 144.5f), F(p.FrameRateLimit));
        Check("window mode 2", p.WindowMode == 2);
        var xh = p.Crosshair.Primary;
        Check("crosshair from profile JSON", p.Crosshair.Name == "Pro", p.Crosshair.Name);
        Check("crosshair dot size 1.5", Near(xh.CenterDotSize, 1.5f), F(xh.CenterDotSize));
        Check("crosshair inner length 4.5 / offset 2.25", Near(xh.Inner.Length, 4.5f) && Near(xh.Inner.Offset, 2.25f), $"{F(xh.Inner.Length)} {F(xh.Inner.Offset)}");
        Check("crosshair outline opacity 0.35", Near(xh.OutlineOpacity, 0.35f), F(xh.OutlineOpacity));

        // Older builds: individual crosshair keys.
        var legacy = CrosshairSettings.FromLegacyKeys(new Dictionary<string, string>
        {
            ["CrosshairColor"] = "(R=0,G=255,B=0,A=255)", ["CrosshairInnerLinesOpacity"] = "0.500000",
            ["CrosshairInnerLinesLineLength"] = "6.000000", ["CrosshairCenterDotSize"] = "2.500000",
        });
        Check("legacy crosshair values", Near(legacy.Primary.Inner.Opacity, 0.5f) && Near(legacy.Primary.Inner.Length, 6f)
                                         && Near(legacy.Primary.CenterDotSize, 2.5f) && legacy.Primary.Color.G8 == 255 && legacy.Primary.Color.R8 == 0,
            $"{F(legacy.Primary.Inner.Opacity)} {F(legacy.Primary.Inner.Length)} {F(legacy.Primary.CenterDotSize)}");
    }

    // ---------------- corrupt files ----------------

    static void CorruptChecks(string root)
    {
        string val = Path.Combine(root, "VALORANT corrupt", "Saved", "Config");
        Paths.SetValorantDirForTests(val);
        string acc = Path.Combine(val, "deadbeef-na");
        // Garbage bytes, absurd numbers, comma decimals (what a broken tool might write), NaN, overflowing colours.
        Directory.CreateDirectory(Path.Combine(acc, "Windows"));
        File.WriteAllBytes(Path.Combine(val, "garbage.bin"), new byte[] { 0, 1, 2, 255, 254, 0, 13, 10 });
        Write(Path.Combine(acc, "Windows", "RiotUserSettings.ini"),
            "\0\0garbage\r\n[broken\r\n=novalue\r\nEAresFloatSettingName::MouseSensitivity=NaN\r\n" +
            "EAresFloatSettingName::MouseSensitivityZoomed=0,5\r\nEAresStringSettingName::SavedCrosshairProfileData=\"{not json\r\n" +
            "CrosshairColor=(R=99999999999999,G=255,B=0,A=255)\r\nEAresEnumSettingName::EnemyHighlightColor=-7\r\n");
        Write(Path.Combine(acc, "WindowsClient", "GameUserSettings.ini"),
            "ResolutionSizeX=99999999999\r\nResolutionSizeY=-5\r\nFrameRateLimit=-1e30\r\nDefaultMonitorIndex=4000000000\r\n");
        Write(Path.Combine(acc, "WindowsClient", "BackupKeybinds.json"), "{ \"actionMappings\": [ { broken");
        Write(Path.Combine(val, "WindowsClient", "RiotLocalMachine.ini"), "LastKnownUser=\r\n");
        ValorantProfile? p = null;
        try
        {
            var accounts = ValorantImporter.FindAccounts();
            p = ValorantImporter.Load(accounts.FirstOrDefault());
            Check("corrupt ini doesn't throw", true);
        }
        catch (Exception e) { Fail("corrupt ini doesn't throw", e.Message); }
        if (p == null) return;
        Check("corrupt: sens falls back to 1.0", p.Sensitivity == 1f && !p.SensFromFile, F(p.Sensitivity));
        Check("corrupt: comma-decimal value ignored", Near(p.ZoomedSensMult, 1f), F(p.ZoomedSensMult));
        Check("corrupt: resolution sane", p.ResX == 1920 && p.ResY == 1080, $"{p.ResX}x{p.ResY}");
        Check("corrupt: frame limit sane", p.FrameRateLimit == 0f, F(p.FrameRateLimit));
        Check("corrupt: monitor/highlight sane", p.MonitorIndex == 0 && p.EnemyHighlight == 0, $"{p.MonitorIndex} {p.EnemyHighlight}");
        var c = CrosshairSettings.ParseUeColor("(R=99999999999999,G=255,B=0,A=255)");
        Check("corrupt: overflowing colour parses", c is { } cc && cc.G8 == 255, c?.ToString() ?? "null");
    }

    // ---------------- settings / stats / telemetry round trips ----------------

    static void DataChecks(string root)
    {
        string data = Path.Combine(root, "data ValTrainer");
        Paths.SetDataDirForTests(data);
        Check("data dir redirected", Paths.DataDir == data, Paths.DataDir);
        bool ro = AppSettings.ReadOnly;
        try
        {
            AppSettings.ReadOnly = false;
            // Missing files -> fresh defaults.
            var fresh = AppSettings.Load();
            Check("no settings file -> IsNew", fresh.IsNew);
            Check("no stats file -> empty", StatsStore.Load().Runs.Count == 0);

            var s = new AppSettings { SensOverride = 0.275f, UseSensOverride = true, Volume = 0.35f, ViewmodelFov = 68.5f, Quality = 2 };
            s.Save();
            string json = File.ReadAllText(Path.Combine(data, "settings.json"));
            Check("settings.json uses '.' decimals", json.Contains("0.275") && !json.Contains("0,275"), Snip(json, "SensOverride"));
            var s2 = AppSettings.Load();
            Check("settings round trip", !s2.IsNew && Near(s2.SensOverride, 0.275f) && Near(s2.Volume, 0.35f) && Near(s2.ViewmodelFov, 68.5f) && s2.Quality == 2,
                $"{F(s2.SensOverride)} {F(s2.Volume)} {F(s2.ViewmodelFov)} q{s2.Quality}");

            // A settings file written by hand on a comma-decimal PC isn't valid JSON for numbers: must not crash.
            File.WriteAllText(Path.Combine(data, "settings.json"), "{ \"SensOverride\": 0,5, \"Quality\": 3 }");
            var bad = AppSettings.Load();
            Check("corrupt settings.json -> defaults, kept as .corrupt", bad.IsNew && File.Exists(Path.Combine(data, "settings.json.corrupt")));

            var st = new StatsStore();
            st.Runs.Add(new RunRecord
            {
                Mode = "flick", When = new DateTime(2026, 10, 2, 13, 45, 0), Score = 12345, Accuracy = 0.875f, AvgKillMs = 412.5f,
                HeadshotPct = 0.25f, Sens = 0.352f, Tier = 2, Metrics = new() { ["overshoot"] = 1.25f },
            });
            st.Save();
            var st2 = StatsStore.Load();
            var r = st2.Runs.FirstOrDefault();
            Check("stats round trip", r != null && r.Score == 12345 && Near(r.Accuracy, 0.875f) && Near(r.AvgKillMs, 412.5f) && Near(r.Sens, 0.352f)
                                      && r.Metrics != null && Near(r.Metrics["overshoot"], 1.25f) && r.When == new DateTime(2026, 10, 2, 13, 45, 0),
                r == null ? "null" : $"{r.Score} {F(r.Accuracy)} {F(r.AvgKillMs)} {r.When:O}");
            File.WriteAllText(Path.Combine(data, "stats.json"), "{\"Runs\":[{\"Mode\":\"flick\",\"Score\":");
            Check("truncated stats.json -> empty, no crash", StatsStore.Load().Runs.Count == 0);

            var t = new RunTelemetry { Mode = "flick", Tier = 2, Sens = 0.352f, Dpi = 800, Weapon = "Vandal", Map = "range", Duration = 12.5f, When = new DateTime(2026, 10, 2, 13, 45, 7) };
            t.Frames.Add(new FrameSample { T = 0.25f, Yaw = 12.5f, Pitch = -3.75f, FocusRadius = 0.6f });
            t.Shots.Add(new ShotSample { T = 0.5f, Zone = 0, ErrYaw = 0.125f });
            t.Events.Add(new TelemetryEvent(0.5f, "kill", 412.5f, 1));
            string file = t.Save(Paths.DataSubDir("telemetry"));
            var t2 = RunTelemetry.Load(file);
            Check("telemetry file name is ASCII digits", Path.GetFileName(file) == "20261002_134507_flick.vtt", Path.GetFileName(file));
            Check("telemetry round trip", t2 != null && Near(t2.Sens, 0.352f) && Near(t2.Duration, 12.5f) && t2.Frames.Count == 1 && Near(t2.Frames[0].Pitch, -3.75f)
                                          && t2.Events.Count == 1 && Near(t2.Events[0].A, 412.5f) && t2.When == t.When,
                t2 == null ? "null" : $"{F(t2.Sens)} {F(t2.Duration)}");

            // A file that exists but can't be opened (locked by OneDrive/antivirus, no permission) must never be
            // overwritten with an empty history.
            st.Save();
            string statsPath = Path.Combine(data, "stats.json");
            string before = File.ReadAllText(statsPath);
            StatsStore locked;
            using (new FileStream(statsPath, FileMode.Open, FileAccess.Read, FileShare.None)) locked = StatsStore.Load();
            locked.Runs.Add(new RunRecord { Mode = "gridshot" });
            locked.Save();
            Check("locked stats.json is not overwritten", File.ReadAllText(statsPath) == before && locked.Runs.Count == 1);

            // Unwritable target: Save must report, not throw.
            string blocker = Path.Combine(data, "blocked");
            File.WriteAllText(blocker, "a file where a folder should be");
            bool ok = Paths.TryWriteAllText(Path.Combine(blocker, "x.json"), "{}");
            Check("unwritable path -> false + LastWriteError, no throw", !ok && Paths.LastWriteError != null, Paths.LastWriteError ?? "null");
        }
        catch (Exception e) { Fail("data round trips threw", e.ToString()); }
        finally { AppSettings.ReadOnly = ro; }
    }

    // ---------------- display ----------------

    static void DisplayChecks(bool fixActive)
    {
        float v = 0.352f;
        string interp = $"{v:0.000}";
        string plain = v.ToString();
        string pct = $"{0.875f:P0}";
        string n = $"{12345:#,0}";
        bool ok = interp == "0.352" && plain == "0.352" && n == "12,345";
        string detail = $"\"{interp}\" \"{plain}\" \"{n}\" \"{pct}\"";
        if (fixActive) Check("on-screen numbers use '.' decimals", ok, detail);
        else Say($"INFO without the invariant-culture fix numbers would read {detail}");
        // What a culture-sensitive parse (anywhere left in the code) would do with Valorant's "0.352000".
        bool naive = float.TryParse("0.352000", out var nv) && Near(nv, 0.352f);
        if (fixActive) Check("culture-default float.Parse(\"0.352000\")", naive, F(nv));
        else Say($"INFO culture-default float.Parse(\"0.352000\") would give {F(nv)} here");
    }

    // ---------------- helpers ----------------

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static string Snip(string s, string key)
    {
        int i = s.IndexOf(key, StringComparison.Ordinal);
        return i < 0 ? "" : s.Substring(i, Math.Min(40, s.Length - i)).Replace("\n", " ").Replace("\r", "");
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { pass++; Say($"PASS {name}"); }
        else Fail(name, detail);
    }

    static void Fail(string name, string detail) { fail++; Say($"FAIL {name}: {detail}"); }

    static void Say(string s) => Godot.GD.Print("[culture-test] " + s);
}
