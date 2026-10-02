using Godot;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>Sens Finder HUD overlay: intro, "SETTING A/B" rest screens, progress, and the end-of-test result chart.</summary>
public sealed partial class SensFinderMode
{
    static readonly Color ColA = UiTheme.Teal, ColB = UiTheme.Warn;
    static float RealNow => Time.GetTicksMsec() / 1000f;

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (cfg == null) return;
        float k = UiTheme.S(size);
        switch (phase)
        {
            case Ph.Intro: DrawIntro(c, size, k); break;
            case Ph.Rest: DrawRest(c, size, k); break;
            case Ph.Summary: DrawSummary(c, size, k); break;
        }
        if (phase is Ph.Rest or Ph.Flick or Ph.TrackReady or Ph.Track) DrawProgress(c, size, k);
    }

    static void Dim(CanvasItem c, Vector2 size, float a) => c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.05f, 0.08f, a));

    void DrawProgress(CanvasItem c, Vector2 size, float k)
    {
        int total = (cfg.Steps + 1) * cfg.BlocksPerStage;
        float f = Mathf.Clamp(doneBlocks / (float)total, 0, 1);
        float h = 5 * k;
        c.DrawRect(new Rect2(0, size.Y - h, size.X, h), new Color(0, 0, 0, 0.45f));
        c.DrawRect(new Rect2(0, size.Y - h, size.X * f, h), UiTheme.Accent);
    }

    void DrawIntro(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.72f);
        float pw = Mathf.Min(size.X - 40 * k, 1040 * k), ph = 560 * k;
        var r = new Rect2(size.X / 2 - pw / 2, size.Y / 2 - ph / 2 - 30 * k, pw, ph);
        Gfx.Plate(c, r, 22 * k, new Color(0.06f, 0.1f, 0.14f, 0.96f), new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(r.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
        float x = r.Position.X + 48 * k, y = r.Position.Y + 92 * k, w = pw - 96 * k;
        Gfx.Text(c, UiTheme.Display, "SENS FINDER", x, y, UiTheme.Fs(64, k), UiTheme.Text);
        y += 36 * k;
        Gfx.Text(c, UiTheme.HudWide, $"BLIND A/B TEST · ABOUT {Mathf.Max(1, Mathf.RoundToInt(cfg.EstimatedMinutes))} MINUTES", x, y, UiTheme.Fs(14, k), UiTheme.Dim);
        y += 50 * k;
        int fs = UiTheme.Fs(21, k);
        string[] lines =
        {
            "Finds the sensitivity you actually aim best with, not the one that feels best.",
            $"You'll play short rounds with two hidden settings, A and B. Each round: {cfg.FlicksPerBlock} head flicks",
            $"(click the head), then {cfg.TrackSec:0} s of tracking (keep your crosshair on the strafing target).",
            "Don't try to work out which setting is which. Just aim as well as you can.",
            $"{cfg.Steps} steps of {cfg.BlocksPerStage} rounds narrow it down, then a final check against your current sens.",
            "The numbers are revealed at the end. Esc pauses; the current round is then replayed.",
        };
        foreach (var l in lines)
        {
            Gfx.TextFit(c, UiTheme.Body, l, x, y, fs, UiTheme.Text, w);
            y += 36 * k;
        }
        y += 14 * k;
        Gfx.Text(c, UiTheme.Body, $"Starting from your current sens {current:0.000} ({Cm(current):0.0} cm/360 at {dpi} DPI).", x, y, UiTheme.Fs(17, k), UiTheme.Dim);
        if (phaseT > 2f)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(RealNow * 4f);
            Gfx.TextC(c, UiTheme.HudWide, "CLICK TO START", r.GetCenter().X, r.End.Y - 34 * k, UiTheme.Fs(20, k), new Color(UiTheme.Accent, a));
        }
    }

    void DrawRest(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.6f);
        var st = stage!;
        char label = block!.Label;
        var col = label == 'A' ? ColA : ColB;
        float cx = size.X / 2, cy = size.Y * 0.42f;
        Gfx.TextC(c, UiTheme.HudWide, "NEXT ROUND", cx, cy - 150 * k, UiTheme.Fs(18, k), UiTheme.Dim);
        Gfx.TextC(c, UiTheme.Display, $"SETTING {label}", cx, cy + 40 * k, UiTheme.Fs(150, k), col);
        string sub = st.Confirm ? $"FINAL CHECK · ROUND {blockIdx + 1} / {st.Order.Length}"
            : $"STEP {st.Step + 1} / {cfg.Steps} · ROUND {blockIdx + 1} / {st.Order.Length}";
        Gfx.TextC(c, UiTheme.HudWide, sub, cx, cy + 100 * k, UiTheme.Fs(18, k), UiTheme.Text);
        Gfx.TextC(c, UiTheme.Body, $"{cfg.FlicksPerBlock} flicks, then {cfg.TrackSec:0} s tracking. Move your mouse to get a feel for it.", cx, cy + 140 * k, UiTheme.Fs(19, k), UiTheme.Dim);
        float left = Mathf.Max(0, cfg.RestSec - phaseT);
        float bw = 360 * k, by = cy + 178 * k;
        c.DrawRect(new Rect2(cx - bw / 2, by, bw, 4 * k), new Color(1, 1, 1, 0.12f));
        c.DrawRect(new Rect2(cx - bw / 2, by, bw * (cfg.RestSec > 0 ? left / cfg.RestSec : 0), 4 * k), col);
        Gfx.TextC(c, UiTheme.Hud, $"{Mathf.CeilToInt(left)}", cx, by + 44 * k, UiTheme.Fs(28, k), UiTheme.Text);
        if (Now < noticeUntil) Gfx.TextC(c, UiTheme.Body, notice, cx, by + 84 * k, UiTheme.Fs(18, k), UiTheme.Warn);
    }

    // =====================================================================================
    // Summary: the bisection funnel (L vs H per step on a log sens axis) + the recommendation
    // =====================================================================================

    void DrawSummary(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.9f);
        // between the HUD's top bar (≈ 90 px) and its health plate (bottom 104 px)
        float pw = Mathf.Min(size.X - 40 * k, 1500 * k), top = 100 * k, ph = Mathf.Max(400 * k, size.Y - 114 * k - top);
        var r = new Rect2(size.X / 2 - pw / 2, top, pw, ph);
        Gfx.Plate(c, r, 24 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(r.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
        float x0 = r.Position.X + 44 * k, x1 = r.End.X - 44 * k, y = r.Position.Y;

        // header
        int hs = UiTheme.Fs(13, k);
        string tagText = aborted ? "ABORTED" : "SENS FINDER RESULT";
        var tag = new Rect2(x0, y + 30 * k, Gfx.TextW(UiTheme.HudWide, tagText, hs) + 18 * k, 22 * k);
        c.DrawRect(tag, aborted ? UiTheme.Warn : UiTheme.Accent);
        Gfx.Text(c, UiTheme.HudWide, tagText, tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, aborted ? Colors.Black : UiTheme.Text);
        string title = aborted ? (KeepSens ? $"KEEP {current:0.000} FOR NOW" : $"PROVISIONAL {rec:0.000}: KEEP {current:0.000} FOR NOW")
            : KeepSens ? $"KEEP YOUR SENSITIVITY: {rec:0.000}"
            : $"SET VALORANT SENSITIVITY TO {rec:0.000}  (WAS {current:0.000})";
        Gfx.TextFit(c, UiTheme.Display, title, x0, y + 100 * k, UiTheme.Fs(52, k), UiTheme.Text, x1 - x0);
        c.DrawRect(new Rect2(x0, y + 122 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));

        // columns
        float split = x0 + (x1 - x0) * 0.62f;
        var chart = new Rect2(x0, y + 150 * k, split - x0 - 30 * k, ph - 150 * k - 140 * k);
        DrawFunnel(c, chart, k);
        DrawNumbers(c, new Rect2(split + 10 * k, y + 150 * k, x1 - split - 10 * k, chart.Size.Y), k);

        // notes
        float ny = r.End.Y - 100 * k;
        c.DrawRect(new Rect2(x0, ny - 22 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));
        int ns = UiTheme.Fs(18, k);
        string n1 = aborted ? abortWhy
            : "VALORANT isn't changed automatically: set it yourself in VALORANT › Settings › General › Mouse › Sensitivity: Aim.";
        Gfx.TextFit(c, UiTheme.Body, n1, x0, ny, ns, aborted ? UiTheme.Warn : UiTheme.Text, x1 - x0);
        string n2 = atLimit
            ? $"Every step pointed {(lastDir < 0 ? "lower" : "higher")}: your best may be beyond one run's reach (±50%). Use this for a few days, then run the finder again."
            : "Run the Sens Finder again on another day. If both runs agree within 10%, use the average of the two.";
        Gfx.TextFit(c, UiTheme.Body, n2, x0, ny + 30 * k, ns, atLimit ? UiTheme.Warn : UiTheme.Dim, x1 - x0);
        if (phaseT > 3f)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(RealNow * 4f);
            Gfx.TextC(c, UiTheme.HudWide, "CLICK TO CONTINUE", r.GetCenter().X, r.End.Y - 26 * k, UiTheme.Fs(18, k), new Color(UiTheme.Accent, a));
        }
    }

    void DrawNumbers(CanvasItem c, Rect2 r, float k)
    {
        float x = r.Position.X, y = r.Position.Y + 10 * k, w = r.Size.X;
        int ls = UiTheme.Fs(12, k), vs = UiTheme.Fs(34, k), ss = UiTheme.Fs(16, k);
        void Row(string label, string value, string sub, Color vc)
        {
            Gfx.Text(c, UiTheme.HudWide, label, x, y + 12 * k, ls, UiTheme.Dim);
            Gfx.Text(c, UiTheme.Display, value, x, y + 50 * k, vs, vc);
            Gfx.TextFit(c, UiTheme.Body, sub, x, y + 74 * k, ss, UiTheme.Dim, w);
            y += 92 * k;
        }
        Row("VALORANT SENSITIVITY", rec.ToString("0.000", Inv), $"was {current.ToString("0.000", Inv)}", aborted ? UiTheme.Warn : UiTheme.Text);
        Row("EDPI · CM/360", $"{rec * dpi:0} · {Cm(rec):0.0} cm", $"was {current * dpi:0} · {Cm(current):0.0} cm  ({dpi} DPI)", UiTheme.Text);
        Row("CHANGE", $"{ChangePct:+0.0;-0.0}%", KeepSens ? "within 2%: keep your sens" : ChangePct < 0 ? "lower sens (more arm)" : "higher sens (less arm)",
            KeepSens ? UiTheme.Good : UiTheme.Text);
        var cc = confidence == "High" ? UiTheme.Good : confidence == "Medium" ? UiTheme.Warn : UiTheme.Accent;
        Row("CONFIDENCE", confidence.ToUpperInvariant(), confidenceWhy, cc);
        float proCm = SfUnits.Cm360(240f / dpi, dpi);
        string cmp = rec * dpi > 240 * 1.05f ? "faster than the pro median" : rec * dpi < 240 / 1.05f ? "slower than the pro median" : "right at the pro median";
        Row("PRO MEDIAN", $"eDPI 240 · {proCm:0} cm", $"yours is {cmp} (pros: 150–400 eDPI)", UiTheme.Text);
    }

    void DrawFunnel(CanvasItem c, Rect2 r, float k)
    {
        var rows = stages.Where(s => s.Result != null).ToList();
        int ls = UiTheme.Fs(12, k), ts = UiTheme.Fs(15, k);
        Gfx.Text(c, UiTheme.HudWide, "WHAT WON EACH STEP (HIGHER SENS →)", r.Position.X, r.Position.Y + 12 * k, ls, UiTheme.Dim);
        if (rows.Count == 0) return;

        // log-sens axis over everything tested
        var all = rows.SelectMany(s => s.Sens).Append(current).Append(rec).ToList();
        float lo = Mathf.Log(all.Min()) - 0.08f, hi = Mathf.Log(all.Max()) + 0.08f;
        float labelW = 120 * k, verdictW = 170 * k;
        float ax0 = r.Position.X + labelW, ax1 = r.End.X - verdictW;
        float X(float sens) => ax0 + (Mathf.Log(sens) - lo) / (hi - lo) * (ax1 - ax0);
        float top = r.Position.Y + 56 * k;
        float rowH = Mathf.Min(78 * k, (r.End.Y - 60 * k - top) / Math.Max(1, rows.Count));
        float bottom = top + rowH * rows.Count;

        // reference lines: current (dashed) and recommended
        float xs = X(current), xr = X(rec);
        for (float yy = top - 6 * k; yy < bottom + 12 * k; yy += 12 * k)
            c.DrawLine(new Vector2(xs, yy), new Vector2(xs, Mathf.Min(bottom + 12 * k, yy + 6 * k)), new Color(UiTheme.Text, 0.35f), Mathf.Max(1, 1.5f * k));
        c.DrawLine(new Vector2(xr, top - 6 * k), new Vector2(xr, bottom + 12 * k), new Color(UiTheme.Accent, 0.9f), Mathf.Max(1, 2.5f * k));

        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            float cy = top + rowH * (i + 0.5f);
            if (i % 2 == 0) c.DrawRect(new Rect2(r.Position.X, cy - rowH / 2, r.Size.X, rowH), new Color(1, 1, 1, 0.025f));
            string name = s.Confirm ? "CHECK" : $"STEP {s.Step + 1} ±{(s.HalfWidth - 1) * 100:0}%";
            Gfx.Text(c, UiTheme.HudWide, name, r.Position.X + 8 * k, Gfx.Mid(cy, ls), ls, UiTheme.Text);
            float xa = X(s.Sens[0]), xb = X(s.Sens[1]);
            c.DrawLine(new Vector2(Mathf.Min(xa, xb), cy), new Vector2(Mathf.Max(xa, xb), cy), new Color(UiTheme.Text, 0.25f), Mathf.Max(1, 2 * k));
            float t = (float)s.Result!.T;
            int win = s.Confirm ? (t >= -0.5f ? 0 : 1) : s.Winner;
            if (!s.Confirm)
            {
                // centre tick and the move to the new centre
                float xc = X(s.Centre), xn = X(s.NewCentre);
                c.DrawLine(new Vector2(xc, cy - 9 * k), new Vector2(xc, cy + 9 * k), new Color(UiTheme.Text, 0.6f), Mathf.Max(1, 1.5f * k));
                if (Mathf.Abs(xn - xc) > 2)
                {
                    c.DrawLine(new Vector2(xc, cy + 14 * k), new Vector2(xn, cy + 14 * k), UiTheme.Accent, Mathf.Max(1, 2 * k));
                    float d = Mathf.Sign(xn - xc);
                    c.DrawLine(new Vector2(xn, cy + 14 * k), new Vector2(xn - d * 6 * k, cy + 10 * k), UiTheme.Accent, Mathf.Max(1, 2 * k));
                    c.DrawLine(new Vector2(xn, cy + 14 * k), new Vector2(xn - d * 6 * k, cy + 18 * k), UiTheme.Accent, Mathf.Max(1, 2 * k));
                }
            }
            for (int cnd = 0; cnd < 2; cnd++)
            {
                var p = new Vector2(cnd == 0 ? xa : xb, cy);
                bool w = win == cnd;
                if (w) { c.DrawCircle(p, 9 * k, UiTheme.Good); c.DrawArc(p, 9 * k, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.8f), Mathf.Max(1, 1.5f * k), true); }
                else c.DrawArc(p, 6.5f * k, 0, Mathf.Tau, 24, win < 0 ? UiTheme.Warn : UiTheme.Faint, Mathf.Max(1, 2 * k), true);
                string lbl = s.Confirm ? (cnd == 0 ? "new" : "current") : s.Sens[cnd].ToString("0.00", Inv);
                Gfx.TextC(c, UiTheme.Body, lbl, p.X, cy - 13 * k, UiTheme.Fs(13, k), w ? UiTheme.Text : UiTheme.Dim);
            }
            string verdict = s.Confirm ? (win == 0 ? $"NEW HOLDS {t:+0.0;-0.0}σ" : $"SPLIT {t:+0.0;-0.0}σ")
                : win == 0 ? $"LOWER {t:+0.0;-0.0}σ" : win == 1 ? $"HIGHER {t:+0.0;-0.0}σ" : $"TIE {t:+0.0;-0.0}σ";
            var vc = win < 0 || (s.Confirm && win == 1) ? UiTheme.Warn : UiTheme.Good;
            Gfx.TextR(c, UiTheme.HudWide, verdict, r.End.X - 8 * k, Gfx.Mid(cy - 8 * k, ts), ts, vc);
            var res = s.Result;
            Gfx.TextR(c, UiTheme.Body, $"flicks {res.DFlick:+0.00;-0.00} · tracking {res.DTrack:+0.00;-0.00}", r.End.X - 8 * k, Gfx.Mid(cy + 12 * k, UiTheme.Fs(12, k)),
                UiTheme.Fs(12, k), UiTheme.Faint);
        }

        // axis: Valorant sens ticks (cm/360 below)
        float ay = bottom + 12 * k;
        c.DrawLine(new Vector2(ax0, ay), new Vector2(ax1, ay), new Color(UiTheme.Text, 0.3f), 1);
        for (int i = 0; i <= 4; i++)
        {
            float s = Mathf.Exp(lo + (hi - lo) * (0.1f + 0.8f * i / 4f));
            float xx = X(s);
            c.DrawLine(new Vector2(xx, ay), new Vector2(xx, ay + 5 * k), new Color(UiTheme.Text, 0.3f), 1);
            Gfx.TextC(c, UiTheme.Body, s.ToString("0.000", Inv), xx, ay + 22 * k, UiTheme.Fs(13, k), UiTheme.Dim);
            Gfx.TextC(c, UiTheme.Body, $"{Cm(s):0} cm", xx, ay + 40 * k, UiTheme.Fs(12, k), UiTheme.Faint);
        }
        Gfx.TextC(c, UiTheme.HudWide, "CURRENT", xs, top - 14 * k, UiTheme.Fs(11, k), UiTheme.Dim);
        Gfx.TextC(c, UiTheme.HudWide, "RECOMMENDED", xr, Mathf.Abs(xr - xs) < 90 * k ? top - 28 * k : top - 14 * k, UiTheme.Fs(11, k), UiTheme.Accent);
    }
}
