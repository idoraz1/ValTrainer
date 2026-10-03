using System.Text;
using System.Text.Json;
using Godot;
using ValTrainer.Core;

namespace ValTrainer.Game;

/// <summary>Per-frame aim sample. Angles in degrees; Focus* = where the current target is relative to the crosshair.</summary>
public struct FrameSample
{
    public float T;                 // seconds since the run started
    public float Yaw, Pitch;        // crosshair direction (view + recoil)
    public float Dx, Dy;            // raw mouse counts this frame
    public int FocusId;             // 0 = no target
    public float FocusYaw, FocusPitch; // target centre minus crosshair (+yaw = target is to the right, +pitch = above)
    public float FocusRadius;       // target angular radius (deg)
    public float Speed;             // player ground speed (m/s)
    public byte Flags;              // FrameFlags

    public float FocusErr => MathF.Sqrt(FocusYaw * FocusYaw + FocusPitch * FocusPitch);
    public bool Has(FrameFlags f) => (Flags & (byte)f) != 0;
}

[Flags]
public enum FrameFlags : byte { None = 0, Firing = 1, Ads = 2, Accurate = 4, Crouch = 8, Blind = 16, Alive = 32, Airborne = 64 }

/// <summary>One bullet. Err* = bullet direction minus target centre (deg), Zone -1 = miss.</summary>
public struct ShotSample
{
    public float T, Yaw, Pitch;
    public sbyte Zone;              // -1 miss, 0 head, 1 body, 2 legs
    public int FocusId;
    public float ErrYaw, ErrPitch, FocusRadius;
    public float Speed;
    public bool Accurate;
    public bool Hit => Zone >= 0;
    public float Err => MathF.Sqrt(ErrYaw * ErrYaw + ErrPitch * ErrPitch);
}

/// <summary>
/// Gameplay event. Kinds (A/B meaning):
/// target_spawn (id, angular distance from crosshair) · target_hit (id, ms since spawn) · target_expired (id) ·
/// kill (ms time-to-kill, 1 = headshot) · bot_seen (placement error deg, pitch error deg: + = crosshair too high) ·
/// stop (ms, 1 = counter-strafe) · died · flash (grade 0 dodged/1 partial/2 flashed, turn-away ms or -1) ·
/// spray_end (bullets, % on target) · spray_residual (vertical deg, horizontal deg; + = above / right of target) ·
/// reaction (ms) · early_click · round_won · round_lost ·
/// jump (horizontal speed m/s at take-off) · land (air time ms, fall height m from the highest point) · fall_damage (hp) ·
/// spot (Jump Peek: distance m, 1 = mid-jump)
/// </summary>
public sealed record TelemetryEvent(float T, string Kind, float A = 0, float B = 0);

/// <summary>Everything recorded during one run. Saved compactly so the coach can analyse recent sessions.</summary>
public sealed class RunTelemetry
{
    public string Mode = "";
    public int Tier;
    public float Sens;
    public int Dpi;
    public string Weapon = "";
    public string Map = "";
    public DateTime When = DateTime.Now;
    public float Duration;
    public float HFov = PlayerView.HipHFov;
    /// <summary>Valorant's ADS/scoped sensitivity multiplier for this run's weapon (zoomed look = sens × mult / zoom).</summary>
    public float ZoomSensMult = 1f;
    public readonly List<FrameSample> Frames = new(16384);
    public readonly List<ShotSample> Shots = new(512);
    public readonly List<TelemetryEvent> Events = new(256);

    public static string Dir
    {
        get => Paths.DataSubDir("telemetry"); // never throws; Save/RecentFiles handle an unwritable folder
    }

    const int Magic = 0x31545456; // "VTT1"

    /// <summary>Where <see cref="Save"/> writes this run in <paramref name="dir"/>.</summary>
    /// <summary>Mode keys can carry an argument ("flashpeek:skye"); ':' and other invalid file-name characters become '-'.</summary>
    static string SafeName(string mode) => string.Concat(mode.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

    public string FileIn(string dir) => Path.Combine(dir, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{When:yyyyMMdd_HHmmss}_{SafeName(Mode)}.vtt"));

    public string Save(string? dir = null, int keep = 80)
    {
        dir ??= Dir;
        Directory.CreateDirectory(dir);
        var file = FileIn(dir);
        using (var bw = new BinaryWriter(File.Create(file)))
        {
            bw.Write(Magic);
            var meta = JsonSerializer.Serialize(new { Mode, Tier, Sens, Dpi, Weapon, Map, When, Duration, HFov, ZoomSensMult });
            bw.Write(meta);
            bw.Write(Frames.Count);
            foreach (var f in Frames)
            {
                bw.Write(f.T); bw.Write(f.Yaw); bw.Write(f.Pitch); bw.Write(f.Dx); bw.Write(f.Dy);
                bw.Write(f.FocusId); bw.Write(f.FocusYaw); bw.Write(f.FocusPitch); bw.Write(f.FocusRadius); bw.Write(f.Speed); bw.Write(f.Flags);
            }
            bw.Write(Shots.Count);
            foreach (var s in Shots)
            {
                bw.Write(s.T); bw.Write(s.Yaw); bw.Write(s.Pitch); bw.Write(s.Zone); bw.Write(s.FocusId);
                bw.Write(s.ErrYaw); bw.Write(s.ErrPitch); bw.Write(s.FocusRadius); bw.Write(s.Speed); bw.Write(s.Accurate);
            }
            bw.Write(Events.Count);
            foreach (var e in Events) { bw.Write(e.T); bw.Write(e.Kind); bw.Write(e.A); bw.Write(e.B); }
        }
        // Keep only the most recent files.
        foreach (var old in Directory.GetFiles(dir, "*.vtt").OrderByDescending(f => f).Skip(keep))
            try { File.Delete(old); } catch { }
        return file;
    }

    public static RunTelemetry? Load(string file)
    {
        try
        {
            using var br = new BinaryReader(File.OpenRead(file));
            if (br.ReadInt32() != Magic) return null;
            using var doc = JsonDocument.Parse(br.ReadString());
            var m = doc.RootElement;
            var t = new RunTelemetry
            {
                Mode = m.GetProperty("Mode").GetString() ?? "",
                Tier = m.GetProperty("Tier").GetInt32(),
                Sens = m.GetProperty("Sens").GetSingle(),
                Dpi = m.GetProperty("Dpi").GetInt32(),
                Weapon = m.GetProperty("Weapon").GetString() ?? "",
                Map = m.GetProperty("Map").GetString() ?? "",
                When = m.GetProperty("When").GetDateTime(),
                Duration = m.GetProperty("Duration").GetSingle(),
                HFov = m.TryGetProperty("HFov", out var hf) ? hf.GetSingle() : PlayerView.HipHFov,
                ZoomSensMult = m.TryGetProperty("ZoomSensMult", out var zm) ? zm.GetSingle() : 1f,
            };
            int n = br.ReadInt32();
            for (int i = 0; i < n; i++)
                t.Frames.Add(new FrameSample
                {
                    T = br.ReadSingle(), Yaw = br.ReadSingle(), Pitch = br.ReadSingle(), Dx = br.ReadSingle(), Dy = br.ReadSingle(),
                    FocusId = br.ReadInt32(), FocusYaw = br.ReadSingle(), FocusPitch = br.ReadSingle(), FocusRadius = br.ReadSingle(),
                    Speed = br.ReadSingle(), Flags = br.ReadByte(),
                });
            n = br.ReadInt32();
            for (int i = 0; i < n; i++)
                t.Shots.Add(new ShotSample
                {
                    T = br.ReadSingle(), Yaw = br.ReadSingle(), Pitch = br.ReadSingle(), Zone = br.ReadSByte(), FocusId = br.ReadInt32(),
                    ErrYaw = br.ReadSingle(), ErrPitch = br.ReadSingle(), FocusRadius = br.ReadSingle(), Speed = br.ReadSingle(), Accurate = br.ReadBoolean(),
                });
            n = br.ReadInt32();
            for (int i = 0; i < n; i++) t.Events.Add(new TelemetryEvent(br.ReadSingle(), br.ReadString(), br.ReadSingle(), br.ReadSingle()));
            return t;
        }
        catch { return null; }
    }

    /// <summary>Most recent saved runs (newest first).</summary>
    public static IEnumerable<string> RecentFiles(int max = 80)
    {
        try { return Directory.GetFiles(Dir, "*.vtt").OrderByDescending(f => f).Take(max).ToList(); }
        catch { return Array.Empty<string>(); } // folder missing or unreadable: no history
    }
}

/// <summary>A mode's current aim focus: what the player should be aiming at right now.</summary>
public readonly record struct AimFocus(int Id, Vector3 Point, float Radius);
