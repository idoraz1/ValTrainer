using ValTrainer.Core;

namespace ValTrainer.Modes;

/// <summary>
/// Which guns a drill rates (design weapons-design-1.18.md §3.4) from its key alone, without building the mode: the coach
/// builds its profile on a worker thread from stored runs, which only carry the mode key and the gun's name. Same rule as
/// <see cref="TrainingMode.RatedWeapons"/>; the coach self-test checks that the two agree for every drill.
/// </summary>
public static class WeaponRating
{
    static readonly WeaponKind[] Rifles = { WeaponKind.Vandal, WeaponKind.Phantom };

    /// <summary>The rated guns of a drill whose own gun is <paramref name="drillGun"/>: the Vandal and the Phantom for
    /// either of them, else that gun alone.</summary>
    public static IReadOnlyCollection<WeaponKind> RatedFor(WeaponKind drillGun) =>
        drillGun is WeaponKind.Vandal or WeaponKind.Phantom ? Rifles : new[] { drillGun };

    /// <summary>A drill's own gun (<see cref="TrainingMode.Weapon"/>) from its key, agent argument included
    /// ("chamber:tdf", "mobility:jett-op").</summary>
    public static WeaponKind DefaultGun(string? modeKey)
    {
        var key = (modeKey ?? "").Trim().ToLowerInvariant();
        int c = key.IndexOf(':');
        string b = c > 0 ? key[..c] : key, arg = c > 0 ? key[(c + 1)..] : "";
        return b switch
        {
            "operator" => WeaponKind.Operator,
            "sheriff" => WeaponKind.Sheriff,
            "spray_phantom" => WeaponKind.Phantom,
            "tracking" or "reaction" or "sensfinder" => WeaponKind.None,
            "chamber" => arg == "tdf" ? WeaponKind.TourDeForce : WeaponKind.Headhunter,
            "mobility" => arg == "jett-op" ? WeaponKind.Operator : WeaponKind.Vandal,
            _ => WeaponKind.Vandal,
        };
    }

    /// <summary>A stored run counts for the coach's rank: no gun recorded (runs from before 1.18 = the drill's gun), a
    /// name this build doesn't know, or a rated gun of the drill.</summary>
    public static bool IsRatedRun(string? modeKey, string? weapon)
    {
        if (Weapons.Parse(weapon) is not { } gun) return true;
        var own = DefaultGun(modeKey);
        return own == WeaponKind.None || gun == own || RatedFor(own).Contains(gun);
    }
}
