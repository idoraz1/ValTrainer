using System.Globalization;
using Godot;
using ValTrainer.Audio;
using ValTrainer.Game.Fx;

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
        ownStepT = 0.06f;
        if (Main.I.Dev && !Demo) DevMovementSetup(); // never in a drill preview (--movecheck quits the app)
    }

    /// <summary>Seconds to the player's next own running footstep.</summary>
    float ownStepT;

    /// <summary>
    /// Your own footsteps, as in VALORANT: running on the ground is audible (quieter than an enemy's steps, 2D), walking
    /// (shift), crouching and the air are silent. Same cadence as the enemies' (<see cref="FootstepFx.StepInterval"/>);
    /// a landing counts as a footfall, so the next step comes a full step after it. Muted in previews (Play2D).
    /// </summary>
    void OwnFootsteps(float dt)
    {
        var m = Mover;
        if (!m.Grounded) { ownStepT = FootstepFx.StepInterval(m.Speed); return; }
        if (m.Walking || m.Crouching || m.SteerLockLeft > 0 || m.Speed <= m.WalkMax + 0.05f) { ownStepT = 0.06f; return; }
        ownStepT -= dt;
        if (ownStepT > 0) return;
        ownStepT += FootstepFx.StepInterval(m.Speed);
        Play2D("footstep", 0.35f, 0.92f + 0.16f * Random.Shared.NextSingle());
    }

    void OnMoverJumped()
    {
        Event("jump", Mover.Speed);
        // No takeoff sound: the real game is silent at jump start (standing or crouched).
        PlayerJumped?.Invoke();
    }

    void OnMoverLanded()
    {
        var a = Mover.LastAir;
        Event("land", a.AirTime * 1000f, a.Drop);
        // Landing audio is a binary cue: high drops and regular jumps are audible, but low walk-offs and
        // the released crouch-jump technique are silent.
        if (Mover.AudibleLanding)
            Play2D("footstep", 0.8f, 0.92f);
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
        Play2D("damage", 0.8f);
        Event("fall_damage", dmg);
        if (!Player.Dead) return;
        Event("died");
        Play2D("death");
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
