using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Maps;

namespace ValTrainer.Modes;

/// <summary>
/// A drill. The session owns the player, weapon, environment and HUD; the mode spawns targets/bots,
/// reacts to bullets and keeps score. Difficulty comes from <see cref="Difficulty"/> via <see cref="Tier"/>.
/// </summary>
public abstract class TrainingMode
{
    public abstract string Key { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string Category => "Aim";

    public virtual float Duration => 60f;
    public virtual bool Timed => true;
    public virtual bool Done => false;
    public virtual WeaponKind Weapon => WeaponKind.Vandal;
    /// <summary>Bottomless magazine: pure aim drills never make you reload.</summary>
    public virtual bool InfiniteAmmo => true;
    /// <summary>Reloads never run dry (bot drills keep real magazines + reloads but can't run out).</summary>
    public virtual bool InfiniteReserve => true;
    public virtual bool Movement => false;
    /// <summary>2D-only drill (reaction test): no world, no weapon.</summary>
    public virtual bool Is2D => false;
    public virtual bool UsesHeadshots => false;
    /// <summary>null = the shooting range.</summary>
    public virtual MapSpot? Map => null;
    public virtual Vector3 StartFeet => Vector3.Zero;
    public virtual float StartYaw => 0f;
    public virtual string? Subtitle => null;
    /// <summary>Extra solid boxes the mode adds to the range (cover, lanes, walls).</summary>
    public virtual IEnumerable<Box> ExtraSolids => Array.Empty<Box>();

    protected IGame G = null!;
    protected int Tier => G?.Tier ?? Main.I.Tier; // ExtraSolids/Map can be read before Begin()
    protected Random Rng => G.Rng;
    protected float Now => G.Now;

    public int Score, Shots, Hits, Kills, HeadHits, Deaths;

    /// <summary>What the player should be aiming at right now (recorded every frame for the aim coach).</summary>
    public virtual AimFocus? Focus => null;
    /// <summary>Zone the last bullet hit (set by modes in OnBullet; None = miss).</summary>
    public HitZone LastShotZone = HitZone.None;
    protected static int IdOf(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    protected void Event(string kind, float a = 0, float b = 0) => G.Event(kind, a, b);

    /// <summary>Angular distance (deg) from the crosshair to a point.</summary>
    protected float AngleFromCrosshair(Vector3 p) => G.View.AngleTo(p);

    /// <summary>Records that a bot became visible: crosshair placement error and its vertical part (+ = too high).</summary>
    protected void SeenEvent(Vector3 head)
    {
        var d = (head - G.View.Eye).Normalized();
        float pitchT = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        Event("bot_seen", G.View.AngleTo(head), G.View.ViewPitch - pitchT);
    }
    public readonly List<float> KillTimes = new();
    /// <summary>Kill banner state shown by the HUD: multi-kill count and remaining time.</summary>
    public (int Count, float Time) KillBanner;
    int streak;
    float lastKillAt = -99f;

    public void Begin(IGame game)
    {
        G = game;
        Setup();
    }

    protected virtual void Setup() { }

    /// <summary>Called every running frame (after movement, before bullets).</summary>
    public abstract void Update(float dt);

    /// <summary>
    /// A bullet was fired along <paramref name="dir"/>. Return the distance to whatever the mode's targets
    /// absorbed it at, or +inf if it missed everything (the session then places a wall impact).
    /// </summary>
    public virtual float OnBullet(Vector3 origin, Vector3 dir, bool accurate) => float.PositiveInfinity;

    /// <summary>The player was killed by a bot.</summary>
    public virtual void OnPlayerDied() { }

    /// <summary>Left-side HUD lines (state, prompts).</summary>
    public virtual IEnumerable<string> HudLines() => Array.Empty<string>();
    /// <summary>Centre prompt (e.g. "Keep strafing").</summary>
    public virtual string? Prompt => null;
    /// <summary>Custom 2D drawing on the HUD canvas (reaction test, etc.).</summary>
    public virtual void Draw2D(CanvasItem canvas, Vector2 size) { }

    public virtual float Accuracy => Shots == 0 ? 0 : (float)Hits / Shots;

    public virtual IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return ("Accuracy", $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (Kills > 0) yield return ("Kills", Kills.ToString());
        if (KillTimes.Count > 0) yield return ("Avg time to kill", $"{KillTimes.Average():0} ms");
        if (UsesHeadshots && Hits > 0) yield return ("Headshot %", $"{100f * HeadHits / Hits:0}%");
    }

    /// <summary>Extra buttons on the results screen (e.g. the sens finder's "Use this sens").</summary>
    public virtual IEnumerable<(string Label, Action Act)> ResultButtons() => Array.Empty<(string, Action)>();
    /// <summary>False for drills whose score/badge mean nothing (Sens Finder): the results screen hides them.</summary>
    public virtual bool ShowScore => true;

    /// <summary>Which rank tier this result matches (-1 = below Rookie).</summary>
    public virtual int Badge() => -1;

    protected static float Median(IReadOnlyList<float> v)
    {
        if (v.Count == 0) return float.PositiveInfinity;
        var s = v.OrderBy(x => x).ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2f;
    }

    public RunRecord MakeRecord(float sens) => new()
    {
        Mode = Key,
        When = DateTime.Now,
        Score = Score,
        Accuracy = Accuracy,
        Hits = Hits,
        Shots = Shots,
        Kills = Kills,
        AvgKillMs = KillTimes.Count > 0 ? KillTimes.Average() : 0,
        HeadshotPct = UsesHeadshots && Hits > 0 ? (float)HeadHits / Hits : -1,
        Sens = sens,
        Tier = Tier,
        Badge = Badge(),
        Map = Map?.Key ?? "",
    };

    protected float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    /// <summary>Records a kill: stats, multi-kill sound and banner like Valorant (rising pitch 1–5).</summary>
    protected void RegisterKill(float spawnTime, bool headshot)
    {
        Kills++;
        KillTimes.Add((Now - spawnTime) * 1000f);
        Event("kill", KillTimes[^1], headshot ? 1 : 0);
        streak = Now - lastKillAt < 5f ? Math.Min(5, streak + 1) : 1;
        lastKillAt = Now;
        G.Sound("kill" + streak, 0.9f);
        KillBanner = (streak, 1.4f);
    }

    public void TickBanner(float dt)
    {
        if (KillBanner.Time > 0) KillBanner.Time -= dt;
    }

    /// <summary>Lowest pitch that keeps a target fully above the floor.</summary>
    protected static float MinPitch(float eyeY, float dist, float radius) =>
        Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp((radius + 0.2f - eyeY) / dist, -1f, 1f)));
}
