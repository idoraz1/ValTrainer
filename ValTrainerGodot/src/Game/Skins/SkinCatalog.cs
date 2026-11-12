using ValTrainer.Core;
using ValTrainer.Game.Weapon;
using ValTrainer.Progression;

namespace ValTrainer.Game.Skins;

/// <summary>
/// The skin catalogue (skins3-contracts.md §1–2): 15 lines in three tiers of our own, Core (5), Signature (7) and Apex (3).
/// Every look is our own procedural shader finish from <see cref="SkinLooks"/>: no Riot assets, names or look-alikes. Display
/// texts are English data (L.N), translated where shown. Unlock requirements are NOT here: Progress owns them
/// (<see cref="UnlockTable"/>).
/// <para>Ids are stable forever: line ids are stored in settings.json (<see cref="SkinChoice"/>) and progress.json
/// (<see cref="ItemIds"/>). Skins-2 ids (v1.19 dev builds, never released) map through <see cref="Legacy"/>.</para>
/// <para><b>Skins data/UI agent</b> owns this file. Every effect recipe id (sound set, bullet FX, kill effect, inspect,
/// special reload, finisher, banner) is the line id: a contract with the Skins audio, Bullet/Kill FX and Animation agents.</para>
/// </summary>
public static class SkinCatalog
{
    /// <summary>What a Signature line's single level has: everything but animation, the finisher and the special reload.</summary>
    public const SkinFeature SignatureFeatures = SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.HitSounds | SkinFeature.BulletFx
                                                 | SkinFeature.KillFx | SkinFeature.Inspect | SkinFeature.KillBanner;

    static readonly SkinVariant[] None = Array.Empty<SkinVariant>();

    /// <summary>The level-1 look from <see cref="SkinLooks"/> with the catalogue's name and blurb.</summary>
    static GunSkin Look(string id, string name, string blurb) => SkinLooks.Look(id) with { Name = name, Blurb = blurb };

    /// <summary>A kill effect needs the gun-side kill pulse; keeps the visuals' own gain when they set one.</summary>
    static GunSkin KillPulse(GunSkin s) => s.KillGlow > 0f ? s : s with { KillGlow = 0.8f };

    static SkinLine Core(string id, string name, string blurb) =>
        new(id, name, blurb, Edition.Core, Look(id, name, blurb), new[] { new SkinLevel(1, SkinFeature.None, L.N("Base")) }, None, SkinLooks.Palette(id));

    /// <summary>Signature: one level with sounds, hit and kill sounds, bullet FX, kill effect, inspect flourish and banner.</summary>
    static SkinLine Signature(string id, string name, string blurb) =>
        new(id, name, blurb, Edition.Signature, KillPulse(Look(id, name, blurb)), new[]
        {
            new SkinLevel(1, SignatureFeatures, L.N("Sounds, bullet & kill effects, inspect"))
                { Sounds = id, BulletFx = id, KillFx = id, Inspect = id, Banner = id },
        }, None, SkinLooks.Palette(id));

    /// <summary>
    /// Apex: five levels (skins3-contracts §2): 1 base look; 2 animated and reactive; 3 sounds, hit and kill sounds, bullet FX
    /// and kill effect; 4 inspect flourish, special reload and banner; 5 finisher and kill counter. Three variants.
    /// </summary>
    static SkinLine Apex(string id, string name, string blurb) =>
        new(id, name, blurb, Edition.Apex, Look(id, name, blurb), new SkinLevel[]
        {
            new(1, SkinFeature.None, L.N("Base")),
            new(2, SkinFeature.Animated | SkinFeature.Reactive, L.N("Animated")) { Look = SkinLooks.Animated(id) },
            new(3, SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.HitSounds | SkinFeature.BulletFx | SkinFeature.KillFx,
                L.N("Sounds + bullet & kill effects")) { Sounds = id, BulletFx = id, KillFx = id, Look = KillPulse },
            new(4, SkinFeature.Inspect | SkinFeature.ReloadAnim | SkinFeature.KillBanner, L.N("Inspect + special reload"))
                { Inspect = id, ReloadAnim = id, Banner = id },
            new(5, SkinFeature.Finisher | SkinFeature.KillCounter, L.N("Finisher + kill counter"))
                { Finisher = id, Look = s => s with { Counter = true } },
        }, SkinLooks.Variants(id), SkinLooks.Palette(id)) { BaseName = SkinLooks.BaseName(id) };

    static readonly SkinLine[] lines =
    {
        // ---- Core: free from the start, a finish only ----
        Core("carbon", L.N("Carbon Weave"), L.N("Clear-coated twill carbon fibre")),
        Core("desertcamo", L.N("Desert Camo"), L.N("Sand and tan blot camo")),
        Core("anodized", L.N("Anodized Teal"), L.N("Brushed teal anodised alloy")),
        Core("damascus", L.N("Damascus Steel"), L.N("Folded steel in wavy light and dark layers")),
        Core("twotone", L.N("Two-Tone Polymer"), L.N("Dark polymer with light furniture")),

        // ---- Signature: one level with its own sounds, bullet and kill effects, inspect and banner ----
        Signature("glasswork", L.N("Glasswork"), L.N("Stained-glass panels in lead came, jewel tones lit from within")),
        Signature("thunderhead", L.N("Thunderhead"), L.N("Silver-blue storm clouds veined with blue-white lightning")),
        Signature("basalt", L.N("Basalt"), L.N("Charcoal hex columns over dim red magma seams")),
        Signature("oscilloscope", L.N("Oscilloscope"), L.N("Gunmetal with glowing green waveforms, a graticule and small dials")),
        Signature("rally", L.N("Rally"), L.N("Motorsport livery: white, red and blue stripes and a number-7 roundel")),
        Signature("frostbite", L.N("Frostbite"), L.N("Pale blue frozen metal with frost crystals and ice fractures")),
        Signature("hive", L.N("Hive"), L.N("Translucent amber honeycomb lit from within, in a bronze frame")),

        // ---- Apex: five levels up to the finisher, three variants ----
        Apex("foundry", L.N("Foundry"), L.N("Cast iron with molten orange glowing through the cracks")),
        Apex("aurora", L.N("Aurora"), L.N("Dark frosted ice with green and violet northern lights flowing through it")),
        Apex("orrery", L.N("Orrery"), L.N("Brass and navy enamel with star charts and planetary rings round the barrel")),
    };

    static readonly Dictionary<string, SkinLine> byId = lines.ToDictionary(l => l.Id);

    /// <summary>
    /// Stored skins-2 id (v1.19 dev builds, never released) → line id. Carbon and Damascus survive as Core lines; every other
    /// skins-2 line maps to <see cref="GunSkins.FactoryId"/>, so a stored choice of it parses to factory without an error
    /// (<see cref="SkinChoice.Parse"/>; per gun, the every-gun skin then applies). A variant or level stored with a kept id is
    /// dropped (Core lines have neither). Unknown ids are factory too; this map only documents and tests the old ones.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Legacy = new Dictionary<string, string>
    {
        ["carbon"] = "carbon", ["damascus"] = "damascus",
        ["graphite"] = GunSkins.FactoryId, ["bone"] = GunSkins.FactoryId, ["olive"] = GunSkins.FactoryId, ["fde"] = GunSkins.FactoryId,
        ["hazard"] = GunSkins.FactoryId, ["woodland"] = GunSkins.FactoryId, ["arctic"] = GunSkins.FactoryId, ["urban"] = GunSkins.FactoryId,
        ["tiger"] = GunSkins.FactoryId, ["signal"] = GunSkins.FactoryId, ["cobalt"] = GunSkins.FactoryId, ["heat"] = GunSkins.FactoryId,
        ["flake"] = GunSkins.FactoryId, ["honeycomb"] = GunSkins.FactoryId, ["rainfront"] = GunSkins.FactoryId, ["strata"] = GunSkins.FactoryId,
        ["escapement"] = GunSkins.FactoryId, ["phosphor"] = GunSkins.FactoryId, ["calibrated"] = GunSkins.FactoryId,
    };

    /// <summary>English display name of a tier (L.N; translate where shown).</summary>
    public static string EditionName(Edition e) => e switch
    {
        Edition.Core => L.N("Core"), Edition.Signature => L.N("Signature"), _ => L.N("Apex"),
    };

    /// <summary>What a tier is, in a few words (English data, translated where shown).</summary>
    public static string EditionRule(Edition e) => e switch
    {
        Edition.Core => L.N("FREE · A FINISH"),
        Edition.Signature => L.N("SOUNDS · EFFECTS · INSPECT"),
        _ => L.N("5 LEVELS · FINISHER · VARIANTS"),
    };

    /// <summary>Every line in catalogue order (grouped by tier: Core, Signature, Apex).</summary>
    public static IReadOnlyList<SkinLine> Lines => lines;

    /// <summary>The line with this id (case-insensitive); null for factory / unknown.</summary>
    public static SkinLine? Get(string? id) => id != null && byId.TryGetValue(id.Trim().ToLowerInvariant(), out var l) ? l : null;

    /// <summary>The lines as Progress registers them (order within each tier = catalogue order).</summary>
    public static IEnumerable<UnlockLine> UnlockLines()
    {
        var count = new Dictionary<Edition, int>();
        foreach (var l in lines)
        {
            int order = count.TryGetValue(l.Edition, out var c) ? c : 0;
            count[l.Edition] = order + 1;
            yield return l.ToUnlockLine(order);
        }
    }

    /// <summary>
    /// Resolves a stored choice to what may be worn, clamped by <paramref name="unlocked"/> (an item-id predicate, normally
    /// <see cref="Progress.IsUnlocked"/>): a locked line → null (factory; the stored choice is kept); a locked level → the
    /// highest unlocked level below it (levels unlock in order); a locked variant → the base palette. Never throws.
    /// </summary>
    public static SkinState? Resolve(SkinChoice choice, Func<string, bool> unlocked)
    {
        if (Get(choice.Line) is not { } l || !unlocked(ItemIds.Base(l.Id))) return null;
        int want = choice.Level <= 0 ? l.MaxLevel : Math.Min(choice.Level, l.MaxLevel);
        int level = 1;
        while (level < want && unlocked(ItemIds.Level(l.Id, level + 1))) level++;
        var v = l.Variant(choice.Variant);
        if (v != null && !unlocked(ItemIds.Variant(l.Id, v.Id))) v = null;
        return new SkinState(l, level, v);
    }
}
