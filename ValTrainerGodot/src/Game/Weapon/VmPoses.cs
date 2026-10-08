using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Per-weapon viewmodel layout (right-handed; the left-handed layout is the mirror image).
/// Camera space: -Z forward, +X right, +Y up. Gun space: grip origin, barrel -Z.
/// </summary>
public sealed record VmPose(
    string Scene,
    Vector3 HipPos, Vector3 HipRot,          // gun grip in camera space; rotation (pitch, yaw, roll) deg
    Vector3 AdsPos, Vector3 AdsRot,
    HandPlacement Grip, HandPlacement Support,
    HandPlacement? Bolt,                     // Operator: trigger hand on the bolt handle
    Vector3 Eject,                           // ejection port (gun space)
    Vector3 MagWell, Vector3 MagHalf, float MagCurve, // magazine insertion point (gun space) and size
    float KickBack, float KickPitch, float KickFreq,  // recoil impulse (m, deg) and spring frequency (rad/s)
    float FlashScale, bool Shells, float ShellLen, float Equip)
{
    // ---------------- hand shapes ----------------

    /// <summary>Trigger hand around a pistol grip (index on the trigger, thumb over the left side).</summary>
    static readonly HandShape PistolGrip = new(
        Index: new Finger(22, 15, 55, 30), Middle: new Finger(0, 68, 85, 45), Ring: new Finger(-2, 70, 85, 45), Little: new Finger(-5, 72, 80, 40),
        ThumbDir: new Vector3(-0.18f, -0.75f, -0.63f), ThumbUp: new Vector3(-0.73f, -0.6f, 0.34f), ThumbCurl: new Vector3(5, 12, 8));

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

    // ---------------- weapons ----------------
    // ADS poses (VALORANT look, ads-model.md §4.3): the gun is raised low and centred and you look OVER it, never through
    // its iron sights. Solved from the models' sight points with the viewmodel projection (70° vertical FOV): rear sight top
    // ≈ 0.625 H, front post ≈ 0.61 H (Headhunter 0.66 / 0.645 H), so the gun's top sits about 0.12 H under the crosshair.
    // The muzzle tilts down a few degrees so the front post doesn't climb toward the crosshair. VmTest checks the result.

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
    };

    public static readonly VmPose Sheriff = new(
        "res://assets/weapons/Pistol_Compact.tscn",
        HipPos: new Vector3(0.125f, -0.15f, -0.42f), HipRot: new Vector3(-3f, -4f, -2f),
        AdsPos: new Vector3(0f, -0.1334f, -0.26f), AdsRot: new Vector3(-9.7f, 0f, 0f), // aimed only as the Headhunter
        Grip: GripAt(new Vector3(0f, -0.005f, 0.005f), 15f, PistolGrip, new Vector3(0.3f, -0.8f, 0.52f)),
        Support: PistolSupportAt(new Vector3(0f, -0.005f, 0.005f), 15f),
        Bolt: null,
        Eject: new Vector3(0.015f, 0.065f, -0.03f),
        MagWell: new Vector3(0f, -0.045f, 0.005f), MagHalf: new Vector3(0.011f, 0.05f, 0.016f), MagCurve: 0f,
        KickBack: 0.03f, KickPitch: 9f, KickFreq: 24f,
        FlashScale: 0.75f, Shells: false, ShellLen: 0.02f, Equip: 0.45f);

    internal static HandShape DebugShape(string name) => name switch
    {
        "grip" => PistolGrip,
        "cup" => Cup,
        "pinch" => Pinch,
        "psup" => PistolSupport,
        _ => new HandShape(new Finger(4, 0, 0, 0), new Finger(0, 0, 0, 0), new Finger(-4, 0, 0, 0), new Finger(-8, 0, 0, 0),
            new Vector3(-0.6f, -0.2f, -0.75f), new Vector3(-0.5f, 1f, 0f), Vector3.Zero, 0.15f),
    };

    public static VmPose For(WeaponKind k) => k switch
    {
        WeaponKind.Phantom => Phantom,
        WeaponKind.Operator => Operator,
        WeaponKind.Sheriff => Sheriff,
        _ => Vandal,
    };
}
