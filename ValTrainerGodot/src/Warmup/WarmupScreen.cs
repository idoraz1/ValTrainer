using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// LOCK IN setup: pick a routine (Short / Lock-In / Training day), see the phase timeline, read a short and honest
/// "why this works", and start. The sens shifter only appears for Training day (off by default, at most 10%).
/// Keyboard: ←→ or 1–3 pick a routine, Space / Enter start, Esc back.
/// </summary>
public partial class WarmupScreen : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    WarmupPlan plan = null!;

    WarmupStore Store => WarmupStore.I;

    public WarmupScreen()
    {
        // Dev "--wupreset short|standard|training": open on that routine (dev runs never save it).
        if (Main.I.Dev && CmdLine.After("--wupreset") is { } p) Store.Preset = WarmupPresets.Get(p).Key;
    }

    void MakePlan() => plan = WarmupPlan.Build(WarmupPresets.Get(Store.Preset), Store.ShiftMode, Store.ShiftPct, Main.I.Sens, WarmupRunner.QuickDev);

    void Pick(string key)
    {
        if (Store.Preset == key) return;
        Store.Preset = key;
        Store.Save();
        Callable.From(Rebuild).CallDeferred();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            var all = WarmupPresets.All;
            int i = Array.FindIndex(all, p => p.Key == plan.Preset.Key);
            int to = key.Keycode switch
            {
                Key.Left => i - 1, Key.Right => i + 1,
                Key.Key1 or Key.Kp1 => 0, Key.Key2 or Key.Kp2 => 1, Key.Key3 or Key.Kp3 => 2,
                _ => -99,
            };
            if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); StartRoutine(); return; }
            if (to != -99) { GetViewport().SetInputAsHandled(); Pick(all[Math.Clamp(to, 0, all.Length - 1)].Key); return; }
        }
        base._UnhandledKeyInput(e);
    }

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
        var title = Heading("LOCK IN", $"A SHORT ROUTINE BEFORE RANKED · {tier.Name.ToUpperInvariant()} TIER · EVERYTHING AT YOUR REAL SENS · ENDS WITH YOUR GOAL AND CUE");
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
        left.AddChild(new SectionLabel { Text = "ROUTINE", Note = "← → or 1–3", K = k });
        var presets = HBox(10 * k);
        foreach (var p in WarmupPresets.All)
        {
            var pp = WarmupPlan.Build(p, ShiftMode.Off, 0, Main.I.Sens, false);
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = p.Name.ToUpperInvariant(),
                Sub = $"≈{WarmupPlan.Minutes(pp.EstimatedSeconds)} · {p.Steps.Length} DRILLS" + (p.Key == WarmupPresets.DefaultKey ? " · DEFAULT" : ""),
                TooltipText = p.Blurb + (p.Note.Length > 0 ? $" ({p.Note})" : ""),
                FontPx = 26, SubPx = 13, CustomMinimumSize = new Vector2(0, 72 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Selected = p.Key == plan.Preset.Key,
            };
            string key = p.Key;
            b.Pressed += () => Pick(key);
            presets.AddChild(b);
        }
        left.AddChild(presets);
        left.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 30 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(16, k);
                float w = Gfx.TextW(UiTheme.Body, plan.Preset.Blurb + ".", fs);
                Gfx.Text(d, UiTheme.Body, plan.Preset.Blurb + ".", 0, Gfx.Mid(d.Size.Y / 2, fs), fs, UiTheme.Text);
                if (plan.Preset.Note.Length > 0)
                    Gfx.TextFit(d, UiTheme.HudWide, "  ·  " + plan.Preset.Note.ToUpperInvariant(), w, Gfx.Mid(d.Size.Y / 2, UiTheme.Fs(14, k)), UiTheme.Fs(14, k), UiTheme.Warn, d.Size.X - w);
            },
        });

        // ---- timeline ----
        var chartPanel = new VPanel
        {
            Title = "TIMELINE", K = k,
            Caption = $"{plan.Steps.Count} drills · {WarmupPlan.Clock(plan.PlaySeconds)} play · ≈{WarmupPlan.Minutes(plan.EstimatedSeconds).ToLowerInvariant()} in total",
        };
        left.AddChild(chartPanel);
        chartPanel.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 250 * k),
            OnDraw = d => WarmupCharts.DrawTimeline(d, new Rect2(Vector2.Zero, d.Size), plan, k),
        });

        // ---- sens shifter: Training day only ----
        if (plan.Preset.AllowShift)
        {
            left.AddChild(new SectionLabel { Text = "SENS SHIFTER", Note = "training only · first phase only · back to your sens before the calibration", K = k });
            var row = HBox(10 * k);
            void ShiftBtn(string label, string sub, ShiftMode m)
            {
                var b = new VButton
                {
                    Kind = VButton.Look.Select, K = k, Label = label, Sub = sub, FontPx = 18, SubPx = 12,
                    CustomMinimumSize = new Vector2(0, 54 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, Selected = Store.ShiftMode == m,
                };
                b.Pressed += () => { Store.ShiftMode = m; Store.Save(); Callable.From(Rebuild).CallDeferred(); };
                row.AddChild(b);
            }
            ShiftBtn("OFF", "default · every drill at your sens", ShiftMode.Off);
            ShiftBtn("HIGHER → YOUR SENS", "start a little faster", ShiftMode.HighToNormal);
            ShiftBtn("LOWER → YOUR SENS", "start a little slower", ShiftMode.LowToNormal);
            row.AddChild(Spacer(10 * k, 0));
            foreach (int pct in WarmupPresets.ShiftOptions)
            {
                string sign = Store.ShiftMode == ShiftMode.LowToNormal ? "−" : "+";
                var b = new VButton
                {
                    Kind = VButton.Look.Select, K = k, Label = $"{sign}{pct}%", FontPx = 20,
                    CustomMinimumSize = new Vector2(90 * k, 54 * k), Selected = Store.ShiftPct == pct, Disabled = Store.ShiftMode == ShiftMode.Off,
                };
                int v = pct;
                b.Pressed += () => { Store.ShiftPct = v; Store.Save(); Callable.From(Rebuild).CallDeferred(); };
                row.AddChild(b);
            }
            left.AddChild(row);
        }

        left.AddChild(new LockInSetupCheck(k)); // same setup as the match: sens, display, VSync, crosshair, headset … (never blocks)

        // ---- start ----
        left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }); // the start button stays at the bottom
        left.AddChild(Spacer(0, 4 * k));
        var startRow = HBox(16 * k);
        var start = Btn("START LOCK-IN", VButton.Look.Primary, StartRoutine, 340, 62, 26);
        startRow.AddChild(start);
        var note = Lbl("Space starts · a 30-second check-in comes first · Esc pauses a drill", UiTheme.Body, 15, UiTheme.Dim);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        startRow.AddChild(note);
        left.AddChild(startRow);

        // ---- right: why + history ----
        var why = new VPanel { Title = "WHY THIS WORKS", K = k, Caption = "what the research supports" };
        right.AddChild(why);
        var wv = VBox(8 * k);
        why.AddChild(wv);
        foreach (var (h, t) in Notes)
        {
            wv.AddChild(Lbl(h, UiTheme.HudWide, 13, UiTheme.Accent));
            var l = Lbl(t, UiTheme.Body, 15, UiTheme.Text);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(440 * k, 0);
            wv.AddChild(l);
        }
        var honest = Lbl("No study has tested an aim-trainer warm-up on ranked results yet.", UiTheme.Body, 13, UiTheme.Faint);
        honest.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        honest.CustomMinimumSize = new Vector2(440 * k, 0);
        wv.AddChild(honest);

        var hist = new VPanel { Title = "YOUR LOCK-INS", K = k, Caption = Store.Warmups.Count == 0 ? "none yet" : $"{Store.Warmups.Count} saved", SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(hist);
        var hv = VBox(14 * k);
        hist.AddChild(hv);
        hv.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 100 * k), OnDraw = DrawHistory });
        hv.AddChild(LockInMatchLog.ReadoutBox(k)); // how your first match after each Lock-In went (optional ratings)
    }

    static readonly (string, string)[] Notes =
    {
        ("SHORT AND AT YOUR REAL SENS",
         "A warm-up wakes up skill you already have. About 10 to 15 minutes at the sens you play with is enough; longer mostly adds fatigue."),
        ("SIMPLE TO GAME-LIKE",
         "Easy drills first, then drills that adapt to keep you near 8 of 10 hits, then bots and a deathmatch. Its last minute is easier, so the session ends on a good note."),
        ("CARRY IT INTO THE MATCH",
         "The effect fades after a 5 to 15 minute break, so queue right away. One goal and one cue word give you something simple to come back to each round."),
    };

    void DrawHistory(DrawBox d)
    {
        float k = K;
        var list = Store.Warmups.Where(w => w.Readiness >= 0).TakeLast(20).ToList();
        int ls = UiTheme.Fs(12, k), fs = UiTheme.Fs(15, k);
        if (Store.Warmups.Count == 0)
        {
            Gfx.TextFit(d, UiTheme.Body, "Finish a Lock-In to see how each one compares with your usual.", 0, Gfx.Mid(20 * k, fs), fs, UiTheme.Dim, d.Size.X);
            return;
        }
        var lastW = Store.Warmups[^1];
        Gfx.Text(d, UiTheme.HudWide, "LAST", 0, Gfx.Mid(14 * k, ls), ls, UiTheme.Faint);
        string lastText = $"{lastW.When:MMM d, HH:mm} · {WarmupPresets.Get(lastW.Preset).Name}" +
                          (lastW.Readiness >= 0 ? $" · {lastW.Readiness:0}% of usual" : "") + (lastW.Cue.Length > 0 ? $" · cue \"{lastW.Cue}\"" : "");
        Gfx.TextFit(d, UiTheme.Body, lastText, 54 * k, Gfx.Mid(14 * k, fs), fs, UiTheme.Text, d.Size.X - 54 * k);
        if (list.Count >= 2)
        {
            Gfx.Text(d, UiTheme.HudWide, "VS YOUR USUAL · LAST 20", 0, Gfx.Mid(44 * k, ls), ls, UiTheme.Faint);
            Sparkline.Draw(d, new Rect2(0, 58 * k, d.Size.X, Mathf.Max(20 * k, d.Size.Y - 66 * k)), list.Select(w => w.Readiness).ToList(), k, UiTheme.Teal);
        }
    }

    void StartRoutine()
    {
        MakePlan();
        WarmupRunner.Start(plan);
    }
}
