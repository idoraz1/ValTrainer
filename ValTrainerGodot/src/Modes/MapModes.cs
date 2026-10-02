using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>Drills on a Valorant map blockout with full movement; bots fight with the tier's duel model.</summary>
public abstract class MapMode : MovementMode
{
    public override string Category => "Map";
    public override float Duration => 120f;

    protected MapSpot MapSpot = MapSpots.Ascent;
    protected Vector3 SpawnFeet;
    protected float SpawnYaw;

    public override MapSpot? Map => MapSpot;
    public override Vector3 StartFeet => SpawnFeet;
    public override float StartYaw => SpawnYaw;
    public override string? Subtitle => $"{MapSpot.Map} — {MapSpot.Name}";

    /// <summary>Choose the map before the session builds the world (called from the constructor path).</summary>
    protected MapMode()
    {
        var key = Main.I?.MapKey ?? "random";
        MapSpot = key == "random" ? MapSpots.All[Random.Shared.Next(MapSpots.All.Length)] : MapSpots.ByKey(key);
        SpawnFeet = MapSpot.StartFeet;
        SpawnYaw = MapSpot.StartYaw;
    }

    protected static Vector3 EyeOf(EnemySpot e) => e.Feet + new Vector3(0, PlayerView.EyeHeight - (e.Stance == Stance.Crouch ? 0.5f : 0f), 0);

    protected (BotCharacter Body, BotBrain Brain) SpawnDefender(EnemySpot spot, Vector3 lookAt)
    {
        var b = G.SpawnBot(spot.Feet, spot.Stance == Stance.Crouch);
        var held = (lookAt - b.Head).Normalized();
        b.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(held.X, -held.Z));
        return (b, new BotBrain(b, Difficulty.Get(Tier).Bot, Rng) { HeldDir = held, ShoulderSight = Difficulty.ShoulderSight(Tier) });
    }
}

/// <summary>Attack a site: defenders hold real angles and shoot back. HP carries through the round.</summary>
public sealed class SiteClearMode : MapMode
{
    public override AimFocus? Focus => between > 0 ? null : NearestHead(defenders.Select(d => d.Body));

    public override string Key => "siteclear";
    public override string Name => "Site Clear";
    public override string Description => "Attack through a real choke (Ascent, Bind, Haven, Split). Defenders hold real angles and duel you.";

    readonly List<(BotCharacter Body, BotBrain Brain)> defenders = new();
    float roundStart, between;
    int won, lost;
    readonly List<float> clearTimes = new();

    protected override void Setup() => NewRound();

    void NewRound()
    {
        foreach (var d in defenders) G.Despawn(d.Body);
        defenders.Clear();
        int n = Tier switch { 0 => 2, 1 => Rng.NextDouble() < 0.3 ? 3 : 2, 2 => 3, 3 => 3, _ => Rng.NextDouble() < 0.5 ? 4 : 3 };
        // Nobody may see ANY part of you at spawn (the bot brain checks head, chest and shoulders).
        var startEye = MapSpot.StartFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        var exposed = new[]
        {
            startEye, startEye + Vector3.Down * 0.45f,
            startEye + new Vector3(0.35f, -0.3f, 0), startEye + new Vector3(-0.35f, -0.3f, 0),
            startEye + new Vector3(0, -0.3f, 0.35f), startEye + new Vector3(0, -0.3f, -0.35f),
        };
        var pool = MapSpot.Enemies.Where(e => exposed.All(p => !MapSpot.LineOfSight(EyeOf(e), p))).OrderBy(_ => Rng.Next()).Take(n);
        foreach (var spot in pool)
        {
            // Higher tiers sometimes hold off-angles (looking slightly away from the choke).
            var look = MapSpot.Choke + new Vector3(0, 1.5f, 0);
            if (Tier >= 3 && Rng.NextDouble() < 0.3) look += new Vector3(R(-4, 4), 0, R(-2, 2));
            defenders.Add(SpawnDefender(spot, look));
        }
        roundStart = Now;
        G.Respawn(MapSpot.StartFeet, MapSpot.StartYaw);
        G.Mover.ResetStops();
    }

    public override void Update(float dt)
    {
        if (between > 0)
        {
            between -= dt;
            if (between <= 0) NewRound();
            return;
        }
        foreach (var d in defenders) { d.Brain.Update(G, dt); TrackSeen(d.Body); }
        if (between > 0) return; // died this frame
        if (defenders.Count > 0 && defenders.All(d => d.Body.Dead))
        {
            float t = Now - roundStart;
            clearTimes.Add(t);
            won++;
            Event("round_won", t);
            Score += 200 + (int)Mathf.Max(0, (15 - t) * 20);
            G.Banner($"SITE CLEAR  {t:0.0}s", UiTheme.Good);
            between = 1.5f;
        }
        else if (Now - roundStart > Difficulty.SiteRoundTime(Tier))
        {
            lost++;
            Event("round_lost");
            G.Banner("TIME", UiTheme.Warn);
            between = 1.5f;
        }
    }

    public override void OnPlayerDied()
    {
        lost++; Deaths++; Score -= 100;
        Event("round_lost");
        G.Banner("YOU DIED", UiTheme.Accent);
        between = 1.5f;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (between > 0) return float.PositiveInfinity;
        var hit = ShootBots(defenders.Select(x => x.Body), o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed) Score += 100;
        return dist;
    }

    public override IEnumerable<string> HudLines()
    {
        if (between <= 0) yield return $"Defenders left: {defenders.Count(d => !d.Body.Dead)}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Rounds won / lost", $"{won} / {lost}");
        if (clearTimes.Count > 0) yield return ("Avg clear time", $"{clearTimes.Average():0.0} s");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        foreach (var l in MovementLines()) yield return l;
    }

    public override int Badge() => won + lost == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)won / (won + lost), 0.40f);
}

/// <summary>
/// Hold a real defender spot. An attacker flashes through the choke (per-agent timing, path and sound), then
/// swings in pre-aimed. Being flashed follows Valorant's on-screen rule; dodge = get the pop off-screen.
/// </summary>
public sealed class FlashDodgeMode : MapMode
{
    public override AimFocus? Focus => st == St.Swing && attacker != null && !attacker.Dead && GodotObject.IsInstanceValid(attacker) && G.LineOfSight(G.View.Eye, attacker.Head)
        ? new AimFocus(IdOf(attacker), attacker.Head, BotCharacter.HeadRadius) : null;

    public override string Key => "flashmap";
    public override string Name => "Flash Dodge";
    public override string Description => "Hold a real angle. Hear the flash, turn it off-screen (60–90°+), swing back, win the duel.";
    public override string Category => "Utility";

    enum St { Hold, Thrown, Swing, After }
    St st;
    float stateT, holdFor, swingDelay, flight, turnAt;
    FlashAgent agent = FlashAgent.All[0];
    Vector3 from, ctrl, pop, swingTo, waypoint;
    bool passedChoke, turnedAway, timingTurn;
    FlashOrb? orb;
    BotCharacter? attacker;
    BotBrain? brain;
    int flashes, dodged, partial, flashed, wins, losses;
    readonly List<float> turnMs = new();
    string lastAgent = "";

    protected override void Setup() => NewRound();

    void NewRound()
    {
        var spots = MapSpot.Enemies
            // Site-level spots only (no heaven): height is measured from the choke's floor, so raised sites
            // (Ascent A is ~1 m above Main) still qualify.
            .Where(e => e.Feet.Y - MapSpot.Choke.Y < 1.5f && MapSpot.LineOfSight(e.Feet + new Vector3(0, PlayerView.EyeHeight, 0), MapSpot.Choke + new Vector3(0, 1.5f, 0)))
            .Where(e => e.Feet.DistanceTo(MapSpot.Choke) is > 6f and < 26f)
            .ToArray();
        var spot = spots.Length > 0 ? spots[Rng.Next(spots.Length)] : MapSpot.Enemies[0];
        SpawnFeet = spot.Feet;
        var d = MapSpot.Choke - spot.Feet;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        G.Respawn(SpawnFeet, SpawnYaw);
        G.Blind.Reset();
        if (attacker != null) G.Despawn(attacker);
        attacker = null; brain = null;
        st = St.Hold; stateT = 0; holdFor = R(1.2f, 2.8f);
    }

    void Throw()
    {
        agent = FlashAgent.All[Rng.Next(FlashAgent.All.Length)];
        var seen = MapSpot.Flashes.Where(f => G.LineOfSight(G.View.Eye, f.Pop)).ToArray();
        var lineup = seen.Length > 0 ? seen[Rng.Next(seen.Length)] : MapSpot.Flashes[Rng.Next(MapSpot.Flashes.Length)];
        from = lineup.From; pop = lineup.Pop;
        flight = agent.Path switch
        {
            FlashPath.Hawk => Mathf.Max(0.45f, from.DistanceTo(pop) / agent.Speed) + agent.Fuse,
            FlashPath.Crawl => Mathf.Max(0.8f, from.DistanceTo(pop) / agent.Speed),
            FlashPath.Bounce => 0.3f + agent.Fuse,
            FlashPath.Arc => agent.Fuse,
            _ => agent.Fuse,
        };
        var side = (pop - from).Cross(Vector3.Up).Normalized() * (Rng.Next(2) == 0 ? -1 : 1);
        ctrl = agent.Path switch
        {
            FlashPath.Curve => (from + pop) / 2 + side * 2.5f,
            FlashPath.Arc => (from + pop) / 2 + new Vector3(0, 3f, 0),
            _ => (from + pop) / 2,
        };
        if (PathBlocked()) ctrl = (from + pop) / 2;
        float aspect = AspectRatio();
        timingTurn = BlindModel.Eccentricity(G.View, pop, aspect) <= 1f;
        turnedAway = false;
        flashes++;
        orb = G.SpawnOrb(agent.Color);
        orb.Visible = agent.Path != FlashPath.Bounce;
        orb.GlobalPosition = from;
        G.SoundAt(agent.Cue, agent.Path == FlashPath.Bounce ? pop : from, maxRange: 50f, floor: 0.6f);
        st = St.Thrown; stateT = 0;
    }

    float AspectRatio()
    {
        var size = ((Node)G).GetViewport().GetVisibleRect().Size;
        return size.X / Mathf.Max(1, size.Y);
    }

    bool PathBlocked()
    {
        if (agent.Path is FlashPath.ThroughWall or FlashPath.Placed) return false;
        var prev = OrbPos(0);
        for (int i = 1; i <= 8; i++) { var p = OrbPos(i / 8f); if (Collision.Blocked(prev, p, G.Solid)) return true; prev = p; }
        return false;
    }

    Vector3 OrbPos(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        if (agent.Path == FlashPath.Crawl) return from.Lerp(pop, t);
        float u = 1 - t;
        return u * u * from + 2 * u * t * ctrl + t * t * pop;
    }

    bool AtPop => agent.Path is FlashPath.ThroughWall or FlashPath.Placed || (agent.Path == FlashPath.Bounce && stateT > 0.3f);

    void Pop()
    {
        float blindMax = agent.Path == FlashPath.Hawk ? 1.0f + 1.25f * Mathf.Clamp((flight - agent.Fuse) / 0.75f, 0, 1) : agent.BlindMax;
        var (sec, grade) = BlindModel.Evaluate(blindMax, G.View, pop, G.LineOfSight(G.View.Eye, pop), AspectRatio());
        G.Blind.Apply(sec);
        Event("flash", grade switch { BlindGrade.Flashed => 2, BlindGrade.Partial => 1, BlindGrade.NoLineOfSight => -1, _ => 0 }, turnedAway ? turnAt : -1);
        orb?.Pop(); orb = null;
        G.SoundAt(agent.Path == FlashPath.Bounce ? "shatter" : "flashpop", pop, maxRange: 50f, floor: 0.6f);
        lastAgent = $"{agent.Agent} {agent.Ability}";
        switch (grade)
        {
            case BlindGrade.Flashed: flashed++; Score -= 40; G.Banner($"FLASHED {sec:0.0}s · {lastAgent}", UiTheme.Accent); break;
            case BlindGrade.Partial: partial++; Score += 10; G.Banner($"Partial {sec:0.0}s · {lastAgent}", UiTheme.Warn); break;
            case BlindGrade.NoLineOfSight: flashes--; G.Banner("Flash was blocked", UiTheme.Dim); break;
            default: dodged++; Score += 50; G.Banner($"Dodged · {lastAgent}", UiTheme.Good); break;
        }

        // Attacker starts hidden behind the choke, walks start → choke → a clear swing spot, pre-aiming you.
        var toPlayer = new Vector3(SpawnFeet.X - MapSpot.Choke.X, 0, SpawnFeet.Z - MapSpot.Choke.Z).Normalized();
        var sideV = new Vector3(-toPlayer.Z, 0, toPlayer.X);
        Vector3 start = MapSpot.Choke - toPlayer * 2.5f;
        start.Y = Collision.Ground(new Vector3(start.X, MapSpot.Choke.Y + 0.7f, start.Z), G.Solid);
        bool found = false;
        foreach (var back in new[] { 1.5f, 2.5f, 3.5f })
        {
            foreach (var sd in new[] { 0f, 1.5f, -1.5f, 2.5f, -2.5f, 3.5f, -3.5f })
            {
                var p = MapSpot.Choke - toPlayer * back + sideV * sd;
                p.Y = Collision.Ground(new Vector3(p.X, MapSpot.Choke.Y + 0.7f, p.Z), G.Solid);
                if (Collision.Overlaps(p, G.Solid)) continue;
                if (G.LineOfSight(p + new Vector3(0, PlayerView.EyeHeight, 0), G.View.Eye)) continue;
                if (!G.LineOfSight(p + Vector3.Up, new Vector3(MapSpot.Choke.X, p.Y + 1, MapSpot.Choke.Z))) continue;
                start = p; found = true; break;
            }
            if (found) break;
        }
        swingTo = MapSpot.Choke + toPlayer * 1.5f;
        for (int i = 0; i < 8; i++)
        {
            var c = MapSpot.Choke + toPlayer * R(1.5f, 3.0f) + sideV * R(-1.2f, 1.2f);
            c.Y = Collision.Ground(new Vector3(c.X, MapSpot.Choke.Y + 0.7f, c.Z), G.Solid);
            if (!Collision.Overlaps(c, G.Solid)) { swingTo = c; break; }
        }
        waypoint = new Vector3(MapSpot.Choke.X, start.Y, MapSpot.Choke.Z);
        passedChoke = false;
        attacker = G.SpawnBot(start);
        attacker.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, -toPlayer.Z));
        brain = new BotBrain(attacker, Difficulty.Get(Tier).Bot, Rng)
        {
            HeldDir = toPlayer,
            PreAimed = Difficulty.FlashPreAim(Tier),
            HoldFireWhileBlind = Difficulty.HoldFireWhileBlind(Tier),
            ShoulderSight = Difficulty.ShoulderSight(Tier),
            Enabled = false,
        };
        var (a, b) = Difficulty.FlashSwingDelay(Tier);
        swingDelay = R(a, b);
        st = St.Swing; stateT = 0;
    }

    public override void Update(float dt)
    {
        stateT += dt;
        float aspect = AspectRatio();
        switch (st)
        {
            case St.Hold:
                if (stateT >= holdFor) Throw();
                break;
            case St.Thrown:
                if (orb != null)
                {
                    orb.GlobalPosition = AtPop ? pop : OrbPos(stateT / flight);
                    orb.Visible = agent.Path != FlashPath.Bounce || stateT > 0.3f;
                    if (AtPop) orb.Windup(stateT);
                }
                if (timingTurn && !turnedAway && BlindModel.Eccentricity(G.View, pop, aspect) > 1f) { turnedAway = true; turnAt = stateT * 1000f; }
                if (stateT >= flight)
                {
                    if (turnedAway && BlindModel.Eccentricity(G.View, pop, aspect) > 1f) turnMs.Add(turnAt);
                    Pop();
                }
                break;
            case St.Swing:
                var bdy = attacker!;
                if (stateT > swingDelay && !bdy.Dead)
                {
                    var target = passedChoke ? swingTo : waypoint;
                    var to = target - bdy.Feet; to.Y = 0;
                    float speed = Difficulty.FlashEntrySpeed(Tier), step = speed * dt;
                    // Rookie walks in, Regular jogs, higher tiers sprint and stop dead at the swing spot.
                    if (to.Length() > step) { bdy.Velocity = to.Normalized() * speed; bdy.Feet += bdy.Velocity * dt; }
                    else { bdy.Feet = new Vector3(target.X, bdy.Feet.Y, target.Z); if (passedChoke) bdy.Velocity = Vector3.Zero; passedChoke = true; }
                    var p = bdy.Feet; p.Y = Collision.Ground(p + new Vector3(0, 0.3f, 0), G.Solid); bdy.Feet = p;
                    brain!.Enabled = true;
                }
                brain?.Update(G, dt);
                TrackSeen(attacker);
                if (stateT > 10f)
                {
                    // The entry happened but nobody won the duel in time (or the attacker never got a sight line).
                    G.Banner(brain is { FirstSeenAt: >= 0 } ? "Too slow — the entry survived" : "They didn't swing", UiTheme.Dim);
                    if (brain is { FirstSeenAt: >= 0 }) losses++;
                    st = St.After; stateT = 0;
                }
                break;
            case St.After:
                if (stateT > 1.3f) NewRound();
                break;
        }
    }

    public override void OnPlayerDied()
    {
        losses++; Deaths++; Score -= 100;
        Event("round_lost");
        G.Banner("YOU DIED", UiTheme.Accent);
        st = St.After; stateT = 0;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (st != St.Swing || attacker == null) return float.PositiveInfinity;
        var hit = ShootBots(new[] { attacker }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            wins++; Score += 100;
            Event("round_won");
            G.Banner("Entry denied", UiTheme.Good);
            st = St.After; stateT = 0;
        }
        return dist;
    }

    public override IEnumerable<string> HudLines()
    {
        if (st == St.Hold) yield return "Hold the angle — listen for the flash";
    }

    public override float Accuracy => Shots == 0 ? 0 : (float)Hits / Shots;

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Dodged / partial / flashed", $"{dodged} / {partial} / {flashed} of {flashes}");
        if (turnMs.Count > 0) yield return ("Avg time to get it off-screen", $"{turnMs.Average():0} ms after the throw");
        yield return ("Entries denied / died", $"{wins} / {losses}");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%");
    }

    public override int Badge() => wins + losses == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)wins / (wins + losses));
}
