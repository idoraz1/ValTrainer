using Godot;
using ValTrainer.Maps;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>Crosshair Finder protocol parameters. <see cref="Quick"/> is the dev/test configuration (--xfquick).</summary>
public sealed class XfConfig
{
    // Part 1: visibility (detection reaction time per colour × background)
    public int ColorCandidates = 4;          // shortlisted colour/outline combos (incl. the player's current one)
    public float ForeMin = 0.7f, ForeMax = 1.9f; // random wait before the crosshair appears
    public float RtTimeout = 1.2f;           // a miss counts as this RT
    public float VisFeedback = 0.45f;
    public string[] Scenes = { "ascent_wall", "bind_floor", "haven_wall", "split_floor", "enemy", "enemy_bright" };
    public float PartHeaderSec = 3.5f;

    // Part 2: shape tournament (blocks of flicks + micro-adjusts + one kill per crosshair, ABBA / BAAB)
    public int Flicks = 4, Micros = 3, Kills = 1;
    public int BlocksPerMatch = 4;
    public float RestSec = 3f;
    public float Foreperiod = 0.3f;
    public float FlickTimeout = 2.0f, MicroTimeout = 2.5f, KillTimeout = 4f;
    public float PrefTimeout = 10f;
    // Target placement: angle from the crosshair (°) and distance (m) per trial kind; the agent's yaw offset and distance
    public float FlickAmpMin = 8f, FlickAmpMax = 30f, FlickDistMin = 13f, FlickDistMax = 18f;
    public float MicroAmpMin = 1.5f, MicroAmpMax = 5f, MicroDistMin = 28f, MicroDistMax = 36f;
    public float KillYawMin = 10f, KillYawMax = 24f, KillDistMin = 15f, KillDistMax = 20f;
    /// <summary>Rough seconds per flick / micro-adjust / kill (for <see cref="EstimatedMinutes"/>).</summary>
    public float FlickSec = 1.2f, MicroSec = 1.3f, KillSec = 1.6f;
    // Shape pick (before the bracket): the time limit, and how long a pick or drop keeps it open at least
    public float PickTimeout = 30f, PickExtend = 10f;
    /// <summary>Flick / micro-adjust hit rate (clicks and timeouts) below which a match means fatigue or a break.</summary>
    public float MinAccuracy = 0.3f;

    public int VisTrials => ColorCandidates * Scenes.Length;
    public int TrialsPerBlock => Flicks + Micros + Kills;

    /// <summary>Rough wall-clock estimate (the shape pick, then 4 matches incl. the check against the player's crosshair, counted as 5 for the screens in between), minutes.</summary>
    public float EstimatedMinutes =>
        (VisTrials * ((ForeMin + ForeMax) / 2 + 0.4f + VisFeedback) + 2 * PartHeaderSec + 15f
         + 5 * (BlocksPerMatch * (RestSec + Flicks * FlickSec + Micros * MicroSec + Kills * KillSec) + 5f)) / 60f;

    public static XfConfig Full() => new();

    /// <summary>The protocol for a tab. ADS keeps the primary's drills (aimed with the Vandal: 1.25× zoom, about 90° wide).
    /// Sniper drills fit the Operator's scope (2.5×, the glass about ±17° high): shorter flicks, farther heads, and one
    /// target per bolt cycle (1.7 s), so a round has fewer targets.</summary>
    public static XfConfig For(XfTab tab, bool quick)
    {
        var c = quick ? Quick() : Full();
        if (tab != XfTab.Sniper) return c;
        if (!quick) { c.Flicks = 3; c.Micros = 3; }
        c.FlickAmpMin = 3f; c.FlickAmpMax = 11f; c.FlickDistMin = 24f; c.FlickDistMax = 32f;
        c.MicroAmpMin = 0.6f; c.MicroAmpMax = 2.2f; c.MicroDistMin = 42f; c.MicroDistMax = 55f;
        c.KillYawMin = 3f; c.KillYawMax = 9f; c.KillDistMin = 28f; c.KillDistMax = 38f;
        c.FlickTimeout = 2.6f; c.MicroTimeout = 3f; c.KillTimeout = 4f;
        c.FlickSec = c.MicroSec = 2.2f; c.KillSec = 2.4f;
        return c;
    }

    public static XfConfig Quick() => new()
    {
        ColorCandidates = 3,
        ForeMin = 0.4f, ForeMax = 0.8f, VisFeedback = 0.3f,
        Scenes = new[] { "haven_wall", "enemy" },
        PartHeaderSec = 1.2f,
        Flicks = 2, Micros = 1, Kills = 1,
        BlocksPerMatch = 2,
        RestSec = 1f,
        PrefTimeout = 2.5f,
        PickTimeout = 5f, PickExtend = 3f,
    };
}

/// <summary>A colour/outline combination tested in part 1.</summary>
public sealed class XfColor
{
    public Color Color;
    public bool Outline;
    public bool IsCurrent;          // the player's own crosshair colour
    public float Contrast;          // computed visibility score (see XfVisibility)
    public readonly List<float> Rt = new();   // detection RT (s) per trial; misses = timeout
    public int Misses;
    public float Score;             // combined (higher = better)
    public string Name => CrosshairCode.ColorName(Color) + (Outline ? " + outline" : "");
    public float MeanLogRt => Rt.Count == 0 ? float.NaN : Rt.Average(r => Mathf.Log(r));
    public float MedianMs => Rt.Count == 0 ? float.NaN : Rt.OrderBy(r => r).ElementAt(Rt.Count / 2) * 1000f;
}

/// <summary>A crosshair in the shape tournament.</summary>
public sealed class XfEntrant
{
    public string Key = "", Name = "", Why = "";
    public CrosshairSettings Xhair = null!;
    public CrosshairCodeExtras Extras = new();
    public bool IsCurrent;
    public string Code => CrosshairCode.Encode(Xhair, Extras);
}

/// <summary>One block (one crosshair): trial-level scores per component.</summary>
public sealed class XfBlock
{
    public int Cond;            // 0 / 1 within the match
    public char Label;
    public readonly List<double> Flick = new();   // ln(ID / MT')
    public readonly List<double> Micro = new();   // ln(ID / MT') − 0.4 · first-shot error (in head radii)
    public readonly List<double> Kill = new();    // −ln(time to kill)
    public readonly List<float> FlickMs = new(), MicroErr = new(), KillMs = new();
    public int Hits, Shots, Timeouts;
    public int AimHits, AimShots;   // flicks + micro-adjusts only (crosshair ray; the kill's spray misses don't count)
    public double Score;
}

/// <summary>A head-to-head between two crosshairs: 4 blocks ABBA / BAAB, then "which felt better?".</summary>
public sealed class XfMatch
{
    public string Title = "";
    public readonly int[] Entrant = new int[2];
    public readonly char[] Label = new char[2];
    public int[] Order = new int[4];
    public readonly List<XfBlock> Blocks = new();
    public SfComparison? Result;
    public int Pref = -1;       // 0 / 1 / -1 none
    public int Winner = -1;
    public string DecidedBy = "";
    public bool IsCheck;        // final check against the player's own crosshair

    public float Accuracy
    {
        get
        {
            int h = Blocks.Sum(b => b.AimHits), s = Blocks.Sum(b => b.AimShots);
            return s == 0 ? 1f : (float)h / s;
        }
    }
}

public static class XfStats
{
    public const double WFlick = 0.4, WMicro = 0.4, WKill = 0.2;

    /// <summary>
    /// Like <see cref="SfStats.Compare"/> with three components: each is standardized within the match (pooled over
    /// both crosshairs), the condition difference is taken with Welch's SE and the components are blended
    /// 0.4 / 0.4 / 0.2 (renormalized over the components that have data). Fills each block's composite score.
    /// </summary>
    public static SfComparison Compare(IReadOnlyList<XfBlock> blocks)
    {
        var comps = new (Func<XfBlock, List<double>> Get, double W)[] { (b => b.Flick, WFlick), (b => b.Micro, WMicro), (b => b.Kill, WKill) };
        double d = 0, se2 = 0, wsum = 0;
        var ds = new double[3]; var ses = new double[3]; var n0 = new int[3]; var n1 = new int[3];
        var has = new bool[3];
        var moments = new (double M, double S)[3];
        for (int i = 0; i < 3; i++)
        {
            var get = comps[i].Get;
            var all = blocks.SelectMany(get).ToList();
            double m = SfStats.Mean(all), sd = Math.Sqrt(SfStats.Var(all));
            if (sd < 1e-9) sd = 1;
            moments[i] = (m, sd);
            var a = blocks.Where(b => b.Cond == 0).SelectMany(get).Select(z => (z - m) / sd).ToList();
            var c = blocks.Where(b => b.Cond == 1).SelectMany(get).Select(z => (z - m) / sd).ToList();
            n0[i] = a.Count; n1[i] = c.Count;
            if (a.Count < 1 || c.Count < 1 || a.Count + c.Count < 3) continue;
            var (di, si) = SfStats.Welch(a, c);
            if (a.Count < 2 || c.Count < 2) si = Math.Max(si, 1.0); // one-trial side: no variance estimate, be conservative
            ds[i] = di; ses[i] = si; has[i] = true;
            wsum += comps[i].W;
        }
        if (wsum <= 0) return new SfComparison(0, 0, 0, 0, 0, 0, 0, n0[0], n1[0], n0[1], n1[1]);
        for (int i = 0; i < 3; i++)
        {
            if (!has[i]) continue;
            double w = comps[i].W / wsum;
            d += w * ds[i];
            se2 += w * w * ses[i] * ses[i];
        }
        double se = Math.Sqrt(se2), t = se > 1e-9 ? d / se : 0;
        foreach (var b in blocks)
        {
            double s = 0, ws = 0;
            for (int i = 0; i < 3; i++)
            {
                var v = comps[i].Get(b);
                if (v.Count == 0) continue;
                s += comps[i].W * v.Average(z => (z - moments[i].M) / moments[i].S);
                ws += comps[i].W;
            }
            b.Score = ws > 0 ? s / ws : 0;
        }
        // DFlick/DTrack slots carry flick and micro (kill is folded into D)
        return new SfComparison(d, se, t, ds[0], ses[0], ds[1], ses[1], n0[0], n1[0], n0[1], n1[1]);
    }
}

/// <summary>
/// Computed visibility of a crosshair colour (and black outline) over the backgrounds a VALORANT player aims at:
/// the four maps' surface palettes in light and shadow, the enemy body and the enemy highlight colour.
/// Contrast is CIELAB lightness difference plus 0.4× the colour difference (see LineContrast); an outline adds the black
/// edge's own contrast.
/// </summary>
public static class XfVisibility
{
    public readonly record struct Bg(string Name, Color Color, float Weight);

    public static readonly Color EnemyBody = Color.Color8(58, 60, 68);

    public static List<Bg> Backgrounds(Color enemy)
    {
        var list = new List<Bg>();
        foreach (var m in MapSpots.All)
        {
            var p = m.Palette;
            foreach (var (name, c, w) in new[] { ("floor", p.Floor, 1f), ("wall", p.Wall, 1.2f), ("cover", p.Cover, 1f), ("trim", p.Trim, 0.5f), ("elevated", p.Elevated, 0.6f), ("sky", p.Sky, 0.5f) })
            {
                list.Add(new Bg($"{m.Key} {name}", c, w));
                list.Add(new Bg($"{m.Key} {name} shade", (c * 0.55f) with { A = 1 }, w * 0.5f));
                list.Add(new Bg($"{m.Key} {name} sun", Lighten(c, 1.18f), w * 0.5f));
            }
        }
        list.Add(new Bg("enemy highlight", enemy, 6f));
        list.Add(new Bg("enemy body", EnemyBody, 2f));
        return list;
    }

    static Color Lighten(Color c, float k) => new(Mathf.Min(1, c.R * k), Mathf.Min(1, c.G * k), Mathf.Min(1, c.B * k));

    static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

    public static Vector3 Lab(Color c)
    {
        float r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
        float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        float fx = F(x), fy = F(y), fz = F(z);
        return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    public static float DeltaE(Color a, Color b) => Lab(a).DistanceTo(Lab(b));

    /// <summary>
    /// Contrast of a thin line against a background: lightness difference at full weight, colour (a*b*) difference at
    /// 0.4. Crosshair lines are 1–3 px, i.e. high spatial frequency, where the eye's colour-contrast sensitivity falls
    /// off much faster than its luminance-contrast sensitivity, so lightness carries most of the visibility.
    /// </summary>
    public static float LineContrast(Color a, Color b)
    {
        var la = Lab(a); var lb = Lab(b);
        float dl = la.X - lb.X, da = la.Y - lb.Y, db = la.Z - lb.Z;
        return Mathf.Sqrt(dl * dl + 0.16f * (da * da + db * db));
    }

    /// <summary>Visibility of the crosshair over one background; a black outline adds half its own edge contrast,
    /// minus a little for the bulkier shape.</summary>
    public static float Visibility(Color c, bool outline, Color bg)
    {
        float fill = LineContrast(c, bg);
        if (!outline) return fill;
        float edge = LineContrast(Colors.Black, bg);
        return Mathf.Sqrt(fill * fill + 0.25f * edge * edge) * 0.93f;
    }

    /// <summary>Weighted mean of log(1 + contrast) over the backgrounds (diminishing returns: 100 isn't twice 50),
    /// with the worst background counted again so a colour that vanishes somewhere loses, minus a penalty for a hue
    /// close to the enemy highlight (the crosshair melts into the outline exactly when it's on target).</summary>
    public static float Score(Color c, bool outline, IReadOnlyList<Bg> bgs, Color enemy)
    {
        float de = DeltaE(c, enemy);
        float penalty = de < 45f ? 0.2f * (1f - de / 45f) : 0f;
        float s = 0, w = 0, worst = float.MaxValue;
        foreach (var b in bgs)
        {
            float v = Mathf.Log(1 + Mathf.Min(100f, Visibility(c, outline, b.Color)));
            s += v * b.Weight;
            w += b.Weight;
            worst = Mathf.Min(worst, v);
        }
        return 0.75f * s / w + 0.25f * worst - penalty;
    }
}

/// <summary>The shapes of the tournament (built in the colour part 1 picked).</summary>
public static class XfShapes
{
    public static CrosshairStyle Base(Color color, bool outline)
    {
        var s = CrosshairCode.DefaultStyle();
        s.Color = color;
        s.HasOutline = outline;
        s.OutlineThickness = 1;
        s.OutlineOpacity = outline ? 1 : 0.5f; // solid black edge when on; untouched default when off (shorter code)
        s.Inner.Opacity = 1;
        s.Inner.ShowShootingError = false;
        s.Outer.Show = false;
        return s;
    }

    static XfEntrant Make(string key, string name, string why, CrosshairStyle st, bool fade = false)
    {
        var x = new CrosshairCodeExtras();
        x.Primary.Fade = fade;
        return new XfEntrant
        {
            Key = key, Name = name, Why = why, Extras = x,
            Xhair = new CrosshairSettings { Name = name, Primary = st, Ads = CrosshairCode.DefaultStyle(), UsePrimaryForAds = true, Extras = x },
        };
    }

    /// <summary>Dot: the least screen covered; pure point of aim (Demon1 / ScreaM style).</summary>
    public static XfEntrant Dot(Color c, bool o)
    {
        var s = Base(c, o);
        s.CenterDot = true;
        s.CenterDotSize = o ? 2 : 3;
        s.CenterDotOpacity = 1;
        s.Inner = CrosshairCode.DefaultStyle().Inner; // lines off; untouched sub-settings keep the code minimal
        s.Inner.Show = false;
        return Make("dot", "Dot", "covers the least of the target", s);
    }

    /// <summary>Small static cross 2×4, gap 2 (the most common pro crosshair, e.g. TenZ without firing error).</summary>
    public static XfEntrant SmallCross(Color c, bool o)
    {
        var s = Base(c, o);
        s.Inner.Thickness = 2; s.Inner.Length = 4; s.Inner.Offset = 2;
        return Make("cross", "Small cross", "the most common pro shape", s);
    }

    /// <summary>Framed dot: a dot inside a wider gap, so a far head fits between the lines instead of under them.</summary>
    public static XfEntrant FramedDot(Color c, bool o)
    {
        var s = Base(c, o);
        s.CenterDot = true; s.CenterDotSize = 2; s.CenterDotOpacity = 1;
        s.Inner.Thickness = 2; s.Inner.Length = 3; s.Inner.Offset = 4;
        return Make("framed", "Framed dot", "the head sits inside the gap", s);
    }

    /// <summary>Dynamic cross: a static inner cross plus outer lines that spread with firing error (spray feedback).</summary>
    public static XfEntrant Dynamic(Color c, bool o)
    {
        var s = Base(c, o);
        s.Inner.Thickness = 2; s.Inner.Length = 4; s.Inner.Offset = 2;
        s.Outer.Show = true; s.Outer.Thickness = 2; s.Outer.Length = 2; s.Outer.Offset = 7;
        s.Outer.Opacity = 0.6f; s.Outer.ShowShootingError = true; s.Outer.FiringErrorScale = 1; s.Outer.ShowMovementError = true;
        return Make("dynamic", "Dynamic cross", "outer lines show your spread", s, fade: true);
    }

    // VALORANT can't draw a real circle or box: the community "circle" / "hollow square" crosshairs are four very short
    // (1 px), thick inner lines around the centre (crosshaircanvas.com database). Thickness 2·(offset + length) closes
    // the corners into a box; a little thinner leaves 1 px corner notches, which reads as a ring. An outline grows
    // into the hole, so the outlined versions sit one pixel further out.

    /// <summary>Closed plus: inner lines at offset 0 meet in the middle (Leo / Kyedae style).</summary>
    public static XfEntrant ClosedPlus(Color c, bool o)
    {
        var s = Base(c, o);
        s.Inner.Thickness = 2; s.Inner.Length = 3; s.Inner.Offset = 0;
        return Make("plus", "Closed plus", "one solid point that's easy to see", s);
    }

    /// <summary>Plus + dot: a closed plus with a 4 px dot on top, a bold centre with short arms.</summary>
    public static XfEntrant PlusDot(Color c, bool o)
    {
        var s = Base(c, o);
        s.CenterDot = true; s.CenterDotSize = 4; s.CenterDotOpacity = 1;
        s.Inner.Thickness = 2; s.Inner.Length = 4; s.Inner.Offset = 0;
        return Make("plusdot", "Plus + dot", "a bold centre that stays visible on busy walls", s);
    }

    /// <summary>Circle (small ring): 1 px lines 4 px thick at offset 2, a 6 px ring around a 4 px hole (the
    /// community "small circle"); outlined: 6 thick at offset 3.</summary>
    public static XfEntrant Circle(Color c, bool o)
    {
        var s = Base(c, o);
        s.Inner.Thickness = o ? 6 : 4; s.Inner.Length = 1; s.Inner.Offset = o ? 3 : 2;
        return Make("circle", "Circle", "the head sits inside the ring: good for taps and far heads", s);
    }

    /// <summary>Circle + dot: an 8 px ring with a 2 px dot in the middle; outlined: a 10 px ring.</summary>
    public static XfEntrant CircleDot(Color c, bool o)
    {
        var s = Base(c, o);
        s.CenterDot = true; s.CenterDotSize = 2; s.CenterDotOpacity = 1;
        s.Inner.Thickness = o ? 8 : 6; s.Inner.Length = 1; s.Inner.Offset = o ? 4 : 3;
        return Make("circledot", "Circle + dot", "a dot to aim with, a ring to frame the head", s);
    }

    /// <summary>Hollow square (box): thickness = 2·(offset + length) closes the corners (the community "medium hollow
    /// square", 6 px); outlined: 8 px.</summary>
    public static XfEntrant HollowSquare(Color c, bool o)
    {
        var s = Base(c, o);
        s.Inner.Thickness = o ? 8 : 6; s.Inner.Length = 1; s.Inner.Offset = o ? 3 : 2;
        return Make("box", "Hollow square", "a crisp box the head fits inside", s);
    }

    /// <summary>Every shape of the pick screen, in card order.</summary>
    public static List<XfEntrant> All(Color c, bool o) => new()
    {
        Dot(c, o), SmallCross(c, o), FramedDot(c, o), Dynamic(c, o), ClosedPlus(c, o),
        PlusDot(c, o), Circle(c, o), CircleDot(c, o), HollowSquare(c, o),
    };

    /// <summary>The recommended four (pre-selected): one of each classic family.</summary>
    public static readonly string[] Defaults = { "dot", "cross", "framed", "dynamic" };

    /// <summary>
    /// Shape checks for --xftest: every shape in several colours, with and without an outline, survives a code round
    /// trip; the circle / box match the community reference codes byte for byte; and the real-size pixels have the
    /// intended form (ring with a hole and notched corners, closed box, solid closed plus).
    /// </summary>
    public static List<string> SelfTest(out int checks)
    {
        var fail = new List<string>();
        int n = 0;
        void Check(string what, bool ok, string detail = "") { n++; if (!ok) fail.Add(what + (detail.Length > 0 ? ": " + detail : "")); }
        foreach (int ci in new[] { 0, 1, 5, 7 })
            foreach (bool o in new[] { false, true })
                foreach (var e in All(CrosshairCode.Presets[ci], o))
                {
                    string code = e.Code, w = $"shape {e.Key} {CrosshairCode.PresetNames[ci]}{(o ? "+outline" : "")}";
                    var back = CrosshairCode.Decode(code, out var bx);
                    Check($"{w} decodes", back != null, code);
                    if (back == null) continue;
                    Check($"{w} decodes to the same settings", CrosshairCode.Diff(back, e.Xhair) == null, CrosshairCode.Diff(back, e.Xhair) ?? "");
                    Check($"{w} re-encodes to itself", CrosshairCode.Encode(back, bx) == code, CrosshairCode.Encode(back, bx));
                    Check($"{w} fade flag kept", bx.Primary.Fade == e.Extras.Primary.Fade);
                }

        var green = CrosshairCode.Presets[1];
        Check("green circle = community small circle", Circle(green, false).Code == CrosshairCode.ShapeCodes[1].Code, Circle(green, false).Code);
        Check("green hollow square = community medium hollow square", HollowSquare(green, false).Code == CrosshairCode.ShapeCodes[3].Code, HollowSquare(green, false).Code);
        Check("white closed plus = Leo's plus (with fade off)", ClosedPlus(Colors.White, false).Code == "0;P;h;0;f;0;0l;3;0o;0;0a;1;0f;0;1b;0", ClosedPlus(Colors.White, false).Code);

        // real-size pixels (8 px around the centre, which is the pixel corner between rows/columns 7 and 8)
        foreach (bool o in new[] { false, true })
        {
            string w = o ? " (outlined)" : "";
            var circ = CrosshairView.Raster(Circle(green, o).Xhair.Primary);
            int rr = o ? 4 : 3; // ring half-size
            Check("circle: hollow centre" + w, circ[7][7] == '.' && circ[8][8] == '.' && circ[7][8] == '.' && circ[8][7] == '.', string.Join("/", circ));
            Check("circle: closed ring sides" + w, circ[8 - rr][8] == '#' && circ[7 + rr][8] == '#' && circ[8][8 - rr] == '#' && circ[8][7 + rr] == '#', string.Join("/", circ));
            Check("circle: notched corners" + w, circ[8 - rr][8 - rr] == '.' && circ[7 + rr][7 + rr] == '.' && circ[8 - rr][7 + rr] == '.' && circ[7 + rr][8 - rr] == '.', string.Join("/", circ));
            Check("circle: symmetric" + w, Symmetric(circ), string.Join("/", circ));
            var box = CrosshairView.Raster(HollowSquare(green, o).Xhair.Primary);
            int br = o ? 4 : 3;
            Check("box: closed corners" + w, box[8 - br][8 - br] == '#' && box[7 + br][7 + br] == '#' && box[8 - br][7 + br] == '#' && box[7 + br][8 - br] == '#', string.Join("/", box));
            Check("box: hollow centre" + w, box[7][7] == '.' && box[8][8] == '.' && box[8 - br + 1][8 - br + 1] == '.', string.Join("/", box));
            Check("box: nothing outside" + w, box[8 - br - 1][8] == '.' && box[8][8 + br] == '.', string.Join("/", box));
            Check("box: symmetric" + w, Symmetric(box), string.Join("/", box));
            var cd = CrosshairView.Raster(CircleDot(green, o).Xhair.Primary);
            Check("circle + dot: dot, gap, ring" + w, cd[7][7] == '#' && cd[8][8] == '#' && cd[6][8] == '.' && cd[8][9] == '.' && cd[o ? 3 : 4][8] == '#', string.Join("/", cd));
            Check("circle + dot: symmetric" + w, Symmetric(cd), string.Join("/", cd));
            var plus = CrosshairView.Raster(ClosedPlus(green, o).Xhair.Primary);
            Check("closed plus: solid centre, arms 3 px, no corners" + w,
                plus[7][7] == '#' && plus[8][8] == '#' && plus[5][7] == '#' && plus[4][7] == '.' && plus[8][10] == '#' && plus[8][11] == '.' && plus[6][6] == '.',
                string.Join("/", plus));
            Check("closed plus: symmetric" + w, Symmetric(plus), string.Join("/", plus));
            var pd = CrosshairView.Raster(PlusDot(green, o).Xhair.Primary);
            Check("plus + dot: 4 px centre with 2 px stubs" + w, pd[6][6] == '#' && pd[9][9] == '#' && pd[5][5] == '.' && pd[4][7] == '#' && pd[3][7] == '.', string.Join("/", pd));
        }
        checks = n;
        return fail;
    }

    /// <summary>Mirror-symmetric about the centre lines (the grid centre is the screen centre).</summary>
    static bool Symmetric(string[] g)
    {
        int h = g.Length, w = g[0].Length;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (g[y][x] != g[y][w - 1 - x] || g[y][x] != g[h - 1 - y][x] || g[y][x] != g[x][y]) return false;
        return true;
    }

    /// <summary>The crosshair the player uses now (imported from VALORANT, or ValTrainer's finder setting): their whole
    /// profile; <paramref name="tab"/> only names it.</summary>
    public static XfEntrant Current(CrosshairSettings cur, XfTab tab = XfTab.Primary) => new()
    {
        Key = "current", IsCurrent = true, Why = "what you use now",
        Name = tab switch { XfTab.Ads => "Your ADS crosshair", XfTab.Sniper => "Your sniper dot", _ => "Your crosshair" },
        Xhair = cur, Extras = cur.Extras,
    };
}

/// <summary>Procedural VALORANT-like backdrops for part 1 (drawn on the HUD canvas, behind a self-drawn crosshair).</summary>
public static class XfScenes
{
    /// <summary>Background colour right under the crosshair for a scene key.</summary>
    public static (MapPalette Pal, Color Under, bool Enemy) Resolve(string key, Color enemy)
    {
        var maps = MapSpots.All;
        MapPalette P(string k) => (maps.FirstOrDefault(m => m.Key == k) ?? maps[0]).Palette;
        return key switch
        {
            "ascent_wall" => (P("ascent"), P("ascent").Wall, false),
            "bind_floor" => (P("bind"), P("bind").Floor, false),
            "haven_wall" => (P("haven"), P("haven").Wall, false),
            "split_floor" => (P("split"), P("split").Floor, false),
            "enemy" => (P("split"), P("split").Wall, true),
            "enemy_bright" => (P("haven"), P("haven").Wall, true),
            "sky" => (P("bind"), P("bind").Sky, false),
            _ => (P("ascent"), P("ascent").Wall, false),
        };
    }

    public static void Draw(CanvasItem c, Vector2 size, string key, Color enemy, int seed)
    {
        var (pal, under, isEnemy) = Resolve(key, enemy);
        var rng = new Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Color Vary(Color col, float amt) { float k = 1 + R(-amt, amt); return new Color(Mathf.Clamp(col.R * k, 0, 1), Mathf.Clamp(col.G * k, 0, 1), Mathf.Clamp(col.B * k, 0, 1)); }
        float W = size.X, H = size.Y;
        float horizon = H * R(0.22f, 0.32f), floorY = H * R(0.68f, 0.76f);

        Gfx.VGradient(c, new Rect2(0, 0, W, horizon), Lighten(pal.Sky, 1.1f), pal.Sky);
        // wall blocks with seams and a trim band
        float x = 0;
        while (x < W)
        {
            float bw = R(W * 0.12f, W * 0.3f);
            float top = horizon - R(0, H * 0.12f);
            c.DrawRect(new Rect2(x, top, bw + 1, floorY - top), Vary(pal.Wall, 0.12f));
            c.DrawRect(new Rect2(x, top, 2, floorY - top), new Color(0, 0, 0, 0.12f));
            x += bw;
        }
        c.DrawRect(new Rect2(0, floorY - H * 0.035f, W, H * 0.035f), Vary(pal.Trim, 0.05f));
        Gfx.VGradient(c, new Rect2(0, floorY, W, H - floorY), pal.Floor, (pal.Floor * 0.7f) with { A = 1 });
        // cover boxes
        for (int i = 0; i < 3; i++)
        {
            float cw = R(W * 0.06f, W * 0.14f), ch = R(H * 0.08f, H * 0.18f), cx = R(0, W - cw);
            if (Mathf.Abs(cx + cw / 2 - W / 2) < W * 0.2f) continue; // keep the centre for the tested surface
            c.DrawRect(new Rect2(cx, floorY - ch, cw, ch), Vary(pal.Cover, 0.08f));
            c.DrawRect(new Rect2(cx, floorY - ch, cw, ch * 0.08f), new Color(1, 1, 1, 0.08f));
        }

        // the tested surface under the crosshair, textured
        var mid = size / 2;
        var region = new Rect2(mid.X - W * 0.16f, mid.Y - H * 0.17f, W * 0.32f, H * 0.34f);
        c.DrawRect(region, under);
        for (int i = 0; i < 90; i++)
        {
            float s = R(6, 40);
            c.DrawRect(new Rect2(region.Position.X + R(0, region.Size.X - s), region.Position.Y + R(0, region.Size.Y - s), s, s * R(0.3f, 1f)),
                new Color(Vary(under, 0.1f), 0.5f));
        }

        if (isEnemy)
        {
            // an agent at ~10 m with the highlight-colour outline; the crosshair lands on the head
            float hr = Mathf.Max(6f, H * 0.012f);
            var head = mid;
            var glow = new Color(enemy, 0.95f);
            var neck = new Rect2(head.X - hr * 0.45f, head.Y + hr * 0.6f, hr * 0.9f, hr * 1.2f);
            Vector2[] torso =
            {
                new(head.X - hr * 2.3f, head.Y + hr * 1.7f), new(head.X + hr * 2.3f, head.Y + hr * 1.7f),
                new(head.X + hr * 1.7f, head.Y + hr * 7.5f), new(head.X - hr * 1.7f, head.Y + hr * 7.5f),
            };
            var legs = new Rect2(head.X - hr * 1.5f, head.Y + hr * 7.4f, hr * 3f, hr * 8f);
            // outline: the same shapes grown by ~2 px in the highlight colour
            c.DrawCircle(head, hr + 2.5f, glow);
            c.DrawRect(neck.Grow(2.5f), glow);
            c.FillPoly(Grow(torso, 2.5f), glow);
            c.DrawRect(legs.Grow(2.5f), glow);
            var body = XfVisibility.EnemyBody;
            c.DrawRect(legs, (body * 0.9f) with { A = 1 });
            c.FillPoly(torso, body);
            c.DrawRect(neck, body);
            c.DrawCircle(head, hr, body);
            c.DrawCircle(head + new Vector2(-hr * 0.35f, -hr * 0.35f), hr * 0.45f, new Color(enemy, 0.22f)); // fresnel
        }
        Gfx.EdgeVignette(c, size, H * 0.18f, new Color(0, 0, 0, 0.35f));
    }

    static Color Lighten(Color c, float k) => new(Mathf.Min(1, c.R * k), Mathf.Min(1, c.G * k), Mathf.Min(1, c.B * k));

    /// <summary>A convex polygon pushed outward by about <paramref name="d"/> pixels (outline).</summary>
    static Vector2[] Grow(Vector2[] p, float d)
    {
        var c = p.Aggregate(Vector2.Zero, (a, b) => a + b) / p.Length;
        return p.Select(v => v + (v - c).Normalized() * d * 1.4f).ToArray();
    }
}
