using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// Between warm-up drills: what was just played, what's next ("Next: Strafe Tracking · 1:00 · sens 0.480 → 0.453"),
/// the focus cue and a progress strip. Starts by itself after a short break; Space starts now, Esc pauses the
/// countdown (Esc again quits), and the drill can be skipped or the warm-up finished early.
/// </summary>
public partial class WarmupCard : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly WarmupRunner run;
    readonly float autoStart;
    float left;
    bool paused, going;

    public WarmupCard(WarmupRunner run)
    {
        this.run = run;
        autoStart = WarmupRunner.QuickDev ? 2f : run.NextIndex == 0 ? 12f : 8f;
        left = autoStart;
    }

    protected override void Back()
    {
        if (!paused) { paused = true; return; }
        run.Quit();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Space or Key.Enter or Key.KpEnter)
        {
            GetViewport().SetInputAsHandled();
            Go();
            return;
        }
        base._UnhandledKeyInput(e);
    }

    protected override void Tick(float dt)
    {
        if (going || paused) return;
        left -= dt;
        if (left <= 0) Go();
    }

    void Go()
    {
        if (going) return;
        going = true;
        Callable.From(run.StartStep).CallDeferred();
    }

    protected override void Build()
    {
        float k = K;
        var st = run.NextStep;
        if (st == null) return;

        AddChild(new DrawBox { AnchorRight = 1, AnchorBottom = 1, OnDraw = DrawCard });

        var buttons = HBox(14 * k);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.AnchorLeft = 0; buttons.AnchorRight = 1; buttons.AnchorTop = 1; buttons.AnchorBottom = 1;
        buttons.OffsetTop = -150 * k; buttons.OffsetBottom = -88 * k;
        buttons.AddChild(Btn("START NOW  (SPACE)", VButton.Look.Primary, Go, 330, 60, 22));
        buttons.AddChild(Btn("SKIP DRILL", VButton.Look.Secondary, () => { going = true; run.Skip(); }, 200, 60, 20));
        if (run.Results.Any(r => !r.Skipped))
            buttons.AddChild(Btn("FINISH NOW", VButton.Look.Secondary, () => { going = true; run.FinishNow(); }, 200, 60, 20));
        buttons.AddChild(Btn("QUIT", VButton.Look.Ghost, run.Quit, 130, 60, 20));
        AddChild(buttons);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        foreach (var c in GetChildren()) if (c is DrawBox d) d.QueueRedraw();
    }

    void DrawCard(DrawBox d)
    {
        float k = K;
        var size = d.Size;
        var plan = run.Plan;
        var st = run.NextStep;
        if (st == null) return;
        float cx = size.X / 2;

        // header + progress strip
        int hs = UiTheme.Fs(15, k);
        Gfx.TextC(d, UiTheme.HudWide, $"WARM-UP · {plan.Preset.Name.ToUpperInvariant()} · DRILL {st.Index + 1} OF {plan.Steps.Count}", cx, 70 * k, hs, UiTheme.Dim);
        float sw = Mathf.Min(size.X - 160 * k, 1200 * k), sx = cx - sw / 2, sy = 96 * k, sh = 34 * k;
        float total = plan.PlaySeconds, t0 = 0;
        int ls = UiTheme.Fs(11, k);
        foreach (var s in plan.Steps)
        {
            float xa = sx + t0 / total * sw, xb = sx + (t0 + s.Seconds) / total * sw;
            var r = new Rect2(xa + 1.5f * k, sy, xb - xa - 3 * k, sh);
            bool done = s.Index < run.NextIndex, now = s.Index == run.NextIndex;
            bool skipped = done && run.Results[s.Index].Skipped;
            var col = s.Shifted ? WarmupCharts.ShiftCol : WarmupCharts.RealCol;
            d.DrawRect(r, done ? new Color(col, skipped ? 0.12f : 0.4f) : new Color(0.07f, 0.11f, 0.15f, 0.9f));
            d.DrawRect(r, now ? UiTheme.Text : new Color(UiTheme.Text, 0.12f), false, now ? Mathf.Max(1, 2 * k) : 1);
            if (now) d.DrawRect(new Rect2(r.Position.X, r.End.Y + 4 * k, r.Size.X, 3 * k), UiTheme.Accent);
            Gfx.TextFit(d, UiTheme.HudWide, s.Short, r.Position.X + 2, Gfx.Mid(r.GetCenter().Y, ls), ls, done || now ? UiTheme.Text : UiTheme.Faint,
                r.Size.X - 4, HorizontalAlignment.Center);
            t0 += s.Seconds;
        }

        // previous result
        float y = 205 * k;
        if (run.LastResult is { } last && run.NextIndex > 0)
        {
            var prevStep = plan.Steps[run.NextIndex - 1];
            string res = last.Skipped ? "skipped" : last.Value < 0 ? "no result"
                : $"{WarmupScore.Label(last.Mode)} {WarmupScore.Format(last.Mode, last.Value)}" +
                  (last.Index >= 0 && last.BaselineKind != "today" ? $" · {last.Index:0}% of {WarmupScore.BaselineText(last.BaselineKind)}" : "");
            int ps = UiTheme.Fs(18, k);
            string head = $"DONE  {prevStep.Name.ToUpperInvariant()}   ";
            float w1 = Gfx.TextW(UiTheme.HudWide, head, UiTheme.Fs(14, k)), w2 = Gfx.TextW(UiTheme.Body, res, ps);
            float x0 = cx - (w1 + w2) / 2;
            Gfx.Text(d, UiTheme.HudWide, head, x0, y, UiTheme.Fs(14, k), UiTheme.Good);
            Gfx.Text(d, UiTheme.Body, res, x0 + w1, y, ps, UiTheme.Text);
        }

        // next drill
        Gfx.TextC(d, UiTheme.HudWide, "NEXT", cx, 300 * k, UiTheme.Fs(18, k), UiTheme.Accent);
        Gfx.TextC(d, UiTheme.Display, st.Name.ToUpperInvariant(), cx, 400 * k, UiTheme.Fs(112, k), UiTheme.Text);
        float prevSens = run.NextIndex > 0 ? plan.Steps[run.NextIndex - 1].Sens : st.Sens;
        string sensText = Mathf.Abs(prevSens - st.Sens) > 1e-4f ? $"SENS {prevSens.ToString("0.000", Inv)} → {st.Sens.ToString("0.000", Inv)}" : $"SENS {st.Sens.ToString("0.000", Inv)}";
        string tag = st.Shifted ? $"  ({st.OffsetPct:+0;-0}% SHIFTED)" : "  (YOUR SENS)";
        string line = $"{WarmupPlan.Clock(st.Seconds)}  ·  {Difficulty.Get(run.Tier).Name.ToUpperInvariant()}  ·  {sensText}{tag}";
        Gfx.TextC(d, UiTheme.HudWide, line, cx, 452 * k, UiTheme.Fs(22, k), st.Shifted ? WarmupCharts.ShiftCol : UiTheme.Text);

        bool backToReal = !st.Shifted && run.NextIndex > 0 && plan.Steps[run.NextIndex - 1].Shifted;
        int cs = UiTheme.Fs(21, k);
        float cw = Mathf.Min(size.X - 200 * k, 1100 * k);
        if (backToReal)
        {
            var br = new Rect2(cx - cw / 2, 486 * k, cw, 44 * k);
            d.DrawRect(br, new Color(WarmupCharts.RealCol, 0.12f));
            d.DrawRect(new Rect2(br.Position, new Vector2(4 * k, br.Size.Y)), WarmupCharts.RealCol);
            Gfx.TextFit(d, UiTheme.HudWide, "BACK TO YOUR REAL SENS · the rest of the warm-up is at the sens you play with", br.Position.X + 18 * k,
                Gfx.Mid(br.GetCenter().Y, UiTheme.Fs(15, k)), UiTheme.Fs(15, k), WarmupCharts.RealCol, br.Size.X - 30 * k, HorizontalAlignment.Center);
        }
        Gfx.TextFit(d, UiTheme.Body, st.Cue, cx - cw / 2, 580 * k, cs, UiTheme.Dim, cw, HorizontalAlignment.Center);

        // countdown
        float ty = size.Y - 200 * k;
        if (going) Gfx.TextC(d, UiTheme.HudWide, "LOADING…", cx, ty, UiTheme.Fs(20, k), UiTheme.Dim);
        else if (paused)
            Gfx.TextC(d, UiTheme.HudWide, "PAUSED · SPACE TO START · ESC AGAIN TO QUIT THE WARM-UP", cx, ty, UiTheme.Fs(18, k), UiTheme.Warn);
        else
        {
            Gfx.TextC(d, UiTheme.HudWide, $"STARTING IN {Mathf.CeilToInt(Mathf.Max(0, left))}", cx, ty, UiTheme.Fs(20, k), UiTheme.Text);
            float bw = 360 * k;
            d.DrawRect(new Rect2(cx - bw / 2, ty + 14 * k, bw, 3 * k), new Color(UiTheme.Text, 0.12f));
            d.DrawRect(new Rect2(cx - bw / 2, ty + 14 * k, bw * Mathf.Clamp(left / autoStart, 0, 1), 3 * k), UiTheme.Accent);
        }
        Gfx.TextC(d, UiTheme.HudWide, "ESC PAUSES · IN A DRILL, ESC → BACK TO MENU ENDS THE WARM-UP", cx, size.Y - 40 * k, UiTheme.Fs(12, k), UiTheme.Faint);
    }
}
