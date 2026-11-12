using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Skins;
using ValTrainer.Progression;

namespace ValTrainer.Game.Weapon;

/// <summary>A skin's pattern. The numbers are the shader's <c>skin_pattern</c> ids (viewmodel.gdshaderinc): never renumber,
/// never reuse a retired number.</summary>
public enum SkinPattern
{
    // in use (skins 3 Core lines): Split = Two-Tone's border, Carbon twill, Blot camo, Brushed anodising, Damascus layers
    Solid = 0, Split = 1, Carbon = 2, Blot = 3, Brushed = 7, Damascus = 8,
    // RETIRED (skins 1 / skins 2 looks no skins-3 line wears): the shader has no branch any more, so these draw as a plain
    // col_a finish. Kept only so the numbers stay reserved; don't give them to a new look.
    Pixel = 4, Stripe = 5, Heat = 6, Hazard = 9, Hex = 10, Flake = 11,
    Strata = 12, Guilloche = 13, Rain = 14, Graticule = 15, Calibration = 16,
    // skins 3 (docs/research/skins3-contracts.md): the Signature and Apex finishes. A..D, Glow and Scale per pattern are
    // documented at each branch in viewmodel.gdshaderinc and set in SkinLooks.cs.
    /// <summary>Glasswork: jewel-tone stained-glass panes (lit from behind) in lead came (A came, B glass gloss).</summary>
    Glass = 17,
    /// <summary>Thunderhead: storm clouds (A storm blue, B silver tops, C shadows) with glowing lightning veins.</summary>
    Storm = 18,
    /// <summary>Basalt: charcoal hex columns (A/B) split by dim red magma seams (C, glow).</summary>
    Basalt = 19,
    /// <summary>Oscilloscope: gunmetal (A) framing phosphor screens (B) with a graticule, glowing traces and a dial (C face).</summary>
    Scope = 20,
    /// <summary>Rally: white (A) livery with red (B) / blue (C) stripes, a chequered band, a "7" roundel and carbon (D).</summary>
    Livery = 21,
    /// <summary>Frostbite: pale blue frozen metal (A) under fern frost (B) with glowing ice fractures.</summary>
    Frost = 22,
    /// <summary>Hive: amber honeycomb lit from within (A, C at the walls) in bronze walls (B), honey drips.</summary>
    Comb = 23,
    /// <summary>Foundry: hammered cast iron (A, B rims, C heat tint) with molten cracks (glow); L2 flowing melt + embers.</summary>
    Molten = 24,
    /// <summary>Aurora: dark frosted ice (A, D frost) under northern-light ribbons (B lower edge, C top); L2 drift.</summary>
    Aurora = 25,
    /// <summary>Orrery: navy enamel (B) with a brass (A) star chart, orbit rings with planets (C, D) and a dial; L2 orbits + ticks.</summary>
    Orrery = 26,
}

/// <summary>One finish of a skin (sRGB colour, PBR numbers).</summary>
public sealed record SkinFinish(Color Color, float Metallic, float Roughness, float Specular = 0.5f)
{
    public static SkinFinish Of(string hex, float metallic, float roughness, float specular = 0.5f) => new(new Color(hex), metallic, roughness, specular);
}

/// <summary>
/// A gun skin: an original procedural finish (research: skins-design-1.19.md §2.2, §3). Body surfaces (polymer, wood)
/// get the pattern; Trim (receiver metal) a solid finish or the pattern; Hardware (barrel, bolt, sights) stays factory
/// unless the skin says otherwise; glass never changes.
/// </summary>
/// <param name="Id">Stable lowercase id stored in settings.json: never renamed.</param>
/// <param name="A">Main colour (Solid: the only one). <paramref name="B"/>, <paramref name="C"/>, <paramref name="D"/>: the
/// pattern's other colours (D: Heat's fourth temper colour).</param>
/// <param name="Trim">Solid finish of the receiver metal; null = the same as A.</param>
/// <param name="Hardware">Solid finish of barrel / bolt / sights; null = factory.</param>
/// <param name="Scale">Pattern cell size, metres in gun space.</param>
/// <param name="Wear">The viewmodel shader's wear noise (detail) on skinned surfaces.</param>
/// <param name="Glow">Emissive accent (Hex walls) and its energy; <paramref name="Anim"/>: band sweeps per second, 0 = static.</param>
public sealed record GunSkin(
    string Id, string Name, string Blurb, SkinPattern Pattern,
    SkinFinish A, SkinFinish? B = null, SkinFinish? C = null, SkinFinish? D = null,
    SkinFinish? Trim = null, SkinFinish? Hardware = null,
    float Scale = 0.05f, float Wear = 0.4f,
    Color? Glow = null, float GlowEnergy = 0f, float Anim = 0f)
{
    /// <summary>Trim gets the pattern too (camo over the whole gun) instead of <see cref="Trim"/>'s solid finish.</summary>
    public bool PatternOnTrim { get; init; }
    /// <summary>Hardware gets the pattern too (metal treatments: Heat Tint, Damascus).</summary>
    public bool PatternOnHardware { get; init; }
    /// <summary>Split: where the colours meet, as a fraction of the gun's length from the muzzle.</summary>
    public float Mid { get; init; } = 0.45f;
    /// <summary>Pattern sub-mode (shader <c>skin_sub</c>): a variation inside a pattern. 0 = the pattern's normal look (no
    /// skins-3 pattern has another one yet; the skins-2 sub-modes are retired with their patterns).</summary>
    public int Sub { get; init; }
    /// <summary>Fire-reactive glow gain (shader <c>fire_pulse</c> × React), 0 = none. Set by a level's look transform.</summary>
    public float React { get; init; }
    /// <summary>Gun-side kill pulse gain (shader <c>kill_pulse</c> × KillGlow), 0 = none.</summary>
    public float KillGlow { get; init; }
    /// <summary>Draws the kill counter digits on the receiver (shader <c>counter_on</c>; value from Viewmodel.SetKillCount).</summary>
    public bool Counter { get; init; }
    public bool Animated => Anim > 0f;
    public bool Patterned => Pattern != SkinPattern.Solid;
}

/// <summary>
/// The player's gun skins: a facade over the skin catalogue (<see cref="SkinCatalog"/>, skins2-design-1.19.md) and the
/// unlocks (<see cref="Progress"/>) for the UI and the viewmodel. Every skin is made for ValTrainer: a shader plus numbers,
/// no textures, no Riot art and no look-alike of a VALORANT skin line. Skins are cosmetic and only on the first-person gun
/// (drills, previews, the Rewind's 3D view); bots, replays, telemetry and stats don't know about them.
/// <para>Storage: <see cref="AppSettings.GunSkins"/> (gun name → equip choice) and <see cref="AppSettings.GunSkinAll"/> (every
/// gun without its own entry); a choice is <c>line[/variant][@level]</c> (<see cref="SkinChoice"/>; v1.19-dev ids are line
/// ids). Resolution: dev <c>--skin line[/variant][@level]</c> (ignores locks) → the gun's entry → GunSkinAll → factory, then
/// clamped to what is unlocked (<see cref="SkinCatalog.Resolve"/>): a locked line shows factory but the choice is KEPT,
/// a locked level shows the highest unlocked one, a locked variant the base palette. An explicit "factory" entry keeps one
/// gun factory under a GunSkinAll; unknown ids and gun names fall through, never an error. Chamber's guns and anything not
/// buyable stay factory. Saving follows the settings rules (--dev never writes).</para>
/// <para>Runtime systems read <see cref="StateFor"/> (line, level, variant, features, effect ids); the viewmodel reads
/// <see cref="For"/> (the look). <see cref="Changed"/> fires on every change, including new unlocks (Main forwards
/// <see cref="Progress.Unlocked"/>).</para>
/// </summary>
public static class GunSkins
{
    public const string FactoryId = "factory";

    /// <summary>The base (level 1) look of every line in menu order (Factory is not in the list: it is "no skin").</summary>
    public static IReadOnlyList<GunSkin> All { get; } = SkinCatalog.Lines.Select(l => l.Look).ToList();

    /// <summary>Dev <c>--skin line[/variant][@level]</c> (also <c>line.variant@n</c>): every gun wears it, unlocked or not
    /// (screenshots, benchmarks); never saved.</summary>
    public static string? DevSkin { get; set; } = CmdLine.DevAfter("--skin");

    /// <summary>Raised after any change (live viewmodels re-skin, pickers redraw).</summary>
    public static event Action? Changed;

    static AppSettings? Current => Main.I?.Settings;

    /// <summary>The base look of the line a choice / id names; null for factory, null or an unknown id.</summary>
    public static GunSkin? Get(string? id) => Line(id)?.Look;

    /// <summary>The line a choice / id names ("carbon", "honeycomb/teal@3"); null for factory / unknown.</summary>
    public static SkinLine? Line(string? id) => SkinChoice.Parse(id) is { } c ? SkinCatalog.Get(c.Line) : null;

    /// <summary>Whether <paramref name="gun"/> can wear a skin (buyable guns; Chamber's keep their gold).</summary>
    public static bool Skinnable(WeaponKind gun) => Weapons.Buyable.Any(w => w.Kind == gun);

    /// <summary>Whether the line a choice / id names is unlocked (factory: always; unknown: false).</summary>
    public static bool IsUnlocked(string? id) =>
        id == null || string.Equals(id, FactoryId, StringComparison.OrdinalIgnoreCase) || (SkinChoice.Parse(id) is { } c && Progress.IsUnlocked(ItemIds.Base(c.Line)));

    /// <summary>The highest unlocked level of <paramref name="line"/> (levels unlock in order); 0 = the line is locked.</summary>
    public static int TopLevel(SkinLine line)
    {
        if (!Progress.IsUnlocked(ItemIds.Base(line.Id))) return 0;
        int n = 1;
        while (n < line.MaxLevel && Progress.IsUnlocked(ItemIds.Level(line.Id, n + 1))) n++;
        return n;
    }

    /// <summary>The first locked item needed to wear <paramref name="line"/> at <paramref name="level"/> in
    /// <paramref name="variant"/> (the line, then the level, then the variant); null = all of it is unlocked.</summary>
    public static string? LockedItem(SkinLine line, int level, SkinVariant? variant)
    {
        if (!Progress.IsUnlocked(ItemIds.Base(line.Id))) return ItemIds.Base(line.Id);
        int top = TopLevel(line);
        if (level > top) return ItemIds.Level(line.Id, top + 1); // the next level in order, not the one asked for
        if (variant != null && !Progress.IsUnlocked(ItemIds.Variant(line.Id, variant.Id))) return ItemIds.Variant(line.Id, variant.Id);
        return null;
    }

    /// <summary>The choice that wears <paramref name="line"/> at <paramref name="level"/> in <paramref name="variant"/>: the
    /// level is stored only when it is below the highest unlocked one (VALORANT's "equip a lower level"); otherwise the
    /// choice says "highest unlocked", so a later unlock upgrades it on its own.</summary>
    public static SkinChoice ChoiceOf(SkinLine line, int level, SkinVariant? variant)
    {
        int top = Math.Max(1, TopLevel(line));
        int lv = line.MaxLevel <= 1 || level >= top ? 0 : Math.Clamp(level, 1, line.MaxLevel);
        return new SkinChoice(line.Id, lv, variant?.Id);
    }

    /// <summary>The look <paramref name="gun"/> wears now (resolved and clamped); null = factory.</summary>
    public static GunSkin? For(WeaponKind gun, AppSettings? settings = null) => StateFor(gun, settings)?.Look;

    /// <summary>What <paramref name="gun"/> wears now: line, level and variant clamped to the unlocks; null = factory.</summary>
    public static SkinState? StateFor(WeaponKind gun, AppSettings? settings = null)
    {
        if (ChoiceFor(gun, settings, out bool dev) is not { } c) return null;
        return SkinCatalog.Resolve(c, dev ? _ => true : Progress.IsUnlocked);
    }

    /// <summary>The choice in effect for <paramref name="gun"/> before the lock clamp (dev skin → own entry → every-gun
    /// choice); null = factory.</summary>
    public static SkinChoice? ChoiceFor(WeaponKind gun, AppSettings? settings = null) => ChoiceFor(gun, settings, out _);

    static SkinChoice? ChoiceFor(WeaponKind gun, AppSettings? settings, out bool dev)
    {
        dev = false;
        if (!Skinnable(gun)) return null;
        if (SkinChoice.Parse(DevSkin) is { } d) { dev = true; return d; }
        var s = settings ?? Current;
        if (s == null) return null;
        if (s.GunSkins != null && s.GunSkins.TryGetValue(gun.ToString(), out var own))
        {
            if (string.Equals(own, FactoryId, StringComparison.OrdinalIgnoreCase)) return null;
            if (SkinChoice.Parse(own) is { } o) return o;
        }
        return SkinChoice.Parse(s.GunSkinAll);
    }

    /// <summary>The resolved line id of <paramref name="gun"/> ("factory" when none, or when its choice is locked).</summary>
    public static string IdFor(WeaponKind gun, AppSettings? settings = null) => StateFor(gun, settings)?.Line.Id ?? FactoryId;

    /// <summary>True when the gun's choice is (partly) locked: it shows factory, a lower level or the base palette.</summary>
    public static bool ShowsLess(WeaponKind gun, AppSettings? settings = null)
    {
        if (ChoiceFor(gun, settings, out bool dev) is not { } c || dev) return false;
        if (SkinCatalog.Resolve(c, Progress.IsUnlocked) is not { } st) return true;
        bool levelLocked = c.Level > 0 && st.Level < c.Level;
        return levelLocked || (c.Variant != null && st.Variant == null);
    }

    /// <summary>The gun's own entry (null = it follows <see cref="AppSettings.GunSkinAll"/>).</summary>
    public static string? OwnId(WeaponKind gun, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        return s?.GunSkins != null && s.GunSkins.TryGetValue(gun.ToString(), out var id) ? id : null;
    }

    /// <summary>Canonical stored form of a choice / id ("factory" stays "factory"; unknown → null).</summary>
    static string? Canonical(string? id) =>
        id == null ? null : string.Equals(id.Trim(), FactoryId, StringComparison.OrdinalIgnoreCase) ? FactoryId : SkinChoice.Parse(id)?.ToString();

    /// <summary>Gives <paramref name="gun"/> a choice (<c>line[/variant][@level]</c>) and saves; a locked choice is stored too
    /// (it applies once unlocked). "factory" stores an explicit factory entry when an every-gun choice is set (so this gun
    /// stays factory), else removes the entry; null removes it (back to the every-gun choice).</summary>
    public static void Set(WeaponKind gun, string? id, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        if (s == null || !Skinnable(gun)) return;
        string? v = Canonical(id);
        string? all = Canonical(s.GunSkinAll);
        if (v == FactoryId && all is null or FactoryId) v = null; // factory is already what it falls through to
        if (v != null && v == all) v = null;                       // the same as every other gun: follow GunSkinAll
        if (v != null)
        {
            s.GunSkins ??= new();
            s.GunSkins[gun.ToString()] = v;
        }
        else if (s.GunSkins != null)
        {
            s.GunSkins.Remove(gun.ToString());
            if (s.GunSkins.Count == 0) s.GunSkins = null;
        }
        s.Save();
        Changed?.Invoke();
    }

    /// <summary>Gives <paramref name="gun"/> a choice (null = back to the every-gun choice). Saves.</summary>
    public static void SetChoice(WeaponKind gun, SkinChoice? choice, AppSettings? settings = null) => Set(gun, choice?.ToString(), settings);

    /// <summary>Every gun wears <paramref name="id"/> (null / "factory" = factory); per-gun entries are cleared. Saves.</summary>
    public static void SetAll(string? id, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        if (s == null) return;
        s.GunSkinAll = SkinChoice.Parse(id)?.ToString();
        s.GunSkins = null;
        s.Save();
        Changed?.Invoke();
    }

    /// <summary>Sets only the every-gun choice; guns with their own entry keep it (Settings' arrows). Saves.</summary>
    public static void SetDefault(string? id, AppSettings? settings = null)
    {
        var s = settings ?? Current;
        if (s == null) return;
        s.GunSkinAll = SkinChoice.Parse(id)?.ToString();
        // entries that now equal the every-gun choice (or an explicit factory with no every-gun choice) are redundant
        if (s.GunSkins != null)
        {
            foreach (var key in s.GunSkins.Keys.ToList())
            {
                var v = Canonical(s.GunSkins[key]);
                if (v == s.GunSkinAll || (s.GunSkinAll == null && v == FactoryId)) s.GunSkins.Remove(key);
            }
            if (s.GunSkins.Count == 0) s.GunSkins = null;
        }
        s.Save();
        Changed?.Invoke();
    }

    /// <summary>Every gun back to factory. Saves.</summary>
    public static void ResetAll(AppSettings? settings = null) => SetAll(null, settings);

    /// <summary>Guns with a skin of their own (entries that differ from the every-gun skin).</summary>
    public static int CustomCount(AppSettings? settings = null) => (settings ?? Current)?.GunSkins?.Count(e => Weapons.Parse(e.Key) is { } g && Skinnable(g)) ?? 0;

    /// <summary>Settings' one-liner: "Factory", "Carbon Weave on every gun", "3 guns customised · others Graphite".</summary>
    public static string Summary(AppSettings? settings = null)
    {
        var s = settings ?? Current;
        int n = CustomCount(s);
        var allSkin = Get(s?.GunSkinAll);
        // shown in Settings (a translated one-liner); skin names are English data, translated here
        if (n == 0) return allSkin == null ? L.T("Factory") : L.F($"{L.T(allSkin.Name)} on every gun");
        string guns = L.P(n, $"{n} gun customised", $"{n} guns customised");
        return allSkin == null ? guns : L.F($"{guns} · others {L.T(allSkin.Name)}");
    }

    /// <summary>Display name of a skin id / choice's line ("Factory" for factory / unknown). English: translate where shown.</summary>
    public static string NameOf(string? id) => Line(id)?.Name ?? L.N("Factory");

    /// <summary>Tells live viewmodels and pickers that the resolved skins changed (dev overrides, previews, new unlocks).</summary>
    public static void NotifyChanged() => Changed?.Invoke();
}
