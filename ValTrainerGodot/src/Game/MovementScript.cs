using System.Globalization;
using Godot;
using ValTrainer.Valorant;

namespace ValTrainer.Game;

/// <summary>
/// DEV / TEST ONLY: scripted movement keys for unattended runs (<c>--dev --movescript "&lt;spec&gt;"</c>), installed as
/// <see cref="Mover.KeyOverride"/> by the session. Times are seconds since GO.
/// <list type="bullet">
/// <item>Spec: comma-separated items <c>key:start-end</c> (hold) or <c>key:start</c> (a 0.08 s tap), optionally starting with
/// <c>loop=&lt;period&gt;</c> to repeat the schedule every period seconds.</item>
/// <item>Keys: any letter, <c>space ctrl shift alt</c>, a Godot key name, or the player's binds by action:
/// <c>forward back left right jump crouch walk spike (use spike: plant / defuse) use ability_c ability_q ability_e ability_x</c> (whatever they are
/// bound to: a key, a mouse button or the wheel).</item>
/// <item><c>auto</c>: the drill plays its own scripted movement (Jump Peek, Jiggle Peek); other drills get no script.</item>
/// </list>
/// Example: <c>--movescript "loop=2.5,right:0-0.3,jump:0.05,left:0.35-0.6"</c> (strafe out, jump, strafe back, every 2.5 s).
/// Add <c>--movelog</c> (implied by --movescript) to print every landing (air time, rise, distance) to the log.
/// </summary>
public sealed class MovementScript
{
    readonly List<(InputBinding Key, float From, float To)> items = new();
    readonly float loop;

    /// <summary>The --movescript value of a dev run (null otherwise).</summary>
    public static string? Spec => Core.CmdLine.DevAfter("--movescript");
    /// <summary>"--movescript auto": drills drive their own scripted movement.</summary>
    public static bool Auto => string.Equals(Spec, "auto", StringComparison.OrdinalIgnoreCase);
    /// <summary>Dev runs print landings with --movelog or --movescript.</summary>
    public static bool Log => Core.CmdLine.Dev && (Core.CmdLine.Has("--movelog") || Spec != null);

    MovementScript(float loop) => this.loop = loop;

    /// <summary>Parses a spec (null if empty or "auto"); unknown items are reported in <paramref name="error"/> and skipped.</summary>
    public static MovementScript? Parse(string? spec, ValorantProfile keys, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        var bad = new List<string>();
        float loop = 0;
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<(InputBinding, float, float)>();
        foreach (var p in parts)
        {
            if (p.StartsWith("loop=", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(p[5..], NumberStyles.Float, CultureInfo.InvariantCulture, out loop) || loop <= 0) bad.Add(p);
                continue;
            }
            int c = p.IndexOf(':');
            if (c <= 0) { bad.Add(p); continue; }
            var key = KeyOf(p[..c].Trim(), keys);
            var times = p[(c + 1)..].Split('-', 2);
            if (key.IsNone || !float.TryParse(times[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float from)) { bad.Add(p); continue; }
            float to = from + 0.08f;
            if (times.Length == 2 && !float.TryParse(times[1], NumberStyles.Float, CultureInfo.InvariantCulture, out to)) { bad.Add(p); continue; }
            list.Add((key, from, Mathf.Max(to, from + 0.001f)));
        }
        if (bad.Count > 0) error = "ignored: " + string.Join(", ", bad);
        if (list.Count == 0) return null;
        var s = new MovementScript(loop);
        s.items.AddRange(list);
        return s;
    }

    /// <summary>Is <paramref name="k"/> held at <paramref name="now"/> (seconds since GO)?</summary>
    public bool Down(InputBinding k, float now)
    {
        if (k.IsNone) return false;
        float t = loop > 0 ? now % loop : now;
        foreach (var (key, from, to) in items)
            if (key == k && t >= from && t < to) return true;
        return false;
    }

    public override string ToString() =>
        (loop > 0 ? $"loop {loop.ToString("0.##", CultureInfo.InvariantCulture)} s: " : "") +
        string.Join(", ", items.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Key} {i.From:0.###}-{i.To:0.###}")));

    static InputBinding KeyOf(string name, ValorantProfile keys)
    {
        switch (name.ToLowerInvariant())
        {
            case "forward": return keys.KeyForward;
            case "back": return keys.KeyBack;
            case "left": return keys.KeyLeft;
            case "right": return keys.KeyRight;
            case "walk": return keys.KeyWalk;
            case "crouch": return keys.KeyCrouch;
            case "jump": return keys.KeyJump.IsNone ? Key.Space : keys.KeyJump;
            // Polled actions (abilities, equip spike = plant / defuse); fire / alt fire / reload are input events, not polled.
            case "spike": return keys.Binds.Primary(GameAction.UseSpike);
            case "use": return keys.Binds.Primary(GameAction.Use);
            case "ability_c": return keys.Binds.Primary(GameAction.AbilityGrenade);
            case "ability_q": return keys.Binds.Primary(GameAction.Ability1);
            case "ability_e": return keys.Binds.Primary(GameAction.Ability2);
            case "ability_x": return keys.Binds.Primary(GameAction.Ultimate);
            case "space": return Key.Space;
            case "ctrl": case "control": return Key.Ctrl;
            case "shift": return Key.Shift;
            case "alt": return Key.Alt;
        }
        if (name.Length == 1 && char.IsLetter(name[0])) return (Key)((int)Key.A + (char.ToUpperInvariant(name[0]) - 'A'));
        return OS.FindKeycodeFromString(name);
    }
}
