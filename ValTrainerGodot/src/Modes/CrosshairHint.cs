using Godot;
using ValTrainer.Maps;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// A one-line visibility hint for the player's imported VALORANT crosshair (Lock-In setup check, Settings → Crosshair),
/// built on the Crosshair Finder's colour maths (<see cref="XfVisibility"/>). It only warns about the clear cases: a fill
/// close to the enemy highlight, or a fill that fades on common map surfaces with no (or an almost invisible) outline.
/// </summary>
public static class CrosshairHint
{
    // ---- Thresholds: judgement calls, kept here in one place. Tuned on VALORANT's 8 colour presets over the 6 map
    // palettes (+ shade / sun variants and the enemy body) that the Crosshair Finder uses. ----

    /// <summary>Fill-vs-surface <see cref="XfVisibility.LineContrast"/> below this: the thin line fades on that surface
    /// (white on Icebox snow ≈ 0–10, cyan on the sky ≈ 12; green never drops below ≈ 35).</summary>
    public const float FadeContrast = 20f;
    /// <summary>Weighted share of those surfaces where the fill fades before we call it "blends in": white without an
    /// outline ≈ 18 %, cyan ≈ 4 %, every other preset 0 %.</summary>
    public const float FadeShare = 0.10f;
    /// <summary>CIELAB ΔE between the fill and the enemy highlight below this: the crosshair melts into an enemy's outline
    /// right when it's on target. Same 45 the Crosshair Finder's score penalises (yellow on yellow ≈ 13, red on red ≈ 19,
    /// pink on purple ≈ 26, yellow-green on yellow ≈ 55).</summary>
    public const float EnemyDeltaE = 45f;
    /// <summary>Outline opacity below this hardly separates the line from the background (VALORANT's default is 0.5).</summary>
    public const float FaintOutline = 0.2f;

    public enum Verdict { Ok, NearEnemy, NoOutlineFades, FaintOutlineFades }

    /// <summary><see cref="Short"/> follows a "Crosshair" label; <see cref="Long"/> is a full sentence or two.</summary>
    public readonly record struct Result(Verdict Verdict, string Short, string Long)
    {
        public bool Ok => Verdict == Verdict.Ok;
    }

    /// <summary>The hint for the imported VALORANT crosshair against VALORANT's own enemy highlight (null when nothing
    /// was imported: there is no crosshair of the player's to judge). A split profile's own ADS crosshair is judged
    /// too: the first one with a problem is reported (the primary first).</summary>
    public static Result? ForImported()
    {
        var v = Main.I.Valorant;
        if (!v.Found) return null;
        int e = Math.Clamp(v.EnemyHighlight, 0, UiTheme.EnemyColors.Length - 1);
        string enemyName = UiTheme.EnemyColorNames[e].Split(" (")[0].ToLowerInvariant();
        var primary = Check(v.Crosshair.Primary, UiTheme.EnemyColors[e], enemyName);
        if (!primary.Ok || !v.Crosshair.AdsIsOwn) return primary;
        var ads = Check(v.Crosshair.Ads, UiTheme.EnemyColors[e], enemyName, ads: true);
        return ads.Ok ? primary : ads;
    }

    /// <param name="ads">Judging the profile's own ADS crosshair (the wording says so).</param>
    public static Result Check(CrosshairStyle s, Color enemy, string enemyName, bool ads = false)
    {
        var fill = new Color(s.Color, 1f);
        bool outlineOn = s.HasOutline && s.OutlineThickness > 0;
        bool faint = outlineOn && s.OutlineOpacity < FaintOutline;
        string who = ads ? "Your VALORANT ADS crosshair" : "Your VALORANT crosshair", pre = ads ? "ADS: " : "";

        const string Finder = " The Crosshair Finder (Coach) can suggest one that stands out.";
        if (XfVisibility.DeltaE(fill, enemy) < EnemyDeltaE)
            return new(Verdict.NearEnemy, $"{pre}colour is close to the {enemyName} enemy highlight",
                $"{who}'s colour is close to the {enemyName} enemy highlight, so it can melt into an enemy's outline right when you're on target." + Finder);

        if (outlineOn && !faint) return Ok(who);
        var (share, maps) = Fades(fill);
        if (share < FadeShare) return Ok(who);
        string where = maps.Count switch { 0 => "some map surfaces", 1 => $"parts of {maps[0]}", _ => $"parts of {maps[0]} and {maps[1]}" };
        return faint
            ? new(Verdict.FaintOutlineFades, $"{pre}faint outline, fades on {where}",
                $"{who}'s outline is almost see-through and its colour fades on {where}. A solid outline or another colour helps." + Finder)
            : new(Verdict.NoOutlineFades, $"{pre}no outline, fades on {where}",
                $"{who} has no outline and its colour fades on {where}. An outline or another colour helps." + Finder);
    }

    static Result Ok(string who) => new(Verdict.Ok, "stands out on common map colours",
        $"{who} stands out on common map colours and isn't close to the enemy highlight.");

    /// <summary>Weighted share of map surfaces (and the enemy body) where the fill alone fades, and the maps where it
    /// fades worst (display names, up to two).</summary>
    static (float Share, List<string> Maps) Fades(Color fill)
    {
        float w = 0, faded = 0;
        var worst = new Dictionary<string, float>();
        foreach (var b in XfVisibility.Backgrounds(Colors.Black))
        {
            if (b.Name == "enemy highlight") continue; // judged by the ΔE rule above
            w += b.Weight;
            float c = XfVisibility.LineContrast(fill, b.Color);
            if (c >= FadeContrast) continue;
            faded += b.Weight;
            if (b.Name.StartsWith("enemy")) continue;
            string key = b.Name.Split(' ')[0];
            worst[key] = worst.TryGetValue(key, out var prev) ? Mathf.Min(prev, c) : c;
        }
        var maps = worst.OrderBy(kv => kv.Value).Take(2).Select(kv => MapSpots.ByKey(kv.Key).Map).ToList();
        return (w > 0 ? faded / w : 0, maps);
    }
}
