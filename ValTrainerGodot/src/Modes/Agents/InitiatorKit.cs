using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

/// <summary>
/// The player's ability in an initiator drill, played like VALORANT: the ability key (imported VALORANT keybind, default
/// C / Q / E / X) equips it — the gun goes away for the ability's equip time — then LMB / RMB cast it (Sova: hold LMB to
/// charge, RMB adds bounces; Skye: keep LMB held to steer). The gun comes back after the ability's put-away time.
/// Pressing the key again while equipped puts it away; while a recastable thing flies (Skye's hawk, Fade's watcher) it
/// re-casts instead (pop / drop). Charges reset every round.
/// </summary>
public sealed class AbilityKit
{
    public enum Phase { Ready, Equipping, Equipped, Charging, Steering }

    public readonly InitiatorAbility A;
    readonly IGame g;
    public int Charges;
    public Phase State { get; private set; } = Phase.Ready;
    public float StateTime { get; private set; }
    /// <summary>Sova: bounces chosen with RMB (0–2).</summary>
    public int Bounces;
    /// <summary>Sova: seconds LMB has been held while charging.</summary>
    public float ChargeHeld;
    /// <summary>Last "can't cast" reason (Breach aiming at nothing), shown by the HUD for a moment.</summary>
    public string? Refusal;
    public float RefusalT;

    bool prevKey, prevAlt, queuedPrimary, queuedAlt;

    /// <summary>Casts the ability: (primary = LMB, charge bars, bounces) → false if it can't be cast here (stays equipped).</summary>
    public Func<bool, int, int, bool>? Cast;
    /// <summary>Something recastable is out (Skye's hawk in flight, Fade's watcher in the air).</summary>
    public Func<bool>? CanRecast;
    public Action? Recast;
    /// <summary>Skye: the hawk still follows the crosshair (LMB held since the throw) — the gun stays away.</summary>
    public Func<bool>? Steering;

    public AbilityKit(IGame g, InitiatorAbility a)
    {
        this.g = g;
        A = a;
        Charges = a.Charges;
    }

    int SlotIndex => (int)A.Slot;
    public string KeyText => Main.I?.Valorant.AbilityBindText(SlotIndex) ?? A.Slot.ToString();
    public bool InHand => State != Phase.Ready;

    bool KeyDown()
    {
        var v = Main.I?.Valorant;
        if (v == null) return false;
        var mb = v.AbilityMouse[SlotIndex];
        if (mb != MouseButton.None) return Input.IsMouseButtonPressed(mb);
        var k = v.AbilityKeys[SlotIndex];
        return k != Key.None && Input.IsKeyPressed(k);
    }

    /// <summary>New round: full charges, gun in hand.</summary>
    public void ResetRound()
    {
        Charges = A.Charges;
        Bounces = 0;
        ChargeHeld = 0;
        queuedPrimary = queuedAlt = false;
        Refusal = null;
        if (State != Phase.Ready) g.SetAbilityInHand(false, 0f);
        State = Phase.Ready;
        StateTime = 0;
    }

    public void AddCharge() => Charges = Math.Min(A.Charges, Charges + 1);

    void Set(Phase p) { State = p; StateTime = 0; }

    public void Equip()
    {
        if (State != Phase.Ready || Charges <= 0) return;
        g.SetAbilityInHand(true);
        Set(Phase.Equipping);
        queuedPrimary = queuedAlt = false;
        AbilitySfx.Play(g, "equip", 0.5f);
    }

    /// <summary>Back to the gun (cancel, or after the cast).</summary>
    public void PutAway(float drawSeconds)
    {
        if (State == Phase.Ready) return;
        Set(Phase.Ready);
        ChargeHeld = 0;
        queuedPrimary = queuedAlt = false;
        g.SetAbilityInHand(false, drawSeconds);
    }

    /// <summary>Dev auto-throw: equip and cast at once (no input).</summary>
    public bool ForceCast(bool primary, int bars, int bounces)
    {
        if (Charges <= 0 || Cast == null) return false;
        if (!Cast(primary, bars, bounces)) return false;
        Charges--;
        return true;
    }

    public void Update(float dt)
    {
        StateTime += dt;
        if (RefusalT > 0) RefusalT -= dt;
        bool key = KeyDown(), keyEdge = key && !prevKey;
        prevKey = key;
        bool alt = Input.IsMouseButtonPressed(MouseButton.Right), altEdge = alt && !prevAlt;
        prevAlt = alt;
        bool fireEdge = g.FirePressed, fireHeld = g.TriggerHeld;

        if (g.Player.Dead)
        {
            if (State != Phase.Ready) PutAway(0f);
            return;
        }

        switch (State)
        {
            case Phase.Ready:
                if (!keyEdge) break;
                if (CanRecast?.Invoke() == true) { Recast?.Invoke(); break; }
                if (Charges <= 0) { AbilitySfx.Play(g, "empty", 0.6f); Refuse("No charges left this round"); break; }
                Equip();
                break;

            case Phase.Equipping:
                if (keyEdge) { PutAway(A.GunBack); break; }
                if (fireEdge) queuedPrimary = true;
                if (altEdge && A.Kind == UtilKind.ReconBolt) Bounces = (Bounces + 1) % 3;
                else if (altEdge && A.Alt != null) queuedAlt = true;
                if (StateTime >= A.Equip) Set(Phase.Equipped);
                break;

            case Phase.Equipped:
                if (keyEdge) { PutAway(A.GunBack); break; }
                if (A.Kind == UtilKind.ReconBolt)
                {
                    if (altEdge) Bounces = (Bounces + 1) % 3;
                    if (fireEdge || (queuedPrimary && fireHeld)) { Set(Phase.Charging); ChargeHeld = 0; queuedPrimary = false; }
                    else if (queuedPrimary) { queuedPrimary = false; DoCast(true, 0); }
                    break;
                }
                if (fireEdge || queuedPrimary) DoCast(true, 0);
                else if (A.Alt != null && (altEdge || queuedAlt)) DoCast(false, 0);
                break;

            case Phase.Charging:
                if (keyEdge) { PutAway(A.GunBack); break; }
                if (altEdge) Bounces = (Bounces + 1) % 3;
                if (fireHeld) ChargeHeld += dt;
                else DoCast(true, ReconBolt.BarsFor(ChargeHeld));
                break;

            case Phase.Steering:
                if (keyEdge && CanRecast?.Invoke() == true) Recast?.Invoke();
                if (Steering?.Invoke() != true) PutAway(A.GunBack);
                break;
        }
    }

    void DoCast(bool primary, int bars)
    {
        queuedPrimary = queuedAlt = false;
        if (Cast == null || !Cast(primary, bars, Bounces))
        {
            // Can't cast here (e.g. Breach with no wall in range): stay equipped.
            if (State == Phase.Charging) { Set(Phase.Equipped); ChargeHeld = 0; }
            return;
        }
        Charges--;
        Bounces = 0;
        ChargeHeld = 0;
        if (A.Kind == UtilKind.GuidingLight && Steering?.Invoke() == true) { Set(Phase.Steering); return; }
        PutAway(A.GunBack);
    }

    public void Refuse(string why)
    {
        Refusal = why;
        RefusalT = 1.6f;
    }
}
