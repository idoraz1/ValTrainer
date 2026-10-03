using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;

namespace ValTrainer.Modes;

/// <summary>
/// DEV-ONLY auto-play for headless validation ("--dev --autothrow", usually with "--simaim good"): the simulated player
/// only aims and shoots, so every round this walks to the last hidden spot before the choke, picks the best throw by
/// simulating candidate casts (aim, curve side, charge, bounces, recast time) against the defenders' current views, casts
/// it, looks away from its own flash, then walks into the site and stops whenever a defender is in sight.
/// </summary>
public abstract partial class InitiatorDrill
{
    static readonly bool DevAuto = CmdLine.Dev && CmdLine.Has("--autothrow");
    enum DevSt { Off, ToSpot, Throw, Wait, Push }
    DevSt devSt;
    List<Vector3> devPath = new();
    int devIdx;
    float devEffectAt = -1f, devRecastAt = -1f, devThrownAt;
    float devPushTargetT;
    BotNav? devNav;

    BotNav? Nav() => devNav ??= BotNav.For($"{MapSpot.Key}:{G.Solid.Count}", G.Solid, MapSpot.StartFeet);

    Vector3 PlayerFeet => G.View.Eye - new Vector3(0, G.Mover.EyeHeight, 0);

    void DevNewRound()
    {
        if (!DevAuto) return;
        devSt = DevSt.ToSpot;
        devEffectAt = devRecastAt = -1f;
        var path = Nav()?.FindPath(MapSpot.StartFeet, MapSpot.Choke) ?? new List<Vector3> { MapSpot.Choke };
        // Walk the path in 0.5 m steps and stop at the last spot no defender can see.
        var pts = new List<Vector3> { MapSpot.StartFeet };
        var prev = MapSpot.StartFeet;
        foreach (var p in path)
        {
            float len = prev.DistanceTo(p);
            for (float s = 0.5f; s < len; s += 0.5f) pts.Add(prev.Lerp(p, s / len));
            pts.Add(p);
            prev = p;
        }
        int last = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            // Hidden = no defender sees any part of you (the bot brain checks head, chest and shoulders).
            var eye = pts[i] + new Vector3(0, PlayerView.EyeHeight, 0);
            var parts = new[]
            {
                eye, eye + Vector3.Down * 0.45f,
                eye + new Vector3(0.35f, -0.3f, 0), eye + new Vector3(-0.35f, -0.3f, 0),
                eye + new Vector3(0, -0.3f, 0.35f), eye + new Vector3(0, -0.3f, -0.35f),
            };
            if (defenders.Any(d => d.Alive && parts.Any(p => G.LineOfSight(d.Body.Head, p)))) break;
            last = i;
        }
        devPath = pts.Take(Math.Max(1, last - 1) + 1).ToList(); // a step back from the edge
        devIdx = 0;
    }

    /// <summary>Moves the player along <see cref="devPath"/>; true when the end is reached.</summary>
    bool DevWalk(float dt, float speed = Mover.RunSpeed)
    {
        float step = speed * dt;
        var feet = PlayerFeet;
        while (devIdx < devPath.Count && step > 0)
        {
            var target = devPath[devIdx];
            var to = new Vector3(target.X - feet.X, 0, target.Z - feet.Z);
            float d = to.Length();
            if (d <= step) { feet = new Vector3(target.X, target.Y, target.Z); step -= d; devIdx++; continue; }
            feet += to / d * step;
            feet.Y = Mathf.Lerp(feet.Y, target.Y, Mathf.Clamp(step / Mathf.Max(d, 0.01f), 0, 1));
            step = 0;
        }
        G.TeleportPlayer(feet);
        return devIdx >= devPath.Count;
    }

    void DevUpdate(float dt)
    {
        if (!DevAuto || devSt == DevSt.Off || G.Player.Dead) return;
        switch (devSt)
        {
            case DevSt.ToSpot:
                if (RoundT < 0.3f) break;
                if (DevWalk(dt)) devSt = DevSt.Throw;
                break;

            case DevSt.Throw:
            {
                var plan = DevBestThrow();
                if (plan == null) { devSt = DevSt.Push; StartPush(); break; }
                var p = plan.Value;
                LookAlong(p.Aim);
                if (Kit.ForceCast(p.Primary, p.Bars, p.Bounces))
                {
                    devThrownAt = Now;
                    devEffectAt = Now + p.EffectAfter;
                    devRecastAt = p.RecastAt >= 0 ? Now + p.RecastAt : -1f;
                    Kit.PutAway(Ability.GunBack);
                    GD.Print($"[autothrow] {Ability.Agent} {Ability.Ability}: score {p.Score:0.00}, effect in {p.EffectAfter:0.00}s, " +
                             $"primary={p.Primary} bars={p.Bars} bounces={p.Bounces} recast={p.RecastAt:0.00}");
                }
                devSt = DevSt.Wait;
                break;
            }

            case DevSt.Wait:
                if (devRecastAt >= 0 && Now >= devRecastAt)
                {
                    foreach (var u in utils.Where(u => u.CanRecast).ToList()) u.Recast();
                    devRecastAt = -1f;
                }
                // Swing once it has popped / started working (the view is the simulated player's from here on).
                if (Now >= devEffectAt + 0.05f || Now - devThrownAt > 4f) { devSt = DevSt.Push; StartPush(); }
                break;

            case DevSt.Push:
                // Hold still while a defender is in sight (the simulated player aims and shoots).
                if (defenders.Any(d => d.Alive && G.LineOfSight(G.View.Eye, d.Body.Head))) break;
                if (DevWalk(dt) || Now > devPushTargetT) StartPush();
                break;
        }
    }

    /// <summary>Path toward the nearest living defender (re-planned every few seconds or when reached).</summary>
    void StartPush()
    {
        var target = defenders.Where(d => d.Alive).OrderBy(d => d.Body.Feet.DistanceTo(PlayerFeet)).FirstOrDefault();
        if (target == null) return;
        var path = Nav()?.FindPath(PlayerFeet, target.Body.Feet);
        if (path == null) GD.Print($"[autothrow] no path from {PlayerFeet} to {target.Body.Feet}");
        devPath = path ?? new List<Vector3> { target.Body.Feet }; // walks until the defender is in sight
        devIdx = 0;
        devPushTargetT = Now + 3f;
        LookAlong(target.Body.Head - G.View.Eye);
    }

    void LookAlong(Vector3 dir)
    {
        dir = dir.Normalized();
        G.View.Yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z));
        G.View.Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f)));
    }

    readonly record struct DevPlan(Vector3 Aim, bool Primary, int Bars, int Bounces, float RecastAt, float EffectAfter, float Score);

    /// <summary>Simulates candidate casts and returns the one that would hurt the most defenders (null = nothing useful).</summary>
    DevPlan? DevBestThrow()
    {
        var eye = G.View.Eye;
        var aims = new List<Vector3>();
        var targets = new List<Vector3> { MapSpot.Choke + new Vector3(0, 1.5f, 0) };
        targets.AddRange(defenders.Where(d => d.Alive).Select(d => d.Body.Head));
        targets.AddRange(MapSpot.Flashes.Select(f => f.Pop));
        bool sova = Ability.Kind == UtilKind.ReconBolt;
        float[] yaws = sova ? new[] { -12f, 0f, 12f } : new[] { -30f, -15f, 0f, 15f, 30f };
        float[] pitches = sova ? new[] { 0f, 10f, 25f, 40f } : new[] { -8f, 0f, 10f, 22f, 35f };
        foreach (var t in targets)
        {
            var d = (t - eye).Normalized();
            float yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
            foreach (var dy in yaws)
                foreach (var dp in pitches)
                    aims.Add(PlayerView.Dir(yaw + dy, Mathf.Clamp(pitch + dp, -30f, 60f)));
        }

        var variants = new List<(bool Primary, int Bars, int Bounces, float Recast)>();
        switch (Ability.Kind)
        {
            case UtilKind.Curveball: case UtilKind.FlashDrive: variants.Add((true, 0, 0, -1)); variants.Add((false, 0, 0, -1)); break;
            case UtilKind.GuidingLight: foreach (var r in new[] { 0.45f, 0.75f, 1.05f, 1.4f }) variants.Add((true, 0, 0, r)); break;
            case UtilKind.Haunt: variants.Add((true, 0, 0, -1)); foreach (var r in new[] { 0.35f, 0.7f, 1.0f }) variants.Add((true, 0, 0, r)); break;
            case UtilKind.ReconBolt: for (int b = 0; b < 4; b++) for (int n = 0; n < 3; n++) variants.Add((true, b, n, -1)); break;
            default: variants.Add((true, 0, 0, -1)); break;
        }

        DevPlan? best = null;
        foreach (var aim in aims)
            foreach (var v in variants)
            {
                var (score, after) = PredictRobust(aim, v.Primary, v.Bars, v.Bounces, v.Recast);
                if (score <= 0f) continue;
                if (best == null || score > best.Value.Score + 1e-3f || (Mathf.Abs(score - best.Value.Score) < 1e-3f && after < best.Value.EffectAfter))
                    best = new DevPlan(aim, v.Primary, v.Bars, v.Bounces, v.Recast, after, score);
            }
        return best;
    }

    /// <summary>
    /// The search favours edge cases (a bolt that just clears a wall top), so a candidate scores its worst result over two
    /// time steps and ±0.5° of aim: the real frame rate and a tiny aim difference must not turn it into a dud.
    /// </summary>
    (float Score, float After) PredictRobust(Vector3 aim, bool primary, int bars, int bounces, float recastAt)
    {
        var (s, after) = Predict(aim, primary, bars, bounces, recastAt, 1f / 120f);
        if (s <= 0f) return (s, after);
        var right = aim.Cross(Vector3.Up);
        right = right.LengthSquared() < 1e-6f ? Vector3.Right : right.Normalized();
        s = Mathf.Min(s, Predict(aim, primary, bars, bounces, recastAt, 1f / 60f).Score);
        if (s > 0f) s = Mathf.Min(s, Predict((aim + right * 0.009f).Normalized(), primary, bars, bounces, recastAt, 1f / 120f).Score);
        if (s > 0f) s = Mathf.Min(s, Predict((aim - right * 0.009f).Normalized(), primary, bars, bounces, recastAt, 1f / 120f).Score);
        return (s, after);
    }

    /// <summary>Runs one cast without visuals: (score = blind seconds / affected defenders, seconds until it takes effect).</summary>
    (float Score, float After) Predict(Vector3 aim, bool primary, int bars, int bounces, float recastAt, float dt)
    {
        var u = CastAbility(primary, bars, bounces, aim, visual: false);
        if (u == null) return (0f, 0f);
        var input = new UtilInput(false, aim);
        var hitByParanoia = new HashSet<Defender>();
        for (float t = 0; t < 4f && !u.Done; t += dt)
        {
            if (recastAt >= 0 && t >= recastAt && u.CanRecast) u.Recast();
            u.Update(dt, input);
            if (u is Paranoia par)
                foreach (var d in defenders)
                    if (d.Alive && (par.Touches(d.Body.Head) || par.Touches(d.Chest))) hitByParanoia.Add(d);
            if (u is LeerEye { Active: true } eye)
            {
                int seen = defenders.Count(d => d.Alive && BlindModel.Eccentricity(ViewOf(d), eye.Pos, Aspect) <= 1f && G.LineOfSight(d.Body.Head, eye.Pos));
                return (seen, t);
            }
            foreach (var e in u.Events)
            {
                switch (e.Kind)
                {
                    case UtilEventKind.Pop:
                    {
                        float s = 0;
                        foreach (var d in defenders.Where(d => d.Alive))
                        {
                            var (sec, grade) = BlindModel.Evaluate(e.Value, ViewOf(d), e.At, G.LineOfSight(d.Body.Head, e.At), Aspect);
                            if (grade is BlindGrade.Flashed or BlindGrade.Partial) s += sec;
                        }
                        // Don't flash yourself: the view at the pop is roughly the throw direction.
                        var me = new PlayerView { Eye = G.View.Eye, Yaw = Mathf.RadToDeg(Mathf.Atan2(aim.X, -aim.Z)), Pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(aim.Y, -1, 1))) };
                        var (own, ownGrade) = BlindModel.Evaluate(e.Value, me, e.At, G.LineOfSight(G.View.Eye, e.At), Aspect);
                        if (ownGrade is BlindGrade.Flashed or BlindGrade.Partial) s -= 1.5f * own;
                        return (s, t);
                    }
                    case UtilEventKind.DizzyFire:
                        return (defenders.Count(d => d.Alive && d.Chest.DistanceTo(e.At) <= e.Value && G.LineOfSight(e.At, d.Chest)), t);
                    case UtilEventKind.Scan:
                        return (defenders.Count(d => d.Alive && d.Body.Head.DistanceTo(e.At) <= e.Value &&
                                                      (G.LineOfSight(e.At, d.Body.Head) || G.LineOfSight(e.At, d.Chest))), t);
                    case UtilEventKind.Fizzle:
                        return (0f, t);
                }
            }
            u.Events.Clear();
        }
        return (hitByParanoia.Count, 0.6f);
    }
}
