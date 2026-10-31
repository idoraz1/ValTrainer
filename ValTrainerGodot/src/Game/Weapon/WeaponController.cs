using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// One bullet or pellet fired this frame. Everything one trigger pull fires shares a <see cref="Volley"/> number: a rifle
/// bullet is a volley of one, a Judge shot a volley of 12 pellets, the Classic's right click a volley of 3. Pellet 0 is the
/// volley's centre (the aim direction plus movement error): drills, telemetry and the Rewind count the volley as one shot
/// through it (GameSession's volley step).
/// </summary>
/// <param name="Dir">Normalized direction.</param>
/// <param name="Accurate">False while moving too fast (uniform error cone).</param>
/// <param name="StartAt">The pellet starts this far (m) along pellet 0's direction instead of at the eye: the Bucky's
/// air-burst pellets begin where the canister splits.</param>
/// <param name="MaxRange">The pellet stops after this many metres (0 = no cap): Shorty / Bucky pellets 50 m, the Bucky's
/// canister at its split distance.</param>
public readonly record struct Shot(Vector3 Dir, bool Accurate, int Volley, int Pellet, float StartAt = 0f, float MaxRange = 0f);

/// <summary>
/// Gun logic modelled on Riot's documented recoil system:
/// - Full auto at the real fire rate (Vandal 9.75 rps; ADS ×0.9), semi-auto for Sheriff/Operator.
/// - "Heat" = fractional bullet index into the per-bullet pitch/spread tables; it decays when you stop
///   (reset time ≈ RecoveryTime·√heat: 1 shot 0.375 s, full spray ≈1.3 s), and grows less for slow taps
///   (Tap Efficiency). Horizontal drift is side-locked with a 10% swap chance after the protected bullets.
/// - The camera follows the recoil (k = 1), and drifts back as heat decays, like the real game.
/// - Spread: per-bullet error + movement error; standing shots bunch to the centre, moving shots are uniform.
/// 1.18 (research: docs/research/weapons-1.18.md, design §4) adds, all driven by <see cref="WeaponDef"/> data:
/// - Pellet volleys (Shorty, Bucky, Judge): the Spread table is the pellet cone, movement error moves the whole pattern.
/// - Aimed bursts (Stinger 4, Bulldog 3): while aimed, a trigger pull fires a whole burst at BurstRps; holding repeats a
///   burst every BurstCycle; own aimed spread / pitch curves and recovery.
/// - Alt-fire volleys: the Classic's 3-pellet blast (cone grows when spammed) and the Bucky's air-burst canister.
/// - Spin-up (Odin): the hip fire rate ramps from Rps to MaxRps over SpinUpTime; aimed fire starts at MaxRps.
/// - The Outlaw's second-shot scoped spread and recovery delay; partial (Outlaw) and shell-by-shell (Bucky, Marshal)
///   reloads.
/// Vandal, Phantom and every single-bullet gun without these fields take exactly the pre-1.18 path (same random draws).
/// </summary>
public sealed class WeaponController
{
    public WeaponDef Def { get; private set; }
    public int Ammo, Reserve;
    /// <summary>Seconds left in the reload in progress (shell-by-shell: for every round still to load).</summary>
    public float ReloadLeft;
    public bool Reloading => ReloadLeft > 0;
    public bool Scoped;
    public float EquipLeft;

    float heat, yawHeat, lastShot = -99f, nextShotAt;
    bool triggerWasDown;
    bool bottomless;
    int sprayShots;
    int side = -1;           // first drift side (WeaponDef.FirstDriftSide): Vandal left, Phantom right
    float sideBlend = -1;
    // aimed bursts
    int burstLeft;
    float burstStart, burstNextAt;
    // alt-fire blasts (Classic): consecutive blasts step through AltSpread
    int altChain;
    float lastAlt = -99f;
    // reloads: the length of the one in progress, and the rounds of a shell-by-shell one
    float reloadTotal;
    int roundsToLoad, roundsLoaded;
    int volleyId;
    readonly WeaponDef altMoveDef; // the Classic's alt-fire movement error as a def (Mover.MovementErrorDeg reads it)

    public float SpreadNow;  // current error cone (degrees) — for the crosshair firing-error lines
    public float Heat => heat;
    /// <summary>Spin-up 0..1 (Odin): 0 = the start rate <see cref="WeaponDef.Rps"/>, 1 = <see cref="WeaponDef.MaxRps"/>.</summary>
    public float Spin { get; private set; }
    /// <summary>True from the first to the last bullet of an aimed burst (HUD BURST chip, the trigger is ignored).</summary>
    public bool Bursting => burstLeft > 0;
    /// <summary>Aim error (deg) of the last volley's centre: the bullet's error cone for single bullets, movement error only
    /// for pellet volleys (weapon check, debugging).</summary>
    public float LastShotError { get; private set; }
    /// <summary>Pellet cone (deg) of the last volley; 0 for a single bullet.</summary>
    public float LastConeDeg { get; private set; }
    /// <summary>Volleys (trigger pulls that fired) since the gun was made.</summary>
    public int Volleys => volleyId;
    /// <summary>0..1 through the reload in progress (viewmodel animation), -1 when not reloading. A partial or
    /// shell-by-shell reload runs its own length, not <see cref="WeaponDef.Reload"/>.</summary>
    public float ReloadProgress => Reloading && reloadTotal > 0 ? Mathf.Clamp(1f - ReloadLeft / reloadTotal, 0f, 1f) : -1f;

    public event Action? Fired;
    readonly List<Shot> shots = new();

    enum VolleyKind { Primary, Blast, AirBurst }

    public WeaponController(WeaponDef def)
    {
        Def = def;
        Ammo = def.Mag;
        Reserve = def.Reserve;
        side = def.FirstDriftSide >= 0 ? 1 : -1; // Phantom right, the rest left
        sideBlend = side;
        var m = def.AltMoveErr;
        altMoveDef = m.Length >= 4 ? def with { CrouchWalkErr = m[0], WalkErr = m[1], RunErr = m[2], AirErr = m[3] } : def;
    }

    bool BurstGun => Def.AltFire == AltFireKind.ZoomBurst && Def.BurstCount > 1 && Def.BurstRps > 0;
    float BurstCycle => Def.BurstCycle > 0 ? Def.BurstCycle : Def.BurstCount / Def.BurstRps;

    /// <summary>The fire rate right now (rounds/s): spin-up (the Odin's aimed fire skips it), else the aimed multiplier
    /// (the Marshal fires ×0.8 scoped). Aimed burst guns use <see cref="WeaponDef.BurstRps"/> / BurstCycle instead.</summary>
    public float CurrentRps => Def.SpinUpTime > 0f
        ? (Scoped && Def.AdsSkipsSpinUp ? Def.MaxRps : Mathf.Lerp(Def.Rps, Def.MaxRps, Spin))
        : Def.Rps * (Scoped ? Def.AdsRpsMul : 1f);

    /// <summary>New round / respawn: no leftover recoil.</summary>
    public void ResetRecoil()
    {
        heat = yawHeat = 0;
        sprayShots = 0;
        triggerWasDown = false;
        burstLeft = 0;
        altChain = 0;
        Spin = 0;
    }

    /// <summary>Starts a reload: the whole magazine in <see cref="WeaponDef.Reload"/>, with rounds left in
    /// <see cref="WeaponDef.ReloadPartial"/> if the gun has one (Outlaw 2.3 s), or a round per
    /// <see cref="WeaponDef.ReloadPerRound"/> (Bucky, Marshal).</summary>
    public void StartReload(bool infinite)
    {
        if (Reloading || Ammo >= Def.Mag || (!infinite && Reserve <= 0)) return;
        burstLeft = 0;
        if (Def.ReloadPerRound > 0f)
        {
            roundsToLoad = infinite ? Def.Mag - Ammo : Math.Min(Def.Mag - Ammo, Reserve);
            roundsLoaded = 0;
            ReloadLeft = roundsToLoad * Def.ReloadPerRound;
        }
        else ReloadLeft = Ammo > 0 && Def.ReloadPartial > 0f ? Def.ReloadPartial : Def.Reload;
        reloadTotal = ReloadLeft;
        Scoped = false;
    }

    void TickReload(float dt, bool infiniteAmmo)
    {
        ReloadLeft -= dt;
        if (Def.ReloadPerRound > 0f)
        {
            // Shell by shell: a round goes in each time another ReloadPerRound has passed.
            int left = ReloadLeft <= 0f ? 0 : Mathf.CeilToInt(ReloadLeft / Def.ReloadPerRound - 1e-4f);
            while (roundsLoaded < roundsToLoad - left)
            {
                roundsLoaded++;
                Ammo++;
                if (!infiniteAmmo) Reserve--;
            }
            return;
        }
        if (ReloadLeft <= 0)
        {
            int need = Def.Mag - Ammo;
            int take = infiniteAmmo ? need : Math.Min(need, Reserve);
            Ammo += take;
            if (!infiniteAmmo) Reserve -= take;
        }
    }

    /// <summary>
    /// Advances the gun one frame and returns the bullets / pellets fired (origin = eye, normalized directions; see
    /// <see cref="Shot"/>). Applies recoil to <paramref name="view"/>. <paramref name="altPressed"/>: the alt-fire press of
    /// a gun whose right click shoots (Classic blast, Bucky air burst); aiming guns use <see cref="Scoped"/> instead.
    /// </summary>
    public IReadOnlyList<Shot> Tick(float now, float dt, PlayerView view, Mover mover, Random rng,
        bool triggerHeld, bool triggerPressed, bool bottomlessMag, bool infiniteAmmo, bool movementMode, bool altPressed = false)
    {
        shots.Clear();
        bottomless = bottomlessMag;
        if (EquipLeft > 0) EquipLeft -= dt;
        bool altShoots = Def.AltFire is AltFireKind.Shotgun or AltFireKind.AirBurst;
        altPressed &= altShoots;

        // Reload. A trigger pull with a round in the magazine stops a shell-by-shell reload (Bucky, Marshal).
        if (Reloading) TickReload(dt, infiniteAmmo);
        if (Reloading && Def.ReloadPerRound > 0f && Ammo > 0 && (triggerPressed || (Def.Auto && triggerHeld) || altPressed))
            ReloadLeft = 0;

        // Spin-up (Odin): winds up while the trigger is held and the gun can fire, down otherwise.
        if (Def.SpinUpTime > 0f)
        {
            bool spinning = Def.Auto && triggerHeld && !Reloading && Ammo > 0 && EquipLeft <= 0;
            Spin = Mathf.Clamp(Spin + (spinning ? dt : -dt) / Def.SpinUpTime, 0f, 1f);
        }

        // Recovery while not firing (aimed burst guns recover between bursts even while the trigger is held), after the
        // gun's recovery delay (Outlaw).
        bool burstMode = BurstGun && Scoped;
        bool firingNow = (burstMode ? burstLeft > 0 : triggerHeld && Def.Auto) || triggerPressed || altPressed;
        if ((!firingNow || Reloading) && now - lastShot >= Def.RecoveryDelay)
        {
            float rec = burstMode && Def.AltRecoveryTime > 0f ? Def.AltRecoveryTime : Def.RecoveryTime;
            heat = Mathf.Max(0, heat - dt * 2f * Mathf.Sqrt(Mathf.Max(heat, 1e-3f)) / rec);
            yawHeat = Mathf.Max(0, yawHeat - dt * 2f * Mathf.Sqrt(Mathf.Max(yawHeat, 1e-3f)) / rec);
            if (heat <= 0f) sprayShots = 0;
        }

        if (!Reloading && EquipLeft <= 0)
        {
            if (burstLeft > 0) ContinueBurst(now, view, mover, rng, movementMode); // a burst finishes whatever the trigger does
            else if (altPressed)
            {
                if (Ammo <= 0) StartReload(infiniteAmmo);
                else if (now >= nextShotAt)
                {
                    FireVolley(now, view, mover, rng, movementMode, Def.AltFire == AltFireKind.Shotgun ? VolleyKind.Blast : VolleyKind.AirBurst);
                    // The blast's own rate (Classic 2.22/s) also holds back the next primary shot (EST).
                    nextShotAt = now + (Def.AltRps > 0f ? 1f / Def.AltRps : Def.Interval);
                }
            }
            else
            {
                bool wantShot = Def.Auto ? triggerHeld : triggerPressed;
                if (wantShot)
                {
                    if (Ammo <= 0) StartReload(infiniteAmmo);
                    else if (burstMode) StartBurst(now, view, mover, rng, movementMode);
                    else
                    {
                        float interval = 1f / CurrentRps;
                        // Only a continuous hold keeps the fixed schedule (FPS-independent auto fire);
                        // a fresh press fires now but never sooner than the gun's interval after the last shot.
                        if (!triggerWasDown || nextShotAt < now - interval) nextShotAt = Mathf.Max(nextShotAt, now);
                        int guard = 0;
                        while (now >= nextShotAt && Ammo > 0 && guard++ < 4)
                        {
                            // This shot's rate schedules the next one: spin-up as it is now, and aimed before a bolt gun
                            // unscopes (the Marshal's ×0.8 scoped rate).
                            interval = 1f / CurrentRps;
                            FireVolley(now, view, mover, rng, movementMode, VolleyKind.Primary);
                            nextShotAt += interval;
                            if (!Def.Auto) { nextShotAt = now + interval; break; }
                        }
                    }
                }
            }
            if (shots.Count > 0 && Ammo <= 0) StartReload(infiniteAmmo);
        }

        // Side swap blends over YawSwitchTime.
        triggerWasDown = Def.Auto && triggerHeld && !Reloading;
        sideBlend = Mathf.MoveToward(sideBlend, side, dt * 2f / Def.YawSwitchTime);

        // Camera follows the recoil (and drifts back as heat recovers).
        bool still = mover.Crouching && mover.Speed < 0.5f, running = movementMode && mover.Speed > mover.WalkMax;
        float crouchMul = still ? Def.CrouchRecoilMul : 1f;
        float runMul = running ? Def.RunPitchMul : 1f, runYawMul = running ? Def.RunYawMul : 1f;
        var pitch = Scoped && Def.AdsPitch.Length > 0 ? Def.AdsPitch : Def.Pitch; // Stinger: own aimed (burst) climb
        view.RecoilPitch = WeaponDef.Lerp(pitch, heat) * crouchMul * runMul;
        view.RecoilYaw = sideBlend * WeaponDef.Lerp(Def.Yaw, yawHeat) * crouchMul * runYawMul;

        SpreadNow = CurrentError(mover, movementMode);
        return shots;
    }

    /// <summary>Aimed burst gun, trigger down: starts a burst on the burst schedule (holding repeats one every BurstCycle;
    /// a fresh press fires now, but never sooner than BurstCycle after the last burst started).</summary>
    void StartBurst(float now, PlayerView view, Mover mover, Random rng, bool movementMode)
    {
        float cycle = BurstCycle;
        if (!triggerWasDown || nextShotAt < now - cycle) nextShotAt = Mathf.Max(nextShotAt, now);
        if (now < nextShotAt) return;
        burstStart = burstNextAt = nextShotAt;
        burstLeft = Def.BurstCount;
        ContinueBurst(now, view, mover, rng, movementMode);
    }

    void ContinueBurst(float now, PlayerView view, Mover mover, Random rng, bool movementMode)
    {
        int guard = 0;
        while (burstLeft > 0 && Ammo > 0 && now >= burstNextAt && guard++ < 4)
        {
            FireVolley(now, view, mover, rng, movementMode, VolleyKind.Primary);
            burstLeft--;
            burstNextAt += 1f / Def.BurstRps;
        }
        if (Ammo <= 0) burstLeft = 0;
        if (burstLeft == 0) nextShotAt = burstStart + BurstCycle;
    }

    float CurrentError(Mover mover, bool movementMode)
    {
        float err, crouch = Def.CrouchSpreadMul;
        if (Def.Scope) err = Scoped ? WeaponDef.Lerp(Def.ScopedSpread, heat) : Def.FirstShotHip; // scoped 0 (Outlaw 2nd shot 2.25°)
        else if (Scoped && Def.AdsSpread.Length > 0)
        {
            err = WeaponDef.Lerp(Def.AdsSpread, heat); // aimed bursts: own curve, above the hip maximum (Stinger, Bulldog)
            if (Def.AltCrouchSpreadMul > 0f) crouch = Def.AltCrouchSpreadMul;
        }
        else
        {
            err = WeaponDef.Lerp(Def.Spread, heat);
            if (Scoped) err = Mathf.Max(0.05f, err - (Def.FirstShotHip - Def.FirstShotAds));
        }
        if (mover.Crouching) err *= crouch;
        if (movementMode) err += mover.MovementErrorDeg(Def);
        return err;
    }

    /// <summary>The pellet cone of a primary shotgun shot: the Spread table at the current heat, crouched ×CrouchSpreadMul.</summary>
    float PelletCone(Mover mover) => WeaponDef.Lerp(Def.Spread, heat) * (mover.Crouching ? Def.CrouchSpreadMul : 1f);

    /// <summary>The Classic's blast cone: the next step of AltSpread while blasts keep coming within the recovery window
    /// (1/AltRps + AltRecoveryDelay + RecoveryTime, EST), else the first step again; crouched ×AltCrouchSpreadMul.</summary>
    float AltCone(float now, Mover mover)
    {
        float window = (Def.AltRps > 0f ? 1f / Def.AltRps : Def.Interval) + Def.AltRecoveryDelay + Def.RecoveryTime;
        altChain = now - lastAlt <= window ? altChain + 1 : 0;
        lastAlt = now;
        float c = Def.AltSpread.Length > 0 ? Def.AltSpread[Math.Min(altChain, Def.AltSpread.Length - 1)] : Def.Spread[0];
        float crouch = Def.AltCrouchSpreadMul > 0f ? Def.AltCrouchSpreadMul : Def.CrouchSpreadMul;
        return mover.Crouching ? c * crouch : c;
    }

    void FireVolley(float now, PlayerView view, Mover mover, Random rng, bool movementMode, VolleyKind kind)
    {
        float gap = now - lastShot;
        lastShot = now;
        int rounds = kind == VolleyKind.Blast ? Math.Clamp(Def.AltAmmoPerShot, 1, Math.Max(1, Ammo)) : 1;
        if (!bottomless) Ammo -= rounds;
        sprayShots++;
        int volley = ++volleyId;
        bool moving = movementMode && !mover.Accurate;

        if (kind == VolleyKind.Primary && Def.Pellets <= 1)
        {
            // Bullet goes where the (recoiled) view points, plus the error cone (the same random draws as before 1.18).
            float err = CurrentError(mover, movementMode);
            LastShotError = err;
            LastConeDeg = 0;
            shots.Add(new Shot(PlayerView.Spread(view.Forward, err, rng, uniform: moving), !moving, volley, 0));
        }
        else
        {
            // Pellet volley: movement error moves the whole pattern; pellet 0 flies at its centre, the rest anywhere in the cone.
            float aimErr = movementMode ? mover.MovementErrorDeg(kind == VolleyKind.Blast ? altMoveDef : Def) : 0f;
            float cone = kind switch
            {
                VolleyKind.Blast => AltCone(now, mover),
                VolleyKind.AirBurst => (Def.AltSpread.Length > 0 ? Def.AltSpread[0] : Def.Spread[0]) * (mover.Crouching ? Def.CrouchSpreadMul : 1f),
                _ => PelletCone(mover),
            };
            int pellets = kind == VolleyKind.Primary ? Def.Pellets : Math.Max(1, Def.AltPellets);
            var centre = PlayerView.Spread(view.Forward, aimErr, rng, uniform: true);
            LastShotError = aimErr;
            LastConeDeg = cone;
            if (kind == VolleyKind.AirBurst)
            {
                // Bucky right click: a hitscan canister to the split point, then AltPellets pellets from there.
                float split = Def.AltBurstDistance;
                shots.Add(new Shot(centre, !moving, volley, 0, 0f, split));
                for (int i = 1; i <= pellets; i++)
                    shots.Add(new Shot(PlayerView.Spread(centre, cone, rng, uniform: true), !moving, volley, i, split, Def.PelletMaxRange));
            }
            else
            {
                shots.Add(new Shot(centre, !moving, volley, 0, 0f, Def.PelletMaxRange));
                for (int i = 1; i < pellets; i++)
                    shots.Add(new Shot(PlayerView.Spread(centre, cone, rng, uniform: true), !moving, volley, i, 0f, Def.PelletMaxRange));
            }
        }

        // Heat grows by one bullet in a spray, less for spaced taps (Tap Efficiency). A volley is one bullet.
        float interval = Def.Interval;
        bool spray = gap <= 1.2f * interval;
        float add = spray || heat <= 0f ? 1f : 1f / (1f + Def.TapEfficiency * (gap - interval) / Def.RecoveryTime);
        heat = Mathf.Min(heat + add, Def.HeatCap);
        yawHeat = Mathf.Min(yawHeat + (spray ? 1f : 0.5f * add), Def.Yaw.Length - 1);
        if (sprayShots > Def.ProtectedBullets && rng.NextDouble() < 0.10) side = -side;

        if (Def.UnscopeOnShot) Scoped = false; // bolt / lever action: unscope after the shot
        Fired?.Invoke();
    }
}
