using Godot;
using ValTrainer.Core;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// DEV / TEST ONLY: <c>--dev --mode &lt;drill&gt; --xhair-switch-test</c>. Once the run starts, presses the player's Previous /
/// Next crosshair binds as real input events (Input.ParseInputEvent) and checks the crosshair switcher in a drill: the drawn
/// crosshair moves to the next entry of <see cref="CrosshairList"/> and back, wraps both ways, the name toast shows, a
/// focused pause-menu sensitivity box swallows the key ("." is the default Next), the binds work on the pause menu, the
/// Crosshair Finder refuses (it picks the crosshair itself), one crosshair only says so, and a dev run without --write-data
/// leaves settings.json alone. Ends by holding the toast of one more switch for ~3 s (screenshots). Prints
/// "[xhair-switch-test] …" (sources and counts only, never profile names) and quits with the number of failures.
/// Combine with <c>--valorant-dir</c> pointing at a fake config folder with several crosshair profiles.
/// </summary>
public partial class GameSession
{
    static readonly bool DevXhairSwitchTest = CmdLine.Dev && CmdLine.Has("--xhair-switch-test");
    IEnumerator<float>? xhTest;
    float xhTestClock, xhTestWaitUntil;
    int xhTestFrames;
    int xhTestPass, xhTestFail;

    /// <summary>Called every frame (dev runs only).</summary>
    void DevXhairSwitchTick(float dt)
    {
        if (!DevXhairSwitchTest) return;
        if (xhTest == null)
        {
            if (State != St.Running) return;
            Player.ProtectedUntil = float.MaxValue; // bots can't interrupt the test
            xhTest = XhairSwitchSteps().GetEnumerator();
        }
        xhTestClock += dt; // game time: the same steps with --fixed-fps (headless runs faster than real time) and in a window
        xhTestFrames++;
        if (xhTestClock < xhTestWaitUntil || xhTestFrames < 2) return; // at least two frames per step
        if (xhTest.MoveNext()) { xhTestWaitUntil = xhTestClock + xhTest.Current; xhTestFrames = 0; return; }
        GD.Print($"[xhair-switch-test] RESULT: {(xhTestFail == 0 ? "PASS" : "FAIL")} ({xhTestPass} passed, {xhTestFail} failed) mode={Mode.Key}");
        GetTree().Quit(xhTestFail);
    }

    void XT(string name, bool ok, string detail = "")
    {
        if (ok) xhTestPass++; else xhTestFail++;
        GD.Print($"[xhair-switch-test] {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    /// <summary>The test script; each yield is the number of seconds (game time, at least two frames) to wait before the next step.</summary>
    IEnumerable<float> XhairSwitchSteps()
    {
        var binds = Main.I.Valorant.Binds;
        var next = binds.Primary(GameAction.CrosshairNext);
        var prev = binds.Primary(GameAction.CrosshairPrev);
        string settingsPath = Path.Combine(Paths.DataDir, "settings.json");
        DateTime? written = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath) : null;
        var list = CrosshairList.All;
        int n = list.Count;
        GD.Print($"[xhair-switch-test] {n} crosshair(s): {string.Join(" ", list.Select(e => e.Source.ToString()))}; current {CrosshairList.Current + 1}; " +
                 $"binds prev {prev} next {next}");
        if (next.IsNone || prev.IsNone) { XT("Previous / Next crosshair are bound", false, $"prev {prev}, next {next}"); yield break; }

        IEnumerable<float> Tap(InputBinding b)
        {
            Send(b, true);
            yield return 0.04f;
            Send(b, false);
            yield return 0.08f;
        }
        string Detail() => $"current {CrosshairList.Current + 1}/{CrosshairList.All.Count}, toast \"{(XhairToastTime > 0 ? XhairToastText.Length + " chars" : "none")}\"";

        if (Mode.Key == "xhairfinder")
        {
            int cur0 = CrosshairList.Current;
            XhairToastTime = 0;
            foreach (var w in Tap(next)) yield return w;
            XT("Crosshair Finder refuses the switch (it picks the crosshair here)", CrosshairList.Current == cur0 && XhairToastTime > 0
                && XhairToastText.Contains("CROSSHAIR FINDER", StringComparison.Ordinal) && XhairToastWarn, Detail());
            for (int i = 0; i < 30; i++) { XhairToastTime = XhairToastLife; yield return 0.1f; } // hold the toast for screenshots
            CheckUnwritten(written, settingsPath);
            yield break;
        }
        if (n < 2)
        {
            XhairToastTime = 0;
            foreach (var w in Tap(next)) yield return w;
            XT("one crosshair: the switch says so", CrosshairList.Current == 0 && XhairToastTime > 0 && XhairToastText == "ONLY ONE CROSSHAIR", Detail());
            for (int i = 0; i < 30; i++) { XhairToastTime = XhairToastLife; yield return 0.1f; }
            CheckUnwritten(written, settingsPath);
            yield break;
        }

        int start = CrosshairList.Current;
        foreach (var w in Tap(next)) yield return w;
        int c1 = CrosshairList.Current;
        XT($"Next ({next}) → the next crosshair", c1 == (start + 1) % n, Detail());
        XT("the drill draws it", ReferenceEquals(crosshair.Profile, CrosshairList.All[c1].Profile) && ReferenceEquals(crosshair.Profile, CrosshairCode.Effective));
        XT("its name shows under the crosshair", XhairToastTime > 0 && XhairToastText == CrosshairList.All[c1].Name && !XhairToastWarn, Detail());
        foreach (var w in Tap(prev)) yield return w;
        XT($"Previous ({prev}) → back", CrosshairList.Current == start && ReferenceEquals(crosshair.Profile, CrosshairList.All[start].Profile), Detail());

        for (int i = 0; i < n; i++)
            foreach (var w in Tap(next)) yield return w;
        XT($"Next {n} times wraps to the same crosshair", CrosshairList.Current == start, Detail());
        CrosshairList.Select(0);
        yield return 0.05f;
        foreach (var w in Tap(prev)) yield return w;
        XT("Previous from the first wraps to the last", CrosshairList.Current == n - 1, Detail());

        // Pause menu: a focused sensitivity box takes "." as a decimal point, not as Next crosshair.
        Pause();
        yield return 0.3f;
        var edit = hud.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().FirstOrDefault();
        edit?.GrabFocus();
        yield return 0.05f;
        int cp = CrosshairList.Current;
        var period = new InputBinding(Key.Period);
        foreach (var w in Tap(period)) yield return w;
        XT("a focused pause-menu sensitivity box swallows '.'", edit != null && GetViewport().GuiGetFocusOwner() == edit && CrosshairList.Current == cp,
            edit == null ? "no sensitivity box" : Detail());
        edit?.ReleaseFocus();
        yield return 0.05f;
        foreach (var w in Tap(next)) yield return w;
        XT("Next works on the pause menu", State == St.Paused && CrosshairList.Current == (cp + 1) % n, Detail());
        Resume();
        yield return 0.3f;

        CrosshairList.Select(start);
        yield return 0.05f;
        CheckUnwritten(written, settingsPath);
        // one more switch, its toast held ~3 s for screenshots
        foreach (var w in Tap(next)) yield return w;
        for (int i = 0; i < 30; i++) { XhairToastTime = XhairToastLife; yield return 0.1f; }
    }

    void CheckUnwritten(DateTime? before, string path)
    {
        if (Main.I.SavesData) { GD.Print("[xhair-switch-test] SKIP settings.json unwritten: this run may write data (--write-data)"); return; }
        DateTime? now = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        XT("settings.json untouched (dev run without --write-data)", now == before, $"{before:O} → {now:O}");
    }
}
