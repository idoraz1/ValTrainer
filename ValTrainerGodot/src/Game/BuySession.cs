using Godot;
using ValTrainer.Core;
using ValTrainer.Modes;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// The in-drill buy menu: VALORANT's "Open buy menu" bind (<see cref="GameAction.BuyMenu"/>, B by default) opens the
/// <see cref="GunPicker"/> over the running drill (also during the countdown, while dead in Deathmatch and from the pause
/// menu). The drill freezes like the pause menu (<see cref="St.Buying"/>) and the mouse is released; only the guns the
/// drill's <see cref="TrainingMode.WeaponChoice"/> offers can be picked, and the pick is remembered for the drill
/// (<see cref="WeaponPicks.Remember"/>, done by the picker).
/// <list type="bullet">
/// <item>A new gun in a scored run restarts it with that gun, countdown included, like the restart key: records are per
/// gun, so one run never mixes guns.</item>
/// <item>Deathmatch (<see cref="TrainingMode.GunOnRespawn"/>) never restarts or respawns you: the gun comes out on your
/// next life, like VALORANT's loadout, and a match that changed guns is saved under the gun held longest.</item>
/// <item>Free play swaps the gun in place (draw time and all) and keeps going; with an ability in hand it restarts.</item>
/// <item>Closing without a new gun (Esc, the bind again, DONE, the equipped gun) resumes exactly where the drill was.</item>
/// <item>A drill locked to its own gun (or without one) shows a banner instead and never opens it.</item>
/// </list>
/// Never in the drill preview's demo (the preview has its own B) and never on the results screen; Rewind and replays
/// aren't sessions. Dev: <c>--buy-test Phantom</c> opens it 3 s after the session starts (focus on that gun) and picks it
/// at 5 s (screenshots); <c>--keybind-test</c> checks the bind (KeybindTest.cs).
/// </summary>
public partial class GameSession
{
    CanvasLayer hudLayer = null!;
    GunPicker? buyPicker;
    /// <summary>The state the buy menu was opened from (Countdown, Running or Paused).</summary>
    St buyFrom;
    /// <summary>The gun picked in the open buy menu (null = none yet).</summary>
    WeaponKind? buyPicked;
    ulong buyClosedFrame = ulong.MaxValue;

    public bool BuyMenuOpen => buyPicker != null && IsInstanceValid(buyPicker);

    /// <summary>The buy menu closed during this frame's input: the same press must not pause or reopen it.</summary>
    bool BuyJustClosed => Engine.GetProcessFrames() == buyClosedFrame;

    /// <summary>Why <paramref name="m"/> has no buy menu (the banner text), or null when it offers a gun choice.</summary>
    public static string? BuyRefusal(TrainingMode m)
    {
        if (m.Weapon == WeaponKind.None || m.Is2D) return "NO GUNS IN THIS DRILL";
        if (m.WeaponChoice is not { Count: > 0 }) return $"THIS DRILL USES THE {GunPicker.GunName(m.Weapon).ToUpperInvariant()}";
        return null;
    }

    /// <summary>Opens the buy menu (the bind's press). False when it can't open here (a locked drill shows why).</summary>
    public bool OpenBuyMenu()
    {
        if (Demo || BuyMenuOpen || State is not (St.Countdown or St.Running or St.Paused)) return false;
        if (BuyRefusal(Mode) is { } why)
        {
            Banner(why, UiTheme.Warn);
            return false;
        }
        var binds = Main.I.Valorant.Binds;
        var size = GetViewport().GetVisibleRect().Size;
        float k = UiTheme.S(size);
        string title = $"{Mode.Name} · {(FreePlay ? "free play: a new gun comes out right away" : GunOnRespawn ? "a new gun comes out when you respawn" : "a new gun restarts the run")}";
        buyPicked = null;
        var picker = GunPicker.OpenForDrill(hudLayer, k, Mode, Mode.ActiveWeapon, g => buyPicked = g, title, p =>
        {
            p.Theme = UiTheme.MakeTheme(k);
            p.CloseOn = e => binds.Pressed(e, GameAction.BuyMenu);
            string bind = binds.Text(GameAction.BuyMenu, " / ");
            p.KeysHint = $"Click or Enter picks · arrows move · {(bind == "B" ? "B" : bind + " / B")} / Esc closes";
        });
        if (picker == null) return false;
        buyPicker = picker;
        buyFrom = State;
        ResetTransientInput(); // a held trigger / aim must not carry over to the resume
        SetWorldRunning(false);
        State = St.Buying;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        picker.Closed += OnBuyClosed;
        if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: buy menu open (from {buyFrom}, {Mode.ActiveWeapon} equipped)");
        return true;
    }

    /// <summary>Closes the buy menu without a change (e.g. the session pauses), back to the state it was opened from.</summary>
    void DismissBuyMenu()
    {
        buyPicked = null;
        if (BuyMenuOpen) buyPicker!.Dismiss(); // → OnBuyClosed
        else if (State == St.Buying) LeaveBuy(false);
    }

    void OnBuyClosed()
    {
        buyPicker = null;
        buyClosedFrame = Engine.GetProcessFrames();
        if (!IsInsideTree() || State != St.Buying) return;
        var gun = buyPicked;
        buyPicked = null;
        if (gun is { } g && g != Mode.ActiveWeapon) ChangeGun(g);
        else if (gun is { } same && nextLifeGun != null)
        {
            // changed their mind back to the gun in hand: no new gun next life
            nextLifeGun = null;
            WeaponPick = same;
            LeaveBuy(true);
            Banner($"KEEPING THE {GunPicker.GunName(same).ToUpperInvariant()}", UiTheme.Text);
        }
        else LeaveBuy(false);
    }

    /// <summary>Back from the buy menu: to the pause menu it was opened from, or the drill exactly where it was
    /// (<paramref name="toGame"/>: to the drill even when it was opened from the pause menu).</summary>
    void LeaveBuy(bool toGame)
    {
        if (buyFrom == St.Paused && !toGame) { State = St.Paused; return; } // still frozen, mouse free
        State = buyFrom == St.Paused ? resumeTo : buyFrom;
        ResetTransientInput(); // a stale click must never fire on resume
        SetWorldRunning(true);
        LockMouse();
    }

    /// <summary>Deathmatch (and other drills where you respawn): a new gun comes out on your next life, like VALORANT's
    /// loadout — the match keeps going, nobody is moved or respawned (<see cref="TrainingMode.GunOnRespawn"/>).</summary>
    bool GunOnRespawn => Mode.GunOnRespawn && !FreePlay;
    /// <summary>The gun picked for the next life (null = keep the one in hand).</summary>
    WeaponKind? nextLifeGun;
    /// <summary>Seconds each gun was in hand this run: a match that changed guns is saved under the gun held longest.</summary>
    readonly Dictionary<WeaponKind, float> gunHeldFor = new();

    /// <summary>Called by the drill when you respawn: the gun picked for this life comes out (full magazine).</summary>
    public void ApplyNextLifeGun()
    {
        if (nextLifeGun is not { } g) return;
        nextLifeGun = null;
        if (g == Mode.ActiveWeapon) return;
        Mode.Pick = g;
        SwitchWeapon(g);
        Banner(GunPicker.GunName(g).ToUpperInvariant(), UiTheme.Text);
        if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: respawned with the {g}");
    }

    /// <summary>Running frame: time with the gun in hand (see <see cref="gunHeldFor"/>).</summary>
    void TrackGunTime(float dt)
    {
        if (Mode.ActiveWeapon == WeaponKind.None) return;
        gunHeldFor[Mode.ActiveWeapon] = gunHeldFor.GetValueOrDefault(Mode.ActiveWeapon) + dt;
    }

    /// <summary>Results: a run that changed guns mid-way (Deathmatch) is saved and shown with the gun held longest.</summary>
    void SettleRunGun()
    {
        if (gunHeldFor.Count < 2) return;
        var longest = gunHeldFor.MaxBy(kv => kv.Value).Key;
        if (longest != Mode.ActiveWeapon) Mode.Pick = longest;
        if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: guns used {string.Join(", ", gunHeldFor.Select(kv => $"{kv.Key} {kv.Value:0}s"))}; saved as {longest}");
    }

    /// <summary>A new gun from the buy menu (already remembered for the drill by the picker).</summary>
    void ChangeGun(WeaponKind g)
    {
        WeaponPick = g; // every later restart of this session plays it too
        if (GunOnRespawn)
        {
            // Deathmatch: no restart, no respawn — the gun comes out on your next life (right away if you're dead)
            LeaveBuy(true);
            nextLifeGun = g;
            Banner($"{GunPicker.GunName(g).ToUpperInvariant()} · NEXT LIFE", UiTheme.Text);
            if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: {g} on the next life (no restart)");
            return;
        }
        if (FreePlay && !abilityInHand && cam != null && IsInstanceValid(cam))
        {
            // free play saves nothing: swap the gun in place (with its draw time) and keep going
            LeaveBuy(true);
            Mode.Pick = g;
            SwitchWeapon(g);
            Banner(GunPicker.GunName(g).ToUpperInvariant(), UiTheme.Text);
            if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: free play now plays the {g} (no restart)");
            return;
        }
        // a scored run restarts with the new gun, countdown and all, like the restart key: a run never mixes guns
        if (Main.I.Dev) Log.Info($"[buy] {Mode.Key}: {Mode.ActiveWeapon} -> {g}, restarting the run");
        Restart();
    }

    // ---------------- dev: --buy-test <Gun> ----------------

    static readonly string? DevBuyGun = CmdLine.DevAfter("--buy-test");
    float devBuyClock;
    int devBuyStage;

    /// <summary>Dev "--buy-test Phantom": opens the buy menu 3 s after the session started (frame time capped like the game
    /// clock, so about when the countdown ends; focus on that gun) and picks it at 5 s, logging "[buy-test] …" (gshot
    /// screenshots of the open picker and the restarted run).</summary>
    void DevBuyTick(float delta)
    {
        if (DevBuyGun == null || devBuyStage >= 3) return;
        devBuyClock += Mathf.Min(delta, 0.05f); // the game's frame cap: the loading hitch doesn't count (t=3 s ≈ the countdown's end)
        if (devBuyStage == 0 && devBuyClock >= 3f)
        {
            devBuyStage = 1;
            bool open = OpenBuyMenu();
            Log.Info($"[buy-test] t=3 s: {(open ? "buy menu open" : $"refused ({BuyRefusal(Mode) ?? State.ToString()})")} in {Mode.Key}");
            if (!open) { devBuyStage = 3; return; }
            if (Weapons.Parse(DevBuyGun) is { } g) buyPicker?.FocusGun(g);
            else Log.Error($"[buy-test] {DevBuyGun} is not a gun");
        }
        else if (devBuyStage == 1 && devBuyClock >= 5f)
        {
            devBuyStage = 2;
            var before = Mode;
            if (Weapons.Parse(DevBuyGun) is { } g && BuyMenuOpen) buyPicker!.Pick(g);
            bool restarted = !ReferenceEquals(before, Mode);
            Log.Info($"[buy-test] t=5 s: picked {DevBuyGun}: {Mode.Key} plays the {Mode.ActiveWeapon}, state {State}, "
                + $"{(restarted ? "run restarted" : "same run")}{(BuyMenuOpen ? ", picker still open (not offered here)" : "")}");
        }
        else if (devBuyStage == 2 && State == St.Running)
        {
            devBuyStage = 3;
            Log.Info($"[buy-test] running again with the {Mode.ActiveWeapon} (gun on the HUD: {Gun?.Def.Name ?? "none"})");
        }
    }
}
