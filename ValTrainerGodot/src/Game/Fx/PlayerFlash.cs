using Godot;

namespace ValTrainer.Game.Fx;

// Player-thrown flashes for the Flash & Peek drill. Values from the official VALORANT wiki (wiki.playvalorant.com,
// patch 13.0x) unless marked "estimate". Projectile "classes" are the wiki's speed/gravity tiers: class 0.7 = 6.3 m/s,
// class 2 = 18 m/s with gravity ×0.3, class 3 = 29 m/s ×0.45 (of world gravity).

/// <summary>Base for the flashes that fly as a <see cref="FlashOrb"/> and detonate once.</summary>
public abstract class OrbFlash : PlayerUtility
{
    protected FlashOrb? Orb;
    protected OrbFlash(IGame g, bool visual, Color color, Vector3 from) : base(g, visual)
    {
        Pos = from;
        if (visual) { Orb = g.SpawnOrb(color); Orb.GlobalPosition = from; }
    }
    public override bool IsFlash => true;

    protected bool OrbOk => Orb != null && GodotObject.IsInstanceValid(Orb);
    protected void PlaceOrb() { if (OrbOk) Orb!.GlobalPosition = Pos; }

    /// <summary>Detonates: a Pop event for the drill (blind = <paramref name="blindMax"/> s) and the orb's burst.</summary>
    protected void Detonate(float blindMax)
    {
        Events.Add(new UtilEvent(UtilEventKind.Pop, Pos, blindMax));
        if (OrbOk) { Orb!.GlobalPosition = Pos; Orb.Pop(); }
        Orb = null;
        Done = true;
    }

    protected void Fizzle()
    {
        Events.Add(new UtilEvent(UtilEventKind.Fizzle, Pos));
        FreeVisual();
        Done = true;
    }

    public override void FreeVisual()
    {
        if (OrbOk) Orb!.QueueFree();
        Orb = null;
    }
}

/// <summary>Phoenix — Curveball: a fixed-path orb that bends left (fire) or right (alt fire) and pops 0.6 s after release.</summary>
public sealed class Curveball : OrbFlash
{
    public const float Fuse = 0.6f, BlindMax = 1.5f;              // wiki: windup 0.6 s (v11.00), max blind 1.5 s
    const float Speed = 15f, Straight = 0.08f, TurnRate = 2.8f;     // estimates: ≈9 m of travel, ≈80° of bend
    Vector3 vel;
    readonly float side;
    bool stuck;

    public Curveball(IGame g, Vector3 from, Vector3 dir, bool right, bool visual)
        : base(g, visual, Color.Color8(255, 150, 50), from)
    {
        vel = dir.Normalized() * Speed;
        side = right ? 1f : -1f;
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (!stuck)
        {
            // Rotating about +Y by a positive angle turns left; right = negative.
            if (Age > Straight) vel = vel.Rotated(Vector3.Up, -side * TurnRate * dt);
            if (Step(ref Pos, ref vel, 0f, dt, G.Solid, out _)) { stuck = true; vel = Vector3.Zero; }
        }
        PlaceOrb();
        if (OrbOk && Age > Fuse - 0.25f) Orb!.Windup(Age - (Fuse - 0.25f));
        if (Age >= Fuse) Detonate(BlindMax);
    }
}

/// <summary>
/// Skye — Guiding Light: a hawk (18 m/s, up to 2 s) that follows your crosshair while fire stays held from the throw;
/// re-use (or the end of its flight) arms it and it pops 0.3 s later. Blind 1 s, growing to 2.25 s for flights of 0.75 s+.
/// </summary>
public sealed class GuidingLight : OrbFlash
{
    public const float Speed = 18f, MaxFlight = 2f, Windup = 0.3f, FullAfter = 0.75f, BlindMin = 1f, BlindFull = 2.25f; // wiki
    const float TurnRate = 2.6f; // rad/s (≈150°/s) — estimate
    Vector3 dir;
    bool steerable = true, activated;
    float activatedAt;

    /// <summary>Fire is still held since the throw and the hawk follows the crosshair (the gun stays away meanwhile).
    /// True at launch (it is thrown with a fire press); the first update sees whether fire is still held.</summary>
    public bool Steering { get; private set; } = true;
    public override bool CanRecast => !activated && !Done;

    public GuidingLight(IGame g, Vector3 from, Vector3 aim, bool visual)
        : base(g, visual, Color.Color8(120, 230, 120), from) => dir = aim.Normalized();

    public override void Recast()
    {
        if (activated || Done) return;
        activated = true;
        activatedAt = Age;
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        Steering = steerable && !activated && input.FireHeld;
        if (!input.FireHeld) steerable = false;
        if (Steering) dir = TurnToward(dir, input.AimDir.Normalized(), TurnRate * dt);
        if (!activated && Age >= MaxFlight) Recast();

        float speed = activated ? Speed * Mathf.Lerp(1f, 0.3f, Mathf.Clamp((Age - activatedAt) / Windup, 0f, 1f)) : Speed;
        var v = dir * speed;
        if (Step(ref Pos, ref v, 0f, dt, G.Solid, out var n))
        {
            // Skim along walls and floors instead of stopping dead.
            var t = dir - n * dir.Dot(n);
            if (t.LengthSquared() > 0.02f) dir = t.Normalized();
        }
        PlaceOrb();
        if (activated)
        {
            if (OrbOk) Orb!.Windup(Age - activatedAt);
            if (Age - activatedAt >= Windup) Detonate(BlindMin + (BlindFull - BlindMin) * Mathf.Clamp(activatedAt / FullAfter, 0f, 1f));
        }
    }

    public static Vector3 TurnToward(Vector3 from, Vector3 to, float maxAngle)
    {
        float ang = from.AngleTo(to);
        if (ang <= maxAngle || ang < 1e-4f) return to;
        var axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-8f) axis = Vector3.Up;
        return from.Rotated(axis.Normalized(), maxAngle).Normalized();
    }
}

/// <summary>
/// Yoru — Blindside: a class-3 projectile that arms only when it hits a surface — it ricochets off and pops 0.6 s later
/// (max blind 1.5 s). Enemies can't see it before the bounce; with no surface within 2 s it fades out.
/// </summary>
public sealed class Blindside : OrbFlash
{
    public const float Speed = 29f, Gravity = 0.45f * WorldGravity, MaxTravel = 2f, Windup = 0.6f, BlindMax = 1.5f; // wiki
    Vector3 vel;
    bool armed;
    float armedAt;

    public Blindside(IGame g, Vector3 from, Vector3 dir, bool visual)
        : base(g, visual, Color.Color8(90, 140, 255), from) => vel = dir.Normalized() * Speed;

    public override bool VisibleToEnemies => armed;

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (Step(ref Pos, ref vel, Gravity, dt, G.Solid, out var n))
        {
            vel = Bounce(vel, n, 0.45f, 0.7f); // estimate
            if (!armed)
            {
                armed = true;
                armedAt = Age;
                Events.Add(new UtilEvent(UtilEventKind.Landed, Pos));
            }
        }
        PlaceOrb();
        if (!armed)
        {
            if (Age >= MaxTravel || OffMap(Pos, G.Solid)) Fizzle();
            return;
        }
        if (OrbOk) Orb!.Windup(Age - armedAt);
        if (Age - armedAt >= Windup) Detonate(BlindMax);
    }
}

/// <summary>
/// KAY/O — FLASH/drive: a grenade that bounces. Fire = overhand (class 2; pops 1.6 s after the throw or 0.8 s after its
/// first bounce, whichever is sooner); alt fire = underhand lob (class 0.7; pops after 1 s). Max blind 2.25 s; it
/// telegraphs (beeps) for its last 0.3 s.
/// </summary>
public sealed class FlashDrive : OrbFlash
{
    public const float BlindMax = 2.25f, Telegraph = 0.3f;          // wiki
    const float OverSpeed = 18f, UnderSpeed = 6.3f, Grav = 0.3f * WorldGravity, OverFuse = 1.6f, BounceFuse = 0.8f, UnderFuse = 1.0f;
    Vector3 vel;
    float popAt;
    readonly bool overhand;
    bool bounced;

    public FlashDrive(IGame g, Vector3 from, Vector3 dir, bool overhand, bool visual)
        : base(g, visual, Color.Color8(90, 220, 255), from)
    {
        this.overhand = overhand;
        var d = dir.Normalized();
        if (!overhand) d = (d + Vector3.Up * 0.15f).Normalized(); // a lob leaves the hand a little upward
        vel = d * (overhand ? OverSpeed : UnderSpeed);
        popAt = overhand ? OverFuse : UnderFuse;
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (Step(ref Pos, ref vel, Grav, dt, G.Solid, out var n))
        {
            vel = Bounce(vel, n, 0.42f, 0.72f); // estimate
            if (n.Y > 0.7f && Mathf.Abs(vel.Y) < 0.8f) vel.Y = 0f; // settles and rolls on the floor
            if (!bounced && overhand) popAt = Mathf.Min(popAt, Age + BounceFuse);
            bounced = true;
        }
        PlaceOrb();
        if (OrbOk && Age > popAt - Telegraph) Orb!.Windup(Age - (popAt - Telegraph));
        if (Age >= popAt) Detonate(BlindMax);
    }
}

/// <summary>
/// Breach — Flashpoint: fired into a wall up to 35 m away and up to 10 m thick. The charge flies to the wall (24 m/s),
/// burns through and bursts out of the far side, popping 0.5 s after it emerges (max blind 2.25 s).
/// </summary>
public sealed class Flashpoint : OrbFlash
{
    public const float MaxRange = 35f, MaxThickness = 10f, Speed = 24f, Windup = 0.5f, BlindMax = 2.25f; // wiki
    const float Drift = 3f; // m/s out of the wall during the windup — estimate
    readonly Vector3 entry, exit, dir;
    readonly float toWall;
    bool emerged;
    float emergedAt;
    readonly Vector3 start;

    public Flashpoint(IGame g, Vector3 from, Vector3 entry, Vector3 exit, Vector3 dir, bool visual)
        : base(g, visual, Color.Color8(255, 190, 60), from)
    {
        start = from;
        this.entry = entry; this.exit = exit; this.dir = dir.Normalized();
        toWall = from.DistanceTo(entry) / Speed;
    }

    public override bool VisibleToEnemies => emerged;

    /// <summary>Finds the wall Breach's charge would burn through along <paramref name="dir"/> (null reason = valid).</summary>
    public static string? Aim(IReadOnlyList<Box> solid, Vector3 eye, Vector3 dir, out Vector3 entry, out Vector3 exit)
    {
        dir = dir.Normalized();
        entry = exit = eye;
        float t = Collision.FirstHit(eye, dir, solid, out int i);
        if (i < 0 || t > MaxRange) return "No wall within 35 m";
        entry = eye + dir * t;
        for (float s = 0.05f; s <= MaxThickness; s += 0.05f)
        {
            var p = entry + dir * s;
            if (Inside(p, solid, 0f)) continue;
            exit = p + dir * 0.25f;
            if (Inside(exit, solid, 0f) || OffMap(exit, solid)) return "Nothing on the other side";
            if (Collision.FirstHit(exit, Vector3.Down, solid) > 8f) return "Nothing on the other side";
            return null;
        }
        return "Wall too thick (max 10 m)";
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (!emerged)
        {
            Pos = start.Lerp(entry, Mathf.Clamp(Age / Mathf.Max(0.01f, toWall), 0f, 1f));
            if (Age >= toWall)
            {
                emerged = true;
                emergedAt = Age;
                Pos = exit;
                Events.Add(new UtilEvent(UtilEventKind.Landed, entry));
            }
        }
        else
        {
            var v = dir * Drift;
            Step(ref Pos, ref v, 0f, dt, G.Solid, out _);
        }
        PlaceOrb();
        if (!emerged) return;
        if (OrbOk) Orb!.Windup(Age - emergedAt);
        if (Age - emergedAt >= Windup) Detonate(BlindMax);
    }
}

/// <summary>
/// Gekko — Dizzy: flies forward for 0.65 s, charges 0.35 s, then for 1 s spits plasma at every enemy she can see within
/// 45 m (each blob blinds within 2.5 m: 1 s full + 1 s fade). 20 HP — enemies can shoot her down first.
/// </summary>
public sealed class Dizzy : OrbFlash
{
    public const float Flight = 0.65f, Charge = 0.35f, Active = 1f, Range = 45f, Health = 20f;   // wiki
    const float Speed = 16f; // estimate (≈10 m before she stops)
    Vector3 vel;
    float lastFire = -9f;
    Vector3 hoverAt;
    bool hovering;

    public Dizzy(IGame g, Vector3 from, Vector3 dir, bool visual)
        : base(g, visual, Color.Color8(200, 80, 220), from)
    {
        vel = dir.Normalized() * Speed;
        Hp = Health;
    }

    public override bool IsFlash => false; // nobody dodges Dizzy by turning away: her blobs hit you
    public override bool Shootable => !Done;
    public bool Firing => Age >= Flight + Charge && Age < Flight + Charge + Active;

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (Age < Flight)
        {
            if (Step(ref Pos, ref vel, 0f, dt, G.Solid, out _)) vel = Vector3.Zero;
        }
        else
        {
            if (!hovering) { hovering = true; hoverAt = Pos; }
            Pos = hoverAt + new Vector3(0, 0.08f * Mathf.Sin(Age * 9f), 0);
        }
        PlaceOrb();
        if (OrbOk && Age >= Flight) Orb!.Windup(Age - Flight);
        if (Firing && Age - lastFire >= 0.25f)
        {
            lastFire = Age;
            Events.Add(new UtilEvent(UtilEventKind.DizzyFire, Pos, Range));
        }
        if (Age >= Flight + Charge + Active)
        {
            // Spent: she turns back into a goo blob (no flash of her own).
            if (OrbOk) Orb!.QueueFree();
            Orb = null;
            Done = true;
        }
    }
}

/// <summary>One Dizzy plasma blob flying at 100 m/s to where an enemy stood; it bursts there.</summary>
public sealed class PlasmaShot : PlayerUtility
{
    public const float Speed = 100f, Radius = 2.5f, BlindFull = 1f; // wiki: v7.12 missile speed 10000, 2.5 m burst, 1 s + 1 s fade
    readonly Vector3 from, to;
    readonly float flight;

    public PlasmaShot(IGame g, Vector3 from, Vector3 to, bool visual) : base(g, visual)
    {
        this.from = from; this.to = to;
        Pos = from;
        flight = from.DistanceTo(to) / Speed;
        if (visual) Effects.Tracer(from, to, new Color(1.6f, 0.45f, 1.9f));
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        Pos = from.Lerp(to, Mathf.Clamp(Age / Mathf.Max(0.001f, flight), 0f, 1f));
        if (Age < flight) return;
        Events.Add(new UtilEvent(UtilEventKind.Plasma, to, BlindFull));
        if (Visual)
        {
            var o = G.SpawnOrb(Color.Color8(200, 80, 220));
            o.GlobalPosition = to;
            o.Pop();
        }
        Done = true;
    }
}
