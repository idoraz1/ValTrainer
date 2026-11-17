# Lotus and Corrode (1.21)

A Discord player asked for the map pool to match the game. The competitive pool in patch 13.08 (October 2026) is Abyss,
Bind, Corrode, Haven, Lotus, Summit and Sunset. This note covers **Lotus** (`MapSpots.Lotus.cs`) and **Corrode**
(`MapSpots.Corrode.cs`). Like the other maps, each one is a single iconic attack route into one site, at true scale.

## Sources

- **Official minimaps and callouts**, valorant-api.com `/v1/maps`: `displayIcon` (1024×1024), plus `xMultiplier`,
  `yMultiplier`, `xScalarToAdd`, `yScalarToAdd` and the callout list.
  - Lotus (`/Game/Maps/Jam/Jam`): 7.2e-5 / −7.2e-5 / 0.454789 / 0.917752, so **1 px = 0.1356 m**.
  - Corrode (`/Game/Maps/Rook/Rook`): 7e-5 / −7e-5 / 0.526158 / 0.5, so **1 px = 0.1395 m**.
  - A pixel is `px = (gameY·xMul + xAdd)·1024` and `py = (gameX·yMul + yAdd)·1024`. Every Lotus callout and the Corrode
    B-side callouts land on walkable floor. On Corrode, the A Main and A Lobby callouts fall outside the current
    minimap's walkable area, so the A side wasn't used.
- **Layout descriptions:** the VALORANT Fandom wiki (via search: the A rotating door stands between A Main and A Tree,
  and turns 180° in 10 s) and valorant-spind.de's Lotus and Corrode guides (Lotus 12.05 A-site changes: a thicker Lobby
  wall, the Vines jump, the reinforced Tree room wall, an extra room outside A Stairs and no plant on the Link side;
  Corrode is Mont-Saint-Michel turned into a radianite salt mine, released in 11.00, codename Rook). Red Bull, Beebom,
  Liquipedia and Dexerto supplied the callout lists.
- **Heights** come from the minimaps' shading (lighter means higher: Lotus A Top and the Drop ledge, A Root's ramp,
  Corrode B Tower) and from general knowledge of the places. No game files or Riot images are in the repository. The
  minimaps were only downloaded to a temp folder and traced there.

## Tracing

A script thresholds the minimap's alpha channel (walkable = opaque) inside a region of interest. It seals the cut-offs,
flood-fills from the start position so pockets you can't reach become solid, and splits the solid area into
axis-aligned rectangles. Diagonal walls become steps of at most about 0.5 m. The walls are 7 m tall. Props, raised
floors, stairs and the door were then added by hand from the light outlines on the minimap.

Both maps are authored in the final in-game frame (no `Mirrored()`): −Z is up on the minimap, +X is right, and the
origin is the choke.

## Lotus: A Lobby → A Root → A Main → A Site

Why A: it is Lotus's signature attack. A has two ways into the site from one Main (the main lane, or the rotating door
through the Tree room), plus the high ground at Stairs, Top and Drop.

- The origin is the A Main mouth onto the site, between the Stairs wall and the Tree block.
- **A Root:** two low mounds (0.3 m and 0.55 m) stand in for the roots and the ramp.
- **A Rubble:** a pillar and a two-step pile (0.6 m and 1.1 m).
- **A Door:** static, in its open position. The leaf lies along the passage, with a pivot and a lintel at 3.6 m, so both
  halves can be walked.
- **A Link breakable wall:** intact (sealed), and drawn in Trim up to 4 m.
- **The Hut:** a closed 3.4 m room with a roof and a cubby on its north side.
- **The default box** stands against the Hut, and a double box is under Drop.
- **A Top:** +2.4 m, with seven 0.3 m steps down to the site.
- **The Drop ledge:** +2.4 m behind a 1 m railing. Its open east end is the silent drop. The double box below lets you
  climb back up instead of using the rope.
- **A Stairs:** six 0.25 m steps up to a 1.5 m landing toward defender spawn (sealed).
- **Simplified:**
  - The Tree is a trunk only, without a canopy.
  - The 12.05 room outside A Stairs isn't on the API minimap, so it isn't modelled.
  - The Vines and the rope are left out.
  - The real heights of Top and Drop are estimates.
- **Look:** temple ruins in the Western Ghats. Laterite clay plaster, sandstone, teal-painted wood, stone-tile floors,
  hazy sun and green hills. It uses the Bind decor style (teal tile bands, gold metal).

## Corrode: B Lobby → B Main → B Site

Why B: the B side matches the current minimap and its callouts exactly, while the A-side callouts don't. B is also the
clean single-lane execute (Lobby, Main, the Main pocket, then the site), and B Tower over the site is the map's most
talked-about angle.

- The origin is the exit from the Main pocket onto the site, between the Main pillar and the Elbow wall.
- **The salt machine** (north of the plant zone) is 2.4 m high, with a 1.2 m step.
- **The B platform** (south) is +0.9 m, with a 0.45 m step in its notch.
- **B Tower:** +2 m and roofed at 5.2 m, with a 1 m railing over the site and four 0.4 m steps down toward Elbow and Arch.
- **Crates:** one at the Main pillar, the B Main box (stacked), one in the Main pocket, two at Elbow, and a crate and
  barrels in Lobby.
- **Simplified:**
  - The flooded (noisy) walkways aren't modelled.
  - The shapes of the machine and the platform are read from the minimap's outlines, so they are approximations.
  - B Link stops at a seal short of Mid.
- **Look:** Mont-Saint-Michel granite (grey sandstone blocks), cobbled lanes, rusted orange machinery, metal decks, an
  overcast Normandy sky and pale salt flats. It uses the Ascent decor style.

## Checks

- Screenshots of the start view and the key angles of each map, taken with `--campos` and `tools\gshot.ps1 -Client`.
- `--mode counterstrafe --jumpaudit lotus|corrode`: both maps are sealed, and no wall tops or spots outside the floor
  can be reached.
  - Lotus jump-only surfaces: the Drop railing (3.4 m, from the ledge), the box on Top, the box by the Stairs room, the
    default box, the low wall at the Stairs corner and the stacked double box.
  - Corrode jump-only surfaces: the salt machine (both parts), the B Main box, the Tower railing and the Lobby barrels.
- `--preview-check --preview-check-only siteclear` for 400 s on each map: no slits. Every death was through an opening at
  least 0.8 m wide, including the Lobby → A Main → open A Door → Tree room line on Lotus, which is a real sightline.
- `tools\selftest.ps1`, which includes the deathmatch spawn check over every map.
- The first Lotus retake run warned that a retake entry on the Drop ledge could see the spike. That entry was moved
  behind the railing.
