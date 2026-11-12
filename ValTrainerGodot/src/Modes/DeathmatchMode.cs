using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// VALORANT-style free-for-all Deathmatch against bots at the player's tier, on one of the true-scale map areas.
/// Rules (Riot support "FFA Deathmatch" + the official VALORANT wiki): first to the kill target or most kills when time
/// runs out (VALORANT: 40 kills / 9 min for 14 players — scaled here to 30 kills / 5 min for 2–6 bots), 1.5 s respawn at
/// a spawn point out of every enemy's sight, back to a wall, facing the fight and away from where you died
/// (<see cref="DeathmatchSpawns"/>), 3 s spawn protection that ends when you fire or move off the spawn (bots hold fire
/// meanwhile), heavy armour every life (100 HP + 50 shield), every kill reloads your gun, every death drops a health pack
/// (10 s, full HP + armour), and respawning briefly reveals where the enemies are. Bots roam the map on a nav grid
/// (<see cref="BotNav"/>, wide lines off the walls), hunt your last known position (gunfire within earshot, damage, the
/// respawn reveal), peek corners, hold angles from sensible spots, and fight you — and each other — with the tier's duel
/// model (<see cref="BotBrain"/>) while strafing like players (<see cref="StrafePlanner"/>: counter-strafe, then shoot).
/// <para>Warm-up / routines: <c>new DeathmatchMode(durationSeconds: 180)</c> or <c>DeathmatchMode.Create(180)</c>; the kill
/// target scales with the length (1 kill per 10 s) unless given. A session <c>DurationOverride</c> is honoured too.</para>
/// </summary>
public sealed partial class DeathmatchMode : MapMode
{
    public const float DefaultSeconds = 300f;
    public const float RespawnDelay = 1.5f, PackLifetime = 10f, RevealTime = 2f;
    /// <summary>Spawn protection: up to 3 s, ended early by firing or by moving 1.5 m off the spawn (VALORANT launched with
    /// 8 s that "dissipates after they move or shoot"). Bots hold fire at a protected player and re-react when it ends.</summary>
    public const float SpawnProtection = 3f, ProtectMoveRadius = 1.5f;
    /// <summary>How far gunfire carries for the bots (m, walking distance around walls, or in plain sight).</summary>
    public const float HearRange = 25f;

    readonly float duration;
    readonly int? killTargetArg;
    readonly WeaponKind weapon;
    int killTarget;

    /// <param name="durationSeconds">Run length (default 5 min).</param>
    /// <param name="killTarget">Kills that end the match (0 = 1 per 10 s of run length, i.e. 30 for 5 min).</param>
    /// <param name="weapon">Your rifle (Vandal, Phantom or Sheriff).</param>
    public DeathmatchMode(float durationSeconds = DefaultSeconds, int killTarget = 0, WeaponKind weapon = WeaponKind.Vandal)
    {
        duration = Mathf.Max(10f, durationSeconds);
        killTargetArg = killTarget > 0 ? killTarget : null;
        this.weapon = weapon;
        this.killTarget = killTargetArg ?? KillsFor(duration);
    }

    /// <summary>Factory for routines (warm-up): a Deathmatch of <paramref name="seconds"/>.</summary>
    public static DeathmatchMode Create(float seconds, int killTarget = 0, WeaponKind weapon = WeaponKind.Vandal) => new(seconds, killTarget, weapon);

    static int KillsFor(float seconds) => Math.Max(5, (int)Mathf.Round(seconds / 10f));

    public override string Key => "deathmatch";
    public override string Name => L.N("Deathmatch");
    public override string Description => L.N("Free-for-all against bots at your tier on a VALORANT map. They hunt you and shoot back; respawn and keep fighting.");
    public override bool Timed => true;
    public override float Duration => duration;
    public override WeaponKind Weapon => weapon;
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.Any;
    public override bool GunOnRespawn => true; // VALORANT: a new loadout comes out on your next life
    public override bool Done => winner != null;
    public override string? Subtitle => L.F($"{MapSpot.Map} — Deathmatch · first to {killTarget}");
    public int KillTarget => killTarget;

    // ---------------- state ----------------

    BotNav nav = null!;
    DeathmatchSpawns spawns = null!;
    readonly List<Bot> bots = new();
    string? winner;
    float playerRespawnAt = -1f, playerSpawnedAt, revealUntil;
    int lastShots, pathBudget, headKills, packsTaken, spawnDeaths, botSpawnDeaths, botBotKills;
    float noiseAt = -99f;
    readonly List<(float T, string Text)> feed = new();
    static readonly bool DevLog = Main.I?.Dev ?? false;
    float logAt = 10f;
    bool started, playerProtectedLast;
    int inWallFrames;
    Vector3 playerSpawnFeet;
    Vector3? playerDeathSpot;
    /// <summary>The last few spawns (anyone's): the picker doesn't reuse them right away.</summary>
    readonly List<Vector3> recentSpawns = new();
    // dev: spawn quality per relaxation level (player / bots), deaths soon after spawning, fight movement
    readonly int[] playerLevels = new int[5], botLevels = new int[5];
    int playerSpawns, ringSeenSpawns;
    DeathmatchSpawns.Level playerSpawnLevel;
    float fightTime, fightMovingTime;
    readonly List<float> firstSightGaps = new();
    readonly int[] fightPatterns = new int[5];

    Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);
    bool PlayerAlive => !G.Player.Dead;
    /// <summary>The player is spawn protected: bots track but hold fire.</summary>
    internal bool PlayerProtected => PlayerAlive && Now < G.Player.ProtectedUntil;

    protected override void Setup()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string key = $"{MapSpot.Key}:{G.Solid.Count}";
        nav = BotNav.For(key, G.Solid, MapSpot.StartFeet);
        spawns = DeathmatchSpawns.For(key, MapSpot, G.Solid, nav);
        if ((G as GameSession)?.DurationOverride is { } ov && killTargetArg == null) killTarget = KillsFor(ov);
        int n = BotCount(nav.Area, Tier);
        for (int i = 0; i < n; i++) bots.Add(new Bot(this, i));
        // First spawn: you at a random spawn point (the session spawns you at SpawnFeet after Setup), then the bots out of
        // sight and ≥ 25 m walk from you (≥ 12 m from each other), with nobody revealed.
        var first = spawns.Pick(Array.Empty<Vector3>(), null, recentSpawns, G.LineOfSight, Rng);
        SpawnFeet = first.Feet; SpawnYaw = first.Yaw;
        playerSpawnFeet = first.Feet;
        Remember(first.Feet);
        playerSpawnedAt = Now;
        foreach (var b in bots) SpawnLife(b, first.Feet, firstSpawn: true);
        if (DevLog)
            GD.Print($"[dm] map {MapSpot.Key} tier {Tier}: nav {nav.Count} nodes ({nav.Area:0} m², built {nav.BuildMs:0} ms), " +
                     $"{spawns.Anchors.Length} spawn points ({spawns.OddSurfaceNodes} open nodes on walls/trim/crates left out), {n} bots, " +
                     $"first to {killTarget}, setup {sw.Elapsed.TotalMilliseconds:0} ms; you start at {Fmt(first.Feet)} yaw {first.Yaw:0}");
    }

    /// <summary>~250 m² of floor per bot: 4–6 on the big maps. Arenas under 500 m² (Bind's Hookah→B, ≈370 m²) get 2:
    /// with 3 bots there a bot could reach half of all spawns within 3 s (docs/research/deathmatch-1.18.md, S6). Riot made
    /// the same density call in patch 4.03 (14 → 12 players). Rookie / Regular get at most 5.</summary>
    internal static int BotCount(float area, int tier)
    {
        int byArea = Math.Clamp((int)Mathf.Round(area / 250f), area < 500f ? 2 : 3, 6);
        return tier <= 1 ? Math.Min(byArea, 5) : byArea;
    }

    static string Fmt(Vector3 p) => $"({p.X:0.0},{p.Y:0.0},{p.Z:0.0})";

    // ---------------- frame ----------------

    public override void Update(float dt)
    {
        pathBudget = 2;
        if (!started)
        {
            // First running frame (the session's Respawn after Setup resets the player state).
            started = true;
            G.Player.ProtectedUntil = Now + SpawnProtection;
            playerSpawnedAt = Now;
            revealUntil = Now + RevealTime;
        }
        if (G.Player.Dead)
        {
            if (Now >= playerRespawnAt) RespawnPlayer();
        }
        else
        {
            if (Shots > lastShots)
            {
                if (Now < G.Player.ProtectedUntil) G.Player.ProtectedUntil = Now; // firing ends spawn protection
                if (Now - noiseAt > 0.5f) { noiseAt = Now; Noise(PlayerFeet, null, HearRange); }
            }
            // ... and so does walking off the spawn.
            if (Now < G.Player.ProtectedUntil && Flat(PlayerFeet, playerSpawnFeet) > ProtectMoveRadius) G.Player.ProtectedUntil = Now;
        }
        lastShots = Shots;
        bool prot = PlayerProtected;
        if (playerProtectedLast && !prot)
            foreach (var b in bots) b.Reacquire(); // a bot that tracked you through the protection reacts afresh
        playerProtectedLast = prot;
        if (!eased && G.EaseLastSeconds > 0 && Now >= G.RunLength - G.EaseLastSeconds) EaseBots();

        foreach (var b in bots) b.Update(dt);
        UpdatePacks(dt);
        UpdateSight();

        var top = bots.OrderByDescending(b => b.Kills).FirstOrDefault();
        if (Kills >= killTarget) winner = "You";
        else if (top != null && top.Kills >= killTarget) winner = top.Name;

        if (DevLog && Now >= logAt) { logAt += 10f; DevReport(false); }
        if (BotCam != 0) UpdateBotCam();
    }

    // ---------------- finisher (Lock-In) ----------------

    bool eased;

    /// <summary>The bots' combat skill right now: the tier's, or one tier easier once the finisher started.</summary>
    internal BotSkill BotSkillNow => eased ? Difficulty.EasedBot(Tier) : Difficulty.Get(Tier).Bot;

    /// <summary>Last <see cref="IGame.EaseLastSeconds"/> of a routine's deathmatch: every bot (alive or respawning) fights
    /// one tier easier, so the player finishes the session on a run of wins. The Lock-In screen says so up front.</summary>
    void EaseBots()
    {
        eased = true;
        var skill = BotSkillNow;
        foreach (var b in bots) b.SetSkill(skill);
        float left = G.RunLength - Now;
        string when = Mathf.Abs(G.EaseLastSeconds - 60f) < 8f ? L.T("LAST MINUTE") : L.F($"LAST {Mathf.Round(G.EaseLastSeconds)} SECONDS");
        G.Banner(L.F($"{when} · FINISH STRONG"), UiTheme.Good);
        G.Sound("go", 0.6f);
        Log.Info($"[dm] finisher: bots eased to tier {Math.Max(0, Tier - 1)}{(Tier == 0 ? " (slower Rookie)" : "")} at {Now:0.0}s, {left:0} s left " +
                 $"(react {skill.ReactMs:0} ms, first-shot hit {skill.PHit1:0.00})");
    }

    // ---------------- spawning ----------------

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>A spawn for you or a bot (<see cref="DeathmatchSpawns.Pick"/>): enemies are taken where they will be in
    /// 0.75 s (a bot running at you counts as closer).</summary>
    DeathmatchSpawns.SpawnPick PickSpawn(Bot? forBot, Vector3? playerFeet, Vector3? deathSpot, float minPath = DeathmatchSpawns.SafePath, int strictCount = int.MaxValue)
    {
        var enemies = new List<Vector3>();
        if (playerFeet is { } pf) enemies.Add(Lead(pf, PlayerAlive ? G.Mover.Vel : Vector3.Zero));
        foreach (var b in bots) if (b != forBot && b.Alive) enemies.Add(Lead(b.Body!.Feet, b.Body.Velocity));
        var pick = spawns.Pick(enemies, deathSpot, recentSpawns, G.LineOfSight, Rng, minPath, strictCount);
        Remember(pick.Feet);
        (forBot == null ? playerLevels : botLevels)[(int)pick.Level]++;
        return pick;
    }

    /// <summary>Where a combatant will be in 0.75 s (if it can walk there in a straight line).</summary>
    Vector3 Lead(Vector3 feet, Vector3 vel)
    {
        var ahead = feet + new Vector3(vel.X, 0, vel.Z) * 0.75f;
        return nav.Straight(feet, ahead) ? ahead : feet;
    }

    void Remember(Vector3 spawn)
    {
        recentSpawns.Add(spawn);
        if (recentSpawns.Count > 4) recentSpawns.RemoveAt(0);
    }

    /// <summary>Facing toward the longest open sightline (and not into a wall) from a spot.</summary>
    float OpenYaw(Vector3 feet)
    {
        var eye = feet + new Vector3(0, PlayerView.EyeHeight, 0);
        float bestYaw = 0, bestD = -1;
        for (int i = 0; i < 16; i++)
        {
            float yaw = i * 22.5f, r = Mathf.DegToRad(yaw);
            var d = new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r));
            float dist = Mathf.Min(40f, Collision.FirstHit(eye, d, G.Solid)) + (float)Rng.NextDouble() * 3f;
            if (dist > bestD) { bestD = dist; bestYaw = yaw; }
        }
        return bestYaw;
    }

    /// <summary>A new life for a bot. First spawns keep ≥ 25 m walk from you (the match starts calm).</summary>
    void SpawnLife(Bot b, Vector3? playerFeet, bool firstSpawn = false)
    {
        var pick = firstSpawn
            ? PickSpawn(b, playerFeet, null, 25f, strictCount: 1) // 25 m walk from you, 12 m from the other bots
            : PickSpawn(b, playerFeet, b.DeathSpot);
        var body = G.SpawnBot(pick.Feet);
        body.FacingYaw = pick.Yaw;
        var brain = new BotBrain(body, BotSkillNow, Rng)
        {
            HeldDir = DeathmatchSpawns.YawDir(pick.Yaw),
            ShoulderSight = Difficulty.ShoulderSight(Tier),
            Weapon = Rng.NextDouble() < 0.55 ? Weapons.Vandal : Weapons.Phantom, // rolled per life (weapons-design-1.18.md §6)
        };
        b.Begin(body, brain, firstSpawn ? null : playerFeet); // the match starts calm: nobody is revealed
    }

    void RespawnPlayer()
    {
        var pick = PickSpawn(null, null, playerDeathSpot);
        G.Respawn(pick.Feet, pick.Yaw);
        playerSpawnFeet = pick.Feet;
        playerSpawnLevel = pick.Level;
        playerSpawns++;
        if (pick.RingSeen) ringSeenSpawns++;
        G.Mover.ResetStops();
        G.Player.ProtectedUntil = Now + SpawnProtection;
        if (G is GameSession s) s.ApplyNextLifeGun(); // a gun picked in the buy menu comes out now (G.Respawn refilled the old one)
        playerRespawnAt = -1f;
        playerSpawnedAt = Now;
        revealUntil = Now + RevealTime; // VALORANT: respawning pulses every enemy on your minimap
        if (DevLog)
            GD.Print($"[dm] spawn you at {Fmt(pick.Feet)} yaw {pick.Yaw:0}, nearest path {pick.NearestPath:0.0} m, ring-seen {(pick.RingSeen ? 1 : 0)}, " +
                     $"level {(int)pick.Level} ({pick.Level}), {(playerDeathSpot is { } ds ? $"{Flat(pick.Feet, ds):0} m from where you died" : "")}");
    }

    // ---------------- kills ----------------

    public override void OnPlayerDied()
    {
        Deaths++;
        Score = Math.Max(0, Score - 50);
        playerRespawnAt = Now + RespawnDelay;
        if (Now - playerSpawnedAt < 2.5f) spawnDeaths++;
        playerDeathSpot = PlayerFeet;
        if (DevLog) GD.Print($"[dm] you died {Now:0.0}s after {Now - playerSpawnedAt:0.0}s alive (spawn level {(int)playerSpawnLevel} {playerSpawnLevel}, shots {Shots})");
        // The killer is the bot that fired the last hit (damage comes from its head position).
        Bot? killer = null; float bd = float.MaxValue;
        foreach (var b in bots)
        {
            if (!b.Alive) continue;
            float d = b.Body!.Head.DistanceSquaredTo(G.Player.LastHitFrom);
            if (d < bd) { bd = d; killer = b; }
        }
        if (killer != null) { killer.Kills++; killer.ForgetPlayer(); }
        AddFeed(L.F($"{killer?.Name ?? "?"} killed You"));
        G.Banner(L.F($"KILLED BY {killer?.Name.ToUpperInvariant() ?? L.T("A BOT")}"), UiTheme.Accent);
        DropPack(PlayerFeet);
    }

    /// <summary>A bot died (to you if <paramref name="killer"/> is null).</summary>
    void OnBotKilled(Bot victim, Bot? killer, bool headshot)
    {
        var feet = victim.Body!.Feet;
        if (Now - victim.SpawnedAt < 2.5f) botSpawnDeaths++;
        victim.OnDeath(Now + RespawnDelay);
        if (killer == null)
        {
            Score += 100 + (headshot ? 50 : 0);
            if (headshot) headKills++;
            // VALORANT DM: every kill reloads your gun.
            G.RefillGun();
            AddFeed(headshot ? L.F($"You killed {victim.Name} · headshot") : L.F($"You killed {victim.Name}"));
        }
        else
        {
            killer.Kills++;
            botBotKills++;
            AddFeed(L.F($"{killer.Name} killed {victim.Name}"));
        }
        DropPack(feet);
    }

    void AddFeed(string text)
    {
        feed.Add((Now, text));
        if (feed.Count > 12) feed.RemoveAt(0);
    }

    /// <summary>Gunfire is heard by bots within <paramref name="range"/> m of walking distance (sound goes around walls,
    /// not through the map) or in plain sight within it; idle ones may head for it (they hunt the player if it was you).</summary>
    void Noise(Vector3 at, Bot? source, float range)
    {
        float[]? field = null;
        var ear = at + new Vector3(0, PlayerView.EyeHeight, 0);
        foreach (var b in bots)
        {
            if (b == source || !b.Alive || !b.Idle) continue;
            var f = b.Body!.Feet;
            if (f.DistanceTo(at) >= range) continue;
            field ??= nav.DistanceField(at, range);
            int n = nav.At(f, Collision.StepHeight);
            bool heard = (n >= 0 && field[n] <= range) || G.LineOfSight(ear, b.Body.Head);
            if (heard) b.Hear(at, source == null);
        }
    }

    // ---------------- bullets ----------------

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        if (!accurate) MovingShots++;
        if (G.Player.Dead) return float.PositiveInfinity;
        float maxDist = Wall(o, d);
        // Spawn-protected bots stop the bullet but take no damage.
        foreach (var b in bots)
            if (b.Alive && b.Protected)
            {
                var z = b.Body!.Raycast(o, d, out var t);
                if (z != HitZone.None && t < maxDist) maxDist = t;
            }
        var live = bots.Where(b => b.Alive && !b.Protected).Select(b => b.Body!).ToList();
        var hit = ShootBots(live, o, d, out var zone, out bool killed, out float dist, maxDist);
        if (hit == null) return maxDist < Wall(o, d) ? maxDist : float.PositiveInfinity;
        var bot = bots.First(b => b.Body == hit);
        bot.Hurt(null);
        if (killed) OnBotKilled(bot, null, zone == HitZone.Head);
        return dist;
    }

    // ---------------- what the player sees (aim coach) ----------------

    readonly List<Bot> visible = new();

    void UpdateSight()
    {
        visible.Clear();
        if (!PlayerAlive) return;
        var eye = G.View.Eye;
        foreach (var b in bots)
        {
            if (!b.Alive) continue;
            var head = b.Body!.Head;
            if (!G.LineOfSight(eye, head)) continue;
            visible.Add(b);
            // A new engagement: first sight after ≥2.5 s unseen. Crosshair placement is graded only for enemies in front.
            if (Now - b.SeenByPlayerAt > 2.5f)
            {
                b.Body.SpawnTime = Now; // time-to-kill counts from first sight
                SeenEvent(head); // graded only within SeenMaxDeg (60°) of the crosshair
            }
            b.SeenByPlayerAt = Now;
        }
    }

    public override AimFocus? Focus
    {
        get
        {
            AimFocus? best = null; float bestA = float.MaxValue;
            foreach (var b in visible)
            {
                if (!b.Alive) continue;
                float a = AngleFromCrosshair(b.Body!.Head);
                if (a < bestA) { bestA = a; best = new AimFocus(IdOf(b.Body), b.Body.Head, BotCharacter.HeadRadius); }
            }
            return best;
        }
    }

    // ---------------- HUD ----------------

    float KD => Deaths == 0 ? Kills : (float)Kills / Deaths;

    public override IEnumerable<string> HudLines()
    {
        yield return L.F($"Kills {Kills} / {killTarget}   Deaths {Deaths}   K/D {KD:0.00}");
        var top = bots.OrderByDescending(b => b.Kills).FirstOrDefault();
        if (top != null) yield return top.Kills > Kills ? L.F($"Leader: {top.Name} ({top.Kills})") : top.Kills == Kills ? L.F($"Tied for the lead ({Kills})") : L.F($"You lead by {Kills - top.Kills}");
        if (PlayerAlive && Now < G.Player.ProtectedUntil) yield return L.N("Spawn protection");
        if (eased) yield return L.N("Finish strong · bots are easier now");
        if (camBot >= 0 && camBot < bots.Count) yield return "[botcam] " + bots[camBot].DebugLine;
        for (int i = feed.Count - 1, n = 0; i >= 0 && n < 4; i--)
            if (Now - feed[i].T < 5f) { yield return feed[i].Text; n++; }
    }

    public override string? Prompt => G.Player.Dead && playerRespawnAt > 0 ? L.F($"Respawning in {Mathf.Max(0, playerRespawnAt - Now):0.0}s") : null;

    /// <summary>Respawn reveal: red markers around the crosshair toward every living enemy (fades over 2 s).</summary>
    public override void Draw2D(CanvasItem canvas, Vector2 size)
    {
        if (G == null || Now >= revealUntil || !PlayerAlive) return;
        float a = Mathf.Clamp((revealUntil - Now) / RevealTime, 0, 1);
        float k = size.Y / 1080f, rad = 150f * k;
        var c = size / 2;
        var pts = new Vector2[3];
        foreach (var b in bots)
        {
            if (!b.Alive) continue;
            var d = b.Body!.Feet - G.View.Eye;
            float rel = Mathf.Wrap(Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)) - G.View.ViewYaw, -180f, 180f);
            float ang = Mathf.DegToRad(rel) - Mathf.Pi / 2;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var perp = new Vector2(-dir.Y, dir.X);
            pts[0] = c + dir * (rad + 20 * k); pts[1] = c + dir * rad + perp * 9 * k; pts[2] = c + dir * rad - perp * 9 * k;
            canvas.DrawColoredPolygon(pts, new Color(UiTheme.Accent, 0.9f * a));
        }
    }

    // ---------------- results ----------------

    int Placement => 1 + bots.Count(b => b.Kills > Kills);

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Map"), $"{MapSpot.Map} {MapSpot.Name}");
        yield return (L.N("Kills / deaths"), $"{Kills} / {Deaths}  (K/D {KD:0.00})");
        yield return (L.N("Placement"), winner == "You" ? L.F($"1st of {bots.Count + 1} — first to {killTarget}")
            : winner != null ? L.F($"{Ordinal(Placement)} of {bots.Count + 1} ({winner} reached {killTarget})") : L.F($"{Ordinal(Placement)} of {bots.Count + 1}"));
        if (Hits > 0) yield return (L.N("Headshot %"), L.F($"{100f * HeadHits / Hits:0}%  ({headKills} headshot kills)"));
        yield return (L.N("Accuracy"), $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (KillTimes.Count > 0) yield return (L.N("Avg time to kill"), L.F($"{KillTimes.Average():0} ms from first sight"));
        if (packsTaken > 0) yield return (L.N("Health packs"), packsTaken.ToString());
        foreach (var l in MovementLines()) yield return l;
    }

    static string Ordinal(int n) => n switch { 1 => L.T("1st"), 2 => L.T("2nd"), 3 => L.T("3rd"), _ => L.F($"{n}th") };

    /// <summary>Kill share vs the tier's bots: 50% (K/D 1.0) matches the tier, 70%+ (K/D 2.3) earns the next one.</summary>
    public override int Badge()
    {
        if (DevLog && !finalLogged) { finalLogged = true; DevReport(true); } // MakeRecord asks once when the run ends
        return Kills + Deaths < 3 ? -1 : Difficulty.BadgeWinRate(Tier, (float)Kills / (Kills + Deaths));
    }
    bool finalLogged;

    // ---------------- dev ----------------

    /// <summary>Dev: "--dm-botcam N" follows bot N (1-based) with a chase camera behind and above it, and puts its state
    /// on the HUD (screenshots of how bots move and fight). "--dm-botcam fight" jumps to whichever bot is fighting.</summary>
    static readonly int BotCam = CmdLine.DevAfter("--dm-botcam") is { } bcs ? (bcs == "fight" ? -1 : int.TryParse(bcs, out var bc) ? bc : 0) : 0;
    Camera3D? botCam;
    int camBot = -1;

    void UpdateBotCam()
    {
        if (BotCam > 0) camBot = BotCam - 1;
        else if (camBot < 0 || camBot >= bots.Count || bots[camBot].State != Bot.St.Fight)
        {
            int f = bots.FindIndex(b => b.Alive && b.State == Bot.St.Fight);
            if (f >= 0) camBot = f; else if (camBot < 0 || !bots[camBot].Alive) camBot = bots.FindIndex(b => b.Alive);
        }
        if (camBot < 0 || camBot >= bots.Count || !bots[camBot].Alive) return;
        var body = bots[camBot].Body!;
        if (botCam == null)
        {
            botCam = new Camera3D { Fov = 85f, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.05f, Far = 400f };
            G.World.AddChild(botCam);
        }
        var fwd = DeathmatchSpawns.YawDir(body.FacingYaw);
        var pivot = body.Feet + new Vector3(0, 1.6f, 0);
        var back = -fwd * 3.4f + new Vector3(0, 1.3f, 0);
        float len = back.Length(), hit = Collision.FirstHit(pivot, back / len, G.Solid);
        botCam.GlobalPosition = pivot + back / len * Mathf.Clamp(hit - 0.3f, 0.5f, len);
        botCam.LookAt(body.Feet + new Vector3(0, 1.2f, 0) + fwd * 3f, Vector3.Up);
        if (!botCam.Current) botCam.MakeCurrent();
    }

    /// <summary>Dev log every 10 s. Spawn levels: how many rules each pick had to drop (0 = out of sight even one step
    /// out, ≥ 20 m walk from everyone, away from the death spot; see <see cref="DeathmatchSpawns.Level"/>). Fight movement:
    /// share of fight time the bots were moving, the body-to-wall gap when a bot first saw you, strafe patterns used.</summary>
    void DevReport(bool final)
    {
        var bs = string.Join(" ", bots.Select(b => $"{b.Kills}/{b.Deaths}{(b.Alive ? "" : "x")}:{b.State}:{b.Moved:0}m"));
        string Lv(int[] l) => string.Join("/", l);
        var pats = string.Join(" ", Enumerable.Range(0, 5).Where(i => fightPatterns[i] > 0).Select(i => $"{StrafePlanner.PatShort[i]} {fightPatterns[i]}"));
        GD.Print($"[dm] {(final ? "FINAL " : "")}t={Now:0} you {Kills}/{Deaths} K/D {KD:0.00} HS {(Hits > 0 ? 100f * HeadHits / Hits : 0):0}% acc {Accuracy * 100:0}% | bots {bs} | " +
                 $"paths {nav.Paths} fail {nav.Failed} avg {(nav.Paths > 0 ? nav.PathMs / (nav.Paths + nav.Failed) : 0):0.00} ms (smooth {nav.SmoothMs:0} ms total, max {nav.MaxPathMs:0.0}) | stuck {bots.Sum(b => b.Stuck)} inwall {inWallFrames} " +
                 $"spawnDeaths you {spawnDeaths} bots {botSpawnDeaths} | spawn levels 0/1/2/3/4 you {Lv(playerLevels)} bots {Lv(botLevels)}, your respawns seen one step out {ringSeenSpawns}/{playerSpawns} " +
                 $"| fights: moving {(fightTime > 0 ? 100f * fightMovingTime / fightTime : 0):0}% of {fightTime:0} s, first-sight wall gap {(firstSightGaps.Count > 0 ? firstSightGaps.Average() : 0):0.00} m " +
                 $"(<0.5 m {(firstSightGaps.Count > 0 ? 100f * firstSightGaps.Count(g => g < 0.5f) / firstSightGaps.Count : 0):0}% of {firstSightGaps.Count}), patterns {pats} | botbot {botBotKills} packs {packsTaken}");
    }
}
