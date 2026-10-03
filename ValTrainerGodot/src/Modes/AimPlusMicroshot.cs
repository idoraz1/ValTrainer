using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// Microshot: one real-size head (r 0.14 m) at a time, each new one 1.5–6° from the last at 10–20 m. Trains small,
/// precise corrections: stop on the head, then click. Tracks overshoot (the crosshair travelling past the far edge of
/// the head before the hit). Static targets, coach-friendly like Head Flicks (Focus + target_spawn / target_hit).
/// </summary>
public sealed class MicroshotMode : TrainingMode
{
    public override string Key => "microshot";
    public override string Name => "Microshot";
    public override string Description => "Real-size heads 1.5–6° apart at 10–20 m: small, precise corrections without overshooting. Higher tiers: farther, wider apart, timed.";
    public override string Category => "Aim";
    public override string SimKind => "static";
    public override AimFocus? Focus => target != null ? new AimFocus(fid, target.GlobalPosition, target.Radius) : null;

    Target target = null!;
    int fid, expired;
    float yaw, pitch, rDeg;              // current head direction from the eye (deg) and its angular radius
    float lastMs = -1;
    // overshoot: did the crosshair pass the far edge of the head (along the flick) before the hit?
    Vector2 flickDir;                    // unit, from the crosshair at spawn toward the head; zero = no flick needed
    bool overNow;
    int overs, judged;                   // targets that overshot / targets with a measurable flick that were hit

    protected override void Setup()
    {
        target = G.SpawnTarget(0.14f);
        yaw = 0f; pitch = 2f;            // first head just above the centre
        Place(yaw, pitch);
        flickDir = Vector2.Zero;         // placed before the view is reset for the run: no flick to judge
    }

    void Place(float y, float p)
    {
        var (dMin, dMax) = AimPlusTiers.MicroDistance(Tier);
        float d = R(dMin, dMax);
        yaw = y; pitch = p;
        target.Position = G.View.Eye + PlayerView.Dir(yaw, pitch) * d;
        target.SpawnTime = Now;
        rDeg = AimPlusMath.RadiusDeg(target.Radius, d);
        fid++;
        var e = AimPlusMath.CrossMinus(G.View, target.Position);
        float a = e.Length();
        flickDir = a > rDeg ? -e / a : Vector2.Zero;
        overNow = false;
        Event("target_spawn", fid, AngleFromCrosshair(target.Position));
    }

    /// <summary>Next head: 1.5–6° (tier) from the previous one, inside a small patch so the drill never wanders off.</summary>
    void Next()
    {
        var (oMin, oMax) = AimPlusTiers.MicroOffset(Tier);
        float d = target.Position.DistanceTo(G.View.Eye);
        float lo = Mathf.Max(AimPlusTiers.MicroPitchMin, MinPitch(G.View.Eye.Y, Mathf.Max(d, 10f), 0.14f));
        float ny = yaw, np = pitch;
        bool ok = false;
        for (int i = 0; i < 24 && !ok; i++)
        {
            float off = R(oMin, oMax), th = R(0, Mathf.Tau);
            ny = yaw + Mathf.Cos(th) * off;
            np = pitch + Mathf.Sin(th) * off;
            ok = Mathf.Abs(ny) <= AimPlusTiers.MicroYaw && np >= lo && np <= AimPlusTiers.MicroPitchMax;
        }
        if (!ok)
        {
            // Near the edge of the patch: step toward its centre instead.
            var c = new Vector2(0, (lo + AimPlusTiers.MicroPitchMax) / 2) - new Vector2(yaw, pitch);
            var dir = c.Length() > 1e-3f ? c.Normalized() : Vector2.Right;
            float off = R(oMin, oMax);
            ny = yaw + dir.X * off; np = pitch + dir.Y * off;
        }
        Place(ny, np);
    }

    public override void Update(float dt)
    {
        if (flickDir != Vector2.Zero && !overNow)
        {
            var e = AimPlusMath.CrossMinus(G.View, target.Position);   // crosshair − head
            if (e.Dot(flickDir) > rDeg + 0.1f) overNow = true;          // past the far edge along the flick
        }
        float life = AimPlusTiers.MicroLifetime(Tier);
        if (life > 0 && Now - target.SpawnTime > life)
        {
            expired++;
            Score -= 30;
            G.Sound("fail", 0.5f);
            Event("target_expired", fid);
            Next();
        }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        float t = target.Ray(o, d);
        if (float.IsPositiveInfinity(t) || t > G.BulletWallDist) { Score -= 20; return float.PositiveInfinity; }
        float ms = (Now - target.SpawnTime) * 1000f;
        Hits++; Kills++;
        LastShotZone = HitZone.Head;
        KillTimes.Add(ms);
        lastMs = ms;
        if (flickDir != Vector2.Zero) { judged++; if (overNow) overs++; }
        Event("target_hit", fid, ms);
        Score += 100 + (int)Mathf.Max(0, (700 - ms) / 4);
        G.Sound("head", 0.8f);
        target.Hit();
        Next();
        return t;
    }

    float OverPct => judged == 0 ? 0 : 100f * overs / judged;

    public override string? Prompt => Now < 2.5f && Hits == 0 ? "Small moves: stop ON the head, then click" : null;

    public override IEnumerable<string> HudLines()
    {
        if (lastMs >= 0) yield return $"Last head {lastMs:0} ms";
        if (judged >= 3) yield return $"Overshoot {OverPct:0}% of heads";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        yield return ("Median time to hit", KillTimes.Count > 0 ? $"{Median(KillTimes):0} ms" : "—");
        if (judged > 0) yield return ("Overshoot", $"{OverPct:0}% of heads ({overs}/{judged}) — lower is better");
        if (AimPlusTiers.MicroLifetime(Tier) > 0) yield return ("Expired heads", expired.ToString());
    }

    /// <summary>Median time to hit (needs ≥ 70% accuracy), thresholds per tier played.</summary>
    public override int Badge() => Accuracy < 0.7f || KillTimes.Count == 0 ? -1 : Difficulty.BadgeLower(Median(KillTimes), Difficulty.MicroshotBadges[Tier]);
}
