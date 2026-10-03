using System.Globalization;
using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game;

/// <summary>
/// The session's player-movement API (<see cref="IGame"/>: jumping, ability impulses, teleports, speed scale) and its
/// dev hooks (--movescript, --movelog, --movecheck, --jumpaudit). The physics itself is <see cref="Mover"/> (Mover.cs,
/// MovementBody.cs).
/// </summary>
public partial class GameSession
{
    public bool PlayerGrounded => Mover.Grounded;
    public float PlayerVerticalSpeed => Mover.VelY;
    public float MoveSpeedScale { get => Mover.SpeedScale; set => Mover.SpeedScale = value; }
    public bool JumpEnabled { get => Mover.JumpEnabled; set => Mover.JumpEnabled = value; }
    public event Action? PlayerJumped;
    public event Action? PlayerLanded;

    /// <summary>The player's feet (bottom of the body; the eye is <c>Mover.EyeHeight</c> above).</summary>
    public Vector3 PlayerFeet => new(View.Eye.X, feetY, View.Eye.Z);

    public void PlayerImpulse(Vector3 velocity, float steerLockSeconds = 0f) => Mover.Impulse(velocity, steerLockSeconds, PlayerFeet);

    public void TeleportPlayer(Vector3 feet)
    {
        Mover.Place(ref feet, solid);
        feetY = feet.Y;
        View.Eye = new Vector3(feet.X, feetY + Mover.EyeHeight, feet.Z);
        UpdateCamera();
    }

    /// <summary>Range drills whose strafing lanes are fenced by low rails (0.8–1.2 m) that a jump would clear: no jumping
    /// there, so the drill stays what it is (verified with --jumpaudit counterstrafe / peekduel).</summary>
    public static readonly HashSet<string> NoJumpDrills = new() { "counterstrafe", "peekduel" };

    /// <summary>Called by Restart right after the new run's Mover is made: the previous drill's event subscriptions end,
    /// the Mover's events are forwarded, dev scripts are installed.</summary>
    void WireMover()
    {
        PlayerJumped = null;
        PlayerLanded = null;
        Mover.Jumped += OnMoverJumped;
        Mover.Landed += OnMoverLanded;
        Mover.JumpEnabled = !NoJumpDrills.Contains(Mode.Key);
        Mover.KeyOverride = null;
        if (Main.I.Dev) DevMovementSetup();
    }

    void OnMoverJumped()
    {
        Event("jump", Mover.Speed);
        Sfx.I.Play("footstep", 0.3f, 1.08f);
        PlayerJumped?.Invoke();
    }

    void OnMoverLanded()
    {
        var a = Mover.LastAir;
        Event("land", a.AirTime * 1000f, a.Drop);
        Sfx.I.Play("footstep", Mathf.Clamp(0.3f + 0.12f * a.Drop, 0.3f, 0.8f), 0.92f);
        if (MovementScript.Log)
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"[move] land t={Now:0.000} {(a.FromJump ? "jump" : "fall/launch")}: air {a.AirTime:0.000} s, feet rise {a.FeetRise:0.000} m, " +
                $"eye rise {a.EyeRise:0.000} m, drop {a.Drop:0.000} m, distance {a.Distance:0.00} m{(Mover.HitCeiling ? ", hit a ceiling" : "")} " +
                $"(flat-ground jump: apex {MovementTuning.JumpApex:0.000} m, air {MovementTuning.JumpAirTime:0.000} s)"));
        float dmg = MovementTuning.FallDamage(a.Drop);
        if (dmg > 0) FallDamage(dmg);
        PlayerLanded?.Invoke();
    }

    /// <summary>Fall damage hits health only (shields don't absorb it), like VALORANT.</summary>
    void FallDamage(float dmg)
    {
        if (Player.Dead || Now < Player.ProtectedUntil) return;
        Player.Hp -= dmg;
        Sfx.I.Play("damage", 0.8f);
        Event("fall_damage", dmg);
        if (!Player.Dead) return;
        Event("died");
        Sfx.I.Play("death");
        Mode.OnPlayerDied();
    }

    /// <summary>Dev runs: --movecheck / --jumpaudit run their checks and quit; --movescript installs scripted keys.</summary>
    void DevMovementSetup()
    {
        if (MovementCheck.Requested)
        {
            int failures = MovementCheck.Run(Main.I.Valorant);
            GetTree().Quit(failures);
            return;
        }
        var spec = MovementScript.Spec;
        if (spec == null || MovementScript.Auto) return;
        var script = MovementScript.Parse(spec, Main.I.Valorant, out var error);
        if (error != null) GD.PushWarning($"--movescript {error}");
        if (script == null) return;
        Mover.KeyOverride = k => script.Down(k, Now);
        GD.Print($"[move] scripted keys: {script}");
    }
}
