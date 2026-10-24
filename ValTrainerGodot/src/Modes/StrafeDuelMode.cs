using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Strafe Duels (Discord request: "range bots only hold A and D for long predictable intervals … just like the
/// counter-strafing drill but the spawning bot is moving"). You strafe in the Counter-Strafe lane; while you run, a bot
/// appears that moves like a player — short ADAD micro-strafes, longer strafes, crouch-strafes, A-crouch / D-crouch,
/// jiggles — and stops to shoot back. It is only accurate while stopped (like you). Counter-strafe, hit first, win.
/// <para>Numbers and where they come from:</para>
/// <list type="bullet">
/// <item>Accuracy deadzone 27.5% of max speed (Riot, VALORANT patch notes 3.0, 2021: "Weapon Deadzones … reduced to
/// 27.5%, down from 30%") = <see cref="Mover.AccurateSpeed"/> ≈ 1.49 m/s with a rifle. The bot only fires below it.</item>
/// <item>Bot acceleration and braking are the player's (<see cref="Mover"/>: <see cref="Mover.GroundAccel"/> 18 m/s², full
/// speed in ≈0.3 s, frame-counted from a VALORANT Range recording; brake 38 / 24 m/s² = Riot's measured ≈105 ms to accurate,
/// ≈160 ms to a full stop; counter-strafe 6% quicker) — the same constants as Mixed Peeks (PeekMixMode).</item>
/// <item>Direction-change cadence: ADAD micro-strafes 0.16–0.45 s per direction, jiggles 0.10–0.16 s (community guides
/// describe ADAD as "spamming the movement keys at short intervals"; with 0.3 s to full speed a 0.2–0.4 s tap is a
/// 0.4–1.4 m step, the size seen in pro duels). Riot gives no figure; these are project estimates, tune them here.</item>
/// <item>Bot reaction / aim: the tier's <see cref="BotSkill"/> (Difficulty.cs; mean human reaction ≈247 ms per Riot's
/// "On Peeker's Advantage" post), with run-and-gun off: every tier stops to shoot.</item>
/// </list>
/// </summary>
public sealed class StrafeDuelMode : MovementMode
{
    public override string Key => "strafeduel";
    public override string Name => "Strafe Duels";
    public override string Description => "Strafe A/D. A bot appears that moves like a player (ADAD, crouch, stop-shots) and shoots back. Counter-strafe and hit first.";
    public override string SimKind => "bot";
    public override float Duration => 90f;

    public override AimFocus? Focus => st == St.Live && Alive ? new AimFocus(IdOf(bot!), bot!.Head, BotCharacter.HeadRadius) : null;

    /// <summary>The Counter-Strafe lane: low rails left / right / front, a back wall.</summary>
    public override IEnumerable<Box> ExtraSolids => new[]
    {
        new Box(new Vector3(-9, 0, -2.5f), new Vector3(-8.5f, 1.2f, 2.5f)),
        new Box(new Vector3(8.5f, 0, -2.5f), new Vector3(9, 1.2f, 2.5f)),
        new Box(new Vector3(-9, 0, -3), new Vector3(9, 0.8f, -2.5f)),
        new Box(new Vector3(-9, 0, 2.5f), new Vector3(9, 1.2f, 3)),
    };

    // ---- bot movement: the player's horizontal model (Mover.cs) ----
    const float Accel = Mover.GroundAccel, BrakeHigh = 38f, BrakeLow = 24f, CounterBonus = 1.06f;
    const float CrouchSpeed = Mover.CrouchSpeed;      // 2.43 m/s
    const float HalfLane = 3.5f;                      // the bot strafes within ±3.5 m of where it appeared

    // ---- tiers (Rookie … Pro) ----
    float RunCap => Difficulty.T(Tier, 2.97f, 4.2f, 5.4f, 5.4f, 5.4f);           // Rookie walks, Regular jogs
    (float, float) LongDur => Tier switch { 0 => (1.0f, 1.6f), 1 => (0.8f, 1.3f), 2 => (0.6f, 1.1f), 3 => (0.5f, 0.9f), _ => (0.4f, 0.8f) };
    (float, float) TapDur => Tier switch { 0 => (0.45f, 0.6f), 1 => (0.35f, 0.5f), 2 => (0.25f, 0.42f), 3 => (0.2f, 0.35f), _ => (0.16f, 0.3f) };
    (float, float) StopDur => Tier switch { 0 => (0.9f, 1.3f), 1 => (0.7f, 1.0f), 2 => (0.5f, 0.8f), 3 => (0.35f, 0.6f), _ => (0.25f, 0.45f) };
    /// <summary>Stopped (below the deadzone) → first trigger pull: Rookie settles before shooting, Pro shoots the moment it's accurate.</summary>
    float ShootDelay => Difficulty.T(Tier, .25f, .17f, .10f, .06f, .03f);
    /// <summary>Chance a movement pattern flows straight into the next one with no stop (a fake-out).</summary>
    float ChainChance => Difficulty.T(Tier, 0f, .1f, .2f, .3f, .35f);
    /// <summary>Chance a stop is crouched (crouch-shoot); Elite+ also crouch-spam during it.</summary>
    float CrouchStopChance => Difficulty.T(Tier, 0f, .1f, .3f, .45f, .55f);
    float Window => Difficulty.T(Tier, 8f, 6.5f, 5.5f, 5f, 4.5f);              // a duel nobody wins in this long times out
    (float, float) Depth => Tier switch { 0 => (9f, 13f), 1 => (10f, 15f), 2 => (10f, 17f), 3 => (11f, 19f), _ => (12f, 21f) };

    enum Pat { Long, Micro, CrouchStrafe, ACrouchD, Jiggle }
    static readonly string[] PatNames = { "Long strafe", "ADAD micro-strafe", "Crouch-strafe", "A-crouch / D-crouch", "Jiggle" };
    static readonly string[] PatShort = { "long", "ADAD", "crouch", "ACD", "jiggle" };
    // Pattern weights per tier: Rookie = long predictable strafes; Pro = mostly micro-strafes, A-crouch-D and jiggles.
    static readonly int[][] Weights =
    {
        new[] { 6, 1, 0, 0, 0 },
        new[] { 4, 3, 1, 1, 0 },
        new[] { 2, 4, 2, 2, 1 },
        new[] { 1, 4, 2, 3, 2 },
        new[] { 1, 4, 2, 3, 3 },
    };

    /// <summary>One "key state" of the bot: strafe direction (−1 left, 0 none), crouch, and whether it may shoot.</summary>
    readonly record struct Seg(int Dir, float Dur, bool Crouch, bool Shoot);

    enum St { Wait, Live, After }
    St st = St.Wait;
    BotCharacter? bot;
    BotBrain? brain;
    readonly Queue<Seg> plan = new();
    Seg seg;
    float segEnd, v, laneLo, laneHi, readyAt = -1, stateT, movingFor, needMoving = 0.6f, cooldown = 0.3f;
    int lastDir = 1;
    bool firstShotDone;
    int wins, losses, timeouts, spawned, firstShots, firstShotHits;
    readonly int[] patSeen = new int[5];
    readonly List<float> errAll = new(), errMoving = new();

    bool Alive => bot != null && GodotObject.IsInstanceValid(bot) && !bot.Dead;
    void Dev(string s) { if (Main.I.Dev && !G.Demo) GD.Print($"[strafeduel] t={Now:0.00} {s}"); }

    // ---- scripted counter-strafing for the drill preview and dev --simaim runs (the simulated player only aims) ----
    bool auto;
    enum Ak { Strafe, Counter, Still }
    Ak autoPhase = Ak.Strafe;
    float autoUntil;
    bool autoRight;

    protected override void Setup()
    {
        G.JumpEnabled = false; // the lane's rails are 0.8–1.2 m: no jumping out (as in Counter-Strafe)
        auto = G.Demo || (CmdLine.Dev && (CmdLine.Has("--simaim") || MovementScript.Auto));
        if (auto) Mover.KeyOverride = AutoKey;
    }

    // =====================================================================================================
    // round flow
    // =====================================================================================================

    public override void Update(float dt)
    {
        stateT += dt;
        var m = G.Mover;
        switch (st)
        {
            case St.Wait:
                cooldown -= dt;
                movingFor = m.Speed > m.RunMax * 0.8f ? movingFor + dt : Mathf.Max(0, movingFor - dt);
                if (cooldown <= 0 && movingFor >= needMoving && !G.Player.Dead) SpawnDuel();
                break;
            case St.Live:
                if (!Alive) break;
                MoveBot(dt);
                brain!.Update(G, dt);
                if (st != St.Live) break; // the player died during the brain's shot (synchronous OnPlayerDied)
                TrackSeen(bot);
                if (Now - bot!.SpawnTime > Window)
                {
                    timeouts++; Score -= 50; G.Sound("fail", 0.6f);
                    Event("target_expired", IdOf(bot)); Dev("timed out");
                    G.Despawn(bot); EndDuel();
                }
                break;
            case St.After:
                if (G.Player.Dead && stateT > 1.2f) { G.Respawn(StartFeet, StartYaw); Dev("respawned"); }
                if (stateT > 0.8f && !G.Player.Dead) { st = St.Wait; stateT = 0; Dev($"waiting (speed {m.Speed:0.0})"); }
                break;
        }
        if (auto) AutoStep();
    }

    void SpawnDuel()
    {
        G.Player.Reset(); // every duel starts at 100 HP + 50 shield
        float anchor = Mathf.Clamp(G.View.Eye.X + R(-4, 4), -5.5f, 5.5f);
        laneLo = Mathf.Max(-9f, anchor - HalfLane); laneHi = Mathf.Min(9f, anchor + HalfLane);
        var (d0, d1) = Depth;
        bot = G.SpawnBot(new Vector3(anchor, 0, -R(d0, d1)));
        bot.FacingYaw = 180f;
        brain = new BotBrain(bot, Difficulty.Get(Tier).Bot with { RunAndGun = false }, Rng) { HoldFire = true };
        plan.Clear();
        PlanPattern(forceMove: true);
        NextSeg();
        v = seg.Dir * RunCap * R(0.6f, 1f); // it appears already moving (no standing pop-in)
        bot.Velocity = new Vector3(v, 0, 0);
        readyAt = -1; firstShotDone = false;
        spawned++;
        st = St.Live; stateT = 0;
        G.Sound("tick");
        Event("target_spawn", IdOf(bot), AngleFromCrosshair(bot.Head)); Dev($"duel {spawned} at x={bot.Feet.X:0.0} z={bot.Feet.Z:0.0}");
        if (auto) { autoPhase = Ak.Counter; autoUntil = Now + 0.07f; }
    }

    void EndDuel()
    {
        bot = null; brain = null;
        st = St.After; stateT = 0;
        movingFor = 0; needMoving = R(0.3f, 1.0f); cooldown = R(0.2f, 0.5f);
    }

    public override void OnPlayerDied()
    {
        losses++; Deaths++; Score -= 75;
        Event("round_lost"); Dev("you died");
        G.Banner("YOU DIED — they stopped and hit first", UI.UiTheme.Accent);
        if (bot != null && GodotObject.IsInstanceValid(bot)) { var b = bot; b.GetTree().CreateTimer(0.6).Timeout += () => { if (GodotObject.IsInstanceValid(b)) G.Despawn(b); }; }
        EndDuel();
    }

    // =====================================================================================================
    // bot movement: pattern planner + the player's acceleration / braking
    // =====================================================================================================

    /// <summary>Dev: "--duel-pattern N" (0 long … 4 jiggle) forces one movement pattern (screenshots / tuning).</summary>
    static readonly int ForcePat = int.TryParse(CmdLine.DevAfter("--duel-pattern"), out var fp) && fp is >= 0 and <= 4 ? fp : -1;

    Pat PickPattern()
    {
        if (ForcePat >= 0 && !G.Demo) return (Pat)ForcePat;
        var w = Weights[Math.Clamp(Tier, 0, Weights.Length - 1)];
        int r = Rng.Next(w.Sum());
        for (int i = 0; i < w.Length; i++) { if (r < w[i]) return (Pat)i; r -= w[i]; }
        return Pat.Long;
    }

    void Add(int dir, float dur, bool crouch = false, bool shoot = false) => plan.Enqueue(new Seg(dir, dur, crouch, shoot));

    /// <summary>Queues one movement pattern and (usually) a stop to shoot after it.</summary>
    void PlanPattern(bool forceMove = false)
    {
        var pat = PickPattern();
        patSeen[(int)pat]++;
        Event("duel_pattern", (int)pat, Tier); Dev($"  pattern {PatNames[(int)pat]}");
        int d = Rng.NextDouble() < 0.65 ? -lastDir : lastDir; // usually reverses, sometimes re-strafes the same way
        var (t0, t1) = TapDur;
        switch (pat)
        {
            case Pat.Long:
            {
                var (l0, l1) = LongDur;
                Add(d, R(l0, l1));
                break;
            }
            case Pat.Micro:
            {
                int n = 3 + Rng.Next(Difficulty.Ti(Tier, 1, 2, 3, 4, 4));
                for (int i = 0; i < n; i++) { Add(d, R(t0, t1)); d = -d; }
                break;
            }
            case Pat.CrouchStrafe:
                Add(d, R(0.45f, 0.8f), crouch: true);
                if (Rng.NextDouble() < 0.5) Add(-d, R(0.35f, 0.6f), crouch: true);
                break;
            case Pat.ACrouchD:
            {
                // Strafe, crouch as you stop (a quick crouched shot), stand, strafe the other way.
                int n = 2 + Rng.Next(2);
                for (int i = 0; i < n; i++) { Add(d, R(t0, t1) * 1.2f); Add(0, R(0.16f, 0.3f), crouch: true, shoot: true); d = -d; }
                break;
            }
            case Pat.Jiggle:
            {
                int n = 2 + Rng.Next(3);
                for (int i = 0; i < n; i++) { Add(d, R(0.10f, 0.16f)); d = -d; }
                break;
            }
        }
        if (forceMove || Rng.NextDouble() >= ChainChance) AddStop();
        if (Main.I.Dev && !G.Demo)
            Dev("    " + string.Join(" ", plan.Select(s => (s.Dir < 0 ? "A" : s.Dir > 0 ? "D" : "stop") + (s.Crouch ? "+C" : "") + $"{s.Dur:0.00}")));
    }

    void AddStop()
    {
        var (s0, s1) = StopDur;
        float total = R(s0, s1);
        bool crouch = Rng.NextDouble() < CrouchStopChance;
        if (crouch && Tier >= 3 && Rng.NextDouble() < 0.6)
        {
            // Crouch spam while shooting: the head bobs ~0.5 m every 0.14–0.24 s.
            bool c = true;
            for (float t = 0; t < total;) { float s = R(0.14f, 0.24f); Add(0, s, crouch: c, shoot: true); c = !c; t += s; }
        }
        else Add(0, total, crouch: crouch, shoot: true);
    }

    void NextSeg()
    {
        if (plan.Count == 0) PlanPattern();
        seg = plan.Dequeue();
        if (seg.Dir != 0)
        {
            // Near the edge of its lane: this and the rest of the pattern go the other way (stays readable, no clamping).
            float x = bot!.Feet.X;
            if ((seg.Dir > 0 && x > laneHi - 1f) || (seg.Dir < 0 && x < laneLo + 1f))
            {
                seg = seg with { Dir = -seg.Dir };
                var rest = plan.ToArray(); plan.Clear();
                foreach (var s in rest) plan.Enqueue(s with { Dir = -s.Dir });
            }
            lastDir = seg.Dir;
        }
        segEnd = Now + seg.Dur;
    }

    void MoveBot(float dt)
    {
        var b = bot!;
        // Running out of lane: end this strafe early, so the brake lands inside it (stopping distance v²/2a).
        if (seg.Dir != 0)
        {
            float stopAt = b.Feet.X + v * Mathf.Abs(v) / (2f * BrakeHigh);
            if ((seg.Dir > 0 && stopAt > laneHi) || (seg.Dir < 0 && stopAt < laneLo)) segEnd = Now;
        }
        while (Now >= segEnd) NextSeg();

        b.Crouched = seg.Crouch;
        float cap = seg.Crouch ? CrouchSpeed : RunCap;
        float brake = (Mathf.Abs(v) > Mover.RunSpeed * 0.25f ? BrakeHigh : BrakeLow) * dt;
        if (seg.Dir == 0) v = Mathf.MoveToward(v, 0, brake);
        else
        {
            float along = v * seg.Dir;
            if (along < 0) along = Mathf.Min(0, along + brake * CounterBonus);   // counter-strafe through zero
            else if (along > cap) along = Mathf.Max(cap, along - brake);         // crouching from a run
            else along = Mathf.Min(cap, along + Accel * dt);
            v = along * seg.Dir;
        }
        var p = b.Feet;
        p.X = Mathf.Clamp(p.X + v * dt, laneLo - 0.5f, laneHi + 0.5f);
        b.Feet = p;
        b.Velocity = new Vector3(v, 0, 0);

        // Shoots only in its stop phases, once below the deadzone and past its tier's settle delay.
        bool slow = Mathf.Abs(v) <= Mover.AccurateSpeed;
        if (seg.Shoot && slow) { if (readyAt < 0) readyAt = Now + ShootDelay; }
        else readyAt = -1;
        brain!.HoldFire = !(readyAt >= 0 && Now >= readyAt);
    }

    // =====================================================================================================
    // shooting + scoring
    // =====================================================================================================

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (st != St.Live || !Alive) return float.PositiveInfinity;
        // Tracking error at the moment of the shot: crosshair → head, split by whether the bot was moving.
        float err = AngleFromCrosshair(bot!.Head);
        errAll.Add(err);
        if (Mathf.Abs(v) > Mover.AccurateSpeed) errMoving.Add(err);
        bool first = !firstShotDone;
        firstShotDone = true;
        if (first) firstShots++;
        var hit = ShootBots(new[] { bot! }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        if (first) firstShotHits++;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            wins++;
            Event("round_won"); Dev($"won in {KillTimes[^1]:0} ms");
            Score += 100 + (int)Mathf.Max(0, (1500 - KillTimes[^1]) / 5);
            EndDuel(); // the body plays its death and frees itself
        }
        return dist;
    }

    public override string? Prompt => st == St.Wait && G.Mover.Speed < G.Mover.RunMax * 0.8f
        ? "Keep strafing A / D — a duel starts while you're running" : null;

    public override IEnumerable<string> HudLines()
    {
        yield return $"Won {wins} · lost {losses} · timed out {timeouts}";
        if (st == St.Live) yield return $"HP {G.Player.Hp:0} + shield {G.Player.Shield:0}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Duels won / lost / timed out", $"{wins} / {losses} / {timeouts}");
        if (KillTimes.Count > 0) yield return ("Median time to kill", $"{Median(KillTimes):0} ms");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (firstShots > 0) yield return ("First-shot accuracy", $"{100f * firstShotHits / firstShots:0}% of duels ({firstShotHits}/{firstShots})");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        if (errAll.Count > 0)
            // Median crosshair-to-head angle at the moment of each shot; the moving-bot part is the tracking under strafes.
            yield return ("Aim error at shot", $"{Median(errAll):0.0}°" + (errMoving.Count > 0 ? $" (moving bot {Median(errMoving):0.0}°)" : ""));
        foreach (var l in MovementLines()) yield return l;
        var seen = Enumerable.Range(0, patSeen.Length).Where(i => patSeen[i] > 0).Select(i => $"{PatShort[i]} {patSeen[i]}");
        if (patSeen.Sum() > 0) yield return ("Bot moves", string.Join(", ", seen));
    }

    public override int Badge()
    {
        int n = wins + losses + timeouts;
        return n == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)wins / n);
    }

    // =====================================================================================================
    // scripted counter-strafing (preview / dev): strafe until a duel, counter-tap, stand to shoot, short re-strafe
    // =====================================================================================================

    void AutoStep()
    {
        if (st != St.Live || !Alive) { autoPhase = Ak.Strafe; return; }
        if (Now < autoUntil) return;
        switch (autoPhase)
        {
            case Ak.Counter: autoPhase = Ak.Still; autoUntil = Now + R(0.55f, 0.85f); break;
            case Ak.Still: autoPhase = Ak.Strafe; autoRight = !autoRight; autoUntil = Now + R(0.22f, 0.35f); break;
            default: autoPhase = Ak.Counter; autoUntil = Now + 0.07f; break;
        }
    }

    bool AutoKey(InputBinding k)
    {
        if (G.Player.Dead) return false;
        var kb = Main.I.Valorant;
        if (st != St.Live || !Alive)
        {
            autoRight = (int)(Now / 1.3f) % 2 == 0;
            return k == (autoRight ? kb.KeyRight : kb.KeyLeft);
        }
        return autoPhase switch
        {
            Ak.Strafe => k == (autoRight ? kb.KeyRight : kb.KeyLeft),
            Ak.Counter => k == (autoRight ? kb.KeyLeft : kb.KeyRight),
            _ => false,
        };
    }
}
