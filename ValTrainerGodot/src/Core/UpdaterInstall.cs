using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Microsoft.Win32;

namespace ValTrainer.Core;

/// <summary>How this copy of ValTrainer got onto the PC (decides how it updates itself).</summary>
public enum InstallKind
{
    /// <summary>Can't tell / not the registered installation: banner only.</summary>
    Unknown,
    /// <summary>Running from the Godot editor or a debug build.</summary>
    Source,
    /// <summary>Installed by Setup for this user (%LOCALAPPDATA%\Programs): silent update, no prompt.</summary>
    InstalledUser,
    /// <summary>Installed by Setup for all users (Program Files): the installer needs a UAC prompt.</summary>
    InstalledAdmin,
    /// <summary>The portable single-file ValTrainer.exe: swaps itself.</summary>
    Portable,
}

public static partial class Updater
{
    /// <summary>Written by the installer next to ValTrainer.exe: [ValTrainer] AppId=&lt;guid&gt;, Mode=user|admin.</summary>
    const string MarkerName = "install.ini";

    /// <summary>Full path of the running ValTrainer.exe.</summary>
    static string ExePath = "";

    /// <summary>Sets <see cref="Kind"/>; returns why this copy can't update itself (null when it can).
    /// Installed: install.ini (written by Setup) names the AppId, and Windows' uninstall entry for that AppId must point
    /// at this very folder. Portable: an exported exe with the game packed inside (no ValTrainer.pck / data folder next
    /// to it) in a folder we can write to.</summary>
    static string? DetectKind()
    {
        try
        {
            if (!OS.HasFeature("template") || OS.HasFeature("editor")) { Kind = InstallKind.Source; return "running from the Godot editor"; }
            ExePath = Path.GetFullPath(System.Environment.ProcessPath ?? OS.GetExecutablePath());
            string exeDir = Path.GetDirectoryName(ExePath)!;
            string marker = Path.Combine(exeDir, MarkerName);
            if (File.Exists(marker))
            {
                var (appId, mode) = ReadMarker(marker);
                if (appId == null) { Kind = InstallKind.Unknown; return $"{MarkerName} is damaged; use the installer from the release page"; }
                bool admin = mode == "admin";
                string? loc = InstallLocation(appId, admin);
                if (loc == null || !SamePath(loc, exeDir))
                {
                    Kind = InstallKind.Unknown;
                    return "this copy isn't the installation Windows knows about; use the installer from the release page";
                }
                Kind = admin ? InstallKind.InstalledAdmin : InstallKind.InstalledUser;
                return null;
            }
            if (File.Exists(Path.ChangeExtension(ExePath, ".pck")) || Directory.Exists(Path.Combine(exeDir, "data_ValTrainer_windows_x86_64")))
            {
                Kind = InstallKind.Unknown; // e.g. installed by a Setup from before automatic updates
                return "installed by an older Setup; install the next update by hand once, later ones are automatic";
            }
            Kind = InstallKind.Portable;
            if (!CanWrite(exeDir)) return $"can't write to {exeDir}; download new versions by hand";
            return null;
        }
        catch (Exception e)
        {
            Kind = InstallKind.Unknown;
            return $"can't tell how ValTrainer was installed ({e.Message})";
        }
    }

    static (string? AppId, string? Mode) ReadMarker(string path)
    {
        string? id = null, mode = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim(), val = line[(eq + 1)..].Trim().Trim('{', '}');
            if (key.Equals("AppId", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(val, "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")) id = val;
            else if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase)) mode = val.ToLowerInvariant();
        }
        return (id, mode);
    }

    /// <summary>InstallLocation of Inno Setup's uninstall entry ("{AppId}_is1") in HKCU (per-user) or HKLM (all users).</summary>
    static string? InstallLocation(string appId, bool admin)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string key = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{{{appId}}}_is1";
            using var root = admin ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                   : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var k = root.OpenSubKey(key);
            return k?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static bool CanWrite(string dir)
    {
        string probe = Path.Combine(dir, $".valtrainer-write-test-{System.Environment.ProcessId}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Portable: the previous exe (renamed to "ValTrainer.exe.old" by the last update) and a half-staged
    /// "ValTrainer.exe.new". The old process may still be exiting, so a locked file is simply left for the next launch.</summary>
    static void CleanPortableLeftovers()
    {
        foreach (var f in new[] { ExePath + ".old", ExePath + ".new" })
        {
            try { if (File.Exists(f)) { File.Delete(f); Log.Info($"Updater: removed {Path.GetFileName(f)}"); } }
            catch (Exception e) { Log.Info($"Updater: {Path.GetFileName(f)} still in use ({e.Message}); next launch removes it"); }
        }
    }

    /// <summary>Starts the release's Setup.exe silently (progress window only): it closes ValTrainer if it's still
    /// running, upgrades in place (same mode as before: per-user or all users, which needs a UAC prompt), writes its log
    /// into the updates folder and, because of /RELAUNCH, starts ValTrainer again with this run's arguments.</summary>
    static string? LaunchInstaller(string setup)
    {
        try
        {
            var psi = new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = dir };
            foreach (var a in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/SP-",
                         Kind == InstallKind.InstalledAdmin ? "/ALLUSERS" : "/CURRENTUSER", "/RELAUNCH" })
                psi.ArgumentList.Add(a);
            string rel = string.Join('|', RelaunchArgs());
            if (rel.Length > 0) psi.ArgumentList.Add("/RELAUNCHARGS=" + rel);
            psi.ArgumentList.Add("/LOG=" + Path.Combine(dir, InstallLogName(st.Version)));
            using var p = Process.Start(psi);
            if (p == null) return "Windows didn't start the installer";
            Log.Info($"Updater: started {Path.GetFileName(setup)} (pid {p.Id})");
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>Portable: "ValTrainer.exe" → "ValTrainer.exe.old" (Windows allows renaming a running exe), the verified
    /// "ValTrainer.exe.new" → "ValTrainer.exe", then a helper starts it once this process has exited (the new exe
    /// unpacks its .NET files over the ones this process still has open). Undone if the second rename fails.</summary>
    static string? SwapPortable(string staged)
    {
        string exe = ExePath, old = exe + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
        }
        catch (Exception e)
        {
            TryDelete(staged);
            return $"can't rename ValTrainer.exe ({e.Message})";
        }
        try { File.Move(staged, exe); }
        catch (Exception e)
        {
            try { File.Move(old, exe); } catch (Exception e2) { Log.Error($"Updater: couldn't put the old ValTrainer.exe back: {e2.Message}"); }
            TryDelete(staged);
            return $"can't put the new ValTrainer.exe in place ({e.Message})";
        }
        Log.Info($"Updater: ValTrainer.exe replaced by {st.Version} (old one kept as {Path.GetFileName(old)} until the next launch)");
        RelaunchAfterExit(exe);
        return null;
    }

    /// <summary>Starts <paramref name="exe"/> with this run's arguments as soon as this process has exited (a hidden
    /// Windows PowerShell waits for our PID; if that can't start, the exe is started right away).</summary>
    static void RelaunchAfterExit(string exe)
    {
        string args = string.Join(' ', RelaunchArgs().Select(QuoteArg));
        string wd = Path.GetDirectoryName(exe)!;
        try
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            string cmd = $"try {{ Wait-Process -Id {System.Environment.ProcessId} -Timeout 60 -ErrorAction Stop }} catch {{ }}; " +
                         $"Start-Process -FilePath {Q(exe)} -WorkingDirectory {Q(wd)}" + (args.Length > 0 ? $" -ArgumentList {Q(args)}" : "");
            string ps = Path.Combine(System.Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(ps) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = wd };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", cmd })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p != null) return;
        }
        catch (Exception e) { Log.Error($"Updater: relaunch helper failed ({e.Message}); starting the new version directly"); }
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = wd };
            foreach (var a in RelaunchArgs()) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception e) { Log.Error($"Updater: couldn't restart ValTrainer ({e.Message}); start it again by hand"); }
    }

    /// <summary>This run's command line (engine and ValTrainer arguments, e.g. the "safe graphics" shortcut's
    /// --rendering-method gl_compatibility) for the restarted app. Arguments with '|' or '"' can't be passed through
    /// the installer and are dropped.</summary>
    static IEnumerable<string> RelaunchArgs()
    {
        string[] all;
        try { all = System.Environment.GetCommandLineArgs(); } catch { yield break; }
        foreach (var a in all.Skip(1))
        {
            if (a.Length == 0 || a.Contains('|') || a.Contains('"')) continue;
            yield return a.Contains(' ') ? a.TrimEnd('\\') : a;
        }
    }

    /// <summary>Windows (MSVC runtime) command-line quoting of one argument.</summary>
    static string QuoteArg(string a)
    {
        if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in a)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }
}
