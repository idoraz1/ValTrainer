# Abyss and Sunset blockouts (1.21)

Asked on Discord: match VALORANT's competitive pool (patch 13.08: Abyss, Bind, Corrode, Haven, Lotus, Summit, Sunset).
This note covers Abyss and Sunset; `MapSpots.Abyss.cs` and `MapSpots.Sunset.cs` hold the numbers.

## Sources

- Geometry: the official minimaps (`displayIcon`) and their transforms from `https://valorant-api.com/v1/maps`
  (Abyss `xMultiplier` 8.1e-05, Sunset 7.8e-05; callout coordinates for orientation). The PNGs were downloaded to a
  temp folder and traced there; **no Riot image or asset is in the repo**, only the box coordinates derived from them.
- Layout and callouts: the API callouts plus community guides (Beebom, Switchblade Gaming, Red Bull, Sportskeeda map
  guides; patch 11.08 coverage for Abyss B). Heights are estimates (see below): Riot publishes none.

## Scale and frame

- 1 minimap px = 1 / (1024 × xMultiplier) game units: Abyss 12.06 cm, Sunset 12.52 cm.
- Both maps are authored in the final in-game frame (no `Mirrored()`): minimap right = +X, minimap down = +Z, so
  -Z is "up" on the minimap. The origin is the choke.
- Tracing: the minimap's pixels were classed as floor (grey), wall line (opaque white), void edge (semi-transparent
  white) and empty. Empty areas enclosed by wall lines are solid; empty areas connected to the outside of the image
  are void (Abyss only; on Sunset everything outside is solid). The result was rasterised on a 2 px (≈ 0.25 m) grid
  and merged into rectangles. Crop edges where the real map continues are sealed with walls.

## Abyss: B Lobby → B Main → B Site

- Why this route: B is where Abyss's identity shows: the site's south and west edges drop into the void, B Danger
  (a ledge on a pillar across a gap, the risky off-route), B Tower (heaven) above a void edge, B Nest over B Main.
- The void: the floor is a rock slab 8 m thick at **Y = 20 m**; the engine's ground plane (Y = 0) is the bottom of the
  void. Falling off is a 20 m drop (lethal fall damage), and `MapSpot.VoidBelow = 10` makes landing down there a death
  even through spawn protection (`GameSession.FellIntoVoid`). Wall lines on void edges became 0.45 m lips (you can
  step over them and fall, like the real map's open edges). No backdrop buildings (`Theme.Buildings = false`), only
  distant mountains and a near-black ground in the fog.
- Heights (estimates from the minimap's shading): B Tower +2.8 m with a 9-step ramp from B Link, B Nest +1.5 m with
  steps down to the lobby corridor, the site platform +1 m (crouch-jump), walls 6.5 m.
- B Danger: the gaps on either side of the ledge are about 2.4 m and 2.9 m, so you jump them (a running jump covers ≈ 4.8 m).
- Simplified: the 11.08 site cover is approximated from minimap outlines (platform, a tall cover, crates, the two
  "Boxes"); rotated crates are axis-aligned; the south end of B Lobby (on the void edge) is left out and the lobby and
  Mid-hallway cut-off is about 5 m further south than the minimap's edge, so the Deathmatch arena (1366 m², 5 bots)
  meets its spacing targets.

## Sunset: A Lobby → A Main → A Site

- Why this route: A Main is Sunset's signature attack: up the main from A Lobby past the Elbow corner, then around the
  rounded planter wall onto a site watched from A Alley, A Link and its ledge, Back site and the east corridor.
- Heights (estimates): flat ground, the A Link ledge +1 m with steps, crates 1.1–2 m, walls 6 m.
- Simplified: the diagonal Site box is two axis-aligned crates, the diagonal walls are 0.25 m saw-tooth steps, and
  the alley's diagonal stair is left out. A perimeter wall seals the crop.

## Checks

- `--jumpaudit abyss|sunset`: sealed (Abyss: void spots are counted as a deadly fall, not an escape; spots on an open
  edge may hang over by the body radius).
- `--dm-spawncheck`: both maps pass every target.
- `--preview-check` (Site Clear, Retake, every drill): no slits under 0.4 m (one corner-to-corner line on Sunset was
  removed by moving the East corridor defender behind the planter wall).
