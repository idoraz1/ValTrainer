using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

/// <summary>Shared bullet → bot logic: real weapon damage (with falloff), hit feedback, kill bookkeeping.</summary>
public abstract class BotMode : TrainingMode
{
    public override bool UsesHeadshots => true;
    public override string Category => "Valorant";
    public override bool InfiniteAmmo => false;   // real magazines + reloads
    public override bool InfiniteReserve => true;  // …that never run dry during a drill

    /// <summary>Hits the closest live bot along the ray (walls checked by the caller via <paramref name="maxDist"/>).</summary>
    protected BotCharacter? ShootBots(IEnumerable<BotCharacter> bots, Vector3 o, Vector3 d, out HitZone zone, out bool killed, out float dist, float maxDist = float.PositiveInfinity)
    {
        zone = HitZone.None; killed = false; dist = float.PositiveInfinity;
        maxDist = Mathf.Min(maxDist, G.BulletWallDist); // walls stop bullets
        BotCharacter? best = null;
        foreach (var b in bots)
        {
            if (!GodotObject.IsInstanceValid(b) || b.Dead) continue;
            var z = b.Raycast(o, d, out var t);
            if (z != HitZone.None && t < dist && t < maxDist) { dist = t; best = b; zone = z; }
        }
        if (best == null) return null;
        Hits++;
        if (zone == HitZone.Head) HeadHits++;
        LastShotZone = zone;
        var w = G.Weapon ?? Weapons.Vandal;
        best.Hp -= (int)Mathf.Round(w.Damage((int)zone, dist));
        best.OnHit(zone);
        Game.Fx.Effects.BodyHit(o + d * dist, G.Enemy, zone == HitZone.Head);
        G.Sound(zone == HitZone.Head ? "head" : "body", zone == HitZone.Head ? 1f : 0.8f);
        if (best.Dead)
        {
            killed = true;
            best.Die(d);
            RegisterKill(best.SpawnTime, zone == HitZone.Head);
        }
        return best;
    }

    readonly HashSet<int> seenBots = new();

    /// <summary>Fires a bot_seen event the first time the player has line of sight to this bot's head.</summary>
    protected void TrackSeen(BotCharacter? b)
    {
        if (b == null || !GodotObject.IsInstanceValid(b) || b.Dead) return;
        int id = IdOf(b);
        if (seenBots.Contains(id) || !G.LineOfSight(G.View.Eye, b.Head)) return;
        seenBots.Add(id);
        SeenEvent(b.Head);
    }

    /// <summary>Focus helper: the live bot whose head is closest to the crosshair (optionally only visible ones).</summary>
    protected AimFocus? NearestHead(IEnumerable<BotCharacter> bots, bool visibleOnly = true)
    {
        AimFocus? best = null;
        float bestA = float.MaxValue;
        foreach (var b in bots)
        {
            if (b == null || !GodotObject.IsInstanceValid(b) || b.Dead) continue;
            if (visibleOnly && !G.LineOfSight(G.View.Eye, b.Head)) continue;
            float a = AngleFromCrosshair(b.Head);
            if (a < bestA) { bestA = a; best = new AimFocus(IdOf(b), b.Head, BotCharacter.HeadRadius); }
        }
        return best;
    }

    /// <summary>Distance to the first wall along the ray (bullets don't go through cover).</summary>
    protected float Wall(Vector3 o, Vector3 d) => Collision.FirstHit(o, d, G.Solid);
}

/// <summary>Range-style strafing bots; they leave if you take too long (tier lifetime).</summary>
public sealed class StrafeBotsMode : BotMode
{
    public override AimFocus? Focus => bot != null && !bot.Dead && GodotObject.IsInstanceValid(bot) ? new AimFocus(IdOf(bot), bot.Head, BotCharacter.HeadRadius) : null;

    public override string Key => "strafebots";
    public override string Name => "Strafe Bots";
    public override string Description => "A-D strafing bots. Vandal damage: 1 head or 4 body. Kill before they leave.";

    BotCharacter? bot;
    float targetVx, turnIn, respawnIn;
    int spawned, escaped;

    protected override void Setup() => Spawn();

    void Spawn()
    {
        var (a, b) = Difficulty.StrafeBotDistance(Tier);
        bot = G.SpawnBot(new Vector3(R(-8, 8), 0, -R(a, b)));
        bot.FacingYaw = 180f;
        spawned++;
        Event("target_spawn", IdOf(bot), AngleFromCrosshair(bot.Head));
        turnIn = 0;
    }

    public override void Update(float dt)
    {
        if (bot == null || bot.Dead)
        {
            respawnIn -= dt;
            if (respawnIn <= 0) Spawn();
            return;
        }
        if (Now - bot.SpawnTime > Difficulty.StrafeBotLifetime(Tier))
        {
            escaped++; Score -= 50; G.Sound("fail", 0.6f);
            Event("target_expired", IdOf(bot));
            G.Despawn(bot); bot = null; respawnIn = 0.3f;
            return;
        }
        turnIn -= dt;
        if (turnIn <= 0)
        {
            var (a, b) = Difficulty.TurnInterval(Tier);
            bool stop = Rng.NextDouble() < Math.Max(0.15, Difficulty.StopChance(Tier));
            targetVx = stop ? 0 : (Rng.Next(2) == 0 ? -1 : 1) * Difficulty.StrafeBotSpeed(Tier);
            turnIn = R(a, b);
        }
        var v = bot.Velocity;
        v.X = Mathf.MoveToward(v.X, targetVx, Difficulty.StrafeDecel * dt);
        var p = bot.Feet + v * dt;
        if (Mathf.Abs(p.X) > 10) { p.X = Mathf.Sign(p.X) * 10; v.X = 0; targetVx = -targetVx; }
        bot.Velocity = v;
        bot.Feet = p;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        if (bot == null) return float.PositiveInfinity;
        var hit = ShootBots(new[] { bot }, o, d, out var zone, out bool killed, out float dist);
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed) { Score += 100 + (int)Mathf.Max(0, (1500 - KillTimes[^1]) / 10); respawnIn = 0.4f; }
        return dist;
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in base.ResultLines()) yield return l;
        yield return ("Bots that got away", escaped.ToString());
    }

    public override int Badge()
    {
        int n = spawned - (bot != null && !bot.Dead ? 1 : 0);
        return Difficulty.BadgeTtk(Tier, n <= 0 ? 0 : (float)Kills / n, Median(KillTimes), Difficulty.StrafeBotTtk);
    }
}

/// <summary>Pre-aim drill: bots swing out from cover and hold the angle (they don't shoot back here).</summary>
public sealed class PeekMode : BotMode
{
    public override AimFocus? Focus => bot != null && st != St.Between && !bot.Dead && GodotObject.IsInstanceValid(bot) ? new AimFocus(IdOf(bot), bot.Head, BotCharacter.HeadRadius) : null;

    public override string Key => "peek";
    public override string Name => "Peek Practice";
    public override string Description => "Bots wide-swing (and jiggle at high tiers) from cover. Pre-aim at head level; kill them before they leave.";

    static readonly Vector3[] Pillars =
    {
        new(-7, 0, -11), new(5, 0, -14), new(-2, 0, -19), new(9, 0, -23),
        new(-11, 0, -26), new(2, 0, -30), new(-5, 0, -35), new(12, 0, -16),
    };
    static readonly Vector3 PillarSize = new(1.6f, 3.2f, 1.6f);
    static Box PillarBox(Vector3 c) => new(c - new Vector3(PillarSize.X / 2, 0, PillarSize.Z / 2), c + new Vector3(PillarSize.X / 2, PillarSize.Y, PillarSize.Z / 2));
    public override IEnumerable<Box> ExtraSolids => Pillars.Select(PillarBox);

    enum St { Between, Out, Hold, Back }
    St st = St.Between;
    BotCharacter? bot;
    Vector3 hidePos, peekDir;
    float peekDist, stateT, waitFor = 1f, holdFor;
    bool seen, jiggle;
    int rounds, escaped;
    readonly List<float> placementErr = new();

    void StartPeek()
    {
        var c = Pillars[Rng.Next(Pillars.Length)];
        var los = new Vector3(c.X, 0, c.Z).Normalized();
        hidePos = c + los * 1.4f;
        float side = Rng.Next(2) == 0 ? -1f : 1f;
        peekDir = new Vector3(-los.Z, 0, los.X).Normalized() * side;
        jiggle = Rng.NextDouble() < Difficulty.JiggleChance(Tier);
        peekDist = jiggle ? 1.1f : 1.05f + Difficulty.PeekWidth(Tier) * R(0.5f, 1f);
        var (a, b) = Difficulty.PeekHold(Tier);
        holdFor = jiggle ? R(0.12f, 0.25f) : R(a, b);
        bot?.QueueFree();
        bot = G.SpawnBot(hidePos);
        bot.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(-hidePos.X, hidePos.Z));
        st = St.Out; stateT = 0; seen = false;
        rounds++;
    }

    public override void Update(float dt)
    {
        stateT += dt;
        float speed = 5.4f, travel = peekDist / speed;
        switch (st)
        {
            case St.Between:
                if (stateT >= waitFor) StartPeek();
                break;
            case St.Out:
                Move(hidePos + peekDir * Mathf.Min(peekDist, stateT * speed), dt);
                if (stateT >= travel) { st = St.Hold; stateT = 0; }
                break;
            case St.Hold:
                bot!.Velocity = Vector3.Zero;
                if (stateT >= holdFor) { st = St.Back; stateT = 0; }
                break;
            case St.Back:
                Move(hidePos + peekDir * Mathf.Max(0, peekDist - stateT * speed), dt);
                if (stateT >= travel)
                {
                    if (jiggle && Rng.NextDouble() < 0.6) { jiggle = false; st = St.Out; stateT = 0; peekDist = 1.05f + Difficulty.PeekWidth(Tier) * R(0.5f, 1f); var (a, b) = Difficulty.PeekHold(Tier); holdFor = R(a, b); break; }
                    if (seen) { escaped++; Score -= 50; G.Sound("fail", 0.6f); Event("target_expired", IdOf(bot!)); }
                    EndRound();
                }
                break;
        }
        if (bot != null && st != St.Between && !seen && G.LineOfSight(G.View.Eye, bot.Head))
        {
            seen = true;
            bot.SpawnTime = Now;
            placementErr.Add(G.View.AngleTo(bot.Head));
            SeenEvent(bot.Head);
        }
    }

    void Move(Vector3 to, float dt)
    {
        bot!.Velocity = dt > 0 ? (to - bot.Feet) / dt : Vector3.Zero;
        bot.Feet = to;
    }

    void EndRound()
    {
        st = St.Between; stateT = 0; waitFor = R(0.5f, 1.6f);
        if (bot != null) { G.Despawn(bot); bot = null; }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        if (bot == null || st == St.Between) return float.PositiveInfinity;
        var hit = ShootBots(new[] { bot }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100 + (int)Mathf.Max(0, (900 - KillTimes[^1]) / 5);
            bot = null; // the body plays its death and frees itself
            st = St.Between; stateT = 0; waitFor = R(0.6f, 1.6f);
        }
        return dist;
    }

    public override IEnumerable<string> HudLines()
    {
        if (placementErr.Count > 0) yield return $"Last pre-aim error {placementErr[^1]:0.0}°";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in base.ResultLines()) yield return l;
        yield return ("Escaped", escaped.ToString());
        if (placementErr.Count > 0) yield return ("Avg crosshair placement error", $"{placementErr.Average():0.0}° (lower = better)");
    }

    public override int Badge()
    {
        int n = rounds - (st != St.Between ? 1 : 0);
        return Difficulty.BadgeTtk(Tier, n <= 0 ? 0 : (float)Kills / n, Median(KillTimes), Difficulty.PeekTtk);
    }
}

/// <summary>Operator: bots cross a gap between cover. Exposure time = gap / speed (tier-calibrated).</summary>
public sealed class OperatorMode : BotMode
{
    public override AimFocus? Focus
    {
        get
        {
            AimFocus? best = null; float bestA = float.MaxValue;
            foreach (var b in bots)
            {
                if (b.Dead || !GodotObject.IsInstanceValid(b)) continue;
                var chest = b.Feet + new Vector3(0, 1.25f, 0);
                if (!G.LineOfSight(G.View.Eye, chest)) continue;
                float a = AngleFromCrosshair(chest);
                if (a < bestA) { bestA = a; best = new AimFocus(IdOf(b), chest, 0.27f); }
            }
            return best;
        }
    }

    public override string Key => "operator";
    public override string Name => "Operator";
    public override string Description => "Bots cross a gap between cover at 22–45 m. Scope (2.5x), one-shot body, bolt action.";
    public override WeaponKind Weapon => WeaponKind.Operator;

    static readonly float[] LaneZ = { -24f, -34f, -44f };
    float Gap => Difficulty.OperatorGap(Tier);

    /// <summary>A wall in front of each lane with a gap in the middle the bots run across.</summary>
    public override IEnumerable<Box> ExtraSolids
    {
        get
        {
            float g = Gap / 2f;
            foreach (var z in LaneZ)
            {
                yield return new Box(new Vector3(-16, 0, z + 1.2f), new Vector3(-g, 2.6f, z + 1.6f));
                yield return new Box(new Vector3(g, 0, z + 1.2f), new Vector3(16, 2.6f, z + 1.6f));
            }
        }
    }

    readonly List<BotCharacter> bots = new();
    float spawnIn = 0.8f;
    int spawned, escaped;

    public override void Update(float dt)
    {
        spawnIn -= dt;
        if (spawnIn <= 0 && bots.Count < Difficulty.OperatorBots(Tier))
        {
            float side = Rng.Next(2) == 0 ? -1 : 1;
            float z = LaneZ[Rng.Next(LaneZ.Length)];
            var b = G.SpawnBot(new Vector3(9 * side, 0, z));
            b.Velocity = new Vector3(-side * Difficulty.OperatorSpeed(Tier) * (Rng.NextDouble() < 0.25 ? 0.55f : 1f), 0, 0);
            b.FacingYaw = side > 0 ? -90f : 90f;
            bots.Add(b);
            spawned++;
            spawnIn = R(0.9f, 2.0f);
        }
        for (int i = bots.Count - 1; i >= 0; i--)
        {
            var b = bots[i];
            if (b.Dead) { bots.RemoveAt(i); continue; } // death anim then self-free
            b.Feet += b.Velocity * dt;
            if (Mathf.Abs(b.Feet.X) > 9.5f) { escaped++; Score -= 40; G.Despawn(b); bots.RemoveAt(i); }
        }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        var hit = ShootBots(bots, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        if (killed) Score += 100 + (zone == HitZone.Head ? 50 : 0) + (int)Mathf.Max(0, (2500 - KillTimes[^1]) / 20);
        return dist;
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in base.ResultLines()) yield return l;
        yield return ("Escaped", escaped.ToString());
    }

    public override int Badge() => Difficulty.BadgeWinRate(Tier, spawned == 0 ? 0 : (float)Kills / Math.Max(1, Kills + escaped));
}
