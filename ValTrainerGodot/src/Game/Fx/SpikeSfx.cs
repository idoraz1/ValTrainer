using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Synthesized spike sounds (no recordings, our own design): "beep" (short high chirp, sped up as the timer runs down),
/// "plant" (rising three-note arm sequence), "defuse_start" (rising warble the defuser's opponents hear), "defuse_tick"
/// (soft click-tone while a defuse is running), "half" (two-note chime at the half-defuse checkpoint), "defused" (falling
/// power-down sweep), "detonate" (deep boom with a long noisy tail) and "fail" (low buzz for a lost round).
/// Positional playback uses the game's flat distance falloff and the master volume.
/// </summary>
public static class SpikeSfx
{
    const int Rate = 22050;
    static readonly Dictionary<string, AudioStreamWav> cache = new();

    static AudioStreamWav Get(string name) => cache.TryGetValue(name, out var s) ? s : cache[name] = Make(name);

    static float Sin(float hz, float t) => MathF.Sin(2f * MathF.PI * hz * t);
    static float Env(float t, float atk, float len) => t < atk ? t / atk : MathF.Max(0f, 1f - (t - atk) / MathF.Max(0.01f, len - atk));

    static AudioStreamWav Make(string name)
    {
        var rng = new Random(name.Length * 7919 + name[0]);
        float dur = name switch
        {
            "beep" => 0.09f, "plant" => 0.75f, "defuse_start" => 0.55f, "defuse_tick" => 0.05f, "half" => 0.42f,
            "defused" => 0.9f, "detonate" => 2.6f, "fail" => 0.5f, _ => 0.2f,
        };
        int n = (int)(dur * Rate);
        var data = new byte[n * 2];
        float lp = 0f, lp2 = 0f, phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            float v;
            switch (name)
            {
                case "beep":
                    v = (Sin(2150f, t) * 0.8f + Sin(4300f, t) * 0.18f) * Env(t, 0.004f, dur) * 0.55f;
                    break;
                case "plant":
                {
                    // three rising notes, then a soft hum
                    int note = Math.Min(2, (int)(t / 0.16f));
                    float nt = t - note * 0.16f;
                    float hz = note switch { 0 => 880f, 1 => 1175f, _ => 1568f };
                    v = Sin(hz, t) * Env(nt, 0.005f, note < 2 ? 0.15f : 0.4f) * 0.5f + Sin(110f, t) * 0.12f * Env(t, 0.2f, dur);
                    break;
                }
                case "defuse_start":
                {
                    float hz = 500f + 900f * (t / dur) + 60f * MathF.Sin(2f * MathF.PI * 18f * t);
                    phase += hz / Rate;
                    v = MathF.Sin(2f * MathF.PI * phase) * Env(t, 0.03f, dur) * 0.45f;
                    break;
                }
                case "defuse_tick":
                    v = (Sin(1250f, t) * 0.6f + noise * 0.2f) * Env(t, 0.002f, dur) * 0.4f;
                    break;
                case "half":
                    v = (t < 0.18f ? Sin(1046f, t) * Env(t, 0.004f, 0.18f) : Sin(1568f, t) * Env(t - 0.18f, 0.004f, 0.24f)) * 0.45f;
                    break;
                case "defused":
                {
                    float hz = 1400f * MathF.Exp(-t * 2.4f) + 90f;
                    phase += hz / Rate;
                    v = (MathF.Sin(2f * MathF.PI * phase) * 0.5f + MathF.Sin(4f * MathF.PI * phase) * 0.15f) * Env(t, 0.01f, dur);
                    break;
                }
                case "detonate":
                {
                    lp += (noise - lp) * 0.05f;
                    lp2 += (lp - lp2) * 0.08f;
                    float boom = MathF.Sin(2f * MathF.PI * (52f - 18f * MathF.Min(1f, t)) * t) * MathF.Exp(-t * 1.6f);
                    float suck = Sin(220f * MathF.Exp(-t * 3f) + 40f, t) * Env(t, 0.002f, 0.35f) * 0.4f;
                    v = MathF.Tanh((boom * 1.2f + lp2 * 5f * MathF.Exp(-t * 1.1f) + suck) * 1.3f) * 0.85f;
                    break;
                }
                case "fail":
                    v = (Sin(140f, t) + 0.5f * Sin(147f, t)) * Env(t, 0.01f, dur) * 0.35f;
                    break;
                default:
                    v = noise * Env(t, 0.005f, dur) * 0.3f;
                    break;
            }
            short smp = (short)(Mathf.Clamp(v, -1f, 1f) * 30000f);
            data[i * 2] = (byte)(smp & 0xff);
            data[i * 2 + 1] = (byte)((smp >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    /// <summary>Plays a spike sound at <paramref name="pos"/> (silent beyond <paramref name="maxRange"/>; never quieter than
    /// <paramref name="floor"/> inside it, like VALORANT's spike which you hear across the site).</summary>
    public static void PlayAt(Node parent, string name, Vector3 pos, Vector3 listener, float vol = 1f, float maxRange = 60f, float floor = 0.35f)
    {
        float d = pos.DistanceTo(listener);
        if (d > maxRange || parent == null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree()) return;
        float k = Mathf.Clamp((d - 2f) / (maxRange - 2f), 0f, 1f);
        float g = floor + (1f - floor) * (1f - k) * (1f - k);
        var p = new AudioStreamPlayer3D
        {
            Stream = Get(name), AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol * g)), Autoplay = false,
        };
        parent.AddChild(p);
        p.GlobalPosition = pos;
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>Non-positional (your own defuse, the round result).</summary>
    public static void Play(Node parent, string name, float vol = 1f)
    {
        if (parent == null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree()) return;
        var p = new AudioStreamPlayer { Stream = Get(name), VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol)) };
        parent.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }
}
