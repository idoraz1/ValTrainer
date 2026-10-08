using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;

namespace ValTrainer.Game;

public enum HitZone { None = -1, Head = 0, Body = 1, Legs = 2 }

/// <summary>Player health/armor (Valorant: 100 HP + 50 heavy shields).</summary>
public sealed class PlayerState
{
    public float Hp = 100, Shield = 50;
    public bool Dead => Hp <= 0;
    public float LastHitAt = -99f;
    public Vector3 LastHitFrom;
    /// <summary>Spawn protection (Deathmatch): damage is ignored while the session clock is below this.</summary>
    public float ProtectedUntil = -1f;
    /// <summary>Agent drills: asked before a bot's hit is applied (damage, from); return true to absorb it (Iso's Double Tap
    /// shield, a dash that's hard to track). Owned by the mode; <see cref="Reset"/> keeps it.</summary>
    public Func<float, Vector3, bool>? Absorb;

    public void Reset() { Hp = 100; Shield = 50; LastHitAt = -99f; ProtectedUntil = -1f; }

    /// <summary>Heavy shields absorb damage first (simplified like the Range bots).</summary>
    public void Damage(float dmg, Vector3 from, float now)
    {
        Shield -= dmg;
        if (Shield < 0) { Hp += Shield; Shield = 0; }
        LastHitAt = now;
        LastHitFrom = from;
    }
}

/// <summary>Everything a training mode can ask the game for. Implemented by <see cref="GameSession"/>.</summary>
public interface IGame
{
    PlayerView View { get; }
    Mover Mover { get; }
    PlayerState Player { get; }
    /// <summary>Tier this run plays at (the session's <c>TierOverride</c> or the chosen tier, 0..4).</summary>
    int Tier { get; }
    /// <summary>Routine: adapt difficulty to keep the hit rate near <see cref="ValTrainer.Modes.Staircase.TargetHitRate"/>
    /// (modes with <c>SupportsAdaptive</c> read its level at every spawn). null = a normal fixed-tier run.</summary>
    ValTrainer.Modes.Staircase? Adaptive { get; }
    /// <summary>Routine: for the last N seconds of a timed run the bots get easier (deathmatch finisher). 0 = off.</summary>
    float EaseLastSeconds { get; }
    /// <summary>Length of this timed run in seconds (the mode's own, or a routine / dev override).</summary>
    float RunLength { get; }
    Color Enemy { get; }
    Random Rng { get; }
    /// <summary>Seconds since the round started running.</summary>
    float Now { get; }
    /// <summary>Parent node for anything the mode spawns.</summary>
    Node3D World { get; }
    /// <summary>Collision / line-of-sight boxes of the current environment.</summary>
    IReadOnlyList<Box> Solid { get; }
    WeaponDef? Weapon { get; }
    BlindOverlay Blind { get; }

    Target SpawnTarget(float radius);
    BotCharacter SpawnBot(Vector3 feet, bool crouched = false);
    FlashOrb SpawnOrb(Color color);
    void Despawn(Node node);

    void Sound(string name, float vol = 1f, float pitch = 1f);
    void SoundAt(string name, Vector3 pos, float maxRange = 40f, float floor = 0.45f, float vol = 1f);

    /// <summary>Deal damage to the player from a world position (bots shooting back).</summary>
    void DamagePlayer(float dmg, Vector3 from);
    void Banner(string text, Color color);
    /// <summary>Teleport the player (new round / new spot).</summary>
    void Respawn(Vector3 feet, float yaw);
    /// <summary>Clear view between two points: no map box in the way and no live smoke / gas or water wall
    /// (<see cref="Smokes"/>), so bots can't see through smokes either way.</summary>
    bool LineOfSight(Vector3 a, Vector3 b);
    /// <summary>Controller vision blockers in the world (smokes, walls). <see cref="LineOfSight"/> treats them as opaque;
    /// bullets pass through. Cleared when the drill restarts.</summary>
    IReadOnlyList<SmokeVolume> Smokes { get; }
    void AddSmoke(SmokeVolume smoke);
    void ClearSmokes();
    /// <summary>Distance to the first wall along the bullet being processed (walls stop bullets).</summary>
    float BulletWallDist { get; }
    /// <summary>Sensitivity override for this session (sens finder trials); null = the player's sens.</summary>
    float? TrialSens { get; set; }
    /// <summary>The sensitivity currently applied to mouse look.</summary>
    float CurrentSens { get; }
    /// <summary>Crosshair override for this session (crosshair finder trials); null = the player's crosshair.</summary>
    ValTrainer.Valorant.CrosshairSettings? TrialCrosshair { get; set; }
    /// <summary>
    /// Aim-down-sights override (crosshair finder ADS / sniper trials). null = normal (the player's alt fire);
    /// true = the gun stays aimed / scoped as if alt fire were held, with the normal raise (a bolt-action Operator
    /// scopes back in after each shot); false = never aim. Only guns with a zoom aim (see <see cref="WeaponDef.Zoom"/>).
    /// Resets to null when the drill restarts; set it in Setup or any time during the run.
    /// </summary>
    bool? ForcedScope { get; set; }
    /// <summary>
    /// Swaps the gun in hand mid-run (fresh magazine, the weapon's equip time, the viewmodel's raise; aiming drops).
    /// A drill normally picks its gun once with <c>TrainingMode.Weapon</c> (read at every restart); use this only to change
    /// it during a run. Telemetry keeps one weapon per run: it is relabelled to the new gun.
    /// </summary>
    void SwitchWeapon(WeaponKind kind);
    /// <summary>Record a gameplay event for the aim coach (see <see cref="TelemetryEvent"/> for kinds).</summary>
    void Event(string kind, float a = 0, float b = 0);
    /// <summary>Mouse 1 currently held (tracking drills).</summary>
    bool TriggerHeld { get; }
    /// <summary>Mouse 1 was pressed this frame (edge; valid during Mode.Update).</summary>
    bool FirePressed { get; }
    /// <summary>Map chosen for map drills ("random" or a map key).</summary>
    string MapKey { get; }

    // ---- Player movement: jumping and ability movement (MovementBody.cs). Only drills with Movement = true move. ----

    /// <summary>True while the player stands on something; false while jumping, falling or launched by an impulse.</summary>
    bool PlayerGrounded { get; }
    /// <summary>The player's vertical speed in m/s (up = positive, 0 on the ground).</summary>
    float PlayerVerticalSpeed { get; }
    /// <summary>
    /// Ability impulse (dash, updraft, satchel blast …). The horizontal velocity becomes <c>velocity.XZ</c> (pass
    /// <c>Mover.Vel</c> plus your Y to keep the current run). <c>velocity.Y &gt; 0</c> launches the player upward at that speed
    /// (works from the ground; doesn't fire <see cref="PlayerJumped"/>); <c>Y &lt; 0</c> sets a downward speed while airborne;
    /// <c>Y = 0</c> keeps the vertical speed. For <paramref name="steerLockSeconds"/> the movement keys don't steer and there's
    /// no friction (a dash keeps its exact speed; gravity still applies in the air). Speeds above the normal max aren't
    /// capped: afterwards the extra speed bleeds off (<see cref="MovementTuning.ImpulseDecayGround"/> /
    /// <see cref="MovementTuning.ImpulseDecayAir"/> m/s²) while the keys can only steer. A dash of d metres in t seconds:
    /// speed d / t with steerLockSeconds = t (plus a short slide-out as the extra speed decays).
    /// </summary>
    void PlayerImpulse(Vector3 velocity, float steerLockSeconds = 0f);
    /// <summary>Instantly moves the player's feet to <paramref name="feet"/> (teleports). Keeps the view angles and the
    /// horizontal velocity, resets falling (vertical speed 0). The feet snap onto floor up to
    /// <see cref="MovementTuning.SnapDown"/> below or a step (0.7 m) above; otherwise the player falls from rest.
    /// No wall check: pass a free spot. Fires no jump / land event.</summary>
    void TeleportPlayer(Vector3 feet);
    /// <summary>Multiplier on the player's max ground speeds (run, walk, crouch, ADS): &gt; 1 sprint, &lt; 1 slows.
    /// Acceleration and braking are unchanged. Resets to 1 when the drill restarts.</summary>
    float MoveSpeedScale { get; set; }
    /// <summary>Whether the jump key works. Default true, reset when the drill restarts (false in the rail-fenced range drills
    /// listed in <c>GameSession.NoJumpDrills</c>).</summary>
    bool JumpEnabled { get; set; }
    /// <summary>The player jumped with the jump key (raised before that frame's movement). Subscriptions end with the run.</summary>
    event Action? PlayerJumped;
    /// <summary>The player touched the ground after any airborne phase (jump, fall, impulse); <c>Mover.LastAir</c> has
    /// its air time, height and distance. Subscriptions end with the run.</summary>
    event Action? PlayerLanded;

    // ---- Agent abilities in hand (AbilitySession.cs) ----

    /// <summary>True while an ability is in the player's hand (see <see cref="SetAbilityInHand"/>).</summary>
    bool AbilityInHand { get; }
    /// <summary>
    /// Agent drills: <paramref name="inHand"/> = true puts an ability in the player's hand — mouse 1 / mouse 2 then belong to
    /// the ability (the gun neither fires nor aims down sights) and the gun viewmodel is put away. false draws the gun again:
    /// it can fire after <paramref name="drawSeconds"/> (-1 = the weapon's own equip time) and the viewmodel replays its
    /// raise. A restart puts the gun back in hand.
    /// </summary>
    void SetAbilityInHand(bool inHand, float drawSeconds = -1f);
}
