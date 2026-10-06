using Godot;
using ValTrainer.UI;

namespace ValTrainer.Warmup;

/// <summary>
/// CHECK-IN (about 30 s, first step of a Lock-In): energy 1–9 (flat ↔ wired), confidence 1–9 (shaky ↔ sure), an optional
/// "tilted" chip, one process goal and one cue word (pick or type), and the breathing guide for the end (suggested when
/// energy is 7+). Everyone gets the reappraisal line; flat and tilted players get one extra line. Nothing is scored.
/// Keyboard: ↑↓ pick a row, ←→ or 1–9 change it, T tilted, B breathing, Space / Enter continue, Esc back.
/// </summary>
public partial class LockInCheckIn : ScreenBase
{
    readonly WarmupRunner run;
    readonly CheckIn c;
    bool breathTouched, done;
    int row;
    string goalPick, cuePick;

    readonly List<ScaleBar> bars = new();
    readonly List<(VButton B, string Text)> goalChips = new(), cueChips = new();
    readonly List<(VButton B, int Secs)> breathChips = new();
    VButton? tiltChip;
    LineEdit? goalEdit, cueEdit;
    DrawBox? lines, goalBox, cueBox;

    public LockInCheckIn(WarmupRunner run)
    {
        this.run = run;
        var s = WarmupStore.I;
        c = new CheckIn
        {
            Energy = Math.Clamp(s.Energy, 1, 9), Confidence = Math.Clamp(s.Confidence, 1, 9),
            Goal = s.Goal.Length > 0 ? s.Goal : LockInText.Goals[0], Cue = s.Cue.Length > 0 ? s.Cue : LockInText.Cues[0],
        };
        // Dev "--lockin-prefill E,C[,tilted]": open with these values (layout checks of the extra lines).
        if (Main.I.Dev && Core.CmdLine.After("--lockin-prefill")?.Split(',') is { Length: >= 2 } pf)
        {
            if (int.TryParse(pf[0], out var e)) c.Energy = Math.Clamp(e, 1, 9);
            if (int.TryParse(pf[1], out var q)) c.Confidence = Math.Clamp(q, 1, 9);
            c.Tilted = pf.Length > 2 && pf[2].StartsWith('t');
        }
        c.BreathSeconds = LockInText.WantsBreathing(c.Energy) ? BreathDefault : 0;
        goalPick = LockInText.Goals.Contains(c.Goal) ? c.Goal : LockInText.Goals[0];
        cuePick = LockInText.Cues.Contains(c.Cue) ? c.Cue : LockInText.Cues[0];
    }

    static int BreathDefault => LockInText.BreathOptions.Contains(WarmupStore.I.BreathSeconds) ? WarmupStore.I.BreathSeconds : 90;

    protected override void Back()
    {
        if (done) return;
        done = true;
        bool agents = Main.I.ReturnToAgents;
        WarmupRunner.Abandon();
        if (agents) Main.I.ShowMenu(); // back to the agent page
        else Main.I.ShowLockIn();
    }

    void Continue()
    {
        if (done) return;
        done = true;
        c.Goal = Clean(goalEdit?.Text, LockInText.MaxGoal) is { Length: > 0 } g ? g : goalPick;
        c.Cue = Clean(cueEdit?.Text, LockInText.MaxCue) is { Length: > 0 } q ? q : cuePick;
        Callable.From(() => run.CheckInDone(c)).CallDeferred();
    }

    static string Clean(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length > max ? s[..max].Trim() : s;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } key && !done)
        {
            bool handled = true;
            switch (key.Keycode)
            {
                case Key.Space or Key.Enter or Key.KpEnter when !key.Echo: Continue(); break;
                case Key.Up: SetRow(row - 1); break;
                case Key.Down: SetRow(row + 1); break;
                case Key.Left: Step(-1); break;
                case Key.Right: Step(1); break;
                case Key.T when !key.Echo: ToggleTilt(); break;
                case Key.B when !key.Echo: CycleBreath(); break;
                case >= Key.Key1 and <= Key.Key9 when row < 2: bars[row].Set((int)(key.Keycode - Key.Key0)); break;
                case >= Key.Kp1 and <= Key.Kp9 when row < 2: bars[row].Set((int)(key.Keycode - Key.Kp0)); break;
                default: handled = false; break;
            }
            if (handled) { GetViewport().SetInputAsHandled(); return; }
        }
        base._UnhandledKeyInput(e);
    }

    void SetRow(int r)
    {
        row = Math.Clamp(r, 0, 3);
        if (IsInsideTree()) GetViewport().GuiReleaseFocus(); // keys go to the rows again, not to a text box
        for (int i = 0; i < bars.Count; i++) { bars[i].Active = i == row; bars[i].QueueRedraw(); }
        goalBox?.QueueRedraw();
        cueBox?.QueueRedraw();
    }

    void Step(int d)
    {
        switch (row)
        {
            case 0 or 1: bars[row].Set(bars[row].Value + d); break;
            case 2: PickGoal(Cycle(LockInText.Goals, goalPick, d)); break;
            case 3: PickCue(Cycle(LockInText.Cues, cuePick, d)); break;
        }
    }

    static string Cycle(string[] list, string cur, int d)
    {
        int i = Array.IndexOf(list, cur);
        return list[((i < 0 ? 0 : i + d) % list.Length + list.Length) % list.Length];
    }

    void PickGoal(string g)
    {
        goalPick = g;
        if (goalEdit != null) goalEdit.Text = "";
        foreach (var (b, t) in goalChips) b.SetSelected(t == g);
    }

    void PickCue(string q)
    {
        cuePick = q;
        if (cueEdit != null) cueEdit.Text = "";
        foreach (var (b, t) in cueChips) b.SetSelected(t == q);
    }

    void ToggleTilt()
    {
        c.Tilted = !c.Tilted;
        tiltChip?.SetSelected(c.Tilted);
        UiTheme.ClickSound();
        lines?.QueueRedraw();
    }

    void SetBreath(int secs, bool byPlayer)
    {
        if (byPlayer) breathTouched = true;
        c.BreathSeconds = secs;
        foreach (var (b, s) in breathChips) b.SetSelected(s == secs);
    }

    void CycleBreath()
    {
        var opts = new[] { 0 }.Concat(LockInText.BreathOptions).ToArray();
        int i = Array.IndexOf(opts, c.BreathSeconds);
        SetBreath(opts[(i + 1) % opts.Length], true);
        UiTheme.ClickSound();
    }

    protected override void Build()
    {
        float k = K;
        bars.Clear(); goalChips.Clear(); cueChips.Clear(); breathChips.Clear();
        var root = Margins(VBox(18 * k), 56 * k, 30 * k, 56 * k, 34 * k);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        var col = (VBoxContainer)root.GetChild(0);

        var head = HBox(16 * k);
        string who = run.Plan.Preset.Key.StartsWith(Agents.AgentRoutines.Prefix) ? $"LOCK IN AS {run.Plan.Preset.Name.ToUpperInvariant()}" : run.Plan.Preset.Name.ToUpperInvariant();
        var title = Heading("CHECK-IN", $"{who} · 30 SECONDS · HOW YOU FEEL RIGHT NOW · NOTHING HERE IS SCORED");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var back = Btn("BACK", VButton.Look.Secondary, Back, 160, 52, 22);
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(back);
        col.AddChild(head);

        var body = HBox(48 * k);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(body);

        // ---- left: how you feel ----
        var left = VBox(12 * k);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(left);
        ScaleBar Bar(string label, string note, string lo, string hi, int value, Action<int> changed)
        {
            left.AddChild(new SectionLabel { Text = label, Note = note, K = k });
            var b = new ScaleBar { K = k, Value = value, LeftLabel = lo, RightLabel = hi, CustomMinimumSize = new Vector2(0, 84 * k), Changed = changed };
            int idx = bars.Count;
            b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) SetRow(idx); };
            bars.Add(b);
            left.AddChild(b);
            return b;
        }
        Bar("ENERGY", "how wound up you feel", "FLAT", "WIRED", c.Energy, v =>
        {
            c.Energy = v;
            if (!breathTouched) SetBreath(LockInText.WantsBreathing(v) ? BreathDefault : 0, false);
            lines?.QueueRedraw();
        });
        left.AddChild(Spacer(0, 4 * k));
        Bar("CONFIDENCE", "how sure your aim feels today", "SHAKY", "SURE", c.Confidence, v => c.Confidence = v);
        left.AddChild(Spacer(0, 4 * k));

        var opts = HBox(10 * k);
        tiltChip = new VButton
        {
            Kind = VButton.Look.Select, K = k, Label = "TILTED OR ANNOYED", Sub = "optional · T", FontPx = 17, SubPx = 12, Selected = c.Tilted,
            CustomMinimumSize = new Vector2(250 * k, 58 * k),
        };
        tiltChip.Pressed += ToggleTilt;
        opts.AddChild(tiltChip);
        opts.AddChild(Spacer(14 * k, 0));
        var bl = new DrawBox
        {
            CustomMinimumSize = new Vector2(150 * k, 58 * k),
            OnDraw = d =>
            {
                int fs = UiTheme.Fs(13, k), ss = UiTheme.Fs(12, k);
                Gfx.Text(d, UiTheme.HudWide, "BREATHING GUIDE", 0, Gfx.Mid(20 * k, fs), fs, UiTheme.Dim);
                Gfx.Text(d, UiTheme.Body, "at the end · B", 0, Gfx.Mid(40 * k, ss), ss, UiTheme.Faint);
            },
        };
        opts.AddChild(bl);
        foreach (int secs in new[] { 0 }.Concat(LockInText.BreathOptions))
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = secs == 0 ? "OFF" : $"{secs} S", FontPx = 17, Selected = c.BreathSeconds == secs,
                CustomMinimumSize = new Vector2(0, 58 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = secs == 0 ? "No breathing guide" : $"{secs} s of slow breathing (4 s in, 6 s out) before your goal card",
            };
            int s = secs;
            b.Pressed += () => SetBreath(s, true);
            breathChips.Add((b, s));
            opts.AddChild(b);
        }
        left.AddChild(opts);
        lines = new DrawBox { CustomMinimumSize = new Vector2(0, 150 * k), SizeFlagsVertical = SizeFlags.ExpandFill, OnDraw = DrawLines };
        left.AddChild(lines);

        // ---- right: goal and cue ----
        var right = VBox(10 * k);
        right.CustomMinimumSize = new Vector2(760 * k, 0);
        body.AddChild(right);
        goalBox = PickRow(right, "ONE GOAL FOR YOUR GAMES", "something you do, not a result", 2, LockInText.Goals, goalChips, goalPick, PickGoal, 2);
        goalEdit = Field(right, "Or type your own goal", LockInText.MaxGoal, LockInText.Goals.Contains(c.Goal) ? "" : c.Goal, t =>
        {
            foreach (var (b, g) in goalChips) b.SetSelected(t.Trim().Length == 0 && g == goalPick);
        });
        right.AddChild(Spacer(0, 14 * k));
        cueBox = PickRow(right, "ONE CUE WORD", "say it before a fight", 3, LockInText.Cues, cueChips, cuePick, PickCue, 3);
        cueEdit = Field(right, "Or type your own cue", LockInText.MaxCue, LockInText.Cues.Contains(c.Cue) ? "" : c.Cue, t =>
        {
            foreach (var (b, q) in cueChips) b.SetSelected(t.Trim().Length == 0 && q == cuePick);
        });
        if (goalEdit.Text.Length > 0) foreach (var (b, _) in goalChips) b.SetSelected(false);
        if (cueEdit.Text.Length > 0) foreach (var (b, _) in cueChips) b.SetSelected(false);

        // ---- continue ----
        var go = HBox(20 * k);
        var cont = Btn("CONTINUE  (SPACE)", VButton.Look.Primary, Continue, 340, 62, 24);
        go.AddChild(cont);
        var hint = Lbl("↑↓ pick a row · ←→ or 1–9 change it · Space continues", UiTheme.Body, 15, UiTheme.Faint);
        hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        go.AddChild(hint);
        col.AddChild(go);
        SetRow(row);
    }

    DrawBox PickRow(VBoxContainer parent, string label, string note, int cols, string[] items, List<(VButton, string)> chips, string picked, Action<string> pick, int myRow)
    {
        float k = K;
        var lbl = new DrawBox { CustomMinimumSize = new Vector2(0, 30 * k) };
        lbl.OnDraw = d =>
        {
            // a SectionLabel that also shows the keyboard row marker
            float cy = d.Size.Y / 2;
            int fs = UiTheme.Fs(14, K), ns = UiTheme.Fs(13, K);
            d.DrawRect(new Rect2(0, cy - 3 * K, 6 * K, 6 * K), UiTheme.Accent);
            Gfx.Text(d, UiTheme.HudWide, label, 14 * K, Gfx.Mid(cy, fs), fs, row == myRow ? UiTheme.Text : UiTheme.Dim);
            Gfx.TextR(d, UiTheme.Body, note, d.Size.X, Gfx.Mid(cy, ns), ns, UiTheme.Faint);
            if (row == myRow) Gfx.Chevron(d, new Vector2(-12 * K, cy), 7 * K, 1, UiTheme.Accent, Mathf.Max(1.5f, 2 * K));
        };
        parent.AddChild(lbl);
        var grid = new GridContainer { Columns = cols };
        grid.AddThemeConstantOverride("h_separation", (int)(8 * k));
        grid.AddThemeConstantOverride("v_separation", (int)(8 * k));
        foreach (var it in items)
        {
            var b = new VButton
            {
                Kind = VButton.Look.Select, K = k, Label = it, TitleFont = UiTheme.Body, FontPx = 17, Selected = it == picked,
                CustomMinimumSize = new Vector2(0, 50 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            string t = it;
            b.Pressed += () => { pick(t); SetRow(myRow); };
            chips.Add((b, it));
            grid.AddChild(b);
        }
        parent.AddChild(grid);
        return lbl;
    }

    LineEdit Field(VBoxContainer parent, string placeholder, int max, string text, Action<string> changed)
    {
        float k = K;
        var e = new LineEdit
        {
            PlaceholderText = placeholder, MaxLength = max, Text = text, CustomMinimumSize = new Vector2(0, 46 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ContextMenuEnabled = false,
        };
        LockInText.StyleField(e, k);
        e.TextChanged += t => changed(t);
        e.TextSubmitted += _ => e.ReleaseFocus();
        parent.AddChild(e);
        return e;
    }

    void DrawLines(DrawBox d)
    {
        float k = K, w = d.Size.X, y = 22 * k;
        int fs = UiTheme.Fs(19, k), ss = UiTheme.Fs(16, k);
        void Line(string text, Color col, int size, Color mark)
        {
            d.DrawRect(new Rect2(0, y - 15 * k, 3 * k, 28 * k), mark);
            d.DrawMultilineString(UiTheme.Body, new Vector2(16 * k, y + 2 * k), text, HorizontalAlignment.Left, w - 16 * k, size, 2, col);
            y += (Gfx.TextW(UiTheme.Body, text, size) > w - 16 * k ? 2 : 1) * size * 1.3f + 16 * k;
        }
        Line(LockInText.Reappraisal, UiTheme.Text, fs, UiTheme.Accent);
        if (c.Energy <= 3) Line(LockInText.FlatTip, UiTheme.Dim, ss, UiTheme.Teal);
        if (c.Tilted) Line(LockInText.TiltLine, UiTheme.Dim, ss, UiTheme.Warn);
    }
}
