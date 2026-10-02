using Godot;
using ValTrainer.Game;
using ValTrainer.Maps;

namespace ValTrainer.World;

/// <summary>
/// Turns collision boxes into a dressed environment. Rules that keep gameplay intact:
/// every decorative piece inside the play space is either inside a collision box or a thin (≤ 12 cm)
/// relief on a wall face (bases, copings, windows, lamps, neon); free-standing set dressing only goes
/// outside the bounds of all solids (backdrop buildings, mountains, ground plane).
/// </summary>
sealed class WorldBuilder
{
    readonly Theme th;
    readonly Node3D root;
    readonly MeshBatch batch = new();
    readonly List<Box> solids;
    readonly List<Vector2> avoid = new();
    readonly Random rng;
    readonly List<(Transform3D, Color)> prisms = new(), cones = new(), domes = new();
    Vector3 lo, hi;

    public WorldBuilder(Theme th, Node3D root, List<Box> solids, int seed)
    {
        this.th = th;
        this.root = root;
        this.solids = solids;
        rng = new Random(seed);
        lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        hi = -lo;
        foreach (var b in solids) { lo = Min(lo, b.Min); hi = Max(hi, b.Max); }
    }

    public Aabb Bounds => new(lo, hi - lo);

    public void Avoid(Vector3 p) => avoid.Add(new Vector2(p.X, p.Z));

    static Vector3 Min(Vector3 a, Vector3 b) => new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
    static Vector3 Max(Vector3 a, Vector3 b) => new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));
    static Material M(Surface s) => SurfaceLib.Get(s);
    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ queries

    bool InsideSolid(Vector3 p, float m = 0.01f)
    {
        foreach (var b in solids)
            if (p.X > b.Min.X + m && p.X < b.Max.X - m && p.Y > b.Min.Y + m && p.Y < b.Max.Y - m && p.Z > b.Min.Z + m && p.Z < b.Max.Z - m)
                return true;
        return false;
    }

    /// <summary>Something to stand on within 3 m below p.</summary>
    bool Supported(Vector3 p)
    {
        foreach (var b in solids)
            if (p.X >= b.Min.X && p.X <= b.Max.X && p.Z >= b.Min.Z && p.Z <= b.Max.Z && b.Max.Y <= p.Y + 0.01f && b.Max.Y >= p.Y - 3f)
                return true;
        return false;
    }

    /// <summary>A spot a player could occupy (open air above walkable ground).</summary>
    bool Open(Vector3 p) => !InsideSolid(p) && Supported(p);

    /// <summary>The box rests on the ground or on another solid (its underside is never seen).</summary>
    bool Grounded(Box b)
    {
        if (b.Min.Y <= 0.01f) return true;
        foreach (var o in solids)
            if (o != b && Mathf.Abs(o.Max.Y - b.Min.Y) < 0.02f && o.Min.X < b.Max.X && o.Max.X > b.Min.X && o.Min.Z < b.Max.Z && o.Max.Z > b.Min.Z)
                return true;
        return false;
    }

    bool NearAvoid(Vector3 p, float r)
    {
        var q = new Vector2(p.X, p.Z);
        foreach (var a in avoid) if (a.DistanceSquaredTo(q) < r * r) return true;
        return false;
    }

    /// <summary>Per-object colour jitter (only on parts that never overlap other objects' coplanar faces).</summary>
    Color Jitter(float amount)
    {
        float k = 1f + R(-amount, amount);
        return new Color(k * (1f + R(-amount, amount) * 0.3f), k, k * (1f + R(-amount, amount) * 0.3f));
    }

    // ------------------------------------------------------------------ core surfaces

    public void Floor(Box b)
    {
        bool raised = b.Size.Y > 0.25f;
        batch.Box(M(th.Floor), b, Faces.PosY, shadow: raised, grime: false);
        if (raised) batch.Box(M(th.Base), b, Faces.Sides);
    }

    public void Wall(Box b, bool decorate = true)
    {
        float h = b.Size.Y;
        bool grounded = Grounded(b);
        bool tall = h > 2.5f;
        float capH = tall ? Mathf.Min(th.CapH, h * 0.12f) : 0f;
        float baseH = grounded && tall ? Mathf.Min(th.BaseH, h * 0.3f) : 0f;
        var faces = Faces.All;
        if (grounded) faces &= ~Faces.NegY;
        if (capH > 0) faces &= ~Faces.PosY;
        batch.Box(M(th.Wall), b, faces);

        var mn = b.Min; var mx = b.Max;
        if (baseH > 0) Band(th.Base, b, mn.Y, mn.Y + baseH, 0.025f, Faces.Sides | Faces.PosY);
        if (capH > 0) Band(th.Cap, b, mx.Y - capH, mx.Y, 0.045f, Faces.All);
        if (!decorate || !tall) return;

        switch (th.Style)
        {
            case DecorStyle.Haven when h >= 3.6f:
                float top = mx.Y - capH;
                Band(th.Cap with { Tint = new Color(0.55f, 0.13f, 0.1f) }, b, top - 0.38f, top, 0.03f, Faces.Sides | Faces.NegY);
                Band(Gold, b, top - 0.46f, top - 0.38f, 0.036f, Faces.Sides | Faces.NegY | Faces.PosY);
                break;
            case DecorStyle.Range:
                Band(RangeOrange, b, mn.Y + baseH, mn.Y + baseH + 0.22f, 0.032f, Faces.Sides | Faces.PosY | Faces.NegY);
                break;
        }
    }

    /// <summary>A horizontal band wrapping a box's sides, sticking out by <paramref name="e"/>.</summary>
    void Band(Surface s, Box b, float y0, float y1, float e, Faces faces) =>
        batch.Box(M(s), new Vector3(b.Min.X - e, y0, b.Min.Z - e), new Vector3(b.Max.X + e, y1, b.Max.Z + e), faces);

    void Band(Material m, Box b, float y0, float y1, float e, Faces faces) =>
        batch.Box(m, new Vector3(b.Min.X - e, y0, b.Min.Z - e), new Vector3(b.Max.X + e, y1, b.Max.Z + e), faces, shadow: false);

    public void Trim(Box b) => batch.Box(M(th.Trim), b, Grounded(b) ? Faces.All & ~Faces.NegY : Faces.All);

    public void Elevated(Box b) => batch.Box(M(th.Elevated), b, Faces.All);

    /// <summary>Crate: inset body + edge frame, all inside the collision box.</summary>
    public void Crate(Box b, Surface body, Surface frame)
    {
        var s = b.Size;
        float t = Mathf.Clamp(Mathf.Min(s.X, Mathf.Min(s.Y, s.Z)) * 0.07f, 0.05f, 0.11f);
        float ins = Mathf.Min(0.03f, t * 0.4f);
        var keep = Grounded(b) ? Faces.All & ~Faces.NegY : Faces.All;
        var col = Jitter(0.06f);
        var mn = b.Min; var mx = b.Max;
        batch.Box(M(body), mn + new Vector3(ins, Grounded(b) ? 0 : ins, ins), mx - new Vector3(ins, ins, ins), keep, col);
        var fm = M(frame);
        float[] xs = { mn.X, mx.X - t }, zs = { mn.Z, mx.Z - t };
        foreach (var x in xs)
            foreach (var z in zs)
                batch.Box(fm, new Vector3(x, mn.Y, z), new Vector3(x + t, mx.Y, z + t), keep, col);
        float[] ys = s.Y > 2.0f ? new[] { mn.Y, (mn.Y + mx.Y - t) / 2, mx.Y - t } : new[] { mn.Y, mx.Y - t };
        foreach (var y in ys)
        {
            foreach (var z in zs) batch.Box(fm, new Vector3(mn.X + t, y, z), new Vector3(mx.X - t, y + t, z + t), keep, col);
            foreach (var x in xs) batch.Box(fm, new Vector3(x, y, mn.Z + t), new Vector3(x + t, y + t, mx.Z - t), keep, col);
        }
    }

    // ------------------------------------------------------------------ wall props

    static readonly StandardMaterial3D Glass = SurfaceLib.Flat(new Color(0.07f, 0.09f, 0.11f), 0.12f, 0.3f);
    static readonly StandardMaterial3D Iron = SurfaceLib.Flat(new Color(0.07f, 0.07f, 0.075f), 0.45f, 0.6f);
    static readonly StandardMaterial3D Gold = SurfaceLib.Flat(new Color(0.83f, 0.62f, 0.24f), 0.35f, 0.8f);
    static readonly StandardMaterial3D LampGlass = SurfaceLib.Glow(new Color(1f, 0.78f, 0.48f), 1.6f);
    static readonly StandardMaterial3D RangeOrange = SurfaceLib.Flat(new Color(0.95f, 0.42f, 0.1f), 0.6f);
    static readonly StandardMaterial3D NeonPink = SurfaceLib.Glow(new Color(1f, 0.18f, 0.55f), 3f);
    static readonly StandardMaterial3D NeonCyan = SurfaceLib.Glow(new Color(0.15f, 0.85f, 1f), 3f);
    static readonly StandardMaterial3D SignWarm = SurfaceLib.Glow(new Color(1f, 0.85f, 0.6f), 2.2f);
    static readonly StandardMaterial3D DarkPanel = SurfaceLib.Flat(new Color(0.06f, 0.065f, 0.075f), 0.5f, 0.2f);

    /// <summary>One vertical face of a wall box (axis 0 = X face, 2 = Z face).</summary>
    readonly record struct WallFace(Box Wall, int Axis, int Sign)
    {
        public int T => Axis == 0 ? 2 : 0;
        public float Plane => Sign > 0 ? Wall.Max[Axis] : Wall.Min[Axis];
        public float Lo => Wall.Min[T];
        public float Hi => Wall.Max[T];
        public float Y0 => Wall.Min.Y;
        public float Y1 => Wall.Max.Y;

        public Vector3 Point(float u, float y, float outward)
        {
            var p = new Vector3(0, y, 0);
            p[Axis] = Plane + Sign * outward;
            p[T] = u;
            return p;
        }

        /// <summary>Box on the face: u0..u1 along it, y0..y1, from out0 to out1 metres off the surface.</summary>
        public Box Piece(float u0, float u1, float y0, float y1, float out0, float out1)
        {
            var a = Point(u0, y0, out0); var b = Point(u1, y1, out1);
            return new Box(Min(a, b), Max(a, b));
        }
    }

    bool OpenAt(WallFace f, float u, float halfWidth)
    {
        float y = f.Y0 + 1.0f;
        return Open(f.Point(u, y, 0.6f)) && Open(f.Point(u - halfWidth, y, 0.6f)) && Open(f.Point(u + halfWidth, y, 0.6f));
    }

    /// <summary>Hangs style props on every wall face that looks into the play space.</summary>
    public void DecorateWalls(IEnumerable<Box> walls)
    {
        foreach (var w in walls)
        {
            if (w.Size.Y < 3.4f || !Grounded(w)) continue;
            for (int axis = 0; axis <= 2; axis += 2)
                for (int s = -1; s <= 1; s += 2)
                {
                    var f = new WallFace(w, axis, s);
                    if (f.Hi - f.Lo >= 2.4f) DecorateFace(f);
                }
        }
    }

    void DecorateFace(WallFace f)
    {
        float len = f.Hi - f.Lo, h = f.Y1 - f.Y0;
        float step = th.Style == DecorStyle.Split ? 4.2f : 3.6f;
        int n = Mathf.Max(1, (int)((len - 0.8f) / step));
        float off = (len - n * step) / 2f;
        bool door = false;
        int seed = (int)(f.Plane * 31 + f.Lo * 17 + f.Axis * 7 + f.Sign);
        var local = new Random(seed);

        if (th.Style == DecorStyle.Split) NeonRun(f, local.Next(2) == 0 ? NeonPink : NeonCyan);

        for (int i = 0; i < n; i++)
        {
            float u = f.Lo + off + step * (i + 0.5f);
            if (!OpenAt(f, u, 0.7f) || NearAvoid(f.Point(u, 0, 0.6f), 1.6f)) continue;
            double r = local.NextDouble();
            switch (th.Style)
            {
                case DecorStyle.Ascent:
                case DecorStyle.Bind:
                case DecorStyle.Haven:
                    if (!door && r < 0.18 && i > 0) { Door(f, u); door = true; }
                    else if (h >= 4.2f && r < 0.85) Window(f, u);
                    if (h >= 3.6f && i < n - 1 && local.NextDouble() < 0.55 && OpenAt(f, u + step / 2, 0.2f)) Lamp(f, u + step / 2);
                    break;
                case DecorStyle.Split:
                    if (r < 0.4 && h >= 4.2f) NeonSign(f, u, local);
                    else if (r < 0.7) Vent(f, u);
                    else Pipe(f, u + 0.8f);
                    break;
            }
        }
    }

    void Window(WallFace f, float u)
    {
        float w = th.Style == DecorStyle.Haven ? 1.1f : 0.9f, hgt = 1.3f, y = f.Y0 + 2.35f;
        if (y + hgt > f.Y1 - th.CapH - (th.Style == DecorStyle.Haven ? 0.5f : 0.15f)) y = f.Y1 - th.CapH - hgt - (th.Style == DecorStyle.Haven ? 0.55f : 0.2f);
        if (y < f.Y0 + 1.6f) return;
        Add(Glass, f.Piece(u - w / 2, u + w / 2, y, y + hgt, 0, 0.02f));
        float t = 0.09f;
        var frame = th.Style switch
        {
            DecorStyle.Bind => M(new Surface("square_tiles", new Color(0.18f, 0.5f, 0.52f), 0.9f, 0.6f, 0.9f, Grime: 0)),
            DecorStyle.Haven => M(th.Trim),
            _ => M(th.Base),
        };
        Add(frame, f.Piece(u - w / 2 - t, u + w / 2 + t, y + hgt, y + hgt + t, 0, 0.05f));
        Add(frame, f.Piece(u - w / 2 - t - 0.04f, u + w / 2 + t + 0.04f, y - 0.08f, y, 0, 0.09f));
        Add(frame, f.Piece(u - w / 2 - t, u - w / 2, y, y + hgt, 0, 0.05f));
        Add(frame, f.Piece(u + w / 2, u + w / 2 + t, y, y + hgt, 0, 0.05f));
        switch (th.Style)
        {
            case DecorStyle.Ascent:
                var shutter = M(new Surface("wood_planks", ShutterColor(u), 0.9f, 0.7f, 0.8f, Grime: 0));
                Add(shutter, f.Piece(u - w / 2 - t - 0.46f, u - w / 2 - t, y, y + hgt, 0, 0.035f));
                Add(shutter, f.Piece(u + w / 2 + t, u + w / 2 + t + 0.46f, y, y + hgt, 0, 0.035f));
                break;
            case DecorStyle.Bind:
            case DecorStyle.Haven:
                var bar = th.Style == DecorStyle.Bind ? M(th.CoverFrame) : M(th.Trim);
                for (int k = 1; k <= 2; k++)
                {
                    float uu = u - w / 2 + w * k / 3f, yy = y + hgt * k / 3f;
                    Add(bar, f.Piece(uu - 0.025f, uu + 0.025f, y, y + hgt, 0, 0.035f));
                    Add(bar, f.Piece(u - w / 2, u + w / 2, yy - 0.025f, yy + 0.025f, 0, 0.035f));
                }
                if (th.Style == DecorStyle.Haven)
                    Add(SurfaceLib.Flat(new Color(0.55f, 0.13f, 0.1f), 0.7f), f.Piece(u - w / 2 - 0.2f, u + w / 2 + 0.2f, y + hgt + t, y + hgt + t + 0.16f, 0, 0.07f));
                break;
        }
    }

    Color ShutterColor(float u)
    {
        var options = new[] { new Color(0.24f, 0.42f, 0.34f), new Color(0.2f, 0.36f, 0.42f), new Color(0.45f, 0.3f, 0.2f) };
        return options[Mathf.Abs((int)(u * 3.7f)) % options.Length];
    }

    void Door(WallFace f, float u)
    {
        float w = 1.2f, hgt = th.Style == DecorStyle.Haven ? 2.15f : 2.3f;
        var paint = th.Style switch
        {
            DecorStyle.Bind => new Color(0.16f, 0.42f, 0.48f),
            DecorStyle.Haven => new Color(0.5f, 0.14f, 0.1f),
            _ => new Color(0.36f, 0.24f, 0.16f),
        };
        var door = M(new Surface("wood_planks", paint, 0.85f, 1.0f, 0.8f, Grime: 0.15f));
        Add(door, f.Piece(u - w / 2, u + w / 2, f.Y0, f.Y0 + hgt, 0, 0.03f));
        var frame = th.Style == DecorStyle.Bind ? M(th.Trim) : th.Style == DecorStyle.Haven ? M(th.CoverFrame) : M(th.Base);
        Add(frame, f.Piece(u - w / 2 - 0.12f, u - w / 2, f.Y0, f.Y0 + hgt + 0.12f, 0, 0.06f));
        Add(frame, f.Piece(u + w / 2, u + w / 2 + 0.12f, f.Y0, f.Y0 + hgt + 0.12f, 0, 0.06f));
        Add(frame, f.Piece(u - w / 2, u + w / 2, f.Y0 + hgt, f.Y0 + hgt + 0.12f, 0, 0.06f));
        Add(Iron, f.Piece(u + w / 2 - 0.2f, u + w / 2 - 0.12f, f.Y0 + 1.0f, f.Y0 + 1.12f, 0.03f, 0.07f));
    }

    void Lamp(WallFace f, float u)
    {
        float y = Mathf.Min(f.Y0 + 3.05f, f.Y1 - th.CapH - 0.6f);
        if (y < f.Y0 + 2.3f) return;
        var metal = th.Style == DecorStyle.Bind ? Gold : Iron;
        Add(metal, f.Piece(u - 0.05f, u + 0.05f, y + 0.12f, y + 0.4f, 0, 0.04f));
        Add(metal, f.Piece(u - 0.03f, u + 0.03f, y + 0.3f, y + 0.36f, 0.04f, 0.11f));
        Add(metal, f.Piece(u - 0.1f, u + 0.1f, y + 0.22f, y + 0.27f, 0.0f, 0.12f));
        Add(LampGlass, f.Piece(u - 0.08f, u + 0.08f, y, y + 0.22f, 0.01f, 0.11f));
    }

    void NeonSign(WallFace f, float u, Random local)
    {
        float w = 1.5f, hgt = 0.75f, y = f.Y0 + 2.5f;
        Add(DarkPanel, f.Piece(u - w / 2, u + w / 2, y, y + hgt, 0, 0.05f));
        var lit = local.Next(3) switch { 0 => NeonPink, 1 => NeonCyan, _ => SignWarm };
        float pad = 0.12f;
        Add(lit, f.Piece(u - w / 2 + pad, u + w / 2 - pad, y + hgt - pad - 0.1f, y + hgt - pad, 0.05f, 0.06f));
        Add(lit, f.Piece(u - w / 2 + pad, u + w / 2 * 0.35f, y + pad + 0.12f, y + pad + 0.2f, 0.05f, 0.06f));
        Add(lit, f.Piece(u - w / 2 + pad, u + w / 2 * 0.1f, y + pad, y + pad + 0.07f, 0.05f, 0.06f));
    }

    void Vent(WallFace f, float u)
    {
        float y = f.Y0 + 2.6f;
        var plate = M(th.Trim);
        Add(plate, f.Piece(u - 0.5f, u + 0.5f, y, y + 0.7f, 0, 0.05f));
        for (int k = 0; k < 5; k++)
        {
            float yy = y + 0.1f + k * 0.11f;
            Add(DarkPanel, f.Piece(u - 0.42f, u + 0.42f, yy, yy + 0.05f, 0.05f, 0.06f));
        }
    }

    void Pipe(WallFace f, float u)
    {
        if (!OpenAt(f, u, 0.1f)) return;
        Add(M(th.Trim), f.Piece(u - 0.06f, u + 0.06f, f.Y0, f.Y1 - th.CapH, 0.02f, 0.12f));
        for (float y = f.Y0 + 1.2f; y < f.Y1 - 0.5f; y += 1.6f)
            Add(Iron, f.Piece(u - 0.09f, u + 0.09f, y, y + 0.06f, 0, 0.12f));
    }

    void NeonRun(WallFace f, Material neon)
    {
        float y = Mathf.Min(f.Y0 + 3.6f, f.Y1 - th.CapH - 0.35f);
        if (y < f.Y0 + 2.6f) return;
        float seg = 1.0f;
        for (float u = f.Lo + 0.3f; u + seg <= f.Hi - 0.3f + 1e-3f; u += seg)
        {
            if (!OpenAt(f, u + seg / 2, 0.45f)) continue;
            Add(neon, f.Piece(u, u + seg, y, y + 0.06f, 0, 0.035f));
            Add(DarkPanel, f.Piece(u, u + seg, y - 0.03f, y + 0.09f, 0, 0.02f));
        }
    }

    void Add(Material m, Box b) => batch.Box(m, b, Faces.All, shadow: false, grime: false);

    // ------------------------------------------------------------------ range

    public void RangeDressing(float halfWidth, float front, float back)
    {
        var orange = RangeOrange;
        var yellow = SurfaceLib.Flat(new Color(0.98f, 0.78f, 0.16f), 0.6f);
        var white = SurfaceLib.Flat(new Color(0.85f, 0.86f, 0.88f), 0.7f);
        // Distance lines every 10 m (the player stands at z = 0), short ticks every 5 m.
        for (int d = 10; d < -front; d += 10)
            batch.FloorQuad(orange, -halfWidth + 0.6f, -d - 0.1f, halfWidth - 0.6f, -d + 0.1f, 0.004f);
        for (int d = 5; d < -front; d += 10)
            batch.FloorQuad(yellow, -1.2f, -d - 0.06f, 1.2f, -d + 0.06f, 0.004f);
        // Lane edges.
        foreach (var x in new[] { -halfWidth + 3f, halfWidth - 3f })
            batch.FloorQuad(white, x - 0.06f, front + 1, x + 0.06f, back - 1, 0.003f);
        // Spawn pad.
        const float P = 1.6f, W = 0.08f;
        batch.FloorQuad(SurfaceLib.Flat(new Color(0.16f, 0.17f, 0.2f), 0.8f), -P, -P, P, P, 0.002f);
        batch.FloorQuad(orange, -P, -P, P, -P + W, 0.004f);
        batch.FloorQuad(orange, -P, P - W, P, P, 0.004f);
        batch.FloorQuad(orange, -P, -P + W, -P + W, P - W, 0.004f);
        batch.FloorQuad(orange, P - W, -P + W, P, P - W, 0.004f);

        for (int d = 10; d < -front; d += 10)
            foreach (var x in new[] { -6.5f, 6.5f })
                root.AddChild(new Label3D
                {
                    Text = $"{d}M", FontSize = 120, PixelSize = 0.008f, Modulate = new Color(0.98f, 0.55f, 0.15f),
                    OutlineSize = 0, Shaded = false, AlphaCut = Label3D.AlphaCutMode.Discard,
                    Position = new Vector3(x, 0.01f, -d + 0.75f), RotationDegrees = new Vector3(-90, 0, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });

        // Panel seams and flood-light panels on the inside faces of the walls.
        var seam = M(th.Base);
        float baseTop = th.BaseH + 0.22f, top = 16f - th.CapH;
        for (float x = -halfWidth + 8; x < halfWidth - 1; x += 8)
        {
            Add(seam, new Box(new Vector3(x - 0.07f, baseTop, front), new Vector3(x + 0.07f, top, front + 0.02f)));
            Add(seam, new Box(new Vector3(x - 0.07f, baseTop, back - 0.02f), new Vector3(x + 0.07f, top, back)));
        }
        for (float z = front + 8; z < back - 1; z += 8)
        {
            Add(seam, new Box(new Vector3(-halfWidth, baseTop, z - 0.07f), new Vector3(-halfWidth + 0.02f, top, z + 0.07f)));
            Add(seam, new Box(new Vector3(halfWidth - 0.02f, baseTop, z - 0.07f), new Vector3(halfWidth, top, z + 0.07f)));
        }
        var flood = SurfaceLib.Glow(new Color(0.9f, 0.95f, 1f), 1.4f);
        for (float x = -halfWidth + 12; x < halfWidth - 4; x += 16)
        {
            Add(DarkPanel, new Box(new Vector3(x - 1.2f, 11.6f, front), new Vector3(x + 1.2f, 12.4f, front + 0.1f)));
            Add(flood, new Box(new Vector3(x - 1.05f, 11.7f, front + 0.1f), new Vector3(x + 1.05f, 12.3f, front + 0.12f)));
        }
        // Big orange stripe high on the far wall (reads as the range's back board).
        Add(orange, new Box(new Vector3(-halfWidth, 7.0f, front), new Vector3(halfWidth, 7.35f, front + 0.03f)));
    }

    // ------------------------------------------------------------------ outside the play space

    /// <summary>Ground plane, a ring of buildings hugging the outer walls, distant mountains.</summary>
    public void Backdrop(bool buildings)
    {
        var c = (lo + hi) / 2;
        batch.Box(M(th.GroundSurf), new Vector3(c.X - 420, -0.07f, c.Z - 420), new Vector3(c.X + 420, -0.05f, c.Z + 420), Faces.PosY, shadow: false, grime: false);
        float wallTop = 0;
        foreach (var b in solids) if (b.Max.Y < 12f) wallTop = Mathf.Max(wallTop, b.Max.Y);

        if (buildings)
        {
            Ring(0.4f, 3f, wallTop, th.MinH, th.MaxH, 6f, 13f);
            if (th.Style == DecorStyle.Split) Ring(18f, 30f, wallTop, 26f, 55f, 10f, 22f);
            else Ring(14f, 26f, wallTop, th.MinH + 3f, th.MaxH + 6f, 8f, 16f);
        }
        if (th.Mountains)
        {
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Tau * (i + R(-0.3f, 0.3f)) / n, dist = R(240f, 320f);
                float rad = R(60f, 110f), hgt = R(35f, 85f);
                var pos = new Vector3(c.X + Mathf.Cos(a) * dist, -2f, c.Z + Mathf.Sin(a) * dist);
                var t = new Transform3D(new Basis(Vector3.Up, R(0, Mathf.Tau)) * Basis.FromScale(new Vector3(rad, hgt, rad * R(0.7f, 1.2f))), pos + new Vector3(0, hgt / 2, 0));
                cones.Add((t, (th.MountainColor * R(0.85f, 1.1f)) with { A = 1 }));
            }
        }
    }

    /// <summary>Buildings around the bounds, <paramref name="d0"/>..<paramref name="d1"/> m beyond them.</summary>
    void Ring(float d0, float d1, float wallTop, float minH, float maxH, float minW, float maxW)
    {
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            int sign = side % 2 == 0 ? -1 : 1;
            float start = (alongX ? lo.X : lo.Z) - 8f, end = (alongX ? hi.X : hi.Z) + 8f;
            float edge = alongX ? (sign < 0 ? lo.Z : hi.Z) : (sign < 0 ? lo.X : hi.X);
            float u = start + R(0, 3);
            while (u < end)
            {
                float w = R(minW, maxW), depth = R(7f, 14f), off = R(d0, d1);
                float h = Mathf.Max(wallTop + R(1.5f, 4f), R(minH, maxH));
                float a0 = edge + sign * off, a1 = edge + sign * (off + depth);
                Vector3 mn, mx;
                if (alongX) { mn = new Vector3(u, 0, Mathf.Min(a0, a1)); mx = new Vector3(u + w, h, Mathf.Max(a0, a1)); }
                else { mn = new Vector3(Mathf.Min(a0, a1), 0, u); mx = new Vector3(Mathf.Max(a0, a1), h, u + w); }
                Building(new Box(mn, mx), alongX ? 2 : 0, -sign);
                u += w + R(0.3f, 2.5f);
            }
        }
    }

    /// <summary>Backdrop building; <paramref name="axis"/>/<paramref name="facing"/> = the facade that faces the map.</summary>
    void Building(Box b, int axis, int facing)
    {
        var hue = th.Hues[rng.Next(th.Hues.Length)].SrgbToLinear();   // vertex colours are linear
        batch.Box(M(th.Backdrop), b, Faces.All & ~Faces.NegY, hue);
        var mn = b.Min; var mx = b.Max;
        var roof = M(th.Roof);
        switch (th.Style)
        {
            case DecorStyle.Ascent:
            case DecorStyle.Haven:
                batch.Box(roof, new Vector3(mn.X - 0.35f, mx.Y, mn.Z - 0.35f), new Vector3(mx.X + 0.35f, mx.Y + 0.25f, mx.Z + 0.35f), Faces.All);
                float span = axis == 2 ? b.Size.X : b.Size.Z, len = axis == 2 ? b.Size.Z : b.Size.X;
                float rh = th.Style == DecorStyle.Haven ? span * 0.38f : span * 0.24f;
                var basis = new Basis(Vector3.Up, axis == 2 ? 0 : Mathf.Pi / 2) * Basis.FromScale(new Vector3(span + 0.7f, rh, len + 0.7f));
                var roofColor = th.Style == DecorStyle.Haven ? new Color(0.36f, 0.16f, 0.12f) : new Color(0.64f, 0.3f, 0.2f);
                prisms.Add((new Transform3D(basis, new Vector3((mn.X + mx.X) / 2, mx.Y + 0.25f + rh / 2, (mn.Z + mx.Z) / 2)), (roofColor * R(0.9f, 1.08f)) with { A = 1 }));
                if (th.Style == DecorStyle.Haven)
                    batch.Box(SurfaceLib.Flat(new Color(0.5f, 0.13f, 0.1f), 0.7f), new Vector3(mn.X - 0.04f, mx.Y - 0.9f, mn.Z - 0.04f), new Vector3(mx.X + 0.04f, mx.Y - 0.3f, mx.Z + 0.04f), Faces.Sides);
                break;
            case DecorStyle.Bind:
                batch.Box(roof, new Vector3(mn.X - 0.05f, mx.Y, mn.Z - 0.05f), new Vector3(mx.X + 0.05f, mx.Y + 0.35f, mx.Z + 0.05f), Faces.All);
                if (rng.NextDouble() < 0.18)
                {
                    float r = Mathf.Min(b.Size.X, b.Size.Z) * 0.35f;
                    domes.Add((new Transform3D(Basis.FromScale(new Vector3(r, r * 1.1f, r)), new Vector3((mn.X + mx.X) / 2, mx.Y + 0.35f, (mn.Z + mx.Z) / 2)),
                        rng.Next(2) == 0 ? new Color(0.2f, 0.52f, 0.55f) : new Color(0.93f, 0.9f, 0.84f)));
                }
                break;
            default:
                batch.Box(roof, new Vector3(mn.X - 0.05f, mx.Y, mn.Z - 0.05f), new Vector3(mx.X + 0.05f, mx.Y + 0.6f, mx.Z + 0.05f), Faces.All);
                break;
        }

        // Windows on the facade that faces the map (only the upper part is seen over the walls).
        var f = new WallFace(b, axis, facing);
        float width = f.Hi - f.Lo;
        if (th.Style == DecorStyle.Split)
        {
            for (float y = 3.5f; y < b.Max.Y - 2f; y += 3.6f)
                batch.Box(Glass, f.Piece(f.Lo + 0.5f, f.Hi - 0.5f, y, y + 1.7f, 0, 0.04f), Faces.All, shadow: false, grime: false);
            if (rng.NextDouble() < 0.35)
            {
                float y = b.Max.Y - R(3f, 6f), u = (f.Lo + f.Hi) / 2;
                var neon = rng.Next(2) == 0 ? NeonPink : NeonCyan;
                batch.Box(DarkPanel, f.Piece(u - 2.2f, u + 2.2f, y - 1.6f, y, 0.04f, 0.2f), Faces.All, shadow: false, grime: false);
                batch.Box(neon, f.Piece(u - 1.9f, u + 1.9f, y - 1.3f, y - 0.3f, 0.2f, 0.24f), Faces.All, shadow: false, grime: false);
            }
            return;
        }
        int cols = Mathf.Max(1, (int)((width - 1.2f) / 2.6f));
        float pad = (width - (cols - 1) * 2.6f) / 2;
        var shutter = M(new Surface("wood_planks", new Color(0.24f, 0.42f, 0.34f), 0.9f, 0.7f, 0.8f, Grime: 0));
        for (float y = 3.0f; y < b.Max.Y - 1.8f; y += 3.2f)
            for (int k = 0; k < cols; k++)
            {
                if (rng.NextDouble() < 0.15) continue;
                float u = f.Lo + pad + k * 2.6f;
                batch.Box(Glass, f.Piece(u - 0.45f, u + 0.45f, y, y + 1.4f, 0, 0.04f), Faces.All, shadow: false, grime: false);
                if (th.Style == DecorStyle.Ascent)
                {
                    batch.Box(shutter, f.Piece(u - 0.95f, u - 0.47f, y, y + 1.4f, 0, 0.05f), Faces.All, shadow: false, grime: false);
                    batch.Box(shutter, f.Piece(u + 0.47f, u + 0.95f, y, y + 1.4f, 0, 0.05f), Faces.All, shadow: false, grime: false);
                }
                else if (th.Style == DecorStyle.Haven)
                    batch.Box(M(th.Trim), f.Piece(u - 0.6f, u + 0.6f, y + 1.4f, y + 1.6f, 0, 0.08f), Faces.All, shadow: false, grime: false);
            }
    }

    // ------------------------------------------------------------------ commit

    public void Commit()
    {
        batch.Commit(root, "World");
        Instances(prisms, new PrismMesh { LeftToRight = 0.5f, Size = Vector3.One }, "Roofs", true);
        Instances(cones, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 1f, Height = 1f, RadialSegments = 9, Rings = 1, CapTop = false, CapBottom = false }, "Mountains", false);
        Instances(domes, new SphereMesh { Radius = 1f, Height = 2f, IsHemisphere = true, RadialSegments = 24, Rings = 8 }, "Domes", true);
    }

    void Instances(List<(Transform3D T, Color C)> list, Mesh mesh, string name, bool shadow)
    {
        if (list.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
        for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i].T); mm.SetInstanceColor(i, list[i].C); }
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f };
        root.AddChild(new MultiMeshInstance3D
        {
            Name = name, Multimesh = mm, MaterialOverride = mat,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
