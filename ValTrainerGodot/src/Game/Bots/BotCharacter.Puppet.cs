using Godot;
using ValTrainer.Game.Replay;

namespace ValTrainer.Game.Bots;

/// <summary>
/// Rewind puppets (Game\Replay\ReplayPuppets): the real bot body driven from recorded samples instead of a mode/brain.
/// <see cref="Puppet"/> makes _Process return early; the Rewind calls <see cref="PuppetApply"/> then
/// <see cref="PuppetStep"/>, which runs the same animation code as a live bot (no footsteps, no tracers, never frees
/// itself). Animation isn't recorded: the smoothed blend weights are re-derived from the recorded velocity/stance.
/// </summary>
public partial class BotCharacter
{
    bool puppetAiming;
    float puppetPitch;

    /// <summary>A pooled Rewind puppet (variant 0..3 as recorded).</summary>
    internal static BotCharacter CreatePuppet(Color enemy, bool outlines, int variant)
    {
        var b = Create(enemy, outlines, Math.Clamp(variant, 0, BotAssets.Variants.Length - 1));
        b.Puppet = true;
        b.QuietSteps = true;
        b.Name = "Puppet";
        return b;
    }

    /// <summary>True once a death has sunk the body out of sight (a live bot would have freed itself by now).</summary>
    internal bool PuppetGone => deathT > 2.3f;

    /// <summary>Recorded state for this frame (position, velocity, facing, stance, rifle pitch).</summary>
    internal void PuppetApply(in BotSample s)
    {
        Feet = s.Feet;
        Velocity = s.Vel;
        FacingYaw = Mathf.RadToDeg(s.Facing);
        Crouched = (s.Bits & BBits.Crouched) != 0;
        Grounded = (s.Bits & BBits.Grounded) != 0;
        puppetAiming = (s.Bits & BBits.Aiming) != 0;
        puppetPitch = s.AimPitch;
    }

    /// <summary>Advances animation and effects by <paramref name="dt"/> seconds of replay time (0 = just re-pose).</summary>
    internal void PuppetStep(float dt)
    {
        // The recorded rifle pitch, as the aim point a live bot would have had (Step turns it back into the same pitch).
        if (puppetAiming && posed)
        {
            float yaw = Mathf.DegToRad(FacingYaw), cp = Mathf.Cos(puppetPitch);
            AimTarget = Head + new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(puppetPitch), -Mathf.Cos(yaw) * cp) * 10f;
        }
        else AimTarget = null;
        Step(dt);
    }

    /// <summary>Back to a fresh, alive body whose smoothed weights (legs, gait, crouch, aim) already match the applied
    /// sample: used after a seek and when a pooled puppet is reused.</summary>
    internal void PuppetReset()
    {
        Hp = MaxHp;
        deathT = -1f;
        dieW = 0f;
        fade = 1f;
        flash = 0f;
        flinchT = 0f;
        recoil = 0f;
        aimHold = 0f;
        backwards = false;
        model.Position = Vector3.Zero;
        Visible = true;
        // A long step with no history snaps every exponential smoother to its target.
        AimTarget = null;
        Step(10f);
        flashT = 0f;
        if (flashLight != null) flashLight.Visible = false;
        if (flashQuad != null) flashQuad.Visible = false;
        if (puppetAiming) { aimPitch = puppetPitch; aimHold = 0.1f; aimW = 1f; }
        else aimW = 0f;
    }

    /// <summary>Hit flash and flinch (same as <see cref="OnHit"/>, without raising <see cref="Fx"/>).</summary>
    internal void PuppetHit(HitZone z)
    {
        flash = 1f;
        if (z != HitZone.Legs) flinchT = 0.4f;
        if (deathT >= 0 || !IsInsideTree()) return;
        if (z == HitZone.Head) tree.Set(BotAssets.P.HitHead, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        else if (z == HitZone.Body) tree.Set(BotAssets.P.HitChest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    /// <summary>Death from <paramref name="fromDir"/>, already <paramref name="elapsed"/> seconds in (seeks land mid-fall).</summary>
    internal void PuppetDie(Vector3 fromDir, float elapsed)
    {
        if (deathT >= 0) return;
        Hp = 0;
        var away = new Vector3(fromDir.X, 0, fromDir.Z);
        if (away.LengthSquared() < 1e-6f) away = -GlobalBasis.Z;
        var local = GlobalBasis.Inverse() * (-away.Normalized());
        deathYaw = Mathf.Atan2(local.X, local.Z);
        elapsed = Mathf.Max(0f, elapsed);
        deathT = elapsed;
        if (elapsed > 0f)
        {
            dieW = Mathf.Min(1f, elapsed / 0.12f);
            legYaw = Mathf.LerpAngle(legYaw, deathYaw, 1f - Mathf.Exp(-14f * elapsed));
            fade = Mathf.Max(0f, 1f - elapsed / 0.45f);
            flash = 0f;
        }
        if (IsInsideTree()) tree.Set(BotAssets.P.DeathSeek, (double)elapsed);
        flashT = 0f;
        if (flashLight != null) flashLight.Visible = false;
        if (flashQuad != null) flashQuad.Visible = false;
    }

    /// <summary>The bot fired at <paramref name="target"/> <paramref name="age"/> seconds ago: rifle aims there for ~1 s,
    /// recoil kick and muzzle flash while still fresh. The tracer is the Rewind's (ReplayFx).</summary>
    internal void PuppetShoot(Vector3 target, float age)
    {
        if (deathT >= 0) return;
        age = Mathf.Max(0f, age);
        recoil = Mathf.Max(0f, 1f - age / 0.09f);
        var to = target - Head;
        aimPitch = Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
        aimHold = 1.2f - age;
        if (age > 0.25f) aimW = aimHold > 0 ? 1f : aimW;
        if (flashLight == null || flashQuad == null || age >= 0.045f) return;
        flashT = 0.045f - age;
        flashLight.Visible = true;
        flashQuad.Visible = true;
        flashQuad.Scale = Vector3.One * (0.8f + 0.4f * Random.Shared.NextSingle());
        flashQuad.SetInstanceShaderParameter(SeedParam, Random.Shared.NextSingle());
    }
}
