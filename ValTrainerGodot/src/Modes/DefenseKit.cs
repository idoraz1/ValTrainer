using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

/// <summary>Which sentinel setup piece (how it is placed and what it does to the attackers).</summary>
public enum SentKind { Alarmbot, Turret, Trapwire, SonicSensor, BarrierMesh, Razorvine, Shear, BarrierOrb, SlowOrb, Chokehold, Interceptor, Trademark }

/// <summary>How a piece is put down: on the floor, on a wall, thrown (lands where its arc meets the floor) or sent a fixed
/// distance ahead.</summary>
public enum SentPlace { Floor, Wall, Thrown, Ahead }

/// <summary>
/// A sentinel ability in the Site Anchor drill. Slot, charges, equip / unequip times and ranges are from the official
/// VALORANT wiki (wiki.playvalorant.com, ability pages as of patch 11–13); effect numbers live next to each device in
/// DefenseDevices.cs. <see cref="Persistent"/> pieces stay where you put them and re-arm every round (like setting the same
/// setup each buy phase); the rest are cast fresh each round.
/// </summary>
public sealed record SentinelAbility(
    string AgentKey, string Agent, string Ability, AbilitySlot Slot, SentKind Kind, int Charges,
    float Equip, float Unequip, float Range, SentPlace Place, bool Persistent, bool Reuse, Color Color,
    string Primary, string? Alt, string HowTo)
{
    static readonly Color KJ = Color.Color8(255, 214, 60), Cy = Color.Color8(215, 230, 245), Dl = Color.Color8(150, 215, 255),
        Vy = Color.Color8(196, 120, 255), Sa = Color.Color8(80, 232, 205), Ve = Color.Color8(255, 120, 90), Ch = Color.Color8(232, 192, 96);

    public static readonly SentinelAbility[] All =
    {
        new("killjoy", "Killjoy", "Alarmbot", AbilitySlot.Q, SentKind.Alarmbot, 1, 0.85f, 0.75f, 10f, SentPlace.Floor, true, false, KJ,
            "LMB: place on the floor", null,
            "Hidden bot on the floor (up to 10 m away). An enemy within 5.5 m sets it off: it chases them and bursts, leaving them Vulnerable (double damage) for 4 s."),
        new("killjoy", "Killjoy", "Turret", AbilitySlot.E, SentKind.Turret, 1, 1.1f, 0.7f, 4.5f, SentPlace.Floor, true, false, KJ,
            "LMB: place, facing where you look", null,
            "Placed up to 4.5 m away, facing where you look. It watches a 100° cone and fires 3-round bursts; every hit slows the target."),
        new("cypher", "Cypher", "Trapwire", AbilitySlot.C, SentKind.Trapwire, 2, 0.8f, 0.7f, 20f, SentPlace.Wall, true, false, Cy,
            "LMB: place on a wall", null,
            "Aim at a wall up to 20 m away: the wire spans to the opposite wall (max 15 m). Whoever crosses it is revealed and slowed, then held in place unless they break it."),
        new("deadlock", "Deadlock", "Sonic Sensor", AbilitySlot.Q, SentKind.SonicSensor, 1, 0.8f, 0.7f, 14f, SentPlace.Wall, true, false, Dl,
            "LMB: place on a wall", null,
            "Wall sensor (up to 14 m away) listening to a 9 × 8 m area in front of it. Running or shooting inside it sets off a concussion (2.5 s). Walking past is silent."),
        new("deadlock", "Deadlock", "Barrier Mesh", AbilitySlot.C, SentKind.BarrierMesh, 1, 0.8f, 0.7f, 22f, SentPlace.Thrown, false, false, Dl,
            "LMB: throw", null,
            "Throw the disc: where it lands, see-through walls grow in four directions (up to 5 m each) that block movement for 30 s until they shoot the orbs."),
        new("vyse", "Vyse", "Razorvine", AbilitySlot.C, SentKind.Razorvine, 1, 0.8f, 0.8f, 22f, SentPlace.Thrown, false, true, Vy,
            "LMB: throw the nest", null,
            "Throw the hidden nest, then press the key again when they're on it: 6.25 m of vines that slow and cut whoever moves through them for 6 s."),
        new("vyse", "Vyse", "Shear", AbilitySlot.Q, SentKind.Shear, 1, 0.7f, 0.8f, 15f, SentPlace.Wall, true, false, Vy,
            "LMB: place on a wall", null,
            "Hidden wall trap (up to 15 m away). When an enemy walks past it, a solid wall bursts up behind them 0.8 s later for 6 s, cutting them off from their team."),
        new("sage", "Sage", "Barrier Orb", AbilitySlot.C, SentKind.BarrierOrb, 1, 0.8f, 0.7f, 15f, SentPlace.Floor, false, false, Sa,
            "LMB: raise the wall", "RMB: rotate",
            "Raise a 10.4 m wall up to 15 m away (RMB rotates it). It blocks sight, bullets and movement; they have to shoot a segment down (400 HP, 600 once fortified)."),
        new("sage", "Sage", "Slow Orb", AbilitySlot.Q, SentKind.SlowOrb, 2, 0.5f, 0.4f, 30f, SentPlace.Thrown, false, false, Sa,
            "LMB: throw", null,
            "Throw it at their path when you hear them: a 7 s field that slows everyone in it by 50% and makes their steps loud."),
        new("veto", "Veto", "Chokehold", AbilitySlot.Q, SentKind.Chokehold, 1, 0.8f, 0.6f, 22f, SentPlace.Thrown, true, false, Ve,
            "LMB: throw", "RMB: lob (short)",
            "Thrown trap that turns invisible. An enemy within 6.6 m in sight of it springs it: everyone in the zone is held in place and decayed (-75 HP) for 4.5 s."),
        new("veto", "Veto", "Interceptor", AbilitySlot.E, SentKind.Interceptor, 1, 0.8f, 0.7f, 7f, SentPlace.Ahead, true, true, Ve,
            "LMB: send it 7 m ahead", null,
            "Sent 7 m ahead. Press the key again to switch it on: for 9 s it zaps enemy utility within 18 m — their flashes die before they pop."),
        new("chamber", "Chamber", "Trademark", AbilitySlot.C, SentKind.Trademark, 1, 1.0f, 0.7f, 12f, SentPlace.Floor, true, false, Ch,
            "LMB: place on the floor", null,
            "Visible floor trap (up to 12 m away). An enemy it sees within 10 m for 0.9 s gets tagged: a 6 m slowing field forms under them for 4 s."),
    };

    public static SentinelAbility[] For(string agent) => All.Where(a => a.AgentKey == agent).ToArray();
}

/// <summary>
/// The player's sentinel abilities in Site Anchor, played like VALORANT: the ability key (imported VALORANT keybind,
/// default C / Q / E) equips it — the gun goes away for the equip time — a placement indicator follows your crosshair
/// (agent colour = valid, red = not), LMB places / throws, RMB is the alt (rotate, lob). Pressing the key again puts it
/// away. Reuse abilities (Razorvine, Interceptor) are activated by pressing the key again while they are out. Pressing the
/// key of a persistent piece with no charges left picks the oldest one up (redeploy).
/// </summary>
public sealed class SentinelKit
{
    public enum Phase { Ready, Equipping, Equipped }

    public readonly SentinelAbility A;
    readonly IGame g;
    public int Charges;
    public Phase State { get; private set; } = Phase.Ready;
    public float StateTime { get; private set; }
    public bool Alt; // Sage: rotated; Veto: lob
    public string? Refusal;
    public float RefusalT;
    bool prevKey, prevAlt;

    /// <summary>Try to place / throw (alt flag) → false = invalid spot (stays equipped).</summary>
    public Func<bool, bool>? Place;
    /// <summary>A reusable piece is out and can be activated now.</summary>
    public Func<bool>? CanReuse;
    public Action? Reuse;
    /// <summary>A persistent piece is placed and can be picked up (charge back).</summary>
    public Func<bool>? CanRecall;
    public Action? Recall;
    /// <summary>Another ability of the kit is equipping/equipped: put it away first.</summary>
    public Action<SentinelKit>? OnEquip;

    public SentinelKit(IGame g, SentinelAbility a) { this.g = g; A = a; Charges = a.Charges; }

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

    void Set(Phase p) { State = p; StateTime = 0; }

    public void Equip()
    {
        if (State != Phase.Ready || Charges <= 0) return;
        OnEquip?.Invoke(this);
        g.SetAbilityInHand(true);
        Set(Phase.Equipping);
        SentinelSfx.Play(g, "place", 0.35f, 1.6f);
    }

    public void PutAway(float drawSeconds)
    {
        if (State == Phase.Ready) return;
        Set(Phase.Ready);
        g.SetAbilityInHand(false, drawSeconds);
    }

    /// <summary>Put away without touching the gun (another ability of the kit takes the hand).</summary>
    public void Drop() { if (State != Phase.Ready) Set(Phase.Ready); }

    public void Refuse(string why) { Refusal = why; RefusalT = 1.6f; }

    public void Update(float dt)
    {
        StateTime += dt;
        if (RefusalT > 0) RefusalT -= dt;
        bool key = KeyDown(), keyEdge = key && !prevKey;
        prevKey = key;
        bool alt = Input.IsMouseButtonPressed(MouseButton.Right), altEdge = alt && !prevAlt;
        prevAlt = alt;
        if (g.Player.Dead) { if (State != Phase.Ready) PutAway(0f); return; }
        switch (State)
        {
            case Phase.Ready:
                if (!keyEdge) break;
                if (CanReuse?.Invoke() == true) { Reuse?.Invoke(); break; }
                if (Charges <= 0 && A.Persistent && CanRecall?.Invoke() == true) { Recall?.Invoke(); Charges++; }
                if (Charges <= 0) { SentinelSfx.Play(g, "reuse", 0.4f, 0.6f); Refuse($"No {A.Ability} charges left this round"); break; }
                Equip();
                break;
            case Phase.Equipping:
                if (keyEdge) { PutAway(A.Unequip); break; }
                if (altEdge && A.Alt != null) Alt = !Alt;
                if (StateTime >= A.Equip) Set(Phase.Equipped);
                break;
            case Phase.Equipped:
                if (keyEdge) { PutAway(A.Unequip); break; }
                if (altEdge && A.Alt != null)
                {
                    if (A.Kind == SentKind.Chokehold) { TryPlace(true); break; }
                    Alt = !Alt;
                }
                if (g.FirePressed) TryPlace(A.Kind == SentKind.BarrierOrb && Alt);
                break;
        }
    }

    void TryPlace(bool alt)
    {
        if (Place == null || !Place(alt)) return;
        Charges--;
        PutAway(A.Unequip);
    }

    /// <summary>New round: full charges, gun in hand.</summary>
    public void ResetRound(int used)
    {
        Charges = Math.Max(0, A.Charges - used);
        Refusal = null;
        if (State != Phase.Ready) g.SetAbilityInHand(false, 0f);
        State = Phase.Ready;
        StateTime = 0;
    }
}
