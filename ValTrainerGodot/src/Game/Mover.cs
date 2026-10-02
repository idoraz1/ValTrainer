using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// Valorant rifle movement (ported unchanged from the tuned raylib build). Braking matches Riot-measured
/// Phantom stop times (≈105 ms to the accuracy threshold, ≈160 ms to full stop); releasing the keys and
/// counter-strafing stop almost equally fast (counter ≈6% quicker).
/// </summary>
public sealed class Mover
{
    public const float RunSpeed = 5.4f;
    public const float WalkSpeed = RunSpeed * 0.55f;
    public const float CrouchSpeed = RunSpeed * 0.45f;
    public const float AdsMultiplier = 0.76f;
    public const float AccurateSpeed = RunSpeed * 0.275f; // ≈1.49 m/s (patch 3.0)

    const float Accel = 40f, BrakeHigh = 38f, BrakeLow = 24f, CounterBonus = 1.06f;

    public const float StandEye = PlayerView.EyeHeight;
    public const float CrouchEye = 1.15f;

    public Vector3 Vel;
    public bool Crouching, Walking;
    public float EyeHeight = StandEye;
    public bool Ads;

    public float Speed => new Vector2(Vel.X, Vel.Z).Length();
    public bool Accurate => Speed <= AccurateSpeed + 0.001f;
    public float MoveError => Mathf.Clamp((Speed - AccurateSpeed) / (RunSpeed - AccurateSpeed), 0, 1);

    public readonly List<float> StopTimesMs = new();
    public readonly List<float> CounterStopTimesMs = new();
    public readonly List<float> ReleaseStopTimesMs = new();
    public float LastStopMs = -1;
    public bool LastStopWasCounter;
    bool stopping, stoppingCounter, opposedKeys;
    float stopT;

    static bool Down(Key k) => Input.IsKeyPressed(k);

    /// <summary>Computes this frame's velocity and returns the position delta (collision is the caller's job).</summary>
    public Vector3 Update(float dt, float yawDeg, ValorantProfile keys)
    {
        float y = Mathf.DegToRad(yawDeg);
        var fwd = new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y));
        var right = new Vector3(Mathf.Cos(y), 0, Mathf.Sin(y));

        var wish = Vector3.Zero;
        if (Down(keys.KeyForward)) wish += fwd;
        if (Down(keys.KeyBack)) wish -= fwd;
        if (Down(keys.KeyRight)) wish += right;
        if (Down(keys.KeyLeft)) wish -= right;
        bool wants = wish.LengthSquared() > 0.001f;
        if (wants) wish = wish.Normalized();
        opposedKeys = !wants && ((Down(keys.KeyLeft) && Down(keys.KeyRight)) || (Down(keys.KeyForward) && Down(keys.KeyBack)));

        Crouching = Down(keys.KeyCrouch);
        Walking = Down(keys.KeyWalk);
        float target = Crouching ? CrouchEye : StandEye;
        EyeHeight += (target - EyeHeight) * (1f - Mathf.Exp(-14f * dt));

        float max = Crouching ? CrouchSpeed : Walking ? WalkSpeed : RunSpeed;
        if (Ads) max = Mathf.Min(max, RunSpeed * AdsMultiplier);

        float speedBefore = Speed;
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

    /// <summary>Movement error added to weapon spread (rifles: crouch-walk 0.8°, walk 3°, run 6°).</summary>
    public float MovementErrorDeg(Core.WeaponDef w)
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

    public void ResetMotion()
    {
        Vel = Vector3.Zero;
        stopping = stoppingCounter = false;
        stopT = 0;
    }
}
