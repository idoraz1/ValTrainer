using System.Globalization;
using System.Text.Json;
using ValTrainer.Core;

namespace ValTrainer.Valorant;

public sealed class ValorantAccount
{
    public required string Id { get; init; }      // config folder name: "<puuid>-<region>"
    public required string Folder { get; init; }
    public DateTime LastModified { get; init; }
    public bool IsLastUsed { get; init; }
    public float? Sens { get; init; }

    public string Region => Id.Contains('-') ? Id[(Id.LastIndexOf('-') + 1)..].ToUpperInvariant() : "?";
    public string ShortId => Id.Length > 8 ? Id[..8] : Id;
}

/// <summary>Everything we copy from Valorant. Values Valorant never wrote stay at Valorant's defaults.</summary>
public sealed class ValorantProfile
{
    /// <summary>True when an account's settings were imported.</summary>
    public bool Found;
    public string? AccountId;
    public string Source = "Not found — using Valorant defaults";
    /// <summary>Why nothing was imported (meaningful when <see cref="Found"/> is false).</summary>
    public ValorantStatus Status = ValorantStatus.NotInstalled;

    /// <summary>One line for the menu/settings when nothing was imported; null when the import worked.</summary>
    public string? NotFoundMessage => Found ? null : Status switch
    {
        ValorantStatus.NoAccounts => "VALORANT settings not found (launch VALORANT once) — using defaults; set your sens in Settings",
        ValorantStatus.Unreadable => "Couldn't read VALORANT's settings — using defaults; set your sens in Settings",
        _ => "VALORANT settings not found — using defaults; set your sens in Settings",
    };

    // Mouse
    public float Sensitivity = 1.0f;
    public bool SensFromFile;
    public float ZoomedSensMult = 1f;  // "Scoped Sensitivity Multiplier"
    public float AdsSensMult = 1f;     // "ADS Sensitivity Multiplier"
    public bool HoldToScope;
    public bool LeftHanded;            // first-person weapon on the left

    /// <summary>All keybinds (both slots per action): VALORANT's defaults with BackupKeybinds.json's overrides applied.</summary>
    public Keybinds Binds = Keybinds.Defaults();

    // Shortcuts to the bind of an action (primary slot, else the secondary): for display and dev scripts. Input checks go
    // through Binds.IsDown / Binds.Match, which honour both slots, mouse buttons and the wheel.
    public InputBinding KeyForward => Binds.Primary(GameAction.MoveForward);
    public InputBinding KeyBack => Binds.Primary(GameAction.MoveBack);
    public InputBinding KeyLeft => Binds.Primary(GameAction.StrafeLeft);
    public InputBinding KeyRight => Binds.Primary(GameAction.StrafeRight);
    public InputBinding KeyWalk => Binds.Primary(GameAction.Walk);
    public InputBinding KeyCrouch => Binds.Primary(GameAction.Crouch);
    public InputBinding KeyJump => Binds.Primary(GameAction.Jump);

    /// <summary>The jump binds for display, e.g. "Space" or "Space · Wheel down".</summary>
    public string JumpBindText => Binds.Text(GameAction.Jump);

    /// <summary>An ability slot's bind for display (0 C, 1 Q, 2 E, 3 X): "Q", "Mouse 4" …</summary>
    public string AbilityBindText(int slot) => slot is < 0 or > 3 ? "?" : Binds.Short(Keybinds.AbilityAction(slot));

    // Visual
    public CrosshairSettings Crosshair = new();
    public int EnemyHighlight;         // 0 red, 1 yellow (deut), 2 yellow (prot), 3 purple (trit)
    public int ColorBlindMode;
    public bool ShowFps;

    // Video / GPU
    public int ResX = 1920, ResY = 1080;
    public int WindowMode = 1;         // UE: 0 fullscreen, 1 windowed fullscreen, 2 windowed
    public int MonitorIndex;
    public bool VSync;
    public float FrameRateLimit;       // 0 = unlimited
    public bool Letterbox;
    public int MaterialQuality = -1, TextureQuality = -1, DetailQuality = -1, UIQuality = -1;
    public int AntiAliasing = -1, Anisotropic = -1, BloomQuality = -1;
    public bool? ImproveClarity, Vignette, Distortion, CastShadows;
    public int Reflex = -1;

    public bool WantsMsaa => AntiAliasing is 1 or 2;
}

/// <summary>What <see cref="ValorantImporter"/> found on this PC.</summary>
public enum ValorantStatus
{
    /// <summary>Settings of an account were imported.</summary>
    Ok,
    /// <summary>No VALORANT config folder at all (not installed, or never launched on this Windows account).</summary>
    NotInstalled,
    /// <summary>The config folder exists but holds no account settings yet.</summary>
    NoAccounts,
    /// <summary>The folder or files exist but couldn't be read (permissions, corrupt).</summary>
    Unreadable,
}

public static partial class ValorantImporter
{
    /// <summary>Result of the last <see cref="FindAccounts"/>: NotInstalled, NoAccounts, Unreadable or Ok.</summary>
    public static ValorantStatus LastScan { get; private set; } = ValorantStatus.NotInstalled;

    /// <summary>All Valorant accounts with settings on this PC, last used first. Never throws.</summary>
    public static List<ValorantAccount> FindAccounts()
    {
        var root = Paths.ValorantConfigDir;
        var list = new List<ValorantAccount>();
        LastScan = ValorantStatus.NotInstalled;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
        LastScan = ValorantStatus.NoAccounts;

        string[] dirs;
        try { dirs = Directory.GetDirectories(root); }
        catch { LastScan = ValorantStatus.Unreadable; return list; }

        string? last = ReadLastKnownUser(root);
        foreach (var dir in dirs)
        {
            try
            {
                var rus = Path.Combine(dir, "Windows", "RiotUserSettings.ini");
                if (!File.Exists(rus)) continue;
                var name = Path.GetFileName(dir);
                var files = new[] { rus, Path.Combine(dir, "WindowsClient", "GameUserSettings.ini") }.Where(File.Exists);
                var ini = ParseIni(rus);
                list.Add(new ValorantAccount
                {
                    Id = name,
                    Folder = dir,
                    LastModified = files.Max(f => File.GetLastWriteTime(f)),
                    IsLastUsed = !string.IsNullOrEmpty(last) && name.StartsWith(last, StringComparison.OrdinalIgnoreCase),
                    Sens = ini.TryGetValue("MouseSensitivity", out var s) && TryF(s, out var f) && ValidSens(f) ? f : null,
                });
            }
            catch { /* skip an unreadable account folder */ }
        }
        if (list.Count > 0) LastScan = ValorantStatus.Ok;
        return list.OrderByDescending(a => a.IsLastUsed).ThenByDescending(a => a.LastModified).ToList();
    }

    static bool ValidSens(float f) => f > 0.0001f && f < 1000f;

    /// <summary>Imports one account (null = machine-wide video settings only). Never throws: anything unreadable
    /// keeps Valorant's defaults.</summary>
    public static ValorantProfile Load(ValorantAccount? acc)
    {
        ValorantProfile p;
        try { p = LoadCore(acc); }
        catch (Exception e)
        {
            Log.Error($"Reading VALORANT settings failed: {e.Message}");
            p = new ValorantProfile { Status = ValorantStatus.Unreadable };
        }
        Sanitize(p);
        return p;
    }

    /// <summary>Clamps values a corrupt or hand-edited ini could make absurd (NaN, tiny windows, negative caps).</summary>
    static void Sanitize(ValorantProfile p)
    {
        if (!ValidSens(p.Sensitivity)) { p.Sensitivity = 1f; p.SensFromFile = false; }
        if (!(p.ZoomedSensMult > 0.01f && p.ZoomedSensMult < 100f)) p.ZoomedSensMult = 1f;
        if (!(p.AdsSensMult > 0.01f && p.AdsSensMult < 100f)) p.AdsSensMult = p.ZoomedSensMult;
        if (p.ResX < 640 || p.ResX > 16384 || p.ResY < 480 || p.ResY > 16384) { p.ResX = 1920; p.ResY = 1080; }
        if (p.WindowMode is < 0 or > 3) p.WindowMode = 1;
        if (p.MonitorIndex < 0 || p.MonitorIndex > 16) p.MonitorIndex = 0;
        if (!(p.FrameRateLimit >= 0f && p.FrameRateLimit <= 1000f)) p.FrameRateLimit = 0f;
        p.EnemyHighlight = Math.Clamp(p.EnemyHighlight, 0, 3);
    }

    static ValorantProfile LoadCore(ValorantAccount? acc)
    {
        var p = new ValorantProfile { Status = LastScan == ValorantStatus.Ok ? ValorantStatus.NoAccounts : LastScan };
        var root = Paths.ValorantConfigDir;
        if (string.IsNullOrEmpty(root)) return p;

        // Video settings: per-account file and machine-wide file; whichever Valorant wrote last wins.
        var gusCandidates = new List<string> { Path.Combine(root, "WindowsClient", "GameUserSettings.ini") };
        if (acc != null) gusCandidates.Add(Path.Combine(acc.Folder, "WindowsClient", "GameUserSettings.ini"));
        var gusPath = gusCandidates.Where(File.Exists).OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
        if (gusPath != null) ApplyGameUserSettings(p, ParseIni(gusPath));

        if (acc == null) return p;

        var rus = ParseIni(Path.Combine(acc.Folder, "Windows", "RiotUserSettings.ini"));
        p.Found = true;
        p.Status = ValorantStatus.Ok;
        p.AccountId = acc.Id;
        p.Source = $"Account {acc.ShortId}… ({acc.Region}), saved {acc.LastModified:yyyy-MM-dd HH:mm}";

        if (rus.TryGetValue("MouseSensitivity", out var sens) && TryF(sens, out var sv)) { p.Sensitivity = sv; p.SensFromFile = true; }
        p.ZoomedSensMult = GetF(rus, "MouseSensitivityZoomed", 1f);
        p.AdsSensMult = GetF(rus, "MouseSensitivityADS", p.ZoomedSensMult);
        p.HoldToScope = GetB(rus, "HoldInputForSniperScopes") ?? false;
        p.LeftHanded = GetB(rus, "LeftHanded") ?? false;

        p.Crosshair = rus.TryGetValue("SavedCrosshairProfileData", out var xh)
                      && CrosshairSettings.FromProfileJson(UnquoteUe(xh)) is { } parsed
            ? parsed
            : CrosshairSettings.FromLegacyKeys(rus);

        p.ColorBlindMode = GetI(rus, "ColorBlindMode", 0);
        var hlKey = rus.Keys.FirstOrDefault(k => k.Contains("EnemyHighlight", StringComparison.OrdinalIgnoreCase));
        p.EnemyHighlight = hlKey != null && TryF(rus[hlKey], out var hl) ? (int)Math.Clamp(hl, 0f, 3f) : 0;
        p.ShowFps = GetI(rus, "PlayerPerfShowFrameRate", 0) > 0;

        p.MaterialQuality = GetI(rus, "MaterialQuality", -1);
        p.TextureQuality = GetI(rus, "TextureQuality", -1);
        p.DetailQuality = GetI(rus, "DetailQuality", -1);
        p.UIQuality = GetI(rus, "UIQuality", -1);
        p.AntiAliasing = GetI(rus, "AntiAliasing", -1);
        p.Anisotropic = GetI(rus, "AnisotropicFiltering", -1);
        p.BloomQuality = GetI(rus, "BloomQuality", -1);
        p.ImproveClarity = GetB(rus, "ImproveClarity");
        p.Vignette = GetB(rus, "Vignette");
        p.Distortion = GetB(rus, "Distortion");
        p.CastShadows = GetB(rus, "CastShadows");
        p.Reflex = GetI(rus, "NvidiaReflexLowLatencySetting", -1);

        ApplyKeybinds(p, Path.Combine(acc.Folder, "WindowsClient", "BackupKeybinds.json"));
        return p;
    }

    static void ApplyGameUserSettings(ValorantProfile p, Dictionary<string, string> g)
    {
        p.ResX = GetI(g, "ResolutionSizeX", p.ResX);
        p.ResY = GetI(g, "ResolutionSizeY", p.ResY);
        p.WindowMode = GetI(g, "FullscreenMode", p.WindowMode);
        p.MonitorIndex = GetI(g, "DefaultMonitorIndex", 0);
        p.VSync = GetB(g, "bUseVSync") ?? false;
        p.FrameRateLimit = GetF(g, "FrameRateLimit", 0);
        p.Letterbox = GetB(g, "bShouldLetterbox") ?? false;
    }

    static string? ReadLastKnownUser(string root)
    {
        var f = Path.Combine(root, "WindowsClient", "RiotLocalMachine.ini");
        if (!File.Exists(f)) return null;
        return ParseIni(f).TryGetValue("LastKnownUser", out var v) ? v.Trim() : null;
    }

    /// <summary>Reads key=value lines. Valorant's "EAresFloatSettingName::X" prefixes are stripped to "X".</summary>
    public static Dictionary<string, string> ParseIni(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> lines;
        try
        {
            // Valorant may hold the file open; share read/write so we never block or disturb it.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            lines = sr.ReadToEnd().Split('\n');
        }
        catch { return d; }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] is '[' or ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            int sep = key.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0) key = key[(sep + 2)..];
            d[key] = line[(eq + 1)..];
        }
        return d;
    }

    static string UnquoteUe(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    /// <summary>Culture-independent ("0.35" on every PC) and finite only: "NaN"/"Infinity" count as missing.</summary>
    internal static bool TryF(string s, out float f) =>
        float.TryParse(s.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) && float.IsFinite(f);
    static float GetF(Dictionary<string, string> d, string k, float def) => d.TryGetValue(k, out var v) && TryF(v, out var f) ? f : def;
    static int GetI(Dictionary<string, string> d, string k, int def) =>
        d.TryGetValue(k, out var v) && TryF(v, out var f) && f is >= int.MinValue and <= int.MaxValue ? (int)f : def;
    static bool? GetB(Dictionary<string, string> d, string k) =>
        d.TryGetValue(k, out var v) ? v.Trim().Equals("True", StringComparison.OrdinalIgnoreCase) : null;
}
