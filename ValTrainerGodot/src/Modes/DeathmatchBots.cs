using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

public sealed partial class DeathmatchMode
{
    /// <summary>
    /// One Deathmatch bot "player" (persists across lives: name, kills, deaths). Each life gets a fresh
    /// <see cref="BotCharacter"/> and <see cref="BotBrain"/> (the brain duels YOU exactly like in the other bot drills).
    /// <para>Movement (docs/research/deathmatch-1.18.md, F-M1…F-M6): the player's acceleration and braking
    /// (<see cref="Mover.GroundAccel"/> 18 m/s², brake 38 / 24 m/s²); nav paths with wide lines off the walls
    /// (<see cref="BotNav.FindPath"/> with a clearance weight); roam / rumour / hunt goals on open floor; the crosshair on
    /// the next corner or where you were last seen while moving, so it often runs sideways to its aim (the legs follow);
    /// corner peeks while hunting, as in Mixed Peeks and scaled by tier (Rookie swings wide or shift-walks; Veteran adds
    /// jiggles; Elite / Pro shoulder-bait and re-peek); holds from spawn-point-like spots (open floor, wall behind, a view).</para>
    /// <para>Fights (you or another bot): strafes across the line to the enemy with Strafe Duels' model
    /// (<see cref="StrafePlanner"/>: Rookie long slow strafes … Pro tight ADAD, A-crouch-D and jiggles) inside the room it
    /// has on either side, and — Veteran and up, like VALORANT players — only fires once counter-strafed below the
    /// accuracy deadzone. Rookie / Regular run-and-gun at reduced accuracy (<see cref="BotBrain"/>).</para>
    /// Bots also duel each other with the same reaction / Fitts settle / burst hit model (free-for-all).
    /// </summary>
    sealed class Bot
    {
        public enum St { Roam, Hunt, Noise, Pack, Hold, Fight, Peek }

        // The player's horizontal model (Mover.cs), as in Strafe Duels / Mixed Peeks.
        const float Accel = Mover.GroundAccel, BrakeHigh = StrafePlanner.BrakeHigh, BrakeLow = StrafePlanner.BrakeLow, CounterBonus = StrafePlanner.CounterBonus;
        /// <summary>Path clearance weight: steps within 1 m of a wall cost up to 3× (<see cref="BotNav.FindPath"/>).</summary>
        const float PathClearance = 2f;

        readonly DeathmatchMode m;
        public readonly int Index;
        public readonly string Name;
        public BotCharacter? Body;
        BotBrain? brain;
        public int Kills, Deaths, Stuck;
        public float SpawnedAt, SeenByPlayerAt = -99f, Moved;
        public St State = St.Roam;
        /// <summary>Where this bot last died (its next spawn favours far from it).</summary>
        public Vector3? DeathSpot;

        float respawnAt = -1f;
        BotSkill skill = null!;
        Vector3 vel;
        List<Vector3>? path;
        int wp;
        float repathAt, holdUntil, protectedUntil;
        Vector3 holdDir, spawnFeet;
        Vector3? lkp, noise, holdSpot;
        float lkpAt = -99f, rumorAt, noiseAt = -99f, holdWalkUntil;
        int lastHp;
        Bot? attackerBot;
        bool hitByPlayer, sawPlayer, fightWasPlayer;
        // bot-vs-bot duel
        Bot? foe;
        float foeFireAt, foeScanAt;
        int foeShot;
        // fight movement (StrafePlanner along the axis across the line to the enemy)
        readonly StrafePlanner planner;
        bool strafing, mayShoot;
        Vector3 laneAxis, laneOrigin, lastLosFeet;
        float laneLo, laneHi, laneProbeAt, losSince, stopAgainAt, seenPlayerAt = -99f;
        // peeks: key states along a direction (out of cover / back), then the hunt goes on
        readonly Queue<(Vector3 Dir, float Dur, bool Crouch, float Speed)> peekPlan = new();
        (Vector3 Dir, float Dur, bool Crouch, float Speed) peekSeg;
        float peekSegEnd, nextPeekAt;
        List<Vector3>? peekedPath;
        int peekedWp = -1;
        // stuck detection
        float stuckAt;
        Vector3 stuckPos;
        int stuckRun;

        IGame G => m.G;
        float Now => m.Now;
        Random Rng => m.Rng;
        int Tier => m.Tier;
        BotNav Nav => m.nav;
        float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public Bot(DeathmatchMode mode, int index)
        {
            m = mode;
            Index = index;
            Name = $"Bot {index + 1}";
            planner = new StrafePlanner(mode.Tier, mode.Rng) { PatternPicked = p => m.fightPatterns[(int)p]++ };
        }

        public bool Alive => Body != null && GodotObject.IsInstanceValid(Body) && !Body.Dead;
        public bool Protected => Now < protectedUntil;
        /// <summary>Not busy: roaming, investigating, or holding an angle (gunfire can pull it away).</summary>
        public bool Idle => State is St.Roam or St.Noise or St.Hold;
        /// <summary>Dev HUD (--dm-botcam): state, speed, strafe key state, wall gap.</summary>
        public string DebugLine => !Alive ? $"{Name}: respawning" :
            $"{Name}: {State} {new Vector2(vel.X, vel.Z).Length():0.0} m/s" +
            (strafing ? $", strafe {(planner.Current.Dir < 0 ? "A" : planner.Current.Dir > 0 ? "D" : "stop")}{(planner.Crouch ? "+C" : "")}{(mayShoot ? " (may shoot)" : "")}" : "") +
            $", wall gap {Nav.ClearAt(Body!.Feet):0.0} m";

        public void Begin(BotCharacter body, BotBrain b, Vector3? playerFeet)
        {
            Body = body; brain = b; skill = b.Skill;
            SpawnedAt = Now; protectedUntil = Now + SpawnProtection; spawnFeet = body.Feet;
            lastHp = body.Hp; vel = Vector3.Zero;
            path = null; pending = null; foe = null; noise = null; lkp = null; holdSpot = null; attackerBot = null; hitByPlayer = false;
            holdUntil = 0; stuckRun = 0; strafing = false; mayShoot = false; sawPlayer = false; seenPlayerAt = -99f; stopAgainAt = 0;
            peekPlan.Clear(); peekedPath = null; peekedWp = -1;
            stuckAt = Now; stuckPos = body.Feet;
            respawnAt = -1f;
            SeenByPlayerAt = -99f;
            planner.Tier = Tier;
            State = St.Roam;
            // VALORANT: respawning reveals every enemy → the bot goes for the nearest one by path (you, or another bot),
            // never for a player who only just spawned.
            var field = Nav.DistanceField(body.Feet, 80f);
            float best = float.PositiveInfinity; Vector3? target = null; bool isPlayer = false;
            if (playerFeet is { } pf && !m.G.Player.Dead && Now - m.playerSpawnedAt > 3f)
            {
                float d = DistOn(field, pf);
                if (d < best) { best = d; target = pf; isPlayer = true; }
            }
            foreach (var o in m.bots)
            {
                if (o == this || !o.Alive || Now - o.SpawnedAt < 3f) continue;
                float d = DistOn(field, o.Body!.Feet);
                if (d < best) { best = d; target = o.Body.Feet; isPlayer = false; }
            }
            if (target is { } t)
            {
                if (isPlayer) { lkp = t; lkpAt = Now; State = St.Hunt; }
                else { noise = t; State = St.Noise; }
            }
            rumorAt = Now + R(6f, 12f);
            repathAt = Now + R(0f, 0.3f);
        }

        float DistOn(float[] field, Vector3 p)
        {
            int n = Nav.At(p, Collision.StepHeight);
            if (n < 0) n = Nav.Nearest(p);
            return n >= 0 ? field[n] : float.PositiveInfinity;
        }

        public void OnDeath(float respawn)
        {
            Deaths++;
            respawnAt = respawn;
            if (Body != null && GodotObject.IsInstanceValid(Body)) DeathSpot = Body.Feet;
            Body = null; brain = null; foe = null; path = null; pending = null;
        }

        /// <summary>New combat skill for this life (finisher); later lives get it from <see cref="DeathmatchMode.BotSkillNow"/>.</summary>
        public void SetSkill(BotSkill s) { skill = s; if (brain != null) brain.Skill = s; }

        public void ForgetPlayer() { lkp = null; if (State == St.Hunt) { State = St.Roam; Replan(); } }

        /// <summary>Your spawn protection ended: if this bot was tracking you through it, it reacts and settles its aim
        /// afresh (no shot landing the frame protection ends).</summary>
        public void Reacquire()
        {
            if (brain == null || !Alive) return;
            brain.Enabled = false; brain.Update(G, 0f); // disabled: drops the sighting, reaction and shot timers
            brain.Enabled = true;
        }

        /// <summary>Gunfire heard at a point (the player's if <paramref name="player"/>). Only idle bots react: a hunting bot
        /// keeps its goal; your shots pull an idle bot half the time, a bot fight a third of the time.</summary>
        public void Hear(Vector3 at, bool player)
        {
            if (!Idle) return;
            if (player)
            {
                if (Rng.NextDouble() >= 0.5) return;
                lkp = at; lkpAt = Now; holdSpot = null;
                State = St.Hunt; Replan();
            }
            else if (State == St.Roam && Rng.NextDouble() < 0.35)
            {
                noise = at; State = St.Noise; Replan();
            }
        }

        /// <summary>Took damage from <paramref name="bot"/> (null = the player).</summary>
        public void Hurt(Bot? bot)
        {
            if (bot == null) hitByPlayer = true; else attackerBot = bot;
        }

        // ---------------------------------------------------------------- frame

        public void Update(float dt)
        {
            if (!Alive)
            {
                if (Body != null && (!GodotObject.IsInstanceValid(Body) || Body.Dead)) { Body = null; brain = null; }
                if (Body == null && respawnAt >= 0 && Now >= respawnAt) m.SpawnLife(this, m.PlayerAlive ? m.PlayerFeet : null);
                return;
            }
            var b = Body!;

            // Damage reaction: turn toward whoever shot us.
            if (b.Hp < lastHp)
            {
                if (hitByPlayer && !brain!.SeesPlayer && m.PlayerAlive)
                {
                    lkp = m.PlayerFeet; lkpAt = Now;
                    if (foe == null) { Face(G.View.Eye - b.Head, snap: 0.6f); if (State is St.Roam or St.Noise or St.Hold or St.Peek) { State = St.Hunt; holdSpot = null; peekPlan.Clear(); Replan(); } }
                }
                else if (attackerBot != null && attackerBot.Alive && foe == null && !brain!.SeesPlayer)
                {
                    Face(attackerBot.Body!.Head - b.Head, snap: 0.6f);
                    foeScanAt = 0;
                }
            }
            hitByPlayer = false; attackerBot = null;
            lastHp = b.Hp;
            // Spawn protection (both ways): ends when the bot engages or walks off its spawn.
            if (Protected && Flat(b.Feet, spawnFeet) > ProtectMoveRadius) protectedUntil = 0;

            brain!.Update(G, dt);
            bool fightPlayer = brain.SeesPlayer;
            if (fightPlayer)
            {
                if (!sawPlayer) losSince = Now;
                if (!sawPlayer && Now - seenPlayerAt > 2.5f)
                {
                    // A new engagement: where do fights start, against a wall? (dev log for the close ones)
                    float gap = Nav.ClearAt(b.Feet);
                    m.firstSightGaps.Add(gap);
                    if (DevLog && gap < 0.5f) GD.Print($"[dm] {Name} saw you from {Fmt(b.Feet)}, {gap:0.00} m off a wall ({State}, {new Vector2(vel.X, vel.Z).Length():0.0} m/s)");
                }
                sawPlayer = true; seenPlayerAt = Now;
                lkp = m.PlayerFeet; lkpAt = Now; foe = null; lastLosFeet = b.Feet;
                protectedUntil = 0; // engaging ends spawn protection
                State = St.Fight; peekPlan.Clear(); holdSpot = null;
                if (Now - noiseAt > 0.6f) { noiseAt = Now; m.Noise(b.Feet, this, HearRange); } // the fight is audible
            }
            else { sawPlayer = false; UpdateFoe(); }
            bool fighting = fightPlayer || foe != null;
            if (fighting) fightWasPlayer = fightPlayer;
            if (!fighting && State == St.Fight) LostSight();

            // ---- desired velocity ----
            Vector3 want = Vector3.Zero;
            float speed = Mover.RunSpeed;
            bool crouch = false;
            if (fighting) crouch = FightMove(b, fightPlayer, dt);
            else
            {
                if (strafing) strafing = false;
                if (State == St.Hold && holdSpot == null && Now >= holdUntil) { State = lkp != null && Now - lkpAt < 10f ? St.Hunt : St.Roam; Replan(); }
                if (State == St.Hold && holdSpot != null && Now >= holdWalkUntil) { holdSpot = null; holdUntil = Now; } // couldn't reach the spot: hold here
                if (State == St.Peek) (want, speed, crouch) = PeekStep();
                else if (State != St.Hold || holdSpot != null)
                {
                    if (path == null || wp >= path.Count) PlanNext();
                    if (path != null && wp < path.Count)
                    {
                        var to = path[wp] - b.Feet; to.Y = 0;
                        if (to.Length() < 0.45f)
                        {
                            if (!TryCornerPeek()) { wp++; if (wp >= path.Count) Arrived(); }
                        }
                        else want = to.Normalized();
                        // Veteran+ walk (silently) the last metres toward where you were.
                        if (State == St.Hunt && Tier >= 2 && lkp is { } k && b.Feet.DistanceTo(k) < 9f) speed = Mover.WalkSpeed;
                        if (Tier == 0) speed = 4.6f; // Rookies jog
                    }
                }
                Accelerate(want, speed, dt);
            }
            b.Crouched = crouch;
            // Spawn protection: nobody shoots a protected player; Veteran+ fire only once counter-strafed (as players do).
            brain.HoldFire = m.PlayerProtected || (fightPlayer && !skill.RunAndGun && !mayShoot);

            var feet = b.Feet;
            var v = vel;
            var before = feet;
            Collision.MoveAndSlide(ref feet, ref v, v * dt, G.Solid);
            feet.Y = Collision.Ground(feet, G.Solid);
            vel = v;
            b.Feet = feet;
            b.Velocity = vel;
            float stepLen = new Vector2(feet.X - before.X, feet.Z - before.Z).Length();
            Moved += stepLen;
            if (fighting) { m.fightTime += dt; if (stepLen > 0.5f * dt) m.fightMovingTime += dt; }
            if (Collision.Overlaps(feet, G.Solid, 0.2f)) m.inWallFrames++;

            // ---- look ----
            if (!fightPlayer)
            {
                Vector3 look;
                var eyeOf = new Vector3(0, PlayerView.EyeHeight, 0);
                if (foe != null && foe.Alive) look = foe.Body!.Head - b.Head;
                else if (State == St.Hold) look = holdSpot != null && vel.LengthSquared() > 0.5f ? new Vector3(vel.X, 0, vel.Z) : holdDir;
                else if (State == St.Peek && lkp is { } pk) look = pk + eyeOf - b.Head;          // pre-aimed peek
                else if (Tier >= 2 && lkp is { } k && Now - lkpAt < 12f && k.DistanceTo(feet) < 25f)
                    look = k + eyeOf - b.Head;                                                  // crosshair on where you were
                else if (Tier >= 1 && CornerAhead(feet) is { } c) look = c + eyeOf - b.Head;   // check the next corner
                else if (vel.LengthSquared() > 0.5f) look = new Vector3(vel.X, 0, vel.Z);
                else look = brain.HeldDir;
                Turn(look, dt);
            }

            // ---- stuck: has a path but isn't getting anywhere ----
            if (Now - stuckAt > 1.2f)
            {
                bool tryingToMove = !fighting && State is not (St.Peek or St.Hold) || (State == St.Hold && holdSpot != null);
                tryingToMove &= path != null && Now - pathSince >= 1.2f;
                if (tryingToMove && feet.DistanceTo(stuckPos) < 0.3f)
                {
                    Stuck++; stuckRun++;
                    if (DevLog && Stuck <= 3) GD.Print($"[dm] {Name} stuck at {feet} ({State}, wp {wp}/{path!.Count} next {(wp < path.Count ? path[wp] : Vector3.Zero)}, vel {vel.Length():0.0}, want {want}, speed {speed}, sees {brain.SeesPlayer})");
                    Replan(); repathAt = Now;
                    if (stuckRun >= 3) { State = St.Roam; lkp = null; noise = null; holdSpot = null; stuckRun = 0; }
                }
                else stuckRun = 0;
                stuckAt = Now; stuckPos = feet;
            }

            if (Now >= rumorAt) Rumor();
        }

        /// <summary>Drop the current path (and any search in flight) so the next frame plans a fresh one.</summary>
        void Replan() { path = null; pending = null; }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

        /// <summary>The player's horizontal acceleration: 18 m/s² toward the wanted velocity, two-phase braking (38 / 24
        /// m/s²) of whatever doesn't point that way, a 6% quicker counter-strafe through zero.</summary>
        void Accelerate(Vector3 want, float speed, float dt)
        {
            float brake = (vel.Length() > Mover.RunSpeed * 0.25f ? BrakeHigh : BrakeLow) * dt;
            if (want.LengthSquared() < 1e-4f) { vel = vel.MoveToward(Vector3.Zero, brake); return; }
            var dir = want.Normalized();
            float along = vel.Dot(dir);
            var side = (vel - dir * along).MoveToward(Vector3.Zero, brake * CounterBonus);
            if (along < 0) along = Mathf.Min(0, along + brake * CounterBonus);
            else if (along > speed) along = Mathf.Max(speed, along - brake);
            else along = Mathf.Min(speed, along + Accel * dt);
            vel = dir * along + side;
        }

        // ---------------------------------------------------------------- fights

        /// <summary>Strafe across the line to the enemy with <see cref="StrafePlanner"/>, inside the room the bot has on
        /// either side (re-probed every 0.2 s). Returns whether it crouches; sets <see cref="mayShoot"/>.</summary>
        bool FightMove(BotCharacter b, bool fightPlayer, float dt)
        {
            var target = fightPlayer ? m.PlayerFeet : foe!.Body!.Feet;
            var to = target - b.Feet; to.Y = 0;
            if (to.LengthSquared() < 0.01f) to = brain!.HeldDir with { Y = 0 };
            var axis = new Vector3(-to.Z, 0, to.X).Normalized();
            if (!strafing || Now >= laneProbeAt) ProbeLane(b.Feet, axis);
            if (!strafing)
            {
                strafing = true;
                planner.Tier = Tier;
                planner.Begin(Now, 0f, laneLo, laneHi, vel.Dot(laneAxis));
            }
            if (laneHi - laneLo < 1.2f)
            {
                // Boxed in (a doorway, a crate corner): stand still and shoot.
                Accelerate(Vector3.Zero, 0, dt);
                mayShoot = vel.Length() <= Mover.AccurateSpeed;
                return false;
            }
            // Aim settled (reaction done): counter-strafe into the next stop instead of finishing the strafe — then strafe
            // again for a while before the next early stop (Rookie lingers, Pro re-stops quickly).
            bool ready = fightPlayer ? Now - losSince >= skill.ReactMs / 1000f : Now >= foeFireAt;
            if (ready && Now >= stopAgainAt && planner.StopSoon(Now)) stopAgainAt = Now + Difficulty.T(Tier, 1.4f, 1.2f, 1.0f, 0.8f, 0.6f);
            float pos = (b.Feet - laneOrigin).Dot(laneAxis);
            planner.V = vel.Dot(laneAxis); // walls and the last frame's motion
            float v = planner.Step(Now, dt, pos, laneLo, laneHi);
            var rest = vel - laneAxis * vel.Dot(laneAxis);
            vel = laneAxis * v + rest.MoveToward(Vector3.Zero, BrakeHigh * dt);
            mayShoot = planner.MayShoot;
            return planner.Crouch;
        }

        /// <summary>How far the bot can strafe left / right of where it stands (0.8–3.5 m, straight walkable lines that keep
        /// the shoulders 12 cm off walls, no drop-offs).</summary>
        void ProbeLane(Vector3 feet, Vector3 axis)
        {
            laneAxis = axis; laneOrigin = feet;
            laneHi = Free(feet, axis);
            laneLo = -Free(feet, -axis);
            laneProbeAt = Now + 0.2f;
        }

        float Free(Vector3 feet, Vector3 dir)
        {
            foreach (float d in new[] { 3.5f, 2.5f, 1.5f, 0.8f })
            {
                var q = feet + dir * d;
                int n = Nav.At(q, Collision.StepHeight);
                if (n >= 0 && Nav.Straight(feet, q with { Y = Nav.Pos[n].Y }, Collision.PlayerRadius + 0.12f)) return d;
            }
            return 0f;
        }

        /// <summary>Lost sight of whoever it was fighting: re-peek (Regular+), hold the angle (Elite+), or chase.</summary>
        void LostSight()
        {
            strafing = false;
            float repeek = Difficulty.T(Tier, 0f, .3f, .4f, .45f, .5f);
            if (fightWasPlayer && lkp != null && Flat(Body!.Feet, lastLosFeet) > 0.3f && Rng.NextDouble() < repeek)
            {
                // Step back into the angle it just lost, after a short pause (Mixed Peeks' repeek).
                var d = lastLosFeet - Body.Feet; d.Y = 0;
                float dist = d.Length() + 0.8f, sp = PeekSpeed;
                peekPlan.Clear();
                peekPlan.Enqueue((Vector3.Zero, InterPoke(.4f, 1.0f), false, 0f));
                peekPlan.Enqueue((d / d.Length(), dist / sp + 0.1f, false, sp));
                StartPeekPlan();
                return;
            }
            if (Tier >= 3 && Rng.NextDouble() < 0.4) { State = St.Hold; holdSpot = null; holdUntil = Now + R(0.5f, 1.4f); holdDir = brain!.HeldDir; }
            else State = lkp != null ? St.Hunt : St.Roam;
            Replan();
        }

        // ---------------------------------------------------------------- peeks (Mixed Peeks' kinds, tier-scaled)

        float PeekSpeed => Difficulty.T(Tier, 2.97f, 4.2f, 5.4f, 5.4f, 5.4f);
        /// <summary>Pause between pokes: Pro re-peeks fastest.</summary>
        float InterPoke(float a, float b) => R(a, b) * Difficulty.T(Tier, 1.3f, 1.15f, 1f, 0.85f, 0.7f);

        /// <summary>Hunting and arriving at a path corner that hides where you were (the angle opens past it): stop, then
        /// peek it — Rookie / Regular swing wide, shift-walk or crouch-peek; Veteran+ also jiggle; Elite+ shoulder-bait.
        /// Returns true when a peek started.</summary>
        bool TryCornerPeek()
        {
            if (State != St.Hunt || lkp is not { } k || path == null || wp + 1 >= path.Count || Now < nextPeekAt) return false;
            if (ReferenceEquals(peekedPath, path) && peekedWp == wp) return false;
            peekedPath = path; peekedWp = wp;
            var corner = path[wp];
            if (k.DistanceTo(corner) > 30f || Rng.NextDouble() >= Difficulty.T(Tier, .35f, .45f, .6f, .7f, .8f)) return false;
            var outDir = path[wp + 1] - corner; outDir.Y = 0;
            if (outDir.LengthSquared() < 0.25f) return false;
            outDir = outDir.Normalized();
            var eye = new Vector3(0, PlayerView.EyeHeight, 0);
            var kEye = k + eye;
            // A real corner: hidden from here, in view a couple of metres past it.
            if (G.LineOfSight(corner + eye, kEye)) return false;
            var past = corner + outDir * 2f;
            if (!Nav.Straight(corner, past) || !G.LineOfSight(past + eye, kEye)) return false;

            peekPlan.Clear();
            float sp = PeekSpeed;
            peekPlan.Enqueue((Vector3.Zero, R(0.12f, 0.3f), false, 0f)); // counter-strafe stop at the corner, crosshair on the angle
            int[] kinds = Tier switch { <= 1 => new[] { 0, 1, 2, 3 }, 2 => new[] { 0, 1, 2, 3, 4 }, _ => new[] { 0, 1, 2, 3, 4, 5, 4 } };
            switch (kinds[Rng.Next(kinds.Length)])
            {
                case 0: // wide swing
                    peekPlan.Enqueue((outDir, R(0.7f, 1.0f), false, sp));
                    break;
                case 1: // short peek: out, a look, back, then commit
                    peekPlan.Enqueue((outDir, R(0.25f, 0.35f), false, sp));
                    peekPlan.Enqueue((Vector3.Zero, 0.18f, false, 0f));
                    peekPlan.Enqueue((-outDir, R(0.25f, 0.35f), false, sp));
                    peekPlan.Enqueue((Vector3.Zero, InterPoke(.3f, .6f), false, 0f));
                    peekPlan.Enqueue((outDir, R(0.7f, 1.0f), false, sp));
                    break;
                case 2: // crouch peek
                    peekPlan.Enqueue((outDir, R(0.9f, 1.2f), true, Mover.CrouchSpeed));
                    break;
                case 3: // shift-walk (silent)
                    peekPlan.Enqueue((outDir, R(1.0f, 1.4f), false, Mover.WalkSpeed));
                    break;
                case 4: // jiggle: 2–4 quick taps out and back, then commit wide
                {
                    int n = 2 + Rng.Next(3);
                    for (int i = 0; i < n; i++)
                    {
                        float t = R(0.10f, 0.16f);
                        peekPlan.Enqueue((outDir, t, false, sp));
                        peekPlan.Enqueue((-outDir, t, false, sp));
                    }
                    peekPlan.Enqueue((Vector3.Zero, InterPoke(.15f, .5f), false, 0f));
                    peekPlan.Enqueue((outDir, R(0.7f, 1.0f), false, sp));
                    break;
                }
                default: // shoulder bait: show a shoulder, hold, back, then commit
                {
                    int n = 1 + Rng.Next(2);
                    for (int i = 0; i < n; i++)
                    {
                        float t = R(0.16f, 0.24f);
                        peekPlan.Enqueue((outDir, t, false, 3.6f));
                        peekPlan.Enqueue((Vector3.Zero, R(0.10f, 0.20f), false, 0f));
                        peekPlan.Enqueue((-outDir, t, false, 3.6f));
                        peekPlan.Enqueue((Vector3.Zero, InterPoke(.25f, .6f), false, 0f));
                    }
                    peekPlan.Enqueue((outDir, R(0.7f, 1.0f), false, sp));
                    break;
                }
            }
            nextPeekAt = Now + 4f; // one corner at a time (the replanned path may still turn at it)
            StartPeekPlan();
            return true;
        }

        void StartPeekPlan()
        {
            State = St.Peek;
            peekSeg = peekPlan.Dequeue();
            peekSegEnd = Now + peekSeg.Dur;
        }

        /// <summary>One frame of a peek: the current key state's direction and speed. When the plan runs out without a
        /// sighting, the hunt goes on from where it stands.</summary>
        (Vector3 Want, float Speed, bool Crouch) PeekStep()
        {
            while (Now >= peekSegEnd)
            {
                if (peekPlan.Count == 0)
                {
                    State = lkp != null ? St.Hunt : St.Roam;
                    Replan();
                    return (Vector3.Zero, 0f, false);
                }
                peekSeg = peekPlan.Dequeue();
                peekSegEnd = Now + peekSeg.Dur;
            }
            return (peekSeg.Dir, peekSeg.Speed, peekSeg.Crouch);
        }

        // ---------------------------------------------------------------- look

        void Face(Vector3 dir, float snap)
        {
            var h = new Vector3(dir.X, 0, dir.Z);
            if (h.LengthSquared() < 1e-4f) return;
            float target = Mathf.RadToDeg(Mathf.Atan2(h.X, -h.Z));
            Body!.FacingYaw = Mathf.LerpAngle(Mathf.DegToRad(Body.FacingYaw), Mathf.DegToRad(target), snap) * 180f / Mathf.Pi;
            brain!.HeldDir = YawDir(Body.FacingYaw);
        }

        static Vector3 YawDir(float yaw)
        {
            float r = Mathf.DegToRad(yaw);
            return new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r));
        }

        void Turn(Vector3 look, float dt)
        {
            var h = new Vector3(look.X, 0, look.Z);
            if (h.LengthSquared() < 1e-4f) return;
            float target = Mathf.RadToDeg(Mathf.Atan2(h.X, -h.Z));
            float rate = Difficulty.T(Tier, 300f, 420f, 560f, 720f, 900f);
            float diff = Mathf.Wrap(target - Body!.FacingYaw, -180f, 180f);
            Body.FacingYaw += Mathf.Clamp(diff, -rate * dt, rate * dt);
            var d = look.Normalized();
            // Held direction = facing yaw with the look's pitch (pre-aim height matters for the brain's placement error).
            var yd = YawDir(Body.FacingYaw);
            float pitchY = Mathf.Clamp(d.Y, -0.5f, 0.5f);
            brain!.HeldDir = (yd * Mathf.Sqrt(1 - pitchY * pitchY) + new Vector3(0, pitchY, 0)).Normalized();
        }

        /// <summary>The angle around the next corner of the path (the first turn of more than 30° within 12 m): a point
        /// a few metres down the next leg, where an enemy would appear. Null on a straight run.</summary>
        Vector3? CornerAhead(Vector3 feet)
        {
            if (path == null || wp >= path.Count) return null;
            var prev = feet;
            for (int k = wp; k + 1 < path.Count && k < wp + 4; k++)
            {
                var a = path[k] - prev; a.Y = 0;
                var c = path[k + 1] - path[k]; c.Y = 0;
                if (Flat(feet, path[k]) > 12f) return null;
                if (a.LengthSquared() > 0.04f && c.LengthSquared() > 0.04f && a.Normalized().Dot(c.Normalized()) < 0.866f)
                    return path[k] + c.Normalized() * Mathf.Min(4f, c.Length());
                prev = path[k];
            }
            return null;
        }

        // ---------------------------------------------------------------- navigation

        Task<List<Vector3>?>? pending;
        St pendingState;
        Vector3 goal;
        float pathSince;

        void PlanNext()
        {
            if (pending != null)
            {
                if (!pending.IsCompleted) return;
                var res = pending.IsCompletedSuccessfully ? pending.Result : null;
                if (pending.IsFaulted) GD.PushWarning($"[dm] path failed: {pending.Exception?.GetBaseException().Message}");
                pending = null;
                if (pendingState != State) return; // plans changed while it was searching: plan again
                path = res; wp = 0; pathSince = Now;
                // The bot kept sliding while the path was searched: make sure the first leg is still walkable.
                if (path != null && path.Count > 0 && !Nav.Straight(Body!.Feet, path[0]))
                {
                    int n = Nav.Nearest(Body.Feet);
                    if (n >= 0) path.Insert(0, Nav.Pos[n]);
                }
                if (path == null || path.Count == 0)
                {
                    path = null;
                    if (State == St.Hold) { holdSpot = null; holdUntil = Now + R(1.2f, 3.5f); }
                    else if (State != St.Roam) { lkp = null; noise = null; State = St.Roam; }
                }
                return;
            }
            if (Now < repathAt || m.pathBudget <= 0) return;
            var feet = Body!.Feet;
            // Goals are open floor (not against a wall), on the near side of walls.
            if (State == St.Hold && holdSpot is { } hs) goal = hs;
            // Hurt and a health pack nearby: go grab it.
            else if (Body.Hp <= 100 && m.NearestPack(feet, 16f) is { } pack) { State = St.Pack; goal = pack; }
            else if (lkp is { } k && Now - lkpAt < 12f && State != St.Noise) { State = St.Hunt; goal = Nav.OpenNear(k, 2.5f, 0.8f, Rng); }
            else if (noise is { } nz) { State = St.Noise; goal = Nav.OpenNear(nz, 3f, 0.8f, Rng); }
            else
            {
                State = St.Roam;
                goal = feet;
                for (int i = 0; i < 8; i++)
                {
                    var c = Nav.RandomOpen(Rng, 1.0f);
                    float d = c.DistanceTo(feet);
                    goal = c;
                    if (d is > 8f and < 35f) break;
                }
            }
            m.pathBudget--;
            path = null;
            pending = Nav.FindPathAsync(feet, goal, PathClearance);
            pendingState = State;
            repathAt = Now + 0.6f;
        }

        void Arrived()
        {
            path = null;
            var prev = State;
            if (State == St.Hold)
            {
                // At the hold spot: hold the angle for a while.
                holdSpot = null;
                holdUntil = Now + R(1.2f, 3.5f);
                return;
            }
            if (State == St.Hunt) lkp = null;
            if (State == St.Noise) noise = null;
            State = St.Roam;
            // Sometimes stop and hold an angle (more often at higher tiers), from a sensible spot: open floor with a wall
            // behind and a view (a spawn point within 8 m), else right here if it's off the walls.
            if (prev != St.Pack && Rng.NextDouble() < Difficulty.T(Tier, 0.15f, 0.22f, 0.3f, 0.38f, 0.45f))
            {
                var feet = Body!.Feet;
                if (m.spawns.HoldSpotNear(feet, 8f, Rng) is { } spot)
                {
                    State = St.Hold;
                    holdSpot = spot.Feet;
                    holdDir = YawDir(spot.Yaw);
                    holdUntil = float.MaxValue; // starts on arrival
                    holdWalkUntil = Now + 5f;
                }
                else if (Nav.ClearAt(feet) >= 0.6f)
                {
                    State = St.Hold;
                    holdUntil = Now + R(1.2f, 3.5f);
                    holdDir = YawDir(m.OpenYaw(feet));
                }
            }
        }

        /// <summary>DM players drift toward the action: every few seconds an idle bot "hears" roughly where someone is —
        /// any combatant, weighted by 1 / walking distance, you counting double; never someone who only just spawned. The
        /// goal is open floor within 5 m of them, on their side of the walls.</summary>
        void Rumor()
        {
            rumorAt = Now + Difficulty.T(Tier, 14f, 12f, 10f, 9f, 8f) * R(0.7f, 1.3f);
            if (State is not (St.Roam or St.Noise) || Rng.NextDouble() >= Difficulty.T(Tier, 0.45f, 0.55f, 0.65f, 0.7f, 0.75f)) return;
            var field = Nav.DistanceField(Body!.Feet, 60f);
            var cands = new List<(Vector3 Feet, float W, bool Player)>();
            if (m.PlayerAlive && Now - m.playerSpawnedAt > 4f)
            {
                float d = DistOn(field, m.PlayerFeet);
                if (!float.IsPositiveInfinity(d)) cands.Add((m.PlayerFeet, 2f / Mathf.Max(5f, d), true));
            }
            foreach (var o in m.bots)
            {
                if (o == this || !o.Alive || Now - o.SpawnedAt < 4f) continue;
                float d = DistOn(field, o.Body!.Feet);
                if (!float.IsPositiveInfinity(d)) cands.Add((o.Body.Feet, 1f / Mathf.Max(5f, d), false));
            }
            if (cands.Count == 0) return;
            float r = (float)Rng.NextDouble() * cands.Sum(c => c.W);
            var pick = cands[^1];
            foreach (var c in cands) { if (r < c.W) { pick = c; break; } r -= c.W; }
            var spot = Nav.OpenNear(pick.Feet, 5f, 0.8f, Rng);
            if (pick.Player) { lkp = spot; lkpAt = Now; State = St.Hunt; }
            else { noise = spot; State = St.Noise; }
            Replan();
        }

        // ---------------------------------------------------------------- bot vs bot

        void UpdateFoe()
        {
            var b = Body!;
            if (foe != null)
            {
                if (!foe.Alive || foe.Protected || b.Head.DistanceTo(foe.Body!.Head) > 45f || !G.LineOfSight(b.Head, foe.Body.Head)) { foe = null; return; }
                State = St.Fight;
                var to = foe.Body.Head - b.Head;
                b.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z));
                brain!.HeldDir = to.Normalized();
                b.AimTarget = foe.Body.Head;
                int guard = 0;
                while (foe != null && Now >= foeFireAt && guard++ < 3) FireAtFoe();
                return;
            }
            b.AimTarget = null;
            if (Now < foeScanAt) return;
            foeScanAt = Now + 0.4f;
            // Bots are here to fight YOU: while chasing a fresh lead on you they sometimes ignore each other.
            if (State == St.Hunt && Now - lkpAt < 4f && Rng.NextDouble() < 0.25) return;
            foreach (var o in m.bots)
            {
                if (o == this || !o.Alive || o.Protected) continue;
                var to = o.Body!.Head - b.Head;
                float dist = to.Length();
                if (dist > 30f) continue;
                float ang = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(brain!.HeldDir.Dot(to / dist), -1, 1)));
                if (ang > brain.HalfFovDeg || !G.LineOfSight(b.Head, o.Body.Head)) continue;
                // Same model as BotBrain.OnLosGained: reaction, then a Fitts' law settle from the crosshair misplacement.
                float react = Mathf.Max(120f, Gauss(skill.ReactMs, skill.ReactSd)) / 1000f;
                float err = Mathf.Sqrt(ang * ang + Mathf.Pow(Gauss(0, skill.XhairErrDeg), 2));
                float w = Mathf.RadToDeg(2f * Mathf.Atan(0.14f / Mathf.Max(1f, dist)));
                float settle = err <= w / 2 ? 0 : skill.MsPerBit * Mathf.Log(err / w + 1f) / Mathf.Log(2f) / 1000f;
                foe = o; foeShot = 0; foeFireAt = Now + react + settle;
                protectedUntil = 0;
                State = St.Fight; peekPlan.Clear(); holdSpot = null;
                return;
            }
        }

        void FireAtFoe()
        {
            var b = Body!; var o = foe!;
            var w = brain?.Weapon ?? Weapons.Vandal;
            bool moving = vel.Length() > 1.5f;
            int s = foeShot % skill.Burst;
            float p = Mathf.Max(skill.PHit1 * 0.35f, skill.PHit1 * Mathf.Pow(skill.Decay, s));
            // Same gate as against you: Veteran+ shoot only once counter-strafed.
            if (!skill.RunAndGun && (moving || (strafing && !mayShoot))) { foeFireAt += 0.05f; return; }
            if (moving) p *= 0.25f;
            var target = o.Body!.Head + Vector3.Down * 0.35f;
            float dist = (target - b.Head).Length();
            if (Rng.NextDouble() < p)
            {
                int zone = Rng.NextDouble() < skill.PHead1 * (s == 0 ? 1f : 0.6f) ? 0 : (Rng.NextDouble() < 0.08 ? 2 : 1);
                o.Body.Hp -= (int)Mathf.Round(w.Damage(zone, dist));
                o.Body.OnHit((HitZone)zone);
                o.Hurt(this);
                if (o.Body.Dead)
                {
                    o.Body.Die((o.Body.Head - b.Head).Normalized());
                    m.OnBotKilled(o, this, zone == 0);
                    foe = null;
                }
            }
            else target += new Vector3(R(-0.6f, 0.6f), R(-0.4f, 0.5f), R(-0.6f, 0.6f));
            b.ShootFx(target);
            G.SoundAt(BotBrain.ShotSoundFor(w), b.Head, maxRange: 60f, floor: 0.6f);
            m.Noise(b.Feet, this, HearRange);
            foeShot++;
            foeFireAt += w.Interval;
            if (foeShot % skill.Burst == 0) foeFireAt += skill.GapMs / 1000f - w.Interval;
        }

        float Gauss(float mean, float sd)
        {
            double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
            return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }
}
