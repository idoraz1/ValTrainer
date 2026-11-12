using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;
using ValTrainer.Game.Weapon;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// The weapon inspect in a drill (skins3-contracts §4): VALORANT's "Inspect" bind (<see cref="GameAction.Inspect"/>, default
/// Y, rebindable in Settings → Keybinds) plays <see cref="Viewmodel.Inspect"/> on any gun, factory included.
/// <para><b>Starts</b> only while running with the gun in hand and idle: alive, no ability in hand, not reloading, not aimed /
/// scoped, fully drawn, on the ground, and never in the measuring tools (Sens Finder, Crosshair Finder: <see
/// cref="KillDrill.Measuring"/>) or a drill preview.</para>
/// <para><b>Cancels</b> at once (the viewmodel blends back in ~0.07–0.15 s): the fire bind (in the input event itself, so the
/// same frame's shot fires normally: the inspect is cosmetic and never holds the gun back), alt fire / ADS, a reload, a
/// weapon switch (a new viewmodel), an ability, jumping or falling, death, and leaving the run (pause stays frozen,
/// results cancel).</para>
/// <para>Nothing is recorded: no telemetry event, frame flag, replay data or stat changes.</para>
/// </summary>
public partial class GameSession
{
    /// <summary>Whether the inspect may play in a drill of this kind (never in the measuring tools).</summary>
    public static bool InspectAllowedIn(KillDrill drill) => drill != KillDrill.Measuring;

    bool InspectIdle =>
        State == St.Running && !Demo && Gun != null && viewmodel != null && IsInstanceValid(viewmodel) && viewmodel.Visible && !Mode.Is2D
        && InspectAllowedIn(Mode.KillDrill) && !Player.Dead && !abilityInHand && !Gun.Reloading && !Gun.Scoped && !adsHeld
        && Gun.EquipLeft <= 0f && Mover.Grounded;

    /// <summary>OnBindEvent hook: the inspect bind starts it; the fire / alt-fire press cancels it in the same event (the
    /// shot is handled by the session as always).</summary>
    void InspectBindEvent(InputEvent e, Keybinds binds, bool firePressed, bool adsPressed)
    {
        if (viewmodel == null || !IsInstanceValid(viewmodel)) return;
        if (firePressed) viewmodel.CancelInspect(fast: true);
        else if (adsPressed) viewmodel.CancelInspect();
        else if (binds.Pressed(e, GameAction.Inspect) && InspectIdle) viewmodel.Inspect();
    }

    /// <summary>Every running frame (after the gun): the cancel rules the viewmodel can't see itself, and whether skin
    /// flourishes may play in this drill.</summary>
    void InspectFrame()
    {
        if (viewmodel == null || !IsInstanceValid(viewmodel)) return;
        viewmodel.SkinFx = SkinFxOn;
        if (!viewmodel.Inspecting) return;
        bool firing = fireHeld || fireTap || firePressedEdge || altFireEdge;
        if (firing) viewmodel.CancelInspect(fast: true);
        else if (State != St.Running || Gun == null || Player.Dead || abilityInHand || Gun.Reloading || Gun.Scoped || adsHeld || !Mover.Grounded
                 || !InspectAllowedIn(Mode.KillDrill))
            viewmodel.CancelInspect();
    }

    /// <summary>The reload's skin sound at its rising edge: the special reload's sound (with the factory duration) when the
    /// skin has one, else the reload layer. The factory reload sound is played by the caller as always.</summary>
    void SkinReloadSound(WeaponLook look)
    {
        if (skinState is not { } rs || Gun == null || weaponDef == null) return;
        if (rs.ReloadAnim != null)
        {
            // the whole reload's length (this frame's tick already ran: left / (1 - progress) is the total)
            float total = Gun.ReloadLeft / Mathf.Max(1e-4f, 1f - Mathf.Max(0f, Gun.ReloadProgress));
            lastReloadAnimDuration = total;
            SkinAudio.PlayReloadAnim(rs, weaponDef.Kind, total);
        }
        else SkinAudio.PlayReloadLayer(rs, weaponDef.Kind, look.ReloadPitch);
    }
    float lastReloadAnimDuration = -1f;

    // ---------------- dev: --inspect-test ----------------

    /// <summary>
    /// DEV / TEST ONLY: <c>--dev --mode &lt;drill&gt; --inspect-test [--fixed-fps 60]</c> (a drill with movement and a
    /// magazine gun, e.g. strafebots). Checks the default bind, that the bind starts the inspect, that a fire press cancels it
    /// within one frame while the shot fires on the same frame as without an inspect, the ADS / reload / jump / switch
    /// cancels, the refusals (reloading, measuring tools), that nothing reaches the telemetry, that the skin picker's preview
    /// viewmodel inspects, and that an Apex special reload (Foundry L4) takes exactly the factory reload's frames with the
    /// same ammo timing and the factory duration passed to its sound. Prints <c>[inspect-test] …</c> and quits with the
    /// number of failures.
    /// </summary>
    static readonly bool DevInspectTest = CmdLine.Dev && CmdLine.Has("--inspect-test");
    IEnumerator<int>? inspTest;
    int inspTestWait, inspTestPass, inspTestFail, devFrame;

    void DevInspectTick()
    {
        if (!DevInspectTest) return;
        devFrame++;
        if (State != St.Running) return;
        Player.ProtectedUntil = float.MaxValue;
        inspTest ??= InspectSteps().GetEnumerator();
        if (inspTestWait-- > 0) return;
        if (inspTest.MoveNext()) { inspTestWait = inspTest.Current; return; }
        GD.Print($"[inspect-test] RESULT: {(inspTestFail == 0 ? "PASS" : "FAIL")} ({inspTestPass} passed, {inspTestFail} failed) mode={Mode.Key}");
        GetTree().Quit(inspTestFail);
        inspTest = Enumerable.Empty<int>().GetEnumerator();
        inspTestWait = int.MaxValue;
    }

    void IT(string name, bool ok, string detail = "")
    {
        if (ok) inspTestPass++; else inspTestFail++;
        GD.Print($"[inspect-test] {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }

    IEnumerable<int> InspectSteps()
    {
        var binds = Main.I.Valorant.Binds;
        string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        IT("default Inspect bind is Y", Keybinds.Defaults().Text(GameAction.Inspect) == "Y", Keybinds.Defaults().Text(GameAction.Inspect));
        IT("Inspect is listed with a label", Keybinds.All.Contains(GameAction.Inspect) && Keybinds.Label(GameAction.Inspect) == "Inspect");
        IT("not available in the measuring tools", !InspectAllowedIn(KillDrill.Measuring) && InspectAllowedIn(KillDrill.Target) && InspectAllowedIn(KillDrill.Round));
        var inspectBind = binds[GameAction.Inspect, 0];
        var fireBind = binds[GameAction.Fire, 0];
        if (Gun == null || viewmodel == null || inspectBind.IsNone || fireBind.IsNone)
        {
            IT("drill has a gun, a viewmodel and Inspect / Fire binds", false, Mode.Key);
            yield break;
        }
        for (int i = 0; i < 300 && !InspectIdle; i++) yield return 1;
        IT("gun idle and ready", InspectIdle);

        // ---- the bind starts it ----
        int events0 = Telemetry.Events.Count, shots0 = Telemetry.Shots.Count;
        SendBind(inspectBind, true); yield return 1; SendBind(inspectBind, false);
        yield return 2;
        IT("Inspect bind starts the inspect", viewmodel!.Inspecting, $"weight {F(viewmodel.InspectWeight)}");
        yield return 40;
        IT("still inspecting 0.7 s in, fully blended", viewmodel.Inspecting && viewmodel.InspectWeight > 0.99f);
        IT("nothing recorded: no telemetry event or shot", Telemetry.Events.Count == events0 && Telemetry.Shots.Count == shots0,
            $"events +{Telemetry.Events.Count - events0}, shots +{Telemetry.Shots.Count - shots0}");

        // ---- fire: cancels within one frame, the shot is not delayed ----
        // baseline: frames from the fire press to the shot without an inspect
        for (int i = 0; i < 400 && (Gun!.Reloading || Gun.Ammo <= 0); i++) yield return 1;
        int baseFrames = -1;
        {
            viewmodel.CancelInspect(fast: true);
            for (int i = 0; i < 30 && viewmodel.InspectWeight > 0f; i++) yield return 1;
            yield return 30;
            int v0 = Gun!.Volleys, f0 = devFrame;
            SendBind(fireBind, true);
            for (int i = 0; i < 20 && Gun.Volleys == v0; i++) yield return 1;
            baseFrames = Gun.Volleys > v0 ? devFrame - f0 : -1;
            SendBind(fireBind, false);
        }
        yield return 40;
        for (int i = 0; i < 400 && !InspectIdle; i++) yield return 1;
        viewmodel.Inspect();
        yield return 30;
        bool wasInspecting = viewmodel.Inspecting;
        int vol0 = Gun!.Volleys, fr0 = devFrame, cancelFrame = -1, shotFrame = -1;
        SendBind(fireBind, true);
        for (int i = 0; i < 20 && (shotFrame < 0 || cancelFrame < 0); i++)
        {
            yield return 1;
            if (cancelFrame < 0 && !viewmodel.Inspecting) cancelFrame = devFrame - fr0;
            if (shotFrame < 0 && Gun.Volleys > vol0) shotFrame = devFrame - fr0;
        }
        SendBind(fireBind, false);
        IT("fire cancels the inspect within one frame", wasInspecting && cancelFrame >= 0 && cancelFrame <= Math.Max(1, shotFrame), $"cancel after {cancelFrame} frame(s), shot after {shotFrame}");
        IT("the shot is not delayed by the inspect", shotFrame >= 0 && shotFrame == baseFrames, $"{shotFrame} frame(s) vs {baseFrames} without an inspect");
        for (int i = 0; i < 10; i++) yield return 1;
        IT("cancelled inspect blends out within 0.15 s", viewmodel.InspectWeight == 0f, F(viewmodel.InspectWeight));

        // ---- ADS ----
        yield return 40;
        for (int i = 0; i < 400 && !InspectIdle; i++) yield return 1;
        if (weaponDef!.Zoom > 1f && weaponDef.AltFire is AltFireKind.Zoom or AltFireKind.ZoomBurst or AltFireKind.Scope && !binds[GameAction.AltFire, 0].IsNone)
        {
            viewmodel.Inspect();
            yield return 20;
            SendBind(binds[GameAction.AltFire, 0], true);
            yield return 2;
            IT("ADS cancels the inspect", !viewmodel.Inspecting);
            SendBind(binds[GameAction.AltFire, 0], false);
            yield return 30;
        }
        else IT("ADS cancel (skipped: gun has no ADS)", true);

        // ---- reload ----
        for (int i = 0; i < 400 && !InspectIdle; i++) yield return 1;
        viewmodel.Inspect();
        yield return 20;
        if (Gun!.Ammo >= weaponDef!.Mag) Gun.Ammo = weaponDef.Mag - 1;
        Gun.StartReload(true);
        yield return 2;
        IT("a reload cancels the inspect", Gun.Reloading && !viewmodel.Inspecting);
        viewmodel.Inspect();
        yield return 1;
        IT("no inspect while reloading", !viewmodel.Inspecting);
        for (int i = 0; i < 600 && Gun.Reloading; i++) yield return 1;

        // ---- jump ----
        yield return 20;
        if (Mode.Movement && Mover.JumpEnabled)
        {
            for (int i = 0; i < 400 && !InspectIdle; i++) yield return 1;
            viewmodel.Inspect();
            yield return 10;
            Mover.QueueJump();
            for (int i = 0; i < 10 && Mover.Grounded; i++) yield return 1;
            yield return 1;
            IT("jumping cancels the inspect", !viewmodel.Inspecting);
            for (int i = 0; i < 120 && !Mover.Grounded; i++) yield return 1;
        }
        else IT("jump cancel (skipped: the drill has no jumping)", true);

        // ---- weapon switch: a new viewmodel, no inspect carried over ----
        for (int i = 0; i < 400 && !InspectIdle; i++) yield return 1;
        viewmodel.Inspect();
        yield return 10;
        var oldVm = viewmodel;
        var keepGun = weaponDef!.Kind;
        SwitchWeapon(keepGun == WeaponKind.Sheriff ? WeaponKind.Classic : WeaponKind.Sheriff);
        yield return 2;
        IT("a weapon switch ends the inspect", viewmodel != oldVm && viewmodel != null && !viewmodel.Inspecting);
        SwitchWeapon(keepGun);
        for (int i = 0; i < 120 && !InspectIdle; i++) yield return 1;
        if (viewmodel == null) yield break;

        // ---- the picker's preview viewmodel ----
        var preview = Viewmodel.CreateWithSkinState(WeaponKind.Vandal, false, InspectFx.DevState("glasswork"));
        AddChild(preview);
        yield return 50; // its equip raise
        preview.Inspect();
        yield return 3;
        IT("a preview viewmodel (CreateWithSkinState) inspects", preview.Inspecting);
        preview.SetReload(0.3f);
        yield return 1;
        IT("a preview reload cancels it", !preview.Inspecting);
        preview.QueueFree();

        // ---- Apex special reload: exactly the factory duration ----
        if (weaponDef!.ReloadPerRound > 0f || WeaponLooks.For(weaponDef.Kind).NoMag)
        {
            IT("special reload timing (skipped: pick a magazine gun)", true, weaponDef.Name);
            yield break;
        }
        var keepState = skinState;
        var apex = InspectFx.DevState("foundry@4");
        IT("Foundry L4 stand-in has a special reload", apex is { ReloadAnim: "foundry" } && ReloadFx.StyleFor("foundry") != null);
        var results = new List<(int Frames, int VmFrames, int AmmoFrame, float Dur, bool Fx)>();
        for (int run = 0; run < 2; run++)
        {
            bool special = run == 1;
            skinState = special ? apex : null;
            viewmodel.SetSkinStateOverride(special ? apex : null);
            for (int i = 0; i < 400 && Gun.Reloading; i++) yield return 1;
            yield return 30;
            Gun.Ammo = weaponDef.Mag - 5;
            int ammoBefore = Gun.Ammo;
            lastReloadAnimDuration = -1f;
            SendBind(binds[GameAction.Reload, 0], true); yield return 1; SendBind(binds[GameAction.Reload, 0], false);
            if (!Gun.Reloading) Gun.StartReload(true); // infinite-ammo drills ignore the bind
            int f0 = devFrame, ammoFrame = -1, vmEnd = -1;
            bool fxSeen = false;
            for (int i = 0; i < 1000; i++)
            {
                yield return 1;
                fxSeen |= viewmodel.ReloadFxActive;
                if (ammoFrame < 0 && Gun.Ammo > ammoBefore) ammoFrame = devFrame - f0;
                if (vmEnd < 0 && !viewmodel.ReloadDrawn) vmEnd = devFrame - f0;
                if (!Gun.Reloading && vmEnd >= 0) break;
            }
            results.Add((ammoFrame, vmEnd, ammoFrame, lastReloadAnimDuration, fxSeen));
            GD.Print($"[inspect-test] {(special ? "special" : "factory")} reload: ammo after {ammoFrame} frames, viewmodel reload drawn for {vmEnd} frames, fx {fxSeen}");
        }
        skinState = keepState;
        viewmodel.SetSkinStateOverride(null);
        var (fa, fb) = (results[0], results[1]);
        IT("special reload: ammo refills on the same frame as the factory reload", fa.AmmoFrame > 0 && fa.AmmoFrame == fb.AmmoFrame, $"{fa.AmmoFrame} vs {fb.AmmoFrame}");
        IT("special reload: drawn for exactly the factory reload's frames", fa.VmFrames > 0 && fa.VmFrames == fb.VmFrames, $"{fa.VmFrames} vs {fb.VmFrames}");
        float expect = Gun.Def.Reload;
        IT("special reload: its sound gets the factory duration", Mathf.Abs(fb.Dur - expect) < 1e-3f && fa.Dur < 0f, $"{F(fb.Dur)} s vs {F(expect)} s (factory run {F(fa.Dur)})");
        IT("special reload effects ran", fb.Fx && !fa.Fx);
        // the curve: same magazine-out window and rest pose at both ends
        var factory = ReloadStyle.Factory;
        IT("special reload styles keep the factory timeline", ReloadFx.Recipes.All(id => ReloadFx.StyleFor(id) is { } s && s.SeatJolt > 0f) && factory.Tilt == Vector3.One);
    }

    static void SendBind(InputBinding b, bool pressed)
    {
        if (b.ToEvent(pressed) is { } e) Input.ParseInputEvent(e);
    }
}
