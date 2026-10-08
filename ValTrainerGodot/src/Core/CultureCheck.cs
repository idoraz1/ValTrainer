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
            CrosshairChecks();
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
        Check("no VALORANT folder -> VALORANT's factory crosshair", CrosshairCode.Diff(p0.Crosshair, CrosshairCode.DefaultSettings()) == null
            && CrosshairCode.Encode(p0.Crosshair) == "0", CrosshairCode.Encode(p0.Crosshair));
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
        KeybindChecks(p, acc);

        // Older builds: individual crosshair keys.
        var legacy = CrosshairSettings.FromLegacyKeys(new Dictionary<string, string>
        {
            ["CrosshairColor"] = "(R=0,G=255,B=0,A=255)", ["CrosshairInnerLinesOpacity"] = "0.500000",
            ["CrosshairInnerLinesLineLength"] = "6.000000", ["CrosshairCenterDotSize"] = "2.500000",
        });
        Check("legacy crosshair values", Near(legacy.Primary.Inner.Opacity, 0.5f) && Near(legacy.Primary.Inner.Length, 6f)
                                         && Near(legacy.Primary.CenterDotSize, 2.5f) && legacy.Primary.Color.G8 == 255 && legacy.Primary.Color.R8 == 0,
            $"{F(legacy.Primary.Inner.Opacity)} {F(legacy.Primary.Inner.Length)} {F(legacy.Primary.CenterDotSize)}");

        // An account whose profiles live only in Riot's cloud: no JSON, just the old flat keys (dot only). The importer uses
        // them (with a note) and the dot is drawn.
        Write(Path.Combine(acc, "Windows", "RiotUserSettings.ini"),
            "[/Script/ShooterGame.ShooterGameUserSettings]\r\nEAresFloatSettingName::MouseSensitivity=0.352000\r\n" +
            "EAresBoolSettingName::CrosshairDisplayCenterDot=True\r\nEAresBoolSettingName::CrosshairInnerLinesShowLines=False\r\n" +
            "EAresBoolSettingName::CrosshairOuterLinesShowLines=False\r\nEAresStringSettingName::CrosshairColor=(R=0,G=255,B=255,A=255)\r\n");
        var cloud = ValorantImporter.Load(ValorantImporter.FindAccounts().FirstOrDefault());
        var dot = cloud.Crosshair.Primary;
        Check("no crosshair JSON -> legacy dot-only crosshair + note", cloud.Found && cloud.CrosshairNote != null && dot.CenterDot && !dot.Inner.Show
            && !dot.Outer.Show && dot.Color.B8 == 255 && dot.Color.R8 == 0 && UI.CrosshairView.Raster(dot).Any(r => r.Contains('#')),
            $"{cloud.CrosshairNote} {CrosshairCode.Encode(cloud.Crosshair)}");
    }

    // ---------------- crosshair model (codes, profile JSON, legacy keys) ----------------

    static void CrosshairChecks()
    {
        // Codes: the encoder/decoder self-test (split codes, vertical-only lines, the advanced gate, unknown sections …).
        var fails = CrosshairCode.SelfTest(out int n);
        foreach (var f in fails) Fail("crosshair code: " + f, "");
        Check($"crosshair code self-test ({n} checks)", fails.Count == 0, $"{fails.Count} failed");

        // Profile JSON (synthetic, shaped like VALORANT's SavedCrosshairProfileData): the active profile is a split one.
        const string split = "{\"currentProfile\":1,\"profiles\":[" +
            "{\"profileName\":\"Other\",\"primary\":{\"color\":{\"r\":255,\"g\":0,\"b\":0,\"a\":255}}}," +
            "{\"profileName\":\"Split\",\"bUseAdvancedOptions\":true,\"bUsePrimaryCrosshairForADS\":false,\"bUsePrimaryCrosshairForFocusMode\":false," +
            "\"bUseCustomCrosshairOnAllPrimary\":false,\"bScaleToResolution\":false," +
            "\"primary\":{\"color\":{\"r\":0,\"g\":255,\"b\":255,\"a\":255},\"bUseCustomColor\":false,\"colorCustom\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255}," +
            "\"bHasOutline\":false,\"outlineThickness\":1,\"outlineOpacity\":0.5,\"outlineColor\":{\"r\":0,\"g\":0,\"b\":0,\"a\":255},\"bDisplayCenterDot\":false," +
            "\"centerDotSize\":2,\"centerDotOpacity\":1,\"bFadeCrosshairWithFiringError\":false,\"bShowSpectatedPlayerCrosshair\":true," +
            "\"bFixMinErrorAcrossWeapons\":true,\"bHideCrosshair\":false," +
            "\"innerLines\":{\"bShowLines\":true,\"lineThickness\":2,\"lineLength\":4,\"lineLengthVertical\":6,\"bAllowVertScaling\":false,\"lineOffset\":2," +
            "\"opacity\":1,\"bShowMovementError\":false,\"movementErrorScale\":1,\"bShowShootingError\":false,\"firingErrorScale\":1,\"bShowMinError\":true}," +
            "\"outerLines\":{\"bShowLines\":false,\"lineThickness\":2,\"lineLength\":2,\"lineLengthVertical\":2,\"bAllowVertScaling\":false,\"lineOffset\":10," +
            "\"opacity\":0.34999999403953552,\"bShowMovementError\":true,\"movementErrorScale\":1,\"bShowShootingError\":true,\"firingErrorScale\":1}}," +
            "\"aDS\":{\"color\":{\"r\":255,\"g\":0,\"b\":0,\"a\":255},\"bUseCustomColor\":false,\"colorCustom\":{\"r\":239,\"g\":146,\"b\":191,\"a\":255}," +
            "\"bHasOutline\":true,\"outlineThickness\":1,\"outlineOpacity\":1,\"bDisplayCenterDot\":true,\"centerDotSize\":2,\"centerDotOpacity\":1," +
            "\"bFadeCrosshairWithFiringError\":false,\"bShowSpectatedPlayerCrosshair\":false,\"bFixMinErrorAcrossWeapons\":false," +
            "\"innerLines\":{\"bShowLines\":false},\"outerLines\":{\"bShowLines\":false}}," +
            "\"focusMode\":{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0},\"bHasOutline\":false,\"outlineThickness\":0,\"outlineOpacity\":0,\"centerDotSize\":0," +
            "\"centerDotOpacity\":0,\"innerLines\":{\"bShowLines\":false,\"lineThickness\":0,\"lineLength\":0,\"opacity\":0},\"outerLines\":{\"bShowLines\":false}}," +
            "\"sniper\":{\"centerDotColor\":{\"r\":255,\"g\":0,\"b\":0,\"a\":255},\"bUseCustomCenterDotColor\":true," +
            "\"centerDotColorCustom\":{\"r\":0,\"g\":255,\"b\":128,\"a\":255},\"bDisplayCenterDot\":true,\"centerDotSize\":0.5,\"centerDotOpacity\":0.80000001192092896}}]}";
        var s = CrosshairSettings.FromProfileJson(split);
        Check("split profile JSON parses (current profile 1)", s is { Name: "Split" }, s?.Name ?? "null");
        if (s != null)
        {
            var ads = s.StyleFor(UI.CrosshairView.Mode.Ads);
            var hip = s.StyleFor(UI.CrosshairView.Mode.Primary);
            var dot = s.SniperDotFor();
            Check("split JSON: advanced on, own ADS = red dot, hip = cyan lines", s.UseAdvancedOptions && s.AdsIsOwn && ads == s.Ads && ads.CenterDot
                && ads.Color.R8 == 255 && ads.Color.G8 == 0 && !ads.Inner.Show && hip == s.Primary && hip.Color.G8 == 255 && hip.Color.R8 == 0 && hip.Inner.Show);
            Check("split JSON: custom sniper dot", dot.Show && dot.Color.G8 == 255 && dot.Color.B8 == 128 && dot.Color.R8 == 0 && Near(dot.Size, 0.5f) && Near(dot.Opacity, 0.8f),
                dot.ToString());
            Check("split JSON: fade / firing-offset flags read", !s.Extras.Primary.Fade && s.Extras.Primary.OverrideFiringOffset && s.Extras.Ads.Fade);
            Check("split JSON: focusMode (console only, all zeros) is never drawn", hip.Inner.Opacity > 0 && ads.CenterDotSize > 0);
            const string want = "0;p;0;s;1;P;c;5;h;0;f;0;m;1;0l;4;0o;2;0a;1;0f;0;1b;0;A;c;7;u;EF92BFFF;o;1;d;1;0b;0;1b;0;S;b;1;c;8;t;00FF80FF;s;0.5;o;0.8";
            string got = CrosshairCode.Encode(s);
            Check("split JSON -> the full code VALORANT would export", got == want, got);
            Check("split JSON code decodes to the same crosshair", CrosshairCode.Decode(got) is { } back && CrosshairCode.Diff(back, s) == null);
        }

        // Copy Primary off but Use Advanced Options off: VALORANT ignores the ADS and Sniper tabs.
        const string gated = "{\"currentProfile\":0,\"profiles\":[{\"profileName\":\"P0\",\"bUseAdvancedOptions\":false,\"bUsePrimaryCrosshairForADS\":false," +
            "\"primary\":{\"color\":{\"r\":0,\"g\":255,\"b\":0,\"a\":255}},\"aDS\":{\"color\":{\"r\":255,\"g\":0,\"b\":0,\"a\":255},\"innerLines\":{\"opacity\":0}}," +
            "\"sniper\":{\"centerDotColor\":{\"r\":0,\"g\":255,\"b\":255,\"a\":255},\"centerDotOpacity\":1}}]}";
        var g = CrosshairSettings.FromProfileJson(gated);
        Check("advanced off + copy off: ADS draws the primary, default red sniper dot, code without s;1 / A / S",
            g != null && !g.AdsIsOwn && g.StyleFor(UI.CrosshairView.Mode.Ads) == g.Primary && g.SniperDotFor().Color.R8 == 255
            && g.SniperDotFor().Color.G8 == 0 && Near(g.SniperDotFor().Opacity, 0.75f) && CrosshairCode.Encode(g) == "0;p;0;P;c;1",
            g == null ? "null" : CrosshairCode.Encode(g));

        // A custom colour (bUseCustomColor) exports like the game: c;8 + u + b;1.
        var cust = CrosshairSettings.FromProfileJson("{\"currentProfile\":0,\"profiles\":[{\"primary\":{\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255}," +
            "\"bUseCustomColor\":true,\"colorCustom\":{\"r\":255,\"g\":255,\"b\":204,\"a\":255}}}]}");
        Check("custom colour JSON -> c;8;u;…;b;1", cust != null && CrosshairCode.Encode(cust) == "0;P;c;8;u;FFFFCCFF;b;1", cust == null ? "null" : CrosshairCode.Encode(cust));
        Check("broken / empty profile JSON -> null", CrosshairSettings.FromProfileJson("{not json") == null
            && CrosshairSettings.FromProfileJson("{\"currentProfile\":0,\"profiles\":[]}") == null);

        // Legacy flat keys: only non-default values are written, so a missing key is VALORANT's factory value.
        var lg = CrosshairSettings.FromLegacyKeys(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CrosshairDisplayCenterDot"] = "True", ["CrosshairInnerLinesAllowVertScaling"] = "True", ["CrosshairInnerLinesLineLengthVertical"] = "9.000000",
            ["CrosshairInnerLinesShowShootingError"] = "False", ["CrosshairOuterLinesFiringErrorScale"] = "2.500000", ["FadeCrosshairWithFiringError"] = "False",
        });
        var lp = lg.Primary;
        Check("legacy keys: real dot key, unlinked length, error flags, fade; factory defaults elsewhere",
            lp.CenterDot && lp.Inner.AllowVertScaling && Near(lp.Inner.LengthVertical, 9) && Near(lp.Inner.Length, 6) && !lp.Inner.ShowShootingError
            && Near(lp.Outer.FiringErrorScale, 2.5f) && lp.Outer.ShowMovementError && lp.Outer.ShowShootingError && !lg.Extras.Primary.Fade
            && Near(lp.Inner.Offset, 3) && lp.Color == Godot.Colors.White, CrosshairCode.Encode(lg));
        var none = CrosshairSettings.FromLegacyKeys(new Dictionary<string, string>());
        Check("legacy keys: none written -> VALORANT's factory crosshair", CrosshairCode.Encode(none) == "0", CrosshairCode.Encode(none));
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
        Check("corrupt: unreadable crosshair JSON -> VALORANT's default + note (never the stale flat keys)",
            CrosshairCode.Encode(p.Crosshair) == "0" && p.CrosshairNote != null, $"{CrosshairCode.Encode(p.Crosshair)} {p.CrosshairNote}");
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

    // ---------------- keybinds (BackupKeybinds.json) ----------------

    /// <summary>The file only holds what the player changed: everything else must stay at VALORANT's defaults; "None" =
    /// unbound; bindIndex 1 = the secondary slot; mouse buttons and wheel notches work for any action.</summary>
    static void KeybindChecks(ValorantProfile noFile, string accDir)
    {
        var d = noFile.Binds;
        Check("keybinds: no file -> VALORANT defaults",
            d.Text(GameAction.MoveForward) == "W" && d.Text(GameAction.Walk) == "Shift" && d.Text(GameAction.Crouch) == "Ctrl" &&
            d.Text(GameAction.Jump) == "Space" && d.Text(GameAction.Fire) == "Mouse 1" && d.Text(GameAction.AltFire) == "Mouse 2" &&
            d.Text(GameAction.Reload) == "R" && d.Text(GameAction.UseSpike) == "4" && d.Text(GameAction.Use) == "F" &&
            string.Join("", Enumerable.Range(0, 4).Select(noFile.AbilityBindText)) == "CQEX", d.ListText().Replace('\n', ';'));

        string M(string name, int slot, string key, string extra = "") =>
            $"{{\"name\":\"{name}\",\"characterName\":\"None\",\"bindIndex\":{slot},\"key\":\"{key}\",\"shift\":false,\"ctrl\":false,\"alt\":false,\"cmd\":false,\"tapHoldType\":\"None\"{extra}}}";
        string json = "{\"settingsVersion\":15,\"actionMappings\":[" + string.Join(",",
            M("Crouch", 1, "SpaceBar"), M("VOICE_TeamPTTAction", 0, "ThumbMouseButton"), M("OpenMegamap", 0, "None"),
            M("Jump", 1, "None"), M("Jump", 0, "MouseScrollDown"), M("Reload", 0, "None"), M("UseChannelObject", 0, "Five"),
            M("Activate_Ability1", 0, "ThumbMouseButton2"), M("Walk", 0, "Escape"), M("PrimaryTrigger", 1, "K"), M("SecondaryTrigger", 0, "ThumbMouseButton"), M("DropEquippable", 0, "F"),
            M("Crouch", 0, "C", ",\"characterName\":\"Clay\"").Replace("\"characterName\":\"None\",", "")) +
            "],\"axisMappings\":[],\"settingsProfiles\":[],\"characterProfileData\":[{\"profileName\":\"None\",\"presetIndex\":0}]}";
        Write(Path.Combine(accDir, "WindowsClient", "BackupKeybinds.json"), json);
        var p = ValorantImporter.Load(ValorantImporter.FindAccounts().FirstOrDefault());
        var b = p.Binds;
        Check("keybinds: secondary slot added, primary kept", b.Text(GameAction.Crouch) == "Ctrl · Space", b.Text(GameAction.Crouch));
        Check("keybinds: jump on the wheel only (secondary unbound)", b.Text(GameAction.Jump) == "Wheel down" && b[GameAction.Jump, 0].IsWheel && b[GameAction.Jump, 1].IsNone, b.Text(GameAction.Jump));
        Check("keybinds: \"None\" unbinds", !b.IsBound(GameAction.Reload) && b.Text(GameAction.Reload) == "unbound", b.Text(GameAction.Reload));
        Check("keybinds: digit key names (use spike on 5)", b.Text(GameAction.UseSpike) == "5" && b.Text(GameAction.EquipSpike) == "4", b.Text(GameAction.UseSpike));
        Check("keybinds: ability on a thumb button", b.Text(GameAction.Ability1) == "Mouse 5" && p.AbilityBindText(1) == "Mouse 5", b.Text(GameAction.Ability1));
        Check("keybinds: fire secondary on a key", b.Text(GameAction.Fire) == "Mouse 1 · K", b.Text(GameAction.Fire));
        Check("keybinds: Escape is never a bind (default kept)", b.Text(GameAction.Walk) == "Shift", b.Text(GameAction.Walk));
        Check("keybinds: per-agent binds not applied to everyone", b[GameAction.Crouch, 0].Key == Godot.Key.Ctrl && b.Skipped.Any(s => s.Contains("Clay")),
            string.Join(", ", b.Skipped));
        Check("keybinds: alt fire on a thumb button", b.Text(GameAction.AltFire) == "Mouse 4", b.Text(GameAction.AltFire));
        Check("keybinds: untouched actions keep defaults", b.Text(GameAction.MoveForward) == "W" && b.Text(GameAction.Use) == "F" &&
            p.AbilityBindText(0) == "C" && p.AbilityBindText(3) == "X", b.ListText().Replace('\n', ';'));
        Check("keybinds: shared bind listed", b.SharedBinds().Any(s => s.StartsWith("F: Use + Drop", StringComparison.Ordinal)), string.Join("; ", b.SharedBinds()));

        // Movement as Unreal axes (MoveForward / MoveRight with a scale), the way the game most likely stores it.
        var ax = Keybinds.Defaults();
        ValorantImporter.ApplyKeybindsJson(ax, "{\"actionMappings\":[],\"axisMappings\":[" +
            "{\"name\":\"MoveForward\",\"characterName\":\"None\",\"bindIndex\":0,\"key\":\"Up\",\"scale\":1}," +
            "{\"name\":\"MoveForward\",\"characterName\":\"None\",\"bindIndex\":1,\"key\":\"Down\",\"scale\":-1}," +
            "{\"name\":\"MoveRight\",\"characterName\":\"None\",\"bindIndex\":0,\"key\":\"Left\",\"scale\":-1}," +
            "{\"name\":\"LookUp\",\"characterName\":\"None\",\"bindIndex\":0,\"key\":\"MouseY\",\"scale\":-1}]}");
        Check("keybinds: movement axes", ax.Text(GameAction.MoveForward) == "Up" && ax.Text(GameAction.MoveBack) == "S · Down" &&
            ax.Text(GameAction.StrafeLeft) == "Left" && ax.Text(GameAction.StrafeRight) == "D", ax.ListText().Replace('\n', ';'));

        // Tolerant names (older / undocumented spellings) and modifier flags.
        var t = Keybinds.Defaults();
        ValorantImporter.ApplyKeybindsJson(t, "{\"actionMappings\":[" + string.Join(",",
            M("EquipGrenadeAbility", 0, "One"), M("EquipUltimateAbility", 0, "Four"), M("Use/Equip Ability: 2", 0, "Three"),
            M("ToggleCrouch", 0, "Z"), M("PingAction", 0, "V"), M("AltFireZoom", 0, "ThumbMouseButton"),
            M("Inspect", 0, "Q", ",\"shift\":true").Replace("\"shift\":false,", "")) + "]}");
        Check("keybinds: tolerant ability names", t.Text(GameAction.AbilityGrenade) == "1" && t.Text(GameAction.Ultimate) == "4" && t.Text(GameAction.Ability2) == "3",
            $"{t.Text(GameAction.AbilityGrenade)} {t.Text(GameAction.Ability2)} {t.Text(GameAction.Ultimate)}");
        Check("keybinds: toggle / ping actions ignored", t.Text(GameAction.Crouch) == "Ctrl", t.Text(GameAction.Crouch));
        Check("keybinds: tolerant alt-fire name", t.Text(GameAction.AltFire) == "Mouse 4", t.Text(GameAction.AltFire));
        Check("keybinds: modifier flag", t.Text(GameAction.Inspect) == "Shift+Q", t.Text(GameAction.Inspect));

        // A broken file keeps every default.
        Write(Path.Combine(accDir, "WindowsClient", "BackupKeybinds.json"), "{\"actionMappings\":[{\"name\":\"Jump\",\"key\":");
        var broken = ValorantImporter.Load(ValorantImporter.FindAccounts().FirstOrDefault());
        Check("keybinds: corrupt file -> defaults", broken.Found && broken.Binds.Text(GameAction.Jump) == "Space",broken.Binds.Text(GameAction.Jump));
        File.Delete(Path.Combine(accDir, "WindowsClient", "BackupKeybinds.json"));
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
