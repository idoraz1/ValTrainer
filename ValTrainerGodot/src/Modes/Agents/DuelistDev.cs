using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// DEV-ONLY auto-play for headless validation of the Mobility Entry drills ("--dev --autoability", usually with
/// "--simaim good"): the simulated player only aims and shoots, so every round this walks the player with the real movement
/// (scripted movement keys through <see cref="Mover.KeyOverride"/>) to the last spot before the choke no defender can
/// see, lets the agent's kit do its entry (<see cref="DuelistKit.DevEntry"/>: prime + dash, sprint + slide, satchel boost,
/// tether + teleport …), then pushes toward the nearest defender and stops whenever one is in sight. The kits log the
/// measured distances / times ("[duelist] …") and audit every dash / teleport end. Not used with --movescript.
/// </summary>
public sealed class DuelistDev
{
    public static bool Requested => CmdLine.Dev && CmdLine.Has("--autoability");

    static DuelistDev? current;
    static bool installed;

    readonly IGame G;
    readonly MobilityEntryMode M;
    readonly HashSet<InputBinding> keys = new();
    readonly bool walks;
    enum St { ToSpot, Entry, Push }
    St st;
    List<Vector3> path = new();
    int idx;
    float pushReplanAt, stuckCheckAt;
    Vector3 stuckFrom;
    BotNav? nav;

    /// <summary>Kit scratch: the entry step and the seconds spent in it.</summary>
    public int Step;
    public float StepT;

    public DuelistDev(IGame g, MobilityEntryMode m)
    {
        G = g;
        M = m;
        if (!installed && Mover.KeyOverride == null)
        {
            // Only while that run's session exists: a later run in the same dev process gets the real keyboard back.
            Mover.KeyOverride = k => current != null && GodotObject.IsInstanceValid(current.G as GodotObject)
                ? current.keys.Contains(k)
                : k.RealHeld;
            installed = true;
        }
        current = this;
        walks = installed;
        if (!walks) GD.Print("[duelist] --autoability: movement keys are scripted elsewhere (--movescript); abilities only");
    }

    BotNav? Nav() => nav ??= BotNav.For($"{M.Spot.Key}:{G.Solid.Count}", G.Solid, M.Spot.StartFeet);

    Vector3 Feet => M.PlayerFeet;

    public void Next(int step = -1) { Step = step >= 0 ? step : Step + 1; StepT = 0; }
    public void Hold(InputBinding k) { if (!k.IsNone) keys.Add(k); }
    public void Release(InputBinding k) => keys.Remove(k);

    /// <summary>A point on the site side of the choke, at eye height.</summary>
    public Vector3 SitePoint(float beyond) => M.Spot.Choke + M.SiteAxis * beyond + Vector3.Up * PlayerView.EyeHeight;

    public void LookFlat(Vector3 p)
    {
        var d = p - G.View.Eye;
        if (new Vector2(d.X, d.Z).LengthSquared() < 1e-4f) return;
        G.View.Yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z));
    }

    public void Pitch(float deg) => G.View.Pitch = deg;

    /// <summary>The flat direction into the site with the longest clear run for a body (up to <paramref name="len"/> m):
    /// toward the choke or points past it. Used to aim dashes so the logged distance is a clean measurement.</summary>
    public Vector3 BestDashDir(float len)
    {
        var feet = Feet;
        var cands = new List<Vector3> { M.Spot.Choke };
        for (float b = 2f; b <= 10f; b += 2f)
            for (float s = -3f; s <= 3f; s += 1.5f)
                cands.Add(M.Spot.Choke + M.SiteAxis * b + new Vector3(-M.SiteAxis.Z, 0, M.SiteAxis.X) * s);
        Vector3 best = M.SiteAxis;
        float bestRun = -1f;
        foreach (var c in cands)
        {
            var d = c - feet; d.Y = 0;
            if (d.LengthSquared() < 0.25f) continue;
            d = d.Normalized();
            var side = new Vector3(-d.Z, 0, d.X) * 0.34f;
            float run = len;
            foreach (var o in new[] { Vector3.Zero, side, -side })
                foreach (var h in new[] { 0.8f, 1.6f })
                {
                    var from = feet + o + Vector3.Up * h;
                    run = Mathf.Min(run, Mathf.Max(0f, Collision.FirstHit(from, d, G.Solid) - 0.35f));
                }
            // Prefer runs that end on the site side of the choke.
            float score = run + 0.3f * (feet + d * run - M.Spot.Choke).Dot(M.SiteAxis);
            if (score > bestRun) { bestRun = score; best = d; }
        }
        return best;
    }

    /// <summary>Looks along a flat direction (yaw only).</summary>
    public void LookDir(Vector3 d) { if (new Vector2(d.X, d.Z).LengthSquared() > 1e-6f) G.View.Yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)); }

    public void NewRound()
    {
        keys.Clear();
        st = St.ToSpot;
        Step = 0;
        StepT = 0;
        traceAt = 0f;
        GD.Print($"[duelist] round {M.RoundIndex} ({M.Spot.Map} {M.Spot.Name}, {M.LiveDefenders.Count()} defenders)");
        var path0 = Nav()?.FindPath(M.Spot.StartFeet, M.Spot.Choke) ?? new List<Vector3> { M.Spot.Choke };
        // Walk the path in 0.5 m steps and stop at the last spot no defender can see (head, chest, shoulders).
        var pts = new List<Vector3> { M.Spot.StartFeet };
        var prev = M.Spot.StartFeet;
        foreach (var p in path0)
        {
            float len = prev.DistanceTo(p);
            for (float s = 0.5f; s < len; s += 0.5f) pts.Add(prev.Lerp(p, s / len));
            pts.Add(p);
            prev = p;
        }
        int last = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var eye = pts[i] + new Vector3(0, PlayerView.EyeHeight, 0);
            var parts = new[]
            {
                eye, eye + Vector3.Down * 0.45f,
                eye + new Vector3(0.35f, -0.3f, 0), eye + new Vector3(-0.35f, -0.3f, 0),
                eye + new Vector3(0, -0.3f, 0.35f), eye + new Vector3(0, -0.3f, -0.35f),
            };
            if (M.LiveDefenders.Any(b => parts.Any(p => G.LineOfSight(b.Head, p)))) break;
            last = i;
        }
        path = pts.Take(Math.Max(1, last - 1) + 1).ToList();
        GD.Print($"[duelist] auto: nav path to the choke {path0.Count} points, hidden spot {path[^1]} ({path.Count} steps)");
        idx = 0;
        stuckCheckAt = -1f;
        M.Kit.DevRoundStart(this);
    }

    float traceAt;

    public void Update(float dt)
    {
        if (G.Player.Dead) { keys.Clear(); return; }
        if (CmdLine.Has("--duelisttrace") && M.RoundTimeNow >= traceAt)
        {
            traceAt = M.RoundTimeNow + 2f;
            GD.Print($"[duelist] trace t={M.RoundTimeNow:0.0} st={st} feet={Feet} keys={string.Join('+', keys)} visible={M.AnyDefenderVisible} " +
                     $"seen={M.AnyDefenderSeesPlayer} shots={M.Shots} hits={M.Hits} hp={G.Player.Hp:0} wp={idx}/{path.Count}");
        }
        switch (st)
        {
            case St.ToSpot:
                if (M.RoundTimeNow < 0.3f) break;
                if (!walks || Walk(stopWhenSeen: false)) { keys.Clear(); st = St.Entry; Step = 0; StepT = 0; }
                break;

            case St.Entry:
                StepT += dt;
                if (M.Kit.DevEntry(this, dt) || StepT > 6f) { st = St.Push; StartPush(); }
                break;

            case St.Push:
                StepT += dt;
                M.Kit.DevPush(this, dt);
                if (M.Kit.Dashing || M.Kit.DevDrives) break;
                if (!walks) break;
                if (Walk(stopWhenSeen: true) || M.RoundTimeNow > pushReplanAt) StartPush();
                break;
        }
    }

    /// <summary>Path toward the nearest living defender (re-planned every few seconds or when reached).</summary>
    void StartPush()
    {
        var feet = Feet;
        var target = M.LiveDefenders.OrderBy(b => b.Feet.DistanceTo(feet)).FirstOrDefault();
        if (target == null) return;
        var p = Nav()?.FindPath(feet, target.Feet);
        if (p == null) GD.Print($"[duelist] auto: no path from {feet} to {target.Feet}");
        path = p ?? new List<Vector3> { target.Feet };
        idx = 0;
        pushReplanAt = M.RoundTimeNow + 3f;
        stuckCheckAt = -1f;
    }

    /// <summary>One frame along <see cref="path"/> with the forward key; true when the end is reached.</summary>
    bool Walk(bool stopWhenSeen)
    {
        var fwd = Main.I.Valorant.KeyForward;
        if (stopWhenSeen && M.AnyDefenderVisible) { keys.Remove(fwd); stuckCheckAt = -1f; return false; }
        var feet = Feet;
        while (idx < path.Count && new Vector2(path[idx].X - feet.X, path[idx].Z - feet.Z).Length() < 0.45f) idx++;
        if (idx >= path.Count) { keys.Remove(fwd); return true; }
        LookFlat(path[idx] + Vector3.Up * PlayerView.EyeHeight);
        keys.Add(fwd);
        // Stuck on a corner: hop to the next waypoint (logged; the real movement stays in charge otherwise).
        if (stuckCheckAt < 0) { stuckCheckAt = M.RoundTimeNow + 1f; stuckFrom = feet; }
        else if (M.RoundTimeNow >= stuckCheckAt)
        {
            if (feet.DistanceTo(stuckFrom) < 0.3f && !M.Kit.Dashing)
            {
                GD.Print($"[duelist] auto-walk stuck at {feet}; skipping to the next waypoint");
                G.TeleportPlayer(path[idx]);
                idx++;
            }
            stuckCheckAt = -1f;
        }
        return false;
    }
}
