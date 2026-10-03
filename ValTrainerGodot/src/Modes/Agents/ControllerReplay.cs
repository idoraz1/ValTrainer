using Godot;
using ValTrainer.Game.Fx;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// Results-screen overlay of the Smoke Execute drill ("Smoke replay" button): a top-down replay of each round on the
/// tactical map — your path, your smokes growing and fading, the standard spots (dashed), the targeted defenders'
/// sight lines turning green as your smokes cut them, defenders going down — with the round's numbers beside it.
/// Runs while the session is frozen on the results screen (process mode Always) and takes all input until closed.
/// </summary>
public partial class ControllerReplay : CanvasLayer
{
    internal SmokeExecuteMode? Mode;
    int round;
    double t0;
    Control view = null!;
    Rect2 prevBtn, nextBtn, closeBtn;
    const float Speed = 1.6f;

    public override void _Ready()
    {
        Layer = 7;
        ProcessMode = ProcessModeEnum.Always;
        view = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        view.Draw += Paint;
        AddChild(view);
        if (Mode != null) round = Math.Max(0, Mode.Rounds.Count - 1);
        t0 = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Process(double delta) => view.QueueRedraw();

    public override void _Input(InputEvent e)
    {
        if (Mode == null) return;
        int n = Mode.Rounds.Count;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.Escape) Close();
            else if (k.Keycode == Key.Left && round > 0) Go(round - 1);
            else if (k.Keycode == Key.Right && round + 1 < n) Go(round + 1);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.Right || closeBtn.HasPoint(mb.Position)) Close();
            else if (mb.ButtonIndex == MouseButton.Left && prevBtn.HasPoint(mb.Position) && round > 0) Go(round - 1);
            else if (mb.ButtonIndex == MouseButton.Left && nextBtn.HasPoint(mb.Position) && round + 1 < n) Go(round + 1);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton) GetViewport().SetInputAsHandled();
    }

    void Go(int r) { round = r; t0 = Time.GetTicksMsec() / 1000.0; UiTheme.ClickSound(); }

    void Close() { UiTheme.ClickSound(); QueueFree(); }

    void Paint()
    {
        var m = Mode;
        if (m == null || m.Rounds.Count == 0) return;
        var size = view.GetViewportRect().Size;
        float k = UiTheme.S(size);
        var r = m.Rounds[Math.Clamp(round, 0, m.Rounds.Count - 1)];
        float dur = Mathf.Max(1f, r.End - r.Start);
        float t = (float)((Time.GetTicksMsec() / 1000.0 - t0) * Speed % (dur + 2.0));
        t = Mathf.Min(t, dur);

        view.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.035f, 0.05f, 0.94f));
        var (mr, info) = ControllerDraw.Columns(size, k);
        mr = new Rect2(mr.Position, new Vector2(mr.Size.X, mr.Size.Y - 40 * k));
        Gfx.Plate(view, mr, 18 * k, new Color(0.04f, 0.07f, 0.1f, 0.95f), new Color(UiTheme.Text, 0.12f));
        m.TMap.Fit(mr.Grow(-16 * k));
        var smokes = r.Smokes.Select(s => s.V).ToList();
        var tints = r.Smokes.ToDictionary(s => s.V, s => s.Tint);
        ControllerDraw.MapLayer(view, k, m, new ControllerDraw.Layer
        {
            Standard = true, Sightlines = true, Smokes = smokes, Time = r.Start + t, Tint = v => tints.TryGetValue(v, out var c) ? c : Colors.White,
            Defs = r.Defs, DefsT = t, Path = r.Path, PathT = t,
        });

        // timeline
        var tl = new Rect2(mr.Position.X, mr.End.Y + 16 * k, mr.Size.X, 6 * k);
        view.DrawRect(tl, new Color(1, 1, 1, 0.1f));
        view.DrawRect(new Rect2(tl.Position, new Vector2(tl.Size.X * t / dur, tl.Size.Y)), new Color(0.35f, 0.85f, 1f));
        foreach (var (v, tint) in r.Smokes)
        {
            float x = tl.Position.X + tl.Size.X * Mathf.Clamp((v.StartTime - r.Start) / dur, 0, 1);
            view.DrawRect(new Rect2(x - 1.5f * k, tl.Position.Y - 6 * k, 3 * k, tl.Size.Y + 12 * k), tint);
        }
        foreach (var d in r.Defs.Where(d => d.KilledAt >= 0))
        {
            float x = tl.Position.X + tl.Size.X * Mathf.Clamp(d.KilledAt / dur, 0, 1);
            view.DrawRect(new Rect2(x - 1 * k, tl.Position.Y - 4 * k, 2 * k, tl.Size.Y + 8 * k), Colors.White);
        }
        if (r.EntryAt >= 0)
        {
            float x = tl.Position.X + tl.Size.X * Mathf.Clamp(r.EntryAt / dur, 0, 1);
            Gfx.Diamond(view, new Vector2(x, tl.GetCenter().Y), 6 * k, 6 * k, UiTheme.Warn);
        }
        int ls = UiTheme.Fs(12, k);
        Gfx.Text(view, UiTheme.HudWide, $"{t:0.0} S", tl.Position.X, tl.End.Y + 20 * k, ls, UiTheme.Dim);
        Gfx.TextR(view, UiTheme.HudWide, "SMOKE  |  KILL  |  ◆ ENTRY", tl.End.X, tl.End.Y + 20 * k, ls, UiTheme.Dim);

        // info column
        Gfx.Plate(view, info, 18 * k, new Color(0.04f, 0.07f, 0.1f, 0.95f), new Color(UiTheme.Text, 0.12f));
        var inner = info.Grow(-26 * k);
        int hs = UiTheme.Fs(13, k), ts = UiTheme.Fs(40, k), ss = UiTheme.Fs(15, k);
        var tag = new Rect2(inner.Position, new Vector2(Gfx.TextW(UiTheme.HudWide, "SMOKE REPLAY", hs) + 18 * k, 22 * k));
        view.DrawRect(tag, UiTheme.Accent);
        Gfx.Text(view, UiTheme.HudWide, "SMOKE REPLAY", tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, UiTheme.Text);
        string res = r.Won ? $"SITE TAKEN · {r.ExecTime:0.0} S" : r.Result;
        Gfx.TextFit(view, UiTheme.Display, $"ROUND {r.Index} — {res}", inner.Position.X, inner.Position.Y + 66 * k, ts, UiTheme.Text, inner.Size.X);
        Gfx.TextFit(view, UiTheme.HudWide, $"{m.Kit.Agent.ToUpperInvariant()} · {m.Spot.Map.ToUpperInvariant()} {m.Spot.Name.ToUpperInvariant()}", inner.Position.X, inner.Position.Y + 92 * k, ss, UiTheme.Dim, inner.Size.X);
        float y = ControllerDraw.RoundStats(view, k, m, r, inner.Position.X, inner.Position.Y + 136 * k, inner.Size.X);
        y += 6 * k;
        int bs = UiTheme.Fs(14, k);
        y = Legend(view, k, inner.Position.X, y, bs);

        // buttons
        float bw = (inner.Size.X - 24 * k) / 3, bh = 48 * k, by = inner.End.Y - bh;
        prevBtn = new Rect2(inner.Position.X, by, bw, bh);
        nextBtn = new Rect2(inner.Position.X + bw + 12 * k, by, bw, bh);
        closeBtn = new Rect2(inner.Position.X + 2 * (bw + 12 * k), by, bw, bh);
        Button(view, prevBtn, "◀  PREV", round > 0, k);
        Button(view, nextBtn, "NEXT  ▶", round + 1 < m.Rounds.Count, k);
        Button(view, closeBtn, "CLOSE (ESC)", true, k, accent: true);
    }

    static float Legend(CanvasItem ci, float k, float x, float y, int fs)
    {
        void Item(Action<Vector2> icon, string text)
        {
            icon(new Vector2(x + 14 * k, y - fs * 0.35f));
            Gfx.Text(ci, UiTheme.Body, text, x + 36 * k, y, fs, UiTheme.Dim);
            y += fs * 1.6f;
        }
        Item(c => TacticalMap.DashedCircle(ci, c, 9 * k, Colors.White, 1.5f * k, 10), "standard smoke spot");
        Item(c => { ci.DrawCircle(c, 9 * k, new Color(0.8f, 0.8f, 0.8f, 0.35f)); ci.DrawArc(c, 9 * k, 0, Mathf.Tau, 20, Colors.White, 1.5f * k, true); }, "your smoke");
        Item(c => ci.DrawLine(c - new Vector2(12 * k, 0), c + new Vector2(12 * k, 0), new Color(1f, 0.32f, 0.36f), 2 * k), "open sightline  ·  green = cut by your smoke");
        Item(c => TacticalMap.Arrow(ci, c, Vector2.Up, 8 * k, new Color(0.35f, 0.85f, 1f)), "you (path)");
        return y;
    }

    static void Button(CanvasItem ci, Rect2 r, string label, bool on, float k, bool accent = false)
    {
        var fill = accent ? new Color(UiTheme.Accent, on ? 0.9f : 0.3f) : new Color(1, 1, 1, on ? 0.1f : 0.04f);
        ci.DrawRect(r, fill);
        ci.DrawRect(r, new Color(1, 1, 1, on ? 0.35f : 0.12f), false, 1f);
        int fs = UiTheme.Fs(16, k);
        Gfx.TextC(ci, UiTheme.HudWide, label, r.GetCenter().X, Gfx.Mid(r.GetCenter().Y, fs), fs, on ? UiTheme.Text : UiTheme.Faint);
    }
}
