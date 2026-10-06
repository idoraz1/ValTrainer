using Godot;

namespace ValTrainer.UI;

/// <summary>
/// VALORANT-style button drawn in code. Looks: Primary (red fill + hover frame), Secondary (dark plate, thin
/// border, red hover tick), Select (chip/tab: off-white fill with navy text when <see cref="Selected"/>),
/// Ghost (text only) and Arrow (square &lt; / &gt; stepper).
/// </summary>
public partial class VButton : BaseButton
{
    public enum Look { Primary, Secondary, Select, Ghost, Arrow }

    public Look Kind = Look.Secondary;
    public string Label = "";
    public string? Sub;
    public float K = 1f;
    public float FontPx = 20f, SubPx = 13f;
    /// <summary>&gt;= -1 draws a rank emblem on the left.</summary>
    public int Emblem = -99;
    public bool Selected;
    /// <summary>Arrow look: -1 = &lt;, +1 = &gt;.</summary>
    public int Dir = 1;
    public HorizontalAlignment Align = HorizontalAlignment.Center;
    public Font? TitleFont;
    public bool Silent;

    float hover, flash;

    public VButton()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        MouseEntered += () => { if (!Disabled && !Silent) UiTheme.HoverSound(); };
        Pressed += () => { if (!Silent) UiTheme.ClickSound(); flash = 1f; QueueRedraw(); };
    }

    /// <summary>Activates the button as if clicked (used by the in-game overlay, which handles clicks itself).</summary>
    public void Click() => EmitSignal(BaseButton.SignalName.Pressed);

    public void SetSelected(bool on)
    {
        if (Selected == on) return;
        Selected = on;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float target = IsHovered() && !Disabled ? 1f : 0f;
        if (hover != target || flash > 0)
        {
            hover = Mathf.MoveToward(hover, target, (float)delta * 8f);
            flash = Mathf.Max(0, flash - (float)delta * 5f);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        float a = Disabled ? 0.35f : 1f;
        bool down = GetDrawMode() is DrawMode.Pressed or DrawMode.HoverPressed;
        Color textCol = UiTheme.Text;
        float k = K;

        switch (Kind)
        {
            case Look.Primary:
            {
                var fill = UiTheme.Accent.Lerp(UiTheme.AccentHover, hover * 0.45f);
                if (down) fill = UiTheme.AccentDark;
                fill = fill.Lerp(Colors.White, flash * 0.25f);
                float o = 4 * k;
                if (hover > 0.01f)
                {
                    var frame = new Rect2(r.Position - new Vector2(o, o), r.Size + new Vector2(o, o) * 2);
                    DrawRect(frame, new Color(UiTheme.Text, 0.5f * hover * a), false, 1f);
                    Gfx.Brackets(this, frame, 6 * k, new Color(UiTheme.Text, hover * a), Mathf.Max(1, 2 * k));
                }
                DrawRect(r, new Color(fill, a));
                break;
            }
            case Look.Secondary:
            {
                var fill = new Color(0.06f, 0.1f, 0.14f, 0.78f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.16f), hover);
                if (down) fill = new Color(0.93f, 0.91f, 0.88f, 0.26f);
                DrawRect(r, new Color(fill, fill.A * a));
                DrawRect(r, new Color(UiTheme.Text, (0.2f + 0.5f * hover) * a), false, 1f);
                if (hover > 0.01f) DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y * hover), UiTheme.Accent);
                break;
            }
            case Look.Select:
            {
                if (Selected)
                {
                    DrawRect(r, new Color(UiTheme.Text, a));
                    DrawRect(new Rect2(0, r.Size.Y - 3 * k, r.Size.X, 3 * k), UiTheme.Accent);
                    textCol = UiTheme.Bg;
                }
                else
                {
                    var fill = new Color(0.06f, 0.1f, 0.14f, 0.72f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.14f), hover);
                    DrawRect(r, new Color(fill, fill.A * a));
                    DrawRect(r, new Color(UiTheme.Text, (0.14f + 0.4f * hover) * a), false, 1f);
                }
                break;
            }
            case Look.Ghost:
                textCol = UiTheme.Dim.Lerp(UiTheme.Text, hover);
                if (hover > 0.01f) DrawRect(new Rect2(0, r.Size.Y - 2 * k, r.Size.X * hover, 2 * k), UiTheme.Accent);
                break;
            case Look.Arrow:
            {
                DrawRect(r, new Color(UiTheme.Accent, (0.9f * hover + flash * 0.1f) * a));
                DrawRect(r, new Color(UiTheme.Text, (0.22f + 0.4f * hover) * a), false, 1f);
                Gfx.Chevron(this, r.GetCenter(), r.Size.Y * 0.36f, Dir, new Color(UiTheme.Text, a), Mathf.Max(1.5f, r.Size.Y * 0.09f));
                return;
            }
        }

        textCol = new Color(textCol, textCol.A * a);
        var font = TitleFont ?? UiTheme.Display;
        int fs = UiTheme.Fs(FontPx, k), ss = UiTheme.Fs(SubPx, k);
        float x = 0, w = r.Size.X;
        float pad = 14 * k;
        if (Emblem >= -1)
        {
            float er = Mathf.Min(r.Size.Y * 0.3f, 20 * k);
            RankEmblem.Draw(this, new Vector2(pad + er, r.Size.Y / 2), er, Emblem, a);
            x = pad + er * 2 + 10 * k;
            w = r.Size.X - x - pad;
        }
        else if (Align != HorizontalAlignment.Center) { x = pad; w = r.Size.X - pad * 2; }

        if (Sub == null)
        {
            Gfx.TextFit(this, font, Label, x, Gfx.Mid(r.Size.Y / 2, fs), fs, textCol, w, Emblem >= -1 ? HorizontalAlignment.Left : Align);
        }
        else
        {
            var align = Emblem >= -1 ? HorizontalAlignment.Left : Align;
            float total = fs * 0.72f + 7 * k + ss * 0.72f, top = (r.Size.Y - total) / 2;
            Gfx.TextFit(this, font, Label, x, top + fs * 0.72f, fs, textCol, w, align);
            var subCol = Kind == Look.Select && Selected ? new Color(UiTheme.Bg, 0.75f * a) : new Color(UiTheme.Dim, a);
            Gfx.TextFit(this, UiTheme.Body, Sub, x, top + total, ss, subCol, w, align);
        }
    }
}

/// <summary>Angular translucent panel with an optional uppercase title bar. Children are laid out inside the margins.</summary>
public partial class VPanel : MarginContainer
{
    public string Title = "";
    public string? Caption;
    public float K = 1f;
    public float Pad = 22f;
    public Color Fill = new(0.07f, 0.11f, 0.15f, 0.84f);

    public float TitleH => Title.Length > 0 ? 56 * K : 0;

    public override void _Ready()
    {
        int p = (int)(Pad * K);
        AddThemeConstantOverride("margin_left", p);
        AddThemeConstantOverride("margin_right", p);
        AddThemeConstantOverride("margin_top", (int)(Title.Length > 0 ? TitleH + 6 * K : p));
        AddThemeConstantOverride("margin_bottom", p);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        Gfx.Plate(this, r, 16 * k, Fill, new Color(UiTheme.Text, 0.1f));
        DrawRect(new Rect2(0, 0, 34 * k, 2 * k), UiTheme.Accent);
        if (Title.Length == 0) return;
        float cy = 28 * k;
        int fs = UiTheme.Fs(19, k);
        DrawRect(new Rect2(Pad * k, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
        Gfx.Text(this, UiTheme.DisplayWide, Title, Pad * k + 16 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text);
        if (Caption != null)
        {
            int cs = UiTheme.Fs(13, k);
            Gfx.TextR(this, UiTheme.Body, Caption, r.Size.X - Pad * k, Gfx.Mid(cy, cs), cs, UiTheme.Dim);
        }
        DrawRect(new Rect2(Pad * k, TitleH - 2 * k, r.Size.X - Pad * k * 2, 1), new Color(UiTheme.Text, 0.09f));
    }
}

/// <summary>Small tracked-out caption with a red tick ("DIFFICULTY TIER") and an optional dim note on the right.</summary>
public partial class SectionLabel : Control
{
    public string Text = "";
    public string? Note;
    public float K = 1f;

    public SectionLabel() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Ready() => CustomMinimumSize = new Vector2(CustomMinimumSize.X, Mathf.Max(CustomMinimumSize.Y, 30 * K));

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2;
        int fs = UiTheme.Fs(14, k);
        DrawRect(new Rect2(0, cy - 3 * k, 6 * k, 6 * k), UiTheme.Accent);
        float w = Gfx.TextW(UiTheme.HudWide, Text, fs);
        Gfx.Text(this, UiTheme.HudWide, Text, 14 * k, Gfx.Mid(cy, fs), fs, UiTheme.Dim);
        float lineX = 14 * k + w + 14 * k;
        if (Note != null)
        {
            int ns = UiTheme.Fs(13, k);
            float nw = Gfx.TextR(this, UiTheme.Body, Note, Size.X, Gfx.Mid(cy, ns), ns, UiTheme.Faint);
            if (Size.X - nw - 14 * k > lineX) DrawRect(new Rect2(lineX, cy, Size.X - nw - 14 * k - lineX, 1), new Color(UiTheme.Text, 0.08f));
        }
        else if (Size.X > lineX) DrawRect(new Rect2(lineX, cy, Size.X - lineX, 1), new Color(UiTheme.Text, 0.08f));
    }
}

/// <summary>Draws one rank emblem centred in its rect.</summary>
public partial class EmblemView : Control
{
    public int Tier = -1;
    public float Fill = 0.45f;
    public EmblemView() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => RankEmblem.Draw(this, Size / 2, Mathf.Min(Size.X, Size.Y) * Fill, Tier);
}

/// <summary>Menu background: navy gradient, faint diagonal hatching and a red light slash (no Riot art).</summary>
public partial class Backdrop : Control
{
    public Backdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Size;
        float k = UiTheme.S(s);
        Gfx.VGradient(this, new Rect2(Vector2.Zero, s), Color.Color8(20, 32, 44), Color.Color8(11, 18, 26));
        // soft red glow top-right
        Span<Vector2> p = stackalloc Vector2[4];
        Span<Color> c = stackalloc Color[4];
        var glow = new Color(UiTheme.Accent, 0.07f);
        var clear = new Color(UiTheme.Accent, 0f);
        p[0] = new(s.X * 0.62f, 0); p[1] = new(s.X, 0); p[2] = new(s.X, s.Y * 0.55f); p[3] = new(s.X * 0.4f, s.Y * 0.55f);
        c[0] = clear; c[1] = glow; c[2] = clear; c[3] = clear;
        this.GradPoly(p, c);
        // diagonal hatching
        float step = 46 * k;
        var line = new Color(1, 1, 1, 0.018f);
        for (float x = -s.Y; x < s.X; x += step) DrawLine(new Vector2(x, s.Y), new Vector2(x + s.Y * 0.58f, 0), line, 1f);
        // red slash
        p[0] = new(s.X * 0.70f, s.Y); p[1] = new(s.X * 0.70f + 26 * k, s.Y); p[2] = new(s.X * 0.70f + 26 * k + s.Y * 0.58f, 0); p[3] = new(s.X * 0.70f + s.Y * 0.58f, 0);
        this.FillPoly(p, new Color(UiTheme.Accent, 0.045f));
        // bottom fade
        Gfx.VGradient(this, new Rect2(0, s.Y * 0.7f, s.X, s.Y * 0.3f), new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.25f));
    }
}

/// <summary>Mini line chart of recent scores.</summary>
public static class Sparkline
{
    public static void Draw(CanvasItem ci, Rect2 r, IReadOnlyList<float> v, float k, Color line)
    {
        if (v.Count == 0 || r.Size.X < 10) return;
        float min = float.MaxValue, max = float.MinValue;
        int best = 0;
        for (int i = 0; i < v.Count; i++)
        {
            if (v[i] < min) min = v[i];
            if (v[i] > max) { max = v[i]; best = i; }
        }
        float span = Mathf.Max(1e-3f, max - min);
        // baseline + mid guide
        ci.DrawRect(new Rect2(r.Position.X, r.End.Y, r.Size.X, 1), new Color(UiTheme.Text, 0.08f));
        Vector2 P(int i) => new(v.Count == 1 ? r.GetCenter().X : r.Position.X + i * r.Size.X / (v.Count - 1),
                                 max == min ? r.GetCenter().Y : r.End.Y - (v[i] - min) / span * r.Size.Y);
        if (v.Count >= 2)
        {
            Span<Vector2> pts = stackalloc Vector2[v.Count];
            Span<Vector2> area = stackalloc Vector2[v.Count + 2];
            for (int i = 0; i < v.Count; i++) { pts[i] = P(i); area[i] = pts[i]; }
            area[v.Count] = new Vector2(r.End.X, r.End.Y);
            area[v.Count + 1] = new Vector2(r.Position.X, r.End.Y);
            Span<Color> cols = stackalloc Color[v.Count + 2];
            for (int i = 0; i < v.Count; i++) cols[i] = new Color(line, 0.22f);
            cols[v.Count] = new Color(line, 0f); cols[v.Count + 1] = new Color(line, 0f);
            try { ci.GradPoly(area, cols); } catch { /* degenerate area: skip fill */ }
            ci.Polyline(pts, line, Mathf.Max(1.5f, 2f * k), true);
        }
        Gfx.Diamond(ci, P(best), 5 * k, 5 * k, UiTheme.Good); // best: a diamond, latest: a dot (shape as well as colour)
        ci.DrawCircle(P(v.Count - 1), 3.5f * k, UiTheme.Text);
    }
}

/// <summary>
/// Base for full-screen menus: rebuilds its node tree whenever the window size changes (so every pixel size
/// is computed from the 1080p design scale and text stays crisp), adds the backdrop, theme and Esc handling.
/// </summary>
public abstract partial class ScreenBase : Control
{
    protected float K = 1f;
    Vector2 builtFor;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (GetViewportRect().Size != builtFor) Rebuild();
        Tick((float)delta);
    }

    protected virtual void Tick(float dt) { }

    protected void Rebuild()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        builtFor = GetViewportRect().Size;
        K = UiTheme.Fit(builtFor);
        Theme = UiTheme.MakeTheme(K);
        AddChild(new Backdrop());
        Build();
    }

    protected abstract void Build();

    /// <summary>Esc behaviour; default goes back to the menu.</summary>
    protected virtual void Back() => Main.I.ShowMenu();

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Back();
        }
    }

    // ---- small builders ----

    protected VButton Btn(string label, VButton.Look look, Action onClick, float w = 0, float h = 52, float fontPx = 20)
    {
        var b = new VButton { Label = label, Kind = look, K = K, FontPx = fontPx, CustomMinimumSize = new Vector2(w * K, h * K) };
        b.Pressed += onClick;
        return b;
    }

    protected Label Lbl(string text, Font f, float px, Color c, HorizontalAlignment align = HorizontalAlignment.Left) =>
        new() { Text = text, LabelSettings = UiTheme.Label(f, px, K, c), HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };

    protected static MarginContainer Margins(Control child, float l, float t, float r, float b)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", (int)l);
        m.AddThemeConstantOverride("margin_top", (int)t);
        m.AddThemeConstantOverride("margin_right", (int)r);
        m.AddThemeConstantOverride("margin_bottom", (int)b);
        m.AddChild(child);
        return m;
    }

    protected static VBoxContainer VBox(float sep) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", (int)sep); return v; }
    protected static HBoxContainer HBox(float sep) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", (int)sep); return h; }

    protected static Control Spacer(float w, float h) => new() { CustomMinimumSize = new Vector2(w, h), MouseFilter = MouseFilterEnum.Ignore };

    protected static Control Expander() => new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };

    /// <summary>Screen title block: big uppercase heading + dim subtitle, used by Settings and Stats.</summary>
    protected Control Heading(string title, string sub)
    {
        var v = VBox(2 * K);
        v.AddChild(Lbl(title, UiTheme.Display, 64, UiTheme.Text));
        v.AddChild(Lbl(sub, UiTheme.HudWide, 15, UiTheme.Dim));
        return v;
    }
}

/// <summary>A Control whose drawing is supplied by a delegate (one-off decorations).</summary>
public partial class DrawBox : Control
{
    public Action<DrawBox>? OnDraw;
    public DrawBox() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => OnDraw?.Invoke(this);
    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }
}

/// <summary>Read-only "label ........ value" row (values are pulled through delegates so <see cref="Control.QueueRedraw"/> refreshes them).</summary>
public partial class InfoRow : Control
{
    public string Label = "";
    public Func<string> Value = () => "";
    public Func<Color>? ValueColor;
    public Func<Color?>? Swatch;
    public Func<string?>? Tag;
    public float K = 1f;
    public float H = 34f;
    public bool Stripe;

    public InfoRow() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, H * K);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public override void _Draw()
    {
        float k = K, cy = Size.Y / 2, pad = 10 * k;
        if (Stripe) DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.025f));
        int ls = UiTheme.Fs(15, k), vs = UiTheme.Fs(16, k);
        float lw = Gfx.TextW(UiTheme.Body, Label, ls);
        Gfx.Text(this, UiTheme.Body, Label, pad, Gfx.Mid(cy, ls), ls, UiTheme.Dim);
        float right = Size.X - pad;
        string v = Value();
        float maxW = Mathf.Max(20, right - (pad + lw + 16 * k));
        float vw = Mathf.Min(Gfx.TextW(UiTheme.Body, v, vs), maxW);
        Gfx.TextFit(this, UiTheme.Body, v, right - maxW, Gfx.Mid(cy, vs), vs, ValueColor?.Invoke() ?? UiTheme.Text, maxW, HorizontalAlignment.Right);
        float x = right - vw - 10 * k;
        if (Swatch?.Invoke() is { } sw)
        {
            float s = 12 * k;
            DrawRect(new Rect2(x - s, cy - s / 2, s, s), sw);
            DrawRect(new Rect2(x - s, cy - s / 2, s, s), new Color(1, 1, 1, 0.4f), false, 1f);
            x -= s + 10 * k;
        }
        if (Tag?.Invoke() is { } tag)
        {
            int ts = UiTheme.Fs(11, k);
            float tw = Gfx.TextW(UiTheme.HudWide, tag, ts) + 12 * k;
            var tr = new Rect2(x - tw, cy - 9 * k, tw, 18 * k);
            DrawRect(tr, new Color(UiTheme.Warn, 0.12f));
            DrawRect(tr, new Color(UiTheme.Warn, 0.7f), false, 1f);
            Gfx.Text(this, UiTheme.HudWide, tag, tr.Position.X + 6 * k, Gfx.Mid(cy, ts), ts, UiTheme.Warn);
        }
    }
}
