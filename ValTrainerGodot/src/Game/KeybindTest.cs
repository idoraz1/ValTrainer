using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// DEV / TEST ONLY: <c>--dev --mode &lt;drill&gt; --keybind-test</c>. Once the run starts, feeds the player's imported VALORANT
/// binds (whatever they are: keys, mouse buttons, wheel notches, both slots) into the game as real input events through
/// Input.ParseInputEvent and checks that the session reacts: movement, crouch, jump, fire, alt fire (ADS / scope levels, or
/// the Classic's blast and the Bucky's air burst), aimed bursts (Stinger, Bulldog), reload, and the
/// polled actions (abilities, use spike / equip spike, use), and last the buy menu (opens the gun picker and freezes the
/// drill, the bind / Esc close it without a restart or a pause, a new gun restarts the run; a locked drill refuses). The
/// player can't be hurt meanwhile. Checks that don't apply to the drill
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
        if (!DevKeybindTest || !(State == St.Running || (State == St.Buying && keyTest != null))) return; // the buy-menu steps run while it is open
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
        var imported = Main.I.Valorant.Imported;
        foreach (var line in binds.Dump(imported).Split('\n')) GD.Print("[keybind-test] bind " + line);
        string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        yield return 5;

        // ---- ValTrainer custom binds (settings.json, Settings → Keybinds) are what the session uses ----
        var custom = Main.I.Settings.CustomBinds;
        if (custom is not { Count: > 0 }) KTSkip("custom binds", "settings.json has none (--data-dir with a settings.json holding CustomBinds tests them)");
        else
        {
            foreach (var (key, value) in custom)
            {
                if (CustomBinds.ParseKey(key) is not { } s || InputBinding.Parse(value) is not { } want)
                {
                    KT($"custom {key}={value} ignored (not a valid action / bind)", CustomBinds.ParseKey(key) is not { } s2 || imported[s2.A, s2.Slot] == binds[s2.A, s2.Slot]);
                    continue;
                }
                KT($"custom {s.A}[{s.Slot}] = {want} in effect", binds[s.A, s.Slot] == want, $"effective {binds[s.A, s.Slot]}, VALORANT {imported[s.A, s.Slot]}");
            }
            // Slots without an override follow VALORANT.
            var follow = Keybinds.All.SelectMany(a => Enumerable.Range(0, Keybinds.Slots).Select(sl => (a, sl)))
                .Where(x => !CustomBinds.IsCustom(custom, x.a, x.sl)).ToList();
            var bad = follow.Where(x => binds[x.a, x.sl] != imported[x.a, x.sl]).Select(x => $"{x.a}[{x.sl}]").ToList();
            KT($"{follow.Count} slots without a custom bind follow VALORANT", bad.Count == 0, string.Join(", ", bad));
        }

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

            // ---- alt fire: ADS / scope, or the alt volley of guns whose right click shoots (Classic blast, Bucky air burst) ----
            if (weaponDef!.AltFire is AltFireKind.Shotgun or AltFireKind.AirBurst)
                foreach (var (a, slot, b) in Slots(binds, GameAction.AltFire))
                {
                    for (int i = 0; i < 400 && (Gun.EquipLeft > 0 || Gun.Reloading); i++) yield return 1;
                    float ready = Now + 0.1f + Mathf.Max(weaponDef.Interval, weaponDef.AltRps > 0 ? 1f / weaponDef.AltRps : 0f);
                    while (Now < ready) yield return 1; // the gun is ready again (Bucky 0.9 s, Classic blast 0.45 s)
                    int before = Telemetry.Shots.Count, ammo = Gun.Ammo, volleys = Gun.Volleys;
                    Send(b, true);
                    yield return 3;
                    Send(b, false);
                    yield return 3;
                    int used = Mode.InfiniteAmmo ? 0 : Math.Min(ammo, weaponDef.AltFire == AltFireKind.Shotgun ? weaponDef.AltAmmoPerShot : 1);
                    bool ok = Telemetry.Shots.Count - before == 1 && Gun.Volleys - volleys == 1 && (Gun.Reloading || ammo - Gun.Ammo == used) && !Gun.Scoped;
                    KT($"AltFire[{slot}] {b} fires the {(weaponDef.AltFire == AltFireKind.Shotgun ? $"{weaponDef.AltPellets}-pellet blast" : "air-burst canister")}",
                        ok, $"{Telemetry.Shots.Count - before} shot(s) recorded, {Gun.Volleys - volleys} volley(s), ammo {ammo} → {Gun.Ammo} (expected −{used}), scoped {Gun.Scoped}");
                }
            else if (weaponDef.Zoom <= 1f) KTSkip("alt fire", $"{weaponDef.Name} has no zoom");
            else
                foreach (var (a, slot, b) in Slots(binds, GameAction.AltFire))
                {
                    for (int i = 0; i < 200 && (Gun.EquipLeft > 0 || Gun.Reloading); i++) yield return 1;
                    bool hold = (!weaponDef.Scope || Main.I.Valorant.HoldToScope) && !b.IsWheel;
                    Send(b, true);
                    yield return 6;
                    bool scoped = Gun.Scoped;
                    float zoom1 = View.Zoom, zoom2 = zoom1;
                    Send(b, false);
                    yield return 6;
                    if (!hold && weaponDef.Zoom2 > 0f) { Send(b, true); yield return 2; Send(b, false); yield return 6; zoom2 = View.Zoom; } // Operator: 2nd zoom
                    if (!hold) { Send(b, true); yield return 2; Send(b, false); yield return 6; } // toggle: press again to leave
                    string what = hold ? "aims while held" : weaponDef.Zoom2 > 0f ? "toggles 1st zoom → 2nd zoom → off" : "toggles the scope";
                    bool levels = hold || weaponDef.Zoom2 <= 0f || (Mathf.IsEqualApprox(zoom1, weaponDef.Zoom) && Mathf.IsEqualApprox(zoom2, weaponDef.Zoom2));
                    KT($"AltFire[{slot}] {b} {what}", scoped && !Gun.Scoped && levels, $"scoped {scoped} ({F(zoom1)}×{(weaponDef.Zoom2 > 0f && !hold ? $", then {F(zoom2)}×" : "")}), after {Gun.Scoped}");
                    yield return 10;
                }

            // ---- aimed bursts (Stinger, Bulldog): one trigger pull while aimed fires a whole burst ----
            if (weaponDef.AltFire == AltFireKind.ZoomBurst && binds.Primary(GameAction.AltFire) is { IsNone: false, IsWheel: false } aim)
            {
                for (int i = 0; i < 400 && (Gun.EquipLeft > 0 || Gun.Reloading); i++) yield return 1;
                yield return 40;
                Send(aim, true);
                yield return 12;
                bool aimed = Gun.Scoped;
                int before = Telemetry.Shots.Count;
                var fire = binds.Primary(GameAction.Fire);
                Send(fire, true); yield return 1; Send(fire, false);
                float done = Now + weaponDef.BurstCount / weaponDef.BurstRps + 0.08f;
                while (Now < done) yield return 1;
                int burst = Telemetry.Shots.Count - before;
                Send(aim, false);
                yield return 10;
                KT($"aimed tap fires a {weaponDef.BurstCount}-round burst", aimed && (burst == weaponDef.BurstCount || (!Mode.InfiniteAmmo && burst > 0 && Gun.Reloading)),
                    $"aimed {aimed}, {burst} shot(s)");
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

        // ---- buy menu: the bind opens the gun picker over the drill (frozen), closes it again; a locked drill refuses ----
        var lockedDrill = new Modes.SheriffMode();
        KT("a locked drill has no buy menu (Sheriff One-Taps)", BuyRefusal(lockedDrill) is { } lr && lr.Contains("SHERIFF", StringComparison.Ordinal), BuyRefusal(lockedDrill) ?? "opens");
        var buy = binds.Primary(GameAction.BuyMenu);
        if (buy.IsNone) KTSkip("buy menu", "BuyMenu is unbound");
        else if (buy.IsWheel) KTSkip("buy menu", $"{buy} is a wheel notch");
        else if (BuyRefusal(Mode) is { } why)
        {
            BannerTime = 0;
            Send(buy, true); yield return 2; Send(buy, false); yield return 2;
            KT($"BuyMenu {buy} refused in locked {Mode.Key}", State == St.Running && !BuyMenuOpen && BannerTime > 0 && BannerText == why, $"state {State}, banner \"{BannerText}\"");
        }
        else
        {
            var run = Mode;
            Send(buy, true); yield return 2; Send(buy, false);
            float frozenAt = Now;
            yield return 10;
            KT($"BuyMenu {buy} opens the gun picker and freezes the drill", State == St.Buying && BuyMenuOpen && Now == frozenAt && Input.MouseMode != Input.MouseModeEnum.Captured,
                $"state {State}, picker {BuyMenuOpen}, clock {F(frozenAt)} → {F(Now)}");
            Send(buy, true); yield return 2; Send(buy, false); yield return 2;
            KT($"BuyMenu {buy} again closes it: the same run resumes", State == St.Running && !BuyMenuOpen && ReferenceEquals(run, Mode) && Now > frozenAt,
                $"state {State}, picker {BuyMenuOpen}, same run {ReferenceEquals(run, Mode)}");
            Send(buy, true); yield return 2; Send(buy, false); yield return 2;
            bool reopened = State == St.Buying;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
            yield return 3;
            KT("Esc closes the buy menu without pausing", reopened && State == St.Running && !BuyMenuOpen && ReferenceEquals(run, Mode), $"reopened {reopened}, state {State}");
            // last: a new gun restarts the run with it (or swaps it in place in free play)
            var gun = Mode.ActiveWeapon;
            var other = Mode.WeaponChoice!.FirstOrDefault(g => g != gun && g != WeaponKind.None);
            if (other == WeaponKind.None) KTSkip("buy menu pick", $"{Mode.Key} offers one gun");
            else
            {
                Send(buy, true); yield return 2; Send(buy, false); yield return 2;
                buyPicker?.Pick(other); // the picker's own path (a click / Enter)
                bool nextLife = GunOnRespawn;
                bool ok = FreePlay ? ReferenceEquals(run, Mode) && State == St.Running && Gun?.Def.Kind == other
                    : nextLife ? ReferenceEquals(run, Mode) && State == St.Running && Gun?.Def.Kind == gun && nextLifeGun == other
                    : !ReferenceEquals(run, Mode) && State == St.Countdown && Mode.ActiveWeapon == other && Gun?.Def.Kind == other;
                KT($"picking the {other} {(FreePlay ? "swaps it in place (free play)" : nextLife ? "keeps the match going, the gun comes out next life" : "restarts the run with it")}", ok && !BuyMenuOpen,
                    $"state {State}, playing {Mode.ActiveWeapon}, gun {Gun?.Def.Name}, next life {nextLifeGun}, same run {ReferenceEquals(run, Mode)}");
                if (nextLife)
                {
                    ApplyNextLifeGun(); // what the drill does on your respawn
                    KT($"on respawn the {other} comes out", Gun?.Def.Kind == other && Mode.ActiveWeapon == other && nextLifeGun == null,
                        $"gun {Gun?.Def.Name}, playing {Mode.ActiveWeapon}");
                }
                yield return 5; // the countdown: the steps resume when the restarted run is running
            }
        }
    }

    static IEnumerable<(GameAction A, int Slot, InputBinding B)> Slots(Keybinds binds, params GameAction[] actions)
    {
        foreach (var a in actions)
            for (int s = 0; s < Keybinds.Slots; s++)
                if (!binds[a, s].IsNone) yield return (a, s, binds[a, s]);
    }
}
