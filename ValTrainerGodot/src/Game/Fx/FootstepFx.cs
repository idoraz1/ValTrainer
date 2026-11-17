using Godot;
using ValTrainer.Audio;
using ValTrainer.Game.Bots;
using ValTrainer.Maps;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Every running footstep in the game (1.21, docs/research/footsteps-1.21.md): the bots' own steps (Deathmatch and every bot
/// drill, <see cref="BotCharacter"/>), the defense drills' attackers (Sound Lock, Site Anchor, <see cref="FootstepEmitter"/>)
/// and your own. Modelled on how VALORANT treats them:
/// <list type="bullet">
/// <item>only RUNNING is audible: walking (shift) and crouch-walking are silent (<see cref="AudibleSpeed"/>);</item>
/// <item>a gentle distance curve (<see cref="FootstepSynth.Gain"/>, −2.5 dB per doubling, silent past
/// <see cref="Range"/> = 40 m): Riot keeps it flat so no step is missed; on top of that a far step loses its click
/// (a low-pass that closes with distance) and gets a little more room reflection, so near and far sound different;</item>
/// <item>direction from the foot itself: each step is rendered in stereo for where you look at that moment (level and
/// time difference between the ears, a duller far ear, a duller sound from behind: <see cref="FootstepSynth.CueFor"/>);</item>
/// <item>mild occlusion: a step behind solid map geometry (no straight line from your head to the foot or knee) is a little
/// quieter and heavily muffled, so direction still reads but "through a wall" sounds different;</item>
/// <item>synthesized sounds (no samples, nothing from Riot): concrete, tile, metal, wood and soft ground, picked from the map
/// box under the foot, alternating left / right foot, with per-step pitch, level and timing variation.</item>
/// </list>
/// Played on a pool of 2D players (the stereo is ours), muted in drill previews.
/// </summary>
public static class FootstepFx
{
    /// <summary>Faster than this (m/s) = running = audible.</summary>
    public const float AudibleSpeed = 3.2f;
    /// <summary>Audible range of running steps (m).</summary>
    public const float Range = FootstepSynth.Range;
    /// <summary>Enemy steps' loudness relative to the master volume (they're the cue you listen for).</summary>
    const float EnemyLevel = 1.25f;
    const int Variants = 8; // per surface: even = left foot, odd = right foot
    const int Voices = 16;

    /// <summary>Seconds per step at <paramref name="speed"/> m/s: ~0.33 s at rifle run speed (5.4 m/s, a 1.78 m stride),
    /// quicker legs when faster, slower when slowed (0.28–0.5 s). Shared by enemy steps, the player's own steps and the
    /// viewmodel's bob.</summary>
    public static float StepInterval(float speed) => Mathf.Clamp(0.33f * 5.4f / Mathf.Max(speed, 0.1f), 0.28f, 0.5f);

    static readonly Dictionary<StepSurface, float[][]> bank = new();
    static readonly List<AudioStreamPlayer> pool = new();
    static readonly ulong[] started = new ulong[Voices];
    static Node? owner;

    /// <summary>Steps played / occluded steps since the last <see cref="ResetStats"/> (dev logs).</summary>
    public static int Played, Muffled;
    public static void ResetStats() { Played = 0; Muffled = 0; }

    /// <summary>The dry takes of a surface (built once, ~5 ms).</summary>
    public static float[][] Takes(StepSurface s)
    {
        if (bank.TryGetValue(s, out var t)) return t;
        t = new float[Variants][];
        for (int i = 0; i < Variants; i++) t[i] = FootstepSynth.Render(s, i / 2, (i & 1) == 1);
        return bank[s] = t;
    }

    static bool Ready()
    {
        if (owner != null && GodotObject.IsInstanceValid(owner) && pool.Count == Voices) return true;
        if (Sfx.I == null || !GodotObject.IsInstanceValid(Sfx.I)) return false;
        owner = Sfx.I;
        pool.Clear();
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Name = $"Footstep{i}" };
            owner.AddChild(p);
            pool.Add(p);
        }
        return true;
    }

    static void PlayStereo(byte[] pcm, float pitch)
    {
        int pick = 0;
        ulong oldest = ulong.MaxValue;
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].Playing) { pick = i; break; }
            if (started[i] < oldest) { oldest = started[i]; pick = i; }
        }
        var p = pool[pick];
        p.Stop();
        p.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = FootstepSynth.SR, Stereo = true, Data = pcm,
        };
        p.VolumeDb = 0f;
        p.PitchScale = pitch;
        p.Play();
        started[pick] = Time.GetTicksUsec();
    }

    /// <summary>Gain of a running step at distance <paramref name="d"/> (0 beyond <see cref="Range"/>).</summary>
    public static float Gain(float d) => FootstepSynth.Gain(d);

    /// <summary>Is the step occluded from the listener (map geometry only: smokes don't muffle sound)?</summary>
    public static bool Occluded(IGame g, Vector3 feet) =>
        Collision.Blocked(g.View.Eye, feet + new Vector3(0, 0.5f, 0), g.Solid) && Collision.Blocked(g.View.Eye, feet + new Vector3(0, 1.5f, 0), g.Solid);

    /// <summary>Plays one enemy footstep at <paramref name="feet"/> for the player's ears. Returns false when it's out of
    /// range (or a preview, which is silent).</summary>
    public static bool Step(IGame g, Vector3 feet, StepSurface surface, bool rightFoot, float vol = 1f)
    {
        if (g.Demo) return false;
        float d = feet.DistanceTo(g.View.Eye);
        float gain = Gain(d);
        if (gain <= 0f || !Ready()) return false;
        bool occ = Occluded(g, feet);
        if (occ) { gain *= 0.7f; Muffled++; }
        Played++;
        Sfx.DuckSkin(); // gameplay cue: skin sounds duck under it
        var takes = Takes(surface);
        var dry = takes[2 * Random.Shared.Next(Variants / 2) + (rightFoot ? 1 : 0)];
        var cue = FootstepSynth.CueFor(g.View.Eye, g.View.ViewYaw, feet + new Vector3(0, 0.05f, 0), occ);
        float level = Sfx.Volume * EnemyLevel * vol * gain * (0.9f + 0.2f * Random.Shared.NextSingle());
        PlayStereo(FootstepSynth.SpatializePcm(dry, cue, level), 0.95f + 0.1f * Random.Shared.NextSingle());
        if (Played == 1 && Core.CmdLine.Dev) // dev log: the first enemy step since start / ResetStats proves the path works
            GD.Print(System.FormattableString.Invariant(
                $"[footsteps] first enemy step: {surface}, {d:0.0} m, L {cue.Left:0.00} R {cue.Right:0.00}, cutoff {cue.CutoffHz:0} Hz{(occ ? ", through a wall" : "")}"));
        return true;
    }

    /// <summary>Your own step (or a landing): centred, close, quieter than an enemy's. The caller mutes it in previews.</summary>
    public static void Own(StepSurface surface, bool rightFoot, float vol, float pitch = 1f)
    {
        if (!Ready()) return;
        Sfx.DuckSkin();
        var takes = Takes(surface);
        var dry = takes[2 * Random.Shared.Next(Variants / 2) + (rightFoot ? 1 : 0)];
        var cue = new FootstepSynth.Cue(0.78f, 0.78f, 0, 0, 0.8f, 0.8f, 9000f, 0.04f, (int)(0.005f * FootstepSynth.SR));
        PlayStereo(FootstepSynth.SpatializePcm(dry, cue, Sfx.Volume * vol), pitch * (0.95f + 0.1f * Random.Shared.NextSingle()));
    }

    /// <summary>Surface under a foot: the topmost map box under it. Crates are wood (metal on Icebox, whose crates, containers
    /// and walkways are metal), raised site floors tile, thin floating walkways metal, Icebox snow and Breeze sand soft,
    /// curbs and the rest concrete. A heuristic: the map data only knows Floor / Elevated / Trim / Cover / Wall.</summary>
    public static StepSurface SurfaceAt(MapSpot? map, Vector3 feet)
    {
        if (map == null) return StepSurface.Concrete;
        BoxDef? best = null;
        float bestY = float.MinValue;
        foreach (var b in map.Boxes)
        {
            if (feet.X < b.Min.X || feet.X > b.Max.X || feet.Z < b.Min.Z || feet.Z > b.Max.Z) continue;
            if (b.Max.Y > feet.Y + 0.1f || b.Max.Y < feet.Y - 0.35f || b.Max.Y <= bestY) continue;
            best = b; bestY = b.Max.Y;
        }
        bool ice = map.Key == "icebox";
        if (best == null) return ice || map.Key == "breeze" ? StepSurface.Soft : StepSurface.Concrete;
        return best.Surf switch
        {
            Surf.Cover => ice ? StepSurface.Metal : StepSurface.Wood,
            Surf.Elevated when ice => StepSurface.Metal,
            Surf.Elevated when best.Max.Y - best.Min.Y < 0.5f && best.Min.Y > 0.3f => StepSurface.Metal, // floating walkways
            Surf.Elevated => StepSurface.Tile,
            Surf.Floor when ice || map.Key == "breeze" => StepSurface.Soft,
            _ => StepSurface.Concrete,
        };
    }
}

/// <summary>Footstep timing for one walker: call <see cref="Update"/> every frame with its speed. Alternates feet and
/// jitters each step by a few ms, like real running.</summary>
public sealed class FootstepEmitter
{
    float t = 0.05f;
    bool right;
    /// <summary>Steps this emitter played (0 when the walker only walked / crouched).</summary>
    public int Steps;

    public void Update(IGame g, MapSpot? map, Vector3 feet, float speed, bool crouched, float dt, float vol = 1f)
    {
        if (crouched || speed <= FootstepFx.AudibleSpeed) { t = 0.05f; return; }
        t -= dt;
        if (t > 0) return;
        // ~0.33 s per step at full run speed (5.4 m/s), a little slower when slowed; ±12 ms of human unevenness.
        t += FootstepFx.StepInterval(speed) + 0.012f * (Random.Shared.NextSingle() * 2f - 1f);
        right = !right;
        if (FootstepFx.Step(g, feet, FootstepFx.SurfaceAt(map, feet), right, vol)) Steps++;
    }
}
