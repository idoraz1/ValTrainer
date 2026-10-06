using Godot;
using ValTrainer.Core;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// The breathing guide at the end of a Lock-In (only when the player wants it): a circle that grows for 4 s (IN) and
/// shrinks for 6 s (OUT), about 6 breaths a minute with the breath out longer than the breath in, for 60–120 s. A short
/// "get ready" lead-in comes first. Space, Enter or Esc skips; it never claims to change aim.
/// </summary>
public partial class LockInBreath : ScreenBase
{
    public const float In = 4f, Out = 6f, LeadIn = 3f;
    readonly float seconds;
    readonly Action<float> finished;
    readonly Action skipped;
    float t = -LeadIn;
    bool over;

    /// <param name="finished">Called with the seconds breathed when the guide ran out (or was skipped after 20 s).</param>
    /// <param name="skipped">Called when skipped early.</param>
    public LockInBreath(int seconds, Action<float> finished, Action skipped)
    {
        this.seconds = WarmupRunner.QuickDev ? Math.Min(seconds, 10) : seconds;
        this.finished = finished;
        this.skipped = skipped;
    }

    protected override void Back() => Skip();

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Space or Key.Enter or Key.KpEnter)
        {
            GetViewport().SetInputAsHandled();
            Skip();
            return;
        }
        base._UnhandledKeyInput(e);
    }

    void Skip()
    {
        if (over) return;
        over = true;
        float done = Mathf.Max(0, t);
        Log.Info($"[lockin] breathing skipped after {done:0}s of {seconds:0}s");
        if (done >= 20) Callable.From(() => finished(done)).CallDeferred();
        else Callable.From(skipped).CallDeferred();
    }

    protected override void Tick(float dt)
    {
        if (over) return;
        t += dt;
        if (t >= seconds)
        {
            over = true;
            Log.Info($"[lockin] breathing done: {seconds:0}s");
            GetTree().CreateTimer(1.0).Timeout += () => { if (IsInstanceValid(this) && IsInsideTree()) finished(seconds); };
        }
    }

    public override void _Ready()
    {
        base._Ready();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        foreach (var c in GetChildren()) if (c is DrawBox d) d.QueueRedraw();
    }

    protected override void Build()
    {
        float k = K;
        AddChild(new DrawBox { AnchorRight = 1, AnchorBottom = 1, OnDraw = DrawGuide });
        var buttons = HBox(14 * k);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.AnchorLeft = 0; buttons.AnchorRight = 1; buttons.AnchorTop = 1; buttons.AnchorBottom = 1;
        buttons.OffsetTop = -156 * k; buttons.OffsetBottom = -100 * k;
        buttons.AddChild(Btn("SKIP  (SPACE)", VButton.Look.Secondary, Skip, 240, 56, 20));
        AddChild(buttons);
    }

    /// <summary>Circle size 0..1 and the phase at time <paramref name="x"/> (s since the first breath).</summary>
    static (float Size, bool Inhale, int Left) Pace(float x)
    {
        float c = x % (In + Out);
        if (c < In)
        {
            float p = c / In;
            return (0.5f - 0.5f * Mathf.Cos(p * Mathf.Pi), true, Mathf.CeilToInt(In - c));
        }
        float q = (c - In) / Out;
        return (0.5f + 0.5f * Mathf.Cos(q * Mathf.Pi), false, Mathf.CeilToInt(In + Out - c));
    }

    void DrawGuide(DrawBox d)
    {
        float k = K;
        var size = d.Size;
        float cx = size.X / 2, cy = size.Y * 0.46f;
        Gfx.TextC(d, UiTheme.HudWide, "LOCK IN · BREATHE", cx, 70 * k, UiTheme.Fs(15, k), UiTheme.Dim);

        float rMin = 70 * k, rMax = Mathf.Min(230 * k, size.Y * 0.27f);
        bool lead = t < 0, end = t >= seconds;
        var (s, inhale, left) = lead || end ? (0f, true, 0) : Pace(t);
        float r = rMin + (rMax - rMin) * s;
        var col = inhale ? UiTheme.Teal : new Color(0.55f, 0.72f, 0.95f);
        // guide rings: the full breath and the empty one
        d.DrawArc(new Vector2(cx, cy), rMax, 0, Mathf.Tau, 96, new Color(UiTheme.Text, 0.12f), Mathf.Max(1, 1.5f * k), true);
        d.DrawArc(new Vector2(cx, cy), rMin, 0, Mathf.Tau, 64, new Color(UiTheme.Text, 0.08f), Mathf.Max(1, k), true);
        d.DrawCircle(new Vector2(cx, cy), r, new Color(col, 0.16f));
        d.DrawArc(new Vector2(cx, cy), r, 0, Mathf.Tau, 96, new Color(col, 0.9f), Mathf.Max(2, 3 * k), true);

        int big = UiTheme.Fs(64, k), num = UiTheme.Fs(28, k);
        if (lead)
        {
            Gfx.TextC(d, UiTheme.Display, "GET READY", cx, Gfx.Mid(cy, UiTheme.Fs(40, k)), UiTheme.Fs(40, k), UiTheme.Text);
            Gfx.TextC(d, UiTheme.Body, Mathf.CeilToInt(-t).ToString(), cx, cy + 56 * k, num, UiTheme.Dim);
        }
        else if (end) Gfx.TextC(d, UiTheme.Display, "DONE", cx, Gfx.Mid(cy, big), big, UiTheme.Text);
        else
        {
            Gfx.TextC(d, UiTheme.Display, inhale ? "IN" : "OUT", cx, Gfx.Mid(cy - 8 * k, big), big, UiTheme.Text);
            Gfx.TextC(d, UiTheme.Display, left.ToString(), cx, Gfx.Mid(cy + 50 * k, num), num, new Color(UiTheme.Text, 0.7f));
        }

        float ty = cy + rMax + 64 * k;
        Gfx.TextC(d, UiTheme.Body, LockInText.BreathLine, cx, ty, UiTheme.Fs(24, k), UiTheme.Text);
        Gfx.TextC(d, UiTheme.Body, "4 seconds in, 6 seconds out.", cx, ty + 36 * k, UiTheme.Fs(17, k), UiTheme.Dim);

        // remaining time: small, out of the way
        float rem = Mathf.Clamp(seconds - Mathf.Max(0, t), 0, seconds);
        float bw = 300 * k, by = size.Y - 48 * k;
        d.DrawRect(new Rect2(cx - bw / 2, by, bw, 3 * k), new Color(UiTheme.Text, 0.1f));
        d.DrawRect(new Rect2(cx - bw / 2, by, bw * (1 - rem / seconds), 3 * k), new Color(UiTheme.Teal, 0.8f));
        Gfx.TextC(d, UiTheme.HudWide, $"{WarmupPlan.Clock(rem)} LEFT", cx, by - 10 * k, UiTheme.Fs(12, k), UiTheme.Faint);
    }
}
