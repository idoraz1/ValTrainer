using Godot;
using ValTrainer.Game.Fx;

namespace ValTrainer.Game.Replay;

/// <summary>
/// The Rewind's own bullet effects: a private <see cref="FxWorld"/> (the same pooled tracers, bullet holes, sparks and
/// hit flashes as a live run) under the stage, so the replay never touches the static <see cref="Effects.Root"/> of the
/// frozen session. Muzzle flashes come with the puppets (bots) and the replay viewmodel (player).
/// </summary>
public partial class ReplayFx : Node3D
{
    static readonly Color TracerColor = new(1f, 0.82f, 0.5f);   // Effects.Tracer's default
    static readonly Color WallDust = new(0.5f, 0.5f, 0.5f);     // GameSession.Bullet's impact tint
    FxWorld fx = null!;

    public override void _Ready()
    {
        Name = "ReplayFx";
        fx = new FxWorld();
        AddChild(fx); // FxWorld._Ready builds (and pre-warms) every pool
    }

    public void Tracer(Vector3 from, Vector3 to) => fx.Tracer(from, to, TracerColor);
    public void Impact(Vector3 pos, Vector3 normal) => fx.Impact(pos, normal, WallDust);
    public void BodyHit(Vector3 pos, Color enemy, bool headshot) => fx.BodyHit(pos, enemy, headshot);

    /// <summary>After a seek: hide every live tracer, flash and bullet hole (they belonged to another moment). The pools'
    /// own timers keep running and only ever hide items, so hiding them here is safe. Particle bursts are restarted
    /// empty (each new burst sets its own amount again).</summary>
    public void Clear()
    {
        foreach (var c in fx.GetChildren())
        {
            if (c is MeshInstance3D or Decal) ((Node3D)c).Visible = false;
            else if (c is GpuParticles3D p) { p.AmountRatio = 0f; p.Restart(); } // a finished one-shot can still have live particles
        }
    }
}
