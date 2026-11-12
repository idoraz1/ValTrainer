# All 18 guns + weapon picker: integration design (v1.18)

Status: design only. No game source was changed. Stats and per-gun numbers come from the companion research doc
`docs/research/weapons-1.18.md` (another agent is writing it; it did not exist yet when this was written). Where this
design names new `WeaponDef` fields, treat the names as proposals and reconcile them with that doc. Its numbers win.

Community asks behind this: "allow phantoms more in drills", "select one's preferred weapons (Ghost, Phantom, Vandal,
Operator, Sheriff, Classic)", "change guns between phantom and vandal". The maintainer promised: *"We will add all the
weapons and allow to choose one before the start of each non weapon-specific drill."*

---

## 0. Summary of decisions

- **WeaponKind**: append 14 values after `TourDeForce` with explicit numbers. Never reorder: the 3D Rewind stores
  `(int)WeaponKind` in `RxKind.Weapon` events (`ReplayRecorder.WeaponSwitch`), and telemetry, replay headers and
  stats store `kind.ToString()`.
- **One table of looks** (`WeaponLooks`) replaces the switches that are scattered today: viewmodel pose and scene,
  shot and reload sound keys, ADS sight outline, scope style and bot model.
- **Gun choice lives on the session, not in the mode key.** `Mode.Key` stays `"gridshot"`. This keeps every existing
  key-based list working: the coach drill lists, `ReplayFile.Full`, `RunSummary`, `SimAim`, warm-up plans and
  `ModeRegistry.Find`. `RunRecord` gains `Weapon` (null means an old run, which counts as the drill's default gun).
- **Picker**: a WEAPON row in the drill preview opens a buy-menu-style overlay (B key, like VALORANT's buy menu). The
  pick is remembered per drill, with an optional global "preferred gun". Map drills get a preview without the live demo
  so they can show the picker too.
- **Personal bests are per gun.** Badges and the coach's rank estimate only count "rated" guns. The drill's default
  gun is always rated, and so is the Phantom in Vandal drills, so Vandal and Phantom are equal citizens.
- **Pellet volleys are normalised in `GameSession`.** Each volley counts as one shot and at most one hit, so none of
  the ~30 modes needs editing for shotguns.
- **Deathmatch bots** carry a Vandal or a Phantom (rolled per bot). Every other bot keeps the Vandal.
- **Work split**: a short Phase 0 foundation by Agent A, then 4 parallel agents (A mechanics, B art/audio/viewmodel,
  C picker UI/records, D modes/bots/analysis), then one integration pass.

---

## 1. Every place that assumes the current gun set

`grep WeaponKind|Weapons.Get|.Sniper|.Bolt|Def.Kind|Def.Family` across `src`, plus hard-coded `Weapons.Vandal`, gives
these places.

### 1.1 Core
| File | Assumption | Change |
|---|---|---|
| `src/Core/Weapons.cs` | `enum WeaponKind { None, Vandal, Phantom, Sheriff, Operator, Headhunter, TourDeForce }`; `Get()` switch; `FamilyOf()`; `Sniper => Family == Operator`; `Bolt => Kind == Operator`; `AdsMoveMul => Kind == Operator ? 0.72 : 0.76` | Append `Classic = 7, Shorty, Frenzy, Ghost, Stinger, Spectre, Bucky, Judge, Bulldog, Guardian, Marshal, Outlaw, Ares, Odin = 20` (explicit numbers). Turn `Sniper`, `Bolt` and `AdsMoveMul` into data fields (`Scope`, `UnscopeOnShot`, `AdsMoveMul`). Add `WeaponClass Class` (Sidearm, SMG, Shotgun, Rifle, Sniper, Heavy), `Weapons.All` (buy-menu order), `Weapons.Buyable` (the 18 guns, without Chamber's) and `Weapons.Parse(string)` (case-insensitive, null-safe). `Get()` becomes a dictionary lookup. Keep `FamilyOf` only for behaviour (Chamber's guns), not for looks. |
| `src/Core/Stats.cs` | `RunRecord` has no weapon; `Best(mode, tier)` / `BestBadge(mode)` ignore guns | Add `public string? Weapon` (`JsonIgnore WhenWritingNull`). Add helpers `GunOf(r, defaultGun)`, `Best(mode, tier, gun, defaultGun)`, `BestBadge(mode, rated)` (see §3.4). |
| `src/Core/AppSettings.cs` | nothing gun-related | Add `string? PreferredWeapon` (null = each drill's default) and `Dictionary<string,string>? WeaponPicks` (mode key → gun name). |
| `src/Core/CultureCheck.cs` | `DataChecks` loads stats/settings | Add checks: an old stats.json without `Weapon` loads and resolves to the default gun; a `WeaponPicks` round trip; an unknown gun name ("Knife") falls back without throwing. |

### 1.2 Session, weapon and viewmodel
| File | Assumption | Change |
|---|---|---|
| `src/Game/GameSession.cs:290-297` | `Equip(Weapons.Get(Mode.Weapon))`; `Telemetry.Weapon = Mode.Weapon.ToString()`; `ZoomSensMultFor(Weapons.Get(Mode.Weapon))` | Use `Mode.ActiveWeapon` (§3.2). |
| `GameSession.cs:773` (`StartReplay`) | `ReplayHeader.Weapon = Mode.Weapon.ToString()` | Use `Mode.ActiveWeapon`. Without this the Rewind shows the wrong viewmodel. |
| `GameSession.cs:531-535` | shot sound `Family switch { Operator, Phantom, Sheriff, _ => "vandal" }`; bolt sound `Kind == Operator`; reload `Kind == Sheriff ? reload_pistol : reload_rifle`, pitch 0.75 for Operator | `WeaponLooks.For(def.Kind).ShotSound / ReloadSound / ReloadPitch / BoltSound`. |
| `GameSession.cs:527-528` | `Gun.Tick` returns `(Dir, Accurate)`; one `Bullet()` per entry | Pellet volleys (§4.2). |
| `GameSession.cs:89-93` (`DemoProfile`) | spray `burst=` from `wd.Rps` | Use the fire rate after spin-up for Odin/Ares (`Def.MaxRps`). |
| `GameSession.cs:471-472` | `new SimAim(profile, Mode.Key, Mode.SimKind)` | Pass `weaponDef` (§1.5). |
| `GameSession.cs:492-505` | demo sniper logic keyed on `weaponDef.Sniper`; spray demo refill | Works once `Sniper` becomes `Scope`. Burst guns in the demo: tap instead of hold. |
| `src/Game/AdsSession.cs:38,81,94-109` | `ZoomSensMultFor` by `Sniper`; `Scoped` needs `w.Zoom > 1`; scope style `TourDeForce ? TdF : Operator` (the comment already says Marshal / Outlaw go here); `AdsSight.LookFor(Kind)` | Scope style from `WeaponLooks` (`CrosshairView.SniperScope.Marshal/Outlaw` already exist and are drawn). Alt-fire dispatch per `Def.Alt` (§4.4): a Burst alt fires the Classic burst instead of aiming. |
| `src/Game/Weapon/WeaponController.cs` | `side = Phantom ? 1 : -1`; `Sniper`, `Bolt`; one bullet per shot; fixed `Def.Interval` | §4: pellets, bursts, spin-up, alt modes. Make the first-drift side a field (`FirstDriftSide`). |
| `src/Game/Weapon/VmPoses.cs` | 4 poses; `For()` maps everything else to the Vandal | 14 new poses (§5), built from 3 bases (pistol, rifle, sniper). `For()` becomes a table in `WeaponLooks`. |
| `src/Game/Weapon/Viewmodel.cs:58,73,211,366,391` | `kind = FamilyOf(kind)` (Chamber's guns use the family model in gold); bolt cycle `actual == Operator`; shells `|| kind == Operator`; `pistol = kind == Sheriff` (reload tilt); reload bolt `actual == Operator` | Pose flags: `Pistol` (reload tilt), `BoltCycle` (Operator, Marshal), `ShellReload` (Bucky, if the research says it reloads one shell at a time), `NoMag` (Shorty, revolver: no spare-magazine swap). The Headhunter gets its own pose (Sheriff becomes a revolver, §5). Attachments (silencer, bipod) from the look. |
| `src/Game/Weapon/VmTest.cs:56,74-75,266,275` | `--vmkind` parses the enum (fine); `ViewXform` distances per Operator/Sheriff/else; fire cadence `Operator ? 2.0` | Read the view distance from the pose (`ViewD` per class) and the cadence from `Def`. |
| `src/Game/Weapon/MuzzleFlash.cs` | flash scale from the pose | Suppressed guns (Ghost, Spectre): a smaller, dimmer flash (`FlashScale` ×0.4, from the look). |
| `src/Game/KeybindTest.cs:165-170` | alt fire is skipped if `Zoom <= 1`; hold vs toggle by `Sniper` | Burst alt-fire guns (Classic) still test alt fire. |
| `src/Game/AbilitySession.cs:30` | `Viewmodel.Create(weaponDef.Kind, …)` | No change (it goes through `WeaponLooks`). |
| `src/Game/MovementCheck.cs:212-268` | uses `Weapons.Vandal` / `Operator` as fixtures | No change. |
| `src/Game/Mover.cs:56` (`Hold`) | reads `RunSpeed`, `AdsMoveMul`, error fields | Data-driven already. Only the `AdsMoveMul` field change above. |

### 1.3 UI
| File | Assumption | Change |
|---|---|---|
| `src/UI/AdsSight.cs:25-31` | outlines for Vandal / Phantom / Headhunter; else none | Outlines for every gun with a red-dot style ADS: all SMGs, rifles, heavies and the Ghost / Sheriff / Frenzy if the research says they ADS. Two or three looks are enough (rifle box, SMG box, pistol). Data from `WeaponLooks`. |
| `src/UI/CrosshairView.cs` | `SniperScope { Operator, Marshal, Outlaw, TourDeForce }` | Already drawn. Only the mapping is missing. |
| `src/UI/Hud.cs:315-332` (ammo) | `gun.Def.Name`, mag bar | Works. Add a small `BURST` chip when the Bulldog / Stinger are aimed and the fire mode changes, and a spin-up bar for the Odin / Ares (optional). |
| `src/UI/Hud.cs:703-` (`BuildResults`) | `PrevBest = Stats.Best(Mode.Key, Tier)`; no gun shown | Show the gun (§3.5). PB per gun. |
| `src/UI/DrillPreview.cs:120,358` | "Weapon" row from `info.Weapon`; `WeaponName` special-cases TdF | Interactive WEAPON row + picker (§3). Use `def.Name`. |
| `DrillPreview.cs:290-310` (`DrawRecord`) | runs filtered by mode + tier | Also filter by the selected gun. |
| `DrillPreview.cs:418-484` (`PreviewCheck`) | demos the default gun | Add `--preview-gun <Gun>` (and `all` = each drill once per allowed class) so the check covers pellets, bursts and spin-up. |
| `src/UI/RewindView3D.cs:401-407` | `sniper = Weapons.Get(kind)?.Sniper`; scope `TdF ? TdF : Operator` | Use `WeaponLooks.ScopeOf(kind)`. |
| `src/UI/StatsScreen.cs:98` | rows per mode key | Gun filter (§3.5). |
| `src/UI/ProfileScreen.cs:433` | text "…and Vandal Spray" | Fine as it is. |
| `src/UI/MenuScreen.cs:531`, `src/UI/AgentsScreen.cs:325`, `src/Main.cs:235` | `StartMode(make, freePlay)` | `StartMode(make, freePlay, WeaponKind? gun = null)`. When `gun` is null the session resolves the pick itself. |
| `src/UI/SettingsScreen.cs` | — | Gameplay page: "Preferred gun" chooser that opens the same picker overlay. |

### 1.4 Modes and bots
| File | Assumption | Change |
|---|---|---|
| `src/Modes/TrainingMode.cs:25,156-171` | `virtual WeaponKind Weapon => Vandal`; `MakeRecord` has no gun | Add `WeaponChoice`, `Pick`, `ActiveWeapon`; record the gun (§3.2). |
| `src/Modes/RecoilModes.cs:30-46,209` | `SprayControlMode(kind)`: key, name and side depend on Vandal/Phantom; `Required` ×1.2 for Phantom | Keep both cards locked (stats compatibility). Optional new `spray:<gun>` card (§2.3). |
| `RecoilModes.cs:301` (SprayTransfer) | `Weapons.Vandal.Interval * 1.6` | `G.Weapon!.Interval` (bug once the picker exists). |
| `src/Modes/DeathmatchBots.cs:410`, `src/Game/Bots/BotBrain.cs:20` | bots shoot `Weapons.Vandal` | `BotBrain` takes a `WeaponDef` (default Vandal). DM bots roll Vandal or Phantom (§6). |
| `src/Modes/DefenseAttackers.cs:395`, `src/Modes/Agents/InitiatorDrill.cs:507-512` | attackers use the Vandal | Keep the Vandal. |
| `src/Modes/DeathmatchMode.cs:28-52` | ctor `weapon` param (Vandal / Phantom / Sheriff) | The param becomes the default. The picker overrides it. |
| `src/Modes/CrosshairFinderMode.cs:45,181,664,750` | Vandal; `SwitchWeapon(Operator)` for the sniper tab | Locked. No change. |
| `src/Modes/SheriffMode.cs`, `BotModes.cs:308`, `ChamberDrill.cs:24`, `DuelistDrills.cs:42` | gun-specific | Locked (Operator: snipers only, §2). |
| `src/Modes/BotModes.cs:32`, `DefenseModes.cs:539`, `AimPlusLongTaps.cs:49` | `G.Weapon ?? Weapons.Vandal` | Data-driven already: they pick up the chosen gun. |
| `src/Audio/Sfx.cs:47-53` | recordings `vandal`, `phantom`, `operator`, `sheriff`, `enemyshot` | New keys per gun (§7). |
| `src/Game/Bots/BotAssets.cs:33` | `Rifle` = AK scene | `RifleFor(kind)` (AK or M4) for DM bots. |

### 1.5 Analysis, telemetry and replay
| File | Assumption | Change |
|---|---|---|
| `src/Analysis/SimAim.cs:125-132` | `weapon = spray_phantom ? Phantom : sheriff ? Sheriff : Vandal`; tap gap only for "sheriff" | Constructor takes `WeaponDef? gun`; falls back to today's logic. Semi-auto guns: `TapGap = RecoveryTime + 0.03`. Shotguns: tap at close range. Burst guns: one press per burst. Spin-up guns: hold. `Climb()` uses `gun.Rps` → the rate after spin-up. |
| `src/Analysis/RunAnalysis.cs:875` | spray `required = spray_phantom ? 4.6 : 5.5` °/s | Keep both constants for the two keys. For any other gun: `5.5 × climb(gun)/climb(Vandal)`, where climb = (Pitch[12]−Pitch[3])·Rps/9. |
| `src/Analysis/Coach.cs:85-102` (skill profile), `RunAnalysis.Normalize` | all runs feed the profile | Runs with an unrated gun (§3.4) drop gun-dependent metrics: `flick.first_hits`, `head.first_hits`, `flick.ttk_ms`, `flick.ttk_hit_ms`, `spray.*`, `move.*`, `speed.*`. Mouse-only metrics (onset, end error, N_c, tracking, crosshair placement) still count. |
| `src/Analysis/Assess.cs:235,615` | "play Vandal or Phantom Spray"; "the Vandal climbs about 5°/s" | Wording only. Optional. |
| `src/Analysis/Prep.cs:34` | zoom from `Enum.TryParse(t.Weapon)` | Works for the new names. Burst-alt guns have `Zoom = 1`. |
| `src/Game/Telemetry.cs:59,95,148` | `Weapon` string | Works. |
| `src/Game/Replay/*` | `RxKind.Weapon` int; header `Weapon` string | Works if the enum values stay stable. An older app reading a new replay gets `Weapons.Get(15) == null` and shows no viewmodel (safe). |
| `src/Analysis/CoachCli.cs:192,372,473,522` | fixtures say "Vandal" | No change. |

### 1.6 Warm-up / Lock-In
`WarmupRunner.cs:194` builds `new GameSession(make)` with no gun, so the session resolves the pick (per-drill pick →
preferred gun → default; §3.2). `WarmupData.cs:217` compares a step's value with earlier runs of the same mode and
tier. Add a filter for the same gun (null = default) so a Phantom warm-up is not compared with Vandal history.

---

## 2. Which drills get the picker (every mode in `ModeRegistry`)

Each mode declares `WeaponChoice`: `null` = locked (no picker), or a set of allowed guns. Sets:
**Any** = all 18 buyable guns. **NoShotguns** = Any minus Shorty / Bucky / Judge (pellets make target-drill scores
meaningless). **Auto** = fully automatic guns (Vandal, Phantom, Spectre, Stinger, Frenzy, Bulldog (hip), Ares, Odin).
**Snipers** = Operator, Marshal, Outlaw.

| Key | Name | Category | Gun today | Verdict | Allowed |
|---|---|---|---|---|---|
| `gridshot` | Gridshot | Aim | Vandal | **Picker** | NoShotguns |
| `flick` | Head Flicks | Aim | Vandal | **Picker** | NoShotguns |
| `spider` | Spidershot | Aim | Vandal | **Picker** | NoShotguns |
| `tracking` | Strafe Tracking | Aim | None (hold Mouse 1, no gun) | Locked: pure tracking measure, no gun | — |
| `strafebots` | Strafe Bots | Valorant | Vandal | **Picker** | Any |
| `peek` | Peek Practice | Valorant | Vandal | **Picker** | Any |
| `operator` | Operator | Valorant | Operator | **Picker, snipers only** (the only drill where the Marshal / Outlaw are useful; key unchanged, PB per gun) | Snipers |
| `sheriff` | Sheriff One-Taps | Valorant | Sheriff | Locked: Sheriff damage breakpoints and the "30 m+ head" banner are the point | — |
| `spray_vandal` | Vandal Spray | Recoil | Vandal | Locked (gun in the name; history) | — |
| `spray_phantom` | Phantom Spray | Recoil | Phantom | Locked | — |
| `spray_transfer` | Spray Transfer | Recoil | Vandal | **Picker** | Auto |
| `counterstrafe` | Counter-Strafe | Movement | Vandal | **Picker** (movement error differs per gun class, which is the point of choosing) | Any |
| `peekduel` | Peek Duels | Movement | Vandal | **Picker** | Any |
| `strafeduel` | Strafe Duels | Movement | Vandal | **Picker** | Any |
| `siteclear` | Site Clear | Map | Vandal | **Picker** (map drill, so a preview without the demo, §3.1) | Any |
| `flashmap` | Flash Dodge | Utility | Vandal | **Picker** (no demo) | Any |
| `reaction` | Reaction Test | Other | None (2D) | Locked: no gun | — |
| `sensfinder` | Sens Finder | Coach | None | Locked | — |
| `xhairfinder` | Crosshair Finder | Coach | Vandal (+ Operator on the sniper tab) | Locked: the gun is part of the test | — |
| `deathmatch` | Deathmatch | Map | Vandal (ctor param) | **Picker** (no demo) | Any |
| `microshot` | Microshot | Aim | Vandal | **Picker** | NoShotguns |
| `switch` | Target Switch | Aim | Vandal | **Picker** | NoShotguns |
| `popup` | Pop-up Reflex | Aim | Vandal | **Picker** | NoShotguns |
| `longtaps` | Long-range Taps | Valorant | Vandal | **Picker** | NoShotguns |
| `jumppeek` | Jump Peek | Movement | Vandal (vs a bot Operator) | **Picker** | Any |
| `jigglepeek` | Jiggle Peek | Movement | Vandal (vs a bot Operator) | **Picker** | Any |
| `peek_mix` | Mixed Peeks | Movement | Vandal | **Picker** | Any |
| `postplant` | Post-Plant | Map | Vandal | **Picker** (no demo) | Any |
| `retake` | Retake | Map | Vandal | **Picker** (no demo) | Any |
| `anchor` / `anchor:<agent>` | Site Anchor | Map / Agents | Vandal | **Picker** | Any |
| `sound` | Sound Lock | Utility | Vandal | **Picker** (no demo) | Any |
| `flashpeek:<agent>` | Flash & Peek | Agents | Vandal | **Pick resolved silently** (Agents screen: a gun chip, §3.1) | Any |
| `recon:<agent>` | Recon & Clear | Agents | Vandal | same | Any |
| `smokeexec:<agent>` | Smoke Execute | Agents | Vandal | same | Any |
| `mobility:<agent>` | Mobility Entry | Agents | Vandal, Operator for `jett-op` | same; `jett-op` locked to the Operator | Any / — |
| `chamber:<variant>` | Chamber Guns | Agents | Headhunter / TdF | Locked: these are the ability guns | — |

Defaults do not change. Every drill still plays the Vandal (or its own gun) unless the player picks another, so
existing benchmarks and PBs stay valid.

### 2.3 Optional: a generic spray card
Add `["spray"] = a => new SprayControlMode(Weapons.Parse(a) ?? Vandal)` to `ModeRegistry.WithArg` for `spray:spectre`,
`spray:odin` and so on. Show one card, "Spray Control", with an Auto-only picker that launches `spray:<gun>`. The
per-gun key then keeps stats apart, like the agent args do. `spray_vandal` and `spray_phantom` stay as they are.
`RunAnalysis`/`InsightsBuilder`/`SimAim` check `t.Mode is "spray_vandal" or "spray_phantom"` and must also accept
`spray:*`. This is phase 2: nice to have, not required by the promise.

---

## 3. The picker UX, memory and records

### 3.1 Where the picker appears
- **Drill preview** (`src/UI/DrillPreview.cs`). Replace the read-only "Weapon" `InfoRow` with an interactive row:
  `WEAPON   Phantom   [CHANGE · B]`, plus a tag: `DRILL DEFAULT`, `YOUR PICK` or `PREFERRED`. Clicking it, or
  pressing **B** (VALORANT's buy-menu key), opens the gun picker overlay. Locked drills show the row as today, with no
  button.
- **Map drills** currently skip the preview (`HasPreview => Map == null`, so the map isn't built twice). Change it to
  `HasPreview => true` and `PreviewDemo => Map == null && !Is2D` for drills with `WeaponChoice != null`. They then get the
  preview's existing "NO PREVIEW FOR THIS ONE" box (new text: "Map drills start straight on the map, so there is no live
  preview."), with no map built and no extra VRAM. Map drills without a choice keep starting right away.
- **Agents screen** (`AgentsScreen.StartDrill` starts directly). Add a small `GUN: VANDAL · B` chip on the agent page
  that opens the same overlay, storing the pick under the drill key. Optional for v1.18: without it, agent drills use
  the preferred gun.
- **Results screen** (phase 2): B opens the picker. PLAY AGAIN then restarts with the new gun
  (`GameSession.WeaponPick` settable, then `Restart()`).
- **Settings → Gameplay**: "Preferred gun: Drill default ▸" opens the overlay in "preferred" mode.

### 3.2 Resolution and plumbing (Phase 0 contract)
```csharp
// TrainingMode
public virtual IReadOnlyCollection<WeaponKind>? WeaponChoice => null;   // null = locked
public WeaponKind? Pick { get; set; }                                     // set by the session before Attach
public WeaponKind ActiveWeapon => Pick is { } p && WeaponChoice?.Contains(p) == true ? p : Weapon;
// MakeRecord: Weapon = Weapon == WeaponKind.None ? null : ActiveWeapon.ToString()
```
```csharp
// GameSession
public WeaponKind? WeaponPick { get; set; }   // explicit (preview, --gun); null = resolve
// Restart(): Mode = factory(); Mode.Pick = WeaponPick ?? WeaponPicks.Resolve(Mode);
```
`src/Core/WeaponPicks.cs` (new): `Resolve(mode)` returns `Settings.WeaponPicks[mode.Key]` if it is allowed, else
`Settings.PreferredWeapon` if it is allowed, else null (the drill's default). `Remember(modeKey, gun)` /
`SetPreferred(gun)` save the settings (`AppSettings.ReadOnly` under `--dev`, as today). Dev flag `--gun <Name>` forces
`WeaponPick` for `--mode` runs and for gshot.

The preview calls `new GameSession(make) { Demo = true, WeaponPick = selected }` and restarts the demo when the pick
changes, as `SelectTier` already does. START and FREE PLAY call `Main.I.StartMode(make, free, selected)`.

### 3.3 The overlay (`src/UI/GunPicker.cs`, new)
A buy-menu layout of 6 columns: SIDEARMS (Classic, Shorty, Frenzy, Ghost, Sheriff) · SMGS (Stinger, Spectre) ·
SHOTGUNS (Bucky, Judge) · RIFLES (Bulldog, Guardian, Phantom, Vandal) · SNIPERS (Marshal, Outlaw, Operator) ·
MACHINE GUNS (Ares, Odin). Each tile shows the name, a side silhouette and one stat line (fire rate · magazine ·
head/body damage) taken from `WeaponDef`.

- The silhouettes are self-made. Render each wrapper scene once in a hidden SubViewport (orthographic side view,
  flat-tinted) and cache it as an ImageTexture. The CC0 models only, no Riot icons.
- Guns not in the drill's set are greyed, with a tooltip ("Pellets don't fit target drills", "This drill is about
  the Sheriff").
- Footer: `DRILL DEFAULT (Vandal)` resets the per-drill pick. A `☐ Use for every drill` checkbox sets
  `PreferredWeapon`. DONE closes.
- Keys: arrows move the focus, Enter picks, B / Esc close. The pick takes effect at once (the record and demo refresh).
- Sizing: 6 columns × 5 rows of tiles about 150×64 k, so it fits 1280×720 at k≈0.67. Check it with gshot
  `-Extra "--window 1280x720"`.

### 3.4 Records: per gun, backward compatible
- **Storage**: `RunRecord.Weapon` is the gun's enum name. Old records have null and mean "the drill's default gun at
  the time", which today is the drill's current default. Old app versions ignore the unknown JSON field
  (System.Text.Json default), so downgrades are safe. No migration and no key changes.
- **Personal best**: keyed by (mode, tier, gun). `Best(mode, tier, gun, defaultGun)` matches `GunOf(r, defaultGun) ==
  gun`. `GameSession.Finish` uses it for `PrevBest`. So the first Phantom Gridshot run says "FIRST RUN WITH THE
  PHANTOM AT VETERAN", not "below your Vandal best".
- **Badges and coach rank**: these are calibrated on the default gun. A run is **rated** if its gun is the drill's
  default or in the drill's `RatedWeapons` (default: `{ default }`; drills whose default is the Vandal also rate the
  Phantom, which answers "allow phantoms more"). `BestBadge(mode)` counts rated runs only. Unrated runs still get a
  score, a PB per gun and a coach review, but the badge area says "Badges are rated with the Vandal or Phantom". The
  coach skill profile drops gun-dependent metrics from unrated runs (§1.5).
- **Telemetry and replays** already carry the gun. `CoachUi.cs:285` counts runs per mode (unchanged).

### 3.5 Showing the gun
- **Results** (`Hud.BuildResults`): the tier line becomes `VETERAN · SILVER–GOLD · PHANTOM`; the chip is `NEW PERSONAL
  BEST · PHANTOM`; the previous-best text is "Veteran best with the Phantom: …". The ammo HUD already names the gun.
- **Preview "YOUR RECORD"**: `BEST · VETERAN · PHANTOM` for the selected gun. When another gun has a higher best, add
  a faint line: "Vandal best 41,200".
- **Stats screen**: rows stay per drill and the default view is ALL GUNS (best across guns, with the gun name in small
  text under the score). A chip row lists only the guns ever used ("ALL · VANDAL · PHANTOM · GHOST") and filters
  `FillTable`.

---

## 4. Mechanics the current code can't do, and the minimal clean way to add them

All the numbers (pellet counts and spread, burst sizes and rates, spin-up times, zoom levels, Shorty's 2 shells,
whether the Bucky reloads one shell at a time, unscope rules) come from `weapons-1.18.md`. The changes stay inside
`WeaponDef` (data), `WeaponController` (behaviour) and a small volley step in `GameSession`.

### 4.1 Proposed `WeaponDef` additions (each with a default, so the current six defs still compile)
| Field | Meaning | Used by |
|---|---|---|
| `WeaponClass Class` | buy-menu column, picker sets, rating | all |
| `int Pellets = 1`, `float PelletSpread = 0` (deg) | pellets per shot and pellet cone; damage tables are **per pellet** | Shorty, Bucky, Judge (+ Bucky air-burst alt) |
| `AltFire Alt` = `None / Zoom / Scope / Burst / ZoomBurst / AirBurst` | what right click does | Classic=Burst (hip 3-round), Bulldog/Stinger=ZoomBurst, Bucky=AirBurst, Shorty/Judge=None, snipers=Scope, rest=Zoom |
| `int BurstCount`, `float BurstRps`, `float BurstSpreadMul` | burst size, rate inside the burst, spread multiplier | Classic, Bulldog, Stinger |
| `float SpinUpRps`, `float SpinUpTime`, `float SpinDownTime` | fire rate when the spin starts, time to reach `Rps`, decay | Ares, Odin |
| `bool Scope` (replaces `Sniper`), `bool UnscopeOnShot` (replaces `Bolt`), `bool BoltCycle` | sniper overlay + zoomed sens; drop scope after a shot; bolt animation/sound | Operator, Marshal, Outlaw, TdF |
| `float AdsMoveMul` (field) | already documented: Outlaw 0.80, Marshal 0.90, Operator 0.72 | Mover |
| `bool Suppressed` | quieter sound, small flash | Ghost, Spectre (Phantom per the research) |
| `int FirstDriftSide = -1` | first horizontal drift side (Phantom +1 today) | WeaponController |
| `float ReloadPerShell = 0` | if > 0, reload adds one shell at a time and firing interrupts it | Bucky (if confirmed) |

`Interval` becomes `1 / Rps` at full spin. A new `MaxRps` is `Rps`, for the demo burst length and SimAim.

### 4.2 Pellets (Shorty, Bucky, Judge) and volley normalisation
- `WeaponController.FireOne` computes the base direction exactly as today (recoil + error cone). For `Pellets > 1` it
  adds `Pellets` directions: pellet 0 is the base direction and the others are
  `PlayerView.Spread(baseDir, PelletSpread, rng, uniform: true)`. Ammo goes down by 1 per shot.
- `Tick` returns `IReadOnlyList<Shot>` with `record struct Shot(Vector3 Dir, bool Accurate, int Volley, int Pellet)`.
  Only `GameSession` and `VmTest` call it.
- `GameSession` resolves a volley **without touching any mode**:
  ```csharp
  // per volley
  int s0 = Mode.Shots, h0 = Mode.Hits, hh0 = Mode.HeadHits; var best = HitZone.None;
  foreach (var p in volley) { Bullet(p.Dir, p.Accurate, recordShot: p.Pellet == 0); best = Better(best, Mode.LastShotZone); }
  Mode.Shots = s0 + 1; Mode.Hits = h0 + (Mode.Hits > h0 ? 1 : 0); Mode.HeadHits = hh0 + (Mode.HeadHits > hh0 ? 1 : 0);
  Mode.LastShotZone = best; // and the telemetry ShotSample of pellet 0 gets Zone = best
  ```
  Damage stays correct because every pellet calls `OnBullet`, which applies `G.Weapon.Damage(zone, dist)` per pellet
  (`BotModes.cs:32`, `DefenseModes.cs:539`, `DeathmatchMode`). Score (kills, multi-kills) stays natural. Accuracy, the
  coach and the Rewind see one shot per trigger pull.
- Telemetry and replay: only pellet 0 gets a `ShotSample` / `PlayerShot` (the Rewind draws one tracer per shot). The
  live game draws a tracer per pellet (fewer for performance: every pellet's impact, a tracer for at most 4).
- Movement/spray error applies to the base direction, so moving shotgun shots spread as a whole, as in VALORANT.

### 4.3 Bursts (Classic right click, Bulldog / Stinger aimed)
`WeaponController` gets `int burstLeft; float burstNextAt;`. When a burst starts (trigger press while aimed for
ZoomBurst; `AltFire()` for Burst), set `burstLeft = BurstCount`. While `burstLeft > 0`, fire at `1/BurstRps`
regardless of the trigger. After the last bullet, `nextShotAt = last + 1/Rps` (the burst cooldown). The spread
multiplier applies during the burst. The heat/recoil model is unchanged: each burst bullet adds heat like a sprayed
bullet.

### 4.4 Alt-fire dispatch (`AdsSession.UpdateAds`)
- `Zoom` / `Scope`: as today (`Scoped = adsHeld && Zoom > 1 …`).
- `Burst` (Classic): `Zoom = 1`, so it never scopes. On the alt press edge, call `Gun.AltFire()`. The ADS outline,
  sens multiplier and telemetry ADS flag stay off.
- `ZoomBurst`: aims as normal. `WeaponController` sees `Scoped` and switches the trigger to burst mode.
- `AirBurst` (Bucky): alt press fires a volley flagged `AirBurst`. The pellets travel together to
  `AirBurstDistance` and then spread. Approximation: offset the cone apex `AirBurstDistance` metres along the base
  ray, then cast each pellet from there. That needs `Bullet(origin, dir)` to take an origin; today it uses
  `View.Eye`, and modes get `origin` already.
- `KeybindTest` still tests alt fire for Burst guns.

### 4.5 Spin-up (Odin, Ares)
`float spin` (0..1): `spin += dt/SpinUpTime` while the trigger is held (and not reloading), and decays with
`SpinDownTime` otherwise. `interval = 1 / Lerp(SpinUpRps, Rps, spin)`. It is computed inside the auto-fire loop, so a
hitch can't dump shots. Sound: a synthesized motor whine (`Sfx.Build`, our own) whose pitch follows `spin`.

### 4.6 Scopes (Marshal, Outlaw) and unscope rules
`Scope = true`, so the overlay, zoomed sens, hold/toggle and the hip error = `FirstShotHip` all work as for the
Operator. The style comes from `WeaponLooks` (`SniperScope.Marshal` / `.Outlaw` are already drawn by
`CrosshairView`). `UnscopeOnShot` per gun replaces `if (Def.Bolt) Scoped = false`. The Outlaw's 2-round magazine and
fast follow-up is just `Mag = 2` + `Rps`. Operator's second zoom level: not modelled today, and it stays out of scope.

### 4.7 Ghost silencer, Shorty's 2 shells, revolver
- Ghost/Spectre: `Suppressed` selects the sound key (`ghost`, `spectre`: §7) and a small flash. The silencer is a
  model attachment (§5). The `Muzzle` marker moves to the silencer tip.
- Shorty: `Mag = 2`, `Pellets` from the research. The viewmodel uses the `NoMag` reload (tilt + hand, no magazine
  swap) and `Shells = false`.
- Sheriff as a revolver: `NoMag` reload, no ejected shells per shot. Optional: drop 6 casings at the reload start.

### 4.8 Not modelled (state it in the changelog)
Wall penetration (the Odin's / Ares' main perk: ValTrainer has no wallbangs), the Operator's second zoom, the
Bucky's/Judge's exact fixed pellet patterns (random cone instead, unless the research gives a pattern), and armour
penetration differences.

---

## 5. Gun models: one CC0 Quaternius model per VALORANT gun

Source: Quaternius **Ultimate Gun Pack (July 2019)**, CC0. I inspected it at
`C:\Users\IdoRaz\AppData\Local\Temp\claude\C--Users-IdoRaz-Desktop-AGY-Projects-valorant-training\fbf26b7d-c776-4050-b015-6a8bc0b65989\scratchpad\gunpack\Ultimate Gun Pack - July 2019\`
(FBX/, OBJ/, Blends/, Preview.jpg; 40 guns + 15 accessories). Side-view renders of every model with its raw size are at
`C:\Users\IdoRaz\AppData\Local\Temp\claude\C--Users-IdoRaz-Desktop-AGY-Projects-valorant-training\fbf26b7d-c776-4050-b015-6a8bc0b65989\scratchpad\gunrender\sheet_a.png`
and `...\gunrender\sheet_b.png`. The script that made them is `...\gunrender\render.py`: run it with
`py -I render.py "<pack>\OBJ" sheet`.

The pack has no LMG/minigun, so the Odin and Ares are bullpups. The Odin gets the pack's Bipod.

**Scale rule.** The hands are posed around each grip, so guns of one class must share one scale or the grips change
size. The current wrappers use about **0.170** for long guns (AK 0.1697, M4 0.170, sniper 0.166) and **0.104** for
the Classic pistol. Use 0.17 for rifles, shotguns, snipers and heavies, about 0.15 for SMGs and 0.104–0.13 for
handguns. Adjust the final scale ±10 % per model from `--vmtest --vmview side` screenshots. Raw lengths are in
OBJ/FBX scene units (barrel along +X; see MANIFEST §5 for the wrapper recipe).

| VALORANT gun | Model (FBX) | Raw L × H | Scale → length | Why / notes |
|---|---|---|---|---|
| Classic | `Pistol_5` (existing `Pistol_Compact.tscn`) | 1.82 × 1.20 | 0.104 → 0.19 m | Already "Classic-like" in MANIFEST. Today it is the Sheriff's model and moves to the Classic. |
| Shorty | `Shotgun_SawedOff` | 3.69 × 1.02 | 0.15 → 0.55 m | Double-barrel sawn-off: exactly the 2 shells. |
| Frenzy | `SubmachineGun_4` | 2.55 × 1.64 | 0.13 → 0.33 m | Smallest SMG, no stock, curved mag: a machine pistol. One-hand pistol grip pose. |
| Ghost | `Pistol_1` + `Accessories/Silencer_1` | 1.82 × 1.16 (+1.75) | 0.104 → 0.19 m + ~0.15 m silencer (silencer ×0.85) | Black slide (reads differently from the grey Classic). The silencer is a child at `Muzzle` and the marker moves to its tip. |
| Sheriff | `Revolver_3` | 1.99 × 0.98 | 0.13 → 0.26 m | Heavy revolver with a top rib, as the Sheriff has. Needs a new pistol pose and a `NoMag` reload. |
| Stinger | `SubmachineGun_2` | 4.04 × 1.85 | 0.15 → 0.61 m | Compact SMG, folding stock, straight mag. Alternative: `SubmachineGun_1` (no stock). |
| Spectre | `SubmachineGun_5` + `Accessories/Silencer_2` | 5.03 × 1.73 | 0.15 → 0.75 m (+ silencer) | Long-receiver SMG with a stock; the Spectre is suppressed. |
| Bucky | `Shotgun_3` | 5.79 × 0.94 | 0.17 → 0.98 m | Wood pump-action with a wood forend (the pump hand). |
| Judge | `Shotgun_ShortStock` | 4.47 × 0.97 | 0.17 → 0.76 m | Pistol-grip compact shotgun. The closest match to an automatic combat shotgun. |
| Bulldog | `AssaultRifle2_3` | 3.75 × 1.46 | 0.17 → 0.64 m | Compact M4 with a big box mag. It stays distinct from the Phantom (`AssaultRifle2_1`). |
| Guardian | `AssaultRifle_2` | 5.42 × 1.60 | 0.17 → 0.92 m | Long rifle with wood furniture, a different silhouette and colour from the black-stock Vandal. Alternative: `AssaultRifle2_2` + `Scope_3` for a DMR look. |
| Phantom | `AssaultRifle2_1` (existing `AssaultRifle_M4.tscn`) | 5.17 × 1.79 | 0.170 → 0.88 m | unchanged |
| Vandal | `AssaultRifle_5` (existing `AssaultRifle_AK.tscn`) | 5.42 × 1.60 | 0.170 → 0.92 m | unchanged |
| Marshal | `SniperRifle_6` | 7.21 × 1.52 | 0.155 → 1.12 m | Light wood-stock hunting bolt-action. Scope included. |
| Outlaw | `SniperRifle_2` | 7.41 × 1.51 | 0.155 → 1.15 m | Grey stock, box mag and muzzle brake: heavier and semi-auto looking. Alternative: `SniperRifle_4` (skeleton stock). |
| Operator | `SniperRifle_3` (existing `SniperRifle_Bolt.tscn`) | 7.25 × 1.20 | 0.166 → 1.20 m | unchanged |
| Ares | `Bullpup_3` | 4.31 × 1.60 | 0.19 → 0.82 m | Bullpup with a large curved mag and a top rail. |
| Odin | `Bullpup_1` + `Accessories/Bipod` (folded under the barrel) | 5.25 × 1.54 | 0.19 → 1.00 m | Longest bullpup, vertical foregrip. The bipod sells "LMG". Alternative: `Bullpup_2`. |
| *Headhunter* (Chamber) | `Pistol_6` (gold, as today) | 2.45 × 1.16 | 0.104 → 0.25 m | Long-slide heavy pistol with a rail. It needs its own pose once the Sheriff is a revolver. |
| *Tour De Force* | unchanged (Operator model, gold) | | | Optional: `SniperRifle_5` (built-in bipod). |

Spare models: AssaultRifle_1/3/4, AssaultRifle2_2/4, Bullpup_2, Pistol_2/3/4, Revolver_1/2/4/5, Shotgun_1/2/4,
SniperRifle_1/4/5, SubmachineGun_1/3 and the Scope_1-3 / Grip / Stock / Flashlight / Bayonet accessories.

**Import recipe (per MANIFEST §5).** Copy the FBX to `assets/weapons/`. Make a wrapper `<Name>.tscn` with Root ›
Model (RotY +90° · uniform scale) › `Muzzle` Marker3D at the barrel tip, and optional `Attach_*` Marker3D nodes for
accessories. Record length, AABB and Muzzle in the MANIFEST table. Note the 14 new models in CREDITS (same pack,
already credited). Measure each pose's anchor points (grip centre, handguard bottom, mag well, ejection port, pump /
bolt handle) from the OBJ vertices with a script (the render script's loader). Don't guess them.

**Poses (VmPoses).** Three bases (`PistolBase` = today's Sheriff pose, `RifleBase` = Vandal, `SniperBase` = Operator)
and `with { Scene, Grip, Support, MagWell, Eject, Kick…, Flags }` per gun. Shotguns: `Support = CupAt(pump)`; the
Bucky's support hand pumps after each shot (reuse the bolt timeline with the support hand). Shorty: one-hand pistol
grip + support under the barrels. Odin/Ares: `KickBack` small, `KickFreq` high. ADS: run `--vmads 1` for the
"[vmtest] ads sight top" check (0.60–0.65 H) on every gun that aims.

---

## 6. Bots

Keep it simple. Only **Deathmatch** gets varied guns:
- Each DM bot rolls **Vandal (55 %) or Phantom (45 %)** at spawn, kept until it dies. `DeathmatchBots.FireAtFoe` and
  `BotBrain` take that `WeaponDef` (interval, damage falloff, burst cadence) instead of `Weapons.Vandal`.
- Its sound is `enemyshot` (AK) or `enemyshot_phantom` (AR-15). Its model is `BotAssets.RifleFor(kind)` (AK or M4
  wrapper).
- No shotgun, sniper or SMG bots: their AI would need new engagement ranges, and a bot Operator already exists in Jump
  Peek / Jiggle Peek.
- All other bot drills keep the Vandal, so their difficulty tuning and benchmarks don't move.

---

## 7. Gun sounds: CC0 candidates (not downloaded)

What exists locally: `assets/audio/weapons` has AK-47 (Vandal, also the bots' `enemyshot`), AR-15 (Phantom), Tikka T3
bolt-action (Operator) and Walther PPQ (Sheriff today), plus the OpenGameArt reloads. The scratchpad's
`dl\oga\Prepared SFX Library\` holds the **five** gun folders of the OpenGameArt copy: AK-47, AR-15, Savage 10 .300
Blackout, Tikka and Walther PPQ. They are empty now (the clips were cut and deleted), but the 194 MB
`Prepared SFX Library.7z` on the OGA page contains them, CC0.

Note: Still North Media's full library had 22 guns, including 3 shotguns (Mossberg, Nova, Model 12) and 6 handguns
(per a secondary source). The OpenGameArt copy has only these 5, and the original sites (freefirearmsfx.com,
stillnorthmedia.com) are dead. I found no archived copy of the full set.

| Need | Candidate | License (verified on its page) | Notes |
|---|---|---|---|
| Marshal / Guardian | **Savage 10 .300 Blackout** from *The Free Firearm Sound Library* (Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney), OGA <https://opengameart.org/content/the-free-firearm-sound-library>, file `Prepared SFX Library.7z` | CC0 | Same archive and credit line as the current gunshots. Bolt-action, lighter than the Tikka: Marshal. A shortened, brighter cut works for the Guardian. |
| Sheriff (revolver) | "357Mag.wav" by Jon285, Freesound #49512 <https://freesound.org/people/Jon285/sounds/49512/> | CC0 | 6 shots of a .357 Magnum, 48 kHz/16-bit stereo, 9 s, noise-reduced. Cut single shots. |
| Shotguns (Shorty, Bucky, Judge) | "shotgun Gunshot.mp3" by ONgiaunbg, Freesound #687399 <https://freesound.org/people/ONgiaunbg/sounds/687399/> | CC0 | Smoothbore shotgun at an open range, 48 kHz, 1.35 s. MP3 only (fine after editing). |
| Shotguns (alt take) | "shotgun targetside.mp3" by cedarstudios, Freesound #148841 <https://freesound.org/people/cedarstudios/sounds/148841/> | CC0 | Field recording, 15.5 s, includes the pump: Bucky pump sound. 128 kbps MP3. |
| Shotgun (punchy, short) | "Shotgun Shot" by LeMudCrab, Freesound #163455 <https://freesound.org/people/LeMudCrab/sounds/163455/> | CC0 | 0.69 s mono WAV made for a game; may be designed rather than recorded. Judge. |
| Shotgun pump | "Gun reload sounds" by SpringySpringo (already in assets, OGA) | CC0 | Contains a shotgun-cocking WAV. Use it for the Bucky pump and the Shorty reload. |
| Odin / Ares | "Clean Machine Gun Burst" by qubodup, Freesound #482122 <https://freesound.org/people/qubodup/sounds/482122/> | CC0 (license field) | 1.6 s burst from public-domain US military video, 48 kHz. Its description also says "see profile for CC BY attribution requirements". Credit qubodup anyway. Layer it under a pitched-down AK single shot for the loop. |
| Heavy MG layer (fallback) | "Machine Guns#1.wav" by ctobin, Freesound #436585 <https://freesound.org/people/ctobin/sounds/436585/> | CC0 | 4 min mix of heavy and light MG bursts, compiled and processed from unnamed sources (provenance unclear). Low priority. |
| Pistol alternatives | "Gunshot Sounds" by Tabasco, OGA <https://opengameart.org/content/gunshot-sounds> | CC0 | CZ-52 pistol, Mosin, SKS, shotgun. The recorder clipped; the author calls it "usable". Last resort. |
| Avoid | "Gunshots" by kurt (OGA) | CC0 claimed, but the uploader says "these were not my guns / found on my computer" | Provenance questioned in its comments. Don't use. |
| Avoid | "Silencers" by EmoPreben (OGA node 118666) | CC0 | Voice-made "oldschool" silencer: wrong style. |
| Avoid | "Machine Gun Burst" by qubodup #482121; "Gun Sound Effects" (OGA); Semi-Auto Shotgun model sounds | CC BY / unclear / CC-BY 3.0 | Not CC0. |

No good CC0 suppressed-pistol or SMG field recording turned up. Make those **ourselves from the CC0 recordings we
already ship**, with a reproducible script in `tools/sfx/` (like `flash_sfx.py`). The derived files stay CC0 and are
credited as "processed from …":
- Ghost: Walther PPQ → low-pass ~2.5 kHz, cut to ~0.25 s, −10 dB, plus a slide-click transient.
- Spectre: AR-15 → the same suppressor chain, pitched up 6 %.
- Stinger / Frenzy: AR-15 / PPQ pitched up 8–12 %, tail shortened (fast "zip").
- Bulldog: AR-15 take 2 with a +2 dB low-mid boost (so it doesn't sound like the Phantom).
- Classic: Walther PPQ (the current "sheriff" clips get renamed "classic").
- Outlaw: Tikka with a heavier low end. Operator: unchanged.
- Odin / Ares spin-up: synthesized in `Sfx.Build`.

Sound keys: one per gun (`classic`, `shorty`, … `odin`). `WeaponLooks` maps a gun to its key, its reload
(`reload_pistol` / `reload_rifle` / `reload_shotgun` (pump clip) / `reload_lmg` (rifle reload pitched down)) and its
volume. Unknown keys fall back to `vandal` / `sheriff`, so a missing file never silences a gun.

---

## 8. Work split: Phase 0, then 4 parallel agents, then integration

Shared rules for every agent:
- **Build**: `dotnet build ValTrainerGodot\ValTrainer.csproj -nologo`, the same command CI runs.
- **Run / screenshots**: `tools\gshot.ps1 -Mode <key> -At 5,8 -Name <n> -Extra "<flags>"`. It runs `--dev`
  (windowed 1600×900, never writes stats/settings), captures only its own window, and kills only the processes it
  launched (keep it that way: Discord was killed once). Godot runs one at a time through the `Global\ValTrainerGodotSlot`
  mutex, so parallel agents queue rather than collide. Output goes to `%TEMP%\gshot` (or `$env:GSHOT_OUT`).
- **Headless checks**: `tools\selftest.ps1` (coach self-test + .vtr round trip, `--culture-test` invariant/de-DE/tr-TR,
  bug-report checks, smoke runs of the drills with `--simaim`, `--replay-check`, `--insights`). Useful single
  flags: `--dev --headless --fixed-fps 60 --preview-check [--preview-check-only k1,k2] [--preview-check-secs N]`;
  `--dev --culture-test`; `--coach-selftest`; `--movecheck`. Always add `--data-dir <tmp> --valorant-dir <empty>` when
  running by hand, as selftest does.
- **Viewmodel**: `-Extra "--vmtest --vmkind <Gun> [--vmview side|left|top] [--vmads 1] [--vmfire] [--vmreload loop] [--vmshots T --vmout prefix]"`.
- Don't commit (the maintainer commits). Don't edit files another agent owns. Ask through the lead instead.

### Phase 0: foundation (Agent A, done first, about half a day, blocks the rest)
Owns these temporarily: `Weapons.cs`, `TrainingMode.cs`, `GameSession.cs`, `Stats.cs`, `AppSettings.cs`, new
`Core/WeaponPicks.cs`, new `Game/Weapon/WeaponLooks.cs` (stub), `SimAim.cs` (constructor only), `Main.cs`
(`StartMode` signature).
1. Append the 14 `WeaponKind` values with explicit numbers. Add `WeaponClass`, `Weapons.All/Buyable/Parse`. Add the
   new `WeaponDef` fields with defaults; `Sniper`→`Scope`, `Bolt`→`UnscopeOnShot`/`BoltCycle`, `AdsMoveMul` becomes a
   field. Add all 18 `WeaponDef`s from `weapons-1.18.md` (estimates marked as such in the doc comments).
2. `WeaponLooks` stub: `For(kind)` returns today's mapping (pose, sound, reload, sight, scope style). New guns
   temporarily borrow their class's look (SMG → Vandal pose, shotgun → Vandal, pistol → Sheriff, sniper → Operator,
   heavy → Vandal). Replace the 6 scattered switches with it (GameSession sounds, AdsSession scope/sight,
   RewindView3D, Viewmodel family, VmPoses.For).
3. `TrainingMode.WeaponChoice/Pick/ActiveWeapon`, the `WeaponSets` constants, `MakeRecord` gun.
   `GameSession.WeaponPick`, resolve in `Restart`, `Mode.ActiveWeapon` in the 4 places. `RunRecord.Weapon`.
   `AppSettings.PreferredWeapon/WeaponPicks`. `WeaponPicks.Resolve/Remember/SetPreferred`. `--gun` dev flag.
   `Main.StartMode(make, free, gun)`.
4. `SimAim(profile, key, simKind, WeaponDef? gun = null)`, with today's logic as the fallback.

Delivers: the build passes; `tools\selftest.ps1` passes unchanged (no behaviour change without a pick).
`gshot -Mode peek -Extra "--gun Spectre"` plays with the Spectre's stats on the borrowed Vandal model.

### Then in parallel
**Agent A: mechanics.** Owns `Core/Weapons.cs`, `Game/Weapon/WeaponController.cs`, `Game/AdsSession.cs`,
`Game/GameSession.cs`, `Game/Mover.cs`/`MovementBody.cs` (if needed), `Game/KeybindTest.cs`, new
`Game/WeaponCheck.cs`.
- Pellets + volley normalisation (§4.2), bursts (§4.3), alt dispatch incl. air burst (§4.4), spin-up (§4.5), scope
  and unscope rules (§4.6), suppressed flag, per-shell reload. Tracer cap for pellets.
- New headless self-test `--dev --headless --fixed-fps 60 --weapon-check`, modelled on `MovementCheck`. For every
  gun it simulates trigger patterns and checks against `WeaponDef`: rounds per second (incl. spin-up curve), burst
  size/rate/cooldown, pellets per shot and ammo used, mag + reload time, unscope after the shot, the first-shot
  error. It prints `[weapon-check] RESULT: PASS|FAIL` and the exit code is the number of failures.
- Test: `--weapon-check`; `gshot -Mode strafebots -At 4,6 -Extra "--gun Judge"`;
  `gshot -Mode operator -Extra "--gun Marshal --force-ads"`; `--gun Classic` + right click via `--keybind-test`.

**Agent B: art, audio, viewmodel.** Owns `assets/weapons/**`, `assets/audio/weapons/**`, `assets/MANIFEST.md`,
`CREDITS.md`, `src/UI/AboutLicenses.cs`, `tools/sfx/**`, `src/Game/Weapon/{Viewmodel,VmPoses,VmHands,VmMesh,VmMaterials,VmTest,MuzzleFlash,WeaponLooks}.cs`,
`src/UI/AdsSight.cs`, `src/Audio/Sfx.cs`, `src/Game/Bots/BotAssets.cs`.
- Import the 14 models + Silencer_1/Silencer_2/Bipod as wrapper scenes (§5). Measure anchors by script. Fill the
  MANIFEST table. Write the poses, pose flags (`Pistol`, `NoMag`, `BoltCycle`, pump) and attachments. Give the
  Headhunter its own pose. ADS outlines for every gun that aims. Fill `WeaponLooks` completely. Make the sounds:
  download the CC0 files in §7 into a fresh scratch folder, cut them, generate the derived ones with a
  `tools/sfx/gun_sfx.py` script, and credit each one. `BotAssets.RifleFor`.
- Test, per gun: `gshot -At 3 -Extra "--vmtest --vmkind <Gun> --vmview side --vmshots 3 --vmout <Gun>"` (proportions and
  hands), `--vmads 1` (ADS check line in the log), `--vmfire` (flash, shells, kick), `--vmreload loop`, `--vmleft`. Then
  a contact sheet of all 18 at hip and ADS for the lead to review.

**Agent C: picker UX and records.** Owns `src/UI/{DrillPreview,GunPicker(new),Hud,StatsScreen,SettingsScreen,MenuScreen,AgentsScreen,Widgets}.cs`,
`src/Core/Stats.cs` (helpers after Phase 0), `src/Core/CultureCheck.cs`, `src/Warmup/WarmupData.cs`.
- The WEAPON row + B key + overlay (§3.3) with self-rendered silhouettes. The preview for map drills without a demo
  (the `HasPreview`/`PreviewDemo` change is a 2-line edit in `TrainingMode.cs`; coordinate with D, who owns the modes,
  or C makes it after Phase 0). Demo restart on pick. YOUR RECORD per gun. Results gun chip + PB per gun +
  "unrated" note. Stats gun filter. Settings "Preferred gun". Agents-page gun chip. Warm-up history filtered by gun.
  `PreviewCheck --preview-gun`. CultureCheck data checks (§1.1).
- Test: `gshot -Extra "--preview gridshot" -At 3`, then press B with `--preview-answer` extended to `gun:Phantom`
  (add it); screenshots at `--window 1280x720` and `--render-size 2560x1440`; `--screen stats`; `--dev --culture-test`
  (+ `--culture de-DE`); `--preview-check --preview-gun Phantom`. Hand-check that an old `stats.json` from the user's
  data folder copied into a temp `--data-dir` still shows the same PBs.

**Agent D: modes, bots, analysis.** Owns `src/Modes/**`, `src/Game/Bots/BotBrain.cs`, `src/Analysis/**` (except the
Phase 0 SimAim constructor), `src/Game/Replay/**`, `src/UI/RewindView3D.cs` (if anything is left after Phase 0),
`tools/selftest.ps1`.
- `WeaponChoice` (and `RatedWeapons`) per mode, per the §2 table. `RecoilModes.cs:301` fix. Deathmatch bot loadout
  (§6). SimAim per gun (taps, bursts, shotguns, spin-up). RunAnalysis spray `required` per gun. Coach: drop
  gun-dependent metrics for unrated runs. Optional `spray:<gun>` card (§2.3). Add `--gun` smoke runs to selftest
  (e.g. `peek --gun Judge`, `gridshot --gun Ghost`, `operator --gun Outlaw`, `deathmatch --gun Odin`,
  `spray_transfer --gun Spectre`).
- Test: `tools\selftest.ps1` (all green, including the new `--gun` runs); `--coach-selftest`; `--replay-check` on
  a `--gun Judge` telemetry (one PlayerShot per volley); `--preview-check` after C lands.

### Integration pass (one agent, last)
Version bump 1.18.0 + CHANGELOG + What's New ("All 18 guns + pick yours before each drill"). Full `selftest.ps1`.
`--weapon-check`. `--preview-check` with and without `--preview-gun`. A gshot tour (picker, each class in a bot drill,
results with the gun chip, the stats filter). Note in MANIFEST/CREDITS what was not modelled (§4.8).

### Order and dependencies
```
Phase 0 (A) ──► A mechanics ─┐
            ├─► B art/audio ─┼─► Integration
            ├─► C picker UI ─┤
            └─► D modes/AI ──┘   (C's PreviewCheck --preview-gun and D's selftest runs meet at integration)
```
Main conflict risks, and how ownership avoids them: `GameSession.cs` (A only), `TrainingMode.cs` (Phase 0, then only
the 2-line `HasPreview` change, agreed between C and D), `Weapons.cs` (A; B and D read it), `WeaponLooks.cs` (B after
Phase 0).

---

## Sources (audio license checks)
- [The Free Firearm Sound Library (OpenGameArt)](https://opengameart.org/content/the-free-firearm-sound-library)
- [jlagun/gierka issue #55 (summary of the full 22-gun library)](https://github.com/jlagun/gierka/issues/55)
- [PanderMusubi/sound-effects-library-weapons (CC0 repackage)](https://github.com/PanderMusubi/sound-effects-library-weapons)
- [357Mag.wav by Jon285](https://freesound.org/people/Jon285/sounds/49512/)
- [shotgun Gunshot.mp3 by ONgiaunbg](https://freesound.org/people/ONgiaunbg/sounds/687399/)
- [shotgun targetside.mp3 by cedarstudios](https://freesound.org/people/cedarstudios/sounds/148841/)
- [Shotgun Shot by LeMudCrab](https://freesound.org/people/LeMudCrab/sounds/163455/)
- [Clean Machine Gun Burst by qubodup](https://freesound.org/people/qubodup/sounds/482122/)
- [Machine Guns#1.wav by ctobin](https://freesound.org/people/ctobin/sounds/436585/)
- [Gunshot Sounds by Tabasco (OGA)](https://opengameart.org/content/gunshot-sounds)
- [Gunshots by kurt (OGA)](https://opengameart.org/content/gunshots)
- [Silencers by EmoPreben (OGA)](https://opengameart.org/node/118666)
- [Gun reload sounds by SpringySpringo (OGA)](https://opengameart.org/content/gun-reload-sounds)
