# ValTrainer aim coach: implementation spec

*From the research agent. Sources are listed at the end. Values marked † are estimates to tune on playtest data.*

## Conventions

Each frame records:
- t, yaw ψ and pitch θ (degrees);
- mouse counts dx, dy;
- the offset from the crosshair to the focus target. In ValTrainer telemetry this is FocusYaw/FocusPitch = target − crosshair, with +yaw = target to the right and +pitch = target above; the crosshair error is therefore e = −Focus;
- the target's angular radius r = atan(R/d);
- whether fire is held, and player speed v.

Units and filtering:
- **Hand velocity** comes from the counts, so recoil and camera kick don't contaminate it: ω⃗ = 0.07·S·z·(dx, dy)/Δt, where z is the zoom multiplier. ω = |ω⃗|.
- **Smoothing:** resample to 240 Hz. Low-pass at 7 Hz with zero phase (Butterworth, 5th order) for segmentation only. Use the raw mean of the last 3 frames for "speed at click".
- **Head size on screen:** a 0.14 m head is r = 0.80° at 10 m and 0.40° at 20 m.

## A. Metrics

### A1. Flicks (Head Flicks, Spidershot, Gridshot, first engagement in bot modes)

- **Trial:** from target appear t₀ (in Gridshot and Spidershot, from the previous hit) to the first hit or expiry. Amplitude A = |e(t₀)|. Flick axis u = −e(t₀)/A.
- **Segmentation:**
  - A submovement starts when ω > 8°/s.
  - It ends when ω < 4°/s and at least 80 ms have passed since it started.
  - Split overlapping submovements at a local minimum where ω_min < 0.5·min(neighbouring peaks), or where ω⃗·u changes sign.
  - The **primary** P is the first submovement that covers at least 0.3·A along u.

| Metric | Definition |
|---|---|
| Onset RT | t_start(P) − t₀. Valid range 100–1000 ms; below 100 ms counts as anticipation |
| Primary MT | t_end(P) − t_start(P) |
| Peak | ω_peak; hand speed in cm/s = \|counts\|/DPI·2.54/Δt |
| Signed endpoint s | s = e(t_end P)·u. Positive means past the target, negative means short |
| Perpendicular error | p = \|e − s·u\| |
| Gain | G = (A+s)/A (1.0 = exactly on target) |
| Over/undershoot | Over if s > r; under if s < −r. Overshoot share O = n_over/(n_over+n_under). Mean overshoot = mean(s \| over), in degrees and as % of A |
| Corrections N_c | Number of submovements after P, up to the hit click |
| Micro-adjust time T_c | t_click − t_end(P) |
| Dwell | t_click − t_enter, where t_enter is the first time \|e\| ≤ r with the crosshair staying on target until the click |
| Premature click | A miss fired with ω_click > max(30°/s, 0.25·ω_peak) |
| Snap-shot miss | A miss fired less than 80 ms after t_end(P), with N_c = 0 and \|e\| > r |
| Path efficiency η | A / ∫\|ω\|dt from onset to click |
| Curvature | Max perpendicular deviation / A |
| Shot error | \|e_shot\| and \|e_shot\|/r. Precision = SD of shot error |
| Fitts | ID = log₂(A/2r + 1). Fit MT = a + b·ID per player; report MT at a reference ID of 4.7 bits |

### A2. Tracking (frames with fire held)

ψ̇_T is the target's angular velocity: target yaw = view yaw + FocusYaw, differentiated.

| Metric | Definition |
|---|---|
| On-target % | Σ(fire ∧ \|e\| ≤ r)Δt / Σ(fire)Δt |
| Signed lag ℓ | −eₓ·sign(ψ̇_T), over frames where \|ψ̇_T\| > 1°/s. Positive = behind |
| Time lag τ | The τ in 0–400 ms that maximises corr(ψ̇_c(t), ψ̇_T(t−τ)) |
| Jitter J | RMS of the 4 Hz high-pass of (ψ̇_c − ψ̇_T), divided by mean\|ψ̇_T\| |
| Reacquire | After each target reversal or stop at t_r: first time \|e\| ≤ r for at least 100 ms, minus t_r. Overrun = largest error in the old direction within 300 ms |
| Vertical drift | mean(e_y) |

### A3. Crosshair placement

Comes from the `bot_seen` event:
- A = total placement error in degrees.
- B = crosshair pitch minus head pitch. Positive = too high, negative = too low.

Pitch error ε_p = B (mean, signed; the rating uses the median \|B\|). Horizontal pre-aim error ε_h = sqrt(A² − B²) (not from Peek Practice or Flash Dodge). Also record the % of events with A ≤ the head's angular radius.

### A4. Movement

- **Moving shot:** v > 1.35 m/s at the shot (25% of 5.4 m/s).
- **Stop time:** from the `stop` event (A = ms, B = 1 if counter-strafed). Riot's best is about 160 ms.
- **Counter-strafe use:** counter-strafe stops ÷ all stops.
- **Early-stop shot:** a shot fired less than 104 ms after a stop starts.

### A5. Spray (sprays of at least 10 bullets)

Use the `spray_residual` events: A = vertical, positive = above the target; B = horizontal, positive = right.
- **Vertical residual** V = mean over bullets 4–15. Positive means under-pulled (bullets high).
- **Horizontal residual** H = RMS over bullets 4–15.
- **Compensation delay:** the first moment downward hand speed reaches at least 0.5 × the required rate (about 5.5°/s for the Vandal) and holds for 50 ms, minus the time of the first shot.

### A6. Reaction and flash

- **Reaction:** median of the `reaction` events.
- **Flash:** the `flash` event gives A = grade (0 dodged, 1 partial, 2 flashed, −1 blocked) and B = turn-away ms (−1 = didn't turn).

## B. Rank benchmarks

Values per tier b₀…b₄. Since v1.1.1 the tiers are anchored to the player population (see *Calibration notes*): b₀…b₄
sit at the 5th / 41st / 81st / 98.6th / 99.9th percentile of ranked players, so the median player (Gold 2) is T ≈ 1.2.

| Metric | Iron–Bronze | Silver–Gold | Plat–Dia | Asc–Imm | Radiant |
|---|---|---|---|---|---|
| Flick onset RT (ms, drill targets — partly anticipated) † | 265 | 225 | 195 | 172 | 155 |
| Primary endpoint error \|e_end\|/A † | 24% | 15% | 10% | 7% | 5% |
| First-shot hit % (head) † | 62 | 70 | 78 | 85 | 90 |
| T_c (ms) † | 420 | 330 | 260 | 205 | 165 |
| N_c — rules / sens advice band † | 2.4 | 2.0 | 1.7 | 1.4 | 1.2 |
| N_c — Precision rating (measured scale) † | 1.8 | 1.15 | 0.8 | 0.55 | 0.4 |
| Tracking on-target % (Veteran-tier target) † | 25 | 35 | 45 | 55 | 65 |
| τ lag (ms) † | 230 | 190 | 160 | 135 | 115 |
| Reacquire (ms, measured scale) † | 240 | 165 | 120 | 92 | 75 |
| Jitter J † | 0.9 | 0.7 | 0.55 | 0.45 | 0.35 |
| Reaction test (ms, in-engine) | 262 | 230 | 206 | 180 | 162 |
| Crosshair placement error E (°) | 13.5 | 10.5 | 8.8 | 7.0 | 5.5 |
| \|Vertical error\| \|ε_p\| (°, component capped at T 2.5) † | 3.0 | 1.6 | 0.9 | 0.5 | 0.3 |
| Spray \|V\| (°, drill) † | 1.6 | 1.05 | 0.7 | 0.45 | 0.3 |
| Spray H (°, drill) † | 1.1 | 0.8 | 0.55 | 0.4 | 0.28 |
| Compensation delay (ms, component capped at T 2.0) † | 450 | 330 | 250 | 190 | 150 |
| Moving shots % † | 28 | 10 | 4 | 2 | 1 |
| Counter-strafe use % † | 10 | 30 | 55 | 75 | 90 |
| Stop time, key release → accurate (ms) † | 128 | 116 | 108 | 102 | 98 |
| Flash turn-away (ms) † | 650 | 520 | 420 | 340 | 280 |

Head Flicks time-to-hit uses `Difficulty.FlickBadges[tier played]`. In the profile, Spidershot / Gridshot speed tiers
(from their badges) are lowered by 0.8 (`Bench.ScoreSpeedOffset`): for the same player those badges sit ≈ 0.8 tier
above Head Flicks' at every difficulty.

**Difficulty normalisation.** Tracking: T = (on% − 25)/10 + 2·(tier played − 2) (the same simulated player scores
91 / 65 / 48 / 26% on Rookie / Regular / Veteran / Elite). Flash outcome: T(outcome) + (tier − 2), at most tier + 1.
Flash Dodge sightings count for the vertical crosshair error only (you are told to turn away). Older stored runs are
brought up to date when the profile is built (`RunAnalysis.Normalize`).

### Scoring

**Metric → tier score.** Interpolate piecewise-linearly between tier medians: T(x) = i + (x − bᵢ)/(bᵢ₊₁ − bᵢ). Extrapolate up to 0.5 beyond either end and clamp to [−0.5, 4.5].

**Data window.** Use the last 300 events per metric, weighted with a 14-day half-life.

**Skill composites:**

| Skill | Weights |
|---|---|
| Flicking | 0.55 time-to-hit (or MT_ref), 0.3 endpoint error, 0.15 onset RT |
| Precision (micro-adjustment) | 0.4 first-shot hit, 0.4 T_c, 0.2 N_c |
| Tracking | 0.6 on-target, 0.2 τ, 0.2 reacquire |
| Reaction | Reaction test only |
| CrosshairPlacement | 0.7 E, 0.3 \|ε_p\| (confidence halved when only ε_p is known) |
| SprayControl | 0.4 V, 0.3 H, 0.3 delay |
| Movement | 0.4 moving shots, 0.3 counter-strafe use, 0.3 stop time |
| Utility | 0.6 turn-away, 0.4 grade |

**Minimum samples (n_min):**

| Skill | n_min |
|---|---|
| Flick | 30 flicks with A ≥ 5° |
| Precision | 30 flicks |
| Tracking | 60 s of firing and 10 reversals |
| Reaction | 10 trials |
| Crosshair placement | 15 first-seen events |
| Spray | 8 sprays |
| Movement | 20 stops / 30 shots |
| Flash | 8 flashes |

**Confidence:** c = n/(n + n_min). Labels: Low < 0.5, Medium < 0.75, High otherwise.

**Overall rank:** M = Σ w·c·T̃ / Σ w·c, where T̃ is each skill's tier clipped to ±0.75 around the (w·c-weighted)
median skill, so one standout skill can't carry it. Evidence E = Σ w·c / Σ w. Overall = 1.2 + λ·(M − 1.2) with
λ = 0.9·E/(E + 0.1): shrunk toward the median player (Gold 2) because aim mechanics only partly predict rank.
Require at least 4 skills with c ≥ 0.5. The UI shows a symmetric band of ±(0.15 + 0.30·(1 − E)) tiers and labels the
estimate "aim only". Weights:

| Skill | Weight |
|---|---|
| Crosshair placement | 0.20 |
| Flicking | 0.15 |
| Precision | 0.15 |
| Movement | 0.15 |
| Spray | 0.10 |
| Tracking | 0.10 |
| Utility | 0.10 |
| Reaction | 0.05 |

**Rank names from T:** Iron < 0 ≤ Bronze < 0.5 ≤ Silver < 1 ≤ Gold < 1.5 ≤ Platinum < 2 ≤ Diamond < 2.5 ≤ Ascendant < 3 ≤ Immortal < 3.5 ≤ Radiant.

### Calibration notes (v1.1.1)

The first release rated a peak-Gold 2 player "Platinum 3 – Diamond" (T 2.09). Causes: metrics recorded on a different
scale than the table assumed pinned at Radiant (onset, N_c, re-acquire, stop time, compensation delay, mean signed
pitch error); Spidershot / Gridshot badges are more generous than Head Flicks'; tracking and flash outcomes were barely
normalised for the (easier) tier played; Flash Dodge's turn-away sightings counted as crosshair placement; the overall
was a plain weighted mean with a narrow ±0.06–0.31 band. The same data now gives Gold 2 (T 1.31, band Gold 1 – Plat 1).

Anchors used:

- Rank distribution (Sep 2026, esportstales.com): Iron 4.7%, Bronze 15.7%, Silver 20.7%, Gold 22.1%, Plat 17.6%,
  Diamond 11.6%, Ascendant 6.3%, Immortal 1.4%, Radiant 0.05% → median ≈ Gold 2 (T 1.2).
- Reaction: Human Benchmark median 273 ms incl. display/input latency (in-engine ≈ 30–45 ms less); lab studies put pro
  FPS players only ≈ 50 ms ahead of novices (≈ 219 vs 270 ms) and high- vs low-skill gamers ≈ 25 ms apart (WPI).
- Crosshair placement: Leetify (CS2) ≈ 10.2° average, 7.5° ≈ top 3%.
- Headshot % by rank (tracker.gg, for context): Iron 12% … Gold 21% … Immortal 29%, Radiant 31% — mechanics separate
  ranks gradually, not in steps.
- Typical-mechanics archetypes (CoachCli self-test "calibration"): Iron → Bronze 1, Silver → Silver 2, Gold → Gold 2,
  Diamond → Platinum 3, Immortal → Ascendant 2 (the shrinkage toward the median is deliberate).

## C. Diagnoses

Each rule is listed as **warning / severe** thresholds, with the minimum sample count in brackets.
- **Severity:** the threshold level, raised one step if that skill's tier is at least 1 below the overall tier. If c < 0.5, show the item as "possible".
- **Sorting:** by severity × skill weight × (overall − skill tier).

| ID · Title | Trigger | Evidence template | Cause → fix · drills | Sens |
|---|---|---|---|---|
| OVERFLICK · Over-flicking: your flicks travel too far | O ≥ .55 and mean overshoot ≥ max(1.5r, 4%A) / O ≥ .70 (30) | "{O}% of your off-target flicks ended past the target, by {d}° ({p}% of the distance)" | The flick is planned too long or the arm is tense. Undoing an overshoot costs more than finishing a short flick. → Land about 90% of the way and finish with a micro-adjust; brake early. Head Flicks one tier lower, Spidershot | If N_c or J is also high: −5 to −10% |
| UNDERFLICK · Stopping short and dragging | G < .85 and under share ≥ .85 / G < .75 (30) | "Your flicks covered {G}% of the distance; {x}% stopped {d}° short" | Hesitation, or arm-limited on wide angles. → Commit to one full motion. Spidershot at a wide tier | If undershoot only shows on A ≥ 25°: +5 to +10% |
| NO_MICRO · Flicks have no micro-adjustment | snap-shot misses ≥ 40% / ≥ 60% of misses (20 misses) | "{x}% of misses were fired straight off the flick, {e}° from the head, with no correction" | The click is chained to the flick. → "Flick, confirm, click." Head Flicks Rookie (untimed) to 85% accuracy, then speed up | — |
| SLOW_MICRO · Slow, fragmented micro-adjustments | T_c > 1.3 × band or N_c ≥ 2.5 / N_c ≥ 3 (30) | "After the flick you needed {T} ms and {N} corrections to settle" | Many tiny corrections. → Make one deliberate correction with fingers and wrist. Gridshot, Head Flicks | Many small corrections (< 2r): −5%. One long slow drag: +5% |
| PREMATURE · Clicking while the crosshair is still flying | ≥ 25% / ≥ 40% of misses (20) | "{x}% of misses fired at {ω}°/s, before you stopped" | Rhythm-clicking. → Stop, then shoot; slow down about 10%. Head Flicks | — |
| HESITATE · On the head but waiting to click | median dwell > 150 / > 250 ms (30) | "You sat on the target {d} ms before firing" | Over-checking. → Fire as soon as you're on. Gridshot | — |
| CURVED · Curved / under-the-head flicks | η < .85, or mean e_y(end) < −r / η < .75 (30) | "Flicks wasted {x}% of their path; {y}% ended {d}° below the head" | Wrist arcs, mouse pad at an angle. → Straighten the arm, align the pad. Spidershot | — |
| SLOW_START · Slow to start moving | onset RT − reaction-test median > 120 / > 180 ms | "You start moving {Δ} ms later than your raw reaction time" | Searching for the target or deciding, not the hands. → Keep eyes centred and use peripheral vision. Reaction Test, Gridshot | — |
| SLOW_REACT · Slow reaction | median > band + 30 ms or > 300 ms (10) | "Median {x} ms (typical for {tier}: {b})" | Fatigue or input lag. → V-Sync off, check FPS cap, warm up | — |
| TRACK_BEHIND · Chasing the target | ℓ > 0.6r or τ > 200 ms / ℓ > r (60 s) | "You trailed by {ℓ}° ({τ} ms)" | Reacting instead of matching speed. → Match velocity, ride the leading edge. Strafe Tracking one tier lower | Smooth but behind: +5% |
| TRACK_OVERRUN · Overshooting strafe reversals | ℓ < −0.4r or overrun > 1.5r / > 2.5r (10 reversals) | "When they changed direction you ran {d}° past" | Anticipating. → React to the stop; watch the body. Strafe Tracking at Veteran+ | With jitter: −5% |
| JITTER · Shaky tracking | J > 1.3× / 1.6× band (60 s) | "Your aim speed wobbled {J}× the target's motion" | Tight grip, fingertip-only aiming. → Relax, use arm and wrist. Strafe Tracking | −5 to −10% |
| SLOW_REACQ · Slow to re-acquire | > 1.3× band (10 reversals) | "{T} ms to get back on after a direction change" | Strafe Bots, Strafe Tracking | — |
| XHAIR_LOW · Crosshair too low | mean ε_p < −1.5°, or ≥ 60% of events below the head / < −3° (15) | "Enemies appeared {d}° above your crosshair ({x}% of peeks)" | Looking at the floor or body height. → Hold a head-height line and move along it. Peek Practice, Site Clear | — |
| XHAIR_WIDE · Not pre-aiming the angle | median ε_h > band / > 2× band (15) | "Enemies appeared {d}° to the side of your crosshair" | Crosshair parked in open space. → Sit just off the cover edge where they'll appear. Peek Practice, Peek Duels | — |
| RUN_GUN · Shooting while moving | moving shots ≥ 15% or early-stop shots ≥ 25% / ≥ 30% (30 shots) | "{x}% of your shots were fired at {v} m/s" | → Stop, then shoot. Counter-Strafe, Peek Duels | — |
| CLUMSY_MOVE · Clumsy stops | counter-strafe use < 40% or stop > 200 ms / < 20% or > 240 ms (20 stops) | "Only {x}% of stops were counter-strafed; average stop {t} ms (best ≈ 160)" | → Tap the opposite key about 80 ms, then release both. Counter-Strafe | — |
| SPRAY_VERT · Spray climbs / over-pulled | \|V\| > 1.0° or delay > 300 ms / \|V\| > 1.8° (8) | "Bullets 4–15 landed {V}° {high/low}; you started pulling {d} ms in" | → Start pulling by bullet 2–3, steady. Vandal/Phantom Spray | — |
| SPRAY_HORIZ · Spray drifts sideways | H > 0.9° / > 1.4° (8) | "Late bullets scattered {H}° left-right" | Not countering the yaw drift from bullet 8 on. → Spray Transfer | — |
| FLASH_SLOW · Late flash turns | > band + 100 ms (8) | "You turned away {t} ms after the cue" | → Turn on the audio cue. Flash Dodge | — |

## D. Sensitivity recommendation

**Conversions.** eDPI E = S·DPI. cm/360 = 13063/E. Valorant sens = 13063/(cm360·DPI).

**Priors:**
- **Pros** (prosettings.net, 700 pros, May 2026): median eDPI 240 (≈ 54 cm/360), mean 267. Typical range 150–400 eDPI (≈ 87–33 cm/360).
- **Lab study** (Boudaoud et al. 2022): completion time, throughput and variance were best in a 20–80 cm/360 band. Submovement count rose with sensitivity.

**Signals.** H > 0 means "sens too high". Each signal is clipped to [−1, 1] and only used when its metric has c ≥ 0.5.

| Signal | Formula | Applies when |
|---|---|---|
| H₁ | (O − 0.40)/0.25 | Always |
| H₂ | (N_c − band)/0.8 | Median correction amplitude < 2r |
| H₃ | (J − band)/band | Always |
| H₄ | −(G_small − G_large − 0.05)/0.10 | G_large (A ≥ 25°) < 0.90 |
| H₅ | −(τ − band)/80 ms | J ≤ band |

H = 0.30H₁ + 0.20H₂ + 0.20H₃ + 0.20H₄ + 0.10H₅

Δ = −0.18 · H · c̄, where c̄ is the mean confidence. S′ = S·e^Δ, so one recommendation never moves more than ±18%.

**Guardrails:**
- If |Δ| < 0.05, say "Keep your sens".
- Require the same sign of Δ in at least 2 of the last 3 sessions.
- If the current sens is outside 20–80 cm/360, add a 0.15 log-step toward the range unless the data strongly disagrees (c̄ ≥ 0.75).
- Never recommend outside 20–80 cm/360 unless |H| ≥ 0.6 and c̄ ≥ 0.8. Hard bounds are 15–100 cm/360.
- After a change, wait at least 5 sessions before re-evaluating.

**Output:** Valorant sens (3 decimals), eDPI, cm/360, pro percentile, the top two contributing signals in plain words, and the confidence label.

## E. Sens Finder mode

This is a blind, performance-scored version of the PSA method that bisects in log space.

### Search

- **Start:** C₁ = current S. If that is outside 20–80 cm/360, start at the nearest bound.
- **Steps:** 5 steps with half-widths h = ln of 1.40, 1.25, 1.15, 1.09 and 1.05.
- **Each step** compares L = C·e^(−h) against H = C·e^(+h).

### Round composition

- **Order:** 4 blocks per step, ABBA or BAAB at random.
- **Blinding:** the player only sees "Setting A / B".
- **Block (40 s):** 12 flicks + 10 s of strafe tracking, with a 5 s rest between blocks. A step takes about 3 min.
- **Flick set:** 12 flicks per step:
  - 4 micro (3–8°), 4 medium (8–20°), 4 large (20–45°), with mixed yaw/pitch.
  - Head size r = 0.5° at 16 m. Timeout 2.0 s.
  - Both conditions get the same set, mirrored and shuffled.
- **Tracking:** the same seeded Veteran-tier strafe script in both conditions.
- **Unscored warm-up:** the first 2 flicks and first 2 s of tracking after each switch.

### Scoring

- **Flick:** z = ln(ID / MT′), with ID = log₂(A/2r + 1) and MT′ = t_hit − t_onset + 0.15 s per miss (a timeout counts as 2.0 s).
- **Tracking:** logit(on-target %), clamped to [.02, .98].
- **Block score:** standardise both within the step, then 0.65·z_flick + 0.35·z_track.

### Decision

- d = mean(L) − mean(H), with SE from Welch's test on trial-level scores.
- d/SE > 1 → L wins. d/SE < −1 → H wins. Otherwise it's a tie.
- If one side wins, move C half a step toward it: C ← C·e^(∓h/2). On a tie, C stays.

### Stopping and confirmation

- **Stop** after 5 steps, or after 3 ties in a row. Abort if accuracy drops below 50% (fatigue).
- **Confirmation:** test the final C against the current S, 2 blind blocks each. Adopt C if d ≥ −0.5 SE; otherwise recommend the geometric mean of C and S.
- **Confidence:** mark it low if the winning direction flipped 3 or more times. Recommend a second run on another day.

### Presenting the result

- Show "Set VALORANT Sensitivity to 0.xxx (was 0.yyy)", with eDPI, cm/360 and the pro median.
- A "Use in ValTrainer" button sets AppSettings.UseSensOverride = true and SensOverride = S′.

## Sources

- Boudaoud, Spjut, Kim 2022: https://arxiv.org/abs/2203.12050
- Meyer et al. 1988, optimized submovement model
- Wavelet-inspired submovement detection: https://arxiv.org/abs/2604.20673
- Undershoot bias in aiming: https://pmc.ncbi.nlm.nih.gov/articles/PMC7080690/
- PNAS Nexus FPS kinematics: https://pmc.ncbi.nlm.nih.gov/articles/PMC10411933/
- Donovan et al. 2022: https://www.frontiersin.org/journals/human-neuroscience/articles/10.3389/fnhum.2022.979293/full
- KovaaK's study: https://pmc.ncbi.nlm.nih.gov/articles/PMC10925653/
- Aim Lab metrics; Aimlabs Sensitivity Finder
- prosettings.net Valorant; PSA method; Human Benchmark
