using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;
using ValTrainer.UI;
using ValTrainer.Valorant;

namespace ValTrainer;

/// <summary>
/// Application root (script of Main.tscn): loads settings + the Valorant profile, applies Valorant's
/// display settings, owns the audio engine and switches between the menu and game sessions.
/// </summary>
public partial class Main : Node
{
    public static Main I { get; private set; } = null!;

    public AppSettings Settings = AppSettings.Load();
    public StatsStore Stats = StatsStore.Load();
    public List<ValorantAccount> Accounts = new();
    public ValorantProfile Valorant = new();

    Node? screen;

    /// <summary>Automation/dev mode (--dev): windowed 1600×900, never captures the mouse or pauses on focus loss,
    /// so test runs don't take over the desktop. Also accepts --tier N and --map key overrides (not saved).</summary>
    public bool Dev { get; private set; }
    public int? TierOverride { get; private set; }
    public string? MapOverride { get; private set; }

    /// <summary>Whether this run may write settings, stats and telemetry: always in normal play; in dev runs only with
    /// --data-dir &lt;folder&gt; --write-data (so tests can exercise saving without touching the user's real files).</summary>
    public bool SavesData => !Dev || (Paths.DataDirOverridden && CmdLine.Has("--write-data"));

    // ---- Effective settings: trainer override if set, otherwise Valorant's ----
    public float Sens => Settings.UseSensOverride ? Settings.SensOverride : Valorant.Sensitivity;
    public int EnemyColorIndex => Settings.EnemyColorOverride >= 0 ? Settings.EnemyColorOverride : Valorant.EnemyHighlight;
    public Color EnemyColor => UiTheme.EnemyColors[Math.Clamp(EnemyColorIndex, 0, UiTheme.EnemyColors.Length - 1)];
    public int WindowMode => Settings.WindowModeOverride >= 0 ? Settings.WindowModeOverride : Valorant.WindowMode;
    public bool VSync => Settings.VSyncOverride >= 0 ? Settings.VSyncOverride == 1 : Valorant.VSync;
    public int FpsCap => Settings.FpsCapOverride >= 0 ? Settings.FpsCapOverride : (int)Valorant.FrameRateLimit;
    public int Monitor => Math.Clamp(Settings.MonitorOverride >= 0 ? Settings.MonitorOverride : Valorant.MonitorIndex, 0, Math.Max(0, DisplayServer.GetScreenCount() - 1));

    public override void _Ready()
    {
        I = this;
        Boot.InstallCrashHandlers();
        // Old builds stored Easy/Normal/Hard; map once onto the 5 rank tiers.
        if (Settings.Tier < 0 || Settings.Tier > 4) Settings.Tier = Settings.Difficulty switch { 0 => 0, 2 => 3, _ => 2 };
        Reimport();
        Sfx.Volume = Settings.Volume;
        AddChild(new Sfx { Name = "Sfx" });
        UiTheme.Init();
        if (!OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).Contains("--dev")) ApplyDisplay();
        GetTree().Root.Title = "ValTrainer";

        var args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        Dev = args.Contains("--dev");
        AppSettings.ReadOnly = !SavesData; // dev/automated runs never write the user's settings (quit, F11, result buttons, coach "Try it" …)
        if (args.Contains("--coach-analyze"))
        {
            // Dev tool: analyse saved telemetry and quit (works with --headless).
            Analysis.CoachCli.Run(args);
            GetTree().Quit();
            return;
        }
        if (Dev && CmdLine.After("--print-licenses") is { } licenses)
        {
            // Build tool (works with --headless): the open-source notices for THIRD-PARTY-NOTICES.txt.
            GetTree().Quit(AboutLicenses.WriteFile(licenses) ? 0 : 1);
            return;
        }
        PickFirstRunQuality();
        Log.Info($"ValTrainer {AppInfo.Version}; {SysInfo.Summary}; quality {Settings.Quality}{(Settings.AutoQualityReason != null ? $" (auto: {Settings.AutoQualityReason})" : "")}; " +
                 $"data {Paths.DataDir}{(Paths.DataDirProblem != null ? $" [{Paths.DataDirProblem}]" : "")}; " +
                 $"VALORANT {(Valorant.Found ? "imported" : Valorant.Status.ToString())}; logs {Paths.LogDir}");
        if (Dev && args.Contains("--culture-test"))
        {
            // Dev self-test (works with --headless): parsers and number formatting under the current culture.
            int failures = CultureCheck.Run();
            GetTree().Quit(failures == 0 ? 0 : 1);
            return;
        }
        if (Dev && args.Contains("--throw-test")) throwTest = 61; // dev: prove callback/task exceptions reach the log
        WhatsNew.Prepare(Settings);  // "What's new" panel after an update (dev: --whats-new [fromVersion])
        Updater.Init();              // how this copy updates itself; finishes / gives up on an update started last run
        UpdateCheck.StartupCheck();  // background, at startup and every 6 h (dev: --update-test <version>, --update-source <url>)
        int ti = Array.IndexOf(args, "--tier");
        if (ti >= 0 && ti + 1 < args.Length && int.TryParse(args[ti + 1], out var tv)) TierOverride = Math.Clamp(tv, 0, 4);
        int mi = Array.IndexOf(args, "--map");
        if (mi >= 0 && mi + 1 < args.Length) MapOverride = args[mi + 1];
        if (Dev)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            // Dev "--window WxH" (e.g. 1280x720, 1024x768, 1720x720) for layout tests at other resolutions / aspects.
            DisplayServer.WindowSetSize(DevWindowSize(CmdLine.After("--window")) ?? new Vector2I(1600, 900));
            DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(Monitor) + new Vector2I(60, 60));
        }
        ApplyFps();
        int i = Array.IndexOf(args, "--mode");
        var make = i >= 0 && i + 1 < args.Length ? ModeRegistry.Find(args[i + 1]) : null;
        // A downloaded update is installed now, before the menu (never mid-drill): "Updating ValTrainer…", then the
        // installer / portable swap starts the new version and this one quits.
        bool updating = make == null && Updater.ShouldApplyAtStartup();
        if (make != null) StartMode(make);
        else if (updating) SetScreen(new UpdatingScreen());
        else ShowMenu();
        if (updating) return;
        // Dev-only: "--screen settings|about|stats" opens that screen directly (MenuScreen handles "profile").
        switch (Dev ? CmdLine.After("--screen")?.ToLowerInvariant() : null)
        {
            case "settings": Callable.From(ShowSettings).CallDeferred(); break;
            case "about": SettingsScreen.StartPage = SettingsScreen.Page.About; Callable.From(ShowSettings).CallDeferred(); break;
            case "stats": Callable.From(ShowStats).CallDeferred(); break;
            case "warmup": Callable.From(ShowWarmup).CallDeferred(); break;
            case "agents": Callable.From(ShowAgents).CallDeferred(); break; // + --agent jett, --agent-go routine|<drill> (AgentsScreen)
        }
        // Dev-only: "--warmup quick|standard|pro|demo" starts a warm-up routine (demo = summary with made-up data).
        if (Dev && make == null && CmdLine.After("--warmup") is { } wu) Callable.From(() => Warmup.WarmupRunner.StartDev(wu)).CallDeferred();
        Analysis.CoachWarmUp.Start(); // JIT the coach off the main thread before the first results screen
    }

    static Vector2I? DevWindowSize(string? wxh)
    {
        var p = wxh?.ToLowerInvariant().Split('x');
        return p is { Length: 2 } && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) && w >= 320 && h >= 240
            ? new Vector2I(w, h) : null;
    }

    /// <summary>First run (no settings file): Low on integrated / virtual / software GPUs and on the Compatibility
    /// renderer, else the default Medium. A quality the user saved is never touched.</summary>
    void PickFirstRunQuality()
    {
        if (!Settings.IsNew) return;
        var (q, reason) = SysInfo.AutoQuality();
        Settings.Quality = q;
        Settings.AutoQualityReason = reason;
    }

    public void Reimport()
    {
        Accounts = ValorantImporter.FindAccounts();
        var acc = Accounts.FirstOrDefault(a => a.Id == Settings.AccountId) ?? Accounts.FirstOrDefault();
        Valorant = ValorantImporter.Load(acc);
    }

    void SetScreen(Node n)
    {
        screen?.QueueFree();
        screen = n;
        AddChild(n);
    }

    /// <summary>Tier in effect (dev override or the saved setting).</summary>
    public int Tier => TierOverride ?? Settings.Tier;
    public string MapKey => MapOverride ?? Settings.MapKey;

    /// <summary>Set when a drill or warm-up is started from the Agents screen: the next <see cref="ShowMenu"/> (results →
    /// MENU, pause → Back to menu, warm-up summary) returns to the Agents screen instead of the main menu.</summary>
    public bool ReturnToAgents;

    public void ShowMenu()
    {
        Warmup.WarmupRunner.Abandon(); // leaving a warm-up mid-routine (pause → Back to menu)
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (ReturnToAgents) { ReturnToAgents = false; SetScreen(new AgentsScreen()); return; }
        SetScreen(new MenuScreen());
    }

    public void ShowAgents() => SetScreen(new AgentsScreen());

    public void ShowSettings() => SetScreen(new SettingsScreen());
    public void ShowStats() => SetScreen(new StatsScreen());
    public void ShowProfile() => SetScreen(new ProfileScreen());
    public void ShowWarmup() => SetScreen(new Warmup.WarmupScreen());
    /// <summary>Shows any screen node (warm-up step cards, sessions prepared with routine hooks, the warm-up summary).</summary>
    public void ShowScreen(Node n) => SetScreen(n);

    public void StartMode(Func<TrainingMode> make) => SetScreen(new GameSession(make));

    public void Quit()
    {
        Settings.Save();
        GetTree().Quit();
    }

    // ---------------- display ----------------

    public void ApplyFps()
    {
        if (Dev) { Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); return; } // benchmarkable
        Engine.MaxFps = Math.Max(0, FpsCap);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    /// <summary>Mirrors Valorant's display mode, resolution and monitor (overridable in Settings).</summary>
    public void ApplyDisplay()
    {
        int mon = Monitor;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetCurrentScreen(mon);
        var screenPos = DisplayServer.ScreenGetPosition(mon);
        var screenSize = DisplayServer.ScreenGetSize(mon);
        switch (WindowMode)
        {
            case 0:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            default:
                var size = new Vector2I(Math.Min(Valorant.ResX, screenSize.X - 80), Math.Min(Valorant.ResY, screenSize.Y - 100));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(screenPos + (screenSize - size) / 2);
                break;
        }
    }

    double fpsLogT;

    int throwTest;

    public override void _Process(double delta)
    {
        UpdateCheck.Poll(); // picks up the background update check's answer (cheap when idle)
        Updater.Poll();     // background update download / install (cheap when idle)
        // Dev runs log FPS so automated checks can read performance from stdout.
        if (!Dev) return;
        if (throwTest > 0)
        {
            // Frame 1: a task that faults and is never awaited; ~60 frames later collect it (fires
            // UnobservedTaskException) and throw from this callback (Godot logs it and keeps running).
            if (--throwTest == 60) System.Threading.Tasks.Task.Run(() => throw new InvalidOperationException("throw-test: exception in a background task"));
            if (throwTest == 0)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                throw new InvalidOperationException("throw-test: exception in Main._Process");
            }
        }
        fpsLogT += delta;
        if (fpsLogT >= 2.0) { fpsLogT = 0; GD.Print($"[fps] {Engine.GetFramesPerSecond()}"); }
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            Settings.WindowModeOverride = WindowMode == 2 ? 1 : 2;
            Settings.Save();
            ApplyDisplay();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Settings.Save();
    }
}
