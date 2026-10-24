namespace ValTrainer.Valorant;

/// <summary>
/// ValTrainer's own keybind overrides (Settings → Keybinds), applied on top of the binds imported from VALORANT.
/// Stored ONLY in ValTrainer's settings.json (<see cref="Core.AppSettings.CustomBinds"/>) as
/// <c>{ "AltFire.primary": "mouse:Xbutton1", "Ability1.secondary": "key:V", "Crouch.secondary": "none" }</c>
/// (<see cref="InputBinding.Serialize"/>). VALORANT's files are never written: an override changes ValTrainer only.
/// A slot without an entry follows VALORANT; "none" = unbound in ValTrainer even if VALORANT binds it.
/// </summary>
public static class CustomBinds
{
    static readonly string[] SlotNames = { "primary", "secondary" };

    /// <summary>settings.json key of one slot: "AltFire.primary".</summary>
    public static string KeyOf(GameAction a, int slot) => $"{a}.{SlotNames[Math.Clamp(slot, 0, Keybinds.Slots - 1)]}";

    /// <summary>"AltFire.primary" → (AltFire, 0); null for anything else (renamed actions, hand edits).</summary>
    public static (GameAction A, int Slot)? ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        int dot = key.LastIndexOf('.');
        if (dot <= 0) return null;
        int slot = Array.FindIndex(SlotNames, s => s.Equals(key[(dot + 1)..], StringComparison.OrdinalIgnoreCase));
        if (slot < 0) return null;
        string name = key[..dot];
        if (name.Length == 0 || char.IsDigit(name[0]) || name[0] == '-') return null;
        return Enum.TryParse<GameAction>(name, true, out var a) && Enum.IsDefined(a) ? (a, slot) : null;
    }

    /// <summary>The override of a slot (null = follows VALORANT; <see cref="InputBinding.None"/> = unbound on purpose).</summary>
    public static InputBinding? Get(IReadOnlyDictionary<string, string>? custom, GameAction a, int slot) =>
        custom != null && custom.TryGetValue(KeyOf(a, slot), out var v) ? InputBinding.Parse(v) : null;

    public static bool IsCustom(IReadOnlyDictionary<string, string>? custom, GameAction a, int slot) => Get(custom, a, slot) != null;

    /// <summary>Sets a slot's override; a bind equal to VALORANT's (<paramref name="imported"/>) removes it instead, so
    /// "custom" always means "different from VALORANT".</summary>
    public static void Set(ref Dictionary<string, string>? custom, Keybinds imported, GameAction a, int slot, InputBinding b)
    {
        if (imported.Get(a, slot) == b) { Clear(ref custom, a, slot); return; }
        custom ??= new Dictionary<string, string>();
        custom[KeyOf(a, slot)] = b.Serialize();
    }

    /// <summary>Back to VALORANT's bind for that slot.</summary>
    public static void Clear(ref Dictionary<string, string>? custom, GameAction a, int slot)
    {
        if (custom == null) return;
        custom.Remove(KeyOf(a, slot));
        if (custom.Count == 0) custom = null;
    }

    /// <summary>Number of slots that differ from VALORANT (valid entries only).</summary>
    public static int Count(IReadOnlyDictionary<string, string>? custom) =>
        custom?.Count(kv => ParseKey(kv.Key) != null && InputBinding.Parse(kv.Value) != null) ?? 0;

    /// <summary>
    /// <paramref name="target"/> = <paramref name="imported"/> with every valid override applied. Unknown actions / bind
    /// names (a hand-edited or newer settings file) are ignored and keep VALORANT's bind. Returns how many were applied.
    /// </summary>
    public static int Apply(Keybinds imported, IReadOnlyDictionary<string, string>? custom, Keybinds target)
    {
        target.CopyFrom(imported);
        if (custom == null) return 0;
        int n = 0;
        foreach (var (key, value) in custom)
        {
            if (ParseKey(key) is not { } slot || InputBinding.Parse(value) is not { } b) continue;
            target.Set(slot.A, slot.Slot, b);
            n++;
        }
        return n;
    }
}
