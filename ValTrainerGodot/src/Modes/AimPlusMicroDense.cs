using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// Microshot Dense (suggested on Discord): two small heads on screen at once, packed close together. Hitting one
/// respawns it a short hop from the other, so every shot is a tiny, precise correction between two nearby targets
/// (Microshot's skill, Gridshot's rhythm). Heads are smaller than real size (r 0.10 m vs 0.14 m) at 12–18 m. Static
/// targets with Focus + target_spawn / target_hit like Microshot, so the coach can read it.
/// </summary>
public sealed class MicroshotDenseMode : TrainingMode
{
    public override string Key => "microshot_dense";
    public override IReadOnlyCollection<WeaponKind>? WeaponChoice => WeaponSets.NoShotguns;
    public override string Name => L.N("Microshot Dense");
    public override string Description => L.N("Two small heads close together: hit one and it reappears right next to the other. Tiny corrections at speed. Higher tiers: smaller gaps, farther heads.");
    public override string Category => L.N("Aim");
    public override string SimKind => "static";

    const float Radius = 0.10f;
    readonly Target?[] heads = new Target?[2];
    readonly int[] ids = new int[2];
    readonly Vector2[] dir = new Vector2[2];   // (yaw, pitch) of each head from the eye, deg
    int nextId = 1;
    float lastHit, lastMs = -1;

    /// <summary>Distance of the heads (m).</summary>
    static (float Min, float Max) Dist(int t) => t switch
    { 0 => (12f, 13f), 1 => (12f, 14f), 2 => (13f, 15f), 3 => (14f, 16.5f), _ => (15f, 18f) };
    /// <summary>Gap from the other head to a new one (deg, centre to centre).</summary>
    static (float Min, float Max) Gap(int t) => t switch
    { 0 => (1.6f, 2.8f), 1 => (1.4f, 2.6f), 2 => (1.3f, 2.4f), 3 => (1.2f, 2.2f), _ => (1.1f, 2.0f) };
    /// <summary>The pair stays in this patch of the wall (deg from straight ahead).</summary>
    const float PatchYaw = 10f, PatchPitchMin = -2f, PatchPitchMax = 7f;

    public override AimFocus? Focus
    {
        get
        {
            int best = -1; float bestA = float.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                if (heads[k] == null) continue;
                float a = AngleFromCrosshair(heads[k]!.GlobalPosition);
                if (a < bestA) { bestA = a; best = k; }
            }
            return best < 0 ? null : new AimFocus(ids[best], heads[best]!.GlobalPosition, heads[best]!.Radius);
        }
    }

    protected override void Setup()
    {
        for (int k = 0; k < 2; k++) heads[k] = G.SpawnTarget(Radius);
        Put(0, new Vector2(-0.9f, 2f));
        Put(1, new Vector2(0.9f, 2f));
        lastHit = Now;
    }

    void Put(int k, Vector2 yp)
    {
        var (dMin, dMax) = Dist(Tier);
        float d = R(dMin, dMax);
        float lo = Mathf.Max(PatchPitchMin, MinPitch(G.View.Eye.Y, d, Radius));
        yp = new Vector2(Mathf.Clamp(yp.X, -PatchYaw, PatchYaw), Mathf.Clamp(yp.Y, lo, PatchPitchMax));
        dir[k] = yp;
        heads[k]!.Position = G.View.Eye + PlayerView.Dir(yp.X, yp.Y) * d;
        heads[k]!.SpawnTime = Now;
        ids[k] = nextId++;
        Event("target_spawn", ids[k], AngleFromCrosshair(heads[k]!.Position));
    }

    /// <summary>Respawn head k a short hop from the other head, never overlapping it, drifting toward the patch centre at the edges.</summary>
    void Respawn(int k)
    {
        var other = dir[1 - k];
        var (gMin, gMax) = Gap(Tier);
        Vector2 best = other + new Vector2(gMin, 0);
        for (int i = 0; i < 24; i++)
        {
            float g = R(gMin, gMax), th = R(0, Mathf.Tau);
            var c = other + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * g;
            if (Mathf.Abs(c.X) <= PatchYaw && c.Y >= PatchPitchMin && c.Y <= PatchPitchMax) { best = c; break; }
            // at the edge: lean toward the centre of the patch
            var toC = new Vector2(0, (PatchPitchMin + PatchPitchMax) / 2) - other;
            best = other + (toC.Length() > 1e-3f ? toC.Normalized() : Vector2.Right) * g;
        }
        Put(k, best);
    }

    public override void Update(float dt) { }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        int best = -1;
        float bestT = float.PositiveInfinity;
        for (int k = 0; k < 2; k++)
        {
            float t = heads[k]!.Ray(o, d);
            if (t < bestT && t < G.BulletWallDist) { bestT = t; best = k; }
        }
        if (best < 0 && IsDoubleClick()) return float.PositiveInfinity;
        Shots++;
        if (best < 0) { Score -= 20; return float.PositiveInfinity; }
        Hits++; Kills++; MarkHit();
        LastShotZone = HitZone.Head;
        float ms = (Now - lastHit) * 1000f;
        lastHit = Now; lastMs = ms;
        KillTimes.Add(ms);
        Event("target_hit", ids[best], ms);
        Score += 100 + (int)Mathf.Max(0, (600 - ms) / 4);
        G.Sound("head", 0.8f);
        heads[best]!.Hit();
        Respawn(best);
        return bestT;
    }

    public override string? Prompt => Now < 2.5f && Hits == 0 ? L.N("Two small heads, close together: tiny moves, then click") : null;

    public override IEnumerable<string> HudLines()
    {
        if (lastMs >= 0) yield return L.F($"Last head {lastMs:0} ms");
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Accuracy"), $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        yield return (L.N("Median time between hits"), KillTimes.Count > 0 ? L.F($"{Median(KillTimes):0} ms") : "—");
        if (DoubleClicks > 0) yield return (L.N("Double-clicks ignored"), DoubleClicks.ToString());
    }

    /// <summary>Median time between hits (needs ≥ 70% accuracy), thresholds per tier played.</summary>
    public override int Badge() => Accuracy < 0.7f || KillTimes.Count == 0 ? -1 : Difficulty.BadgeLower(Median(KillTimes), Difficulty.MicroDenseBadges[Tier]);
}
