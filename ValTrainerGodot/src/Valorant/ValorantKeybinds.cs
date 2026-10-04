using System.Text.Json;
using Godot;

namespace ValTrainer.Valorant;

/// <summary>
/// BackupKeybinds.json → <see cref="Keybinds"/>. The file lists only the binds the player CHANGED from VALORANT's defaults:
/// <c>actionMappings: [ { name, characterName, bindIndex (0 primary / 1 secondary), key, shift, ctrl, alt, cmd, tapHoldType } ]</c>.
/// <c>key</c> is an Unreal key name ("SpaceBar", "ThumbMouseButton", "MouseScrollDown", "One" …); "None" = unbound.
/// So the import starts from <see cref="Keybinds.Defaults"/> and applies each entry to its action's slot.
/// </summary>
public static partial class ValorantImporter
{
    /// <summary>Applies the file's overrides to <see cref="ValorantProfile.Binds"/>. A missing or broken file keeps the defaults.</summary>
    internal static void ApplyKeybinds(ValorantProfile p, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            ApplyKeybindsJson(p.Binds, text);
        }
        catch (Exception e) { p.Binds.Skipped.Add($"file unreadable ({e.GetType().Name})"); }
    }

    /// <summary>Applies BackupKeybinds.json text to <paramref name="binds"/> (also used by the dev self-tests).</summary>
    internal static void ApplyKeybindsJson(Keybinds binds, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        // Movement is most likely an Unreal axis pair (MoveForward: W +1 / S −1, MoveRight: D +1 / A −1) in axisMappings;
        // older or other files may list it as actions instead. Both are read.
        if (root.TryGetProperty("axisMappings", out var axes) && axes.ValueKind == JsonValueKind.Array)
            foreach (var m in axes.EnumerateArray())
                if (m.ValueKind == JsonValueKind.Object) ApplyEntry(binds, m, AxisActionOf(Str(m, "name") ?? "", Num(m, "scale")));
        if (root.TryGetProperty("actionMappings", out var maps) && maps.ValueKind == JsonValueKind.Array)
            foreach (var m in maps.EnumerateArray())
                if (m.ValueKind == JsonValueKind.Object) ApplyEntry(binds, m, ActionOf(Str(m, "name") ?? ""));
    }

    /// <summary>One mapping entry → the action's slot (pings, chat, voice, map, menus … map to no action and are skipped).</summary>
    static void ApplyEntry(Keybinds binds, JsonElement m, GameAction? mapped)
    {
        if (mapped is not { } action) return;
        string name = Str(m, "name") ?? "";
        string keyName = (Str(m, "key") ?? "").Trim();
        string who = Str(m, "characterName") ?? "None";
        int slot = m.TryGetProperty("bindIndex", out var bi) && bi.ValueKind == JsonValueKind.Number && bi.TryGetInt32(out int b) ? b : 0;
        if (!string.IsNullOrEmpty(who) && !who.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            binds.Skipped.Add($"{name}[{slot}] for agent {who}"); // per-agent binds: not applied to every drill
            return;
        }
        if (slot is < 0 or >= Keybinds.Slots) { binds.Skipped.Add($"{name} bindIndex {slot}"); return; }
        if (keyName.Length == 0 || keyName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            binds.Set(action, slot, InputBinding.None);
            binds.FromFile.Add($"{action}[{slot}]=unbound");
            return;
        }
        if (UeBinding(keyName) is not { } bind)
        {
            binds.Skipped.Add($"{name}[{slot}]={keyName} (unknown key: default kept)");
            return;
        }
        bind = bind with { Shift = Flag(m, "shift"), Ctrl = Flag(m, "ctrl"), Alt = Flag(m, "alt") };
        binds.Set(action, slot, bind);
        binds.FromFile.Add($"{action}[{slot}]={bind.Text}");
    }

    /// <summary>An axis entry: MoveForward +1 = forward, −1 = back; MoveRight +1 = right, −1 = left.</summary>
    internal static GameAction? AxisActionOf(string name, float scale)
    {
        if (scale == 0f || !float.IsFinite(scale)) return null;
        if (name.Contains("Forward", StringComparison.OrdinalIgnoreCase)) return scale > 0 ? GameAction.MoveForward : GameAction.MoveBack;
        if (name.Contains("Right", StringComparison.OrdinalIgnoreCase) || name.Contains("Strafe", StringComparison.OrdinalIgnoreCase))
            return scale > 0 ? GameAction.StrafeRight : GameAction.StrafeLeft;
        return null; // look / gamepad axes
    }

    static string? Str(JsonElement m, string prop) =>
        m.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static float Num(JsonElement m, string prop) =>
        m.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f) ? f : 0f;

    static bool Flag(JsonElement m, string prop) =>
        m.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>
    /// VALORANT's internal action names (BackupKeybinds.json / the player-preferences "actionMappings").
    /// <list type="bullet">
    /// <item>Seen in real files / payloads: Crouch, Jump, UseObject, OpenMegamap, VOICE_TeamPTTAction, PrevWeapon, NextWeapon.</item>
    /// <item>From the game's action classes (one "…ActionTraits" class per bindable action, in a public UE type dump; the
    /// class name normally equals the action name): Walk, PrimaryTrigger (fire), SecondaryTrigger (alt fire), Reload,
    /// Activate_Primary / _Secondary / _Melee, Activate_GrenadeAbility (C), Activate_Ability1 (Q), Activate_Ability2 (E),
    /// Activate_Ultimate (X), Inspect, DropEquippable, UseChannelObject (use spike: plant / defuse, likely) and
    /// Activate_Level (equip spike, likely), MoveForward / MoveRight (axes).</item>
    /// <item>Everything else here is an older or guessed spelling; unknown names fall back to <see cref="ActionOf"/>'s word match.</item>
    /// </list>
    /// </summary>
    static readonly Dictionary<string, GameAction> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MoveForward"] = GameAction.MoveForward, ["MoveBackward"] = GameAction.MoveBack, ["MoveBack"] = GameAction.MoveBack,
        ["StrafeLeft"] = GameAction.StrafeLeft, ["MoveLeft"] = GameAction.StrafeLeft,
        ["StrafeRight"] = GameAction.StrafeRight, ["MoveRight"] = GameAction.StrafeRight,
        ["Walk"] = GameAction.Walk,
        ["Crouch"] = GameAction.Crouch,
        ["Jump"] = GameAction.Jump,
        ["PrimaryTrigger"] = GameAction.Fire, ["Fire"] = GameAction.Fire, ["PrimaryFire"] = GameAction.Fire,
        ["SecondaryTrigger"] = GameAction.AltFire, ["AltFire"] = GameAction.AltFire, ["AlternateFire"] = GameAction.AltFire, ["Zoom"] = GameAction.AltFire,
        ["Reload"] = GameAction.Reload,
        ["UseObject"] = GameAction.Use, ["Use"] = GameAction.Use, ["Interact"] = GameAction.Use,
        ["Activate_Level"] = GameAction.EquipSpike, ["EquipSpike"] = GameAction.EquipSpike, ["EquipBomb"] = GameAction.EquipSpike,
        ["UseChannelObject"] = GameAction.UseSpike, ["UseSpike"] = GameAction.UseSpike, ["UseBomb"] = GameAction.UseSpike,
        ["Activate_GrenadeAbility"] = GameAction.AbilityGrenade, ["EquipGrenadeAbility"] = GameAction.AbilityGrenade, ["GrenadeAbility"] = GameAction.AbilityGrenade,
        ["Activate_Ability1"] = GameAction.Ability1, ["EquipAbility1"] = GameAction.Ability1, ["Ability1"] = GameAction.Ability1,
        ["Activate_Ability2"] = GameAction.Ability2, ["EquipAbility2"] = GameAction.Ability2, ["Ability2"] = GameAction.Ability2,
        ["Activate_Ultimate"] = GameAction.Ultimate, ["EquipUltimateAbility"] = GameAction.Ultimate, ["Ultimate"] = GameAction.Ultimate,
        ["Activate_Primary"] = GameAction.EquipPrimary, ["EquipPrimaryWeapon"] = GameAction.EquipPrimary,
        ["Activate_Secondary"] = GameAction.EquipSecondary, ["EquipSecondaryWeapon"] = GameAction.EquipSecondary,
        ["Activate_Melee"] = GameAction.EquipMelee, ["EquipMeleeWeapon"] = GameAction.EquipMelee,
        ["Inspect"] = GameAction.Inspect, ["InspectWeapon"] = GameAction.Inspect,
        ["DropEquippable"] = GameAction.Drop, ["Drop"] = GameAction.Drop, ["DropItem"] = GameAction.Drop,
    };

    /// <summary>
    /// The trainer action of a VALORANT action name: the known names first, then a tolerant match on the words in the name
    /// (Riot doesn't document them and they changed over the years). Toggle variants, pings, chat, voice, map, scoreboard,
    /// spectator and gamepad actions map to nothing.
    /// </summary>
    internal static GameAction? ActionOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (KnownNames.TryGetValue(name.Trim(), out var known)) return known;
        const StringComparison I = StringComparison.OrdinalIgnoreCase;
        bool Has(string w) => name.Contains(w, I);
        string[] ignore = { "Toggle", "Ping", "Chat", "Radio", "Comm", "Voice", "PTT", "Map", "Score", "Spectat", "Observer", "Gamepad",
                            "Menu", "Camera", "Replay", "Spray", "Emote", "Wheel", "Cycle", "Previous", "Next", "Last", "Buy", "Shop" };
        if (ignore.Any(Has)) return null;
        if (Has("Ultimate")) return GameAction.Ultimate;
        if (Has("Grenade")) return GameAction.AbilityGrenade;
        if (Has("Ability"))
        {
            if (name.EndsWith('1') || Has("One")) return GameAction.Ability1;
            if (name.EndsWith('2') || Has("Two")) return GameAction.Ability2;
            return null;
        }
        if (Has("Channel") || ((Has("Spike") || Has("Bomb")) && (Has("Use") || Has("Plant") || Has("Defuse")))) return GameAction.UseSpike;
        if (Has("Spike") || Has("Bomb")) return GameAction.EquipSpike;
        if (Has("Forward")) return GameAction.MoveForward;
        if (Has("Backward")) return GameAction.MoveBack;
        if ((Has("Strafe") || Has("Move")) && Has("Left")) return GameAction.StrafeLeft;
        if ((Has("Strafe") || Has("Move")) && Has("Right")) return GameAction.StrafeRight;
        if (Has("Walk")) return GameAction.Walk;
        if (Has("Crouch")) return GameAction.Crouch;
        if (Has("Jump")) return GameAction.Jump;
        if (Has("Reload")) return GameAction.Reload;
        if (Has("Trigger")) return Has("Secondary") || Has("Alt") ? GameAction.AltFire : GameAction.Fire;
        if (Has("AltFire") || Has("Alternate") || Has("Zoom") || Has("Scope") || Has("ADS")) return GameAction.AltFire;
        if (Has("Fire") || Has("Shoot")) return GameAction.Fire;
        if (Has("Inspect")) return GameAction.Inspect;
        if (Has("Drop")) return GameAction.Drop;
        if (Has("Melee") || Has("Knife")) return GameAction.EquipMelee;
        if (Has("Secondary") || Has("Sidearm") || Has("Pistol")) return GameAction.EquipSecondary;
        if (Has("Primary")) return GameAction.EquipPrimary;
        if (Has("Use") || Has("Interact")) return GameAction.Use;
        return null;
    }

    /// <summary>Unreal key name → bind (keyboard keys, mouse buttons, wheel notches). Null = unknown or not bindable here
    /// (Escape always pauses; gamepad keys).</summary>
    internal static InputBinding? UeBinding(string ue)
    {
        if (ue.Length == 1 && ue[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            return (InputBinding)(Key)((int)Key.A + (char.ToUpperInvariant(ue[0]) - 'A'));
        if (ue.StartsWith('F') && ue.Length is 2 or 3 && int.TryParse(ue[1..], out int fn) && fn is >= 1 and <= 24)
            return (InputBinding)(Key)((int)Key.F1 + fn - 1);
        int digit = Array.IndexOf(Digits, ue);
        if (digit >= 0) return (InputBinding)(Key)((int)Key.Key0 + digit);
        if (ue.StartsWith("NumPad", StringComparison.Ordinal) && Array.IndexOf(Digits, ue[6..]) is >= 0 and var nd)
            return (InputBinding)(Key)((int)Key.Kp0 + nd);
        return ue switch
        {
            "LeftMouseButton" => InputBinding.Button(MouseButton.Left),
            "RightMouseButton" => InputBinding.Button(MouseButton.Right),
            "MiddleMouseButton" => InputBinding.Button(MouseButton.Middle),
            "ThumbMouseButton" => InputBinding.Button(MouseButton.Xbutton1),
            "ThumbMouseButton2" => InputBinding.Button(MouseButton.Xbutton2),
            "MouseScrollUp" => InputBinding.Button(MouseButton.WheelUp),
            "MouseScrollDown" => InputBinding.Button(MouseButton.WheelDown),
            "MouseWheelLeft" or "MouseScrollLeft" => InputBinding.Button(MouseButton.WheelLeft),
            "MouseWheelRight" or "MouseScrollRight" => InputBinding.Button(MouseButton.WheelRight),
            "SpaceBar" => Key.Space,
            "LeftShift" or "RightShift" => Key.Shift,
            "LeftControl" or "RightControl" => Key.Ctrl,
            "LeftAlt" or "RightAlt" => Key.Alt,
            "LeftCommand" or "RightCommand" => Key.Meta,
            "Tab" => Key.Tab,
            "CapsLock" => Key.Capslock,
            "Enter" => Key.Enter,
            "BackSpace" => Key.Backspace,
            "Tilde" => Key.Quoteleft,
            "Hyphen" => Key.Minus,
            "Equals" => Key.Equal,
            "LeftBracket" => Key.Bracketleft,
            "RightBracket" => Key.Bracketright,
            "Backslash" => Key.Backslash,
            "Semicolon" => Key.Semicolon,
            "Apostrophe" => Key.Apostrophe,
            "Comma" => Key.Comma,
            "Period" => Key.Period,
            "Slash" => Key.Slash,
            "Section" => Key.Section,
            "Insert" => Key.Insert,
            "Delete" => Key.Delete,
            "Home" => Key.Home,
            "End" => Key.End,
            "PageUp" => Key.Pageup,
            "PageDown" => Key.Pagedown,
            "Up" => Key.Up,
            "Down" => Key.Down,
            "Left" => Key.Left,
            "Right" => Key.Right,
            "Multiply" => Key.KpMultiply,
            "Add" => Key.KpAdd,
            "Subtract" => Key.KpSubtract,
            "Divide" => Key.KpDivide,
            "Decimal" => Key.KpPeriod,
            "NumLock" => Key.Numlock,
            "ScrollLock" => Key.Scrolllock,
            "Pause" => Key.Pause,
            _ => null, // Escape (always pause), gamepad keys, anything new
        };
    }

    static readonly string[] Digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
}
