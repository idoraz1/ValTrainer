using Godot;

namespace ValTrainer.World;

/// <summary>
/// Sky (HDRI with the sun clamped out + a drawn sun disc), sun light, tonemapping/grade and the graphics
/// quality presets: 0 Competitive/Low, 1 Medium, 2 High, 3 Ultra.
/// </summary>
public static class WorldLighting
{
    static readonly Dictionary<string, ShaderMaterial> skyCache = new();

    public static string QualityName(int q) => q switch { 0 => "Low", 1 => "Medium", 2 => "High", _ => "Ultra" };

    public static void Build(Node3D root, Theme th, int q, Aabb bounds, float? yawOverride = null)
    {
        q = Math.Clamp(q, 0, 3);
        var sky = new Sky
        {
            SkyMaterial = SkyMaterial(th),
            RadianceSize = q >= 2 ? Sky.RadianceSizeEnum.Size256 : Sky.RadianceSizeEnum.Size128,
            ProcessMode = Sky.ProcessModeEnum.Quality,
        };
        float yaw = Mathf.DegToRad(yawOverride ?? th.SkyYaw);
        var horizon = th.Style == DecorStyle.Bind ? new Color(0.78f, 0.74f, 0.68f) : new Color(0.66f, 0.72f, 0.8f);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            SkyRotation = new Vector3(0, yaw, 0),
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            AmbientLightEnergy = th.Ambient,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,

            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = th.Exposure,
            TonemapAgxWhite = 10f,
            TonemapAgxContrast = 1.3f,

            GlowEnabled = true,
            GlowNormalized = false,
            GlowIntensity = 0.45f,
            GlowStrength = 1f,
            GlowBloom = 0.015f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            GlowHdrThreshold = 1.0f,
            GlowHdrScale = 2f,

            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = horizon,
            FogDensity = th.FogDensity,
            FogAerialPerspective = 0.55f,
            FogSkyAffect = 0f,

            AdjustmentEnabled = true,
            AdjustmentBrightness = 1f,
            AdjustmentContrast = th.Contrast,
            AdjustmentSaturation = th.Saturation,
        };

        // The sun sits where the panorama's sun is, after the sky rotation.
        var sunDir = (new Basis(Vector3.Up, yaw).Inverse() * Hdris.Sun(th.Hdri)).Normalized();
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = th.SunColor,
            LightEnergy = th.SunEnergy,
            LightAngularDistance = 0f,       // no PCSS: plain PCF soft shadows
            ShadowEnabled = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.1f,
            ShadowBlur = 1f,
            DirectionalShadowBlendSplits = q >= 2,
            DirectionalShadowFadeStart = 0.85f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky,
        };
        sun.Basis = Basis.LookingAt(-sunDir, Mathf.Abs(sunDir.Y) > 0.99f ? Vector3.Forward : Vector3.Up);

        ApplyQuality(env, sun, q, bounds);
        if (EnvDev.Has("noshadow")) sun.ShadowEnabled = false;
        if (EnvDev.Has("noglow")) env.GlowEnabled = false;
        if (EnvDev.Has("nofog")) env.FogEnabled = false;
        if (EnvDev.Has("noadj")) env.AdjustmentEnabled = false;
        if (EnvDev.Has("softlow")) RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
        if (EnvDev.Has("noblend")) sun.DirectionalShadowBlendSplits = false;
        if (EnvDev.Has("nosdfgi")) env.SdfgiEnabled = false;
        if (EnvDev.Has("sdfgilow")) { env.SdfgiCascades = 2; env.SdfgiMinCellSize = 0.5f; RenderingServer.EnvironmentSetSdfgiRayCount(RenderingServer.EnvironmentSdfgiRayCount.Count16); }
        if (EnvDev.Has("novfog")) env.VolumetricFogEnabled = false;
        if (EnvDev.Has("nossr")) env.SsrEnabled = false;
        if (EnvDev.Has("nossao")) env.SsaoEnabled = false;
        if (EnvDev.Has("aces")) env.TonemapMode = Godot.Environment.ToneMapper.Aces;
        if (EnvDev.Has("filmic")) env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        if (EnvDev.Has("hardshadow")) RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
        if (EnvDev.Has("noreflect")) env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        if (EnvDev.Has("ambcolor")) { env.AmbientLightSource = Godot.Environment.AmbientSource.Color; env.AmbientLightColor = new Color(0.55f, 0.62f, 0.72f); }
        if (EnvDev.Has("noaerial")) env.FogAerialPerspective = 0f;
        if (EnvDev.Has("nosky"))
        {
            env.BackgroundMode = Godot.Environment.BGMode.Color;
            env.BackgroundColor = new Color(0.55f, 0.68f, 0.85f);
            env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor = new Color(0.55f, 0.62f, 0.72f);
            env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        }
        root.AddChild(new WorldEnvironment { Name = "Env", Environment = env });
        root.AddChild(sun);

        // No ReflectionProbe: it cost ≈1.1 ms per frame here for barely visible gains on these rough surfaces.

        // Viewport-level settings need the viewport, which exists once the session adds the root.
        root.TreeEntered += () => ApplyViewport(root.GetViewport(), q);
    }

    static ShaderMaterial SkyMaterial(Theme th)
    {
        string key = $"{th.Hdri}|{th.SkyTint}|{th.SkySaturation}|{th.SkyEnergy}|{th.Ground}";
        if (skyCache.TryGetValue(key, out var m)) return m;
        m = new ShaderMaterial { Shader = SurfaceLib.SkyShader };
        m.SetShaderParameter("panorama", GD.Load<Texture2D>($"res://assets/hdri/{th.Hdri}.hdr"));
        m.SetShaderParameter("exposure", th.SkyEnergy);
        m.SetShaderParameter("clamp_lum", 4.5f);
        m.SetShaderParameter("saturation", th.SkySaturation);
        m.SetShaderParameter("tint", th.SkyTint);
        m.SetShaderParameter("ground_color", th.Ground);
        m.SetShaderParameter("ground_energy", 1.0f);
        m.SetShaderParameter("sun_energy", 30f);
        m.SetShaderParameter("sun_glow", 1.2f);
        skyCache[key] = m;
        return m;
    }

    static void ApplyQuality(Godot.Environment env, DirectionalLight3D sun, int q, Aabb bounds)
    {
        // ---- shadows ----
        RenderingServer.DirectionalShadowAtlasSetSize(q >= 2 ? 4096 : 2048, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(q switch
        {
            0 => RenderingServer.ShadowQuality.SoftVeryLow,
            1 => RenderingServer.ShadowQuality.SoftLow,
            2 => RenderingServer.ShadowQuality.SoftMedium,
            _ => RenderingServer.ShadowQuality.SoftMedium,
        });
        float reach = Mathf.Clamp(Mathf.Max(bounds.Size.X, bounds.Size.Z) * 1.1f, 50f, 110f);
        sun.DirectionalShadowMode = q >= 2 ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowMaxDistance = q switch { 0 => Mathf.Min(reach, 70f), 1 => Mathf.Min(reach, 80f), _ => reach };
        if (q >= 2) { sun.DirectionalShadowSplit1 = 0.07f; sun.DirectionalShadowSplit2 = 0.2f; sun.DirectionalShadowSplit3 = 0.48f; }
        else sun.DirectionalShadowSplit1 = 0.22f;

        // ---- glow (≈1.2 ms at 1080p on a GTX 1050-class GPU, so not on Low) / fog ----
        env.GlowEnabled = q >= 2;
        for (int i = 0; i < 7; i++) env.SetGlowLevel(i, 0f);
        env.SetGlowLevel(1, 0.5f); env.SetGlowLevel(2, 1f); env.SetGlowLevel(4, 0.6f);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(q >= 2);
        if (q == 0)
        {
            env.FogAerialPerspective = 0f;   // aerial perspective re-samples the sky per pixel (~0.3 ms)
            env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;   // sky reflections ≈0.4 ms
        }

        // ---- ambient occlusion / GI ----
        env.SsaoEnabled = q >= 1;
        env.SsaoRadius = 1.2f;
        env.SsaoIntensity = 1.6f;
        env.SsaoPower = 1.4f;
        env.SsaoDetail = 0.5f;
        env.SsaoHorizon = 0.06f;
        env.SsaoSharpness = 0.98f;
        env.SsaoLightAffect = 0.05f;
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, q <= 1, 0.5f, 2, 50f, 300f);

        env.SsilEnabled = false;   // ≈2.3 ms here; SDFGI covers bounce light on Ultra
        env.SsilRadius = 4f;
        env.SsilIntensity = 0.8f;
        RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Medium, true, 0.5f, 2, 50f, 300f);

        env.SdfgiEnabled = q >= 3;
        if (q >= 3)
        {
            env.SdfgiUseOcclusion = true;
            env.SdfgiReadSkyLight = true;
            env.SdfgiCascades = 2;           // 2 × 0.5 m cells cover 64 m / 128 m: enough for these maps, ≈4 ms cheaper
            env.SdfgiMinCellSize = 0.5f;
            env.SdfgiBounceFeedback = 0.4f;
            env.SdfgiEnergy = 0.9f;
            env.SdfgiNormalBias = 1.1f;
            env.SdfgiProbeBias = 1.1f;
            RenderingServer.EnvironmentSetSdfgiRayCount(RenderingServer.EnvironmentSdfgiRayCount.Count16);
            RenderingServer.GISetUseHalfResolution(true);
            env.AmbientLightEnergy *= 0.75f;
        }

        // ---- volumetric fog (sun shafts) ----
        env.VolumetricFogEnabled = q >= 3;
        if (q >= 3)
        {
            env.VolumetricFogDensity = 0.0035f;
            env.VolumetricFogAlbedo = new Color(0.92f, 0.95f, 1f);
            env.VolumetricFogAnisotropy = 0.55f;
            env.VolumetricFogLength = 64f;
            env.VolumetricFogSkyAffect = 0.15f;
            env.VolumetricFogAmbientInject = 0.4f;
            env.VolumetricFogTemporalReprojectionEnabled = true;
            RenderingServer.EnvironmentSetVolumetricFogVolumeSize(64, 48);
            RenderingServer.EnvironmentSetVolumetricFogFilterActive(true);
        }

        // ---- screen-space reflections ----
        env.SsrEnabled = q >= 3;
        env.SsrMaxSteps = 48;
        env.SsrFadeIn = 0.15f;
        env.SsrFadeOut = 2f;
        env.SsrDepthTolerance = 0.3f;
        RenderingServer.EnvironmentSetSsrHalfSize(true);
    }

    public static void ApplyViewport(Viewport? vp, int q)
    {
        if (vp == null) return;
        // MSAA is costly in Forward+ on this class of GPU (2x ≈ +2.5 ms with resolves), so the presets use post AA.
        vp.Msaa3D = Viewport.Msaa.Disabled;
        // FXAA costs ≈0.7 ms and SMAA ≈1.2 ms at 1080p on this class of GPU: Low skips post AA to hold 300+ FPS.
        vp.ScreenSpaceAA = q switch { 0 => Viewport.ScreenSpaceAAEnum.Disabled, 1 => Viewport.ScreenSpaceAAEnum.Fxaa, _ => Viewport.ScreenSpaceAAEnum.Smaa };
        vp.UseTaa = false;
        vp.UseDebanding = q >= 2;
        vp.AnisotropicFilteringLevel = q switch
        {
            0 => Viewport.AnisotropicFiltering.Anisotropy4X,
            1 => Viewport.AnisotropicFiltering.Anisotropy8X,
            _ => Viewport.AnisotropicFiltering.Anisotropy16X,
        };
        vp.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = EnvDev.Scale ?? 1f;
        if (EnvDev.Has("msaa2")) { vp.Msaa3D = Viewport.Msaa.Msaa2X; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled; }
        if (EnvDev.Has("smaa")) vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
        if (EnvDev.Has("mipbias")) vp.TextureMipmapBias = 0.5f;
        if (EnvDev.Has("noaniso")) vp.AnisotropicFilteringLevel = Viewport.AnisotropicFiltering.Disabled;
        if (EnvDev.Has("noaa")) { vp.Msaa3D = Viewport.Msaa.Disabled; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled; }
    }
}
