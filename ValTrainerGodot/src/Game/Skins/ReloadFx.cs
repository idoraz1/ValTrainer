using Godot;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// How a special reload restyles the viewmodel's reload curve (<see cref="Viewmodel"/>). Every value is a shape on the SAME
/// progress timeline as the factory reload (the session's <c>ReloadProgress</c>, 0..1): the magazine still leaves and seats
/// at exactly the factory progress points and the bolt / pump still cycles at the same point, so the reload takes exactly
/// the factory duration and the ammo timing (WeaponController) is never touched. All offsets are 0 at progress 0 and 1.
/// </summary>
/// <param name="Tilt">Multiplies the factory tilt rotation (pitch, yaw, roll).</param>
/// <param name="TiltAdd">Added to the tilt (deg), on the same envelope.</param>
/// <param name="Away">Added to the magazine's "away" point (gun space, m): a lower or wider path.</param>
/// <param name="Arc">A swoop added mid-path (sin π·m), m.</param>
/// <param name="MagSpin">Magazine turn about its long axis at the far point (deg; back to 0 when it seats).</param>
/// <param name="MagRoll">Magazine roll at the far point (deg).</param>
/// <param name="SeatJolt">Strength of the jolt when it seats (factory 1).</param>
public readonly record struct ReloadStyle(Vector3 Tilt, Vector3 TiltAdd, Vector3 Away, Vector3 Arc, float MagSpin, float MagRoll, float SeatJolt)
{
    public static readonly ReloadStyle Factory = new(Vector3.One, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0f, 0f, 1f);
}

/// <summary>
/// The Apex special reload (skins3-contracts §2, <see cref="SkinState.ReloadAnim"/> at Apex L4+; recipe id = line id): a
/// restyled reload motion (<see cref="Style"/>) plus effects on the magazine, and <see cref="SkinAudio.PlayReloadAnim"/>
/// (called by the session at the reload start instead of the reload layer). Foundry: the magazine glows red-hot and throws
/// sparks when it seats. Aurora: a ribbon of light trails the magazine. Orrery: rings spin up around the magazine and lock as
/// it seats. Guns without a magazine (shotguns, revolver, Marshal) get the same effects on the support hand at the loading
/// port, one "seat" per round. Cosmetic: same duration as the factory reload, never recorded.
/// </summary>
public sealed class ReloadFx : VmSkinFx
{
    public static readonly string[] Recipes = { "foundry", "aurora", "orrery" };

    /// <summary>The restyled reload curve of a recipe (null = factory).</summary>
    public static ReloadStyle? StyleFor(string? id) => id switch
    {
        // heavy: deeper cant, the magazine dropped low, slammed home
        "foundry" => new ReloadStyle(new Vector3(1.25f, 1f, 1.25f), new Vector3(2f, 0f, -6f), new Vector3(-0.01f, -0.05f, 0.02f), Vector3.Zero, 0f, 10f, 1.9f),
        // flowing: the magazine swoops out wide and back with a roll
        "aurora" => new ReloadStyle(new Vector3(0.9f, 1.4f, 0.9f), new Vector3(0f, 4f, 0f), new Vector3(-0.03f, 0.02f, 0.03f), new Vector3(-0.07f, 0.04f, 0.04f), 0f, 35f, 0.8f),
        // precise: crisp tilt, the magazine turns a quarter like a clockwork part and clicks in
        "orrery" => new ReloadStyle(new Vector3(0.85f, 0.7f, 0.85f), new Vector3(3f, 0f, 0f), new Vector3(0.01f, 0f, 0f), Vector3.Zero, 90f, 0f, 1.25f),
        _ => null,
    };

    readonly ReloadStyle style;
    float acc, seatT = 9f, lastP = -1f;
    bool active;
    readonly List<VmParticles.P> rings = new();

    ReloadFx(Viewmodel vm, SkinState s, string id, ReloadStyle style) : base(vm, s, id, 160) => this.style = style;

    /// <summary>The special reload of a state (null = the factory reload).</summary>
    public static ReloadFx? For(Viewmodel vm, SkinState? s) =>
        s is { ReloadAnim: { } id } st && Recipes.Contains(id) && StyleFor(id) is { } style ? new ReloadFx(vm, st, id, style) : null;

    public ReloadStyle Style => style;
    public bool Active => active;

    public void Begin() { active = true; acc = 0f; seatT = 9f; lastP = -1f; rings.Clear(); }

    /// <summary>The reload ended (finished or interrupted): no new particles, the rest fades.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        Parts.FadeAll(0.2f);
        rings.Clear();
    }

    /// <summary>The magazine (or a round) seated at gun-space <paramref name="at"/>.</summary>
    public void OnSeat(Vector3 at)
    {
        seatT = 0f;
        switch (Id)
        {
            case "foundry":
                for (int i = Lite ? 10 : 22; i > 0; i--)
                {
                    var v = new Vector3(R(-0.5f, 0.5f), R(0.3f, 1.1f), R(-0.4f, 0.4f));
                    Parts.Spawn(new() { Pos = ToFree(at), Vel = v, Grav = 3f, Drag = 1f, Shape = VmParticles.Shape.Streak, Size = 0.0022f, Length = 0.014f,
                        Soft = 0.5f, Life = R(0.25f, 0.5f), Col = Colors.White, Col1 = Pal.Main, FadeIn = 0.02f, FadeOut = 0.5f, Alpha = 1.2f });
                }
                break;
            case "aurora":
                for (int i = 0; i < 12; i++)
                {
                    var o = new Vector3(R(-0.02f, 0.02f), R(-0.03f, 0.02f), R(-0.03f, 0.03f));
                    Parts.Spawn(new() { Pos = at + o, Attached = true, Shape = VmParticles.Shape.Glint, Size = R(0.008f, 0.016f), Life = R(0.3f, 0.5f),
                        Col = i % 2 == 0 ? Pal.Main : Pal.Accent, Spin = R(0f, 1f), FadeIn = 0.2f, FadeOut = 0.6f });
                }
                break;
            case "orrery":
                foreach (var r in rings)
                {
                    r.SpinV = 0f;
                    r.Spin = Mathf.Round(r.Spin / (Mathf.Tau / 24f)) * (Mathf.Tau / 24f);
                    r.Alpha = 1.5f;
                    r.Size1 = r.Size * 0.6f;
                    r.Life = r.Age + 0.35f;
                    r.FadeOut = 0.6f;
                }
                rings.Clear();
                break;
        }
    }

    /// <summary>Every frame. <paramref name="p"/> = reload progress (−1 = none), <paramref name="mag"/> = the magazine's
    /// (or the loading hand's) gun-space position, <paramref name="magOut"/> = the magazine is off the gun.</summary>
    public void Tick(float dt, float p, Vector3 mag, bool magOut, ref SkinMod mod)
    {
        seatT += dt;
        if (active && p >= 0f)
        {
            mod.ReloadK = 1f;
            mod.ReloadP = p;
            switch (Id)
            {
                case "foundry": Foundry(dt, p, mag, magOut, ref mod); break;
                case "aurora": Aurora(dt, p, mag, magOut, ref mod); break;
                case "orrery": Orrery(dt, p, mag, magOut, ref mod); break;
            }
        }
        float flash = Mathf.Exp(-seatT / 0.12f);
        var col = Id == "aurora" ? Pal.Accent : Pal.Main;
        SetLight(mag + new Vector3(Side * 0.03f, 0.02f, 0f), col, (active && magOut ? 0.35f : 0f) + 1.3f * flash);
        mod.Glow += 1.5f * flash;
        lastP = p;
        if (Parts.Count > 0 || active) Parts.Tick(dt);
    }

    int Rate(float rate, float dt)
    {
        if (Lite) rate *= 0.5f;
        acc += rate * dt;
        int n = (int)acc;
        acc -= n;
        return n;
    }

    void Foundry(float dt, float p, Vector3 mag, bool magOut, ref SkinMod mod)
    {
        // red-hot from when it leaves the gun, hottest on the way back, cooling once it has seated
        float heat = Mathf.SmoothStep(0.18f, 0.4f, p) * (1f - Mathf.SmoothStep(0.64f, 0.9f, p));
        mod.MagEmit += Pal.Main * (1.6f * heat);
        if (magOut)
            for (int i = Rate(16f, dt); i > 0; i--)
                Parts.Spawn(new() { Pos = ToFree(mag + new Vector3(R(-0.01f, 0.01f), R(-0.05f, 0f), R(-0.015f, 0.015f))), Vel = new Vector3(R(-0.05f, 0.05f), R(0.05f, 0.2f), R(-0.05f, 0.05f)),
                    Grav = 0.4f, Drag = 1.5f, Shape = VmParticles.Shape.Dot, Size = R(0.003f, 0.006f), Soft = 0.5f, Life = R(0.3f, 0.6f),
                    Col = Pal.Accent, Col1 = Pal.Main.Darkened(0.4f), Flicker = 0.5f, FadeIn = 0.05f, FadeOut = 0.6f });
        mod.Glow += 0.6f * heat;
    }

    void Aurora(float dt, float p, Vector3 mag, bool magOut, ref SkinMod mod)
    {
        if (!magOut) return;
        int n = Rate(110f, dt);
        for (int i = 0; i < n; i++)
            Parts.Spawn(new() { Pos = ToFree(mag + new Vector3(R(-0.006f, 0.006f), R(-0.04f, 0.01f), R(-0.006f, 0.006f))), Shape = VmParticles.Shape.Dot, Size = R(0.01f, 0.017f),
                Soft = 1.2f, Life = 0.5f, Col = (i + (int)(p * 40f)) % 2 == 0 ? Pal.Main : Pal.Accent, Col1 = Pal.Alt, FadeIn = 0.05f, FadeOut = 0.75f, Alpha = 0.5f });
        mod.Glow += 0.8f;
    }

    void Orrery(float dt, float p, Vector3 mag, bool magOut, ref SkinMod mod)
    {
        if (magOut && rings.Count == 0 && p < 0.6f)
        {
            float rad = Mathf.Clamp(Vm.MagHalfExtent * 1.9f, 0.025f, 0.05f);
            for (int i = 0; i < 2; i++)
                rings.Add(Parts.Spawn(new()
                {
                    Pos = mag, Attached = true, Shape = VmParticles.Shape.Ring, Size = 0f, Size1 = -1f, Soft = 1f, Life = 2f, Col = Pal.Main,
                    Alpha = 0.8f, FadeIn = 0.08f, FadeOut = 0.05f, SpinV = i == 0 ? 2f : -2f, Seed = rad,
                }));
        }
        float spin = Mathf.Lerp(2f, 14f, Mathf.SmoothStep(0.2f, 0.58f, p));
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            r.Pos = mag + Vector3.Down * 0.03f;
            r.SpinV = (i == 0 ? 1f : -1f) * spin;
            float rad = Mathf.Clamp(Vm.MagHalfExtent * 1.9f, 0.025f, 0.05f) * (i == 0 ? 1f : 1.3f);
            r.Size = Mathf.MoveToward(r.Size, rad / 0.43f, dt * 0.6f);
            // the ring axis is the magazine's long axis (vertical), precessing a little
            r.Orient = new Basis(Vector3.Right, Mathf.Pi / 2f + (i == 0 ? 0.35f : -0.5f) * Mathf.Sin(p * 9f));
        }
        if (magOut)
            for (int i = Rate(10f, dt); i > 0; i--)
                Parts.Spawn(new() { Pos = mag + new Vector3(R(-0.03f, 0.03f), R(-0.06f, 0.02f), R(-0.03f, 0.03f)), Attached = true, Shape = VmParticles.Shape.Glint,
                    Size = R(0.006f, 0.011f), Life = 0.35f, Col = R(0f, 1f) < 0.5f ? Colors.White : Pal.Accent, FadeIn = 0.3f, FadeOut = 0.6f });
        mod.Glow += 0.5f;
    }
}
