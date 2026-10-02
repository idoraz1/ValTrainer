using Godot;

namespace ValTrainer.Core;

/// <summary>
/// What this PC runs the game with: renderer (after Godot's automatic fallback Vulkan → Direct3D 12 → OpenGL 3 →
/// ANGLE), GPU and adapter type. Read on the main thread once the window exists (from Main._Ready on).
/// </summary>
public static class SysInfo
{
    /// <summary>"vulkan", "d3d12", "opengl3", "opengl3_angle" …</summary>
    public static string DriverName => Safe(RenderingServer.GetCurrentRenderingDriverName);

    /// <summary>"forward_plus" or "gl_compatibility" (after a fallback to OpenGL).</summary>
    public static string RenderingMethod => Safe(RenderingServer.GetCurrentRenderingMethod);

    /// <summary>True when the game runs on the Compatibility (OpenGL / ANGLE) renderer: simpler graphics, no SSAO,
    /// SDFGI, volumetric fog or screen-space reflections.</summary>
    public static bool IsCompatibility => RenderingMethod == "gl_compatibility";

    /// <summary>Human-readable renderer for the UI, e.g. "Vulkan 1.4 · Forward+", "Direct3D 12 · Forward+",
    /// "OpenGL 3.3 · Compatibility", "OpenGL ES (ANGLE) · Compatibility".</summary>
    public static string RendererName
    {
        get
        {
            string api = Safe(RenderingServer.GetVideoAdapterApiVersion);
            string ver = ShortVersion(api);
            string driver = DriverName switch
            {
                "vulkan" => "Vulkan" + (ver != "" ? " " + ver : ""),
                "d3d12" => "Direct3D 12",
                "opengl3" => "OpenGL" + (ver != "" ? " " + ver : ""),
                "opengl3_angle" => "OpenGL ES (ANGLE)",
                "opengl3_es" => "OpenGL ES",
                "" => "Unknown",
                var d => d,
            };
            string method = RenderingMethod switch
            {
                "forward_plus" => "Forward+",
                "mobile" => "Mobile",
                "gl_compatibility" => "Compatibility",
                var m => m,
            };
            return method == "" ? driver : $"{driver} · {method}";
        }
    }

    /// <summary>GPU name as the driver reports it, e.g. "Quadro P2000".</summary>
    public static string GpuName => Safe(RenderingServer.GetVideoAdapterName);

    public static string GpuVendor => Safe(RenderingServer.GetVideoAdapterVendor);

    /// <summary>Integrated / discrete / virtual / CPU (software). The Compatibility renderer can't tell and reports Other.</summary>
    public static RenderingDevice.DeviceType AdapterType
    {
        get { try { return RenderingServer.GetVideoAdapterType(); } catch { return RenderingDevice.DeviceType.Other; } }
    }

    /// <summary>One line for logs / bug reports.</summary>
    public static string Summary =>
        $"{RendererName} on {GpuName} ({GpuVendor}, {AdapterType}); {OS.GetName()} {Safe(OS.GetVersion)}, culture {Boot.SystemCulture}";

    /// <summary>
    /// Graphics preset for a first run (no settings file yet): Low on integrated / virtual / software GPUs and on the
    /// Compatibility renderer, otherwise Medium. Returns the preset and a short reason for the UI/log.
    /// </summary>
    public static (int Quality, string Reason) AutoQuality()
    {
        if (IsCompatibility) return (0, "Compatibility renderer (no Vulkan / Direct3D 12)");
        switch (AdapterType)
        {
            case RenderingDevice.DeviceType.IntegratedGpu: return (0, "integrated GPU");
            case RenderingDevice.DeviceType.VirtualGpu: return (0, "virtual GPU");
            case RenderingDevice.DeviceType.Cpu: return (0, "software rendering");
        }
        return (1, "dedicated GPU");
    }

    static string ShortVersion(string api)
    {
        // "1.4.312" -> "1.4"; "3.3.0 NVIDIA 582.08" -> "3.3"
        var first = api.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var parts = first.Split('.');
        return parts.Length >= 2 && parts[0].All(char.IsAsciiDigit) && parts[1].All(char.IsAsciiDigit) ? $"{parts[0]}.{parts[1]}" : "";
    }

    static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch { return ""; }
    }
}
