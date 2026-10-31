using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// How one gun looks and sounds: everything that is not behaviour (behaviour is <see cref="WeaponDef"/>).
/// </summary>
/// <param name="Base">The gun whose viewmodel model and pose this one uses (<see cref="Pose"/>). Every gun has its own since
/// 1.18, except Chamber's Tour De Force (the Operator's, in gold).</param>
/// <param name="ShotSound">Sfx key of a shot (one of <see cref="Audio.Sfx.GunKeys"/>; a missing clip falls back, see
/// <see cref="Audio.Sfx.Fallback"/>).</param>
/// <param name="ReloadSound">Sfx key of the reload (played at its start).</param>
/// <param name="ReloadPitch">Pitch of the reload sound (the Operator's is slowed to 0.75).</param>
/// <param name="BoltSound">Sfx key played <see cref="WeaponLooks.BoltDelay"/> after each shot (bolt action, or the Bucky's
/// pump), or null.</param>
/// <param name="BoltCycle">The viewmodel's trigger hand works the bolt after each shot and at the end of the reload.</param>
/// <param name="Pistol">Reloads with a handgun's tilt instead of a rifle's.</param>
/// <param name="Gold">Chamber's guns: gold finish on the model.</param>
/// <param name="Sight">Hologram sight outline while aimed (<see cref="AdsSight"/>); null = none (no ADS, or a scope).</param>
/// <param name="ScopeStyle">Which eyepiece a scoped gun draws (<see cref="CrosshairView.SniperScope"/>).</param>
public sealed record WeaponLook(
    WeaponKind Base, string ShotSound, string ReloadSound, float ReloadPitch, string? BoltSound,
    bool BoltCycle, bool Pistol, bool Gold, AdsSight.SightLook? Sight, CrosshairView.SniperScope ScopeStyle)
{
    /// <summary>Viewmodel layout of <see cref="Base"/>.</summary>
    public VmPose Pose => WeaponLooks.PoseOf(Base);
    /// <summary>Shot volume multiplier. The clips are loudness-matched offline (tools/sfx/gun_sfx.py: suppressed guns are
    /// quieter, snipers and shotguns louder), so this stays 1 unless a gun needs a nudge in game.</summary>
    public float Volume { get; init; } = 1f;
    /// <summary>No magazine: the reload pushes rounds into the loading port (revolver, shotguns, Marshal) instead of swapping
    /// a magazine (<see cref="VmPose.Loads"/>).</summary>
    public bool NoMag { get; init; }
    /// <summary>The support hand works a pump after each shot and after the reload (Bucky).</summary>
    public bool Pump { get; init; }
    /// <summary>Suppressor: small, dim muzzle flash (Ghost, Spectre).</summary>
    public bool Suppressed { get; init; }
    /// <summary>Sfx key of the barrel spin-up when the trigger starts a spin (Odin), and of the spin-down; null = none.</summary>
    public string? SpinUpSound { get; init; }
    public string? SpinDownSound { get; init; }
}

/// <summary>
/// One table of looks for every gun (research: weapons-design-1.18.md §1, §5, §7). It replaces the per-gun switches that
/// used to be spread over GameSession (sounds), AdsSession (scope style, sight), the Rewind, the viewmodel and VmPose.For.
/// <para>Models: one CC0 Quaternius Ultimate Gun Pack model per gun (assets/MANIFEST.md §5). Sounds: one recorded CC0 shot
/// per gun (assets/MANIFEST.md §6, tools/sfx/gun_sfx.py). Chamber's guns keep the gold finish.</para>
/// </summary>
public static class WeaponLooks
{
    /// <summary>Seconds after a shot that <see cref="WeaponLook.BoltSound"/> plays.</summary>
    public const float BoltDelay = 0.45f;

    static readonly WeaponLook Rifle = new(WeaponKind.Vandal, "vandal", "reload_rifle", 1f, null,
        BoltCycle: false, Pistol: false, Gold: false, Sight: AdsSight.Vandal, ScopeStyle: CrosshairView.SniperScope.Operator);
    static readonly WeaponLook Handgun = Rifle with { Base = WeaponKind.Classic, ShotSound = "classic", ReloadSound = "reload_pistol", Pistol = true, Sight = null };
    static readonly WeaponLook Sniper = Rifle with { Base = WeaponKind.Operator, ShotSound = "operator", ReloadPitch = 0.75f, BoltSound = "bolt", BoltCycle = true, Sight = null };
    static readonly WeaponLook Shotgun = Rifle with { ReloadSound = "reload_shotgun", Sight = null, NoMag = true };

    static readonly Dictionary<WeaponKind, WeaponLook> looks = new()
    {
        // ---- sidearms ----
        [WeaponKind.Classic] = Handgun,
        [WeaponKind.Shorty] = Shotgun with { Base = WeaponKind.Shorty, ShotSound = "shorty" },
        [WeaponKind.Frenzy] = Handgun with { Base = WeaponKind.Frenzy, ShotSound = "frenzy" },
        [WeaponKind.Ghost] = Handgun with { Base = WeaponKind.Ghost, ShotSound = "ghost", Suppressed = true },
        [WeaponKind.Bandit] = Handgun with { Base = WeaponKind.Bandit, ShotSound = "bandit" },
        [WeaponKind.Sheriff] = Handgun with { Base = WeaponKind.Sheriff, ShotSound = "sheriff", ReloadSound = "reload_revolver", NoMag = true },
        // ---- SMGs ----
        [WeaponKind.Stinger] = Rifle with { Base = WeaponKind.Stinger, ShotSound = "stinger", ReloadPitch = 1.1f, Sight = AdsSight.Smg },
        [WeaponKind.Spectre] = Rifle with { Base = WeaponKind.Spectre, ShotSound = "spectre", ReloadPitch = 1.06f, Sight = AdsSight.Smg, Suppressed = true },
        // ---- shotguns ----
        [WeaponKind.Bucky] = Shotgun with { Base = WeaponKind.Bucky, ShotSound = "bucky", BoltSound = "pump", Pump = true },
        [WeaponKind.Judge] = Shotgun with { Base = WeaponKind.Judge, ShotSound = "judge" },
        // ---- rifles ----
        [WeaponKind.Bulldog] = Rifle with { Base = WeaponKind.Bulldog, ShotSound = "bulldog" },
        [WeaponKind.Guardian] = Rifle with { Base = WeaponKind.Guardian, ShotSound = "guardian", Sight = AdsSight.Dmr },
        [WeaponKind.Phantom] = Rifle with { Base = WeaponKind.Phantom, ShotSound = "phantom", Sight = AdsSight.Phantom },
        [WeaponKind.Vandal] = Rifle,
        [WeaponKind.Warden] = Rifle with { Base = WeaponKind.Warden, ShotSound = "warden", Sight = AdsSight.Dmr },
        // ---- snipers ----
        [WeaponKind.Marshal] = Sniper with
        {
            Base = WeaponKind.Marshal, ShotSound = "marshal", ReloadPitch = 0.85f, BoltSound = "lever", NoMag = true,
            ScopeStyle = CrosshairView.SniperScope.Marshal,
        },
        [WeaponKind.Outlaw] = Sniper with
        {
            Base = WeaponKind.Outlaw, ShotSound = "outlaw", ReloadPitch = 0.85f, BoltSound = null, BoltCycle = false,
            ScopeStyle = CrosshairView.SniperScope.Outlaw,
        },
        [WeaponKind.Operator] = Sniper,
        // ---- machine guns ----
        [WeaponKind.Ares] = Rifle with { Base = WeaponKind.Ares, ShotSound = "ares", ReloadSound = "reload_lmg", Sight = AdsSight.Heavy },
        [WeaponKind.Odin] = Rifle with
        {
            Base = WeaponKind.Odin, ShotSound = "odin", ReloadSound = "reload_lmg", Sight = AdsSight.Heavy,
            SpinUpSound = "spinup", SpinDownSound = "spindown",
        },
        // ---- Chamber: gold. The Headhunter has its own long-slide pistol; the Tour De Force keeps the Operator model, at
        // normal reload speed and without a bolt, as before.
        [WeaponKind.Headhunter] = Handgun with { Base = WeaponKind.Headhunter, ShotSound = "headhunter", Gold = true, Sight = AdsSight.Headhunter },
        [WeaponKind.TourDeForce] = Sniper with { ReloadPitch = 1f, BoltSound = null, BoltCycle = false, Gold = true, ScopeStyle = CrosshairView.SniperScope.TourDeForce },
    };

    /// <summary>The look of <paramref name="k"/>; the Vandal's for None or a gun this build doesn't know.</summary>
    public static WeaponLook For(WeaponKind k) => looks.TryGetValue(k, out var l) ? l : Rifle;

    /// <summary>Whether <paramref name="k"/> has a sniper scope, and which eyepiece (the Rewind, AdsSession).</summary>
    public static (bool Scope, CrosshairView.SniperScope Style) ScopeOf(WeaponKind k) => (Weapons.Get(k)?.Scope == true, For(k).ScopeStyle);

    /// <summary>Viewmodel layout of a gun (the Vandal's for None or an unknown gun).</summary>
    public static VmPose PoseOf(WeaponKind k) => k switch
    {
        WeaponKind.Classic => VmPose.Classic,
        WeaponKind.Shorty => VmPose.Shorty,
        WeaponKind.Frenzy => VmPose.Frenzy,
        WeaponKind.Ghost => VmPose.Ghost,
        WeaponKind.Bandit => VmPose.Bandit,
        WeaponKind.Sheriff => VmPose.Sheriff,
        WeaponKind.Stinger => VmPose.Stinger,
        WeaponKind.Spectre => VmPose.Spectre,
        WeaponKind.Bucky => VmPose.Bucky,
        WeaponKind.Judge => VmPose.Judge,
        WeaponKind.Bulldog => VmPose.Bulldog,
        WeaponKind.Guardian => VmPose.Guardian,
        WeaponKind.Phantom => VmPose.Phantom,
        WeaponKind.Warden => VmPose.Warden,
        WeaponKind.Marshal => VmPose.Marshal,
        WeaponKind.Outlaw => VmPose.Outlaw,
        WeaponKind.Operator or WeaponKind.TourDeForce => VmPose.Operator,
        WeaponKind.Ares => VmPose.Ares,
        WeaponKind.Odin => VmPose.Odin,
        WeaponKind.Headhunter => VmPose.Headhunter,
        _ => VmPose.Vandal,
    };
}
