using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>3 targets on a 3×3 grid. Tier changes target size and grid spacing (research-calibrated).</summary>
public sealed class GridshotMode : TrainingMode
{
    readonly int[] ids = new int[3];
    int nextId = 1;
    public override AimFocus? Focus
    {
        get
        {
            int best = -1; float bestA = float.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                if (targets[k] == null) continue;
                float a = AngleFromCrosshair(targets[k]!.GlobalPosition);
                if (a < bestA) { bestA = a; best = k; }
            }
            return best < 0 ? null : new AimFocus(ids[best], targets[best]!.GlobalPosition, radius);
        }
    }

    public override string Key => "gridshot";
    public override string Name => "Gridshot";
    public override string Description => "3 targets on a 3x3 grid. Warm-up for speed and click timing.";

    readonly int[] active = new int[3];
    readonly Target?[] targets = new Target?[3];
    float radius, spacing, lastHit;

    Vector3 Cell(int i) => new((i % 3 - 1) * spacing, PlayerView.EyeHeight + (1 - i / 3) * spacing, -9f);

    public override IEnumerable<Box> ExtraSolids => new[] { new Box(new Vector3(-2.6f, 0f, -9.9f), new Vector3(2.6f, 4.4f, -9.75f)) };

    protected override void Setup()
    {
        radius = Difficulty.GridshotRadius(Tier);
        spacing = Difficulty.GridshotSpacing(Tier);
        var cells = Enumerable.Range(0, 9).OrderBy(_ => Rng.Next()).Take(3).ToArray();
        for (int k = 0; k < 3; k++)
        {
            active[k] = cells[k];
            targets[k] = G.SpawnTarget(radius);
            targets[k]!.Position = Cell(cells[k]);
            ids[k] = nextId++;
        }
    }

    public override void Update(float dt) { }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            float t = targets[k]!.Ray(o, d);
            if (t < bestD && t < G.BulletWallDist) { bestD = t; best = k; }
        }
        if (best < 0) { Score -= 25; return float.PositiveInfinity; }
        Hits++;
        LastShotZone = HitZone.Body;
        Score += 100;
        KillTimes.Add((Now - lastHit) * 1000f);
        Event("target_hit", ids[best], KillTimes[^1]);
        lastHit = Now;
        G.Sound("body");
        int old = active[best], n;
        do n = Rng.Next(9); while (n == old || active.Contains(n));
        active[best] = n;
        targets[best]!.Position = Cell(n);
        ids[best] = nextId++;
        Event("target_spawn", ids[best], AngleFromCrosshair(targets[best]!.GlobalPosition));
        targets[best]!.Hit();
        return bestD;
    }

    public override int Badge() => Difficulty.BadgeHigher(Score, Difficulty.GridshotBadges[Tier]);
}

/// <summary>One head-sized target at tier-dependent distance/angle; expires at higher tiers.</summary>
public sealed class FlickMode : TrainingMode
{
    int fid;
    public override AimFocus? Focus => target != null ? new AimFocus(fid, target.GlobalPosition, target.Radius) : null;

    public override string Key => "flick";
    public override string Name => "Head Flicks";
    public override string Description => "One real-size head (r 0.14 m). Flick, click, repeat. Higher tiers: farther, wider, timed.";
    public override bool UsesHeadshots => false;

    Target target = null!;
    int expired;

    protected override void Setup()
    {
        target = G.SpawnTarget(0.14f);
        Spawn();
    }

    void Spawn()
    {
        var (dMin, dMax) = Difficulty.FlickDistance(Tier);
        float yawRange = Difficulty.FlickYaw(Tier), dist = R(dMin, dMax);
        float yaw = 0, pitch = 0;
        for (int i = 0; i < 20; i++)
        {
            yaw = R(-yawRange, yawRange);
            pitch = R(Mathf.Max(-4, MinPitch(G.View.Eye.Y, dist, 0.14f)), 7);
            if (Mathf.Abs(yaw - G.View.Yaw) + Mathf.Abs(pitch - G.View.Pitch) > 10) break;
        }
        target.Position = G.View.Eye + PlayerView.Dir(yaw, pitch) * dist;
        target.SpawnTime = Now;
        fid++;
        Event("target_spawn", fid, AngleFromCrosshair(target.Position));
    }

    public override void Update(float dt)
    {
        float life = Difficulty.FlickLifetime(Tier);
        if (life > 0 && Now - target.SpawnTime > life) { expired++; Score -= 30; G.Sound("fail", 0.5f); Event("target_expired", fid); Spawn(); }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        float t = target.Ray(o, d);
        if (float.IsPositiveInfinity(t)) { Score -= 20; return t; }
        float ms = (Now - target.SpawnTime) * 1000f;
        Hits++; Kills++;
        LastShotZone = HitZone.Head;
        KillTimes.Add(ms);
        Event("target_hit", fid, ms);
        Score += 100 + (int)Mathf.Max(0, (900 - ms) / 6);
        G.Sound("head");
        Spawn();
        return t;
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        yield return ("Median time to hit", KillTimes.Count > 0 ? $"{Median(KillTimes):0} ms" : "—");
        if (Difficulty.FlickLifetime(Tier) > 0) yield return ("Expired targets", expired.ToString());
    }

    public override int Badge() => Accuracy < 0.7f || KillTimes.Count == 0 ? -1 : Difficulty.BadgeLower(Median(KillTimes), Difficulty.FlickBadges[Tier]);
}

/// <summary>Centre target, then a random one. Trains flicking out and back.</summary>
public sealed class SpidershotMode : TrainingMode
{
    int sid = 1;
    public override AimFocus? Focus => target != null ? new AimFocus(sid, target.GlobalPosition, radius) : null;

    public override string Key => "spider";
    public override string Name => "Spidershot";
    public override string Description => "Centre target, then a random one. Flick out and back; higher tiers are smaller and wider.";

    bool center = true;
    Target target = null!;
    float radius;
    int expired;

    protected override void Setup()
    {
        radius = Difficulty.SpiderRadius(Tier);
        target = G.SpawnTarget(radius);
        target.Position = new Vector3(0, PlayerView.EyeHeight, -10);
    }

    public override void Update(float dt)
    {
        float life = Difficulty.SpiderLifetime(Tier);
        if (!center && life > 0 && Now - target.SpawnTime > life) { expired++; Score -= 30; Event("target_expired", sid); Next(); }
    }

    void Next()
    {
        center = !center;
        var (yr, pr) = Difficulty.SpiderRange(Tier);
        target.Position = center ? new Vector3(0, PlayerView.EyeHeight, -10)
            : G.View.Eye + PlayerView.Dir(R(-yr, yr), R(Mathf.Max(-pr, MinPitch(G.View.Eye.Y, 10f, radius)), pr)) * 10f;
        target.SpawnTime = Now;
        sid++;
        Event("target_spawn", sid, AngleFromCrosshair(target.Position));
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        float t = target.Ray(o, d);
        if (float.IsPositiveInfinity(t)) { Score -= 20; return t; }
        Hits++;
        LastShotZone = HitZone.Body;
        KillTimes.Add((Now - target.SpawnTime) * 1000f);
        Event("target_hit", sid, KillTimes[^1]);
        Score += 100;
        G.Sound("body");
        Next();
        return t;
    }

    public override int Badge() => Difficulty.BadgeHigher(Score, Difficulty.SpiderBadges[Tier]);
}

/// <summary>Hold Mouse 1 on a target that A-D strafes like a player (no instant direction flips).</summary>
public sealed class TrackingMode : TrainingMode
{
    public override AimFocus? Focus => target != null ? new AimFocus(1, target.GlobalPosition, target.Radius) : null;

    public override string Key => "tracking";
    public override string Name => "Strafe Tracking";
    public override string Description => "Hold Mouse 1 on a target that A-D strafes like a real player (realistic stops and reversals).";
    public override WeaponKind Weapon => WeaponKind.None;

    Target target = null!;
    float vx, targetVx, nextTurn, held, onTarget, crouchT;
    bool onNow;

    protected override void Setup()
    {
        target = G.SpawnTarget(Difficulty.TrackRadius(Tier));
        target.Position = new Vector3(0, PlayerView.EyeHeight, -11);
        targetVx = Difficulty.TrackSpeed(Tier);
    }

    public override void Update(float dt)
    {
        nextTurn -= dt;
        if (nextTurn <= 0)
        {
            var (a, b) = Difficulty.TurnInterval(Tier);
            float speed = Difficulty.TrackSpeed(Tier);
            if (Tier == 4 && Rng.NextDouble() < 0.15) speed = 6.75f; // Pro: occasional knife-speed burst
            targetVx = Rng.NextDouble() < Difficulty.StopChance(Tier) ? 0 : (Rng.Next(2) == 0 ? -1 : 1) * speed;
            if (Rng.NextDouble() < Difficulty.StopChance(Tier) * 0.5) crouchT = 0.5f;
            nextTurn = R(a, b);
        }
        vx = Mathf.MoveToward(vx, targetVx, Difficulty.StrafeDecel * dt);
        var p = target.Position;
        p.X += vx * dt;
        if (Mathf.Abs(p.X) > 6) { p.X = Mathf.Sign(p.X) * 6; targetVx = -targetVx; vx = 0; }
        crouchT -= dt;
        p.Y = PlayerView.EyeHeight - (crouchT > 0 ? 0.5f : 0f);
        target.Position = p;

        onNow = !float.IsPositiveInfinity(target.Ray(G.View.Eye, G.View.Forward));
        target.SetHot(onNow && G.TriggerHeld);
        if (G.TriggerHeld)
        {
            held += dt;
            if (onNow) { onTarget += dt; if ((int)(onTarget * 10) != (int)((onTarget - dt) * 10)) G.Sound("tick", 0.15f, 1.6f); }
        }
        Score = (int)(onTarget * 100);
    }

    public override float Accuracy => held <= 0 ? 0 : onTarget / held;

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("On target while firing", $"{Accuracy * 100:0.0}%");
        yield return ("Time on target", $"{onTarget:0.0} s of {Duration:0} s");
    }

    public override int Badge()
    {
        float a = Accuracy;
        if (a >= 0.58f) return Math.Min(4, Tier + 1);
        if (a >= 0.45f) return Tier;
        if (a >= 0.35f) return Tier - 1;
        return -1;
    }
}

/// <summary>Click the moment the screen flashes. 10 trials; early clicks count as fails.</summary>
public sealed class ReactionMode : TrainingMode
{
    public override string Key => "reaction";
    public override string Name => "Reaction Test";
    public override string Description => "Click the moment the screen flashes. 10 trials; early clicks count as fails.";
    public override string Category => "Other";
    public override bool Timed => false;
    public override bool Is2D => true;
    public override WeaponKind Weapon => WeaponKind.None;
    public override bool Done => KillTimes.Count >= Trials;

    const int Trials = 10;
    enum St { Wait, Go, Show, Early }
    St st;
    float t, goAt, lastMs;
    ulong goUsec;
    int early;

    protected override void Setup() => Next();

    void Next() { st = St.Wait; t = 0; goAt = R(1.2f, 3.8f); }

    public override void Update(float dt)
    {
        t += dt;
        if (st == St.Wait && t >= goAt) { st = St.Go; t = 0; goUsec = Time.GetTicksUsec(); }
        else if (st is St.Show or St.Early && t > 1.1f && !Done) Next();
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        if (st == St.Wait) { st = St.Early; t = 0; early++; G.Sound("fail"); Event("early_click"); }
        else if (st == St.Go)
        {
            lastMs = (Time.GetTicksUsec() - goUsec) / 1000f;
            KillTimes.Add(lastMs);
            Event("reaction", lastMs);
            Hits++; Shots++;
            st = St.Show; t = 0;
            G.Sound("body");
            Score = (int)Mathf.Max(0, 1000 - KillTimes.Average()) - early * 20;
        }
        return float.PositiveInfinity;
    }

    public override float Accuracy => Hits + early == 0 ? 0 : (float)Hits / (Hits + early);

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Average reaction", $"{KillTimes.Average():0} ms");
        yield return ("Best", $"{KillTimes.Min():0} ms");
        yield return ("Early clicks", early.ToString());
    }

    public override int Badge() => KillTimes.Count == 0 ? -1 : Difficulty.BadgeLower(KillTimes.Average(), Difficulty.ReactionMs);

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        var bg = st switch { St.Go => G.Enemy, St.Early => new Color(0.24f, 0.12f, 0.12f), _ => new Color(0.09f, 0.12f, 0.16f) };
        c.DrawRect(new Rect2(Vector2.Zero, size), bg);
        string big = st switch { St.Wait => "Wait for it…", St.Go => "CLICK!", St.Show => $"{lastMs:0} ms", _ => "Too early!" };
        var col = st == St.Go ? Colors.Black : UI.UiTheme.Text;
        float k = size.Y / 1080f;
        void Center(string s, float y, int fs, Color cc)
        {
            var w = UI.UiTheme.Display.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X;
            c.DrawString(UI.UiTheme.Display, new Vector2(size.X / 2 - w / 2, y), s, HorizontalAlignment.Left, -1, fs, cc);
        }
        Center(big, size.Y / 2 + 20 * k, (int)(96 * k), col);
        Center($"Trial {Math.Min(KillTimes.Count + 1, Trials)} / {Trials}", size.Y / 2 + 90 * k, (int)(30 * k), st == St.Go ? Colors.Black : UI.UiTheme.Dim);
        if (KillTimes.Count > 0) Center($"Average {KillTimes.Average():0} ms", size.Y / 2 + 130 * k, (int)(26 * k), st == St.Go ? Colors.Black : UI.UiTheme.Dim);
    }
}
