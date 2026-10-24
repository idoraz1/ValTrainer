using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Sheriff One-Taps (community request: "a Sheriff mode with straight one-taps that scales to the game"). One agent at a
/// time at a close (8–15 m), mid (20–28 m) or long (35–45 m) range; standing in the open at every tier, shoulder- and
/// wide-peeking from pillars from Veteran up. The Sheriff is the real gun (<see cref="Weapons.Sheriff"/>): one bullet per
/// click at 4 rounds/s, 6-round magazine and a 2.25 s reload, and a shot fired before the recoil has recovered
/// (<see cref="WeaponDef.RecoveryTime"/>, read from the gun's heat like <see cref="LongTapsMode"/>) has spread and climb —
/// it counts as a spammed shot and a kill with it scores nothing.
/// <para>The damage lesson: within 30 m a head does 159 and kills a full-shield agent (150 HP); from 30 m it does 145, so a
/// long-range head leaves them on 5 HP and needs a second (recovered) tap. Body 55 / 50: three body shots.</para>
/// <para>Sheriff numbers (checked 2026-10-24, patch 13.x): official VALORANT wiki (wiki.playvalorant.com/en-us/Sheriff):
/// 4 rounds/s, 6 + 24, reload 2.25 s, equip 1 s, run speed 5.4 m/s, damage 0–30 m head 159 / body 55 / legs 46 and 30–50 m
/// 145 / 50 / 42, first-shot spread 0.25° (crouched 0.19°), max 2.75°, movement error +0.5° crouch-walk / 1.2° walk /
/// 3° run / 7° air (v3.0: walk 0.25 → 1.2, run 2.0 → 3.0; v9.10 crouch-walk 0.1 → 0.5). valorant-api.com
/// (/v1/weapons/e336c6b8-418d-9340-d77f-7a9e4cfe0702): the same fire rate, magazine, reload, equip and exact damage
/// 159.5 / 55 / 46.75 and 145 / 50 / 42.5, and adsStats = null — the Sheriff has no aim-down-sights zoom (alt fire does
/// nothing; Weapons.Sheriff Zoom = 1). Riot publishes no Sheriff recovery time: the 0.4 s in Weapons.cs is an estimate.</para>
/// </summary>
public sealed class SheriffMode : BotMode
{
    public override string Key => "sheriff";
    public override string Name => "Sheriff One-Taps";
    public override string Description => "One click, one head with the Sheriff at 8–45 m. Past 30 m a head does 145 and won't kill a full shield: tap again. Spamming is punished.";
    public override WeaponKind Weapon => WeaponKind.Sheriff;
    /// <summary>The simulated player pursues the agent (peekers move) and taps once the head is under the crosshair.</summary>
    public override string SimKind => "bot";
    /// <summary>Only a visible head is a target: a hidden peeker (or one showing just a shoulder) is nothing to aim at.</summary>
    public override AimFocus? Focus => Alive(bot) && headVisible ? new AimFocus(IdOf(bot!), bot!.Head, BotCharacter.HeadRadius) : null;

    // ---------------- tiers: Rookie, Regular, Veteran, Elite, Pro ----------------

    /// <summary>Share of close (8–15 m) and long (35–45 m) agents; the rest are mid (20–28 m).</summary>
    static float CloseShare(int t) => Difficulty.T(t, 0.60f, 0.45f, 0.35f, 0.30f, 0.25f);
    static float LongShare(int t) => Difficulty.T(t, 0f, 0.15f, 0.25f, 0.30f, 0.35f);
    /// <summary>Share of agents that peek from a pillar (the rest stand in the open).</summary>
    static float PeekShare(int t) => Difficulty.T(t, 0f, 0f, 0.35f, 0.55f, 0.70f);
    /// <summary>Peekers that first show only a shoulder (bait: the head stays behind cover) before the real peek.</summary>
    static float ShoulderChance(int t) => Difficulty.T(t, 0f, 0f, 0f, 0.35f, 0.50f);
    /// <summary>Open agents spawn within ± this yaw (deg) of straight ahead.</summary>
    static float SpreadYaw(int t) => Difficulty.T(t, 16f, 20f, 24f, 28f, 32f);
    /// <summary>How long an open agent stays after you first see it (s): its "reaction" — after that it would have shot you.
    /// Long-range agents get <see cref="LongExtra"/> on top (they need a second tap after the recovery).</summary>
    static float Window(int t) => Difficulty.T(t, 3.0f, 2.3f, 1.7f, 1.3f, 1.0f);
    const float LongExtra = 0.9f;
    static (float Min, float Max) PeekHold(int t) => t switch
    { 0 or 1 => (1.5f, 2.0f), 2 => (0.9f, 1.3f), 3 => (0.65f, 1.0f), _ => (0.45f, 0.75f) };
    static float PeekSpeed(int t) => Difficulty.T(t, 4.0f, 4.0f, 5.4f, 5.4f, 5.4f);
    /// <summary>Median time to kill per badge (ms, lower is better) of the close / mid agents (the one-tap range).</summary>
    static readonly float[] BadgeTtkMs = { 1250, 1050, 880, 740, 620 };
    /// <summary>A shot counts as "recovered" when the recoil has less than this left to recover (s).</summary>
    const float ResetSlack = 0.05f;

    enum Band { Close, Mid, Long }
    static (float Min, float Max) BandRange(Band b) => b switch { Band.Close => (8f, 15f), Band.Mid => (20f, 28f), _ => (35f, 45f) };
    static string BandName(Band b) => b switch { Band.Close => "CLOSE", Band.Mid => "MID", _ => "LONG" };

    // ---------------- cover: pillars at every range ----------------

    static readonly Vector3[] Pillars =
    {
        new(-6, 0, -10), new(6, 0, -12),                       // close
        new(-10, 0, -23), new(2, 0, -25), new(12, 0, -27),     // mid
        new(-13, 0, -39), new(-3, 0, -42), new(9, 0, -40),     // long
    };
    static readonly Vector3 PillarHalf = new(0.7f, 2.6f, 0.5f);
    static Box PillarBox(Vector3 c) => new(c - new Vector3(PillarHalf.X, 0, PillarHalf.Z), c + PillarHalf);
    public override IEnumerable<Box> ExtraSolids => Pillars.Select(PillarBox);

    // ---------------- state ----------------

    enum PSt { Wait, Out, Hold, Back }

    BotCharacter? bot;
    Band band;
    bool peeker, shoulderFirst, seen, headVisible;
    PSt pst;
    Vector3 hidePos, peekDir;
    float peekOut, holdFor, stT, respawnIn = 0.7f, seenAt, spawnedAt, dist;
    int repeeks;
    // this agent's bullets
    int botShots;
    bool firstWasHead;
    // totals
    int oneTaps, nearKills, nearResolved, longKills, longResolved, longClean, escaped, spammed, wastedKills, bodyHits, legHits;
    // recovery (copied from Long-range Taps: the gun's heat → time left to recover)
    float gunHeat, lastBulletAt = -99f, leftAtShot = 0.4f, readyAt = -99f, tooFastUntil = -99f;

    static bool Alive(BotCharacter? b) => b != null && GodotObject.IsInstanceValid(b) && !b.Dead;
    float Recovery => (G.Weapon ?? Weapons.Sheriff).RecoveryTime;

    Band PickBand()
    {
        double r = Rng.NextDouble();
        return r < CloseShare(Tier) ? Band.Close : r < CloseShare(Tier) + LongShare(Tier) ? Band.Long : Band.Mid;
    }

    void Spawn()
    {
        band = PickBand();
        seen = false; headVisible = false; botShots = 0; firstWasHead = false; repeeks = 0; spawnedAt = Now;
        peeker = Rng.NextDouble() < PeekShare(Tier) && SpawnPeeker();
        if (!peeker) SpawnOpen();
    }

    void SpawnOpen()
    {
        var (dMin, dMax) = BandRange(band);
        float spread = SpreadYaw(Tier);
        var feet = Vector3.Zero;
        for (int i = 0; i < 40; i++)
        {
            float d = R(dMin, dMax), yaw = R(-spread, spread);
            feet = new Vector3(d * Mathf.Sin(Mathf.DegToRad(yaw)), 0, -d * Mathf.Cos(Mathf.DegToRad(yaw)));
            var head = feet + new Vector3(0, PlayerView.EyeHeight, 0);
            bool open = G.LineOfSight(G.View.Eye, head) && G.LineOfSight(G.View.Eye, feet + new Vector3(0, 1.0f, 0));
            if (open && AngleFromCrosshair(head) > 4f) break; // never spawn right under the crosshair
        }
        bot = G.SpawnBot(feet);
        bot.FacingYaw = AimPlusMath.FaceOrigin(feet);
        bot.Velocity = Vector3.Zero;
        dist = feet.Length();
    }

    /// <summary>A pillar in the chosen range band; the agent waits behind it. False = none fits (the agent stands in the open).</summary>
    bool SpawnPeeker()
    {
        var (dMin, dMax) = BandRange(band);
        var options = Pillars.Where(c => c.Length() >= dMin - 1.5f && c.Length() <= dMax + 1.5f).ToArray();
        if (options.Length == 0) return false;
        var c = options[Rng.Next(options.Length)];
        var los = new Vector3(c.X, 0, c.Z).Normalized();
        hidePos = c + los * (PillarHalf.Z + 0.7f);
        float side = Rng.Next(2) == 0 ? -1f : 1f;
        peekDir = new Vector3(-los.Z, 0, los.X) * side;
        // Lateral offset (m) at which the head centre leaves the pillar's shadow, as seen from the player.
        float shadow = Shadow(c, hidePos.Length());
        shoulderFirst = Rng.NextDouble() < ShoulderChance(Tier);
        float wide = shadow + R(0.55f, 1.15f);
        peekOut = shoulderFirst ? shadow - 0.1f : wide; // a shoulder peek keeps the head centre just behind the edge
        wideOut = wide;
        bot = G.SpawnBot(hidePos);
        bot.FacingYaw = AimPlusMath.FaceOrigin(hidePos);
        bot.Velocity = Vector3.Zero;
        pst = PSt.Wait;
        stT = -R(0.25f, 0.8f);
        dist = hidePos.Length();
        return true;
    }
    float wideOut;

    /// <summary>Half-width of the pillar's shadow (m) at distance <paramref name="atDist"/> behind it, seen from the origin.</summary>
    static float Shadow(Vector3 c, float atDist)
    {
        var dir = new Vector3(c.X, 0, c.Z).Normalized();
        var perp = new Vector3(-dir.Z, 0, dir.X);
        float maxTan = 0;
        foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var corner = c + new Vector3(sx * PillarHalf.X, 0, sz * PillarHalf.Z);
                maxTan = Mathf.Max(maxTan, Mathf.Abs(corner.Dot(perp)) / Mathf.Max(0.1f, corner.Dot(dir)));
            }
        return maxTan * atDist;
    }

    public override void Update(float dt)
    {
        gunHeat = G is GameSession gs && gs.Gun != null ? gs.Gun.Heat : 0f;
        if (readyAt < lastBulletAt && Recovery * Mathf.Sqrt(gunHeat) <= ResetSlack) readyAt = Now;
        if (!Alive(bot))
        {
            headVisible = false;
            respawnIn -= dt;
            if (respawnIn <= 0) Spawn();
            return;
        }
        var b = bot!;
        if (peeker) UpdatePeek(dt, b);
        else
        {
            b.Velocity = Vector3.Zero;
            if (seen && Now - seenAt > Window(Tier) + (band == Band.Long ? LongExtra : 0f)) { Leave(); return; }
            if (!seen && Now - spawnedAt > 3f) { Leave(); return; } // no clear spot found (head never in view): next one
        }
        if (bot == null) return; // left

        headVisible = G.LineOfSight(G.View.Eye, b.Head);
        if (!seen && headVisible)
        {
            seen = true;
            seenAt = Now;
            b.SpawnTime = Now; // time to kill counts from first sight of the head
            var (err, pe) = AimPlusMath.SeenError(G.View, b.Head);
            Event(AimPlusTiers.SeenEvent, err, pe);
        }
    }

    void UpdatePeek(float dt, BotCharacter b)
    {
        stT += dt;
        float speed = PeekSpeed(Tier), travel = peekOut / speed;
        switch (pst)
        {
            case PSt.Wait:
                b.Velocity = Vector3.Zero;
                if (stT >= 0)
                {
                    pst = PSt.Out; stT = 0;
                    var (h0, h1) = PeekHold(Tier);
                    holdFor = shoulderFirst ? R(0.15f, 0.3f) : R(h0, h1) + (band == Band.Long ? LongExtra : 0f);
                }
                break;
            case PSt.Out:
                MoveTo(b, hidePos + peekDir * Mathf.Min(peekOut, stT * speed), dt);
                if (stT >= travel) { pst = PSt.Hold; stT = 0; }
                break;
            case PSt.Hold:
                b.Velocity = Vector3.Zero;
                if (stT >= holdFor) { pst = PSt.Back; stT = 0; }
                break;
            case PSt.Back:
                MoveTo(b, hidePos + peekDir * Mathf.Max(0, peekOut - stT * speed), dt);
                if (stT >= travel)
                {
                    if (shoulderFirst) { shoulderFirst = false; peekOut = wideOut; pst = PSt.Wait; stT = -R(0.15f, 0.45f); break; }
                    if (repeeks < 1 && Rng.NextDouble() < 0.4) { repeeks++; pst = PSt.Wait; stT = -R(0.3f, 0.7f); break; }
                    Leave();
                }
                break;
        }
    }

    static void MoveTo(BotCharacter b, Vector3 to, float dt)
    {
        b.Velocity = dt > 0 ? (to - b.Feet) / dt : Vector3.Zero;
        b.Feet = to;
    }

    void Leave()
    {
        if (seen)
        {
            escaped++;
            if (peeker) escapedPeekers++;
            if (botShots == 0) escapedUnshot++;
            if (band == Band.Long) longResolved++; else nearResolved++;
            Score -= 40;
            G.Sound("fail", 0.5f);
            Event("target_expired", IdOf(bot!));
        }
        G.Despawn(bot!);
        bot = null;
        headVisible = false;
        respawnIn = R(0.5f, 1.1f);
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        // Recoil state when this shot left: the gun's heat as of this frame (Long-range Taps does the same).
        float heatPre = Now == lastBulletAt ? 99f : gunHeat;
        lastBulletAt = Now;
        float left = Recovery * Mathf.Sqrt(Mathf.Max(0, heatPre));
        bool tooFast = left > ResetSlack;
        if (tooFast) { spammed++; tooFastUntil = Now + 1.4f; Score -= 15; Event("tap_too_fast", left * 1000f); }
        float post = G is GameSession gs && gs.Gun != null ? gs.Gun.Heat : 1f;
        leftAtShot = Mathf.Max(0.05f, Recovery * Mathf.Sqrt(post));

        if (!Alive(bot)) { Score -= 15; return float.PositiveInfinity; }
        botShots++;
        var hit = ShootBots(new[] { bot! }, o, d, out var zone, out bool killed, out float hd, Wall(o, d));
        if (botShots == 1) firstWasHead = hit != null && zone == HitZone.Head;
        if (hit == null) { Score -= 15; return float.PositiveInfinity; }
        if (zone == HitZone.Body) bodyHits++;
        else if (zone == HitZone.Legs) legHits++;

        if (!killed)
        {
            if (zone == HitZone.Head)
            {
                if (!tooFast) Score += 20;
                if (hd > 30f) G.Banner($"{Mathf.Round((G.Weapon ?? Weapons.Sheriff).Damage(0, hd)):0} — 30 M+ HEAD, TAP AGAIN", UiTheme.Warn);
            }
            else Score -= zone == HitZone.Body ? 5 : 10;
            return hd;
        }

        bool isLong = band == Band.Long;
        if (isLong) { longKills++; longResolved++; } else { nearKills++; nearResolved++; nearTtk.Add(KillTimes[^1]); }
        bool oneTap = botShots == 1 && zone == HitZone.Head && !tooFast;
        bool clean2 = isLong && botShots == 2 && firstWasHead && !tooFast;
        if (oneTap) oneTaps++;
        if (clean2) longClean++;
        if (tooFast)
        {
            wastedKills++;
            G.Banner("SPAMMED — NO POINTS", UiTheme.Warn);
        }
        else if (oneTap)
        {
            Score += 150 + (int)Mathf.Max(0, (1400 - KillTimes[^1]) / 8);
            G.Banner("ONE TAP", UiTheme.Good);
        }
        else if (clean2) Score += 130 + (int)Mathf.Max(0, (2000 - KillTimes[^1]) / 10);
        else Score += 50 + (int)Mathf.Max(0, (1400 - KillTimes[^1]) / 16);
        bot = null; // death animation, then it frees itself
        headVisible = false;
        respawnIn = R(0.6f, 1.1f);
        return hd;
    }

    int Resolved => nearResolved + longResolved;
    readonly List<float> nearTtk = new();
    int escapedPeekers, escapedUnshot;
    bool reported;
    float OneTapRate => nearResolved == 0 ? 0f : (float)oneTaps / nearResolved;
    float SpamShare => Shots == 0 ? 0f : (float)spammed / Shots;

    public override string? Prompt =>
        Now < tooFastUntil ? $"Too fast — let the Sheriff recover (≈ {Recovery * 1000:0} ms between taps)"
        : Shots == 0 ? "One click, one head. Past 30 m a head does 145: tap again once the gun recovers" : null;

    public override IEnumerable<string> HudLines()
    {
        if (Alive(bot) && seen)
            yield return band == Band.Long ? $"{BandName(band)} {dist:0} m · head 145: not a one-tap" : $"{BandName(band)} {dist:0} m · head 159: one-tap";
        if (Shots > 0) yield return $"One-taps {oneTaps} · spammed {spammed}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("One-tap kills", nearResolved == 0 ? "—" : $"{OneTapRate * 100:0}%  ({oneTaps}/{nearResolved} close/mid)");
        yield return ("Long-range kills", longResolved == 0 ? "—" : $"{longKills}/{longResolved}  ·  {longClean} head + tap");
        yield return ("Headshot %", Hits == 0 ? "—" : $"{100f * HeadHits / Hits:0}%  ({bodyHits + legHits} body/legs)");
        yield return ("Avg time to kill", KillTimes.Count > 0 ? $"{KillTimes.Average():0} ms" : "—");
        yield return ("Spammed shots", $"{spammed} of {Shots}" + (wastedKills > 0 ? $"  ({wastedKills} kills wasted)" : ""));
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        yield return ("Escaped", $"{escaped} of {Resolved}");
    }

    /// <summary>Median time from first sight of the head to the kill of the close / mid agents (≥ 5 of them; their kill
    /// rate as in <see cref="Difficulty.BadgeTtk"/>; long range is a bonus lesson, not graded), like the kill-time drills; fewer
    /// than half the close / mid agents one-tapped or a quarter of the shots spammed costs a badge, half spammed all of it.</summary>
    public override int Badge()
    {
        if (Main.I.Dev && G is { Demo: false } && !reported)
        {
            // dev runs (--simaim calibration) print the results once: the session asks for the badge when the run ends
            reported = true;
            GD.Print($"[drill] {Key} tier {Tier} ({Difficulty.Get(Tier).Name}), score {Score}: " +
                     string.Join(" | ", ResultLines().Select(l => $"{l.Label}: {l.Value}")) + $" | escaped: {escapedPeekers} peekers, {escapedUnshot} never shot at");
        }
        if (nearResolved < 5 || nearTtk.Count == 0) return -1;
        int b = Difficulty.BadgeTtk(Tier, (float)nearKills / nearResolved, Median(nearTtk), BadgeTtkMs);
        if (b < 0 || SpamShare > 0.5f) return -1;
        if (OneTapRate < 0.5f) b--;
        if (SpamShare > 0.25f) b--;
        return Math.Max(-1, b);
    }

    // ---------------- recovery ring around the crosshair (as in Long-range Taps) ----------------

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (lastBulletAt < 0) return;
        bool ready = readyAt >= lastBulletAt;
        float alpha = ready ? Mathf.Clamp(1f - (Now - readyAt) / 0.35f, 0f, 1f) : 0.9f;
        if (alpha <= 0.01f) return;
        float k = size.Y / 1080f, rad = 22f * k;
        var centre = size / 2;
        float left = Recovery * Mathf.Sqrt(Mathf.Max(0, gunHeat));
        float prog = ready ? 1f : Mathf.Clamp(1f - left / leftAtShot, 0f, 1f);
        c.DrawArc(centre, rad, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.35f * alpha), 3.5f * k, true);
        var col = ready ? UiTheme.Good : UiTheme.Warn;
        if (prog > 0.01f) c.DrawArc(centre, rad, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * prog, 48, new Color(col, alpha), 2.2f * k, true);
    }
}
