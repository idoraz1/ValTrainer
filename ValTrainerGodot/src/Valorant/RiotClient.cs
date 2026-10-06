using System.Diagnostics;
using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Valorant;

/// <summary>
/// Starts VALORANT through the Riot Client (the Lock-In hand-off's LAUNCH VALORANT button). The client's path comes
/// from Riot's own install list (<c>C:\ProgramData\Riot Games\RiotClientInstalls.json</c>, read only). Nothing is ever
/// started without a click, no VALORANT file is touched and no process is ever stopped. Dev runs only log the command.
/// </summary>
public static class RiotClient
{
    const string Args = "--launch-product=valorant --launch-patchline=live";
    static string? exe;
    static bool looked;

    /// <summary>RiotClientServices.exe, or null when the Riot Client isn't installed (the button stays hidden).</summary>
    public static string? Exe
    {
        get
        {
            if (!looked) { looked = true; exe = Find(); }
            return exe;
        }
    }

    public static bool Found => Exe != null;

    static string? Find()
    {
        try
        {
            string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string list = Path.Combine(string.IsNullOrEmpty(data) ? @"C:\ProgramData" : data, "Riot Games", "RiotClientInstalls.json");
            if (!File.Exists(list)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(list));
            foreach (var key in new[] { "rc_default", "rc_live" })
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } p)
                {
                    string full = Path.GetFullPath(p);
                    if (File.Exists(full) && Path.GetFileName(full).Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)) return full;
                }
        }
        catch (Exception e) { Log.Info($"[riot] Riot Client not found ({e.Message})"); }
        return null;
    }

    /// <summary>Starts VALORANT (only call this from a button click). Dev runs log what would start instead.</summary>
    public static bool Launch()
    {
        if (Exe is not { } path) return false;
        if (Main.I.Dev)
        {
            Log.Info($"[riot] dev: would start \"{path}\" {Args}");
            return true;
        }
        try
        {
            var psi = new ProcessStartInfo(path, Args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) ?? "" };
            Process.Start(psi)?.Dispose();
            Log.Info("[riot] started VALORANT through the Riot Client");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Couldn't start the Riot Client: {e.Message}");
            return false;
        }
    }
}
