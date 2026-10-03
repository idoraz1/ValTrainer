using System.Globalization;
using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Fx;
using ValTrainer.Maps;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// 2D drawing of the Smoke Execute drill (called from the HUD's Draw2D): the execute plan, the tactical-map placement
/// view, the in-world HUD (phase timer, abilities, star targeting, aim marker distance, throw previews) and the round
/// summary. The results replay (<see cref="ControllerReplay"/>) reuses <see cref="MapLayer"/>.
/// </summary>
static class ControllerDraw
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Color Open = new(1f, 0.32f, 0.36f);
    static readonly Color Cut = new(0.45f, 0.95f, 0.7f);
    static readonly Color PathCol = new(0.35f, 0.85f, 1f);
    static readonly Color Panel = new(0.04f, 0.07f, 0.1f, 0.9f);

    public static void Draw(CanvasItem ci, Vector2 size, SmokeExecuteMode m)
    {
        float k = UiTheme.S(size);
        switch (m.CurrentPhase)
        {
            case SmokeExecuteMode.Phase.Plan:
                PlanScreen(ci, size, k, m);
                break;
            case SmokeExecuteMode.Phase.Summary:
                if (m.Rounds.Count > 0) SummaryScreen(ci, size, k, m, m.Rounds[^1]);
                break;
            default:
                WorldHud(ci, size, k, m);
                var open = m.Abilities.FirstOrDefault(a => a.MapOpen);
                if (open != null) PlaceMap(ci, size, k, m, open);
                break;
        }
    }

    // =================================================================================================================
    // map layer (shared by plan, placement, summary and replay)
    // =================================================================================================================

    public sealed class Layer
    {
        public bool Labels = true, Targets = true, AllThreats, Sightlines, Standard, StandardNames = true, Route, Choke = true;
        public float RangeM;
        public Vector3? Player;
        public float PlayerYaw;
        public IReadOnlyList<SmokeVolume>? Smokes;
        public bool SmokesFull;
        public float Time;
        public Func<SmokeVolume, Color>? Tint;
        public IReadOnlyList<(EnemySpot Spot, bool Target, float KilledAt)>? Defs;
        public float DefsT = float.MaxValue;
        public IReadOnlyList<(float T, Vector3 P, float Yaw)>? Path;
        public float PathT = float.MaxValue;
    }

    public static void MapLayer(CanvasItem ci, float k, SmokeExecuteMode m, Layer o)
    {
        var map = m.TMap;
        var plan = m.Plan;
        map.DrawBase(ci);
        int ls = UiTheme.Fs(12, k);
        float px = map.Scale;

        if (o.Route && plan.Route.Count > 1) DashedPath(ci, plan.Route.Select(map.ToScreen).ToList(), new Color(1, 1, 1, 0.35f), Mathf.Max(1.5f, 2 * k), 7 * k);

        // smokes
        if (o.Smokes != null)
            foreach (var v in o.Smokes)
            {
                var tint = o.Tint?.Invoke(v) ?? new Color(0.8f, 0.8f, 0.8f);
                if (v.Shape == SmokeShape.Sphere)
                {
                    float r = (o.SmokesFull ? v.Radius : v.RadiusAt(o.Time)) * px;
                    if (r < 1f) continue;
                    var c = map.ToScreen(v.Center);
                    ci.DrawCircle(c, r, new Color(tint, 0.34f));
                    ci.DrawArc(c, r, 0, Mathf.Tau, 48, new Color(tint, 0.95f), Mathf.Max(1.5f, 2.2f * k), true);
                }
                else
                {
                    var pts = new List<Vector2>();
                    for (int i = 0; i < v.Points.Length; i++)
                        if (o.SmokesFull || v.ColumnScale(i, o.Time) > 0.1f) pts.Add(map.ToScreen(v.Points[i]));
                    if (pts.Count > 1)
                    {
                        ci.DrawPolyline(pts.ToArray(), new Color(tint, 0.45f), Mathf.Max(3f, 1.4f * px), true);
                        ci.DrawPolyline(pts.ToArray(), new Color(tint, 0.95f), Mathf.Max(1.5f, 2f * k), true);
                    }
                }
            }

        // standard smokes (dashed)
        if (o.Standard)
            for (int i = 0; i < m.Assigned.Count; i++)
            {
                var s = m.Assigned[i];
                var c = map.ToScreen(s.Ground);
                TacticalMap.DashedCircle(ci, c, ExecutePlan.PlanRadius * px, new Color(1, 1, 1, 0.8f), Mathf.Max(1.5f, 2f * k), 22);
                if (o.StandardNames) TacticalMap.Label(ci, $"{i + 1} {s.Name.ToUpperInvariant()}", c + new Vector2(0, -ExecutePlan.PlanRadius * px - 9 * k), ls, new Color(1, 1, 1, 0.9f));
            }

        // sight lines of the targeted defender spots onto the entry
        if (o.Sightlines)
        {
            var smokes = o.Smokes ?? Array.Empty<SmokeVolume>();
            foreach (var t in m.Targets)
            {
                var a = map.ToScreen(t.Spot.Feet);
                for (int j = 0; j < t.Seen.Count; j += 2)
                {
                    int i = t.Seen[j];
                    bool cut = smokes.Count > 0 && SmokeVolume.Blocks(smokes, t.Eye, plan.Entry[i], o.Time, o.SmokesFull);
                    ci.DrawLine(a, map.ToScreen(plan.Entry[i]), new Color(cut ? Cut : Open, cut ? 0.28f : 0.42f), Mathf.Max(1f, 1.3f * k), true);
                }
            }
        }

        // callouts
        if (o.Labels)
        {
            var done = new List<Vector3>();
            foreach (var e in m.Spot.Enemies)
            {
                if (done.Any(p => p.DistanceTo(e.Feet) < 3.5f)) continue;
                done.Add(e.Feet);
                bool target = m.Targets.Any(t => t.Spot == e);
                if (o.Targets && target) continue; // drawn bold below
                string name = e.Name.Replace(" (deep)", "").ToUpperInvariant();
                TacticalMap.Label(ci, name, map.ToScreen(e.Feet) + new Vector2(0, 12 * k), ls, new Color(0.85f, 0.9f, 0.95f, 0.55f));
            }
        }

        // threats
        if (o.AllThreats)
            foreach (var t in plan.Threats)
                if (!m.Targets.Contains(t)) ci.DrawCircle(map.ToScreen(t.Spot.Feet), 3.5f * k, new Color(Open, 0.55f));
        if (o.Targets && o.Defs == null)
            foreach (var t in m.Targets)
            {
                var c = map.ToScreen(t.Spot.Feet);
                ci.DrawCircle(c, 5.5f * k, Open);
                ci.DrawArc(c, 9 * k, 0, Mathf.Tau, 24, new Color(Open, 0.8f), Mathf.Max(1f, 1.5f * k), true);
                TacticalMap.Label(ci, t.Name.ToUpperInvariant(), c + new Vector2(0, 17 * k), UiTheme.Fs(13, k), new Color(1f, 0.75f, 0.77f));
            }

        // defenders of a played round
        if (o.Defs != null)
            foreach (var (spot, target, killed) in o.Defs)
            {
                var c = map.ToScreen(spot.Feet);
                bool dead = killed >= 0 && killed <= o.DefsT;
                if (dead)
                {
                    float r = 5 * k;
                    ci.DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), new Color(1, 1, 1, 0.85f), 2.2f * k, true);
                    ci.DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), new Color(1, 1, 1, 0.85f), 2.2f * k, true);
                }
                else ci.DrawCircle(c, 5.5f * k, Open);
                if (target) ci.DrawArc(c, 9 * k, 0, Mathf.Tau, 24, new Color(Open, 0.8f), Mathf.Max(1f, 1.5f * k), true);
                TacticalMap.Label(ci, spot.Name.ToUpperInvariant(), c + new Vector2(0, 17 * k), UiTheme.Fs(12, k), dead ? new Color(1, 1, 1, 0.6f) : new Color(1f, 0.75f, 0.77f));
            }

        if (o.Choke)
        {
            var c = map.ToScreen(m.Spot.Choke);
            Gfx.Diamond(ci, c, 6 * k, 6 * k, UiTheme.Warn);
            TacticalMap.Label(ci, "ENTRY", c + new Vector2(0, -13 * k), UiTheme.Fs(11, k), UiTheme.Warn);
        }

        // path of a played round
        if (o.Path != null && o.Path.Count > 1)
        {
            var pts = o.Path.Where(p => p.T <= o.PathT).Select(p => map.ToScreen(p.P)).ToArray();
            if (pts.Length > 1) ci.DrawPolyline(pts, new Color(PathCol, 0.9f), Mathf.Max(1.5f, 2.4f * k), true);
            var last = o.Path.LastOrDefault(p => p.T <= o.PathT);
            if (pts.Length > 0) TacticalMap.Arrow(ci, map.ToScreen(last.P), map.YawDir(last.Yaw), 9 * k, PathCol);
        }

        if (o.Player is { } pl)
        {
            var c = map.ToScreen(pl);
            if (o.RangeM > 0) ClippedCircle(ci, c, o.RangeM * px, map.Area, new Color(1, 1, 1, 0.5f), Mathf.Max(1f, 1.5f * k));
            TacticalMap.Arrow(ci, c, map.YawDir(o.PlayerYaw), 10 * k, PathCol);
        }
    }

    /// <summary>Circle outline drawn only where it lies inside <paramref name="clip"/>.</summary>
    static void ClippedCircle(CanvasItem ci, Vector2 c, float r, Rect2 clip, Color col, float w)
    {
        const int n = 160;
        var prev = c + new Vector2(r, 0);
        for (int i = 1; i <= n; i++)
        {
            float a = i * Mathf.Tau / n;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (clip.HasPoint(p) && clip.HasPoint(prev)) ci.DrawLine(prev, p, col, w, true);
            prev = p;
        }
    }

    static void DashedPath(CanvasItem ci, List<Vector2> pts, Color c, float w, float dash)
    {
        float acc = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            float len = a.DistanceTo(b);
            if (len < 1e-3f) continue;
            float s = 0f;
            while (s < len - 1e-3f)
            {
                float ph = (acc + s) % (2f * dash);
                bool on = ph < dash;
                float e = Mathf.Min(len, s + Mathf.Max(0.5f, on ? dash - ph : 2f * dash - ph)); // always advance (float rounding)
                if (on) ci.DrawLine(a.Lerp(b, s / len), a.Lerp(b, e / len), c, w, true);
                s = e;
            }
            acc += len;
        }
    }

    // =================================================================================================================
    // layouts
    // =================================================================================================================

    /// <summary>Map rect + info column for full-screen panels (plan, summary, replay).</summary>
    public static (Rect2 Map, Rect2 Info) Columns(Vector2 size, float k)
    {
        float top = 118 * k, bottom = size.Y - 118 * k;
        float total = Mathf.Min(size.X - 60 * k, 1820 * k);
        float left = (size.X - total) / 2;
        float mapW = total * 0.56f;
        return (new Rect2(left, top, mapW, bottom - top), new Rect2(left + mapW + 26 * k, top, total - mapW - 26 * k, bottom - top));
    }

    public static List<string> Wrap(Font f, string text, int fs, float maxW)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var line = "";
            foreach (var w in para.Split(' '))
            {
                var t = line.Length == 0 ? w : line + " " + w;
                if (Gfx.TextW(f, t, fs) > maxW && line.Length > 0) { lines.Add(line); line = w; }
                else line = t;
            }
            lines.Add(line);
        }
        return lines;
    }

    static float Para(CanvasItem ci, Font f, string text, float x, float y, int fs, float maxW, Color c, float lh = 1.32f)
    {
        foreach (var l in Wrap(f, text, fs, maxW)) { Gfx.Text(ci, f, l, x, y, fs, c); y += fs * lh; }
        return y;
    }

    static void Header(CanvasItem ci, float k, Rect2 info, string tag, string title, string sub)
    {
        int hs = UiTheme.Fs(13, k), ts = UiTheme.Fs(40, k), ss = UiTheme.Fs(15, k);
        float x = info.Position.X, y = info.Position.Y;
        var tr = new Rect2(x, y, Gfx.TextW(UiTheme.HudWide, tag, hs) + 18 * k, 22 * k);
        ci.DrawRect(tr, UiTheme.Accent);
        Gfx.Text(ci, UiTheme.HudWide, tag, x + 9 * k, Gfx.Mid(tr.GetCenter().Y, hs), hs, UiTheme.Text);
        Gfx.TextFit(ci, UiTheme.Display, title, x, y + 66 * k, ts, UiTheme.Text, info.Size.X);
        Gfx.TextFit(ci, UiTheme.HudWide, sub, x, y + 92 * k, ss, UiTheme.Dim, info.Size.X);
    }

    // =================================================================================================================
    // plan
    // =================================================================================================================

    static void PlanScreen(CanvasItem ci, Vector2 size, float k, SmokeExecuteMode m)
    {
        ci.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.035f, 0.05f, 0.72f));
        var (mr, info) = Columns(size, k);
        Gfx.Plate(ci, mr, 18 * k, Panel, new Color(UiTheme.Text, 0.12f));
        m.TMap.Fit(mr.Grow(-16 * k));
        var feet = m.Feet;
        MapLayer(ci, k, m, new Layer
        {
            Route = true, Sightlines = true, AllThreats = true, Standard = m.Ghosts, Player = feet, PlayerYaw = m.Game.View.Yaw,
        });

        Gfx.Plate(ci, info, 18 * k, Panel, new Color(UiTheme.Text, 0.12f));
        var inner = info.Grow(-26 * k);
        Header(ci, k, inner, $"ROUND {m.Current.Index} / 5", "EXECUTE PLAN", $"{m.Kit.Agent.ToUpperInvariant()} · {m.Spot.Map.ToUpperInvariant()} {m.Spot.Name.ToUpperInvariant()}");
        float x = inner.Position.X, y = inner.Position.Y + 136 * k, w = inner.Size.X;
        int bs = UiTheme.Fs(17, k), ss = UiTheme.Fs(13, k), ns = UiTheme.Fs(22, k);
        Gfx.Text(ci, UiTheme.HudWide, "CUT THESE SIGHTLINES", x, y, ss, UiTheme.Accent);
        y += 30 * k;
        for (int i = 0; i < m.Assigned.Count; i++)
        {
            var s = m.Assigned[i];
            Gfx.Text(ci, UiTheme.Display, $"{i + 1}  {s.Name.ToUpperInvariant()}", x, y, ns, UiTheme.Text);
            y += 24 * k;
            string covers = s.Covers.Count > 0 ? "blinds " + string.Join(", ", s.Covers.Select(c => c.Name)) : "cuts the angles onto the entry";
            y = Para(ci, UiTheme.Body, covers, x + 26 * k, y, bs, w - 26 * k, UiTheme.Dim) + 6 * k;
        }
        if (m.Kit.HasWall && m.Assigned.Count > 0)
            y = Para(ci, UiTheme.Body, $"Use your {m.Kit.WallAbility} for the angles the {(m.Kit.Placing == SmokePlacing.Throw ? "orb" : m.Kit.Ability)} doesn't cover.", x, y + 4 * k, bs, w, UiTheme.Dim) + 6 * k;
        if (m.Plan.Derived)
            y = Para(ci, UiTheme.Body, "No standard smokes are set for this map yet: the plan smokes the defender spots that see the most of your entry.", x, y + 4 * k, UiTheme.Fs(14, k), w, UiTheme.Faint) + 4 * k;
        y += 14 * k;
        Gfx.Text(ci, UiTheme.HudWide, "YOUR KIT", x, y, ss, UiTheme.Accent);
        y += 26 * k;
        y = Para(ci, UiTheme.Body, $"{m.Kit.Ability}: {m.Kit.Summary}", x, y, bs, w, UiTheme.Text) + 4 * k;
        y = Para(ci, UiTheme.Body, m.Kit.HowTo, x, y, UiTheme.Fs(15, k), w, UiTheme.Dim) + 10 * k;
        Gfx.Text(ci, UiTheme.HudWide, "LEGEND", x, y, ss, UiTheme.Accent);
        y += 24 * k;
        ci.DrawLine(new Vector2(x, y - 5 * k), new Vector2(x + 30 * k, y - 5 * k), Open, 2 * k);
        Gfx.Text(ci, UiTheme.Body, "defender sightline onto your entry", x + 40 * k, y, UiTheme.Fs(14, k), UiTheme.Dim);
        y += 22 * k;
        if (m.Ghosts)
        {
            TacticalMap.DashedCircle(ci, new Vector2(x + 15 * k, y - 5 * k), 8 * k, Colors.White, 1.5f * k, 10);
            Gfx.Text(ci, UiTheme.Body, "standard smoke (shown at Rookie / Regular)", x + 40 * k, y, UiTheme.Fs(14, k), UiTheme.Dim);
            y += 22 * k;
        }
        // countdown
        float left = Mathf.Max(0, m.PhaseLeft);
        var bar = new Rect2(x, inner.End.Y - 44 * k, w, 6 * k);
        ci.DrawRect(bar, new Color(1, 1, 1, 0.1f));
        ci.DrawRect(new Rect2(bar.Position, new Vector2(w * Mathf.Clamp(m.PhaseFrac, 0, 1), bar.Size.Y)), UiTheme.Accent);
        Gfx.Text(ci, UiTheme.HudWide, $"PLACEMENT STARTS IN {Mathf.CeilToInt(left)}  ·  CLICK / SPACE TO START NOW", x, inner.End.Y - 12 * k, UiTheme.Fs(14, k), UiTheme.Text);
    }

    // =================================================================================================================
    // placement map (Brimstone / Clove / Miks, Astral Form, Omen overhead)
    // =================================================================================================================

    static void PlaceMap(CanvasItem ci, Vector2 size, float k, SmokeExecuteMode m, SmokeAbility a)
    {
        ci.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.035f, 0.05f, 0.55f));
        float mw = Mathf.Min(size.X - 80 * k, 1240 * k), top = 150 * k, h = size.Y - top - 150 * k;
        var mr = new Rect2((size.X - mw) / 2, top, mw, h);
        Gfx.Plate(ci, mr, 18 * k, Panel, new Color(UiTheme.Text, 0.14f));
        m.TMap.Fit(mr.Grow(-12 * k));
        m.MapArea = m.TMap.Area;
        var map = m.TMap;
        bool astral = a is AstralAbility;
        MapLayer(ci, k, m, new Layer
        {
            Targets = false, Standard = m.Ghosts, Player = m.Feet, PlayerYaw = m.Game.View.Yaw,
            RangeM = astral ? 0f : m.Kit.Range, Smokes = m.Game.Smokes, Time = m.Clock, Tint = _ => m.Kit.Look.Light,
        });
        float px = map.Scale;
        // Astra's stars
        foreach (var s in m.Stars) Star(ci, map.ToScreen(s.Ground), 8 * k, s.Used ? new Color(m.Kit.Look.Rim, 0.35f) : m.Kit.Look.Rim);

        string title, help;
        if (a is MapDropAbility md)
        {
            for (int i = 0; i < md.Marks.Count; i++)
            {
                var c = map.ToScreen(md.Marks[i]);
                ci.DrawCircle(c, m.Kit.Radius * px, new Color(m.Kit.Look.Light, 0.22f));
                ci.DrawArc(c, m.Kit.Radius * px, 0, Mathf.Tau, 48, m.Kit.Look.Rim, Mathf.Max(2f, 2.5f * k), true);
                TacticalMap.Label(ci, (i + 1).ToString(Inv), c, UiTheme.Fs(16, k), UiTheme.Text, UiTheme.Display);
            }
            title = $"{m.Kit.Ability.ToUpperInvariant()}  ·  {md.Charges - md.Marks.Count} OF {md.Charges} LEFT TO MARK";
            help = "CLICK mark / remove   ·   RIGHT-CLICK launch   ·   " + Key(m.Kit.SmokeKey) + " put away";
        }
        else if (astral)
        {
            title = $"ASTRAL FORM  ·  {m.StarsLeft} STAR{(m.StarsLeft == 1 ? "" : "S")} LEFT";
            help = "CLICK place / take back a star   ·   RIGHT-CLICK or X return   ·   then look at a star + E = Nebula";
        }
        else if (a is AimAbility aim)
        {
            var c = map.ToScreen(aim.Landing);
            ci.DrawLine(map.ToScreen(m.Feet), c, new Color(0.5f, 0.75f, 1f, 0.6f), 1.5f * k, true);
            ci.DrawCircle(c, m.Kit.Radius * px, new Color(0.45f, 0.7f, 1f, 0.2f));
            ci.DrawArc(c, m.Kit.Radius * px, 0, Mathf.Tau, 48, new Color(0.55f, 0.8f, 1f), 2.5f * k, true);
            title = $"{m.Kit.Ability.ToUpperInvariant()}  ·  OVERHEAD  ·  {aim.Distance:0} M";
            help = "AIM moves the marker   ·   HOLD CLICK out / RIGHT-CLICK in   ·   " + Key(m.Kit.SmokeKey) + " throw   ·   R normal view";
        }
        else { title = a.Name.ToUpperInvariant(); help = ""; }

        // cursor
        if (a is MapAbility ma)
        {
            ma.EnsureCursor(map);
            var cur = ma.Cursor;
            var spot = map.FloorAt(cur, 1.0f);
            bool inRange = spot is { } p && (m.Kit.Range <= 0 || new Vector2(p.X - m.Feet.X, p.Z - m.Feet.Z).Length() <= m.Kit.Range);
            var col = spot != null && inRange ? new Color(1, 1, 1, 0.95f) : new Color(1f, 0.35f, 0.35f, 0.95f);
            float r = (astral ? 0.9f : m.Kit.Radius) * px;
            ci.DrawArc(cur, r, 0, Mathf.Tau, 40, col, Mathf.Max(1.5f, 2f * k), true);
            ci.DrawLine(cur + new Vector2(-12 * k, 0), cur + new Vector2(12 * k, 0), col, 1.5f * k);
            ci.DrawLine(cur + new Vector2(0, -12 * k), cur + new Vector2(0, 12 * k), col, 1.5f * k);
            if (spot is { } sp)
                TacticalMap.Label(ci, $"{new Vector2(sp.X - m.Feet.X, sp.Z - m.Feet.Z).Length():0} M{(sp.Y - m.Feet.Y > 1.5f ? " · HIGH" : "")}", cur + new Vector2(0, r + 12 * k), UiTheme.Fs(12, k), col);
        }

        int ts = UiTheme.Fs(26, k), hs = UiTheme.Fs(15, k);
        Gfx.TextC(ci, UiTheme.Display, title, size.X / 2, mr.Position.Y - 16 * k, ts, UiTheme.Text);
        Gfx.TextC(ci, UiTheme.HudWide, help, size.X / 2, mr.End.Y + 30 * k, hs, UiTheme.Dim);
        if (m.CurrentPhase == SmokeExecuteMode.Phase.Place)
            Gfx.TextC(ci, UiTheme.HudWide, $"PLACEMENT TIME {Mathf.Max(0, m.PhaseLeft):0.0} S", size.X / 2, mr.End.Y + 54 * k, hs, m.PhaseLeft < 3 ? UiTheme.Accent : UiTheme.Warn);
    }

    static string Key(Godot.Key key) => OS.GetKeycodeString(key).ToUpperInvariant();

    static void Star(CanvasItem ci, Vector2 c, float r, Color col)
    {
        if (!c.IsFinite() || r < 1f) return;
        Span<Vector2> p = stackalloc Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        ci.FillPoly(p, col);
    }

    // =================================================================================================================
    // in-world HUD
    // =================================================================================================================

    static bool Project(CanvasItem ci, Vector3 p, out Vector2 s)
    {
        s = default;
        var cam = ci.GetViewport()?.GetCamera3D();
        if (cam == null || cam.IsPositionBehind(p)) return false;
        s = cam.UnprojectPosition(p);
        return true;
    }

    static void WorldHud(CanvasItem ci, Vector2 size, float k, SmokeExecuteMode m)
    {
        var g = m.Game;
        // phase bar under the top bar
        bool place = m.CurrentPhase == SmokeExecuteMode.Phase.Place;
        string label = place ? "PLACE YOUR SMOKES" : "EXECUTE";
        float left = Mathf.Max(0, m.PhaseLeft);
        int fs = UiTheme.Fs(17, k);
        float cx = size.X / 2, y = 116 * k, bw = 360 * k;
        if (!m.AnyMapOpen)
        {
        Gfx.TextC(ci, UiTheme.HudWide, $"{label}  {left:0.0}", cx, y, fs, place ? UiTheme.Warn : UiTheme.Text);
        ci.DrawRect(new Rect2(cx - bw / 2, y + 9 * k, bw, 4 * k), new Color(0, 0, 0, 0.45f));
        ci.DrawRect(new Rect2(cx - bw / 2, y + 9 * k, bw * Mathf.Clamp(1 - m.PhaseFrac, 0, 1), 4 * k), place ? UiTheme.Warn : UiTheme.Accent);
        }

        // abilities (bottom right)
        var abil = m.Abilities;
        float rowH = 34 * k, pw = 330 * k;
        float extra = m.Kit.Placing == SmokePlacing.Throw ? 30 * k : 0;
        var panel = new Rect2(size.X - pw - 28 * k, size.Y - 40 * k - abil.Count * rowH - extra - 16 * k, pw, abil.Count * rowH + extra + 16 * k);
        ci.DrawRect(panel, new Color(0.03f, 0.05f, 0.08f, 0.6f));
        float ry = panel.Position.Y + 8 * k;
        int ks = UiTheme.Fs(15, k), ns = UiTheme.Fs(16, k);
        foreach (var a in abil)
        {
            var kr = new Rect2(panel.Position.X + 10 * k, ry + 4 * k, 26 * k, 26 * k);
            ci.DrawRect(kr, a.Equipped ? m.Kit.Look.Rim : new Color(1, 1, 1, 0.12f));
            Gfx.TextC(ci, UiTheme.HudWide, Key(a.HotKey), kr.GetCenter().X, Gfx.Mid(kr.GetCenter().Y, ks), ks, a.Equipped ? new Color(0.05f, 0.05f, 0.08f) : UiTheme.Text);
            Gfx.Text(ci, UiTheme.HudWide, a.Name.ToUpperInvariant(), kr.End.X + 10 * k, Gfx.Mid(kr.GetCenter().Y, ns), ns, a.Spent ? UiTheme.Faint : UiTheme.Text);
            Gfx.TextR(ci, UiTheme.HudWide, a.Status, panel.End.X - 10 * k, Gfx.Mid(kr.GetCenter().Y, ns), ns, a.Spent ? UiTheme.Faint : m.Kit.Look.Rim);
            ry += rowH;
        }
        if (extra > 0)
        {
            float f = m.Fuel.Fuel / 100f;
            var fb = new Rect2(panel.Position.X + 10 * k, ry + 10 * k, pw - 20 * k, 6 * k);
            ci.DrawRect(fb, new Color(1, 1, 1, 0.12f));
            ci.DrawRect(new Rect2(fb.Position, new Vector2(fb.Size.X * f, fb.Size.Y)), m.Fuel.Fuel < ViperFuel.Min ? UiTheme.Accent : m.Kit.Look.Rim);
            ci.DrawRect(new Rect2(fb.Position.X + fb.Size.X * ViperFuel.Min / 100f, fb.Position.Y - 3 * k, 2 * k, fb.Size.Y + 6 * k), UiTheme.Text);
            Gfx.Text(ci, UiTheme.HudWide, $"FUEL {m.Fuel.Fuel:0}", fb.Position.X, fb.Position.Y - 6 * k, UiTheme.Fs(12, k), UiTheme.Dim);
        }

        // Astra: stars on screen (through walls), the one E would hit is ringed
        var neb = abil.OfType<NebulaAbility>().FirstOrDefault();
        if (neb != null && !m.AstralOpen)
        {
            var target = neb.Target();
            foreach (var s in m.Stars)
            {
                if (!Project(ci, s.Ground + Vector3.Up, out var sp)) continue;
                Star(ci, sp, 9 * k, s.Used ? new Color(m.Kit.Look.Rim, 0.3f) : m.Kit.Look.Rim);
                if (s == target)
                {
                    ci.DrawArc(sp, 18 * k, 0, Mathf.Tau, 32, UiTheme.Text, 2 * k, true);
                    Gfx.TextC(ci, UiTheme.HudWide, neb.Charges > 0 ? "E  NEBULA   ·   F  DISSIPATE" : "F  DISSIPATE", sp.X, sp.Y + 38 * k, UiTheme.Fs(13, k), UiTheme.Text);
                }
            }
            if (m.Stars.Count == 0 && m.StarsLeft > 0)
                Gfx.TextC(ci, UiTheme.HudWide, "X  ASTRAL FORM — PLACE YOUR STARS", size.X / 2, size.Y * 0.64f, UiTheme.Fs(16, k), UiTheme.Warn);
        }

        // Omen / Harbor: marker distance and an inset map
        var aim = abil.OfType<AimAbility>().FirstOrDefault(a => a.Equipped);
        if (aim != null && !aim.Overhead)
        {
            var c = size / 2;
            Gfx.TextC(ci, UiTheme.HudWide, $"{aim.Distance:0} M", c.X, c.Y + 46 * k, UiTheme.Fs(16, k), aim.InRange ? UiTheme.Text : UiTheme.Accent);
            Gfx.TextC(ci, UiTheme.HudWide, $"HOLD CLICK OUT · HOLD RIGHT-CLICK IN · {Key(m.Kit.SmokeKey)} THROW · R OVERHEAD", c.X, c.Y + 70 * k, UiTheme.Fs(13, k), UiTheme.Dim);
            var inset = new Rect2(size.X - 400 * k, 150 * k, 370 * k, 300 * k);
            ci.DrawRect(inset, new Color(0.03f, 0.05f, 0.08f, 0.75f));
            m.TMap.Fit(inset.Grow(-8 * k));
            MapLayer(ci, k, m, new Layer { Labels = false, Targets = false, Player = m.Feet, PlayerYaw = g.View.Yaw, Smokes = g.Smokes, Time = m.Clock, Tint = _ => m.Kit.Look.Light });
            var lc = m.TMap.ToScreen(aim.Landing);
            ci.DrawArc(lc, m.Kit.Radius * m.TMap.Scale, 0, Mathf.Tau, 32, new Color(0.55f, 0.8f, 1f), 2 * k, true);
        }

        // Viper / Harbor previews at the easy tiers (the game has no preview; beginners get one)
        if (m.Ghosts)
        {
            if (abil.OfType<OrbAbility>().FirstOrDefault(a => a.Equipped) is { } orb)
            {
                var (p0, v0) = orb.ThrowFrom(false);
                var pts = OrbAbility.Simulate(g.Solid, p0, v0, out var land);
                for (int i = 2; i < pts.Count; i += 3)
                    if (Project(ci, pts[i], out var sp)) ci.DrawCircle(sp, 2.2f * k, new Color(m.Kit.Look.Rim, 0.8f));
                GroundRing(ci, land, m.Kit.Radius, new Color(m.Kit.Look.Rim, 0.7f), k);
            }
            if (abil.OfType<ScreenAbility>().FirstOrDefault(a => a.Equipped) is { })
            {
                foreach (var p in ScreenAbility.Line(g.Solid, g.View.Eye, g.View.Yaw, m.WorldBounds))
                    if (Project(ci, p + Vector3.Up * 0.1f, out var sp)) ci.DrawCircle(sp, 3f * k, new Color(m.Kit.Look.Rim, 0.75f));
            }
            if (abil.OfType<TideAbility>().FirstOrDefault(a => a.Equipped) is { } tide && tide.Status != "FLOWING")
            {
                float yr = Mathf.DegToRad(g.View.Yaw);
                var dir = new Vector3(Mathf.Sin(yr), 0, -Mathf.Cos(yr));
                for (float s = 1; s < 14; s += 1.2f)
                {
                    var p = m.Feet + dir * s;
                    p.Y = Collision.Ground(new Vector3(p.X, m.Feet.Y + 0.7f, p.Z), g.Solid) + 0.1f;
                    if (Project(ci, p, out var sp)) ci.DrawCircle(sp, 3f * k, new Color(m.Kit.Look.Rim, 0.75f));
                }
            }
        }

        // what to do next (no map open)
        if (!m.AnyMapOpen && place && abil.All(a => !a.Equipped))
        {
            var next = abil.FirstOrDefault(a => !a.Spent);
            if (next != null)
                Gfx.TextC(ci, UiTheme.HudWide, $"{Key(next.HotKey)}  {next.Name.ToUpperInvariant()}", size.X / 2, size.Y * 0.64f, UiTheme.Fs(16, k), UiTheme.Warn);
        }
        if (m.Kit.Placing == SmokePlacing.Throw && !place)
        {
            var gases = abil.OfType<ViperGas>().ToList();
            if (gases.Count > 0 && gases.All(x => x.Deployed && !x.On) && m.Fuel.Fuel >= ViperFuel.Min)
                Gfx.TextC(ci, UiTheme.HudWide, $"{string.Join(" / ", gases.Select(x => Key(x.HotKey)))}  SWITCH YOUR GAS ON", size.X / 2, size.Y * 0.64f, UiTheme.Fs(16, k), UiTheme.Warn);
        }
    }

    static void GroundRing(CanvasItem ci, Vector3 c, float r, Color col, float k)
    {
        Vector2? prev = null;
        for (int i = 0; i <= 32; i++)
        {
            float a = i / 32f * Mathf.Tau;
            var p = c + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r);
            if (Project(ci, p, out var sp))
            {
                if (prev is { } pv) ci.DrawLine(pv, sp, col, 2 * k, true);
                prev = sp;
            }
            else prev = null;
        }
    }

    // =================================================================================================================
    // round summary
    // =================================================================================================================

    static void SummaryScreen(CanvasItem ci, Vector2 size, float k, SmokeExecuteMode m, SmokeExecuteMode.RoundLog r)
    {
        ci.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.035f, 0.05f, 0.72f));
        var (mr, info) = Columns(size, k);
        Gfx.Plate(ci, mr, 18 * k, Panel, new Color(UiTheme.Text, 0.12f));
        m.TMap.Fit(mr.Grow(-16 * k));
        var smokes = r.Smokes.Select(s => s.V).ToList();
        var tints = r.Smokes.ToDictionary(s => s.V, s => s.Tint);
        MapLayer(ci, k, m, new Layer
        {
            Standard = true, Sightlines = true, Smokes = smokes, SmokesFull = true, Tint = v => tints.TryGetValue(v, out var c) ? c : Colors.White,
            Defs = r.Defs, Path = r.Path, Labels = true, Targets = true,
        });
        Gfx.Plate(ci, info, 18 * k, Panel, new Color(UiTheme.Text, 0.12f));
        var inner = info.Grow(-26 * k);
        string res = r.Won ? $"SITE TAKEN · {r.ExecTime:0.0} S" : r.Result;
        Header(ci, k, inner, $"ROUND {r.Index} / 5", res, $"{m.Kit.Agent.ToUpperInvariant()} · {m.Kit.Ability.ToUpperInvariant()}");
        RoundStats(ci, k, m, r, inner.Position.X, inner.Position.Y + 136 * k, inner.Size.X);
        bool last = m.Rounds.Count >= 5;
        Gfx.Text(ci, UiTheme.HudWide, last ? $"RESULTS IN {Mathf.CeilToInt(Mathf.Max(0, m.PhaseLeft))}  ·  CLICK TO CONTINUE" : $"NEXT ROUND IN {Mathf.CeilToInt(Mathf.Max(0, m.PhaseLeft))}  ·  CLICK TO CONTINUE",
            inner.Position.X, inner.End.Y - 12 * k, UiTheme.Fs(14, k), UiTheme.Text);
    }

    /// <summary>The numbers of one round (summary and replay).</summary>
    public static float RoundStats(CanvasItem ci, float k, SmokeExecuteMode m, SmokeExecuteMode.RoundLog r, float x, float y, float w)
    {
        int ss = UiTheme.Fs(13, k), bs = UiTheme.Fs(17, k), big = UiTheme.Fs(30, k);
        Gfx.Text(ci, UiTheme.HudWide, "SIGHTLINES CUT", x, y, ss, UiTheme.Accent);
        y += 34 * k;
        Gfx.Text(ci, UiTheme.Display, $"{r.Coverage * 100:0}%", x, y, big, r.Coverage >= m.StdCoverage - 0.08f ? UiTheme.Good : UiTheme.Warn);
        Gfx.Text(ci, UiTheme.Body, $"standard smokes: {m.StdCoverage * 100:0}%", x + 90 * k, y - 4 * k, bs, UiTheme.Dim);
        y += 12 * k;
        var bar = new Rect2(x, y, w, 6 * k);
        ci.DrawRect(bar, new Color(1, 1, 1, 0.1f));
        ci.DrawRect(new Rect2(bar.Position, new Vector2(w * Mathf.Clamp(r.Coverage, 0, 1), bar.Size.Y)), r.Coverage >= m.StdCoverage - 0.08f ? UiTheme.Good : UiTheme.Warn);
        ci.DrawRect(new Rect2(x + w * Mathf.Clamp(m.StdCoverage, 0, 1) - 1 * k, y - 4 * k, 2 * k, 14 * k), UiTheme.Text);
        y += 36 * k;
        Gfx.Text(ci, UiTheme.HudWide, "YOUR SMOKES VS THE STANDARD SPOTS", x, y, ss, UiTheme.Accent);
        y += 28 * k;
        foreach (var s in r.Spots)
        {
            string v = float.IsInfinity(s.Error) ? "not placed" : $"{s.Error:0.0} m off{(s.ByWall ? " (wall)" : "")}";
            Gfx.Text(ci, UiTheme.Body, s.Name, x, y, bs, UiTheme.Text);
            Gfx.TextR(ci, UiTheme.Body, v, x + w, y, bs, s.Good ? UiTheme.Good : float.IsInfinity(s.Error) || s.Error > 6 ? UiTheme.Accent : UiTheme.Warn);
            y += 26 * k;
        }
        y += 10 * k;
        void Row(string a, string b, Color c) { Gfx.Text(ci, UiTheme.Body, a, x, y, bs, UiTheme.Dim); Gfx.TextR(ci, UiTheme.Body, b, x + w, y, bs, c); y += 26 * k; }
        if (r.EntryAt >= 0)
            Row("Smokes up when you entered", $"{r.SmokesUpAtEntry} / {r.SmokesAtEntry}", r.AllUpAtEntry ? UiTheme.Good : UiTheme.Warn);
        else Row("Entered the site", "no", UiTheme.Warn);
        Row("Kills", $"{r.Kills} / {r.Defenders}", r.Kills == r.Defenders ? UiTheme.Good : UiTheme.Text);
        Row("Enemy sight blocked by smoke", $"{r.SmokeSaves:0.0} s", UiTheme.Text);
        Row("Round points", $"+{r.Points}", UiTheme.Text);
        return y;
    }
}
