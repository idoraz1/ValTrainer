using System.Globalization;
using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.UI;

/// <summary>
/// Rewind: replays one run from its telemetry. Main panel = the player's view (crosshair fixed at the centre, the target
/// where it was relative to it, a fading trail of its path, bullets as hit / miss dots); timeline across the bottom with
/// every engagement and the run's moments (scrub, play / pause, speed, previous / next moment); side panel with this
/// engagement's numbers and three run charts (shots, time to hit, flick directions). Opened from the results screen, the
/// coach review and the profile's "Show me"; when opened on a moment code it cycles and loops only those moments.
/// Works while the results screen is frozen (process mode Always) and takes all input until closed.
/// Drawing lives in RewindDraw.cs.
/// </summary>
public partial class RewindScreen : CanvasLayer
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly float[] Speeds = { 0.25f, 0.5f, 1f };

    readonly RunTelemetry run;
    RunInsights? ins;
    Task<RunInsights>? insTask;
    /// <summary>Moment code this rewind is focused on (null = the whole run).</summary>
    readonly string? focusCode;

    // ---- options (set before the node enters the tree) ----
    /// <summary>Initial playhead (s); &lt; 0 = the start, or the first focused moment.</summary>
    public float StartAt = -1f;
    public bool StartPaused;
    public int StartZoom = 1;
    /// <summary>Initial side tab (0 shots, 1 time to hit, 2 flick directions); -1 = the first with data.</summary>
    public int StartTab = -1;
    public Action? Closed;

    // ---- playback ----
    float t, speed = 1f, dur = 1f, hold;
    float? restartAt;
    bool playing = true, loop, dragging;
    int zoom = 1, tab = -1;
    /// <summary>Moments prev / next cycle through (all, or the focused code's), sorted by start.</summary>
    List<Moment> moments = new();
    /// <summary>Focused mode: the moment being looped. Otherwise: the moment the playhead is in (-1 = none).</summary>
    int mi = -1;

    // ---- view ----
    Control view = null!, eye = null!;
    Vector2 mouse, laidOut;
    float k = 1f;
    Rect2 eyeRect, side, tlArea, track;
    readonly List<(Rect2 R, Action Act)> buttons = new();

    public RewindScreen(RunTelemetry run, RunInsights? insights = null, string? focusCode = null)
    {
        this.run = run;
        ins = insights;
        this.focusCode = string.IsNullOrEmpty(focusCode) ? null : focusCode;
    }

    bool Focused => focusCode != null && moments.Count > 0;

    public override void _Ready()
    {
        Layer = 8;
        ProcessMode = ProcessModeEnum.Always;
        view = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        view.Draw += Paint;
        AddChild(view);
        eye = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        eye.Draw += PaintEye;
        view.AddChild(eye);
        Setup3D();

        float last = run.Frames.Count > 0 ? run.Frames[^1].T : 0f;
        dur = Mathf.Max(0.1f, Mathf.Max(run.Duration, last));
        zoom = StartZoom >= 4 ? 4 : 1;
        if (StartAt >= 0) t = Mathf.Clamp(StartAt, 0, dur);
        else if (run.Frames.Count > 0) t = Mathf.Max(0, run.Frames[0].T);
        if (StartPaused) playing = false;
        if (ins == null) insTask = RewindData.Insights(run);
        else SetupMoments();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        UiTheme.ClickSound();
    }

    void SetupMoments()
    {
        if (ins == null) return;
        moments = ins.Moments.Where(m => focusCode == null || m.Code == focusCode).OrderBy(m => m.T0).ToList();
        if (tab < 0) tab = StartTab >= 0 && HasTab(StartTab) ? StartTab : FirstTab();
        if (!Focused) return;
        // Focused: start on the moment at / after the requested time (else the first), loop it at half speed.
        int i = 0;
        if (StartAt >= 0)
        {
            i = moments.FindIndex(m => m.T1 >= StartAt);
            if (i < 0) i = moments.Count - 1;
        }
        speed = 0.5f;
        GoMoment(i, seek: StartAt < 0 || StartAt < moments[i].T0 || StartAt > moments[i].T1);
        if (StartPaused) playing = false;
    }

    // =====================================================================================
    // update / input
    // =====================================================================================

    public override void _Process(double delta)
    {
        if (ins == null && insTask is { IsCompleted: true } task)
        {
            ins = task.IsCompletedSuccessfully ? task.Result : new RunInsights { Run = run };
            SetupMoments();
        }
        var size = view.GetViewportRect().Size;
        if (size != laidOut) Layout(size);

        float dt = (float)Mathf.Min(delta, 0.1);
        // while the 3D replay loads the playhead waits (the controls look disabled), unless the user switched to 2D
        if (playing && !dragging && !Loading3D)
        {
            if (hold > 0)
            {
                hold -= dt;
                if (hold <= 0 && restartAt is { } r) { t = r; restartAt = null; }
            }
            else
            {
                t += dt * speed;
                if (loop && mi >= 0 && mi < moments.Count && t >= moments[mi].T1)
                {
                    // hold the end of the moment for a beat, then replay it
                    t = moments[mi].T1;
                    hold = 0.6f;
                    restartAt = moments[mi].T0;
                }
                else if (t >= dur) { t = dur; playing = false; }
            }
        }
        if (!Focused) mi = MomentAt(t);
        Process3D(dt);
        view.QueueRedraw();
        if (eye.Visible) eye.QueueRedraw();
        over.QueueRedraw();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey key)
        {
            if (key.Keycode == Key.F11) return; // window mode toggle (Main) still works
            GetViewport().SetInputAsHandled();
            if (!key.Pressed || key.Echo) return;
            switch (key.Keycode)
            {
                case Key.Escape: Close(); break;
                case Key.Space: if (!Loading3D) TogglePlay(); break;
                case Key.Key1 or Key.Kp1: SetSpeed(0.25f); break;
                case Key.Key2 or Key.Kp2: SetSpeed(0.5f); break;
                case Key.Key3 or Key.Kp3: SetSpeed(1f); break;
                case Key.Left or Key.Q: if (!Loading3D) Step(-1); break;
                case Key.Right or Key.E: if (!Loading3D) Step(1); break;
                case Key.Z: ToggleZoom(); break;
                case Key.V: Toggle3D(); break;
                case Key.Tab: NextTab(); break;
                case Key.Home: Seek(0); break;
            }
        }
        else if (e is InputEventMouseButton mb)
        {
            GetViewport().SetInputAsHandled();
            mouse = mb.Position;
            if (mb.ButtonIndex != MouseButton.Left) return;
            if (!mb.Pressed) { dragging = false; return; }
            foreach (var (r, act) in buttons)
                if (r.HasPoint(mb.Position)) { UiTheme.ClickSound(); act(); return; }
            if (TrackHit(mb.Position)) { dragging = true; SeekX(mb.Position.X); }
        }
        else if (e is InputEventMouseMotion mm)
        {
            GetViewport().SetInputAsHandled();
            mouse = mm.Position;
            if (dragging) SeekX(mm.Position.X);
        }
        else if (e is not InputEventJoypadMotion) GetViewport().SetInputAsHandled();
    }

    bool TrackHit(Vector2 p) => new Rect2(track.Position.X - 8 * k, track.Position.Y - 22 * k, track.Size.X + 16 * k, track.Size.Y + 30 * k).HasPoint(p);

    void SeekX(float x) => Seek((x - track.Position.X) / Mathf.Max(1f, track.Size.X) * dur);

    void Seek(float time)
    {
        t = Mathf.Clamp(time, 0, dur);
        hold = 0; restartAt = null;
        // Scrubbing out of the looped moment stops the loop until prev / next picks a moment again.
        if (loop && mi >= 0 && mi < moments.Count && (t < moments[mi].T0 - 0.05f || t > moments[mi].T1 + 0.05f)) loop = false;
    }

    void TogglePlay()
    {
        if (!playing && t >= dur - 1e-3f && !loop) t = 0; // replay from the start
        playing = !playing;
        hold = 0;
        if (restartAt is { } r) { t = r; restartAt = null; }
    }

    void SetSpeed(float s) => speed = s;
    void ToggleZoom() => zoom = zoom == 1 ? 4 : 1;

    void Step(int dir)
    {
        if (moments.Count == 0) return;
        if (focusCode != null)
        {
            // focused: cycle through this code's moments only
            int n = moments.Count;
            GoMoment(((mi < 0 ? (dir > 0 ? -1 : 0) : mi) + dir + n) % n, seek: true);
            return;
        }
        int i;
        if (dir > 0) i = moments.FindIndex(m => m.T0 > t + 0.05f);
        else
        {
            // inside a moment, "previous" first goes back to its start
            i = moments.FindLastIndex(m => m.T0 < t - 0.3f);
        }
        if (i >= 0) GoMoment(i, seek: true);
    }

    void GoMoment(int i, bool seek)
    {
        if (i < 0 || i >= moments.Count) return;
        mi = i;
        loop = focusCode != null;
        if (seek) t = Mathf.Clamp(moments[i].T0, 0, dur);
        hold = 0; restartAt = null;
        playing = true;
    }

    /// <summary>Index of the latest-starting moment containing <paramref name="time"/>, else -1.</summary>
    int MomentAt(float time)
    {
        int best = -1;
        for (int i = 0; i < moments.Count; i++)
        {
            if (moments[i].T0 > time) break;
            if (time <= moments[i].T1) best = i;
        }
        return best;
    }

    void Close()
    {
        UiTheme.ClickSound();
        Closed?.Invoke();
        QueueFree();
    }

    // ---- tabs: 0 shots, 1 time to hit, 2 flick directions (only those with data) ----

    static readonly string[] TabNames = { "SHOTS", "TIME TO HIT", "DIRECTIONS" };

    bool HasTab(int i) => ins != null && i switch
    {
        0 => ins.Scatter.Count > 0 || ins.Shots > 0,
        1 => ins.Ttk.N > 0,
        2 => ins.SectorN.Any(n => n > 0),
        _ => false,
    };

    int FirstTab()
    {
        for (int i = 0; i < 3; i++) if (HasTab(i)) return i;
        return -1;
    }

    void NextTab()
    {
        for (int d = 1; d <= 3; d++)
        {
            int i = ((tab < 0 ? 0 : tab) + d) % 3;
            if (HasTab(i)) { tab = i; return; }
        }
    }

    // =====================================================================================
    // layout
    // =====================================================================================

    void Layout(Vector2 size)
    {
        laidOut = size;
        k = Mathf.Min(size.Y / 1080f, size.X / 1920f);
        float m = 36 * k, left = m, right = size.X - m;
        float contentTop = 124 * k;
        float tlH = 44 * k + 30 * k + 40 * k + 56 * k;
        tlArea = new Rect2(left, size.Y - 22 * k - tlH, right - left, tlH);
        float contentBottom = tlArea.Position.Y - 20 * k;
        float sideMin = Mathf.Max(540 * k, 300f);
        var avail = new Rect2(left, contentTop, Mathf.Max(100, right - left - sideMin - 24 * k), Mathf.Max(60, contentBottom - contentTop));
        float aspect = size.X / Mathf.Max(1f, size.Y);
        float w = avail.Size.X, h = w / aspect;
        if (h > avail.Size.Y) { h = avail.Size.Y; w = h * aspect; }
        eyeRect = new Rect2(left, contentTop, w, h);
        float sx = eyeRect.End.X + 24 * k;
        side = new Rect2(sx, contentTop, right - sx, avail.Size.Y);
        foreach (var c in new Control?[] { eye, over, svc })
            if (c != null) { c.Position = eyeRect.Position; c.Size = eyeRect.Size; }
        track = new Rect2(tlArea.Position.X, tlArea.Position.Y + 44 * k + 30 * k, tlArea.Size.X, 40 * k);
    }

    // =====================================================================================
    // painting: frame, header, timeline (the player's view and side panel are in RewindDraw.cs)
    // =====================================================================================

    void Paint()
    {
        buttons.Clear();
        var size = view.GetViewportRect().Size;
        view.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.035f, 0.05f, 1f));
        var line = new Color(1, 1, 1, 0.015f);
        for (float x = -size.Y; x < size.X; x += 46 * k) view.DrawLine(new Vector2(x, size.Y), new Vector2(x + size.Y * 0.58f, 0), line, 1f);

        PaintHeader(size);
        // the player's view frame (contents drawn by the clipped child)
        view.DrawRect(eyeRect.Grow(1), new Color(UiTheme.Text, 0.16f), false, 1f);
        Gfx.Brackets(view, eyeRect.Grow(6 * k), 18 * k, new Color(UiTheme.Text, 0.5f), Mathf.Max(1, 2 * k));
        PaintSide();
        PaintTimeline();
    }

    void PaintHeader(Vector2 size)
    {
        float left = 36 * k, right = size.X - 36 * k, top = 26 * k;
        int hs = UiTheme.Fs(13, k), ts = UiTheme.Fs(40, k), ss = UiTheme.Fs(13, k);
        float tx = left;
        var tag = new Rect2(tx, top, Gfx.TextW(UiTheme.HudWide, "REWIND", hs) + 18 * k, 22 * k);
        view.DrawRect(tag, UiTheme.Accent);
        Gfx.Text(view, UiTheme.HudWide, "REWIND", tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, UiTheme.Text);
        tx = tag.End.X + 10 * k;
        if (focusCode != null)
        {
            string f = (Focused ? "ONLY: " : "NONE IN THIS RUN: ") + RewindData.CodeName(focusCode).ToUpperInvariant();
            float maxW = Mathf.Max(60 * k, right - 240 * k - tx);
            var fr = new Rect2(tx, top, Mathf.Min(maxW, Gfx.TextW(UiTheme.HudWide, f, hs) + 18 * k), 22 * k);
            view.DrawRect(fr, new Color(UiTheme.Warn, 0.14f));
            view.DrawRect(fr, UiTheme.Warn, false, 1f);
            Gfx.TextFit(view, UiTheme.HudWide, f, fr.Position.X + 9 * k, Gfx.Mid(fr.GetCenter().Y, hs), hs, UiTheme.Warn, fr.Size.X - 16 * k);
        }

        // close
        var close = new Rect2(right - 210 * k, top + 6 * k, 210 * k, 50 * k);
        Button(close, "CLOSE  (ESC)", Close, accent: true, fs: 15);

        string title = RewindData.ModeName(run.Mode).ToUpperInvariant();
        float titleBase = top + 78 * k;
        float tw = Mathf.Min(Gfx.TextW(UiTheme.Display, title, ts), (right - left) * 0.4f);
        Gfx.TextFit(view, UiTheme.Display, title, left, titleBase, ts, UiTheme.Text, tw + 1);
        string sub = string.Create(Inv, $"{dur:0.0} S RUN · {When(run.When)}");
        Gfx.Text(view, UiTheme.HudWide, sub, left + tw + 16 * k, titleBase, ss, UiTheme.Dim);
        float subEnd = left + tw + 16 * k + Gfx.TextW(UiTheme.HudWide, sub, ss);

        // moment label (right, before the close button)
        string label;
        Color lc;
        if (ins == null) { label = "Analysing the run…"; lc = UiTheme.Dim; }
        else if (mi >= 0 && mi < moments.Count)
        {
            var m = moments[mi];
            label = $"Moment {mi + 1} of {moments.Count}: {m.Label}";
            lc = RewindData.CodeColor(m.Code);
        }
        else if (focusCode != null && moments.Count == 0) { label = "No example of this in this run"; lc = UiTheme.Dim; }
        else if (moments.Count > 0) { label = $"{moments.Count} moment{(moments.Count == 1 ? "" : "s")} · ← → to jump"; lc = UiTheme.Dim; }
        else { label = "No moments found in this run"; lc = UiTheme.Faint; }
        int ls = UiTheme.Fs(30, k);
        float lx = subEnd + 30 * k, lw = close.Position.X - 24 * k - lx;
        if (lw > 80 * k) Gfx.TextFit(view, UiTheme.Display, label, lx, titleBase, ls, lc, lw, HorizontalAlignment.Right);
    }

    static string When(DateTime w)
    {
        if (w.Date == DateTime.Today) return "TODAY " + w.ToString("HH:mm", Inv);
        return w.ToString("MMM d HH:mm", Inv).ToUpperInvariant();
    }

    float X(float time) => track.Position.X + track.Size.X * Mathf.Clamp(time / dur, 0, 1);

    void PaintTimeline()
    {
        var a = tlArea;
        float bh = 44 * k, y = a.Position.Y;
        float x = a.Position.X;
        int fs = UiTheme.Fs(14, k), cs = UiTheme.Fs(11, k);

        // transport: prev · play / pause · next
        // (disabled while the 3D replay loads: the playhead waits for it)
        bool waiting = Loading3D;
        bool canStep = moments.Count > 0 && !waiting;
        Button(new Rect2(x, y, 58 * k, bh), "", () => Step(-1), on: canStep, icon: (r, c) => StepIcon(r, c, -1));
        x += 64 * k;
        Button(new Rect2(x, y, 76 * k, bh), "", TogglePlay, on: !waiting, selected: playing && !waiting,
            icon: (r, c) => { if (playing && !waiting) PauseIcon(r, c); else PlayIcon(r, c); });
        x += 82 * k;
        Button(new Rect2(x, y, 58 * k, bh), "", () => Step(1), on: canStep, icon: (r, c) => StepIcon(r, c, 1));
        x += 58 * k + 26 * k;

        Gfx.Text(view, UiTheme.HudWide, "SPEED", x, Gfx.Mid(y + bh / 2, cs), cs, UiTheme.Faint);
        x += Gfx.TextW(UiTheme.HudWide, "SPEED", cs) + 10 * k;
        foreach (var sp in Speeds)
        {
            float s = sp;
            string l = s == 1f ? "1×" : s == 0.5f ? "0.5×" : "0.25×";
            float w = Mathf.Max(62 * k, Gfx.TextW(UiTheme.HudWide, l, fs) + 20 * k);
            Button(new Rect2(x, y, w, bh), l, () => SetSpeed(s), selected: Mathf.IsEqualApprox(speed, s), fs: 14);
            x += w + 6 * k;
        }
        x += 20 * k;
        Gfx.Text(view, UiTheme.HudWide, "ZOOM", x, Gfx.Mid(y + bh / 2, cs), cs, UiTheme.Faint);
        x += Gfx.TextW(UiTheme.HudWide, "ZOOM", cs) + 10 * k;
        foreach (int z in new[] { 1, 4 })
        {
            int zz = z;
            string l = z == 1 ? "1×" : "4×";
            float w = Mathf.Max(56 * k, Gfx.TextW(UiTheme.HudWide, l, fs) + 20 * k);
            Button(new Rect2(x, y, w, bh), l, () => zoom = zz, selected: zoom == z, fs: 14);
            x += w + 6 * k;
        }
        x += 20 * k;
        PaintViewToggle(x, y, bh, fs, cs);

        // time readout (right)
        int ts = UiTheme.Fs(28, k);
        string time = string.Create(Inv, $"{t:0.00} / {dur:0.00} S");
        Gfx.TextR(view, UiTheme.Display, time, a.End.X, Gfx.Mid(y + bh / 2, ts), ts, UiTheme.Text);

        // track
        var tr = track;
        view.DrawRect(tr, new Color(1, 1, 1, 0.05f));
        // focused / looped moment range
        if (mi >= 0 && mi < moments.Count)
        {
            var m = moments[mi];
            float x0 = X(m.T0), x1 = Mathf.Max(X(m.T1), x0 + 2 * k);
            var col = RewindData.CodeColor(m.Code);
            view.DrawRect(new Rect2(x0, tr.Position.Y, x1 - x0, tr.Size.Y), new Color(col, loop ? 0.22f : 0.12f));
            view.DrawRect(new Rect2(x0, tr.Position.Y, x1 - x0, tr.Size.Y), new Color(col, 0.6f), false, 1f);
        }
        // engagements: a span per target / bot (green when it ended in a hit) with a tick at its start
        if (ins != null)
            foreach (var tri in ins.Trials)
            {
                float x0 = X(tri.T0), x1 = Mathf.Max(X(tri.TEnd), x0 + 1);
                view.DrawRect(new Rect2(x0, tr.Position.Y + 4 * k, x1 - x0, tr.Size.Y * 0.38f), tri.Hit ? new Color(UiTheme.Good, 0.28f) : new Color(UiTheme.Text, 0.14f));
                view.DrawRect(new Rect2(x0, tr.Position.Y + 2 * k, Mathf.Max(1, 1.5f * k), tr.Size.Y * 0.46f), new Color(UiTheme.Text, 0.55f));
            }
        // shots: small ticks along the bottom
        float sy = tr.Position.Y + tr.Size.Y * 0.62f, sh = tr.Size.Y * 0.3f;
        foreach (var s in run.Shots)
            view.DrawRect(new Rect2(X(s.T) - 0.5f, sy, Mathf.Max(1, 1.2f * k), sh), s.Hit ? new Color(RewindData.HitColor, 0.8f) : new Color(RewindData.MissColor, 0.8f));
        view.DrawRect(tr, new Color(UiTheme.Text, 0.18f), false, 1f);

        // moment markers above the track (kills and deaths from the events if the insights have none)
        float my = tr.Position.Y - 5 * k;
        if (ins != null)
        {
            bool kills = ins.Moments.Any(m => m.Code == "KILL"), deaths = ins.Moments.Any(m => m.Code == "DEATH");
            foreach (var ev in run.Events)
                if ((ev.Kind == "kill" && !kills) || (ev.Kind == "died" && !deaths))
                    Marker(X(ev.T), my, 5.5f * k, ev.Kind == "kill" ? UiTheme.Good : UiTheme.Accent);
            foreach (var m in ins.Moments)
            {
                bool mine = focusCode == null || m.Code == focusCode;
                var c = RewindData.CodeColor(m.Code);
                Marker(X(m.T0), my, (mine ? 6f : 4f) * k, mine ? c : new Color(c, 0.3f));
            }
            if (mi >= 0 && mi < moments.Count)
            {
                float cx = X(moments[mi].T0);
                Marker(cx, my - 3 * k, 9f * k, RewindData.CodeColor(moments[mi].Code));
                Gfx.DiamondLine(view, new Vector2(cx, my - 9 * k), 6 * k, 6 * k, UiTheme.Text, Mathf.Max(1, 1.2f * k));
            }
        }

        // playhead
        float px = X(t);
        view.DrawRect(new Rect2(px - 1 * k, tr.Position.Y - 12 * k, Mathf.Max(2, 2 * k), tr.Size.Y + 16 * k), UiTheme.Text);
        view.DrawCircle(new Vector2(px, tr.Position.Y - 14 * k), 5 * k, UiTheme.Text);

        // hover time
        if (TrackHit(mouse) && !dragging)
        {
            float ht = Mathf.Clamp((mouse.X - tr.Position.X) / tr.Size.X * dur, 0, dur);
            view.DrawRect(new Rect2(mouse.X, tr.Position.Y, 1, tr.Size.Y), new Color(UiTheme.Text, 0.35f));
            Gfx.TextC(view, UiTheme.Hud, string.Create(Inv, $"{ht:0.0} s"), mouse.X, tr.End.Y + 18 * k, UiTheme.Fs(12, k), UiTheme.Text);
        }

        // axis labels under the track; key hints (left) and the legend (right) below them
        int ls = UiTheme.Fs(12, k);
        float step = AxisStep(dur, tr.Size.X / (70 * k));
        if (!(TrackHit(mouse) && !dragging))
            for (float s = 0; s <= dur + 1e-3f; s += step)
            {
                float lx = X(s);
                view.DrawRect(new Rect2(lx, tr.End.Y, 1, 5 * k), new Color(UiTheme.Text, 0.3f));
                if (lx > tr.End.X - 14 * k) break;
                Gfx.TextC(view, UiTheme.Hud, s.ToString("0", Inv) + "s", lx, tr.End.Y + 20 * k, ls, UiTheme.Faint);
            }
        float ly = tr.End.Y + 48 * k;
        var legend = new (string Text, Color C, int Kind)[]
        {
            ("coach moment", UiTheme.Warn, 0), ("kill", UiTheme.Good, 0), ("death", UiTheme.Accent, 0), ("miss", RewindData.CodeColor("MISS"), 0),
            ("hit / miss shot", RewindData.HitColor, 1), ("target / bot", UiTheme.Text, 2),
        };
        float legendW = 0;
        foreach (var (text, _, _) in legend) legendW += 18 * k + Gfx.TextW(UiTheme.Body, text, ls) + 16 * k;
        float lx0 = legendW <= tr.Size.X ? tr.End.X - legendW + 16 * k : tr.End.X;
        int hs = UiTheme.Fs(11, k);
        const string hints = "SPACE PLAY · 1 2 3 SPEED · ← → MOMENT · Z ZOOM · V 2D/3D · TAB CHARTS · ESC CLOSE";
        const string shortHints = "SPACE · 1 2 3 · ← → · Z · V · TAB · ESC";
        float room = lx0 - tr.Position.X - 24 * k;
        string hint = Gfx.TextW(UiTheme.HudWide, hints, hs) <= room ? hints : shortHints;
        if (Gfx.TextW(UiTheme.HudWide, hint, hs) <= room) Gfx.Text(view, UiTheme.HudWide, hint, tr.Position.X, ly, hs, UiTheme.Faint);
        if (legendW <= tr.Size.X)
        {
            float lx = lx0;
            foreach (var (text, c, kind) in legend)
            {
                var ic = new Vector2(lx + 6 * k, ly - ls * 0.35f);
                if (kind == 0) Marker(ic.X, ic.Y + 4 * k, 5f * k, c);
                else if (kind == 1)
                {
                    view.DrawRect(new Rect2(ic.X - 3 * k, ic.Y - 6 * k, 2 * k, 12 * k), RewindData.HitColor);
                    view.DrawRect(new Rect2(ic.X + 2 * k, ic.Y - 6 * k, 2 * k, 12 * k), RewindData.MissColor);
                }
                else view.DrawRect(new Rect2(ic.X - 6 * k, ic.Y - 4 * k, 12 * k, 8 * k), new Color(UiTheme.Text, 0.3f));
                Gfx.Text(view, UiTheme.Body, text, lx + 18 * k, ly, ls, UiTheme.Dim);
                lx += 18 * k + Gfx.TextW(UiTheme.Body, text, ls) + 16 * k;
            }
        }
    }

    static float AxisStep(float dur, float maxLabels)
    {
        foreach (var s in new[] { 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f })
            if (dur / s <= Mathf.Max(2, maxLabels)) return s;
        return 600f;
    }

    /// <summary>Downward triangle with its tip at (x, y).</summary>
    void Marker(float x, float y, float r, Color c)
    {
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = new(x, y); p[1] = new(x - r, y - r * 1.5f); p[2] = new(x + r, y - r * 1.5f);
        view.FillPoly(p, c);
    }

    // ---- buttons (immediate mode: registered while painting, hit-tested in _Input) ----

    void Button(Rect2 r, string label, Action act, bool on = true, bool selected = false, bool accent = false, int fs = 15,
        Action<Rect2, Color>? icon = null, CanvasItem? ci = null)
    {
        ci ??= view;
        bool hov = on && r.HasPoint(mouse);
        Color fill = accent ? new Color(hov ? UiTheme.AccentHover : UiTheme.Accent, on ? 0.92f : 0.3f)
            : selected ? new Color(UiTheme.Text, hov ? 0.24f : 0.18f)
            : new Color(1, 1, 1, !on ? 0.03f : hov ? 0.13f : 0.06f);
        ci.DrawRect(r, fill);
        ci.DrawRect(r, selected ? new Color(UiTheme.Text, 0.85f) : new Color(1, 1, 1, !on ? 0.1f : hov ? 0.5f : 0.22f), false, 1f);
        var tc = on ? UiTheme.Text : UiTheme.Faint;
        if (icon != null) icon(r, tc);
        if (label.Length > 0)
        {
            int f = UiTheme.Fs(fs, k);
            Gfx.TextC(ci, UiTheme.HudWide, label, r.GetCenter().X, Gfx.Mid(r.GetCenter().Y, f), f, tc);
        }
        if (on) buttons.Add((r, act));
    }

    void PlayIcon(Rect2 r, Color c)
    {
        var o = r.GetCenter();
        float s = Mathf.Min(r.Size.X, r.Size.Y) * 0.22f;
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = o + new Vector2(-s * 0.8f, -s); p[1] = o + new Vector2(s * 1.1f, 0); p[2] = o + new Vector2(-s * 0.8f, s);
        view.FillPoly(p, c);
    }

    void PauseIcon(Rect2 r, Color c)
    {
        var o = r.GetCenter();
        float s = Mathf.Min(r.Size.X, r.Size.Y) * 0.22f;
        view.DrawRect(new Rect2(o.X - s * 0.9f, o.Y - s, s * 0.6f, s * 2), c);
        view.DrawRect(new Rect2(o.X + s * 0.3f, o.Y - s, s * 0.6f, s * 2), c);
    }

    void StepIcon(Rect2 r, Color c, int dir)
    {
        var o = r.GetCenter();
        float s = Mathf.Min(r.Size.X, r.Size.Y) * 0.2f;
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = o + new Vector2(-dir * s * 0.7f, -s); p[1] = o + new Vector2(dir * s * 0.7f, 0); p[2] = o + new Vector2(-dir * s * 0.7f, s);
        view.FillPoly(p, c);
        view.DrawRect(new Rect2(o.X + dir * s * 0.7f - (dir > 0 ? 0 : s * 0.35f), o.Y - s, s * 0.35f, s * 2), c);
    }

    // =====================================================================================
    // opening
    // =====================================================================================

    /// <summary>Opens a rewind as a child of <paramref name="parent"/> (on a results screen: the session's world, so a
    /// restart frees it).</summary>
    public static RewindScreen Open(Node parent, RunTelemetry run, RunInsights? insights = null, string? code = null)
    {
        var r = new RewindScreen(run, insights ?? RewindData.Ready(run), code) { Name = "Rewind" };
        ApplyDevArgs(r);
        parent.AddChild(r);
        return r;
    }

    /// <summary>Dev-only screenshot switches: --rewind-at S (paused at S seconds), --rewind-zoom 4, --rewind-tab 0|1|2,
    /// --rewind-play (keep playing), --rewind-2d (start in the 2D view).</summary>
    static void ApplyDevArgs(RewindScreen r)
    {
        if (!(Main.I?.Dev ?? false)) return;
        if (float.TryParse(CmdLine.After("--rewind-at"), NumberStyles.Float, Inv, out var at)) { r.StartAt = at; r.StartPaused = !CmdLine.Has("--rewind-play"); }
        if (int.TryParse(CmdLine.After("--rewind-zoom"), out var z)) r.StartZoom = z;
        if (int.TryParse(CmdLine.After("--rewind-tab"), out var tb)) r.StartTab = tb;
        if (CmdLine.Has("--rewind-2d")) r.Start2D = true;
    }

    /// <summary>Dev "--rewind-file path.vtt [--rewind-code CODE]": opens a rewind of a saved run at startup.</summary>
    public static void OpenDevFile(string path)
    {
        var run = RunTelemetry.Load(path);
        if (run == null) { Log.Error($"[rewind] can't read {path}"); return; }
        run.SourceFile ??= Path.GetFullPath(path); // finds the .vtr sidecar (the 3D replay), loaded by the Rewind on a worker
        Log.Info($"[rewind] {path}: {run.Frames.Count} frames, {run.Shots.Count} shots");
        Open(Main.I, run, null, CmdLine.After("--rewind-code"));
    }
}
