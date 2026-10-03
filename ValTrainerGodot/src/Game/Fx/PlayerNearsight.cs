using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Reyna — Leer: an eye that drifts 10 m forward in 0.55 s (straight through walls), opens after 0.4 s and stays 1.6 s.
/// Every enemy who has its pupil on screen is nearsighted (can see only 6 m) until it closes. 60 HP. (Official wiki.)
/// </summary>
public sealed class LeerEye : PlayerUtility
{
    public const float Travel = 10f, TravelTime = 0.55f, Windup = 0.4f, ActiveTime = 1.6f, VisionRadius = 6f, Health = 60f;
    readonly Vector3 from, end;
    Node3D? root;
    MeshInstance3D? iris;
    ShaderMaterial? glowMat, irisMat;
    OmniLight3D? light;

    public float OpenAt => TravelTime + Windup;
    public float CloseAt => OpenAt + ActiveTime;
    /// <summary>Open and nearsighting whoever looks at it.</summary>
    public bool Active => !Done && Age >= OpenAt && Age < CloseAt;
    public override bool Shootable => !Done && Age >= TravelTime * 0.5f;

    public LeerEye(IGame g, Vector3 from, Vector3 dir, bool visual) : base(g, visual)
    {
        this.from = from;
        Pos = from;
        Hp = Health;
        dir = dir.Normalized();
        // It passes through walls but must not come to rest inside one: pull the end point back along the path.
        var e = from + dir * Travel;
        for (int i = 0; i < 40 && Inside(e, g.Solid, 0.05f); i++) e -= dir * 0.25f;
        end = e;
        if (visual) Build(g);
    }

    void Build(IGame g)
    {
        root = new Node3D { Name = "Leer" };
        g.World.AddChild(root);
        root.GlobalPosition = from;
        var purple = Color.Color8(190, 90, 255);
        UtilLook.Core(root, new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 16, Rings = 8 }, new Color(0.95f, 0.85f, 1f), 1.6f);
        (iris, irisMat) = UtilLook.Core(root, new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 12, Rings = 6 }, purple, 5f);
        iris.Position = new Vector3(0, 0, -0.12f);
        (_, glowMat) = UtilLook.Glow(root, purple, 1.4f, 2.4f);
        UtilLook.Glow(root, purple, 0.75f, 2.5f, ring: true);
        light = UtilLook.Light(root, purple, 1.6f, 5f);
        UtilLook.Trail(root, new Color(1.4f, 0.6f, 2f, 0.8f), new Color(0.5f, 0.2f, 0.9f, 0f), 0.22f, 0.45f, 24);
        root.Scale = Vector3.One * 0.4f;
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        float k = Mathf.Clamp(Age / TravelTime, 0f, 1f);
        Pos = from.Lerp(end, 1f - (1f - k) * (1f - k));
        if (root != null && GodotObject.IsInstanceValid(root))
        {
            root.GlobalPosition = Pos;
            // The pupil keeps looking at the player's side of the map (towards the caster) like a watching eye.
            var cam = root.GetViewport()?.GetCamera3D();
            var away = cam != null ? Pos - cam.GlobalPosition : Vector3.Zero;
            if (away.LengthSquared() > 0.01f && Mathf.Abs(away.Normalized().Y) < 0.98f) root.LookAt(Pos + away, Vector3.Up);
            float open = Mathf.Clamp((Age - TravelTime) / Windup, 0f, 1f);
            float close = Age > CloseAt ? Mathf.Clamp(1f - (Age - CloseAt) / 0.25f, 0f, 1f) : 1f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Age * 14f);
            root.Scale = Vector3.One * (0.4f + 0.6f * open) * close * (hurtT >= 0 && hurtT < 0.12f ? 1.15f : 1f);
            glowMat?.SetShaderParameter("energy", (1.5f + 2.5f * open) * pulse);
            irisMat?.SetShaderParameter("energy", 3f + 4f * open);
            if (light != null) light.LightEnergy = (0.8f + 1.6f * open) * pulse;
        }
        if (hurtT >= 0) hurtT += dt;
        if (Age >= CloseAt + 0.25f)
        {
            FreeVisual();
            Done = true;
        }
    }

    protected override void OnDestroyed()
    {
        if (Visual)
        {
            var o = G.SpawnOrb(Color.Color8(180, 90, 255));
            o.GlobalPosition = Pos;
            o.Pop();
            G.SoundAt("shatter", Pos, maxRange: 45f, floor: 0.5f);
        }
        FreeVisual();
    }

    public override void FreeVisual()
    {
        if (root != null && GodotObject.IsInstanceValid(root)) root.QueueFree();
        root = null;
    }
}

/// <summary>
/// Omen — Paranoia: a shadow that flies 25 m at 20 m/s straight through walls. Everyone it touches (radius 4.3 m) is
/// nearsighted — can see only 7.5 m — for 2 s. (Official wiki.)
/// </summary>
public sealed class Paranoia : PlayerUtility
{
    public const float Speed = 20f, Range = 25f, Radius = 4.3f, Duration = 2f, VisionRadius = 7.5f; // wiki
    /// <summary>Vertical half-height of the touch volume (estimate; the shadow is a wide, low shape).</summary>
    public const float HalfHeight = 2.2f;
    readonly Vector3 from, dir;
    public Vector3 PrevPos;
    Node3D? root;
    GpuParticles3D? smoke;
    float fadeT = -1f;

    public bool Moving => Age * Speed < Range;

    public Paranoia(IGame g, Vector3 from, Vector3 dir, bool visual) : base(g, visual)
    {
        this.from = from + dir.Normalized() * 0.8f; // spawns a little in front of the caster
        this.dir = dir.Normalized();
        Pos = PrevPos = this.from;
        if (visual) Build(g);
    }

    void Build(IGame g)
    {
        root = new Node3D { Name = "Paranoia" };
        g.World.AddChild(root);
        root.GlobalPosition = from;
        var body = new Color(0.03f, 0.01f, 0.06f);
        var edge = new Color(0.42f, 0.28f, 0.95f);
        var blob = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 20, Rings = 10 };
        var (core, _) = UtilLook.Dark(root, blob, body, edge, 0.78f);
        core.Scale = new Vector3(1.5f, 0.85f, 1.1f);
        var (wisp, _) = UtilLook.Dark(root, blob, body, edge * 1.2f, 0.45f);
        wisp.Scale = new Vector3(2.2f, 1.2f, 1.7f);
        UtilLook.Glow(root, new Color(0.55f, 0.35f, 1f), 2.6f, 0.9f);
        UtilLook.Light(root, new Color(0.5f, 0.3f, 1f), 1.2f, 6f);
        smoke = UtilLook.Trail(root, new Color(0.1f, 0.04f, 0.2f, 0.75f), new Color(0.25f, 0.12f, 0.5f, 0f), 1.1f, 0.7f, 48);
        root.LookAt(from + dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up);
    }

    public override void Update(float dt, UtilInput input)
    {
        PrevPos = Pos;
        Age += dt;
        Pos = from + dir * Mathf.Min(Age * Speed, Range);
        if (root != null && GodotObject.IsInstanceValid(root))
        {
            root.GlobalPosition = Pos;
            if (!Moving)
            {
                if (fadeT < 0) { fadeT = 0; if (smoke != null) smoke.Emitting = false; }
                fadeT += dt;
                root.Scale = Vector3.One * Mathf.Max(0.01f, 1f - fadeT / 0.35f);
            }
        }
        if (!Moving && Age * Speed >= Range + 0.35f * Speed)
        {
            FreeVisual();
            Done = true;
        }
    }

    /// <summary>Did the shadow sweep over this point this frame (swept capsule from PrevPos to Pos)?</summary>
    public bool Touches(Vector3 p)
    {
        if (!Moving && Age * Speed > Range + 0.1f) return false;
        var a = PrevPos; var b = Pos;
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        var c = a + ab * t;
        var d = p - c;
        return new Vector2(d.X, d.Z).Length() <= Radius && Mathf.Abs(d.Y) <= HalfHeight;
    }

    public override void FreeVisual()
    {
        if (root != null && GodotObject.IsInstanceValid(root)) root.QueueFree();
        root = null;
    }
}
