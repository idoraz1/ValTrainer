using ValTrainer.Modes;

namespace ValTrainer.Core;

/// <summary>
/// The gun picker's memory (research: weapons-design-1.18.md §3.2): a pick per drill and an optional preferred gun for every
/// drill, both in <see cref="AppSettings"/> (<see cref="AppSettings.WeaponPicks"/>, <see cref="AppSettings.PreferredWeapon"/>).
/// Saving follows the settings file's rules: --dev runs never write it (<see cref="AppSettings.ReadOnly"/>).
/// <para>Resolution order for a drill that offers a choice: its own pick → the preferred gun → its default
/// (<see cref="TrainingMode.Weapon"/>). A stored gun the drill doesn't allow, or a name this build doesn't know, is skipped
/// (never an error), so a pick made in a newer version or for another drill falls through.</para>
/// </summary>
public static class WeaponPicks
{
    static AppSettings? Current => Main.I?.Settings;

    /// <summary>The gun <paramref name="mode"/> should play, or null for its default (also for locked drills). The session
    /// calls this at every Restart unless it was given an explicit pick.</summary>
    public static WeaponKind? Resolve(TrainingMode mode, AppSettings? settings = null)
    {
        var choice = mode.WeaponChoice;
        var s = settings ?? Current;
        if (choice == null || choice.Count == 0 || s == null) return null;
        if (PickFor(mode.Key, s) is { } own && choice.Contains(own)) return own;
        if (Weapons.Parse(s.PreferredWeapon) is { } pref && choice.Contains(pref)) return pref;
        return null;
    }

    /// <summary>The gun stored for <paramref name="modeKey"/> (whether or not that drill allows it); null = none.</summary>
    public static WeaponKind? PickFor(string modeKey, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        return s?.WeaponPicks != null && s.WeaponPicks.TryGetValue(modeKey, out var name) ? Weapons.Parse(name) : null;
    }

    /// <summary>The preferred gun for every drill; null = each drill's default.</summary>
    public static WeaponKind? Preferred(AppSettings? settings = null) => Weapons.Parse((settings ?? Current)?.PreferredWeapon);

    /// <summary>Stores the pick for one drill and saves the settings. Null forgets it (the drill then uses the preferred gun,
    /// else its default).</summary>
    public static void Remember(string modeKey, WeaponKind? gun, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        if (s == null || string.IsNullOrEmpty(modeKey)) return;
        if (gun is { } g && g != WeaponKind.None)
        {
            s.WeaponPicks ??= new();
            s.WeaponPicks[modeKey] = g.ToString();
        }
        else if (s.WeaponPicks != null)
        {
            s.WeaponPicks.Remove(modeKey);
            if (s.WeaponPicks.Count == 0) s.WeaponPicks = null;
        }
        s.Save();
    }

    /// <summary>Sets the gun every drill that offers it plays (null = each drill's default) and saves the settings.</summary>
    public static void SetPreferred(WeaponKind? gun, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        if (s == null) return;
        s.PreferredWeapon = gun is { } g && g != WeaponKind.None ? g.ToString() : null;
        s.Save();
    }
}
