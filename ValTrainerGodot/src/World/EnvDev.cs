using System.Globalization;
using Godot;

namespace ValTrainer.World;

/// <summary>
/// Developer command-line switches for the environment (ignored in normal play):
/// <c>--quality N</c> preset override, <c>--envperf</c> frame-time log, <c>--envcam x,y,z,yaw,pitch</c> fixed camera,
/// <c>--skyyaw deg</c> sky rotation, <c>--envscale s</c> 3D render scale (1.2 at 1600×900 = 1080p cost),
/// <c>--envtweak a,b</c> toggles for profiling / inspection: noshadow, hardshadow, noglow, nofog, noaerial, noadj, noaa,
/// msaa2, smaa, noaniso, mipbias, nosky, noreflect, ambcolor, aces, filmic, flat (untextured), fulllite, nodecor,
/// nobots, nossao, nosdfgi, sdfgilow, novfog, nossr, flashtest (needs --envcam).
/// </summary>
public static class EnvDev
{
    static bool read;
    static int? quality;
    static bool perf;
    static float[]? cam;
    static float? yaw, scale;
    static readonly HashSet<string> tweaks = new();

    static void Read()
    {
        if (read) return;
        read = true;
        var args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        static float? F(string? s) => s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        if (int.TryParse(Arg("--quality"), out var q)) quality = Math.Clamp(q, 0, 3);
        perf = args.Contains("--envperf");
        cam = Arg("--envcam")?.Split(',').Select(v => F(v) ?? 0f).ToArray();
        yaw = F(Arg("--skyyaw"));
        scale = F(Arg("--envscale"));
        foreach (var t in (Arg("--envtweak") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) tweaks.Add(t.Trim());
    }

    public static int Quality(int requested) { Read(); return Math.Clamp(quality ?? requested, 0, 3); }
    public static bool Perf { get { Read(); return perf; } }
    public static float[]? Cam { get { Read(); return cam; } }
    public static float? SkyYaw { get { Read(); return yaw; } }
    public static float? Scale { get { Read(); return scale; } }
    public static bool Has(string tweak) { Read(); return tweaks.Contains(tweak); }
}
