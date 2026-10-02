# Changelog

All notable changes to ValTrainer are listed here, newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and ValTrainer uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). CONTRIBUTING.md explains what counts as a major, minor
or patch change. Add every change under **Unreleased**; `tools\version.ps1` turns that section into the next version.

## [Unreleased]

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

[Unreleased]: https://github.com/idoraz1/ValTrainer/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/idoraz1/ValTrainer/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/idoraz1/ValTrainer/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/idoraz1/ValTrainer/releases/tag/v1.0.0
