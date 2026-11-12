# Aim Coach rank estimate reads too high (1.19 calibration design)

**Report.** A player said the coach's rank estimate is "way off": too high. We have no details from them. The developer
is Gold 2 in game (2026-11-02). From 128 runs the coach tells him **"GOLD 3 – PLATINUM 2 · HIGH CONFIDENCE · most likely
Platinum 1"**.

**Scope.** Research and design only. No source was changed. Line refs are against the working tree on 2026-11-07
(v1.18.0 plus the uncommitted v1.19 changes; in `src/Analysis` those changes are i18n only). Paths are relative to
`ValTrainerGodot/`. Marks used below: **[P]** primary source, **[S]** secondary or aggregator, **[E]** our estimate,
**[M]** measured here.

---

## 0. Summary

- **Reproduced exactly [M].** The headless coach on a copy of the developer's `stats.json` gives overall **1.58**, i.e.
  Platinum 1, with evidence 0.89 and a band of 1.40–1.76 (Gold 3 – Platinum 2). His true rank, Gold 2, is T ≈ 1.17–1.33.
  The error is **≈ +0.33 tier, about 2 divisions**.
- **What pulls it up.** Four "drill behaviour" skills read Platinum to Ascendant:
  - Crosshair placement 2.72
  - Movement 2.64
  - Spray 1.89
  - Utility 1.59

  His actual aim reads Bronze to Gold: Flicking 1.12, Precision 0.01, Tracking 1.21. Before the shrink step, crosshair
  placement adds **+0.33** tier to the mean and movement **+0.21**. Precision takes off −0.19.
- **Root causes, in order of impact:**
  1. Behaviour is measured **in drills that make it easy or that ask for it**:
     - Crosshair placement comes mostly from Deathmatch (7.3°), and from Peek Duel / Jiggle Peek where the angle is known
       in advance (2.2–2.7°). The same player scores 12.1° in Site Clear.
     - Counter-strafe use comes mostly from the Counter-Strafe drill (98.5%, because that drill tells you to do it).
     - Spray is measured on a static target at fixed range.

     The benchmark tables still treat these numbers as if they were match habits.
  2. The shrink toward the median assumes aim explains **90%** of rank (`Validity = 0.9`). No published study supports
     anything near that. Our estimate is that mechanics explain about 20–35% of rank variance, r ≈ 0.45–0.6 [E].
  3. The "robust" outlier clip works around the **weighted median**. Behaviour skills carry 0.55 of the weight, so they
     set the median, and the clip then **lifts the low aim skills** (Precision 0.01 is clipped up to 0.84). A synthetic
     player with Gold aim and Radiant drill behaviour is estimated **Ascendant 1 (2.79)** today.
  4. The band and the confidence label overstate certainty:
     - The band is ±0.15 tier (±1 division) at full data. That implies r ≈ 0.96 between the estimate and rank.
     - "HIGH CONFIDENCE" measures **how much data there is**, not how accurate the estimate is.
- **The fix**, all in `Benchmarks.cs`, `Assess.cs`, `RunAnalysis.cs` and `CoachUi.cs`:
  - Normalise crosshair placement by drill, and keep instructed drills out of the movement habits.
  - Spray tables ×0.8 and the pull-delay cap lowered to 1.5.
  - A validity for each skill, so drill behaviour counts less than aim.
  - Behaviour skills may lift the estimate at most +0.5 above the aim core.
  - A wider "likely range" (±0.35 + 0.25·(1−E)), with the label changed from confidence to data coverage.
- **Result:** the developer's data lands at **1.18, Gold 2** (range Silver 2 – Platinum 1). The structural part alone
  (validity, lift cap, no change to tables or data) already gives 1.21 Gold 2, so the result does not hinge on the
  per-drill factors fitted from his data.
- **"Your actual rank" input: worth doing,** but as a **gap view** ("your aim is ahead of / behind your rank → what to
  train") plus an anonymous **copyable calibration line** for fitting the tables later. It should **not** feed a per-user
  offset into the estimate, because that only echoes the entered rank back.

---

## 1. Code map

| Step | Where | What |
|---|---|---|
| Per-run metrics | `src/Analysis/RunAnalysis.cs` (`Run`, A1–A6) | `flick.*`, `head.*`, `speed.*`, `track.*`, `xhair.*` (`Crosshair()` :786), `move.*` (`Movement()` :828), `spray.*`, `react.*`, `flash.*` |
| Sightings | `src/Modes/TrainingMode.cs:110` `SeenEvent` | `bot_seen` = angle crosshair→head when a bot becomes visible. Sightings **> 60° are not recorded** (`SeenMaxDeg`) |
| Stored-run fix-ups | `RunAnalysis.Normalize` :964 | Difficulty-normalised tracking / flash tiers, Spidershot/Gridshot −0.8 speed, drops gun-shaped metrics for unrated guns (`GunMetric` :1000), drops horizontal crosshair numbers for `HorizExcluded` drills :951 (peek, flashmap, longtaps, sheriff, peek_mix, sound) |
| Aggregation | `src/Analysis/Agg.cs` | Per family (`flick`, `xhair`, `move` …): **newest runs until 300 events**, 14-day half-life. Counts are summed, averages weighted by their own sample count. Means of per-run values, **not bests** |
| Profile | `src/Analysis/Coach.cs:83` `BuildProfile` | All runs except Sens/Crosshair Finder → `Normalize` → `Agg.Aggregate` → `Assess.Run(agg, runLevel:false)` |
| Skill tiers | `src/Analysis/Assess.cs:109` `Rate` | 8 composites (below), each metric mapped through `Bench.TierOf` (piecewise-linear over b0…b4, ±0.5 extrapolation) |
| Overall | `Assess.cs:74-96` | Weighted median (w·c) → clip each skill ±0.75 around it → mean M → `Bench.Shrink`: 1.2 + 0.9·E/(E+0.1)·(M − 1.2). Needs ≥ 4 skills with c ≥ 0.5 |
| Constants | `src/Analysis/Benchmarks.cs` | Tables :18–81, `PriorTier` 1.2 :104, `Validity` 0.9 :110, `Evidence0` 0.1 :112, `OutlierClip` 0.75 :114, `Rank()` names |
| Range shown | `src/UI/CoachUi.cs:67` `Spread` = 0.15 + 0.30·(1−E), `RangeTiers` :77, `ConfidenceCaps` :99 | Symmetric band. "most likely" = the estimate itself (`ProfileScreen.cs:178`) |
| Dev CLI | `src/Analysis/CoachCli.cs` | `--coach-analyze <dir> [--brief]`, `--coach-stats <stats.json> --explain`, `--coach-selftest` (archetype calibration :288–318) |
| Sim player | `src/Analysis/SimAim.cs` | `--simaim good[:k=v,…]`. **It cannot move**, so it produces no Movement, Site Clear or Deathmatch data |

**Tier scale.** The tier is continuous: Iron < 0 ≤ Bronze < 0.5 ≤ Silver < 1 ≤ Gold < 1.5 ≤ Plat < 2 ≤ Diamond < 2.5 ≤
Asc < 3 ≤ Imm < 3.5 ≤ Radiant. Each rank is split into 3 divisions of 0.167. Gold 2 = 1.167–1.333. b0…b4 are anchored
at the 5th / 41st / 81st / 98.6th / 99.9th population percentile, so the median player (Gold 2) is 1.2.

**Composites and weights** (`Assess.cs:29`):

| Skill | Weight | Components |
|---|---|---|
| Crosshair placement | 0.20 | 0.7 E, 0.3 \|ε_p\| |
| Flicking | 0.15 | 0.55 speed tier, 0.3 endpoint error, 0.15 onset |
| Precision | 0.15 | 0.4 first-shot head %, 0.4 T_c, 0.2 N_c |
| Movement | 0.15 | 0.4 moving shots, 0.3 counter-strafe use, 0.3 stop time |
| Spray | 0.10 | 0.4 \|V\|, 0.3 H, 0.3 delay |
| Tracking | 0.10 | 0.6 on-target tier, 0.2 τ, 0.2 re-acquire |
| Utility | 0.10 | 0.6 turn-away, 0.4 outcome |
| Reaction | 0.05 | Reaction Test median only |

Confidence per skill is c = n/(n + n_min). Evidence E = Σw·c/Σw.

**How drill tier and guns enter:**
- Tracking: T = (on% − 25)/10 + 2·(tier − 2).
- Flash outcome: T + (tier − 2), capped at tier + 1.
- Head Flicks speed: `Difficulty.FlickBadges[tier]`.
- Spidershot / Gridshot speed: badges − 0.8.
- **Crosshair placement, movement and spray get no normalisation for drill or tier.**
- Guns: runs with an unrated gun lose `spray.*`, `move.*`, `speed.*` and first-hit % (`Normalize`, `WeaponRating.IsRatedRun`).

### 1.1 Every benchmark number and its source

The tables come from the original research spec (`docs/coach_spec.md` B). They were re-anchored in v1.1.1 against one
real profile (the developer's, "peak Gold 2") and the simulated player. † means the spec itself marks the value as an
estimate.

| Table (`Benchmarks.cs`) | b0 … b4 | Source / status |
|---|---|---|
| `OnsetMs` :18 | 265 225 195 172 155 | † spec, re-anchored to drill scale v1.1.1 (targets on a rhythm) |
| `EndErrPct` :20 | 24 15 10 7 5 | † spec; "simulated good player ≈ 5%" |
| `FirstShotHitPct` :21 | 62 70 78 85 90 | † spec |
| `TcMs` :22 | 420 330 260 205 165 | † spec |
| `Nc` (rules) :24 / `NcRating` :26 | 2.4…1.2 / 1.8 1.15 0.8 0.55 0.4 | † spec / re-anchored to measured scale v1.1.1 |
| `TrackOnPct` :27 + `TrackTier` | 25 35 45 55 65 (Veteran) | † spec; tier step = 2 table tiers, from the sim (91/65/48/26% on Rookie…Elite) |
| `ScoreSpeedOffset` :46 | 0.8 | Sim (flick 2.3 vs spider/grid 2.7–3.4) + the real profile (1.3 vs 2.25) |
| `TauMs` :47 | 230 190 160 135 115 | † spec |
| `ReacqMs` :49 | 240 165 120 92 75 | † re-anchored v1.1.1 |
| `Jitter` :50 | 0.9 0.7 0.55 0.45 0.35 | † spec (rules / sens only, not rated) |
| `ReactMs` :55 | 262 230 206 180 162 | Human Benchmark median 273 ms minus an assumed 30–45 ms browser/monitor latency; "pros ≈ 50 ms ahead of novices", WPI. **The 273 ms median and the 219-vs-270 ms pro study could not be verified (§4)** |
| `XhairErrDeg` :60 | 13.5 10.5 8.8 7.0 5.5 | Leetify CS2: "≈ 10.2° average, 7.5° top 3%". **"Top 3%" is unverified.** Leetify measures matches; we measure drills (§2.1) |
| `PitchAbsDeg` :62, cap 2.5 | 3.0 1.6 0.9 0.5 0.3 | † spec |
| `SprayV` / `SprayH` :65–66 | 1.6 1.05 0.7 0.45 0.3 / 1.1 0.8 0.55 0.4 0.28 | † spec; comment: "drill sprays tighter than in-game" |
| `CompDelayMs` :68, cap 2.0 | 450 330 250 190 150 | † spec |
| `MovingShotPct` :70 | 28 10 4 2 1 | † spec |
| `CounterPct` :71 | 10 30 55 75 90 | † spec |
| `StopMs` :76 | 128 116 108 102 98 | VALORANT movement model (plain release ≈ 112–120 ms, counter-strafe ≈ 100 ms) |
| `FlashTurnMs` :77 | 650 520 420 340 280 | † spec |
| `MtRefMs` :79 | 720 600 500 420 360 | † not in spec |
| `FlashDodgePct` :81 + `DodgeTier` | 30 45 60 75 88 | † not in spec |
| `Difficulty.FlickBadges` / `GridshotBadges` / `SpiderBadges` (`src/Core/Difficulty.cs:201-227`) | per tier | No external source in the repo ("rank-calibrated" tiers) |
| `PriorTier` 1.2, percentile anchors | — | Esports Tales rank distribution, V26 A5 (2026-09-08) [S]. Re-verified, see §4 |
| `Validity` 0.9, `Evidence0` 0.1, `OutlierClip` 0.75 | — | Design choice v1.1.1. **No source** |
| `Spread` 0.15 + 0.30(1−E) | — | Design choice. **No source** |

Only three anchors are external: the rank distribution, reaction time and crosshair placement. Everything else is a spec
estimate tuned on **one** real profile and a simulated player. The self-test archetypes (`CoachCli.cs:302-308`) are built
from the same tables, so they check that the overall formula is consistent with itself. They do not test the tables
against real players.

---

## 2. Reproduction and root causes

### 2.1 The developer's data [M]

**Method.** `%APPDATA%\ValTrainer\stats.json` and `telemetry\` were copied to the scratchpad, then:

```
tools\godot-run.ps1 -GodotArgs "--headless","--path","<repo>\ValTrainerGodot","--","--dev","--data-dir","<scratch>\sandbox",
  "--valorant-dir","<scratch>\noval","--coach-analyze","<scratch>\empty","--coach-stats","<scratch>\data\stats.json","--explain"
```

The sandbox data dir stayed empty, and no process other than our own Godot was touched.

**Data.** 187 runs, of which 129 have metrics and 128 are used (Oct 1 – Oct 31). The profile:

| Skill | Tier | Rank | c | Key numbers |
|---|---|---|---|---|
| Flicking | 1.12 | Gold 1 | .90 | 1058 ms to hit (speed tier 1.54), end error 18.9% → 0.56, onset 238 ms → 0.68 |
| Precision | **0.01** | Bronze 1 | .92 | 57% first-shot head → −0.50, T_c 416 ms → 0.05, N_c 1.18 → 0.96 |
| Tracking | 1.21 | Gold 2 | .86 | on-target tier 1.52, τ 206 ms → 0.59, re-acquire 171 ms → 0.91 |
| Reaction | 1.55 | Plat 1 | .67 | 217 ms median |
| Crosshair placement | **2.72** | Asc 1 | .95 | E 7.2° → 2.88, \|ε_p\| 0.76° → 2.34 |
| Spray | **1.89** | Plat 3 | .88 | \|V\| 0.64° → 2.25, H 0.73° → 1.30, pull delay 75 ms → 2.00 (capped) |
| Movement | **2.64** | Asc 1 | .86 | 2.0% moving shots → 3.03, 83% counter-strafed → 3.55, stop 114 ms → 1.23 |
| Utility | 1.59 | Plat 1 | .87 | turn 488 ms → 1.32, outcome tier 2.0 |

`overall: weighted median 1.59, robust mean 1.66, evidence 0.89 → λ 0.81 → 1.58 (Platinum)`. Band ±(0.15 + 0.30·0.11)
= ±0.18 → **Gold 3 – Platinum 2, "most likely Platinum 1"**. That is exactly what the developer sees.

**Contribution to the mean before the shrink** (w·c·(T − 1.2)/Σw·c):

| Skill | Contribution |
|---|---|
| Crosshair | **+0.325** |
| Movement | **+0.210** |
| Spray | +0.068 |
| Utility | +0.038 |
| Reaction | +0.013 |
| Tracking | +0.001 |
| Flicking | −0.013 |
| Precision | −0.185 |
| **Sum** | **+0.458** |

### 2.2 Where the inflating numbers come from (per-drill breakdown of his runs) [M]

**Crosshair placement.** The `xhair` window (newest 300 sightings) is almost all Deathmatch.

| Drill | Median E (first-sight error) | Note |
|---|---|---|
| Site Clear | **12.1°** | → T 0.82, Silver 3 |
| Peek Mix | 19.2° | |
| Flash Peek (Gekko) | 14.7° | |
| Jump Peek | 8.5° | |
| Deathmatch | **7.3°** (per run 3.9–11.8) | → T 2.86, Ascendant |
| Recon (Sova) | 2.7° | angle known in advance |
| Jiggle Peek | 2.5° | angle known in advance |
| Peek Duel | 2.2° | angle known in advance ("a bot holding the angle") |

- So the **same player spans Silver to Radiant depending on the drill**, and nothing normalises for that.
- Decoding the two Deathmatch telemetry files directly: the median/mean ratio of sightings is 0.61–0.71. Sightings over
  60° are never recorded, which truncates the bad tail.
- Leetify's crosshair placement is also a median, but over real match engagements ("first sighting to first damage")
  [P, glossary]. Leetify's CS2 rank averages run 12.4° (5k Premier) → 11.5° (10k) → 10.9° (15k) → 10.3° (20k) → 7.2°
  (25k+) [S]. So **7.2° is a top-few-percent match number**. A Gold 2 reaching it in our Deathmatch means our Deathmatch
  sightings are easier than match sightings. Bots are more predictable, and the player knows the spawn logic.

**Movement.** The `move` window is mostly Counter-Strafe drill runs (98.5% counter-strafed, 0.9% moving shots). That
drill's instructions are literally "tap the opposite key to stop, then one-tap".
- The newest Deathmatch run (Classic/Sheriff, an unrated gun, so dropped) had **32% moving shots**.
- Without the instructed drills, his free-play numbers are 5.0% moving shots, 66.7% counter-strafed, 110 ms stop
  → Movement 2.05 instead of 2.64.

**Spray.** Spray comes from Vandal Spray only: a static target at fixed range.
- The pull-delay component is capped at 2.0, but any early pull reaches the cap.
- Two simulated players with weak or late pull-down settings (`pull=0.75,pulldelay=0.3`) still read **Ascendant spray
  (2.67–2.79)**, because visual feedback on a static target makes |V| tiny (0.23° → 4.49).

### 2.3 Hypotheses checked

| Hypothesis | Verdict | Evidence |
|---|---|---|
| Benchmarks built from pro / high-elo / optimistic sources | **Partly** | The only external crosshair anchor is Leetify (engaged CS2 players). "7.5° = top 3%" is unverified. Most tables are spec estimates tuned on one profile and a sim (§1.1) |
| Drills easier than matches (predictable, no pressure) | **Confirmed, main cause** | §2.2: Deathmatch / Peek Duel crosshair 2–7° vs Site Clear 12°; Counter-Strafe drill 98.5% vs free play 67%; static-target spray maxes out even for a weak sim |
| Reaction / flick measured differently (latency, sens) | Minor | Reaction 217 ms → Plat 1, weight 0.05, contribution +0.013. The in-engine test still includes display and input latency (≈ 17–26 ms mouse-to-photon on fast PCs, TechPowerUp LDAT [P]), so "30–45 ms faster than Human Benchmark" is optimistic. But a slower population would push a 217 ms player *higher*, not lower. Low validity either way |
| Selection bias: best-of runs | **Rejected** for bests | `Agg` uses sample-weighted means of all recent runs |
| Selection bias: drill mix / recency window | **Confirmed (variance)** | The newest drill dominates each family's 300-event window: crosshair = Deathmatch, movement = Counter-Strafe drill. It also cuts the other way: Precision 0.01 comes from Deathmatch / Strafe Bots heads (12–30% first-shot) vs 81–94% in Head Flicks / Microshot |
| Averaging rewards a few strong skills | **Confirmed** | The weighted-median clip lifts weak aim skills when behaviour holds the weight majority. Synthetic Gold aim + Radiant behaviour → 2.79 today |
| Tier scaling (Veteran bots) | Not a cause here | Tracking and flash are normalised. His tracking (tier 1, 60%) → 1.52, plausible |
| "Most likely" picks the top of the range | **Rejected** | It is the centre of a symmetric band (`ProfileScreen.cs:178`) |
| The coach measures only mechanics; rank depends on more | **Confirmed, mis-modelled** | `Validity = 0.9` says aim explains ~90% of the distance from the median. No source supports this (§4: no published r between aim-trainer scores and rank; community consensus is "minimal correlation"). The ±0.15 band implies r ≈ 0.96 (rank SD ≈ 0.83 tier) |
| Practice effect (training to the test) | Likely, unquantified | Players get better at the drills they repeat faster than their rank moves. Nothing in the coach corrects for it |

### 2.4 Why it drifted since v1.1.1

The v1.1.1 notes say the same player then read Gold 2 (1.31). Since then:
- "all drills feed the coach" (bc55f6a) and Deathmatch (v1.18) feed **crosshair placement and movement** from easy or
  instructing drills (Deathmatch, Peek Duel, Jiggle Peek, Recon, Counter-Strafe).
- Most of his crosshair data used to come from Site Clear (12°). Now it comes from Deathmatch (7.3°).

That is why the error came back with more data and "high confidence".

---

## 3. Simulated players [M]

All runs at tier 1, 40 s, `--simaim`, analysed with `--coach-analyze --brief --explain`. The sim cannot move, so there
is no Movement, Site Clear or Deathmatch data, and the overall stays "unrated" (provisional value shown).

| Profile | Flick | Precision | Tracking | Spray | Provisional overall today |
|---|---|---|---|---|---|
| `good` (12 drills) | 2.32 | 3.20 | 2.13 | 2.92 | ≈ 2.2 (Diamond 1) |
| **`mid`** = `good:rt=0.25,gainsd=0.12,dir=0.05,lat=0.11,confirm=0.09,mtb=0.05,jitter=19,kp=4.5,delay=0.145,tremor=4` (7 drills) | 1.04 | 1.16 | 1.12 | **2.79** | 1.23 (Gold 2) |
| `weak` = `good:rt=0.27,gainsd=0.18,dir=0.07,lat=0.13,confirm=0.12,mtb=0.06,jitter=24,kp=4,delay=0.17,tremor=5,pulldelay=0.3,pull=0.75` | 0.20 | −0.45 | −0.10 | **2.67** | 0.44 (Bronze 1) |

- `mid` is a usable **"Gold aim" reference profile**: its three aim skills sit at 1.04–1.16.
- The sims land roughly right *because* they have none of the inflating behaviour skills, except spray, which reads
  Ascendant for every profile.

---

## 4. External calibration data

| Topic | Finding | Source |
|---|---|---|
| Rank distribution | Iron 4.67, Bronze 15.72, Silver 20.66, Gold 22.12 (G1 8.70 / G2 7.58 / G3 5.84), Plat 17.56, Diamond 11.55, Asc 6.32, Imm 1.37, Radiant 0.05. Matches the current anchors | Esports Tales V26 A5, 2026-09-08 [S]: https://www.esportstales.com/valorant/rank-distribution-and-percentage-of-players-by-tier |
| | Valking.gg snapshot 2026-10-18 shows essentially the same (Gold 9.2 / 7.6 / 5.8) | GameRiv 2026-10-20 [S]: https://gameriv.com/valorant-rank-distribution-2022/ |
| | vstats.gg, May 2026: Gold 23.0, Plat 18.8 | PC Gamer [S]: https://www.pcgamer.com/all-valorant-ranks-list/ |
| | → Gold 2 ≈ 53rd percentile; Plat 1 ≈ 63rd–71st. The coach's error is ≈ +10–15 percentile points | [E] |
| Aim trainer → rank correlation | **No published r or R² found** for Aimlabs, KovaaK's or Voltaic scores vs VALORANT or CS rank | — |
| | Aimlabs benchmark article (270k players): tiers only, no game-rank claim | [P] https://aimlabs.com/articles/aimlabs/what-does-your-aimlabs-benchmarks-rank-actually-mean/ |
| | KovaaK's study, n=10: test-retest ICC .95–.99 (reliability, not validity); notes rank "inadequately capture[s] individual skills" | [P] https://pmc.ncbi.nlm.nih.gov/articles/PMC10925653/ |
| | PNAS Nexus 2023, FPS kinematics: marginal R² 0.22 within the task, not vs rank | [P] https://academic.oup.com/pnasnexus/article/2/8/pgad249/7233873 |
| | UP Diliman thesis 2024, n=37 VALORANT players: Human Benchmark reaction time vs rank, "weak positive correlation" | [P] https://digitalarchives.upd.edu.ph/item/61559/981/alkHEkG8Ka2DACddFHdGHk8i |
| | hnA tacFPS aim guide: "essentially minimal correlation between any kovaak scores and rank" | [S] https://docs.google.com/document/d/1WZbVuSqRiDhY77jsVDOOVaylWtHiKc06S0Xvw2QJveo/mobilebasic |
| | → We assume mechanics explain ~20–35% of rank variance (r ≈ 0.45–0.6) | [E] |
| Headshot % by rank (context) | tracker.gg: Iron 12.4 … Gold 21.0, Plat 23.4, Dia 25.4 … Radiant 31.1 | [S] https://tracker.gg/valorant/insights |
| | UpForge medians (n=422): Gold 22.4 with IQR 18.3–27.1, Plat 26.0, Dia 27.7. Ranks overlap heavily | [S] https://upforge.gg/valorant/stats/average-headshot-percentage |
| Crosshair placement (CS2) | Leetify definition: median angle from first sighting to first damage. Glossary publishes no benchmarks | [P] https://leetify.com/blog/leetify-stats-glossary |
| | "Avg player" 10.19° | [S] profile snippets |
| | Rank averages 12.43° (5k) / 11.53° (10k) / 10.87° (15k) / 10.28° (20k) Premier; 7.24° at 25k+; 8.36° at FACEIT 10 | [S], unverified |
| Counter-strafing (CS2) | Leetify "Avg player" counter-strafing 76% (share of rifle shots at < 34% max speed: a different definition from ours) | [S] |
| Reaction time | Human Benchmark 2015 snapshot: median 251 ms, mean 262 ms over 15M clicks; the current median is quoted as 273 ms (unverified) | [S] https://ibmathsresources.com/2015/03/20/reaction-times-how-fast-are-you |
| | WPI (Jiang, Kundu & Claypool 2020): choice RT ≈ 350 ms low-skill vs ≈ 325 ms high-skill | [P] https://web.cs.wpi.edu/~claypool/papers/reaction-time/paper.pdf |
| | LDAT mouse-to-photon 16.8–25.7 ms on a fast PC | [P] https://techpowerup.com/review/nvidia-reflex-review-test-ldat-v2/3.html |
| Rank SD in tier units | 0.83 tier (from the distribution above). With r = 0.5 the residual SD is 0.72 tier, so a 50% interval is ±0.49 tier (≈ ±3 divisions) and an 80% interval ±0.92 | [E] computed |

---

## 5. Proposed change (1.19)

The structural part (5.4–5.6) is source-independent and carries most of the fix. The table and data parts (5.1–5.3)
remove measured drill biases. Their factors are fitted from **one** player plus the sims, so they are marked [E] and get
re-fit when calibration lines come in (5.8).

### 5.1 Crosshair placement: normalise by drill (`RunAnalysis.cs`)

- **Drop the horizontal error for drills that announce the angle.** Add `peekduel`, `jigglepeek` and `recon` to
  `HorizExcluded` (:951). Their sightings then count for the vertical error only, like Peek Practice. Measured: 2.2–2.7°
  for a Gold 2. Confirm each drill's spawn logic before merging; `jumppeek` (8.5°, Operator at a fixed long angle) is a
  candidate too.
- **Add a per-drill difficulty factor k** and store a normalised error next to the raw one:
  - `xhair.err_ref_deg = xhair.err_deg / k(drill)`.
  - k values: Site Clear, Retake, Post-plant, Anchor, Smoke Exec, Flash Peek, Mobility, Chamber = **1.0** (match-like:
    enemies come from several unknown spots); `deathmatch` = **0.70** [E]; anything not listed = 1.0.
  - The developer's Site-Clear-to-Deathmatch ratio is 7.3/12.1 = 0.61. We use 0.70 to stay conservative.
  - Compute it in `Crosshair()` for new runs, and in `Normalize()` for stored runs (they only have `xhair.err_deg`; no
    double application, because `err_ref` is a separate key).
  - Add `["xhair.err_ref_deg"] = "xhair.nh"` to `Agg.Weight`.
  - `Assess.Rate` uses `err_ref` when present. The run review and the problem text keep showing the raw degrees.
- `XhairErrDeg` table: **unchanged** (13.5 10.5 8.8 7.0 5.5). With k it stays on the match scale it was taken from.

Effect for the developer: E_ref = 7.22/0.7 = 10.3° → 1.12. Crosshair placement goes **2.72 → 1.48**.

### 5.2 Movement habits only from drills that don't ask for them (`RunAnalysis.Normalize`)

- New set `InstructedMovement = { "counterstrafe", "strafeduel" }`. Their descriptions literally say "tap the opposite
  key" / "counter-strafe and hit first".
- In `Normalize()`, remove `move.counter`, `move.counter_pct`, `move.moving`, `move.moving_shot_pct`, `move.early`,
  `move.early_shot_pct` and `move.shots` from those drills for the **profile**. Keep `move.stops` / `move.stop_ms`:
  stop time is a mechanic. The run review still sees everything.
- Tables unchanged.

Effect: moving 5.0%, counter 66.7%, stop 110 ms → Movement **2.64 → 2.05**.

### 5.3 Spray (`Benchmarks.cs`)

| Constant | Today | Proposed |
|---|---|---|
| `SprayV` | 1.6 1.05 0.7 0.45 0.3 | **1.28 0.84 0.56 0.36 0.24** (×0.8) [E] |
| `SprayH` | 1.1 0.8 0.55 0.4 0.28 | **0.88 0.64 0.44 0.32 0.22** (×0.8) [E] |
| `CompDelayCap` | 2.0 | **1.5** |

Reasons: a static target at fixed range, and any early pull hits the cap. Every sim profile, including the weak one,
reads Ascendant.

Effect: Spray **1.89 → 1.33**.

### 5.4 A validity for each skill instead of one global `Validity` (`Benchmarks.cs`, `Assess.cs`)

Replace `Validity = 0.9` with how much each skill's **drill measurement** says about rank [E]:

```csharp
public static readonly Dictionary<Skill, float> SkillValidity = new()
{
    [Skill.Flicking] = 0.85f, [Skill.Precision] = 0.85f, [Skill.Tracking] = 0.75f, [Skill.CrosshairPlacement] = 0.65f,
    [Skill.SprayControl] = 0.50f, [Skill.Movement] = 0.45f, [Skill.Utility] = 0.40f, [Skill.Reaction] = 0.40f,
};
public static float ShrinkFactor(float evidence) => evidence <= 0 ? 0 : evidence / (evidence + Evidence0); // no 0.9
```

The new overall:

```
Overall = 1.2 + λ(E) · Σ w·c·v·(T̃ − 1.2) / Σ w·c
```

with λ(E) = E/(E + 0.1). Dividing by Σ w·c, not Σ w·c·v, makes v a per-skill shrink.

- At full evidence the w-weighted mean of v is ≈ 0.6. The effective slope is ≈ 0.55–0.6. That fits r ≈ 0.45–0.6 once
  you allow for the composite's smaller spread [E].
- A more conservative set (aim 0.75, tracking 0.65, crosshair 0.6, spray 0.45, movement 0.4, utility / reaction 0.35)
  gives the developer Gold 2 as well (≈ 1.2) but compresses the archetypes more (Iron → Silver 1, Immortal → Diamond 1).

### 5.5 Robust clip that can't lift weak aim; behaviour can't carry the estimate (`Assess.cs:89-92`)

- **Aim core** = {Flicking, Precision, Tracking, Reaction}. Compute the clip median over the **core** skills when at
  least 2 are rated, else over all skills.
- Behaviour skills (Crosshair, Spray, Movement, Utility) are clipped to at most **core median + 0.5**
  (`Bench.BehaviourLift = 0.5`). There is no extra floor: weak drill behaviour still pulls down, because it is real
  evidence of a habit gap.
- Keep `OutlierClip = 0.75` for the symmetric clip, now around the core median.

Synthetic check, Gold aim (1.0) plus Radiant behaviour (3.5): today **2.79 (Asc 1)** → **1.21 (Gold 2)**.

### 5.6 Range and label (`CoachUi.cs`, `ProfileScreen.cs`, strings)

- `Spread(E)` = **0.35 + 0.25·(1 − E)** (today 0.15 + 0.30·(1 − E)).
  - At full data that is ±2 divisions. It is still narrower than an honest 50% interval at r = 0.5 (±0.49).
  - Going wider would make the headline useless. The label (below) carries the rest of the honesty.
- `ConfidenceCaps` / `ConfidenceLower`: describe **data coverage**, not accuracy. "LOW / SOME / GOOD DATA" or
  "BASED ON LITTLE / SOME / PLENTY OF DATA". Keep the bars.
- Under the range add one dim line: *"Aim only: your rank also depends on game sense, utility and teamwork."* The caption
  "ESTIMATED RANK · AIM ONLY" already exists.
- "most likely X" stays (it is the centre).
- New strings need fr / es / pt-BR / de entries (the v1.19 i18n pipeline, `docs/research/i18n-howto.md`).

### 5.7 Optional "your actual rank" (evaluated: worth it as a gap view)

| Option | Verdict |
|---|---|
| Show the gap between the aim estimate and the entered rank | **Do it.** It turns "too high" into advice. Gap ≥ +0.25: "Your aim is ahead of your rank: decisions, utility and teamplay are likely what holds you back. Review deaths, not drills." Gap ≤ −0.25: "Your rank is ahead of your aim: aim training is your biggest lever. Start with ‹weakest skill›." Otherwise: "Your aim matches your rank." |
| Learn a per-user offset and apply it to the estimate | **Don't.** The estimate would just echo the entered rank, it goes stale when the rank changes, and it hides the gap, which is the useful signal |
| Use the entered rank as the prior instead of Gold 2 | Not in 1.19. It mixes self-report into a mechanics read. Revisit once the tables are fitted |
| Anonymous calibration line | **Do it.** It is the only way to fit v and the drill factors without a server (the app is offline and collects nothing). A "Copy calibration data" button writes one line with no personal data, which the player pastes in Discord (#coach-calibration): `vtcal1 rank=G2 date=2026-11-02 runs=128 E=0.89 T=fl1.12,pr0.01,tr1.21,re1.55,xh2.72,sp1.89,mv2.64,ut1.59 xhsrc=dm:372,sc:24 mvsrc=cs:326,dm:195 raw=1.66 est=1.18 v=1.19.0`. With 30–50 lines, fit v_s and k(drill) by least squares (dev script) |

**Implementation sketch:**
- `AppSettings`: `int ActualRank = -1` (0…24 = Iron 1 … Immortal 3, 25 = Radiant) and `string ActualRankDate = ""`.
- Profile screen: a chip under the ladder, "Your VALORANT rank: Gold 2 · set Nov 2", with a 9×3 picker plus "Unranked /
  don't say".
- A marker on `CoachDraw.Ladder` at the entered rank.
- After 60 days (≈ one act), prompt to update.
- `CoachCli --actual-rank G2` prints the gap and the calibration line.
- Cost: about 1 day including i18n. No privacy impact: it stays local unless the player copies the line.

### 5.8 Self-test and spec updates (`CoachCli.cs`, `docs/coach_spec.md`)

- **Archetype ranges** (`CoachCli.cs:302-308`) for the new formula, using the 5.4 main v set:

  | Archetype | New value | Expected range |
  |---|---|---|
  | Iron | 0.45 | [0.20, 0.65] |
  | Silver | 0.93 | [0.75, 1.05] |
  | Gold | 1.24 | [1.10, 1.40] |
  | Diamond | 1.71 | [1.55, 1.95] |
  | Immortal | 2.21 | [2.00, 2.50] |

  Strictly increasing as before.
- **New regression case "real Gold 2 profile":** the developer's aggregated metrics from §2.1, with
  `xhair.err_ref_deg = 10.3` and movement 5.0 / 66.7 / 110. They are not personal data. Expected overall in **[1.05,
  1.40]**.
- **New case "behaviour can't carry":** core 1.0, behaviour 3.5 → overall ≤ 1.4 (proposal: 1.21).
- **New case "flat Ascendant everywhere":** all skills 2.5 → overall in [1.85, 2.15] (proposal: 1.95). This documents the deliberate
  regression to the mean.
- Spec: update B (tables, composites, overall formula, band) and add "Calibration notes (v1.19)" with this document's
  §2 numbers.

### 5.9 Not in this change (follow-ups)

- **Per-drill normalisation of head first-shot %.** Deathmatch / Strafe Bots 12–30% vs Head Flicks 81–94% currently
  pulls Precision down and adds variance across drill mixes. This is the mirror image of 5.1. Same pattern
  (`head.fsh_ref`), needs data.
- **Strength texts "(Ascendant level)".** They use the per-skill tier. After 5.1–5.3 these are less extreme. Consider
  "in drills" wording.
- `RefTier` (the diagnosis bands and sens advice) follows `Overall`. The lower overall shifts "typical for ‹rank›" bands
  toward Gold. Re-run the diagnosis self-test cases (run-level reviews use the tier played and are unaffected).

---

## 6. Validation plan

**Commands** (one Godot at a time; `tools\godot-run.ps1` takes the shared slot; always `--dev`, a throw-away
`--data-dir`, an empty `--valorant-dir`):

1. `dotnet build ValTrainerGodot\ValTrainer.csproj`
2. Self-test:
   `--coach-analyze <empty> --coach-selftest` → all calibration cases PASS with the new ranges plus the three new cases.
3. Developer's data (on a **copy** of `%APPDATA%\ValTrainer`):
   `--coach-analyze <empty> --coach-stats <copy>\stats.json --explain`.
4. Sim players:
   `--mode <m> --tier 1 --simaim <profile> --duration 40 --telemetry-out <dir>` for flick, gridshot, spider, tracking,
   strafebots, spray_vandal and microshot (plus peekduel, siteclear, deathmatch and flashmap for `good`), then
   `--coach-analyze <dir> --brief --explain`.
5. `tools\selftest.ps1`: the full smoke test still passes (no exceptions, telemetry loads).
6. **Reporter.** Ask the player on Discord for their in-game rank and either their calibration line (once 5.7 ships) or
   a zipped `%APPDATA%\ValTrainer\stats.json`. Run step 3 on it.

**Expected outputs, today → after** [M] for today, [E] for after (computed with a Python port of `Assess` that
reproduces today's values exactly):

| Case | Today | After (whole proposal) |
|---|---|---|
| Developer, 128 runs (true **Gold 2**) | **1.58 Plat 1**, "Gold 3 – Plat 2", HIGH CONFIDENCE | **1.18 Gold 2**, "Silver 2 – Plat 1", "plenty of data · aim only" |
| └ crosshair / movement / spray | 2.72 / 2.64 / 1.89 | 1.48 / 2.05 / 1.33 |
| └ structural only (5.4–5.6, no table or data change) | — | 1.21 Gold 2 |
| └ step by step | — | +5.1 → 1.40, +5.2 → 1.38, +5.3 → 1.31 (still current overall formula), +5.4 (v and λ, today's clip) → 1.23, +5.5 (core-median clip + lift) → 1.18 |
| Archetype Iron / Silver / Gold / Diamond / Immortal | 0.18 / 0.85 / 1.28 / 1.94 / 2.62 | 0.45 / 0.93 / 1.24 / 1.71 / 2.21 |
| Sim `mid` (Gold aim, 7 drills) | provisional 1.23 Gold 2 | ≈ 1.18 Gold 2 (spray capped by the lift rule) |
| Sim `good` | provisional ≈ 2.2 Diamond 1 | ≈ 2.0 Plat 3 / Diamond 1 |
| Sim `weak` | provisional 0.44 Bronze 1 | ≈ 0.47 Bronze 1 |
| Synthetic Gold aim + Radiant behaviour | 2.79 Asc 1 | 1.21 Gold 2 |

**Acceptance:**
- The developer's estimate is within Gold 1 – Gold 3 (1.0–1.5), and his true rank is inside the displayed range.
- Sim `mid` is within Gold.
- The archetypes stay ordered.
- No skill's per-run review changes except crosshair placement (reference error) and spray (tables).

---

## 7. Risks and open questions

- **n = 1 calibration.** The k(Deathmatch) = 0.7 factor and the spray ×0.8 come from one Gold 2 player and the sims.
  The structural changes don't depend on them (1.21 without them). The calibration line (5.7) is how to replace these
  guesses.
- **Compression.** With v ≈ 0.6, typical Immortal mechanics read Diamond 2 and typical Iron mechanics read Bronze 3. That
  is statistically correct for "rank given aim", but strong players may feel underrated. The gap view and the per-skill
  tiers, which stay unshrunk, carry the "your aim is elite" message.
- **The reporter's case is unknown.** If they are in a low rank with strong drill numbers, the proposal lowers their
  estimate. If their problem is Spidershot / Gridshot speed or something else, 5.1–5.3 may not touch it. Ask for data
  (validation step 6).
- **Strings.** New UI strings need translation in 4 languages before release.
