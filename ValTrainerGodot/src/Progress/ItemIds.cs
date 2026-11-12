namespace ValTrainer.Progression;

/// <summary>
/// The unlock item id scheme shared by Progress and the skins (reconciles skins2-design §3.2 and progression-design §9):
/// <list type="bullet">
/// <item><c>line</c>: the line's level 1 (base), e.g. <c>carbon</c>, <c>foundry</c>.</item>
/// <item><c>line/n</c>: level n ≥ 2, e.g. <c>foundry/3</c> (levels unlock in order).</item>
/// <item><c>line/variant</c>: a variant, e.g. <c>foundry/blueflame</c> (variant ids always start with a letter).</item>
/// <item><c>factory</c>: always unlocked.</item>
/// </list>
/// Ids are lowercase ASCII, stable forever (progress.json stores them in <see cref="ProgressFile.Unlocked"/>).
/// The skins' stored equip string (<see cref="Game.Skins.SkinChoice"/>) is a superset: <c>line[/variant][@level]</c>.
/// </summary>
public static class ItemIds
{
    public const string Factory = "factory";

    /// <summary>The base item (level 1) of <paramref name="line"/>.</summary>
    public static string Base(string line) => line;
    /// <summary>Level <paramref name="n"/> of <paramref name="line"/> (1 = the base id).</summary>
    public static string Level(string line, int n) => n <= 1 ? line : $"{line}/{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    /// <summary>Variant <paramref name="variant"/> of <paramref name="line"/>.</summary>
    public static string Variant(string line, string variant) => $"{line}/{variant}";

    /// <summary>Splits an item id. <paramref name="level"/> is 1 for the base and for variants; <paramref name="variant"/> is
    /// null unless it is a variant id. False for null / empty / malformed ids (factory parses as line "factory").</summary>
    public static bool TryParse(string? id, out string line, out int level, out string? variant)
    {
        line = ""; level = 1; variant = null;
        if (string.IsNullOrWhiteSpace(id)) return false;
        id = id.Trim().ToLowerInvariant();
        int slash = id.IndexOf('/');
        if (slash < 0) { line = id; return IsToken(line); }
        line = id[..slash];
        string rest = id[(slash + 1)..];
        if (!IsToken(line) || !IsToken(rest)) return false;
        if (char.IsAsciiDigit(rest[0]))
        {
            if (!int.TryParse(rest, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out level) || level < 1) return false;
        }
        else variant = rest;
        return true;
    }

    /// <summary>Lowercase ASCII letters and digits only (the id alphabet).</summary>
    public static bool IsToken(string s) => s.Length > 0 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
}
