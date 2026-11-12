# Skins 2 + progression: the shared contracts (v1.19)

Status: foundation in code (compiling stubs). Designs: `skins2-design-1.19.md` (skins), `progression-design-1.19.md`
(progression). Both proposed an interface between progression and skins; this page is the ONE reconciled contract that
every parallel agent builds against. Change a signature here only together with everyone who calls it.

## 0. What was reconciled

| Topic | skins2-design | progression-design | Contract |
|---|---|---|---|
| Item ids | `line`, `line@n`, `line.variant` | `line`, `line/vfx`, `line/v2` | `line` (level 1), `line/n` (level n ≥ 2), `line/variant`, `factory` (`ItemIds`) |
| Interface | `ISkinUnlocks` (Has/HowTo/TryUnlock, SP points) | `Progress` static (IsUnlocked/Status/events) | `Progress` static facade; no SP currency, nothing is bought: items unlock when their requirement is met |
| Who owns pacing | gate tiers U0–U6 + SP costs in the catalogue | table in Progress | Progress only (`UnlockTable`); the catalogue has no numbers |
| Editions | Select, Deluxe, Premium, Ultra, Exclusive | Select < Deluxe < Premium < Exclusive < Ultra | `Edition { Select, Deluxe, Premium, Ultra, Exclusive }` (display order); Ultra is the top unlock tier |
| Level vs slot | numbered levels bundling features | slots base/vfx/sound/kill/finisher | numbered levels; each level reports one `UnlockSlot` so Progress can gate kill effects / finishers on stars |
| Select | free (today's skins stay free) | levels 1–9 | **free** (stub and default table) |
| Exclusive line | challenges (`bench.gold5` …) | level + stars | challenges, written in the grammar as overrides (`bench:gold:5`, `lockins:10`, `finder:sens`) |
| Stored equip | `line.variant` + `SkinLevels[line]` | — | per gun `line[/variant][@level]` in the existing `GunSkins` / `GunSkinAll` fields; no level = highest unlocked |
| Dev unlock flag | `--skins-unlock-all` | `--unlock-all` | `--progress-unlock-all` and `--skins-unlock-all` (aliases, dev only, never saved) |

Namespace note: the folder is `src/Progress`, the namespace is **`ValTrainer.Progression`** (a class `Progress` inside a
namespace `ValTrainer.Progress` would be shadowed by the namespace everywhere under `ValTrainer.*`).

## 1. The contracts

### 1.1 Item ids (`src/Progress/ItemIds.cs`)
```csharp
ItemIds.Factory = "factory"; ItemIds.Base(line); ItemIds.Level(line, n) /* n<=1 -> line */; ItemIds.Variant(line, v);
bool ItemIds.TryParse(string? id, out string line, out int level, out string? variant); bool ItemIds.IsToken(string);
```
Lowercase ASCII, stable forever (stored in progress.json). Variant ids start with a letter, levels are digits.

### 1.2 Progress facade (`src/Progress/ProgressApi.cs`, main thread only, safe before `Init`)
```csharp
static class Progress {
  void Init();                                         // Main._Ready (done); loads progress.json; dev flags
  bool DevUnlockAll { get; set; }                      // --progress-unlock-all / --skins-unlock-all
  ProgressLoad LoadResult; ProgressFile Data;          // Data is read-only for everyone but Progress core
  void RegisterLines(IEnumerable<UnlockLine>);         // optional: the catalogue is pulled lazily
  IReadOnlyList<UnlockLine> Lines; IEnumerable<string> AllItems; Requirement RequirementOf(string itemId);
  int Xp, Level, XpIntoLevel, XpForLevel, Stars, Rested; RunXp? LastRun; ProgressFacts Facts;
  bool IsUnlocked(string? itemId);                     // factory true; unknown false; permanent unlocks never revoked
  UnlockStatus Status(string itemId);                  // translated text, progress fraction/text, structural blocker
  IReadOnlyList<UnlockStatus> NextUnlocks(int n); IReadOnlyList<UnlockInfo> RecentUnlocks(int n);
  IReadOnlyList<string> Unseen; void MarkSeen(string itemId);
  event Action<UnlockInfo>? Unlocked;                  // Main forwards to GunSkins.NotifyChanged (done)
  event Action<int,int>? LevelUp; event Action? Changed;
  internal RaiseUnlocked/RaiseLevelUp/RaiseChanged;    // Progress core only
  RunXp? OnRunFinished(RunFacts run);                  // feeds: stubs (Progress core fills in)
  void OnLockInFinished(Warmup.WarmupRecord rec); void OnProfile(Analysis.SkillProfile p);
}
record UnlockStatus(string ItemId, bool Known, bool Unlocked, Requirement Req, string Text, float Progress01, string ProgressText, string? Blocker) { string Summary; }
record UnlockInfo(string ItemId, string Source, DateTime When);   // Source: run|lockin|challenge|coach|retro|dev
record RunXp(int Total, IReadOnlyList<XpPart> Parts, int StarsGained, IReadOnlyList<string> StarReasons, int LevelBefore, int LevelAfter,
             float BarBefore, float BarAfter, IReadOnlyList<ChallengeTick> Challenges, IReadOnlyList<string> NewUnlocks);
record RunFacts(RunRecord Record, bool Stored, float ActiveSecs, string? SkinLine, bool LockInStep, bool Adaptive, bool Finder);
static class XpCurve { int CostOf(int level); int TotalFor(int level); int LevelOf(int xp); }   // min(500+100(L-1), 2500)
```
Structural rules enforced by `Status` / `IsUnlocked`: level n needs n−1; a variant needs the top level on Premium/Ultra
and only the base elsewhere (`UnlockLine.VariantNeedsLevel`).

**Stub behaviour now:** Factory + the 9 Select lines unlocked, every other item locked with its requirement text;
XP/stars 0 unless `--progress-xp N`, `--progress-level N`, `--progress-stars N` (dev, in memory); feeds do nothing;
progress.json is read, never written.

### 1.3 Requirement grammar (`src/Progress/Requirements.cs`, table in `UnlockTable.cs`)
Terms: `free`, `level:N`, `stars:N`, `rankup:overall:N`, `rankup:skill:N`, `coach:placed`, `badgeups:N`, `pbstars:N`,
`lockins:N`, `streak:N`, `drills:N`, `badge:<rookie..pro>`, `with:<line>:runs:N`, `with:<line>:pb`,
`with:<line>:badgeup`, `weeklyimprove:N`, `bench:<iron..radiant>:N`, `finder:sens`. `+` = all of, `|` = any of
(`+` binds tighter), parentheses group. Unknown / malformed → never met (logged once). Types: `ReqKind`, `ReqTerm`
(`Measure/Met/Fraction/Text/ProgressText` against `ProgressFacts`), `ReqNode` (`ReqLeaf`, `ReqAll`, `ReqAny`),
`Requirement(Source, Root)`. `UnlockTable.Build(lines)` gives every item's source string: edition defaults
(`Level(line, n)`, `Variant(line, i)`) + `Overrides` by item id.

### 1.4 progress.json (`src/Progress/ProgressData.cs`, `ProgressStore.cs`)
`ProgressFile` (Version, MinReader, Salt, CreatedWith, Xp, Stars, CreditedThrough, LockInsThrough, Retro, Rested,
LastActiveDay, Days, Daily, Weekly, Streak, Coach, Counters, With, Unlocked, Unseen) and nested `RetroInfo`, `DayInfo`,
`ChallengeSet`, `ChallengeItem`, `StreakInfo`, `CoachTrackInfo`, `CoachDay`, `CounterInfo`, `WithLine`. Every class
derives `ProgressJson` (`[JsonExtensionData] Extra`): unknown fields round-trip. `ProgressStore.Load(out ProgressLoad)`
(Missing / Loaded / Corrupt → `.corrupt` / Unreadable → read-only / Newer → read-only), `Parse`, `Serialize`,
`Save` (no-op unless `CanWrite` = `Main.SavesData` and not read-only), `CurrentVersion = 1`.

### 1.5 Catalogue (`src/Game/Skins/SkinModel.cs`, `SkinCatalog.cs`)
```csharp
[Flags] enum SkinFeature { Animated, Reactive, Sounds, KillFx, Finisher, KillBanner, KillSounds, KillCounter, Tracers, EquipFlourish }
record FxPalette(Color Main, Color Accent, Color Alt);
record SkinLevel(int N, SkinFeature Adds, string Blurb) { Func<GunSkin,GunSkin>? Look; string? Sounds, KillFx, Finisher, Banner; UnlockSlot Slot; }
record SkinVariant(string Id, string Name, Func<GunSkin,GunSkin> Recolour, FxPalette? Palette = null);
record SkinLine(string Id, string Name, string Blurb, Edition Edition, GunSkin Look, IReadOnlyList<SkinLevel> Levels,
                IReadOnlyList<SkinVariant> Variants, FxPalette Palette) { string? Collection; string BaseName; int MaxLevel;
                SkinFeature FeaturesAt(int); SkinVariant? Variant(string?); GunSkin LookAt(int level, SkinVariant?); UnlockLine ToUnlockLine(int order); }
readonly record struct SkinState(SkinLine Line, int Level, SkinVariant? Variant)   // what a gun wears, clamped
  { Features; Has(f); Palette; Look; Sounds; KillFx; Finisher; Banner; Color? Tracer; LevelItem; VariantItem; }
static class SkinCatalog { Lines; Get(id); Legacy; EditionName(e); UnlockLines(); SkinState? Resolve(SkinChoice, Func<string,bool> unlocked); }
```
21 lines = 14 catalogue entries (Field Issue ×5 and Camo Kit ×4 Select, 6 Deluxe, 4 Premium, Phosphor Trace Ultra,
Calibrated Exclusive), all levels, variants and colours from skins2-design §3. Effect ids (contracts with their owners):

| Line | Sound set (L) | Kill effect (L) | Finisher (L) | Banner (L) | Other |
|---|---|---|---|---|---|
| honeycomb (Lattice) | honeycomb (3) | hexshards (3) | hexdome (4) | honeycomb (4) | L2 Anim 0.25 React 1 |
| rainfront | rainfront (3) | splashcrown (3) | cloudburst (4) | rainfront (4) | L2 Anim 0.3 React 0.8 |
| strata | strata (3) | rockshards (3) | upthrust (4) | strata (4) | L2 Anim 0.1; Agate `Sub = 1` |
| escapement | escapement (3) | hairspring (3) | mainspring (4) | escapement (4) | L3 EquipFlourish |
| phosphor | phosphor (3) | beamtrace (3) | poweroff (4) | phosphor (5) | L3 Tracers; L5 Counter + KillSounds |
| calibrated | calibrated (2) | bracket (2) | pass (3) | — | kill distance callout |

`GunSkin` (the look record, `GunSkins.cs`) gained `Sub` (shader `skin_sub`), `React` (fire pulse gain), `KillGlow`
(kill pulse gain), `Counter` (digits); `SkinPattern` gained `Strata 12, Guilloche 13, Rain 14, Graticule 15,
Calibration 16` (no shader branch yet: they draw as a plain `col_a` finish). The v1.19-dev ids are all still line ids
(`SkinCatalog.Legacy`, identity); old `honeycomb` = Lattice at its highest unlocked level (its old animated look is L2).

### 1.6 Equip model (`GunSkins` facade, `SkinChoice`)
`readonly record struct SkinChoice(string Line, int Level = 0, string? Variant = null)`: `Parse` accepts
`line[/variant][@level]`, item ids (`line/3`), `line.variant@n` and legacy ids; `ToString()` is the canonical stored
form. `GunSkins`: `All` (base looks), `Get`, `Line`, `IsUnlocked(id)`, `For(gun)` (look), `StateFor(gun)` (SkinState?),
`ChoiceFor(gun)` (unclamped), `IdFor(gun)` (resolved line id), `ShowsLess(gun)`, `OwnId`, `Set(gun, string?)`,
`SetChoice(gun, SkinChoice?)`, `SetAll`, `SetDefault`, `ResetAll`, `Summary`, `NameOf`, `DevSkin`, `Changed`,
`NotifyChanged`. Resolution: dev `--skin` (ignores locks) → own entry → `GunSkinAll` → factory, then clamped:
**a locked line shows Factory (not GunSkinAll) and the choice is kept**; a locked level → highest unlocked below it; a
locked variant → base palette.

### 1.7 Kills and rounds (`src/Game/Skins/KillEvents.cs`, `IGame`)
```csharp
readonly record struct KillInfo(BotCharacter Bot, Vector3 Feet, Vector3 Chest, HitZone Zone, float Dist, WeaponKind Gun, float Time)
  { bool Headshot; KillDrill Drill {init;} bool RoundWinning {init;} int LifeKills {init;}
    static KillInfo For(IGame g, BotCharacter bot, HitZone zone, float dist, bool roundWinning = false); }
enum KillDrill { Unknown, Round, Deathmatch, Rep, Target, Measuring }
readonly record struct RoundEndInfo(RoundEndKind Kind, float Time) { bool PlayerWon; }   // RoundWon, RoundLost, MatchWon, RunEnded
enum FinisherReason { RoundWon, MatchWon, LifeStreak, Dev }; readonly record struct FinisherCue(KillInfo Kill, bool Lite, FinisherReason Reason);
interface IGame { void OnPlayerKill(Skins.KillInfo kill) { } void OnRoundEnd(Skins.RoundEndInfo end) { } }   // default no-ops
```
Raised nowhere yet. `TODO(skins2 kills agent)` markers: `BotModes.ShootBots` (after `best.Die`), `DefenseModes`
direct kill, `GameSession` (prewarm, shot sound, reload sound, `Event` round_won watch, `Bullet` player-bullet flag,
`SpawnBot` spawn-clear); `TODO(skins2 progress core)` at `GameSession.Finish`.

### 1.8 Audio (`src/Game/Skins/SkinAudio.cs`, `Sfx.SkinBus = "Skin"`)
`SkinAudio.Enabled`, `Volume`, `Preload(SkinState?)`, `PlayFireLayer(s, gun, pitch)`, `PlayBoltLayer(s, gun)`,
`PlayReloadLayer(s, gun, pitch)`, `PlayEquip(s, gun)`, `bool PlayKillSound(s, n, vol)` (false = caller plays factory
`kill<n>`), `PlayKillEffect(s)`, `PlayFinisher(s, lite)`, `PlayCounterTick(s)`, `StopAll()`. All no-ops now. Callers
play the factory sound first, then the layer at the same pitch, and skip `Demo`.

### 1.9 Visuals and FX stubs
`Viewmodel.OnPlayerKill()` and `Viewmodel.SetKillCount(int kills, bool gg = false)` (no-ops).
`KillFxWorld : Node3D` (`Create(root, state)`, `Setup`, `Kill(in KillInfo, SkinState, Color enemy)`,
`Finisher(in FinisherCue, SkinState, Color enemy)`, `ClearNear(point, radius)`, `ClearAll()`, static `Mode`,
`FinishersOn`); `enum SkinKillFxMode { Off, GunOnly, Full }`.

### 1.10 Settings (`AppSettings`, persisted like the other fields)
`SkinSounds = true`, `SkinKillFx = 2` (0 off, 1 gun only, 2 full), `SkinFinishers = true`, `SkinKillCounter = true`,
`SkinFxVolume = 0.8f`. `GunSkins` / `GunSkinAll` now hold `SkinChoice` strings.

### 1.11 Coach
`Bench.Calibration = 1` (`src/Analysis/Benchmarks.cs`): the coach agent bumps it with the recalibration; Progress
stores it with each coach sample and re-places quietly when it changes.

### 1.12 Dev flags (all `--dev` only, never saved)
`--progress-unlock-all` / `--skins-unlock-all`, `--progress-xp N`, `--progress-level N`, `--progress-stars N`,
`--skin line[/variant][@level]` (also `line.variant@n`; ignores locks), `--screen skins` with `--skins-gun`,
`--skins-skin`, `--skins-view`, `--skins-apply`.

## 2. Who owns which files

| Agent | Owns (create / change) | May touch (small, listed) |
|---|---|---|
| **Coach** | `src/Analysis/**`, `src/UI/CoachUi.cs` (coach screen) | `Bench.Calibration` bump |
| **Progress core** | `src/Progress/**` (fill the stubs: XP, stars, coach track, challenges, streak, retro, store, dev tools, `--progress-test`) | `GameSession.Finish` hook (`OnRunFinished`), `WarmupRunner.Complete` (`OnLockInFinished`), `CoachUi.CoachData` (`OnProfile`, one call), `Main._Ready` (`Progress.Init` already there; `--progress-test`/`--screen progress` exits), `tools/selftest.ps1` (one step) |
| **Progress UI** | new `src/UI/ProgressScreen.cs`, `src/UI/ProgressWidgets.cs` (account strip, XP bar, challenge cards, level track, unlock toast, reveal) | `MenuScreen` header strip + SKINS dot + reveal queue, `Hud.BuildResults` XP row, `LockInHandoff` one line, `Main.ShowProgress()` |
| **Skins data/UI** | `src/Game/Skins/SkinCatalog.cs`, `SkinModel.cs`, `src/Game/Weapon/GunSkins.cs`, `src/UI/SkinPicker.cs`, `src/UI/SkinPreview.cs` (+ SHOWCASE), new `src/UI/SkinLevelTrack.cs`, `SettingsScreen` skin-effects rows | `--screen skins` dev args |
| **Skins visuals** | `assets/shaders/weapon/viewmodel.gdshader*` (patterns 12–16, `skin_sub`, `fire_pulse`, `kill_pulse`, `gun_heat`, counter digits, Lite), `src/Game/Weapon/VmMaterials.cs`, `Viewmodel.cs`, `VmTest.cs` | `GunSkin` look fields (ask Skins data/UI) |
| **Skins audio** | `tools/sfx/skin_sfx.py`, `assets/audio/skins/**`, `src/Game/Skins/SkinAudio.cs`, `src/Audio/Sfx.cs` (Register, Skin bus, voice pool, ducking, `--dump-kills`) | `CREDITS.md` line |
| **Kills/FX** | new `src/Game/Skins/KillTracker.cs`, `KillFxWorld.cs`, `KillFxRecipes.cs`, `assets/shaders/skins/skinfx.gdshader`; `src/Game/Skins/KillEvents.cs` | `IGame.cs`, `GameSession.cs` (all skins TODOs), `AdsSession`/`AbilitySession` (equip sound), `BotModes.cs`, `DefenseModes.cs`, `TrainingMode.cs` (`SkinFxAllowed`, `KillDrill`, kill-sound key), finder modes (override), `Hud.cs` kill banner style only |

Shared files (append in small separate edits, never rewrite): `src/Core/CultureCheck.cs` (each agent adds its own
check method, called from one line), `translations/*.json`, `CHANGELOG.md`, `CREDITS.md`. `Hud.cs`: Progress UI owns
the results XP row, Kills/FX only `DrawKillBanner`. `GameSession.cs`: Kills/FX owns it except the one Progress line in
`Finish`.

## 3. Rules for every agent
- **Preserve line endings and BOM** of every file you edit (the tree mixes LF and CRLF; `AppSettings.cs` has a BOM).
  New files: UTF-8 without BOM, LF.
- **New UI text through `L`** (`L.T` / `L.F` / `L.P` / `L.N` for data, `L.Upper`), per `i18n-howto.md`. Never `L.T` in
  a static initializer. Skin, level and variant names are English data (L.N), translated where shown. Logic never
  compares translated text (use ids, enums, `ReqKind`).
- **No Riot assets, names or look-alikes**: procedural shaders, our own synthesis (`tools/sfx`) and CC0 files in
  `CREDITS.md` only. Cosmetic only: recoil, timings, damage, hit registration, scores, telemetry, replays, stats and
  the coach must not change.
- **Dev runs never write** (settings, stats, progress.json, telemetry): `Main.SavesData` / `AppSettings.ReadOnly` /
  `ProgressStore.CanWrite`. `tools/selftest.ps1` fails if its data dir gets a file.
- Test tools only start and kill their own processes (`gshot.ps1` / `selftest.ps1` already do). One Godot at a time.
- Screenshots of locked content need `--skins-unlock-all` (or `--progress-level N --progress-stars N`).
- Verify: `dotnet build` with no new warnings, `tools/selftest.ps1 -Project <copy>`, gshot of what you changed.

## 4. Things to know
- Locks are live now: Deluxe/Premium/Ultra/Exclusive lines show Factory in drills until unlocked. The picker dims them
  with a padlock, previews them and shows `Status(...).Summary`; Settings' arrows skip them.
- Swatch / look ids: `line` for the base palette, `line/variant` for variants (`SkinLine.LookAt`); the level is not in
  the id.
- Renamed display names (Lattice, Signal, Anodize, Temper, Candy Flake) and all new names have no translations yet.
- `TrainingMode.KillDrill` / `SkinFxAllowed` don't exist yet (Kills/FX adds them); `KillInfo.Drill` defaults to
  `Unknown`.
- `Progress.Facts` only has progress.json numbers; the stats-derived terms (`badge:`, `drills:`, `bench:`,
  `finder:sens`) are 0 until Progress core fills them.
