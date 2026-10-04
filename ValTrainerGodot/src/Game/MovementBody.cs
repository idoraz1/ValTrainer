using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// The numbers behind jumping, falling and ability movement (metres, seconds). VALORANT runs on Unreal Engine 4's
/// character movement. Riot publishes no jump numbers, so the jump uses Unreal's stock jump height (JumpZVelocity
/// 420 cm/s at 980 cm/s² = 0.90 m apex), which matches the documented spots where ~1 m boxes need a crouch-jump, with the
/// trainer's existing gravity (unchanged falls), so a jump lasts 0.60 s. Crouch-jumps follow Unreal (the capsule shrinks
/// around its centre in the air). Weapon numbers are Riot's: airborne error per weapon (WeaponDef.AirErr: rifles 10°,
/// Sheriff 7°, Operator 15°) and the patch 1.09 landing penalty (7° for 0.225 s). Fall damage per the official wiki.
/// </summary>
public static class MovementTuning
{
    /// <summary>Gravity for jumps and falls (m/s²). The trainer's tuned fall speed, kept as it was.</summary>
    public const float Gravity = 20f;
    /// <summary>Take-off speed of a jump (m/s): apex = JumpSpeed² / 2g = 0.90 m, time in the air = 2·JumpSpeed / g = 0.60 s.</summary>
    public const float JumpSpeed = 6.0f;
    /// <summary>Air control (m/s²): the movement keys steer you while airborne (turning keeps your speed) and can brake,
    /// but never add speed beyond your take-off speed or the run speed: no bunny-hop gain.</summary>
    public const float AirAccel = 20f;
    /// <summary>Crouching in the air pulls the legs up by this much (feet rise, head lowers by the rest): the extra ledge
    /// clearance of a crouch-jump. Unreal: (standing − crouched height) / 2.</summary>
    public const float CrouchTuck = 0.3f;
    /// <summary>Height of the crouched body (standing = <see cref="Collision.PlayerHeight"/> 1.8 m).</summary>
    public const float CrouchHeight = 1.2f;
    /// <summary>Walking off a drop up to this high keeps you on the ground (stairs, curbs); higher drops are falls.</summary>
    public const float SnapDown = 0.32f;
    /// <summary>How fast the extra speed of an ability impulse bleeds off once its steer lock ends (m/s², ground / air).</summary>
    public const float ImpulseDecayGround = 18f, ImpulseDecayAir = 2.5f;
    /// <summary>Patch 1.09: landing gives every gun this much movement error (degrees) …</summary>
    public const float LandingErr = 7f;
    /// <summary>… for this long (seconds).</summary>
    public const float LandingErrTime = 0.225f;
    /// <summary>Falls shorter than this (highest point to landing, metres) never hurt. Wiki: ~15 damage at 8 m, ~100 at 17 m,
    /// to health only (shields don't absorb it).</summary>
    public const float FallDamageFrom = 8f;
    /// <summary>Damage at <see cref="FallDamageFrom"/>, then per extra metre (linear between the wiki's two points).</summary>
    public const float FallDamageBase = 15f, FallDamagePerMetre = 85f / 9f;

    /// <summary>Feet apex of a standing jump (m).</summary>
    public static float JumpApex => JumpSpeed * JumpSpeed / (2f * Gravity);
    /// <summary>Time in the air of a jump on flat ground (s).</summary>
    public static float JumpAirTime => 2f * JumpSpeed / Gravity;
    /// <summary>Highest ledge a crouch-jump can land on, relative to the take-off floor (m).</summary>
    public static float CrouchJumpReach => JumpApex + CrouchTuck + Collision.AirStep;
    /// <summary>Fall damage (to health) for a fall of <paramref name="drop"/> metres (highest point to landing).</summary>
    public static float FallDamage(float drop) => drop < FallDamageFrom ? 0f : FallDamageBase + (drop - FallDamageFrom) * FallDamagePerMetre;
}

/// <summary>The last finished airborne phase (a jump, a fall off a ledge or an ability launch).</summary>
/// <param name="FromJump">Started with the jump key (false = walked off a ledge or launched by an impulse).</param>
/// <param name="AirTime">Seconds between leaving the ground and landing.</param>
/// <param name="FeetRise">Highest feet height above the take-off point (includes a crouch-jump tuck).</param>
/// <param name="EyeRise">Highest camera height above the take-off camera height.</param>
/// <param name="Drop">Fall height from the highest point to the landing spot (fall damage uses this).</param>
/// <param name="Distance">Horizontal distance from take-off to landing.</param>
public readonly record struct AirInfo(bool FromJump, float AirTime, float FeetRise, float EyeRise, float Drop, float Distance);

public sealed partial class Mover
{
    /// <summary>Standing on something. False while jumping, falling or launched.</summary>
    public bool Grounded { get; private set; } = true;
    /// <summary>Vertical speed (m/s, up = positive; 0 on the ground).</summary>
    public float VelY { get; private set; }
    /// <summary>Multiplier on the max ground speeds (run / walk / crouch / ADS): sprint &gt; 1, slows &lt; 1. Acceleration is unchanged.</summary>
    public float SpeedScale { get => speedScale; set => speedScale = Mathf.Max(0f, value); }
    float speedScale = 1f;
    /// <summary>The jump key works (default true).</summary>
    public bool JumpEnabled = true;
    /// <summary>Seconds left in which the movement keys don't steer (ability dash). Friction is off meanwhile.</summary>
    public float SteerLockLeft { get; private set; }
    /// <summary>Body in its crouched shape (shorter; in the air the legs are pulled up).</summary>
    public bool BodyCrouched => bodyCrouched;
    /// <summary>Collision height of the body right now.</summary>
    public float BodyHeight => bodyCrouched ? MovementTuning.CrouchHeight : Collision.PlayerHeight;
    /// <summary>Seconds since the feet left the ground (0 on the ground).</summary>
    public float AirTime => Grounded ? 0f : airT;
    /// <summary>The last finished airborne phase (for drills, HUD and dev logs).</summary>
    public AirInfo LastAir { get; private set; }
    /// <summary>The head hit a ceiling during the current / last airborne phase.</summary>
    public bool HitCeiling { get; private set; }
    /// <summary>Within <see cref="MovementTuning.LandingErrTime"/> of landing: guns are inaccurate (patch 1.09).</summary>
    public bool LandingPenalty => Grounded && sinceLanded < MovementTuning.LandingErrTime;
    float sinceLanded = 99f;

    /// <summary>The jump key was pressed on the ground (fired before this frame's movement).</summary>
    public event Action? Jumped;
    /// <summary>Touched the ground after any airborne phase (jump, fall, impulse). <see cref="LastAir"/> is already updated.</summary>
    public event Action? Landed;

    bool boosting, jumpQueued, jumpWasHeld, bodyCrouched, airFromJump;
    float airT, airStartY, airTopY, airStartEye, airTopEye;
    Vector3 airStart;

    /// <summary>A jump from a mouse bind (wheel notch, thumb button): consumed by the next <see cref="Step"/>.</summary>
    public void QueueJump() => jumpQueued = true;

    /// <summary>
    /// One physics frame of the player body: jump input, crouch-jump tuck, horizontal movement (ground or air control),
    /// wall sliding, then gravity with ceiling and landing. <paramref name="feet"/> is the body's feet position (in/out).
    /// </summary>
    public void Step(float dt, float yawDeg, ValorantProfile keys, ref Vector3 feet, IReadOnlyList<Box> world)
    {
        if (Grounded) sinceLanded += dt;
        // Jump: a fresh press of a jump key (or a queued mouse-bind jump) while standing on something. Holding the key
        // doesn't re-jump on landing; press again.
        bool held = Down(keys, GameAction.Jump);
        bool pressed = (held && !jumpWasHeld) || jumpQueued;
        jumpWasHeld = held;
        jumpQueued = false;
        if (pressed && Grounded && JumpEnabled)
        {
            VelY = MovementTuning.JumpSpeed;
            Grounded = false;
            BeginAir(feet, fromJump: true);
            Jumped?.Invoke();
        }

        Shape(Down(keys, GameAction.Crouch), ref feet, world);
        var delta = Update(dt, yawDeg, keys);
        Collision.MoveAndSlide(ref feet, ref Vel, delta, world,
            Grounded ? Collision.StepHeight : Collision.AirStep, Grounded ? Collision.PlayerHeight : BodyHeight);
        Vertical(dt, ref feet, world);
    }

    /// <summary>Crouch on the ground lowers the head; crouch in the air pulls the legs up instead (feet rise by
    /// <see cref="MovementTuning.CrouchTuck"/>), which is what lets a crouch-jump land on higher ledges.</summary>
    void Shape(bool crouch, ref Vector3 feet, IReadOnlyList<Box> world)
    {
        if (Grounded) { bodyCrouched = crouch; return; }
        if (crouch == bodyCrouched) return;
        float tuck = MovementTuning.CrouchTuck;
        if (crouch)
        {
            feet.Y += tuck;
            EyeHeight -= tuck; // the camera doesn't jump; it then eases down to the crouched eye height
            bodyCrouched = true;
            return;
        }
        // Legs out again: the feet drop (onto whatever is right below), but only if the head has room to rise.
        float floor = Collision.Ground(feet, world, 0f);
        float drop = Mathf.Clamp(feet.Y - floor, 0f, tuck);
        float newTop = feet.Y - drop + Collision.PlayerHeight;
        if (newTop > Collision.Ceiling(feet, world, feet.Y + MovementTuning.CrouchHeight)) return; // no room: stay tucked
        feet.Y -= drop;
        EyeHeight += drop;
        bodyCrouched = false;
    }

    void Vertical(float dt, ref Vector3 feet, IReadOnlyList<Box> world)
    {
        if (Grounded)
        {
            float g = Collision.Ground(feet, world);
            if (g >= feet.Y - 0.001f) { feet.Y = g; VelY = 0; return; }                       // flat / step up (unchanged)
            if (feet.Y - g <= MovementTuning.SnapDown) { feet.Y = g; VelY = 0; return; }      // walking down a step
            Grounded = false;                                                                 // walked off a ledge
            VelY = 0;
            BeginAir(feet, fromJump: false);
        }

        airT += dt;
        float h = BodyHeight;
        float y0 = feet.Y;
        // Exact ballistic step (frame-rate independent apex and air time).
        float y1 = y0 + VelY * dt - 0.5f * MovementTuning.Gravity * dt * dt;
        VelY -= MovementTuning.Gravity * dt;
        if (y1 > y0)
        {
            float ceil = Collision.Ceiling(feet, world, y0 + h);
            if (y1 + h > ceil)
            {
                y1 = Mathf.Max(y0, ceil - h);
                if (VelY > 0) VelY = 0;
                HitCeiling = true;
            }
        }
        float ground = Collision.Ground(feet, world, Collision.AirStep); // surfaces under the feet at the start of the frame
        if (y1 <= ground && VelY <= 0)
        {
            feet.Y = ground;
            Land(feet);
            return;
        }
        feet.Y = Mathf.Max(y1, ground);
        airTopY = Mathf.Max(airTopY, feet.Y);
        airTopEye = Mathf.Max(airTopEye, feet.Y + EyeHeight);
    }

    void BeginAir(Vector3 feet, bool fromJump)
    {
        airT = 0;
        airFromJump = fromJump;
        airStart = feet;
        airStartY = airTopY = feet.Y;
        airStartEye = airTopEye = feet.Y + EyeHeight;
        HitCeiling = false;
    }

    void Land(Vector3 feet)
    {
        Grounded = true;
        VelY = 0;
        sinceLanded = 0f;
        LastAir = new AirInfo(airFromJump, airT, airTopY - airStartY, airTopEye - airStartEye, airTopY - feet.Y,
            new Vector2(feet.X - airStart.X, feet.Z - airStart.Z).Length());
        Landed?.Invoke();
    }

    /// <summary>
    /// Ability impulse (see <see cref="IGame.PlayerImpulse"/>): the horizontal velocity becomes velocity.XZ; Y &gt; 0 launches
    /// you upward at that speed, Y &lt; 0 sets a downward speed while airborne, Y = 0 keeps the vertical speed. For
    /// <paramref name="steerLockSeconds"/> the keys don't steer and there's no friction; after that any speed above the
    /// normal max bleeds off (<see cref="MovementTuning.ImpulseDecayGround"/> / <see cref="MovementTuning.ImpulseDecayAir"/>).
    /// </summary>
    public void Impulse(Vector3 velocity, float steerLockSeconds, Vector3 feet)
    {
        Vel = new Vector3(velocity.X, 0, velocity.Z);
        if (velocity.Y > 0)
        {
            VelY = velocity.Y;
            if (Grounded) { Grounded = false; BeginAir(feet, fromJump: false); }
        }
        else if (velocity.Y < 0 && !Grounded) VelY = velocity.Y;
        SteerLockLeft = Mathf.Max(0f, steerLockSeconds);
        boosting = true;
        CancelStop();
    }

    /// <summary>
    /// Puts the body at <paramref name="feet"/> (respawn, teleport): vertical speed 0; standing if there's floor within a
    /// step below (or a step-up above) the feet, which are moved onto it; otherwise it starts falling from rest.
    /// Never fires <see cref="Jumped"/> / <see cref="Landed"/>. Horizontal velocity is kept (call <see cref="ResetMotion"/> to stop).
    /// </summary>
    public void Place(ref Vector3 feet, IReadOnlyList<Box> world)
    {
        VelY = 0;
        float g = Collision.Ground(feet, world);
        if (g >= feet.Y - MovementTuning.SnapDown)
        {
            feet.Y = g;
            Grounded = true;
        }
        else
        {
            Grounded = false;
            BeginAir(feet, fromJump: false);
        }
        if (Grounded) bodyCrouched = Crouching;
    }

    /// <summary>Respawn: standing body and camera (no leftover crouch-jump tuck).</summary>
    public void ResetBody()
    {
        bodyCrouched = false;
        EyeHeight = StandEye;
        HitCeiling = false;
    }
}
