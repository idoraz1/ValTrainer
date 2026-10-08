using Godot;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>Which of VALORANT's crosshair tabs the Crosshair Finder tests.</summary>
public enum XfTab { Primary, Ads, Sniper }

/// <summary>
/// The sniper scope's centre dot (VALORANT's Sniper tab: on/off, colour, size 0–4, opacity), the only thing VALORANT
/// draws inside an Operator / Marshal / Outlaw scope. <see cref="CrosshairSettings.SniperDotFor"/> is the dot the game shows.
/// </summary>
public static class XfDot
{
    /// <summary>VALORANT's default dot (also what the game shows while Use Advanced Options is off).</summary>
    public static SniperDotStyle Default => new CrosshairSettings().SniperDotFor();

    public static bool Same(SniperDotStyle a, SniperDotStyle b) => a.Show == b.Show && (!a.Show
        || (a.Color.ToRgba32() == b.Color.ToRgba32() && Math.Abs(a.Size - b.Size) < 6e-4f && Math.Abs(a.Opacity - b.Opacity) < 6e-4f));

    /// <summary>The dot ×<paramref name="zoom"/> at <paramref name="centre"/> (the in-game radius rule of
    /// <see cref="CrosshairView.DrawSniperDot"/>, scaled for magnified previews).</summary>
    public static void Draw(CanvasItem c, SniperDotStyle d, Vector2 centre, float zoom = 1f)
    {
        if (zoom == 1f) { CrosshairView.DrawSniperDot(c, centre, d); return; }
        if (!d.Show || d.Opacity <= 0) return;
        c.DrawCircle(centre, Mathf.Max(1.5f, d.Size * 2.5f) * zoom, new Color(d.Color, d.Opacity));
    }

    public static string Describe(SniperDotStyle d) => !d.Show ? "no dot"
        : $"{CrosshairCode.ColorName(d.Color)} dot, size {CrosshairCode.Fmt(d.Size)}, opacity {CrosshairCode.Fmt(d.Opacity)}";
}

/// <summary>Per-tab helpers: what the tab looks like, and how a result is merged into the player's whole profile.</summary>
public static class XfTabs
{
    public static string Name(XfTab t) => t switch { XfTab.Ads => "ADS", XfTab.Sniper => "Sniper", _ => "Primary" };

    /// <summary>The crosshair VALORANT draws for this tab (ADS: its own only with Use Advanced Options on and Copy
    /// Primary Crosshair off; otherwise the primary).</summary>
    public static CrosshairStyle Style(XfTab t, CrosshairSettings s) =>
        s.StyleFor(t == XfTab.Ads ? CrosshairView.Mode.Ads : CrosshairView.Mode.Primary);

    /// <summary>Main colour of the tab's crosshair.</summary>
    public static Color ColorOf(XfTab t, CrosshairSettings s) => t == XfTab.Sniper ? s.SniperDotFor().Color : Style(t, s).Color;

    /// <summary>Same look on screen in this tab (used to skip the final check when the winner already is the player's).</summary>
    public static bool SameLook(XfTab t, CrosshairSettings a, CrosshairSettings b)
    {
        if (t == XfTab.Sniper) return XfDot.Same(a.SniperDotFor(), b.SniperDotFor());
        var x = new CrosshairSettings { Primary = Style(t, a), UsePrimaryForAds = true };
        var y = new CrosshairSettings { Primary = Style(t, b), UsePrimaryForAds = true };
        return CrosshairCode.Diff(x, y) == null;
    }

    /// <summary>
    /// The player's whole profile <paramref name="cur"/> with only the tested tab replaced by the candidate. A candidate
    /// is built on the factory profile: Primary / ADS candidates carry their crosshair in <c>Primary</c> (and its code
    /// flags in <paramref name="candX"/>.Primary), sniper candidates in the sniper fields.
    /// <list type="bullet">
    /// <item>Primary: replaces P (the shape's own fade flag; the player's "show spectated" stays). Everything else stays.</item>
    /// <item>ADS: replaces A, turns Copy Primary Crosshair off (p;0) and Use Advanced Options on (s;1). P stays. If
    /// advanced options were off, the sniper dot becomes VALORANT's default one, which is what the player sees now.</item>
    /// <item>Sniper: replaces S and turns Use Advanced Options on (s;1). P and A stay. If advanced options were off, ADS
    /// copies the primary (p;1), which is what the player sees now.</item>
    /// </list>
    /// </summary>
    public static CrosshairSettings Merge(XfTab t, CrosshairSettings cur, CrosshairSettings cand, CrosshairCodeExtras candX)
    {
        var s = cur.Clone();
        var x = s.Extras;
        bool adv = s.UseAdvancedOptions;
        switch (t)
        {
            case XfTab.Primary:
                s.Primary = cand.Primary.Clone();
                x.Primary = Section(candX.Primary, spectated: x.Primary.ShowSpectated, fade: candX.Primary.Fade);
                break;
            case XfTab.Ads:
                s.Ads = cand.Primary.Clone();
                x.Ads = Section(candX.Primary, spectated: true, fade: true); // both General flags live in P
                s.UsePrimaryForAds = false;
                if (!adv)
                {
                    var d = XfDot.Default;
                    (s.SniperDot, s.SniperDotColor, s.SniperDotSize, s.SniperDotOpacity) = (d.Show, d.Color, d.Size, d.Opacity);
                    x.SniperCustom = false;
                }
                SetAdvanced(s);
                break;
            case XfTab.Sniper:
                if (!adv) s.UsePrimaryForAds = true;
                (s.SniperDot, s.SniperDotColor, s.SniperDotSize, s.SniperDotOpacity) = (cand.SniperDot, cand.SniperDotColor, cand.SniperDotSize, cand.SniperDotOpacity);
                x.SniperCustom = candX.SniperCustom;
                SetAdvanced(s);
                break;
        }
        return s;
    }

    /// <summary>Advanced options on, as the game exports it: A only for an own ADS crosshair, S always (no leftover
    /// sections from a decoded code the game ignored).</summary>
    static void SetAdvanced(CrosshairSettings s)
    {
        s.UseAdvancedOptions = true;
        s.Extras.AdvancedOptions = true;
        s.Extras.KeepAdsSection = false;
        s.Extras.KeepSniperSection = false;
    }

    static CrosshairCodeSection Section(CrosshairCodeSection cand, bool spectated, bool fade) => new()
    {
        Fade = fade,
        ShowSpectated = spectated,
        OverrideFiringOffset = cand.OverrideFiringOffset,
        CustomViaC8 = cand.CustomViaC8,
        CustomViaB = cand.CustomViaB,
    };

    /// <summary>A candidate as a tournament entrant for this tab: its crosshair merged into the player's profile (what
    /// the rounds show and what the code exports).</summary>
    public static XfEntrant Into(XfTab t, CrosshairSettings cur, XfEntrant e)
    {
        var s = Merge(t, cur, e.Xhair, e.Extras);
        s.Name = e.Name;
        return new XfEntrant { Key = e.Key, Name = e.Name, Why = e.Why, Xhair = s, Extras = s.Extras };
    }

    // =====================================================================================
    // Self-test (--xftest)
    // =====================================================================================

    /// <summary>Merge checks: a result changes only its own tab and sets the global flags VALORANT needs.</summary>
    public static List<string> SelfTest(out int checks)
    {
        var fail = new List<string>();
        int n = 0;
        void Check(string what, bool ok, string detail = "") { n++; if (!ok) fail.Add(what + (detail.Length > 0 ? ": " + detail : "")); }
        static CrosshairSettings P(string code) => CrosshairCode.Decode(code)!;
        static string Code(XfTab t, string cur, XfEntrant e) => Into(t, P(cur), e).Code;
        static bool SameStyle(CrosshairStyle a, CrosshairStyle b) =>
            CrosshairCode.Diff(new CrosshairSettings { Primary = a }, new CrosshairSettings { Primary = b }) == null;
        static string From(string code, string marker) => code[code.IndexOf(marker, StringComparison.Ordinal)..];
        static string Upto(string code, string marker) => code[..code.IndexOf(marker, StringComparison.Ordinal)];
        void RoundTrip(string w, string code)
        {
            var back = CrosshairCode.Decode(code);
            Check($"{w}: decodes", back != null, code);
            if (back != null) Check($"{w}: re-encodes to itself", CrosshairCode.Encode(back) == code, CrosshairCode.Encode(back));
        }

        const string tenz = "0;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0";
        string crazy = CrosshairCode.ProCodes.First(p => p.Who == "Crazyface").Code;   // p;0 s;1, own ADS and sniper dot
        string scream = CrosshairCode.ProCodes.First(p => p.Who == "ScreaM").Code;     // s;1, ADS copies, pink sniper dot
        const string copyOff = "0;p;0;P;c;5;u;A020F0FF;h;0;f;0;0l;4;0o;2;0a;1;0f;0;1b;0"; // p;0 without s;1: ADS = primary
        var dot = XfShapes.Dot(CrosshairCode.Presets[5], false);
        var cross = XfShapes.SmallCross(CrosshairCode.Presets[7], true);
        var fine = XfDots.All(Colors.White).First(e => e.Key == "fine");

        // Primary: into the factory profile it is the shape's own code; into a split profile it keeps A, S and the flags
        foreach (var e in XfShapes.All(CrosshairCode.Presets[1], false))
            Check($"primary {e.Key} into \"0\" = its own code", Code(XfTab.Primary, "0", e) == e.Code, Code(XfTab.Primary, "0", e));
        {
            var c = P(crazy);
            string m = Into(XfTab.Primary, c, dot).Code;
            var back = P(m);
            Check("primary result keeps the ADS crosshair", back.AdsIsOwn && SameStyle(back.Ads, c.Ads), m);
            Check("primary result keeps the sniper dot", XfDot.Same(back.SniperDotFor(), c.SniperDotFor()), m);
            Check("primary result keeps p;0 and s;1", m.StartsWith("0;p;0;s;1;P;"), m);
            Check("primary result replaces P", SameStyle(back.Primary, dot.Xhair.Primary), m);
            Check("primary result: A and S unchanged", From(m, ";A;") == From(crazy, ";A;"), m);
            RoundTrip("primary into Crazyface", m);
            Check("primary result doesn't change the profile it merged into", CrosshairCode.Encode(c) == crazy, CrosshairCode.Encode(c));
        }
        {
            string m = Code(XfTab.Primary, "0;c;1;s;1;P;c;5;s;0;0b;0;S;c;0;o;1", dot);
            Check("primary result keeps Override All Primary, Show Spectated and the sniper dot",
                m.StartsWith("0;c;1;s;1;P;") && m.Contains(";s;0;") && m.EndsWith(";S;c;0;o;1"), m);
        }

        // ADS: sets s;1 and p;0, keeps the primary
        {
            string m = Code(XfTab.Ads, tenz, cross);
            var back = P(m);
            Check("ADS result sets p;0 and s;1 and keeps the primary", m.StartsWith("0;p;0;s;1;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0;A;"), m);
            Check("ADS result: the ADS crosshair is the candidate", back.UseAdvancedOptions && back.AdsIsOwn
                && SameStyle(XfTabs.Style(XfTab.Ads, back), cross.Xhair.Primary), m);
            Check("ADS result (advanced options were off): default sniper dot", XfDot.Same(back.SniperDotFor(), XfDot.Default) && !m.Contains(";S;"), m);
            RoundTrip("ADS into TenZ", m);
        }
        {
            string m = Code(XfTab.Ads, scream, cross);
            Check("ADS result keeps a custom sniper dot", m.StartsWith("0;p;0;s;1;" + From(Upto(scream, ";S;"), "P;")) && m.EndsWith(From(scream, ";S;")), m);
            string m2 = Code(XfTab.Ads, crazy, cross);
            Check("ADS result into a split profile replaces only A", m2.StartsWith(Upto(crazy, ";A;") + ";A;") && m2.EndsWith(From(crazy, ";S;"))
                && From(m2, ";A;") != From(crazy, ";A;"), m2);
            RoundTrip("ADS into Crazyface", m2);
        }
        {
            // advanced options off, but a different dot stored in the Sniper tab: the result keeps the dot the player sees
            var c = P(tenz);
            c.SniperDotColor = CrosshairCode.Presets[5]; c.SniperDotSize = 3;
            var back = P(Into(XfTab.Ads, c, cross).Code);
            Check("ADS result ignores a sniper dot VALORANT doesn't show", XfDot.Same(back.SniperDotFor(), XfDot.Default));
        }

        // Sniper: sets s;1, keeps P and A
        {
            string m = Code(XfTab.Sniper, tenz, fine);
            Check("sniper result: s;1, primary kept, ADS still copies it", m.StartsWith("0;s;1;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0;S;") && !m.Contains("p;0"), m);
            Check("sniper result: the dot is the candidate", XfDot.Same(P(m).SniperDotFor(), fine.Xhair.SniperDotFor()), m);
            RoundTrip("sniper into TenZ", m);
            string m2 = Code(XfTab.Sniper, copyOff, fine);
            Check("sniper result (advanced options were off, p;0): ADS stays the primary", !m2.Contains("p;0") && !m2.Contains(";A;") && m2.StartsWith("0;s;1;P;"), m2);
            string m3 = Code(XfTab.Sniper, crazy, fine);
            Check("sniper result into a split profile replaces only S", m3.StartsWith(Upto(crazy, ";S;") + ";S;") && From(m3, ";S;") != From(crazy, ";S;"), m3);
            Check("sniper result into a split profile: the dot is the candidate", XfDot.Same(P(m3).SniperDotFor(), fine.Xhair.SniperDotFor()), m3);
            RoundTrip("sniper into Crazyface", m3);
            string m4 = Code(XfTab.Sniper, "0;p;0;P;c;1;A;c;5;S;c;0", fine);
            Check("sniper result drops A / S sections VALORANT ignored", !m4.Contains(";A;") && m4.StartsWith("0;s;1;P;c;1;S;"), m4);
        }

        // every dot: round trip, its own code = merged into the factory profile, a look of its own
        foreach (int ci in new[] { 0, 5, 7 })
            foreach (var e in XfDots.All(CrosshairCode.Presets[ci]))
            {
                string w = $"dot {e.Key} {CrosshairCode.PresetNames[ci]}";
                RoundTrip(w, e.Code);
                Check($"{w}: own code = merged into \"0\"", Code(XfTab.Sniper, "0", e) == e.Code, Code(XfTab.Sniper, "0", e));
                Check($"{w}: advanced options on", e.Code.StartsWith("0;s;1"), e.Code);
                Check($"{w}: not the default dot", !XfDot.Same(e.Xhair.SniperDotFor(), XfDot.Default) || (e.Key == "standard" && ci == 7));
            }

        // same look per tab
        {
            var c = P(crazy);
            Check("same ADS look", SameLook(XfTab.Ads, Into(XfTab.Ads, c, new XfEntrant { Xhair = new CrosshairSettings { Primary = c.Ads } }).Xhair, c));
            Check("different ADS look", !SameLook(XfTab.Ads, Into(XfTab.Ads, c, cross).Xhair, c));
            Check("an ADS that copies the primary looks like the primary",
                SameLook(XfTab.Ads, P(tenz), Into(XfTab.Ads, P("0"), new XfEntrant { Xhair = P(tenz), Extras = P(tenz).Extras }).Xhair));
            Check("sniper look with advanced options off is the default dot", SameLook(XfTab.Sniper, P(tenz), P("0")) && !SameLook(XfTab.Sniper, P(scream), P("0")));
        }
        checks = n;
        return fail;
    }
}

/// <summary>The sniper centre-dot candidates of the tournament (in the colour part 1 picked).</summary>
public static class XfDots
{
    static XfEntrant Make(string key, string name, string why, Color c, float size, float opacity, bool on = true)
    {
        var s = CrosshairCode.DefaultSettings();
        s.Name = name;
        s.UseAdvancedOptions = s.Extras.AdvancedOptions = true;
        (s.SniperDot, s.SniperDotColor, s.SniperDotSize, s.SniperDotOpacity) = (on, c, size, opacity);
        return new XfEntrant { Key = key, Name = name, Why = why, Xhair = s, Extras = s.Extras };
    }

    /// <summary>Every dot of the pick screen, in card order.</summary>
    public static List<XfEntrant> All(Color c) => new()
    {
        Make("fine", "Fine dot", "a pinpoint, like most pros use", c, 0.6f, 1f),
        Make("solid", "Solid dot", "default size, fully solid", c, 1f, 1f),
        Make("standard", "Standard dot", "VALORANT's default size and opacity", c, 1f, 0.75f),
        Make("medium", "Medium dot", "a little bigger, easy to find", c, 1.5f, 1f),
        Make("big", "Big dot", "stays visible on busy backgrounds", c, 2f, 1f),
        Make("soft", "Soft big dot", "big but see-through: the head stays visible", c, 2f, 0.5f),
        Make("none", "No dot", "only the scope's own lines", c, 1f, 0.75f, on: false),
    };

    /// <summary>The recommended four (pre-selected).</summary>
    public static readonly string[] Defaults = { "fine", "solid", "big", "soft" };
}
