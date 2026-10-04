using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Shared machinery of the spike drills (<see cref="PostPlantMode"/>, <see cref="RetakeMode"/>): VALORANT's spike rules,
/// rounds, bots that walk the map's nav grid, the spike HUD and the aim-coach hooks.
/// <para>Spike rules (VALORANT wiki / Riot): planting takes 4 s, the spike detonates 45 s after the plant, defusing takes
/// 7 s and has a checkpoint at half (3.5 s): a defuse that is stopped after the checkpoint resumes from it, an earlier one
/// starts over. The defuser can't move or shoot (the gun is put away), and the spike beeps faster as it runs down. Both
/// drills plant the spike when the round starts.</para>
/// <para>Defuse key: VALORANT's "Use Spike" bind (plant / defuse, default 4), imported with the other keybinds; both bind
/// slots work.</para>
/// </summary>
public abstract partial class TacticalMode : MapMode
{
    public const float PlantTime = 4f, DefuseTime = 7f, HalfDefuse = 3.5f, SpikeTimer = 45f, DefuseRange = 1.5f;
    /// <summary>The plant / defuse bind for prompts: "4" unless the player rebound Use Spike.</summary>
    public static string DefuseKeyText => Main.I?.Valorant.Binds.Short(GameAction.UseSpike) ?? "4";
    public override float Duration => 240f;
    public override string SimKind => "bot";
    public override bool InfiniteReserve => true;

    protected static readonly bool DevLog = CmdLine.Dev;

    protected BotNav Nav = null!;
    protected TacticalSite Site = null!;
    protected Vector3[] Plants = Array.Empty<Vector3>();
    protected readonly List<TacBot> Bots = new();
    protected SpikeFx? SpikeNode;
    protected Vector3 Spike;

    protected enum Phase { Live, Over }
    protected Phase RoundPhase = Phase.Over;
    protected float RoundStart, SpikeEnd, OverUntil;
    protected int Round, Won, Lost;
    protected float NextBeepAt;
    protected int PathBudget;

    // defuse state (one defuse at a time)
    protected float DefuseProgress;         // seconds of the 7
    protected bool DefuseCheckpoint;        // the half has been reached (a stop resumes from 3.5)
    protected object? Defuser;              // a TacBot or the player (this)
    protected float DefuseStartedAt, NextTickAt;

    protected float SpikeLeft => Mathf.Max(0f, SpikeEnd - Now);
    protected float RoundT => Now - RoundStart;
    protected bool Live => RoundPhase == Phase.Live;
    protected Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);
    protected float PlayerSeenAt = -99f;     // last time any bot had line of sight to you
    protected Vector3? PlayerKnownAt;         // where the bots last saw / heard you
    protected float PlayerKnownT = -99f;
    int lastShots;
    bool started;

    protected override void Setup()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // The walkable grid seeded on the SITE (not the attackers' start): some maps only drop onto the site (Bind's Hookah
        // window), and drops aren't links, so the defenders' side is its own component.
        Nav = BotNav.For($"{MapSpot.Key}:{G.Solid.Count}:site", G.Solid, TacticalSite.SiteSeed(MapSpot));
        Plants = TacticalSite.PlantSpotsFor(MapSpot, Nav, G.Solid);
        // Precompute every plant's geometry in parallel (cached for the process).
        var sites = new TacticalSite[Plants.Length];
        Parallel.For(0, Plants.Length, i => sites[i] = TacticalSite.For(MapSpot, Nav, G.Solid, i));
        if (DevLog)
        {
            GD.Print($"[tac] {Key} map {MapSpot.Key} tier {Tier}: nav {Nav.Count} nodes, {Plants.Length} plant spot(s){(sites[0].PlantDerived ? " (derived)" : "")}, setup {sw.Elapsed.TotalMilliseconds:0} ms");
            for (int i = 0; i < sites.Length; i++)
            {
                var s = sites[i];
                GD.Print($"[tac]  plant {i} {Fmt(s.Spike)} built {s.BuildMs:0} ms | entries{(s.EntriesDerived ? " (derived)" : "")}: {string.Join(" ", s.Entries.Select(Fmt))} " +
                         $"| routes {string.Join("/", s.Routes.Select(r => $"{TacticalSite.Length(r[0], r):0}m"))} | stages {string.Join(" ", s.Stages.Select(Fmt))}");
                GD.Print($"[tac]  spots: {string.Join(" | ", s.Spots.SelectMany(k => k).Select(p => $"{p.Name}{(p.Crouch ? " (crouch)" : "")} {Fmt(p.Feet)} {H(p.Feet, s.Spike):0}m"))}; lurks {s.Lurks.Length}; clear {s.ClearPoints.Length}");
                foreach (var w in s.Warnings) GD.Print($"[tac]  WARNING {w}");
            }
        }
        NewRound();
    }

    protected static string Fmt(Vector3 v) => $"({v.X:0.0},{v.Y:0.0},{v.Z:0.0})";
    protected static float H(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ---------------------------------------------------------------- rounds

    void NewRound()
    {
        foreach (var b in Bots) b.Despawn();
        Bots.Clear();
        if (SpikeNode != null && GodotObject.IsInstanceValid(SpikeNode)) SpikeNode.QueueFree();
        G.Blind.Reset();
        G.MoveSpeedScale = 1f;
        G.SetAbilityInHand(false, 0f);
        int plant = Rng.Next(Plants.Length);
        Site = TacticalSite.For(MapSpot, Nav, G.Solid, plant);
        Spike = Site.Spike;
        DefuseProgress = 0; DefuseCheckpoint = false; Defuser = null;
        PlayerSeenAt = -99f; PlayerKnownAt = null; PlayerKnownT = -99f;
        Round++;
        RoundStart = Now;
        SpikeEnd = Now + SpikeTimerAtStart;
        NextBeepAt = Now + 0.5f;
        BeginRound();          // places the player and the bots
        SpikeNode = SpikeFx.Create();
        G.World.AddChild(SpikeNode);
        SpikeNode.GlobalPosition = Spike;
        SpikeSfx.PlayAt(G.World, "plant", Spike, G.View.Eye, 0.9f);
        Event("spike_plant", SpikeTimerAtStart, Round);
        RoundPhase = Phase.Live;
        G.Mover.ResetStops();
        DevNewRound();
    }

    /// <summary>Seconds on the spike when the round starts (post-plant: a fresh plant; retake: you rotated in).</summary>
    protected virtual float SpikeTimerAtStart => SpikeTimer;

    /// <summary>Place the player and spawn the bots for a new round.</summary>
    protected abstract void BeginRound();
    /// <summary>Per-frame squad logic (after the bots' own movement / duels).</summary>
    protected abstract void UpdateSquad(float dt);
    /// <summary>The spike detonated.</summary>
    protected abstract void OnDetonated();
    /// <summary>A defuse finished.</summary>
    protected abstract void OnDefused(bool byPlayer);

    protected void EndRound(bool won, string text, Color color, string how)
    {
        if (!Live) return;
        RoundPhase = Phase.Over;
        OverUntil = Now + 2.2f;
        if (Defuser != null) StopDefuse(false);
        if (won) { Won++; Event("round_won", RoundT, SpikeLeft); }
        else { Lost++; Event("round_lost", RoundT, SpikeLeft); }
        G.Banner(text, color);
        if (DevLog) GD.Print($"[tac] round {Round} {(won ? "WON" : "LOST")} ({how}) at {RoundT:0.0}s, spike {SpikeLeft:0.0}s left, alive {Bots.Count(b => b.Alive)}/{Bots.Count}, you {(G.Player.Dead ? "dead" : $"{G.Player.Hp:0}hp")}");
        foreach (var b in Bots) b.Freeze();
        G.MoveSpeedScale = 1f;
        OnRoundEnd(won);
    }

    protected virtual void OnRoundEnd(bool won) { }

    public override void Update(float dt)
    {
        PathBudget = 3;
        if (!started) { started = true; }
        if (RoundPhase == Phase.Over)
        {
            foreach (var b in Bots) b.UpdateDead();
            if (Now >= OverUntil) NewRound();
            return;
        }

        // Your gunfire is heard (≈ 35 m): the bots learn roughly where you are.
        if (Shots > lastShots && !G.Player.Dead) { Heard(PlayerFeet, 35f); }
        lastShots = Shots;

        foreach (var b in Bots) b.Update(dt);
        foreach (var b in Bots)
            if (b.Alive && b.Brain.SeesPlayer) { PlayerSeenAt = Now; Know(PlayerFeet); }
        UpdateSight();
        UpdateDefuse(dt);
        if (!Live) return;
        UpdateSquad(dt);
        if (!Live) return;
        UpdateSpike();
        DevUpdate(dt);
    }

    Vector3 calloutNoise;

    /// <summary>The team learns where you are (a sighting, your gunfire, a hit). Teammates get it as a callout: late and rough.</summary>
    protected void Know(Vector3 at)
    {
        if (PlayerKnownAt is { } old && Now - PlayerKnownT < 0.5f && old.DistanceTo(at) < 1f) { PlayerKnownT = Now; return; }
        PlayerKnownAt = at; PlayerKnownT = Now;
        float r = Difficulty.T(Tier, 3f, 2.5f, 2f, 1.5f, 1f) * Mathf.Sqrt((float)Rng.NextDouble()), a = (float)Rng.NextDouble() * Mathf.Tau;
        calloutNoise = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
    }

    /// <summary>Where this bot thinks you are: its own sighting / the shots it took or heard (exact-ish), else the team's
    /// callout once it has had time to arrive (late, rough); null when nothing is fresher than <paramref name="maxAge"/>.</summary>
    protected Vector3? Belief(TacBot b, float maxAge)
    {
        if (b.OwnSeenAt > 0 && Now - b.OwnSeenAt < maxAge) return b.OwnSeen;
        float delay = Difficulty.T(Tier, 1.5f, 1.2f, 1.0f, 0.8f, 0.6f);
        if (PlayerKnownAt is { } k && Now - PlayerKnownT > delay && Now - PlayerKnownT < maxAge) return k + calloutNoise;
        return null;
    }

    protected static Vector3 EyeAt(Vector3 feet) => feet + new Vector3(0, PlayerView.EyeHeight, 0);

    protected virtual void Heard(Vector3 at, float range)
    {
        foreach (var b in Bots)
            if (b.Alive && b.Body.Feet.DistanceTo(at) < range) b.HeardShot(at);
        Know(at);
    }

    void UpdateSpike()
    {
        if (SpikeNode != null && GodotObject.IsInstanceValid(SpikeNode))
            SpikeNode.SetDefuse(Defuser != null || DefuseProgress > 0 ? DefuseProgress / DefuseTime : -1f);
        float left = SpikeLeft;
        if (left <= 0f)
        {
            SpikeFx.Detonate(G.World, Spike);
            SpikeSfx.PlayAt(G.World, "detonate", Spike, G.View.Eye, 1.1f, 120f, 0.7f);
            if (SpikeNode != null && GodotObject.IsInstanceValid(SpikeNode)) SpikeNode.QueueFree();
            SpikeNode = null;
            OnDetonated();
            return;
        }
        if (Now >= NextBeepAt)
        {
            // VALORANT-like cadence: slow at first, quicker under 20 s, fast under 10 s, frantic in the last 5 s.
            float interval = left > 20f ? 1.0f : left > 10f ? 0.5f : left > 5f ? 0.25f : 0.125f;
            NextBeepAt = Now + interval;
            SpikeSfx.PlayAt(G.World, "beep", Spike, G.View.Eye, left > 10f ? 0.55f : 0.75f, 70f, 0.4f);
            SpikeNode?.Beep();
        }
    }

    // ---------------------------------------------------------------- defuse

    protected bool Defusing => Defuser != null;
    protected bool PlayerDefusing => Defuser == this;

    protected void StartDefuse(object who)
    {
        if (Defuser != null || !Live) return;
        Defuser = who;
        DefuseStartedAt = Now;
        DefuseProgress = DefuseCheckpoint ? HalfDefuse : 0f;
        NextTickAt = Now + 0.25f;
        bool player = who == this;
        Event("defuse_start", DefuseProgress, player ? 1 : 0);
        if (DevLog) GD.Print($"[tac] defuse start by {(player ? "you" : ((TacBot)who).Name)} at {RoundT:0.0}s from {DefuseProgress:0.0}s, spike {SpikeLeft:0.0}s left");
        if (player) SpikeSfx.Play(G.World, "defuse_start", 0.6f);
        else SpikeSfx.PlayAt(G.World, "defuse_start", Spike, G.View.Eye, 1f, 70f, 0.45f);
        OnDefuseStarted(player);
    }

    /// <summary>Stops the current defuse; it keeps the half checkpoint if it was reached.</summary>
    protected void StopDefuse(bool interrupted = true)
    {
        if (Defuser == null) return;
        bool player = Defuser == this;
        Event("defuse_stop", DefuseProgress, player ? 1 : 0);
        if (DevLog) GD.Print($"[tac] defuse stopped at {DefuseProgress:0.0}s ({(player ? "you" : "bot")}, {RoundT:0.0}s into the round)");
        float was = DefuseProgress;
        Defuser = null;
        DefuseProgress = DefuseCheckpoint ? HalfDefuse : 0f;
        OnDefuseStopped(player, was);
    }

    protected virtual void OnDefuseStarted(bool player) { }
    protected virtual void OnDefuseStopped(bool player, float progress) { }

    void UpdateDefuse(float dt)
    {
        if (Defuser == null) return;
        DefuseProgress += dt;
        if (!DefuseCheckpoint && DefuseProgress >= HalfDefuse)
        {
            DefuseCheckpoint = true;
            SpikeSfx.PlayAt(G.World, "half", Spike, G.View.Eye, 0.9f, 70f, 0.45f);
        }
        if (Now >= NextTickAt)
        {
            NextTickAt = Now + 0.5f;
            SpikeSfx.PlayAt(G.World, "defuse_tick", Spike, G.View.Eye, 0.7f, 50f, 0.35f);
        }
        if (DefuseProgress >= DefuseTime)
        {
            bool player = Defuser == this;
            Event("defuse_stop", DefuseTime, player ? 1 : 0);
            Defuser = null;
            DefuseProgress = DefuseTime;
            SpikeNode?.Disarm();
            SpikeSfx.PlayAt(G.World, "defused", Spike, G.View.Eye, 1f, 90f, 0.6f);
            OnDefused(player);
        }
    }

    // ---------------------------------------------------------------- what you see (aim coach)

    void UpdateSight()
    {
        if (G.Player.Dead) return;
        var eye = G.View.Eye;
        foreach (var b in Bots)
        {
            if (!b.Alive) continue;
            var head = b.Body.Head;
            if (!G.LineOfSight(eye, head)) continue;
            // A new engagement: first sight after ≥2.5 s unseen. Placement is graded only for enemies in front.
            if (Now - b.SeenByPlayerAt > 2.5f)
            {
                b.Body.SpawnTime = Now; // time-to-kill counts from first sight
                if (AngleFromCrosshair(head) <= 60f) SeenEvent(head);
            }
            b.SeenByPlayerAt = Now;
        }
    }

    public override AimFocus? Focus => Live && !G.Player.Dead ? NearestHead(Bots.Where(b => b.Alive).Select(b => b.Body)) : null;

    protected int AliveBots => Bots.Count(b => b.Alive);

    /// <summary>The first point of a retake route (walking from its entry) that <paramref name="eye"/> can see, at eye height:
    /// where someone taking that route shows up.</summary>
    protected Vector3? FirstVisibleOnRoute(Vector3 eye, int route, float fromDist = 0f)
    {
        if (route < 0 || route >= Site.Routes.Length) return null;
        var pts = TacticalSite.Sample(Site.Routes[route], 1f);
        float walked = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) walked += pts[i].DistanceTo(pts[i - 1]);
            if (walked < fromDist) continue;
            var pe = pts[i] + new Vector3(0, PlayerView.EyeHeight, 0);
            if (G.LineOfSight(eye, pe)) return pe;
        }
        return null;
    }

    // ---------------------------------------------------------------- bullets

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (!Live || G.Player.Dead) return float.PositiveInfinity;
        var hit = ShootBots(Bots.Where(b => b.Alive).Select(b => b.Body).ToList(), o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        var bot = Bots.First(b => b.Body == hit);
        Score += zone == HitZone.Head ? 30 : 10;
        bot.HitByPlayerAt(PlayerFeet);
        Know(PlayerFeet);
        if (killed)
        {
            Score += 100 + (zone == HitZone.Head ? 50 : 0);
            if (Defuser == bot) StopDefuse();
            OnBotKilled(bot, zone == HitZone.Head);
        }
        return dist;
    }

    protected virtual void OnBotKilled(TacBot bot, bool headshot) { }

    /// <summary>Dev log: how you died (who, from where, what the bots were doing).</summary>
    protected void DevDeathLog(string spot)
    {
        if (!DevLog) return;
        TacBot? killer = null; float bd = float.MaxValue;
        foreach (var b in Bots)
        {
            if (!b.Alive) continue;
            float d = b.Body.Head.DistanceSquaredTo(G.Player.LastHitFrom);
            if (d < bd) { bd = d; killer = b; }
        }
        GD.Print($"[tac] you died at {RoundT:0.0}s ({spot}, feet {Fmt(PlayerFeet)}): killer {killer?.Name} plan {killer?.Plan} at {(killer != null ? Fmt(killer.Feet) : "-")} " +
                 $"{(killer != null ? G.View.Eye.DistanceTo(killer.Body.Head) : 0):0}m, its first sight {(killer?.SpottedAt ?? -1) - RoundStart:0.0}s, you saw it last {(killer?.SeenByPlayerAt ?? -99) - RoundStart:0.0}s, " +
                 $"head LOS {(killer != null && G.LineOfSight(G.View.Eye, killer.Body.Head))} angle {(killer != null ? AngleFromCrosshair(killer.Body.Head) : -1):0}° crouched {killer?.Body.Crouched} | shots {Shots} hits {Hits} kills {Kills} | " +
                 string.Join(" ", Bots.Select(b => $"{b.Name}:{(b.Alive ? b.Plan.ToString() : "dead")}{(b.Fighting ? "F" : "")}")));
    }

    // ---------------------------------------------------------------- HUD

    protected abstract string Side { get; }     // "ATK" or "DEF" (your side)
    protected abstract string OtherSide { get; }

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (G == null || Site == null || size.Y < 100) return;
        float k = UiTheme.S(size);
        float cx = size.X / 2, y = 112 * k;

        // ---- spike timer: icon + draining bar + seconds, alive counts either side ----
        float bw = 300 * k, bh = 34 * k;
        var r = new Rect2(cx - bw / 2, y, bw, bh);
        c.DrawRect(r, UiTheme.Plate);
        float left = Live ? SpikeLeft : 0f;
        bool hurry = left < 10f && Live;
        float frac = Mathf.Clamp(left / SpikeTimer, 0, 1);
        var barCol = hurry ? UiTheme.Accent : new Color(1f, 0.42f, 0.38f);
        c.DrawRect(new Rect2(r.Position.X + 40 * k, r.End.Y - 7 * k, (bw - 50 * k) * frac, 4 * k), barCol);
        DrawSpikeIcon(c, new Vector2(r.Position.X + 20 * k, r.Position.Y + bh / 2), 11 * k, Live && Now - (NextBeepAt - 0.1f) < 0 ? barCol : barCol, Live ? 1f : 0.4f);
        int fs = UiTheme.Fs(20, k);
        string label = !Live ? "ROUND OVER" : DefuseProgress >= DefuseTime ? "DEFUSED" : $"SPIKE  {left:0.0}s";
        Gfx.TextC(c, UiTheme.HudWide, label, cx + 16 * k, Gfx.Mid(r.Position.Y + bh / 2 - 3 * k, fs), fs, hurry ? UiTheme.Accent : UiTheme.Text);
        int ns = UiTheme.Fs(22, k), ls = UiTheme.Fs(11, k);
        int you = G.Player.Dead ? 0 : 1, them = AliveBots;
        var lr = new Rect2(r.Position.X - 76 * k, y, 70 * k, bh);
        var rr = new Rect2(r.End.X + 6 * k, y, 70 * k, bh);
        c.DrawRect(lr, new Color(UiTheme.Teal, 0.75f));
        c.DrawRect(rr, new Color(UiTheme.Accent, 0.75f));
        Gfx.TextC(c, UiTheme.Display, you.ToString(), lr.GetCenter().X - 12 * k, Gfx.Mid(lr.GetCenter().Y, ns), ns, UiTheme.Text);
        Gfx.TextC(c, UiTheme.HudWide, Side, lr.GetCenter().X + 14 * k, Gfx.Mid(lr.GetCenter().Y, ls), ls, UiTheme.Text);
        Gfx.TextC(c, UiTheme.Display, them.ToString(), rr.GetCenter().X + 12 * k, Gfx.Mid(rr.GetCenter().Y, ns), ns, UiTheme.Text);
        Gfx.TextC(c, UiTheme.HudWide, OtherSide, rr.GetCenter().X - 14 * k, Gfx.Mid(rr.GetCenter().Y, ls), ls, UiTheme.Text);

        // ---- defuse progress (you hear it too) ----
        if (Live && (Defuser != null))
        {
            float dw = 340 * k, dh = 16 * k, dy = size.Y * 0.63f;
            var dr = new Rect2(cx - dw / 2, dy, dw, dh);
            c.DrawRect(dr, new Color(0.03f, 0.05f, 0.08f, 0.75f));
            float p = Mathf.Clamp(DefuseProgress / DefuseTime, 0, 1);
            var col = PlayerDefusing ? UiTheme.Teal : UiTheme.Accent;
            c.DrawRect(new Rect2(dr.Position, new Vector2(dw * p, dh)), col);
            c.DrawRect(new Rect2(dr.Position.X + dw / 2 - 1.5f * k, dr.Position.Y - 4 * k, 3 * k, dh + 8 * k), new Color(UiTheme.Text, 0.9f));
            int ds = UiTheme.Fs(16, k);
            string who = PlayerDefusing ? $"DEFUSING  {DefuseProgress:0.0} / 7.0s" : $"ENEMY DEFUSING  {DefuseProgress:0.0} / 7.0s";
            Gfx.TextC(c, UiTheme.HudWide, who, cx, dr.Position.Y - 12 * k, ds, col);
        }

        // ---- world markers: the spike (and whatever the mode adds) ----
        var cam = ((Node)G).GetViewport()?.GetCamera3D();
        if (cam != null && Live)
        {
            DrawMarker(c, cam, Spike + new Vector3(0, 0.9f, 0), k, "SPIKE", new Color(1f, 0.42f, 0.38f));
            DrawMarkers(c, cam, k);
        }
    }

    protected virtual void DrawMarkers(CanvasItem c, Camera3D cam, float k) { }

    protected void DrawMarker(CanvasItem c, Camera3D cam, Vector3 world, float k, string text, Color col)
    {
        if (cam.IsPositionBehind(world)) return;
        float dist = G.View.Eye.DistanceTo(world);
        if (dist < 1f) return;
        var p = cam.UnprojectPosition(world);
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || Mathf.Abs(p.X) > 20000f || Mathf.Abs(p.Y) > 20000f) return;
        DrawSpikeIcon(c, p, 7 * k, col, 0.85f);
        int fs = UiTheme.Fs(12, k);
        Gfx.TextC(c, UiTheme.HudWide, $"{text} {dist:0}m", p.X, p.Y - 12 * k, fs, new Color(col, 0.9f));
    }

    protected static void DrawSpikeIcon(CanvasItem c, Vector2 at, float s, Color col, float a)
    {
        if (s < 0.5f) return;
        Span<Vector2> d = stackalloc Vector2[4];
        d[0] = at + new Vector2(0, -s); d[1] = at + new Vector2(s * 0.7f, 0); d[2] = at + new Vector2(0, s); d[3] = at + new Vector2(-s * 0.7f, 0);
        c.FillPoly(d, new Color(col, a));
    }

    protected static float Pct(int a, int b) => b == 0 ? 0 : 100f * a / b;

    public override int Badge() => Won + Lost == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)Won / (Won + Lost), BadgeThreshold);
    protected virtual float BadgeThreshold => 0.45f;
}
