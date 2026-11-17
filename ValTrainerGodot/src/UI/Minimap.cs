using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Game.Bots;
using ValTrainer.Modes;

namespace ValTrainer.UI;

/// <summary>
/// The static half of the minimap: a top-down plan of the level, drawn once from the session's solid boxes (any map, the
/// range and a drill's own arena alike, so new maps get it for free). Each box raises the "top" of the cells it covers;
/// boxes that start more than <see cref="Overhead"/> above the top already there (lintels, roofs, a window's upper wall)
/// are overhead and ignored. The playable floor is flooded from the player's feet over steps of at most
/// <see cref="WalkStep"/> (stairs yes, crates no; drops are free). Cells next to the floor that stand higher are walls and
/// obstacles; everything else (outside the play space) stays transparent. No VALORANT minimap images are used.
/// </summary>
public sealed class MinimapPlan
{
    /// <summary>Highest step the floor flood climbs (m): a walkable step (Collision.StepHeight 0.7), not a jump.</summary>
    public const float WalkStep = 0.75f;
    /// <summary>A box starting this far above the top already under it is overhead (doesn't block, isn't drawn).</summary>
    public const float Overhead = 2.0f;
    /// <summary>Non-floor cells within this distance of the floor are drawn as walls / obstacles (m).</summary>
    const float WallBand = 1.0f;
    const int MaxCells = 1100;

    public readonly float Cell;
    /// <summary>World X / Z of the grid's corner (cell 0, 0).</summary>
    public readonly Vector2 Origin;
    public readonly int W, H;
    readonly float[] top;
    readonly bool[] floor;
    readonly List<int> seeds = new();
    /// <summary>The playable floor's extent (world X, Z).</summary>
    public Rect2 FloorBounds { get; private set; }
    /// <summary>RGBA8 pixels, one per cell (<see cref="W"/> × <see cref="H"/>), made by <see cref="Render"/>.</summary>
    public byte[] Pixels { get; private set; } = Array.Empty<byte>();
    /// <summary>Lowest floor height (shading reference).</summary>
    float floorMin;

    MinimapPlan(float cell, Vector2 origin, int w, int h)
    {
        Cell = cell; Origin = origin; W = w; H = h;
        top = new float[w * h];
        Array.Fill(top, float.NaN);
        floor = new bool[w * h];
    }

    /// <summary>Rasterizes the boxes and floods the floor from <paramref name="feet"/>. Safe on a worker thread (no Godot objects).</summary>
    public static MinimapPlan Build(IReadOnlyList<Box> solid, Vector3 feet)
    {
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var b in solid)
        {
            x0 = Mathf.Min(x0, b.Min.X); z0 = Mathf.Min(z0, b.Min.Z);
            x1 = Mathf.Max(x1, b.Max.X); z1 = Mathf.Max(z1, b.Max.Z);
        }
        if (solid.Count == 0) { x0 = feet.X - 10; x1 = feet.X + 10; z0 = feet.Z - 10; z1 = feet.Z + 10; }
        float cell = Mathf.Max(0.2f, Mathf.Max(x1 - x0, z1 - z0) / (MaxCells - 4));
        // one empty cell all round: the texture's clamped edge stays transparent
        var origin = new Vector2(x0 - 2 * cell, z0 - 2 * cell);
        int w = (int)Mathf.Ceil((x1 - x0) / cell) + 4, h = (int)Mathf.Ceil((z1 - z0) / cell) + 4;
        var p = new MinimapPlan(cell, origin, w, h);
        p.Rasterize(solid);
        p.AddSeed(feet);
        p.Flood();
        p.Render();
        return p;
    }

    int Idx(int i, int j) => j * W + i;
    int CellI(float x) => Math.Clamp((int)Mathf.Floor((x - Origin.X) / Cell), 0, W - 1);
    int CellJ(float z) => Math.Clamp((int)Mathf.Floor((z - Origin.Y) / Cell), 0, H - 1);

    void Rasterize(IReadOnlyList<Box> solid)
    {
        // supports first: a box stacks on what is under it unless it floats overhead
        foreach (var b in solid.OrderBy(b => b.Min.Y))
        {
            if (b.Max.X - b.Min.X < 1e-3f || b.Max.Z - b.Min.Z < 1e-3f) continue;
            // conservative: every cell the box touches (a 0.1 m wall still blocks the flood)
            int i0 = CellI(b.Min.X), i1 = CellI(b.Max.X - 1e-4f), j0 = CellJ(b.Min.Z), j1 = CellJ(b.Max.Z - 1e-4f);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int c = Idx(i, j);
                    float t = top[c];
                    if (!float.IsNaN(t) && b.Min.Y > t + Overhead) continue;
                    if (float.IsNaN(t) || b.Max.Y > t) top[c] = b.Max.Y;
                }
        }
    }

    /// <summary>The floor cell nearest to <paramref name="feet"/> at about its height (-1 = none within 2 m).</summary>
    int SeedAt(Vector3 feet)
    {
        int ci = CellI(feet.X), cj = CellJ(feet.Z);
        int r = (int)Mathf.Ceil(2f / Cell);
        int best = -1; float bestD = float.MaxValue;
        for (int j = Math.Max(0, cj - r); j <= Math.Min(H - 1, cj + r); j++)
            for (int i = Math.Max(0, ci - r); i <= Math.Min(W - 1, ci + r); i++)
            {
                float t = top[Idx(i, j)];
                if (float.IsNaN(t) || Mathf.Abs(t - feet.Y) > 0.45f) continue;
                float d = (i - ci) * (i - ci) + (j - cj) * (j - cj);
                if (d < bestD) { bestD = d; best = Idx(i, j); }
            }
        return best;
    }

    bool AddSeed(Vector3 feet)
    {
        int s = SeedAt(feet);
        if (s < 0 || floor[s]) return false;
        seeds.Add(s);
        return true;
    }

    /// <summary>The player stands somewhere the plan has no floor (a teleport into another area): flood from there too and
    /// render again. Returns false when nothing changed. Safe on a worker thread.</summary>
    public bool Extend(Vector3 feet)
    {
        if (!AddSeed(feet)) return false;
        Flood();
        Render();
        return true;
    }

    /// <summary>True when the feet stand on a cell the plan doesn't count as floor yet (but could).</summary>
    public bool Missing(Vector3 feet)
    {
        int s = SeedAt(feet);
        return s >= 0 && !floor[s];
    }

    void Flood()
    {
        var q = new Queue<int>();
        foreach (int s in seeds) if (!floor[s]) { floor[s] = true; q.Enqueue(s); }
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int i = c % W, j = c / W;
            float t = top[c];
            for (int n = 0; n < 4; n++)
            {
                int ni = i + (n == 0 ? 1 : n == 1 ? -1 : 0), nj = j + (n == 2 ? 1 : n == 3 ? -1 : 0);
                if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                int d = Idx(ni, nj);
                if (floor[d] || float.IsNaN(top[d]) || top[d] - t > WalkStep) continue;
                floor[d] = true;
                q.Enqueue(d);
            }
        }
        float fx0 = float.MaxValue, fz0 = float.MaxValue, fx1 = float.MinValue, fz1 = float.MinValue;
        floorMin = float.MaxValue;
        for (int c = 0; c < floor.Length; c++)
        {
            if (!floor[c]) continue;
            int i = c % W, j = c / W;
            fx0 = Mathf.Min(fx0, i); fx1 = Mathf.Max(fx1, i + 1); fz0 = Mathf.Min(fz0, j); fz1 = Mathf.Max(fz1, j + 1);
            floorMin = Mathf.Min(floorMin, top[c]);
        }
        if (fx0 > fx1) { fx0 = 0; fz0 = 0; fx1 = W; fz1 = H; floorMin = 0; }
        FloorBounds = new Rect2(Origin.X + fx0 * Cell, Origin.Y + fz0 * Cell, (fx1 - fx0) * Cell, (fz1 - fz0) * Cell);
    }

    // colours (VALORANT-like: light floor, white wall edges, dark walls; nothing outside the play space)
    static readonly Color FloorCol = new(0.46f, 0.53f, 0.58f, 0.78f), EdgeCol = new(0.88f, 0.92f, 0.94f, 0.95f),
        StepCol = new(0.30f, 0.35f, 0.40f, 0.85f), WallCol = new(0.13f, 0.16f, 0.19f, 0.88f), LowCol = new(0.27f, 0.31f, 0.35f, 0.9f);

    void Render()
    {
        int n = W * H;
        // nearest floor height for the cells around the floor (multi-source BFS, WallBand deep)
        var near = new float[n];
        Array.Fill(near, float.NaN);
        var dist = new int[n];
        Array.Fill(dist, int.MaxValue);
        var q = new Queue<int>();
        for (int c = 0; c < n; c++) if (floor[c]) { near[c] = top[c]; dist[c] = 0; q.Enqueue(c); }
        int band = Math.Max(1, (int)Mathf.Round(WallBand / Cell));
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (dist[c] >= band) continue;
            int i = c % W, j = c / W;
            for (int k = 0; k < 4; k++)
            {
                int ni = i + (k == 0 ? 1 : k == 1 ? -1 : 0), nj = j + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                int d = Idx(ni, nj);
                if (dist[d] != int.MaxValue) continue;
                dist[d] = dist[c] + 1; near[d] = near[c];
                q.Enqueue(d);
            }
        }
        var px = new byte[n * 4];
        void Put(int c, Color col)
        {
            px[c * 4] = (byte)(col.R * 255); px[c * 4 + 1] = (byte)(col.G * 255);
            px[c * 4 + 2] = (byte)(col.B * 255); px[c * 4 + 3] = (byte)(col.A * 255);
        }
        int edgeR = Math.Max(1, (int)Mathf.Round(0.55f / Cell)); // ~0.55 m white wall edge: still a line when zoomed out
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int c = Idx(i, j);
                if (floor[c])
                {
                    bool edge = false, step = false;
                    for (int dj = -edgeR; dj <= edgeR && !edge; dj++)
                        for (int di = -edgeR; di <= edgeR; di++)
                        {
                            int ni = i + di, nj = j + dj;
                            if (ni < 0 || nj < 0 || ni >= W || nj >= H) { edge = true; break; }
                            int d = Idx(ni, nj);
                            if (!floor[d]) { edge = true; break; }
                            if (Math.Abs(di) <= 1 && Math.Abs(dj) <= 1 && top[d] - top[c] > 0.25f) step = true;
                        }
                    if (edge) Put(c, EdgeCol);
                    else if (step) Put(c, StepCol);
                    else
                    {
                        float lift = Mathf.Clamp((top[c] - floorMin) * 0.06f, 0f, 0.22f); // higher floors read lighter
                        Put(c, new Color(FloorCol.R + lift, FloorCol.G + lift, FloorCol.B + lift, FloorCol.A));
                    }
                }
                else if (!float.IsNaN(near[c]) && !float.IsNaN(top[c]) && top[c] > near[c] + 0.3f)
                    Put(c, top[c] - near[c] <= 1.3f ? LowCol : WallCol); // jumpable boxes a touch lighter than walls
            }
        Pixels = px;
    }
}

/// <summary>
/// VALORANT-style minimap in the top-left corner (a reusable HUD part: a drill turns it on with
/// <see cref="TrainingMode.Minimap"/>; players turn it off per drill in the drill preview, M, and pick rotating or fixed in
/// Settings → Gameplay). The level is drawn once into a texture (<see cref="MinimapPlan"/>, built off the main thread);
/// each frame only the player arrow, the field-of-view cone and the enemies are drawn. Enemies show as red dots only while
/// spotted, as in VALORANT: in your field of view with line of sight, or right after they fire or you hit them; then the
/// dot stays where you last saw them and fades.
/// </summary>
public sealed partial class MinimapView : Control
{
    readonly GameSession s;
    public MinimapView(GameSession session) => s = session;

    /// <summary>Seconds a spotted enemy's dot stays fully visible after you lose sight of it, then fades over <see cref="Fade"/>.</summary>
    const float Hold = 0.35f, Fade = 1.6f;

    MinimapPlan? plan;
    Task<MinimapPlan>? building;
    Task<bool>? extending;
    ImageTexture? tex;
    IReadOnlyList<Box>? builtFor;
    float nextExtendCheck;

    sealed class Seen { public float At = -99f; public Vector3 Pos; public int Shots, Hits; public bool Now; }
    readonly Dictionary<BotCharacter, Seen> seen = new();

    /// <summary>The drill shows the minimap: it offers one and the player didn't turn it off for this drill.</summary>
    public static bool On(TrainingMode m) => !m.Is2D && (DevForce || (m.Minimap && !(Main.I.Settings.MinimapOffDrills?.Contains(m.Key) ?? false)));
    /// <summary>Dev "--minimap": every 3D drill shows it (check a new map: --mode siteclear --map NAME --minimap;
    /// "--minimap fixed" also forces the fixed orientation).</summary>
    static readonly bool DevForce = CmdLine.Dev && CmdLine.Has("--minimap");
    static readonly bool DevFixed = CmdLine.DevAfter("--minimap") == "fixed";

    /// <summary>Where the minimap sits (VALORANT's top-left corner), HUD scale <paramref name="k"/>.</summary>
    public static Rect2 Area(float k) => new(22 * k, 22 * k, 270 * k, 270 * k);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps; // the plan is drawn scaled down and rotated
    }

    public bool Shown => s.Mode != null && On(s.Mode) && s.State != GameSession.St.Results;

    public override void _Process(double delta)
    {
        Visible = Shown;
        if (!Visible) return;
        if (!ReferenceEquals(builtFor, s.Solid))
        {
            builtFor = s.Solid;
            var boxes = s.Solid.ToArray();
            var feet = s.PlayerFeet;
            plan = null; tex = null; extending = null; seen.Clear();
            building = Task.Run(() => MinimapPlan.Build(boxes, feet));
        }
        if (building is { IsCompleted: true } b)
        {
            building = null;
            if (b.IsCompletedSuccessfully) { plan = b.Result; tex = MakeTexture(plan); }
            else GD.PushWarning($"Minimap off: {b.Exception?.GetBaseException().Message}");
        }
        if (extending is { IsCompleted: true } e)
        {
            extending = null;
            if (e.IsCompletedSuccessfully && e.Result && plan != null) tex = MakeTexture(plan);
        }
        // a respawn or teleport into an area the flood didn't reach: add it
        if (plan != null && extending == null && s.Mover.Grounded && s.Now >= nextExtendCheck)
        {
            nextExtendCheck = s.Now + 1f;
            var feet = s.PlayerFeet;
            var p = plan;
            if (p.Missing(feet)) extending = Task.Run(() => p.Extend(feet));
        }
        TrackEnemies();
        QueueRedraw();
    }

    static ImageTexture MakeTexture(MinimapPlan p)
    {
        var img = Image.CreateFromData(p.W, p.H, false, Image.Format.Rgba8, p.Pixels);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    void TrackEnemies()
    {
        float now = s.Now;
        var eye = s.View.Eye;
        float half = s.View.HFov / 2f;
        foreach (var bot in s.SpawnedBots)
        {
            if (!IsInstanceValid(bot) || !bot.IsInsideTree()) continue;
            if (!seen.TryGetValue(bot, out var st)) { st = new Seen { Shots = bot.ShotsFired, Hits = bot.HitsTaken }; seen[bot] = st; }
            st.Now = false;
            if (bot.Dead) { st.At = -99f; continue; }
            bool reveal = bot.ShotsFired != st.Shots || bot.HitsTaken != st.Hits; // fired, or you hit him
            st.Shots = bot.ShotsFired; st.Hits = bot.HitsTaken;
            var head = bot.Head;
            var to = head - eye;
            float yaw = Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z));
            if (Mathf.Abs(Mathf.Wrap(yaw - s.View.ViewYaw, -180f, 180f)) <= half
                && (s.LineOfSight(eye, head) || s.LineOfSight(eye, head + Vector3.Down * 0.5f)))
                reveal = st.Now = true;
            if (reveal) { st.At = now; st.Pos = bot.Feet; }
        }
        if (seen.Count > 16) foreach (var dead in seen.Keys.Where(b => !IsInstanceValid(b)).ToList()) seen.Remove(dead);
    }

    // ---------------- drawing ----------------

    static readonly Color Bg = new(0.03f, 0.05f, 0.08f, 0.55f), Frame = new(1, 1, 1, 0.16f),
        Self = Color.Color8(255, 214, 92), Enemy = Color.Color8(255, 70, 85);

    /// <summary>Rotating: the player sits this far below the centre (fraction of the half size), so more of what is ahead
    /// shows, like VALORANT's rotating minimap with "Keep player centered" off.</summary>
    public const float AheadShift = 0.3f;

    /// <summary>World X/Z → screen, and back (rotating: the view direction points up; fixed: north (-Z) up).</summary>
    readonly struct Xf
    {
        readonly Vector2 c, p, f, r; readonly float sc;
        public Xf(Rect2 area, Vector2 player, float yawDeg, bool rotate, float range, Rect2 bounds)
        {
            c = area.GetCenter();
            if (rotate)
            {
                float y = Mathf.DegToRad(yawDeg);
                p = player; f = new Vector2(Mathf.Sin(y), -Mathf.Cos(y)); r = new Vector2(Mathf.Cos(y), Mathf.Sin(y));
                sc = area.Size.X / 2f / range;
                c += new Vector2(0, area.Size.Y / 2f * AheadShift);
            }
            else
            {
                p = bounds.GetCenter(); f = new Vector2(0, -1); r = new Vector2(1, 0);
                sc = Mathf.Min(area.Size.X / bounds.Size.X, area.Size.Y / bounds.Size.Y);
            }
        }
        public float Scale => sc;
        public Vector2 ToScreen(Vector2 w) { var d = w - p; return c + new Vector2(d.Dot(r), -d.Dot(f)) * sc; }
        public Vector2 ToWorld(Vector2 q) { var v = (q - c) / sc; return p + r * v.X - f * v.Y; }
        /// <summary>A world direction (yaw) as a screen direction.</summary>
        public Vector2 Dir(float yawDeg)
        {
            float y = Mathf.DegToRad(yawDeg);
            var w = new Vector2(Mathf.Sin(y), -Mathf.Cos(y));
            return new Vector2(w.Dot(r), -w.Dot(f)).Normalized();
        }
    }

    public override void _Draw()
    {
        var m = s.Mode;
        if (m == null) return;
        float k = UiTheme.S(GetViewportRect().Size);
        var area = Area(k);
        DrawRect(area, Bg);
        bool rotate = !Main.I.Settings.MinimapFixed && !DevFixed;
        var player = new Vector2(s.View.Eye.X, s.View.Eye.Z);
        var bounds = plan != null ? plan.FloorBounds.Grow(2f) : new Rect2(player - new Vector2(20, 20), new Vector2(40, 40));
        var xf = new Xf(area, player, s.View.Yaw, rotate, Mathf.Max(5f, m.MinimapRange), bounds);

        if (plan != null && tex != null)
        {
            // the whole square as one textured quad: each corner's UV is the world point under it
            var size = new Vector2(plan.W * plan.Cell, plan.H * plan.Cell);
            Span<Vector2> corners = stackalloc Vector2[4];
            corners[0] = area.Position; corners[1] = new Vector2(area.End.X, area.Position.Y);
            corners[2] = area.End; corners[3] = new Vector2(area.Position.X, area.End.Y);
            var pts = new Vector2[4];
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                pts[i] = corners[i];
                uvs[i] = (xf.ToWorld(corners[i]) - plan.Origin) / size;
            }
            DrawPolygon(pts, new[] { Colors.White }, uvs, tex);
        }

        var me = xf.ToScreen(player);
        DrawCone(area, me, xf.Dir(s.View.ViewYaw), s.View.HFov, area.Size.X * 0.36f);

        // enemies: red dots while spotted, fading at the last place you saw them
        float now = s.Now, dr = 6.5f * k;
        foreach (var (bot, st) in seen)
        {
            float t = now - st.At;
            if (t > Hold + Fade || !IsInstanceValid(bot) || bot.Dead) continue;
            float a = st.Now ? 1f : t <= Hold ? 1f : 1f - (t - Hold) / Fade;
            var pos = st.Now ? bot.Feet : st.Pos;
            var q = xf.ToScreen(new Vector2(pos.X, pos.Z));
            if (!area.Grow(-dr).HasPoint(q)) continue;
            DrawCircle(q, dr + 1.5f * k, new Color(0, 0, 0, 0.55f * a));
            DrawCircle(q, dr, new Color(Enemy, a));
        }

        DrawPlayer(me, xf.Dir(s.View.ViewYaw), k);
        // frame + corner ticks
        DrawRect(area, Frame, false, Mathf.Max(1f, 1.5f * k));
        Gfx.Brackets(this, area, 12 * k, new Color(UiTheme.Text, 0.45f), Mathf.Max(1, 2 * k));
    }

    void DrawCone(Rect2 area, Vector2 apex, Vector2 dir, float fovDeg, float len)
    {
        const int Seg = 14;
        float half = Mathf.DegToRad(Mathf.Clamp(fovDeg, 5f, 170f) / 2f);
        float baseAng = Mathf.Atan2(dir.Y, dir.X);
        var pts = new Vector2[Seg + 2];
        pts[0] = apex;
        bool inside = area.HasPoint(apex);
        for (int i = 0; i <= Seg; i++)
        {
            float a = baseAng - half + 2 * half * i / Seg;
            pts[i + 1] = apex + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len;
            inside &= area.Grow(0.5f).HasPoint(pts[i + 1]);
        }
        if (inside)
        {
            var cols = new Color[pts.Length];
            cols[0] = new Color(1, 1, 1, 0.26f);
            for (int i = 1; i < cols.Length; i++) cols[i] = new Color(1, 1, 1, 0.03f);
            DrawPolygon(pts, cols);
            return;
        }
        // fixed mode near the edge: clip the cone to the square (flat colour)
        var sq = new[] { area.Position, new Vector2(area.End.X, area.Position.Y), area.End, new Vector2(area.Position.X, area.End.Y) };
        foreach (var poly in Geometry2D.IntersectPolygons(pts, sq))
            if (poly.Length >= 3) DrawColoredPolygon(poly, new Color(1, 1, 1, 0.13f));
    }

    void DrawPlayer(Vector2 at, Vector2 dir, float k)
    {
        var side = new Vector2(-dir.Y, dir.X);
        float l = 11f * k, w = 7.5f * k;
        var tip = at + dir * l;
        var back = at - dir * l * 0.6f;
        var arrow = new[] { tip, back + side * w, at - dir * l * 0.2f, back - side * w };
        DrawCircle(at, 8.5f * k, new Color(0, 0, 0, 0.45f));
        DrawColoredPolygon(arrow, Self);
        DrawPolyline(new[] { tip, back + side * w, at - dir * l * 0.2f, back - side * w, tip }, new Color(0, 0, 0, 0.7f), Mathf.Max(1f, 1.2f * k));
    }
}
