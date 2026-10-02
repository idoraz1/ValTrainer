using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;

namespace ValTrainer.Game.Bots;

/// <summary>
/// Walkable grid for bots in a box world (map blockouts). Every 0.5 m column is sampled for standing heights the same
/// way the player collides (<see cref="Collision.Ground"/> + <see cref="Collision.Overlaps"/> with a little extra
/// clearance), neighbours are linked when the height step is ≤ <see cref="Collision.StepHeight"/> (8-connected, no
/// corner cutting), and only the component connected to the seed (the map's start) is kept, so every node is reachable.
/// Paths: A* over the grid, then greedy string-pulling with knee-height rays so bots run straight lines across open
/// ground. Built once per map (≈ 70 ms) and cached for the process; paths can be found on worker threads (<see cref="FindPathAsync"/>).
/// </summary>
public sealed class BotNav
{
    public const float Cell = 0.5f;
    const float Clearance = 0.36f;

    public readonly float X0, Z0;
    public readonly int Nx, Nz;
    /// <summary>Node feet positions (x, z = cell centre; y = standing height).</summary>
    public readonly Vector3[] Pos;
    /// <summary>Linked neighbours per node.</summary>
    public readonly int[][] Links;
    readonly int[] colStart; // nodes of column c are colNodes[colStart[c] .. colStart[c+1])
    readonly int[] colNodes;
    readonly Box[] solid;
    public readonly float BuildMs;

    public int Count => Pos.Length;
    /// <summary>Walkable floor area in m².</summary>
    public float Area => Count * Cell * Cell;

    // A* scratch, one set per thread (the grid itself is immutable, so paths can be found on worker threads)
    [ThreadStatic] static float[]? g;
    [ThreadStatic] static int[]? from, stamp;
    [ThreadStatic] static int gen;
    [ThreadStatic] static PriorityQueue<int, float>? open;

    /// <summary>Path statistics for dev logs.</summary>
    public int Paths, Failed;
    public double PathMs, MaxPathMs, SmoothMs;
    readonly object statLock = new();

    /// <summary>Finds a path on a worker thread (keeps A* off the frame).</summary>
    public Task<List<Vector3>?> FindPathAsync(Vector3 a, Vector3 b) => Task.Run(() => FindPath(a, b));

    static readonly ConcurrentDictionary<string, BotNav> Cache = new();

    /// <summary>The cached grid for this world (key should identify the geometry, e.g. map key + box count).</summary>
    public static BotNav For(string key, IReadOnlyList<Box> world, Vector3 seed) =>
        Cache.GetOrAdd(key, _ => new BotNav(world.ToArray(), seed));

    BotNav(Box[] world, Vector3 seed)
    {
        var sw = Stopwatch.StartNew();
        solid = world;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var b in world)
        {
            x0 = Mathf.Min(x0, b.Min.X); z0 = Mathf.Min(z0, b.Min.Z);
            x1 = Mathf.Max(x1, b.Max.X); z1 = Mathf.Max(z1, b.Max.Z);
        }
        X0 = x0; Z0 = z0;
        Nx = Math.Max(1, (int)Mathf.Ceil((x1 - x0) / Cell));
        Nz = Math.Max(1, (int)Mathf.Ceil((z1 - z0) / Cell));

        // 1. standing heights per column
        var pos = new List<Vector3>();
        var col = new List<int>();
        var heights = new List<float>(8);
        const float fr = Collision.PlayerRadius - 0.05f;
        for (int iz = 0; iz < Nz; iz++)
            for (int ix = 0; ix < Nx; ix++)
            {
                float x = X0 + (ix + 0.5f) * Cell, z = Z0 + (iz + 0.5f) * Cell;
                heights.Clear();
                heights.Add(0f);
                foreach (var b in world)
                {
                    if (x + fr < b.Min.X || x - fr > b.Max.X || z + fr < b.Min.Z || z - fr > b.Max.Z) continue;
                    float y = b.Max.Y;
                    if (y > 30f || heights.Any(h => Mathf.Abs(h - y) < 0.01f)) continue;
                    heights.Add(y);
                }
                heights.Sort();
                foreach (var y in heights)
                {
                    var p = new Vector3(x, y, z);
                    if (Mathf.Abs(Collision.Ground(p, world) - y) > 0.01f) continue;
                    if (Collision.Overlaps(p, world, Clearance)) continue;
                    pos.Add(p);
                    col.Add(ix + iz * Nx);
                }
            }

        // 2. column index (nodes are already in column order)
        int nCols = Nx * Nz;
        var cs = new int[nCols + 1];
        foreach (var c in col) cs[c + 1]++;
        for (int i = 0; i < nCols; i++) cs[i + 1] += cs[i];
        colStart = cs;
        colNodes = Enumerable.Range(0, pos.Count).ToArray();
        var allPos = pos.ToArray();

        // 3. links
        var links = new int[allPos.Length][];
        var tmp = new List<int>(8);
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        for (int n = 0; n < allPos.Length; n++)
        {
            tmp.Clear();
            var p = allPos[n];
            int ix = col[n] % Nx, iz = col[n] / Nx;
            int[] ortho = new int[4];
            for (int k = 0; k < 8; k++)
            {
                int m = Find(ix + dx[k], iz + dz[k], p.Y, Collision.StepHeight, allPos);
                if (k < 4) ortho[k] = m;
                else if (m >= 0)
                {
                    // diagonal only when both orthogonal neighbours it passes are linked too (no corner cutting)
                    int a = ortho[dx[k] > 0 ? 0 : 1], c2 = ortho[dz[k] > 0 ? 2 : 3];
                    if (a < 0 || c2 < 0) m = -1;
                }
                if (m >= 0) tmp.Add(m);
            }
            links[n] = tmp.ToArray();
        }

        // 4. keep the component reachable from the seed
        int seedNode = -1; float best = float.MaxValue;
        for (int n = 0; n < allPos.Length; n++)
        {
            float d = allPos[n].DistanceSquaredTo(seed);
            if (d < best) { best = d; seedNode = n; }
        }
        var keep = new bool[allPos.Length];
        if (seedNode >= 0)
        {
            var q = new Queue<int>();
            q.Enqueue(seedNode); keep[seedNode] = true;
            while (q.Count > 0)
                foreach (var m in links[q.Dequeue()])
                    if (!keep[m]) { keep[m] = true; q.Enqueue(m); }
        }
        var remap = new int[allPos.Length];
        var finalPos = new List<Vector3>();
        var finalCol = new List<int>();
        for (int n = 0; n < allPos.Length; n++)
        {
            remap[n] = keep[n] ? finalPos.Count : -1;
            if (keep[n]) { finalPos.Add(allPos[n]); finalCol.Add(col[n]); }
        }
        Pos = finalPos.ToArray();
        Links = new int[Pos.Length][];
        for (int n = 0, k = 0; n < allPos.Length; n++)
            if (keep[n]) Links[k++] = links[n].Select(m => remap[m]).Where(m => m >= 0).ToArray();
        var cs2 = new int[nCols + 1];
        foreach (var c in finalCol) cs2[c + 1]++;
        for (int i = 0; i < nCols; i++) cs2[i + 1] += cs2[i];
        colStart = cs2;
        colNodes = Enumerable.Range(0, Pos.Length).ToArray();
        BuildMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    int Find(int ix, int iz, float y, float maxDy, Vector3[] pos)
    {
        if (ix < 0 || iz < 0 || ix >= Nx || iz >= Nz) return -1;
        int c = ix + iz * Nx, best = -1;
        float bd = maxDy + 1e-3f;
        for (int i = colStart[c]; i < colStart[c + 1]; i++)
        {
            float d = Mathf.Abs(pos[colNodes[i]].Y - y);
            if (d <= bd) { bd = d; best = colNodes[i]; }
        }
        return best;
    }

    /// <summary>The node in the column under <paramref name="p"/> closest to its height (within <paramref name="maxDy"/>), or -1.</summary>
    public int At(Vector3 p, float maxDy = 1.0f)
    {
        int ix = (int)Mathf.Floor((p.X - X0) / Cell), iz = (int)Mathf.Floor((p.Z - Z0) / Cell);
        return Find(ix, iz, p.Y, maxDy, Pos);
    }

    /// <summary>Nearest node to a point (searches rings up to ~3 m around its column).</summary>
    public int Nearest(Vector3 p)
    {
        int n = At(p);
        if (n >= 0) return n;
        int cx = (int)Mathf.Floor((p.X - X0) / Cell), cz = (int)Mathf.Floor((p.Z - Z0) / Cell);
        float best = float.MaxValue; int bi = -1;
        for (int r = 1; r <= 6 && bi < 0; r++)
            for (int iz = cz - r; iz <= cz + r; iz++)
                for (int ix = cx - r; ix <= cx + r; ix++)
                {
                    if (Math.Abs(ix - cx) != r && Math.Abs(iz - cz) != r) continue;
                    int m = Find(ix, iz, p.Y, 1.5f, Pos);
                    if (m < 0) continue;
                    float d = Pos[m].DistanceSquaredTo(p);
                    if (d < best) { best = d; bi = m; }
                }
        if (bi >= 0) return bi;
        for (int i = 0; i < Pos.Length; i++)
        {
            float d = Pos[i].DistanceSquaredTo(p);
            if (d < best) { best = d; bi = i; }
        }
        return bi;
    }

    /// <summary>A* from a world point to another; returns smoothed waypoints (excluding the start) or null.</summary>
    public List<Vector3>? FindPath(Vector3 a, Vector3 b, int maxExpand = 20000)
    {
        var sw = Stopwatch.StartNew();
        List<Vector3>? r = null;
        double smooth = 0;
        int s = Nearest(a), t = Nearest(b);
        var nodes = s >= 0 && t >= 0 ? AStar(s, t, maxExpand) : null;
        if (nodes != null)
        {
            double t0 = sw.Elapsed.TotalMilliseconds;
            r = Smooth(a, nodes);
            smooth = sw.Elapsed.TotalMilliseconds - t0;
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        lock (statLock)
        {
            if (r != null) Paths++; else Failed++;
            PathMs += ms; SmoothMs += smooth;
            if (Paths + Failed > 2) MaxPathMs = Math.Max(MaxPathMs, ms);
        }
        return r;
    }

    List<int>? AStar(int s, int t, int maxExpand)
    {
        if (g == null || g.Length != Pos.Length) { g = new float[Pos.Length]; from = new int[Pos.Length]; stamp = new int[Pos.Length]; gen = 0; }
        var fromA = BotNav.from!; var stampA = BotNav.stamp!; var gA = BotNav.g;
        var open = BotNav.open ??= new PriorityQueue<int, float>();
        gen++;
        open.Clear();
        g[s] = 0; from[s] = -1; stamp[s] = gen;
        var goal = Pos[t];
        open.Enqueue(s, 0);
        int expanded = 0;
        while (open.TryDequeue(out int n, out float f))
        {
            if (n == t) break;
            if (f - H(Pos[n], goal) > g[n] + 1e-3f) continue; // stale entry
            if (++expanded > maxExpand) return null;
            var pn = Pos[n];
            foreach (var m in Links[n])
            {
                float ng = g[n] + pn.DistanceTo(Pos[m]);
                if (stamp[m] == gen && ng >= g[m]) continue;
                stamp[m] = gen; g[m] = ng; from[m] = n;
                open.Enqueue(m, ng + H(Pos[m], goal));
            }
        }
        if (stamp[t] != gen) return null;
        var path = new List<int>();
        for (int n = t; n >= 0; n = from[n]) path.Add(n);
        path.Reverse();
        return path;
    }

    static float H(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    List<Vector3> Smooth(Vector3 start, List<int> nodes)
    {
        var outp = new List<Vector3>();
        var anchor = start;
        // The first node is just the one under / nearest the start: don't step back to its centre.
        int i = nodes.Count > 1 && new Vector2(start.X - Pos[nodes[0]].X, start.Z - Pos[nodes[0]].Z).Length() < 0.75f ? 1 : 0;
        while (i < nodes.Count)
        {
            // furthest node j ≥ i reachable in a straight line from the anchor
            int j = i;
            while (j + 1 < nodes.Count && j - i < 40 && Straight(anchor, Pos[nodes[j + 1]])) j++;
            anchor = Pos[nodes[j]];
            outp.Add(anchor);
            i = j + 1;
        }
        return outp;
    }

    /// <summary>Can a body walk the straight segment a→b? Ground must be continuous (every column on the way has a node
    /// within a step of the previous one) and knee-height rays (centre + both shoulders) must be clear.</summary>
    public bool Straight(Vector3 a, Vector3 b)
    {
        var d = new Vector3(b.X - a.X, 0, b.Z - a.Z);
        float len = d.Length();
        if (len < 0.05f) return true;
        if (Mathf.Abs(b.Y - a.Y) > 0.05f && len > 6f) return false; // ramps/stairs: follow the grid
        int steps = (int)Mathf.Ceil(len / (Cell * 0.5f));
        float y = a.Y;
        for (int k = 1; k <= steps; k++)
        {
            var p = a + d * (k / (float)steps);
            p.Y = y;
            int n = At(p, Collision.StepHeight);
            if (n < 0) return false;
            y = Pos[n].Y;
        }
        if (Mathf.Abs(y - b.Y) > 0.3f) return false;
        var dir = d / len;
        var side = new Vector3(-dir.Z, 0, dir.X) * (Collision.PlayerRadius + 0.06f);
        float ky = Mathf.Max(a.Y, b.Y) + Collision.StepHeight + 0.05f;
        var c = new Vector3(a.X, ky, a.Z);
        return !HRayBlocked(c, dir, len) && !HRayBlocked(c + side, dir, len) && !HRayBlocked(c - side, dir, len);
    }

    /// <summary>Horizontal ray vs the world with a cheap AABB reject (only boxes spanning the ray's height and its XZ extent).</summary>
    bool HRayBlocked(Vector3 o, Vector3 dir, float len)
    {
        float ex = o.X + dir.X * len, ez = o.Z + dir.Z * len;
        float minX = Mathf.Min(o.X, ex), maxX = Mathf.Max(o.X, ex), minZ = Mathf.Min(o.Z, ez), maxZ = Mathf.Max(o.Z, ez);
        foreach (ref readonly var b in solid.AsSpan())
        {
            if (b.Max.Y < o.Y || b.Min.Y > o.Y || b.Max.X < minX || b.Min.X > maxX || b.Max.Z < minZ || b.Min.Z > maxZ) continue;
            if (b.Ray(o, dir) < len) return true;
        }
        return false;
    }

    /// <summary>Random reachable node position.</summary>
    public Vector3 Random(Random rng) => Pos[rng.Next(Pos.Length)];

    /// <summary>Number of links (open nodes have 8).</summary>
    public int Degree(int n) => Links[n].Length;
}
