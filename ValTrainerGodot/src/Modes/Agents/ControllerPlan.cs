using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;

namespace ValTrainer.Modes;

/// <summary>
/// The walkable areas of a map blockout as one or more <see cref="BotNav"/> grids. A grid keeps only what its seed can
/// walk to, and some attacks include a drop the grid can't climb back up (Bind's Hookah window), so the choke, every
/// defender spot and the plant / retake spots seed their own grid when no earlier grid reaches them. Paths between grids
/// cross with one straight segment (the drop).
/// </summary>
public sealed class ControllerNav
{
    public readonly List<BotNav> Grids = new();
    readonly Dictionary<long, List<float>> near = new();
    readonly float x0, z0;

    public ControllerNav(MapSpot map, IReadOnlyList<Box> solid)
    {
        var seeds = new List<Vector3> { map.StartFeet, map.Choke };
        seeds.AddRange(map.Enemies.Select(e => e.Feet));
        seeds.AddRange(map.PlantSpots);
        seeds.AddRange(map.RetakeEntries);
        int k = 0;
        foreach (var s in seeds)
        {
            if (Grids.Count >= 6) break;
            if (Grids.Any(gr => Has(gr, s))) continue;
            // The start's grid uses the same cache key as the Deathmatch bots (shared, built once per process).
            string key = Grids.Count == 0 ? $"{map.Key}:{solid.Count}" : $"{map.Key}:{solid.Count}:smoke{k++}";
            var grid = BotNav.For(key, solid, s);
            if (grid.Count > 0 && !Grids.Contains(grid) && (Grids.Count == 0 || Has(grid, s))) Grids.Add(grid);
        }
        if (Grids.Count == 0) return;
        x0 = Grids[0].X0; z0 = Grids[0].Z0;
        // Heights of reachable floor around every 0.5 m column (±1 m), for the tactical map's floor test.
        foreach (var grid in Grids)
            foreach (var p in grid.Pos)
            {
                int ix = (int)Mathf.Floor((p.X - x0) / BotNav.Cell), iz = (int)Mathf.Floor((p.Z - z0) / BotNav.Cell);
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        long key = ((long)(ix + dx) << 32) ^ (uint)(iz + dz);
                        if (!near.TryGetValue(key, out var list)) near[key] = list = new List<float>(2);
                        bool dup = false;
                        foreach (var y in list) if (Mathf.Abs(y - p.Y) < 0.08f) { dup = true; break; }
                        if (!dup) list.Add(p.Y);
                    }
            }
    }

    static bool Has(BotNav grid, Vector3 p)
    {
        int n = grid.Nearest(p);
        if (n < 0) return false;
        var q = grid.Pos[n];
        return new Vector2(q.X - p.X, q.Z - p.Z).Length() < 1.2f && Mathf.Abs(q.Y - p.Y) < 0.9f;
    }

    /// <summary>Is there walkable, reachable floor at height <paramref name="y"/> within about a metre of (x, z)?</summary>
    public bool Reachable(float x, float z, float y, float tol = 0.4f)
    {
        int ix = (int)Mathf.Floor((x - x0) / BotNav.Cell), iz = (int)Mathf.Floor((z - z0) / BotNav.Cell);
        if (!near.TryGetValue(((long)ix << 32) ^ (uint)iz, out var list)) return false;
        foreach (var h in list) if (Mathf.Abs(h - y) <= tol) return true;
        return false;
    }

    BotNav? GridOf(Vector3 p)
    {
        BotNav? best = null;
        float bd = float.MaxValue;
        foreach (var grid in Grids)
        {
            int n = grid.Nearest(p);
            if (n < 0) continue;
            float d = grid.Pos[n].DistanceSquaredTo(p);
            if (d < bd) { bd = d; best = grid; }
        }
        return best;
    }

    /// <summary>Nearest walkable node position (any grid).</summary>
    public Vector3 Snap(Vector3 p)
    {
        var grid = GridOf(p);
        if (grid == null) return p;
        return grid.Pos[grid.Nearest(p)];
    }

    /// <summary>Walking polyline a → b (both included); crosses between grids with one straight drop.</summary>
    public List<Vector3> Path(Vector3 a, Vector3 b)
    {
        var outp = new List<Vector3> { a };
        var ga = GridOf(a);
        var gb = GridOf(b);
        if (ga == null || gb == null) { outp.Add(b); return outp; }
        if (ga == gb)
        {
            var p = ga.FindPath(a, b);
            if (p != null) outp.AddRange(p);
        }
        else
        {
            var pa = ga.Pos[ga.Nearest(b)];
            var pb = gb.Pos[gb.Nearest(pa)];
            var p1 = ga.FindPath(a, pa);
            if (p1 != null) outp.AddRange(p1);
            outp.Add(pa);
            outp.Add(pb);
            var p2 = gb.FindPath(pb, b);
            if (p2 != null) outp.AddRange(p2);
        }
        outp.Add(b);
        for (int i = outp.Count - 1; i > 0; i--) if (outp[i].DistanceTo(outp[i - 1]) < 0.05f) outp.RemoveAt(i);
        return outp;
    }
}

/// <summary>A defender spot that sees the attackers' entry, with the entry samples it sees.</summary>
public sealed class Threat
{
    public required EnemySpot Spot { get; init; }
    public required Vector3 Eye { get; init; }
    public readonly List<int> Seen = new();
    public float Exposure;
    public string Name => Spot.Name;
}

/// <summary>A standard smoke of the plan (landing point on the floor) and the defender spots it blinds.</summary>
public sealed class PlanSmoke
{
    public required string Name { get; init; }
    public required Vector3 Ground { get; init; }
    public bool Derived { get; init; }
    public readonly List<Threat> Covers = new();
    public Vector3 Center => ExecutePlan.CenterFor(Ground, ExecutePlan.PlanRadius);
}

/// <summary>How close one placed smoke came to a standard spot.</summary>
public readonly record struct SpotResult(string Name, float Error, bool ByWall)
{
    /// <summary>0..1: ≤ 1 m is perfect, ≥ 6 m is a miss.</summary>
    public float Score => float.IsInfinity(Error) ? 0f : Mathf.Clamp(1f - (Error - 1f) / 5f, 0f, 1f);
    public bool Good => Error <= 2.5f;
}

/// <summary>
/// The execute plan for one attack: the attacker's route (start → choke → site), eye samples along the entry (a few
/// metres before the choke to ~16 m into the site, weighted toward the choke exit), which defender spots see that entry
/// and how much (the threats), the standard smokes (<see cref="MapSpot.Smokes"/>, or picked greedily from the most
/// exposed defender spots when the map has none) and scoring of any set of smokes against it: the share of the targeted
/// defenders' sight lines a set of smokes cuts, and how far placed smokes are from the standard spots.
/// </summary>
public sealed class ExecutePlan
{
    /// <summary>Nominal smoke radius for planning (most smokes are 4.0–4.75 m).</summary>
    public const float PlanRadius = 4.1f;
    /// <summary>A smoke's centre sits this many radii above where it lands (the sphere rests in the floor like a dome).</summary>
    public const float Lift = 0.6f;
    public static Vector3 CenterFor(Vector3 ground, float radius) => ground + new Vector3(0, Lift * radius, 0);

    public readonly MapSpot Map;
    public readonly ControllerNav Nav;
    readonly IReadOnlyList<Box> solid;
    public readonly Vector3 Anchor;
    public readonly List<Vector3> Route;
    readonly List<float> routeS = new();
    public readonly float ChokeS;
    public readonly List<Vector3> Entry = new();
    public readonly List<float> EntryW = new();
    public readonly List<float> EntryS = new();
    /// <summary>Eye points along the walk up to the choke (where site defenders shouldn't be able to see you yet).</summary>
    public readonly List<Vector3> Approach = new();
    public readonly List<Threat> Threats = new();
    public readonly List<PlanSmoke> Standard = new();
    public bool Derived { get; private set; }

    public static Vector3 EyeOf(EnemySpot e) => e.Feet + new Vector3(0, PlayerView.EyeHeight - (e.Stance == Stance.Crouch ? 0.5f : 0f), 0);

    public ExecutePlan(MapSpot map, IReadOnlyList<Box> solid, ControllerNav nav)
    {
        Map = map; this.solid = solid; Nav = nav;
        Anchor = FindAnchor();
        Route = Nav.Path(map.StartFeet, map.Choke);
        var r2 = Nav.Path(map.Choke, Anchor);
        Route.AddRange(r2.Skip(1));
        float s = 0f;
        for (int i = 0; i < Route.Count; i++) { if (i > 0) s += Route[i].DistanceTo(Route[i - 1]); routeS.Add(s); }
        ChokeS = Project(map.Choke);
        SampleEntry();
        FindThreats();
        BuildStandard();
    }

    public float RouteLength => routeS.Count > 0 ? routeS[^1] : 0f;

    /// <summary>Route point at arc length s.</summary>
    public Vector3 RouteAt(float s)
    {
        if (Route.Count == 0) return Map.StartFeet;
        if (s <= 0) return Route[0];
        for (int i = 1; i < Route.Count; i++)
            if (s <= routeS[i]) return Route[i - 1].Lerp(Route[i], (s - routeS[i - 1]) / Mathf.Max(1e-4f, routeS[i] - routeS[i - 1]));
        return Route[^1];
    }

    /// <summary>Arc length of the route point closest to p (horizontal distance).</summary>
    public float Project(Vector3 p)
    {
        float best = float.MaxValue, bs = 0f;
        var q = new Vector2(p.X, p.Z);
        for (int i = 1; i < Route.Count; i++)
        {
            var a = new Vector2(Route[i - 1].X, Route[i - 1].Z);
            var b = new Vector2(Route[i].X, Route[i].Z);
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float d = q.DistanceSquaredTo(a + ab * t) + 0.25f * Mathf.Pow(p.Y - Mathf.Lerp(Route[i - 1].Y, Route[i].Y, t), 2);
            if (d < best) { best = d; bs = routeS[i - 1] + t * (routeS[i] - routeS[i - 1]); }
        }
        return bs;
    }

    Vector3 FindAnchor()
    {
        if (Map.PlantSpots.Length > 0) return Map.PlantSpots[0];
        var chokeEye = Map.Choke + new Vector3(0, 1.5f, 0);
        var site = Map.Enemies.Where(e => e.Feet.Y - Map.Choke.Y < 1.5f && e.Feet.DistanceTo(Map.Choke) is > 6f and < 26f
                                          && Map.LineOfSight(EyeOf(e), chokeEye)).ToList();
        if (site.Count == 0) site = Map.Enemies.OrderBy(e => e.Feet.DistanceTo(Map.Choke)).Take(3).ToList();
        if (site.Count == 0) return Map.Choke;
        var c = site.Aggregate(Vector3.Zero, (a, e) => a + e.Feet) / site.Count;
        return Nav.Snap(c);
    }

    void SampleEntry()
    {
        float from = Mathf.Max(0f, ChokeS - 4f), to = Mathf.Min(RouteLength, ChokeS + 16f);
        for (float s = from; s <= to + 0.01f; s += 1f)
        {
            var p = RouteAt(s);
            float y = Collision.Ground(new Vector3(p.X, p.Y + 0.6f, p.Z), solid);
            Entry.Add(new Vector3(p.X, y + PlayerView.EyeHeight, p.Z));
            EntryW.Add(s >= ChokeS && s <= ChokeS + 6f ? 1.5f : 1f);
            EntryS.Add(s);
        }
        for (float s = 0f; s < ChokeS - 3f; s += 2f)
        {
            var p = RouteAt(s);
            float y = Collision.Ground(new Vector3(p.X, p.Y + 0.6f, p.Z), solid);
            Approach.Add(new Vector3(p.X, y + PlayerView.EyeHeight, p.Z));
        }
    }

    /// <summary>Does this spot watch the approach before the choke (e.g. a defender pushed up the attackers' lane)?</summary>
    public bool SeesApproach(EnemySpot e)
    {
        var eye = EyeOf(e);
        foreach (var p in Approach) if (Map.LineOfSight(eye, p)) return true;
        return false;
    }

    void FindThreats()
    {
        foreach (var e in Map.Enemies)
        {
            var t = new Threat { Spot = e, Eye = EyeOf(e) };
            for (int i = 0; i < Entry.Count; i++)
                if (Map.LineOfSight(t.Eye, Entry[i])) { t.Seen.Add(i); t.Exposure += EntryW[i]; }
            if (t.Seen.Count > 0) Threats.Add(t);
        }
        Threats.Sort((a, b) => b.Exposure.CompareTo(a.Exposure));
    }

    /// <summary>Share (0..1) of a threat's entry sight lines blocked by these smokes (at full size).</summary>
    public float Blocked(Threat t, IReadOnlyList<SmokeVolume> smokes)
    {
        float tot = 0f, blk = 0f;
        foreach (var i in t.Seen)
        {
            tot += EntryW[i];
            if (SmokeVolume.Blocks(smokes, t.Eye, Entry[i], 0f, full: true)) blk += EntryW[i];
        }
        return tot <= 0f ? 0f : blk / tot;
    }

    static SmokeVolume PlanSphere(Vector3 ground) => SmokeVolume.Sphere(CenterFor(ground, PlanRadius), PlanRadius, 0f, 1f);

    void BuildStandard()
    {
        if (Map.Smokes.Length > 0)
            foreach (var s in Map.Smokes) Standard.Add(new PlanSmoke { Name = s.Name, Ground = s.Center });
        else
        {
            Derived = true;
            Derive();
        }
        foreach (var ps in Standard)
        {
            var one = new[] { PlanSphere(ps.Ground) };
            foreach (var t in Threats) if (Blocked(t, one) >= 0.5f) ps.Covers.Add(t);
        }
    }

    /// <summary>No authored smokes: greedily pick up to three smokes (on, or just in front of, the most exposed defender
    /// spots) that cut the most weighted entry sight lines, stopping when the next one adds under 10%.</summary>
    void Derive()
    {
        var cands = new List<(string Name, Vector3 Ground)>();
        foreach (var t in Threats.Take(8))
        {
            cands.Add((t.Name, t.Spot.Feet));
            var dir = Map.Choke - t.Spot.Feet;
            dir.Y = 0;
            if (dir.Length() > 4f)
            {
                var p = t.Spot.Feet + dir.Normalized() * 2.5f;
                float y = Collision.Ground(new Vector3(p.X, t.Spot.Feet.Y + 0.3f, p.Z), solid);
                if (Mathf.Abs(y - t.Spot.Feet.Y) < 0.8f) cands.Add((t.Name, new Vector3(p.X, y, p.Z)));
            }
        }
        var pairs = new List<(Threat T, int I)>();
        foreach (var t in Threats) foreach (var i in t.Seen) pairs.Add((t, i));
        float total = pairs.Sum(p => EntryW[p.I]);
        var blocked = new bool[pairs.Count];
        for (int k = 0; k < 3 && cands.Count > 0; k++)
        {
            int best = -1; float bestGain = 0f;
            for (int c = 0; c < cands.Count; c++)
            {
                var center = CenterFor(cands[c].Ground, PlanRadius);
                float gain = 0f;
                for (int p = 0; p < pairs.Count; p++)
                    if (!blocked[p] && SmokeVolume.Chord(pairs[p].T.Eye, Entry[pairs[p].I], center, PlanRadius) > SmokeVolume.SeeThrough) gain += EntryW[pairs[p].I];
                if (gain > bestGain) { bestGain = gain; best = c; }
            }
            if (best < 0 || bestGain < 0.1f * total) break;
            var pick = cands[best];
            var cc = CenterFor(pick.Ground, PlanRadius);
            for (int p = 0; p < pairs.Count; p++)
                if (!blocked[p] && SmokeVolume.Chord(pairs[p].T.Eye, Entry[pairs[p].I], cc, PlanRadius) > SmokeVolume.SeeThrough) blocked[p] = true;
            Standard.Add(new PlanSmoke { Name = pick.Name, Ground = pick.Ground, Derived = true });
            cands.RemoveAll(c => new Vector2(c.Ground.X - pick.Ground.X, c.Ground.Z - pick.Ground.Z).Length() < 5f);
        }
    }

    /// <summary>The standard smokes this kit is asked to place (all of them when the kit has a wall to cover the rest).</summary>
    public List<PlanSmoke> AssignedFor(ControllerKit kit) =>
        Standard.Take(kit.HasWall ? Standard.Count : Math.Min(kit.Charges, Standard.Count)).ToList();

    /// <summary>The defender spots whose sight lines the plan cuts (union of the assigned smokes' covers).</summary>
    public List<Threat> TargetsFor(IEnumerable<PlanSmoke> assigned)
    {
        var list = assigned.SelectMany(s => s.Covers).Distinct().ToList();
        if (list.Count == 0) list = Threats.Take(2).ToList();
        return list;
    }

    /// <summary>Weighted share (0..1) of the targets' entry sight lines these smokes cut (smokes at full size).</summary>
    public float Coverage(IReadOnlyList<SmokeVolume> smokes, IEnumerable<Threat> targets)
    {
        float tot = 0f, blk = 0f;
        foreach (var t in targets)
            foreach (var i in t.Seen)
            {
                tot += EntryW[i];
                if (smokes.Count > 0 && SmokeVolume.Blocks(smokes, t.Eye, Entry[i], 0f, full: true)) blk += EntryW[i];
            }
        return tot <= 0f ? 0f : blk / tot;
    }

    public float StandardCoverage(IEnumerable<PlanSmoke> assigned, IEnumerable<Threat> targets) =>
        Coverage(assigned.Select(s => PlanSphere(s.Ground)).ToList(), targets);

    /// <summary>Distance error of a placed smoke's landing point to a standard spot (other floor levels cost extra).</summary>
    static float SphereError(Vector3 std, SmokeVolume v)
    {
        var ground = v.Center - new Vector3(0, Lift * v.Radius, 0);
        float flat = new Vector2(ground.X - std.X, ground.Z - std.Z).Length();
        return flat + Mathf.Max(0f, Mathf.Abs(ground.Y - std.Y) - 1f) * 1.5f;
    }

    static float WallError(Vector3 std, SmokeVolume w)
    {
        float best = float.PositiveInfinity;
        var q = new Vector2(std.X, std.Z);
        for (int i = 0; i + 1 < w.Points.Length; i++)
        {
            var a = new Vector2(w.Points[i].X, w.Points[i].Z);
            var b = new Vector2(w.Points[i + 1].X, w.Points[i + 1].Z);
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float y = Mathf.Lerp(w.Points[i].Y, w.Points[i + 1].Y, t);
            best = Mathf.Min(best, q.DistanceTo(a + ab * t) + Mathf.Max(0f, Mathf.Abs(y - std.Y) - 1f) * 1.5f);
        }
        return best;
    }

    /// <summary>Matches placed smokes to the assigned standard spots (closest pairs first; a wall may serve several spots).</summary>
    public List<SpotResult> Match(IReadOnlyList<PlanSmoke> assigned, IReadOnlyList<SmokeVolume> placed)
    {
        var spheres = placed.Where(p => p.Shape == SmokeShape.Sphere).ToList();
        var walls = placed.Where(p => p.Shape == SmokeShape.Wall).ToList();
        var err = new float[assigned.Count];
        var byWall = new bool[assigned.Count];
        for (int i = 0; i < err.Length; i++)
        {
            err[i] = float.PositiveInfinity;
            foreach (var w in walls)
            {
                float e = WallError(assigned[i].Ground, w);
                if (e < err[i]) { err[i] = e; byWall[i] = true; }
            }
        }
        var pairs = new List<(int S, int P, float D)>();
        for (int i = 0; i < assigned.Count; i++)
            for (int j = 0; j < spheres.Count; j++) pairs.Add((i, j, SphereError(assigned[i].Ground, spheres[j])));
        pairs.Sort((a, b) => a.D.CompareTo(b.D));
        var sDone = new bool[assigned.Count];
        var pDone = new bool[spheres.Count];
        foreach (var (s, p, d) in pairs)
        {
            if (sDone[s] || pDone[p]) continue;
            sDone[s] = pDone[p] = true;
            if (d < err[s]) { err[s] = d; byWall[s] = false; }
        }
        return assigned.Select((a, i) => new SpotResult(a.Name, err[i], byWall[i])).ToList();
    }

    /// <summary>Dev report of the plan (threat exposure, standard smokes, what each covers, coverage).</summary>
    public string Describe(ControllerKit kit)
    {
        var sb = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(string.Create(inv, $"[smokeplan] {Map.Key}: route {RouteLength:0.0} m, choke at {ChokeS:0.0} m, anchor ({Anchor.X:0.0}, {Anchor.Y:0.0}, {Anchor.Z:0.0}), {Entry.Count} entry samples\n"));
        foreach (var t in Threats) sb.Append(string.Create(inv, $"[smokeplan]   threat {t.Name,-14} exposure {t.Exposure,5:0.0} ({t.Seen.Count} samples)\n"));
        foreach (var s in Standard)
            sb.Append(string.Create(inv, $"[smokeplan]   {(s.Derived ? "derived" : "standard")} smoke {s.Name,-12} at ({s.Ground.X:0.0}, {s.Ground.Y:0.0}, {s.Ground.Z:0.0}) blinds {string.Join(", ", s.Covers.Select(c => c.Name))}\n"));
        var assigned = AssignedFor(kit);
        var targets = TargetsFor(assigned);
        sb.Append(string.Create(inv, $"[smokeplan]   {kit.Agent}: {assigned.Count} assigned, targets {string.Join(", ", targets.Select(t => t.Name))}, standard coverage {StandardCoverage(assigned, targets) * 100:0}%"));
        return sb.ToString();
    }
}
