using System.Globalization;
using Godot;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.Game.Skins;

/// <summary>What a skin level adds (skins2-design-1.19.md §4.1). Features accumulate: level n has every feature of levels 1..n.</summary>
[Flags]
public enum SkinFeature
{
    None = 0,
    /// <summary>Animated material (<see cref="GunSkin.Anim"/>, TIME-driven pattern motion). Owner: Skins visuals.</summary>
    Animated = 1,
    /// <summary>Reacts to firing / kills (<see cref="GunSkin.React"/> → shader <c>fire_pulse</c> / <c>kill_pulse</c>). Owner: Skins visuals.</summary>
    Reactive = 2,
    /// <summary>Sound set: fire / reload layers and equip flourish (<see cref="SkinLevel.Sounds"/>). Owner: Skins audio.</summary>
    Sounds = 4,
    /// <summary>Kill effect: gun-side pulse + world effect + kill-effect sound (<see cref="SkinLevel.KillFx"/>). Owner: Kills/FX.</summary>
    KillFx = 8,
    /// <summary>Finisher on the round-winning kill (<see cref="SkinLevel.Finisher"/>). Owner: Kills/FX.</summary>
    Finisher = 16,
    /// <summary>Restyled HUD multi-kill banner (<see cref="SkinLevel.Banner"/>). Owner: Kills/FX (Hud).</summary>
    KillBanner = 32,
    /// <summary>Escalating kill sounds 1–5 replace the factory kill1..kill5. Owner: Skins audio.</summary>
    KillSounds = 64,
    /// <summary>Kill counter digits on the gun (<see cref="GunSkin.Counter"/>). Owner: Skins visuals; value fed by Kills/FX.</summary>
    KillCounter = 128,
    /// <summary>The player's tracers take <see cref="FxPalette.Main"/>. Owner: Kills/FX.</summary>
    Tracers = 256,
    /// <summary>Equip flourish added to the raise curve (same duration). Owner: Skins visuals (+ equip sound: audio).</summary>
    EquipFlourish = 512,
    // ---- skins 3 (docs/research/skins3-contracts.md) ----
    /// <summary>Muzzle flash, tracer style and bullet impact of the line (<see cref="SkinLevel.BulletFx"/> recipe id). Implies
    /// <see cref="Tracers"/>. Owner: Bullet/Kill FX.</summary>
    BulletFx = 1024,
    /// <summary>Inspect flourish: the line's VFX + sound on top of the generic inspect animation (<see cref="SkinLevel.Inspect"/>
    /// recipe id). Every gun can be inspected; without this the plain inspect plays. Owner: Animation (+ sound: audio).</summary>
    Inspect = 2048,
    /// <summary>Special reload animation: a restyled reload motion + VFX + sound with EXACTLY the factory reload duration
    /// (<see cref="SkinLevel.ReloadAnim"/> recipe id). Apex only. Owner: Animation (+ sound: audio).</summary>
    ReloadAnim = 4096,
    /// <summary>The line's own body-hit / headshot sounds (unless Settings → "Classic hit sounds"). Owner: audio.</summary>
    HitSounds = 8192,
}

/// <summary>The colours of a line's (or variant's) effects: kill effect, finisher, banner, tracers.
/// <see cref="Alt"/> replaces <see cref="Main"/> when Main is close to the enemy highlight colour (skins2-design §4.6).</summary>
public sealed record FxPalette(Color Main, Color Accent, Color Alt)
{
    public static FxPalette Of(string main, string accent, string alt) => new(new Color(main), new Color(accent), new Color(alt));
}

/// <summary>
/// One upgrade level of a line. Level 1's look is <see cref="SkinLine.Look"/>; a level's <see cref="Look"/> transform is applied
/// on top of every lower level's (e.g. L2 turns on <see cref="GunSkin.Anim"/> and <see cref="GunSkin.React"/>).
/// Effect ids are resolved by their owners: <see cref="Sounds"/> = a folder <c>assets/audio/skins/&lt;id&gt;/</c> (Skins audio),
/// <see cref="KillFx"/> / <see cref="Finisher"/> = recipe ids (Kills/FX: KillFxRecipes), <see cref="Banner"/> = a HUD banner
/// style id (Kills/FX). Null = this level adds none (a lower level's id still applies: see <see cref="SkinState"/>).
/// </summary>
/// <param name="N">1-based level number.</param>
/// <param name="Adds">The features this level adds.</param>
/// <param name="Blurb">Short English label for the level track ("Animated", "Inspect + special reload"); marked with L.N,
/// translated where shown.</param>
public sealed record SkinLevel(int N, SkinFeature Adds, string Blurb)
{
    public Func<GunSkin, GunSkin>? Look { get; init; }
    public string? Sounds { get; init; }
    public string? KillFx { get; init; }
    public string? Finisher { get; init; }
    public string? Banner { get; init; }
    /// <summary>Skins 3: bullet FX recipe id (muzzle / tracer / impact; Bullet/Kill FX agent).</summary>
    public string? BulletFx { get; init; }
    /// <summary>Skins 3: inspect flourish recipe id (Animation agent).</summary>
    public string? Inspect { get; init; }
    /// <summary>Skins 3: special reload animation recipe id (Animation agent).</summary>
    public string? ReloadAnim { get; init; }

    /// <summary>The level's main slot for the progression table.</summary>
    public UnlockSlot Slot =>
        N <= 1 ? UnlockSlot.Base
        : (Adds & SkinFeature.Finisher) != 0 ? UnlockSlot.Finisher
        : (Adds & SkinFeature.KillCounter) != 0 ? UnlockSlot.Extra
        : (Adds & SkinFeature.KillFx) != 0 ? UnlockSlot.Kill
        : (Adds & (SkinFeature.Sounds | SkinFeature.KillSounds)) != 0 ? UnlockSlot.Sound
        : UnlockSlot.Vfx;
}

/// <summary>A recolour of a line: transforms the look's finishes (and pattern sub-mode) and replaces the effect palette.</summary>
/// <param name="Id">Lowercase ASCII, stable (item id <c>line/&lt;Id&gt;</c>).</param>
/// <param name="Name">English display name (L.N), translated where shown.</param>
/// <param name="Recolour">Applied to the level-1 look before the level transforms.</param>
/// <param name="Palette">Effect colours; null = the line's.</param>
public sealed record SkinVariant(string Id, string Name, Func<GunSkin, GunSkin> Recolour, FxPalette? Palette = null);

/// <summary>
/// A skin line (skins3-contracts.md §1–2): a tier (<see cref="Edition"/>: Core, Signature, Apex), 1 or 5 levels, 0 or 3
/// variants. Display texts are English data (L.N), translated where shown.
/// </summary>
public sealed record SkinLine(string Id, string Name, string Blurb, Edition Edition, GunSkin Look,
                              IReadOnlyList<SkinLevel> Levels, IReadOnlyList<SkinVariant> Variants, FxPalette Palette)
{
    /// <summary>Unused since skins 3 (no line is in a collection); kept for the contract. Null.</summary>
    public string? Collection { get; init; }
    /// <summary>Display name of the base palette in the variant swatches ("Molten"); English, L.N.</summary>
    public string BaseName { get; init; } = "";

    public int MaxLevel => Levels.Count;

    public SkinFeature FeaturesAt(int level)
    {
        var f = SkinFeature.None;
        for (int i = 0; i < Math.Min(level, Levels.Count); i++) f |= Levels[i].Adds;
        return f;
    }

    public SkinVariant? Variant(string? id) => id == null ? null : Variants.FirstOrDefault(v => v.Id == id);

    /// <summary>The look at <paramref name="level"/> (clamped 1..MaxLevel) wearing <paramref name="v"/> (null = base palette).
    /// The look's Id is <c>line</c> or <c>line/variant</c> (swatch cache key; the level is not part of it).</summary>
    public GunSkin LookAt(int level, SkinVariant? v)
    {
        var look = v != null ? v.Recolour(Look) with { Id = ItemIds.Variant(Id, v.Id) } : Look;
        int n = Math.Clamp(level, 1, Math.Max(1, MaxLevel));
        for (int i = 1; i < n; i++) if (Levels[i].Look is { } t) look = t(look);
        return look;
    }

    /// <summary>The newest non-null effect id at or below <paramref name="level"/>.</summary>
    public string? Upto(int level, Func<SkinLevel, string?> pick)
    {
        for (int i = Math.Min(level, Levels.Count) - 1; i >= 0; i--) if (pick(Levels[i]) is { } id) return id;
        return null;
    }

    public UnlockLine ToUnlockLine(int order) => new(Id, Edition, order, Levels.Select(l => l.Slot).ToList(), Variants.Select(v => v.Id).ToList(), Collection);
}

/// <summary>
/// A stored equip choice (per gun in <c>AppSettings.GunSkins</c>, or <c>GunSkinAll</c>): <c>line[/variant][@level]</c>.
/// Level 0 = the highest unlocked level (the default; VALORANT's "equip a lower level" toggle stores an explicit level).
/// <see cref="Parse"/> also accepts item ids (<c>line/3</c>), the skins doc's dev syntax (<c>line.variant@n</c>) and every
/// skins-2 id (<see cref="SkinCatalog.Legacy"/>: Carbon and Damascus kept, the rest factory). A choice is <b>kept</b> when locked; resolution
/// (<see cref="SkinCatalog.Resolve"/>) clamps it.
/// </summary>
public readonly record struct SkinChoice(string Line, int Level = 0, string? Variant = null)
{
    /// <summary>A known line's choice (unknown variants dropped, level clamped to 0..MaxLevel); null for factory, null,
    /// empty or unknown lines.</summary>
    public static SkinChoice? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().ToLowerInvariant();
        int level = 0;
        int at = s.IndexOf('@');
        if (at >= 0)
        {
            if (!int.TryParse(s[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out level)) level = 0;
            s = s[..at];
        }
        int sep = s.IndexOfAny(new[] { '/', '.' });
        string line = sep < 0 ? s : s[..sep];
        string? rest = sep < 0 ? null : s[(sep + 1)..];
        if (SkinCatalog.Legacy.TryGetValue(line, out var mapped)) line = mapped;
        if (SkinCatalog.Get(line) is not { } l) return null;
        string? variant = null;
        if (rest is { Length: > 0 })
        {
            if (char.IsAsciiDigit(rest[0])) { if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && level == 0) level = n; }
            else variant = l.Variant(rest)?.Id;
        }
        return new SkinChoice(l.Id, l.MaxLevel <= 1 ? 0 : Math.Clamp(level, 0, l.MaxLevel), variant);
    }

    /// <summary>Canonical stored form: <c>line</c>, <c>line/variant</c>, <c>line@3</c>, <c>line/variant@3</c>.</summary>
    public override string ToString() =>
        Line + (Variant != null ? "/" + Variant : "") + (Level > 0 ? "@" + Level.ToString(CultureInfo.InvariantCulture) : "");
}

/// <summary>
/// What one gun wears right now: resolved and clamped to what is unlocked (<see cref="Weapon.GunSkins.StateFor"/>). The
/// runtime systems (viewmodel, audio, kill effects) read everything from here.
/// </summary>
public readonly record struct SkinState(SkinLine Line, int Level, SkinVariant? Variant)
{
    public SkinFeature Features => Line.FeaturesAt(Level);
    public bool Has(SkinFeature f) => (Features & f) != 0;
    public FxPalette Palette => Variant?.Palette ?? Line.Palette;
    /// <summary>The viewmodel look (pattern, colours, level shader params).</summary>
    public GunSkin Look => Line.LookAt(Level, Variant);
    /// <summary>Sound set id (folder under assets/audio/skins), null = factory sounds.</summary>
    public string? Sounds => Has(SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.KillFx) ? Line.Upto(Level, l => l.Sounds) : null;
    public string? KillFx => Has(SkinFeature.KillFx) ? Line.Upto(Level, l => l.KillFx) : null;
    public string? Finisher => Has(SkinFeature.Finisher) ? Line.Upto(Level, l => l.Finisher) : null;
    public string? Banner => Has(SkinFeature.KillBanner) ? Line.Upto(Level, l => l.Banner) : null;
    /// <summary>Tracer colour for the player's shots, null = factory.</summary>
    public Color? Tracer => Has(SkinFeature.Tracers | SkinFeature.BulletFx) ? Palette.Main : null;
    /// <summary>Skins 3: bullet FX recipe id, null = factory muzzle / tracer / impact.</summary>
    public string? BulletFx => Has(SkinFeature.BulletFx) ? Line.Upto(Level, l => l.BulletFx) : null;
    /// <summary>Skins 3: inspect flourish recipe id, null = the plain inspect.</summary>
    public string? Inspect => Has(SkinFeature.Inspect) ? Line.Upto(Level, l => l.Inspect) : null;
    /// <summary>Skins 3: special reload animation recipe id, null = the factory reload.</summary>
    public string? ReloadAnim => Has(SkinFeature.ReloadAnim) ? Line.Upto(Level, l => l.ReloadAnim) : null;
    /// <summary>Item id of what is worn (<c>line</c>, <c>line/n</c>); the variant has its own <see cref="VariantItem"/>.</summary>
    public string LevelItem => ItemIds.Level(Line.Id, Level);
    public string? VariantItem => Variant != null ? ItemIds.Variant(Line.Id, Variant.Id) : null;
}
