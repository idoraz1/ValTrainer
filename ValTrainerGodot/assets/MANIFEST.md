# ValTrainer asset manifest

All third-party assets in `res://assets/` are **CC0 1.0 (public domain)**. No Riot/Valorant, Mixamo or CC-BY(-SA) content.
Imported and inspected with Godot 4.7.2 (.NET). Source size ≈ 197 MB (textures 110 MB, characters 67 MB, HDRI 14 MB, audio 6.2 MB, weapons 0.2 MB).
The import cache (`.godot/imported`) is ≈ 250 MB.

| Folder | Contents | Source / license |
|---|---|---|
| `assets/textures/<id>/` | 14 PBR sets, 2K JPG (diff / nor_gl / arm) | Poly Haven, CC0 |
| `assets/hdri/` | 3 pure-sky HDRIs, 2K `.hdr` | Poly Haven, CC0 |
| `assets/characters/` | 6 rigged character glTFs (4 looks + 2 colour variants), shared textures, BoneMap | Quaternius (Universal Base Characters + Modular Character Outfits – Fantasy), CC0 |
| `assets/characters/animations/` | Universal Animation Library (Standard), imported as **AnimationLibrary** | Quaternius, CC0 |
| `assets/weapons/` | 4 gun FBX + 4 normalized wrapper scenes | Quaternius Ultimate Gun Pack, CC0 |
| `assets/audio/` | gunshots, reloads, impacts, footsteps, UI | Free Firearm Sound Library / OpenGameArt / Kenney, CC0 |

---

## 1. PBR textures (`assets/textures/<id>/`)

Each set has three 2048×2048 JPGs:
- `<id>_diff_2k.jpg`: albedo (sRGB)
- `<id>_nor_gl_2k.jpg`: normal map, **OpenGL convention (+Y)**, which matches Godot
- `<id>_arm_2k.jpg`: packed **R=AO, G=Roughness, B=Metallic**. Use it directly as `ORMMaterial3D.orm_texture`.

Import settings: VRAM compressed (BC/S3TC → `.s3tc.ctex`), mipmaps on, normal maps with `compress/normal_map=1`, `detect_3d/compress_to=0`. All 42 compressed files were verified in `.godot/imported/`.

"Tile size" is the real-world size one texture repeat covers (from Poly Haven). For metre-accurate tiling, set `uv1_scale = surface_size_m / tile_size_m` with `uv1_triplanar` off. Alternatively use triplanar with `uv1_scale = 1 / tile_size_m`.

| id | name | tile size | suggested use |
|---|---|---|---|
| `plastered_wall_02` | Plastered Wall 02 | 2.23 m | clean light plaster walls |
| `painted_plaster_wall` | Painted Plaster Wall | 2.0 m | painted interior/exterior walls |
| `clay_plaster` | Clay Plaster | 2.0 m | warm adobe walls (Bind/Split-like) |
| `sandstone_blocks_08` | Sandstone Blocks 08 | 3.0 m | block walls, boxes |
| `red_sandstone_wall` | Red Sandstone Wall | 2.0 m | warm stone walls |
| `concrete_wall_008` | Concrete Wall 008 | 2.71 m | concrete walls / cover |
| `concrete_floor_worn_02` | Concrete Floor Worn 02 | 2.0 m | default floor |
| `stone_tiles_03` | Stone Tiles 03 | 1.92 m | plaza floors |
| `cobblestone_floor_06` | Cobblestone Floor 06 | 2.0 m | streets |
| `square_tiles` | Square Tiles | 2.4 m | interior tiles |
| `wood_planks` | Wood Planks | 1.5 m | crates, walls |
| `wood_floor_worn` | Wood Floor Worn | 2.0 m | wooden floors |
| `rusty_metal_sheet` | Rusty Metal Sheet | 2.0 m | metal doors/containers |
| `metal_plate_02` | Metal Plate 02 | 2.0 m | metal floor plates, trims |

All 14 requested sets existed on Poly Haven, so no substitutes were needed.

## 2. HDRI skies (`assets/hdri/`)

All are 2048×1024 Radiance `.hdr`, imported as Texture2D (lossless, no mipmaps, to avoid an equirect seam line). Use them with `PanoramaSkyMaterial.panorama`.

| file | look |
|---|---|
| `assets/hdri/kloofendal_48d_partly_cloudy_puresky_2k.hdr` | **default**: midday, partly cloudy, high contrast |
| `assets/hdri/qwantani_late_afternoon_puresky_2k.hdr` | warm sunny late afternoon, clear, high contrast. Chosen over `kloppenheim_06_puresky`, which is a low-contrast sunset. |
| `assets/hdri/overcast_soil_puresky_2k.hdr` | overcast, soft light |

## 3. Characters (`assets/characters/`)

The Standard (free) packs only ship near-nude base bodies (underwear) and two outfits (Peasant, Ranger). I built clean single-file characters by merging glTFs offline. The merge kept the original rig, gave each source its own skin with the shared joints, dropped the vertex-colour mask channels, and fixed broken texture URIs. Textures are shared in `assets/characters/textures/`; the Ranger textures were downscaled from 4K to 2K.

| scene (`load()` as PackedScene) | look | height (T-pose AABB) | Hips (rest) | Head bone (rest) | head mesh AABB | eyes |
|---|---|---|---|---|---|---|
| `res://assets/characters/Agent_Male.gltf` | male, green hooded Ranger outfit | **1.869 m** (hood top) | 0.949 m | (0, 1.600, -0.017) | `Male_Head` y 1.535–1.811 | y≈1.698 |
| `res://assets/characters/Agent_Male_Tan.gltf` | same, tan/brown outfit (`T_Ranger_3_BaseColor`) | 1.869 m | 0.949 m | same | same | same |
| `res://assets/characters/Agent_Female.gltf` | female, green hooded Ranger outfit | **1.798 m** | 0.932 m | (0, 1.550, -0.011) | `Female_Head` y 1.486–1.767 | y≈1.656 |
| `res://assets/characters/Agent_Female_Tan.gltf` | same, tan/brown outfit | 1.798 m | 0.932 m | same | same | same |
| `res://assets/characters/Base_Male.gltf` | muscular male base body + buzz-cut hair (underwear). Good "training bot" body with a material override. | 1.823 m | 0.949 m | (0, 1.600, -0.017) | body is one mesh | y≈1.698 |
| `res://assets/characters/Base_Female.gltf` | female base body + bun hair (underwear) | 1.788 m | 0.932 m | (0, 1.550, -0.011) | body is one mesh | y≈1.656 |

Imported scene files (cache):
- `res://.godot/imported/Agent_Male.gltf-e5faadd4786b602517cbf177a6a6c639.scn`
- `res://.godot/imported/Agent_Male_Tan.gltf-b079148d80eee14e12adad204677cdf7.scn`
- `res://.godot/imported/Agent_Female.gltf-2b0eaeb3b7144b376c973a14bae52b03.scn`
- `res://.godot/imported/Agent_Female_Tan.gltf-df7c4b5592525e89a739e9dc44ef407f.scn`
- `res://.godot/imported/Base_Male.gltf-4db58eba09c7d9453d8dd0513e2f44ed.scn`
- `res://.godot/imported/Base_Female.gltf-20bf255477c52e0e0eb21f4d83818e61.scn`

Always `load("res://assets/characters/X.gltf")`; never reference the `.scn` directly.

**Units and orientation:** metres, feet at y=0, origin between the feet. **The model faces +Z**, which is the glTF/humanoid convention and the opposite of Godot's -Z "forward". When orienting a bot with `LookAt`, use `use_model_front = true` (`LookAt(target, Vector3.Up, true)`) or rotate the model 180° under a pivot. Character left is +X.

### Node tree (identical layout for all six)

```
Agent_Male (Node3D)                       <- scene root, owner of everything
└─ Armature (Node3D)
   └─ GeneralSkeleton (Skeleton3D)        <- unique name: %GeneralSkeleton, 65 bones, motion_scale set by importer
      ├─ Male_Ranger_Acc_Pauldron, Male_Ranger_Arms (2 surfaces: MI_Ranger + MI_Regular_Male = hands),
      │  Male_Ranger_Arms_Bracer, Male_Ranger_Body, Male_Ranger_Body_Belt_1, Male_Ranger_Body_Belt_2,
      │  Male_Ranger_Feet_Boots, Male_Ranger_Head_Hood, Male_Ranger_Legs   (MeshInstance3D, MI_Ranger)
      ├─ Eyebrows (MI_Hair_1)   Eyes (MI_Eyes)   Male_Head (MI_Regular_Male)
```
- **Female agents** use the same layout with `Female_Ranger_*` meshes and `Female_Ranger_Feet`; brows use `MI_Hair_2` and the head is `Female_Head` with `MI_Regular_Female`.
- **Base_Male** has `Eyebrows`, `Eyes`, `SuperHero_Male` (MI_Superhero_Male) and `Hair_Buzzed` (MI_Hair_1).
- **Base_Female** has `Eyebrows`, `Eyes`, `Superhero_Female` (MI_Superhero_Female) and `Hair_Buns` (MI_Hair_2).
- There is no AnimationPlayer in the character scenes; add one yourself (see below).

**Materials:**
- `MI_Ranger`: albedo `T_Ranger_BaseColor` (or `T_Ranger_3_BaseColor` in the Tan variants), normal `T_Ranger_Normal`, and the ORM texture `T_Ranger_ORM` used for both AO and metallic/roughness. The Tan variants rename the material to `MI_Ranger_Tan`.
- `MI_Regular_*` and `MI_Superhero_*` are skin materials. `MI_Hair_*` is used for hair and brows; `MI_Eyes` for the eyes. All are double-sided.
- For a flat training-bot look, override every surface with one material (`MeshInstance3D.material_override`).

### Skeleton (after Godot humanoid retarget, so these are the runtime bone names)

The skeleton is `Armature/GeneralSkeleton`, a unique-name node, so `GetNode("%GeneralSkeleton")` works from the character root. All 65 bones, in index order:

`Root, Hips, Spine, Chest, UpperChest, Neck, Head, LeftShoulder, LeftUpperArm, LeftLowerArm, LeftHand, LeftIndexProximal, LeftIndexIntermediate, LeftIndexDistal, index_04_leaf_l, LeftMiddleProximal, LeftMiddleIntermediate, LeftMiddleDistal, middle_04_leaf_l, LeftLittleProximal, LeftLittleIntermediate, LeftLittleDistal, pinky_04_leaf_l, LeftRingProximal, LeftRingIntermediate, LeftRingDistal, ring_04_leaf_l, LeftThumbMetacarpal, LeftThumbProximal, LeftThumbDistal, thumb_04_leaf_l, RightShoulder, RightUpperArm, RightLowerArm, RightHand, RightIndexProximal, RightIndexIntermediate, RightIndexDistal, index_04_leaf_r, RightMiddleProximal, RightMiddleIntermediate, RightMiddleDistal, middle_04_leaf_r, RightLittleProximal, RightLittleIntermediate, RightLittleDistal, pinky_04_leaf_r, RightRingProximal, RightRingIntermediate, RightRingDistal, ring_04_leaf_r, RightThumbMetacarpal, RightThumbProximal, RightThumbDistal, thumb_04_leaf_r, LeftUpperLeg, LeftLowerLeg, LeftFoot, LeftToes, ball_leaf_l, RightUpperLeg, RightLowerLeg, RightFoot, RightToes, ball_leaf_r`

| role | bone | original (UE-style) name in the source files |
|---|---|---|
| root | `Root` | root |
| hips / pelvis | `Hips` | pelvis |
| spine | `Spine`, `Chest`, `UpperChest` | spine_01/02/03 |
| neck | `Neck` | neck_01 |
| **head** (headshot hitbox) | `Head` (bone origin = base of skull; head mesh extends ~0.21–0.28 m above it) | Head |
| hands | `LeftHand`, `RightHand` | hand_l, hand_r |
| upper arms | `LeftUpperArm`, `RightUpperArm` | upperarm_l/r |
| legs | `LeftUpperLeg`/`LeftLowerLeg`/`LeftFoot`/`LeftToes` (and Right) | thigh/calf/foot/ball |

The Rest Fixer gives every bone a clean rest: the Y axis points down the bone and Z is the character's forward in T-pose. `motion_scale` is the hip height: 0.9491 (male bodies) / 0.9318 (female bodies). Do not change it; it makes the shared animations fit each body.

**How the retarget is configured:** `assets/characters/ual_bone_map.tres` is a BoneMap with SkeletonProfileHumanoid. It is assigned under `_subresources → nodes → "PATH:Armature/Skeleton3D" → retarget/bone_map` in every character `.gltf.import` and in the UAL `.glb.import`, using the default Rest Fixer options (overwrite axis, normalize position tracks, remove unimportant positions, unique node `GeneralSkeleton`). This is the setup Quaternius recommends for Godot. It was required here because the source rigs differ by up to 23° in rest rotations (neck/spine) and in bone lengths.

**Verified:** I played the library on all six characters headlessly. In every animation the head mesh stays rigidly on the `Head` bone (constant 0.076–0.078 m centroid offset). In Idle and Walk the feet sit at y ≈ 0 (±1 cm).

## 4. Animation library (`assets/characters/animations/UAL1_Standard.glb`)

This is the Quaternius Universal Animation Library v3, Standard edition, **without root motion** (in-place). It is imported with `importer="animation_library"`, so `load()` returns an **AnimationLibrary**:
- Imported file: `res://.godot/imported/UAL1_Standard.glb-89988f03cb3ec28f5d4610773035c7d7.res`
- Track paths look like `%GeneralSkeleton:Hips`.
- After cleanup each animation has 58 tracks: one position track (Hips only), 55 rotation tracks and 2 near-constant scale tracks.

Usage (C#):
```csharp
var character = GD.Load<PackedScene>("res://assets/characters/Agent_Male.gltf").Instantiate<Node3D>();
var lib = GD.Load<AnimationLibrary>("res://assets/characters/animations/UAL1_Standard.glb");
var ap = new AnimationPlayer();
character.AddChild(ap);
ap.RootNode = new NodePath("..");            // must resolve to the character scene root (owner of %GeneralSkeleton)
ap.AddAnimationLibrary("ual", lib);
ap.Play("ual/Idle");
```
(An AnimationTree with `anim_player` set works the same way.)

Godot stripped the `_Loop` suffix from the source names and set those animations to loop. **Full list (43), with length in seconds; [L] marks a looping animation:**

- **Basic and locomotion:** A_TPose 2.50, Idle 2.50 [L], Idle_Talking 2.93 [L], Idle_Torch 1.27 [L], Walk 1.33 [L], Walk_Formal 1.33 [L], Jog_Fwd 0.93 [L], Sprint 0.67 [L], Crouch_Idle 2.93 [L], Crouch_Fwd 2.00 [L], Jump_Start 1.33, Jump 2.50 [L], Jump_Land 1.27, Roll 1.47
- **Combat:** Death01 2.40, Hit_Chest 0.33, Hit_Head 0.43, Pistol_Idle 1.67 [L], Pistol_Aim_Neutral 0.17, Pistol_Aim_Up 0.17, Pistol_Aim_Down 0.17, Pistol_Shoot 0.63, Pistol_Reload 1.67, Punch_Jab 0.87, Punch_Cross 1.00, Sword_Idle 1.67, Sword_Attack 1.53, Spell_Simple_Enter 0.53, Spell_Simple_Idle 2.10 [L], Spell_Simple_Shoot 0.50, Spell_Simple_Exit 0.43
- **Misc:** Dance 1.00 [L], Driving 1.67 [L], Fixing_Kneeling 5.20, Interact 2.00, PickUp_Table 0.83, Push 2.67 [L], Sitting_Enter 1.30, Sitting_Exit 1.03, Sitting_Idle 1.67 [L], Sitting_Talking 2.93 [L], Swim_Fwd 1.33 [L], Swim_Idle 3.33 [L]

**Mapping for the trainer:**

| need | animation | notes |
|---|---|---|
| idle | `ual/Idle` (unarmed) or `ual/Pistol_Idle` (armed stance, both arms forward at chest height) | |
| walk | `ual/Walk` | |
| run | `ual/Jog_Fwd` (or `ual/Sprint`) | |
| strafe L / R | **not in the free Standard pack** | Workaround: play `Jog_Fwd`/`Walk` with the body yawed ±90° toward the move direction, and keep the torso and head on the player with a `LookAtModifier3D` (or by rotating `Spine`/`UpperChest`). Alternatively use `Walk` with the body yawed ±60–90°. |
| back | **not included** | Play `ual/Walk` or `ual/Jog_Fwd` backwards (`ap.PlayBackwards("ual/Walk")`). |
| crouch idle / crouch walk | `ual/Crouch_Idle` / `ual/Crouch_Fwd` | |
| death | `ual/Death01` | Falls backwards. In the final lying pose, hood and pauldrons clip up to ~0.1 m below y=0. Fine on solid floors, or lift the model ~0.05 m. |
| hit reactions | `ual/Hit_Head`, `ual/Hit_Chest` | |
| aim / shoot | `ual/Pistol_Aim_Up` / `Pistol_Aim_Neutral` / `Pistol_Aim_Down` are single-pose clips for a vertical aim blend. Also `ual/Pistol_Shoot` and `ual/Pistol_Reload`. | There are **no rifle-specific animations**; use the pistol stance for rifles too. |
| jump | `ual/Jump_Start` → `ual/Jump` → `ual/Jump_Land` | |

The 8-directional locomotion and rifle sets exist only in Quaternius' paid Source edition. UAL 2 (Standard), which I checked, has no strafes or rifle animations either, so I did not include it.

## 5. Weapons (`assets/weapons/`)

These come from the Quaternius Ultimate Gun Pack (2019), which ships only as FBX (plus OBJ/Blend). Godot imports the FBX natively. Each gun is a single MeshInstance3D with 3–5 flat-colour materials and no textures.

**Use the wrapper scenes.** They are normalized to real-world size, with the root at the pistol grip, **barrel along -Z** (Godot forward), +Y up, and a `Marker3D` named **`Muzzle`** at the barrel tip.

| wrapper scene | role | source FBX | length | AABB size (x, y, z) | Muzzle (local) |
|---|---|---|---|---|---|
| `res://assets/weapons/AssaultRifle_AK.tscn` | **Vandal-like** (AK, black stock) | `AssaultRifle_5.fbx` | 0.92 m | (0.033, 0.272, 0.920) | (0, 0.102, -0.649) |
| `res://assets/weapons/AssaultRifle_M4.tscn` | Phantom-like (M4) | `AssaultRifle2_1.fbx` | 0.88 m | (0.059, 0.305, 0.880) | (0, 0.109, -0.614) |
| `res://assets/weapons/SniperRifle_Bolt.tscn` | **Operator-like** (AWP-style bolt action, scope, thumbhole stock) | `SniperRifle_3.fbx` | 1.20 m | (0.074, 0.199, 1.200) | (0, 0.067, -0.880) |
| `res://assets/weapons/Pistol_Compact.tscn` | **Classic-like** polymer pistol | `Pistol_5.fbx` | 0.19 m | (0.029, 0.125, 0.190) | (0, 0.057, -0.152) |

Wrapper tree: `<Root Node3D> ├─ Model (instance of the FBX scene; transform = RotY(+90°) · uniform scale) └─ Muzzle (Marker3D)`. The stock end is at +Z, for example AK z ≈ +0.27 and sniper z ≈ +0.32.

**Raw FBX imports** (`res://assets/weapons/<name>.fbx`):
- Each is a root Node3D with one MeshInstance3D child, whose node transform is scale 100 and rotation -90° about X.
- In the FBX scene frame the guns are huge (AK 5.42 units long). **The barrel points +X** (muzzle at max X), +Y is up, and the origin is near the pistol grip.
- Imported scenes:
  - `.godot/imported/AssaultRifle_5.fbx-f7d9fef2a102b6b65d9f4fad2f22504c.scn`
  - `.godot/imported/AssaultRifle2_1.fbx-576f0bee0037ac168597d5a12c9bd439.scn`
  - `.godot/imported/SniperRifle_3.fbx-438079b3ada6aa8afdc1a3916c18e401.scn`
  - `.godot/imported/Pistol_5.fbx-adedefc3909281bb9f2cf1bea5b66a4c.scn`

**Materials** (FBX import gives roughness 1.0 / metallic 0, so everything is matte):
- AK: Black #28292a, DarkMetal #373736, Metal #414241
- M4: Main #22272a, MainDark #191f27, MainLight #2a2f33
- Sniper: Green #4d5449, Black, DarkMetal, Glass #070e1c, Grey #333435
- Pistol: Metal, Black, LightMetal #555659

For a nicer look, override the surfaces in code with roughness ~0.4–0.6 and metallic ~0.6 for the metal surfaces.

**Holding a weapon on a bot:**
- Add a `BoneAttachment3D` with `bone_name = "RightHand"` under `%GeneralSkeleton`, then add the wrapper as its child with this local transform:
  ```csharp
  new Transform3D(new Basis(new Vector3(0.0467f,-0.0217f,-0.9987f), new Vector3(0.9817f,0.1856f,0.0419f), new Vector3(0.1844f,-0.9824f,0.0300f)), new Vector3(0.005f, 0.065f, 0.003f))
  ```
- With the `Pistol_Idle` / `Pistol_Aim_*` poses this points the barrel along the character's forward (+Z) with the grip in the palm. The rotation is identical for all characters; the position varies by ±1.5 cm.
- Rifles held this way use a one-hand pistol stance, so expect the stock to clip near the chest.

## 6. Audio (`assets/audio/`, 6.2 MB)

| path | content | source |
|---|---|---|
| `audio/weapons/rifle_ak_shot_01..03.wav` | AK-47, single shot, near distance (Vandal) | Free Firearm Sound Library (C_28P) |
| `audio/weapons/rifle_ar_shot_01..02.wav` | AR-15, single shot, near distance (Phantom) | Free Firearm Sound Library (D_32P) |
| `audio/weapons/sniper_bolt_shot_01..02.wav` | Tikka T3 .30-06 bolt action, single shot, with long outdoor tail (Operator) | Free Firearm Sound Library (W_29P) |
| `audio/weapons/pistol_shot_01..03.wav` | Walther PPQ 9 mm, single shot (Classic) | Free Firearm Sound Library (X_39P) |
| `audio/weapons/rifle_reload_airsoft.wav`, `gun_reload_airsoft.wav` | magazine reloads (airsoft recordings), ~1.6 s | OpenGameArt "Gun reload sounds" by SpringySpringo |
| `audio/weapons/pistol_reload.wav` | handgun mag drop, insert and slide rack, 1.6 s | OpenGameArt "Handgun Reload Sound Effect" by zer0_sol |
| `audio/impacts/impact*.ogg` (105 files) | bullet-impact / surface hits: Metal, Plate, Wood, Glass, Soft, Punch, Mining, Bell, Tin, Generic. `impactBell_heavy_*` and `impactMetal_light_*` work well as hit/headshot markers. | Kenney Impact Sounds |
| `audio/footsteps/footstep_{carpet,concrete,grass,snow,wood}_000..004.ogg` | footsteps | Kenney Impact Sounds |
| `audio/ui/*.ogg` (100 files: click, select, confirmation, error, toggle, tick, …) | menu / UI sounds | Kenney Interface Sounds |

- **Gunshot WAVs** are 48 kHz / 16-bit stereo, 1.40 s each. Each is one shot that starts ~4 ms in, with a 0.25 s fade-out at the end, peak-normalized to -1 dBFS.
- They were cut from the library's 96 kHz / 24-bit multi-shot takes, so each file is a single shot ready for repeated firing. Echo tails are natural (outdoor range).
- Godot imports WAVs as AudioStreamWAV and OGGs as AudioStreamOggVorbis, both with default settings.

## 7. Known limitations

- No strafe-left/right or backwards animations, and no rifle animations, in the free UAL. See the workarounds in section 4.
- The Base_* bodies wear underwear only. They are best used with a flat material override (training bot look) or not at all.
- `T_*_Normal.png` (character) files are large RGBA PNGs (~4 MB each). They import VRAM-compressed, so runtime cost is normal.
- Gun materials are flat and matte (low-poly style), not PBR-textured.
