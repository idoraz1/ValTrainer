using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Gun logic modelled on Riot's documented recoil system:
/// - Full auto at the real fire rate (Vandal 9.75 rps; ADS ×0.9), semi-auto for Sheriff/Operator.
/// - "Heat" = fractional bullet index into the per-bullet pitch/spread tables; it decays when you stop
///   (reset time ≈ RecoveryTime·√heat: 1 shot 0.375 s, full spray ≈1.3 s), and grows less for slow taps
///   (Tap Efficiency). Horizontal drift is side-locked with a 10% swap chance after the protected bullets.
/// - The camera follows the recoil (k = 1), and drifts back as heat decays, like the real game.
/// - Spread: per-bullet error + movement error; standing shots bunch to the centre, moving shots are uniform.
/// </summary>
public sealed class WeaponController
{
    public WeaponDef Def { get; private set; }
    public int Ammo, Reserve;
    public float ReloadLeft;
    public bool Reloading => ReloadLeft > 0;
    public bool Scoped;
    public float EquipLeft;

    float heat, yawHeat, lastShot = -99f, nextShotAt;
    bool triggerWasDown;
    bool bottomless;
    int sprayShots;
    int side = -1;           // Vandal tends to drift left first
    float sideBlend = -1;
    public float SpreadNow;  // current error cone (degrees) — for the crosshair firing-error lines
    public float Heat => heat;
    public event Action? Fired;
    readonly List<(Vector3 Dir, bool Accurate)> shots = new();

    public WeaponController(WeaponDef def)
    {
        Def = def;
        Ammo = def.Mag;
        Reserve = def.Reserve;
        side = def.Kind == WeaponKind.Phantom ? 1 : -1;
        sideBlend = side;
    }

    /// <summary>New round / respawn: no leftover recoil.</summary>
    public void ResetRecoil()
    {
        heat = yawHeat = 0;
        sprayShots = 0;
        triggerWasDown = false;
    }

    public void StartReload(bool infinite)
    {
        if (Reloading || Ammo >= Def.Mag || (!infinite && Reserve <= 0)) return;
        ReloadLeft = Def.Reload;
        Scoped = false;
    }

    /// <summary>
    /// Advances the gun one frame and returns the bullets fired (origin = eye, normalized directions).
    /// Applies recoil to <paramref name="view"/>.
    /// </summary>
    public IReadOnlyList<(Vector3 Dir, bool Accurate)> Tick(float now, float dt, PlayerView view, Mover mover, Random rng,
        bool triggerHeld, bool triggerPressed, bool bottomlessMag, bool infiniteAmmo, bool movementMode)
    {
        shots.Clear();
        bottomless = bottomlessMag;
        if (EquipLeft > 0) EquipLeft -= dt;

        // Reload
        if (Reloading)
        {
            ReloadLeft -= dt;
            if (ReloadLeft <= 0)
            {
                int need = Def.Mag - Ammo;
                int take = infiniteAmmo ? need : Math.Min(need, Reserve);
                Ammo += take;
                if (!infiniteAmmo) Reserve -= take;
            }
        }

        // Recovery while not firing.
        bool firingNow = triggerHeld && Def.Auto || triggerPressed;
        if (!firingNow || Reloading)
        {
            float before = heat;
            heat = Mathf.Max(0, heat - dt * 2f * Mathf.Sqrt(Mathf.Max(heat, 1e-3f)) / Def.RecoveryTime);
            yawHeat = Mathf.Max(0, yawHeat - dt * 2f * Mathf.Sqrt(Mathf.Max(yawHeat, 1e-3f)) / Def.RecoveryTime);
            if (heat <= 0f) sprayShots = 0;
        }

        float interval = Def.Interval / (Scoped && Def.Kind != WeaponKind.Operator ? Def.AdsRpsMul : 1f);
        bool wantShot = Def.Auto ? triggerHeld : triggerPressed;
        if (wantShot && !Reloading && EquipLeft <= 0)
        {
            if (Ammo <= 0) StartReload(infiniteAmmo);
            else
            {
                // Only a continuous hold keeps the fixed schedule (FPS-independent auto fire);
                // a fresh press fires now but never sooner than the gun's interval after the last shot.
                if (!triggerWasDown || nextShotAt < now - interval) nextShotAt = Mathf.Max(nextShotAt, now);
                int guard = 0;
                while (now >= nextShotAt && Ammo > 0 && guard++ < 4)
                {
                    shots.Add(FireOne(now, view, mover, rng, movementMode));
                    nextShotAt += interval;
                    if (!Def.Auto) { nextShotAt = now + interval; break; }
                }
                if (Ammo == 0) StartReload(infiniteAmmo);
            }
        }

        // Side swap blends over YawSwitchTime.
        triggerWasDown = Def.Auto && triggerHeld && !Reloading;
        sideBlend = Mathf.MoveToward(sideBlend, side, dt * 2f / Def.YawSwitchTime);

        // Camera follows the recoil (and drifts back as heat recovers).
        float crouchMul = mover.Crouching && mover.Speed < 0.5f ? 0.85f : 1f;
        float runMul = movementMode && mover.Speed > Mover.WalkSpeed ? 1.8f : 1f;
        view.RecoilPitch = WeaponDef.Lerp(Def.Pitch, heat) * crouchMul * runMul;
        view.RecoilYaw = sideBlend * WeaponDef.Lerp(Def.Yaw, yawHeat) * crouchMul;

        SpreadNow = CurrentError(mover, movementMode);
        return shots;
    }

    float CurrentError(Mover mover, bool movementMode)
    {
        float err;
        if (Def.Kind == WeaponKind.Operator) err = Scoped ? 0f : Def.FirstShotHip;
        else
        {
            err = WeaponDef.Lerp(Def.Spread, heat);
            if (Scoped) err = Mathf.Max(0.05f, err - (Def.FirstShotHip - Def.FirstShotAds));
        }
        if (mover.Crouching) err *= 0.85f;
        if (movementMode) err += mover.MovementErrorDeg(Def);
        return err;
    }

    (Vector3, bool) FireOne(float now, PlayerView view, Mover mover, Random rng, bool movementMode)
    {
        float gap = now - lastShot;
        lastShot = now;
        if (!bottomless) Ammo--;
        sprayShots++;

        // Bullet goes where the (recoiled) view points, plus the error cone.
        float err = CurrentError(mover, movementMode);
        bool moving = movementMode && !mover.Accurate;
        var dir = PlayerView.Spread(view.Forward, err, rng, uniform: moving);

        // Heat grows by one bullet in a spray, less for spaced taps (Tap Efficiency).
        float interval = Def.Interval;
        bool spray = gap <= 1.2f * interval;
        float add = spray || heat <= 0f ? 1f : 1f / (1f + Def.TapEfficiency * (gap - interval) / Def.RecoveryTime);
        heat = Mathf.Min(heat + add, Def.HeatCap);
        yawHeat = Mathf.Min(yawHeat + (spray ? 1f : 0.5f * add), Def.Yaw.Length - 1);
        if (sprayShots > Def.ProtectedBullets && rng.NextDouble() < 0.10) side = -side;

        if (Def.Kind == WeaponKind.Operator) Scoped = false; // bolt action: unscope after the shot
        Fired?.Invoke();
        return (dir, !moving);
    }
}
