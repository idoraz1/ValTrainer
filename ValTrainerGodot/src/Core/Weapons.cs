using Gs = ValTrainer.Core.ValorantGs;

namespace ValTrainer.Core;

/// <summary>Guns. Headhunter and TourDeForce are Chamber's ability guns (they reuse the Sheriff / Operator viewmodels and
/// behaviour, see <see cref="WeaponDef.Family"/>).
/// <para>The numbers are stored: the 3D Rewind keeps <c>(int)WeaponKind</c> in its weapon-switch events. Never renumber or
/// reorder; new guns get the next free number. Telemetry, replay headers and stats store the name.</para></summary>
public enum WeaponKind
{
    None = 0, Vandal = 1, Phantom = 2, Sheriff = 3, Operator = 4, Headhunter = 5, TourDeForce = 6,
    Classic = 7, Shorty = 8, Frenzy = 9, Ghost = 10, Stinger = 11, Spectre = 12, Bucky = 13, Judge = 14,
    Bulldog = 15, Guardian = 16, Marshal = 17, Outlaw = 18, Ares = 19, Odin = 20,
    /// <summary>Patch 12.00 sidearm.</summary>
    Bandit = 21,
    /// <summary>Patch 13.06 scoped rifle.</summary>
    Warden = 22,
}

/// <summary>VALORANT's buy-menu column: picker layout, picker sets, the look a gun borrows until it has its own.</summary>
public enum WeaponClass { Sidearm, SMG, Shotgun, Rifle, Sniper, Heavy }

/// <summary>What alt fire (right click) does. <see cref="Zoom"/>: aim down sights; <see cref="Scope"/>: sniper scope;
/// <see cref="ZoomBurst"/>: aims, and the trigger fires bursts while aimed (Stinger, Bulldog); <see cref="Shotgun"/>: a
/// pellet blast from the hip (Classic); <see cref="AirBurst"/>: a canister that splits into pellets (Bucky).
/// (weapons-1.18.md calls ZoomBurst "Burst" and Zoom "Ads".)</summary>
public enum AltFireKind { None, Zoom, Scope, ZoomBurst, Shotgun, AirBurst }

/// <summary>Wall penetration tier (display only: ValTrainer has no wallbangs).</summary>
public enum Penetration { Low, Medium, High }

/// <summary>
/// The recoil calibration: one scale per axis between the game files' recoil curves and the view kick ValTrainer applies
/// (<see cref="WeaponDef.Pitch"/> / <see cref="WeaponDef.Yaw"/> hold the files' numbers as they are). 1.24 reading
/// (docs/research/recoil-1.24.md): the curves are Unreal rotator degrees and VALORANT's camera follows the recoil
/// (the bullet leaves along the kicked view, plus the error cone), which is what ValTrainer does, so both are 1. Medium
/// confidence: recalibrate from a measured spray video (bullet-hole height ÷ distance against the table) by changing these
/// two numbers only.
/// </summary>
public static class RecoilScale
{
    /// <summary>Vertical: in-game degrees of view kick per unit of the files' pitch curve.</summary>
    public const float Pitch = 1f;
    /// <summary>Horizontal: in-game degrees of view kick per unit of the files' yaw curve.</summary>
    public const float Yaw = 1f;
}

/// <summary>
/// Weapon stats from the official wiki / valorant-api / patch notes (checked against 13.09, research: weapons-1.18.md).
/// Since 1.24 the per-bullet recoil and spread tables, recovery, tap efficiency, the aimed / crouched / running multipliers
/// and the movement error come from the game files via <see href="https://valorant.gs">valorant.gs</see> (build 13.10,
/// <see cref="ValorantGs"/>, docs/research/recoil-1.24.md) wherever the files have them; before that they were
/// reconstructed from Riot's documented rules. The drift side, its 10% swap chance after the protected bullets and the
/// protected bullet counts are still ValTrainer's model. All angles in degrees, cumulative from the original aim point.
/// <para>RunSpeed (m/s) is the in-game "Run Speed" stat with the gun in hand, which the player's movement uses (Mover.Hold).
/// VALORANT's tiers: knife 6.75; Classic / Ghost / Frenzy / Bandit / Stinger / Spectre 5.73; Vandal / Phantom / Sheriff /
/// Shorty / Bulldog / Guardian / Warden / Marshal / Outlaw 5.40; Operator / Odin / Ares 5.13; Bucky / Judge 5.06. Chamber's
/// guns have no published number: Headhunter uses the Sheriff's, Tour De Force the Operator's.</para>
/// <para>The init-only properties below the constructor are optional: their defaults are the behaviour every gun had
/// before 1.18. Since 1.18 <see cref="Game.Weapon.WeaponController"/> models pellets, aimed bursts, the Classic's pellet
/// blast and the Bucky's air burst, spin-up, the Operator's second zoom, the Outlaw's second-shot spread and recovery
/// delay, partial and shell-by-shell reloads. Fields marked "data only" (cost, penetration, silenced) feed the picker,
/// sounds and effects but don't change the shooting. <c>--weapon-check</c> (Game/WeaponCheck.cs) tests every gun against
/// these numbers.</para>
/// </summary>
public sealed record WeaponDef(
    WeaponKind Kind, string Name,
    bool Auto, float Rps, float AdsRpsMul, int Mag, int Reserve, float Reload, float Equip,
    float FirstShotHip, float FirstShotAds, float MaxSpread,
    float RecoveryTime, float TapEfficiency, int ProtectedBullets, float YawSwitchTime,
    float Zoom, float[] Pitch, float[] Yaw, float[] Spread, int HeatCap,
    float RunErr, float WalkErr, float CrouchWalkErr, float AirErr,
    float RunSpeed, float[] DamageHead, float[] DamageBody, float[] DamageLegs, float[] DamageRangeEnd)
{
    public float Interval => 1f / Rps;

    /// <summary>The base gun this one plays like (Chamber's guns: Headhunter → Sheriff, Tour De Force → Operator). Only for
    /// behaviour; looks (model, sounds, scope style) come from <see cref="Game.Weapon.WeaponLooks"/>.</summary>
    public WeaponKind Family => Weapons.FamilyOf(Kind);

    // ---------------- buy menu ----------------

    /// <summary>Buy-menu column (picker, weapon sets, borrowed looks).</summary>
    public WeaponClass Class { get; init; } = WeaponClass.Rifle;
    /// <summary>Credits (display only). Chamber's guns: 0.</summary>
    public int Cost { get; init; }
    /// <summary>Wall penetration (display only).</summary>
    public Penetration Penetration { get; init; } = Penetration.Medium;

    // ---------------- scope / aim (used) ----------------

    /// <summary>Sniper scope (used): the scope overlay, zoomed sensitivity, toggle-able scope, hip error =
    /// <see cref="FirstShotHip"/>, scoped error 0. Operator, Marshal, Outlaw, Tour De Force.</summary>
    public bool Scope { get; init; }
    /// <summary>The scope drops after every shot (used): Operator and Marshal. The Outlaw stays zoomed (patch 8.0), and so
    /// does the Tour De Force (assumption).</summary>
    public bool UnscopeOnShot { get; init; }
    /// <summary>Max move speed while aimed / scoped, as a fraction of the run speed (used, Mover.Hold; valorant-api adsStats
    /// runSpeedMultiplier): 0.76 for most guns, Operator 0.72 (patch 1.09: 3.69 m/s), Outlaw 0.80, Marshal 0.90 (patch 2.03).
    /// Chamber's guns: no number, 0.76.</summary>
    public float AdsMoveMul { get; init; } = 0.76f;

    AltFireKind? altFire;
    /// <summary>What right click does (used, AdsSession / WeaponController). Default: <see cref="AltFireKind.Scope"/> for
    /// scoped guns, <see cref="AltFireKind.Zoom"/> when <see cref="Zoom"/> &gt; 1, else none.</summary>
    public AltFireKind AltFire { get => altFire ?? (Scope ? AltFireKind.Scope : Zoom > 1f ? AltFireKind.Zoom : AltFireKind.None); init => altFire = value; }

    // ---------------- recoil / spread multipliers (used) ----------------

    /// <summary>First horizontal drift side (used): -1 = left (Vandal and most guns), +1 = right (Phantom).</summary>
    public int FirstDriftSide { get; init; } = -1;
    /// <summary>Error multiplier while crouched (used; for pellet guns it shrinks the pellet cone). Default 0.85 (rifles,
    /// SMGs, Frenzy; shotguns since patch 12.09). Wiki Stats: Classic / Ghost / Sheriff / Bandit 0.75, snipers 0.9, Ares /
    /// Odin 0.6.</summary>
    public float CrouchSpreadMul { get; init; } = 0.85f;
    /// <summary>Recoil multiplier while crouched and still (used). Default 0.85; Ares / Odin 0.6 (patch 9.05).</summary>
    public float CrouchRecoilMul { get; init; } = 0.85f;
    /// <summary>Vertical recoil multiplier while running (used, movement drills). 1.8 for the Vandal / Phantom / Spectre
    /// (patch 6.11), 1.5 for the Frenzy (patch 6.11); every other gun 1.5 (EST: the pre-6.11 default).</summary>
    public float RunPitchMul { get; init; } = 1.8f;
    /// <summary>Horizontal recoil multiplier while running (used, movement drills). Default 1; Spectre 1.5 (patch 4.0).</summary>
    public float RunYawMul { get; init; } = 1f;
    /// <summary>Accuracy deadzone as a fraction of the run speed (used, Mover.Hold → Mover.AccurateMax): 0.275 (patch 3.0);
    /// Operator 0.15 (patch 1.09; EST that patch 3.0's global deadzone didn't override it).</summary>
    public float DeadzoneMul { get; init; } = 0.275f;
    /// <summary>Recoil multipliers while aimed down sights (used; valorant.gs curves.ads.pitch / yaw): ×0.7 for the Vandal,
    /// Phantom, Warden and Odin, yaw ×0.7 for the Ares. Default 1 (the files leave it out: inherited, EST no change).</summary>
    public float AdsPitchMul { get; init; } = 1f;
    /// <inheritdoc cref="AdsPitchMul"/>
    public float AdsYawMul { get; init; } = 1f;
    /// <summary>Aimed error = hip error × <see cref="AdsErrorMul"/> + <see cref="AdsErrorAdd"/>, at least 0.05° (used;
    /// valorant.gs curves.ads: Vandal ×1.15 − 0.13, which gives its 0.1575° aimed first shot). Without sourced numbers:
    /// ×1 and <see cref="FirstShotAds"/> − <see cref="FirstShotHip"/>, the pre-1.24 rule (the hip error minus the
    /// first-shot difference). Not used by scoped guns or the aimed-burst tables (<see cref="AdsSpread"/>).</summary>
    public float AdsErrorMul { get; init; } = 1f;
    float? adsErrorAdd;
    /// <inheritdoc cref="AdsErrorMul"/>
    public float AdsErrorAdd { get => adsErrorAdd ?? FirstShotAds - FirstShotHip; init => adsErrorAdd = value; }
    /// <summary>Walking error curve (used, Mover): (speed as a fraction of the run speed, degrees) pairs after (0, 0), e.g.
    /// rifles {0.275, 3, 0.601, 5} (valorant.gs movement "walking"). Above the last key the gun has <see cref="RunErr"/>,
    /// at or under the deadzone none. Empty: the pre-1.24 ramp from 0 at the deadzone to <see cref="WalkErr"/> at walk speed
    /// and on to <see cref="RunErr"/> at run speed.</summary>
    public float[] WalkErrKeys { get; init; } = [];
    /// <summary>Suppressed (Ghost, Spectre, Phantom): quieter shot and no enemy-visible tracer. Data only: the shot sound
    /// and muzzle flash (WeaponLooks) read it; the shooting is unchanged.</summary>
    public bool Silenced { get; init; }

    // ---------------- shotguns (used) ----------------

    /// <summary>Pellets per shot (used): Shorty 15, Bucky 15, Judge 12; the damage tables are per pellet. The
    /// <see cref="Spread"/> table is the pellet cone (constant for the Shorty / Bucky, growing for the Judge); movement
    /// error moves the whole pattern. Pellet 0 flies at the pattern's centre (EST: VALORANT's fixed patterns have a centre
    /// pellet; the rest are random in the cone). One trigger pull counts as one shot and at most one hit for drill scoring
    /// (GameSession's volley step); every pellet deals its own damage.</summary>
    public int Pellets { get; init; } = 1;
    /// <summary>Pellets stop at this range (used, m, 0 = no cap): Shorty / Bucky 50 (Wiki).</summary>
    public float PelletMaxRange { get; init; }

    // ---------------- alt fire and bursts (used) ----------------

    /// <summary>Pellets per alt-fire volley: Classic 3 (one blast), Bucky 5 (after the canister splits).</summary>
    public int AltPellets { get; init; }
    /// <summary>Alt-fire blasts per second: Classic 2.22 (0 = the primary rate, e.g. the Bucky's air burst).</summary>
    public float AltRps { get; init; }
    /// <summary>Rounds used per alt-fire blast: Classic 3 (EST; with fewer left, the blast uses what's left).</summary>
    public int AltAmmoPerShot { get; init; } = 1;
    /// <summary>Alt-fire pellet cone per consecutive blast (deg): Classic {1.9, 2.5, 6.0} (patch 2.0); Bucky {2.0} (2.06).</summary>
    public float[] AltSpread { get; init; } = [];
    /// <summary>Delay before alt-fire error recovers (s): Classic 0.1 (patch 2.0). A blast counts as "consecutive" (next step
    /// of <see cref="AltSpread"/>) when it comes within 1/<see cref="AltRps"/> + this delay + <see cref="RecoveryTime"/> of
    /// the previous one (EST).</summary>
    public float AltRecoveryDelay { get; init; }
    /// <summary>Recovery time while aimed in burst mode (s): Stinger 0.4 (patch 2.03), Bulldog 0.35 (patch 4.0).</summary>
    public float AltRecoveryTime { get; init; }
    /// <summary>Alt-fire movement error {crouch-walk, walk, run, air} (deg): Classic {0, 0.6, 1.5, 2.25} (Wiki).</summary>
    public float[] AltMoveErr { get; init; } = [];
    /// <summary>Alt-fire crouch error multiplier (0 = <see cref="CrouchSpreadMul"/>): Classic 0.9, Stinger / Bulldog 0.75.</summary>
    public float AltCrouchSpreadMul { get; init; }
    /// <summary>Distance (m) at which the Bucky's air-burst canister splits: 7.5 (valorant-api airBurstStats). A target
    /// before it takes the canister only (one pellet's damage).</summary>
    public float AltBurstDistance { get; init; }
    /// <summary>Rounds per aimed burst: Stinger 4, Bulldog 3 (1 = no burst). Holding the trigger repeats the bursts every
    /// <see cref="BurstCycle"/>, which gives valorant-api's average ADS fire rate (Stinger 8.47, Bulldog 6.32).</summary>
    public int BurstCount { get; init; } = 1;
    /// <summary>Fire rate inside a burst: Stinger 18, Bulldog 13.333 (Wiki).</summary>
    public float BurstRps { get; init; }
    /// <summary>Burst start to burst start (s): Stinger 0.472, Bulldog 0.475 (Wiki: 2.118 / 2.105 bursts/s).</summary>
    public float BurstCycle { get; init; }
    /// <summary>Own aimed spread curve where "hip minus offset" is wrong (Stinger, Bulldog; EST tables).</summary>
    public float[] AdsSpread { get; init; } = [];
    /// <summary>Own aimed pitch curve (Stinger burst; EST).</summary>
    public float[] AdsPitch { get; init; } = [];

    // ---------------- snipers (used) ----------------

    /// <summary>Second scope level (Operator 5×, valorant-api DualZoom): with toggle scope input, a second alt-fire press
    /// zooms in further and a third leaves the scope. Hold-to-scope stays at the first level.</summary>
    public float Zoom2 { get; init; }
    /// <summary>Scoped error per bullet where it isn't 0: Outlaw {0, 2.25} (patch 13.01).</summary>
    public float[] ScopedSpread { get; init; } = [];
    /// <summary>Delay after a shot before recovery starts (s): Outlaw 0.364 = its shot interval, so patch 13.01's 0.15 s
    /// recovery can't erase the second-shot penalty before the second barrel is ready (EST, research §Outlaw).</summary>
    public float RecoveryDelay { get; init; }
    /// <summary>The view kick is per shot and recovers between shots even while the trigger is held (Judge, measured
    /// from Range footage 2026-11-25: each shot kicks ≈2.1° and settles in ≈0.4 s, no climb, no sideways). Spread still
    /// grows with the spray. Pitch / Yaw then hold the kick of one shot, not a climbing curve.</summary>
    public bool KickPerShot { get; init; }
    /// <summary>Reload with rounds left in the magazine (s): Outlaw 2.3 (empty: <see cref="Reload"/>).</summary>
    public float ReloadPartial { get; init; }
    /// <summary>Shell-by-shell reload (s per round): Bucky 0.5, Marshal 0.5 (Wiki). A trigger pull with a round loaded
    /// stops the reload and fires.</summary>
    public float ReloadPerRound { get; init; }

    // ---------------- heavies (used) ----------------

    /// <summary>Seconds of held fire from the start rate (<see cref="Rps"/>) to the full rate (<see cref="MaxRps"/>),
    /// linear; it winds down at the same speed when the trigger is released (EST). Odin 1.0 (EST; Riot only says it
    /// "increases over time").</summary>
    public float SpinUpTime { get; init; }
    /// <summary>Start rate as a fraction of the full rate: Odin 12 / 15.6 = 0.769.</summary>
    public float SpinUpStartMul { get; init; } = 1f;
    /// <summary>Aimed fire starts at the full rate (Odin: valorant-api adsStats.fireRate 15.6). For such a gun
    /// <see cref="AdsRpsMul"/> is only the cross-check MaxRps / Rps.</summary>
    public bool AdsSkipsSpinUp { get; init; }
    /// <summary>Fire rate after spin-up (= <see cref="Rps"/> for guns without one): demo burst lengths, SimAim.</summary>
    public float MaxRps => SpinUpTime > 0f && SpinUpStartMul > 0f ? Rps / SpinUpStartMul : Rps;

    /// <summary>Damage for a hit zone at a distance (meters).</summary>
    public float Damage(int zone, float dist)
    {
        int i = 0;
        while (i < DamageRangeEnd.Length - 1 && dist > DamageRangeEnd[i]) i++;
        return zone switch { 0 => DamageHead[i], 1 => DamageBody[i], _ => DamageLegs[i] };
    }

    /// <summary>Vertical recoil (degrees, calibrated by <see cref="RecoilScale.Pitch"/>) at a heat (bullets), before the
    /// crouch / running / aimed multipliers. <paramref name="aimedTable"/>: the gun's own aimed pitch curve if it has one.</summary>
    public float PitchAt(float heat, bool aimedTable = false) =>
        Lerp(aimedTable && AdsPitch.Length > 0 ? AdsPitch : Pitch, heat) * RecoilScale.Pitch;
    /// <summary>Horizontal recoil (degrees, calibrated by <see cref="RecoilScale.Yaw"/>, before the drift side) at a heat.</summary>
    public float YawAt(float heat) => Lerp(Yaw, heat) * RecoilScale.Yaw;

    /// <summary>Linear interpolation into a per-bullet table at a fractional bullet index (0-based).</summary>
    public static float Lerp(float[] table, float index)
    {
        if (table.Length == 0) return 0;
        if (index <= 0) return table[0];
        int i = (int)index;
        if (i >= table.Length - 1) return table[^1];
        float f = index - i;
        return table[i] + (table[i + 1] - table[i]) * f;
    }
}

/// <summary>
/// Every gun's stats. Sources per field are in docs/research/weapons-1.18.md. Riot publishes fire rate, magazine, reload,
/// equip, first-shot and max error, movement error, run speed, zoom and damage; everything marked EST (and every per-bullet
/// Pitch / Yaw / Spread table between its sourced end points) is an estimate.
/// </summary>
public static class Weapons
{
    static float[] Pad(float[] v, int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = v[Math.Min(i, v.Length - 1)]; return r; }

    // ================= sidearms =================
    // 1.24: "Gs.X" = the game files via valorant.gs (ValorantGs.g.cs, regenerate with tools/valorant-gs/import_recoil.py).
    // Recoil tables hold the files' numbers; RecoilScale calibrates them. A field without Gs is ValTrainer's own (the files
    // leave it at a blueprint default the site can't see).

    /// <summary>Classic (free sidearm). Tables, recovery 0.3, tap efficiency 4, movement error: game files (valorant.gs);
    /// no horizontal recoil in the files. Protected bullets 2 EST. Right click is a 3-pellet blast (2.22 per second, 3 rounds
    /// each, cone 1.9° → 2.5° → 6° when spammed). Crouch spread ×0.75 (Wiki; not in the files).</summary>
    public static readonly WeaponDef Classic = new(
        WeaponKind.Classic, "Classic", Auto: false, Rps: 6.75f, AdsRpsMul: 1f, Mag: 12, Reserve: 36, Reload: 1.75f, Equip: 0.75f,
        FirstShotHip: Gs.Classic.FirstShotHip, FirstShotAds: Gs.Classic.FirstShotHip, MaxSpread: Gs.Classic.MaxSpread,
        RecoveryTime: Gs.Classic.RecoveryTime, TapEfficiency: Gs.Classic.TapEfficiency, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: Gs.Classic.Pitch, Yaw: Gs.Classic.Yaw, Spread: Gs.Classic.Spread, HeatCap: Gs.Classic.HeatCap,
        RunErr: Gs.Classic.RunErr, WalkErr: Gs.Classic.WalkErr, CrouchWalkErr: Gs.Classic.CrouchWalkErr, AirErr: Gs.Classic.AirErr, RunSpeed: 5.73f,
        DamageHead: new[] { 78f, 66f }, DamageBody: new[] { 26f, 22f }, DamageLegs: new[] { 22.1f, 18.7f }, DamageRangeEnd: new[] { 30f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 0, Penetration = Penetration.Low,
        CrouchSpreadMul = 0.75f, RunPitchMul = 1.5f /* EST: pre-6.11 default */, WalkErrKeys = Gs.Classic.WalkErrKeys,
        AltFire = AltFireKind.Shotgun, AltPellets = 3, AltRps = 2.22f, AltAmmoPerShot = 3 /* EST */,
        AltSpread = new[] { 1.9f, 2.5f, 6.0f }, AltRecoveryDelay = 0.1f,
        AltMoveErr = new[] { 0f, 0.6f, 1.5f, 2.25f } /* crouch-walk, walk, run, air */, AltCrouchSpreadMul = 0.9f,
    };

    /// <summary>Shorty: 2-shell shotgun sidearm, 15 pellets (damage per pellet), 4° cone, 4.5° for a shot fired before it
    /// recovers (game files). Recoil and recovery EST (the files leave them at the blueprint default). Crouch ×0.85.</summary>
    public static readonly WeaponDef Shorty = new(
        WeaponKind.Shorty, "Shorty", Auto: false, Rps: 3f, AdsRpsMul: 1f, Mag: 2, Reserve: 6, Reload: 1.75f, Equip: 0.75f,
        FirstShotHip: Gs.Shorty.FirstShotHip, FirstShotAds: Gs.Shorty.FirstShotHip, MaxSpread: Gs.Shorty.MaxSpread,
        RecoveryTime: 0.35f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: new[] { 0f, 2f }, Yaw: new[] { 0f, 0.3f },
        Spread: Gs.Shorty.Spread, HeatCap: Gs.Shorty.HeatCap,
        RunErr: Gs.Shorty.RunErr, WalkErr: Gs.Shorty.WalkErr, CrouchWalkErr: Gs.Shorty.CrouchWalkErr, AirErr: Gs.Shorty.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 22f, 12f, 6f }, DamageBody: new[] { 11f, 6f, 3f }, DamageLegs: new[] { 9.35f, 5.1f, 2.55f }, DamageRangeEnd: new[] { 7f, 15f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 300, Penetration = Penetration.Low, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Shorty.CrouchSpreadMul, WalkErrKeys = Gs.Shorty.WalkErrKeys,
        Pellets = 15, PelletMaxRange = 50f,
    };

    /// <summary>Frenzy: full-auto machine pistol. Tables (max spread and climb at bullet 5), recovery 0.45, tap efficiency 3,
    /// crouch ×0.85, running yaw ×1.25, movement error: game files. Running pitch ×1.5 (patch 6.11; not in the files).</summary>
    public static readonly WeaponDef Frenzy = new(
        WeaponKind.Frenzy, "Frenzy", Auto: true, Rps: 10f, AdsRpsMul: 1f, Mag: 15, Reserve: 45, Reload: 1.5f, Equip: 1f,
        FirstShotHip: Gs.Frenzy.FirstShotHip, FirstShotAds: Gs.Frenzy.FirstShotHip, MaxSpread: Gs.Frenzy.MaxSpread,
        RecoveryTime: Gs.Frenzy.RecoveryTime, TapEfficiency: Gs.Frenzy.TapEfficiency, ProtectedBullets: 3, YawSwitchTime: 0.3f, Zoom: 1f,
        Pitch: Gs.Frenzy.Pitch, Yaw: Gs.Frenzy.Yaw, Spread: Gs.Frenzy.Spread, HeatCap: Gs.Frenzy.HeatCap,
        RunErr: Gs.Frenzy.RunErr, WalkErr: Gs.Frenzy.WalkErr, CrouchWalkErr: Gs.Frenzy.CrouchWalkErr, AirErr: Gs.Frenzy.AirErr, RunSpeed: 5.73f,
        DamageHead: new[] { 78f, 63f }, DamageBody: new[] { 26f, 21f }, DamageLegs: new[] { 22.1f, 17.85f }, DamageRangeEnd: new[] { 20f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 450, Penetration = Penetration.Low,
        RunPitchMul = 1.5f, RunYawMul = Gs.Frenzy.RunYawMul,
        CrouchSpreadMul = Gs.Frenzy.CrouchSpreadMul, CrouchRecoilMul = Gs.Frenzy.CrouchRecoilMul, WalkErrKeys = Gs.Frenzy.WalkErrKeys,
    };

    /// <summary>Ghost: silenced semi-auto pistol. Tables, recovery 0.3, tap efficiency 4, movement error: game files (no
    /// horizontal recoil). Crouch ×0.75 (Wiki; not in the files).</summary>
    public static readonly WeaponDef Ghost = new(
        WeaponKind.Ghost, "Ghost", Auto: false, Rps: 6.75f, AdsRpsMul: 1f, Mag: 13, Reserve: 39, Reload: 1.5f, Equip: 0.75f,
        FirstShotHip: Gs.Ghost.FirstShotHip, FirstShotAds: Gs.Ghost.FirstShotHip, MaxSpread: Gs.Ghost.MaxSpread,
        RecoveryTime: Gs.Ghost.RecoveryTime, TapEfficiency: Gs.Ghost.TapEfficiency, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: Gs.Ghost.Pitch, Yaw: Gs.Ghost.Yaw, Spread: Gs.Ghost.Spread, HeatCap: Gs.Ghost.HeatCap,
        RunErr: Gs.Ghost.RunErr, WalkErr: Gs.Ghost.WalkErr, CrouchWalkErr: Gs.Ghost.CrouchWalkErr, AirErr: Gs.Ghost.AirErr, RunSpeed: 5.73f,
        DamageHead: new[] { 105f, 87.5f }, DamageBody: new[] { 30f, 25f }, DamageLegs: new[] { 25.5f, 21.25f }, DamageRangeEnd: new[] { 30f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 500, Penetration = Penetration.Medium,
        CrouchSpreadMul = 0.75f, RunPitchMul = 1.5f /* EST */, Silenced = true, WalkErrKeys = Gs.Ghost.WalkErrKeys,
    };

    /// <summary>Bandit (patch 12.00, tuned in 13.00): semi-auto sidearm, headshot ×3.9 inside 10 m. Tables (pitch plateau
    /// 3°, max error 2°), recovery 0.4, tap efficiency 4, movement error: game files (no horizontal recoil). Sourced elsewhere:
    /// rate, magazine, reload, equip, damage, run speed (valorant-api 0.85). EST: protected bullets, yaw switch.</summary>
    public static readonly WeaponDef Bandit = new(
        WeaponKind.Bandit, "Bandit", Auto: false, Rps: 5.1f, AdsRpsMul: 1f, Mag: 8, Reserve: 24, Reload: 1.5f, Equip: 0.75f,
        FirstShotHip: Gs.Bandit.FirstShotHip, FirstShotAds: Gs.Bandit.FirstShotHip, MaxSpread: Gs.Bandit.MaxSpread,
        RecoveryTime: Gs.Bandit.RecoveryTime, TapEfficiency: Gs.Bandit.TapEfficiency, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: Gs.Bandit.Pitch, Yaw: Gs.Bandit.Yaw, Spread: Gs.Bandit.Spread, HeatCap: Gs.Bandit.HeatCap,
        RunErr: Gs.Bandit.RunErr, WalkErr: Gs.Bandit.WalkErr, CrouchWalkErr: Gs.Bandit.CrouchWalkErr, AirErr: Gs.Bandit.AirErr, RunSpeed: 5.73f,
        DamageHead: new[] { 152f, 128f, 112f }, DamageBody: new[] { 39f, 39f, 34f }, DamageLegs: new[] { 33f, 33f, 28f }, DamageRangeEnd: new[] { 10f, 30f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 600, Penetration = Penetration.Medium,
        CrouchSpreadMul = 0.75f, RunPitchMul = 1.5f /* EST */, WalkErrKeys = Gs.Bandit.WalkErrKeys,
    };

    /// <summary>Sheriff (verified against 13.09). Tables (1° then 2° kick, error 0.25° → 1.1° → 2.75°), recovery 0.5, tap
    /// efficiency 2, movement error: game files (no horizontal recoil). Crouch spread ×0.75 (Wiki; not in the files).</summary>
    public static readonly WeaponDef Sheriff = new(
        WeaponKind.Sheriff, "Sheriff", Auto: false, Rps: 4f, AdsRpsMul: 1f, Mag: 6, Reserve: 24, Reload: 2.25f, Equip: 1.0f,
        FirstShotHip: Gs.Sheriff.FirstShotHip, FirstShotAds: Gs.Sheriff.FirstShotHip, MaxSpread: Gs.Sheriff.MaxSpread,
        RecoveryTime: Gs.Sheriff.RecoveryTime, TapEfficiency: Gs.Sheriff.TapEfficiency, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: Gs.Sheriff.Pitch, Yaw: Gs.Sheriff.Yaw, Spread: Gs.Sheriff.Spread, HeatCap: Gs.Sheriff.HeatCap,
        RunErr: Gs.Sheriff.RunErr, WalkErr: Gs.Sheriff.WalkErr, CrouchWalkErr: Gs.Sheriff.CrouchWalkErr, AirErr: Gs.Sheriff.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 159.5f, 145f }, DamageBody: new[] { 55f, 50f }, DamageLegs: new[] { 46.75f, 42.5f }, DamageRangeEnd: new[] { 30f, 999f })
    {
        Class = WeaponClass.Sidearm, Cost = 800, Penetration = Penetration.High,
        CrouchSpreadMul = 0.75f, RunPitchMul = 1.5f /* EST: pre-6.11 default (1.8 before 1.18) */, WalkErrKeys = Gs.Sheriff.WalkErrKeys,
    };

    // ================= SMGs =================

    /// <summary>Stinger: 16 rps SMG, aimed = 4-round bursts at 18 rps, a burst every 0.472 s (AdsRpsMul 0.5294 = that
    /// average, valorant-api). Hip tables, recovery 0.45, tap efficiency 1, crouch ×0.85, running ×1.25 pitch and yaw,
    /// movement error: game files. The aimed burst tables stay EST (the files' aimed numbers aren't in the API).</summary>
    public static readonly WeaponDef Stinger = new(
        WeaponKind.Stinger, "Stinger", Auto: true, Rps: 16f, AdsRpsMul: 0.5294f, Mag: 20, Reserve: 60, Reload: 2.25f, Equip: 0.75f,
        FirstShotHip: Gs.Stinger.FirstShotHip, FirstShotAds: Gs.Stinger.FirstShotAds, MaxSpread: Gs.Stinger.MaxSpread,
        RecoveryTime: Gs.Stinger.RecoveryTime, TapEfficiency: Gs.Stinger.TapEfficiency, ProtectedBullets: 3, YawSwitchTime: 0.25f, Zoom: 1.15f,
        Pitch: Gs.Stinger.Pitch, Yaw: Gs.Stinger.Yaw, Spread: Gs.Stinger.Spread, HeatCap: Gs.Stinger.HeatCap,
        RunErr: Gs.Stinger.RunErr, WalkErr: Gs.Stinger.WalkErr, CrouchWalkErr: Gs.Stinger.CrouchWalkErr, AirErr: 10f, RunSpeed: 5.73f,
        DamageHead: new[] { 67.5f, 57f }, DamageBody: new[] { 27f, 23f }, DamageLegs: new[] { 22.95f, 19f }, DamageRangeEnd: new[] { 15f, 999f })
    {
        Class = WeaponClass.SMG, Cost = 1100, Penetration = Penetration.Low,
        RunPitchMul = Gs.Stinger.RunPitchMul, RunYawMul = Gs.Stinger.RunYawMul,
        CrouchSpreadMul = Gs.Stinger.CrouchSpreadMul, CrouchRecoilMul = Gs.Stinger.CrouchRecoilMul, WalkErrKeys = Gs.Stinger.WalkErrKeys,
        AltFire = AltFireKind.ZoomBurst, BurstCount = 4, BurstRps = 18f, BurstCycle = 0.472f /* 2.118 bursts/s */,
        AdsSpread = new[] { 0.35f, 0.6f, 0.9f, 1.2f, 1.7f, 2.2f, 2.74f } /* EST */,
        AdsPitch = new[] { 0f, 0.35f, 0.8f, 1.3f, 2.0f, 2.6f, 3.1f, 3.4f } /* EST */,
        AltRecoveryTime = 0.4f, AltCrouchSpreadMul = 0.75f,
    };

    /// <summary>Spectre: silenced SMG. Tables (5.4° climb by bullet 11, horizontal from bullet 7), recovery 0.45, tap
    /// efficiency 3, aimed error −0.15°, crouch ×0.85, running pitch ×1.8, movement error: game files. Protected 5, yaw switch
    /// 0.28 s (patch 11.08), running yaw ×1.5 (patch 4.0; not in the files).</summary>
    public static readonly WeaponDef Spectre = new(
        WeaponKind.Spectre, "Spectre", Auto: true, Rps: 13.333f, AdsRpsMul: 0.9f, Mag: 30, Reserve: 90, Reload: 2.25f, Equip: 0.75f,
        FirstShotHip: Gs.Spectre.FirstShotHip, FirstShotAds: Gs.Spectre.FirstShotAds, MaxSpread: Gs.Spectre.MaxSpread,
        RecoveryTime: Gs.Spectre.RecoveryTime, TapEfficiency: Gs.Spectre.TapEfficiency, ProtectedBullets: 5, YawSwitchTime: 0.28f, Zoom: 1.15f,
        Pitch: Gs.Spectre.Pitch, Yaw: Gs.Spectre.Yaw, Spread: Gs.Spectre.Spread, HeatCap: Gs.Spectre.HeatCap,
        RunErr: Gs.Spectre.RunErr, WalkErr: Gs.Spectre.WalkErr, CrouchWalkErr: Gs.Spectre.CrouchWalkErr, AirErr: Gs.Spectre.AirErr, RunSpeed: 5.73f,
        DamageHead: new[] { 78f, 66f, 60f }, DamageBody: new[] { 26f, 22f, 20f }, DamageLegs: new[] { 22.1f, 18.7f, 17f }, DamageRangeEnd: new[] { 15f, 30f, 999f })
    {
        Class = WeaponClass.SMG, Cost = 1600, Penetration = Penetration.Low,
        Silenced = true, RunPitchMul = Gs.Spectre.RunPitchMul, RunYawMul = 1.5f,
        CrouchSpreadMul = Gs.Spectre.CrouchSpreadMul, CrouchRecoilMul = Gs.Spectre.CrouchRecoilMul,
        AdsErrorAdd = Gs.Spectre.AdsErrorAdd, WalkErrKeys = Gs.Spectre.WalkErrKeys,
    };

    // ================= shotguns =================

    /// <summary>Bucky: pump shotgun, 15 pellets (damage per pellet), constant 3° cone, no recoil (game files: pitch 0; the
    /// yaw curve is absent, EST 0 too); right click = air-burst canister (5 pellets at 7.5 m); reloads a shell at a time
    /// (0.5 s). Recovery EST.</summary>
    public static readonly WeaponDef Bucky = new(
        WeaponKind.Bucky, "Bucky", Auto: false, Rps: 1.1f, AdsRpsMul: 1f, Mag: 5, Reserve: 10, Reload: 2.5f, Equip: 1f,
        FirstShotHip: Gs.Bucky.FirstShotHip, FirstShotAds: Gs.Bucky.FirstShotHip, MaxSpread: Gs.Bucky.MaxSpread,
        RecoveryTime: 0.8f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: Gs.Bucky.Pitch, Yaw: new[] { 0f } /* EST: absent in the files */,
        Spread: Gs.Bucky.Spread, HeatCap: Gs.Bucky.HeatCap,
        RunErr: Gs.Bucky.RunErr, WalkErr: Gs.Bucky.WalkErr, CrouchWalkErr: Gs.Bucky.CrouchWalkErr, AirErr: Gs.Bucky.AirErr, RunSpeed: 5.06f,
        DamageHead: new[] { 34f, 26f, 18f }, DamageBody: new[] { 17f, 13f, 9f }, DamageLegs: new[] { 14f, 11.05f, 7.65f }, DamageRangeEnd: new[] { 8f, 12f, 999f })
    {
        Class = WeaponClass.Shotgun, Cost = 850, Penetration = Penetration.Low, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Bucky.CrouchSpreadMul, WalkErrKeys = Gs.Bucky.WalkErrKeys,
        Pellets = 15, PelletMaxRange = 50f,
        AltFire = AltFireKind.AirBurst, AltPellets = 5, AltBurstDistance = 7.5f, AltSpread = new[] { 2.0f },
        ReloadPerRound = 0.5f,
    };

    /// <summary>Judge: full-auto shotgun, 12 pellets (damage per pellet), no 50 m pellet cap. Cone 2.5° → 4°, recovery 0.4,
    /// tap efficiency 4, movement error: game files (valorant.gs). Kick: MEASURED, not the files' curve (whose 5° climb by
    /// the 4th shot the game doesn't show: the Judge has no recoil-rate block). Range footage 2026-11-25, 13 single shots:
    /// up 2.08 ± 0.08°, sideways 0.03 ± 0.10°, back in ≈0.4 s; held at full rate every shot kicks ≈2.0–2.7° and comes
    /// back (research spray-measure.md).</summary>
    public static readonly WeaponDef Judge = new(
        WeaponKind.Judge, "Judge", Auto: true, Rps: 3.5f, AdsRpsMul: 1f, Mag: 5, Reserve: 15, Reload: 2.2f, Equip: 1f,
        FirstShotHip: Gs.Judge.FirstShotHip, FirstShotAds: Gs.Judge.FirstShotHip, MaxSpread: Gs.Judge.MaxSpread,
        RecoveryTime: Gs.Judge.RecoveryTime, TapEfficiency: Gs.Judge.TapEfficiency, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: new[] { 0f, 2.08f, 2.08f }, Yaw: new[] { 0f, 0f, 0f }, Spread: Gs.Judge.Spread, HeatCap: Gs.Judge.HeatCap,
        RunErr: Gs.Judge.RunErr, WalkErr: Gs.Judge.WalkErr, CrouchWalkErr: Gs.Judge.CrouchWalkErr, AirErr: Gs.Judge.AirErr, RunSpeed: 5.06f,
        DamageHead: new[] { 34f, 20f, 14f }, DamageBody: new[] { 17f, 10f, 7f }, DamageLegs: new[] { 14.45f, 8.5f, 5.95f }, DamageRangeEnd: new[] { 10f, 15f, 999f })
    {
        Class = WeaponClass.Shotgun, Cost = 1850, Penetration = Penetration.Low, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Judge.CrouchSpreadMul, WalkErrKeys = Gs.Judge.WalkErrKeys,
        Pellets = 12, KickPerShot = true,
    };

    // ================= rifles =================

    /// <summary>Bulldog: aimed = 3-round bursts at 13.333 rps, a burst every 0.475 s (AdsRpsMul 0.6316 = that average,
    /// valorant-api). Tables (horizontal drifts one way for 5 bullets, then swings to the other side), recovery 0.35, tap
    /// efficiency 3, crouch ×0.85, jump error 13°, movement error: game files. The files have no aimed multipliers for it
    /// (aimed first shot 0.3° = hip), so the aimed bursts use the hip error curve. Yaw switch 0.6 s (11.08), protected
    /// bullets EST.</summary>
    public static readonly WeaponDef Bulldog = new(
        WeaponKind.Bulldog, "Bulldog", Auto: true, Rps: 10f, AdsRpsMul: 0.6316f, Mag: 24, Reserve: 72, Reload: 2.5f, Equip: 1f,
        FirstShotHip: Gs.Bulldog.FirstShotHip, FirstShotAds: Gs.Bulldog.FirstShotAds, MaxSpread: Gs.Bulldog.MaxSpread,
        RecoveryTime: Gs.Bulldog.RecoveryTime, TapEfficiency: Gs.Bulldog.TapEfficiency, ProtectedBullets: 4, YawSwitchTime: 0.6f, Zoom: 1.25f,
        Pitch: Gs.Bulldog.Pitch, Yaw: Gs.Bulldog.Yaw, Spread: Gs.Bulldog.Spread, HeatCap: Gs.Bulldog.HeatCap,
        RunErr: Gs.Bulldog.RunErr, WalkErr: Gs.Bulldog.WalkErr, CrouchWalkErr: Gs.Bulldog.CrouchWalkErr, AirErr: Gs.Bulldog.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 115.5f }, DamageBody: new[] { 35f }, DamageLegs: new[] { 29.75f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Rifle, Cost = 2050, Penetration = Penetration.Medium, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Bulldog.CrouchSpreadMul, CrouchRecoilMul = Gs.Bulldog.CrouchRecoilMul, WalkErrKeys = Gs.Bulldog.WalkErrKeys,
        AltFire = AltFireKind.ZoomBurst, BurstCount = 3, BurstRps = 13.333f, BurstCycle = 0.475f /* 2.105 bursts/s */,
        AdsSpread = Gs.Bulldog.Spread /* no aimed multipliers in the files */,
        AltRecoveryTime = 0.35f, AltCrouchSpreadMul = 0.75f,
    };

    /// <summary>Guardian: semi-auto rifle. Tables (error 0.1° → 1.6° by the 4th spammed shot, 5° climb, horizontal −1.25°),
    /// recovery 0.295, tap efficiency 10, aimed error −0.1°, crouch ×0.85, jump error 13.5°, movement error: game files.
    /// Protected bullets EST.</summary>
    public static readonly WeaponDef Guardian = new(
        WeaponKind.Guardian, "Guardian", Auto: false, Rps: 5.25f, AdsRpsMul: 1f, Mag: 12, Reserve: 36, Reload: 2.5f, Equip: 1f,
        FirstShotHip: Gs.Guardian.FirstShotHip, FirstShotAds: 0f, MaxSpread: Gs.Guardian.MaxSpread,
        RecoveryTime: Gs.Guardian.RecoveryTime, TapEfficiency: Gs.Guardian.TapEfficiency, ProtectedBullets: 3, YawSwitchTime: 0.6f, Zoom: 1.5f,
        Pitch: Gs.Guardian.Pitch, Yaw: Gs.Guardian.Yaw, Spread: Gs.Guardian.Spread, HeatCap: Gs.Guardian.HeatCap,
        RunErr: Gs.Guardian.RunErr, WalkErr: Gs.Guardian.WalkErr, CrouchWalkErr: Gs.Guardian.CrouchWalkErr, AirErr: Gs.Guardian.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 195f }, DamageBody: new[] { 65f }, DamageLegs: new[] { 48.75f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Rifle, Cost = 2250, Penetration = Penetration.High, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Guardian.CrouchSpreadMul, CrouchRecoilMul = Gs.Guardian.CrouchRecoilMul,
        AdsErrorAdd = Gs.Guardian.AdsErrorAdd, WalkErrKeys = Gs.Guardian.WalkErrKeys,
    };

    /// <summary>Phantom (verified against 13.09): silenced, drifts right first. Tables (6.4° climb by bullet 8, then a
    /// sawtooth around 6.5–7.1°; horizontal 1.8° by bullet 14, then swinging ±0.1°), recovery, tap efficiency, aimed
    /// ×1.15 − 0.12 error and ×0.7 recoil, crouch ×0.85, running pitch ×1.8, jump error 13°, movement error: game files.</summary>
    public static readonly WeaponDef Phantom = new(
        WeaponKind.Phantom, "Phantom", Auto: true, Rps: 11f, AdsRpsMul: 0.9f, Mag: 30, Reserve: 60, Reload: 2.5f, Equip: 1.0f,
        FirstShotHip: Gs.Phantom.FirstShotHip, FirstShotAds: Gs.Phantom.FirstShotAds, MaxSpread: Gs.Phantom.MaxSpread,
        RecoveryTime: Gs.Phantom.RecoveryTime, TapEfficiency: Gs.Phantom.TapEfficiency, ProtectedBullets: 8, YawSwitchTime: 0.6f, Zoom: 1.25f,
        Pitch: Gs.Phantom.Pitch, Yaw: Gs.Phantom.Yaw, Spread: Gs.Phantom.Spread, HeatCap: Gs.Phantom.HeatCap,
        RunErr: Gs.Phantom.RunErr, WalkErr: Gs.Phantom.WalkErr, CrouchWalkErr: Gs.Phantom.CrouchWalkErr, AirErr: Gs.Phantom.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 156f, 140f }, DamageBody: new[] { 39f, 35f }, DamageLegs: new[] { 33f, 29.75f }, DamageRangeEnd: new[] { 20f, 999f })
    {
        Class = WeaponClass.Rifle, Cost = 2900, Penetration = Penetration.Medium,
        Silenced = true, FirstDriftSide = 1, RunPitchMul = Gs.Phantom.RunPitchMul,
        CrouchSpreadMul = Gs.Phantom.CrouchSpreadMul, CrouchRecoilMul = Gs.Phantom.CrouchRecoilMul,
        AdsErrorMul = Gs.Phantom.AdsErrorMul, AdsErrorAdd = Gs.Phantom.AdsErrorAdd, AdsPitchMul = Gs.Phantom.AdsPitchMul, AdsYawMul = Gs.Phantom.AdsYawMul,
        WalkErrKeys = Gs.Phantom.WalkErrKeys,
    };

    /// <summary>Vandal (verified against 13.09). Tables (7.6° climb by bullet 8, then a sawtooth around 7.5–8.4°;
    /// horizontal 2° by bullet 13, then swinging 1.92–2.12°), recovery 0.375, tap efficiency 6, aimed ×1.15 − 0.13 error and
    /// ×0.7 recoil, crouch ×0.85, running pitch ×1.8, movement error: game files. Jump error 10° (not in the files).</summary>
    public static readonly WeaponDef Vandal = new(
        WeaponKind.Vandal, "Vandal", Auto: true, Rps: 9.75f, AdsRpsMul: 0.9f, Mag: 25, Reserve: 50, Reload: 2.5f, Equip: 1.0f,
        FirstShotHip: Gs.Vandal.FirstShotHip, FirstShotAds: Gs.Vandal.FirstShotAds, MaxSpread: Gs.Vandal.MaxSpread,
        RecoveryTime: Gs.Vandal.RecoveryTime, TapEfficiency: Gs.Vandal.TapEfficiency, ProtectedBullets: 6, YawSwitchTime: 0.6f, Zoom: 1.25f,
        Pitch: Gs.Vandal.Pitch, Yaw: Gs.Vandal.Yaw, Spread: Gs.Vandal.Spread, HeatCap: Gs.Vandal.HeatCap,
        RunErr: Gs.Vandal.RunErr, WalkErr: Gs.Vandal.WalkErr, CrouchWalkErr: Gs.Vandal.CrouchWalkErr, AirErr: 10f, RunSpeed: 5.4f,
        DamageHead: new[] { 160f }, DamageBody: new[] { 40f }, DamageLegs: new[] { 34f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Rifle, Cost = 2900, Penetration = Penetration.Medium, RunPitchMul = Gs.Vandal.RunPitchMul,
        CrouchSpreadMul = Gs.Vandal.CrouchSpreadMul, CrouchRecoilMul = Gs.Vandal.CrouchRecoilMul,
        AdsErrorMul = Gs.Vandal.AdsErrorMul, AdsErrorAdd = Gs.Vandal.AdsErrorAdd, AdsPitchMul = Gs.Vandal.AdsPitchMul, AdsYawMul = Gs.Vandal.AdsYawMul,
        WalkErrKeys = Gs.Vandal.WalkErrKeys,
    };

    /// <summary>Warden (patch 13.06, buffed in 13.09): full-auto rifle with a 2× scope (modelled as a zoom like the rifles'
    /// ADS, EST). Tables, recovery 0.4, tap efficiency 9, aimed ×1.15 − 0.13 error and ×0.7 recoil, crouch ×0.85, movement
    /// error: game files. Sourced elsewhere (valorant-api / patch notes): rate, magazine, reload, equip, damage, zoom, run and
    /// aimed speed. EST: protected bullets, jump error and running pitch (not in the files).</summary>
    public static readonly WeaponDef Warden = new(
        WeaponKind.Warden, "Warden", Auto: true, Rps: 6.5f, AdsRpsMul: 1f, Mag: 18, Reserve: 36, Reload: 2.5f, Equip: 1f,
        FirstShotHip: Gs.Warden.FirstShotHip, FirstShotAds: Gs.Warden.FirstShotAds, MaxSpread: Gs.Warden.MaxSpread,
        RecoveryTime: Gs.Warden.RecoveryTime, TapEfficiency: Gs.Warden.TapEfficiency, ProtectedBullets: 4, YawSwitchTime: 0.6f, Zoom: 2f,
        Pitch: Gs.Warden.Pitch, Yaw: Gs.Warden.Yaw, Spread: Gs.Warden.Spread, HeatCap: Gs.Warden.HeatCap,
        RunErr: Gs.Warden.RunErr, WalkErr: Gs.Warden.WalkErr, CrouchWalkErr: Gs.Warden.CrouchWalkErr, AirErr: 10f, RunSpeed: 5.4f,
        DamageHead: new[] { 200f }, DamageBody: new[] { 50f }, DamageLegs: new[] { 42f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Rifle, Cost = 2900, Penetration = Penetration.Medium, RunPitchMul = 1.5f /* EST */,
        CrouchSpreadMul = Gs.Warden.CrouchSpreadMul, CrouchRecoilMul = Gs.Warden.CrouchRecoilMul,
        AdsErrorMul = Gs.Warden.AdsErrorMul, AdsErrorAdd = Gs.Warden.AdsErrorAdd, AdsPitchMul = Gs.Warden.AdsPitchMul, AdsYawMul = Gs.Warden.AdsYawMul,
        WalkErrKeys = Gs.Warden.WalkErrKeys,
    };

    // ================= snipers =================
    // Scoped guns keep their own hip error (FirstShotHip) and Spread: the files' small growth after a shot (Marshal 1° →
    // 1.25°, Operator 5° → 5.25°) isn't modelled for scoped guns.

    /// <summary>Marshal: lever-action sniper, 3.5× scope, scoped move ×0.90, hip error only 1°. No recoil (game files),
    /// movement error (jump 20°): game files. Unscoping after each shot and recovery 0.65 are EST. Scoped fire rate ×0.8;
    /// reloads a round at a time (0.5 s).</summary>
    public static readonly WeaponDef Marshal = new(
        WeaponKind.Marshal, "Marshal", Auto: false, Rps: 1.5f, AdsRpsMul: 0.8f, Mag: 5, Reserve: 15, Reload: 2.5f, Equip: 1.25f,
        FirstShotHip: 1f, FirstShotAds: 0f, MaxSpread: 1f,
        RecoveryTime: 0.65f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 3.5f,
        Pitch: new[] { 0f }, Yaw: new[] { 0f }, Spread: new[] { 1f }, HeatCap: 1,
        RunErr: Gs.Marshal.RunErr, WalkErr: Gs.Marshal.WalkErr, CrouchWalkErr: Gs.Marshal.CrouchWalkErr, AirErr: Gs.Marshal.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 202f }, DamageBody: new[] { 101f }, DamageLegs: new[] { 85.85f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Sniper, Cost = 950, Penetration = Penetration.Medium,
        Scope = true, UnscopeOnShot = true /* EST */, AdsMoveMul = 0.9f, CrouchSpreadMul = Gs.Marshal.CrouchSpreadMul,
        WalkErrKeys = Gs.Marshal.WalkErrKeys,
        ReloadPerRound = 0.5f,
    };

    /// <summary>Outlaw: double-barrel sniper that stays zoomed (patch 8.0). Second-shot kick 3° (game files, build 13.10;
    /// patch 13.01 said 4), second-shot scoped spread 2.25° (patch 13.01), recovery 0.15 s, which starts after the shot
    /// interval (<see cref="WeaponDef.RecoveryDelay"/>, EST). Movement error (jump 20°): game files. Reload 3.8 s empty,
    /// 2.3 s with a round left.</summary>
    public static readonly WeaponDef Outlaw = new(
        WeaponKind.Outlaw, "Outlaw", Auto: false, Rps: 2.75f, AdsRpsMul: 1f, Mag: 2, Reserve: 10, Reload: 3.8f, Equip: 1.25f,
        FirstShotHip: Gs.Outlaw.FirstShotHip, FirstShotAds: 0f, MaxSpread: Gs.Outlaw.MaxSpread,
        RecoveryTime: 0.15f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 3.5f,
        Pitch: Gs.Outlaw.Pitch, Yaw: new[] { 0f, 0f }, Spread: Gs.Outlaw.Spread, HeatCap: Gs.Outlaw.HeatCap,
        RunErr: Gs.Outlaw.RunErr, WalkErr: Gs.Outlaw.WalkErr, CrouchWalkErr: Gs.Outlaw.CrouchWalkErr, AirErr: Gs.Outlaw.AirErr, RunSpeed: 5.4f,
        DamageHead: new[] { 238f }, DamageBody: new[] { 140f }, DamageLegs: new[] { 119f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Sniper, Cost = 2400, Penetration = Penetration.High,
        Scope = true, UnscopeOnShot = false, AdsMoveMul = 0.8f, CrouchSpreadMul = Gs.Outlaw.CrouchSpreadMul, RunPitchMul = 1.5f /* EST */,
        WalkErrKeys = Gs.Outlaw.WalkErrKeys,
        ScopedSpread = new[] { 0f, 2.25f }, RecoveryDelay = 0.364f /* = 1 / 2.75 */, ReloadPartial = 2.3f,
    };

    /// <summary>Operator (verified against 13.09). No recoil; movement error (walk curve from the 15% deadzone: 10° → 15°,
    /// crouch-walk 7.5°, run and jump 15°) and crouch ×0.9: game files. The 5× second zoom and the 15% movement deadzone
    /// (patch 1.09; the files' walking curve starts there too).</summary>
    public static readonly WeaponDef Operator = new(
        WeaponKind.Operator, "Operator", Auto: false, Rps: 0.6f, AdsRpsMul: 1f, Mag: 5, Reserve: 10, Reload: 3.7f, Equip: 1.5f,
        FirstShotHip: Gs.Operator.FirstShotHip, FirstShotAds: 0f, MaxSpread: 5f,
        RecoveryTime: 1.6f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 2.5f,
        Pitch: new[] { 0f }, Yaw: new[] { 0f }, Spread: new[] { 5f }, HeatCap: 1,
        RunErr: Gs.Operator.RunErr, WalkErr: Gs.Operator.WalkErr, CrouchWalkErr: Gs.Operator.CrouchWalkErr, AirErr: Gs.Operator.AirErr, RunSpeed: 5.13f,
        DamageHead: new[] { 255f }, DamageBody: new[] { 150f }, DamageLegs: new[] { 120f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Sniper, Cost = 4700, Penetration = Penetration.High,
        Scope = true, UnscopeOnShot = true, AdsMoveMul = 0.72f, Zoom2 = 5f, CrouchSpreadMul = Gs.Operator.CrouchSpreadMul, DeadzoneMul = 0.15f,
        WalkErrKeys = Gs.Operator.WalkErrKeys,
    };

    // ================= machine guns =================

    /// <summary>Ares: reverse spread (1.0° → 0.7° by bullet 13), no spin-up since patch 4.0. Tables (13° climb over the
    /// magazine), recovery 0.65, tap efficiency 1, aimed error ×1.15 − 0.25 and yaw ×0.7, crouch ×0.6 spread and recoil,
    /// running ×1.4 pitch and yaw, movement error: game files.</summary>
    public static readonly WeaponDef Ares = new(
        WeaponKind.Ares, "Ares", Auto: true, Rps: 13f, AdsRpsMul: 1f, Mag: 50, Reserve: 100, Reload: 3.25f, Equip: 1.25f,
        FirstShotHip: Gs.Ares.FirstShotHip, FirstShotAds: Gs.Ares.FirstShotAds, MaxSpread: Gs.Ares.MaxSpread,
        RecoveryTime: Gs.Ares.RecoveryTime, TapEfficiency: Gs.Ares.TapEfficiency, ProtectedBullets: 8, YawSwitchTime: 0.5f, Zoom: 1.15f,
        Pitch: Gs.Ares.Pitch, Yaw: Gs.Ares.Yaw, Spread: Gs.Ares.Spread, HeatCap: Gs.Ares.HeatCap,
        RunErr: Gs.Ares.RunErr, WalkErr: Gs.Ares.WalkErr, CrouchWalkErr: Gs.Ares.CrouchWalkErr, AirErr: Gs.Ares.AirErr, RunSpeed: 5.13f,
        DamageHead: new[] { 75f, 70f }, DamageBody: new[] { 30f, 28f }, DamageLegs: new[] { 25.5f, 23.8f }, DamageRangeEnd: new[] { 30f, 999f })
    {
        Class = WeaponClass.Heavy, Cost = 1600, Penetration = Penetration.High,
        RunPitchMul = Gs.Ares.RunPitchMul, RunYawMul = Gs.Ares.RunYawMul,
        CrouchSpreadMul = Gs.Ares.CrouchSpreadMul, CrouchRecoilMul = Gs.Ares.CrouchRecoilMul,
        AdsErrorMul = Gs.Ares.AdsErrorMul, AdsErrorAdd = Gs.Ares.AdsErrorAdd, AdsYawMul = Gs.Ares.AdsYawMul,
        WalkErrKeys = Gs.Ares.WalkErrKeys,
    };

    /// <summary>Odin: hip fire starts at 12 rps and ramps to 15.6 while held (ramp time 1.0 s EST); aimed fire is 15.6 from
    /// the first bullet (AdsRpsMul 1.3 = 15.6 / 12, valorant-api). Tables (17° climb over 75 bullets, 0.8° → 1.3° error),
    /// recovery 0.65, tap efficiency 1, aimed ×1.15 − 0.13 error and ×0.7 recoil, crouch ×0.6, running ×1.4, movement error:
    /// game files.</summary>
    public static readonly WeaponDef Odin = new(
        WeaponKind.Odin, "Odin", Auto: true, Rps: 12f, AdsRpsMul: 1.3f, Mag: 100, Reserve: 200, Reload: 5f, Equip: 1.25f,
        FirstShotHip: Gs.Odin.FirstShotHip, FirstShotAds: Gs.Odin.FirstShotAds, MaxSpread: Gs.Odin.MaxSpread,
        RecoveryTime: Gs.Odin.RecoveryTime, TapEfficiency: Gs.Odin.TapEfficiency, ProtectedBullets: 8, YawSwitchTime: 0.5f, Zoom: 1.15f,
        Pitch: Gs.Odin.Pitch, Yaw: Gs.Odin.Yaw, Spread: Gs.Odin.Spread, HeatCap: Gs.Odin.HeatCap,
        RunErr: Gs.Odin.RunErr, WalkErr: Gs.Odin.WalkErr, CrouchWalkErr: Gs.Odin.CrouchWalkErr, AirErr: Gs.Odin.AirErr, RunSpeed: 5.13f,
        DamageHead: new[] { 95f, 77.5f }, DamageBody: new[] { 38f, 31f }, DamageLegs: new[] { 32.3f, 26.35f }, DamageRangeEnd: new[] { 30f, 999f })
    {
        Class = WeaponClass.Heavy, Cost = 3200, Penetration = Penetration.High,
        RunPitchMul = Gs.Odin.RunPitchMul, RunYawMul = Gs.Odin.RunYawMul,
        CrouchSpreadMul = Gs.Odin.CrouchSpreadMul, CrouchRecoilMul = Gs.Odin.CrouchRecoilMul,
        AdsErrorMul = Gs.Odin.AdsErrorMul, AdsErrorAdd = Gs.Odin.AdsErrorAdd, AdsPitchMul = Gs.Odin.AdsPitchMul, AdsYawMul = Gs.Odin.AdsYawMul,
        WalkErrKeys = Gs.Odin.WalkErrKeys,
        SpinUpTime = 1.0f /* EST */, SpinUpStartMul = 0.769f /* 12 / 15.6 */, AdsSkipsSpinUp = true,
    };

    // ================= Chamber =================

    /// <summary>
    /// Chamber's Headhunter (official wiki, game-file values; patch 13.0x): semi-auto heavy pistol, 4 rps, 8 rounds and no
    /// reserve, 0.3 s equip, no damage falloff (159 head / 55 body / 46 legs), first shot 0.25° (ADS ×1.5: 0°), max spread
    /// 3° (ADS 2.75°) with the spread growing after the 2nd spammed bullet (patch 5.12), movement error crouch 0.1° / walk
    /// 1.2° / run 3° / air 7°. Recoil climb, recovery and the per-bullet spread curve are estimates (Sheriff-like).
    /// </summary>
    public static readonly WeaponDef Headhunter = new(
        WeaponKind.Headhunter, "Headhunter", Auto: false, Rps: 4f, AdsRpsMul: 1f, Mag: 8, Reserve: 0, Reload: 2.25f, Equip: 0.3f,
        FirstShotHip: 0.25f, FirstShotAds: 0f, MaxSpread: 3f,
        RecoveryTime: 0.4f, TapEfficiency: 3, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1.5f,
        Pitch: new[] { 0f, 0.9f, 1.8f, 2.5f, 3.0f, 3.3f, 3.5f, 3.6f }, Yaw: new[] { 0f, 0.15f, 0.3f, 0.45f, 0.55f, 0.6f, 0.65f, 0.7f },
        Spread: new[] { 0.25f, 0.25f, 1.4f, 2.4f, 3f, 3f, 3f, 3f }, HeatCap: 7,
        RunErr: 3f, WalkErr: 1.2f, CrouchWalkErr: 0.1f, AirErr: 7f, RunSpeed: 5.4f,
        DamageHead: new[] { 159f }, DamageBody: new[] { 55f }, DamageLegs: new[] { 46f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Sidearm,
    };

    /// <summary>
    /// Chamber's Tour De Force (official wiki; fire rate per patch 8.03): semi-auto sniper, 0.9 rps scoped or not, 5 rounds
    /// and no reserve, 0.667 s re-equip, 150 body (one-shots 150 HP) ×1.7 head / ×0.85 legs, hip error 5° (crouched 4.5°),
    /// scoped 0°, scope 2.5× (first level), movement error crouch 7.5° / walk 10° / run 15° / air 15°. Not bolt action:
    /// the scope stays up between shots (assumption).
    /// </summary>
    public static readonly WeaponDef TourDeForce = new(
        WeaponKind.TourDeForce, "Tour De Force", Auto: false, Rps: 0.9f, AdsRpsMul: 1f, Mag: 5, Reserve: 0, Reload: 3.7f, Equip: 0.667f,
        FirstShotHip: 5f, FirstShotAds: 0f, MaxSpread: 5f,
        RecoveryTime: 1.1f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 2.5f,
        Pitch: new[] { 0f }, Yaw: new[] { 0f }, Spread: new[] { 5f }, HeatCap: 1,
        RunErr: 15f, WalkErr: 10f, CrouchWalkErr: 7.5f, AirErr: 15f, RunSpeed: 5.13f,
        DamageHead: new[] { 255f }, DamageBody: new[] { 150f }, DamageLegs: new[] { 127.5f }, DamageRangeEnd: new[] { 999f })
    {
        Class = WeaponClass.Sniper, Scope = true, CrouchSpreadMul = 0.9f /* crouched hip 4.5° */,
    };

    // ================= lists and lookup =================

    /// <summary>The 20 buyable guns in buy-menu order: sidearms, SMGs, shotguns, rifles, snipers, machine guns (each column by
    /// price).</summary>
    public static readonly IReadOnlyList<WeaponDef> Buyable = new[]
    {
        Classic, Shorty, Frenzy, Ghost, Bandit, Sheriff,
        Stinger, Spectre,
        Bucky, Judge,
        Bulldog, Guardian, Phantom, Vandal, Warden,
        Marshal, Outlaw, Operator,
        Ares, Odin,
    };

    /// <summary>Every gun: <see cref="Buyable"/> plus Chamber's two ability guns at the end.</summary>
    public static readonly IReadOnlyList<WeaponDef> All = Buyable.Concat(new[] { Headhunter, TourDeForce }).ToArray();

    static readonly Dictionary<WeaponKind, WeaponDef> byKind = All.ToDictionary(w => w.Kind);

    /// <summary>The gun's stats; null for <see cref="WeaponKind.None"/> or a number this build doesn't know (an older app
    /// reading a newer replay).</summary>
    public static WeaponDef? Get(WeaponKind k) => byKind.TryGetValue(k, out var w) ? w : null;

    /// <summary>A gun from its name ("Spectre", "spectre", "Tour De Force", "TourDeForce"); null for null, empty, unknown
    /// names ("Knife"), numbers and None. Never throws.</summary>
    public static WeaponKind? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = name.Trim().Replace(" ", "").Replace("-", "").Replace("_", "");
        if (s.Length == 0 || char.IsDigit(s[0]) || s[0] == '+' || s.Contains(',')) return null; // no numbers or flag lists
        return Enum.TryParse<WeaponKind>(s, ignoreCase: true, out var k) && k != WeaponKind.None && byKind.ContainsKey(k) ? k : null;
    }

    /// <summary>The base gun a kind plays like (see <see cref="WeaponDef.Family"/>).</summary>
    public static WeaponKind FamilyOf(WeaponKind k) => k switch
    {
        WeaponKind.Headhunter => WeaponKind.Sheriff,
        WeaponKind.TourDeForce => WeaponKind.Operator,
        _ => k,
    };
}

/// <summary>
/// The gun sets a drill can offer in its picker (<see cref="Modes.TrainingMode.WeaponChoice"/>), in buy-menu order.
/// </summary>
public static class WeaponSets
{
    static WeaponKind[] Where(Func<WeaponDef, bool> f) => Weapons.Buyable.Where(f).Select(w => w.Kind).ToArray();

    /// <summary>Every buyable gun.</summary>
    public static readonly IReadOnlyCollection<WeaponKind> Any = Where(_ => true);
    /// <summary>Every buyable gun except the pellet guns (Shorty, Bucky, Judge): pellets make target-drill scores meaningless.</summary>
    public static readonly IReadOnlyCollection<WeaponKind> NoShotguns = Where(w => w.Pellets <= 1);
    /// <summary>Fully automatic single-bullet guns: Frenzy, Stinger, Spectre, Bulldog, Phantom, Vandal, Warden, Ares, Odin.</summary>
    public static readonly IReadOnlyCollection<WeaponKind> Auto = Where(w => w.Auto && w.Pellets <= 1);
    /// <summary>Marshal, Outlaw, Operator.</summary>
    public static readonly IReadOnlyCollection<WeaponKind> Snipers = Where(w => w.Class == WeaponClass.Sniper);
}
