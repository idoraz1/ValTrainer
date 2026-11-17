# Footsteps you can locate by ear (1.21)

Request (Discord, "Dixi"): *"The sound of footsteps sound nothing like the actual VALORANT, and it just throws you off, you
cant really tell where they are coming from or how far they are (especially in DMs)."*

## 1. What was wrong before

- **Two different systems.** Bots in Deathmatch, the peek drills and every other bot drill played their steps from
  `BotCharacter.Footsteps`: one `AudioStreamPlayer3D` per bot, a fixed 0.33 s cadence at any speed, always the same
  concrete sound, no wall check. Only the defense drills (Sound Lock, Site Anchor) used `FootstepFx` (surfaces, muffling).
- **The sound.** Both used the CC0 Kenney "Impact Sounds" footstep recordings: soft shoe scuffs with little transient and
  little low end, so they read as "someone walking in a hallway", not as a fast, hard VALORANT step.
- **Direction.** Godot's `AudioStreamPlayer3D` with stereo output only does amplitude panning from the camera (we set
  `PanningStrength` 0.85–0.9). No time difference between the ears, no head shadow, nothing that separates front from
  back: a step right behind you and right in front sound identical.
- **Distance.** Attenuation was a flat volume curve only (on purpose, like VALORANT), with no change in timbre, so a step at
  8 m and at 25 m sounded the same, just a little quieter.

## 2. How VALORANT does it (public sources)

| Topic | What is known | Source |
|---|---|---|
| Design goal | Riot's audio director Peter Zinda: "We optimize for making sure footsteps are heard, as opposed to optimizing for portraying distance", with "an attenuation curve that is somewhat flat, versus one that drops off a lot over distance", because "it is essential that you don't miss a footstep." | [GameRiv](https://gameriv.com/riot-games-explains-why-footsteps-in-valorant-sometimes-feel-closer-than-they-actually-are/), [Dot Esports](https://dotesports.com/valorant/news/riot-addresses-footstep-sound-complaints-in-valorant), [Dexerto](https://jamtest.dexerto.com/valorant/riot-respond-to-claims-that-valorant-footstep-audio-is-broken-1412600) |
| Audible range | No official number. Community testing: running roughly 40–50 m; shift-walking silent. (One guide also lists ~15 m "walking" and ~12 m crouch-walking, which contradicts the in-game rule that shift-walk and crouch-walk make no footstep sound; we keep them silent.) The minimap shows a ring around you while running: the radius at which others hear you. | [Switchblade Gaming](https://www.switchbladegaming.com/valorant/platinum-to-diamond-guide/), [ONE Esports minimap guide](https://www.oneesports.gg/valorant/valorant-minimap-guide-radar/), [win.gg](https://win.gg/news/the-mystery-behind-the-sound-circle-in-valorant-revealed/) |
| Complaints | Players report steps barely fade with distance or through walls ("an enemy five meters away in the same room" sounds like one behind a wall). | [Dexerto](https://dexerto.com/valorant/valorant-players-call-for-fix-to-footstep-audio-giving-unfair-advantage-1406614), [Gfinity](https://www.gfinityesports.com/article/valorant-developers-respond-to-broken-audio-claims-pc) |
| HRTF | Patch 2.06 (March 2021) added an HRTF toggle for headphones; only footsteps, reloads and Deathmatch respawns are rendered in 3D with it. Riot recommends turning off other "3D audio" processing with it. Patch 8.0 added third-party spatial audio support. | [Patch notes 2.06](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-2-06), [Patch notes 8.0](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-8-0/) |
| Occlusion | Closed beta patch 0.49: "Updated the audio occlusion system to help players better differentiate audio distancing cues" (e.g. Haven B to A: sounds should be heard "from their correct distance and not as if they were right next to you"). An undated note quoted on SteamDB (403 for us, unverified) describes 2 → 4 occlusion levels with exceptions for gameplay sounds like footsteps. | [Patch notes 0.49](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-0-49/) |
| Cadence / speed | Rifle run speed 5.4 m/s (Phantom/Vandal), knife ~6.75 m/s (community figure); heavies and snipers slower. Footstep rate follows the leg speed. | [Twinfinite weapon stats](https://twinfinite.net/features/complete-valorant-weapon-stats-list/), [vlr.gg](https://vlr.gg/post/4240306) |
| Reading distance | Guides agree that loudness alone is unreliable in VALORANT (flat curve); they recommend HRTF on headphones, sound effects at 100 %, a headset with clear mids/highs (steps live there), and the minimap ring. | [prosettings.net](https://prosettings.net/guides/best-valorant-audio-settings/), [setup.gg](https://www.setup.gg/game/valorant/best-audio-settings/), [Switchblade Gaming](https://www.switchbladegaming.com/valorant/platinum-to-diamond-guide/) |

Takeaways for ValTrainer: keep steps loud and audible to ~40 m (never miss one), only running makes noise, but add the
cues a listener actually uses for *where*: timbre (a near step is crisp, a far one dull and roomier), interaural level and
time differences, a duller sound from behind, and a clearly muffled step behind a wall.

## 3. What 1.21 does

All in `src/Audio/FootstepSynth.cs` (pure code, tested headless) and `src/Game/Fx/FootstepFx.cs` (playback).

- **One system for everything**: bots spawned through `GameSession.SpawnBot` (Deathmatch, Peek Mix, every bot drill) and
  the defense drills' `FootstepEmitter` both go through `FootstepFx.Step`; your own steps and landings go through
  `FootstepFx.Own`. Cadence `FootstepFx.StepInterval(speed)` (0.33 s at 5.4 m/s) for bots too now, ±12 ms jitter,
  alternating left / right foot. Muted in drill previews.
- **Sound** (synthesized, nothing recorded, nothing from Riot): a sharp sole transient (band-passed noise, 2–4 ms), the
  shoe slap (mid band), a low body thump (falling sine 120 → 55 Hz plus low noise), a second lighter contact 25–45 ms later
  (forefoot roll), a short scuff and a little gear rattle. Surfaces: concrete, tile (brighter, a short ring), metal
  (ringing partials), wood (hollow knock) and soft ground (snow / sand: crunch, little click). 8 takes per surface (4 per
  foot), random pick, ±5 % pitch, ±1 dB level per step. Measured balance (concrete, first 300 ms): ~20 % below 150 Hz,
  ~26 % 0.5–2 kHz, ~41 % 2–6 kHz.
- **Surfaces from the map** (`SurfaceAt`, a heuristic since boxes only know Floor / Elevated / Trim / Cover / Wall): Floor
  and Trim concrete; raised site floors tile; thin floating walkways metal; crates wood; on Icebox crates, containers and
  raised floors metal and the ground snow; Breeze ground sand.
- **Direction** (`CueFor`, per step, from the foot position and where you look at that moment, horizontal plane):
  level difference up to ~10 dB (far ear 0.25 + 0.75 · equal-power), interaural delay up to 0.63 ms on the far ear, a head
  shadow on the far ear's highs (up to −60 % above 2 kHz), and steps behind you 50 % duller above 2 kHz and ~1 dB quieter.
  Measured on the dump of an enemy circling at 6 m: 0 dB L/R in front and behind, +11 dB left at 90° left, −10 dB at 90°
  right. Directly below or above you the left/right cue fades out.
- **Distance**: −2.5 dB per doubling past 2 m (4 m −2.5, 8 m −5, 16 m −7.5, 32 m −10 dB), a fade to silence over the last
  4 m before 40 m. On top: a 12 dB/oct low-pass that closes from 14 kHz near to ~2.6 kHz at 40 m, and a faint low-passed
  reflection (6–26 ms) that grows from 5 % to 27 % with distance. Through a wall (no line from your head to the foot or the
  knee, map boxes only, smokes don't count): −3 dB, the low-pass closes to ~22 % (min 650 Hz) and more reflection.
- **Why our own stereo instead of `AudioStreamPlayer3D`**: Godot has no HRTF and its 3D panning is amplitude-only; a bus
  per voice with panner/delay effects would need runtime bus juggling. A step is short (0.2–0.35 s), so each one is
  rendered to a stereo buffer (~10k frames, reused scratch buffers) and played on a 16-voice pool of 2D players. Listener
  turns during one step are negligible.

## 4. Tests and dev tools

- `--culture-test` (in `tools\selftest.ps1`) has 7 footstep checks: every surface renders cleanly, gain falls with distance
  and is 0 past the range, cutoff falls with distance and through walls, a step on the left is >3× louder in the left ear
  (and on the right in the right), panning follows your yaw, a step behind is duller than in front, far and walled steps
  lose their highs.
- `--dev --dump-footsteps DIR` writes WAVs to listen to: one run per surface, an enemy circling you at 6 m, one running
  away 2 → 40 m, and the same through a wall.

## 5. Open questions

- Loudness was set by measurement (attack RMS −14 dBFS per take, enemy level 1.25 × the master volume), not by ear against
  the gunshots; it may need a trim after a listen.
- No elevation cue (steps above / below sound like level ones apart from the fading left/right), like VALORANT without HRTF.
- The Kenney footstep files in `assets/audio/footsteps/` are no longer played; they could be removed in a later cleanup.
