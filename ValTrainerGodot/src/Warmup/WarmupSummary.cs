using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// WARM-UP SUMMARY: the lock-in graph (performance per drill as % of your usual, with the sens overlaid), a verdict
/// ("You're warmed up: above your 7-day average"), start → end comparisons for drills played at both ends, how it
/// compares with previous warm-ups, and a per-drill table.
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

    protected override void Back() => Main.I.ShowMenu();

    public override void _Ready()
    {
        base._Ready();
        // Dev "--wuquit": quit a few seconds after the summary appears (headless validation runs).
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
        string shift = rec.Shift == "off" ? "sens shifter off" : $"sens shifter {(rec.Shift == "low" ? "−" : "+")}{rec.ShiftPct}% → your sens";
        var title = Heading(rec.Completed ? "WARM-UP COMPLETE" : "WARM-UP SUMMARY",
            $"{preset.Name.ToUpperInvariant()} · {Difficulty.Get(rec.Tier).Name.ToUpperInvariant()} TIER · {WarmupPlan.Clock(rec.PlaySeconds)} PLAYED · {shift.ToUpperInvariant()}" +
            (Demo ? " · DEMO DATA" : ""));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var again = Btn("WARM UP AGAIN", VButton.Look.Secondary, Main.I.ShowWarmup, 220, 52, 20);
        again.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(again);
        var menu = Btn("MENU", VButton.Look.Primary, Main.I.ShowMenu, 170, 52, 22);
        menu.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(menu);
        col.AddChild(head);

        // the graph
        var gp = new VPanel { Title = "LOCK-IN GRAPH", K = k, Caption = "bars: each drill vs your usual level · line: sensitivity", SizeFlagsVertical = SizeFlags.ExpandFill };
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
        var rp = new VPanel { Title = "LOCK-IN", K = k, Caption = "your real sens at the end vs the start and your usual", SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
            CustomMinimumSize = new Vector2(0, (rec.Steps.Count + 1) * 26 * k + 6 * k),
            OnDraw = DrawTable,
        });
    }

    // ---------------- readout ----------------

    /// <summary>Verdict from the steps at the real sens that have a history baseline.</summary>
    (string Head, string Sub, Color Col) Verdict()
    {
        float r = rec.Readiness;
        string usual = WarmupScore.BaselineText(rec.ReadinessKind);
        if (r < 0) return ("WARM-UP DONE", "No history for these drills at this tier yet: the graph compares each drill with your first run of it today. Play them normally a few times and the next warm-up compares you with your usual level.", UiTheme.Text);
        if (r >= 100) return ("YOU'RE WARMED UP", $"At your real sens you played at {r:0}% of {usual}. You're above your usual level, so go play.", UiTheme.Good);
        if (r >= 94) return ("READY", $"At your real sens you played at {r:0}% of {usual}: right at your usual level.", UiTheme.Teal);
        return ("STILL A BIT COLD", $"At your real sens you played at {r:0}% of {usual}. One more deathmatch or a Quick warm-up should get you there. Off days happen too.", UiTheme.Warn);
    }

    /// <summary>Drills played both early and late: first → last value.</summary>
    IEnumerable<(string Text, float Change, bool Better)> StartEnd()
    {
        foreach (var mode in rec.Steps.Where(s => !s.Skipped && s.Value > 0).Select(s => s.Mode).Distinct())
        {
            var runs = rec.Steps.Where(s => s.Mode == mode && !s.Skipped && s.Value > 0).ToList();
            if (runs.Count < 2) continue;
            var a = runs[0];
            var b = runs[^1];
            bool lower = WarmupScore.LowerBetter(mode);
            float pct = (b.Value - a.Value) / a.Value * 100f;
            bool better = lower ? b.Value < a.Value : b.Value > a.Value;
            string sens = a.Shifted && !b.Shifted ? $"  (sens {a.Sens.ToString("0.000", Inv)} → {b.Sens.ToString("0.000", Inv)})" : "";
            string change = WarmupScore.Def(mode) == WarmupScore.Kind.OnTarget ? $"{b.Value - a.Value:+0;-0} pts" : $"{pct:+0;-0}%";
            yield return ($"{WarmupDrills.Name(mode)} · {WarmupScore.Label(mode)}  {WarmupScore.Format(mode, a.Value)} → {WarmupScore.Format(mode, b.Value)}  {change}{sens}", pct, better);
        }
    }

    void DrawReadout(DrawBox d)
    {
        float k = K, w = d.Size.X;
        var (headText, sub, col) = Verdict();
        int hs = UiTheme.Fs(40, k), ss = UiTheme.Fs(15, k), ls = UiTheme.Fs(16, k);
        Gfx.Text(d, UiTheme.Display, headText, 0, 36 * k, hs, col);
        if (rec.Readiness >= 0)
            Gfx.TextR(d, UiTheme.Display, $"{rec.Readiness:0}%", w, 36 * k, hs, col);
        d.DrawMultilineString(UiTheme.Body, new Vector2(0, 58 * k + UiTheme.Body.GetAscent(ss)), sub, HorizontalAlignment.Left, w, ss, 2, UiTheme.Dim);

        float y = 120 * k;
        foreach (var (text, _, better) in StartEnd().Take(3))
        {
            Gfx.Diamond(d, new Vector2(6 * k, y - 5 * k), 4 * k, 4 * k, better ? UiTheme.Good : UiTheme.Warn);
            Gfx.TextFit(d, UiTheme.Body, text, 20 * k, y, ls, UiTheme.Text, w - 20 * k);
            y += 26 * k;
        }
        // vs previous warm-ups
        var prev = previous.Where(p => p.Readiness >= 0).TakeLast(5).ToList();
        string hist = rec.Readiness < 0 || prev.Count == 0
            ? previous.Count == 0 ? "Your first warm-up. Next time you'll see how it compares." : "Not enough history to compare with your previous warm-ups yet."
            : $"Better than {prev.Count(p => rec.Readiness > p.Readiness)} of your last {prev.Count} warm-up{(prev.Count == 1 ? "" : "s")} (readiness at your real sens).";
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
        float k = K, w = d.Size.X, rh = 26 * k;
        int ls = UiTheme.Fs(11, k), fs = UiTheme.Fs(15, k);
        float[] cx = { 0, 0.06f, 0.36f, 0.53f, 0.76f, 1f };
        string[] heads = { "#", "DRILL", "SENS", "RESULT", "VS USUAL" };
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
            string sens = s.Sens.ToString("0.000", Inv) + (s.Shifted ? $" {s.OffsetPct:+0;-0}%" : "");
            Gfx.TextFit(d, UiTheme.Body, sens, cx[2] * w, Gfx.Mid(cy, fs), fs, s.Shifted ? WarmupCharts.ShiftCol : dim, (cx[3] - cx[2]) * w - 6 * k);
            string res = s.Skipped ? "skipped" : $"{WarmupScore.Format(s.Mode, s.Value)} {WarmupScore.Label(s.Mode)}";
            Gfx.TextFit(d, UiTheme.Body, res, cx[3] * w, Gfx.Mid(cy, fs), fs, dim, (cx[4] - cx[3]) * w - 6 * k);
            string vs = s.Skipped || s.Index < 0 ? "—" : s.BaselineKind == "today" ? $"{s.Index:0}% (today)" : $"{s.Index:0}%";
            var vc = s.Index < 0 || s.Skipped ? UiTheme.Faint : s.Index >= 100 ? UiTheme.Good : s.Index >= 94 ? UiTheme.Text : UiTheme.Warn;
            Gfx.TextR(d, UiTheme.Display, vs, w - 4 * k, Gfx.Mid(cy, fs), fs, vc);
            y += rh;
        }
    }
}
