using System.Globalization;
using System.Text;
using Godot;
using ValTrainer.Core;
using Color = Godot.Color;

namespace ValTrainer.Valorant;

/// <summary>
/// Settings of a VALORANT crosshair code beyond the drawn styles (<see cref="CrosshairSettings.Extras"/>): General flags,
/// colour provenance and anything unknown, so a decoded code re-encodes without loss.
/// </summary>
public sealed class CrosshairCodeExtras
{
    /// <summary>Global "s" as seen by the code alone: kept equal to <see cref="CrosshairSettings.UseAdvancedOptions"/> by
    /// the decoders. The encoder treats the profile as advanced when either flag is set.</summary>
    public bool AdvancedOptions;
    /// <summary>Global "c":Override All Primary Crosshairs With My Primary Crosshair (shotguns; ValTrainer has none).</summary>
    public bool OverrideAllPrimary;
    public CrosshairCodeSection Primary = new(), Ads = new();
    /// <summary>Sniper "b" / "c;8": custom centre-dot colour even if it equals a preset.</summary>
    public bool SniperCustom;
    /// <summary>The decoded code had an "A" / "S" section the game wouldn't export with its flags (A without s;1 + p;0,
    /// S without s;1): VALORANT ignores it, the encoder re-emits it so the code round-trips.</summary>
    public bool KeepAdsSection, KeepSniperSection;
    /// <summary>Keys ValTrainer doesn't know, per section ("" global, "P", "A", "S"), re-emitted at the end of it.</summary>
    public List<(string Section, string Key, string Value)> UnknownKeys = new();
    /// <summary>Sections with a marker ValTrainer doesn't know (a future tab, the community NAME;"…" extension), kept
    /// verbatim and re-emitted after the known sections.</summary>
    public List<(string Marker, List<string> Tokens)> UnknownSections = new();

    public CrosshairCodeExtras Clone()
    {
        var c = (CrosshairCodeExtras)MemberwiseClone();
        c.Primary = Primary.Clone();
        c.Ads = Ads.Clone();
        c.UnknownKeys = new(UnknownKeys);
        c.UnknownSections = UnknownSections.Select(s => (s.Marker, new List<string>(s.Tokens))).ToList();
        return c;
    }
}

public sealed class CrosshairCodeSection
{
    /// <summary>"f": Fade Crosshair With Firing Error (default on; a General setting stored with the primary).</summary>
    public bool Fade = true;
    /// <summary>"s": Show Spectated Player's Crosshair (default on).</summary>
    public bool ShowSpectated = true;
    /// <summary>"m": Override Firing Error Offset With Crosshair Offset (default off).</summary>
    public bool OverrideFiringOffset;
    /// <summary>The colour came from "u" (c;8 and/or b;1) even if it equals a preset.</summary>
    public bool CustomViaC8, CustomViaB;
    /// <summary>A leftover "u" kept by the game while a preset colour is selected (exported as-is).</summary>
    public string? StaleHex;

    public CrosshairCodeSection Clone() => (CrosshairCodeSection)MemberwiseClone();
}

/// <summary>
/// VALORANT crosshair profile codes (Settings → Crosshair → Import Profile Code), e.g. "0;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0".
/// <para>Grammar: version token "0", then key;value pairs. Bare "P" / "A" / "S" switch to the primary / ADS / sniper
/// section; pairs before the first marker are global ("p" copy primary for ADS, "c" override all primary, "s" advanced
/// options). Any other bare upper-case token is an unknown section (kept verbatim). Keys equal to their default are
/// omitted; numbers have at most 3 decimals ("0.374", "1"); colours are "RRGGBBAA". The game exports "A" only with
/// s;1 and p;0, and "S" only with s;1. Key table, defaults and order follow the @valapi/crosshair library and real
/// in-game exports (genesy/crosshair-codes samples, prosettings.net pro codes).</para>
/// ValTrainer only reads and writes these strings: VALORANT's files are never touched.
/// </summary>
public static class CrosshairCode
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>VALORANT's crosshair colour presets (code "c" 0–7; 8 = custom "u").</summary>
    public static readonly Color[] Presets =
    {
        Color.Color8(255, 255, 255), Color.Color8(0, 255, 0), Color.Color8(127, 255, 0), Color.Color8(223, 255, 0),
        Color.Color8(255, 255, 0), Color.Color8(0, 255, 255), Color.Color8(255, 0, 255), Color.Color8(255, 0, 0),
    };
    public static readonly string[] PresetNames = { "White", "Green", "Yellow Green", "Green Yellow", "Yellow", "Cyan", "Pink", "Red" };

    /// <summary>Preset index of a colour (exact RGB, opaque), or -1.</summary>
    public static int PresetIndex(Color c)
    {
        if (c.A8 != 255) return -1;
        for (int i = 0; i < Presets.Length; i++)
            if (Presets[i].R8 == c.R8 && Presets[i].G8 == c.G8 && Presets[i].B8 == c.B8) return i;
        return -1;
    }

    public static string ColorName(Color c) => PresetIndex(c) is var i and >= 0 ? PresetNames[i] : "#" + Hex(c)[..6];

    // =====================================================================================
    // Defaults (what an omitted key means)
    // =====================================================================================

    /// <summary>VALORANT's factory crosshair (the code "0"): white, outlined, inner 2×6 at 3 (0.8), outer 2×2 at 10 (0.35).</summary>
    public static CrosshairStyle DefaultStyle() => new()
    {
        Color = Presets[0],
        HasOutline = true, OutlineThickness = 1, OutlineOpacity = 0.5f, OutlineColor = Colors.Black,
        CenterDot = false, CenterDotSize = 2, CenterDotOpacity = 1,
        Inner = new CrosshairLines
        {
            Show = true, Thickness = 2, Length = 6, LengthVertical = 6, AllowVertScaling = false, Offset = 3, Opacity = 0.8f,
            ShowMovementError = false, MovementErrorScale = 1, ShowShootingError = true, FiringErrorScale = 1,
        },
        Outer = new CrosshairLines
        {
            Show = true, Thickness = 2, Length = 2, LengthVertical = 2, AllowVertScaling = false, Offset = 10, Opacity = 0.35f,
            ShowMovementError = true, MovementErrorScale = 1, ShowShootingError = true, FiringErrorScale = 1,
        },
    };

    public static CrosshairSettings DefaultSettings() => new()
    {
        Name = "VALORANT default",
        Primary = DefaultStyle(),
        Ads = DefaultStyle(),
        UsePrimaryForAds = true,
        UseAdvancedOptions = false,
        SniperDot = true, SniperDotColor = Presets[7], SniperDotSize = 1, SniperDotOpacity = 0.75f,
    };

    // =====================================================================================
    // Decode
    // =====================================================================================

    static readonly HashSet<string> GlobalKeys = new() { "p", "c", "s" };
    static readonly HashSet<string> StyleKeys = new[] { "c", "u", "h", "t", "o", "d", "b", "z", "a", "f", "s", "m" }
        .Concat(new[] { "0", "1" }.SelectMany(p => new[] { "b", "t", "l", "v", "g", "o", "a", "m", "s", "f", "e" }.Select(k => p + k)))
        .ToHashSet();
    static readonly HashSet<string> SniperKeys = new() { "d", "b", "c", "t", "s", "o" };

    /// <summary>A bare upper-case token in key position: a section marker ("P", "A", "S" or an unknown one).</summary>
    static bool IsMarker(string t) => t.Length is >= 1 and <= 16 && t.All(ch => ch is >= 'A' and <= 'Z');

    /// <summary>Parses a crosshair code; null if it isn't one. Keys may come in any order, a repeated key wins last,
    /// out-of-range numbers are clamped; unknown keys and sections are kept in <see cref="CrosshairSettings.Extras"/>.</summary>
    public static CrosshairSettings? Decode(string? code) => Decode(code, out _);

    /// <summary><paramref name="extras"/> is the result's <see cref="CrosshairSettings.Extras"/>.</summary>
    public static CrosshairSettings? Decode(string? code, out CrosshairCodeExtras extras)
    {
        extras = new CrosshairCodeExtras();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 2000) return null;
        var tok = code.Trim().Split(';').Select(t => t.Trim()).ToList();
        while (tok.Count > 0 && tok[^1].Length == 0) tok.RemoveAt(tok.Count - 1);
        if (tok.Count == 0 || tok[0] != "0") return null;

        var sec = new Dictionary<string, Dictionary<string, string>>
        {
            [""] = new(), ["P"] = new(), ["A"] = new(), ["S"] = new(),
        };
        string cur = "";
        bool hadA = false, hadS = false;
        List<string>? unknown = null; // tokens of the unknown section being read
        for (int i = 1; i < tok.Count;)
        {
            string t = tok[i];
            if (IsMarker(t))
            {
                if (t is "P" or "A" or "S") { cur = t; unknown = null; hadA |= t == "A"; hadS |= t == "S"; }
                else { unknown = new List<string>(); extras.UnknownSections.Add((t, unknown)); }
                i++;
                continue;
            }
            if (unknown != null)
            {
                // Verbatim: key;value pairs, or a lone token (the community NAME;"…" extension) before a known marker / the end.
                unknown.Add(t);
                i++;
                if (i < tok.Count && !t.StartsWith('"') && tok[i] is not ("P" or "A" or "S")) unknown.Add(tok[i++]);
                continue;
            }
            if (t.Length == 0 || t.Length > 3 || i + 1 >= tok.Count) return null; // not a key;value list
            var known = cur switch { "" => GlobalKeys, "S" => SniperKeys, _ => StyleKeys };
            if (known.Contains(t)) sec[cur][t] = tok[i + 1];
            else extras.UnknownKeys.Add((cur, t, tok[i + 1]));
            i += 2;
        }

        var s = DefaultSettings();
        s.Name = "Crosshair code";
        s.Extras = extras;
        var g = sec[""];
        s.UsePrimaryForAds = Bool(g, "p", true);
        extras.OverrideAllPrimary = Bool(g, "c", false);
        s.UseAdvancedOptions = extras.AdvancedOptions = Bool(g, "s", false);
        extras.KeepAdsSection = hadA && !s.AdsIsOwn;
        extras.KeepSniperSection = hadS && !s.UseAdvancedOptions;
        ReadStyle(sec["P"], s.Primary, extras.Primary);
        ReadStyle(sec["A"], s.Ads, extras.Ads);

        var sn = sec["S"];
        s.SniperDot = Bool(sn, "d", true);
        int sc = Int(sn, "c", 7, 0, 8);
        bool sCustom = Bool(sn, "b", false) || sc == 8;
        extras.SniperCustom = sCustom;
        s.SniperDotColor = sCustom ? HexColor(Str(sn, "t"), Colors.White) : Presets[Math.Min(sc, 7)];
        s.SniperDotSize = Num(sn, "s", 1, 0, 4);
        s.SniperDotOpacity = Num(sn, "o", 0.75f, 0, 1);
        return s;
    }

    static void ReadStyle(Dictionary<string, string> d, CrosshairStyle st, CrosshairCodeSection x)
    {
        int c = Int(d, "c", 0, 0, 8);
        bool b = Bool(d, "b", false);
        x.CustomViaC8 = c == 8;
        x.CustomViaB = b;
        string? hex = Str(d, "u");
        st.Color = b || c == 8 ? HexColor(hex, Colors.White) : Presets[c];
        if (!(b || c == 8) && hex != null && HexColor(hex, Colors.White) is var hc && Hex(hc) != "FFFFFFFF") x.StaleHex = Hex(hc);
        st.HasOutline = Bool(d, "h", true);
        st.OutlineThickness = Num(d, "t", 1, 1, 6);
        st.OutlineOpacity = Num(d, "o", 0.5f, 0, 1);
        st.OutlineColor = Colors.Black;
        st.CenterDot = Bool(d, "d", false);
        st.CenterDotSize = Num(d, "z", 2, 1, 6);
        st.CenterDotOpacity = Num(d, "a", 1, 0, 1);
        x.Fade = Bool(d, "f", true);
        x.ShowSpectated = Bool(d, "s", true);
        x.OverrideFiringOffset = Bool(d, "m", false);
        ReadLines(d, "0", st.Inner, 20, 20);
        ReadLines(d, "1", st.Outer, 10, 40);
    }

    static void ReadLines(Dictionary<string, string> d, string p, CrosshairLines l, float maxLen, float maxOff)
    {
        var def = p == "0" ? DefaultStyle().Inner : DefaultStyle().Outer;
        l.Show = Bool(d, p + "b", def.Show);
        l.Thickness = Num(d, p + "t", def.Thickness, 0, 10);
        l.Length = Num(d, p + "l", def.Length, 0, maxLen);
        l.LengthVertical = Num(d, p + "v", def.LengthVertical, 0, maxLen);
        l.AllowVertScaling = Bool(d, p + "g", def.AllowVertScaling);
        l.Offset = Num(d, p + "o", def.Offset, 0, maxOff);
        l.Opacity = Num(d, p + "a", def.Opacity, 0, 1);
        l.ShowMovementError = Bool(d, p + "m", def.ShowMovementError);
        l.MovementErrorScale = Num(d, p + "s", def.MovementErrorScale, 0, 3);
        l.ShowShootingError = Bool(d, p + "f", def.ShowShootingError);
        l.FiringErrorScale = Num(d, p + "e", def.FiringErrorScale, 0, 3);
    }

    static string? Str(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;

    static bool Bool(Dictionary<string, string> d, string k, bool def) =>
        d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, Inv, out var x) ? x != 0 : def;

    static int Int(Dictionary<string, string> d, string k, int def, int lo, int hi) =>
        d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, Inv, out var x) && !double.IsNaN(x)
            ? (int)Math.Clamp(Math.Round(x), lo, hi) : def;

    static float Num(Dictionary<string, string> d, string k, float def, float lo, float hi) =>
        d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, Inv, out var x) && !double.IsNaN(x)
            ? (float)Math.Clamp(Math.Round(x, 3), lo, hi) : def;

    /// <summary>"RRGGBB" or "RRGGBBAA" (any case, optional '#').</summary>
    static Color HexColor(string? hex, Color def)
    {
        if (hex == null) return def;
        hex = hex.TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, Inv, out var v)) return def;
        if (hex.Length == 6) v = (v << 8) | 0xFF;
        return Color.Color8((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    static string Hex(Color c) => $"{c.R8:X2}{c.G8:X2}{c.B8:X2}{c.A8:X2}";

    // =====================================================================================
    // Encode
    // =====================================================================================

    /// <summary>
    /// Canonical code for <paramref name="s"/> in VALORANT's own key order, omitting every default (like the game's
    /// export). Pixel sizes are rounded to whole pixels where VALORANT only allows integers; values are clamped to the
    /// in-game ranges. Like the game: "s;1" only from <see cref="CrosshairSettings.UseAdvancedOptions"/>, the ADS section
    /// only when advanced options are on and ADS doesn't copy the primary, the sniper section only with advanced
    /// options (plus any A / S section, unknown key or section a decoded code carried, see <see cref="CrosshairCodeExtras"/>).
    /// <paramref name="x"/> defaults to <c>s.Extras</c>.
    /// </summary>
    public static string Encode(CrosshairSettings s, CrosshairCodeExtras? x = null)
    {
        x ??= s.Extras ?? new CrosshairCodeExtras();
        var sb = new StringBuilder("0");
        void Put(string k, string v) => sb.Append(';').Append(k).Append(';').Append(v);
        string Unknown(string section) =>
            string.Concat(x.UnknownKeys.Where(u => u.Section == section).Select(u => ";" + u.Key + ";" + u.Value));

        bool adv = s.UseAdvancedOptions || x.AdvancedOptions;
        if (!s.UsePrimaryForAds) Put("p", "0");
        if (x.OverrideAllPrimary) Put("c", "1");
        if (adv) Put("s", "1");
        sb.Append(Unknown(""));

        var p = Section(s.Primary, x.Primary) + Unknown("P");
        if (p.Length > 0) sb.Append(";P").Append(p);
        if ((adv && !s.UsePrimaryForAds) || x.KeepAdsSection)
        {
            var a = Section(s.Ads, x.Ads) + Unknown("A");
            if (a.Length > 0) sb.Append(";A").Append(a);
        }

        if (adv || x.KeepSniperSection)
        {
            var sn = new StringBuilder();
            void SPut(string k, string v) => sn.Append(';').Append(k).Append(';').Append(v);
            if (!s.SniperDot) SPut("d", "0");
            int si = PresetIndex(s.SniperDotColor);
            bool sCustom = x.SniperCustom || si < 0;
            if (sCustom) SPut("b", "1");
            if (sCustom) SPut("c", "8");
            else if (si != 7) SPut("c", si.ToString(Inv));
            if (sCustom && Hex(s.SniperDotColor) != "FFFFFFFF") SPut("t", Hex(s.SniperDotColor));
            PutNum(SPut, "s", s.SniperDotSize, 1, 0, 4, false);
            PutNum(SPut, "o", s.SniperDotOpacity, 0.75f, 0, 1, false);
            sn.Append(Unknown("S"));
            if (sn.Length > 0) sb.Append(";S").Append(sn);
        }
        foreach (var (marker, tokens) in x.UnknownSections)
        {
            sb.Append(';').Append(marker);
            foreach (var t in tokens) sb.Append(';').Append(t);
        }
        return sb.ToString();
    }

    static string Section(CrosshairStyle st, CrosshairCodeSection x)
    {
        var sb = new StringBuilder();
        void Put(string k, string v) => sb.Append(';').Append(k).Append(';').Append(v);
        int ci = PresetIndex(st.Color);
        bool custom = ci < 0 || x.CustomViaC8 || x.CustomViaB;
        bool viaC8 = custom && (x.CustomViaC8 || !x.CustomViaB), viaB = custom && (x.CustomViaB || !x.CustomViaC8);
        if (custom && viaC8) Put("c", "8");
        else if (!custom && ci != 0) Put("c", ci.ToString(Inv));
        if (custom && Hex(st.Color) != "FFFFFFFF") Put("u", Hex(st.Color));
        else if (!custom && x.StaleHex != null && x.StaleHex != "FFFFFFFF") Put("u", x.StaleHex);
        if (!st.HasOutline) Put("h", "0");
        PutNum(Put, "t", st.OutlineThickness, 1, 1, 6, true);
        PutNum(Put, "o", st.OutlineOpacity, 0.5f, 0, 1, false);
        if (st.CenterDot) Put("d", "1");
        if (custom && viaB) Put("b", "1");
        PutNum(Put, "z", st.CenterDotSize, 2, 1, 6, true);
        PutNum(Put, "a", st.CenterDotOpacity, 1, 0, 1, false);
        if (!x.Fade) Put("f", "0");
        if (!x.ShowSpectated) Put("s", "0");
        if (x.OverrideFiringOffset) Put("m", "1");
        PutLines(Put, "0", st.Inner, DefaultStyle().Inner, 20, 20);
        PutLines(Put, "1", st.Outer, DefaultStyle().Outer, 10, 40);
        return sb.ToString();
    }

    static void PutLines(Action<string, string> put, string p, CrosshairLines l, CrosshairLines def, float maxLen, float maxOff)
    {
        if (l.Show != def.Show) put(p + "b", l.Show ? "1" : "0");
        PutNum(put, p + "t", l.Thickness, def.Thickness, 0, 10, true);
        PutNum(put, p + "l", l.Length, def.Length, 0, maxLen, true);
        PutNum(put, p + "v", l.LengthVertical, def.LengthVertical, 0, maxLen, true);
        if (l.AllowVertScaling != def.AllowVertScaling) put(p + "g", l.AllowVertScaling ? "1" : "0");
        PutNum(put, p + "o", l.Offset, def.Offset, 0, maxOff, false);
        PutNum(put, p + "a", l.Opacity, def.Opacity, 0, 1, false);
        if (l.ShowMovementError != def.ShowMovementError) put(p + "m", l.ShowMovementError ? "1" : "0");
        PutNum(put, p + "s", l.MovementErrorScale, def.MovementErrorScale, 0, 3, false);
        if (l.ShowShootingError != def.ShowShootingError) put(p + "f", l.ShowShootingError ? "1" : "0");
        PutNum(put, p + "e", l.FiringErrorScale, def.FiringErrorScale, 0, 3, false);
    }

    static void PutNum(Action<string, string> put, string k, float v, float def, float lo, float hi, bool integer)
    {
        double r = Math.Clamp(integer ? Math.Round(v) : Math.Round(v, 3), lo, hi);
        if (Math.Abs(r - Math.Round(def, 3)) < 1e-6) return;
        put(k, Fmt(r));
    }

    /// <summary>VALORANT's number format: up to 3 decimals, no trailing zeros, leading zero ("0.374", "1").</summary>
    public static string Fmt(double v)
    {
        var s = Math.Round(v, 3).ToString("0.###", Inv);
        return s == "-0" ? "0" : s;
    }

    // =====================================================================================
    // ValTrainer's effective crosshair
    // =====================================================================================

    static string? finderCode, finderLast, finderTab;
    static CrosshairSettings? finder, finderLive;

    /// <summary>ValTrainer's own crosshair (Settings → Crosshair → CUSTOM; null = none / unreadable): a code the player
    /// pasted (the whole profile), or the Crosshair Finder's result saved with "Use in ValTrainer", which overrides only
    /// the tab it tested (<see cref="AppSettings.FinderCrosshairTab"/>) on top of the live VALORANT import, so later
    /// changes to the other tabs in VALORANT still show up (<see cref="ApplyTab"/>).</summary>
    public static CrosshairSettings? Finder
    {
        get
        {
            var st = Main.I?.Settings;
            var code = st?.FinderCrosshairCode;
            var last = st?.LastFinderCrosshairCode;
            var tabName = st?.FinderCrosshairTab;
            var live = Main.I?.Valorant?.Crosshair;
            if (code != finderCode || last != finderLast || tabName != finderTab || !ReferenceEquals(live, finderLive))
            {
                finderCode = code;
                finderLast = last;
                finderTab = tabName;
                finderLive = live;
                finder = Decode(code);
                if (finder != null && TabFromName(tabName) is { } tab && live != null)
                {
                    finder = ApplyTab(tab, live, finder);
                    finder.Name = $"{live.Name} + Finder {TabName(tab).ToUpperInvariant()}";
                }
                else if (finder != null) finder.Name = IsFinderResult(code) ? "Crosshair Finder" : "Custom code";
            }
            return finder;
        }
    }

    /// <summary>"primary" / "ads" / "sniper" (<see cref="AppSettings.FinderCrosshairTab"/>).</summary>
    public static string TabName(UI.CrosshairView.Mode tab) => tab switch
    {
        UI.CrosshairView.Mode.Ads => "ads",
        UI.CrosshairView.Mode.Sniper => "sniper",
        _ => "primary",
    };

    public static UI.CrosshairView.Mode? TabFromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "primary" => UI.CrosshairView.Mode.Primary,
        "ads" => UI.CrosshairView.Mode.Ads,
        "sniper" => UI.CrosshairView.Mode.Sniper,
        _ => null,
    };

    /// <summary>
    /// <paramref name="live"/> (the player's VALORANT profile) with only <paramref name="tab"/> taken from
    /// <paramref name="result"/> (a whole profile, e.g. the Crosshair Finder's merged result code). The live profile keeps
    /// its other tabs and general flags, except what the tab needs to show in VALORANT:
    /// <list type="bullet">
    /// <item>Primary: P (the result's fade flag; the live "show spectated" stays).</item>
    /// <item>ADS: A, Copy Primary off (p;0) and Use Advanced Options on (s;1). If advanced options were off, the sniper
    /// dot stays VALORANT's default one (what the player saw).</item>
    /// <item>Sniper: S and Use Advanced Options on (s;1). If advanced options were off, ADS keeps copying the primary.</item>
    /// </list>
    /// Same rules as the Crosshair Finder's merge (XfTabs.Merge). Returns a new profile; neither input changes.
    /// </summary>
    public static CrosshairSettings ApplyTab(UI.CrosshairView.Mode tab, CrosshairSettings live, CrosshairSettings result)
    {
        var s = live.Clone();
        var x = s.Extras;
        bool adv = s.UseAdvancedOptions;
        switch (tab)
        {
            case UI.CrosshairView.Mode.Ads:
                s.Ads = result.Ads.Clone();
                x.Ads = result.Extras.Ads.Clone();
                s.UsePrimaryForAds = false;
                if (!adv)
                {
                    var d = new CrosshairSettings().SniperDotFor();
                    (s.SniperDot, s.SniperDotColor, s.SniperDotSize, s.SniperDotOpacity) = (d.Show, d.Color, d.Size, d.Opacity);
                    x.SniperCustom = false;
                }
                SetAdvanced(s);
                break;
            case UI.CrosshairView.Mode.Sniper:
                if (!adv) s.UsePrimaryForAds = true;
                (s.SniperDot, s.SniperDotColor, s.SniperDotSize, s.SniperDotOpacity) =
                    (result.SniperDot, result.SniperDotColor, result.SniperDotSize, result.SniperDotOpacity);
                x.SniperCustom = result.Extras.SniperCustom;
                SetAdvanced(s);
                break;
            default:
                s.Primary = result.Primary.Clone();
                var sec = result.Extras.Primary.Clone();
                sec.ShowSpectated = x.Primary.ShowSpectated;
                x.Primary = sec;
                break;
        }
        return s;

        static void SetAdvanced(CrosshairSettings s)
        {
            s.UseAdvancedOptions = s.Extras.AdvancedOptions = true;
            s.Extras.KeepAdsSection = s.Extras.KeepSniperSection = false; // as the game exports it: no ignored leftovers
        }
    }

    /// <summary>True when <paramref name="code"/> is the Crosshair Finder's latest result (not a pasted code).</summary>
    public static bool IsFinderResult(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim() == Main.I?.Settings.LastFinderCrosshairCode?.Trim();

    static bool devRead;
    static CrosshairSettings? devCode;

    /// <summary>Dev-only "--xhair-code CODE": every drill and preview uses this crosshair (screenshots, tests).</summary>
    public static CrosshairSettings? DevOverride
    {
        get
        {
            if (devRead) return devCode;
            devRead = true;
            devCode = Decode(CmdLine.DevAfter("--xhair-code"));
            if (devCode != null) devCode.Name = "Dev code (--xhair-code)";
            return devCode;
        }
    }

    /// <summary>VALORANT profile code of the crosshair ValTrainer uses now (copy buttons in Settings and on the menu): the
    /// whole profile (Primary, ADS, sniper dot and General flags), as VALORANT would export it.</summary>
    public static string EffectiveCode
    {
        get
        {
            var s = Main.I.Settings;
            if (DevOverride is { } dev) return Encode(dev);
            if (s.UseFinderCrosshair && Finder is { } f && !string.IsNullOrWhiteSpace(s.FinderCrosshairCode))
                return TabFromName(s.FinderCrosshairTab) != null ? Encode(f) : s.FinderCrosshairCode!.Trim(); // one tab on the live import / the pasted code as is
            try { return Encode(Main.I.Valorant.Crosshair); } catch { return "0"; } // "0" = VALORANT's default crosshair
        }
    }

    /// <summary>The crosshair drills use: ValTrainer's own code when Settings → Crosshair is CUSTOM, else VALORANT's.</summary>
    public static CrosshairSettings Effective =>
        DevOverride ?? (Main.I.Settings.UseFinderCrosshair && Finder is { } f ? f : Main.I.Valorant.Crosshair);

    // =====================================================================================
    // Self-test (dev: --dev --culture-test, which tools/selftest.ps1 runs; also --dev --mode xhairfinder --xftest)
    // =====================================================================================

    /// <summary>Real codes (prosettings.net) exported by the game, i.e. canonical: re-encoding must reproduce them exactly.</summary>
    public static readonly (string Who, string Code)[] ProCodes =
    {
        ("TenZ", "0;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0"),
        ("Leo", "0;P;h;0;0l;3;0o;0;0a;1;0f;0;1b;0"),
        ("Demon1", "0;s;1;P;o;1;d;1;m;1;0b;0;1b;0"),
        ("ScreaM", "0;s;1;P;c;5;o;1;d;1;z;3;f;0;0t;6;0l;0;0a;1;0f;0;1b;0;S;c;6;s;0.949;o;1"),
        ("Kyedae", "0;s;1;P;c;8;u;FFFFCCFF;h;0;b;1;f;0;0l;4;0o;0;0a;1;0f;0;1b;0"),
        ("Crazyface", "0;p;0;s;1;P;c;5;h;0;d;1;z;1;f;0;m;1;0t;1;0l;2;0o;1;0a;1;0e;0.847;1b;0;A;o;1;d;1;z;3;f;0;s;0;0b;0;1b;0;S;c;0;s;0.7;o;0.7"),
        ("f0rsakeN", "0;s;1;P;c;8;u;000000FF;o;1;b;1;0t;3;0l;1;0v;0;0g;1;0o;0;0a;1;0f;0;1t;1;1l;4;1g;1;1o;0;1a;1;1m;0;1f;0;S;s;0.664;o;1"),
    };

    /// <summary>
    /// Community shape crosshairs (crosshaircanvas.com database): VALORANT can't draw a circle or box, so these are
    /// short, thick inner lines. <c>Canonical</c> is the form VALORANT itself exports when the database code isn't it
    /// (the database writes the default colour "c;0", which the game omits, as in Leo's white code); null = canonical.
    /// </summary>
    public static readonly (string Name, string Code, string? Canonical)[] ShapeCodes =
    {
        ("Very small circle (white)", "0;P;c;0;h;0;f;0;0l;1;0o;1;0a;1;0f;0;1b;0", "0;P;h;0;f;0;0l;1;0o;1;0a;1;0f;0;1b;0"),
        ("Small circle (green)", "0;P;c;1;h;0;f;0;0t;4;0l;1;0o;2;0a;1;0f;0;1b;0", null),
        ("Small hollow square (white)", "0;P;c;0;h;0;f;0;0t;5;0l;1;0o;1;0a;1;0f;0;1b;0", "0;P;h;0;f;0;0t;5;0l;1;0o;1;0a;1;0f;0;1b;0"),
        ("Medium hollow square (green)", "0;P;c;1;h;0;f;0;0t;6;0l;1;0o;2;0a;1;0f;0;1b;0", null),
        ("Large hollow square (cyan)", "0;P;c;5;h;0;f;0;0t;9;0l;1;0a;1;0f;0;1b;0", null),
        ("Medium dot (green)", "0;P;c;1;h;0;d;1;z;1;f;0;0t;6;0l;5;0o;0;0a;1;0f;0;1b;0", null),
        ("Large square dot (cyan)", "0;P;c;5;h;0;d;1;z;6;f;0;0b;0;1b;0", null),
        ("Closed plus (green)", "0;P;c;1;h;0;0l;3;0o;0;0a;1;0f;0;1b;0", null),
    };

    /// <summary>Real split codes (genesy/crosshair-codes samples, prosettings.net) exported by the game, i.e. canonical,
    /// plus one hand-made code with A / S sections VALORANT ignores (no s;1).</summary>
    public static readonly (string Name, string Code)[] SplitCodes =
    {
        ("split 1 (red cross, red ADS dot)", "0;p;0;s;1;P;c;7;u;64AAE6FF;h;0;f;0;0l;5;0o;0;0a;1;0f;0;1b;0;A;c;7;u;EF92BFFF;o;1;d;1;f;0;s;0;0b;0;1b;0;S;o;1"),
        ("split 2", "0;p;0;s;1;P;o;1;f;0;0t;1;0l;2;0o;2;0a;1;0f;0;1b;0;A;o;1;d;1;0b;0;1b;0;S;s;0.359;o;1"),
        ("split 3", "0;p;0;s;1;P;o;1;f;0;0t;1;0l;2;0a;1;0f;0;1l;0;A;o;1;d;1;z;1;0l;0;1l;0;S;c;0;s;0.75;o;1"),
        ("Crazyface (now)", "0;s;1;P;c;5;h;0;d;1;f;0;m;1;0l;2;0a;1;0e;0.7;1b;0;S;c;0;s;0.652;o;1"),
        ("p;0 without advanced options", "0;p;0;P;c;5;u;A020F0FF;h;0;f;0;0l;4;0o;2;0a;1;0f;0;1b;0"),
        ("A / S without s;1 (hand-made)", "0;p;0;P;c;1;A;c;5;S;c;0"),
    };

    /// <summary>Unlinked lengths with a 0 horizontal length (a "|" crosshair) and split inner / outer lengths.</summary>
    public static readonly (string Name, string Code)[] VerticalOnlyCodes =
    {
        ("vertical-only inner lines", "0;P;h;0;0l;0;0v;4;0g;1;0o;2;1b;0"),
        ("horizontal inner + vertical outer", "0;P;h;0;0l;5;0v;0;0g;1;1l;0;1v;4;1g;1;1o;4"),
    };

    /// <summary>Codes with keys / sections ValTrainer doesn't know: they must survive a round trip.</summary>
    public static readonly (string Name, string Code)[] UnknownCodes =
    {
        ("unknown section at the end", "0;s;1;P;c;1;S;o;1;F;c;5;0l;3"),
        ("community NAME extension", "0;P;c;1;NAME;\"Test\""),
        ("unknown global and primary keys", "0;q;1;P;c;1;zz;5"),
        ("unknown sniper key", "0;s;1;S;o;1;q;2"),
    };

    /// <summary>Hand-made (non-canonical) code: out of order, 6-digit hex, explicit defaults.</summary>
    public const string DerkeCode = "0;P;c;8;b;1;t;1;o;1;z;2;a;1;0t;2;0l;6;0v;6;0o;3;0a;0.8;0s;1;0e;1;1t;2;1l;2;1v;2;1o;10;1a;0.35;1s;1;1e;1;u;FFFFFF;d;1;h;1;0g;0;1g;0;0f;0;1f;1;0m;0;1m;1;0b;0;1b;0;m;0";

    /// <summary>Encode/decode checks; returns the failures (empty = all passed).</summary>
    public static List<string> SelfTest(out int checks)
    {
        var fail = new List<string>();
        int n = 0;
        void Check(string what, bool ok, string detail = "") { n++; if (!ok) fail.Add(what + (detail.Length > 0 ? ": " + detail : "")); }
        bool Near(float a, float b) => Math.Abs(a - b) < 6e-4f;
        bool SameColor(Color a, Color b) => a.ToRgba32() == b.ToRgba32();

        // 1. factory default
        var d = Decode("0")!;
        Check("decode \"0\"", d != null);
        Check("\"0\" is VALORANT's default", d != null && Diff(d, DefaultSettings()) == null, d == null ? "" : Diff(d, DefaultSettings()) ?? "");
        Check("encode default = \"0\"", Encode(DefaultSettings()) == "0", Encode(DefaultSettings()));

        // 2. garbage
        foreach (var bad in new[] { "", "   ", "hello", "1;P;c;5", "P;c;5", "0;P;c", "0;P;toolong;1" })
            Check($"reject \"{bad}\"", Decode(bad) == null);
        Check("trailing ';' and spaces", Decode(" 0;P;c;5; ") is { } tr && SameColor(tr.Primary.Color, Presets[5]));

        // 3. pro codes: canonical round trip + their known looks
        foreach (var (who, code) in ProCodes)
        {
            var s = Decode(code, out var x);
            Check($"{who} decodes", s != null);
            if (s == null) continue;
            string again = Encode(s, x);
            Check($"{who} re-encodes exactly", again == code, again);
            var s2 = Decode(again);
            Check($"{who} decode(encode) equal", s2 != null && Diff(s, s2) == null, s2 == null ? "null" : Diff(s, s2) ?? "");
        }
        var tenz = Decode(ProCodes[0].Code)!;
        Check("TenZ: cyan small cross, no outline, firing error on, no outer lines",
            SameColor(tenz.Primary.Color, Presets[5]) && !tenz.Primary.HasOutline && !tenz.Primary.CenterDot
            && tenz.Primary.Inner.Show && Near(tenz.Primary.Inner.Thickness, 2) && Near(tenz.Primary.Inner.Length, 4) && Near(tenz.Primary.Inner.Offset, 2)
            && Near(tenz.Primary.Inner.Opacity, 1) && tenz.Primary.Inner.ShowShootingError && !tenz.Primary.Outer.Show);
        var leo = Decode(ProCodes[1].Code)!;
        Check("Leo: white tight plus (offset 0), static",
            SameColor(leo.Primary.Color, Presets[0]) && !leo.Primary.HasOutline && Near(leo.Primary.Inner.Length, 3)
            && Near(leo.Primary.Inner.Offset, 0) && !leo.Primary.Inner.ShowShootingError && !leo.Primary.Outer.Show);
        var demon = Decode(ProCodes[2].Code, out var demonX)!;
        Check("Demon1: white dot only, solid outline",
            demon.Primary.CenterDot && Near(demon.Primary.CenterDotSize, 2) && demon.Primary.HasOutline && Near(demon.Primary.OutlineOpacity, 1)
            && !demon.Primary.Inner.Show && !demon.Primary.Outer.Show && demon.UseAdvancedOptions && demonX.Primary.OverrideFiringOffset);
        var scream = Decode(ProCodes[3].Code, out var screamX)!;
        Check("ScreaM: cyan dot 3, inner length 0, pink sniper dot 0.949",
            SameColor(scream.Primary.Color, Presets[5]) && scream.Primary.CenterDot && Near(scream.Primary.CenterDotSize, 3)
            && Near(scream.Primary.Inner.Length, 0) && Near(scream.Primary.Inner.Thickness, 6) && !screamX.Primary.Fade
            && SameColor(scream.SniperDotColor, Presets[6]) && Near(scream.SniperDotSize, 0.949f) && Near(scream.SniperDotOpacity, 1));
        var kyedae = Decode(ProCodes[4].Code)!;
        Check("Kyedae: custom pale yellow FFFFCC plus, gap 0",
            SameColor(kyedae.Primary.Color, Color.Color8(255, 255, 204)) && Near(kyedae.Primary.Inner.Offset, 0) && Near(kyedae.Primary.Inner.Length, 4));
        var crazy = Decode(ProCodes[5].Code, out var crazyX)!;
        Check("Crazyface: separate ADS (white dot 3), primary cyan cross+dot, 0e 0.847",
            !crazy.UsePrimaryForAds && SameColor(crazy.Ads.Color, Presets[0]) && crazy.Ads.CenterDot && Near(crazy.Ads.CenterDotSize, 3)
            && !crazy.Ads.Inner.Show && !crazyX.Ads.ShowSpectated && SameColor(crazy.Primary.Color, Presets[5]) && crazy.Primary.CenterDot
            && Near(crazy.Primary.Inner.FiringErrorScale, 0.847f) && SameColor(crazy.SniperDotColor, Presets[0]) && Near(crazy.SniperDotSize, 0.7f));
        var fors = Decode(ProCodes[6].Code)!;
        Check("f0rsakeN: black custom, unlinked lengths (inner 1/0, outer 4/2 default)",
            SameColor(fors.Primary.Color, Color.Color8(0, 0, 0)) && fors.Primary.Inner.AllowVertScaling && Near(fors.Primary.Inner.Length, 1)
            && Near(fors.Primary.Inner.LengthVertical, 0) && fors.Primary.Outer.AllowVertScaling && Near(fors.Primary.Outer.Length, 4)
            && Near(fors.Primary.Outer.LengthVertical, 2) && !fors.Primary.Outer.ShowMovementError && Near(fors.SniperDotSize, 0.664f));

        // 3b. community shape codes: canonical ones re-encode byte for byte; the others to VALORANT's canonical form
        foreach (var (name, code, canonical) in ShapeCodes)
        {
            var s = Decode(code, out var x);
            Check($"{name} decodes", s != null);
            if (s == null) continue;
            string again = Encode(s, x), want = canonical ?? code;
            Check($"{name} re-encodes to {(canonical == null ? "itself" : "its canonical form")}", again == want, again);
            var s2 = Decode(want);
            Check($"{name} canonical form is the same crosshair", s2 != null && Diff(s, s2) == null, s2 == null ? "null" : Diff(s, s2) ?? "");
            Check($"{name}: no outline, no outer lines, static opaque inner lines (if shown)",
                !s.Primary.HasOutline && !s.Primary.Outer.Show
                && (!s.Primary.Inner.Show || (Near(s.Primary.Inner.Opacity, 1) && !s.Primary.Inner.ShowShootingError)));
        }
        var circle = Decode(ShapeCodes[1].Code)!.Primary.Inner;
        Check("small circle: 1 px lines 4 px thick at offset 2", Near(circle.Thickness, 4) && Near(circle.Length, 1) && Near(circle.Offset, 2));
        var sq = Decode(ShapeCodes[4].Code)!.Primary.Inner;
        Check("large hollow square: 9 thick, length 1, default offset 3", Near(sq.Thickness, 9) && Near(sq.Length, 1) && Near(sq.Offset, 3));
        var sqDot = Decode(ShapeCodes[6].Code)!.Primary;
        Check("large square dot: dot 6, no lines", sqDot.CenterDot && Near(sqDot.CenterDotSize, 6) && !sqDot.Inner.Show);

        // 3c. split crosshairs, the advanced-options gate, vertical-only lines, unknown keys / sections
        Check("new CrosshairSettings() is VALORANT's factory profile", Diff(new CrosshairSettings(), DefaultSettings()) == null,
            Diff(new CrosshairSettings(), DefaultSettings()) ?? "");
        foreach (var (name, code) in SplitCodes)
        {
            var s = Decode(code);
            Check($"{name} decodes", s != null);
            if (s == null) continue;
            string again = Encode(s);
            Check($"{name} re-encodes exactly", again == code, again);
            Check($"{name} survives Clone()", Encode(s.Clone()) == code, Encode(s.Clone()));
        }
        var sp1 = Decode(SplitCodes[0].Code)!;
        var hip1 = sp1.StyleFor(UI.CrosshairView.Mode.Primary);
        var ads1 = sp1.StyleFor(UI.CrosshairView.Mode.Ads);
        var dot1 = sp1.SniperDotFor();
        Check("split 1: red primary cross (stale hex kept), red ADS dot, opaque default-red sniper dot",
            sp1.UseAdvancedOptions && sp1.AdsIsOwn && SameColor(hip1.Color, Presets[7]) && !hip1.CenterDot && hip1.Inner.Show && sp1.Extras.Primary.StaleHex == "64AAE6FF"
            && ads1 == sp1.Ads && ads1.CenterDot && SameColor(ads1.Color, Presets[7]) && !ads1.Inner.Show && !ads1.Outer.Show
            && dot1.Show && SameColor(dot1.Color, Presets[7]) && Near(dot1.Opacity, 1) && Near(dot1.Size, 1));
        var sp3 = Decode(SplitCodes[2].Code)!;
        Check("split 3: ADS dot only (zero-length lines), white sniper dot 0.75",
            sp3.StyleFor(UI.CrosshairView.Mode.Ads).CenterDot && Near(sp3.Ads.Inner.Length, 0) && Near(sp3.Ads.Outer.Length, 0)
            && SameColor(sp3.SniperDotFor().Color, Presets[0]) && Near(sp3.SniperDotFor().Size, 0.75f));
        var cf = Decode(SplitCodes[3].Code)!;
        Check("Crazyface now: ADS copies the primary, own sniper dot", cf.UseAdvancedOptions && !cf.AdsIsOwn
            && cf.StyleFor(UI.CrosshairView.Mode.Ads) == cf.Primary && SameColor(cf.SniperDotFor().Color, Presets[0]) && Near(cf.SniperDotFor().Size, 0.652f));
        var p0 = Decode(SplitCodes[4].Code)!;
        Check("p;0 with advanced options off: ADS = primary, default sniper dot", !p0.UseAdvancedOptions && !p0.UsePrimaryForAds && !p0.AdsIsOwn
            && p0.StyleFor(UI.CrosshairView.Mode.Ads) == p0.Primary && SameColor(p0.SniperDotFor().Color, Presets[7]) && Near(p0.SniperDotFor().Opacity, 0.75f));
        var gated = Decode(SplitCodes[5].Code)!;
        Check("A / S without s;1: kept for the code, ignored for drawing", gated.Extras.KeepAdsSection && gated.Extras.KeepSniperSection
            && gated.StyleFor(UI.CrosshairView.Mode.Ads) == gated.Primary && SameColor(gated.Ads.Color, Presets[5])
            && SameColor(gated.SniperDotFor().Color, Presets[7]) && SameColor(gated.SniperDotColor, Presets[0]));

        // The encoder writes s;1 only from the flag, A only with advanced options + own ADS, S only with advanced options.
        var split = DefaultSettings();
        split.UsePrimaryForAds = false;
        split.Ads.CenterDot = true;
        split.SniperDotColor = Presets[5];
        Check("encode: p;0 without advanced options = no s;1, no A, no S", Encode(split) == "0;p;0", Encode(split));
        split.UseAdvancedOptions = true;
        Check("encode: advanced split = s;1 + A + S", Encode(split) == "0;p;0;s;1;A;d;1;S;c;5", Encode(split));
        var copy = split.Clone();
        copy.Ads.CenterDot = false;
        copy.Extras.UnknownKeys.Add(("", "q", "1"));
        Check("Clone() is deep", split.Ads.CenterDot && split.Extras.UnknownKeys.Count == 0);

        // Vertical-only lines (unlinked lengths with a 0 horizontal length) must draw their vertical arms.
        var vert = Decode(VerticalOnlyCodes[0].Code)!.Primary;
        var vr = UI.CrosshairView.Raster(vert, 10);
        Check("vertical-only inner lines draw (| shape)", vr[5].Contains('#') && vr[14].Contains('#') && !vr[9].Contains('#') && !vr[10].Contains('#'),
            string.Join("/", vr));
        var vOuter = Decode(VerticalOnlyCodes[1].Code)!.Primary;
        var vo = UI.CrosshairView.Raster(vOuter, 12);
        Check("vertical-only outer lines + horizontal-only inner lines draw (split lengths)",
            vo[11][5] == '#' && vo[11][18] == '#' && vo[5].Contains('#') && vo[18].Contains('#') && !vo[11].Substring(10, 4).Contains('#') && !vo[9].Contains('#'),
            string.Join("/", vo));
        foreach (var (name, code) in VerticalOnlyCodes)
            Check($"{name} re-encodes exactly", Decode(code) is { } vc && Encode(vc) == code, Decode(code) is { } vc2 ? Encode(vc2) : "null");

        // Unknown sections (a future tab marker, the community NAME;"…" extension) and unknown keys are kept.
        foreach (var (name, code) in UnknownCodes)
            Check($"{name} re-encodes exactly", Decode(code) is { } uc && Encode(uc) == code, Decode(code) is { } uc2 ? Encode(uc2) : "null");
        var mid = Decode("0;s;1;P;c;1;F;c;5;0l;3;S;o;1")!;
        Check("unknown section in the middle doesn't leak into P / S", SameColor(mid.Primary.Color, Presets[1]) && Near(mid.Primary.Inner.Length, 6)
            && Near(mid.SniperDotFor().Opacity, 1) && mid.Extras.UnknownSections.Count == 1 && mid.Extras.UnknownSections[0].Marker == "F",
            Diff(mid, DefaultSettings()) ?? "");
        Check("unknown section in the middle re-encodes after the known ones", Encode(mid) == "0;s;1;P;c;1;S;o;1;F;c;5;0l;3", Encode(mid));

        // 3d. a Finder result applied as one tab on top of the live VALORANT profile
        string crazyNow = SplitCodes[3].Code, split1 = SplitCodes[0].Code, tenzCode = ProCodes[0].Code;
        foreach (var (what, tab, live, result, want) in new[]
                 {
                     ("ADS onto an advanced profile", UI.CrosshairView.Mode.Ads, crazyNow, split1,
                         "0;p;0;s;1;P;c;5;h;0;d;1;f;0;m;1;0l;2;0a;1;0e;0.7;1b;0;A;c;7;u;EF92BFFF;o;1;d;1;f;0;s;0;0b;0;1b;0;S;c;0;s;0.652;o;1"),
                     ("ADS onto advanced off (sniper dot stays default)", UI.CrosshairView.Mode.Ads, tenzCode, split1,
                         "0;p;0;s;1;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0;A;c;7;u;EF92BFFF;o;1;d;1;f;0;s;0;0b;0;1b;0"),
                     ("sniper onto p;0 with advanced off (ADS stays the primary)", UI.CrosshairView.Mode.Sniper, SplitCodes[4].Code, SplitCodes[1].Code,
                         "0;s;1;P;c;5;u;A020F0FF;h;0;f;0;0l;4;0o;2;0a;1;0f;0;1b;0;S;s;0.359;o;1"),
                     ("primary onto a split profile", UI.CrosshairView.Mode.Primary, split1, tenzCode,
                         "0;p;0;s;1;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0;A;c;7;u;EF92BFFF;o;1;d;1;f;0;s;0;0b;0;1b;0;S;o;1"),
                 })
        {
            var l = Decode(live)!;
            var r = Decode(result)!;
            string got = Encode(ApplyTab(tab, l, r));
            Check($"ApplyTab: {what}", got == want, got);
            Check($"ApplyTab: {what} leaves its inputs alone", Encode(l) == live && Encode(r) == result);
        }

        // 4. non-canonical input
        var derke = Decode(DerkeCode, out var derkeX);
        Check("Derke (hand-made) decodes", derke != null);
        if (derke != null)
        {
            Check("Derke: custom white dot, outline opacity 1, no lines",
                SameColor(derke.Primary.Color, Presets[0]) && derke.Primary.CenterDot && Near(derke.Primary.OutlineOpacity, 1)
                && !derke.Primary.Inner.Show && !derke.Primary.Outer.Show && derkeX.Primary.CustomViaB && derkeX.Primary.CustomViaC8);
            var canon = Encode(derke, derkeX);
            Check("Derke canonical form decodes to the same crosshair", Decode(canon) is { } dc && Diff(dc, derke) == null, canon);
            Check("Derke canonical form is shorter", canon.Length < DerkeCode.Length, canon);
        }

        // 5. random round trips (values VALORANT can represent)
        var rng = new Random(20261002);
        for (int i = 0; i < 2000; i++)
        {
            var s = RandomSettings(rng);
            string code = Encode(s);
            var back = Decode(code);
            string? diff = back == null ? "null" : Diff(s, back);
            Check($"random #{i} decode(encode(x)) == x", diff == null, $"{diff} · {code}");
            if (back != null) Check($"random #{i} encode is stable", Encode(back) == code, Encode(back));
            if (fail.Count > 20) break;
        }
        checks = n;
        return fail;
    }

    static CrosshairSettings RandomSettings(Random r)
    {
        float I(int lo, int hi) => r.Next(lo, hi + 1);
        float D(float lo, float hi) => (float)Math.Round(lo + r.NextDouble() * (hi - lo), 3);
        bool B() => r.Next(2) == 0;
        Color Col()
        {
            if (B()) return Presets[r.Next(Presets.Length)];
            return Color.Color8((byte)r.Next(256), (byte)r.Next(256), (byte)r.Next(256), r.Next(4) == 0 ? (byte)r.Next(256) : (byte)255);
        }
        CrosshairLines L(int maxLen, int maxOff) => new()
        {
            Show = B(), Thickness = I(0, 10), Length = I(0, maxLen), LengthVertical = I(0, maxLen), AllowVertScaling = B(),
            Offset = r.Next(3) == 0 ? D(0, maxOff) : I(0, maxOff), Opacity = D(0, 1),
            ShowMovementError = B(), MovementErrorScale = D(0, 3), ShowShootingError = B(), FiringErrorScale = D(0, 3),
        };
        CrosshairStyle St() => new()
        {
            Color = Col(), HasOutline = B(), OutlineThickness = I(1, 6), OutlineOpacity = D(0, 1), OutlineColor = Colors.Black,
            CenterDot = B(), CenterDotSize = I(1, 6), CenterDotOpacity = D(0, 1),
            Inner = L(20, 20), Outer = L(10, 40),
        };
        bool sep = B();
        var s = new CrosshairSettings
        {
            Name = "random", Primary = St(), UsePrimaryForAds = !sep, UseAdvancedOptions = B(), SniperDot = B(), SniperDotColor = Col(),
            SniperDotSize = D(0, 4), SniperDotOpacity = D(0, 1),
        };
        s.Ads = sep ? St() : DefaultStyle(); // a copied ADS isn't in the code: it decodes as the factory style
        return s;
    }

    /// <summary>First differing field (null = same crosshair as far as a code can express).</summary>
    public static string? Diff(CrosshairSettings a, CrosshairSettings b)
    {
        static bool N(float x, float y) => Math.Abs(x - y) < 6e-4f;
        static string? Style(string w, CrosshairStyle a, CrosshairStyle b)
        {
            if (a.Color.ToRgba32() != b.Color.ToRgba32()) return $"{w}.color {Hex(a.Color)}≠{Hex(b.Color)}";
            if (a.HasOutline != b.HasOutline) return w + ".outline";
            if (!N(a.OutlineThickness, b.OutlineThickness)) return w + ".outlineThickness";
            if (!N(a.OutlineOpacity, b.OutlineOpacity)) return w + ".outlineOpacity";
            if (a.CenterDot != b.CenterDot) return w + ".dot";
            if (!N(a.CenterDotSize, b.CenterDotSize)) return w + ".dotSize";
            if (!N(a.CenterDotOpacity, b.CenterDotOpacity)) return w + ".dotOpacity";
            return Lines(w + ".inner", a.Inner, b.Inner) ?? Lines(w + ".outer", a.Outer, b.Outer);
        }
        static string? Lines(string w, CrosshairLines a, CrosshairLines b)
        {
            if (a.Show != b.Show) return w + ".show";
            if (!N(a.Thickness, b.Thickness)) return w + ".thickness";
            if (!N(a.Length, b.Length)) return w + ".length";
            if (!N(a.LengthVertical, b.LengthVertical)) return w + ".lengthV";
            if (a.AllowVertScaling != b.AllowVertScaling) return w + ".unlinked";
            if (!N(a.Offset, b.Offset)) return $"{w}.offset {a.Offset}≠{b.Offset}";
            if (!N(a.Opacity, b.Opacity)) return w + ".opacity";
            if (a.ShowMovementError != b.ShowMovementError) return w + ".moveErr";
            if (!N(a.MovementErrorScale, b.MovementErrorScale)) return w + ".moveErrScale";
            if (a.ShowShootingError != b.ShowShootingError) return w + ".fireErr";
            if (!N(a.FiringErrorScale, b.FiringErrorScale)) return w + ".fireErrScale";
            return null;
        }
        if (a.UseAdvancedOptions != b.UseAdvancedOptions) return "advancedOptions";
        if (a.UsePrimaryForAds != b.UsePrimaryForAds) return "usePrimaryForAds";
        if (a.SniperIsOwn) // advanced options off: VALORANT ignores (and doesn't export) the Sniper tab
        {
            if (a.SniperDot != b.SniperDot) return "sniperDot";
            if (a.SniperDotColor.ToRgba32() != b.SniperDotColor.ToRgba32()) return "sniperColor";
            if (!N(a.SniperDotSize, b.SniperDotSize)) return "sniperSize";
            if (!N(a.SniperDotOpacity, b.SniperDotOpacity)) return "sniperOpacity";
        }
        return Style("primary", a.Primary, b.Primary) ?? (a.AdsIsOwn ? Style("ads", a.Ads, b.Ads) : null);
    }
}
