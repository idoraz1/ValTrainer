# Crosshair switcher (1.19): research + design

Request (Discord, Dixi): "an option to switch between our in-game crosshairs (I know you can copy paste crosshair
codes but it would just make life easier if i can scroll through my current crosshairs like in the game)".

Hard rules (unchanged): ValTrainer only READS VALORANT's local config files. It never writes them, never reads game
memory, never parses .vrf files, and never automates input. Everything below is ValTrainer-only state in settings.json.

Status: research and design only. No source file was changed.

---

## 1. Code map: how the crosshair works today

### 1.1 Reading VALORANT's config

| What | Where |
|---|---|
| Config root `%LOCALAPPDATA%\VALORANT\Saved\Config` (dev override `--valorant-dir`) | `src/Core/Paths.cs:136-147` |
| Account scan: `<id>\Windows\RiotUserSettings.ini` must exist; last-used from `WindowsClient\RiotLocalMachine.ini` `LastKnownUser` | `src/Valorant/ValorantImporter.cs:122-157`, `:275-280` |
| `Load(acc)` → `LoadCore` reads RiotUserSettings.ini (sens, crosshair, highlight, video) + BackupKeybinds.json | `ValorantImporter.cs:163-237` |
| INI reader (shared read, `EAres…SettingName::` prefix stripped) | `ValorantImporter.cs:283-308` |
| UE string unquoting of the JSON value | `ValorantImporter.cs:310-315` |
| **`ImportCrosshair`**: `SavedCrosshairProfileData` JSON → active profile; JSON missing → legacy flat `Crosshair*` keys (+ note "profiles are online"); JSON unreadable → VALORANT default (+ note) | `ValorantImporter.cs:239-262` |
| Result fields: `ValorantProfile.Crosshair` (active profile) and `CrosshairNote` | `ValorantImporter.cs:79-83` |
| Re-import (startup and the RE-IMPORT button, account switch) replaces `Main.I.Valorant` wholesale | `src/Main.cs:65`, `:203-213`; `src/UI/SettingsScreen.cs:536-540`, `:168` |

### 1.2 Parsers / model

| What | Where |
|---|---|
| `CrosshairLines`, `CrosshairStyle` (one tab), `CrosshairSettings` (whole profile: Primary, ADS, sniper dot, General flags, `Name`, `Extras`) | `src/Valorant/Crosshair.cs:12-102` |
| **`CrosshairSettings.FromProfileJson(json)`**: parses `{currentProfile, profiles[]}` but returns ONLY `profiles[currentProfile]`; name = `profileName` or "Profile N" | `Crosshair.cs:104-152` |
| `FromLegacyKeys` (old flat keys; name from `CrosshairProfileName`) | `Crosshair.cs:156-190` |
| Crosshair code decode / encode (lossless round trip, `Extras` keep unknown keys) | `src/Valorant/CrosshairCode.cs:59-380` (`Decode` :139-203, `Encode` :281-325) |
| VALORANT factory crosshair (`"0"`) | `CrosshairCode.cs:96-126` |

### 1.3 Which crosshair is drawn ("effective")

| What | Where |
|---|---|
| `CrosshairCode.Finder`: the ValTrainer CUSTOM slot = `AppSettings.FinderCrosshairCode` (pasted whole code, or Crosshair Finder result applied to ONE tab on top of the live import via `ApplyTab`), cached by reference to `Main.I.Valorant.Crosshair` | `CrosshairCode.cs:386-418`, `ApplyTab` :448-488 |
| `DevOverride` (`--xhair-code`) | `CrosshairCode.cs:494-508` |
| **`CrosshairCode.Effective`** = `DevOverride ?? (UseFinderCrosshair && Finder ? Finder : Valorant.Crosshair)` | `CrosshairCode.cs:524-526` |
| `EffectiveCode` (COPY buttons) | `CrosshairCode.cs:510-522` |

Consumers of `Effective` (all pick up a switch automatically, every frame):
- In drills: `src/Game/GameSession.cs:476` (`crosshair.Profile = TrialCrosshair ?? CrosshairCode.Effective`, each `_Process`); initial `:225`.
- Previews: `src/UI/CrosshairView.cs:321` (`CrosshairPreview.Source`, refreshed in `_Process` :384).
- Menu: `src/UI/MenuScreen.cs:634` ("Crosshair profile" row), `:643-658` (preview + COPY CROSSHAIR CODE).
- Crosshair Finder start: `src/Modes/CrosshairFinderMode.cs:150` (snapshots `Effective`, then drives `G.TrialCrosshair`).
- Rewind: `src/UI/RewindDraw.cs:244`, `src/UI/RewindView3D.cs:299` (draw today's crosshair, not the one recorded).

Consumers of the IMPORTED active profile (`Main.I.Valorant.Crosshair`), which should stay "VALORANT's active profile":
- `CrosshairHint.ForImported` (visibility hint, Settings + Lock-In check): `src/Modes/CrosshairHint.cs:42-51`, `src/Warmup/LockInSetupCheck.cs:56`.
- Finder's one-tab overlay base: `CrosshairCode.cs:401`.
- Settings imported panel "Crosshair profile": `SettingsScreen.cs:533`.

### 1.4 Renderer
- `CrosshairView` (Control) draws a `CrosshairSettings` in mode Primary / Ads / Sniper / Hidden: `src/UI/CrosshairView.cs:9-67`; style/dot/scope helpers :69-310.
- `CrosshairPreview` (PRIMARY / ADS / SNIPER chips + caption): `CrosshairView.cs:313-431`.
- HUD layer order: crosshair under the blind overlay and HUD (`GameSession.cs:219-230`).

### 1.5 In-app code import + settings UI
- Settings → GENERAL → CROSSHAIR section: `SettingsScreen.cs:203-218`
  - Segmented `VALORANT | CUSTOM` (`St.UseFinderCrosshair`), note row with `CrosshairNote`, preview, hint row, "Code of the crosshair ValTrainer uses now" + COPY, "Last Crosshair Finder result" + COPY, PASTE row.
  - `PasteRow()` :251-283: clipboard → `CrosshairCode.Decode` → sets `FinderCrosshairCode`, `FinderCrosshairTab = null`, `UseFinderCrosshair = true`. (So today ValTrainer holds ONE custom code; pasting a second one overwrites the first.)
- Crosshair Finder "Use in ValTrainer" / "Keep my crosshair": `CrosshairFinderMode.cs:1000-1031`.
- `Selector(...)` row widget (◀ value ▶, click = +1): `SettingsScreen.cs:1003-1035`.

### 1.6 Persistence (settings.json)
- `src/Core/AppSettings.cs:29-32`: `UseFinderCrosshair`, `FinderCrosshairCode`, `FinderCrosshairTab`, `LastFinderCrosshairCode`.
- JSON with `IncludeFields = true` (nested field classes serialize, e.g. `UpdateAssets`): `AppSettings.cs:59-60`, `Load` :76-91, `Save` :97-102.
- `--dev` never writes settings unless `--data-dir … --write-data` (`src/Main.cs:38`, `:74`).
- Bug report field `crosshair_source`: `src/UI/BugReport.cs:188-189`.

### 1.7 Keybinds
- `GameAction` enum (VALORANT actions the trainer uses; new actions are appended at the end, binds are saved by name): `src/Valorant/Keybinds.cs:7-27`.
- `InputBinding` (key / mouse button / wheel + Shift/Ctrl/Alt), `Match`, `Serialize`/`Parse`, `FromEvent` (capture ignores modifiers): `Keybinds.cs:34-217`.
- `Keybinds` (2 slots per action), `Label` :342-351, `Defaults()` :354-382, wheel pulse handling `Observe`/`EndFrame` :440-448.
- VALORANT import of `WindowsClient\BackupKeybinds.json` (`actionMappings`/`axisMappings`, only changed binds): `src/Valorant/ValorantKeybinds.cs:15-72`; name → action `KnownNames` :106-131 and tolerant `ActionOf` :138-178 (ignores names with "Cycle", "Next", "Previous", "Toggle", … :147-149).
- ValTrainer custom binds (v1.17): `src/Valorant/CustomBinds.cs` (keys `"Action.primary"` → `"key:Q"`), stored in `AppSettings.CustomBinds` (`AppSettings.cs:33-35`), applied by `ValorantProfile.ApplyCustomBinds` (`ValorantImporter.cs:56-60`) via `Main.ApplyBinds` (`Main.cs:213`).
- Settings → KEYBINDS page: groups `SettingsKeybinds.cs:17-30`, rows `BindRow` :157-185, note `BindNote` :188-193 ("From VALORANT"), capture `_Input` :213-236, UI self-test asserts every action has two slots on screen :84.
- In-drill input: `GameSession._Input` `GameSession.cs:350-386` (Esc, buy-menu bind with the pause-menu LineEdit guard :381-386, then `OnBindEvent` :392-410).
- Dev tests: `--keybind-test` (`src/Game/KeybindTest.cs`, loops `Keybinds.All` :87, :259-275), `--keybind-ui-test` (`SettingsKeybinds.cs:68-154`), import checks in `src/Core/CultureCheck.cs:363-421`.

### 1.8 HUD / pause
- Banner (big, 1.4 s, used by modes): `GameSession.cs:973-978`, drawn `src/UI/Hud.cs:170`, `:422-446`.
- Pause menu (580×600 panel: RESUME, RESTART, sensitivity box, BACK TO MENU, key hint line): `Hud.cs:596-706`.

---

## 2. What VALORANT data is available locally

### 2.1 Crosshair profiles: ALL of them are in the local file (when the JSON key exists)

`RiotUserSettings.ini` → `EAresStringSettingName::SavedCrosshairProfileData` is a quoted JSON
`{ "currentProfile": <int>, "profiles": [ … ] }`. Each profile has
`profileName, primary, aDS, sniper, bUsePrimaryCrosshairForADS, bUseAdvancedOptions, bScaleToResolution,
bUseCustomCrosshairOnAllPrimary, bUsePrimaryCrosshairForFocusMode, focusMode`.
`FromProfileJson` already parses this but throws away every profile except `currentProfile` (`Crosshair.cs:112-115`).

Read-only check of the files on this PC (key names and counts only; no values copied):

| Accounts with RiotUserSettings.ini | with `SavedCrosshairProfileData` | profiles per account | unnamed profiles | duplicate names |
|---|---|---|---|---|
| 10 | 9 | 3, 3, 4, 4, 5, 5, 6, 7, 9 | in 4 accounts | in 3 accounts |

(The 10th account has only the legacy flat `Crosshair*` keys.) Other facts:
- The JSON is 10–30 KB. `currentProfile` was the last index in 7 of 9 accounts but not in every one, so the index must be read, not assumed.
- 5 of 9 accounts have at least one profile with `bUseAdvancedOptions` (split ADS / sniper): the existing model handles this.
- Profile names can be empty (→ the existing "Profile N" fallback) and can repeat, so a name can't identify a profile on its own.
- All 10 files also still carry old flat keys (`CrosshairProfileName`, `CrosshairColor`, `Crosshair*Lines*`, …). Those describe ONE crosshair, are often stale, and are used only when the JSON is missing (as today).

### 2.2 What's honestly NOT available
- **Accounts whose profiles VALORANT keeps only online**: 1 of 10 here. The local file has no profile list, only the stale flat keys. The existing note (`ValorantImporter.cs:254-261`) already says so. For these players the switcher can offer only that one crosshair plus their pasted codes, and the UI has to say this.
- **Freshness**: VALORANT's crosshair profiles sync to Riot's cloud. The local JSON is what VALORANT last wrote on THIS PC, so profiles edited on another PC or added since the last session here only appear after VALORANT has run here again. ValTrainer reads the file only at startup and on RE-IMPORT. Show the account's "saved <date>" (`ValorantProfile.Source`, `ValorantImporter.cs:207`) next to the list.
- **Cloud settings**: reading them would mean calling Riot's player-preferences API with the user's session tokens. ValTrainer won't do that (it isn't local file reading, and it would need the Riot client's auth). Out of scope.
- **"Active in VALORANT" right now**: `currentProfile` as of the last write. Switching in ValTrainer never changes VALORANT. The UI should say "ValTrainer only".

### 2.3 Keybinds: VALORANT has no crosshair-switch bind to import
- VALORANT's crosshair profile dropdown (Settings → Crosshair) has no keybind for cycling profiles. The BackupKeybinds.json files here have only 4 distinct action names across all accounts, and none is crosshair-related (no "crosshair", "profile", "cycle", "next", "prev"). The file's root keys are `actionMappings, axisMappings, characterProfileData, settingsProfiles, settingsVersion`.
- So next / previous crosshair have to be **ValTrainer-only binds**. `ActionOf` already ignores "Cycle" / "Next" / "Previous" names, so an imported VALORANT action can never land on them by accident.

---

## 3. Design

### 3.1 Concepts
- **Crosshair list**: the ordered set the switcher cycles through. It is built fresh from the import and settings, and it always has at least 1 entry:
  1. **VALORANT profiles**: every profile in `SavedCrosshairProfileData`, in VALORANT's order. Tag `VALORANT n/N`, plus `ACTIVE IN VALORANT` on `currentProfile`. Without the JSON there is one entry: the legacy or default crosshair as today, with its `CrosshairNote`. With no VALORANT at all, the one entry is "VALORANT default".
  2. **Crosshair Finder / custom code**: the existing `FinderCrosshairCode` slot, when `CrosshairCode.Finder != null`. Name as today ("Crosshair Finder", "<profile> + Finder PRIMARY", "Custom code"), tag `VALTRAINER`.
  3. **Saved codes**: new user-saved list (`SavedCrosshairs`), in the user's order, with tag `SAVED`. Added by PASTE or by SAVE CURRENT.
- **Choice**: which entry is drawn. The default (null) is "follow VALORANT's active profile", which is exactly today's behaviour.
- Duplicate / empty names are shown disambiguated in the list only: "Profile 3", "Main (2)".

### 3.2 Settings model (AppSettings, additive, back-compatible)
```csharp
// AppSettings.cs (new fields; existing ones keep their meaning)
/// <summary>The crosshair picked in ValTrainer when the CUSTOM slot isn't in use (UseFinderCrosshair false).
/// Null = VALORANT's active profile (default; follows VALORANT after every re-import).</summary>
public CrosshairChoice? CrosshairChoice;
/// <summary>Crosshair codes saved in ValTrainer (Settings → Crosshair: PASTE / SAVE CURRENT), in cycle order. Max 20.</summary>
public List<SavedCrosshair>? SavedCrosshairs;

public sealed class CrosshairChoice
{
    public string Kind = "valorant";   // "valorant" | "saved"
    public string? Account;            // valorant: the account id the index belongs to (another account → ignored)
    public int Index;                  // valorant: index in profiles[]
    public string? Name;               // valorant: profileName at pick time (validates the index)
    public string? Code;               // saved: the saved entry's code (survives reorder / rename)
}
public sealed class SavedCrosshair { public string Name = ""; public string Code = ""; }
```
- `UseFinderCrosshair` keeps its meaning: true means the CUSTOM slot wins. That keeps Crosshair Finder's "Use in ValTrainer" / "Keep my crosshair" (`CrosshairFinderMode.cs:1012-1029`) and the BugReport field unchanged. Picking any other entry sets it to false, and picking the custom entry sets it to true.
- Resolving a VALORANT choice: if the account matches, use `profiles[Index]` when its name equals `Name`. Otherwise use the first profile with that `Name`. Otherwise fall back to VALORANT's active profile. The stored choice stays, so switching back to the account restores it. Picking the entry that is VALORANT's `currentProfile` stores `CrosshairChoice = null` (follow VALORANT), so a later change in VALORANT shows up.
- Resolving a saved choice: the entry with that `Code`. If it was deleted, fall back to VALORANT's active profile.
- Old settings.json files have neither field, so they behave exactly as today. An old build reading a new file ignores the unknown fields.

### 3.3 Import change (read-only, same files)
- `Crosshair.cs`: add `public static List<CrosshairSettings>? AllFromProfileJson(string json, out int current)`. It parses every profile with the existing per-profile code (`Crosshair.cs:118-146`, moved into a helper), sets each Name to `profileName` or "Profile i+1", and records the original index (`ProfileIndex`, a new int field on `CrosshairSettings`, -1 for non-VALORANT). An entry that isn't an object is skipped but its index stays reserved. `FromProfileJson` becomes `AllFromProfileJson(...)` → the `current` entry, so behaviour is unchanged and the existing `CultureCheck` tests keep passing.
- `ValorantProfile` (`ValorantImporter.cs:79-83`): add `public List<CrosshairSettings> CrosshairProfiles = new();` and `public int CrosshairActive;`. `ImportCrosshair` (:242-262) fills them, and `Crosshair` stays `profiles[current]`. In the legacy and default branches the list is `[Crosshair]`.
- No new file is read and nothing is written.

### 3.4 New `src/Valorant/CrosshairList.cs`
```csharp
public enum CrosshairSource { Valorant, Custom, Saved }
public sealed record CrosshairEntry(CrosshairSource Source, int Index, string Name, string Tag, CrosshairSettings Profile, bool ActiveInValorant);

public static class CrosshairList
{
    public static IReadOnlyList<CrosshairEntry> All { get; }      // cached; rebuilt when Main.I.Valorant (reference),
                                                                   // the Finder profile (reference) or Revision changes
    public static int Current { get; }                             // index of the entry Effective draws
    public static CrosshairSettings? Picked { get; }               // the non-custom choice resolved (null = VALORANT's active)
    public static CrosshairEntry Select(int i);                    // sets UseFinderCrosshair / CrosshairChoice, Save()
    public static CrosshairEntry Step(int d);                      // Select((Current + d) mod Count)
    public static bool Add(string code, string? name, out string msg);   // dedup by canonical Encode, max 20
    public static void Rename(int savedIdx, string name); public static void Remove(int savedIdx); public static void Move(int savedIdx, int d);
    public static int Revision { get; }                            // bumped by every mutation (cache key)
}
```
- `CrosshairCode.Effective` (:524-526) becomes `DevOverride ?? (UseFinderCrosshair && Finder is {} f ? f : CrosshairList.Picked ?? Main.I.Valorant.Crosshair)`. `EffectiveCode` (:510-522) gets one more branch: a saved pick returns its stored code trimmed, as is (like a pasted code), and a VALORANT pick returns `Encode(profile)`.
- Performance: `Effective` runs every frame (`GameSession.cs:476`, every preview). `Picked` must be an O(1) cached lookup, the same pattern as `Finder` (`CrosshairCode.cs:396-417`). Saved codes are decoded once per rebuild.
- `CrosshairHint.ForImported`, `Finder`'s `ApplyTab` base and the imported info row keep using `Valorant.Crosshair` (VALORANT's active profile). That keeps their wording ("Your VALORANT crosshair") true.

### 3.5 Hotkeys (ValTrainer-only binds)
- Append `GameAction.CrosshairPrev, GameAction.CrosshairNext` after `BuyMenu` (`Keybinds.cs:26`). Appending keeps the stored names stable.
  - `Label`: "Previous crosshair" / "Next crosshair".
  - `Defaults()`: primary `Key.Comma` / `Key.Period`, secondary unbound. Neither is a VALORANT default ValTrainer uses, and neither is used by any drill. **Open choice for the owner:** `[` `]` or PgUp / PgDn are alternatives; layout caveat: on AZERTY `.` is Shift+`;`, so those players rebind.
  - VALORANT's file never maps to them (§2.3), so "imported" = these defaults, and `CustomBinds` works unchanged (custom = different from default).
- Settings → KEYBINDS: add a `("CROSSHAIR", new[] { CrosshairPrev, CrosshairNext })` group to `BindGroupsLeft` (`SettingsKeybinds.cs:17-23`). The left column has 11 rows today and the right has 12, so check at 1280×720 (the columns scroll). `BindNote` / `SlotTip` (:188-203): for these two actions say "ValTrainer only (VALORANT has no crosshair bind)" instead of "From VALORANT". The UI self-test count (:84) follows `Keybinds.All` automatically, as long as both rows exist.
- The wheel works through the existing capture (`InputBinding.FromEvent` :157-169). A player can bind Next to Mouse 5 or Wheel. A wheel shared with Jump triggers both, like every shared bind, and the KEYBINDS page already warns about shared binds.
- `GameSession._Input` (`GameSession.cs:350-386`): after the Esc block and before the buy-menu check, add:
  ```csharp
  if (State is St.Countdown or St.Running or St.Paused && !(State == St.Paused && GetViewport().GuiGetFocusOwner() is LineEdit)
      && (binds.Pressed(e, GameAction.CrosshairNext) ? 1 : binds.Pressed(e, GameAction.CrosshairPrev) ? -1 : 0) is var d && d != 0)
      SwitchCrosshair(d);   // no return: a shared bind still does its other action (consistent with all binds)
  ```
  The LineEdit guard is required: the pause menu's sensitivity box takes typed decimals, and `.` is the default Next key (see the `--pause-type` test at `Hud.cs:651-665`).
- `SwitchCrosshair(d)`:
  - Refuse with a toast "Crosshair Finder picks the crosshair here" in `Mode.Key == "xhairfinder"`, because it drives `TrialCrosshair`, records its own trials (`RunAnalysis.cs:41`), and snapshots `Effective` at start.
  - Refuse while `CrosshairCode.DevOverride != null`.
  - With 1 entry, show "Only one crosshair · add codes in Settings → Crosshair".
  - Otherwise call `CrosshairList.Step(d)`, play `Sfx "kill1"` quietly, and show the toast.
  - Saving: `Select` saves through `AppSettings.Save()`. That's a no-op in `--dev`. A file write per keypress is fine (as with the pause sens arrows, `Hud.cs:609-616`).
- Analysis impact: none for scores. The crosshair isn't a skill input, and Rewind already draws today's crosshair, not the recorded one (`RewindDraw.cs:244`). Optional: a telemetry event `xhair_switch` (entry name) for the Rewind timeline later. Not required.

### 3.6 Toast (on-screen name)
- New fields on `GameSession` next to `BannerText` (`GameSession.cs:74-76`): `XhairToastText`, `XhairToastSub`, `XhairToastTime` (1.3 s).
- `Hud.DrawXhairToast` is called after `DrawBanner` (`Hud.cs:170`). It draws a small plate centred about 90 px (×k) under the crosshair: the name (Display 22) and "VALORANT 2/4 · ACTIVE IN VALORANT" / "SAVED 3/7" (HudWide 11). It fades over the last 0.3 s and stays drawn in Paused too.
- It's deliberately not the big `Banner`: modes use that one for round events, and it would cover the crosshair being judged.

### 3.7 Where the switcher appears
1. **Settings → GENERAL → CROSSHAIR** (`SettingsScreen.cs:203-218`):
   - Replace the `VALORANT | CUSTOM` Segmented with `Selector("Crosshair", () => entry.Name, d => CrosshairList.Step(d), note: () => entry.Tag + (custom ? " · ValTrainer only" : ""))`. The selector has ◀ ▶ and click = next.
   - The note row with `CrosshairNote` shows only when the current entry is a VALORANT one.
   - New info line: "N profiles from VALORANT (saved on this PC <date>) · M saved codes · keys , ." with the live bind text from `Binds.Short(CrosshairPrev/Next)`.
   - Preview, hint row and code row stay as they are.
   - **PASTE** (`PasteRow`, :251-283) now adds the code to `SavedCrosshairs` and selects it, instead of overwriting the single custom slot. The name comes from the community `NAME;"…"` extension when present (kept in `Extras.UnknownSections`), else "Code n". A duplicate (same canonical `Encode`) selects the existing entry and flashes "ALREADY SAVED ✓".
   - New **SAVE CURRENT** button: snapshots the drawn crosshair into the saved list, named after the entry, e.g. "Main (VALORANT)". This keeps a VALORANT profile across account switches and cloud-only accounts.
   - New **SAVED CROSSHAIRS** list: one row per saved code with the name (click to rename in a LineEdit styled with `SettingsScreen.StyleEdit`), USE, COPY, ▲/▼ and ✕. ✕ asks for a second click to confirm. "Max 20" is shown when full.
   - Legacy: an existing pasted code in `FinderCrosshairCode` (tab null, not the Finder result) is NOT migrated. It remains the "Custom code" entry in the list, so nothing is lost and old builds still read it.
2. **Main menu** CROSSHAIR panel (`MenuScreen.cs:634-658`): small ◀ ▶ arrows either side of the profile name above the preview, and the mouse wheel over the preview cycles. This is the "scroll through" Dixi asked for. The "Crosshair profile" row shows the entry name + `n/N`.
3. **Pause menu** (`Hud.cs:596-706`): a CROSSHAIR row under the sensitivity block with ◀ name ▶ (OverlayButton arrows, the same pattern as the sens arrows :617-618). Grow the panel from 600 to about 680 (×k; still fits 720p: 680×0.667 ≈ 453 px) and add `, . — CROSSHAIR` to the key hint line (:674).
4. **In-drill** hotkeys + toast (§3.5, §3.6).

### 3.8 Defaults and back-compat
- A fresh install, or an existing settings.json, draws exactly what it does today: the custom slot if `UseFinderCrosshair`, else VALORANT's active profile.
- The only behaviour change without user action is that `,` and `.` now switch crosshairs in drills. Mention it in the changelog + What's New.
- Re-import / account switch rebuilds the list (cache keyed on the `Main.I.Valorant` reference). A VALORANT choice from another account is ignored, not deleted.
- Bug report `crosshair_source` (`BugReport.cs:188-189`) becomes "VALORANT active", "VALORANT profile 2/4", "saved code 3/7" or "custom code (primary tab)", plus `crosshair_list_count`.

---

## 4. Exact files

Create:
- `src/Valorant/CrosshairList.cs`: list, choice resolution, cycle, saved-code mutations, cache (§3.4).

Change:
- `src/Valorant/Crosshair.cs`: `AllFromProfileJson`, `ProfileIndex` field; `FromProfileJson` wraps it.
- `src/Valorant/ValorantImporter.cs`: `ValorantProfile.CrosshairProfiles`, `CrosshairActive`; fill in `ImportCrosshair`.
- `src/Valorant/CrosshairCode.cs`: `Effective` / `EffectiveCode` via `CrosshairList.Picked`.
- `src/Core/AppSettings.cs`: `CrosshairChoice`, `SavedCrosshairs`, the two field classes.
- `src/Valorant/Keybinds.cs`: `CrosshairPrev`, `CrosshairNext` (enum end), `Label`, `Defaults`.
- `src/UI/SettingsKeybinds.cs`: CROSSHAIR group, ValTrainer-only note / tooltip.
- `src/UI/SettingsScreen.cs`: crosshair section (selector, PASTE → saved list, SAVE CURRENT, saved list rows, info line).
- `src/UI/MenuScreen.cs`: arrows + wheel on the crosshair panel; name `n/N`.
- `src/Game/GameSession.cs`: input hook, `SwitchCrosshair`, toast fields.
- `src/UI/Hud.cs`: `DrawXhairToast`; pause-menu crosshair row, panel height, key hint.
- `src/UI/BugReport.cs`: crosshair source / count.
- `src/Core/CultureCheck.cs`: self-tests (§5.1).
- `CHANGELOG.md` (Unreleased → Added). Commit trailer per the Discord convention (`Adds-Discord: <post>`).

Leave unchanged: `ValorantKeybinds.cs` (no VALORANT action to map), `CustomBinds.cs`, `CrosshairFinderMode.cs`, `CrosshairHint.cs`, `CrosshairView.cs`.

---

## 5. Test plan

### 5.1 Headless self-tests (`--dev --culture-test`, run by `tools\selftest.ps1`)
Add to `CultureCheck` next to the existing crosshair and import fixtures (`CultureCheck.cs:60-130`, `:137`):
- `AllFromProfileJson`: 3 profiles (one unnamed, two named "Main"), `currentProfile: 1` → 3 entries, names "Main", "Profile 2"…, active = 1. `FromProfileJson` still returns entry 1. Broken JSON → null; a non-object entry is skipped with its index kept.
- Importer fixture: `CrosshairProfiles.Count == 3`, `Crosshair` is `profiles[1]`. Legacy-keys account → list = `[legacy]` + note. No VALORANT → `[default]`.
- `CrosshairList`:
  - order is VALORANT…, custom, saved; `Step` wraps both ways.
  - selecting the active VALORANT entry stores null.
  - a choice with a renamed or reordered profile resolves by name; a choice for another account falls back.
  - a deleted saved choice falls back.
  - `Add` dedups by canonical code and caps at 20.
  - `EffectiveCode` of a saved pick == the stored code.
- Settings round trip: serialize/deserialize `AppSettings` with `CrosshairChoice` + `SavedCrosshairs`. A settings.json without them deserializes to null (today's behaviour).
- Keybinds: defaults `CrosshairPrev` "," / `CrosshairNext` "."; no VALORANT action name maps to them (`ActionOf("NextCrosshair")`, `"CycleCrosshairProfile"` → null); a custom bind `"CrosshairNext.primary": "mouse:Xbutton2"` applies.

### 5.2 Fixtures (never the real folders)
- **Data dir**: a temp COPY, e.g. `Copy-Item "$env:APPDATA\ValTrainer\settings.json" "$env:TEMP\vt-xh\data\"`, or start empty. Add `--write-data` only for the persistence test.
- **VALORANT dir**:
  - `"$env:TEMP\vt-xh\val-empty"`: an empty folder → no VALORANT.
  - `"$env:TEMP\vt-xh\val-multi"`: `WindowsClient\RiotLocalMachine.ini` (`LastKnownUser=0123abcd-…`) + `0123abcd-5678-9abc-def0-123456789abc-eu\Windows\RiotUserSettings.ini` with a synthetic `EAresStringSettingName::SavedCrosshairProfileData="{\"currentProfile\":1,\"profiles\":[…4 profiles: cyan dot / unnamed / two named Main, one advanced with own ADS…]}"`. Same format as the fixture at `CultureCheck.cs:71-83`.
  - `"$env:TEMP\vt-xh\val-legacy"`: flat keys only (cloud-only account).

### 5.3 Screenshots (`tools\gshot.ps1`: always `--dev`, 1600×900, own window only, one Godot at a time)
```powershell
$x = "--data-dir $env:TEMP\vt-xh\data --valorant-dir $env:TEMP\vt-xh\val-multi"
# Settings crosshair section: selector, info line, saved list (scroll to the crosshair block)
& tools\gshot.ps1 -At 4 -Name xh-settings -Extra "$x --screen settings --xhair-scroll"
# 720p layout of the same + the KEYBINDS page with the new CROSSHAIR group
& tools\gshot.ps1 -At 4 -Name xh-settings720 -Extra "$x --screen settings --xhair-scroll --window 1280x720"
& tools\gshot.ps1 -At 4 -Name xh-keybinds720 -Extra "$x --screen keybinds --window 1280x720"
# Menu crosshair panel with arrows / n/N
& tools\gshot.ps1 -At 4 -Name xh-menu -Extra $x
# Pause menu with the crosshair row (720p too)
& tools\gshot.ps1 -Mode gridshot -At 6 -Name xh-pause -Extra "$x --pause-at 3 --window 1280x720"
# No VALORANT: one "VALORANT default" entry + "add codes" info; legacy: note shown
& tools\gshot.ps1 -At 4 -Name xh-empty  -Extra "--data-dir $env:TEMP\vt-xh\data --valorant-dir $env:TEMP\vt-xh\val-empty --screen settings --xhair-scroll"
& tools\gshot.ps1 -At 4 -Name xh-legacy -Extra "--data-dir $env:TEMP\vt-xh\data --valorant-dir $env:TEMP\vt-xh\val-legacy --screen settings --xhair-scroll"
```
New dev-only flags to add with the feature (documented in their summaries like the existing ones):
- `--xhair-pick <n>`: select list entry n at startup (screenshots of a given entry in a drill).
- `--xhair-switch-test` (with `--mode <drill>`): once Running, feeds the player's `CrosshairNext` / `CrosshairPrev` binds via `Input.ParseInputEvent` (as `KeybindTest.cs` does). It checks:
  - `crosshair.Profile` changes to the next entry and wraps;
  - the toast shows;
  - a focused pause LineEdit swallows `.`;
  - `xhairfinder` refuses;
  - settings stay unwritten without `--write-data`.

  It prints `[xhair-switch-test] PASS/FAIL` and quits with the failure count.
```powershell
& tools\gshot.ps1 -Mode gridshot -At 3,4.2 -Name xh-toast -Extra "$x --xhair-switch-test"   # 2nd shot catches the toast
& tools\gshot.ps1 -Mode xhairfinder -At 5 -Name xh-finder -Extra "$x --xhair-switch-test"   # refusal toast
```
Persistence: run once with `--write-data`, switch (`--xhair-pick 3`), quit. Check that `$env:TEMP\vt-xh\data\settings.json` has `CrosshairChoice` and that a second run draws entry 3. Also run `tools\selftest.ps1`: it must stay green, including `--keybind-ui-test`, whose "two slots per action" count now includes the 2 new actions, and the selftest's "nothing written to the throw-away data folder" check.

### 5.4 Manual checks
- Wheel bound to Next while Jump is on the wheel: both fire, and the KEYBINDS page lists the shared bind.
- Re-import after changing the profile in VALORANT: a "follow" choice moves with it, and a specific pick stays.
- Account switch in Settings: the list rebuilds and a foreign pick falls back.
- Long and duplicate names truncate cleanly in the selector, toast and pause row at 720p.
