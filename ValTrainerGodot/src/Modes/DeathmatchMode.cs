using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// VALORANT-style free-for-all Deathmatch against bots at the player's tier, on one of the true-scale map areas.
/// Rules (Riot support "FFA Deathmatch" + the official VALORANT wiki): first to the kill target or most kills when time
/// runs out (VALORANT: 40 kills / 9 min for 14 players — scaled here to 30 kills / 5 min for 3–6 bots), 1.5 s respawn at
/// a random spot free of enemies, brief spawn protection that ends when you fire, heavy armour every life
/// (100 HP + 50 shield), every kill reloads your gun, every death drops a health pack (10 s, full HP + armour), and
/// respawning briefly reveals where the enemies are. Bots roam the map on a nav grid (<see cref="BotNav"/>), hunt your
/// last known position (gunfire, damage, the respawn reveal), hold angles, and fight you with the tier's duel model
/// (<see cref="BotBrain"/>) — and each other.
/// <para>Warm-up / routines: <c>new DeathmatchMode(durationSeconds: 180)</c> or <c>DeathmatchMode.Create(180)</c>; the kill
/// target scales with the length (1 kill per 10 s) unless given. A session <c>DurationOverride</c> is honoured too.</para>
/// </summary>
public sealed partial class DeathmatchMode : MapMode
{
    public const float DefaultSeconds = 300f;
    public const float RespawnDelay = 1.5f, SpawnProtection = 1.5f, PackLifetime = 10f, RevealTime = 2f;

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
    public override string Name => "Deathmatch";
    public override string Description => "Free-for-all against bots at your tier on a VALORANT map. They hunt you and shoot back; respawn and keep fighting.";
    public override bool Timed => true;
    public override float Duration => duration;
    public override WeaponKind Weapon => weapon;
    public override bool Done => winner != null;
    public override string? Subtitle => $"{MapSpot.Map} — Deathmatch · first to {killTarget}";
    public int KillTarget => killTarget;

    // ---------------- state ----------------

    BotNav nav = null!;
    readonly List<Bot> bots = new();
    int[] spawnPool = Array.Empty<int>();
    string? winner;
    float playerRespawnAt = -1f, playerSpawnedAt, revealUntil;
    int lastShots, pathBudget, headKills, packsTaken, spawnDeaths, botSpawnDeaths, botBotKills, compromisedSpawns;
    float noiseAt = -99f;
    readonly List<(float T, string Text)> feed = new();
    static readonly bool DevLog = Main.I?.Dev ?? false;
    float logAt = 10f;
    bool started, lastSpawnClean = true, playerSpawnClean = true;
    int inWallFrames;

    Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);
    bool PlayerAlive => !G.Player.Dead;

    protected override void Setup()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        nav = BotNav.For($"{MapSpot.Key}:{G.Solid.Count}", G.Solid, MapSpot.StartFeet);
        if ((G as GameSession)?.DurationOverride is { } ov && killTargetArg == null) killTarget = KillsFor(ov);
        // Spawn spots: open floor (all 8 neighbours walkable).
        spawnPool = Enumerable.Range(0, nav.Count).Where(i => nav.Degree(i) == 8).ToArray();
        if (spawnPool.Length < 20) spawnPool = Enumerable.Range(0, nav.Count).ToArray();
        int n = BotCount();
        for (int i = 0; i < n; i++) bots.Add(new Bot(this, i));
        // First spawn: you at the map's attacker start, bots away from you and out of sight.
        foreach (var b in bots) SpawnLife(b, MapSpot.StartFeet);
        if (DevLog)
            GD.Print($"[dm] map {MapSpot.Key} tier {Tier}: nav {nav.Count} nodes ({nav.Area:0} m², built {nav.BuildMs:0} ms), " +
                     $"{spawnPool.Length} spawn spots, {n} bots, first to {killTarget}, setup {sw.Elapsed.TotalMilliseconds:0} ms");
    }

    int BotCount()
    {
        // ~250 m² of floor per bot: 4–6 on Ascent / Haven / Split; Bind's Hookah→B area is tiny (≈370 m²) and gets 3.
        int byArea = Math.Clamp((int)Mathf.Round(nav.Area / 250f), 3, 6);
        return Tier <= 1 ? Math.Min(byArea, 5) : byArea;
    }

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
        else if (Shots > lastShots)
        {
            if (Now < G.Player.ProtectedUntil) G.Player.ProtectedUntil = Now; // firing ends spawn protection
            if (Now - noiseAt > 0.5f) { noiseAt = Now; Noise(PlayerFeet, null, 35f); }
        }
        lastShots = Shots;

        foreach (var b in bots) b.Update(dt);
        UpdatePacks(dt);
        UpdateSight();

        var top = bots.OrderByDescending(b => b.Kills).FirstOrDefault();
        if (Kills >= killTarget) winner = "You";
        else if (top != null && top.Kills >= killTarget) winner = top.Name;

        if (DevLog && Now >= logAt) { logAt += 10f; DevReport(false); }
    }

    // ---------------- spawning ----------------

    /// <summary>Random open spot that no living enemy can see, ≥10 m from all of them (prefers 12–28 m: "close to the action").</summary>
    Vector3 PickSpawn(List<Vector3> enemyEyes, out float yaw)
    {
        Vector3 best = nav.Pos[spawnPool[Rng.Next(spawnPool.Length)]];
        float bestScore = float.MinValue;
        bool bestOk = false, lastSpawnOk;
        for (int k = 0; k < 90; k++)
        {
            var p = nav.Pos[spawnPool[Rng.Next(spawnPool.Length)]];
            var eye = p + new Vector3(0, PlayerView.EyeHeight, 0);
            float minD = 999f; bool seen = false;
            foreach (var e in enemyEyes)
            {
                minD = Mathf.Min(minD, e.DistanceTo(eye));
                if (!seen && (G.LineOfSight(e, eye) || G.LineOfSight(e, p + new Vector3(0, 1.0f, 0)))) seen = true;
            }
            lastSpawnOk = !seen && minD >= 10f;
            float score = (seen ? -1000f : 0f) + (minD < 10f ? -400f + minD * 10f : 0f) - Mathf.Abs(Mathf.Min(minD, 60f) - 20f) * 0.5f;
            if (score > bestScore) { bestScore = score; best = p; bestOk = lastSpawnOk; }
            if (k >= 24 && !seen && minD is >= 12f and <= 28f) break;
        }
        yaw = OpenYaw(best);
        if (!bestOk) compromisedSpawns++;
        lastSpawnClean = bestOk;
        return best;
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

    List<Vector3> EnemyEyes(Bot? except, Vector3? playerFeet)
    {
        var l = new List<Vector3>();
        if (playerFeet is { } pf) l.Add(pf + new Vector3(0, PlayerView.EyeHeight, 0));
        foreach (var b in bots) if (b != except && b.Alive) l.Add(b.Body!.Feet + new Vector3(0, PlayerView.EyeHeight, 0)); // (Head isn't posed until the first frame)
        return l;
    }

    void SpawnLife(Bot b, Vector3? playerFeet)
    {
        var p = PickSpawn(EnemyEyes(b, playerFeet), out float yaw);
        var body = G.SpawnBot(p);
        body.FacingYaw = yaw;
        var r = Mathf.DegToRad(yaw);
        var brain = new BotBrain(body, Difficulty.Get(Tier).Bot, Rng)
        {
            HeldDir = new Vector3(Mathf.Sin(r), 0, -Mathf.Cos(r)),
            ShoulderSight = Difficulty.ShoulderSight(Tier),
        };
        b.Begin(body, brain, playerFeet);
    }

    void RespawnPlayer()
    {
        var p = PickSpawn(EnemyEyes(null, null), out float yaw);
        G.Respawn(p, yaw);
        playerSpawnClean = lastSpawnClean;
        G.Mover.ResetStops();
        G.Player.ProtectedUntil = Now + SpawnProtection;
        if (G is GameSession s && s.Gun != null) { s.Gun.Ammo = s.Gun.Def.Mag; s.Gun.ReloadLeft = 0; }
        playerRespawnAt = -1f;
        playerSpawnedAt = Now;
        revealUntil = Now + RevealTime; // VALORANT: respawning pulses every enemy on your minimap
    }

    // ---------------- kills ----------------

    public override void OnPlayerDied()
    {
        Deaths++;
        Score = Math.Max(0, Score - 50);
        playerRespawnAt = Now + RespawnDelay;
        if (Now - playerSpawnedAt < 2.5f) spawnDeaths++;
        if (DevLog) GD.Print($"[dm] you died {Now:0.0}s after {Now - playerSpawnedAt:0.0}s alive (spawn {(playerSpawnClean ? "clean" : "NOT clean")}, shots {Shots})");
        // The killer is the bot that fired the last hit (damage comes from its head position).
        Bot? killer = null; float bd = float.MaxValue;
        foreach (var b in bots)
        {
            if (!b.Alive) continue;
            float d = b.Body!.Head.DistanceSquaredTo(G.Player.LastHitFrom);
            if (d < bd) { bd = d; killer = b; }
        }
        if (killer != null) { killer.Kills++; killer.ForgetPlayer(); }
        AddFeed($"{killer?.Name ?? "?"} killed You");
        G.Banner($"KILLED BY {killer?.Name.ToUpperInvariant() ?? "A BOT"}", UiTheme.Accent);
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
            if (G is GameSession s && s.Gun != null) { s.Gun.Ammo = s.Gun.Def.Mag; s.Gun.ReloadLeft = 0; }
            AddFeed($"You killed {victim.Name}{(headshot ? " · headshot" : "")}");
        }
        else
        {
            killer.Kills++;
            botBotKills++;
            AddFeed($"{killer.Name} killed {victim.Name}");
        }
        DropPack(feet);
    }

    void AddFeed(string text)
    {
        feed.Add((Now, text));
        if (feed.Count > 12) feed.RemoveAt(0);
    }

    /// <summary>Gunfire is heard: idle bots in range head for it (they hunt the player if it was you).</summary>
    void Noise(Vector3 at, Bot? source, float range)
    {
        foreach (var b in bots)
            if (b != source && b.Alive && b.Body!.Feet.DistanceTo(at) < range) b.Hear(at, source == null);
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
                if (AngleFromCrosshair(head) <= 60f) SeenEvent(head);
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
        yield return $"Kills {Kills} / {killTarget}   Deaths {Deaths}   K/D {KD:0.00}";
        var top = bots.OrderByDescending(b => b.Kills).FirstOrDefault();
        if (top != null) yield return top.Kills > Kills ? $"Leader: {top.Name} ({top.Kills})" : top.Kills == Kills ? $"Tied for the lead ({Kills})" : $"You lead by {Kills - top.Kills}";
        if (PlayerAlive && Now < G.Player.ProtectedUntil) yield return "Spawn protection";
        for (int i = feed.Count - 1, n = 0; i >= 0 && n < 4; i--)
            if (Now - feed[i].T < 5f) { yield return feed[i].Text; n++; }
    }

    public override string? Prompt => G.Player.Dead && playerRespawnAt > 0 ? $"Respawning in {Mathf.Max(0, playerRespawnAt - Now):0.0}s" : null;

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
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Kills / deaths", $"{Kills} / {Deaths}  (K/D {KD:0.00})");
        yield return ("Placement", winner == "You" ? $"1st of {bots.Count + 1} — first to {killTarget}" : $"{Ordinal(Placement)} of {bots.Count + 1}" + (winner != null ? $" ({winner} reached {killTarget})" : ""));
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%  ({headKills} headshot kills)");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (KillTimes.Count > 0) yield return ("Avg time to kill", $"{KillTimes.Average():0} ms from first sight");
        if (packsTaken > 0) yield return ("Health packs", packsTaken.ToString());
        foreach (var l in MovementLines()) yield return l;
    }

    static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    /// <summary>Kill share vs the tier's bots: 50% (K/D 1.0) matches the tier, 70%+ (K/D 2.3) earns the next one.</summary>
    public override int Badge()
    {
        if (DevLog && !finalLogged) { finalLogged = true; DevReport(true); } // MakeRecord asks once when the run ends
        return Kills + Deaths < 3 ? -1 : Difficulty.BadgeWinRate(Tier, (float)Kills / (Kills + Deaths));
    }
    bool finalLogged;

    // ---------------- dev ----------------

    void DevReport(bool final)
    {
        var bs = string.Join(" ", bots.Select(b => $"{b.Kills}/{b.Deaths}{(b.Alive ? "" : "x")}:{b.State}:{b.Moved:0}m"));
        GD.Print($"[dm] {(final ? "FINAL " : "")}t={Now:0} you {Kills}/{Deaths} K/D {KD:0.00} HS {(Hits > 0 ? 100f * HeadHits / Hits : 0):0}% acc {Accuracy * 100:0}% | bots {bs} | " +
                 $"paths {nav.Paths} fail {nav.Failed} avg {(nav.Paths > 0 ? nav.PathMs / (nav.Paths + nav.Failed) : 0):0.00} ms (smooth {nav.SmoothMs:0} ms total, max {nav.MaxPathMs:0.0}) | stuck {bots.Sum(b => b.Stuck)} inwall {inWallFrames} spawnDeaths you {spawnDeaths} bots {botSpawnDeaths} (spawns not clean {compromisedSpawns}) botbot {botBotKills} packs {packsTaken}");
    }
}
