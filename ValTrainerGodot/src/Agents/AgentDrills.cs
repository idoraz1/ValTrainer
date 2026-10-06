using ValTrainer.Core;
using ValTrainer.Modes;
using ValTrainer.Warmup;

namespace ValTrainer.Agents;

/// <summary>
/// Which drills an agent gets. Signature drills are read at runtime from the drill classes' static argument lists
/// (FlashPeekMode.Agents, ReconMode.Agents, SmokeExecuteMode.Agents, MobilityEntryMode.Agents, AnchorMode.Agents and
/// ChamberMode.Variants for Chamber), so adding or removing an agent there is all it takes. Role drills are general drills.
/// </summary>
public static class AgentDrills
{
    /// <summary>
    /// Every drill registered in <see cref="ModeRegistry.WithArg"/> and its public static string[] list: "Agents" (agent keys,
    /// e.g. FlashPeekMode.Agents → "flashpeek:skye") or "Variants" when the base key is itself an agent (ChamberMode.Variants →
    /// "chamber:tdf"). Found by reflection once; the arrays are read on every call, so a drill that adds an agent, or a new
    /// per-agent drill registered by its owner, shows up without touching this file.
    /// </summary>
    static List<(string Base, System.Reflection.FieldInfo List, bool AgentArgs)>? lists;

    static IEnumerable<(string Base, string[] Args, bool AgentArgs)> Variants()
    {
        if (lists == null)
        {
            lists = new();
            foreach (var (b, make) in ModeRegistry.WithArg)
            {
                Type t;
                try { t = make("").GetType(); } catch { continue; }
                const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                if (t.GetField("Agents", Flags) is { } f && f.FieldType == typeof(string[])) lists.Add((b.ToLowerInvariant(), f, true));
                else if (t.GetField("Variants", Flags) is { } v && v.FieldType == typeof(string[])) lists.Add((b.ToLowerInvariant(), v, false));
            }
        }
        foreach (var (b, f, agentArgs) in lists)
        {
            string[] args;
            try { args = f.GetValue(null) as string[] ?? Array.Empty<string>(); } catch { args = Array.Empty<string>(); }
            yield return (b, args, agentArgs);
        }
    }

    static bool Has(string[] list, string key) => list.Any(x => string.Equals(x?.Trim(), key, StringComparison.OrdinalIgnoreCase));

    public static string Base(string key) { int c = key.IndexOf(':'); return c > 0 ? key[..c] : key; }
    public static string Arg(string key) { int c = key.IndexOf(':'); return c > 0 ? key[(c + 1)..] : ""; }

    static readonly Dictionary<string, bool> exists = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A drill with this key is registered (cached: looking up a plain key builds every mode once).</summary>
    public static bool Exists(string key)
    {
        if (!exists.TryGetValue(key, out bool e))
        {
            try { e = ModeRegistry.Find(key) != null; } catch { e = false; }
            exists[key] = e;
        }
        return e;
    }

    /// <summary>A shared instance of the drill (name, description, category) or null.</summary>
    public static TrainingMode? Proto(string key) => WarmupDrills.Proto(key);

    public static string Name(string key) => Proto(key)?.Name ?? key;

    /// <summary>The order signature drills are listed in: the role's own drill first (an initiator's recon before its flash).</summary>
    static int Priority(AgentRole role, string b) => role switch
    {
        AgentRole.Duelist => b switch { "mobility" => 0, "flashpeek" => 1, _ => 2 },
        AgentRole.Initiator => b switch { "recon" => 0, "flashpeek" => 1, _ => 2 },
        AgentRole.Controller => b switch { "smokeexec" => 0, "flashpeek" => 1, _ => 2 },
        _ => b switch { "chamber" => 0, "anchor" => 1, _ => 2 },
    };

    /// <summary>This agent's signature drill keys ("flashpeek:phoenix", "chamber:tdf" …), most relevant first.</summary>
    public static List<string> Signature(AgentInfo a)
    {
        var list = new List<(int P, int I, string Key)>();
        int i = 0;
        foreach (var (b, args, agentArgs) in Variants())
        {
            if (agentArgs) { if (Has(args, a.Key)) list.Add((Priority(a.Role, b), i++, b + ":" + a.Key)); }
            else if (b == a.Key)
                foreach (var v in args)
                    if (!string.IsNullOrWhiteSpace(v)) list.Add((Priority(a.Role, b), i++, b + ":" + v.Trim().ToLowerInvariant()));
        }
        return list.OrderBy(x => x.P).ThenBy(x => x.I).Select(x => x.Key).Distinct().Where(Exists).ToList();
    }

    /// <summary>General drills for this agent: its extras (e.g. the Operator), then its role's, without the plain Site Anchor
    /// when the agent has its own anchor drill.</summary>
    public static List<string> Role(AgentInfo a)
    {
        var sig = Signature(a);
        bool ownAnchor = sig.Any(s => Base(s) == "anchor");
        return a.Extra.Concat(AgentRoles.Drills(a.Role))
            .Where(k => !(ownAnchor && k == "anchor"))
            .Distinct().Where(Exists).ToList();
    }

    /// <summary>Drills that practise an ability right now. A per-agent drill the agent isn't (or no longer) part of falls back
    /// to the plain drill when that is a general one (Site Anchor); otherwise it's dropped.</summary>
    public static List<string> TrainedBy(AgentInfo a, AgentAbility ab)
    {
        var sig = Signature(a);
        var res = new List<string>();
        foreach (var k in ab.Drills)
        {
            string key = k;
            if (key.Contains(':') && !sig.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                string b = Base(key);
                if (!Exists(b) || Proto(b)?.Category == "Agents") continue;
                key = b;
            }
            if (Exists(key) && !res.Contains(key)) res.Add(key);
        }
        return res;
    }

    /// <summary>The abilities a drill trains for this agent (the drill card says "Uses Curveball (E)").</summary>
    public static List<AgentAbility> UsesOf(AgentInfo a, string key) =>
        a.Abilities.Where(ab => ab.Drills.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();

    public static string AbilityLabel(AgentAbility ab) => ab.Slot.Length > 0 ? $"{ab.Name} ({ab.Slot})" : ab.Name;

    /// <summary>"Uses Curveball (E)" / "Uses Trademark (C) · Rendezvous (E)" / null when the roster doesn't say.</summary>
    public static string? UsesLine(AgentInfo a, string key)
    {
        var uses = UsesOf(a, key);
        return uses.Count == 0 ? null : "Uses " + string.Join(" · ", uses.Select(AbilityLabel));
    }

    /// <summary>Card name of a drill on an agent page: "Chamber Guns · Tour De Force" for gun variants, else the drill's name.</summary>
    public static string Title(string key) => Base(key) == "chamber" && Arg(key).Length > 0 ? $"{Name(key)} · {AgentRoster.ArgName(Arg(key))}" : Name(key);
}

/// <summary>
/// The agent Lock-In: about 10 minutes built from the Lock-In system (<see cref="WarmupPlan"/>, <see cref="WarmupRunner"/>):
/// the same check-in, an easy tracking start one tier down, an adaptive flick drill, then the agent's role drills and
/// signature drills alternating, a deathmatch with an easier last 45 s, and the same goal and cue hand-off. Everything is at
/// the real sens (no shifter). Preset keys are "agent:&lt;agent&gt;" so warmups.json and the hand-off know whose routine it was.
/// </summary>
public static class AgentRoutines
{
    public const string Prefix = "agent:";
    /// <summary>Planned lengths; a drill with its own length (most timed drills: 60 s) keeps it, see <see cref="WarmupDrills.Factory"/>.</summary>
    const float AimSeconds = 60, RoleSeconds = 60, SigSeconds = 90, DmSeconds = 150, MinDm = 90;
    /// <summary>Estimated routine length (play time + the break before each drill): role drills are added up to ~11 minutes,
    /// and the deathmatch gets shorter if long signature drills would push the routine past 12.</summary>
    const float Target = 660, MaxTotal = 720;
    const float DmEase = 45;

    static readonly Dictionary<string, float> secs = new();

    /// <summary>How long a drill runs in a routine (its own length, or <paramref name="planned"/> for untimed drills), cached per tier.</summary>
    static float Secs(string key, float planned)
    {
        string id = $"{key}@{planned}@{Main.I.Tier}";
        if (!secs.TryGetValue(id, out float s))
        {
            WarmupDrills.Factory(key, planned, out s);
            secs[id] = s;
        }
        return s;
    }

    const string CueTrack = WarmupPresets.CueTrack;
    const string CueFlick = WarmupPresets.CueFlick;
    const string CueDm = WarmupPresets.CueDm;

    public static WarmupPreset Build(AgentInfo a)
    {
        var sig = AgentDrills.Signature(a).Take(3).ToList();
        var taken = new HashSet<string>(sig, StringComparer.OrdinalIgnoreCase) { "tracking", "flick", "deathmatch" };
        var pool = a.Extra.Concat(AgentRoles.RoutineDrills(a.Role)).Concat(AgentDrills.Role(a))
            .Where(k => !taken.Contains(k) && AgentDrills.Exists(k)).Distinct().ToList();
        var aim = new[] { "tracking", "flick" }.Where(AgentDrills.Exists).ToList();
        bool hasDm = AgentDrills.Exists("deathmatch");
        float gap = WarmupPlan.StepOverhead;
        float total = aim.Sum(k => Secs(k, AimSeconds) + gap) + sig.Sum(k => Secs(k, SigSeconds) + gap) + (hasDm ? DmSeconds + gap : 0);
        var role = new List<string>();
        foreach (var k in pool)
        {
            float s = Secs(k, RoleSeconds) + gap;
            if (role.Count > 0 && total + s > Target) continue; // a shorter one may still fit
            role.Add(k);
            total += s;
            if (role.Count == 4) break;
        }
        float dm = hasDm ? Math.Max(MinDm, DmSeconds - Math.Max(0, total - MaxTotal)) : 0;

        var steps = new List<WarmupStepDef>();
        foreach (var k in aim)
            steps.Add(k == "tracking" ? new(k, AimSeconds, CueTrack, Phase.Activation, TierOffset: -1) : new(k, AimSeconds, CueFlick, Phase.Calibration, Adaptive: true));
        for (int ri = 0, si = 0; ri < role.Count || si < sig.Count;)
        {
            if (ri < role.Count) { var k = role[ri++]; steps.Add(new(k, RoleSeconds, RoleCue(k))); }
            if (si < sig.Count) { var k = sig[si++]; steps.Add(new(k, SigSeconds, SigCue(a, k))); }
        }
        if (hasDm) steps.Add(new("deathmatch", dm, CueDm, Phase.Deathmatch, EaseLast: Math.Min(DmEase, dm)));
        string roleText = $"{role.Count} {a.Role.ToString().ToLowerInvariant()} drill{(role.Count == 1 ? "" : "s")}";
        string what = sig.Count > 0 ? $"{(sig.Count == 1 ? "a signature drill" : $"{sig.Count} signature drills")}, {roleText}" : roleText;
        return new WarmupPreset(Prefix + a.Key, a.Name, $"{a.Name} Lock-In: aim, {what} and a deathmatch", 0, steps.ToArray());
    }

    /// <summary>The plan as it would run now (the player's sens and tier).</summary>
    public static WarmupPlan Plan(AgentInfo a)
    {
        return WarmupPlan.Build(Build(a), ShiftMode.Off, 0, Main.I.Sens, WarmupRunner.QuickDev);
    }

    public static void Start(AgentInfo a)
    {
        var plan = Plan(a);
        Main.I.ReturnToAgents = true; // leaving the routine (or its hand-off's MENU) comes back to the Agents screen
        WarmupRunner.Start(plan);
    }

    /// <summary>Preset for an "agent:&lt;key&gt;" key (Lock-In history and hand-off), null for anything else.</summary>
    public static WarmupPreset? Find(string? presetKey) =>
        presetKey != null && presetKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && AgentRoster.ByKey(presetKey[Prefix.Length..]) is { } a
            ? Build(a) : null;

    /// <summary>"Lock in again" on the details of an agent routine restarts that routine (null: not an agent routine).</summary>
    public static Action? Again(string? presetKey) =>
        presetKey != null && presetKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && AgentRoster.ByKey(presetKey[Prefix.Length..]) is { } a
            ? () => Start(a) : null;

    static string RoleCue(string key) => key switch
    {
        "peekduel" => "Wide swing, stop, shoot first, then get back behind cover.",
        "jigglepeek" => "Short shoulder peeks to bait the shot, then punish the miss.",
        "jumppeek" => "Jump out to see the angle and land back safe. Don't take the fight in the air.",
        "switch" => "Kill, then snap straight to the next target. Don't watch the body drop.",
        "counterstrafe" => "Tap the opposite key to stop dead, then fire. Moving shots miss.",
        "peek" => "Crosshair on the edge at head height before they swing.",
        "popup" => "See it, flick, click. Let targets go once they're gone.",
        "siteclear" => "Clear one angle at a time; never show yourself to two at once.",
        "strafebots" => "Let the crosshair follow them and shoot when it lines up with the head.",
        "flashmap" => "Hear the flash, turn away, swing back as soon as it's over.",
        "longtaps" => "Tap, let the recoil reset, tap again. Only the first bullet counts.",
        "postplant" => "Hold your post-plant angle and let them come to the spike.",
        "sound" => "Listen first, then pre-aim where the steps will come out.",
        "spray_vandal" or "spray_phantom" => "Pull down and against the pattern; the first bullets matter most.",
        "spray_transfer" => "Keep the spray going: pull down while you drag onto the next agent.",
        "microshot" => "Tiny corrections: stop on the head before you click.",
        "retake" => "Clear the site piece by piece, then go for the defuse.",
        "anchor" => "Hold your angle and stall the push. Don't chase.",
        "operator" => "Hold the angle scoped and shoot as they cross. One shot, then move.",
        "tracking" => CueTrack,
        "flick" => CueFlick,
        _ => AgentDrills.Proto(key)?.Description ?? "",
    };

    static string SigCue(AgentInfo a, string key)
    {
        var uses = AgentDrills.UsesOf(a, key);
        string ab = uses.Count > 0 ? string.Join(" and ", uses.Take(2).Select(AgentDrills.AbilityLabel)) : "your utility";
        return AgentDrills.Base(key) switch
        {
            "flashpeek" => $"Pop {ab} for yourself, swing as it blinds them, kill before they recover.",
            "recon" => $"Send {ab} first, read where they are, then clear them one angle at a time.",
            "smokeexec" => $"Smoke off their sightlines with {ab}, then take the site through the gaps.",
            "mobility" => $"Use {ab} to get in fast, stop, and win the first duel.",
            "anchor" => $"Set up {ab}, hold your angle and stall the push until it breaks.",
            "chamber" => AgentDrills.Arg(key) == "tdf"
                ? "Tour De Force: scope early, crosshair where they'll appear, one clean shot."
                : "Headhunter: one precise tap at a time, reset between shots.",
            _ => $"Use {ab}, then take the fight.",
        };
    }

    /// <summary>Short labels for the Lock-In timeline (drills added in 1.4; null = let the Lock-In decide).</summary>
    public static string? ShortLabel(string key) => AgentDrills.Base(key) switch
    {
        "flashpeek" => "FLASH",
        "recon" => "RECON",
        "smokeexec" => "SMOKES",
        "mobility" => "ENTRY",
        "chamber" => AgentDrills.Arg(key) == "tdf" ? "TDF" : "HEADHUNT",
        "anchor" => "ANCHOR",
        "microshot" => "MICRO",
        "switch" => "SWITCH",
        "popup" => "POP-UP",
        "longtaps" => "TAPS",
        "jumppeek" => "JUMP",
        "jigglepeek" => "JIGGLE",
        "postplant" => "POST",
        "retake" => "RETAKE",
        "sound" => "SOUND",
        "siteclear" => "CLEAR",
        "flashmap" => "DODGE",
        "operator" => "OP",
        "spray_phantom" => "SPRAY",
        "spray_transfer" => "TRANSFER",
        _ => null,
    };
}
