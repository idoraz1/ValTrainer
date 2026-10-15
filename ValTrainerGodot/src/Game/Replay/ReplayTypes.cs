using Godot;

namespace ValTrainer.Game.Replay;

// 3D Rewind: what a run looked like, so it can be re-rendered with puppets (no gameplay logic). Recorded by
// ReplayRecorder during the run, saved next to the .vtt as a .vtr sidecar (ReplayFile), played by ReplayStage.
// Camera yaw/pitch (with recoil) are NOT here: they come from RunTelemetry.Frames, recorded every rendered frame.
// Times are seconds since the run started (same clock as FrameSample.T).

public enum ReplaySupport : byte { None, Partial, Full }
public enum EntKind : byte { Bot = 1, Target = 2 }
[Flags] public enum PBits : byte { Cut = 1, AbilityInHand = 2, VmShown = 4, Scoped = 8, HasFocus = 16, Crouch = 32 }
[Flags] public enum BBits : byte { Cut = 1, Crouched = 2, Grounded = 4, Visible = 8, Aiming = 16 }
[Flags] public enum TBits : byte { Cut = 1, Visible = 2, Hot = 4, Flash = 8 }
/// <summary>BotHit: Arg = zone · BotDie: P = from-direction · BotShoot: P = target point ·
/// PlayerShot: Arg = shot index in RunTelemetry.Shots, P = end point, N = wall normal (zero = no wall) · Weapon: Arg = WeaponKind.</summary>
public enum RxKind : byte { BotHit, BotDie, BotShoot, PlayerShot, Weapon }

/// <summary>Player state the .vtt frames lack. <see cref="Cut"/>: don't interpolate from the previous sample.</summary>
public struct PlayerSample
{
    public float T;
    public Vector3 Eye, Focus;          // eye position; current aim target's world point (if HasFocus)
    public float VisualZoom, AdsBlend, Reload, Blind; // 1 = no zoom; 0..1; reload progress 0..1 (0 = not reloading); blind 0..1
    public PBits Bits;
}

public struct BotSample
{
    public float T;
    public Vector3 Feet, Vel;
    public float Facing, AimPitch;      // radians
    public BBits Bits;
}

public struct TargetSample
{
    public float T;
    public Vector3 Pos;
    public float Radius;                // metres
    public TBits Bits;
}

public sealed class EntityTrack
{
    public ushort Id;
    public EntKind Kind;
    public byte Variant;
    public float SpawnT, EndT = float.PositiveInfinity;
    public readonly List<BotSample> Bot = new();
    public readonly List<TargetSample> Tgt = new();
}

public readonly record struct ReplayEvent(float T, RxKind Kind, ushort Ent, Vector3 P, Vector3 N, int Arg);

public sealed class ReplayHeader
{
    public string Mode = "", MapKey = "range", Weapon = "", EnemyColor = "", TargetStyle = "", AppVersion = "";
    public uint MapHash;
    /// <summary>The range's extra boxes exactly as built (Min.xyz, Max.xyz), so tier or code changes don't matter.</summary>
    public float[][] ExtraSolids = Array.Empty<float[]>();
    public bool Outlines, Viewmodel, LeftHanded;
    public ReplaySupport Support;
}

public sealed class ReplayRecording
{
    public ReplayHeader Header = new();
    public float Duration;
    public readonly List<PlayerSample> Player = new();
    public readonly List<EntityTrack> Entities = new();
    public readonly List<ReplayEvent> Events = new();

    /// <summary>Player state at <paramref name="t"/>: linear between samples, never across a Cut.</summary>
    public PlayerSample PlayerAt(float t)
    {
        var l = Player;
        if (l.Count == 0) return new PlayerSample { T = t, VisualZoom = 1 };
        int i = Floor(l, t, s => s.T);
        if (i < 0) return l[0];
        if (i >= l.Count - 1) return l[^1];
        var a = l[i]; var b = l[i + 1];
        if ((b.Bits & PBits.Cut) != 0 || b.T <= a.T) return a;
        float f = Math.Clamp((t - a.T) / (b.T - a.T), 0f, 1f);
        var r = a;
        r.T = t;
        r.Eye = a.Eye.Lerp(b.Eye, f);
        r.Focus = a.Focus.Lerp(b.Focus, f);
        r.VisualZoom = Mathf.Lerp(a.VisualZoom, b.VisualZoom, f);
        r.AdsBlend = Mathf.Lerp(a.AdsBlend, b.AdsBlend, f);
        r.Reload = Mathf.Lerp(a.Reload, b.Reload, f);
        r.Blind = Mathf.Lerp(a.Blind, b.Blind, f);
        return r;
    }

    /// <summary>Bot state at <paramref name="t"/>; false outside [SpawnT, EndT) or with no samples yet.</summary>
    public bool BotAt(EntityTrack e, float t, out BotSample s)
    {
        s = default;
        var l = e.Bot;
        if (l.Count == 0 || t < e.SpawnT || t >= e.EndT) return false;
        int i = Floor(l, t, x => x.T);
        if (i < 0) { s = l[0]; return true; }
        if (i >= l.Count - 1) { s = l[^1]; return true; }
        var a = l[i]; var b = l[i + 1];
        if ((b.Bits & BBits.Cut) != 0 || b.T <= a.T) { s = a; return true; }
        float f = Math.Clamp((t - a.T) / (b.T - a.T), 0f, 1f);
        s = a;
        s.T = t;
        s.Feet = a.Feet.Lerp(b.Feet, f);
        s.Vel = a.Vel.Lerp(b.Vel, f);
        s.Facing = Mathf.LerpAngle(a.Facing, b.Facing, f);
        s.AimPitch = Mathf.Lerp(a.AimPitch, b.AimPitch, f);
        return true;
    }

    /// <summary>Target state at <paramref name="t"/>; false outside [SpawnT, EndT) or with no samples yet.</summary>
    public bool TargetAt(EntityTrack e, float t, out TargetSample s)
    {
        s = default;
        var l = e.Tgt;
        if (l.Count == 0 || t < e.SpawnT || t >= e.EndT) return false;
        int i = Floor(l, t, x => x.T);
        if (i < 0) { s = l[0]; return true; }
        if (i >= l.Count - 1) { s = l[^1]; return true; }
        var a = l[i]; var b = l[i + 1];
        if ((b.Bits & TBits.Cut) != 0 || b.T <= a.T) { s = a; return true; }
        float f = Math.Clamp((t - a.T) / (b.T - a.T), 0f, 1f);
        s = a;
        s.T = t;
        s.Pos = a.Pos.Lerp(b.Pos, f);
        s.Radius = Mathf.Lerp(a.Radius, b.Radius, f);
        return true;
    }

    /// <summary>Index of the first event with T ≥ <paramref name="t"/> (Events are sorted by time).</summary>
    public int FirstEventAtOrAfter(float t)
    {
        int lo = 0, hi = Events.Count;
        while (lo < hi) { int m = (lo + hi) >> 1; if (Events[m].T < t) lo = m + 1; else hi = m; }
        return lo;
    }

    /// <summary>Last index with key ≤ t (−1 = before the first).</summary>
    static int Floor<T>(List<T> l, float t, Func<T, float> key)
    {
        int lo = 0, hi = l.Count - 1, r = -1;
        while (lo <= hi) { int m = (lo + hi) >> 1; if (key(l[m]) <= t) { r = m; lo = m + 1; } else hi = m - 1; }
        return r;
    }
}
