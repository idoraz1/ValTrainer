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
    int Tier { get; }
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
    bool LineOfSight(Vector3 a, Vector3 b);
    /// <summary>Distance to the first wall along the bullet being processed (walls stop bullets).</summary>
    float BulletWallDist { get; }
    /// <summary>Sensitivity override for this session (sens finder trials); null = the player's sens.</summary>
    float? TrialSens { get; set; }
    /// <summary>The sensitivity currently applied to mouse look.</summary>
    float CurrentSens { get; }
    /// <summary>Crosshair override for this session (crosshair finder trials); null = the player's crosshair.</summary>
    ValTrainer.Valorant.CrosshairSettings? TrialCrosshair { get; set; }
    /// <summary>Record a gameplay event for the aim coach (see <see cref="TelemetryEvent"/> for kinds).</summary>
    void Event(string kind, float a = 0, float b = 0);
    /// <summary>Mouse 1 currently held (tracking drills).</summary>
    bool TriggerHeld { get; }
    /// <summary>Mouse 1 was pressed this frame (edge; valid during Mode.Update).</summary>
    bool FirePressed { get; }
    /// <summary>Map chosen for map drills ("random" or a map key).</summary>
    string MapKey { get; }
}
