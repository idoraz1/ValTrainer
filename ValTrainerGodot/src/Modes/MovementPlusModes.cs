using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Jump Peek: an Operator holds a long angle (24–47 m) from a random spot. Gather the info by jump-peeking: strafe out and
/// jump, see him, back out before you land. Why it works in VALORANT: you're exposed only for a moment, higher than his
/// pre-aim, and moving vertically, so a sniper has to react, flick up and click inside that window. A missed Op shot
/// buys ~1.7 s of bolt cycle: then swing out and make the play. Ground peeks get you one-tapped.
/// </summary>
public sealed class JumpPeekMode : OpAngleMode
{
    public override string Key => "jumppeek";
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.Any;
    public override bool Minimap => true; // suggested on Discord: get used to reading the minimap while you peek
    public override float MinimapRange => 38f; // 49 m ahead of you: the Op holds from up to ~48 m away
    public override string Name => L.N("Jump Peek");
    public override string Description => L.N("An Operator holds a long angle. Jump-peek to spot him and get back before he shoots; if he misses, swing and kill him during his bolt.");

    protected override (string Name, Vector3 Feet)[] Spots { get; } =
    {
        (L.N("Long"), new Vector3(-4f, 0, -36f)),
        (L.N("Far long"), new Vector3(-8.5f, 0, -47f)),
        (L.N("Heaven"), new Vector3(-8.5f, 2.4f, -43f)),
        (L.N("Crate"), new Vector3(3.8f, 0, -34.8f)),
        (L.N("Right side"), new Vector3(8f, 0, -41f)),
        (L.N("Mid"), new Vector3(-1.5f, 0, -27f)),
        (L.N("Tall crate"), new Vector3(8.2f, 0, -27.5f)),
    };

    int spots, jumpSpots, opShots, dodged, airDodged, plays, safeRounds, finishedRounds, heldFire;
    bool roundSpotted, roundDied;
    float spotAt = -99f, lastExposure = -1f;
    bool lastExposureAir;

    protected override void OnNewRound()
    {
        roundSpotted = roundDied = false;
        spotAt = -99f;
        jumping = false;
    }

    // ---- clean jump peeks (v1.21, suggested on Discord): every jump from the corner is graded when you land ----
    // Clean = you saw the Op during the jump (or already knew where he was: a bait re-peek), landed where he can't see any
    // part of you, and didn't fire in the air (airborne error: rifles +10°). That is how a jump peek is used in VALORANT:
    // jump past the corner to gather info or bait the Op, land back behind cover, take the fight on the ground.
    int jpPeeks, jpClean, jpOpen, jpNoInfo, airShots;
    bool jumping, jpSaw, jpKnew, jpFired;
    /// <summary>Outcome of a graded jump (the "jump_peek" event's A): clean, landed in the open, fired in the air, saw nothing.</summary>
    public enum JumpPeekResult { Clean, LandedOpen, FiredInAir, NoInfo }

    protected override void Setup()
    {
        base.Setup();
        G.PlayerJumped += OnJumped;
    }

    void OnJumped()
    {
        // a jump from the corner pocket while the Op holds (not the swing after his miss)
        jumping = Between < 0 && Bot is { Dead: false } && !PunishWindow && PlayerX > CornerX - 1.7f && PlayerX < CornerX + 1.2f;
        jpSaw = jpFired = false;
        jpKnew = roundSpotted;
    }

    /// <summary>Some part of the player's body is in the Op's line of sight right now (wherever he looks).</summary>
    bool OpCanSee()
    {
        if (Bot == null) return false;
        for (int i = 0; i < 4; i++)
            if (G.LineOfSight(Bot.Head, PlayerHitbox.PartAt((PlayerHitbox.Part)i, G.View.Eye, G.View.Yaw))) return true;
        return false;
    }

    void GradeJump()
    {
        jumping = false;
        if (Between >= 0 || Bot == null || Bot.Dead) return; // the round ended in the air (a kill or a death)
        jpPeeks++;
        bool exposed = OpCanSee();
        var r = jpFired ? JumpPeekResult.FiredInAir : exposed ? JumpPeekResult.LandedOpen
            : !(jpSaw || jpKnew) ? JumpPeekResult.NoInfo : JumpPeekResult.Clean;
        Event("jump_peek", (int)r, jpSaw ? 1 : 0);
        var k = Main.I.Valorant;
        switch (r)
        {
            case JumpPeekResult.Clean:
                jpClean++;
                Score += 75;
                if (PunishWindow) G.Banner(L.N("CLEAN JUMP PEEK — he missed: swing now"), UiTheme.Good);
                else G.Banner(L.F($"CLEAN JUMP PEEK · OP {SpotLabel} · landed in cover"), UiTheme.Good);
                break;
            case JumpPeekResult.LandedOpen:
                jpOpen++;
                Score -= 25;
                G.Banner(L.F($"LANDED IN THE OPEN — hold {k.KeyLeft} in the air to drift back behind the corner"), UiTheme.Warn);
                break;
            case JumpPeekResult.FiredInAir:
                Score -= 50;
                G.Banner(L.F($"DON'T SHOOT IN THE AIR — +{G.Weapon?.AirErr ?? 10f:0}° spread while airborne. Land, stop, then shoot"), UiTheme.Warn);
                break;
            default:
                jpNoInfo++;
                G.Banner(L.N("NO INFO — strafe out at full speed and jump right at the corner's edge"), UiTheme.Warn);
                break;
        }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        if (!G.PlayerGrounded) { airShots++; if (jumping) jpFired = true; }
        return base.OnBullet(o, d, accurate);
    }

    void FinishRound()
    {
        finishedRounds++;
        if (roundSpotted && !roundDied) safeRounds++;
    }

    protected override void OnSpotted(bool airborne)
    {
        spots++;
        roundSpotted = true;
        spotAt = Now;
        Score += 100;
        if (airborne) { jumpSpots++; Score += 50; }
        Event("spot", Bot != null ? Bot.Head.DistanceTo(G.View.Eye) : 0, airborne ? 1 : 0);
        G.Banner(airborne ? L.F($"JUMP-SPOT · OP {SpotLabel}") : L.F($"SPOTTED · OP {SpotLabel}"),
            airborne ? UiTheme.Good : UiTheme.Warn);
    }

    protected override void OnOpFired(OpShot s)
    {
        base.OnOpFired(s);
        opShots++;
        if (s.Hit)
        {
            Score -= (int)Mathf.Min(s.Damage, 150f);
            if (s.Zone == HitZone.Legs) G.Banner(L.N("LEG SHOT — 120 damage. Shorter peeks!"), UiTheme.Warn);
            return;
        }
        dodged++;
        if (s.TargetAirborne) airDodged++;
        Score += 50;
        G.Banner(L.N("OP MISSED — SWING NOW"), UiTheme.Good);
    }

    protected override void OnOpKilled(bool inPunishWindow)
    {
        if (inPunishWindow) { plays++; Score += 200; G.Banner(L.N("PLAY MADE — punished the missed shot"), UiTheme.Good); }
        else { Score += 100; G.Banner(L.N("OP DOWN — risky swing, but it worked"), UiTheme.Warn); }
        FinishRound();
    }

    public override void OnPlayerDied()
    {
        roundDied = true;
        Score -= 200;
        FinishRound();
        base.OnPlayerDied();
    }

    protected override void OnExposureEnded(float seconds, bool airborne)
    {
        lastExposure = seconds;
        lastExposureAir = airborne;
    }

    protected override void OnOpHeldFire()
    {
        heldFire++;
        Score += 25;
        if (Between < 0) G.Banner(L.N("CLEAN PEEK — he couldn't get the shot off"), UiTheme.Good);
    }

    protected override void OnRoundTimeout()
    {
        FinishRound();
        if (!roundSpotted) G.Banner(L.N("NO INFO — the Op repositioned"), UiTheme.Warn);
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        if (jumping)
        {
            if (!G.PlayerGrounded)
            {
                if (!jpSaw && Bot is { Dead: false } && OnScreen(Bot.Head) && G.LineOfSight(G.View.Eye, Bot.Head)) jpSaw = true;
            }
            else GradeJump();
        }
        // Info gathered: a few seconds to make a play, then he repositions.
        if (Between < 0 && roundSpotted && Now - spotAt > 4.5f && !PunishWindow && Op is { SeesPlayer: false })
        {
            FinishRound();
            G.Banner(L.F($"INFO: OP {SpotLabel} — he repositions"), UiTheme.Text);
            EndRound(0.8f);
        }
    }

    public override IEnumerable<string> HudLines()
    {
        yield return L.F($"Spots {spots} ({jumpSpots} mid-jump) · Op misses {dodged} · Deaths {Deaths}");
        if (jpPeeks > 0) yield return L.F($"Clean jump peeks {jpClean} of {jpPeeks}");
        if (lastExposure >= 0) yield return lastExposureAir ? L.F($"Last peek: seen for {lastExposure * 1000:0} ms (jumping)") : L.F($"Last peek: seen for {lastExposure * 1000:0} ms (on the ground)");
        if (G.Mover is { Grounded: false }) yield return L.N("AIRBORNE — inaccurate");
    }

    public override string? Prompt
    {
        get
        {
            if (Between >= 0 || Bot == null) return null;
            var k = Main.I.Valorant;
            if (PunishWindow) return L.N("He missed — swing out and kill him before he re-chambers");
            if (roundSpotted) return L.N("Spotted. Re-peek to bait a shot, or wait — he repositions");
            return L.F($"Jump-peek: {k.KeyRight} + {k.KeyJump} out, {k.KeyLeft} back before you land");
        }
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Rounds with the Op spotted and you alive"), L.F($"{safeRounds} of {finishedRounds}"));
        yield return (L.N("Clean jump peeks (saw him, landed in cover, no shots in the air)"), L.F($"{jpClean} of {jpPeeks}"));
        if (jpOpen > 0 || airShots > 0) yield return (L.N("Landings in the open / shots fired in the air"), $"{jpOpen} / {airShots}");
        if (jpNoInfo > 0) yield return (L.N("Jumps that saw nothing"), jpNoInfo.ToString());
        yield return (L.N("Spots (mid-jump)"), $"{spots} ({jumpSpots})");
        yield return (L.N("Op shots that missed you"), airDodged > 0 ? L.F($"{dodged} of {opShots} ({airDodged} while airborne)") : L.F($"{dodged} of {opShots}"));
        yield return (L.N("Peeks he reacted to but couldn't shoot"), heldFire.ToString());
        yield return (L.N("Plays: kills after a miss"), plays.ToString());
        if (Kills > plays) yield return (L.N("Other kills"), (Kills - plays).ToString());
        yield return (L.N("Damage taken / deaths"), $"{DamageTaken:0} / {Deaths}");
        if (Exposures.Count > 0) yield return (L.N("Avg time seen per peek"), L.F($"{Exposures.Average() * 1000:0} ms (shorter = safer)"));
        foreach (var l in MovementLines()) yield return l;
    }

    public override int Badge()
    {
        DevReport();
        return finishedRounds == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)safeRounds / finishedRounds);
    }

    // dev --movescript auto: jump-peek from the corner, back out in the air; swing when he misses.
    protected override void Auto(float dt, HashSet<InputBinding> keys)
    {
        var k = Main.I.Valorant;
        keys.Clear();
        if (Between >= 0) { AutoPhase = 0; AutoT = 0; return; }
        if (PunishWindow && AutoPhase < 3) { AutoPhase = 3; AutoT = 0; }
        switch (AutoPhase)
        {
            case 0: // wait deep in the pocket (a run-up: the jump has to leave at full speed)
                if (PlayerX > -1.7f) keys.Add(k.KeyLeft);
                else if (AutoT > 0.9f && G.PlayerGrounded) { AutoPhase = 1; AutoT = 0; }
                break;
            case 1: // strafe out, jump right at the corner's edge
                keys.Add(k.KeyRight);
                if (PlayerX > -0.35f && G.PlayerGrounded) keys.Add(k.KeyJump);
                if (!G.PlayerGrounded || AutoT > 1.2f) { AutoPhase = 2; AutoT = 0; } // the opposite key as soon as you're up
                break;
            case 2: // drift back in the air, land behind the corner
                keys.Add(k.KeyLeft);
                if (PlayerX <= -1.2f && G.PlayerGrounded) { AutoPhase = 0; AutoT = 0; }
                break;
            case 3: // swing during the bolt
                if (PlayerX < 2.2f && AutoT < 0.8f) keys.Add(k.KeyRight);
                else if (AutoT < 0.9f || G.Mover.Speed > G.Mover.AccurateMax) { if (G.Mover.Vel.X > 0.3f) keys.Add(k.KeyLeft); }
                if (AutoT > 2.4f || Bot == null) { AutoPhase = 2; AutoT = 0; }
                break;
        }
    }
}

/// <summary>
/// Jiggle Peek: an Operator holds a closer angle (11–24 m). Bait his shot with a quick shoulder peek (show a shoulder,
/// not your head, and get back before he clicks), then wide-swing during his ~1.7 s bolt cycle and kill him.
/// Every Op shoots a shoulder he sees: low tiers bite almost every time and click late (a wide bait window); high tiers
/// react fast and only shoot what they still see (a tighter bait window); a Pro sometimes doesn't bite at all.
/// </summary>
public sealed class JigglePeekMode : OpAngleMode
{
    public override string Key => "jigglepeek";
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.Any;
    public override bool Minimap => true;
    public override float MinimapRange => 21f; // 27 m ahead: the Op holds from 11-24 m
    public override string Name => L.N("Jiggle Peek");
    public override string Description => L.N("Shoulder-peek an Operator to bait his shot without showing your head, then wide-swing during his bolt and kill him.");
    protected override float RoundTime => 16f;

    protected override (string Name, Vector3 Feet)[] Spots { get; } =
    {
        (L.C("spot", "Close"), new Vector3(-3.5f, 0, -12.5f)), // translated now (each run builds the drill): "Close" alone is a button tooltip
        (L.N("Deep"), new Vector3(-8.5f, 0, -22.5f)),
        (L.N("Right"), new Vector3(6.2f, 0, -16f)),
        (L.N("Mid"), new Vector3(-1f, 0, -19.5f)),
        (L.N("Left"), new Vector3(-10.6f, 0, -15.5f)),
        (L.N("Crate"), new Vector3(-5.4f, 0, -23.2f)),
    };

    int baits, punishes, riskyWins, hitsTaken, cleanPeeks, peeks, finishedRounds, heldFire, ignored;
    float noteCooldown;
    bool baitedThisRound, headSeen;
    float lastExposure = -1f;
    string lastPeekNote = "";

    protected override void OnNewRound() { baitedThisRound = false; headSeen = false; }

    protected override void OnOpFired(OpShot s)
    {
        base.OnOpFired(s);
        if (s.Hit)
        {
            hitsTaken++;
            Score -= (int)Mathf.Min(s.Damage, 150f) / 2;
            autoHold = Mathf.Max(0.05f, autoHold - 0.06f); // dev autopilot: too long out
            return;
        }
        baits++;
        baitedThisRound = true;
        Score += 50;
        G.Banner(L.N("BAITED — WIDE SWING NOW"), UiTheme.Good);
    }

    protected override void OnOpKilled(bool inPunishWindow)
    {
        finishedRounds++;
        if (inPunishWindow) { punishes++; Score += 200; G.Banner(L.N("PUNISHED"), UiTheme.Good); }
        else { riskyWins++; Score += 75; G.Banner(L.N("KILL — but he wasn't baited (risky)"), UiTheme.Warn); }
    }

    public override void OnPlayerDied()
    {
        finishedRounds++;
        Score -= 150;
        base.OnPlayerDied();
    }

    protected override void OnRoundTimeout() { finishedRounds++; G.Banner(L.N("TIME — he repositions"), UiTheme.Warn); }

    public override void Update(float dt)
    {
        base.Update(dt);
        // Did he get a look at your head during this peek? (a clean jiggle shows only the shoulder)
        if (Op is { SeesPlayer: true } && Bot != null && G.LineOfSight(Bot.Head, PlayerHitbox.PartAt(PlayerHitbox.Part.Head, G.View.Eye, G.View.Yaw)))
            headSeen = true;
    }

    protected override void OnExposureEnded(float seconds, bool airborne)
    {
        lastExposure = seconds;
        if (PunishWindow) { headSeen = false; return; } // the swing after a bait isn't a jiggle
        peeks++;
        if (!headSeen) cleanPeeks++;
        lastPeekNote = headSeen ? L.N("head shown") : L.N("shoulder only");
        headSeen = false;
        if (!ReactedThisPeek && Op is { State: OpBotBrain.St.Holding })
        {
            ignored++;
            autoHold += 0.04f; // dev autopilot: show a little more next time
            if (Now > noteCooldown)
            {
                noteCooldown = Now + 2.5f;
                G.Banner(Op.ShoulderSight ? L.N("NO REACTION — too quick for him: show the shoulder a bit longer")
                                          : L.N("NO REACTION — this Op ignores a lone shoulder: show a bit more of you"), UiTheme.Warn);
            }
        }
    }

    protected override void OnOpHeldFire()
    {
        if (PunishWindow) return;
        heldFire++;
        autoHold += 0.03f;
        if (Now > noteCooldown) { noteCooldown = Now + 2.5f; G.Banner(L.N("HE HELD FIRE — you hid too early: stay out a touch longer"), UiTheme.Warn); }
    }

    public override IEnumerable<string> HudLines()
    {
        yield return L.F($"Baits {baits} · Punishes {punishes} · Deaths {Deaths}");
        if (lastExposure >= 0) yield return L.F($"Last peek: seen for {lastExposure * 1000:0} ms · {L.T(lastPeekNote)}");
        if (Op != null && Op.State == OpBotBrain.St.Cycling) yield return L.F($"Op re-chambering: {Mathf.Max(0, Op.ReadyAt - Now):0.0} s");
    }

    public override string? Prompt
    {
        get
        {
            if (Between >= 0 || Bot == null) return null;
            var k = Main.I.Valorant;
            if (PunishWindow) return L.F($"Baited! Wide-swing ({k.KeyRight}), stop, kill him before he re-chambers");
            if (Op is { State: OpBotBrain.St.Engaging }) return null;
            return L.F($"Tap {k.KeyRight} to show a shoulder, {k.KeyLeft} back — bait the shot without showing your head");
        }
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Punishes (kills after a bait)"), L.P(finishedRounds, $"{punishes} of {finishedRounds} round", $"{punishes} of {finishedRounds} rounds"));
        yield return (L.N("Baits (Op shots that missed)"), baits.ToString());
        yield return (L.N("Kills without a bait (risky)"), riskyWins.ToString());
        yield return (L.N("Times the Op hit you"), hitsTaken.ToString());
        yield return (L.N("Clean shoulder peeks"), L.F($"{cleanPeeks} of {peeks}"));
        yield return (L.N("Peeks he ignored / reacted to but held fire"), $"{ignored} / {heldFire}");
        if (Exposures.Count > 0) yield return (L.N("Avg time seen per peek"), L.F($"{Exposures.Average() * 1000:0} ms"));
        yield return (L.N("Damage taken / deaths"), $"{DamageTaken:0} / {Deaths}");
        foreach (var l in MovementLines()) yield return l;
    }

    public override int Badge()
    {
        DevReport();
        return finishedRounds == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)punishes / finishedRounds);
    }

    // dev --movescript auto: show a shoulder for a while, back; like a player, show more when he doesn't react and less
    // when he hits. When baited: wide swing, counter-strafe, let the simulated aim (--simaim) shoot.
    float autoHold = 0.12f;

    protected override void Auto(float dt, HashSet<InputBinding> keys)
    {
        var k = Main.I.Valorant;
        keys.Clear();
        if (Between >= 0) { AutoPhase = 0; AutoT = 0; return; }
        if (PunishWindow && AutoPhase < 3) { AutoPhase = 3; AutoT = 0; }
        switch (AutoPhase)
        {
            case 0:
                if (PlayerX > -0.5f) keys.Add(k.KeyLeft);
                else if (AutoT > 0.6f + 0.4f * (float)Rng.NextDouble() && Op is { State: OpBotBrain.St.Holding }) { AutoPhase = 1; AutoT = 0; }
                break;
            case 1: // step out until he sees the shoulder, stop there (counter-strafe), hold it for autoHold seconds
                if (Op is { SeesPlayer: true }) autoSeen += dt;
                if (autoSeen >= Mathf.Min(autoHold, 1.2f) || AutoT > 2f) { AutoPhase = 2; AutoT = 0; autoSeen = 0; break; }
                if (autoSeen == 0) { keys.Add(k.KeyRight); keys.Add(k.KeyWalk); } // shift-creep: small overshoot
                else if (G.Mover.Vel.X > 0.4f) keys.Add(k.KeyLeft);
                break;
            case 2:
                keys.Add(k.KeyLeft);
                if (PlayerX <= -0.6f) { AutoPhase = 0; AutoT = 0; }
                break;
            case 3: // wide swing, counter-strafe, shoot
                if (PlayerX < 2.0f && AutoT < 0.8f) keys.Add(k.KeyRight);
                else if (G.Mover.Vel.X > 0.3f) keys.Add(k.KeyLeft);
                if (AutoT > 2.4f || Bot == null) { AutoPhase = 2; AutoT = 0; }
                break;
        }
    }

    float autoSeen;
}
