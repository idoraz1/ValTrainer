using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Game.Fx;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>
/// Crosshair Finder: finds the crosshair a player sees and shoots best with and hands it over as a VALORANT
/// crosshair profile code (VALORANT itself is never changed).
/// <para>Part 1, visibility: colour/outline candidates are ranked by computed contrast (CIELAB, lightness-weighted) against the four maps'
/// palettes and the enemy highlight (<see cref="XfVisibility"/>); the best few plus the player's own colour are tested
/// with a detection task (click the moment the crosshair appears over map / enemy backdrops). Reaction time to a
/// stimulus falls with its contrast (Piéron's law), so the fastest colour is the one that pops on this monitor for
/// these eyes. Combined score: 0.6 z(−mean ln RT) + 0.4 z(contrast).</para>
/// <para>Part 2, shape: the player shoots cards to pick 2–4 of the shapes in <see cref="XfShapes.All"/> (dot, small cross,
/// framed dot, dynamic cross, closed plus, plus + dot, circle, circle + dot, hollow square; the first four start picked),
/// then a bracket in that colour (4: two semis and a final; 3: one semi and a bye; 2: a final), then a check of the
/// winner against the player's own crosshair. Each match is 4 blocks ABBA / BAAB with the real Vandal; a block is
/// head flicks (ln ID/MT'), micro-adjusts on far heads (ln ID/MT' − 0.4·first-shot error in head radii) and one agent
/// to kill (−ln TTK), compared like the sens finder (standardized within the match, Welch SE, weights 0.4/0.4/0.2).
/// |t| ≥ 1 decides; otherwise the player's "which felt better?" answer, otherwise the edge in d.</para>
/// <para>Tabs (shot on the start screen): PRIMARY as above; ADS plays the rounds aimed down sights with the Vandal
/// (<see cref="IGame.ForcedScope"/>) and shows the candidates as the ADS crosshair; SNIPER tests the scope's centre dot
/// (<see cref="XfDots"/>: size and opacity, after the colour part) scoped in with the Operator. Every candidate is the
/// player's whole profile with only that tab replaced (<see cref="XfTabs.Merge"/>), so the code and "Use in ValTrainer"
/// never touch the other tabs.</para>
/// Dev flags (with --dev): --xfquick (short protocol), --xfseed N, --xfexit (quit at the result), --xfpick key,key,...
/// (fix the shape picks), --xftype primary|ads|sniper (the tab the dev run picks), --xhair-code CODE (the player's crosshair
/// for this run), --xftest (code, shape and merge self-test, then quit with the number of failures).
/// </summary>
public sealed partial class CrosshairFinderMode : TrainingMode
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public override string Key => "xhairfinder";
    public override string Name => "Crosshair Finder";
    public override string Description => "Try dot, cross, plus, circle and box crosshairs in real drills (hip fire, ADS or the sniper scope); get the one you see and shoot best with as a VALORANT code.";
    public override string Category => "Coach";
    public override WeaponKind Weapon => WeaponKind.Vandal;
    public override bool Timed => false;
    public override bool Done => done;
    public override bool ShowScore => false;
    public override string? Subtitle => "Visibility + shape test";

    enum Ph { Intro, Header, VisWait, VisShow, VisFeedback, VisResult, Pick, Rest, Trial, Pref, Summary }
    enum TK { Flick, Micro, Kill }

    Ph phase;
    float phaseT;
    bool done, devExit, simPlayer, quick;
    ulong lastRealUsec;
    XfConfig cfg = null!;
    XfTab tab, devTab;
    bool devTabSet;
    float lastShotAt = -99f;
    Random rng = null!;
    string notice = "";
    float noticeUntil;
    int focusId = 1;

    // the player's crosshair (whole profile + the code-only flags) and ValTrainer setting before the drill
    // ("Keep my crosshair" restores them)
    CrosshairSettings cur = null!;
    bool origUse;
    string? origCode, origTab;
    static readonly CrosshairSettings Hidden = new() { Name = "hidden", Primary = new CrosshairStyle { Hide = true } };

    // ---- part 1 ----
    int part;
    readonly List<XfColor> colors = new();
    readonly List<(int Cond, string Scene, int Seed)> visQueue = new();
    int visIdx, falseStarts;
    float fore, onset;
    Vector3 visPoint;
    string visFeedback = "";
    XfColor chosen = null!;

    // ---- shape pick ----
    List<XfEntrant> shapes = new();
    bool[] sel = Array.Empty<bool>();
    float pickDeadline, pickShotAt = -9f;
    Vector3 pickEye;
    readonly List<int> simPlan = new(); // dev simulated player: cards to shoot, START last
    int nPick;                           // shapes in the bracket (entrants 0..nPick-1; the player's crosshair is nPick)
    string byeName = "";

    // ---- part 2 ----
    readonly List<XfEntrant> entrants = new();
    readonly List<XfMatch> matches = new();
    XfMatch? match;
    int blockIdx, doneBlocks, totalBlocks;
    XfBlock? block;
    readonly List<TK> seq = new();
    int trialIdx, misses;
    bool trialLive;
    float spawnAt, amp, rAng, firstErr;
    Vector3 aimFwd = Vector3.Forward; // crosshair direction this frame before the gun fires (and kicks the view)
    Target target = null!;
    BotCharacter? bot, deadBot;
    readonly Target[] prefT = new Target[3];
    int simPref = -1;
    bool aborted;
    string abortWhy = "";

    // ---- result ----
    XfEntrant? result;
    bool keepCurrent;
    string code = "";

    public override AimFocus? Focus => phase switch
    {
        Ph.VisShow => new AimFocus(focusId, visPoint, 0.3f),
        Ph.Trial when trialLive && seq[trialIdx] == TK.Kill && bot != null && GodotObject.IsInstanceValid(bot) => new AimFocus(focusId, bot.Head, BotCharacter.HeadRadius),
        Ph.Trial when trialLive => new AimFocus(focusId, target.GlobalPosition, target.Radius),
        Ph.Pref when simPref >= 0 && phaseT > 1.5f => new AimFocus(focusId, prefT[simPref].GlobalPosition, prefT[simPref].Radius),
        Ph.Pick when simPlan.Count > 0 && phaseT > 1.2f => new AimFocus(focusId, CardQuad(simPlan[0]).C, CardQuad(simPlan[0]).U.Length() * 0.3f),
        Ph.Intro when simPlayer && phaseT > 1.5f => new AimFocus(focusId, Quad(ChoiceLayout((int)devTab)).C, Quad(ChoiceLayout((int)devTab)).U.Length() * 0.3f),
        _ => null,
    };

    static string[] Args => OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
    static string? ArgAfter(string flag)
    {
        var a = Args;
        int i = Array.IndexOf(a, flag);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    protected override void Setup()
    {
        bool dev = Main.I.Dev;
        if (dev && Args.Contains("--xftest")) { RunSelfTest(); return; }
        quick = dev && Args.Contains("--xfquick");
        cfg = XfConfig.For(XfTab.Primary, quick); // per tab once the player picks one
        int seed = dev && int.TryParse(ArgAfter("--xfseed"), out var s) ? s : System.Environment.TickCount;
        rng = new Random(seed);
        devExit = dev && Args.Contains("--xfexit");
        simPlayer = dev && ArgAfter("--simaim") != null;
        devTab = (ArgAfter("--xftype") ?? "").ToLowerInvariant() switch { "ads" => XfTab.Ads, "sniper" => XfTab.Sniper, _ => XfTab.Primary };
        devTabSet = dev && (simPlayer || ArgAfter("--xftype") != null);
        origUse = Main.I.Settings.UseFinderCrosshair;
        origCode = Main.I.Settings.FinderCrosshairCode;
        origTab = Main.I.Settings.FinderCrosshairTab;
        cur = CrosshairCode.Effective.Clone(); // the whole profile (every tab and the General flags)
        G.TrialCrosshair = null;
        G.ForcedScope = false;

        // Targets / an agent stay visible (covered by the intro and part-1 backdrops) so their materials are compiled
        // before the first timed trial.
        target = G.SpawnTarget(BotCharacter.HeadRadius);
        target.Position = new Vector3(0.5f, PlayerView.EyeHeight, -12f);
        for (int i = 0; i < 3; i++)
        {
            prefT[i] = G.SpawnTarget(0.45f);
            prefT[i].Position = new Vector3(-2 + 2 * i, PlayerView.EyeHeight, -14f);
        }
        bot = G.SpawnBot(new Vector3(-1.2f, 0, -9f));
        bot.FacingYaw = 180f;

        phase = Ph.Intro;
        phaseT = 0;
        lastRealUsec = Time.GetTicksUsec();
        GD.Print($"[xf] start seed={seed} quick={quick} current={CrosshairCode.Encode(cur)}");
    }

    /// <summary>The player picked the tab to test: set up its protocol, its colour candidates and its gun.</summary>
    void Choose(XfTab t)
    {
        tab = t;
        cfg = XfConfig.For(t, quick);
        BuildColorCandidates();
        foreach (var sc in cfg.Scenes)
            for (int c = 0; c < colors.Count; c++) visQueue.Add((c, sc, rng.Next()));
        Shuffle(visQueue);
        if (t == XfTab.Sniper) G.SwitchWeapon(WeaponKind.Operator);
        Event("xhair_tab", (int)t);
        GD.Print($"[xf] tab {XfTabs.Name(t)}: your {XfTabs.Name(t)} now = {CurrentLook(t)}; candidates: "
                 + string.Join(" | ", colors.Select(c => $"{c.Name}{(c.IsCurrent ? " (current)" : "")} contrast={c.Contrast:0.000}")));
        StartHeader(1);
    }

    /// <summary>Short description of the player's crosshair in a tab (log and start screen).</summary>
    string CurrentLook(XfTab t) => t switch
    {
        XfTab.Sniper => XfDot.Describe(cur.SniperDotFor()) + (cur.SniperIsOwn ? "" : " (VALORANT's default)"),
        XfTab.Ads when !cur.AdsIsOwn => "copies your primary",
        _ => CrosshairCode.ColorName(XfTabs.Style(t, cur).Color),
    };

    void RunSelfTest()
    {
        var fails = CrosshairCode.SelfTest(out int n);
        fails.AddRange(XfShapes.SelfTest(out int ns));
        n += ns;
        fails.AddRange(XfTabs.SelfTest(out int nt));
        n += nt;
        foreach (var f in fails) GD.Print($"[xf] FAIL {f}");
        GD.Print($"[xf] code self-test: {n - fails.Count}/{n} passed ({ns} shape checks, {nt} tab merge checks)");
        foreach (var (who, c) in CrosshairCode.ProCodes) GD.Print($"[xf]   {who}: {c}");
        foreach (var e in XfShapes.All(Colors.White, false))
            GD.Print($"[xf]   {e.Name} (white): {e.Code}  {string.Join("/", CrosshairView.Raster(e.Xhair.Primary, 7).Skip(1).Take(12).Select(r => r[1..13]))}");
        done = true;
        G.World.GetTree().Quit(fails.Count);
    }

    void Shuffle<T>(List<T> l)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }

    float Rf(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // =====================================================================================
    // Frame
    // =====================================================================================

    public override void Update(float dt)
    {
        if (cfg == null) return; // self-test run
        bool pressed = G.FirePressed;
        ulong nowUsec = Time.GetTicksUsec();
        float realGap = (nowUsec - lastRealUsec) / 1e6f;
        lastRealUsec = nowUsec;
        phaseT += dt;
        aimFwd = G.View.Forward;
        if (bot != null && GodotObject.IsInstanceValid(bot)) bot.Velocity = Vector3.Zero;
        // ADS / sniper rounds (and the look-around before them) are played aimed / scoped in; everything else hip fire
        bool aim = tab != XfTab.Primary && phase is Ph.Rest or Ph.Trial;
        if (G.ForcedScope != aim) G.ForcedScope = aim;

        // A pause (Esc / focus loss) inside a timed trial invalidates it.
        if (realGap > dt + 0.75f)
        {
            if (phase is Ph.VisWait or Ph.VisShow) { Notice("Trial restarted after the pause"); StartVisTrial(); return; }
            if (phase == Ph.Trial) { Notice("Round restarted after the pause"); StartRest(blockIdx); return; }
        }

        switch (phase)
        {
            case Ph.Intro:
                pickEye = G.View.Eye; // the start cards hang in front of the starting view (the player can't move here)
                // dev: --xftype picks after 3 s (the simulated player shoots the card itself; 8 s fallback); a plain dev
                // run waits on this screen
                if (devTabSet && phaseT > (simPlayer ? 8f : 3f)) Choose(devTab);
                break;
            case Ph.Header:
                if (phaseT >= cfg.PartHeaderSec)
                {
                    if (part == 1) StartVisTrial();
                    else BeginMatch();
                }
                break;
            case Ph.VisWait:
                if (pressed)
                {
                    falseStarts++;
                    // requeue this trial later (a few times at most, then it's simply dropped)
                    if (falseStarts <= 6) { var t = visQueue[visIdx]; visQueue.Add((t.Cond, t.Scene, rng.Next())); }
                    VisFeedback("TOO EARLY · wait for it");
                }
                else if (phaseT >= fore)
                {
                    phase = Ph.VisShow;
                    phaseT = 0;
                    onset = Now;
                    focusId++;
                    visPoint = G.View.Eye + G.View.Forward * 10f;
                }
                break;
            case Ph.VisShow:
            {
                var c = colors[visQueue[visIdx].Cond];
                if (pressed || Now - onset >= cfg.RtTimeout)
                {
                    bool hit = pressed && Now - onset < cfg.RtTimeout;
                    float rt = hit ? Mathf.Max(0.1f, Now - onset) : cfg.RtTimeout;
                    c.Rt.Add(rt);
                    if (!hit) c.Misses++;
                    Event("xhair_vis", visQueue[visIdx].Cond, hit ? rt * 1000f : -1f);
                    VisFeedback(hit ? $"{rt * 1000f:0} ms" : "MISSED");
                }
                break;
            }
            case Ph.VisFeedback:
                if (phaseT >= cfg.VisFeedback)
                {
                    visIdx++;
                    if (visIdx < visQueue.Count) StartVisTrial();
                    else FinishVisibility();
                }
                break;
            case Ph.VisResult:
                if (phaseT >= (cfg.BlocksPerMatch < 4 ? 2f : 5f) || (phaseT > 1.5f && pressed)) StartPick();
                break;
            case Ph.Pick:
                if (phaseT >= pickDeadline) { GD.Print("[xf] shape pick timed out"); StartBracket(); }
                break;
            case Ph.Rest:
                if (phaseT >= cfg.RestSec) StartBlock();
                break;
            case Ph.Trial:
                UpdateTrial();
                break;
            case Ph.Pref:
                if (phaseT >= cfg.PrefTimeout) EndMatch(-1);
                break;
            case Ph.Summary:
                if (devExit) { done = true; GD.Print("[xf] exit"); G.World.GetTree().Quit(); break; }
                if ((phaseT > 3f && pressed) || (Main.I.Dev && phaseT > 8f)) { done = true; G.TrialCrosshair = null; }
                break;
        }
    }

    void Notice(string s) { notice = s; noticeUntil = Now + 4f; }

    void StartHeader(int p)
    {
        part = p;
        phase = Ph.Header;
        phaseT = 0;
        G.TrialCrosshair = p == 1 ? Hidden : null;
        if (p == 2)
        {
            target.Visible = false;
            foreach (var t in prefT) t.Visible = false;
            if (bot != null && GodotObject.IsInstanceValid(bot)) bot.Visible = false;
        }
    }

    // =====================================================================================
    // Part 1: visibility
    // =====================================================================================

    /// <summary>
    /// Shortlist for part 1: the player's own colour/outline, the best computed colour with and without an outline
    /// (outlines are the other big visibility lever), then the next best colours, up to <see cref="XfConfig.ColorCandidates"/>.
    /// </summary>
    void BuildColorCandidates()
    {
        // the sniper dot has no outline
        bool[] outlines = tab == XfTab.Sniper ? new[] { false } : new[] { false, true };
        var bgs = XfVisibility.Backgrounds(G.Enemy);
        var all = new List<XfColor>();
        foreach (var col in CrosshairCode.Presets)
            foreach (bool o in outlines)
                all.Add(new XfColor { Color = col, Outline = o, Contrast = XfVisibility.Score(col, o, bgs, G.Enemy) });
        all.Sort((x, y) => y.Contrast.CompareTo(x.Contrast));

        Color curColor;
        bool curOutline = false;
        if (tab == XfTab.Sniper) curColor = new Color(cur.SniperDotFor().Color, 1f);
        else
        {
            var mine = XfTabs.Style(tab, cur);
            curColor = new Color(mine.Color, 1f);
            curOutline = mine.HasOutline && mine.OutlineOpacity > 0.05f && mine.OutlineThickness > 0;
        }
        colors.Add(new XfColor { Color = curColor, Outline = curOutline, IsCurrent = true, Contrast = XfVisibility.Score(curColor, curOutline, bgs, G.Enemy) });
        bool Has(Color c, bool o) => colors.Any(x => x.Color.ToRgba32() == c.ToRgba32() && x.Outline == o);
        bool HasColor(Color c) => colors.Any(x => x.Color.ToRgba32() == c.ToRgba32());
        var best = all[0];
        if (!Has(best.Color, best.Outline)) colors.Add(best);
        var flip = all.FirstOrDefault(x => x.Color.ToRgba32() == best.Color.ToRgba32() && x.Outline != best.Outline);
        if (flip != null && colors.Count < cfg.ColorCandidates && !Has(flip.Color, flip.Outline)) colors.Add(flip);
        foreach (var c in all)
        {
            if (colors.Count >= cfg.ColorCandidates) break;
            if (HasColor(c.Color)) continue; // one variant per further colour (its better outline choice comes first)
            colors.Add(c);
        }
    }

    void StartVisTrial()
    {
        fore = Rf(cfg.ForeMin, cfg.ForeMax);
        phase = Ph.VisWait;
        phaseT = 0;
        G.TrialCrosshair = Hidden;
    }

    void VisFeedback(string text)
    {
        visFeedback = text;
        phase = Ph.VisFeedback;
        phaseT = 0;
    }

    /// <summary>Combined colour score: 0.6 z(−mean ln RT) + 0.4 z(contrast), z across the tested colours.</summary>
    void FinishVisibility()
    {
        float[] Z(Func<XfColor, float> f)
        {
            var v = colors.Select(f).Select(x => float.IsNaN(x) ? 0 : x).ToArray();
            float m = v.Average(), sd = Mathf.Sqrt(v.Sum(x => (x - m) * (x - m)) / Math.Max(1, v.Length - 1));
            return v.Select(x => sd > 1e-6f ? (x - m) / sd : 0f).ToArray();
        }
        var zr = Z(c => -c.MeanLogRt);
        var zc = Z(c => c.Contrast);
        for (int i = 0; i < colors.Count; i++) colors[i].Score = 0.6f * zr[i] + 0.4f * zc[i];
        chosen = colors.OrderByDescending(c => c.Score).First();
        foreach (var c in colors)
            GD.Print($"[xf] colour {c.Name}{(c.IsCurrent ? " (current)" : "")}: n={c.Rt.Count} median={c.MedianMs:0}ms misses={c.Misses} contrast={c.Contrast:0.000} score={c.Score:+0.00;-0.00}");
        GD.Print($"[xf] colour pick: {chosen.Name} (false starts {falseStarts})");
        Event("xhair_color", CrosshairCode.PresetIndex(chosen.Color), chosen.MedianMs);

        // each candidate is the player's whole profile with only the tested tab replaced
        var raw = tab == XfTab.Sniper ? XfDots.All(chosen.Color) : XfShapes.All(chosen.Color, chosen.Outline);
        shapes = raw.Select(e => XfTabs.Into(tab, cur, e)).ToList();
        phase = Ph.VisResult;
        phaseT = 0;
        G.TrialCrosshair = null;
    }

    // =====================================================================================
    // Shape pick: shoot cards to pick 2–4 shapes (the recommended 4 start picked), then START
    // =====================================================================================

    // Cards lie on one flat plane facing the starting view (same size and spacing on screen), in units of the distance:
    // 0.31 ≈ 17° between card centres in the middle; the whole grid fits in VALORANT's 103° field of view.
    const float CardDist = 10f, CardW = 0.272f, CardH = 0.263f, CardStep = 0.31f;
    int PickedCount => sel.Count(b => b);
    /// <summary>The recommended candidates (picked when the pick screen opens).</summary>
    string[] Defaults => tab == XfTab.Sniper ? XfDots.Defaults : XfShapes.Defaults;
    string ShapeWord => tab == XfTab.Sniper ? "dot" : "shape";
    int StartCard => shapes.Count;

    /// <summary>Card <paramref name="i"/> (the last index is START) on the plane: centre and size (right / up, in units
    /// of <see cref="CardDist"/>). Five cards on the top row, the rest below, START under them.</summary>
    (float X, float Y, float W, float H) CardLayout(int i)
    {
        if (i >= shapes.Count) return (0f, -0.4f, 0.54f, 0.115f);
        int top = Math.Min(5, shapes.Count);
        bool row0 = i < top;
        int col = row0 ? i : i - top, nRow = row0 ? top : shapes.Count - top;
        return ((col - (nRow - 1) / 2f) * CardStep, row0 ? 0.167f : -0.13f, CardW, CardH);
    }

    /// <summary>Start screen card of tab <paramref name="i"/>: ADS left, PRIMARY (the default) in the middle where the
    /// crosshair starts, SNIPER right.</summary>
    static (float X, float Y, float W, float H) ChoiceLayout(int i) =>
        ((XfTab)i switch { XfTab.Ads => -0.47f, XfTab.Sniper => 0.47f, _ => 0f }, -0.02f, 0.42f, 0.40f);

    /// <summary>The card as a world-space rectangle: centre and right / up half-extents.</summary>
    (Vector3 C, Vector3 R, Vector3 U) CardQuad(int i) => Quad(CardLayout(i));

    (Vector3 C, Vector3 R, Vector3 U) Quad((float X, float Y, float W, float H) l)
    {
        var fwd = PlayerView.Dir(0, 0);
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd).Normalized();
        return (pickEye + (fwd + right * l.X + up * l.Y) * CardDist, right * (l.W / 2 * CardDist), up * (l.H / 2 * CardDist));
    }

    /// <summary>The card the ray points at, or -1.</summary>
    int CardHit(Vector3 o, Vector3 d) => Hit(o, d, shapes.Count + 1, CardLayout);

    int Hit(Vector3 o, Vector3 d, int count, Func<int, (float, float, float, float)> layout)
    {
        var n = PlayerView.Dir(0, 0);
        float den = d.Dot(n);
        if (den <= 1e-4f) return -1;
        for (int i = 0; i < count; i++)
        {
            var (c, r, u) = Quad(layout(i));
            var p = o + d * ((c - o).Dot(n) / den) - c;
            if (Mathf.Abs(p.Dot(r.Normalized())) <= r.Length() && Mathf.Abs(p.Dot(u.Normalized())) <= u.Length()) return i;
        }
        return -1;
    }

    /// <summary>Start screen: shooting a card picks the tab to test.</summary>
    float ChoiceShot()
    {
        if (phaseT < 0.8f) return float.PositiveInfinity;
        int i = Hit(G.View.Eye, aimFwd, 3, ChoiceLayout);
        if (i < 0) return float.PositiveInfinity;
        G.Sound("head", 0.8f);
        Choose((XfTab)i);
        return float.PositiveInfinity;
    }

    void StartPick()
    {
        G.Respawn(Vector3.Zero, 0f);
        pickEye = G.View.Eye;
        target.Visible = false;
        foreach (var t in prefT) t.Visible = false;
        if (bot != null && GodotObject.IsInstanceValid(bot)) bot.Visible = false;
        G.TrialCrosshair = null;
        sel = shapes.Select(e => Defaults.Contains(e.Key)).ToArray();

        // dev: --xfpick key,key,... (2–4 shape keys) fixes the picks; the simulated player then only shoots START
        string? forced = Main.I.Dev ? ArgAfter("--xfpick") : null;
        if (forced != null)
        {
            var keys = forced.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var f = shapes.Select(e => keys.Contains(e.Key)).ToArray();
            if (f.Count(b => b) is >= 2 and <= 4) sel = f;
            else GD.Print($"[xf] --xfpick needs 2–4 of: {string.Join(",", shapes.Select(e => e.Key))}");
        }
        simPlan.Clear();
        if (simPlayer)
        {
            if (forced == null)
            {
                // a random set of 2–4 shapes: drop and add cards (count stays within 2–4), then START
                var want = Enumerable.Range(0, shapes.Count).OrderBy(_ => rng.Next()).Take(2 + rng.Next(3)).ToHashSet();
                var drops = Enumerable.Range(0, shapes.Count).Where(i => sel[i] && !want.Contains(i)).ToList();
                var adds = Enumerable.Range(0, shapes.Count).Where(i => !sel[i] && want.Contains(i)).ToList();
                int count = PickedCount;
                while (drops.Count > 0 || adds.Count > 0)
                {
                    if (drops.Count > 0 && count > 2) { simPlan.Add(drops[0]); drops.RemoveAt(0); count--; }
                    else { simPlan.Add(adds[0]); adds.RemoveAt(0); count++; }
                }
            }
            simPlan.Add(StartCard);
        }
        focusId++;
        pickDeadline = cfg.PickTimeout;
        pickShotAt = -9f;
        phase = Ph.Pick;
        phaseT = 0;
        GD.Print($"[xf] shape pick: {string.Join(", ", shapes.Select((e, i) => (sel[i] ? "*" : "") + e.Key))}"
                 + (simPlan.Count > 0 ? $" · sim plan {string.Join(",", simPlan.Select(i => i == StartCard ? "START" : shapes[i].Key))}" : ""));
    }

    float PickShot(Vector3 o, Vector3 d)
    {
        // ignore a shot still in flight from part 1 and the rest of a held burst (the Vandal is automatic)
        if (phaseT < 0.5f || Now - pickShotAt < 0.3f) return float.PositiveInfinity;
        int i = CardHit(G.View.Eye, aimFwd);
        if (i < 0) return float.PositiveInfinity;
        pickShotAt = Now;
        if (simPlan.Count > 0 && simPlan[0] == i) { simPlan.RemoveAt(0); focusId++; }
        if (i == StartCard)
        {
            G.Sound("head", 0.8f);
            StartBracket();
            return float.PositiveInfinity;
        }
        if (sel[i] && PickedCount <= 2) { Notice($"Keep at least 2 {ShapeWord}s"); G.Sound("fail", 0.4f); }
        else if (!sel[i] && PickedCount >= 4) { Notice($"Up to 4 {ShapeWord}s: shoot a picked one to drop it first"); G.Sound("fail", 0.4f); }
        else
        {
            sel[i] = !sel[i];
            G.Sound("head", sel[i] ? 0.7f : 0.45f);
            pickDeadline = Mathf.Max(pickDeadline, phaseT + cfg.PickExtend);
        }
        return float.PositiveInfinity;
    }

    int BracketMatches => nPick switch { >= 4 => 3, 3 => 2, _ => 1 };

    /// <summary>The picked shapes enter the bracket in card order (the recommended four keep their old pairings: dot vs
    /// cross, framed dot vs dynamic). With 3, a random one gets a bye to the final; with 2 they meet in the final.</summary>
    void StartBracket()
    {
        entrants.Clear();
        for (int i = 0; i < shapes.Count; i++) if (sel[i]) entrants.Add(shapes[i]);
        nPick = entrants.Count;
        byeName = "";
        if (nPick == 3)
        {
            int bye = rng.Next(3);
            var b = entrants[bye];
            entrants.RemoveAt(bye);
            entrants.Insert(0, b);
            byeName = b.Name;
        }
        entrants.Add(XfShapes.Current(cur, tab));
        simPlan.Clear();
        totalBlocks = BracketMatches * cfg.BlocksPerMatch; // the bracket (+ the check, added when it starts)
        Event("xhair_pick", nPick, shapes.Select((e, i) => sel[i] ? 1 << i : 0).Sum());
        GD.Print($"[xf] picked {nPick}: {string.Join(", ", entrants.Take(nPick).Select(e => e.Name))}{(byeName.Length > 0 ? $" (bye: {byeName})" : "")}");
        StartHeader(2);
    }

    // =====================================================================================
    // Part 2: matches and blocks
    // =====================================================================================

    void BeginMatch()
    {
        var m = new XfMatch();
        int mi = matches.Count;
        if (mi >= BracketMatches) { m.Title = "FINAL CHECK"; m.IsCheck = true; m.Entrant[0] = Winner(BracketMatches - 1); m.Entrant[1] = nPick; }
        else if (nPick >= 4)
        {
            if (mi == 0) { m.Title = "SEMI-FINAL 1"; m.Entrant[0] = 0; m.Entrant[1] = 1; }
            else if (mi == 1) { m.Title = "SEMI-FINAL 2"; m.Entrant[0] = 2; m.Entrant[1] = 3; }
            else { m.Title = "FINAL"; m.Entrant[0] = Winner(0); m.Entrant[1] = Winner(1); }
        }
        else if (nPick == 3)
        {
            if (mi == 0) { m.Title = "SEMI-FINAL"; m.Entrant[0] = 1; m.Entrant[1] = 2; }
            else { m.Title = "FINAL"; m.Entrant[0] = 0; m.Entrant[1] = Winner(0); }
        }
        else { m.Title = "FINAL"; m.Entrant[0] = 0; m.Entrant[1] = 1; }
        bool aIs0 = rng.Next(2) == 0;
        m.Label[0] = aIs0 ? 'A' : 'B';
        m.Label[1] = aIs0 ? 'B' : 'A';
        int ca = aIs0 ? 0 : 1, cb = 1 - ca;
        bool abba = rng.Next(2) == 0;
        m.Order = cfg.BlocksPerMatch >= 4
            ? (abba ? new[] { ca, cb, cb, ca } : new[] { cb, ca, ca, cb })
            : (abba ? new[] { ca, cb } : new[] { cb, ca });
        matches.Add(m);
        match = m;
        GD.Print($"[xf] t={Now:0.0}s {m.Title}: {entrants[m.Entrant[0]].Name} ({m.Label[0]}) vs {entrants[m.Entrant[1]].Name} ({m.Label[1]}) order {string.Concat(m.Order.Select(c => m.Label[c]))}");
        StartRest(0);
    }

    int Winner(int matchIdx) => matches[matchIdx].Entrant[Math.Max(0, matches[matchIdx].Winner)];

    XfEntrant EntrantOf(int cond) => entrants[match!.Entrant[cond]];

    void StartRest(int bi)
    {
        var m = match!;
        blockIdx = bi;
        int cond = m.Order[bi];
        block = new XfBlock { Cond = cond, Label = m.Label[cond] };
        G.TrialCrosshair = EntrantOf(cond).Xhair;
        phase = Ph.Rest;
        phaseT = 0;
        trialLive = false;
        target.Visible = false;
        // a fresh agent for this block's kill (the previous one may be lying dead)
        if (deadBot != null && GodotObject.IsInstanceValid(deadBot)) G.Despawn(deadBot);
        deadBot = null;
        if (bot == null || !GodotObject.IsInstanceValid(bot) || bot.Dead)
        {
            if (bot != null && GodotObject.IsInstanceValid(bot)) G.Despawn(bot);
            bot = G.SpawnBot(new Vector3(0, -6f, 0));
        }
        bot.Visible = false;
    }

    void StartBlock()
    {
        G.Respawn(Vector3.Zero, 0f); // every block starts from the same view
        seq.Clear();
        for (int i = 0; i < cfg.Flicks; i++) seq.Add(TK.Flick);
        for (int i = 0; i < cfg.Micros; i++) seq.Add(TK.Micro);
        Shuffle(seq);
        for (int i = 0; i < cfg.Kills; i++) seq.Add(TK.Kill);
        trialIdx = 0;
        trialLive = false;
        phase = Ph.Trial;
        phaseT = 0;
    }

    float Timeout(TK k) => k switch { TK.Flick => cfg.FlickTimeout, TK.Micro => cfg.MicroTimeout, _ => cfg.KillTimeout };

    /// <summary>The next target waits for the Operator's bolt and scope (a target the gun can't shoot yet would only
    /// time the bolt). Other guns are always ready.</summary>
    bool GunReady => tab != XfTab.Sniper || G.Weapon == null || Now - lastShotAt >= G.Weapon.Interval + 0.25f;

    void UpdateTrial()
    {
        if (!trialLive)
        {
            if (phaseT > 0.08f) target.Visible = false; // the hit flash shows briefly, then the gap
            if (phaseT >= cfg.Foreperiod && GunReady) SpawnTrial();
            return;
        }
        if (Now - spawnAt >= Timeout(seq[trialIdx])) EndTrial(false);
    }

    void SpawnTrial()
    {
        var k = seq[trialIdx];
        focusId++;
        misses = 0;
        firstErr = -1;
        spawnAt = Now;
        trialLive = true;
        if (k == TK.Kill)
        {
            if (bot == null || !GodotObject.IsInstanceValid(bot) || bot.Dead) bot = G.SpawnBot(new Vector3(0, -6f, 0));
            float yaw = Mathf.Clamp(G.View.ViewYaw + (rng.Next(2) == 0 ? -1 : 1) * Rf(cfg.KillYawMin, cfg.KillYawMax), -50f, 50f);
            float dist = Rf(cfg.KillDistMin, cfg.KillDistMax);
            var dir = PlayerView.Dir(yaw, 0);
            bot!.Feet = new Vector3(dir.X * dist, 0f, dir.Z * dist);
            bot.FacingYaw = yaw + 180f;
            bot.Hp = BotCharacter.MaxHp;
            bot.Visible = true;
            bot.SpawnTime = Now;
            amp = G.View.AngleTo(bot.Head);
            rAng = Mathf.RadToDeg(Mathf.Atan2(BotCharacter.HeadRadius, dist));
            return;
        }
        bool micro = k == TK.Micro;
        amp = micro ? Rf(cfg.MicroAmpMin, cfg.MicroAmpMax) : Rf(cfg.FlickAmpMin, cfg.FlickAmpMax);
        float d = micro ? Rf(cfg.MicroDistMin, cfg.MicroDistMax) : Rf(cfg.FlickDistMin, cfg.FlickDistMax);
        target.Position = G.View.Eye + Place(amp) * d;
        target.SpawnTime = Now;
        target.Visible = true;
        rAng = Mathf.RadToDeg(Mathf.Atan2(target.Radius, d));
    }

    /// <summary>A direction <paramref name="a"/>° from the crosshair, in a random direction that keeps the head in a
    /// comfortable window (|yaw| ≤ 55°, pitch −3…12°).</summary>
    Vector3 Place(float a)
    {
        var fwd = G.View.Forward;
        var right = fwd.Cross(Vector3.Up).Normalized();
        var up = right.Cross(fwd).Normalized();
        float ar = Mathf.DegToRad(a);
        for (int i = 0; i < 24; i++)
        {
            float th = Rf(0, Mathf.Tau);
            if (i < 16 && Mathf.Abs(Mathf.Sin(th)) > 0.75f) continue; // mostly horizontal-ish, like real flicks
            var dir = (fwd * Mathf.Cos(ar) + (right * Mathf.Cos(th) + up * Mathf.Sin(th)) * Mathf.Sin(ar)).Normalized();
            float yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, -dir.Z)), pitch = Mathf.RadToDeg(Mathf.Asin(dir.Y));
            if (Mathf.Abs(yaw) <= 55f && pitch >= -3f && pitch <= 12f) return dir;
        }
        var mid = PlayerView.Dir(0, 4);
        var axis = fwd.Cross(mid);
        return axis.LengthSquared() < 1e-8f ? PlayerView.Dir(G.View.ViewYaw + a, G.View.ViewPitch) : fwd.Rotated(axis.Normalized(), ar).Normalized();
    }

    public override float OnBullet(Vector3 o, Vector3 d, bool accurate)
    {
        Shots++;
        lastShotAt = Now;
        if (phase == Ph.Intro) return ChoiceShot();
        if (phase == Ph.Pref) return PrefShot(o, d);
        if (phase == Ph.Pick) return PickShot(o, d);
        if (phase != Ph.Trial || !trialLive) return float.PositiveInfinity;
        var b = block!;
        b.Shots++;
        var k = seq[trialIdx];
        if (k == TK.Kill)
        {
            if (bot == null || !GodotObject.IsInstanceValid(bot)) return float.PositiveInfinity;
            if (firstErr < 0) firstErr = Mathf.RadToDeg(aimFwd.AngleTo((bot.Head - G.View.Eye).Normalized())) / Mathf.Max(0.01f, rAng);
            var zone = bot.Raycast(o, d, out float dist);
            if (zone == HitZone.None || dist >= G.BulletWallDist) { misses++; return float.PositiveInfinity; }
            Hits++; b.Hits++;
            if (zone == HitZone.Head) HeadHits++;
            LastShotZone = zone;
            bot.Hp -= (int)Mathf.Ceil(G.Weapon!.Damage((int)zone, dist));
            bot.OnHit(zone);
            Effects.BodyHit(o + d * dist, G.Enemy, zone == HitZone.Head);
            G.Sound(zone == HitZone.Head ? "head" : "body", zone == HitZone.Head ? 0.8f : 0.5f);
            if (bot.Hp <= 0) { bot.Die(d); EndTrial(true); }
            return dist;
        }
        // Flicks and micro-adjusts are judged on the crosshair ray (like the sens finder): bullet spread would only add
        // noise that has nothing to do with the crosshair. The kill above uses the real bullets.
        var toT = (target.GlobalPosition - G.View.Eye).Normalized();
        if (firstErr < 0) firstErr = Mathf.RadToDeg(aimFwd.AngleTo(toT)) / Mathf.Max(0.01f, rAng);
        float t = target.Ray(G.View.Eye, aimFwd);
        b.AimShots++;
        if (float.IsPositiveInfinity(t)) { misses++; return float.PositiveInfinity; }
        Hits++; b.Hits++; b.AimHits++;
        LastShotZone = HitZone.Head;
        target.Hit();
        G.Sound("head", 0.8f);
        EndTrial(true);
        return t;
    }

    void EndTrial(bool hit)
    {
        var k = seq[trialIdx];
        var b = block!;
        float mt = hit ? Now - spawnAt : Timeout(k);
        mt = Mathf.Max(0.08f, mt + 0.15f * misses);
        double id = SfStats.Id(Mathf.Max(0.5f, amp), Mathf.Max(0.05f, rAng));
        switch (k)
        {
            case TK.Flick:
                b.Flick.Add(Math.Log(id / mt));
                b.FlickMs.Add(mt * 1000f);
                break;
            case TK.Micro:
            {
                float err = firstErr < 0 ? 3f : Mathf.Min(firstErr, 3f);
                b.Micro.Add(Math.Log(id / mt) - 0.4 * err);
                b.MicroErr.Add(err);
                break;
            }
            default:
                b.Kill.Add(-Math.Log(mt));
                b.KillMs.Add(mt * 1000f);
                break;
        }
        if (!hit)
        {
            b.Timeouts++;
            if (misses == 0) { b.Shots++; if (k != TK.Kill) b.AimShots++; } // a timeout without a shot still counts as a failed attempt
            G.Sound("fail", 0.4f);
            target.Visible = false;
            if (k == TK.Kill && bot != null) bot.Visible = false;
        }
        if (k == TK.Kill && hit) { deadBot = bot; bot = null; }
        Event("xhair_t", (float)k, hit ? mt * 1000f : -1f);
        trialLive = false;
        trialIdx++;
        phaseT = 0;
        if (trialIdx >= seq.Count) EndBlock();
    }

    void EndBlock()
    {
        var m = match!;
        var b = block!;
        target.Visible = false;
        m.Blocks.Add(b);
        doneBlocks++;
        GD.Print($"[xf]   block {blockIdx + 1} {b.Label} {EntrantOf(b.Cond).Name}: flick n={b.Flick.Count} med={(b.FlickMs.Count > 0 ? Median(b.FlickMs) : 0):0}ms  micro n={b.Micro.Count} err={(b.MicroErr.Count > 0 ? b.MicroErr.Average() : 0):0.00}r  kill={(b.KillMs.Count > 0 ? b.KillMs.Average() : 0):0}ms  hits {b.Hits}/{b.Shots} (aim {b.AimHits}/{b.AimShots}) timeouts={b.Timeouts}");
        if (blockIdx + 1 < m.Order.Length)
        {
            // the bot for the next block needs to exist before the rest screen ends
            StartRest(blockIdx + 1);
            return;
        }
        StartPref();
    }

    // =====================================================================================
    // "Which felt better?" — shoot A, SAME or B
    // =====================================================================================

    void StartPref()
    {
        G.Respawn(Vector3.Zero, 0f);
        G.TrialCrosshair = null; // neutral: your own crosshair for the choice
        float[] yaws = { -13f, 0f, 13f };
        for (int i = 0; i < 3; i++)
        {
            prefT[i].Position = G.View.Eye + PlayerView.Dir(yaws[i], 2f) * 10f;
            prefT[i].Visible = true;
            prefT[i].SetHot(false);
        }
        if (bot != null && GodotObject.IsInstanceValid(bot)) bot.Visible = false;
        if (deadBot != null && GodotObject.IsInstanceValid(deadBot)) deadBot.Visible = false;
        focusId++;
        simPref = simPlayer ? rng.Next(3) : -1;
        phase = Ph.Pref;
        phaseT = 0;
    }

    /// <summary>Left = A, middle = no preference, right = B. Judged on the crosshair ray: an unscoped Operator's
    /// hip-fire spread must not pick the wrong answer.</summary>
    float PrefShot(Vector3 o, Vector3 d)
    {
        if (phaseT < 0.4f) return float.PositiveInfinity; // ignore a shot still in flight from the last block
        for (int i = 0; i < 3; i++)
        {
            float t = prefT[i].Ray(G.View.Eye, aimFwd);
            if (float.IsPositiveInfinity(t)) continue;
            prefT[i].Hit();
            G.Sound("head", 0.7f);
            var m = match!;
            int pref = i == 1 ? -1 : Array.IndexOf(m.Label, i == 0 ? 'A' : 'B');
            EndMatch(pref);
            return t;
        }
        return float.PositiveInfinity;
    }

    void EndMatch(int pref)
    {
        var m = match!;
        m.Pref = pref;
        foreach (var t in prefT) t.Visible = false;
        simPref = -1;
        var cmp = XfStats.Compare(m.Blocks);
        m.Result = cmp;
        double t0 = cmp.T;
        foreach (var b in m.Blocks) Event("xhair_trial", m.Entrant[b.Cond], (float)b.Score);
        if (m.IsCheck)
        {
            // the new crosshair (cond 0) has to hold up against yours (cond 1): yours wins only if clearly better,
            // or if it's close and you preferred it
            if (t0 <= -1) { m.Winner = 1; m.DecidedBy = "performance"; }
            else if (t0 < 1 && pref == 1) { m.Winner = 1; m.DecidedBy = "your preference"; }
            else { m.Winner = 0; m.DecidedBy = t0 >= 1 ? "performance" : pref == 0 ? "your preference" : "held up"; }
        }
        else if (Math.Abs(t0) >= 1) { m.Winner = t0 > 0 ? 0 : 1; m.DecidedBy = "performance"; }
        else if (pref >= 0) { m.Winner = pref; m.DecidedBy = "your preference"; }
        else { m.Winner = cmp.D >= 0 ? 0 : 1; m.DecidedBy = "slight edge"; }
        Event("xhair_match", matches.Count - 1, (float)t0);
        Event("xhair_pref", matches.Count - 1, pref);
        GD.Print($"[xf] {m.Title} result: d={cmp.D:+0.000;-0.000} SE={cmp.Se:0.000} t={t0:+0.00;-0.00} (flick {cmp.DFlick:+0.00;-0.00} micro {cmp.DTrack:+0.00;-0.00}) acc={m.Accuracy * 100:0}% pref={(pref < 0 ? "none" : m.Label[pref].ToString())} -> {EntrantOf(m.Winner).Name} ({m.DecidedBy})");

        // (Not for the dev simulated player: its pursuit model can't hold still on static heads in this drill, and the
        // dev run has to reach the end.)
        if (m.Accuracy < cfg.MinAccuracy && !simPlayer)
        {
            aborted = true;
            abortWhy = $"Accuracy fell to {m.Accuracy * 100:0}% (below {cfg.MinAccuracy * 100:0}%). Take a break and run it again later; this result is provisional.";
            GD.Print($"[xf] ABORT: {abortWhy}");
            Finish();
            return;
        }
        if (matches.Count < BracketMatches) { BeginMatch(); return; }
        if (matches.Count == BracketMatches)
        {
            if (!XfTabs.SameLook(tab, entrants[Winner(BracketMatches - 1)].Xhair, cur)) { totalBlocks += cfg.BlocksPerMatch; BeginMatch(); return; }
            GD.Print("[xf] the winner already is your crosshair: no check needed");
        }
        Finish();
    }

    // =====================================================================================
    // Result
    // =====================================================================================

    XfMatch? Check => matches.FirstOrDefault(m => m.IsCheck && m.Result != null);

    void Finish()
    {
        G.TrialCrosshair = null;
        target.Visible = false;
        foreach (var t in prefT) t.Visible = false;
        if (bot != null && GodotObject.IsInstanceValid(bot)) bot.Visible = false;
        int last = matches.Count(m => m.Result != null) - 1;
        int idx = last < 0 ? 0 : matches[last].Entrant[Math.Max(0, matches[last].Winner)];
        result = entrants.Count > 0 ? entrants[idx] : XfShapes.Current(cur, tab);
        keepCurrent = result.IsCurrent || XfTabs.SameLook(tab, result.Xhair, cur);
        // the player's whole profile with only the tested tab changed (or, kept, their own profile)
        code = result.Code;
        Main.I.Settings.LastFinderCrosshairCode = code; // copyable later from Settings → Crosshair
        Main.I.Settings.Save();                          // no-op in --dev (AppSettings.ReadOnly)
        var col = XfTabs.ColorOf(tab, result.Xhair);
        Event("xhair_result", idx, CrosshairCode.PresetIndex(col));
        GD.Print($"[xf] RESULT t={Now:0.0}s tab={XfTabs.Name(tab)} {result.Name}{(keepCurrent ? " (keep yours)" : "")} colour={CrosshairCode.ColorName(col)}"
                 + (tab == XfTab.Sniper ? $" dot={XfDot.Describe(result.Xhair.SniperDotFor())}" : $" outline={XfTabs.Style(tab, result.Xhair).HasOutline}")
                 + $" code={code} was={CrosshairCode.Encode(cur)} aborted={aborted}");
        phase = Ph.Summary;
        phaseT = 0;
    }

    string ResultName => result == null ? "" : result.IsCurrent ? result.Name
        : $"{CrosshairCode.ColorName(XfTabs.ColorOf(tab, result.Xhair))} {result.Name.ToLowerInvariant()}";

    /// <summary>What importing the code changes in VALORANT (result screen).</summary>
    string ChangeNote => tab switch
    {
        XfTab.Ads => "Only your ADS crosshair changes. After import, Use Advanced Options is on and Copy Primary Crosshair is off.",
        XfTab.Sniper => "Only your sniper scope dot changes. After import, Use Advanced Options is on.",
        _ => cur.UseAdvancedOptions && !cur.UsePrimaryForAds
            ? "Only your primary crosshair changes. Your ADS crosshair and sniper dot stay as they are."
            : "Only your primary crosshair changes (your ADS crosshair copies it). Your sniper dot stays as it is.",
    };

    public override IEnumerable<string> HudLines()
    {
        if (phase is Ph.VisWait or Ph.VisShow or Ph.VisFeedback)
            yield return $"Visibility · {Math.Min(visIdx + 1, visQueue.Count)} / {visQueue.Count}";
        else if (phase is Ph.Rest or Ph.Trial or Ph.Pref && match != null)
        {
            yield return $"{Title(match.Title)} · round {Math.Min(blockIdx + 1, match.Order.Length)} / {match.Order.Length}";
            if (phase == Ph.Trial) yield return $"Crosshair {block?.Label} · target {Math.Min(trialIdx + 1, seq.Count)} / {seq.Count}";
        }
        if (Now < noticeUntil) yield return notice;
    }

    static string Title(string s) => s.Length == 0 ? s : s[0] + s[1..].ToLowerInvariant();

    public override string? Prompt => phase switch
    {
        Ph.VisWait or Ph.VisShow when visIdx < 3 => "Click the moment you see the crosshair",
        Ph.Trial when trialLive && seq[trialIdx] == TK.Kill && doneBlocks < 2 => "Kill the agent",
        Ph.Trial when trialLive && doneBlocks < 2 => "Flick to the head and shoot it",
        _ => null,
    };

    /// <summary>Short values (≤ 24 chars); the code itself, the bracket and the colour test are on the summary screen.</summary>
    public override IEnumerable<(string Label, string Value)> ResultLines()
    {
        if (phase != Ph.Summary || result == null) yield break;
        if (aborted) yield return ("Result", "provisional (fatigue)");
        yield return ("Tested", tab switch { XfTab.Ads => "ADS crosshair", XfTab.Sniper => "sniper scope dot", _ => "primary crosshair" });
        yield return (tab == XfTab.Sniper ? "Dot" : "Crosshair", keepCurrent ? "keep yours" : Short(ResultName));
        bool outline = tab != XfTab.Sniper && XfTabs.Style(tab, result.Xhair).HasOutline;
        yield return ("Colour", Short($"{CrosshairCode.ColorName(XfTabs.ColorOf(tab, result.Xhair))}{(outline ? " + outline" : "")}"));
        if (chosen != null && !float.IsNaN(chosen.MedianMs)) yield return ("Spotted in", $"{chosen.MedianMs:0} ms (median)");
        var fin = matches.LastOrDefault(m => m.Result != null);
        if (fin != null) yield return ("Decided by", fin.DecidedBy);
        yield return ("In VALORANT", keepCurrent ? "nothing to change" : "Import Profile Code");
        if (!keepCurrent) yield return ("Code changes", $"{XfTabs.Name(tab)} tab only");
        if (!keepCurrent && tab != XfTab.Primary) yield return ("Advanced Options", "on after import");
        yield return ("Code", "Copy it with the button");
    }

    static string Short(string s) => s.Length <= 24 ? s : s[..23] + "…";

    public override IEnumerable<(string Label, Action Act)> ResultButtons()
    {
        if (phase != Ph.Summary || result == null) yield break;
        string c = code;
        yield return ("Copy VALORANT code", () =>
        {
            DisplayServer.ClipboardSet(c);
            GD.Print("[xf] code copied to the clipboard");
        });
        // The code is the player's profile with only the tested tab changed; ValTrainer applies just that tab on top of the
        // live VALORANT import (CrosshairCode.ApplyTab), so later changes to the other tabs in VALORANT still show.
        string tabName = CrosshairCode.TabName(tab switch { XfTab.Ads => CrosshairView.Mode.Ads, XfTab.Sniper => CrosshairView.Mode.Sniper, _ => CrosshairView.Mode.Primary });
        if (!keepCurrent && !aborted)
            yield return ("Use in ValTrainer", () =>
            {
                var s = Main.I.Settings;
                s.UseFinderCrosshair = true;
                s.FinderCrosshairCode = c;
                s.FinderCrosshairTab = tabName;
                s.Save(); // no-op in --dev (AppSettings.ReadOnly)
            });
        // Undoes "Use in ValTrainer" if it was clicked; otherwise nothing changes.
        yield return ("Keep my crosshair", () =>
        {
            var s = Main.I.Settings;
            if (s.UseFinderCrosshair == origUse && s.FinderCrosshairCode == origCode && s.FinderCrosshairTab == origTab) return;
            s.UseFinderCrosshair = origUse;
            s.FinderCrosshairCode = origCode;
            s.FinderCrosshairTab = origTab;
            s.Save();
        });
    }

    /// <summary>No rank badge: the drill measures which crosshair suits you, not skill.</summary>
    public override int Badge() => -1;
}
