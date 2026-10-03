using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Synthesized cues for the initiator-drill abilities (no samples; built once, ~0.3 MB): equip, bow release, bolt bounce,
/// recon ping, reveal alert, haunt, leer, paranoia, dizzy plasma, "no charges". World sounds use the same flat VALORANT-like
/// distance curve as <see cref="Sfx.PlayAt"/>; players are pooled under the session's world node.
/// </summary>
public static class AbilitySfx
{
    const float SR = 44100f;
    static readonly Dictionary<string, AudioStreamWav> streams = new();
    static Node3D? root;
    static readonly List<AudioStreamPlayer> p2 = new();
    static readonly List<AudioStreamPlayer3D> p3 = new();
    static int n2, n3;

    static float Env(float t, float atk, float tau) => t < 0 ? 0 : t < atk ? t / atk : MathF.Exp(-(t - atk) / tau);
    static float Sin(float hz, float t) => MathF.Sin(2 * MathF.PI * hz * t);

    static void Build()
    {
        if (streams.Count > 0) return;
        var rng = new Random(11);
        float N() => (float)rng.NextDouble() * 2 - 1;
        float lp = 0;
        float LP(float x, float a) => lp += a * (x - lp);

        Add("equip", 0.22f, t => (LP(N(), 0.25f) * Env(t, 0.01f, 0.05f) * 0.6f + Sin(900 + 600 * t, t) * Env(t, 0.003f, 0.04f) * 0.25f));
        Add("bow_release", 0.5f, t =>
            Sin(150f * (1 + 0.4f * Env(t, 0, 0.02f)), t) * Env(t, 0.001f, 0.12f) * 0.55f
            + Sin(310f, t) * Env(t, 0.001f, 0.06f) * 0.2f + LP(N(), 0.2f) * Env(t, 0.02f, 0.12f) * 0.5f);
        Add("bolt_tick", 0.18f, t => (Sin(2400, t) * 0.5f + N() * 0.4f) * Env(t, 0.0005f, 0.03f));
        Add("recon_ping", 1.1f, t =>
        {
            float f = 1500f - 300f * Mathf.Clamp(t / 0.6f, 0f, 1f);
            float a = Sin(f, t) * Env(t, 0.004f, 0.32f) * 0.5f + Sin(f * 2.01f, t) * Env(t, 0.004f, 0.12f) * 0.15f;
            float echo = t > 0.18f ? Sin(f * 0.98f, t - 0.18f) * Env(t - 0.18f, 0.004f, 0.3f) * 0.22f : 0f;
            return a + echo;
        });
        Add("reveal", 0.42f, t => (t < 0.12f ? Sin(1180, t) : Sin(1560, t)) * Env(t % 0.14f, 0.004f, 0.06f) * (t < 0.3f ? 0.45f : 0f));
        Add("haunt", 1.2f, t =>
        {
            float vib = 1 + 0.05f * Sin(6.5f, t);
            return (Sin(260f * vib * (1 - 0.35f * Mathf.Clamp(t, 0, 1)), t) * 0.35f + Sin(390f * vib, t) * 0.12f
                    + LP(N(), 0.08f) * 0.35f) * Env(t, 0.08f, 0.45f);
        });
        Add("leer", 0.6f, t => (Sin(520f + 900f * t, t) * 0.3f + Sin(780f + 1300f * t, t) * 0.15f) * Env(t, 0.02f, 0.2f)
                                + LP(N(), 0.3f) * Env(t, 0.01f, 0.08f) * 0.25f);
        Add("paranoia", 1.4f, t =>
        {
            float wob = 1 + 0.08f * Sin(3.2f, t);
            return (LP(N(), 0.05f) * 0.7f + Sin(95f * wob, t) * 0.35f + Sin(190f * wob, t) * 0.12f) * Env(t, 0.06f, 0.5f);
        });
        Add("dizzy", 0.25f, t => Sin(900f * MathF.Exp(-t * 9f) + 200f, t) * Env(t, 0.002f, 0.07f) * 0.5f);
        Add("empty", 0.16f, t => Sin(220, t) * Env(t, 0.002f, 0.05f) * 0.4f);
    }

    static void Add(string name, float seconds, Func<float, float> f)
    {
        int n = (int)(seconds * SR);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float x = f(i / SR);
            if (float.IsNaN(x)) x = 0;
            float fade = Math.Min(1f, (n - i) / 300f);
            short v = (short)(Math.Clamp(x * fade, -1f, 1f) * short.MaxValue);
            data[2 * i] = (byte)(v & 0xff);
            data[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        streams[name] = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = (int)SR, Stereo = false, Data = data };
    }

    static bool Ready(IGame g)
    {
        Build();
        if (root != null && GodotObject.IsInstanceValid(root) && root.IsInsideTree()) return true;
        if (g.World == null || !GodotObject.IsInstanceValid(g.World)) return false;
        root = new Node3D { Name = "AbilitySfx" };
        g.World.AddChild(root);
        p2.Clear(); p3.Clear();
        for (int i = 0; i < 6; i++) { var p = new AudioStreamPlayer(); root.AddChild(p); p2.Add(p); }
        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, PanningStrength = 0.85f };
            root.AddChild(p);
            p3.Add(p);
        }
        return true;
    }

    /// <summary>Non-positional (your own cast).</summary>
    public static void Play(IGame g, string name, float vol = 1f, float pitch = 1f)
    {
        if (!Ready(g) || !streams.TryGetValue(name, out var s)) return;
        var p = p2[n2 = (n2 + 1) % p2.Count];
        p.Stream = s;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol));
        p.PitchScale = pitch;
        p.Play();
    }

    /// <summary>Positional, VALORANT-like flat falloff (floor until ~maxRange, then fades out).</summary>
    public static void PlayAt(IGame g, string name, Vector3 pos, float vol = 1f, float maxRange = 50f, float floor = 0.5f)
    {
        if (!Ready(g) || !streams.TryGetValue(name, out var s)) return;
        float d = pos.DistanceTo(g.View.Eye);
        if (d > maxRange) return;
        float k = Mathf.Clamp((d - 2f) / (maxRange - 2f), 0f, 1f);
        float gain = (floor + (1 - floor) * (1 - k) * (1 - k)) * (k < 0.9f ? 1f : (1 - k) / 0.1f);
        var p = p3[n3 = (n3 + 1) % p3.Count];
        p.Stream = s;
        p.GlobalPosition = pos;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol * gain));
        p.PitchScale = 1f;
        p.Play();
    }
}
