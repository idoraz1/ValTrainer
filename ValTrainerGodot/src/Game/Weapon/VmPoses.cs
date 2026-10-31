using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Per-weapon viewmodel layout (right-handed; the left-handed layout is the mirror image).
/// Camera space: -Z forward, +X right, +Y up. Gun space: grip origin, barrel -Z.
/// <para>Every anchor (grip, handguard / pump bottom, magazine well or loading port, ejection port, bolt handle) was measured
/// from the model's OBJ vertices in gun space (assets/MANIFEST.md §5), not guessed.</para>
/// </summary>
public sealed record VmPose(
    string Scene,
    Vector3 HipPos, Vector3 HipRot,          // gun grip in camera space; rotation (pitch, yaw, roll) deg
    Vector3 AdsPos, Vector3 AdsRot,
    HandPlacement Grip, HandPlacement Support,
    HandPlacement? Bolt,                     // bolt action (Operator, Marshal): trigger hand on the bolt handle
    Vector3 Eject,                           // ejection port (gun space)
    Vector3 MagWell, Vector3 MagHalf, float MagCurve, // magazine insertion point (gun space) and size; NoMag guns: the loading port
    float KickBack, float KickPitch, float KickFreq,  // recoil impulse (m, deg) and spring frequency (rad/s)
    float FlashScale, bool Shells, float ShellLen, float Equip)
{
    /// <summary>Rounds pushed in one by one during a reload without a magazine (<see cref="WeaponLook.NoMag"/>): one hand
    /// trip to the loading port each.</summary>
    public int Loads { get; init; } = 1;
    /// <summary>How far the support hand slides back to work the pump after a shot (m), for <see cref="WeaponLook.Pump"/>.</summary>
    public float PumpTravel { get; init; } = 0.075f;
    /// <summary>Debug side / top views (VmTest --vmview): camera distance (m) and the gun-space z the view centres on.</summary>
    public float ViewD { get; init; } = 0.66f;
    public float ViewZ { get; init; } = -0.17f;

    // ---------------- hand shapes ----------------

    /// <summary>Trigger hand around a pistol grip (index on the trigger, thumb over the left side).</summary>
    static readonly HandShape PistolGrip = new(
        Index: new Finger(22, 15, 55, 30), Middle: new Finger(0, 68, 85, 45), Ring: new Finger(-2, 70, 85, 45), Little: new Finger(-5, 72, 80, 40),
        ThumbDir: new Vector3(-0.18f, -0.75f, -0.63f), ThumbUp: new Vector3(-0.73f, -0.6f, 0.34f), ThumbCurl: new Vector3(5, 12, 8));

    /// <summary>Support hand wrapped around a vertical foregrip (index curled like the others: no trigger there).</summary>
    static readonly HandShape ForeGrip = PistolGrip with { Index = new Finger(8, 62, 80, 42) };

    /// <summary>Support hand cupping a handguard (fingers wrap up the far side, thumb along the near side).</summary>
    static readonly HandShape Cup = new(
        Index: new Finger(6, 45, 50, 30), Middle: new Finger(2, 50, 55, 30), Ring: new Finger(-2, 52, 55, 30), Little: new Finger(-6, 55, 55, 28),
        ThumbDir: new Vector3(-0.94f, -0.12f, -0.31f), ThumbUp: new Vector3(-0.28f, -0.48f, 0.86f), ThumbCurl: new Vector3(2, 8, 5));

    /// <summary>Support hand wrapping the trigger hand on a pistol (thumb forward along the frame).</summary>
    static readonly HandShape PistolSupport = new(
        Index: new Finger(2, 50, 60, 35), Middle: new Finger(0, 52, 62, 35), Ring: new Finger(-2, 55, 62, 35), Little: new Finger(-5, 58, 62, 35),
        ThumbDir: new Vector3(-0.49f, 0.1f, -0.86f), ThumbUp: new Vector3(-0.58f, 0.8f, 0.15f), ThumbCurl: new Vector3(2, 6, 4));

    /// <summary>Hand pinching the bolt handle.</summary>
    static readonly HandShape Pinch = new(
        Index: new Finger(6, 55, 70, 40), Middle: new Finger(2, 70, 85, 45), Ring: new Finger(-2, 75, 85, 45), Little: new Finger(-6, 80, 85, 45),
        ThumbDir: new Vector3(-0.4f, -0.4f, -0.8f), ThumbUp: new Vector3(-0.6f, 0.6f, 0f), ThumbCurl: new Vector3(10, 25, 15));

    // ---------------- placement helpers (gun space) ----------------

    /// <summary>Trigger hand on a pistol grip centred at <paramref name="c"/>, slanted back by <paramref name="slant"/> deg.</summary>
    static HandPlacement GripAt(Vector3 c, float slant, HandShape shape, Vector3? elbow = null)
    {
        float a = Mathf.DegToRad(slant);
        var fingers = new Vector3(0, -Mathf.Sin(a), -Mathf.Cos(a));
        var zh = -fingers;
        var xh = new Vector3(0, -Mathf.Cos(a), Mathf.Sin(a));
        var palm = c + new Vector3(0.03f, 0f, 0.01f);
        return new HandPlacement(palm + 0.05f * zh - 0.001f * xh, fingers, Vector3.Right, elbow ?? new Vector3(0.35f, -0.45f, 0.82f), shape);
    }

    /// <summary>Support hand around a vertical foregrip centred at <paramref name="c"/>: the trigger-hand grip mirrored to
    /// the left side, forearm down and back toward the left shoulder.</summary>
    static HandPlacement ForeGripAt(Vector3 c, float slant)
    {
        var g = GripAt(new Vector3(-c.X, c.Y, c.Z), slant, ForeGrip, new Vector3(-0.45f, -0.7f, 0.55f));
        static Vector3 M(Vector3 v) => new(-v.X, v.Y, v.Z);
        return new HandPlacement(M(g.Wrist), M(g.Fingers), M(g.Back), g.Elbow, ForeGrip);
    }

    /// <summary>Support hand under a handguard whose bottom centre is <paramref name="b"/>.</summary>
    static HandPlacement CupAt(Vector3 b) =>
        new(b + new Vector3(-0.056f, -0.036f, -0.009f), new Vector3(1f, 0.35f, -0.25f), new Vector3(-0.3f, -1f, 0f), new Vector3(-0.5f, -0.55f, 0.65f), Cup);

    /// <summary>Pistol support hand wrapping the grip hand from the left.</summary>
    static HandPlacement PistolSupportAt(Vector3 c, float slant)
    {
        float a = Mathf.DegToRad(slant);
        var fingers = new Vector3(0.15f, -Mathf.Sin(a) - 0.25f, -Mathf.Cos(a));
        var g = GripAt(c, slant, PistolGrip);
        var w = g.Wrist;
        return new HandPlacement(new Vector3(-w.X - 0.028f, w.Y - 0.02f, w.Z + 0.006f), fingers, new Vector3(-1f, -0.25f, 0.15f), new Vector3(-0.35f, -0.8f, 0.5f), PistolSupport);
    }

    /// <summary>Trigger hand pinching a bolt handle at <paramref name="h"/> (the Operator's offsets: wrist behind and right of it).</summary>
    static HandPlacement BoltAt(Vector3 h) =>
        new(h + new Vector3(0.05f, -0.015f, 0.13f), new Vector3(-0.5f, -0.1f, -0.86f), new Vector3(0.7f, 0.7f, 0f), new Vector3(0.4f, -0.4f, 0.8f), Pinch);

    // ---------------- weapons ----------------
    // ADS poses (VALORANT look, ads-model.md §4.3): the gun is raised low and centred and you look OVER it, never through
    // its iron sights. Solved from the models' sight points with the viewmodel projection (70° vertical FOV): rear sight top
    // ≈ 0.625 H, front post ≈ 0.61 H (Headhunter 0.66 / 0.645 H), so the gun's top sits about 0.12 H under the crosshair.
    // The muzzle tilts down a few degrees so the front post doesn't climb toward the crosshair. VmTest checks the result.
    // Scoped guns (Operator, Marshal, Outlaw) bring the eyepiece to the eye instead, then the scope overlay takes over.

    // ---- rifles (base: Vandal) ----

    public static readonly VmPose Vandal = new(
        "res://assets/weapons/AssaultRifle_AK.tscn",
        HipPos: new Vector3(0.225f, -0.19f, -0.36f), HipRot: new Vector3(-1.5f, -2.5f, -4f),
        AdsPos: new Vector3(0f, -0.2066f, -0.24f), AdsRot: new Vector3(-7.05f, 0f, 0f),
        Grip: GripAt(new Vector3(0f, 0.01f, 0f), 25f, PistolGrip),
        Support: CupAt(new Vector3(0f, 0.07f, -0.34f)),
        Bolt: null,
        Eject: new Vector3(0.018f, 0.105f, -0.1f),
        MagWell: new Vector3(0f, 0.066f, -0.158f), MagHalf: new Vector3(0.012f, 0.095f, 0.028f), MagCurve: 26f,
        KickBack: 0.022f, KickPitch: 2.6f, KickFreq: 30f,
        FlashScale: 1f, Shells: true, ShellLen: 0.039f, Equip: 0.55f);

    public static readonly VmPose Phantom = Vandal with
    {
        Scene = "res://assets/weapons/AssaultRifle_M4.tscn",
        AdsPos = new Vector3(0f, -0.206f, -0.24f), AdsRot = new Vector3(-10.8f, 0f, 0f),
        Grip = GripAt(new Vector3(0f, 0f, 0.01f), 30f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.077f, -0.3f)),
        Eject = new Vector3(0.02f, 0.11f, -0.07f),
        MagWell = new Vector3(0f, 0.045f, -0.148f), MagHalf = new Vector3(0.012f, 0.078f, 0.02f), MagCurve = 6f,
        KickBack = 0.018f, KickPitch = 2.1f, KickFreq = 32f, FlashScale = 0.85f, ShellLen = 0.045f,
    };

    /// <summary>Bulldog: compact M4 receiver with a big box magazine and a skeleton stock (accessory).</summary>
    public static readonly VmPose Bulldog = Vandal with
    {
        Scene = "res://assets/weapons/AssaultRifle_Carbine.tscn",
        AdsPos = new Vector3(0f, -0.2359f, -0.24f), AdsRot = new Vector3(-8f, 0f, 0f),
        Grip = GripAt(new Vector3(0f, 0f, 0.012f), 12f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.074f, -0.29f)),
        Eject = new Vector3(0.022f, 0.11f, -0.09f),
        MagWell = new Vector3(0f, 0.035f, -0.118f), MagHalf = new Vector3(0.015f, 0.05f, 0.033f), MagCurve = 0f,
        KickBack = 0.02f, KickPitch = 2.4f, KickFreq = 31f, FlashScale = 0.9f, ShellLen = 0.045f,
        ViewD = 0.66f, ViewZ = -0.14f,
    };

    /// <summary>Guardian: long AK-pattern rifle with a skeleton stock and wood handguard, a heavier kick per (semi-auto) shot.</summary>
    public static readonly VmPose Guardian = Vandal with
    {
        Scene = "res://assets/weapons/AssaultRifle_DMR.tscn",
        AdsPos = new Vector3(0f, -0.224f, -0.24f), AdsRot = new Vector3(-7f, 0f, 0f),
        KickBack = 0.028f, KickPitch = 3.4f, KickFreq = 26f, FlashScale = 1.1f,
        ViewD = 0.67f, ViewZ = -0.18f,
    };

    /// <summary>Warden: M4-pattern rifle with a short scope on the rail (aims like a rifle: the scope is only the look).</summary>
    public static readonly VmPose Warden = Vandal with
    {
        Scene = "res://assets/weapons/AssaultRifle_Scoped.tscn",
        AdsPos = new Vector3(0f, -0.2623f, -0.24f), AdsRot = new Vector3(-5f, 0f, 0f),
        Grip = GripAt(new Vector3(0f, 0.005f, 0f), 35f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.083f, -0.27f)),
        Eject = new Vector3(0.022f, 0.115f, -0.07f),
        MagWell = new Vector3(0f, 0.035f, -0.12f), MagHalf = new Vector3(0.012f, 0.08f, 0.026f), MagCurve = 15f,
        KickBack = 0.022f, KickPitch = 2.6f, KickFreq = 29f, FlashScale = 0.95f, ShellLen = 0.045f,
        ViewD = 0.6f, ViewZ = -0.15f,
    };

    // ---- SMGs ----

    /// <summary>Stinger: compact SMG, straight magazine, folding stock; the support hand under the receiver's front.</summary>
    public static readonly VmPose Stinger = Vandal with
    {
        Scene = "res://assets/weapons/SMG_Compact.tscn",
        HipPos = new Vector3(0.2f, -0.18f, -0.37f), HipRot = new Vector3(-1.5f, -3f, -4f),
        AdsPos = new Vector3(0f, -0.2102f, -0.24f), AdsRot = new Vector3(-6f, 0f, 0f),
        Grip = GripAt(new Vector3(0f, -0.01f, 0.01f), 20f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.058f, -0.235f)),
        Eject = new Vector3(0.024f, 0.11f, -0.1f),
        MagWell = new Vector3(0f, 0.005f, -0.122f), MagHalf = new Vector3(0.012f, 0.065f, 0.022f), MagCurve = 0f,
        KickBack = 0.014f, KickPitch = 1.6f, KickFreq = 34f, FlashScale = 0.7f, ShellLen = 0.025f, Equip = 0.45f,
        ViewD = 0.5f, ViewZ = -0.04f,
    };

    /// <summary>Spectre: the long-receiver SMG with a suppressor (small dim flash).</summary>
    public static readonly VmPose Spectre = Stinger with
    {
        Scene = "res://assets/weapons/SMG_Suppressed.tscn",
        AdsPos = new Vector3(0f, -0.2204f, -0.24f),
        Support = CupAt(new Vector3(0f, 0.05f, -0.3f)),
        FlashScale = 0.35f,
        ViewD = 0.7f, ViewZ = -0.21f,
    };

    // ---- shotguns ----

    /// <summary>Bucky: wood pump-action. The support hand works the pump after each shot (the spent shell flies out then) and
    /// pushes shells in under the receiver to reload.</summary>
    public static readonly VmPose Bucky = Vandal with
    {
        Scene = "res://assets/weapons/Shotgun_Pump.tscn",
        HipPos = new Vector3(0.215f, -0.175f, -0.38f), HipRot = new Vector3(-1.5f, -2.5f, -4f),
        Grip = GripAt(Vector3.Zero, 40f, PistolGrip),
        Support = CupAt(new Vector3(0f, -0.013f, -0.38f)),
        Eject = new Vector3(0.022f, 0.035f, -0.13f),
        MagWell = new Vector3(0f, -0.005f, -0.17f), MagHalf = new Vector3(0.012f, 0.02f, 0.02f), MagCurve = 0f,
        KickBack = 0.05f, KickPitch = 7f, KickFreq = 16f, FlashScale = 1.5f, Shells = false, ShellLen = 0.06f, Equip = 0.6f,
        Loads = 3, PumpTravel = 0.075f, ViewD = 0.72f, ViewZ = -0.27f,
    };

    /// <summary>Judge: pistol-grip combat shotgun (semi-auto: shells eject per shot; reloads through the side port).</summary>
    public static readonly VmPose Judge = Bucky with
    {
        Scene = "res://assets/weapons/Shotgun_Combat.tscn",
        HipPos = new Vector3(0.21f, -0.18f, -0.37f),
        Grip = GripAt(Vector3.Zero, 50f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.032f, -0.52f)),
        Eject = new Vector3(0.022f, 0.083f, -0.283f),
        MagWell = new Vector3(0f, 0.048f, -0.248f),
        KickBack = 0.04f, KickPitch = 6f, KickFreq = 18f, FlashScale = 1.4f, Shells = true,
        ViewD = 0.56f, ViewZ = -0.34f,
    };

    /// <summary>Shorty: sawn-off double barrel. Pistol grip in the trigger hand, the support hand under the barrels; two shells
    /// into the breech to reload, no shells ejected per shot.</summary>
    public static readonly VmPose Shorty = Bucky with
    {
        Scene = "res://assets/weapons/Shotgun_Sawnoff.tscn",
        HipPos = new Vector3(0.19f, -0.17f, -0.39f), HipRot = new Vector3(-2f, -3f, -4f),
        Grip = GripAt(Vector3.Zero, 50f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.04f, -0.31f)),
        Eject = new Vector3(0.02f, 0.075f, -0.15f),
        MagWell = new Vector3(0f, 0.075f, -0.15f),
        KickBack = 0.045f, KickPitch = 9f, KickFreq = 18f, FlashScale = 1.6f, Shells = false, Equip = 0.45f,
        Loads = 2, ViewD = 0.42f, ViewZ = -0.25f,
    };

    // ---- snipers (base: Operator) ----

    public static readonly VmPose Operator = Vandal with
    {
        Scene = "res://assets/weapons/SniperRifle_Bolt.tscn",
        HipPos = new Vector3(0.22f, -0.175f, -0.36f), HipRot = new Vector3(-1f, -2f, -4f),
        AdsPos = new Vector3(0f, -0.114f, -0.12f), AdsRot = new Vector3(0f, 0f, 0f), // scope to the eye (then the overlay)
        Grip = GripAt(new Vector3(0f, -0.015f, 0.015f), 15f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.01f, -0.33f)),
        Bolt = new HandPlacement(new Vector3(0.075f, 0.045f, 0.09f), new Vector3(-0.5f, -0.1f, -0.86f), new Vector3(0.7f, 0.7f, 0f), new Vector3(0.4f, -0.4f, 0.8f), Pinch),
        Eject = new Vector3(0.02f, 0.06f, -0.04f),
        MagWell = new Vector3(0f, 0.0f, -0.12f), MagHalf = new Vector3(0.014f, 0.035f, 0.03f), MagCurve = 0f,
        KickBack = 0.05f, KickPitch = 8f, KickFreq = 17f, FlashScale = 1.5f, Shells = false, ShellLen = 0.06f, Equip = 0.8f,
        ViewD = 0.85f, ViewZ = -0.25f,
    };

    /// <summary>Marshal: light wood-stock bolt action; bolt cycle after each shot, rounds loaded one at a time.</summary>
    public static readonly VmPose Marshal = Operator with
    {
        Scene = "res://assets/weapons/SniperRifle_Hunting.tscn",
        AdsPos = new Vector3(0f, -0.0984f, -0.094f), // eyepiece at the Operator's eye distance
        Grip = GripAt(Vector3.Zero, 35f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.017f, -0.37f)),
        Bolt = BoltAt(new Vector3(0.025f, 0.055f, -0.075f)),
        Eject = new Vector3(0.02f, 0.06f, -0.08f),
        MagWell = new Vector3(0f, 0.04f, -0.08f), MagHalf = new Vector3(0.012f, 0.025f, 0.025f),
        KickBack = 0.04f, KickPitch = 6.5f, KickFreq = 18f, FlashScale = 1.2f, Equip = 0.7f,
        Loads = 3, ViewD = 0.8f, ViewZ = -0.28f,
    };

    /// <summary>Outlaw: heavy grey-stock rifle with a box magazine and a muzzle brake; no bolt cycle (fast follow-up),
    /// a big kick and an ejected case per shot.</summary>
    public static readonly VmPose Outlaw = Operator with
    {
        Scene = "res://assets/weapons/SniperRifle_Heavy.tscn",
        AdsPos = new Vector3(0f, -0.0972f, -0.126f),
        Grip = GripAt(Vector3.Zero, 35f, PistolGrip),
        Support = CupAt(new Vector3(0f, 0.02f, -0.40f)),
        Bolt = null,
        Eject = new Vector3(0.02f, 0.06f, -0.06f),
        MagWell = new Vector3(0f, 0.04f, -0.26f), MagHalf = new Vector3(0.013f, 0.03f, 0.035f), MagCurve = 0f,
        KickBack = 0.055f, KickPitch = 8.5f, KickFreq = 16f, FlashScale = 1.5f, Shells = true, ShellLen = 0.065f,
        ViewD = 0.82f, ViewZ = -0.31f,
    };

    // ---- machine guns (bullpups: the grip sits mid-gun, so they are held further out) ----

    /// <summary>Ares: bullpup with a curved magazine behind the grip; small fast kick.</summary>
    public static readonly VmPose Ares = Vandal with
    {
        Scene = "res://assets/weapons/Bullpup_LMG.tscn",
        HipPos = new Vector3(0.24f, -0.235f, -0.56f), HipRot = new Vector3(-1f, -3f, -4f),
        AdsPos = new Vector3(0f, -0.2868f, -0.40f), AdsRot = new Vector3(-5f, 0f, 0f),
        Grip = GripAt(new Vector3(0f, 0f, 0.018f), 25f, PistolGrip, new Vector3(0.3f, -0.55f, 0.78f)),
        Support = CupAt(new Vector3(0f, 0.072f, -0.26f)),
        Eject = new Vector3(0.026f, 0.11f, 0.08f),
        MagWell = new Vector3(0f, 0.063f, 0.17f), MagHalf = new Vector3(0.014f, 0.07f, 0.03f), MagCurve = -25f,
        KickBack = 0.016f, KickPitch = 1.8f, KickFreq = 36f, FlashScale = 1.1f, ShellLen = 0.045f, Equip = 0.7f,
        ViewD = 0.6f, ViewZ = -0.09f,
    };

    /// <summary>Odin: the longest bullpup, support hand on its vertical foregrip, a folded bipod under the barrel.</summary>
    public static readonly VmPose Odin = Ares with
    {
        Scene = "res://assets/weapons/Bullpup_HMG.tscn",
        HipPos = new Vector3(0.24f, -0.235f, -0.62f),
        AdsPos = new Vector3(0f, -0.3037f, -0.45f),
        Support = ForeGripAt(new Vector3(0f, 0.045f, -0.342f), 5f),
        MagWell = new Vector3(0f, 0.065f, 0.165f), MagHalf = new Vector3(0.014f, 0.075f, 0.028f), MagCurve = -12f,
        KickBack = 0.018f, KickPitch = 2f, KickFreq = 38f, FlashScale = 1.2f,
        ViewD = 0.66f, ViewZ = -0.02f,
    };

    // ---- sidearms (base: Classic) ----

    /// <summary>Classic: the polymer pistol (before 1.18 this layout was the Sheriff's). Aimed only as Chamber's Headhunter
    /// used to be.</summary>
    public static readonly VmPose Classic = new(
        "res://assets/weapons/Pistol_Compact.tscn",
        HipPos: new Vector3(0.125f, -0.15f, -0.42f), HipRot: new Vector3(-3f, -4f, -2f),
        AdsPos: new Vector3(0f, -0.1334f, -0.26f), AdsRot: new Vector3(-9.7f, 0f, 0f),
        Grip: GripAt(new Vector3(0f, -0.005f, 0.005f), 15f, PistolGrip, new Vector3(0.3f, -0.8f, 0.52f)),
        Support: PistolSupportAt(new Vector3(0f, -0.005f, 0.005f), 15f),
        Bolt: null,
        Eject: new Vector3(0.015f, 0.065f, -0.03f),
        MagWell: new Vector3(0f, -0.045f, 0.005f), MagHalf: new Vector3(0.011f, 0.05f, 0.016f), MagCurve: 0f,
        KickBack: 0.03f, KickPitch: 9f, KickFreq: 24f,
        FlashScale: 0.75f, Shells: false, ShellLen: 0.02f, Equip: 0.45f)
    { ViewD = 0.26f, ViewZ = -0.03f };

    /// <summary>Ghost: black-slide pistol with a suppressor (the muzzle marker is at its tip; small dim flash).</summary>
    public static readonly VmPose Ghost = Classic with
    {
        Scene = "res://assets/weapons/Pistol_Suppressed.tscn",
        KickBack = 0.024f, KickPitch = 7f, FlashScale = 0.3f, Shells = true,
        ViewD = 0.32f, ViewZ = -0.13f,
    };

    /// <summary>Bandit: bigger-slide heavy pistol with a rail; a stronger kick than the Classic.</summary>
    public static readonly VmPose Bandit = Classic with
    {
        Scene = "res://assets/weapons/Pistol_Heavy.tscn",
        Eject = new Vector3(0.016f, 0.075f, -0.035f),
        MagWell = new Vector3(0f, -0.048f, 0.008f), MagHalf = new Vector3(0.012f, 0.05f, 0.017f),
        KickBack = 0.034f, KickPitch = 10.5f, KickFreq = 22f, FlashScale = 0.9f, Shells = true,
        ViewD = 0.27f, ViewZ = -0.065f,
    };

    /// <summary>Sheriff: heavy revolver. No magazine: the support hand loads the cylinder; no case ejected per shot.</summary>
    public static readonly VmPose Sheriff = Classic with
    {
        Scene = "res://assets/weapons/Revolver_Heavy.tscn",
        HipPos = new Vector3(0.125f, -0.148f, -0.42f),
        Grip = GripAt(new Vector3(0f, -0.01f, 0.015f), 30f, PistolGrip, new Vector3(0.3f, -0.8f, 0.52f)),
        Support = PistolSupportAt(new Vector3(0f, -0.01f, 0.015f), 30f),
        MagWell = new Vector3(-0.02f, 0.045f, -0.065f), MagHalf = new Vector3(0.012f, 0.02f, 0.02f),
        KickBack = 0.036f, KickPitch = 11f, KickFreq = 21f, FlashScale = 1f, Shells = false,
        Loads = 1, ViewD = 0.3f, ViewZ = -0.1f,
    };

    /// <summary>Headhunter (Chamber, gold): long-slide heavy pistol with a rail. It aims down sights (orange outline).</summary>
    public static readonly VmPose Headhunter = Classic with
    {
        Scene = "res://assets/weapons/Pistol_Long.tscn",
        AdsPos = new Vector3(0f, -0.127f, -0.26f),
        KickBack = 0.032f, KickPitch = 9.5f,
        ViewD = 0.3f, ViewZ = -0.09f,
    };

    /// <summary>Frenzy: machine pistol (no stock, curved magazine ahead of the trigger), held two-handed like a pistol.</summary>
    public static readonly VmPose Frenzy = Classic with
    {
        Scene = "res://assets/weapons/MachinePistol.tscn",
        HipPos = new Vector3(0.13f, -0.155f, -0.42f),
        Grip = GripAt(new Vector3(0f, -0.01f, 0.005f), 10f, PistolGrip, new Vector3(0.3f, -0.8f, 0.52f)),
        Support = PistolSupportAt(new Vector3(0f, -0.01f, 0.005f), 10f),
        Eject = new Vector3(0.02f, 0.085f, -0.05f),
        MagWell = new Vector3(0f, 0.003f, -0.1f), MagHalf = new Vector3(0.011f, 0.055f, 0.016f), MagCurve = 12f,
        KickBack = 0.018f, KickPitch = 4.5f, KickFreq = 30f, FlashScale = 0.7f, Shells = true,
        ViewD = 0.36f, ViewZ = -0.12f,
    };

    internal static HandShape DebugShape(string name) => name switch
    {
        "grip" => PistolGrip,
        "cup" => Cup,
        "pinch" => Pinch,
        "psup" => PistolSupport,
        "fore" => ForeGrip,
        _ => new HandShape(new Finger(4, 0, 0, 0), new Finger(0, 0, 0, 0), new Finger(-4, 0, 0, 0), new Finger(-8, 0, 0, 0),
            new Vector3(-0.6f, -0.2f, -0.75f), new Vector3(-0.5f, 1f, 0f), Vector3.Zero, 0.15f),
    };

    /// <summary>The layout a gun is drawn with (<see cref="WeaponLooks"/>).</summary>
    public static VmPose For(WeaponKind k) => WeaponLooks.For(k).Pose;
}
