using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// Opt-in for the only network use (the GitHub update check and download, see <see cref="UpdateCheck"/> and
/// <see cref="Updater"/>): on a fresh install, and once for settings from before this prompt existed, the menu asks
/// "Check GitHub for new versions?" (after the "What's new" panel when both are due). Until the player clicks ENABLE
/// UPDATES nothing is requested; NOT NOW (or Esc) keeps it off for good, and Settings → About can change it later.
/// Players who had already turned "Check for updates" off aren't asked again.
/// <para>Dev: like "What's new", only in runs that behave like a normal launch (<c>--data-dir … --write-data</c>) or
/// with <c>--updates-prompt</c>; <c>--updates-consent yes|no</c> answers it up front (automated updater tests) and
/// <c>--updates-answer yes|no</c> presses one of its buttons after 2 s.</para>
/// </summary>
public static class UpdatesPrompt
{
    /// <summary>The menu should ask (cleared once answered).</summary>
    public static bool Pending;

    /// <summary>Called once from Main._Ready (after the settings are loaded, before <see cref="UpdateCheck.StartupCheck"/>).</summary>
    public static void Prepare(AppSettings s)
    {
        if (s.UpdatesConsent == null && !s.IsNew && !s.CheckUpdates)
        {
            // settings from before the prompt, with "Check for updates" already turned off: that was the answer
            s.UpdatesConsent = false;
            s.Save();
        }
        if (CmdLine.DevAfter("--updates-consent")?.ToLowerInvariant() is { } a)
        {
            bool yes = a is "yes" or "true" or "on" or "1";
            s.UpdatesConsent = yes;
            s.CheckUpdates = yes;
            Log.Info($"Updates: --updates-consent {(yes ? "yes" : "no")}");
            return;
        }
        bool force = CmdLine.Dev && CmdLine.Has("--updates-prompt");
        if (!force && (s.UpdatesConsent != null || !AppInfo.HasRepo)) return;
        // automated runs on the real settings (read-only) never stop at a prompt
        if (!force && CmdLine.Dev && !Main.I.SavesData) return;
        Pending = true;
        Log.Info("Updates: asking whether to check GitHub for new versions (nothing is requested until the player says yes)");
    }

    /// <summary>ENABLE UPDATES (true) / NOT NOW (false). Saved right away; turning on starts the first check.</summary>
    public static void Answer(bool yes)
    {
        Pending = false;
        var s = Main.I.Settings;
        s.UpdatesConsent = yes;
        s.CheckUpdates = yes;
        s.Save();
        Log.Info(yes ? "Updates: turned on by the player (ENABLE UPDATES)" : "Updates: left off by the player (NOT NOW); no update requests");
        UpdateCheck.EnabledChanged();
    }
}

/// <summary>Small modal over the menu: "CHECK GITHUB FOR NEW VERSIONS?" with what it does, ENABLE UPDATES / NOT NOW.</summary>
public partial class UpdatesPromptPanel : Control
{
    readonly float k;
    readonly Action<bool> onAnswer;

    public UpdatesPromptPanel(float k, Action<bool> onAnswer)
    {
        this.k = k;
        this.onAnswer = onAnswer;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the menu underneath doesn't get clicks
    }

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.04f, 0.06f, 0.72f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vs = GetViewportRect().Size;
        var panel = new VPanel
        {
            Title = "UPDATES", Caption = $"ValTrainer {AppInfo.Version}", K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.97f),
            CustomMinimumSize = new Vector2(Mathf.Min(780 * k, vs.X - 80 * k), 0),
        };
        center.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(10 * k));
        panel.AddChild(col);

        col.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, 58 * k),
            OnDraw = b =>
            {
                int fs = UiTheme.Fs(40, k);
                float y = b.Size.Y - 14 * k;
                const string q1 = "CHECK GITHUB FOR ", q2 = "NEW VERSIONS?";
                Gfx.Text(b, UiTheme.Display, q1, 0, y, fs, UiTheme.Text);
                Gfx.Text(b, UiTheme.Display, q2, Gfx.TextW(UiTheme.Display, q1, fs), y, fs, UiTheme.Accent);
            },
        });
        col.AddChild(Text("ValTrainer can keep itself up to date. If you turn this on, it will:", 16, UiTheme.Dim));
        Bullet(col, "ask github.com for the latest ValTrainer release, at startup and every 6 hours");
        Bullet(col, "download a newer version in the background and install it when you restart ValTrainer");
        Bullet(col, "send nothing about you: no settings, stats, recordings or IDs (GitHub sees an ordinary web request)");
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2 * k), MouseFilter = MouseFilterEnum.Ignore });
        col.AddChild(Text("Until you say yes, ValTrainer makes no network requests. You can change this any time in Settings → About, where CHECK NOW also checks once.", 14, UiTheme.Faint));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6 * k), MouseFilter = MouseFilterEnum.Ignore });

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(14 * k));
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var no = new VButton { Label = "NOT NOW", Kind = VButton.Look.Ghost, K = k, FontPx = 18, CustomMinimumSize = new Vector2(150 * k, 50 * k),
            TooltipText = "No update checks; you can turn them on later in Settings → About" };
        no.Pressed += () => onAnswer(false);
        foot.AddChild(no);
        var yes = new VButton { Label = "ENABLE UPDATES", Kind = VButton.Look.Primary, K = k, FontPx = 20, CustomMinimumSize = new Vector2(230 * k, 50 * k),
            TooltipText = "Check GitHub for new versions and download them in the background" };
        yes.Pressed += () => onAnswer(true);
        foot.AddChild(yes);
        col.AddChild(foot);

        // Dev: "--updates-answer yes|no" presses ENABLE UPDATES / NOT NOW after 2 s (automated tests of the prompt)
        if (CmdLine.DevAfter("--updates-answer")?.ToLowerInvariant() is { } ans)
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree()) return;
                Log.Info($"Updates: --updates-answer: {(ans == "yes" ? "ENABLE UPDATES" : "NOT NOW")}");
                onAnswer(ans == "yes");
            };
    }

    Label Text(string s, float px, Color c) => new()
    {
        Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        LabelSettings = UiTheme.Label(UiTheme.Body, px, k, c), MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(40 * k, 0),
    };

    void Bullet(VBoxContainer col, string s)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", (int)(10 * k));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(12 * k, 0), MouseFilter = MouseFilterEnum.Ignore });
        row.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(8 * k, 22 * k),
            OnDraw = b => Gfx.Diamond(b, new Vector2(4 * k, 12 * k), 3.5f * k, 3.5f * k, UiTheme.Accent),
        });
        row.AddChild(Text(s, 16, UiTheme.Text));
        col.AddChild(row);
    }

    /// <summary>Esc = NOT NOW (never a yes by accident).</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            onAnswer(false);
        }
    }
}
