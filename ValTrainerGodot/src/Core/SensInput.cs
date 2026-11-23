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

    public static string Hint => L.F($"Type a number between {Min.ToString(CultureInfo.InvariantCulture)} and {Max.ToString(CultureInfo.InvariantCulture)}, e.g. 0.35");
}

/// <summary>
/// Mouse DPI and polling rate as the player sets them in ValTrainer (the ESC menu and Settings → General). Neither can be
/// read from the mouse: DPI is used for eDPI / cm/360, and both are saved with every run so the coach's sens history can
/// compare setups (Discord suggestion).
/// </summary>
public static class MouseSetup
{
    public const int MinDpi = 50, MaxDpi = 32000;
    public static readonly int[] DpiPresets = { 400, 800, 1000, 1200, 1600, 2000, 3200 };
    /// <summary>Polling rate choices in Hz; 0 = not set.</summary>
    public static readonly int[] PollingPresets = { 0, 125, 250, 500, 1000, 2000, 4000, 8000 };

    /// <summary>A typed DPI ("800", " 1600 "): a whole number in range.</summary>
    public static bool TryParseDpi(string? text, out int dpi)
    {
        dpi = 0;
        var t = (text ?? "").Trim();
        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < MinDpi || v > MaxDpi) return false;
        dpi = v;
        return true;
    }

    /// <summary>The next preset above (d &gt; 0) or below a DPI (which may be a typed value between presets); wraps around.</summary>
    public static int StepDpi(int dpi, int d) => Step(DpiPresets, dpi, d);

    /// <summary>The next polling preset, through "not set" (wraps); a value that isn't a preset steps to its neighbour.</summary>
    public static int StepPolling(int hz, int d) => Step(PollingPresets, hz, d);

    static int Step(int[] p, int v, int d)
    {
        int i = Array.IndexOf(p, v);
        if (i >= 0) return p[((i + d) % p.Length + p.Length) % p.Length];
        if (d > 0) return p.FirstOrDefault(x => x > v, p[0]);
        var below = p.Where(x => x < v).ToArray();
        return below.Length > 0 ? below[^1] : p[^1];
    }

    /// <summary>"1000 Hz", or "not set" (translated).</summary>
    public static string PollingText(int hz) => hz > 0 ? hz.ToString(CultureInfo.InvariantCulture) + " Hz" : L.T("not set");
}
