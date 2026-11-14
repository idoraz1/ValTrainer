using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;

namespace ValTrainer.Core;

/// <summary>
/// Graphics-driver crash recovery (bug report: v1.19 closed itself on an Intel Iris Plus laptop as soon as a bot appeared,
/// with nothing in the log: the Vulkan driver crashed). At startup, before anything else:
/// <list type="bullet">
/// <item>If the previous session didn't close normally (its log lacks <see cref="UI.BugReport.CleanExit"/>) and it ran on
/// Vulkan, the renderer steps down to DirectX 12; a crash on DirectX 12 steps down to OpenGL (the "safe graphics"
/// renderer). Each crashed log is only counted once.</item>
/// <item>The chosen renderer (Settings → Graphics → Renderer, or the step-down) is applied by restarting ValTrainer once with
/// Godot's --rendering-driver / --rendering-method arguments. A renderer given on the command line (the "safe graphics"
/// shortcut) always wins and turns the guard off.</item>
/// </list>
/// Never in dev runs.
/// </summary>
public static class RendererGuard
{
    public const string Auto = "auto", Vulkan = "vulkan", D3D12 = "d3d12", OpenGl = "opengl";
    public static readonly string[] Choices = { Auto, Vulkan, D3D12, OpenGl };

    /// <summary>The renderer this session is drawing with (Godot's driver name: vulkan / d3d12 / opengl3).</summary>
    public static string Current => RenderingServer.GetCurrentRenderingDriverName().ToLowerInvariant();

    static bool ForcedOnCommandLine(string[] args) => args.Any(a => a is "--rendering-driver" or "--rendering-method");

    /// <summary>Call first thing in Main._Ready. True = a restart with another renderer was launched: quit now.</summary>
    public static bool CheckAtStartup(AppSettings st, string[] args)
    {
        if (args.Contains("--dev")) return false;
        if (ForcedOnCommandLine(args)) return false;
        string cur = Current;
        try { StepDownAfterCrash(st, Paths.LogDir, save: true); }
        catch (System.Exception e) { Log.Error($"Renderer guard: {Log.Describe(e)}"); }
        string want = st.Renderer ?? Auto;
        if (want == Auto || Matches(want, cur)) return false;
        return Relaunch(want);
    }

    static bool Matches(string choice, string driver) => choice switch
    {
        Vulkan => driver == "vulkan",
        D3D12 => driver == "d3d12",
        OpenGl => driver.StartsWith("opengl"),
        _ => true,
    };

    /// <summary>The previous session's log (Godot renames it to godot&lt;date&gt;.log on the next start) without the clean-exit
    /// line = a crash. Step the renderer down once per crashed log.</summary>
    internal static void StepDownAfterCrash(AppSettings st, string dir, bool save)
    {
        if (!Directory.Exists(dir)) return;
        var prev = new DirectoryInfo(dir).GetFiles("godot*.log")
            .Where(f => !f.Name.Equals("godot.log", System.StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (prev == null || st.RendererCrashSeen == prev.Name) return;
        string text;
        using (var fs = new FileStream(prev.FullName, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var rd = new StreamReader(fs)) text = rd.ReadToEnd();
        st.RendererCrashSeen = prev.Name;
        if (text.Contains("[ValTrainer] " + UI.BugReport.CleanExit)) { if (save) st.Save(); return; }
        // which renderer the crashed session used (Godot's first lines: "Vulkan 1.3 …" / "D3D12 …" / "OpenGL …")
        string? crashed = text.Contains("Vulkan") ? Vulkan : text.Contains("D3D12") ? D3D12 : null;
        string? next = crashed switch { Vulkan => D3D12, D3D12 => OpenGl, _ => null };
        if (next != null && (st.Renderer is null or Auto || st.Renderer == crashed))
        {
            Log.Info($"Renderer guard: the last session ({prev.Name}) didn't close normally on {crashed}; switching to {next}");
            st.Renderer = next;
            st.RendererNotice = next;
        }
        if (save) st.Save();
    }

    static bool Relaunch(string want)
    {
        try
        {
            string exe = OS.GetExecutablePath();
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
            if (want == OpenGl) { psi.ArgumentList.Add("--rendering-method"); psi.ArgumentList.Add("gl_compatibility"); }
            else { psi.ArgumentList.Add("--rendering-driver"); psi.ArgumentList.Add(want); }
            foreach (var a in System.Environment.GetCommandLineArgs().Skip(1)) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
            Log.Info($"Renderer guard: restarting with {want}");
            return true;
        }
        catch (System.Exception e) { Log.Error($"Renderer guard: couldn't restart with {want} ({Log.Describe(e)})"); return false; }
    }
}
