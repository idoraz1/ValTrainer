using Godot;

namespace ValTrainer.Game.Skins;

/// <summary>
/// The kill effects and finishers of the skins-3 lines (skins3-contracts §1–3), as compositions of <see cref="KillFxWorld"/>'s
/// building blocks: ground quads, camera-facing quads, shells (code-built meshes) and particle bursts, all drawn by
/// <c>assets/shaders/skins/skinfx*.gdshader</c>. Recipe ids are the line ids (kill effect: the 10 Signature / Apex lines;
/// finisher: the 3 Apex lines).
/// <para>Readability (the skins-2 playtest found the effects too faint on bright floors and too subtle): every effect has a
/// dark under-glow under its bright lines, a ground part about 2 m across plus a vertical part or burst that reads from
/// 30 m, and still stays short: kill effects ≤ 1 s and (Thunderhead's brief bolt and Aurora's low ring excepted) ≤ 1 m above
/// the feet; finishers ≤ 3 s and only on the round-winning kill of the last enemy (KillTracker), when nothing is left to aim at.</para>
/// <para>Budgets (Medium, P2000 class): a kill effect ≤ 32 particles + 3 quads; a finisher ≤ 160 particles and ≤ 8 meshes.</para>
/// </summary>
internal static class KillFxRecipes
{
    /// <summary>skinfx.gdshader kinds (keep in sync with the shader's K_ constants).</summary>
    public static class K
    {
        public const int Rose = 1, Bolt = 2, Scorch = 3, Basalt = 4, Scope = 5, Checker = 6, Frost = 7, IceSpikes = 8, Comb = 9,
            Pool = 10, Curtain = 11, CurtainRing = 12, Glow = 13, Orbit = 14, Chart = 15, Pulse = 16, Stream = 17, Beam = 18;
    }

    /// <summary>skinfx_particle.gdshader shapes.</summary>
    public static class Shape
    {
        public const int Mote = 0, Glass = 1, Grit = 2, Streak = 3, Crystal = 4, Droplet = 5, Steam = 6, Ember = 7, Chip = 8;
    }

    /// <summary>Every kill-effect recipe id (SkinLevel.KillFx): the Signature and Apex line ids.</summary>
    public static readonly string[] KillIds =
        { "glasswork", "thunderhead", "basalt", "oscilloscope", "rally", "frostbite", "hive", "foundry", "aurora", "orrery" };
    /// <summary>Every finisher recipe id (SkinLevel.Finisher): the Apex line ids.</summary>
    public static readonly string[] FinisherIds = { "foundry", "aurora", "orrery" };

    /// <summary>Adds the recipe's parts to <paramref name="s"/>; returns its length in seconds (0 parts = unknown id).</summary>
    /// <param name="finisher">Build the line's finisher instead of its kill effect.</param>
    public static float Build(KillFxWorld w, FxSlot s, string id, bool finisher) => finisher ? BuildFinisher(w, s, id) : BuildKill(w, s, id);

    // ---------------- kill effects (≤ 1 s) ----------------

    static float BuildKill(KillFxWorld w, FxSlot s, string id)
    {
        switch (id)
        {
            case "glasswork": // a stained-glass rose window lights up under the body and shatters; jewel shards burst from the chest
            {
                var rose = w.Quad(s, K.Rose, new Vector2(2.0f, 2.0f), ground: true);
                rose.Dur = 0.95f;
                var flash = w.Quad(s, K.Pulse, new Vector2(0.9f, 0.9f), ground: false, billboard: 2);
                flash.AtChest = true;
                flash.Dur = 0.3f;
                var shards = w.Burst(s, Assets.GlassShardProc, Shape.Glass, 16, 0.8f);
                shards.AtChest = true;
                return 0.95f;
            }
            case "thunderhead": // a forked bolt strikes the body (flickers 3 times), Lichtenberg scorch, blue-white sparks
            {
                var bolt = w.Quad(s, K.Bolt, new Vector2(0.9f, 2.8f), ground: false, billboard: 1);
                bolt.Offset = new Vector3(0, 1.4f, 0);
                bolt.Dur = 0.6f;
                var scorch = w.Quad(s, K.Scorch, new Vector2(2.2f, 2.2f), ground: true);
                scorch.Dur = 0.95f;
                var sparks = w.Burst(s, Assets.SparkUpProc, Shape.Streak, 18, 0.45f);
                sparks.UseAccent = true;
                sparks.Offset = new Vector3(0, 0.05f, 0);
                return 0.95f;
            }
            case "basalt": // hex columns crack open with dim red seams; rock grit and a few embers jump off
            {
                var cols = w.Quad(s, K.Basalt, new Vector2(2.0f, 2.0f), ground: true);
                cols.Dur = 0.95f;
                var grit = w.Burst(s, Assets.GritProc, Shape.Grit, 14, 0.75f);
                grit.Offset = new Vector3(0, 0.05f, 0);
                var embers = w.Burst(s, Assets.EmberRiseProc, Shape.Ember, 10, 0.85f);
                embers.Offset = new Vector3(0, 0.1f, 0);
                return 0.95f;
            }
            case "oscilloscope": // a CRT disc on the floor: the polar trace spikes, flatlines to a ring and shrinks; a green snap
            {
                var scope = w.Quad(s, K.Scope, new Vector2(2.0f, 2.0f), ground: true);
                scope.Dur = 0.95f;
                var snap = w.Quad(s, K.Pulse, new Vector2(0.8f, 0.8f), ground: false, billboard: 2);
                snap.AtChest = true;
                snap.Dur = 0.25f;
                var sparks = w.Burst(s, Assets.SnapProc, Shape.Streak, 14, 0.35f);
                sparks.AtChest = true;
                return 0.95f;
            }
            case "rally": // a chequered band rolls out from the feet with the livery stripes; livery chips fly
            {
                var flag = w.Quad(s, K.Checker, new Vector2(2.3f, 2.3f), ground: true);
                flag.Dur = 0.95f;
                var chips = w.Burst(s, Assets.ChipProc, Shape.Chip, 18, 0.9f);
                chips.AtChest = true;
                return 0.95f;
            }
            case "frostbite": // ice crystals shoot up around the feet over a six-armed frost fracture, crystal shards
            {
                var frost = w.Quad(s, K.Frost, new Vector2(2.2f, 2.2f), ground: true);
                frost.Dur = 0.95f;
                var spikes = w.Quad(s, K.IceSpikes, new Vector2(1.5f, 1.0f), ground: false, billboard: 1);
                spikes.Offset = new Vector3(0, 0.5f, -0.05f);
                spikes.Dur = 0.95f;
                var shards = w.Burst(s, Assets.CrystalProc, Shape.Crystal, 12, 0.7f);
                shards.AtChest = true;
                shards.UseAccent = true;
                return 0.95f;
            }
            case "hive": // honeycomb cells fill with glowing amber around the feet; amber droplets splash from the chest
            {
                var comb = w.Quad(s, K.Comb, new Vector2(2.0f, 2.0f), ground: true);
                comb.Dur = 0.95f;
                var drops = w.Burst(s, Assets.HoneyProc, Shape.Droplet, 16, 0.75f);
                drops.AtChest = true;
                return 0.95f;
            }
            case "foundry": // a molten splash pool spreads and crusts; molten droplets and sparks spray up
            {
                var pool = w.Quad(s, K.Pool, new Vector2(1.7f, 1.7f), ground: true);
                pool.Dur = 0.95f;
                var drops = w.Burst(s, Assets.MoltenProc, Shape.Droplet, 14, 0.7f);
                drops.Offset = new Vector3(0, 0.05f, 0);
                var sparks = w.Burst(s, Assets.SparkUpProc, Shape.Streak, 14, 0.5f);
                sparks.UseAccent = true;
                sparks.Offset = new Vector3(0, 0.05f, 0);
                return 0.95f;
            }
            case "aurora": // a low ring of northern light rises around the body over a green floor glow; glitter drifts up
            {
                var ring = w.Shell(s, K.CurtainRing, Assets.Cylinder, new Vector3(1.6f, 1.3f, 1.6f));
                ring.Offset = new Vector3(0, 0.65f, 0);
                ring.Dur = 0.95f;
                var glow = w.Quad(s, K.Glow, new Vector2(2.2f, 2.2f), ground: true);
                glow.Dur = 0.9f;
                var motes = w.Burst(s, Assets.GlitterProc, Shape.Mote, 16, 0.9f);
                motes.Offset = new Vector3(0, 0.2f, 0);
                return 0.95f;
            }
            case "orrery": // two tilted orbits draw themselves round the chest with their planets; a star chart on the floor
            {
                var o1 = w.Quad(s, K.Orbit, new Vector2(1.4f, 1.4f), ground: false);
                o1.AtChest = true;
                o1.Rot = new Vector3(-1.15f, 0.3f, 0f);
                o1.Anim = SpinSlow;
                o1.Dur = 0.95f;
                var o2 = w.Quad(s, K.Orbit, new Vector2(1.0f, 1.0f), ground: false);
                o2.AtChest = true;
                o2.Rot = new Vector3(-1.9f, -0.5f, 2f);
                o2.Anim = SpinFast;
                o2.Dur = 0.95f;
                var chart = w.Quad(s, K.Chart, new Vector2(1.9f, 1.9f), ground: true);
                chart.Dur = 0.95f;
                var motes = w.Burst(s, Assets.GoldMoteProc, Shape.Mote, 12, 0.7f);
                motes.AtChest = true;
                return 0.95f;
            }
        }
        return 0f;
    }

    // ---------------- finishers (≤ 3 s, the round-winning kill of the last enemy only) ----------------

    static float BuildFinisher(KillFxWorld w, FxSlot s, string id)
    {
        switch (id)
        {
            case "foundry": // molten metal pours from a glowing spout over the body into a spreading pool, then a quench burst of steam
            {
                var spout = w.Quad(s, K.Pulse, new Vector2(0.7f, 0.7f), ground: false, billboard: 2);
                spout.Offset = new Vector3(0, 2.75f, 0);
                spout.Dur = 0.55f;
                var stream = w.Shell(s, K.Stream, Assets.Cylinder, new Vector3(0.26f, 2.7f, 0.26f));
                stream.Offset = new Vector3(0, 1.35f, 0);
                stream.Start = 0.1f;
                stream.Dur = 1.6f;
                stream.Anim = PourWobble;
                var pool = w.Quad(s, K.Pool, new Vector2(2.7f, 2.7f), ground: true);
                pool.Start = 0.25f;
                pool.Dur = 2.65f;
                var heat = w.Quad(s, K.Glow, new Vector2(3.2f, 3.2f), ground: true);
                heat.Start = 0.25f;
                heat.Dur = 1.6f;
                var embers = w.Burst(s, Assets.SplashEmberProc, Shape.Ember, 46, 0.6f, stream: true);
                embers.Offset = new Vector3(0, 0.15f, 0);
                embers.Start = 0.3f;
                embers.Dur = 1.3f;
                var drops = w.Burst(s, Assets.MoltenProc, Shape.Droplet, 28, 0.6f, stream: true);
                drops.Offset = new Vector3(0, 0.1f, 0);
                drops.Start = 0.3f;
                drops.Dur = 1.3f;
                var quench = w.Quad(s, K.Pulse, new Vector2(2.4f, 2.4f), ground: false, billboard: 2);
                quench.Offset = new Vector3(0, 0.7f, 0);
                quench.Start = 1.72f;
                quench.Dur = 0.6f;
                var steam = w.Burst(s, Assets.SteamProc, Shape.Steam, 30, 1.15f);
                steam.Offset = new Vector3(0, 0.2f, 0);
                steam.Start = 1.72f;
                var hiss = w.Burst(s, Assets.HissProc, Shape.Mote, 24, 0.7f);
                hiss.UseAccent = true;
                hiss.Offset = new Vector3(0, 0.3f, 0);
                hiss.Start = 1.74f;
                return 2.95f;
            }
            case "aurora": // a curtain of northern light sweeps round the body and fades; a second, nearer fold follows; glitter
            {
                var outer = w.Shell(s, K.Curtain, Assets.Cylinder, new Vector3(3.0f, 3.2f, 3.0f));
                outer.Offset = new Vector3(0, 1.6f, 0);
                outer.Dur = 2.8f;
                var inner = w.Shell(s, K.Curtain, Assets.Cylinder, new Vector3(2.0f, 2.4f, 2.0f));
                inner.Offset = new Vector3(0, 1.2f, 0);
                inner.Rot = new Vector3(0, 0.9f, 0);
                inner.Start = 0.35f;
                inner.Dur = 2.45f;
                var glow = w.Quad(s, K.Glow, new Vector2(3.4f, 3.4f), ground: true);
                glow.Dur = 2.8f;
                var glitter = w.Burst(s, Assets.CurtainGlitterProc, Shape.Mote, 60, 1.0f, stream: true);
                glitter.Offset = new Vector3(0, 0.3f, 0);
                glitter.Start = 0.2f;
                glitter.Dur = 1.8f;
                var crystals = w.Burst(s, Assets.CrystalProc, Shape.Crystal, 16, 0.7f);
                crystals.AtChest = true;
                crystals.UseAccent = true;
                return 2.9f;
            }
            case "orrery": // three planetary rings tilt into alignment round the body, then a pulse of light and a beam
            {
                var r1 = w.Quad(s, K.Orbit, new Vector2(1.5f, 1.5f), ground: false);
                r1.Offset = new Vector3(0, 1.05f, 0);
                r1.Anim = Align1;
                r1.Dur = 2.2f;
                var r2 = w.Quad(s, K.Orbit, new Vector2(2.2f, 2.2f), ground: false);
                r2.Offset = new Vector3(0, 1.05f, 0);
                r2.Anim = Align2;
                r2.Start = 0.08f;
                r2.Dur = 2.12f;
                var r3 = w.Quad(s, K.Orbit, new Vector2(2.9f, 2.9f), ground: false);
                r3.Offset = new Vector3(0, 1.05f, 0);
                r3.Anim = Align3;
                r3.Start = 0.16f;
                r3.Dur = 2.04f;
                var chart = w.Quad(s, K.Chart, new Vector2(3.0f, 3.0f), ground: true);
                chart.Start = 0.1f;
                chart.Dur = 2.6f;
                var pulse = w.Quad(s, K.Pulse, new Vector2(3.2f, 3.2f), ground: false, billboard: 2);
                pulse.Offset = new Vector3(0, 1.05f, 0);
                pulse.Start = 1.25f;
                pulse.Dur = 0.9f;
                var beam = w.Quad(s, K.Beam, new Vector2(0.6f, 6.0f), ground: false, billboard: 1);
                beam.Offset = new Vector3(0, 3.0f, 0);
                beam.Start = 1.22f;
                beam.Dur = 1.5f;
                var glow = w.Quad(s, K.Glow, new Vector2(3.2f, 3.2f), ground: true);
                glow.Start = 1.22f;
                glow.Dur = 1.4f;
                var motes = w.Burst(s, Assets.AlignBurstProc, Shape.Mote, 56, 1.0f);
                motes.Offset = new Vector3(0, 1.05f, 0);
                motes.Start = 1.25f;
                return 2.8f;
            }
        }
        return 0f;
    }

    // ---------------- part motion (static: no allocation per spawn) ----------------

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
    static float EaseInOut(float x) => x * x * (3f - 2f * x);

    static void SpinSlow(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot) => rot.Z += t * 2.2f;
    static void SpinFast(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot) => rot.Z -= t * 3.4f;

    /// <summary>Foundry's pour: a slight sway of the falling stream.</summary>
    static void PourWobble(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot)
    {
        rot.Z = 0.03f * Mathf.Sin(t * 19f);
        size.X *= 1f + 0.12f * Mathf.Sin(t * 31f);
        size.Z = size.X;
    }

    /// <summary>Orrery: a ring starts tilted (its own tilt and azimuth), spins in its plane, eases flat (aligned with the
    /// others) by 55 % of its life, holds while the pulse fires, then widens and fades.</summary>
    static void Align(float t, ref Vector3 size, ref Vector3 rot, float tiltX, float tiltZ, float az, float spin)
    {
        float k = EaseInOut(Mathf.Clamp(t / 0.55f, 0f, 1f));
        rot = new Vector3(-Mathf.Pi / 2f + tiltX * (1f - k), az * (1f - k), tiltZ * (1f - k) + spin * t);
        if (t > 0.6f) size *= 1f + EaseOut((t - 0.6f) / 0.4f) * 0.6f;
    }

    static void Align1(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot) => Align(t, ref size, ref rot, 0.9f, 0.5f, 0.6f, 4.2f);
    static void Align2(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot) => Align(t, ref size, ref rot, -0.7f, -0.8f, -0.9f, -3.1f);
    static void Align3(float t, ref Vector3 off, ref Vector3 size, ref Vector3 rot) => Align(t, ref size, ref rot, 0.4f, 1.1f, 1.4f, 2.3f);

    // ---------------- shared resources (created once) ----------------

    /// <summary>Create-once materials, meshes and particle process materials of the skin effects.</summary>
    internal static class Assets
    {
        const string Dir = "res://assets/shaders/skins/";
        static ShaderMaterial? fxMat, particleMat;
        static QuadMesh? quad, particleQuad;
        static CylinderMesh? cylinder;

        public static ShaderMaterial FxMat => fxMat ??= new ShaderMaterial { Shader = GD.Load<Shader>(Dir + "skinfx.gdshader") };
        public static ShaderMaterial ParticleMat => particleMat ??= new ShaderMaterial { Shader = GD.Load<Shader>(Dir + "skinfx_particle.gdshader") };

        /// <summary>1 × 1 quad in XY (ground quads are laid flat by their node; billboards face the camera in the shader).</summary>
        public static QuadMesh Quad => quad ??= new QuadMesh { Size = Vector2.One, Material = FxMat };
        public static QuadMesh ParticleQuad => particleQuad ??= new QuadMesh { Size = Vector2.One, Material = ParticleMat };
        /// <summary>Open unit cylinder (radius 0.5, height 1): Aurora's curtains, Foundry's pour.</summary>
        public static CylinderMesh Cylinder => cylinder ??= new CylinderMesh
        {
            TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 40, Rings = 3, CapTop = false, CapBottom = false, Material = FxMat,
        };
        /// <summary>The HUD face for text labels (none of the skins-3 recipes uses one; kept for KillFxWorld.Label).</summary>
        public static Font? LabelFont => ValTrainer.UI.UiTheme.Hud;

        internal static CurveTexture Curve(params Vector2[] pts)
        {
            var c = new Curve();
            foreach (var p in pts) c.AddPoint(p);
            return new CurveTexture { Curve = c };
        }

        static ParticleProcessMaterial? glassShard, sparkUp, grit, emberRise, snap, chip, crystal, honey, molten, glitter,
            splashEmber, steam, hiss, curtainGlitter, goldMote, alignBurst;

        /// <summary>Glasswork: jewel shards fly out of the chest, tumbling, and fall.</summary>
        public static ParticleProcessMaterial GlassShardProc => glassShard ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 160f, InitialVelocityMin = 1.4f, InitialVelocityMax = 2.6f,
            Gravity = new Vector3(0, -7f, 0), DampingMin = 0.8f, DampingMax = 1.5f,
            ScaleMin = 0.09f, ScaleMax = 0.16f, AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -420f, AngularVelocityMax = 420f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.12f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Sparks thrown up and out from the floor (Thunderhead, Foundry).</summary>
        public static ParticleProcessMaterial SparkUpProc => sparkUp ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 55f, InitialVelocityMin = 2.5f, InitialVelocityMax = 5f,
            Gravity = new Vector3(0, -9.8f, 0), DampingMin = 1f, DampingMax = 2f, ScaleMin = 0.07f, ScaleMax = 0.11f,
            ParticleFlagAlignY = true, ScaleCurve = Curve(new Vector2(0, 1), new Vector2(1, 0.3f)),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.3f, EmissionRingInnerRadius = 0.05f, EmissionRingHeight = 0.02f, LifetimeRandomness = 0.4f,
        };

        /// <summary>Basalt: dark rock grit jumps off the cracked columns.</summary>
        public static ParticleProcessMaterial GritProc => grit ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 40f, InitialVelocityMin = 1.6f, InitialVelocityMax = 3.2f,
            Gravity = new Vector3(0, -9.8f, 0), ScaleMin = 0.06f, ScaleMax = 0.12f,
            AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -360f, AngularVelocityMax = 360f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.6f, EmissionRingInnerRadius = 0.1f, EmissionRingHeight = 0.02f, LifetimeRandomness = 0.25f,
        };

        /// <summary>Embers drifting up slowly.</summary>
        public static ParticleProcessMaterial EmberRiseProc => emberRise ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 30f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f,
            Gravity = new Vector3(0, 0.6f, 0), DampingMin = 0.5f, DampingMax = 1f, ScaleMin = 0.035f, ScaleMax = 0.06f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.6f, EmissionRingInnerRadius = 0.05f, EmissionRingHeight = 0.02f, LifetimeRandomness = 0.4f,
        };

        /// <summary>Oscilloscope: a flat electric snap: short streaks flung sideways from the chest.</summary>
        public static ParticleProcessMaterial SnapProc => snap ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Right, Spread = 180f, Flatness = 0.9f, InitialVelocityMin = 3f, InitialVelocityMax = 5.5f,
            Gravity = Vector3.Zero, DampingMin = 6f, DampingMax = 9f, ScaleMin = 0.06f, ScaleMax = 0.1f, ParticleFlagAlignY = true,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.08f,
        };


        /// <summary>Rally: livery chips tumble up and flutter down.</summary>
        public static ParticleProcessMaterial ChipProc => chip ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 2.2f, InitialVelocityMax = 3.4f,
            Gravity = new Vector3(0, -5.5f, 0), DampingMin = 1.5f, DampingMax = 2.5f, ScaleMin = 0.06f, ScaleMax = 0.09f,
            AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -540f, AngularVelocityMax = 540f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.15f, LifetimeRandomness = 0.2f,
        };

        /// <summary>Frostbite / Aurora: ice crystals burst out and fall.</summary>
        public static ParticleProcessMaterial CrystalProc => crystal ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 150f, InitialVelocityMin = 1.2f, InitialVelocityMax = 2.4f,
            Gravity = new Vector3(0, -6f, 0), DampingMin = 1f, DampingMax = 1.5f, ScaleMin = 0.08f, ScaleMax = 0.14f,
            AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -300f, AngularVelocityMax = 300f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.12f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Hive: amber droplets thrown out, stretched along their fall.</summary>
        public static ParticleProcessMaterial HoneyProc => honey ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 120f, InitialVelocityMin = 1.2f, InitialVelocityMax = 2.4f,
            Gravity = new Vector3(0, -9.8f, 0), DampingMin = 0.5f, DampingMax = 1f, ScaleMin = 0.06f, ScaleMax = 0.1f,
            ParticleFlagAlignY = true, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.1f,
            LifetimeRandomness = 0.25f,
        };

        /// <summary>Foundry: molten droplets splash up from the floor.</summary>
        public static ParticleProcessMaterial MoltenProc => molten ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 35f, InitialVelocityMin = 2f, InitialVelocityMax = 3.6f,
            Gravity = new Vector3(0, -9.8f, 0), ScaleMin = 0.05f, ScaleMax = 0.09f, ParticleFlagAlignY = true,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.35f, EmissionRingInnerRadius = 0.05f, EmissionRingHeight = 0.02f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Aurora kill: glitter rising slowly around the body.</summary>
        public static ParticleProcessMaterial GlitterProc => glitter ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 25f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.0f,
            Gravity = new Vector3(0, 0.3f, 0), ScaleMin = 0.03f, ScaleMax = 0.06f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.8f, EmissionRingInnerRadius = 0.6f, EmissionRingHeight = 0.6f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Foundry finisher: embers splashing off the pour's impact.</summary>
        public static ParticleProcessMaterial SplashEmberProc => splashEmber ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 65f, InitialVelocityMin = 1.8f, InitialVelocityMax = 3.8f,
            Gravity = new Vector3(0, -7f, 0), DampingMin = 0.5f, DampingMax = 1f, ScaleMin = 0.035f, ScaleMax = 0.06f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.18f, LifetimeRandomness = 0.4f,
        };

        /// <summary>Foundry quench: a burst of steam billows up and spreads.</summary>
        public static ParticleProcessMaterial SteamProc => steam ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 50f, InitialVelocityMin = 1.4f, InitialVelocityMax = 3.4f,
            Gravity = new Vector3(0, 0.8f, 0), DampingMin = 2f, DampingMax = 3.2f, ScaleMin = 0.45f, ScaleMax = 0.85f,
            AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -40f, AngularVelocityMax = 40f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.9f, EmissionRingInnerRadius = 0.1f, EmissionRingHeight = 0.1f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Foundry quench: bright specks spat out with the steam.</summary>
        public static ParticleProcessMaterial HissProc => hiss ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 80f, InitialVelocityMin = 2f, InitialVelocityMax = 4.5f,
            Gravity = new Vector3(0, -4f, 0), DampingMin = 2f, DampingMax = 3f, ScaleMin = 0.025f, ScaleMax = 0.045f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.5f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Aurora finisher: glitter falling through the curtain.</summary>
        public static ParticleProcessMaterial CurtainGlitterProc => curtainGlitter ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 20f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f,
            Gravity = new Vector3(0, 0.25f, 0), ScaleMin = 0.03f, ScaleMax = 0.06f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 1.5f, EmissionRingInnerRadius = 0.6f, EmissionRingHeight = 1.2f, LifetimeRandomness = 0.3f,
        };

        /// <summary>Orrery kill: gold motes drift out of the orbits.</summary>
        public static ParticleProcessMaterial GoldMoteProc => goldMote ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f,
            Gravity = new Vector3(0, 0.2f, 0), DampingMin = 1f, DampingMax = 2f, ScaleMin = 0.03f, ScaleMax = 0.06f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.5f,
        };

        /// <summary>Orrery finisher: the alignment pulse throws gold motes out in a flat disc.</summary>
        public static ParticleProcessMaterial AlignBurstProc => alignBurst ??= new ParticleProcessMaterial
        {
            Direction = Vector3.Right, Spread = 180f, Flatness = 0.85f, InitialVelocityMin = 2.5f, InitialVelocityMax = 4.5f,
            Gravity = new Vector3(0, 0.3f, 0), DampingMin = 2f, DampingMax = 3f, ScaleMin = 0.04f, ScaleMax = 0.07f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.2f, LifetimeRandomness = 0.3f,
        };
    }
}
