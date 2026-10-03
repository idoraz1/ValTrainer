namespace ValTrainer.Agents;

public enum AgentRole { Duelist, Initiator, Controller, Sentinel }

/// <summary>
/// One ability: its VALORANT key ("C", "Q", "E", "X"; "" for a passive), its name, a one-line summary written for
/// ValTrainer (never Riot's text) and the drills that practise it. Per-agent drill keys ("flashpeek:phoenix") only count
/// while that drill lists the agent at runtime (see <see cref="AgentDrills"/>); plain keys only while the drill exists.
/// </summary>
public sealed record AgentAbility(string Slot, string Name, string Summary, string[] Drills);

/// <summary>A playable agent: key (lower case, as used in drill keys), display name, a 2-letter monogram for the drawn
/// badge (no Riot portraits or icons), role, a one-line summary in our own words and the abilities.</summary>
public sealed record AgentInfo(string Key, string Name, string Mono, AgentRole Role, string Summary, AgentAbility[] Abilities)
{
    /// <summary>General drills this agent gets on top of its role's (e.g. the Operator for Jett and Chamber).</summary>
    public string[] Extra { get; init; } = Array.Empty<string>();
}

/// <summary>
/// All 29 playable agents (valorant-api.com, isPlayableCharacter). Names of agents and abilities are used descriptively;
/// every summary is our own wording. Keys follow the in-game keybinds (C / Q / E / X), which for Phoenix and Deadlock
/// differ from the API's internal slot names.
/// </summary>
public static class AgentRoster
{
    static AgentAbility A(string slot, string name, string summary, params string[] drills) => new(slot, name, summary, drills);

    public static readonly AgentInfo[] All =
    {
        // ---------------- duelists ----------------
        new("iso", "Iso", "IS", AgentRole.Duelist, "Duel specialist who shields up and takes fights one enemy at a time.", new[]
        {
            A("C", "Contingency", "Pushes a bulletproof wall of energy ahead of you."),
            A("Q", "Undercut", "A bolt that slips through walls and leaves enemies vulnerable."),
            A("E", "Double Tap", "Focus for a one-hit shield; shoot the orbs your kills drop to keep it.", "mobility:iso", "popup"),
            A("X", "Kill Contract", "Drags you and one enemy into an arena for a one-on-one.", "peekduel"),
        }),
        new("jett", "Jett", "JE", AgentRole.Duelist, "Nimble entry duelist: dash in, win the opening duel, get out before the trade.", new[]
        {
            A("C", "Cloudburst", "A quick puff of smoke you can bend while it flies; it fades fast."),
            A("Q", "Updraft", "Pops you straight up for high angles or a drop from above."),
            A("E", "Tailwind", "Prime it, then burst a short way in the direction you're moving.", "mobility:jett"),
            A("X", "Blade Storm", "Precise throwing knives; every kill hands the knives back.", "flick"),
            A("", "Drift", "Hold jump while falling to glide down slowly."),
        }) { Extra = new[] { "operator" } },
        new("neon", "Neon", "NE", AgentRole.Duelist, "Sprinting duelist who closes the distance before defenders can line up a shot.", new[]
        {
            A("C", "Fast Lane", "Two electric walls race along the floor and cut off vision."),
            A("Q", "Relay Bolt", "A bolt that bounces once and concusses where it hits."),
            A("E", "High Gear", "Sprint flat out and slide once it's charged; kills refill the slide.", "mobility:neon"),
            A("X", "Overdrive", "Top speed plus a lightning beam that stays accurate while you run.", "tracking"),
        }),
        new("phoenix", "Phoenix", "PH", AgentRole.Duelist, "Self-reliant duelist who flashes for himself and heals in his own flames.", new[]
        {
            A("C", "Blaze", "A bending wall of flame that blocks sight and burns whoever crosses it."),
            A("Q", "Hot Hands", "A fireball that leaves a burning patch on the floor."),
            A("E", "Curveball", "A flash that hooks left or right around a corner before it pops.", "flashpeek:phoenix"),
            A("X", "Run It Back", "Fight freely: die during it and you're back at your marker.", "peekduel"),
            A("", "Heating Up", "Your own fire heals you instead of hurting you."),
        }),
        new("raze", "Raze", "RA", AgentRole.Duelist, "Explosive duelist who blasts into a site and flushes out its corners.", new[]
        {
            A("C", "Boom Bot", "A rolling bot that chases the first enemy it spots and explodes."),
            A("Q", "Blast Pack", "A sticky charge: set it off to launch yourself or hurt enemies.", "mobility:raze"),
            A("E", "Paint Shells", "A cluster grenade that splits into more blasts."),
            A("X", "Showstopper", "A rocket launcher with huge splash damage."),
        }),
        new("reyna", "Reyna", "RE", AgentRole.Duelist, "Snowballing duelist: every kill fuels a heal or an escape.", new[]
        {
            A("C", "Leer", "A fragile eye that nearsights every enemy who looks at it.", "flashpeek:reyna"),
            A("Q", "Devour", "Eat a soul orb from a kill to heal up fast."),
            A("E", "Dismiss", "Eat a soul orb to turn intangible and reposition.", "mobility:reyna"),
            A("X", "Empress", "A frenzy of faster shooting and reloading; soul orbs never run out.", "switch"),
        }),
        new("waylay", "Waylay", "WA", AgentRole.Duelist, "Light-speed duelist who dashes in, strikes and zips back to safety.", new[]
        {
            A("C", "Saturate", "A burst of light that badly slows movement and firing nearby."),
            A("Q", "Lightspeed", "Up to two quick forward dashes; aim the first one up to gain height.", "mobility:waylay"),
            A("E", "Refract", "Drop a beacon, then rush back to it while nothing can hurt you.", "mobility:waylay"),
            A("X", "Convergent Paths", "An afterimage beam slows enemies, then you get a big speed boost."),
        }),
        new("yoru", "Yoru", "YO", AgentRole.Duelist, "Trickster duelist who fakes, teleports and flashes to strike from odd angles.", new[]
        {
            A("C", "Fakeout", "A decoy that runs like you and flashes whoever shoots it."),
            A("Q", "Blindside", "A flash that ricochets off a wall before it pops.", "flashpeek:yoru"),
            A("E", "Gatecrash", "Send a tether ahead and teleport to it when the moment is right.", "mobility:yoru"),
            A("X", "Dimensional Drift", "Slip into another dimension: unseen and untouchable while you scout."),
        }),

        // ---------------- initiators ----------------
        new("breach", "Breach", "BR", AgentRole.Initiator, "Breaching initiator who flashes and stuns straight through walls.", new[]
        {
            A("C", "Aftershock", "A charge that goes through a wall and bursts three times on the far side.", "postplant"),
            A("Q", "Flashpoint", "A flash fired through a wall that pops on the other side.", "flashpeek:breach"),
            A("E", "Fault Line", "A charged quake that dazes everyone along a long strip.", "siteclear"),
            A("X", "Rolling Thunder", "A wide cone of quakes that throws enemies up and dazes them.", "siteclear"),
        }),
        new("fade", "Fade", "FA", AgentRole.Initiator, "Hunter initiator who reveals, chases down and pins enemies in place.", new[]
        {
            A("C", "Prowler", "A creature that chases an enemy or a trail and nearsights on contact.", "recon:fade"),
            A("Q", "Seize", "A thrown snare that drops and pins nearby enemies, deafening and decaying them.", "siteclear"),
            A("E", "Haunt", "A thrown watcher that reveals the enemies it sees and leaves trails to them.", "recon:fade"),
            A("X", "Nightfall", "A wave of nightmare that marks, deafens and decays everyone it hits.", "siteclear"),
        }),
        new("gekko", "Gekko", "GK", AgentRole.Initiator, "Initiator whose reusable creature buddies flash, stun and even plant for him.", new[]
        {
            A("C", "Mosh Pit", "A thrown creature that spreads out and blows up after a moment.", "postplant"),
            A("Q", "Wingman", "A buddy that runs ahead to concuss the first enemy, or plants and defuses.", "siteclear"),
            A("E", "Dizzy", "A buddy that soars forward and blinds enemies with plasma.", "flashpeek:gekko"),
            A("X", "Thrash", "Pilot a creature into the enemy, then pounce to detain everyone close by."),
        }),
        new("kayo", "KAY/O", "KO", AgentRole.Initiator, "Robot initiator who shuts down enemy abilities and flashes the way in.", new[]
        {
            A("C", "FRAG/ment", "A sticky grenade that pulses damage, deadliest at its centre.", "postplant"),
            A("Q", "FLASH/drive", "A flash grenade: a full throw, or a quick lob that pops sooner.", "flashpeek:kayo"),
            A("E", "ZERO/point", "A knife that suppresses abilities and reveals enemies in its blast.", "siteclear"),
            A("X", "NULL/cmd", "Overload to suppress everyone nearby; teammates can bring you back up."),
        }),
        new("skye", "Skye", "SK", AgentRole.Initiator, "Nature initiator who scouts with a tiger and flashes with guiding birds.", new[]
        {
            A("C", "Regrowth", "Heals nearby teammates while you channel it."),
            A("Q", "Trailblazer", "Steer a tiger that pounces on an enemy and concusses them.", "siteclear"),
            A("E", "Guiding Light", "Steer a hawk and burst it into a flash; you hear if it caught someone.", "flashpeek:skye"),
            A("X", "Seekers", "Three seekers chase down the closest enemies and nearsight them."),
        }),
        new("sova", "Sova", "SO", AgentRole.Initiator, "Recon initiator who finds enemies with arrows and a drone before the team commits.", new[]
        {
            A("C", "Owl Drone", "Fly a drone and tag an enemy with a revealing dart.", "recon:sova"),
            A("Q", "Shock Bolt", "An arrow that bursts with electric damage where it lands.", "postplant"),
            A("E", "Recon Bolt", "An arrow that pulses and reveals every enemy in its sight.", "recon:sova"),
            A("X", "Hunter's Fury", "Up to three long energy blasts that go through walls."),
            A("", "Uncanny Marksman", "Arrows can bounce off walls; hold fire to send them farther."),
        }),
        new("tejo", "Tejo", "TE", AgentRole.Initiator, "Ballistics initiator who scouts with a drone and pounds positions with missiles.", new[]
        {
            A("C", "Stealth Drone", "Fly a drone, then pulse it to suppress and reveal enemies.", "siteclear"),
            A("Q", "Special Delivery", "A sticky grenade that concusses and hurts; it can bounce once.", "siteclear"),
            A("E", "Guided Salvo", "Pick up to two spots on a map and send missiles there.", "postplant"),
            A("X", "Armageddon", "Calls a long line of explosions along a path you draw.", "retake"),
        }),

        // ---------------- controllers ----------------
        new("astra", "Astra", "AS", AgentRole.Controller, "Cosmic controller who places stars across the map and turns them into smokes, pulls or stuns.", new[]
        {
            A("C", "Gravity Well", "A star becomes a well that pulls players in, then leaves them vulnerable."),
            A("Q", "Nova Pulse", "A star becomes a short pulse that concusses."),
            A("E", "Nebula / Dissipate", "A star becomes a smoke, or fades into a brief fake one and comes back.", "smokeexec:astra"),
            A("X", "Astral Form / Cosmic Divide", "Place stars from above; the ultimate draws a long wall that stops bullets and sound."),
            A("", "Astral Form", "Your stars are placed from a top-down view of the map."),
        }),
        new("brimstone", "Brimstone", "BM", AgentRole.Controller, "Veteran controller who calls in smokes, fire and an orbital strike from above.", new[]
        {
            A("C", "Stim Beacon", "Drops a beacon that speeds up teammates' movement and firing."),
            A("Q", "Incendiary", "A launched grenade that leaves a burning zone.", "postplant"),
            A("E", "Sky Smoke", "Pick spots on a map and drop long-lasting smokes there.", "smokeexec:brimstone"),
            A("X", "Orbital Strike", "Calls a lingering laser down onto a spot you choose.", "postplant"),
        }),
        new("clove", "Clove", "CL", AgentRole.Controller, "Immortal controller who keeps smoking after death and fights back from it.", new[]
        {
            A("C", "Pick-me-up", "Drain a fallen enemy you hurt for speed and temporary health."),
            A("Q", "Meddle", "A fragment that erupts and decays everyone caught inside."),
            A("E", "Ruse", "Set smokes from a map view; it works even after you die.", "smokeexec:clove"),
            A("X", "Not Dead Yet", "Revive after dying; get a kill or assist quickly or fall again.", "peekduel"),
        }),
        new("harbor", "Harbor", "HA", AgentRole.Controller, "Water controller who covers pushes with moving walls and a bulletproof dome.", new[]
        {
            A("C", "Storm Surge", "A whirlpool that slows and nearsights players caught in it."),
            A("Q", "High Tide", "A steerable water wall along the floor that blocks sight and slows.", "smokeexec:harbor"),
            A("E", "Cove", "A water smoke you can shield so it stops bullets.", "smokeexec:harbor"),
            A("X", "Reckoning", "A surge of water that nearsights and slows the enemies it hits."),
        }),
        new("miks", "Miks", "MI", AgentRole.Controller, "Sound-powered controller who smokes, stims his team and rattles defenders.", new[]
        {
            A("C", "M-pulse", "A device whose sound waves either concuss enemies or heal players."),
            A("Q", "Harmonize", "A combat stim and speed boost for you and a teammate, refreshed by kills."),
            A("E", "Waveform", "Mark spots on a map view and raise smokes there.", "smokeexec:miks"),
            A("X", "Bassquake", "A charged sonic blast that knocks back, deafens and slows."),
        }),
        new("omen", "Omen", "OM", AgentRole.Controller, "Shadowy controller who smokes from afar and teleports to unexpected angles.", new[]
        {
            A("C", "Shrouded Step", "A short teleport to a spot you can see."),
            A("Q", "Paranoia", "A shadow that passes through walls and nearsights everyone it touches.", "flashpeek:omen"),
            A("E", "Dark Cover", "Long-range smokes you can even send through walls.", "smokeexec:omen"),
            A("X", "From the Shadows", "Teleport anywhere on the map."),
        }),
        new("viper", "Viper", "VI", AgentRole.Controller, "Toxic controller who walls off space with gas that wears enemies down.", new[]
        {
            A("C", "Snake Bite", "A canister that leaves a damaging acid pool.", "postplant"),
            A("Q", "Poison Cloud", "A gas emitter you switch on and off, fuelled by your toxin.", "smokeexec:viper"),
            A("E", "Toxic Screen", "A long gas wall that runs straight through walls, toggled with fuel.", "smokeexec:viper"),
            A("X", "Viper's Pit", "A huge toxic cloud that hides you and drains enemies inside.", "postplant"),
            A("", "Toxic", "Your gas eats away at enemies' health while they stay in it."),
        }),

        // ---------------- sentinels ----------------
        new("chamber", "Chamber", "CH", AgentRole.Sentinel, "Marksman sentinel with his own custom guns and a teleport to escape.", new[]
        {
            A("C", "Trademark", "A trap that slows enemies who walk into its range.", "anchor:chamber"),
            A("Q", "Headhunter", "A heavy pistol with a scope: precise, and deadly to the head.", "chamber:headhunter"),
            A("E", "Rendezvous", "An anchor you can teleport back to while you're in range.", "anchor:chamber"),
            A("X", "Tour De Force", "A custom sniper that kills with one upper-body hit and slows around the kill.", "chamber:tdf", "operator"),
        }) { Extra = new[] { "operator" } },
        new("cypher", "Cypher", "CY", AgentRole.Sentinel, "Spy sentinel who watches flanks with tripwires, cages and a camera.", new[]
        {
            A("C", "Trapwire", "A hidden tripwire that reveals and holds whoever crosses it.", "anchor:cypher"),
            A("Q", "Cyber Cage", "A remote cage that blocks sight and slows enemies passing through.", "anchor:cypher"),
            A("E", "Spycam", "A wall camera you look through and fire a tracking dart from.", "anchor:cypher"),
            A("X", "Neural Theft", "Uses a dead enemy to show where their whole team is.", "retake"),
        }),
        new("deadlock", "Deadlock", "DL", AgentRole.Sentinel, "Nanowire sentinel who stops pushes cold with barriers, nets and sound sensors.", new[]
        {
            A("C", "GravNet", "A grenade that forces everyone inside to crouch and move slowly.", "anchor:deadlock"),
            A("Q", "Sonic Sensor", "A sensor that concusses an area when it hears steps, shots or loud noise.", "anchor:deadlock", "sound"),
            A("E", "Barrier Mesh", "A disc that grows barriers to block movement through a path.", "anchor:deadlock"),
            A("X", "Annihilation", "Cocoons the first enemy hit and drags them to their death unless freed."),
        }),
        new("killjoy", "Killjoy", "KJ", AgentRole.Sentinel, "Gadget sentinel whose turret, alarm bot and swarm grenades lock a site down.", new[]
        {
            A("C", "Nanoswarm", "A hidden grenade you set off to release a damaging swarm.", "anchor:killjoy"),
            A("Q", "ALARMBOT", "A bot that hunts enemies in range and leaves them vulnerable.", "anchor:killjoy"),
            A("E", "TURRET", "A turret that shoots at enemies in its field of view.", "anchor:killjoy"),
            A("X", "Lockdown", "After a wind-up, detains every enemy in a large radius.", "retake"),
        }),
        new("sage", "Sage", "SA", AgentRole.Sentinel, "Support sentinel who walls, slows and heals, and can bring a teammate back.", new[]
        {
            A("C", "Barrier Orb", "Raises a tall, sturdy wall to block a path or boost a teammate up.", "anchor:sage"),
            A("Q", "Slow Orb", "Bursts into a field that slows everyone walking through it.", "anchor:sage"),
            A("E", "Healing Orb", "Heals a teammate, or yourself, over a few seconds."),
            A("X", "Resurrection", "Brings a fallen teammate back to life."),
        }),
        new("veto", "Veto", "VE", AgentRole.Sentinel, "Counter-utility sentinel who deletes enemy abilities and forces honest gunfights.", new[]
        {
            A("C", "Crosscut", "A vortex you can teleport to when you look at it in range.", "anchor:veto"),
            A("Q", "Chokehold", "A thrown trap that grabs the first enemy to step in and holds them.", "anchor:veto"),
            A("E", "Interceptor", "Once switched on, it destroys enemy utility thrown near it.", "anchor:veto"),
            A("X", "Evolution", "Full mutation: combat stim, regeneration and immunity to debuffs.", "peekduel"),
        }),
        new("vyse", "Vyse", "VY", AgentRole.Sentinel, "Liquid-metal sentinel who traps, cuts off and disarms attackers.", new[]
        {
            A("C", "Razorvine", "A hidden nest you trigger into a field that slows and cuts.", "anchor:vyse"),
            A("Q", "Shear", "A hidden trap that raises a wall behind the first enemy through.", "anchor:vyse"),
            A("E", "Arc Rose", "A hidden flower you trigger to flash everyone looking at it.", "anchor:vyse"),
            A("X", "Steel Garden", "A burst of metal thorns that jams enemies' main weapons.", "retake"),
        }),
    };

    /// <summary>Agents of a role, alphabetical (like VALORANT's agent select).</summary>
    public static IEnumerable<AgentInfo> OfRole(AgentRole r) => All.Where(a => a.Role == r).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

    public static AgentInfo? ByKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(a => a.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Display name for a drill argument: an agent ("skye" → "Skye") or a Chamber gun ("tdf" → "Tour De Force").</summary>
    public static string ArgName(string arg) => arg.ToLowerInvariant() switch
    {
        "headhunter" => "Headhunter",
        "tdf" => "Tour De Force",
        _ => ByKey(arg)?.Name ?? (arg.Length > 0 ? char.ToUpperInvariant(arg[0]) + arg[1..] : arg),
    };
}

/// <summary>What each role does and which general drills train it (our own wording).</summary>
public static class AgentRoles
{
    public static readonly AgentRole[] All = { AgentRole.Duelist, AgentRole.Initiator, AgentRole.Controller, AgentRole.Sentinel };

    public static string Name(AgentRole r) => r.ToString();

    public static string Summary(AgentRole r) => r switch
    {
        AgentRole.Duelist => "Takes the first fight: makes space with movement and wins the opening duel.",
        AgentRole.Initiator => "Finds and flushes defenders with recon, flashes and stuns, then trades in behind.",
        AgentRole.Controller => "Cuts sightlines with smokes and walls so fights happen where the team wants them.",
        _ => "Locks down a site or a flank with traps and holds angles on their own.",
    };

    /// <summary>General drills for the role, most important first (agent pages list them; missing keys are skipped).</summary>
    public static string[] Drills(AgentRole r) => r switch
    {
        AgentRole.Duelist => new[] { "flick", "peekduel", "jigglepeek", "jumppeek", "switch", "deathmatch" },
        AgentRole.Initiator => new[] { "siteclear", "flashmap", "peek", "popup", "strafebots", "spray_transfer" },
        AgentRole.Controller => new[] { "postplant", "longtaps", "sound", "tracking", "spray_vandal", "retake" },
        _ => new[] { "anchor", "retake", "sound", "peek", "microshot", "longtaps" },
    };

    /// <summary>Order in which the role's drills join an agent warm-up (game-like drills first; no deathmatch, it always ends the routine).</summary>
    public static string[] RoutineDrills(AgentRole r) => r switch
    {
        AgentRole.Duelist => new[] { "peekduel", "jigglepeek", "switch", "jumppeek", "counterstrafe" },
        AgentRole.Initiator => new[] { "peek", "popup", "siteclear", "strafebots", "flashmap" },
        AgentRole.Controller => new[] { "longtaps", "postplant", "sound", "spray_vandal", "strafebots" },
        _ => new[] { "peek", "microshot", "sound", "retake", "longtaps" },
    };
}
