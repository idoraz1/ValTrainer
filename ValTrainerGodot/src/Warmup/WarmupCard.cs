using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// Between Lock-In drills, in focus mode: a thin progress strip, what was just played, the next drill and one cue in
/// large type (no research text, no charts). Starts by itself after a short break; Space starts now, Esc pauses the
/// countdown (Esc again quits), and the drill can be skipped or the Lock-In finished early. Past 15 minutes it says the
/// rest is optional; past 20 it says you're ready and FINISH NOW becomes the main button. In the mechanics phase, when
/// the calibration levels are at the player's usual level (<see cref="LockInReady"/>), it also offers SKIP TO DEATHMATCH.
/// </summary>
public partial class WarmupCard : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly WarmupRunner run;
    readonly float autoStart;
    float left;
    bool paused, going;
    /// <summary>Past 20 minutes the next drill doesn't start by itself: finishing is the suggested move.</summary>
    readonly bool hold;
    /// <summary>Mechanics phase, calibration at the player's usual level: a secondary "skip to deathmatch" button (see
    /// <see cref="LockInReady"/>). Only an offer: the countdown still starts the next drill, just a little later.</summary>
    readonly ReadyOffer? offer;
    float shown;

    public WarmupCard(WarmupRunner run)
    {
        this.run = run;
        offer = run.ReadyCheck() is { Offer: true } o ? o : null;
        autoStart = offer != null ? WarmupRunner.QuickDev ? 8f : 15f : WarmupRunner.QuickDev ? 2f : run.NextIndex == 0 ? 12f : 8f;
        left = autoStart;
        hold = TimeToQueue;
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
        shown += dt;
        // Dev "--lockin-ready-take": press SKIP TO DEATHMATCH after a second (headless test of the skip path).
        if (offer != null && !going && shown > 1f && Main.I.Dev && CmdLine.Has("--lockin-ready-take")) { TakeOffer(); return; }
        if (going || paused || hold) return;
        left -= dt;
        if (left <= 0) Go();
    }

    void Go()
    {
        if (going) return;
        going = true;
        Callable.From(run.StartStep).CallDeferred();
    }

    void TakeOffer()
    {
        if (going) return;
        going = true;
        Callable.From(run.SkipToDeathmatch).CallDeferred();
    }

    bool TimeToQueue => run.ElapsedMinutes >= WarmupPlan.ReadyMinutes && run.Results.Any(r => !r.Skipped);

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
        bool ready = TimeToQueue;
        buttons.AddChild(Btn("START NOW  (SPACE)", ready ? VButton.Look.Secondary : VButton.Look.Primary, Go, 330, 60, 22));
        buttons.AddChild(Btn("SKIP DRILL", VButton.Look.Secondary, () => { going = true; run.Skip(); }, 200, 60, 20));
        if (run.Results.Any(r => !r.Skipped))
            buttons.AddChild(Btn("FINISH NOW", ready ? VButton.Look.Primary : VButton.Look.Secondary, () => { going = true; run.FinishNow(); }, 200, 60, 20));
        buttons.AddChild(Btn("QUIT", VButton.Look.Ghost, run.Quit, 130, 60, 20));
        AddChild(buttons);

        if (offer != null)
        {
            const string label = "YOU'RE AT YOUR USUAL LEVEL · SKIP TO DEATHMATCH";
            var row = HBox(0);
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.AnchorLeft = 0; row.AnchorRight = 1; row.AnchorTop = 1; row.AnchorBottom = 1;
            row.OffsetTop = -380 * k; row.OffsetBottom = -326 * k;
            float w = Gfx.TextW(UiTheme.Display, label, UiTheme.Fs(18, k)) / k + 80;
            row.AddChild(Btn(label, VButton.Look.Secondary, TakeOffer, w, 54, 18));
            AddChild(row);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        foreach (var c in GetChildren()) if (c is DrawBox d) d.QueueRedraw();
    }

    /// <summary>"1:00 · REGULAR, ONE TIER DOWN · YOUR SENS" and the like: what's special about this run, in one line.</summary>
    string TagLine(PlannedStep st, out Color col)
    {
        col = UiTheme.Text;
        var parts = new List<string> { WarmupPlan.Clock(st.Seconds) };
        string tier = Difficulty.Get(st.Tier).Name.ToUpperInvariant();
        if (st.Adaptive) { parts.Add("ADAPTIVE: TARGETS ADJUST TO KEEP YOU NEAR 80% HITS"); col = UiTheme.Teal; }
        else if (st.Easier) { parts.Add($"{tier}, ONE TIER DOWN"); col = UiTheme.Teal; }
        else parts.Add(tier);
        if (st.EaseLast > 0) parts.Add($"LAST {WarmupPlan.Clock(st.EaseLast)} EASIER");
        if (st.Shifted) { parts.Add($"SENS {st.Sens.ToString("0.000", Inv)} ({st.OffsetPct:+0;-0}%)"); col = WarmupCharts.ShiftCol; }
        else if (run.Plan.ShiftedCount > 0) parts.Add("YOUR SENS");
        return string.Join("  ·  ", parts);
    }

    void DrawCard(DrawBox d)
    {
        float k = K;
        var size = d.Size;
        var plan = run.Plan;
        var st = run.NextStep;
        if (st == null) return;
        float cx = size.X / 2;

        // header + a thin progress strip (no labels: focus mode)
        int hs = UiTheme.Fs(15, k);
        string who = plan.Preset.Key.StartsWith(Agents.AgentRoutines.Prefix) ? $"LOCK-IN AS {plan.Preset.Name.ToUpperInvariant()}" : "LOCK-IN";
        Gfx.TextC(d, UiTheme.HudWide, $"{who} · {Phases.Name(st.Phase)} · DRILL {st.Index + 1} OF {plan.Steps.Count}", cx, 70 * k, hs, UiTheme.Dim);
        float sw = Mathf.Min(size.X - 160 * k, 900 * k), sx = cx - sw / 2, sy = 92 * k, sh = 8 * k;
        float total = plan.PlaySeconds, t0 = 0;
        foreach (var s in plan.Steps)
        {
            float xa = sx + t0 / total * sw, xb = sx + (t0 + s.Seconds) / total * sw;
            var r = new Rect2(xa + 1.5f * k, sy, xb - xa - 3 * k, sh);
            bool done = s.Index < run.NextIndex, now = s.Index == run.NextIndex;
            bool skipped = done && run.Results[s.Index].Skipped;
            d.DrawRect(r, done ? new Color(UiTheme.Teal, skipped ? 0.15f : 0.6f) : now ? UiTheme.Text : new Color(UiTheme.Text, 0.1f));
            t0 += s.Seconds;
        }

        // previous result
        float y = 190 * k;
        if (run.LastResult is { } last && run.NextIndex > 0)
        {
            var prevStep = plan.Steps[run.NextIndex - 1];
            string res = last.Skipped ? "skipped"
                : last.Adaptive ? last.Level >= 0 ? $"level {WarmupScore.LevelText(last.Level)} at {last.HitRate * 100:0}% hits" : "no level (too few shots)"
                : last.Value < 0 ? "no result"
                : $"{WarmupScore.Label(last.Mode)} {WarmupScore.Format(last.Mode, last.Value)}" +
                  (last.Index >= 0 && last.BaselineKind != "today" ? $" · {last.Index:0}% of {WarmupScore.BaselineText(last.BaselineKind)}" : "");
            int ps = UiTheme.Fs(18, k);
            string head = $"DONE  {prevStep.Name.ToUpperInvariant()}   ";
            float w1 = Gfx.TextW(UiTheme.HudWide, head, UiTheme.Fs(14, k)), w2 = Gfx.TextW(UiTheme.Body, res, ps);
            float x0 = cx - (w1 + w2) / 2;
            Gfx.Text(d, UiTheme.HudWide, head, x0, y, UiTheme.Fs(14, k), UiTheme.Good);
            Gfx.Text(d, UiTheme.Body, res, x0 + w1, y, ps, UiTheme.Dim);
        }

        // next drill, one cue in large type
        Gfx.TextC(d, UiTheme.HudWide, "NEXT", cx, 290 * k, UiTheme.Fs(18, k), UiTheme.Accent);
        Gfx.TextC(d, UiTheme.Display, st.Name.ToUpperInvariant(), cx, 390 * k, UiTheme.Fs(104, k), UiTheme.Text);
        string tag = TagLine(st, out var tagCol);
        Gfx.TextFit(d, UiTheme.HudWide, tag, cx - (size.X - 160 * k) / 2, 440 * k, UiTheme.Fs(20, k), tagCol, size.X - 160 * k, HorizontalAlignment.Center);

        float cw = Mathf.Min(size.X - 160 * k, 1400 * k);
        bool backToReal = !st.Shifted && run.NextIndex > 0 && plan.Steps[run.NextIndex - 1].Shifted;
        float cueY = 560 * k;
        if (backToReal)
        {
            var br = new Rect2(cx - cw / 2, 474 * k, cw, 40 * k);
            d.DrawRect(br, new Color(WarmupCharts.RealCol, 0.12f));
            Gfx.TextFit(d, UiTheme.HudWide, "BACK TO YOUR REAL SENS FROM HERE ON", br.Position.X + 18 * k,
                Gfx.Mid(br.GetCenter().Y, UiTheme.Fs(15, k)), UiTheme.Fs(15, k), WarmupCharts.RealCol, br.Size.X - 30 * k, HorizontalAlignment.Center);
            cueY = 580 * k;
        }
        Gfx.TextFit(d, UiTheme.Body, st.Cue, cx - cw / 2, cueY, UiTheme.Fs(34, k), UiTheme.Text, cw, HorizontalAlignment.Center);
        if (st.Phase == Phase.Deathmatch && (run.Check.Goal.Length > 0 || run.Check.Cue.Length > 0))
        {
            string carry = $"YOUR GOAL: {run.Check.Goal.ToUpperInvariant()}   ·   YOUR CUE: {run.Check.Cue.ToUpperInvariant()}";
            Gfx.TextFit(d, UiTheme.HudWide, carry, cx - cw / 2, cueY + 50 * k, UiTheme.Fs(17, k), UiTheme.Dim, cw, HorizontalAlignment.Center);
        }

        // ready offer: today's calibration levels next to the usual range (the button itself is in Build)
        if (offer != null)
        {
            string lv = string.Join("   ·   ", offer.Drills.Select(x =>
                $"{WarmupDrills.Name(x.Mode).ToUpperInvariant()} {x.Level.ToString("0.0", Inv)} (USUAL {Mathf.Max(0, x.Low).ToString("0.0", Inv)}–{Mathf.Min(4, x.High).ToString("0.0", Inv)})"));
            Gfx.TextFit(d, UiTheme.HudWide, "CALIBRATION TODAY   " + lv, cx - cw / 2, size.Y - 404 * k, UiTheme.Fs(14, k), UiTheme.Dim, cw, HorizontalAlignment.Center);
        }

        // 15 / 20 minute reminders
        float mins = run.ElapsedMinutes;
        if (mins >= WarmupPlan.RemindMinutes && run.Results.Any(r => !r.Skipped))
        {
            bool ready = mins >= WarmupPlan.ReadyMinutes;
            string text = ready ? "YOU'RE READY · 20 MINUTES IN · FINISH NOW AND QUEUE" : "15 MINUTES IN · THE REST IS OPTIONAL · FINISH WHEN YOU LIKE";
            var c = ready ? UiTheme.Teal : UiTheme.Warn;
            int rs = UiTheme.Fs(16, k);
            float rw = Gfx.TextW(UiTheme.HudWide, text, rs) + 60 * k;
            var rr = new Rect2(cx - rw / 2, size.Y - 290 * k, rw, 42 * k);
            d.DrawRect(rr, new Color(c, 0.12f));
            d.DrawRect(new Rect2(rr.Position, new Vector2(4 * k, rr.Size.Y)), c);
            Gfx.TextC(d, UiTheme.HudWide, text, cx, Gfx.Mid(rr.GetCenter().Y, rs), rs, c);
        }

        // countdown
        float ty = size.Y - 200 * k;
        if (going) Gfx.TextC(d, UiTheme.HudWide, "LOADING…", cx, ty, UiTheme.Fs(20, k), UiTheme.Dim);
        else if (hold) Gfx.TextC(d, UiTheme.HudWide, "YOUR CALL · SPACE STARTS THE NEXT DRILL", cx, ty, UiTheme.Fs(18, k), UiTheme.Dim);
        else if (paused)
            Gfx.TextC(d, UiTheme.HudWide, "PAUSED · SPACE TO START · ESC AGAIN TO QUIT THE LOCK-IN", cx, ty, UiTheme.Fs(18, k), UiTheme.Warn);
        else
        {
            Gfx.TextC(d, UiTheme.HudWide, $"STARTING IN {Mathf.CeilToInt(Mathf.Max(0, left))}", cx, ty, UiTheme.Fs(20, k), UiTheme.Text);
            float bw = 360 * k;
            d.DrawRect(new Rect2(cx - bw / 2, ty + 14 * k, bw, 3 * k), new Color(UiTheme.Text, 0.12f));
            d.DrawRect(new Rect2(cx - bw / 2, ty + 14 * k, bw * Mathf.Clamp(left / autoStart, 0, 1), 3 * k), UiTheme.Accent);
        }
        Gfx.TextC(d, UiTheme.HudWide, "ESC PAUSES · IN A DRILL, ESC → BACK TO MENU ENDS THE LOCK-IN", cx, size.Y - 40 * k, UiTheme.Fs(12, k), UiTheme.Faint);
    }
}
