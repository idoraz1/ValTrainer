using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

/// <summary>
/// Target Switch: three agents strafe A-D in front of you (Vandal: 1 head or 4 body). Kill one, switch to the next;
/// a killed agent is replaced half a second later somewhere else, so there is always someone to switch to.
/// Measures kill rate, switch time (kill → first hit on the next agent), time per kill and overflick on the switch
/// (the crosshair travelling past the far side of the next agent's body before the first hit).
/// </summary>
public sealed class TargetSwitchMode : BotMode
{
    public override string Key => "switch";
    public override string Name => "Target Switch";
    public override string Description => "Three strafing agents (Vandal: 1 head or 4 body). Kill one, switch to the next — fast switches, no flicking past them.";
    public override string Category => "Aim";
    public override bool InfiniteAmmo => true; // pure aim drill: no reloads
    public override string SimKind => "bot";
    public override AimFocus? Focus => focus?.B is { } b && Alive(b) ? new AimFocus(IdOf(b), b.Head, BotCharacter.HeadRadius) : null;

    sealed class Lane
    {
        public BotCharacter? B;
        public float X0, TargetVx, TurnIn, SpawnedAt, EngagedAt = -1, RespawnAt = -1;
    }

    readonly Lane[] lanes = new Lane[AimPlusTiers.SwitchAlive];
    Lane? focus;
    float switchStart;          // last kill (or the start): switch and per-kill times count from here
    bool switchOpen = true;     // waiting for the first hit after a kill
    // overflick tracking for the switch in progress
    Lane? flickTo;
    Vector2 flickU;
    float flickMax = float.NegativeInfinity, flickHalf;
    readonly List<float> switchMs = new(), engageMs = new(), overDeg = new();
    int judged, overflicks;
    float lastSwitchMs = -1;
    bool started;

    static bool Alive(BotCharacter? b) => b != null && GodotObject.IsInstanceValid(b) && !b.Dead;

    protected override void Setup()
    {
        for (int i = 0; i < lanes.Length; i++) { lanes[i] = new Lane(); Spawn(lanes[i]); }
        PickFocus();
    }

    void Spawn(Lane lane)
    {
        var (dMin, dMax) = AimPlusTiers.SwitchDistance(Tier);
        float spread = AimPlusTiers.SwitchSpread(Tier), d = R(dMin, dMax), yaw = 0;
        for (int i = 0; i < 30; i++)
        {
            yaw = R(-spread, spread);
            bool clear = true;
            foreach (var l in lanes)
            {
                if (l == null || l == lane || !Alive(l.B)) continue;
                var (ly, _) = AimPlusMath.Angles(G.View.Eye, l.B!.Feet);
                if (Mathf.Abs(ly - yaw) < 11f) { clear = false; break; }
            }
            if (clear && Now > 0.1f && Mathf.Abs(Mathf.Wrap(yaw - G.View.ViewYaw, -180f, 180f)) < 8f) clear = false;
            if (clear) break;
        }
        var feet = new Vector3(d * Mathf.Sin(Mathf.DegToRad(yaw)), 0, -d * Mathf.Cos(Mathf.DegToRad(yaw)));
        var b = G.SpawnBot(feet);
        b.FacingYaw = AimPlusMath.FaceOrigin(feet);
        lane.B = b;
        lane.X0 = feet.X;
        lane.TurnIn = 0;
        lane.SpawnedAt = Now;
        lane.EngagedAt = -1;
        lane.RespawnAt = -1;
        Event("target_spawn", IdOf(b), AngleFromCrosshair(b.Head));
    }

    Vector3 Chest(BotCharacter b) => b.Feet + new Vector3(0, 1.25f, 0);

    /// <summary>The agent the player should go for next: the live one nearest the crosshair (sticky until it dies or
    /// the player hits another one). Starts the overflick measurement when a switch is open.</summary>
    void PickFocus()
    {
        Lane? best = null;
        float bestA = float.MaxValue;
        foreach (var l in lanes)
        {
            if (!Alive(l.B)) continue;
            float a = AngleFromCrosshair(l.B!.Head);
            if (a < bestA) { bestA = a; best = l; }
        }
        focus = best;
        flickTo = null;
        if (best == null || !switchOpen) return;
        var b = best.B!;
        float dist = G.View.Eye.DistanceTo(Chest(b));
        flickHalf = AimPlusMath.RadiusDeg(0.3f, dist); // half a body width (shoulders + arms)
        var e = AimPlusMath.CrossMinus(G.View, Chest(b));
        float a0 = e.Length();
        if (a0 <= 2f * flickHalf) return; // already on it: no flick to judge
        flickTo = best;
        flickU = -e / a0;
        flickMax = float.NegativeInfinity;
    }

    public override void Update(float dt)
    {
        if (!started) { started = true; switchStart = Now; PickFocus(); } // the view is only reset for the run after Setup
        foreach (var l in lanes)
        {
            if (!Alive(l.B))
            {
                if (l.RespawnAt >= 0 && Now >= l.RespawnAt) Spawn(l);
                continue;
            }
            var b = l.B!;
            b.SpawnTime = Mathf.Max(l.SpawnedAt, switchStart); // kill times = time since the last kill
            l.TurnIn -= dt;
            if (l.TurnIn <= 0)
            {
                var (a, c) = Difficulty.TurnInterval(Tier);
                bool stop = Rng.NextDouble() < Math.Max(0.12, Difficulty.StopChance(Tier));
                l.TargetVx = stop ? 0 : (Rng.Next(2) == 0 ? -1 : 1) * AimPlusTiers.SwitchSpeed(Tier);
                l.TurnIn = R(a, c);
            }
            var v = b.Velocity;
            v.X = Mathf.MoveToward(v.X, l.TargetVx, Difficulty.StrafeDecel * dt);
            var p = b.Feet + v * dt;
            if (Mathf.Abs(p.X - l.X0) > 3.5f) { p.X = l.X0 + Mathf.Sign(p.X - l.X0) * 3.5f; v.X = 0; l.TargetVx = -l.TargetVx; }
            b.Velocity = v;
            b.Feet = p;
        }
        if (focus == null || !Alive(focus.B)) PickFocus();

        if (switchOpen && flickTo != null)
        {
            if (!Alive(flickTo.B) || Now - switchStart > 2f) flickTo = null;
            else
            {
                var e = AimPlusMath.CrossMinus(G.View, Chest(flickTo.B!)); // crosshair − body centre
                flickMax = Mathf.Max(flickMax, e.Dot(flickU));
            }
        }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        var live = lanes.Where(l => Alive(l.B)).Select(l => l.B!).ToList();
        var hit = ShootBots(live, o, d, out var zone, out bool killed, out float dist);
        if (hit == null) { Score -= 5; return float.PositiveInfinity; }
        var lane = lanes.First(l => l.B == hit);
        if (lane.EngagedAt < 0) lane.EngagedAt = Now;
        if (switchOpen)
        {
            switchOpen = false;
            lastSwitchMs = (Now - switchStart) * 1000f;
            switchMs.Add(lastSwitchMs);
            if (flickTo == lane && float.IsFinite(flickMax))
            {
                judged++;
                float over = flickMax - flickHalf;
                if (over > 0) { overflicks++; overDeg.Add(over); }
            }
            flickTo = null;
        }
        focus = lane; // whoever you shoot is your target now
        Score += zone == HitZone.Head ? 0 : 10;
        if (killed)
        {
            float tpk = KillTimes[^1];
            engageMs.Add((Now - lane.EngagedAt) * 1000f);
            Score += 100 + (int)Mathf.Max(0, (1500 - tpk) / 10) + (zone == HitZone.Head ? 25 : 0);
            lane.B = null; // the body plays its death and frees itself
            lane.RespawnAt = Now + AimPlusTiers.SwitchRespawn;
            switchStart = Now;
            switchOpen = true;
            focus = null;
            PickFocus();
        }
        return dist;
    }

    float PerMinute => Kills * 60f / Mathf.Max(Now, 1f);

    public override string? Prompt => Kills == 0 && Now < 4f ? "Kill one, switch to the next — stop on the body, don't flick past it" : null;

    public override IEnumerable<string> HudLines()
    {
        if (lastSwitchMs >= 0) yield return $"Last switch {lastSwitchMs:0} ms";
        if (judged > 0) yield return $"Overflicks {overflicks} / {judged} switches";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Kills", $"{Kills}  ({PerMinute:0.0} per minute)");
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})" + (Hits > 0 ? $"  ·  {100f * HeadHits / Hits:0}% headshots" : ""));
        yield return ("Switch time", switchMs.Count > 0 ? $"{Median(switchMs):0} ms median (kill → next hit)" : "—");
        yield return ("Time per kill", KillTimes.Count > 0 ? $"{Median(KillTimes):0} ms median" + (engageMs.Count > 0 ? $" (first hit → kill {Median(engageMs):0} ms)" : "") : "—");
        yield return ("Overflick on switch", judged == 0 ? "—" : overflicks == 0 ? $"none in {judged} switches"
            : $"{100f * overflicks / judged:0}% of switches, {overDeg.Average():0.0}° past");
    }

    /// <summary>Kills per minute (needs ≥ 30% accuracy and 3 kills), thresholds per tier played.</summary>
    public override int Badge() => Kills < 3 || Accuracy < 0.3f ? -1 : Difficulty.BadgeHigher(PerMinute, Difficulty.SwitchBadges[Tier]);
}
