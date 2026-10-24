using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>
/// Settings → KEYBINDS: every action ValTrainer uses with its primary and secondary bind and where each comes from
/// (VALORANT's import, or a ValTrainer custom bind). Click a bind, then press any key or mouse button (thumb buttons and the
/// wheel too); Esc cancels, Backspace / Delete puts VALORANT's bind back, right-click unbinds the slot in ValTrainer.
/// Binds shared by several actions are listed (all of them trigger, as in VALORANT). Custom binds live only in ValTrainer's
/// settings.json (<see cref="AppSettings.CustomBinds"/>) and apply on top of the import everywhere; VALORANT is never written.
/// </summary>
public partial class SettingsScreen
{
    static readonly (string Title, GameAction[] Actions)[] BindGroupsLeft =
    {
        ("MOVEMENT", new[] { GameAction.MoveForward, GameAction.MoveBack, GameAction.StrafeLeft, GameAction.StrafeRight, GameAction.Walk, GameAction.Crouch, GameAction.Jump }),
        ("COMBAT", new[] { GameAction.Fire, GameAction.AltFire, GameAction.Reload }),
    };

    static readonly (string Title, GameAction[] Actions)[] BindGroupsRight =
    {
        ("ABILITIES", new[] { GameAction.AbilityGrenade, GameAction.Ability1, GameAction.Ability2, GameAction.Ultimate }),
        ("EQUIPMENT", new[] { GameAction.EquipPrimary, GameAction.EquipSecondary, GameAction.EquipMelee, GameAction.EquipSpike }),
        ("INTERACT", new[] { GameAction.Use, GameAction.UseSpike, GameAction.Drop, GameAction.Inspect }),
    };

    /// <summary>The slot waiting for a key / mouse button, if any.</summary>
    (GameAction A, int Slot)? capturing;
    string? bindMsg;
    Color bindMsgColor = UiTheme.Dim;
    float bindMsgFlash;

    static Keybinds Eff => App.Valorant.Binds;
    static Keybinds Imp => App.Valorant.Imported;

    void BuildKeybinds(HBoxContainer cols)
    {
        capturing = null;
        foreach (var (title, groups) in new[] { ("MOVEMENT & COMBAT", BindGroupsLeft), ("ABILITIES & ITEMS", BindGroupsRight) })
        {
            var v = Column(cols, title, "PRIMARY · SECONDARY", 1f);
            bool first = true;
            foreach (var (section, actions) in groups)
            {
                if (!first) v.AddChild(Spacer(0, 6 * K));
                first = false;
                v.AddChild(new SectionLabel { Text = section, K = K });
                foreach (var a in actions) v.AddChild(BindRow(a));
            }
        }
        BuildBindInfo(cols);
        // Dev-only: "--bind-capture AltFire.primary" opens that slot's "press a key" capture (screenshots).
        if (CmdLine.Dev && CustomBinds.ParseKey(CmdLine.DevAfter("--bind-capture")) is { } dev) capturing = dev;
        // Dev self-test: "--screen keybinds --keybind-ui-test" drives the capture with real input events and quits.
        if (CmdLine.Dev && CmdLine.Has("--keybind-ui-test") && !uiTestStarted) { uiTestStarted = true; GetTree().CreateTimer(0.5).Timeout += () => _ = UiTest(); }
    }

    static bool uiTestStarted;

    /// <summary>DEV: the capture flow end to end through Input.ParseInputEvent (as the OS would deliver it): thumb buttons,
    /// the wheel, a key, Esc cancels, Backspace restores VALORANT's, right-click unbinds, reset all. Prints
    /// "[keybind-ui-test] …"; the exit code is the number of failures.</summary>
    async Task UiTest()
    {
        int pass = 0, fail = 0;
        void T(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            GD.Print($"[keybind-ui-test] {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
        }
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Press(InputEvent down, InputEvent up) { Input.ParseInputEvent(down); await Frames(2); Input.ParseInputEvent(up); await Frames(2); }
        Task Mouse(MouseButton b) => Press(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Position = new Vector2(5, 5), GlobalPosition = new Vector2(5, 5) },
            new InputEventMouseButton { ButtonIndex = b, Pressed = false, Position = new Vector2(5, 5), GlobalPosition = new Vector2(5, 5) });
        Task KeyTap(Key k) => Press(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true }, new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        St.CustomBinds = null;
        App.ApplyBinds();
        RefreshAll();
        T("every action has two slots on screen", FindChildren("*", "", true, false).OfType<BindSlot>().Count() == Keybinds.All.Length * Keybinds.Slots,
            FindChildren("*", "", true, false).OfType<BindSlot>().Count().ToString());

        StartCapture(GameAction.AltFire, 0);
        await Mouse(MouseButton.Xbutton1);
        T("alt fire ← thumb button (Mouse 4)", Eff[GameAction.AltFire, 0] == InputBinding.Button(MouseButton.Xbutton1) && capturing == null
            && St.CustomBinds?["AltFire.primary"] == "mouse:Xbutton1", Eff.Text(GameAction.AltFire));
        T("VALORANT's alt fire kept apart", Imp[GameAction.AltFire, 0] == InputBinding.Button(MouseButton.Right), Imp.Text(GameAction.AltFire));

        StartCapture(GameAction.Ability1, 0);
        await Mouse(MouseButton.Xbutton2);
        StartCapture(GameAction.AbilityGrenade, 1);
        await Mouse(MouseButton.WheelDown);
        StartCapture(GameAction.Ability2, 1);
        await Mouse(MouseButton.Middle);
        StartCapture(GameAction.Jump, 1);
        await Mouse(MouseButton.WheelUp);
        StartCapture(GameAction.Reload, 1);
        await KeyTap(Key.T);
        bool Is(GameAction a, int s, InputBinding b) => Eff[a, s] == b && Eff[a, 1 - s] == Imp[a, 1 - s];
        T("abilities ← Mouse 5 / wheel / Mouse 3, jump ← wheel up, reload ← T", Is(GameAction.Ability1, 0, InputBinding.Button(MouseButton.Xbutton2))
            && Is(GameAction.AbilityGrenade, 1, InputBinding.Button(MouseButton.WheelDown)) && Is(GameAction.Ability2, 1, InputBinding.Button(MouseButton.Middle))
            && Is(GameAction.Jump, 1, InputBinding.Button(MouseButton.WheelUp)) && Is(GameAction.Reload, 1, Key.T)
            && CustomBinds.Count(St.CustomBinds) == 5 + (Imp[GameAction.Jump, 1] == InputBinding.Button(MouseButton.WheelUp) ? 0 : 1),
            Eff.ListText().Replace('\n', ';'));

        StartCapture(GameAction.Fire, 1);
        await Mouse(MouseButton.Left);
        T("fire secondary ← Mouse 1 = same as primary is allowed", Eff[GameAction.Fire, 1] == InputBinding.Button(MouseButton.Left), Eff.Text(GameAction.Fire));
        StartCapture(GameAction.Fire, 1);
        await KeyTap(Key.Escape);
        T("Esc cancels (bind kept, still on Settings)", capturing == null && Eff[GameAction.Fire, 1] == InputBinding.Button(MouseButton.Left) && IsInsideTree() && Main.I.GetChildren().Contains(this));
        StartCapture(GameAction.Fire, 1);
        await KeyTap(Key.Backspace);
        T("Backspace → back to VALORANT's", capturing == null && Eff[GameAction.Fire, 1].IsNone && !CustomBinds.IsCustom(St.CustomBinds, GameAction.Fire, 1));

        StartCapture(GameAction.Crouch, 0);
        await KeyTap(Key.Shift);
        T("conflict allowed: crouch ← Shift (walk too)", Eff[GameAction.Crouch, 0] == (InputBinding)Key.Shift && Eff.Others(Key.Shift, GameAction.Crouch).Contains(GameAction.Walk)
            && SharedLines().Any(l => l.Custom && l.Text.StartsWith("Shift:")), string.Join("; ", SharedLines().Select(l => l.Text)));

        // Right-click a slot (not capturing) = unbound in ValTrainer.
        var jumpSlot = FindChildren("*", "", true, false).OfType<BindSlot>().FirstOrDefault(x => x.Action == GameAction.Jump && x.Slot == 0);
        if (jumpSlot != null)
        {
            var at = jumpSlot.GetGlobalRect().GetCenter();
            await Press(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at },
                new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
        }
        T("right-click unbinds (jump primary)", jumpSlot != null && Eff[GameAction.Jump, 0].IsNone && St.CustomBinds?["Jump.primary"] == "none", Eff.Text(GameAction.Jump));

        // A left click on a slot starts its capture; the next press binds.
        var dropSlot = FindChildren("*", "", true, false).OfType<BindSlot>().FirstOrDefault(x => x.Action == GameAction.Drop && x.Slot == 0);
        if (dropSlot != null)
        {
            var at = dropSlot.GetGlobalRect().GetCenter();
            await Press(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left },
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }
        bool started = capturing == (GameAction.Drop, 0);
        await Mouse(MouseButton.Xbutton2);
        T("click a slot, then Mouse 5 binds it", started && Eff[GameAction.Drop, 0] == InputBinding.Button(MouseButton.Xbutton2), $"capture started {started}, drop {Eff.Text(GameAction.Drop)}");
        T("hint words follow alt fire on Mouse 4", Eff.Hint("RMB to recall") == "Mouse 4 to recall", Eff.Hint("RMB to recall"));

        St.CustomBinds = null;
        App.ApplyBinds();
        RefreshAll();
        T("reset all → VALORANT's binds", Eff.Dump() == Imp.Dump());
        GD.Print($"[keybind-ui-test] RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} passed, {fail} failed)");
        GetTree().Quit(fail);
    }

    /// <summary>One action: label + where it comes from, and its two bind slots.</summary>
    SettingRow BindRow(GameAction a)
    {
        float k = K;
        var row = new SettingRow { K = k, Label = Keybinds.Label(a), Stacked = stacked, Note = () => BindNote(a) };
        var slots = HBox(6 * k);
        slots.CustomMinimumSize = new Vector2(stacked ? 0 : 250 * k, 0);
        slots.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slots.SizeFlagsStretchRatio = 1.15f;
        for (int s = 0; s < Keybinds.Slots; s++)
        {
            int slot = s;
            var b = new BindSlot
            {
                Action = a, Slot = slot, K = k, CustomMinimumSize = new Vector2(0, 36 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
                Bind = () => Eff[a, slot],
                Custom = () => CustomBinds.IsCustom(St.CustomBinds, a, slot),
                Capturing = () => capturing == (a, slot),
                Shared = () => Eff.Others(Eff[a, slot], a),
            };
            b.Pressed += () => StartCapture(a, slot);
            b.RightClicked += () => SetBind(a, slot, InputBinding.None);
            b.TooltipText = SlotTip(a, slot);
            refreshers.Add(() => { b.TooltipText = SlotTip(a, slot); b.QueueRedraw(); });
            slots.AddChild(b);
        }
        row.AddChild(slots);
        refreshers.Add(row.Refresh);
        return row;
    }

    /// <summary>"From VALORANT" / "Custom · VALORANT: E · Mouse 4" (the note under the action's name).</summary>
    static string BindNote(GameAction a)
    {
        bool p = CustomBinds.IsCustom(St.CustomBinds, a, 0), s = CustomBinds.IsCustom(St.CustomBinds, a, 1);
        if (!p && !s) return App.Valorant.Found ? "From VALORANT" : "VALORANT default";
        return $"Custom · VALORANT: {Imp.Text(a)}";
    }

    static string SlotTip(GameAction a, int slot)
    {
        string name = slot == 0 ? "primary" : "secondary";
        var b = Eff[a, slot];
        string src = CustomBinds.IsCustom(St.CustomBinds, a, slot) ? $"Custom (VALORANT: {Imp[a, slot].Text})" : "From VALORANT";
        var others = Eff.Others(b, a);
        string shared = others.Count > 0 ? $"\nAlso bound to {string.Join(", ", others.Select(Keybinds.Label))}: both trigger" : "";
        return $"{Keybinds.Label(a)} · {name}: {b.Text}\n{src}{shared}\nClick, then press a key or mouse button · Esc cancels · Backspace / Delete = VALORANT's · right-click unbinds";
    }

    void StartCapture(GameAction a, int slot)
    {
        capturing = (a, slot);
        bindMsg = null;
        RefreshAll();
    }

    /// <summary>While a slot waits for input, every key / mouse button goes to it (before the buttons, scrolling and Esc).</summary>
    public override void _Input(InputEvent e)
    {
        if (capturing is not { } c || e is InputEventMouseMotion) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            GetViewport().SetInputAsHandled();
            if (k.Keycode == Key.Escape) { capturing = null; Msg("Cancelled", UiTheme.Dim); RefreshAll(); return; }
            if (k.Keycode is Key.Backspace or Key.Delete)
            {
                capturing = null;
                CustomBinds.Clear(ref St.CustomBinds, c.A, c.Slot);
                App.ApplyBinds();
                Msg($"{Keybinds.Label(c.A)} ({SlotName(c.Slot)}): back to VALORANT's {Imp[c.A, c.Slot].Text}", UiTheme.Good);
                Sfx.I?.Play("kill1", 0.35f);
                Changed();
                return;
            }
        }
        var b = InputBinding.FromEvent(e);
        if (e is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled(); // releases / repeats too
        if (b is not { } bind) return;
        capturing = null;
        SetBind(c.A, c.Slot, bind);
    }

    static string SlotName(int slot) => slot == 0 ? "primary" : "secondary";

    void SetBind(GameAction a, int slot, InputBinding b)
    {
        CustomBinds.Set(ref St.CustomBinds, Imp, a, slot, b);
        App.ApplyBinds();
        var others = Eff.Others(b, a);
        string what = $"{Keybinds.Label(a)} ({SlotName(slot)}): {b.Text}";
        if (b.IsNone) Msg($"{Keybinds.Label(a)} ({SlotName(slot)}): unbound in ValTrainer", UiTheme.Warn);
        else if (others.Count > 0) Msg($"{what} · also {string.Join(", ", others.Select(Keybinds.Label))}: both trigger", UiTheme.Warn);
        else Msg(what + (CustomBinds.IsCustom(St.CustomBinds, a, slot) ? " · custom" : " · same as VALORANT"), UiTheme.Good);
        Sfx.I?.Play("kill1", 0.35f);
        Changed();
    }

    void Msg(string text, Color c)
    {
        bindMsg = text;
        bindMsgColor = c;
        bindMsgFlash = 6f;
    }

    /// <summary>Right column: where the binds come from, the capture prompt, shared binds and RESET ALL TO VALORANT.</summary>
    void BuildBindInfo(HBoxContainer cols)
    {
        float k = K;
        var p = new VPanel { Title = "YOUR KEYBINDS", Caption = "ValTrainer only", K = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        cols.AddChild(p);
        var outer = VBox(12 * k);
        p.AddChild(outer);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var v = VBox(4 * k);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);

        // Capture prompt / last change.
        var prompt = new DrawBox { CustomMinimumSize = new Vector2(0, 92 * k), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        prompt.OnDraw = d =>
        {
            var r = new Rect2(0, 4 * k, d.Size.X, d.Size.Y - 8 * k);
            bool cap = capturing != null;
            float pulse = cap ? 0.55f + 0.45f * Mathf.Sin((float)Time.GetTicksMsec() / 160f) : 0f;
            Gfx.Plate(d, r, 10 * k, cap ? new Color(UiTheme.Accent, 0.10f + 0.06f * pulse) : new Color(1, 1, 1, 0.03f),
                cap ? new Color(UiTheme.Accent, 0.5f + 0.5f * pulse) : new Color(UiTheme.Text, 0.08f), cap ? Mathf.Max(1, 2 * k) : 1f);
            int hs = UiTheme.Fs(22, k), ss = UiTheme.Fs(13.5f, k);
            float x = 16 * k, w = r.Size.X - 32 * k, cy = r.GetCenter().Y;
            if (capturing is { } c)
            {
                Gfx.TextFit(d, UiTheme.Display, "PRESS A KEY OR MOUSE BUTTON", x, Gfx.Mid(cy - 16 * k, hs), hs, UiTheme.Text, w);
                Gfx.TextFit(d, UiTheme.Body, $"{Keybinds.Label(c.A)} · {SlotName(c.Slot)}", x, Gfx.Mid(cy + 6 * k, ss), ss, UiTheme.Accent, w);
                Gfx.TextFit(d, UiTheme.Body, "Esc cancels · Backspace / Delete = VALORANT's bind", x, Gfx.Mid(cy + 24 * k, ss), ss, UiTheme.Dim, w);
            }
            else
            {
                Gfx.TextFit(d, UiTheme.Display, "CLICK A BIND TO CHANGE IT", x, Gfx.Mid(cy - 16 * k, hs), hs, UiTheme.Text, w);
                if (bindMsg != null && bindMsgFlash > 0)
                    Gfx.TextFit(d, UiTheme.Body, bindMsg, x, Gfx.Mid(cy + 6 * k, ss), ss, new Color(bindMsgColor, Mathf.Min(1f, bindMsgFlash)), w);
                else Gfx.TextFit(d, UiTheme.Body, "Keys, mouse buttons 1–5 and the wheel all work", x, Gfx.Mid(cy + 6 * k, ss), ss, UiTheme.Dim, w);
                Gfx.TextFit(d, UiTheme.Body, "Right-click a bind to unbind it in ValTrainer", x, Gfx.Mid(cy + 24 * k, ss), ss, UiTheme.Faint, w);
            }
        };
        refreshers.Add(prompt.QueueRedraw);
        promptBox = prompt;
        v.AddChild(prompt);

        v.AddChild(Spacer(0, 4 * k));
        v.AddChild(new SectionLabel { Text = "WHERE THEY COME FROM", K = k });
        int stripe = 0;
        void Info(string label, Func<string> value, Func<Color>? color = null)
        {
            var row = new InfoRow { K = k, H = 31, Label = label, Value = value, ValueColor = color, Stripe = stripe++ % 2 == 0 };
            refreshers.Add(row.QueueRedraw);
            v.AddChild(row);
        }
        Info("VALORANT", () => App.Valorant.Found ? $"account {App.Valorant.AccountId?[..Math.Min(8, App.Valorant.AccountId.Length)]}…" : "not found: defaults",
            () => App.Valorant.Found ? UiTheme.Good : UiTheme.Warn);
        Info("Changed in VALORANT", () => Imp.FromFile.Count == 0 ? "none (defaults)" : $"{Imp.FromFile.Count} bind{(Imp.FromFile.Count == 1 ? "" : "s")}");
        Info("Custom in ValTrainer", () => { int n = CustomBinds.Count(St.CustomBinds); return n == 0 ? "none" : $"{n} bind{(n == 1 ? "" : "s")}"; },
            () => CustomBinds.Count(St.CustomBinds) > 0 ? UiTheme.Warn : UiTheme.Text);
        v.AddChild(NoteRow(() =>
        {
            int agents = Imp.Skipped.Count(s => s.Contains(" for agent ", StringComparison.Ordinal));
            return agents > 0 ? $"{agents} agent-specific VALORANT bind{(agents == 1 ? " isn't" : "s aren't")} used (one set of binds for every drill): set them here." : null;
        }));
        v.AddChild(NoteRow(() => App.Valorant.Found ? null
            : "VALORANT's settings weren't found on this PC, so these start at VALORANT's defaults. Set yours here."));

        v.AddChild(Spacer(0, 6 * k));
        v.AddChild(new SectionLabel { Text = "SHARED BINDS", K = k });
        // One line per shared bind: the warning colour when a custom bind is involved (VALORANT's own defaults share 4).
        var shared = VBox(2 * k);
        shared.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        v.AddChild(Margins(shared, 12 * k, 2 * k, 6 * k, 4 * k));
        string lastShared = "";
        refreshers.Add(() =>
        {
            var lines = SharedLines();
            string key = string.Join("\n", lines.Select(l => l.Text + l.Custom));
            if (key == lastShared && shared.GetChildCount() > 0) return;
            lastShared = key;
            foreach (var c in shared.GetChildren()) { shared.RemoveChild(c); c.QueueFree(); }
            Label Line(string text, Color c)
            {
                var l = Lbl(text, UiTheme.Body, 14, c);
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                shared.AddChild(l);
                return l;
            }
            if (lines.Count == 0) { Line("None: every bind does one thing.", UiTheme.Dim); return; }
            foreach (var (text, custom) in lines) Line(text, custom ? UiTheme.Warn : UiTheme.Text);
            Line("Every action on a shared bind triggers, like in VALORANT.", UiTheme.Dim);
        });

        v.AddChild(Spacer(0, 6 * k));
        var safe = Lbl("Custom binds change ValTrainer only. VALORANT's own settings are read, never written.", UiTheme.Body, 13, UiTheme.Faint);
        safe.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(Margins(safe, 12 * k, 0, 6 * k, 0));

        var bottom = HBox(14 * k);
        VButton? reset = null;
        ulong armedAt = 0;
        reset = Btn("RESET ALL TO VALORANT", VButton.Look.Secondary, () =>
        {
            ulong now = Time.GetTicksMsec();
            if (armedAt == 0 || now - armedAt > 4000)
            {
                armedAt = now;
                reset!.Label = "CLICK AGAIN TO RESET";
                reset.QueueRedraw();
                GetTree().CreateTimer(4.0).Timeout += () =>
                {
                    if (!IsInstanceValid(reset) || armedAt == 0 || Time.GetTicksMsec() - armedAt < 3900) return;
                    armedAt = 0; reset.Label = "RESET ALL TO VALORANT"; reset.QueueRedraw();
                };
                return;
            }
            armedAt = 0;
            reset!.Label = "RESET ALL TO VALORANT";
            int n = CustomBinds.Count(St.CustomBinds);
            St.CustomBinds = null;
            capturing = null;
            App.ApplyBinds();
            Msg(n == 0 ? "Already VALORANT's binds" : $"{n} custom bind{(n == 1 ? "" : "s")} removed: VALORANT's binds everywhere", UiTheme.Good);
            Sfx.I?.Play("kill1", 0.5f);
            Changed();
        }, 300, 50, 18);
        reset.TooltipText = "Removes every ValTrainer custom bind: back to the binds imported from VALORANT";
        refreshers.Add(() => { reset.Disabled = St.CustomBinds == null && armedAt == 0; reset.QueueRedraw(); });
        bottom.AddChild(reset);
        outer.AddChild(bottom);
    }

    DrawBox? promptBox;

    /// <summary>"Mouse 4: Alt fire / ADS + Ability Q (1)" for every bind used by 2+ actions; Custom = a custom bind is
    /// involved (shown as a warning; VALORANT's own defaults share 4 for the spike).</summary>
    static List<(string Text, bool Custom)> SharedLines()
    {
        var by = new Dictionary<InputBinding, List<GameAction>>();
        var customBinds = new HashSet<InputBinding>();
        foreach (var a in Keybinds.All)
            for (int s = 0; s < Keybinds.Slots; s++)
            {
                var b = Eff[a, s];
                if (b.IsNone) continue;
                if (!by.TryGetValue(b, out var l)) by[b] = l = new List<GameAction>();
                if (!l.Contains(a)) l.Add(a);
                if (CustomBinds.IsCustom(St.CustomBinds, a, s)) customBinds.Add(b);
            }
        var lines = new List<(string, bool)>();
        foreach (var (b, acts) in by)
            if (acts.Count >= 2) lines.Add(($"{b.Text}: {string.Join(" + ", acts.Select(Keybinds.Label))}", customBinds.Contains(b)));
        return lines;
    }

    /// <summary>Keeps the capture prompt pulsing and the last-change message fading.</summary>
    void TickKeybinds(float dt)
    {
        if (page != Page.Keybinds) return;
        if (bindMsgFlash > 0) bindMsgFlash -= dt;
        if (capturing != null || bindMsgFlash > 0) promptBox?.QueueRedraw();
    }
}

/// <summary>One bind slot of Settings → Keybinds: the bind's name (custom binds in the warning colour with a tick, a "!" when
/// another action shares it), a pulsing accent frame while it waits for a key. Right-click = <see cref="RightClicked"/>.</summary>
public partial class BindSlot : BaseButton
{
    public GameAction Action;
    public int Slot;
    public Func<InputBinding> Bind = () => InputBinding.None;
    public Func<bool> Custom = () => false;
    public Func<bool> Capturing = () => false;
    public Func<List<GameAction>> Shared = () => new();
    public float K = 1f;
    public event Action? RightClicked;
    float hover;

    public BindSlot()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready() => Pressed += UiTheme.ClickSound;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && !Capturing())
        {
            AcceptEvent();
            UiTheme.ClickSound();
            RightClicked?.Invoke();
        }
    }

    public override void _Process(double delta)
    {
        float t = IsHovered() && !Disabled ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
        if (Capturing()) QueueRedraw();
    }

    public override void _Draw()
    {
        float k = K;
        var r = new Rect2(Vector2.Zero, Size);
        bool cap = Capturing(), custom = Custom();
        var b = Bind();
        DrawRect(r, new Color(0.04f, 0.07f, 0.1f, 0.8f).Lerp(new Color(1, 1, 1, 0.08f), hover));
        int fs = UiTheme.Fs(15, k);
        float cy = r.Size.Y / 2;
        if (cap)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin((float)Time.GetTicksMsec() / 160f);
            DrawRect(r, new Color(UiTheme.Accent, 0.14f * pulse));
            DrawRect(r, new Color(UiTheme.Accent, 0.6f + 0.4f * pulse), false, Mathf.Max(1, 2 * k));
            Gfx.TextFit(this, UiTheme.Body, "PRESS A KEY…", 8 * k, Gfx.Mid(cy, fs), fs, UiTheme.Text, r.Size.X - 16 * k, HorizontalAlignment.Center);
            return;
        }
        DrawRect(new Rect2(0, r.Size.Y - 1, r.Size.X, 1), new Color(custom ? UiTheme.Warn : UiTheme.Text, custom ? 0.8f : 0.25f));
        if (custom) DrawRect(new Rect2(0, 0, 3 * k, r.Size.Y), UiTheme.Warn);
        bool shared = !b.IsNone && Shared().Count > 0;
        float right = shared ? 18 * k : 8 * k;
        var col = b.IsNone ? UiTheme.Faint : custom ? UiTheme.Warn : UiTheme.Text;
        Gfx.TextFit(this, UiTheme.Body, b.IsNone ? "—" : b.Text, 8 * k, Gfx.Mid(cy, fs), fs, col, r.Size.X - 8 * k - right, HorizontalAlignment.Center);
        if (shared)
        {
            int ms = UiTheme.Fs(14, k);
            Gfx.TextC(this, UiTheme.Body, "!", r.Size.X - 10 * k, Gfx.Mid(cy, ms), ms, custom ? UiTheme.Warn : UiTheme.Dim);
        }
    }
}
