using System.Globalization;
using System.Text;
using System.Text.Json;
using ValTrainer.Game;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;
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
            CrosshairListChecks();
            ValorantChecks(root);
            CorruptChecks(root);
            DataChecks(root);
            DisplayChecks(fixActive);
            SkinAudioChecks();
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
        Check("one profile -> crosshair list of one (the active one)", p.CrosshairProfiles.Count == 1 && ReferenceEquals(p.CrosshairProfiles[0], p.Crosshair)
            && p.CrosshairActive == 0, p.CrosshairProfiles.Count.ToString(CultureInfo.InvariantCulture));
        Check("no VALORANT -> the switcher has VALORANT's default only", p0.CrosshairProfiles.Count == 0
            && CrosshairList.Build(p0, null, null) is [{ Source: CrosshairSource.Valorant, ActiveInValorant: true } d0] && CrosshairCode.Encode(d0.Profile) == "0");
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
        Check("no crosshair JSON -> the switcher has that one crosshair", cloud.CrosshairProfiles.Count == 1 && ReferenceEquals(cloud.CrosshairProfiles[0], cloud.Crosshair)
            && CrosshairList.Build(cloud, null, null) is [{ Index: -1, ActiveInValorant: true } one] && one.Tag.Contains("only one", StringComparison.Ordinal));

        // Several profiles (one unnamed, two named "Main", one split), the second active: every one is imported, in order.
        string multi = "{\"currentProfile\":1,\"profiles\":[" +
            "{\"profileName\":\"Main\",\"primary\":{\"color\":{\"r\":0,\"g\":255,\"b\":255,\"a\":255},\"bDisplayCenterDot\":true}}," +
            "{\"profileName\":\"\",\"primary\":{\"color\":{\"r\":0,\"g\":255,\"b\":0,\"a\":255}}}," +
            "{\"profileName\":\"Main\",\"bUseAdvancedOptions\":true,\"bUsePrimaryCrosshairForADS\":false,\"aDS\":{\"bDisplayCenterDot\":true}}]}";
        Write(Path.Combine(acc, "Windows", "RiotUserSettings.ini"),
            "[/Script/ShooterGame.ShooterGameUserSettings]\r\nEAresStringSettingName::SavedCrosshairProfileData=\"" + multi.Replace("\"", "\\\"") + "\"\r\n" +
            "EAresStringSettingName::CrosshairColor=(R=255,G=0,B=0,A=255)\r\n");
        var mp = ValorantImporter.Load(ValorantImporter.FindAccounts().FirstOrDefault());
        Check("several profiles -> all imported in VALORANT's order, the active one drawn", mp.CrosshairProfiles.Count == 3 && mp.CrosshairActive == 1
            && ReferenceEquals(mp.Crosshair, mp.CrosshairProfiles[1]) && mp.Crosshair.Name == "Profile 2" && mp.CrosshairNote == null
            && mp.CrosshairProfiles[2].AdsIsOwn && mp.CrosshairProfiles.Select(x => x.ProfileIndex).SequenceEqual(new[] { 0, 1, 2 }),
            string.Join(",", mp.CrosshairProfiles.Select(x => $"{x.ProfileIndex}:{x.Name}")) + $" active {mp.CrosshairActive}");
        Check("several profiles -> switcher names told apart", CrosshairList.Build(mp, null, null).Select(e => e.Name).SequenceEqual(new[] { "Main", "Profile 2", "Main (2)" }),
            string.Join(",", CrosshairList.Build(mp, null, null).Select(e => e.Name)));
    }

    // ---------------- crosshair switcher (CrosshairList) ----------------

    /// <summary>The switcher's list, pick resolution, saved codes and the binds, on made-up profiles; then the live API
    /// against a throw-away settings object (the real ones are put back, nothing is saved).</summary>
    static void CrosshairListChecks()
    {
        // VALORANT's profiles: "Main", unnamed, a non-object entry (skipped, index kept), "Main" again; the unnamed one active.
        const string json = "{\"currentProfile\":1,\"profiles\":[{\"profileName\":\"Main\"},{\"profileName\":\"\",\"primary\":{\"bDisplayCenterDot\":true}},5," +
                            "{\"profileName\":\"Main\",\"primary\":{\"color\":{\"r\":255,\"g\":0,\"b\":0,\"a\":255}}}]}";
        var all = CrosshairSettings.AllFromProfileJson(json, out int cur);
        Check("AllFromProfileJson: 3 profiles, the non-object skipped (index kept), names, active 1", all is { Count: 3 } && cur == 1
            && all.Select(x => x.Name).SequenceEqual(new[] { "Main", "Profile 2", "Main" }) && all.Select(x => x.ProfileIndex).SequenceEqual(new[] { 0, 1, 3 }),
            all == null ? "null" : string.Join(",", all.Select(x => $"{x.ProfileIndex}:{x.Name}")) + $" current {cur}");
        Check("FromProfileJson still returns the active profile", CrosshairSettings.FromProfileJson(json) is { Name: "Profile 2", ProfileIndex: 1 } fp && fp.Primary.CenterDot);
        Check("active profile not an object -> unreadable (as before)", CrosshairSettings.FromProfileJson(json.Replace("\"currentProfile\":1", "\"currentProfile\":2")) == null);
        Check("broken JSON -> no profiles", CrosshairSettings.AllFromProfileJson("{\"profiles\":[{", out _) == null);

        var v = new ValorantProfile { Found = true, AccountId = "acc-a", CrosshairProfiles = all!, CrosshairActive = 1 };
        v.Crosshair = all![1];
        var finder = CrosshairCode.Decode(CrosshairCode.ProCodes[0].Code)!;
        finder.Name = "Crosshair Finder";
        var saved = new List<SavedCrosshair> { new() { Name = "Leo", Code = CrosshairCode.ProCodes[1].Code }, new() { Name = "Dot", Code = " " + CrosshairCode.ProCodes[2].Code + " " } };
        var list = CrosshairList.Build(v, finder, saved);
        Check("order: VALORANT profiles, the custom slot, saved codes", list.Select(e => e.Source).SequenceEqual(new[]
            {
                CrosshairSource.Valorant, CrosshairSource.Valorant, CrosshairSource.Valorant, CrosshairSource.Custom, CrosshairSource.Saved, CrosshairSource.Saved,
            }) && list.Select(e => e.Position).SequenceEqual(Enumerable.Range(0, 6)), string.Join(",", list.Select(e => e.Source)));
        Check("names: duplicates told apart, tags", list.Select(e => e.Name).SequenceEqual(new[] { "Main", "Profile 2", "Main (2)", "Crosshair Finder", "Leo", "Dot" })
            && list[1].ActiveInValorant && list[1].Tag == "VALORANT 2/3 · active in VALORANT" && list[2].Index == 3 && list[5].Tag == "Saved 2/2",
            string.Join(" | ", list.Select(e => $"{e.Name} [{e.Tag}]")));
        Check("Step wraps both ways", CrosshairList.WrapIndex(-1, 6) == 5 && CrosshairList.WrapIndex(6, 6) == 0 && CrosshairList.WrapIndex(2, 6) == 2);

        var st = new AppSettings { SavedCrosshairs = saved };
        Check("no choice -> VALORANT's active profile (as before 1.19)", CrosshairList.Resolve(list, v, st) == (1, null));
        CrosshairList.Apply(list[1], st, v);
        Check("picking VALORANT's active profile stores no choice (follows VALORANT)", st.CrosshairChoice == null && !st.UseFinderCrosshair);
        CrosshairList.Apply(list[2], st, v);
        Check("picking another profile stores account, index and name", st.CrosshairChoice is { Kind: "valorant", Account: "acc-a", Index: 3, Name: "Main" }
            && CrosshairList.Resolve(list, v, st) is (2, { } pk) && ReferenceEquals(pk, list[2].Profile));
        // VALORANT reordered the profiles: index 3 is now another one, the picked "Main" is found by name.
        var moved = CrosshairSettings.AllFromProfileJson("{\"currentProfile\":0,\"profiles\":[{\"profileName\":\"Other\"},{\"profileName\":\"x\"},{},{\"profileName\":\"New\"},{\"profileName\":\"Main\"}]}", out _)!;
        var v2 = new ValorantProfile { Found = true, AccountId = "acc-a", CrosshairProfiles = moved, Crosshair = moved[0] };
        var l2 = CrosshairList.Build(v2, null, null);
        Check("a moved / renamed profile resolves by name, else falls back", CrosshairList.Resolve(l2, v2, st) is (4, { Name: "Main" }));
        var v3 = new ValorantProfile { Found = true, AccountId = "acc-b", CrosshairProfiles = all, Crosshair = all[1] };
        Check("a pick from another account is ignored (VALORANT's active profile), not deleted",
            CrosshairList.Resolve(CrosshairList.Build(v3, null, null), v3, st) == (1, null) && st.CrosshairChoice != null);
        CrosshairList.Apply(list[3], st, v);
        Check("the custom slot wins through UseFinderCrosshair", st.UseFinderCrosshair && CrosshairList.Resolve(list, v, st).Current == 3);
        CrosshairList.Apply(list[5], st, v);
        Check("a saved pick: by its code", !st.UseFinderCrosshair && st.CrosshairChoice is { Kind: "saved" } && CrosshairList.Resolve(list, v, st) is (5, { Name: "Dot" }));
        saved.RemoveAt(1);
        Check("a deleted saved pick falls back to VALORANT's active profile", CrosshairList.Resolve(CrosshairList.Build(v, finder, saved), v, st) == (1, null));

        // Saved codes: dedup by the crosshair (not the text, not the NAME section), names, the cap.
        var s2 = new AppSettings();
        int a0 = CrosshairList.AddTo(s2, " " + CrosshairCode.ProCodes[0].Code + " ", null, out var m0, out bool added0);
        int a1 = CrosshairList.AddTo(s2, CrosshairCode.ProCodes[0].Code + ";NAME;\"TenZ\"", null, out var m1, out bool added1);
        int a2 = CrosshairList.AddTo(s2, "hello", null, out var m2, out _);
        int a3 = CrosshairList.AddTo(s2, "0;P;c;1;NAME;\"Green \"", null, out _, out _);
        Check("Add: new code saved trimmed, named \"Code 1\"", a0 == 0 && added0 && m0 == "SAVED ✓" && s2.SavedCrosshairs![0].Code == CrosshairCode.ProCodes[0].Code
            && s2.SavedCrosshairs[0].Name == "Code 1", $"{a0} {m0} {s2.SavedCrosshairs?[0].Name}");
        Check("Add: the same crosshair again (with a NAME) is not saved twice", a1 == 0 && !added1 && m1 == "ALREADY SAVED ✓", $"{a1} {m1}");
        Check("Add: not a code", a2 == -1 && m2 == "NOT A CODE");
        Check("Add: the name from the code's NAME section", a3 == 1 && s2.SavedCrosshairs![1].Name == "Green", s2.SavedCrosshairs?.ElementAtOrDefault(1)?.Name ?? "null");
        for (int i = 0; s2.SavedCrosshairs!.Count < CrosshairList.MaxSaved && i < 40; i++) CrosshairList.AddTo(s2, $"0;P;c;5;0l;{i % 20};0o;{i / 20 + 1}", null, out _, out _);
        int full = CrosshairList.AddTo(s2, "0;P;c;7;h;0", null, out var mf, out _);
        Check($"Add: at most {CrosshairList.MaxSaved}", s2.SavedCrosshairs.Count == CrosshairList.MaxSaved && full == -1 && mf.StartsWith("LIST FULL", StringComparison.Ordinal),
            $"{s2.SavedCrosshairs.Count} {mf}");

        // Binds: ValTrainer-only defaults; no VALORANT action name maps to them; a custom bind applies.
        var kb = Keybinds.Defaults();
        Check("binds: Previous / Next crosshair default to , and .", kb.Text(GameAction.CrosshairPrev) == "," && kb.Text(GameAction.CrosshairNext) == ".",
            $"{kb.Text(GameAction.CrosshairPrev)} {kb.Text(GameAction.CrosshairNext)}");
        Check("binds: no VALORANT action name lands on the crosshair binds", new[] { "NextCrosshair", "CycleCrosshairProfile", "PreviousCrosshair", "CrosshairNext", "Crosshair" }
            .All(nm => ValorantImporter.ActionOf(nm) is not (GameAction.CrosshairNext or GameAction.CrosshairPrev)));
        var cp = new ValorantProfile();
        int ap = cp.ApplyCustomBinds(new Dictionary<string, string> { ["CrosshairNext.primary"] = "mouse:Xbutton2" });
        Check("binds: custom \"CrosshairNext.primary\": Mouse 5 applies", ap == 1 && cp.Binds[GameAction.CrosshairNext, 0] == InputBinding.Button(Godot.MouseButton.Xbutton2)
            && cp.Imported.Text(GameAction.CrosshairNext) == ".", cp.Binds.Text(GameAction.CrosshairNext));

        // The live API on a throw-away settings object and import (the real ones are put back; nothing is written).
        var app = Main.I;
        if (app == null) { Say("SKIP live crosshair switcher checks: no Main"); return; }
        var (realSt, realVal) = (app.Settings, app.Valorant);
        bool ro = AppSettings.ReadOnly;
        try
        {
            AppSettings.ReadOnly = true;
            string pasted = "  " + CrosshairCode.ProCodes[4].Code + "  ";
            app.Settings = new AppSettings();
            app.Valorant = new ValorantProfile { Found = true, AccountId = "acc-a", CrosshairProfiles = all, CrosshairActive = 1, Crosshair = all[1] };
            Check("live: 3 VALORANT profiles, VALORANT's active one drawn", CrosshairList.All.Count == 3 && CrosshairList.Current == 1 && CrosshairList.Picked == null
                && ReferenceEquals(CrosshairCode.Effective, all[1]));
            bool addOk = CrosshairList.Add(pasted, null, out var am);
            Check("live: a pasted code is saved and drawn; COPY gives the code as saved", addOk && am == "SAVED ✓" && CrosshairList.CurrentEntry.Source == CrosshairSource.Saved
                && CrosshairCode.EffectiveCode == pasted.Trim() && ReferenceEquals(CrosshairCode.Effective, CrosshairList.CurrentEntry.Profile), CrosshairCode.EffectiveCode);
            CrosshairList.Step(1);
            Check("live: Next from the last wraps to the first", CrosshairList.Current == 0 && app.Settings.CrosshairChoice is { Kind: "valorant", Index: 0 }
                && ReferenceEquals(CrosshairCode.Effective, all[0]) && CrosshairCode.EffectiveCode == CrosshairCode.Encode(all[0]));
            CrosshairList.Step(-1);
            CrosshairList.SaveCurrent(out var sm);
            Check("live: SAVE CURRENT on a saved code saves nothing", sm == "ALREADY SAVED ✓" && app.Settings.SavedCrosshairs!.Count == 1);
            CrosshairList.Select(2);
            CrosshairList.SaveCurrent(out var sm2);
            Check("live: SAVE CURRENT copies a VALORANT profile, named after it, the pick unchanged", sm2 == "SAVED ✓" && app.Settings.SavedCrosshairs!.Count == 2
                && app.Settings.SavedCrosshairs[1].Name == "Main (2) (VALORANT)" && CrosshairList.Current == 2, $"{sm2} {app.Settings.SavedCrosshairs?.Count}");
            CrosshairList.Move(1, -1);
            CrosshairList.Rename(0, "  My main  ");
            Check("live: reorder and rename", app.Settings.SavedCrosshairs![0].Name == "My main" && app.Settings.SavedCrosshairs[1].Code == pasted.Trim()
                && CrosshairList.All[3].Name == "My main", string.Join(",", app.Settings.SavedCrosshairs.Select(x => x.Name)));
            CrosshairList.Select(4);
            CrosshairList.Remove(1);
            Check("live: deleting the saved pick goes back to VALORANT's active profile", app.Settings.CrosshairChoice == null && CrosshairList.Current == 1
                && app.Settings.SavedCrosshairs!.Count == 1);
            CrosshairList.Remove(0);
            Check("live: deleting the last saved code leaves no SavedCrosshairs", app.Settings.SavedCrosshairs == null && CrosshairList.All.Count == 3);
            Check("live: bug report source", CrosshairList.SourceText() == "VALORANT active", CrosshairList.SourceText());
        }
        catch (Exception e) { Fail("live crosshair switcher threw", e.ToString()); }
        finally
        {
            app.Settings = realSt;
            app.Valorant = realVal;
            AppSettings.ReadOnly = ro;
        }
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

            // Typed sensitivity: '.' or ',' decimals in any culture, VALORANT's range, 3 decimals.
            Check("typed sens '0.35' / '0,35' / ' .4 '", SensInput.TryParse("0.35", out var ts1) && Near(ts1, 0.35f)
                && SensInput.TryParse("0,35", out var ts2) && Near(ts2, 0.35f) && SensInput.TryParse(" .4 ", out var ts3) && Near(ts3, 0.4f));
            Check("typed sens rejects junk / 0 / > 10", !SensInput.TryParse("abc", out _) && !SensInput.TryParse("0", out _)
                && !SensInput.TryParse("11", out _) && !SensInput.TryParse("", out _));

            var s = new AppSettings { SensOverride = 0.275f, UseSensOverride = true, Volume = 0.35f, ViewmodelFov = 68.5f, Quality = 2,
                CustomBinds = new() { ["AltFire.primary"] = "mouse:Xbutton1", ["Jump.secondary"] = "mouse:WheelDown" } };
            s.Save();
            string json = File.ReadAllText(Path.Combine(data, "settings.json"));
            Check("settings.json uses '.' decimals", json.Contains("0.275") && !json.Contains("0,275"), Snip(json, "SensOverride"));
            var s2 = AppSettings.Load();
            Check("settings round trip", !s2.IsNew && Near(s2.SensOverride, 0.275f) && Near(s2.Volume, 0.35f) && Near(s2.ViewmodelFov, 68.5f) && s2.Quality == 2,
                $"{F(s2.SensOverride)} {F(s2.Volume)} {F(s2.ViewmodelFov)} q{s2.Quality}");
            Check("settings round trip: custom keybinds", s2.CustomBinds is { Count: 2 } cb && cb["AltFire.primary"] == "mouse:Xbutton1"
                && CustomBinds.Get(cb, GameAction.Jump, 1) == InputBinding.Button(Godot.MouseButton.WheelDown), Snip(json, "CustomBinds"));
            Check("settings without a crosshair pick -> none (follows VALORANT as before 1.19)", s2.CrosshairChoice == null && s2.SavedCrosshairs == null);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{ \"Quality\": 2, \"UseFinderCrosshair\": false }"); // a file from before 1.19
            var old = AppSettings.Load();
            Check("settings.json from before 1.19 -> no pick, no saved codes", !old.IsNew && old.CrosshairChoice == null && old.SavedCrosshairs == null && old.Quality == 2);
            var xs = new AppSettings
            {
                CrosshairChoice = new CrosshairChoice { Kind = "valorant", Account = "0123abcd-eu", Index = 3, Name = "Main" },
                SavedCrosshairs = new() { new SavedCrosshair { Name = "Pro \"dot\"", Code = CrosshairCode.ProCodes[2].Code } },
            };
            xs.Save();
            var xs2 = AppSettings.Load();
            Check("settings round trip: crosshair pick + saved codes", xs2.CrosshairChoice is { Kind: "valorant", Account: "0123abcd-eu", Index: 3, Name: "Main", Code: null }
                && xs2.SavedCrosshairs is [{ Name: "Pro \"dot\"" } sx] && sx.Code == CrosshairCode.ProCodes[2].Code,
                Snip(File.ReadAllText(Path.Combine(data, "settings.json")), "CrosshairChoice"));

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
            GunChecks(data);

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
            d.Text(GameAction.Reload) == "R" && d.Text(GameAction.UseSpike) == "4" && d.Text(GameAction.Use) == "F" && d.Text(GameAction.BuyMenu) == "B" &&
            string.Join("", Enumerable.Range(0, 4).Select(noFile.AbilityBindText)) == "CQEX", d.ListText().Replace('\n', ';'));

        string M(string name, int slot, string key, string extra = "") =>
            $"{{\"name\":\"{name}\",\"characterName\":\"None\",\"bindIndex\":{slot},\"key\":\"{key}\",\"shift\":false,\"ctrl\":false,\"alt\":false,\"cmd\":false,\"tapHoldType\":\"None\"{extra}}}";
        string json = "{\"settingsVersion\":15,\"actionMappings\":[" + string.Join(",",
            M("Crouch", 1, "SpaceBar"), M("VOICE_TeamPTTAction", 0, "ThumbMouseButton"), M("OpenMegamap", 0, "None"),
            M("Jump", 1, "None"), M("Jump", 0, "MouseScrollDown"), M("Reload", 0, "None"), M("UseChannelObject", 0, "Five"),
            M("Activate_Ability1", 0, "ThumbMouseButton2"), M("Walk", 0, "Escape"), M("PrimaryTrigger", 1, "K"), M("SecondaryTrigger", 0, "ThumbMouseButton"), M("DropEquippable", 0, "F"),
            M("ToggleShop", 0, "N"), M("ToggleShop", 1, "MiddleMouseButton"),
            M("Crouch", 0, "C",",\"characterName\":\"Clay\"").Replace("\"characterName\":\"None\",", "")) +
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
        Check("keybinds: buy menu (ToggleShop) on N and Mouse 3", b.Text(GameAction.BuyMenu) == "N · Mouse 3", b.Text(GameAction.BuyMenu));
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
        // Buy-menu spellings: matched before the toggle / menu ignore list; other shop and buy actions stay ignored.
        string? Buy(string n) => ValorantImporter.ActionOf(n)?.ToString();
        Check("keybinds: buy menu names", Buy("ToggleShop") == "BuyMenu" && Buy("OpenBuyMenu") == "BuyMenu" && Buy("Toggle_Buy_Menu") == "BuyMenu"
            && Buy("ShopToggle") == "BuyMenu" && Buy("ShopSwitchTabs") == null && Buy("TapHoldReloadBuy") == null && Buy("ToggleMap") == null,
            string.Join(" ", new[] { "ToggleShop", "OpenBuyMenu", "Toggle_Buy_Menu", "ShopToggle", "ShopSwitchTabs", "TapHoldReloadBuy", "ToggleMap" }.Select(n => $"{n}={Buy(n) ?? "-"}")));
        Check("keybinds: toggle / ping actions ignored", t.Text(GameAction.Crouch) == "Ctrl", t.Text(GameAction.Crouch));
        Check("keybinds: tolerant alt-fire name", t.Text(GameAction.AltFire) == "Mouse 4", t.Text(GameAction.AltFire));
        Check("keybinds: modifier flag", t.Text(GameAction.Inspect) == "Shift+Q", t.Text(GameAction.Inspect));

        // A broken file keeps every default.
        Write(Path.Combine(accDir, "WindowsClient", "BackupKeybinds.json"), "{\"actionMappings\":[{\"name\":\"Jump\",\"key\":");
        var broken = ValorantImporter.Load(ValorantImporter.FindAccounts().FirstOrDefault());
        Check("keybinds: corrupt file -> defaults", broken.Found && broken.Binds.Text(GameAction.Jump) == "Space",broken.Binds.Text(GameAction.Jump));
        File.Delete(Path.Combine(accDir, "WindowsClient", "BackupKeybinds.json"));
        CustomBindChecks();
    }

    /// <summary>Settings → Keybinds: ValTrainer's own overrides (settings.json) on top of the VALORANT import; every key and
    /// mouse button (thumb buttons, wheel) round-trips; clearing one goes back to VALORANT's bind.</summary>
    static void CustomBindChecks()
    {
        var all = new List<InputBinding>
        {
            Godot.Key.Q, Godot.Key.Space, Godot.Key.Shift, Godot.Key.Ctrl, Godot.Key.Key4, Godot.Key.Kp7, Godot.Key.F5, Godot.Key.Quoteleft,
            new(Godot.Key.Q, Shift: true), new(Godot.Key.E, Ctrl: true, Alt: true), InputBinding.None,
        };
        foreach (var mb in new[] { Godot.MouseButton.Left, Godot.MouseButton.Right, Godot.MouseButton.Middle, Godot.MouseButton.Xbutton1,
                     Godot.MouseButton.Xbutton2, Godot.MouseButton.WheelUp, Godot.MouseButton.WheelDown, Godot.MouseButton.WheelLeft, Godot.MouseButton.WheelRight })
            all.Add(InputBinding.Button(mb));
        var broken = all.Where(b => InputBinding.Parse(b.Serialize()) != b).Select(b => $"{b.Text}→{b.Serialize()}").ToList();
        Check($"custom binds: {all.Count} keys / mouse buttons / wheel round-trip", broken.Count == 0, string.Join(", ", broken));
        Check("custom binds: junk / Escape / numbers rejected", InputBinding.Parse("key:Escape") == null && InputBinding.Parse("key:4194305") == null
            && InputBinding.Parse("mouse:7") == null && InputBinding.Parse("joy:A") == null && InputBinding.Parse("") == null && InputBinding.Parse("key:Nope") == null);
        Check("custom binds: capture of a thumb button / wheel / key", InputBinding.FromEvent(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Xbutton2, Pressed = true }) == InputBinding.Button(Godot.MouseButton.Xbutton2)
            && InputBinding.FromEvent(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.WheelDown, Pressed = true }) == InputBinding.Button(Godot.MouseButton.WheelDown)
            && InputBinding.FromEvent(new Godot.InputEventKey { Keycode = Godot.Key.V, Pressed = true }) == (InputBinding)Godot.Key.V
            && InputBinding.FromEvent(new Godot.InputEventKey { Keycode = Godot.Key.Escape, Pressed = true }) == null
            && InputBinding.FromEvent(new Godot.InputEventKey { Keycode = Godot.Key.V, Pressed = true, Echo = true }) == null
            && InputBinding.FromEvent(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Xbutton1, Pressed = false }) == null);

        // A profile as imported (VALORANT: crouch secondary on Space), then the player's ValTrainer overrides.
        var p = new ValorantProfile();
        p.Binds.Set(GameAction.Crouch, 1, Godot.Key.Space);
        Dictionary<string, string>? custom = null;
        CustomBinds.Set(ref custom, p.Imported, GameAction.AltFire, 0, InputBinding.Button(Godot.MouseButton.Xbutton1));
        CustomBinds.Set(ref custom, p.Imported, GameAction.Ability1, 0, InputBinding.Button(Godot.MouseButton.Xbutton2));
        CustomBinds.Set(ref custom, p.Imported, GameAction.Jump, 1, InputBinding.Button(Godot.MouseButton.WheelDown));
        CustomBinds.Set(ref custom, p.Imported, GameAction.Crouch, 1, InputBinding.None);       // unbind VALORANT's secondary
        CustomBinds.Set(ref custom, p.Imported, GameAction.Reload, 0, Godot.Key.R);               // same as VALORANT: no override
        custom!["Bogus.primary"] = "key:K";                                                        // hand edits / renamed actions
        custom["Fire.tertiary"] = "key:K";
        custom["Drop.primary"] = "key:Escape";
        var held = p.Binds;
        int applied = p.ApplyCustomBinds(custom);
        var b = p.Binds;
        Check("custom binds: applied on top of VALORANT", applied == 4 && ReferenceEquals(b, held) && b.Text(GameAction.AltFire) == "Mouse 4"
            && b.Text(GameAction.Ability1) == "Mouse 5" && b.Text(GameAction.Jump) == "Space · Wheel down" && b.Text(GameAction.Crouch) == "Ctrl"
            && p.AbilityBindText(1) == "Mouse 5", $"{applied} {b.ListText().Replace('\n', ';')}");
        Check("custom binds: VALORANT's binds kept apart", p.Imported.Text(GameAction.AltFire) == "Mouse 2" && p.Imported.Text(GameAction.Crouch) == "Ctrl · Space"
            && !ReferenceEquals(p.Imported, b), p.Imported.ListText().Replace('\n', ';'));
        Check("custom binds: same as VALORANT = no override; invalid entries ignored", !CustomBinds.IsCustom(custom, GameAction.Reload, 0)
            && CustomBinds.Count(custom) == 4 && b.Text(GameAction.Drop) == "G" && b.Text(GameAction.Fire) == "Mouse 1", $"{CustomBinds.Count(custom)} {b.Text(GameAction.Drop)}");
        Check("custom binds: hint words follow a moved alt fire", b.Hint("RMB to recall") == "Mouse 4 to recall" && b.Hint("LMB to throw") == "LMB to throw", b.Hint("RMB to recall"));
        Check("custom binds: event match on a thumb button", b.Pressed(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Xbutton1, Pressed = true }, GameAction.AltFire)
            && !b.Pressed(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Right, Pressed = true }, GameAction.AltFire)
            && b.Released(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Xbutton1, Pressed = false }, GameAction.AltFire));
        CustomBinds.Clear(ref custom, GameAction.AltFire, 0);
        p.ApplyCustomBinds(custom);
        Check("custom binds: clearing one goes back to VALORANT's", b.Text(GameAction.AltFire) == "Mouse 2" && b.Text(GameAction.Ability1) == "Mouse 5", b.Text(GameAction.AltFire));
        p.ApplyCustomBinds(null);
        Check("custom binds: reset all = VALORANT's binds", b.Dump() == p.Imported.Dump(), b.ListText().Replace('\n', ';'));
        var conflict = Keybinds.Defaults();
        conflict.Set(GameAction.Ability2, 0, InputBinding.Button(Godot.MouseButton.Right));
        Check("custom binds: conflicts listed", conflict.Others(InputBinding.Button(Godot.MouseButton.Right), GameAction.Ability2).SequenceEqual(new[] { GameAction.AltFire }),
            string.Join(",", conflict.Others(InputBinding.Button(Godot.MouseButton.Right), GameAction.Ability2)));
    }

    // ---------------- helpers ----------------

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    // ---------------- guns (1.18: per-gun records and picks) ----------------

    /// <summary>A drill stand-in for the gun checks (key and gun choice only; never played).</summary>
    sealed class GunTestDrill : Modes.TrainingMode
    {
        readonly string key;
        readonly IReadOnlyCollection<WeaponKind>? choice;
        readonly WeaponKind gun;
        public GunTestDrill(string key, IReadOnlyCollection<WeaponKind>? choice, WeaponKind gun = WeaponKind.Vandal) { this.key = key; this.choice = choice; this.gun = gun; }
        public override string Key => key;
        public override string Name => key;
        public override string Description => "";
        public override WeaponKind Weapon => gun;
        public override IReadOnlyCollection<WeaponKind>? WeaponChoice => choice;
        public override void Update(float dt) { }
    }

    /// <summary>Old stats without a gun keep their PBs and badges (they count as the drill's default gun); runs with another
    /// gun get their own PB and no badge when unrated; an unknown gun name never throws and never counts as a real gun; the
    /// picker's memory (per-drill picks, preferred gun) round-trips through settings.json.</summary>
    static void GunChecks(string data)
    {
        string statsPath = Path.Combine(data, "stats.json");
        // a stats.json written before 1.18: no "Weapon" anywhere
        File.WriteAllText(statsPath, "{\"Runs\":[" +
            "{\"Mode\":\"gridshot\",\"When\":\"2026-09-01T10:00:00\",\"Score\":41200,\"Accuracy\":0.5,\"Tier\":2,\"Badge\":2,\"Map\":\"\"}," +
            "{\"Mode\":\"gridshot\",\"When\":\"2026-09-02T10:00:00\",\"Score\":39000,\"Accuracy\":0.5,\"Tier\":2,\"Badge\":3,\"Map\":\"\"}," +
            "{\"Mode\":\"operator\",\"When\":\"2026-09-03T10:00:00\",\"Score\":9000,\"Accuracy\":0.5,\"Tier\":2,\"Badge\":1,\"Map\":\"\"}]}");
        var old = StatsStore.Load();
        var grid = new GunTestDrill("gridshot", WeaponSets.NoShotguns);
        Check("old stats.json loads, runs have no gun", old.Runs.Count == 3 && old.Runs.All(r => r.Weapon == null), $"{old.Runs.Count} runs");
        Check("old run without a gun counts as the drill's default", old.Runs.Count == 3 && old.Runs.All(r => StatsStore.GunOf(r, WeaponKind.Vandal) == WeaponKind.Vandal)
            && StatsStore.GunOf(old.Runs[2], WeaponKind.Operator) == WeaponKind.Operator);
        Check("old stats: same PB per default gun as before", old.Best("gridshot", 2, WeaponKind.Vandal, WeaponKind.Vandal) == old.Best("gridshot", 2)
            && old.Best("gridshot", 2) == 41200 && old.Best("gridshot", 2, WeaponKind.Phantom, WeaponKind.Vandal) == -1,
            $"{old.Best("gridshot", 2, WeaponKind.Vandal, WeaponKind.Vandal)} vs {old.Best("gridshot", 2)}");
        Check("old stats: same best badge (rated) as before", old.BestBadge(grid) == old.BestBadge("gridshot") && old.BestBadge(grid) == 3,
            $"{old.BestBadge(grid)} vs {old.BestBadge("gridshot")}");

        // new runs: a Phantom PB, an unrated Spectre with a high badge, a gun this build doesn't know
        old.Runs.Add(new RunRecord { Mode = "gridshot", When = new DateTime(2026, 10, 1, 9, 0, 0), Score = 50000, Tier = 2, Badge = 3, Weapon = "Phantom" });
        old.Runs.Add(new RunRecord { Mode = "gridshot", When = new DateTime(2026, 10, 1, 9, 5, 0), Score = 30000, Tier = 2, Badge = 4, Weapon = "Spectre" });
        old.Runs.Add(new RunRecord { Mode = "gridshot", When = new DateTime(2026, 10, 1, 9, 10, 0), Score = 99999, Tier = 2, Badge = 4, Weapon = "Knife" });
        old.Save();
        string json = File.ReadAllText(statsPath);
        int weaponKeys = json.Split("\"Weapon\"").Length - 1;
        Check("runs without a gun write no \"Weapon\" (older versions read the file as before)", weaponKeys == 3, $"{weaponKeys} \"Weapon\" keys");
        var st = StatsStore.Load();
        Check("gun round trip", st.Runs.Count(r => r.Weapon == "Phantom") == 1 && st.Runs.Count(r => r.Weapon == null) == 3, Snip(json, "Weapon"));
        Check("per-gun PB: Vandal unchanged, Phantom and Spectre their own", st.Best("gridshot", 2, WeaponKind.Vandal, WeaponKind.Vandal) == 41200
            && st.Best("gridshot", 2, WeaponKind.Phantom, WeaponKind.Vandal) == 50000 && st.Best("gridshot", 2, WeaponKind.Spectre, WeaponKind.Vandal) == 30000,
            $"{st.Best("gridshot", 2, WeaponKind.Vandal, WeaponKind.Vandal)} / {st.Best("gridshot", 2, WeaponKind.Phantom, WeaponKind.Vandal)}");
        bool knifeOk;
        try { knifeOk = st.Runs.Count > 0 && StatsStore.GunOf(st.Runs[^1], WeaponKind.Vandal) == WeaponKind.None && Weapons.Parse("Knife") == null; }
        catch { knifeOk = false; }
        Check("unknown gun name 'Knife': no throw, never counts as a real gun", knifeOk);
        Check("badges count rated guns only (Vandal / Phantom, not the Spectre)", st.BestBadge(grid) == 3
            && GunRating.IsRated(grid, WeaponKind.Phantom) && !GunRating.IsRated(grid, WeaponKind.Spectre), st.BestBadge(grid).ToString(CultureInfo.InvariantCulture));
        var op = new GunTestDrill("operator", WeaponSets.Snipers, WeaponKind.Operator);
        Check("Operator drill: only the Operator is rated", GunRating.IsRated(op, WeaponKind.Operator) && !GunRating.IsRated(op, WeaponKind.Marshal)
            && GunRating.RatedText(op) == "the Operator" && GunRating.RatedText(grid) == "the Vandal or Phantom", GunRating.RatedText(grid));

        // the picker's memory in settings.json
        var s = new AppSettings { PreferredWeapon = "Ghost", WeaponPicks = new() { ["gridshot"] = "Phantom", ["flick"] = "Knife", ["operator"] = "Vandal" } };
        s.Save();
        var s2 = AppSettings.Load();
        Check("settings round trip: gun picks + preferred gun", s2.PreferredWeapon == "Ghost" && s2.WeaponPicks is { Count: 3 } wp && wp["gridshot"] == "Phantom",
            $"{s2.PreferredWeapon} {s2.WeaponPicks?.Count}");
        WeaponKind? r1 = null, r2 = null, r3 = null, r4 = null;
        bool threw = false;
        try
        {
            r1 = WeaponPicks.Resolve(grid, s2);                                                   // own pick
            r2 = WeaponPicks.Resolve(new GunTestDrill("flick", WeaponSets.NoShotguns), s2);       // unknown pick -> preferred
            r3 = WeaponPicks.Resolve(op, s2);                                                     // pick + preferred not allowed -> default
            r4 = WeaponPicks.Resolve(new GunTestDrill("sheriff", null, WeaponKind.Sheriff), s2); // locked
        }
        catch { threw = true; }
        Check("resolve: own pick -> preferred -> default; unknown or not allowed falls through", !threw && r1 == WeaponKind.Phantom && r2 == WeaponKind.Ghost
            && r3 == null && r4 == null, $"{r1} {r2} {r3} {r4}");
        s2.PreferredWeapon = "Knife";
        WeaponKind? r5 = WeaponKind.Odin;
        try { r5 = WeaponPicks.Resolve(new GunTestDrill("spider", WeaponSets.NoShotguns), s2); } catch { threw = true; }
        Check("unknown preferred gun 'Knife' -> the drill's default, no throw", !threw && r5 == null && WeaponPicks.Preferred(s2) == null);
        WeaponPicks.Remember("spider", WeaponKind.Spectre, s2);
        WeaponPicks.SetPreferred(WeaponKind.Vandal, s2);
        var s3 = AppSettings.Load();
        Check("Remember / SetPreferred save", WeaponPicks.PickFor("spider", s3) == WeaponKind.Spectre && WeaponPicks.Preferred(s3) == WeaponKind.Vandal);
        foreach (var key in (s3.WeaponPicks?.Keys ?? Enumerable.Empty<string>()).ToList()) WeaponPicks.Remember(key, null, s3);
        Check("forgetting every pick leaves no empty WeaponPicks", s3.WeaponPicks == null && AppSettings.Load().WeaponPicks == null);
        SkinChecks();
    }

    /// <summary>Gun skins (skins-design-1.19.md §2.7, skins2-design-1.19.md §5.4): the catalogue, settings round trip,
    /// resolution and the lock clamp.</summary>
    static void SkinChecks()
    {
        var ids = GunSkins.All.Select(x => x.Id).ToList();
        Check("skin catalogue: 15 lines, ids unique, lowercase ASCII, none is 'factory'", ids.Count == 15 && ids.Distinct().Count() == ids.Count
            && ids.All(id => id.Length > 0 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')) && !ids.Contains(GunSkins.FactoryId), string.Join(",", ids));
        var patterns = Enum.GetValues<SkinPattern>();
        Check("every line's pattern is a SkinPattern id (the shader's numbers are never reused)", GunSkins.All.All(x => Enum.IsDefined(x.Pattern))
            && patterns.Select(p => (int)p).Distinct().Count() == patterns.Length);
        Check("patterned skins have their second colour", GunSkins.All.Where(x => x.Patterned && x.Pattern != SkinPattern.Flake).All(x => x.B != null));
        SkinCatalogChecks();

        // the resolution rules below are about storage, not locks: everything unlocked (restored after)
        bool devAll = Progress.DevUnlockAll;
        Progress.DevUnlockAll = true;
        try { SkinResolveChecks(); }
        finally { Progress.DevUnlockAll = devAll; }
        SkinLockChecks();
        SkinEquipChecks();
        KillFxChecks();
    }

    /// <summary>The collection's equip helpers (GunSkins.TopLevel / LockedItem / ChoiceOf): what EQUIP stores and what a
    /// locked item says it needs.</summary>
    static void SkinEquipChecks()
    {
        bool devAll = Progress.DevUnlockAll;
        try
        {
            Progress.DevUnlockAll = true;
            var fd = SkinCatalog.Get("foundry")!;
            var v0 = fd.Variants[0];
            Check("equip: the top level is stored as 'highest unlocked', a lower one explicitly, variants kept",
                GunSkins.TopLevel(fd) == 5 && GunSkins.ChoiceOf(fd, 5, null).ToString() == "foundry" && GunSkins.ChoiceOf(fd, 2, v0).ToString() == $"foundry/{v0.Id}@2"
                && GunSkins.ChoiceOf(SkinCatalog.Get("carbon")!, 1, null).ToString() == "carbon" && GunSkins.LockedItem(fd, 5, fd.Variants[1]) == null,
                $"{GunSkins.ChoiceOf(fd, 5, null)} {GunSkins.ChoiceOf(fd, 2, v0)}");
            Progress.DevUnlockAll = false;
            if (Progress.Data.Unlocked.Count == 0 && Progress.Xp == 0 && Progress.Stars == 0)
                Check("equip: a locked line reports itself, a free line nothing; TopLevel 0 when locked",
                    GunSkins.TopLevel(fd) == 0 && GunSkins.LockedItem(fd, 3, null) == "foundry" && GunSkins.TopLevel(SkinCatalog.Get("desertcamo")!) == 1
                    && GunSkins.LockedItem(SkinCatalog.Get("desertcamo")!, 1, null) == null, $"{GunSkins.TopLevel(fd)} {GunSkins.LockedItem(fd, 3, null)}");
        }
        finally { Progress.DevUnlockAll = devAll; }
    }

    static void SkinResolveChecks()
    {
        var s = new AppSettings { GunSkins = new() { ["Vandal"] = "carbon", ["Sheriff"] = "factory", ["Knife"] = "carbon", ["Phantom"] = "nosuchskin" }, GunSkinAll = "desertcamo" };
        s.Save();
        var s2 = AppSettings.Load();
        Check("settings round trip: GunSkins + GunSkinAll", s2.GunSkinAll == "desertcamo" && s2.GunSkins is { Count: 4 } gs && gs["Vandal"] == "carbon",
            $"{s2.GunSkinAll} {s2.GunSkins?.Count}");
        string? v = null, sh = null, ph = null, op = null, hh = null;
        bool threw = false;
        try
        {
            v = GunSkins.IdFor(WeaponKind.Vandal, s2);       // own entry
            sh = GunSkins.IdFor(WeaponKind.Sheriff, s2);     // explicit factory under an every-gun skin
            ph = GunSkins.IdFor(WeaponKind.Phantom, s2);     // unknown id -> every-gun skin
            op = GunSkins.IdFor(WeaponKind.Operator, s2);    // no entry -> every-gun skin
            hh = GunSkins.IdFor(WeaponKind.Headhunter, s2);  // Chamber's gun: always factory (gold)
        }
        catch { threw = true; }
        Check("skin resolve: own -> every gun -> factory; unknown id or gun ignored; Chamber stays factory", !threw && v == "carbon" && sh == "factory"
            && ph == "desertcamo" && op == "desertcamo" && hh == "factory", $"{v} {sh} {ph} {op} {hh}");
        var empty = new AppSettings();
        Check("no skin settings: factory, GunSkins.For is null", GunSkins.IdFor(WeaponKind.Vandal, empty) == "factory" && GunSkins.For(WeaponKind.Vandal, empty) == null
            && GunSkins.Get("CARBON")?.Id == "carbon" && GunSkins.Get(null) == null && GunSkins.Get("factory") == null);
        // skins-2 choices (v1.19 dev builds) stored before the remake: Carbon / Damascus kept, the rest factory, never a throw
        var old = new AppSettings
        {
            GunSkins = new() { ["Vandal"] = "honeycomb/teal@3", ["Phantom"] = "carbon/aramid", ["Odin"] = "damascus/ladder", ["Spectre"] = "PHOSPHOR.amber@5" },
            GunSkinAll = "graphite",
        };
        string? ov = null, op2 = null, oo = null, os = null, oa = null;
        bool oldThrew = false;
        try
        {
            ov = GunSkins.IdFor(WeaponKind.Vandal, old); op2 = GunSkins.IdFor(WeaponKind.Phantom, old); oo = GunSkins.IdFor(WeaponKind.Odin, old);
            os = GunSkins.IdFor(WeaponKind.Spectre, old); oa = GunSkins.IdFor(WeaponKind.Ares, old);
        }
        catch { oldThrew = true; }
        Check("skins-2 stored choices: carbon / damascus survive (variant dropped), every other old id is factory",
            !oldThrew && ov == "factory" && op2 == "carbon" && oo == "damascus" && os == "factory" && oa == "factory"
            && GunSkins.StateFor(WeaponKind.Phantom, old) is { Level: 1, Variant: null }, $"{ov} {op2} {oo} {os} {oa}");
        var fd = SkinCatalog.Get("foundry")!;
        string v0 = fd.Variants[0].Id;
        GunSkins.Set(WeaponKind.Odin, "anodized", s2);
        GunSkins.Set(WeaponKind.Vandal, null, s2);
        GunSkins.Set(WeaponKind.Bulldog, "desertcamo", s2); // the same as every gun: no entry
        GunSkins.Set(WeaponKind.Spectre, $"FOUNDRY.{v0}@3", s2); // the skins doc's dev syntax, stored canonical
        var s3 = AppSettings.Load();
        Check("Set saves; null removes; the every-gun skin needs no entry", GunSkins.IdFor(WeaponKind.Odin, s3) == "anodized" && GunSkins.OwnId(WeaponKind.Vandal, s3) == null
            && GunSkins.IdFor(WeaponKind.Vandal, s3) == "desertcamo" && GunSkins.OwnId(WeaponKind.Bulldog, s3) == null);
        var spectre = GunSkins.StateFor(WeaponKind.Spectre, s3);
        Check("a line/variant@level choice is stored canonical and resolves to that level and variant", GunSkins.OwnId(WeaponKind.Spectre, s3) == $"foundry/{v0}@3"
            && spectre is { Level: 3 } st && st.Line.Id == "foundry" && st.Variant?.Id == v0 && st.Has(SkinFeature.KillFx) && !st.Has(SkinFeature.Finisher)
            && st.Look.Id == $"foundry/{v0}" && st.Look.Animated, $"{GunSkins.OwnId(WeaponKind.Spectre, s3)} {spectre?.Level} {spectre?.Variant?.Id}");
        GunSkins.SetAll("damascus", s3);
        var s4 = AppSettings.Load();
        Check("SetAll clears every per-gun entry", s4.GunSkins == null && s4.GunSkinAll == "damascus" && GunSkins.IdFor(WeaponKind.Sheriff, s4) == "damascus");
        GunSkins.Set(WeaponKind.Ghost, "factory", s4);
        Check("factory on one gun under an every-gun skin is kept", GunSkins.IdFor(WeaponKind.Ghost, AppSettings.Load()) == "factory");
        GunSkins.ResetAll(s4);
        var s5 = AppSettings.Load();
        Check("ResetAll clears both fields", s5.GunSkins == null && s5.GunSkinAll == null);
        string json = File.ReadAllText(Path.Combine(Paths.DataDir, "settings.json"));
        Check("settings.json keeps both skin fields (null when empty)", json.Contains("\"GunSkins\": null") && json.Contains("\"GunSkinAll\": null"), Snip(json, "GunSkin"));
        Check("settings.json has the skin effect fields with safe defaults", json.Contains("\"SkinSounds\": true") && json.Contains("\"SkinKillFx\": 2")
            && json.Contains("\"SkinFinishers\": true") && json.Contains("\"SkinKillCounter\": true") && json.Contains("\"SkinFxVolume\": 0.8"), Snip(json, "SkinSounds"));
    }

    /// <summary>Skins 3 data (skins3-contracts.md §1–2): tiers, levels, features, effect ids, variants, legacy ids, the choice
    /// grammar.</summary>
    static void SkinCatalogChecks()
    {
        var lines = SkinCatalog.Lines;
        int Levels(Edition e) => e == Edition.Apex ? 5 : 1;
        Check("every line has its tier's level count (Core 1, Signature 1, Apex 5); levels numbered 1..n",
            lines.All(l => l.MaxLevel == Levels(l.Edition) && l.Levels.Select(x => x.N).SequenceEqual(Enumerable.Range(1, l.MaxLevel))),
            string.Join(",", lines.Where(l => l.MaxLevel != Levels(l.Edition)).Select(l => l.Id)));
        Check("tiers: 5 Core, 7 Signature, 3 Apex, in SkinLooks' order, grouped Core → Signature → Apex",
            lines.Where(l => l.Edition == Edition.Core).Select(l => l.Id).SequenceEqual(SkinLooks.Core)
            && lines.Where(l => l.Edition == Edition.Signature).Select(l => l.Id).SequenceEqual(SkinLooks.Signature)
            && lines.Where(l => l.Edition == Edition.Apex).Select(l => l.Id).SequenceEqual(SkinLooks.Apex)
            && lines.Select(l => l.Edition).SequenceEqual(lines.Select(l => l.Edition).OrderBy(e => e)));
        var riot = new[] { "select", "deluxe", "premium", "ultra", "exclusive" };
        Check("no Riot edition names: the tiers are Core, Signature, Apex", Enum.GetNames<Edition>().SequenceEqual(new[] { "Core", "Signature", "Apex" })
            && Enum.GetValues<Edition>().All(e => !riot.Contains(SkinCatalog.EditionName(e).ToLowerInvariant()) && !riot.Any(r => SkinCatalog.EditionRule(e).ToLowerInvariant().Contains(r))));
        Check("variant ids unique per line, lowercase ASCII; Apex has 3 with a base name, the others none",
            lines.All(l => l.Variants.Select(x => x.Id).Distinct().Count() == l.Variants.Count && l.Variants.All(x => ItemIds.IsToken(x.Id))
                && l.Variants.Count == (l.Edition == Edition.Apex ? 3 : 0) && (l.Edition != Edition.Apex || l.BaseName.Length > 0)));
        Check("Core: a finish only (no features, no effect ids)", lines.Where(l => l.Edition == Edition.Core).All(l => l.FeaturesAt(1) == SkinFeature.None
            && new SkinState(l, 1, null) is { Sounds: null, KillFx: null, BulletFx: null, Inspect: null, Banner: null, Tracer: null }));
        Check("Signature: one level with sounds, hit + kill sounds, bullet FX, kill effect, inspect and banner; every effect id = the line id; no animation, finisher or special reload",
            lines.Where(l => l.Edition == Edition.Signature).All(l => l.FeaturesAt(1) == SkinCatalog.SignatureFeatures
                && new SkinState(l, 1, null) is var s && s.Sounds == l.Id && s.BulletFx == l.Id && s.KillFx == l.Id && s.Inspect == l.Id && s.Banner == l.Id
                && s.Finisher == null && s.ReloadAnim == null && !s.Look.Animated && s.Tracer != null && s.Has(SkinFeature.HitSounds) && s.Has(SkinFeature.KillSounds)));
        Check("Apex: L2 animated + reactive, L3 sounds + bullet & kill effects, L4 inspect + special reload + banner, L5 finisher + kill counter; ids = the line id",
            lines.Where(l => l.Edition == Edition.Apex).All(l =>
                new SkinState(l, 1, null) is { Sounds: null, KillFx: null, BulletFx: null } s1 && !s1.Look.Animated
                && new SkinState(l, 2, null) is var s2 && s2.Look.Animated && s2.Look.React > 0f && s2.Sounds == null
                && new SkinState(l, 3, null) is var s3 && s3.Sounds == l.Id && s3.BulletFx == l.Id && s3.KillFx == l.Id && s3.Has(SkinFeature.HitSounds) && s3.Inspect == null && s3.Finisher == null && s3.Look.KillGlow > 0f
                && new SkinState(l, 4, null) is var s4 && s4.Inspect == l.Id && s4.ReloadAnim == l.Id && s4.Banner == l.Id && s4.Finisher == null && !s4.Look.Counter
                && new SkinState(l, 5, null) is var s5 && s5.Finisher == l.Id && s5.Look.Counter && s5.Has(SkinFeature.KillCounter)
                && l.Levels.Select(x => x.Blurb).Distinct().Count() == 5));
        Check("skins-2 ids: carbon and damascus kept, every other old id is factory (parses to null, no throw)",
            SkinCatalog.Legacy.Count == 21 && SkinCatalog.Legacy.All(e => e.Key is "carbon" or "damascus"
                ? SkinChoice.Parse(e.Key) is { } c && c.Line == e.Value && c.Level == 0 && c.Variant == null
                : e.Value == GunSkins.FactoryId && SkinChoice.Parse(e.Key) == null && SkinChoice.Parse(e.Key + "/teal@3") == null && SkinCatalog.Get(e.Key) == null));
        var fd = SkinCatalog.Get("foundry")!;
        string v0 = fd.Variants[0].Id;
        string P(string s) => SkinChoice.Parse(s)?.ToString() ?? "null";
        Check("choice grammar: line[/variant][@level], item ids and line.variant@n; unknown parts dropped",
            P("foundry") == "foundry" && P($" FOUNDRY.{v0.ToUpperInvariant()}@3 ") == $"foundry/{v0}@3" && P("foundry/3") == "foundry@3" && P($"foundry/{v0}") == $"foundry/{v0}"
            && P("foundry@9") == "foundry@5" && P("carbon/nosuch") == "carbon" && P("carbon@2") == "carbon" && P("glasswork@3") == "glasswork"
            && P("carbon/aramid") == "carbon" && P("honeycomb/teal@3") == "null" && P("nosuch") == "null" && P("factory") == "null" && P("") == "null",
            $"{P($" FOUNDRY.{v0.ToUpperInvariant()}@3 ")} {P("foundry/3")} {P("foundry@9")} {P("carbon@2")}");
        bool ok1 = ItemIds.TryParse("foundry/3", out var l1, out int n1, out var v1), ok2 = ItemIds.TryParse("Foundry/Blueflame", out var l2, out int n2, out var v2);
        Check("item ids: line, line/n, line/variant", ok1 && l1 == "foundry" && n1 == 3 && v1 == null && ok2 && l2 == "foundry" && n2 == 1 && v2 == "blueflame"
            && !ItemIds.TryParse("foundry/0", out _, out _, out _) && !ItemIds.TryParse("a b", out _, out _, out _) && !ItemIds.TryParse("", out _, out _, out _)
            && ItemIds.Level("carbon", 1) == "carbon" && ItemIds.Level("carbon", 2) == "carbon/2" && ItemIds.Variant("carbon", "forged") == "carbon/forged");
        // clamp: only the base and level 2 of Foundry unlocked
        var only = new HashSet<string> { "foundry", "foundry/2" };
        var r = SkinCatalog.Resolve(new SkinChoice("foundry", 5, v0), only.Contains);
        var r0 = SkinCatalog.Resolve(new SkinChoice("foundry"), only.Contains);
        Check("resolve clamps: locked level -> highest unlocked below, locked variant -> base palette, locked line -> factory",
            r is { Level: 2, Variant: null } && r0 is { Level: 2 } && SkinCatalog.Resolve(new SkinChoice("glasswork"), only.Contains) == null, $"{r?.Level} {r?.Variant?.Id} {r0?.Level}");
    }

    /// <summary>Progression stub (the contract with the skins): requirements, the table, locks, progress.json round trip.</summary>
    static void SkinLockChecks()
    {
        bool devAll = Progress.DevUnlockAll;
        Progress.DevUnlockAll = false;
        try
        {
            var req = Requirement.Parse("level:30+stars:25+(rankup:overall:1|rankup:skill:3)");
            var f = new ProgressFacts { Level = 30, Stars = 25, SkillUps = 3 };
            var f2 = new ProgressFacts { Level = 30, Stars = 25, SkillUps = 2 };
            Check("requirement grammar: + binds tighter than |, parentheses group", req.Root is ReqAll { Parts.Count: 3 } all && all.Parts[2] is ReqAny { Parts.Count: 2 }
                && req.Met(f) && !req.Met(f2) && req.Fraction(f2) is > 0.8f and < 1f && req.Root.FirstUnmet(f2)?.Kind == ReqKind.RankupSkill, req.Source);
            Check("requirement grammar: unknown / malformed terms are never met, empty is free", !Requirement.Parse("bogus:1").Met(f) && !Requirement.Parse("level:").Met(f)
                && !Requirement.Parse("(level:1").Met(f) && Requirement.Parse("").Met(new ProgressFacts()) && Requirement.Parse("level:1|bogus").Met(new ProgressFacts()));
            var unknownTerms = Progress.AllItems.SelectMany(id => Progress.RequirementOf(id).Root.Terms()).Where(t => t.Kind == ReqKind.Unknown).Select(t => t.Source).Distinct().ToList();
            int items = SkinCatalog.Lines.Sum(l => l.MaxLevel + l.Variants.Count);
            Check("every level and variant has a requirement in the table, all terms understood", Progress.AllItems.Count() == items && unknownTerms.Count == 0,
                $"{Progress.AllItems.Count()} / {items} {string.Join(",", unknownTerms)}");
            // stub: a fresh account (level 1, 0 stars) has Core + Factory only
            bool fresh = Progress.Data.Unlocked.Count == 0 && Progress.Level == 1 && Progress.Stars == 0; // level 1 (the welcome +250 XP stays below level 2)
            if (fresh)
            {
                var core = SkinCatalog.Lines.Where(l => l.Edition == Edition.Core).Select(l => l.Id).ToList();
                var locked = Progress.AllItems.Where(id => !core.Contains(id)).ToList();
                var st = Progress.Status("glasswork");
                Check("fresh account: Factory + Core unlocked, every other item locked with its requirement", Progress.IsUnlocked("factory") && core.All(Progress.IsUnlocked)
                    && locked.All(id => !Progress.IsUnlocked(id)) && st is { Known: true, Unlocked: false } && st.Text.Length > 0 && st.Progress01 is >= 0f and < 1f
                    && Progress.Status("foundry/2").Blocker != null && !Progress.IsUnlocked("nosuch") && !Progress.Status("nosuch").Known
                    && !Progress.IsUnlocked("honeycomb") && !Progress.Status("calibrated").Known, $"{st.Text} {st.ProgressText}");
                var s = new AppSettings { GunSkins = new() { ["Vandal"] = "glasswork", ["Phantom"] = "desertcamo" }, GunSkinAll = "anodized" };
                Check("a locked choice shows factory (not the every-gun skin) and is kept", GunSkins.IdFor(WeaponKind.Vandal, s) == "factory" && GunSkins.For(WeaponKind.Vandal, s) == null
                    && GunSkins.OwnId(WeaponKind.Vandal, s) == "glasswork" && GunSkins.ShowsLess(WeaponKind.Vandal, s) && GunSkins.IdFor(WeaponKind.Phantom, s) == "desertcamo"
                    && GunSkins.IdFor(WeaponKind.Odin, s) == "anodized" && !GunSkins.ShowsLess(WeaponKind.Odin, s));
                string? dev = GunSkins.DevSkin;
                string ov = SkinCatalog.Get("orrery")!.Variants[0].Id;
                GunSkins.DevSkin = $"orrery.{ov}@5";
                try { Check("dev --skin ignores locks", GunSkins.StateFor(WeaponKind.Vandal, s) is { Level: 5 } ds && ds.Variant?.Id == ov); }
                finally { GunSkins.DevSkin = dev; }
                Progress.DevUnlockAll = true;
                Check("--progress-unlock-all / --skins-unlock-all unlock everything", Progress.AllItems.All(Progress.IsUnlocked) && GunSkins.IdFor(WeaponKind.Vandal, s) == "glasswork");
                Progress.DevUnlockAll = false;
                // supporter codes (Core/Supporter.cs): the dev test key's code unlocks every item; a changed code nothing
                var app = Main.I?.Settings;
                if (app != null)
                {
                    string? keep = app.SupporterCode;
                    try
                    {
                        string good = Supporter.DevTestCode;
                        string bad = good[..^1] + (good[^1] == 'A' ? 'B' : 'A');
                        Check("supporter code: the test code is valid and names its supporter", Supporter.Check(good) is { Name: "Test Supporter" }
                            && Supporter.Check(good.ToLowerInvariant().Replace("-", " ")) != null);
                        Check("supporter code: a changed, empty or junk code is not valid", Supporter.Check(bad) == null && Supporter.Check("") == null
                            && Supporter.Check("VT-HELLO-WORLD") == null && Supporter.Check("not a code !") == null);
                        app.SupporterCode = bad;
                        Check("supporter code: an invalid code unlocks nothing", !Supporter.Active && locked.All(id => !Progress.IsUnlocked(id)));
                        app.SupporterCode = good;
                        Check("supporter code: a valid code unlocks every skin, level and variant", Supporter.Active && Progress.AllItems.All(Progress.IsUnlocked)
                            && GunSkins.IdFor(WeaponKind.Vandal, s) == "glasswork");
                    }
                    finally { app.SupporterCode = keep; }
                }
            }
            else Say("SKIP stub lock checks: this data dir has progress (dev flags or progress.json)");

            Check("level curve: 500 / 1,100 / 11,000 / 26,600 XP for levels 2 / 3 / 12 / 20, LevelOf inverts it",
                XpCurve.TotalFor(2) == 500 && XpCurve.TotalFor(3) == 1100 && XpCurve.TotalFor(12) == 11000 && XpCurve.TotalFor(20) == 26600
                && Enumerable.Range(1, 60).All(n => XpCurve.LevelOf(XpCurve.TotalFor(n)) == n && (n == 1 || XpCurve.LevelOf(XpCurve.TotalFor(n) - 1) == n - 1)));
            const string newer = "{\"Version\":2,\"MinReader\":1,\"Xp\":5,\"Future\":{\"a\":1},\"Counters\":{\"Runs\":3,\"NewCounter\":2},\"Unlocked\":{\"carbon\":\"2026-11-07T18:00:00\"}}";
            var pf = ProgressStore.Parse(newer);
            string back = ProgressStore.Serialize(pf);
            Check("progress.json round trip keeps unknown fields (root and nested), fills missing objects", pf.Xp == 5 && pf.Counters.Runs == 3 && pf.Unlocked.ContainsKey("carbon")
                && pf.Unseen != null && back.Contains("\"Future\"") && back.Contains("\"NewCounter\"") && ProgressStore.Parse(back).Extra?.ContainsKey("Future") == true, Snip(back, "Future"));
            bool corrupt = false;
            try { ProgressStore.Parse("{\"Xp\": 5,"); } catch (Exception e) { corrupt = Paths.IsCorruptData(e); }
            Check("truncated progress.json is reported as corrupt (kept as .corrupt, re-derived)", corrupt);
            Check("dev runs never write progress.json", !ProgressStore.CanWrite && !File.Exists(ProgressStore.FilePath));
        }
        finally { Progress.DevUnlockAll = devAll; }
    }

    /// <summary>Skin sound sets (skins2-design §5.4): every line with sound features has every file it needs
    /// (assets/audio/skins, tools/sfx/skin_sfx.py), and every gun has a fire-layer class.</summary>
    static void SkinAudioChecks()
    {
        var missing = new List<string>();
        int files = 0;
        foreach (var line in SkinCatalog.Lines)
        {
            var top = new SkinState(line, line.MaxLevel, null);
            if (top.Sounds is not { } set) continue;
            foreach (var f in SkinAudio.Required(top).SelectMany(SkinAudio.FilesOf))
            {
                files++;
                if (!Godot.ResourceLoader.Exists(SkinAudio.PathOf(set, f))) missing.Add($"{set}/{f}");
            }
        }
        Check($"skin sound sets: all {files} files of the lines with sounds exist", files > 0 && missing.Count == 0, string.Join(",", missing.Take(8)));
        Check("every gun has a skin fire-layer class", Enum.GetValues<WeaponKind>().All(g => SkinAudio.Classes.Contains(SkinAudio.ClassOf(g))));
    }

    /// <summary>Skin Bullet FX, kill effects, finishers and banners (skins 3): every Signature / Apex line has a bullet / kill
    /// recipe and a banner style, every Apex line a finisher, every effect id the catalogue names has a recipe, no effect colour
    /// is ever close to an enemy highlight colour, the drills' finisher classes, and the finisher rule (VALORANT's: only the
    /// round-winning kill of the last enemy) through a decide-only KillTracker.</summary>
    static void KillFxChecks()
    {
        var fxLines = SkinLooks.Signature.Concat(SkinLooks.Apex).ToList();
        Check("skins-3 lines: a bullet FX and a kill effect for each Signature / Apex line, a finisher for each Apex line",
            fxLines.All(id => BulletFx.Get(id) != null && KillFxRecipes.KillIds.Contains(id)) && SkinLooks.Apex.All(KillFxRecipes.FinisherIds.Contains)
            && KillFxRecipes.KillIds.Length == fxLines.Count && KillFxRecipes.FinisherIds.Length == SkinLooks.Apex.Length && BulletFx.Ids.Count() == fxLines.Count,
            string.Join(",", fxLines.Where(id => BulletFx.Get(id) == null || !KillFxRecipes.KillIds.Contains(id))));
        // the catalogue's ids (only its skins-3 lines: the parallel builders merge the new catalogue later)
        var cat = SkinCatalog.Lines.Where(l => fxLines.Contains(l.Id)).ToList();
        var levels = cat.SelectMany(l => l.Levels).ToList();
        var bad = levels.Select(v => v.KillFx).OfType<string>().Where(id => !KillFxRecipes.KillIds.Contains(id))
            .Concat(levels.Select(v => v.Finisher).OfType<string>().Where(id => !KillFxRecipes.FinisherIds.Contains(id)))
            .Concat(levels.Select(v => v.BulletFx).OfType<string>().Where(id => BulletFx.Get(id) == null))
            .Concat(levels.Select(v => v.Banner).OfType<string>().Where(id => !fxLines.Contains(id))).Distinct().ToList();
        Check($"every effect id of the catalogue's {cat.Count} skins-3 lines has a recipe", bad.Count == 0, string.Join(",", bad));
        // the dev / test states really carry every recipe
        var foundry = SkinFxDev.State("foundry", 5);
        var glass = SkinFxDev.State("glasswork", 1);
        Check("skins-3 states: Apex L5 has bullet FX, kill FX, finisher and banner; Signature has all but the finisher",
            foundry is { BulletFx: "foundry", KillFx: "foundry", Finisher: "foundry", Banner: "foundry" } && glass is { BulletFx: "glasswork", KillFx: "glasswork", Finisher: null }
            && SkinFxDev.State("foundry", 2) is { BulletFx: null, KillFx: null } && SkinFxDev.State("foundry", 3) is { BulletFx: "foundry", Finisher: null });
        var badColour = new List<string>();
        foreach (var enemy in ValTrainer.UI.UiTheme.EnemyColors)
            foreach (var id in fxLines)
                foreach (var pal in SkinLooks.Variants(id).Select(v => v.Palette ?? SkinLooks.Palette(id)).Prepend(SkinLooks.Palette(id)))
                {
                    var (m, a, alt) = KillFxWorld.SafeColors(pal, enemy);
                    if (KillFxWorld.NearEnemy(m, enemy) || KillFxWorld.NearEnemy(a, enemy) || KillFxWorld.NearEnemy(alt, enemy)) badColour.Add($"{id} vs {enemy.ToHtml(false)}");
                }
        Check("effect colours (main, accent, alt) are never within 27° of an enemy highlight colour", badColour.Count == 0, string.Join("; ", badColour));
        var hive = SkinLooks.Palette("hive");
        Check("Hive amber swaps to its Alt for a yellow enemy, stays amber for red",
            KillFxWorld.SafePalette(hive, new Godot.Color("#EEEE33")).Main == hive.Alt && KillFxWorld.SafePalette(hive, new Godot.Color("#FF4655")).Main == hive.Main);
        var modes = ValTrainer.Modes.ModeRegistry.All.Select(f => f()).ToList();
        string Kind(string key) => modes.FirstOrDefault(m => m.Key == key)?.KillDrill.ToString() ?? "missing";
        bool Allowed(string key) => modes.FirstOrDefault(m => m.Key == key)?.SkinFxAllowed ?? true;
        string[] roundKeys = { "siteclear", "strafeduel", "retake", "postplant", "anchor", "sound", "spray_transfer", "peekduel" };
        Check("finisher classes: round drills (map, tactical, defense, duels), Deathmatch, Peek rep, Gridshot target; finders measuring, no skin effects",
            roundKeys.All(k => Kind(k) == "Round") && Kind("deathmatch") == "Deathmatch" && Kind("peek") == "Rep"
            && Kind("gridshot") == "Target" && Kind("sensfinder") == "Measuring" && Kind("xhairfinder") == "Measuring"
            && !Allowed("sensfinder") && !Allowed("xhairfinder") && Allowed("gridshot") && Allowed("deathmatch"),
            string.Join(" ", roundKeys.Concat(new[] { "deathmatch", "peek", "gridshot", "sensfinder", "xhairfinder" }).Select(k => $"{k}={Kind(k)}")));
        FinisherRuleChecks(foundry);
    }

    /// <summary>The finisher rule (skins3-contracts §3) through a decide-only KillTracker: last enemy + round_won within the
    /// window → finisher; enemies alive, late or lost rounds, Deathmatch, 5 kills in one life, rep and measuring drills → none.</summary>
    static void FinisherRuleChecks(SkinState? apex)
    {
        if (apex is not { } st) { Check("finisher rule: an Apex L5 state to test with", false); return; }
        int alive = 0;
        KillTracker T(KillDrill d) => new(null, d, () => null, () => Godot.Colors.Red, _ => alive > 0) { State = st, Dry = true };
        KillInfo K(float time, bool roundWinning = false) =>
            new(null!, Godot.Vector3.Zero, new Godot.Vector3(0, 1.3f, 0), HitZone.Head, 10f, WeaponKind.Vandal, time) { RoundWinning = roundWinning };
        var won = new List<string>();
        void Case(string name, bool expect, Func<int> run)
        {
            int n = run();
            bool ok = expect ? n == 1 : n == 0;
            Say($"  finisher rule: {name}: {n} finisher(s) {(ok ? "ok" : "WRONG")}");
            if (!ok) won.Add(name);
        }
        Case("round drill, last enemy, round_won 0.05 s later", true, () =>
        {
            var t = T(KillDrill.Round); alive = 0;
            t.OnKill(K(10f)); t.Tick(10.03f); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 10.05f)); return t.Finishers;
        });
        Case("round drill, last enemy, the mode flags the kill round-winning", true, () =>
        {
            var t = T(KillDrill.Round); alive = 0;
            t.OnKill(K(3f, roundWinning: true)); return t.Finishers;
        });
        Case("round drill, two kills: the first leaves an enemy alive, the last wins", true, () =>
        {
            var t = T(KillDrill.Round);
            alive = 1; t.OnKill(K(5f));
            alive = 0; t.OnKill(K(5.6f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 5.6f)); return t.Finishers;
        });
        Case("round drill, enemies alive, round_won (a defuse) right after the kill", false, () =>
        {
            var t = T(KillDrill.Round); alive = 2;
            t.OnKill(K(8f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 8.1f)); return t.Finishers;
        });
        Case("round drill, last enemy, round_won 0.5 s later (outside the window)", false, () =>
        {
            var t = T(KillDrill.Round); alive = 0;
            t.OnKill(K(2f)); t.Tick(2.2f); t.Tick(2.4f); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 2.5f)); return t.Finishers;
        });
        Case("round drill, last enemy, round lost", false, () =>
        {
            var t = T(KillDrill.Round); alive = 0;
            t.OnKill(K(4f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundLost, 4.1f)); return t.Finishers;
        });
        Case("Deathmatch: last bot standing, round_won and the match won", false, () =>
        {
            var t = T(KillDrill.Deathmatch); alive = 0;
            t.OnKill(K(30f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 30.05f));
            t.OnKill(K(31f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.MatchWon, 31.05f)); return t.Finishers;
        });
        Case("Deathmatch: 5 kills in one life", false, () =>
        {
            var t = T(KillDrill.Deathmatch); alive = 3;
            for (int i = 0; i < 5; i++) t.OnKill(K(1f + i)); return t.Finishers;
        });
        Case("rep drill: 5 kills in one life, no rounds", false, () =>
        {
            var t = T(KillDrill.Rep); alive = 0;
            for (int i = 0; i < 5; i++) { t.OnKill(K(1f + i)); t.Tick(1.5f + i); } return t.Finishers;
        });
        Case("rep-classed drill that emits round_won: last enemy + round_won", true, () =>
        {
            var t = T(KillDrill.Rep); alive = 0;
            t.OnKill(K(6f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 6.02f)); return t.Finishers;
        });
        Case("measuring drill (Sens Finder): last target + round_won", false, () =>
        {
            var t = T(KillDrill.Measuring); alive = 0;
            t.OnKill(K(6f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 6.02f)); return t.Finishers;
        });
        Case("a level without the finisher (Apex L4)", false, () =>
        {
            var t = new KillTracker(null, KillDrill.Round, () => null, () => Godot.Colors.Red, _ => false) { State = SkinFxDev.State(st.Line.Id, 4), Dry = true };
            t.OnKill(K(1f)); t.OnRoundEnd(new RoundEndInfo(RoundEndKind.RoundWon, 1.05f)); return t.Finishers;
        });
        Check("finisher rule = VALORANT's: only the round-winning kill of the last enemy (12 cases)", won.Count == 0, string.Join("; ", won));
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
