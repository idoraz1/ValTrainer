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
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.Any;
    public override string Name => L.N("Strafe Duels");
    public override string Description => L.N("Strafe A/D. A bot appears that moves like a player (ADAD, crouch, stop-shots) and shoots back. Counter-strafe and hit first.");
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

    // ---- bot movement: the shared player-like strafe model (StrafePlanner: the player's acceleration / braking) ----
    const float HalfLane = 3.5f;                      // the bot strafes within ±3.5 m of where it appeared

    // ---- tiers (Rookie … Pro); the movement tables live in StrafePlanner ----
    float Window => Difficulty.T(Tier, 8f, 6.5f, 5.5f, 5f, 4.5f);              // a duel nobody wins in this long times out
    (float, float) Depth => Tier switch { 0 => (9f, 13f), 1 => (10f, 15f), 2 => (10f, 17f), 3 => (11f, 19f), _ => (12f, 21f) };

    enum St { Wait, Live, After }
    St st = St.Wait;
    BotCharacter? bot;
    BotBrain? brain;
    StrafePlanner planner = null!;
    float laneLo, laneHi, stateT, movingFor, needMoving = 0.6f, cooldown = 0.3f;
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
        // One planner for the whole run (it remembers its last strafe direction across duels).
        planner ??= new StrafePlanner(Tier, Rng)
        {
            ForcePattern = G.Demo ? -1 : ForcePat,
            PatternPicked = pat =>
            {
                patSeen[(int)pat]++;
                Event("duel_pattern", (int)pat, Tier); Dev($"  pattern {StrafePlanner.PatNames[(int)pat]}");
            },
            Planned = Main.I.Dev && !G.Demo ? s => Dev("    " + s) : null,
        };
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
        G.Player.Reset(); G.RefillGun(); // every duel starts at 100 HP + 50 shield and a full magazine
        float anchor = Mathf.Clamp(G.View.Eye.X + R(-4, 4), -5.5f, 5.5f);
        laneLo = Mathf.Max(-9f, anchor - HalfLane); laneHi = Mathf.Min(9f, anchor + HalfLane);
        var (d0, d1) = Depth;
        bot = G.SpawnBot(new Vector3(anchor, 0, -R(d0, d1)));
        bot.FacingYaw = 180f;
        brain = new BotBrain(bot, Difficulty.Get(Tier).Bot with { RunAndGun = false }, Rng) { HoldFire = true };
        planner.Tier = Tier;
        planner.Begin(Now, bot.Feet.X, laneLo, laneHi); // it appears already moving (no standing pop-in)
        bot.Velocity = new Vector3(planner.V, 0, 0);
        firstShotDone = false;
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
        G.Banner(L.N("YOU DIED — they stopped and hit first"), UI.UiTheme.Accent);
        if (bot != null && GodotObject.IsInstanceValid(bot)) { var b = bot; b.GetTree().CreateTimer(0.6).Timeout += () => { if (GodotObject.IsInstanceValid(b)) G.Despawn(b); }; }
        EndDuel();
    }


    // =====================================================================================================
    // bot movement: StrafePlanner's patterns + the player's acceleration / braking
    // =====================================================================================================

    /// <summary>Dev: "--duel-pattern N" (0 long … 4 jiggle) forces one movement pattern (screenshots / tuning).</summary>
    static readonly int ForcePat = int.TryParse(CmdLine.DevAfter("--duel-pattern"), out var fp) && fp is >= 0 and <= 4 ? fp : -1;

    void MoveBot(float dt)
    {
        var b = bot!;
        float v = planner.Step(Now, dt, b.Feet.X, laneLo, laneHi);
        b.Crouched = planner.Crouch;
        var p = b.Feet;
        p.X = Mathf.Clamp(p.X + v * dt, laneLo - 0.5f, laneHi + 0.5f);
        b.Feet = p;
        b.Velocity = new Vector3(v, 0, 0);
        // Shoots only in its stop phases, once below the deadzone and past its tier's settle delay.
        brain!.HoldFire = !planner.MayShoot;
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
        if (Mathf.Abs(planner.V) > Mover.AccurateSpeed) errMoving.Add(err);
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
        ? L.N("Keep strafing A / D — a duel starts while you're running") : null;

    public override IEnumerable<string> HudLines()
    {
        yield return L.F($"Won {wins} · lost {losses} · timed out {timeouts}");
        if (st == St.Live) yield return L.F($"HP {G.Player.Hp:0} + shield {G.Player.Shield:0}");
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Duels won / lost / timed out"), $"{wins} / {losses} / {timeouts}");
        if (KillTimes.Count > 0) yield return (L.N("Median time to kill"), L.F($"{Median(KillTimes):0} ms"));
        yield return (L.N("Accuracy"), $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (firstShots > 0) yield return (L.N("First-shot accuracy"), L.F($"{100f * firstShotHits / firstShots:0}% of duels ({firstShotHits}/{firstShots})"));
        if (Hits > 0) yield return (L.N("Headshot %"), $"{100f * HeadHits / Hits:0}%");
        if (errAll.Count > 0)
            // Median crosshair-to-head angle at the moment of each shot; the moving-bot part is the tracking under strafes.
            yield return (L.N("Aim error at shot"), errMoving.Count > 0 ? L.F($"{Median(errAll):0.0}° (moving bot {Median(errMoving):0.0}°)") : $"{Median(errAll):0.0}°");
        foreach (var l in MovementLines()) yield return l;
        var seen = Enumerable.Range(0, patSeen.Length).Where(i => patSeen[i] > 0).Select(i => $"{L.T(StrafePlanner.PatShort[i])} {patSeen[i]}");
        if (patSeen.Sum() > 0) yield return (L.N("Bot moves"), string.Join(", ", seen));
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
