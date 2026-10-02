using Godot;

namespace ValTrainer.Game.Bots;

/// <summary>
/// Resources shared by every <see cref="BotCharacter"/> (loaded once): character scenes, the UAL animation library,
/// the locomotion/aim/hit/death blend tree, enemy highlight materials, the rifle, footstep sounds and the
/// per-body calibration (model scale so the head hitbox sits exactly at eye height, crouch depth).
/// </summary>
internal static class BotAssets
{
    public static readonly string[] Variants = { "Agent_Male", "Agent_Male_Tan", "Agent_Female", "Agent_Female_Tan" };
    public static bool IsFemale(int variant) => variant >= 2;

    /// <summary>Skull centre in the Head bone's local frame (bone Y runs up the head, Z forward; unscaled model metres).</summary>
    public static readonly Vector3 HeadOffset = new(0f, 0.085f, 0.012f);

    /// <summary>RightHand → rifle grip (from the asset manifest; barrel along the character's forward in the pistol poses).</summary>
    public static readonly Transform3D RifleInHand = new(
        new Basis(new Vector3(0.0467f, -0.0217f, -0.9987f), new Vector3(0.9817f, 0.1856f, 0.0419f), new Vector3(0.1844f, -0.9824f, 0.0300f)),
        new Vector3(0.005f, 0.065f, 0.003f));

    static readonly PackedScene?[] scenes = new PackedScene?[Variants.Length];
    static AnimationLibrary? lib;
    static PackedScene? rifle;
    static AudioStream[]? steps;
    static Shader? outlineShader, overlayShader, flashShader;
    static ShaderMaterial? flashMat;
    static QuadMesh? flashMesh;

    public static PackedScene Scene(int v) => scenes[v] ??= GD.Load<PackedScene>($"res://assets/characters/{Variants[v]}.gltf");
    public static AnimationLibrary Library => lib ??= GD.Load<AnimationLibrary>("res://assets/characters/animations/UAL1_Standard.glb");
    public static PackedScene Rifle => rifle ??= GD.Load<PackedScene>("res://assets/weapons/AssaultRifle_AK.tscn");

    public static AudioStream[] Steps
    {
        get
        {
            if (steps != null) return steps;
            var list = new List<AudioStream>();
            for (int i = 0; i < 5; i++)
            {
                var path = $"res://assets/audio/footsteps/footstep_concrete_{i:000}.ogg";
                if (ResourceLoader.Exists(path) && GD.Load<AudioStream>(path) is { } s) list.Add(s);
            }
            return steps = list.ToArray();
        }
    }

    // ---------------------------------------------------------------- skeleton bone indices (same rig for all agents)

    public static bool BonesReady;
    public static int BHips, BSpine, BChest, BUpperChest, BNeck, BHead,
        BLUpperArm, BLLowerArm, BLHand, BRUpperArm, BRLowerArm, BRHand,
        BLUpperLeg, BLLowerLeg, BLFoot, BLToes, BRUpperLeg, BRLowerLeg, BRFoot, BRToes;

    public static void InitBones(Skeleton3D s)
    {
        if (BonesReady) return;
        int F(string n) { int i = s.FindBone(n); if (i < 0) GD.PushError($"[BotCharacter] bone '{n}' missing"); return Math.Max(0, i); }
        BHips = F("Hips"); BSpine = F("Spine"); BChest = F("Chest"); BUpperChest = F("UpperChest"); BNeck = F("Neck"); BHead = F("Head");
        BLUpperArm = F("LeftUpperArm"); BLLowerArm = F("LeftLowerArm"); BLHand = F("LeftHand");
        BRUpperArm = F("RightUpperArm"); BRLowerArm = F("RightLowerArm"); BRHand = F("RightHand");
        BLUpperLeg = F("LeftUpperLeg"); BLLowerLeg = F("LeftLowerLeg"); BLFoot = F("LeftFoot"); BLToes = F("LeftToes");
        BRUpperLeg = F("RightUpperLeg"); BRLowerLeg = F("RightLowerLeg"); BRFoot = F("RightFoot"); BRToes = F("RightToes");
        BonesReady = true;
    }

    // ---------------------------------------------------------------- animation tree (shared resource; parameters live per AnimationTree)

    public static class P
    {
        public static readonly StringName WalkScale = "parameters/walk_ts/scale";
        public static readonly StringName RunScale = "parameters/run_ts/scale";
        public static readonly StringName CrouchWalkScale = "parameters/cwalk_ts/scale";
        public static readonly StringName Gait = "parameters/gait/blend_amount";
        public static readonly StringName Move = "parameters/move/blend_amount";
        public static readonly StringName CrouchMove = "parameters/cmove/blend_amount";
        public static readonly StringName Crouch = "parameters/crouch/blend_amount";
        public static readonly StringName Upper = "parameters/upper/blend_amount";
        public static readonly StringName HitChest = "parameters/hitc/request";
        public static readonly StringName HitHead = "parameters/hith/request";
        public static readonly StringName DeathSeek = "parameters/death_seek/seek_request";
        public static readonly StringName Die = "parameters/die/blend_amount";
    }

    static AnimationNodeBlendTree? tree;

    static bool IsUpper(string b)
    {
        if (b.StartsWith("ball")) return false;
        foreach (var k in UpperKeys) if (b.Contains(k)) return true;
        return false;
    }
    static readonly string[] UpperKeys = { "Spine", "Chest", "Neck", "Head", "Shoulder", "Arm", "Hand", "Index", "Middle", "Little", "Ring", "Thumb", "leaf_l", "leaf_r", "index_", "middle_", "pinky_", "ring_", "thumb_" };

    /// <summary>
    /// idle / walk / run (time-scaled, negative = backwards) → crouch blend → upper body (spine, arms, head) from the
    /// armed Pistol_Idle stance → Hit_Chest / Hit_Head one-shots on the spine → Death01.
    /// </summary>
    public static AnimationNodeBlendTree Tree(Skeleton3D skel)
    {
        if (tree != null) return tree;
        var bt = new AnimationNodeBlendTree();
        AnimationNodeAnimation A(string n) => new() { Animation = "ual/" + n };
        void Add(string name, AnimationNode node) => bt.AddNode(name, node);

        Add("idle", A("Idle"));
        Add("walk", A("Walk")); Add("walk_ts", new AnimationNodeTimeScale()); bt.ConnectNode("walk_ts", 0, "walk");
        Add("run", A("Jog_Fwd")); Add("run_ts", new AnimationNodeTimeScale()); bt.ConnectNode("run_ts", 0, "run");
        Add("gait", new AnimationNodeBlend2()); bt.ConnectNode("gait", 0, "walk_ts"); bt.ConnectNode("gait", 1, "run_ts");
        Add("move", new AnimationNodeBlend2()); bt.ConnectNode("move", 0, "idle"); bt.ConnectNode("move", 1, "gait");

        Add("cidle", A("Crouch_Idle"));
        Add("cwalk", A("Crouch_Fwd")); Add("cwalk_ts", new AnimationNodeTimeScale()); bt.ConnectNode("cwalk_ts", 0, "cwalk");
        Add("cmove", new AnimationNodeBlend2()); bt.ConnectNode("cmove", 0, "cidle"); bt.ConnectNode("cmove", 1, "cwalk_ts");
        Add("crouch", new AnimationNodeBlend2()); bt.ConnectNode("crouch", 0, "move"); bt.ConnectNode("crouch", 1, "cmove");

        var upper = new AnimationNodeBlend2 { FilterEnabled = true };
        var hitC = new AnimationNodeOneShot { FilterEnabled = true, FadeInTime = 0.04, FadeOutTime = 0.22 };
        var hitH = new AnimationNodeOneShot { FilterEnabled = true, FadeInTime = 0.04, FadeOutTime = 0.25 };
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            string b = skel.GetBoneName(i);
            var path = new NodePath("%GeneralSkeleton:" + b);
            if (IsUpper(b)) upper.SetFilterPath(path, true);
            if (b is "Spine" or "Chest" or "UpperChest" or "Neck" or "Head") { hitC.SetFilterPath(path, true); hitH.SetFilterPath(path, true); }
        }
        Add("aim", A("Pistol_Idle"));
        Add("upper", upper); bt.ConnectNode("upper", 0, "crouch"); bt.ConnectNode("upper", 1, "aim");
        Add("hit_chest", A("Hit_Chest"));
        Add("hitc", hitC); bt.ConnectNode("hitc", 0, "upper"); bt.ConnectNode("hitc", 1, "hit_chest");
        Add("hit_head", A("Hit_Head"));
        Add("hith", hitH); bt.ConnectNode("hith", 0, "hitc"); bt.ConnectNode("hith", 1, "hit_head");
        Add("death", A("Death01"));
        Add("death_seek", new AnimationNodeTimeSeek()); bt.ConnectNode("death_seek", 0, "death");
        Add("die", new AnimationNodeBlend2()); bt.ConnectNode("die", 0, "hith"); bt.ConnectNode("die", 1, "death_seek");
        bt.ConnectNode("output", 0, "die");
        return tree = bt;
    }

    // ---------------------------------------------------------------- per-body calibration

    public sealed class BodyCal
    {
        /// <summary>Uniform model scale: standing head-hitbox centre == PlayerView.EyeHeight.</summary>
        public float Scale = 1f;
        /// <summary>Crouch blend amount that puts the head hitbox 0.5 m lower than standing.</summary>
        public float CrouchAmount = 1f;
        /// <summary>Standing hips height (skeleton space, unscaled) — the reference for damping the run bounce.</summary>
        public float HipsY = 0.9f;
    }
    public static readonly BodyCal?[] Cal = new BodyCal?[2];

    // ---------------------------------------------------------------- materials

    public const float RimStrength = 0.6f;
    static readonly Dictionary<(Material, bool stencil, bool gun), Material> baseMats = new();
    static readonly Dictionary<(Color, bool, bool), ShaderMaterial> overlays = new();

    /// <summary>Base material for an enemy surface: writes stencil 1 (so the outline only lands outside the silhouette).</summary>
    public static Material Base(Material m, bool stencil, bool gun)
    {
        if (baseMats.TryGetValue((m, stencil, gun), out var r)) return r;
        r = m;
        if (m is BaseMaterial3D bm && (stencil || gun))
        {
            var c = (BaseMaterial3D)bm.Duplicate();
            if (stencil)
            {
                c.StencilMode = BaseMaterial3D.StencilModeEnum.Custom;
                c.StencilFlags = (int)BaseMaterial3D.StencilFlagsEnum.Write;
                c.StencilCompare = BaseMaterial3D.StencilCompareEnum.Always;
                c.StencilReference = 1;
            }
            if (gun)
            {
                // FBX import is flat matte: give the gun a little gunmetal sheen.
                c.Roughness = 0.45f;
                c.Metallic = 0.55f;
            }
            r = c;
        }
        baseMats[(m, stencil, gun)] = r;
        return r;
    }

    /// <summary>Additive fresnel/hit-flash overlay (+ outline next pass when highlights are on), shared per colour.
    /// <paramref name="rim"/> = false for hard-surface props (the gun) where a fresnel would flood the thin faces.</summary>
    public static ShaderMaterial Overlay(Color enemy, bool outlines, bool rim = true)
    {
        if (overlays.TryGetValue((enemy, outlines, rim), out var ov)) return ov;
        overlayShader ??= GD.Load<Shader>("res://assets/shaders/characters/enemy_overlay.gdshader");
        outlineShader ??= GD.Load<Shader>("res://assets/shaders/characters/enemy_outline.gdshader");
        ov = new ShaderMaterial { Shader = overlayShader };
        ov.SetShaderParameter("rim_color", enemy);
        ov.SetShaderParameter("rim_strength", outlines && rim ? RimStrength : 0f);
        if (outlines)
        {
            var ol = new ShaderMaterial { Shader = outlineShader };
            ol.SetShaderParameter("outline_color", enemy);
            ov.NextPass = ol;
        }
        overlays[(enemy, outlines, rim)] = ov;
        return ov;
    }

    public static QuadMesh FlashMesh
    {
        get
        {
            if (flashMesh != null) return flashMesh;
            flashShader ??= GD.Load<Shader>("res://assets/shaders/characters/muzzle_flash.gdshader");
            flashMat = new ShaderMaterial { Shader = flashShader };
            return flashMesh = new QuadMesh { Size = new Vector2(0.32f, 0.32f), Material = flashMat };
        }
    }
}
