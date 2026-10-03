using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Synthesized cues for the sentinel setups of the Site Anchor drill (no samples, built once): place, deploy, alarm,
/// turret burst, wire snap, sonic concuss, barrier rise, vines, shear wall, crystal slow, choke trap, interceptor beam,
/// trademark charge, device destroyed. World sounds use the same flat VALORANT-like distance curve as <see cref="Sfx.PlayAt"/>.
/// </summary>
public static class SentinelSfx
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
        var rng = new Random(23);
        float N() => (float)rng.NextDouble() * 2 - 1;
        float lp = 0, lp2 = 0;
        float LP(float x, float a) => lp += a * (x - lp);
        float LP2(float x, float a) => lp2 += a * (x - lp2);

        Add("place", 0.25f, t => Sin(180f, t) * Env(t, 0.002f, 0.05f) * 0.5f + LP(N(), 0.3f) * Env(t, 0.001f, 0.03f) * 0.45f
                                   + Sin(1300f, t) * Env(t - 0.06f, 0.002f, 0.04f) * 0.15f);
        Add("deploy", 0.6f, t => Sin(500f + 900f * Mathf.Clamp(t / 0.4f, 0, 1), t) * Env(t, 0.02f, 0.25f) * 0.25f
                                   + LP(N(), 0.15f) * Env(t, 0.01f, 0.1f) * 0.2f);
        Add("reuse", 0.15f, t => Sin(1600f, t) * Env(t, 0.001f, 0.03f) * 0.35f + Sin(2400f, t) * Env(t - 0.05f, 0.001f, 0.03f) * 0.25f);
        Add("alarm", 0.7f, t => (t % 0.18f < 0.09f ? Sin(1850f, t) : Sin(1250f, t)) * Env(t % 0.18f, 0.003f, 0.06f) * (t < 0.55f ? 0.45f : 0f));
        Add("boom", 0.8f, t => (LP(N(), 0.12f) * 0.9f + Sin(70f * (1 + Env(t, 0, 0.04f)), t) * 0.7f) * Env(t, 0.002f, 0.18f));
        Add("turret", 0.12f, t => (Sin(2200f * (1 - 0.4f * t / 0.12f), t) * 0.4f + N() * 0.35f) * Env(t, 0.0005f, 0.025f));
        Add("wire", 0.9f, t =>
        {
            float f = 440f * (1 + 0.02f * Sin(30, t));
            return (Sin(f, t) * 0.35f + Sin(f * 2.5f, t) * 0.15f) * Env(t, 0.002f, 0.35f) + LP(N(), 0.4f) * Env(t, 0.001f, 0.02f) * 0.4f;
        });
        Add("snap", 0.2f, t => (N() * 0.5f + Sin(900f, t) * 0.3f) * Env(t, 0.0005f, 0.03f));
        Add("concuss", 1.3f, t =>
        {
            float wob = 1 + 0.15f * Sin(9f, t);
            return (Sin(55f * wob, t) * 0.8f + LP(N(), 0.05f) * 0.6f + Sin(330f * wob, t) * 0.12f) * Env(t, 0.01f, 0.45f);
        });
        Add("hum", 1.0f, t => (Sin(120f, t) * 0.3f + Sin(240f, t) * 0.15f + Sin(600f + 300f * t, t) * 0.1f) * Env(t, 0.15f, 0.5f));
        Add("barrier", 1.1f, t =>
        {
            float f = 300f + 500f * Mathf.Clamp(t / 0.8f, 0, 1);
            return (Sin(f, t) * 0.25f + Sin(f * 1.5f, t) * 0.12f + LP(N(), 0.2f) * 0.35f) * Env(t, 0.05f, 0.4f);
        });
        Add("vines", 1.0f, t => (LP(N(), 0.5f) - LP2(N(), 0.05f)) * (0.6f + 0.4f * Sin(14f, t)) * Env(t, 0.02f, 0.45f) * 0.8f);
        Add("shear", 0.9f, t => (LP(N(), 0.08f) * 0.8f + Sin(90f, t) * 0.5f * Env(t - 0.05f, 0.005f, 0.2f)) * Env(t, 0.01f, 0.3f));
        Add("crystal", 0.9f, t =>
        {
            float s = 0;
            for (int i = 0; i < 4; i++) s += Sin(1100f + 430f * i, t) * Env(t - i * 0.045f, 0.002f, 0.18f);
            return s * 0.16f + LP(N(), 0.3f) * Env(t, 0.002f, 0.06f) * 0.35f;
        });
        Add("crunch", 0.18f, t => LP(N(), 0.35f) * Env(t, 0.002f, 0.04f) * 0.7f + Sin(2600f, t) * Env(t, 0.001f, 0.02f) * 0.1f);
        Add("choke", 0.9f, t =>
        {
            float f = 160f * (1 - 0.4f * Mathf.Clamp(t, 0, 1));
            return (Sin(f, t) * 0.5f + LP(N(), 0.1f) * 0.5f) * Env(t, 0.01f, 0.3f) + Sin(1400f, t) * Env(t, 0.001f, 0.03f) * 0.2f;
        });
        Add("beam", 0.35f, t => (Sin(3000f - 4000f * t, t) * 0.3f + N() * 0.25f) * Env(t, 0.002f, 0.12f));
        Add("charge", 0.9f, t => Sin(400f + 1400f * Mathf.Clamp(t / 0.85f, 0, 1), t) * Env(t, 0.3f, 2f) * (t < 0.88f ? 0.3f : 0f));
        Add("broken", 0.4f, t => (LP(N(), 0.4f) * 0.6f + Sin(240f * (1 - t), t) * 0.3f) * Env(t, 0.002f, 0.1f));
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
        root = new Node3D { Name = "SentinelSfx" };
        g.World.AddChild(root);
        p2.Clear(); p3.Clear();
        for (int i = 0; i < 6; i++) { var p = new AudioStreamPlayer(); root.AddChild(p); p2.Add(p); }
        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, PanningStrength = 0.85f };
            root.AddChild(p);
            p3.Add(p);
        }
        return true;
    }

    /// <summary>Non-positional (your own actions).</summary>
    public static void Play(IGame g, string name, float vol = 1f, float pitch = 1f)
    {
        if (!Ready(g) || !streams.TryGetValue(name, out var s)) return;
        var p = p2[n2 = (n2 + 1) % p2.Count];
        p.Stream = s;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Sfx.Volume * vol));
        p.PitchScale = pitch;
        p.Play();
    }

    /// <summary>Positional, VALORANT-like flat falloff.</summary>
    public static void PlayAt(IGame g, string name, Vector3 pos, float vol = 1f, float maxRange = 50f, float floor = 0.5f, float pitch = 1f)
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
        p.PitchScale = pitch;
        p.Play();
    }
}
