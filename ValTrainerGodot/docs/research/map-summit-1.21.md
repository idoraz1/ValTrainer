# Summit (B Main → B Site): research notes (1.21)

Summit is VALORANT's 13th standard map, released in patch 13.00 (Act 4, 24 June 2026) and revealed at Masters London.
It is a Radiant training academy (the Yue Yuan Men academy, on the grounds of the monastery where Sage trained) in
the karst mountains of Zhangjiajie, Hunan, China. Riot's internal codename is *Plummet*. It has two sites (A, B), three
lanes and one new mechanic: three **droppable walls** (A, Mid, B). Shooting a switch next to one lowers it for the rest
of the round; it kills anyone underneath, destroys utility, and keeps a small gap at the top. It is in the patch 13.08
competitive pool.

## What ValTrainer builds

One route: **B Lobby → B Main → B Site** (`src/Maps/MapSpots.Summit.cs`, key `summit`).

Why B: guides agree that B is the larger site with "several defensive positions" and the more vertical one (B Tower
high ground, B Drop, the rope from B Gym, the B Wall), while A is small and cramped. B gives the site-clear,
retake, post-plant and anchor drills more distinct angles to hold. B Main is the attackers' main route into it.

## Sources

- valorant-api.com `/v1/maps` (Summit, uuid `756da597-…`): the minimap (`displayIcon`, 1024 px), its transform
  (xMultiplier 7.5e-05, yMultiplier −7.5e-05, xScalarToAdd 0.047401, yScalarToAdd 0.978891) and the 24 official
  callouts. The callouts land inside the right corridors once the transform is applied, which confirms the frame.
  Official callouts: A Lobby, A Main, A Garden, A Site, A Art, A Cave, A Hall, A Link; Mid Top, Mid Tiles, Mid
  Fountain, Mid Bend, Mid Bottom, Mid Window; B Lobby, B Main, B Site, B Tower, B Drop, B Gym, B Trophy, B Link;
  Attacker Spawn and Defender Spawn.
- valorant-spind.de Summit guide (patch 13.08): wall and switch positions (B Wall between B Tower and B Site, switch
  on the site), B Tower reached by rope from B Gym with a silent drop to B Drop, the setting and codename.
- thespike.gg, beebom.com, rankforge.gg, games.gg, keengamer.com guides: layout, site sizes, wall mechanic, theme.
- cgmagonline.com (Masters London reveal): release patch and date.
- The map's splash art (valorant-api.com), only looked at for the colours: white plaster halls, jade-green glazed tile
  roofs, grey stone, green gardens, karst peaks, a gold evening sky.

No Riot image or asset is in the repository: only the numbers derived from the minimap.

## Frame and scale

- 1 minimap px = 1 / (1024 × 7.5e-05) = 13.02 Unreal units = **0.130 m** (the same scale as Haven).
- Authored in the final in-game frame (no `Mirrored()`): origin = the B Main exit into the site (minimap px 125, 450),
  `X = (px − 125) × 0.130`, `Z = (py − 450) × 0.130`. −Z is up on the minimap (north: B Tower, B Gym, the defender
  side), +X is right (B Link, Mid).
- Walls were traced by thresholding the minimap's walkable area and cutting the rest into rectangles, then cleaned
  by hand (diagonal corners as 2–5 steps). They follow the minimap's wall lines within about 0.3 m.
- Cut-offs where the real map goes on are sealed with walls: B Gym east (to the defender spawn), B Trophy and B Link
  (to Mid Bottom), B Lobby south and east (the loop to the attacker spawn and Mid Tiles).

## What is approximate

- **Heights.** The minimap only shows relative height by shade: ground 118, B Tower 145, a strip along B Main's south
  wall 131. B Tower is set to **4.0 m** (a guess that fits a rope climb and a no-damage silent drop), the B Main strip
  to a 0.45 m walkway, walls to 8 m. No in-game screenshots of B were available to check them.
- **B Tower / B Wall.** The Tower is a balcony on the site's north side with a 0.85 m rail. The B Wall is shown raised
  (open, as at round start) and can't be dropped: the wall mechanic isn't modelled. The rope from B Gym isn't
  modelled either, so the Tower can only be held, not reached. The silent drop is a plain 4 m edge onto B Drop.
- **Props.** Crate positions and footprints come from the minimap's prop squares. Their heights (1.1–1.2 m single,
  2.3 m double) are standard VALORANT crate heights, not measured. The two site boxes are rotated about 30° in-game;
  here they are axis-aligned.
- **Callouts not in Riot's list** (used for defender spots): Close box, Close left, Boxes, Corner, Pillar cubby, Wall
  box, Gym door, Behind pillar. Some early (13.00) guides use other names (Triples, Plant, Close Box, B Wall).
- **Look.** Haven's monastery decor with Summit's colours: jade tile caps, friezes and backdrop roofs, white plaster,
  grey stone pavers (CC0 Poly Haven textures already in the project). Summit's waterfalls, karst
  pillars and gardens aren't modelled.

Confidence: the floor plan (wall positions, widths, distances, where B Tower, B Drop, B Gym and B Link are) is as good
as the other maps, because it comes from the official minimap and transform. Heights and the look of the walls are
estimates.
