using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// DEV / TEST ONLY: <c>--dev --mode &lt;drill&gt; --keybind-test</c>. Once the run starts, feeds the player's imported VALORANT
/// binds (whatever they are: keys, mouse buttons, wheel notches, both slots) into the game as real input events through
/// Input.ParseInputEvent and checks that the session reacts: movement, crouch, jump, fire, alt fire (ADS), reload, and the
/// polled actions (abilities, use spike / equip spike, use). The player can't be hurt meanwhile. Checks that don't apply to the drill
/// (no movement, infinite ammo, no zoom) are skipped. Prints "[keybind-test] …" lines and quits with the number of failures.
/// Combine with <c>--valorant-dir</c> pointing at a fake config folder holding a crafted BackupKeybinds.json.
/// </summary>
public partial class GameSession
{
    static readonly bool DevKeybindTest = CmdLine.Dev && CmdLine.Has("--keybind-test");
    IEnumerator<int>? keyTest;
    int keyTestWait, keyTestPass, keyTestFail, keyTestSkip;

    /// <summary>Called every frame (dev runs only).</summary>
    void DevKeybindTick()
    {
        if (!DevKeybindTest || State != St.Running) return;
        Player.ProtectedUntil = float.MaxValue; // bots can't interrupt the test
        keyTest ??= KeybindSteps().GetEnumerator();
        if (keyTestWait-- > 0) return;
        if (keyTest.MoveNext()) { keyTestWait = keyTest.Current; return; }
        DevKeybindTestEnd();
    }

    void DevKeybindTestEnd()
    {
        GD.Print($"[keybind-test] RESULT: {(keyTestFail == 0 ? "PASS" : "FAIL")} ({keyTestPass} passed, {keyTestFail} failed, {keyTestSkip} skipped) mode={Mode.Key}");
        GetTree().Quit(keyTestFail);
    }

    void KT(string name, bool ok, string detail = "")
    {
        if (ok) keyTestPass++; else keyTestFail++;
        GD.Print($"[keybind-test] {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    void KTSkip(string name, string why) { keyTestSkip++; GD.Print($"[keybind-test] SKIP {name}: {why}"); }

    /// <summary>Presses / releases a bind like a player would: modifier keys (Shift+Q …) go down first and up last.</summary>
    static void Send(InputBinding b, bool pressed)
    {
        var mods = new List<Key>();
        if (b.Ctrl) mods.Add(Key.Ctrl);
        if (b.Shift) mods.Add(Key.Shift);
        if (b.Alt) mods.Add(Key.Alt);
        mods.Remove(b.Key);
        if (pressed) foreach (var m in mods) Input.ParseInputEvent(new InputEventKey { Keycode = m, PhysicalKeycode = m, Pressed = true });
        if (b.ToEvent(pressed) is { } e) Input.ParseInputEvent(e);
        if (!pressed) foreach (var m in mods) Input.ParseInputEvent(new InputEventKey { Keycode = m, PhysicalKeycode = m, Pressed = false });
    }

    /// <summary>The test script; each yield is the number of frames to wait before the next step.</summary>
    IEnumerable<int> KeybindSteps()
    {
        var binds = Main.I.Valorant.Binds;
        foreach (var line in binds.Dump().Split('\n')) GD.Print("[keybind-test] bind " + line);
        string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        yield return 5;

        // ---- movement, walk, crouch, jump (every bound slot) ----
        if (!Mode.Movement) KTSkip("movement / crouch / jump", $"{Mode.Key} has no movement");
        else
        {
            foreach (var (a, slot, b) in Slots(binds, GameAction.MoveForward, GameAction.MoveBack, GameAction.StrafeLeft, GameAction.StrafeRight))
            {
                if (b.IsWheel) { KTSkip($"{a}[{slot}] {b}", "a wheel notch can't be held to move"); continue; }
                // A wall right next to the spawn could stop one direction: turn and try again (up to 4 headings).
                float speed = 0f;
                for (int turn = 0; turn < 4 && speed <= 2f; turn++)
                {
                    View.Yaw += turn == 0 ? 0f : 90f;
                    Send(b, true);
                    yield return 30;
                    speed = Mover.Speed;
                    Send(b, false);
                    yield return 40;
                }
                KT($"{a}[{slot}] {b} moves", speed > 2f, $"speed {F(speed)} m/s");
            }
            foreach (var (a, slot, b) in Slots(binds, GameAction.Crouch))
            {
                if (b.IsWheel) { KTSkip($"Crouch[{slot}] {b}", "a wheel notch can't be held"); continue; }
                Send(b, true);
                yield return 15;
                bool crouched = Mover.Crouching;
                Send(b, false);
                yield return 15;
                KT($"Crouch[{slot}] {b} crouches while held", crouched && !Mover.Crouching, $"held {crouched}, released {Mover.Crouching}");
            }
            foreach (var (a, slot, b) in Slots(binds, GameAction.Walk))
            {
                if (b.IsWheel) { KTSkip($"Walk[{slot}] {b}", "a wheel notch can't be held"); continue; }
                Send(b, true);
                yield return 5;
                bool walking = Mover.Walking;
                Send(b, false);
                yield return 5;
                KT($"Walk[{slot}] {b} walks while held", walking && !Mover.Walking);
            }
            if (!Mover.JumpEnabled) KTSkip("jump", $"{Mode.Key} has jumping off");
            else
                foreach (var (a, slot, b) in Slots(binds, GameAction.Jump))
                {
                    for (int i = 0; i < 120 && !Mover.Grounded; i++) yield return 1;
                    Send(b, true);
                    bool left = false;
                    for (int i = 0; i < 6 && !left; i++) { yield return 1; left = !Mover.Grounded; }
                    Send(b, false);
                    KT($"Jump[{slot}] {b} jumps", left, $"airborne {left}");
                    for (int i = 0; i < 120 && !Mover.Grounded; i++) yield return 1;
                    yield return 20;
                }
        }

        // ---- fire (every slot) ----
        if (Gun == null || Mode.Is2D) KTSkip("fire / alt fire / reload", $"{Mode.Key} has no gun");
        else
        {
            foreach (var (a, slot, b) in Slots(binds, GameAction.Fire))
            {
                for (int i = 0; i < 200 && (Gun.EquipLeft > 0 || Gun.Reloading); i++) yield return 1;
                int before = Telemetry.Shots.Count;
                Send(b, true);
                yield return 2;
                bool held = TriggerHeld || b.IsWheel;
                yield return 12;
                Send(b, false);
                yield return 3;
                int shots = Telemetry.Shots.Count - before;
                KT($"Fire[{slot}] {b} shoots", held && shots > 0 && !fireHeld, $"trigger {held}, {shots} shot(s), released {!fireHeld}");
                yield return 30 + Mathf.CeilToInt(weaponDef!.Interval * 150f); // the next shot is ready (Operator bolt)
            }

            // ---- alt fire: ADS / scope ----
            if (weaponDef!.Zoom <= 1f) KTSkip("alt fire", $"{weaponDef.Name} has no zoom");
            else
                foreach (var (a, slot, b) in Slots(binds, GameAction.AltFire))
                {
                    for (int i = 0; i < 200 && (Gun.EquipLeft > 0 || Gun.Reloading); i++) yield return 1;
                    bool hold = (!weaponDef.Sniper || Main.I.Valorant.HoldToScope) && !b.IsWheel;
                    Send(b, true);
                    yield return 6;
                    bool scoped = Gun.Scoped;
                    Send(b, false);
                    yield return 6;
                    if (!hold) { Send(b, true); yield return 2; Send(b, false); yield return 6; } // toggle: press again to leave
                    KT($"AltFire[{slot}] {b} {(hold ? "aims while held" : "toggles the scope")}", scoped && !Gun.Scoped, $"scoped {scoped}, after {Gun.Scoped}");
                    yield return 10;
                }

            // ---- reload ----
            if (Mode.InfiniteAmmo) KTSkip("reload", $"{Mode.Key} has infinite ammo");
            else
                foreach (var (a, slot, b) in Slots(binds, GameAction.Reload))
                {
                    for (int i = 0; i < 400 && Gun.Reloading; i++) yield return 1;
                    if (Gun.Ammo >= weaponDef.Mag)
                    {
                        // Spend a bullet so there is something to reload.
                        Send(binds.Primary(GameAction.Fire), true); yield return 2; Send(binds.Primary(GameAction.Fire), false); yield return 20;
                    }
                    Send(b, true);
                    yield return 3;
                    bool reloading = Gun.Reloading;
                    Send(b, false);
                    KT($"Reload[{slot}] {b} reloads", reloading, $"ammo {Gun.Ammo}/{weaponDef.Mag}");
                    yield return 10;
                }
        }

        // ---- polled actions: abilities, use spike (plant / defuse), equip spike, use ----
        foreach (var (a, slot, b) in Slots(binds, GameAction.UseSpike, GameAction.EquipSpike, GameAction.Use, GameAction.AbilityGrenade, GameAction.Ability1,
                     GameAction.Ability2, GameAction.Ultimate))
        {
            // Checked right after this frame's RunFrame, i.e. what the drill's polling saw this frame.
            Send(b, true);
            int framesDown = 0;
            for (int i = 0; i < 4; i++) { yield return 0; if (binds.IsDown(a)) framesDown++; }
            Send(b, false);
            yield return 2;
            bool ok = b.IsWheel ? framesDown == 1 : framesDown == 4;
            KT($"{a}[{slot}] {b} {(b.IsWheel ? "counts for exactly one frame" : "is held")}", ok && !binds.IsDown(a), $"down {framesDown} of 4 frames");
        }

        // ---- unbound actions never trigger ----
        foreach (var a in Keybinds.All.Where(x => !binds.IsBound(x)))
            KT($"{a} unbound", !binds.IsDown(a) && binds.Text(a) == "unbound", binds.Text(a));

        // ---- a bind shared by several actions triggers all of them ----
        var shared = Keybinds.All.SelectMany(x => new[] { binds[x, 0], binds[x, 1] }).Where(x => !x.IsNone && !x.IsWheel)
            .GroupBy(x => x).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (shared is { } sb)
        {
            var acts = Keybinds.All.Where(x => binds[x, 0] == sb || binds[x, 1] == sb).ToList();
            Send(sb, true);
            yield return 2;
            var down = acts.Where(x => binds.IsDown(x)).ToList();
            Send(sb, false);
            KT($"shared bind {sb} triggers {string.Join(" + ", acts)}", down.Count == acts.Count, $"down: {string.Join(" + ", down)}");
            yield return 10;
        }
        else KTSkip("shared bind", "no bind is used by two actions");
    }

    static IEnumerable<(GameAction A, int Slot, InputBinding B)> Slots(Keybinds binds, params GameAction[] actions)
    {
        foreach (var a in actions)
            for (int s = 0; s < Keybinds.Slots; s++)
                if (!binds[a, s].IsNone) yield return (a, s, binds[a, s]);
    }
}
