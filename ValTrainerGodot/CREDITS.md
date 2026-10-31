# Credits

ValTrainer is a fan-made aim/movement trainer and is not affiliated with or endorsed by Riot Games. It contains no Riot Games / VALORANT assets.

Every third-party asset bundled in `assets/` is released under **CC0 1.0 Universal (public domain)**: <https://creativecommons.org/publicdomain/zero/1.0/>.
Credit is not required, but these creators deserve it. Thank you!

## 3D models and animation: Quaternius (<https://quaternius.com>, <https://www.patreon.com/quaternius>)
- **Universal Base Characters** (Standard): base bodies, heads, eyes, eyebrows, hair. <https://quaternius.itch.io/universal-base-characters>
- **Modular Character Outfits – Fantasy** (Standard): Ranger outfits used for the agent characters. <https://quaternius.itch.io/modular-character-outfits-fantasy>
- **Universal Animation Library** (Standard), animated with help from Gonzalo Furnier. <https://quaternius.itch.io/universal-animation-library>
- **Ultimate Gun Pack** (July 2019): one model per gun. Pistols, revolver, SMGs, shotguns, assault rifles, bullpups and sniper rifles, plus the Silencer, Scope, Stock and Bipod accessories. <https://quaternius.itch.io/50-lowpoly-guns>

The gun wrapper scenes in `assets/weapons/` scale and orient these models and mount the accessories (assets/MANIFEST.md §5).
The character `.gltf` files in `assets/characters/` are derivative merges of the above: outfit plus base-character head, eyes, brows and hair. Ranger textures were downscaled to 2K.

## Textures and HDRIs: Poly Haven (<https://polyhaven.com>)
| asset | author(s) |
|---|---|
| Plastered Wall 02 | Charlotte Baglioni |
| Painted Plaster Wall | Amal Kumar |
| Clay Plaster | Amal Kumar |
| Sandstone Blocks 08 | Rob Tuytel |
| Red Sandstone Wall | Amal Kumar |
| Concrete Wall 008 | Dario Barresi, Charlotte Baglioni |
| Concrete Floor Worn 02 | Dimitrios Savva |
| Stone Tiles 03 | eye-candy.xyz |
| Cobblestone Floor 06 | Amal Kumar |
| Square Tiles | Charlotte Baglioni |
| Wood Planks | Amal Kumar |
| Wood Floor Worn | Dimitrios Savva |
| Rusty Metal Sheet | Amal Kumar |
| Metal Plate 02 | Rob Tuytel |
| Snow 02 (Icebox) | Rob Tuytel |
| Container Side (Icebox) | Dimitrios Savva |
| Kloofendal 48d Partly Cloudy (Pure Sky) HDRI | Greg Zaal, Jarod Guest |
| Qwantani Late Afternoon (Pure Sky) HDRI | Greg Zaal, Jarod Guest |
| Overcast Soil (Pure Sky) HDRI | Sergej Majboroda, Jarod Guest |

## Audio
- **The Free Firearm Sound Library** ("Prepared SFX Library"; AK-47, AR-15, Tikka T3, Walther PPQ, 1911, Bersa, Ruger Mark III, Smith & Wesson 642, Carl Gustav M45, PPSh, Charles Daly, Winchester Model 12, Benelli Nova, Marlin 336, Norinco SKS, Springfield 1917, Savage 10, Mosin Nagant, Winchester 1894 and Arisaka recordings) by Ben Jaszczak, Brian Nelson, Kevin Heras and Matthew Nanney, posted on OpenGameArt by bart. The single-shot clips were cut and resampled from the original takes. The 1.18 gun clips are also processed (pitch, EQ, tail length, suppressor) by our script `tools/sfx/gun_sfx.py`. <https://opengameart.org/content/the-free-firearm-sound-library>
- **Shotgun Shot** by LeMudCrab (Freesound #163455, CC0), layered into the Judge's shot: <https://freesound.org/people/LeMudCrab/sounds/163455/>
- **Clean Machine Gun Burst** by qubodup (Freesound #482122, CC0), single shots layered into the Odin's and Ares' shots: <https://freesound.org/people/qubodup/sounds/482122/>
- **Gun reload sounds** by SpringySpringo: <https://opengameart.org/content/gun-reload-sounds>
- **Handgun Reload Sound Effect** by zer0_sol: <https://opengameart.org/content/handgun-reload-sound-effect>
- The shotgun, revolver and machine-gun reloads and the Bucky's pump (`assets/audio/weapons/shotgun_*`, `revolver_reload`, `lmg_reload`) are edits by `tools/sfx/gun_sfx.py` of the recordings above (the Model 12 take's own pump and shell sounds; the OpenGameArt reloads). The Odin's spin-up / spin-down (`heavy_spin*.wav`) and the brass in the revolver reload are synthesized by the same script: our own work, CC0.
- **Impact Sounds** (impacts and footsteps) by Kenney: <https://kenney.nl/assets/impact-sounds>
- **Interface Sounds** by Kenney: <https://kenney.nl/assets/interface-sounds>
- The sentinel-setup cues of Site Anchor (alarm, turret, wire, sensor, barriers, vines…) are synthesized in code (`src/Game/Fx/SentinelSfx.cs`); no samples.
- The agent flash sounds in `assets/audio/flash` (throw, flight, windup and pop per agent, Gekko's Dizzy set, and the "flashed" ring) are synthesized by ValTrainer's own generator `tools/sfx/flash_sfx.py`. They are our own work, released as CC0, with no samples and no third-party or Riot audio.

## Engine
Built with the Godot Engine (MIT license): <https://godotengine.org/license>
