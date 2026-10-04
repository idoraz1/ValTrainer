using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Site Anchor ("anchor", or "anchor:&lt;agent&gt;" with a sentinel's setup). You hold a real site-level defender spot alone.
/// Every round: a short setup phase (place your sentinel utility like in the buy phase: aim, LMB), then 2–4 attackers
/// (by tier) run up behind the choke — you can hear them — stack out of sight and execute through it on the map's
/// navigation grid: higher tiers flash first and swing together, lower tiers stagger. They clear common angles, duel
/// you with the tier's bot model, shoot utility they spot, must break walls in their way, and one carries the spike to
/// plant (4 s). You hold the round by killing them all (or surviving until time runs out without a plant).
/// Scored on rounds held, first-blood rate, time alive, kills and deaths, plus the setup's value: enemies caught,
/// reveals, kills on enemies your utility affected, flashes intercepted.
/// </summary>
public sealed partial class AnchorMode : MapMode
{
    /// <summary>Arguments this drill supports (lower-case agent keys); the Agents screen lists one entry per item.</summary>
    public static readonly string[] Agents = { "killjoy", "cypher", "deadlock", "vyse", "sage", "veto", "chamber" };
    public readonly string Agent;

    public AnchorMode(string agent = "")
    {
        var a = string.IsNullOrWhiteSpace(agent) ? "" : agent.Trim().ToLowerInvariant();
        Agent = Agents.Contains(a) ? a : "";
    }

    public override string Key => Agent.Length == 0 ? "anchor" : "anchor:" + Agent;
    public override string Name => "Site Anchor";
    public override string Description => Agent.Length == 0
        ? "Hold the site alone while 2–4 attackers execute through the choke. Hear them, hold your angle, win the duels."
        : $"Hold the site alone with {AgentName}'s setup: place it, then stall and kill the execute.";
    public override string Category => Agent.Length == 0 ? "Map" : "Agents";
    public override float Duration => 180f;
    public override string SimKind => "bot";
    public override string? Subtitle => Agent.Length == 0
        ? $"{MapSpot.Map} {MapSpot.Name} · {anchorName}"
        : $"{AgentName}  —  {MapSpot.Map} {MapSpot.Name} · {anchorName}";
    public override AimFocus? Focus => phase == Phase.Live ? NearestHead(attackers.Where(a => a.Alive).Select(a => a.Body)) : null;

    string AgentName => Kit.Length > 0 ? Kit[0].Agent : "";
    SentinelAbility[] Kit => Agent.Length == 0 ? Array.Empty<SentinelAbility>() : SentinelAbility.For(Agent);

    enum Phase { Setup, Live, After }
    Phase phase;
    float phaseT, setupLen, liveStart;

    BotNav nav = null!;
    EnemySpot anchor = null!;
    string anchorName = "";
    readonly List<Vector3> angles = new();
    List<Vector3> approachPts = new();
    int lastHidden;
    readonly List<Vector3> entryPool = new();
    Vector3 plantSpot, toSite;

    readonly List<Attacker> attackers = new();
    readonly List<Device> devices = new();
    readonly List<SentinelKit> kits = new();
    readonly List<(SentinelAbility A, Placement P)> memory = new();
    readonly List<(string Text, float Until, Color Color)> notes = new();
    SentinelPreview? preview;

    // execute
    bool executing;
    float executeAt;
    FlashOrb? orb;
    FlashAgent? flashAgent;
    Vector3 flashFrom, flashCtrl, flashPop;
    float flashT, flashFlight;
    bool planted;

    // stats
    int rounds, held, lost, firstBloods, roundKills, plants, utilKills, utilDirectKills, caughtCount, triggers, destroyedByEnemy, intercepted, flashesThrown, flashedFull;
    internal int reveals;
    readonly List<float> timeAlive = new();
    readonly HashSet<(Attacker, SentKind)> caughtPairs = new();
    readonly Dictionary<SentKind, int> caughtBy = new();

    Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);
    float RoundTime => Difficulty.SiteRoundTime(Tier) + 15f;

    static readonly bool DevLog = CmdLine.Dev;
    void Log(string s) { if (DevLog) GD.Print($"[anchor] {Now,6:0.0}s {s}"); }

    // ------------------------------------------------------------------ setup

    protected override void Setup()
    {
        nav = BotNav.For($"{MapSpot.Key}:{G.Solid.Count}", G.Solid, MapSpot.StartFeet);
        ChooseAnchor();
        foreach (var a in Kit)
        {
            var k = new SentinelKit(G, a);
            WireKit(k);
            kits.Add(k);
        }
        if (kits.Count > 0) preview = SentinelPreview.Create(G.World, kits[0].A.Color);
        Log($"map {MapSpot.Key} anchor '{anchorName}' {anchor.Feet} tier {Tier} agent '{Agent}' nav {nav.Count} nodes, approach {approachPts.Count} pts (hidden to #{lastHidden}), entries {entryPool.Count}, plant {plantSpot}");
        NewRound();
    }

    void ChooseAnchor()
    {
        var choke = MapSpot.Choke;
        var startEye = MapSpot.StartFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        bool SeesChoke(EnemySpot e) => MapSpot.LineOfSight(EyeOf(e), choke + new Vector3(0, 1.5f, 0));
        bool SiteLevel(EnemySpot e) => e.Feet.Y - choke.Y < 1.5f && e.Feet.DistanceTo(choke) is > 6f and < 28f;
        var pool = MapSpot.Enemies.Where(e => SiteLevel(e) && SeesChoke(e) && !MapSpot.LineOfSight(EyeOf(e), startEye)).ToArray();
        if (pool.Length == 0) pool = MapSpot.Enemies.Where(e => SiteLevel(e) && SeesChoke(e)).ToArray();
        if (pool.Length == 0) pool = MapSpot.Enemies;
        anchor = pool[Rng.Next(pool.Length)];
        anchorName = anchor.Name;
        SpawnFeet = anchor.Feet;
        var d = choke - anchor.Feet;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        toSite = new Vector3(anchor.Feet.X - choke.X, 0, anchor.Feet.Z - choke.Z).Normalized();

        angles.Clear();
        foreach (var e in MapSpot.Enemies) angles.Add(EyeOf(e));

        // The attackers' approach: start → choke, sampled every 0.5 m; the last point the anchor can't see is the stack.
        var path = nav.FindPath(MapSpot.StartFeet, choke) ?? new List<Vector3> { choke };
        var pts = new List<Vector3> { nav.Pos[nav.Nearest(MapSpot.StartFeet)] };
        var prev = pts[0];
        foreach (var p in path)
        {
            float len = prev.DistanceTo(p);
            for (float s = 0.5f; s < len; s += 0.5f) pts.Add(prev.Lerp(p, s / len));
            pts.Add(p);
            prev = p;
        }
        approachPts = pts;
        // The attack direction through the choke (the site lies beyond it): from ~3 m before the choke on the approach.
        for (int j = pts.Count - 1; j >= 0; j--)
        {
            var off = new Vector3(choke.X - pts[j].X, 0, choke.Z - pts[j].Z);
            if (off.Length() < 3f) continue;
            toSite = off.Normalized();
            break;
        }
        var eye = EyeOf(anchor);
        lastHidden = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var e = pts[i] + new Vector3(0, PlayerView.EyeHeight, 0);
            if (MapSpot.LineOfSight(eye, e) || MapSpot.LineOfSight(eye, e + Vector3.Down * 0.6f)) break;
            lastHidden = i;
        }

        // Entry spots: walkable floor on the site side of the choke, in view of the choke.
        entryPool.Clear();
        var chokeEye = choke + new Vector3(0, 1.5f, 0);
        for (int n = 0; n < nav.Count; n++)
        {
            var p = nav.Pos[n];
            var off = new Vector3(p.X - choke.X, 0, p.Z - choke.Z);
            float dist = off.Length();
            if (dist < 3f || dist > 13f || off.Dot(toSite) < 0.35f * dist) continue;
            if (Mathf.Abs(p.Y - choke.Y) > 2.5f || nav.Degree(n) < 8) continue;
            if (!MapSpot.LineOfSight(chokeEye, p + new Vector3(0, 1.5f, 0))) continue;
            entryPool.Add(p);
        }
        if (entryPool.Count == 0) entryPool.Add(nav.Pos[nav.Nearest(choke + toSite * 4f)]);

        // Plant spot: the map's own, or the middle of the site-level defender spots.
        if (MapSpot.PlantSpots.Length > 0) plantSpot = MapSpot.PlantSpots[Rng.Next(MapSpot.PlantSpots.Length)];
        else
        {
            var site = MapSpot.Enemies.Where(SiteLevel).Select(e => e.Feet).ToList();
            var c = site.Count > 0 ? site.Aggregate(Vector3.Zero, (s, v) => s + v) / site.Count : choke + toSite * 6f;
            plantSpot = c;
        }
        // Inside the site: an entry spot at least 5 m past the choke, nearest that point.
        var inside = entryPool.Where(p => new Vector2(p.X - choke.X, p.Z - choke.Z).Length() >= 5f).ToList();
        var ps = plantSpot;
        plantSpot = inside.Count > 0 && MapSpot.PlantSpots.Length == 0 ? inside.OrderBy(p => p.DistanceTo(ps)).First() : nav.Pos[nav.Nearest(plantSpot)];
    }

    void NewRound()
    {
        foreach (var a in attackers) if (GodotObject.IsInstanceValid(a.Body)) G.Despawn(a.Body);
        attackers.Clear();
        foreach (var d in devices) d.Free();
        devices.Clear();
        G.ClearSmokes();
        if (orb != null && GodotObject.IsInstanceValid(orb)) G.Despawn(orb);
        orb = null;
        executing = false;
        planted = false;
        roundKills = 0;
        caughtPairs.Clear();
        G.Respawn(SpawnFeet, SpawnYaw);
        G.Blind.Reset();
        G.Mover.ResetStops();

        // Persistent setup comes back where you left it, armed.
        foreach (var (a, p) in memory)
        {
            var dev = CreateDevice(a, p);
            if (dev == null) continue;
            dev.PlacedAt = Now - dev.DeployTime - 0.01f;
            dev.From = p;
        }
        foreach (var k in kits) k.ResetRound(memory.Count(x => x.A == k.A));

        phase = Phase.Setup;
        phaseT = 0;
        setupLen = kits.Count == 0 ? 3f : rounds == 0 ? 20f : 8f;
        DevNewRound();
    }

    void StartLive()
    {
        phase = Phase.Live;
        phaseT = 0;
        liveStart = Now;
        rounds++;
        SpawnAttackers();
        G.Banner("THEY'RE COMING", UiTheme.Warn);
        Log($"round {rounds}: {attackers.Count} attackers");
    }

    void SpawnAttackers()
    {
        int n = Tier switch { 0 => 2, 1 => 2, 2 => Rng.NextDouble() < 0.5 ? 3 : 2, 3 => 3, _ => Rng.NextDouble() < 0.5 ? 4 : 3 };
        var targets = entryPool.OrderBy(_ => Rng.Next()).ToList();
        // spread the entry spots: greedily take spots at least 2.5 m apart
        var picks = new List<Vector3>();
        foreach (var t in targets) { if (picks.All(p => p.DistanceTo(t) > 2.5f)) picks.Add(t); if (picks.Count >= n) break; }
        while (picks.Count < n) picks.Add(targets[Rng.Next(targets.Count)]);
        var chokeP = nav.Pos[nav.Nearest(MapSpot.Choke)];
        for (int i = 0; i < n; i++)
        {
            int stackIdx = Math.Max(0, lastHidden - 2 - 3 * i);
            int startIdx = Math.Max(0, stackIdx - 20 - (int)R(0, 12));
            var start = approachPts[startIdx];
            var stack = approachPts[stackIdx];
            var body = G.SpawnBot(start);
            body.QuietSteps = true;
            var brain = new BotBrain(body, Difficulty.Get(Tier).Bot, Rng) { ShoulderSight = Difficulty.ShoulderSight(Tier) };
            var at = new Attacker(this, i, body, brain) { Carrier = i == 0, StackAt = stack };
            var toStack = stack - start;
            body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(toStack.X, -toStack.Z));
            brain.HeldDir = toStack.LengthSquared() > 0.01f ? toStack.Normalized() : Vector3.Forward;
            RevealFx.Attach(body, G.Enemy.Lerp(Colors.White, 0.2f));
            at.SetPath(nav.FindPath(start, stack) ?? new List<Vector3> { stack });
            var goal = at.Carrier ? plantSpot : picks[i];
            var entry = nav.FindPath(stack, chokeP) ?? new List<Vector3>();
            entry.AddRange(nav.FindPath(chokeP, goal) ?? new List<Vector3> { goal });
            at.EntryPath = entry;
            attackers.Add(at);
        }
        executeAt = Now + 12f; // at the latest
    }

    // ------------------------------------------------------------------ frame

    public override void Update(float dt)
    {
        phaseT += dt;
        notes.RemoveAll(n => Now > n.Until);
        foreach (var k in kits) k.Update(dt);
        UpdatePreview();
        UpdateThrows(dt);
        DevUpdate(dt);
        if (phase != Phase.After) foreach (var d in devices.ToArray()) if (!d.Dead && !d.Spent) d.Update(dt);
        devices.RemoveAll(d => (d.Dead || d.Spent) && d.From == null);

        switch (phase)
        {
            case Phase.Setup:
                if (phaseT >= setupLen) StartLive();
                break;
            case Phase.Live:
                UpdateExecute(dt);
                foreach (var a in attackers)
                {
                    a.Update(dt);
                    if (phase != Phase.Live) return; // you died / they planted
                    if (a.Alive && !a.SeenByPlayer && G.LineOfSight(G.View.Eye, a.Body.Head))
                    {
                        a.SeenByPlayer = true;
                        SeenEvent(a.Body.Head);
                    }
                    if (GodotObject.IsInstanceValid(a.Body))
                        RevealFx.Set(a.Body, a.Alive && a.IsRevealed ? 1f : 0f);
                }
                if (attackers.Count > 0 && attackers.All(a => !a.Alive)) EndRound(true, $"SITE HELD  {Now - liveStart:0.0}s");
                else if (Now - liveStart > RoundTime) EndRound(true, "TIME — they didn't plant");
                else if (attackers.Count > 0 && !attackers.Any(a => a.Alive && a.Carrier))
                {
                    // The carrier died: the nearest teammate picks up the spike and goes for the plant.
                    var next = attackers.Where(a => a.Alive).OrderBy(a => a.Feet.DistanceTo(plantSpot)).FirstOrDefault();
                    if (next != null && next.State is not Attacker.St.Approach and not Attacker.St.Stack)
                    {
                        next.Carrier = true;
                        var p = nav.FindPath(next.Feet, plantSpot);
                        if (p != null) { next.State = Attacker.St.Entry; next.SetPath(p); }
                    }
                    else if (next != null) next.Carrier = true;
                }
                break;
            case Phase.After:
                if (phaseT >= 2f) NewRound();
                break;
        }
    }

    void UpdateExecute(float dt)
    {
        if (!executing)
        {
            bool stacked = attackers.Where(a => a.Alive).All(a => a.State == Attacker.St.Stack);
            if ((stacked && phaseT > 1.5f) || Now >= executeAt || attackers.Any(a => a.Alive && a.Brain.SeesPlayer)) BeginExecute();
            return;
        }
        if (orb != null) UpdateFlash(dt);
    }

    void BeginExecute()
    {
        executing = true;
        float flashChance = Agent == "veto" ? 0.9f : Difficulty.T(Tier, 0.15f, 0.3f, 0.5f, 0.65f, 0.8f);
        float go = Now + R(0.3f, 0.9f);
        if (MapSpot.Flashes.Length > 0 && Rng.NextDouble() < flashChance)
        {
            ThrowFlash();
            var (a, b) = Difficulty.FlashSwingDelay(Tier);
            go = Now + flashFlight - 0.15f + R(a, b) * 0.6f;
        }
        int i = 0;
        foreach (var at in attackers.Where(x => x.Alive).OrderBy(_ => Rng.Next()))
        {
            float gap = Tier >= 3 ? R(0f, 0.25f) : R(0.5f, 1.3f);
            at.GoAt = go + gap * i++;
        }
        Log($"execute{(orb != null ? $" with {flashAgent?.Agent} flash" : "")}");
    }

    void ThrowFlash()
    {
        var all = FlashAgent.All.Where(f => f.Path is FlashPath.Curve or FlashPath.Arc or FlashPath.Hawk).ToArray();
        flashAgent = all[Rng.Next(all.Length)];
        var seen = MapSpot.Flashes.Where(f => G.LineOfSight(G.View.Eye, f.Pop)).ToArray();
        var lineup = seen.Length > 0 ? seen[Rng.Next(seen.Length)] : MapSpot.Flashes[Rng.Next(MapSpot.Flashes.Length)];
        flashFrom = lineup.From; flashPop = lineup.Pop;
        flashCtrl = (flashFrom + flashPop) / 2 + (flashAgent.Path == FlashPath.Arc ? new Vector3(0, 2.5f, 0) : Vector3.Zero);
        flashFlight = flashAgent.Path == FlashPath.Hawk ? Mathf.Max(0.6f, flashFrom.DistanceTo(flashPop) / flashAgent.Speed) + flashAgent.Fuse
                    : Mathf.Clamp(flashAgent.Fuse, 0.6f, 1.6f);
        flashT = 0;
        orb = G.SpawnOrb(flashAgent.Color);
        orb.GlobalPosition = flashFrom;
        G.SoundAt(flashAgent.Cue, flashFrom, maxRange: 50f, floor: 0.6f);
        flashesThrown++;
        DevFlashThrown();
    }

    Vector3 OrbPos(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        float u = 1 - t;
        return u * u * flashFrom + 2 * u * t * flashCtrl + t * t * flashPop;
    }

    void UpdateFlash(float dt)
    {
        flashT += dt;
        if (orb == null || !GodotObject.IsInstanceValid(orb)) { orb = null; return; }
        var p = OrbPos(flashT / flashFlight);
        orb.GlobalPosition = p;
        // Veto's Interceptor zaps it before it pops.
        foreach (var ic in devices.OfType<InterceptorDev>())
        {
            if (!ic.TryZap(p)) continue;
            intercepted++;
            Score += 60;
            Event("util_intercepted", (int)SentKind.Interceptor);
            Note("FLASH INTERCEPTED", UiTheme.Good);
            G.Despawn(orb);
            orb = null;
            Log("flash intercepted");
            return;
        }
        if (flashT < flashFlight) return;
        float aspect = ScreenAspect();
        var (sec, grade) = BlindModel.Evaluate(flashAgent!.BlindMax, G.View, flashPop, G.LineOfSight(G.View.Eye, flashPop), aspect);
        G.Blind.Apply(sec);
        Event("flash", grade switch { BlindGrade.Flashed => 2, BlindGrade.Partial => 1, BlindGrade.NoLineOfSight => -1, _ => 0 }, -1);
        if (grade == BlindGrade.Flashed) { flashedFull++; Note($"FLASHED {sec:0.0}s · {flashAgent.Agent}", UiTheme.Accent); }
        else if (grade == BlindGrade.Dodged) Note($"Flash dodged · {flashAgent.Agent}", UiTheme.Good);
        orb.Pop();
        orb = null;
        Log($"flash popped: {grade} {sec:0.0}s");
        G.SoundAt("flashpop", flashPop, maxRange: 50f, floor: 0.6f);
    }

    float ScreenAspect()
    {
        var size = ((Node)G).GetViewport().GetVisibleRect().Size;
        return size.X / Mathf.Max(1, size.Y);
    }

    void EndRound(bool won, string text)
    {
        if (phase != Phase.Live) return;
        timeAlive.Add(G.Player.Dead ? Now - liveStart : Now - liveStart);
        if (won) { held++; Score += 300; Event("round_won", Now - liveStart); G.Banner(text, UiTheme.Good); }
        else { lost++; Event("round_lost"); G.Banner(text, UiTheme.Accent); }
        Log($"round {rounds} {(won ? "HELD" : "LOST")} ({text}) kills {roundKills} alive {attackers.Count(a => a.Alive)} caught {caughtPairs.Count}");
        foreach (var k in kits) k.PutAway(0f);
        phase = Phase.After;
        phaseT = 0;
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Score -= 100;
        EndRound(false, "YOU DIED");
    }

    void OnPlanted(Attacker a)
    {
        if (planted || phase != Phase.Live) return;
        planted = true;
        plants++;
        Score -= 100;
        Event("spike_planted");
        EndRound(false, "SPIKE PLANTED");
    }

    // ------------------------------------------------------------------ hooks for devices / attackers

    void AddDevice(Device d) => devices.Add(d);

    bool MoveBlocked(Vector3 a, Vector3 b, out Device wall)
    {
        foreach (var d in devices)
            if (!d.Dead && !d.Spent && d.BlocksMove(a, b, out wall)) return true;
        wall = null!;
        return false;
    }

    void Moved(Attacker at, Vector3 from, Vector3 to)
    {
        foreach (var d in devices) if (!d.Dead && !d.Spent) d.OnMoved(at, from, to);
    }

    void Caught(Attacker at, Device d)
    {
        at.Caught = true;
        at.LastUtil = d;
        at.UtilHitAt = Now;
        if (!caughtPairs.Add((at, d.A.Kind))) return;
        caughtCount++;
        caughtBy[d.A.Kind] = caughtBy.GetValueOrDefault(d.A.Kind) + 1;
        Score += 30;
        Event("util_caught", (int)d.A.Kind, at.Index);
    }

    void UtilTrigger(Device d, int affected)
    {
        triggers++;
        Event("util_trigger", (int)d.A.Kind, affected);
        string what = d.A.Kind switch
        {
            SentKind.Alarmbot => affected > 0 ? $"ALARMBOT · {affected} vulnerable" : "Alarmbot went off",
            SentKind.Turret => "TURRET · contact",
            SentKind.Trapwire => "TRAPWIRE · caught one",
            SentKind.SonicSensor => affected > 0 ? $"SONIC SENSOR · {affected} concussed" : "Sonic sensor fired",
            SentKind.Shear => "SHEAR · wall up",
            SentKind.Chokehold => $"CHOKEHOLD · {affected} held",
            SentKind.Trademark => "TRADEMARK · tagged",
            SentKind.Razorvine => $"Razorvine: {affected} cut",
            SentKind.SlowOrb => $"Slow orb: {affected} slowed",
            SentKind.Interceptor => affected > 0 ? $"Interceptor: {affected} zapped" : "Interceptor: nothing to zap",
            _ => d.A.Ability,
        };
        if (d.A.Kind != SentKind.Turret || affected > 0) Note(what, d.A.Color);
        Log($"trigger {d.A.Ability}: {affected} affected");
    }

    void OnDeviceDestroyed(Device d)
    {
        if (d is SageSegDev or MeshOrbDev) { Event("util_broken", (int)d.A.Kind); Note($"{d.A.Ability} broken", UiTheme.Warn); return; }
        destroyedByEnemy++;
        Event("util_destroyed", (int)d.A.Kind);
        Note($"{d.A.Ability} destroyed", UiTheme.Warn);
        SentinelSfx.PlayAt(G, "broken", d.AimPoint, 0.9f);
    }

    void OnUtilKilled(Attacker at, Device? src)
    {
        utilDirectKills++;
        Score += 100;
        Event("util_kill_direct", src != null ? (int)src.A.Kind : -1);
        Note($"{src?.A.Ability ?? "Utility"} got a kill", UiTheme.Good);
    }

    void Tracer(Vector3 a, Vector3 b, Color c, float life = 0.06f, float radius = 0.015f) => Effects.Tracer(a, b, c);

    void Note(string text, Color c)
    {
        notes.Add((text, Now + 2.4f, c));
        if (notes.Count > 4) notes.RemoveAt(0);
    }

    // ------------------------------------------------------------------ shooting

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        float block = float.PositiveInfinity;
        foreach (var dev in devices) if (!dev.Dead && !dev.Spent) block = Mathf.Min(block, dev.BulletBlock(o, d));
        if (phase != Phase.Live) return block;
        var live = attackers.Where(a => a.Alive).ToList();
        var hit = ShootBots(live.Select(a => a.Body), o, d, out var zone, out bool killed, out float dist, Mathf.Min(Wall(o, d), block));
        if (hit == null) return block;
        var at = live.First(a => a.Body == hit);
        if (!killed && at.IsVulnerable)
        {
            // Vulnerable: your bullets do double damage.
            var w = G.Weapon ?? Weapons.Vandal;
            hit.Hp -= (int)Mathf.Round(w.Damage((int)zone, dist));
            if (hit.Dead) { killed = true; hit.Die(d); RegisterKill(hit.SpawnTime, zone == HitZone.Head); }
        }
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100;
            if (roundKills == 0 && attackers.All(a => a == at || a.Alive)) { firstBloods++; Event("first_blood", Now - liveStart); }
            roundKills++;
            if (at.Affected)
            {
                utilKills++;
                Score += 50;
                Event("util_kill", (Now - at.UtilHitAt) * 1000f, at.LastUtil != null ? (int)at.LastUtil.A.Kind : -1);
            }
        }
        return dist;
    }

    // ------------------------------------------------------------------ HUD

    public override string? Prompt
    {
        get
        {
            var k = kits.FirstOrDefault(x => x.InHand);
            if (k != null)
            {
                if (k.RefusalT > 0 && k.Refusal != null) return k.Refusal;
                return k.A.Alt != null ? $"{k.A.Primary}   ·   {k.A.Alt}" : k.A.Primary;
            }
            var refused = kits.FirstOrDefault(x => x.RefusalT > 0 && x.Refusal != null);
            if (refused != null) return refused.Refusal;
            if (phase == Phase.Live)
            {
                var vine = devices.OfType<RazorvineDev>().FirstOrDefault(v => v.CanActivate);
                var ic = devices.OfType<InterceptorDev>().FirstOrDefault(v => v.CanActivate);
                var kv = kits.FirstOrDefault(x => x.A.Kind == SentKind.Razorvine);
                var ki = kits.FirstOrDefault(x => x.A.Kind == SentKind.Interceptor);
                if (vine != null && kv != null && attackers.Any(a => a.Alive && a.Feet.DistanceTo(vine.Pos) < 7f)) return $"{kv.KeyText}: activate Razorvine now";
                if (ic != null && ki != null && orb != null) return $"{ki.KeyText}: switch the Interceptor on";
            }
            return null;
        }
    }

    public override IEnumerable<string> HudLines()
    {
        if (phase == Phase.Setup)
        {
            if (kits.Count == 0) { yield return "Hold your angle — listen for the push"; yield break; }
            foreach (var k in kits)
            {
                bool placed = devices.Any(d => d.A == k.A && !d.Dead) || k.Charges < k.A.Charges;
                yield return $"[{k.KeyText}] {k.A.Ability}{(placed ? " — placed" : "")}";
            }
            if (rounds == 0)
                foreach (var k in kits) yield return k.A.HowTo;
            yield break;
        }
        if (phase == Phase.Live)
        {
            yield return $"Attackers left: {attackers.Count(a => a.Alive)}";
            var planter = attackers.FirstOrDefault(a => a.Alive && a.State == Attacker.St.Plant);
            if (planter != null) yield return $"SPIKE BEING PLANTED  {SpikePlantTime - planter.PlantT:0.0}s";
            if (kits.Count > 0 && caughtPairs.Count > 0) yield return $"Caught by your setup: {caughtPairs.Count}";
        }
        foreach (var n in notes) yield return n.Text;
    }

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        float k = UiTheme.S(size);
        if (phase == Phase.Setup && phaseT > 0f) DrawSetupTimer(c, size, k); // not under the start countdown
        if (phase == Phase.Live) DrawMarkers(c, size, k);
        for (int i = 0; i < kits.Count; i++) DrawPlate(c, size, k, kits[i], i);
    }

    void DrawSetupTimer(CanvasItem c, Vector2 size, float k)
    {
        float left = Mathf.Max(0, setupLen - phaseT);
        int fs = UiTheme.Fs(30, k), ss = UiTheme.Fs(14, k);
        float cx = size.X / 2, y = 150 * k;
        Gfx.TextC(c, UiTheme.Display, kits.Count > 0 ? $"SETUP  {left:0}" : $"GET READY  {left:0}", cx, y, fs, UiTheme.Text);
        Gfx.TextC(c, UiTheme.HudWide, kits.Count > 0 ? "PLACE YOUR UTILITY — THE PUSH COMES AFTER THE SETUP" : "THE ATTACKERS EXECUTE THROUGH THE CHOKE", cx, y + 24 * k, ss, UiTheme.Dim);
        float w = 320 * k;
        c.DrawRect(new Rect2(cx - w / 2, y + 36 * k, w, 3 * k), new Color(1, 1, 1, 0.12f));
        c.DrawRect(new Rect2(cx - w / 2, y + 36 * k, w * (left / Mathf.Max(0.1f, setupLen)), 3 * k), kits.Count > 0 ? kits[0].A.Color : UiTheme.Warn);
    }

    void DrawPlate(CanvasItem c, Vector2 size, float k, SentinelKit kit, int i)
    {
        float cx = size.X / 2, bw = 250 * k, bh = 74 * k;
        float ammoY = size.Y - 30 * k - bh;
        var r = new Rect2(cx + 140 * k + i * (bw + 10 * k), ammoY - 12 * k - 64 * k, bw, 64 * k);
        if (r.End.X > size.X - 8 * k) r.Position = new Vector2(size.X - 8 * k - bw - (kits.Count - 1 - i) * (bw + 10 * k), r.Position.Y - 70 * k);
        Gfx.Plate(c, r, 10 * k, UiTheme.Plate, new Color(UiTheme.Text, 0.08f));
        bool ready = kit.Charges > 0;
        var accent = new Color(kit.A.Color, ready ? 1f : 0.35f);
        var key = new Rect2(r.Position + new Vector2(12 * k, 12 * k), new Vector2(40 * k, 40 * k));
        c.DrawRect(key, new Color(0, 0, 0, 0.35f));
        c.DrawRect(key, accent, false, Mathf.Max(1f, 2f * k));
        string cap = InputBinding.Cap(kit.KeyText);
        int ks = UiTheme.Fs(cap.Length > 2 ? 12 : 22, k);
        Gfx.TextC(c, UiTheme.Display, cap, key.GetCenter().X, Gfx.Mid(key.GetCenter().Y, ks), ks, UiTheme.Text);
        int ns = UiTheme.Fs(16, k), ss = UiTheme.Fs(12, k);
        float tx = key.End.X + 12 * k;
        Gfx.Text(c, UiTheme.HudWide, kit.A.Ability.ToUpperInvariant(), tx, Gfx.Mid(r.Position.Y + 22 * k, ns), ns, UiTheme.Text);
        var out_ = devices.FirstOrDefault(d => d.A == kit.A && !d.Dead && !d.Spent);
        string state = kit.State switch
        {
            SentinelKit.Phase.Equipping => "EQUIPPING…",
            SentinelKit.Phase.Equipped => kit.A.Kind == SentKind.BarrierOrb ? $"PLACE · {(kit.Alt ? "ROTATED" : "ACROSS")}" : "PLACE IT",
            _ => out_ is RazorvineDev { CanActivate: true } || out_ is InterceptorDev { CanActivate: true } ? "OUT · KEY TO ACTIVATE"
                 : out_ != null ? "OUT" : ready ? "READY" : "USED",
        };
        Gfx.Text(c, UiTheme.HudWide, state, tx, Gfx.Mid(r.Position.Y + 44 * k, ss), ss, kit.State != SentinelKit.Phase.Ready ? accent : UiTheme.Dim);
        for (int j = 0; j < kit.A.Charges; j++)
        {
            var pc = new Vector2(r.End.X - 18 * k - j * 18 * k, r.Position.Y + 22 * k);
            Gfx.Diamond(c, pc, 6 * k, 6 * k, j < kit.Charges ? accent : new Color(UiTheme.Text, 0.15f));
        }
        if (kit.State == SentinelKit.Phase.Equipping)
        {
            float prog = Mathf.Clamp(kit.StateTime / kit.A.Equip, 0, 1);
            float bx = r.Position.X + 12 * k, bwid = r.Size.X - 24 * k, by = r.End.Y - 7 * k;
            c.DrawRect(new Rect2(bx, by, bwid, 3 * k), new Color(1, 1, 1, 0.12f));
            c.DrawRect(new Rect2(bx, by, bwid * prog, 3 * k), accent);
        }
    }

    void DrawMarkers(CanvasItem c, Vector2 size, float k)
    {
        var cam = c.GetViewport()?.GetCamera3D();
        if (cam == null) return;
        int fs = UiTheme.Fs(12, k);
        foreach (var a in attackers)
        {
            if (!a.Alive) continue;
            bool vis = G.LineOfSight(G.View.Eye, a.Body.Head);
            string tag = a.Held ? "HELD" : a.IsVulnerable ? "VULNERABLE" : a.Brain.IsDazed(Now) ? "CONCUSSED" : a.SlowMul < 0.99f ? "SLOWED" : a.IsRevealed && !vis ? "REVEALED" : "";
            if (tag.Length == 0) continue;
            if (!OnScreen(cam, a.Body.Head + Vector3.Up * 0.45f, size, out var h)) continue;
            var tc = tag == "VULNERABLE" ? UiTheme.Accent : tag == "REVEALED" ? G.Enemy : UiTheme.Warn;
            float w = Gfx.TextW(UiTheme.HudWide, tag, fs) + 12 * k;
            c.DrawRect(new Rect2(h.X - w / 2, h.Y - fs - 4 * k, w, fs + 8 * k), new Color(0, 0, 0, 0.45f));
            Gfx.TextC(c, UiTheme.HudWide, tag, h.X, h.Y, fs, tc);
        }
    }

    static bool OnScreen(Camera3D cam, Vector3 world, Vector2 size, out Vector2 p)
    {
        p = Vector2.Zero;
        var local = cam.GlobalTransform.AffineInverse() * world;
        if (local.Z > -0.1f) return false;
        p = cam.UnprojectPosition(world);
        return float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X > -size.X && p.X < 2 * size.X && p.Y > -size.Y && p.Y < 2 * size.Y;
    }

    // ------------------------------------------------------------------ results

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} · {anchorName}");
        if (Agent.Length > 0) yield return ("Agent", $"{AgentName} — {string.Join(" + ", Kit.Select(k => k.Ability))}");
        yield return ("Rounds held / lost", $"{held} / {lost}" + (plants > 0 ? $"  ({plants} planted)" : ""));
        int played = held + lost;
        if (played > 0) yield return ("First blood", $"{100f * firstBloods / played:0}% of rounds");
        if (timeAlive.Count > 0) yield return ("Avg time alive", $"{timeAlive.Average():0.0} s per round");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        if (Agent.Length > 0)
        {
            yield return ("Caught by your setup", $"{caughtCount}" + (caughtBy.Count > 0 ? "  (" + string.Join(", ", caughtBy.Select(kv => $"{Label(kv.Key)} {kv.Value}")) + ")" : ""));
            if (reveals > 0) yield return ("Enemies revealed", reveals.ToString());
            yield return ("Utility-assisted kills", $"{utilKills} of {Kills}" + (utilDirectKills > 0 ? $" (+{utilDirectKills} by it)" : ""));
            if (flashesThrown > 0 && kits.Any(k => k.A.Kind == SentKind.Interceptor)) yield return ("Flashes intercepted", $"{intercepted} of {flashesThrown}");
            if (destroyedByEnemy > 0) yield return ("Utility they destroyed", destroyedByEnemy.ToString());
        }
        else if (flashesThrown > 0) yield return ("Flashed by their entry", $"{flashedFull} of {flashesThrown}");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%");
    }

    static string Label(SentKind k) => SentinelAbility.All.First(a => a.Kind == k).Ability;

    public override int Badge()
    {
        int played = held + lost;
        if (played == 0) return -1;
        int b = Difficulty.BadgeWinRate(Tier, (float)held / played, 0.40f);
        // A sentinel run that never caught anyone with the setup doesn't earn the drill's badge.
        if (Agent.Length > 0 && caughtCount == 0) b--;
        return Math.Max(-1, b);
    }
}
