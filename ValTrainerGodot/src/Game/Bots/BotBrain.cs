using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Bots;

/// <summary>
/// How a bot duels (replaces the old "instant perfect kill"). Calibrated per rank tier:
/// 1. Sees you when any exposed point (head, chest, shoulders) has line of sight and you're within its field of view.
/// 2. Reacts after a human-like delay (normal distribution; faster if it already saw you this fight).
/// 3. Moves its crosshair onto your head (Fitts' law — bigger misplacement = longer settle).
/// 4. Fires the Vandal at 9.75 rps in bursts; each shot hits with a chance that decays during the spray.
/// Damage goes to your 50 shield + 100 HP, so duels are won by whoever hits first and best.
/// </summary>
public sealed class BotBrain
{
    public readonly BotCharacter Body;
    public readonly BotSkill Skill;
    readonly Random rng;
    readonly WeaponDef weapon = Weapons.Vandal;

    /// <summary>Direction the bot is holding / pre-aiming (normalized).</summary>
    public Vector3 HeldDir;
    public float HalfFovDeg = 70f;
    public bool Enabled = true;
    /// <summary>Pre-aims your position (e.g. entry after a flash): halves crosshair error.</summary>
    public bool PreAimed;
    /// <summary>Don't fire while the player is fully blinded (easy tiers in Flash Dodge).</summary>
    public bool HoldFireWhileBlind;
    /// <summary>Engage when only a shoulder is visible (higher tiers). Off = needs head or chest.</summary>
    public bool ShoulderSight = true;

    float? fireAt;
    int shot;
    float alertUntil = -99f;
    bool hadLos;
    public bool SeesPlayer => hadLos;
    public float FirstSeenAt = -1f;

    // ---- utility effects on this bot (initiator drills); times on the session clock (g.Now) ----
    /// <summary>Fully blind until this time: can't spot, aim or shoot, and loses whatever it was engaging.</summary>
    public float BlindUntil = -1f;
    /// <summary>Seeing again but still dazed until this time (flash fade-out / partial flash): slower reaction, worse aim.</summary>
    public float DazedUntil = -1f;
    /// <summary>Nearsighted until this time (Leer, Paranoia): sees the player only within <see cref="NearsightRange"/> metres.</summary>
    public float NearsightUntil = -1f;
    public float NearsightRange = 6f;
    public bool IsBlind(float now) => now < BlindUntil;
    public bool IsDazed(float now) => now < DazedUntil;
    public bool IsNearsighted(float now) => now < NearsightUntil;

    /// <summary>A flash hit this bot: fully blind for <paramref name="fullSeconds"/>, then dazed for <paramref name="dazedSeconds"/> more.</summary>
    public void Flash(float now, float fullSeconds, float dazedSeconds)
    {
        BlindUntil = Mathf.Max(BlindUntil, now + fullSeconds);
        DazedUntil = Mathf.Max(DazedUntil, now + fullSeconds + dazedSeconds);
    }

    /// <summary>Nearsighted for <paramref name="seconds"/>: it only sees within <paramref name="range"/> metres.</summary>
    public void Nearsight(float now, float seconds, float range)
    {
        NearsightUntil = Mathf.Max(NearsightUntil, now + seconds);
        NearsightRange = range;
    }

    public BotBrain(BotCharacter body, BotSkill skill, Random rng)
    {
        Body = body;
        Skill = skill;
        this.rng = rng;
        HeldDir = Vector3.Back; // default: looking toward +Z (the player side of the range)
    }

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    static IEnumerable<Vector3> Exposure(PlayerView v)
    {
        var right = new Vector3(Mathf.Cos(Mathf.DegToRad(v.Yaw)), 0, Mathf.Sin(Mathf.DegToRad(v.Yaw)));
        yield return v.Eye;                                   // head
        yield return v.Eye + Vector3.Down * 0.45f;            // chest
        yield return v.Eye + Vector3.Down * 0.3f + right * 0.3f;
        yield return v.Eye + Vector3.Down * 0.3f - right * 0.3f;
    }

    bool headVisible;

    bool CanSee(IGame g)
    {
        var eye = Body.Head;
        var toPlayer = (g.View.Eye - eye).Normalized();
        headVisible = false;
        if (Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(HeldDir.Dot(toPlayer), -1, 1))) > HalfFovDeg) return false;
        if (g.Now < NearsightUntil && eye.DistanceTo(g.View.Eye) > NearsightRange) return false; // nearsighted: only close range
        headVisible = g.LineOfSight(eye, g.View.Eye);
        if (headVisible) return true;
        foreach (var p in Exposure(g.View).Skip(1).Take(ShoulderSight ? 3 : 1)) // chest, then shoulders
            if (g.LineOfSight(eye, p)) return true;
        return false;
    }

    public void Update(IGame g, float dt)
    {
        if (!Enabled || Body.Dead || g.Player.Dead)
        {
            if (hadLos) { hadLos = false; fireAt = null; shot = 0; Body.AimTarget = null; }
            return;
        }
        if (g.Now < BlindUntil)
        {
            // Flashed: loses whatever it was engaging and re-acquires from scratch afterwards (no "alert" pre-aim).
            if (hadLos) { hadLos = false; fireAt = null; shot = 0; Body.AimTarget = null; }
            alertUntil = -99f;
            return;
        }
        bool los = CanSee(g);
        float now = g.Now;

        if (los && !hadLos) OnLosGained(g, now);
        else if (!los && hadLos)
        {
            alertUntil = now + 1.5f;   // jiggles are pre-aimed next time, not a full reset
            shot = 0;
            fireAt = null;
            Body.AimTarget = null;
        }
        hadLos = los;
        if (!los || fireAt == null) return;

        // Turn to face the player while engaging.
        var to = g.View.Eye - Body.Head;
        Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z));
        HeldDir = to.Normalized();
        Body.AimTarget = g.View.Eye; // aim the rifle up/down at the player

        // Easy tiers wait out your blind, then still need a full reaction before the first shot.
        if (HoldFireWhileBlind && g.Blind.FullyBlind) { fireAt = Mathf.Max(fireAt ?? now, now + Skill.ReactMs / 1000f); return; }
        if (fireAt < now - weapon.Interval * 2) fireAt = now; // never dump a backlog of shots after a hitch
        int guard = 0;
        while (fireAt != null && now >= fireAt && guard++ < 4) FireShot(g);
    }

    void OnLosGained(IGame g, float now)
    {
        if (FirstSeenAt < 0) FirstSeenAt = now;
        bool alert = now < alertUntil;
        float react = Mathf.Max(120f, Gauss(Skill.ReactMs, Skill.ReactSd)) * (alert ? 0.6f : 1f) / 1000f;
        if (now < DazedUntil) react *= 1.8f; // still seeing spots after a flash

        var toHead = (g.View.Eye - Body.Head);
        float dist = Mathf.Max(1f, toHead.Length());
        float placement = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(HeldDir.Dot(toHead.Normalized()), -1, 1)));
        float err = alert || PreAimed ? 0.5f * Mathf.Abs(Gauss(0, Skill.XhairErrDeg))
                                      : Mathf.Sqrt(placement * placement + Mathf.Pow(Gauss(0, Skill.XhairErrDeg), 2));
        if (now < DazedUntil) err = 2f * err + 1.5f; // dazed: worse crosshair placement
        float w = Mathf.RadToDeg(2f * Mathf.Atan(0.14f / dist)); // angular size of a head
        float settle = err <= w / 2 ? 0 : Skill.MsPerBit * Mathf.Log(err / w + 1f) / Mathf.Log(2f) / 1000f;

        fireAt = now + react + settle;
        shot = 0;
    }

    void FireShot(IGame g)
    {
        float interval = weapon.Interval;
        bool moving = Body.Velocity.Length() > 1.5f;
        int s = shot % Skill.Burst; // accuracy recovers between bursts
        float p = Mathf.Max(Skill.PHit1 * 0.35f, Skill.PHit1 * Mathf.Pow(Skill.Decay, s));
        if (!headVisible) p *= 0.5f; // only a shoulder/chest is exposed
        if (g.Now < DazedUntil) p *= 0.6f; // dazed after a flash
        if (moving)
        {
            if (!Skill.RunAndGun) { fireAt += 0.05f; return; } // good players stop before shooting
            p *= 0.25f;
        }

        var target = g.View.Eye + Vector3.Down * 0.35f;
        float dist = (target - Body.Head).Length();
        bool hit = rng.NextDouble() < p;
        if (hit)
        {
            double r = rng.NextDouble();
            int zone = headVisible && r < Skill.PHead1 * (s == 0 ? 1f : 0.6f) ? 0 : (rng.NextDouble() < 0.08 ? 2 : 1);
            g.DamagePlayer(weapon.Damage(zone, dist), Body.Head);
        }
        else
        {
            // A miss lands near you (tracer flies past).
            target += new Vector3(R(-0.6f, 0.6f), R(-0.4f, 0.5f), R(-0.6f, 0.6f));
        }
        Body.ShootFx(target);
        g.SoundAt("enemyshot", Body.Head, maxRange: 60f, floor: 0.6f);

        shot++;
        fireAt += interval;
        if (shot % Skill.Burst == 0) fireAt += Skill.GapMs / 1000f - interval;
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
}
