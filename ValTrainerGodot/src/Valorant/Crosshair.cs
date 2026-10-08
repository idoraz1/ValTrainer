using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using ValTrainer.UI;
using Color = Godot.Color;

namespace ValTrainer.Valorant;

/// <summary>One line group (inner or outer). Field defaults are VALORANT's factory inner lines; the outer group's
/// factory values are set by <see cref="CrosshairStyle"/>.</summary>
public sealed class CrosshairLines
{
    public bool Show = true;
    public float Thickness = 2, Length = 6, LengthVertical = 6, Offset = 3, Opacity = 0.8f;
    /// <summary>"Separate vertical length" (the chain icon off): <see cref="LengthVertical"/> is used for the vertical arms.</summary>
    public bool AllowVertScaling;
    public bool ShowMovementError;
    public float MovementErrorScale = 1;
    public bool ShowShootingError = true;
    public float FiringErrorScale = 1;

    public CrosshairLines Clone() => (CrosshairLines)MemberwiseClone();
}

/// <summary>One crosshair (VALORANT's Primary or ADS tab). <c>new CrosshairStyle()</c> is VALORANT's factory crosshair.</summary>
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
    public CrosshairLines Outer = new()
    {
        Thickness = 2, Length = 2, LengthVertical = 2, Offset = 10, Opacity = 0.35f, ShowMovementError = true, ShowShootingError = true,
    };

    public CrosshairStyle Clone()
    {
        var c = (CrosshairStyle)MemberwiseClone();
        c.Inner = Inner.Clone();
        c.Outer = Outer.Clone();
        return c;
    }
}

/// <summary>The sniper scope centre dot VALORANT draws (see <see cref="CrosshairSettings.SniperDotFor"/>).</summary>
public readonly record struct SniperDotStyle(bool Show, Color Color, float Size, float Opacity);

/// <summary>A VALORANT crosshair profile: Primary, ADS and sniper dot plus the General flags. <c>new()</c> is VALORANT's
/// factory profile (the code "0").</summary>
public sealed class CrosshairSettings
{
    public string Name = "VALORANT default";
    public CrosshairStyle Primary = new();
    /// <summary>The ADS tab as stored. Only drawn when <see cref="AdsIsOwn"/>; use <see cref="StyleFor"/>.</summary>
    public CrosshairStyle Ads = new();
    /// <summary>ADS ▸ "Copy Primary Crosshair" (JSON bUsePrimaryCrosshairForADS, code global "p"; default on).</summary>
    public bool UsePrimaryForAds = true;
    /// <summary>General ▸ "Use Advanced Options" (JSON bUseAdvancedOptions, code global "s"). Off: VALORANT ignores the
    /// ADS and Sniper tabs (ADS uses the primary crosshair, the scope shows the default red dot).</summary>
    public bool UseAdvancedOptions;
    public bool ScaleToResolution;
    /// <summary>The Sniper tab as stored. Only drawn when <see cref="UseAdvancedOptions"/>; use <see cref="SniperDotFor"/>.</summary>
    public Color SniperDotColor = Color.Color8(255, 0, 0, 255);
    public bool SniperDot = true;
    public float SniperDotSize = 1, SniperDotOpacity = 0.75f;
    /// <summary>Everything else a crosshair code holds (fade, spectated, colour provenance, unknown keys / sections), so
    /// <see cref="CrosshairCode.Encode"/> reproduces the profile without loss.</summary>
    public CrosshairCodeExtras Extras = new();

    /// <summary>True when VALORANT draws the ADS tab while aiming down sights (advanced options on, copy primary off).</summary>
    public bool AdsIsOwn => UseAdvancedOptions && !UsePrimaryForAds;

    /// <summary>True when the scope shows the player's own Sniper tab dot (advanced options on).</summary>
    public bool SniperIsOwn => UseAdvancedOptions;

    /// <summary>The crosshair VALORANT draws in this mode: ADS uses the ADS tab only when <see cref="AdsIsOwn"/>, else the
    /// primary (also for Sniper / Hidden, which draw no crosshair style).</summary>
    public CrosshairStyle StyleFor(CrosshairView.Mode m) => m == CrosshairView.Mode.Ads && AdsIsOwn ? Ads : Primary;

    /// <summary>The code section ("P" or "A") of <see cref="StyleFor"/>: firing-error offset override etc.</summary>
    public CrosshairCodeSection SectionFor(CrosshairView.Mode m) => m == CrosshairView.Mode.Ads && AdsIsOwn ? Extras.Ads : Extras.Primary;

    /// <summary>The scope centre dot VALORANT draws: the Sniper tab with advanced options on, else the default red dot.</summary>
    public SniperDotStyle SniperDotFor() => UseAdvancedOptions
        ? new SniperDotStyle(SniperDot, SniperDotColor, SniperDotSize, SniperDotOpacity)
        : new SniperDotStyle(true, CrosshairCode.Presets[7], 1f, 0.75f);

    /// <summary>Deep copy (styles, extras): change one tab without touching the original.</summary>
    public CrosshairSettings Clone()
    {
        var c = (CrosshairSettings)MemberwiseClone();
        c.Primary = Primary.Clone();
        c.Ads = Ads.Clone();
        c.Extras = Extras.Clone();
        return c;
    }

    /// <summary>Parses Valorant's SavedCrosshairProfileData JSON (the active profile); null if it isn't readable.
    /// <c>focusMode</c> (console only, all zeros on PC) is never read.</summary>
    public static CrosshairSettings? FromProfileJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var profiles = root.GetProperty("profiles");
            if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() == 0) return null;
            int idx = Math.Clamp(Int(root, "currentProfile", 0), 0, profiles.GetArrayLength() - 1);
            var p = profiles[idx];
            if (p.ValueKind != JsonValueKind.Object) return null;

            var c = new CrosshairSettings
            {
                Name = p.TryGetProperty("profileName", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { } nm
                       && !string.IsNullOrWhiteSpace(nm) ? nm : $"Profile {idx + 1}",
                UsePrimaryForAds = Bool(p, "bUsePrimaryCrosshairForADS", true),
                UseAdvancedOptions = Bool(p, "bUseAdvancedOptions", false),
                ScaleToResolution = Bool(p, "bScaleToResolution", false),
            };
            c.Extras.AdvancedOptions = c.UseAdvancedOptions;
            c.Extras.OverrideAllPrimary = Bool(p, "bUseCustomCrosshairOnAllPrimary", false);
            if (p.TryGetProperty("primary", out var prim) && prim.ValueKind == JsonValueKind.Object)
            {
                c.Primary = Style(prim, c.Extras.Primary);
                c.Extras.Primary.Fade = Bool(prim, "bFadeCrosshairWithFiringError", true);
                c.Extras.Primary.ShowSpectated = Bool(prim, "bShowSpectatedPlayerCrosshair", true);
            }
            if (p.TryGetProperty("aDS", out var ads) && ads.ValueKind == JsonValueKind.Object)
                c.Ads = Style(ads, c.Extras.Ads); // the aDS fade / spectated flags are always false and unused: recent exports omit them
            if (p.TryGetProperty("sniper", out var sn) && sn.ValueKind == JsonValueKind.Object)
            {
                c.SniperDot = Bool(sn, "bDisplayCenterDot", true);
                c.SniperDotSize = Float(sn, "centerDotSize", 1);
                c.SniperDotOpacity = Float(sn, "centerDotOpacity", 0.75f);
                c.Extras.SniperCustom = Bool(sn, "bUseCustomCenterDotColor", false);
                c.SniperDotColor = c.Extras.SniperCustom
                    ? Col(sn, "centerDotColorCustom", Colors.White)
                    : Col(sn, "centerDotColor", c.SniperDotColor);
            }
            return c;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Older Valorant builds (and accounts whose profiles live only in Riot's cloud) keep the primary crosshair as
    /// individual keys. Only non-default values are written, so every missing key means VALORANT's factory value.</summary>
    public static CrosshairSettings FromLegacyKeys(IReadOnlyDictionary<string, string> s)
    {
        var c = CrosshairCode.DefaultSettings();
        var st = c.Primary;
        bool useCustom = GetB(s, "CrosshairUseCustomColor", false);
        if (s.TryGetValue("CrosshairColor", out var col) && ParseUeColor(col) is { } cc) st.Color = cc;
        if (useCustom && s.TryGetValue("CrosshairColorCustom", out var cus) && ParseUeColor(cus) is { } cu) st.Color = cu;
        c.Extras.Primary.CustomViaB = c.Extras.Primary.CustomViaC8 = useCustom;
        st.HasOutline = GetB(s, "CrosshairHasOutline", st.HasOutline);
        st.OutlineThickness = GetF(s, "CrosshairOutlineThickness", st.OutlineThickness);
        st.OutlineOpacity = GetF(s, "CrosshairOutlineOpacity", st.OutlineOpacity);
        st.CenterDot = GetB(s, "CrosshairDisplayCenterDot", GetB(s, "CrosshairHasCenterDot", st.CenterDot));
        st.CenterDotSize = GetF(s, "CrosshairCenterDotSize", st.CenterDotSize);
        st.CenterDotOpacity = GetF(s, "CrosshairCenterDotOpacity", st.CenterDotOpacity);
        c.Extras.Primary.Fade = GetB(s, "FadeCrosshairWithFiringError", true);
        LegacyLines(s, "CrosshairInnerLines", st.Inner);
        LegacyLines(s, "CrosshairOuterLines", st.Outer);
        c.Name = s.TryGetValue("CrosshairProfileName", out var name) && name.Trim().Trim('"') is { Length: > 0 } nm ? nm : "Imported (legacy keys)";
        return c;
    }

    static void LegacyLines(IReadOnlyDictionary<string, string> s, string p, CrosshairLines l)
    {
        l.Show = GetB(s, p + "ShowLines", l.Show);
        l.Thickness = GetF(s, p + "LineThickness", l.Thickness);
        l.Length = GetF(s, p + "LineLength", l.Length);
        l.LengthVertical = GetF(s, p + "LineLengthVertical", l.LengthVertical);
        l.AllowVertScaling = GetB(s, p + "AllowVertScaling", l.AllowVertScaling);
        l.Offset = GetF(s, p + "LineOffset", l.Offset);
        l.Opacity = GetF(s, p + "Opacity", l.Opacity);
        l.ShowMovementError = GetB(s, p + "ShowMovementError", l.ShowMovementError);
        l.MovementErrorScale = GetF(s, p + "MovementErrorScale", l.MovementErrorScale);
        l.ShowShootingError = GetB(s, p + "ShowShootingError", l.ShowShootingError);
        l.FiringErrorScale = GetF(s, p + "FiringErrorScale", l.FiringErrorScale);
    }

    static CrosshairStyle Style(JsonElement e, CrosshairCodeSection x)
    {
        bool custom = Bool(e, "bUseCustomColor", false);
        var colorCustom = Col(e, "colorCustom", Colors.White);
        var s = new CrosshairStyle
        {
            Color = custom ? colorCustom : Col(e, "color", Colors.White),
            HasOutline = Bool(e, "bHasOutline", true),
            OutlineThickness = Float(e, "outlineThickness", 1),
            OutlineOpacity = Float(e, "outlineOpacity", 0.5f),
            OutlineColor = Col(e, "outlineColor", Colors.Black),
            CenterDot = Bool(e, "bDisplayCenterDot", false),
            CenterDotSize = Float(e, "centerDotSize", 2),
            CenterDotOpacity = Float(e, "centerDotOpacity", 1),
            Hide = Bool(e, "bHideCrosshair", false),
        };
        // Colour provenance for the code: the game's exports write both "c;8" and "b;1" for a custom colour, and keep a
        // stale custom hex ("u") while a preset is selected.
        x.CustomViaB = x.CustomViaC8 = custom;
        string staleHex = $"{colorCustom.R8:X2}{colorCustom.G8:X2}{colorCustom.B8:X2}{colorCustom.A8:X2}";
        x.StaleHex = !custom && staleHex != "FFFFFFFF" ? staleHex : null;
        x.OverrideFiringOffset = Bool(e, "bFixMinErrorAcrossWeapons", false);
        var def = new CrosshairStyle();
        if (e.TryGetProperty("innerLines", out var il) && il.ValueKind == JsonValueKind.Object) s.Inner = Lines(il, def.Inner);
        if (e.TryGetProperty("outerLines", out var ol) && ol.ValueKind == JsonValueKind.Object) s.Outer = Lines(ol, def.Outer);
        return s;
    }

    static CrosshairLines Lines(JsonElement e, CrosshairLines d) => new()
    {
        Show = Bool(e, "bShowLines", d.Show),
        Thickness = Float(e, "lineThickness", d.Thickness),
        Length = Float(e, "lineLength", d.Length),
        LengthVertical = Float(e, "lineLengthVertical", d.LengthVertical),
        Offset = Float(e, "lineOffset", d.Offset),
        Opacity = Float(e, "opacity", d.Opacity),
        AllowVertScaling = Bool(e, "bAllowVertScaling", d.AllowVertScaling),
        ShowMovementError = Bool(e, "bShowMovementError", d.ShowMovementError),
        MovementErrorScale = Float(e, "movementErrorScale", d.MovementErrorScale),
        ShowShootingError = Bool(e, "bShowShootingError", d.ShowShootingError),
        FiringErrorScale = Float(e, "firingErrorScale", d.FiringErrorScale),
    };

    static float Float(JsonElement e, string name, float def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && float.IsFinite(v.GetSingle()) ? v.GetSingle() : def;

    static int Int(JsonElement e, string name, int def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.GetDouble() is var d && double.IsFinite(d)
            ? (int)Math.Clamp(d, int.MinValue, int.MaxValue) : def;

    static bool Bool(JsonElement e, string name, bool def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;

    static Color Col(JsonElement e, string name, Color def) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? Color.Color8(B8(v, "r"), B8(v, "g"), B8(v, "b"), B8(v, "a"))
            : def;

    static byte B8(JsonElement e, string name) => (byte)Math.Clamp(Int(e, name, 255), 0, 255);

    static float GetF(IReadOnlyDictionary<string, string> s, string k, float def) =>
        s.TryGetValue(k, out var v) && float.TryParse(v.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : def;

    static bool GetB(IReadOnlyDictionary<string, string> s, string k, bool def) =>
        s.TryGetValue(k, out var v) ? v.Trim().Equals("True", StringComparison.OrdinalIgnoreCase) : def;

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
