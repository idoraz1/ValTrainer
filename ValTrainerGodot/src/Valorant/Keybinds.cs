using System.Text;
using Godot;

namespace ValTrainer.Valorant;

/// <summary>The VALORANT actions the trainer uses. Each has two bind slots like VALORANT (primary / secondary).</summary>
public enum GameAction
{
    MoveForward, MoveBack, StrafeLeft, StrafeRight, Walk, Crouch, Jump,
    Fire, AltFire, Reload, Use,
    /// <summary>Take the spike out (VALORANT "Equip Spike", default 4).</summary>
    EquipSpike,
    /// <summary>Plant / defuse (VALORANT "Use Spike", default 4): what the tactical drills' defuse reads.</summary>
    UseSpike,
    /// <summary>C (VALORANT's "Grenade" ability slot).</summary>
    AbilityGrenade,
    /// <summary>Q.</summary>
    Ability1,
    /// <summary>E (signature).</summary>
    Ability2,
    /// <summary>X.</summary>
    Ultimate,
    EquipPrimary, EquipSecondary, EquipMelee, Inspect, Drop,
}

/// <summary>
/// One bind: a keyboard key or a mouse button (incl. the wheel notches and the thumb buttons), optionally with modifier
/// keys that must be held (VALORANT's "shift" / "ctrl" / "alt" flags). <c>default</c> = unbound.
/// A wheel notch has no "held" state: it counts as pressed for exactly one game frame (<see cref="Keybinds.Observe"/>).
/// </summary>
public readonly record struct InputBinding(Key Key, MouseButton Mouse = MouseButton.None, bool Shift = false, bool Ctrl = false, bool Alt = false)
{
    public static readonly InputBinding None = default;
    public static implicit operator InputBinding(Key key) => new(key);
    public static InputBinding Button(MouseButton b) => new(Key.None, b);

    public bool IsNone => Key == Key.None && Mouse == MouseButton.None;
    public bool IsMouse => Key == Key.None && Mouse != MouseButton.None;
    public bool IsWheel => Key == Key.None && Mouse is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight;
    bool HasMods => (Shift || Ctrl || Alt) && Key is not (Key.Shift or Key.Ctrl or Key.Alt or Key.Meta);

    /// <summary>Held right now (a wheel notch: during the frame it happened). Honours the dev/test <see cref="Keybinds.Override"/>.</summary>
    public bool Held => Keybinds.Override is { } f ? !IsNone && f(this) : RealHeld;

    /// <summary>The real keyboard / mouse state (ignores the dev override).</summary>
    public bool RealHeld
    {
        get
        {
            if (IsNone) return false;
            bool down = Key != Key.None ? Input.IsKeyPressed(Key)
                : IsWheel ? Keybinds.WheelPulse(Mouse) : Input.IsMouseButtonPressed(Mouse);
            return down && (!HasMods || ModsDown(Input.IsKeyPressed(Key.Shift), Input.IsKeyPressed(Key.Ctrl), Input.IsKeyPressed(Key.Alt)));
        }
    }

    bool ModsDown(bool shift, bool ctrl, bool alt) => (!Shift || shift) && (!Ctrl || ctrl) && (!Alt || alt);

    /// <summary>1 = this event presses the bind (key repeats don't count), -1 = it releases it (wheel notches never
    /// release), 0 = not this bind. A release counts whatever modifiers are held then.</summary>
    public int Match(InputEvent e)
    {
        if (IsNone) return 0;
        switch (e)
        {
            case InputEventKey k when Key != Key.None:
                if ((k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode) != Key) return 0;
                if (!k.Pressed) return -1;
                if (k.Echo) return 0;
                return !HasMods || ModsDown(k.ShiftPressed, k.CtrlPressed, k.AltPressed) ? 1 : 0;
            case InputEventMouseButton mb when Key == Key.None:
                if (mb.ButtonIndex != Mouse) return 0;
                if (!mb.Pressed) return IsWheel ? 0 : -1;
                return !HasMods || ModsDown(mb.ShiftPressed, mb.CtrlPressed, mb.AltPressed) ? 1 : 0;
        }
        return 0;
    }

    /// <summary>A synthetic event for this bind (dev tests and dev auto-play feed it to Input.ParseInputEvent).</summary>
    public InputEvent? ToEvent(bool pressed)
    {
        if (Key != Key.None) return new InputEventKey { Keycode = Key, PhysicalKeycode = Key, Pressed = pressed, ShiftPressed = Shift, CtrlPressed = Ctrl, AltPressed = Alt };
        if (Mouse != MouseButton.None)
            return new InputEventMouseButton { ButtonIndex = Mouse, Pressed = pressed, ShiftPressed = Shift, CtrlPressed = Ctrl, AltPressed = Alt, Factor = 1f };
        return null;
    }

    /// <summary>Short display name: "W", "Space", "Mouse 1", "Wheel down", "Shift+Q"; "—" when unbound.</summary>
    public string Text
    {
        get
        {
            if (IsNone) return "—";
            string name = Key != Key.None ? KeyName(Key) : MouseName(Mouse);
            if (!HasMods) return name;
            return (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + name;
        }
    }

    public override string ToString() => Text;

    /// <summary>A bind name squeezed onto a small HUD key cap (at most 4 characters, upper case): "Q", "M4", "WD", "SPC".</summary>
    public static string Cap(string text)
    {
        var t = text.ToUpperInvariant();
        if (t.StartsWith("MOUSE ", StringComparison.Ordinal)) return "M" + t[6..];
        if (t.StartsWith("NUM ", StringComparison.Ordinal)) return "N" + t[4..];
        int plus = t.LastIndexOf('+');
        if (plus > 0 && plus < t.Length - 1) t = t[(plus + 1)..]; // modifiers don't fit on a cap
        return t switch
        {
            "WHEEL UP" => "WU", "WHEEL DOWN" => "WD", "WHEEL LEFT" => "WL", "WHEEL RIGHT" => "WR", "SPACE" => "SPC", "SHIFT" => "SHFT",
            "ENTER" => "ENT", "BACKSPACE" => "BKSP", "UNBOUND" => "—",
            _ => t.Length <= 4 ? t : t[..4],
        };
    }

    static string MouseName(MouseButton b) => b switch
    {
        MouseButton.Left => "Mouse 1", MouseButton.Right => "Mouse 2", MouseButton.Middle => "Mouse 3",
        MouseButton.Xbutton1 => "Mouse 4", MouseButton.Xbutton2 => "Mouse 5",
        MouseButton.WheelUp => "Wheel up", MouseButton.WheelDown => "Wheel down",
        MouseButton.WheelLeft => "Wheel left", MouseButton.WheelRight => "Wheel right",
        _ => b.ToString(),
    };

    static string KeyName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
        if (k >= Key.Key0 && k <= Key.Key9) return ((char)('0' + (k - Key.Key0))).ToString();
        if (k >= Key.Kp0 && k <= Key.Kp9) return "Num " + (char)('0' + (k - Key.Kp0));
        return k switch
        {
            Key.Space => "Space", Key.Shift => "Shift", Key.Ctrl => "Ctrl", Key.Alt => "Alt", Key.Tab => "Tab",
            Key.Capslock => "Caps", Key.Enter => "Enter", Key.Backspace => "Backspace", Key.Quoteleft => "`",
            Key.Minus => "-", Key.Equal => "=", Key.Bracketleft => "[", Key.Bracketright => "]", Key.Backslash => "\\",
            Key.Semicolon => ";", Key.Apostrophe => "'", Key.Comma => ",", Key.Period => ".", Key.Slash => "/",
            Key.Pageup => "PgUp", Key.Pagedown => "PgDn", Key.Delete => "Del", Key.Insert => "Ins",
            _ => SafeKeycodeString(k),
        };
    }

    static string SafeKeycodeString(Key k)
    {
        try { var s = OS.GetKeycodeString(k); return string.IsNullOrEmpty(s) ? k.ToString() : s; }
        catch { return k.ToString(); }
    }
}

/// <summary>
/// The player's VALORANT keybinds: two slots (primary / secondary) per <see cref="GameAction"/>. Starts from VALORANT's
/// defaults (BackupKeybinds.json only stores the binds the player changed); <see cref="ValorantImporter"/> applies the
/// file's overrides on top. Use <see cref="IsDown"/> for held actions (polled every frame; works for keys and mouse buttons,
/// a wheel notch counts for one frame) and <see cref="Match"/> / <see cref="Pressed"/> for event handlers.
/// One key may be bound to several actions: every action checks its own binds, so they all trigger (as in VALORANT).
/// Esc is never a bind (it always pauses).
/// </summary>
public sealed class Keybinds
{
    public const int Slots = 2;
    public static readonly GameAction[] All = Enum.GetValues<GameAction>();
    static readonly int Count = All.Length;

    readonly InputBinding[] binds = new InputBinding[Count * Slots];

    /// <summary>Overrides read from the file (for the dump / settings): "action slot = key".</summary>
    public readonly List<string> FromFile = new();
    /// <summary>File entries that weren't used: unknown key names (default kept), per-agent binds, unknown actions.</summary>
    public readonly List<string> Skipped = new();

    public InputBinding Get(GameAction a, int slot) => slot is >= 0 and < Slots ? binds[(int)a * Slots + slot] : InputBinding.None;
    public void Set(GameAction a, int slot, InputBinding b) { if (slot is >= 0 and < Slots) binds[(int)a * Slots + slot] = b; }
    public InputBinding this[GameAction a, int slot] => Get(a, slot);

    /// <summary>The bind to show / script for an action: the primary slot, else the secondary, else unbound.</summary>
    public InputBinding Primary(GameAction a) => !Get(a, 0).IsNone ? Get(a, 0) : Get(a, 1);
    public bool IsBound(GameAction a) => !Get(a, 0).IsNone || !Get(a, 1).IsNone;

    /// <summary>Any of the action's binds held right now (a wheel notch: this frame). Honours the dev <see cref="Override"/>
    /// unless <paramref name="real"/>.</summary>
    public bool IsDown(GameAction a, bool real = false)
    {
        for (int s = 0; s < Slots; s++)
        {
            var b = Get(a, s);
            if (!b.IsNone && (real ? b.RealHeld : b.Held)) return true;
        }
        return false;
    }

    /// <summary>1 = the event presses one of the action's binds, -1 = it releases one, 0 = unrelated.</summary>
    public int Match(InputEvent e, GameAction a)
    {
        int r = 0;
        for (int s = 0; s < Slots; s++)
        {
            int m = Get(a, s).Match(e);
            if (m > 0) return 1;
            if (m < 0) r = -1;
        }
        return r;
    }

    public bool Pressed(InputEvent e, GameAction a) => Match(e, a) > 0;
    public bool Released(InputEvent e, GameAction a) => Match(e, a) < 0;

    /// <summary>The bind(s) for display: "Space · Wheel down", "R", "unbound".</summary>
    public string Text(GameAction a, string sep = " · ")
    {
        var p = Get(a, 0);
        var s = Get(a, 1);
        if (p.IsNone && s.IsNone) return "unbound";
        if (p.IsNone) return s.Text;
        if (s.IsNone || s == p) return p.Text;
        return p.Text + sep + s.Text;
    }

    /// <summary>One bind for tight spots (HUD key caps): the primary, else the secondary, else "—".</summary>
    public string Short(GameAction a) => Primary(a).Text;

    /// <summary>
    /// A help line with the player's fire / alt-fire binds: the words "LMB" / "RMB" (VALORANT's own wording for fire / alt
    /// fire in ability help) become the bind when the player moved fire / alt fire off mouse 1 / 2.
    /// </summary>
    public string Hint(string text)
    {
        if (string.IsNullOrEmpty(text) || (text.IndexOf("LMB", StringComparison.Ordinal) < 0 && text.IndexOf("RMB", StringComparison.Ordinal) < 0)) return text;
        var fire = Primary(GameAction.Fire);
        var alt = Primary(GameAction.AltFire);
        if (fire.IsMouse && fire.Mouse == MouseButton.Left && alt.IsMouse && alt.Mouse == MouseButton.Right) return text;
        string Swap(string s, string word, InputBinding b, MouseButton def)
        {
            if (b.IsMouse && b.Mouse == def) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, $@"\b{word}\b", b.IsNone ? word : b.Text);
        }
        return Swap(Swap(text, "LMB", fire, MouseButton.Left), "RMB", alt, MouseButton.Right);
    }

    /// <summary>The ability action of a slot index (0 C, 1 Q, 2 E, 3 X — the order of AbilitySlot).</summary>
    public static GameAction AbilityAction(int slot) => slot switch
    {
        0 => GameAction.AbilityGrenade, 1 => GameAction.Ability1, 2 => GameAction.Ability2, _ => GameAction.Ultimate,
    };

    /// <summary>Human label of an action.</summary>
    public static string Label(GameAction a) => a switch
    {
        GameAction.MoveForward => "Move forward", GameAction.MoveBack => "Move back", GameAction.StrafeLeft => "Strafe left",
        GameAction.StrafeRight => "Strafe right", GameAction.Walk => "Walk", GameAction.Crouch => "Crouch", GameAction.Jump => "Jump",
        GameAction.Fire => "Fire", GameAction.AltFire => "Alt fire / ADS", GameAction.Reload => "Reload", GameAction.Use => "Use",
        GameAction.EquipSpike => "Equip spike", GameAction.UseSpike => "Use spike (plant / defuse)", GameAction.AbilityGrenade => "Ability C (grenade)",
        GameAction.Ability1 => "Ability Q (1)", GameAction.Ability2 => "Ability E (2)", GameAction.Ultimate => "Ultimate (X)",
        GameAction.EquipPrimary => "Equip primary", GameAction.EquipSecondary => "Equip secondary", GameAction.EquipMelee => "Equip melee",
        GameAction.Inspect => "Inspect", GameAction.Drop => "Drop", _ => a.ToString(),
    };

    /// <summary>VALORANT's default binds (Settings → Controls with nothing changed).</summary>
    public static Keybinds Defaults()
    {
        var k = new Keybinds();
        void D(GameAction a, InputBinding p, InputBinding s = default) { k.Set(a, 0, p); k.Set(a, 1, s); }
        D(GameAction.MoveForward, Key.W);
        D(GameAction.MoveBack, Key.S);
        D(GameAction.StrafeLeft, Key.A);
        D(GameAction.StrafeRight, Key.D);
        D(GameAction.Walk, Key.Shift);
        D(GameAction.Crouch, Key.Ctrl);
        D(GameAction.Jump, Key.Space);
        D(GameAction.Fire, InputBinding.Button(MouseButton.Left));
        D(GameAction.AltFire, InputBinding.Button(MouseButton.Right));
        D(GameAction.Reload, Key.R);
        D(GameAction.Use, Key.F);
        D(GameAction.EquipSpike, Key.Key4);
        D(GameAction.UseSpike, Key.Key4);
        D(GameAction.AbilityGrenade, Key.C);
        D(GameAction.Ability1, Key.Q);
        D(GameAction.Ability2, Key.E);
        D(GameAction.Ultimate, Key.X);
        D(GameAction.EquipPrimary, Key.Key1);
        D(GameAction.EquipSecondary, Key.Key2);
        D(GameAction.EquipMelee, Key.Key3);
        D(GameAction.Inspect, Key.Y);
        D(GameAction.Drop, Key.G);
        return k;
    }

    /// <summary>Every action, one per line ("Jump: Space"), for the settings tooltip.</summary>
    public string ListText() => string.Join("\n", All.Select(a => $"{Label(a)}: {Text(a)}"));

    /// <summary>Every action with both slots and where they came from, for --keybind-dump.</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        foreach (var a in All)
            sb.Append($"{Label(a),-30} {Get(a, 0).Text,-12} {Get(a, 1).Text}\n");
        var shared = SharedBinds();
        if (shared.Count > 0) sb.Append("shared: ").Append(string.Join("; ", shared)).Append('\n');
        sb.Append("from file: ").Append(FromFile.Count == 0 ? "nothing (all defaults)" : string.Join(", ", FromFile)).Append('\n');
        if (Skipped.Count > 0) sb.Append("skipped: ").Append(string.Join(", ", Skipped)).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>Binds used by more than one action ("Space: Crouch + Jump"): all of them trigger.</summary>
    public List<string> SharedBinds()
    {
        var by = new Dictionary<InputBinding, List<GameAction>>();
        foreach (var a in All)
            for (int s = 0; s < Slots; s++)
            {
                var b = Get(a, s);
                if (b.IsNone) continue;
                if (!by.TryGetValue(b, out var l)) by[b] = l = new List<GameAction>();
                if (!l.Contains(a)) l.Add(a);
            }
        return by.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key.Text}: {string.Join(" + ", kv.Value.Select(Label))}").ToList();
    }

    // ------------------------------------------------------------------ input state shared by every bind

    /// <summary>
    /// DEV / TEST ONLY: when set, every bind read through <see cref="InputBinding.Held"/> / <see cref="IsDown"/> comes from
    /// this function instead of the keyboard and mouse (scripted movement: --movescript, --movecheck, dev auto-play).
    /// Exposed as <c>Mover.KeyOverride</c> too. Null (the default) = the real input.
    /// </summary>
    public static Func<InputBinding, bool>? Override;

    static readonly bool[] wheel = new bool[4];
    static int WheelIndex(MouseButton b) => b switch
    {
        MouseButton.WheelUp => 0, MouseButton.WheelDown => 1, MouseButton.WheelLeft => 2, MouseButton.WheelRight => 3, _ => -1,
    };

    /// <summary>Called for every input event of a session: remembers wheel notches until <see cref="EndFrame"/>.</summary>
    public static void Observe(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb && WheelIndex(mb.ButtonIndex) is >= 0 and var i) wheel[i] = true;
    }

    /// <summary>End of a game frame: the wheel notches seen since the last one stop counting as held.</summary>
    public static void EndFrame() => Array.Clear(wheel);

    internal static bool WheelPulse(MouseButton b) => WheelIndex(b) is >= 0 and var i && wheel[i];
}
