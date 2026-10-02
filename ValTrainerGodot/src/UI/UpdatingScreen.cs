using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// Shown at launch instead of the menu when a downloaded update is waiting (see <see cref="Updater.ShouldApplyAtStartup"/>):
/// "Updating ValTrainer to X.Y.Z…" while the file is checked once more and the installer starts (or the portable exe is
/// swapped); then the app quits and the new version starts by itself. If anything fails it falls through to the menu
/// (the banner then explains).
/// </summary>
public partial class UpdatingScreen : ScreenBase
{
    double t;
    bool started;
    DrawBox? box;
    readonly string version = Updater.Target ?? "";

    protected override void Back() { } // nothing to go back to

    protected override void Build()
    {
        float k = K;
        box = new DrawBox
        {
            AnchorRight = 1, AnchorBottom = 1,
            OnDraw = d =>
            {
                float cx = d.Size.X / 2, cy = d.Size.Y / 2;
                int ts = UiTheme.Fs(54, k), vs = UiTheme.Fs(18, k), ss = UiTheme.Fs(15, k);
                // spinner: a rotating accent arc
                float r = 22 * k, a0 = (float)(t * 4.0);
                d.DrawArc(new Vector2(cx, cy - 92 * k), r, a0, a0 + Mathf.Pi * 1.4f, 32, UiTheme.Accent, 4 * k, true);
                Gfx.TextC(d, UiTheme.Display, "UPDATING VALTRAINER", cx, cy, ts, UiTheme.Text);
                Gfx.TextC(d, UiTheme.HudWide, $"{AppInfo.Version}  →  {version}", cx, cy + 38 * k, vs, UiTheme.Accent);
                string sub = Updater.Kind == InstallKind.Portable
                    ? "Replacing ValTrainer.exe. ValTrainer starts again by itself in a moment."
                    : "Installing the update. ValTrainer closes and starts again by itself in a few seconds.";
                Gfx.TextC(d, UiTheme.Body, sub, cx, cy + 74 * k, ss, UiTheme.Dim);
                Gfx.TextC(d, UiTheme.Body, "Your settings and stats are kept.", cx, cy + 98 * k, ss, UiTheme.Faint);
            },
        };
        AddChild(box);
    }

    protected override void Tick(float dt)
    {
        t += dt;
        box?.QueueRedraw();
        if (!started)
        {
            if (t < 0.5) return; // let the screen show first
            started = true;
            Updater.RestartToUpdate();
            return;
        }
        // Applying → the app quits when the installer has started. Anything else means it failed: go to the menu.
        if (Updater.Status != Updater.State.Applying) Main.I.ShowMenu();
    }
}
