using Godot;
using ValTrainer.Audio;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Synthesized cues for the duelist drills (no samples; built once): wind gust (Tailwind prime / dash), updraft whoosh,
/// sprint hum and slide (High Gear), satchel throw / beep / blast (Blast Pack), soul orb, intangible (Dismiss), rift tether
/// hum and teleport (Gatecrash), light dash and refract (Waylay), shield form / break and orb (Double Tap). Same flat
/// VALORANT-like distance curve and pooling as <see cref="AbilitySfx"/>.
/// </summary>
public static class DuelistSfx
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

        // Wind: band-limited noise with a sweeping cutoff.
        Add("wind_prime", 0.9f, t => LP(N(), 0.04f + 0.18f * Env(t, 0.25f, 0.3f)) * Env(t, 0.2f, 0.35f) * 1.4f);
        Add("wind_dash", 0.6f, t => (LP(N(), 0.35f * Env(t, 0.02f, 0.15f) + 0.03f) * 1.3f + Sin(180f + 300f * t, t) * 0.12f) * Env(t, 0.015f, 0.18f));
        Add("updraft", 0.8f, t => (LP(N(), 0.1f + 0.3f * Env(t, 0.05f, 0.2f)) * 1.2f + Sin(120f + 500f * t, t) * 0.15f) * Env(t, 0.02f, 0.3f));
        // High Gear: electric hum + crackle; slide: gritty noise burst.
        Add("sprint_on", 0.5f, t => (Sin(110f, t) * 0.3f + Sin(220f * (1 + 0.5f * t), t) * 0.2f + (N() > 0.97f ? N() * 0.6f : 0)) * Env(t, 0.02f, 0.18f));
        Add("slide", 0.7f, t => (LP(N(), 0.5f) * 0.8f + (N() > 0.95f ? N() * 0.5f : 0) + Sin(90f, t) * 0.15f) * Env(t, 0.01f, 0.3f));
        // Blast Pack.
        Add("satchel_throw", 0.25f, t => LP(N(), 0.3f) * Env(t, 0.005f, 0.06f) * 0.7f + Sin(600f - 400f * t, t) * Env(t, 0.002f, 0.05f) * 0.2f);
        Add("satchel_beep", 0.12f, t => Sin(1900f, t) * Env(t, 0.002f, 0.04f) * 0.45f);
        Add("satchel_blast", 1.3f, t =>
        {
            float thump = Sin(55f * (1 + 2f * Env(t, 0, 0.04f)), t) * Env(t, 0.002f, 0.22f) * 0.9f;
            float crack = LP(N(), 0.6f) * Env(t, 0.001f, 0.08f) * 0.9f;
            float rumble = LP2(N(), 0.03f) * Env(t, 0.02f, 0.5f) * 2.2f;
            return thump + crack + rumble;
        });
        // Reyna.
        Add("soul_orb", 0.6f, t => (Sin(420f + 80f * Sin(5f, t), t) * 0.3f + Sin(840f, t) * 0.1f) * Env(t, 0.03f, 0.25f));
        Add("dismiss", 0.9f, t => (Sin(300f - 150f * t, t) * 0.25f + LP(N(), 0.08f) * 0.6f + Sin(610f - 300f * t, t) * 0.12f) * Env(t, 0.02f, 0.4f));
        Add("devour", 0.7f, t => (Sin(200f + 300f * t, t) * 0.3f + LP(N(), 0.1f) * 0.3f) * Env(t, 0.03f, 0.3f));
        // Yoru.
        Add("rift_send", 0.5f, t => (Sin(330f + 400f * t, t) * 0.3f + LP(N(), 0.2f) * 0.35f) * Env(t, 0.01f, 0.15f));
        Add("teleport_out", 0.6f, t => (LP(N(), 0.15f) * 0.9f + Sin(160f + 900f * t, t) * 0.25f) * Env(t, 0.02f, 0.2f));
        Add("teleport_in", 0.9f, t => (Sin(900f - 700f * Mathf.Clamp(t, 0, 1), t) * 0.35f + LP(N(), 0.25f) * Env(t, 0.003f, 0.12f) * 0.9f
                                       + Sin(70f, t) * Env(t, 0.003f, 0.25f) * 0.5f) * Env(t, 0.003f, 0.35f));
        // Waylay.
        Add("light_dash", 0.45f, t => (Sin(1200f + 1600f * t, t) * 0.18f + Sin(2400f + 2000f * t, t) * 0.08f + LP(N(), 0.4f) * 0.5f) * Env(t, 0.005f, 0.12f));
        Add("refract", 0.8f, t => (Sin(1320f, t) * 0.2f + Sin(1980f, t) * 0.12f + Sin(2640f, t) * 0.06f) * Env(t, 0.01f, 0.3f));
        // Iso.
        Add("shield_on", 0.6f, t => (Sin(520f, t) * 0.3f + Sin(780f, t) * 0.2f + Sin(1040f, t) * 0.1f) * Env(t, 0.05f, 0.22f));
        Add("shield_break", 0.5f, t => (LP(N(), 0.7f) * 0.7f + Sin(1400f - 1000f * t, t) * 0.3f) * Env(t, 0.002f, 0.12f));
        Add("iso_orb", 0.4f, t => (Sin(660f, t) * 0.25f + Sin(990f, t) * 0.15f) * Env(t, 0.01f, 0.15f));
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

    /// <summary>Builds the sounds now (the round countdown) so the first cast doesn't hitch.</summary>
    public static void Prewarm() => Build();

    static bool Ready(IGame g)
    {
        Build();
        if (root != null && GodotObject.IsInstanceValid(root) && root.IsInsideTree()) return true;
        if (g.World == null || !GodotObject.IsInstanceValid(g.World)) return false;
        root = new Node3D { Name = "DuelistSfx" };
        g.World.AddChild(root);
        p2.Clear(); p3.Clear();
        for (int i = 0; i < 6; i++) { var p = new AudioStreamPlayer(); root.AddChild(p); p2.Add(p); }
        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, PanningStrength = 0.85f };
            root.AddChild(p);
            p3.Add(p);
        }
        return true;
    }

    /// <summary>Non-positional (your own ability).</summary>
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
