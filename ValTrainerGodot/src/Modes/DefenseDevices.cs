using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

/// <summary>
/// The sentinel setup pieces of Site Anchor and what they do to the attackers. Numbers are from the official VALORANT wiki
/// ability pages (detection radii, wind-ups, durations, health); the few values the wiki doesn't list are marked "est.".
/// Effects map onto the bots as: reveal = outline through walls (<see cref="RevealFx"/>), slow = lower move speed,
/// held (tether / hold) = can't move, daze / concuss = slower reaction and worse aim (<see cref="Game.Bots.BotBrain.DazedUntil"/>),
/// vulnerable = your bullets do double damage, decay = temporary HP loss, walls = blocked paths they must shoot open.
/// </summary>
public sealed partial class AnchorMode
{
    /// <summary>Where and how a piece will be put down (computed from the crosshair, or by the dev auto-setup).</summary>
    sealed class Placement
    {
        public bool Ok;
        public string? Why;
        public Vector3 Pos, Normal = Vector3.Up, End, Axis = Vector3.Right;
        public List<Vector3>? Arc;
    }

    abstract class Device
    {
        protected readonly AnchorMode M;
        public readonly SentinelAbility A;
        public Vector3 Pos, Normal = Vector3.Up;
        public float Hp, MaxHp = 20f;
        public bool Dead;
        public float PlacedAt, DeployTime;
        public Node3D? Node;
        protected readonly List<Node> extra = new();
        /// <summary>Remembered placement (persistent pieces are rebuilt from it every round).</summary>
        public Placement? From;
        /// <summary>Used up (fired / expired) rather than destroyed.</summary>
        public bool Spent;

        protected Device(AnchorMode m, SentinelAbility a) { M = m; A = a; PlacedAt = m.Now; }

        protected IGame G => M.G;
        protected float Now => M.Now;
        public bool Armed => !Dead && !Spent && Now >= PlacedAt + DeployTime;
        public virtual Vector3 AimPoint => Pos + Normal * 0.15f;
        public virtual Vector3 AimFor(Vector3 from) => AimPoint;
        /// <summary>Attackers notice it within this distance (covert pieces: their wiki detection radius).</summary>
        public virtual float SeenRange => 7f;
        public virtual bool Shootable => true;
        public virtual bool Spottable(Attacker a) => true;
        public virtual void Update(float dt) { }
        public virtual void OnMoved(Attacker a, Vector3 from, Vector3 to) { }
        public virtual bool BlocksMove(Vector3 a, Vector3 b, out Device part) { part = this; return false; }
        public virtual float BulletBlock(Vector3 o, Vector3 d) => float.PositiveInfinity;

        public virtual void Damage(float dmg)
        {
            if (Dead || Spent || !Shootable) return;
            Hp -= dmg;
            if (Hp <= 0) Destroy();
        }

        public virtual void Destroy()
        {
            if (Dead) return;
            Dead = true;
            M.OnDeviceDestroyed(this);
            Free();
        }

        protected void Finish() { Spent = true; Free(); }

        public virtual void Free()
        {
            if (Node != null && GodotObject.IsInstanceValid(Node)) Node.QueueFree();
            Node = null;
            foreach (var n in extra) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
            extra.Clear();
        }

        protected T Keep<T>(T n) where T : Node { extra.Add(n); return n; }
        protected void Trigger(int affected) => M.UtilTrigger(this, affected);
    }

    // ------------------------------------------------------------------ geometry helpers

    static bool SegCross(Vector3 a, Vector3 b, Vector3 p, Vector3 q, out float s)
    {
        s = 0;
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        var e = new Vector2(q.X - p.X, q.Z - p.Z);
        float den = d.X * e.Y - d.Y * e.X;
        if (Mathf.Abs(den) < 1e-7f) return false;
        var w = new Vector2(p.X - a.X, p.Z - a.Z);
        s = (w.X * e.Y - w.Y * e.X) / den;
        float t = (w.X * d.Y - w.Y * d.X) / den;
        return s >= 0f && s <= 1f && t >= 0f && t <= 1f;
    }

    /// <summary>Distance in XZ from point p to segment a–b.</summary>
    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = new Vector2(b.X - a.X, b.Z - a.Z);
        var ap = new Vector2(p.X - a.X, p.Z - a.Z);
        float t = ab.LengthSquared() < 1e-6f ? 0 : Mathf.Clamp(ap.Dot(ab) / ab.LengthSquared(), 0, 1);
        return (ap - ab * t).Length();
    }

    /// <summary>Movement a→b runs into a wall segment p–q (with body radius).</summary>
    static bool HitsWall(Vector3 a, Vector3 b, Vector3 p, Vector3 q, float halfThick)
    {
        if (SegCross(a, b, p, q, out _)) return true;
        float before = SegDist(a, p, q), after = SegDist(b, p, q);
        return after < halfThick + Collision.PlayerRadius && after < before;
    }

    /// <summary>Ray vs an upright box around the segment p–q (bullets into walls).</summary>
    static float RayWall(Vector3 o, Vector3 d, Vector3 p, Vector3 q, float height, float thick)
    {
        var x = new Vector3(q.X - p.X, 0, q.Z - p.Z);
        float len = x.Length();
        if (len < 0.01f) return float.PositiveInfinity;
        x /= len;
        var z = new Vector3(-x.Z, 0, x.X);
        float baseY = Mathf.Min(p.Y, q.Y);
        // to the wall's local frame (x along, y up, z across)
        var lo = o - p;
        var lp = new Vector3(lo.Dot(x), o.Y - baseY, lo.Dot(z));
        var ld = new Vector3(d.Dot(x), d.Y, d.Dot(z));
        var box = new Box(new Vector3(0, 0, -thick / 2), new Vector3(len, height, thick / 2));
        return box.Ray(lp, ld);
    }

    // ------------------------------------------------------------------ Killjoy

    /// <summary>Alarmbot: covert floor bot. Enemy within 5.5 m → 0.5 s alert → chases at 22.5 m/s → within 2 m, 0.5 s wind-up
    /// → bursts: Vulnerable 4 s (est. 3.5 m burst radius). 20 HP; agents spot it within 7 m.</summary>
    sealed class AlarmbotDev : Device
    {
        enum S { Idle, Alert, Chase, Windup }
        S s;
        float t;
        Attacker? target;
        public AlarmbotDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 20f; DeployTime = 1.85f;
            Node = SentinelFx.Gadget(G.World, "alarmbot", a.Color, Pos, Vector3.Up, M.G.View.Yaw);
        }
        public override float SeenRange => s == S.Idle ? 7f : 25f;
        public override Vector3 AimPoint => Pos + new Vector3(0, 0.2f, 0);

        public override void Update(float dt)
        {
            if (!Armed) return;
            t += dt;
            switch (s)
            {
                case S.Idle:
                    target = M.attackers.Where(x => x.Alive && x.Chest.DistanceTo(AimPoint) <= 5.5f && G.LineOfSight(AimPoint, x.Chest))
                        .OrderBy(x => x.Chest.DistanceTo(AimPoint)).FirstOrDefault();
                    if (target != null) { s = S.Alert; t = 0; SentinelSfx.PlayAt(G, "alarm", Pos, 1f); M.Log($"alarmbot triggered by {target.Name}"); }
                    break;
                case S.Alert:
                    if (t >= 0.5f) { s = S.Chase; t = 0; }
                    break;
                case S.Chase:
                    if (target == null || !target.Alive) { s = S.Idle; t = 0; break; }
                    var to = target.Body.Feet - Pos; to.Y = 0;
                    float d = to.Length();
                    if (d <= 2f || t > 2f) { s = S.Windup; t = 0; break; }
                    var step = to / d * Mathf.Min(d, 22.5f * dt);
                    var np = Pos + step;
                    np.Y = Collision.Ground(new Vector3(np.X, Pos.Y + 0.5f, np.Z), G.Solid);
                    if (Collision.Blocked(Pos + Vector3.Up * 0.2f, np + Vector3.Up * 0.2f, G.Solid)) { s = S.Windup; t = 0; break; }
                    Pos = np;
                    if (Node != null) Node.GlobalPosition = Pos;
                    break;
                case S.Windup:
                    if (t < 0.5f) break;
                    int n = 0;
                    foreach (var x in M.attackers)
                    {
                        if (!x.Alive || x.Chest.DistanceTo(AimPoint) > 3.5f || !G.LineOfSight(AimPoint, x.Chest)) continue;
                        x.Vulnerable(4f);
                        M.Caught(x, this);
                        n++;
                    }
                    SentinelSfx.PlayAt(G, "boom", Pos, 1f);
                    Effects.BodyHit(AimPoint, A.Color, false);
                    Trigger(n);
                    Finish();
                    break;
            }
        }
    }

    /// <summary>Turret: 100° cone, 0.75 s wind-up on a new target, 3-round bursts at 12/s with 1 s between bursts, 8 / 6 / 4
    /// damage per shot (0–20 / 20–35 / 35+ m), each hit slows 50% briefly. 100 HP.</summary>
    sealed class TurretDev : Device
    {
        readonly Vector3 facing;
        Attacker? target;
        float readyAt, nextShot;
        int inBurst;
        public int HitsLanded;
        public TurretDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 100f; DeployTime = 2.7f;
            facing = new Vector3(p.Axis.X, 0, p.Axis.Z).Normalized();
            Node = SentinelFx.Gadget(G.World, "turret", a.Color, Pos, Vector3.Up, Yaw(facing));
        }
        static float Yaw(Vector3 d) => Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        Vector3 Muzzle => Pos + new Vector3(0, 0.45f, 0);
        public override Vector3 AimPoint => Pos + new Vector3(0, 0.4f, 0);
        public override float SeenRange => 35f;

        bool InCone(Attacker x)
        {
            var to = x.Chest - Muzzle; to.Y = 0;
            return to.Length() < 45f && Mathf.RadToDeg(facing.AngleTo(to.Normalized())) <= 50f && G.LineOfSight(Muzzle, x.Chest);
        }

        public override void Update(float dt)
        {
            if (!Armed) return;
            if (target == null || !target.Alive || !InCone(target))
            {
                var nt = M.attackers.Where(x => x.Alive && InCone(x)).OrderBy(x => x.Chest.DistanceTo(Muzzle)).FirstOrDefault();
                if (nt != target)
                {
                    target = nt;
                    readyAt = Now + 0.75f;
                    nextShot = readyAt;
                    inBurst = 0;
                    if (nt != null) { M.Log($"turret locks {nt.Name}"); Trigger(1); }
                }
                if (target == null) return;
            }
            var look = target.Chest - Muzzle;
            if (Node != null) Node.Rotation = new Vector3(0, Mathf.DegToRad(-Yaw(look)), 0);
            int guard = 0;
            while (Now >= nextShot && guard++ < 3)
            {
                float dist = look.Length();
                bool hit = M.Rng.NextDouble() < (target.Speed > 2f ? 0.55f : 0.75f);
                var end = hit ? target.Chest : target.Chest + new Vector3(M.R(-0.5f, 0.5f), M.R(-0.3f, 0.4f), M.R(-0.5f, 0.5f));
                M.Tracer(Muzzle, end, A.Color);
                SentinelSfx.PlayAt(G, "turret", Muzzle, 0.6f);
                if (hit)
                {
                    float dmg = dist < 20f ? 8f : dist < 35f ? 6f : 4f;
                    target.Slow(0.5f, 0.45f);
                    M.Caught(target, this);
                    HitsLanded++;
                    target.TakeDamage(dmg, this, (target.Chest - Muzzle).Normalized());
                }
                inBurst++;
                nextShot += inBurst % 3 == 0 ? 1f : 1f / 12f;
                if (!target.Alive) break;
            }
        }
    }

    // ------------------------------------------------------------------ Cypher

    /// <summary>Trapwire: spans wall to wall (≤ 15 m). Crossing it reveals + slows (1.25 s); after a 0.7 s wind-up the victim
    /// is held (tethered, dazed, 5 damage) until it breaks an anchor (20 HP). Re-arms 2 s after its victim is gone. Agents
    /// spot it within 3 m.</summary>
    sealed class TrapwireDev : Device
    {
        public Vector3 End;
        readonly float wireY;
        Attacker? victim;
        float caughtAt, rearmAt = -1f;
        bool dealt;
        MeshInstance3D? beam;
        public TrapwireDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; End = p.End; Normal = p.Normal; Hp = MaxHp = 20f; DeployTime = 1.5f;
            wireY = Pos.Y;
            Node = SentinelFx.Gadget(G.World, "anchor", a.Color, Pos, p.Normal);
            Keep(SentinelFx.Gadget(G.World, "anchor", a.Color, End, -p.Normal));
            beam = Keep(SentinelFx.Beam(G.World, Pos, End, 0.012f, a.Color, 0.45f));
        }
        public override float SeenRange => victim != null ? 30f : 3f;
        public override Vector3 AimFor(Vector3 from) => from.DistanceTo(Pos) <= from.DistanceTo(End) ? Pos + Normal * 0.08f : End - Normal * 0.08f;
        public override bool Spottable(Attacker a) => victim != null || SegDist(a.Body.Feet, Pos, End) < 3f;

        public override void OnMoved(Attacker a, Vector3 from, Vector3 to)
        {
            if (!Armed || victim != null || Now < rearmAt) return;
            if (to.Y > wireY + 0.3f || to.Y + 1.8f < wireY) return;
            if (!SegCross(from, to, Pos, End, out _)) return;
            victim = a; caughtAt = Now; dealt = false;
            a.Reveal(0.6f);
            a.Slow(0.4f, 1.25f);
            a.ForceTarget(this, 0.15f);
            M.Caught(a, this);
            SentinelSfx.PlayAt(G, "wire", (Pos + End) / 2, 1f);
            Trigger(1);
            M.Log($"trapwire caught {a.Name}");
        }

        public override void Update(float dt)
        {
            if (beam != null) beam.Visible = Armed && Now >= rearmAt;
            if (victim == null) return;
            if (!victim.Alive) { victim = null; rearmAt = Now + 2f; return; }
            victim.Reveal(0.3f);
            if (Now - caughtAt >= 0.7f)
            {
                // Not broken in time: tethered — held, dazed, a small hit.
                victim.Hold(0.15f);
                victim.Daze(0.3f);
                if (!dealt) { dealt = true; victim.TakeDamage(5f, this, Vector3.Up); SentinelSfx.PlayAt(G, "snap", victim.Body.Feet, 0.8f); }
            }
        }

        public override void Destroy()
        {
            victim = null;
            base.Destroy();
        }
    }

    // ------------------------------------------------------------------ Deadlock

    /// <summary>Sonic Sensor: listens to a 9 m (out from the wall) × 8 m area; running, shooting or loud noise inside → 0.5 s
    /// wind-up → concusses everyone in the area for 2.5 s. Walking through is silent. 20 HP; enemies inside see it.</summary>
    sealed class SonicSensorDev : Device
    {
        readonly Vector3 f, s;
        readonly float floorY;
        float fireAt = -1f;
        public SonicSensorDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Normal = p.Normal; Hp = MaxHp = 20f; DeployTime = 1.75f;
            f = new Vector3(p.Normal.X, 0, p.Normal.Z).Normalized();
            s = new Vector3(-f.Z, 0, f.X);
            floorY = Collision.Ground(new Vector3(Pos.X + f.X * 1.5f, Pos.Y + 0.3f, Pos.Z + f.Z * 1.5f), G.Solid);
            Node = SentinelFx.Gadget(G.World, "sensor", a.Color, Pos, p.Normal);
            var c = new Vector3(Pos.X, floorY + 1.25f, Pos.Z) + f * 4.5f;
            Keep(SentinelFx.Volume(G.World, c, new Basis(s, Vector3.Up, f), new Vector3(8f, 2.5f, 9f), a.Color, 0.07f));
        }
        public bool Inside(Vector3 feet)
        {
            var d = feet - Pos;
            float along = d.X * f.X + d.Z * f.Z, side = d.X * s.X + d.Z * s.Z;
            return along >= 0 && along <= 9f && Mathf.Abs(side) <= 4f && feet.Y > floorY - 1f && feet.Y < floorY + 2.5f;
        }
        public override float SeenRange => 12f;
        public override bool Spottable(Attacker a) => Inside(a.Body.Feet);

        public override void Update(float dt)
        {
            if (!Armed) return;
            if (fireAt < 0)
            {
                var loud = M.attackers.FirstOrDefault(x => x.Alive && Inside(x.Body.Feet) && (x.Loud || x.Firing));
                if (loud != null) { fireAt = Now + 0.5f; SentinelSfx.PlayAt(G, "hum", Pos, 0.8f); M.Log($"sonic sensor heard {loud.Name} ({(loud.Firing ? "gunfire" : "running")})"); }
                return;
            }
            if (Now < fireAt) return;
            int n = 0;
            foreach (var x in M.attackers)
            {
                if (!x.Alive || !Inside(x.Body.Feet)) continue;
                x.Concuss(2.5f);
                M.Caught(x, this);
                n++;
            }
            SentinelSfx.PlayAt(G, "concuss", Pos + f * 4f, 1f);
            Trigger(n);
            Finish();
        }
    }

    /// <summary>One orb of a Barrier Mesh (centre 680 → 1200 HP, small 320 → 480 HP once fortified 3 s after landing).</summary>
    sealed class MeshOrbDev : Device
    {
        public readonly bool Center;
        readonly float baseHp;
        bool fortified;
        public MeshOrbDev(AnchorMode m, SentinelAbility a, Vector3 pos, bool center) : base(m, a)
        {
            Pos = pos; Center = center; DeployTime = 1f;
            Hp = MaxHp = baseHp = center ? 680f : 320f;
            Node = SentinelFx.Gadget(G.World, "orb", a.Color, pos, Vector3.Up);
            if (!center) Node.Scale = Vector3.One * 0.7f;
        }
        public override float SeenRange => 40f;
        public override Vector3 AimPoint => Pos + new Vector3(0, 0.2f, 0);
        public override void Update(float dt)
        {
            if (!fortified && Now >= PlacedAt + 3f) { fortified = true; float add = (Center ? 1200f : 480f) - baseHp; Hp += add; MaxHp += add; }
        }
    }

    /// <summary>Barrier Mesh: walls grow from the landing point in four directions (≤ 5 m each, cut short by terrain) and block
    /// movement only (bullets and sight pass). Destroying a small orb drops its wall; the centre drops all. 30 s.</summary>
    sealed class BarrierMeshDev : Device
    {
        readonly MeshOrbDev center;
        readonly (MeshOrbDev Orb, Vector3 End, MeshInstance3D Wall)[] arms;
        public BarrierMeshDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; DeployTime = 1f;
            center = new MeshOrbDev(m, a, Pos, true);
            m.AddDevice(center);
            var f = new Vector3(p.Axis.X, 0, p.Axis.Z).Normalized();
            var sd = new Vector3(-f.Z, 0, f.X);
            var list = new List<(MeshOrbDev, Vector3, MeshInstance3D)>();
            foreach (var dir in new[] { f, -f, sd, -sd })
            {
                var o = Pos + new Vector3(0, 0.6f, 0);
                float len = Mathf.Min(5f, Collision.FirstHit(o, dir, G.Solid) - 0.2f);
                if (len < 0.8f) continue;
                var end = Pos + dir * len;
                end.Y = Collision.Ground(new Vector3(end.X, Pos.Y + 0.6f, end.Z), G.Solid);
                if (Mathf.Abs(end.Y - Pos.Y) > 1.2f) continue; // no floor there
                var orb = new MeshOrbDev(m, a, end, false);
                m.AddDevice(orb);
                list.Add((orb, end, Keep(SentinelFx.Slab(G.World, Pos, end, 2.2f, 0.08f, a.Color, 0.25f, 1.2f))));
            }
            arms = list.ToArray();
            M.Log($"barrier mesh: {arms.Length} walls");
        }
        public override bool Shootable => false;
        public override float SeenRange => 0f;

        bool ArmUp(int i) => !Dead && Now >= PlacedAt + DeployTime && !center.Dead && !arms[i].Orb.Dead;

        public override void Update(float dt)
        {
            if (Now >= PlacedAt + 30f || center.Dead) { center.Destroy(); foreach (var a in arms) a.Orb.Destroy(); Finish(); return; }
            for (int i = 0; i < arms.Length; i++)
                if (GodotObject.IsInstanceValid(arms[i].Wall)) arms[i].Wall.Visible = ArmUp(i);
        }

        public override bool BlocksMove(Vector3 a, Vector3 b, out Device part)
        {
            part = center;
            for (int i = 0; i < arms.Length; i++)
            {
                if (!ArmUp(i) || b.Y > Pos.Y + 1.5f || b.Y < Pos.Y - 1.2f) continue;
                if (!HitsWall(a, b, Pos, arms[i].End, 0.05f)) continue;
                part = arms[i].Orb;
                return true;
            }
            return false;
        }

        public override void Free()
        {
            base.Free();
        }
    }

    // ------------------------------------------------------------------ Vyse

    /// <summary>Razorvine: covert nest (agents spot it within 3.5 m). Reuse → 0.2 s → 6.25 m vines for 6 s: 15% slow and 10
    /// damage per 1.25 m moved inside.</summary>
    sealed class RazorvineDev : Device
    {
        float activeAt = -1f;
        readonly Dictionary<Attacker, (Vector3 Last, float Acc)> moved = new();
        readonly HashSet<Attacker> hit = new();
        public RazorvineDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 20f; DeployTime = 0.75f;
            Node = SentinelFx.Gadget(G.World, "nest", a.Color, Pos, Vector3.Up);
        }
        public bool Active => activeAt >= 0;
        public bool CanActivate => Armed && !Active;
        public override float SeenRange => Active ? 0f : 3.5f;
        public override bool Shootable => !Active;

        public void Activate()
        {
            if (!CanActivate) return;
            activeAt = Now + 0.2f;
            SentinelSfx.PlayAt(G, "vines", Pos, 1f);
            Keep(SentinelFx.Disc(G.World, Pos, 6.25f, A.Color, 0.3f));
            M.Log("razorvine activated");
        }

        public override void Update(float dt)
        {
            if (!Active || Now < activeAt) return;
            if (Now > activeAt + 6f)
            {
                Trigger(hit.Count);
                Finish();
                return;
            }
            foreach (var x in M.attackers)
            {
                if (!x.Alive) continue;
                var f = x.Body.Feet;
                if (new Vector2(f.X - Pos.X, f.Z - Pos.Z).Length() > 6.25f || Mathf.Abs(f.Y - Pos.Y) > 2f) { moved.Remove(x); continue; }
                x.Slow(0.85f, 0.15f);
                if (hit.Add(x)) M.Caught(x, this);
                var (last, acc) = moved.TryGetValue(x, out var v) ? v : (f, 0f);
                acc += new Vector2(f.X - last.X, f.Z - last.Z).Length();
                while (acc >= 1.25f) { acc -= 1.25f; x.TakeDamage(10f, this, Vector3.Up); SentinelSfx.PlayAt(G, "crunch", f, 0.7f); if (!x.Alive) break; }
                moved[x] = (f, acc);
            }
        }
    }

    /// <summary>Shear: wall trap. An enemy walking past it → 0.8 s → a solid wall (≤ 12 m long, 2.5 m tall, 1 m thick) for 6 s
    /// that blocks sight, bullets and movement, cutting them off from their team. Can't be destroyed.</summary>
    sealed class ShearDev : Device
    {
        readonly Vector3 a0, a1;
        float wallAt = -1f;
        SmokeVolume? vol;
        MeshInstance3D? slab;
        Attacker? first;
        public ShearDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Normal = p.Normal; DeployTime = 2f;
            a0 = new Vector3(p.Pos.X, p.End.Y, p.Pos.Z);
            a1 = p.End;
            Node = SentinelFx.Gadget(G.World, "sensor", a.Color, Pos, p.Normal);
            Node.Scale = new Vector3(1.2f, 0.6f, 1.2f);
        }
        public override bool Shootable => false;
        public override float SeenRange => 0f;
        bool Up => wallAt >= 0 && Now >= wallAt && Now < wallAt + 6f;

        public override void OnMoved(Attacker a, Vector3 from, Vector3 to)
        {
            if (!Armed || wallAt >= 0) return;
            if (!SegCross(from, to, a0, a1, out _)) return;
            first = a;
            wallAt = Now + 0.8f;
            SentinelSfx.PlayAt(G, "shear", (a0 + a1) / 2, 0.9f, pitch: 1.3f);
            M.Log($"shear tripped by {a.Name}");
        }

        public override void Update(float dt)
        {
            if (wallAt < 0) return;
            if (Now >= wallAt && slab == null)
            {
                slab = Keep(SentinelFx.Slab(G.World, a0, a1, 2.5f, 1f, A.Color, 0.92f, 0.5f));
                vol = SmokeVolume.Wall(new[] { a0, a1 }, 2.5f, wallAt, 6f, 0f, 0.15f, 0.3f, "Vyse · Shear");
                G.AddSmoke(vol);
                SentinelSfx.PlayAt(G, "shear", (a0 + a1) / 2, 1f);
                // Anyone standing in it is pushed out to the side they came from; count who got cut off.
                int split = 0;
                var dir = (a1 - a0); dir.Y = 0;
                var nrm = new Vector3(-dir.Z, 0, dir.X).Normalized();
                foreach (var x in M.attackers)
                {
                    if (!x.Alive) continue;
                    var f = x.Body.Feet;
                    float side = (f - a0).Dot(nrm);
                    if (SegDist(f, a0, a1) < 0.5f + Collision.PlayerRadius)
                    {
                        float sign = x.Velocity.Dot(nrm) > 0 ? -1 : 1;
                        x.Body.Feet = f + nrm * sign * (0.9f + Collision.PlayerRadius - Mathf.Abs(side));
                    }
                    if (x != first) split++;
                }
                if (first != null && first.Alive) M.Caught(first, this);
                Trigger(first != null ? 1 : 0);
                M.Log($"shear wall up ({split} cut off)");
            }
            if (slab != null && Now >= wallAt + 6f) Finish();
        }

        public override bool BlocksMove(Vector3 a, Vector3 b, out Device part)
        {
            part = this;
            return Up && HitsWall(a, b, a0, a1, 0.5f);
        }

        public override float BulletBlock(Vector3 o, Vector3 d) => Up ? RayWall(o, d, a0, a1, 2.5f, 1f) : float.PositiveInfinity;
    }

    // ------------------------------------------------------------------ Sage

    /// <summary>One Barrier Orb segment: 2.6 m wide, 400 HP → 600 HP fortified 2 s after the cast; blocks sight, bullets and
    /// movement. Height 3.5 m and thickness 0.6 m are estimates.</summary>
    sealed class SageSegDev : Device
    {
        public readonly Vector3 P0, P1;
        public const float Height = 3.5f, Thick = 0.6f;
        readonly float end;
        bool fortified;
        readonly SmokeVolume vol;
        readonly MeshInstance3D slab;
        public SageSegDev(AnchorMode m, SentinelAbility a, Vector3 p0, Vector3 p1, float castAt) : base(m, a)
        {
            P0 = p0; P1 = p1; Pos = (p0 + p1) / 2; DeployTime = 1.35f; PlacedAt = castAt;
            Hp = MaxHp = 400f;
            end = castAt + 40f;
            slab = Keep(SentinelFx.Slab(G.World, p0, p1, Height, Thick, a.Color.Lerp(new Color(0.9f, 1f, 1f), 0.35f), 0.95f, 0.35f));
            vol = SmokeVolume.Wall(new[] { p0, p1 }, Height, castAt + 0.4f, 40f - 0.4f, 0f, 0.9f, 0.2f, "Sage · Barrier Orb");
            G.AddSmoke(vol);
        }
        public override Vector3 AimPoint => Pos + new Vector3(0, 1.4f, 0);
        public override Vector3 AimFor(Vector3 from)
        {
            // the nearest point of the face toward the shooter
            var ab = P1 - P0; ab.Y = 0;
            float t = Mathf.Clamp((from - P0).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0.15f, 0.85f);
            return P0 + ab * t + new Vector3(0, 1.4f, 0);
        }
        public override float SeenRange => 40f;
        float Rise => Mathf.Clamp((Now - PlacedAt) / DeployTime, 0f, 1f);

        public override void Update(float dt)
        {
            if (!fortified && Now >= PlacedAt + 2f) { fortified = true; Hp += 200f; MaxHp = 600f; }
            if (GodotObject.IsInstanceValid(slab))
            {
                float r = Mathf.Max(0.02f, Rise);
                slab.Scale = new Vector3(1, r, 1);
                var p = slab.GlobalPosition; p.Y = Mathf.Min(P0.Y, P1.Y) + Height * r / 2; slab.GlobalPosition = p;
            }
            if (Now >= end) { vol.DownAt = Now; Finish(); }
        }

        public override bool BlocksMove(Vector3 a, Vector3 b, out Device part)
        {
            part = this;
            return Rise > 0.3f && b.Y < Pos.Y + Height - 0.5f && HitsWall(a, b, P0, P1, Thick / 2);
        }

        public override float BulletBlock(Vector3 o, Vector3 d) => Rise > 0.2f ? RayWall(o, d, P0, P1, Height * Rise, Thick) : float.PositiveInfinity;

        public override void Destroy()
        {
            vol.DownAt = Now;
            vol.FadeTime = 0.05f;
            SentinelSfx.PlayAt(G, "crystal", AimPoint, 1f, pitch: 0.7f);
            base.Destroy();
        }
    }

    /// <summary>Slow Orb field: 0.5 s after landing, 7 s, 50% slow; running in it makes a loud crunch. Radius 5.5 m (est.).</summary>
    sealed class SlowFieldDev : Device
    {
        readonly float radius, slow, life;
        readonly HashSet<Attacker> caught = new();
        readonly Dictionary<Attacker, float> crunchAt = new();
        MeshInstance3D? disc;
        public SlowFieldDev(AnchorMode m, SentinelAbility a, Vector3 at, float radius, float slow, float life, float windup) : base(m, a)
        {
            Pos = at; this.radius = radius; this.slow = slow; this.life = life; DeployTime = windup;
        }
        public override bool Shootable => false;
        public override float SeenRange => 0f;

        public override void Update(float dt)
        {
            if (Now < PlacedAt + DeployTime) return;
            if (disc == null) { disc = Keep(SentinelFx.Disc(G.World, Pos, radius, A.Color, 0.32f)); SentinelSfx.PlayAt(G, "crystal", Pos, 1f); }
            if (Now > PlacedAt + DeployTime + life) { Trigger(caught.Count); Finish(); return; }
            foreach (var x in M.attackers)
            {
                if (!x.Alive) continue;
                var f = x.Body.Feet;
                if (new Vector2(f.X - Pos.X, f.Z - Pos.Z).Length() > radius || Mathf.Abs(f.Y - Pos.Y) > 1.8f) continue;
                x.Slow(slow, 0.2f);
                if (caught.Add(x)) M.Caught(x, this);
                if (A.Kind == SentKind.SlowOrb && x.Speed > 1.2f && Now >= crunchAt.GetValueOrDefault(x, -1f))
                {
                    crunchAt[x] = Now + 0.35f;
                    SentinelSfx.PlayAt(G, "crunch", f, 1f);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Veto

    /// <summary>Chokehold: covert trap (agents spot it within 9 m). An enemy within 6.58 m in its sight → 0.5 s → everyone in the
    /// 6.58 m zone it can see is held in place and decayed (-75 HP, restored over 4.5 s) for 4.5 s. 20 HP.</summary>
    sealed class ChokeholdDev : Device
    {
        float fireAt = -1f, zoneUntil = -1f;
        readonly HashSet<Attacker> held = new();
        public ChokeholdDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 20f; DeployTime = 1.5f;
            Node = SentinelFx.Gadget(G.World, "choke", a.Color, Pos, Vector3.Up);
        }
        Vector3 Eye => Pos + new Vector3(0, 0.3f, 0);
        public override float SeenRange => 9f;
        public override bool Shootable => fireAt < 0;

        public override void Update(float dt)
        {
            if (!Armed) return;
            if (fireAt < 0)
            {
                var x = M.attackers.FirstOrDefault(a => a.Alive && a.Chest.DistanceTo(Eye) <= 6.58f && G.LineOfSight(Eye, a.Chest));
                if (x != null) { fireAt = Now + 0.5f; SentinelSfx.PlayAt(G, "choke", Pos, 1f); M.Log($"chokehold sprung by {x.Name}"); }
                return;
            }
            if (Now < fireAt) return;
            if (zoneUntil < 0)
            {
                zoneUntil = Now + 4.5f;
                Keep(SentinelFx.Disc(G.World, Pos, 6.58f, A.Color, 0.26f));
                foreach (var a in M.attackers)
                {
                    if (!a.Alive || a.Chest.DistanceTo(Eye) > 6.58f || !G.LineOfSight(Eye, a.Chest)) continue;
                    held.Add(a);
                    a.Decay(75f, 4.5f);
                    M.Caught(a, this);
                }
                Trigger(held.Count);
            }
            foreach (var a in held) if (a.Alive) { a.Hold(0.12f); a.Reveal(0.15f); }
            if (Now >= zoneUntil) Finish();
        }
    }

    /// <summary>Interceptor: sent 7 m ahead (shortened by terrain), floats. Reuse → 0.5 s → for 9 s it zaps enemy utility within
    /// 18 m that it can see (here: the attackers' flashes, destroyed before they pop). 50 HP (est.).</summary>
    sealed class InterceptorDev : Device
    {
        float activeAt = -1f;
        public int Zapped;
        public InterceptorDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 50f; DeployTime = 0.85f;
            Node = SentinelFx.Gadget(G.World, "interceptor", a.Color, Pos, Vector3.Up);
        }
        public override Vector3 AimPoint => Pos;
        public override float SeenRange => 25f;
        public bool Active => !Dead && !Spent && activeAt >= 0 && Now >= activeAt && Now < activeAt + 9f;
        public bool CanActivate => Armed && activeAt < 0;
        public void Activate()
        {
            if (!CanActivate) return;
            activeAt = Now + 0.5f;
            SentinelSfx.PlayAt(G, "hum", Pos, 1f, pitch: 1.6f);
            M.Log("interceptor on");
        }
        public bool TryZap(Vector3 p)
        {
            if (!Active || p.DistanceTo(Pos) > 18f || !G.LineOfSight(Pos, p)) return false;
            Zapped++;
            M.Tracer(Pos, p, A.Color, 0.15f, 0.03f);
            SentinelSfx.PlayAt(G, "beam", Pos, 1f);
            return true;
        }
        public override void Update(float dt)
        {
            if (Node != null)
            {
                Node.GlobalPosition = Pos + new Vector3(0, 0.08f * Mathf.Sin(Now * 3f), 0);
                Node.RotateY(dt * (Active ? 6f : 1f));
            }
            if (activeAt >= 0 && Now >= activeAt + 9f) { Trigger(Zapped); Finish(); }
        }
    }

    // ------------------------------------------------------------------ Chamber

    /// <summary>Trademark: visible floor trap. A visible enemy within 10 m for 0.9 s (losing sight cancels) gets tagged: a 6 m
    /// slowing field under them for 4 s (slow amount 40%, est.). 20 HP.</summary>
    sealed class TrademarkDev : Device
    {
        Attacker? target;
        float seenFor;
        public TrademarkDev(AnchorMode m, SentinelAbility a, Placement p) : base(m, a)
        {
            Pos = p.Pos; Hp = MaxHp = 20f; DeployTime = 2f;
            Node = SentinelFx.Gadget(G.World, "trademark", a.Color, Pos, Vector3.Up);
        }
        Vector3 Eye => Pos + new Vector3(0, 0.25f, 0);
        public override float SeenRange => 22f;
        public override Vector3 AimPoint => Pos + new Vector3(0, 0.12f, 0);

        public override void Update(float dt)
        {
            if (!Armed) return;
            if (target == null || !target.Alive || target.Chest.DistanceTo(Eye) > 10f || !G.LineOfSight(Eye, target.Chest))
            {
                var nt = M.attackers.Where(x => x.Alive && x.Chest.DistanceTo(Eye) <= 10f && G.LineOfSight(Eye, x.Chest)).OrderBy(x => x.Chest.DistanceTo(Eye)).FirstOrDefault();
                if (nt != target) { target = nt; seenFor = 0f; if (nt != null) SentinelSfx.PlayAt(G, "charge", Pos, 0.9f); }
                if (target == null) return;
            }
            seenFor += dt;
            if (seenFor < 0.9f) return;
            var at = target.Body.Feet;
            at.Y = Collision.Ground(at + new Vector3(0, 0.3f, 0), G.Solid);
            M.Tracer(Eye, target.Chest, A.Color, 0.12f, 0.02f);
            M.AddDevice(new SlowFieldDev(M, A, at, 6f, 0.6f, 4f, 0f));
            M.Log($"trademark tagged {target.Name}");
            Trigger(1);
            Finish();
        }
    }
}
