using Godot;

namespace ValTrainer.Audio;

/// <summary>What a foot lands on (picked from the map box under it, <see cref="Game.Fx.FootstepFx.SurfaceAt"/>).</summary>
public enum StepSurface { Concrete, Tile, Metal, Wood, Soft }

/// <summary>
/// ValTrainer's own running footsteps (1.21, footsteps-1.21.md): synthesized here, no samples, nothing from Riot.
/// <para><see cref="Render"/> makes one dry mono step: a sharp sole/heel transient (band-passed noise, a few ms), the slap of
/// the shoe (mid band), a low body thump (a falling sine plus low noise), a second lighter contact 25–45 ms later (the
/// forefoot rolling through), a short scuff and a tiny gear rattle; metal and tile add a ring, wood a hollow knock, soft
/// ground (snow, sand) a crunch. Every (surface, variant, foot) is a different seeded take.</para>
/// <para><see cref="Spatialize"/> places a dry step around the listener as a stereo buffer: level and inter-ear time
/// difference (left/right), a head shadow on the far ear and a duller sound from behind (front/back), a low-pass that closes
/// with distance plus a faint delayed room reflection that grows with it (near vs far), and a muffled through-the-wall
/// version. Godot has no HRTF; doing this per step in code keeps it exact, cheap (~10k samples) and testable headless.</para>
/// </summary>
public static class FootstepSynth
{
    public const int SR = 44100;

    // ---------------- dry steps ----------------

    sealed record Recipe(float ClickHz, float ClickQ, float ClickTau, float Click, float SlapHz, float SlapTau, float Slap,
        float Thump0, float Thump1, float ThumpTau, float Thump, float Low, float Scuff, float Gear, float Crunch,
        float[] Ring, float[] RingTau, float[] RingAmp, float Seconds);

    static readonly float[] None = Array.Empty<float>();
    // Layer balance (band-passed noise comes out much quieter than a sine, hence the big noise gains).
    const float ClickGain = 5f, SlapGain = 6f, ThumpGain = 0.4f, LowGain = 7f, ScuffGain = 4f;

    static Recipe Of(StepSurface s) => s switch
    {
        StepSurface.Tile => new(4800, 1.6f, 0.0025f, 1.0f, 1600, 0.008f, 0.45f, 140, 70, 0.022f, 0.55f, 0.30f, 0.08f, 0.06f, 0,
            new[] { 2300f, 3900f }, new[] { 0.014f, 0.010f }, new[] { 0.10f, 0.07f }, 0.2f),
        StepSurface.Metal => new(3800, 1.3f, 0.0040f, 0.8f, 900, 0.010f, 0.40f, 95, 60, 0.050f, 0.60f, 0.30f, 0.06f, 0.06f, 0,
            new[] { 430f, 1150f, 1980f, 3150f, 4400f }, new[] { 0.09f, 0.07f, 0.05f, 0.04f, 0.03f },
            new[] { 0.16f, 0.15f, 0.11f, 0.08f, 0.05f }, 0.34f),
        StepSurface.Wood => new(2200, 1.0f, 0.0040f, 0.65f, 700, 0.014f, 0.60f, 150, 85, 0.040f, 0.80f, 0.35f, 0.10f, 0.06f, 0,
            new[] { 185f, 410f }, new[] { 0.05f, 0.03f }, new[] { 0.20f, 0.08f }, 0.24f),
        StepSurface.Soft => new(2600, 1.0f, 0.0020f, 0.25f, 900, 0.020f, 0.35f, 100, 55, 0.030f, 0.55f, 0.35f, 0.05f, 0.05f, 0.5f,
            None, None, None, 0.22f),
        _ => new(3200, 1.1f, 0.0035f, 0.9f, 1100, 0.012f, 0.55f, 120, 55, 0.030f, 0.70f, 0.40f, 0.12f, 0.07f, 0,
            None, None, None, 0.2f),
    };

    /// <summary>One dry step (mono, <see cref="SR"/> Hz, RMS-normalized over its attack so every surface is about as loud).
    /// <paramref name="rightFoot"/> is a slightly lower, differently timed take: left and right feet don't sound identical.</summary>
    public static float[] Render(StepSurface surface, int variant, bool rightFoot)
    {
        var r = Of(surface);
        var rng = new Random(1009 + (int)surface * 97 + variant * 13 + (rightFoot ? 7 : 0));
        float J(float a) => 1f + a * (rng.NextSingle() * 2f - 1f);
        float N() => rng.NextSingle() * 2f - 1f;
        float foot = rightFoot ? 0.94f : 1f;

        var click = new BP(r.ClickHz * foot * J(0.08f), r.ClickQ);
        var click2 = new BP(r.ClickHz * 1.15f * foot * J(0.08f), r.ClickQ);
        var slap = new BP(r.SlapHz * J(0.1f), 0.9f);
        var slap2 = new BP(r.SlapHz * 1.2f * J(0.1f), 0.9f);
        var scuff = new BP(2400 * J(0.15f), 0.7f);
        var gear = new BP(4600 * J(0.1f), 1.5f);
        var crunch = new BP(3000 * J(0.15f), 1.2f);
        var low = new OnePole(380);
        float t2 = (rightFoot ? 0.033f : 0.027f) + 0.012f * rng.NextSingle(); // second contact (forefoot roll)
        float c2 = 0.4f + 0.2f * rng.NextSingle();
        float th0 = r.Thump0 * foot * J(0.06f), th1 = r.Thump1 * J(0.06f), thTau = r.ThumpTau * J(0.15f);
        float clickLvl = r.Click * J(0.12f), slapLvl = r.Slap * J(0.12f), gearLvl = r.Gear * J(0.4f);
        var ringF = r.Ring.Select(f => f * J(0.04f)).ToArray();
        var ringPh = r.Ring.Select(_ => rng.NextSingle() * 6.283f).ToArray();
        float jingle1 = 5200 * J(0.1f), jingle2 = 6900 * J(0.1f);

        int n = (int)(r.Seconds * SR);
        var x = new float[n];
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float s = click.P(N()) * Env(t, 0.0003f, r.ClickTau) * clickLvl * ClickGain
                    + slap.P(N()) * Env(t, 0.0008f, r.SlapTau) * slapLvl * SlapGain;
            float k = Math.Clamp(t / 0.04f, 0f, 1f);
            ph += 2 * MathF.PI * (th0 * MathF.Pow(th1 / th0, k)) / SR;
            s += MathF.Sin(ph) * Env(t, 0.001f, thTau) * r.Thump * ThumpGain + low.LP(N()) * Env(t, 0.001f, 0.02f) * r.Low * LowGain;
            float u = t - t2;
            s += (click2.P(N()) * Env(u, 0.0004f, r.ClickTau * 0.8f) * clickLvl * ClickGain
                + slap2.P(N()) * Env(u, 0.0008f, r.SlapTau * 0.7f) * slapLvl * SlapGain) * c2;
            s += scuff.P(N()) * Env(t - 0.004f, 0.005f, 0.02f) * r.Scuff * ScuffGain;
            s += (gear.P(N()) * 1.5f + 0.3f * MathF.Sin(2 * MathF.PI * jingle1 * t) + 0.2f * MathF.Sin(2 * MathF.PI * jingle2 * t))
                 * Env(t - 0.012f, 0.006f, 0.03f) * gearLvl;
            if (r.Crunch > 0)
            {
                float dens = 0.02f * Env(t, 0.002f, 0.04f);
                s += crunch.P(rng.NextSingle() < dens ? N() * 6f : 0f) * r.Crunch;
            }
            for (int p = 0; p < ringF.Length; p++)
                s += MathF.Sin(2 * MathF.PI * ringF[p] * t + ringPh[p]) * Env(t, 0.0005f, r.RingTau[p]) * r.RingAmp[p]
                     + MathF.Sin(2 * MathF.PI * ringF[p] * t + ringPh[p]) * Env(u, 0.0005f, r.RingTau[p]) * r.RingAmp[p] * c2;
            x[i] = MathF.Tanh(1.3f * s);
        }
        // Normalize the attack (first 60 ms) to a fixed RMS, then make sure nothing clips; short fade-out at the end.
        int a = Math.Min(n, (int)(0.06f * SR));
        double sum = 0;
        for (int i = 0; i < a; i++) sum += x[i] * x[i];
        float rms = (float)Math.Sqrt(sum / Math.Max(1, a));
        float g = rms > 1e-6f ? 0.2f / rms : 1f;
        float peak = 0;
        for (int i = 0; i < n; i++) peak = MathF.Max(peak, MathF.Abs(x[i] * g));
        if (peak > 0.95f) g *= 0.95f / peak;
        int fade = Math.Min(n, 400);
        for (int i = 0; i < n; i++) x[i] *= g * Math.Min(1f, (n - i) / (float)fade);
        return x;
    }

    // ---------------- placing a step around the listener ----------------

    /// <summary>How one step is heard: per-ear level, delay and brightness, the distance low-pass and the room reflection.</summary>
    public readonly record struct Cue(float Left, float Right, int DelayLeft, int DelayRight, float HfLeft, float HfRight,
        float CutoffHz, float Reflection, int ReflectionDelay);

    /// <summary>The audible range of a running step (m): VALORANT's commonly measured 40–50 m, the low end.</summary>
    public const float Range = 40f;
    /// <summary>Loudness lost per doubling of distance beyond 2 m (dB). Real air is ~6; VALORANT keeps it flat so no step is
    /// missed, so this is gentle: 4 m −2.5 dB, 8 m −5, 16 m −7.5, 32 m −10, then a fade to silence over the last 4 m.</summary>
    public const float DbPerDoubling = 2.5f;

    /// <summary>Linear gain of a running step at distance <paramref name="d"/> (m); 0 beyond <see cref="Range"/>.</summary>
    public static float Gain(float d)
    {
        if (d > Range) return 0f;
        float db = -DbPerDoubling * MathF.Log2(MathF.Max(d, 2f) / 2f);
        float edge = Math.Clamp((Range - d) / 4f, 0f, 1f);
        return Mathf.DbToLinear(db) * edge;
    }

    /// <summary>The distance low-pass (Hz): 14 kHz up close, ~2.6 kHz at the edge of the range (log scale), so far steps
    /// lose their click and turn into a dull thud; through a wall it closes much further.</summary>
    public static float Cutoff(float d, bool occluded)
    {
        float k = Math.Clamp((d - 2f) / (Range - 2f), 0f, 1f);
        float fc = 14000f * MathF.Pow(2600f / 14000f, MathF.Sqrt(k));
        return occluded ? MathF.Max(650f, fc * 0.22f) : fc;
    }

    /// <summary>The cue of a step at <paramref name="source"/> heard from <paramref name="eye"/> looking along
    /// <paramref name="yawDeg"/> (the game's yaw: 0 = −Z, + = right). Pitch is ignored (VALORANT pans on the horizontal plane).</summary>
    public static Cue CueFor(Vector3 eye, float yawDeg, Vector3 source, bool occluded)
    {
        var rel = source - eye;
        float d = rel.Length();
        float y = Mathf.DegToRad(yawDeg);
        var flat = new Vector2(rel.X, rel.Z);
        float lat = 0f, front = 1f;
        if (flat.Length() > 0.3f)
        {
            flat = flat.Normalized();
            lat = flat.X * MathF.Cos(y) + flat.Y * MathF.Sin(y);    // · right (cos y, sin y)
            front = flat.X * MathF.Sin(y) - flat.Y * MathF.Cos(y);  // · forward (sin y, −cos y)
            // Right under / over you there's little left-right to hear.
            float horiz = new Vector2(rel.X, rel.Z).Length();
            float spread = Math.Clamp(horiz / MathF.Max(0.5f, MathF.Abs(rel.Y) * 1.5f), 0f, 1f);
            lat *= spread;
        }
        float p = 0.9f * lat;
        float th = (p + 1f) * MathF.PI / 4f;
        float gl = 0.25f + 0.75f * MathF.Cos(th), gr = 0.25f + 0.75f * MathF.Sin(th);
        int itd = (int)MathF.Round(MathF.Abs(lat) * 0.00063f * SR); // up to ~0.63 ms on the far ear
        float shadow = 1f - 0.6f * MathF.Abs(lat);                  // the head shades the far ear's highs
        float back = front < 0 ? 1f - 0.5f * -front : 1f;           // behind you: duller
        float gBack = front < 0 ? 1f - 0.12f * -front : 1f;
        float k = Math.Clamp((d - 2f) / (Range - 2f), 0f, 1f);
        float refl = 0.05f + 0.22f * k + (occluded ? 0.1f : 0f);
        int reflDelay = (int)(MathF.Min(0.03f, 0.006f + 0.0005f * d) * SR);
        return new Cue(gl * gBack, gr * gBack, lat < 0 ? 0 : itd, lat > 0 ? 0 : itd,
            back * (lat < 0 ? 1f : shadow), back * (lat > 0 ? 1f : shadow), Cutoff(d, occluded), refl, reflDelay);
    }

    // Scratch buffers reused by every step (main thread only), so a busy Deathmatch doesn't churn the GC.
    static float[] lowBand = Array.Empty<float>(), highBand = Array.Empty<float>(), room = Array.Empty<float>(), mix = Array.Empty<float>();

    /// <summary>A dry step placed by <paramref name="c"/>, times <paramref name="gain"/>: interleaved stereo (L, R).</summary>
    public static float[] Spatialize(float[] dry, in Cue c, float gain)
    {
        int n = Place(dry, c, gain);
        var o = new float[2 * n];
        Array.Copy(mix, o, 2 * n);
        return o;
    }

    /// <summary><see cref="Spatialize"/> straight to 16-bit stereo PCM (what the game plays).</summary>
    public static byte[] SpatializePcm(float[] dry, in Cue c, float gain)
    {
        int n = Place(dry, c, gain);
        return Pcm16(mix, 2 * n);
    }

    /// <summary>Renders into <see cref="mix"/>; returns the frame count.</summary>
    static int Place(float[] dry, in Cue c, float gain)
    {
        int extra = Math.Max(c.DelayLeft, c.DelayRight) + c.ReflectionDelay + 1;
        int n = dry.Length + extra;
        if (lowBand.Length < n) { lowBand = new float[n]; highBand = new float[n]; room = new float[n]; }
        if (mix.Length < 2 * n) mix = new float[2 * n];
        var lp1 = new OnePole(c.CutoffHz); var lp2 = new OnePole(c.CutoffHz); var split = new OnePole(2000f);
        var roomLp = new OnePole(MathF.Min(c.CutoffHz, 1800f));
        for (int i = 0; i < n; i++)
        {
            float s = lp2.LP(lp1.LP(i < dry.Length ? dry[i] : 0f));
            float lo = split.LP(s);
            lowBand[i] = lo;
            highBand[i] = s - lo;
            room[i] = roomLp.LP(s);
        }
        for (int i = 0; i < n; i++)
        {
            int il = i - c.DelayLeft, ir = i - c.DelayRight, ie = i - c.ReflectionDelay;
            float l = il >= 0 ? lowBand[il] + c.HfLeft * highBand[il] : 0f;
            float r = ir >= 0 ? lowBand[ir] + c.HfRight * highBand[ir] : 0f;
            float e = ie >= 0 ? room[ie] * c.Reflection : 0f;
            mix[2 * i] = Math.Clamp((l * c.Left + e * 0.8f) * gain, -1f, 1f);
            mix[2 * i + 1] = Math.Clamp((r * c.Right + e * 0.8f) * gain, -1f, 1f);
        }
        return n;
    }

    /// <summary>16-bit little-endian PCM of <paramref name="samples"/> (any channel layout).</summary>
    public static byte[] Pcm16(float[] samples, int count = -1)
    {
        if (count < 0) count = samples.Length;
        var data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short v = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[2 * i] = (byte)(v & 0xff);
            data[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        return data;
    }

    /// <summary>Dev "--dump-footsteps DIR": one running sequence per surface (8 steps, alternating feet), and a concrete
    /// enemy circling you at 6 m, then walking away 2 → 40 m, then the same through a wall, as stereo WAVs to listen to.
    /// Returns the number of files not written.</summary>
    public static int Dump(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        int bad = 0;
        void Save(string name, List<float> stereo)
        {
            var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SR, Stereo = true, Data = Pcm16(stereo.ToArray()) };
            var err = w.SaveToWav(System.IO.Path.Combine(dir, name + ".wav"));
            if (err != Error.Ok) { bad++; GD.PrintErr($"[dump-footsteps] {name}: {err}"); }
        }
        // Mixes steps every 0.33 s into one buffer.
        List<float> Seq(int count, Func<int, (float[] Dry, Cue Cue, float Gain)> step)
        {
            int hop = (int)(0.33f * SR);
            var buf = new List<float>();
            for (int i = 0; i < count; i++)
            {
                var (dry, cue, gain) = step(i);
                var st = Spatialize(dry, cue, gain);
                int at = 2 * i * hop;
                while (buf.Count < at + st.Length) buf.Add(0f);
                for (int k = 0; k < st.Length; k++) buf[at + k] += st[k];
            }
            return buf;
        }
        var eye = new Vector3(0, 1.6f, 0);
        foreach (var surf in Enum.GetValues<StepSurface>())
            Save("surface_" + surf.ToString().ToLowerInvariant(),
                Seq(8, i => (Render(surf, i / 2 % 4, (i & 1) == 1), CueFor(eye, 0, new Vector3(0, 0, -4), false), Gain(4))));
        var takes = Enumerable.Range(0, 8).Select(i => Render(StepSurface.Concrete, i / 2, (i & 1) == 1)).ToArray();
        Save("circle_6m", Seq(24, i =>
        {
            float a = i / 24f * MathF.Tau; // front → right → behind → left
            var src = new Vector3(6 * MathF.Sin(a), 0, -6 * MathF.Cos(a));
            return (takes[i % 8], CueFor(eye, 0, src, false), Gain(6));
        }));
        foreach (bool occ in new[] { false, true })
            Save(occ ? "away_2_to_40m_wall" : "away_2_to_40m", Seq(30, i =>
            {
                float d = 2 + 38 * i / 29f;
                var src = new Vector3(0.3f * d, 0, -d);
                return (takes[i % 8], CueFor(eye, 0, src, occ), Gain(src.DistanceTo(eye)) * (occ ? 0.7f : 1f));
            }));
        GD.Print($"[dump-footsteps] wrote {Enum.GetValues<StepSurface>().Length + 3 - bad} WAVs to {dir}");
        return bad;
    }

    // ---------------- helpers ----------------

    static float Env(float t, float atk, float tau) => t < 0 ? 0 : t < atk ? t / atk : MathF.Exp(-(t - atk) / tau);

    /// <summary>Energy of one channel (0 = left, 1 = right) of an interleaved stereo buffer, optionally of its highs only.</summary>
    public static float Energy(float[] stereo, int ch, bool highsOnly = false)
    {
        double e = 0;
        float prev = 0;
        for (int i = ch; i < stereo.Length; i += 2)
        {
            float v = highsOnly ? stereo[i] - prev : stereo[i]; // first difference ≈ a high-pass
            prev = stereo[i];
            e += v * v;
        }
        return (float)e;
    }

    sealed class OnePole
    {
        float y;
        readonly float a;
        public OnePole(float fc) => a = 1 - MathF.Exp(-2 * MathF.PI * MathF.Min(fc, SR * 0.45f) / SR);
        public float LP(float x) => y += a * (x - y);
    }

    sealed class BP
    {
        readonly float b0, b2, a1, a2;
        float x1, x2, y1, y2;
        public BP(float fc, float q)
        {
            float w = 2 * MathF.PI * fc / SR, al = MathF.Sin(w) / (2 * q), a0 = 1 + al;
            b0 = al / a0; b2 = -al / a0; a1 = -2 * MathF.Cos(w) / a0; a2 = (1 - al) / a0;
        }
        public float P(float x) { float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
    }
}
