using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Synthesized smoke sounds (no recordings): "launch" (rising filtered-noise whoosh), "pop" (low thump + swelling hiss as
/// a smoke forms) and "warn" (soft fading hiss before a smoke expires). Played positionally with the game's flat
/// distance falloff and the master volume.
/// </summary>
public static class SmokeSfx
{
    const int Rate = 22050;
    static readonly Dictionary<string, AudioStreamWav> cache = new();

    static AudioStreamWav Get(string name)
    {
        if (cache.TryGetValue(name, out var s)) return s;
        return cache[name] = Make(name);
    }

    static AudioStreamWav Make(string name)
    {
        var rng = new Random(name.Length * 7919 + name[0]);
        float dur = name switch { "launch" => 0.45f, "pop" => 1.15f, "warn" => 0.75f, _ => 0.3f };
        int n = (int)(dur * Rate);
        var data = new byte[n * 2];
        float lp = 0f, lp2 = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            float v;
            switch (name)
            {
                case "launch":
                {
                    float cut = 0.03f + 0.3f * (t / dur);
                    lp += (noise - lp) * cut;
                    hp = lp - prev; prev = lp;
                    v = (lp * 1.6f + hp * 0.8f) * Env(t, 0.05f, dur);
                    break;
                }
                case "pop":
                {
                    lp += (noise - lp) * 0.09f;
                    lp2 += (lp - lp2) * 0.09f;
                    float thump = MathF.Sin(2f * MathF.PI * (75f - 35f * t) * t) * MathF.Exp(-t * 8f);
                    v = lp2 * 3.4f * Env(t, 0.08f, dur) + thump * 0.55f;
                    break;
                }
                default:
                {
                    float cut = 0.32f - 0.26f * (t / dur);
                    lp += (noise - lp) * cut;
                    v = lp * Env(t, 0.03f, dur) * 0.9f;
                    break;
                }
            }
            short smp = (short)(Mathf.Clamp(v, -1f, 1f) * 30000f);
            data[i * 2] = (byte)(smp & 0xff);
            data[i * 2 + 1] = (byte)((smp >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    static float Env(float t, float atk, float len) => t < atk ? t / atk : MathF.Max(0f, 1f - (t - atk) / MathF.Max(0.01f, len - atk));

    /// <summary>Plays a smoke sound at <paramref name="pos"/> (silent beyond <paramref name="maxRange"/>).</summary>
    public static void PlayAt(Node parent, string name, Vector3 pos, Vector3 listener, float vol = 1f, float pitch = 1f, float maxRange = 55f)
    {
        float d = pos.DistanceTo(listener);
        if (d > maxRange || parent == null || !GodotObject.IsInstanceValid(parent)) return;
        float k = Mathf.Clamp((d - 2f) / (maxRange - 2f), 0f, 1f);
        float g = 0.45f + 0.55f * (1f - k) * (1f - k);
        var p = new AudioStreamPlayer3D
        {
            Stream = Get(name), AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol * g)), PitchScale = pitch, Autoplay = false,
        };
        parent.AddChild(p);
        p.GlobalPosition = pos;
        p.Finished += p.QueueFree;
        p.Play();
    }
}
