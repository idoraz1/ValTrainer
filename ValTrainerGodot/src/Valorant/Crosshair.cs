using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Color = Godot.Color;

namespace ValTrainer.Valorant;

public sealed class CrosshairLines
{
    public bool Show = true;
    public float Thickness = 2, Length = 4, LengthVertical = 4, Offset = 3, Opacity = 0.8f;
    public bool AllowVertScaling;
    public bool ShowMovementError;
    public float MovementErrorScale = 1;
    public bool ShowShootingError;
    public float FiringErrorScale = 1;
}

public sealed class CrosshairStyle
{
    public Color Color = Colors.White;
    public bool HasOutline = true;
    public float OutlineThickness = 1, OutlineOpacity = 0.5f;
    public Color OutlineColor = Colors.Black;
    public bool CenterDot;
    public float CenterDotSize = 2, CenterDotOpacity = 1;
    public bool Hide;
    public CrosshairLines Inner = new();
    public CrosshairLines Outer = new() { Thickness = 2, Length = 2, LengthVertical = 2, Offset = 10, Opacity = 0.35f };
}

public sealed class CrosshairSettings
{
    public string Name = "Valorant default";
    public CrosshairStyle Primary = new();
    public CrosshairStyle Ads = new();
    public bool UsePrimaryForAds = true;
    public bool ScaleToResolution;
    public Color SniperDotColor = Color.Color8(255, 0, 0, 255);
    public bool SniperDot = true;
    public float SniperDotSize = 1, SniperDotOpacity = 0.75f;

    /// <summary>Parses Valorant's SavedCrosshairProfileData JSON (the active profile).</summary>
    public static CrosshairSettings? FromProfileJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var profiles = root.GetProperty("profiles");
            if (profiles.GetArrayLength() == 0) return null;
            int idx = Math.Clamp(Int(root, "currentProfile", 0), 0, profiles.GetArrayLength() - 1);
            var p = profiles[idx];

            var c = new CrosshairSettings
            {
                Name = p.TryGetProperty("profileName", out var n) ? n.GetString() ?? "Profile" : "Profile",
                UsePrimaryForAds = Bool(p, "bUsePrimaryCrosshairForADS", true),
                ScaleToResolution = Bool(p, "bScaleToResolution", false),
            };
            if (p.TryGetProperty("primary", out var prim)) c.Primary = Style(prim);
            if (p.TryGetProperty("aDS", out var ads)) c.Ads = Style(ads);
            if (p.TryGetProperty("sniper", out var sn))
            {
                c.SniperDot = Bool(sn, "bDisplayCenterDot", true);
                c.SniperDotSize = Float(sn, "centerDotSize", 1);
                c.SniperDotOpacity = Float(sn, "centerDotOpacity", 0.75f);
                c.SniperDotColor = Bool(sn, "bUseCustomCenterDotColor", false)
                    ? Col(sn, "centerDotColorCustom", c.SniperDotColor)
                    : Col(sn, "centerDotColor", c.SniperDotColor);
            }
            return c;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Older Valorant builds stored the crosshair as individual keys.</summary>
    public static CrosshairSettings FromLegacyKeys(IReadOnlyDictionary<string, string> s)
    {
        var c = new CrosshairSettings();
        var st = c.Primary;
        if (s.TryGetValue("CrosshairColor", out var col) && ParseUeColor(col) is { } cc) st.Color = cc;
        if (s.TryGetValue("CrosshairColorCustom", out var cus) && ParseUeColor(cus) is { } cu
            && s.TryGetValue("CrosshairUseCustomColor", out var uc) && uc.Equals("True", StringComparison.OrdinalIgnoreCase))
            st.Color = cu;
        st.HasOutline = GetB(s, "CrosshairHasOutline", st.HasOutline);
        st.OutlineThickness = GetF(s, "CrosshairOutlineThickness", st.OutlineThickness);
        st.OutlineOpacity = GetF(s, "CrosshairOutlineOpacity", st.OutlineOpacity);
        st.CenterDot = GetB(s, "CrosshairHasCenterDot", st.CenterDot);
        st.CenterDotSize = GetF(s, "CrosshairCenterDotSize", st.CenterDotSize);
        st.CenterDotOpacity = GetF(s, "CrosshairCenterDotOpacity", st.CenterDotOpacity);
        st.Inner.Show = GetB(s, "CrosshairInnerLinesShowLines", st.Inner.Show);
        st.Inner.Thickness = GetF(s, "CrosshairInnerLinesLineThickness", st.Inner.Thickness);
        st.Inner.Length = GetF(s, "CrosshairInnerLinesLineLength", st.Inner.Length);
        st.Inner.LengthVertical = GetF(s, "CrosshairInnerLinesLineLengthVertical", st.Inner.Length);
        st.Inner.Offset = GetF(s, "CrosshairInnerLinesLineOffset", st.Inner.Offset);
        st.Inner.Opacity = GetF(s, "CrosshairInnerLinesOpacity", st.Inner.Opacity);
        st.Outer.Show = GetB(s, "CrosshairOuterLinesShowLines", st.Outer.Show);
        st.Outer.Thickness = GetF(s, "CrosshairOuterLinesLineThickness", st.Outer.Thickness);
        st.Outer.Length = GetF(s, "CrosshairOuterLinesLineLength", st.Outer.Length);
        st.Outer.LengthVertical = GetF(s, "CrosshairOuterLinesLineLengthVertical", st.Outer.Length);
        st.Outer.Offset = GetF(s, "CrosshairOuterLinesLineOffset", st.Outer.Offset);
        st.Outer.Opacity = GetF(s, "CrosshairOuterLinesOpacity", st.Outer.Opacity);
        c.Name = "Imported (legacy keys)";
        return c;
    }

    static CrosshairStyle Style(JsonElement e)
    {
        var s = new CrosshairStyle
        {
            Color = Bool(e, "bUseCustomColor", false) ? Col(e, "colorCustom", Colors.White) : Col(e, "color", Colors.White),
            HasOutline = Bool(e, "bHasOutline", true),
            OutlineThickness = Float(e, "outlineThickness", 1),
            OutlineOpacity = Float(e, "outlineOpacity", 0.5f),
            OutlineColor = Col(e, "outlineColor", Colors.Black),
            CenterDot = Bool(e, "bDisplayCenterDot", false),
            CenterDotSize = Float(e, "centerDotSize", 2),
            CenterDotOpacity = Float(e, "centerDotOpacity", 1),
            Hide = Bool(e, "bHideCrosshair", false),
        };
        if (e.TryGetProperty("innerLines", out var il)) s.Inner = Lines(il);
        if (e.TryGetProperty("outerLines", out var ol)) s.Outer = Lines(ol);
        return s;
    }

    static CrosshairLines Lines(JsonElement e) => new()
    {
        Show = Bool(e, "bShowLines", true),
        Thickness = Float(e, "lineThickness", 2),
        Length = Float(e, "lineLength", 4),
        LengthVertical = Float(e, "lineLengthVertical", 4),
        Offset = Float(e, "lineOffset", 3),
        Opacity = Float(e, "opacity", 0.8f),
        AllowVertScaling = Bool(e, "bAllowVertScaling", false),
        ShowMovementError = Bool(e, "bShowMovementError", false),
        MovementErrorScale = Float(e, "movementErrorScale", 1),
        ShowShootingError = Bool(e, "bShowShootingError", false),
        FiringErrorScale = Float(e, "firingErrorScale", 1),
    };

    static float Float(JsonElement e, string name, float def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : def;

    static int Int(JsonElement e, string name, int def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : def;

    static bool Bool(JsonElement e, string name, bool def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;

    static Color Col(JsonElement e, string name, Color def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? Color.Color8((byte)Int(v, "r", 255), (byte)Int(v, "g", 255), (byte)Int(v, "b", 255), (byte)Int(v, "a", 255))
            : def;

    static float GetF(IReadOnlyDictionary<string, string> s, string k, float def) =>
        s.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;

    static bool GetB(IReadOnlyDictionary<string, string> s, string k, bool def) =>
        s.TryGetValue(k, out var v) ? v.Equals("True", StringComparison.OrdinalIgnoreCase) : def;

    /// <summary>Parses Unreal's "(R=0,G=255,B=255,A=255)" color format.</summary>
    public static Color? ParseUeColor(string v)
    {
        // [0-9] (not \d, which also matches non-ASCII digits) and TryParse: a corrupt value can't throw.
        var m = Regex.Matches(v, @"([RGBA])=([0-9]{1,9})", RegexOptions.CultureInvariant);
        if (m.Count < 3) return null;
        int Get(string ch, int d) => m.FirstOrDefault(x => x.Groups[1].Value == ch) is { } x
            && int.TryParse(x.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, 255) : d;
        return Color.Color8((byte)Get("R", 255), (byte)Get("G", 255), (byte)Get("B", 255), (byte)Get("A", 255));
    }
}
