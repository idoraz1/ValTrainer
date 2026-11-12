using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
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
        PollShowMe();
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
            if (Main.I.Dev && Core.CmdLine.After("--showme") is { } sc) expanded.Add(sc); // dev: open the card being shown
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

        left.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 268 * k), OnDraw = d => DrawRank(d, p), MouseFilter = MouseFilterEnum.Pass,
            TooltipText = L.T("An aim-only estimate: ValTrainer rates your mechanics (aim, movement, flash dodging).\nYour in-game rank also depends on game sense, comms and utility."),
        });
        left.AddChild(SkillsPanel(p));
        right.AddChild(RankGapPanel(p));
        right.AddChild(ProblemsPanel(p));
        right.AddChild(SensPanel(p));
        if (Main.I.Dev && Core.CmdLine.Has("--rank-picker") && !devPickerDone)
        {
            devPickerDone = true; // dev screenshots: --screen profile --rank-picker
            Callable.From(OpenRankPicker).CallDeferred();
        }
    }

    static bool devPickerDone;

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
            var tag = Lbl(L.Raw($"DEV · FAKE COACH DATA ({CoachData.FakeVariant!.ToUpperInvariant()})"), UiTheme.HudWide, 13, UiTheme.Warn); // dev only
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
        string caption = L.T("ESTIMATED RANK · AIM ONLY");
        Gfx.Text(d, UiTheme.HudWide, caption, x0, Gfx.Mid(46 * k, cs), cs, UiTheme.Dim);
        string whenText = When(p.Updated);
        string runs = L.P(p.RunsAnalyzed, $"{p.RunsAnalyzed} RUN · {whenText}", $"{p.RunsAnalyzed} RUNS · {whenText}");
        string meta = p.RunsAnalyzed > 0 ? L.F($"BASED ON {runs}") : L.T("NO COACHED RUNS YET");
        // Narrow (4:3 / 5:4) windows: shorten, or drop, the caption rather than overlap "ESTIMATED RANK".
        float room = tw - Gfx.TextW(UiTheme.HudWide, caption, cs) - 16 * k;
        if (Gfx.TextW(UiTheme.HudWide, meta, ms) > room && p.RunsAnalyzed > 0) meta = runs;
        if (Gfx.TextW(UiTheme.HudWide, meta, ms) <= room)
            Gfx.TextR(d, UiTheme.HudWide, meta, right, Gfx.Mid(46 * k, ms), ms, UiTheme.Faint);

        int fs = UiTheme.Fs(15, k);
        if (ranked)
        {
            var (lo, hi) = CoachRank.RangeTiers(t, p.OverallConfidence);
            RangeTitle(d, lo, hi, x0, 114 * k, tw, k);
            float y = 148 * k;
            float bw = CoachDraw.ConfBars(d, new Vector2(x0, y), k, p.OverallConfidence);
            string conf = CoachRank.DataCaps(p.OverallConfidence); // how much data, not how accurate (was "HIGH CONFIDENCE")
            float cw = Gfx.TextW(UiTheme.HudWide, conf, fs);
            Gfx.Text(d, UiTheme.HudWide, conf, x0 + bw + 10 * k, Gfx.Mid(y, fs), fs, CoachRank.ConfidenceColor(p.OverallConfidence));
            float nx = x0 + bw + 10 * k + cw + 14 * k;
            Gfx.TextFit(d, UiTheme.Body, L.F($"·   most likely {Pretty(t)}"), nx, Gfx.Mid(y, fs), fs, UiTheme.Dim, right - nx);
        }
        else
        {
            Gfx.Text(d, UiTheme.Display, "UNRANKED", x0, 114 * k, UiTheme.Fs(64, k), UiTheme.Dim);
            int rated = p.Skills.Count(CoachRank.Rated);
            int need = CoachData.RunsNeeded(), more = need > 0 ? need : 3;
            string msg = rated == 0
                ? L.P(more, $"Not enough data yet — the coach needs about {more} more run.", $"Not enough data yet — the coach needs about {more} more runs.")
                : L.F($"{rated} of 8 skills rated — 4 with medium confidence unlock your overall rank.");
            Gfx.TextFit(d, UiTheme.Body, msg, x0, Gfx.Mid(148 * k, fs), fs, UiTheme.Dim, tw);
        }
        CoachDraw.Ladder(d, new Rect2(x0, 182 * k, tw, 12 * k), k, ranked ? t : -1, p.OverallConfidence, true,
            actual: ActualRank.IsSet ? ActualRank.Tier(ActualRank.Value) : null);
        if (ranked)
        {
            int ns = UiTheme.Fs(13, k);
            Gfx.TextFit(d, UiTheme.Body, "Aim only: your rank also depends on game sense, utility and teamwork.", x0, Gfx.Mid(242 * k, ns), ns, UiTheme.Faint, tw);
        }
    }

    /// <summary>"GOLD 2  –  PLATINUM 1" from the range's tiers (the names translated, each in its rank colour).</summary>
    static void RangeTitle(CanvasItem d, float loT, float? hiT, float x, float baseline, float maxW, float k)
    {
        const string sep = "  –  ";
        string lo = CoachRank.Name(loT);
        string? hi = hiT is { } h ? CoachRank.Name(h) : null;
        var f = UiTheme.Display;
        int fs = UiTheme.Fs(66, k);
        float W(int s) => Gfx.TextW(f, lo, s) + (hi == null ? 0 : Gfx.TextW(f, sep, s) + Gfx.TextW(f, hi, s));
        while (fs > 20 && W(fs) > maxW) fs -= 2;
        Gfx.Text(d, f, lo, x, baseline, fs, CoachRank.Col(loT));
        if (hi == null) return;
        x += Gfx.TextW(f, lo, fs);
        Gfx.Text(d, f, sep, x, baseline, fs, UiTheme.Faint);
        x += Gfx.TextW(f, sep, fs);
        Gfx.Text(d, f, hi, x, baseline, fs, CoachRank.Col(hiT!.Value));
    }

    /// <summary>"Gold 2" / "Radiant" (translated rank name, not upper case).</summary>
    static string Pretty(float t)
    {
        int i = CoachRank.Index(t);
        string n = L.T(CoachRank.Names[i]);
        return i == 8 ? n : n + " " + CoachRank.Division(t).ToString(Inv);
    }

    static string When(DateTime u)
    {
        var age = DateTime.Now - u;
        if (age.TotalMinutes < 1) return L.T("JUST NOW");
        if (u.Date == DateTime.Today) return L.F($"TODAY {u:HH:mm}");
        return u.ToString("MMM d", Inv).ToUpperInvariant();
    }

    // ---------------- your actual rank (gap view) ----------------

    /// <summary>
    /// "Your VALORANT rank": the rank the player entered next to the estimate, as a gap (aim ahead of / behind the rank →
    /// what to train). It never changes the estimate. Plus the anonymous calibration line to paste on Discord.
    /// </summary>
    Control RankGapPanel(SkillProfile p)
    {
        float k = K;
        var panel = new VPanel { K = k, Pad = 18 };
        var h = HBox(18 * k);
        panel.AddChild(h);
        var info = VBox(4 * k);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(info);

        int rank = ActualRank.Value;
        bool set = rank >= 0, ranked = CoachData.HasOverall(p);
        var row = HBox(10 * k);
        info.AddChild(row);
        var tick = new DrawBox
        {
            CustomMinimumSize = new Vector2(12 * k, 18 * k), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            OnDraw = d =>
            {
                var c = new Vector2(6 * k, d.Size.Y / 2);
                d.DrawColoredPolygon(new[] { c + new Vector2(0, -6 * k), c + new Vector2(6 * k, 5 * k), c + new Vector2(-6 * k, 5 * k) },
                    set ? CoachDraw.ActualColor : new Color(UiTheme.Text, 0.25f));
            },
        };
        row.AddChild(tick);
        var cap = Lbl("YOUR VALORANT RANK", UiTheme.HudWide, 13, UiTheme.Dim);
        cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(cap);
        if (set)
        {
            float rt = ActualRank.Tier(rank);
            var name = Lbl(CoachRank.Name(rt), UiTheme.Display, 24, CoachRank.Col(rt));
            name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(name);
            if (ActualRank.SetOn is { } on)
            {
                bool stale = ActualRank.Stale;
                string date = on.ToString("MMM d", Inv).ToUpperInvariant();
                var when = Lbl(stale ? L.F($"SET {date} · STILL RIGHT?") : L.F($"SET {date}"), UiTheme.HudWide, 12, stale ? UiTheme.Warn : UiTheme.Faint);
                when.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                row.AddChild(when);
            }
        }

        if (set && ranked && CoachCalibration.Gap(p, rank) is { } gap)
        {
            var gc = gap.Kind == CoachCalibration.GapKind.Matches ? UiTheme.Good : UiTheme.Warn;
            info.AddChild(Lbl(CoachCalibration.Headline(gap), UiTheme.HudWide, 15, gc));
            info.AddChild(Para(CoachCalibration.Advice(gap), UiTheme.Body, 15, new Color(UiTheme.Text, 0.85f)));
        }
        else
        {
            string msg = !set
                ? L.T("Enter your rank to see whether your aim is ahead of or behind it, and what to train. It never changes the estimate.")
                : L.T("Your aim isn't rated yet: play a few more runs to compare it with your rank.");
            info.AddChild(Para(msg, UiTheme.Body, 15, UiTheme.Dim));
        }

        var buttons = VBox(6 * k);
        buttons.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        h.AddChild(buttons);
        var setBtn = Btn(set ? "CHANGE RANK" : "SET YOUR RANK", set ? VButton.Look.Secondary : VButton.Look.Primary, OpenRankPicker, 230, 44, 17);
        setBtn.TooltipText = L.T("Your rank stays on this PC. The coach only compares it with your aim; it never changes the estimate.");
        buttons.AddChild(setBtn);
        if (ranked)
        {
            VButton? copy = null;
            copy = Btn("COPY CALIBRATION LINE", VButton.Look.Ghost, () =>
            {
                DisplayServer.ClipboardSet(CoachCalibration.Line(p, ActualRank.Value, ActualRank.SetOn));
                copy!.Label = "COPIED";
                copy.QueueRedraw();
            }, 230, 34, 14);
            copy.TitleFont = UiTheme.HudWide;
            copy.TooltipText = L.T("Copies one anonymous line (your rank and skill numbers, no names) to paste in #coach-calibration on the ValTrainer Discord. It helps calibrate the rank estimate.");
            buttons.AddChild(copy);
        }
        return panel;
    }

    Control? picker;

    /// <summary>Rank picker overlay: Iron 1 … Immortal 3, Radiant, or "don't say" (clears it).</summary>
    void OpenRankPicker()
    {
        if (picker != null && IsInstanceValid(picker)) return;
        float k = K;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new DrawBox { MouseFilter = MouseFilterEnum.Ignore, OnDraw = d => d.DrawRect(new Rect2(Vector2.Zero, d.Size), new Color(0, 0, 0, 0.6f)) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.AddChild(center);
        var panel = new VPanel { Title = "YOUR VALORANT RANK", Caption = L.T("stays on this PC"), K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.98f) };
        center.AddChild(panel);
        var v = VBox(12 * k);
        panel.AddChild(v);
        v.AddChild(Para(L.T("Pick your current competitive rank. The coach shows how your aim compares with it."), UiTheme.Body, 15, UiTheme.Dim));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", (int)(8 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(6 * k));
        v.AddChild(grid);
        int cur = ActualRank.Value;
        void Pick(int r)
        {
            ActualRank.Set(r);
            ClosePicker();
            Rebuild();
        }
        for (int g = 0; g < 8; g++)
        {
            var name = Lbl(L.Upper(L.T(CoachRank.Names[g])), UiTheme.Display, 22, CoachRank.Colors[g]);
            name.CustomMinimumSize = new Vector2(190 * k, 0);
            name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            grid.AddChild(name);
            for (int dv = 1; dv <= 3; dv++)
            {
                int r = ActualRank.Make(g, dv);
                var b = Btn(L.Raw(dv.ToString(Inv)), VButton.Look.Select, () => Pick(r), 84, 40, 19);
                b.Selected = r == cur;
                grid.AddChild(b);
            }
        }
        var bottom = HBox(10 * k);
        v.AddChild(bottom);
        var rad = Btn(L.Upper(L.T(CoachRank.Names[8])), VButton.Look.Select, () => Pick(ActualRank.Radiant), 190, 44, 19);
        rad.Selected = cur == ActualRank.Radiant;
        bottom.AddChild(rad);
        bottom.AddChild(Expander());
        var clear = Btn("DON'T SAY", VButton.Look.Secondary, () => Pick(ActualRank.None), 150, 44, 17);
        clear.TooltipText = L.T("Unranked, or you'd rather not say: removes the rank");
        bottom.AddChild(clear);
        bottom.AddChild(Btn("CANCEL", VButton.Look.Ghost, ClosePicker, 120, 44, 17));
        picker = overlay;
        AddChild(overlay);
    }

    void ClosePicker()
    {
        if (picker != null && IsInstanceValid(picker)) picker.QueueFree();
        picker = null;
    }

    protected override void Back()
    {
        if (picker != null && IsInstanceValid(picker)) { ClosePicker(); return; }
        base.Back();
    }

    // ---------------- skills ----------------

    Control SkillsPanel(SkillProfile p)
    {
        float k = K;
        int rated = p.Skills.Count(CoachRank.Rated);
        var panel = new VPanel { Title = "SKILL BREAKDOWN", Caption = L.F($"{rated} of 8 rated · hover a skill for details"), K = k, SizeFlagsVertical = SizeFlags.ExpandFill };
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
        string legend = L.T(CoachData.HasOverall(p) ? "dashed ring = your overall rank" : "centre = Iron · rim = Radiant");
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
            Caption = any ? L.F($"{p.Problems.Count} found · most important first · click to expand") : null,
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
            list.AddChild(Para(L.T("No problems found in your recent runs — solid fundamentals. Keep playing: the coach re-checks after every run."),
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
        v.AddChild(Spacer(0, 6 * k));
        var row = HBox(10 * k);
        if (drills.Count > 0)
        {
            var cap = Lbl("PRACTISE", UiTheme.HudWide, 13, UiTheme.Dim);
            cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(cap);
            row.AddChild(Spacer(4 * k, 0));
            foreach (var key in drills)
            {
                string name = L.Upper(L.T(CoachData.DrillName(key)));
                float w = Gfx.TextW(UiTheme.Display, name, UiTheme.Fs(18, k)) / k + 60;
                var b = Btn(name, VButton.Look.Secondary, () => CoachData.StartDrill(key, CoachData.FocusFrom(d)), Mathf.Max(150, w), 42, 18);
                b.TooltipText = L.F($"Start {L.T(CoachData.DrillName(key))} now");
                row.AddChild(b);
            }
            row.AddChild(Spacer(8 * k, 0));
        }
        // "Show me": replay a moment from a recent run where this happened (searched on a worker thread)
        var status = Lbl("", UiTheme.Body, 14, UiTheme.Dim);
        status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(60 * k, 0);
        VButton? sm = null;
        sm = Btn("SHOW ME", VButton.Look.Ghost, () => ShowMe(d, sm!, status), 130, 42, 17);
        sm.TitleFont = UiTheme.HudWide;
        sm.TooltipText = L.T("Replay a moment from your recent runs where this happened");
        row.AddChild(sm);
        row.AddChild(status);
        v.AddChild(row);
        if (Main.I.Dev && !devShowMeDone && Core.CmdLine.After("--showme") == d.Id)
        {
            devShowMeDone = true; // dev screenshots: --screen profile --showme CODE
            Callable.From(() => { if (IsInstanceValid(sm)) ShowMe(d, sm, status); }).CallDeferred();
        }
        return v;
    }

    // ---------------- "Show me" ----------------

    static bool devShowMeDone;
    Task<(RunTelemetry Run, RunInsights Ins)?>? search;
    /// <summary>The found run's 3D replay being read (after <see cref="search"/>; the button says "LOADING RUN…").</summary>
    Task? loading;
    (string Code, string Drill, VButton Btn, Label Status)? searchFor;

    void ShowMe(Diagnosis d, VButton b, Label status)
    {
        if (search != null) return;
        string drill = (d.Drills ?? Array.Empty<string>()).Where(CoachData.HasDrill).Select(CoachData.DrillName).FirstOrDefault() is { } dn ? L.T(dn) : L.T("a few drills");
        b.Disabled = true;
        b.Label = "SEARCHING…";
        b.QueueRedraw();
        status.Text = "";
        searchFor = (d.Id, drill, b, status);
        search = RewindData.FindExample(d.Id);
    }

    void PollShowMe()
    {
        if (search is not { IsCompleted: true } task || searchFor is not { } sf) return;
        var found = task.IsCompletedSuccessfully ? task.Result : null;
        if (found is { } run && loading == null && run.Run.Replay == null && run.Run.SourceFile != null)
        {
            // Found: read its 3D replay (.vtr sidecar) on a worker too, so the Rewind opens ready to build the 3D view.
            loading = Task.Run(() => { try { run.Run.Replay ??= Game.Replay.ReplayFile.For(run.Run); } catch { /* 2D only */ } });
            if (IsInstanceValid(sf.Btn)) { sf.Btn.Label = "LOADING RUN…"; sf.Btn.QueueRedraw(); }
            return;
        }
        if (loading is { IsCompleted: false }) return;
        loading = null;
        search = null;
        searchFor = null;
        if (IsInstanceValid(sf.Btn)) { sf.Btn.Disabled = false; sf.Btn.Label = "SHOW ME"; sf.Btn.QueueRedraw(); }
        if (found is { } f) RewindScreen.Open(Main.I, f.Run, f.Ins, sf.Code); // not a child: a resize rebuilds this screen
        else if (IsInstanceValid(sf.Status)) sf.Status.Text = L.F($"No recorded example yet. Play {sf.Drill}.");
    }

    Control GetRated(SkillProfile p, bool partial)
    {
        float k = K;
        var v = VBox(10 * k);
        int need = CoachData.RunsNeeded();
        if (partial) v.AddChild(new SectionLabel { Text = "UNLOCK YOUR OVERALL RANK", K = k });
        int more = need > 0 ? need : 3;
        v.AddChild(Para(
            L.P(more, $"Play Head Flicks, Strafe Tracking, Peek Practice, Counter-Strafe and Vandal Spray a few times — the coach needs about {more} more run to estimate your rank. Every run is analysed automatically.",
                $"Play Head Flicks, Strafe Tracking, Peek Practice, Counter-Strafe and Vandal Spray a few times — the coach needs about {more} more runs to estimate your rank. Every run is analysed automatically."),
            UiTheme.Body, 16, UiTheme.Text));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", (int)(10 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(10 * k));
        foreach (var (key, target, rates) in CoachData.Starter)
            if (CoachData.HasDrill(key))
                grid.AddChild(new DrillTile { Key = key, Target = target, Rates = rates, Runs = CoachData.CoachedRuns(key), K = k });
        v.AddChild(grid);
        if (Main.I.Dev && CoachData.LastError is { } err) v.AddChild(Para(L.Raw($"Coach error: {err}"), UiTheme.Body, 13, UiTheme.Warn)); // dev only
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
            Caption = has ? L.F($"recommendation · {CoachRank.ConfidenceLower(a!.Confidence)}") : "not enough data yet",
            K = k,
        };
        var v = VBox(10 * k);
        panel.AddChild(v);
        v.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 112 * k), OnDraw = d => DrawSens(d, p) });
        string reason = has
            ? a!.Reason
            : L.T("Not enough flick and tracking data for a recommendation yet. Play Head Flicks and Strafe Tracking a few times — or run the Sens Finder for a direct blind A/B test.");
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
        finder.TooltipText = L.T("Blind A/B test of sensitivities with flicks + tracking (about 15 minutes)");
        finder.Disabled = !CoachData.HasDrill("sensfinder");
        row.AddChild(finder);
        var note = Para(L.T("ValTrainer can't change Valorant's settings — set it in Valorant: Settings → General → Mouse → Sensitivity."),
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
            ? L.T("ValTrainer is using the recommended sens. Click to go back to your previous sens.")
            : L.F($"Use {Rounded(a.RecommendedSens):0.000} in ValTrainer's drills (your Valorant settings stay unchanged)");
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
            Gfx.Text(d, UiTheme.Body, L.F($"{sens * dpi:0} eDPI · {cm:0.0} cm/360"), x, Gfx.Mid(100 * k, sub), sub, UiTheme.Dim);
        }

        Block(0, has ? L.T("CURRENT") : app.Settings.UseSensOverride ? L.T("CURRENT · VALTRAINER") : L.T("CURRENT · FROM VALORANT"), cur, curCm, UiTheme.Text);
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
            string pct = dir == "keep" || cur <= 0 ? L.T("KEEP") : string.Create(Inv, $"{(rec / cur - 1f) * 100f:+0;-0}%");
            Gfx.TextC(d, UiTheme.Display, pct, ax, Gfx.Mid(80 * k, ps), ps, dc);
            if (dir != "keep") Gfx.TextC(d, UiTheme.HudWide, dir == "lower" ? "LOWER" : dir == "higher" ? "HIGHER" : L.Raw(dir.ToUpperInvariant()), ax, Gfx.Mid(100 * k, ls), ls, UiTheme.Faint);
            string capText = L.T(dir != "keep" ? "RECOMMENDED" : a.Confidence < 0.5f ? "KEEP FOR NOW" : "KEEP YOUR SENS");
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
        Marker(curCm, L.T("NOW"), UiTheme.Text, recCm == null || !recRight);
        if (recCm is { } rc) Marker(rc, L.C("sens", "NEW"), UiTheme.Good, recRight); // the new sens ("NEW" alone: a new drill)
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
