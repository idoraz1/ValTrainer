using System.Globalization;
using System.Text.Json;
using Key = Godot.Key;
using MouseButton = Godot.MouseButton;
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

    // Movement keys (Valorant defaults; overridden by BackupKeybinds.json when rebound)
    public Key KeyForward = Key.W, KeyBack = Key.S;
    public Key KeyLeft = Key.A, KeyRight = Key.D;
    public Key KeyWalk = Key.Shift;
    public Key KeyCrouch = Key.Ctrl;

    // Jump: two bind slots like VALORANT (primary / secondary). A slot holds a key or a mouse bind (wheel notch, thumb
    // button); Key.None / MouseButton.None = not that kind. Default: Space, no secondary.
    public Key KeyJump = Key.Space, KeyJump2 = Key.None;
    public MouseButton MouseJump = MouseButton.None, MouseJump2 = MouseButton.None;

    /// <summary>True if this mouse button / wheel notch is bound to Jump.</summary>
    public bool JumpsOn(MouseButton b) => b != MouseButton.None && (b == MouseJump || b == MouseJump2);

    internal void SetJumpSlot(int slot, Key key, MouseButton mouse)
    {
        if (slot == 0) { KeyJump = key; MouseJump = mouse; }
        else { KeyJump2 = key; MouseJump2 = mouse; }
    }

    /// <summary>The jump binds for display, e.g. "Space + Wheel down".</summary>
    public string JumpBindText
    {
        get
        {
            var parts = new List<string>(2);
            void Add(Key k, MouseButton b)
            {
                if (b != MouseButton.None) parts.Add(b switch
                {
                    MouseButton.WheelUp => "Wheel up", MouseButton.WheelDown => "Wheel down", MouseButton.Middle => "Mouse 3",
                    MouseButton.Xbutton1 => "Mouse 4", MouseButton.Xbutton2 => "Mouse 5", _ => b.ToString(),
                });
                else if (k != Key.None) parts.Add(k.ToString());
            }
            Add(KeyJump, MouseJump);
            Add(KeyJump2, MouseJump2);
            return parts.Count == 0 ? "unbound" : string.Join(" + ", parts);
        }
    }

    // Agent abilities (agent drills), by slot: 0 = C (grenade slot), 1 = Q (ability 1), 2 = E (ability 2 / signature),
    // 3 = X (ultimate). A slot holds a key or a mouse button (Key.None / MouseButton.None = not that kind).
    public readonly Key[] AbilityKeys = { Key.C, Key.Q, Key.E, Key.X };
    public readonly MouseButton[] AbilityMouse = { MouseButton.None, MouseButton.None, MouseButton.None, MouseButton.None };

    /// <summary>An ability slot's bind for display: "Q", "Mouse 4" …</summary>
    public string AbilityBindText(int slot)
    {
        if (slot < 0 || slot > 3) return "?";
        var b = AbilityMouse[slot];
        if (b != MouseButton.None) return b switch
        {
            MouseButton.Middle => "Mouse 3", MouseButton.Xbutton1 => "Mouse 4", MouseButton.Xbutton2 => "Mouse 5", _ => b.ToString(),
        };
        var k = AbilityKeys[slot];
        return k == Key.None ? "unbound" : Godot.OS.GetKeycodeString(k);
    }

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

public static class ValorantImporter
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

    /// <summary>The keybind file only lists keys the player rebound; anything missing keeps the default.</summary>
    static void ApplyKeybinds(ValorantProfile p, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("actionMappings", out var maps)) return;
            foreach (var m in maps.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var keyName = m.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                var who = m.TryGetProperty("characterName", out var c) ? c.GetString() : "None";
                if (who == "None" && name.Equals("Jump", StringComparison.OrdinalIgnoreCase)) { ApplyJumpBind(p, m, keyName); continue; }
                if (who == "None" && AbilitySlotOf(name) is int abilitySlot) { ApplyAbilityBind(p, m, abilitySlot, keyName); continue; }
                if (who != "None" || UeKey(keyName) is not { } key) continue;

                if (name.Contains("Forward", StringComparison.OrdinalIgnoreCase)) p.KeyForward = key;
                else if (name.Contains("Backward", StringComparison.OrdinalIgnoreCase)) p.KeyBack = key;
                else if (name.Contains("Left", StringComparison.OrdinalIgnoreCase) && name.Contains("Strafe", StringComparison.OrdinalIgnoreCase)) p.KeyLeft = key;
                else if (name.Contains("Right", StringComparison.OrdinalIgnoreCase) && name.Contains("Strafe", StringComparison.OrdinalIgnoreCase)) p.KeyRight = key;
                else if (name.Contains("Walk", StringComparison.OrdinalIgnoreCase)) p.KeyWalk = key;
                else if (name.Equals("Crouch", StringComparison.OrdinalIgnoreCase)) p.KeyCrouch = key;
            }
        }
        catch { /* keep defaults */ }
    }

    /// <summary>
    /// Jump has two bind slots ("bindIndex" 0 = primary, 1 = secondary). Each is a key, a mouse wheel notch / button, or
    /// "None" (unbound). The file only lists rebound slots: the others keep their defaults (Space / nothing).
    /// </summary>
    static void ApplyJumpBind(ValorantProfile p, JsonElement m, string keyName)
    {
        int slot = m.TryGetProperty("bindIndex", out var bi) && bi.ValueKind == JsonValueKind.Number && bi.TryGetInt32(out int b) ? b : 0;
        if (slot is < 0 or > 1) return;
        var mouse = UeMouse(keyName);
        var key = mouse == null ? UeKey(keyName) : null;
        bool unbound = keyName.Length == 0 || keyName.Equals("None", StringComparison.OrdinalIgnoreCase);
        if (mouse == null && key == null && !unbound) return; // a key we can't map: keep the default
        p.SetJumpSlot(slot, key ?? Key.None, mouse ?? MouseButton.None);
    }

    /// <summary>
    /// VALORANT's ability actions → slot (0 C grenade, 1 Q ability 1, 2 E ability 2, 3 X ultimate). The action names in
    /// BackupKeybinds.json aren't documented, so this matches tolerantly: "…Grenade…", "…Ultimate…", "…Ability…1/One",
    /// "…Ability…2/Two". Anything else (pings, chat …) is ignored.
    /// </summary>
    static int? AbilitySlotOf(string name)
    {
        const StringComparison I = StringComparison.OrdinalIgnoreCase;
        if (name.Contains("Ping", I) || name.Contains("Chat", I) || name.Contains("Radio", I) || name.Contains("Comm", I)) return null;
        if (name.Contains("Ultimate", I)) return 3;
        if (name.Contains("Grenade", I)) return 0;
        if (!name.Contains("Ability", I)) return null;
        if (name.EndsWith('1') || name.Contains("One", I)) return 1;
        if (name.EndsWith('2') || name.Contains("Two", I)) return 2;
        return null;
    }

    /// <summary>Primary bind (bindIndex 0) of an ability slot: a key or a mouse button; unmappable keys keep the default.</summary>
    static void ApplyAbilityBind(ValorantProfile p, JsonElement m, int slot, string keyName)
    {
        int bind = m.TryGetProperty("bindIndex", out var bi) && bi.ValueKind == JsonValueKind.Number && bi.TryGetInt32(out int b) ? b : 0;
        if (bind != 0 || slot is < 0 or > 3) return;
        var mouse = UeMouse(keyName);
        if (mouse is MouseButton.WheelUp or MouseButton.WheelDown) return; // wheel notches can't be held: keep the default
        var key = mouse == null ? UeKey(keyName) ?? UeExtraKey(keyName) : null;
        if (mouse == null && key == null) return;
        p.AbilityKeys[slot] = key ?? Key.None;
        p.AbilityMouse[slot] = mouse ?? MouseButton.None;
    }

    /// <summary>Unreal names of keys people bind abilities to that <see cref="UeKey"/> doesn't cover (digits, Tab …).</summary>
    static Key? UeExtraKey(string ue) => ue switch
    {
        "One" => Key.Key1, "Two" => Key.Key2, "Three" => Key.Key3, "Four" => Key.Key4, "Five" => Key.Key5,
        "Six" => Key.Key6, "Seven" => Key.Key7, "Eight" => Key.Key8, "Nine" => Key.Key9, "Zero" => Key.Key0,
        "Tab" => Key.Tab, "Tilde" => Key.Quoteleft, "RightAlt" => Key.Alt,
        _ => null,
    };

    /// <summary>Unreal mouse key names → Godot mouse buttons (wheel notches and the extra buttons people bind jump to).</summary>
    static MouseButton? UeMouse(string ue) => ue switch
    {
        "MouseScrollUp" => MouseButton.WheelUp,
        "MouseScrollDown" => MouseButton.WheelDown,
        "MiddleMouseButton" => MouseButton.Middle,
        "ThumbMouseButton" => MouseButton.Xbutton1,
        "ThumbMouseButton2" => MouseButton.Xbutton2,
        _ => null,
    };

    /// <summary>Unreal key names → Godot keys (only what's plausible for movement).</summary>
    static Key? UeKey(string ue)
    {
        if (ue.Length == 1 && char.IsLetter(ue[0])) return (Key)((int)Key.A + (char.ToUpperInvariant(ue[0]) - 'A'));
        return ue switch
        {
            "LeftShift" => Key.Shift,
            "RightShift" => Key.Shift,
            "LeftControl" => Key.Ctrl,
            "RightControl" => Key.Ctrl,
            "LeftAlt" => Key.Alt,
            "SpaceBar" => Key.Space,
            "CapsLock" => Key.Capslock,
            "Up" => Key.Up,
            "Down" => Key.Down,
            "Left" => Key.Left,
            "Right" => Key.Right,
            _ => null,
        };
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
