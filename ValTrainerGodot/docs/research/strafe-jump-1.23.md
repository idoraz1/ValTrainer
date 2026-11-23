# Strafe Jump drill (1.23)

Request (Discord, Slend): "add a training exercise to help players learn bunny hopping and strafe jumping, allowing
them to peek angles quickly without getting killed by enemies. The difficulty could scale according to the player's
rank. The higher the rank, the more likely bots should be to kill you if your strafe jump is too slow or poorly
executed."

## 1. What VALORANT actually does

| Item | VALORANT | Source |
|---|---|---|
| Air control | Air strafing changes the **direction** of your velocity while its size stays the same: no Source-style speed gain from strafing + turning in the air | Unreal forum, [How to recreate this type of movement from Valorant?](https://forums.unrealengine.com/t/how-to-recreate-this-type-of-movement-from-valorant/507483) ("the velocity vector retains its magnitude while changing direction"); [How does VALORANT handle air strafing?](https://forums.unrealengine.com/t/how-does-valorant-handle-air-strafing/586555) |
| Bunny hopping | Guides disagree on a small gain; the careful ones say the momentum gained is "barely worth mentioning" and a failed hop slows you down. Nobody measured a number, and Riot never documented one | [Sportskeeda, how to bhop](https://sportskeeda.com/esports/how-to-bhop-or-bunny-hop-in-valorant), [Mein-MMO](https://mein-mmo.de/en/valorant-bunny-hopping-made-easy-the-experts-tactic,492774); the optimistic view: [TheGamer](https://www.thegamer.com/valorant-bunny-hop-guide/) ("greater than base running speed", no figure) |
| Air speed cap | Neon 12.09: "air speed while sprinting will match melee speed", i.e. the air speed is set by the ground speed you left with | Riot, [patch 12.09](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-12-09/) |
| Landing | No landing slowdown documented; the landing penalty is for **guns** (+7° error for 0.225 s after landing, patch 1.09) | Riot, [patch 1.09](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-1-09/); `minimap-jumppeek-1.21.md` |
| Stopping | Releasing the keys or counter-strafing stops you in ~0.16 s; no key = ground friction as soon as you land | Riot_Classick via [devtrackers](https://devtrackers.gg/valorant/p/f29d0021-walking-running-accuracy-too-high) |
| Crossing / peeking | Don't drift or stop in the open; a jump peek needs cover to land behind ("you get killed on landing"); move to avoid damage, stop to deal it | [Boostroom movement guide](https://boostroom.com/blog/valorant-movement-guide-strafing-jump-peeks-and-staying-hard-to-hit), [ProSettings, How to peek](https://prosettings.net/blog/how-to-peek-in-valorant/), [Dignitas](https://dignitas.gg/articles/positioning-and-movement-101-the-dos-and-don-ts-of-peeking-and-holding-angles) |

**Honest summary.** A CS-style bunny hop (gaining speed by strafing and turning in the air) is not a real technique in
VALORANT. What *is* real, and what the drill teaches:

1. Reach full speed **in cover** (0.3 s of running) so you enter the open at run speed, never accelerating in his view.
2. Cross at full speed. Jumping is **not faster**: you keep exactly the speed you took off with. What a jump at the
   edge of cover does is move your head off the height he pre-aims, while your path in the air is fixed (no stopping,
   no turning back), so a good player leads it. It helps more against lower ranks than against Radiants.
3. **Keep holding the key through the landing.** In the air there is no friction, so releasing the key mid-jump costs
   nothing until you land; then the ground brakes you in ~0.16 s, in the middle of his sightline. Chained jumps keep
   your speed (no gain, no loss) as long as the key stays down.
4. Never walk, crouch or stop in the open, and once you've shown yourself, commit.

## 2. Our movement vs the sources

| Value | ValTrainer | Verdict |
|---|---|---|
| Air control | `MovementTuning.AirAccel` 20 m/s², speed capped at max(take-off speed, stance max), no friction in the air (`Mover.AirMove`) | matches "magnitude kept, direction changes"; no gain → no bhop. **Unchanged.** |
| Landing | lands with the speed you had; with a key held the ground keeps you at run speed, with none the braking (38 / 24 m/s²) starts | matches (no documented landing slowdown) |
| Re-jump | a fresh press of a jump bind (key, wheel notch) on the ground; holding the key does not auto-hop | matches VALORANT (wheel-jump binds work) |
| Air time | 0.71 s, 5.4 m/s × 0.71 s = 3.8 m carried by a full-speed jump | our Range measurement (`jump-measure.md`) |

**No Mover change.** Every behaviour the drill relies on was already faithful.

## 3. The drill (`src/Modes/StrafeJumpMode.cs`, key `strafejump`)

- **Arena:** a corridor (cover A, west; cover B, east) with a 3.4 m open gap in its north wall onto a yard. You wait
  2.8 m deep in cover on either side; each crossing goes to the other side and the next one comes back.
- **Defender** (`GapHolderBrain`): one rifler (Vandal) in the yard, 12–28 m away, up to 7 m off the gap's axis,
  placed where he can't see you in cover. He holds the edge of the gap you'll come out of at head height (crosshair
  error = 0.3 × the tier's placement error). He sees head / chest / shoulders inside ±40° with line of sight, reacts
  (tier reaction time, `Difficulty` Rookie 480 ms … Pro 180 ms), settles (Fitts' law, tier ms/bit) and taps at the
  position he perceived `Lag` ago plus a tier lead. Every bullet is a real ray against the player's hitbox at that
  instant (a bullet that arrives after you're behind the wall hits the wall). Vandal damage: head 160, body 40, legs 34.
- **Why speed matters, per tier** (`GapSkill`): aim error = `AimSd + TrackK × (deg/s you move across his view)`, so a
  full-speed crossing is hard to hit and a slow one easy; vertical error × `AirMul` on an airborne target; low tiers
  barely lead (Predict 0 → they shoot where you were 0.13 s ago, ≈0.7 m behind a runner), Radiant leads 90%.

  | Tier | Aim (still) | TrackK | AirMul | Predict | Lag | Tap every | Commit |
  |---|---|---|---|---|---|---|---|
  | Rookie (Iron–Bronze) | 0.90° | 0.060 | 1.50 | 0.00 | 0.13 s | 0.30 s | 0.25 s |
  | Regular (Silver–Gold) | 0.70° | 0.050 | 1.45 | 0.30 | 0.12 s | 0.24 s | 0.20 s |
  | Veteran (Plat–Diamond) | 0.50° | 0.042 | 1.40 | 0.55 | 0.10 s | 0.19 s | 0.15 s |
  | Elite (Asc–Immortal) | 0.36° | 0.036 | 1.30 | 0.75 | 0.08 s | 0.15 s | 0.12 s |
  | Pro (Radiant) | 0.26° | 0.032 | 1.20 | 0.90 | 0.06 s | 0.13 s | 0.10 s |

- **Grading** (on reaching the other cover, or on death): time seen, speed when you came out (entry), lowest speed in
  the open (the 0.2 s after a landing excluded), the landing (speed 0.15 s after touchdown vs at touchdown), walk /
  crouch. Main fault in order: walked, crouched, braked on landing, too slow out of cover (< 85% of run speed), slowed
  in the open (< 70%). No fault + alive = **clean** (+100, +25 if he couldn't fire); a fault = made it (+40); death
  −100; damage −1 per 4 HP. Backing out after showing yourself is counted, not graded.
- **Tips** (banners): the fault, with the player's own keys and numbers ("KEEP HOLDING D THROUGH THE LANDING — no key when
  you land = the ground brakes you", "TOO SLOW OUT OF COVER — 3.1 of 5.4 m/s …").
- **Results:** clean crossings, survived / deaths, avg time seen, avg speed in the open, jump vs running crossings
  survived (so you see what works against that tier), crossings he couldn't shoot, the fault counts, movement lines.
- **Badge:** `BadgeWinRate(tier, clean / crossings)`.
- **Telemetry:** `cross` (A = ms seen, B = grade 0 clean / 1 fault / 2 died), `cross_fault` (A = fault, B = jumped),
  `round_lost` on death, `died` from the session. The coach reads the run's movement (stops, moving shots) like the other
  movement drills (`RunAnalysis.MovementModes`, `RunSummary.CoachModes`); 3D Rewind records it (`ReplayFile` Full).
- **Preview / dev autopilot** (`--movescript auto`, `--sjstyle fast|run|jump|slow`, `--sjseed N` seeds the defender):
  `fast` = full speed from the rest spot, every other crossing a jump at the edge, key held through the landing.

## 4. Self-test

`tools/selftest.ps1` §3g plays the drill with the autopilot and a seeded defender: fast crossings at Rookie must
survive (≥ 85%), walked crossings at Pro must mostly die (≥ 60%). The smoke list also runs `strafejump`.

## 5. Open questions

- Whether VALORANT's air control has *any* small gain (some guides say "slightly faster") is unmeasured. A Range clip
  of chained jumps with the speed readout would settle it; until then we model none.
- The defender never moves, swings or uses utility, and he hears no footsteps (he always holds the edge you come from).
