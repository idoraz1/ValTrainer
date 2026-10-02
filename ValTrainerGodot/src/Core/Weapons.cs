namespace ValTrainer.Core;

public enum WeaponKind { None, Vandal, Phantom, Sheriff, Operator }

/// <summary>
/// Weapon stats from the official wiki / valorant-api / patch notes (to 13.06). The per-bullet recoil and
/// spread tables are RECONSTRUCTED from Riot's documented recoil rules (deterministic first bullets, vertical
/// climb that plateaus, side-locked horizontal drift with 10% swap chance after the protected bullets) —
/// Riot never published per-bullet numbers. All angles in degrees, cumulative from the original aim point.
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

    /// <summary>Damage for a hit zone at a distance (meters).</summary>
    public float Damage(int zone, float dist)
    {
        int i = 0;
        while (i < DamageRangeEnd.Length - 1 && dist > DamageRangeEnd[i]) i++;
        return zone switch { 0 => DamageHead[i], 1 => DamageBody[i], _ => DamageLegs[i] };
    }

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

public static class Weapons
{
    static float[] Pad(float[] v, int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = v[Math.Min(i, v.Length - 1)]; return r; }

    public static readonly WeaponDef Vandal = new(
        WeaponKind.Vandal, "Vandal", Auto: true, Rps: 9.75f, AdsRpsMul: 0.9f, Mag: 25, Reserve: 50, Reload: 2.5f, Equip: 1.0f,
        FirstShotHip: 0.25f, FirstShotAds: 0.157f, MaxSpread: 1.0f,
        RecoveryTime: 0.375f, TapEfficiency: 6, ProtectedBullets: 6, YawSwitchTime: 0.6f, Zoom: 1.25f,
        Pitch: Pad(new[] { 0f, 0.2f, 0.55f, 1.05f, 1.65f, 2.27f, 2.87f, 3.42f, 3.87f, 4.2f, 4.4f, 4.5f }, 25),
        Yaw: Pad(new[] { 0f, 0.02f, 0.05f, 0.08f, 0.1f, 0.12f, 0.25f, 0.45f, 0.7f, 0.95f, 1.2f, 1.45f, 1.7f, 1.9f, 2.1f, 2.25f, 2.35f, 2.4f }, 25),
        Spread: Pad(new[] { 0.25f, 0.3f, 0.4f, 0.52f, 0.65f, 0.78f, 0.9f, 1.0f }, 25), HeatCap: 12,
        RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
        DamageHead: new[] { 160f }, DamageBody: new[] { 40f }, DamageLegs: new[] { 34f }, DamageRangeEnd: new[] { 999f });

    public static readonly WeaponDef Phantom = new(
        WeaponKind.Phantom, "Phantom", Auto: true, Rps: 11f, AdsRpsMul: 0.9f, Mag: 30, Reserve: 60, Reload: 2.5f, Equip: 1.0f,
        FirstShotHip: 0.2f, FirstShotAds: 0.11f, MaxSpread: 0.9f,
        RecoveryTime: 0.35f, TapEfficiency: 4, ProtectedBullets: 8, YawSwitchTime: 0.6f, Zoom: 1.25f,
        Pitch: Pad(new[] { 0f, 0.15f, 0.4f, 0.75f, 1.15f, 1.57f, 1.99f, 2.39f, 2.75f, 3.05f, 3.29f, 3.46f, 3.56f, 3.6f }, 30),
        Yaw: Pad(new[] { 0f, 0.01f, 0.03f, 0.05f, 0.07f, 0.09f, 0.11f, 0.13f, 0.25f, 0.42f, 0.6f, 0.8f, 1.0f, 1.2f, 1.38f, 1.55f, 1.7f, 1.82f, 1.92f, 2.0f }, 30),
        Spread: Pad(new[] { 0.2f, 0.23f, 0.3f, 0.38f, 0.47f, 0.56f, 0.65f, 0.73f, 0.8f, 0.86f, 0.9f }, 30), HeatCap: 14,
        RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
        DamageHead: new[] { 156f, 140f }, DamageBody: new[] { 39f, 35f }, DamageLegs: new[] { 33f, 29.75f }, DamageRangeEnd: new[] { 20f, 999f });

    public static readonly WeaponDef Sheriff = new(
        WeaponKind.Sheriff, "Sheriff", Auto: false, Rps: 4f, AdsRpsMul: 1f, Mag: 6, Reserve: 24, Reload: 2.25f, Equip: 1.0f,
        FirstShotHip: 0.25f, FirstShotAds: 0.25f, MaxSpread: 2.75f,
        RecoveryTime: 0.4f, TapEfficiency: 3, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
        Pitch: new[] { 0f, 1.2f, 2.2f, 2.9f, 3.4f, 3.7f }, Yaw: new[] { 0f, 0.2f, 0.4f, 0.6f, 0.7f, 0.8f },
        Spread: new[] { 0.25f, 1.0f, 2.0f, 2.75f, 2.75f, 2.75f }, HeatCap: 5,
        RunErr: 3f, WalkErr: 1.2f, CrouchWalkErr: 0.5f, AirErr: 7f, RunSpeed: 5.4f,
        DamageHead: new[] { 159.5f, 145f }, DamageBody: new[] { 55f, 50f }, DamageLegs: new[] { 46.75f, 42.5f }, DamageRangeEnd: new[] { 30f, 999f });

    public static readonly WeaponDef Operator = new(
        WeaponKind.Operator, "Operator", Auto: false, Rps: 0.6f, AdsRpsMul: 1f, Mag: 5, Reserve: 10, Reload: 3.7f, Equip: 1.5f,
        FirstShotHip: 5f, FirstShotAds: 0f, MaxSpread: 5f,
        RecoveryTime: 1.6f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 2.5f,
        Pitch: new[] { 0f }, Yaw: new[] { 0f }, Spread: new[] { 5f }, HeatCap: 1,
        RunErr: 10f, WalkErr: 6f, CrouchWalkErr: 3f, AirErr: 15f, RunSpeed: 5.13f,
        DamageHead: new[] { 255f }, DamageBody: new[] { 150f }, DamageLegs: new[] { 120f }, DamageRangeEnd: new[] { 999f });

    public static WeaponDef? Get(WeaponKind k) => k switch
    {
        WeaponKind.Vandal => Vandal, WeaponKind.Phantom => Phantom, WeaponKind.Sheriff => Sheriff,
        WeaponKind.Operator => Operator, _ => null,
    };
}
