using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Sound Lock ("sound"): a footsteps drill on a real map. You hold a site-level defender spot; the drill finds the corners
/// enemies can appear from around you (where hidden floor meets floor you can see, on the navigation grid) and the hidden
/// routes leading to them. Each rep one or more attackers run a route out of sight — their footsteps are positional
/// (<see cref="FootstepFx"/>: running audible to ~40 m with VALORANT's flat curve, walking silent, muffled through walls,
/// per-surface) — then peek you from that corner. Pre-aim where they'll appear: you're scored on the crosshair error at
/// the first frame they're visible (bot_seen), the time to your first hit, and the kill. Higher tiers: more attackers,
/// jiggles before the swing, and fakes (run loud toward one corner, then walk silently to another).
/// </summary>
public sealed class SoundLockMode : MapMode
{
    public override string Key => "sound";
    public override string Name => "Sound Lock";
    public override string Description => "Listen for footsteps and pre-aim where the enemy will appear.";
    public override string Category => "Utility";
    public override float Duration => 90f;
    public override string SimKind => "bot";
    public override string? Subtitle => $"{MapSpot.Map} — {MapSpot.Name} · {spotName}";
    public override AimFocus? Focus => NearestHead(walkers.Where(w => w.Alive).Select(w => w.Body));

    sealed class Entrance
    {
        public Vector3 Corner, Out;
        public float Yaw;
        public readonly List<List<Vector3>> Paths = new();
        public readonly List<Vector3> Peeks = new();
        /// <summary>Silent fakes: from waypoint <c>At</c> of route <c>Route</c>, a hidden walk to another corner.</summary>
        public readonly List<(int Route, int At, Entrance To, List<Vector3> Path)> Fakes = new();
        public int Index;
    }

    sealed class Walker
    {
        public enum St { Wait, Run, Walk, Jiggle, Swing, Hold }
        public BotCharacter Body = null!;
        public BotBrain Brain = null!;
        public Entrance Ent = null!;
        public Entrance? FakeTo;
        public int FakeWp;
        public List<Vector3>? FakePath;
        public St State;
        public List<Vector3> Path = new();
        public int Wp;
        public float StartAt, StateT;
        public int Jiggles;
        public Vector3 Peek, JiggleTo, Vel;
        public bool Seen, Hit, Fake, WalkedIn, Decoy, Silent;
        public float SeenAt, Err, PitchErr;
        public readonly FootstepEmitter Steps = new();
        public bool Alive => GodotObject.IsInstanceValid(Body) && !Body.Dead;
    }

    BotNav nav = null!;
    EnemySpot spot = null!;
    string spotName = "";
    readonly List<Entrance> ents = new();
    readonly List<Walker> walkers = new();
    enum Ph { Gap, Live, After }
    Ph ph;
    float phT, gapLen, lastSwingAt;
    int reps, repsLost;
    // stats
    readonly List<float> errs = new(), reacts = new();
    int locked, fakes, fakesRead, deaths2, silent;
    string lastLine = "";

    static readonly bool DevLog = CmdLine.Dev;
    void Log(string s) { if (DevLog) GD.Print($"[sound] {Now,6:0.0}s {s}"); }

    // ------------------------------------------------------------------ setup: corners and routes

    protected override void Setup()
    {
        nav = BotNav.For($"{MapSpot.Key}:{G.Solid.Count}", G.Solid, MapSpot.StartFeet);
        var choke = MapSpot.Choke;
        var cands = MapSpot.Enemies.Where(e => e.Feet.Y - choke.Y < 1.5f).OrderBy(_ => Rng.Next())
            .Concat(MapSpot.Enemies.Where(e => e.Feet.Y - choke.Y >= 1.5f)).ToList();
        int want = Tier >= 2 ? 3 : 2;
        List<Entrance>? best = null;
        EnemySpot? bestSpot = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in cands.Take(10))
        {
            var e = FindEntrances(c);
            if (best == null || e.Count > best.Count) { best = e; bestSpot = c; }
            if (e.Count >= want) break;
        }
        spot = bestSpot ?? MapSpot.Enemies[0];
        spotName = spot.Name;
        ents.AddRange(best ?? new List<Entrance>());
        for (int i = 0; i < ents.Count; i++) ents[i].Index = i;
        SpawnFeet = spot.Feet;
        FindFakes();
        // Face the middle of the entrances (or the choke).
        var look = ents.Count > 0 ? ents.Aggregate(Vector3.Zero, (s, x) => s + (x.Corner - spot.Feet).Normalized()) : choke - spot.Feet;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(look.X, -look.Z));
        G.Respawn(SpawnFeet, SpawnYaw);
        Log($"map {MapSpot.Key} spot '{spotName}' {spot.Feet}: {ents.Count} entrances " +
            string.Join(", ", ents.Select(x => $"#{x.Index} yaw {x.Yaw:0} at {x.Corner.DistanceTo(spot.Feet):0}m ({x.Paths.Count} routes, {x.Peeks.Count} peeks, {x.Fakes.Count} fakes)")) + $" [{sw.ElapsedMilliseconds} ms]");
        StartGap(1.5f);
    }

    Vector3 Eye(Vector3 feet) => feet + new Vector3(0, PlayerView.EyeHeight, 0);

    List<Entrance> FindEntrances(EnemySpot s)
    {
        var eye = Eye(s.Feet);
        int n = nav.Count;
        var vis = new sbyte[n]; // 0 = far / ignored, 1 = hidden, 2 = visible
        for (int i = 0; i < n; i++)
        {
            var p = nav.Pos[i];
            float d = new Vector2(p.X - s.Feet.X, p.Z - s.Feet.Z).Length();
            if (d > 34f || Mathf.Abs(p.Y - s.Feet.Y) > 3f) continue;
            bool see = !Collision.Blocked(eye, Eye(p), G.Solid) && !Collision.Blocked(eye, p + new Vector3(0, 1.0f, 0), G.Solid);
            vis[i] = (sbyte)(see ? 2 : 1);
        }
        // Corners: hidden nodes next to a visible one, 6–30 m away.
        var corners = new List<(int Hidden, int Seen, float Yaw, float Dist)>();
        for (int i = 0; i < n; i++)
        {
            if (vis[i] != 1) continue;
            var p = nav.Pos[i];
            float d = new Vector2(p.X - s.Feet.X, p.Z - s.Feet.Z).Length();
            if (d < 6f || d > 30f) continue;
            foreach (var j in nav.Links[i])
            {
                if (vis[j] != 2) continue;
                var to = p - s.Feet;
                corners.Add((i, j, Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z)), d));
                break;
            }
        }
        // Cluster by direction (12° bins), keep corners backed by a real hidden corridor.
        var bins = corners.GroupBy(c => (int)Mathf.Floor((c.Yaw + 180f) / 12f)).OrderByDescending(g => g.Count()).ToList();
        var chosen = new List<Entrance>();
        foreach (var bin in bins)
        {
            if (bin.Count() < 2) break;
            float yaw = bin.Average(c => c.Yaw);
            if (chosen.Any(e => Mathf.Abs(Mathf.Wrap(e.Yaw - yaw, -180f, 180f)) < 25f)) continue;
            var sorted = bin.OrderBy(c => c.Dist).ToList();
            var c0 = sorted[sorted.Count / 2];
            if (HiddenReach(c0.Hidden, vis, 50) < 30) continue;
            var ent = new Entrance { Corner = nav.Pos[c0.Hidden], Out = nav.Pos[c0.Seen], Yaw = yaw };
            // Peek points: a step or two out of the corner, in view.
            var dir = ent.Out - ent.Corner; dir.Y = 0;
            dir = dir.LengthSquared() < 1e-4f ? Vector3.Forward : dir.Normalized();
            foreach (var k in new[] { 1.2f, 1.7f, 2.3f, 3f })
            {
                var q = ent.Corner + dir * k;
                int qi = nav.At(q, 0.8f);
                if (qi < 0 || !nav.Straight(ent.Corner, nav.Pos[qi])) continue;
                var qp = nav.Pos[qi];
                if (Collision.Blocked(eye, Eye(qp), G.Solid)) continue;
                ent.Peeks.Add(qp);
            }
            if (ent.Peeks.Count == 0) ent.Peeks.Add(ent.Out);
            FindRoutes(ent, vis, eye);
            if (ent.Paths.Count == 0) continue;
            chosen.Add(ent);
            if (chosen.Count >= 3) break;
        }
        return chosen;
    }

    /// <summary>For each route, a point partway along it from which another corner can be reached without being seen.</summary>
    void FindFakes()
    {
        foreach (var a in ents)
            for (int r = 0; r < a.Paths.Count; r++)
            {
                var path = a.Paths[r];
                foreach (var b in ents)
                {
                    if (b == a) continue;
                    for (int at = Math.Max(1, path.Count / 3); at < path.Count - 1; at++)
                    {
                        var p = nav.FindPath(path[at], b.Corner);
                        if (p == null || p.Count == 0 || !Hidden(p)) continue;
                        float len = 0; var prev = path[at];
                        foreach (var q in p) { len += prev.DistanceTo(q); prev = q; }
                        if (len > 26f) continue;
                        a.Fakes.Add((r, at, b, p));
                        break;
                    }
                }
            }
    }

    int HiddenReach(int start, sbyte[] vis, int cap)
    {
        var seen = new HashSet<int> { start };
        var q = new Queue<int>();
        q.Enqueue(start);
        while (q.Count > 0 && seen.Count < cap)
            foreach (var m in nav.Links[q.Dequeue()])
                if (vis[m] != 2 && seen.Add(m)) q.Enqueue(m);
        return seen.Count;
    }

    void FindRoutes(Entrance ent, sbyte[] vis, Vector3 eye)
    {
        var cands = new List<int>();
        for (int i = 0; i < nav.Count; i++)
        {
            if (vis[i] == 2) continue;
            float d = new Vector2(nav.Pos[i].X - ent.Corner.X, nav.Pos[i].Z - ent.Corner.Z).Length();
            float fromYou = new Vector2(nav.Pos[i].X - eye.X, nav.Pos[i].Z - eye.Z).Length();
            if (d is > 9f and < 24f && fromYou > 11f && Mathf.Abs(nav.Pos[i].Y - ent.Corner.Y) < 3f && nav.Degree(i) == 8) cands.Add(i);
        }
        int tries = 0;
        foreach (var i in cands.OrderBy(_ => Rng.Next()))
        {
            if (ent.Paths.Count >= 3 || ++tries > 24) break;
            var start = nav.Pos[i];
            var path = nav.FindPath(start, ent.Corner);
            if (path == null || path.Count == 0) continue;
            float len = 0;
            var prev = start;
            bool hidden = true;
            foreach (var p in path)
            {
                float l = prev.DistanceTo(p);
                for (float t = 0; t <= l && hidden; t += 0.5f)
                {
                    var q = prev.Lerp(p, l < 1e-3f ? 1 : t / l);
                    if (!Collision.Blocked(eye, Eye(q), G.Solid) || !Collision.Blocked(eye, q + new Vector3(0, 1.0f, 0), G.Solid)) hidden = false;
                    if (new Vector2(q.X - eye.X, q.Z - eye.Z).Length() < 4.5f) hidden = false; // not right behind your back
                }
                len += l;
                prev = p;
                if (!hidden) break;
            }
            // the last metre at the corner may be borderline: only require the route up to it to be hidden
            if (!hidden || len < 8f || len > 32f) continue;
            path.Insert(0, start);
            ent.Paths.Add(path);
        }
    }

    // ------------------------------------------------------------------ reps

    void StartGap(float len)
    {
        ph = Ph.Gap; phT = 0; gapLen = len;
    }

    void NewRep()
    {
        foreach (var w in walkers) if (GodotObject.IsInstanceValid(w.Body)) G.Despawn(w.Body);
        walkers.Clear();
        if (ents.Count == 0) { StartGap(3f); return; }
        if (G.Player.Dead) G.Respawn(SpawnFeet, SpawnYaw);
        else G.Player.Reset();
        reps++;
        int count = Tier <= 2 ? 1 : Tier == 3 ? 2 : (Rng.NextDouble() < 0.5 ? 2 : 3);
        count = Math.Min(count, ents.Count);
        var order = ents.OrderBy(_ => Rng.Next()).ToList();
        float delay = 0;
        for (int i = 0; i < count; i++)
        {
            var ent = order[i];
            int route = Rng.Next(ent.Paths.Count);
            var w = MakeWalker(ent, route, Now + delay);
            // Higher tiers: jiggle before swinging, or fake one corner and walk (silently) to another.
            w.Jiggles = Tier >= 2 && Rng.NextDouble() < Difficulty.T(Tier, 0f, 0f, 0.3f, 0.4f, 0.5f) ? 1 + Rng.Next(2) : 0;
            bool fake = Tier >= 2 && i == 0 && ents.Count > 1 && Rng.NextDouble() < Difficulty.T(Tier, 0f, 0f, 0.3f, 0.38f, 0.45f);
            var fk = ent.Fakes.Where(f => f.Route == route).ToList();
            if (fake && fk.Count > 0 && count == 1)
            {
                var f = fk[Rng.Next(fk.Count)];
                w.FakeTo = f.To;
                w.FakeWp = f.At;
                w.FakePath = f.Path;
            }
            else if (fake)
            {
                // No hidden way between the corners: a teammate runs loud toward this corner and stops (the fake) while
                // this one walks — silently — to another corner and peeks it a moment after the noise stops.
                var other = order[1];
                int r2 = Rng.Next(other.Paths.Count);
                var decoy = w;
                decoy.Decoy = true;
                decoy.Jiggles = 0;
                var real = MakeWalker(other, r2, Now);
                real.Silent = true; real.Fake = true; silent++;
                float decoyDur = Len(decoy.Path) / (Tier == 0 ? 4.6f : Mover.RunSpeed), realDur = Len(real.Path) / Mover.WalkSpeed;
                float lag = R(0.6f, 1.6f);
                decoy.StartAt = Now + Mathf.Max(0f, realDur - decoyDur - lag);
                real.StartAt = Now + Mathf.Max(0f, decoyDur + lag - realDur);
                walkers.Add(decoy);
                walkers.Add(real);
                break;
            }
            walkers.Add(w);
            delay += R(0.6f, 2.2f);
        }
        ph = Ph.Live; phT = 0; lastSwingAt = -1f;
        Log($"rep {reps}: {string.Join(", ", walkers.Select(w => $"#{w.Ent.Index}{(w.Decoy ? " (loud decoy)" : w.Silent ? " (silent walk)" : "")}{(w.FakeTo != null ? $"→fake→#{w.FakeTo.Index}" : "")}{(w.Jiggles > 0 ? $" jiggle×{w.Jiggles}" : "")}"))}");
    }

    Walker MakeWalker(Entrance ent, int route, float startAt)
    {
        var w = new Walker { Ent = ent, StartAt = startAt };
        w.Path = new List<Vector3>(ent.Paths[route]);
        w.Peek = ent.Peeks[Rng.Next(ent.Peeks.Count)];
        w.Body = G.SpawnBot(w.Path[0]);
        w.Body.QuietSteps = true;
        w.Brain = new BotBrain(w.Body, Difficulty.Get(Tier).Bot, Rng)
        {
            Enabled = false,
            PreAimed = Tier >= 3,
            ShoulderSight = Difficulty.ShoulderSight(Tier),
        };
        var to = w.Path.Count > 1 ? w.Path[1] - w.Path[0] : Vector3.Forward;
        w.Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z));
        w.Wp = 1;
        w.State = Walker.St.Wait;
        return w;
    }

    static float Len(List<Vector3> p)
    {
        float l = 0;
        for (int i = 1; i < p.Count; i++) l += p[i - 1].DistanceTo(p[i]);
        return l;
    }

    public override void Update(float dt)
    {
        phT += dt;
        switch (ph)
        {
            case Ph.Gap:
                if (phT >= gapLen) NewRep();
                return;
            case Ph.After:
                if (phT >= 1.2f) StartGap(R(0.8f, 1.8f));
                return;
        }
        foreach (var w in walkers)
        {
            if (!w.Alive) continue;
            Move(w, dt);
            w.Brain.Update(G, dt);
            if (ph != Ph.Live) return; // you died
            if (!w.Seen && G.LineOfSight(G.View.Eye, w.Body.Head)) OnSeen(w);
        }
        bool allDone = walkers.Where(w => !w.Decoy).All(w => !w.Alive);
        bool stale = lastSwingAt >= 0 && Now - lastSwingAt > 6f && walkers.Where(w => !w.Decoy).All(w => !w.Alive || w.State == Walker.St.Hold);
        if (allDone || stale) EndRep(allDone);
    }

    void OnSeen(Walker w)
    {
        w.Seen = true;
        w.SeenAt = Now;
        var head = w.Body.Head;
        SeenEvent(head);
        w.Err = G.View.AngleTo(head);
        var d = (head - G.View.Eye).Normalized();
        w.PitchErr = G.View.ViewPitch - Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        errs.Add(w.Err);
        if (w.Err <= 10f) locked++;
        if (w.Fake) { fakes++; if (w.Err <= 10f) fakesRead++; }
        Score += (int)Mathf.Max(0, 60 - 6 * w.Err);
        Event("sound_seen", w.Err, w.Fake ? 1 : 0);
        Log($"seen #{w.Ent.Index} err {w.Err:0.0}° (pitch {w.PitchErr:+0.0;-0.0}) {(w.Fake ? "after a fake" : "")}");
    }

    void Move(Walker w, float dt)
    {
        var b = w.Body;
        w.StateT += dt;
        Vector3 target = b.Feet;
        float speed = 0;
        switch (w.State)
        {
            case Walker.St.Wait:
                if (Now >= w.StartAt) { w.State = w.Silent ? Walker.St.Walk : Walker.St.Run; w.StateT = 0; }
                break;
            case Walker.St.Run:
            case Walker.St.Walk:
            {
                speed = w.State == Walker.St.Walk ? Mover.WalkSpeed : Tier == 0 ? 4.6f : Mover.RunSpeed;
                if (w.Wp >= w.Path.Count)
                {
                    if (w.Decoy) { w.State = Walker.St.Hold; break; } // the fake: noise, then nothing
                    BeginPeek(w);
                    break;
                }
                target = w.Path[w.Wp];
                // Fake: partway along the loud route, stop and walk to another corner.
                if (w.FakeTo != null && w.FakePath != null && w.State == Walker.St.Run && w.Wp > w.FakeWp)
                {
                    var p = new List<Vector3>(w.FakePath);
                    {
                        w.Ent = w.FakeTo;
                        w.Peek = w.FakeTo.Peeks[Rng.Next(w.FakeTo.Peeks.Count)];
                        w.Path = p; w.Wp = 0;
                        w.State = Walker.St.Walk; w.Fake = true; w.WalkedIn = true; silent++;
                        Log($"fake: walking silently to #{w.FakeTo.Index}");
                    }
                    w.FakeTo = null;
                    target = w.Path[Math.Min(w.Wp, w.Path.Count - 1)];
                }
                break;
            }
            case Walker.St.Jiggle:
                speed = Mover.RunSpeed;
                // out to the jiggle point and straight back to the corner
                target = (w.StateT < 0.3f) ? w.JiggleTo : w.Ent.Corner;
                if (w.StateT > 0.75f)
                {
                    w.Jiggles--;
                    w.StateT = 0;
                    if (w.Jiggles <= 0) { w.State = Walker.St.Swing; lastSwingAt = Now; }
                }
                break;
            case Walker.St.Swing:
                speed = Mover.RunSpeed;
                target = w.Peek;
                if (new Vector2(target.X - b.Feet.X, target.Z - b.Feet.Z).Length() < 0.1f) { w.State = Walker.St.Hold; w.StateT = 0; }
                break;
            case Walker.St.Hold:
                break;
        }
        var to = target - b.Feet; to.Y = 0;
        float len = to.Length();
        if (speed > 0 && len > 1e-3f)
        {
            float step = speed * dt;
            if (len <= step)
            {
                b.Feet = new Vector3(target.X, b.Feet.Y, target.Z);
                if (w.State is Walker.St.Run or Walker.St.Walk) w.Wp++;
            }
            else b.Feet += to / len * step;
            w.Vel = to / Mathf.Max(len, 1e-3f) * speed;
        }
        else w.Vel = w.Vel.MoveToward(Vector3.Zero, (Tier >= 2 ? 45f : 15f) * dt); // Veteran+ stop dead after the swing
        var f = b.Feet;
        f.Y = Collision.Ground(f + new Vector3(0, 0.3f, 0), G.Solid);
        b.Feet = f;
        b.Velocity = w.Vel;
        w.Steps.Update(G, MapSpot, f, w.Vel.Length(), b.Crouched, dt);

        // Look where it's heading; once peeking, pre-aim the defender's spot.
        if (!w.Brain.SeesPlayer)
        {
            var look = !w.Decoy && w.State is Walker.St.Swing or Walker.St.Hold or Walker.St.Jiggle ? G.View.Eye - b.Head : new Vector3(w.Vel.X, 0, w.Vel.Z);
            if (look.LengthSquared() > 0.01f)
            {
                b.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(look.X, -look.Z));
                var held = look.Normalized();
                if (w.State is Walker.St.Swing or Walker.St.Hold or Walker.St.Jiggle)
                    held = held.Rotated(Vector3.Up, Mathf.DegToRad(R(-1, 1) * Difficulty.Get(Tier).Bot.XhairErrDeg));
                w.Brain.HeldDir = held;
            }
        }
    }

    bool Hidden(List<Vector3> path)
    {
        var eye = Eye(SpawnFeet);
        var prev = path[0];
        foreach (var p in path.Take(path.Count - 1))
        {
            float l = prev.DistanceTo(p);
            for (float t = 0; t <= l; t += 0.5f)
            {
                var q = prev.Lerp(p, l < 1e-3f ? 1 : t / l);
                if (!Collision.Blocked(eye, Eye(q), G.Solid) || !Collision.Blocked(eye, q + new Vector3(0, 1.0f, 0), G.Solid)) return false;
            }
            prev = p;
        }
        return true;
    }

    void BeginPeek(Walker w)
    {
        w.Brain.Enabled = true;
        w.StateT = 0;
        if (w.Jiggles > 0)
        {
            var dir = w.Peek - w.Ent.Corner; dir.Y = 0;
            w.JiggleTo = w.Ent.Corner + (dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Forward) * 0.9f;
            w.State = Walker.St.Jiggle;
        }
        else { w.State = Walker.St.Swing; lastSwingAt = Now; }
    }

    void EndRep(bool cleared)
    {
        ph = Ph.After; phT = 0;
        if (!cleared) repsLost++;
        var seen = walkers.Where(w => w.Seen).ToList();
        if (seen.Count > 0)
        {
            var w = seen[0];
            string r = w.Hit ? $" · first hit {(reacts.Count > 0 ? reacts[^1] : 0):0} ms" : "";
            lastLine = $"Pre-aim error {w.Err:0.0}°{r}";
            G.Banner(cleared ? $"{w.Err:0.0}° OFF{r.ToUpperInvariant()}" : "THEY GOT AWAY", cleared ? (w.Err <= 10f ? UiTheme.Good : UiTheme.Warn) : UiTheme.Dim);
        }
        Event(cleared ? "round_won" : "round_lost");
        foreach (var w in walkers) w.Brain.Enabled = false;
    }

    public override void OnPlayerDied()
    {
        Deaths++; deaths2++; Score -= 100;
        Log("you died");
        Event("round_lost");
        G.Banner("YOU DIED", UiTheme.Accent);
        ph = Ph.After; phT = 0;
        repsLost++;
        foreach (var w in walkers) w.Brain.Enabled = false;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (ph != Ph.Live) return float.PositiveInfinity;
        var live = walkers.Where(w => w.Alive).ToList();
        var hit = ShootBots(live.Select(w => w.Body), o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        var wk = live.First(w => w.Body == hit);
        if (!wk.Hit && wk.Seen)
        {
            wk.Hit = true;
            float ms = (Now - wk.SeenAt) * 1000f;
            reacts.Add(ms);
            Event("reaction", ms);
            Score += (int)Mathf.Max(0, (700 - ms) / 5);
        }
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed) Score += 100;
        return dist;
    }

    // ------------------------------------------------------------------ HUD / results

    public override IEnumerable<string> HudLines()
    {
        yield return $"Rep {Math.Max(1, reps)} · on the angle {locked}/{errs.Count}";
        if (ph == Ph.Live && walkers.All(w => !w.Seen)) yield return "Listen — where will they come from?";
        if (lastLine.Length > 0) yield return lastLine;
        if (ents.Count == 0) yield return "No usable corners on this map spot";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} · {spotName} ({ents.Count} corners)");
        yield return ("Peeks", $"{errs.Count} seen in {reps} reps");
        if (errs.Count > 0)
        {
            yield return ("Pre-aim error", $"{Median(errs):0.0}° median");
            yield return ("On the angle (≤ 10°)", $"{100f * locked / errs.Count:0}% of peeks");
        }
        if (fakes > 0) yield return ("Fakes read", $"{fakesRead} of {fakes}");
        if (reacts.Count > 0) yield return ("First sight → first hit", $"{Median(reacts):0} ms median");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%");
    }

    static readonly float[] ReactMs = { 1000, 800, 640, 520, 430 };

    public override int Badge()
    {
        if (errs.Count == 0) return -1;
        float killRate = (float)Kills / Math.Max(1, errs.Count);
        int b = Difficulty.BadgeTtk(Tier, killRate, reacts.Count > 0 ? Median(reacts) : float.PositiveInfinity, ReactMs);
        if (Median(errs) > 12f) b--; // not listening: flicking to every peek doesn't earn the badge
        return Math.Max(-1, b);
    }
}
