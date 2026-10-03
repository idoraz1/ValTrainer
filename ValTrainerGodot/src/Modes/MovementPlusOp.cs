using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// How an Operator holder plays, per rank tier (Rookie … Pro). Reaction and aim-settle come from the tier's
/// <see cref="BotSkill"/>; these are the Op-specific parts.
/// </summary>
/// <param name="AimSd">Aim error (deg, per axis) on a target standing still.</param>
/// <param name="AirMul">Aim error × this on an airborne target (vertical motion is hard to track).</param>
/// <param name="MoveMul">Aim error × this on a target strafing on the ground.</param>
/// <param name="Predict">How much of the target's motion he leads (0 = shoots where he last saw you).</param>
/// <param name="Commit">He still fires if you vanished at most this long (s) before his click: the bait window.
/// Low tiers shoot late at where you were; high tiers only shoot what they still see.</param>
/// <param name="Bite">Chance he engages when only a shoulder shows.</param>
/// <param name="Adapt">After a jump-peek, how far he moves his held aim up toward where your head appeared (0–1).</param>
/// <param name="Reacquire">After the bolt cycle, time (s) to re-scope and re-settle on a target he has been watching.</param>
/// <param name="Lag">Seconds between what he sees and where his click lands (perception + motor delay).</param>
public sealed record OpSkill(float AimSd, float AirMul, float MoveMul, float Predict, float Commit, float Bite, float Adapt, float Reacquire, float Lag)
{
    public static OpSkill For(int tier) => Math.Clamp(tier, 0, 4) switch
    {
        // Bite: lower-ranked Op players take the shoulder bait almost every time; the best ones bite less often.
        0 => new(0.55f, 2.6f, 1.6f, 0.0f, 0.30f, 1.00f, 0.00f, 0.45f, 0.12f),
        1 => new(0.42f, 2.3f, 1.5f, 0.3f, 0.25f, 0.95f, 0.15f, 0.35f, 0.11f),
        2 => new(0.30f, 2.0f, 1.35f, 0.55f, 0.18f, 0.90f, 0.35f, 0.28f, 0.10f),
        3 => new(0.22f, 1.7f, 1.25f, 0.75f, 0.13f, 0.80f, 0.55f, 0.22f, 0.08f),
        _ => new(0.16f, 1.45f, 1.15f, 0.9f, 0.10f, 0.65f, 0.70f, 0.18f, 0.06f),
    };
}

/// <summary>The player's hit volumes (VALORANT zones: head, body incl. shoulders/arms, legs) and the points a bot can spot.</summary>
public static class PlayerHitbox
{
    public enum Part { Head, Chest, ShoulderL, ShoulderR }

    static Vector3 Right(float yawDeg) => new(Mathf.Cos(Mathf.DegToRad(yawDeg)), 0, Mathf.Sin(Mathf.DegToRad(yawDeg)));

    /// <summary>Where a spotting point is for a body with this eye and facing.</summary>
    public static Vector3 PartAt(Part p, Vector3 eye, float yawDeg) => p switch
    {
        Part.Head => eye + new Vector3(0, 0.03f, 0),
        Part.Chest => eye + Vector3.Down * 0.45f,
        Part.ShoulderL => eye + Vector3.Down * 0.3f - Right(yawDeg) * 0.3f,
        _ => eye + Vector3.Down * 0.3f + Right(yawDeg) * 0.3f,
    };

    /// <summary>Ray (normalized <paramref name="d"/>) against the body: zone and distance (HitZone.None = miss).</summary>
    public static HitZone Ray(Vector3 eye, float eyeHeight, float yawDeg, Vector3 o, Vector3 d, out float dist)
    {
        var right = Right(yawDeg);
        var feet = eye - new Vector3(0, eyeHeight, 0);
        float head = BotHitMath.Sphere(o, d, eye + new Vector3(0, 0.03f, 0), 0.15f);
        float torso = BotHitMath.Capsule(o, d, eye + Vector3.Down * 0.28f, eye + Vector3.Down * 0.8f, 0.2f);
        var sh = eye + Vector3.Down * 0.3f;
        float arms = BotHitMath.Capsule(o, d, sh - right * 0.3f, sh + right * 0.3f, 0.09f);
        var hip = eye + Vector3.Down * 0.85f;
        float legs = hip.Y > feet.Y + 0.12f ? BotHitMath.Capsule(o, d, feet + new Vector3(0, 0.1f, 0), hip, 0.14f) : float.PositiveInfinity;
        float body = Mathf.Min(torso, arms);
        dist = Mathf.Min(head, Mathf.Min(body, legs));
        if (float.IsPositiveInfinity(dist)) return HitZone.None;
        return dist == head ? HitZone.Head : dist == body ? HitZone.Body : HitZone.Legs;
    }
}

/// <summary>One Operator shot: did it hit, where, how much, and was the target in the air.</summary>
public readonly record struct OpShot(bool Hit, HitZone Zone, float Damage, bool TargetAirborne, Vector3 End);

/// <summary>
/// An enemy holding an angle with a scoped Operator (no peeking, no moving):
/// <list type="number">
/// <item>Sees you when a spotting point (head, chest, a shoulder if the tier engages shoulders) is in his scoped view
/// (±26°; ±50° unscoped during the bolt) with line of sight.</item>
/// <item>Reacts (tier reaction time; 0.6× when alert from a recent peek), then flicks onto the part he saw (Fitts' law:
/// a jump-peek's head appears well above his pre-aim, so the flick is longer).</item>
/// <item>Clicks at the position he perceived 60–120 ms earlier (by tier) plus a tier-dependent lead, with Gaussian aim error that's
/// larger on airborne / strafing targets. The bullet is a real ray against your body at that instant, so ducking back
/// in time makes it hit the wall. If you vanished more than <see cref="OpSkill.Commit"/> before his click, he holds fire.</item>
/// <item>One shot: head 255, body 150 (lethal at 100 HP + 50 shields), legs 120. Then the bolt cycle (0.6 shots/s =
/// 1.67 s, unscoped) before the next shot: the window to punish a missed shot.</item>
/// </list>
/// </summary>
public sealed class OpBotBrain
{
    public enum St { Holding, Engaging, Cycling }

    public readonly BotCharacter Body;
    public readonly OpSkill Skill;
    readonly BotSkill human;
    readonly Random rng;
    readonly WeaponDef op = Weapons.Operator;

    /// <summary>Where his crosshair rests (world point, normally the corner at head height).</summary>
    public Vector3 HeldPoint;
    /// <summary>Engages a lone shoulder (tier ≥ Veteran).</summary>
    public bool ShoulderSight;
    public const float ScopedHalfFov = 26f, UnscopedHalfFov = 50f;

    public St State { get; private set; } = St.Holding;
    /// <summary>When the bolt cycle ends (next shot possible).</summary>
    public float ReadyAt { get; private set; }
    public bool Scoped => State != St.Cycling;
    /// <summary>He can see some part of you right now (any spotting point, regardless of his shoulder discipline).</summary>
    public bool SeesPlayer { get; private set; }
    public int ShotsFired { get; private set; }
    public float LastShotAt { get; private set; } = -99f;
    public OpShot LastShot { get; private set; }
    /// <summary>Raised after every shot (hit or miss).</summary>
    public event Action<OpShot>? Fired;
    /// <summary>He reacted to you (started his flick).</summary>
    public event Action? Engaged;
    /// <summary>He reacted, but you were gone before he could click: no shot.</summary>
    public event Action? HeldFire;

    float fireAt, lastSeenAt = -99f, alertUntil = -99f, firstSeenAt = -1f;
    PlayerHitbox.Part target;
    bool ignoreThisExposure, airborneAtEngage;

    readonly record struct Sample(float T, Vector3 Eye, float Yaw, bool Air);
    readonly List<Sample> hist = new(128);

    public OpBotBrain(BotCharacter body, int tier, Random rng)
    {
        Body = body;
        Skill = OpSkill.For(tier);
        human = Difficulty.Get(tier).Bot;
        this.rng = rng;
        ShoulderSight = true; // an Op holding an angle shoots any body part he sees, at every tier; how often he bites is Skill.Bite
    }

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    Vector3 Eye => Body.Head;
    Vector3 HeldDir => (HeldPoint - Eye).Normalized();

    /// <summary>Which spotting points he can see (bit per <see cref="PlayerHitbox.Part"/>).</summary>
    int Visible(IGame g)
    {
        float half = Scoped ? ScopedHalfFov : UnscopedHalfFov;
        // Holding / cycling: looking at the held angle. Engaging: his scope is on you.
        var look = State == St.Engaging ? (g.View.Eye - Eye).Normalized() : HeldDir;
        int mask = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = PlayerHitbox.PartAt((PlayerHitbox.Part)i, g.View.Eye, g.View.Yaw);
            var to = (p - Eye).Normalized();
            if (Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(look.Dot(to), -1, 1))) > half) continue;
            if (g.LineOfSight(Eye, p)) mask |= 1 << i;
        }
        return mask;
    }

    static bool Has(int mask, PlayerHitbox.Part p) => (mask & (1 << (int)p)) != 0;

    public void Update(IGame g, float dt)
    {
        float now = g.Now;
        hist.Add(new Sample(now, g.View.Eye, g.View.Yaw, !g.PlayerGrounded));
        while (hist.Count > 2 && now - hist[0].T > 0.6f) hist.RemoveAt(0);
        if (Body.Dead || g.Player.Dead)
        {
            SeesPlayer = false;
            if (State == St.Engaging) State = St.Holding;
            return;
        }

        int vis = Visible(g);
        SeesPlayer = vis != 0;
        bool core = Has(vis, PlayerHitbox.Part.Head) || Has(vis, PlayerHitbox.Part.Chest);
        bool shoulder = Has(vis, PlayerHitbox.Part.ShoulderL) || Has(vis, PlayerHitbox.Part.ShoulderR);
        if (!SeesPlayer) ignoreThisExposure = false;
        bool engageable = core || (shoulder && ShoulderSight && !ignoreThisExposure);

        switch (State)
        {
            case St.Cycling:
                if (engageable) lastSeenAt = now;
                if (now >= ReadyAt)
                {
                    State = St.Holding;
                    alertUntil = now + 1.5f;
                    if (engageable)
                    {
                        // He watched you through the bolt cycle: re-scope and settle, no fresh reaction needed.
                        target = Pick(vis);
                        fireAt = now + Skill.Reacquire;
                        State = St.Engaging;
                        airborneAtEngage = !g.PlayerGrounded;
                    }
                }
                break;

            case St.Holding:
                if (!engageable) break;
                if (!core && rng.NextDouble() > Skill.Bite) { ignoreThisExposure = true; break; } // didn't bite on the shoulder
                Engage(g, vis, now);
                break;

            case St.Engaging:
                if (engageable) { lastSeenAt = now; target = Pick(vis); } // aims at the best part he sees right now
                if (now >= fireAt)
                {
                    if (now - lastSeenAt <= Skill.Commit) Fire(g, now);
                    else { State = St.Holding; alertUntil = now + 1.5f; HeldFire?.Invoke(); } // you were gone before he could click
                }
                else if (!engageable && now - lastSeenAt > Skill.Commit + 0.3f) { State = St.Holding; alertUntil = now + 1.5f; HeldFire?.Invoke(); }
                break;
        }

        // Face and point the rifle: at you while engaging, else at the held angle.
        var aim = State == St.Engaging && now - lastSeenAt < 0.3f ? g.View.Eye : HeldPoint;
        var dir = aim - Eye;
        Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        Body.AimTarget = aim;
    }

    PlayerHitbox.Part Pick(int vis) =>
        Has(vis, PlayerHitbox.Part.Head) ? PlayerHitbox.Part.Head :
        Has(vis, PlayerHitbox.Part.Chest) ? PlayerHitbox.Part.Chest :
        Has(vis, PlayerHitbox.Part.ShoulderR) ? PlayerHitbox.Part.ShoulderR : PlayerHitbox.Part.ShoulderL;

    void Engage(IGame g, int vis, float now)
    {
        bool alert = now < alertUntil;
        target = Pick(vis);
        if (firstSeenAt < 0) firstSeenAt = now;
        float react = Mathf.Max(120f, Gauss(human.ReactMs, human.ReactSd)) * (alert ? 0.6f : 1f) / 1000f;
        var p = PlayerHitbox.PartAt(target, g.View.Eye, g.View.Yaw);
        float dist = Mathf.Max(1f, (p - Eye).Length());
        float amp = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(HeldDir.Dot((p - Eye).Normalized()), -1, 1)));
        float size = target == PlayerHitbox.Part.Chest ? 0.4f : target == PlayerHitbox.Part.Head ? 0.28f : 0.18f;
        float w = Mathf.RadToDeg(2f * Mathf.Atan(size / 2f / dist));
        float settle = amp <= w / 2 ? 0 : human.MsPerBit * Mathf.Log(amp / w + 1f) / Mathf.Log(2f) / 1000f;
        fireAt = now + react + settle;
        lastSeenAt = now;
        airborneAtEngage = !g.PlayerGrounded;
        State = St.Engaging;
        Engaged?.Invoke();
        // A jump-peek makes him hold higher next time (better players adapt more).
        if (airborneAtEngage && Skill.Adapt > 0) HeldPoint = HeldPoint.Lerp(new Vector3(HeldPoint.X, p.Y, HeldPoint.Z), Skill.Adapt);
    }

    /// <summary>History sample at time <paramref name="t"/> (nearest earlier one).</summary>
    Sample At(float t)
    {
        for (int i = hist.Count - 1; i >= 0; i--) if (hist[i].T <= t) return hist[i];
        return hist[0];
    }

    void Fire(IGame g, float now)
    {
        float lag = Skill.Lag;
        var s = At(now - lag);
        var s0 = At(now - lag - 0.05f);
        var p = PlayerHitbox.PartAt(target, s.Eye, s.Yaw);
        var p0 = PlayerHitbox.PartAt(target, s0.Eye, s0.Yaw);
        var vel = s.T - s0.T > 0.005f ? (p - p0) / (s.T - s0.T) : Vector3.Zero;
        var aim = p + vel * (lag * Skill.Predict);
        float sd = Skill.AimSd * (s.Air ? Skill.AirMul : new Vector2(vel.X, vel.Z).Length() > 1.5f ? Skill.MoveMul : 1f);
        var dir = (aim - Eye).Normalized();
        var right = dir.Cross(Vector3.Up).Normalized();
        var up = right.Cross(dir);
        dir = (dir + right * Mathf.Tan(Mathf.DegToRad(Gauss(0, sd))) + up * Mathf.Tan(Mathf.DegToRad(Gauss(0, sd)))).Normalized();

        float wall = Collision.FirstHit(Eye, dir, g.Solid);
        var zone = PlayerHitbox.Ray(g.View.Eye, g.Mover.EyeHeight, g.View.Yaw, Eye, dir, out float body);
        bool hit = zone != HitZone.None && body < wall;
        float dmg = hit ? op.Damage((int)zone, body) : 0f;
        var end = Eye + dir * Mathf.Min(hit ? body : wall, 120f);
        bool air = !g.PlayerGrounded;

        ShotsFired++;
        LastShotAt = now;
        State = St.Cycling;
        ReadyAt = now + op.Interval;
        alertUntil = ReadyAt + 1.5f;
        Body.ShootFx(end);
        g.SoundAt("operator", Eye, maxRange: 120f, floor: 0.65f, vol: 0.9f);
        var bodyRef = Body;
        Body.GetTree().CreateTimer(0.5).Timeout += () => { if (GodotObject.IsInstanceValid(bodyRef) && !bodyRef.Dead) g.SoundAt("bolt", bodyRef.Head, maxRange: 60f, floor: 0.5f, vol: 0.7f); };
        LastShot = new OpShot(hit, zone, dmg, air, end);
        Fired?.Invoke(LastShot);            // before the damage: a kill ends the round (the mode drops this bot)
        if (hit) g.DamagePlayer(dmg, Eye);
    }
}

/// <summary>
/// Shared arena for the Operator peeking drills: you start in a pocket behind a tall corner (east edge at x = 0); north of
/// it a long lane where one Operator holds the corner from a random spot (some on a raised platform). Walls are 4.5 m
/// (no jumping out). Rounds: the Op is placed out of sight; HP refills each round; dying ends the round.
/// </summary>
public abstract class OpAngleMode : MovementMode
{
    public override string Category => "Movement";
    public override float Duration => 90f;
    public override string SimKind => "bot";
    public override Vector3 StartFeet => new(-0.7f, 0, 1.0f);
    public override float StartYaw => 10f;

    /// <summary>Where a peek starts showing you: the corner's east edge.</summary>
    protected const float CornerX = 0f;
    const float WallH = 4.5f;

    public override IEnumerable<Box> ExtraSolids => new[]
    {
        new Box(new Vector3(-12, 0, -1.6f), new Vector3(CornerX, WallH, -0.2f)),        // the corner you peek
        new Box(new Vector3(-3, 0, -0.2f), new Vector3(-2.5f, WallH, 3.5f)),             // pocket, west
        new Box(new Vector3(-3, 0, 3.0f), new Vector3(11, WallH, 3.5f)),                 // pocket, south (behind you)
        new Box(new Vector3(10.5f, 0, -50.5f), new Vector3(11, WallH, 3.5f)),            // lane, east
        new Box(new Vector3(-12.5f, 0, -50.5f), new Vector3(-12, WallH, -0.2f)),         // lane, west
        new Box(new Vector3(-12.5f, 0, -51), new Vector3(11, 6, -50.5f)),                // lane, far end
        new Box(new Vector3(-10.5f, 0, -45.5f), new Vector3(-6.5f, 2.4f, -40.5f)),       // raised platform ("heaven")
        new Box(new Vector3(1.6f, 0, -33.4f), new Vector3(3.0f, 1.2f, -32.0f)),          // crate
        new Box(new Vector3(-6.2f, 0, -21.0f), new Vector3(-4.6f, 1.4f, -19.4f)),        // crate
        new Box(new Vector3(5.6f, 0, -24.4f), new Vector3(7.3f, 2.2f, -22.6f)),          // tall crate
        new Box(new Vector3(-11.5f, 0, -12.5f), new Vector3(-9.8f, 1.6f, -10.8f)),       // crate
    };

    /// <summary>Named Op spots (feet). Each round picks one that can't see your start and that a peek reveals.</summary>
    protected abstract (string Name, Vector3 Feet)[] Spots { get; }
    /// <summary>Seconds a round may last before the Op repositions.</summary>
    protected virtual float RoundTime => 14f;

    protected BotCharacter? Bot;
    protected OpBotBrain? Op;
    protected string SpotName = "";
    protected float RoundT, Between = -1f;
    protected int Rounds;
    protected float DamageTaken;
    int lastSpot = -1;
    float seenByYouFor, exposureStart = -1f;
    bool exposureAir;
    protected bool SeenThisRound;
    protected readonly List<float> Exposures = new();
    protected float PingUntil = -1f;
    /// <summary>The Op's last shot missed you and he's still cycling the bolt: the punish window.</summary>
    protected bool PunishWindow => Op is { State: OpBotBrain.St.Cycling } o && !o.LastShot.Hit && Bot is { Dead: false };

    public override AimFocus? Focus => Bot != null && !Bot.Dead && GodotObject.IsInstanceValid(Bot) && Between < 0
        ? new AimFocus(IdOf(Bot), Bot.Head, BotCharacter.HeadRadius) : null;

    protected override void Setup()
    {
        NewRound();
        if (Main.I.Dev && MovementScript.Auto) Mover.KeyOverride = k => autoKeys.Contains(k);
    }

    protected void NewRound()
    {
        if (Bot != null && GodotObject.IsInstanceValid(Bot) && !Bot.Dead) G.Despawn(Bot);
        Bot = null; Op = null;
        G.Respawn(StartFeet, StartYaw);
        var startEye = StartFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        var peekEye = new Vector3(CornerX + 1.2f, PlayerView.EyeHeight, StartFeet.Z);
        var order = Enumerable.Range(0, Spots.Length).OrderBy(_ => Rng.Next()).ToList();
        int pick = -1;
        Vector3 feet = Vector3.Zero;
        foreach (int i in order)
        {
            if (i == lastSpot && Spots.Length > 1) continue;
            var f = Spots[i].Feet + new Vector3(R(-0.6f, 0.6f), 0, R(-0.6f, 0.6f));
            var head = f + new Vector3(0, PlayerView.EyeHeight, 0);
            bool hidden = Enumerable.Range(0, 4).All(p => !G.LineOfSight(head, PlayerHitbox.PartAt((PlayerHitbox.Part)p, startEye, StartYaw)))
                          && !G.LineOfSight(startEye, head);
            if (!hidden || !G.LineOfSight(peekEye, head)) continue;
            pick = i; feet = f;
            break;
        }
        if (pick < 0) { pick = order[0]; feet = Spots[pick].Feet; }
        lastSpot = pick;
        SpotName = Spots[pick].Name;
        Bot = G.SpawnBot(feet);
        Op = new OpBotBrain(Bot, Tier, Rng);
        // He holds the corner at head height, with the tier's crosshair-placement error.
        var corner = new Vector3(CornerX + 0.25f, PlayerView.EyeHeight, -0.9f);
        float d = corner.DistanceTo(Bot.Head);
        float err = Difficulty.Get(Tier).Bot.XhairErrDeg * 0.25f; // a known corner: tighter than a roaming bot's placement
        Op.HeldPoint = corner + new Vector3(Gauss0() * err, Gauss0() * err * 0.6f, 0) * Mathf.DegToRad(1f) * d;
        Op.Fired += OnOpFired;
        Op.Engaged += () => ReactedThisPeek = true;
        Op.HeldFire += OnOpHeldFire;
        var toCorner = corner - Bot.Head;
        Bot.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(toCorner.X, -toCorner.Z));
        Bot.AimTarget = Op.HeldPoint;
        RoundT = 0; Between = -1f; SeenThisRound = false; seenByYouFor = 0; exposureStart = -1f; PingUntil = -1f;
        Rounds++;
        OnNewRound();
    }

    float Gauss0()
    {
        double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    protected virtual void OnNewRound() { }
    /// <summary>The player saw the Op long enough to know where he is (once per round).</summary>
    protected virtual void OnSpotted(bool airborne) { }
    protected virtual void OnOpFired(OpShot s) { if (s.Hit) DamageTaken += Mathf.Min(s.Damage, 150f); }
    /// <summary>He reacted to your peek but you were gone before he could click.</summary>
    protected virtual void OnOpHeldFire() { }
    /// <summary>The Op reacted (began his flick) during the current exposure.</summary>
    protected bool ReactedThisPeek;
    /// <summary>The player killed the Op.</summary>
    protected virtual void OnOpKilled(bool inPunishWindow) { }
    /// <summary>One continuous exposure (the Op could see you) ended.</summary>
    protected virtual void OnExposureEnded(float seconds, bool airborne) { }
    /// <summary>The round ran out without a kill or death.</summary>
    protected virtual void OnRoundTimeout() { }

    /// <summary>End the round (next one after a short pause).</summary>
    protected void EndRound(float pause = 1.2f) { Between = pause; }

    public override void Update(float dt)
    {
        if (Between >= 0)
        {
            Between -= dt;
            if (Between < 0) NewRound();
            AutoPilot(dt);
            return;
        }
        RoundT += dt;
        if (Bot == null || Op == null) return;
        Op.Update(G, dt);
        TrackSeen(Bot);
        if (Between >= 0) return; // died this frame

        // How long the Op could see you, per peek.
        if (Op.SeesPlayer && exposureStart < 0) { exposureStart = Now; exposureAir = !G.PlayerGrounded; }
        else if (!Op.SeesPlayer && exposureStart >= 0)
        {
            float e = Now - exposureStart;
            exposureStart = -1f;
            Exposures.Add(e);
            OnExposureEnded(e, exposureAir);
            ReactedThisPeek = false;
        }

        // Spotting: he's on your screen with line of sight for a moment.
        if (!SeenThisRound && !Bot.Dead)
        {
            if (OnScreen(Bot.Head) && (G.LineOfSight(G.View.Eye, Bot.Head) || G.LineOfSight(G.View.Eye, Bot.Head + Vector3.Down * 0.45f)))
            {
                seenByYouFor += dt;
                if (seenByYouFor >= 0.06f)
                {
                    SeenThisRound = true;
                    PingUntil = Now + 2.5f;
                    if (Bot.SpawnTime < Now) Bot.SpawnTime = Now - seenByYouFor; // time-to-kill counts from first sight
                    OnSpotted(!G.PlayerGrounded);
                }
            }
        }

        if (RoundT > RoundTime && Between < 0 && !PunishWindow) { OnRoundTimeout(); EndRound(0.6f); }
        AutoPilot(dt);
    }

    /// <summary>Inside the visible screen area (approximate 16:9 frustum).</summary>
    protected bool OnScreen(Vector3 p)
    {
        var d = (p - G.View.Eye).Normalized();
        float yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        float dy = Mathf.Abs(Mathf.Wrap(yaw - G.View.ViewYaw, -180f, 180f)), dp = Mathf.Abs(pitch - G.View.ViewPitch);
        float hHalf = G.View.HFov / 2f - 2f;
        float vHalf = Mathf.RadToDeg(Mathf.Atan(Mathf.Tan(Mathf.DegToRad(G.View.HFov / 2f)) * 9f / 16f)) - 2f;
        return dy < hHalf && dp < vHalf;
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Event("round_lost");
        G.Banner("YOU DIED — the Op saw too much of you", UiTheme.Accent);
        if (Bot != null) { var b = Bot; b.GetTree().CreateTimer(0.8).Timeout += () => G.Despawn(b); }
        Bot = null; Op = null;
        EndRound(1.4f);
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (Bot == null || Between >= 0) return float.PositiveInfinity;
        bool window = PunishWindow;
        var hit = ShootBots(new[] { Bot }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 30 : 10;
        if (killed)
        {
            Event("round_won");
            OnOpKilled(window);
            Bot = null; Op = null; // the body plays its death and frees itself
            EndRound(1.2f);
        }
        return dist;
    }

    /// <summary>Spot ping: a marker over the Op for a moment after you spot him (like a teammate's ping).</summary>
    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (Bot == null || !GodotObject.IsInstanceValid(Bot) || Bot.Dead || Now > PingUntil) return;
        var p = Bot.Head + new Vector3(0, 0.55f, 0);
        var v = G.View;
        var f = v.Forward;
        var right = f.Cross(Vector3.Up).Normalized();
        var up = right.Cross(f);
        var d = p - v.Eye;
        float z = d.Dot(f);
        if (z < 0.1f) return;
        float focal = size.X / 2f / Mathf.Tan(Mathf.DegToRad(v.HFov / 2f));
        var s = size / 2f + new Vector2(d.Dot(right) / z * focal, -d.Dot(up) / z * focal);
        float k = size.Y / 1080f, r = 11f * k;
        float a = Mathf.Clamp((PingUntil - Now) / 0.4f, 0f, 1f);
        var col = new Color(G.Enemy, 0.9f * a);
        c.DrawColoredPolygon(new[] { s + new Vector2(0, -r), s + new Vector2(r, 0), s + new Vector2(0, r), s + new Vector2(-r, 0) }, col);
        c.DrawPolyline(new[] { s + new Vector2(0, -r), s + new Vector2(r, 0), s + new Vector2(0, r), s + new Vector2(-r, 0), s + new Vector2(0, -r) },
            new Color(1, 1, 1, 0.9f * a), 2f * k);
        var label = $"OP · {SpotName.ToUpperInvariant()} · {d.Length():0} M";
        c.DrawString(UiTheme.HudWide, s + new Vector2(-Gfx.TextW(UiTheme.HudWide, label, UiTheme.Fs(14, k)) / 2f, -r - 8 * k), label,
            HorizontalAlignment.Left, -1, UiTheme.Fs(14, k), new Color(1, 1, 1, a));
    }

    // ---------------- dev: scripted player (--dev --movescript auto) ----------------

    readonly HashSet<Key> autoKeys = new();
    protected float AutoT;
    protected int AutoPhase;

    /// <summary>Drives <see cref="autoKeys"/> each frame (only with --movescript auto). The simulated aim (--simaim) shoots.</summary>
    protected virtual void Auto(float dt, HashSet<Key> keys) { }

    void AutoPilot(float dt)
    {
        if (!(Main.I.Dev && MovementScript.Auto)) return;
        AutoT += dt;
        Auto(dt, autoKeys);
    }

    /// <summary>Feet X of the player (the peek axis: more = further out past the corner).</summary>
    protected float PlayerX => G.View.Eye.X;

    bool reported;

    /// <summary>Dev runs with --movelog / --movescript print the results once (the session asks for the badge when the run ends).</summary>
    protected void DevReport()
    {
        if (reported || !MovementScript.Log) return;
        reported = true;
        GD.Print($"[drill] {Key} tier {Tier} ({Difficulty.Get(Tier).Name}), {Rounds} rounds, score {Score}: " +
                 string.Join(" | ", ResultLines().Select(l => $"{l.Label}: {l.Value}")));
    }
}
