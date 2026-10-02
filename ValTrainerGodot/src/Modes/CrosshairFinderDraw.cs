using Godot;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer.Modes;

/// <summary>Crosshair Finder HUD overlay: intro, part headers, the visibility test backdrops, rest / choice screens and
/// the result (big preview, real-size strip over map backgrounds, bracket, colour ranking and the VALORANT code).</summary>
public sealed partial class CrosshairFinderMode
{
    static readonly Color ColA = UiTheme.Teal, ColB = UiTheme.Warn;
    static float RealNow => Time.GetTicksMsec() / 1000f;
    static readonly Color PanelFill = new(0.06f, 0.1f, 0.14f, 0.97f);

    public override void Draw2D(CanvasItem c, Vector2 size)
    {
        if (cfg == null) return;
        float k = UiTheme.S(size);
        switch (phase)
        {
            case Ph.Intro: DrawIntro(c, size, k); break;
            case Ph.Header: DrawHeader(c, size, k); break;
            case Ph.VisWait or Ph.VisShow or Ph.VisFeedback: DrawVis(c, size, k); break;
            case Ph.VisResult: DrawVisResult(c, size, k); break;
            case Ph.Pick: DrawPick(c, size, k); break;
            case Ph.Rest: DrawRest(c, size, k); break;
            case Ph.Pref: DrawPref(c, size, k); break;
            case Ph.Summary: DrawSummary(c, size, k); break;
        }
        if (phase is Ph.VisWait or Ph.VisShow or Ph.VisFeedback)
            DrawProgress(c, size, k, visQueue.Count == 0 ? 0 : visIdx / (float)visQueue.Count);
        else if (phase is Ph.Rest or Ph.Trial or Ph.Pref)
            DrawProgress(c, size, k, totalBlocks == 0 ? 0 : doneBlocks / (float)totalBlocks);
    }

    static void Dim(CanvasItem c, Vector2 size, float a) => c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.05f, 0.08f, a));

    static void DrawProgress(CanvasItem c, Vector2 size, float k, float f)
    {
        float h = 5 * k;
        c.DrawRect(new Rect2(0, size.Y - h, size.X, h), new Color(0, 0, 0, 0.45f));
        c.DrawRect(new Rect2(0, size.Y - h, size.X * Mathf.Clamp(f, 0, 1), h), UiTheme.Accent);
    }

    /// <summary>The crosshair magnified ×<paramref name="zoom"/> (pixel-exact blocks) on a small plate.</summary>
    static void Chip(CanvasItem c, Rect2 r, CrosshairStyle st, float zoom, Color? bg = null, Color? border = null)
    {
        c.DrawRect(r, bg ?? Color.Color8(74, 84, 92));
        if (border is { } b) c.DrawRect(r, b, false, 2f);
        CrosshairView.DrawStyle(c, st, r.GetCenter().Round(), zoom);
    }

    /// <summary>Word-wrapped text; returns the baseline after the last line.</summary>
    static float Wrap(CanvasItem c, Font f, string text, float x, float y, int fs, Color col, float maxW, float lineH)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Gfx.TextW(f, tryLine, fs) > maxW)
            {
                Gfx.Text(c, f, line, x, y, fs, col);
                y += lineH;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) { Gfx.Text(c, f, line, x, y, fs, col); y += lineH; }
        return y;
    }

    static void Pulse(CanvasItem c, string text, float cx, float y, float k, int px = 20)
    {
        float a = 0.55f + 0.45f * Mathf.Sin(RealNow * 4f);
        Gfx.TextC(c, UiTheme.HudWide, text, cx, y, UiTheme.Fs(px, k), new Color(UiTheme.Accent, a));
    }

    static Rect2 Panel(CanvasItem c, Vector2 size, float k, float w, float h, float dy = 0)
    {
        float pw = Mathf.Min(size.X - 40 * k, w * k), ph = Mathf.Min(size.Y - 40 * k, h * k);
        var r = new Rect2(size.X / 2 - pw / 2, size.Y / 2 - ph / 2 + dy * k, pw, ph);
        Gfx.Plate(c, r, 22 * k, PanelFill, new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(r.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
        return r;
    }

    // =====================================================================================
    // Intro / headers
    // =====================================================================================

    void DrawIntro(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.8f);
        var r = Panel(c, size, k, 1120, 540, -20);
        float x = r.Position.X + 48 * k, y = r.Position.Y + 92 * k, w = r.Size.X - 96 * k - 230 * k;
        Gfx.Text(c, UiTheme.Display, "CROSSHAIR FINDER", x, y, UiTheme.Fs(64, k), UiTheme.Text);
        y += 36 * k;
        Gfx.Text(c, UiTheme.HudWide, $"VISIBILITY + SHAPE TEST · ABOUT {Mathf.Max(1, Mathf.RoundToInt(cfg.EstimatedMinutes))} MINUTES", x, y, UiTheme.Fs(14, k), UiTheme.Dim);
        y += 50 * k;
        int fs = UiTheme.Fs(19, k);
        string[] paras =
        {
            "Finds the crosshair you see and shoot best with, and gives you its VALORANT code.",
            "Part 1 · Visibility: click the moment a crosshair appears over map and enemy backgrounds.",
            "Part 2 · Shape: pick up to 4 shapes (dot, cross, closed plus, circle, hollow square…), then play short rounds with crosshair A and B. Flick to heads, fine-tune on far heads, then kill an agent. After each matchup, shoot the one that felt better (or SAME). Your aim decides; your taste breaks ties. The winner then faces your own crosshair.",
            "ValTrainer never changes VALORANT: you import the code yourself. Esc pauses; the round is then replayed.",
        };
        foreach (var l in paras) y = Wrap(c, UiTheme.Body, l, x, y, fs, UiTheme.Text, w, 30 * k) + 12 * k;

        // the player's crosshair now
        float cx = r.End.X - 48 * k - 100 * k;
        var chip = new Rect2(cx - 100 * k, r.Position.Y + 150 * k, 200 * k, 200 * k);
        Gfx.TextC(c, UiTheme.HudWide, "YOUR CROSSHAIR NOW", chip.GetCenter().X, chip.Position.Y - 14 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        Chip(c, chip, cur.Primary, Mathf.Max(2f, Mathf.Round(5 * k)));
        Gfx.TextFit(c, UiTheme.Body, cur.Name, chip.Position.X, chip.End.Y + 26 * k, UiTheme.Fs(15, k), UiTheme.Dim, chip.Size.X, HorizontalAlignment.Center);
        if (phaseT > 2f) Pulse(c, "CLICK TO START", r.GetCenter().X, r.End.Y - 34 * k, k);
    }

    void DrawHeader(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, part == 1 ? 0.96f : 0.7f);
        float cx = size.X / 2, cy = size.Y * 0.42f;
        Gfx.TextC(c, UiTheme.HudWide, part == 1 ? "PART 1 OF 2" : "PART 2 OF 2", cx, cy - 120 * k, UiTheme.Fs(18, k), UiTheme.Dim);
        Gfx.TextC(c, UiTheme.Display, part == 1 ? "VISIBILITY" : "SHAPE", cx, cy + 20 * k, UiTheme.Fs(130, k), UiTheme.Text);
        string a = part == 1 ? "Look at the centre of the screen. Click the moment the crosshair appears."
            : $"Your colour: {chosen.Name}. Now your {nPick} shapes, two at a time.";
        string b = part == 1 ? "Don't click early, don't guess. Each colour is tried on several backgrounds."
            : "Each round: flick to heads, fine-tune on far heads, kill the agent. Then pick the one that felt better.";
        Gfx.TextC(c, UiTheme.Body, a, cx, cy + 80 * k, UiTheme.Fs(22, k), UiTheme.Text);
        Gfx.TextC(c, UiTheme.Body, b, cx, cy + 116 * k, UiTheme.Fs(19, k), UiTheme.Dim);
        float bw = 360 * k, by = cy + 150 * k, left = Mathf.Max(0, cfg.PartHeaderSec - phaseT);
        c.DrawRect(new Rect2(cx - bw / 2, by, bw, 4 * k), new Color(1, 1, 1, 0.12f));
        c.DrawRect(new Rect2(cx - bw / 2, by, bw * (cfg.PartHeaderSec > 0 ? left / cfg.PartHeaderSec : 0), 4 * k), UiTheme.Accent);
    }

    // =====================================================================================
    // Part 1
    // =====================================================================================

    CrosshairStyle VisStyle(XfColor col) => XfShapes.SmallCross(col.Color, col.Outline).Xhair.Primary;

    void DrawVis(CanvasItem c, Vector2 size, float k)
    {
        if (visIdx >= visQueue.Count) return;
        var (cond, scene, seed) = visQueue[visIdx];
        XfScenes.Draw(c, size, scene, G.Enemy, seed);
        var mid = (size / 2).Floor();
        bool early = visFeedback.StartsWith("TOO");
        if (phase == Ph.VisShow || (phase == Ph.VisFeedback && !early)) CrosshairView.DrawStyle(c, VisStyle(colors[cond]), mid);
        if (phase == Ph.VisFeedback)
        {
            bool bad = early || visFeedback.StartsWith("MISSED");
            var plate = new Rect2(mid.X - 170 * k, mid.Y + 215 * k, 340 * k, 60 * k); // below the agent
            c.DrawRect(plate, new Color(0.03f, 0.05f, 0.08f, 0.8f));
            Gfx.TextC(c, UiTheme.Display, visFeedback, mid.X, Gfx.Mid(plate.GetCenter().Y, UiTheme.Fs(34, k)), UiTheme.Fs(34, k), bad ? UiTheme.Warn : UiTheme.Good);
        }
    }

    void DrawVisResult(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.9f);
        int n = colors.Count;
        var r = Panel(c, size, k, 1180, 210 + n * 96);
        float x0 = r.Position.X + 44 * k, x1 = r.End.X - 44 * k, y = r.Position.Y + 76 * k;
        Gfx.Text(c, UiTheme.HudWide, "PART 1 RESULT · MOST VISIBLE FOR YOU", x0, y - 26 * k, UiTheme.Fs(13, k), UiTheme.Dim);
        Gfx.TextFit(c, UiTheme.Display, chosen.Name.ToUpperInvariant(), x0, y + 26 * k, UiTheme.Fs(48, k), UiTheme.Text, x1 - x0);
        y += 60 * k;
        DrawColorRows(c, new Rect2(x0, y, x1 - x0, n * 96 * k), k, 96 * k);
    }

    /// <summary>One row per tested colour: zoomed chip, real-size chips over three backgrounds, spotting time, contrast.</summary>
    void DrawColorRows(CanvasItem c, Rect2 r, float k, float rowH, bool compact = false)
    {
        var list = colors.OrderByDescending(x => x.Score).ToList();
        float maxMs = Mathf.Max(400f, list.Where(x => !float.IsNaN(x.MedianMs)).Select(x => x.MedianMs).DefaultIfEmpty(400f).Max());
        float maxC = list.Max(x => x.Contrast);
        var maps = Maps.MapSpots.All;
        Color[] bgs = { maps[2].Palette.Wall, maps[3].Palette.Floor, XfVisibility.EnemyBody };
        if (!compact)
        {
            float cs = rowH - 22 * k;
            float lx = r.Position.X + cs + 10 * k + 3 * (cs * 0.8f + 4 * k) + 14 * k + 270 * k;
            Gfx.Text(c, UiTheme.HudWide, "TIME TO SPOT (SHORTER = BETTER) · CONTRAST (LONGER = BETTER)", lx, r.Position.Y - 4 * k, UiTheme.Fs(10, k), UiTheme.Faint);
            Gfx.Text(c, UiTheme.HudWide, "×3 · REAL SIZE ON HAVEN, SPLIT, AN AGENT", r.Position.X, r.Position.Y - 4 * k, UiTheme.Fs(10, k), UiTheme.Faint);
        }
        for (int i = 0; i < list.Count; i++)
        {
            var col = list[i];
            float y = r.Position.Y + i * rowH, cy = y + rowH / 2;
            bool pick = col == chosen;
            if (pick) c.DrawRect(new Rect2(r.Position.X - 10 * k, y + 4 * k, r.Size.X + 20 * k, rowH - 8 * k), new Color(UiTheme.Good, 0.08f));
            float chipS = rowH - 22 * k, x = r.Position.X;
            var st = VisStyle(col);
            Chip(c, new Rect2(x, cy - chipS / 2, chipS, chipS), st, compact ? 2 : 3);
            x += chipS + 10 * k;
            if (!compact)
                foreach (var bg in bgs)
                {
                    var rr = new Rect2(x, cy - chipS / 2, chipS * 0.8f, chipS);
                    c.DrawRect(rr, bg);
                    CrosshairView.DrawStyle(c, st, rr.GetCenter().Round());
                    x += chipS * 0.8f + 4 * k;
                }
            x += 14 * k;
            int ns = UiTheme.Fs(compact ? 15 : 20, k);
            Gfx.TextFit(c, UiTheme.Body, col.Name + (col.IsCurrent ? "  (yours)" : ""), x, cy - (compact ? 2 : 6) * k, ns, pick ? UiTheme.Text : UiTheme.Dim, 260 * k);
            if (pick) Gfx.Text(c, UiTheme.HudWide, "PICK", x, cy + 18 * k, UiTheme.Fs(11, k), UiTheme.Good);
            x += (compact ? 200 : 270) * k;
            float bw = r.End.X - x - (compact ? 60 : 110) * k;
            // spotting time (shorter = better) and computed contrast bars
            float ms = col.MedianMs;
            c.DrawRect(new Rect2(x, cy - 12 * k, bw, 8 * k), new Color(1, 1, 1, 0.06f));
            if (!float.IsNaN(ms)) c.DrawRect(new Rect2(x, cy - 12 * k, bw * Mathf.Clamp(ms / maxMs, 0.02f, 1), 8 * k), pick ? UiTheme.Good : UiTheme.Faint);
            c.DrawRect(new Rect2(x, cy + 4 * k, bw, 6 * k), new Color(1, 1, 1, 0.06f));
            c.DrawRect(new Rect2(x, cy + 4 * k, bw * Mathf.Clamp(col.Contrast / Mathf.Max(0.01f, maxC), 0.02f, 1), 6 * k), new Color(UiTheme.Teal, pick ? 0.9f : 0.45f));
            int vs = UiTheme.Fs(compact ? 13 : 16, k);
            Gfx.TextR(c, UiTheme.Hud, float.IsNaN(ms) ? "—" : $"{ms:0} ms", r.End.X, cy - 2 * k, vs, pick ? UiTheme.Text : UiTheme.Dim);
            if (!compact) Gfx.TextR(c, UiTheme.Body, col.Misses > 0 ? $"{col.Misses} missed" : "contrast", r.End.X, cy + 20 * k, UiTheme.Fs(12, k), UiTheme.Faint);
        }
    }

    // =====================================================================================
    // Shape pick
    // =====================================================================================

    void DrawPick(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.6f);
        float cx = size.X / 2;
        var top = new Rect2(cx - 560 * k, 76 * k, 1120 * k, 104 * k); // below the HUD's mode title and clock
        c.DrawRect(top, new Color(0.03f, 0.05f, 0.08f, 0.85f));
        Gfx.TextC(c, UiTheme.HudWide, $"PART 2 · SHAPE · YOUR COLOUR: {chosen.Name.ToUpperInvariant()}", cx, top.Position.Y + 24 * k, UiTheme.Fs(13, k), UiTheme.Dim);
        Gfx.TextC(c, UiTheme.Display, "PICK UP TO 4 SHAPES", cx, top.Position.Y + 66 * k, UiTheme.Fs(42, k), UiTheme.Text);
        Gfx.TextC(c, UiTheme.Body, "Shoot a card to pick or drop it, then shoot START. The 4 recommended shapes are already picked.", cx, top.Position.Y + 92 * k, UiTheme.Fs(17, k), UiTheme.Dim);
        float left = Mathf.Clamp((pickDeadline - phaseT) / Mathf.Max(0.1f, cfg.PickTimeout), 0, 1);
        c.DrawRect(new Rect2(top.Position.X, top.End.Y, top.Size.X * left, 3 * k), UiTheme.Accent);
        if (Now < noticeUntil) Gfx.TextC(c, UiTheme.Body, notice, cx, top.End.Y + 30 * k, UiTheme.Fs(19, k), UiTheme.Warn);

        var cam = c.GetViewport().GetCamera3D();
        if (cam == null) return;
        int hover = CardHit(G.View.Eye, G.View.Forward);
        for (int i = 0; i <= shapes.Count; i++)
        {
            var (cc, r, u) = CardQuad(i);
            var corners = new[] { cc - r - u, cc + r - u, cc + r + u, cc - r + u };
            if (corners.Any(cam.IsPositionBehind)) continue;
            var pts = corners.Select(p => cam.UnprojectPosition(p)).ToArray();
            var min = pts.Aggregate((a, b) => a.Min(b));
            var max = pts.Aggregate((a, b) => a.Max(b));
            var rect = new Rect2(min.Round(), (max - min).Round());
            if (i == StartCard) DrawStartCard(c, rect, k, hover == i);
            else DrawShapeCard(c, rect, k, i, hover == i);
        }
    }

    void DrawShapeCard(CanvasItem c, Rect2 r, float k, int i, bool hover)
    {
        var e = shapes[i];
        bool on = sel[i], rec = XfShapes.Defaults.Contains(e.Key);
        Gfx.Plate(c, r, 12 * k, new Color(0.06f, 0.1f, 0.14f, on ? 0.97f : 0.9f), new Color(UiTheme.Text, 0.14f));
        if (on) c.DrawRect(r.Grow(-1.5f * k), UiTheme.Good, false, 3 * k);
        if (hover) c.DrawRect(r.Grow(4 * k), UiTheme.Accent, false, 2 * k);
        float pad = 10 * k, x = r.Position.X + pad, w = r.Size.X - 2 * pad;
        Gfx.TextFit(c, UiTheme.Body, e.Name, x, r.Position.Y + 25 * k, UiTheme.Fs(17, k), on ? UiTheme.Text : UiTheme.Dim, w);

        // ×N zoom and real size (on a map wall)
        float s = Mathf.Max(20f, r.Size.Y - 96 * k), y = r.Position.Y + 34 * k;
        s = Mathf.Min(s, w * 0.62f);
        int zoom = Math.Max(2, (int)(s * 0.8f / 20f));
        var zr = new Rect2(x, y, s, s);
        Chip(c, zr, e.Xhair.Primary, zoom);
        Gfx.Text(c, UiTheme.HudWide, $"×{zoom}", zr.Position.X + 4 * k, zr.End.Y - 5 * k, UiTheme.Fs(10, k), new Color(UiTheme.Text, 0.6f));
        var rr = new Rect2(zr.End.X + 6 * k, y, w - s - 6 * k, s);
        c.DrawRect(rr, Maps.MapSpots.All[2].Palette.Wall);
        CrosshairView.DrawStyle(c, e.Xhair.Primary, rr.GetCenter().Round());
        Gfx.Text(c, UiTheme.HudWide, "1×", rr.Position.X + 4 * k, rr.End.Y - 5 * k, UiTheme.Fs(10, k), new Color(0, 0, 0, 0.6f));

        Wrap(c, UiTheme.Body, e.Why, x, zr.End.Y + 17 * k, UiTheme.Fs(12, k), UiTheme.Dim, w, 15 * k);
        float by = r.End.Y - 9 * k;
        Gfx.Text(c, UiTheme.HudWide, on ? "PICKED" : "SHOOT TO PICK", x, by, UiTheme.Fs(10, k), on ? UiTheme.Good : UiTheme.Faint);
        if (rec) Gfx.TextR(c, UiTheme.HudWide, "RECOMMENDED", r.End.X - pad, by, UiTheme.Fs(9, k), UiTheme.Faint);
    }

    void DrawStartCard(CanvasItem c, Rect2 r, float k, bool hover)
    {
        int n = PickedCount;
        Gfx.Plate(c, r, 12 * k, new Color(UiTheme.Accent, hover ? 0.75f : 0.5f), new Color(UiTheme.Accent, 0.9f), 2f);
        if (hover) c.DrawRect(r.Grow(4 * k), UiTheme.Text, false, 2 * k);
        float cy = r.GetCenter().Y;
        Gfx.TextC(c, UiTheme.Display, "START BRACKET", r.GetCenter().X, Gfx.Mid(cy - 9 * k, UiTheme.Fs(32, k)), UiTheme.Fs(32, k), UiTheme.Text);
        string sub = $"{n} SHAPE{(n == 1 ? "" : "S")} PICKED · STARTS BY ITSELF IN {Mathf.CeilToInt(Mathf.Max(0, pickDeadline - phaseT))} S";
        Gfx.TextC(c, UiTheme.HudWide, sub, r.GetCenter().X, Gfx.Mid(cy + 20 * k, UiTheme.Fs(11, k)), UiTheme.Fs(11, k), new Color(UiTheme.Text, 0.85f));
    }

    // =====================================================================================
    // Part 2
    // =====================================================================================

    void DrawRest(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.62f);
        var m = match!;
        char label = block!.Label;
        var col = label == 'A' ? ColA : ColB;
        float cx = size.X / 2, cy = size.Y * 0.36f;
        Gfx.TextC(c, UiTheme.HudWide, $"{m.Title} · ROUND {blockIdx + 1} / {m.Order.Length}", cx, cy - 130 * k, UiTheme.Fs(18, k), UiTheme.Dim);
        Gfx.TextC(c, UiTheme.Display, $"CROSSHAIR {label}", cx, cy + 20 * k, UiTheme.Fs(120, k), col);
        var chip = new Rect2(cx - 70 * k, cy + 50 * k, 140 * k, 140 * k);
        Chip(c, chip, EntrantOf(block.Cond).Xhair.Primary, Mathf.Max(2f, Mathf.Round(4 * k)), null, new Color(col, 0.8f));
        Gfx.TextC(c, UiTheme.Body, $"{cfg.Flicks} flicks, {cfg.Micros} far heads, then kill the agent. Look around to get a feel for it.", cx, chip.End.Y + 40 * k, UiTheme.Fs(19, k), UiTheme.Dim);
        float left = Mathf.Max(0, cfg.RestSec - phaseT), bw = 360 * k, by = chip.End.Y + 62 * k;
        c.DrawRect(new Rect2(cx - bw / 2, by, bw, 4 * k), new Color(1, 1, 1, 0.12f));
        c.DrawRect(new Rect2(cx - bw / 2, by, bw * (cfg.RestSec > 0 ? left / cfg.RestSec : 0), 4 * k), col);
        Gfx.TextC(c, UiTheme.Hud, $"{Mathf.CeilToInt(left)}", cx, by + 40 * k, UiTheme.Fs(28, k), UiTheme.Text);
        if (Now < noticeUntil) Gfx.TextC(c, UiTheme.Body, notice, cx, by + 80 * k, UiTheme.Fs(18, k), UiTheme.Warn);
    }

    void DrawPref(CanvasItem c, Vector2 size, float k)
    {
        var m = match!;
        float cx = size.X / 2;
        var top = new Rect2(cx - 430 * k, 96 * k, 860 * k, 92 * k);
        c.DrawRect(top, new Color(0.03f, 0.05f, 0.08f, 0.78f));
        Gfx.TextC(c, UiTheme.Display, "WHICH FELT BETTER?", cx, top.Position.Y + 50 * k, UiTheme.Fs(44, k), UiTheme.Text);
        Gfx.TextC(c, UiTheme.Body, "Shoot A, SAME or B. Your aim decides; this breaks ties.", cx, top.Position.Y + 78 * k, UiTheme.Fs(17, k), UiTheme.Dim);
        float left = Mathf.Clamp(1 - phaseT / cfg.PrefTimeout, 0, 1);
        c.DrawRect(new Rect2(top.Position.X, top.End.Y, top.Size.X * left, 3 * k), UiTheme.Accent);

        var cam = c.GetViewport().GetCamera3D();
        if (cam == null) return;
        string[] names = { "A", "SAME", "B" };
        for (int i = 0; i < 3; i++)
        {
            var p = prefT[i].GlobalPosition;
            if (cam.IsPositionBehind(p)) continue;
            var sp = cam.UnprojectPosition(p + new Vector3(0, prefT[i].Radius, 0));
            var col = i == 0 ? ColA : i == 2 ? ColB : UiTheme.Text;
            Gfx.TextC(c, UiTheme.Display, names[i], sp.X, sp.Y - 14 * k, UiTheme.Fs(i == 1 ? 30 : 48, k), col);
            if (i == 1) continue;
            int cond = Array.IndexOf(m.Label, i == 0 ? 'A' : 'B');
            var chip = new Rect2(sp.X - 50 * k, sp.Y - 175 * k, 100 * k, 100 * k);
            Chip(c, chip, EntrantOf(cond).Xhair.Primary, Mathf.Max(2f, Mathf.Round(3 * k)), null, new Color(col, 0.8f));
        }
    }

    // =====================================================================================
    // Summary
    // =====================================================================================

    void DrawSummary(CanvasItem c, Vector2 size, float k)
    {
        if (result == null) return;
        Dim(c, size, 0.92f);
        float pw = Mathf.Min(size.X - 40 * k, 1560 * k), top = 96 * k, ph = Mathf.Max(420 * k, size.Y - 110 * k - top);
        var r = new Rect2(size.X / 2 - pw / 2, top, pw, ph);
        Gfx.Plate(c, r, 24 * k, PanelFill, new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(r.Position, new Vector2(60 * k, 3 * k)), UiTheme.Accent);
        float x0 = r.Position.X + 44 * k, x1 = r.End.X - 44 * k, y = r.Position.Y;

        int hs = UiTheme.Fs(13, k);
        string tagText = aborted ? "PROVISIONAL" : "CROSSHAIR FINDER RESULT";
        var tag = new Rect2(x0, y + 30 * k, Gfx.TextW(UiTheme.HudWide, tagText, hs) + 18 * k, 22 * k);
        c.DrawRect(tag, aborted ? UiTheme.Warn : UiTheme.Accent);
        Gfx.Text(c, UiTheme.HudWide, tagText, tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, aborted ? Colors.Black : UiTheme.Text);
        string title = keepCurrent ? "KEEP YOUR CROSSHAIR: IT HELD UP" : $"YOUR CROSSHAIR: {ResultName.ToUpperInvariant()}";
        Gfx.TextFit(c, UiTheme.Display, title, x0, y + 100 * k, UiTheme.Fs(52, k), UiTheme.Text, x1 - x0);
        c.DrawRect(new Rect2(x0, y + 122 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));

        float colTop = y + 146 * k, bottomH = 150 * k, colH = ph - 146 * k - bottomH;
        float wL = (x1 - x0) * 0.3f, wM = (x1 - x0) * 0.4f, gap = 30 * k;
        DrawBigPreview(c, new Rect2(x0, colTop, wL, colH), k);
        DrawBracket(c, new Rect2(x0 + wL + gap, colTop, wM - gap, colH), k);
        float xr = x0 + wL + wM + gap;
        Gfx.Text(c, UiTheme.HudWide, "PART 1 · HOW FAST YOU SPOTTED EACH COLOUR", xr, colTop + 12 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        float rowH = Mathf.Min(64 * k, (colH - 40 * k) / Math.Max(1, colors.Count));
        DrawColorRows(c, new Rect2(xr, colTop + 30 * k, x1 - xr, rowH * colors.Count), k, rowH, compact: true);

        // the code
        float by = r.End.Y - bottomH + 6 * k;
        c.DrawRect(new Rect2(x0, by - 8 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));
        Gfx.Text(c, UiTheme.HudWide, keepCurrent ? "YOUR CROSSHAIR AS A VALORANT CODE" : "VALORANT CROSSHAIR CODE", x0, by + 14 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        var codeBox = new Rect2(x0, by + 24 * k, x1 - x0, 46 * k);
        c.DrawRect(codeBox, new Color(0, 0, 0, 0.35f));
        Gfx.TextFit(c, UiTheme.Hud, code, codeBox.Position.X + 14 * k, Gfx.Mid(codeBox.GetCenter().Y, UiTheme.Fs(24, k)), UiTheme.Fs(24, k), UiTheme.Good, codeBox.Size.X - 28 * k);
        string note = aborted ? abortWhy
            : "VALORANT › Settings › Crosshair › Import Profile Code. Copy it with the button on the next screen; ValTrainer never changes VALORANT.";
        Gfx.TextFit(c, UiTheme.Body, note, x0, by + 98 * k, UiTheme.Fs(17, k), aborted ? UiTheme.Warn : UiTheme.Text, x1 - x0);
        if (phaseT > 3f) Pulse(c, "CLICK TO CONTINUE", r.GetCenter().X, r.End.Y - 14 * k, k, 16);
    }

    void DrawBigPreview(CanvasItem c, Rect2 r, float k)
    {
        var st = result!.Xhair.Primary;
        Gfx.Text(c, UiTheme.HudWide, "×8 ZOOM · THEN REAL SIZE ON EACH MAP", r.Position.X, r.Position.Y + 12 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        float s = Mathf.Min(r.Size.X, r.Size.Y - 150 * k);
        var big = new Rect2(r.Position.X, r.Position.Y + 26 * k, r.Size.X, s);
        Gfx.VGradient(c, big, Color.Color8(96, 112, 126), Color.Color8(60, 70, 80));
        CrosshairView.DrawStyle(c, st, big.GetCenter().Round(), Mathf.Max(3f, Mathf.Round(8 * k)));
        Gfx.TextFit(c, UiTheme.Body, result.IsCurrent ? cur.Name : $"{result.Name} · {result.Why}", big.Position.X + 10 * k, big.End.Y - 12 * k, UiTheme.Fs(14, k), UiTheme.Text, big.Size.X - 20 * k);

        // real size over the four maps and the enemy
        var maps = Maps.MapSpots.All;
        var swatches = new List<(string, Color)>();
        foreach (var m in maps) swatches.Add((m.Map.ToUpperInvariant(), m.Palette.Wall));
        swatches.Add(("ENEMY", G.Enemy));
        float sy = big.End.Y + 12 * k, sw = (r.Size.X - 4 * k * (swatches.Count - 1)) / swatches.Count, sh = Mathf.Min(90 * k, r.End.Y - sy - 22 * k);
        for (int i = 0; i < swatches.Count; i++)
        {
            var (name, col) = swatches[i];
            var rr = new Rect2(r.Position.X + i * (sw + 4 * k), sy, sw, sh);
            c.DrawRect(rr, col);
            if (name == "ENEMY")
            {
                // the crosshair on an agent's head (highlight outline)
                float hr = 7f;
                var hc = rr.GetCenter();
                c.DrawCircle(hc, hr + 2, G.Enemy);
                c.DrawRect(new Rect2(hc.X - hr * 1.6f, hc.Y + hr, hr * 3.2f, rr.End.Y - hc.Y - hr), XfVisibility.EnemyBody);
                c.DrawCircle(hc, hr, XfVisibility.EnemyBody);
            }
            CrosshairView.DrawStyle(c, st, rr.GetCenter().Round());
            Gfx.TextC(c, UiTheme.HudWide, name, rr.GetCenter().X, rr.End.Y + 16 * k, UiTheme.Fs(10, k), UiTheme.Faint);
        }
    }

    void DrawBracket(CanvasItem c, Rect2 r, float k)
    {
        Gfx.Text(c, UiTheme.HudWide, "PART 2 · HEAD-TO-HEAD (AIM SCORE, THEN YOUR PICK)", r.Position.X, r.Position.Y + 12 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        var rows = matches.Where(m => m.Result != null).ToList();
        if (rows.Count == 0) return;
        float top = r.Position.Y + 30 * k, rowH = Mathf.Min(120 * k, (r.End.Y - top) / Math.Max(4, rows.Count));
        for (int i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            float y = top + i * rowH, cy = y + rowH / 2;
            if (i % 2 == 0) c.DrawRect(new Rect2(r.Position.X, y, r.Size.X, rowH), new Color(1, 1, 1, 0.025f));
            var res = m.Result!;
            double t = res.T;
            int lead = t >= 0 ? 0 : 1;
            string aim = Math.Abs(t) < 0.3 ? "aim even" : $"aim {m.Label[lead]} +{Math.Abs(t):0.0}σ";
            string pick = m.Pref < 0 ? "pick same" : $"pick {m.Label[m.Pref]}";
            float rx = r.End.X - 8 * k, hy = y + 20 * k;
            Gfx.Text(c, UiTheme.HudWide, m.Title, r.Position.X + 8 * k, hy, UiTheme.Fs(11, k), UiTheme.Dim);
            float vw = Gfx.TextR(c, UiTheme.HudWide, m.DecidedBy.ToUpperInvariant(), rx, hy, UiTheme.Fs(11, k), m.DecidedBy == "performance" ? UiTheme.Good : UiTheme.Warn);
            Gfx.TextR(c, UiTheme.Body, $"{aim} · {pick} ·", rx - vw - 6 * k, hy, UiTheme.Fs(12, k), UiTheme.Faint);
            float chipS = Mathf.Min(rowH - 40 * k, 80 * k), x = r.Position.X + 8 * k, half = (r.Size.X - 16 * k) / 2;
            for (int cond = 0; cond < 2; cond++)
            {
                var e = entrants[m.Entrant[cond]];
                bool win = m.Winner == cond;
                var chip = new Rect2(x, y + 30 * k, chipS, chipS);
                Chip(c, chip, e.Xhair.Primary, Mathf.Max(2f, Mathf.Round(2 * k)), null, win ? UiTheme.Good : new Color(UiTheme.Text, 0.12f));
                float tx = chip.End.X + 8 * k, nw = half - chipS - 16 * k;
                Gfx.TextFit(c, UiTheme.Body, e.Name, tx, chip.GetCenter().Y, UiTheme.Fs(15, k), win ? UiTheme.Text : UiTheme.Faint, nw);
                Gfx.Text(c, UiTheme.HudWide, (win ? "WIN · " : "") + m.Label[cond], tx, chip.GetCenter().Y + 20 * k, UiTheme.Fs(11, k), m.Label[cond] == 'A' ? ColA : ColB);
                x += half;
            }
        }
        if (byeName.Length > 0)
            Gfx.TextFit(c, UiTheme.Body, $"{byeName} had a bye to the final (3 shapes picked)", r.Position.X + 8 * k, top + rows.Count * rowH + 22 * k, UiTheme.Fs(13, k), UiTheme.Faint, r.Size.X - 16 * k);
    }
}
