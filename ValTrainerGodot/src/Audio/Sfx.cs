using Godot;

namespace ValTrainer.Audio;

/// <summary>
/// All audio is synthesized at startup (Riot's audio isn't legally usable). Recipes approximate VALORANT's
/// sound design (punchy Vandal crack, "metal bat" headshot, rising multi-kill sounds, footsteps, a distinct
/// cue per agent flash). 2D sounds use a pool of AudioStreamPlayers; world sounds use AudioStreamPlayer3D
/// (engine panning from the camera) with a VALORANT-like flat distance curve.
/// </summary>
public partial class Sfx : Node
{
    const float SR = 44100f;
    public static Sfx I { get; private set; } = null!;
    public static float Volume = 0.5f;

    readonly Dictionary<string, AudioStream> streams = new();
    /// <summary>Recorded CC0 variants (gunshots, reloads, footsteps) — preferred over the synthesized sound of the same name.</summary>
    readonly Dictionary<string, AudioStream[]> recorded = new();
    readonly List<AudioStreamPlayer> players2D = new();
    readonly List<AudioStreamPlayer3D> players3D = new();
    int next2D, next3D;

    public override void _Ready()
    {
        I = this;
        for (int i = 0; i < 24; i++) { var p = new AudioStreamPlayer(); AddChild(p); players2D.Add(p); }
        for (int i = 0; i < 24; i++)
        {
            var p = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, PanningStrength = 0.85f };
            AddChild(p);
            players3D.Add(p);
        }
        Build();
        LoadRecorded();
    }

    /// <summary>CC0 recordings from assets/audio (Free Firearm Sound Library, OpenGameArt, Kenney). See CREDITS.md.</summary>
    void LoadRecorded()
    {
        void Rec(string name, params string[] files)
        {
            var list = files.Select(f => ResourceLoader.Exists(f) ? GD.Load<AudioStream>(f) : null).Where(x => x != null).Cast<AudioStream>().ToArray();
            if (list.Length > 0) recorded[name] = list;
        }
        const string W = "res://assets/audio/weapons/";
        Rec("vandal", W + "rifle_ak_shot_01.wav", W + "rifle_ak_shot_02.wav", W + "rifle_ak_shot_03.wav");
        Rec("phantom", W + "rifle_ar_shot_01.wav", W + "rifle_ar_shot_02.wav");
        Rec("operator", W + "sniper_bolt_shot_01.wav", W + "sniper_bolt_shot_02.wav");
        Rec("sheriff", W + "pistol_shot_01.wav", W + "pistol_shot_02.wav", W + "pistol_shot_03.wav");
        Rec("enemyshot", W + "rifle_ak_shot_01.wav", W + "rifle_ak_shot_02.wav", W + "rifle_ak_shot_03.wav");
        Rec("reload_rifle", W + "rifle_reload_airsoft.wav");
        Rec("reload_pistol", W + "pistol_reload.wav");
        const string F = "res://assets/audio/footsteps/";
        Rec("footstep", Enumerable.Range(0, 5).Select(i => $"{F}footstep_concrete_00{i}.ogg").ToArray());
    }

    AudioStream? Pick(string name) =>
        recorded.TryGetValue(name, out var r) ? r[Random.Shared.Next(r.Length)] : streams.TryGetValue(name, out var s) ? s : null;

    void Build()
    {
        var rng = new Random(7);
        float N() => (float)rng.NextDouble() * 2 - 1;

        // ---- Weapons ----
        Add("vandal", 0.35f, 6, () =>
        {
            var hp = new OnePole(2000); var bp = new BP(600, 0.8f); var clk = new BP(3500, 4); var tail = new OnePole(800);
            var sw = new Sweep(140, 55, 0.06f);
            return t => Sat(1.8f * (hp.HP(N()) * Env(t, 0.0005f, 0.012f) * 0.55f + bp.P(N()) * Env(t, 0.001f, 0.035f) * 0.45f
                + sw.Next(t) * Env(t, 0.0005f, 0.06f) * 0.6f + clk.P(N()) * Env(t, 0, 0.004f) * 0.2f
                + tail.LP(N()) * Env(t - 0.015f, 0.01f, 0.12f) * 0.15f)) * 0.7f;
        });
        Add("operator", 1.2f, 2, () =>
        {
            var hp = new OnePole(1000); var lp = new OnePole(400); var sw = new Sweep(80, 35, 0.1f);
            return t => Sat(1.8f * (sw.Next(t) * Env(t, 0.001f, 0.18f) * 0.9f + hp.HP(N()) * Env(t, 0.0005f, 0.025f) * 0.7f
                + lp.LP(N()) * (1 + 0.3f * Sin(7, t)) * Env(t, 0.01f, 0.4f) * 0.3f)) * 0.75f;
        });
        Add("bolt", 0.2f, 2, () =>
        {
            var a = new BP(2500, 3); var b = new BP(700, 2);
            return t => (a.P(N()) * Env(t, 0, 0.006f) * 0.5f + b.P(N()) * Env(t, 0, 0.015f) * 0.3f) * 1.4f;
        });
        Add("enemyshot", 0.35f, 4, () =>
        {
            var hp = new OnePole(1800); var bp = new BP(500, 0.8f); var sw = new Sweep(120, 50, 0.06f);
            return t => Sat(1.6f * (hp.HP(N()) * Env(t, 0.0005f, 0.015f) * 0.5f + bp.P(N()) * Env(t, 0.001f, 0.04f) * 0.4f
                + sw.Next(t) * Env(t, 0.0005f, 0.06f) * 0.5f)) * 0.7f;
        });

        // ---- Hit feedback ----
        Add("body", 0.09f, 4, () =>
        {
            var bp = new BP(1200, 1); var sw = new Sweep(240, 160, 0.09f);
            return t => (sw.Next(t) * Env(t, 0, 0.025f) * 0.5f + bp.P(N()) * Env(t, 0, 0.015f) * 0.3f) * 0.9f;
        });
        Add("head", 0.35f, 4, () =>
        {
            var hp = new OnePole(4000);
            return t => Sat(1.2f * (hp.HP(N()) * Env(t, 0, 0.002f) * 0.25f
                + Sin(650, t) * Env(t, 0.001f, 0.12f) * 0.45f + Sin(180, t) * Env(t, 0, 0.02f) * 0.4f
                + Sin(1950, t) * Env(t, 0.001f, 0.09f) * 0.5f + Sin(5380, t) * Env(t, 0.001f, 0.04f) * 0.18f
                + Sin(1794, t) * Env(t, 0.001f, 0.07f) * 0.2f)) * 0.6f;
        });
        int[] semis = { 0, 2, 4, 7, 12 };
        for (int k = 1; k <= 5; k++)
        {
            float f = 660f * MathF.Pow(2, semis[k - 1] / 12f);
            bool ace = k == 5;
            Add("kill" + k, ace ? 0.8f : 0.45f, 2, () =>
            {
                var hp = new OnePole(6000);
                return t =>
                {
                    float s = (Sin(f, t) + 0.4f * Sin(2 * f, t) + 0.15f * Sin(3 * f, t)) * Env(t, 0.002f, 0.15f) * 0.45f
                              + hp.HP(N()) * Env(t, 0, 0.06f) * 0.08f + Sin(90, t) * Env(t, 0, 0.04f) * 0.35f;
                    if (ace)
                        for (int i = 1; i <= 3; i++)
                            s += Sin(f * new[] { 1.26f, 1.5f, 2f }[i - 1], t) * Env(t - 0.04f * i, 0.002f, 0.3f) * 0.25f;
                    return s;
                };
            });
        }

        // ---- Footsteps (4 variants) ----
        for (int v = 0; v < 4; v++)
            Add("step" + v, 0.12f, 3, () =>
            {
                var heel = new BP(180 + 20 * v, 1); var scuff = new BP(2500, 0.7f);
                return t => (heel.P(N()) * Env(t, 0, 0.015f) * 0.6f + Sin(90, t) * Env(t, 0, 0.02f) * 0.4f
                    + scuff.P(N()) * Env(t - 0.01f, 0.002f, 0.03f) * 0.25f) * 1.6f;
            });

        // ---- UI ----
        Add("tick", 0.06f, 2, () => t => (Sin(880, t) + 0.25f * Sin(2640, t)) * Env(t, 0.001f, 0.025f) * 0.35f);
        Add("go", 0.45f, 1, () =>
        {
            var sw = new BPSweep(500, 4000, 0.4f, 1.5f);
            return t => (Sin(660, t) * Env(t, 0.002f, 0.12f) + Sin(990, t) * Env(t - 0.05f, 0.002f, 0.12f)) * 0.3f + sw.Next(t, N()) * 0.25f;
        });
        Add("fail", 0.25f, 2, () => { var sw = new Sweep(220, 120, 0.25f); return t => sw.Next(t) * Env(t, 0, 0.11f) * 0.45f; });
        Add("damage", 0.12f, 2, () =>
        {
            var lp = new OnePole(1500); var sw = new Sweep(150, 70, 0.12f);
            return t => sw.Next(t) * Env(t, 0, 0.04f) * 0.6f + lp.LP(N()) * Env(t, 0, 0.03f) * 0.3f;
        });
        Add("death", 0.6f, 1, () =>
        {
            var lp = new OnePole(500); var sw = new Sweep(90, 40, 0.6f);
            return t => (sw.Next(t) * Env(t, 0.005f, 0.2f) * 0.7f + lp.LP(N()) * Env(t, 0.1f, 0.2f) * 0.4f) * 0.9f;
        });

        // ---- Agent flash cues: windup (plays at throw/trigger) and pop ----
        Add("phoenix_wind", 0.7f, 2, () =>
        {
            var sw = new BPSweep(800, 3000, 0.6f, 2f); var crack = new BP(1500, 2);
            return t => (sw.Next(t, N()) * (0.7f + 0.3f * Sin(12, t)) * 0.6f
                + crack.P(rng.NextDouble() < 0.0015 ? 1f : 0f) * 4f) * Env(t, 0.04f, 0.5f);
        });
        Add("breach_wind", 0.6f, 2, () =>
        {
            var crack = new BP(3000, 3); var lp = new OnePole(300);
            return t =>
            {
                float density = 0.0007f + 0.0025f * (t / 0.6f);
                return crack.P(rng.NextDouble() < density ? 1f : 0f) * 3.5f + Saw(60 + 60 * t / 0.6f, t) * 0.12f
                       + lp.LP(N()) * 0.25f + Sin(400 + 1200 * t / 0.6f, t) * 0.12f * (t / 0.6f);
            };
        });
        Add("kayo_wind", 0.8f, 2, () => t =>
        {
            // accelerating beeps: gaps shrink from ~200 ms to ~40 ms
            float phase = 7.5f * t + 9f * t * t;
            float frac = phase - MathF.Floor(phase);
            return frac < 0.3f ? (Sin(1800, t) + 0.3f * Sin(5400, t)) * 0.3f : 0f;
        });
        Add("skye_wind", 0.5f, 2, () =>
        {
            float ph = 0;
            return t =>
            {
                float carrier = 3000 - 1500 * t / 0.5f;
                ph += 2 * MathF.PI * carrier / SR;
                return MathF.Sin(ph + 4 * Sin(90, t)) * Env(t, 0.02f, 0.25f) * 0.35f;
            };
        });
        Add("yoru_wind", 0.6f, 2, () =>
        {
            var sw = new BPSweep(500, 3000, 0.6f, 3f);
            return t => sw.Next(t, N()) * Env(t, 0.5f, 0.1f) * 0.7f + (rng.NextDouble() < 0.0008 ? Sin(5000, t) * 0.4f : 0);
        });
        Add("vyse_wind", 0.5f, 2, () => t => Sin(600 + 1400 * t / 0.5f, t) * Sin(2300, t) * Env(t, 0.05f, 0.4f) * 0.35f);
        Add("gekko_wind", 0.8f, 2, () => t =>
        {
            float local = t % 0.16f;
            return local < 0.08f ? Sin(1200 + 1200 * local / 0.08f, t) * 0.3f * Env(local, 0.005f, 0.05f) : 0f;
        });
        Add("flashpop", 0.6f, 3, () =>
        {
            var hp = new OnePole(1000); var lp = new OnePole(300);
            return t => Sat((hp.HP(N()) * Env(t, 0.001f, 0.08f) * 0.6f + lp.LP(N()) * Env(t, 0.005f, 0.15f) * 0.7f
                + Sin(2500, t) * Env(t, 0.002f, 0.12f) * 0.25f) * 1.5f) * 0.7f;
        });
        Add("shatter", 0.4f, 2, () =>
        {
            var hp = new OnePole(3000);
            return t => (hp.HP(N()) * 0.6f + Sin(2900, t) * 0.2f + Sin(4600, t) * 0.15f + Sin(6300, t) * 0.1f) * Env(t, 0.001f, 0.2f) * 0.7f;
        });

    }

    public bool Has(string name) => streams.ContainsKey(name) || recorded.ContainsKey(name);

    public void Play(string name, float vol = 1f, float pitch = 1f)
    {
        if (Pick(name) is not { } s) return;
        var p = players2D[next2D = (next2D + 1) % players2D.Count];
        p.Stream = s;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Volume * vol));
        p.PitchScale = pitch;
        p.Play();
    }

    /// <summary>Positional sound with VALORANT-like flat attenuation (volume floor, fade near max range).</summary>
    public void PlayAt(string name, Vector3 pos, Vector3 listener, float maxRange = 40f, float floor = 0.45f, float vol = 1f, float pitchJitter = 0f)
    {
        if (Pick(name) is not { } s) return;
        float d = pos.DistanceTo(listener);
        if (d > maxRange) return;
        float k = Mathf.Clamp((d - 2f) / (maxRange - 2f), 0f, 1f);
        float g = (floor + (1 - floor) * (1 - k) * (1 - k)) * (k < 0.9f ? 1f : (1 - k) / 0.1f);
        var p = players3D[next3D = (next3D + 1) % players3D.Count];
        p.Stream = s;
        p.GlobalPosition = pos;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(1e-4f, Volume * vol * g));
        p.PitchScale = 1f + (pitchJitter > 0 ? ((float)Random.Shared.NextDouble() * 2 - 1) * pitchJitter : 0f);
        p.Play();
    }

    // ---------------- synthesis helpers ----------------
    static float Sin(float hz, float t) => MathF.Sin(2 * MathF.PI * hz * t);
    static float Saw(float hz, float t) { float p = hz * t; return 2 * (p - MathF.Floor(p)) - 1; }
    static float Sat(float x) => MathF.Tanh(x);
    static float Env(float t, float atk, float tau) => t < 0 ? 0 : t < atk ? t / atk : MathF.Exp(-(t - atk) / tau);

    sealed class OnePole
    {
        float y;
        readonly float a;
        public OnePole(float fc) => a = 1 - MathF.Exp(-2 * MathF.PI * fc / SR);
        public float LP(float x) => y += a * (x - y);
        public float HP(float x) => x - LP(x);
    }

    sealed class BP
    {
        float b0, b2, a1, a2, x1, x2, y1, y2;
        public BP(float fc, float q) => Set(fc, q);
        public void Set(float fc, float q)
        {
            float w = 2 * MathF.PI * fc / SR, al = MathF.Sin(w) / (2 * q), a0 = 1 + al;
            b0 = al / a0; b2 = -al / a0; a1 = -2 * MathF.Cos(w) / a0; a2 = (1 - al) / a0;
        }
        public float P(float x) { float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
    }

    /// <summary>Sine whose frequency glides exponentially from f0 to f1 over <c>dur</c> (phase-accumulated).</summary>
    sealed class Sweep(float f0, float f1, float dur)
    {
        float ph;
        public float Next(float t)
        {
            float k = Math.Clamp(t / dur, 0, 1);
            ph += 2 * MathF.PI * (f0 * MathF.Pow(f1 / f0, k)) / SR;
            return MathF.Sin(ph);
        }
    }

    /// <summary>Noise through a band-pass whose centre sweeps f0→f1.</summary>
    sealed class BPSweep(float f0, float f1, float dur, float q)
    {
        readonly BP bp = new(f0, q);
        int n;
        public float Next(float t, float noise)
        {
            if ((n++ & 63) == 0) bp.Set(f0 + (f1 - f0) * Math.Clamp(t / dur, 0, 1), q);
            return bp.P(noise);
        }
    }

    void Add(string name, float seconds, int voices, Func<Func<float, float>> make)
    {
        var f = make();
        int n = (int)(seconds * SR);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float t = i / SR;
            float fade = Math.Min(1f, (n - i) / 300f);
            float x = f(t);
            if (float.IsNaN(x)) x = 0;
            short v = (short)(Math.Clamp(x * fade, -1f, 1f) * short.MaxValue);
            data[2 * i] = (byte)(v & 0xff);
            data[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        streams[name] = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = (int)SR, Stereo = false, Data = data };
    }
}
