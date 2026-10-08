using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// Valorant rifle movement (ported unchanged from the tuned raylib build). Braking matches Riot-measured
/// Phantom stop times (≈105 ms to the accuracy threshold, ≈160 ms to full stop); releasing the keys and
/// counter-strafing stop almost equally fast (counter ≈6% quicker).
/// <para>This file is the horizontal model (ground, air control, dash lock). Jumping, crouch-jumps, falling, landing,
/// ability impulses and the collision step are in MovementBody.cs; the numbers are in <see cref="MovementTuning"/>.</para>
/// </summary>
public sealed partial class Mover
{
    public const float RunSpeed = 5.4f;
    public const float WalkSpeed = RunSpeed * 0.55f;
    public const float CrouchSpeed = RunSpeed * 0.45f;
    public const float AdsMultiplier = 0.76f;
    public const float AccurateSpeed = RunSpeed * 0.275f; // ≈1.49 m/s (patch 3.0)

    const float Accel = 40f, BrakeHigh = 38f, BrakeLow = 24f, CounterBonus = 1.06f;

    public const float StandEye = PlayerView.EyeHeight;
    public const float CrouchEye = 1.15f;

    /// <summary>Horizontal velocity (m/s; Y is always 0). The vertical speed is <see cref="VelY"/>.</summary>
    public Vector3 Vel;
    public bool Crouching, Walking;
    public float EyeHeight = StandEye;
    public bool Ads;
    /// <summary>Aimed / scoped max speed as a fraction of the run speed: the gun's (<see cref="Core.WeaponDef.AdsMoveMul"/>,
    /// Operator 0.72), <see cref="AdsMultiplier"/> by default.</summary>
    public float AdsSpeedMul = AdsMultiplier;

    /// <summary>Horizontal (ground) speed in m/s.</summary>
    public float Speed => new Vector2(Vel.X, Vel.Z).Length();
    /// <summary>Standing on something, past the landing penalty and at most <see cref="AccurateSpeed"/>: first shots go where you aim.</summary>
    public bool Accurate => Grounded && !LandingPenalty && Speed <= AccurateSpeed + 0.001f;
    /// <summary>0 = accurate … 1 = running speed, airborne or just landed (crosshair movement-error display).</summary>
    public float MoveError => Grounded && !LandingPenalty ? Mathf.Clamp((Speed - AccurateSpeed) / (RunSpeed - AccurateSpeed), 0, 1) : 1f;

    public readonly List<float> StopTimesMs = new();
    public readonly List<float> CounterStopTimesMs = new();
    public readonly List<float> ReleaseStopTimesMs = new();
    public float LastStopMs = -1;
    public bool LastStopWasCounter;
    bool stopping, stoppingCounter, opposedKeys;
    float stopT;

    /// <summary>
    /// DEV / TEST ONLY: when set, every bind the mover (and every other bind check) reads comes from this function instead
    /// of the keyboard and mouse, so headless runs can script movement (see the --movescript dev flag in MovementScript.cs).
    /// Same as <see cref="Keybinds.Override"/>. Null (the default) = the real input.
    /// </summary>
    public static Func<InputBinding, bool>? KeyOverride { get => Keybinds.Override; set => Keybinds.Override = value; }

    static bool Down(ValorantProfile keys, GameAction a) => keys.Binds.IsDown(a);

    /// <summary>Computes this frame's horizontal velocity and returns the position delta (collision is the caller's job).
    /// Prefer <see cref="Step"/>, which also jumps, falls and collides.</summary>
    public Vector3 Update(float dt, float yawDeg, ValorantProfile keys)
    {
        float y = Mathf.DegToRad(yawDeg);
        var fwd = new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y));
        var right = new Vector3(Mathf.Cos(y), 0, Mathf.Sin(y));

        var wish = Vector3.Zero;
        bool f = Down(keys, GameAction.MoveForward), b = Down(keys, GameAction.MoveBack);
        bool l = Down(keys, GameAction.StrafeLeft), r = Down(keys, GameAction.StrafeRight);
        if (f) wish += fwd;
        if (b) wish -= fwd;
        if (r) wish += right;
        if (l) wish -= right;
        bool wants = wish.LengthSquared() > 0.001f;
        if (wants) wish = wish.Normalized();
        opposedKeys = !wants && ((l && r) || (f && b));

        Crouching = Down(keys, GameAction.Crouch);
        Walking = Down(keys, GameAction.Walk);
        float target = Crouching ? CrouchEye : StandEye;
        EyeHeight += (target - EyeHeight) * (1f - Mathf.Exp(-14f * dt));

        float max = (Crouching ? CrouchSpeed : Walking ? WalkSpeed : RunSpeed) * SpeedScale;
        if (Ads) max = Mathf.Min(max, RunSpeed * AdsSpeedMul * SpeedScale);

        // Ability dash: no steering and no friction until the lock runs out (gravity still applies in the air).
        if (SteerLockLeft > 0)
        {
            SteerLockLeft = Mathf.Max(0, SteerLockLeft - dt);
            CancelStop();
            return Vel * dt;
        }
        if (!Grounded)
        {
            AirMove(dt, wish, wants, max);
            CancelStop(); // counter-strafe stops are measured on the ground only
            return Vel * dt;
        }

        float speedBefore = Speed;
        if (boosting && speedBefore > max)
        {
            BoostDecay(dt, wish, wants, MovementTuning.ImpulseDecayGround, max);
            CancelStop();
            return Vel * dt;
        }
        boosting = false;

        float brake = (speedBefore > RunSpeed * 0.25f ? BrakeHigh : BrakeLow) * dt;
        bool reversing = false;

        if (!wants)
        {
            float s = Mathf.Max(0, speedBefore - brake);
            Vel = speedBefore > 0 ? Vel * (s / speedBefore) : Vector3.Zero;
        }
        else
        {
            float along = Vel.Dot(wish);
            var lateral = Vel - wish * along;
            float lat = lateral.Length();
            if (lat > 0) lateral *= Mathf.Max(0, lat - brake) / lat;
            if (along < 0) { reversing = true; along = Mathf.Min(0, along + brake * CounterBonus); }
            else if (along > max) along = Mathf.Max(max, along - brake);
            else along = Mathf.Min(max, along + Accel * dt);
            Vel = wish * along + lateral;
        }

        TrackStop(dt, wants, wish, reversing, speedBefore);
        return Vel * dt;
    }

    /// <summary>
    /// Air control: the keys accelerate you at <see cref="MovementTuning.AirAccel"/> but can't raise your speed above what
    /// you took off with (or your stance's max), so air-strafing turns and brakes but never builds speed (no bunny hops).
    /// No key = no friction: you keep your momentum.
    /// </summary>
    void AirMove(float dt, Vector3 wish, bool wants, float max)
    {
        float before = Speed;
        if (boosting && before > max) { BoostDecay(dt, wish, wants, MovementTuning.ImpulseDecayAir, max); return; }
        boosting = false;
        float cap = Mathf.Max(max, before);
        if (wants) Vel += wish * MovementTuning.AirAccel * dt;
        float s = Speed;
        if (s > cap) Vel *= cap / s;
    }

    /// <summary>After an ability impulse: the extra speed bleeds off at <paramref name="decay"/> m/s²; the keys can steer
    /// (turn) but not add speed. Normal movement resumes once you're back under the stance's max.</summary>
    void BoostDecay(float dt, Vector3 wish, bool wants, float decay, float max)
    {
        float before = Speed;
        float cap = Mathf.Max(max, before - decay * dt);
        if (wants) Vel += wish * (Grounded ? Accel : MovementTuning.AirAccel) * dt;
        float s = Speed;
        if (s > cap) Vel *= cap / s;
        if (Speed <= max + 1e-3f) boosting = false;
    }

    void TrackStop(float dt, bool wants, Vector3 wish, bool reversing, float speedBefore)
    {
        if (!stopping && speedBefore > RunSpeed * 0.8f && (!wants || reversing || opposedKeys))
        {
            stopping = true;
            stoppingCounter = reversing;
            stopT = 0;
        }
        if (!stopping) return;
        stopT += dt;
        if (reversing || opposedKeys) stoppingCounter = true;
        if (Accurate)
        {
            stopping = false;
            LastStopMs = stopT * 1000f;
            LastStopWasCounter = stoppingCounter;
            StopTimesMs.Add(LastStopMs);
            if (stoppingCounter) CounterStopTimesMs.Add(LastStopMs); else ReleaseStopTimesMs.Add(LastStopMs);
        }
        else if (wants && !reversing && wish.Dot(Vel) > Speed * 0.7f && Speed > speedBefore)
            stopping = false;
    }

    /// <summary>A stop that was being timed no longer counts (you jumped, dashed or got launched).</summary>
    void CancelStop() { stopping = stoppingCounter = false; stopT = 0; }

    /// <summary>Movement error added to weapon spread (rifles: crouch-walk 0.8°, walk 3°, run 6°; airborne: the weapon's air
    /// error; just landed: at least <see cref="MovementTuning.LandingErr"/>).</summary>
    public float MovementErrorDeg(Core.WeaponDef w)
    {
        if (!Grounded) return w.AirErr;
        if (LandingPenalty) return Mathf.Max(MovementTuning.LandingErr, GroundErrorDeg(w));
        return GroundErrorDeg(w);
    }

    float GroundErrorDeg(Core.WeaponDef w)
    {
        float s = Speed;
        if (s <= AccurateSpeed) return 0f;
        if (Crouching) return w.CrouchWalkErr;
        if (s <= WalkSpeed) return w.WalkErr * (s - AccurateSpeed) / (WalkSpeed - AccurateSpeed);
        return w.WalkErr + (w.RunErr - w.WalkErr) * Mathf.Clamp((s - WalkSpeed) / (RunSpeed - WalkSpeed), 0, 1);
    }

    public void ResetStops()
    {
        StopTimesMs.Clear(); CounterStopTimesMs.Clear(); ReleaseStopTimesMs.Clear();
        LastStopMs = -1;
        ResetMotion();
    }

    /// <summary>Stops all motion: horizontal and vertical speed, dashes and impulses, a stop being timed and a queued jump.
    /// The ground state is re-checked on the next step (a respawn in the air starts falling from rest).</summary>
    public void ResetMotion()
    {
        Vel = Vector3.Zero;
        stopping = stoppingCounter = false;
        stopT = 0;
        VelY = 0;
        SteerLockLeft = 0;
        boosting = false;
        jumpQueued = false;
        sinceLanded = 99f;
    }
}
