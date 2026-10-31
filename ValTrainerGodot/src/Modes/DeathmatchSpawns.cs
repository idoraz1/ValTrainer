using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Maps;

namespace ValTrainer.Modes;

/// <summary>
/// Deathmatch spawn points and the spawn picker (docs/research/deathmatch-1.18.md, F-S1/F-S2). VALORANT: designated
/// spawn points with your back to a wall, out of every enemy's sightline, facing where the fight comes from, away from
/// where you died but not far from the action (Riot support "FFA Deathmatch"; patch notes 1.10 and 4.03).
/// <para><b>Spawn points</b> (built once per map, cached): open nav nodes (all 8 neighbours walkable) that stand on real
/// floor (the ground, a <see cref="Surf.Floor"/> or <see cref="Surf.Elevated"/> box: never on top of walls, rafters,
/// curbs or crates), keep ≥ 0.6 m between the body and the walls, and have a wall within 3.5 m behind with ≥ 10 m of
/// open view in front; thinned to one per 2 m.</para>
/// <para><b>Picking</b> (<see cref="Pick"/>): walking distance from the enemies (one <see cref="BotNav.DistanceField"/>
/// flood from where each will be in 0.75 s), then hard rules — no line of sight to the spot, nor to any point one step
/// (2.5 m) out of it; ≥ 20 m walk from every enemy; ≥ 12 m from where you died; not one of the last spawns — and one of
/// the best six by score (far by path, far from the death spot, not absurdly far from the action) at random. When nothing
/// passes, the rules relax one at a time (<see cref="Level"/>): spacing first, then the walk distance (down to 12 m),
/// then the one-step-out sightline (spawn protection ends once you walk 1.5 m, so a sightline right next to the spawn is
/// worse than a bot 12–20 m away), then anything out of direct sight. Facing: the open direction closest to the way the
/// nearest enemy will come.</para>
/// Pure logic over the nav grid and a line-of-sight test, so the headless <c>--dm-spawncheck</c> runs the same code.
/// </summary>
public sealed class DeathmatchSpawns
{
    /// <summary>A spawn point: feet, the yaw of its open view (away from the wall behind it), its nav node.</summary>
    public readonly record struct Anchor(Vector3 Feet, float Yaw, int Node);

    /// <summary>How many rules a pick had to drop. 0 clean: every rule. 1 spaced: near where you died or a recent spawn,
    /// otherwise clean. 2 near: an enemy 12–20 m walk away. 3 ring: seen one step out. 4 fallback: the least bad spot.</summary>
    public enum Level { Clean, Spaced, Near, RingSeen, Fallback }

    /// <summary>The result of one pick (for dev logs and the spawn check). <c>NearestPath</c> is the walking distance (m)
    /// of the nearest enemy (70 = none within reach).</summary>
    public readonly record struct SpawnPick(Vector3 Feet, float Yaw, float NearestPath, bool RingSeen, Level Level);

    public const float Eye = PlayerView.EyeHeight;
    public const float MinGap = 0.6f, BackWall = 3.5f, FrontOpen = 10f, Spacing = 2f;
    public const float SafePath = 20f, NearPath = 12f, DeathRadius = 12f, RecentRadius = 6f, StepOut = 2.5f;
    public const int TopK = 6;
    const float FieldMax = 70f;
    /// <summary>Grid distances run up to 8% longer than the straight-line paths bots actually walk (8-connected grid):
    /// rules compare the shorter figure.</summary>
    const float GridToPath = 0.92f;
    const int MaxRingChecks = 60;

    public readonly BotNav Nav;
    public readonly Anchor[] Anchors;
    /// <summary>Open nav nodes on a wall top, trim curb or crate, which spawns never use (dev log / spawn check).</summary>
    public readonly int OddSurfaceNodes;
    public readonly float BuildMs;
    readonly int[] pool; // open real-floor nodes (last-resort spawns)
    readonly IReadOnlyList<Box> solid;

    static readonly ConcurrentDictionary<string, DeathmatchSpawns> Cache = new();

    /// <summary>The cached spawn points for this map (key as for <see cref="BotNav.For"/>).</summary>
    public static DeathmatchSpawns For(string key, MapSpot map, IReadOnlyList<Box> solid, BotNav nav) =>
        Cache.GetOrAdd(key, _ => new DeathmatchSpawns(map, solid, nav));

    DeathmatchSpawns(MapSpot map, IReadOnlyList<Box> solid, BotNav nav)
    {
        var sw = Stopwatch.StartNew();
        Nav = nav;
        this.solid = solid;
        var cand = new List<Anchor>();
        var open = new List<int>();
        var byHeight = new Dictionary<int, Box[]>(); // boxes a horizontal ray at that eye height can hit
        var dist = new float[16];
        int odd = 0;
        for (int n = 0; n < nav.Count; n++)
        {
            if (nav.Degree(n) != 8) continue;
            var p = nav.Pos[n];
            if (!RealFloor(map, p)) { odd++; continue; }
            if (nav.Clear[n] < MinGap) continue;
            open.Add(n);
            if (nav.Clear[n] > BackWall) continue; // no wall within reach behind, whatever the direction
            var eye = p + new Vector3(0, Eye, 0);
            int hk = Mathf.RoundToInt(eye.Y * 100f);
            if (!byHeight.TryGetValue(hk, out var boxes))
                byHeight[hk] = boxes = solid.Where(b => b.Min.Y <= eye.Y && b.Max.Y >= eye.Y).ToArray();
            for (int k = 0; k < 16; k++) dist[k] = Mathf.Min(40f, Collision.FirstHit(eye, YawDir(k * 22.5f), boxes));
            float bestYaw = -1, bestOpen = 0;
            for (int k = 0; k < 16; k++)
                if (dist[k] >= FrontOpen && dist[k] > bestOpen && dist[(k + 8) % 16] <= BackWall) { bestOpen = dist[k]; bestYaw = k * 22.5f; }
            if (bestYaw >= 0) cand.Add(new Anchor(p, bestYaw, n));
        }
        OddSurfaceNodes = odd;
        pool = open.ToArray();
        // Thin to one per 2 m (greedy over a fixed shuffle: the same spawn points every run on this map).
        var rng = new Random(map.Key.Aggregate(17, (h, ch) => h * 31 + ch));
        var picked = new List<Anchor>();
        foreach (var c in cand.OrderBy(_ => rng.Next()))
            if (picked.All(a => a.Feet.DistanceSquaredTo(c.Feet) >= Spacing * Spacing)) picked.Add(c);
        Anchors = picked.ToArray();
        BuildMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>The surface a node stands on is the ground or a floor / elevated box (not a wall top, trim curb or crate).</summary>
    public static bool RealFloor(MapSpot map, Vector3 p)
    {
        Surf? under = null, near = null;
        foreach (var b in map.Boxes)
        {
            if (Mathf.Abs(b.Max.Y - p.Y) > 0.01f) continue;
            if (p.X >= b.Min.X && p.X <= b.Max.X && p.Z >= b.Min.Z && p.Z <= b.Max.Z) { under = b.Surf; if (b.Surf is Surf.Floor or Surf.Elevated) break; }
            else if (p.X >= b.Min.X - 0.3f && p.X <= b.Max.X + 0.3f && p.Z >= b.Min.Z - 0.3f && p.Z <= b.Max.Z + 0.3f) near ??= b.Surf;
        }
        var s = under ?? near;
        if (s == null) return p.Y < 0.01f; // the ground plane
        return s is Surf.Floor or Surf.Elevated;
    }

    public static Vector3 YawDir(float yaw)
    {
        float r = Mathf.DegToRad(yaw);
        return new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r));
    }

    /// <summary>Would an enemy see you one step out of the spot? (8 points 2.5 m around it at eye height, the ones you can
    /// walk to in a straight line.)</summary>
    public bool RingSeen(Vector3 p, IReadOnlyList<Vector3> enemyEyes, Func<Vector3, Vector3, bool> los)
    {
        if (enemyEyes.Count == 0) return false;
        for (int i = 0; i < 8; i++)
        {
            var q = p + YawDir(i * 45f) * StepOut;
            int n = Nav.At(q, Collision.StepHeight);
            if (n < 0 || !Nav.Straight(p, Nav.Pos[n])) continue;
            var e2 = Nav.Pos[n] + new Vector3(0, Eye, 0);
            foreach (var e in enemyEyes) if (los(e, e2)) return true;
        }
        return false;
    }

    /// <summary>Directly seen: an enemy eye has line of sight to the spot's eye or chest.</summary>
    public static bool Seen(Vector3 p, IReadOnlyList<Vector3> enemyEyes, Func<Vector3, Vector3, bool> los)
    {
        var eye = p + new Vector3(0, Eye, 0);
        var chest = p + new Vector3(0, 1.0f, 0);
        foreach (var e in enemyEyes) if (los(e, eye) || los(e, chest)) return true;
        return false;
    }

    /// <summary>Picks a spawn.</summary>
    /// <param name="enemies">Where every living enemy's feet will be (predicted ~0.75 s ahead).</param>
    /// <param name="deathSpot">Where this combatant died (favour respawning far from it), if any.</param>
    /// <param name="recent">The last few spawns (anyone's): don't reuse them right away.</param>
    /// <param name="los">Line of sight (the session's, so smokes count).</param>
    /// <param name="minPath">Walking distance every enemy must be at for a clean pick (first spawns use more).</param>
    /// <param name="strictCount">Only the first this-many enemies must be <paramref name="minPath"/> away; the others
    /// only <see cref="NearPath"/> (first spawns: the bots keep 25 m from you, 12 m from each other). Sightlines count for all.</param>
    public SpawnPick Pick(IReadOnlyList<Vector3> enemies, Vector3? deathSpot, IReadOnlyList<Vector3> recent,
                          Func<Vector3, Vector3, bool> los, Random rng, float minPath = SafePath, int strictCount = int.MaxValue)
    {
        int nA = Anchors.Length;
        if (nA == 0) return LastResort(enemies, los, rng, null);
        var eyes = enemies.Select(e => e + new Vector3(0, Eye, 0)).ToList();
        var field = enemies.Count > 0 ? Nav.DistanceField(enemies, FieldMax, null) : null;
        // Lenient enemies (past strictCount) count as if they were (minPath − NearPath) m nearer.
        var strictField = strictCount < enemies.Count && strictCount > 0 ? Nav.DistanceField(enemies.Take(strictCount).ToList(), FieldMax, null) : null;
        float lenient = Mathf.Max(0f, minPath - NearPath);
        var minP = new float[nA];
        var score = new float[nA];
        var farDeath = new bool[nA];
        var fresh = new bool[nA];
        for (int i = 0; i < nA; i++)
        {
            var a = Anchors[i];
            float mp = field == null ? FieldMax : Mathf.Min(field[a.Node] * GridToPath, FieldMax);
            if (strictField != null) mp = Mathf.Min(Mathf.Min(strictField[a.Node] * GridToPath, FieldMax), mp + lenient);
            minP[i] = mp;
            float dDeath = deathSpot is { } ds ? Flat(a.Feet, ds) : 99f;
            farDeath[i] = dDeath >= DeathRadius;
            fresh[i] = !recent.Any(r => r.DistanceSquaredTo(a.Feet) < RecentRadius * RecentRadius);
            // Far by path (up to 35 m), far from where you died, but not absurdly far from the action (> 45 m).
            score[i] = Mathf.Min(mp, 35f) + 0.3f * Mathf.Min(dDeath, 30f) - Mathf.Max(0f, mp - 45f) * 0.5f + (float)rng.NextDouble() * 3f;
        }
        var order = Enumerable.Range(0, nA).OrderByDescending(i => score[i]).ToArray();

        // Sightline tests are the expensive part: evaluated lazily, in score order, once per spot.
        var seenC = new sbyte[nA]; var ringC = new sbyte[nA];
        int ringChecks = 0;
        bool IsSeen(int i) { if (seenC[i] == 0) seenC[i] = (sbyte)(Seen(Anchors[i].Feet, eyes, los) ? 1 : -1); return seenC[i] > 0; }
        bool? IsRing(int i)
        {
            if (ringC[i] == 0)
            {
                if (ringChecks >= MaxRingChecks) return null; // budget spent: unknown
                ringChecks++;
                ringC[i] = (sbyte)(RingSeen(Anchors[i].Feet, eyes, los) ? 1 : -1);
            }
            return ringC[i] > 0;
        }
        List<int> Collect(Func<int, bool> cheap, bool needRingClear)
        {
            var ok = new List<int>();
            foreach (int i in order)
            {
                if (!cheap(i) || IsSeen(i)) continue;
                if (needRingClear && IsRing(i) != false) continue;
                ok.Add(i);
                if (ok.Count >= TopK) break;
            }
            return ok;
        }

        var level = Level.Clean;
        var pickList = Collect(i => minP[i] >= minPath && farDeath[i] && fresh[i], true);
        if (pickList.Count == 0) { level = Level.Spaced; pickList = Collect(i => minP[i] >= minPath, true); }
        if (pickList.Count == 0) { level = Level.Near; pickList = Collect(i => minP[i] >= NearPath, true); }
        if (pickList.Count == 0) { level = Level.RingSeen; pickList = Collect(i => minP[i] >= NearPath, false); }
        if (pickList.Count == 0) return LastResort(enemies, los, rng, field);

        int pick = pickList[rng.Next(pickList.Count)];
        var an = Anchors[pick];
        bool ring = IsRing(pick) ?? RingSeen(an.Feet, eyes, los);
        return new SpawnPick(an.Feet, Facing(an.Node, an.Yaw, field), minP[pick], ring, level);
    }

    /// <summary>Nothing out of sight among the spawn points (a tiny arena, a crowd): the open floor node farthest by path
    /// that is out of sight (up to 2000 tried, farthest first), else simply the farthest.</summary>
    SpawnPick LastResort(IReadOnlyList<Vector3> enemies, Func<Vector3, Vector3, bool> los, Random rng, float[]? field)
    {
        var eyes = enemies.Select(e => e + new Vector3(0, Eye, 0)).ToList();
        field ??= enemies.Count > 0 ? Nav.DistanceField(enemies, FieldMax, null) : null;
        var src = pool.Length > 0 ? pool : Enumerable.Range(0, Nav.Count).ToArray();
        float D(int n) => field == null ? FieldMax : Mathf.Min(field[n] * GridToPath, FieldMax);
        var byDist = src.Select(n => (N: n, K: D(n) + (float)rng.NextDouble())).OrderByDescending(x => x.K).Take(2000).ToList();
        int best = byDist[0].N;
        foreach (var (n, _) in byDist)
            if (!Seen(Nav.Pos[n], eyes, los)) { best = n; break; }
        var p = Nav.Pos[best];
        return new SpawnPick(p, Facing(best, 0f, field), D(best), RingSeen(p, eyes, los), Level.Fallback);
    }

    /// <summary>Facing: of the 16 directions with ≥ 6 m open, the one closest to the way the nearest enemy will come —
    /// the first ~8 m of the walk toward it (down the distance field) — else the spot's own view.</summary>
    float Facing(int node, float viewYaw, float[]? field)
    {
        Vector3 to = YawDir(viewYaw);
        var start = Nav.Pos[node];
        if (field != null)
        {
            int n = node;
            for (int step = 0; step < 24 && Flat(Nav.Pos[n], start) < 8f; step++)
            {
                int next = n;
                foreach (var m in Nav.Links[n]) if (field[m] < field[next]) next = m;
                if (next == n) break;
                n = next;
            }
            var d = Nav.Pos[n] - start; d.Y = 0;
            if (d.LengthSquared() > 1f) to = d.Normalized();
        }
        var eye = start + new Vector3(0, Eye, 0);
        float bestYaw = viewYaw, bestDot = -2f;
        for (int k = 0; k < 16; k++)
        {
            float yw = k * 22.5f;
            var d = YawDir(yw);
            if (Collision.FirstHit(eye, d, solid) < 6f) continue;
            float dot = d.Dot(to);
            if (dot > bestDot) { bestDot = dot; bestYaw = yw; }
        }
        return bestYaw;
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ---------------- hold spots for the bots (same rules: open floor, wall behind, a view) ----------------

    /// <summary>The spawn point nearest <paramref name="p"/> within <paramref name="radius"/> m (straight line, a little
    /// random so neighbours spread out), or null: a sensible spot to hold an angle from.</summary>
    public Anchor? HoldSpotNear(Vector3 p, float radius, Random rng)
    {
        Anchor? best = null; float bd = radius * radius;
        foreach (var a in Anchors)
        {
            if (Mathf.Abs(a.Feet.Y - p.Y) > 1.5f) continue;
            float d = a.Feet.DistanceSquaredTo(p) * (0.8f + 0.4f * (float)rng.NextDouble());
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }
}
