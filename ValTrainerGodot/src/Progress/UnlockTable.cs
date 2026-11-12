namespace ValTrainer.Progression;

/// <summary>A skin line's tier (skins3-contracts.md §1; our own names). Declaration order is the display order and the
/// order of richness: Core (a finish, free), Signature (one level with sounds and effects), Apex (five levels, variants).</summary>
public enum Edition { Core, Signature, Apex }

/// <summary>What a skin level mainly adds, as Progress sees it (the requirement table gates kill effects and finishers on
/// improvement, everything else on XP). The skins side derives it from the level's features
/// (<see cref="Game.Skins.SkinLevel.Slot"/>); Progress never looks at skin data beyond this.</summary>
public enum UnlockSlot { Base, Vfx, Sound, Kill, Finisher, Extra }

/// <summary>
/// One skin line as Progress needs it: registered once by the skins side (<see cref="Game.Skins.SkinCatalog.UnlockLines"/>,
/// pulled lazily by <see cref="Progress"/>). Item ids follow <see cref="ItemIds"/>: <c>line</c> (level 1), <c>line/n</c>
/// (level n ≥ 2), <c>line/variant</c>.
/// </summary>
/// <param name="Line">Stable lowercase line id ("carbon", "foundry").</param>
/// <param name="Edition">The line's tier.</param>
/// <param name="Order">0-based position of the line within its tier in catalogue order (the table's "Signature #2" = Order 1).</param>
/// <param name="Levels">Slot of each level, index 0 = level 1 (always <see cref="UnlockSlot.Base"/>).</param>
/// <param name="Variants">Variant ids in catalogue order (the base palette is not a variant).</param>
/// <param name="Collection">Unused since skins 3 (kept for the contract); null.</param>
public sealed record UnlockLine(string Line, Edition Edition, int Order, IReadOnlyList<UnlockSlot> Levels, IReadOnlyList<string> Variants,
                                string? Collection = null)
{
    public int MaxLevel => Levels.Count;
    /// <summary>Apex variants need the top level (VALORANT's rule); any other line's variants only its base.</summary>
    public int VariantNeedsLevel => Edition == Edition.Apex ? MaxLevel : 1;
}

/// <summary>
/// The requirement table: item id → requirement source string (<see cref="Requirement"/> grammar). Pacing lives here and
/// only here (the skins side never hard-codes these numbers).
/// <para>Skins 3 pacing (skins3-contracts.md §2; philosophy from progression-design-1.19.md §6: every reward mixes grind XP
/// with real improvement, and kill effects and finishers need improvement):</para>
/// <list type="bullet">
/// <item><b>Core</b>: free from the start (never recorded).</item>
/// <item><b>Signature</b> (sounds, bullet and kill effects, inspect, banner in one level): one unlock each, spread from
/// level 3 to 26, each with an improvement term that grows: a badge in 3 drills, stars, the coach placement, badge
/// rank-ups and, for the last one, a confirmed coach rank-up.</item>
/// <item><b>Apex</b>: level by level. L1 (look) and L2 (animated) are XP; L3 (sounds + effects) adds stars; L4 (inspect +
/// special reload) adds stars and badge rank-ups; L5 (finisher + kill counter) needs confirmed coach rank-ups, the
/// hardest gate in the game. Variants need L5 and are "earned with the skin on".</item>
/// </list>
/// <para>The developer's retro credit (level 19, 12★, 5 badge rank-ups, 7 real PBs, coach placed, Pro badges, no
/// confirmed rank-up yet) gets Signature #1–#5 and Foundry L1–L2, and nothing at Apex L4/L5 or the last Signature: the
/// playtest found the old challenge line (benchmark ranks the developer already had) far too generous, so no gate here
/// is met by past badges alone.</para>
/// <para>Implicit structural rules (not written in the strings, enforced by <see cref="Progress.Status"/>): level n needs
/// level n−1; a variant needs <see cref="UnlockLine.VariantNeedsLevel"/>.</para>
/// </summary>
public static class UnlockTable
{
    /// <summary>Hand-set requirements by item id; they win over the tier defaults. Empty: the defaults cover every line.</summary>
    public static readonly IReadOnlyDictionary<string, string> Overrides = new Dictionary<string, string>();

    /// <summary>Signature #1–#7, in catalogue order (lines past #7 continue the last row: see <see cref="Level"/>).</summary>
    static readonly string[] Signature =
    {
        "level:3+drills:3",                                     // #1, the first session: a badge in 3 drills
        "level:6+stars:1",                                      // #2: the first real PB or badge rank-up
        "level:9+(coach:placed+stars:1|stars:3)",               // #3: the coach tie-in (placed + 1 star, or 3 stars)
        "level:12+stars:5",                                     // #4
        "level:16+stars:8",                                     // #5
        "level:21+stars:12+badgeups:2",                         // #6
        "level:26+stars:16+(rankup:overall:1|rankup:skill:1)",  // #7: a confirmed coach rank-up
    };

    /// <summary>Apex #1–#3 per level (index = level − 1): the account level, the stars, the badge rank-ups (L4) and the
    /// confirmed overall / skill rank-ups (L5). Lines past #3 continue with <see cref="ApexStep"/>.</summary>
    static readonly int[] ApexBase = { 14, 24, 34 };
    static readonly int[] ApexBaseStars = { 0, 10, 20 };
    static readonly int[] ApexKillStars = { 6, 14, 24 };
    static readonly int[] ApexL4Stars = { 10, 18, 28 };
    static readonly int[] ApexL4BadgeUps = { 2, 4, 6 };
    static readonly int[] ApexL5Stars = { 16, 26, 36 };
    const int ApexStep = 10;

    /// <summary>Builds the whole table for <paramref name="lines"/> (every level and variant item).</summary>
    public static Dictionary<string, string> Build(IEnumerable<UnlockLine> lines)
    {
        var t = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            for (int n = 1; n <= l.MaxLevel; n++)
            {
                string id = ItemIds.Level(l.Line, n);
                t[id] = Overrides.TryGetValue(id, out var o) ? o : Level(l, n);
            }
            for (int i = 0; i < l.Variants.Count; i++)
            {
                string id = ItemIds.Variant(l.Line, l.Variants[i]);
                t[id] = Overrides.TryGetValue(id, out var o) ? o : Variant(l, i);
            }
        }
        return t;
    }

    static int At(int[] table, int i, int step) => i < table.Length ? table[i] : table[^1] + step * (i - table.Length + 1);

    /// <summary>Tier default for level <paramref name="n"/> (1-based) of <paramref name="l"/>.</summary>
    public static string Level(UnlockLine l, int n)
    {
        int k = l.Order;
        switch (l.Edition)
        {
            case Edition.Core:
                return "free";
            case Edition.Signature:
            {
                if (k < Signature.Length) return Signature[k];
                // more Signature lines than planned: 5 levels and 4 stars further each, still a confirmed rank-up
                int extra = k - Signature.Length + 1;
                return $"level:{26 + 5 * extra}+stars:{16 + 4 * extra}+(rankup:overall:1|rankup:skill:{1 + extra})";
            }
            default: // Apex
            {
                int lv = At(ApexBase, k, ApexStep);
                string Stars(int[] table, int step) => At(table, k, step) is var s and > 0 ? $"+stars:{s}" : "";
                return n switch
                {
                    <= 1 => $"level:{lv}{Stars(ApexBaseStars, 10)}",
                    2 => $"level:{lv + 3}",
                    3 => $"level:{lv + 6}{Stars(ApexKillStars, 10)}",
                    4 => $"level:{lv + 10}{Stars(ApexL4Stars, 10)}+badgeups:{At(ApexL4BadgeUps, k, 2)}",
                    _ => $"level:{lv + 12}{Stars(ApexL5Stars, 10)}+(rankup:overall:{1 + k}|rankup:skill:{3 + 2 * k})",
                };
            }
        }
    }

    /// <summary>Tier default for variant number <paramref name="i"/> (0-based) of <paramref name="l"/>: "earn it with the
    /// skin on" (runs, a real PB, a badge rank-up while the line is on the gun).</summary>
    public static string Variant(UnlockLine l, int i)
    {
        string line = l.Line;
        int runs = l.Edition == Edition.Apex ? 25 : 10;
        return i switch { 0 => $"with:{line}:runs:{runs}", 1 => $"with:{line}:pb", 2 => $"with:{line}:badgeup", _ => $"with:{line}:runs:{runs * i}" };
    }
}
