using Godot;

namespace ValTrainer.Game.Fx;

/// <summary>
/// Bullet tracers, wall impacts and hit sparks. Static facade over a pooled <see cref="FxWorld"/> node that
/// is created lazily under <see cref="Root"/> (the session's per-run world node, set on every restart):
/// glowing tracer ribbons, Decal bullet holes (cap 64, fade after ~6 s), dust/spark GPUParticles3D bursts
/// and small impact flashes. Nothing allocates materials or compiles shaders per shot.
/// </summary>
public static class Effects
{
    public static Node3D? Root;

    static FxWorld? fx;
    static Node3D? fxRoot;

    static FxWorld? World
    {
        get
        {
            if (Root == null || !GodotObject.IsInstanceValid(Root)) return null;
            if (fx == null || fxRoot != Root || !GodotObject.IsInstanceValid(fx))
            {
                fx = new FxWorld();
                fxRoot = Root;
                Root.AddChild(fx);
            }
            return fx;
        }
    }

    /// <summary>Create the pools now (call once the session's world exists) so the first shot doesn't hitch.</summary>
    public static void Prewarm() => _ = World;

    /// <summary>Glowing tracer from the muzzle to the hit point.</summary>
    public static void Tracer(Vector3 from, Vector3 to, Color? color = null) =>
        World?.Tracer(from, to, color ?? new Color(1f, 0.82f, 0.5f));

    /// <summary>Wall hit: bullet-hole decal + dust tinted by <paramref name="surface"/> + sparks + flash.</summary>
    public static void Impact(Vector3 pos, Vector3 normal, Color surface) => World?.Impact(pos, normal, surface);

    /// <summary>Enemy hit: enemy-coloured spark burst + flash (bigger/brighter for headshots).</summary>
    public static void BodyHit(Vector3 pos, Color enemy, bool headshot) => World?.BodyHit(pos, enemy, headshot);
}
