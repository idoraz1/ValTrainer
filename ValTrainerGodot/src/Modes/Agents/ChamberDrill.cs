using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Chamber Guns engine. Each round you hold a real defender spot on the chosen map (site level, line of sight to the choke;
/// Headhunter prefers mid range 9–28 m, Tour De Force 12–40 m) with a fresh magazine — 8 Headhunter bullets or 5 Tour De
/// Force rounds, like the real abilities (no reserve). 3–5 attackers (by tier) come through the choke one at a time and
/// shoot back with the tier's duel model: wide swings, jiggle peeks (Regular+), strafing ADAD peeks (Headhunter) and fast
/// crossings (Tour De Force). One that survives on the angle for a few seconds — or crosses — gets in. Scored on kills,
/// one-taps (the first bullet you fired at it killed it), shots per kill, spam taps (Headhunter shots under 0.35 s apart:
/// the spread blooms), deaths and attackers that got in.
/// </summary>
public sealed partial class ChamberMode
{
    bool Tdf => Variant == "tdf";
    string GunName => Tdf ? "Tour De Force" : "Headhunter";
    public override WeaponKind Weapon => Tdf ? WeaponKind.TourDeForce : WeaponKind.Headhunter;
    public override bool InfiniteAmmo => false;
    public override bool InfiniteReserve => false;
    public override string SimKind => "bot";
    public override float Duration => 90f;
    public override string? Subtitle => $"Chamber · {GunName}  —  {MapSpot.Map} {MapSpot.Name}";
    public override AimFocus? Focus => attacker != null && GodotObject.IsInstanceValid(attacker) && !attacker.Dead && G.LineOfSight(G.View.Eye, attacker.Head)
        ? new AimFocus(IdOf(attacker), attacker.Head, BotCharacter.HeadRadius) : null;

    enum Peek { Wide, Jiggle, Strafe, Cross }
    enum St { Wait, Move, Live, Between }
    St st = St.Between;
    Peek peek;
    float stateT, waitFor, seenAt = -1f, lastShotAt = -99f, roundEndIn = 1f;
    int attackersLeft, shotsAtThis, jiggles, strafeDir = 1;
    BotCharacter? attacker;
    BotBrain? brain;
    Vector3 start, swing, crossTo, toPlayer, side;
    readonly List<Vector3> waypoints = new();
    int wp;

    int rounds, roundsSurvived, spawned, escaped, oneTaps, spam, legOnly, unscoped, outOfAmmo;
    readonly List<float> ttkMs = new(), shotsPerKill = new();
    EnemySpot? holdSpot;

    int MagSize => Tdf ? Weapons.TourDeForce.Mag : Weapons.Headhunter.Mag;
    int AmmoLeft => G is GameSession { Gun: { } gun } ? gun.Ammo : MagSize;

    protected override void Setup()
    {
        if (DevAuto) GD.Print($"[chamber] {GunName} on {MapSpot.Map} {MapSpot.Name}");
        NewRound();
    }

    // ------------------------------------------------------------------ rounds

    void NewRound()
    {
        rounds++;
        // A defender spot at the gun's range with a clear view of the choke.
        var chokeEye = MapSpot.Choke + new Vector3(0, 1.5f, 0);
        var (lo, hi) = Tdf ? (12f, 40f) : (9f, 28f);
        var spots = MapSpot.Enemies
            .Where(e => e.Feet.Y - MapSpot.Choke.Y < 1.5f && MapSpot.LineOfSight(EyeOf(e), chokeEye))
            .ToArray();
        var ranged = spots.Where(e => e.Feet.DistanceTo(MapSpot.Choke) is var d && d >= lo && d <= hi).ToArray();
        var pool = ranged.Length > 0 ? ranged : spots.Length > 0 ? spots : MapSpot.Enemies;
        holdSpot = pool[Rng.Next(pool.Length)];
        SpawnFeet = holdSpot.Feet;
        var dir = MapSpot.Choke - SpawnFeet;
        SpawnYaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        G.Respawn(SpawnFeet, SpawnYaw);
        G.Mover.ResetStops();
        // A fresh magazine each round (the real guns have no reserve).
        if (G is GameSession { Gun: { } gun }) { gun.Ammo = gun.Def.Mag; gun.ReloadLeft = 0; gun.ResetRecoil(); }
        attackersLeft = Difficulty.Ti(Tier, 3, 3, 4, 4, 5);
        Despawn();
        st = St.Wait; stateT = 0; waitFor = R(0.8f, 1.6f);
        if (DevAuto) GD.Print($"[chamber] round {rounds}: holding {holdSpot.Name} at {holdSpot.Feet.DistanceTo(MapSpot.Choke):0} m from the choke");
    }

    void Despawn()
    {
        if (attacker != null && GodotObject.IsInstanceValid(attacker) && !attacker.Dead) G.Despawn(attacker);
        attacker = null;
        brain = null;
    }

    Vector3 Ground(Vector3 p) { p.Y = Collision.Ground(new Vector3(p.X, MapSpot.Choke.Y + 0.7f, p.Z), G.Solid); return p; }
    bool Free(Vector3 p) => !Collision.Overlaps(p, G.Solid);

    void SpawnAttacker()
    {
        toPlayer = new Vector3(SpawnFeet.X - MapSpot.Choke.X, 0, SpawnFeet.Z - MapSpot.Choke.Z).Normalized();
        side = new Vector3(-toPlayer.Z, 0, toPlayer.X);
        // Hidden start behind the choke (no line of sight to you), with a clear walk to the choke.
        start = Ground(MapSpot.Choke - toPlayer * 2.5f);
        bool found = false;
        foreach (var back in new[] { 1.5f, 2.5f, 3.5f, 4.5f })
        {
            foreach (var sd in new[] { 0f, 1.5f, -1.5f, 2.5f, -2.5f, 3.5f, -3.5f })
            {
                var p = Ground(MapSpot.Choke - toPlayer * back + side * sd);
                if (!Free(p) || G.LineOfSight(p + new Vector3(0, PlayerView.EyeHeight, 0), G.View.Eye)) continue;
                if (!G.LineOfSight(p + Vector3.Up, new Vector3(MapSpot.Choke.X, p.Y + 1, MapSpot.Choke.Z))) continue;
                start = p; found = true; break;
            }
            if (found) break;
        }
        // Peek type by gun and tier.
        double r = Rng.NextDouble();
        peek = Tdf
            ? (Tier >= 2 && r < 0.2 ? Peek.Jiggle : r < 0.55 ? Peek.Cross : Peek.Wide)
            : (Tier >= 2 && r < 0.25 ? Peek.Jiggle : Tier >= 1 && r < 0.6 ? Peek.Strafe : Peek.Wide);
        swing = Ground(MapSpot.Choke + toPlayer * 1.5f);
        for (int i = 0; i < 10; i++)
        {
            var c = Ground(MapSpot.Choke + toPlayer * R(1.5f, 3f) + side * R(-1.2f, 1.2f));
            if (Free(c)) { swing = c; break; }
        }
        waypoints.Clear();
        var choke = Ground(new Vector3(MapSpot.Choke.X, start.Y, MapSpot.Choke.Z));
        switch (peek)
        {
            case Peek.Cross:
            {
                // Run across the choke's opening, sideways to you, and out of sight on the far side.
                float s = Rng.Next(2) == 0 ? 1f : -1f;
                var a = Ground(MapSpot.Choke + toPlayer * 0.8f - side * s * 3.5f);
                var b = Ground(MapSpot.Choke + toPlayer * 0.8f + side * s * 3.5f);
                if (!Free(a) || !Free(b)) { peek = Peek.Wide; goto default; }
                waypoints.Add(choke); waypoints.Add(a); waypoints.Add(b);
                crossTo = b;
                break;
            }
            case Peek.Jiggle:
                jiggles = Rng.Next(1, 3);
                waypoints.Add(choke);
                break;
            default:
                waypoints.Add(choke); waypoints.Add(swing);
                break;
        }
        wp = 0;
        attacker = G.SpawnBot(start);
        attacker.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, -toPlayer.Z));
        brain = new BotBrain(attacker, Difficulty.Get(Tier).Bot, Rng)
        {
            HeldDir = (G.View.Eye - attacker.Head).Normalized(),
            PreAimed = Difficulty.FlashPreAim(Tier),
            ShoulderSight = Difficulty.ShoulderSight(Tier),
        };
        spawned++;
        attackersLeft--;
        shotsAtThis = 0;
        seenAt = -1f;
        st = St.Move; stateT = 0;
    }

    float Speed => peek == Peek.Cross ? Difficulty.T(Tier, 4.0f, 5.4f, 5.4f, 6.0f, 6.75f) : Difficulty.FlashEntrySpeed(Tier);

    /// <summary>Moves the attacker toward <paramref name="target"/>; true when it's there.</summary>
    bool MoveTo(Vector3 target, float speed, float dt)
    {
        var b = attacker!;
        var to = target - b.Feet; to.Y = 0;
        float step = speed * dt;
        if (to.Length() > step) { b.Velocity = to.Normalized() * speed; b.Feet += b.Velocity * dt; }
        else { b.Feet = new Vector3(target.X, b.Feet.Y, target.Z); b.Velocity = Vector3.Zero; }
        var p = b.Feet; p.Y = Collision.Ground(p + new Vector3(0, 0.3f, 0), G.Solid); b.Feet = p;
        return to.Length() <= step;
    }

    public override void Update(float dt)
    {
        stateT += dt;
        DevUpdate();
        switch (st)
        {
            case St.Between:
                roundEndIn -= dt;
                if (roundEndIn <= 0) NewRound();
                return;
            case St.Wait:
                if (stateT >= waitFor)
                {
                    if (attackersLeft <= 0) { EndRound(true); return; }
                    SpawnAttacker();
                }
                return;
        }
        var bot = attacker;
        if (bot == null || !GodotObject.IsInstanceValid(bot) || bot.Dead) { st = St.Wait; stateT = 0; waitFor = R(0.6f, 1.4f); return; }

        switch (peek)
        {
            case Peek.Wide:
                if (wp < waypoints.Count && MoveTo(waypoints[wp], Speed, dt)) wp++;
                break;
            case Peek.Strafe:
                if (wp < waypoints.Count) { if (MoveTo(waypoints[wp], Speed, dt)) { wp++; stateT = 0; } }
                else
                {
                    // ADAD on the angle: short strafes with brief stops (it only shoots when it stops, like a real player).
                    float cycle = 0.55f;
                    float ph = stateT % cycle;
                    if (ph < 0.35f)
                    {
                        var target = swing + side * strafeDir * 1.3f;
                        if (MoveTo(target, Mover.RunSpeed, dt)) strafeDir = -strafeDir;
                    }
                    else bot.Velocity = Vector3.Zero;
                }
                break;
            case Peek.Jiggle:
                // Out to the choke, a short look, back behind cover; then commit with a wide swing.
                if (jiggles > 0)
                {
                    bool outward = (int)(stateT / 0.45f) % 2 == 0;
                    if (outward ? MoveTo(waypoints[0] + toPlayer * 0.4f, Mover.RunSpeed, dt) : MoveTo(start, Mover.RunSpeed, dt))
                    {
                        if (!outward && stateT > 0.9f) { jiggles--; stateT = 0; }
                    }
                }
                else if (MoveTo(swing, Speed, dt)) { }
                break;
            case Peek.Cross:
                if (wp < waypoints.Count && MoveTo(waypoints[wp], Speed, dt)) wp++;
                if (wp >= waypoints.Count) { GotIn("crossed"); return; }
                break;
        }
        brain!.Enabled = peek != Peek.Cross;
        brain.Update(G, dt);
        if (st == St.Between) return; // the player died
        if (seenAt < 0 && G.LineOfSight(G.View.Eye, bot.Head)) { seenAt = Now; bot.SpawnTime = Now; SeenEvent(bot.Head); }
        // On the angle too long → it got onto the site.
        float onAngle = seenAt < 0 ? 0 : Now - seenAt;
        if (onAngle > Difficulty.T(Tier, 6f, 5f, 4.5f, 4f, 3.5f) || stateT > 14f) { GotIn("got in"); return; }
        if (AmmoLeft <= 0 && Now - lastShotAt > 0.6f) { outOfAmmo++; GotIn("out of bullets"); }
    }

    void GotIn(string why)
    {
        if (DevAuto) GD.Print($"[chamber] attacker ({peek}) {why}");
        escaped++;
        Score -= 40;
        Event("target_expired", attacker != null ? IdOf(attacker) : 0);
        G.Banner(why == "out of bullets" ? "Out of bullets" : $"Attacker {why}", UiTheme.Warn);
        Despawn();
        if (why == "out of bullets") { escaped += attackersLeft; attackersLeft = 0; EndRound(false); return; }
        st = St.Wait; stateT = 0; waitFor = R(0.6f, 1.4f);
    }

    void EndRound(bool survived)
    {
        if (survived) { roundsSurvived++; Event("round_won"); G.Banner("Round held", UiTheme.Good); }
        else Event("round_lost");
        Despawn();
        st = St.Between; roundEndIn = 1.4f;
    }

    public override void OnPlayerDied()
    {
        if (DevAuto) GD.Print($"[chamber] died to a {peek} peek");
        Deaths++;
        Score -= 100;
        escaped += attackersLeft + (attacker != null ? 1 : 0);
        attackersLeft = 0;
        G.Banner("YOU DIED", UiTheme.Accent);
        if (attacker != null) { var b = attacker; b.GetTree().CreateTimer(0.6).Timeout += () => { if (GodotObject.IsInstanceValid(b)) G.Despawn(b); }; }
        attacker = null; brain = null;
        Event("round_lost");
        st = St.Between; roundEndIn = 1.6f;
    }

    // ------------------------------------------------------------------ shooting

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        bool scoped = G is GameSession { Gun.Scoped: true };
        if (!Tdf && Now - lastShotAt < 0.35f) { spam++; Score -= 10; Event("spam_tap", (Now - lastShotAt) * 1000f); }
        if (Tdf && !scoped) unscoped++;
        lastShotAt = Now;
        if (attacker == null || st is St.Between or St.Wait) return float.PositiveInfinity;
        shotsAtThis++;
        var hit = ShootBots(new[] { attacker }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (DevAuto) GD.Print($"[chamber] shot {shotsAtThis} at a {peek} peek: {(hit == null ? "miss" : zone.ToString())}{(killed ? " KILL" : "")}, scoped={scoped}, ammo left {AmmoLeft}");
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (!killed && zone == HitZone.Legs && Tdf) legOnly++;
        if (killed)
        {
            float ttk = KillTimes[^1];
            ttkMs.Add(ttk);
            shotsPerKill.Add(shotsAtThis);
            Score += 100 + (int)Mathf.Max(0, (900 - ttk) / 5);
            if (shotsAtThis == 1) { oneTaps++; Score += 50; Event("one_tap", ttk); }
            attacker = null; brain = null; // the body plays its death and frees itself
            st = St.Wait; stateT = 0; waitFor = R(0.6f, 1.4f);
        }
        return dist;
    }

    // ------------------------------------------------------------------ HUD

    public override IEnumerable<string> HudLines()
    {
        if (rounds <= 1)
        {
            if (Tdf) { yield return "RMB: scope (2.5×) — any body hit kills."; yield return "0.9 shots a second: make the first one count."; }
            else { yield return "RMB: aim down sights (1.5×, perfect first shot)."; yield return "Space your taps — spamming blooms the spread to 3°."; }
        }
        if (st != St.Between) yield return $"{GunName}: {AmmoLeft} / {MagSize} this round · attackers left {attackersLeft + (attacker != null ? 1 : 0)}";
    }

    // ------------------------------------------------------------------ results

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Map", $"{MapSpot.Map} {MapSpot.Name}");
        yield return ("Gun", GunName);
        yield return ("Kills / attackers", $"{Kills} / {spawned}");
        if (escaped > 0) yield return ("Got past you", escaped.ToString());
        if (Kills > 0) yield return ("One-taps", $"{oneTaps} of {Kills}  ({100f * oneTaps / Kills:0}%)");
        if (shotsPerKill.Count > 0) yield return ("Shots per kill", $"{shotsPerKill.Average():0.0}");
        if (ttkMs.Count > 0) yield return ("Median time to kill", $"{Median(ttkMs):0} ms");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        if (!Tdf) yield return ("Spam taps (<0.35 s)", spam.ToString());
        else
        {
            if (unscoped > 0) yield return ("Unscoped shots", unscoped.ToString());
            if (legOnly > 0) yield return ("Leg hits, no kill", legOnly.ToString());
        }
        if (outOfAmmo > 0) yield return ("Ran out of bullets", outOfAmmo.ToString());
        yield return ("Rounds held / died", $"{roundsSurvived} / {Deaths}");
        foreach (var l in MovementLines()) yield return l;
    }

    /// <summary>Share of attackers stopped (killed vs got in or killed you), one tier lower under 40% one-taps with Headhunter.</summary>
    public override int Badge()
    {
        int n = Kills + escaped + Deaths;
        if (n == 0) return -1;
        int b = Difficulty.BadgeWinRate(Tier, (float)Kills / n, 0.5f);
        if (!Tdf && Kills > 0 && (float)oneTaps / Kills < 0.4f) b--;
        return Math.Max(-1, b);
    }

    // ------------------------------------------------------------------ dev

    /// <summary>DEV-ONLY (--dev --autoability): keep the gun aimed down sights / scoped so the simulated player plays the gun
    /// the way it's meant to be played (RMB via a synthetic input event).</summary>
    static readonly bool DevAuto = CmdLine.Dev && CmdLine.Has("--autoability");
    float devAdsAt;

    void DevUpdate()
    {
        if (!DevAuto || G is not GameSession { Gun: { } gun } || G.Player.Dead) return;
        if (gun.Scoped || gun.Reloading || gun.EquipLeft > 0 || Now - devAdsAt < 0.4f) return;
        devAdsAt = Now;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
    }
}
