using Godot;

namespace ValTrainer.Game.Bots;

/// <summary>
/// Last modifier on a bot's skeleton: runs after the AnimationTree has posed it (during the skeleton update) and lets
/// <see cref="BotCharacter"/> apply its procedural overrides (spine counter-twist for strafing, recoil kick) and capture
/// the FINAL pose (hitboxes, rifle mount). Overrides made outside a modifier would not reach the rendered skin.
/// </summary>
public partial class BotPoseModifier : SkeletonModifier3D
{
    internal BotCharacter? Owner_;

    public override void _ProcessModificationWithDelta(double delta) => Owner_?.OnSkeletonModify();
}
