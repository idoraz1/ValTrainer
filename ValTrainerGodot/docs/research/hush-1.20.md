# Hush (Agent 31): research for 1.20

Hush is VALORANT's 31st agent, a Duelist. She was revealed on 2026-10-18 and released in patch 13.09 (2026-10-27).
A Discord player asked for her ("hush dash and flash is lowk crazy"). Researched on 2026-11-15.

## What is published

Patch 13.09 notes (playvalorant.com, mirrored by Liquipedia) describe the inputs and effects, but give **no numbers**
except the signature's reset. The fandom wiki page returned HTTP 402 and Dot Esports returned 403, so neither could be checked.

| Ability | Key | What the patch notes say | Numbers found |
|---|---|---|---|
| Telekinesis (passive) | – | Picks up weapons and the Spike (on attack), and uses switches, from an increased range | ~36 m range and a ~30 s cooldown, from a pre-release hands-on (turbosmurfs, beebom). The guides call these "close, not exact". |
| Flow | C | Instantly creates an anchor. If Hush is in the air, she briefly levitates in place. After a delay she propels toward the anchor. | 200 credits (one guide, cites "a public agent database"). Guides call it the longest dash in the game but slower than Jett's. No distance, delay or speed was published. |
| Flashbulb | Q | EQUIP, FIRE throws, ALT FIRE prepares, REACTIVATE directs it toward the crosshair. It explodes on any surface or after a delay and flashes everyone looking at it. | 250 credits (same single guide). No charges, windup or blind duration were published. |
| Displacement (signature) | E | Same inputs as Flashbulb. It explodes on any surface or after a delay, pulls all players toward the centre, and concusses them on collision with a surface or a player. | **Resets every two kills** (patch notes). Free (the same guide). No radius, pull strength or concuss time was published. |
| Suspension (ult) | X | Prepared instantly. FIRE sends it forward and you steer it with the crosshair. REACTIVATE or FIRE again detonates it and levitates all players. | **8 ultimate points** (Riot's own cosplay guide, quoted by turbosmurfs). |

The ability names come from the patch notes. The brief's descriptions match the notes. One detail from the brief:
"alt fire sticks the charge to a surface so it launches from there". The notes only say "prepare". ValTrainer models
ALT FIRE as: throw it so it sticks where it lands, then the key launches it.

## What ValTrainer uses (estimates are marked)

Every number below except the Displacement reset and the slots is an **estimate**. Riot hasn't published them. They
were picked to feel like the closest existing abilities (Jett's Tailwind, Yoru's Blindside, KAY/O's flash). Change them
in one place when real values appear: `src/Modes/Agents/HushKit.cs` and `src/Game/Fx/HushFlash.cs`.

**Flow (C), `mobility:hush`**
- 1 charge per round (estimate).
- The anchor goes on the surface under the crosshair, up to 15 m away. With no surface in reach, it goes 15 m out in the air (estimate: "longer than Jett's ~11 m dash").
- 0.4 s delay before the pull (estimate). You hang in the air during it if you're airborne (patch notes).
- Pull speed 18 m/s (estimate: "slower than Jett's ~24 m/s"). The gun is back 0.25 s after arriving (estimate).
- On a floor or ledge top you land on it. On a wall you stop 0.5 m off it.
- At most 4.5 m above your feet (ValTrainer limit, so map walls and roofs stay out of reach).

**Displacement (E), `mobility:hush`**
- 1 charge, back every two kills (**patch notes**).
- 0.6 s equip (estimate). LMB throws it at 18 m/s with light gravity (estimate). It blows on the first surface or after 1.2 s (estimate).
- RMB throws it to stick on the first surface. It waits up to 10 s (estimate).
- The key again sends a flying or stuck charge toward the point under your crosshair, at 22 m/s for up to 1 s, once (estimate).
- The blast has a 0.3 s windup (estimate). It drags every player within 7 m that it can see (estimate) to 1.2 m from the centre in 0.35 s (estimate).
- A wall or another body in the way concusses them: a 2.5 s daze, meaning slower reactions and worse aim (estimate). Everyone else gets a 1 s daze (estimate).
- It drags you too.

**Flashbulb (Q), `flashpeek:hush`**
- 2 charges per round (estimate). 0.8 s equip and 0.8 s back to the gun (estimate, same as Yoru's Blindside).
- LMB throws it at 20 m/s with light gravity (estimate). It pops on the first surface or after 1.1 s (estimate), with a 0.25 s windup (estimate).
- The key again redirects it once toward the point under the crosshair, at 24 m/s for up to 1 s (estimate).
- RMB sticks it to the first surface. It's quiet there (it doesn't blind) for up to 10 s, until the key launches it (estimate).
- Max blind 2 s (estimate), using the same on-screen blind rule as every other flash in ValTrainer.

**Not modelled:** Suspension (no drill: a steered orb that levitates enemies doesn't fit an aim drill yet) and
Telekinesis (no gun or spike pickups in the drills). Both are listed on the Agents screen with no drill.

## Assets

Everything is ValTrainer's own: the sounds are synthesized at runtime in `DuelistSfx` (flow_anchor, flow_pull,
displace_*, flashbulb_*). The visuals reuse the procedural `DuelistVisual` / `DuelistBurst` / `FlashOrb` shaders in
Hush's pink. No Riot art, icons or sounds are used, and no new CC0 assets were added.

## Sources

- VALORANT Patch Notes 13.09: https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-1309
- Liquipedia, Patch 13.09: https://liquipedia.net/valorant/Patch_13.09
- Turbosmurfs, Hush guide (costs, 8-point ult from Riot's cosplay guide, Telekinesis preview numbers): https://turbosmurfs.gg/?p=9035
- Beebom, Hush guide (Telekinesis 30 s / 36–37 m, two-kill reset): https://beebom.com/valorant-hush-guide/
- Reveal coverage named in the request (Sheep Esports returned 403): https://www.sheepesports.com/en/articles/riot-games-reveals-agent-31-hush-as-valorant-s-new-duelist/en, https://win.gg/valorant-new-agent-hush-release-date-and-abilities/, https://www.forbes.com/sites/mikestubbs/2026/10/18/new-valorant-agent-hush-has-telekinesis-powers-to-move-enemies/
- Search summaries (no numbers): Dexerto, esports.gg, Red Bull, ofzenandcomputing, positioniseverything ("no authoritative table of cooldowns, costs, ranges or durations verified").
