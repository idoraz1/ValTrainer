using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

public sealed partial class AnchorMode
{
    /// <summary>
    /// One attacker of the execute. Runs the map's navigation grid (<see cref="BotNav"/>) from behind the choke: approach
    /// (audible run) → stack out of your sight → entry through the choke to its own site spot, clearing common angles on
    /// the way (pre-aims defender spots it can see, your last known position first) → hold / plant / hunt you. Duels with
    /// the tier's <see cref="BotBrain"/> (Veteran+ stop to shoot, lower tiers run-and-gun), shoots utility it notices
    /// (tier-scaled) and must shoot walls that block its path. Sentinel effects land on it through the small API below.
    /// </summary>
    sealed class Attacker
    {
        public enum St { Approach, Stack, Entry, Hold, Plant, Hunt }

        readonly AnchorMode m;
        public readonly int Index;
        public readonly string Name;
        public readonly BotCharacter Body;
        public readonly BotBrain Brain;
        public St State = St.Approach;
        public List<Vector3> Path = new();
        int wp;
        public List<Vector3> EntryPath = new();
        public Vector3 StackAt;
        public float GoAt = float.PositiveInfinity;
        public bool Carrier;
        public float PlantT;
        float holdUntil;
        Vector3 vel;
        public readonly FootstepEmitter Steps = new();

        // effects (session clock)
        readonly List<(float Until, float Mul)> slows = new();
        float heldUntil = -1f, revealUntil = -1f, vulnUntil = -1f, decayFrom = -1f, decayAmount;
        public float UtilHitAt = -99f;
        public bool Caught, SeenByPlayer, Revealed;
        public Device? LastUtil;

        // utility shooting
        public Device? Target;
        float shootAt;
        int shotsAt;
        float noticeScanAt;
        readonly HashSet<Device> noticed = new();
        float lastFireAt = -99f;

        // looking / clearing
        Vector3? lookAt;
        float lookPickAt, lookedSince;
        readonly HashSet<int> cleared = new();
        Vector3? lkp;
        float lkpAt = -99f;

        float stuckAt;
        Vector3 stuckPos;

        public Attacker(AnchorMode mode, int index, BotCharacter body, BotBrain brain)
        {
            m = mode; Index = index; Body = body; Brain = brain;
            Name = $"Attacker {index + 1}";
            stuckAt = mode.Now; stuckPos = body.Feet;
        }

        float Now => m.Now;
        IGame G => m.G;
        int Tier => m.Tier;
        public bool Alive => GodotObject.IsInstanceValid(Body) && !Body.Dead;
        public Vector3 Chest => Body.Head + Vector3.Down * 0.45f;
        public Vector3 Velocity => vel;
        public float Speed => new Vector2(vel.X, vel.Z).Length();
        /// <summary>Running = audible footsteps (walking / crouching is silent).</summary>
        public bool Loud => Alive && Speed > FootstepFx.AudibleSpeed;
        /// <summary>Shot a gun in the last half second (sonic sensor, audio).</summary>
        public bool Firing => Now - lastFireAt < 0.5f || (Brain.SeesPlayer && Now - Brain.FirstSeenAt > 0.3f);
        public bool Held => Now < heldUntil;
        public bool IsRevealed => Now < revealUntil;
        public bool IsVulnerable => Now < vulnUntil;
        public bool Affected => Held || IsVulnerable || Brain.IsDazed(Now) || SlowMul < 0.99f || IsRevealed || Now - UtilHitAt < 3f;
        public float SlowMul
        {
            get
            {
                float k = 1f;
                foreach (var (u, mul) in slows) if (Now < u) k = Mathf.Min(k, mul);
                return k;
            }
        }

        // ---------------------------------------------------------------- effects API

        void Hit() => UtilHitAt = Now;
        public void Slow(float mul, float seconds) { slows.Add((Now + seconds, mul)); if (slows.Count > 24) slows.RemoveAll(s => s.Until < Now); Hit(); }
        public void Hold(float seconds) { heldUntil = Mathf.Max(heldUntil, Now + seconds); Hit(); }
        public void Reveal(float seconds)
        {
            revealUntil = Mathf.Max(revealUntil, Now + seconds);
            if (!Revealed) { Revealed = true; m.reveals++; }
            Hit();
        }
        public void Vulnerable(float seconds) { vulnUntil = Mathf.Max(vulnUntil, Now + seconds); Hit(); }
        public void Daze(float seconds) { Brain.DazedUntil = Mathf.Max(Brain.DazedUntil, Now + seconds); Hit(); }
        /// <summary>Concuss: severely slowed, dazed (slower reaction, worse aim, reduced fire rate).</summary>
        public void Concuss(float seconds) { Slow(0.3f, seconds); Daze(seconds); }
        /// <summary>Decay: <paramref name="hp"/> taken now (never lethal), restored over <paramref name="seconds"/> afterwards.</summary>
        public void Decay(float hp, float seconds)
        {
            int take = Math.Min(Body.Hp - 1, (int)hp);
            if (take <= 0) return;
            Body.Hp -= take;
            decayAmount = take;
            decayFrom = Now + seconds;
            Hit();
        }

        public void TakeDamage(float dmg, Device? src, Vector3 dir)
        {
            if (!Alive) return;
            if (IsVulnerable) dmg *= 2f;
            Body.Hp -= (int)Mathf.Round(dmg);
            Body.OnHit(HitZone.Body);
            Hit();
            if (Body.Dead)
            {
                Body.Die(dir);
                m.OnUtilKilled(this, src);
            }
        }

        /// <summary>Must deal with this piece (caught in a wire): shoot it after a reaction.</summary>
        public void ForceTarget(Device d, float extra)
        {
            Target = d;
            shootAt = Now + React() + extra;
            shotsAt = 0;
        }

        float React() => Mathf.Max(0.15f, m.Gauss(Brain.Skill.ReactMs, Brain.Skill.ReactSd) / 1000f) * (Brain.IsDazed(Now) ? 1.6f : 1f);

        // ---------------------------------------------------------------- frame

        public void Update(float dt)
        {
            if (!Alive) return;
            var b = Body;
            if (decayFrom >= 0 && Now >= decayFrom && decayAmount > 0)
            {
                // Decay wears off: health comes back over the next 4.5 s (est. rate).
                float back = Mathf.Min(decayAmount, 75f / 4.5f * dt);
                b.Hp = Math.Min(BotCharacter.MaxHp, b.Hp + (int)Mathf.Ceil(back));
                decayAmount -= back;
            }

            Brain.Update(G, dt);
            if (!Alive) return;
            bool fight = Brain.SeesPlayer;
            if (fight)
            {
                lkp = m.PlayerFeet; lkpAt = Now;
                lastFireAt = Now;
                if (Target != null && Target is not TrapwireDev) Target = null; // the duel comes first
            }

            NoticeUtility();

            // ---- desired velocity ----
            Vector3 want = Vector3.Zero;
            float speed = Tier == 0 ? 4.6f : Mover.RunSpeed;
            bool shooting = Target != null && Now >= shootAt - 0.25f;
            if (Held) { }
            else if (fight)
            {
                // Rookie / Regular keep drifting along their path (run-and-gun); Veteran+ stop dead to shoot.
                if (Brain.Skill.RunAndGun) { want = PathDir(); speed *= 0.45f; }
            }
            else if (shooting && (Tier >= 2 || Target is SageSegDev or MeshOrbDev || Target is TrapwireDev)) { }
            else
            {
                switch (State)
                {
                    case St.Approach:
                        want = PathDir();
                        if (wp >= Path.Count) { State = St.Stack; }
                        break;
                    case St.Stack:
                        if (Now >= GoAt) { State = St.Entry; SetPath(EntryPath); m.Log($"{Name} swings"); }
                        break;
                    case St.Entry:
                    case St.Hunt:
                        want = PathDir();
                        if (wp >= Path.Count) Arrived();
                        // Elite+ walk (silently) the last metres toward where they think you are.
                        if (State == St.Hunt && Tier >= 3 && lkp is { } k && b.Feet.DistanceTo(k) < 9f) speed = Mover.WalkSpeed;
                        break;
                    case St.Hold:
                        if (Now >= holdUntil) StartHunt();
                        break;
                    case St.Plant:
                        PlantT += dt;
                        if (PlantT >= SpikePlantTime) m.OnPlanted(this);
                        break;
                }
            }

            // ---- accelerate / stop (counter-strafe at Veteran+) ----
            var target = want * speed * SlowMul;
            float accel = target.LengthSquared() > 0.01f ? 40f : Difficulty.T(Tier, 9f, 15f, 34f, 42f, 50f);
            vel = vel.MoveToward(target, accel * dt);
            if (Held) vel = Vector3.Zero;
            var before = b.Feet;
            var feet = before;
            var v = vel;
            Collision.MoveAndSlide(ref feet, ref v, v * dt, G.Solid);
            if (m.MoveBlocked(before, feet, out var wall))
            {
                feet = before; v = Vector3.Zero;
                if (!fight && wall.Shootable && Target != wall && (Target == null || Target.Dead || Target.Spent))
                {
                    Target = wall; shootAt = Now + React() * 0.6f; shotsAt = 0;
                    m.Log($"{Name} blocked by {wall.A.Ability}");
                }
            }
            feet.Y = Collision.Ground(feet, G.Solid);
            vel = v;
            b.Feet = feet;
            b.Velocity = vel;
            m.Moved(this, before, feet);
            Steps.Update(G, m.MapSpot, feet, Speed, b.Crouched, dt);

            // ---- look ----
            if (!fight)
            {
                if (Target != null && !Target.Dead && !Target.Spent) Face(Target.AimFor(b.Head) - b.Head, dt, 900f);
                else Turn(LookDir(), dt);
            }

            ShootUtility();
            CheckStuck();
        }

        Vector3 PathDir()
        {
            while (wp < Path.Count)
            {
                var to = Path[wp] - Body.Feet; to.Y = 0;
                if (to.Length() < 0.45f) { wp++; continue; }
                return to.Normalized();
            }
            return Vector3.Zero;
        }

        public void SetPath(List<Vector3> p) { Path = p; wp = 0; stuckAt = Now; stuckPos = Body.Feet; }

        void Arrived()
        {
            if (Carrier && State == St.Entry) { State = St.Plant; PlantT = 0; m.Log($"{Name} planting"); return; }
            State = St.Hold;
            holdUntil = Now + m.R(1.2f, 3f) * (Tier >= 3 ? 1.4f : 1f);
            lookPickAt = 0;
        }

        /// <summary>Go after the player: toward where you were last seen (or roughly where you are — they heard you).</summary>
        void StartHunt()
        {
            var goal = lkp is { } k && Now - lkpAt < 10f ? k : m.PlayerFeet + new Vector3(m.R(-4, 4), 0, m.R(-4, 4));
            var path = m.nav.FindPath(Body.Feet, goal);
            if (path == null || path.Count == 0) { holdUntil = Now + 1.5f; return; }
            State = St.Hunt;
            SetPath(path);
        }

        void CheckStuck()
        {
            if (Now - stuckAt < 1.5f) return;
            bool trying = State is St.Approach or St.Entry or St.Hunt && wp < Path.Count && !Held && Target == null && !Brain.SeesPlayer;
            if (trying && Body.Feet.DistanceTo(stuckPos) < 0.3f && wp < Path.Count)
            {
                // Re-path from here to the current leg's end.
                var p = m.nav.FindPath(Body.Feet, Path[^1]);
                if (p != null && p.Count > 0) SetPath(p);
                else wp = Math.Min(wp + 1, Path.Count);
            }
            stuckAt = Now; stuckPos = Body.Feet;
        }

        // ---------------------------------------------------------------- clearing angles

        Vector3 LookDir()
        {
            var head = Body.Head;
            var travel = new Vector3(vel.X, 0, vel.Z);
            if (travel.LengthSquared() < 0.5f) travel = Brain.HeldDir with { Y = 0 };
            if (State == St.Stack || State == St.Approach)
            {
                // Stacked: hold the choke's exit (where a defender would swing from).
                var toChoke = m.MapSpot.Choke + new Vector3(0, 1.5f, 0) - head;
                if (State == St.Stack) return toChoke;
                return travel;
            }
            if (lkp is { } k && Now - lkpAt < 6f && Tier >= 1 && !Collision.Blocked(head, k + new Vector3(0, 1.4f, 0), G.Solid))
                return k + new Vector3(0, PlayerViewEye, 0) - head;
            if (Now >= lookPickAt || lookAt == null)
            {
                lookPickAt = Now + m.R(0.45f, 0.9f) * Difficulty.T(Tier, 1.6f, 1.3f, 1f, 0.85f, 0.75f);
                lookAt = PickAngle(head, travel);
            }
            return lookAt is { } la ? la - head : travel;
        }

        const float PlayerViewEye = PlayerView.EyeHeight;

        /// <summary>The next common angle to check: defender spots it can see, nearest to where it's heading, not yet cleared.</summary>
        Vector3? PickAngle(Vector3 head, Vector3 travel)
        {
            Vector3? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < m.angles.Count; i++)
            {
                var p = m.angles[i];
                var to = p - head;
                float d = to.Length();
                if (d < 2f || d > 40f) continue;
                float ang = travel.LengthSquared() > 0.01f ? Mathf.RadToDeg(new Vector3(to.X, 0, to.Z).AngleTo(travel)) : 0f;
                if (ang > (State == St.Hold ? 160f : 110f)) continue;
                if (Collision.Blocked(head, p, G.Solid)) continue;
                float score = ang + (cleared.Contains(i) ? 200f : 0f) + d * 0.5f;
                if (score < bestScore) { bestScore = score; best = p; lookedSince = Now; if (!cleared.Contains(i)) { } }
            }
            if (best is { } bp) cleared.Add(m.angles.IndexOf(bp));
            return best;
        }

        void Turn(Vector3 look, float dt) => Face(look, dt, Difficulty.T(Tier, 300f, 420f, 560f, 720f, 900f));

        void Face(Vector3 look, float dt, float rate)
        {
            var h = new Vector3(look.X, 0, look.Z);
            if (h.LengthSquared() < 1e-4f) return;
            float target = Mathf.RadToDeg(Mathf.Atan2(h.X, -h.Z));
            float diff = Mathf.Wrap(target - Body.FacingYaw, -180f, 180f);
            Body.FacingYaw += Mathf.Clamp(diff, -rate * dt, rate * dt);
            var d = look.Normalized();
            float r = Mathf.DegToRad(Body.FacingYaw);
            var yd = new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r));
            float pitchY = Mathf.Clamp(d.Y, -0.5f, 0.5f);
            Brain.HeldDir = (yd * Mathf.Sqrt(1 - pitchY * pitchY) + new Vector3(0, pitchY, 0)).Normalized();
        }

        // ---------------------------------------------------------------- utility

        static float NoticeChance(int tier) => Difficulty.T(tier, 0.2f, 0.4f, 0.6f, 0.75f, 0.9f);

        void NoticeUtility()
        {
            if (Now < noticeScanAt || Brain.SeesPlayer || (Target != null && !Target.Dead && !Target.Spent)) return;
            noticeScanAt = Now + 0.25f;
            Target = null;
            if (Brain.IsBlind(Now)) return;
            foreach (var d in m.devices)
            {
                if (d.Dead || d.Spent || !d.Shootable || noticed.Contains(d) || d.SeenRange <= 0f) continue;
                if (d is SageSegDev or MeshOrbDev) continue; // walls are only shot when they're in the way
                var aim = d.AimFor(Body.Head);
                var to = aim - Body.Head;
                float dist = to.Length();
                if (dist > d.SeenRange || !d.Spottable(this)) continue;
                if (Mathf.RadToDeg(Brain.HeldDir.AngleTo(to)) > Brain.HalfFovDeg) continue;
                if (!G.LineOfSight(Body.Head, aim)) continue;
                noticed.Add(d);
                if (m.Rng.NextDouble() >= NoticeChance(Tier)) continue;
                Target = d;
                shootAt = Now + React() + m.R(0.1f, 0.35f);
                shotsAt = 0;
                return;
            }
        }

        void ShootUtility()
        {
            if (Target == null) return;
            if (Target.Dead || Target.Spent) { Target = null; return; }
            if (Brain.SeesPlayer || Brain.IsBlind(Now) || Now < shootAt) return;
            var aim = Target.AimFor(Body.Head);
            if (!G.LineOfSight(Body.Head, aim) && Target is not (SageSegDev or MeshOrbDev))
            {
                if (Now - shootAt > 2f) Target = null;
                return;
            }
            var w = Weapons.Vandal;
            shotsAt++;
            shootAt += w.Interval;
            if (shotsAt % Brain.Skill.Burst == 0) shootAt += Brain.Skill.GapMs / 1000f * 0.5f;
            float p = Mathf.Clamp(Brain.Skill.PHit1 * Mathf.Pow(Brain.Skill.Decay, (shotsAt - 1) % Brain.Skill.Burst) + 0.2f, 0.3f, 0.97f);
            if (Brain.IsDazed(Now)) p *= 0.6f;
            if (Target is SageSegDev or MeshOrbDev) p = 0.95f; // walls are big
            bool hit = m.Rng.NextDouble() < p;
            var end = hit ? aim : aim + new Vector3(m.R(-0.4f, 0.4f), m.R(-0.3f, 0.3f), m.R(-0.4f, 0.4f));
            Body.AimTarget = end;
            Body.ShootFx(end);
            lastFireAt = Now;
            G.SoundAt("enemyshot", Body.Head, maxRange: 60f, floor: 0.6f);
            if (hit) Target.Damage(w.Damage(1, Body.Head.DistanceTo(aim)));
            if (Target.Dead) { m.Log($"{Name} destroyed {Target.A.Ability}"); Target = null; Body.AimTarget = null; }
        }

        public Vector3 Feet => Body.Feet;
    }

    /// <summary>VALORANT spike plant time (s).</summary>
    const float SpikePlantTime = 4f;

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }
}
