using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// How a rifler holding an open gap plays, per rank tier (Rookie = Iron–Bronze … Pro = Radiant). Reaction and aim-settle
/// come from the tier's <see cref="BotSkill"/>; these are the parts that decide whether he hits a player crossing.
/// </summary>
/// <param name="AimSd">Aim error (deg, per axis) on a target standing still.</param>
/// <param name="TrackK">Extra aim error (deg) per deg/s the target moves across his view: a full-speed crossing at 15 m
/// is ≈20°/s. Fast targets are hard to hit; slow or stopped ones are easy.</param>
/// <param name="AirMul">Vertical aim error × this on an airborne target (his crosshair waits at head height).</param>
/// <param name="Predict">How much of the target's motion he leads (0 = shoots where he last saw you).</param>
/// <param name="Lag">Seconds between what he sees and where his click lands (perception + motor delay).</param>
/// <param name="Interval">Seconds between his taps while he can see you.</param>
/// <param name="Commit">He still fires if you vanished at most this long before the shot.</param>
public sealed record GapSkill(float AimSd, float TrackK, float AirMul, float Predict, float Lag, float Interval, float Commit)
{
    public static GapSkill For(int tier) => Math.Clamp(tier, 0, 4) switch
    {
        0 => new(0.90f, 0.060f, 1.50f, 0.00f, 0.13f, 0.30f, 0.25f),
        1 => new(0.70f, 0.050f, 1.45f, 0.30f, 0.12f, 0.24f, 0.20f),
        2 => new(0.50f, 0.042f, 1.40f, 0.55f, 0.10f, 0.19f, 0.15f),
        3 => new(0.36f, 0.036f, 1.30f, 0.75f, 0.08f, 0.15f, 0.12f),
        _ => new(0.26f, 0.032f, 1.20f, 0.90f, 0.06f, 0.13f, 0.10f),
    };
}

/// <summary>
/// A defender holding an open gap with a Vandal (no moving):
/// <list type="number">
/// <item>Sees you when a spotting point (head, chest, a shoulder) is inside his view (±40° of where he holds) with line of
/// sight.</item>
/// <item>Reacts (tier reaction time, 0.7× when alert from a recent sighting), then settles onto you (Fitts' law from the
/// edge he holds).</item>
/// <item>Taps at the position he perceived <see cref="GapSkill.Lag"/> ago plus his lead, with Gaussian aim error that grows
/// with how fast you move across his view and on an airborne target. Every bullet is a real ray against your body at
/// that instant: if you're already behind the wall, it hits the wall.</item>
/// <item>Vandal damage: head 160 (kills through 100 HP + 50 shields), body 40, legs 34.</item>
/// </list>
/// </summary>
public sealed class GapHolderBrain
{
    public enum St { Holding, Engaging }

    public readonly BotCharacter Body;
    public readonly GapSkill Skill;
    readonly BotSkill human;
    readonly Random rng;
    readonly WeaponDef gun = Weapons.Vandal;
    public const float HalfFov = 40f;

    /// <summary>Where his crosshair rests (world point: the edge of the gap you'll appear from, at head height).</summary>
    public Vector3 HeldPoint;
    public St State { get; private set; } = St.Holding;
    /// <summary>He can see some part of you right now.</summary>
    public bool SeesPlayer { get; private set; }
    public int ShotsFired { get; private set; }
    /// <summary>Raised after every shot: hit, zone, damage.</summary>
    public event Action<bool, HitZone, float>? Fired;

    float fireAt, lastSeenAt = -99f, alertUntil = -99f;
    int burst;
    PlayerHitbox.Part target;

    readonly record struct Sample(float T, Vector3 Eye, float Yaw, bool Air);
    readonly List<Sample> hist = new(128);

    public GapHolderBrain(BotCharacter body, int tier, Random rng)
    {
        Body = body;
        Skill = GapSkill.For(tier);
        human = Difficulty.Get(tier).Bot;
        this.rng = rng;
    }

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    Vector3 Eye => Body.Head;

    /// <summary>Spotting points he can see (bit per <see cref="PlayerHitbox.Part"/>) for a body at this eye and yaw.</summary>
    public int Visible(IGame g, Vector3 eye, float yaw)
    {
        var look = (HeldPoint - Eye).Normalized();
        int mask = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = PlayerHitbox.PartAt((PlayerHitbox.Part)i, eye, yaw);
            var to = (p - Eye).Normalized();
            if (Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(look.Dot(to), -1, 1))) > HalfFov) continue;
            if (g.LineOfSight(Eye, p)) mask |= 1 << i;
        }
        return mask;
    }

    static bool Has(int mask, PlayerHitbox.Part p) => (mask & (1 << (int)p)) != 0;

    static PlayerHitbox.Part Pick(int vis) =>
        Has(vis, PlayerHitbox.Part.Head) ? PlayerHitbox.Part.Head :
        Has(vis, PlayerHitbox.Part.Chest) ? PlayerHitbox.Part.Chest :
        Has(vis, PlayerHitbox.Part.ShoulderR) ? PlayerHitbox.Part.ShoulderR : PlayerHitbox.Part.ShoulderL;

    public void Update(IGame g, float dt)
    {
        float now = g.Now;
        hist.Add(new Sample(now, g.View.Eye, g.View.Yaw, !g.PlayerGrounded));
        while (hist.Count > 2 && now - hist[0].T > 0.6f) hist.RemoveAt(0);
        if (Body.Dead || g.Player.Dead) { SeesPlayer = false; State = St.Holding; return; }

        int vis = Visible(g, g.View.Eye, g.View.Yaw);
        SeesPlayer = vis != 0;
        switch (State)
        {
            case St.Holding:
                // Fair: he only reacts once you could see some of him too (BotBrain.PlayerCanSee).
                if (!SeesPlayer || !BotBrain.PlayerCanSee(g, Body)) break;
                target = Pick(vis);
                bool alert = now < alertUntil;
                // The tier's human floor (Pro 150 ms) holds for re-peeks too: the old 120 ms x 0.7 reached 84 ms.
                float react = Mathf.Max(human.ReactFloorMs, Gauss(human.ReactMs, human.ReactSd) * (alert ? 0.7f : 1f)) / 1000f;
                var p = PlayerHitbox.PartAt(target, g.View.Eye, g.View.Yaw);
                float dist = Mathf.Max(1f, (p - Eye).Length());
                float amp = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((HeldPoint - Eye).Normalized().Dot((p - Eye).Normalized()), -1, 1)));
                float w = Mathf.RadToDeg(2f * Mathf.Atan(0.2f / dist));
                float settle = amp <= w / 2 ? 0 : human.MsPerBit * Mathf.Log(amp / w + 1f) / Mathf.Log(2f) / 1000f;
                fireAt = now + react + settle;
                lastSeenAt = now;
                burst = 0;
                State = St.Engaging;
                break;
            case St.Engaging:
                if (SeesPlayer) { lastSeenAt = now; target = Pick(vis); }
                if (now >= fireAt)
                {
                    if (now - lastSeenAt <= Skill.Commit) { Fire(g); fireAt = Mathf.Max(fireAt + Skill.Interval, now); }
                    else { State = St.Holding; alertUntil = now + 2.5f; }
                }
                else if (!SeesPlayer && now - lastSeenAt > Skill.Commit + 0.3f) { State = St.Holding; alertUntil = now + 2.5f; }
                break;
        }

        var aim = State == St.Engaging && now - lastSeenAt < 0.3f ? g.View.Eye : HeldPoint;
        var dir = aim - Eye;
        Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        Body.AimTarget = aim;
    }

    Sample At(float t)
    {
        for (int i = hist.Count - 1; i >= 0; i--) if (hist[i].T <= t) return hist[i];
        return hist[0];
    }

    void Fire(IGame g)
    {
        float now = g.Now, lag = Skill.Lag;
        var s = At(now - lag);
        var s0 = At(now - lag - 0.05f);
        var p = PlayerHitbox.PartAt(target, s.Eye, s.Yaw);
        var p0 = PlayerHitbox.PartAt(target, s0.Eye, s0.Yaw);
        var vel = s.T - s0.T > 0.005f ? (p - p0) / (s.T - s0.T) : Vector3.Zero;
        var aim = p + vel * (lag * Skill.Predict);
        var dir = (aim - Eye).Normalized();
        float dist = Mathf.Max(1f, (aim - Eye).Length());
        // How fast you cross his view (deg/s): the motion across his line of sight.
        var across = vel - dir * vel.Dot(dir);
        float angSpeed = Mathf.RadToDeg(across.Length() / dist);
        float sd = (Skill.AimSd + Skill.TrackK * angSpeed) * (1f + 0.12f * Math.Min(burst, 4)); // taps drift a little
        var right = dir.Cross(Vector3.Up).Normalized();
        var up = right.Cross(dir);
        float sdY = s.Air ? sd * Skill.AirMul : sd;
        dir = (dir + right * Mathf.Tan(Mathf.DegToRad(Gauss(0, sd))) + up * Mathf.Tan(Mathf.DegToRad(Gauss(0, sdY)))).Normalized();

        float wall = Collision.FirstHit(Eye, dir, g.Solid);
        var zone = PlayerHitbox.Ray(g.View.Eye, g.Mover.EyeHeight, g.View.Yaw, Eye, dir, out float bodyDist);
        bool hit = zone != HitZone.None && bodyDist < wall;
        float dmg = hit ? gun.Damage((int)zone, bodyDist) : 0f;
        var end = Eye + dir * Mathf.Min(hit ? bodyDist : wall, 80f);
        ShotsFired++;
        burst++;
        Body.ShootFx(end);
        g.SoundAt(BotBrain.ShotSoundFor(gun), Eye, maxRange: 60f, floor: 0.6f);
        Fired?.Invoke(hit, zone, dmg);
        if (hit) g.DamagePlayer(dmg, Eye);
    }
}

/// <summary>
/// Strafe Jump (suggested on Discord): cross an open gap that a rifler holds, from cover to cover, and keep your speed.
/// VALORANT has no CS-style bunny-hop gain (air strafing turns your speed but doesn't add to it, see
/// docs/research/strafe-jump-1.23.md), so this teaches what does work: build full speed in cover, cross the gap at full
/// speed (a jump at the edge moves your head off his crosshair), hold the key through the landing so the ground doesn't
/// brake you, never walk or stop in the open. Each crossing is graded on time seen, speed kept and the landing. The
/// defender's reaction and aim follow the tier, so a slow crossing that an Iron misses gets you killed at Radiant.
/// </summary>
public sealed class StrafeJumpMode : MovementMode
{
    public override string Key => "strafejump";
    public override string Name => L.N("Strafe Jump");
    public override string Description => L.N("Cross an open gap a rifler is holding: full speed from cover, jump at the edge if you like, keep holding the key through the landing. Slow or stopped in the open, you die.");
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.Any;
    public override float Duration => 90f;
    public override bool Minimap => true;
    public override float MinimapRange => 30f;
    public override Vector3 StartFeet => new(RestA, 0, PathZ);
    public override float StartYaw => 0f;

    /// <summary>Width of the open gap (x from 0 to Gap).</summary>
    const float Gap = 3.4f, PathZ = 0.25f, WallH = 4.5f;
    /// <summary>Where you wait in cover on each side (2.8 m from the edge: room to reach full speed, 0.3 s).</summary>
    const float RestA = -2.8f, RestB = Gap + 2.8f;

    public override IEnumerable<Box> ExtraSolids => new[]
    {
        new Box(new Vector3(-10.5f, 0, -1.6f), new Vector3(0, WallH, -1.1f)),             // cover A (north wall, west of the gap)
        new Box(new Vector3(Gap, 0, -1.6f), new Vector3(Gap + 10.5f, WallH, -1.1f)),      // cover B (north wall, east of the gap)
        new Box(new Vector3(-10.5f, 0, 1.6f), new Vector3(Gap + 10.5f, WallH, 2.1f)),     // corridor, south wall
        new Box(new Vector3(-11f, 0, -1.6f), new Vector3(-10.5f, WallH, 2.1f)),           // corridor, west end
        new Box(new Vector3(Gap + 10.5f, 0, -1.6f), new Vector3(Gap + 11f, WallH, 2.1f)), // corridor, east end
        new Box(new Vector3(-13f, 0, -32.5f), new Vector3(-12.5f, WallH, -1.6f)),         // yard, west
        new Box(new Vector3(Gap + 12.5f, 0, -32.5f), new Vector3(Gap + 13f, WallH, -1.6f)), // yard, east
        new Box(new Vector3(-13f, 0, -33f), new Vector3(Gap + 13f, 6f, -32.5f)),          // yard, far end
        new Box(new Vector3(-12.4f, 0, -9f), new Vector3(-10.6f, 1.4f, -7.2f)),           // crates along the yard walls
        new Box(new Vector3(Gap + 10.4f, 0, -16f), new Vector3(Gap + 12.4f, 2.2f, -14f)),
        new Box(new Vector3(-12.4f, 0, -24f), new Vector3(-10.4f, 2.2f, -22f)),
        new Box(new Vector3(Gap + 10.8f, 0, -29f), new Vector3(Gap + 12.4f, 1.2f, -27.4f)),
    };

    /// <summary>How a crossing ended: clean, made it with a fault, died. (The "cross" event's B.)</summary>
    public enum Grade { Clean, Fault, Died }
    /// <summary>The main fault of a crossing (the "cross_fault" event's A).</summary>
    public enum Fault { None, Walked, Crouched, SlowEntry, SlowedInOpen, LandingBrake }

    enum Phase { Between, Ready, Crossing }
    Phase phase = Phase.Between;
    float between = 0.5f;
    /// <summary>+1: in cover A, crossing east to B; -1: in cover B, crossing west.</summary>
    int dir = 1;
    BotCharacter? bot;
    GapHolderBrain? brain;
    Random? brainRng;
    float defenderDist;

    // ---- the crossing in progress ----
    float seenT, entrySpeed, minSpeed, sumSpeed, sumT, lastJumpAt = -99f, landedAt = -1f, landSpeed, damage;
    bool walked, crouched, jumped, landingBrake;
    int shotsAtYou;

    // ---- totals ----
    int crossings, clean, survived, died, aborted, noShot, landingLosses, jumpCrossings, jumpSurvived, runCrossings, runSurvived, botKills;
    readonly int[] faults = new int[6];
    readonly List<float> seenMs = new(), speedPct = new();
    string lastLine = "";

    float X => G.View.Eye.X;
    float CrossSpeed => G.Mover.Vel.X * dir; // speed toward the other side (m/s)

    protected override void Setup()
    {
        if (Main.I.Dev && int.TryParse(CmdLine.DevAfter("--sjseed"), out int seed)) brainRng = new Random(seed);
        G.PlayerJumped += () => lastJumpAt = Now;
        G.PlayerLanded += () =>
        {
            if (phase != Phase.Crossing) return;
            landedAt = Now;
            landSpeed = Mathf.Max(0f, CrossSpeed);
        };
        if ((Main.I.Dev && MovementScript.Auto) || G.Demo) Mover.KeyOverride = k => autoKeys.Contains(k);
    }

    bool InCover(int side) => side > 0 ? X < -1.0f : X > Gap + 1.0f;

    void SpawnDefender()
    {
        var eye = G.View.Eye;
        var mid = new Vector3(Gap / 2f, PlayerView.EyeHeight, PathZ);
        Vector3 feet = new(Gap / 2f, 0, -20f);
        for (int i = 0; i < 60; i++)
        {
            var f = new Vector3(Mathf.Clamp(Gap / 2f + R(-7f, 7f), -11.5f, Gap + 11.5f), 0, -R(12f, 28f));
            var head = f + new Vector3(0, PlayerView.EyeHeight, 0);
            if (!G.LineOfSight(head, mid)) continue;
            bool hidden = Enumerable.Range(0, 4).All(p => !G.LineOfSight(head, PlayerHitbox.PartAt((PlayerHitbox.Part)p, eye, G.View.Yaw)));
            if (!hidden) continue;
            feet = f;
            break;
        }
        bot = G.SpawnBot(feet);
        brain = new GapHolderBrain(bot, Tier, brainRng ?? Rng);
        // He holds the edge of the gap you'll come out of, at head height (he heard you), with the tier's placement error.
        float edge = WindowEdge(bot.Head);
        var held = new Vector3(edge, PlayerView.EyeHeight, PathZ);
        float d = held.DistanceTo(bot.Head);
        float err = Difficulty.Get(Tier).Bot.XhairErrDeg * 0.3f;
        brain.HeldPoint = held + new Vector3(Gauss0() * err, Gauss0() * err * 0.5f, 0) * Mathf.DegToRad(1f) * d;
        brain.Fired += OnFired;
        var to = brain.HeldPoint - bot.Head;
        bot.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z));
        bot.AimTarget = brain.HeldPoint;
        float dist = new Vector2(bot.Head.X - Gap / 2f, bot.Head.Z - PathZ).Length();
        defenderDist = dist;
        phase = Phase.Ready;
        ResetCrossing();
    }

    /// <summary>The first point of your path (coming from your side) where he can see any part of you.</summary>
    float WindowEdge(Vector3 head)
    {
        float yaw = G.View.Yaw;
        float start = dir > 0 ? -3f : Gap + 3f;
        for (int i = 0; i <= 240; i++)
        {
            float x = start + dir * i * 0.05f;
            var eye = new Vector3(x, PlayerView.EyeHeight, PathZ);
            for (int p = 0; p < 4; p++)
                if (G.LineOfSight(head, PlayerHitbox.PartAt((PlayerHitbox.Part)p, eye, yaw))) return x + dir * 0.15f;
        }
        return dir > 0 ? 0f : Gap;
    }

    float Gauss0()
    {
        double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    void ResetCrossing()
    {
        seenT = sumSpeed = sumT = damage = 0;
        entrySpeed = -1f; minSpeed = 99f; landedAt = -1f;
        walked = crouched = jumped = landingBrake = false;
        shotsAtYou = 0;
    }

    void DropDefender()
    {
        if (bot != null && GodotObject.IsInstanceValid(bot) && !bot.Dead) G.Despawn(bot);
        bot = null; brain = null;
    }

    void OnFired(bool hit, HitZone zone, float dmg)
    {
        shotsAtYou++;
        if (hit) damage += dmg;
    }

    public override void Update(float dt)
    {
        if (phase == Phase.Between)
        {
            between -= dt;
            // Died: back to the cover you came from, then the next defender (a fresh one, somewhere else).
            if (G.Player.Dead && between <= 0.5f) G.Respawn(new Vector3(dir > 0 ? RestA : RestB, 0, PathZ), StartYaw);
            if (between <= 0 && !G.Player.Dead && InCover(dir)) SpawnDefender();
            AutoPilot(dt);
            return;
        }
        if (bot == null || brain == null) { phase = Phase.Between; between = 0.5f; return; }
        brain.Update(G, dt);
        if (phase == Phase.Between || bot == null) return; // died this frame

        var m = G.Mover;
        if (phase == Phase.Ready && brain.SeesPlayer)
        {
            phase = Phase.Crossing;
            entrySpeed = Mathf.Max(0f, CrossSpeed);
            jumped = Now - lastJumpAt < 0.35f || !m.Grounded;
        }
        if (phase == Phase.Crossing)
        {
            if (brain.SeesPlayer)
            {
                seenT += dt;
                float s = Mathf.Max(0f, CrossSpeed);
                sumSpeed += s * dt; sumT += dt;
                bool landingWindow = landedAt >= 0 && Now - landedAt < 0.2f;
                if (seenT > 0.08f && !landingWindow) minSpeed = Mathf.Min(minSpeed, s);
                if (m.Walking && m.Grounded) walked = true;
                if (m.Crouching) crouched = true;
                if (!m.Grounded) jumped = true;
            }
            // The landing: speed 0.15 s after touching down vs at touchdown (keys released in the air = the ground brakes you).
            if (landedAt >= 0 && Now - landedAt >= 0.15f)
            {
                if (landSpeed > 2f && CrossSpeed < landSpeed * 0.85f) landingBrake = true;
                landedAt = -1f;
            }
            if (!brain.SeesPlayer && InCover(-dir) && G.Mover.Grounded) Finish(false);
            else if (!brain.SeesPlayer && InCover(dir) && seenT > 0)
            {
                aborted++;
                G.Banner(L.N("BACKED OUT — once you show, commit: keep going"), UiTheme.Warn);
                ResetCrossing();
                phase = Phase.Ready;
            }
        }
        AutoPilot(dt);
    }

    Fault MainFault()
    {
        float run = G.Mover.RunMax;
        if (walked) return Fault.Walked;
        if (crouched) return Fault.Crouched;
        if (landingBrake) return Fault.LandingBrake;
        if (entrySpeed >= 0 && entrySpeed < run * 0.85f) return Fault.SlowEntry;
        if (minSpeed < run * 0.7f) return Fault.SlowedInOpen;
        return Fault.None;
    }

    void Finish(bool dead)
    {
        crossings++;
        var fault = MainFault();
        var grade = dead ? Grade.Died : fault == Fault.None ? Grade.Clean : Grade.Fault;
        float avg = sumT > 0 ? sumSpeed / sumT : 0f;
        float pct = 100f * avg / Mathf.Max(0.1f, G.Mover.RunMax);
        seenMs.Add(seenT * 1000f);
        speedPct.Add(pct);
        faults[(int)fault]++;
        if (landingBrake) landingLosses++;
        if (jumped) { jumpCrossings++; if (!dead) jumpSurvived++; } else { runCrossings++; if (!dead) runSurvived++; }
        if (!dead) survived++;
        if (!dead && shotsAtYou == 0) noShot++;
        Event("cross", seenT * 1000f, (int)grade);
        Event("cross_fault", (int)fault, jumped ? 1 : 0);
        string how = jumped ? L.C("crossing", "jump") : L.C("crossing", "run");
        string res = grade switch { Grade.Clean => L.C("crossing", "clean"), Grade.Fault => L.C("crossing", "made it"), _ => L.C("crossing", "died") };
        lastLine = L.F($"Last: {res} · seen {seenT * 1000:0} ms · {pct:0}% speed · {how}");

        if (!dead)
        {
            if (grade == Grade.Clean)
            {
                clean++;
                Score += 100 + (shotsAtYou == 0 ? 25 : 0);
                G.Banner(shotsAtYou == 0 ? L.F($"CLEAN CROSS · seen {seenT * 1000:0} ms · he couldn't get a shot off")
                                         : L.F($"CLEAN CROSS · seen {seenT * 1000:0} ms · {pct:0}% speed"), UiTheme.Good);
            }
            else { Score += 40; G.Banner(Tip(fault), UiTheme.Warn); }
            Score -= (int)(damage / 4f);
            G.Player.Reset(); // a fresh 150 for the next crossing
            dir = -dir;
            DropDefender();
            phase = Phase.Between;
            between = 0.7f;
            return;
        }
        Score -= 100;
        G.Banner(fault == Fault.None ? L.N("YOU DIED — he hit you anyway: cross further from his crosshair (jump at the edge)") : Tip(fault), UiTheme.Accent);
    }

    string Tip(Fault f)
    {
        var k = Main.I.Valorant;
        var key = dir > 0 ? k.KeyRight : k.KeyLeft;
        float run = G.Mover.RunMax;
        return f switch
        {
            Fault.Walked => L.F($"WALKING — {k.KeyWalk} cuts you to {run * Mover.WalkMul:0.0} m/s: run across"),
            Fault.Crouched => L.N("CROUCHING — it slows you down in the open: stand and run across"),
            Fault.LandingBrake => L.F($"KEEP HOLDING {key} THROUGH THE LANDING — no key when you land = the ground brakes you"),
            Fault.SlowEntry => L.F($"TOO SLOW OUT OF COVER — {entrySpeed:0.0} of {run:0.0} m/s: start running earlier, full speed takes 0.3 s"),
            Fault.SlowedInOpen => L.F($"YOU SLOWED DOWN IN THE OPEN — {minSpeed:0.0} m/s: keep the key down until you're in cover"),
            _ => "",
        };
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        died++;
        Event("round_lost");
        if (phase == Phase.Crossing || phase == Phase.Ready) Finish(true);
        var b = bot;
        if (b != null) b.GetTree().CreateTimer(0.8).Timeout += () => { if (GodotObject.IsInstanceValid(b)) G.Despawn(b); };
        bot = null; brain = null;
        phase = Phase.Between;
        between = 1.4f;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (bot == null || phase == Phase.Between) return float.PositiveInfinity;
        var hit = ShootBots(new[] { bot }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 20 : 5;
        if (killed)
        {
            botKills++;
            Event("round_won");
            G.Banner(L.N("KILL — fine, but the drill is the crossing: you had to stop to win that"), UiTheme.Warn);
            bot = null; brain = null;
            ResetCrossing();
            phase = Phase.Between;
            between = 0.9f;
        }
        return dist;
    }

    public override IEnumerable<string> HudLines()
    {
        yield return L.F($"Crossings {crossings} · clean {clean} · deaths {died}");
        if (bot != null && phase != Phase.Between) yield return L.F($"Defender {defenderDist:0} m away, holding the gap");
        if (lastLine != "") yield return lastLine;
        if (G.Mover is { Grounded: false }) yield return L.N("AIRBORNE — no speed gained, none lost");
    }

    public override string? Prompt
    {
        get
        {
            if (phase == Phase.Between || G.Player.Dead) return null;
            var k = Main.I.Valorant;
            var key = dir > 0 ? k.KeyRight : k.KeyLeft;
            if (phase == Phase.Ready) return L.F($"Cross to the other side: {key} from deep in cover, full speed through the gap ({k.KeyJump} at the edge if you like)");
            return null;
        }
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Clean crossings (full speed, kept it through the landing, alive)"), L.F($"{clean} of {crossings}"));
        yield return (L.N("Crossings survived / deaths"), $"{survived} / {died}");
        if (seenMs.Count > 0) yield return (L.N("Avg time seen per crossing"), L.F($"{seenMs.Average():0} ms (shorter = safer)"));
        if (speedPct.Count > 0) yield return (L.N("Avg speed in the open"), L.F($"{speedPct.Average():0}% of run speed"));
        if (jumpCrossings > 0) yield return (L.N("Jump crossings survived"), L.F($"{jumpSurvived} of {jumpCrossings}"));
        if (runCrossings > 0) yield return (L.N("Running crossings survived"), L.F($"{runSurvived} of {runCrossings}"));
        yield return (L.N("Crossings he couldn't shoot"), noShot.ToString());
        int slow = faults[(int)Fault.SlowEntry] + faults[(int)Fault.SlowedInOpen];
        if (slow > 0) yield return (L.N("Too slow out of cover / slowed in the open"), $"{faults[(int)Fault.SlowEntry]} / {faults[(int)Fault.SlowedInOpen]}");
        if (landingLosses > 0) yield return (L.N("Landings that braked you"), landingLosses.ToString());
        if (faults[(int)Fault.Walked] + faults[(int)Fault.Crouched] > 0) yield return (L.N("Walked / crouched across"), $"{faults[(int)Fault.Walked]} / {faults[(int)Fault.Crouched]}");
        if (aborted > 0) yield return (L.N("Backed out after showing"), aborted.ToString());
        if (botKills > 0) yield return (L.N("Kills"), botKills.ToString());
        foreach (var l in MovementLines()) yield return l;
    }

    public override int Badge()
    {
        DevReport();
        return crossings == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)clean / crossings);
    }

    bool reported;

    void DevReport()
    {
        if (reported || !MovementScript.Log) return;
        reported = true;
        GD.Print(FormattableString.Invariant(
            $"[strafejump] RESULT tier={Tier} style={AutoStyle} crossings={crossings} clean={clean} survived={survived} died={died} jumps={jumpCrossings} jumpSurvived={jumpSurvived} runs={runCrossings} runSurvived={runSurvived} landBrakes={landingLosses} avgSeenMs={(seenMs.Count > 0 ? seenMs.Average() : 0):0} avgSpeedPct={(speedPct.Count > 0 ? speedPct.Average() : 0):0}"));
    }

    // ---------------- dev / preview: scripted player (--dev --movescript auto [--sjstyle fast|run|jump|slow]) ----------------

    readonly HashSet<InputBinding> autoKeys = new();
    float autoT;
    int autoPhase, autoCount;
    bool autoJumped;
    float autoReleasedFor;
    /// <summary>fast (default, the preview): full speed from deep in cover, every other crossing a jump at the edge, key held
    /// through the landing. run / jump: only that. slow: walks across (shift); release: jumps at the edge but lets go of the
    /// key in the air (braked on landing). The last two are what the drill punishes.</summary>
    static string AutoStyle => CmdLine.DevAfter("--sjstyle") is { Length: > 0 } s ? s.ToLowerInvariant() : "fast";

    void AutoPilot(float dt)
    {
        if (!((Main.I.Dev && MovementScript.Auto) || G.Demo)) return;
        autoT += dt;
        var k = Main.I.Valorant;
        autoKeys.Clear();
        if (G.Player.Dead) return;
        float rest = dir > 0 ? RestA : RestB;
        var toward = dir > 0 ? k.KeyRight : k.KeyLeft;
        var away = dir > 0 ? k.KeyLeft : k.KeyRight;
        string style = G.Demo ? "fast" : AutoStyle;
        switch (autoPhase)
        {
            case 0: // back to the rest spot, deep in cover, and wait for him
                if ((X - rest) * dir > 0.15f) autoKeys.Add(away);
                else if (phase == Phase.Ready && autoT > 0.6f && G.Mover.Speed < 0.5f) { autoPhase = 1; autoT = 0; autoJumped = false; autoCount++; }
                break;
            case 1: // cross
                autoKeys.Add(toward);
                bool jump = style is "jump" or "release" || (style == "fast" && autoCount % 2 == 0);
                if (style == "slow") autoKeys.Add(k.KeyWalk);
                float edge = dir > 0 ? -0.25f : Gap + 0.25f;
                if (jump && !autoJumped && (X - edge) * dir > 0 && G.PlayerGrounded) { autoKeys.Add(k.KeyJump); autoJumped = true; }
                // release: lets go of the key in the air (no cost until the landing, then the ground brakes you)
                if (style == "release" && autoJumped && (!G.PlayerGrounded || autoReleasedFor < 0.35f))
                {
                    autoKeys.Remove(toward);
                    if (G.PlayerGrounded) autoReleasedFor += dt;
                }
                else if (style == "release" && !autoJumped) autoReleasedFor = 0;
                if (phase != Phase.Crossing && phase != Phase.Ready || autoT > 6f) { autoPhase = 2; autoT = 0; }
                break;
            case 2: // stop in cover (counter-strafe: dir already points back at the gap), then wait for the next one
                if (CrossSpeed < -0.6f) autoKeys.Add(toward);
                if (autoT > 0.4f) { autoPhase = 0; autoT = 0; }
                break;
        }
    }
}
