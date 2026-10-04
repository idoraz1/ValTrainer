using Godot;

namespace ValTrainer.Modes;

/// <summary>
/// Input hook of the Smoke Execute drill. It lives under the session's mode world, so it gets every input event before
/// the session (and the HUD) does: while a smoke ability is in hand the drill can take the mouse buttons (and, with the
/// tactical map open, the mouse motion) so they place smokes instead of shooting or turning the view.
/// </summary>
public partial class ControllerInput : Node
{
    internal SmokeExecuteMode? Mode;

    public override void _Input(InputEvent e)
    {
        if (Mode == null || !Mode.HandleInput(e)) return;
        // Never swallow a RELEASE (mouse button or key — fire can be bound to either): the session must see it, or it thinks
        // the trigger is still held (e.g. the last defender dies while you're firing, the summary takes the release, and the
        // gun fires forever).
        if (e is InputEventMouseButton { Pressed: false } or InputEventKey { Pressed: false }) return;
        GetViewport().SetInputAsHandled();
    }
}
