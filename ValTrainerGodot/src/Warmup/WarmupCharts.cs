using System.Globalization;
using Godot;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>The Lock-In's two charts, drawn with Godot 2D in the VALORANT UI style: the phase timeline (setup screen)
/// and the lock-in graph: each drill vs your usual, adaptive drills as the level held, the sens only if it was shifted.</summary>
public static class WarmupCharts
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly Color ShiftCol = UiTheme.Warn;
    public static readonly Color RealCol = UiTheme.Teal;

    public static Color CatColor(string mode) => ModeCard.CategoryColor(WarmupDrills.Category(mode));

    static string Sens(float s) => s.ToString("0.000", Inv);

    /// <summary>A shape that says the direction without colour (colour-blind safe): ▲ up (dir &gt; 0), ▼ down (dir &lt; 0),
    /// "=" for about the same (0). Drawn, not typed: the HUD fonts (Bahnschrift) have no ▲▼ glyphs.
    /// <paramref name="s"/> is the half-width.</summary>
    public static void TrendMark(CanvasItem c, Vector2 at, float s, int dir, Color col)
    {
        if (dir == 0)
        {
            float t = Mathf.Max(1, s * 0.3f); // "=": about the same
            c.DrawRect(new Rect2(at.X - s, at.Y - s * 0.45f - t / 2, s * 2, t), col);
            c.DrawRect(new Rect2(at.X - s, at.Y + s * 0.45f - t / 2, s * 2, t), col);
            return;
        }
        float h = s * 0.95f, d = dir > 0 ? -1 : 1;
        Span<Vector2> p = stackalloc Vector2[3];
        p[0] = new(at.X, at.Y + d * h);
        p[1] = new(at.X + s, at.Y - d * h);
        p[2] = new(at.X - s, at.Y - d * h);
        c.FillPoly(p, col);
    }

    /// <summary>The <see cref="WarmupScore.VerdictDir"/> shape in front of a verdict headline whose text's vertical middle
    /// is at <paramref name="mid"/>.Y. Returns the x where the headline starts (0 when there's nothing to compare).</summary>
    public static float VerdictMark(CanvasItem c, float readiness, Vector2 mid, float k, Color col)
    {
        int dir = WarmupScore.VerdictDir(readiness);
        if (dir == -2) return mid.X;
        TrendMark(c, new Vector2(mid.X + 9 * k, mid.Y), 9 * k, dir, col);
        return mid.X + 28 * k;
    }

    static void Dashed(CanvasItem c, Vector2 a, Vector2 b, Color col, float w, float dash)
    {
        float len = a.DistanceTo(b);
        if (len < 1) return;
        var dir = (b - a) / len;
        for (float t = 0; t < len; t += dash * 2) c.DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + dash), col, w);
    }

    // =====================================================================================
    // Timeline: phases on top, drills below (setup screen)
    // =====================================================================================

    public static Color PhaseColor(Phase p) => p switch
    {
        Phase.Activation => UiTheme.Teal,
        Phase.Calibration => new Color(0.45f, 0.66f, 0.98f),
        Phase.Mechanics => UiTheme.Warn,
        Phase.Deathmatch => UiTheme.Accent,
        _ => UiTheme.Dim,
    };

    public static void DrawTimeline(CanvasItem c, Rect2 r, WarmupPlan plan, float k)
    {
        if (plan.Steps.Count == 0) return;
        int ns = UiTheme.Fs(13, k), bs = UiTheme.Fs(12, k), ls = UiTheme.Fs(11, k);
        float fw = 110 * k, gap = 6 * k;                          // check-in and lock-in: fixed-width ends
        float left = r.Position.X, right = r.End.X;
        float x0 = left + fw + gap, x1 = right - fw - gap;
        float total = plan.PlaySeconds;
        float X(float t) => x0 + t / total * (x1 - x0);
        float phaseH = 70 * k, drillH = Mathf.Clamp(r.Size.Y - phaseH - 12 * k - 90 * k, 70 * k, 150 * k);
        float phaseY = r.Position.Y + 6 * k;
        float drillY = phaseY + phaseH + 12 * k;

        void PhaseBox(float xa, float xb, Phase p, string time)
        {
            var col = PhaseColor(p);
            var pr = new Rect2(xa, phaseY, xb - xa, phaseH);
            c.DrawRect(pr, new Color(col, 0.07f));
            c.DrawRect(new Rect2(pr.Position, new Vector2(pr.Size.X, 3 * k)), col);
            float tx = xa + 10 * k, tw = pr.Size.X - 16 * k;
            Gfx.TextFit(c, UiTheme.HudWide, Phases.Name(p), tx, phaseY + 26 * k, ns, UiTheme.Text, tw);
            Gfx.TextFit(c, UiTheme.Body, Phases.Blurb(p), tx, phaseY + 46 * k, bs, UiTheme.Dim, tw);
            Gfx.TextFit(c, UiTheme.Body, time, tx, phaseY + 63 * k, bs, UiTheme.Faint, tw);
        }

        // phases
        PhaseBox(left, left + fw, Phase.CheckIn, "30 s");
        float t0 = 0;
        foreach (var (p, secs, n) in plan.PhaseSpans())
        {
            PhaseBox(X(t0) + (t0 > 0 ? gap / 2 : 0), X(t0 + secs) - gap / 2, p, $"{WarmupPlan.Clock(secs)} · {n} drill{(n == 1 ? "" : "s")}");
            t0 += secs;
        }
        PhaseBox(right - fw, right, Phase.LockIn, "goal card");

        // drills
        t0 = 0;
        foreach (var s in plan.Steps)
        {
            var br = new Rect2(X(t0) + 1.5f * k, drillY, X(t0 + s.Seconds) - X(t0) - 3 * k, drillH);
            var pc = PhaseColor(s.Phase);
            c.DrawRect(br, new Color(0.07f, 0.11f, 0.15f, 0.92f));
            if (s.EaseLast > 0)
            {
                float ex = X(t0 + s.Seconds - s.EaseLast);
                var er = new Rect2(ex, br.Position.Y, br.End.X - ex, br.Size.Y);
                c.DrawRect(er, new Color(UiTheme.Good, 0.1f));
                c.DrawLine(new Vector2(ex, br.Position.Y), new Vector2(ex, br.End.Y), new Color(UiTheme.Good, 0.5f), 1);
                Gfx.TextFit(c, UiTheme.HudWide, "EASIER", ex + 2 * k, br.End.Y - 10 * k, ls, UiTheme.Good, er.Size.X - 4 * k, HorizontalAlignment.Center);
            }
            c.DrawRect(new Rect2(br.Position, new Vector2(br.Size.X, 3 * k)), s.Shifted ? ShiftCol : pc);
            c.DrawRect(br, new Color(UiTheme.Text, 0.1f), false, 1);
            float lw = s.EaseLast > 0 ? X(t0 + s.Seconds - s.EaseLast) - br.Position.X - 4 * k : br.Size.X - 4 * k;
            Gfx.TextFit(c, UiTheme.HudWide, s.Short, br.Position.X + 2 * k, br.Position.Y + 24 * k, ls, UiTheme.Text, lw, HorizontalAlignment.Center);
            Gfx.TextFit(c, UiTheme.Body, WarmupPlan.Clock(s.Seconds), br.Position.X + 2 * k, br.Position.Y + 42 * k, ls, UiTheme.Dim, lw, HorizontalAlignment.Center);
            string tag = s.Shifted ? $"{s.OffsetPct:+0;-0}%" : s.Adaptive ? "ADAPT" : s.Easier ? "EASY" : "";
            if (tag.Length > 0 && drillH > 58 * k)
                Gfx.TextFit(c, UiTheme.HudWide, tag, br.Position.X + 2 * k, br.Position.Y + 60 * k, ls, s.Shifted ? ShiftCol : pc, lw, HorizontalAlignment.Center);
            t0 += s.Seconds;
        }

        // ends: no drills
        foreach (var xa in new[] { left, right - fw })
        {
            var er = new Rect2(xa, drillY, fw, drillH);
            c.DrawRect(er, new Color(UiTheme.Text, 0.025f));
            c.DrawRect(er, new Color(UiTheme.Text, 0.06f), false, 1);
        }

        // time axis (minutes of play)
        float ay = drillY + drillH + 8 * k;
        int stepMin = total > 900 ? 5 : total > 420 ? 2 : 1;
        if (plan.Quick) stepMin = 0;
        if (stepMin > 0)
            for (int m = 0; m * 60 <= total + 1; m += stepMin)
            {
                float x = X(m * 60);
                c.DrawLine(new Vector2(x, ay - 4 * k), new Vector2(x, ay), new Color(UiTheme.Text, 0.3f), 1);
                Gfx.TextC(c, UiTheme.Body, m == 0 ? "0 min" : m.ToString(Inv), x, ay + 14 * k, ls, UiTheme.Faint);
            }

        // what the tags mean (honest: the player knows when the difficulty moves)
        float gy = ay + 50 * k, gx = left;
        int gs = UiTheme.Fs(13, k);
        foreach (var (tag, text, col) in new[]
        {
            ("EASY", "one tier below yours", PhaseColor(Phase.Activation)),
            ("ADAPT", "gets harder or easier to keep you near 80% hits", PhaseColor(Phase.Calibration)),
            ("EASIER", "the deathmatch's last minute has easier bots", UiTheme.Good),
        })
        {
            if (tag == "ADAPT" && !plan.Steps.Any(s => s.Adaptive)) continue;
            if (tag == "EASY" && !plan.Steps.Any(s => s.Easier)) continue;
            if (tag == "EASIER" && !plan.Steps.Any(s => s.EaseLast > 0)) continue;
            float tw = Gfx.TextW(UiTheme.HudWide, tag, ls);
            Gfx.Text(c, UiTheme.HudWide, tag, gx, gy, ls, col);
            float w2 = Gfx.TextW(UiTheme.Body, text, gs);
            Gfx.Text(c, UiTheme.Body, text, gx + tw + 8 * k, gy, gs, UiTheme.Dim);
            gx += tw + 8 * k + w2 + 30 * k;
        }
    }

    // =====================================================================================
    // Result: performance per step (bars, % of your usual) + sens overlay (right axis)
    // =====================================================================================

    public static void DrawResult(CanvasItem c, Rect2 r, WarmupRecord rec, float k)
    {
        var steps = rec.Steps;
        if (steps.Count == 0) return;
        int ls = UiTheme.Fs(12, k), ts = UiTheme.Fs(13, k), vs = UiTheme.Fs(16, k);
        float left = r.Position.X + 64 * k, right = r.End.X - 74 * k;
        float top = r.Position.Y + 58 * k, bottom = r.End.Y - 58 * k;
        float total = steps.Sum(s => Mathf.Max(1, s.Seconds));
        float X(float t) => left + t / total * (right - left);

        // performance axis: % of usual
        var idx = steps.Where(s => !s.Skipped && s.Index >= 0).Select(s => s.Index).ToList();
        float pLo = Mathf.Min(70, idx.Count > 0 ? idx.Min() - 10 : 70), pHi = Mathf.Max(130, idx.Count > 0 ? idx.Max() + 12 : 130);
        pLo = Mathf.Floor(pLo / 10) * 10; pHi = Mathf.Ceil(pHi / 10) * 10;
        float Y(float p) => bottom - (Mathf.Clamp(p, pLo, pHi) - pLo) / (pHi - pLo) * (bottom - top);

        // sens axis (right)
        var sensList = steps.Select(s => s.Sens).Where(s => s > 0).ToList();
        float baseS = rec.BaseSens > 0 ? rec.BaseSens : sensList.DefaultIfEmpty(1).Average();
        float sLo = Mathf.Min(baseS, sensList.DefaultIfEmpty(baseS).Min()), sHi = Mathf.Max(baseS, sensList.DefaultIfEmpty(baseS).Max());
        float sPad = Mathf.Max(baseS * 0.08f, (sHi - sLo) * 0.35f);
        sLo -= sPad; sHi += sPad;
        float S(float s) => bottom - (s - sLo) / Mathf.Max(1e-5f, sHi - sLo) * (bottom - top);

        // titles + legend
        Gfx.Text(c, UiTheme.HudWide, "PERFORMANCE PER DRILL · % OF YOUR USUAL", r.Position.X, r.Position.Y + 16 * k, ls, UiTheme.Dim);
        float lx = right + 70 * k;
        void Legend(string text, Color col, bool line)
        {
            float w = Gfx.TextW(UiTheme.HudWide, text, ls);
            Gfx.Text(c, UiTheme.HudWide, text, lx - w, r.Position.Y + 16 * k, ls, UiTheme.Dim);
            float sx = lx - w - 22 * k, sy = r.Position.Y + 11 * k;
            if (line) c.DrawLine(new Vector2(sx, sy), new Vector2(sx + 16 * k, sy), col, Mathf.Max(2, 3 * k));
            else c.DrawRect(new Rect2(sx + 3 * k, sy - 6 * k, 11 * k, 11 * k), col);
            lx = sx - 18 * k;
        }
        bool sensLine = steps.Any(s => s.Shifted);
        if (sensLine)
        {
            Legend("SENSITIVITY (RIGHT AXIS)", ShiftCol, true);
            Legend("YOUR SENS", RealCol, false);
            Legend("SHIFTED SENS", new Color(ShiftCol, 0.75f), false);
        }
        else
        {
            if (steps.Any(s => s.Adaptive)) Legend("ADAPTIVE: LEVEL HELD", PhaseColor(Phase.Calibration), false);
            Legend("% OF YOUR USUAL", RealCol, false);
        }

        // grid + left axis
        for (float p = pLo; p <= pHi + 0.1f; p += 10)
        {
            float y = Y(p);
            bool hundred = Mathf.Abs(p - 100) < 0.1f;
            if (hundred) continue;
            c.DrawLine(new Vector2(left, y), new Vector2(right, y), new Color(UiTheme.Text, 0.05f), 1);
            if (((int)p) % 20 == 0) Gfx.TextR(c, UiTheme.Body, $"{p:0}%", left - 10 * k, Gfx.Mid(y, ts), ts, UiTheme.Faint);
        }

        // shifted band(s)
        float t0 = 0;
        float bandA = -1, bandB = -1;
        foreach (var s in steps)
        {
            if (s.Shifted) { if (bandA < 0) bandA = t0; bandB = t0 + Mathf.Max(1, s.Seconds); }
            t0 += Mathf.Max(1, s.Seconds);
        }
        if (bandA >= 0)
        {
            c.DrawRect(new Rect2(X(bandA), top - 14 * k, X(bandB) - X(bandA), bottom - top + 14 * k), new Color(ShiftCol, 0.07f));
            var first = steps.First(s => s.Shifted);
            var last = steps.Last(s => s.Shifted);
            Gfx.TextFit(c, UiTheme.HudWide, $"SENS SHIFT {first.OffsetPct:+0;-0}% → {last.OffsetPct:+0;-0}%", X(bandA) + 8 * k, top - 22 * k + ls, ls,
                ShiftCol, X(bandB) - X(bandA) - 12 * k);
        }

        // 100% = usual
        float y100 = Y(100);
        c.DrawLine(new Vector2(left, y100), new Vector2(right, y100), new Color(UiTheme.Text, 0.45f), Mathf.Max(1, 1.5f * k));
        Gfx.TextR(c, UiTheme.HudWide, "USUAL", left - 10 * k, Gfx.Mid(y100, ls), ls, UiTheme.Text);

        // bars
        t0 = 0;
        var segs = new List<List<Vector2>> { new() }; // trend line pieces, broken at adaptive drills (no % of usual there)
        foreach (var s in steps)
        {
            float w = Mathf.Max(1, s.Seconds);
            float xa = X(t0), xb = X(t0 + w);
            float gap = Mathf.Min(6 * k, (xb - xa) * 0.18f);
            var col = s.Shifted ? ShiftCol : RealCol;
            float cx = (xa + xb) / 2;
            if (s.Skipped)
            {
                var sr = new Rect2(xa + gap, y100 - 2, xb - xa - 2 * gap, 4);
                Dashed(c, new Vector2(sr.Position.X, y100), new Vector2(sr.End.X, y100), new Color(UiTheme.Text, 0.3f), Mathf.Max(1, 2 * k), 4 * k);
                Gfx.TextC(c, UiTheme.HudWide, "SKIP", cx, y100 - 10 * k, UiTheme.Fs(10, k), UiTheme.Faint);
            }
            else if (s.Index >= 0)
            {
                float y = Y(s.Index);
                var bar = new Rect2(xa + gap, Mathf.Min(y, y100), xb - xa - 2 * gap, Mathf.Max(2, Mathf.Abs(y - y100)));
                bool hollow = s.BaselineKind == "today";
                if (hollow) c.DrawRect(bar, new Color(col, 0.85f), false, Mathf.Max(1, 1.5f * k));
                else
                {
                    Gfx.VGradient(c, bar, new Color(col, s.Index >= 100 ? 0.85f : 0.45f), new Color(col, s.Index >= 100 ? 0.45f : 0.75f));
                    c.DrawRect(new Rect2(bar.Position.X, y - 1.5f * k, bar.Size.X, 3 * k), col);
                }
                string lab = $"{s.Index:0}";
                float ly = s.Index >= 100 ? y - 8 * k : y + vs + 4 * k;
                if (xb - xa > 26 * k) Gfx.TextC(c, UiTheme.Display, lab, cx, ly, vs, s.Index >= 100 ? UiTheme.Text : UiTheme.Dim);
                segs[^1].Add(new Vector2(cx, y));
            }
            else if (s.Adaptive && s.Level >= 0)
            {
                // adaptive drills: the level held near 80% hits (their scores aren't comparable with normal runs)
                var ac = PhaseColor(Phase.Calibration);
                var box = new Rect2(xa + gap, top + 6 * k, xb - xa - 2 * gap, bottom - top - 12 * k);
                c.DrawRect(box, new Color(ac, 0.06f));
                c.DrawRect(box, new Color(ac, 0.35f), false, 1);
                if (segs[^1].Count > 0) segs.Add(new());
                float my = top + 44 * k;
                Gfx.Diamond(c, new Vector2(cx, my - 18 * k), 6 * k, 7 * k, ac);
                if (xb - xa > 26 * k)
                {
                    Gfx.TextC(c, UiTheme.HudWide, "LEVEL", cx, my + 2 * k, UiTheme.Fs(10, k), UiTheme.Dim);
                    Gfx.TextC(c, UiTheme.Display, s.Level.ToString("0.0", Inv), cx, my + 2 * k + vs + 4 * k, vs, UiTheme.Text);
                    if (s.HitRate >= 0) Gfx.TextC(c, UiTheme.Body, $"{s.HitRate * 100:0}% hits", cx, my + 2 * k + vs * 2 + 8 * k, UiTheme.Fs(11, k), UiTheme.Dim);
                }
            }
            else
            {
                Gfx.TextC(c, UiTheme.HudWide, "NO DATA", cx, y100 - 10 * k, UiTheme.Fs(10, k), UiTheme.Faint);
            }
            // drill label under the plot
            Gfx.TextFit(c, UiTheme.HudWide, WarmupDrills.Short(s.Mode), xa + 1, Gfx.Mid(bottom + 18 * k, ls), ls, s.Skipped ? UiTheme.Faint : UiTheme.Text,
                xb - xa - 2, HorizontalAlignment.Center);
            t0 += w;
        }

        // trend line through the bar tops
        foreach (var seg in segs.Where(s => s.Count >= 2)) c.DrawPolyline(seg.ToArray(), new Color(UiTheme.Text, 0.55f), Mathf.Max(1, 1.5f * k), true);
        foreach (var p in segs.SelectMany(s => s)) c.DrawCircle(p, 3 * k, UiTheme.Text);

        // time axis
        float ay = bottom + 34 * k;
        Gfx.Text(c, UiTheme.Body, "0:00", left, ay + 4 * k, ls, UiTheme.Faint);
        Gfx.TextR(c, UiTheme.Body, WarmupPlan.Clock(total), right, ay + 4 * k, ls, UiTheme.Faint);
        Gfx.TextC(c, UiTheme.HudWide, "LOCK-IN TIMELINE (PLAY TIME)", (left + right) / 2, ay + 4 * k, ls, UiTheme.Faint);
        c.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), new Color(UiTheme.Text, 0.25f), 1);
        if (!sensLine) return;

        // sens overlay (step line, right axis): only when the shifter was on
        t0 = 0;
        float? prev = null;
        foreach (var s in steps)
        {
            float w = Mathf.Max(1, s.Seconds);
            float xa = X(t0), xb = X(t0 + w), y = S(s.Sens > 0 ? s.Sens : baseS);
            if (prev is { } py && Mathf.Abs(py - y) > 0.5f) c.DrawLine(new Vector2(xa, py), new Vector2(xa, y), new Color(ShiftCol, 0.8f), Mathf.Max(1, 2 * k));
            c.DrawLine(new Vector2(xa, y), new Vector2(xb, y), new Color(ShiftCol, s.Shifted ? 1f : 0.55f), Mathf.Max(2, 3 * k));
            prev = y;
            t0 += w;
        }
        // right axis ticks: the real sens and the extremes
        var ticks = new List<float> { baseS };
        if (sensList.Count > 0) { ticks.Add(sensList.Max()); ticks.Add(sensList.Min()); }
        foreach (var tv in ticks.Distinct().Where(v => v > 0))
        {
            float y = S(tv);
            c.DrawLine(new Vector2(right, y), new Vector2(right + 5 * k, y), new Color(ShiftCol, 0.7f), 1);
            Gfx.Text(c, UiTheme.Body, Sens(tv), right + 9 * k, Gfx.Mid(y, ts), ts, Mathf.Abs(tv - baseS) < 1e-4f ? UiTheme.Text : ShiftCol);
        }
        Gfx.Text(c, UiTheme.HudWide, "SENS", right + 9 * k, top - 22 * k + ls, ls, ShiftCol);
    }
}
