using Godot;
using ValTrainer.Audio;
using ValTrainer.Maps;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Enemy footsteps for the defense drills (Sound Lock, Site Anchor), modelled on how VALORANT treats them:
/// <list type="bullet">
/// <item>only RUNNING is audible: walking (shift) and crouch-walking are silent (<see cref="AudibleSpeed"/>, the same
/// 3.2 m/s threshold the bot character uses);</item>
/// <item>a deliberately FLAT distance curve: Riot tunes footsteps so they are always heard inside their range rather than
/// to portray distance, so the volume only drops gently and then fades out near the edge (running steps are commonly
/// measured at roughly 40–50 m; <see cref="Range"/> = 40 m);</item>
/// <item>mild occlusion: a step behind solid geometry (no straight line from your head to the foot or knee) is a little
/// quieter and muffled through a low-pass bus, so direction still reads but "through a wall" sounds different;</item>
/// <item>per-surface sounds from the CC0 Kenney Impact Sounds set (concrete, wood, grass, snow, carpet) picked from the
/// map box under the foot.</item>
/// </list>
/// Positional playback uses pooled <see cref="AudioStreamPlayer3D"/>s (engine panning from the camera, attenuation done
/// here so the curve is exact).
/// </summary>
public static class FootstepFx
{
    /// <summary>Faster than this (m/s) = running = audible.</summary>
    public const float AudibleSpeed = 3.2f;
    /// <summary>Audible range of running steps (m).</summary>
    public const float Range = 40f;
    /// <summary>Gain at the edge of the flat part of the curve.</summary>
    const float Floor = 0.35f;
    const string MuffledBus = "VT_FootstepsMuffled";

    static readonly Dictionary<string, AudioStream[]> sets = new();
    static Node3D? root;
    static readonly List<AudioStreamPlayer3D> pool = new();
    static int next;

    /// <summary>Steps played / occluded steps since the last <see cref="ResetStats"/> (dev logs).</summary>
    public static int Played, Muffled;
    public static void ResetStats() { Played = 0; Muffled = 0; }

    static AudioStream[] Set(string surface)
    {
        if (sets.TryGetValue(surface, out var s)) return s;
        var list = new List<AudioStream>();
        for (int i = 0; i < 5; i++)
        {
            var path = $"res://assets/audio/footsteps/footstep_{surface}_{i:000}.ogg";
            if (ResourceLoader.Exists(path) && GD.Load<AudioStream>(path) is { } a) list.Add(a);
        }
        if (list.Count == 0 && surface != "concrete") return sets[surface] = Set("concrete");
        return sets[surface] = list.ToArray();
    }

    static bool Ready(IGame g)
    {
        if (root != null && GodotObject.IsInstanceValid(root) && root.IsInsideTree()) return true;
        if (g.World == null || !GodotObject.IsInstanceValid(g.World)) return false;
        EnsureBus();
        root = new Node3D { Name = "FootstepFx" };
        g.World.AddChild(root);
        pool.Clear();
        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, PanningStrength = 0.9f };
            root.AddChild(p);
            pool.Add(p);
        }
        return true;
    }

    /// <summary>A runtime bus with a low-pass filter for steps heard through walls (created once, not saved).</summary>
    static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(MuffledBus) >= 0) return;
        AudioServer.AddBus();
        int idx = AudioServer.BusCount - 1;
        AudioServer.SetBusName(idx, MuffledBus);
        AudioServer.SetBusSend(idx, "Master");
        AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter { CutoffHz = 1700f, Resonance = 0.5f });
    }

    /// <summary>Gain of a running step at distance <paramref name="d"/> (flat VALORANT-like curve, 0 beyond <see cref="Range"/>).</summary>
    public static float Gain(float d)
    {
        if (d > Range) return 0f;
        float k = Mathf.Clamp((d - 3f) / (Range - 3f), 0f, 1f);
        return (Floor + (1 - Floor) * (1 - k) * (1 - k)) * (k < 0.88f ? 1f : (1 - k) / 0.12f);
    }

    /// <summary>Is the step occluded from the listener (map geometry only: smokes don't muffle sound)?</summary>
    public static bool Occluded(IGame g, Vector3 feet) =>
        Collision.Blocked(g.View.Eye, feet + new Vector3(0, 0.5f, 0), g.Solid) && Collision.Blocked(g.View.Eye, feet + new Vector3(0, 1.5f, 0), g.Solid);

    /// <summary>Plays one footstep at <paramref name="feet"/>. Returns false when it's out of range.</summary>
    public static bool Step(IGame g, Vector3 feet, string surface, float vol = 1f)
    {
        if (!Ready(g)) return false;
        float d = feet.DistanceTo(g.View.Eye);
        float gain = Gain(d);
        if (gain <= 0f) return false;
        var steps = Set(surface);
        if (steps.Length == 0) return false;
        bool occ = Occluded(g, feet);
        if (occ) { gain *= 0.7f; Muffled++; }
        Played++;
        var p = pool[next = (next + 1) % pool.Count];
        p.Stream = steps[Random.Shared.Next(steps.Length)];
        p.Bus = occ ? MuffledBus : "Master";
        p.GlobalPosition = feet + new Vector3(0, 0.1f, 0);
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * 1.15f * vol * gain));
        p.PitchScale = 0.92f + 0.16f * Random.Shared.NextSingle();
        p.Play();
        return true;
    }

    /// <summary>Surface sound under a foot: the topmost map box under it (crates and wooden floors = wood, Icebox ground =
    /// snow, Breeze ground = grass-like sand, the rest concrete).</summary>
    public static string SurfaceAt(MapSpot? map, Vector3 feet)
    {
        if (map == null) return "concrete";
        BoxDef? best = null;
        float bestY = float.MinValue;
        foreach (var b in map.Boxes)
        {
            if (feet.X < b.Min.X || feet.X > b.Max.X || feet.Z < b.Min.Z || feet.Z > b.Max.Z) continue;
            if (b.Max.Y > feet.Y + 0.1f || b.Max.Y < feet.Y - 0.35f || b.Max.Y <= bestY) continue;
            best = b; bestY = b.Max.Y;
        }
        if (best == null) return "concrete";
        return best.Surf switch
        {
            Surf.Cover => "wood",
            Surf.Elevated when best.Max.Y - best.Min.Y < 0.5f => "wood", // thin raised floors (balconies, heaven walkways)
            Surf.Floor when map.Key == "icebox" => "snow",
            Surf.Floor when map.Key == "breeze" => "grass",
            _ => "concrete",
        };
    }
}

/// <summary>Footstep timing for one walker: call <see cref="Update"/> every frame with its speed.</summary>
public sealed class FootstepEmitter
{
    float t = 0.05f;
    /// <summary>Steps this emitter played (0 when the walker only walked / crouched).</summary>
    public int Steps;

    public void Update(IGame g, MapSpot? map, Vector3 feet, float speed, bool crouched, float dt, float vol = 1f)
    {
        if (crouched || speed <= FootstepFx.AudibleSpeed) { t = 0.05f; return; }
        t -= dt;
        if (t > 0) return;
        // Cadence: ~0.33 s per step at full run speed (5.4 m/s), a little slower when slowed.
        t += Mathf.Clamp(0.33f * 5.4f / Mathf.Max(speed, 0.1f), 0.28f, 0.5f);
        if (FootstepFx.Step(g, feet, FootstepFx.SurfaceAt(map, feet), vol)) Steps++;
    }
}
