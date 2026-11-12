# Translating ValTrainer's UI: how to wrap strings (v1.19)

The design and the reasons are in `i18n-design-1.19.md`. This page is the working guide for anyone converting a
folder: the exact API, the patterns, and what must stay English. The framework, the language picker, `src/UI` and
`src/Core` are done. `src/Modes`, `src/Modes/Agents`, `src/Warmup`, `src/Agents`, `src/Game`, `src/Valorant`
(the few UI notes) and later `src/Analysis` (coach, phase 2) still need it.

## 1. The API (`src/Core/L.cs`, namespace `ValTrainer.Core`)

| Call | Use it for | Key in the catalogue |
|---|---|---|
| `L.T("Plain text")` | a plain English string, translated now | `"Plain text"` |
| `L.F($"{n:0} of 30 flicks")` | an interpolated string | the composite format `"{0:0} of 30 flicks"` |
| `L.P(n, $"{n} drill", $"{n} drills")` | a count with singular/plural | `"{0} drill\|{0} drills"` → `{ "one": …, "other": … }` |
| `L.N("Windowed")` | marks English data that is translated **later**, where it is shown (returns it unchanged) | `"Windowed"` |
| `L.C("toggle", "ON")` | a short word that needs a context to translate | `"toggle\|ON"` |
| `L.Upper(L.T(x))` | upper-casing translated text (instead of `ToUpperInvariant`) | — |
| `L.Raw(s)` / `Gfx.TextRaw(...)` | text that must never be looked up (names, codes, paths, callouts, key names, release notes) | — |
| `mode.DisplayName` / `DisplayDescription` / `DisplayCategory` | a drill's name, description, category for display | `Name` / `Description` / `Category` |

Rules built into `L`:
- English is the key and the fallback. A missing or empty translation shows English, never a blank.
- `L.F`/`L.P` format numbers with the **invariant culture** (`.` decimals), like the rest of the app. Never use
  `string.Format` with a culture, and never `ToString()` a number without `CultureInfo.InvariantCulture` /
  `FormattableString.Invariant` (the existing rule; `CultureCheck` tests it).
- A translation with broken placeholders falls back to English and logs once; it never crashes a frame.
- Thread-safe (the coach builds text on worker threads). Cheap: one dictionary read; English does no lookup at all.
- `L.P`: en/de/es use the singular for 1 only; fr and pt-BR also for 0. **Both forms must have the same holes**: write
  `$"{n} drill"`, never `$"1 drill"` or `"a drill"` (French needs the number in the singular for 0).
- `L.F` only has a `FormattableString` overload on purpose: `L.F($"…")` must stay an interpolated string literal at
  the call site (a `string` variable can't be translated, it has no format).

## 2. What translates on its own ("sinks")

These draw helpers call `L.T` on the text they get, so **a plain literal passed straight to them needs no change**:

- `Gfx.Text`, `Gfx.TextC`, `Gfx.TextR`, `Gfx.TextFit`, `Gfx.TextW` (the width is measured on the translated text, so
  measure and draw stay in sync; `Gfx.RawW` measures without translating)
- `VButton.Label` / `Sub`, `Btn("LABEL", …)`, `SectionLabel.Text` / `Note`, `VPanel.Title` / `Caption`,
  `InfoRow.Label` / `Value()` / `Tag()`, `ValueField.Value()`
- `ScreenBase.Lbl("text", …)` and `Heading(title, sub)` (Godot `Label` built through `Lbl`)
- SettingsScreen rows: `Selector(label, value, …, note:)`, `Segmented(label, options, …, note:)`, `SettingRow.Note`
  (translated before it is split into two lines), `CodeRow(label)`, `FolderRow(label)`
- `G.Banner(text, colour)` / `GameSession.Banner` and `Kit.Refuse(why)` (the HUD translates the banner / refusal when it
  draws it)

A sink only translates **the exact string it receives**. So:

```csharp
Gfx.Text(ci, f, "SCORE", …);                          // ok: looked up as "SCORE"
Gfx.Text(ci, f, done ? "DONE" : "NEXT", …);           // ok: both are plain literals
Gfx.Text(ci, f, $"{hits}/{n} on target", …);          // WRONG: "12/30 on target" is no key
Gfx.Text(ci, f, L.F($"{hits}/{n} on target"), …);    // right: key "{0}/{1} on target"
Gfx.Text(ci, f, "Score: " + s, …);                    // WRONG: concatenation
Gfx.Text(ci, f, L.F($"Score: {s}"), …);               // right
```

Not sinks (wrap yourself):
- Godot `Label.Text` set directly (`new Label { Text = … }`, `lbl.Text = …`): `L.T(...)` / `L.F(...)`.
- `TooltipText = …`: always `L.T("…")` / `L.F($"…")` (Godot's own auto-translation is not used).
- `LineEdit.PlaceholderText`, `DrawMultilineString`, `DrawString` / `DrawStringOutline` called directly: wrap the
  text, and measure the same (translated) string you draw.
- Strings stored and drawn later through your own code (fields, records, lists, return values): see section 3.

## 3. Patterns

**Interpolation.** `L.F($"…")` around the whole sentence. English inside a hole needs its own `L.T`:

```csharp
L.F($"Bots react in ~{ms:0} ms")
L.F($"Valorant ({L.T(WindowModes[i])})")                  // table value inside a sentence
L.F($"{L.T(free ? "free play" : "scored")} · {name}")      // ternary of literals inside a hole
```

Keep format specifiers inside the hole (`{v:0.00}`, `{n,-5}`); they become part of the key (`{0:0.00}`). Don't
split a sentence into several `L.T` pieces joined with `+`: word order differs between languages. List-style joins
(`a + " · " + b` of independent labels) are fine: translate each part.

**Plurals.** Replace every `n == 1 ? "" : "s"` / `n == 1 ? "drill" : "drills"`:

```csharp
L.P(n, $"{n} drill", $"{n} drills")
L.P(runs, $"Rewind recordings · {runs} run · {mb} MB", $"Rewind recordings · {runs} runs · {mb} MB")
```

**One word, two meanings.** A short word used in unrelated places gets a context so each place has its own
translation: `L.C("range", "CLOSE")` (Sheriff range) vs the plain "CLOSE" button, `L.C("spot", "Close")`,
`L.C("breath", "OUT")` / `L.C("ability", "OUT")`, `L.C("peek", "Short")`, `L.C("sens", "NEW")`,
`L.C("confidence", value)`. `L.C` falls back to the plain key, then English. A word without letters has no key at all:
`L.C("crosshair result: colour, shape", "{0} {1}")` gives translators a reorderable template.

**ALL CAPS.** A caps literal is its own key: `"SETTINGS"` and `"Settings"` are two entries (that's fine and
intended; VALORANT does the same). For runtime caps, translate first, then upper-case with the UI language:
`L.Upper(L.T(name))` or `L.Upper(mode.DisplayName)`, never `L.T(name.ToUpperInvariant())` (that key wouldn't exist).

**Data tables and values drawn later.** Tables stay English (they are also logic keys); mark their literals with
`L.N` so the extractor finds them, and translate where shown. Never call `L.T` in a `static readonly` initializer or
any other place that runs once (it would freeze the startup language):

```csharp
static readonly string[] WindowModes = { L.N("Fullscreen"), L.N("Windowed Fullscreen"), L.N("Windowed") };
Gfx.Text(ci, f, WindowModes[i], …);                       // the sink translates
string Label(GameAction a) => a switch { GameAction.Walk => L.N("Walk"), … };  // shown via a sink later
```

A value computed at draw time that goes to a sink can use `L.N` (the sink translates it) or `L.T` (translated twice
is harmless: the second lookup misses). If the value goes anywhere that is *not* a sink (a Godot Label, a tooltip, a
string built with other text), use `L.T` at the point where it's used.

**Drill names, descriptions, categories.** `TrainingMode.Name/Description/Category` stay English (keys for logic,
stats, telemetry, Discord). Show `mode.DisplayName`, `mode.DisplayDescription`, `mode.DisplayCategory`. Category
comparisons (`== "Agents"`, `CategoryColor(cat)`, `GroupBy`) keep the English value.

**Overrides in modes** (`Prompt`, `Subtitle`, `HudLines()`, `ResultLines()`): return plain English literals where
the HUD draws them through a sink, and `L.F`/`L.P` for interpolated lines. HUD lines are translated by the HUD
(`L.T(rawLine)`) *before* `LMB` / `RMB` are swapped for the player's binds: keep the tokens `LMB` / `RMB` unchanged in
translations. Lines starting with `TrainingMode.LastStopLine` ("Last stop") must stay plain English (the HUD hides
them on movement drills by that prefix).

**Logic never depends on displayed text.** Don't compare (`==`, `StartsWith`, `Contains`, `switch`) a string that
may be translated. Compare the English key before translating, or use a flag/enum (example: `SkillRating.IsProgress`
replaced `Headline.StartsWith("Not enough data")`). Tests that compare English output (`XhairSwitchTest`,
self-tests) must keep working: translate at the draw site, not where the value is produced.

**Width.** Translations run 15–35% longer (German most). Already handled: `TextFit` shrinks the font to 85% before
cutting with "…" (non-English only), and `VButton` grows to fit its label (non-English only; English keeps the
designed sizes). For text you measure yourself, measure the translated string. For a fixed-width tag sized from a
literal (`TextW(f, "RESULTS", …)`), the sink already measures the translation: keep measure and draw on the same
string. Check with the pseudo-language (section 6).

## 4. Never translate

- Brand names: VALORANT, ValTrainer, the "VAL"/"TRAINER" logo (use `Gfx.TextRaw` / `L.Raw`).
- Agent names and gun names (VALORANT keeps them in every language). Ability names: VALORANT localizes them; leave
  them English for now (a later script can fetch the official names).
- Map callouts (`src/Maps`, TacticalMap), key names (Keybinds, `InputBinding.Text`), crosshair codes and crosshair /
  profile names, player names, file paths, URLs.
- The What's New panel (CHANGELOG.md) and the bug / suggestion **payload** (the maintainer reads it). The panel's
  own labels are translated.
- Log lines (`Log.*`, `GD.Print`), dev and self-test output, CLI output (`CoachCli`), settings/telemetry keys, Godot
  node / action / sound names, command-line flags.
- Text that a test compares in English (keep it English where produced; translate at the draw site).

When such a string reaches a sink, mark it: `Gfx.TextRaw(...)`, `Label = L.Raw(name)`, `Value = () => L.Raw(code)`.

## 5. Tone and glossary (for translators)

Informal: "tu" (fr), "tú" (es), "você" (pt-BR), "du" (de), like VALORANT talks to players. Keep gaming jargon players
use in English: flick, spray, peek, counter-strafe, crosshair placement, eDPI, cm/360, headshot, one-tap, ADS, FPS.
Rank names follow VALORANT's localization (fr: Fer, Bronze, Argent, Or, Platine, Diamant, Ascendant, Immortel,
Radiant). Drill names inside sentences must match the drill card's translation.

## 6. Tools and checks

The tools live outside the Godot project (the csproj compiles every `.cs` under it): `..\i18n-tools\` next to the
project folder.

```powershell
py ..\i18n-tools\extract.py                 # scan src\ → translations\en.json + _sources.json, ..\i18n-tools\i18n-todo.txt
py ..\i18n-tools\extract.py --todo-only UI  # just the "unwrapped literal" report for one folder (src\UI)
py ..\i18n-tools\check.py fr                # translations\fr.json vs en.json: missing, stale, broken {0} placeholders, plural shape
py ..\i18n-tools\check.py --all             # every catalogue; exit code = number of errors
py ..\i18n-tools\check.py fr --sync         # add missing keys with "" (= English), drop stale ones, sort
```

- `extract.py` collects: every literal inside `L.T/L.N/L.C/L.F/L.P` (interpolations rebuilt into the same composite
  format the compiler makes: holes numbered in order, `{i,align:fmt}`, literal braces doubled), and plain literals
  (or ternaries of literals) passed straight to a sink (section 2). `i18n-todo.txt` lists, per folder, literals that
  look user-facing but reach no wrapper or sink, interpolated strings / concatenations passed to a sink unwrapped,
  and `==` / `StartsWith` / `Contains` on English literals. It's a heuristic worklist, not a gate.
- In game (dev runs): `--lang xx` = pseudo-language. Every string in `en.json` shows accented and ~35% longer in
  `[brackets]`; **anything still plain English is not extracted/wrapped**, and anything with "…" or overlapping is a
  width problem. `--lang fr` forces a language, `--riot-locale fr_FR` fakes VALORANT's language (Auto), and
  `--i18n-missing <file>` writes every drawn/looked-up string without a translation on exit.
- Screenshots: `tools\gshot.ps1 -ProjectPath <copy> -Client -Name x -Extra "--data-dir <tmp> --valorant-dir <empty> --lang xx"`.

Workflow per folder: wrap → `extract.py` → screenshots with `--lang xx` → fix what's plain English or cut →
`extract.py` again → build with no new warnings → `tools\selftest.ps1`.

## 7. Catalogue format

`translations/<code>.json`, flat, sorted keys, UTF-8:

```json
{
  "_meta": { "code": "fr", "name": "Français", "english": "French", "status": "machine", "basedOn": "1.19.0", "translators": [] },
  "SETTINGS": "PARAMÈTRES",
  "Bots react in ~{0:0} ms": "Les bots réagissent en ~{0:0} ms",
  "{0} drill|{0} drills": { "one": "{0} exercice", "other": "{0} exercices" }
}
```

`status`: `machine` | `reviewed` | `community` (the picker says "Machine translation · help improve it" for
`machine`). An empty value means "use English". Players can drop a file with the same name into
`%APPDATA%\ValTrainer\translations\` to override single lines without rebuilding. `en.json` is generated (key = value)
and is what the pseudo-language and `check.py` read.

## 8. Status after step A (framework + `src/UI` + `src/Core`)

Done:
- `L` (`src/Core/L.cs`), `LangDetect.cs`, `I18nCheck.cs` (`--dev --headless --i18n-check`), sinks in `Gfx`/widgets,
  `VButton` / `SectionLabel` grow-to-fit and `TextFit` shrink-before-ellipsis (non-English only; English layouts are
  pixel-identical), Settings → General → LANGUAGE (live switch: every `ScreenBase` rebuilds on `L.Changed`), the
  one-time "ValTrainer is now in …" menu notice (`LanguageToast`, `AppSettings.LanguageNotice`),
  `translations/*.json` in both export presets.
- Every user-facing string in `src/UI` and `src/Core`, plus the new skin names (`GunSkins`), crosshair-toast texts
  (`GameSession.XhairToast`), `Keybinds.Label`, `CrosshairCode.PresetNames`, `CrosshairList` button messages.
- Logic on text removed: `SkillRating.IsProgress`, `TrainingMode.LastStopLine`, `XfVisibility.EnemyHighlight`,
  `CoachRank` colours from tiers.
- Dev flags: `--lang <code|xx>`, `--riot-locale fr_FR`, `--lang-live <code>` (switches 2.5 s in), `--i18n-missing <file>`,
  `--i18n-check`.

Left for the next steps (run `extract.py` and read `i18n-todo.txt` per folder):
- `src/Modes`, `src/Modes/Agents`: drill `Name`/`Description`/`Category`/`Subtitle`/`Prompt`/`HudLines()`/`ResultLines()`
  (names and categories show English until wrapped; the UI already calls `DisplayName`/`DisplayCategory`, so marking
  the overrides `=> L.N("Gridshot")` is enough for those), `CrosshairFinderDraw`, `SensFinderDraw`,
  `ControllerDraw` ("ENTRY"/" · HIGH" passed to `TacticalMap.Label` need `L.T` there), direct `DrawString` calls
  (`AimModes.cs`, `RecoilModes.cs`, `MovementPlusOp.cs`).
- `src/Warmup` (Lock-In), `src/Agents` (`AgentRoster` summaries → `L.N`; the Lock-In preset blurb is composed there and
  needs `L.F` at the source), `src/Game` (BuySession, the rest of GameSession), `src/Valorant`
  (`CrosshairList` tags "Saved {j}/{n}" are interpolated → `L.F` where built, but CultureCheck reads `Tag`
  (`Contains("only one")`): keep an English copy; `ValorantImporter.Source` / `CrosshairNote`; `Keybinds.Text`
  "unbound"), `UiTheme.EnemyColorNames` is done.
- Phase 2, `src/Analysis` (coach): `F(...)` → `L.F`, `Coach.SkillName`, diagnosis texts, `RunSummary`; the UI side
  already draws them through sinks.
- Dates ("NOV 6") stay English: `L` has no date formatting yet (add `L.Date(DateTime)` with the UI culture's month
  names if wanted).
- `CONTRIBUTING.md` (repo root) "Translations" section, `tools/selftest.ps1` / CI running `--i18n-check` and
  `check.py --all`, `build-release.ps1` post-export `--i18n-check`, the bug report's `ui_lang` (the relay ignores
  unknown fields, so it needs a relay change to show it).
