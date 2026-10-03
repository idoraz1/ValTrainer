using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Looks of the sentinel devices in the Site Anchor drill, built from primitive meshes in code (no external art): small
/// emissive gadgets in the agent's colour, translucent ground fields, wall slabs, beams and the placement preview.
/// </summary>
public static class SentinelFx
{
    static readonly Dictionary<(Color, float, float), StandardMaterial3D> mats = new();

    /// <summary>A shared material: <paramref name="glow"/> = emission strength, <paramref name="alpha"/> &lt; 1 = translucent, unshaded.</summary>
    public static StandardMaterial3D Mat(Color c, float glow = 0.5f, float alpha = 1f)
    {
        var key = (c, glow, alpha);
        if (mats.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = new Color(c, alpha), Roughness = 0.45f, Metallic = 0.3f };
        if (glow > 0) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = glow; }
        if (alpha < 1f)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            m.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
        }
        return mats[key] = m;
    }

    static readonly StandardMaterial3D dark = new() { AlbedoColor = new Color(0.12f, 0.13f, 0.15f), Roughness = 0.5f, Metallic = 0.6f };

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material m)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Sphere(Node3D parent, float r, Vector3 pos, Material m)
    {
        var mi = new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = 2 * r, RadialSegments = 16, Rings = 8 }, Position = pos, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Cyl(Node3D parent, float rTop, float rBottom, float h, Vector3 pos, Material m, int seg = 20)
    {
        var mi = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = rTop, BottomRadius = rBottom, Height = h, RadialSegments = seg, Rings = 1 }, Position = pos, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>Orthonormal basis whose Y axis points along <paramref name="dir"/>.</summary>
    public static Basis AlongY(Vector3 dir)
    {
        var y = dir.Normalized();
        var x = Mathf.Abs(y.Y) < 0.99f ? Vector3.Up.Cross(y).Normalized() : Vector3.Right;
        var z = x.Cross(y);
        return new Basis(x, y, z);
    }

    /// <summary>Basis with X along the horizontal part of <paramref name="dir"/>, Y up.</summary>
    public static Basis AlongX(Vector3 dir)
    {
        var x = new Vector3(dir.X, 0, dir.Z);
        x = x.LengthSquared() < 1e-6f ? Vector3.Right : x.Normalized();
        var y = Vector3.Up;
        return new Basis(x, y, x.Cross(y));
    }

    /// <summary>A gadget model rooted at its base (local +Y up). <paramref name="kind"/>: alarmbot, turret, anchor, sensor,
    /// orb, nest, trademark, choke, interceptor, shear.</summary>
    public static Node3D Gadget(Node3D world, string kind, Color c, Vector3 pos, Vector3 up, float yawDeg = 0f)
    {
        var n = new Node3D { Name = "Sentinel_" + kind };
        world.AddChild(n);
        n.GlobalTransform = new Transform3D(AlongY(up) * new Basis(Vector3.Up, Mathf.DegToRad(-yawDeg)), pos);
        var glow = Mat(c, 1.2f);
        var body = Mat(c.Lerp(new Color(0.85f, 0.85f, 0.85f), 0.55f), 0.15f);
        switch (kind)
        {
            case "alarmbot":
                Sphere(n, 0.17f, new Vector3(0, 0.19f, 0), body);
                Sphere(n, 0.07f, new Vector3(0, 0.24f, -0.13f), glow);
                Cyl(n, 0.12f, 0.15f, 0.05f, new Vector3(0, 0.03f, 0), dark);
                break;
            case "turret":
                Cyl(n, 0.08f, 0.22f, 0.35f, new Vector3(0, 0.17f, 0), dark);
                Box(n, new Vector3(0.34f, 0.2f, 0.36f), new Vector3(0, 0.45f, 0), body);
                Box(n, new Vector3(0.06f, 0.06f, 0.36f), new Vector3(0, 0.45f, -0.3f), dark);
                Box(n, new Vector3(0.24f, 0.05f, 0.02f), new Vector3(0, 0.5f, -0.19f), glow);
                break;
            case "anchor":
                Cyl(n, 0.07f, 0.09f, 0.08f, new Vector3(0, 0.04f, 0), body);
                Sphere(n, 0.04f, new Vector3(0, 0.1f, 0), glow);
                break;
            case "sensor":
                Cyl(n, 0.14f, 0.16f, 0.06f, new Vector3(0, 0.03f, 0), body);
                Cyl(n, 0.08f, 0.08f, 0.03f, new Vector3(0, 0.075f, 0), glow);
                break;
            case "orb":
                Sphere(n, 0.18f, new Vector3(0, 0.2f, 0), glow);
                Cyl(n, 0.2f, 0.25f, 0.06f, new Vector3(0, 0.03f, 0), dark);
                break;
            case "nest":
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 1.256f;
                    Sphere(n, 0.06f, new Vector3(Mathf.Cos(a) * 0.1f, 0.05f, Mathf.Sin(a) * 0.1f), body);
                }
                Sphere(n, 0.05f, new Vector3(0, 0.1f, 0), glow);
                break;
            case "trademark":
                Cyl(n, 0.1f, 0.2f, 0.12f, new Vector3(0, 0.06f, 0), body);
                Cyl(n, 0.07f, 0.07f, 0.05f, new Vector3(0, 0.14f, 0), glow);
                break;
            case "choke":
                Sphere(n, 0.14f, new Vector3(0, 0.1f, 0), body);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 1.047f;
                    Sphere(n, 0.05f, new Vector3(Mathf.Cos(a) * 0.14f, 0.12f, Mathf.Sin(a) * 0.14f), glow);
                }
                break;
            case "interceptor":
                var core = Sphere(n, 0.08f, new Vector3(0, 0f, 0), glow);
                core.Scale = new Vector3(0.8f, 1.4f, 0.8f);
                Cyl(n, 0.13f, 0.13f, 0.02f, Vector3.Zero, body);
                break;
            default:
                Box(n, new Vector3(0.25f, 0.06f, 0.25f), new Vector3(0, 0.03f, 0), body);
                Box(n, new Vector3(0.12f, 0.03f, 0.12f), new Vector3(0, 0.075f, 0), glow);
                break;
        }
        return n;
    }

    /// <summary>A thin glowing line (cylinder) from a to b.</summary>
    public static MeshInstance3D Beam(Node3D world, Vector3 a, Vector3 b, float radius, Color c, float alpha = 0.9f)
    {
        var mi = new MeshInstance3D
        {
            Name = "SentinelBeam",
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = Mat(c, 2f, alpha),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        world.AddChild(mi);
        SetBeam(mi, a, b);
        return mi;
    }

    public static void SetBeam(MeshInstance3D mi, Vector3 a, Vector3 b)
    {
        var d = b - a;
        float len = Mathf.Max(0.01f, d.Length());
        mi.GlobalTransform = new Transform3D(AlongY(d) * Basis.FromScale(new Vector3(1, len, 1)), (a + b) / 2);
    }

    /// <summary>A translucent ground disc (fields: slows, vines, trap zones).</summary>
    public static MeshInstance3D Disc(Node3D world, Vector3 center, float radius, Color c, float alpha = 0.28f)
    {
        var mi = new MeshInstance3D
        {
            Name = "SentinelField",
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.06f, RadialSegments = 40, Rings = 1 },
            MaterialOverride = Mat(c, 1f, alpha),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = center + new Vector3(0, 0.05f, 0),
        };
        world.AddChild(mi);
        mi.GlobalPosition = center + new Vector3(0, 0.05f, 0);
        return mi;
    }

    /// <summary>A vertical slab from a to b (ground points), <paramref name="height"/> tall.</summary>
    public static MeshInstance3D Slab(Node3D world, Vector3 a, Vector3 b, float height, float thick, Color c, float alpha = 1f, float glow = 0.4f)
    {
        float len = Mathf.Max(0.05f, new Vector2(b.X - a.X, b.Z - a.Z).Length());
        var mi = new MeshInstance3D
        {
            Name = "SentinelWall",
            Mesh = new BoxMesh { Size = new Vector3(len, height, thick) },
            MaterialOverride = Mat(c, glow, alpha),
            CastShadow = alpha < 1f ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
        };
        world.AddChild(mi);
        var mid = (a + b) / 2;
        mi.GlobalTransform = new Transform3D(AlongX(b - a), new Vector3(mid.X, Mathf.Min(a.Y, b.Y) + height / 2, mid.Z));
        return mi;
    }

    /// <summary>A translucent oriented box (the sonic sensor's listening area).</summary>
    public static MeshInstance3D Volume(Node3D world, Vector3 center, Basis basis, Vector3 size, Color c, float alpha = 0.12f)
    {
        var mi = new MeshInstance3D
        {
            Name = "SentinelVolume",
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = Mat(c, 1f, alpha),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        world.AddChild(mi);
        mi.GlobalTransform = new Transform3D(basis, center);
        return mi;
    }
}

/// <summary>
/// The placement indicator while a sentinel ability is equipped: a ring where it will land (agent colour when valid, red
/// when not) plus an optional line (trap wire, wall footprint) and a facing tick (turret, barrier orientation).
/// </summary>
public partial class SentinelPreview : Node3D
{
    MeshInstance3D ring = null!, line = null!, tick = null!;
    StandardMaterial3D ok = null!, bad = null!;

    public static SentinelPreview Create(Node3D world, Color c)
    {
        var p = new SentinelPreview { Name = "SentinelPreview", Visible = false };
        world.AddChild(p);
        p.ok = SentinelFx.Mat(c, 1.6f, 0.75f);
        p.bad = SentinelFx.Mat(new Color(1f, 0.2f, 0.2f), 1.6f, 0.75f);
        p.ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.4f, Rings = 24, RingSegments = 6 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        p.AddChild(p.ring);
        p.line = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        p.AddChild(p.line);
        p.tick = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.04f, 0.6f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        p.AddChild(p.tick);
        return p;
    }

    /// <summary>Show the indicator at <paramref name="at"/> on a surface with normal <paramref name="normal"/>.</summary>
    public void Show(Vector3 at, Vector3 normal, bool valid, Vector3? lineTo = null, float lineHeight = 0.04f, Vector3? facing = null)
    {
        Visible = true;
        var m = valid ? ok : bad;
        ring.MaterialOverride = m;
        ring.GlobalTransform = new Transform3D(SentinelFx.AlongY(normal), at + normal * 0.04f);
        if (lineTo is { } b)
        {
            line.Visible = true;
            line.MaterialOverride = m;
            var d = b - at;
            float len = Mathf.Max(0.05f, d.Length());
            float h = Mathf.Max(0.04f, lineHeight);
            var basis = SentinelFx.AlongX(d);
            if (Mathf.Abs(d.Y) > 0.5f * len) basis = SentinelFx.AlongY(d) * new Basis(Vector3.Forward, Mathf.Pi / 2);
            line.GlobalTransform = new Transform3D(basis * Basis.FromScale(new Vector3(len, h, 0.04f)), (at + b) / 2 + new Vector3(0, h / 2 > 0.05f ? h / 2 : 0.03f, 0));
        }
        else line.Visible = false;
        if (facing is { } f && new Vector2(f.X, f.Z).LengthSquared() > 1e-4f)
        {
            tick.Visible = true;
            tick.MaterialOverride = m;
            var fz = new Vector3(f.X, 0, f.Z).Normalized();
            var x = Vector3.Up.Cross(fz).Normalized();
            tick.GlobalTransform = new Transform3D(new Basis(x, Vector3.Up, fz), at + fz * 0.7f + new Vector3(0, 0.05f, 0));
        }
        else tick.Visible = false;
    }

    public void HideAll() => Visible = false;
}
