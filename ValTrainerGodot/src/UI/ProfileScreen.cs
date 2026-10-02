using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Game;

namespace ValTrainer.UI;

/// <summary>
/// Aim coach / skill profile. Left: estimated overall rank (emblem, rank range, confidence, Iron→Radiant ladder)
/// and the 8-skill breakdown (radar + rows). Right: the problems the coach found as expandable cards (evidence,
/// why it happens, how to fix it, drill buttons), strengths, and the sensitivity recommendation. The profile is
/// built on a background thread (<see cref="CoachData"/>), so the screen opens instantly with a loading state.
/// </summary>
public partial class ProfileScreen : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    SkillProfile? profile;
    readonly HashSet<string> expanded = new();
    bool expandInit;
    DrawBox? spinner;
    float spinT;
    // sensitivity state before "Try it" (restored by undo; static so it survives leaving and reopening the screen)
    static bool? prevUse;
    static float prevSens;

    public override void _Ready()
    {
        CoachData.Request();
        profile = CoachData.Ready;
        base._Ready();
    }

    protected override void Tick(float dt)
    {
        if (profile != null) return;
        spinT += dt;
        spinner?.QueueRedraw();
        if (CoachData.Ready is { } p)
        {
            profile = p;
            Rebuild();
        }
    }

    protected override void Build()
    {
        float k = K;
        spinner = null;
        var root = Margins(VBox(16 * k), 48 * k, 26 * k, 48 * k, 30 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);
        col.AddChild(HeaderRow());

        if (profile == null)
        {
            col.AddChild(Loading());
            return;
        }
        var p = profile;
        if (!expandInit)
        {
            expandInit = true;
            if (p.Problems.Count > 0) expanded.Add(p.Problems[0].Id);
        }

        var body = HBox(24 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);
        float avail = GetViewportRect().Size.X - 96 * k - 24 * k;
        var left = VBox(16 * k);
        left.CustomMinimumSize = new Vector2(Mathf.Min(900 * k, avail * 0.5f), 0);
        body.AddChild(left);
        var right = VBox(16 * k);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(right);

        left.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 250 * k), OnDraw = d => DrawRank(d, p) });
        left.AddChild(SkillsPanel(p));
        right.AddChild(ProblemsPanel(p));
        right.AddChild(SensPanel(p));
    }

    // ---------------- header / loading ----------------

    Control HeaderRow()
    {
        float k = K;
        var head = HBox(16 * k);
        var title = Heading("AIM COACH", "YOUR ESTIMATED RANK · WHAT HOLDS YOU BACK · HOW TO FIX IT");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        if (CoachData.Fake && !Core.CmdLine.Showcase)
        {
            var tag = Lbl($"DEV · FAKE COACH DATA ({CoachData.FakeVariant!.ToUpperInvariant()})", UiTheme.HudWide, 13, UiTheme.Warn);
            tag.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            head.AddChild(tag);
        }
        var back = Btn("BACK", VButton.Look.Primary, Main.I.ShowMenu, 180, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        return head;
    }

    Control Loading()
    {
        var panel = new VPanel { K = K, SizeFlagsVertical = SizeFlags.ExpandFill };
        spinner = new DrawBox
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                var c = d.Size / 2;
                CoachDraw.Spinner(d, c - new Vector2(0, 24 * K), 26 * K, K, spinT);
                Gfx.TextC(d, UiTheme.HudWide, "ANALYSING YOUR RUNS…", c.X, c.Y + 40 * K, UiTheme.Fs(15, K), UiTheme.Dim);
            },
        };
        panel.AddChild(spinner);
        return panel;
    }

    // ---------------- overall rank ----------------

    void DrawRank(DrawBox d, SkillProfile p)
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, d.Size);
        Gfx.Plate(d, r, 16 * k, new Color(0.07f, 0.11f, 0.15f, 0.84f), new Color(UiTheme.Text, 0.1f));
        d.DrawRect(new Rect2(0, 0, 34 * k, 2 * k), UiTheme.Accent);
        bool ranked = CoachData.HasOverall(p);
        float t = p.OverallTier;
        var rc = ranked ? CoachRank.Col(t) : UiTheme.NoTier;

        // emblem with a confidence ring
        var ec = new Vector2(132 * k, r.Size.Y / 2);
        float er = 62 * k, ring = er * 1.45f;
        d.DrawCircle(ec, ring, new Color(rc, ranked ? 0.08f : 0.035f));
        d.DrawArc(ec, ring, 0, Mathf.Tau, 72, new Color(rc, ranked ? 0.25f : 0.16f), Mathf.Max(1, 2 * k), true);
        if (ranked)
        {
            float a0 = -Mathf.Pi / 2;
            d.DrawArc(ec, ring, a0, a0 + Mathf.Tau * Mathf.Clamp(p.OverallConfidence, 0.02f, 1f), 72, rc, Mathf.Max(2, 3.5f * k), true);
        }
        RankEmblem.Draw(d, ec, er, CoachRank.EmblemTier(ranked ? t : -1));

        float x0 = 268 * k, right = r.Size.X - 32 * k, tw = right - x0;
        int cs = UiTheme.Fs(14, k), ms = UiTheme.Fs(12, k);
        Gfx.Text(d, UiTheme.HudWide, "ESTIMATED RANK", x0, Gfx.Mid(46 * k, cs), cs, UiTheme.Dim);
        string runs = $"{p.RunsAnalyzed} RUN{(p.RunsAnalyzed == 1 ? "" : "S")} · {When(p.Updated)}";
        string meta = p.RunsAnalyzed > 0 ? "BASED ON " + runs : "NO COACHED RUNS YET";
        // Narrow (4:3 / 5:4) windows: shorten, or drop, the caption rather than overlap "ESTIMATED RANK".
        float room = tw - Gfx.TextW(UiTheme.HudWide, "ESTIMATED RANK", cs) - 16 * k;
        if (Gfx.TextW(UiTheme.HudWide, meta, ms) > room && p.RunsAnalyzed > 0) meta = runs;
        if (Gfx.TextW(UiTheme.HudWide, meta, ms) <= room)
            Gfx.TextR(d, UiTheme.HudWide, meta, right, Gfx.Mid(46 * k, ms), ms, UiTheme.Faint);

        int fs = UiTheme.Fs(15, k);
        if (ranked)
        {
            var (lo, hi) = CoachRank.Range(t, p.OverallConfidence);
            RangeTitle(d, lo, hi, x0, 114 * k, tw, k);
            float y = 148 * k;
            float bw = CoachDraw.ConfBars(d, new Vector2(x0, y), k, p.OverallConfidence);
            string conf = $"{CoachRank.Confidence(p.OverallConfidence)} CONFIDENCE";
            float cw = Gfx.TextW(UiTheme.HudWide, conf, fs);
            Gfx.Text(d, UiTheme.HudWide, conf, x0 + bw + 10 * k, Gfx.Mid(y, fs), fs, CoachRank.ConfidenceColor(p.OverallConfidence));
            float nx = x0 + bw + 10 * k + cw + 14 * k;
            Gfx.TextFit(d, UiTheme.Body, $"·   most likely {Pretty(CoachRank.Name(t))}", nx, Gfx.Mid(y, fs), fs, UiTheme.Dim, right - nx);
        }
        else
        {
            Gfx.Text(d, UiTheme.Display, "UNRANKED", x0, 114 * k, UiTheme.Fs(64, k), UiTheme.Dim);
            int rated = p.Skills.Count(CoachRank.Rated);
            int need = CoachData.RunsNeeded();
            string msg = rated == 0
                ? $"Not enough data yet — the coach needs about {(need > 0 ? need : 3)} more runs."
                : $"{rated} of 8 skills rated — 4 with medium confidence unlock your overall rank.";
            Gfx.TextFit(d, UiTheme.Body, msg, x0, Gfx.Mid(148 * k, fs), fs, UiTheme.Dim, tw);
        }
        CoachDraw.Ladder(d, new Rect2(x0, 182 * k, tw, 12 * k), k, ranked ? t : -1, p.OverallConfidence, true);
    }

    static void RangeTitle(CanvasItem d, string lo, string? hi, float x, float baseline, float maxW, float k)
    {
        const string sep = "  –  ";
        var f = UiTheme.Display;
        int fs = UiTheme.Fs(66, k);
        float W(int s) => Gfx.TextW(f, lo, s) + (hi == null ? 0 : Gfx.TextW(f, sep, s) + Gfx.TextW(f, hi, s));
        while (fs > 20 && W(fs) > maxW) fs -= 2;
        Gfx.Text(d, f, lo, x, baseline, fs, CoachRank.Col(lo));
        if (hi == null) return;
        x += Gfx.TextW(f, lo, fs);
        Gfx.Text(d, f, sep, x, baseline, fs, UiTheme.Faint);
        x += Gfx.TextW(f, sep, fs);
        Gfx.Text(d, f, hi, x, baseline, fs, CoachRank.Col(hi));
    }

    static string Pretty(string rank) => rank.Length == 0 ? rank : rank[0] + rank[1..].ToLowerInvariant();

    static string When(DateTime u)
    {
        var age = DateTime.Now - u;
        if (age.TotalMinutes < 1) return "JUST NOW";
        if (u.Date == DateTime.Today) return "TODAY " + u.ToString("HH:mm", Inv);
        return u.ToString("MMM d", Inv).ToUpperInvariant();
    }

    // ---------------- skills ----------------

    Control SkillsPanel(SkillProfile p)
    {
        float k = K;
        int rated = p.Skills.Count(CoachRank.Rated);
        var panel = new VPanel { Title = "SKILL BREAKDOWN", Caption = $"{rated} of 8 rated · hover a skill for details", K = k, SizeFlagsVertical = SizeFlags.ExpandFill };
        var h = HBox(14 * k);
        panel.AddChild(h);
        h.AddChild(new DrawBox { CustomMinimumSize = new Vector2(388 * k, 0), OnDraw = d => DrawRadar(d, p) });
        var rows = VBox(4 * k);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(rows);
        int i = 0;
        foreach (var s in Enum.GetValues<Skill>())
            rows.AddChild(new SkillRow { Skill = s, Rating = p.Skills.FirstOrDefault(r => r.Skill == s), K = k, Stripe = i++ % 2 == 0 });
        return panel;
    }

    void DrawRadar(DrawBox d, SkillProfile p)
    {
        float k = K;
        // Leave room for the widest side label (the 10 px font floor makes labels relatively wider in small windows,
        // which used to push "TRACKING / DIAMOND 1" under the skill list at 1024×768).
        int nameFs = UiTheme.Fs(15, k), rankFs = UiTheme.Fs(12.5f, k);
        float labelW = 0;
        foreach (var sk in Enum.GetValues<Skill>())
        {
            var rating = p.Skills.FirstOrDefault(x => x.Skill == sk);
            labelW = Mathf.Max(labelW, Gfx.TextW(UiTheme.Display, CoachSkills.Short(sk), nameFs));
            if (CoachRank.Rated(rating)) labelW = Mathf.Max(labelW, Gfx.TextW(UiTheme.Display, CoachRank.Name(rating!.Tier), rankFs));
        }
        float margin = Mathf.Max(74 * k, labelW + 20 * k);
        float R = Mathf.Max(30 * k, Mathf.Min(d.Size.X / 2 - margin, d.Size.Y / 2 - 70 * k));
        var c = new Vector2(d.Size.X / 2, d.Size.Y / 2 - 14 * k);
        CoachDraw.Radar(d, c, R, k, p);
        int ls = UiTheme.Fs(12, k);
        float ly = c.Y + R + 62 * k;
        if (ly > d.Size.Y - 4 * k) return;
        string legend = CoachData.HasOverall(p) ? "dashed ring = your overall rank" : "centre = Iron · rim = Radiant";
        Gfx.TextC(d, UiTheme.Body, legend, d.Size.X / 2, ly, ls, UiTheme.Faint);
    }

    // ---------------- problems & strengths ----------------

    Control ProblemsPanel(SkillProfile p)
    {
        float k = K;
        bool overall = CoachData.HasOverall(p), any = p.Problems.Count > 0;
        var panel = new VPanel
        {
            Title = any || overall ? "WHAT TO FIX" : "GET RATED",
            Caption = any ? $"{p.Problems.Count} found · most important first · click to expand" : null,
            K = k, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);
        var list = VBox(12 * k);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var pad = Margins(list, 0, 2 * k, 14 * k, 6 * k);
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(pad);

        int idx = 1;
        foreach (var d in p.Problems) list.AddChild(ProblemCard(d, idx++, p));
        if (!overall)
        {
            if (any) list.AddChild(Spacer(0, 4 * k));
            list.AddChild(GetRated(p, any || p.Skills.Any(CoachRank.Rated)));
        }
        else if (!any)
            list.AddChild(Para("No problems found in your recent runs — solid fundamentals. Keep playing: the coach re-checks after every run.",
                UiTheme.Body, 16, UiTheme.Text));

        if (p.Strengths.Count > 0)
        {
            list.AddChild(Spacer(0, 2 * k));
            list.AddChild(new SectionLabel { Text = "WHAT YOU DO WELL", K = k });
            foreach (var s in p.Strengths) list.AddChild(Bullet(s, UiTheme.Good, check: true));
        }
        return panel;
    }

    Control ProblemCard(Diagnosis d, int index, SkillProfile p)
    {
        float k = K;
        var skill = p.Skills.FirstOrDefault(s => s.Skill == d.Skill);
        var frame = new CardFrame { K = k, Strip = CoachSkills.SeverityColor(d.Severity), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var v = VBox(0);
        frame.AddChild(v);
        bool open = expanded.Contains(d.Id);
        var header = new ProblemHeader
        {
            D = d, Index = index, Possible = d.Severity != Severity.Tip && skill != null && skill.Confidence < 0.5f, Expanded = open, K = k,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        header.CustomMinimumSize = new Vector2(0, header.WantedHeight);
        v.AddChild(header);
        var details = Margins(Details(d), 24 * k, 0, 24 * k, 20 * k);
        details.Visible = open;
        v.AddChild(details);
        header.Pressed += () =>
        {
            bool now = !details.Visible;
            details.Visible = now;
            header.Expanded = now;
            header.CustomMinimumSize = new Vector2(0, header.WantedHeight);
            header.QueueRedraw();
            if (now) expanded.Add(d.Id); else expanded.Remove(d.Id);
        };
        return frame;
    }

    Control Details(Diagnosis d)
    {
        float k = K;
        var v = VBox(6 * k);
        if (d.Evidence.Length > 0) v.AddChild(Para(d.Evidence, UiTheme.Body, 16.5f, UiTheme.Text));
        if (d.Why.Length > 0)
        {
            v.AddChild(Spacer(0, 2 * k));
            v.AddChild(new SectionLabel { Text = "WHY IT HAPPENS", K = k });
            v.AddChild(Para(d.Why, UiTheme.Body, 15, new Color(UiTheme.Text, 0.78f)));
        }
        if (d.Fixes is { Length: > 0 })
        {
            v.AddChild(Spacer(0, 2 * k));
            v.AddChild(new SectionLabel { Text = "HOW TO FIX", K = k });
            foreach (var f in d.Fixes) v.AddChild(Bullet(f, UiTheme.Accent));
        }
        var drills = (d.Drills ?? Array.Empty<string>()).Where(CoachData.HasDrill).Distinct().Take(3).ToList();
        if (drills.Count > 0)
        {
            v.AddChild(Spacer(0, 6 * k));
            var row = HBox(10 * k);
            var cap = Lbl("PRACTISE", UiTheme.HudWide, 13, UiTheme.Dim);
            cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(cap);
            row.AddChild(Spacer(4 * k, 0));
            foreach (var key in drills)
            {
                string name = CoachData.DrillName(key).ToUpperInvariant();
                float w = Gfx.TextW(UiTheme.Display, name, UiTheme.Fs(18, k)) / k + 60;
                var b = Btn(name, VButton.Look.Secondary, () => CoachData.StartDrill(key, CoachData.FocusFrom(d)), Mathf.Max(150, w), 42, 18);
                b.TooltipText = $"Start {CoachData.DrillName(key)} now";
                row.AddChild(b);
            }
            v.AddChild(row);
        }
        return v;
    }

    Control GetRated(SkillProfile p, bool partial)
    {
        float k = K;
        var v = VBox(10 * k);
        int need = CoachData.RunsNeeded();
        if (partial) v.AddChild(new SectionLabel { Text = "UNLOCK YOUR OVERALL RANK", K = k });
        v.AddChild(Para(
            $"Play Head Flicks, Strafe Tracking, Peek Practice, Counter-Strafe and Vandal Spray a few times — the coach needs about {(need > 0 ? need : 3)} more run{(need == 1 ? "" : "s")} to estimate your rank. Every run is analysed automatically.",
            UiTheme.Body, 16, UiTheme.Text));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", (int)(10 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(10 * k));
        foreach (var (key, target, rates) in CoachData.Starter)
            if (CoachData.HasDrill(key))
                grid.AddChild(new DrillTile { Key = key, Target = target, Rates = rates, Runs = CoachData.CoachedRuns(key), K = k });
        v.AddChild(grid);
        if (Main.I.Dev && CoachData.LastError is { } err) v.AddChild(Para($"Coach error: {err}", UiTheme.Body, 13, UiTheme.Warn));
        return v;
    }

    // ---------------- sensitivity ----------------

    Control SensPanel(SkillProfile p)
    {
        float k = K;
        var a = p.Sens;
        bool has = a != null && a.RecommendedSens > 0;
        var panel = new VPanel
        {
            Title = "SENSITIVITY",
            Caption = has ? $"recommendation · {CoachRank.Confidence(a!.Confidence).ToLowerInvariant()} confidence" : "not enough data yet",
            K = k,
        };
        var v = VBox(10 * k);
        panel.AddChild(v);
        v.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 112 * k), OnDraw = d => DrawSens(d, p) });
        string reason = has
            ? a!.Reason
            : "Not enough flick and tracking data for a recommendation yet. Play Head Flicks and Strafe Tracking a few times — or run the Sens Finder for a direct blind A/B test.";
        if (reason.Length > 0) v.AddChild(Para(reason, UiTheme.Body, 15, new Color(UiTheme.Text, 0.85f)));

        var row = HBox(12 * k);
        v.AddChild(row);
        if (has && a!.Direction != "keep")
        {
            var tb = new VButton { Kind = VButton.Look.Primary, K = k, FontPx = 19, SubPx = 11.5f, CustomMinimumSize = new Vector2(280 * k, 48 * k) };
            tb.Pressed += () => ToggleTry(tb, a);
            ConfigureTry(tb, a);
            row.AddChild(tb);
        }
        var finder = Btn("RUN SENS FINDER", VButton.Look.Secondary, () => CoachData.StartDrill("sensfinder"), 220, 48, 19);
        finder.TooltipText = "Blind A/B test of sensitivities with flicks + tracking (about 15 minutes)";
        finder.Disabled = !CoachData.HasDrill("sensfinder");
        row.AddChild(finder);
        var note = Para("ValTrainer can't change Valorant's settings — set it in Valorant: Settings → General → Mouse → Sensitivity.",
            UiTheme.Body, 12.5f, UiTheme.Faint);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(note);
        return panel;
    }

    static float Rounded(float sens) => MathF.Round(sens * 1000f) / 1000f;

    static bool TryActive(SensAdvice a)
    {
        var st = Main.I.Settings;
        return st.UseSensOverride && MathF.Abs(st.SensOverride - Rounded(a.RecommendedSens)) < 0.0006f;
    }

    void ConfigureTry(VButton b, SensAdvice a)
    {
        bool on = TryActive(a);
        b.Kind = on ? VButton.Look.Select : VButton.Look.Primary;
        b.Selected = on;
        b.Label = on ? "ACTIVE IN VALTRAINER" : "TRY IT IN VALTRAINER";
        b.Sub = on ? "click to switch back" : null;
        b.TooltipText = on
            ? "ValTrainer is using the recommended sens. Click to go back to your previous sens."
            : $"Use {Rounded(a.RecommendedSens).ToString("0.000", Inv)} in ValTrainer's drills (your Valorant settings stay unchanged)";
        b.QueueRedraw();
    }

    void ToggleTry(VButton b, SensAdvice a)
    {
        var st = Main.I.Settings;
        if (TryActive(a))
        {
            st.UseSensOverride = prevUse ?? false;
            if (prevUse == true) st.SensOverride = prevSens;
        }
        else
        {
            prevUse = st.UseSensOverride;
            prevSens = st.SensOverride;
            st.UseSensOverride = true;
            st.SensOverride = Rounded(a.RecommendedSens);
        }
        CoachData.SaveSettings();
        ConfigureTry(b, a);
    }

    void DrawSens(DrawBox d, SkillProfile p)
    {
        float k = K;
        var app = Main.I;
        var a = p.Sens;
        bool has = a != null && a.RecommendedSens > 0;
        float cur = has && a!.CurrentSens > 0 ? a.CurrentSens : app.Sens;
        int dpi = has && a!.Dpi > 0 ? a.Dpi : app.Settings.Dpi;
        float curCm = has && a!.CurrentCm360 > 0 ? a.CurrentCm360 : PlayerView.Cm360(cur, dpi);
        int cap = UiTheme.Fs(12, k), big = UiTheme.Fs(58, k), sub = UiTheme.Fs(14, k);

        void Block(float x, string caption, float sens, float cm, Color c)
        {
            Gfx.Text(d, UiTheme.HudWide, caption, x, Gfx.Mid(10 * k, cap), cap, UiTheme.Dim);
            Gfx.Text(d, UiTheme.Display, sens.ToString("0.000", Inv), x - 2 * k, 76 * k, big, c);
            Gfx.Text(d, UiTheme.Body, $"{sens * dpi:0} eDPI · {cm:0.0} cm/360", x, Gfx.Mid(100 * k, sub), sub, UiTheme.Dim);
        }

        Block(0, has ? "CURRENT" : "CURRENT · " + (app.Settings.UseSensOverride ? "VALTRAINER" : "FROM VALORANT"), cur, curCm, UiTheme.Text);
        float colW = 190 * k, ax = colW + 46 * k, rx = colW + 104 * k;
        float? recCm = null;
        if (has)
        {
            float rec = a!.RecommendedSens;
            float rcm = a.RecommendedCm360 > 0 ? a.RecommendedCm360 : PlayerView.Cm360(rec, dpi);
            string dir = a.Direction;
            if (dir != "keep") recCm = rcm;
            var dc = dir == "keep" ? UiTheme.Good : UiTheme.Warn;
            float ay = 44 * k;
            if (dir == "lower")
            {
                Gfx.ChevronDown(d, new Vector2(ax, ay - 8 * k), 34 * k, 16 * k, 6 * k, dc);
                Gfx.ChevronDown(d, new Vector2(ax, ay + 6 * k), 34 * k, 16 * k, 6 * k, new Color(dc, 0.55f));
            }
            else if (dir == "higher")
            {
                Gfx.ChevronDown(d, new Vector2(ax, ay + 6 * k), 34 * k, -16 * k, -6 * k, dc);
                Gfx.ChevronDown(d, new Vector2(ax, ay - 8 * k), 34 * k, -16 * k, -6 * k, new Color(dc, 0.55f));
            }
            else
            {
                d.DrawRect(new Rect2(ax - 14 * k, ay - 8 * k, 28 * k, 5 * k), dc);
                d.DrawRect(new Rect2(ax - 14 * k, ay + 4 * k, 28 * k, 5 * k), dc);
            }
            int ps = UiTheme.Fs(20, k), ls = UiTheme.Fs(11, k);
            string pct = dir == "keep" || cur <= 0 ? "KEEP" : $"{(rec / cur - 1f) * 100f:+0;-0}%";
            Gfx.TextC(d, UiTheme.Display, pct, ax, Gfx.Mid(80 * k, ps), ps, dc);
            if (dir != "keep") Gfx.TextC(d, UiTheme.HudWide, dir.ToUpperInvariant(), ax, Gfx.Mid(100 * k, ls), ls, UiTheme.Faint);
            string capText = dir != "keep" ? "RECOMMENDED" : a.Confidence < 0.5f ? "KEEP FOR NOW" : "KEEP YOUR SENS";
            Block(rx, capText, rec, rcm, dir == "keep" ? UiTheme.Text : UiTheme.Good);
        }
        else
        {
            int ns = UiTheme.Fs(22, k);
            Gfx.Text(d, UiTheme.HudWide, "RECOMMENDED", rx, Gfx.Mid(10 * k, cap), cap, UiTheme.Dim);
            Gfx.Text(d, UiTheme.Display, "NOT YET", rx, Gfx.Mid(50 * k, ns), ns, UiTheme.Faint);
        }

        float px0 = rx + 232 * k, px1 = d.Size.X - 6 * k;
        if (px1 - px0 >= 150 * k) ProBar(d, new Rect2(px0, 0, px1 - px0, d.Size.Y), k, curCm, recCm);
    }

    /// <summary>cm/360 on a log scale with the pro range (≈33–87 cm, median ≈54 cm) and your current / new sens.</summary>
    static void ProBar(CanvasItem d, Rect2 r, float k, float curCm, float? recCm)
    {
        const float lo = 15f, hi = 100f;
        float X(float cm) => r.Position.X + Mathf.Log(Mathf.Clamp(cm, lo, hi) / lo) / Mathf.Log(hi / lo) * r.Size.X;
        int cap = UiTheme.Fs(12, k), ls = UiTheme.Fs(11, k), ts = UiTheme.Fs(12, k);
        Gfx.Text(d, UiTheme.HudWide, "CM / 360 VS PROS", r.Position.X, Gfx.Mid(10 * k, cap), cap, UiTheme.Dim);
        float by = r.Position.Y + 50 * k, bh = 6 * k;
        d.DrawRect(new Rect2(r.Position.X, by, r.Size.X, bh), new Color(UiTheme.Text, 0.08f));
        d.DrawRect(new Rect2(X(33), by, X(87) - X(33), bh), new Color(UiTheme.Teal, 0.45f));
        d.DrawRect(new Rect2(X(54) - 1 * k, by - 4 * k, 2 * k, bh + 8 * k), UiTheme.Teal);
        foreach (int cm in new[] { 20, 30, 50, 80 })
            Gfx.TextC(d, UiTheme.Hud, cm.ToString(Inv), X(cm), Gfx.Mid(by + 20 * k, ts), ts, UiTheme.Faint);
        Gfx.Text(d, UiTheme.Body, "pros 33–87 cm · median 54", r.Position.X, Gfx.Mid(by + 46 * k, ts), ts, UiTheme.Teal);

        void Marker(float cm, string label, Color c, bool leftAlign)
        {
            float x = X(cm);
            Gfx.Diamond(d, new Vector2(x, by - 8 * k), 5.5f * k, 6.5f * k, c);
            if (leftAlign) Gfx.Text(d, UiTheme.HudWide, label, x - 3 * k, Gfx.Mid(by - 24 * k, ls), ls, c);
            else Gfx.TextR(d, UiTheme.HudWide, label, x + 3 * k, Gfx.Mid(by - 24 * k, ls), ls, c);
        }
        bool recRight = recCm is { } rc0 && rc0 >= curCm;
        Marker(curCm, "NOW", UiTheme.Text, recCm == null || !recRight);
        if (recCm is { } rc) Marker(rc, "NEW", UiTheme.Good, recRight);
    }

    // ---------------- small builders ----------------

    Label Para(string text, Font f, float px, Color c)
    {
        var l = Lbl(text, f, px, c);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.CustomMinimumSize = new Vector2(60 * K, 0);
        return l;
    }

    Control Bullet(string text, Color c, bool check = false)
    {
        float k = K;
        var h = HBox(10 * k);
        var dot = new DrawBox
        {
            CustomMinimumSize = new Vector2(16 * k, 22 * k),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            OnDraw = d =>
            {
                if (!check) { Gfx.Diamond(d, new Vector2(7 * k, 12 * k), 4 * k, 4 * k, c); return; }
                Span<Vector2> pts = stackalloc Vector2[3];
                pts[0] = new(2 * k, 12 * k); pts[1] = new(6.5f * k, 16.5f * k); pts[2] = new(14 * k, 6 * k);
                d.Polyline(pts, c, Mathf.Max(1.5f, 2.4f * k), true);
            },
        };
        h.AddChild(dot);
        h.AddChild(Para(text, UiTheme.Body, 15.5f, UiTheme.Text));
        return h;
    }
}
