# More languages (i18n): research + design (v1.19)

Status: design only. No game source was changed. Counts come from a read-only scan of `src\` on 2026-11-06 (v1.18.0,
commit bd24c8c); the scan script is heuristic, so treat counts as "about" numbers. Use them to size the work, not as an
exact inventory. The extraction tool in section 5 produces the real list.

Community ask: Soonah (Belgian, French speaker) on Discord: *"adding support for more languages would be pretty cool"*.
Today the whole UI is English only.

---

## 0. Summary of decisions

- **Use a small C# helper (`L.T` / `L.F` / `L.P`) with flat JSON catalogues in `res://translations/`. Do not use
  Godot's TranslationServer.** Almost all text is drawn by hand (`DrawString` through `Gfx.Text*`), so Godot's
  auto-translation of Controls would barely help. The coach builds its text on worker threads (`Task.Run` in
  `CoachUi.cs:205`). The helper also needs to work in headless self-tests and from static code, and it needs no
  import step.
- **The English source string is the key.** For interpolated strings the key is the composite format
  (`"{0:0} of 30 flicks (5°+) — play Head Flicks or Spidershot"`). This is exactly `FormattableString.Format`, so the
  coach's existing `F(FormattableString)` helpers become translation hooks with a one-line change. That covers about
  170 call sites.
- **Translate where the text is drawn ("at the sink"), plus explicit wrapping.** `Gfx.Text/TextC/TextR/TextFit/TextW`,
  `VButton.Label`, `SectionLabel`, `VPanel.Title/Caption` and the Settings row helpers look plain literals up when they
  draw them. Interpolated strings use `L.F($"…")` and plurals use `L.P(n, one, other)`. Data tables (drill names,
  agent summaries, rank names) stay in English and are translated when shown.
- **Numbers keep the invariant culture** (`.` as the decimal point). This is already a hard guarantee of the codebase
  (`Boot.cs`, `CultureCheck`). Only the words change.
- **Language setting**: `AppSettings.Language` (`null` = Auto). Auto follows VALORANT's own language, which is
  readable from `C:\ProgramData\Riot Games\Metadata\valorant.live\valorant.live.product_settings.yaml` → `settings:
  locale: "fr_FR"` (read-only, verified on this PC). If that file is missing, Auto falls back to the Windows display
  language, then to English. The picker goes in Settings → General.
- **Ship first: French (requested), Spanish, Portuguese (Brazil) and German.** They are machine/LLM-translated with a
  glossary, marked `"status": "machine"`, and open to community fixes through `translations/` plus a CONTRIBUTING
  section. Ask Soonah to review French before release.
- **Phase 1**: menus, settings, drill names and descriptions, HUD, results, Lock-In, agents, gun picker, update and
  support prompts. **Phase 2**: coach texts (Assess, Insights, RunSummary, SensAdvisor, Profile, Rewind).
  **Not translated**: CHANGELOG/What's New, bug-report payloads, dev and self-test output, map callouts, key names.
- **Fonts are fine.** Segoe UI and Bahnschrift have every Latin-1 and Latin Extended-A glyph that fr/es/pt/de/tr/pl
  need, plus Cyrillic and Vietnamese. CJK would fall back to system fonts. The real risk is **width**: about 175
  `TextFit` sites truncate with "…" and many buttons have fixed widths. Phase 1 adds shrink-before-ellipsis and
  auto-sized buttons, and uses a pseudo-locale to find every overflow.

---

## 1. Code map: where UI text comes from

### 1.1 Mechanics

| Mechanism | Count | Notes |
|---|---|---|
| `.tscn` text | **0** | `Main.tscn` only attaches `src/Main.cs`; the 21 weapon `.tscn` files have no text. All UI is built in C#. |
| `Gfx.Text(…)` / `TextC` / `TextR` / `TextFit` (UiTheme.cs) | 200 / 129 / 61 / 175 call sites | Custom `DrawString` at a baseline. `TextFit` truncates with "…" to a max width. |
| `Gfx.TextW(font, "LITERAL", size)` measured separately | ~10 sites with literals | e.g. `Hud.cs:827` measures `"RESULTS"` to size a tag, `Hud.cs:994` `"COACH REVIEW"`, `ControllerReplay.cs:112` `"SMOKE REPLAY"`, `Hud.cs:1043` `"SHOW ME ›"`, `CoachUi.cs:360` `"ASCENDANT"`. **Measure and draw must use the same translated string.** |
| `VButton { Label = … }` (Widgets.cs) | many; 24 `Label = "CAPS"` assignments | The label is drawn with `TextFit`, so a long translation gets "…" instead of overflowing. Widths are fixed: `CustomMinimumSize = new Vector2(156*bw, 48)` etc. (122 fixed `CustomMinimumSize` in `src/UI`). |
| `SectionLabel.Text`, `VPanel.Title/Caption`, Settings `Lbl/Selector/Segmented/NoteRow/CodeRow/FolderRow` | ~250 in SettingsScreen alone | Most text is fixed when the screen is built; values come from lambdas (`() => …`). |
| `TooltipText` | 86 | Godot Controls; drawn by the theme's `TooltipLabel` (Segoe UI). |
| Godot `Label` | 4 `new Label` | Almost unused. |
| `TrainingMode.Name / Description / Category / Subtitle / Prompt / HudLines() / ResultLines()` | 42 / 35 / 23 / 12 / 28 / 26 / 55 overrides | `Category` is also a **logic key** (`== "Agents"`, `CategoryColor(cat)`, `GroupBy`). Translate it only when shown. |
| Data tables (`static readonly string[]`, records) | 54 tables | `AgentRoster.All` (29 agents with ability names and summaries, ~305 literals), `Benchmarks.RankNames`, `StrafePlanner.PatNames`, `UiTheme.EnemyColorNames`, `Difficulty` tier names, `MapSpots.*` callouts (128). |
| Coach composition `F(FormattableString) => FormattableString.Invariant(s)` | Assess 117, InsightsBuilder 26, SensAdvisor 18, RunSummary 11 (CoachCli 33 is dev-only) | **The best hook in the codebase**: `s.Format` is already the key, with positional `{0:0}` holes. |
| Release notes | n/a | `WhatsNewPanel` parses `res://CHANGELOG.md`. Stays English. |

### 1.2 Counts per folder (heuristic)

These are string literals that contain words, outside comments, logging, paths and keys. Dev and self-test files are
excluded (CultureCheck, CoachCli, WeaponCheck, KeybindTest, MovementCheck, DeathmatchSpawnCheck, SimAim, CoachFake,
`*Dev.cs` and similar; about 660 literals). Total: **≈4,940 literals, ≈21,700 English words.** Probably 75–85% of them
are really shown to the user. The rest are bone and node names, ini action names and similar.

| Folder | Literals | Interpolated | Words | What it is | Phase |
|---|---:|---:|---:|---|---|
| `src/UI` | 1,362 | 273 | 5,656 | menu, settings, HUD, results, drill preview, gun picker, bug report, prompts, coach and rewind screens | 1 (coach/rewind screens: 2) |
| `src/Modes` | 1,039 | 323 | 4,497 | drill names, descriptions, prompts, HUD lines, result lines, Sens Finder, Crosshair Finder | 1 |
| `src/Modes/Agents` | 487 | 115 | 1,943 | agent drill prompts, kits, plans | 1 |
| `src/Analysis` | 451 | 176 | 3,196 | coach diagnoses, evidence, fixes, insights, sens advice | **2** |
| `src/Warmup` | 422 | 103 | 1,717 | Lock-In: check-in, plan, cards, summary, match log, hand-off | 1 |
| `src/Agents` | 361 | 9 | 2,330 | AgentRoster (ability names and own-words summaries), AgentDrills | 1 |
| `src/Valorant` | 323 | 16 | 794 | mostly **not** UI (ini/action names, crosshair code fields); some notes ("No Valorant config folder found") | 1 (few) |
| `src/Core` | 197 | 78 | 716 | updater dialogs, update check, tiers, weapons, errors shown to the user | 1 |
| `src/Maps` | 128 | 0 | 409 | callouts ("A Main → A Site", "Heaven") | keep English |
| `src/Game` (+Fx, Bots, Replay, World, Weapon) | ~170 | ~38 | ~400 | BuySession and GameSession text; the rest is node and asset names | 1 (few) |

Biggest files: Assess.cs 308 · AgentRoster.cs 305 · SettingsScreen.cs 254 · BugReport.cs 191 · DuelistKits.cs 155 ·
MenuScreen.cs 99 · SettingsKeybinds.cs 79 · CrosshairFinderDraw.cs 79 · DefenseModes.cs 77 · RewindDraw.cs 76 ·
SensFinderMode.cs 75 · CoachUi.cs 74 · DrillPreview.cs 74 · CrosshairFinderMode.cs 70 · ProfileScreen.cs 70 ·
MovementPlusModes.cs 68 · GunPicker.cs 66 · Hud.cs 64 · TacticalModes.cs 64 · InsightsBuilder.cs 59 ·
LockInMatchLog.cs 56 · AgentsScreen.cs 52.

Phase sizing: **phase 1 ≈ 3,800 literals** (fewer once noise is removed) and **phase 2 ≈ 750** (Analysis 451 + CoachUi
74 + ProfileScreen 70 + RewindDraw/RewindData/RewindScreen 158). Phase 2 has the most words per string: long
explanations.

### 1.3 Patterns that matter for translation

- **String interpolation with numbers**: 1,100+ interpolated literals. They use format specifiers (`{x:0}`,
  `{v:0.00}`, `{n:0.###}`), and dotted separators are common: `"·"` appears 411 times and `" — "` 286 times.
  Interpolated holes sometimes contain **nested English** (`{(FreePlay ? "free play: …" : "…")}`, `BuySession.cs:63`).
  Those inner literals need their own `L.T`.
- **Plurals**: 22 sites like `drill{(n == 1 ? "" : "s")}`. They can't be translated as written and become `L.P`.
- **ALL-CAPS**: 124 `ToUpperInvariant()` calls on display text (mode names, categories, tier names) plus hundreds of
  caps literals (`"SETTINGS"`, `"LOCK IN"`). Caps literals are their own keys. For runtime caps, translate first and
  then upper-case (`L.Upper(L.T(name))`). Invariant upper-casing is correct for fr/es/pt/de (é→É, ß stays ß). Turkish
  needs culture-aware casing (i→İ), hence `L.Upper`.
- **Sentence assembly**: 38 `string.Join(", ", parts)` and many `a + " · " + b` joins. Each part is translated as its
  own unit, which is fine for list-style UI. Whole sentences built from parts need a positional format key instead.
- **Logic on display strings** must be fixed before translation:
  - `CoachUi.cs:646`: `!r.Headline.StartsWith("Not enough data")`. Replace with a flag on `SkillRating`.
  - `Hud.cs:276`: `line.StartsWith("Last stop")`. Replace with a structured HUD line or a marker.
  - `CrosshairHint.cs:89`: `b.Name == "enemy highlight"`. This is an internal name, so keep the name English and
    translate only when drawn.
  - `Category == "Agents"` and `CategoryColor(cat)`: keep English keys and translate at draw time.

  The extractor (section 5) flags `StartsWith("…")`, `== "…"` and `Contains("…")` on known display fields.
- **Drill names inside coach sentences**: `"… — play Head Flicks or Spidershot"`. The translated sentence must use the
  same translated drill names as the menu. A glossary check enforces this (section 5).
- **Strings that must stay English**: the bug and suggestion payload sent to the relay (the maintainer reads it),
  crosshair codes, file paths, `godot.log` lines, CLI and dev output (`CoachCli`, `--culture-test`, selftests), and
  brand names ("VALORANT", "ValTrainer", "VAL"/"TRAINER" logo).
- **Text measured for layout**: widths are fixed in many places (`Btn("SETTINGS", …, 156 * bw, 48, …)`,
  `Btn("REPORT A BUG", …, 150, 32, 14)`, `Btn("SUGGEST A FEATURE", …, 180, 32, 14)`, `DrillPreview` BACK/FREE
  PLAY/START at 160/190/260). Two-column result rows give the label `colW - valueW` (`Hud.cs:905-913`). Two custom
  word-wrap helpers exist (`ControllerDraw.Wrap`, `CrosshairFinderDraw.Wrap`); the rest is single-line.

---

## 2. Fonts and glyph coverage

`UiTheme.Init()` uses only Windows **SystemFont**s. No font files ship.

| Role | Font | Used for |
|---|---|---|
| `Body` | Segoe UI 600 | paragraphs, tooltips, notes ("has every glyph (→ — …)") |
| `Hud` | Bahnschrift → Segoe UI | HUD text |
| `Display` | Bahnschrift SemiBold Condensed → Bahnschrift → Segoe UI | big numbers and titles |
| `HudWide` / `DisplayWide` | Bahnschrift SemiBold / Display with `SpacingGlyph = 2` | tracked-out caps captions |

Glyph check (WPF `GlyphTypeface` on this PC's `C:\Windows\Fonts`):

- `éèêëàâçùûüôöîïœæÉÈÀÇŒñãõáíóúÁÍÓÚÑßäÄÖÜ«»’…–—·×°±`: **all present** in Bahnschrift, Segoe UI and Segoe UI
  Semibold. Bahnschrift lacks only `→`, which is already routed to `Body`.
- Turkish `ğĞşŞıİ`, Polish `ąćęłńśźżŁŚŻ`, Cyrillic and Vietnamese: present in both fonts.
- Japanese and Korean: **missing** in both. Godot's `SystemFont` keeps system fallback on by default, so CJK would
  render through Yu Gothic / Malgun Gothic without matching weights, and the condensed headings would look off. Keep
  CJK out of scope for 1.19.

Width risks for fr/es/pt/de. Expect about +15–30% length; German compounds are worse and caps with
`SpacingGlyph = 2` are wider still:

- **Fixed-width buttons that will truncate**: menu nav (`LOCK IN`/`AGENTS`/`COACH`/`SETTINGS`/`STATS`/`QUIT` at 156
  or 116 × bw), the support row (`SUGGEST A FEATURE` 180 → fr "SUGGÉRER UNE FONCTIONNALITÉ", de "FUNKTION
  VORSCHLAGEN"), the update banner (`RESTART TO UPDATE` 210, `SKIP THIS VERSION` 170), DrillPreview `FREE PLAY` 190,
  GunPicker `DONE` 180.
- **Tags sized from a measured English literal**: `Hud.cs:827`, `Hud.cs:994`, `Hud.cs:1043`,
  `ControllerReplay.cs:112`, `CoachUi.cs:360-363`.
- **Single-line `TextFit` rows** (175) shorten long text to "…": settings notes, drill descriptions on cards, result
  labels.
- **Settings `Segmented` options** (`"VALORANT" | "CUSTOM"`, `"OFF" | "ON"`): short segments at 50% width each.
- **HUD top bar and pause subtitle** (`"{MODE} · {TIER}"`): long French drill names plus a tier.

Mitigations, all in phase 1:
- (a) `VButton` reports a minimum size of `max(CustomMinimumSize, TextW(label) + padding)`, so HBoxes grow.
- (b) `Gfx.TextFit` first shrinks the font to 85% (`fitShrink: 0.85f`) and only then adds "…".
- (c) Tags measure the translated string.
- (d) A pseudo-locale run finds the rest (section 7).

---

## 3. Following VALORANT's language (read-only)

Verified on this PC (read-only):

```
C:\ProgramData\Riot Games\Metadata\valorant.live\valorant.live.product_settings.yaml
  locale_data:
      available_locales: ["en_US","de_DE","ar_AE","es_ES","es_MX","fr_FR","id_ID","it_IT","ja_JP","ko_KR",
                          "pl_PL","pt_BR","ru_RU","th_TH","tr_TR","vi_VN","zh_TW"]
      default_locale: "en_US"
  settings:
      locale: "en_US"        <- VALORANT's chosen language (what the Riot Client's VALORANT language setting writes)
      region: "NA"
```

Fallback: `%LOCALAPPDATA%\Riot Games\Riot Client\Config\RiotClientSettings.yaml` → `install: globals: locale:` (or
`install: localization: locale:`). That is the Riot Client's language, usually the same as VALORANT's.
`%LOCALAPPDATA%\VALORANT\Saved\Config\…` (where ValTrainer imports sens, binds and crosshair) has **no** locale entry;
I grepped it.

This fits the existing rules. `RiotClient.cs` already reads `C:\ProgramData\Riot Games\RiotClientInstalls.json`
read-only, so another small Riot metadata file is the same kind of access. No VALORANT file is ever written. No process
is touched.

Parser: no YAML library. Read lines and find the top-level `settings:` block. Inside it (indented), match
`locale:\s*"?([a-z]{2}_[A-Z]{2})"?`. Use `FileShare.ReadWrite` and wrap everything in try/catch, because the Riot
Client may hold the file open. Read once at startup (the file is under 2 KB).

Mapping: `fr_FR`→`fr`, `es_ES`/`es_MX`→`es` (one neutral Spanish file first; `es-419` can come later), `pt_BR`→`pt-BR`,
`de_DE`→`de`, `tr_TR`→`tr` when shipped. Anything not shipped → the next fallback.

Order for **Auto**:
1. VALORANT locale, if shipped.
2. Windows display language, if shipped. `Boot.Init` sets `CurrentUICulture` to invariant, so record
   `CultureInfo.CurrentUICulture.Name` in a new `Boot.SystemUiCulture` **before** `Apply()`.
3. English.

Dev isolation: when `--valorant-dir` is passed (every test tool does this), **skip both** the Riot files and the
Windows language so screenshots and selftests are deterministic. Add the dev flag `--riot-locale fr_FR` to simulate
detection and `--lang fr` to force a language.

---

## 4. The i18n system

### 4.1 Why a C# helper and not Godot's TranslationServer

| | Godot TranslationServer (.po/.csv) | C# `L` helper + JSON (**recommended**) |
|---|---|---|
| Fits hand-drawn UI | `Tr()` must still be called at every `DrawString`; auto-translate only covers Controls (4 Labels + tooltips) | same call sites |
| Coach on worker threads | Godot API off the main thread is risky | an immutable dictionary is safe to read from any thread |
| Headless self-tests and `CoachCli` | works, but needs imported `.translation` resources | plain file read |
| Static and record data | `Tr` is a Node method; `TranslationServer.Translate` needs a StringName per call | plain `string` |
| Hot path (HUD every frame) | interop plus StringName per lookup | one `FrozenDictionary` lookup |
| Community fixes without rebuilding | .po needs an import, so it can't simply be dropped in | drop `fr.json` into `<DataDir>\translations\` |
| Plurals and context | built in (.po `msgid_plural`, `msgctxt`) | ~30 lines: `{ "one": …, "other": … }` + `ctx|` key prefix |
| Translator tooling | Poedit, Weblate (very good) | GitHub edits, Weblate "JSON file", LLMs; the extractor can also write a `.pot` later |

Recommendation: the **C# helper with JSON**. The TranslationServer benefits (auto-translated Controls, .po tooling)
mostly don't apply here, and its costs (threading, interop, import) do. Keep Godot's TranslationServer empty so no
Control ever auto-translates on its own.

### 4.2 API: new `src/Core/L.cs` (namespace `ValTrainer.Core`)

```csharp
public static class L
{
    public static string Lang { get; }                        // "en", "fr", "es", "pt-BR", "de"
    public static event Action? Changed;                      // screens rebuild on this
    public static void Init(AppSettings s);                   // after settings load, before the first screen
    public static void Set(string? code);                     // null = Auto; loads, swaps one volatile reference, fires Changed

    public static string T(string en);                        // plain text; returns en when missing
    public static string C(string ctx, string en);            // disambiguated short words: key "toggle|ON"
    public static string F(FormattableString s);              // key = s.Format; string.Format(Invariant, tr ?? s.Format, args)
    public static string P(long n, FormattableString one, FormattableString other); // plural; key = one.Format + "|" + other.Format
    public static string Upper(string s);                     // culture-aware upper case for the UI language (tr: i→İ), never touches CurrentCulture
    public static string Raw(string s);                       // marks "never translate" for the sink (codes, paths, names)
}
```

Rules:
- `F` catches `FormatException`: a bad translation such as `"{0"` falls back to the English format and logs once.
- Plural rule per language. en/de/es: `n == 1` → one. fr/pt-BR: `n is 0 or 1` → one (CLDR, integer counts). tr: one
  form, but keep the `one`/`other` shape.
- The table is `FrozenDictionary<string, object>` (string, or a plural `{one, other}`), compared ordinally. Switching
  language replaces the whole reference, so worker threads see either the old table or the new one, never a partial
  one.
- A miss returns the input. In `--dev`, misses are recorded in a set, and `--i18n-missing` writes them to a file on
  exit.
- **Pseudo-locale** `--lang qps` (dev only) adds accents and about 35% padding:
  `"Settings"` → `"[Šéţţîñĝš ~~~]"`. Anything still in plain English after that is unwrapped. Anything showing "…" is
  a width problem.
- `en` has no file: the key is the English text.

### 4.3 Catalogue format: `ValTrainerGodot/translations/<code>.json`

```json
{
  "_meta": { "code": "fr", "name": "Français", "english": "French", "status": "machine",
             "basedOn": "1.19.0", "translators": [] },
  "SETTINGS": "PARAMÈTRES",
  "Gridshot": "Gridshot",
  "3 targets on a 3x3 grid. Warm-up for speed and click timing.": "3 cibles sur une grille 3x3. Échauffement pour la vitesse et le timing du clic.",
  "{0:0} of 30 flicks (5°+) — play Head Flicks or Spidershot": "{0:0} flicks sur 30 (5°+) — joue Head Flicks ou Spidershot",
  "{0} drill|{0} drills": { "one": "{0} exercice", "other": "{0} exercices" },
  "toggle|ON": "ACTIVÉ"
}
```

- Flat, sorted keys. Diffs stay clean and anyone can edit them on GitHub.
- `status`: `machine` | `reviewed` | `community`. The language picker shows "(machine translation · help improve it)"
  for `machine`.
- Next to the catalogues:
  - `translations/_sources.json` (generated): key → `["src/UI/MenuScreen.cs:282"]` plus width hints such as
    `"fits 156px @20"`.
  - `translations/glossary.json` (hand-kept): drill names, rank and tier names, skill names, and "keep English" terms.
- Override folder: `<DataDir>\translations\<code>.json` is merged over the shipped file at load. Translators can test
  without building, and the Settings note says where it lives.
- Packaging: add `translations/*.json` to `include_filter` in **both** presets in `export_presets.cfg` (next to
  `CHANGELOG.md, CREDITS.md`). Load with `Godot.FileAccess.GetFileAsString("res://translations/fr.json")` on the main
  thread inside `L.Init`/`Set`, then parse with `System.Text.Json`.

### 4.4 Where to call it ("sink" translation + explicit wrapping)

1. **Sinks translate plain literals**:
   - `Gfx.Text/TextC/TextR/TextFit/TextW` (UiTheme.cs)
   - `VButton.Label/Sub` and `SectionLabel.Text/Note`
   - `VPanel.Title/Caption` (Widgets.cs)
   - SettingsScreen `Lbl`, `Selector(label…)`, `Segmented(label, options…)`, the `note:` lambdas' results, `CodeRow`
     and `FolderRow` labels
   - `TooltipText`, through a small `Tip(control, text)` helper or by translating in the screens' build code

   A sink does `L.T(text)` once. `TextFit` keeps its internal truncation loop on the already-translated raw string. One
   dictionary lookup per drawn string is negligible. Dynamic strings (numbers, names) miss and come back unchanged.
   `L.Raw(…)` or a `TextRaw` overload covers crosshair codes, paths and player names that must never be looked up.
   About 560 draw sites and ~250 Settings rows are covered **without touching the call sites**.
2. **Interpolated strings**: `L.F($"…")`. Nested English inside holes gets its own `L.T`. Only define
   `F(FormattableString)` with that name: if a `string` overload also existed, C# would bind `$"…"` to `string`.
3. **Coach (phase 2)**: change the four `static string F(FormattableString s) => FormattableString.Invariant(s);` in
   `Assess.cs:33`, `InsightsBuilder.cs:12`, `RunSummary.cs:7` and `SensAdvisor.cs:25` to `=> L.F(s);`. That is 172
   sentences with a 4-line change. Plain literals in `Diagnosis(Title, Why, Fixes[])` records get `L.T` where the
   record is built, or where `CoachUi`/`ProfileScreen` draw them; pick one, not both. Leave `CoachCli.F` invariant
   English, because that is CLI output.
4. **Drills**: keep `Name`, `Description` and `Category` English (logic keys, telemetry, Discord). Add non-virtual
   display properties to `TrainingMode`: `Title => L.T(Name)`, `Blurb => L.T(Description)`,
   `CategoryTitle => L.T(Category)`. Switch about 10 display sites to them (`Hud.cs:85/671/730`,
   `StatsScreen.cs:278-279`, `GunPicker.cs:111`, `DrillPreview.cs:282/465-471`, `MenuScreen` cards and groups,
   `BuySession.cs:63`). `Prompt`, `HudLines()`, `ResultLines()` and `Subtitle` translate in their overrides (`L.T`/`L.F`)
   or at the HUD sink.
5. **Data tables**: `AgentRoster` summaries and ability names, `Benchmarks.RankNames`, Difficulty tier names,
   `StrafePlanner.PatNames` and `UiTheme.EnemyColorNames` stay English and are translated at display. **Never call
   `L.T` in a static initializer**: it would freeze the startup language.
6. **Plurals**: the 22 `== 1 ? "" : "s"` sites become `L.P(n, $"{n} drill", $"{n} drills")`.
7. **Caps**: `x.ToUpperInvariant()` on translated display text becomes `L.Upper(x)`. That matters only for Turkish, so
   it can wait until `tr` ships, but new code should use `L.Upper`.

### 4.5 Language picker (Settings → General)

- New section **LANGUAGE** at the top of the left column, before VALORANT ACCOUNT, in `SettingsScreen.BuildAim`.
  It is a `Selector("Language", …)` that cycles through `Auto`, English, Français, Español, Português (Brasil) and
  Deutsch. Language names always appear in their own language (endonyms).
- The value text for Auto shows its source: `Auto · VALORANT: Français`, `Auto · Windows: Deutsch` or
  `Auto · English`.
- Note line: for a machine catalogue, "Machine translation. Fix a line: translations folder on GitHub, or drop a file
  in <DataDir>\translations". For English: "Follows VALORANT's language when it's available".
- On change: `St.Language = code; St.Save(); L.Set(code);` then rebuild the screen (`Main.I.ShowSettings()` keeps the
  page). The menu rebuilds when shown. In-drill screens read `L` live.
- First run with Auto picking a non-English language: one toast on the menu, written in that language. It says "now in
  Français, change it in Settings → Language".
- `AppSettings.Language` is `string?`, where `null` = Auto. Existing users get Auto on update, so a French VALORANT
  player sees French on the first 1.19 launch. This is intended, and the toast explains it.

### 4.6 What not to translate (list it in CONTRIBUTING)

- Brand and proper names: VALORANT, ValTrainer, agent names, gun names (VALORANT keeps them in every language).
- Map callouts: phase 1 keeps them English. The community uses English callouts, and wrong ones would hurt more than
  English ones.
- Key names in Keybinds.
- Crosshair codes.
- CHANGELOG / What's New.
- The bug and suggestion **payload**. The panel's own text is translated. Add `"ui_lang": "fr"` to the report JSON
  so the maintainer knows which language the user saw.
- Dev, selftest and CLI output. Log lines.
- **Ability names**: VALORANT localizes them (for example, Jett's Tailwind has a French name). A dev script can fill
  them from `https://valorant-api.com/v1/agents?language=fr-FR` (`displayName` only). That is descriptive use, same as
  the English names. Our own summaries are translated normally.
- **Rank names**: VALORANT localizes them (fr: Fer, Bronze, Argent, Or, Platine, Diamant, Ascendant, Immortel,
  Radiant). Put them in `glossary.json`.
- **Gaming jargon**: French, Spanish, Portuguese and German players mostly say "flick", "spray", "peek",
  "counter-strafe", "crosshair placement", "eDPI" in English. The glossary marks these terms as **keep**, so the LLM
  doesn't produce absurd literal translations. This is the main quality risk.

---

## 5. Extracting strings: `tools/i18n/` (repo root, dev-only .NET console)

It must live **outside** `ValTrainerGodot/`, because the Godot csproj compiles every `**/*.cs` under it. It uses Roslyn
(`Microsoft.CodeAnalysis.CSharp`) so interpolated keys come out exactly as the runtime builds them.

`dotnet run --project tools/i18n -- <command>`:

- **`extract`**: walks `ValTrainerGodot/src` and collects every first argument of `L.T/C/F/P`, every string or
  interpolated string reaching a configured sink (`Gfx.Text*` arg 2, `VButton.Label` init, `SectionLabel.Text`,
  `Btn(label…)`, `Lbl`, `Selector`, `Segmented` labels and options, `TooltipText`, `Prompt`/`Name`/`Description`/
  `Subtitle` overrides, and the four Analysis `F(...)` helpers). It also takes plain literals from configured data
  tables (`AgentRoster`, `RankNames`, …). For interpolations it rebuilds the composite format the same way the
  compiler does: holes numbered in order, `{i,align:fmt}`, literal braces doubled. It writes
  `translations/_sources.json` and `translations/_template.json` (all keys, empty values) and can optionally write a
  `.pot`.
- **`todo`**: lists string literals that look user-facing but reach no sink or wrapper (the retrofit worklist, like
  the heuristic in 1.2), plus logic-on-display-string sites (`StartsWith/==/Contains` with English literals).
- **`check <code>`**: reports missing keys, stale keys (in the catalogue but no longer in source), placeholder
  mismatches (same `{n[:fmt]}` set as the English key), and plural entries without `one`/`other`. It also runs the
  **glossary consistency** check: if a key contains a glossary term, the value must contain that term's translation;
  for example, every sentence mentioning "Head Flicks" must use the same name as the drill card. It **warns on length
  ratio > 1.35** for keys whose `_sources` hint says fixed width. Exit code = errors, so it can run in CI.
- **`sync <code>`**: adds new keys with `""` values in sorted order and removes stale keys after confirmation. An empty
  value means "use English".

Machine translation workflow (per release): `extract` → `sync fr` → send the empty and changed keys, with
`glossary.json` and the `_sources` width hints, to an LLM in batches of ~200 → paste into `fr.json` → `check fr` → a
human skims the screenshots. Note the status in `_meta`.

---

## 6. Files to create and change

**Create**
- `ValTrainerGodot/src/Core/L.cs`: the helper (4.2).
- `ValTrainerGodot/src/Core/LangDetect.cs`: VALORANT/Riot locale reader, Windows UI language, mapping, dev flags
  (`--lang`, `--riot-locale`).
- `ValTrainerGodot/src/Core/I18nCheck.cs`: `--dev --i18n-check` (headless). It loads every shipped catalogue and
  formats every `F`/`P` entry with dummy args. It checks the YAML parser on fixture texts (quoted, unquoted, CRLF, BOM,
  missing `settings:`, `locale_data.default_locale` only), plural rules per language, and that `L.T` is thread-safe
  under a language swap. It prints coverage % per language. Exit code = failures, in the style of `CultureCheck`.
- `ValTrainerGodot/translations/fr.json`, `es.json`, `pt-BR.json`, `de.json`, `glossary.json`, `_sources.json`
  (generated), `_template.json` (generated).
- `tools/i18n/I18nTool.csproj`, `tools/i18n/Program.cs` (section 5) and `tools/i18n/sinks.json` (sink config).
- `CONTRIBUTING.md`: new section "Translations": how to fix a line, the override folder, `check`, the do-not-translate
  list and the tone (informal "tu" in French, "tú" in Spanish, "você" in Portuguese and "du" in German, matching how
  VALORANT talks to players).

**Change, phase 1**
- `src/Core/AppSettings.cs`: `public string? Language;   // null = Auto (VALORANT → Windows → English)`.
- `src/Core/Boot.cs`: record `SystemUiCulture` before `Apply()`. **Do not** change the invariant culture.
- `src/Main.cs`: `L.Init(Settings)` next to `UiTheme.Init()` (line ~68), the `--i18n-check` hook next to
  `--culture-test` (line ~101), and the first-run toast trigger.
- `src/UI/UiTheme.cs` (`Gfx`): sink translation in `Text/TextC/TextR/TextFit/TextW`, `TextFit` shrink-before-ellipsis
  and `TextRaw`. `UiTheme.MakeTheme` is unchanged.
- `src/UI/Widgets.cs`: `VButton` translates `Label/Sub` and overrides `_GetMinimumSize` to fit the label;
  `SectionLabel` and `VPanel` translate when they draw.
- `src/UI/SettingsScreen.cs`: LANGUAGE section, rebuild on change, row helpers translate.
- `src/UI/MenuScreen.cs`: category titles via `CategoryTitle`, nav and support buttons sized from text, toast.
- `src/Modes/TrainingMode.cs`: `Title/Blurb/CategoryTitle`. The display sites listed in 4.4 move to them.
- `src/UI/Hud.cs`, `DrillPreview.cs`, `GunPicker.cs`, `StatsScreen.cs`, `AgentsScreen.cs`, `AgentsWidgets.cs`,
  `SettingsKeybinds.cs`, `UpdatesPrompt.cs`, `UpdatingScreen.cs`, `SupportPrompt.cs`, `BugReport.cs` (UI only; the
  payload stays English and gains `ui_lang`), `src/Warmup/*`, `src/Modes/**` (Prompts/HudLines/ResultLines with
  `L.F`), `src/Core/Updater*.cs` (user-facing messages): `L.F` for interpolations, `L.P` for the 22 plurals.
- Fix the 3 logic-on-text sites (`CoachUi.cs:646`, `Hud.cs:276`, `CrosshairHint.cs:89`) and the measured-literal tags
  (`Hud.cs:827/994/1043`, `ControllerReplay.cs:112`, `CoachUi.cs:360-363`).
- `export_presets.cfg`: `include_filter="CHANGELOG.md, CREDITS.md, translations/*.json"` in both presets.
- `tools/selftest.ps1` and `.github/workflows/ci.yml`: run `--i18n-check` and `tools/i18n check` for every language
  (missing keys → warning; placeholder or plural errors → failure).
- `tools/build-release.ps1`: after export, smoke-run `ValTrainer.exe --dev --headless --i18n-check` against the
  exported build so a missing `include_filter` can't ship silently.

**Change, phase 2 (coach)**
- `src/Analysis/Assess.cs`, `InsightsBuilder.cs`, `RunSummary.cs`, `SensAdvisor.cs`: `F` → `L.F`; plain literals in
  `Diagnosis`/`SkillRating`/`SensAdvice` via `L.T`; `unit` words passed into holes via `L.T`/`L.P`. Add
  `SkillRating.HasData` and use it instead of the headline text check.
- `src/UI/CoachUi.cs`, `ProfileScreen.cs`, `RewindDraw.cs`, `RewindData.cs`, `RewindScreen.cs`, plus Lock-In coach
  lines.
- `glossary.json`: skill names (Flicking, Precision, Tracking, Reaction, Crosshair Placement, Spray Control, Movement,
  Utility).

Rough effort:
- Phase 1: infrastructure 1 day; wrapping 2–3 days (sinks do most of it, `todo` drives the rest); layout fixes 1–2
  days; LLM translation of ~3.5k strings × 4 languages + checks: under a day.
- Phase 2: 1–2 days + translation.

---

## 7. Test plan

0. **Build**: `dotnet build ValTrainerGodot\ValTrainer.csproj`.
1. **Headless checks**: `--dev --headless --i18n-check` (section 6), and `tools\selftest.ps1` (existing culture tests
   de-DE and tr-TR must still pass; the coach selftest output stays English because `CoachCli` doesn't use `L`).
   Then `dotnet run --project tools\i18n -- check fr` (and es, pt-BR, de): 0 placeholder, plural and glossary errors.
2. **Throw-away folders** (paths without spaces, because `gshot.ps1` splits `-Extra` on spaces):
   ```powershell
   $t = "$env:TEMP\vt-i18n"; New-Item -ItemType Directory -Force "$t\data","$t\noval" | Out-Null
   Copy-Item "$env:APPDATA\ValTrainer\*" "$t\data" -Recurse -Force   # real stats/telemetry so Coach/Stats have content; never written back
   $env:GSHOT_OUT = "$t\shots"
   $x = "--data-dir $t\data --valorant-dir $t\noval --lang fr"
   ```
   `--dev` never writes data unless `--write-data` is passed, and the empty `--valorant-dir` shows the defaults, which
   also turns Auto detection off. The script is `tools\gshot.ps1` in the repo root (it adds `--dev` itself; `-Client`
   saves at the native 1600×900).
3. **French screenshots of the key screens** (`& tools\gshot.ps1 -Client -Name <n> -At <t> [-Mode <key>] -Extra
   "$x <flags>"`):

   | Name | Mode / flags | At |
   |---|---|---|
   | `fr_menu` | (none) | 6 |
   | `fr_settings` | `--screen settings` | 6 |
   | `fr_keybinds` | `--screen keybinds` | 6 |
   | `fr_about` | `--screen about` | 6 |
   | `fr_stats` | `--screen stats` | 6 |
   | `fr_coach` | `--screen profile` (phase 2) | 8 |
   | `fr_lockin` | `--screen lockin` | 6 |
   | `fr_agents` | `--screen agents` | 6 |
   | `fr_preview` | `--preview gridshot` | 7 |
   | `fr_gunpick` | `--preview gridshot --preview-answer gun:Phantom` (picker opens at ~3.5 s after the preview) | 8 |
   | `fr_buy` | `-Mode gridshot`, `--buy-test Phantom` (buy menu 3 s in) | 5 |
   | `fr_hud` | `-Mode gridshot` | 5 |
   | `fr_results` | `-Mode gridshot`, `--duration 4` | 9 |
   | `fr_sens` | `-Mode sensfinder` | 6 |
   | `fr_bug` | `--screen bugreport` | 6 |
   | `fr_whatsnew` | `--whats-new 1.17.0` (stays English: check it doesn't break) | 6 |
   | `fr_small` | the menu, settings and results again with `--window 1280x720` | |
   | `es_*`, `pt-BR_*`, `de_*` | menu, settings, results (German = widest) | |

   Pass: no untranslated English outside the do-not-translate list; primary buttons, headings and nav have no "…" at
   1600×900; numbers keep `.`; accented capitals render in Bahnschrift (no tofu boxes); no text overlaps.
4. **Pseudo-locale sweep**: the same table with `--lang qps`. Every plain-English string left is a missed wrap, and
   every "…" on a heading or button is a layout bug. Iterate until clean. This is the main retrofit test.
5. **Missing-key log**: run the screens with `--lang fr --i18n-missing $t\missing.txt` and confirm the file is empty or
   contains only whitelisted strings.
6. **Auto detection**: (a) unit fixtures in `--i18n-check`; (b) `--riot-locale fr_FR` without `--lang`: French, and
   the Settings value reads `Auto · VALORANT: Français`; (c) `--riot-locale ja_JP`: English (not shipped); (d) a
   manual run without dev flags on a PC whose VALORANT is set to French. Confirm the Riot files' timestamps and
   contents are unchanged afterwards.
7. **Live switch (manual)**: Settings → Language → Français → Deutsch → Auto. The screen rebuilds, the menu updates,
   starting a drill shows the new names, and a coach analysis already running on a worker thread finishes without
   errors.
8. **Exported build**: `tools\build-release.ps1`, then run the exported exe with
   `--dev --headless --i18n-check` (the catalogues must be in the .pck) and take one manual French screenshot of the
   menu from the installed build.
9. **Regression**: English screenshots of the same screens, compared with v1.18 (sink translation of English text
   must change nothing). Also check the HUD frame time with `--lang fr` against `en`; it should be within noise.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| Machine-translation quality, especially jargon and coach explanations | glossary with "keep English" terms; `status: machine` shown in the picker; French review by Soonah; community override folder + CONTRIBUTING; coach text waits for phase 2 |
| Stale translations when English changes (the key is the English text) | `tools/i18n check` reports stale and missing keys in CI; a missing key shows English, never a blank |
| Width overflow / "…" | auto-sized `VButton`, shrink-before-ellipsis, pseudo-locale sweep, `_sources` width hints with a length-ratio warning |
| Logic on display strings breaks silently | fix the 3 known sites first; `todo` flags `StartsWith/==/Contains` on English literals |
| Double translation (a translated value equal to another English key) | `check` warns when a value is also a key with a different translation; sinks skip `L.Raw` |
| Translation frozen in static initializers | rule: data tables stay English; `todo` flags `L.T` inside `static readonly` initializers |
| Bad placeholders crash a frame | `L.F` catches `FormatException` → English; `--i18n-check` formats every entry |
| Decimal comma confusion | keep invariant `.`; never set `CurrentCulture` from the UI language; `CultureCheck` stays green |
| Turkish caps (i/İ) or CJK fonts later | `L.Upper` already exists; CJK needs a bundled CC0/OFL font (Noto Sans CJK is OFL, but the project rule is CC0 assets only, so this needs a maintainer decision) |
| Riot changes the metadata path or format | the reader is best-effort and falls back to Windows, then English; the user can always pick a language by hand |
| Catalogue not exported | `include_filter` + the post-export `--i18n-check` in `build-release.ps1` |
| Bug reports in French are unreadable for triage | the payload stays English; `ui_lang` is added; the free-text field is the user's own words, as today |

---

## 9. Languages to ship first

1. **French (fr)**: the request came from here (Belgium and France). EMEA's largest Romance-language VALORANT scene
   (Vitality, Karmine Corp, Gentle Mates). The reviewer is available on Discord.
2. **Portuguese, Brazil (pt-BR)**: one of VALORANT's biggest and most engaged communities (LOUD, MIBR, FURIA, big
   streamers), with lower English comfort than EMEA players. Probably the **biggest reach per string translated**.
3. **Spanish (es, neutral)**: Spain plus LATAM (VALORANT ships es_ES and es_MX; both map to `es` first; `es-419` can
   come later if LATAM players ask).
4. **German (de)**: large EMEA PC audience, but most German players are comfortable in English. It is also the
   **longest** language, so it is the best stress test for layout.

Next wave candidates: **Turkish (tr)**, a very large VALORANT community (needs `L.Upper`, fonts OK); Polish and
Russian (fonts OK); Japanese and Korean (need a CJK font decision). All of them follow automatically once a catalogue
exists, because the system is per-file.
