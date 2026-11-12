# Gun skins 2: skin lines, levels, sounds, kill effects, finishers (v1.19)

Status: research + design only. No game source was changed. This builds on `skins-design-1.19.md` (the procedural
skin shader, `GunSkins`, `SkinPicker`, `SkinPreview`, all present as uncommitted v1.19 code) and replaces its flat
"16 colour skins" catalogue with VALORANT-style **skin lines** that have **editions, upgrade levels and variants**.

The developer's brief: skins *like VALORANT's*. A Level 1 base look, then levels that add animated or glowing
materials, custom gun sounds, kill effects and a finisher. Some lines have no upgrades (Select-like), variants
unlock at higher levels, and the better lines are unlocked by training. The unlock/XP system belongs to another agent.
This doc defines what each line and level contains and what it costs in **unlock tier / skin point** terms.

Hard rules (unchanged, restated because every section below obeys them):
- **No Riot assets, names or look-alikes** of specific VALORANT skin lines. Everything is procedural (shaders,
  synthesized audio, code-built meshes). The only sound sources are our own synthesis (`tools/sfx`) and CC0 files
  already credited in `CREDITS.md`.
- **Cosmetic only.** Recoil, damage, fire rate, equip and reload times, hit registration, scores, telemetry (`.vtt`),
  replays (`.vtr`), stats and coach analysis must not change. Gameplay sound cues keep the factory onset, length and
  loudness. The Rewind may show the viewmodel skin (it already does), but kill effects are not replayed.
- **Modest PCs.** The target is a Quadro P2000 / GTX 1050 class GPU at the default **Medium** preset. Everything must
  also work on GL Compatibility.

---

## 0. Summary of decisions

- **Five editions, with our own names and icons:** Select, Deluxe, Premium, Ultra, Exclusive. Riot's edition gem icons
  are not copied: we use pips (◇ … ◇◇◇◇, ★ for Exclusive). The level structure follows VALORANT's public pattern (§1):
  - **Select:** 1 level.
  - **Deluxe:** 1 level plus 3 variants.
  - **Premium:** 4 levels (base → animated VFX → custom sounds + kill effect → finisher) plus 3 variants.
  - **Ultra:** 5 levels (… → kill counter + kill banner + escalating kill sounds) plus 3 variants.
  - **Exclusive:** 3 levels, unlocked by **performance challenges** rather than points.
- **14 catalogue entries.** Two Select collections reuse 9 of the 16 existing finishes. Six Deluxe lines reuse the
  other finishes as their Level 1. There are 4 Premium lines, 1 Ultra and 1 Exclusive (§3). The existing skin ids stay
  valid (`carbon`, `honeycomb`, …), so v1.19 settings files keep working.
- **Sound sets are layered on the factory sound, never swapped in.** A skin's fire sound is the **factory clip plus a
  quiet synthesized layer**. The layer starts after the factory transient, ends inside the factory length and keeps the
  first-100 ms loudness within +0.5 dB. Reload layers follow the factory reload's timing. Escalating kill sounds 1–5
  replace `kill1..kill5` at identical length and loudness. Everything is rendered offline by a new
  `tools/sfx/skin_sfx.py` (deterministic, with an automatic check table) into `assets/audio/skins/<line>/`.
- **Kill detection is explicit and player-only.** The shared `BotMode.ShootBots` (every bot drill's bullet → bot path)
  and Defense's direct kill call a new `IGame.OnPlayerKill(...)`. It only counts while `GameSession` is resolving one
  of the player's bullets with a skinnable gun in hand. Bot-on-bot kills, utility kills and Rewind puppets never
  trigger anything.
- **Finisher rule.** The finisher plays on the player kill that ends the round. The trigger is a `round_won`
  telemetry event, or `Mode.Done` becoming true (the Deathmatch win), within 0.3 s after the kill. Rep drills without
  rounds get a **lite finisher** on every 5th kill of one life, and Deathmatch also gets one on 5 kills in one life.
  Target-sphere drills and the measuring tools (Sens Finder, Crosshair Finder) get none.
- **Kill effects have two parts.**
  - A **gun-side** pulse on the viewmodel, which is VALORANT's own pattern (§1.5) and the most readable.
  - A small **world** effect at the death point: pooled, additive, ground-level, at most 0.9 s, and never in the
    enemy-highlight colour.
  - Full finishers (up to 2 s) only play when no enemy is left alive. Otherwise the lite version plays.
- **Collection UI.** The existing `SkinPicker` gains a skin-line list grouped by edition, a **level track** with live
  3D previews (a SHOWCASE view: a dummy bot at 7 m is shot and killed every 2.5 s, so kill effects and finishers can be
  seen and heard), variant swatches, lock states and "how to unlock" text from the progression system.
- **Settings:** skin sounds on/off, kill effects off / gun only / full, finishers on/off, kill counter on/off and a skin
  effects volume. All of them live in `settings.json`. Unlock state lives with the progression system.
- **Budgets at Medium on a P2000:**
  - Animated skin: at most +0.2 ms GPU.
  - Two concurrent kill effects: at most 0.25 ms.
  - Finisher: at most 0.5 ms.
  - No lights, decals or screen-space passes.
  - At most 2 extra audio voices per shot.
- **Build split: 4 agents by file ownership** (§6). Agent A ships the data and API stub on day 0, and the other three
  build against it in parallel.

---

## 1. Research: how VALORANT's skins work (public sources)

Sources:
- valorant.fandom.com wiki pages, read through the MediaWiki API because the HTML returns 402.
- Riot's support page, Riot's 2020 dev blog on skins and patch notes.
- Press coverage.
- An analysis of the community game-data dump at `valorant-api.com/v1/weapons/skins` and `/contenttiers`, downloaded
  2026-11-07. It is extracted from the game files but is not an official Riot source.

Full URLs are in §8.

### 1.1 Editions
| Edition | Price (VP, per gun) | What the data shows |
|---|---|---|
| Select | 875 [S1] | all 426 skins have **1 level**, no upgrades. Battle-pass and Agent Gear skins are mostly Select [W1] |
| Deluxe | 1,275 [S1] | 297 of 301 have 1 level, with **variants** (recolours) |
| Premium | 1,775 [S1] | mostly **4 levels** (84 skins); some lighter lines have only a Level 2 "Kill Effects" |
| Ultra | 2,475 / 2,975 [W1] | always **4–5 levels**, always an Animation level and a Finisher level; 5-level Ultras add Voiceover or Song Shuffle |
| Exclusive | 2,175–2,675 [W1] | 1–5 levels. Every melee skin is classed as Exclusive |

Custom kill banners and kill sounds come with several Premium skins and all Exclusive and Ultra skins [W2]. A kill
counter on the gun exists on exactly one line, at its Level 5 [W3].

### 1.2 Upgrade levels
- Each level costs **10 Radianite Points** and levels unlock **in order**. Variants cost 15 RP each, only after the
  skin is fully upgraded, in any order (pre-1.10 they were sequential levels 5–7) [W4][W5][P1].
- The typical 4-level layout in the data dump:
  - **L1:** the base model.
  - **L2:** VFX (147 skins) or sound effects (304).
  - **L3:** animation, meaning custom equip, reload and inspect (194). Kill-banner levels also sit here (144).
  - **L4:** the finisher (260 of 280 finisher levels are at L4).
  - **L5,** where it exists: a kill counter, voiceover or song shuffle.
- Generic examples from three published lines (summarised, not copied):
  - **A Premium line:** L2 muzzle-flash VFX and new fire sound; L3 model lighting and a new reload animation and
    sound; L4 kill banner, finisher and kill audio.
  - **An Ultra line:** L2 animated material and new fire sound; L3 a living model with equip and reload animations;
    L4 a finisher, kill banner and kill audio.
  - **An Exclusive line:** L2 lights and moving parts; L3 coloured tracers and equip, reload and inspect effects;
    L4 finisher and banner; L5 a **kill counter** on the gun that shows a short word after the last kill of a round.

### 1.3 Variants
- Usually **3 variants** plus the base. Some lines have 1–2.
- A variant can only be equipped on the maxed skin [W4]. A later toggle lets players equip a lower level [P2].
- Variants recolour the model. On some lines the **kill banner and finisher colours follow the variant**, and one line
  also changes its inspect light palette [W6].

### 1.4 Finishers
- **Trigger:** the holder kills the **last enemy alive in the round** with that gun ("last kill finisher") [P3].
- It plays **on the victim's body**.
- Since patch 10.11 a **Finisher Cam** shows it to the whole lobby, like the Spike Cam. Riot cited visual clutter,
  photosensitivity and performance as the reasons [P4].
- Melee skins never have finishers [W7].
- Not verified: how long a finisher lasts (they look about 2–4 s), and what happens when the round ends on the Spike.

### 1.5 Kill banners, kill sounds, kill effects
- Every kill shows a **banner** at the bottom of the screen and plays a **kill sound**. The default set escalates:
  separate sounds for kills 1, 2, 3, 4 and 5+ [W2].
- Upgraded skins replace both with their own escalating 1–5 set [P5].
- The "kill effects" of lighter Premium lines are **on the weapon** (the gun flares or gives off wisps), not on the
  victim [W8]. We copy this pattern: the **gun-side pulse** is our primary kill effect.

### 1.6 Equip, inspect and sound rules (Riot dev blog, 2020) [R1]
- "No pay-to-win. No pay-to-lose."
- A skin's sound is "**not any louder or quieter** than the base version". Also: "If you're relying on audio for your
  spray control, we don't want new firing sounds to mess with that."
- Cosmetic audio is "**ducked by more important gameplay cues**".
- Reload and fire timings **match the base weapon's animation**.
- A skin must "never feel slower", and "**being faster is unacceptable**".
- Effects are "fully immersive in **1st-person only**". Others see only the model, and tracers are visible only to the
  shooter.
- A collaboration skin's audio was kept clean with minimal reverb so it gives no edge [P6].

These are exactly our constraints. §4.3 and §4.4 turn them into measurable checks.

### 1.7 Collection UI and free unlock paths
- **Collection screen:** Collection → weapon → skin. An upgrades column lists each level, and the next one is bought
  with RP [P1].
- The data gives each level a streamed **preview video** and each variant a **swatch** [D1].
- A "Random Favorite" option picks a favourited skin each game [W9].
- **Free unlock paths:**
  - **Battle pass:** 55 tiers, 13 weapons' skins (Select/Deluxe), RP rewards [W10].
  - **Agent Gear:** a Select sidearm at tier 10 [W11].
  - **Events:** RP rewards.
  - Higher editions are never free. Our progression system inverts this: *everything* is earned by training.

---

## 2. Code map: where each feature hooks in

Line numbers are for the current working tree (uncommitted v1.19 on top of `bd24c8c`).

### 2.1 Gun sounds (fire, bolt, spin, reload, equip)
| Event | Where | Notes |
|---|---|---|
| Shot sound | `src/Game/GameSession.cs:584-588` | `Play2D(look.ShotSound, 0.55f * look.Volume, 1 ± 2.5 % pitch)` once per frame with shots; `look = WeaponLooks.For(weaponDef.Kind)` (l.583). **Skin hook:** play the fire layer here with the **same** pitch value |
| Bolt / pump after the shot | `GameSession.cs:587` | timer `WeaponLooks.BoltDelay` = 0.45 s (`WeaponLooks.cs:52`). **Skin hook:** optional bolt layer |
| Odin spin-up / spin-down | `GameSession.cs:589-597` | not skinned (it is a timing cue for the fire-rate ramp) |
| Reload start | `GameSession.cs:598` | `Play2D(look.ReloadSound, 0.6f, look.ReloadPitch)` on the reload's rising edge. **Skin hook:** reload layer, same pitch |
| Equip | **no sound today.** `AdsSession.cs:42-55` `Equip()` builds the viewmodel (l.51) and wires `Gun.Fired → viewmodel.OnFire` (l.53); `SwitchWeapon` l.57-70 sets `Gun.EquipLeft`; `AbilitySession.cs:25-32` redraws after an ability | **Skin hook:** equip flourish sound (skins with sounds only); factory stays silent |
| Every session sound | `GameSession.cs:954-971` `Sound`/`SoundAt`/`Play2D` | **`Play2D` is muted in drill previews (`Demo`)**: skin layers inherit that |
| Look table | `src/Game/Weapon/WeaponLooks.cs:22-41` (`ShotSound`, `ReloadSound`, `ReloadPitch`, `BoltSound`, `Volume`, `SpinUpSound`) | keys in `Sfx.GunKeys` (`Sfx.cs:88-92`) |
| Sfx engine | `src/Audio/Sfx.cs`: 24 2D + 24 3D players (l.24-36); recorded CC0/our WAVs (`LoadRecorded` l.39-84, private `Rec`); key → fallback chain (l.97-106, `Resolve`/`Pick` l.110-120); synthesized `kill1..kill5` rising C-major arpeggio at 660 Hz (l.168-186); `Play` l.278-286 | **Hooks:** a public `Register(key, streams)` for skin WAVs, a "Skin" bus, a small dedicated voice pool |
| Runtime bus precedent | `src/Game/Fx/FootstepFx.cs:80-84` (`AudioServer.AddBus`, `SetBusSend("Master")`) | same pattern for a "Skin" bus with its own volume and ducking |
| Offline pipelines | `tools/sfx/gun_sfx.py` (CC0 recordings → shots; **clip starts 4 ms before the onset**, fade over the last min(0.25 s, 30 %), loudness = RMS of the first 100 ms); `tools/sfx/flash_sfx.py` (pure numpy/scipy synthesis: `sweep`, `env`, `resonators`, `modal`, `bubbles`, `reverb`, `crush`, `impulses`; deterministic seeds) | the new `skin_sfx.py` imports the `flash_sfx.py` helpers and reads the factory WAVs for its timing and loudness checks |

### 2.2 Kills, rounds, matches
| What | Where | Notes |
|---|---|---|
| **The shared bot-kill path** | `src/Modes/BotModes.cs:17-43` `BotMode.ShootBots`: closest bot, damage, `OnHit`, `Effects.BodyHit`, hit sound; if dead → `best.Die(d)` (l.40) + `RegisterKill(...)` (l.41) | used by **every bot drill** (`MovementMode : BotMode`, so Deathmatch, Tactical, Site Clear, duels, agent drills, Peek, Sheriff, PeekMix, Long Taps, Target Switch …: 19 files call it). **Kill hook:** `G.OnPlayerKill(best, zone, dist)` right after l.40 |
| Defense direct kill | `src/Modes/DefenseModes.cs:544` | same hook |
| Kill bookkeeping | `src/Modes/TrainingMode.cs:213-222` `RegisterKill`: `Kills++`, `Event("kill")`, streak (5 s window, max 5), `G.Sound("kill" + streak)` (l.220), `KillBanner` (l.221) | **kill sound hook:** l.220 → the skin's escalating key |
| HUD multi-kill banner | `src/UI/Hud.cs:196` → `DrawKillBanner` l.504-540 (diamond, blades, chevrons per kill; gold at 5) | **banner hook:** a per-skin style |
| Bot death (visual) | `src/Game/Bots/BotCharacter.cs:332-345` `Die(fromDir)` raises `Fx(this, BotFx.Die, …)` (event l.71-72); `Step` auto-dies a `Dead` bot (l.377); `Feet` / `Head` give the anchor points | `BotFx` is also what the **Rewind records** (`Replay/ReplayRecorder.cs:69, 118-126`), so we **don't** hang skin logic on it |
| Bot spawn (all bots) | `GameSession.cs:907-915` `SpawnBot` (the only `BotCharacter.Create` caller) | **spawn-clear hook:** fade nearby kill effects when a new bot appears |
| Player bullet resolution | `GameSession.cs:682-692` `Volleys` → `Volley` (l.694-741, pellets) / `Bullet` (l.747-…); `Mode.OnBullet` at **l.757** | **player-bullet flag:** set around l.757 |
| Telemetry events | `GameSession.cs:655` `Event(kind, a, b)` → `Telemetry.Events` | **read-only watch** for `"round_won"` |
| Round-won emitters | `TacticalCore.cs:138`, `StrafeDuelMode.cs:209`, `RecoilModes.cs:344`, `MovementModes.cs:215`, `MovementPlusOp.cs:502`, `MapModes.cs:104, 593`, `DefenseModes.cs:426`, `Agents/DuelistDrills.cs:146`, `Agents/InitiatorDrill.cs:140`, `Agents/ChamberDrill.cs:257` | Peek, StrafeBots, Operator, Sheriff, PeekMix and Deathmatch emit none |
| Deathmatch | `DeathmatchMode.cs:62` `Done => winner != null`; player kills via `OnBullet` l.352-372 → `OnBotKilled` l.305-325; bot-on-bot kills `DeathmatchBots.cs:767` (not through `ShootBots`) | finisher on the match-winning kill and on 5 kills in one life |
| Run end | `GameSession.cs:820` `Finish()` | finishers never delay the results screen |

### 2.3 Viewmodel, VFX and existing skin plumbing
| What | Where |
|---|---|
| Viewmodel create / skin resolve | `src/Game/Weapon/Viewmodel.cs:56-80` (`Create`, `CreateWithSkin`, `ResolveSkin`) |
| `OnFire` (kick, flash, shell) | `Viewmodel.cs:85-95`. **Hook:** `fire_pulse` uniform for reactive skins |
| Build / re-material / skin space | `Viewmodel.cs:166-207`, `SetSkinSpace` l.276-282, `Reskin` + `GunSkins.Changed` l.298-321 |
| Per-frame | `Viewmodel.cs:334-345` (`mats.SetFov`, `mats.SetFlash`). **Hook:** `mats.SetSkinDynamics(...)` |
| Equip raise | `Viewmodel.cs:355-357` (`equipT`, `e`, `eo = (1-e)^3`), applied at l.413-415. **Hook:** equip flourish (same duration) |
| Inspect | `GameAction.Inspect` bound to **Y** (`src/Valorant/Keybinds.cs:24, 383`), unused in game |
| Materials | `VmMaterials.cs:48-56` (`Gold`, `Skin`, `Lite`, `lit`/`all` lists), `MakeSkin` l.79-104, `SetFlash` l.190-195 |
| Skin shader | `assets/shaders/weapon/viewmodel.gdshaderinc`: skin uniforms l.31-48; Hex glow with the `TIME` sweep and the sight-picture mask (top 15 %) at l.213-231; `EMISSION` l.278-284; Lite variant via `#define SKIN_LITE` |
| Skin data | `src/Game/Weapon/GunSkins.cs`: `GunSkin` record l.32-47 (becomes the per-level *look*), catalogue l.65-101, `DevSkin` (`--skin`) l.109, `IdFor` resolution l.126-138, `Set` l.149-168 |
| Pooled world FX | `src/Game/Fx/FxWorld.cs` (pools l.10-13, `GpuParticles3D` emitters l.86-103, `Burst` with instance uniforms l.106-116, invisible pre-warm burst below the world) behind `Effects` (`Effects.cs:11-44`); `Effects.Root = World; Effects.Prewarm()` at `GameSession.cs:305-306` |
| Rewind | `Replay/ReplayStage.cs:401` builds a real `Viewmodel`, so the skin shows; `ReplayFx` draws its own tracers and never touches `Effects.Root` |
| Picker UI | `src/UI/SkinPicker.cs:18-72` (overlay, `SkinSwatches.Warm()`), `SkinPreview.cs:14-96` (INSPECT / IN HAND SubViewport), `SkinSwatches` l.194-204 |
| Settings | `AppSettings.cs:24` (`Volume`), l.50-52 (`GunSkins`, `GunSkinAll`); `SettingsScreen.cs:518-525` (volume `Selector` pattern) |
| Benchmarks (Exclusive challenges) | `src/Analysis/Benchmarks.cs:148` `RankNames` Iron … Radiant |

---

## 3. The catalogue: 14 original skin lines

### 3.1 Clean-room rules and name check
- **Names.** All line names were checked on 2026-11-07 against the 3,193 skin, chroma and bundle names in the
  valorant-api dump: **no hits** for Field Issue, Camo Kit, Carbon Weave, Temper, Anodize, Candy Flake, Damascus,
  Signal, Lattice, Strata, Escapement, Rainfront, Phosphor Trace or Calibrated. Variant words that do appear in Riot
  chroma names (e.g. "Cobalt", "Emerald") are generic colour words. They are kept only as *our* variant labels and
  never with a Riot line name. Re-run the check before each release (script idea in §5.6).
- **Themes avoided.** These are crowded or too close to specific Riot lines:
  - dragons and fire-breath;
  - gold-and-white royalty;
  - purple void or cosmic;
  - neon glitch;
  - RGB colour cycling;
  - Japanese masks and water spirits;
  - ice crystals;
  - insect hives (one Riot line is hive-themed, so `honeycomb` is presented as abstract geometry, never bees);
  - lava cracks (a Riot line is named after rupture or fire);
  - audio-waveform knives (a Riot melee is named after waveforms, so Phosphor Trace is an *instrument graticule*,
    never a music visualiser).
- **Our themes come from:**
  - real finishes (paints, camo, carbon, anodising, heat tint, Damascus);
  - geology (strata, agate);
  - horology (guilloché, hairsprings);
  - weather (rain);
  - lab instruments (CRT oscilloscopes, calibration targets).

### 3.2 Unlock economy (for the progression agent)
Two numbers per unlockable item:
- **Gate tier `U0..U6`.** The minimum progression tier at which the item becomes available. The progression agent maps
  tiers to XP or hours. Suggested pacing: U1 about 1 h of training, U2 about 3 h, U3 about 6 h, U4 about 12 h, U5 about
  25 h, U6 about 50 h.
- **Cost in skin points (SP).** This is the VP/RP analog: the currency the progression system grants. Levels unlock
  in order. Variants unlock after the line's top level, in any order. **Exclusive** items cost nothing. Each is gated
  by a challenge id.

| Edition | Item | Gate | Cost |
|---|---|---|---|
| Select | each skin | U0 | 0 (free from first launch: today's v1.19 skins stay free) |
| Deluxe | Level 1 | U1–U2 | 2 SP |
| Deluxe | each variant (needs L1) | same as L1 | 1 SP |
| Premium | Level 1 | U2–U4 | 3 SP |
| Premium | Levels 2, 3, 4 (in order) | +0 / +1 / +1 tier after L1 | 2 SP each |
| Premium | each variant (needs L4) | L4's tier | 1 SP |
| Ultra | Level 1 | U5 | 4 SP |
| Ultra | Levels 2–5 (in order) | U5, U5, U6, U6 | 2 SP each |
| Ultra | each variant (needs L5) | U6 | 1 SP |
| Exclusive | each level / variant | challenge | 0 |

**Everything purchasable costs 93 SP:**

| Edition | Lines | SP per line | Total |
|---|---|---|---|
| Deluxe | 6 | 2 + 3 × 1 = 5 | 30 |
| Premium | 4 | 3 + 3 × 2 + 3 × 1 = 12 | 48 |
| Ultra | 1 | 4 + 4 × 2 + 3 × 1 = 15 | 15 |

The progression agent owns the SP income rate. Suggestion: about 1 SP per 30–40 min of active training, so a full
collection takes about 50–60 h, with first Deluxe items inside the first hour.

Unlock ids are stable strings: `<line>` = Level 1, `<line>@<n>` = level n, `<line>.<variant>` = variant
(e.g. `honeycomb@3`, `phosphor.amber`).

### 3.3 Overview
| # | Line (id) | Edition | Levels | Variants | Base pattern | Gate | VFX | Sounds | Kill effect | Finisher | Extra |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | **Field Issue** collection: `graphite`, `bone`, `olive`, `fde`, `hazard` | Select | 1 | – | Solid / Hazard (existing) | U0 | – | – | – | – | – |
| 2 | **Camo Kit** collection: `woodland`, `arctic`, `urban`, `tiger` | Select | 1 | – | Blot / Pixel / Stripe (existing) | U0 | – | – | – | – | – |
| 3 | **Carbon Weave** `carbon` | Deluxe | 1 | 3 | Carbon (+ Blot for Forged) | U1 | – | – | – | – | – |
| 4 | **Signal** `signal` | Deluxe | 1 | 3 | Split | U1 | – | – | – | – | – |
| 5 | **Anodize** `cobalt` | Deluxe | 1 | 3 | Brushed | U1 | – | – | – | – | – |
| 6 | **Temper** `heat` | Deluxe | 1 | 3 | Heat (+ Blot for case-hardened) | U2 | – | – | – | – | – |
| 7 | **Candy Flake** `flake` | Deluxe | 1 | 3 | Flake | U2 | – | – | – | – | – |
| 8 | **Damascus** `damascus` | Deluxe | 1 | 3 | Damascus (+ 2 sub-modes) | U2 | – | – | – | – | – |
| 9 | **Lattice** `honeycomb` | Premium | 4 | 3 | Hex (existing) | U2 | L2 | L3 | L3 | L4 | – |
| 10 | **Rainfront** `rainfront` | Premium | 4 | 3 | **Rain** (new 14) | U3 | L2 | L3 | L3 | L4 | – |
| 11 | **Strata** `strata` | Premium | 4 | 3 | **Strata** (new 12) | U3 | L2 | L3 | L3 | L4 | – |
| 12 | **Escapement** `escapement` | Premium | 4 | 3 | **Guilloché** (new 13) | U4 | L2 | L3 | L3 | L4 | equip flourish |
| 13 | **Phosphor Trace** `phosphor` | Ultra | 5 | 3 | **Graticule** (new 15) | U5 | L2 | L3 | L3 | L4 | L5 counter + banner + kill sounds, tinted tracers |
| 14 | **Calibrated** `calibrated` | Exclusive | 3 | 2 | **Calibration** (new 16) | challenges | – | L2 (kill) | L2 | L3 | kill-distance callout |

The Select collections are groups of single-level lines (`Collection = "field"` / `"camo"`). Each skin is still its own
line, so per-gun equipping keeps working exactly as in v1.19.

### 3.4 Select
**1 · Field Issue** (free). Graphite, Bone, Olive Drab, Desert Two-Tone and Hazard, exactly as in `GunSkins.cs:67-95`.

**2 · Camo Kit** (free). Woodland Blot, Arctic Blot, Urban Pixel and Tiger Stripe, unchanged.

No levels, no sounds, no effects: the Select "no upgrades" edition.

### 3.5 Deluxe (Level 1 + 3 variants, look only)
Variants are palette and parameter changes of the same `GunSkin` look. Two new shader *sub-modes* reuse existing
branches: a `skin_sub` int selects a variation inside a pattern.

| Line | L1 (existing look) | Variant 1 | Variant 2 | Variant 3 |
|---|---|---|---|---|
| **3 Carbon Weave** | Carbon: black twill, clear coat | **Aramid**: yellow #C9A227 / black weave | **Blue Dye**: #1D3B6E / #0C0F14 | **Forged**: Blot at 8 mm, #0B0C0E / #1E2023 / #33363A, roughness 0.2, spec 0.65 (chopped-flake look) |
| **4 Signal** | Signal Split: slate / grey / orange seam | **Range Day**: white #E6E6E2 rear, orange #E8742A front, black seam | **Night Signal**: #121417 / #2B3036, teal #3FB8AF seam | **Sand Signal**: #8C7553 / #C9B79A, olive seam |
| **5 Anodize** | Cobalt Brushed | **Bronze** #7A5A2E | **Teal** #1F6B6B | **Violet** #4E3D7A (deep, desaturated: no red-purple energy look) |
| **6 Temper** | Heat Tint gradient | **Blued**: near-uniform deep blue #1E2E57 → #2F4C7A, metallic 0.9 | **Straw**: pale straw → bronze only | **Case-Hardened**: Blot at 25 mm with heat colours (straw / blue / violet), metallic 0.85 |
| **7 Candy Flake** | Emerald Flake | **Midnight** #0E1C3F, flake #9FB8FF | **Copper** #5A2A14, flake #FFB27A | **Plum** #3A1730, flake #E7A8D8 |
| **8 Damascus** | Damascus (wavy bands) | **Ladder** (`skin_sub = 1`: bands crossed by periodic ridges along z) | **Raindrop** (`skin_sub = 2`: concentric rings around hashed 3D cell centres) | **Bronze Etch**: warm #7B6A55 / #3A3027 palette |

### 3.6 Premium lines (4 levels + 3 variants)
Every Premium line follows the same level template, which is VALORANT's dominant 4-level structure (§1.2):
- **L1:** a static base look.
- **L2:** animated or reactive material (fire-reactive glow, slow motion in the pattern).
- **L3:** custom sound set (fire layer, reload layer, equip flourish, escalating kill sounds 1–5) plus a **kill
  effect** (gun-side pulse plus world effect).
- **L4:** **finisher**, plus the restyled HUD kill banner.

Variants recolour the gun *and* the kill effect, finisher and banner (§1.3).

#### 9 · Lattice (`honeycomb`): abstract hex geometry with warm light in the walls
- **L1 (U2, 3 SP).** Today's Honeycomb Pulse *without* the sweep: dark #1E2226 cells, #2A2219 walls, faint amber
  glow (energy 0.22).
- **L2 (2 SP).**
  - The existing slow sweep (`anim 0.25`).
  - **Fire-reactive:** each shot lights the walls near the muzzle for 0.12 s (`fire_pulse`, decays exp(-t/0.06)),
    masked off the top 15 % and faded at ADS.
- **L3 (2 SP), sounds + kill effect.**
  - *Fire layer:* a glassy tick. Two modal partials at 3.1 kHz and 4.7 kHz (decay 25 ms), start +6 ms, −16 dB
    relative to the factory peak.
  - *Reload layer:* panel clacks at the factory's mag-out and mag-in times (filtered noise click + resonator
    1.2 kHz) over a soft hum (saw 110 → 220 Hz, LP 800 Hz, −20 dB).
  - *Equip:* three rising ticks (E6–G#6–B6 region), 0.35 s.
  - *Kill 1–5:* glass-bell pentatonic (same 660 Hz root and envelope lengths as factory `kill1..5`).
  - *Kill effect:*
    - **Gun-side:** every hex wall flashes along the whole gun, 0.35 s.
    - **World:** 12 small flat hexagon shards burst outward from the chest (radius 0.6 m, gravity 4 m/s²), fading by
      0.7 s.
    - **Sound:** crystalline tinkle (5 random modal partials 3–7 kHz, 0.4 s).
- **L4 (2 SP), finisher "Hex Dome".**
  - A low-poly **geodesic hex shell** (r = 0.9 m, 42 cells) assembles around the body from the ground up (0–0.4 s),
    glows (0.4–1.1 s), then breaks into shards that drop and fade (1.1–1.8 s).
  - Sound: a rising open-fifth chord swell (FM bells) into a soft shatter, 1.6 s.
  - *Lite:* only the ground ring of cells rises to 0.4 m, 1.0 s.
- **Variants (1 SP each):** **Amber** (base); **Teal** #3FC1B5 glow on #161C20; **Frost** #DDE6EA glow on #2A3036;
  **Lime** #A6D84A on #1A1F18.
- **Enemy-colour rule:** Amber swaps to its Frost palette for the world effect if the enemy highlight is yellow
  (§4.6).

#### 10 · Rainfront (`rainfront`): rain beading on a matte gun
- **L1 (U3, 3 SP).**
  - Slate-blue matte polymer #33404D.
  - **Pattern Rain (new 14):** per-cell (6 mm, 3D-hashed) droplet discs on upward-facing and side faces. Each drop
    is a darker wet ring plus a specular highlight dot. Droplet density is lower on downward faces. Hardware stays
    factory.
- **L2 (2 SP).**
  - Droplets **run**: a subset slides down gun-space −y, leaving a fading wet streak (`TIME`, at most 0.3 cells/s).
  - Firing shakes loose a short "spray" of sparkle near the muzzle (`fire_pulse`).
  - Lite: static drops only.
- **L3 (2 SP), sounds + kill effect.**
  - *Fire layer:* short wet spritz (HP-noise 4–8 kHz, 35 ms, start +10 ms, −18 dB).
  - *Reload layer:* two droplet "bloops" (`bubbles()` from flash_sfx) at mag-out and mag-in.
  - *Equip:* rain swell (pink noise LP 3 kHz, 0.5 s, −18 dB).
  - *Kill 1–5:* water-drop plinks rising (bubble pitch 660 → 1320 Hz).
  - *Kill effect:*
    - **Gun-side:** all droplets brighten and run at once, 0.4 s.
    - **World:** a **splash crown**: a ground ring ripple (2 expanding rings, 0.8 m) plus 24 droplet motes thrown
      up 0.6 m.
    - **Sound:** splash (bubble burst + noise splash, 0.35 s).
- **L4 (2 SP), finisher "Cloudburst".**
  - A small dark cloud (flattened ellipsoid, noise-alpha shader, 1.2 m wide, 2.6 m up) forms over the body (0.3 s).
  - **Rain streaks** fall from it (GPU particles, 120 streaks) for 1.2 s, with a puddle ripple field on the ground.
    The cloud then thins out (to 2.0 s).
  - Sound: a rain hiss swell plus a distant, low, soft roll (LP brown noise, kept under −14 dB), 1.8 s.
  - *Lite:* the puddle ripple field plus 40 drops from 1.5 m, 1.0 s, no cloud.
- **Variants:** **Slate** (base); **Sunshower** (warm #8A7A5A body, gold-lit drops); **Night** (#121B2B, cyan-white
  drops); **Fog** (#C9CDD1 body, grey drops).

#### 11 · Strata (`strata`): sedimentary rock and agate banding
- **L1 (U3, 3 SP).**
  - **Pattern Strata (new 12):** layered bands along gun-space y, warped by fbm (sandstone #B08A5E / #8A5E3C /
    #D2B48C / #5E4632 bands). Matte roughness 0.8, a thin darker "bedding line" between layers.
  - Trim gets the pattern, hardware stays factory.
- **L2 (2 SP).**
  - Mica glints in the light bands (sparse specular cells like Flake, fade by pixel size).
  - On each kill, **the bands slide one layer** (0.4 s ease): a satisfying, non-glowing animation.
  - Lite: glints off.
- **L3 (2 SP), sounds + kill effect.**
  - *Fire layer:* stone thock (resonator 180 Hz Q 4, 40 ms, +8 ms, −14 dB) plus grit (granular BP noise 2.5 kHz,
    30 ms).
  - *Reload:* stone slide grind (brown noise through a BP sweep 400 → 900 Hz, 0.3 s) at mag-out, a clack at mag-in.
  - *Equip:* pebble rattle (granular impulses through 1–3 kHz resonators).
  - *Kill 1–5:* stacked stone clacks (1 → 5 clacks, pitch rising).
  - *Kill effect:*
    - **Gun-side:** band slide plus a dust shimmer.
    - **World:** 10 flat rock shards (3–5-gon) split off the chest and fall with gravity 9.8, plus a low dust puff
      (existing dust particle look, sand tint).
    - **Sound:** rock crack (impulse into modal 400/950/1700 Hz plus falling pebble grains).
- **L4 (2 SP), finisher "Upthrust".**
  - A **stacked strata pillar** (5 bevel-less boxes in band colours, 0.7 m wide) pushes up from the ground under the
    body to 1.6 m (0–0.5 s, with a small screen-independent jolt), holds, then crumbles top-down into shards and dust
    (0.9–2.0 s).
  - Sound: deep rumble (LP noise 40–60 Hz, HP 35 Hz, capped −12 dB) into a crumble.
  - *Lite:* a ground crack ring plus dust, 1.0 s.
- **Variants:** **Sandstone** (base); **Agate** (`skin_sub = 1`: concentric warped banding in blue-grey and white);
  **Slate** (cool grey layers); **Ochre** (red-earth layers #8E4A2B / #C27C45 / #5A3A28).

#### 12 · Escapement (`escapement`): watchmaker's steel
- **L1 (U4, 3 SP).**
  - **Pattern Guilloché (new 13):** box-projected engine-turned engraving on body and trim. A sunburst (sin of angle
    around the panel centre) on side faces, basket-weave on top faces. Brushed steel #9AA0A6, metallic 0.9,
    roughness 0.3.
  - **Hardware = heat-blued** #1F3A6B, so screws, pins and bolt look like blued watch parts. Small **jewel dots**
    (ruby #8C1C2C, specular) on the receiver.
- **L2 (2 SP).**
  - A slow anisotropic light sweep travels across the guilloché (period 5 s).
  - A **balance-wheel** ring motif on the receiver side (an annulus with 8 spokes) oscillates ±40° at 2.5 Hz, masked
    off the sight picture.
  - Lite: static.
- **L3 (2 SP), sounds + kill effect + equip flourish.**
  - *Fire layer:* tiny steel tink (inharmonic modal 5.2 / 7.9 kHz, 15 ms, −18 dB).
  - *Reload:* crown-winding ratchet (25 Hz click train, BP 3 kHz, 0.4 s) at mag-in, plus a mainspring "zing".
  - *Equip:* tick-tock pair.
  - *Kill 1–5:* n ticks then a small bell whose pitch rises with the streak.
  - *Kill effect:*
    - **Gun-side:** the balance wheel spins one full turn.
    - **World:** a flat **hairspring spiral** (an Archimedean spiral line on a ground quad, r = 0.7 m) draws itself
      outward around the feet in 0.5 s, then fades.
    - **Sound:** two ticks plus a soft FM bell (1:3.5 ratio, 1.8 kHz, 0.5 s).
  - *Equip flourish (same duration as factory):* a quarter-turn roll added to the raise curve (`eo` envelope), ending
    exactly at `pose.Equip`.
- **L4 (2 SP), finisher "Mainspring".**
  - A large hairspring spiral (r = 1.4 m) coils *inward* to the body (0–0.6 s).
  - Then a vertical **escapement wheel** silhouette (a 15-tooth gear outline, 1.6 m, steel line art) rises behind the
    body, ticks 5 steps (0.6–1.6 s) and dissolves into steel motes.
  - Sound: accelerating ticks into one low bell strike (FM 220 Hz, 1.2 s tail, −10 dB).
  - *Lite:* the inward spiral only, 1.0 s.
- **Variants:** **Steel & Blue** (base); **Rose Copper** (#B07A62 body, dark steel hardware: copper, not gold);
  **Black & Lume** (#1A1C1F DLC body, pale-green lume dots and wheel); **Silver & Salmon** (#C9CCD0, salmon #E39A7E
  jewels).
- **Clean-room note:** no clock faces, no Roman numerals, no purple, no time-stop effects.

### 3.7 Ultra line (5 levels + 3 variants)
#### 13 · Phosphor Trace (`phosphor`): lab oscilloscope / CRT phosphor
- **L1 (U5, 4 SP).**
  - **Pattern Graticule (new 15):** instrument-black #0D0F10 body with a phosphor-green #41FF8A graticule: 10 mm
    major lines, 2 mm minor ticks on the centre lines, box-projected on side faces.
  - Static glow 0.25. Masked off the sight picture (top 15 %) and faded at ADS like Hex.
- **L2 (U5, 2 SP).**
  - A live **trace** scrolls along each side panel: a sine plus a slow Lissajous wobble, with a phosphor afterglow
    tail (exponential in phase distance).
  - Firing spikes the trace (`fire_pulse` adds a sharp pulse that runs muzzle → stock in 0.25 s).
  - A faint scanline flicker at 0.5 Hz, low contrast.
  - Lite: static trace, no flicker.
- **L3 (U5, 2 SP), sounds + kill effect + tinted tracers.**
  - *Fire layer:* CRT blip (band-limited square 2.4 kHz, 8 ms, start +5 ms, −16 dB).
  - *Reload:* relay clicks at mag-out and mag-in, plus a quiet 3-beep "seek" rising in fifths.
  - *Equip:* power-on thunk (60 Hz sine burst with a 120 Hz buzz, 0.25 s) plus a short rising whine (1 → 6 kHz,
    −22 dB).
  - *Kill effect:*
    - **Gun-side:** the whole graticule flares and the trace locks into a square wave for 0.4 s.
    - **World:** a vertical **beam trace** at the body: a camera-facing 2 m line that draws a spike, then flatlines
      and fades, 0.7 s.
    - **Sound:** a soft-attack 1 kHz beep, 0.25 s.
  - *Tracers* (the player's own, `Effects.Tracer` colour argument): phosphor green at the factory brightness. Only
    the shooter sees them anyway.
- **L4 (U6, 2 SP), finisher "Power Off".**
  - Over the body, a camera-facing 3 m-wide bright horizontal line collapses vertically then horizontally into a
    point, like a CRT switching off (0–0.5 s).
  - The **afterglow dot** fades with a slight bloom (to 1.8 s).
  - Sound: whine-down sweep 8 kHz → 200 Hz (0.6 s) plus a 50 Hz hum fade.
  - *Lite:* a 1 m line collapse, 0.8 s.
- **L5 (U6, 2 SP), kill counter + kill banner + escalating kill sounds.**
  - **Kill counter:** two 7-segment phosphor digits drawn by the shader on the camera-facing side of the receiver.
    The value is the run's kills (`Mode.Kills`, `99+` caps). The digits flicker on increment and show **"GG"** for
    2 s after a finisher.
  - **Kill banner:** the HUD multi-kill banner is restyled as a small scope screen. Its trace gains one harmonic per
    streak kill, and at 5 it shows a full Lissajous figure.
  - **Kill 1–5:** square-wave beeps rising on the factory notes, with a tiny data chirp, matched in length and RMS.
  - A counter relay click on each increment, −24 dB.
- **Variants (1 SP each, after L5):** **P31 Green** (base); **Amber** #FFB000; **Blue-White** #CFE8FF; **Paper White**
  #F2F0E6. The colours are named after phosphor types.

### 3.8 Exclusive (challenge-unlocked)
#### 14 · Calibrated (`calibrated`): the trainer's own measuring instrument
- **L1.** *Challenge `bench.gold5`: reach **Gold** benchmark rank (`Benchmarks.RankNames`) in 5 different drills.*
  - **Pattern Calibration (new 16):** off-white #E4E2DC polymer, **mm graduation ticks** along gun-space z on the side
    faces (1 mm / 5 mm / 10 mm tick heights, black ink), quarter-circle photogrammetry targets on the stock and a tiny
    inspection stamp.
  - Trim #2A2C30.
- **L2.** *Challenge `bench.diamond3`: **Diamond** in 3 drills.* Kill effect + kill sounds.
  - **Gun-side:** the graduation ticks run like a scanning ruler, 0.4 s.
  - **World:** a thin bracket marker frames the body, with the **kill distance** ("23.4 m") as a `Label3D` billboard
    for 0.8 s. This shows only the distance already computed in `ShootBots`, which the player could read from the
    stats anyway. It is cosmetic and is never recorded.
  - **Sound:** two short measurement beeps (2 kHz, 60 ms, 80 ms apart).
- **L3.** *Challenge `bench.immortal1`: **Immortal** in any benchmark drill.* Finisher **"Pass"**.
  - A horizontal laser-like calibration plane (thin emissive grid quad) sweeps up through the body (0–0.8 s).
  - A "PASS" stamp decal-quad appears on the ground (0.8–1.8 s).
  - Sound: a soft FM shimmer sweep plus a stamp thud.
  - *Lite:* the plane sweep only.
- **Variants:** **Bench Black** (*`lockin.10`: finish 10 Lock-In routines*); **Lab Blue** (*`sens.done`: complete the
  Sens Finder*).

The progression agent owns the challenge evaluation. This doc only fixes the ids and descriptions.

---

## 4. Technical design

### 4.1 Data model (`src/Game/Skins/SkinCatalog.cs`, new; `GunSkin` stays as the *look*)
```csharp
namespace ValTrainer.Game.Skins;

public enum SkinEdition { Select, Deluxe, Premium, Ultra, Exclusive }

[Flags] public enum SkinFeature
{
    None = 0, Animated = 1, Reactive = 2, Sounds = 4, KillFx = 8, Finisher = 16,
    KillBanner = 32, KillSounds = 64, KillCounter = 128, Tracers = 256, EquipFlourish = 512,
}

/// <summary>What a level adds. Features accumulate: level n has every feature of levels 1..n.</summary>
public sealed record SkinLevel(int N, SkinFeature Adds, string Blurb, int Gate, int Cost, string? Challenge = null)
{ public string UnlockId(string line) => N == 1 ? line : $"{line}@{N}"; }

/// <summary>A recolour: replaces the look's finishes and the effect palette.</summary>
public sealed record SkinVariant(string Id, string Name, Func<GunSkin, GunSkin> Recolour, FxPalette Palette,
                                 int Gate, int Cost, string? Challenge = null);

public sealed record FxPalette(Color Main, Color Accent, Color Alt); // Alt = the enemy-colour fallback (§4.6)

public sealed record SkinLine(
    string Id, string Name, string Blurb, SkinEdition Edition, string? Collection,
    GunSkin Look,                         // Level-1 look (the existing record; GunSkins.cs:32)
    IReadOnlyList<SkinLevel> Levels,      // 1 for Select/Deluxe
    IReadOnlyList<SkinVariant> Variants,  // base palette is implicit
    FxPalette Palette,
    SkinSounds? Sounds = null,            // §4.3: keys under assets/audio/skins/<id>/
    KillFxRecipe? KillFx = null,          // §4.4
    FinisherRecipe? Finisher = null,      // §4.5
    BannerStyle? Banner = null)
{
    public int MaxLevel => Levels.Count;
    public SkinFeature FeaturesAt(int level) => Levels.Take(level).Aggregate(SkinFeature.None, (a, l) => a | l.Adds);
    /// <summary>The look at a level: L1 = Look; L2+ turns on Anim / Reactive / counter uniforms.</summary>
    public GunSkin LookAt(int level, SkinVariant? v);
}

/// <summary>What one gun wears right now (resolved and clamped to what is unlocked).</summary>
public readonly record struct SkinState(SkinLine Line, int Level, SkinVariant? Variant)
{
    public SkinFeature Features => Line.FeaturesAt(Level);
    public bool Has(SkinFeature f) => (Features & f) != 0;
    public FxPalette Palette => Variant?.Palette ?? Line.Palette;
    public GunSkin Look => Line.LookAt(Level, Variant);
}
```
- `GunSkin` (the look record) gains a few init-only fields: `Sub` (`skin_sub`), `React` (fire-pulse gain),
  `KillGlow`, `Counter` (draw digits) and `CounterSide`. Existing skins leave them at 0.
- **`GunSkins` stays as the facade** for the UI and `Viewmodel`. `For(gun)` returns the resolved look. A new
  `StateFor(gun)` returns the `SkinState?`, which the runtime systems use.
- **Ids and stored values:**

  | Stored value | Example | Meaning |
  |---|---|---|
  | line id | `honeycomb` | the base palette |
  | `line.variant` | `honeycomb.teal` | a variant |

  The equipped **level** is per line: `SkinLevels[line]`. When it is missing, the highest unlocked level is used.
  This matches VALORANT's "equip a lower level" toggle [P2].
- **Resolution** (extends `GunSkins.IdFor`, `GunSkins.cs:126-138`):
  1. Dev `--skin line[.variant][@level]`.
  2. The gun's own entry.
  3. `GunSkinAll`.
  4. Factory.

  The result is then **clamped to unlocks**:
  - A locked variant falls back to the base palette.
  - A locked level falls back to the highest unlocked level.
  - A locked line falls back to factory.

  So a copied settings file can never show locked content, and nothing ever errors.

### 4.2 Unlock interface (owned by the progression agent; stubbed by Agent A)
```csharp
public interface ISkinUnlocks
{
    bool Has(string unlockId);                 // "carbon", "honeycomb@3", "phosphor.amber"
    string HowTo(string unlockId);             // UI text: "2 SP · you have 1 · reach Tier U2" / challenge text
    bool CanUnlock(string unlockId, out string why);
    bool TryUnlock(string unlockId);           // spends SP; levels in order; variants need the top level
    int Points { get; }
    event Action? Changed;                     // GunSkins.NotifyChanged() → viewmodels re-skin, pickers redraw
}
public static class SkinUnlocks { public static ISkinUnlocks Current = new DefaultSkinUnlocks(); }
```
- **`DefaultSkinUnlocks`** (until the progression system lands): every gate-U0 item is unlocked, everything else is
  locked, with `HowTo = "Unlocked by training (coming soon)"`.
- **Dev:** `--skins-unlock-all` makes `Has` always true. It is never saved.
- **Persistence:** unlock state lives in the progression system's own file, not in `settings.json`. A downgrade to
  v1.19.0 ignores it.

### 4.3 Sound sets (layered on factory timing)
**Rules** (from Riot's public rules, §1.6, made measurable):
1. **Fire.** The factory clip always plays unchanged. The skin adds one **layer** clip on the same frame, at the same
   pitch, routed to the Skin bus. The layer:
   - is silent for the first **9 ms** of the clip (the factory clip's onset sits at 4 ms, so the transient stays
     clean);
   - has a peak at least **12 dB** below the factory clip's peak;
   - keeps factory + layer RMS over the first 100 ms within **+0.5 dB** of the factory alone;
   - is at most the factory length and is −40 dB by 60 % of it;
   - is high-passed at 60 Hz;
   - for automatic guns (`Def.Auto` with Rps ≥ 8), lasts at most **60 ms**, so sprays don't smear;
   - for suppressed guns (Ghost, Spectre), sits 6 dB lower still.
2. **Reload.** The factory reload clip plays unchanged. The layer is rendered to the factory reload clip's exact length
   and its events sit on the factory clip's mag-out and mag-in transients (found by onset detection in `skin_sfx.py`).
   It plays at the factory `ReloadPitch`. It never lasts longer than the reload.
3. **Equip.** At most 0.6 s and −12 dB below a factory shot. Only lines with Sounds. Factory equip stays silent.
4. **Kill 1–5** (Ultra L5, Premium L3). These **replace** `kill1..kill5`. Onset at most 2 ms, length within ±10 % of
   factory kill k, RMS within ±1 dB. They keep the same pitch order, so the escalation still reads as 1, 2, 3, 4, 5.
5. **Kill effect / finisher sounds.** At most 0.5 s / 2.0 s, peak at least 10 dB below `kill5`. These are on the Skin
   bus, which **ducks by 8 dB for 0.35 s** whenever a gameplay cue plays: enemy shots (`enemyshot*`), `damage`,
   footsteps, spike or utility cues. This uses the `FootstepFx` bus pattern, and the duck is driven from `Sfx.Play`
   and `PlayAt` by key prefix.
6. **Enemy guns and bots never use skin sounds.** Only the player's gun has a skin.

**Files.** `assets/audio/skins/<line>/` holds:
- `fire_<class>.wav`, where class is sidearm / smg / shotgun / rifle / sniper / heavy / suppressed;
- `reload_<reloadkey>.wav` for rifle, pistol, shotgun, revolver and lmg;
- `equip.wav`, `kill.wav`, `fin.wav`, `fin_lite.wav`, `k1..k5.wav`.

That is about 20 short files and about 300 KB per Premium line, 44.1 kHz 16-bit mono. Gun → class comes from a table
in `SkinAudio` (Classic/Frenzy/Bandit/Sheriff → sidearm, Ghost/Spectre → suppressed, …).

**Generator: `tools/sfx/skin_sfx.py`** (new, deterministic, `py -I`):
- It imports the helpers from `flash_sfx.py` (`sweep`, `env`, `resonators`, `modal`, `bubbles`, `impulses`, `reverb`,
  `sat`, `norm`, `fades`).
- It reads the factory clips from `assets/audio/weapons` for the onset, length, RMS and mag-event times.
- It renders each line's recipes (§3) and then prints a **check table** that fails the run if a rule is broken:

  | Check | Rule |
  |---|---|
  | layer silence | the first 9 ms are silent |
  | peak | the layer peak is at least 12 dB under the factory peak |
  | loudness | factory + layer RMS in the first 100 ms is within +0.5 dB of factory |
  | length | the layer is no longer than the factory clip |
  | auto guns | 60 ms maximum |
  | kill k | length and RMS within tolerance of `kill1..5` |
  | duplicates | no two files share the same audio |
  | clipping | nothing clips |

  To check `kill1..5`, the script re-implements `Sfx.cs:168-186` in numpy, or reads a `--dump-kills` WAV export from
  the game.
- Usage: `py -I tools\sfx\skin_sfx.py [--only honeycomb,phosphor] [--review DIR]`. `--review` writes spectrogram sheets
  of factory vs factory + layer.
- Its header lists "100 % our own synthesis, no samples" and is credited in `CREDITS.md`, like `flash_sfx.py`.

**Runtime: `src/Game/Skins/SkinAudio.cs`.**
- `Load(SkinLine)` reads the files lazily through a new `Sfx.Register(key, AudioStream[])`, with keys
  `skin.<line>.fire_rifle` and so on. It is called at `Equip` (before the countdown) and in the picker.
- `Shot(look, pitch)`, `Reload(look)`, `Equip()`, `KillKey(streak)` and `Kill()` / `Finisher(lite)` play through
  `Sfx.PlaySkin(...)`. That is a **dedicated pool of 12 `AudioStreamPlayer`s** on bus `"Skin"`, so the 24 gameplay
  voices are never stolen.
- `Sfx.PlaySkin` respects `SkinSounds` (off → returns) and `SkinFxVolume`. **`GameSession.Play2D` keeps its `Demo`
  mute**, so skin calls from the session go through a `GameSession` wrapper that checks `Demo` too.

### 4.4 Player kills and kill effects
**Detection (`src/Game/Skins/KillTracker.cs`, one per session, owned by `GameSession`):**
1. `IGame` gains `void OnPlayerKill(BotCharacter bot, HitZone zone, float dist) {}`, a default no-op so modes and tests
   compile.
2. It is called from `BotMode.ShootBots` right after `best.Die(d)` (`BotModes.cs:40`) and from `DefenseModes.cs:544`.
3. `GameSession` sets `inPlayerBullet = true` around `Mode.OnBullet` in `Bullet()` (l.757).
   `OnPlayerKill` is ignored unless **all** of these hold:
   - `inPlayerBullet` is true;
   - `!Demo` (the skin collection's own stage runs its own tracker);
   - the gun in hand is skinnable (`GunSkins.Skinnable`, so Chamber's gold guns are out);
   - `!AbilityInHand`;
   - `Mode.SkinFxAllowed` is true (a new virtual, default true; **false** in `SensFinderMode`, `CrosshairFinderMode`
     and 2D modes).
4. The tracker records `KillInfo(Feet, Chest = Head − 0.35 m·up, Headshot, Dist, Now)`.
   - **Immediately:** it plays the kill effect (§4.4 world + gun-side) if the state has `KillFx`, and calls
     `viewmodel.OnPlayerKill()`.
   - **It then waits up to 0.3 s** for a finisher trigger: `GameSession.Event` sees `"round_won"` (a read-only check
     in l.655), or `Mode.Done` turns true.
   - If one comes, it launches the full **finisher** at that `KillInfo` (§4.5). The kill effect is short and the
     finisher overlaps it.
5. Streaks: the tracker keeps its own count of kills per life (reset in `Respawn` and on player death) for the lite
   finisher rules. The kill-sound streak stays in `TrainingMode.RegisterKill`.
6. **Nothing is written anywhere.** No telemetry event, no replay event, no stats. The tracker only reads.

**Gun-side kill effect.** `Viewmodel.OnPlayerKill()` sets `kill_t = 0`. `VmMaterials.SetSkinDynamics(fire, kill,
heat, count, ads)` pushes `fire_pulse`, `kill_pulse`, `gun_heat` (from `WeaponController.Heat`) and `kill_count` to
**skin materials only** (a third list next to `lit` and `all`). The shader adds glow **only** where the pattern
already glows, masked off the top 15 % and multiplied by (1 − adsK). Energy is at most 1.0 and the decay is at most
0.4 s. This costs nothing when unused: the uniforms default to 0, and Factory and Select never get the skin dynamics
call.

**World kill effect (`src/Game/Skins/KillFxWorld.cs` + `KillFxRecipes.cs`, `assets/shaders/skins/skinfx.gdshader`).**
- It is a pooled node like `FxWorld`, created under `Effects.Root` at `Restart` (`GameSession.cs:305-306`). It only
  builds the pools the equipped line's recipes need, and it pre-warms them with an invisible burst 200 m below the
  world (the `FxWorld` trick), so no pipeline compiles mid-fight.
- Recipes are **compositions of five building blocks**, so the lines share code:

  | Block | Node | Used by |
  |---|---|---|
  | `Burst(shape, n, speed, gravity, life, size)` | `GpuParticles3D`, one-shot, draw meshes: spark quad (existing), flat **shard** (hex / tri / 3–5-gon, built in code), soft **mote**, rain **streak** | hex shards, rock shards, droplets, steel motes |
  | `GroundRing(kind, r, life)` | 1 horizontal `QuadMesh` at the feet + `skinfx.gdshader` (`kind`: ripple, crack ring, hex tiles, hairspring spiral, puddle field, calibration grid, stamp) | Rainfront, Strata, Escapement, Calibrated |
  | `Billboard(kind, w, h, life)` | camera-facing quad (`billboard_mode` in the shader) | Phosphor beam / CRT line + dot, Escapement wheel outline |
  | `Shell(kind)` | a small code-built `ArrayMesh`: geodesic hex dome (42 cells), strata pillar (5 boxes), cloud ellipsoid | Lattice, Strata, Rainfront finishers |
  | `Label(text, life)` | `Label3D` (billboard, no depth test off: it stays occluded by walls) | Calibrated distance callout |

- `skinfx.gdshader` is **one** uber shader (`kind` int, `t` 0..1, `col_main` / `col_accent`, additive,
  `unshaded`, `depth_draw_never`, `cull_disabled`), with `#define LITE` for Low. Each kind is a cheap SDF (rings,
  spiral, hex grid, line) with `fwidth` anti-aliasing. It works on GL Compatibility (no `textureQueryLod`, constant
  loops).
- **Slots:** 3 kill slots (round-robin; a 4th concurrent kill steals the oldest) and 1 finisher slot. Each slot
  pre-holds its recipe's nodes. Spawning means: set the transform, set instance uniforms, `Restart()`. No allocations
  and no `new` during play.
- **Readability constraints** (enforced in `KillFxWorld`, not left to recipes):
  - Kill effects last at most 0.9 s and stay at most 1.0 m above the feet, except Phosphor's thin beam.
  - Alpha is capped at 0.5 and blending is additive.
  - No flash brighter than emission 1.0, no screen-space effect, no `OmniLight3D`, no decals.
  - **Spawn-clear rule:** `GameSession.SpawnBot` tells `KillFxWorld` the spawn point, and any effect within 2.5 m
    fast-fades in 0.12 s. The next bot is never hidden.
  - **Close kills:** if the death point is within 3 m of the eye, the world effect scales ×0.5 and the finisher uses
    its lite version.
  - The `VisualLayer` is world layer 1, like `FxWorld.WorldMask`, so it never draws on the viewmodel.

### 4.5 Finisher trigger rules
| Drill type | Examples | Full finisher | Lite finisher |
|---|---|---|---|
| **Round drills** (emit `round_won`) | Site Clear, Post-Plant, Retake, Anchor, Strafe Duel, Spray Transfer, Counter-Strafe, Jump / Jiggle Peek, Defense, Duelist / Initiator / Controller drills | the player kill followed by `round_won` within 0.3 s (the last enemy of the round, as in VALORANT) | – |
| **Deathmatch** | `deathmatch` | the kill that wins the match (`Mode.Done` turns true within 0.3 s) | 5 kills in one life (then every 5th in that life) |
| **Rep drills** (one bot at a time, no rounds) | Strafe Bots, Peek, Operator, Sheriff One-Taps, PeekMix, Long Taps, Target Switch | – | every 5th kill without dying (tracker streak) |
| **Target drills** (spheres) | Gridshot, Flick, Spidershot, Microshot, Popup, Tracking, Reaction | none: no bot dies. The gun-side pulse and kill sounds are also off: no `ShootBots` | – |
| **Measuring tools** | Sens Finder, Crosshair Finder | none (`SkinFxAllowed = false`): trials must look and sound identical | – |
| **Previews** | drill preview (`Demo`) | none (muted and quiet like today) | – |
| **Chamber drill** | gold Headhunter / Tour De Force | none (not skinnable) | – |

- A full finisher lasts at most 2.0 s. It plays only when no enemy is left (round won / match won), so it can be
  bigger.
- A lite finisher lasts at most 1.2 s, is ground-level and at most 0.8 m high, because enemies may still be alive.
- When a round ends, modes freeze or despawn bots (`TacticalCore.cs:143`). The finisher is world-anchored and does not
  follow the corpse.
- Finishers never delay `Finish()`. If the run ends during one, the world is freed with the session.

### 4.6 Enemy-colour safety
`G.Enemy` is the player's enemy highlight (red, yellow or purple, as in VALORANT). Before spawning, `KillFxWorld`
compares the palette's `Main` hue with `G.Enemy`'s hue. Within 30° and at saturation above 0.4, it uses `Alt`
instead. So no world effect is ever mistaken for an enemy outline.

### 4.7 Equip flourish and inspect
- **Equip flourish** (`EquipFlourish` feature): `Viewmodel.UpdatePose` (l.413-415) adds `pose + flourish(e) * eo`. The
  flourish is a small extra rotation curve per line (Escapement: a quarter roll). It ends exactly when the factory
  raise ends, and **`Gun.EquipLeft` is untouched**: never slower, never faster [R1].
- **Inspect (optional, all guns including factory).** `GameAction.Inspect` (Y) plays a 2.5 s procedural twist (an
  additive curve). It is blocked while aiming, reloading, firing or equipping, and any of those cancels it at once.
  - Lines with `Sounds` add an inspect sound and set the L2 animation to full intensity during the inspect.
  - Ship this only if time allows. The design does not depend on it.

### 4.8 Kill counter and banner
- **Kill counter** (Phosphor L5). There are new shader uniforms: `kill_count` (int), `counter_on` and `counter_rect`
  (a gun-space rect on the camera-facing receiver side). `Viewmodel` computes the rect from the gun bounds: centre
  45–60 % along z, 40–60 % of the height, on the −x side, or +x when `LeftHandedWeapon`. A per-pose override may be
  added after the screenshots.
  - The digits are 7-segment SDFs, two digits wide, with a "GG" state.
  - The value comes from `GameSession` every frame: `Mode.Kills`, the drill's own count, already on the HUD.
  - It is hidden at ADS and outside the sight picture by the same mask.
- **Banner.** `Hud.DrawKillBanner` (l.504-540) takes a `BannerStyle` (colours plus a motif callback: Lattice hex
  frame, Rainfront drop, Strata layers, Escapement spiral, Phosphor scope screen).
  - It is drawn only where the banner already exists (every `RegisterKill` drill) and with the same timing (1.4 s).
  - At L4 for Premium lines, L5 for Ultra.

### 4.9 Settings and persistence (`settings.json`)
```jsonc
"GunSkins":   { "Vandal": "honeycomb.teal", "Sheriff": "escapement" }, // existing field; may now carry .variant
"GunSkinAll": "carbon",                                                 // existing
"SkinLevels": { "honeycomb": 3 },   // displayed level per line; missing = highest unlocked
"SkinSounds": true,                 // false = factory sounds (layers, equip, kill 1–5 all off)
"SkinKillFx": 2,                    // 0 off · 1 gun only · 2 gun + world
"SkinFinishers": true,
"SkinKillCounter": true,
"SkinFxVolume": 0.8                 // Skin bus volume (× master Volume)
```
- These are plain fields in `AppSettings` next to `GunSkins` (l.50-52). Old builds ignore them. Unlock state is not
  stored here (§4.2).
- **Settings → General → "Skin effects"** (rows after "Gun skins"): Sounds, Kill effects (Off / Gun only / Full),
  Finishers, Kill counter and Volume. They use the `Selector` pattern of the volume row (`SettingsScreen.cs:518-525`).
  A footnote reads: *"Cosmetic only: never in Sens / Crosshair Finder; gameplay sounds always play at full volume."*
- `SetLevel(line, n)` / `SetVariant(gun, id)` live in `GunSkins` and save under the usual rules (`--dev` writes only
  with `--data-dir … --write-data`).

### 4.10 Collection UI (extends `SkinPicker` / `SkinPreview`)
```
┌ GUN SKINS · COLLECTION ─────────────────────────────────────────────────────────────────────────────────────────┐
│ GUNS (existing list)  │  PREVIEW  [INSPECT | IN HAND | SHOWCASE]                    │ SKIN LINES  (by edition)   │
│  ▸ Vandal · Lattice   │                                                              │ ◇ SELECT · Field Issue ▸  │
│    Phantom · Factory  │      (live 3D: turntable / first person / showcase stage)    │ ◇◇ DELUXE  Carbon Weave   │
│    …                  │                                                              │ ◇◇◇ PREMIUM ▸ Lattice ✓  │
│                       │  LATTICE · PREMIUM  ─────────────────────────────────────    │    Rainfront 🔒 U3        │
│                       │  LEVELS  (1)━━(2)━━(3)━━(4)   L3 SOUNDS + KILL EFFECT         │ ◇◇◇◇ ULTRA  Phosphor 🔒   │
│                       │          ✓    ✓    ✓   🔒  "2 SP · you have 1"               │ ★ EXCLUSIVE Calibrated 🔒 │
│                       │  VARIANTS [■ Amber][■ Teal🔒][■ Frost🔒][■ Lime🔒]  needs L4   │                            │
│                       │  [UNLOCK LEVEL 4]  [WEAR LEVEL: 3 ▾]  [EQUIP ON VANDAL] [ALL GUNS]                        │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Level track.** Each node is a level (number, short label, ✓ / 🔒 / "wearing").
  - Clicking a node **previews that level live**, even when it is locked. VALORANT previews locked levels by video;
    ours renders them live.
    - L2: the animated shader.
    - L3: SHOWCASE fires every 1.2 s and kills the dummy every 2.5 s with the kill effect and sounds.
    - L4: the dummy's death plays the finisher.
    - L5: the counter digits and a banner mock in the corner.
  - Locked items preview but show the lock and `ISkinUnlocks.HowTo(id)`. Their EQUIP button is disabled.
- **SHOWCASE view** (new `SkinPreview.View.Showcase`).
  - The preview's own `World3D` holds a floor disc, the IN HAND viewmodel, a `BotCharacter` dummy 7 m ahead and its
    own `KillTracker`-like driver that calls `KillFxWorld` and `SkinAudio` directly. No `GameSession` and no
    `Effects.Root` are involved, so drills are never touched.
  - Sounds play at `SkinFxVolume` (the picker isn't a `Demo`).
  - `UpdateMode.WhenVisible`. Freed on close.
- **Variant swatches** reuse `SkinSwatches` renders: one cached render per line × variant, lazily, about 4 frames
  each.
- **Unlock button.** It calls `ISkinUnlocks.TryUnlock`. Levels are bought in order (button text "UNLOCK LEVEL n · 2
  SP"). Variants become available after the top level.
- **"WEAR LEVEL"** chooses the displayed level, which is stored in `SkinLevels`.
- **Keyboard:** ←/→ lines, ↑/↓ guns, 1–5 level preview, V cycles variants, Tab cycles views, Esc closes. This
  continues the existing picker's keys (`SkinPicker.cs:387-…`).
- The **edition pips** are our own glyphs drawn with `Gfx`: Select Dim, Deluxe Teal, Premium Good, Ultra Warn,
  Exclusive Accent star. They are not Riot's gem icons.

### 4.11 Performance budgets (Medium, P2000 / GTX 1050 class)
| Item | Budget | How |
|---|---|---|
| Animated / reactive skin (L2) | +0.2 ms GPU over Factory | one extra uniform-branch per pixel; `--vmbench` (VmTest) |
| New patterns 12–16 (L1) | +0.15 ms, as in v1.19 | at most 2 noise lookups; `fwidth` fades |
| Low (`SKIN_LITE`) | +0.08 ms | no `TIME` animation (static L1 look plus the reactive pulse), no glints |
| Kill effect (2 concurrent) | at most 0.25 ms GPU, at most 0.05 ms CPU per spawn | at most 48 particles, 1 quad, additive unshaded; Low ×0.5 particles |
| Full finisher | at most 0.5 ms GPU during its 2 s | at most 160 particles, at most 3 meshes; Low ×0.5 |
| Shader pipelines | 0 compiles mid-run | pre-warm at `Restart`; the picker pre-warms its line |
| Audio | at most 2 extra voices per shot, dedicated pool | layer files about 15–60 ms |
| Memory | about 0.3 MB audio + about 50 small nodes per equipped line | lazy load per line |
| Settings Off | exactly the v1.19 cost | no pools, no dynamics calls, no layer plays |

### 4.12 What must NOT change
- `Game/Replay/*` (format and recorder), `Game/Telemetry.cs`, `Core/Stats.cs`, `Analysis/*`.
- `WeaponController` behaviour (`Heat` is only read), `WeaponDef` numbers, `Gun.EquipLeft`.
- Bots' look and outlines (`BotAssets`). Enemy sounds.
- `Sfx` gameplay voices (skin audio has its own pool and bus).
- `GameSession.Event` only gets a read-only check. No new events are written.

---

## 5. Tests

### 5.1 Dev flags (all `--dev` only, never saved)
| Flag | Effect |
|---|---|
| `--skins-unlock-all` | `ISkinUnlocks.Has` always true |
| `--skin <line>[.<variant>][@<level>]` | every gun wears it (extends today's `--skin`, `GunSkins.cs:109`), e.g. `--skin phosphor.amber@5` |
| `--skinfx-auto [S]` | every S s (default 2) fire **one real player bullet** (through `GameSession.Bullet`, so it is tracked like a shot) from the eye at the nearest live bot's head. It drives the real kill → finisher path in any bot drill. Telemetry is suppressed for these shots (dev runs never save anyway) |
| `--skinfx-finisher` | treat every tracked kill as round-ending (full finisher every kill) for screenshots |
| `--skinfx-lite` | force the lite finisher |
| `--skinfx-bench` | in a range drill: 0, 1, 2, 3 concurrent kill effects, then a finisher, at fixed spots; logs `[skinfx] gpu ms` for each step (VmTest's GPU timing method) |
| `--vmtest … --skincycle all\|ids --skin-level N` | the existing contact-sheet tool, now per level / variant (`prefix_<line>_<level>_<variant>.png`) |
| `--vmkill` | VmTest: trigger `OnPlayerKill()` every 1.5 s (gun-side pulse screenshots) |
| `--screen skins --skins-line <id> --skins-level N --skins-variant <id> --skins-view inspect\|hand\|showcase` | picker screenshots |
| `--dump-kills DIR` | write `kill1..5` WAVs (for `skin_sfx.py` matching) and quit |

### 5.2 Offline audio checks
`py -I tools\sfx\skin_sfx.py --review %TEMP%\skinsfx` must print **all PASS**. It covers onset silence, peak margin,
first-100 ms RMS, length, the auto-gun 60 ms rule, kill-set matching, no clipping and no duplicates. Spectrogram
sheets go to the review folder for a by-ear and by-eye pass. CI runs it in check-only mode (`--check`, no writes).

### 5.3 Screenshot and run plan (sandboxed, as in skins-design §6)
```powershell
$t = Join-Path $env:TEMP "vt-skins2"; $data = "$t\data"; $noval = "$t\valorant"
New-Item -ItemType Directory -Force $data, $noval | Out-Null
Copy-Item "$env:APPDATA\ValTrainer\settings.json" $data -ErrorAction SilentlyContinue
$sb = "--data-dir $data --valorant-dir $noval --skins-unlock-all"
```
(Use paths without spaces: gshot splits `-Extra` on spaces. Run one Godot at a time. gshot only captures and kills its
own process tree.)

| # | What | Command | Pass |
|---|---|---|---|
| S1 | Factory and Select unchanged | `tools\gshot.ps1 -Mode gridshot -At 60 -Name f -Extra "$sb --vmtest --vmcycle all --vmevery 2.5 --vmshots 1 --vmout $t\after"` vs the pre-change set | pixel-identical |
| S2 | Every line × level on Vandal, Sheriff, Operator, Odin | `… -Name lv -Extra "$sb --vmtest --vmkind Vandal --vmview side --skincycle all --skin-level 4 --vmout $t\lv"` (repeat per kind and level) | patterns continuous across attachments; glass and sights intact; no glow in the top 15 % |
| S3 | Gun-side pulse + ADS fade | `… -Mode strafebots -At 4,4.15,4.3 -Name gp -Extra "$sb --skin honeycomb@3 --skinfx-auto 2"`; again with `--force-ads` | pulse visible at hip, gone at ADS; sight picture clean |
| S4 | World kill effects | `-Mode peek -At 4.05,4.25,4.5,4.8 -Name kf -Extra "$sb --skin <line>@3 --skinfx-auto 2"` for each Premium / Ultra / Exclusive line | at most 0.9 s, ground-level, no flash, the next bot is not hidden (spawn-clear) |
| S5 | Full finishers | `-Mode strafeduel -At 4.1,4.6,5.2,5.9 -Name fin -Extra "$sb --skin <line>@4 --skinfx-auto 2"` (round drill: a real `round_won`); `-Mode siteclear` equivalent | a finisher on the round-winning kill only; at most 2 s |
| S6 | DM rules | `-Mode deathmatch -At 30 -Name dm -Extra "$sb --skin phosphor@5 --skinfx-auto 1.2"`; read the log for `[skinfx] lite finisher (5 in a life)` / `finisher (match won)` | bot-on-bot kills trigger nothing; the counter matches the kills |
| S7 | Excluded drills | `-Mode sensfinder …`, `-Mode xhairfinder … --skin honeycomb@4 --skinfx-auto 2` | no effects, factory kill sounds |
| S8 | Enemy-colour swap | set `EnemyColor` yellow in the sandbox settings, S4 with `honeycomb@3` | the world effect uses Frost, not amber |
| S9 | Performance | `-Mode gridshot -At 30 -Name bench -Extra "$sb --skinfx-bench --skin rainfront@4"`; `--vmbench` with each L2 skin | within the §4.11 budgets at Medium; repeat with `"Quality": 0` |
| S10 | GL Compatibility | `tools\godot-run.ps1 … --rendering-method gl_compatibility -- --dev $sb --mode strafeduel --skin phosphor@5 --skinfx-auto 2` | no `SHADER ERROR`; effects visible |
| S11 | Collection UI | `tools\gshot.ps1 -At 4,7 -Name ui -Client -Extra "$sb --screen skins --skins-line honeycomb --skins-level 4 --skins-view showcase"`; without `--skins-unlock-all` (lock states, HowTo text); at `--window 1280x720` and `1024x768` | track, swatches and locks readable; showcase kills and finisher play |
| S12 | Persistence + clamping | `--write-data` + picker dev answers set `honeycomb.teal`, level 3; relaunch **without** `--skins-unlock-all` | resolution clamps to unlocked (base palette, highest unlocked level); the file is unchanged without `--write-data` |
| S13 | Replays and telemetry untouched | `tools\selftest.ps1` (`--replay-check`, `--culture-test`); a Peek run with `--skinfx-auto` then Rewind | `.vtr` and `.vtt` identical in structure; Rewind shows the skin and no kill effects |

### 5.4 Data checks (`CultureCheck`, run by `selftest.ps1`)
- Line, level and variant ids are unique, lowercase and stable. Every Premium line has 4 levels, Ultra has 5,
  Select 1.
- Features accumulate. Every line with `Sounds` has every required audio key (a `ResourceLoader.Exists` check).
- Resolution: unknown or locked → clamped. `line.variant` parsing. `SkinLevels` round trip under invariant, de-DE and
  tr-TR.
- `SkinFxAllowed` is false for the finder modes.

### 5.5 Hand checks by the maintainer
- One minute each of Deathmatch, Strafe Duel and Peek with Phosphor L5 at Medium on the P2000. FPS must match Factory
  within noise.
- Spray-by-ear check: with Lattice L3, the fire sound's attack and spray rhythm must sound the same as Factory.
- Footsteps must be audible during a finisher (ducking works).

### 5.6 Name re-check before release
A tiny script (not committed, run from the scratchpad):
1. Download `https://valorant-api.com/v1/weapons/skins` and `/v1/bundles`.
2. Grep every line and variant display name (case-insensitive substring).
3. Rename on any line-name hit.

---

## 6. Build split: 4 parallel agents by file ownership

**Day 0 contract (Agent A, first 1–2 h):** commit the **API stubs**, so B, C and D compile against them at once:
- `SkinCatalog` types (§4.1), with full data for `honeycomb` and stubs for the rest;
- `SkinState`;
- `GunSkins.StateFor`;
- `ISkinUnlocks` + `DefaultSkinUnlocks`;
- `IGame.OnPlayerKill` default;
- the empty `SkinAudio` / `KillFxWorld` signatures.

| Agent | Owns (create / change) | Delivers | Tests |
|---|---|---|---|
| **A: Data, unlocks, persistence, UI** | create `src/Game/Skins/SkinCatalog.cs`, `SkinState.cs`, `SkinUnlocks.cs`, `src/UI/SkinLevelTrack.cs`; change `src/Game/Weapon/GunSkins.cs` (facade, resolution, clamping, `--skin` grammar, `--skins-unlock-all`), `src/Core/AppSettings.cs`, `src/Core/CultureCheck.cs`, `src/UI/SkinPicker.cs`, `src/UI/SkinPreview.cs` (SHOWCASE view, using B/C/D APIs), `src/UI/SettingsScreen.cs` (Skin effects rows), `src/Main.cs` (`--screen skins` args) | the catalogue data for all 14 entries (looks, levels, variants, palettes, unlock ids, gates, costs, challenge ids), the collection screen and settings | S11, S12, §5.4 |
| **B: Viewmodel visuals** | change `assets/shaders/weapon/viewmodel.gdshaderinc` (patterns 12–16, `skin_sub`, `fire_pulse`, `kill_pulse`, `gun_heat`, kill-counter digits, Lite reductions), `src/Game/Weapon/VmMaterials.cs` (`skin` list, `SetSkinDynamics`, new uniforms in `MakeSkin`), `src/Game/Weapon/Viewmodel.cs` (`OnPlayerKill`, `SetKillCount`, `SetHeat`, dynamics in `_Process`, equip flourish, optional inspect), `src/Game/Weapon/VmTest.cs` (`--skin-level`, `--vmkill`, skincycle per level / variant) | every look and level renders correctly and stays inside budget | S1, S2, S3, S9 (vmbench), S10 |
| **C: Audio** | create `tools/sfx/skin_sfx.py`, `assets/audio/skins/**`, `src/Game/Skins/SkinAudio.cs`; change `src/Audio/Sfx.cs` (`Register`, `"Skin"` bus, 12-voice skin pool, ducking by cue key, `PlaySkin`, `--dump-kills`); `CREDITS.md` line for `skin_sfx.py` | all sound recipes of §3 rendered and passing the checks, plus the runtime playback API: `Shot(look, pitch)`, `Reload(look)`, `Equip()`, `KillKey(streak)`, `Kill()`, `Finisher(lite)` | §5.2, S5 / S7 by ear |
| **D: Kill / finisher runtime, VFX, wiring** | create `src/Game/Skins/KillTracker.cs`, `KillFxWorld.cs`, `KillFxRecipes.cs`, `assets/shaders/skins/skinfx.gdshader` (+ lite); change `src/Game/IGame.cs`, `src/Game/GameSession.cs` (player-bullet flag at l.757, `OnPlayerKill`, `round_won` watch at l.655, spawn-clear in `SpawnBot` l.907, pre-warm at l.305, shot / reload hooks at l.586 / 598 calling C's API, counter and heat feed to B's API, `--skinfx-*` flags), `src/Game/AdsSession.cs` + `AbilitySession.cs` (equip sound), `src/Modes/BotModes.cs` (call at l.40), `src/Modes/DefenseModes.cs` (l.544), `src/Modes/TrainingMode.cs` (`SkinFxAllowed`, kill-sound key at l.220), `SensFinderMode.cs` / `CrosshairFinderMode.cs` (override false), `src/UI/Hud.cs` (banner style) | the kill effects, finishers and banners of all lines; the trigger rules of §4.5; enemy-colour safety | S4–S8, S9 (skinfx-bench), S13 |

- **Merge order:** A's stubs → B, C and D in parallel → A's UI showcase last, because it uses all three APIs.
- **Integration (whoever finishes first):** `CHANGELOG.md`, What's New, `CREDITS.md` (procedural skins + `skin_sfx.py`),
  the full `selftest.ps1`, a contact sheet of all lines × levels on 4 guns and a short showcase clip for Discord.
  Commits carry `Adds-Discord:` trailers for the skin posts.
- **Shared-file conflicts avoided:**
  - `GameSession.cs` is D's alone. C and B only expose methods.
  - `GunSkins.cs` is A's alone.
  - `Sfx.cs` is C's alone.
  - `Viewmodel.cs` and `VmMaterials.cs` are B's alone.

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| Look-alike or name clash with a Riot line | Clean-room theme list (§3.1); names checked against the 3,193-name dump (zero hits); re-check before release (§5.6); no clock faces, hives, lava, void, glitch or RGB |
| Skin sounds hurt spray-by-ear or footstep cues | Layer-only design, the 9 ms silent onset, the +0.5 dB loudness cap, the 60 ms auto-gun cap, a separate bus ducked by gameplay cues, an automatic check table, a "Skin sounds" off switch |
| Effects hide the next enemy | Ground-level, alpha-capped, at most 0.9 s kill effects; full finishers only when no enemy is alive; spawn-clear fast-fade; close-kill scaling; never the enemy colour |
| Effects become a distraction in focused practice | Settings: kill effects off / gun only / full, finishers off; measuring drills always off; target drills get nothing |
| Wrong kill attribution (bot-on-bot, utility, abilities, Rewind) | The explicit `OnPlayerKill` call **and** the `inPlayerBullet` flag **and** gun-in-hand checks; never hung on `BotFx` (the Rewind's event) |
| Finisher misses because `round_won` fires a frame later, or never fires in some mode | A 0.3 s window; `Mode.Done` as a second trigger; modes without either fall back to the lite streak rule. List per mode in §4.5; S5 / S6 verify |
| GPU cost on P2000 / Low / Compatibility | Budgets §4.11; one uber shader each for the skin and the FX; pre-warm; Lite variants; `--skinfx-bench` / `--vmbench`; S10 on GL |
| Shader-compile hitch on the first kill | Pools are built and pre-warmed at `Restart` (invisible burst below the world), the same proven method as `FxWorld` |
| Audio voice stealing during sprays | A dedicated 12-voice Skin pool; the gameplay pool of 24 is untouched |
| Showing information through skins (heat, kill distance) | Only data the player already has (recoil feel, the HUD kill count, the hit distance); never enemy positions, timers or anything hidden; all cosmetic and switchable |
| Unlock state edited or copied | Resolution clamps to `ISkinUnlocks`; locked content never renders in drills (the preview may show it, as VALORANT's videos do) |
| Scope creep (inspect, tracers, banner everywhere) | Inspect and tracers are optional (§4.7, Phosphor L3); the banner is restyled only where it exists |
| The progression system isn't ready for 1.19 | `DefaultSkinUnlocks` ships U0 content (both Select collections = today's free skins); everything else shows "Unlocked by training (coming soon)", or the maintainer temporarily sets gates to U0 |

---

## 8. Sources
- [S1] Riot Support, "Price tiers for skins in VALORANT":
  https://support.riotgames.com/valorant/store/price-tiers-for-skins-in-valorant
- [R1] Riot, "The craft and fantasy of VALORANT weapon skins" (dev blog, 2020):
  https://playvalorant.com/en-us/news/dev/the-craft-and-fantasy-of-valorant-weapon-skins (coverage:
  https://www.oneesports.gg/valorant/riot-explains-how-it-designs-valorant-weapon-skins/)
- [P4] Riot, VALORANT Patch Notes 10.11 (Finisher Cam):
  https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-10-11/ ; explainer
  https://eloking.com/blog/what-are-finisher-cams-in-valorant
- [W1] VALORANT Wiki, Weapon Skins: https://valorant.fandom.com/wiki/Weapon_Skins
- [W2] VALORANT Wiki, Kill Banners: https://valorant.fandom.com/wiki/Kill_Banners
- [W3] VALORANT Wiki, RGX 11z Pro Collection (L5 kill counter): https://valorant.fandom.com/wiki/RGX_11z_Pro_Collection
- [W4] VALORANT Wiki, Prime Collection (level and variant rules): https://valorant.fandom.com/wiki/Prime_Collection ;
  Elderflame Collection: https://valorant.fandom.com/wiki/Elderflame_Collection ; Champions 2024 Collection:
  https://valorant.fandom.com/wiki/Champions_2024_Collection ; Kuronami Collection:
  https://valorant.fandom.com/wiki/Kuronami_Collection
- [W5] VALORANT Wiki, Radianite Points: https://valorant.fandom.com/wiki/Radianite_Points
- [W6] VALORANT Wiki, Reaver Collection (variant-coloured finisher and banner):
  https://valorant.fandom.com/wiki/Reaver_Collection ; Protocol 781-A:
  https://valorant.fandom.com/wiki/Protocol_781-A_Collection
- [W7] VALORANT Wiki, Melee: https://valorant.fandom.com/wiki/Melee
- [W8] VALORANT Wiki, Crimsonbeast Collection (on-weapon kill effect):
  https://valorant.fandom.com/wiki/Crimsonbeast_Collection
- [W9] VALORANT Wiki, Cosmetics (Random Favorite): https://valorant.fandom.com/wiki/Cosmetics
- [W10] VALORANT Wiki, Battlepass: https://valorant.fandom.com/wiki/Battlepass
- [W11] VALORANT Wiki, Contracts / Agent Gear: https://valorant.fandom.com/wiki/Contracts
- [P1] GameRevolution, how to level up skins:
  https://www.gamerevolution.com/guides/652665-how-to-level-up-skins-in-valorant-upgrade-radianite-points ;
  esportnow: https://esportnow.gg/valorant/news/how-to-upgrade-valorant-skins-in-2026 ; GameRiv (patch 1.09 variant
  choice): https://gameriv.com/in-valorant-patch-1-09-players-will-finally-be-able-to-select-any-variant-of-the-skin-they-want-from-the-start/
- [P2] Dexerto, skin level toggle: https://www.dexerto.com/valorant/valorant-skin-level-toggle-arriving-patch-3-08-1671668
- [P3] bo3.gg, finishers: https://bo3.gg/valorant/articles/the-best-finishers-in-valorant
- [P5] Sportskeeda, ace effects: https://www.sportskeeda.com/valorant/top-5-valorant-skin-collections-best-ace-effects
- [P6] Billboard, collaboration skin audio:
  https://www.billboard.com/music/music-news/zedd-valorant-riot-games-skins-collaboration-9625347/
- [D1] Community game-data dump (not official; level and variant counts, preview-video and swatch fields, name check):
  https://valorant-api.com/v1/weapons/skins , https://valorant-api.com/v1/contenttiers ,
  https://valorant-api.com/v1/bundles (downloaded 2026-11-07)
- Bundle prices: Dexerto, https://www.dexerto.com/valorant/all-valorant-skin-bundles-tiers-weapons-prices-1456863

Not verified: how long finishers last, finisher behaviour when the round ends on the Spike, and exactly when the
escalating kill sounds reset.
