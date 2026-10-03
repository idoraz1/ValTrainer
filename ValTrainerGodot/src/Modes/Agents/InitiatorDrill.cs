using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Shared engine of the initiator drills (Flash &amp; Peek, Recon &amp; Clear): Site Clear's rounds on a real map spot (you
/// start behind the choke, 2–4 tier-scaled defenders hold real angles from spots that can't see your spawn) plus the
/// player's agent ability (<see cref="AbilityKit"/>) and what it does to the defenders:
/// <list type="bullet">
/// <item>flashes blind with VALORANT's on-screen rule from each bot's own view (<see cref="BlindModel"/>): full blind =
/// can't spot / aim / shoot, then a dazed fade (slower reaction, worse aim); higher tiers sometimes turn away from a flash
/// they see coming; your own flash can blind you too;</item>
/// <item>nearsight (Leer, Paranoia) limits a bot's vision to a few metres;</item>
/// <item>recon (Recon Bolt, Haunt) reveals defenders through walls (outline + ping);</item>
/// <item>defenders shoot destructible utility (Leer, Dizzy, recon devices) when they see it (tier-scaled).</item>
/// </list>
/// </summary>
public abstract partial class InitiatorDrill : MapMode
{
    public override string Category => "Agents";
    public override string SimKind => "bot";
    public override float Duration => 120f;
    public override string? Subtitle => $"{Ability.Agent} · {Ability.Ability}  —  {MapSpot.Map} {MapSpot.Name}";
    public override AimFocus? Focus => between > 0 ? null : NearestHead(defenders.Where(d => d.Alive).Select(d => d.Body));

    protected readonly InitiatorAbility Ability;
    protected AbilityKit Kit = null!;

    protected InitiatorDrill(InitiatorAbility ability) => Ability = ability;

    protected sealed class Defender
    {
        public BotCharacter Body = null!;
        public BotBrain Brain = null!;
        /// <summary>The angle it holds (restored after turning away from a flash or shooting utility).</summary>
        public Vector3 Hold;
        // turning away from a flash it saw coming
        public float TurnAt = -1f, BackAt = -1f;
        public Vector3 TurnDir;
        public bool Turned;
        // shooting a utility it can see
        public PlayerUtility? Target;
        public float ShootAt = -1f;
        public int ShotsFired;
        // bookkeeping
        public bool Seen, Spotted, Revealed, CountedUtil;
        public float RevealUntil = -1f, PingUntil = -1f, UtilHitAt = -99f;
        public Vector3 PingAt;
        public readonly HashSet<PlayerUtility> Noticed = new();
        public bool Alive => GodotObject.IsInstanceValid(Body) && !Body.Dead;
        public Vector3 Chest => Body.Head + Vector3.Down * 0.45f;
    }

    protected readonly List<Defender> defenders = new();
    protected readonly List<PlayerUtility> utils = new();
    readonly Dictionary<PlayerUtility, HashSet<Defender>> touched = new();
    readonly List<Node> roundNodes = new();
    protected float roundStart, between;
    protected int won, lost, rounds, roundKills, defendersSpawned;
    protected readonly List<float> clearTimes = new();
    protected bool castThisRun;

    // stats
    protected int casts, blindFull, blindPartial, nearsighted, utilKills, selfFlashes, dryPeeks, revealed, destroyedUtil, fizzles;
    protected readonly List<float> popToKillMs = new();
    protected readonly List<float> preaimRevealed = new(), preaimOther = new();

    protected float RoundTime => Difficulty.SiteRoundTime(Tier) + 6f;
    protected float RoundT => Now - roundStart;
    static float Aspect => 16f / 9f;

    // ------------------------------------------------------------------ setup / rounds

    protected override void Setup()
    {
        Kit = new AbilityKit(G, Ability)
        {
            Cast = (primary, bars, bounces) => CastAbility(primary, bars, bounces, G.View.Forward, visual: true) != null,
            CanRecast = () => utils.Any(u => u.CanRecast),
            Recast = () => { foreach (var u in utils.Where(u => u.CanRecast).ToList()) u.Recast(); },
            Steering = () => utils.OfType<GuidingLight>().Any(h => !h.Done && h.Steering),
        };
        PrewarmVisuals();
        NewRound();
    }

    void NewRound()
    {
        foreach (var d in defenders) if (GodotObject.IsInstanceValid(d.Body)) G.Despawn(d.Body);
        defenders.Clear();
        ClearUtils();
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
            var d = new Defender { Body = body, Brain = brain, Hold = brain.HeldDir };
            if (Ability.IsRecon) RevealFx.Attach(body, G.Enemy.Lerp(Colors.White, 0.15f));
            defenders.Add(d);
        }
        defendersSpawned += defenders.Count;
        rounds++;
        roundKills = 0;
        roundStart = Now;
        G.Respawn(MapSpot.StartFeet, MapSpot.StartYaw);
        G.Mover.ResetStops();
        G.Blind.Reset();
        Kit.ResetRound();
        DevNewRound();
    }

    void ClearUtils()
    {
        foreach (var u in utils) u.FreeVisual();
        utils.Clear();
        touched.Clear();
        foreach (var n in roundNodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        roundNodes.Clear();
    }

    void EndRound(bool win, string text, Color color)
    {
        if (win) { won++; float t = RoundT; clearTimes.Add(t); Event("round_won", t); Score += 200 + (int)Mathf.Max(0, (15 - t) * 20); }
        else { lost++; Event("round_lost"); }
        G.Banner(text, color);
        Kit.PutAway(0f);
        ClearUtils();
        between = 1.6f;
    }

    public override void OnPlayerDied()
    {
        Deaths++;
        Score -= 100;
        EndRound(false, "YOU DIED", UiTheme.Accent);
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
        Kit.Update(dt);
        DevUpdate(dt);
        UpdateUtils(dt);
        UpdateDefenders(dt);
        if (between > 0) return; // died this frame
        if (defenders.Count > 0 && defenders.All(d => !d.Alive))
            EndRound(true, $"SITE CLEAR  {RoundT:0.0}s", UiTheme.Good);
        else if (RoundT > RoundTime)
            EndRound(false, "TIME", UiTheme.Warn);
    }

    // ------------------------------------------------------------------ casting

    /// <summary>Spawns the ability's projectile / device from the player (visual = false: a prediction for the dev auto-throw).
    /// Null when it can't be cast (Breach: no wall to burn through).</summary>
    protected PlayerUtility? CastAbility(bool primary, int bars, int bounces, Vector3 aim, bool visual)
    {
        aim = aim.Normalized();
        var hand = PlayerUtility.HandPoint(G, aim);
        PlayerUtility? u;
        switch (Ability.Kind)
        {
            case UtilKind.Curveball: u = new Curveball(G, hand, aim, right: !primary, visual); break;
            case UtilKind.GuidingLight: u = new GuidingLight(G, hand, aim, visual); break;
            case UtilKind.Blindside: u = new Blindside(G, hand, aim, visual); break;
            case UtilKind.FlashDrive: u = new FlashDrive(G, hand, aim, overhand: primary, visual); break;
            case UtilKind.Flashpoint:
                var why = Flashpoint.Aim(G.Solid, G.View.Eye, aim, out var entry, out var exit);
                if (why != null) { if (visual) Kit.Refuse(why); return null; }
                u = new Flashpoint(G, hand, entry, exit, aim, visual);
                break;
            case UtilKind.Dizzy: u = new Dizzy(G, hand, aim, visual); break;
            case UtilKind.Leer: u = new LeerEye(G, hand, aim, visual); break;
            case UtilKind.Paranoia: u = new Paranoia(G, hand, aim, visual); break;
            case UtilKind.ReconBolt: u = new ReconBolt(G, hand, aim, bars, bounces, visual); break;
            case UtilKind.Haunt: u = new Haunt(G, hand, aim, visual); break;
            default: return null;
        }
        if (!visual) return u;
        utils.Add(u);
        casts++;
        castThisRun = true;
        Event("flash_throw", (int)Ability.Kind, Kit.Charges - 1);
        string cue = Ability.Kind switch
        {
            UtilKind.Curveball => "phoenix_wind", UtilKind.GuidingLight => "skye_wind", UtilKind.Blindside => "yoru_wind",
            UtilKind.FlashDrive => "kayo_wind", UtilKind.Flashpoint => "breach_wind", UtilKind.Dizzy => "gekko_wind", _ => "",
        };
        if (cue != "") G.Sound(cue, 0.55f);
        switch (Ability.Kind)
        {
            case UtilKind.ReconBolt: AbilitySfx.Play(G, "bow_release", 0.8f); break;
            case UtilKind.Haunt: AbilitySfx.Play(G, "haunt", 0.6f); break;
            case UtilKind.Leer: AbilitySfx.Play(G, "leer", 0.7f); break;
            case UtilKind.Paranoia: AbilitySfx.Play(G, "paranoia", 0.8f); break;
        }
        return u;
    }

    // ------------------------------------------------------------------ utilities → effects

    void UpdateUtils(float dt)
    {
        var input = new UtilInput(G.TriggerHeld, G.View.Forward);
        for (int i = 0; i < utils.Count; i++)
        {
            var u = utils[i];
            if (!u.Done) u.Update(dt, input);
            // Events can also come from outside Update (enemies shooting it: Destroyed), so drain them even when done.
            foreach (var e in u.Events.ToArray()) OnUtilEvent(u, e);
            u.Events.Clear();
            if (!u.Done) ContinuousEffects(u);
        }
        utils.RemoveAll(u => u.Done && u.Events.Count == 0);
    }

    HashSet<Defender> Touched(PlayerUtility u) => touched.TryGetValue(u, out var s) ? s : touched[u] = new HashSet<Defender>();

    void OnUtilEvent(PlayerUtility u, UtilEvent e)
    {
        switch (e.Kind)
        {
            case UtilEventKind.Pop: ApplyPop(e.At, e.Value); break;
            case UtilEventKind.DizzyFire:
                var set = Touched(u);
                foreach (var d in defenders)
                {
                    if (!d.Alive || set.Contains(d)) continue;
                    if (d.Chest.DistanceTo(e.At) > e.Value || !G.LineOfSight(e.At, d.Chest)) continue;
                    set.Add(d);
                    utils.Add(new PlasmaShot(G, e.At, d.Chest, true));
                    AbilitySfx.PlayAt(G, "dizzy", e.At, 0.8f);
                }
                break;
            case UtilEventKind.Plasma:
                foreach (var d in defenders)
                {
                    if (!d.Alive || d.Chest.DistanceTo(e.At) > PlasmaShot.Radius || !G.LineOfSight(e.At + Vector3.Up * 0.1f, d.Body.Head)) continue;
                    d.Brain.Flash(Now, e.Value, 1f);
                    UtilHit(d, full: true, e.Value, "bot_blinded");
                }
                break;
            case UtilEventKind.Scan: ApplyScan(u, e.At, e.Value); break;
            case UtilEventKind.Fizzle:
                fizzles++;
                Event("util_fizzle", (int)Ability.Kind);
                if (u is Blindside) G.Banner("No wall hit — the shard faded", UiTheme.Dim);
                else if (u is ReconBolt or Haunt) G.Banner("Lost it off the map", UiTheme.Dim);
                break;
            case UtilEventKind.Destroyed:
                destroyedUtil++;
                Event("util_destroyed", (int)Ability.Kind);
                G.Banner($"They shot your {Ability.Ability}", UiTheme.Warn);
                if (u is LeerEye)
                    foreach (var d in Touched(u)) d.Brain.NearsightUntil = Mathf.Min(d.Brain.NearsightUntil, Now + 0.25f);
                break;
            case UtilEventKind.Landed:
                if (u is ReconBolt) AbilitySfx.PlayAt(G, "bolt_tick", e.At, 1f);
                else if (u is Haunt) AbilitySfx.PlayAt(G, "haunt", e.At, 0.7f);
                else if (u is Blindside) G.SoundAt("shatter", e.At, maxRange: 50f, floor: 0.6f, vol: 0.6f);
                break;
        }
    }

    /// <summary>Leer and Paranoia work every frame they're out.</summary>
    void ContinuousEffects(PlayerUtility u)
    {
        switch (u)
        {
            case LeerEye { Active: true } eye:
            {
                var set = Touched(eye);
                foreach (var d in defenders)
                {
                    if (!d.Alive || d.Brain.IsBlind(Now)) continue;
                    if (BlindModel.Eccentricity(ViewOf(d), eye.Pos, Aspect) > 1f || !G.LineOfSight(d.Body.Head, eye.Pos)) continue;
                    d.Brain.Nearsight(Now, eye.CloseAt - eye.Age + 0.3f, LeerEye.VisionRadius);
                    if (set.Add(d)) UtilHit(d, full: true, LeerEye.ActiveTime, "bot_nearsighted", nearsight: true);
                }
                break;
            }
            case Paranoia p:
            {
                var set = Touched(p);
                foreach (var d in defenders)
                {
                    if (!d.Alive || set.Contains(d) || (!p.Touches(d.Body.Head) && !p.Touches(d.Chest))) continue;
                    set.Add(d);
                    d.Brain.Nearsight(Now, Paranoia.Duration, Paranoia.VisionRadius);
                    UtilHit(d, full: true, Paranoia.Duration, "bot_nearsighted", nearsight: true);
                }
                break;
            }
        }
    }

    /// <summary>A bot's own "screen" for the blind rule: its eyes and the direction it holds.</summary>
    static PlayerView ViewOf(Defender d)
    {
        var f = d.Brain.HeldDir.LengthSquared() > 1e-6f ? d.Brain.HeldDir.Normalized() : Vector3.Forward;
        return new PlayerView
        {
            Eye = d.Body.Head,
            Yaw = Mathf.RadToDeg(Mathf.Atan2(f.X, -f.Z)),
            Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(f.Y, -1f, 1f))),
        };
    }

    void ApplyPop(Vector3 pop, float blindMax)
    {
        int full = 0, part = 0;
        foreach (var d in defenders)
        {
            if (!d.Alive) continue;
            var (sec, grade) = BlindModel.Evaluate(blindMax, ViewOf(d), pop, G.LineOfSight(d.Body.Head, pop), Aspect);
            if (grade == BlindGrade.NoLineOfSight || sec <= 0f) continue;
            float fade = Mathf.Min(BlindModel.FadeTime, 0.3f + sec * 0.5f);
            if (grade == BlindGrade.Flashed) { d.Brain.Flash(Now, sec, fade); full++; UtilHit(d, true, sec, "bot_blinded"); }
            else if (grade == BlindGrade.Partial) { d.Brain.Flash(Now, sec * 0.35f, sec * 0.65f + fade); part++; UtilHit(d, false, sec, "bot_blinded"); }
            else d.Brain.Flash(Now, sec, fade * 0.5f); // turned away: the short minimum
            if (d.Turned && d.BackAt < 0) d.BackAt = Now + R(0.15f, 0.3f);
            else if (!d.Turned && d.TurnAt > Now) d.TurnAt = -1f; // reacted too late: it already popped
        }
        // Your own flash follows the same rule from your view.
        var (ps, pg) = BlindModel.Evaluate(blindMax, G.View, pop, G.LineOfSight(G.View.Eye, pop), ScreenAspect());
        if (ps > 0f) G.Blind.Apply(ps);
        if (pg is BlindGrade.Flashed or BlindGrade.Partial)
        {
            selfFlashes++;
            Score -= 40;
            Event("self_flash", ps);
        }
        G.SoundAt("flashpop", pop, maxRange: 50f, floor: 0.6f);
        string who = full + part == 0 ? "Nobody flashed" : $"{full + part} FLASHED" + (part > 0 ? $" ({part} partial)" : "");
        G.Banner(pg is BlindGrade.Flashed or BlindGrade.Partial ? $"{who} · you flashed yourself" : who,
            pg is BlindGrade.Flashed or BlindGrade.Partial ? UiTheme.Accent : full + part > 0 ? UiTheme.Good : UiTheme.Dim);
        Event("flash_pop", full + part, ps);
    }

    void ApplyScan(PlayerUtility source, Vector3 at, float radius)
    {
        int count = 0;
        bool haunt = source is Haunt;
        float revealFor = haunt ? Haunt.RevealTime : ReconBolt.RevealTime;
        foreach (var d in defenders)
        {
            if (!d.Alive) continue;
            if (d.Body.Head.DistanceTo(at) > radius) continue;
            if (!G.LineOfSight(at, d.Body.Head) && !G.LineOfSight(at, d.Chest)) continue;
            count++;
            d.RevealUntil = Now + revealFor;
            d.PingUntil = Now + revealFor + 2.5f;
            d.PingAt = d.Body.Head;
            if (haunt && TerrorTrail.Spawn(G, at - Vector3.Up * 0.45f, d.Body.Feet) is { } trail) roundNodes.Add(trail);
            if (!d.Revealed)
            {
                d.Revealed = true;
                revealed++;
                Score += 30;
            }
        }
        Event("bot_revealed", count, haunt ? 1 : 0);
        AbilitySfx.PlayAt(G, "recon_ping", at, 0.9f, maxRange: 60f, floor: 0.7f);
        if (count > 0) AbilitySfx.Play(G, "reveal", 0.6f);
        G.Banner(count == 0 ? "Scan: nobody seen" : $"{count} REVEALED", count == 0 ? UiTheme.Dim : UiTheme.Good);
    }

    void UtilHit(Defender d, bool full, float seconds, string ev, bool nearsight = false)
    {
        d.UtilHitAt = Now;
        Event(ev, seconds, nearsight ? 3 : full ? 2 : 1);
        if (nearsight) { nearsighted++; Score += 25; }
        else if (full) { blindFull++; Score += 30; }
        else { blindPartial++; Score += 10; }
    }

    float ScreenAspect()
    {
        var size = ((Node)G).GetViewport().GetVisibleRect().Size;
        return size.X / Mathf.Max(1, size.Y);
    }

    // ------------------------------------------------------------------ defenders

    static float TurnChance(int tier) => Difficulty.T(tier, 0f, 0.12f, 0.3f, 0.45f, 0.6f);
    static float ShootUtilChance(int tier) => Difficulty.T(tier, 0f, 0.25f, 0.5f, 0.7f, 0.85f);

    /// <summary>Time to find a device after it comes into view, before aiming at it: a recon arrow on a wall is hard to
    /// spot, a glowing eye or Dizzy much easier (estimates, faster at higher tiers).</summary>
    static float FindDelay(PlayerUtility u, int tier) => u switch
    {
        ReconBolt => Difficulty.T(tier, 1.3f, 1.0f, 0.75f, 0.6f, 0.5f),
        Haunt => Difficulty.T(tier, 1.0f, 0.8f, 0.6f, 0.45f, 0.35f),
        LeerEye => Difficulty.T(tier, 0.6f, 0.45f, 0.35f, 0.25f, 0.2f),
        _ => Difficulty.T(tier, 0.5f, 0.4f, 0.3f, 0.25f, 0.2f),
    };

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    float ReactSeconds(Defender d) => Mathf.Max(0.12f, Gauss(d.Brain.Skill.ReactMs, d.Brain.Skill.ReactSd) / 1000f);

    bool BotSees(Defender d, Vector3 p, bool ignoreNearsight = false)
    {
        var b = d.Brain;
        if (b.IsBlind(Now)) return false;
        var head = d.Body.Head;
        var to = p - head;
        float dist = to.Length();
        if (dist < 0.05f) return true;
        if (!ignoreNearsight && b.IsNearsighted(Now) && dist > b.NearsightRange) return false;
        if (Mathf.RadToDeg(b.HeldDir.AngleTo(to)) > b.HalfFovDeg) return false;
        return G.LineOfSight(head, p);
    }

    void Face(Defender d, Vector3 dir)
    {
        d.Brain.HeldDir = dir.Normalized();
        d.Body.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
    }

    void UpdateDefenders(float dt)
    {
        foreach (var d in defenders)
        {
            if (!d.Alive) { if (GodotObject.IsInstanceValid(d.Body)) RevealFx.Set(d.Body, 0f); continue; }
            bool engaging = d.Brain.SeesPlayer;

            // Notice utility in view: flashes may be dodged by turning away, destructible things get shot.
            foreach (var u in utils)
            {
                if (u.Done || !u.VisibleToEnemies || d.Noticed.Contains(u) || engaging) continue;
                bool candidate = (u.IsFlash && d.TurnAt < 0) || (u.Shootable && d.Target == null);
                if (!candidate || !BotSees(d, u.Pos, ignoreNearsight: u is LeerEye)) continue;
                d.Noticed.Add(u);
                if (u.IsFlash && Rng.NextDouble() < TurnChance(Tier))
                {
                    d.TurnAt = Now + ReactSeconds(d);
                    var away = d.Body.Head - u.Pos;
                    away.Y = 0;
                    if (away.LengthSquared() < 1e-4f) away = -d.Hold;
                    d.TurnDir = away.Normalized().Rotated(Vector3.Up, R(-0.5f, 0.5f));
                    d.BackAt = -1f;
                }
                else if (u.Shootable && Rng.NextDouble() < ShootUtilChance(Tier))
                {
                    d.Target = u;
                    d.ShootAt = Now + ReactSeconds(d) + FindDelay(u, Tier) + R(0.08f, 0.3f);
                    d.ShotsFired = 0;
                }
            }

            if (!engaging && !d.Brain.IsBlind(Now))
            {
                // Turning away from a flash, then back to the held angle.
                if (d.TurnAt >= 0 && Now >= d.TurnAt)
                {
                    d.Turned = true;
                    if (d.BackAt < 0 && Now - d.TurnAt > 1.6f) d.BackAt = Now;
                    bool back = d.BackAt >= 0 && Now >= d.BackAt;
                    var target = back ? d.Hold : d.TurnDir;
                    Face(d, GuidingLight.TurnToward(d.Brain.HeldDir, target, (back ? 9f : 16f) * dt));
                    if (back && d.Brain.HeldDir.AngleTo(d.Hold) < 0.05f) { d.TurnAt = -1f; d.BackAt = -1f; d.Turned = false; Face(d, d.Hold); }
                }
                // Shooting a device it can see.
                else if (d.Target is { } t)
                {
                    if (t.Done || !G.LineOfSight(d.Body.Head, t.Pos))
                    {
                        d.Target = null;
                        Face(d, d.Hold);
                    }
                    else
                    {
                        Face(d, GuidingLight.TurnToward(d.Brain.HeldDir, (t.Pos - d.Body.Head).Normalized(), 12f * dt));
                        if (Now >= d.ShootAt && d.ShotsFired < 12)
                        {
                            d.ShotsFired++;
                            d.ShootAt += Weapons.Vandal.Interval;
                            bool hit = Rng.NextDouble() < d.Brain.Skill.PHit1 * Mathf.Pow(d.Brain.Skill.Decay, d.ShotsFired - 1) + 0.1f;
                            var aimAt = hit ? t.Pos : t.Pos + new Vector3(R(-0.5f, 0.5f), R(-0.3f, 0.4f), R(-0.5f, 0.5f));
                            d.Body.ShootFx(aimAt);
                            G.SoundAt("enemyshot", d.Body.Head, maxRange: 60f, floor: 0.6f);
                            if (hit) t.Damage(Weapons.Vandal.Damage(1, d.Body.Head.DistanceTo(t.Pos)));
                        }
                    }
                }
            }
            else if (engaging) { d.TurnAt = -1f; d.Turned = false; d.Target = null; }

            d.Brain.Update(G, dt);
            if (between > 0) return; // the player died

            // First sight of this defender: crosshair placement (aim coach) and the recon pre-aim stat.
            if (!d.Seen && G.LineOfSight(G.View.Eye, d.Body.Head))
            {
                d.Seen = true;
                SeenEvent(d.Body.Head);
                float err = G.View.AngleTo(d.Body.Head);
                (d.Revealed ? preaimRevealed : preaimOther).Add(err);
            }
            // Spotted you: a dry peek if no utility is affecting it.
            if (d.Brain.SeesPlayer && !d.Spotted)
            {
                d.Spotted = true;
                bool helped = d.Brain.IsDazed(Now) || d.Brain.IsNearsighted(Now) || d.Turned || d.Target != null || Now - d.UtilHitAt < 2.5f;
                if (!helped && CountsDryPeeks) { dryPeeks++; Score -= 30; Event("dry_peek"); }
            }
            // Reveal outline fade.
            if (Ability.IsRecon)
            {
                float a = Now < d.RevealUntil ? 1f : Mathf.Clamp(1f - (Now - d.RevealUntil) / 0.3f, 0f, 1f);
                RevealFx.Set(d.Body, a);
            }
        }
    }

    /// <summary>Flash &amp; Peek counts swings into defenders no utility touched.</summary>
    protected virtual bool CountsDryPeeks => false;

    // ------------------------------------------------------------------ shooting

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (between > 0) return float.PositiveInfinity;
        var hit = ShootBots(defenders.Where(x => x.Alive).Select(x => x.Body), o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100;
            roundKills++;
            var def = defenders.FirstOrDefault(x => x.Body == hit);
            if (def != null && (def.Brain.IsBlind(Now) || def.Brain.IsDazed(Now) || def.Brain.IsNearsighted(Now) || def.Turned))
            {
                utilKills++;
                Score += 50;
                if (Now - def.UtilHitAt < 6f) popToKillMs.Add((Now - def.UtilHitAt) * 1000f);
                Event("util_kill", (Now - def.UtilHitAt) * 1000f);
            }
            // Phoenix: every two kills recharge a Curveball.
            if (Ability.Kind == UtilKind.Curveball && roundKills % 2 == 0 && Kit.Charges < Ability.Charges)
            {
                Kit.AddCharge();
                G.Banner("Curveball recharged (2 kills)", UiTheme.Teal);
            }
        }
        return dist;
    }

    // ------------------------------------------------------------------ HUD

    public override string? Prompt
    {
        get
        {
            if (between > 0) return null;
            if (Kit.RefusalT > 0 && Kit.Refusal != null) return Kit.Refusal;
            return Kit.State switch
            {
                AbilityKit.Phase.Equipping or AbilityKit.Phase.Equipped => Ability.Alt != null ? $"{Ability.Primary}   ·   {Ability.Alt}" : Ability.Primary,
                AbilityKit.Phase.Charging => $"Release LMB to shoot   ·   RMB: bounces ({Kit.Bounces})",
                AbilityKit.Phase.Steering => $"Steering — {Kit.KeyText} to pop it",
                _ => utils.Any(u => u.CanRecast) ? Ability.Kind == UtilKind.Haunt ? $"{Kit.KeyText}: drop the watcher now" : $"{Kit.KeyText}: pop the flash" : null,
            };
        }
    }

    public override IEnumerable<string> HudLines()
    {
        if (between > 0) yield break;
        if (!castThisRun && rounds <= 2)
            foreach (var s in Ability.HowTo.Split(". ", StringSplitOptions.RemoveEmptyEntries)) yield return s.TrimEnd('.') + ".";
        yield return $"Defenders left: {defenders.Count(d => d.Alive)}";
        foreach (var l in StatusLines()) yield return l;
    }

    protected virtual IEnumerable<string> StatusLines() => Array.Empty<string>();

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (between > 0) return;
        float k = UiTheme.S(size);
        DrawMarkers(c, size, k);
        DrawAbilityPlate(c, size, k);
    }

    void DrawAbilityPlate(CanvasItem c, Vector2 size, float k)
    {
        float cx = size.X / 2, bw = 300 * k, bh = 74 * k;
        float ammoY = size.Y - 30 * k - bh;
        var r = new Rect2(cx + 140 * k, ammoY - 12 * k - 64 * k, bw, 64 * k);
        Gfx.Plate(c, r, 10 * k, UiTheme.Plate, new Color(UiTheme.Text, 0.08f));
        bool ready = Kit.Charges > 0;
        var accent = new Color(Ability.Color, ready ? 1f : 0.35f);
        // key badge
        var key = new Rect2(r.Position + new Vector2(12 * k, 12 * k), new Vector2(40 * k, 40 * k));
        c.DrawRect(key, new Color(0, 0, 0, 0.35f));
        c.DrawRect(key, accent, false, Mathf.Max(1f, 2f * k));
        int ks = UiTheme.Fs(Kit.KeyText.Length > 2 ? 12 : 22, k);
        Gfx.TextC(c, UiTheme.Display, Kit.KeyText.ToUpperInvariant(), key.GetCenter().X, Gfx.Mid(key.GetCenter().Y, ks), ks, UiTheme.Text);
        // name + state
        int ns = UiTheme.Fs(18, k), ss = UiTheme.Fs(12, k);
        float tx = key.End.X + 12 * k;
        Gfx.Text(c, UiTheme.HudWide, Ability.Ability.ToUpperInvariant(), tx, Gfx.Mid(r.Position.Y + 22 * k, ns), ns, UiTheme.Text);
        string state = Kit.State switch
        {
            AbilityKit.Phase.Equipping => "EQUIPPING…",
            AbilityKit.Phase.Equipped => "READY TO CAST",
            AbilityKit.Phase.Charging => $"CHARGE {ReconBolt.BarsFor(Kit.ChargeHeld)}/3 · BOUNCES {Kit.Bounces}",
            AbilityKit.Phase.Steering => "STEERING",
            _ => utils.Any(u => !u.Done && u is not PlasmaShot) ? "OUT" : ready ? "READY" : "NO CHARGES",
        };
        if (Ability.Kind == UtilKind.ReconBolt && Kit.State == AbilityKit.Phase.Equipped) state = $"READY · BOUNCES {Kit.Bounces}";
        Gfx.Text(c, UiTheme.HudWide, state, tx, Gfx.Mid(r.Position.Y + 44 * k, ss), ss, Kit.State != AbilityKit.Phase.Ready ? accent : UiTheme.Dim);
        // charges (pips)
        for (int i = 0; i < Ability.Charges; i++)
        {
            var pc = new Vector2(r.End.X - 18 * k - i * 18 * k, r.Position.Y + 22 * k);
            Gfx.Diamond(c, pc, 6 * k, 6 * k, i < Kit.Charges ? accent : new Color(UiTheme.Text, 0.15f));
        }
        // equip / charge progress
        float prog = Kit.State switch
        {
            AbilityKit.Phase.Equipping => Mathf.Clamp(Kit.StateTime / Ability.Equip, 0, 1),
            AbilityKit.Phase.Charging => Mathf.Clamp(Kit.ChargeHeld / (3 * ReconBolt.ChargePerBar), 0, 1),
            _ => -1f,
        };
        if (prog >= 0)
        {
            float bx = r.Position.X + 12 * k, bwid = r.Size.X - 24 * k, by = r.End.Y - 7 * k;
            c.DrawRect(new Rect2(bx, by, bwid, 3 * k), new Color(1, 1, 1, 0.12f));
            c.DrawRect(new Rect2(bx, by, bwid * prog, 3 * k), accent);
            if (Kit.State == AbilityKit.Phase.Charging)
                for (int i = 1; i < 3; i++) c.DrawRect(new Rect2(bx + bwid * i / 3f - 1 * k, by - 2 * k, 2 * k, 7 * k), UiTheme.Text);
        }
    }

    void DrawMarkers(CanvasItem c, Vector2 size, float k)
    {
        var cam = c.GetViewport()?.GetCamera3D();
        if (cam == null) return;
        int fs = UiTheme.Fs(12, k);
        foreach (var d in defenders)
        {
            if (!d.Alive) continue;
            // Recon ping: where the bot was revealed (through walls), fading.
            var pingAt = d.PingAt + Vector3.Up * 0.35f;
            if (Now < d.PingUntil && OnScreen(cam, pingAt, size, out var p))
            {
                float a = Mathf.Clamp((d.PingUntil - Now) / 0.6f, 0f, 1f);
                var col = new Color(G.Enemy, a);
                float pulse = Now < d.RevealUntil ? 1f + 0.15f * Mathf.Sin(Now * 18f) : 1f;
                Gfx.DiamondLine(c, p, 9 * k * pulse, 11 * k * pulse, col, Mathf.Max(1f, 2f * k));
                Gfx.Diamond(c, p, 3.5f * k, 4 * k, col);
                Gfx.TextC(c, UiTheme.HudWide, $"{G.View.Eye.DistanceTo(d.PingAt):0}m", p.X, p.Y - 16 * k, fs, new Color(UiTheme.Text, 0.85f * a));
            }
            // Training aid: a visible defender your utility is affecting.
            bool blind = d.Brain.IsBlind(Now), dazed = d.Brain.IsDazed(Now), near = d.Brain.IsNearsighted(Now);
            if (!(blind || dazed || near) || !G.LineOfSight(G.View.Eye, d.Body.Head)) continue;
            if (!OnScreen(cam, d.Body.Head + Vector3.Up * 0.45f, size, out var h)) continue;
            string tag = blind ? $"BLIND {d.Brain.BlindUntil - Now:0.0}" : near ? "NEARSIGHTED" : "DAZED";
            var tc = blind ? new Color(1f, 0.97f, 0.85f) : near ? new Color(0.8f, 0.6f, 1f) : UiTheme.Warn;
            float w = Gfx.TextW(UiTheme.HudWide, tag, fs) + 12 * k;
            c.DrawRect(new Rect2(h.X - w / 2, h.Y - fs - 4 * k, w, fs + 8 * k), new Color(0, 0, 0, 0.45f));
            Gfx.TextC(c, UiTheme.HudWide, tag, h.X, h.Y, fs, tc);
        }
    }

    /// <summary>Screen position of a world point in front of the camera (false behind it or far off screen).</summary>
    static bool OnScreen(Camera3D cam, Vector3 world, Vector2 size, out Vector2 p)
    {
        p = Vector2.Zero;
        var local = cam.GlobalTransform.AffineInverse() * world;
        if (local.Z > -0.1f) return false; // behind or at the camera plane
        p = cam.UnprojectPosition(world);
        return float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X > -size.X && p.X < 2 * size.X && p.Y > -size.Y && p.Y < 2 * size.Y;
    }

    // ------------------------------------------------------------------ results

    protected IEnumerable<(string, string)> CommonResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Agent", $"{Ability.Agent} — {Ability.Ability}");
        yield return ("Rounds won / lost", $"{won} / {lost}");
        if (clearTimes.Count > 0) yield return ("Avg clear time", $"{clearTimes.Average():0.0} s");
    }

    protected IEnumerable<(string, string)> DuelResultLines()
    {
        yield return ("Kills / deaths", $"{Kills} / {Deaths}");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        foreach (var l in MovementLines()) yield return l;
    }

    protected int WinBadge(float share, float needShare)
    {
        if (won + lost == 0) return -1;
        int b = Difficulty.BadgeWinRate(Tier, (float)won / (won + lost), 0.40f);
        if (share < needShare) b--; // winning without using the ability well doesn't earn the drill's badge
        return Math.Max(-1, b);
    }

    // ------------------------------------------------------------------ prewarm

    /// <summary>Builds each new look once under the floor during the countdown so the first cast doesn't hitch.</summary>
    void PrewarmVisuals()
    {
        var at = MapSpot.StartFeet + new Vector3(0, -1.2f, -4f);
        var dir = Vector3.Forward;
        PlayerUtility? u = Ability.Kind switch
        {
            UtilKind.Leer => new LeerEye(G, at, dir, true),
            UtilKind.Paranoia => new Paranoia(G, at, dir, true),
            UtilKind.ReconBolt => new ReconBolt(G, at, dir, 0, 0, true),
            UtilKind.Haunt => new Haunt(G, at, dir, true),
            _ => null,
        };
        if (u != null) ((Node)G).GetTree().CreateTimer(0.6).Timeout += () => u.FreeVisual();
        if (Ability.IsRecon) ScanWave.Spawn(G, at, 0.5f, Ability.Color);
    }
}
