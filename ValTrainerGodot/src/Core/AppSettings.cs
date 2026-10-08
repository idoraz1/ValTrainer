using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValTrainer.Core;

/// <summary>Trainer-side preferences. Anything set to -1 / false falls back to the imported Valorant value.</summary>
public sealed class AppSettings
{
    public string? AccountId;          // null = Valorant's last used account
    public bool UseSensOverride;
    public float SensOverride = 0.4f;
    public int Dpi = 800;              // only used to show eDPI and cm/360
    public int EnemyColorOverride = -1;
    public int WindowModeOverride = -1;
    public int MonitorOverride = -1;   // -1 = same monitor as Valorant
    public int VSyncOverride = -1;     // -1 Valorant, 0 off, 1 on
    public int FpsCapOverride = -1;    // -1 Valorant, 0 unlimited, else cap
    public int Difficulty = 1;         // legacy (raylib build): 0 easy, 1 normal, 2 hard
    public int Tier = 1;               // skill tier 0..4 (rank-calibrated), see Difficulty.cs
    public int Quality = 1;            // graphics preset: 0 Low (≈300 FPS), 1 Medium (≈150), 2 High (≈85), 3 Ultra (≈60) on a GTX 1050-class GPU
    public float ViewmodelFov = 70f;
    public string MapKey = "random";   // map for map-based drills
    public float Volume = 0.5f;
    public bool Outlines = true;      // enemy outline + fresnel (Valorant: "Hide outlines and fresnel" off)
    public string TargetStyle = "valorant"; // aim-drill targets: "valorant" = dark body + enemy-colour outline/fresnel, "classic" = solid enemy-colour sphere
    public bool ViewModel = true;      // first-person gun
    public bool LeftHandedWeapon;      // weapon hand: false = right (default), true = left (VALORANT's own setting is not used)
    public bool UseFinderCrosshair;    // crosshair: false = VALORANT's (imported), true = FinderCrosshairCode
    public string? FinderCrosshairCode; // VALORANT profile code from the Crosshair Finder ("Use in ValTrainer") or pasted (Settings → Crosshair); VALORANT itself is never changed
    public string? FinderCrosshairTab;  // "primary" / "ads" / "sniper": only that tab of FinderCrosshairCode is used, on top of the live VALORANT import (Crosshair Finder); null = the whole code (a pasted code, results from before 1.6)
    public string? LastFinderCrosshairCode; // the latest Crosshair Finder result, kept so it can be copied any time (Settings → Crosshair)

    // ---- updates / version (see UpdateCheck, WhatsNew, UpdatesPrompt) ----
    public bool? UpdatesConsent;       // the "Check GitHub for new versions?" answer: null = not asked yet, true = ENABLE UPDATES, false = NOT NOW (or turned off)
    public bool CheckUpdates = true;   // ask GitHub for a newer release (at startup and every 6 h, see UpdateCheck); only counts once UpdatesConsent is true
    public bool AutoDownloadUpdates = true; // download a newer release in the background and install it on restart (see Updater)
    public long LastUpdateCheck;       // unix seconds (UTC) of the last check that got an answer from GitHub
    public string? LatestVersion;      // newest release that check found ("1.2.0"), null = none / never checked
    public string? LatestUrl;          // its release page
    public UpdateAssets? LatestAssets; // its downloads (installer, portable zip, SHA256SUMS.txt)
    public string? SkippedVersion;     // "Skip this version" on the menu's update banner
    public string? LastSeenVersion;    // version whose "What's new" was shown (or that was installed fresh)

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Paths.DataDir, "settings.json");

    /// <summary>True when no settings file was loaded (first run, or the file was unreadable): first-run defaults apply.</summary>
    [JsonIgnore] public bool IsNew;
    /// <summary>Why the graphics quality was picked automatically on this first run (null = the user's saved choice).</summary>
    [JsonIgnore] public string? AutoQualityReason;

    /// <summary>Automatic update checks and background downloads may use the network: only after the player said yes
    /// (first-launch prompt, or "Check for updates" turned on in Settings → About). Until then nothing is sent anywhere
    /// unless the player clicks CHECK NOW.</summary>
    [JsonIgnore] public bool AutoUpdates => UpdatesConsent == true && CheckUpdates;

    /// <summary>settings.json exists but couldn't be opened (locked, no permission, OneDrive placeholder offline):
    /// run with defaults but never overwrite the user's file this session.</summary>
    [JsonIgnore] bool readFailed;

    public static AppSettings Load()
    {
        string path = FilePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings { IsNew = true };
        }
        catch (Exception e) when (Paths.IsCorruptData(e)) { Paths.KeepCorrupt(path, e); /* start fresh */ }
        catch (Exception e)
        {
            Log.Error($"{path} couldn't be opened ({e.Message}); using defaults and leaving the file alone");
            return new AppSettings { IsNew = true, readFailed = true };
        }
        return new AppSettings { IsNew = true };
    }

    /// <summary>Set for --dev runs: changes stay in memory and never reach the user's settings file.</summary>
    public static bool ReadOnly;

    /// <summary>Never throws: an unwritable data folder is logged and reported via <see cref="Paths.LastWriteError"/>.</summary>
    public void Save()
    {
        if (ReadOnly || readFailed) return;
        try { Paths.TryWriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Error($"Saving settings failed: {e.Message}"); }
    }
}
