using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// A flying / winding-up flash projectile with a per-agent look picked from its colour (see <see cref="FlashAgent"/>):
/// bright core + soft billboard halo, a GPU particle trail, a charging pulse in <see cref="Windup"/> and a big
/// readable detonation in <see cref="Pop"/> (light burst, flare, anamorphic streak, shockwave ring, sparks).
/// API: position it each frame, call Windup / Pop; after Pop the node frees itself.
/// </summary>
public partial class FlashOrb : Node3D
{
    enum Kind { Generic, Phoenix, Breach, Kayo, Skye, Yoru, Vyse, Gekko }

    // ---- shared resources (built once) ----
    static Shader? spriteShader, coreShader;
    static Texture2D? glowTex, ringTex, sparkTex;
    static QuadMesh? quad;

    const string SpriteCode = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled;
uniform sampler2D tex : source_color, filter_linear, hint_default_white;
uniform vec3 color : source_color = vec3(1.0);
uniform float energy = 1.0;
uniform float fade = 1.0;
void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}
void fragment() {
	ALBEDO = color * energy;
	ALPHA = clamp(texture(tex, UV).a * fade, 0.0, 1.0);
}";

    const string CoreCode = @"
shader_type spatial;
render_mode unshaded, fog_disabled;
uniform vec3 color : source_color = vec3(1.0);
uniform float energy = 3.0;
void fragment() {
	float f = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = color * energy * (0.55 + 0.45 * f);
}";

    static Texture2D Radial(params (float Offset, float Alpha)[] stops)
    {
        var g = new Gradient();
        g.Offsets = stops.Select(s => s.Offset).ToArray();
        g.Colors = stops.Select(s => new Color(1, 1, 1, s.Alpha)).ToArray();
        return new GradientTexture2D
        {
            Gradient = g, Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
    }

    static void InitShared()
    {
        if (spriteShader != null) return;
        spriteShader = new Shader { Code = SpriteCode };
        coreShader = new Shader { Code = CoreCode };
        glowTex = Radial((0f, 1f), (0.12f, 0.75f), (0.35f, 0.25f), (0.7f, 0.06f), (1f, 0f));
        ringTex = Radial((0f, 0f), (0.62f, 0f), (0.8f, 0.9f), (0.88f, 0.35f), (1f, 0f));
        sparkTex = Radial((0f, 1f), (0.3f, 0.8f), (0.6f, 0.2f), (1f, 0f));
        quad = new QuadMesh { Size = Vector2.One };
    }

    // ---- instance ----
    Kind kind;
    Color color, hot;
    Node3D body = null!;
    MeshInstance3D halo = null!, ring = null!, flare = null!, streak = null!, shock = null!;
    ShaderMaterial haloMat = null!, ringMat = null!, flareMat = null!, streakMat = null!, shockMat = null!;
    ShaderMaterial coreMat = null!;
    OmniLight3D light = null!;
    GpuParticles3D trail = null!, burst = null!;
    float age, popT = -1, windT = -1, life = -1;
    Vector3 lastPos;
    bool hasLast;

    public static FlashOrb Create(Color color)
    {
        InitShared();
        var o = new FlashOrb { color = color, kind = KindOf(color), Name = "FlashOrb" };
        o.hot = color.Lerp(Colors.White, 0.65f);
        o.Build();
        return o;
    }

    /// <summary>
    /// Adds one orb per agent look for a second at <paramref name="at"/> (pick a spot hidden under the floor but inside
    /// the start view) so shaders and pipelines are compiled before the first throw. Works before entering the tree.
    /// </summary>
    public static void Prewarm(Node3D parent, Vector3 at)
    {
        foreach (var a in FlashAgent.All)
        {
            var o = Create(a.Color);
            o.life = 1.0f;
            o.Position = at;
            o.light.Visible = false;
            parent.AddChild(o);
        }
    }

    static Kind KindOf(Color c)
    {
        var best = Kind.Generic;
        float bestD = 0.02f;
        foreach (var a in FlashAgent.All)
        {
            float d = (a.Color.R - c.R) * (a.Color.R - c.R) + (a.Color.G - c.G) * (a.Color.G - c.G) + (a.Color.B - c.B) * (a.Color.B - c.B);
            if (d < bestD)
            {
                bestD = d;
                best = a.Agent switch
                {
                    "Phoenix" => Kind.Phoenix, "Breach" => Kind.Breach, "KAY/O" => Kind.Kayo, "Skye" => Kind.Skye,
                    "Yoru" => Kind.Yoru, "Vyse" => Kind.Vyse, "Gekko" => Kind.Gekko, _ => Kind.Generic,
                };
            }
        }
        return best;
    }

    ShaderMaterial Sprite(Texture2D tex, Color c, float energy, float fade = 1f)
    {
        var m = new ShaderMaterial { Shader = spriteShader };
        m.SetShaderParameter("tex", tex);
        m.SetShaderParameter("color", c);
        m.SetShaderParameter("energy", energy);
        m.SetShaderParameter("fade", fade);
        return m;
    }

    MeshInstance3D SpriteNode(ShaderMaterial m, float size)
    {
        var n = new MeshInstance3D
        {
            Mesh = quad, MaterialOverride = m, Scale = Vector3.One * size,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 2f,
        };
        AddChild(n);
        return n;
    }

    void Build()
    {
        // ---- core: a small hot shape per agent ----
        body = new Node3D();
        AddChild(body);
        coreMat = new ShaderMaterial { Shader = coreShader };   // HDR-bright so it stays the hottest point inside its halo
        coreMat.SetShaderParameter("color", hot);
        coreMat.SetShaderParameter("energy", 3.5f);
        Mesh coreMesh = kind switch
        {
            Kind.Yoru => new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 4, Rings = 2 },       // shard
            Kind.Kayo => new SphereMesh { Radius = 0.075f, Height = 0.15f, RadialSegments = 6, Rings = 3 },       // grenade
            Kind.Gekko => new SphereMesh { Radius = 0.11f, Height = 0.2f, RadialSegments = 12, Rings = 6 },        // Dizzy blob
            _ => new SphereMesh { Radius = 0.075f, Height = 0.15f, RadialSegments = 12, Rings = 6 },
        };
        var core = new MeshInstance3D { Mesh = coreMesh, MaterialOverride = coreMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        body.AddChild(core);
        if (kind == Kind.Yoru) core.Scale = new Vector3(0.8f, 1.8f, 0.8f);
        if (kind == Kind.Skye) core.Scale = new Vector3(0.8f, 0.8f, 1.9f);
        if (kind == Kind.Vyse)
        {
            // Arc Rose: three crossed thorns around the core.
            var thorn = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.34f) };
            for (int i = 0; i < 3; i++)
                body.AddChild(new MeshInstance3D
                {
                    Mesh = thorn, MaterialOverride = coreMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Rotation = new Vector3(i * 1.05f, i * 2.1f, 0.4f),
                });
        }
        if (kind == Kind.Gekko)
        {
            var eyeMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.1f, 0.05f, 0.12f) };
            foreach (var x in new[] { -0.045f, 0.045f })
                body.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.022f, Height = 0.044f, RadialSegments = 8, Rings = 4 }, MaterialOverride = eyeMat,
                    Position = new Vector3(x, 0.04f, -0.09f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
        }

        // ---- halo + charge ring ----
        float haloSize = kind switch { Kind.Breach => 1.3f, Kind.Phoenix => 1.1f, Kind.Gekko => 1.0f, _ => 0.9f };
        haloMat = Sprite(glowTex!, color, 2.2f);
        halo = SpriteNode(haloMat, haloSize);
        ringMat = Sprite(ringTex!, color, 3f, 0f);
        ring = SpriteNode(ringMat, 0.6f);

        // ---- pop sprites (hidden until Pop) ----
        flareMat = Sprite(glowTex!, color.Lerp(Colors.White, 0.75f), 6f, 0f);
        flare = SpriteNode(flareMat, 1f);
        streakMat = Sprite(glowTex!, color.Lerp(Colors.White, 0.5f), 5f, 0f);
        streak = SpriteNode(streakMat, 1f);
        shockMat = Sprite(ringTex!, color.Lerp(Colors.White, 0.4f), 4f, 0f);
        shock = SpriteNode(shockMat, 1f);
        flare.Visible = streak.Visible = shock.Visible = false;

        light = new OmniLight3D { LightColor = color, LightEnergy = 1.6f, OmniRange = 4.5f, ShadowEnabled = false, LightSpecular = 0.3f };
        AddChild(light);

        trail = MakeTrail();
        AddChild(trail);
        burst = MakeBurst();
        AddChild(burst);
    }

    GpuParticles3D MakeTrail()
    {
        (Color a, Color b) = kind switch
        {
            Kind.Phoenix => (new Color(3f, 2.4f, 1.2f), new Color(1.6f, 0.35f, 0.05f, 0f)),
            Kind.Gekko => (hot * 1.6f, color with { A = 0f }),
            _ => (hot * 2.2f, color with { A = 0f }),
        };
        var ramp = new Gradient { Offsets = new[] { 0f, 0.35f, 1f }, Colors = new[] { a, a.Lerp(b, 0.5f) with { A = 0.7f }, b } };
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = kind == Kind.Gekko ? 0.08f : 0.04f,
            Direction = Vector3.Up, Spread = 180f,
            InitialVelocityMin = 0.1f, InitialVelocityMax = kind == Kind.Skye ? 0.4f : 0.7f,
            Gravity = kind switch { Kind.Phoenix => new Vector3(0, 2.2f, 0), Kind.Gekko => new Vector3(0, -6f, 0), _ => Vector3.Zero },
            DampingMin = 1f, DampingMax = 3f,
            ScaleMin = kind == Kind.Skye ? 0.9f : 0.6f, ScaleMax = kind == Kind.Phoenix ? 1.5f : 1.1f,
            ScaleCurve = new CurveTexture { Curve = Shrink() },
            ColorRamp = new GradientTexture1D { Gradient = ramp, UseHdr = true },
            TurbulenceEnabled = kind is Kind.Phoenix or Kind.Skye,
            TurbulenceNoiseStrength = 1.2f, TurbulenceNoiseScale = 2f,
        };
        return new GpuParticles3D
        {
            Amount = kind is Kind.Phoenix or Kind.Skye ? 56 : 36,
            Lifetime = kind switch { Kind.Skye => 0.55, Kind.Phoenix => 0.4, _ => 0.32 },
            LocalCoords = false, Emitting = false, ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One * (kind == Kind.Phoenix ? 0.24f : 0.17f), Material = ParticleMat(sparkTex!) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-4, -4, -4), new Vector3(8, 8, 8)),
        };
    }

    GpuParticles3D MakeBurst()
    {
        var ramp = new Gradient
        {
            Offsets = new[] { 0f, 0.25f, 1f },
            Colors = new[] { new Color(4f, 4f, 4f), hot * 2.5f, color with { A = 0f } },
        };
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.1f,
            Direction = Vector3.Up, Spread = 180f,
            InitialVelocityMin = 3f, InitialVelocityMax = 7f,      // linear damping: stops within ≈1.5 m
            DampingMin = 14f, DampingMax = 20f,
            Gravity = new Vector3(0, -2f, 0),
            ScaleMin = 0.5f, ScaleMax = 1.2f,
            ScaleCurve = new CurveTexture { Curve = Shrink() },
            ColorRamp = new GradientTexture1D { Gradient = ramp, UseHdr = true },
        };
        return new GpuParticles3D
        {
            Amount = 48, Lifetime = 0.45, OneShot = true, Explosiveness = 1f, Emitting = false,
            LocalCoords = false, ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One * 0.16f, Material = ParticleMat(sparkTex!) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
        };
    }

    static Curve Shrink()
    {
        var c = new Curve();
        c.AddPoint(new Vector2(0, 1));
        c.AddPoint(new Vector2(0.6f, 0.6f));
        c.AddPoint(new Vector2(1, 0));
        return c;
    }

    static StandardMaterial3D ParticleMat(Texture2D tex) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = tex,
        DisableFog = true,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// <summary>Windup pulse while the flash sits at its pop point (Breach charge, Yoru fragment, KAY/O beeps, Vyse rose).
    /// <paramref name="t"/> is the time since release (seconds, ~0..1).</summary>
    public void Windup(float t)
    {
        windT = t;
        float freq = kind == Kind.Kayo ? 10f + 26f * t : 18f + 22f * t;
        float pulse = kind == Kind.Kayo
            ? (Mathf.Sin(t * freq) > 0.3f ? 1f : 0f)                 // beep-beep
            : 0.5f + 0.5f * Mathf.Sin(t * freq);
        float charge = Mathf.Clamp(t * 1.6f, 0f, 1f);
        body.Scale = Vector3.One * (1.2f + 0.5f * pulse + 0.5f * charge);
        halo.Scale = Vector3.One * (1.0f + 0.6f * pulse + 0.9f * charge);
        haloMat.SetShaderParameter("energy", 2.2f + 2.5f * pulse + 2f * charge);
        light.LightEnergy = 1.6f + 3f * pulse + 2f * charge;
        light.OmniRange = 4.5f + 2f * charge;
        // Charging ring that collapses onto the core.
        float ph = (t * (1.5f + 2.5f * charge)) % 1f;
        ring.Scale = Vector3.One * Mathf.Lerp(2.2f, 0.4f, ph);
        ringMat.SetShaderParameter("fade", 0.9f * Mathf.Sin(ph * Mathf.Pi));
    }

    /// <summary>Detonation: bright burst, then the node frees itself.</summary>
    public void Pop()
    {
        if (popT >= 0) return;
        popT = 0;
        body.Visible = halo.Visible = ring.Visible = false;
        trail.Emitting = false;
        flare.Visible = streak.Visible = shock.Visible = true;
        light.LightColor = Colors.White.Lerp(color, 0.25f);
        light.LightEnergy = 14f;
        light.OmniRange = 14f;
        burst.Restart();
        burst.Emitting = true;
        UpdatePop();
    }

    void UpdatePop()
    {
        float t = popT;
        float grow = Mathf.Clamp(t / 0.07f, 0f, 1f);
        float fadeOut = 1f - Mathf.Clamp((t - 0.06f) / 0.3f, 0f, 1f);
        flare.Scale = Vector3.One * Mathf.Lerp(1.5f, 7.5f, Mathf.Sqrt(grow));
        flareMat.SetShaderParameter("fade", fadeOut * fadeOut);
        flareMat.SetShaderParameter("energy", 6f + 6f * (1f - grow));
        streak.Scale = new Vector3(Mathf.Lerp(3f, 11f, grow), 0.32f, 1f);
        streakMat.SetShaderParameter("fade", Mathf.Max(0f, 1f - t / 0.22f));
        float sh = Mathf.Clamp(t / 0.32f, 0f, 1f);
        shock.Scale = Vector3.One * Mathf.Lerp(0.6f, 6.5f, 1f - (1f - sh) * (1f - sh));
        shockMat.SetShaderParameter("fade", (1f - sh) * 0.9f);
        light.LightEnergy = 14f * Mathf.Max(0f, 1f - t / 0.28f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        age += dt;
        if (popT >= 0)
        {
            popT += dt;
            UpdatePop();
            if (popT > 0.6f) QueueFree();
            return;
        }

        if (life > 0 && age > life) { QueueFree(); return; }
        // Start the trail once placed (avoids a streak from the origin on the spawn frame).
        if (!trail.Emitting && age > 0.02f && Visible) trail.Emitting = true;

        var p = GlobalPosition;
        if (hasLast)
        {
            var v = (p - lastPos) / Mathf.Max(dt, 1e-4f);
            if (v.LengthSquared() > 0.25f && kind is Kind.Skye or Kind.Yoru)
            {
                var dir = v.Normalized();
                body.LookAt(p + dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up);
            }
        }
        lastPos = p; hasLast = true;

        // Idle motion per agent.
        switch (kind)
        {
            case Kind.Phoenix:
                float flick = 0.85f + 0.3f * Mathf.Sin(age * 47f) * Mathf.Sin(age * 13f);
                if (windT < 0) { body.Scale = Vector3.One * flick; haloMat.SetShaderParameter("energy", 2.4f * flick); }
                break;
            case Kind.Yoru:
            case Kind.Vyse:
                body.RotateObjectLocal(Vector3.Up, dt * (kind == Kind.Yoru ? 9f : 2.5f));
                break;
            case Kind.Kayo:
                body.RotateObjectLocal(Vector3.Up, dt * 6f);
                break;
            case Kind.Gekko:
                float hop = Mathf.Abs(Mathf.Sin(age * 9f));
                body.Position = new Vector3(0, hop * 0.18f, 0);
                if (windT < 0) body.Scale = new Vector3(1f + 0.25f * (1f - hop), 1f - 0.2f * (1f - hop), 1f + 0.25f * (1f - hop));
                break;
        }
        if (windT < 0 && kind != Kind.Phoenix) haloMat.SetShaderParameter("energy", 2.2f + 0.4f * Mathf.Sin(age * 12f));
    }
}
