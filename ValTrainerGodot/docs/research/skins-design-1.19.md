# Gun skins: research and design (v1.19)

Status: design only. No game source was changed.

Community ask (Discord): *"It would be fun if we could have skins to add on our guns!!! ... Not actual valorant vandal
skins because of copyright, but having something similar would be awesome."*

Hard rule: **no Riot assets and no look-alikes of specific Riot skins.** Every skin here is procedural, made for
ValTrainer: a shader plus numbers. There are no textures, no downloads and no new third-party assets, so CREDITS only
needs a line saying the skins are original procedural finishes.

---

## 0. Summary of decisions

- **Skins are viewmodel-only.** They apply to the first-person gun (live drills, drill-preview demos and the Rewind's
  3D view) and to the new skin preview. **Bots keep the factory look.** Telemetry, replays (`.vtr`), stats and
  analysis don't change.
- **One new shader variant, no textures.** `viewmodel_skin.gdshader` is the existing viewmodel shader (same
  re-projection, depth squeeze, magazine clip box, muzzle-flash fill and rim) with one procedural pattern chosen by a
  uniform. It follows the `surface.gdshaderinc` / `surface_lite.gdshader` precedent: a shared `.gdshaderinc` and
  `#define` variants. Solid-colour skins don't use it at all: they go through today's `VmMaterials.Make`, so they cost
  nothing extra.
- **Object-space (gun-space) patterns, not UVs.** The Quaternius models have **no usable UVs** (§1.4). Patterns are
  evaluated in *gun space, in metres*, so a camo blot is the same size on a Classic as on an Odin, and it continues
  across attachments (silencer, bipod, stock, scope). 3D patterns use solid noise (no projection). 2D patterns (weave,
  hex, stripes) use **dominant-axis box projection**: one evaluation, not three. On these flat-shaded low-poly models,
  the projection seams fall on the hard edges anyway.
- **Surface roles from the FBX material names** that `VmMaterials` already switches on: **Body** (polymer and wood),
  **Trim** (receiver metal), **Hardware** (barrel, bolt, iron sights, small parts) and **Glass**. A skin paints Body,
  usually Trim, and leaves Hardware (where the sights are) and Glass at factory. That keeps sights readable and scope
  glass dark.
- **Catalogue: 16 original skins plus Factory** (§3). Each one is data: a `GunSkin` record with a stable id. Unknown ids
  fall back to Factory.
- **Persistence:** `settings.json` gets `GunSkins` (gun name → skin id) and `GunSkinAll` (the skin for every gun
  without its own entry). Old builds ignore both fields.
- **UI:** a `SkinPicker` overlay (built in code, like `GunPicker`). On the left are the 20 buyable guns in buy-menu
  columns, in the centre a live 3D preview (INSPECT turntable or IN HAND first-person), and on the right a grid of
  skin swatches. Its buttons are APPLY TO ALL GUNS, RESET GUN and RESET ALL. It opens from a new main-menu **SKINS**
  button, the gun picker's footer, Settings → General → "Gun skins", and the drill preview (**K**).
- **Chamber's Headhunter and Tour De Force keep their gold.** They are ability guns, not buyable.

---

## 1. Code map (as of v1.18.0, `bd24c8c`)

### 1.1 First-person viewmodel: model load and materials
| What | Where |
|---|---|
| Entry point `Viewmodel.Create(kind, leftHanded)`. It resolves `WeaponLooks.For(kind)` → `look.Base` (whose model and pose), `actual` = gun held. Dev `--vmkind` overrides. | `src/Game/Weapon/Viewmodel.cs:56-65` |
| Callers: drills `AdsSession.cs:51`, ability drills `AbilitySession.cs:30`, Rewind 3D `Replay/ReplayStage.cs:395-403` (`SetWeapon`, from `ReplayHeader.Weapon`) | |
| `Build()`: `mats.Gold = look.Gold` (Chamber); `GD.Load<PackedScene>(pose.Scene).Instantiate()` → node "Gun" under "Pivot"; `Muzzle` marker | `Viewmodel.cs:151-164` |
| **The re-material loop.** For every `MeshInstance3D` under the gun scene (model **and** `Attach_*` accessories), for every surface: `src = mesh.SurfaceGetMaterial(i)`, `name = src.ResourceName` (the FBX material name: "Black", "Metal", "Glass", …), `col = src.AlbedoColor`, then `mats.ForGunSurface(name, col)`. It sets the reload clip box (`clip_min/clip_max` in *mesh-local* space via `toMesh = (gun⁻¹ · mesh)⁻¹`), then `mi.SetSurfaceOverrideMaterial(i, m)`, and keeps the material in `gunMats`. | `Viewmodel.cs:166-187` |
| `Configure()`: visual layer 20 (`VisualLayer = 1<<19`, so decals skip it), no shadows, GI off | `Viewmodel.cs:19-20, 143-149` |
| Gloves, sleeves, accent (own materials via `mats.Make`) | `Viewmodel.cs:189-202` |
| Spare magazine for reloads (`VmHands.Magazine`, mats `magBody`/`magPlate`, dark polymer) | `Viewmodel.cs:204-209` |
| Muzzle flash (`MuzzleFlash.Create(mats, …)`), shell pool (brass) | `Viewmodel.cs:211-229` |
| FOV changes at runtime → `mats.SetFov`; flash fill every frame → `mats.SetFlash` | `Viewmodel.cs:252-257` |
| `VmMaterials`: shader paths; `Make(albedo, metallic, roughness, specular, detail, rim, detailScale)` creates **one `ShaderMaterial` per surface** and registers it in `lit`/`all` so FOV and flash updates reach it | `src/Game/Weapon/VmMaterials.cs:12-38` |
| `ForGunSurface(name, src)`: per-name finishes. Gold branch for Chamber (`main/mainlight/metal/lightmetal/grey` → gold; `darkmetal/maindark` → dark gold; `black/green` → bronze polymer). Default branch: `glass` (dark blue, gloss), `green`, `black`, `black2`, `wood`, `darkwood`, `main`, `maindark`, `mainlight`, `darkmetal`, `metal`, `lightmetal`, `grey`, fallback | `VmMaterials.cs:50-87` |
| `SetFov` (clamps 40–110°, sets `vm_focal` on all), `SetFlash` (sets `flash` on `lit`) | `VmMaterials.cs:89-106` |
| **Shader** `viewmodel.gdshader`: `render_mode … diffuse_burley, specular_schlick_ggx` (l.8); uniforms albedo/metallic/roughness/specular/detail/detail_scale/rim/emission (l.10-18); `vm_focal`, `vm_depth = 0.01`, `flash`, `clip_min/max/on` (l.19-24); `varying obj_pos` (l.26, mesh-local, **not metres**); hash + 3D value noise (l.28-39); `vertex()` re-projects with its own focal length and squeezes depth to the near 1 %, with reverse-Z detection, so it works on Forward+ and Compatibility (l.41-56); `fragment()`: clip-box discard (l.59-61), detail noise wear (l.65-71), rim + flash fill in `EMISSION` (l.76-80) | `assets/shaders/weapon/viewmodel.gdshader` |
| `WeaponLook` record (`Base`, sounds, `Gold`, `Sight`, `ScopeStyle`, `Suppressed` …) and the per-gun table; `For(k)`; `PoseOf(k)` | `src/Game/Weapon/WeaponLooks.cs:22-41, 60-107, 113-136` |
| `VmPose.Scene` per gun (wrapper `.tscn` paths) | `src/Game/Weapon/VmPoses.cs:120-365` |
| Wrapper scenes: `Root › Model (FBX instance, RotY 90°, uniform scale ≈0.17 rifles / 0.104–0.15 handguns+SMGs) › Muzzle`, plus `Attach_Silencer` (`Pistol_Suppressed`, `SMG_Suppressed`), `Attach_Stock` (`AssaultRifle_Carbine`), `Attach_Bipod` (`Bullpup_HMG`), `Attach_Scope` (`AssaultRifle_Scoped`) | `assets/weapons/*.tscn` |
| Dev test hook: `--vmtest --vmkind --vmview side|left|top --vmads --vmfire --vmreload --vmshots T --vmout prefix --vmcycle all|A,B [--vmevery S]` (contact sheets), **`--vmbench`** (GPU ms with the viewmodel hidden vs shown) | `src/Game/Weapon/VmTest.cs:8-20, 45-82, 178-200` |

### 1.2 Bots and other characters
- `BotCharacter` always instantiates `BotAssets.Rifle` (the AK wrapper `AssaultRifle_AK.tscn`, `BotAssets.cs:34`) on a
  hand mount (`BotCharacter.cs:147-153`). `BotAssets.RifleFor(kind)` (`BotAssets.cs:39-41`, AK or M4) exists but has
  no caller today.
- Materials: `SetupGunMeshes` → `SetupMesh(gun: true)` (`BotCharacter.cs:184-205`) replaces each surface with
  `BotAssets.Base(m, stencil, gun)` (`BotAssets.cs:170-192`). That is a cached **`StandardMaterial3D` duplicate** of
  the FBX material (roughness 0.45, metallic 0.55, stencil write for outlines), plus an additive fresnel/outline
  `MaterialOverlay`. This path does not use the viewmodel shader. Skins don't touch it (§2.6).
- `ReplayPuppets`/`ReplayStage` draw enemies through the same bot assets. The player's gun in the Rewind is a real
  `Viewmodel` (above).

### 1.3 Picker and buy menu previews
- `GunPicker` (`src/UI/GunPicker.cs`) is a full-rect overlay `Control` built in code, not a `.tscn`. It has 6
  buy-menu columns (`Columns`, l.31-35), `GunTile` buttons (l.430-500), a detail strip and a footer (DRILL DEFAULT /
  USE FOR EVERY DRILL / hint / DONE, l.241-282). `OpenForDrill` / `OpenPreferred` / `OpenForGroup` (l.106-164).
  `CloseOn` (l.51) is set only by the in-drill buy menu (`Game/BuySession.cs`). Keys are handled in `_Input`
  (l.390-418).
- **There is no 3D preview today.** `GunIcons` (l.589-) renders each gun's wrapper scene **once** in a hidden
  `SubViewport` (`OwnWorld3D`, `TransparentBg`, 768×288, MSAA 4×, an orthographic side camera), with **all
  surfaces overridden by one unshaded white `StandardMaterial3D`**. It caches the result as an `ImageTexture` and
  tints it when drawn (l.619-670). Silhouettes are therefore skin-agnostic and stay as they are. The pattern
  (SubViewport + wait 3 × `FramePostDraw` + `GetImage`) is reusable for skin swatches.
- `DrillPreview` already hosts a live 3D `SubViewport` with a demo `GameSession` (`DrillPreview.cs:318-326`). The
  demo's viewmodel comes from `AdsSession`, so it shows the skin for free.

### 1.4 Mesh UVs: unusable, so no UV-based skins
Checked in `assets/weapons/*.fbx` (binary FBX: count of `LayerElementUV` nodes) and in the pack's OBJ sources
(`…\scratchpad\gunpack\Ultimate Gun Pack - July 2019\OBJ`):
- **No UV layer at all** in 23 of the 26 FBX files used (AK/Vandal, every pistol, revolver, shotgun, sniper, SMG,
  bullpup, Bipod, Silencers, Scope, Stock).
- `AssaultRifle2_1/2/3` (Phantom, Bulldog, Warden) have a UV layer, but it is degenerate: of 1201 `vt` entries in
  `AssaultRifle2_1.obj`, 641 are `(0,0)`, 114 sit on one point `(0.9327, 0.9488)`, and the rest are on the corners
  `(0|1, 0|1)`. That is a palette lookup, not an unwrap.
- The models are flat-shaded low-poly (530–1490 vertices). Each surface is one flat colour (`Kd` 0.01–0.11) named
  from a shared palette: `Black, Black2, DarkMetal, Metal, LightMetal, Grey, Green, Glass, Wood, DarkWood, Main,
  MainDark, MainLight`.

**Consequence:** every patterned skin must be **object-space**: solid 3D noise, or box projection on the dominant
axis
of the gun-space normal. Re-unwrapping 26 models in Blender is possible, but it is not worth it. It would add
textures and import churn, and the faceted style suits procedural finishes.

### 1.5 Settings, storage, UI conventions
- `AppSettings` (`src/Core/AppSettings.cs`) is a plain class serialised by System.Text.Json with
  `IncludeFields = true` (l.59). Public fields persist automatically, and unknown fields are ignored on load, so
  downgrades are safe. `Save()` is a no-op when `ReadOnly` (l.94-102). `Main` sets `ReadOnly = !SavesData`, and dev
  runs write only with `--data-dir … --write-data` (`Main.cs:38, 74`).
- The gun picker's storage is `AppSettings.PreferredWeapon` and `WeaponPicks` (mode key → gun name) (l.36-41), with
  helpers in `src/Core/WeaponPicks.cs` (`Resolve/PickFor/Preferred/Remember/SetPreferred`). Unknown names are
  skipped, never an error. Round-trip checks live in `src/Core/CultureCheck.cs:562-572`, which `tools/selftest.ps1`
  runs.
- UI is built entirely in C#, with no `.tscn` screens. Full screens derive from `ScreenBase` (`src/UI/Widgets.cs:313-`).
  It rebuilds the whole tree on window resize; `K = UiTheme.Fit(size)` is the 1080p-relative scale, capped at a
  1600-unit design width. Esc calls `Back()`. Overlays such as `GunPicker` are a plain `Control` with
  `SetAnchorsAndOffsetsPreset(FullRect)`, `ZIndex = 10`, a dark `ColorRect` scrim, a `CenterContainer` and a `VPanel`
  (Title, Caption, K, Fill). Widgets: `VButton` (`Look.Primary/Secondary/Select`), `VPanel`, `SectionLabel`, `DrawBox`
  (draw callback), `InfoRow`; `Gfx.*` text and plate helpers.
- Theme (`src/UI/UiTheme.cs`): VALORANT-like palette `Bg #0F1923`, `Panel`, `Accent #FF4655`, `Text #ECE8E1`, `Dim`,
  `Faint`, `Good`, `Warn`, `Teal`. Fonts are system fonts: `Body` = Segoe UI 600, `Hud` = Bahnschrift, `Display` =
  Bahnschrift SemiBold Condensed, and the letter-spaced `HudWide`/`DisplayWide`. `UiTheme.Fs(px, k)` sets font sizes;
  `HoverSound()` and `ClickSound()` play UI sounds.
- Main menu nav (`src/UI/MenuScreen.cs:245-284`): LOCK IN, AGENTS, COACH, SETTINGS, STATS, QUIT. A `compact` mode
  (×0.86 widths, 18 px font) applies when `width/k - 96 < 1800`.
- Settings → General has a "Preferred gun" `Selector` that opens `GunPicker.OpenPreferred` (`SettingsScreen.cs:450-462`).
- Dev flags: `--screen settings|about|keybinds|stats|lockin|agents` (`Main.cs:168-177`), `--window WxH`,
  `--render-size WxH` (`Main.cs:146-149`), `--preview <key>` + `--preview-answer gun:Phantom` (`DrillPreview.cs:226-250`),
  `--gun <Name>` (`GameSession.cs:95, 307`).

### 1.6 Renderer and performance constraints
- `project.godot`: **Forward+** (`renderer/rendering_method="forward_plus"`), Vulkan → D3D12 → **OpenGL 3.3
  Compatibility** → ANGLE fallback chain. `SysInfo.IsCompatibility` detects the fallback, and then the first-run
  quality is Low (`SysInfo.cs:69-80`). So any skin shader **must compile and look right on GL Compatibility**: no
  compute, no `textureQueryLod`, no SSBOs. `fwidth`, `TIME`, loops with constant bounds and `#include` all work there.
- Quality presets (`AppSettings.Quality`, default **1 = Medium**; `World/WorldLighting.cs`):
  - **Glow only on High and Ultra** (`env.GlowEnabled = q >= 2`, l.152; HDR threshold 1.0, l.47). On Low and Medium
    an emissive accent does **not** bloom, so it must read through colour alone. On High and Ultra it blooms once
    emission > 1.
  - **No MSAA in the 3D viewport** (`ApplyViewport`, l.222). Post-AA: Low none, Medium FXAA, High and Ultra SMAA.
    There is **no TAA**. High-frequency procedural patterns will shimmer and moiré as the gun bobs, unless edges are
    filtered with `fwidth` and detail fades out when a cell gets close to a pixel.
  - Low drops sky reflections (`ReflectedLightSource = Disabled`, l.159). Metallic skins then lose their reflections
    and look dark, so the Low variant should lower metallic and raise albedo.
  - The world has a dedicated cheap shader variant on Low (`World/SurfaceLib.cs:66-70`: `surface_lite.gdshader` =
    `#define LITE…` + `#include surface.gdshaderinc`). That is the precedent for a skin "lite" variant.
- Budget reference: a GTX 1050-class GPU, and the user's **Quadro P2000** (Pascal, ~3 TFLOPS, about a GTX 1050 Ti).
  The viewmodel covers about 8–20 % of a 1080p frame. A pattern costing about 60–120 extra ALU ops per pixel on that
  area is ≈0.05–0.1 ms on this class of GPU (estimate). **Budget: ≤ +0.15 ms GPU on Medium**, measured with
  `--vmbench` (§6).
- **Shader compile hitches.** Every distinct shader is a new pipeline. Using **one** skin shader driven by uniforms,
  not one shader per skin, means at most one more pipeline set. The viewmodel is built at session start, before the
  countdown, so a first-use compile is hidden there. Prewarm it once when the SkinPicker opens.

---

## 2. Design

### 2.1 Surface roles
`VmMaterials.RoleOf(string fbxName)`:

| Role | FBX names | Skinned? | Why |
|---|---|---|---|
| **Body** | `black`, `black2`, `green`, `wood`, `darkwood`, `main`, `maindark` | yes: the pattern | furniture, stocks, handguards, magazines, bullpup shells. The bulk of every gun (e.g. AK: Black = 262 of 616 faces; Phantom: Main + MainDark = 842 of 913) |
| **Trim** | `darkmetal`, `grey` | per skin: a solid finish, or the pattern if `PatternOnTrim` | receivers, sniper bodies (`Grey`), revolver frame |
| **Hardware** | `metal`, `lightmetal`, `mainlight` | **factory**, unless the skin sets `Hardware` | barrels, bolts, slides' small parts, **iron sights, rails** (Phantom's `MainLight` = 71 faces = rails and sights). Keeping it factory keeps sights readable |
| **Glass** | `glass` | **never** | scope lenses (Operator, Marshal, Outlaw, Warden's `Scope_3`) |
| unknown name | anything else | treated as Trim | future models |

Chamber (`look.Gold`): skins are skipped and gold stays (`Viewmodel.Build`). The same goes for anything not in
`Weapons.Buyable`.

### 2.2 Data: `GunSkin` catalogue (`src/Game/Weapon/GunSkins.cs`, new)
```csharp
public enum SkinPattern { Solid = 0, Split = 1, Carbon = 2, Blot = 3, Pixel = 4, Stripe = 5, Heat = 6, Brushed = 7,
                          Damascus = 8, Hazard = 9, Hex = 10, Flake = 11 }   // explicit numbers: shader ids

public sealed record SkinFinish(Color Color, float Metallic, float Roughness, float Specular = 0.5f);

public sealed record GunSkin(
    string Id, string Name, string Blurb, SkinPattern Pattern,
    SkinFinish A, SkinFinish? B = null, SkinFinish? C = null, // pattern colours (B/C unused by Solid)
    SkinFinish? Trim = null,          // null = same as A (solid)
    SkinFinish? Hardware = null,      // null = factory
    float Scale = 0.05f,              // pattern cell size, METRES in gun space
    float Wear = 0.4f,                // existing `detail` noise amount
    Color? Glow = null, float GlowEnergy = 0f, float Anim = 0f) // emissive accent (Hex), 0 = static
{
    public bool PatternOnTrim { get; init; }
    public bool Animated => Anim > 0f;
}

public static class GunSkins
{
    public const string FactoryId = "factory";
    public static readonly IReadOnlyList<GunSkin> All;          // menu order (§3)
    public static GunSkin? Get(string? id);                     // null for factory/unknown
    public static GunSkin? For(WeaponKind gun, AppSettings? s = null); // per gun → GunSkinAll → null; dev --skin wins
    public static void Set(WeaponKind gun, string? id);         // null/"factory" removes the entry; Save()
    public static void SetAll(string? id);                      // GunSkinAll = id, clears GunSkins; Save()
    public static void ResetAll();
    public static event Action? Changed;                        // live viewmodels re-skin
    static readonly string? DevSkin = CmdLine.DevAfter("--skin");
}
```
Ids are stable, lowercase ASCII, and never renamed. Their display names are separate and can change.

### 2.3 Shader: `viewmodel_skin.gdshader`
Refactor `viewmodel.gdshader` into `viewmodel.gdshaderinc`, holding the uniforms, `hash13`/`vnoise`, `vertex()`,
the clip discard, rim and flash. Then:
- `viewmodel.gdshader` = `shader_type spatial; render_mode …; #include "viewmodel.gdshaderinc"`. The output is
  byte-identical to today: verify with the before/after contact sheet in §6.
- `viewmodel_skin.gdshader` = `#define SKIN` + include.
- `viewmodel_skin_lite.gdshader` = `#define SKIN` + `#define SKIN_LITE` + include. This variant is for Low: one
  noise octave, no flake sparkle, no hex animation, metallic ×0.6 because Low has no sky reflections.

Extra uniforms under `#ifdef SKIN`:
```glsl
uniform int   skin_pattern = 0;
uniform vec4  col_a : source_color; uniform vec4 col_b : source_color; uniform vec4 col_c : source_color;
uniform vec3  mr_a;  // metallic, roughness, specular of A   (same for mr_b, mr_c)
uniform vec3  mr_b;  uniform vec3 mr_c;
uniform float skin_scale = 0.05;          // metres per cell
uniform mat4  skin_xform;                 // mesh-local -> gun space (metres): Viewmodel sets it per surface
uniform vec2  gun_z;                      // gun-space z range (muzzle .. stock) for gradients / splits
uniform float skin_seed = 0.0;            // per gun, so two guns' camo isn't identical
uniform vec4  glow_color : source_color;  uniform float glow_energy = 0.0; uniform float anim = 0.0;
varying vec3 gun_pos; varying vec3 gun_nrm;
```
- `vertex()`: `gun_pos = (skin_xform * vec4(VERTEX,1)).xyz; gun_nrm = normalize(mat3(skin_xform) * NORMAL);`.
  In Godot's `vertex()`, VERTEX and NORMAL are model-space. Then the existing re-projection runs unchanged.
- `fragment()`: `pattern(gun_pos, gun_nrm)` returns weights `(wa, wb, wc)` plus an optional glow mask. ALBEDO,
  METALLIC, ROUGHNESS and SPECULAR are blended from the A/B/C finishes. The existing wear noise is applied on top, and
  rim, flash and glow go into EMISSION.
- Box projection for 2D patterns: `vec3 a = abs(gun_nrm); vec2 uv = a.x > a.y && a.x > a.z ? gun_pos.zy : (a.y > a.z
  ? gun_pos.xz : gun_pos.xy);`. Barrel-axis (z) stretch is natural for stripes.
- **Anti-aliasing (required):** every hard edge is `smoothstep(-w, w, d)` with `w = fwidth(d)`. For periodic
  detail, `float f = fwidth(uv.x)/skin_scale` fades the pattern to its average colour as `f` → 0.5. That stops weave,
  hex and flake from crawling under FXAA or no AA.
- Emission: `glow_energy` ≤ 1.5. Glow stays off the top 15 % of the gun-space height so it never sits in the ADS sight
  picture, and animation stays slow (≤ 0.5 Hz, low contrast).
- Per-pattern recipes (cost in value-noise lookups; each lookup = 8 hashes):

| Id | Pattern | Recipe | Cost |
|---|---|---|---|
| 0 | Solid | (doesn't use this shader: `VmMaterials.Make`) | 0 |
| 1 | Split | `t = (gun_pos.z - gun_z.x)/(gun_z.y-gun_z.x)`; A behind `split`, B in front, a 4 mm C line at the border | 0 |
| 2 | Carbon | box-projected 2×2 twill: `checker(floor(uv/s))` × `abs(sin)` thread shading; A/B two near-blacks, low roughness 0.25 (clear-coat look) | 0 |
| 3 | Blot | solid 3D `fbm(gun_pos/s, 2 oct)` → thresholds 0.42/0.58 for 3 colours; `seed` offset | 2 |
| 4 | Pixel | `floor(gun_pos/s)` cells → hash + 1 coarse noise → 3 colours (digital camo) | 1 |
| 5 | Stripe | noise of `vec3(p.x*1, p.y*1, p.z*0.25)/s` + `sin` bands → elongated stripes along the barrel | 2 |
| 6 | Heat | gradient along `t` through 4 temper colours, hue shifted by `(1-ndv)*0.15` (thin-film sheen); metallic 0.9, roughness 0.28 | 1 |
| 7 | Brushed | noise stretched ×40 along z → roughness and albedo streaks (anodised/brushed alloy) | 1 |
| 8 | Damascus | `sin((p.y + 0.6*fbm(p/s))*k)` → wavy layered bands, A/B steels | 2 |
| 9 | Hazard | diagonal bands `fract((uv.x+uv.y)/s)` on Body only, Trim black | 0 |
| 10 | Hex | box-projected hex grid; edge distance → thin emissive lines; `anim`: a soft band moves muzzle→stock every ~4 s | 0 |
| 11 | Flake | base A + per-cell (1.5 mm) random normal-ish sparkle: `pow(max(0,dot(hashdir,VIEW)),40)` added to specular; fades via fwidth; off in lite | 1 |

### 2.4 Applying a skin (`Viewmodel.Build`, `VmMaterials`)
- `VmMaterials` gets `public GunSkin? Skin;` next to `Gold`. In `ForGunSurface(name, src)`, after the Gold branch:
  - Glass, or no skin → today's switch, unchanged. **Factory therefore takes exactly today's code path.**
  - Hardware → today's switch, or `Make(skin.Hardware)` if the skin sets it.
  - Solid skins → `Make(finish.Color, finish.Metallic, finish.Roughness, finish.Specular, skin.Wear, rim)` for Body,
    and `Make(skin.Trim ?? skin.A …)` for Trim.
  - Patterned → `MakeSkin(skin, role)`, which builds a `ShaderMaterial` with `SkinShader` (lite on Quality 0), sets
    the uniforms and **adds it to `lit` and `all`** so `SetFov` and `SetFlash` reach it. Trim without
    `PatternOnTrim` uses a solid `Make`.
- `Viewmodel.Build` (`Viewmodel.cs:156-187`):
  - `mats.Skin = look.Gold ? null : GunSkins.For(actual);`
  - compute `gunBounds` (merge of each mesh AABB in gun space; the same `gun⁻¹·mesh` transform the clip box already
    uses at l.173);
  - inside the surface loop, for skin materials: `m.SetShaderParameter("skin_xform", new Projection(gun⁻¹·mesh))`,
    `gun_z = (bounds.Position.Z, bounds.End.Z)`, `skin_seed = (int)actual * 0.37f`.
  - Pattern scale is in gun-space metres. That space is the scaled wrapper space (rifles ≈0.9 m long), so one
    `Scale` value looks right on every gun and continues across the `Attach_*` meshes.
  - **Spare magazine** (l.205): when skinned, `magBody` = the skin's Body material. It is in gun space already, so
    `skin_xform = identity`, and the pattern matches the gun's own magazine during reloads.
- **Live re-skin:** a `Reskin()` method re-runs only the surface loop: it clears skin materials out of
  `gunMats`/`lit`/`all` and re-applies `SetSurfaceOverrideMaterial`. `Viewmodel` subscribes to `GunSkins.Changed` in
  `_EnterTree` and unsubscribes in `_ExitTree`. That updates the drill-preview demo and a paused drill without a
  restart.
- `Viewmodel.Create(kind, leftHanded, GunSkin? skinOverride = null)` takes an optional override for the preview.
  `null` resolves from settings.

### 2.5 Catalogue placement in code
`GunSkins.All` is defined in `GunSkins.cs`, in the order of §3, as `static readonly GunSkin X = new(…)` lines, the
same way `WeaponLooks` lists its looks. Swatch colours come from the same records.

### 2.6 What does NOT change (and why)
- **Bots**: factory. Their gun is a different material path (`BotAssets.Base` StandardMaterial3D duplicates, cached
  per source material, plus the enemy fresnel/outline overlay tuned for dark factory metal). A bright skin would
  change enemy readability, which is the trainer's job, and would multiply the material cache. A skin is also "your"
  loadout. *Optional later:* a Deathmatch "bots use random skins" toggle would need a `BotAssets.Base(m, …, skin)`
  path with `StandardMaterial3D` solid colours only.
- **Replays/telemetry**: `ReplayHeader` (`Replay/ReplayTypes.cs:56-64`) and `RunTelemetry` get **no** skin field.
  The Rewind's viewmodel (`ReplayStage.cs:401`) uses the player's **current** skin (cosmetic). Old and new `.vtr`
  files stay byte-compatible, and `--replay-check` / `CoachCli` round trips are untouched.
- **Stats / PBs / coach**: nothing. Skins have no gameplay effect: same model, same muzzle, same ADS sight overlay
  (`AdsSight` is 2D).
- **GunIcons silhouettes**: flat white by design. Unchanged.
- **VALORANT import**: VALORANT's skins are not read and nothing is written to VALORANT.

### 2.7 Persistence (`settings.json`)
```jsonc
"GunSkins": { "Vandal": "carbon", "Sheriff": "damascus" },  // WeaponKind name -> skin id; null when empty
"GunSkinAll": "graphite"                                      // skin for every gun without an entry; null = factory
```
- `AppSettings`: `public Dictionary<string,string>? GunSkins;` and `public string? GunSkinAll;`, documented like
  `WeaponPicks`.
- Resolution: `--skin` (dev) → `GunSkins[gun]` → `GunSkinAll` → factory. An explicit `"factory"` entry overrides
  `GunSkinAll` for that gun. An unknown id or gun name falls through, never an error.
- APPLY TO ALL GUNS: `GunSkinAll = id`, `GunSkins = null`. If per-gun entries exist, the button asks first
  ("Replace 3 per-gun skins?").
- RESET GUN removes the entry. RESET ALL clears both fields.
- Saves follow the settings rules: nothing is written in `--dev` without `--data-dir … --write-data`.

### 2.8 UI: `SkinPicker` overlay (`src/UI/SkinPicker.cs`, new) + preview (`src/UI/SkinPreview.cs`, new)
Layout (design units at 1080p, K-scaled, built in code like `GunPicker`):
```
┌ VPanel "GUN SKINS" · caption "ORIGINAL FINISHES MADE FOR VALTRAINER · COSMETIC ONLY" ───────────────────────────┐
│ GUNS (6 buy-menu groups,     │  LIVE PREVIEW (SubViewport ~860×420)               │ SKINS (4×5 swatch grid)     │
│ compact rows: silhouette +   │  [INSPECT | IN HAND] toggle · drag to rotate        │ FACTORY  GRAPHITE  BONE … │
│ name + current skin chip)    │  name: "CARBON WEAVE" · blurb · ANIMATED tag        │ (selected = accent frame,  │
│  ▸ Vandal   · Carbon         │                                                     │  applied = ✓)              │
│    Phantom  · Factory        │                                                     │                             │
├──────────────────────────────┴─────────────────────────────────────────────────────┴─────────────────────────────┤
│ [RESET GUN]  [RESET ALL]  [APPLY TO ALL GUNS]     Click applies · ←/→ skins · ↑/↓ guns · Tab view · Esc closes [DONE] │
└───────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- Clicking a swatch **applies it at once** to the selected gun (`GunSkins.Set`), the same "pick = apply" model as the
  gun picker. Hovering a swatch previews it without applying.
- Guns column: `Weapons.Buyable` (20) grouped by `WeaponClass` with the `GunPicker.Columns` labels; the silhouette
  comes from `GunIcons.Draw`. The guns in the open drill's `WeaponChoice` are not filtered: skins are per gun,
  everywhere.
- **SkinPreview** (`SubViewport`, `OwnWorld3D`, `TransparentBg`, `Msaa3D = 4×` (only this small viewport),
  `UpdateMode.WhenVisible`):
  - *INSPECT*: the gun's `VmPose.Scene` is instantiated and re-materialed through the **same** `VmMaterials` path
    (`Skin` set, `SetFov(cam.Fov)`, `vm_depth = 1` so there is no squeeze). A perspective camera (fov 30°) looks at
    the right side, and the gun turns 12°/s. A mouse drag rotates it, and it resumes after 2 s idle.
  - *IN HAND*: `Viewmodel.Create(gun, leftHanded, skin)` under a `Camera3D` with fov 70, so idle breathing and sway
    play. This is exactly what the player sees in game.
  - Lighting: a `WorldEnvironment` with a `ProceduralSkyMaterial` sky (ambient and reflections from the sky, so
    metallic skins aren't black), a key `DirectionalLight3D` (no shadows), a dim rim light and AgX tonemapping like
    the game. Verify that `TransparentBg` still keeps sky reflections. If not, use `BackgroundMode.Color` = panel
    colour with `AmbientSource/ReflectedSource = Sky`.
  - One preview viewport at a time. It is freed on close, and on `ScreenBase` rebuild when hosted by Settings.
- **Swatches**: `SkinSwatches`, the same mechanism as `GunIcons.Render`. One hidden SubViewport renders a bevelled
  `BoxMesh` (gun-like proportions 0.12×0.06×0.06 m) per skin with `MakeSkin` in Body role, once, and caches an
  `ImageTexture`. 16 renders take about 50 frames, only the first time the picker opens. Under `--headless`, a flat
  2D fallback draws colour bands A/B/C.
- **Entry points**:
  1. **Main menu nav `SKINS`** (`MenuScreen.cs:276-284`, after AGENTS). Seven buttons need a tighter `compact`:
     widths ×0.78 and font 17 when 7 buttons don't fit. Check at `--window 1280x720` and `--window 1024x768`.
     If it still overflows, fall back to a "SKINS" link under the nav (the `coachHint` row).
  2. **GunPicker footer** "SKINS" button, opened on the focused gun. It is hidden when `CloseOn != null` (the in-drill
     buy menu: keeps `BuySession`'s state machine unchanged). The picker stays open underneath and `SkinPicker`
     stacks above it (`ZIndex = 11`).
  3. **Settings → General** "Gun skins" `Selector` under "Preferred gun" (`SettingsScreen.cs:462`). It shows
     `Factory` / `Carbon on every gun` / `5 guns customised`, and clicking it opens the overlay.
  4. **Drill preview**: the WEAPON row's tooltip and hint say "K: skin". **K** opens SkinPicker on the drill's gun.
     The live demo re-skins through `GunSkins.Changed`.
- Dev: `--screen skins` (Main.cs switch) opens the menu plus the overlay. `--skins-gun <Gun>` selects a gun,
  `--skins-skin <id>` hovers a skin, `--skins-apply <id>` applies it after 3 s (persistence tests), and
  `--skins-view hand|inspect` picks the preview mode, for screenshots.

### 2.9 Optional (not required for v1.19)
- **Inspect** (`GameAction.Inspect`, bound to Y and imported from VALORANT, `Valorant/Keybinds.cs:378`; nothing uses it
  in game yet): a 2.5 s procedural viewmodel twist to show the skin off. It goes in `Viewmodel` as an additive
  rotation curve. It is blocked while aiming, reloading or firing, and the drill keeps running.
- Bug report line `gun_skins: carbon (Vandal), all=graphite` (`UI/BugReport.cs`), useful if a skin shader misbehaves on
  someone's GPU.
- A "Custom" skin later (pick 3 colours and a pattern). The data model supports it as a `GunSkin` built from settings.

---

## 3. Skin catalogue (16 original finishes + Factory)

**Clean-room rules.** These are generic real-world finishes (paints, camo families, metal treatments) and simple
geometric motifs. None is modelled on a VALORANT skin line. Explicitly avoided: gold-and-white or gold-teal schemes,
purple or red energy glows, neon pink-cyan glitch looks, colour-cycling RGB variants, dragons or flames, masks, anime
or "kingdom" motifs, and any VALORANT skin *names*. Before release, check the 16 names against a current list of
VALORANT skin-line names (a quick manual check, names only) and rename any that clash.

| # | Id | Name | Pattern | Colours (sRGB-ish) | Metal/rough (A) | Trim | Notes |
|---|---|---|---|---|---|---|---|
| 0 | `factory` | Factory | — | model colours | today's | today's | default, today's code path |
| 1 | `graphite` | Graphite | Solid | A #2B2E33 | 0.0 / 0.55 | #1B1D20 satin metal 0.6/0.4 | all-dark matte |
| 2 | `bone` | Bone | Solid | A #C9C2B0 | 0.0 / 0.6 | #3A3C40 | off-white polymer, dark metal |
| 3 | `olive` | Olive Drab | Solid | A #4B5136 | 0.0 / 0.65 | #2E3033 parkerised 0.5/0.55 | military generic |
| 4 | `fde` | Desert Two-Tone | Solid | A #8C7553 (FDE) | 0.0 / 0.6 | #151618 | tan furniture on black metal |
| 5 | `signal` | Signal Split | Split | A #3D4A5C slate, B #C9CCD1 grey, C #E8742A line | 0.1 / 0.5 | = A | rear slate, front grey, thin orange seam at 45 % length |
| 6 | `carbon` | Carbon Weave | Carbon | A #101113, B #26282C | 0.2 / 0.25 (spec 0.6) | #0E0F10 | Scale 6 mm; weave fades out past ≈1 px |
| 7 | `woodland` | Woodland Blot | Blot | A #3E4A2C, B #6B5A3A, C #1C1F18 | 0.0 / 0.7 | = pattern (`PatternOnTrim`) | Scale 0.06 m |
| 8 | `arctic` | Arctic Blot | Blot | A #D6DADC, B #9AA3A8, C #5E676D | 0.0 / 0.65 | pattern | Scale 0.07 m |
| 9 | `urban` | Urban Pixel | Pixel | A #6E7276, B #45494D, C #A4A8AB | 0.0 / 0.6 | pattern | 8 mm cells |
| 10 | `tiger` | Tiger Stripe | Stripe | A #8B8358 sand, B #4D5232 olive, C #1A1A16 | 0.0 / 0.7 | pattern | stripes run along the barrel |
| 11 | `heat` | Heat Tint | Heat | straw #C9A55A → bronze #8A5A2E → violet #4B3A6B → steel blue #2F4C7A | 0.9 / 0.28 | pattern | temper gradient along the gun's length, sheen at grazing angles; Hardware too |
| 12 | `cobalt` | Cobalt Brushed | Brushed | A #2C4F86, B #23406E | 0.85 / 0.35 | #1A1C20 | anodised alloy with brush streaks along z |
| 13 | `damascus` | Damascus | Damascus | A #5F6266, B #2C2E31 | 0.9 / 0.32 | pattern | folded-steel bands; Hardware too |
| 14 | `hazard` | Hazard | Hazard | A #E2B521, B #17181A | 0.0 / 0.55 | #17181A | diagonal bands 25 mm, Body only |
| 15 | `hexpulse` | Hex Pulse | Hex | A #1E2226, glow #FFB347 (amber) | 0.6 / 0.4 | #121416 | 9 mm hexes, faint amber edges (energy 0.6, 1.2 in the band), a slow band runs muzzle→stock every 4 s. **Animated** |
| 16 | `flake` | Emerald Flake | Flake | A #0F4A3A, sparkle #9FE3C8 | 0.7 / 0.3 | #111315 | candy paint with fine metallic flake; no sparkle on Low |

Each solid or simple skin is about 1 line in `GunSkins.cs`. Animated skins (only Hex Pulse) get an "ANIMATED" tag in
the picker. The animation is shader-only (`TIME`), so there is no CPU cost.

---

## 4. Files an implementer must create or change

### Create
| File | Content |
|---|---|
| `assets/shaders/weapon/viewmodel.gdshaderinc` | the body of today's `viewmodel.gdshader` + `#ifdef SKIN` uniforms, varyings, `pattern()` and AA helpers, `#ifdef SKIN_LITE` reductions |
| `assets/shaders/weapon/viewmodel_skin.gdshader` | `shader_type spatial; render_mode …; #define SKIN #include` |
| `assets/shaders/weapon/viewmodel_skin_lite.gdshader` | `#define SKIN` + `#define SKIN_LITE` + include (Quality Low) |
| `src/Game/Weapon/GunSkins.cs` | `SkinPattern`, `SkinFinish`, `GunSkin`, catalogue `All`, `Get/For/Set/SetAll/ResetAll`, `Changed`, dev `--skin` |
| `src/UI/SkinPicker.cs` | the overlay (§2.8): gun list, swatch grid, footer, keyboard, dev answers |
| `src/UI/SkinPreview.cs` | `SkinPreview` (SubViewport turntable / in-hand) + `SkinSwatches` (cached renders, 2D fallback) |

### Change
| File | Change |
|---|---|
| `assets/shaders/weapon/viewmodel.gdshader` | becomes `render_mode …` + `#include "viewmodel.gdshaderinc"` (identical output) |
| `src/Game/Weapon/VmMaterials.cs` | `SkinShader`/`SkinLiteShader` loaders; `public GunSkin? Skin`; `RoleOf(name)`; `ForGunSurface` skin branch (§2.4); `MakeSkin(skin, role)` registered in `lit`/`all`; `PreviewDepth` (`vm_depth = 1`) |
| `src/Game/Weapon/Viewmodel.cs` | `Create(..., GunSkin? skinOverride = null)`; in `Build` set `mats.Skin` (not for `look.Gold`), gun-space bounds, `skin_xform`/`gun_z`/`skin_seed` per surface (l.170-187); spare mag uses the Body skin (l.205); `Reskin()` + `GunSkins.Changed` subscription |
| `src/Game/Weapon/VmTest.cs` | `--skin <id>` (via `GunSkins` dev override) and `--skincycle all|id,id [--vmevery S]` (like `--vmcycle`: rebuild per skin, save `prefix_<id>.png`, quit after the last) |
| `src/Core/AppSettings.cs` | `GunSkins`, `GunSkinAll` fields (after `WeaponPicks`, l.41) |
| `src/Core/CultureCheck.cs` | data checks next to the gun-pick round trip (l.562-572): settings round trip of both fields; unknown id → factory; unknown gun name ignored; `SetAll` clears per-gun; catalogue ids unique, lowercase, every `SkinPattern` value handled |
| `src/UI/GunPicker.cs` | footer "SKINS" button (not when `CloseOn != null`), opens `SkinPicker` on the focused gun; optional small skin dot on `GunTile` |
| `src/UI/MenuScreen.cs` | `SKINS` nav button + 7-button compact sizing (l.247-284) |
| `src/UI/SettingsScreen.cs` | "Gun skins" `Selector` after "Preferred gun" (l.462) |
| `src/UI/DrillPreview.cs` | **K** opens `SkinPicker` on the shown gun; hint text |
| `src/Main.cs` | `--screen skins` (l.169-177) |
| `CHANGELOG.md`, What's New (`WhatsNewPanel`), `CREDITS.md` | "Gun skins: 16 original procedural finishes…"; the credit says the finishes are original and procedural, with no third-party art |
| `tools/selftest.ps1` | nothing new is needed if the checks live in `CultureCheck` (already run). Optionally add one windowed `--vmtest --skincycle all` smoke run whose log is grepped for `SHADER ERROR` |

### Must NOT change
`Game/Bots/*`, `Game/Replay/*` (format), `Game/Telemetry.cs`, `Core/Stats.cs`, `Analysis/*`, `GunIcons` rendering.

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| **No UVs** on the models (§1.4) | Object-space patterns only (solid noise or dominant-axis box projection in gun space). |
| `obj_pos` is mesh-local in FBX units (×1/0.17 for rifles), and attachments have their own transforms | `skin_xform` = `gun⁻¹ · mesh` per surface: patterns are in metres and continuous across `Attach_*`. |
| Pattern shimmer: no MSAA, no TAA; FXAA on Medium, nothing on Low | `fwidth`-filtered edges; fade detail by pixel footprint; minimum cell sizes: weave 6 mm, hex 9 mm, pixel 8 mm, flake off on Low. Check at 1080p *and* `--render-size 2560x1440`. |
| Glow only on High/Ultra | Emissive colours designed to read without bloom; energy ≤ 1.5 so High/Ultra don't blow out. |
| Compatibility (GL 3.3/ANGLE) fallback | The precedent `surface.gdshaderinc` works there. Avoid unsupported built-ins; run one `--rendering-method gl_compatibility` capture (§6). |
| Low drops sky reflections, so metallic skins look black | `SKIN_LITE`: metallic ×0.6, albedo lifted. |
| Shader compile hitch on first use | One uber skin shader (plus lite), built at session start before the countdown; SkinPicker prewarms it. |
| Factory look drifts after the `.gdshaderinc` refactor | Before/after `--vmcycle all` contact sheets, compared pixel by pixel (§6 T1). |
| ADS readability / distraction | Hardware (sights, rails) stays factory; no glow near the top of the gun; animation ≤ 0.5 Hz; one animated skin only. |
| Seven nav buttons overflow on 4:3 | Tighter compact sizing, or a link row fallback; checked at 1024×768 and 1280×720. |
| Look-alike or name clash with a VALORANT skin | Clean-room rules (§3) and a name check before release. |
| `MakeSkin` materials not registered, so FOV/flash don't update | Register in `lit`/`all`; T3 changes the viewmodel FOV with a skin on. |
| Reload clip box missing on skin materials, so the magazine doesn't hide | Same `clip_*` uniforms in the include; T2 `--vmreload loop`. |

---

## 6. Test plan

All runs are `--dev` (gshot adds it). Always sandbox: a temp `--data-dir` copy and an empty `--valorant-dir`.
`gshot.ps1` splits `-Extra` on spaces, so **use paths without spaces** (`%TEMP%` is fine). Only one Godot runs at a
time (the `Global\ValTrainerGodotSlot` mutex). gshot only captures and kills its own process tree. Screenshots and logs
go to `%TEMP%\gshot`. gshot echoes `ERROR`/`Exception` lines from the logs, so a skin shader compile error shows up
as `SHADER ERROR`.

```powershell
# sandbox: a copy of the real settings (so the user's file is never touched) and "VALORANT not installed"
$t = Join-Path $env:TEMP "vt-skins"; $data = "$t\data"; $noval = "$t\valorant"
New-Item -ItemType Directory -Force $data, $noval | Out-Null
Copy-Item "$env:APPDATA\ValTrainer\settings.json" $data -ErrorAction SilentlyContinue
$sb = "--data-dir $data --valorant-dir $noval"
```

**Build:** `dotnet build ValTrainerGodot\ValTrainer.csproj -nologo` (same as CI).

| # | What | Command | Pass |
|---|---|---|---|
| T1 | Factory unchanged after the shader refactor | before the change: `tools\gshot.ps1 -Mode gridshot -At 60 -Name vmA -Extra "$sb --vmtest --vmcycle all --vmevery 2.5 --vmshots 1 --vmout $t\before"`; after: the same with `$t\after` | every `before_<Gun>.png` = `after_<Gun>.png` (pixel diff ≈ 0) |
| T2 | Every skin on a representative gun from each model family | `tools\gshot.ps1 -Mode gridshot -At 50 -Name sk -Extra "$sb --vmtest --vmkind Vandal --vmview side --skincycle all --vmout $t\vandal"`; repeat `--vmkind` Phantom (Main palette), Sheriff (revolver, wood), Operator (Glass), Odin (Bipod attachment), Ghost (Silencer), Bucky (wood) | patterns continuous across attachments; glass untouched; sights factory; no stretched or smeared box-projection faces; Chamber unaffected (`--vmkind Headhunter --skin carbon` stays gold) |
| T3 | In-game look, hip + ADS + reload + fire | `tools\gshot.ps1 -Mode strafebots -At 4,6 -Name hex -Extra "$sb --skin hexpulse"`; `… -Extra "$sb --vmtest --vmkind Vandal --skin carbon --vmads 1"` (ADS check line stays 0.60–0.65 H); `… --vmreload loop` (magazine hides, spare mag matches); `… --vmfire` (flash fill on the skin) | readable sight picture; magazine clip works; flash lights the skin |
| T4 | Shimmer | `tools\gshot.ps1 -Mode strafebots -At 5,5.1,5.2 -Name aa -Extra "$sb --skin carbon --vmtest --vmspeed 5"`, plus the same with `--render-size 2560x1440` and with Quality Low (set `"Quality": 0` in the sandbox settings.json) | no moiré or crawling between consecutive frames |
| T5 | Performance (the P2000 question) | `tools\gshot.ps1 -Mode gridshot -At 25 -Name bF -Extra "$sb --vmtest --vmbench"` then `-Name bH -Extra "$sb --vmtest --vmbench --skin hexpulse"`, `--skin woodland`, `--skin flake`; read `[vmbench] off/on gpu` in `%TEMP%\gshot\b*.out.log` | (on − off) of the skin minus (on − off) of Factory ≤ 0.15 ms at Medium |
| T6 | Compatibility renderer | `tools\godot-run.ps1 -Seconds 20 -GodotArgs "--path","<repo>\ValTrainerGodot","--rendering-method","gl_compatibility","--","--dev","--data-dir",$data,"--valorant-dir",$noval,"--mode","gridshot","--vmtest","--skincycle","all","--vmout","$t\gl"` | no `SHADER ERROR` in the log; `gl_<id>.png` look like the Forward+ ones |
| T7 | Picker UI and layout | `tools\gshot.ps1 -At 4,7 -Name ui -Client -Extra "$sb --screen skins --skins-gun Vandal --skins-skin damascus"`; again with `--skins-view hand`; with `--window 1280x720`, `--window 1024x768` and `--render-size 2560x1440`; main menu at `--window 1024x768` (7 nav buttons fit) | nothing clipped; swatches render; the preview is lit (metals not black) |
| T8 | Entry points | `tools\gshot.ps1 -At 3,6 -Name pv -Extra "$sb --preview gridshot --preview-answer gun:Phantom"` (the demo shows the Phantom's skin after the pick); `--screen settings` shows the "Gun skins" row | correct skin in the demo; row text right |
| T9 | Persistence | `tools\gshot.ps1 -At 6 -Name save -Extra "$sb --write-data --screen skins --skins-gun Vandal --skins-apply carbon"` (dev answer that applies), then inspect `$data\settings.json` for `"GunSkins": { "Vandal": "carbon" }`; relaunch `-Mode strafebots` with the same `$sb`, no `--skin` | the skin persists; without `--write-data` the file is unchanged (`(Get-FileHash)` before = after) |
| T10 | Data checks | `tools\selftest.ps1` (runs `--culture-test` incl. the new CultureCheck skin checks under invariant / de-DE / tr-TR) | all green |
| T11 | Bots and replays unaffected | `tools\gshot.ps1 -Mode deathmatch -At 8 -Name dm -Extra "$sb --skin hazard"` (bots keep factory guns); `selftest.ps1`'s `--replay-check` stays green; open a Rewind (`--screen stats` → a run → Rewind 3D) | bot guns factory; replay header unchanged |

Hand check by the maintainer: one minute of Gridshot with Hex Pulse and Carbon at Medium on the Quadro P2000, with
the FPS overlay on. The FPS must match Factory within noise.

---

## 7. Suggested work split (multi-agent, like 1.18)
- **Agent A: shader + materials** (`viewmodel*.gdshader*`, `VmMaterials.cs`, `Viewmodel.cs`, `VmTest.cs`,
  `GunSkins.cs` catalogue data). It delivers T1–T6.
- **Agent B: UI + persistence** (`SkinPicker.cs`, `SkinPreview.cs`, `GunPicker.cs`, `MenuScreen.cs`,
  `SettingsScreen.cs`, `DrillPreview.cs`, `Main.cs`, `AppSettings.cs`, `CultureCheck.cs`). It delivers T7–T10.
  Agent A ships the `GunSkins` API (signatures in §2.2) first, as a stub with Factory + Graphite, so B can start at
  once.
- **Integration**: CHANGELOG / What's New / CREDITS, T11, the full `selftest.ps1`, a contact sheet of 16 skins × 4
  guns for review, and a Discord-ready image.
