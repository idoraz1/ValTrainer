using System.Globalization;
using Godot;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>The warm-up's two charts, drawn with Godot 2D in the VALORANT UI style: the sens schedule (setup screen)
/// and the performance-over-the-warm-up graph with the sens overlaid (summary screen).</summary>
public static class WarmupCharts
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly Color ShiftCol = UiTheme.Warn;
    public static readonly Color RealCol = UiTheme.Teal;

    public static Color CatColor(string mode) => ModeCard.CategoryColor(WarmupDrills.Category(mode));

    static string Sens(float s) => s.ToString("0.000", Inv);

    static void Dashed(CanvasItem c, Vector2 a, Vector2 b, Color col, float w, float dash)
    {
        float len = a.DistanceTo(b);
        if (len < 1) return;
        var dir = (b - a) / len;
        for (float t = 0; t < len; t += dash * 2) c.DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + dash), col, w);
    }

    // =====================================================================================
    // Schedule: sens over the warm-up timeline + drill blocks
    // =====================================================================================

    public static void DrawSchedule(CanvasItem c, Rect2 r, WarmupPlan plan, float k)
    {
        if (plan.Steps.Count == 0) return;
        int ls = UiTheme.Fs(12, k), ts = UiTheme.Fs(13, k), vs = UiTheme.Fs(15, k);
        float total = plan.PlaySeconds;
        float left = r.Position.X + 84 * k, right = r.End.X - 12 * k;
        float blockH = 54 * k, axisH = 26 * k;
        float blocksY = r.End.Y - axisH - blockH;
        float top = r.Position.Y + 34 * k, bottom = blocksY - 22 * k;
        float X(float t) => left + t / total * (right - left);

        // sens range (log scale is overkill for ±35%)
        float baseS = plan.BaseSens > 0 ? plan.BaseSens : 1;
        float lo = plan.Steps.Min(s => s.Sens), hi = plan.Steps.Max(s => s.Sens);
        float pad = Mathf.Max(baseS * 0.06f, (hi - lo) * 0.18f);
        lo -= pad; hi += pad;
        float Y(float s) => bottom - (s - lo) / Mathf.Max(1e-5f, hi - lo) * (bottom - top);

        Gfx.Text(c, UiTheme.HudWide, "SENSITIVITY", r.Position.X, r.Position.Y + 14 * k, ls, UiTheme.Dim);
        string legend = plan.Shift == ShiftMode.Off ? "SENS SHIFTER OFF · EVERY DRILL AT YOUR SENS" : $"SENS SHIFTER {plan.ShiftLabel.ToUpperInvariant()}";
        Gfx.TextR(c, UiTheme.HudWide, legend, right, r.Position.Y + 14 * k, ls, plan.Shift == ShiftMode.Off ? UiTheme.Faint : ShiftCol);

        // shifted band
        float t0 = 0;
        float shiftEnd = 0;
        foreach (var s in plan.Steps) { if (s.Shifted) shiftEnd = t0 + s.Seconds; t0 += s.Seconds; }
        if (shiftEnd > 0)
        {
            c.DrawRect(new Rect2(X(0), top - 8 * k, X(shiftEnd) - X(0), blocksY + blockH - top + 8 * k), new Color(ShiftCol, 0.06f));
            c.DrawLine(new Vector2(X(shiftEnd), top - 8 * k), new Vector2(X(shiftEnd), blocksY + blockH), new Color(ShiftCol, 0.4f), Mathf.Max(1, k));
            Gfx.Text(c, UiTheme.HudWide, "BACK TO YOUR SENS", X(shiftEnd) + 8 * k, top + 4 * k, ls, new Color(RealCol, 0.9f));
        }

        // real sens reference
        float yb = Y(baseS);
        Dashed(c, new Vector2(left, yb), new Vector2(right, yb), new Color(UiTheme.Text, 0.3f), Mathf.Max(1, k), 6 * k);
        Gfx.TextR(c, UiTheme.Body, Sens(baseS), left - 10 * k, Gfx.Mid(yb, ts), ts, UiTheme.Text);
        Gfx.TextR(c, UiTheme.HudWide, "YOURS", left - 10 * k, Gfx.Mid(yb, ts) + 15 * k, UiTheme.Fs(10, k), UiTheme.Faint);

        // step line
        t0 = 0;
        float? prevY = null;
        float lastLabelX = -1e9f;
        foreach (var s in plan.Steps)
        {
            float xa = X(t0), xb = X(t0 + s.Seconds), y = Y(s.Sens);
            var col = s.Shifted ? ShiftCol : RealCol;
            if (prevY is { } py && Mathf.Abs(py - y) > 0.5f) c.DrawLine(new Vector2(xa, py), new Vector2(xa, y), new Color(UiTheme.Text, 0.5f), Mathf.Max(1, 1.5f * k));
            c.DrawLine(new Vector2(xa, y), new Vector2(xb, y), col, Mathf.Max(2, 3.5f * k));
            if (s.Shifted && xa - lastLabelX > 52 * k)
            {
                Gfx.TextC(c, UiTheme.Display, Sens(s.Sens), (xa + xb) / 2, y - 10 * k, vs, col);
                Gfx.TextC(c, UiTheme.Body, $"{s.OffsetPct:+0;-0}%", (xa + xb) / 2, y + 20 * k, UiTheme.Fs(12, k), UiTheme.Dim);
                lastLabelX = xa;
            }
            prevY = y;
            t0 += s.Seconds;
        }

        // drill blocks
        t0 = 0;
        foreach (var s in plan.Steps)
        {
            var br = new Rect2(X(t0) + 1.5f * k, blocksY, X(t0 + s.Seconds) - X(t0) - 3 * k, blockH);
            var cc = WarmupCharts.CatColor(s.Mode);
            c.DrawRect(br, new Color(0.07f, 0.11f, 0.15f, 0.92f));
            c.DrawRect(new Rect2(br.Position, new Vector2(br.Size.X, 3 * k)), s.Shifted ? ShiftCol : cc);
            c.DrawRect(br, new Color(UiTheme.Text, 0.1f), false, 1);
            Gfx.TextFit(c, UiTheme.HudWide, s.Short, br.Position.X + 2 * k, Gfx.Mid(br.Position.Y + 22 * k, ls), ls, UiTheme.Text, br.Size.X - 4 * k, HorizontalAlignment.Center);
            Gfx.TextFit(c, UiTheme.Body, WarmupPlan.Clock(s.Seconds), br.Position.X + 2 * k, Gfx.Mid(br.Position.Y + 40 * k, ls), ls, UiTheme.Dim, br.Size.X - 4 * k, HorizontalAlignment.Center);
            t0 += s.Seconds;
        }

        // time axis (minutes)
        float ay = r.End.Y - axisH + 6 * k;
        int stepMin = total > 900 ? 5 : total > 420 ? 2 : 1;
        if (plan.Quick) stepMin = 0;
        if (stepMin > 0)
            for (int m = 0; m * 60 <= total + 1; m += stepMin)
            {
                float x = X(m * 60);
                c.DrawLine(new Vector2(x, ay - 4 * k), new Vector2(x, ay), new Color(UiTheme.Text, 0.3f), 1);
                Gfx.TextC(c, UiTheme.Body, m == 0 ? "0 min" : m.ToString(Inv), x, ay + 14 * k, ls, UiTheme.Faint);
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
        Legend("SENSITIVITY (RIGHT AXIS)", ShiftCol, true);
        Legend("YOUR SENS", RealCol, false);
        if (steps.Any(s => s.Shifted)) Legend("SHIFTED SENS", new Color(ShiftCol, 0.75f), false);

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
        var pts = new List<Vector2>();
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
                pts.Add(new Vector2(cx, y));
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
        if (pts.Count >= 2) c.DrawPolyline(pts.ToArray(), new Color(UiTheme.Text, 0.55f), Mathf.Max(1, 1.5f * k), true);
        foreach (var p in pts) c.DrawCircle(p, 3 * k, UiTheme.Text);

        // sens overlay (step line, right axis)
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

        // time axis
        float ay = bottom + 34 * k;
        Gfx.Text(c, UiTheme.Body, "0:00", left, ay + 4 * k, ls, UiTheme.Faint);
        Gfx.TextR(c, UiTheme.Body, WarmupPlan.Clock(total), right, ay + 4 * k, ls, UiTheme.Faint);
        Gfx.TextC(c, UiTheme.HudWide, "WARM-UP TIMELINE (PLAY TIME)", (left + right) / 2, ay + 4 * k, ls, UiTheme.Faint);
        c.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), new Color(UiTheme.Text, 0.25f), 1);
    }
}
