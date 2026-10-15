using Godot;
using ValTrainer.Analysis;
using ValTrainer.Game;

namespace ValTrainer.UI;

/// <summary>Rewind drawing: the player's view (clipped child control) and the side panel with its three charts.</summary>
public partial class RewindScreen
{
    const float TrailSec = 0.5f, ShotFadeSec = 1.2f;

    // =====================================================================================
    // frames
    // =====================================================================================

    /// <summary>Index of the last frame at or before <paramref name="time"/> (0 if none).</summary>
    int FrameIndex(float time)
    {
        var f = run.Frames;
        int lo = 0, hi = f.Count - 1, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (f[mid].T <= time) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }

    /// <summary>The target relative to the crosshair at <paramref name="time"/>, interpolated between frames.</summary>
    (int Id, float Yaw, float Pitch, float R) FocusAt(float time)
    {
        var f = run.Frames;
        if (f.Count == 0) return (0, 0, 0, 0);
        int i = FrameIndex(time);
        var a = f[i];
        if (a.FocusId != 0 && i + 1 < f.Count && f[i + 1].FocusId == a.FocusId && f[i + 1].T > a.T)
        {
            var b = f[i + 1];
            float u = Mathf.Clamp((time - a.T) / (b.T - a.T), 0, 1);
            return (a.FocusId, Mathf.Lerp(a.FocusYaw, b.FocusYaw, u), Mathf.Lerp(a.FocusPitch, b.FocusPitch, u), Mathf.Lerp(a.FocusRadius, b.FocusRadius, u));
        }
        return (a.FocusId, a.FocusYaw, a.FocusPitch, a.FocusRadius);
    }

    // =====================================================================================
    // the player's view
    // =====================================================================================

    float focal;
    Vector2 centre;

    /// <summary>Screen position of a direction (deg from the crosshair, + = right / up); null when behind the player.</summary>
    Vector2? P(float yawDeg, float pitchDeg)
    {
        float y = Mathf.DegToRad(yawDeg), p = Mathf.DegToRad(pitchDeg);
        float dx = Mathf.Sin(y) * Mathf.Cos(p), dy = Mathf.Sin(p), dz = Mathf.Cos(y) * Mathf.Cos(p);
        if (dz < 0.03f) return null;
        return centre + new Vector2(focal * dx / dz, -focal * dy / dz);
    }

    float Px(float deg) => focal * Mathf.Tan(Mathf.DegToRad(Mathf.Clamp(deg, 0, 80)));

    void PaintEye()
    {
        var sz = eye.Size;
        if (sz.X < 4 || sz.Y < 4) return;
        centre = sz / 2;
        float hfov = run.HFov > 1 && run.HFov < 170 ? run.HFov : PlayerView.HipHFov;
        focal = sz.X / 2 / Mathf.Tan(Mathf.DegToRad(hfov / 2)) * zoom;
        var bounds = new Rect2(Vector2.Zero, sz);
        Gfx.VGradient(eye, bounds, new Color(0.06f, 0.09f, 0.12f), new Color(0.035f, 0.055f, 0.075f));
        float gridDeg = Grid(sz);

        var f = run.Frames;
        int fs = UiTheme.Fs(14, k), cs = UiTheme.Fs(12, k);
        if (f.Count == 0)
        {
            Gfx.TextC(eye, UiTheme.HudWide, "NO FRAMES RECORDED", centre.X, centre.Y - 40 * k, cs, UiTheme.Dim);
            Crosshair(sz);
            return;
        }
        var cur = FocusAt(t);
        var enemy = Main.I?.EnemyColor ?? UiTheme.EnemyColors[0];

        // a target that just left: shown faded for up to 2 s
        float fade = 1f;
        var shown = cur;
        if (cur.Id == 0)
        {
            shown = (0, 0, 0, 0);
            for (int j = FrameIndex(t); j >= 0 && f[j].T > t - 2f; j--)
                if (f[j].FocusId != 0) { shown = (f[j].FocusId, f[j].FocusYaw, f[j].FocusPitch, f[j].FocusRadius); fade = 0.28f; break; }
        }

        // trail: the target's path relative to the crosshair over the last half second (flicks, overshoots, corrections)
        if (shown.Id != 0)
        {
            float tEnd = cur.Id != 0 ? t : FrameTimeOfLast(shown.Id, t);
            int j0 = FrameIndex(tEnd - TrailSec), j1 = FrameIndex(tEnd);
            int stepN = Math.Max(1, (j1 - j0) / 160);
            Vector2? prev = null;
            for (int j = j0; j <= j1; j += stepN)
            {
                var fr = f[j];
                if (fr.FocusId != shown.Id) { prev = null; continue; }
                var p = P(fr.FocusYaw, fr.FocusPitch);
                if (p is { } pp && prev is { } pv)
                {
                    float age = Mathf.Clamp((tEnd - fr.T) / TrailSec, 0, 1);
                    float a = (1 - age) * 0.85f * fade;
                    eye.DrawLine(pv, pp, new Color(enemy.Lightened(0.35f), a), Mathf.Max(1.5f, (1.2f + 2.2f * (1 - age)) * k), true);
                }
                prev = p;
            }
            if (cur.Id != 0 && P(cur.Yaw, cur.Pitch) is { } cp && prev is { } last) eye.DrawLine(last, cp, new Color(enemy.Lightened(0.35f), 0.85f), Mathf.Max(1.5f, 3.4f * k), true);
        }

        // the target
        bool onScreen = false;
        if (shown.Id != 0 && P(shown.Yaw, shown.Pitch) is { } tp && bounds.Grow(-4).HasPoint(tp))
        {
            onScreen = true;
            float r = Mathf.Max(2.5f, Px(shown.R));
            bool on = cur.Id != 0 && Mathf.Sqrt(cur.Yaw * cur.Yaw + cur.Pitch * cur.Pitch) <= cur.R;
            eye.DrawCircle(tp, r, new Color(enemy, 0.85f * fade));
            eye.DrawArc(tp, r, 0, Mathf.Tau, 48, new Color(on ? UiTheme.Good : Colors.White, (on ? 0.95f : 0.55f) * fade), Mathf.Max(1, (on ? 2.2f : 1.2f) * k), true);
            if (r < 9 * k) eye.DrawArc(tp, r + 7 * k, 0, Mathf.Tau, 32, new Color(enemy, 0.45f * fade), Mathf.Max(1, 1 * k), true); // tiny targets: a halo to find them
        }
        else if (shown.Id != 0 && fade >= 1f) EdgeArrow(bounds, shown.Yaw, shown.Pitch, enemy);

        // bullets: appear at their time, stuck to the target they were fired at, fading out
        int si = FirstShotAfter(t - ShotFadeSec);
        for (int i = si; i < run.Shots.Count && run.Shots[i].T <= t; i++)
        {
            var s = run.Shots[i];
            float age = t - s.T;
            Vector2? pos;
            if (s.FocusId != 0 && s.FocusId == cur.Id) pos = P(cur.Yaw + s.ErrYaw, cur.Pitch + s.ErrPitch);
            else if (s.FocusId != 0)
            {
                var at = FocusAt(s.T);
                pos = at.Id == s.FocusId ? P(at.Yaw + s.ErrYaw, at.Pitch + s.ErrPitch) : centre;
            }
            else pos = centre;
            if (pos is not { } bp || !bounds.HasPoint(bp)) continue;
            ShotDot(eye, bp, s, age);
        }

        Crosshair(sz);

        // captions
        CaptionPlate(new Vector2(12 * k, 12 * k), zoom == 1 ? "PLAYER'S VIEW · 1×" : "CLOSE-UP · 4×", UiTheme.Text, cs);
        Gfx.TextR(eye, UiTheme.HudWide, $"{hfov:0}° FOV", sz.X - 14 * k, 12 * k + cs + 6 * k, cs, UiTheme.Faint);
        Gfx.TextR(eye, UiTheme.HudWide, gridDeg < 1 ? $"GRID {gridDeg:0.0}°" : $"GRID {gridDeg:0}°", sz.X - 14 * k, sz.Y - 14 * k, cs, UiTheme.Faint);

        var fr0 = f[FrameIndex(t)];
        string status;
        Color sc = UiTheme.Text;
        if (cur.Id != 0)
        {
            float err = Mathf.Sqrt(cur.Yaw * cur.Yaw + cur.Pitch * cur.Pitch);
            if (err <= cur.R) { status = "On the target"; sc = UiTheme.Good; }
            else status = $"Target {err:0.0}° away";
            if (!onScreen) status += " (off screen)";
        }
        else status = shown.Id != 0 ? "No target — last one shown faded" : "No target";
        float x = 14 * k, y = sz.Y - 14 * k;
        x += Gfx.TextW(UiTheme.Body, status, fs) + 0;
        Gfx.Text(eye, UiTheme.Body, status, 14 * k, y, fs, sc);
        if (fr0.Has(FrameFlags.Firing)) { Gfx.Text(eye, UiTheme.HudWide, "  ·  FIRING", x, y, cs, UiTheme.Accent); x += Gfx.TextW(UiTheme.HudWide, "  ·  FIRING", cs); }
        if (fr0.Speed > 1f) Gfx.Text(eye, UiTheme.HudWide, $"  ·  MOVING {fr0.Speed:0.0} M/S", x, y, cs, UiTheme.Warn);
        if (cur.Id == 0)
            Gfx.TextC(eye, UiTheme.HudWide, "NO TARGET", centre.X, centre.Y - Mathf.Max(60 * k, sz.Y * 0.18f), UiTheme.Fs(16, k), new Color(UiTheme.Text, 0.55f));
    }

    /// <summary>A bullet: green hit / red miss dot (white ring = headshot), popping in and fading out over <see cref="ShotFadeSec"/>.</summary>
    void ShotDot(CanvasItem ci, Vector2 bp, ShotSample s, float age)
    {
        float a = 1 - age / ShotFadeSec;
        var c = s.Hit ? RewindData.HitColor : RewindData.MissColor;
        float r = (zoom > 1 ? 5.5f : 4.5f) * k;
        ci.DrawCircle(bp, r + 1.5f * k, new Color(0, 0, 0, 0.6f * a));
        ci.DrawCircle(bp, r, new Color(c, a));
        if (s.Zone == 0) ci.DrawArc(bp, r + 3 * k, 0, Mathf.Tau, 20, new Color(Colors.White, a), Mathf.Max(1, 1.4f * k), true); // headshot
        if (age < 0.15f) ci.DrawArc(bp, r + age * 120 * k, 0, Mathf.Tau, 24, new Color(c, (0.15f - age) / 0.15f), Mathf.Max(1, 2 * k), true);
    }

    float FrameTimeOfLast(int id, float before)
    {
        var f = run.Frames;
        for (int j = FrameIndex(before); j >= 0; j--) if (f[j].FocusId == id) return f[j].T;
        return before;
    }

    int FirstShotAfter(float time)
    {
        var s = run.Shots;
        int lo = 0, hi = s.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (s[mid].T < time) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Degree grid (spacing picked to stay readable at this zoom); returns the spacing.</summary>
    float Grid(Vector2 sz)
    {
        float step = 20f;
        foreach (var s in new[] { 0.5f, 1f, 2f, 5f, 10f, 20f })
            if (Px(s) >= 46 * k) { step = s; break; }
        var minor = new Color(1, 1, 1, 0.04f);
        var axis = new Color(1, 1, 1, 0.1f);
        for (int n = 0; n * step < 80; n++)
        {
            float d = Px(n * step);
            if (d > sz.X / 2 && d > sz.Y / 2) break;
            var c = n == 0 ? axis : minor;
            if (d <= sz.X / 2)
            {
                eye.DrawLine(new Vector2(centre.X + d, 0), new Vector2(centre.X + d, sz.Y), c, 1f);
                if (n > 0) eye.DrawLine(new Vector2(centre.X - d, 0), new Vector2(centre.X - d, sz.Y), c, 1f);
            }
            if (d <= sz.Y / 2)
            {
                eye.DrawLine(new Vector2(0, centre.Y - d), new Vector2(sz.X, centre.Y - d), c, 1f);
                if (n > 0) eye.DrawLine(new Vector2(0, centre.Y + d), new Vector2(sz.X, centre.Y + d), c, 1f);
            }
        }
        return step;
    }

    void Crosshair(Vector2 sz) => Crosshair(eye, sz, CrosshairView.Mode.Primary);

    /// <summary>The player's crosshair (their profile, magnified with the view's zoom) at the centre of a <paramref name="sz"/> canvas.</summary>
    void Crosshair(CanvasItem ci, Vector2 sz, CrosshairView.Mode mode)
    {
        var c0 = (sz / 2).Floor();
        float ck = Mathf.Max(1f, sz.Y / 1080f * zoom);
        try
        {
            var style = Valorant.CrosshairCode.Effective.StyleFor(mode);
            if (!style.Hide) CrosshairView.DrawStyle(ci, style, c0, ck);
            return;
        }
        catch { /* no profile: fall back to a plain cross */ }
        float l = 6 * ck, g = 3 * ck, w = Mathf.Max(1, 2 * ck / 2) * 1;
        var c = new Color(0.4f, 1f, 0.6f);
        ci.DrawRect(new Rect2(c0.X + g, c0.Y - w / 2, l, w), c);
        ci.DrawRect(new Rect2(c0.X - g - l, c0.Y - w / 2, l, w), c);
        ci.DrawRect(new Rect2(c0.X - w / 2, c0.Y + g, w, l), c);
        ci.DrawRect(new Rect2(c0.X - w / 2, c0.Y - g - l, w, l), c);
    }

    void EdgeArrow(Rect2 bounds, float yaw, float pitch, Color c) => EdgeArrow(eye, bounds, yaw, pitch, c);

    /// <summary>Off-screen target: an arrow on the edge pointing at it, with its distance.</summary>
    void EdgeArrow(CanvasItem ci, Rect2 bounds, float yaw, float pitch, Color c)
    {
        var dir = new Vector2(yaw, -pitch);
        if (dir.LengthSquared() < 1e-6f) return;
        dir = dir.Normalized();
        var inner = bounds.Grow(-30 * k);
        float sx = dir.X != 0 ? (inner.Size.X / 2) / Mathf.Abs(dir.X) : float.MaxValue;
        float sy = dir.Y != 0 ? (inner.Size.Y / 2) / Mathf.Abs(dir.Y) : float.MaxValue;
        var p = centre + dir * Mathf.Min(sx, sy);
        var n = new Vector2(-dir.Y, dir.X);
        float s = 12 * k;
        Span<Vector2> tri = stackalloc Vector2[3];
        tri[0] = p + dir * s; tri[1] = p - dir * s * 0.6f + n * s * 0.8f; tri[2] = p - dir * s * 0.6f - n * s * 0.8f;
        ci.FillPoly(tri, c);
        int fs = UiTheme.Fs(13, k);
        string d = $"{Mathf.Sqrt(yaw * yaw + pitch * pitch):0}°";
        var lp = p - dir * 26 * k;
        Gfx.TextC(ci, UiTheme.HudWide, d, lp.X, Gfx.Mid(lp.Y, fs), fs, UiTheme.Text);
    }

    void CaptionPlate(Vector2 pos, string text, Color c, int fs) => CaptionPlate(eye, pos, text, c, fs);

    /// <summary>A small dark plate with a coloured bar and one line of text (shortened to <paramref name="maxW"/>); returns its rect.</summary>
    Rect2 CaptionPlate(CanvasItem ci, Vector2 pos, string text, Color c, int fs, Color? bar = null, float maxW = float.MaxValue)
    {
        float w = Mathf.Min(Gfx.TextW(UiTheme.HudWide, text, fs) + 18 * k, maxW);
        var r = new Rect2(pos, new Vector2(w, fs + 12 * k));
        ci.DrawRect(r, new Color(0.02f, 0.035f, 0.05f, 0.8f));
        ci.DrawRect(new Rect2(r.Position, new Vector2(3 * k, r.Size.Y)), bar ?? UiTheme.Accent);
        Gfx.TextFit(ci, UiTheme.HudWide, text, r.Position.X + 10 * k, Gfx.Mid(r.GetCenter().Y, fs), fs, c, r.Size.X - 14 * k);
        return r;
    }

    // =====================================================================================
    // side panel
    // =====================================================================================

    /// <summary>The engagement at the playhead (or the focused moment's), with its index in the insights' trials.</summary>
    (TrialInfo? T, int I) CurrentTrial()
    {
        if (ins == null || ins.Trials.Count == 0) return (null, -1);
        if (mi >= 0 && mi < moments.Count && moments[mi].Trial is int ti && ti >= 0 && ti < ins.Trials.Count)
        {
            var tr = ins.Trials[ti];
            if (tr.T0 <= moments[mi].T1 + 0.5f && tr.TEnd >= moments[mi].T0 - 0.5f) return (tr, ti);
        }
        int best = -1;
        for (int i = 0; i < ins.Trials.Count; i++)
        {
            var tr = ins.Trials[i];
            if (tr.T0 <= t && t <= tr.TEnd + 0.15f && (best < 0 || tr.T0 >= ins.Trials[best].T0)) best = i;
        }
        if (best < 0)
            for (int i = 0; i < ins.Trials.Count; i++)
            {
                var tr = ins.Trials[i];
                if (tr.TEnd < t && t - tr.TEnd < 1f && (best < 0 || tr.TEnd > ins.Trials[best].TEnd)) best = i;
            }
        return best < 0 ? (null, -1) : (ins.Trials[best], best);
    }

    void PaintSide()
    {
        var sp = side;
        Gfx.Plate(view, sp, 18 * k, new Color(0.04f, 0.07f, 0.1f, 0.95f), new Color(UiTheme.Text, 0.12f));
        view.DrawRect(new Rect2(sp.Position, new Vector2(48 * k, 3 * k)), UiTheme.Accent);
        var inner = sp.Grow(-22 * k);
        float x = inner.Position.X, w = inner.Size.X, y = inner.Position.Y + 4 * k;
        int cs = UiTheme.Fs(12, k), bs = UiTheme.Fs(15, k);

        var (tr, ti) = CurrentTrial();
        string count = tr == null || ins == null ? "" : $"{(tr.Bot ? "BOT" : "TARGET")} {ti + 1} OF {ins.Trials.Count}";
        y = Section("THIS ENGAGEMENT", count, x, y, w);

        // the moment at the playhead
        if (mi >= 0 && mi < moments.Count)
        {
            var m = moments[mi];
            var mc = RewindData.CodeColor(m.Code);
            string chip = RewindData.CodeName(m.Code).ToUpperInvariant();
            float cw = Gfx.TextW(UiTheme.HudWide, chip, cs) + 14 * k;
            var cr = new Rect2(x, y, Mathf.Min(cw, w * 0.5f), 22 * k);
            view.DrawRect(cr, new Color(mc, 0.16f));
            view.DrawRect(cr, mc, false, 1f);
            Gfx.TextFit(view, UiTheme.HudWide, chip, cr.Position.X + 7 * k, Gfx.Mid(cr.GetCenter().Y, cs), cs, mc, cr.Size.X - 12 * k);
            int ls = UiTheme.Fs(22, k), lsMin = UiTheme.Fs(16, k);
            float lw = w - cr.Size.X - 10 * k;
            while (ls > lsMin && Gfx.TextW(UiTheme.Display, m.Label, ls) > lw) ls--; // shrink a little before truncating
            Gfx.TextFit(view, UiTheme.Display, m.Label, cr.End.X + 10 * k, Gfx.Mid(cr.GetCenter().Y, ls), ls, UiTheme.Text, lw);
            y += 34 * k;
        }

        if (tr == null)
        {
            Gfx.Text(view, UiTheme.Body, ins == null ? "Analysing…" : "No target at this point of the run.", x, y + bs, bs, UiTheme.Dim);
            y += 40 * k;
        }
        else
        {
            // 3 × 2 tiles
            float gap = 8 * k, tw = (w - 2 * gap) / 3, th = 64 * k;
            string Ms(float a, float b) => float.IsNaN(a) || float.IsNaN(b) || b < a ? "—" : $"{(b - a) * 1000f:0} ms";
            Tile(new Rect2(x, y, tw, th), "REACT", Ms(tr.T0, tr.TOnset), null, UiTheme.Text);
            Tile(new Rect2(x + tw + gap, y, tw, th), "FLICK", Ms(tr.TOnset, tr.TPrimEnd), null, UiTheme.Text);
            Tile(new Rect2(x + 2 * (tw + gap), y, tw, th), "SETTLE", Ms(tr.TPrimEnd, tr.TOnTarget), null, UiTheme.Text);
            float y2 = y + th + gap;
            Tile(new Rect2(x, y2, tw, th), "CLICK", Ms(tr.TOnTarget, tr.THit), null, UiTheme.Text);
            string over = "—";
            var oc = UiTheme.Text;
            if (!float.IsNaN(tr.Gain))
            {
                float pct = (tr.Gain - 1f) * 100f;
                over = pct >= 0 ? $"+{pct:0}%" : $"{-pct:0}% short";
                oc = Mathf.Abs(pct) <= 10 ? UiTheme.Good : pct > 0 ? UiTheme.Warn : UiTheme.Teal;
            }
            Tile(new Rect2(x + tw + gap, y2, tw, th), "OVERSHOOT", over, null, oc);
            string first = "no shot";
            string? firstSub = null;
            var fc = UiTheme.Dim;
            if (tr.HasFirstShot)
            {
                if (tr.FirstHit) { first = tr.Head ? "HEAD" : "HIT"; fc = UiTheme.Good; }
                else
                {
                    first = "MISS"; fc = RewindData.MissColor;
                    if (!float.IsNaN(tr.FirstShotErrR)) firstSub = $"{tr.FirstShotErrR:0.0}× radius off";
                }
            }
            Tile(new Rect2(x + 2 * (tw + gap), y2, tw, th), "FIRST SHOT", first, firstSub, fc);
            y = y2 + th + 12 * k;

            // one line describing the engagement
            var parts = new List<string>();
            if (tr.AmpDeg > 0) parts.Add(float.IsNaN(tr.DirDeg) ? $"{tr.AmpDeg:0.0}° flick" : $"{tr.AmpDeg:0.0}° flick {RewindData.SectorName((int)MathF.Round(tr.DirDeg / 45f))}");
            parts.Add(tr.Bot ? "bot" : tr.Static ? "still target" : "moving target");
            if (tr.Corrections > 0) parts.Add($"{tr.Corrections} correction{(tr.Corrections == 1 ? "" : "s")}");
            parts.Add(tr.Hit ? $"hit after {(tr.THit - tr.T0) * 1000f:0} ms" : "no hit");
            int ds = UiTheme.Fs(14, k);
            Gfx.TextFit(view, UiTheme.Body, string.Join(" · ", parts), x, y + ds, ds, UiTheme.Dim, w);
            y += ds + 18 * k;
        }

        // tabs
        y += 6 * k;
        var tabs = Enumerable.Range(0, 3).Where(HasTab).ToList();
        if (ins == null) return;
        if (tabs.Count == 0)
        {
            y = Section("THIS RUN", "", x, y, w);
            Gfx.Text(view, UiTheme.Body, "No shots or flicks were recorded in this run.", x, y + bs, bs, UiTheme.Dim);
            return;
        }
        if (!tabs.Contains(tab)) tab = tabs[0];
        float tabH = 38 * k, tabW = w / tabs.Count;
        int tfs = UiTheme.Fs(13, k);
        view.DrawRect(new Rect2(x, y + tabH - 1, w, 1), new Color(UiTheme.Text, 0.15f));
        for (int i = 0; i < tabs.Count; i++)
        {
            int id = tabs[i];
            var r = new Rect2(x + i * tabW, y, tabW, tabH);
            bool sel = id == tab, hov = r.HasPoint(mouse);
            if (sel) view.DrawRect(new Rect2(r.Position.X, r.End.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
            else if (hov) view.DrawRect(r, new Color(1, 1, 1, 0.05f));
            Gfx.TextFit(view, UiTheme.HudWide, TabNames[id], r.Position.X + 4 * k, Gfx.Mid(r.GetCenter().Y, tfs), tfs, sel ? UiTheme.Text : hov ? UiTheme.Text : UiTheme.Dim, r.Size.X - 8 * k, HorizontalAlignment.Center);
            buttons.Add((r, () => tab = id));
        }
        y += tabH + 16 * k;
        var content = new Rect2(x, y, w, inner.End.Y - y);
        if (content.Size.Y < 60 * k) return;
        switch (tab)
        {
            case 0: ShotsTab(content, tr); break;
            case 1: TtkTab(content, tr); break;
            case 2: DirsTab(content, tr); break;
        }
    }

    float Section(string title, string right, float x, float y, float w)
    {
        int cs = UiTheme.Fs(13, k);
        float cy = y + 8 * k;
        view.DrawRect(new Rect2(x, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
        float tw = Gfx.TextW(UiTheme.HudWide, title, cs);
        Gfx.Text(view, UiTheme.HudWide, title, x + 14 * k, Gfx.Mid(cy, cs), cs, UiTheme.Dim);
        float rw = right.Length > 0 ? Gfx.TextW(UiTheme.HudWide, right, cs) + 12 * k : 0;
        if (right.Length > 0) Gfx.TextR(view, UiTheme.HudWide, right, x + w, Gfx.Mid(cy, cs), cs, UiTheme.Faint);
        float lx = x + 28 * k + tw, lw = w - 28 * k - tw - rw;
        if (lw > 4) view.DrawRect(new Rect2(lx, cy, lw, 1), new Color(UiTheme.Text, 0.1f));
        return y + 30 * k;
    }

    void Tile(Rect2 r, string caption, string value, string? sub, Color vc)
    {
        view.DrawRect(r, new Color(1, 1, 1, 0.035f));
        view.DrawRect(new Rect2(r.Position, new Vector2(2 * k, r.Size.Y)), new Color(UiTheme.Text, 0.2f));
        int cs = UiTheme.Fs(11, k), vs = UiTheme.Fs(28, k), ss = UiTheme.Fs(11, k);
        float pad = 10 * k;
        Gfx.TextFit(view, UiTheme.HudWide, caption, r.Position.X + pad, r.Position.Y + 8 * k + cs, cs, UiTheme.Dim, r.Size.X - pad * 2);
        float vy = r.Position.Y + (sub != null ? 44 * k : 52 * k);
        Gfx.TextFit(view, UiTheme.Display, value, r.Position.X + pad, vy, vs, vc, r.Size.X - pad * 2);
        if (sub != null) Gfx.TextFit(view, UiTheme.Body, sub, r.Position.X + pad, r.End.Y - 6 * k, ss, UiTheme.Dim, r.Size.X - pad * 2);
    }

    /// <summary>A big statistic: caption, value, note. Returns the next y.</summary>
    float Stat(float x, float y, float w, string caption, string value, string note, Color vc)
    {
        int cs = UiTheme.Fs(11, k), vs = UiTheme.Fs(34, k), ns = UiTheme.Fs(13, k);
        Gfx.TextFit(view, UiTheme.HudWide, caption, x, y + cs, cs, UiTheme.Dim, w);
        Gfx.TextFit(view, UiTheme.Display, value, x, y + cs + vs * 0.95f + 4 * k, vs, vc, w);
        Gfx.TextFit(view, UiTheme.Body, note, x, y + cs + vs + ns + 8 * k, ns, UiTheme.Dim, w);
        return y + cs + vs + ns + 24 * k;
    }

    static string Pct(float v) => float.IsNaN(v) ? "—" : $"{v:0}%";

    // ---------------- shots ----------------

    void ShotsTab(Rect2 c, TrialInfo? tr)
    {
        var I = ins!;
        int ls = UiTheme.Fs(12, k);
        float sq = Mathf.Min(c.Size.X * 0.56f, c.Size.Y - 30 * k);
        bool stack = c.Size.X - sq - 20 * k < 130 * k;
        if (stack) sq = Mathf.Min(c.Size.X, c.Size.Y * 0.55f);
        var box = new Rect2(c.Position.X, c.Position.Y, sq, sq);
        view.DrawRect(box, new Color(0.02f, 0.035f, 0.05f, 0.9f));
        view.DrawRect(box, new Color(UiTheme.Text, 0.12f), false, 1f);
        var o = box.GetCenter();
        const float range = 3f;
        float pr = sq / 2 / range; // px per target radius
        var enemy = Main.I?.EnemyColor ?? UiTheme.EnemyColors[0];
        view.DrawLine(new Vector2(box.Position.X, o.Y), new Vector2(box.End.X, o.Y), new Color(1, 1, 1, 0.07f), 1f);
        view.DrawLine(new Vector2(o.X, box.Position.Y), new Vector2(o.X, box.End.Y), new Color(1, 1, 1, 0.07f), 1f);
        view.DrawArc(o, pr * 2, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.07f), 1f, true);
        view.DrawCircle(o, pr, new Color(enemy, 0.16f));
        view.DrawArc(o, pr, 0, Mathf.Tau, 48, new Color(enemy, 0.8f), Mathf.Max(1, 1.5f * k), true);
        Gfx.Text(view, UiTheme.Body, "target", o.X + pr * 0.72f, o.Y - pr * 0.78f, UiTheme.Fs(11, k), new Color(enemy, 0.9f));
        float trT0 = tr?.T0 ?? -1, trT1 = tr?.TEnd + 0.1f ?? -1;
        foreach (var s in I.Scatter)
        {
            var v = new Vector2(s.X, -s.Y);
            bool outside = v.Length() > range * 0.96f;
            if (outside) v = v.Normalized() * range * 0.96f;
            var p = o + v * pr;
            var col = s.Hit ? RewindData.HitColor : RewindData.MissColor;
            float r = 3.2f * k;
            bool mine = tr != null && s.T >= trT0 && s.T <= trT1;
            if (outside) view.DrawArc(p, r, 0, Mathf.Tau, 12, new Color(col, 0.8f), Mathf.Max(1, 1.2f * k), true);
            else view.DrawCircle(p, r, new Color(col, mine ? 1f : 0.7f));
            if (mine) view.DrawArc(p, r + 3 * k, 0, Mathf.Tau, 16, UiTheme.Text, Mathf.Max(1, 1.5f * k), true);
        }
        // legend under the box
        float ly = box.End.Y + 18 * k;
        float lx = box.Position.X;
        view.DrawCircle(new Vector2(lx + 4 * k, ly - ls * 0.35f), 3.5f * k, RewindData.HitColor);
        Gfx.Text(view, UiTheme.Body, "hit", lx + 12 * k, ly, ls, UiTheme.Dim);
        lx += 12 * k + Gfx.TextW(UiTheme.Body, "hit", ls) + 12 * k;
        view.DrawCircle(new Vector2(lx + 4 * k, ly - ls * 0.35f), 3.5f * k, RewindData.MissColor);
        Gfx.Text(view, UiTheme.Body, "miss", lx + 12 * k, ly, ls, UiTheme.Dim);
        lx += 12 * k + Gfx.TextW(UiTheme.Body, "miss", ls) + 12 * k;
        if (tr != null)
        {
            view.DrawArc(new Vector2(lx + 4 * k, ly - ls * 0.35f), 5 * k, 0, Mathf.Tau, 16, UiTheme.Text, Mathf.Max(1, 1.2f * k), true);
            Gfx.TextFit(view, UiTheme.Body, "this engagement", lx + 14 * k, ly, ls, UiTheme.Dim, box.End.X - lx - 14 * k + (stack ? 0 : 200 * k));
        }

        // numbers
        float sx = stack ? c.Position.X : box.End.X + 22 * k, sy = stack ? ly + 18 * k : c.Position.Y, sw = stack ? c.Size.X : c.End.X - sx;
        int hits = I.Scatter.Count(s => s.Hit);
        float fb = I.FirstBulletAcc * 100f;
        if (stack)
        {
            // two columns
            float cw = (sw - 16 * k) / 2;
            Stat(sx, sy, cw, "FIRST BULLET", Pct(fb), $"{I.FirstHits} of {I.FirstShots} hit", fb >= 60 ? UiTheme.Good : UiTheme.Text);
            Stat(sx + cw + 16 * k, sy, cw, "WHILE MOVING", Pct(I.MovingShotPct), $"{I.MovingShots} of {I.Shots} shots", I.MovingShotPct > 15 ? UiTheme.Warn : UiTheme.Text);
            return;
        }
        sy = Stat(sx, sy, sw, "FIRST BULLET", Pct(fb), I.FirstShots > 0 ? $"{I.FirstHits} of {I.FirstShots} first shots hit" : "no first shots", fb >= 60 ? UiTheme.Good : UiTheme.Text);
        sy = Stat(sx, sy, sw, "SHOTS WHILE MOVING", Pct(I.MovingShotPct), $"{I.MovingShots} of {I.Shots} shots", I.MovingShotPct > 15 ? UiTheme.Warn : UiTheme.Text);
        if (sy < c.End.Y - 60 * k && I.Scatter.Count > 0)
            Stat(sx, sy, sw, "ALL SHOTS", $"{100f * hits / I.Scatter.Count:0}%", $"{hits} of {I.Scatter.Count} hit", UiTheme.Text);
    }

    // ---------------- time to hit ----------------

    static readonly (string Name, string What, Color C)[] Phases =
    {
        ("React", "target appears → you start moving", new Color(0.35f, 0.8f, 0.95f)),
        ("Flick", "the main movement", new Color(0.45f, 0.55f, 1f)),
        ("Settle", "small corrections until on it", UiTheme.Warn),
        ("Click", "on the target → the hit", new Color(0.85f, 0.5f, 1f)),
    };

    void TtkTab(Rect2 c, TrialInfo? tr)
    {
        var tk = ins!.Ttk;
        float x = c.Position.X, w = c.Size.X, y = c.Position.Y;
        int cs = UiTheme.Fs(12, k), vs = UiTheme.Fs(34, k), bs = UiTheme.Fs(15, k), ns = UiTheme.Fs(13, k);
        float[] med = { tk.ReactMs, tk.FlickMs, tk.SettleMs, tk.ClickMs };
        float total = float.IsNaN(tk.TotalMs) ? med.Where(v => !float.IsNaN(v)).Sum() : tk.TotalMs;
        float[]? mine = null;
        if (tr is { Hit: true })
        {
            float D(float a, float b) => float.IsNaN(a) || float.IsNaN(b) || b < a ? float.NaN : (b - a) * 1000f;
            mine = new[] { D(tr.T0, tr.TOnset), D(tr.TOnset, tr.TPrimEnd), D(tr.TPrimEnd, tr.TOnTarget), D(tr.TOnTarget, tr.THit) };
            if (mine.All(float.IsNaN)) mine = null;
        }
        Gfx.Text(view, UiTheme.HudWide, "MEDIAN TIME TO HIT", x, y + cs, cs, UiTheme.Dim);
        Gfx.Text(view, UiTheme.Display, $"{total:0} ms", x, y + cs + vs + 2 * k, vs, UiTheme.Text);
        Gfx.TextR(view, UiTheme.Body, $"over {tk.N} hit{(tk.N == 1 ? "" : "s")}", x + w, y + cs + vs + 2 * k, ns, UiTheme.Dim);
        y += cs + vs + 22 * k;

        float scale = Mathf.Max(1f, Mathf.Max(med.Where(v => !float.IsNaN(v)).Sum(), mine?.Where(v => !float.IsNaN(v)).Sum() ?? 0));
        y = Bar(x, y, w, 34 * k, med, scale, "TYPICAL");
        if (mine != null) y = Bar(x, y + 4 * k, w, 22 * k, mine, scale, "THIS ENGAGEMENT");
        y += 8 * k;

        for (int i = 0; i < 4; i++)
        {
            if (y + bs > c.End.Y) break;
            var (name, what, col) = Phases[i];
            view.DrawRect(new Rect2(x, y + bs * 0.15f, 12 * k, 12 * k), col);
            Gfx.Text(view, UiTheme.Body, name, x + 22 * k, y + bs, bs, UiTheme.Text);
            float nw = 90 * k;
            string val = float.IsNaN(med[i]) ? "—" : $"{med[i]:0} ms";
            float vw = Gfx.TextR(view, UiTheme.Hud, val, x + w, y + bs, bs, UiTheme.Text);
            Gfx.TextFit(view, UiTheme.Body, what, x + 22 * k + nw, y + bs, ns, UiTheme.Dim, w - 22 * k - nw - vw - 12 * k);
            y += bs + 14 * k;
        }
    }

    float Bar(float x, float y, float w, float h, float[] ms, float scale, string caption)
    {
        int cs = UiTheme.Fs(11, k), vs = UiTheme.Fs(12, k);
        Gfx.Text(view, UiTheme.HudWide, caption, x, y + cs, cs, UiTheme.Faint);
        y += cs + 6 * k;
        view.DrawRect(new Rect2(x, y, w, h), new Color(1, 1, 1, 0.04f));
        float bx = x;
        for (int i = 0; i < 4; i++)
        {
            if (float.IsNaN(ms[i]) || ms[i] <= 0) continue;
            float sw = w * ms[i] / scale;
            view.DrawRect(new Rect2(bx, y, Mathf.Max(1, sw - 1), h), Phases[i].C);
            string l = $"{ms[i]:0}";
            if (Gfx.TextW(UiTheme.Hud, l, vs) + 8 * k < sw && h >= 18 * k)
                Gfx.TextC(view, UiTheme.Hud, l, bx + sw / 2, Gfx.Mid(y + h / 2, vs), vs, new Color(0.03f, 0.05f, 0.07f));
            bx += sw;
        }
        return y + h + 12 * k;
    }

    // ---------------- flick directions ----------------

    void DirsTab(Rect2 c, TrialInfo? tr)
    {
        var I = ins!;
        int ls = UiTheme.Fs(12, k), bs = UiTheme.Fs(14, k);
        float sq = Mathf.Min(c.Size.X * 0.6f, c.Size.Y - 20 * k);
        bool stack = c.Size.X - sq - 20 * k < 140 * k;
        if (stack) sq = Mathf.Min(c.Size.X, c.Size.Y * 0.6f);
        var box = new Rect2(c.Position.X, c.Position.Y, sq, sq);
        var o = box.GetCenter();
        float R = sq / 2 - 24 * k;
        int maxN = Math.Max(1, I.SectorN.Max());
        view.DrawArc(o, R, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.1f), 1f, true);
        view.DrawArc(o, R * 0.5f, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.06f), 1f, true);
        int cur = tr != null && !float.IsNaN(tr.DirDeg) ? (int)MathF.Round(tr.DirDeg / 45f) % 8 : -1;
        for (int s = 0; s < 8; s++)
        {
            float a = s * Mathf.Pi / 4;
            var d = new Vector2(Mathf.Cos(a), -Mathf.Sin(a));
            view.DrawLine(o, o + d * R, new Color(1, 1, 1, 0.05f), 1f);
            int n = I.SectorN[s];
            if (n <= 0) continue;
            float len = R * (0.3f + 0.7f * Mathf.Sqrt((float)n / maxN));
            var col = SectorColor(I, s);
            Wedge(o, a, len, col, s == cur);
            float g = I.SectorGain[s];
            string l = float.IsNaN(g) ? $"{n}" : $"{(g - 1f) * 100f:+0;-0;0}%";
            var lp = o + d * (len + 14 * k);
            Gfx.TextC(view, UiTheme.Hud, l, lp.X, Gfx.Mid(lp.Y, ls), ls, UiTheme.Text);
        }
        // the crosshair at the centre
        view.DrawRect(new Rect2(o.X - 6 * k, o.Y - 1 * k, 12 * k, 2 * k), UiTheme.Text);
        view.DrawRect(new Rect2(o.X - 1 * k, o.Y - 6 * k, 2 * k, 12 * k), UiTheme.Text);

        float sx = stack ? c.Position.X : box.End.X + 20 * k, sy = stack ? box.End.Y + 10 * k : c.Position.Y + 4 * k, sw = stack ? c.Size.X : c.End.X - sx;
        void Key(Color col, string text)
        {
            view.DrawRect(new Rect2(sx, sy + ls * 0.1f, 12 * k, 12 * k), col);
            Gfx.TextFit(view, UiTheme.Body, text, sx + 20 * k, sy + ls, ls, UiTheme.Dim, sw - 20 * k);
            sy += ls + 12 * k;
        }
        Key(OverCol, "mostly overshoot");
        Key(ShortCol, "mostly short");
        Key(OkCol, "about right");
        sy += 4 * k;
        Gfx.TextFit(view, UiTheme.Body, "length = how many flicks", sx, sy + ls, ls, UiTheme.Faint, sw);
        sy += ls + 8 * k;
        Gfx.TextFit(view, UiTheme.Body, "% = past (+) or short (−)", sx, sy + ls, ls, UiTheme.Faint, sw);
        sy += ls + 18 * k;

        // the worst direction, in words
        int worst = -1;
        float worstOff = 0.08f;
        for (int s = 0; s < 8; s++)
        {
            float g = I.SectorGain[s];
            if (I.SectorN[s] < 2 || float.IsNaN(g)) continue;
            if (Mathf.Abs(g - 1f) > worstOff) { worstOff = Mathf.Abs(g - 1f); worst = s; }
        }
        string summary = worst < 0
            ? (I.SectorN.Sum() < 6 ? "Not many flicks yet — play longer for a clearer picture." : "Your flicks land about right in every direction.")
            : $"Flicks {RewindData.SectorName(worst)} {(I.SectorGain[worst] > 1 ? "overshoot" : "fall short")} the most ({(I.SectorGain[worst] - 1f) * 100f:+0;-0}%, {I.SectorN[worst]} flicks).";
        if (sy + bs < c.End.Y) Para(summary, sx, sy, sw, bs, UiTheme.Text, 4);
    }

    static readonly Color OverCol = new(1f, 0.42f, 0.4f), ShortCol = new(0.35f, 0.75f, 1f), OkCol = new(0.36f, 0.86f, 0.55f);

    static Color SectorColor(RunInsights I, int s)
    {
        int n = Math.Max(1, I.SectorN[s]);
        float over = (float)I.SectorOver[s] / n, under = (float)I.SectorUnder[s] / n;
        float g = I.SectorGain[s];
        if (!float.IsNaN(g) && I.SectorOver[s] + I.SectorUnder[s] == 0) return g > 1.08f ? OverCol : g < 0.92f ? ShortCol : OkCol;
        if (over > under + 0.15f) return OverCol;
        if (under > over + 0.15f) return ShortCol;
        return OkCol;
    }

    void Wedge(Vector2 o, float a, float len, Color c, bool current)
    {
        const int seg = 8;
        float half = Mathf.DegToRad(19f);
        Span<Vector2> p = stackalloc Vector2[seg + 2];
        p[0] = o;
        for (int i = 0; i <= seg; i++)
        {
            float aa = a - half + 2 * half * i / seg;
            p[i + 1] = o + new Vector2(Mathf.Cos(aa), -Mathf.Sin(aa)) * len;
        }
        view.FillPoly(p, new Color(c, current ? 0.95f : 0.7f));
        if (current)
        {
            Span<Vector2> q = stackalloc Vector2[seg + 3];
            p.CopyTo(q);
            q[seg + 2] = o;
            view.Polyline(q, UiTheme.Text, Mathf.Max(1, 1.5f * k), true);
        }
    }

    /// <summary>Word-wrapped paragraph (at most <paramref name="maxLines"/> lines).</summary>
    void Para(string text, float x, float y, float w, int fs, Color c, int maxLines)
    {
        var line = "";
        int n = 0;
        foreach (var word in text.Split(' '))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (Gfx.TextW(UiTheme.Body, tryLine, fs) > w && line.Length > 0)
            {
                Gfx.Text(view, UiTheme.Body, line, x, y + fs, fs, c);
                y += fs * 1.35f;
                if (++n >= maxLines) return;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) Gfx.Text(view, UiTheme.Body, line, x, y + fs, fs, c);
    }
}
