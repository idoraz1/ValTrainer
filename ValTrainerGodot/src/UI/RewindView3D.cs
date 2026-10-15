using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Replay;

namespace ValTrainer.UI;

/// <summary>
/// Rewind's 3D view: the run re-rendered by a <see cref="ReplayStage"/> (its own World3D inside a SubViewport) in the
/// player's-view panel, with the analysis drawn on top (crosshair trail, target ring, shots, moment caption) and the
/// player's crosshair / sniper scope / flash white-out. Default when the run has a 3D recording (.vtr) and the drill
/// replays in 3D; V or the VIEW buttons switch to the 2D diagram.
/// <para>Loading is always visible: "Loading run…" while the sidecar is read on a worker, then "Loading 3D replay…"
/// with the stage's progress and step until it's built (the dimmed 2D view behind, the playhead waiting and the
/// transport looking disabled), then a fade-in. A failed build or an old run falls back to 2D with a short note.</para>
/// </summary>
public partial class RewindScreen
{
    // ---- options ----
    /// <summary>Start in the 2D view even when 3D is available (dev --rewind-2d).</summary>
    public bool Start2D;

    // ---- state ----
    SubViewportContainer? svc;
    SubViewport? sv;
    ReplayStage? stage;
    /// <summary>Overlay over the eye panel: the 3D analysis layer, the loading card and notes.</summary>
    Control over = null!;
    ReplayRecording? rec;
    /// <summary>The .vtr sidecar being read on a worker (Profile / dev paths; the run just played has it in memory).</summary>
    Task<ReplayRecording?>? recTask;
    ReplaySupport support = ReplaySupport.None;
    /// <summary>The user wants the 3D view (it's loading or shown).</summary>
    bool view3D;
    /// <summary>Animated load bar (follows the stage's progress smoothly), 3D fade-in, time spent loading.</summary>
    float barShown, fade3D, loadClock, stallClock, lastProgress = -1f;
    /// <summary>Short note shown in the 2D view about why 3D isn't there.</summary>
    string? note;
    bool? prevDisable3D;
    float renderedT = float.NaN;
    ulong settleUntilMs;
    int renderedZoom;
    Vector2I renderedSize;

    const float FadeInSec = 0.4f, StallSec = 20f;

    bool Has3D => stage != null && !stage.Failed;
    /// <summary>The 3D view is wanted but not ready yet: the playhead waits.</summary>
    bool Loading3D => view3D && (recTask != null || stage is { Built: false, Failed: false });
    bool Show3D => view3D && stage is { Built: true, Failed: false } && stage.Camera != null;

    /// <summary>Test seam: lets a dev-only file supply the stage (no implementation in release builds).</summary>
    static partial void StageOverride(ReplayRecording rec, RunTelemetry run, int quality, ref ReplayStage? stage);

    void Setup3D()
    {
        // The frozen session behind the Rewind is fully covered: don't render its 3D world meanwhile (restored in _ExitTree).
        var root = GetTree().Root;
        prevDisable3D = root.Disable3D;
        root.Disable3D = true;

        svc = new SubViewportContainer { Name = "View3D", Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        view.AddChild(svc);
        view.MoveChild(svc, eye.GetIndex()); // under the 2D eye and the overlay
        over = new Control { Name = "Over", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        over.Draw += PaintOver;
        view.AddChild(over);

        try { support = ReplayFile.SupportFor(run.Mode); } catch { support = ReplaySupport.None; }
        if (support == ReplaySupport.None) return; // 2D-only drill (reaction, finders, 2D modes): nothing to say
        if (run.Replay != null) StartStage(run.Replay);
        else if (run.SourceFile is { } f && SidecarExists(f))
        {
            view3D = !Start2D;
            recTask = Task.Run(() => { try { return ReplayFile.For(run); } catch { return null; } });
        }
        else note = "3D replay is recorded for new runs only";
    }

    static bool SidecarExists(string vtt)
    {
        try { return File.Exists(ReplayFile.SidecarOf(vtt)); } catch { return false; }
    }

    void StartStage(ReplayRecording r)
    {
        rec = r;
        int q = Math.Min(Main.I?.Settings.Quality ?? 1, 2); // replay quality: no SDFGI / volumetric fog / SSR
        ReplayStage? s = null;
        try
        {
            StageOverride(r, run, q, ref s);
            s ??= new ReplayStage(r, run, q);
            sv = new SubViewport
            {
                Name = "Stage",
                OwnWorld3D = true,
                HandleInputLocally = false,
                GuiDisableInput = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            sv.AddChild(s);
            svc!.AddChild(sv);
            stage = s;
            view3D = !Start2D;
            laidOut = default; // size the new viewport
        }
        catch (Exception ex)
        {
            Log.Error($"[rewind] 3D stage: {ex.Message}");
            Fail3D();
        }
    }

    /// <summary>3D can't be shown: 2D with a short note, the stage freed.</summary>
    void Fail3D()
    {
        note = "3D replay unavailable for this run";
        view3D = false;
        stage = null;
        if (sv != null && IsInstanceValid(sv)) sv.QueueFree();
        sv = null;
        if (svc != null) svc.Visible = false;
    }

    void Toggle3D()
    {
        if (!view3D && !Has3D && recTask == null) return; // nothing to show in 3D
        view3D = !view3D;
        if (view3D) fade3D = 0f; // fade back in
        UiTheme.ClickSound();
    }

    void Set3D(bool on)
    {
        if (on != view3D) Toggle3D();
    }

    public override void _ExitTree()
    {
        if (prevDisable3D is { } d && GetTree()?.Root is { } root) root.Disable3D = d;
        prevDisable3D = null;
    }

    /// <summary>Per frame (after the playhead moved): sidecar / stage loading, fade, time and render mode of the 3D view.</summary>
    void Process3D(float dt)
    {
        if (recTask is { IsCompleted: true } rt)
        {
            recTask = null;
            var r = rt.IsCompletedSuccessfully ? rt.Result : null;
            if (r != null) { run.Replay = r; StartStage(r); }
            else Fail3D();
        }
        if (stage is { Failed: true }) Fail3D();
        else if (stage is { Built: false } st)
        {
            // never stuck without explanation: a build that stops progressing for a long time falls back to 2D
            if (!Mathf.IsEqualApprox(st.LoadProgress, lastProgress)) { lastProgress = st.LoadProgress; stallClock = 0; }
            else if ((stallClock += dt) > StallSec) { Log.Info("[rewind] 3D stage stalled; showing 2D"); Fail3D(); }
        }

        // load bar: eases toward the real progress, never backwards, with a slow creep so it never looks frozen
        float target = recTask != null ? 0.04f : stage == null ? 0f : stage.Built ? 1f : Mathf.Clamp(stage.LoadProgress, 0f, 1f);
        if (barShown < target) barShown = Mathf.Min(target, barShown + Mathf.Max((target - barShown) * (1f - Mathf.Exp(-8f * dt)), 0.04f * dt));
        if (stage is { Built: true }) barShown = 1f;
        if (Loading3D) loadClock += dt;

        bool show = Show3D;
        fade3D = show ? Mathf.MoveToward(fade3D, 1f, dt / FadeInSec) : 0f;
        if (show)
        {
            stage!.LensZoom = zoom;
            stage.SetTime(t, dragging);
        }
        if (svc != null)
        {
            svc.Visible = show;
            svc.Modulate = new Color(1, 1, 1, fade3D);
        }
        // the 2D diagram: the view itself in 2D, a dimmed backdrop while 3D loads, gone once 3D is fully in
        eye.Visible = !show || fade3D < 1f;
        eye.Modulate = new Color(1, 1, 1, !view3D ? 1f : show ? 0.4f * (1f - fade3D) : 0.4f);

        // render the stage only when something changes while paused (and not at all while hidden)
        if (sv != null && stage != null)
        {
            var size = sv.Size;
            bool changed = t != renderedT || zoom != renderedZoom || size != renderedSize;
            // after a change, keep rendering ~0.5 s so hit flashes, sparks and the viewmodel settle (then stop)
            ulong nowMs = Time.GetTicksMsec();
            if (changed) settleUntilMs = nowMs + 500;
            bool live = !stage.Built || fade3D < 1f || dragging || (playing && hold <= 0 && !Loading3D) || nowMs < settleUntilMs;
            sv.RenderTargetUpdateMode = !view3D ? SubViewport.UpdateMode.Disabled
                : live ? SubViewport.UpdateMode.Always
                : changed ? SubViewport.UpdateMode.Once
                : SubViewport.UpdateMode.Disabled;
            if (view3D) { renderedT = t; renderedZoom = zoom; renderedSize = size; }
            else renderedT = float.NaN;
        }
    }

    // =====================================================================================
    // the overlay: loading card, 3D analysis layer, notes
    // =====================================================================================

    void PaintOver()
    {
        var sz = over.Size;
        if (sz.X < 4 || sz.Y < 4) return;
        centre = sz / 2;
        var bounds = new Rect2(Vector2.Zero, sz);
        if (Show3D)
        {
            Paint3D(sz, bounds);
            if (fade3D < 1f) PaintLoading(sz, 1f - fade3D);
        }
        else if (Loading3D) PaintLoading(sz, 1f);
        else if (note != null) Note(sz, note);
    }

    /// <summary>The loading card: title, spinner, progress bar with the stage's step, and the V hint.</summary>
    void PaintLoading(Vector2 sz, float a)
    {
        over.DrawRect(new Rect2(Vector2.Zero, sz), new Color(0.02f, 0.035f, 0.05f, 0.35f * a));
        bool readingFile = recTask != null;
        float w = Mathf.Min(560 * k, sz.X - 40 * k), h = 168 * k;
        var card = new Rect2((sz.X - w) / 2, (sz.Y - h) / 2, w, h);
        Gfx.Plate(over, card, 14 * k, new Color(0.035f, 0.06f, 0.085f, 0.94f * a), new Color(UiTheme.Text, 0.16f * a));
        over.DrawRect(new Rect2(card.Position, new Vector2(48 * k, 3 * k)), new Color(UiTheme.Accent, a));
        float x = card.Position.X + 28 * k, right = card.End.X - 28 * k;

        // spinner + title
        int ts = UiTheme.Fs(26, k);
        float ty = card.Position.Y + 30 * k + ts * 0.5f;
        var sc = new Vector2(x + 11 * k, ty);
        over.DrawArc(sc, 11 * k, 0, Mathf.Tau, 32, new Color(UiTheme.Text, 0.12f * a), Mathf.Max(1.5f, 3 * k), true);
        float a0 = loadClock * 5.5f;
        over.DrawArc(sc, 11 * k, a0, a0 + 1.9f, 20, new Color(UiTheme.Accent, a), Mathf.Max(1.5f, 3 * k), true);
        string title = readingFile ? "Loading run…" : stage is { Built: true } ? "3D replay ready" : "Loading 3D replay…";
        Gfx.TextFit(over, UiTheme.Display, title, x + 34 * k, Gfx.Mid(ty, ts), ts, new Color(UiTheme.Text, a), right - x - 34 * k - 70 * k);
        int ps = UiTheme.Fs(18, k);
        Gfx.TextR(over, UiTheme.HudWide, $"{Mathf.RoundToInt(barShown * 100)}%", right, Gfx.Mid(ty, ps), ps, new Color(UiTheme.Text, 0.85f * a));

        // bar
        var bar = new Rect2(x, card.Position.Y + 72 * k, right - x, 10 * k);
        over.DrawRect(bar, new Color(1, 1, 1, 0.08f * a));
        var fill = new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(barShown, 0, 1), bar.Size.Y));
        over.DrawRect(fill, new Color(UiTheme.Accent, a));
        // a light sweep across the filled part: alive even while a step takes a moment
        if (fill.Size.X > 12 * k)
        {
            float sw = 60 * k, sx = fill.Position.X - sw + (loadClock * 260 * k) % (fill.Size.X + sw);
            float x0 = Mathf.Max(fill.Position.X, sx), x1 = Mathf.Min(fill.End.X, sx + sw);
            if (x1 > x0) over.DrawRect(new Rect2(x0, fill.Position.Y, x1 - x0, fill.Size.Y), new Color(1, 1, 1, 0.22f * a));
        }
        over.DrawRect(bar, new Color(UiTheme.Text, 0.2f * a), false, 1f);

        // step + hint
        int ss = UiTheme.Fs(15, k), hs = UiTheme.Fs(11, k);
        string status = readingFile ? "Reading the replay file…" : stage?.LoadStatus is { Length: > 0 } st ? st : "Preparing…";
        Gfx.TextFit(over, UiTheme.Body, status, x, bar.End.Y + 14 * k + ss, ss, new Color(UiTheme.Dim, a), right - x);
        Gfx.TextFit(over, UiTheme.HudWide, "PLAYS WHEN READY · PRESS V FOR 2D NOW", x, card.End.Y - 18 * k, hs,
            new Color(UiTheme.Faint, a), right - x);
    }

    /// <summary>A subtle note at the top centre of the view (2D fallback reasons).</summary>
    void Note(Vector2 sz, string text)
    {
        int fs = UiTheme.Fs(12, k);
        float w = Mathf.Min(Gfx.TextW(UiTheme.HudWide, text.ToUpperInvariant(), fs) + 24 * k, sz.X * 0.5f);
        var r = new Rect2((sz.X - w) / 2, 12 * k, w, fs + 12 * k);
        over.DrawRect(r, new Color(0.02f, 0.035f, 0.05f, 0.75f));
        over.DrawRect(r, new Color(UiTheme.Text, 0.14f), false, 1f);
        Gfx.TextFit(over, UiTheme.HudWide, text.ToUpperInvariant(), r.Position.X + 12 * k, Gfx.Mid(r.GetCenter().Y, fs), fs, UiTheme.Dim, w - 20 * k);
    }

    /// <summary>The analysis over the 3D picture, all anchored in the world through the replay camera.</summary>
    void Paint3D(Vector2 sz, Rect2 bounds)
    {
        var cam = stage!.Camera!;
        if (!IsInstanceValid(cam) || !cam.IsInsideTree()) return;
        var vs = sv != null ? (Vector2)sv.Size : sz;
        var scale = new Vector2(sz.X / Mathf.Max(1f, vs.X), sz.Y / Mathf.Max(1f, vs.Y));
        Vector2? Proj(Vector3 w) => cam.IsPositionBehind(w) ? null : cam.UnprojectPosition(w) * scale;
        var p = rec!.PlayerAt(t);
        int cs = UiTheme.Fs(12, k), fs = UiTheme.Fs(14, k);
        var f = run.Frames;
        var cur = f.Count > 0 ? FocusAt(t) : (Id: 0, Yaw: 0f, Pitch: 0f, R: 0f);
        var enemy = Main.I?.EnemyColor ?? UiTheme.EnemyColors[0];

        // what the player saw: the sniper scope, then the flash white-out
        var (sniper, scopeKind) = ScopeAt(t);
        float e = Mathf.Clamp(p.AdsBlend, 0, 1);
        e = e * e * (3f - 2f * e); // AdsSession.AdsEase
        bool scoped = (p.Bits & PBits.Scoped) != 0;
        bool scopeUp = scoped && sniper && e >= 0.6f;
        if (scopeUp)
            try { CrosshairView.DrawScope(over, bounds, scopeKind, Mathf.SmoothStep(0.6f, 1f, e), Valorant.CrosshairCode.Effective.SniperDotFor()); }
            catch { /* no profile */ }
        if (p.Blind > 0.01f) over.DrawRect(bounds, new Color(1, 1, 1, Mathf.Clamp(p.Blind, 0, 1) * 0.92f));

        // crosshair trail: where the aim went over the last half second, anchored in the world
        if (f.Count > 1)
        {
            int j0 = FrameIndex(t - TrailSec), j1 = FrameIndex(t);
            int stepN = Math.Max(1, (j1 - j0) / 90);
            Vector2? prev = null;
            var tc = new Color(0.85f, 1f, 0.95f);
            for (int j = j0; ; j += stepN)
            {
                bool last = j >= j1;
                float tj = last ? t : f[j].T;
                if (tj < t - TrailSec - 0.02f) { if (last) break; continue; }
                var q = Proj(stage.AimPoint(tj));
                if (q is { } qq && prev is { } pv && pv.DistanceTo(qq) < sz.X)
                {
                    float age = Mathf.Clamp((t - tj) / TrailSec, 0, 1);
                    over.DrawLine(pv, qq, new Color(tc, (1 - age) * 0.8f), Mathf.Max(1.5f, (1.2f + 2.2f * (1 - age)) * k), true);
                }
                prev = q;
                if (last) break;
            }
        }

        // target ring (same size as the 2D view's target; green while on it) or an edge arrow when off screen
        bool onScreen = false;
        if (cur.Id != 0 && stage.FocusPoint is { } fp)
        {
            if (Proj(fp) is { } tp && bounds.Grow(-4).HasPoint(tp))
            {
                onScreen = true;
                float dist = cam.GlobalPosition.DistanceTo(fp);
                var edge = Proj(fp + cam.GlobalBasis.X * dist * Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(cur.R, 0, 80))));
                float r = Mathf.Max(3f, edge is { } ep ? ep.DistanceTo(tp) : 0f);
                bool on = Mathf.Sqrt(cur.Yaw * cur.Yaw + cur.Pitch * cur.Pitch) <= cur.R;
                over.DrawArc(tp, r + 4 * k, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.45f), Mathf.Max(1, 3.4f * k), true);
                over.DrawArc(tp, r + 4 * k, 0, Mathf.Tau, 48, on ? UiTheme.Good : new Color(Colors.White, 0.85f), Mathf.Max(1, (on ? 2.4f : 1.4f) * k), true);
                if (r < 9 * k) over.DrawArc(tp, r + 11 * k, 0, Mathf.Tau, 32, new Color(enemy, 0.55f), Mathf.Max(1, 1 * k), true); // tiny targets: a halo
            }
            else EdgeArrow(over, bounds, cur.Yaw, cur.Pitch, enemy);
        }

        // bullets where they landed
        for (int i = FirstShotAfter(t - ShotFadeSec); i < run.Shots.Count && run.Shots[i].T <= t; i++)
            if (stage.ShotEnd(i) is { } end && Proj(end) is { } bp && bounds.HasPoint(bp))
                ShotDot(over, bp, run.Shots[i], t - run.Shots[i].T);

        if (!scopeUp)
            Crosshair(over, sz, scoped && !sniper && e >= 0.55f ? CrosshairView.Mode.Ads : CrosshairView.Mode.Primary);

        // captions: the view, the moment at the playhead, partial support, status line
        var cap = CaptionPlate(over, new Vector2(12 * k, 12 * k), zoom == 1 ? "3D REPLAY · 1×" : "3D CLOSE-UP · 4×", UiTheme.Text, cs);
        if (mi >= 0 && mi < moments.Count)
        {
            var m = moments[mi];
            var mc = RewindData.CodeColor(m.Code);
            CaptionPlate(over, new Vector2(12 * k, cap.End.Y + 6 * k), $"MOMENT {mi + 1}/{moments.Count} · {m.Label.ToUpperInvariant()}", mc, cs, mc,
                sz.X * 0.62f);
        }
        string fov = $"{stage.Pose.HFov:0}° FOV";
        float fw = Gfx.TextW(UiTheme.HudWide, fov, cs) + 18 * k;
        var fovR = new Rect2(sz.X - 12 * k - fw, 12 * k, fw, cs + 12 * k);
        over.DrawRect(fovR, new Color(0.02f, 0.035f, 0.05f, 0.7f));
        Gfx.TextC(over, UiTheme.HudWide, fov, fovR.GetCenter().X, Gfx.Mid(fovR.GetCenter().Y, cs), cs, UiTheme.Dim);
        if (support == ReplaySupport.Partial)
        {
            const string chip = "UTILITY NOT SHOWN IN 3D YET";
            int chs = UiTheme.Fs(11, k);
            float cw = Gfx.TextW(UiTheme.HudWide, chip, chs) + 18 * k;
            var cr = new Rect2(sz.X - 12 * k - cw, fovR.End.Y + 6 * k, cw, chs + 10 * k);
            over.DrawRect(cr, new Color(0.02f, 0.035f, 0.05f, 0.75f));
            over.DrawRect(cr, new Color(UiTheme.Warn, 0.7f), false, 1f);
            Gfx.Text(over, UiTheme.HudWide, chip, cr.Position.X + 9 * k, Gfx.Mid(cr.GetCenter().Y, chs), chs, UiTheme.Warn);
        }

        if (f.Count == 0) return;
        var fr0 = f[FrameIndex(t)];
        string status;
        Color sc = UiTheme.Text;
        if (cur.Id != 0)
        {
            float err = Mathf.Sqrt(cur.Yaw * cur.Yaw + cur.Pitch * cur.Pitch);
            if (err <= cur.R) { status = "On the target"; sc = UiTheme.Good; }
            else status = $"Target {err:0.0}° away";
            if (!onScreen && stage.FocusPoint != null) status += " (off screen)";
        }
        else status = "No target";
        string extra = (fr0.Has(FrameFlags.Firing) ? "  ·  FIRING" : "") + (fr0.Speed > 1f ? $"  ·  MOVING {fr0.Speed:0.0} M/S" : "");
        float sw = Gfx.TextW(UiTheme.Body, status, fs) + Gfx.TextW(UiTheme.HudWide, extra, cs);
        float y = sz.Y - 14 * k;
        over.DrawRect(new Rect2(6 * k, y - fs - 8 * k, sw + 16 * k, fs + 16 * k), new Color(0.02f, 0.035f, 0.05f, 0.7f));
        Gfx.Text(over, UiTheme.Body, status, 14 * k, y, fs, sc);
        if (extra.Length > 0)
            Gfx.Text(over, UiTheme.HudWide, extra, 14 * k + Gfx.TextW(UiTheme.Body, status, fs), y, cs, fr0.Has(FrameFlags.Firing) ? UiTheme.Accent : UiTheme.Warn);
    }

    /// <summary>The gun at <paramref name="time"/> (the last weapon switch, else the run's weapon): sniper scope or not.</summary>
    (bool Sniper, CrosshairView.SniperScope Kind) ScopeAt(float time)
    {
        var kind = WeaponKind.None;
        if (rec != null)
            for (int i = rec.FirstEventAtOrAfter(time) - 1; i >= 0; i--)
                if (rec.Events[i].Kind == RxKind.Weapon) { kind = (WeaponKind)rec.Events[i].Arg; break; }
        if (kind == WeaponKind.None) Enum.TryParse(run.Weapon, true, out kind);
        bool sniper = Weapons.Get(kind)?.Sniper == true;
        return (sniper, kind == WeaponKind.TourDeForce ? CrosshairView.SniperScope.TourDeForce : CrosshairView.SniperScope.Operator);
    }

    /// <summary>Timeline: "VIEW 2D | 3D" (3D shows its load progress under the button).</summary>
    void PaintViewToggle(float x, float y, float bh, int fs, int cs)
    {
        Gfx.Text(view, UiTheme.HudWide, "VIEW", x, Gfx.Mid(y + bh / 2, cs), cs, UiTheme.Faint);
        x += Gfx.TextW(UiTheme.HudWide, "VIEW", cs) + 10 * k;
        float w = Mathf.Max(56 * k, Gfx.TextW(UiTheme.HudWide, "3D", fs) + 20 * k);
        Button(new Rect2(x, y, w, bh), "2D", () => Set3D(false), selected: !view3D, fs: 14);
        x += w + 6 * k;
        var r3 = new Rect2(x, y, w, bh);
        Button(r3, "3D", () => Set3D(true), on: Has3D || recTask != null, selected: view3D, fs: 14);
        if (stage is { Built: false, Failed: false } || recTask != null)
            view.DrawRect(new Rect2(r3.Position.X + 1, r3.End.Y - 3 * k, (r3.Size.X - 2) * barShown, 3 * k), UiTheme.Accent);
    }
}
