# Changelog

All notable changes to ValTrainer are listed here, newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and ValTrainer uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). CONTRIBUTING.md explains what counts as a major, minor
or patch change. Add every change under **Unreleased**; `tools\version.ps1` turns that section into the next version.

## [Unreleased]

## [1.16.0] - 2026-10-18

### Changed
- Movement feels heavier, closer to VALORANT (a player said on Discord that it felt too light and too fast). Your run speed now depends on the gun in hand, using VALORANT's numbers: 5.40 m/s with the Vandal, Phantom or Sheriff, and 5.13 m/s with the Operator (3.69 m/s scoped). Walking, crouching and aiming are slower in proportion, and you're accurate below 27.5% of the gun's run speed, as in VALORANT. Before, every gun ran at the rifle's 5.40 m/s.
- You speed up like in VALORANT: about 0.3 s from standing to full run speed (it was 0.15 s, twice as fast). Stopping and counter-strafing are unchanged (they already matched Riot's own numbers). The Mixed Peeks bots strafe with the same acceleration.
- Jumps last as long as in VALORANT: 0.71 s in the air (it was 0.60 s), with the same height, so the ledges you can reach by jumping and crouch-jumping don't change. Riot doesn't publish these numbers; both were frame-counted from a VALORANT recording.
- The gun bob while running matches your steps: one sway left and right every two steps and one small dip per step, like VALORANT's gun sway. It used to bob more than twice as fast as a footstep, which made running look light and twitchy. The idle sway is unchanged.

### Added
- You hear your own footsteps when you run, quieter than an enemy's. Walking (shift), crouching and jumping are silent, as in VALORANT. Previously the only sound was the landing.

## [1.15.0] - 2026-10-18

### Added
- Free play (suggested on Discord): a FREE PLAY button (or F) next to START in the drill preview. The drill runs with no clock and no score on screen, saves nothing, and starts over by itself when its time is up, so you can just keep shooting. Esc → menu ends it. It's there for every drill the preview can demo (not the map drills, Reaction Test, Sens Finder or Crosshair Finder).
- Gekko's Dizzy in Flash Dodge works like in VALORANT now: she's thrown through the air, forms, hovers for a second, and spits one plasma blast at you if she sees you. Turning away doesn't help: shoot her first (20 HP, any bullet) or get out of her sight. Her blind is a blue goo blob that leaves the edges of your screen visible (1 s, then a 1 s fade).
- Vyse's Arc Rose can be shot during its windup (it breaks without flashing anyone), as in VALORANT.

### Changed
- Flashes look and sound much closer to VALORANT (suggested on Discord). Each agent's blind is now a tinted screen with light rays coming from where the flash popped (so a flash that pops to your side streams in from that side), not a plain white-out. It shrinks as the full blind runs out, and visibility comes back fastest at the start of the fade.
- New sounds for every flash: a throw, a flight sound, a windup that ends exactly on the pop (so you can time your turn by ear, like in VALORANT), a pop of its own per agent, and the "flashed" ring. Gekko's Dizzy has her own set (throw, charge, plasma blast, splat, and a sound when you shoot her).
- Flash timings checked against VALORANT patch 13.08. KAY/O's FLASH/drive flies faster when thrown overhand (18 m/s) and slower underhand (6.3 m/s), and the overhand one pops 0.8 s after it bounces (never later than 1.6 s). Yoru's Blindside flies faster (29 m/s) and stays invisible until it bounces. Skye's Guiding Light pops by itself at the end of its 2 s flight.

### Not done (and why)
- The flashes aren't 1:1 copies of VALORANT. Riot's sounds, images and effects can't be used, so everything is made from scratch to sound and look close. Agent voice lines aren't included at all.
- Some numbers aren't published by Riot, so they're estimates: how fast Phoenix's Curveball flies, how much shorter a flash gets with angle and distance, the short blind when a flash pops behind you, and how fast Dizzy drifts while she hovers.
- Gekko can't pick Dizzy back up as a globule in the drill (she simply disappears), and Reyna's Leer and Fade's Prowler aren't in Flash Dodge: they're nearsights, not flashes.

## [1.14.1] - 2026-10-17

### Changed
- Flash Dodge and Sound Lock start right away again, like the other drills on a VALORANT map. Their preview had to load the whole map a second time, which could be too much for some PCs (a player reported a crash when choosing Flash Dodge).

### Fixed
- Report a bug after a crash: the report now includes the end of the log from the session that crashed (it was only sending the log of the session you restarted into, so the crash itself was missing).

## [1.14.0] - 2026-10-16

### Added
- Drill previews: clicking a drill in the menu opens a preview instead of starting it right away. The drill plays live in it, run by the built-in simulated player (muted): it scopes in with the Operator, sprays the full length your difficulty asks for, counter-strafes, and turns flashes off-screen in Flash Dodge. Next to it: what the drill is, how long it runs, the weapon, which coach skills it trains, your best score, badge and recent runs at the chosen difficulty, and a difficulty picker. START (Space / Enter) plays it, BACK (Esc) goes back. Reaction Test, Sens Finder and Crosshair Finder run on your own reactions and picks, so they show a short note instead of a demo. Map drills (Site Clear, Deathmatch, Post-Plant, Retake, Site Anchor) and the Agents screen's drills still start right away.

### Fixed
- Icebox: the crates beside Yellow now sit against the container. A slit between them let defenders see and shoot through from across B.

## [1.13.0] - 2026-10-16

### Added
- Suggest a feature: a new button in the menu and in Settings › About sends your idea (with an optional category: new drill, agent, map, coach, UI) to #suggestions on the ValTrainer Discord. Only the ValTrainer version goes with it, nothing about your PC. When an update adds it, your post is marked Added.

### Changed
- Settings › Sensitivity: the value between the arrows is now a box you click and type into (Enter). The arrows still step ±0.01 (Shift ±0.001). Typing or an arrow switches to your own sensitivity; "VALORANT" brings back the imported one.
- Pause menu: the sensitivity box can be clicked and typed into during a drill (in 1.12.0 it ignored clicks). Clicking away applies the value, and Esc just stops typing.
- Report a bug: after sending, you're invited to the ValTrainer Discord to follow your report (JOIN DISCORD), and VIEW MY REPORT opens your post. We reply there and mark it Fixed when an update fixes it.

## [1.12.0] - 2026-10-16

### Added
- Type an exact sensitivity: in Settings › Sensitivity ("Type a sensitivity" + SET) or by clicking the number in the pause menu. "0.35" and "0,35" both work.

### Fixed
- Map fixes from a community bug report (thanks VDOW!):
  - **Ascent:** the Heaven Pillar crate sits against the Heaven railing with a second crate on top, and the site's central stack is taller.
  - **Bind:** a jumpable box was added in the back-site corner, and three low boxes are now full height.
  - **Breeze:** the back-site box is flush with the wall and taller, and the A Main exit crate is raised.
  - **Haven:** the Link-exit and Long-exit crates are double height, the C-site logs step up (crouch-jump onto the short pile, then onto the long one), and you can jump through C Window.
  - **Icebox:** a barred tunnel at ground level under B Hall now lets you see and shoot between the site and back site (nobody can walk through), the Orange crates sit against the containers, the B Green hanging container that's gone from the real map was removed, and several crates were adjusted.
  - **Split:** a see-through gap between two walls was closed, along with three more hairline gaps; Icebox had several as well.
- Crates no longer look hollow from eye height. Their tops sat a few centimetres below the rim and read as an open tray.
- Rewind: when a shot hits while the crosshair is still flying, that engagement's reaction / flick / settle / click split no longer comes out in the wrong order.

## [1.11.0] - 2026-10-15

### Added
- Rewind: replay any run from the results screen in the real game graphics: the same map, the bots moving, crouching, shooting and dying as they did, your own camera, gun and crosshair. On top of that you get the aim analysis: a crosshair trail anchored in the world, a ring on the target, every shot, slow motion (0.25×/0.5×) and a 4× close-up. A timeline marks every engagement, kill, death and coach moment. Press V for the 2D aim view. The 3D replay is recorded for new runs (a small file next to each run); older runs open in 2D. A short loading bar shows while the replay world is built.
- Utility (flashes, the spike, smokes, abilities) isn't shown in the 3D replay yet, and replays are silent.
- Rewind recordings clean themselves up. ValTrainer keeps your last 20 runs plus up to 3 examples of each problem the coach still sees, so "Show me" keeps working. It deletes examples of problems you've fixed and anything older than 30 days.
- Target Switch, Operator and Spray Transfer now feed the aim coach:
  - Target Switch: flicks between targets.
  - Operator: crosshair placement while holding the gap.
  - Spray Transfer: vertical spray control; the drag between agents isn't counted as spray error.
- Settings › About › Folders shows the recordings folder, how many runs it holds and their size. OPEN shows the folder; DELETE ALL (click twice) removes every recording. Your stats and coach results are kept.
- The Rewind's side panel breaks each engagement into reaction, flick, settle and click time. It also shows your shot spread around the target, first-bullet accuracy, shots fired while moving, and over/undershoot by flick direction.
- "Show me" on coach problems (results screen and coach profile): jumps straight to the moments of a recent run where the problem happened, e.g. each overshot flick or each time your crosshair was too low. Everything runs offline from your own runs.

### Fixed
- Aim coach accuracy. These fixes also apply to the runs you already played:
  - Sound Lock's time from spotting an enemy to hitting it no longer counts as reaction time. It's a different skill, and one Sound Lock run could rate Reaction as Iron.
  - Flashes in Site Anchor no longer rate the Utility skill. Holding an angle you get flashed by design; Utility is measured in Flash Dodge.
  - Crosshair placement only counts enemies that appear within 60° of your crosshair, in every drill. Some agent and map drills also counted enemies beside or behind you.
  - Mixed Peeks and Sound Lock count only the height of your crosshair, not its side-to-side position, because peeks can come from either side.
- "Crosshair too wide" now recommends drills that measure it: Peek Duels, Site Clear and Site Anchor.

## [1.10.0] - 2026-10-14

### Added
- Mixed Peeks (Movement): hold an angle against short, wide, crouched, double, jiggle, shoulder, walking and repeat peeks with audible approaches and reactive baits. Difficulty sets which peeks appear, how fast and unpredictable they are, and how quickly baits punish; runs feed the aim coach. Contributed by @Widowrasko (#1).
- Drills made by the community show a COMMUNITY tag on their card and credit their creator before the drill starts.

### Fixed
- Jump takeoffs are silent; landing cues distinguish standing jumps, crouch-jump techniques and high drops with a stable impact-speed threshold.
- Bots stop playing footsteps while airborne during jump peeks.

## [1.9.0] - 2026-10-11

### Added

- **Report a bug from inside ValTrainer:** a REPORT A BUG button on the menu (and in Settings → About → Help & feedback) opens a short form: a title, what happened, optional steps and what you expected, and your Discord name if you'd like a reply. SEND posts it to #bug-reports on the ValTrainer Discord together with your version, PC and graphics info, the settings that matter for bugs and the end of the log. The form shows exactly what gets sent, and your Windows user name, PC name and folders are removed first. COPY REPORT copies it to paste anywhere, and OPEN ON GITHUB still opens the GitHub issue form.

## [1.8.0] - 2026-10-09

### Added

- **ValTrainer Discord:** a DISCORD button on the menu and in Settings → About (Help & feedback) opens the invite (https://discord.gg/Y6UKUX89C8): release news, bug reports, suggestions, help and voice chats by rank.

## [1.7.1] - 2026-10-09

### Fixed

- **Peek Duels:** after the bot killed you, you never came back (from the menu and from agent training). You now respawn behind the corner a moment later and the next duel starts.
- **Only one ValTrainer at a time:** opening ValTrainer while it's already running brings the open window to the front (restoring it if it was minimized) instead of starting a second copy.

## [1.7.0] - 2026-10-08

### Added

- **Support ValTrainer:** a SUPPORT VALTRAINER button on the menu and in Settings → About opens [Buy Me a Coffee](https://buymeacoffee.com/idoraz1) or [Ko-fi](https://ko-fi.com/idoraz1) in your browser. A short panel asks once on first launch (after the updates question) and then at most every 14 days; DON'T SHOW AGAIN or Settings → About → Support reminders turns it off. ValTrainer stays free either way, and the panel makes no network requests.

## [1.6.0] - 2026-10-08

### Added

- **Crosshair Finder for every crosshair tab:** pick PRIMARY, ADS or SNIPER. ADS rounds are played aiming down sights with the Vandal; SNIPER tests the scope's centre dot with the Operator. The result changes only the tab you tested and keeps the rest of your profile, and its code turns on Use Advanced Options when VALORANT needs it.
- **See all your crosshairs:** Settings → Crosshair (and the menu, for split profiles) shows your PRIMARY, ADS and SNIPER crosshairs, with a line saying where each one is used.
- **Paste a crosshair code** in Settings → Crosshair if your crosshair didn't import.

### Changed

- **Aiming down sights looks like VALORANT:** the gun stays low and you aim over it, a thin sight outline (the "box") fades in around your crosshair, and the zoom eases in instead of jumping. Your sensitivity while aiming is unchanged.
- **New sniper scopes:** the Operator scope has VALORANT's eyepiece look with a full reticle and range marks and fades in after the gun is raised, and Chamber's Tour de Force has its own orange scope. Moving while scoped with the Operator is slower (72%), as in VALORANT.
- The Crosshair setting's second option is now CUSTOM (a Crosshair Finder result or a pasted code). A Finder result follows later changes you make in VALORANT to your other tabs.

### Fixed

- **Split crosshairs:** ValTrainer now follows VALORANT's Use Advanced Options switch, so your ADS crosshair and sniper dot appear exactly when they do in the game, and crosshairs with only vertical lines (horizontal length 0) are drawn instead of disappearing.
- Crosshairs saved in VALORANT's older settings format import correctly again (dot-only crosshairs were invisible), and unreadable crosshair data falls back to VALORANT's default crosshair with a note in Settings instead of an old one.
- Crosshair codes keep everything VALORANT writes (fade, spectated crosshair, override settings and unknown sections), and the code ValTrainer shows is a complete code VALORANT imports as is.
- Firing-error lines rest where VALORANT draws them, and the crosshair uses the aiming accuracy while aiming down sights.

## [1.5.0] - 2026-10-07

### Added

- **Lock-In replaces Warm-up.** A routine built from sports-psychology and motor-learning research to get you ready for ranked: about 12 minutes (or a 7-minute Short), all at your real sensitivity, going from easy drills to drills that adapt to you, then bots and a deathmatch. Training Day (about 20 minutes) is for practice days and is the only routine with the sens shifter (off by default, at most 10%).
  - **Check-in:** rate your energy and confidence, then pick one goal for your games ("crosshair at head height") and one cue word. An optional breathing guide (slow breaths, longer out than in) turns itself on when you feel wired.
  - **Adaptive drills:** Head Flicks, Spidershot and Gridshot adjust target size, spread and timing to keep you near 80% hits, and report the level you held.
  - **Finish on a good note:** the deathmatch bots get easier for the last minute.
  - **Hand-off:** your goal and cue, a three-step buy-phase routine, "queue now" tips and a LAUNCH VALORANT button when the Riot Client is installed. Results compare you with your usual range in neutral words, plus what went well; the graph and table are under DETAILS.
  - **Setup check:** the Lock-In screen shows whether your sens, FOV, display mode, monitor, VSync and FPS cap match VALORANT, plus a short checklist (same headset and volume, a lamp on, warm hands).
  - **Stop when you're ready:** once your adaptive drills are back in your usual range (after 3 or more Lock-Ins), you can skip straight to the deathmatch. It's only offered, never automatic.
  - **Match log (optional, stays on your PC):** after a Lock-In the menu asks once how your next match went and whether you felt locked in. After 15 answers, Lock-In shows what seems to work for you (energy before, breathing, full routine or not), labelled as a small personal sample.
  - Agent routines on the Agents screen use the same flow ("LOCK IN AS JETT").
  - Your warm-up history is kept. The sens shifter is switched off once, because shifting sensitivity right before ranked isn't supported by the research.
- **Aim targets look like VALORANT enemies:** a dark body with a fresnel rim and an outline in your enemy highlight colour, so they stand out on light and dark surfaces in every highlight colour. Settings → Enemies → Aim drill targets → CLASSIC brings back the old solid spheres.

### Changed

- The Range has a darker lower wall band and darker cover panels, and less haze, so targets have more contrast.
- **Crosshair visibility hint** in Settings → Crosshair and on the Lock-In screen: a warning when your crosshair is close to your enemy highlight colour or fades into common map colours, with a link to the Crosshair Finder.
- **Colour-blind friendly results:** better/worse is now also shown with ▲ / ▼ / = marks, not only green and red (Lock-In results, the drill table and the history graph's best/latest points).

## [1.4.4] - 2026-10-06

### Fixed

- **Automatic updates could break the installed app.** When ValTrainer took a few seconds to close after starting an update, Setup removed part of the old version, failed to replace ValTrainer.exe and rolled back, leaving an install whose update checks failed with no message. Setup now waits until ValTrainer has closed before it changes anything (and the uninstaller does too). If your update checks show an error about missing or damaged files, reinstall from [valtrainer.github.io](https://valtrainer.github.io/).
- Settings → About always says why an update check failed (it could show an empty line), and the log records the full error.

## [1.4.3] - 2026-10-04

### Added

- **Your VALORANT keybinds everywhere.** ValTrainer now imports all your keybinds from VALORANT (primary and secondary binds, keys, mouse buttons and the scroll wheel), applied on top of VALORANT's defaults and including actions you unbound. Movement, walk, crouch, jump, fire, alt fire / scope, reload, plant/defuse and every agent ability use them, and so do the on-screen hints. Settings → Imported from VALORANT lists them; hover a row to see every action.

## [1.4.2] - 2026-10-04

### Added

- **Copy your crosshair code any time:** a COPY CROSSHAIR CODE button under the crosshair preview on the menu, and in Settings → Crosshair the code of the crosshair ValTrainer uses now plus your last Crosshair Finder result, each with a COPY button. Paste it in VALORANT → Settings → Crosshair → Import Profile Code.

## [1.4.1] - 2026-10-03

### Changed

- **Updates are now opt-in.** On first start (and once after updating), ValTrainer asks "Check GitHub for new versions?". Until you choose ENABLE UPDATES it makes no network requests at all. CHECK NOW in Settings → About still checks once when you click it. If you had already turned update checks off, you aren't asked again.
- Releases are ready for free code signing through SignPath Foundation (see the [code signing policy](https://valtrainer.github.io/code-signing-policy.html)). The exe and installer now carry the full product version (x.y.z.0).
- ValTrainer has been submitted to winget, Windows' package manager. Once Microsoft approves it: `winget install Valtrainer.ValTrainer`.

### Fixed

- The warm-up summary no longer shows "DEMO DATA" in showcase screenshots.
- The simulated test player could produce invalid mouse movement at low frame rates, breaking sped-up Sens Finder test runs.

## [1.4.0] - 2026-10-03

### Added

- **Agents screen** (AGENTS on the menu): all 29 agents grouped by role. Each agent page shows the agent's abilities and which drill trains each one, signature drills played with that agent's own utility, drills for its role, and a 10-minute agent warm-up (sens shifter included).
- **Agent drills**:
  - **Flash & Peek**: throw your own flash (Phoenix, Skye, Yoru, KAY/O, Breach, Gekko) or nearsight (Reyna's Leer, Omen's Paranoia), then swing and kill the blinded defenders. Each ability works like the real one, and you can still flash yourself.
  - **Recon & Clear**: Sova's Recon Bolt (charge and bounces) or Fade's Haunt reveals defenders through walls before you clear the site.
  - **Smoke Execute** for Brimstone, Omen, Astra, Clove, Viper, Harbor and Miks: plan on a top-down tactical map, place smokes with the agent's own mechanic, then take the site while smoked defenders can't see you. Scored on the sightlines you cut and how close you got to the standard smokes, with a top-down replay of every round.
  - **Mobility Entry** for Jett (Tailwind, Updraft, and an Op + dash variant), Neon (sprint and slide), Raze (satchel boost), Reyna (Dismiss, Devour), Yoru (Gatecrash), Waylay (Lightspeed, Refract) and Iso (Double Tap shield).
  - **Chamber Guns**: Headhunter one-taps and Tour De Force holds.
  - **Site Anchor**: hold a site alone while 2–4 attackers execute (entry flashes, swings, spike plant), plain or with a sentinel setup you place like in VALORANT: Killjoy, Cypher, Deadlock, Vyse, Sage, Veto or Chamber.
- **Jumping**: jump and crouch-jump onto ledges, air strafing, airborne and landing inaccuracy, and fall damage. Your jump keybind (including mouse wheel) is imported from VALORANT. Your ability keybinds are imported too.
- **New drills**:
  - **Microshot**: tiny, close heads for small corrections.
  - **Target Switch**: three strafing agents; switch fast and watch your overflicks.
  - **Pop-up Reflex**: heads that vanish in a split second.
  - **Long-range Taps**: 30–50 m Vandal taps, with a ring that shows when the recoil has reset.
  - **Jump Peek**: spot an Operator mid-jump, then punish his miss.
  - **Jiggle Peek**: bait the Op with your shoulder, then swing during his bolt.
  - **Post-Plant**: hold an on-site, off-angle or crossfire spot while defenders retake and go for the defuse.
  - **Retake**: come in as the last defender, clear the attackers and defuse (key 4, 7 s with the half-defuse checkpoint) before the spike blows.
  - **Sound Lock**: hear positional footsteps (running audible, walking silent, muffled through walls) and pre-aim the corner they'll peek from.
- **New maps**: **Icebox** (B Main → B Site: Yellow, Top Site and the Bridge, Snowman, Kitchen, Tube, Hut) and **Breeze** (A Lobby → A Main → A Site: the pyramids and pool, Yellow, A Bridge, Mid Doors), traced at true scale from the official layouts, for every map drill and Deathmatch.
- Every map now has plant spots, retake routes and the standard controller smokes.

### Changed

- The menu now fits every drill on one screen. It has a compact map selector, and the Career screen lists agent drills per agent.
- The aim coach now also reviews the new drills.

## [1.3.0] - 2026-10-02

### Added

- **Automatic updates**: ValTrainer now updates itself, like Discord or Steam. When a new version is out, it downloads in the background (the menu shows "Downloading update 42%") and is checked against the release's published SHA-256 hash. Then the menu says "ValTrainer x.y.z is ready — RESTART TO UPDATE". Click it, or keep playing and the update installs the next time you start ValTrainer, before the menu. It takes a few seconds, ValTrainer restarts by itself, and your settings and stats are kept. This works for the installer version and the portable version; the portable version replaces its own `ValTrainer.exe`. ValTrainer now checks for updates at startup and every 6 hours instead of once a day. Settings → About adds "Download updates automatically" (on by default), the update status and a restart button. Skipped versions are never downloaded. If an update is damaged or doesn't install, ValTrainer deletes it and links to the release page instead. If you installed for all users, Windows asks for permission when you click RESTART TO UPDATE. Updating from a version before this one still needs the new installer once, run by hand.

## [1.2.0] - 2026-10-02

### Added

- **Deathmatch**: a VALORANT-style free-for-all against 3–6 bots (by map size) at your tier on Ascent, Bind, Haven or Split. First to 30 kills or the most kills in 5 minutes. Bots roam the map, hunt you, hold angles and fight each other. You respawn after 1.5 s with brief spawn protection, every kill reloads your gun, and the fallen drop health packs.
- **Warm up**: a guided warm-up routine (WARM UP on the menu). Quick (≈8 min), Standard (≈15 min) and Pro (≈25 min) run drills at your tier from easy to hard: smooth tracking and big targets first, then flicks and target switching, then strafing bots, movement and peeks, then a deathmatch. A card between drills shows what's next and the sens; you can start it early, skip it or finish the warm-up there.
- **Sens shifter** (part of the warm-up): the first drills start a little above your sens (+10, +20 or +35%) or below it, then step back to your sens. The rest of the warm-up is always at your real sens. You can turn it off, and the screen notes that there's little evidence for it either way.
- **Warm-up summary with a lock-in graph**: shows each drill as a % of your usual level (your 7-day average), with your sens over the warm-up drawn on top. It also gives start → end comparisons (for example flick time-to-kill and tracking on-target %), tells you whether you're warmed up, and compares this warm-up with your last ones. Warm-ups are saved to `warmups.json`. Drills played at a shifted sens stay out of your stats so they don't skew the coach's sens advice.
- **Crosshair Finder** (Coach): finds the crosshair you see and shoot best with, in about 6 minutes. Part 1 ranks crosshair colours and outlines by contrast against the maps and your enemy highlight colour, then times how fast you spot each one. Part 2 starts with a quick shape pick: shoot 2–4 of nine shapes to try (dot, small cross, framed dot, dynamic cross, closed plus, plus + dot, circle, circle + dot, hollow square. VALORANT can't draw a real circle or box, so like the community versions they're built from short, thick lines). Four recommended ones are already picked, so you can just shoot START. Your picks then play a bracket in short rounds of flicks, far-head micro-adjusts and a kill. Your aim decides, your "which felt better?" pick breaks ties, and the winner then faces your current crosshair. You get the result as a VALORANT crosshair code to copy into VALORANT (Settings → Crosshair → Import Profile Code); ValTrainer never changes VALORANT. "Use in ValTrainer" switches ValTrainer's crosshair to the result, and Settings → Crosshair switches back to VALORANT's.

### Fixed

- **More realistic rank estimates**: the coach rated many players about a rank too high (for example "Platinum 3 – Diamond" for a Gold player). The estimate is now based on real VALORANT rank and reaction-time data. It allows for drills played at easier tiers, a single strong skill can no longer carry it, and it leans toward the average player unless your runs clearly show otherwise. The range around it is wider and honest about the uncertainty, and the profile now labels it "aim only": your in-game rank also depends on game sense, comms and utility.

## [1.1.0] - 2026-10-02

### Added

- **Weapon hand setting**: Settings → Gameplay → Weapon hand switches the first-person gun between right and left hand. Right is the default; ValTrainer no longer follows VALORANT's left-handed option on its own (the setting shows which hand VALORANT uses).

### Changed

- **Map drills now use true-scale blockouts of the real map areas** (Ascent A Main → A Site, Bind Hookah → B Site, Haven C Long → C Site, Split A Main → A Site), traced from the official map layouts and callout positions, with real cover, heights, defender spots and flash lineups.

### Fixed

- Flash Dodge can now use defender spots on raised sites (for example Ascent A, which is about 1 m above A Main); only heaven-height spots are left out.

## [1.0.0] - 2026-10-02

The first public release of ValTrainer, a free aim, movement and utility trainer that plays with your own VALORANT settings.

### Added

- **16 drills**, each scaled to your difficulty tier:
  - Aim: Gridshot, Head Flicks, Spidershot and Strafe Tracking (a target that strafes with realistic stops and reversals).
  - VALORANT-style bot drills: Strafe Bots, Peek Practice (bots wide-swing and, on higher tiers, jiggle-peek) and Operator (2.5× scope, bolt action, bots crossing a gap at 22–45 m).
  - Recoil: Vandal Spray and Phantom Spray, with a spray chart and coaching ("pull down more", "counter the drift right") after every spray, and Spray Transfer (kill every agent in one spray).
  - Movement: Counter-Strafe, and Peek Duels where the bot holding the angle shoots back.
  - Maps: Site Clear through real chokes on Ascent, Bind, Haven and Split, against defenders holding real angles.
  - Utility: Flash Dodge against Phoenix, Breach, KAY/O, Skye, Yoru, Vyse and Gekko flashes, using VALORANT's on-screen blind rule and each agent's timings and sounds.
  - Reaction Test.
- **5 difficulty tiers calibrated to ranks**: Rookie (Iron–Bronze), Regular (Silver–Gold), Veteran (Platinum–Diamond), Elite (Ascendant–Immortal) and Pro (Radiant).
  - Bots react with a human-like delay (480 ms on Rookie down to 180 ms on Pro), move their crosshair onto you and fire the Vandal with a per-shot hit chance against your 100 HP + 50 shield.
  - Target sizes, speeds and time limits follow aim-benchmark data.
  - Every result shows the rank tier you performed at, and personal bests are kept per tier.
- **Your VALORANT settings, imported automatically** every time ValTrainer starts. It only reads VALORANT's config files and never touches the game.
  - Sensitivity with the scoped and ADS multipliers, using VALORANT's math (0.07° per count, 103° horizontal FOV) and raw mouse input, so your cm/360 is identical.
  - Your crosshair profile, drawn pixel for pixel, including movement and firing error.
  - The enemy highlight colour.
  - Movement, walk and crouch keybinds, left-handed weapon, and hold or toggle scope.
  - Resolution, display mode, monitor, VSync and FPS cap.
  - Several VALORANT accounts on one PC, a re-import button, and an override for everything in Settings.
- **Weapons**:
  - Vandal (9.75 rounds/s, 25 rounds) and Phantom (11 rounds/s, 30 rounds, damage falloff) with VALORANT-style recoil: a vertical climb that plateaus, side-locked horizontal drift, spread that grows per bullet, and recovery after a tap or a full spray.
  - Operator with a 2.5× scope and bolt action.
  - Movement error for running and walking, accuracy below 27.5% of run speed (what makes counter-strafing work), and VALORANT's stop timings.
- **Graphics**: a different look for each map, HDRI skies, sun shadows, ambient occlusion and glow, animated agents with VALORANT-style enemy outlines, a first-person viewmodel, muzzle flash, tracers and bullet holes. Four presets: Low (competitive), Medium, High and Ultra.
- **Aim coach** (COACH on the menu): every run is recorded on your PC and analysed.
  - An estimated rank, overall and for 8 skills: flicking, precision, tracking, reaction, crosshair placement, spray, movement and utility, each with a confidence level.
  - Your problems, most important first, each with your numbers, why it happens, how to fix it and which drills to practise. It spots over- and under-flicking, missing micro-adjustments, clicking while the crosshair is still moving, slow or shaky tracking, a low crosshair, clumsy stops, late spray pull-down and more, and lists your strengths.
  - A run review on every results screen: a summary, the top problems with fixes, and what went well.
  - Sensitivity advice: a recommended sens that changes by at most 18% at a time and stays within 20–80 cm/360.
- **Sens Finder**: a blind A/B test of about 13 minutes. You alternate between two unlabelled sensitivities with flicks and strafe tracking, and it narrows down to the one you score best with. You get the VALORANT sens, eDPI and cm/360 and can use it in ValTrainer straight away. Your VALORANT settings are never changed.
- **Stats**: personal bests per drill and tier, averages over your last runs, and your career totals.
- **Windows installer and portable zip**, with SHA-256 checksums. Nothing else to install (.NET is bundled).
- **Update check**: once a day ValTrainer asks GitHub whether a newer version is out and shows it on the menu. You can skip a version or turn the check off in Settings. After an update, a "What's new" panel shows the changes.
- **Settings → About**: version, graphics card, renderer and data folders, "Copy system info" and "Report a bug" for bug reports, and the open-source licenses.
- **Windows compatibility**:
  - Windows 10 (1607 or newer) and Windows 11, 64-bit.
  - Starts on PCs without a working Vulkan driver: it falls back to Direct3D 12, then to a simpler OpenGL / Direct3D 11 mode for older GPUs, virtual machines and remote desktop. A "ValTrainer (safe graphics)" shortcut starts in that mode directly.
  - The first start picks a graphics quality for your PC (Low on integrated, virtual or software GPUs).
  - Numbers always use "." as the decimal point, whatever your Windows region settings, and user names with non-English characters work.
  - A damaged settings or stats file is kept as `*.corrupt` and ValTrainer starts fresh instead of crashing.

[Unreleased]: https://github.com/idoraz1/ValTrainer/compare/v1.16.0...HEAD
[1.16.0]: https://github.com/idoraz1/ValTrainer/compare/v1.15.0...v1.16.0
[1.15.0]: https://github.com/idoraz1/ValTrainer/compare/v1.14.1...v1.15.0
[1.14.1]: https://github.com/idoraz1/ValTrainer/compare/v1.14.0...v1.14.1
[1.14.0]: https://github.com/idoraz1/ValTrainer/compare/v1.13.0...v1.14.0
[1.13.0]: https://github.com/idoraz1/ValTrainer/compare/v1.12.0...v1.13.0
[1.12.0]: https://github.com/idoraz1/ValTrainer/compare/v1.11.0...v1.12.0
[1.11.0]: https://github.com/idoraz1/ValTrainer/compare/v1.10.0...v1.11.0
[1.10.0]: https://github.com/idoraz1/ValTrainer/compare/v1.9.0...v1.10.0
[1.9.0]: https://github.com/idoraz1/ValTrainer/compare/v1.8.0...v1.9.0
[1.8.0]: https://github.com/idoraz1/ValTrainer/compare/v1.7.1...v1.8.0
[1.7.1]: https://github.com/idoraz1/ValTrainer/compare/v1.7.0...v1.7.1
[1.7.0]: https://github.com/idoraz1/ValTrainer/compare/v1.6.0...v1.7.0
[1.6.0]: https://github.com/idoraz1/ValTrainer/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/idoraz1/ValTrainer/compare/v1.4.4...v1.5.0
[1.4.4]: https://github.com/idoraz1/ValTrainer/compare/v1.4.3...v1.4.4
[1.4.3]: https://github.com/idoraz1/ValTrainer/compare/v1.4.2...v1.4.3
[1.4.2]: https://github.com/idoraz1/ValTrainer/compare/v1.4.1...v1.4.2
[1.4.1]: https://github.com/idoraz1/ValTrainer/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/idoraz1/ValTrainer/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/idoraz1/ValTrainer/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/idoraz1/ValTrainer/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/idoraz1/ValTrainer/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/idoraz1/ValTrainer/releases/tag/v1.0.0
