using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// DEV-ONLY auto-setup for headless validation ("--dev --autosetup", usually with "--simaim good"): the simulated player
/// only aims and shoots, so each round this puts the agent's setup at sensible spots around the choke (wires across it,
/// sensor and shear on its walls, alarmbot / trap / nest just inside, turret beside you facing it, Sage's wall across it),
/// activates reuse pieces when the attackers reach them (Razorvine) or their flash flies (Interceptor), throws Sage's slow
/// at the entry, and cuts the setup phase short. Placement ranges are not enforced here.
/// </summary>
public sealed partial class AnchorMode
{
    static readonly bool DevAuto = CmdLine.Dev && CmdLine.Has("--autosetup");
    bool devSlowThrown;

    Vector3 Side => new(-toSite.Z, 0, toSite.X);

    Vector3 FloorAt(Vector3 p)
    {
        int n = nav.Nearest(p);
        var q = n >= 0 && nav.Pos[n].DistanceTo(p) < 1.5f ? nav.Pos[n] : p;
        return new Vector3(q.X, Collision.Ground(new Vector3(q.X, MapSpot.Choke.Y + 1.2f, q.Z), G.Solid), q.Z);
    }

    /// <summary>The walls left and right of a point (rays across the corridor at <paramref name="h"/> m).</summary>
    bool Walls(Vector3 at, float h, out Vector3 left, out Vector3 right) => Walls(at, h, out left, out right, out _);

    /// <summary>The narrowest wall-to-wall span through a point, roughly across the attack direction (searched ±50°):
    /// <paramref name="dir"/> points from <paramref name="left"/> to <paramref name="right"/>.</summary>
    bool Walls(Vector3 at, float h, out Vector3 left, out Vector3 right, out Vector3 dir)
    {
        var o = new Vector3(at.X, FloorAt(at).Y + h, at.Z);
        float best = float.MaxValue;
        left = right = o; dir = Side;
        for (int deg = -50; deg <= 50; deg += 10)
        {
            var sd = Side.Rotated(Vector3.Up, Mathf.DegToRad(deg));
            float tl = Collision.FirstHit(o, -sd, G.Solid), tr = Collision.FirstHit(o, sd, G.Solid);
            if (tl + tr >= best || tl > 15f || tr > 15f) continue;
            best = tl + tr;
            left = o - sd * tl; right = o + sd * tr; dir = sd;
        }
        return best < 15f;
    }

    /// <summary>Floor spot near <paramref name="want"/> that the attackers can't see from their stack (covert pieces).</summary>
    Vector3 Hidden(Vector3 want)
    {
        var stackEyes = Enumerable.Range(0, 4).Select(i => approachPts[Math.Max(0, lastHidden - 2 - 3 * i)] + new Vector3(0, PlayerView.EyeHeight, 0)).ToArray();
        Vector3? best = null;
        float bestD = float.MaxValue;
        foreach (var k in new[] { 0f, 1f, 2f, 3f })
            foreach (var sd in new[] { 0f, 1.5f, -1.5f, 3f, -3f })
            {
                var p = FloorAt(want + toSite * k + Side * sd);
                if (Collision.Overlaps(p, G.Solid, 0.15f)) continue;
                if (stackEyes.Any(e => !Collision.Blocked(e, p + new Vector3(0, 0.3f, 0), G.Solid))) continue;
                float d = p.DistanceTo(want);
                if (d < bestD) { bestD = d; best = p; }
            }
        return best ?? FloorAt(want);
    }

    Placement? DevPlacement(SentinelAbility a, int nth)
    {
        var c = MapSpot.Choke;
        var s = toSite;
        var p = new Placement { Ok = true, Normal = Vector3.Up, Axis = s };
        switch (a.Kind)
        {
            case SentKind.Alarmbot: p.Pos = Hidden(c + s * 2.5f); break;
            case SentKind.Trademark: p.Pos = Hidden(c + s * 3.5f + Side * 1.2f); break;
            case SentKind.Chokehold: p.Pos = Hidden(c + s * 2.5f); break;
            case SentKind.Razorvine: p.Pos = FloorAt(c + s * 2.5f); break;
            case SentKind.BarrierMesh: p.Pos = FloorAt(c + s * 1.5f); break;
            case SentKind.BarrierOrb:
            {
                // Across the entry, centred between its walls.
                var at = c + s * 1.2f;
                if (Walls(at, 1f, out var l, out var r, out var wd)) { p.Pos = FloorAt((l + r) / 2); p.Axis = wd; }
                else { p.Pos = FloorAt(at); p.Axis = Side; }
                break;
            }
            case SentKind.Turret:
            {
                var toChoke = new Vector3(c.X - SpawnFeet.X, 0, c.Z - SpawnFeet.Z).Normalized();
                var side = new Vector3(-toChoke.Z, 0, toChoke.X);
                p.Pos = SpawnFeet + toChoke * 1.2f + side * 1.0f;
                p.Pos = new Vector3(p.Pos.X, Collision.Ground(p.Pos + new Vector3(0, 0.6f, 0), G.Solid), p.Pos.Z);
                if (Collision.Overlaps(p.Pos, G.Solid, 0.12f)) p.Pos = SpawnFeet + toChoke * 1.2f;
                p.Axis = toChoke;
                break;
            }
            case SentKind.Interceptor:
            {
                var eye = SpawnFeet + new Vector3(0, PlayerView.EyeHeight, 0);
                var to = (c + new Vector3(0, 1.4f, 0) - eye);
                var side = new Vector3(-to.Z, 0, to.X).Normalized();
                p.Pos = eye + to.Normalized() * Mathf.Min(5f, to.Length() * 0.3f) + side * 1.2f + Vector3.Down * 0.4f;
                if (Collision.Blocked(eye, p.Pos, G.Solid)) p.Pos = eye + to.Normalized() * Mathf.Min(5f, to.Length() * 0.3f) + Vector3.Down * 0.4f;
                break;
            }
            case SentKind.Trapwire:
            {
                var at = c + s * (nth == 0 ? 0.8f : 3.5f);
                if (!Walls(at, 0.5f, out var l, out var r, out var wd)) { at = c + s * (nth == 0 ? -1.2f : 1.8f); if (!Walls(at, 0.5f, out l, out r, out wd)) return null; }
                p.Pos = l + wd * 0.02f; p.Normal = wd; p.End = r;
                break;
            }
            case SentKind.SonicSensor:
            case SentKind.Shear:
            {
                var at = c + s * (a.Kind == SentKind.Shear ? 2.2f : 1.0f);
                if (!Walls(at, a.Kind == SentKind.Shear ? 1f : 1.3f, out var l, out var r, out var wd) && !Walls(at = c + s * 0.5f, 1f, out l, out r, out wd)) return null;
                bool useLeft = l.DistanceTo(at) <= r.DistanceTo(at);
                p.Pos = useLeft ? l : r;
                p.Normal = useLeft ? wd : -wd;
                if (a.Kind == SentKind.Shear)
                {
                    var floor = FloorAt(p.Pos + p.Normal * 0.4f).Y;
                    p.Pos = new Vector3(p.Pos.X, floor, p.Pos.Z) + p.Normal * 0.02f;
                    float span = Mathf.Min(12f, Collision.FirstHit(p.Pos + Vector3.Up, p.Normal, G.Solid));
                    p.End = p.Pos + p.Normal * span;
                }
                break;
            }
            case SentKind.SlowOrb: p.Pos = FloorAt(c + s * 0.8f); break;
        }
        return p;
    }

    void DevNewRound()
    {
        devSlowThrown = false;
        if (!DevAuto || kits.Count == 0) return;
        foreach (var k in kits)
        {
            if (k.A.Kind == SentKind.SlowOrb) continue; // thrown at the push
            int have = memory.Count(m => m.A == k.A);
            for (int i = have; i < k.A.Charges && k.Charges > 0; i++)
            {
                var p = DevPlacement(k.A, i);
                if (p == null) { Log($"auto-setup: no spot for {k.A.Ability}"); break; }
                Commit(k.A, p);
                k.Charges--;
            }
        }
        setupLen = Mathf.Min(setupLen, 2f);
    }

    void DevUpdate(float dt)
    {
        if (!DevAuto || phase != Phase.Live) return;
        foreach (var v in devices.OfType<RazorvineDev>())
            if (v.CanActivate && attackers.Any(a => a.Alive && a.Feet.DistanceTo(v.Pos) < 4.5f)) v.Activate();
        var slow = kits.FirstOrDefault(k => k.A.Kind == SentKind.SlowOrb);
        if (slow != null && !devSlowThrown && executing && attackers.Any(a => a.Alive && a.State == Attacker.St.Entry))
        {
            devSlowThrown = true;
            for (int i = 0; i < slow.Charges && i < 1; i++)
                if (DevPlacement(slow.A, 0) is { } p) { Commit(slow.A, p); slow.Charges--; }
        }
        // No flash this round: switch the Interceptor on when they swing anyway (it still zaps nothing).
        if (executing && orb == null && attackers.Any(a => a.Alive && a.State == Attacker.St.Entry))
            foreach (var ic in devices.OfType<InterceptorDev>()) if (ic.CanActivate && Now - liveStart > 30f) ic.Activate();
    }

    void DevFlashThrown()
    {
        if (!DevAuto) return;
        foreach (var ic in devices.OfType<InterceptorDev>()) if (ic.CanActivate) ic.Activate();
    }
}
