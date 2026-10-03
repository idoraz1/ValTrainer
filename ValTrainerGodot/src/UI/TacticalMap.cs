using Godot;
using ValTrainer.Maps;

namespace ValTrainer.UI;

/// <summary>
/// Top-down tactical map of a map blockout, used by the controller drill (Brimstone / Clove / Miks placement, Astra's
/// Astral Form, Omen's overhead view, the execute plan, the round summary and the smoke replay).
/// The floor image is baked once per map from the boxes: every 1/6 m column gets the highest surface a player can stand
/// on with head room that the walkable grid reaches (so roofs and closed-off blocks stay dark); shading follows height
/// (higher = lighter), walls and crates get a bright outline. It is turned in quarter turns so the attack runs bottom → top.
/// </summary>
public sealed class TacticalMap
{
    const float Px = 6f; // image pixels per metre

    public readonly MapSpot Map;
    /// <summary>World XZ directions shown up / right on screen.</summary>
    public readonly Vector2 Up, Right;
    readonly float u0, v0, u1, v1;
    readonly int w, h;
    readonly float[] top;
    public readonly ImageTexture Texture;
    public readonly float MinY, MaxY;

    Vector2 origin;
    float scale = 1f;
    /// <summary>Screen rectangle of the map after <see cref="Fit"/>.</summary>
    public Rect2 Area { get; private set; }
    /// <summary>Screen pixels per metre after <see cref="Fit"/>.</summary>
    public float Scale => scale;

    static readonly Dictionary<string, TacticalMap> cache = new();

    /// <param name="reachable">(x, z, y) → reachable floor at height y near (x, z)?</param>
    /// <param name="from">Attack start (map bottom).</param>
    /// <param name="to">Attack target (map top).</param>
    public static TacticalMap For(MapSpot map, Func<float, float, float, bool> reachable, Vector3 from, Vector3 to)
    {
        string key = $"{map.Key}|{map.Boxes.Length}";
        if (cache.TryGetValue(key, out var m)) return m;
        return cache[key] = new TacticalMap(map, reachable, from, to);
    }

    TacticalMap(MapSpot map, Func<float, float, float, bool> reachable, Vector3 from, Vector3 to)
    {
        Map = map;
        var d = new Vector2(to.X - from.X, to.Z - from.Z);
        Up = Mathf.Abs(d.X) > Mathf.Abs(d.Y) ? new Vector2(Mathf.Sign(d.X), 0) : new Vector2(0, d.Y > 0 ? 1 : -1);
        Right = new Vector2(-Up.Y, Up.X);

        float wx0 = float.MaxValue, wz0 = float.MaxValue, wx1 = float.MinValue, wz1 = float.MinValue;
        foreach (var b in map.Boxes)
        {
            wx0 = Mathf.Min(wx0, b.Min.X); wz0 = Mathf.Min(wz0, b.Min.Z);
            wx1 = Mathf.Max(wx1, b.Max.X); wz1 = Mathf.Max(wz1, b.Max.Z);
        }
        // map-frame bounds of the world rectangle
        float fu0 = float.MaxValue, fv0 = float.MaxValue, fu1 = float.MinValue, fv1 = float.MinValue;
        foreach (var c in new[] { new Vector2(wx0, wz0), new Vector2(wx1, wz0), new Vector2(wx0, wz1), new Vector2(wx1, wz1) })
        {
            float u = c.Dot(Right), v = -c.Dot(Up);
            fu0 = Mathf.Min(fu0, u); fu1 = Mathf.Max(fu1, u); fv0 = Mathf.Min(fv0, v); fv1 = Mathf.Max(fv1, v);
        }
        int W = Math.Max(1, (int)Mathf.Ceil((fu1 - fu0) * Px)), H = Math.Max(1, (int)Mathf.Ceil((fv1 - fv0) * Px));

        // boxes bucketed on a 2 m grid
        const float B = 2f;
        int bx = Math.Max(1, (int)Mathf.Ceil((wx1 - wx0) / B)), bz = Math.Max(1, (int)Mathf.Ceil((wz1 - wz0) / B));
        var buckets = new List<int>[bx * bz];
        for (int i = 0; i < map.Boxes.Length; i++)
        {
            var b = map.Boxes[i];
            int ax = Math.Clamp((int)((b.Min.X - wx0) / B), 0, bx - 1), az = Math.Clamp((int)((b.Min.Z - wz0) / B), 0, bz - 1);
            int cx = Math.Clamp((int)((b.Max.X - wx0) / B), 0, bx - 1), cz = Math.Clamp((int)((b.Max.Z - wz0) / B), 0, bz - 1);
            for (int z = az; z <= cz; z++)
                for (int x = ax; x <= cx; x++) (buckets[z * bx + x] ??= new List<int>()).Add(i);
        }

        var full = new float[W * H];
        var crate = new bool[W * H];
        var cands = new List<float>(8);
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float u = fu0 + (px + 0.5f) / Px, v = fv0 + (py + 0.5f) / Px;
                var xz = Right * u - Up * v;
                float x = xz.X, z = xz.Y;
                int idx = py * W + px;
                full[idx] = float.NaN;
                int kx = (int)((x - wx0) / B), kz = (int)((z - wz0) / B);
                var bucket = kx >= 0 && kz >= 0 && kx < bx && kz < bz ? buckets[kz * bx + kx] : null;
                cands.Clear();
                cands.Add(0f);
                BoxDef? highest = null;
                if (bucket != null)
                    foreach (var bi in bucket)
                    {
                        var b = map.Boxes[bi];
                        if (x < b.Min.X || x > b.Max.X || z < b.Min.Z || z > b.Max.Z) continue;
                        if (b.Max.Y < 25f) cands.Add(b.Max.Y);
                        if (highest == null || b.Max.Y > highest.Max.Y) highest = b;
                    }
                cands.Sort((a, c) => c.CompareTo(a));
                foreach (var c in cands)
                {
                    bool blocked = false;
                    if (bucket != null)
                        foreach (var bi in bucket)
                        {
                            var b = map.Boxes[bi];
                            if (x < b.Min.X || x > b.Max.X || z < b.Min.Z || z > b.Max.Z) continue;
                            if (b.Min.Y < c + 1.75f && b.Max.Y > c + 0.02f) { blocked = true; break; }
                        }
                    if (blocked || !reachable(x, z, c)) continue;
                    full[idx] = c;
                    break;
                }
                if (float.IsNaN(full[idx]) && highest is { Surf: Surf.Cover }) crate[idx] = true;
            }

        // crop to the floor (+3 m)
        int minX = W, minY = H, maxX = -1, maxY = -1;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float t = full[py * W + px];
                if (float.IsNaN(t)) continue;
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
            }
        if (maxX < 0) { minX = 0; minY = 0; maxX = W - 1; maxY = H - 1; lo = 0; hi = 1; }
        int m = (int)(3 * Px);
        minX = Math.Max(0, minX - m); minY = Math.Max(0, minY - m); maxX = Math.Min(W - 1, maxX + m); maxY = Math.Min(H - 1, maxY + m);
        w = maxX - minX + 1; h = maxY - minY + 1;
        u0 = fu0 + minX / Px; v0 = fv0 + minY / Px; u1 = u0 + w / Px; v1 = v0 + h / Px;
        MinY = lo; MaxY = hi;
        top = new float[w * h];
        var isCrate = new bool[w * h];
        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                top[py * w + px] = full[(py + minY) * W + px + minX];
                isCrate[py * w + px] = crate[(py + minY) * W + px + minX];
            }

        // walls fade out with distance from the floor (separable max filter of the floor mask, 3 m)
        var nearH = new byte[w * h];
        var near = new byte[w * h];
        int R = (int)(3 * Px);
        for (int py = 0; py < h; py++)
        {
            int last = -100000;
            for (int px = 0; px < w; px++) { if (!float.IsNaN(top[py * w + px])) last = px; nearH[py * w + px] = (byte)Math.Clamp(R - (px - last), 0, R); }
            last = 100000;
            for (int px = w - 1; px >= 0; px--) { if (!float.IsNaN(top[py * w + px])) last = px; nearH[py * w + px] = (byte)Math.Max(nearH[py * w + px], Math.Clamp(R - (last - px), 0, R)); }
        }
        for (int px = 0; px < w; px++)
        {
            int best = 0;
            for (int py = 0; py < h; py++)
            {
                best = 0;
                for (int dy = -R; dy <= R; dy += 2)
                {
                    int yy = py + dy;
                    if (yy < 0 || yy >= h) continue;
                    best = Math.Max(best, nearH[yy * w + px] - Math.Abs(dy));
                }
                near[py * w + px] = (byte)Math.Clamp(best, 0, R);
            }
        }

        var bytes = new byte[w * h * 4];
        float span = Mathf.Max(0.5f, hi - lo);
        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                int i = py * w + px;
                float t = top[i];
                Color c;
                if (!float.IsNaN(t))
                {
                    float k = 0.18f + 0.82f * Mathf.Clamp((t - lo) / span, 0f, 1f);
                    c = new Color(0.27f, 0.33f, 0.38f).Lerp(new Color(0.6f, 0.67f, 0.72f), k);
                    bool edge = false, ledge = false;
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = px + (n == 0 ? 1 : n == 1 ? -1 : 0), ny = py + (n == 2 ? 1 : n == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) { edge = true; continue; }
                        float o = top[ny * w + nx];
                        if (float.IsNaN(o)) edge = true;
                        else if (t - o > 0.75f) ledge = true;
                    }
                    if (edge) c = new Color(0.84f, 0.9f, 0.93f);
                    else if (ledge) c = c.Lerp(new Color(0.9f, 0.94f, 0.96f), 0.55f);
                }
                else if (isCrate[i])
                {
                    bool edge = false;
                    for (int n = 0; n < 4 && !edge; n++)
                    {
                        int nx = px + (n == 0 ? 1 : n == 1 ? -1 : 0), ny = py + (n == 2 ? 1 : n == 3 ? -1 : 0);
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && !isCrate[ny * w + nx]) edge = true;
                    }
                    c = edge ? new Color(0.58f, 0.64f, 0.68f, 0.95f) : new Color(0.16f, 0.2f, 0.24f, 0.95f);
                }
                else
                {
                    float a = near[i] / (float)R;
                    c = new Color(0.05f, 0.08f, 0.1f, 0.82f * a * a);
                }
                bytes[i * 4] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255);
                bytes[i * 4 + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255);
                bytes[i * 4 + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
                bytes[i * 4 + 3] = (byte)(Mathf.Clamp(c.A, 0, 1) * 255);
            }
        Texture = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes));
    }

    /// <summary>Lays the map out inside <paramref name="area"/> (keeps the aspect, centred).</summary>
    public void Fit(Rect2 area)
    {
        scale = Mathf.Min(area.Size.X / (u1 - u0), area.Size.Y / (v1 - v0));
        var size = new Vector2(u1 - u0, v1 - v0) * scale;
        origin = area.Position + (area.Size - size) / 2f;
        Area = new Rect2(origin, size);
    }

    public Vector2 ToScreen(Vector3 p) => origin + new Vector2(p.X * Right.X + p.Z * Right.Y - u0, -(p.X * Up.X + p.Z * Up.Y) - v0) * scale;

    /// <summary>Screen direction of a world direction (XZ).</summary>
    public Vector2 ScreenDir(Vector3 d)
    {
        var s = new Vector2(d.X * Right.X + d.Z * Right.Y, -(d.X * Up.X + d.Z * Up.Y));
        return s.LengthSquared() < 1e-8f ? Vector2.Up : s.Normalized();
    }

    /// <summary>Screen direction of a view yaw (0 = -Z, + = right).</summary>
    public Vector2 YawDir(float yawDeg)
    {
        float y = Mathf.DegToRad(yawDeg);
        return ScreenDir(new Vector3(Mathf.Sin(y), 0, -Mathf.Cos(y)));
    }

    /// <summary>World XZ under a screen point (y = 0).</summary>
    public Vector3 ToWorldFlat(Vector2 s)
    {
        var uv = (s - origin) / scale + new Vector2(u0, v0);
        var xz = Right * uv.X - Up * uv.Y;
        return new Vector3(xz.X, 0, xz.Y);
    }

    /// <summary>Highest reachable floor at (x, z), or null (walls, crates, outside).</summary>
    public float? TopAt(float x, float z)
    {
        float u = x * Right.X + z * Right.Y, v = -(x * Up.X + z * Up.Y);
        int px = (int)((u - u0) * Px), py = (int)((v - v0) * Px);
        if (px < 0 || py < 0 || px >= w || py >= h) return null;
        float t = top[py * w + px];
        return float.IsNaN(t) ? null : t;
    }

    /// <summary>The floor point under a screen point, snapped to the nearest floor within <paramref name="maxM"/> metres.</summary>
    public Vector3? FloorAt(Vector2 screen, float maxM = 1.2f)
    {
        var p = ToWorldFlat(screen);
        if (TopAt(p.X, p.Z) is { } y) return new Vector3(p.X, y, p.Z);
        float u = p.X * Right.X + p.Z * Right.Y, v = -(p.X * Up.X + p.Z * Up.Y);
        int cx = (int)((u - u0) * Px), cy = (int)((v - v0) * Px), r = (int)(maxM * Px);
        float bd = float.MaxValue; Vector3? best = null;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int px = cx + dx, py = cy + dy;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                float t = top[py * w + px];
                if (float.IsNaN(t)) continue;
                float d = dx * dx + dy * dy;
                if (d >= bd || d > r * r) continue;
                bd = d;
                float uu = u0 + (px + 0.5f) / Px, vv = v0 + (py + 0.5f) / Px;
                var xz = Right * uu - Up * vv;
                best = new Vector3(xz.X, t, xz.Y);
            }
        return best;
    }

    public void DrawBase(CanvasItem ci, float alpha = 1f) => ci.DrawTextureRect(Texture, Area, false, new Color(1, 1, 1, alpha));

    // ---------------- drawing helpers (screen space) ----------------

    public static void DashedCircle(CanvasItem ci, Vector2 c, float r, Color col, float width, int dashes = 24)
    {
        float step = Mathf.Tau / dashes;
        for (int i = 0; i < dashes; i++)
            ci.DrawArc(c, r, i * step, i * step + step * 0.55f, 6, col, width, true);
    }

    public static void Label(CanvasItem ci, string text, Vector2 at, int fs, Color col, Font? font = null)
    {
        font ??= UiTheme.HudWide;
        float tw = Gfx.TextW(font, text, fs);
        var pos = new Vector2(at.X - tw / 2, at.Y + fs * 0.36f);
        ci.DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fs, Math.Max(2, fs / 5), new Color(0.02f, 0.03f, 0.05f, 0.85f * col.A));
        ci.DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, col);
    }

    /// <summary>Player arrow at a screen point, pointing along a screen direction.</summary>
    public static void Arrow(CanvasItem ci, Vector2 at, Vector2 dir, float size, Color col)
    {
        if (!at.IsFinite() || !dir.IsFinite() || dir.LengthSquared() < 0.25f || size < 1f) return;
        dir = dir.Normalized();
        var perp = new Vector2(-dir.Y, dir.X);
        Span<Vector2> p = stackalloc Vector2[4];
        p[0] = at + dir * size;
        p[1] = at - dir * size * 0.6f + perp * size * 0.65f;
        p[2] = at - dir * size * 0.25f;
        p[3] = at - dir * size * 0.6f - perp * size * 0.65f;
        ci.FillPoly(p, col);
    }
}
