using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Sova — Recon Bolt. Charge level = projectile class (official wiki): no charge = class 2 (18 m/s, gravity ×0.3), 1 bar
/// (0.333 s) = class 3 (29 m/s, ×0.45), 2 bars (0.667 s) = class 4 (41.25 m/s, ×0.3), 3 bars (1 s) = class 5 (41.25 m/s,
/// ×0.15). Up to 2 bounces, then it sticks; 0.667 s later it scans twice, 1.6 s apart, revealing the enemies it can see
/// within 30 m for 0.75 s each time. 20 HP once it has stuck.
/// </summary>
public sealed class ReconBolt : PlayerUtility
{
    public static readonly (float Speed, float GravityScale)[] Levels = { (18f, 0.3f), (29f, 0.45f), (41.25f, 0.3f), (41.25f, 0.15f) };
    public const float ChargePerBar = 1f / 3f, Windup = 0.667f, PulseEvery = 1.6f, RevealTime = 0.75f, Radius = 30f, Health = 20f;
    public const int Pulses = 2;
    const float MaxFlight = 6f;

    Vector3 vel, normal;
    readonly float grav;
    int bounces;
    public bool Stuck { get; private set; }
    float stuckAt;
    int pulsesDone;

    Node3D? root, arrow;
    ShaderMaterial? headMat;
    OmniLight3D? light;
    GpuParticles3D? trail;
    MeshInstance3D? head;

    public override bool Shootable => Stuck && !Done;
    public Vector3 Normal => normal;

    /// <param name="level">0–3 charge bars.</param>
    public ReconBolt(IGame g, Vector3 from, Vector3 dir, int level, int bounces, bool visual) : base(g, visual)
    {
        level = Math.Clamp(level, 0, 3);
        Pos = from;
        vel = dir.Normalized() * Levels[level].Speed;
        grav = Levels[level].GravityScale * WorldGravity;
        this.bounces = Math.Clamp(bounces, 0, 2);
        Hp = Health;
        if (visual) Build(g);
    }

    static readonly Color Cyan = Color.Color8(80, 200, 255);

    void Build(IGame g)
    {
        root = new Node3D { Name = "ReconBolt" };
        g.World.AddChild(root);
        root.GlobalPosition = Pos;
        arrow = new Node3D();
        root.AddChild(arrow);
        var shaft = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.025f, 0.025f, 0.62f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.2f), Roughness = 0.4f, Metallic = 0.6f },
            Position = new Vector3(0, 0, 0.28f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        arrow.AddChild(shaft);
        (head, headMat) = UtilLook.Core(arrow, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.045f, Height = 0.16f, RadialSegments = 8 }, Cyan, 4f);
        head.RotationDegrees = new Vector3(-90, 0, 0); // cone tip along -Z (forward)
        head.Position = new Vector3(0, 0, -0.06f);
        UtilLook.Glow(arrow, Cyan, 0.45f, 2.2f).Node.Position = new Vector3(0, 0, -0.08f);
        light = UtilLook.Light(root, Cyan, 1.2f, 3.5f);
        trail = UtilLook.Trail(root, new Color(0.6f, 1.6f, 2.2f, 0.8f), new Color(0.2f, 0.6f, 1f, 0f), 0.09f, 0.35f, 32);
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (!Stuck)
        {
            var prev = Pos;
            if (Step(ref Pos, ref vel, grav, dt, G.Solid, out var n))
            {
                if (bounces > 0)
                {
                    bounces--;
                    vel = Bounce(vel, n, 0.8f, 0.9f); // estimate: keeps most of its speed
                    if (Visual) AbilitySfx.PlayAt(G, "bolt_tick", Pos, 0.6f);
                }
                else
                {
                    Stuck = true;
                    stuckAt = Age;
                    normal = n;
                    Events.Add(new UtilEvent(UtilEventKind.Landed, Pos));
                    if (trail != null) trail.Emitting = false;
                }
            }
            if (!Stuck && (Age > MaxFlight || OffMap(Pos, G.Solid)))
            {
                Events.Add(new UtilEvent(UtilEventKind.Fizzle, Pos));
                FreeVisual();
                Done = true;
                return;
            }
            if (arrow != null && (Pos - prev).LengthSquared() > 1e-6f)
            {
                var d = (Pos - prev).Normalized();
                arrow.LookAt(arrow.GlobalPosition + d, Mathf.Abs(d.Y) > 0.98f ? Vector3.Right : Vector3.Up);
            }
        }
        else
        {
            float since = Age - stuckAt;
            // Pulses: Windup, Windup + PulseEvery.
            while (pulsesDone < Pulses && since >= Windup + pulsesDone * PulseEvery)
            {
                pulsesDone++;
                var at = Pos + normal * 0.12f;
                Events.Add(new UtilEvent(UtilEventKind.Scan, at, Radius));
                if (Visual) ScanWave.Spawn(G, at, Radius, Cyan);
            }
            if (since >= Windup + (Pulses - 1) * PulseEvery + RevealTime + 0.4f)
            {
                FreeVisual();
                Done = true;
                return;
            }
            if (light != null)
            {
                float blink = since < Windup ? 0.5f + 0.5f * Mathf.Sin(since * 30f) : 0.6f + 0.4f * Mathf.Sin(since * 8f);
                light.LightEnergy = 0.6f + 1.4f * blink;
                headMat?.SetShaderParameter("energy", 2f + 4f * blink);
            }
        }
        if (root != null && GodotObject.IsInstanceValid(root)) root.GlobalPosition = Pos;
    }

    protected override void OnDestroyed()
    {
        if (Visual) G.SoundAt("shatter", Pos, maxRange: 45f, floor: 0.5f);
        FreeVisual();
    }

    public override void FreeVisual()
    {
        if (root != null && GodotObject.IsInstanceValid(root)) root.QueueFree();
        root = null;
    }

    /// <summary>Charge bars after holding fire for <paramref name="held"/> seconds.</summary>
    public static int BarsFor(float held) => Math.Clamp((int)(held / ChargePerBar + 1e-3f), 0, 3);
}

/// <summary>
/// Fade — Haunt: thrown as a class-2 projectile; after 1.5 s in the air (or on re-use, or when it hits a wall) it drops
/// straight down. 0.825 s after landing it opens and reveals the enemies it can see within 30 m (live silhouettes for
/// 1.5 s) and marks them with a Terror Trail (12 s). 1 HP once it has landed. (Official wiki.)
/// </summary>
public sealed class Haunt : PlayerUtility
{
    public const float Speed = 18f, GravityScale = 0.3f, MaxAir = 1.5f, Windup = 0.825f, RevealTime = 1.5f, Radius = 30f, TrailTime = 12f, Health = 1f;
    const float DropGravity = 20f; // estimate
    Vector3 vel;
    bool dropping, landed;
    float landedAt;
    bool revealed;

    Node3D? root;
    MeshInstance3D? eye;
    ShaderMaterial? eyeMat;
    OmniLight3D? light;
    GpuParticles3D? smoke;

    static readonly Color Teal = Color.Color8(70, 200, 190);

    public override bool Shootable => landed && !Done;
    public override bool CanRecast => !dropping && !landed && !Done;
    public bool Landed => landed;
    /// <summary>Open and watching (the reveal window).</summary>
    public bool Watching => landed && !Done && Age - landedAt >= Windup;

    public Haunt(IGame g, Vector3 from, Vector3 dir, bool visual) : base(g, visual)
    {
        Pos = from;
        vel = dir.Normalized() * Speed;
        Hp = Health;
        if (visual) Build(g);
    }

    void Build(IGame g)
    {
        root = new Node3D { Name = "Haunt" };
        g.World.AddChild(root);
        root.GlobalPosition = Pos;
        var blob = new SphereMesh { Radius = 0.2f, Height = 0.4f, RadialSegments = 16, Rings = 8 };
        UtilLook.Dark(root, blob, new Color(0.02f, 0.03f, 0.04f), new Color(0.2f, 0.75f, 0.7f), 0.92f);
        (eye, eyeMat) = UtilLook.Core(root, new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 12, Rings = 6 }, Teal, 3f);
        eye.Position = new Vector3(0, 0.05f, -0.15f);
        UtilLook.Glow(root, Teal, 0.7f, 1.4f);
        light = UtilLook.Light(root, Teal, 0.8f, 3f);
        smoke = UtilLook.Trail(root, new Color(0.04f, 0.06f, 0.08f, 0.8f), new Color(0.1f, 0.3f, 0.3f, 0f), 0.35f, 0.6f, 40, 0.4f);
    }

    public override void Recast()
    {
        if (!CanRecast) return;
        dropping = true;
        vel = new Vector3(0, Mathf.Min(vel.Y, 0f), 0);
    }

    public override void Update(float dt, UtilInput input)
    {
        Age += dt;
        if (!landed)
        {
            if (!dropping && Age >= MaxAir) Recast();
            float g = dropping ? DropGravity : GravityScale * WorldGravity;
            if (Step(ref Pos, ref vel, g, dt, G.Solid, out var n))
            {
                if (n.Y > 0.6f)
                {
                    landed = true;
                    landedAt = Age;
                    vel = Vector3.Zero;
                    Events.Add(new UtilEvent(UtilEventKind.Landed, Pos));
                    if (smoke != null) smoke.Emitting = false;
                }
                else if (n.Y < -0.6f) vel.Y = Mathf.Min(0f, -vel.Y * 0.2f); // hit a ceiling: fall
                else Recast();                                                 // hit a wall: drops from there
            }
            if (!landed && OffMap(Pos, G.Solid))
            {
                Events.Add(new UtilEvent(UtilEventKind.Fizzle, Pos));
                FreeVisual();
                Done = true;
                return;
            }
        }
        else
        {
            float since = Age - landedAt;
            if (!revealed && since >= Windup)
            {
                revealed = true;
                var at = Pos + Vector3.Up * 0.45f;
                Events.Add(new UtilEvent(UtilEventKind.Scan, at, Radius));
                if (Visual) ScanWave.Spawn(G, at, Radius, new Color(0.25f, 0.85f, 0.8f), dark: true);
            }
            if (since >= Windup + RevealTime)
            {
                FreeVisual();
                Done = true;
                return;
            }
            if (root != null && GodotObject.IsInstanceValid(root))
            {
                float open = Mathf.Clamp(since / Windup, 0f, 1f);
                if (eye != null) eye.Scale = Vector3.One * (0.6f + 1.6f * open);
                eyeMat?.SetShaderParameter("energy", 2f + 5f * open);
                if (light != null) light.LightEnergy = 0.6f + 2f * open;
                // The watcher rises a little as it opens.
                root.GlobalPosition = Pos + Vector3.Up * (0.25f * open);
                return;
            }
        }
        if (root != null && GodotObject.IsInstanceValid(root)) root.GlobalPosition = Pos;
    }

    protected override void OnDestroyed()
    {
        if (Visual) G.SoundAt("shatter", Pos, maxRange: 45f, floor: 0.5f);
        FreeVisual();
    }

    public override void FreeVisual()
    {
        if (root != null && GodotObject.IsInstanceValid(root)) root.QueueFree();
        root = null;
    }
}

/// <summary>An expanding recon scan shell (fades as it grows).</summary>
public partial class ScanWave : Node3D
{
    static Shader? shader;
    const string Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform vec3 color : source_color = vec3(0.3, 0.8, 1.0);
uniform float fade = 1.0;
uniform float dark = 0.0;
void fragment() {
	float ndv = abs(dot(NORMAL, VIEW));
	float rim = pow(1.0 - ndv, 3.0);
	float bands = 0.6 + 0.4 * step(0.5, fract(UV.y * 40.0));
	ALBEDO = color * (0.06 + 1.4 * rim) * bands * fade;
}";

    float t, radius;
    ShaderMaterial mat = null!;
    MeshInstance3D shell = null!;
    const float Grow = 0.6f;

    public static void Spawn(IGame g, Vector3 at, float radius, Color c, bool dark = false)
    {
        shader ??= new Shader { Code = Code };
        var w = new ScanWave { radius = radius, Name = "ScanWave" };
        w.mat = new ShaderMaterial { Shader = shader };
        w.mat.SetShaderParameter("color", dark ? c * 0.8f : c);
        w.shell = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = w.mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 40f,
        };
        w.AddChild(w.shell);
        g.World.AddChild(w);
        w.GlobalPosition = at;
        w.Scale = Vector3.One * 0.3f;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float k = Mathf.Clamp(t / Grow, 0f, 1f);
        Scale = Vector3.One * Mathf.Max(0.3f, radius * (1f - (1f - k) * (1f - k)));
        mat.SetShaderParameter("fade", Mathf.Clamp(1f - k, 0f, 1f) * 0.9f);
        if (t > Grow + 0.05f) QueueFree();
    }
}
