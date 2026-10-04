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
/// Mobility Entry ("mobility:&lt;agent&gt;"): attack a real site with a duelist's movement ability — Jett's Tailwind / Updraft /
/// Drift, Neon's High Gear sprint and slide, Raze's Blast Pack boost, Reyna's Dismiss / Devour after a kill, Yoru's
/// Gatecrash teleport (and fake), Waylay's Lightspeed dashes and Refract escape, Iso's Double Tap shield. Site Clear's
/// rounds: you start behind the choke, 2–4 tier-scaled defenders hold real angles and duel you. Higher tiers also react to
/// loud ability cues (a satchel blast, a teleport arrival) by turning toward them. Hits on a target moving faster than the
/// knife run speed (a dash, a launch) miss more often (tracking is harder). Scored on round wins, the first duel of each
/// round, time to get into the site, time from the ability to the next kill, deaths and per-agent ability efficiency.
/// Ability numbers (official wiki, game-file / patch values to 13.0x) are next to each kit in DuelistKits.cs.
/// <para>Hidden variant "mobility:jett-op": the same drill with the Operator (dash, scope, shoot) — not on the Agents
/// screen, reachable with --mode.</para>
/// </summary>
public sealed partial class MobilityEntryMode : MapMode
{
    /// <summary>Arguments this drill supports (lower-case agent keys); the Agents screen lists one entry per item.</summary>
    public static readonly string[] Agents = { "jett", "neon", "raze", "reyna", "yoru", "waylay", "iso" };
    public readonly string Agent;

    public MobilityEntryMode(string agent = "jett")
    {
        var a = string.IsNullOrWhiteSpace(agent) ? "jett" : agent.Trim().ToLowerInvariant();
        Agent = Agents.Contains(a) || a == "jett-op" ? a : "jett";
    }

    public override string Key => "mobility:" + Agent;
    public override string Name => "Mobility Entry";
    public override string Description => "Use your movement ability to enter the site and take the first duel.";
    public override string Category => "Agents";
    public override string SimKind => "bot";
    public override float Duration => 120f;
    public override WeaponKind Weapon => Agent == "jett-op" ? WeaponKind.Operator : WeaponKind.Vandal;
    public override string? Subtitle => $"{AgentTitle} · {AbilityTitle}  —  {MapSpot.Map} {MapSpot.Name}";
    public override AimFocus? Focus => between > 0 || Kit == null ? null
        : NearestHead(defenders.Where(d => d.Alive).Select(d => d.Body)) ?? Kit.ExtraFocus;

    string AgentTitle => Agent switch
    {
        "neon" => "Neon", "raze" => "Raze", "reyna" => "Reyna", "yoru" => "Yoru", "waylay" => "Waylay", "iso" => "Iso",
        "jett-op" => "Jett (Operator)", _ => "Jett",
    };

    string AbilityTitle => Agent switch
    {
        "neon" => "High Gear", "raze" => "Blast Pack", "reyna" => "Dismiss / Devour", "yoru" => "Gatecrash",
        "waylay" => "Lightspeed / Refract", "iso" => "Double Tap", _ => "Tailwind / Updraft",
    };

    internal DuelistKit Kit = null!;
    DuelistDev? dev;

    sealed class Defender
    {
        public BotCharacter Body = null!;
        public BotBrain Brain = null!;
        public Vector3 Hold;
        public bool Seen, Spotted;
        public float TurnAt = -1f, BackAt = -1f;
        public Vector3 TurnDir;
        public bool Alive => GodotObject.IsInstanceValid(Body) && !Body.Dead;
    }

    readonly List<Defender> defenders = new();
    float roundStart, between;
    int won, lost, rounds;
    readonly List<float> clearTimes = new();

    // first duel / entry / ability stats
    int firstDuel; // 0 = undecided, 1 = won, -1 = lost
    int firstWon, firstLost, firstWithAbility, entered, abilityKills, dodged, alertsHeard, seenBeforeAbility;
    bool roundEntered, abilityBeforeDuel;
    readonly List<float> entryTimes = new(), abilityToKillMs = new();

    float RoundTime => Difficulty.SiteRoundTime(Tier) + 8f;
    float RoundT => Now - roundStart;
    internal float RoundTimeNow => RoundT;
    internal int RoundIndex => rounds;
    internal MapSpot Spot => MapSpot;
    internal int DrillTier => Tier;

    // ------------------------------------------------------------------ setup / rounds

    protected override void Setup()
    {
        Kit = Agent switch
        {
            "neon" => new NeonKit(G, this),
            "raze" => new RazeKit(G, this),
            "reyna" => new ReynaKit(G, this),
            "yoru" => new YoruKit(G, this),
            "waylay" => new WaylayKit(G, this),
            "iso" => new IsoKit(G, this),
            _ => new JettKit(G, this),
        };
        DuelistSfx.Prewarm();
        G.Player.Absorb = (dmg, from) => Kit.Absorb(dmg, from) || TrackingMiss();
        if (DuelistDev.Requested) dev = new DuelistDev(G, this);
        NewRound();
    }

    void NewRound()
    {
        foreach (var d in defenders) if (GodotObject.IsInstanceValid(d.Body)) G.Despawn(d.Body);
        defenders.Clear();
        int n = Tier switch { 0 => 2, 1 => Rng.NextDouble() < 0.3 ? 3 : 2, 2 => 3, 3 => 3, _ => Rng.NextDouble() < 0.5 ? 4 : 3 };
        // Nobody may see ANY part of you at spawn (the bot brain checks head, chest and shoulders).
        var startEye = MapSpot.StartFeet + new Vector3(0, PlayerView.EyeHeight, 0);
        var exposed = new[]
        {
            startEye, startEye + Vector3.Down * 0.45f,
            startEye + new Vector3(0.35f, -0.3f, 0), startEye + new Vector3(-0.35f, -0.3f, 0),
            startEye + new Vector3(0, -0.3f, 0.35f), startEye + new Vector3(0, -0.3f, -0.35f),
        };
        var pool = MapSpot.Enemies.Where(e => exposed.All(p => !MapSpot.LineOfSight(EyeOf(e), p))).OrderBy(_ => Rng.Next()).Take(n).ToList();
        foreach (var spot in pool)
        {
            var look = MapSpot.Choke + new Vector3(0, 1.5f, 0);
            if (Tier >= 3 && Rng.NextDouble() < 0.3) look += new Vector3(R(-4, 4), 0, R(-2, 2)); // off-angles
            var (body, brain) = SpawnDefender(spot, look);
            defenders.Add(new Defender { Body = body, Brain = brain, Hold = brain.HeldDir });
        }
        rounds++;
        roundStart = Now;
        firstDuel = 0;
        roundEntered = abilityBeforeDuel = false;
        G.Respawn(MapSpot.StartFeet, MapSpot.StartYaw);
        G.Mover.ResetStops();
        G.Blind.Reset();
        Kit.ResetRound();
        dev?.NewRound();
    }

    void EndRound(bool win, string text, Color color)
    {
        if (win) { won++; float t = RoundT; clearTimes.Add(t); Event("round_won", t); Score += 200 + (int)Mathf.Max(0, (15 - t) * 20); }
        else { lost++; Event("round_lost"); }
        G.Banner(text, color);
        between = 1.6f;
        if (dev != null) GD.Print($"[duelist] round {rounds}: {text} (kills {Kills}, deaths {Deaths}, ability used: {Kit.UsedThisRound})");
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Score -= 100;
        DecideFirstDuel(false);
        EndRound(false, "YOU DIED", UiTheme.Accent);
    }

    void DecideFirstDuel(bool win)
    {
        if (firstDuel != 0) return;
        firstDuel = win ? 1 : -1;
        if (win) { firstWon++; Score += 100; } else firstLost++;
        if (abilityBeforeDuel) firstWithAbility++;
        Event("first_duel", win ? 1 : 0, abilityBeforeDuel ? 1 : 0);
    }

    // ------------------------------------------------------------------ frame

    public override void Update(float dt)
    {
        if (between > 0)
        {
            between -= dt;
            if (between <= 0) NewRound();
            return;
        }
        Kit.Tick(dt);
        dev?.Update(dt);
        UpdateDefenders(dt);
        if (between > 0) return; // died this frame
        if (!roundEntered)
        {
            var axis = SiteAxis;
            if ((PlayerFeet - MapSpot.Choke).Dot(axis) > 1.5f)
            {
                roundEntered = true;
                entered++;
                entryTimes.Add(RoundT);
                Event("site_entry", RoundT * 1000f, Kit.UsedThisRound ? 1 : 0);
            }
        }
        if (defenders.Count > 0 && defenders.All(d => !d.Alive))
            EndRound(true, $"SITE CLEAR  {RoundT:0.0}s", UiTheme.Good);
        else if (RoundT > RoundTime)
            EndRound(false, "TIME", UiTheme.Warn);
    }

    internal Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);

    /// <summary>Flat direction from the spawn through the choke into the site.</summary>
    internal Vector3 SiteAxis
    {
        get
        {
            var a = MapSpot.Choke - MapSpot.StartFeet; a.Y = 0;
            return a.LengthSquared() > 1e-4f ? a.Normalized() : Vector3.Forward;
        }
    }

    internal IEnumerable<BotCharacter> LiveDefenders => defenders.Where(d => d.Alive).Select(d => d.Body);
    internal bool AnyDefenderVisible => defenders.Any(d => d.Alive && G.LineOfSight(G.View.Eye, d.Body.Head));
    internal bool AnyDefenderSeesPlayer => defenders.Any(d => d.Alive && d.Brain.SeesPlayer);

    /// <summary>The kit used an ability (for "ability before the first duel" and the dev log).</summary>
    internal void OnAbilityUsed(string tag)
    {
        if (firstDuel == 0) abilityBeforeDuel = true;
        if (defenders.Any(d => d.Alive && d.Brain.SeesPlayer)) seenBeforeAbility++;
        Event("ability_" + tag, RoundT * 1000f);
    }

    /// <summary>A loud ability cue at <paramref name="at"/>: defenders in earshot that aren't fighting may turn toward it
    /// (chance and speed by tier).</summary>
    internal void Alert(Vector3 at, float range, float chanceMul = 1f)
    {
        float chance = Difficulty.T(Tier, 0.1f, 0.3f, 0.5f, 0.7f, 0.85f) * chanceMul;
        foreach (var d in defenders)
        {
            if (!d.Alive || d.Brain.SeesPlayer || d.Body.Head.DistanceTo(at) > range) continue;
            if (Rng.NextDouble() >= chance) continue;
            var to = at + Vector3.Up * 1.2f - d.Body.Head;
            if (to.LengthSquared() < 0.01f) continue;
            d.TurnDir = to.Normalized();
            d.TurnAt = Now + Mathf.Max(0.15f, d.Brain.Skill.ReactMs / 1000f * R(0.8f, 1.3f));
            d.BackAt = d.TurnAt + R(2.5f, 4f);
            alertsHeard++;
        }
    }

    bool TrackingMiss()
    {
        float h = G.Mover.Speed, v = G.PlayerVerticalSpeed;
        float s = Mathf.Sqrt(h * h + v * v);
        if (s < 7f) return false;
        float p = Mathf.Clamp((s - 7f) / 17f, 0f, 0.5f);
        if (Rng.NextDouble() >= p) return false;
        dodged++;
        return true;
    }

    void Face(Defender d, Vector3 dir)
    {
        d.Brain.HeldDir = dir.Normalized();
        d.Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
    }

    static Vector3 TurnToward(Vector3 from, Vector3 to, float maxRad)
    {
        float a = from.AngleTo(to);
        if (a <= maxRad || a < 1e-4f) return to;
        var axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-8f) axis = Vector3.Up;
        return from.Rotated(axis.Normalized(), maxRad).Normalized();
    }

    void UpdateDefenders(float dt)
    {
        foreach (var d in defenders)
        {
            if (!d.Alive) continue;
            if (d.Brain.SeesPlayer) { d.TurnAt = d.BackAt = -1f; }
            else if (d.TurnAt >= 0 && Now >= d.TurnAt)
            {
                bool back = Now >= d.BackAt;
                var target = back ? d.Hold : d.TurnDir;
                Face(d, TurnToward(d.Brain.HeldDir, target, (back ? 6f : 10f) * dt));
                if (back && d.Brain.HeldDir.AngleTo(d.Hold) < 0.03f) { d.TurnAt = d.BackAt = -1f; Face(d, d.Hold); }
            }
            d.Brain.Update(G, dt);
            if (between > 0) return; // the player died
            if (!d.Seen && G.LineOfSight(G.View.Eye, d.Body.Head)) { d.Seen = true; SeenEvent(d.Body.Head); }
            if (d.Brain.SeesPlayer && !d.Spotted) { d.Spotted = true; Event("bot_spotted_you", RoundT * 1000f, Kit.UsedThisRound ? 1 : 0); }
        }
    }

    // ------------------------------------------------------------------ shooting

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (between > 0) return float.PositiveInfinity;
        float wall = Wall(o, d);
        var hit = ShootBots(defenders.Where(x => x.Alive).Select(x => x.Body), o, d, out var zone, out bool killed, out float dist, wall);
        float kitHit = Kit.HitTest(o, d, Mathf.Min(wall, hit != null ? dist : float.PositiveInfinity));
        if (hit == null) return kitHit;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100;
            DecideFirstDuel(true);
            if (Now - Kit.LastAbilityAt < 3f)
            {
                float ms = (Now - Kit.LastAbilityAt) * 1000f;
                abilityToKillMs.Add(ms);
                abilityKills++;
                Score += 50;
                Event("ability_kill", ms, 0);
            }
            Kit.OnKill(hit, hit.Feet);
        }
        return dist;
    }

    // ------------------------------------------------------------------ HUD

    public override string? Prompt => between > 0 ? null : Kit?.Prompt;

    public override IEnumerable<string> HudLines()
    {
        if (between > 0 || Kit == null) yield break;
        if (rounds <= 1 || (Kit.RoundsUsed == 0 && rounds <= 3))
            foreach (var s in Kit.HowTo.Split(". ", StringSplitOptions.RemoveEmptyEntries)) yield return s.TrimEnd('.') + ".";
        yield return $"Defenders left: {defenders.Count(d => d.Alive)}";
        foreach (var l in Kit.StatusLines()) yield return l;
    }

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (between > 0 || Kit == null) return;
        float k = UiTheme.S(size);
        Kit.Draw2D(c, size, k);
        DrawPlates(c, size, k, Kit.Color, Kit.Slots.ToList());
    }

    /// <summary>Ability plates right of the ammo counter: key badge, name, state, charges, progress bar.</summary>
    internal static void DrawPlates(CanvasItem c, Vector2 size, float k, Color color, List<KitSlot> slots)
    {
        float cx = size.X / 2, bw = 250 * k, bh = 64 * k, gap = 10 * k;
        float y = size.Y - 30 * k - 74 * k - 12 * k - bh;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var r = new Rect2(cx + 140 * k + i * (bw + gap), y, bw, bh);
            Gfx.Plate(c, r, 10 * k, UiTheme.Plate, new Color(UiTheme.Text, s.Active ? 0.3f : 0.08f));
            bool ready = s.Charges > 0 || s.Active;
            var accent = new Color(color, ready ? 1f : 0.35f);
            var key = new Rect2(r.Position + new Vector2(12 * k, 12 * k), new Vector2(40 * k, 40 * k));
            c.DrawRect(key, new Color(0, 0, 0, 0.35f));
            c.DrawRect(key, accent, false, Mathf.Max(1f, 2f * k));
            string kt = InputBinding.Cap(s.Key is { } sk ? Main.I.Valorant.Binds.Hint(sk) : DuelistKit.KeyText(s.Slot));
            int ks = UiTheme.Fs(kt.Length > 2 ? 12 : 22, k);
            Gfx.TextC(c, UiTheme.Display, kt, key.GetCenter().X, Gfx.Mid(key.GetCenter().Y, ks), ks, UiTheme.Text);
            int ns = UiTheme.Fs(17, k), ss = UiTheme.Fs(12, k);
            float tx = key.End.X + 12 * k;
            Gfx.TextFit(c, UiTheme.HudWide, s.Name.ToUpperInvariant(), tx, Gfx.Mid(r.Position.Y + 22 * k, ns), ns, UiTheme.Text, r.End.X - tx - 40 * k);
            Gfx.TextFit(c, UiTheme.HudWide, Main.I.Valorant.Binds.Hint(s.State), tx, Gfx.Mid(r.Position.Y + 44 * k, ss), ss, s.Active ? accent : UiTheme.Dim, r.End.X - tx - 8 * k);
            for (int j = 0; j < s.Max; j++)
            {
                var pc = new Vector2(r.End.X - 16 * k - j * 16 * k, r.Position.Y + 22 * k);
                Gfx.Diamond(c, pc, 5.5f * k, 5.5f * k, j < s.Charges ? accent : new Color(UiTheme.Text, 0.15f));
            }
            if (s.Progress >= 0)
            {
                float bx = r.Position.X + 12 * k, bwid = r.Size.X - 24 * k, by = r.End.Y - 7 * k;
                c.DrawRect(new Rect2(bx, by, bwid, 3 * k), new Color(1, 1, 1, 0.12f));
                c.DrawRect(new Rect2(bx, by, bwid * Mathf.Clamp(s.Progress, 0, 1), 3 * k), accent);
            }
        }
    }

    // ------------------------------------------------------------------ results

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Agent", $"{AgentTitle} — {AbilityTitle}");
        yield return ("Rounds won / lost", $"{won} / {lost}");
        int duels = firstWon + firstLost;
        if (duels > 0) yield return ("First duels won", $"{firstWon} of {duels}  ({100f * firstWon / duels:0}%)");
        if (entryTimes.Count > 0) yield return ("Time to enter site", $"{entryTimes.Average():0.0} s  ({entered} of {rounds})");
        if (Kit != null)
        {
            yield return ("Ability used", $"{Kit.RoundsUsed} of {rounds} rounds");
            if (duels > 0) yield return ("Used before 1st duel", $"{firstWithAbility} of {duels}");
            if (abilityToKillMs.Count > 0) yield return ("Ability → kill", $"{abilityToKillMs.Average():0} ms  ({abilityKills} kill{(abilityKills == 1 ? "" : "s")})");
            if (seenBeforeAbility > 0) yield return ("Spotted before using it", seenBeforeAbility.ToString());
            foreach (var l in Kit.ResultLines()) yield return l;
        }
        if (dodged > 0) yield return ("Hits dodged (speed)", dodged.ToString());
        if (alertsHeard > 0) yield return ("Heard your ability", $"{alertsHeard} defender{(alertsHeard == 1 ? "" : "s")}");
        if (clearTimes.Count > 0) yield return ("Avg clear time", $"{clearTimes.Average():0.0} s");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        foreach (var l in MovementLines()) yield return l;
    }

    /// <summary>First-duel win rate vs the tier's defenders; one tier lower when the ability was used in under half the rounds.</summary>
    public override int Badge()
    {
        int duels = firstWon + firstLost;
        if (duels == 0 || won + lost == 0) return -1;
        float rate = 0.5f * firstWon / duels + 0.5f * won / (won + lost);
        int b = Difficulty.BadgeWinRate(Tier, rate, 0.40f);
        if (Kit != null && rounds > 0 && (float)Kit.RoundsUsed / rounds < 0.5f) b--;
        return Math.Max(-1, b);
    }
}

/// <summary>
/// Chamber Guns ("chamber:headhunter" / "chamber:tdf"): hold a real site angle with Chamber's own guns while attackers peek
/// the choke and shoot back. Headhunter: one-tap heads at mid range vs wide swings, jiggles and strafing peeks, 8 bullets a
/// round (tap discipline: spamming opens the spread). Tour De Force: Operator-like holds vs swings and crossings, any upper
/// body hit kills, 5 bullets a round. Implementation in ChamberDrill.cs.
/// </summary>
public sealed partial class ChamberMode : MapMode
{
    /// <summary>Weapon variants this drill supports ("headhunter", "tdf" = Tour De Force).</summary>
    public static readonly string[] Variants = { "headhunter", "tdf" };
    public readonly string Variant;
    public ChamberMode(string variant = "headhunter")
    {
        var v = string.IsNullOrWhiteSpace(variant) ? "headhunter" : variant.Trim().ToLowerInvariant();
        Variant = Variants.Contains(v) ? v : "headhunter";
    }
    public override string Key => "chamber:" + Variant;
    public override string Name => "Chamber Guns";
    public override string Description => "One-tap drills with Headhunter and Tour De Force.";
    public override string Category => "Agents";
}
