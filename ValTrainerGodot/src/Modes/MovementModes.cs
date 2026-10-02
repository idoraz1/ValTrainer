using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

/// <summary>Shared bits for modes where the player moves.</summary>
public abstract class MovementMode : BotMode
{
    public override bool Movement => true;
    public override string Category => "Movement";
    protected int MovingShots;

    protected void CountShot(bool accurate)
    {
        Shots++;
        if (!accurate) { MovingShots++; Score -= 15; }
    }

    protected IEnumerable<(string, string)> MovementLines()
    {
        yield return ("Shots while moving", $"{MovingShots} of {Shots}");
        var m = G.Mover;
        if (m.CounterStopTimesMs.Count > 0) yield return ("Avg counter-strafe stop", $"{m.CounterStopTimesMs.Average():0} ms");
        if (m.ReleaseStopTimesMs.Count > 0) yield return ("Avg stop by releasing keys", $"{m.ReleaseStopTimesMs.Average():0} ms");
    }

    // The HUD shows the last stop time under the speed meter.
}

/// <summary>Strafe A/D; a bot appears only while you run. Counter-strafe to stop dead, then one-tap.</summary>
public sealed class CounterStrafeMode : MovementMode
{
    public override AimFocus? Focus => bot != null && !bot.Dead && GodotObject.IsInstanceValid(bot) ? new AimFocus(IdOf(bot), bot.Head, BotCharacter.HeadRadius) : null;

    public override string Key => "counterstrafe";
    public override string Name => "Counter-Strafe";
    public override string Description => "Strafe A/D. A bot appears while you run: tap the opposite key to stop, then one-tap.";

    public override IEnumerable<Box> ExtraSolids => new[]
    {
        new Box(new Vector3(-9, 0, -2.5f), new Vector3(-8.5f, 1.2f, 2.5f)),
        new Box(new Vector3(8.5f, 0, -2.5f), new Vector3(9, 1.2f, 2.5f)),
        new Box(new Vector3(-9, 0, -3), new Vector3(9, 0.8f, -2.5f)),
        new Box(new Vector3(-9, 0, 2.5f), new Vector3(9, 1.2f, 3)),
    };

    BotCharacter? bot;
    float movingFor, needMoving = 0.6f, cooldown;
    int spawned, timeouts;

    public override void Update(float dt)
    {
        var m = G.Mover;
        if (bot == null)
        {
            cooldown -= dt;
            movingFor = m.Speed > Mover.RunSpeed * 0.8f ? movingFor + dt : Mathf.Max(0, movingFor - dt * 2);
            if (cooldown <= 0 && movingFor >= needMoving)
            {
                bot = G.SpawnBot(new Vector3(Mathf.Clamp(G.View.Eye.X + R(-6, 6), -9, 9), 0, -R(10, 18)));
                bot.FacingYaw = 180f;
                spawned++;
                G.Sound("tick");
                Event("target_spawn", IdOf(bot), AngleFromCrosshair(bot.Head));
            }
            return;
        }
        if (Now - bot.SpawnTime > Difficulty.CounterStrafeWindow(Tier))
        {
            timeouts++; Score -= 50; G.Sound("fail");
            Event("target_expired", IdOf(bot));
            G.Despawn(bot); Next();
        }
    }

    void Next()
    {
        bot = null; movingFor = 0; needMoving = R(0.3f, 1.2f); cooldown = R(0.2f, 0.6f);
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (bot == null) return float.PositiveInfinity;
        var hit = ShootBots(new[] { bot }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            Score += 100 + (int)Mathf.Max(0, (1000 - KillTimes[^1]) / 5);
            Next(); // the dead body frees itself after its death animation
        }
        return dist;
    }

    public override string? Prompt => bot == null && G.Mover.Speed < Mover.RunSpeed * 0.8f ? "Keep strafing A / D — a bot appears while you're running" : null;

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        foreach (var l in base.ResultLines()) yield return l;
        foreach (var l in MovementLines()) yield return l;
        yield return ("Bots that timed out", timeouts.ToString());
    }

    public override int Badge()
    {
        int n = spawned - (bot != null && !bot.Dead ? 1 : 0);
        return Difficulty.BadgeTtk(Tier, n <= 0 ? 0 : (float)Kills / n, Median(KillTimes), Difficulty.CounterStrafeTtk);
    }
}

/// <summary>Wide-swing a corner into a bot holding the angle. It fights back with the tier's duel model.</summary>
public sealed class PeekDuelMode : MovementMode
{
    public override AimFocus? Focus => st == St.Live && bot != null && !bot.Dead && GodotObject.IsInstanceValid(bot) ? new AimFocus(IdOf(bot), bot.Head, BotCharacter.HeadRadius) : null;

    public override string Key => "peekduel";
    public override string Name => "Peek Duels";
    public override string Description => "Swing out from a corner vs a bot holding the angle. It shoots back — stop, hit first, get back.";
    public override Vector3 StartFeet => new(0.4f, 0, 0);
    public override float StartYaw => 6f;

    static readonly Box WallBox = new(new Vector3(-14, 0, -4.4f), new Vector3(1.0f, 4.2f, -3.0f));
    const float HiddenX = 1.05f;
    static readonly Vector3 SwungOut = new(5f, PlayerView.EyeHeight, 0);

    public override IEnumerable<Box> ExtraSolids => new[]
    {
        WallBox,
        new Box(new Vector3(-0.6f, 0, -1), new Vector3(-0.1f, 4.2f, 3)),
        new Box(new Vector3(-0.6f, 0, 2.2f), new Vector3(9, 4.2f, 2.7f)),
        new Box(new Vector3(8, 0, -1), new Vector3(8.5f, 1.2f, 2.2f)),
        new Box(new Vector3(-0.6f, 0, -0.7f), new Vector3(9, 0.8f, -0.6f)),
    };

    enum St { ToCover, Delay, Live, After }
    St st = St.ToCover;
    BotCharacter? bot;
    BotBrain? brain;
    float stateT, delay;
    int wins, losses;

    bool WallBlocks(Vector3 a, Vector3 b) => Collision.Blocked(a, b, new[] { WallBox });

    void PlaceBot()
    {
        Vector3 pos = new(-4, 0, -16);
        for (int i = 0; i < 30; i++)
        {
            pos = new Vector3(R(-9, -1.0f), 0, -R(10, 26));
            var head = pos + new Vector3(0, PlayerView.EyeHeight, 0);
            if (WallBlocks(G.View.Eye, head) && !WallBlocks(SwungOut, head)) break;
        }
        bot = G.SpawnBot(pos);
        var toCorner = (new Vector3(1.0f, PlayerView.EyeHeight, -3.0f) - bot.Head).Normalized();
        bot.FacingYaw = Mathf.RadToDeg(Mathf.Atan2(toCorner.X, -toCorner.Z));
        brain = new BotBrain(bot, Difficulty.Get(Tier).Bot, Rng) { HeldDir = toCorner, ShoulderSight = Difficulty.ShoulderSight(Tier) };
    }

    public override void Update(float dt)
    {
        stateT += dt;
        switch (st)
        {
            case St.ToCover:
                if (G.View.Eye.X <= HiddenX) { st = St.Delay; stateT = 0; delay = R(0.3f, 0.9f); G.Player.Reset(); }
                break;
            case St.Delay:
                if (G.View.Eye.X > HiddenX) st = St.ToCover;
                else if (stateT >= delay) { PlaceBot(); st = St.Live; stateT = 0; }
                break;
            case St.Live:
                brain?.Update(G, dt);
                TrackSeen(bot);
                // Time-to-kill counts from the moment the bot first saw you.
                if (bot != null && brain is { FirstSeenAt: >= 0 } && bot.SpawnTime < brain.FirstSeenAt) bot.SpawnTime = brain.FirstSeenAt;
                break;
            case St.After:
                if (stateT > 0.8f) st = St.ToCover;
                break;
        }
    }

    public override void OnPlayerDied()
    {
        losses++; Deaths++; Score -= 75;
        Event("round_lost");
        G.Banner("YOU DIED — they hit first", UI.UiTheme.Accent);
        if (bot != null) { var b = bot; b.GetTree().CreateTimer(0.6).Timeout += () => G.Despawn(b); }
        bot = null; brain = null;
        st = St.After; stateT = 0;
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        CountShot(accurate);
        if (st != St.Live || bot == null) return float.PositiveInfinity;
        var hit = ShootBots(new[] { bot }, o, d, out var zone, out bool killed, out float dist, Wall(o, d));
        if (hit == null) return float.PositiveInfinity;
        Score += zone == HitZone.Head ? 50 : 15;
        if (killed)
        {
            wins++;
            Event("round_won");
            Score += 100 + (int)Mathf.Max(0, (600 - KillTimes[^1]) / 3);
            bot = null; brain = null; // dead body frees itself
            st = St.After; stateT = 0;
        }
        return dist;
    }

    public override string? Prompt => st switch
    {
        St.ToCover => "Back behind the corner (A) for the next duel",
        St.Live when brain is { SeesPlayer: false } => "Swing out (D), counter-strafe, shoot",
        _ => null,
    };

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Duels won / lost", $"{wins} / {losses}");
        if (KillTimes.Count > 0) yield return ("Median time to kill", $"{Median(KillTimes):0} ms");
        if (Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
        foreach (var l in MovementLines()) yield return l;
    }

    public override int Badge() => wins + losses == 0 ? -1 : Difficulty.BadgeWinRate(Tier, (float)wins / (wins + losses));
}
