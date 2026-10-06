using System.Globalization;
using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.Modes;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// SETUP CHECK on the Lock-In setup screen: a warm-up carries over best with the same mapping, latency and display as
/// the match, so it compares what was imported from VALORANT with what ValTrainer runs with right now (sens + FOV,
/// display mode + monitor, VSync + FPS cap), adds a neutral VSync / Reflex note, the crosshair visibility hint
/// (<see cref="CrosshairHint"/>) and a short manual checklist (headset, lamp, warm hands) that is remembered for this
/// session only. Nothing here blocks the start.
/// </summary>
public partial class LockInSetupCheck : VPanel
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Manual checklist, ticked state kept for this app session (never saved).</summary>
    static readonly string[] Manual = { "Same headset and volume as in VALORANT", "A lamp on, not a dark room", "Hands warm" };
    static readonly bool[] Ticked = new bool[Manual.Length];

    enum Mark { Ok, Differs, Warn, Info }
    readonly record struct Line(Mark Mark, string Label, string Value, string Note = "");

    const float RowH = 24f;

    public LockInSetupCheck(float k)
    {
        K = k;
        Title = "SETUP CHECK";
        Caption = "same setup as your match · nothing here is required";

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 0);
        AddChild(body);
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", (int)(28 * k));
        body.AddChild(cols);

        // ---- left: compared with VALORANT ----
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.7f };
        left.AddThemeConstantOverride("separation", 0);
        cols.AddChild(left);
        left.AddChild(Caption2("COMPARED WITH VALORANT"));
        var lines = Lines();
        left.AddChild(new DrawBox
        {
            CustomMinimumSize = new Vector2(0, lines.Count * RowH * k),
            OnDraw = d => { for (int i = 0; i < lines.Count; i++) DrawLine(d, lines[i], (i + 0.5f) * RowH * k, k); },
        });

        // ---- crosshair: full width under both columns (room for the warning and the Crosshair Finder link) ----
        if (CrosshairHint.ForImported() is { } hint)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowH * k) };
            row.AddThemeConstantOverride("separation", (int)(10 * k));
            var line = new Line(hint.Ok ? Mark.Ok : Mark.Warn, "Crosshair", hint.Short);
            row.AddChild(new DrawBox
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass, TooltipText = hint.Long,
                OnDraw = d => DrawLine(d, line, d.Size.Y / 2, k),
            });
            if (!hint.Ok && ModeRegistry.Find("xhairfinder") is { } finder)
            {
                const string label = "Try the Crosshair Finder →";
                var b = new VButton
                {
                    Kind = VButton.Look.Ghost, K = k, Label = label, FontPx = 15, TitleFont = UiTheme.Body,
                    CustomMinimumSize = new Vector2(Gfx.TextW(UiTheme.Body, label, UiTheme.Fs(15, k)) + 8 * k, RowH * k),
                    TooltipText = "Opens the Crosshair Finder (leaves this screen)",
                };
                b.Pressed += () => Main.I.StartMode(finder);
                row.AddChild(b);
            }
            body.AddChild(row);
        }

        // ---- right: manual checklist ----
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
        right.AddThemeConstantOverride("separation", 0);
        cols.AddChild(right);
        right.AddChild(Caption2("BEFORE YOU START"));
        for (int i = 0; i < Manual.Length; i++)
        {
            int idx = i;
            right.AddChild(new CheckRow { K = k, Text = Manual[i], On = Ticked[i], Toggled = on => Ticked[idx] = on, CustomMinimumSize = new Vector2(0, RowH * k) });
        }
    }

    DrawBox Caption2(string text) => new()
    {
        CustomMinimumSize = new Vector2(0, 22 * K),
        OnDraw = d => { int s = UiTheme.Fs(12, K); Gfx.Text(d, UiTheme.HudWide, text, 0, Gfx.Mid(9 * K, s), s, UiTheme.Faint); },
    };

    // ---------------- automatic lines ----------------

    static readonly string[] ModeNames = { "Fullscreen", "Windowed fullscreen", "Windowed" };

    static string Fps(int cap) => cap <= 0 ? "unlimited" : cap.ToString(Inv);

    /// <summary>What ValTrainer runs with right now: window mode (0 fullscreen, 1 windowed fullscreen, 2 windowed),
    /// monitor, VSync, FPS cap. Dev runs are forced windowed, VSync off and uncapped, so they report the settings a normal
    /// run would apply instead.</summary>
    static (int Mode, int Monitor, bool VSync, int Fps) Running()
    {
        var m = Main.I;
        if (m.Dev) return (Math.Min(m.WindowMode, 2), m.Monitor, m.VSync, Math.Max(0, m.FpsCap));
        int mode = DisplayServer.WindowGetMode() switch
        {
            DisplayServer.WindowMode.ExclusiveFullscreen => 0,
            DisplayServer.WindowMode.Fullscreen => 1,
            _ => 2,
        };
        return (mode, DisplayServer.WindowGetCurrentScreen(), DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled, Math.Max(0, Engine.MaxFps));
    }

    static List<Line> Lines()
    {
        var m = Main.I;
        var v = m.Valorant;
        string fov = $"{PlayerView.HipHFov:0}°";
        var list = new List<Line>();
        if (!v.Found)
        {
            list.Add(new(Mark.Info, "VALORANT", "settings not imported, so there is nothing to compare"));
            list.Add(new(Mark.Ok, "FOV", fov, "same as VALORANT"));
            return list;
        }

        // Sensitivity (VALORANT's own number; ValTrainer converts it 1:1) and the 103° FOV, which is fixed like VALORANT's.
        string Sens(float s) => s.ToString("0.###", Inv);
        if (!v.SensFromFile) list.Add(new(Mark.Info, "Sens · FOV", $"{Sens(m.Sens)} · {fov}", "VALORANT's file has no sens"));
        else if (Mathf.Abs(m.Sens - v.Sensitivity) < 0.0005f) list.Add(new(Mark.Ok, "Sens · FOV", $"{Sens(m.Sens)} · {fov}", "same as VALORANT"));
        else list.Add(new(Mark.Differs, "Sens · FOV", $"{Sens(m.Sens)} here · {Sens(v.Sensitivity)} in VALORANT", $"FOV {fov} same"));

        var run = Running();
        int screens = Math.Max(1, DisplayServer.GetScreenCount());
        int vMode = Math.Clamp(v.WindowMode, 0, 2), vMon = Math.Clamp(v.MonitorIndex, 0, screens - 1);
        int vFps = Math.Max(0, (int)MathF.Round(v.FrameRateLimit));
        string Disp(int mode, int mon) => ModeNames[Math.Clamp(mode, 0, 2)] + (screens > 1 ? $", monitor #{mon + 1}" : "");
        if (run.Mode == vMode && (screens < 2 || run.Monitor == vMon)) list.Add(new(Mark.Ok, "Display", Disp(run.Mode, run.Monitor), "same as VALORANT"));
        else list.Add(new(Mark.Differs, "Display", $"{Disp(run.Mode, run.Monitor)} here · {Disp(vMode, vMon).ToLowerInvariant()} in VALORANT"));

        string Sync(bool on, int cap) => $"{(on ? "on" : "off")}, {Fps(cap)}";
        if (run.VSync == v.VSync && run.Fps == vFps) list.Add(new(Mark.Ok, "VSync · FPS cap", Sync(run.VSync, run.Fps), "same as VALORANT"));
        else list.Add(new(Mark.Differs, "VSync · FPS cap", $"{Sync(run.VSync, run.Fps)} here · {Sync(v.VSync, vFps)} in VALORANT"));

        // Neutral notes, never a "fix this".
        if (v.VSync) list.Add(new(Mark.Info, "", "VSync is on in VALORANT. It adds input delay; most players turn it off."));
        else if (v.Reflex >= 1) list.Add(new(Mark.Info, "", "NVIDIA Reflex is on in VALORANT; ValTrainer can't use Reflex."));
        return list;
    }

    static void DrawLine(Control d, Line l, float cy, float k)
    {
        int fs = UiTheme.Fs(15, k), ic = UiTheme.Fs(16, k);
        float x = 0;
        // Shape carries the meaning as well as the colour: ✓ same / fine, ≠ differs, ! worth a look, i a note.
        var (glyph, col) = l.Mark switch
        {
            Mark.Ok => ("✓", UiTheme.Good),
            Mark.Differs => ("≠", UiTheme.Warn),
            Mark.Warn => ("!", UiTheme.Warn),
            _ => ("i", UiTheme.Dim),
        };
        Gfx.TextC(d, UiTheme.Body, glyph, x + 8 * k, Gfx.Mid(cy, ic), ic, col);
        x += 26 * k;
        if (l.Label.Length > 0)
        {
            Gfx.TextFit(d, UiTheme.Body, l.Label, x, Gfx.Mid(cy, fs), fs, UiTheme.Dim, 124 * k);
            x += 130 * k;
        }
        float room = d.Size.X - x;
        var valCol = l.Mark is Mark.Differs or Mark.Warn ? UiTheme.Warn : l.Mark == Mark.Info && l.Label.Length == 0 ? UiTheme.Dim : UiTheme.Text;
        float vw = Mathf.Min(Gfx.TextW(UiTheme.Body, l.Value, fs), room);
        Gfx.TextFit(d, UiTheme.Body, l.Value, x, Gfx.Mid(cy, fs), fs, valCol, room);
        if (l.Note.Length > 0 && room - vw > 40 * k)
            Gfx.TextFit(d, UiTheme.Body, "  ·  " + l.Note, x + vw, Gfx.Mid(cy, fs), fs, UiTheme.Faint, room - vw);
    }
}

/// <summary>A small tick box with a label (mouse only): click anywhere on the row to toggle.</summary>
public partial class CheckRow : Control
{
    public string Text = "";
    public float K = 1f;
    public bool On;
    public Action<bool>? Toggled;
    bool hover;

    public CheckRow()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            On = !On;
            UiTheme.ClickSound();
            Toggled?.Invoke(On);
            QueueRedraw();
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { hover = true; UiTheme.HoverSound(); QueueRedraw(); }
        else if (what == NotificationMouseExit) { hover = false; QueueRedraw(); }
        else if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, s = 15 * k;
        var box = new Rect2(1, cy - s / 2, s, s);
        if (On)
        {
            DrawRect(box, UiTheme.Text);
            Span<Vector2> tick = stackalloc Vector2[] { box.Position + new Vector2(s * 0.22f, s * 0.52f), box.Position + new Vector2(s * 0.42f, s * 0.72f), box.Position + new Vector2(s * 0.8f, s * 0.28f) };
            this.Polyline(tick, UiTheme.Bg, Mathf.Max(1.5f, 2 * k), true);
        }
        else
        {
            DrawRect(box, new Color(0.06f, 0.1f, 0.14f, 0.72f));
            DrawRect(box, new Color(UiTheme.Text, hover ? 0.7f : 0.3f), false, 1f);
        }
        int fs = UiTheme.Fs(15, k);
        float x = s + 12 * k;
        Gfx.TextFit(this, UiTheme.Body, Text, x, Gfx.Mid(cy, fs), fs, On ? UiTheme.Dim : hover ? UiTheme.Text : UiTheme.Text.Lerp(UiTheme.Dim, 0.25f), Size.X - x);
    }
}
