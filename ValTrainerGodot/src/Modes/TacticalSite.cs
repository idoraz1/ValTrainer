using System.Collections.Concurrent;
using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Maps;

namespace ValTrainer.Modes;

/// <summary>A post-plant position: where an attacker plays after the plant (feet on a nav node) and its peek node.</summary>
public sealed record PostSpot(string Kind, string Name, Vector3 Feet, bool Crouch, Vector3 Peek);

/// <summary>
/// Geometry for the post-plant / retake drills on one map and plant spot, computed once from the map blockout and its
/// walkable grid (<see cref="BotNav"/>) and cached for the process:
/// <list type="bullet">
/// <item>the spike (the map's <see cref="MapSpot.PlantSpots"/>, or derived: the site-level defender spot in the middle of
/// the site, moved onto open floor);</item>
/// <item>the retake entries (<see cref="MapSpot.RetakeEntries"/>, or derived: walkable points 15–55 m from the spike, closer
/// to it than to the attackers' start, out of sight of the site, spread apart);</item>
/// <item>each entry's route to the spike, its "stage" point (the last spot on the route the site can't see, where a retake
/// gathers before swinging) and clear points (open floor 3.5–10 m from the spike that sees a kneeling defuser);</item>
/// <item>post-plant spots in three kinds — <b>On site</b> (close, next to cover), <b>Off-angle</b> (long range from the
/// attackers' side) and <b>Crossfire</b> (a different angle on the spike) — every one sees the defuser, is hidden from the
/// entries and is scored on how little of the retake routes it is exposed to; plus <b>Lurk</b> spots that don't see the
/// spike but are a step from a peek that does (play off the defuse sound).</item>
/// </list>
/// Everything also works on a map that has no plant / entry data (derived), e.g. a placeholder layout.
/// </summary>
public sealed class TacticalSite
{
    public const float DefuserHeadY = 1.0f;

    public readonly string MapKey;
    public readonly Vector3 Spike;
    public readonly Vector3[] Entries;
    public readonly List<Vector3>[] Routes;
    public readonly Vector3[] Stages;
    public readonly Vector3[] ClearPoints;
    /// <summary>Post-plant spots by kind: [0] On site, [1] Off-angle, [2] Crossfire (each best-first, may be empty).</summary>
    public readonly PostSpot[][] Spots;
    public readonly PostSpot[] Lurks;
    public readonly bool PlantDerived, EntriesDerived;
    public readonly float BuildMs;
    public readonly List<string> Warnings = new();

    public static readonly string[] Kinds = { "On site", "Off-angle", "Crossfire" };

    static readonly ConcurrentDictionary<string, TacticalSite> Cache = new();
    static readonly ConcurrentDictionary<string, Vector3[]> PlantCache = new();

    /// <summary>The plant spots of a map (its own, or one derived spot), snapped onto walkable floor.</summary>
    public static Vector3[] PlantSpotsFor(MapSpot map, BotNav nav, IReadOnlyList<Box> solid) =>
        PlantCache.GetOrAdd($"{map.Key}:{solid.Count}", _ =>
        {
            if (map.PlantSpots.Length > 0) return map.PlantSpots.Select(p => Snap(nav, p, solid)).ToArray();
            return new[] { DerivePlant(map, nav, solid) };
        });

    public static TacticalSite For(MapSpot map, BotNav nav, IReadOnlyList<Box> solid, int plantIndex)
    {
        var plants = PlantSpotsFor(map, nav, solid);
        plantIndex = Math.Clamp(plantIndex, 0, plants.Length - 1);
        return Cache.GetOrAdd($"{map.Key}:{solid.Count}:{plantIndex}", _ => new TacticalSite(map, nav, solid, plants[plantIndex], map.PlantSpots.Length == 0));
    }

    TacticalSite(MapSpot map, BotNav nav, IReadOnlyList<Box> world, Vector3 spike, bool plantDerived)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var solid = world.ToArray();
        MapKey = map.Key;
        Spike = spike;
        PlantDerived = plantDerived;
        var heads = DefuserHeads(spike);
        bool SeesSpike(Vector3 eye, int need = 2)
        {
            int n = 0;
            for (int i = 0; i < heads.Length; i++)
                if (!Blocked(eye, heads[i], solid) && ++n >= need) return true;
            return false;
        }

        var geoSpike = Geo(nav, nav.Nearest(spike));
        // The attackers' side: their start if it's on this grid, else where they come onto the site (the choke).
        int atk = nav.Nearest(map.StartFeet);
        if (atk < 0 || nav.Pos[atk].DistanceTo(map.StartFeet) > 3f) atk = nav.Nearest(map.Choke);
        var geoStart = Geo(nav, atk);

        // ---- entries ----
        if (map.RetakeEntries.Length > 0)
        {
            Entries = map.RetakeEntries.Select(p => Snap(nav, p, solid)).ToArray();
            foreach (var e in Entries)
                if (SeesSpike(Eye(e), 1)) Warnings.Add($"entry {e} sees the spike");
        }
        else
        {
            EntriesDerived = true;
            Entries = DeriveEntries(nav, solid, spike, geoSpike, geoStart, SeesSpike);
        }

        // ---- routes and stage points ----
        Routes = new List<Vector3>[Entries.Length];
        Stages = new Vector3[Entries.Length];
        for (int i = 0; i < Entries.Length; i++)
        {
            var path = nav.FindPath(Entries[i], spike) ?? new List<Vector3> { spike };
            path.Insert(0, Entries[i]);
            Routes[i] = path;
            var pts = Sample(path, 0.5f);
            int lastHidden = 0;
            for (int k = 0; k < pts.Count; k++)
            {
                if (SeesSpike(Eye(pts[k]), 1) || pts[k].DistanceTo(spike) < 6f) break;
                lastHidden = k;
            }
            Stages[i] = pts[Math.Max(0, lastHidden - 2)];
        }

        // ---- clear points: open floor near the spike that sees a kneeling defuser, spread around it ----
        var clear = new List<Vector3>();
        var cand = new List<int>();
        for (int n = 0; n < nav.Count; n++)
        {
            var p = nav.Pos[n];
            float d = H(p, spike);
            if (d < 3.5f || d > 10f || Mathf.Abs(p.Y - spike.Y) > 1.5f || nav.Degree(n) < 8) continue;
            if (!SeesSpike(Eye(p), 3)) continue;
            cand.Add(n);
        }
        // farthest-point sampling by angle around the spike
        if (cand.Count > 0)
        {
            clear.Add(nav.Pos[cand[cand.Count / 2]]);
            while (clear.Count < 5)
            {
                int best = -1; float bd = -1;
                foreach (var n in cand)
                {
                    float md = clear.Min(c => AngleAround(spike, c, nav.Pos[n]));
                    if (md > bd) { bd = md; best = n; }
                }
                if (best < 0 || bd < 35f) break;
                clear.Add(nav.Pos[best]);
            }
        }
        if (clear.Count == 0) clear.Add(spike + new Vector3(0, 0, 0));
        ClearPoints = clear.ToArray();

        // ---- post-plant spots ----
        var routePts = new List<Vector3>();
        foreach (var r in Routes)
            foreach (var p in Sample(r, 2.5f))
                if (H(p, spike) > 7f && routePts.All(q => q.DistanceTo(Eye(p)) > 1.5f)) routePts.Add(Eye(p));
        var entryEyes = Entries.Select(Eye).ToArray();
        var cands = new List<Cand>();
        var lurkCands = new List<Cand>();
        float startGeo = geoSpike[atk];
        for (int n = 0; n < nav.Count; n++)
        {
            var p = nav.Pos[n];
            // every other column in x and z: 1 m spacing is plenty for positions (and 4× cheaper)
            int ix = (int)Mathf.Floor((p.X - nav.X0) / BotNav.Cell), iz = (int)Mathf.Floor((p.Z - nav.Z0) / BotNav.Cell);
            if ((ix & 1) != 0 || (iz & 1) != 0) continue;
            float d = H(p, spike);
            if (d < 3f || d > 32f || Mathf.Abs(p.Y - spike.Y) > 5f) continue;
            bool nearEntry = false;
            foreach (var e in Entries) if (e.DistanceTo(p) < 4f) { nearEntry = true; break; }
            if (nearEntry || Collision.Overlaps(p, solid, 0.4f)) continue; // keep a body's width from walls
            var eye = Eye(p);
            bool sees = SeesSpike(eye);
            if (!sees && (d < 6f || d > 20f)) continue;
            var chest = p + new Vector3(0, 1.1f, 0);
            bool seen = false;
            foreach (var e in entryEyes) if (!Blocked(e, eye, solid) || !Blocked(e, chest, solid)) { seen = true; break; }
            if (seen) continue;
            int vis = 0;
            foreach (var r in routePts) if (!Blocked(r, eye, solid)) vis++;
            float exp = routePts.Count == 0 ? 0 : vis / (float)routePts.Count;
            var c = new Cand(n, p, d, exp, nav.Degree(n) < 8, geoStart[n], 0);
            if (sees) cands.Add(c); else lurkCands.Add(c);
        }
        var r0 = new Random(map.Key.GetHashCode() ^ (int)(spike.X * 100));
        Spots = new PostSpot[3][];
        // On site: close to the spike, against cover, little exposure.
        Spots[0] = Pick(cands.Where(c => c.D is >= 3.5f and <= 9f), c => -c.Exp * 3f + (c.Cover ? 0.8f : 0f) - c.D * 0.05f, 0, map, nav, solid, spike, heads);
        // Off-angle: long range, on the attackers' side of the site (closer to their start than the retake comes from).
        Spots[1] = Pick(cands.Where(c => c.D is >= 12f and <= 32f && c.GeoStart < startGeo + 6f),
            c => -c.Exp * 3f + c.D / 30f + (c.Cover ? 0.3f : 0f), 1, map, nav, solid, spike, heads);
        if (Spots[1].Length == 0)
            Spots[1] = Pick(cands.Where(c => c.D is >= 11f and <= 32f), c => -c.Exp * 3f + c.D / 30f, 1, map, nav, solid, spike, heads);
        // Crossfire: a different angle on the spike from both others.
        var used = Spots[0].Take(1).Concat(Spots[1].Take(1)).Select(s => s.Feet).ToList();
        Spots[2] = Pick(cands.Where(c => c.D is >= 6f and <= 26f && used.All(u => AngleAround(spike, u, c.P) >= 50f && u.DistanceTo(c.P) > 6f)),
            c => -c.Exp * 3f + used.Select(u => AngleAround(spike, u, c.P)).DefaultIfEmpty(90f).Min() / 90f, 2, map, nav, solid, spike, heads);
        if (Spots[2].Length == 0)
            Spots[2] = Pick(cands.Where(c => c.D is >= 5f and <= 28f && used.All(u => u.DistanceTo(c.P) > 6f)), c => -c.Exp * 3f, 2, map, nav, solid, spike, heads);
        if (Spots.All(s => s.Length == 0))
        {
            // Nothing qualified (odd geometry): play next to the spike.
            var p = nav.Pos[nav.Nearest(spike + new Vector3(2.5f, 0, 0))];
            Spots[0] = new[] { new PostSpot(Kinds[0], "Spike", p, false, p) };
            Warnings.Add("no post-plant spot qualified");
        }

        // Lurk: hidden from the spike, a few metres from a peek that sees it.
        var lurks = new List<PostSpot>();
        int tries = 0;
        foreach (var c in lurkCands.OrderBy(c => c.Exp + (float)r0.NextDouble() * 0.05f))
        {
            if (lurks.Count >= 3 || ++tries > 40) break;
            if (lurks.Any(l => l.Feet.DistanceTo(c.P) < 6f)) continue;
            Vector3? peek = null;
            float bd = float.MaxValue;
            for (int n = 0; n < nav.Count; n++)
            {
                var q = nav.Pos[n];
                float dd = q.DistanceTo(c.P);
                if (dd < 1.5f || dd > 5f || Mathf.Abs(q.Y - c.P.Y) > 0.8f || dd >= bd) continue;
                if (!nav.Straight(c.P, q) || !SeesSpike(Eye(q), 3)) continue;
                bd = dd; peek = q;
            }
            if (peek is { } pk) lurks.Add(new PostSpot("Lurk", Callout(map, c.P, "Lurk"), c.P, false, pk));
        }
        Lurks = lurks.ToArray();
        BuildMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    readonly record struct Cand(int N, Vector3 P, float D, float Exp, bool Cover, float GeoStart, float GeoEntry);

    PostSpot[] Pick(IEnumerable<Cand> pool, Func<Cand, float> score, int kind, MapSpot map, BotNav nav, Box[] solid, Vector3 spike, Vector3[] heads)
    {
        var res = new List<PostSpot>();
        foreach (var c in pool.OrderByDescending(score))
        {
            if (res.Count >= 3) break;
            if (res.Any(r => r.Feet.DistanceTo(c.P) < 4f)) continue;
            // crouch where the crouched eye still sees the defuser (low cover in front)
            var ce = c.P + new Vector3(0, 1.15f, 0);
            bool crouch = c.Cover && heads.Count(h => !Blocked(ce, h, solid)) >= 2;
            res.Add(new PostSpot(Kinds[kind], Callout(map, c.P, Kinds[kind]), c.P, crouch, c.P));
        }
        return res.ToArray();
    }

    /// <summary>"Off-angle · Wine": the kind plus the nearest callout of the map's defender spots (within 7 m).</summary>
    static string Callout(MapSpot map, Vector3 p, string kind)
    {
        var e = map.Enemies.OrderBy(x => x.Feet.DistanceTo(p)).FirstOrDefault();
        return e != null && e.Feet.DistanceTo(p) < 7f ? $"{kind} · {e.Name}" : kind;
    }

    // ---------------------------------------------------------------- derivation

    /// <summary>A point on the site floor to seed the drills' walkable grid: the first plant spot, else the middle of the
    /// site-level defender spots.</summary>
    public static Vector3 SiteSeed(MapSpot map) => map.PlantSpots.Length > 0 ? map.PlantSpots[0] : SiteMiddle(map) ?? map.Choke;

    /// <summary>Site-level defender spots (no heaven) away from the choke: their medoid is "the middle of the site".</summary>
    static Vector3? SiteMiddle(MapSpot map)
    {
        var site = map.Enemies
            .Where(e => Mathf.Abs(e.Feet.Y - map.Choke.Y) < 2.0f && e.Feet.DistanceTo(map.Choke) is > 5f and < 30f)
            .Select(e => e.Feet).ToList();
        if (site.Count == 0) site = map.Enemies.Select(e => e.Feet).ToList();
        if (site.Count == 0) return null;
        return site.OrderBy(a => site.Sum(b => a.DistanceTo(b))).First();
    }

    static Vector3 DerivePlant(MapSpot map, BotNav nav, IReadOnlyList<Box> solid)
    {
        if (SiteMiddle(map) is not { } med) return nav.Pos[nav.Nearest(map.Choke)];
        // Open floor within 3 m of it.
        int best = nav.Nearest(med);
        float bd = float.MaxValue;
        for (int n = 0; n < nav.Count; n++)
        {
            float d = nav.Pos[n].DistanceTo(med);
            if (d > 3f || nav.Degree(n) < 8 || Mathf.Abs(nav.Pos[n].Y - med.Y) > 0.6f) continue;
            if (d < bd) { bd = d; best = n; }
        }
        return nav.Pos[best];
    }

    static Vector3[] DeriveEntries(BotNav nav, Box[] solid, Vector3 spike, float[] geoSpike, float[] geoStart, Func<Vector3, int, bool> seesSpike)
    {
        // Out of sight of the spike and of the site around it (a ring of points 6 m out).
        var siteEyes = new List<Vector3> { Eye(spike), spike + new Vector3(0, 1.0f, 0) };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8;
            int n = nav.Nearest(spike + new Vector3(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f));
            if (n >= 0 && H(nav.Pos[n], spike) < 8f) siteEyes.Add(Eye(nav.Pos[n]));
        }
        bool Hidden(Vector3 p) => !seesSpike(Eye(p), 1) && siteEyes.All(s => Blocked(s, Eye(p), solid));
        for (int pass = 0; pass < 3; pass++)
        {
            var cand = new List<int>();
            for (int n = 0; n < nav.Count; n++)
            {
                float gs = geoSpike[n];
                if (gs < 20f || gs > 60f || float.IsInfinity(gs)) continue;
                if (pass < 2 && !(geoStart[n] > gs + 8f)) continue; // the defenders' side
                if (pass < 1 && nav.Degree(n) < 8) continue;
                if (!Hidden(nav.Pos[n])) continue;
                cand.Add(n);
            }
            if (cand.Count == 0) continue;
            var res = new List<Vector3>();
            // start with the one furthest from the attackers' start, then farthest-point sampling
            res.Add(nav.Pos[cand.OrderByDescending(n => geoStart[n] - geoSpike[n]).First()]);
            while (res.Count < 3)
            {
                int best = -1; float bd = -1;
                foreach (var n in cand)
                {
                    float md = res.Min(r => r.DistanceTo(nav.Pos[n]));
                    if (md > bd) { bd = md; best = n; }
                }
                if (best < 0 || bd < 8f) break;
                res.Add(nav.Pos[best]);
            }
            return res.ToArray();
        }
        // Last resort: the farthest walkable point from the spike.
        int far = 0;
        for (int n = 0; n < nav.Count; n++) if (!float.IsInfinity(geoSpike[n]) && geoSpike[n] > geoSpike[far]) far = n;
        return new[] { nav.Pos[far] };
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Segment a→b blocked by a box (AABB-rejects boxes off the segment's extent first).</summary>
    static bool Blocked(Vector3 a, Vector3 b, Box[] boxes)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return false;
        var dir = d / len;
        float x0 = Mathf.Min(a.X, b.X), x1 = Mathf.Max(a.X, b.X), y0 = Mathf.Min(a.Y, b.Y), y1 = Mathf.Max(a.Y, b.Y), z0 = Mathf.Min(a.Z, b.Z), z1 = Mathf.Max(a.Z, b.Z);
        foreach (ref readonly var bx in boxes.AsSpan())
        {
            if (bx.Max.X < x0 || bx.Min.X > x1 || bx.Max.Y < y0 || bx.Min.Y > y1 || bx.Max.Z < z0 || bx.Min.Z > z1) continue;
            if (bx.Ray(a, dir) < len) return true;
        }
        return false;
    }

    public static Vector3 Eye(Vector3 feet) => feet + new Vector3(0, PlayerView.EyeHeight, 0);
    static float H(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Where a kneeling defuser's head can be (around the spike).</summary>
    public static Vector3[] DefuserHeads(Vector3 spike) => new[]
    {
        spike + new Vector3(0.6f, DefuserHeadY, 0), spike + new Vector3(-0.6f, DefuserHeadY, 0),
        spike + new Vector3(0, DefuserHeadY, 0.6f), spike + new Vector3(0, DefuserHeadY, -0.6f),
    };

    /// <summary>Angle (deg) between two points as seen from the spike (horizontal).</summary>
    static float AngleAround(Vector3 c, Vector3 a, Vector3 b)
    {
        var u = new Vector2(a.X - c.X, a.Z - c.Z); var v = new Vector2(b.X - c.X, b.Z - c.Z);
        if (u.LengthSquared() < 1e-4f || v.LengthSquared() < 1e-4f) return 0;
        return Mathf.RadToDeg(Mathf.Abs(u.AngleTo(v)));
    }

    /// <summary>Snaps a map point onto the walkable grid (keeps x/z when it's within 1.5 m of a node).</summary>
    static Vector3 Snap(BotNav nav, Vector3 p, IReadOnlyList<Box> solid)
    {
        int n = nav.Nearest(p);
        if (n < 0) return p;
        var q = nav.Pos[n];
        if (H(p, q) < 1.5f && !Collision.Overlaps(new Vector3(p.X, q.Y, p.Z), solid, 0.3f)) return new Vector3(p.X, q.Y, p.Z);
        return q;
    }

    /// <summary>Points along a polyline every <paramref name="step"/> metres (both ends included).</summary>
    public static List<Vector3> Sample(List<Vector3> path, float step)
    {
        var pts = new List<Vector3>();
        if (path.Count == 0) return pts;
        pts.Add(path[0]);
        for (int i = 1; i < path.Count; i++)
        {
            var a = path[i - 1]; var b = path[i];
            float len = a.DistanceTo(b);
            for (float s = step; s < len; s += step) pts.Add(a.Lerp(b, s / len));
            pts.Add(b);
        }
        return pts;
    }

    /// <summary>Walking distance from a node to every node (Dijkstra over the grid; +inf = unreachable).</summary>
    public static float[] Geo(BotNav nav, int src)
    {
        var d = new float[nav.Count];
        Array.Fill(d, float.PositiveInfinity);
        if (src < 0) return d;
        var q = new PriorityQueue<int, float>();
        d[src] = 0; q.Enqueue(src, 0);
        while (q.TryDequeue(out int n, out float dn))
        {
            if (dn > d[n] + 1e-4f) continue;
            foreach (var m in nav.Links[n])
            {
                float nd = dn + nav.Pos[n].DistanceTo(nav.Pos[m]);
                if (nd < d[m]) { d[m] = nd; q.Enqueue(m, nd); }
            }
        }
        return d;
    }

    /// <summary>Length of a polyline.</summary>
    public static float Length(Vector3 from, List<Vector3>? path)
    {
        if (path == null) return float.PositiveInfinity;
        float l = 0; var a = from;
        foreach (var p in path) { l += a.DistanceTo(p); a = p; }
        return l;
    }
}
