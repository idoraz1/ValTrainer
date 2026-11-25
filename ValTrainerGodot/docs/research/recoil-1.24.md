# Recoil and spread from the game files (v1.24)

Until 1.23 every per-bullet recoil and spread table in `src/Core/Weapons.cs` was an estimate rebuilt from Riot's patch-note
rules (weapons-1.18.md §3). Since 1.24 they are the real per-gun curves from the game files, as published by
**[valorant.gs](https://valorant.gs)** (unofficial VALORANT database, values read from the game files, build
13.10.00.5893614, extracted 2026-11-10). Suggested on Discord.

## Source and terms

- Data: the JSON Data API, `https://valorant.gs/api/v1/weapons/` (21 rows: 20 guns + melee) and `/api/v1/meta/`. The
  per-weapon pages (e.g. `https://valorant.gs/weapons/vandal`) show the same numbers and explain each field.
- Copies of the raw JSON we used: `docs/research/valorant-gs/weapons-13.10.json`, `meta-13.10.json` (data only: no images,
  sounds or models; the JSON holds image URLs as text, nothing was downloaded from them).
- Terms (`https://valorant.gs/terms/`, updated 2026-11-10): "You are welcome to link to any page and to quote figures with
  a link back. Please do not scrape the site at high volume or republish the data wholesale as your own." The Data API page
  adds: "The game data belongs to Riot Games. The extraction and the processing are free to reuse; a link back to
  valorant.gs is the only ask." So reuse is allowed with credit. We fetch one table per patch (two requests), credit
  valorant.gs in the code (`ValorantGs.g.cs`, `Weapons.cs`), here and in the changelog, and only keep the weapons table.
- Chamber's Headhunter and Tour De Force are **not** in the API (they're ability equippables); they keep their estimates.

## Importer

`python tools/valorant-gs/import_recoil.py [--fetch] [--summary]`

- `--fetch` downloads the current `weapons` and `meta` tables into `docs/research/valorant-gs/` (`weapons-<version>.json`).
- It samples each StabilityComponent firing curve (x = consecutive shots) at whole bullets 0, 1, 2 … up to the magazine
  (linear between keys, flat before the first and after the last key, like an Unreal curve with constant extrapolation)
  and writes `src/Core/ValorantGs.g.cs`: one class per gun with `Pitch`, `Yaw`, `Spread`, `HeatCap` and every scalar the
  files have (recovery, tap efficiency, first-shot error, aimed / crouched / running multipliers, movement error). It never
  touches `Weapons.cs`: that file reads `Gs.<Gun>.<Field>`, so after a patch you re-run the script, build, and run the
  selftest. A field a patch removes becomes a compile error, which is the point.
- A value the files leave out is a blueprint default the site can't see (it's inherited from a parent blueprint, not
  zero). The importer writes nothing for it and Weapons.cs keeps ValTrainer's own number (marked in its doc comment).
- Integer sampling flattens keys that sit between bullets (Vandal 8.77 → 7.455): the in-game curve is within ~0.1° of the
  table at every bullet. Interpolation between keys is assumed linear (the API doesn't say; the Vandal's error keys
  1 → 0.3, 2 → 0.35, 5 → 1 and the first-shot / max error stats agree with linear).

## Units: what the recoil numbers mean

ValTrainer's `Pitch` / `Yaw` tables are degrees of view kick, cumulative from the original aim point, and the camera follows
the recoil 1:1 (the bullet leaves along the kicked view, then the error cone is applied). The question is whether the
files' numbers are the same thing.

Evidence:

1. **They're Unreal rotator values.** The site reads them from `StabilityComponent.PitchRecoil.FiringCurve` and
   `YawRecoil.FiringCurve` (About page). VALORANT is Unreal Engine (5.3.2 per `meta`); a pitch/yaw recoil amount on a
   rotator is in degrees. The site calls them "Pitch kick per shot; the climb of the spray pattern" and "Yaw kick per shot;
   where the pattern wanders sideways".
2. **Riot's patch notes use the same numbers.** Patch 13.00 said the Bandit's "pitch recoil at max 4 → 3": its file curve
   tops out at exactly 3. Patch 6.11: the Frenzy reaches max recoil and max spread at bullet 5: its pitch and error curves
   both reach their last key at x = 4 (the 5th bullet). Patch 11.08: Stinger max spread at bullet 6: its error curve ends
   at x = 6. The error curves are certainly degrees (x = 0 is the armory's "first-bullet accuracy" exactly, for every gun:
   Vandal 0.25, Phantom 0.2, Sheriff 0.25 …, and the ADS rule ×1.15 − 0.13 gives the armory's ADS first-bullet accuracy
   0.1575), and the recoil curves share the patch-note vocabulary and scale with them.
3. **Magnitude is plausible.** The Vandal climbs 7.6° by its 8th bullet and holds ~7.5–8.4°: about 1.3–1.5 m at 10 m, i.e.
   an uncompensated spray from the head ends roughly a body height higher, which is what Range wall sprays look like. The
   Phantom (6.4–7.1°) climbs less than the Vandal, matching the community consensus. Our old estimates (Vandal 4.5°) were
   about half that, which is why sprays felt too easy (Discord).
4. **Camera follows the bullets.** VALORANT draws no separate "view punch": the crosshair rises with the recoil and the
   bullets land around it (firing error), which is the model ValTrainer already had (k = 1). No public source suggests a
   CS-style "bullets go 2× the view kick".

What we could **not** find: a published frame-by-frame measurement of a VALORANT spray in degrees (guides show wall sprays
without distances or angles). So the conversion is reasoned, not measured.

**Conclusion:** file units = degrees of view kick = degrees of bullet offset from the original aim point, so the scale is
1 on both axes. **Confidence: medium** (roughly 2 in 3 that 1.0 is right within ±15% for pitch; a bit lower for yaw, where
the sign convention is also unknown, see below).

The calibration is one constant per axis, **`RecoilScale.Pitch` and `RecoilScale.Yaw` in `src/Core/Weapons.cs`** (both
1.0). Every consumer goes through them (`WeaponDef.PitchAt` / `YawAt`: WeaponController, the coach's SimAim, the spray drill
requirement in RunAnalysis; WeaponCheck's reference controller multiplies too). To recalibrate from a spray video: Range,
Vandal, 10 m wall, no compensation, hold 25 bullets; measure the bullet-hole height above the first hole for bullets 8–25
(cm ÷ 1000 cm → radians → degrees) and divide by the table value (~7.9° average); same for the sideways spread with Yaw.
Change only those two numbers; the selftest checks against them, not against hard-coded kicks.

### Horizontal sign

The files' yaw curves are signed: the Vandal and Phantom are positive throughout, the Guardian is negative throughout, and
the Bulldog goes +0.5° by bullet 5 and then swings to −1.5…−1.8°. ValTrainer keeps its side-locked drift model (each gun
starts on `FirstDriftSide`: Vandal left, Phantom right; 10% chance to swap per bullet after the protected bullets) and
multiplies the **signed** table by that side, so a curve that crosses zero (Bulldog) swings to the other side relative to
where it started, and the small left/right wobble after the climb (Vandal 1.92–2.12°, Phantom 1.70–1.84°) is in the tables.
Whether the files' positive yaw is left or right on screen isn't known (Unreal's +yaw is to the right, but Riot may apply
it negated or randomise the side), so we keep our drift sides; the Guardian now drifts opposite to the old estimate because
its whole curve is negative.

## What else came from the files (where WeaponController/Mover already had the concept)

| Concept | Files (valorant.gs field) | ValTrainer |
|---|---|---|
| Per-bullet error | `curves.error` | `Spread` table, `MaxSpread`, `FirstShotHip` |
| Recovery time, tap efficiency | `stability.recoveryTime`, `tapEfficiency` | same fields |
| Aimed error | `curves.ads.error` (×) and `errorAdd` (+) | **new** `AdsErrorMul` / `AdsErrorAdd` (default: the old hip − (FirstShotHip − FirstShotAds) rule) |
| Aimed recoil | `curves.ads.pitch` / `yaw` (×0.7 on Vandal, Phantom, Warden, Odin; Ares yaw only) | **new** `AdsPitchMul` / `AdsYawMul` (default 1) |
| Crouched | `states.crouching` error / pitch / yaw | `CrouchSpreadMul`, `CrouchRecoilMul` (all guns that list it have pitch = yaw) |
| Running | `states.running` pitch / yaw | `RunPitchMul`, `RunYawMul` |
| Walking error | `movement.walking` keys (speed fraction → degrees) | **new** `WalkErrKeys`, evaluated by `Mover`; `WalkErr` = the curve at our walk speed (55%) |
| Running / crouch-walk / jump error | `movement.running`, `crouching`, `jumping` | `RunErr`, `CrouchWalkErr`, `AirErr` |
| Heat cap | last curve key | `HeatCap` (the heat can now reach the end of the curve so the post-climb sawtooth plays) |

The walking curve changes how shooting while walking feels: the files give rifles **3° the moment you leave the 27.5%
deadzone**, rising to 5° at 60% of the run speed (4.7° at ValTrainer's walk speed) and 6° running. The old ramp went from
0° at the deadzone to 3° at walk speed. The curve's first key sits on the deadzone for every gun (Operator 15%, snipers 30%),
which also confirms the deadzone numbers.

## Present in the files, not modelled (no new systems added)

- **Recoil build / decay rates** (`recoilRates`, e.g. "12 up, 1 down"; Ares / Odin 2 up): how fast the view moves toward
  the curve value and back. ValTrainer applies the kick at once and recovers with its own √heat curve over `RecoveryTime`.
  Side effect of the longer heat cap: a full Vandal spray now takes ~1.9 s to recover (was ~1.3 s), an Odin 100-round
  spray ~5.6 s (`RecoveryTime·√heat`).
- **Movement error decay** (`decay`, by how much error has built up): movement error stops at once when you stop.
- **Landing penalty curve** (0.7 at touchdown → 0 at 0.225 s, every gun): read as a multiplier (0.7 × the jump error = 7°
  for rifles, patch 1.09's number), fading out. ValTrainer keeps a flat 7° for 0.225 s for every gun
  (`MovementTuning.LandingErr`); per-gun scaling and the fade need the interpretation confirmed first.
- **Jumping / ascender / ladder recoil multipliers** (Ares / Odin ×1.4, Stinger / Frenzy ×1.25): only running is modelled.
- **Ascender (rope) movement error** and **tagging** (slow when hit; ValTrainer's tagging is its own model).
- **Scoped guns' hip error growth** (Operator 5° → 5.25°, Marshal 1° → 1.25° after a shot): scoped guns keep a constant hip
  error.
- **The Stinger's aimed burst curves**: the API has one stability component per gun; the Stinger's aimed first shot is
  0.35° but its ADS block isn't exported, so its aimed burst tables stay EST. The Bulldog has no ADS block at all (aimed
  first shot 0.3° = hip), so its aimed bursts now use the hip error curve (was an EST 0.1° curve).
- Blueprint defaults the site can't see (kept as before): Vandal / Warden / Stinger jump error, Shorty recoil and
  recovery, Bucky yaw (set to 0, its pitch is 0 in the files) and recovery, snipers' recovery, every pistol's crouch
  multiplier (Wiki 0.75), running pitch for most guns, protected bullets, yaw switch time, the 10% side swap.

## Per gun: before → after

Pitch / yaw = the most the table reaches (degrees); spread = first → max error. "Walk" = error at ValTrainer's walk speed.

| Gun | Pitch | Yaw | Spread | Recovery / tap | Other |
|---|---|---|---|---|---|
| **Vandal** | 4.5 by bullet 12 → **7.6 by bullet 8**, then 7.5–8.4 sawtooth | 2.4 → **2.0 by 13**, wobbling 1.92–2.12 | 0.25 → 1.0 at bullet 8 → **at bullet 6** | 0.375 / 6 (same) | aimed error ×1.15 − 0.13 (aimed 1st 0.1575); **aimed recoil ×0.7**; walk 3 → 4.7 |
| **Phantom** | 3.6 → **6.4 by bullet 8**, then 6.5–7.1 | 2.0 → **1.8 by 14**, wobbling 1.70–1.84 | 0.2 → 0.9 at bullet 11 → **at bullet 5** | 0.35 / 4 (same) | aimed ×1.15 − 0.12; aimed recoil ×0.7; jump 10 → 13 |
| **Spectre** | 2.6 → **5.0 by 10, 5.4 by 12** | 1.6 → **1.25**, none before bullet 7 | stepped → 0.4 → 0.85 (3rd) → 1.05 (6th) → 1.4 (8th) | 0.3 → **0.45** / 3 | aimed −0.15 (same aimed 1st 0.25); walk 1 → 2.2 |
| **Sheriff** | 3.7 over 6 shots → **1, then 2** | 0.8 → **0** | 0.25, 1.0, 2.0, 2.75 → **0.25, 1.1, 2.75** | 0.4 / 3 → **0.5 / 2** | heat cap 5 → 2; walk 1.2 → 2.6 |
| Classic | 2.8 → 1.0 | 0.5 → 0 | 0.4 → 1.8 (by 4th, was 7th) | 0.3 / 3 → 0.3 / 4 | |
| Shorty | (kept) | (kept) | 4 → 4, 4.5 for a quick second shot | (kept) | |
| Frenzy | 2.6 → 5.5 (by 5th) | 1.2 → 1.25 | 0.65 → 1.7 (same) | 0.3 / 2 → 0.45 / 3 | running yaw ×1.25 |
| Ghost | 2.4 → 2.0 | 0.4 → 0 | 0.3 → 1.65 (by 4th, was 8th) | 0.3 / 3 → 0.3 / 4 | |
| Bandit | 3.0 (same plateau, by 5th) | 0.5 → 0 | 0.275 → 2.0 (was 1.97) | 0.4 / 4 (same) | |
| Stinger | 4.0 → 9.0 (5 by 7th) | 1.5 (same, by 11th) | 0.65 → 1.5 (same) | 0.35 / 2 → 0.45 / 1 | running ×1.25 pitch and yaw (was 1.5 / 1) |
| Bucky | 2.0 → 0 | 0.3 → 0 | 3 (same) | (kept) | |
| Judge | 4.0 → 6.0 | 0.8 → 2.5 | 2.5 → 4 (same) | 0.45 / 1 → 0.4 / 4 | |
| Bulldog | 3.9 → 6.7 | 2.0 one way → +0.5, then swings to −1.8 | 0.3 → 1.25 (by 7th) | 0.35 / 3 (same) | aimed 1st 0.1 → 0.3; jump 13 |
| Guardian | 3.2 → 5.0 | 0.6 → −1.25 (other side) | 0.1 ×3 then up → 0.1, 0.3, 1.1, 1.6 | 0.35 / 3 → 0.295 / 10 | aimed −0.1; jump 13.5 |
| Warden | 3.6 → 8.1 | 1.9 → 1.4 | 0.15 → 1.0 (by 6th) | 0.4 / 4 → 0.4 / 9 | aimed ×1.15 − 0.13, recoil ×0.7 |
| Marshal, Operator | 0 (same) | 0 | (kept) | (kept) | walk curve from the deadzone: Operator 10° → 14.3° at walk |
| Outlaw | 4.0 → 3.0 (second shot) | 0 | 3.5 (same) | (kept) | |
| Ares | 3.2 → 13 (over the magazine) | 2.2 → 2.75 | 1.0 → 0.7 by bullet 13 (same) | 0.5 / 2 → 0.65 / 1 | aimed ×1.15 − 0.25, yaw ×0.7; running ×1.4 / ×1.4 |
| Odin | 3.8 → 17 (over 75 bullets) | 2.4 → 3.75 | 0.8 → 1.3 by bullet 75 (was 11) | 0.5 / 2 → 0.65 / 1 | aimed ×1.15 − 0.13, recoil ×0.7; running ×1.4 / ×1.4 |
| Headhunter, Tour De Force | not in the API: unchanged estimates | | | | |

Run `python tools/valorant-gs/import_recoil.py --summary` for the full table straight from the data.

## Follow-ups noticed (not changed)

- The coach's spray drill pull-down requirement keeps its measured constants (Vandal 5.5°/s, Phantom 4.6°/s); other guns
  scale from the Vandal by the table climb over bullets 3–12. With the file curves the Phantom climbs about as fast per
  second as the Vandal there (4.7° in 0.82 s vs 5.5° in 0.92 s), so the 4.6 constant may deserve a re-measure. The Spectre
  now comes out at ≈5.8°/s (the coach self-test bound was updated to that, with a comment).
- An aimed spray fires at ×0.9 rate, so at 60 fps a shot gap can land just over the controller's 1.2 × hip-interval "spray"
  test and count as a (slightly lighter) tap. Pre-existing; it makes aimed sprays climb a little less than the table.

## Checks

`--weapon-check` (selftest "weapon check") gained, per gun: the aimed error rule reproduces the armory's aimed first-shot
error; the walking curve starts on the deadzone and the Mover returns the curve's values (deadzone 0, first key past it,
walk, run, crouch-walk); aimed recoil = hip × AdsPitchMul / AdsYawMul; a held spray puts the view at table × RecoilScale on
every bullet; and anchor bullets copied by hand from the valorant.gs pages (Vandal, Phantom, Spectre, Sheriff, Bulldog,
Guardian, Odin, Ares, Outlaw) so a broken import or a hand edit fails loudly. The old checks were consistency checks
(Spread[0] = first shot, table maximum = MaxSpread …) and still hold; none encoded the old estimates' magnitudes. The
reference controller of the frame-by-frame regression applies the same RecoilScale and aimed multipliers (data, not a
mechanic).
