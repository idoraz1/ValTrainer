using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Fx;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>Site Anchor: placing the sentinel setup from the crosshair (indicator, validity rules from the wiki: floor /
/// wall, max distances, wire span), thrown pieces' flight, and building the devices.</summary>
public sealed partial class AnchorMode
{
    const float ThrowSpeed = 19f, LobSpeed = 9f, ThrowGravity = 16f;

    void WireKit(SentinelKit k)
    {
        k.OnEquip = me => { foreach (var o in kits) if (o != me) o.Drop(); };
        k.Place = alt =>
        {
            var p = Compute(k.A, G.View.Eye, G.View.Forward, alt);
            if (!p.Ok) { k.Refuse(p.Why ?? "Can't place it there"); SentinelSfx.Play(G, "reuse", 0.4f, 0.6f); return false; }
            Commit(k.A, p);
            return true;
        };
        if (k.A.Kind == SentKind.Razorvine)
        {
            k.CanReuse = () => devices.OfType<RazorvineDev>().Any(v => v.CanActivate);
            k.Reuse = () => { foreach (var v in devices.OfType<RazorvineDev>().Where(v => v.CanActivate)) v.Activate(); SentinelSfx.Play(G, "reuse", 0.6f); };
        }
        if (k.A.Kind == SentKind.Interceptor)
        {
            k.CanReuse = () => devices.OfType<InterceptorDev>().Any(v => v.CanActivate);
            k.Reuse = () => { foreach (var v in devices.OfType<InterceptorDev>().Where(v => v.CanActivate)) v.Activate(); SentinelSfx.Play(G, "reuse", 0.6f); };
        }
        if (k.A.Persistent)
        {
            k.CanRecall = () => memory.Any(m => m.A == k.A);
            k.Recall = () => RecallOldest(k.A);
        }
    }

    void RecallOldest(SentinelAbility a)
    {
        int i = memory.FindIndex(m => m.A == a);
        if (i < 0) return;
        var p = memory[i].P;
        memory.RemoveAt(i);
        foreach (var d in devices.Where(d => d.A == a && d.From == p).ToList()) { d.Free(); devices.Remove(d); }
        SentinelSfx.Play(G, "place", 0.5f, 0.8f);
    }

    /// <summary>Puts a piece down (thrown pieces fly first) and remembers persistent ones for the next rounds.</summary>
    void Commit(SentinelAbility a, Placement p)
    {
        Event("util_place", (int)a.Kind, phase == Phase.Setup ? 0 : 1);
        if (a.Place == SentPlace.Thrown && p.Arc is { Count: > 1 })
        {
            var ball = new Node3D { Name = "SentinelThrow" };
            G.World.AddChild(ball);
            SentinelFx.Sphere(ball, 0.09f, Vector3.Zero, SentinelFx.Mat(a.Color, 1.5f));
            ball.GlobalPosition = p.Arc[0];
            throws.Add(new Throw { A = a, P = p, Ball = ball, T = 0, Dur = (p.Arc.Count - 1) / 60f });
            SentinelSfx.Play(G, "place", 0.5f, 1.2f);
        }
        else
        {
            var d = CreateDevice(a, p);
            if (d != null && a.Persistent) d.From = p;
            SentinelSfx.Play(G, "place", 0.7f);
            SentinelSfx.PlayAt(G, "deploy", p.Pos, 0.5f);
        }
        if (a.Persistent) memory.Add((a, p));
        Log($"placed {a.Ability} at {p.Pos}{(a.Place == SentPlace.Wall ? $" n {p.Normal}" : "")}");
    }

    sealed class Throw { public SentinelAbility A = null!; public Placement P = null!; public Node3D Ball = null!; public float T, Dur; }
    readonly List<Throw> throws = new();

    void UpdateThrows(float dt)
    {
        for (int i = throws.Count - 1; i >= 0; i--)
        {
            var t = throws[i];
            t.T += dt;
            var arc = t.P.Arc!;
            float f = Mathf.Clamp(t.T / Mathf.Max(0.01f, t.Dur), 0, 1) * (arc.Count - 1);
            int j = Math.Min((int)f, arc.Count - 2);
            if (GodotObject.IsInstanceValid(t.Ball)) t.Ball.GlobalPosition = arc[j].Lerp(arc[j + 1], f - j);
            if (t.T < t.Dur) continue;
            if (GodotObject.IsInstanceValid(t.Ball)) t.Ball.QueueFree();
            throws.RemoveAt(i);
            var d = CreateDevice(t.A, t.P);
            if (d != null && t.A.Persistent && memory.Any(m => m.P == t.P)) d.From = t.P;
            SentinelSfx.PlayAt(G, "place", t.P.Pos, 0.8f);
        }
    }

    Device? CreateDevice(SentinelAbility a, Placement p)
    {
        Device? d = a.Kind switch
        {
            SentKind.Alarmbot => new AlarmbotDev(this, a, p),
            SentKind.Turret => new TurretDev(this, a, p),
            SentKind.Trapwire => new TrapwireDev(this, a, p),
            SentKind.SonicSensor => new SonicSensorDev(this, a, p),
            SentKind.BarrierMesh => new BarrierMeshDev(this, a, p),
            SentKind.Razorvine => new RazorvineDev(this, a, p),
            SentKind.Shear => new ShearDev(this, a, p),
            SentKind.SlowOrb => new SlowFieldDev(this, a, p.Pos, 5.5f, 0.5f, 7f, 0.5f),
            SentKind.Chokehold => new ChokeholdDev(this, a, p),
            SentKind.Interceptor => new InterceptorDev(this, a, p),
            SentKind.Trademark => new TrademarkDev(this, a, p),
            SentKind.BarrierOrb => null,
            _ => null,
        };
        if (a.Kind == SentKind.BarrierOrb)
        {
            // Four 2.6 m segments centred on the aim point; a segment needs floor under it and free space.
            var axis = new Vector3(p.Axis.X, 0, p.Axis.Z).Normalized();
            int made = 0;
            for (int i = 0; i < 4; i++)
            {
                float a0 = -5.2f + i * 2.6f;
                var s0 = p.Pos + axis * a0;
                var s1 = p.Pos + axis * (a0 + 2.6f);
                var mid = (s0 + s1) / 2;
                float gy = Collision.Ground(new Vector3(mid.X, p.Pos.Y + 0.7f, mid.Z), G.Solid);
                if (Mathf.Abs(gy - p.Pos.Y) > 1.0f) continue;
                if (Collision.Blocked(p.Pos + Vector3.Up, new Vector3(mid.X, gy + 1f, mid.Z), G.Solid)) continue;
                s0.Y = s1.Y = gy;
                devices.Add(new SageSegDev(this, a, s0, s1, Now));
                made++;
            }
            SentinelSfx.PlayAt(G, "barrier", p.Pos, 1f);
            Log($"barrier orb: {made} segments");
            return null;
        }
        if (d != null) devices.Add(d);
        return d;
    }

    // ------------------------------------------------------------------ placement rules

    Placement Compute(SentinelAbility a, Vector3 eye, Vector3 dir, bool alt)
    {
        dir = dir.Normalized();
        var p = new Placement();
        var flat = new Vector3(dir.X, 0, dir.Z);
        flat = flat.LengthSquared() < 1e-4f ? Vector3.Forward : flat.Normalized();
        switch (a.Place)
        {
            case SentPlace.Floor:
            {
                float t = Collision.FirstHit(eye, dir, G.Solid, out int idx);
                Vector3 hit;
                bool floorHit = false;
                if (t <= a.Range) { hit = eye + dir * t; floorHit = G.Solid[idx].NormalAt(hit).Y > 0.7f; }
                else hit = eye + dir * a.Range;
                var q = floorHit ? hit : hit - flat * 0.35f;
                float gy = floorHit ? hit.Y : Collision.Ground(new Vector3(q.X, q.Y + 0.05f, q.Z), G.Solid, 0.05f);
                p.Pos = new Vector3(q.X, gy, q.Z);
                p.Normal = Vector3.Up;
                p.Axis = a.Kind == SentKind.BarrierOrb ? (alt ? flat : new Vector3(-flat.Z, 0, flat.X)) : flat;
                var feet = PlayerFeet;
                float reach = new Vector2(p.Pos.X - feet.X, p.Pos.Z - feet.Z).Length();
                if (q.Y - gy > 6f) p.Why = "No floor there";
                else if (reach > a.Range + 0.6f) p.Why = $"Too far (max {a.Range:0.#} m)";
                else if (Collision.Overlaps(p.Pos, G.Solid, 0.12f)) p.Why = "Not enough room";
                else p.Ok = true;
                break;
            }
            case SentPlace.Wall:
            {
                float t = Collision.FirstHit(eye, dir, G.Solid, out int idx);
                if (t > a.Range) { p.Pos = eye + dir * Mathf.Min(t, a.Range); p.Normal = -dir; p.Why = $"Aim at a wall within {a.Range:0} m"; break; }
                var hit = eye + dir * t;
                var n = G.Solid[idx].NormalAt(hit);
                p.Pos = hit; p.Normal = n;
                if (Mathf.Abs(n.Y) > 0.3f) { p.Why = "Aim at a wall"; break; }
                float floor = Collision.Ground(new Vector3(hit.X + n.X * 0.4f, hit.Y + 0.05f, hit.Z + n.Z * 0.4f), G.Solid, 0.05f);
                switch (a.Kind)
                {
                    case SentKind.Trapwire:
                    {
                        float y = Mathf.Clamp(hit.Y, floor + 0.15f, floor + 1.3f);
                        var s = new Vector3(hit.X, y, hit.Z) + n * 0.02f;
                        float span = Collision.FirstHit(s, n, G.Solid);
                        p.Pos = s;
                        if (span > 15f) { p.End = s + n * 15f; p.Why = "No wall across within 15 m"; break; }
                        if (span < 0.6f) { p.End = s + n * span; p.Why = "Too narrow"; break; }
                        p.End = s + n * span;
                        p.Ok = true;
                        break;
                    }
                    case SentKind.Shear:
                    {
                        var s = new Vector3(hit.X, floor, hit.Z) + n * 0.02f;
                        float span = Collision.FirstHit(s + Vector3.Up, n, G.Solid);
                        float len = Mathf.Min(12f, span);
                        p.End = s + n * len;
                        p.End = new Vector3(p.End.X, Collision.Ground(p.End + new Vector3(0, 0.5f, 0), G.Solid), p.End.Z);
                        p.Ok = len >= 1f;
                        if (!p.Ok) p.Why = "No room for the wall";
                        break;
                    }
                    case SentKind.SonicSensor:
                        if (hit.Y - PlayerFeet.Y > 4.5f) { p.Why = "Too high (max 4.5 m above you)"; break; }
                        p.Ok = true;
                        break;
                    default:
                        p.Ok = true;
                        break;
                }
                break;
            }
            case SentPlace.Ahead:
            {
                float t = Collision.FirstHit(eye, dir, G.Solid);
                float d = t < a.Range ? Mathf.Max(0.6f, t - 0.4f) : a.Range;
                p.Pos = eye + dir * d;
                p.Normal = Vector3.Up;
                p.Ok = true;
                break;
            }
            case SentPlace.Thrown:
            {
                p.Arc = ThrowArc(eye + dir * 0.4f + Vector3.Down * 0.15f, dir * (alt ? LobSpeed : ThrowSpeed) + Vector3.Up * 1.5f, out var land, out bool ok);
                p.Pos = land;
                p.Normal = Vector3.Up;
                p.Axis = flat;
                p.Ok = ok;
                if (!ok) p.Why = "It wouldn't land anywhere";
                break;
            }
        }
        return p;
    }

    /// <summary>Simple projectile: gravity, bounces off walls (losing most of its speed), lands on the first floor it touches.</summary>
    List<Vector3> ThrowArc(Vector3 from, Vector3 v, out Vector3 land, out bool ok)
    {
        var pts = new List<Vector3> { from };
        var p = from;
        const float dt = 1f / 60f;
        ok = false;
        land = from;
        for (int i = 0; i < 240; i++)
        {
            v.Y -= ThrowGravity * dt;
            var step = v * dt;
            float len = step.Length();
            float t = Collision.FirstHit(p, step / len, G.Solid, out int idx);
            if (t <= len)
            {
                var hit = p + step / len * Mathf.Max(0, t - 0.02f);
                var n = G.Solid[idx].NormalAt(p + step / len * t);
                pts.Add(hit);
                if (n.Y > 0.7f) { land = hit + Vector3.Down * 0.02f; ok = true; return pts; }
                v = (v - 2 * v.Dot(n) * n) * 0.35f;
                p = hit;
                continue;
            }
            p += step;
            pts.Add(p);
            if (p.Y < -5f) return pts;
        }
        return pts;
    }

    // ------------------------------------------------------------------ indicator

    void UpdatePreview()
    {
        if (preview == null) return;
        var k = kits.FirstOrDefault(x => x.InHand);
        if (k == null || k.A.Place == SentPlace.Thrown || G.Player.Dead) { preview.HideAll(); return; }
        var p = Compute(k.A, G.View.Eye, G.View.Forward, k.Alt);
        switch (k.A.Kind)
        {
            case SentKind.Trapwire: preview.Show(p.Pos, p.Normal, p.Ok, p.End); break;
            case SentKind.Shear: preview.Show(p.Pos, p.Normal, p.Ok, p.End, 0.3f); break;
            case SentKind.BarrierOrb:
                var ax = p.Axis.Normalized();
                preview.Show(p.Pos - ax * 5.2f, Vector3.Up, p.Ok, p.Pos + ax * 5.2f, 0.3f);
                break;
            case SentKind.Turret: preview.Show(p.Pos, Vector3.Up, p.Ok, null, 0.04f, p.Axis); break;
            default: preview.Show(p.Pos, p.Normal.LengthSquared() > 0.5f ? p.Normal : Vector3.Up, p.Ok); break;
        }
    }
}
