using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;
using ValTrainer.UI;

namespace ValTrainer.Game;

/// <summary>
/// Aim down sights and scopes, VALORANT style (research: ads-model.md). Alt fire raises the gun over AdsInTime: the gun
/// sits low and centred and a hologram sight outline (<see cref="AdsSight"/>) fades in around the crosshair near the end
/// of the raise; snipers raise the gun first, then the scope overlay fades in and replaces it. The picture zooms with the
/// raise (<see cref="PlayerView.VisualZoom"/>), while the sensitivity and the telemetry's ADS flag switch on the press
/// exactly as before (<see cref="PlayerView.Zoom"/>), so the coach maths (Analysis/Prep.cs) never change.
/// </summary>
public partial class GameSession
{
    /// <summary>Raise (hip → aimed) and lower times in seconds. VALO U MECH 112 at 60 fps: ≈7 frames in; Riot publishes
    /// no number.</summary>
    public const float AdsInTime = 0.12f, AdsOutTime = 0.10f;
    /// <summary>Eased raise at which a sniper's scope overlay replaces the raised gun (it fades in over the rest).</summary>
    const float ScopeShowAt = 0.6f;
    /// <summary>Eased raise at which the hologram sight starts to fade in and the HUD shows the ADS crosshair.</summary>
    const float SightShowAt = 0.55f;

    public bool? ForcedScope { get; set; }
    AdsSight adsSight = null!;

    /// <summary>The raise eased like the viewmodel's (smoothstep of <see cref="AdsBlend"/>).</summary>
    public float AdsEase => AdsBlend * AdsBlend * (3f - 2f * AdsBlend);

    /// <summary>Dev-only "--force-ads [blend]": every drill starts aimed / scoped (ForcedScope = true unless the drill sets
    /// its own); an optional 0..1 number freezes the raise there (mid-transition screenshots).</summary>
    static readonly bool DevForceAds = CmdLine.Dev && CmdLine.Has("--force-ads");
    static readonly float? DevAdsFreeze = float.TryParse(CmdLine.DevAfter("--force-ads"), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var fz) ? Mathf.Clamp(fz, 0f, 1f) : null;

    /// <summary>VALORANT's multiplier for this gun's zoomed look: scoped (snipers) or ADS (everything else).</summary>
    static float ZoomSensMultFor(WeaponDef? w) => w?.Sniper == true ? Main.I.Valorant.ZoomedSensMult : Main.I.Valorant.AdsSensMult;

    /// <summary>Puts <paramref name="def"/> in hand: a fresh gun and viewmodel (none for null / 2D drills).</summary>
    void Equip(WeaponDef? def)
    {
        if (viewmodel != null && IsInstanceValid(viewmodel)) viewmodel.QueueFree();
        viewmodel = null;
        weaponDef = def;
        Gun = def != null ? new WeaponController(def) : null;
        Mover.AdsSpeedMul = def?.AdsMoveMul ?? Mover.AdsMultiplier;
        if (def != null && !Mode.Is2D && Main.I.Settings.ViewModel)
        {
            viewmodel = Viewmodel.Create(def.Kind, Main.I.Settings.LeftHandedWeapon);
            cam.AddChild(viewmodel);
            Gun!.Fired += () => viewmodel?.OnFire();
        }
    }

    public void SwitchWeapon(WeaponKind kind)
    {
        var def = Weapons.Get(kind);
        if (def == weaponDef || Mode.Is2D || cam == null || !IsInstanceValid(cam)) return;
        Equip(def);
        if (Gun != null) Gun.EquipLeft = def!.Equip;
        adsHeld = false;
        AdsBlend = 0;
        View.Zoom = View.VisualZoom = 1;
        View.RecoilPitch = View.RecoilYaw = 0;
        Telemetry.Weapon = kind.ToString();
        Telemetry.ZoomSensMult = ZoomSensMultFor(def);
    }

    /// <summary>Restart: the dev --force-ads switch (after the drill's Setup, which may set its own ForcedScope).</summary>
    void DevAdsArgs()
    {
        if (DevForceAds && ForcedScope == null) ForcedScope = true;
    }

    /// <summary>Running frame: aim state, raise progress, sensitivity zoom and picture zoom.</summary>
    void UpdateAds(float dt)
    {
        var w = weaponDef!;
        Gun!.Scoped = (ForcedScope ?? adsHeld) && !abilityInHand && !Gun.Reloading && w.Zoom > 1f && (!w.Sniper || Gun.EquipLeft <= 0);
        Mover.Ads = Gun.Scoped;
        float target = Gun.Scoped ? 1f : 0f;
        AdsBlend = Mathf.MoveToward(AdsBlend, target, dt / (target > AdsBlend ? AdsInTime : AdsOutTime));
        if (DevAdsFreeze is { } freeze && Gun.Scoped) AdsBlend = Mathf.Min(AdsBlend, freeze);
        View.Zoom = Gun.Scoped ? w.Zoom : 1f;                   // sensitivity + telemetry: switches on the press
        View.VisualZoom = 1f + (w.Zoom - 1f) * AdsEase;         // the picture: eases with the raise
    }

    /// <summary>Every frame: which crosshair / scope / sight shows, and the viewmodel's aim pose.</summary>
    void UpdateAdsVisuals()
    {
        float e = AdsEase;
        bool scoped = Gun?.Scoped == true, sniper = weaponDef?.Sniper == true;
        bool hidden = State == St.Results || Mode.Is2D;
        bool scopeUp = scoped && sniper && e >= ScopeShowAt;
        crosshair.Showing = hidden ? CrosshairView.Mode.Hidden
            : scopeUp ? CrosshairView.Mode.Sniper
            : scoped && !sniper && e >= SightShowAt ? CrosshairView.Mode.Ads : CrosshairView.Mode.Primary;
        crosshair.ScopeBlend = scopeUp ? Mathf.SmoothStep(ScopeShowAt, 1f, e) : 1f;
        // ValTrainer's snipers today: Operator and Tour de Force. A Marshal / Outlaw (once added to WeaponKind) maps to
        // CrosshairView.SniperScope.Marshal / .Outlaw here.
        crosshair.ScopeStyle = weaponDef?.Kind switch
        {
            WeaponKind.TourDeForce => CrosshairView.SniperScope.TourDeForce,
            _ => CrosshairView.SniperScope.Operator,
        };

        adsSight.Look = hidden || weaponDef == null || sniper || abilityInHand ? null : AdsSight.LookFor(weaponDef.Kind);
        adsSight.Blend = e;
        adsSight.Offset = viewmodel != null && viewmodel.Visible ? viewmodel.SightScreenOffset() : Vector2.Zero;

        if (viewmodel != null)
        {
            viewmodel.Visible = !scopeUp && State != St.Results && !abilityInHand;
            viewmodel.SetAds(AdsBlend);
        }
    }
}
