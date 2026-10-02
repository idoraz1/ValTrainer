using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// WARM UP setup: pick a routine (Quick / Standard / Pro), set the sens shifter (Off / Higher → your sens /
/// Lower → your sens, start offset) and see the schedule (sens over time + drills) before starting.
/// </summary>
public partial class WarmupScreen : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    WarmupPlan plan = null!;
    DrawBox? chart, shiftInfo;

    WarmupStore Store => WarmupStore.I;

    void MakePlan() => plan = WarmupPlan.Build(WarmupPresets.Get(Store.Preset), Store.ShiftMode, Store.ShiftPct, Main.I.Sens, WarmupRunner.QuickDev);

    protected override void Build()
    {
        float k = K;
        MakePlan();
        var root = Margins(VBox(14 * k), 48 * k, 30 * k, 48 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var tier = Difficulty.Get(Main.I.Tier);
        var title = Heading("WARM UP", $"GUIDED ROUTINE · {tier.Name.ToUpperInvariant()} TIER · EASY → HARD · THE LAST MINUTES ARE ALWAYS AT YOUR REAL SENS");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var back = Btn("BACK", VButton.Look.Secondary, Main.I.ShowMenu, 160, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        var body = HBox(26 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);
        var left = VBox(10 * k);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(left);
        var right = VBox(16 * k);
        right.CustomMinimumSize = new Vector2(500 * k, 0);
        body.AddChild(right);

        // ---- routine ----
        left.AddChild(new SectionLabel { Text = "ROUTINE", Note = "drills at your tier · every drill except deathmatch is a normal 60 s run", K = k });
        var presets = HBox(10 * k);
        foreach (var p in WarmupPresets.All)
        {
            var pp = WarmupPlan.Build(p, ShiftMode.Off, 0, Main.I.Sens, false);
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = p.Name.ToUpperInvariant(),
                Sub = $"≈{WarmupPlan.Minutes(pp.EstimatedSeconds)} · {p.Steps.Length} DRILLS", TooltipText = p.Blurb,
                FontPx = 26, SubPx = 13, CustomMinimumSize = new Vector2(0, 72 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Selected = p.Key == plan.Preset.Key,
            };
            string key = p.Key;
            b.Pressed += () => { Store.Preset = key; Store.Save(); Callable.From(Rebuild).CallDeferred(); };
            presets.AddChild(b);
        }
        left.AddChild(presets);

        // ---- schedule chart ----
        var chartPanel = new VPanel { Title = "SCHEDULE", K = k, Caption = $"{plan.Steps.Count} drills · {WarmupPlan.Clock(plan.PlaySeconds)} play time", SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(chartPanel);
        chart = new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 250 * k), SizeFlagsVertical = SizeFlags.ExpandFill,
            OnDraw = d => WarmupCharts.DrawSchedule(d, new Rect2(Vector2.Zero, d.Size), plan, k),
        };
        chartPanel.AddChild(chart);

        // ---- sens shifter ----
        left.AddChild(new SectionLabel { Text = "SENS SHIFTER", Note = "start a bit off your sens, step back to it, finish on your real sens", K = k });
        var row = HBox(10 * k);
        void ShiftBtn(string label, string sub, ShiftMode m)
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = label, Sub = sub, FontPx = 19, SubPx = 12,
                CustomMinimumSize = new Vector2(0, 58 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, Selected = Store.ShiftMode == m,
            };
            b.Pressed += () => { Store.ShiftMode = m; Store.Save(); Callable.From(Rebuild).CallDeferred(); };
            row.AddChild(b);
        }
        ShiftBtn("OFF", "every drill at your sens", ShiftMode.Off);
        ShiftBtn("HIGHER → YOUR SENS", "default · your sens feels slower after", ShiftMode.HighToNormal);
        ShiftBtn("LOWER → YOUR SENS", "big arm motions first", ShiftMode.LowToNormal);
        row.AddChild(Spacer(10 * k, 0));
        foreach (int pct in WarmupPresets.ShiftOptions)
        {
            string sign = Store.ShiftMode == ShiftMode.LowToNormal ? "−" : "+";
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = $"{sign}{pct}%", Sub = pct == WarmupPresets.DefaultShiftPct ? "default" : pct < WarmupPresets.DefaultShiftPct ? "subtle" : "strong",
                FontPx = 20, SubPx = 12, CustomMinimumSize = new Vector2(96 * k, 58 * k), Selected = Store.ShiftPct == pct,
                Disabled = Store.ShiftMode == ShiftMode.Off,
            };
            int v = pct;
            b.Pressed += () => { Store.ShiftPct = v; Store.Save(); Callable.From(Rebuild).CallDeferred(); };
            row.AddChild(b);
        }
        left.AddChild(row);
        shiftInfo = new DrawBox { CustomMinimumSize = new Vector2(0, 50 * k), OnDraw = DrawShiftInfo };
        left.AddChild(shiftInfo);

        // ---- start ----
        var startRow = HBox(16 * k);
        var start = Btn("START WARM-UP", VButton.Look.Primary, StartRoutine, 340, 62, 26);
        startRow.AddChild(start);
        var note = Lbl($"≈{WarmupPlan.Minutes(plan.EstimatedSeconds).ToLowerInvariant()} incl. short breaks · Esc pauses a drill · between drills: Space starts, the card lets you skip",
            UiTheme.Body, 15, UiTheme.Dim);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        startRow.AddChild(note);
        left.AddChild(startRow);

        // ---- right: research + history ----
        var why = new VPanel { Title = "HOW PROS WARM UP", K = k, Caption = "what this routine is built on" };
        right.AddChild(why);
        var wv = VBox(8 * k);
        why.AddChild(wv);
        foreach (var (h, t) in Notes)
        {
            wv.AddChild(Lbl(h, UiTheme.HudWide, 13, UiTheme.Accent));
            var l = Lbl(t, UiTheme.Body, 14.5f, UiTheme.Text);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(440 * k, 0);
            wv.AddChild(l);
        }

        var hist = new VPanel { Title = "YOUR WARM-UPS", K = k, Caption = Store.Warmups.Count == 0 ? "none yet" : $"{Store.Warmups.Count} saved", SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(hist);
        hist.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 110 * k), SizeFlagsVertical = SizeFlags.ExpandFill, OnDraw = DrawHistory });
    }

    static readonly (string, string)[] Notes =
    {
        ("SHORT AND SPECIFIC",
         "A warm-up wakes up skill you already have; it doesn't build new skill. Coaches suggest 10–15 minutes, starting easy and slow and ending with game-like fights. Sports science agrees: short, task-specific, getting harder, no fatigue."),
        ("SMOOTH → FAST → GAME-LIKE",
         "Tracking and big targets first, then precise flicks and target switching, then strafing bots, movement and peeks, then a deathmatch. Play it to warm up, not to win (Demon1, TenZ, ProSettings and Dignitas routines)."),
        ("THE SENS SHIFTER: HONEST VERSION",
         "Warming up slightly faster so your sens feels slower and calmer afterwards is a popular habit. There is no controlled study of it for aiming. Related research (variable practice, weighted-bat warm-ups) shows small or 'feels better' effects. Voltaic suggests small shifts (+10–15%). So the shift stays small and fades out, and the last minutes are always at your real sens."),
    };

    void DrawShiftInfo(DrawBox d)
    {
        float k = K;
        int fs = UiTheme.Fs(16, k), ss = UiTheme.Fs(14, k);
        string line;
        if (plan.Shift == ShiftMode.Off || plan.ShiftedCount == 0)
            line = $"Every drill at your sens {plan.BaseSens.ToString("0.000", Inv)}.";
        else
        {
            var first = plan.Steps[0];
            var lastShift = plan.Steps.Last(s => s.Shifted);
            line = $"Starts at {first.Sens.ToString("0.000", Inv)} ({first.OffsetPct:+0;-0}%), steps back over the first {plan.ShiftedCount} drills " +
                   $"(last {lastShift.Sens.ToString("0.000", Inv)}), then {WarmupPlan.Clock(plan.RealSeconds)} at your sens {plan.BaseSens.ToString("0.000", Inv)}.";
        }
        Gfx.TextFit(d, UiTheme.Body, line, 0, Gfx.Mid(14 * k, fs), fs, UiTheme.Text, d.Size.X);
        Gfx.TextFit(d, UiTheme.Body, "Shifted drills aren't saved to your stats (they'd skew the coach's sens advice); the summary graph shows them all.",
            0, Gfx.Mid(38 * k, ss), ss, UiTheme.Faint, d.Size.X);
    }

    void DrawHistory(DrawBox d)
    {
        float k = K;
        var list = Store.Warmups.Where(w => w.Readiness >= 0).TakeLast(20).ToList();
        int ls = UiTheme.Fs(12, k), fs = UiTheme.Fs(15, k);
        if (Store.Warmups.Count == 0)
        {
            Gfx.TextFit(d, UiTheme.Body, "Finish a warm-up to see your lock-in graph and how each warm-up compares.", 0, Gfx.Mid(20 * k, fs), fs, UiTheme.Dim, d.Size.X);
            return;
        }
        var lastW = Store.Warmups[^1];
        Gfx.Text(d, UiTheme.HudWide, "LAST", 0, Gfx.Mid(14 * k, ls), ls, UiTheme.Faint);
        string lastText = $"{lastW.When:MMM d, HH:mm} · {WarmupPresets.Get(lastW.Preset).Name}" +
                          (lastW.Readiness >= 0 ? $" · {lastW.Readiness:0}% of usual at your sens" : "");
        Gfx.TextFit(d, UiTheme.Body, lastText, 54 * k, Gfx.Mid(14 * k, fs), fs, UiTheme.Text, d.Size.X - 54 * k);
        if (list.Count >= 2)
        {
            Gfx.Text(d, UiTheme.HudWide, "READINESS · LAST 20", 0, Gfx.Mid(44 * k, ls), ls, UiTheme.Faint);
            Sparkline.Draw(d, new Rect2(0, 58 * k, d.Size.X, Mathf.Max(20 * k, d.Size.Y - 66 * k)), list.Select(w => w.Readiness).ToList(), k, UiTheme.Teal);
        }
    }

    void StartRoutine()
    {
        MakePlan();
        WarmupRunner.Start(plan);
    }
}
