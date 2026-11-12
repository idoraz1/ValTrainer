# Progression: research and design (v1.19)

Status: design only. No game source was changed. The only file written is this document.

Goal: a progression system that gets players training more often and coming back. It unlocks the gun skins and their
upgrade levels, which another agent is designing: lines with levels (base, animated VFX, sounds, kill effects,
finishers, variants) in editions from Select up to Ultra.

The developer chose a **mix**:
- **Time:** XP from training runs raises the account level, so grinders always progress.
- **Improvement:** the top skins need real improvement (coach rank-ups, real personal bests, badge rank-ups).

---

## 0. Summary of decisions

- **Two tracks, one screen.**
  - **XP / account level** comes from active training time. It unlocks Select, Deluxe and Premium skins and most
    upgrade levels.
  - **Stars (★, "proof")** come only from improvement: real PBs, badge rank-ups, confirmed coach rank-ups and the
    weekly "beat your baseline" challenge. Stars gate the Exclusive and Ultra lines and every kill effect and finisher.
  - Nothing that affects training is ever locked. Skins are cosmetic only.
- **XP per run** = `100 × active minutes (cap 5) × tier factor (1.0–1.4) × variety factor (1 → 0.5) × engagement`.
  - Above 2,500 run XP in one day, run XP is halved.
  - Bonuses: first run of the day +150, rested XP (+100 % until a pool built up while away runs out), PB +50,
    badge rank-up +100, Lock-In completed +150 to +400, challenges.
  - Idle runs get 0 XP. Spamming the easiest drill decays to half XP.
- **Level curve:** level L→L+1 costs `min(500 + 100·(L−1), 2500)` XP, i.e. 2,500 per level from level 21 on.
  - A first session (about 10 runs) reaches level 3, which is 3 Select skins.
  - A regular player (about 15 runs a day, 4 days a week) gets the first Premium (level 12) in about 9–10 days.
  - Ultra needs level 30, 25★ and a confirmed coach rank-up (or 3 confirmed skill rank-ups): about 6–10 weeks of
    improving.
- **The coach rank counts only when confirmed.** The rank used is the median of the last 5 daily estimates with HIGH
  confidence, with a 0.10-tier margin. The first confirmed rank (3 days) is a placement, not a rank-up.
  - On the developer's real history, this filters a 2-day overall "Diamond" spike and Precision swinging
    0.01 ↔ 2.50 day to day (§1.4).
  - Each estimate is tagged with the coach's calibration version. A recalibration re-places silently and never takes
    anything back.
- **Challenges:** 3 daily (about 100 XP each, +100 for all three, 1 reroll a day) and 3 weekly (500 XP each).
  - One daily is always the coach's focus drill. Targets are relative to the player's own baseline, so they suit
    every skill level and Rookie farming doesn't help.
- **Streak:** a *weekly* training streak (a week counts with 2+ training days). A freeze token is earned every 4
  weeks, up to 2 are held, and they apply automatically. There is no daily streak and no "streak lost" message.
- **Retroactive credit:** on the first 1.19 launch the whole `stats.json` and `warmups.json` history is replayed
  through the same rules, followed by a skippable reveal ("Your 187 runs count").
  - Measured on the developer's copy: **level 19 (26.0k XP, 600 short of level 20), 12★, 14 skin lines (22 items
    including upgrades)**.
  - Coach rank-ups are not retroactive (the coach is being recalibrated). The current estimate becomes the
    placement.
- **Storage:** a new `progress.json`, versioned, with `[JsonExtensionData]` round-tripping and
  newer-version-read-only. It is mostly rebuildable from stats, so a corrupt file is re-derived.
  - Dev runs never write it (`Main.SavesData`), the same rule as `stats.json`.
- **Interface for the skins agents:** item ids such as `"<line>"` and `"<line>/vfx"`.
  - `Progress.IsUnlocked(itemId)`, `Progress.Status(itemId)` (requirement, progress, text) and
    `Progress.Unlocked` / `Changed` events.
  - The requirement table lives in Progress, with edition defaults, so pacing is tuned in one place (§9).

---

## 1. What the game already records (code map + the developer's real data)

### 1.1 Per run: `stats.json` (`src/Core/Stats.cs`)
| Data | Where | Use for progression |
|---|---|---|
| `RunRecord`: `Mode`, `When`, `Score`, `Accuracy`, `Hits/Shots/Kills`, `AvgKillMs`, `HeadshotPct`, `Sens`, `Tier` (−1 = old build), `Badge` (−1 = below Rookie / unrated), `Map`, `Weapon` (null = before 1.18 / default gun), `Metrics` (coach), `TelemetryFile`, `Warmup` | `Stats.cs:5-29` | XP inputs (tier, mode, warm-up flag), engagement check (shots/kills/score), PB and badge detection, retro credit |
| Run length: **not a field**. `Metrics["run.secs"]` (frame span, `RunAnalysis.cs:44`) on runs with coach metrics; otherwise `TrainingMode.Duration` (60 s default, `TrainingMode.cs:30`; DM `DeathmatchMode.cs:58`, maps 120 s, tactical 240 s, defense 180 s). `WarmupScore.SecsOf` already does this fallback (`WarmupData.cs:205-210`) | | retro time XP |
| `Best(mode, tier)`, `Best(mode,tier,gun,default)`, `BestBadge(mode)`, rated `BestBadge(m)` | `Stats.cs:73-106` | PB / badge rank-up detection (per drill + tier + gun; badges only on rated guns, `GunRating`) |
| Load: corrupt → `.corrupt` + fresh; unreadable → `readFailed`, never overwritten | `Stats.cs:38-69` | the pattern `progress.json` copies |
| Run is recorded in `GameSession.Finish()`: `PrevBest`, `Record = Mode.MakeRecord`, `Telemetry.Duration = Now` (l.834), coach metrics + review, adaptive Lock-In steps get `adapt.*` metrics and are **not** stored (l.867-872), `KeepInStats` false (sens-shifted) not stored, `OnFinished` = routine step | `GameSession.cs:820-875` | **the run hook** (§10) |
| In-memory telemetry at run end: per-frame `Dx, Dy` (raw mouse counts), `Flags` (Firing, Alive …), `Shots`, `Events` | `Game/Telemetry.cs:9-24, 50-70` | **active seconds** (anti-idle) computed once at Finish |
| Demo / free play never reach the record (`GameSession.cs:822-823`); dev runs never write (`Main.SavesData`, `Main.cs:38`) | | dev/automation never writes progression |

### 1.2 Badges and tiers
- There are 5 difficulty tiers: Rookie, Regular, Veteran, Elite, Pro (`Core/Difficulty.cs:29-40`). Badges are the
  same 5 indices, plus −1 for Below Rookie.
  - Badge rules: `BadgeHigher/BadgeLower` thresholds per played tier (`Difficulty.cs:184-198`).
  - Duels use `BadgeWinRate`: a win rate of 70 % or more gives the played tier +1 (`Difficulty.cs:286-292`).
  - About 15 drills use the win-rate rule (bot, map, deathmatch, agent drills).
- A run with an unrated gun gets a score and a PB, but no badge (`Stats.cs:97-103`).

### 1.3 Aim Coach (`src/Analysis`, `src/UI/CoachUi.cs`)
- `SkillProfile` holds `OverallTier` (continuous: 0 ≈ Iron/Bronze … 4 ≈ Radiant; −1 when fewer than 4 skills are
  confident), `OverallConfidence`, per-skill `SkillRating(Skill, Tier, Confidence, Samples)` for 8 skills, and
  `Problems`, a list of `Diagnosis(Id, Skill, Severity, …, Drills[])` (`Coach.cs:7-48`).
- The overall tier is a robust weighted mean, shrunk toward a prior, and only set with 4 or more confident skills
  (`Assess.cs:78-96`).
- The aggregation window is the newest runs that add up to 300 events per family, with a 14-day half-life
  (`Agg.cs:3-10`). This is why the estimate moves noticeably from day to day.
- Display bands: `CoachRank.Index(t)` gives 9 ranks, each 0.5 tier wide (`CoachUi.cs:43`). `Bench.RankNames` are at
  `Benchmarks.cs:148`.
- **No rank history is stored anywhere.** The profile is rebuilt on a worker by `CoachData.Request()`, keyed on the
  run count, sens, DPI and language (`CoachUi.cs:198-245`). Progression must therefore store its own daily samples.
- `CoachData.Starter` (`CoachUi.cs:283-290`) lists the drills that fill an empty profile. This is the source for
  daily challenges before there is coach data.
- `--coach-analyze <dir> --coach-stats <stats.json>` rebuilds a profile from a copied stats file
  (`CoachCli.cs:78-120`). This is what was used for §1.4.

### 1.4 Lock-In / warm-ups: `warmups.json` (`src/Warmup/WarmupData.cs`)
- `WarmupRecord` (`WarmupData.cs:40-70`) holds `When`, `Preset` (quick/standard/pro/training/agent), `Tier`,
  `Completed`, `PlaySeconds`, `Steps` (each with `Mode`, `Seconds`, `Skipped`, `Adaptive`, `Index`), `Readiness` and
  check-in data.
- The store keeps at most 200 records (`WarmupData.cs:151`). It is written in `WarmupRunner.Complete`
  (`WarmupRunner.cs:313-333`), **the Lock-In hook**.
- Adaptive steps are not in `stats.json`. Their time is only in `Steps[].Seconds`.
- Steps played at the player's own sens are in stats with `Warmup = true`. Retro must count those once: as stats
  runs, not again from warmups.
- **There is no streak anywhere** in the codebase today; the only "streak" is the kill-streak banner.
- `WarmupScore.Baseline(mode, tier, now, gun)` (`WarmupData.cs:219-232`) gives the player's 7-day mean per drill,
  tier and gun, and `IndexOf` gives a % of baseline. This is ready-made for "beat your baseline" challenges.

### 1.5 The developer's real data (copied read-only to a scratch folder, `%APPDATA%\ValTrainer` untouched)

**Overall:**

| Measure | Value |
|---|---|
| Runs in stats.json | **187** (Oct 1 to Oct 31 2026; later play was dev runs, which aren't saved) |
| Active days / sessions | 12 days, 23 sessions (a session ends after a 30-minute gap) |
| Runs per active day | 33, 61, 18, 25, 14, 7, 5, 7, 4, 11, 1, 1. The first 5 days had 151 runs |
| Runs per session | median ≈ 6 (range 1–27) |
| Play time | ≈ 212 min (128 runs with `run.secs`: 149 min; the rest at their drills' default length) |
| Drills played | 26 distinct keys. Top: tracking 24, strafebots 22, flick 16, gridshot 15, counterstrafe 14, spider 13 |
| Tiers | Regular 110, Veteran 30, Rookie 8, Pro 3, old builds (−1) 36 |
| Badges | Below Rookie/unrated 67, Rookie 14, Regular 46, Veteran 54, Elite 5, Pro 1 |
| Warm-up runs (in stats) | 71 |
| Lock-Ins (warmups.json) | 9 (8 completed) on 9 of the 12 days, 5.5–17 min each |

**Improvement events:**

| Measure | Value |
|---|---|
| First plays of a (drill, tier, gun) | 60 |
| PBs (beating a previous best) | **34**, of which **32 were in the first 4 days**. Only 2 PBs in the last 50 runs |
| "Real" PBs (≥ 3 earlier runs there, ≥ 2 % better, max 1 per drill per day) | **7** |
| Badge rank-ups (beating an earlier earned badge in the same drill) | **5** in the month (tracking, flashmap, jumppeek, spray_vandal, counterstrafe) |

Coach rank history, rebuilt by running `--coach-analyze --coach-stats` on the history cut off at the end of each
active day:

| Day | Overall | Flicking | Precision | Tracking | Crosshair | Movement |
|---|---|---|---|---|---|---|
| 10-02 | 1.58 Plat | 1.49 | 0.75 | 1.74 | 2.44 | 1.79 |
| 10-03 | 1.79 Plat | 0.99 | **2.11** | 2.03 | 2.84 | 1.89 |
| 10-04 | 1.64 Plat | 1.21 | 0.94 | 1.66 | 3.50 | 2.43 |
| 10-05 | 1.68 Plat | 1.68 | 0.80 | 1.19 | 3.38 | 2.52 |
| 10-07 | 1.65 Plat | 1.61 | 0.59 | 1.29 | 3.16 | 2.42 |
| 10-09 | 1.63 Plat | 1.53 | 0.53 | 1.38 | 3.52 | 2.40 |
| 10-11 | **2.11 Diamond** | 1.57 | **2.50** | 1.09 | 3.56 | 2.54 |
| 10-13 | **2.09 Diamond** | 1.53 | **2.50** | 1.21 | 3.56 | 2.48 |
| 10-15 | 1.60 Plat | 1.36 | **0.01** | 1.21 | 3.46 | 2.48 |
| 10-31 | 1.58 Plat | 1.12 | 0.01 | 1.21 | 2.72 | 2.64 |

What the data shows:
- **The raw estimate is not stable enough to reward directly.** A "reach Diamond" unlock would have fired on 10-11
  and then vanished. Precision jumps by 2.5 tiers between days.
  - A **5-day median with a 0.10 margin** places the developer at Platinum overall and never confirms Diamond.
  - It confirms two plausible skill rank-ups: Crosshair Ascendant → Immortal (sustained 3.2–3.6), and Movement
    Platinum → Diamond (sustained 2.4–2.6).
- **PBs come in a burst early, then dry up.** Raw PB counting would hand out nearly all improvement rewards in the
  first 2 days. Filtering for "real PBs" plus daily caps spreads them out.
- **Badge rank-ups are rare:** about 5 a month for an active player. They are a good unit for stars.
- **Play is front-loaded:** 61 runs on day 2, then 4–11 runs a day. A daily taper and rested XP keep the binge day
  from finishing the track, and welcome the player back after gaps (Oct 16 → Oct 31).

---

## 2. Research: what works, what backfires

Sources are numbered in §14. The findings that drive the design:

1. **Give XP for every run; make challenges a bonus.**
   - Halo Infinite launched with challenge-only progression and "no per-match XP". Players chased challenges instead
     of playing, and winning was worth nothing [11]. 343 added 50 XP per match, then a tapering daily XP
     (300/200/200/100/100/100, then 50, against 1,000 per level) [12]. In Winter 2022 they added match XP and made
     challenges general bonuses [13].
   - In our design, run XP is the base and challenges are extra.
2. **Taper the day; catch up after absence.**
   - Halo's tapering first-matches bonus [12].
   - VALORANT's 2023 daily checkpoints: a missed day becomes a ×2 checkpoint the next day [5].
   - WoW's rested XP: 5 % of a level per 8 h away, up to 150 %, doubles kill XP [17].
   - We combine these: a first-run-of-day bonus, a taper over 2,500 run XP a day, and a rested pool.
3. **Keep warm-ups low-pressure.**
   - VALORANT excludes Deathmatch from checkpoints so it stays a free warm-up you can leave [5].
   - Our Lock-In (the pre-ranked warm-up) earns XP, but its steps don't count toward score-target challenges, and
     toasts are held until its summary.
4. **Make streaks forgiving.**
   - Duolingo: holding up to 2 streak freezes raised DAU by 0.38 %; making the streak easier to keep raised D14
     retention by 3.3 % and 7+-day streaks by 40 % [6][7].
   - Snapchat added a one-tap restore [8].
   - Silverman & Barasch (7 studies): showing a *broken* streak lowers later engagement, and less so when it can be
     repaired [9][10].
   - For a training tool, rest days are good, so we use a weekly streak with automatic tokens and never show
     "streak lost".
5. **Rewards should read as feedback on competence, not as control.**
   - Deci, Koestner & Ryan's meta-analysis (128 studies) [14]:
     - Expected, engagement-contingent tangible rewards undermine intrinsic motivation (d = −0.40).
     - Unexpected rewards don't (d = 0.01).
     - Positive feedback helps (+0.33).
   - SDT in games: perceived competence and autonomy predict enjoyment and future play [15].
   - Applied here:
     - Every reward says what improved ("PB +4 % · Strafe Tracking").
     - The surprise retro reveal and occasional unannounced bonuses use the "unexpected" effect.
     - Challenges are relative to the player's own baseline.
6. **The goal gradient.**
   - People speed up as a reward gets close. A 12-stamp card with 2 stamps pre-filled was finished faster than a
     blank 10-stamp card (median 10 vs 15 days) [16].
   - Applied here: start with a pre-filled bar (welcome +250 XP), put a reward on every level, and always show the
     next three unlocks with progress.
7. **Reward skill, not hours, at the top.**
   - osu!'s pp system "shifts the focus of skill progression from the amount of time played" by counting only best
     scores [18].
   - Aimlabs' benchmark rank is set by the weakest subcategory [3].
   - Our top tier is gated on stars (improvement), not XP.
8. **What backfires:**
   - **Grind walls:** Battlefront II's ≈ 40-hour hero unlocks [19]; Overwatch 2's heroes at battle-pass tier 55,
     moved to 45, then made free [20].
   - **Locking content that matters:** we lock only cosmetics, never drills or features.
   - **AFK/XP farming:** Fortnite Creative needed AFK detection and banned "XP farm" maps [21].
   - **Monetisation that feels aggressive:** Aimlabs+ at about $9.99 a month bundles training features and
     currency [2][4]. ValTrainer is free and offline, which is a selling point, so nothing is bought.
9. **Aim trainers today:**
   - Aimlabs: a 40-level battle pass progressed by any task, plus bonus XP for leaderboards and ranked [1]; paid
     editions with gem skins [4]; daily Playlist of the Day credits.
   - KovaaK's: no XP. Stars and unlocks per workout, benchmarks, cosmetic DLC [22]. Its de facto ranks come from the
     community Voltaic benchmarks [23].
   - Neither ties cosmetics to measured improvement the way our coach allows. That is the differentiator.
10. **Retention baselines:**
    - Mobile medians are D7 < 4 % and top-quartile D1 ≈ 30 % (secondhand, [24]).
    - Duolingo calls the first 7 days the fragile window [25].
    - So the first session must unlock something, and days 2–7 must each offer a visible next step.

What could not be verified: how Aimlabs account XP works in detail, exact VALORANT checkpoint and weekly XP, and any
peer-reviewed proof that aim-trainer practice carries over to matches. The design doesn't rely on any of these.

---

## 3. XP: the formula

### 3.1 Per run (`Progress.ScoreRun`)
```
active   = seconds of the run with input: 1-s buckets of telemetry frames with |Dx|+|Dy| > 0, or Firing, or a key-driven
           movement speed > 0.5 m/s (movement drills). Retro (no telemetry): 0.9 × run.secs, or 0.9 × the drill's Duration.
minutes  = min(active, 300) / 60                                     // per-run cap 5 min (a full DM; tactical 240 s fits)
E        = engagement: 0 if Shots == 0 && Kills == 0 && Score <= 0 && active < 15 s   (idle / walked away)
           × 0.5 if Shots >= 60 && 0 <= Accuracy < 0.10                (spray-and-pray spam)
           else 1
T        = tier factor: 1.0 Rookie · 1.1 Regular · 1.2 Veteran · 1.3 Elite · 1.4 Pro (tier −1 / no tier: 1.0)
V        = variety: n-th run of this drill key today: 1–3 → 1.0 · 4–6 → 0.75 · 7+ → 0.5
           exempt (always 1.0): today's coach-focus drill and the drill of an open challenge (focused practice is good)
runXp    = 100 × minutes × E × T × V
taper    = run XP beyond 2,500 in the same day counts × 0.5           (grinders still progress; the binge day doesn't finish the track)
```
- A typical 60-s drill (about 54 s active) at Regular is worth **≈ 99 XP**.
- A 5-minute Deathmatch is worth ≈ 450–500 XP.
- Sens Finder and Crosshair Finder: a flat 50 XP, once per day each. They're tools, not training.
- Adaptive Lock-In steps (not in stats) earn time XP at the routine's tier with V = 1. They don't count for PBs.
- Runs with `KeepInStats = false` (sens-shifted) earn time XP, but no PB, badge or star.

### 3.2 Bonuses
| Bonus | XP | Notes |
|---|---|---|
| Welcome (first launch of 1.19, new or existing player) | +250 | bar half-filled toward level 2: the goal-gradient head start |
| First run of the day | +150 | local day starts **04:00** (late-night sessions don't split at midnight) |
| Rested | +100 % of the run's XP until the pool is empty | pool +300 per calendar day without a run, cap 1,500 (5 days); shown as a second colour on the XP bar |
| New personal best (drill + tier + gun, not the first run there) | +50 | up to 5 a day for XP; stars use the stricter rule in §4 |
| Badge rank-up | +100 | |
| Lock-In completed | quick +150 · standard/training/agent +300 · pro +400 | only `Completed` (FINISH NOW: the time XP only) |
| Daily challenge | +100 each, +100 for all three | |
| Weekly challenge | +500 each | |
| Coach placement / skill rank-up / overall rank-up (confirmed) | +300 / +300 / +1,000 | |
| Weekly streak milestone (2/4/8/12/26/52 weeks) | +500 … +2,000 | |

### 3.3 Level curve
`cost(L → L+1) = min(500 + 100·(L−1), 2500)`. There is no level cap. A level border changes every 10 levels
(VALORANT does it every 20 [5]).

| Level | 2 | 3 | 5 | 10 | 12 | 15 | 20 | 25 | 30 | 40 | 50 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| total XP | 500 | 1,100 | 2,600 | 8,100 | 11,000 | 16,100 | 26,600 | 39,000 | 51,500 | 76,500 | 101,500 |

### 3.4 Pacing check (modelled; scripts in the session scratchpad)
| Player | Level at end of week 1 / 2 / 4 / 8 | Notes |
|---|---|---|
| First session: 10 runs + first-day + welcome + 2 dailies ≈ 1,600 XP | **level 3** | 3 Select skins in session one |
| Casual: 8 runs × 2 days/wk | 5 / 9 / 13 / 20 | first Premium (level 12) in week 4 |
| Regular: 15 runs × 4 days/wk, dailies + 2 weeklies | 10 / 15 / 22 / 36 | **Premium on day 9–10**; level 30 in about week 6 |
| Grinder: 40 runs × 6 days/wk (taper on) | 17 / 26 / 42 / 76 | XP comes fast, but Exclusive and Ultra still need stars and rank-ups |
| Developer, retro (187 real runs, 212 min, 8 Lock-Ins) | **level 19 (26,029 XP), 12★** | run 17.8k, Lock-In 2.5k, rested 2.0k, first-of-day 1.8k, PB 0.9k, badge-ups 0.5k, placement 0.3k, welcome 0.25k |

---

## 4. Improvement: stars (★) and confirmed ranks

### 4.1 Star sources
| Source | ★ | Rule (all on rated guns; never from dev runs, adaptive steps or sens-shifted runs) |
|---|---|---|
| **Real PB** | 1 | Beats the best for this drill + tier + gun by **≥ 2 %**, with **≥ 3 earlier runs** there. Counts only at a tier **no lower than (best badge in that drill − 1)**: once you hold a Veteran badge in Gridshot, Rookie PBs give XP but no star. At most **1 per drill per day** and **3 per day** |
| **Badge rank-up** | 1 | A new best badge in a drill that already had an earned badge (≥ Rookie). A drill's first badge is not a rank-up |
| **Confirmed skill rank-up** | 2 | One of the 8 coach skills reaches a new confirmed high rank (§4.2) |
| **Confirmed overall rank-up** | 5 | The overall coach rank reaches a new confirmed high |
| **Weekly "beat your baseline" challenge** | 1 | +5 % over your own 7-day baseline in the coach's focus drill (§5). Relative, so any skill level can earn it |

Expected pace:
- Developer (real data, retro): 7 PB★ + 5 badge★ = **12★** in a month.
- A regular player who is improving: about 3–5★ a week early on, then about 2–3.
- 25★ (the Ultra gate) is about 6–10 weeks away.

### 4.2 Confirming coach ranks
- **Sample.** Each time `CoachData` finishes a real profile (not `--fakecoach`), Progress stores *today's* sample:
  overall (tier, confidence) and each skill (tier, confidence). The last profile of the day replaces earlier ones.
  At most 40 days are kept.
- **Valid sample.** Confidence ≥ 0.75 (HIGH) and tier ≥ 0, from the current `Bench.Calibration` version.
- **Placement.** The first time a track has 3 valid days, rank = `CoachRank.Index(median)`. This earns +300 XP and the
  `coach:placed` requirement. It gives no stars.
- **Confirmed rank.** `CoachRank.Index(median of the last 5 valid days − 0.10)`. Every day in the window must be
  after the placement day, so a placement made on immature data can't hand out an instant rank-up.
- **Rank-up.** Awarded when the confirmed rank is above the track's high-water mark. The mark only rises: falling
  back never takes anything away, and climbing again past the mark is what counts.
- **Recalibration.** Add `public const int Calibration = 1;` to `Bench` (`Analysis/Benchmarks.cs`). The coach agent
  bumps it with the less-optimistic recalibration.
  - Samples from another version are ignored, and placement starts again quietly.
  - The new placement and high-water mark replace the old ones, with no XP and no stars.
  - Earned stars and unlocks stay. The Progress screen shows: *"The coach was recalibrated in 1.19. Your coach
    rank is being re-placed; nothing you earned is lost."*
  - The XP and stars that rank-ups grant are fixed. A lower scale only delays future rank-ups; it doesn't punish.
- **Why 5 days:** on the real history, the raw daily estimate would have given and then taken away Diamond. The
  5-day median confirms only sustained moves (§1.5). A real rank-up therefore takes about 1–3 weeks to confirm,
  which suits "real improvement".

### 4.3 Other achievements (requirement ids, no stars)
- `coach:placed`: first confirmed overall rank.
- `lockins:N`: completed Lock-Ins.
- `streak:N`: weekly streak of N weeks.
- `drills:N`: distinct drills with a badge.
- `badge:<tier>`: first badge of that tier in any drill.
- `with:<line>:runs:N` / `with:<line>:pb` / `with:<line>:badgeup`: runs, a real PB or a badge rank-up while that
  skin line was on the gun. These unlock variants (§6).

---

## 5. Challenges

**Rules:**
- **Generation is deterministic per day and week:** `seed = hash(dayKey + install salt)`, where the salt is a random
  GUID kept in progress.json. A restart doesn't reroll.
- **Daily reset** at 04:00 local; **weekly** on Monday 04:00 local.
- **No backlog and no penalty:** unfinished challenges simply expire. They complete automatically, with nothing to
  claim.
- **One reroll per day** (any daily slot).
- Sens Finder and Crosshair Finder never count. Adaptive Lock-In steps count for "play / time / different drills",
  but never for score targets.
- **All targets are relative to the player:** their 7-day baseline via `WarmupScore.Baseline`, their typical badge
  (the median of the last 20 rated badges), and drills from their own coach profile.

**Daily (3 slots):**

| Slot | Pool (one is picked) | Example |
|---|---|---|
| A · Coach focus | `Profile.Problems[0].Drills` → "Play 3 runs of {drill}". With no profile: the first `CoachData.Starter` drill still short of its target. Fallback: a drill not played in 7 days | *"Coach focus: 3 runs of Head Flicks. Fixes: slow micro-adjustments"* |
| B · Quality (vs yourself) | "Score at least your 7-day average in {drill} once" (`IndexOf ≥ 100`) · "Earn a {typical badge} badge or better in any drill" · "Hit {your 7-day accuracy, rounded down}% accuracy in {drill}" | *"Match your Strafe Tracking average (12,400)"* |
| C · Habit / variety | "Play 3 different drills" · "Train for 10 minutes" · "Try a drill you haven't played in 14 days" · "Play a map or agent drill" · "Complete a Lock-In" (offered only to players who have done one, at most every 4th day) | *"Play 3 different drills"* |

**Weekly (3):**

| Slot | Challenge | Reward |
|---|---|---|
| W1 | Train on 3 different days | 500 XP |
| W2 | **Improvement:** beat your 7-day baseline by 5 % in the coach's focus drill (frozen when the week starts) | 500 XP + **1★** |
| W3 (rotates) | Complete 2 Lock-Ins · earn 2 badge rank-ups · train {weakest skill} drills for 20 minutes · set 3 PBs | 500 XP |

- **Weekly streak:** a week (Monday to Sunday, local) counts with 2 or more training days.
  - You earn 1 freeze token every 4 counted weeks and can hold 2. A missed week uses a token automatically.
  - The UI says *"Rest week used: your streak is safe"*. It never says "streak lost". With no token, the counter
    restarts quietly at the next counted week, and the best streak is kept.
  - Milestones at 2, 4, 8, 12, 26 and 52 weeks.

---

## 6. The unlock table

Editions follow the skins agent's scale: Select < Deluxe < Premium < Exclusive < Ultra.

**Slot ids:**
- `base`, `vfx` (animated), `sound`, `kill` (kill effect), `finisher`.
- `v1`, `v2`, `v3` (variants).

**Rules:**
- An upgrade slot needs its line's base.
- The table assumes about 6 Select, 5 Deluxe, 4 Premium, 3 Exclusive and 2 Ultra lines.
- **If the skins agent ships more lines, `EditionDefaults` assigns them** using the same pattern, continuing from the
  next free level.

### 6.1 Level track (every level gives something: goal gradient)
| Lvl | Reward | Lvl | Reward |
|---|---|---|---|
| 1 | Select #1 (welcome gift) | 16 | Premium #2 VFX |
| 2 | Select #2 | 17 | Premium #3 base |
| 3 | Select #3 | 18 | Premium #2 sound |
| 4 | Deluxe #1 | 19 | Premium #3 VFX |
| 5 | Select #4 | 20 | Premium #3 sound · *Exclusive #1 base (+10★)* · border |
| 6 | Deluxe #2 | 22 | Premium #4 base (24: VFX, 26: sound) |
| 7 | Select #5 | 24 | Exclusive #1 VFX |
| 8 | Deluxe #3 | 26 | *Exclusive #2 base (+18★)* |
| 9 | Select #6 | 28 | Exclusive #1 sound |
| 10 | Deluxe #4 · level border | 30 | ***Ultra #1 base (+25★ + rank gate)*** · Exclusive #2 VFX · border |
| 11 | Deluxe #5 (or earlier: `coach:placed`) | 32 | *Exclusive #3 base (+25★)* |
| 12 | **Premium #1 base** | 34 | Ultra #1 VFX (36: Exclusive #2 sound, 38: Ultra #1 sound) |
| 13 | Premium #1 VFX | 40 | ***Ultra #2 base (+35★ + rank gate)*** · border |
| 14 | Premium #2 base | 42–48 | remaining Exclusive #3 / Ultra #2 VFX and sound, every 2 levels |
| 15 | Premium #1 sound | 50+ | a border every 10 levels; levels past the track grant a bonus reroll |

Odd levels between 21 and 49 with no skin give an extra daily reroll token and the "next unlock" preview. They can
also host variant colours if the skins agent wants more items.

### 6.2 Improvement-gated slots (independent of level, except where shown)
| Item | Requirement |
|---|---|
| Premium #1–#4 **kill effect** | 3★ / 6★ / 9★ / 12★ |
| Exclusive #1 / #2 / #3 **base** | level 20 + 10★ / level 26 + 18★ / level 32 + 25★ |
| Exclusive **kill effect** | 15★ / 22★ / 30★ |
| Exclusive **finisher** | 1 / 2 / 3 confirmed skill rank-ups |
| Ultra #1 **base** | level 30 + 25★ + **(1 confirmed overall rank-up or 3 confirmed skill rank-ups)** |
| Ultra #2 **base** | level 40 + 35★ + (2 overall rank-ups or 5 skill rank-ups) |
| Ultra **kill effect** | 30★ / 45★ |
| Ultra **finisher** | a confirmed overall rank-up made *after* that line's base was unlocked, or 2 more skill rank-ups |
| Deluxe #5 | `coach:placed` or level 11, whichever comes first (the coach tie-in for new players on day 3) |

### 6.3 Variants (all editions): "earn it with the skin on"
| Variant | Select / Deluxe | Premium | Exclusive | Ultra |
|---|---|---|---|---|
| v1 | 10 runs with the line | 15 runs with it | 20 runs with it | 25 runs with it |
| v2 | 1 real PB with it | 1 real PB with it | 1 badge rank-up with it | 4-week streak |
| v3 | — | 1 badge rank-up with it | weekly improvement challenge ×3 | **12-week streak** |

"With it" means the run's gun wore any level of that line. Skins become a reason to pick a gun and train with it.

### 6.4 Outcomes by player type
| Player | What they have |
|---|---|
| First session | Select #1–#3 (and close to Deluxe #1 at level 4) |
| Regular, week 2 | Select + Deluxe complete, Premium #1 with VFX and sound, Premium #2 base; Premium kill effects as stars come in |
| Developer retro (level 19, 12★) | 22 items in 14 lines: all Select (6) and Deluxe (5), Premium #1–#3 with upgrades up to level 19, Premium #1–#3 kill effects. Exclusive #1 is one session away (it needs level 20; the stars are already met) |
| Grinder who isn't improving | Every XP item, but stuck at Exclusive #1 or #2 and with no Ultra until they get better. That is intended, and the Progress screen says what to do: "Your coach says: fix slow micro-adjustments. Each real PB is a ★" |

---

## 7. Retroactive credit and the reveal

**When it runs:** on the first launch of 1.19, when `progress.json` is missing and `stats.json` has runs. It runs on a
worker (pure data, under 100 ms for 10k runs) while the menu loads.

**How it works:**
1. Replay every stats run in time order through exactly the per-run function the live game uses (time from
   `run.secs` or the drill's Duration × 0.9; engagement from shots, kills and score). This includes first-of-day,
   rested, variety, taper, PB and badge rules, and stars.
2. Add the completed Lock-Ins from warmups.json: the completion bonus, plus time XP for adaptive steps only.
3. Coach: build the current profile once. If it qualifies, it becomes the **placement** (+300 XP). There are no
   retro rank-ups: the history wasn't sampled, and the coach is being recalibrated.
4. Challenges and streaks:
   - No retro challenges.
   - The weekly streak *is* derived from run dates (with tokens), because it is cheap and honest.
5. Set `CreditedThrough` to the newest run's `When`.

**Invariant (tested): retro equals live.** Feeding the same history run by run through the live path gives the same
XP and stars.

**The reveal** (full-screen overlay on the menu, after the What's-new panel; skippable with Esc or a click at any
time):
1. "YOUR TRAINING COUNTS". Counters roll up: 187 runs · 3 h 32 min · 34 personal bests · 5 badge rank-ups ·
   8 Lock-Ins.
2. The level bar fills 1 → 20 with a soft tick per level (≈ 2.5 s total).
3. The ★ counter fills to 12.
4. Unlocked skins flip in, grouped by edition colour ("14 SKINS UNLOCKED").
5. Buttons: **OPEN COLLECTION** · **CONTINUE**.

New players see a short version instead: "Welcome: +250 XP. Your first skin is ready."

**Catch-up path:** the same function runs on every launch for stats runs newer than `CreditedThrough`. These are
runs played on another build, for example after a downgrade. The reveal for them is a normal unlock toast.

---

## 8. Anti-abuse (light) and dev safety

Progress is offline and open-source. There is no cheat detection, no checksums and no "suspicious activity"
messages. People who edit `progress.json` only cheat themselves. We only avoid *rewarding* obvious farming:

| Exploit | Handling |
|---|---|
| Idle runs / walking away | `active` seconds come from real input, and engagement is 0 without shots, kills or score |
| Spamming the easiest drill | variety decay to ×0.5, tier factor (Rookie ×1.0), daily taper; PB stars need a tier no lower than best badge − 1, ≥ 3 earlier runs and +2 % |
| Restarting until lucky | no record means no XP; PB stars are capped at 1 per drill per day and 3 per day |
| Abusing long drills | XP is per active minute, capped at 5 min per run |
| Clock tricks | daily and weekly keys move only forward. If the clock goes back, nothing resets until the real day passes the last key. Rested XP accrues only for forward gaps |
| Dev / automated runs | **never write progress** (`Main.SavesData`, like stats). `--dev` without `--data-dir … --write-data` keeps everything in memory. `selftest.ps1` already asserts that the data dir stays empty |

`--duration`, `--simaim`, `--tier` and `--gun` dev runs show the XP strip in memory only.

---

## 9. Interface with the skins agents

```csharp
namespace ValTrainer.Progress;

public enum Edition { Select, Deluxe, Premium, Exclusive, Ultra }
public enum SkinSlot { Base, Vfx, Sound, Kill, Finisher, V1, V2, V3 }

/// Skins side declares each line once (GunSkins.cs catalogue): id, edition, order within the edition, slots it has.
public sealed record SkinLineInfo(string Line, Edition Edition, int Order, IReadOnlyList<SkinSlot> Slots);

public static class Progress
{
    // ---- ids ----
    public static string ItemId(string line, SkinSlot slot);      // "carbon", "carbon/vfx", "carbon/v2" (lower-case, stable)
    // ---- queries (main thread; cheap; safe before Init: everything "free" is unlocked, the rest locked) ----
    public static bool IsUnlocked(string itemId);                 // "factory" and unknown-but-free → true
    public static UnlockStatus Status(string itemId);             // requirement + progress for locked tiles
    public static int Level { get; }  public static int Xp { get; }  public static int XpIntoLevel { get; }  public static int XpForLevel { get; }
    public static int Stars { get; }
    public static IReadOnlyList<UnlockStatus> NextUnlocks(int n); // nearest by level and by ★ (Progress screen, menu tooltip)
    // ---- registration (skins side, static init) ----
    public static void RegisterLines(IEnumerable<SkinLineInfo> lines); // builds requirements from the table + EditionDefaults
    // ---- events (main thread) ----
    public static event Action<UnlockInfo>? Unlocked;             // one per newly unlocked item (live, retro, dev)
    public static event Action<int, int>? LevelUp;                // from, to
    public static event Action? Changed;                          // XP/★/challenge change (menu strip redraw)
    // ---- feeds (Progress core only) ----
    public static RunXp OnRunFinished(GameSession s, RunRecord rec, bool stored);   // returns the breakdown for the results strip
    public static void OnLockInFinished(WarmupRecord rec);
    public static void OnProfile(SkillProfile p);
}

public sealed record Requirement(string Id, string Text, /* parsed */ IReadOnlyList<ReqTerm> AllOf, IReadOnlyList<IReadOnlyList<ReqTerm>> AnyOf);
public sealed record UnlockStatus(string ItemId, bool Unlocked, Requirement Req, float Progress01, string ProgressText); // "Level 12 (you're 9)", "★ 7 / 10 · Level 20 ✓"
public sealed record UnlockInfo(string ItemId, string Source, DateTime When);            // Source: run | lockin | challenge | coach | retro | dev
public sealed record RunXp(int Total, IReadOnlyList<(string Label, int Xp)> Parts, int StarsGained, IReadOnlyList<string> StarReasons,
                           int LevelBefore, int LevelAfter, float BarBefore, float BarAfter, IReadOnlyList<ChallengeTick> Challenges);
```

**Requirement grammar** (data, so it can be tuned without code; `+` = all of, `|` = any of, parentheses allowed):
- `free`, `level:N`, `stars:N`
- `rankup:overall:N`, `rankup:skill:N`, `coach:placed`
- `badgeups:N`, `pbstars:N`, `lockins:N`, `streak:N`, `badge:veteran`
- `with:<line>:runs:N`, `with:<line>:pb`, `with:<line>:badgeup`, `weeklyimprove:N`

Example: `level:30+stars:25+(rankup:overall:1|rankup:skill:3)`.

**What the skins side does:**
1. **Ids and editions.** Give each line a stable id and an `Edition`, and call `Progress.RegisterLines` once.
   Existing ids stay as they are (`graphite`, `carbon` …). The 16 existing finishes become the Select and Deluxe
   lines.
2. **Resolution.** `GunSkins.For(gun)` returns the chosen line at its **highest unlocked level ≤ the chosen level**.
   For a locked base it falls back to Factory, but **keeps the setting**, so it applies once unlocked.
   - `--skin <id>` (dev) and `--unlock-all` (dev) bypass the locks for screenshots.
   - The viewmodel re-skins on `Progress.Unlocked` through the existing `GunSkins.Changed`.
3. **SkinPicker.** Locked swatches are drawn dimmed with a lock and the edition colour. Selecting one still shows the
   live preview (try before you earn it), but APPLY becomes *"LOCKED · Reach level 12 (you're 9)"* with a progress
   bar from `Status`. Upgrade slots show their lock state the same way.
4. **"With it" counting.** At Finish, Progress records the line on `Mode.ActiveWeapon` via `GunSkins.IdFor(gun)`. No
   new field is needed on the skins side.

**What Progress does:** owns the table (§6) and `EditionDefaults`, evaluates requirements, persists unlocks, which
are permanent, and raises the events. **Agreement:** neither side hard-codes the other's numbers.

---

## 10. UI

All strings go through `L.T`/`L.F`/`L.P`/`L.N` (`docs/research/i18n-howto.md`), including fr/es/pt-BR/de entries in
`translations/*.json`. Rank names reuse the existing keys. Layouts are K-scaled, built in code, and Esc goes back.

1. **Main menu: account strip.**
   - **Where:** in the header's status row (`MenuScreen.Header`, the `coachHint` row at `MenuScreen.cs:299`). It
     merges into one `HeaderStatus` row: account strip on the left, coach rank and problem on the right. This keeps
     the header height and `GridTop` (`MenuScreen.cs:106`) unchanged.
   - **Contents:** `[level emblem] LV 14  [XP bar 220 px: XP fill + rested overlay]  1,240 / 1,800  ★ 9  ·  DAILY 2/3`.
   - **Interaction:** clicking opens the Progress screen. The tooltip lists the next 3 unlocks. The coach text is
     truncated first when the window is narrow.
   - **Also:** a small dot appears on the SKINS button when there are unseen unlocks.
2. **Progress screen** (`ProgressScreen : ScreenBase`, dev `--screen progress`):
   - **Left:** the account card: level emblem, XP bar, ★ total with "where stars come from", weekly streak with
     tokens, rested pool, coach placement and confirmed ranks with a "next rank-up needs 5 days at …" line.
   - **Centre:** daily challenges (3 cards: title, why ("Coach: slow micro-adjustments"), progress bar, PLAY button
     that starts the drill, one REROLL) and weekly challenges (3 cards, "resets in 3 d 7 h").
   - **Right:** NEXT UNLOCKS (the 3 nearest, with progress), RECENT (the last 5), OPEN COLLECTION (SkinPicker).
   - **Bottom:** a horizontal **level track** (levels N−2 to N+8 with reward chips, scrollable), the
     battle-pass-style view that makes the goal gradient visible.
3. **Results screen: XP strip** (`Hud.BuildResults`, `Hud.cs:832`).
   - **Where:** one 44-px row above the buttons in the left panel. Add `44 * k` to `ph` (`Hud.cs:886`).
   - **Contents:** `+186 XP` and the level bar animating from `BarBefore` to `BarAfter`. Chips: `RUN 99 · VETERAN +20 ·
     FIRST TODAY +150 · REST +99 · PB +50`, plus `★ +1 REAL PB (+4.1%)` in gold when earned. Then challenge ticks
     ("Head Flicks 2/3").
   - **Interaction:** hovering shows the full breakdown. It never blocks PLAY AGAIN (R).
   - **Lock-In:** steps show nothing. The Lock-In hand-off (`LockInHandoff`) gets one summary line ("Lock-In complete
     +300 XP · 2 dailies done"). Toasts are held until after the hand-off: don't distract before ranked.
4. **Unlock and level-up popups** (`UnlockToast`, top-right of the results screen or menu, queued, 4 s each, click
   opens the SkinPicker on that item):
   - Contents: "UNLOCKED · CARBON WEAVE · DELUXE", an edition-coloured frame and a mini swatch, with **EQUIP** and
     **LATER**.
   - Level-up: a 1.2-s "LEVEL 12" flash on the bar, with a sound.
   - Several unlocks at once collapse into "3 UNLOCKS · VIEW".
5. **Retro and welcome reveal** (`ProgressReveal` overlay on the menu): §7. It is queued after What's-new and the
   updates prompt, and before Support (`MenuScreen.cs:80-86`).
6. **SkinPicker:** locked states as in §9. A filter chip row offers ALL · OWNED · LOCKED, and an edition legend.

---

## 11. Data: `progress.json`

```jsonc
{
  "Version": 1, "MinReader": 1, "Salt": "9f2c…", "CreatedWith": "1.19.0",
  "Xp": 26029, "Stars": 12,                     // level is derived from Xp (curve in code)
  "CreditedThrough": "2026-10-31T23:26:25", "LockInsThrough": "2026-10-13T00:13:00",
  "Retro": { "Done": true, "When": "…", "Runs": 187, "Xp": 26029, "Stars": 12, "Shown": false },
  "Rested": 600, "LastActiveDay": "2026-11-07",
  "Days": { "2026-11-07": { "RunXp": 1234, "Drills": { "flick": 3 }, "PbXp": 2, "PbStars": 1, "PbDrills": ["flick"], "Finders": ["sensfinder"] } }, // last 14 days
  "Daily":  { "Key": "2026-11-07", "Rerolls": 1, "Items": [ { "Kind": "focus_runs", "Drill": "flick", "Target": 3, "Value": 1, "Done": false, "Xp": 100, "Why": "SLOW_MICRO" } ] },
  "Weekly": { "Key": "2026-W45", "Items": [ { "Kind": "improve", "Drill": "tracking", "Baseline": 12400, "Target": 13020, "Value": 0, "Done": false } ] },
  "Streak": { "Weeks": 3, "Best": 5, "Tokens": 0, "LastWeek": "2026-W45", "Counted": 4 },
  "Coach":  { "Calib": 1, "PlacedOn": "2026-11-05", "Placed": { "overall": 4 }, "High": { "overall": 4, "Movement": 5 },
              "OverallUps": 0, "SkillUps": 2, "Days": [ { "D": "2026-11-07", "O": [1.61, 0.89], "S": { "Flicking": [1.37, 0.90] } } ] },
  "Counters": { "Runs": 187, "ActiveSecs": 11400, "Pbs": 34, "PbStars": 7, "BadgeUps": 5, "LockIns": 8, "Dailies": 0, "Weeklies": 0, "WeeklyImprove": 0 },
  "With":     { "carbon": { "Runs": 12, "Pb": 1, "BadgeUp": 0 } },
  "Unlocked": { "graphite": "2026-11-07T18:00:00", "carbon/vfx": "…" },  // permanent; never removed by any version
  "Unseen":   [ "carbon/vfx" ]                                            // drives toasts, the SKINS dot and the reveal
}
```

**Load and save** (copy `StatsStore`, `Stats.cs:38-69`):
- **Missing file** → retro (§7).
- **Corrupt** (`Paths.IsCorruptData`) → `KeepCorrupt` (`progress.json.corrupt`), then **re-derive** with retro. Only
  challenge state and coach samples are lost. Unlocks are re-derived, so the player loses almost nothing.
- **Unreadable** (locked or no permission) → `readFailed`. Nothing is written this session, and progress runs in
  memory.
- **Newer file** (`MinReader > CurrentVersion`) → load the known fields, **never save** (log once).
- **Unknown fields:** `[JsonExtensionData] Dictionary<string, JsonElement>? Extra` on the root and on nested objects,
  so a 1.19 save never drops fields a 1.20 build wrote.
- **Writes:** `Paths.TryWriteAllText` (atomic tmp + move), no-op unless `Main.SavesData`. Written after each run, each
  Lock-In, each coach sample and each reroll.
- **Older builds** (≤ 1.18) never read progress.json, and **stats.json and warmups.json are unchanged** (no new
  fields). Playing an older build and coming back is handled by the `CreditedThrough` catch-up.
- **Not a new stats field:** `active` seconds are used at Finish and summed into `Counters`. stats.json stays
  byte-compatible.
- **Self-test round trips** go in `CultureCheck`, like the existing stats ones (`CultureCheck.cs:700-730`).

---

## 12. Tests

**Headless self-test** `--progress-test` (exit code = failures; add it to `tools/selftest.ps1` next to
`--culture-test`). It checks:
- **Curve:** monotonic, with the level and XP-into-level inverse.
- **Run XP:** idle = 0; spam ×0.5; variety 1 / 0.75 / 0.5 with the focus-drill exemption; taper at 2,500;
  first-of-day once per 04:00 day; rested accrual, cap and consumption; per-run 5-minute cap; finders once a day.
- **Stars:**
  - PB★: needs ≥ 3 earlier runs and +2 %; tier no lower than best badge − 1; 1 per drill per day and 3 per day.
    Unrated guns, adaptive steps and sens-shifted runs give none.
  - Badge-up: none for a drill's first badge.
- **Coach confirmation:**
  - Synthetic series built from §1.5: the Diamond spike → no rank-up; the Precision 0.01 ↔ 2.50 swing → no rank-up;
    sustained Movement 2.4–2.6 → +1 skill rank-up.
  - Low-confidence days are ignored. A placement window can't produce a rank-up.
  - A `Calibration` bump re-places with no XP or stars, and unlocks stay.
- **Requirements:** the parser (`+`, `|`, parentheses, unknown term → locked and logged); `EditionDefaults` for
  extra lines; upgrade slots need the base.
- **Never revoke:** load a file with `Unlocked` entries the current table wouldn't grant → they stay unlocked.
- **Persistence:** JSON round trip keeps unknown fields; newer `MinReader` → read-only; corrupt → `.corrupt` and
  re-derived; unreadable → no write; `SavesData=false` → no file.
- **Time:** day and week keys over DST changes and New Year (ISO weeks); the clock going backwards does nothing.
- **Challenges:** deterministic for a given seed and day; never Sens or Crosshair Finder; targets relative to the
  baseline; Lock-In steps excluded from score targets.
- **Retro equals live** on a generated 500-run history and on a fixture shaped like the developer's file (counts
  only, no personal data).

**Dev flags** (all `--dev` only; none of them write unless `--data-dir … --write-data`):

| Flag | What it does |
|---|---|
| `--progress-xp N` | grant XP |
| `--progress-level N` | set the level |
| `--progress-stars N` | set the stars |
| `--progress-to-next N` | put the bar N XP short of the next level (level-up screenshots) |
| `--unlock-all` / `--unlock id,id` / `--lock-all` | force unlock states |
| `--progress-reset` | start empty, in memory |
| `--progress-now 2026-11-20T10:00` | fake clock for resets and rested XP |
| `--progress-retro` | force the retro reveal from the data-dir's stats |
| `--progress-reveal demo\|welcome` | show the reveal with made-up or welcome data |
| `--progress-demo-unlock <id>` | queue an unlock toast |
| `--progress-sim casual\|regular\|grinder --sim-days N` | headless: simulate history and print level, stars and unlocks per day. CI pacing asserts: a first-session unlock; regular Premium within 14 days; no Ultra without a rank-up |
| `--screen progress` | open the Progress screen |
| `--fakecoach full` | fake coach data for the coach-focus challenge text (already exists) |

**Screenshot plan** (`tools\gshot.ps1` always passes `--dev`). First copy the data:
`Copy-Item "$env:APPDATA\ValTrainer" "$env:TEMP\vt-prog" -Recurse`. Never pass `--write-data` for the real-data
copy, so it stays untouched as well.

| Shot | Command (`-Extra`) |
|---|---|
| Retro reveal (real history) | `-At 3,5,8 -Name prog-retro -Extra "--data-dir $env:TEMP\vt-prog --progress-retro"` |
| Menu strip | `-At 4 -Name prog-menu -Extra "--data-dir $env:TEMP\vt-prog"`, then `--window 1280x720`, `--window 1024x768`, `--lang de` |
| Progress screen | `-At 4 -Name prog-screen -Extra "--screen progress --progress-level 14 --progress-stars 9 --fakecoach full"` |
| Results XP strip + level-up + star | `-Mode flick -At 12 -Name prog-results -Extra "--simaim good --duration 8 --progress-to-next 40"` |
| Unlock toast | `-Mode gridshot -At 12 -Extra "--simaim good --duration 8 --progress-demo-unlock carbon"` |
| Locked skins in the picker | `-At 4 -Extra "--screen skins --progress-level 5"` (the skins agent adds `--screen skins` if it's missing) |
| Lock-In hand-off line | `-At 30 -Extra "--lockin short --wuquick"` |

Check every shot at 1600×900, 1280×720 and 1024×768, and in de, which has the longest strings.

---

## 13. Build split (2 progression agents + the skins agents)

**Order:**
1. **Day 0:** P1 lands `src/Progress/ProgressApi.cs`, the frozen §9 signatures with a working stub (in-memory level
   and stars, dev flags, a table with `EditionDefaults`). P2 and the skins agents can then compile against it.
2. P1 fills in the real logic behind the same API.

| Agent | Owns (create) | Touches (small, listed edits) |
|---|---|---|
| **P1 · Progress core** | `src/Progress/ProgressApi.cs` (facade, events), `ProgressStore.cs` (json, versioning, ExtensionData), `XpRules.cs` (§3), `Stars.cs` (§4.1), `CoachTrack.cs` (§4.2), `Challenges.cs` (§5), `Streak.cs`, `UnlockTable.cs` (§6, EditionDefaults, requirement parser), `Retro.cs` (§7), `ProgressDev.cs` (flags, `--progress-sim`), `ProgressCheck.cs` (`--progress-test`) | `GameSession.Finish` (after l.872: active seconds from `Telemetry.Frames`, `Progress.OnRunFinished`; store the `RunXp` on the session for the Hud) · `WarmupRunner.Complete` (after `store.Save()`, l.330) · `CoachUi.CoachData` (main-thread `OnProfile` when `Ready` first completes for a key) · `Main._Ready` (Init after stats load; `--progress-test`/`--progress-sim` exits; `--screen progress`) · `Analysis/Benchmarks.cs` (`Calibration` const; tell the coach-recalibration agent) · `tools/selftest.ps1` (one step) |
| **P2 · Progress UI** | `src/UI/ProgressScreen.cs`, `src/UI/ProgressWidgets.cs` (AccountStrip, XpBar, ChallengeCard, LevelTrack, UnlockToast, ProgressReveal) | `MenuScreen.Header` (HeaderStatus row; SKINS dot; reveal queue) · `Hud.BuildResults` (XP strip; `ph` +44·k) · `LockInHandoff` (one line) · `Main.ShowProgress()` · `translations/*.json` (new keys) |
| **Skins agents** | `GunSkins.cs` catalogue (`Edition`, slots, `RegisterLines`), `SkinPicker.cs`/`SkinPreview.cs` lock UI | `GunSkins.For` resolution through `Progress.IsUnlocked` (§9.2); `--screen skins` if not present |

**Conflict rules:**
- Only P1 edits `GameSession`, `WarmupRunner`, `CoachUi` and `Main`. P2 asks P1 for `Main.ShowProgress` or adds only
  that method.
- Only P2 edits `MenuScreen` and `Hud`.
- The skins agents never edit `src/Progress`. Table changes go through P1, or later through data.
- `translations/*.json` is a shared file: agents append keys in separate commits. Rebase, don't overwrite.
- Commit trailers per the Discord-close convention, if a Discord post asked for progression or unlocks.

---

## 14. Risks

| Risk | Mitigation |
|---|---|
| **Locking skins that were free in the unreleased 1.19 build** feels like a take-away to testers | 1.19 isn't released, so no real user loses anything. The retro grant gives existing players most of the Select and Deluxe set at once |
| **Extrinsic rewards crowd out training for its own sake** [14] | Rewards are framed as feedback ("real PB +4 %"), the retro reveal is a surprise, challenges are relative and coach-driven, and training content is never locked |
| **Coach noise or recalibration produces false rank-ups, or takes ranks away** | 5-day median + margin + HIGH confidence; calibration versioning; high-water marks; unlocks are permanent |
| **Grinders feel walled at Ultra** | The Progress screen explains the gate and links the coach's focus drill. Every Ultra gate has an "or 3 skill rank-ups" path, and weekly improvement stars are relative to your own baseline, so every skill level can progress |
| **Players near the top of the scale can't rank up** | Skill rank-ups (8 tracks), badge rank-ups at Elite and Pro tiers, and the relative weekly challenge all still give stars |
| **XP numbers feel stingy or flood in once the catalogue size changes** | One table and `EditionDefaults` in P1; `--progress-sim` pacing asserts in CI; retune after 2 weeks of release data (only the developer's history exists today: 12 days, 187 runs) |
| **UI crowding** (the menu header and results panel are already dense) | The menu strip shares the existing coach row; the results strip is one row with hover detail; toasts are queued and non-blocking |
| **Data loss or downgrade** | Atomic writes, `.corrupt` copy + re-derive, newer-version read-only, ExtensionData round trip, stats and warmups untouched |
| **Dev and test runs writing to the player's progress** | `Main.SavesData` gate everywhere; `selftest.ps1` already fails when the data dir isn't empty |
| **Lock-In pressure before ranked** | Lock-In steps never count toward score-target challenges; no toasts until the hand-off |

---

## 15. Sources
1. r/aimlab (mirror), Aimlabs Season 4 battle pass: 40 levels, bonus XP for leaderboards and ranked. https://redlib.belloworld.it/r/aimlab
2. Aimlabs+ on Steam (features and price). https://store.steampowered.com/app/2253310
3. Aimlabs, "What does your Aimlabs Benchmarks rank actually mean". https://aimlabs.com/articles/aimlabs/what-does-your-aimlabs-benchmarks-rank-actually-mean/
4. Aimlabs paid editions (Xbox store): Aim Credits, gem skins. https://www.xbox.com/en-US/games/store/-/9PC2B65HB60N
5. Riot, "Progression update explainer FAQ" (Patch 7.0: daily checkpoints, missed-day ×2, Deathmatch excluded, Kingdom Credits) and "Account leveling explained". https://playvalorant.com/en-us/news/game-updates/progression-update-explainer-faq/ · https://playvalorant.com/en-us/news/game-updates/valorant-account-leveling-explained/
6. Duolingo, "How Duolingo streak builds habit" (2 freezes: +0.38 % DAU; 7-day streak: 3.6× course completion). https://blog.duolingo.com/how-duolingo-streak-builds-habit
7. Duolingo, "Improving the streak" (D14 +3.3 %, 7+-day streaks +40 %). https://blog.duolingo.com/improving-the-streak
8. Snap, "Keep the streak with Restore". https://newsroom.snap.com/keep-the-streak-with-restore
9. Silverman & Barasch, "On or Off Track: How (Broken) Streaks Affect Consumer Decisions", JCR 2023. https://udspace.udel.edu/items/42ce576b-8e1f-429a-8541-e29e48dbcfbb
10. CU Boulder summary of the streaks research. https://www.colorado.edu/business/news/2023/04/20/research-streaks-marketing-tech-barasch
11. GameRevolution on Halo Infinite challenge-only progression. https://www.gamerevolution.com/features/697737-halo-infinite-update-battle-pass-challenges
12. TheSixthAxis on Halo Infinite's match XP and tapering daily XP. https://www.thesixthaxis.com/?p=389725
13. TheSixthAxis, Halo Infinite Winter Update progression overhaul. https://www.thesixthaxis.com/2022/11/02/halo-infinite-is-finally-getting-a-match-xp-and-progression-overhaul-in-the-winter-update/
14. Deci, Koestner & Ryan (1999), meta-analysis of extrinsic rewards and intrinsic motivation. https://depts.washington.edu/techdocs/papers/deciExtrinsicRewardsAndIntrinsicMotivation99.pdf
15. Ryan, Rigby & Przybylski (2006), "The Motivational Pull of Video Games: A Self-Determination Theory Approach". https://link.springer.com/content/pdf/10.1007%2Fs11031-006-9051-8.pdf
16. Kivetz, Urminsky & Zheng (2006), "The Goal-Gradient Hypothesis Resurrected". https://business.columbia.edu/sites/default/files-efs/pubfiles/1200/goalgradient.pdf
17. Warcraft Wiki, Experience point (rested XP). https://warcraft.wiki.gg/wiki/Experience_point
18. osu! wiki, Performance points. https://osu.ppy.sh/wiki/en/Performance_points
19. Vice and TheGamer on the Battlefront II "pride and accomplishment" comment. https://vice.com/en/article/star-wars-battlefront-most-downvoted-comment-on-reddit · https://www.thegamer.com/ea-guinness-world-record-most-downvoted-reddit-comment/
20. PC Gamer on Overwatch 2 battle-pass heroes (tier 55 → 45); Blizzard, all heroes free from Season 10. https://pcgamer.com/blizzard-overwatch-2-hero-battle-pass-interview · https://pcgamer.com/blizzard-bends-on-overwatch-2-battle-pass-makes-next-hero-easier-to-unlock-for-free · https://overwatch.blizzard.com/en-us/news/24062312/
21. Fortnite Creative AFK and XP-farm changes. https://fortnitenews.com/xp-changes-coming-to-creative-mode-in-fortnite-v18-40/ · https://sportskeeda.com/fortnite/news-afk-xp-coin-farm-coin-slide-images-fortnite-coins-removed-creative
22. KovaaK's on Steam. https://store.steampowered.com/app/824270/KovaaKs/
23. Voltaic benchmarks documentation. https://docs.google.com/document/d/1vHiQRZMBJlmI69-SgHm3i0eS5ALfq2hEu-ZPyvC6ycE/mobilebasic
24. GameAnalytics mobile benchmarks (secondhand summary). https://gamedevreports.substack.com/p/gameanalytics-mobile-gaming-benchmarks
25. Lenny's Newsletter, "Behind the product: Duolingo streaks". https://lennysnewsletter.com/p/behind-the-product-duolingo-streaks
