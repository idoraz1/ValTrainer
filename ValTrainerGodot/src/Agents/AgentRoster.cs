using ValTrainer.Core;

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
        new("iso", "Iso", "IS", AgentRole.Duelist, L.N("Duel specialist who shields up and takes fights one enemy at a time."), new[]
        {
            A("C", "Contingency", L.N("Pushes a bulletproof wall of energy ahead of you.")),
            A("Q", "Undercut", L.N("A bolt that slips through walls and leaves enemies vulnerable.")),
            A("E", "Double Tap", L.N("Focus for a one-hit shield; shoot the orbs your kills drop to keep it."), "mobility:iso", "popup"),
            A("X", "Kill Contract", L.N("Drags you and one enemy into an arena for a one-on-one."), "peekduel"),
        }),
        new("jett", "Jett", "JE", AgentRole.Duelist, L.N("Nimble entry duelist: dash in, win the opening duel, get out before the trade."), new[]
        {
            A("C", "Cloudburst", L.N("A quick puff of smoke you can bend while it flies; it fades fast.")),
            A("Q", "Updraft", L.N("Pops you straight up for high angles or a drop from above.")),
            A("E", "Tailwind", L.N("Prime it, then burst a short way in the direction you're moving."), "mobility:jett"),
            A("X", "Blade Storm", L.N("Precise throwing knives; every kill hands the knives back."), "flick"),
            A("", "Drift", L.N("Hold jump while falling to glide down slowly.")),
        }) { Extra = new[] { "operator" } },
        new("neon", "Neon", "NE", AgentRole.Duelist, L.N("Sprinting duelist who closes the distance before defenders can line up a shot."), new[]
        {
            A("C", "Fast Lane", L.N("Two electric walls race along the floor and cut off vision.")),
            A("Q", "Relay Bolt", L.N("A bolt that bounces once and concusses where it hits.")),
            A("E", "High Gear", L.N("Sprint flat out and slide once it's charged; kills refill the slide."), "mobility:neon"),
            A("X", "Overdrive", L.N("Top speed plus a lightning beam that stays accurate while you run."), "tracking"),
        }),
        new("phoenix", "Phoenix", "PH", AgentRole.Duelist, L.N("Self-reliant duelist who flashes for himself and heals in his own flames."), new[]
        {
            A("C", "Blaze", L.N("A bending wall of flame that blocks sight and burns whoever crosses it.")),
            A("Q", "Hot Hands", L.N("A fireball that leaves a burning patch on the floor.")),
            A("E", "Curveball", L.N("A flash that hooks left or right around a corner before it pops."), "flashpeek:phoenix"),
            A("X", "Run It Back", L.N("Fight freely: die during it and you're back at your marker."), "peekduel"),
            A("", "Heating Up", L.N("Your own fire heals you instead of hurting you.")),
        }),
        new("raze", "Raze", "RA", AgentRole.Duelist, L.N("Explosive duelist who blasts into a site and flushes out its corners."), new[]
        {
            A("C", "Boom Bot", L.N("A rolling bot that chases the first enemy it spots and explodes.")),
            A("Q", "Blast Pack", L.N("A sticky charge: set it off to launch yourself or hurt enemies."), "mobility:raze"),
            A("E", "Paint Shells", L.N("A cluster grenade that splits into more blasts.")),
            A("X", "Showstopper", L.N("A rocket launcher with huge splash damage.")),
        }),
        new("reyna", "Reyna", "RE", AgentRole.Duelist, L.N("Snowballing duelist: every kill fuels a heal or an escape."), new[]
        {
            A("C", "Leer", L.N("A fragile eye that nearsights every enemy who looks at it."), "flashpeek:reyna"),
            A("Q", "Devour", L.N("Eat a soul orb from a kill to heal up fast.")),
            A("E", "Dismiss", L.N("Eat a soul orb to turn intangible and reposition."), "mobility:reyna"),
            A("X", "Empress", L.N("A frenzy of faster shooting and reloading; soul orbs never run out."), "switch"),
        }),
        new("waylay", "Waylay", "WA", AgentRole.Duelist, L.N("Light-speed duelist who dashes in, strikes and zips back to safety."), new[]
        {
            A("C", "Saturate", L.N("A burst of light that badly slows movement and firing nearby.")),
            A("Q", "Lightspeed", L.N("Up to two quick forward dashes; aim the first one up to gain height."), "mobility:waylay"),
            A("E", "Refract", L.N("Drop a beacon, then rush back to it while nothing can hurt you."), "mobility:waylay"),
            A("X", "Convergent Paths", L.N("An afterimage beam slows enemies, then you get a big speed boost.")),
        }),
        new("yoru", "Yoru", "YO", AgentRole.Duelist, L.N("Trickster duelist who fakes, teleports and flashes to strike from odd angles."), new[]
        {
            A("C", "Fakeout", L.N("A decoy that runs like you and flashes whoever shoots it.")),
            A("Q", "Blindside", L.N("A flash that ricochets off a wall before it pops."), "flashpeek:yoru"),
            A("E", "Gatecrash", L.N("Send a tether ahead and teleport to it when the moment is right."), "mobility:yoru"),
            A("X", "Dimensional Drift", L.N("Slip into another dimension: unseen and untouchable while you scout.")),
        }),

        // ---------------- initiators ----------------
        new("breach", "Breach", "BR", AgentRole.Initiator, L.N("Breaching initiator who flashes and stuns straight through walls."), new[]
        {
            A("C", "Aftershock", L.N("A charge that goes through a wall and bursts three times on the far side."), "postplant"),
            A("Q", "Flashpoint", L.N("A flash fired through a wall that pops on the other side."), "flashpeek:breach"),
            A("E", "Fault Line", L.N("A charged quake that dazes everyone along a long strip."), "siteclear"),
            A("X", "Rolling Thunder", L.N("A wide cone of quakes that throws enemies up and dazes them."), "siteclear"),
        }),
        new("fade", "Fade", "FA", AgentRole.Initiator, L.N("Hunter initiator who reveals, chases down and pins enemies in place."), new[]
        {
            A("C", "Prowler", L.N("A creature that chases an enemy or a trail and nearsights on contact."), "recon:fade"),
            A("Q", "Seize", L.N("A thrown snare that drops and pins nearby enemies, deafening and decaying them."), "siteclear"),
            A("E", "Haunt", L.N("A thrown watcher that reveals the enemies it sees and leaves trails to them."), "recon:fade"),
            A("X", "Nightfall", L.N("A wave of nightmare that marks, deafens and decays everyone it hits."), "siteclear"),
        }),
        new("gekko", "Gekko", "GK", AgentRole.Initiator, L.N("Initiator whose reusable creature buddies flash, stun and even plant for him."), new[]
        {
            A("C", "Mosh Pit", L.N("A thrown creature that spreads out and blows up after a moment."), "postplant"),
            A("Q", "Wingman", L.N("A buddy that runs ahead to concuss the first enemy, or plants and defuses."), "siteclear"),
            A("E", "Dizzy", L.N("A buddy that soars forward and blinds enemies with plasma."), "flashpeek:gekko"),
            A("X", "Thrash", L.N("Pilot a creature into the enemy, then pounce to detain everyone close by.")),
        }),
        new("kayo", "KAY/O", "KO", AgentRole.Initiator, L.N("Robot initiator who shuts down enemy abilities and flashes the way in."), new[]
        {
            A("C", "FRAG/ment", L.N("A sticky grenade that pulses damage, deadliest at its centre."), "postplant"),
            A("Q", "FLASH/drive", L.N("A flash grenade: a full throw, or a quick lob that pops sooner."), "flashpeek:kayo"),
            A("E", "ZERO/point", L.N("A knife that suppresses abilities and reveals enemies in its blast."), "siteclear"),
            A("X", "NULL/cmd", L.N("Overload to suppress everyone nearby; teammates can bring you back up.")),
        }),
        new("skye", "Skye", "SK", AgentRole.Initiator, L.N("Nature initiator who scouts with a tiger and flashes with guiding birds."), new[]
        {
            A("C", "Regrowth", L.N("Heals nearby teammates while you channel it.")),
            A("Q", "Trailblazer", L.N("Steer a tiger that pounces on an enemy and concusses them."), "siteclear"),
            A("E", "Guiding Light", L.N("Steer a hawk and burst it into a flash; you hear if it caught someone."), "flashpeek:skye"),
            A("X", "Seekers", L.N("Three seekers chase down the closest enemies and nearsight them.")),
        }),
        new("sova", "Sova", "SO", AgentRole.Initiator, L.N("Recon initiator who finds enemies with arrows and a drone before the team commits."), new[]
        {
            A("C", "Owl Drone", L.N("Fly a drone and tag an enemy with a revealing dart."), "recon:sova"),
            A("Q", "Shock Bolt", L.N("An arrow that bursts with electric damage where it lands."), "postplant"),
            A("E", "Recon Bolt", L.N("An arrow that pulses and reveals every enemy in its sight."), "recon:sova"),
            A("X", "Hunter's Fury", L.N("Up to three long energy blasts that go through walls.")),
            A("", "Uncanny Marksman", L.N("Arrows can bounce off walls; hold fire to send them farther.")),
        }),
        new("tejo", "Tejo", "TE", AgentRole.Initiator, L.N("Ballistics initiator who scouts with a drone and pounds positions with missiles."), new[]
        {
            A("C", "Stealth Drone", L.N("Fly a drone, then pulse it to suppress and reveal enemies."), "siteclear"),
            A("Q", "Special Delivery", L.N("A sticky grenade that concusses and hurts; it can bounce once."), "siteclear"),
            A("E", "Guided Salvo", L.N("Pick up to two spots on a map and send missiles there."), "postplant"),
            A("X", "Armageddon", L.N("Calls a long line of explosions along a path you draw."), "retake"),
        }),

        // ---------------- controllers ----------------
        new("astra", "Astra", "AS", AgentRole.Controller, L.N("Cosmic controller who places stars across the map and turns them into smokes, pulls or stuns."), new[]
        {
            A("C", "Gravity Well", L.N("A star becomes a well that pulls players in, then leaves them vulnerable.")),
            A("Q", "Nova Pulse", L.N("A star becomes a short pulse that concusses.")),
            A("E", "Nebula / Dissipate", L.N("A star becomes a smoke, or fades into a brief fake one and comes back."), "smokeexec:astra"),
            A("X", "Astral Form / Cosmic Divide", L.N("Place stars from above; the ultimate draws a long wall that stops bullets and sound.")),
            A("", "Astral Form", L.N("Your stars are placed from a top-down view of the map.")),
        }),
        new("brimstone", "Brimstone", "BM", AgentRole.Controller, L.N("Veteran controller who calls in smokes, fire and an orbital strike from above."), new[]
        {
            A("C", "Stim Beacon", L.N("Drops a beacon that speeds up teammates' movement and firing.")),
            A("Q", "Incendiary", L.N("A launched grenade that leaves a burning zone."), "postplant"),
            A("E", "Sky Smoke", L.N("Pick spots on a map and drop long-lasting smokes there."), "smokeexec:brimstone"),
            A("X", "Orbital Strike", L.N("Calls a lingering laser down onto a spot you choose."), "postplant"),
        }),
        new("clove", "Clove", "CL", AgentRole.Controller, L.N("Immortal controller who keeps smoking after death and fights back from it."), new[]
        {
            A("C", "Pick-me-up", L.N("Drain a fallen enemy you hurt for speed and temporary health.")),
            A("Q", "Meddle", L.N("A fragment that erupts and decays everyone caught inside.")),
            A("E", "Ruse", L.N("Set smokes from a map view; it works even after you die."), "smokeexec:clove"),
            A("X", "Not Dead Yet", L.N("Revive after dying; get a kill or assist quickly or fall again."), "peekduel"),
        }),
        new("harbor", "Harbor", "HA", AgentRole.Controller, L.N("Water controller who covers pushes with moving walls and a bulletproof dome."), new[]
        {
            A("C", "Storm Surge", L.N("A whirlpool that slows and nearsights players caught in it.")),
            A("Q", "High Tide", L.N("A steerable water wall along the floor that blocks sight and slows."), "smokeexec:harbor"),
            A("E", "Cove", L.N("A water smoke you can shield so it stops bullets."), "smokeexec:harbor"),
            A("X", "Reckoning", L.N("A surge of water that nearsights and slows the enemies it hits.")),
        }),
        new("miks", "Miks", "MI", AgentRole.Controller, L.N("Sound-powered controller who smokes, stims his team and rattles defenders."), new[]
        {
            A("C", "M-pulse", L.N("A device whose sound waves either concuss enemies or heal players.")),
            A("Q", "Harmonize", L.N("A combat stim and speed boost for you and a teammate, refreshed by kills.")),
            A("E", "Waveform", L.N("Mark spots on a map view and raise smokes there."), "smokeexec:miks"),
            A("X", "Bassquake", L.N("A charged sonic blast that knocks back, deafens and slows.")),
        }),
        new("omen", "Omen", "OM", AgentRole.Controller, L.N("Shadowy controller who smokes from afar and teleports to unexpected angles."), new[]
        {
            A("C", "Shrouded Step", L.N("A short teleport to a spot you can see.")),
            A("Q", "Paranoia", L.N("A shadow that passes through walls and nearsights everyone it touches."), "flashpeek:omen"),
            A("E", "Dark Cover", L.N("Long-range smokes you can even send through walls."), "smokeexec:omen"),
            A("X", "From the Shadows", L.N("Teleport anywhere on the map.")),
        }),
        new("viper", "Viper", "VI", AgentRole.Controller, L.N("Toxic controller who walls off space with gas that wears enemies down."), new[]
        {
            A("C", "Snake Bite", L.N("A canister that leaves a damaging acid pool."), "postplant"),
            A("Q", "Poison Cloud", L.N("A gas emitter you switch on and off, fuelled by your toxin."), "smokeexec:viper"),
            A("E", "Toxic Screen", L.N("A long gas wall that runs straight through walls, toggled with fuel."), "smokeexec:viper"),
            A("X", "Viper's Pit", L.N("A huge toxic cloud that hides you and drains enemies inside."), "postplant"),
            A("", "Toxic", L.N("Your gas eats away at enemies' health while they stay in it.")),
        }),

        // ---------------- sentinels ----------------
        new("chamber", "Chamber", "CH", AgentRole.Sentinel, L.N("Marksman sentinel with his own custom guns and a teleport to escape."), new[]
        {
            A("C", "Trademark", L.N("A trap that slows enemies who walk into its range."), "anchor:chamber"),
            A("Q", "Headhunter", L.N("A heavy pistol with a scope: precise, and deadly to the head."), "chamber:headhunter"),
            A("E", "Rendezvous", L.N("An anchor you can teleport back to while you're in range."), "anchor:chamber"),
            A("X", "Tour De Force", L.N("A custom sniper that kills with one upper-body hit and slows around the kill."), "chamber:tdf", "operator"),
        }) { Extra = new[] { "operator" } },
        new("cypher", "Cypher", "CY", AgentRole.Sentinel, L.N("Spy sentinel who watches flanks with tripwires, cages and a camera."), new[]
        {
            A("C", "Trapwire", L.N("A hidden tripwire that reveals and holds whoever crosses it."), "anchor:cypher"),
            A("Q", "Cyber Cage", L.N("A remote cage that blocks sight and slows enemies passing through."), "anchor:cypher"),
            A("E", "Spycam", L.N("A wall camera you look through and fire a tracking dart from."), "anchor:cypher"),
            A("X", "Neural Theft", L.N("Uses a dead enemy to show where their whole team is."), "retake"),
        }),
        new("deadlock", "Deadlock", "DL", AgentRole.Sentinel, L.N("Nanowire sentinel who stops pushes cold with barriers, nets and sound sensors."), new[]
        {
            A("C", "GravNet", L.N("A grenade that forces everyone inside to crouch and move slowly."), "anchor:deadlock"),
            A("Q", "Sonic Sensor", L.N("A sensor that concusses an area when it hears steps, shots or loud noise."), "anchor:deadlock", "sound"),
            A("E", "Barrier Mesh", L.N("A disc that grows barriers to block movement through a path."), "anchor:deadlock"),
            A("X", "Annihilation", L.N("Cocoons the first enemy hit and drags them to their death unless freed.")),
        }),
        new("killjoy", "Killjoy", "KJ", AgentRole.Sentinel, L.N("Gadget sentinel whose turret, alarm bot and swarm grenades lock a site down."), new[]
        {
            A("C", "Nanoswarm", L.N("A hidden grenade you set off to release a damaging swarm."), "anchor:killjoy"),
            A("Q", "ALARMBOT", L.N("A bot that hunts enemies in range and leaves them vulnerable."), "anchor:killjoy"),
            A("E", "TURRET", L.N("A turret that shoots at enemies in its field of view."), "anchor:killjoy"),
            A("X", "Lockdown", L.N("After a wind-up, detains every enemy in a large radius."), "retake"),
        }),
        new("sage", "Sage", "SA", AgentRole.Sentinel, L.N("Support sentinel who walls, slows and heals, and can bring a teammate back."), new[]
        {
            A("C", "Barrier Orb", L.N("Raises a tall, sturdy wall to block a path or boost a teammate up."), "anchor:sage"),
            A("Q", "Slow Orb", L.N("Bursts into a field that slows everyone walking through it."), "anchor:sage"),
            A("E", "Healing Orb", L.N("Heals a teammate, or yourself, over a few seconds.")),
            A("X", "Resurrection", L.N("Brings a fallen teammate back to life.")),
        }),
        new("veto", "Veto", "VE", AgentRole.Sentinel, L.N("Counter-utility sentinel who deletes enemy abilities and forces honest gunfights."), new[]
        {
            A("C", "Crosscut", L.N("A vortex you can teleport to when you look at it in range."), "anchor:veto"),
            A("Q", "Chokehold", L.N("A thrown trap that grabs the first enemy to step in and holds them."), "anchor:veto"),
            A("E", "Interceptor", L.N("Once switched on, it destroys enemy utility thrown near it."), "anchor:veto"),
            A("X", "Evolution", L.N("Full mutation: combat stim, regeneration and immunity to debuffs."), "peekduel"),
        }),
        new("vyse", "Vyse", "VY", AgentRole.Sentinel, L.N("Liquid-metal sentinel who traps, cuts off and disarms attackers."), new[]
        {
            A("C", "Razorvine", L.N("A hidden nest you trigger into a field that slows and cuts."), "anchor:vyse"),
            A("Q", "Shear", L.N("A hidden trap that raises a wall behind the first enemy through."), "anchor:vyse"),
            A("E", "Arc Rose", L.N("A hidden flower you trigger to flash everyone looking at it."), "anchor:vyse"),
            A("X", "Steel Garden", L.N("A burst of metal thorns that jams enemies' main weapons."), "retake"),
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
        AgentRole.Duelist => L.N("Takes the first fight: makes space with movement and wins the opening duel."),
        AgentRole.Initiator => L.N("Finds and flushes defenders with recon, flashes and stuns, then trades in behind."),
        AgentRole.Controller => L.N("Cuts sightlines with smokes and walls so fights happen where the team wants them."),
        _ => L.N("Locks down a site or a flank with traps and holds angles on their own."),
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
