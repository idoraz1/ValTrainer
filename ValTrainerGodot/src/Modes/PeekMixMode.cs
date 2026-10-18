using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

public sealed class PeekMixMode : BotMode
{
    public override string Key => "peek_mix";
    public override string Name => "Mixed Peeks";
    public override string Description => "Hold an angle against unpredictable peeks. Shoot a bait and it punishes you; hold fire and it commits.";
    public override string Category => "Movement";
    public override string? MadeBy => "Widowrasko";
    public override string SimKind => "bot";
    public override float Duration => 120f;
    public override bool InfiniteAmmo => true;
    public override IEnumerable<Box> ExtraSolids => new[] {
        new Box(new Vector3(-1.8f, 0, -10), new Vector3(1.8f, 3.2f, -9)) };

    // Player-consistent horizontal profile (Mover.cs): 5.4 run / 2.97 walk / 2.43 crouch, the player's acceleration,
    // two-phase brake ≈160–190 ms from full run. Bots faster than 3.2 m/s are audible (BotCharacter.RunStepSpeed).
    const float Accel = Mover.GroundAccel, BrakeHigh = 38f, BrakeLow = 24f;
    const float RunSpeed = 5.4f, WalkSpeed = 2.97f, CrouchSpeed = 2.43f;
    // Jump project parameters (community height estimate ≈1.1–1.2 m; knowledge/Valorant Movement §5): apex v0²/2g,
    // airtime 2·v0/g — tune these, not the physics.
    const float JumpG = 12.5f, JumpV0 = 5.3f;

    // Peek kinds (telemetry v3, peek_trial kind 0–7).
    const int KShort = 0, KWide = 1, KCrouch = 2, KDouble = 3, KJiggle = 4, KShoulder = 5, KShift = 6, KRepeek = 7;
    static readonly string[] names = { "Short", "Wide", "Crouch", "Double", "Jiggle", "Shoulder", "Shift walk", "Repeek" };
    static readonly float[] timeouts = { 5f, 5f, 5f, 5f, 8f, 7f, 7f, 8f };

    // Difficulty (tier 0 Rookie … 4 Pro). The duel itself (reaction, accuracy) is BotBrain's tier skill; these set
    // which peeks appear, how fast and unpredictable they are, and how well baits read and punish your shots.
    static readonly int[][] kindsByTier =
    {
        new[] { KShort, KWide, KCrouch, KShift },
        new[] { KShort, KWide, KCrouch, KShift, KDouble, KRepeek },
        new[] { KShort, KWide, KCrouch, KShift, KDouble, KRepeek, KJiggle },
        new[] { KShort, KWide, KCrouch, KShift, KDouble, KRepeek, KJiggle, KShoulder },
        new[] { KShort, KWide, KCrouch, KShift, KDouble, KRepeek, KJiggle, KShoulder },
    };
    float PeekSpeed => Difficulty.T(Tier, 2.97f, 4.2f, 5.4f, 5.4f, 5.4f);   // running exits (approach stays a run)
    (float, float) StartDelay => Tier switch
    {
        0 => (1.6f, 2.2f), 1 => (1.3f, 2.3f), 2 => (1.0f, 2.5f), 3 => (0.8f, 2.8f), _ => (0.5f, 3.2f),
    };
    (float, float) DoubleGap => Tier switch
    {
        <= 1 => (0.45f, 0.7f), 2 => (0.25f, 0.45f), 3 => (0.1f, 0.25f), _ => (0f, 0.14f),
    };
    float PunishDelay => Difficulty.T(Tier, .45f, .35f, .25f, .15f, .08f); // shot → bait converts to the punish
    float JumpOutChance => Difficulty.T(Tier, 0f, 0f, .2f, .3f, .35f);
    float SpotChance => Difficulty.T(Tier, 0f, 0f, .4f, .6f, .75f);       // jiggle escalates to a jump-spot

    enum Phase { Approach, Wait, Out, Hold, Back, Hidden, JumpSpot, Duel }
    // What the bot does when the between-poke pause (Hidden) ends.
    enum After { Poke, Commit, Shuffle, Spot }

    sealed class Opponent
    {
        public BotCharacter Body = null!;
        public BotBrain Brain = null!;
        public float Side, StartAt, HoldUntil, HiddenUntil, SeenAt = -1;
        public Phase Phase = Phase.Approach;
        public After AfterHidden = After.Commit;
        public float Destination;
        public float V;                    // signed speed along the moving axis (X for peeks, Z for the approach)
        public float JumpV;
        public float WaitSpot, WaitZ;
        public float PokeStart = -1;       // current bait stimulus start (the bite window opens here)
        public int Poke, Pokes, FailedBaits;
        public float CommitSide = 1f;
        public bool Escaped, FirstHit, Bait, Converted, ChestExposed, ShoulderExposed;
        public bool Alive => GodotObject.IsInstanceValid(Body) && !Body.Dead && !Escaped;
    }
    readonly List<Opponent> opponents = new();
    readonly int[] attempts = new int[8], wins = new int[8], losses = new int[8];
    readonly List<float> firstHitMs = new(), transferMs = new(), trialShots = new();
    // Dev test seam (--dev --peek-forcebite): the simulated player never throws preemptive shots at motion, so the
    // bait→punish branches need forced bites to be smoke-testable. Odd pokes and the jump-spot convert.
    bool forceBite;
    int forceKind = -1;
    int kind, trial;
    bool active;
    float nextAt, expires, lastKill = -1;
    public override AimFocus? Focus => NearestHead(opponents.Where(x => x.Alive).Select(x => x.Body));
    protected override void Setup()
    {
        nextAt = 1f; Event("peek_config", Tier, 3);
        var argv = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        forceBite = Main.I.Dev && argv.Contains("--peek-forcebite");
        int ki = Array.IndexOf(argv, "--peek-forcekind");
        if (Main.I.Dev && ki >= 0 && ki + 1 < argv.Length) int.TryParse(argv[ki + 1], out forceKind);
    }
    void StartTrial()
    {
        opponents.Clear();
        var pool = kindsByTier[Math.Clamp(Tier, 0, kindsByTier.Length - 1)];
        kind = forceKind is >= 0 and <= 7 ? forceKind : pool[Rng.Next(pool.Length)]; attempts[kind]++; trial++; active = true;
        lastKill = -1; expires = Now + timeouts[kind]; G.Player.Reset();
        trialShots.Clear();
        Event("peek_trial", trial, kind);
        float side = Rng.Next(2) == 0 ? -1 : 1;
        float waitZ = -R(11.6f, 12.4f);                    // small per-trial depth variation
        var (d0, d1) = StartDelay;
        Spawn(side, waitZ, Now + R(d0, d1));
        var (g0, g1) = DoubleGap;
        if (kind == KDouble) Spawn(-side, waitZ, opponents[0].StartAt + R(g0, g1));
    }
    void Spawn(float side, float waitZ, float startAt)
    {
        // Spawns DEEP behind the cover wall (same |x| as the wait spot, so the wall occludes the whole approach)
        // and RUNS up to it: running footsteps give an audible warning before the first appearance — except the
        // shift-walk kind, which approaches silently at walk speed.
        var b = G.SpawnBot(new Vector3(side * 1.3f, 0, waitZ - R(3f, 6f)));
        b.FacingYaw = AimPlusMath.FaceOrigin(b.Feet);
        var brain = new BotBrain(b, Difficulty.Get(Tier).Bot, Rng) { Enabled = false };
        opponents.Add(new Opponent { Body = b, Brain = brain, Side = side,
            WaitSpot = side * 1.3f, WaitZ = waitZ, StartAt = startAt, CommitSide = side });
        Event("target_spawn", IdOf(b), AngleFromCrosshair(b.Head));
    }
    void Finish(bool won, int reason)
    {
        if (!active) return;
        active = false;
        if (won) wins[kind]++; else losses[kind]++;
        Event(won ? "peek_win" : "peek_loss", trial, reason);
        foreach (var x in opponents) if (GodotObject.IsInstanceValid(x.Body) && !x.Body.Dead) G.Despawn(x.Body);
        nextAt = Now + .8f + Rng.NextSingle() * .6f;
    }
    // Accelerates toward dest and brakes so the stop lands on it (Mover-like profile); signed speed means a
    // direction flip decelerates THROUGH zero (counter-strafe turn) instead of teleporting the other way.
    bool MoveAxis(Opponent x, float dest, bool alongZ, float maxSpeed, float dt)
    {
        var p = x.Body.Feet;
        float d = dest - (alongZ ? p.Z : p.X);
        float brake = Mathf.Abs(x.V) > WalkSpeed ? BrakeHigh : BrakeLow;
        float vTarget = Mathf.Abs(d) < .002f ? 0 : Mathf.Sign(d) * Mathf.Min(maxSpeed, Mathf.Sqrt(2f * brake * Mathf.Abs(d)));
        float rate = Mathf.Abs(vTarget) > Mathf.Abs(x.V) ? Accel : brake;
        x.V = Mathf.MoveToward(x.V, vTarget, rate * dt);
        float step = x.V * dt;
        if (Mathf.Abs(d) <= Mathf.Max(Mathf.Abs(step), .002f))
        {
            x.V = 0;
            x.Body.Velocity = Vector3.Zero;
            x.Body.Feet = alongZ ? new Vector3(p.X, p.Y, dest) : new Vector3(dest, p.Y, p.Z);
            return true;
        }
        x.Body.Feet = alongZ ? new Vector3(p.X, p.Y, p.Z + step) : new Vector3(p.X + step, p.Y, p.Z);
        x.Body.Velocity = alongZ ? new Vector3(0, 0, x.V) : new Vector3(x.V, 0, 0);
        return false;
    }
    float OutSpeed(Opponent x) => x.Body.Crouched ? CrouchSpeed : kind == KShift ? WalkSpeed
        : x.Bait ? (kind == KShoulder ? 3.6f : 4.5f) : PeekSpeed;
    float BaitHold() => kind == KShoulder ? R(.10f, .20f) : kind == KRepeek ? R(.10f, .18f) : R(0f, .06f);
    float InterPoke() => kind == KRepeek ? R(.4f, 1.0f) : kind == KShoulder ? R(.25f, .6f) : R(.15f, .5f);
    bool BaitKind() => kind is KJiggle or KShoulder or KRepeek;
    // Did the holder fire during this stimulus? The shot timing against the exposure window is the whole signal.
    // With the dev seam: every poke after the first counts as bitten; the jump-spot bites on the way down so its
    // arc develops first.
    bool Bit(Opponent x) => x.PokeStart >= 0 && (forceBite
        ? x.Poke >= 1 || x.Phase == Phase.JumpSpot && x.JumpV < 0
        : trialShots.Any(s => s >= x.PokeStart - 0.05f && Now >= s + PunishDelay));   // lower tiers read it later
    // Plans the first action once the wait after the approach ends.
    void StartAct(Opponent x)
    {
        x.Bait = BaitKind();
        x.Pokes = kind switch
        {
            KJiggle => 2 + Rng.Next(3),
            KShoulder => 1 + (Rng.NextDouble() < .5 ? 1 : 0),
            _ => 1,
        };
        x.Poke = 0;
        x.FailedBaits = 0;
        x.PokeStart = Now;
        x.Destination = PokeDest(x);
        if (kind == KCrouch) x.Body.Crouched = true;
        x.Phase = Phase.Out;
    }
    float PokeDest(Opponent x) => x.Side * (kind switch
    {
        KShort => 2.85f,
        KJiggle => R(2.5f, 3.2f),
        KShoulder => R(2.3f, 2.5f),
        KRepeek => R(2.6f, 2.9f),
        KCrouch => R(4.4f, 5.2f),
        KShift => R(4.0f, 4.8f),
        _ => R(4.4f, 5.2f),                                // wide, double
    });
    void StartJump(Opponent x, bool spot)
    {
        x.Body.Grounded = false;
        x.JumpV = JumpV0;
        Event("peek_jump", trial, spot ? 0 : 1);
    }
    // The holder fired at the bait: stop fishing, punish through the recovery — wide swing or jump-out.
    void ConvertToPunish(Opponent x)
    {
        if (x.Converted) return;
        x.Converted = true;
        x.Bait = false;
        Event("peek_bite", trial, 1);
        bool jumpOut = x.Body.Grounded && Rng.NextDouble() < JumpOutChance;
        Event("peek_punish", trial, jumpOut ? 1 : 0);
        x.Destination = x.CommitSide * R(4.4f, 5.2f);
        if (jumpOut) StartJump(x, false);
        x.Phase = Phase.Out;
    }
    // A bait stimulus ended behind cover with no shot from the holder.
    void BaitFailed(Opponent x)
    {
        x.FailedBaits++;
        Event("peek_bite", trial, 0);
    }
    // Between-poke pause over: next poke, the jump-spot escalation, or the commit.
    void ResumeAfterHidden(Opponent x)
    {
        switch (x.AfterHidden)
        {
            case After.Poke:
                x.PokeStart = Now;
                x.Destination = PokeDest(x);
                x.Phase = Phase.Out;
                break;
            case After.Spot:
                x.PokeStart = Now;
                StartJump(x, true);
                x.Phase = Phase.JumpSpot;
                break;
            case After.Shuffle:
                x.PokeStart = -1;
                x.Destination = x.CommitSide * 1.3f;
                x.Phase = Phase.Hidden;                    // one more leg: shuffle behind cover, then commit
                x.AfterHidden = After.Commit;
                break;
            default:
                x.Bait = false;
                x.Destination = x.CommitSide * R(4.4f, 5.2f);
                x.Phase = Phase.Out;
                break;
        }
    }
    // A bait poke finished its hold: go back behind cover and plan what follows.
    void AfterHold(Opponent x)
    {
        x.Destination = x.WaitSpot;
        x.Phase = Phase.Back;
    }
    // Reached cover after a poke (or landed a spot): record the bait result and plan the next step.
    void PlanNext(Opponent x)
    {
        bool bitten = Bit(x);
        if (bitten) { ConvertToPunish(x); return; }
        BaitFailed(x);
        x.Poke++;
        // Under the dev seam the jiggle's first failed poke goes straight to the spot, so the spot path is
        // reachable even though every later poke converts.
        if (x.Bait && x.Poke < x.Pokes && !(forceBite && kind == KJiggle && x.FailedBaits == 1))
        {
            x.HiddenUntil = Now + InterPoke();
            x.AfterHidden = After.Poke;
            x.Phase = Phase.Hidden;
            return;
        }
        if (kind == KRepeek)
        {
            // Slip away, maybe swap sides (the shuffle is audible), then commit wide.
            x.HiddenUntil = Now + InterPoke();
            bool swap = Rng.NextDouble() >= .75;
            x.CommitSide = swap ? -x.Side : x.Side;
            x.AfterHidden = swap ? After.Shuffle : After.Commit;
            x.Phase = Phase.Hidden;
            return;
        }
        if (kind == KJiggle && (forceBite ? x.FailedBaits == 1 : x.FailedBaits is >= 1 and <= 2 && Rng.NextDouble() < SpotChance))
        {
            // Patient holder: change the stimulus once — the jump-spot (the classic bait against a big punish).
            x.HiddenUntil = Now + InterPoke();
            x.AfterHidden = After.Spot;
            x.Phase = Phase.Hidden;
            return;
        }
        x.HiddenUntil = Now + R(.1f, .3f);
        x.AfterHidden = After.Commit;
        x.Phase = Phase.Hidden;
    }
    public override void Update(float dt)
    {
        if (!active) { if (Now >= nextAt) StartTrial(); return; }
        if (Now >= expires) { Finish(false, 2); return; }
        foreach (var x in opponents)
        {
            if (!x.Alive) continue;
            // Vertical when a jump owns Feet.Y: gravity down, landing event, then the plan decides.
            if (!x.Body.Grounded)
            {
                x.JumpV -= JumpG * dt;
                var jp = x.Body.Feet;
                jp.Y += x.JumpV * dt;
                if (jp.Y <= 0f) { jp.Y = 0f; x.Body.Grounded = true; x.JumpV = 0f; Event("peek_land", trial, kind); }
                x.Body.Feet = jp;
                if (x.Body.Grounded && x.Phase == Phase.JumpSpot)
                {
                    // The spot landed: bite converts to the punish; a patient holder gets the commit.
                    if (Bit(x)) ConvertToPunish(x);
                    else
                    {
                        BaitFailed(x);
                        x.Bait = false;
                        x.Destination = x.CommitSide * R(4.4f, 5.2f);
                        x.Phase = Phase.Out;
                    }
                }
            }
            TrackExposure(x);
            switch (x.Phase)
            {
                case Phase.Approach:
                    if (MoveAxis(x, x.WaitZ, alongZ: true, kind == KShift ? WalkSpeed : RunSpeed, dt))
                    {
                        x.Phase = Phase.Wait;
                        if (kind == KCrouch) x.Body.Crouched = true;
                    }
                    continue;
                case Phase.Wait:
                    if (Now < x.StartAt) continue;
                    StartAct(x);
                    break;
                case Phase.Out:
                {
                    // The bait reads the holder: a shot during the exposure converts the poke into the punish.
                    if (x.Bait && !x.Converted && Bit(x)) { ConvertToPunish(x); break; }
                    float left = Mathf.Abs(x.Destination - x.Body.Feet.X);
                    if (x.Bait && left <= .06f) { x.HoldUntil = Now + BaitHold(); x.Phase = Phase.Hold; break; }
                    if (MoveAxis(x, x.Destination, alongZ: false, OutSpeed(x), dt))
                    {
                        if (x.Bait) { x.HoldUntil = Now + BaitHold(); x.Phase = Phase.Hold; }
                        else if (kind == KShort) { x.HoldUntil = Now + .18f; x.Phase = Phase.Hold; }
                        else
                        {
                            x.Phase = Phase.Duel;
                            Event("peek_commit", trial, kind);
                        }
                    }
                    break;
                }
                case Phase.Hold:
                    if (x.Bait && !x.Converted && Bit(x)) { ConvertToPunish(x); break; }
                    if (Now >= x.HoldUntil) AfterHold(x);
                    break;
                case Phase.Back:
                    if (x.Bait && !x.Converted && Bit(x)) { ConvertToPunish(x); break; }
                    if (MoveAxis(x, x.Destination, alongZ: false, RunSpeed, dt))
                    {
                        if (kind == KShort) { x.Escaped = true; Finish(false, 1); return; }
                        PlanNext(x);                        // bait: record the poke result behind cover
                    }
                    break;
                case Phase.Hidden:
                    // Poke/spot pauses stand still; commit/shuffle legs walk (audibly) behind cover first.
                    float hideAt = x.AfterHidden == After.Shuffle ? x.CommitSide * 1.3f : x.WaitSpot;
                    if (Mathf.Abs(x.Body.Feet.X - hideAt) > .002f) { MoveAxis(x, hideAt, alongZ: false, RunSpeed, dt); break; }
                    if (Now >= x.HiddenUntil) ResumeAfterHidden(x);
                    break;
                case Phase.JumpSpot:
                {
                    if (x.Bait && !x.Converted && Bit(x)) { ConvertToPunish(x); break; }
                    // The jump-spot arc: drift toward the angle on the way up, back to cover on the way down.
                    float dest = x.JumpV > 0 ? x.Side * 2.35f : x.Side * 1.3f;
                    MoveAxis(x, dest, alongZ: false, 3.4f, dt);
                    break;
                }
            }
            bool visible = G.LineOfSight(G.View.Eye, x.Body.Head);
            if (visible && x.SeenAt < 0)
            {
                x.SeenAt = Now; x.Body.SpawnTime = Now;
                Event("peek_visible", IdOf(x.Body), trial); TrackSeen(x.Body);
            }
            // Fire only from the duel phase (stopped at the commit); bait pokes never shoot.
            x.Brain.Enabled = x.Phase == Phase.Duel;
            x.Brain.Update(G, dt);
            if (!active) return; // player death is a synchronous callback from DamagePlayer
        }
    }
    // First-exposure telemetry (v2+): chest and outer-shoulder sample points; head-centre stays peek_visible (v1-comparable).
    void TrackExposure(Opponent x)
    {
        if (x.ChestExposed && x.ShoulderExposed) return;
        var head = x.Body.Head;
        var side = new Vector3(Mathf.Sign(head.X), 0, 0);
        if (!x.ChestExposed && G.LineOfSight(G.View.Eye, head + new Vector3(0, -.45f, 0)))
        {
            x.ChestExposed = true; Event("peek_expose", trial, 0);
        }
        if (!x.ShoulderExposed && G.LineOfSight(G.View.Eye, head + side * .30f + new Vector3(0, -.28f, 0)))
        {
            x.ShoulderExposed = true; Event("peek_expose", trial, 1);
        }
    }
    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        trialShots.Add(Now);
        if (!active) return float.PositiveInfinity;
        var hit = ShootBots(opponents.Where(x => x.Alive).Select(x => x.Body), o, d, out var zone, out bool killed, out float distance);
        if (hit == null) { Event("peek_miss", trial, kind); return float.PositiveInfinity; }
        var x = opponents.First(x => x.Body == hit);
        if (!x.FirstHit)
        {
            x.FirstHit = true;
            if (x.SeenAt >= 0) { firstHitMs.Add((Now - x.SeenAt) * 1000f); Event("peek_first_hit", IdOf(hit), firstHitMs[^1]); }
            if (lastKill >= 0) { transferMs.Add((Now - lastKill) * 1000f); Event("peek_transfer", trial, transferMs[^1]); }
        }
        if (killed)
        {
            Score += 100; lastKill = Now; Event("peek_kill", IdOf(hit), trial);
            if (opponents.All(x => !x.Alive)) Finish(true, 0);
        }
        return distance;
    }
    public override void OnPlayerDied() { Deaths++; Finish(false, 3); }
    public override string? Prompt => active ? null : "Hold either edge; the next peek is unpredictable";
    public override IEnumerable<string> HudLines()
    {
        yield return $"Won {wins.Sum()}/{attempts.Sum()} | lost {losses.Sum()}";
        yield return $"HP {G.Player.Hp:0} + shield {G.Player.Shield:0}";
    }
    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        for (int i = 0; i < 8; i++)
            if (attempts[i] > 0) yield return (names[i], $"won {wins[i]}/{attempts[i]}, lost {losses[i]}, unfinished {attempts[i] - wins[i] - losses[i]}");
        yield return ("Accuracy", $"{Hits}/{Shots} ({Accuracy * 100:0.0}%)");
        yield return ("Visible head to first hit", firstHitMs.Count > 0 ? $"{Median(firstHitMs):0} ms median ({firstHitMs.Count} hit targets only)" : "No hits");
        yield return ("Kill to next first hit", transferMs.Count > 0 ? $"{Median(transferMs):0} ms median ({transferMs.Count} transfers)" : "No transfers");
    }
    public override int Badge()
    {
        int won = wins.Sum(), finished = won + losses.Sum();
        return finished == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)won / finished);
    }
}
