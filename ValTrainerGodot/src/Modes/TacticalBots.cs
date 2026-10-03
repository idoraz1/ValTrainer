using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

public abstract partial class TacticalMode
{
    /// <summary>
    /// One spike-drill bot: an animated <see cref="BotCharacter"/> with the tier's duel model (<see cref="BotBrain"/>) and a
    /// small motor in the style of the Deathmatch bots — runs nav-grid paths (<see cref="BotNav"/>), pre-aims a point while
    /// it moves, holds a direction when it stands, stops to shoot (Veteran+ counter-strafe dead, Rookie/Regular slide and
    /// run-and-gun), Elite+ jiggle between bursts, kneels to defuse. The squad logic of each mode decides where it goes;
    /// the per-bot plan state below is the mode's memory.
    /// </summary>
    protected sealed class TacBot
    {
        readonly TacticalMode m;
        public readonly int Index;
        public readonly string Name;
        public BotCharacter Body = null!;
        public BotBrain Brain = null!;
        public float SeenByPlayerAt = -99f;
        public int Stuck;
        public float Moved;

        // ---- squad memory (owned by the modes) ----
        public int Plan;
        public float PlanAt, GoAt, WaitUntil;
        public int Entry;
        public Vector3 Stage, ClearPt, Anchor;
        public PostSpot? Spot;
        public bool Defuser, Staged;
        public float HeardAt = -99f, HitAt = -99f, LostSightAt = -99f, FoughtAt = -99f, SpottedAt = -1f;
        public Vector3 HeardPos;
        public float DefuseReactAt = -1f;
        /// <summary>Its own knowledge of you (saw you, took your shots, heard you).</summary>
        public Vector3 OwnSeen;
        public float OwnSeenAt = -99f;
        public int Sticks;

        // ---- motor ----
        Vector3 vel;
        List<Vector3>? path;
        int wp;
        Vector3 goal;
        bool wantPath;
        public bool Walk;
        /// <summary>Pre-aim point (eye height) while moving or holding; null = look where you go / <see cref="HoldDir"/>.</summary>
        public Vector3? LookAt;
        public Vector3 HoldDir = Vector3.Forward;
        public bool CrouchHold;
        public bool Kneeling { get; private set; }
        public bool Frozen;
        bool wasFighting;
        float strafeUntil, nextStrafeAt, strafeSign = 1f, stuckAt, pathSince;
        int stuckRun;
        Vector3 stuckPos;
        int lastHp = BotCharacter.MaxHp;

        IGame G => m.G;
        float Now => m.Now;
        int Tier => m.Tier;
        Random Rng => m.Rng;
        float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public TacBot(TacticalMode mode, int index, string name) { m = mode; Index = index; Name = name; }

        public bool Alive => Body != null && GodotObject.IsInstanceValid(Body) && !Body.Dead;
        public bool Moving => path != null || wantPath;
        public bool Fighting => Alive && Brain.SeesPlayer;
        public Vector3 Goal => goal;
        public Vector3 Feet => Body.Feet;

        public void Spawn(Vector3 feet, Vector3 lookAt, bool crouch)
        {
            Body = G.SpawnBot(feet, crouch);
            var d = lookAt - (feet + new Vector3(0, PlayerView.EyeHeight, 0));
            Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
            HoldDir = d.LengthSquared() > 1e-4f ? d.Normalized() : Vector3.Forward;
            Brain = new BotBrain(Body, Difficulty.Get(Tier).Bot, Rng) { HeldDir = HoldDir, ShoulderSight = Difficulty.ShoulderSight(Tier) };
            CrouchHold = crouch;
            lastHp = Body.Hp;
            stuckAt = Now; stuckPos = feet;
        }

        public void Despawn() { if (Body != null && GodotObject.IsInstanceValid(Body)) G.Despawn(Body); }

        public void Freeze()
        {
            Frozen = true; path = null; wantPath = false;
            if (Alive) { Brain.Enabled = false; Body.Velocity = Vector3.Zero; Body.AimTarget = null; }
        }

        public void UpdateDead() { }

        /// <summary>Walk / run a nav path to <paramref name="target"/> (planned this frame if the path budget allows, else soon).</summary>
        public void GoTo(Vector3 target, bool walk = false)
        {
            goal = target; Walk = walk; wantPath = true; path = null;
            TryPlan();
        }

        public void Stop() { path = null; wantPath = false; }

        public bool Near(Vector3 p, float r = 0.8f) => Alive && H(Body.Feet, p) < r && Mathf.Abs(Body.Feet.Y - p.Y) < 1.2f;

        void TryPlan()
        {
            if (!wantPath || m.PathBudget <= 0 || !Alive) return;
            m.PathBudget--;
            wantPath = false;
            var p = m.Nav.FindPath(Body.Feet, goal);
            if (p == null || p.Count == 0) { path = null; return; }
            // end exactly on the goal when it's reachable in a straight line from the last node
            if (H(p[^1], goal) > 0.1f && m.Nav.Straight(p[^1], goal)) p.Add(goal);
            path = p; wp = 0; pathSince = Now;
        }

        public void BeginKneel() { Kneeling = true; Stop(); Body.Crouched = true; Brain.Enabled = false; Body.AimTarget = null; }
        public void EndKneel() { Kneeling = false; if (Alive) { Body.Crouched = CrouchHold && !Moving; Brain.Enabled = true; } }

        /// <summary>Raw sight of the player for a kneeling defuser (its brain is off): line of sight within ±100° of its facing.</summary>
        public bool CanSeePlayerRaw()
        {
            if (!Alive || G.Player.Dead || DevGhost) return false;
            var to = G.View.Eye - Body.Head;
            var f = YawDir(Body.FacingYaw);
            if (new Vector3(to.X, 0, to.Z).Normalized().Dot(f) < -0.17f) return false;
            return G.LineOfSight(Body.Head, G.View.Eye) || G.LineOfSight(Body.Head, G.View.Eye + Vector3.Down * 0.45f);
        }

        public void HeardShot(Vector3 at)
        {
            HeardAt = Now; HeardPos = at;
            if (Now - OwnSeenAt > 0.5f) { OwnSeen = at + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f)); OwnSeenAt = Now; }
        }
        public void HitByPlayerAt(Vector3 from) { HitAt = Now; HeardPos = from; OwnSeen = from; OwnSeenAt = Now; }

        // ---------------------------------------------------------------- frame

        public void Update(float dt)
        {
            if (!Alive || Frozen) return;
            var b = Body;
            if (wantPath) TryPlan();

            // Shot by the player without seeing them: turn toward the shots.
            if (b.Hp < lastHp && !Brain.SeesPlayer && !Kneeling && Now - HitAt < 0.3f)
                Face(G.View.Eye - b.Head, 0.65f);
            lastHp = b.Hp;

            if (Kneeling)
            {
                vel = Vector3.Zero; b.Velocity = Vector3.Zero;
                b.Crouched = true;
                return;
            }

            if (DevGhost) Brain.Enabled = false;
            Brain.Update(G, dt);
            bool fighting = Brain.SeesPlayer;
            if (fighting) { FoughtAt = Now; if (SpottedAt < 0) SpottedAt = Now; OwnSeen = G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0); OwnSeenAt = Now; }
            if (!fighting && wasFighting) LostSightAt = Now;
            wasFighting = fighting;

            // ---- desired velocity ----
            Vector3 want = Vector3.Zero;
            float spd = Walk ? Mover.WalkSpeed : Tier == 0 ? 4.6f : Mover.RunSpeed;
            if (fighting)
            {
                if (Tier >= 3 && Now >= nextStrafeAt)
                {
                    // A-D jiggle between bursts (the brain holds fire while moving faster than ~1.5 m/s).
                    strafeSign = -strafeSign;
                    strafeUntil = Now + R(0.16f, 0.26f);
                    nextStrafeAt = Now + R(0.8f, 1.6f);
                }
                if (Now < strafeUntil)
                {
                    var f = Brain.HeldDir; var side = new Vector3(-f.Z, 0, f.X).Normalized() * strafeSign;
                    if (m.Nav.At(b.Feet + side * 0.8f, Collision.StepHeight) >= 0) want = side;
                }
            }
            else if (path != null && wp < path.Count)
            {
                var to = path[wp] - b.Feet; to.Y = 0;
                if (to.Length() < 0.4f) { wp++; if (wp >= path.Count) path = null; }
                else want = to.Normalized();
            }

            var target = want * spd;
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
            b.Crouched = CrouchHold && !Moving && vel.Length() < 0.5f && !fighting;

            // ---- look ----
            if (!fighting)
            {
                Vector3 look;
                if (LookAt is { } la) look = la - b.Head;
                else if (vel.LengthSquared() > 0.5f) look = new Vector3(vel.X, 0, vel.Z);
                else look = HoldDir;
                Turn(look, dt);
            }

            // ---- stuck: has a path but isn't getting anywhere ----
            if (Now - stuckAt > 1.2f)
            {
                if (path != null && !fighting && Now - pathSince > 1.2f && feet.DistanceTo(stuckPos) < 0.3f)
                {
                    Stuck++; stuckRun++;
                    if (DevLog && Stuck <= 3) GD.Print($"[tac] {Name} stuck at {Fmt(feet)} → goal {Fmt(goal)} (wp {wp}/{path.Count}: {string.Join(" ", path.Select(Fmt))}) vel {vel.Length():0.0} kneel {Kneeling} plan {Plan}");
                    if (H(feet, goal) < 1.6f) Stop();                       // close enough: call it arrived
                    else if (stuckRun >= 2)
                    {
                        // un-wedge: step onto the nearest open grid node, then try again
                        int n = m.Nav.Nearest(feet);
                        if (n >= 0) b.Feet = m.Nav.Pos[n];
                        GoTo(goal, Walk);
                    }
                    else GoTo(goal, Walk);
                }
                else stuckRun = 0;
                stuckAt = Now; stuckPos = feet;
            }
        }

        void Face(Vector3 dir, float snap)
        {
            var h = new Vector3(dir.X, 0, dir.Z);
            if (h.LengthSquared() < 1e-4f) return;
            float target = Mathf.RadToDeg(Mathf.Atan2(h.X, -h.Z));
            Body.FacingYaw = Mathf.LerpAngle(Mathf.DegToRad(Body.FacingYaw), Mathf.DegToRad(target), snap) * 180f / Mathf.Pi;
            Brain.HeldDir = YawDir(Body.FacingYaw);
        }

        /// <summary>Turn instantly toward a point (heard the defuse: snap the crosshair there).</summary>
        public void SnapLook(Vector3 point)
        {
            if (!Alive) return;
            var d = point - Body.Head;
            Face(d, 1f);
            var n = d.Normalized();
            Brain.HeldDir = n;
        }

        void Turn(Vector3 look, float dt)
        {
            var h = new Vector3(look.X, 0, look.Z);
            if (h.LengthSquared() < 1e-4f) return;
            float target = Mathf.RadToDeg(Mathf.Atan2(h.X, -h.Z));
            float rate = Difficulty.T(Tier, 300f, 420f, 560f, 720f, 900f);
            float diff = Mathf.Wrap(target - Body.FacingYaw, -180f, 180f);
            Body.FacingYaw += Mathf.Clamp(diff, -rate * dt, rate * dt);
            var d = look.Normalized();
            var yd = YawDir(Body.FacingYaw);
            float pitchY = Mathf.Clamp(d.Y, -0.5f, 0.5f);
            Brain.HeldDir = (yd * Mathf.Sqrt(1 - pitchY * pitchY) + new Vector3(0, pitchY, 0)).Normalized();
        }

        static Vector3 YawDir(float yaw)
        {
            float r = Mathf.DegToRad(yaw);
            return new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r));
        }
    }
}
