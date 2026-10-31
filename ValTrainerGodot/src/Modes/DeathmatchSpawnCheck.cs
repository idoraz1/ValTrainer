using System.Diagnostics;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Maps;

namespace ValTrainer.Modes;

/// <summary>
/// Dev self-test: <c>--dev --dm-spawncheck [--dm-spawncheck-trials N]</c> (works with --headless). Runs the Deathmatch
/// spawn picker (<see cref="DeathmatchSpawns"/>) on every map's real boxes and nav grid, the way the mode uses it, and
/// checks it against the acceptance targets of docs/research/deathmatch-1.18.md (section 4):
/// <list type="bullet">
/// <item>seen one step (2.5 m) out of the spawn by a living bot: ≤ 5% (arenas under 500 m², i.e. Bind: ≤ 20%);</item>
/// <item>nearest bot by walking distance ≥ 20 m in ≥ 90% of respawns (small arenas: ≥ 12 m — Bind is 33 × 26 m);</item>
/// <item>the nearest bot (by path) behind your spawn facing: ≤ 20%;</item>
/// <item>first spawns: every bot out of sight of you and ≥ 20 m walk away in ≥ 90% (small arenas: ≥ 12 m);</item>
/// <item>no spawn point on a wall top, trim curb or crate and none within 0.6 m of a wall; at least 10 per map;</item>
/// <item>bot paths (clearance-weighted, F-M4): turn points with the body &lt; 0.15 m from a wall &lt; 5%.</item>
/// </list>
/// Bots stand on random nav nodes, either spread over the map or half of them clustered 4–18 m from where you died (they
/// were hunting you); the death spot is a random node. Fixed seeds: the same numbers every run. Prints
/// "[dm-spawncheck] …" lines; the process exit code is the number of failed checks (0 = pass).
/// </summary>
public static class DeathmatchSpawnCheck
{
    const float Eye = PlayerView.EyeHeight;
    static int pass, fail;

    public static int Run()
    {
        pass = fail = 0;
        int trials = int.TryParse(CmdLine.After("--dm-spawncheck-trials"), out var t) && t > 0 ? t : 200;
        var sw = Stopwatch.StartNew();
        foreach (var map in MapSpots.All)
        {
            try { CheckMap(map, trials); }
            catch (Exception e) { Fail(map.Key, "exception", e.ToString()); }
        }
        Say($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed, {trials} respawns per scenario per map, {sw.Elapsed.TotalSeconds:0.0} s)");
        return fail;
    }

    static void CheckMap(MapSpot map, int trials)
    {
        var solid = map.Solid;
        string key = $"{map.Key}:{solid.Count}";
        var sw = Stopwatch.StartNew();
        var nav = BotNav.For(key, solid, map.StartFeet);
        var spawns = DeathmatchSpawns.For(key, map, solid, nav);
        double buildMs = sw.Elapsed.TotalMilliseconds;
        bool small = nav.Area < 500f;
        int nBots = DeathmatchMode.BotCount(nav.Area, 4);
        float pathTarget = small ? DeathmatchSpawns.NearPath : DeathmatchSpawns.SafePath, ringMax = small ? 0.20f : 0.05f;
        Func<Vector3, Vector3, bool> los = map.LineOfSight;
        var rng = new Random(map.Key.Aggregate(1234, (h, ch) => h * 31 + ch));
        Say($"{map.Key}: nav {nav.Count} nodes ({nav.Area:0} m²), {spawns.Anchors.Length} spawn points " +
            $"({spawns.OddSurfaceNodes} open nodes on walls / trim / crates left out), {nBots} bots, nav + spawn points built {buildMs:0} ms (spawn points {spawns.BuildMs:0} ms)");

        // ---- spawn points themselves ----
        int bad = spawns.Anchors.Count(a => !DeathmatchSpawns.RealFloor(map, a.Feet) || nav.Clear[a.Node] < DeathmatchSpawns.MinGap - 0.01f);
        Check(map.Key, $"spawn points on real floor, ≥ {DeathmatchSpawns.MinGap} m off walls", bad == 0, $"{bad} bad of {spawns.Anchors.Length}");
        Check(map.Key, "at least 10 spawn points", spawns.Anchors.Length >= 10, spawns.Anchors.Length.ToString());

        // ---- respawns: bots spread over the map / clustered near where you died ----
        foreach (bool clustered in new[] { false, true })
        {
            var recent = new List<Vector3>();
            int ring = 0, seen = 0, farEnough = 0, behind = 0, nearDeath = 0, run3 = 0;
            var levels = new int[5];
            var paths = new List<float>();
            double pickMs = 0;
            for (int i = 0; i < trials; i++)
            {
                var death = nav.Random(rng);
                var bots = new List<Vector3>();
                for (int k = 0; k < nBots; k++)
                    bots.Add(clustered && k < Math.Max(2, nBots / 2) ? NodeNear(nav, rng, death, 4f, 18f) : nav.Random(rng));
                var t0 = sw.Elapsed.TotalMilliseconds;
                var pick = spawns.Pick(bots, death, recent, los, rng);
                pickMs += sw.Elapsed.TotalMilliseconds - t0;
                recent.Add(pick.Feet); if (recent.Count > 4) recent.RemoveAt(0);
                levels[(int)pick.Level]++;

                // Measured independently of the picker: line of sight again, path lengths with the real path finder.
                var eyes = bots.Select(b => b + new Vector3(0, Eye, 0)).ToList();
                if (spawns.RingSeen(pick.Feet, eyes, los)) ring++;
                var pe = pick.Feet + new Vector3(0, Eye, 0);
                if (eyes.Any(e => los(e, pe) || los(e, pick.Feet + new Vector3(0, 1f, 0)))) seen++;
                var (dPath, nearest) = NearestByPath(nav, pick.Feet, bots);
                paths.Add(dPath);
                if (dPath >= pathTarget) farEnough++;
                if (dPath < 16.2f) run3++; // reached in ≤ 3 s at 5.4 m/s
                var to = nearest - pick.Feet; to.Y = 0;
                if (to.LengthSquared() > 0.01f && DeathmatchSpawns.YawDir(pick.Yaw).Dot(to.Normalized()) < 0) behind++;
                if (new Vector2(pick.Feet.X - death.X, pick.Feet.Z - death.Z).Length() < 15f) nearDeath++;
            }
            string sc = clustered ? "bots clustered near the death spot" : "bots spread over the map";
            Say($"{map.Key} [{sc}]: seen one step out {Pct(ring, trials)}, seen at the spawn {Pct(seen, trials)}, nearest bot by path " +
                $"median {Median(paths):0.0} m (p10 {Q(paths, 0.1):0.0}), ≥ {pathTarget:0} m {Pct(farEnough, trials)}, ≤ 3 s run {Pct(run3, trials)}, " +
                $"nearest bot behind you {Pct(behind, trials)}, < 15 m from the death spot {Pct(nearDeath, trials)}, " +
                $"levels 0/1/2/3/4 {string.Join("/", levels)}, pick {pickMs / trials:0.0} ms");
            Check(map.Key, $"{sc}: seen one step out ≤ {ringMax:P0}", ring <= ringMax * trials, Pct(ring, trials));
            Check(map.Key, $"{sc}: never seen at the spawn itself", seen == 0, Pct(seen, trials));
            Check(map.Key, $"{sc}: nearest bot ≥ {pathTarget:0} m walk in ≥ 90%", farEnough >= 0.9f * trials, Pct(farEnough, trials));
            Check(map.Key, $"{sc}: nearest bot behind you ≤ 20%", behind <= 0.2f * trials, Pct(behind, trials));
        }

        // ---- first spawns: you anywhere, then each bot out of everyone's sight, ≥ 25 m walk from you (12 m from the other bots) ----
        {
            int ok = 0, n = 0, seen = 0;
            var paths = new List<float>();
            for (int i = 0; i < Math.Max(20, trials / 5); i++)
            {
                var recent = new List<Vector3>();
                var you = spawns.Pick(Array.Empty<Vector3>(), null, recent, los, rng);
                recent.Add(you.Feet);
                var placed = new List<Vector3> { you.Feet };
                for (int k = 0; k < nBots; k++)
                {
                    var p = spawns.Pick(placed, null, recent, los, rng, 25f, strictCount: 1);
                    recent.Add(p.Feet); if (recent.Count > 4) recent.RemoveAt(0);
                    var (d, _) = NearestByPath(nav, p.Feet, new List<Vector3> { you.Feet });
                    paths.Add(d); n++;
                    if (d >= pathTarget) ok++;
                    if (los(you.Feet + new Vector3(0, Eye, 0), p.Feet + new Vector3(0, Eye, 0))) seen++;
                    placed.Add(p.Feet);
                }
            }
            Say($"{map.Key} [first spawns]: bot → you by path median {Median(paths):0.0} m (p10 {Q(paths, 0.1):0.0}), ≥ {pathTarget:0} m {Pct(ok, n)}, in your sight {Pct(seen, n)}");
            Check(map.Key, $"first spawns: bots ≥ {pathTarget:0} m walk from you in ≥ 90%", ok >= 0.9f * n, Pct(ok, n));
            Check(map.Key, "first spawns: no bot in your sight", seen == 0, Pct(seen, n));
        }

        // ---- bot paths (clearance-weighted): wide corners, goals on open floor ----
        {
            int turns = 0, tight = 0, goals = 0, goalWall = 0, oldTurns = 0, oldTight = 0;
            for (int i = 0; i < Math.Max(40, trials / 2); i++)
            {
                var a = nav.RandomOpen(rng, 1.0f);
                var b = nav.RandomOpen(rng, 1.0f);
                goals++; if (nav.ClearAt(b) < 0.5f) goalWall++;
                var p = nav.FindPath(a, b, clearanceWeight: 2f);
                if (p != null) for (int k = 0; k + 1 < p.Count; k++) { turns++; if (nav.ClearAt(p[k]) < 0.15f) tight++; }
                var q = nav.FindPath(a, b);
                if (q != null) for (int k = 0; k + 1 < q.Count; k++) { oldTurns++; if (nav.ClearAt(q[k]) < 0.15f) oldTight++; }
            }
            Say($"{map.Key} [bot paths]: turn points at a wall (gap < 0.15 m) {Pct(tight, turns)} of {turns} (shortest-path lines {Pct(oldTight, oldTurns)}), " +
                $"roam goals within 0.5 m of a wall {Pct(goalWall, goals)} (floor within 0.5 m of a wall {Pct(nav.Clear.Count(c => c < 0.5f), nav.Count)})");
            Check(map.Key, "bot paths: turn points at a wall < 5%", tight < 0.05f * Math.Max(1, turns), Pct(tight, turns));
        }
    }

    /// <summary>Walking distance (smoothed path length) to the nearest of <paramref name="bots"/>: the two nearest on the
    /// grid are measured with the real path finder.</summary>
    static (float Dist, Vector3 Bot) NearestByPath(BotNav nav, Vector3 from, List<Vector3> bots)
    {
        var field = nav.DistanceField(from, 120f);
        var byGrid = bots.Select(b => (B: b, D: GridDist(nav, field, b))).OrderBy(x => x.D).Take(2).ToList();
        float best = float.PositiveInfinity; var bestBot = bots[0];
        foreach (var (b, d) in byGrid)
        {
            float len = PathLen(nav, from, b);
            if (float.IsPositiveInfinity(len)) len = d;
            if (len < best) { best = len; bestBot = b; }
        }
        return (best, bestBot);
    }

    static float GridDist(BotNav nav, float[] field, Vector3 p)
    {
        int n = nav.At(p, Collision.StepHeight);
        if (n < 0) n = nav.Nearest(p);
        return n >= 0 ? field[n] : float.PositiveInfinity;
    }

    static float PathLen(BotNav nav, Vector3 a, Vector3 b)
    {
        var p = nav.FindPath(a, b);
        if (p == null) return float.PositiveInfinity;
        float len = 0; var c = a;
        foreach (var w in p) { len += c.DistanceTo(w); c = w; }
        return len;
    }

    static Vector3 NodeNear(BotNav nav, Random rng, Vector3 c, float d0, float d1)
    {
        for (int i = 0; i < 200; i++)
        {
            var q = nav.Random(rng);
            float d = new Vector2(q.X - c.X, q.Z - c.Z).Length();
            if (d >= d0 && d <= d1) return q;
        }
        return nav.Random(rng);
    }

    static string Pct(int a, int n) => n == 0 ? "-" : $"{100.0 * a / n:0}%";
    static float Median(List<float> l) => Q(l, 0.5);
    static float Q(List<float> l, double q)
    {
        if (l.Count == 0) return float.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
    }

    static void Say(string s) => GD.Print("[dm-spawncheck] " + s);

    static void Check(string map, string name, bool ok, string value)
    {
        if (ok) { pass++; Say($"PASS {map}: {name} ({value})"); }
        else Fail(map, name, value);
    }

    static void Fail(string map, string name, string value)
    {
        fail++;
        Say($"FAIL {map}: {name} ({value})");
    }
}
