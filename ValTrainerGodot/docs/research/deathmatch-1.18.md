# Deathmatch diagnosis for 1.18: spawns and bot movement

Research date 2026-10-31, on `main` at `abbf167` (v1.17.0). This is a diagnosis only. No game source was changed.

Two Discord #suggestions reports:

1. "Spawns in deathmatch are very broken and dont work quite right, not similar to valorant at all."
2. "the bots in DM have an NPC-type movement, they are just stuck to the wall and not really strafing or anything like that."

Both reports are accurate. They come from several separate causes, each listed below with its file and line, the evidence, what VALORANT does, and a fix plan.

## 0. Summary

| # | Root cause | Where | Effect |
|---|---|---|---|
| S1 | The spawn score aims for **12-28 m from the nearest enemy**, measured in a straight line through walls and floors. The "clean" check only tests the spawn spot itself. | `DeathmatchMode.cs:160-184` | On the 5 large maps, 19-44% of your respawns can be seen by a bot after **one step (2.5 m)**. On Bind it is 56-61%. Meanwhile the dev counter "spawns not clean" reads 0. |
| S2 | Spawn candidates are **any open nav node**. There are no spawn points and no back-to-wall rule. Facing is the **longest sightline**, chosen without looking at where enemies are. | `DeathmatchMode.cs:81-82`, `187-199` | You spawn on Split's Heaven/Rafters (on top of 4 m wall boxes), on Ascent's 1.3 m planter curbs, or in corridors. The nearest bot is **behind you in 30-43%** of spawns. On Bind you face out of the Hookah window onto site. |
| S3 | Where you died is ignored (VALORANT 4.03: "favor respawn locations farther away from where you are defeated"). | `RespawnPlayer`, `DeathmatchMode.cs:223-234` | 23% (Ascent) to 48% (Bind) of respawns are within 15 m of where you died. |
| S4 | **Every bot hunts you.** A respawned bot gets your exact position. Every shot you fire alerts all bots within 35 m (about the whole map). Rumors send 45-75% of idle bots to you every 8-14 s. Hunting bots ignore each other 60% of the time. | `DeathmatchBots.cs:82`, `101-109`, `249-258`, `386`; `DeathmatchMode.cs:119` | A spawn that was safe when picked is reached in seconds. In-game (Ascent, Elite) the stationary simulated player died **1.9, 3.4, 5.0 and 6.2 s** after spawns the code rated "clean". |
| S5 | 1.5 s spawn protection. Bots keep their reaction and aim timers running during it. | `DeathmatchMode.cs:24`, `108`, `118`; `BotBrain.Update` | A bot that saw you while you were protected is already firing when protection ends. VALORANT launched with 8 s that ended when you moved or shot. |
| S6 | **Bind's DM arena is 366 m²** with 3 bots. `BotCount` never goes below 3. | `DeathmatchMode.cs:92-97` | Even a perfect picker cannot make Bind safe. In-game: 12 deaths in 100 s, 3 of them within 2.5 s of spawning, and **9 of 11 respawns in two small clusters**. |
| M1 | **No movement while fighting below Elite.** Elite and Pro get a 0.2 s "jiggle" every 0.7-1.5 s, which is skipped when one side is near a wall. | `DeathmatchBots.cs:173-187` | Rookie, Regular and Veteran bots stand still for the whole duel. Elite and Pro move about 19% of the time. There are no ADAD strafes, long strafes or crouches. |
| M2 | **Fights start at walls.** Shortest-path string pulling rounds every corner at the wall: nav clearance is 0.36 m against a 0.34 m body. Bots see you as they round a corner, then M1 freezes them there. | `BotNav.cs:18`, `272-288`, `310` | 26-47% of first sightings happen with the bot within 0.5 m of a wall. Walls cover only 15-21% of the floor. This is "stuck to the wall". |
| M3 | **Goals and holds are at walls.** Roam goals are random nodes. Rumor goals ("you ±5 m") land inside walls 31-43% of the time and get snapped to the nearest node. Bots then hold there for 1.2-3.5 s. | `DeathmatchBots.cs:330-341`, `255`, `349-363` | Bots park against walls and in corners (screenshots `bind_4_zoom`, `bindtop_crop_10/11`). |
| M4 | **Robotic locomotion.** Bots accelerate at 40 m/s² (0 to full speed in 0.135 s; the player takes 0.3 s). Braking is 9-50 m/s² by tier. They run at a constant 5.4 m/s and their body always faces where they are going. | `DeathmatchBots.cs:172`, `207-209`, `229` | Bots run like NPCs on rails. They never move sideways to their aim, never slice a corner, and never place their crosshair on angles while moving. |
| M5 | **No peeks.** A hunting bot walks its path straight to your position (Veteran and up walk the last 9 m). After losing sight it holds or chases. | `DeathmatchBots.cs:190-203`, `162-168` | There are no wide swings, jiggles, shoulder peeks or re-peeks, although Mixed Peeks already implements all of them. |

**Reusable code already in the repo:** Strafe Duels has a full player-like strafe planner: ADAD, long strafes, crouch-strafes, A-crouch-D, jiggles, counter-strafed stops, and "shoot only when stopped" through `BotBrain.HoldFire`, all tuned per tier (`StrafeDuelMode.cs:48-76`, `193-314`). Mixed Peeks has the player's acceleration and braking plus eight peek kinds (`PeekMixMode.cs:23-56`). The bot body already turns its legs for sideways movement (`BotCharacter.cs:12-14`). DM uses none of these.

## 1. How this was checked

* **Headless harness** (not in the repo, so the Godot build is unaffected): `C:\Users\IdoRaz\AppData\Local\Temp\claude\C--Users-IdoRaz-Desktop-AGY-Projects-valorant-training\fbf26b7d-c776-4050-b015-6a8bc0b65989\scratchpad\dm-diag\harness\Program.cs`. It is a net8 console app. It compiles *copies* of `Collision.cs`, `BotNav.cs` and `MapSpot*.cs` (theme code removed) against the editor's `GodotSharp.dll`, so it uses the real map boxes and the real nav grid. `G.Solid` for map modes is exactly `MapSpot.Boxes` (`Environments.BuildMap`), so its collision is the game's. It contains:
  * an exact copy of `PickSpawn` and `OpenYaw`;
  * the DM bot's path-following controller (40 m/s² acceleration, 0.45 m waypoint radius, `Collision.MoveAndSlide`);
  * a prototype of the proposed spawn picker (section 2.4).

  Run it with `dotnet build -c Release; TOPK=6 dotnet bin/Release/net8.0/DmDiag.dll 600`, which takes about 40 s. Raw output is in `...\scratchpad\dm-diag\harness-final.txt`, Bind variants in `harness-bind.txt`. Each map runs 600 player-respawn trials per scenario, 200 roam paths and 2000 rumor goals.
* **Dev runs** used `tools/gshot.ps1 -Mode deathmatch -Extra "--map <m> --tier <t> --simaim good --data-dir <temp>"`, with `--envcam x,y,z,yaw,-90` for top-down views. Bind ran at Veteran for 110 s, Ascent at Elite for 125 s. Logs and screenshots are in `...\scratchpad\dm-diag\` (`bind.out.log`, `ascent.out.log`, `bind_*.png`, `bindtop_*.png`, `ascent_*.png`). `--simaim` drives only the mouse, so the simulated player **never moves**. Because it never moves, its `[died]` position is also its spawn position. Bot behaviour, bot spawns and the spawn picker are the real ones.
* **Evidence used from the game's own logs:** `[died] ... eye=(...) shot from (...)` (GameSession.DamagePlayer) and `[dm] you died Xs after Ys alive (spawn clean/NOT clean)` plus the 10 s `DevReport`.

## 2. Spawns

### 2.1 What the code does

* **Pool** (`DeathmatchMode.cs:81-82`): every nav node whose 8 neighbours are all walkable. That is roughly all floor at least 0.86 m from a wall, including raised surfaces reachable by 0.7 m steps.
* **First spawn** (`:85-86`): you always start at `MapSpot.StartFeet`, the attacker start. Bots are placed one by one with `PickSpawn`.
* **`PickSpawn`** (`:160-184`): samples up to 90 random pool nodes. A node is "ok" when no living enemy has a line of sight to its eye (1.65 m) or chest (1.0 m), and when the straight-line distance to the nearest enemy is at least 10 m. The score is `-|min(minD,60) - 20| * 0.5`, so it **prefers about 20 m from the nearest enemy**. Sampling stops at the first ok node between 12 and 28 m after 24 samples. Only current enemy positions count.
* **Facing** (`OpenYaw`, `:187-199`): the longest of 16 horizontal rays, plus up to 3 m of random noise. Enemies are not considered.
* **After a respawn**: 1.5 s protection that ends when you fire (`:24`, `:108`, `:118`). A respawning bot learns your exact feet and hunts you (`DeathmatchBots.cs:82`), unless you spawned less than 3 s ago.

### 2.2 What VALORANT does

* **Riot support, "FFA Deathmatch":** "You'll respawn in ideal spots without enemies, but close to the action." "Every time you respawn, enemies will be revealed." Each death drops a health pack (10 s, full HP and armour). First to 40 kills, or most kills in 9 minutes. [support.riotgames.com, Valorant game mode FFA Deathmatch](https://support.riotgames.com/el-gr/valorant/gameplay/valorant-game-mode-ffa-deathmatch) (Greek page; the English article redirects).
* **Launch rules (Act II, 2020):** 3 s respawn and "an eight-second invulnerability period that dissipates after they move or shoot". Back then a map-wide scan ran every 5 s. [ONE Esports](https://www.oneesports.gg/valorant/free-for-all-deathmatch-is-finally-coming-to-valorant-heres-how-it-works/). Prima Games' launch article (403 when fetched; quoted by search results) says spawn points are designated, "will not have enemies near line of sight", "put players' backs to the wall" and "aren't too far from action". [primagames.com](https://primagames.com/tips/how-will-deathmatch-work-valorant)
* **Patch 1.10:** 14 players. "Updated spawning algorithm to encourage safer spawns." "Removed periodic location pulse, and you'll now only receive a pulse right when you respawn." 40 kills / 9 min. Every kill gives a full reload. [Patch Notes 1.10](https://playvalorant.com/en-gb/news/game-updates/valorant-patch-notes-1-10/)
* **Patch 4.03 (Feb 2022):** "Removed and in some cases relocated 'dangerous' spawn locations." "Improved spawn logic and spawn placement to increase the likelihood that you will be facing each other." "Spawning logic will now favor respawn locations farther away from where you are defeated." Respawn time 3 s to 1.5 s. [Patch Notes 4.03](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-4-03/)
* **4.03 aftermath:** players still complained ("spawns were killing players far too quickly"), so Riot cut lobbies from 14 to 12 to "give players more room". This shows that **player density per spawnable area** is itself a spawn-quality lever. [Dexerto](https://www.dexerto.com/valorant/valorant-pros-slam-changes-to-deathmatch-after-spawn-complaints-1765593/)

In short, VALORANT uses designated spawn points with your back to a wall, facing where the fight comes from. It avoids enemy sightlines and spawns you away from where you died, but not too far from the action. Respawn is 1.5 s, followed by a reveal pulse. Thirteen other players spread everyone's attention, and lobby density is tuned to the map.

### 2.3 Root causes and evidence

**S1. The score pulls spawns towards enemies, and "clean" only checks the spawn spot** (`DeathmatchMode.cs:175-178`).
The nearest bot ends up a median 19.5 m away (straight line) on every map. The p10 is 15-16 m (9.7 m on Bind). By path the median is 22-24 m, about 4 s of running.

Only the spawn point is tested. One step away is not. Share of player respawns where a living bot can see your eye from any of the 8 points 2.5 m around the spawn:

| Map (bots) | Ascent (5) | Bind (3) | Breeze (6) | Haven (6) | Icebox (6) | Split (6) |
|---|---|---|---|---|---|---|
| "clean" per code | 100% | 80% | 100% | 100% | 100% | 100% |
| **seen one step out** | **19%** | **61%** | **42%** | **38%** | **40%** | **43%** |
| nearest bot within a 3 s run (path ≤16 m) | 3% | **55%** | 4% | 5% | 3% | 10% |

These numbers assume bots spread uniformly. Clustering bots near your death spot gives the same picture (`harness-final.txt`).

The distance is straight-line (`e.DistanceTo(eye)`). A bot one floor below, or behind a thin wall, counts as far away even when its path is short.

**S2. No spawn points, back-to-wall rule or enemy-aware facing** (`:81-82`, `:187-199`).
* The pool includes odd surfaces:
  * Split: 128 nodes on top of 4 m `Wall` boxes (#108, #109, #112), i.e. the Rafters/Heaven ledges;
  * Ascent: 96 nodes on the 1.3 m planter curbs (`Trim` #2, #3);
  * Haven: 42 nodes on a 0.6 m trim and 4 on top of a crate (#61);
  * Bind: 8 nodes on the window ledge trim (#23).
* Facing ignores enemies. The nearest bot is behind you (dot < 0) in **30-37%** of spawns on the big maps and **41-43%** on Bind.
* On Bind, the longest sightline from Hookah is through the window onto site. Both `bind_1.png` and `bind_6.png` (about 75 s apart) show the player spawned on the Hookah platform looking out of the window.

**S3. Death location is ignored.** Share of respawns within 15 m of the death spot: Ascent 23%, Bind 48%, Breeze 16%, Haven 18%, Icebox 17%, Split 19% (bots uniform). VALORANT 4.03 explicitly favors far from where you died.

**S4. All attention is on you, so "safe" spawns don't stay safe.**
* `Bot.Begin` (`DeathmatchBots.cs:82`): every respawned bot gets `lkp = your feet` and goes into `Hunt`. VALORANT's respawn pulse reveals all enemies, so a real player would pick the nearest of 13. Here you are the only target the code reveals.
* `Noise(PlayerFeet, null, 35f)` (`DeathmatchMode.cs:119`) runs on your shots, at most every 0.5 s. A 35 m radius covers almost all of Bind (33 × 26 m) and most of Ascent (54 × 45 m). Every bot that hears it switches to `Hunt` with your exact position (`Hear`, `:101-109`).
* Rumors (`:249-258`): every 8-14 s, 45-75% of idle bots get "you ± 5 m".
* While hunting, bots ignore each other 60% of the time (`UpdateFoe`, `:386`).

In-game evidence from `ascent.out.log` (Elite, stationary simulated player): life lengths after respawn were 13.8, **5.0, 6.2, 3.4, 1.9**, 11.7 s. The code rated every one of those spawns "clean". `[died]` lines put the killer 6-33 m away at the moment of death, so bots crossed the "safe" distance within seconds.

**S5. Protection is short, and bots pre-aim through it.** `DamagePlayer` ignores hits while `Now < ProtectedUntil` (`GameSession.cs:805`). However, `BotBrain` keeps its `fireAt`, burst and alert state running. A bot that acquired you during the 1.5 s therefore lands its next shot the frame protection ends, with no reaction time. Firing ends your protection immediately (`:118`).

**S6. Bind is too small for 3 bots plus you.** `BotCount` gives `clamp(round(366/250), 3, 6) = 3` (`:95`).
* In the harness, bots can reach 55% of player spawns within 3 s.
* In-game on Bind (Veteran, `bind.out.log`), 12 deaths happened in 100 s. Three were within 2.5 s of spawning, and 21 of roughly 55 spawns were "not clean".
* Spawn positions (= death positions, because the simulated player doesn't move) fall into two clusters. Five are on the Hookah platform (x -3..2, z 0..4, eye 3.25-3.9 m). Four are at B Short south (x 12-17, z 24-25), and three of those four were shot from about (11, 14). This matches "the same few spots".
* The first death came **1.7 s** after the match started: the player was at the fixed `StartFeet` (14, 0, 14) and was shot from 7.3 m away. The harness shows Bind's first-spawn bot placements are "clean" only 60% of the time (p10 9 m from you).

**S7. Fixed first spawn** (`:85-86`). You always start at the attacker start. This is minor and only adds predictability.

### 2.4 Fix plan: spawns

**F-S1. Spawn anchors** (new `src/Modes/DeathmatchSpawns.cs`, partial of `DeathmatchMode`; built in `Setup` and cached per map key like `BotNav.For`).
* From the pool, keep nodes that:
  * stand on a `Surf.Floor` or `Surf.Elevated` box top, or the ground. Find the box whose `Max.Y` equals the node's Y, as the harness does.
  * have a body-to-wall gap ≥ 0.6 m;
  * have a "back wall": one of 16 directions has a wall within 3.5 m behind and at least 10 m of open view in front. Store that front yaw.
* Thin the result to 4 m spacing (Poisson-style greedy pass).
* The harness gets 57 / 20 / 88 / 66 / 94 / 64 anchors (Ascent / Bind / Breeze / Haven / Icebox / Split).
* Add an optional `MapSpot.DmSpawns` (feet + yaw) so a map can hand-author spawn points later, like `PlantSpots` and `RetakeEntries` (`MapSpot.cs:47-52`).
* Size: about 80 lines.

**F-S2. New `PickSpawn(enemies, deathSpot?, out yaw)`.**
* Add `BotNav.DistanceField(Vector3 from, float maxDist = 60f) → float[]`: Dijkstra over `Links`, reusing the `[ThreadStatic]` scratch. It runs once per living enemy per spawn. At most 6 floods of ≤ 8k nodes costs well under 1 ms each.
* For each anchor:
  * **Hard reject:**
    * a direct line of sight (the existing eye and chest rays);
    * a line of sight from any enemy eye to any of the 8 points 2.5 m around the anchor that are reachable on the grid (`nav.At` + `nav.Straight`);
    * a path distance under **20 m** to any enemy;
    * (player only) within 12 m of your death spot;
    * within 6 m of the last 3 spawns.
  * **Score:** `min(minPath, 35) + 0.3·min(deathDist, 30) + jitter`. Keep "close to the action" only as a penalty for `minPath > 45 m`.
  * **Pick uniformly from the top 6** survivors. Pure argmax reuses the same few anchors: in the harness the top 10% of cells took 60-78% of spawns.
* Enemy positions: use each bot's feet + `vel · 0.75 s`, which accounts for bots running at you.
* **Relaxation ladder** when nothing passes: drop the ring test, then lower the path limit to 12 m, then fall back to today's scoring. Count each level in `DevReport` (replaces `compromisedSpawns`).
* **Facing:** among the 16 directions with ≥ 6 m open, pick the one closest to the nearest enemy *by path*. You then face where the fight will come from (4.03 "facing each other").
* Harness result for this prototype (`Proposed()` in `Program.cs`, TopK 6, 15 m path limit), uniform bots:

  | | Ascent | Breeze | Haven | Icebox | Split |
  |---|---|---|---|---|---|
  | seen one step out: now → proposed | 19% → **0%** | 42% → **0%** | 38% → **2%** | 40% → **0%** | 43% → **2%** |
  | nearest bot path median | 24.0 → 27.1 m | 21.7 → 32.2 m | 22.6 → 26.7 m | 23.8 → 33.2 m | 21.8 → 26.7 m |
  | nearest bot behind you | 34% → 22% | 30% → 13% | 37% → 11% | 36% → 29% | 32% → 16% |
  | within 15 m of the death spot | 23% → 12% | 16% → 5% | 18% → 12% | 17% → 4% | 19% → 14% |

  The ≤3 s-run share rises slightly on Ascent, Haven and Split (3-10% to 8-14%) because the prototype's path limit was 15 m. The 20 m limit above removes that.
* Size: about 150 lines plus about 30 in `BotNav`.

**F-S3. Spread the bots' attention** (`DeathmatchBots.cs`).
* `Begin` (`:82`): reveal the **nearest enemy by path** among you and the other bots. Set `lkp` only if that enemy is you; otherwise use a `noise` goal toward that bot. This matches VALORANT's "all enemies revealed to the respawner".
* `Noise` for player shots (`DeathmatchMode.cs:119`): use 25 m like bot fights. Only idle bots in `Roam` react, with probability 0.5. Bots already in `Hunt` keep their goal.
* Rumors (`:249-258`): choose the target among all combatants, weighted by `1/pathDistance`, with you weighted 2×. That keeps the trainer focused on you without a swarm.
* `UpdateFoe` ignore chance (`:386`): 0.6 → 0.25.
* Size: about 40 lines. Tune with the dev log's "life after spawn".

**F-S4. Protection** (`DeathmatchMode.cs:24`, `:108-118`).
* Change `SpawnProtection` to 3 s. It ends when you fire, or after you have moved 1.5 m from the spawn (closer to VALORANT's launch rule of "ends on move or shoot").
* While you are protected, set `brain.HoldFire = true` for every bot.
* When protection ends, add a new `BotBrain.Reacquire()` that clears `hadLos`, `fireAt` and `alertUntil`, so bots that see you run a fresh reaction and Fitts settle (`OnLosGained`).
* Bots get the same symmetric rule (`Bot.Protected`).
* Size: about 25 lines.

**F-S5. Bind density** (`DeathmatchMode.BotCount`, `:92-97`).
* Allow 2 bots when `nav.Area < 500 m²`. Harness for Bind with 2 bots: seen-one-step 61% → 38% with today's picker, and 13-19% with the proposed one; ≤3 s run 55% → 26-41%.
* Also either enlarge Bind's blockout (B Long / Garden / Hall) or drop Bind from the random DM rotation (`MapMode` constructor, `MapModes.cs:29-30`) until it is bigger. Riot made the same density call in 4.03.
* Size: 5 lines plus a product decision.

**F-S6. First spawn.** Run your first spawn through the new picker as well, or keep `StartFeet` but require bots' first spawns to be ≥ 25 m away by path. Size: about 5 lines.

**F-S7. Dev guard.**
* Log `[dm] spawn you at (x,y,z) yaw Y, nearest path D m, ring-seen R, level L`.
* Port the harness's spawn statistics to a headless dev self-test `--dm-spawncheck`, in the style of `--culture-test` in `Main.cs`. It should fail when ring-seen exceeds 5% on any map except Bind, or 20% on Bind.
* Size: about 150 lines.

## 3. Bot movement

### 3.1 What the code does (`DeathmatchBots.cs`, `Bot.Update`)

* **Moving** (`:189-203`): follow `BotNav` waypoints at `Mover.RunSpeed` 5.4 m/s, advancing at 0.45 m. Rookies jog at 4.6. Veteran and up walk the last 9 m of a Hunt.
* **Acceleration** (`:207-209`): 40 m/s² toward the target velocity. When stopping, braking is per tier: 9 / 15 / 34 / 42 / 50 m/s².
* **Look** (`:222-232`): when not fighting, the bot faces its velocity, or pre-aims the last known position (Veteran and up, within 25 m), or a hold direction.
* **Fighting** (`:173-187`): `want = 0`. Only `Tier >= 3` flips `strafeSign` and moves 0.16-0.26 s sideways every 0.7-1.5 s, and only if `nav.At(feet + side*0.8)` is walkable. Otherwise the bot stands still.
* **Paths** (`BotNav`): grid nodes keep 0.36 m from walls (`:18`) for a 0.34 m body (`Collision.PlayerRadius`). `Smooth` (`:272-288`) string-pulls to the farthest straight-walkable node, using `Straight`, whose shoulder rays sit only 0.06 m outside the body (`:310`).

### 3.2 How a DM player moves

* **Counter-strafe to shoot.** Rifles are accurate only below about 27.5% of max speed (Riot, Patch 3.0 deadzone, already cited in `StrafeDuelMode.cs:17-19`). Players tap the opposite key to stop, shoot a tap or burst, then strafe again (ADAD), sometimes crouching. Riot's netcode article: "firing while moving is inaccurate, so peekers must stop", which gives holders time to react. [Riot, Peeking into VALORANT's Netcode](https://www.riotgames.com/en/news/peeking-valorants-netcode)
* **Peeks:** wide swings, jiggles, shoulder baits and re-peeks. The peeker gets about 40-70 ms of advantage depending on tick rate and fps (same Riot article). The repo's own taxonomy is in `PeekMixMode.cs:30-56` and `docs/mixed_peeks.md`.
* **Movement shape:**
  * Players don't hug walls; they take corners wide to see the angle first ("slicing the pie").
  * They keep the crosshair at head height on the next angle while moving, so they often move sideways to their aim.
  * They accelerate to full speed in about 0.3 s (`Mover.GroundAccel` 18 m/s², frame-counted from a Range recording).
* Sources for the 0.3 s acceleration, 105-160 ms stop and ADAD cadences are already in `StrafeDuelMode.cs:9-25`. Community guides describe ADAD counter-strafing as alternating strafes with a counter-tap before each shot ([Sportskeeda counter-strafing guide](https://sportskeeda.com/esports/valorant-counter-strafing-guide); [Dot Esports guide](https://dotesports.com/valorant/news/a-complete-guide-to-counter-strafing-in-valorant)). Both returned 403/405 when fetched today and are cited from search results.

### 3.3 Root causes and evidence

**M1. Bots stand still in fights** (`DeathmatchBots.cs:173-187`).
* Rookie, Regular and Veteran (tiers 0-2) have `want = Vector3.Zero` for the entire engagement. Their only motion is sliding to a stop at 9-34 m/s².
* Elite and Pro move 0.16-0.26 s out of every 0.7-1.5 s (≈19% of the fight), always alternating sides, about 0.6-0.9 m per step. There are no long strafes, no ADAD runs and no crouch.
* The step is not tied to the brain's burst gaps. Instead `BotBrain.FireShot` delays shots in 50 ms steps while the bot moves faster than 1.5 m/s (`BotBrain.cs:178-181`).
* The step is **skipped entirely** when the 0.8 m point on that side is off the grid. That happened in 18-27% of path samples and 9-31% of first sightings (harness), with no fallback to the other side.
* The class doc (`:13-16`) says "Rookie/Regular slide to a stop and run-and-gun". In reality they slide to a stop and then stand.

**M2. Paths round corners at the wall, and fights begin there** (`BotNav.cs:18`, `:272-288`, `:310`).
* String-pulled shortest paths touch inside corners by construction. With 0.36 m node clearance against a 0.34 m body, the gap is 2 cm.
* Inner waypoints (turns) with a body-wall gap < 0.15 m: Ascent 29%, Bind 7%, Breeze 23%, Haven 5%, Icebox 27%, Split 15%.
* Line of sight is usually gained as a bot comes around a corner. At **first sight** of a random watcher the bot is within 0.5 m of a wall **47% / 26% / 30% / 43% / 32% / 46%** of the time (same map order). Walls cover only 15-21% of the floor (nav nodes within 0.5 m).
* M1 then freezes the bot at that corner for the whole duel. This is the "stuck to the wall" in the report.
* On straight runs bots are not unusually close to walls (lateral position median 0.23-0.29, where 0.5 is the lane centre and 0.25 is what uniformly random positions give). The problem is concentrated at corners, path ends and fights.
* No scraping or stuck bots were found: `MoveAndSlide` clipped velocity in 0-1% of frames, the stuck detector fired 0-2 times in 200 paths, and the dev logs report `stuck 0 inwall 0`.

**M3. Goals and holds sit at walls** (`:330-341`, `:255`, `:349-363`).
* Roam goals are `nav.Random`: uniform over nodes, 13-22% within 0.5 m of a wall.
* Rumor goals are `your feet ± 5 m` in X and Z. 31-43% of them fall inside a wall or outside the arena, and `Nearest` snaps them to a node that touches a wall in 7-21% of cases. That node can be in the next room, because `Nearest` is not path-aware.
* On arrival, 15-45% (by tier) start `Hold` for 1.2-3.5 s at that exact spot.
* Screenshots: `bind_4_zoom.png` shows a bot standing in the inside corner at the base of a wall next to the crate. `bindtop_crop_10.png` and `bindtop_crop_11.png` show a bot parked against Hookah's north wall, then at the crate corner, 1.5 s later.

**M4. NPC locomotion** (`:172`, `:207-209`, `:229`).
* Acceleration is 40 m/s², 0 to 5.4 m/s in 0.135 s, versus 0.3 s for the player and the Strafe Duels / Mixed Peeks bots. Braking runs from 9 m/s² (Rookie, a slide of up to 1.6 m) to 50 m/s².
* Speed is a constant 5.4 m/s.
* The body faces its velocity, so the bot never moves sideways to its aim, never checks the corner it is about to pass, and holds its crosshair along its running line.

**M5. No peeks** (`:162-168`, `:190-203`).
* Hunt walks the path to your last known position. The last 9 m are a slow walk, pre-aimed but straight into your crosshair.
* After losing sight, Elite and up hold 50% of the time; everyone else chases.
* Mixed Peeks already has short, wide, crouch, double, jiggle, shoulder bait, shift-walk and repeek, with per-tier timing (`PeekMixMode.cs:30-56`, `StartAct` `:167`).

**M6. Bot-vs-bot fights look the same.** `UpdateFoe` and `FireAtFoe` (`:367-441`) snap the body to face the foe and inherit M1's freeze.

### 3.4 Fix plan: movement

**F-M1. Extract the Strafe Duels planner into `src/Game/Bots/StrafePlanner.cs`.** This is the largest reusable piece.
* Move these from `StrafeDuelMode.cs`:
  * `Pat`, `Seg` and the tier tables `Weights`, `LongDur`, `TapDur`, `StopDur`, `ShootDelay`, `ChainChance`, `CrouchStopChance`, `RunCap` (`:48-76`);
  * `PickPattern`, `PlanPattern`, `AddStop`, `NextSeg` (`:193-280`);
  * the 1-D integrator from `MoveBot`: player acceleration, two-phase brake 38/24 m/s², 1.06 counter-strafe bonus, and the lane-edge brake look-ahead (`:282-314`).
* API sketch:

  ```csharp
  public sealed class StrafePlanner {
      public StrafePlanner(int tier, Random rng);
      public void Reset(float v0 = 0);
      /// lane = how far (m) the bot may go left (-) / right (+) of its current position along the strafe axis.
      public (float Vel, bool Crouch, bool MayShoot) Step(float now, float dt, float pos, float laneLo, float laneHi);
      public string? LastPattern; // dev log / telemetry
  }
  ```

* `StrafeDuelMode` keeps its behaviour by calling `Step` with its lane, and still owns telemetry (`Event("duel_pattern")`).
* Check that nothing regressed with its existing dev log (`--duel-pattern N`, `[strafeduel]` lines) and `tools/selftest.ps1`.
* Size: about 180 moved lines plus 30 new.

**F-M2. Make DM bots fight like players** (`Bot.Update`, replace `:173-187`).
* When fighting you or a `foe`:
  * Strafe axis = horizontal perpendicular to the line to the enemy.
  * Lane = probe `nav.Straight(feet, feet ± side·d)` for d = 3.5, 2, 1, 0.5 m on each side. Re-probe at each new segment, which is 2-8 cheap calls.
* `vel = side · planner.Vel`; crouch goes to `Body.Crouched`.
* For tiers with `RunAndGun == false` (Veteran+), set `brain.HoldFire = !MayShoot`, which is the Strafe Duels gate. RunAndGun tiers keep shooting at reduced accuracy (`BotBrain.cs:178-182`).
* Use the same gate in `FireAtFoe` for bot-vs-bot fights.
* Every tier moves. The planner's tier weights already go from Rookie's long, slow strafes to Pro's ADAD, A-crouch-D and jiggles.
* Size: about 60 lines.

**F-M3. Player-like locomotion and look** (`:206-232`).
* Replace `40f` and the per-tier brake with `Mover.GroundAccel`, `BrakeHigh 38`, `BrakeLow 24`, as in `PeekMixMode.cs:23`.
* Look while moving: target the next **corner** on the path (the first waypoint where the path turns more than 30°) at head height, or the last known position when known. Movement then becomes sideways to the aim, and the legs follow (`BotCharacter.cs:12-14`).
* Keep the turn-rate limits.
* Size: about 40 lines.

**F-M4. Clearance-aware paths** (`BotNav`).
* At build time, compute a per-node `Clear[n]`: the horizontal gap from the body to the nearest blocking box. This is the harness's `WallGap`; it costs about 8k nodes × ≤231 boxes once per map, a few ms, and is cached with the grid.
* `FindPath(a, b, float clearanceWeight = 0)`: multiply edge cost by `1 + w · max(0, 1 − Clear/1.0)`. DM passes `w = 2`. The default of 0 keeps Defense, Retake, `ControllerPlan` and the Site Clear demo paths unchanged (`GameSession.cs:122`, `DefenseModes.cs:130-257`, `ControllerPlan.cs`).
* `Smooth` with clearance: accept a shortcut only if `Straight` passes with shoulder offset `PlayerRadius + 0.5`. Fall back to the current `+0.06` only where the passage is narrower than about 1.7 m.
* Post-pass: push each inner waypoint along the clearance gradient until `Clear ≥ min(1.0, local half-width)`. This gives wide corner rounding, which is also the natural start of a wide swing.
* Acceptance (harness): turn points at a wall < 5% (now 5-29%); bot within 0.5 m of a wall at first sight within a few points of the floor share (now 26-47% vs 15-21%).
* Size: about 100 lines.

**F-M5. Better goals and hold spots** (`PlanNext` `:330-341`, rumor `:255`, `Arrived` `:349-363`).
* Roam goals: nodes with `Clear ≥ 1.0`.
* Rumor or `lkp` goals: a node on your side of the walls. Flood from your node within 5 m and pick one with `Clear ≥ 0.8`; do not `Nearest` a random offset.
* Holds: pick a spot 1-2 m back from a corner, with `Clear ≥ 0.6`, facing the open angle. Do not hold where the path happened to end.
* Size: about 40 lines.

**F-M6. Peeks while hunting** (new `St.Peek` in `Bot`).
* When the next path corner hides the last known position (no line of sight from the corner waypoint pushed 1.5 m out), run a peek using Mixed Peeks' parameters (`PeekSpeed`, `DoubleGap`, bait timings):
  * Rookie and Regular: short or wide swing.
  * Veteran: plus crouch and double.
  * Elite and up: plus jiggle, shoulder bait and repeek.
* Peek point = the corner waypoint pushed 1.5-2.5 m outward (wide) or 0.4 m (jiggle). Run there, counter-strafe stop, then hand over to F-M2.
* On losing sight, choose a repeek or shift-walk instead of today's "hold 50% / chase straight".
* Size: about 150 lines. Ship after F-M1 to F-M5.

**F-M7. Dev visibility.**
* Add to `DevReport`: fight-time share moving, mean first-sight wall gap, and a pattern histogram.
* Add a `--dm-botcam N` dev flag that attaches the `--envcam` style camera behind bot N. Today's top-down `--envcam` views were too sparse to show strafing.
* Size: about 40 lines.

## 4. Suggested order and acceptance

1. **F-S1, F-S2, F-S4, F-S5.** These address report 1 directly. Accept when the harness and `--dm-spawncheck` show one-step exposure ≤ 5% on the large maps and ≤ 20% on Bind, the nearest-by-path bot ≥ 20 m in at least 90% of spawns, and enemy-behind ≤ 20%. In-game dev runs with `--simaim` (stationary) should show no deaths within 2.5 s of a respawn on the large maps across 3 minutes.
2. **F-M1, F-M2, F-M3.** These address report 2's "not really strafing". Accept when bots of every tier move during at least 50% of fight time and Veteran+ shoot only below `Mover.AccurateSpeed`. `StrafeDuelMode` dev logs must be unchanged.
3. **F-M4, F-M5.** These address "stuck to the wall"; the targets are in F-M4.
4. **F-S3.** Attention tuning: check with `DevReport` life-after-spawn and kill-share numbers.
5. **F-M6, F-M7.** Peeks and dev tooling.

Steps 1-3 together are about 700 lines, mostly new and moved code. The riskiest shared change is `BotNav.FindPath`, which stays opt-in through `clearanceWeight`.

## Appendix A: in-game logs used

`bind.out.log` (Bind, Veteran, 3 bots, stationary simulated player):

```
[dm] you died 1.7s after 1.7s alive (spawn clean, shots 1)        eye (14.00,1.65,14.00)  shot from (20.45,1.65,10.57)
[dm] you died 1.9s ... (spawn NOT clean)                          eye (17.35,1.65,24.85)  shot from (19.72,1.65,11.92)
[dm] you died 1.7s ... (spawn clean)                              eye (-0.15,3.25,0.85)   shot from (1.95,2.20,9.40)
... B Short south spawns: (17.35,24.85) (17.35,23.85) (12.35,24.85) (14.35,24.35), three of them shot from ~(11,14)
... Hookah platform spawns: (-0.15,4.35) (-2.65,1.35) (-0.15,0.85) (0.85,0.35) (1.85,3.85)
[dm] t=100 you 12/12 ... spawnDeaths you 3 bots 7 (spawns not clean 21) botbot 30
```

`ascent.out.log` (Ascent, Elite, 5 bots): life after respawn 13.8, 5.0, 6.2, 3.4, 1.9, 11.7 s. `spawns not clean 0` all run long, while the `[died]` killer distance was 6-33 m.

## Appendix B: screenshots (session scratchpad `...\scratchpad\dm-diag\`)

* `bind_1.png`, `bind_6.png`: two respawns about 75 s apart on the same Hookah platform spot, facing out of the window onto site (the longest sightline).
* `bind_4.png` / `bind_4_zoom.png`: a bot standing in the inside corner at the foot of a wall, by the crate.
* `bindtop_crop_10.png`, `bindtop_crop_11.png` (top-down `--envcam -9,26,-2,0,-90`): a bot pressed against Hookah's north wall, then against the crate corner.
* `bindtop_3.png`: "KILLED BY BOT 2" about 8 s into the match, already the second death.
* `ascent_3.png`: killed from the left (damage arc on the left) while facing a wall corner after a "clean" spawn.
