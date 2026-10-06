using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// LOCK-IN DETAILS (from the hand-off's DETAILS button): the lock-in graph (fixed drills as % of your usual, adaptive
/// drills as the level held near 80% hits, the sens only if it was shifted), the neutral verdict, start → end
/// comparisons, adaptive levels vs previous Lock-Ins, how it compares with previous Lock-Ins, and a per-drill table.
/// Esc / BACK return to the hand-off.
/// </summary>
public partial class WarmupSummary : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly WarmupRecord rec;
    readonly List<WarmupRecord> previous;
    /// <summary>Dev demo data (shown with a marker).</summary>
    public bool Demo;

    public WarmupSummary(WarmupRecord rec, List<WarmupRecord> previous)
    {
        this.rec = rec;
        this.previous = previous;
    }

    protected override void Back() => Main.I.ShowScreen(new LockInHandoff(rec, previous) { Demo = Demo });

    public override void _Ready()
    {
        base._Ready();
        // Dev "--wuquit": quit a few seconds after the details appear (headless validation runs).
        if (Main.I.Dev && CmdLine.Has("--wuquit")) GetTree().CreateTimer(3.0).Timeout += () => GetTree().Quit();
    }

    protected override void Build()
    {
        float k = K;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var root = Margins(VBox(14 * k), 48 * k, 28 * k, 48 * k, 30 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var preset = WarmupPresets.Get(rec.Preset);
        string shift = rec.Shift == "off" ? "every drill at your sens" : $"sens shifter {(rec.Shift == "low" ? "−" : "+")}{rec.ShiftPct}% → your sens";
        var title = Heading("LOCK-IN DETAILS",
            $"{preset.Name.ToUpperInvariant()} · {Difficulty.Get(rec.Tier).Name.ToUpperInvariant()} TIER · {WarmupPlan.Clock(rec.PlaySeconds)} PLAYED · {shift.ToUpperInvariant()}" +
            (Demo && !Core.CmdLine.Showcase ? " · DEMO DATA" : ""));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var back = Btn("BACK", VButton.Look.Secondary, Back, 150, 52, 20);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        var again = Btn("LOCK IN AGAIN", VButton.Look.Secondary, Agents.AgentRoutines.Again(rec.Preset) ?? (Action)Main.I.ShowLockIn, 220, 52, 20);
        again.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(again);
        var menu = Btn("MENU", VButton.Look.Primary, Main.I.ShowMenu, 170, 52, 22);
        menu.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(menu);
        col.AddChild(head);

        // the graph
        var gp = new VPanel
        {
            Title = "LOCK-IN GRAPH", K = k, SizeFlagsVertical = SizeFlags.ExpandFill,
            Caption = rec.Steps.Any(s => s.Shifted) ? "bars: each drill vs your usual level · line: sensitivity" : "bars: each drill vs your usual level · boxes: adaptive drills",
        };
        col.AddChild(gp);
        gp.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 330 * k), SizeFlagsVertical = SizeFlags.ExpandFill,
            OnDraw = d => WarmupCharts.DrawResult(d, new Rect2(Vector2.Zero, d.Size), rec, k),
        });

        // bottom: readout + table
        var bottom = HBox(20 * k);
        bottom.CustomMinimumSize = new Vector2(0, 300 * k);
        col.AddChild(bottom);
        var rp = new VPanel { Title = "TODAY", K = k, Caption = "vs your usual and your previous Lock-Ins", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rp.SizeFlagsStretchRatio = 1.1f;
        bottom.AddChild(rp);
        rp.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 220 * k), OnDraw = DrawReadout });
        var tp = new VPanel { Title = "DRILLS", K = k, Caption = $"{rec.Steps.Count(s => !s.Skipped)} played", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bottom.AddChild(tp);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 220 * k) };
        tp.AddChild(scroll);
        scroll.AddChild(new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, (rec.Steps.Count + 1) * 24 * k + 6 * k),
            OnDraw = DrawTable,
        });
    }

    // ---------------- readout ----------------

    /// <summary>Fixed-tier drills played both early and late: first → last value (adaptive runs move their difficulty, so
    /// their scores aren't compared).</summary>
    IEnumerable<(string Text, float Change, int Dir)> StartEnd()
    {
        var fixedRuns = rec.Steps.Where(s => !s.Skipped && !s.Adaptive && s.Value > 0).ToList();
        foreach (var mode in fixedRuns.Select(s => s.Mode).Distinct())
        {
            var runs = fixedRuns.Where(s => s.Mode == mode).ToList();
            if (runs.Count < 2 || runs[0].Tier != runs[^1].Tier) continue;
            var a = runs[0];
            var b = runs[^1];
            bool lower = WarmupScore.LowerBetter(mode);
            float pct = (b.Value - a.Value) / a.Value * 100f;
            int dir = b.Value == a.Value ? 0 : (lower ? b.Value < a.Value : b.Value > a.Value) ? 1 : -1; // +1 = better
            string sens = a.Shifted && !b.Shifted ? $"  (sens {a.Sens.ToString("0.000", Inv)} → {b.Sens.ToString("0.000", Inv)})" : "";
            string change = WarmupScore.Def(mode) == WarmupScore.Kind.OnTarget ? $"{b.Value - a.Value:+0;-0} pts" : $"{pct:+0;-0}%";
            yield return ($"{WarmupDrills.Name(mode)} · {WarmupScore.Label(mode)}  {WarmupScore.Format(mode, a.Value)} → {WarmupScore.Format(mode, b.Value)}  {change}{sens}", pct, dir);
        }
    }

    /// <summary>Adaptive drills: the level held vs this drill's level in previous Lock-Ins (Dir: +1 higher, 0 about the
    /// same, -1 lower, -2 first time).</summary>
    IEnumerable<(string Text, int Dir)> Levels()
    {
        foreach (var g in rec.Steps.Where(s => !s.Skipped && s.Adaptive && s.Level >= 0).GroupBy(s => s.Mode))
        {
            var last = g.Last();
            float lv = g.Average(s => s.Level);
            float prev = g.First().PrevLevel;
            string hits = last.HitRate >= 0 ? $" at {g.Average(s => Mathf.Max(0, s.HitRate)) * 100:0}% hits" : "";
            string vs = prev >= 0 ? $"  (last Lock-Ins {prev.ToString("0.0", Inv)})" : "  (first time)";
            yield return ($"{WarmupDrills.Name(g.Key)} · held level {WarmupScore.LevelText(lv)}{hits}{vs}", prev < 0 ? -2 : lv > prev + 0.05f ? 1 : lv < prev - 0.05f ? -1 : 0);
        }
    }

    void DrawReadout(DrawBox d)
    {
        float k = K, w = d.Size.X;
        var (headText, sub, col) = WarmupScore.Verdict(rec.Readiness, rec.ReadinessKind);
        int hs = UiTheme.Fs(34, k), ss = UiTheme.Fs(15, k), ls = UiTheme.Fs(16, k);
        float pw = rec.Readiness >= 0 ? Gfx.TextR(d, UiTheme.Display, $"{rec.Readiness:0}%", w, 34 * k, hs, col) + 16 * k : 0;
        float hx = WarmupCharts.VerdictMark(d, rec.Readiness, new Vector2(0, 34 * k - hs * 0.36f), k, col);
        Gfx.TextFit(d, UiTheme.Display, headText, hx, 34 * k, hs, col, w - pw - hx);
        d.DrawMultilineString(UiTheme.Body, new Vector2(0, 54 * k + UiTheme.Body.GetAscent(ss)), sub, HorizontalAlignment.Left, w, ss, 2, UiTheme.Dim);

        float y = 116 * k;
        var lines = Levels().Concat(StartEnd().Select(s => (s.Text, s.Dir))).Take(3).ToList();
        foreach (var (text, dir) in lines)
        {
            // shape, not just colour: ▲ better, ▼ worse, = about the same, a diamond for "first time"
            var mc = dir > 0 ? UiTheme.Good : dir == 0 ? UiTheme.Text : UiTheme.Dim;
            if (dir == -2) Gfx.Diamond(d, new Vector2(6 * k, y - 5 * k), 4 * k, 4 * k, UiTheme.Dim);
            else WarmupCharts.TrendMark(d, new Vector2(6 * k, y - 5 * k), 5 * k, dir, mc);
            Gfx.TextFit(d, UiTheme.Body, text, 20 * k, y, ls, UiTheme.Text, w - 20 * k);
            y += 26 * k;
        }
        // vs previous Lock-Ins
        var prev = previous.Where(p => p.Readiness >= 0).TakeLast(5).ToList();
        string hist = rec.Readiness < 0 || prev.Count == 0
            ? previous.Count == 0 ? "Your first Lock-In. Next time you'll see how it compares." : "Not enough history to compare with your previous Lock-Ins yet."
            : $"Higher than {prev.Count(p => rec.Readiness > p.Readiness)} of your last {prev.Count} Lock-In{(prev.Count == 1 ? "" : "s")} (vs your usual).";
        Gfx.Diamond(d, new Vector2(6 * k, y - 5 * k), 4 * k, 4 * k, UiTheme.Accent);
        Gfx.TextFit(d, UiTheme.Body, hist, 20 * k, y, ls, UiTheme.Text, w - 20 * k);
        y += 26 * k;
        var shifted = rec.Steps.Where(s => s.Shifted && !s.Skipped).ToList();
        if (shifted.Count > 0)
        {
            float realSecs = rec.Steps.Where(s => !s.Shifted && !s.Skipped).Sum(s => s.Seconds);
            string s = $"Sens {shifted[0].Sens.ToString("0.000", Inv)} ({shifted[0].OffsetPct:+0;-0}%) → {rec.BaseSens.ToString("0.000", Inv)} over {shifted.Count} drills, " +
                       $"then {WarmupPlan.Clock(realSecs)} at your real sens.";
            Gfx.TextFit(d, UiTheme.Body, s, 20 * k, y, UiTheme.Fs(14, k), UiTheme.Faint, w - 20 * k);
        }
    }

    // ---------------- table ----------------

    void DrawTable(DrawBox d)
    {
        float k = K, w = d.Size.X, rh = 24 * k;
        int ls = UiTheme.Fs(11, k), fs = UiTheme.Fs(15, k);
        bool anyShift = rec.Steps.Any(s => s.Shifted);
        float[] cx = { 0, 0.06f, 0.36f, 0.53f, 0.76f, 1f };
        string[] heads = { "#", "DRILL", anyShift ? "SENS" : "TIER", "RESULT", "VS USUAL" };
        for (int i = 0; i < heads.Length; i++)
        {
            if (i == heads.Length - 1) Gfx.TextR(d, UiTheme.HudWide, heads[i], w - 4 * k, Gfx.Mid(rh / 2, ls), ls, UiTheme.Faint);
            else Gfx.Text(d, UiTheme.HudWide, heads[i], cx[i] * w, Gfx.Mid(rh / 2, ls), ls, UiTheme.Faint);
        }
        float y = rh;
        for (int i = 0; i < rec.Steps.Count; i++)
        {
            var s = rec.Steps[i];
            float cy = y + rh / 2;
            if (i % 2 == 0) d.DrawRect(new Rect2(0, y, w, rh), new Color(1, 1, 1, 0.03f));
            var dim = s.Skipped ? UiTheme.Faint : UiTheme.Text;
            Gfx.Text(d, UiTheme.Body, (i + 1).ToString(Inv), cx[0] * w, Gfx.Mid(cy, fs), fs, UiTheme.Faint);
            Gfx.TextFit(d, UiTheme.Body, WarmupDrills.Name(s.Mode), cx[1] * w, Gfx.Mid(cy, fs), fs, dim, (cx[2] - cx[1]) * w - 6 * k);
            string third = anyShift ? s.Sens.ToString("0.000", Inv) + (s.Shifted ? $" {s.OffsetPct:+0;-0}%" : "")
                : s.Adaptive ? "adaptive" : s.Tier >= 0 ? Difficulty.Get(s.Tier).Name : "—";
            Gfx.TextFit(d, UiTheme.Body, third, cx[2] * w, Gfx.Mid(cy, fs), fs, s.Shifted ? WarmupCharts.ShiftCol : dim, (cx[3] - cx[2]) * w - 6 * k);
            string res = s.Skipped ? "skipped"
                : s.Adaptive ? s.HitRate >= 0 ? $"{s.HitRate * 100:0}% hits · {s.Trials} targets" : "—"
                : $"{WarmupScore.Format(s.Mode, s.Value)} {WarmupScore.Label(s.Mode)}";
            Gfx.TextFit(d, UiTheme.Body, res, cx[3] * w, Gfx.Mid(cy, fs), fs, dim, (cx[4] - cx[3]) * w - 6 * k);
            string vs;
            Color vc;
            if (s.Adaptive && !s.Skipped && s.Level >= 0) { vs = $"LEVEL {s.Level.ToString("0.0", Inv)}"; vc = WarmupCharts.PhaseColor(Phase.Calibration); }
            else
            {
                vs = s.Skipped || s.Index < 0 ? "—" : s.BaselineKind == "today" ? $"{s.Index:0}% (today)" : $"{s.Index:0}%";
                vc = s.Index < 0 || s.Skipped ? UiTheme.Faint : s.Index >= 106 ? UiTheme.Good : s.Index >= 94 ? UiTheme.Text : UiTheme.Dim;
            }
            float vw = Gfx.TextR(d, UiTheme.Display, vs, w - 4 * k, Gfx.Mid(cy, fs), fs, vc);
            // ▲ above / ▼ below / = in your usual range, so it doesn't hang on green vs grey alone
            int dir = WarmupScore.VerdictDir(s.Index);
            if (!s.Skipped && !s.Adaptive && dir != -2) WarmupCharts.TrendMark(d, new Vector2(w - 4 * k - vw - 10 * k, cy), 4.5f * k, dir, vc);
            y += rh;
        }
    }
}
