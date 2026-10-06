using Godot;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>The Lock-In's fixed wording: goal and cue suggestions and the check-in lines. Plain and honest: no claim that
/// any of it changes aim or results.</summary>
public static class LockInText
{
    /// <summary>Process goals (what to do, not what to win).</summary>
    public static readonly string[] Goals =
    {
        "Crosshair at head height", "Clear one angle at a time", "Trade my entry",
        "Stop before I shoot", "Comms first", "Play my role",
    };

    /// <summary>Cue words, aimed at the crosshair and the fight rather than the hand.</summary>
    public static readonly string[] Cues = { "Head level", "Smooth", "Patient", "Crisp", "Stop. Shoot.", "Calm" };

    public const string Reappraisal = "Nerves are your body getting ready. Use them.";
    public const string FlatTip = "Feeling flat? Put on your own up-tempo music for the first drills. Turn it off before the deathmatch.";
    public const string TiltLine = "Tilted? Maybe take a 10-minute break before ranked. Your call.";
    public const string BreathLine = "Breathe in through your nose… long slow breath out.";

    public const int MaxGoal = 40, MaxCue = 18;
    public static readonly int[] BreathOptions = { 60, 90, 120 };

    /// <summary>The breathing guide is suggested only when the player feels wired (energy 7 or more).</summary>
    public static bool WantsBreathing(int energy) => energy >= 7;

    /// <summary>Text field look shared by the check-in's "type your own" boxes.</summary>
    public static void StyleField(LineEdit e, float k)
    {
        StyleBoxFlat Box(Color border, bool fill) => new()
        {
            BgColor = new Color(0.06f, 0.1f, 0.14f, 0.85f), DrawCenter = fill, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14 * k, ContentMarginRight = 8 * k, ContentMarginTop = 4 * k, ContentMarginBottom = 4 * k,
        };
        e.AddThemeStyleboxOverride("normal", Box(new Color(UiTheme.Text, 0.2f), true));
        e.AddThemeStyleboxOverride("focus", Box(UiTheme.Accent, false));
        e.AddThemeFontOverride("font", UiTheme.Body);
        e.AddThemeFontSizeOverride("font_size", UiTheme.Fs(17, k));
        e.AddThemeColorOverride("font_color", UiTheme.Text);
        e.AddThemeColorOverride("font_placeholder_color", UiTheme.Faint);
        e.AddThemeColorOverride("caret_color", UiTheme.Accent);
        e.AddThemeColorOverride("selection_color", new Color(UiTheme.Accent, 0.35f));
    }
}

/// <summary>
/// A 1–9 rating as nine segments (no slider widget in the app): click or drag to pick, and the end labels say what
/// the ends mean ("FLAT" … "WIRED"). <see cref="Active"/> marks the row the keyboard edits.
/// </summary>
public partial class ScaleBar : Control
{
    public int Value = 5;
    public const int Min = 1, Max = 9;
    public string LeftLabel = "", RightLabel = "";
    public float K = 1f;
    public bool Active;
    public Action<int>? Changed;
    int hoverCell = -1;
    bool dragging;

    public ScaleBar()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusMode = FocusModeEnum.None;
    }

    float CellsH => Size.Y - 24 * K;

    int CellAt(Vector2 p)
    {
        if (p.Y < 0 || p.Y > CellsH) return -1;
        int i = (int)(p.X / Mathf.Max(1, Size.X) * (Max - Min + 1));
        return Math.Clamp(i, 0, Max - Min);
    }

    public void Set(int v)
    {
        v = Math.Clamp(v, Min, Max);
        if (v == Value) return;
        Value = v;
        UiTheme.ClickSound();
        QueueRedraw();
        Changed?.Invoke(v);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            dragging = mb.Pressed;
            if (mb.Pressed && CellAt(mb.Position) is var c and >= 0) { Set(Min + c); AcceptEvent(); }
        }
        else if (e is InputEventMouseMotion mm)
        {
            int c = CellAt(mm.Position);
            if (dragging) { if (c >= 0) Set(Min + c); }
            if (c != hoverCell) { hoverCell = c; QueueRedraw(); }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { hoverCell = -1; dragging = false; QueueRedraw(); }
        else if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        float k = K, h = CellsH, gap = 4 * k;
        int n = Max - Min + 1;
        float w = (Size.X - gap * (n - 1)) / n;
        int fs = UiTheme.Fs(24, k), ls = UiTheme.Fs(12, k);
        for (int i = 0; i < n; i++)
        {
            var r = new Rect2(i * (w + gap), 0, w, h);
            bool sel = Min + i == Value, filled = Min + i <= Value;
            var fill = sel ? UiTheme.Text : filled ? new Color(UiTheme.Text, 0.16f) : new Color(0.06f, 0.1f, 0.14f, 0.72f);
            if (!sel && hoverCell == i) fill = fill.Lerp(new Color(UiTheme.Text, 0.3f), 0.5f);
            DrawRect(r, fill);
            if (!sel) DrawRect(r, new Color(UiTheme.Text, hoverCell == i ? 0.5f : 0.14f), false, 1f);
            else DrawRect(new Rect2(r.Position.X, r.End.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
            Gfx.TextC(this, UiTheme.Display, (Min + i).ToString(), r.GetCenter().X, Gfx.Mid(r.GetCenter().Y, fs), fs, sel ? UiTheme.Bg : UiTheme.Dim);
        }
        if (Active) Gfx.Brackets(this, new Rect2(-4 * k, -4 * k, Size.X + 8 * k, h + 8 * k), 8 * k, UiTheme.Text, Mathf.Max(1, 2 * k));
        float ly = h + 17 * k;
        Gfx.Text(this, UiTheme.HudWide, LeftLabel, 0, ly, ls, UiTheme.Faint);
        Gfx.TextR(this, UiTheme.HudWide, RightLabel, Size.X, ly, ls, UiTheme.Faint);
    }
}
