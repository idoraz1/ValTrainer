using System.Globalization;

namespace ValTrainer.Core;

/// <summary>A sensitivity typed by the player ("0.35", "0,35", " .4 "): VALORANT's range, rounded to 3 decimals.</summary>
public static class SensInput
{
    public const float Min = 0.001f, Max = 10f;

    public static bool TryParse(string? text, out float sens)
    {
        sens = 0;
        var t = (text ?? "").Trim().Replace(',', '.');
        if (t.Length == 0 || !float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !float.IsFinite(v)) return false;
        if (v < Min || v > Max) return false;
        sens = MathF.Round(v * 1000f) / 1000f;
        return sens >= Min;
    }

    public static string Hint => $"Type a number between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}, e.g. 0.35";
}
