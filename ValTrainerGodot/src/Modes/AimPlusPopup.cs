using Godot;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Modes;

/// <summary>
/// Pop-up Reflex: a real-size head pops up somewhere on the screen for a short window (≈ 700 ms at Rookie → 320 ms at
/// Pro, plus 12 ms per degree it is away from your crosshair), then vanishes. See it, flick, click before it's gone.
/// Measures hits, time to hit, reaction to the first move (when your crosshair starts toward the head) and misses.
/// Pause between pop-ups is random so they can't be timed. Static targets: Focus + target_spawn / target_hit / expired.
/// </summary>
public sealed class PopupMode : TrainingMode
{
    public override string Key => "popup";
    public override string Name => "Pop-up Reflex";
    public override string Description => "Heads pop up anywhere on screen for a split second (700 ms at Rookie, 320 ms at Pro, a little longer when far): see, flick, click.";
    public override string Category => "Aim";
    public override string SimKind => "static";
    public override AimFocus? Focus => up ? new AimFocus(pid, head.GlobalPosition, head.Radius) : null;

    Target head = null!;
    bool up;
    int pid, shown, expired, misses;
    float upAt, window, nextAt = 0.6f, lastMs = -1;
    // reaction to first move: crosshair speed toward the head
    Vector2 toHead;                       // unit, crosshair → head at the pop-up
    bool movingAtPop, onsetDone;
    readonly List<float> onsets = new();
    readonly List<(float T, float Yaw, float Pitch)> hist = new();

    protected override void Setup()
    {
        head = G.SpawnTarget(0.14f);
        head.Visible = false;
        head.Position = new Vector3(0, -5, 0);
    }

    void PopUp()
    {
        var (dMin, dMax) = AimPlusTiers.PopupDistance(Tier);
        var (pMin, pMax) = AimPlusTiers.PopupPitch(Tier);
        float yr = AimPlusTiers.PopupYaw(Tier), d = R(dMin, dMax);
        float lo = Mathf.Max(pMin, MinPitch(G.View.Eye.Y, d, 0.14f));
        var pos = Vector3.Zero;
        float a = 0;
        for (int i = 0; i < 40; i++)
        {
            pos = G.View.Eye + PlayerView.Dir(R(-yr, yr), R(lo, pMax)) * d;
            a = AngleFromCrosshair(pos);
            if (a >= AimPlusTiers.PopupMinAngle) break;
        }
        head.Position = pos;
        head.Visible = true;
        head.SpawnTime = Now;
        up = true;
        upAt = Now;
        window = AimPlusTiers.PopupWindow(Tier) + AimPlusTiers.PopupPerDeg * a;
        pid++;
        shown++;
        var e = AimPlusMath.CrossMinus(G.View, pos);
        toHead = e.Length() > 1e-3f ? -e.Normalized() : Vector2.Zero;
        movingAtPop = CrossSpeed(out _) > 20f;
        onsetDone = false;
        Event("target_spawn", pid, a);
    }

    void Down()
    {
        up = false;
        head.Visible = false;
        nextAt = Now + R(AimPlusTiers.PopupGapMin, AimPlusTiers.PopupGapMax);
    }

    /// <summary>Crosshair angular speed (deg/s) over the last ~25 ms and its direction.</summary>
    float CrossSpeed(out Vector2 dir)
    {
        dir = Vector2.Zero;
        if (hist.Count < 2) return 0;
        var b = hist[^1];
        int i = hist.Count - 2;
        while (i > 0 && b.T - hist[i].T < 0.025f) i--;
        var a = hist[i];
        float dt = b.T - a.T;
        if (dt < 0.004f) return 0;
        var v = new Vector2(Mathf.Wrap(b.Yaw - a.Yaw, -180f, 180f), b.Pitch - a.Pitch) / dt;
        float s = v.Length();
        if (s > 1e-3f) dir = v / s;
        return s;
    }

    public override void Update(float dt)
    {
        hist.Add((Now, G.View.ViewYaw, G.View.ViewPitch));
        while (hist.Count > 2 && Now - hist[0].T > 0.1f) hist.RemoveAt(0);

        if (!up)
        {
            if (Now >= nextAt) PopUp();
            return;
        }
        // Reaction to the first move: the crosshair starts toward the head (≥ 20°/s, within 60° of its direction).
        if (!onsetDone && !movingAtPop && toHead != Vector2.Zero)
        {
            float s = CrossSpeed(out var dir);
            if (s >= 20f && dir.Dot(toHead) >= 0.5f)
            {
                onsetDone = true;
                float ms = (Now - upAt) * 1000f;
                if (ms >= 80f) onsets.Add(ms); // faster than 80 ms = you were already moving / guessing
            }
        }
        if (Now - upAt > window)
        {
            expired++;
            Score -= 10;
            Event("target_expired", pid);
            Down();
        }
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        float t = up ? head.Ray(o, d) : float.PositiveInfinity;
        if (float.IsPositiveInfinity(t) || t > G.BulletWallDist) { misses++; Score -= 15; return float.PositiveInfinity; }
        float ms = (Now - upAt) * 1000f;
        Hits++; Kills++;
        LastShotZone = HitZone.Head;
        KillTimes.Add(ms);
        lastMs = ms;
        Event("target_hit", pid, ms);
        Score += 100 + (int)(100 * Mathf.Clamp(1f - ms / (window * 1000f), 0f, 1f));
        G.Sound("head", 0.8f);
        Down();
        return t;
    }

    float HitPct => shown == 0 ? 0 : 100f * Hits / Mathf.Max(1, Resolved);
    /// <summary>Pop-ups that are over (hit or vanished): the one on screen when time runs out doesn't count against you.</summary>
    int Resolved => Hits + expired;

    public override string? Prompt => shown == 0 ? "Heads pop up anywhere — hit them before they vanish" : null;

    public override IEnumerable<string> HudLines()
    {
        yield return $"Window {AimPlusTiers.PopupWindow(Tier) * 1000:0} ms + travel";
        if (Resolved > 0) yield return $"Hit {Hits} / {Resolved} pop-ups";
        if (lastMs >= 0) yield return $"Last hit {lastMs:0} ms";
    }

    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Pop-ups hit", Resolved == 0 ? "—" : $"{HitPct:0}%  ({Hits}/{Resolved})");
        yield return ("Median time to hit", KillTimes.Count > 0 ? $"{Median(KillTimes):0} ms" : "—");
        yield return ("Reaction (first move)", onsets.Count > 0 ? $"{Median(onsets):0} ms" : "—");
        yield return ("Missed shots", misses.ToString());
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
    }

    /// <summary>% of pop-ups hit (needs ≥ 50% accuracy and 8 pop-ups); at most one badge above the tier played.</summary>
    public override int Badge() => Resolved < 8 || Accuracy < 0.5f ? -1 : Difficulty.BadgeHigher(HitPct, Difficulty.PopupBadges[Tier]);
}
