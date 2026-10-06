using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Warmup;

/// <summary>
/// The end of a Lock-In ("YOU'RE LOCKED IN"): the goal and cue word in big type, the buy-phase micro-routine, what to
/// do next (queue now, Range while in queue, a few deliberate flicks in the first buy phase), LAUNCH VALORANT when the
/// Riot Client is installed, and a short, neutral look at today (what went well, then above / in / below your usual
/// range). DETAILS opens the lock-in graph and the drill table. Space / Enter / Esc: menu.
/// </summary>
public partial class LockInHandoff : ScreenBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly WarmupRecord rec;
    readonly List<WarmupRecord> previous;
    /// <summary>Dev demo data (shown with a marker).</summary>
    public bool Demo;
    bool launched;

    public LockInHandoff(WarmupRecord rec, List<WarmupRecord> previous)
    {
        this.rec = rec;
        this.previous = previous;
    }

    protected override void Back() => Main.I.ShowMenu();

    public override void _Ready()
    {
        base._Ready();
        // Dev "--wuquit": quit a few seconds after the hand-off appears (headless validation runs).
        if (Main.I.Dev && CmdLine.Has("--wuquit")) GetTree().CreateTimer(3.0).Timeout += () => GetTree().Quit();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); Main.I.ShowMenu(); return; }
            if (key.Keycode == Key.D) { GetViewport().SetInputAsHandled(); ShowDetails(); return; }
        }
        base._UnhandledKeyInput(e);
    }

    void ShowDetails() => Main.I.ShowScreen(new WarmupSummary(rec, previous) { Demo = Demo });

    void Launch()
    {
        if (launched) return;
        launched = true; // one click, one start
        RiotClient.Launch();
        Rebuild();
    }

    protected override void Build()
    {
        float k = K;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var root = Margins(VBox(18 * k), 56 * k, 30 * k, 56 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        var preset = WarmupPresets.Get(rec.Preset);
        string who = rec.Preset.StartsWith(Agents.AgentRoutines.Prefix) ? $"AS {preset.Name.ToUpperInvariant()}" : preset.Name.ToUpperInvariant();
        var title = Heading("YOU'RE LOCKED IN",
            $"{who} · {Difficulty.Get(rec.Tier).Name.ToUpperInvariant()} TIER · {Clock(rec)} IN TOTAL · {rec.Steps.Count(s => !s.Skipped)} DRILLS" +
            (Demo && !CmdLine.Showcase ? " · DEMO DATA" : ""));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var details = Btn("DETAILS  (D)", VButton.Look.Secondary, ShowDetails, 190, 52, 20);
        details.TooltipText = "The lock-in graph and every drill's result";
        details.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(details);
        var menu = Btn("MENU", VButton.Look.Secondary, Main.I.ShowMenu, 150, 52, 22);
        menu.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(menu);
        col.AddChild(head);

        var body = HBox(24 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);

        // ---- left: goal, cue, buy-phase routine ----
        var left = new VPanel { K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, Pad = 34 };
        left.SizeFlagsStretchRatio = 1.25f;
        body.AddChild(left);
        left.AddChild(new DrawBox { SizeFlagsVertical = SizeFlags.ExpandFill, OnDraw = DrawGoal });

        // ---- right: next + today ----
        var right = VBox(18 * k);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(right);
        var next = new VPanel { Title = "NEXT", K = k, Caption = "the warm-up fades after a 5–15 min break" };
        right.AddChild(next);
        var nv = VBox(12 * k);
        next.AddChild(nv);
        nv.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, 140 * k), OnDraw = DrawNext });
        if (RiotClient.Found)
        {
            var launch = Btn(launched ? "STARTING VALORANT…" : "LAUNCH VALORANT", VButton.Look.Primary, Launch, 0, 60, 24);
            launch.Disabled = launched;
            launch.TooltipText = "Starts VALORANT through the Riot Client";
            nv.AddChild(launch);
        }
        var today = new VPanel { Title = "TODAY", K = k, Caption = "your Lock-In vs your usual", SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(today);
        today.AddChild(new DrawBox { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200 * k), OnDraw = DrawToday });
    }

    static string Clock(WarmupRecord r) => WarmupPlan.Clock(r.TotalSeconds > 0 ? r.TotalSeconds : r.PlaySeconds);

    void DrawGoal(DrawBox d)
    {
        float k = K, w = d.Size.X, h = d.Size.Y;
        int ls = UiTheme.Fs(15, k);
        string goal = rec.Goal.Length > 0 ? rec.Goal : "Play your game";
        string cue = rec.Cue.Length > 0 ? rec.Cue : "Calm";

        // goal and cue, centred in the space above the buy-phase card
        int gs = UiTheme.Fs(54, k), cs = UiTheme.Fs(140, k);
        while (gs > UiTheme.Fs(30, k) && Gfx.TextW(UiTheme.Display, goal.ToUpperInvariant(), gs) > w) gs -= 2;
        while (cs > UiTheme.Fs(50, k) && Gfx.TextW(UiTheme.Display, cue.ToUpperInvariant(), cs) > w) cs -= 4;
        float bh = 128 * k, by = h - bh;
        float block = 14 * k + gs * 0.8f + 64 * k + 10 * k + cs * 0.78f;
        float y = Mathf.Max(18 * k, (by - 40 * k - block) / 2);
        Gfx.Text(d, UiTheme.HudWide, "YOUR GOAL", 0, y, ls, UiTheme.Dim);
        Gfx.TextFit(d, UiTheme.Display, goal.ToUpperInvariant(), 0, y + 14 * k + gs * 0.8f, gs, UiTheme.Text, w);
        y += 14 * k + gs * 0.8f + 64 * k;
        Gfx.Text(d, UiTheme.HudWide, "YOUR CUE", 0, y, ls, UiTheme.Dim);
        Gfx.Text(d, UiTheme.Display, cue.ToUpperInvariant(), 0, y + 10 * k + cs * 0.78f, cs, UiTheme.Accent);

        // buy-phase micro-routine: three steps along the bottom
        Gfx.Text(d, UiTheme.HudWide, "EVERY BUY PHASE", 0, by - 16 * k, ls, UiTheme.Dim);
        string[] steps = { "ONE LONG BREATH OUT", "SAY YOUR CUE", "THINK YOUR GOAL" };
        float gap = 12 * k, bw = (w - gap * 2) / 3;
        int ns = UiTheme.Fs(40, k), ts = UiTheme.Fs(17, k);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect2(i * (bw + gap), by, bw, bh);
            Gfx.Plate(d, r, 12 * k, new Color(0.05f, 0.08f, 0.11f, 0.9f), new Color(UiTheme.Text, 0.12f));
            Gfx.Text(d, UiTheme.Display, (i + 1).ToString(Inv), r.Position.X + 18 * k, r.Position.Y + 18 * k + ns * 0.75f, ns, UiTheme.Teal);
            Gfx.TextFit(d, UiTheme.HudWide, steps[i], r.Position.X + 18 * k, r.End.Y - 26 * k, ts, UiTheme.Text, r.Size.X - 30 * k);
        }
    }

    void DrawNext(DrawBox d)
    {
        float k = K, w = d.Size.X, y = 22 * k;
        int fs = UiTheme.Fs(20, k);
        foreach (var line in new[] { "Queue now.", "Shoot bots in the Range while you wait.", "Take 3–5 deliberate flicks in the first buy phase." })
        {
            Gfx.Diamond(d, new Vector2(6 * k, y - 7 * k), 4.5f * k, 4.5f * k, UiTheme.Accent);
            Gfx.TextFit(d, UiTheme.Body, line, 22 * k, y, fs, UiTheme.Text, w - 22 * k);
            y += 36 * k;
        }
        // the personal match log (LockInMatchLog): one quiet line, so the question later isn't a surprise
        Gfx.TextFit(d, UiTheme.Body, LockInMatchLog.HandoffLine, 0, y - 2 * k, UiTheme.Fs(14, k), UiTheme.Faint, w);
    }

    /// <summary>Up to three honest highlights: the best fixed drill vs your usual, the best adaptive level vs your
    /// previous Lock-Ins, and the deathmatch. Nothing is shown that the numbers don't back.</summary>
    IEnumerable<string> WentWell()
    {
        var played = rec.Steps.Where(s => !s.Skipped).ToList();
        if (played.Where(s => !s.Adaptive && s.Index >= 0 && s.BaselineKind is "7d" or "recent").OrderByDescending(s => s.Index).FirstOrDefault() is { } best && best.Index >= 94)
            yield return $"{WarmupDrills.Name(best.Mode)}: {best.Index:0}% of {WarmupScore.BaselineText(best.BaselineKind)}.";
        var adapt = played.Where(s => s.Adaptive && s.Level >= 0).ToList();
        if (adapt.Count > 0)
        {
            var a = adapt.OrderByDescending(s => s.PrevLevel >= 0 ? s.Level - s.PrevLevel : s.Level - 10).First();
            string hits = a.HitRate >= 0 ? $"{a.HitRate * 100:0}% hits" : "your hits";
            yield return $"{WarmupDrills.Name(a.Mode)}: held {hits} at level {WarmupScore.LevelText(a.Level)}" +
                         (a.PrevLevel >= 0 ? $" (your last Lock-Ins: {a.PrevLevel.ToString("0.0", Inv)})." : ".");
        }
        if (played.FirstOrDefault(s => s.Mode == "deathmatch") is { } dm && dm.Kills > 0)
            yield return $"Deathmatch: {dm.Kills} kills{(dm.Value > 0 ? $", {dm.Value.ToString("0.0", Inv)} per minute" : "")}.";
        else if (rec.Completed) yield return $"All {played.Count} drills done.";
    }

    void DrawToday(DrawBox d)
    {
        float k = K, w = d.Size.X;
        var (headText, sub, col) = WarmupScore.Verdict(rec.Readiness, rec.ReadinessKind);
        int hs = UiTheme.Fs(32, k), ss = UiTheme.Fs(15, k), ls = UiTheme.Fs(16, k), cs = UiTheme.Fs(12, k);
        float hx = WarmupCharts.VerdictMark(d, rec.Readiness, new Vector2(0, 30 * k - hs * 0.36f), k, col); // a shape, not just the colour
        Gfx.TextFit(d, UiTheme.Display, headText, hx, 30 * k, hs, col, w - hx);
        d.DrawMultilineString(UiTheme.Body, new Vector2(0, 48 * k + UiTheme.Body.GetAscent(ss)), sub, HorizontalAlignment.Left, w, ss, 2, UiTheme.Dim);

        float y = 116 * k;
        var good = WentWell().Take(3).ToList();
        if (good.Count > 0)
        {
            Gfx.Text(d, UiTheme.HudWide, "WHAT WENT WELL", 0, y, cs, UiTheme.Faint);
            y += 26 * k;
            foreach (var g in good)
            {
                Gfx.Diamond(d, new Vector2(6 * k, y - 6 * k), 4 * k, 4 * k, UiTheme.Good);
                Gfx.TextFit(d, UiTheme.Body, g, 20 * k, y, ls, UiTheme.Text, w - 20 * k);
                y += 28 * k;
            }
        }
        // time: honest about long sessions
        float mins = (rec.TotalSeconds > 0 ? rec.TotalSeconds : rec.PlaySeconds) / 60f;
        string time = mins > WarmupPlan.RemindMinutes
            ? $"{Clock(rec)} in total. That's past 15 minutes. Next time, a shorter routine is enough."
            : $"{Clock(rec)} in total.";
        float ty = Mathf.Max(y + 8 * k, d.Size.Y - 6 * k);
        Gfx.TextFit(d, UiTheme.Body, time, 0, ty, UiTheme.Fs(14, k), mins > WarmupPlan.RemindMinutes ? UiTheme.Warn : UiTheme.Faint, w);
    }
}
