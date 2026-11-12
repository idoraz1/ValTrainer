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
    public virtual string Category => L.N("Aim");
    // Name, Description and Category stay English: they are keys (logic, telemetry, stats, Discord). Show these instead.
    /// <summary>The drill's name in the UI language.</summary>
    public string DisplayName => L.T(Name);
    /// <summary>The drill's description in the UI language. Override it (with <c>L.F</c>) when <see cref="Description"/>
    /// is interpolated: an interpolated string has no catalogue key.</summary>
    public virtual string DisplayDescription => L.T(Description);
    /// <summary>The category's name in the UI language (<see cref="Category"/> itself is a logic key).</summary>
    public string DisplayCategory => L.T(Category);
    /// <summary>GitHub handle of the community member who made this drill: a COMMUNITY tag on its card and a credit
    /// line on its countdown. Null = made by the ValTrainer team.</summary>
    public virtual string? MadeBy => null;

    public virtual float Duration => 60f;
    public virtual bool Timed => true;
    public virtual bool Done => false;
    /// <summary>The drill's own gun (its default): what it plays when nothing is picked. None = no gun (2D, tracking).</summary>
    public virtual WeaponKind Weapon => WeaponKind.Vandal;
    /// <summary>The guns the player may pick for this drill (a <see cref="WeaponSets"/> set); null = locked to
    /// <see cref="Weapon"/> (no picker). Default: locked.</summary>
    public virtual IReadOnlyCollection<WeaponKind>? WeaponChoice => null;
    /// <summary>The guns whose runs count for badges and the coach's rank estimate (both are calibrated on the drill's own
    /// gun): <see cref="Weapon"/>, plus the Phantom where that is the Vandal (and the other way round), so the two rifles
    /// are equal. Other picks still get a score, a personal best per gun and a review. Same rule as
    /// <see cref="WeaponRating.RatedFor"/>, which the coach uses with only the mode key.</summary>
    public virtual IReadOnlyCollection<WeaponKind> RatedWeapons => WeaponRating.RatedFor(Weapon);
    /// <summary>A run with <paramref name="gun"/> counts for badges and the coach's rank (see <see cref="RatedWeapons"/>).</summary>
    public bool IsRated(WeaponKind gun) => gun == Weapon || RatedWeapons.Contains(gun);
    /// <summary>The player's pick, set by the session at every (re)start before <see cref="Attach"/>; null = the default.
    /// Ignored unless it is in <see cref="WeaponChoice"/> (or <see cref="ForcePick"/>).</summary>
    public WeaponKind? Pick { get; set; }
    /// <summary>Dev "--gun": the pick applies even if the drill is locked (never for a drill without a gun).</summary>
    public bool ForcePick { get; set; }
    /// <summary>The gun this run actually plays: the pick if it is allowed, else <see cref="Weapon"/>. Everything that sets up
    /// or records the gun (the session's equip, telemetry, the Rewind header, the run record) reads this, not Weapon.</summary>
    public WeaponKind ActiveWeapon => Pick is { } p && p != Weapon && Weapon != WeaponKind.None && Weapons.Get(p) != null
        && (ForcePick || WeaponChoice?.Contains(p) == true) ? p : Weapon;
    /// <summary>Bottomless magazine: pure aim drills never make you reload.</summary>
    public virtual bool InfiniteAmmo => true;
    /// <summary>Reloads never run dry (bot drills keep real magazines + reloads but can't run out).</summary>
    public virtual bool InfiniteReserve => true;
    public virtual bool Movement => false;
    /// <summary>2D-only drill (reaction test): no world, no weapon.</summary>
    public virtual bool Is2D => false;
    /// <summary>The drill preview can show the simulated player playing it (false for drills that need a person, and for
    /// drills on a VALORANT map: their preview has no live demo, see <see cref="HasPreview"/>).</summary>
    public virtual bool PreviewDemo => !Is2D && Map == null;
    /// <summary>Clicking the card opens the drill preview first. Drills on a VALORANT map (the Map category, Flash Dodge,
    /// Sound Lock) start right away unless they offer a gun choice: then the preview opens without a live demo (a demo would
    /// build the whole map twice, popup + START, about double the video memory) so it can show the gun picker.</summary>
    public virtual bool HasPreview => Map == null || WeaponChoice != null;
    /// <summary>A gun picked mid-run from the buy menu comes out on your next life instead of restarting the run (drills
    /// where you die and respawn, like Deathmatch; the drill calls <see cref="Game.GameSession.ApplyNextLifeGun"/>).</summary>
    public virtual bool GunOnRespawn => false;
    public virtual bool UsesHeadshots => false;
    /// <summary>null = the shooting range.</summary>
    public virtual MapSpot? Map => null;
    public virtual Vector3 StartFeet => Vector3.Zero;
    public virtual float StartYaw => 0f;
    public virtual string? Subtitle => null;
    /// <summary>A HUD line starting with this (English, untranslated) is hidden on movement drills, which show the last
    /// stop under the speed meter. Return such lines from <see cref="HudLines"/> untranslated (the HUD translates them).</summary>
    public const string LastStopLine = "Last stop";
    /// <summary>The drill can adapt its difficulty per spawn to the session's <see cref="IGame.Adaptive"/> staircase.</summary>
    public virtual bool SupportsAdaptive => false;
    /// <summary>How this drill ends kills, for the skin finisher rules (skins2-design §4.5): target drills (no bot dies) and
    /// the measuring tools (Sens / Crosshair Finder) by default; <see cref="BotMode"/> sorts the bot drills into round
    /// drills, Deathmatch and one-bot rep drills.</summary>
    public virtual Game.Skins.KillDrill KillDrill =>
        Key is "sensfinder" or "xhairfinder" ? Game.Skins.KillDrill.Measuring : Game.Skins.KillDrill.Target;
    /// <summary>Skin effects may play in this drill: kill effects, finishers, banner, counter, sound layers and tinted tracers
    /// (the gun's look itself always shows). False for 2D drills and the measuring tools, whose trials must look and sound
    /// identical.</summary>
    public virtual bool SkinFxAllowed => !Is2D && KillDrill != Game.Skins.KillDrill.Measuring;
    /// <summary>Extra solid boxes the mode adds to the range (cover, lanes, walls).</summary>
    public virtual IEnumerable<Box> ExtraSolids => Array.Empty<Box>();

    protected IGame G = null!;
    protected int Tier => G?.Tier ?? Main.I.Tier; // ExtraSolids/Map can be read before Begin()
    /// <summary>The session's adaptive staircase when this drill supports it (null = a normal fixed-tier run).</summary>
    protected Staircase? Adapt => SupportsAdaptive ? G?.Adaptive : null;
    protected Random Rng => G.Rng;
    protected float Now => G.Now;

    public int Score, Shots, Hits, Kills, HeadHits, Deaths;

    /// <summary>What the player should be aiming at right now (recorded every frame for the aim coach).</summary>
    public virtual AimFocus? Focus => null;
    /// <summary>Zone the last bullet hit (set by modes in OnBullet; None = miss).</summary>
    public HitZone LastShotZone = HitZone.None;
    /// <summary>Set by a click drill in OnBullet when the bullet was an accidental double-click right after a hit
    /// (see <see cref="IsDoubleClick"/>): the session then leaves it out of the telemetry and the Rewind.</summary>
    public bool LastShotIgnored;
    /// <summary>Accidental double-clicks ignored this run (shown on the results screen).</summary>
    public int DoubleClicks;
    /// <summary>A shot this soon after a hit can't be aimed at the next target (human reaction time is ≥ ~150 ms),
    /// so a miss inside it is a double-click or a held trigger, not a real miss (reported on Discord).</summary>
    public const float DoubleClickGrace = 0.15f;
    float lastHitAt = float.NegativeInfinity;
    /// <summary>Click drills: call on every hit.</summary>
    protected void MarkHit() => lastHitAt = Now;
    /// <summary>Click drills: call on a miss before penalising it. True = forgive it (not a shot, no penalty).</summary>
    protected bool IsDoubleClick()
    {
        if (Now - lastHitAt >= DoubleClickGrace) return false;
        DoubleClicks++; LastShotIgnored = true;
        Event("double_click");
        return true;
    }
    protected static int IdOf(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    protected void Event(string kind, float a = 0, float b = 0) => G.Event(kind, a, b);

    /// <summary>Angular distance (deg) from the crosshair to a point.</summary>
    protected float AngleFromCrosshair(Vector3 p) => G.View.AngleTo(p);

    /// <summary>Largest angle (deg) between the crosshair and an enemy's head at first sight that still counts as crosshair
    /// placement. Enemies further out (beside or behind you) are a positioning problem, not placement: not graded.</summary>
    public const float SeenMaxDeg = 60f;

    /// <summary>Records that a bot became visible: crosshair placement error and its vertical part (+ = too high).
    /// Sightings beyond <see cref="SeenMaxDeg"/> are not recorded.</summary>
    protected void SeenEvent(Vector3 head)
    {
        if (G.View.AngleTo(head) > SeenMaxDeg) return;
        var d = (head - G.View.Eye).Normalized();
        float pitchT = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(d.Y, -1, 1)));
        Event("bot_seen", G.View.AngleTo(head), G.View.ViewPitch - pitchT);
    }
    public readonly List<float> KillTimes = new();
    /// <summary>Kill banner state shown by the HUD: multi-kill count and remaining time.</summary>
    public (int Count, float Time) KillBanner;
    int streak;
    float lastKillAt = -99f;

    /// <summary>Binds the mode to its session before the environment is built, so <see cref="ExtraSolids"/> and
    /// <see cref="Map"/> already see the session's tier (a routine's tier override).</summary>
    public void Attach(IGame game) => G = game;

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
    public virtual IEnumerable<string> HudLines() => Adapt is { } a ? new[] { AdaptiveLine(a) } : Array.Empty<string>();
    protected static string AdaptiveLine(Staircase a) => L.F($"Level {Difficulty.DisplayLevelName(a.Level)} ({a.Level:0.0}) · adapts to {a.TargetHitRate * 100:0}% hits");
    /// <summary>Centre prompt (e.g. "Keep strafing").</summary>
    public virtual string? Prompt => null;
    /// <summary>Custom 2D drawing on the HUD canvas (reaction test, etc.).</summary>
    public virtual void Draw2D(CanvasItem canvas, Vector2 size) { }

    public virtual float Accuracy => Shots == 0 ? 0 : (float)Hits / Shots;

    public virtual IEnumerable<(string Label, string Value)> ResultLines()
    {
        yield return (L.N("Accuracy"), $"{Accuracy * 100:0.0}%  ({Hits}/{Shots})");
        if (Kills > 0) yield return (L.N("Kills"), Kills.ToString());
        if (KillTimes.Count > 0) yield return (L.N("Avg time to kill"), L.F($"{KillTimes.Average():0} ms"));
        if (UsesHeadshots && Hits > 0) yield return (L.N("Headshot %"), $"{100f * HeadHits / Hits:0}%");
        if (DoubleClicks > 0) yield return (L.N("Double-clicks ignored"), DoubleClicks.ToString());
        if (AdaptiveResult() is { } ar) yield return ar;
    }

    /// <summary>Adaptive runs: the level the player held at the target hit rate.</summary>
    protected (string Label, string Value)? AdaptiveResult()
    {
        if (Adapt is not { } a) return null;
        float lv = a.Threshold >= 0 ? a.Threshold : a.Level;
        return (L.F($"Level at {a.TargetHitRate * 100:0}% hits"), L.F($"{Difficulty.DisplayLevelName(lv)} ({lv:0.0}) · {a.HitRate * 100:0}% of {a.Trials}"));
    }

    /// <summary>Extra buttons on the results screen (e.g. the sens finder's "Use this sens").</summary>
    public virtual IEnumerable<(string Label, Action Act)> ResultButtons() => Array.Empty<(string, Action)>();
    /// <summary>False for drills whose score/badge mean nothing (Sens Finder): the results screen hides them.</summary>
    public virtual bool ShowScore => true;
    /// <summary>Dev-only hint for the simulated player (--simaim): "static" (still targets), "bot" (agents that move),
    /// "track" (hold on a moving target) or "spray". Empty = guessed from the mode key.</summary>
    public virtual string SimKind => "";

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
        Weapon = Weapon == WeaponKind.None ? null : ActiveWeapon.ToString(),
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
        G.KillSound(streak, 0.9f); // factory kill1..kill5, or the skin's escalating set after its own kill
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
