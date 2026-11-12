using Godot;
using ValTrainer.Game.Weapon;

namespace ValTrainer.Game.Skins;

/// <summary>
/// A small CPU particle pool for the viewmodel's skin flourishes (<see cref="InspectFx"/>, <see cref="ReloadFx"/>): one
/// MultiMesh (one draw call) with the additive <c>vm_skinfx.gdshader</c>, drawn with the viewmodel projection like the muzzle
/// flash, on the viewmodel's visual layer. The node is a child of the <see cref="Viewmodel"/> (camera space). A particle is
/// either <see cref="P.Attached"/> (its position is in gun space and it rides the gun as it turns: glints, frost, rings) or
/// free (camera space, with velocity, gravity and drag: embers, sparks, drips). Cosmetic only.
/// </summary>
public sealed partial class VmParticles : MultiMeshInstance3D
{
    public enum Shape { Dot = 0, Glint = 1, Streak = 2, Ring = 3, Flake = 4 }

    public const string ShaderPath = "res://assets/shaders/weapon/vm_skinfx.gdshader";
    static Shader? shader;

    public sealed class P
    {
        public Vector3 Pos, Vel;
        public float Age, Life = 0.5f, Size = 0.01f, Size1 = -1f, Spin, SpinV, Soft = 1f;
        public Shape Shape;
        public Color Col = Colors.White;
        public Color? Col1;
        public float Alpha = 1f;
        public bool Attached;
        /// <summary>Free particles: gravity (m/s², camera space, + = down) and drag (1/s).</summary>
        public float Grav, Drag;
        /// <summary>0..1: random brightness flicker.</summary>
        public float Flicker;
        public float FadeIn = 0.12f, FadeOut = 0.4f;
        /// <summary>Streaks: direction (gun space if attached, else camera space; null = along the velocity) and length (m).</summary>
        public Vector3? Dir;
        public float Length = 0.03f;
        /// <summary>Rings: orientation of the ring plane (its Z = the ring's axis), gun space if attached. Spin turns it about Z.</summary>
        public Basis Orient = Basis.Identity;
        /// <summary>An attached particle lets go of the gun at this age (s) and falls freely (a drip); NaN = never.</summary>
        public float Detach = float.NaN;
        /// <summary>Shape after it lets go (a drip stretches into a streak).</summary>
        public Shape? FreeShape;
        public float Seed;
        public bool Dead => Age >= Life;
    }

    readonly List<P> live = new();
    readonly Func<Transform3D> gunXf;
    readonly Func<float> focal;
    readonly Random rng = new();
    ShaderMaterial mat = null!;
    int cap;
    float lastFocal = -1f;

    /// <summary>Multiplies every particle's brightness (the inspect's blend weight: a cancelled inspect fades its effects).</summary>
    public float Weight = 1f;

    VmParticles(Func<Transform3D> gunXf, Func<float> focal) { this.gunXf = gunXf; this.focal = focal; }

    /// <summary>A pool of at most <paramref name="capacity"/> particles under <paramref name="parent"/> (the viewmodel).
    /// <paramref name="gunXf"/> maps gun space to the parent's space (the viewmodel's pivot).</summary>
    public static VmParticles Create(Node3D parent, Func<Transform3D> gunXf, Func<float> focal, int capacity, float depth = 0.01f)
    {
        var p = new VmParticles(gunXf, focal) { Name = "SkinVmFx", cap = capacity };
        p.mat = new ShaderMaterial { Shader = shader ??= GD.Load<Shader>(ShaderPath) };
        p.mat.SetShaderParameter("vm_depth", depth);
        p.Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = capacity, VisibleInstanceCount = 0,
        };
        p.MaterialOverride = p.mat;
        p.Layers = Viewmodel.VisualLayer;
        p.CastShadow = ShadowCastingSetting.Off;
        p.GIMode = GIModeEnum.Disabled;
        p.ExtraCullMargin = 4f;
        p.CustomAabb = new Aabb(new Vector3(-3f, -3f, -3f), new Vector3(6f, 6f, 6f));
        parent.AddChild(p);
        return p;
    }

    public int Count => live.Count;
    public float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Adds a particle (the oldest goes when the pool is full).</summary>
    public P Spawn(P p)
    {
        if (live.Count >= cap) live.RemoveAt(0);
        p.Seed = R(0f, 100f);
        live.Add(p);
        return p;
    }

    public void Clear() { live.Clear(); if (Multimesh != null) Multimesh.VisibleInstanceCount = 0; }

    /// <summary>Every particle fades out within <paramref name="secs"/> (an inspect that ended: the frost melts away).</summary>
    public void FadeAll(float secs)
    {
        foreach (var p in live)
        {
            if (p.Life - p.Age <= secs) continue;
            float u = p.Age / p.Life;
            float env = Mathf.SmoothStep(0f, Mathf.Max(1e-3f, p.FadeIn), u);
            p.Alpha *= env; // keep the current brightness: no pop when the fade-in is cut short
            p.FadeIn = 0f;
            p.Life = p.Age + secs;
            p.FadeOut = Mathf.Clamp(secs / p.Life, 0.01f, 1f);
        }
    }

    /// <summary>Ages, moves and draws the particles.</summary>
    public void Tick(float dt)
    {
        float f = focal();
        if (f != lastFocal) { lastFocal = f; mat.SetShaderParameter("vm_focal", f); }
        var g = gunXf();
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var p = live[i];
            float prev = p.Age;
            p.Age += dt;
            if (p.Dead) { live.RemoveAt(i); continue; }
            if (p.Attached && !float.IsNaN(p.Detach) && prev < p.Detach && p.Age >= p.Detach)
            {
                p.Attached = false;
                p.Pos = g * p.Pos;
                p.Vel = g.Basis * p.Vel;
                if (p.FreeShape is { } fs) p.Shape = fs;
            }
            if (p.Attached) p.Pos += p.Vel * dt;
            else
            {
                p.Vel += Vector3.Down * p.Grav * dt;
                p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Pos += p.Vel * dt;
            }
            p.Spin += p.SpinV * dt;
        }
        var mm = Multimesh;
        mm.VisibleInstanceCount = live.Count;
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            float u = p.Age / p.Life;
            float env = Mathf.SmoothStep(0f, Mathf.Max(1e-3f, p.FadeIn), u) * (1f - Mathf.SmoothStep(1f - p.FadeOut, 1f, u));
            float fl = p.Flicker > 0f ? 1f - p.Flicker * (0.5f + 0.5f * Mathf.Sin(p.Age * 47f + p.Seed * 13f) * Mathf.Sin(p.Age * 23f + p.Seed)) : 1f;
            float size = p.Size1 >= 0f ? Mathf.Lerp(p.Size, p.Size1, u) : p.Size;
            var pos = p.Attached ? g * p.Pos : p.Pos;
            Basis b;
            switch (p.Shape)
            {
                case Shape.Streak:
                {
                    var d = p.Dir ?? (p.Vel.LengthSquared() > 1e-8f ? p.Vel : Vector3.Up);
                    if (p.Attached && p.Dir != null) d = g.Basis * d;
                    d = d.Normalized();
                    var side = d.Cross(Vector3.Back);
                    if (side.LengthSquared() < 1e-6f) side = d.Cross(Vector3.Right);
                    b = new Basis(side.Normalized() * size, d * p.Length, d.Cross(side).Normalized() * size);
                    break;
                }
                case Shape.Ring:
                {
                    var o = p.Orient * new Basis(Vector3.Back, p.Spin);
                    if (p.Attached) o = g.Basis.Orthonormalized() * o;
                    b = o.Scaled(Vector3.One * size);
                    break;
                }
                default:
                    b = Basis.Identity.Scaled(Vector3.One * size);
                    break;
            }
            mm.SetInstanceTransform(i, new Transform3D(b, pos));
            var c = p.Col1 is { } c1 ? p.Col.Lerp(c1, u) : p.Col;
            mm.SetInstanceColor(i, new Color(c.R, c.G, c.B, p.Alpha * env * fl * Weight));
            mm.SetInstanceCustomData(i, new Color((float)p.Shape, p.Spin, p.Soft, p.Seed));
        }
    }
}
