# Minimap and jump peeks (1.21)

Request (Discord, Dixi): "Adding a minimap with the drills as Jiggle/Jump peeks to get used to it in game, as well as
the mechanics of the jump peeks aren't the best at the moment."

## 1. Minimap

### What VALORANT does (and what we copy)
- Top-left corner, square, drawn over the game. Settings → Map: **Rotate** (turns with your view; default) or **Fixed**,
  "Keep player centered" (off puts you lower so more of what is ahead shows), size, zoom, vision cones.
- You: an arrow with a white **vision cone**. Enemies appear as **red markers only while someone on your team sees
  them** (in view, line of sight) or when they make themselves known (firing an unsuppressed gun); when sight is lost the
  marker stays briefly at the last known spot.
- No Riot art is used. ValTrainer draws everything from its own level geometry.

### How ours works (`src/UI/Minimap.cs`)
- `TrainingMode.Minimap` (bool, default false) turns it on for a drill; `MinimapRange` is the rotating zoom. Jump Peek
  (38 m) and Jiggle Peek (21 m) turn it on. Any other drill can opt in with one override.
- Settings: `AppSettings.MinimapOffDrills` (drills where the player turned it off in the drill preview, key **M**, same
  pattern as `UnlimitedAmmoDrills`) and `AppSettings.MinimapFixed` (Settings → Gameplay → Minimap: ROTATING / FIXED).
- **Static layer** (`MinimapPlan`, built once per run on a worker thread from `GameSession.Solid`, so every `MapSpot`,
  the range and drill arenas work with no per-map data):
  1. Rasterize every box into a height grid (cell 0.2 m, at most 1100 cells a side). Boxes are stacked lowest first; a
     box starting more than 2 m above what is under it is overhead (lintels, roofs, a window's upper wall) and is skipped.
     Rasterization is conservative, so a 10 cm wall still blocks.
  2. Flood the playable floor from the player's feet over steps of at most 0.75 m (stairs yes, crates no, drops free).
     A respawn or teleport onto floor the flood didn't reach extends it (checked once a second).
  3. Floor = light grey (higher floor a little lighter), a white 0.55 m edge where the floor meets a wall, a darker line
     at height steps, dark walls / obstacles within 1 m of the floor (jumpable ≤ 1.3 m boxes a little lighter), the rest
     transparent. One RGBA texture with mipmaps.
- **Dynamic layer**, every frame: one textured quad for the whole square (each corner's UV is the world point under it, so
  rotation costs nothing), the vision cone (the current FOV, so it narrows when scoped), the enemies, your arrow.
  Rotating mode puts you 30% below the centre (VALORANT with "Keep player centered" off).
- **Enemies** (`GameSession.SpawnedBots`): a bot is spotted while it is inside your horizontal FOV with line of sight to
  its head or chest (smokes block it: `LineOfSight`), or the frame it fires (`BotCharacter.ShotsFired`) or you hit it
  (`HitsTaken`). The dot then holds 0.35 s at the last known spot and fades over 1.6 s. Dead bots vanish.
- HUD scale `k` like the rest of `Hud.cs`; the drill name / HUD lines move under the minimap when it shows.
- Dev: `--minimap` forces it in every 3D drill (check a new map: `--mode siteclear --map NAME --minimap`),
  `--minimap fixed` also forces the fixed orientation.

## 2. Jump peeks

### Public sources

| Item | VALORANT | Source |
|---|---|---|
| Landing inaccuracy | binary, +7° for 0.225 s after landing (was gradual 5° / 0.2 s); done because "players landing from a jump are able to shoot accurately sooner than is reasonable", especially Op jump peeks | Riot, [patch 1.09](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-1-09/) |
| Operator | deadzone 15% of speed, scoped move 72%, 0.6 shots/s (1.67 s bolt), equip 0.5 s; "dry peeking angles with an Operator is noticeably more effective than we'd like (especially when jump peeking)" | Riot, patch 1.09 (same page) |
| Stopping | counter-strafe: run → deadzone 0.104 s, full stop 0.160 s; releasing keys 0.110 / 0.170 s (≈6% slower). No instant stop. | Riot_Classick (Riot), [devtrackers mirror](https://devtrackers.gg/valorant/p/f29d0021-walking-running-accuracy-too-high) |
| Air speed | no gain above ground speed (Neon 12.09: "air speed while sprinting will match melee speed"); air strafing keeps the speed and turns it | Riot [12.09](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-12-09/); community, [Unreal forum thread](https://forums.unrealengine.com/t/how-does-valorant-handle-air-strafing/586555) |
| Jump apex | not published; Riot raised the molly "jump-over" height from 0.8 to 1.2 m (0.50), so the feet apex is between those; crouch-jumps get higher (6.05) | Riot 0.50 / 6.05 via the support folder's `research/movement.md` |
| Air time | 0.71 ± 0.01 s, frame-counted from our own VALORANT Range recording (2026-10-18), constant gravity, symmetric arc | `ValTrainer-support/research/jump-measure.md` |
| Airborne error | per gun, e.g. rifles +10°, Sheriff/Ghost/Classic +7°, shotguns +4° (12.09) | Wiki + patch notes, `weapons-1.18.md` |
| Landing slowdown | none documented by Riot or the wikis | `research/movement.md` §4 |
| How it is used | jump and strafe out past the corner, expose a small part of yourself, retreat; mainly to **gather info or bait an Operator shot**, then wide-swing during his bolt; not to take the fight in the air | [ProSettings, How to peek](https://prosettings.net/blog/how-to-peek-in-valorant/), [Dignitas, peeking and holding angles](https://dignitas.gg/articles/positioning-and-movement-101-the-dos-and-don-ts-of-peeking-and-holding-angles) ("not planning to shoot them, but just grab info"; gives away your location with noise) |

### Our values compared

| Value | ValTrainer (before = after) | Verdict |
|---|---|---|
| Gravity / jump speed | 14.3 m/s², 5.07 m/s → apex 0.90 m, 0.71 s in the air | matches our measurement and the 0.8–1.2 m bound |
| Standing / crouch-jump reach | 0.95 m / 1.25 m | consistent (map notes) |
| Landing penalty | 7° for 0.225 s, binary | = patch 1.09 |
| Braking | 38 / 24 m/s², counter ×1.06 → deadzone ≈0.10 s, stop ≈0.16 s | = Riot table |
| Ground acceleration | 18 m/s² (0 → full speed in 0.3 s) | = our Range measurement |
| Air control | 20 m/s², capped at take-off speed / stance max, no friction in the air | unmeasured; reverses a 5.4 m/s strafe in 0.27 s, so a full-speed jump peek reaches ~0.7 m past take-off and lands ~1.2 m back behind it, which is how the technique plays in VALORANT. Kept. |
| Airborne error | per gun (rifles 10°) | = sources |
| Op | 0.6 shots/s, 15% deadzone, 72% scoped speed | = patch 1.09 |

**No movement constant changed.** Every sourced number already matched. What was off was the drill:

1. **Nothing taught the technique.** A jump was only rewarded if it was the round's first spot. Shooting in the air,
   landing in the open and jumps that saw nothing went unnoticed.
2. **The preview demo (and dev autopilot) did it wrong**: it jumped from a near standstill and reversed after 0.12 s, so
   its eye never passed the corner. In a 90 s run, 13 of 22 jumps saw nothing (measured with
   `--movescript auto --simaim good --tier 2`). A drill preview that shows a bad jump peek teaches a bad jump peek.

### Changes
- **Every jump from the corner is graded when you land** (`JumpPeekMode.GradeJump`, event `jump_peek` A = outcome
  0 clean / 1 landed in the open / 2 fired in the air / 3 saw nothing, B = saw the Op during the jump):
  - **Clean** (+75): you saw the Op during the jump (or already knew where he was: a bait re-peek), landed where no part
    of you is in his line of sight, and didn't fire in the air. Banner `CLEAN JUMP PEEK · OP LONG · landed in cover`
    (or "he missed: swing now" during his bolt).
  - **Landed in the open** (−25): `hold A in the air to drift back behind the corner` (the player's own key).
  - **Fired in the air** (−50): `DON'T SHOOT IN THE AIR — +10° spread while airborne. Land, stop, then shoot` (the gun's
    own airborne error).
  - **Saw nothing** (0): `strafe out at full speed and jump right at the corner's edge`.
  - HUD: "Clean jump peeks X of Y". Results: clean jump peeks, landings in the open / shots fired in the air, jumps that
    saw nothing.
- The autopilot / preview demo now does a real jump peek: run-up from deep in the pocket, jump at the corner's edge at
  full speed (from x > -0.35 m), opposite key as soon as it leaves the ground. Same tier-2 run: 17 of 19 jumps clean,
  12 of 12 spots mid-jump, rounds with the Op spotted and you alive 8 of 12 (before: 3 of 9). Jumping from -0.42 m
  instead already left 18 of 28 jumps seeing nothing: the window is a few tenths of a metre, which is the skill.
- The badge (rounds with the Op spotted and you alive) is unchanged, so badges and coach calibration stay comparable.

### Coach consistency
`RunAnalysis` lists `jumppeek` in `SeenBotModes` (crosshair placement, with its own drill factor) and `MovementModes`
(moving shots, stops). Its RUN_GUN issue counts shots fired above 1.35 m/s horizontal speed, which covers almost every
airborne shot in a jump peek (you leave the ground at strafe speed), and it says the same thing as the new banner: stop,
then shoot. No coach change was needed. The new `jump_peek` event isn't read by the coach yet (a later "clean jump peek
%" metric could be).

### Open questions
- Air acceleration (20 m/s²) is still unmeasured. A Range clip of strafe → jump → opposite key would settle it.
- Bunny-hop speed gain (the wiki says "slightly faster than sprinting") is not modelled; irrelevant for peeking.
- VALORANT shows a "?" at an enemy's last known position and reveals gunfire by sound radius; ours reveals any firing bot
  wherever it is, and uses a fading dot instead of "?".
