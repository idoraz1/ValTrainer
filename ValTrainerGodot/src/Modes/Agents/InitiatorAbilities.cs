using Godot;

namespace ValTrainer.Modes;

/// <summary>VALORANT ability slots and their default keys: C (grenade slot), Q (ability 1), E (ability 2 / signature), X (ultimate).</summary>
public enum AbilitySlot { C, Q, E, X }

/// <summary>Which initiator-drill ability (how it is cast and what it does).</summary>
public enum UtilKind { Curveball, GuidingLight, Blindside, FlashDrive, Flashpoint, Dizzy, Leer, Paranoia, ReconBolt, Haunt }

/// <summary>
/// A player ability in the initiator drills. Slot, charges and equip times are from the official VALORANT wiki
/// (wiki.playvalorant.com, values to patch 13.0x); the flight numbers live next to each projectile in src/Game/Fx.
/// <see cref="GunBack"/> (from the throw until the gun can fire again) = the wiki's unequip time + ≈0.3 s weapon draw
/// (estimate; 0 draw for Leer's instant re-equip).
/// </summary>
public sealed record InitiatorAbility(
    string AgentKey, string Agent, string Ability, AbilitySlot Slot, UtilKind Kind, int Charges,
    float Equip, float GunBack, Color Color, string Primary, string? Alt, string HowTo)
{
    /// <summary>Flash / nearsight abilities of the Flash &amp; Peek drill, then the recon abilities.</summary>
    public static readonly InitiatorAbility[] All =
    {
        new("phoenix", "Phoenix", "Curveball", AbilitySlot.E, UtilKind.Curveball, 2, 0.8f, 0.78f, Color.Color8(255, 150, 50),
            "LMB: curve left", "RMB: curve right",
            "Bends left (LMB) or right (RMB) and pops 0.6 s after it leaves your hand. Throw it around the corner, not at your own screen."),
        new("skye", "Skye", "Guiding Light", AbilitySlot.E, UtilKind.GuidingLight, 2, 0.8f, 1.15f, Color.Color8(120, 230, 120),
            "LMB: send the hawk · hold to steer", null,
            "LMB sends the hawk; keep LMB held to steer it with your crosshair. Press the key again to pop it — a longer flight blinds longer."),
        new("yoru", "Yoru", "Blindside", AbilitySlot.Q, UtilKind.Blindside, 1, 0.8f, 0.8f, Color.Color8(90, 140, 255),
            "LMB: throw at a wall", null,
            "The shard only arms when it hits a surface: it bounces off and pops 0.6 s later. Bank it off a wall into the site."),
        new("kayo", "KAY/O", "FLASH/drive", AbilitySlot.Q, UtilKind.FlashDrive, 2, 0.8f, 0.9f, Color.Color8(90, 220, 255),
            "LMB: overhand", "RMB: underhand",
            "LMB: overhand — pops 1.6 s after the throw, or 0.8 s after its first bounce. RMB: underhand lob — pops after 1 s."),
        new("breach", "Breach", "Flashpoint", AbilitySlot.Q, UtilKind.Flashpoint, 2, 1.1f, 0.8f, Color.Color8(255, 190, 60),
            "LMB: fire into a wall", null,
            "Aim at a wall up to 35 m away and up to 10 m thick, then fire. The charge bursts out of the far side 0.5 s later."),
        new("gekko", "Gekko", "Dizzy", AbilitySlot.E, UtilKind.Dizzy, 1, 0.9f, 1.15f, Color.Color8(200, 80, 220),
            "LMB: send Dizzy", null,
            "Dizzy flies forward, charges, then spits plasma at every enemy she can see within 45 m. A hit blinds them. Enemies can shoot her first."),
        new("reyna", "Reyna", "Leer", AbilitySlot.C, UtilKind.Leer, 2, 0.77f, 0.5f, Color.Color8(200, 90, 255),
            "LMB: cast the eye", null,
            "The eye drifts 10 m forward, straight through walls. Anyone who looks at it can only see 6 m. It has 60 HP — they will shoot it."),
        new("omen", "Omen", "Paranoia", AbilitySlot.Q, UtilKind.Paranoia, 1, 1.1f, 1.0f, Color.Color8(120, 90, 210),
            "LMB: fire the shadow", null,
            "A shadow flies 25 m straight ahead, through walls. Everyone it touches can only see 7.5 m for 2 s."),
        new("sova", "Sova", "Recon Bolt", AbilitySlot.E, UtilKind.ReconBolt, 1, 0.8f, 0.9f, Color.Color8(80, 200, 255),
            "Hold LMB: charge · release: shoot", "RMB: add a bounce",
            "Hold LMB to charge (3 bars = farther), RMB adds a bounce (0–2). Where the bolt sticks it scans twice and reveals the enemies it can see."),
        new("fade", "Fade", "Haunt", AbilitySlot.E, UtilKind.Haunt, 1, 0.85f, 0.9f, Color.Color8(70, 200, 190),
            "LMB: throw the watcher", null,
            "The watcher flies 1.5 s, then drops (press the key again to drop it early). When it lands it reveals the enemies it can see and leaves a trail to them."),
    };

    public static InitiatorAbility For(string agent, InitiatorAbility fallback) =>
        All.FirstOrDefault(a => a.AgentKey == agent) ?? fallback;

    public static InitiatorAbility Get(string agent) => All.First(a => a.AgentKey == agent);

    /// <summary>Flash-type abilities (blind with the on-screen rule); the rest nearsight or reveal.</summary>
    public bool IsFlash => Kind is UtilKind.Curveball or UtilKind.GuidingLight or UtilKind.Blindside or UtilKind.FlashDrive or UtilKind.Flashpoint or UtilKind.Dizzy;
    public bool IsNearsight => Kind is UtilKind.Leer or UtilKind.Paranoia;
    public bool IsRecon => Kind is UtilKind.ReconBolt or UtilKind.Haunt;
    /// <summary>Pressing the ability key again while it flies does something (Skye: pop, Fade: drop).</summary>
    public bool Recastable => Kind is UtilKind.GuidingLight or UtilKind.Haunt;
}
