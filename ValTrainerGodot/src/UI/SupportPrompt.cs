using Godot;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// "Support ValTrainer": a small panel with the two tip jars (Buy Me a Coffee, Ko-fi). It shows on its own on the first
/// launch (after the "What's new" and updates panels), then at most once every <see cref="IntervalDays"/> days and
/// <see cref="MinLaunches"/> launches, until the player picks DON'T SHOW AGAIN or turns it off in Settings → About. The
/// menu's SUPPORT button opens it any time. ValTrainer never contacts either site itself: the buttons open the page in
/// the browser.
/// <para>Dev: only in runs that behave like a normal launch (<c>--data-dir … --write-data</c>) or with
/// <c>--support-prompt</c>.</para>
/// </summary>
public static class SupportPrompt
{
    const int IntervalDays = 14, MinLaunches = 5;

    /// <summary>The menu should show the panel (cleared once it has been shown).</summary>
    public static bool Pending;

    /// <summary>Called once from Main._Ready (after <see cref="UpdatesPrompt.Prepare"/>): counts the launch and decides.</summary>
    public static void Prepare(AppSettings s)
    {
        bool force = CmdLine.Dev && CmdLine.Has("--support-prompt");
        if (!force && CmdLine.Dev && !Main.I.SavesData) return; // automated runs never stop at a prompt
        s.SupportLaunches++;
        s.Save();
        if (!force && !s.SupportReminders) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool first = s.SupportLastShown <= 0;
        // after an update the "What's new" panel is enough for one launch: existing players get it next time
        if (!force && first && !s.IsNew && WhatsNew.Pending != null) return;
        bool due = first || (now - s.SupportLastShown >= IntervalDays * 86400L && s.SupportLaunches >= MinLaunches)
                   || s.SupportLastShown > now + 86400; // clock moved back
        if (!force && !due) return;
        Pending = true;
        Log.Info("Support: showing the support panel" + (first ? " (first time)" : ""));
    }

    /// <summary>The panel showed on its own: start the next interval.</summary>
    public static void Shown()
    {
        Pending = false;
        var s = Main.I.Settings;
        s.SupportLastShown = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        s.SupportLaunches = 0;
        s.Save();
    }

    /// <summary>DON'T SHOW AGAIN (same as turning it off in Settings → About).</summary>
    public static void TurnOff()
    {
        Pending = false;
        Main.I.Settings.SupportReminders = false;
        Main.I.Settings.Save();
        Log.Info("Support: reminders turned off by the player");
    }
}

/// <summary>Modal over the menu: why ValTrainer is free, BUY ME A COFFEE / KO-FI, and MAYBE LATER (plus DON'T SHOW AGAIN
/// when it opened on its own).</summary>
public partial class SupportPanel : Control
{
    readonly float k;
    readonly bool automatic;
    readonly Action onClose;

    /// <param name="automatic">Opened on its own (first launch / reminder) rather than with the SUPPORT button.</param>
    public SupportPanel(float k, bool automatic, Action onClose)
    {
        this.k = k;
        this.automatic = automatic;
        this.onClose = onClose;
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
            Title = "SUPPORT VALTRAINER", Caption = "optional", K = k, Fill = new Color(0.07f, 0.11f, 0.15f, 0.97f),
            CustomMinimumSize = new Vector2(Mathf.Min(760 * k, vs.X - 80 * k), 0),
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
                const string q1 = "ENJOYING ", q2 = "VALTRAINER?";
                Gfx.Text(b, UiTheme.Display, q1, 0, y, fs, UiTheme.Text);
                Gfx.Text(b, UiTheme.Display, q2, Gfx.TextW(UiTheme.Display, q1, fs), y, fs, UiTheme.Accent);
            },
        });
        col.AddChild(Text("ValTrainer is free and open source, with no ads, no account and nothing tracked. It's made by one developer in their spare time.", 16, UiTheme.Text));
        col.AddChild(Text("If it helps your game, you can buy me a coffee. It keeps new drills, agents and fixes coming. Everything stays free either way.", 16, UiTheme.Dim));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * k), MouseFilter = MouseFilterEnum.Ignore });

        var jars = new HBoxContainer();
        jars.AddThemeConstantOverride("separation", (int)(14 * k));
        var bmc = new VButton { Label = "BUY ME A COFFEE", Kind = VButton.Look.Primary, K = k, FontPx = 20,
            CustomMinimumSize = new Vector2(0, 56 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Opens buymeacoffee.com/idoraz1 in your browser" };
        bmc.Pressed += () => Open(AppInfo.BuyMeACoffeeUrl);
        jars.AddChild(bmc);
        var kofi = new VButton { Label = "KO-FI", Kind = VButton.Look.Secondary, K = k, FontPx = 20,
            CustomMinimumSize = new Vector2(0, 56 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Opens ko-fi.com/idoraz1 in your browser" };
        kofi.Pressed += () => Open(AppInfo.KofiUrl);
        jars.AddChild(kofi);
        col.AddChild(jars);

        col.AddChild(Text("Payments happen on Buy Me a Coffee or Ko-fi in your browser. ValTrainer never sees them.", 14, UiTheme.Faint));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * k), MouseFilter = MouseFilterEnum.Ignore });

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", (int)(14 * k));
        if (automatic)
        {
            var never = new VButton { Label = "DON'T SHOW AGAIN", Kind = VButton.Look.Ghost, K = k, FontPx = 16, CustomMinimumSize = new Vector2(200 * k, 46 * k),
                TooltipText = "No more reminders. The SUPPORT button on the menu stays; Settings → About turns reminders back on" };
            never.Pressed += () => { SupportPrompt.TurnOff(); Close(); };
            foot.AddChild(never);
        }
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var later = new VButton { Label = automatic ? "MAYBE LATER" : "CLOSE", Kind = VButton.Look.Secondary, K = k, FontPx = 18, CustomMinimumSize = new Vector2(190 * k, 46 * k) };
        later.Pressed += Close;
        foot.AddChild(later);
        col.AddChild(foot);

        // Dev: "--support-answer later|never|bmc|kofi" presses a button after 2 s (automated tests; bmc/kofi only log)
        if (CmdLine.DevAfter("--support-answer")?.ToLowerInvariant() is { } ans)
            GetTree().CreateTimer(2.0).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree()) return;
                Log.Info($"Support: --support-answer {ans}");
                if (ans == "never") SupportPrompt.TurnOff();
                Close();
            };
    }

    void Open(string url)
    {
        if (Main.I.Dev) Log.Info($"Support: dev run, would open {url}");
        else if (!AppInfo.OpenSupport(url)) Log.Error($"Support: couldn't open {url}");
        else Log.Info($"Support: opened {url}");
        Close();
    }

    bool closed;

    void Close()
    {
        if (closed || !IsInsideTree()) return;
        closed = true;
        UiTheme.ClickSound();
        QueueFree();
        onClose();
    }

    Label Text(string s, float px, Color c) => new()
    {
        Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        LabelSettings = UiTheme.Label(UiTheme.Body, px, k, c), MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(40 * k, 0),
    };

    /// <summary>Esc = MAYBE LATER / CLOSE.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}
