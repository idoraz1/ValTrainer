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
    /// Movement: run along nav-grid paths (roam / hunt your last known position / investigate gunfire / grab a health
    /// pack), pre-aim where you were last seen (Veteran+), sometimes stop to hold an angle, stop to shoot (Veteran+
    /// counter-strafe dead; Rookie/Regular slide to a stop and run-and-gun), Elite+ jiggle A-D between bursts.
    /// Bots also duel each other with the same reaction / Fitts settle / burst hit model (free-for-all).
    /// </summary>
    sealed class Bot
    {
        public enum St { Roam, Hunt, Noise, Pack, Hold, Fight }

        readonly DeathmatchMode m;
        public readonly int Index;
        public readonly string Name;
        public BotCharacter? Body;
        BotBrain? brain;
        public int Kills, Deaths, Stuck;
        public float SpawnedAt, SeenByPlayerAt = -99f, Moved;
        public St State = St.Roam;

        float respawnAt = -1f;
        BotSkill skill = null!;
        Vector3 vel;
        List<Vector3>? path;
        int wp;
        float repathAt, holdUntil, protectedUntil;
        Vector3 holdDir;
        Vector3? lkp, noise;
        float lkpAt = -99f, rumorAt, noiseAt = -99f;
        int lastHp;
        Bot? attackerBot;
        bool hitByPlayer;
        // bot-vs-bot duel
        Bot? foe;
        float foeFireAt, foeScanAt;
        int foeShot;
        // engagement movement
        float strafeUntil, nextStrafeAt;
        float strafeSign = 1f;
        // stuck detection
        float stuckAt;
        Vector3 stuckPos;
        int stuckRun;

        IGame G => m.G;
        float Now => m.Now;
        Random Rng => m.Rng;
        int Tier => m.Tier;
        float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public Bot(DeathmatchMode mode, int index)
        {
            m = mode;
            Index = index;
            Name = $"Bot {index + 1}";
        }

        public bool Alive => Body != null && GodotObject.IsInstanceValid(Body) && !Body.Dead;
        public bool Protected => Now < protectedUntil;

        public void Begin(BotCharacter body, BotBrain b, Vector3? playerFeet)
        {
            Body = body; brain = b; skill = b.Skill;
            SpawnedAt = Now; protectedUntil = Now + SpawnProtection;
            lastHp = body.Hp; vel = Vector3.Zero;
            path = null; pending = null; foe = null; noise = null; attackerBot = null; hitByPlayer = false;
            holdUntil = 0; strafeUntil = 0; nextStrafeAt = 0; stuckRun = 0;
            stuckAt = Now; stuckPos = body.Feet;
            respawnAt = -1f;
            SeenByPlayerAt = -99f;
            // VALORANT: respawning reveals every enemy for a moment → the bot knows where you are.
            if (playerFeet is { } pf && !m.G.Player.Dead && Now - m.playerSpawnedAt > 3f) { lkp = pf; lkpAt = Now; State = St.Hunt; }
            else { lkp = null; State = St.Roam; }
            rumorAt = Now + R(6f, 12f);
            repathAt = Now + R(0f, 0.3f);
        }

        public void OnDeath(float respawn)
        {
            Deaths++;
            respawnAt = respawn;
            Body = null; brain = null; foe = null; path = null; pending = null;
        }

        public void ForgetPlayer() { lkp = null; if (State == St.Hunt) { State = St.Roam; Replan(); } }

        /// <summary>Gunfire heard at a point (the player's if <paramref name="player"/>).</summary>
        public void Hear(Vector3 at, bool player)
        {
            if (State is St.Fight or St.Pack) return;
            if (player)
            {
                lkp = at; lkpAt = Now;
                // Re-plan only when this changes where we're going (not on every shot you fire).
                if (State != St.Hunt || (path == null && pending == null) || goal.DistanceTo(at) > 5f) { State = St.Hunt; Replan(); }
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
                    if (foe == null) { Face(G.View.Eye - b.Head, snap: 0.6f); if (State is St.Roam or St.Noise or St.Hold) { State = St.Hunt; Replan(); } }
                }
                else if (attackerBot != null && attackerBot.Alive && foe == null && !brain!.SeesPlayer)
                {
                    Face(attackerBot.Body!.Head - b.Head, snap: 0.6f);
                    foeScanAt = 0;
                }
            }
            hitByPlayer = false; attackerBot = null;
            lastHp = b.Hp;

            brain!.Update(G, dt);
            bool fightPlayer = brain.SeesPlayer;
            if (fightPlayer)
            {
                lkp = m.PlayerFeet; lkpAt = Now; foe = null;
                protectedUntil = 0; // engaging ends spawn protection
                State = St.Fight;
                if (Now - noiseAt > 0.6f) { noiseAt = Now; m.Noise(b.Feet, this, 25f); } // the fight is audible
            }
            else UpdateFoe();
            bool fighting = fightPlayer || foe != null;
            if (!fighting && State == St.Fight)
            {
                // Lost sight: Elite+ often keep holding the angle for a moment, others chase where you were.
                if (Tier >= 3 && Rng.NextDouble() < 0.5) { State = St.Hold; holdUntil = Now + R(0.5f, 1.4f); holdDir = brain.HeldDir; }
                else { State = lkp != null ? St.Hunt : St.Roam; }
                Replan();
            }

            // ---- desired velocity ----
            Vector3 want = Vector3.Zero;
            float speed = Mover.RunSpeed;
            if (fighting)
            {
                if (Tier >= 3 && Now >= nextStrafeAt)
                {
                    // A-D jiggle between bursts (brain holds fire while moving faster than ~1.5 m/s).
                    strafeSign = -strafeSign;
                    strafeUntil = Now + R(0.16f, 0.26f);
                    nextStrafeAt = Now + R(0.7f, 1.5f);
                }
                if (Now < strafeUntil)
                {
                    var f = brain.HeldDir; var side = new Vector3(-f.Z, 0, f.X).Normalized() * strafeSign;
                    if (m.nav.At(b.Feet + side * 0.8f, Collision.StepHeight) >= 0) want = side;
                }
            }
            else
            {
                if (State == St.Hold && Now >= holdUntil) { State = lkp != null && Now - lkpAt < 10f ? St.Hunt : St.Roam; Replan(); }
                if (State != St.Hold)
                {
                    if (path == null || wp >= path.Count) PlanNext();
                    if (path != null && wp < path.Count)
                    {
                        var to = path[wp] - b.Feet; to.Y = 0;
                        if (to.Length() < 0.45f) { wp++; if (wp >= path.Count) Arrived(); }
                        else want = to.Normalized();
                        // Veteran+ walk (silently) the last metres toward where you were.
                        if (State == St.Hunt && Tier >= 2 && lkp is { } k && b.Feet.DistanceTo(k) < 9f) speed = Mover.WalkSpeed;
                        if (Tier == 0) speed = 4.6f; // Rookies jog
                    }
                }
            }

            // ---- accelerate / stop (counter-strafe) ----
            var target = want * speed;
            float accel = target.LengthSquared() > 0.01f ? 40f : Difficulty.T(Tier, 9f, 15f, 34f, 42f, 50f);
            vel = vel.MoveToward(target, accel * dt);
            var feet = b.Feet;
            var v = vel;
            var before = feet;
            Collision.MoveAndSlide(ref feet, ref v, v * dt, G.Solid);
            feet.Y = Collision.Ground(feet, G.Solid);
            vel = v;
            b.Feet = feet;
            b.Velocity = vel;
            Moved += new Vector2(feet.X - before.X, feet.Z - before.Z).Length();
            if (Collision.Overlaps(feet, G.Solid, 0.2f)) m.inWallFrames++;

            // ---- look ----
            if (!fightPlayer)
            {
                Vector3 look;
                if (foe != null && foe.Alive) look = foe.Body!.Head - b.Head;
                else if (State == St.Hold) look = holdDir;
                else if (Tier >= 2 && lkp is { } k && Now - lkpAt < 12f && k.DistanceTo(feet) < 25f)
                    look = k + new Vector3(0, PlayerView.EyeHeight, 0) - b.Head;   // crosshair on where you were
                else if (vel.LengthSquared() > 0.5f) look = new Vector3(vel.X, 0, vel.Z);
                else look = brain.HeldDir;
                Turn(look, dt);
            }

            // ---- stuck: has a path but isn't getting anywhere ----
            if (Now - stuckAt > 1.2f)
            {
                bool tryingToMove = !fighting && State != St.Hold && path != null && Now - pathSince >= 1.2f;
                if (tryingToMove && feet.DistanceTo(stuckPos) < 0.3f)
                {
                    Stuck++; stuckRun++;
                    if (DevLog && Stuck <= 3) GD.Print($"[dm] {Name} stuck at {feet} ({State}, wp {wp}/{path!.Count} next {(wp < path.Count ? path[wp] : Vector3.Zero)}, vel {vel.Length():0.0}, want {want}, speed {speed}, sees {brain.SeesPlayer})");
                    Replan(); repathAt = Now;
                    if (stuckRun >= 3) { State = St.Roam; lkp = null; noise = null; stuckRun = 0; }
                }
                else stuckRun = 0;
                stuckAt = Now; stuckPos = feet;
            }

            if (Now >= rumorAt && m.PlayerAlive && Now - m.playerSpawnedAt > 4f) // never tip bots off to a fresh spawn
            {
                // DM players drift toward the action: every few seconds a bot "hears" roughly where you are.
                rumorAt = Now + Difficulty.T(Tier, 14f, 12f, 10f, 9f, 8f) * R(0.7f, 1.3f);
                if (State is St.Roam or St.Noise && Rng.NextDouble() < Difficulty.T(Tier, 0.45f, 0.55f, 0.65f, 0.7f, 0.75f))
                {
                    lkp = m.PlayerFeet + new Vector3(R(-5, 5), 0, R(-5, 5));
                    lkpAt = Now; State = St.Hunt; Replan();
                }
            }
        }

        /// <summary>Drop the current path (and any search in flight) so the next frame plans a fresh one.</summary>
        void Replan() { path = null; pending = null; }

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
                if (path != null && path.Count > 0 && !m.nav.Straight(Body!.Feet, path[0]))
                {
                    int n = m.nav.Nearest(Body.Feet);
                    if (n >= 0) path.Insert(0, m.nav.Pos[n]);
                }
                if (path == null || path.Count == 0)
                {
                    path = null;
                    if (State != St.Roam) { lkp = null; noise = null; State = St.Roam; }
                }
                return;
            }
            if (Now < repathAt || m.pathBudget <= 0) return;
            var feet = Body!.Feet;
            // Hurt and a health pack nearby: go grab it.
            if (Body.Hp <= 100 && m.NearestPack(feet, 16f) is { } pack) { State = St.Pack; goal = pack; }
            else if (lkp is { } k && Now - lkpAt < 12f && State != St.Noise) { State = St.Hunt; goal = k; }
            else if (noise is { } nz) { State = St.Noise; goal = nz; }
            else
            {
                State = St.Roam;
                goal = feet;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.nav.Random(Rng);
                    float d = c.DistanceTo(feet);
                    goal = c;
                    if (d is > 8f and < 35f) break;
                }
            }
            m.pathBudget--;
            path = null;
            pending = m.nav.FindPathAsync(feet, goal);
            pendingState = State;
            repathAt = Now + 0.6f;
        }

        void Arrived()
        {
            path = null;
            var prev = State;
            if (State == St.Hunt) lkp = null;
            if (State == St.Noise) noise = null;
            State = St.Roam;
            // Sometimes stop and hold an angle (more often at higher tiers): face the longest open sightline.
            if (prev != St.Pack && Rng.NextDouble() < Difficulty.T(Tier, 0.15f, 0.22f, 0.3f, 0.38f, 0.45f))
            {
                State = St.Hold;
                holdUntil = Now + R(1.2f, 3.5f);
                holdDir = YawDir(m.OpenYaw(Body!.Feet));
            }
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
            // Bots are here to fight YOU: while chasing a fresh lead on you they mostly ignore each other.
            if (State == St.Hunt && Now - lkpAt < 4f && Rng.NextDouble() < 0.6) return;
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
                State = St.Fight;
                return;
            }
        }

        void FireAtFoe()
        {
            var b = Body!; var o = foe!;
            var w = Weapons.Vandal;
            bool moving = vel.Length() > 1.5f;
            int s = foeShot % skill.Burst;
            float p = Mathf.Max(skill.PHit1 * 0.35f, skill.PHit1 * Mathf.Pow(skill.Decay, s));
            if (moving)
            {
                if (!skill.RunAndGun) { foeFireAt += 0.05f; return; }
                p *= 0.25f;
            }
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
            G.SoundAt("enemyshot", b.Head, maxRange: 60f, floor: 0.6f);
            m.Noise(b.Feet, this, 25f);
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
