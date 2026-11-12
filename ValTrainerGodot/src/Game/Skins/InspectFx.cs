using Godot;
using ValTrainer.Core;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// What the skin flourishes ask of the gun's materials this frame (<see cref="Viewmodel"/> resets it every frame, the
/// flourishes add to it, the viewmodel applies it once). Glow multiplies each skin material's own <c>glow_energy</c> (so it
/// only brightens where the pattern already glows); Emit adds a flat emission to every gun surface (frost, amber);
/// MagEmit lights the reload's spare magazine (Foundry's red-hot magazine). The four "skin_fx" values go to the shader
/// uniforms <c>inspect_k</c>, <c>inspect_t</c>, <c>reload_k</c>, <c>reload_p</c> when the skin shader declares them (see
/// <see cref="Viewmodel"/>): the visuals agent can key pattern motion on them (Orrery's dial, Oscilloscope's needle).
/// </summary>
public struct SkinMod
{
    public float Glow;
    public Color Emit;
    public Color MagEmit;
    /// <summary>Inspect flourish envelope 0..1 and seconds since it started (9 = none).</summary>
    public float InspectK, InspectT;
    /// <summary>Special reload envelope 0..1 and the reload progress 0..1 (-1 = none).</summary>
    public float ReloadK, ReloadP;

    public static SkinMod None => new() { Emit = Colors.Black, MagEmit = Colors.Black, InspectT = 9f, ReloadP = -1f };
}

/// <summary>Shared plumbing of the viewmodel skin flourishes: the particle pool, a small light on the gun (it lights only the
/// viewmodel), the effect palette, and surface points of the gun model to put particles on.</summary>
public abstract class VmSkinFx
{
    protected readonly Viewmodel Vm;
    protected readonly SkinState State;
    protected readonly string Id;
    protected readonly VmParticles Parts;
    protected readonly OmniLight3D Light;
    protected readonly FxPalette Pal;
    protected readonly Aabb B;
    /// <summary>Gun-space x of the side the camera sees (-1 right-handed, +1 left-handed).</summary>
    protected readonly float Side;
    protected readonly bool Lite;

    protected VmSkinFx(Viewmodel vm, SkinState state, string id, int capacity)
    {
        Vm = vm; State = state; Id = id;
        Pal = state.Palette;
        B = vm.GunBounds;
        Side = vm.CameraSide;
        Lite = (Main.I?.Settings?.Quality ?? 1) <= 0;
        Parts = VmParticles.Create(vm, () => vm.GunToViewmodel, () => vm.Focal, Lite ? capacity / 2 : capacity);
        Light = new OmniLight3D
        {
            Name = "SkinFxLight", LightEnergy = 0f, OmniRange = 0.45f, OmniAttenuation = 1.6f, ShadowEnabled = false,
            LightCullMask = Viewmodel.VisualLayer, Visible = false, LightSpecular = 0.4f,
        };
        vm.GunNode.AddChild(Light);
    }

    protected float R(float a, float b) => Parts.R(a, b);
    protected Color Jitter(Color c, float k = 0.12f) => c.Lerp(Colors.White, R(0f, k));

    /// <summary>Frees the nodes (the viewmodel was rebuilt or re-skinned).</summary>
    public void Free()
    {
        if (GodotObject.IsInstanceValid(Parts)) Parts.QueueFree();
        if (GodotObject.IsInstanceValid(Light)) Light.QueueFree();
    }

    /// <summary>Light at a gun-space point (0 energy hides it).</summary>
    protected void SetLight(Vector3 at, Color c, float energy)
    {
        bool on = energy > 0.002f;
        if (Light.Visible != on) Light.Visible = on;
        if (!on) return;
        Light.Position = at;
        Light.LightColor = c;
        Light.LightEnergy = Vm.Modelled ? energy * 0.35f : energy; // modelled skins: the textures and glow map carry the colour
    }

    protected Vector3 Centre => B.GetCenter();

    /// <summary>A random point on the gun's surface near the length fraction <paramref name="zf"/> (0 = muzzle, 1 = stock),
    /// optionally only where the normal passes <paramref name="where"/>; with its outward normal.</summary>
    protected (Vector3 P, Vector3 N) Surf(float zf, float spread = 0.06f, Func<Vector3, bool>? where = null)
    {
        var pts = Vm.SurfacePoints;
        if (pts.Count == 0) return (Centre, Vector3.Up);
        float z = Mathf.Lerp(B.Position.Z, B.End.Z, Mathf.Clamp(zf, 0f, 1f));
        float half = Mathf.Max(0.004f, spread * B.Size.Z);
        int lo = Lower(pts, z - half), hi = Lower(pts, z + half);
        if (hi <= lo) { lo = Math.Clamp(lo - 1, 0, pts.Count - 1); hi = lo + 1; }
        for (int tries = 0; tries < 8; tries++)
        {
            var c = pts[Math.Clamp(lo + (int)R(0f, hi - lo), 0, pts.Count - 1)];
            if (where == null || where(c.N) || tries == 7) return c;
        }
        return pts[lo];
    }

    static int Lower(IReadOnlyList<(Vector3 P, Vector3 N)> pts, float z)
    {
        int a = 0, b = pts.Count;
        while (a < b) { int m = (a + b) / 2; if (pts[m].P.Z < z) a = m + 1; else b = m; }
        return a;
    }

    /// <summary>Gun space → the viewmodel's (camera) space, for free particles.</summary>
    protected Vector3 ToFree(Vector3 gunPos) => Vm.GunToViewmodel * gunPos;
    protected Vector3 ToFreeDir(Vector3 gunDir) => Vm.GunToViewmodel.Basis * gunDir;
}

/// <summary>
/// The skin inspect flourish (skins3-contracts §2, <see cref="SkinState.Inspect"/>: Signature lines and Apex L4+; recipe id =
/// line id): particles and light on the gun plus a glow / emission modulation of its materials while the generic inspect
/// motion (<see cref="VmInspect"/>) plays, and <see cref="SkinAudio.PlayInspect"/> at the start. Everything follows the
/// inspect's blend weight, so a cancelled inspect (fire, ADS, reload …) takes its flourish with it within ~0.1 s. Our own
/// procedural effects; no VALORANT animation or look-alike. Cosmetic: never recorded anywhere.
/// </summary>
public sealed class InspectFx : VmSkinFx
{
    /// <summary>The recipes this file implements (other ids play the plain inspect).</summary>
    public static readonly string[] Recipes =
        { "glasswork", "thunderhead", "basalt", "oscilloscope", "rally", "frostbite", "hive", "foundry", "aurora", "orrery" };

    float t = -1f, dur = 3f, acc, acc2, lastT;
    int bolts;
    readonly List<VmParticles.P> rings = new();
    readonly float[] ringTilt = new float[3];

    InspectFx(Viewmodel vm, SkinState s, string id) : base(vm, s, id, 200) { }

    /// <summary>The flourish of a state (null when it has no inspect recipe this file knows).</summary>
    public static InspectFx? For(Viewmodel vm, SkinState? s) =>
        s is { Inspect: { } id } st && Recipes.Contains(id) ? new InspectFx(vm, st, id) : null;

    public bool Playing => t >= 0f;

    /// <summary>The inspect started: <paramref name="duration"/> is its length (the flourish is timed to it).</summary>
    public void Start(float duration)
    {
        t = 0f; lastT = 0f; dur = duration; acc = acc2 = 0f; bolts = 0;
        Parts.Clear();
        rings.Clear();
        SkinAudio.PlayInspect(State, Vm.Held);
    }

    /// <summary>The inspect ended or was cancelled: no new particles; the rest fades.</summary>
    public void Stop()
    {
        if (t < 0f) return;
        t = -1f;
        Parts.FadeAll(0.25f);
    }

    /// <summary>Every frame. <paramref name="w"/> = the inspect's blend weight (1 playing, falling to 0 when cancelled).</summary>
    public void Tick(float dt, float w, ref SkinMod mod)
    {
        Parts.Weight = w;
        if (t >= 0f)
        {
            lastT = t;
            t += dt;
            float k = dur / 3.2f; // recipes are timed on the rifle's 3.2 s and scaled to the gun's inspect
            float env = Mathf.SmoothStep(0f, 0.35f, t) * (1f - Mathf.SmoothStep(dur - 0.5f, dur, t));
            mod.InspectK = Mathf.Max(mod.InspectK, env * w);
            mod.InspectT = Mathf.Min(mod.InspectT, t);
            switch (Id)
            {
                case "glasswork": Glasswork(dt, k, w, env, ref mod); break;
                case "thunderhead": Thunderhead(dt, k, w, ref mod); break;
                case "basalt": Basalt(dt, k, w, ref mod); break;
                case "oscilloscope": Oscilloscope(dt, k, w, env, ref mod); break;
                case "rally": Rally(dt, k, w, ref mod); break;
                case "frostbite": Frostbite(dt, k, w, env, ref mod); break;
                case "hive": Hive(dt, k, w, env, ref mod); break;
                case "foundry": Foundry(dt, k, w, env, ref mod); break;
                case "aurora": Aurora(dt, k, w, env, ref mod); break;
                case "orrery": Orrery(dt, k, w, env, ref mod); break;
            }
        }
        else SetLight(Centre, Colors.White, 0f);
        if (Parts.Count > 0 || t >= 0f) Parts.Tick(dt);
    }

    /// <summary>Whether <paramref name="at"/> (s, rifle timing × k) was crossed this frame.</summary>
    bool At(float at, float k) => lastT < at * k && t >= at * k;

    /// <summary>A spawn budget: how many particles at <paramref name="rate"/> per second this frame.</summary>
    int Rate(ref float a, float rate, float dt)
    {
        if (Lite) rate *= 0.5f;
        a += rate * dt;
        int n = (int)a;
        a -= n;
        return n;
    }

    static float Window(float t, float a, float b) => t >= a && t <= b ? Mathf.Sin(Mathf.Pi * (t - a) / (b - a)) : 0f;

    // ---------------- Signature ----------------

    /// <summary>Light refracting across the panels: two bands of jewel-coloured glints sweep muzzle → stock, a coloured light
    /// rides the band, the glass glows up under it.</summary>
    void Glasswork(float dt, float k, float w, float env, ref SkinMod mod)
    {
        Color[] jewels = { Pal.Main, Pal.Accent, Pal.Alt, new("#D8344E"), new("#3A6CF0"), new("#2FC27A") };
        float band = 0f, zf = 0f;
        foreach (var (a, b) in new[] { (0.35f, 1.35f), (1.75f, 2.65f) })
        {
            if (t < a * k || t > b * k) continue;
            zf = (t - a * k) / ((b - a) * k);
            band = Mathf.Sin(Mathf.Pi * zf);
        }
        for (int i = Rate(ref acc, 55f * band, dt); i > 0; i--)
        {
            var (p, n) = Surf(zf, 0.05f);
            Parts.Spawn(new() { Pos = p + n * 0.003f, Attached = true, Shape = VmParticles.Shape.Glint, Size = R(0.012f, 0.024f),
                Life = R(0.3f, 0.45f), Col = jewels[(int)R(0f, jewels.Length - 0.01f)], Spin = R(0f, Mathf.Tau), FadeIn = 0.25f, FadeOut = 0.6f, Alpha = 0.9f });
        }
        var at = new Vector3(Side * B.Size.X * 0.6f, Centre.Y, Mathf.Lerp(B.Position.Z, B.End.Z, zf));
        SetLight(at, jewels[(int)(zf * 5.99f)], 0.55f * band * w);
        mod.Glow += 1.6f * band * w + 0.3f * env * w;
    }

    /// <summary>Lightning flickers along the veins: five forked bolts (two double strikes) of streaks across the gun, a
    /// blue-white flash and glow spike with each, static crackle in between.</summary>
    void Thunderhead(float dt, float k, float w, ref SkinMod mod)
    {
        float[] strikes = { 0.35f, 0.43f, 1.30f, 2.02f, 2.10f };
        float flash = 0f;
        foreach (var s in strikes)
        {
            if (At(s, k)) Bolt();
            if (t >= s * k) flash += Mathf.Exp(-(t - s * k) / 0.07f);
        }
        for (int i = Rate(ref acc, 9f, dt); i > 0; i--)
        {
            var (p, n) = Surf(R(0f, 1f), 0.1f);
            Parts.Spawn(new() { Pos = p + n * 0.002f, Attached = true, Shape = VmParticles.Shape.Glint, Size = R(0.005f, 0.009f), Life = 0.09f,
                Col = Colors.White, Spin = R(0f, 3f), FadeIn = 0.1f, FadeOut = 0.5f, Alpha = 0.8f });
        }
        flash = Mathf.Min(flash, 1.5f);
        SetLight(Centre + new Vector3(Side * 0.05f, 0.04f, 0f), Pal.Main.Lerp(Colors.White, 0.5f), 1.6f * flash * w);
        mod.Glow += 4f * flash * w;
    }

    void Bolt()
    {
        bolts++;
        bool fwd = bolts % 2 == 1;
        float a = R(0f, 0.3f), b = R(0.65f, 1f);
        if (!fwd) (a, b) = (b, a);
        int n = Lite ? 5 : 8;
        Vector3 prev = Vector3.Zero;
        for (int i = 0; i <= n; i++)
        {
            var (p, nn) = Surf(Mathf.Lerp(a, b, i / (float)n), 0.03f, v => v.X * Side > 0.2f || v.Y > 0.3f);
            p += nn * 0.003f;
            if (i > 0)
            {
                var d = p - prev;
                Parts.Spawn(new() { Pos = (p + prev) * 0.5f, Attached = true, Shape = VmParticles.Shape.Streak, Dir = d, Length = d.Length() * 1.15f,
                    Size = 0.0035f, Soft = 0.5f, Life = 0.16f, Col = Colors.White, Col1 = Pal.Main, Flicker = 0.7f, FadeIn = 0.02f, FadeOut = 0.7f, Alpha = 1.2f });
                if (R(0f, 1f) < 0.35f) // a fork
                {
                    var f = d.Rotated(Vector3.Right, R(-0.9f, 0.9f)) * R(0.4f, 0.7f);
                    Parts.Spawn(new() { Pos = prev + f * 0.5f, Attached = true, Shape = VmParticles.Shape.Streak, Dir = f, Length = f.Length(),
                        Size = 0.0022f, Soft = 0.5f, Life = 0.12f, Col = Colors.White, Col1 = Pal.Main, Flicker = 0.8f, FadeIn = 0.02f, FadeOut = 0.7f });
                }
            }
            prev = p;
        }
        Parts.Spawn(new() { Pos = prev, Attached = true, Shape = VmParticles.Shape.Dot, Size = 0.035f, Soft = 1f, Life = 0.15f, Col = Pal.Main, FadeIn = 0.02f, FadeOut = 0.8f });
    }

    /// <summary>Magma seams pulse: three slow heartbeats of the dim red seams, a red under-light and embers that rise off
    /// the gun at each beat.</summary>
    void Basalt(float dt, float k, float w, ref SkinMod mod)
    {
        float pulse = 0f;
        foreach (var s in new[] { 0.4f, 1.3f, 2.2f }) pulse += Mathf.Exp(-Mathf.Pow((t - s * k) / 0.2f, 2f));
        for (int i = Rate(ref acc, 16f * pulse, dt); i > 0; i--)
        {
            var (p, n) = Surf(R(0.05f, 0.95f), 0.1f, v => v.Y > 0f);
            Parts.Spawn(new() { Pos = ToFree(p + n * 0.003f), Vel = new Vector3(R(-0.02f, 0.02f), R(0.05f, 0.12f), R(-0.02f, 0.02f)), Grav = -0.02f, Drag = 1.2f,
                Shape = VmParticles.Shape.Dot, Size = R(0.004f, 0.007f), Soft = 0.6f, Life = R(0.7f, 1.1f), Col = Pal.Accent, Col1 = Pal.Main.Darkened(0.5f),
                Flicker = 0.5f, FadeIn = 0.1f, FadeOut = 0.6f });
        }
        SetLight(new Vector3(Side * 0.03f, B.Position.Y, Centre.Z), Pal.Main, 0.7f * pulse * w);
        mod.Glow += 2.6f * pulse * w;
    }

    /// <summary>The waveform spikes: a phosphor trace runs along the camera side again and again with spikes in it (the glow
    /// spikes with them), on a faint green light. (The dial needle swing needs the shader: it reads inspect_k / inspect_t.)</summary>
    void Oscilloscope(float dt, float k, float w, float env, ref SkinMod mod)
    {
        const float period = 0.8f;
        float spike = 0f;
        if (t > 0.2f * k && t < dur - 0.45f)
        {
            float ph = ((t - 0.2f * k) / period) % 1f;
            float zf = ph;
            float spk = Mathf.Exp(-Mathf.Pow(((ph * 3.7f) % 1f - 0.5f) / 0.05f, 2f)); // three spikes per sweep
            spike = spk;
            float y = 0.006f * Mathf.Sin(ph * 42f + t * 3f) + 0.016f * spk * (((int)(ph * 3.7f) % 2 == 0) ? 1f : -1f);
            for (int i = Rate(ref acc, 140f, dt); i > 0; i--)
            {
                var (p, n) = Surf(zf + R(-0.004f, 0.004f), 0.02f, v => v.X * Side > 0.5f);
                Parts.Spawn(new() { Pos = p + n * 0.0025f + Vector3.Up * y, Attached = true, Shape = VmParticles.Shape.Dot, Size = 0.007f, Soft = 0.5f,
                    Life = 0.5f, Col = Pal.Main.Lerp(Colors.White, 0.3f * spk), Col1 = Pal.Main.Darkened(0.4f), FadeIn = 0.02f, FadeOut = 0.9f, Alpha = 0.9f });
            }
        }
        SetLight(Centre + new Vector3(Side * 0.05f, 0f, 0f), Pal.Main, (0.25f + 0.5f * spike) * env * w);
        mod.Glow += (0.6f * env + 2.5f * spike) * w;
    }

    /// <summary>A quick checkered shimmer: two fast passes of white and livery-colour glints in a checkerboard, muzzle → stock
    /// (the engine blip is the sound's).</summary>
    void Rally(float dt, float k, float w, ref SkinMod mod)
    {
        float band = 0f, zf = 0f;
        foreach (var (a, b) in new[] { (0.45f, 1.05f), (1.85f, 2.35f) })
        {
            if (t < a * k || t > b * k) continue;
            zf = (t - a * k) / ((b - a) * k);
            band = 1f;
        }
        for (int i = Rate(ref acc, 70f * band, dt); i > 0; i--)
        {
            var (p, n) = Surf(zf, 0.035f);
            const float cell = 0.022f;
            bool white = ((int)Mathf.Floor(p.Z / cell) + (int)Mathf.Floor(p.Y / cell)) % 2 == 0;
            Parts.Spawn(new() { Pos = p + n * 0.002f, Attached = true, Shape = white ? VmParticles.Shape.Glint : VmParticles.Shape.Dot, Size = white ? 0.016f : 0.01f,
                Soft = 0.5f, Life = 0.22f, Col = white ? Colors.White : Pal.Main, Spin = Mathf.Pi / 4f, FadeIn = 0.15f, FadeOut = 0.6f, Alpha = 0.9f });
        }
        SetLight(new Vector3(Side * 0.06f, Centre.Y, Mathf.Lerp(B.Position.Z, B.End.Z, zf)), Colors.White, 0.35f * band * w);
        mod.Glow += 1f * band * w;
    }

    /// <summary>Frost creeps over the gun: crystals grow in from the muzzle toward the stock and twinkle, the metal takes a cold
    /// sheen, a little cold mist falls off; they melt away when the inspect ends.</summary>
    void Frostbite(float dt, float k, float w, float env, ref SkinMod mod)
    {
        float front = Mathf.SmoothStep(0.15f * k, 1.9f * k, t);
        if (Parts.Count < (Lite ? 60 : 130))
            for (int i = Rate(ref acc, 70f, dt); i > 0; i--)
            {
                var (p, n) = Surf(R(0f, front), 0.03f);
                float life = Mathf.Max(0.4f, dur - t + 0.15f);
                Parts.Spawn(new() { Pos = p + n * 0.0025f, Attached = true, Shape = VmParticles.Shape.Flake, Size = R(0.007f, 0.015f), Spin = R(0f, Mathf.Tau),
                    Life = life, Col = Colors.White, Col1 = Pal.Main, Flicker = 0.3f, FadeIn = Mathf.Min(0.5f, 0.3f / life), FadeOut = Mathf.Min(0.5f, 0.35f / life), Alpha = 0.75f });
            }
        for (int i = Rate(ref acc2, 7f * env, dt); i > 0; i--)
        {
            var (p, _) = Surf(R(0f, front), 0.1f, v => v.Y < -0.2f);
            Parts.Spawn(new() { Pos = ToFree(p), Vel = new Vector3(0f, -0.02f, 0f), Grav = 0.02f, Drag = 0.5f, Shape = VmParticles.Shape.Dot, Size = R(0.03f, 0.05f),
                Soft = 1.4f, Life = 1.2f, Col = Pal.Main, Alpha = 0.12f, FadeIn = 0.3f, FadeOut = 0.6f });
        }
        SetLight(Centre + new Vector3(Side * 0.06f, 0.03f, 0f), Pal.Main, 0.35f * front * env * w);
        mod.Emit += Pal.Main * (0.16f * front * env * w);
        mod.Glow += 0.8f * front * env * w;
    }

    /// <summary>The amber glow brightens from within and honey drips: beads swell on the underside, let go and fall.</summary>
    void Hive(float dt, float k, float w, float env, ref SkinMod mod)
    {
        foreach (var s in new[] { 0.45f, 0.95f, 1.5f, 2.0f })
            if (At(s, k))
            {
                var (p, n) = Surf(R(0.15f, 0.85f), 0.15f, v => v.Y < -0.5f);
                Parts.Spawn(new() { Pos = p + n * 0.002f, Attached = true, Shape = VmParticles.Shape.Dot, Size = 0.004f, Size1 = 0.011f, Soft = 0.45f, Life = 1.3f,
                    Detach = 0.62f, FreeShape = VmParticles.Shape.Streak, Length = 0.016f, Grav = 0.9f, Drag = 0.6f, Col = Pal.Main, Col1 = Pal.Main.Darkened(0.3f),
                    FadeIn = 0.15f, FadeOut = 0.25f, Alpha = 0.85f });
            }
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.1f);
        SetLight(new Vector3(Side * 0.03f, Centre.Y, Centre.Z), Pal.Main, (0.45f + 0.15f * pulse) * env * w);
        mod.Glow += (2f + 0.5f * pulse) * env * w;
        mod.Emit += Pal.Main * (0.06f * env * w);
    }

    // ---------------- Apex ----------------

    /// <summary>Embers flare: the cracks flare up and flicker, a burst of sparks and a steady stream of embers rise off the
    /// casting under a flickering forge light.</summary>
    void Foundry(float dt, float k, float w, float env, ref SkinMod mod)
    {
        int burst = At(0.3f, k) ? (Lite ? 10 : 20) : 0;
        int n = burst + (t > 0.3f * k && t < dur - 0.6f ? Rate(ref acc, 24f, dt) : 0);
        for (int i = 0; i < n; i++)
        {
            var (p, nn) = Surf(R(0.05f, 0.95f), 0.1f, v => v.Y > 0.2f);
            var up = ToFreeDir(nn) * R(0.05f, 0.15f);
            Parts.Spawn(new() { Pos = ToFree(p + nn * 0.003f), Vel = up + new Vector3(R(-0.12f, 0.12f), R(0.25f, 0.55f), R(-0.1f, 0.1f)) * (i < burst ? 1.4f : 1f),
                Grav = 0.5f, Drag = 1.3f, Shape = VmParticles.Shape.Streak, Size = 0.0025f, Length = 0.013f, Soft = 0.6f, Life = R(0.45f, 0.9f),
                Col = Pal.Accent, Col1 = Pal.Main.Darkened(0.35f), Flicker = 0.4f, FadeIn = 0.05f, FadeOut = 0.5f });
        }
        float fl = 0.75f + 0.25f * Mathf.Sin(t * 37f) * Mathf.Sin(t * 13f + 1f);
        SetLight(new Vector3(Side * 0.04f, B.End.Y, Centre.Z), Pal.Main, 0.8f * fl * env * w);
        mod.Glow += (1.8f * fl + (t < 0.6f * k ? 1.5f * Window(t, 0.2f * k, 0.6f * k) : 0f)) * env * w;
    }

    /// <summary>The ribbons surge: two curtains of light (the line's two colours) ripple along the camera side, sweeping
    /// back and forth, and the ice glows with them.</summary>
    void Aurora(float dt, float k, float w, float env, ref SkinMod mod)
    {
        float x = Side > 0 ? B.End.X + 0.004f : B.Position.X - 0.004f;
        int n = Rate(ref acc, 100f * env, dt);
        for (int r = 0; r < 2; r++)
        {
            float ph = r * 2.2f;
            float zf = 0.5f + 0.46f * Mathf.Sin(t * 2.1f + ph);
            float y = Centre.Y + B.Size.Y * 0.28f * Mathf.Sin(zf * 9f + t * 3f + ph);
            var head = new Vector3(x, y, Mathf.Lerp(B.Position.Z, B.End.Z, zf));
            for (int i = 0; i < n; i++)
                Parts.Spawn(new() { Pos = head + new Vector3(0f, R(-0.004f, 0.004f), R(-0.004f, 0.004f)), Vel = Vector3.Up * R(0.01f, 0.03f), Attached = true,
                    Shape = VmParticles.Shape.Dot, Size = R(0.01f, 0.016f), Soft = 1.2f, Life = 0.6f, Col = r == 0 ? Pal.Main : Pal.Accent,
                    Col1 = r == 0 ? Pal.Accent : Pal.Main, FadeIn = 0.08f, FadeOut = 0.7f, Alpha = 0.55f });
        }
        float surge = 0.5f + 0.5f * Mathf.Sin(t * 4f);
        SetLight(Centre + new Vector3(Side * 0.06f, 0.02f, 0f), Pal.Main.Lerp(Pal.Accent, surge), 0.12f * env * w); // a shimmer, not a tint over the whole gun
        mod.Glow += (1f + 1.4f * surge) * env * w;
    }

    /// <summary>The rings spin up: three engraved brass rings appear around the barrel and receiver at different tilts, spin
    /// up and precess like an orrery, then lock square with a flash and fade; star glints twinkle around them.</summary>
    void Orrery(float dt, float k, float w, float env, ref SkinMod mod)
    {
        float lockAt = dur - 0.75f;
        if (rings.Count == 0)
        {
            float rad = Mathf.Clamp(Mathf.Max(B.Size.Y, B.Size.X) * 0.7f, 0.035f, 0.085f);
            float[] zs = { 0.2f, 0.48f, 0.72f };
            for (int i = 0; i < 3; i++)
            {
                ringTilt[i] = new[] { 0.3f, -0.45f, 0.6f }[i];
                var rr = rad * new[] { 0.8f, 1f, 1.15f }[i];
                rings.Add(Parts.Spawn(new()
                {
                    Pos = new Vector3(0f, Centre.Y, Mathf.Lerp(B.Position.Z, B.End.Z, zs[i])), Attached = true, Shape = VmParticles.Shape.Ring,
                    Size = rr / 0.43f, Soft = 1f, Life = dur + 0.2f, Col = Pal.Main, Alpha = 0.75f, FadeIn = 0.2f / (dur + 0.2f), FadeOut = 0.3f / (dur + 0.2f),
                    SpinV = 0.5f,
                }));
            }
        }
        for (int i = 0; i < rings.Count; i++)
        {
            var p = rings[i];
            float dir = i % 2 == 0 ? 1f : -1f;
            if (t < lockAt)
            {
                p.SpinV = dir * Mathf.Lerp(0.6f, 10f, Mathf.SmoothStep(0.2f * k, 1.7f * k, t));
                p.Orient = new Basis(Vector3.Back, dir * t * 0.8f) * new Basis(Vector3.Right, ringTilt[i] * (1f - 0.3f * Mathf.SmoothStep(0f, lockAt, t)));
            }
            else if (lastT < lockAt)
            {
                // lock: square to the barrel, the ticks aligned
                p.SpinV = 0f;
                p.Spin = Mathf.Round(p.Spin / (Mathf.Tau / 24f)) * (Mathf.Tau / 24f);
                p.Orient = Basis.Identity;
                p.Alpha = 1.4f;
            }
            else p.Alpha = Mathf.MoveToward(p.Alpha, 0.6f, dt * 3f);
        }
        for (int i = Rate(ref acc, 12f * env, dt); i > 0; i--)
        {
            var (pp, n) = Surf(R(0f, 1f), 0.1f);
            Parts.Spawn(new() { Pos = pp + n * R(0.01f, 0.03f), Attached = true, Shape = VmParticles.Shape.Glint, Size = R(0.006f, 0.012f), Life = 0.4f,
                Col = R(0f, 1f) < 0.5f ? Colors.White : Pal.Accent, Spin = R(0f, 1f), FadeIn = 0.3f, FadeOut = 0.6f });
        }
        float flash = t >= lockAt ? Mathf.Exp(-(t - lockAt) / 0.12f) : 0f;
        SetLight(Centre + new Vector3(Side * 0.05f, 0.03f, 0f), Pal.Main, (0.3f * env + 1.2f * flash) * w);
        mod.Glow += (0.8f * env + 2f * flash) * w;
    }

    // ---------------- dev ----------------

    /// <summary>Dev: a state for <paramref name="choice"/> (<c>line[@level]</c>). The catalogue's when it knows the line with
    /// an inspect recipe; otherwise a stand-in built from <see cref="SkinLooks"/> with the skins-3 level template (Signature:
    /// one level with the inspect; Apex: inspect + special reload from level 4), so the flourishes can be checked before
    /// the catalogue is merged. Null for factory / unknown ids.</summary>
    public static SkinState? DevState(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice) || choice == GunSkins.FactoryId) return null;
        string s = choice.Trim().ToLowerInvariant();
        int level = 0;
        int at = s.IndexOf('@');
        if (at >= 0) { int.TryParse(s[(at + 1)..], out level); s = s[..at]; }
        string? variant = null;
        int sep = s.IndexOf('/');
        if (sep >= 0) { variant = s[(sep + 1)..]; s = s[..sep]; }
        if (SkinCatalog.Get(s) is { } real && real.Levels.Any(l => l.Inspect != null || l.ReloadAnim != null))
            return new SkinState(real, Math.Clamp(level <= 0 ? real.MaxLevel : level, 1, real.MaxLevel), real.Variant(variant));
        bool apex = SkinLooks.Apex.Contains(s), sig = SkinLooks.Signature.Contains(s);
        if (!apex && !sig) return null;
        var fx = SkinFeature.Sounds | SkinFeature.KillSounds | SkinFeature.HitSounds | SkinFeature.BulletFx | SkinFeature.KillFx;
        SkinLevel[] levels = sig
            ? new[] { new SkinLevel(1, fx | SkinFeature.Inspect | SkinFeature.KillBanner, "Base") { Sounds = s, KillFx = s, BulletFx = s, Inspect = s, Banner = s } }
            : new[]
            {
                new SkinLevel(1, SkinFeature.None, "Base"),
                new SkinLevel(2, SkinFeature.Animated | SkinFeature.Reactive, "Animated") { Look = g => g with { Anim = 0.3f, React = 1f } },
                new SkinLevel(3, fx, "Sounds") { Sounds = s, KillFx = s, BulletFx = s },
                new SkinLevel(4, SkinFeature.Inspect | SkinFeature.ReloadAnim | SkinFeature.KillBanner, "Inspect") { Inspect = s, ReloadAnim = s, Banner = s },
                new SkinLevel(5, SkinFeature.Finisher | SkinFeature.KillCounter, "Finisher") { Finisher = s },
            };
        var line = new SkinLine(s, s, "", default, SkinLooks.Look(s), levels, SkinLooks.Variants(s), SkinLooks.Palette(s));
        return new SkinState(line, Math.Clamp(level <= 0 ? line.MaxLevel : level, 1, line.MaxLevel), line.Variant(variant));
    }
}
