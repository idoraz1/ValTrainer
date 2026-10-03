using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>What a player-thrown utility did this frame (the drill applies the effect to bots and the player).</summary>
public enum UtilEventKind
{
    /// <summary>A flash detonated at <c>At</c>; <c>Value</c> = max blind (s). Blinds by the on-screen rule (<see cref="BlindModel"/>).</summary>
    Pop,
    /// <summary>Dizzy starts firing: the drill picks the enemies she can see and launches <see cref="PlasmaShot"/>s.</summary>
    DizzyFire,
    /// <summary>A Dizzy plasma blob exploded at <c>At</c>: blinds enemies within <see cref="PlasmaShot.Radius"/> with line of sight.</summary>
    Plasma,
    /// <summary>Recon pulse at <c>At</c>: reveals enemies in its line of sight within <c>Value</c> metres.</summary>
    Scan,
    /// <summary>The thrown thing expired without doing anything (no wall hit, flew off the map …).</summary>
    Fizzle,
    /// <summary>Enemies shot it to pieces.</summary>
    Destroyed,
    /// <summary>It landed / stuck (recon bolt, haunt) or armed (blindside bounce): a sound cue.</summary>
    Landed,
}

public readonly record struct UtilEvent(UtilEventKind Kind, Vector3 At, float Value = 0f);

/// <summary>Per-frame player input a utility may use (Skye steers with the crosshair while LMB is held).</summary>
public readonly record struct UtilInput(bool FireHeld, Vector3 AimDir);

/// <summary>
/// A utility the player threw (flash, nearsight, recon device). Pure motion model + an optional visual, so the same class
/// can predict a throw without drawing anything (dev auto-throw aim search). Positions in world metres.
/// </summary>
public abstract class PlayerUtility
{
    /// <summary>World gravity used with VALORANT's projectile gravity scales (Unreal default 9.8 m/s²; estimate).</summary>
    public const float WorldGravity = 9.8f;

    protected readonly IGame G;
    protected readonly bool Visual;
    public Vector3 Pos;
    public float Age;
    public bool Done;
    public readonly List<UtilEvent> Events = new();

    /// <summary>Enemies could see it right now (flashes: drives turning away; devices: drives shooting it).</summary>
    public virtual bool VisibleToEnemies => true;
    /// <summary>A flash in flight: enemies who see it may turn away.</summary>
    public virtual bool IsFlash => false;
    /// <summary>Enemies may shoot it (HP &gt; 0 and in a destructible phase).</summary>
    public virtual bool Shootable => false;
    public float Hp;
    /// <summary>Last time an enemy bullet hit it (visual feedback).</summary>
    protected float hurtT = -1f;

    protected PlayerUtility(IGame g, bool visual) { G = g; Visual = visual; }

    public abstract void Update(float dt, UtilInput input);
    /// <summary>The ability key was pressed again while this is out (Skye pops, Fade drops early).</summary>
    public virtual void Recast() { }
    public virtual bool CanRecast => false;

    /// <summary>An enemy bullet hit it.</summary>
    public void Damage(float dmg)
    {
        if (Done || !Shootable) return;
        Hp -= dmg;
        hurtT = 0f;
        if (Hp <= 0) { Events.Add(new UtilEvent(UtilEventKind.Destroyed, Pos)); OnDestroyed(); Done = true; }
    }

    protected virtual void OnDestroyed() => FreeVisual();
    /// <summary>Removes the visual immediately (round reset).</summary>
    public virtual void FreeVisual() { }

    // ---------------------------------------------------------------- motion helpers

    /// <summary>
    /// Moves a point by v·dt under gravity g against the box world. On a hit it stops just outside the face and returns
    /// true with that face's outward normal; the caller decides whether to bounce, slide or stick.
    /// </summary>
    public static bool Step(ref Vector3 p, ref Vector3 v, float g, float dt, IReadOnlyList<Box> solid, out Vector3 normal)
    {
        normal = Vector3.Zero;
        v.Y -= g * dt;
        var d = v * dt;
        float len = d.Length();
        if (len < 1e-6f) return false;
        var dir = d / len;
        float t = Collision.FirstHit(p, dir, solid, out int i);
        if (t <= len && i >= 0)
        {
            var hit = p + dir * t;
            normal = solid[i].NormalAt(hit);
            p = hit + normal * 0.03f;
            return true;
        }
        p += d;
        return false;
    }

    /// <summary>Reflects a velocity off a surface: <paramref name="restitution"/> of the normal speed, <paramref name="friction"/> of the tangential.</summary>
    public static Vector3 Bounce(Vector3 v, Vector3 n, float restitution, float friction)
    {
        var vn = n * v.Dot(n);
        var vt = v - vn;
        return vt * friction - vn * restitution;
    }

    /// <summary>Inside any solid box (with a small margin).</summary>
    public static bool Inside(Vector3 p, IReadOnlyList<Box> solid, float margin = 0.01f)
    {
        foreach (var b in solid)
            if (p.X > b.Min.X + margin && p.X < b.Max.X - margin && p.Y > b.Min.Y + margin && p.Y < b.Max.Y - margin &&
                p.Z > b.Min.Z + margin && p.Z < b.Max.Z - margin) return true;
        return false;
    }

    /// <summary>Rough world extent (XZ) of the boxes: anything far outside it flew off the map.</summary>
    public static bool OffMap(Vector3 p, IReadOnlyList<Box> solid)
    {
        if (p.Y < -6f || p.Y > 60f) return true;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var b in solid) { x0 = Mathf.Min(x0, b.Min.X); z0 = Mathf.Min(z0, b.Min.Z); x1 = Mathf.Max(x1, b.Max.X); z1 = Mathf.Max(z1, b.Max.Z); }
        return p.X < x0 - 8 || p.X > x1 + 8 || p.Z < z0 - 8 || p.Z > z1 + 8;
    }

    /// <summary>Where a thrown ability leaves the hand: a little right of and below the eye, never inside a wall.</summary>
    public static Vector3 HandPoint(IGame g, Vector3 dir)
    {
        var eye = g.View.Eye;
        var right = dir.Cross(Vector3.Up);
        right = right.LengthSquared() < 1e-4f ? Vector3.Right : right.Normalized();
        var p = eye + dir * 0.45f + right * 0.18f + Vector3.Down * 0.22f;
        return g.LineOfSight(eye, p) ? p : eye;
    }
}

/// <summary>Small procedural look kit for the utility visuals (own shaders; no imported art).</summary>
public static class UtilLook
{
    static Shader? glowShader, coreShader, darkShader;
    static Texture2D? glowTex, ringTex;
    static QuadMesh? quad;

    const string GlowCode = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform sampler2D tex : source_color, filter_linear, hint_default_white;
uniform vec3 color : source_color = vec3(1.0);
uniform float energy = 1.0;
uniform float fade = 1.0;
void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}
void fragment() {
	ALBEDO = color * energy;
	ALPHA = clamp(texture(tex, UV).a * fade, 0.0, 1.0);
}";

    const string CoreCode = @"
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled;
uniform vec3 color : source_color = vec3(1.0);
uniform float energy = 3.0;
void fragment() {
	float f = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = color * energy * (0.55 + 0.45 * f);
}";

    /// <summary>Smoky translucent shell (Omen / Fade darkness): dark centre, coloured fresnel edge, slow noise.</summary>
    const string DarkCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, fog_disabled, shadows_disabled;
uniform vec3 color : source_color = vec3(0.05, 0.02, 0.08);
uniform vec3 edge : source_color = vec3(0.45, 0.3, 0.9);
uniform float alpha = 0.75;
uniform float seed = 0.0;
float h(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
void fragment() {
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - ndv, 2.5);
	float n = 0.5 + 0.5 * sin(TIME * 3.0 + seed * 7.0 + UV.x * 18.0 + UV.y * 11.0);
	ALBEDO = mix(color, edge, rim * (0.6 + 0.4 * n));
	ALPHA = alpha * (0.35 + 0.65 * ndv) * (0.8 + 0.2 * n);
}";

    static void Init()
    {
        if (glowShader != null) return;
        glowShader = new Shader { Code = GlowCode };
        coreShader = new Shader { Code = CoreCode };
        darkShader = new Shader { Code = DarkCode };
        glowTex = Radial((0f, 1f), (0.12f, 0.75f), (0.35f, 0.25f), (0.7f, 0.06f), (1f, 0f));
        ringTex = Radial((0f, 0f), (0.62f, 0f), (0.8f, 0.9f), (0.88f, 0.35f), (1f, 0f));
        quad = new QuadMesh { Size = Vector2.One };
    }

    static Texture2D Radial(params (float Offset, float Alpha)[] stops)
    {
        var g = new Gradient
        {
            Offsets = stops.Select(s => s.Offset).ToArray(),
            Colors = stops.Select(s => new Color(1, 1, 1, s.Alpha)).ToArray(),
        };
        return new GradientTexture2D
        {
            Gradient = g, Width = 96, Height = 96, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
    }

    /// <summary>Additive camera-facing glow sprite.</summary>
    public static (MeshInstance3D Node, ShaderMaterial Mat) Glow(Node3D parent, Color c, float size, float energy = 2f, bool ring = false)
    {
        Init();
        var m = new ShaderMaterial { Shader = glowShader };
        m.SetShaderParameter("tex", (ring ? ringTex : glowTex)!);
        m.SetShaderParameter("color", c);
        m.SetShaderParameter("energy", energy);
        m.SetShaderParameter("fade", 1f);
        var n = new MeshInstance3D { Mesh = quad, MaterialOverride = m, Scale = Vector3.One * size, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 2f };
        parent.AddChild(n);
        return (n, m);
    }

    /// <summary>Unshaded HDR core mesh.</summary>
    public static (MeshInstance3D Node, ShaderMaterial Mat) Core(Node3D parent, Mesh mesh, Color c, float energy = 3f)
    {
        Init();
        var m = new ShaderMaterial { Shader = coreShader };
        m.SetShaderParameter("color", c);
        m.SetShaderParameter("energy", energy);
        var n = new MeshInstance3D { Mesh = mesh, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(n);
        return (n, m);
    }

    /// <summary>Dark smoky translucent shell.</summary>
    public static (MeshInstance3D Node, ShaderMaterial Mat) Dark(Node3D parent, Mesh mesh, Color body, Color edge, float alpha)
    {
        Init();
        var m = new ShaderMaterial { Shader = darkShader };
        m.SetShaderParameter("color", body);
        m.SetShaderParameter("edge", edge);
        m.SetShaderParameter("alpha", alpha);
        m.SetShaderParameter("seed", (float)Random.Shared.NextDouble());
        var n = new MeshInstance3D { Mesh = mesh, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(n);
        return (n, m);
    }

    public static OmniLight3D Light(Node3D parent, Color c, float energy, float range)
    {
        var l = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false, LightSpecular = 0.2f };
        parent.AddChild(l);
        return l;
    }

    /// <summary>Soft particle trail (world space) in a colour.</summary>
    public static GpuParticles3D Trail(Node3D parent, Color head, Color tail, float size, float life, int amount, float gravity = 0f)
    {
        Init();
        var ramp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { head, tail } };
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = size * 0.3f,
            Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.05f, InitialVelocityMax = 0.4f,
            Gravity = new Vector3(0, gravity, 0), DampingMin = 1f, DampingMax = 2f,
            ScaleMin = 0.7f, ScaleMax = 1.2f,
            ColorRamp = new GradientTexture1D { Gradient = ramp, UseHdr = true },
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true, AlbedoTexture = glowTex, DisableFog = true,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        var p = new GpuParticles3D
        {
            Amount = amount, Lifetime = life, LocalCoords = false, Emitting = true, ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One * size, Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-6, -6, -6), new Vector3(12, 12, 12)),
        };
        parent.AddChild(p);
        return p;
    }
}
