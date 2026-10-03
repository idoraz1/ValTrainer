using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Long-range Taps: one agent at a time at 30–50 m (head ≈ 0.2° radius) with the Vandal. Standing agents at Rookie;
/// strafers and peekers from cover join at higher tiers. The first bullet is the accurate one: a tap fired before the
/// recoil has reset (the real Vandal recovery, <see cref="WeaponDef.RecoveryTime"/>, read from the gun's heat) is
/// wasted — it scores nothing even if it lands — and a ring around the crosshair shows the reset. Scores per head,
/// first-bullet headshots and tap discipline (% of taps fired with the recoil reset).
/// </summary>
public sealed class LongTapsMode : BotMode
{
    public override string Key => "longtaps";
    public override string Name => "Long-range Taps";
    public override string Description => "Tiny heads at 30–50 m with the Vandal: make the first bullet count, then let the recoil reset before the next tap. Higher tiers add strafers and peekers.";
    public override string Category => "Valorant";
    /// <summary>The simulated player clicks precisely on still heads ("static") but pursues moving ones ("bot");
    /// Elite and Pro are mostly strafers and peekers.</summary>
    public override string SimKind => Tier >= 3 ? "bot" : "static";
    public override AimFocus? Focus => Alive(bot) ? new AimFocus(IdOf(bot!), bot!.Head, BotCharacter.HeadRadius) : null;

    // Cover for peekers (also in the way at every tier, like a real long angle).
    static readonly Vector3[] Crates = { new(-12, 0, -32), new(9, 0, -35), new(-4, 0, -39), new(15, 0, -42), new(-17, 0, -46), new(4, 0, -48) };
    static readonly Vector3 CrateHalf = new(0.9f, 2.4f, 0.6f);
    static Box CrateBox(Vector3 c) => new(c - new Vector3(CrateHalf.X, 0, CrateHalf.Z), c + CrateHalf);
    public override IEnumerable<Box> ExtraSolids => Crates.Select(CrateBox);

    enum Kind { Stand, Strafe, Peek }
    enum PSt { Wait, Out, Hold, Back }

    BotCharacter? bot;
    Kind kind;
    PSt pst;
    Vector3 hidePos, peekDir;
    float peekDist, holdFor, stT, x0, targetVx, turnIn, respawnIn = 0.6f, seenAt = -1, dist;
    int peeks;
    bool seen, firstShotDone;
    int escaped, headKills, wastedKills, firstShots, firstHeads;
    // tap discipline
    float gunHeat, lastBulletAt = -99f, leftAtShot = 0.375f, readyAt = -99f, tooFastUntil = -99f;
    int taps, disciplined;

    static bool Alive(BotCharacter? b) => b != null && GodotObject.IsInstanceValid(b) && !b.Dead;
    float Recovery => (G.Weapon ?? Weapons.Vandal).RecoveryTime;

    void Spawn()
    {
        float peekP = AimPlusTiers.LongPeekShare(Tier), strafeP = AimPlusTiers.LongStrafeShare(Tier);
        double r = Rng.NextDouble();
        kind = r < peekP ? Kind.Peek : r < peekP + strafeP ? Kind.Strafe : Kind.Stand;
        seen = false; firstShotDone = false; seenAt = -1; peeks = 0;
        if (kind == Kind.Peek) SpawnPeeker();
        else SpawnOpen();
    }

    void SpawnOpen()
    {
        var (dMin, dMax) = AimPlusTiers.LongDistance(Tier);
        float spread = AimPlusTiers.LongSpread(Tier);
        var feet = Vector3.Zero;
        for (int i = 0; i < 40; i++)
        {
            float d = R(dMin, dMax), yaw = R(-spread, spread);
            feet = new Vector3(d * Mathf.Sin(Mathf.DegToRad(yaw)), 0, -d * Mathf.Cos(Mathf.DegToRad(yaw)));
            var head = feet + new Vector3(0, PlayerView.EyeHeight, 0);
            bool open = G.LineOfSight(G.View.Eye, head) && G.LineOfSight(G.View.Eye, feet + new Vector3(0, 1.0f, 0));
            // strafers need room to move without walking behind a crate
            if (open && kind == Kind.Strafe)
                open = G.LineOfSight(G.View.Eye, head + new Vector3(3.5f, 0, 0)) && G.LineOfSight(G.View.Eye, head - new Vector3(3.5f, 0, 0));
            if (open && AngleFromCrosshair(head) > 4f) break;
        }
        bot = G.SpawnBot(feet);
        bot.FacingYaw = AimPlusMath.FaceOrigin(feet);
        x0 = feet.X;
        turnIn = 0;
        targetVx = 0;
        dist = feet.Length();
    }

    void SpawnPeeker()
    {
        var (dMin, dMax) = AimPlusTiers.LongDistance(Tier);
        var options = Crates.Where(c => c.Length() >= dMin - 2 && c.Length() <= dMax + 2).ToArray();
        var c = options.Length > 0 ? options[Rng.Next(options.Length)] : Crates[Rng.Next(Crates.Length)];
        var los = new Vector3(c.X, 0, c.Z).Normalized();
        hidePos = c + los * (CrateHalf.Z + 0.9f);
        float side = Rng.Next(2) == 0 ? -1f : 1f;
        peekDir = new Vector3(-los.Z, 0, los.X).Normalized() * side;
        peekDist = R(1.5f, 2.1f);
        bot = G.SpawnBot(hidePos);
        bot.FacingYaw = AimPlusMath.FaceOrigin(hidePos);
        pst = PSt.Wait;
        stT = -R(0.2f, 0.7f);
        dist = hidePos.Length();
    }

    public override void Update(float dt)
    {
        gunHeat = G is GameSession gs && gs.Gun != null ? gs.Gun.Heat : 0f;
        if (readyAt < lastBulletAt && Recovery * Mathf.Sqrt(gunHeat) <= AimPlusTiers.LongResetSlack) readyAt = Now;
        if (!Alive(bot))
        {
            respawnIn -= dt;
            if (respawnIn <= 0) Spawn();
            return;
        }
        var b = bot!;
        if (kind == Kind.Peek) UpdatePeek(dt, b);
        else UpdateOpen(dt, b);
        if (bot == null) return; // left

        if (!seen && G.LineOfSight(G.View.Eye, b.Head))
        {
            seen = true;
            seenAt = Now;
            b.SpawnTime = Now; // time to kill counts from first sight
            var (err, pe) = AimPlusMath.SeenError(G.View, b.Head);
            Event(AimPlusTiers.SeenEvent, err, pe);
        }
    }

    void UpdateOpen(float dt, BotCharacter b)
    {
        if (seen && Now - seenAt > AimPlusTiers.LongLifetime(Tier)) { Leave(); return; }
        if (kind != Kind.Strafe) { b.Velocity = Vector3.Zero; return; }
        turnIn -= dt;
        if (turnIn <= 0)
        {
            var (a, c) = Tier >= 3 ? Difficulty.TurnInterval(Tier) : (0.5f, 1.4f);
            bool stop = Rng.NextDouble() < 0.2;
            targetVx = stop ? 0 : (Rng.Next(2) == 0 ? -1 : 1) * AimPlusTiers.LongStrafeSpeed(Tier);
            turnIn = R(a, c);
        }
        var v = b.Velocity;
        v.X = Mathf.MoveToward(v.X, targetVx, Difficulty.StrafeDecel * dt);
        var p = b.Feet + v * dt;
        if (Mathf.Abs(p.X - x0) > 3.5f) { p.X = x0 + Mathf.Sign(p.X - x0) * 3.5f; v.X = 0; targetVx = -targetVx; }
        b.Velocity = v;
        b.Feet = p;
    }

    void UpdatePeek(float dt, BotCharacter b)
    {
        stT += dt;
        float speed = Tier >= 3 ? 5.4f : 4.5f, travel = peekDist / speed;
        switch (pst)
        {
            case PSt.Wait:
                b.Velocity = Vector3.Zero;
                if (stT >= 0)
                {
                    pst = PSt.Out; stT = 0;
                    var (h0, h1) = AimPlusTiers.LongPeekHold(Tier);
                    holdFor = R(h0, h1);
                }
                break;
            case PSt.Out:
                MoveTo(b, hidePos + peekDir * Mathf.Min(peekDist, stT * speed), dt);
                if (stT >= travel) { pst = PSt.Hold; stT = 0; }
                break;
            case PSt.Hold:
                b.Velocity = Vector3.Zero;
                if (stT >= holdFor) { pst = PSt.Back; stT = 0; }
                break;
            case PSt.Back:
                MoveTo(b, hidePos + peekDir * Mathf.Max(0, peekDist - stT * speed), dt);
                if (stT >= travel)
                {
                    peeks++;
                    if (peeks < 2 && Rng.NextDouble() < 0.5) { pst = PSt.Wait; stT = -R(0.3f, 0.8f); break; }
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
            Score -= 40;
            G.Sound("fail", 0.5f);
            Event("target_expired", IdOf(bot!));
        }
        G.Despawn(bot!);
        bot = null;
        respawnIn = R(0.5f, 1.0f);
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        // Recoil state at the moment of this shot: the gun's heat as of this frame (a 2nd bullet in one frame = a spray).
        float heatPre = Now == lastBulletAt ? 99f : gunHeat;
        lastBulletAt = Now;
        float left = Recovery * Mathf.Sqrt(Mathf.Max(0, heatPre));
        bool tooFast = left > AimPlusTiers.LongResetSlack;
        taps++;
        if (tooFast) { tooFastUntil = Now + 1.4f; Score -= 5; Event("tap_too_fast", left * 1000f); }
        else disciplined++;
        // The gun already applied this shot's recoil: the ring runs until that has recovered.
        float post = G is GameSession gs && gs.Gun != null ? gs.Gun.Heat : 1f;
        leftAtShot = Mathf.Max(0.05f, Recovery * Mathf.Sqrt(post));

        bool firstBullet = Alive(bot) && seen && !firstShotDone;
        HitZone zone = HitZone.None;
        bool killed = false;
        float hd = float.PositiveInfinity;
        BotCharacter? hit = null;
        if (Alive(bot)) hit = ShootBots(new[] { bot! }, o, d, out zone, out killed, out hd, Wall(o, d));
        if (firstBullet) { firstShotDone = true; firstShots++; if (hit != null && zone == HitZone.Head) firstHeads++; }
        if (hit == null) { Score -= 10; return float.PositiveInfinity; }
        if (!tooFast && !killed) Score += 10;
        if (killed)
        {
            if (zone == HitZone.Head) headKills++;
            if (tooFast)
            {
                wastedKills++;
                G.Banner("TOO FAST — NO POINTS", UiTheme.Warn);
            }
            else Score += (zone == HitZone.Head ? 150 : 80) + (int)Mathf.Max(0, (2500 - KillTimes[^1]) / 20) + (firstBullet && zone == HitZone.Head ? 25 : 0);
            bot = null; // death animation, then it frees itself
            respawnIn = R(0.6f, 1.1f);
        }
        return hd;
    }

    float Discipline => taps == 0 ? 1f : (float)disciplined / taps;
    int Resolved => Kills + escaped;

    public override string? Prompt =>
        Now < tooFastUntil ? $"Too fast — let the recoil reset (≈ {Recovery * 1000:0} ms between taps)"
        : Shots == 0 ? "Tap the head once, then wait for the recoil to reset before the next tap" : null;

    public override IEnumerable<string> HudLines()
    {
        if (Alive(bot)) yield return $"Distance {dist:0} m";
        if (taps > 0) yield return $"Tap discipline {Discipline * 100:0}%";
        if (firstShots > 0) yield return $"First-bullet heads {firstHeads} / {firstShots}";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Kills", Resolved == 0 ? "—" : $"{Kills} / {Resolved} agents  ·  {headKills} headshots");
        yield return ("First-bullet headshots", firstShots == 0 ? "—" : $"{100f * firstHeads / firstShots:0}%  ({firstHeads}/{firstShots})");
        yield return ("Tap discipline", taps == 0 ? "—" : $"{Discipline * 100:0}%  ({taps - disciplined} too fast" + (wastedKills > 0 ? $", {wastedKills} kills wasted)" : ")"));
        yield return ("Median time to kill", KillTimes.Count > 0 ? $"{Median(KillTimes):0} ms" : "—");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})  ·  {escaped} escaped");
    }

    /// <summary>Median time from first sight to the kill (needs ≥ 60% kills of 5+ agents); poor tap discipline costs
    /// a badge (&lt; 75%) or all of it (&lt; 50%).</summary>
    public override int Badge()
    {
        if (Resolved < 5 || (float)Kills / Resolved < 0.6f || KillTimes.Count == 0) return -1;
        int b = Difficulty.BadgeLower(Median(KillTimes), Difficulty.LongTapsBadges[Tier]);
        if (Discipline < 0.5f) return -1;
        if (Discipline < 0.75f) b--;
        return Math.Max(-1, b);
    }

    // ---------------- recoil-reset ring around the crosshair ----------------

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
