using System.Globalization;
using System.Runtime.CompilerServices;
using Godot;

namespace ValTrainer.Core;

/// <summary>
/// Process-wide startup fixes that must be in place before anything parses or formats a number.
/// <para>Culture: Valorant's ini files, our JSON/telemetry and every number on screen use '.' decimals. On a PC with a
/// comma-decimal locale (de-DE, fr-FR, ru-RU, pt-BR …) culture-sensitive Parse/ToString/interpolation would read
/// "0.35" as 35 or show "0,35", so the whole app runs in the invariant culture. This runs as a module initializer,
/// i.e. when Godot first loads the game assembly — before Main's constructor loads the settings.</para>
/// <para>Dev-only (<c>--dev --culture de-DE</c>): runs the app in that culture INSTEAD of the invariant one, i.e. as a
/// comma-decimal PC would without this fix, to prove the parsers don't depend on it (see <see cref="CultureCheck"/>).</para>
/// </summary>
public static class Boot
{
    /// <summary>The OS/user culture the process started with (diagnostics only).</summary>
    public static string SystemCulture { get; private set; } = "";

    /// <summary>The culture the app runs in: invariant, unless a dev run forced another one with --culture.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    static bool done;

    [ModuleInitializer]
    internal static void Init()
    {
        if (done) return;
        done = true;
        SystemCulture = CultureInfo.CurrentCulture.Name;
        var culture = CultureInfo.InvariantCulture;
        // Plain .NET args (no Godot API this early); they hold the whole command line, engine and user args alike.
        string[] args;
        try { args = System.Environment.GetCommandLineArgs(); } catch { args = Array.Empty<string>(); }
        if (args.Contains("--dev"))
        {
            int i = Array.IndexOf(args, "--culture");
            if (i >= 0 && i + 1 < args.Length)
                try { culture = CultureInfo.GetCultureInfo(args[i + 1]); } catch { /* unknown name: stay invariant */ }
        }
        Apply(culture);
    }

    static bool handlers;

    /// <summary>
    /// Exceptions in Godot callbacks (_Process, _Input, signals …) are caught by Godot, logged to stderr and
    /// user://logs/godot.log, and the game keeps running. These handlers log the rest (worker threads, faulted tasks
    /// nobody awaited). Called from Main._Ready, i.e. only in the running game: in the editor a static subscription
    /// to AppDomain events would keep the game assembly from unloading on rebuild.
    /// </summary>
    public static void InstallCrashHandlers()
    {
        if (handlers) return;
        handlers = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception{(e.IsTerminating ? " (fatal)" : "")}: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>Sets the culture for this thread and every thread that hasn't chosen one (thread pool, tasks).</summary>
    public static void Apply(CultureInfo c)
    {
        Culture = c;
        CultureInfo.DefaultThreadCurrentCulture = c;
        CultureInfo.DefaultThreadCurrentUICulture = c;
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
    }
}

/// <summary>
/// Command-line switches. Godot's own options go before "--" and ours after it (the engine ignores those).
/// Dev switches only count together with --dev; normal players never get them by accident.
/// </summary>
public static class CmdLine
{
    static string[]? all;
    public static string[] All => all ??= SafeArgs();

    static string[] SafeArgs()
    {
        try { return OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray(); }
        catch { return System.Environment.GetCommandLineArgs(); }
    }

    public static bool Dev => Has("--dev");
    /// <summary>Dev-only "--showcase": hide dev markers (DEV tag, fake-data label) for README/store screenshots.</summary>
    public static bool Showcase => Dev && Has("--showcase");
    public static bool Has(string flag) => All.Contains(flag);
    public static string? After(string flag)
    {
        int i = Array.IndexOf(All, flag);
        return i >= 0 && i + 1 < All.Length ? All[i + 1] : null;
    }
    /// <summary>Value of a dev-only switch (null without --dev).</summary>
    public static string? DevAfter(string flag) => Dev ? After(flag) : null;
}

/// <summary>Error/warning output that reaches stderr and Godot's log file (user://logs/godot.log) from any thread.</summary>
public static class Log
{
    public static void Error(string msg)
    {
        try { GD.PrintErr("[ValTrainer] " + msg); }
        catch { try { Console.Error.WriteLine("[ValTrainer] " + msg); } catch { } }
    }

    public static void Info(string msg)
    {
        try { GD.Print("[ValTrainer] " + msg); } catch { }
    }

    /// <summary>A short, never-empty reason for the UI (some exceptions have an empty Message). A missing or broken .NET
    /// file (e.g. an install that was interrupted half-way) says so, because only reinstalling fixes it.</summary>
    public static string Describe(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
            if (x is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMethodException)
                return $"some of ValTrainer's files are missing or damaged ({x.GetType().Name}): reinstall it from {AppInfo.Website}";
        var m = e.Message?.Trim();
        return string.IsNullOrEmpty(m) ? e.GetType().Name : m;
    }

    /// <summary>Logs everything about an exception (type, message, inner exceptions, stack) from any thread.</summary>
    public static void Exception(string what, Exception e) => Error($"{what}: {e}");
}
