using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>How a smoke gets to where it forms (visual only).</summary>
enum LeadKind { None, Drop, Missile }

/// <summary>
/// Smoke Execute (argument after the colon, e.g. "smokeexec:omen"): controller utility drill on a map blockout.
/// Each of the 5 rounds: (1) the execute plan — the defender spots whose sight lines onto your entry the standard smokes
/// cut (<see cref="MapSpot.Smokes"/>, or picked from the most exposed spots when a map has none); (2) a placement time
/// limit in which you put your smokes down with your agent's own mechanic (tactical map, aimed marker, Astral Form stars,
/// thrown orb / walls); (3) the execute — walk in and clear the site while smoked defenders can't see you (smokes are vision
/// blockers for the bots both ways; bullets pass through). Scored on the share of the targeted sight lines you cut,
/// placement accuracy vs the standard spots, whether the smokes were up when you entered, kills, deaths and time.
/// Results include a top-down replay of your smokes vs the standard ones.
/// Dev: --autosmoke places the standard smokes and walks the route by itself (use with --simaim for headless runs);
/// --duration N ends the run after N seconds.
/// </summary>
public sealed class SmokeExecuteMode : MapMode
{
    /// <summary>Arguments this drill supports (lower-case agent keys); the Agents screen lists one entry per item.</summary>
    public static readonly string[] Agents = { "brimstone", "omen", "astra", "clove", "viper", "harbor", "miks" };
    public readonly string Agent;
    internal readonly ControllerKit Kit;

    public SmokeExecuteMode(string agent = "omen")
    {
        Kit = ControllerKit.For(string.IsNullOrWhiteSpace(agent) ? "omen" : agent.Trim().ToLowerInvariant());
        Agent = Kit.Key;
    }

    public override string Key => "smokeexec:" + Agent;
    public override string Name => "Smoke Execute";
    public override string Description => "Place your smokes to cut the defenders' sightlines, then execute onto the site.";
    public override string Category => "Agents";
    public override bool Timed => false;
    public override bool Done => finished || (Main.I.Dev && DevDuration is { } d && Now >= d);
    public override string SimKind => "bot";
    public override string? Subtitle => $"{Kit.Agent} · {Kit.Ability} — {MapSpot.Map} {MapSpot.Name}";
    public override bool Movement => !abilities.Any(a => a.BlocksMovement);

    public override AimFocus? Focus => phase is Phase.Place or Phase.Execute && !AnyMapOpen
        ? NearestHead(defenders.Select(d => d.Body)) : null;

    // ---------------- tuning ----------------
    const int RoundsTotal = 5;
    float PlanTime => Difficulty.T(Tier, 7f, 6f, 5f, 4.5f, 4f);
    float PlaceTime => Difficulty.T(Tier, 16f, 14f, 12f, 10f, 9f);
    float ExecTime => Difficulty.T(Tier, 40f, 37f, 35f, 32f, 30f);
    const float SummaryTime = 5.5f;
    /// <summary>Rookie / Regular see the standard spots while placing; higher tiers only get the plan's callouts.</summary>
    internal bool Ghosts => Tier <= 1;
    internal int TierLevel => Tier;
    internal int Quality => Main.I.Settings.Quality;

    static readonly bool AutoFlag = CmdLine.Has("--autosmoke");
    bool Auto => Main.I.Dev && AutoFlag;
    static readonly float? DevDuration =
        float.TryParse(CmdLine.After("--duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var dd) ? dd : null;

    // ---------------- state ----------------
    internal enum Phase { Plan, Place, Execute, Summary }
    internal Phase CurrentPhase => phase;
    Phase phase = Phase.Plan;
    float phaseT, execStart, pathT;
    bool skip, finished, autoDone;

    internal ControllerNav Nav = null!;
    internal ExecutePlan Plan = null!;
    internal TacticalMap TMap = null!;
    internal List<PlanSmoke> Assigned = new();
    internal List<Threat> Targets = new();
    internal float StdCoverage;
    internal Rect2 MapArea;
    internal Rect2 WorldBounds { get; private set; }

    readonly List<SmokeAbility> abilities = new();
    internal IReadOnlyList<SmokeAbility> Abilities => abilities;
    internal readonly List<AstraStar> Stars = new();
    internal int StarsLeft;
    readonly List<float> starBack = new();
    internal readonly ViperFuel Fuel = new();

    internal sealed class Defender
    {
        public required BotCharacter Body { get; init; }
        public required BotBrain Brain { get; init; }
        public required EnemySpot Spot { get; init; }
        public bool Target;
        public int Vis = -1;
        public float ThroughT;
        public float KilledAt = -1f;
    }
    readonly List<Defender> defenders = new();
    internal IReadOnlyList<Defender> Defenders => defenders;

    internal sealed class RoundLog
    {
        public int Index;
        public string Result = "";
        public bool Won, Died;
        public float Start, End, ExecStart = -1f, EntryAt = -1f, ExecTime;
        public float Coverage;
        public List<SpotResult> Spots = new();
        public int SmokesAtEntry, SmokesUpAtEntry, Kills, Defenders, Points;
        public float SmokeSaves;
        public readonly List<(float T, Vector3 P, float Yaw)> Path = new();
        public readonly List<(SmokeVolume V, Color Tint)> Smokes = new();
        public readonly List<(EnemySpot Spot, bool Target, float KilledAt)> Defs = new();
        public bool AllUpAtEntry => EntryAt >= 0 && SmokesAtEntry > 0 && SmokesUpAtEntry == SmokesAtEntry;
    }
    readonly List<RoundLog> rounds = new();
    internal IReadOnlyList<RoundLog> Rounds => rounds;
    RoundLog cur = new();
    internal RoundLog Current => cur;

    readonly List<SmokeVolume> roundSmokes = new();
    readonly List<Node> roundNodes = new();
    SmokeScreenFog fog = null!;
    List<Vector3>? autoPath;
    int autoIdx;
    float autoWait;

    internal IGame Game => G;
    internal MapSpot Spot => MapSpot;
    internal float Clock => Now;
    internal Vector3 Feet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);
    internal bool AnyMapOpen => abilities.Any(a => a.MapOpen);
    internal bool AstralOpen => abilities.Any(a => a is AstralAbility { Equipped: true });
    internal float PhaseLeft => phase switch
    {
        Phase.Plan => PlanTime - phaseT,
        Phase.Place => PlaceTime - phaseT,
        Phase.Execute => ExecTime - (Now - execStart),
        _ => SummaryTime - phaseT,
    };
    internal float PhaseFrac => phase switch
    {
        Phase.Plan => phaseT / PlanTime,
        Phase.Place => phaseT / PlaceTime,
        Phase.Execute => (Now - execStart) / ExecTime,
        _ => phaseT / SummaryTime,
    };

    // =================================================================================================================
    // setup and rounds
    // =================================================================================================================

    protected override void Setup()
    {
        Nav = new ControllerNav(MapSpot, G.Solid);
        Plan = new ExecutePlan(MapSpot, G.Solid, Nav);
        TMap = TacticalMap.For(MapSpot, (x, z, y) => Nav.Reachable(x, z, y), MapSpot.StartFeet, Plan.Anchor);
        Assigned = Plan.AssignedFor(Kit);
        Targets = Plan.TargetsFor(Assigned);
        StdCoverage = Plan.StandardCoverage(Assigned, Targets);
        float x0 = MapSpot.Boxes.Min(b => b.Min.X), x1 = MapSpot.Boxes.Max(b => b.Max.X);
        float z0 = MapSpot.Boxes.Min(b => b.Min.Z), z1 = MapSpot.Boxes.Max(b => b.Max.Z);
        WorldBounds = new Rect2(x0, z0, x1 - x0, z1 - z0);
        SmokeAssets.ReadLighting((Node)G);
        var input = new ControllerInput { Mode = this, Name = "SmokeInput" };
        G.World.AddChild(input);
        fog = SmokeScreenFog.Create(G);
        G.World.AddChild(fog);
        var fwd = PlayerView.Dir(MapSpot.StartYaw, 0);
        SmokeFx.Prewarm(G, G.World, MapSpot.StartFeet + fwd * 6f + Vector3.Up * 1.5f, Kit.Look, Quality);
        if (Main.I.Dev) GD.Print(Plan.Describe(Kit));
        NewRound();
    }

    void BuildAbilities()
    {
        switch (Kit.Placing)
        {
            case SmokePlacing.Map: abilities.Add(new MapDropAbility(this)); break;
            case SmokePlacing.Aim: abilities.Add(new AimAbility(this)); break;
            case SmokePlacing.Stars: abilities.Add(new AstralAbility(this)); abilities.Add(new NebulaAbility(this)); break;
            case SmokePlacing.Throw: abilities.Add(new OrbAbility(this)); break;
        }
        if (Kit.Wall == WallKind.ToxicScreen) abilities.Add(new ScreenAbility(this));
        if (Kit.Wall == WallKind.HighTide) abilities.Add(new TideAbility(this));
    }

    void ClearRound()
    {
        foreach (var a in abilities) a.Clear();
        abilities.Clear();
        G.SetAbilityInHand(false, 0f);
        foreach (var d in defenders) G.Despawn(d.Body);
        defenders.Clear();
        G.ClearSmokes();
        foreach (var n in roundNodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        roundNodes.Clear();
        roundSmokes.Clear();
        fog.Tints.Clear();
        foreach (var s in Stars) if (s.Node != null && GodotObject.IsInstanceValid(s.Node)) s.Node.QueueFree();
        Stars.Clear();
        starBack.Clear();
    }

    void NewRound()
    {
        ClearRound();
        StarsLeft = Kit.Stars;
        Fuel.Fuel = 100f;
        BuildAbilities();
        G.Respawn(MapSpot.StartFeet, MapSpot.StartYaw);
        G.Mover.ResetStops();
        SpawnDefenders();
        cur = new RoundLog { Index = rounds.Count + 1, Start = Now, Defenders = defenders.Count };
        phase = Phase.Plan;
        phaseT = 0;
        skip = false;
        autoDone = false;
        autoPath = null;
        autoWait = 0;
    }

    void SpawnDefenders()
    {
        int n = Tier switch { 0 => 2, 1 => Rng.NextDouble() < 0.4 ? 3 : 2, 2 => 3, 3 => 3, _ => Rng.NextDouble() < 0.5 ? 4 : 3 };
        var startEye = MapSpot.StartFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        var exposed = new[]
        {
            startEye, startEye + Vector3.Down * 0.45f,
            startEye + new Vector3(0.35f, -0.3f, 0), startEye + new Vector3(-0.35f, -0.3f, 0),
            startEye + new Vector3(0, -0.3f, 0.35f), startEye + new Vector3(0, -0.3f, -0.35f),
        };
        bool Hidden(EnemySpot e) => exposed.All(p => !MapSpot.LineOfSight(EyeOf(e), p)) && !Plan.SeesApproach(e);
        var targetSpots = Targets.Select(t => t.Spot).Where(Hidden).OrderBy(_ => Rng.Next()).ToList();
        var threatSpots = Plan.Threats.Select(t => t.Spot).Where(s => !targetSpots.Contains(s) && Hidden(s)).OrderBy(_ => Rng.Next()).ToList();
        var others = MapSpot.Enemies.Where(e => Hidden(e) && !targetSpots.Contains(e) && !threatSpots.Contains(e)).OrderBy(_ => Rng.Next()).ToList();
        var pick = new List<EnemySpot>();
        pick.AddRange(targetSpots.Take(Math.Min(targetSpots.Count, Math.Max(1, n - 1))));
        pick.AddRange(threatSpots.Take(Math.Max(0, n - pick.Count)));
        pick.AddRange(others.Take(Math.Max(0, n - pick.Count)));
        foreach (var spot in pick)
        {
            var look = MapSpot.Choke + new Vector3(0, 1.5f, 0);
            if (Tier >= 3 && Rng.NextDouble() < 0.3) look += new Vector3(R(-4, 4), 0, R(-2, 2));
            var (body, brain) = SpawnDefender(spot, look);
            defenders.Add(new Defender { Body = body, Brain = brain, Spot = spot, Target = targetSpots.Contains(spot) });
        }
    }

    void StartPlace()
    {
        phase = Phase.Place;
        phaseT = 0;
        skip = false;
        pathT = 0;
        if (!Auto && abilities.Count > 0) abilities[0].OnKey(); // the main smoke ability comes out right away
        G.Sound("go", 0.5f);
    }

    /// <summary>Everything this round's placement needs is down (Astra: Astral Form visited and left).</summary>
    bool PlacementComplete
    {
        get
        {
            if (abilities.Count == 0) return true;
            if (Kit.Placing == SmokePlacing.Stars)
                return abilities.OfType<AstralAbility>().All(a => a.Visited && !a.Equipped);
            return abilities.All(a => a.Spent);
        }
    }

    void StartExecute()
    {
        foreach (var a in abilities)
        {
            if (a is MapDropAbility md) md.LaunchPending();
            else if (a.Equipped && a is not TideAbility) a.Unequip();
        }
        phase = Phase.Execute;
        phaseT = 0;
        execStart = Now;
        cur.ExecStart = Now;
        G.Banner("EXECUTE", UiTheme.Accent);
    }

    void EndRound(bool won, string why)
    {
        if (phase == Phase.Summary) return;
        foreach (var a in abilities) if (a.Equipped) a.Unequip();
        G.SetAbilityInHand(false, 0f);
        cur.End = Now;
        cur.Won = won;
        cur.Result = why;
        cur.ExecTime = cur.ExecStart >= 0 ? Now - cur.ExecStart : 0f;
        cur.Coverage = Plan.Coverage(roundSmokes, Targets);
        cur.Spots = Plan.Match(Assigned, roundSmokes);
        cur.Kills = defenders.Count(d => d.Body.Dead);
        foreach (var d in defenders) cur.Defs.Add((d.Spot, d.Target, d.KilledAt >= 0 ? d.KilledAt - cur.Start : -1f));
        int pts = (int)(cur.Coverage * 300f) + (int)(cur.Spots.Sum(s => s.Score) * 60f) + (cur.AllUpAtEntry ? 50 : 0);
        if (won) pts += 200 + (int)Mathf.Max(0f, (25f - cur.ExecTime) * 10f);
        cur.Points = pts;
        Score += pts;
        rounds.Add(cur);
        Event(won ? "round_won" : "round_lost", cur.ExecTime);
        if (won) G.Banner($"SITE TAKEN  {cur.ExecTime:0.0}s", UiTheme.Good);
        else if (!cur.Died) G.Banner(why, UiTheme.Warn);
        phase = Phase.Summary;
        phaseT = 0;
        skip = false;
        if (Main.I.Dev) LogRound(cur);
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Score -= 100;
        cur.Died = true;
        G.Banner("YOU DIED", UiTheme.Accent);
        if (Main.I.Dev)
        {
            var killer = defenders.Where(d => !d.Body.Dead && d.Brain.SeesPlayer).Select(d => d.Spot.Name).FirstOrDefault() ?? "?";
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"[smokeexec] t={Now:0.00} died in {phase} at ({Feet.X:0.0}, {Feet.Y:0.0}, {Feet.Z:0.0}), route {Plan.Project(Feet):0.0}/{Plan.ChokeS:0.0} m, shot by {killer}"));
        }
        EndRound(false, "YOU DIED");
    }

    // =================================================================================================================
    // frame
    // =================================================================================================================

    public override void Update(float dt)
    {
        phaseT += dt;
        for (int i = starBack.Count - 1; i >= 0; i--)
            if (Now >= starBack[i]) { starBack.RemoveAt(i); StarsLeft++; }
        foreach (var a in abilities) a.Update(dt);
        UpdateFuel(dt);
        switch (phase)
        {
            case Phase.Plan:
                UpdateDefenders(dt);
                if (Auto && phaseT > 0.6f) skip = true;
                if ((skip && phaseT > 0.35f) || phaseT >= PlanTime) StartPlace();
                break;
            case Phase.Place:
                if (Auto && !autoDone && phaseT > 0.4f) AutoPlace();
                UpdateDefenders(dt);
                if (phase != Phase.Place) break;
                RecordPath(dt);
                CheckEntry();
                if (defenders.Count > 0 && defenders.All(d => d.Body.Dead)) { StartExecute(); EndRound(true, "SITE TAKEN"); break; }
                if (PlacementComplete || phaseT >= PlaceTime) StartExecute();
                break;
            case Phase.Execute:
                UpdateDefenders(dt);
                if (phase != Phase.Execute) break;
                if (Auto) AutoWalk(dt);
                RecordPath(dt);
                CheckEntry();
                if (defenders.Count > 0 && defenders.All(d => d.Body.Dead)) EndRound(true, "SITE TAKEN");
                else if (Now - execStart > ExecTime) EndRound(false, "TIME");
                break;
            case Phase.Summary:
                if (Auto && phaseT > 1.2f) skip = true;
                if ((skip && phaseT > 0.8f) || phaseT >= SummaryTime)
                {
                    if (rounds.Count >= RoundsTotal) finished = true;
                    else NewRound();
                }
                break;
        }
    }

    void UpdateFuel(float dt)
    {
        if (Kit.Placing != SmokePlacing.Throw) return;
        int on = abilities.OfType<ViperGas>().Count(g => g.On);
        Fuel.Update(dt, on);
        if (Fuel.Fuel <= 0f) foreach (var g in abilities.OfType<ViperGas>()) if (g.On) g.SwitchOff(Now);
    }

    void UpdateDefenders(float dt)
    {
        foreach (var d in defenders)
        {
            d.Brain.Update(G, dt);
            TrackSeen(d.Body);
            Vision(d, dt);
        }
    }

    /// <summary>Smoke bookkeeping per defender: time it would have seen you but a smoke blocked it ("smoke saves"); dev log.</summary>
    void Vision(Defender d, float dt)
    {
        if (d.Body.Dead || G.Player.Dead || !GodotObject.IsInstanceValid(d.Body)) return;
        var eye = d.Body.Head;
        var pe = G.View.Eye;
        bool geo = !Collision.Blocked(eye, pe, G.Solid);
        bool smoked = geo && G.Smokes.Count > 0 && SmokeVolume.Blocks(G.Smokes, eye, pe, Now);
        var to = (pe - eye).Normalized();
        bool inFov = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(d.Brain.HeldDir.Dot(to), -1f, 1f))) <= d.Brain.HalfFovDeg;
        int vis = !geo ? 0 : smoked ? 2 : 1;
        if (vis == 2 && inFov) cur.SmokeSaves += dt;
        if (vis != d.Vis)
        {
            if (Main.I.Dev && vis == 2)
            {
                float inside = G.Smokes.Max(s => s.Depth(eye, Now));
                GD.Print(string.Create(CultureInfo.InvariantCulture,
                    $"[smokeexec] t={Now:0.00} {d.Spot.Name}: clear line to you ({eye.DistanceTo(pe):0.0} m{(inFov ? ", in view" : "")}) but the smoke blocks it{(inside > 0 ? $" (it stands inside a smoke, depth {inside:0.00})" : "")}; sees you: {d.Brain.SeesPlayer}"));
            }
            else if (Main.I.Dev && vis == 1 && d.Vis == 2 && inFov)
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"[smokeexec] t={Now:0.00} {d.Spot.Name}: line to you is clear again ({eye.DistanceTo(pe):0.0} m)"));
            d.Vis = vis;
        }
        // Dev check: the brain must never keep seeing you while every point it checks is behind smoke (one frame of
        // disagreement is normal: the bot turns toward you after its check and its head moves).
        d.ThroughT = Main.I.Dev && vis == 2 && d.Brain.SeesPlayer && !ExposedTo(eye) ? d.ThroughT + dt : 0f;
        if (d.ThroughT > 0.25f && d.ThroughT - dt <= 0.25f)
            GD.PushWarning($"[smokeexec] {d.Spot.Name} keeps seeing the player through a smoke");
    }

    /// <summary>Any of the points the bot brain checks (head, chest, shoulders) visible from <paramref name="eye"/>?</summary>
    bool ExposedTo(Vector3 eye)
    {
        var v = G.View;
        var right = new Vector3(Mathf.Cos(Mathf.DegToRad(v.Yaw)), 0, Mathf.Sin(Mathf.DegToRad(v.Yaw)));
        return G.LineOfSight(eye, v.Eye) || G.LineOfSight(eye, v.Eye + Vector3.Down * 0.45f)
            || G.LineOfSight(eye, v.Eye + Vector3.Down * 0.3f + right * 0.3f) || G.LineOfSight(eye, v.Eye + Vector3.Down * 0.3f - right * 0.3f);
    }

    void RecordPath(float dt)
    {
        pathT -= dt;
        if (pathT > 0) return;
        pathT = 0.1f;
        cur.Path.Add((Now - cur.Start, Feet, G.View.Yaw));
    }

    void CheckEntry()
    {
        if (cur.EntryAt >= 0) return;
        if (Plan.Project(Feet) < Plan.ChokeS + 1f) return;
        cur.EntryAt = Now - cur.Start;
        cur.SmokesAtEntry = roundSmokes.Count;
        cur.SmokesUpAtEntry = roundSmokes.Count(v => v.Up(Now));
        if (cur.SmokesAtEntry > 0 && cur.SmokesUpAtEntry < cur.SmokesAtEntry) G.Banner("ENTERED BEFORE YOUR SMOKES WERE UP", UiTheme.Warn);
        else if (cur.SmokesAtEntry == 0) G.Banner("NO SMOKES OUT", UiTheme.Warn);
    }

    // =================================================================================================================
    // abilities → world
    // =================================================================================================================

    /// <summary>Puts a smoke sphere down: it forms at <paramref name="ground"/> after <paramref name="delay"/> seconds.</summary>
    internal SmokeVolume DeploySphere(Vector3 ground, float delay, float form, float duration, LeadKind lead, Vector3 from, bool scored = true)
    {
        var center = ExecutePlan.CenterFor(ground, Kit.Radius);
        var v = SmokeVolume.Sphere(center, Kit.Radius, Now + delay, duration, form, 0.5f, $"{Kit.Agent} · {Kit.Ability}");
        G.AddSmoke(v);
        var fx = SmokeFx.Sphere(G, v, Kit.Look, Quality);
        if (lead == LeadKind.Drop) fx.WithLead(ground + new Vector3(0, 24f, 0), center, Now, Now + delay, drop: true).WithRing(ground, Kit.Radius);
        else if (lead == LeadKind.Missile) fx.WithLead(from, center, Now, Now + delay, drop: false, size: 0.12f);
        G.World.AddChild(fx);
        roundNodes.Add(fx);
        fog.Tints[v] = Kit.Look.Dark.Lerp(Kit.Look.Light, 0.45f);
        if (scored) { roundSmokes.Add(v); cur.Smokes.Add((v, Kit.Look.Light)); }
        var tree = G.World.GetTree();
        if (tree != null && delay > 0.05f) tree.CreateTimer(delay).Timeout += () => SmokeSfx.PlayAt(G.World, "pop", center, G.View.Eye, 0.8f);
        else SmokeSfx.PlayAt(G.World, "pop", center, G.View.Eye, 0.8f);
        return v;
    }

    /// <summary>Puts a gas / water wall up along <paramref name="pts"/> (column i rises <paramref name="riseStep"/> s after column i-1).</summary>
    internal SmokeVolume DeployWall(IReadOnlyList<Vector3> pts, float height, float delay, float riseStep, float duration, float form, float fade, SmokeLook look, bool scored = true)
    {
        var v = SmokeVolume.Wall(pts, height, Now + delay, duration, riseStep, form, fade, $"{Kit.Agent} · {Kit.WallAbility}");
        G.AddSmoke(v);
        var fx = SmokeFx.Wall(G, v, look);
        G.World.AddChild(fx);
        roundNodes.Add(fx);
        fog.Tints[v] = look.Dark.Lerp(look.Light, 0.45f);
        if (scored) { roundSmokes.Add(v); cur.Smokes.Add((v, look.Light)); }
        if (pts.Count > 0) SmokeSfx.PlayAt(G.World, "pop", pts[0], G.View.Eye, 0.6f, 1.2f);
        return v;
    }

    internal void Whoosh(float vol, float pitch) => SmokeSfx.PlayAt(G.World, "launch", G.View.Eye + G.View.Forward, G.View.Eye, vol, pitch);

    Node3D Glow(Vector3 at, float size, Color c, bool xray = false)
    {
        var mat = new ShaderMaterial { Shader = xray ? SmokeAssets.XrayShader : SmokeAssets.RingShader };
        mat.SetShaderParameter("kind", xray ? 2 : 3);
        mat.SetShaderParameter("color", c);
        mat.SetShaderParameter("energy", xray ? 1.6f : 1.8f);
        var n = new MeshInstance3D
        {
            Mesh = xray ? SmokeAssets.Quad : SmokeAssets.SmallSphere, MaterialOverride = mat, Scale = Vector3.One * size,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        G.World.AddChild(n);
        n.GlobalPosition = at;
        roundNodes.Add(n);
        return n;
    }

    /// <summary>Viper's orb stuck to the floor.</summary>
    internal void EmitterPlaced(Vector3 p) => Glow(p + Vector3.Up * 0.12f, 0.14f, Kit.Look.Lead);

    /// <summary>Viper's wall emitters along the line.</summary>
    internal void WallPlaced(IReadOnlyList<Vector3> pts)
    {
        var c = (Kit.WallLook ?? Kit.Look).Lead;
        foreach (var p in pts) Glow(p + Vector3.Up * 0.08f, 0.07f, c);
    }

    internal void PlaceStar(Vector3 ground)
    {
        if (StarsLeft <= 0) return;
        StarsLeft--;
        var s = new AstraStar { Ground = ground };
        s.Node = Glow(ground + Vector3.Up * 1.0f, 0.75f, Kit.Look.Rim, xray: true);
        Stars.Add(s);
    }

    /// <summary>Takes a star back: instantly in Astral Form, after 25 s when dissipated.</summary>
    internal void RecallStar(AstraStar s, bool instant)
    {
        if (s.Node != null && GodotObject.IsInstanceValid(s.Node)) s.Node.QueueFree();
        s.Node = null;
        Stars.Remove(s);
        if (instant) StarsLeft++;
        else starBack.Add(Now + 25f);
    }

    internal void StarConsumed(AstraStar s)
    {
        if (s.Node is MeshInstance3D m && GodotObject.IsInstanceValid(m) && m.MaterialOverride is ShaderMaterial sm) sm.SetShaderParameter("opacity", 0.25f);
    }

    // =================================================================================================================
    // input (from ControllerInput, before the session sees it)
    // =================================================================================================================

    internal bool HandleInput(InputEvent e)
    {
        if (G is not GameSession { State: GameSession.St.Running }) return false;
        if (phase is Phase.Plan or Phase.Summary)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } ||
                e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Space or Godot.Key.Enter or Godot.Key.E })
            { skip = true; return true; }
            return e is InputEventMouseButton;
        }
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Godot.Key.Key1)
            {
                bool any = false;
                foreach (var a in abilities) if (a.Equipped && a is not TideAbility) { a.Unequip(); any = true; }
                return any;
            }
            if (Kit.Placing == SmokePlacing.Stars && k.Keycode == Godot.Key.F)
                return abilities.OfType<NebulaAbility>().FirstOrDefault()?.Dissipate() ?? false;
            foreach (var a in abilities)
                if (k.Keycode == a.HotKey)
                {
                    foreach (var o in abilities) if (o != a && o.Equipped) o.Unequip();
                    a.OnKey();
                    return true;
                }
        }
        var eq = abilities.FirstOrDefault(a => a.Equipped);
        return eq != null && eq.OnInput(e);
    }

    // =================================================================================================================
    // shooting (same as Site Clear)
    // =================================================================================================================

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (phase is not (Phase.Place or Phase.Execute)) return float.PositiveInfinity;
        var hit = ShootBots(defenders.Select(x => x.Body), o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100;
            var def = defenders.FirstOrDefault(x => x.Body == hit);
            if (def != null) def.KilledAt = Now;
        }
        return dist;
    }

    // =================================================================================================================
    // HUD
    // =================================================================================================================

    public override IEnumerable<string> HudLines()
    {
        yield return $"Round {Math.Min(rounds.Count + (phase == Phase.Summary ? 0 : 1), RoundsTotal)} / {RoundsTotal}";
        if (phase is Phase.Place or Phase.Execute) yield return $"Defenders left: {defenders.Count(d => !d.Body.Dead)}";
    }

    public override void Draw2D(CanvasItem canvas, Vector2 size) => ControllerDraw.Draw(canvas, size, this);

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Agent", $"{Kit.Agent} · {Kit.Ability}{(Kit.HasWall ? " + " + Kit.WallAbility : "")}");
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        int won = rounds.Count(r => r.Won);
        yield return ("Rounds won / lost", $"{won} / {rounds.Count - won}");
        if (rounds.Count > 0)
        {
            yield return ("Sightlines cut", $"{rounds.Average(r => r.Coverage) * 100f:0}%  (standard smokes {StdCoverage * 100f:0}%)");
            var errs = rounds.SelectMany(r => r.Spots).Where(s => !float.IsInfinity(s.Error)).ToList();
            int missed = rounds.Sum(r => r.Spots.Count(s => float.IsInfinity(s.Error)));
            yield return ("Off the standard spots", errs.Count > 0 ? $"{errs.Average(s => s.Error):0.0} m on average{(missed > 0 ? $" · {missed} not placed" : "")}" : "no smokes placed");
            int entries = rounds.Count(r => r.EntryAt >= 0);
            yield return ("Smokes up when you entered", $"{rounds.Count(r => r.AllUpAtEntry)} of {entries} entries");
            var wins = rounds.Where(r => r.Won).ToList();
            if (wins.Count > 0) yield return ("Avg execute time", $"{wins.Average(r => r.ExecTime):0.0} s");
            yield return ("Blinded by your smokes", $"{rounds.Sum(r => r.SmokeSaves):0.0} s of enemy sight lines");
        }
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
    }

    public override IEnumerable<(string Label, Action Act)> ResultButtons()
    {
        if (rounds.Count == 0) yield break;
        if (Main.I.Dev && CmdLine.Has("--smokereplay") && !replayShown) { replayShown = true; Callable.From(OpenReplay).CallDeferred(); } // dev screenshots
        yield return ("Smoke replay", OpenReplay);
    }
    bool replayShown;

    void OpenReplay()
    {
        var r = new ControllerReplay { Mode = this, Name = "SmokeReplay" };
        G.World.AddChild(r);
    }

    public override int Badge()
    {
        if (rounds.Count == 0) return -1;
        float win = rounds.Count(r => r.Won) / (float)rounds.Count;
        float cov = rounds.Average(r => r.Coverage);
        float acc = rounds.Average(r => r.Spots.Count == 0 ? 0f : r.Spots.Average(s => s.Score));
        return Difficulty.BadgeWinRate(Tier, 0.45f * win + 0.4f * cov + 0.15f * acc, 0.45f);
    }

    // =================================================================================================================
    // dev auto-play (--autosmoke)
    // =================================================================================================================

    void AutoPlace()
    {
        autoDone = true;
        var spots = Assigned.Count > 0 ? Assigned.ToList() : Plan.Standard.ToList();
        if (spots.Count == 0) return;
        foreach (var a in abilities)
        {
            switch (a)
            {
                case AstralAbility astral: astral.Auto(spots); break;
                case NebulaAbility neb: neb.Auto(spots); break;
                case TideAbility or ScreenAbility: a.Auto(spots); break;
                default: a.Auto(spots); break;
            }
        }
        GD.Print($"[smokeexec] auto-placed {roundSmokes.Count} smoke(s) for {Kit.Agent} at {string.Join(", ", spots.Select(s => s.Name))}");
    }

    void AutoWalk(float dt)
    {
        var eye = G.View.Eye;
        if (CmdLine.Has("--smokeinside"))
        {
            // dev screenshots: stand inside the first smoke once it is up, looking toward the site
            int which = int.TryParse(CmdLine.After("--smokeinside"), out var wi) ? wi : 0;
            var first = roundSmokes.Where(v => v.Shape == SmokeShape.Sphere).Skip(which).FirstOrDefault();
            if (first != null && first.Up(Now) && Feet.DistanceTo(first.Center) > first.Radius * 0.8f)
            {
                G.TeleportPlayer(first.Center - new Vector3(0, ExecutePlan.Lift * first.Radius, 0));
                Face(Plan.Anchor + Vector3.Up * 1.5f);
            }
            return;
        }
        if (CmdLine.Has("--smokecam"))
        {
            // dev screenshots: walk to just past the choke, then look at the smokes (no --simaim with this)
            if (autoPath == null) { autoPath = Nav.Path(Feet, Plan.RouteAt(Plan.ChokeS + 2f)); autoIdx = 1; }
            if (autoIdx >= autoPath.Count)
            {
                var c = roundSmokes.Count > 0 ? roundSmokes.Aggregate(Vector3.Zero, (a, v) => a + v.Center) / roundSmokes.Count : Plan.Anchor;
                Face(c);
                return;
            }
            var f = Feet;
            var to = new Vector3(autoPath[autoIdx].X - f.X, 0, autoPath[autoIdx].Z - f.Z);
            if (to.Length() <= 6f * dt) autoIdx++;
            else { Face(f + to + Vector3.Up * 1.6f); G.TeleportPlayer(f + to.Normalized() * 6f * dt); }
            return;
        }
        if (defenders.Any(d => !d.Body.Dead && G.LineOfSight(eye, d.Body.Head))) return; // stand still while the sim shoots
        if (roundSmokes.Any(v => !v.Up(Now) && v.StartTime > Now - 0.1f) && Now - execStart < 3f) return; // let the smokes form
        if (autoPath == null || autoIdx >= autoPath.Count)
        {
            autoWait -= dt;
            if (autoWait > 0) return;
            autoWait = 0.75f;
            // Next goal: the site, then each defender still alive (walks into smokes to find blinded ones).
            var alive = defenders.Where(d => !d.Body.Dead).OrderBy(d => d.Spot.Feet.DistanceTo(Feet)).FirstOrDefault();
            var goal = autoPath == null ? Plan.Anchor : alive?.Spot.Feet ?? Plan.Anchor;
            autoPath = Nav.Path(Feet, goal);
            autoIdx = 1;
        }
        if (autoIdx >= autoPath.Count) return;
        var feet = Feet;
        var target = autoPath[autoIdx];
        var d2 = new Vector3(target.X - feet.X, 0, target.Z - feet.Z);
        float step = 4.2f * dt;
        if (d2.Length() <= step) { feet = new Vector3(target.X, feet.Y, target.Z); autoIdx++; }
        else feet += d2.Normalized() * step;
        G.TeleportPlayer(feet);
    }

    void Face(Vector3 p)
    {
        var d = p - G.View.Eye;
        G.View.Yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
        G.View.Pitch = Mathf.Clamp(Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())), -30f, 30f);
    }

    void LogRound(RoundLog r)
    {
        var inv = CultureInfo.InvariantCulture;
        GD.Print(string.Create(inv,
            $"[smokeexec] round {r.Index} {Kit.Agent} on {MapSpot.Key}: {r.Result}{(r.Won ? $" in {r.ExecTime:0.0}s" : "")} | sightlines cut {r.Coverage * 100:0}% (standard {StdCoverage * 100:0}%) | " +
            $"spots {string.Join(", ", r.Spots.Select(s => $"{s.Name} {(float.IsInfinity(s.Error) ? "missing" : s.Error.ToString("0.0", inv) + " m")}{(s.ByWall ? " (wall)" : "")}"))} | " +
            $"{(r.EntryAt >= 0 ? $"smokes up at entry {r.SmokesUpAtEntry}/{r.SmokesAtEntry}" : "never entered")} | kills {r.Kills}/{r.Defenders} | smoke-blocked sight {r.SmokeSaves:0.0}s | +{r.Points}"));
    }
}
