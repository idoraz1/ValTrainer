using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Hush — Flashbulb (Q). Patch 13.09 notes: FIRE throws it, ALT FIRE "prepares" it, REACTIVATE sends it toward your
/// crosshair; it explodes on any surface or after a delay and flashes everyone looking at it. Here: FIRE throws it
/// (it pops on the first surface it hits or <see cref="MaxFlight"/> s after the throw); ALT FIRE throws it to stick on the
/// first surface it reaches, where it waits (up to <see cref="PreparedLife"/> s) without blinding anyone; the ability key
/// again redirects a flying one, or launches a stuck one, toward the point under your crosshair, once. Riot publishes no
/// numbers for it: every value below is an estimate (docs/research/hush-1.20.md).
/// </summary>
public sealed class Flashbulb : OrbFlash
{
    // All estimates (no published values as of patch 13.09).
    public const float Speed = 20f, Gravity = 0.15f * WorldGravity, MaxFlight = 1.1f, RedirectSpeed = 24f, RedirectFlight = 1.0f;
    public const float Windup = 0.25f, BlindMax = 2f, PreparedLife = 10f, AimRange = 40f;
    public static readonly Color Tint = Color.Color8(235, 120, 200);

    Vector3 vel;
    readonly bool prepare;
    bool stuck, redirected, recastPending, exploding;
    float flightT, explodeAt;

    public Flashbulb(IGame g, Vector3 from, Vector3 dir, bool prepare, bool visual) : base(g, visual, Tint, from)
    {
        this.prepare = prepare;
        vel = dir.Normalized() * Speed;
    }

    /// <summary>Stuck and waiting is quiet; a flying or exploding one can be seen (defenders may turn away).</summary>
    public override bool VisibleToEnemies => !stuck;
    public override bool CanRecast => !Done && !redirected && !exploding;
    /// <summary>It was redirected / launched (dev log and the drill's stats).</summary>
    public bool Redirected => redirected;
    public bool Stuck => stuck;

    public override void Recast()
    {
        if (CanRecast) recastPending = true;
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (exploding)
        {
            PlaceOrb();
            if (OrbOk) Orb!.Windup(Age - explodeAt);
            if (Age - explodeAt >= Windup) Detonate(BlindMax);
            return;
        }
        if (recastPending)
        {
            recastPending = false;
            redirected = true;
            // Toward the point under the crosshair (from your eye), not just parallel to it.
            var aim = input.AimDir.LengthSquared() > 1e-6f ? input.AimDir.Normalized() : vel.Normalized();
            var eye = G.View.Eye;
            float hit = Mathf.Min(AimRange, Collision.FirstHit(eye, aim, G.Solid));
            var to = eye + aim * hit - Pos;
            vel = (to.LengthSquared() > 0.04f ? to.Normalized() : aim) * RedirectSpeed;
            stuck = false;
            flightT = 0f;
            Events.Add(new UtilEvent(UtilEventKind.Landed, Pos, 1f)); // Value 1 = redirect cue
        }
        if (stuck)
        {
            PlaceOrb();
            if (Age >= PreparedLife) Fizzle();
            return;
        }
        flightT += dt;
        if (Step(ref Pos, ref vel, redirected ? 0f : Gravity, dt, G.Solid, out _))
        {
            if (prepare && !redirected)
            {
                stuck = true;
                vel = Vector3.Zero;
                Events.Add(new UtilEvent(UtilEventKind.Landed, Pos, 0f));
                PlaceOrb();
                return;
            }
            Explode();
        }
        else if (flightT >= (redirected ? RedirectFlight : MaxFlight) && !(prepare && !redirected)) Explode();
        else if (prepare && !redirected && flightT >= MaxFlight * 2f) { stuck = true; vel = Vector3.Zero; } // hangs in the air
        if (OffMap(Pos, G.Solid)) { Fizzle(); return; }
        PlaceOrb();
    }

    void Explode()
    {
        exploding = true;
        explodeAt = Age;
        vel = Vector3.Zero;
        PlaceOrb();
    }
}
