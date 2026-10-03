# Contributing to ValTrainer

Thanks for helping! Bug reports, drill ideas, tuning data and code are all welcome. This page covers the project rules,
how to build and test, how changes flow from an issue to a release, and how versions work.

- [Project rules](#project-rules)
- [Prerequisites](#prerequisites)
- [Build, run and export](#build-run-and-export)
- [Dev flags](#dev-flags)
- [Test tools](#test-tools)
- [Project layout](#project-layout)
- [Branches, commits and pull requests](#branches-commits-and-pull-requests)
- [Versions (SemVer)](#versions-semver)
- [The changelog rule](#the-changelog-rule)
- [Release process](#release-process)
- [Code signing](#code-signing)
- [Website](#website)
- [Feature management](#feature-management)

## Project rules

These are hard rules. A pull request that breaks one can't be merged.

1. **Never touch the game.** ValTrainer never reads or writes VALORANT's memory, never injects anything, never automates
   or simulates input for the game and never interacts with the VALORANT process. The only thing it does with
   VALORANT is **read its config files** (`%LOCALAPPDATA%\VALORANT\Saved\Config`), read-only.
2. **No Riot assets.** Riot's fan policy doesn't allow its IP in games and apps: no VALORANT art, models, textures,
   sounds, voice lines, maps, icons, logos or fonts, not even "just for testing". Use **CC0** or self-made assets only
   and list every third-party asset in [`ValTrainerGodot/CREDITS.md`](ValTrainerGodot/CREDITS.md). Agent, map and
   weapon *names* as plain text are fine.
3. **Dev runs never write user data.** Anything started with `--dev` must not write settings, stats or telemetry to
   the player's data folder (`%APPDATA%\ValTrainer`). Tests that need saving use `--dev --data-dir <temp folder>
   --write-data`. `tools\selftest.ps1` fails if a dev run writes anything.
4. **Nothing leaves the PC** except the optional update check to `api.github.com` and the download of a newer
   release from this project's GitHub Releases (players can turn both off). No analytics, no telemetry uploads, no
   other network requests.
5. **Stay compatible.** Settings and stats files from older versions must keep loading. If that's impossible, it's a
   MAJOR version (see [Versions](#versions-semver)).

## Prerequisites

- **Windows 10 or 11, 64-bit.**
- **Godot 4.7.2 .NET** ("Godot Engine - .NET", Windows 64-bit) from <https://godotengine.org/download/archive/4.7.2-stable/>.
  The exact version CI uses is in [`tools/godot-version.txt`](tools/godot-version.txt). Either:
  - unzip it into `tools\godot\` so that `tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`
    exists (that folder is git-ignored), or
  - unzip it anywhere and set the `GODOT` environment variable to the `..._console.exe`.
- **.NET 8 SDK** (or newer): <https://dotnet.microsoft.com/download/dotnet/8.0>.
- For release builds only: the Godot 4.7.2 .NET **export templates** (Godot editor → Editor → Manage Export Templates →
  Download and Install) and **Inno Setup 6** (<https://jrsoftware.org/isdl.php>, or unpacked into `tools\innosetup\`).

## Build, run and export

From the repository root in PowerShell:

```powershell
# once per shell, unless GODOT is already set
$env:GODOT = "$PWD\tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

dotnet build ValTrainerGodot\ValTrainer.csproj           # compile the C# code
& $env:GODOT --headless --path ValTrainerGodot --import  # first time and after adding assets: import them
& $env:GODOT --path ValTrainerGodot -- --dev             # run it (windowed, never saves anything)
& $env:GODOT -e --path ValTrainerGodot                   # or open the editor
```

Godot creates a `.uid` file for every new C# script and an `.import` file for every new asset the first time it scans
the project (the `--import` line above does that). **Commit those files too**; Godot 4 needs them.

**Release build** (installer, portable zip and checksums in `dist\`):

```powershell
tools\build-release.ps1                 # add -SkipInstaller to build only the portable zip
tools\build-release.ps1 -Godot <console exe> -Iscc <ISCC.exe> -OutDir dist
```

It works on a private copy of the project, so your working tree is never modified. It runs in three stages, which
`-Stage app`, `-Stage package` and `-Stage checksums` run one at a time (the release workflow signs the files in
between, see [Code signing](#code-signing)). Godot only runs while the script holds the machine-wide Godot slot, so it
waits for other Godot runs (`gshot.ps1`, `selftest.ps1`, `godot-run.ps1`) to finish.

**Upgrading Godot:** change `tools/godot-version.txt`, the `Godot.NET.Sdk/x.y.z` version in
`ValTrainerGodot/ValTrainer.csproj` and `config/features` in `project.godot` together. CI fails if the first two disagree.

## Dev flags

Godot's own options go **before** `--`, ValTrainer's **after** it:
`& $env:GODOT --path ValTrainerGodot -- --dev --mode flick --tier 2`. Most switches only work together with `--dev`, so
players can never trigger them by accident.

| Flag | What it does |
|---|---|
| `--dev` | Windowed 1600×900, no mouse capture, doesn't pause when the window loses focus, uncapped FPS with an FPS log. **Never writes settings, stats or telemetry.** |
| `--mode <key>` | Starts a drill directly. Keys: `gridshot`, `flick`, `spider`, `tracking`, `strafebots`, `peek`, `operator`, `spray_vandal`, `spray_phantom`, `spray_transfer`, `counterstrafe`, `peekduel`, `siteclear`, `flashmap`, `reaction`, `sensfinder` (see `src/Modes/ModeRegistry.cs`). |
| `--tier 0-4` | Difficulty tier (Rookie … Pro), not saved. |
| `--map ascent\|bind\|haven\|split` | Map for the map and utility drills, not saved. |
| `--quality 0-3` | Graphics preset (Low … Ultra) for this run. |
| `--window WxH` | Dev window size, e.g. `1280x720` or `1720x720`, for layout tests. |
| `--screen settings\|stats\|profile` | Opens that screen at start (`profile` is the coach). |
| `--simaim <profile>` | A simulated player plays the drill: `good`, `overshoot`, `undershoot`, `nomicro`, `premature`, `slow`, `jitter`, `lowxhair`, `hesitate`, `curved`, `spraylate`, `runngun`. Tweak it with `key=value`, e.g. `good:pref=40` prefers 40 cm/360. |
| `--duration N` | Shortens timed drills to N seconds. |
| `--autofire` | Holds the trigger 2.6 s, releases 1.2 s, repeats (spray tests). |
| `--pause-at N` | Opens the pause menu N seconds into the run. |
| `--telemetry-out <dir>` | Saves the run's telemetry (`.vtt`) to that folder. |
| `--data-dir <dir>` | Uses this folder instead of `%APPDATA%\ValTrainer`. Add `--write-data` to let the dev run save there. |
| `--valorant-dir <dir>` | Reads VALORANT's settings from this `Saved\Config` folder instead of the real one. |
| `--culture <name>` | Runs in that culture (e.g. `de-DE`) instead of the invariant one, to prove parsing doesn't depend on it. |
| `--culture-test` | Locale self-test: settings, stats, telemetry and VALORANT ini parsing and number formatting. Prints `[culture-test] RESULT: PASS/FAIL`; the exit code is the number of failures. |
| `--coach-analyze [dir]` | Prints the coach's analysis (metrics, problems, run review, skill profile, sens advice) of the `.vtt` files in `dir`, then quits. Add `--brief`, `--out <file>`, `--fake-sessions N`. Works with `--headless`. Without `dir` it reads your real telemetry (read-only). |
| `--coach-selftest` | With `--coach-analyze`: synthetic cases for the rules and sens guardrails the simulated player can't reach. Prints `PASS`/`FAIL` lines. |
| `--fakecoach [full\|partial\|empty]` | Coach screen with sample data. |
| `--fakebuttons` | Shows every extra results-screen button (layout tests). |
| `--sfquick`, `--sfseed N`, `--sfsens S`, `--sfcm C`, `--sftimescale X`, `--sfexit`, `--sfshot <dir>` | Sens Finder testing: short run, fixed seed, start sens or cm/360, game speed, quit at the end, screenshots. |
| `--update-test <version>` | Pretends that version is the latest GitHub release (no network, nothing to download). |
| `--update-source <url>` | Asks this URL instead of GitHub's API for the latest release (a local fake release, see [Automatic updates](#automatic-updates)). |
| `--update-allow-dev` | Lets the updater download and install in a dev run. Needs `--data-dir` (downloads go to `<data-dir>\updates`). |
| `--update-throttle <KB/s>` | Slows the update download (progress and interrupted-download tests). |
| `--update-apply-after <s>` | Clicks RESTART TO UPDATE by itself that many seconds after the update is ready. |
| `--updates-prompt`, `--updates-consent yes\|no`, `--updates-answer yes\|no`, `--update-check-now <s>` | Update opt-in testing: force the "Check GitHub for new versions?" prompt, answer it up front, click a button after 2 s, or press CHECK NOW after that many seconds. |
| `--whats-new [fromVersion]` | Shows the "What's new" panel as if you updated from that version. |
| `--mode <key>:<agent>` | Agent drills take the agent after a colon: `flashpeek:skye`, `recon:fade`, `smokeexec:viper`, `mobility:neon`, `chamber:tdf`, `anchor:cypher`. |
| `--autothrow`, `--autosmoke`, `--autoability`, `--autosetup`, `--autotac`, `--tacghost` | Auto-play the agent and tactical drills so they can run unattended with `--simaim` (initiator throws, controller smokes, duelist abilities, sentinel setups, post-plant/retake). `--tacghost`: bots can't see you. |
| `--movescript "<keys>"` / `--movescript auto`, `--movelog`, `--movecheck`, `--jumpaudit all\|<map>\|<mode>` | Movement testing: scripted key presses, landing log, 27 physics checks, and an audit of spots a jump can reach that it shouldn't. |
| `--screen agents`, `--agent <key>`, `--agent-search <text>`, `--agent-role <role>`, `--agent-go routine\|<drill>`, `--map-picker` | Agents screen and map picker. |
| `--throw-test` | Throws test exceptions to check that they reach the log. |
| `--vmtest …` | Viewmodel and effects test harness, see `src/Game/Weapon/VmTest.cs`. |
| `--envperf`, `--envcam`, `--envscale`, `--envtweak`, `--skyyaw` | Environment and performance tuning, see `src/World/EnvDev.cs`. |

Useful Godot options (before `--`): `--headless`, `--fixed-fps 120`, `--quit-after <frames>`,
`--rendering-method gl_compatibility`, `--rendering-driver d3d12|opengl3|opengl3_angle`.

## Test tools

**`tools\selftest.ps1`** runs what CI runs: the coach self-test, the locale tests (invariant, `de-DE`, `tr-TR`), a 5-second
headless run of every timed drill with the simulated player, a coach analysis of those runs, and a check that no dev run
wrote any data. It takes about 30 seconds. Build first (`dotnet build`), then:

```powershell
tools\selftest.ps1                          # everything
tools\selftest.ps1 -Modes flick,tracking    # fewer drills
tools\selftest.ps1 -SkipSmoke               # only the coach and locale tests
```

The exit code is the number of failed checks; logs of failed checks are kept and their path is printed.

**One Godot at a time.** `tools\godot-run.ps1`, `gshot.ps1` and `selftest.ps1` share a machine-wide lock
(`Global\ValTrainerGodotSlot`), so only one Godot process runs at once even when several tools or agents test in
parallel. Running many Godot instances at the same time can freeze a PC. Start headless runs, imports and exports
through `tools\godot-run.ps1 -Seconds <limit> -GodotArgs "--path","<project>","--headless",…`.

**`tools\gshot.ps1`** starts a `--dev` run in a window and saves screenshots of **its own window only** at the given
times (other windows on top are never captured), then stops only the processes it started. Screenshots go to
`$env:GSHOT_OUT` (default `%TEMP%\gshot`).

```powershell
tools\gshot.ps1 -Mode spray_vandal -At 6,8 -Name spray -Extra "--tier 2 --autofire"
tools\gshot.ps1 -Name coach -At 7 -Client -Extra "--fakecoach --screen profile --showcase"   # --showcase hides dev labels (README shots)
tools\gshot.ps1 -Mode flick -At 33 -Name review -Client -Extra "--simaim overshoot --duration 28"   # -Client: full-size, no title bar
```

**Coach analysis** of recorded runs:

```powershell
# record a simulated run, then analyse it
& $env:GODOT --path ValTrainerGodot -- --dev --mode flick --simaim overshoot --duration 30 --telemetry-out $env:TEMP\vt-runs
& $env:GODOT --headless --path ValTrainerGodot -- --dev --coach-analyze $env:TEMP\vt-runs --brief
```

The method behind the coach is documented in [`ValTrainerGodot/docs/coach_spec.md`](ValTrainerGodot/docs/coach_spec.md).
If you change a rule or a benchmark, update the spec in the same pull request.

## Project layout

```text
.github/                 issue forms, PR template, CI and release workflows, setup-godot action
docs/images/             README screenshots
installer/               Inno Setup script for the Windows installer
signpath/                SignPath artifact configurations for code signing (see Code signing)
site-redirect/           forwards the old website address to valtrainer.github.io (see Website)
tools/
  build-release.ps1      release build: installer, portable zip, SHA256SUMS.txt
  version.ps1            print or bump the version; release notes from CHANGELOG.md
  selftest.ps1           headless self-tests (same as CI)
  gshot.ps1              screenshots of a --dev run
  setup-github.ps1       one-time GitHub labels and milestone
  godot-version.txt      the Godot version CI downloads
  godot/, innosetup/     your local Godot editor and Inno Setup (git-ignored)
ValTrainerGodot/         the Godot project
  project.godot          engine settings; application/config/version is THE app version
  Main.tscn              main scene; menus and worlds are built in code
  src/Main.cs            app root: settings, VALORANT import, screen switching
  src/Core/              settings, stats, data paths, difficulty tiers, weapons, version, update check and updater, startup fixes
  src/Valorant/          reading VALORANT's config files (sens, crosshair, keybinds, display)
  src/Modes/             the drills (listed in ModeRegistry.cs) and the Sens Finder
  src/Game/              game session, movement, weapons and viewmodel, bots, effects, telemetry recording
  src/Maps/, src/World/  map spots for Site Clear and Flash Dodge; environments, materials, lighting
  src/Analysis/          the aim coach: metrics, rank benchmarks, diagnoses, sens advice, simulated player
  src/UI/                menu, settings, stats, coach screen, HUD, What's new, theme
  src/Audio/             sound playback
  assets/                CC0 models, textures, skies, sounds and shaders (CREDITS.md, assets/MANIFEST.md)
  docs/coach_spec.md     how the aim coach works
CHANGELOG.md             player-facing changes per version
```

## Branches, commits and pull requests

- **`main` is always releasable.** Don't push half-finished work to it; CI must be green.
- Work on a branch: `feature/<short-name>` for features and drills, `fix/<short-name>` for bug fixes
  (e.g. `feature/jiggle-peek-drill`, `fix/crosshair-outline-scale`). Add the issue number if there is one:
  `fix/123-crosshair-outline`.
- Open a **pull request** into `main`. Fill in the template, link the issue ("Fixes #123") and add screenshots for
  anything visual. Draft PRs are welcome for early feedback.
- CI builds the project and runs `tools\selftest.ps1` on every PR. A second check makes sure `CHANGELOG.md` changed.
- Keep PRs focused: one feature or fix per PR is easier to review and to describe in the changelog.
- **Commit messages:** a short imperative summary ("Add jiggle-peek drill"). Conventional Commit prefixes are optional
  but welcome: `feat:`, `fix:`, `perf:`, `refactor:`, `docs:`, `test:`, `ci:`, `build:`, `chore:`, and `release:` for
  version commits.

## Versions (SemVer)

ValTrainer follows [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`. The single source of truth is
`application/config/version` in `ValTrainerGodot/project.godot`; the exe's file version, the installer, the in-game
version and the update check all read it. Change it only with `tools\version.ps1`.

| Bump | When | Examples |
|---|---|---|
| **MAJOR** (2.0.0) | Saved settings or stats from older versions stop loading or get reset, or a big overhaul of the app | new stats format without migration, a rewrite of the rank system |
| **MINOR** (1.1.0) | New drills or features | a new drill or map, a new coach analysis, importing another VALORANT setting |
| **PATCH** (1.0.1) | Fixes and tuning | bug fixes, bot timing or difficulty tuning, performance, wording |

Prereleases (`1.1.0-beta.1`, `1.1.0-rc.1`) are published as GitHub prereleases. The in-app update check ignores them, so
only people who download them by hand get them.

## The changelog rule

**Every pull request adds a line under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md)**, in the right group:
`### Added`, `### Changed`, `### Fixed`, `### Removed` (format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)).

- Write for players, not developers: "Flash Dodge: Skye's flash now plays its sound when it's behind you", not
  "Fix FlashOrb.cs audio bus".
- These lines become the GitHub Release notes and the in-app "What's new" panel, word for word.
- Changes players won't notice (CI, refactoring, docs) can skip it: label the PR `skip-changelog`.

## Release process

1. Make sure `main` is green and `## [Unreleased]` in `CHANGELOG.md` describes everything that's in it.
2. Bump the version (choose `major`, `minor` or `patch` by the table above):

   ```powershell
   tools\version.ps1 -Bump minor         # or -Bump patch / -Bump major, or -Set 1.1.0-beta.1
   ```

   This updates `project.godot`, turns `## [Unreleased]` into `## [1.1.0] - <today>` with a new empty Unreleased
   section above it, and updates the compare links at the bottom of the changelog.
3. Review the diff and commit it: `git commit -am "release: v1.1.0"`.
4. Tag and push:

   ```powershell
   git tag v1.1.0
   git push origin main --tags
   ```

5. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches the version, runs the
   self-tests, builds `ValTrainer-1.1.0-Setup.exe`, `ValTrainer-1.1.0-Portable.zip` and `SHA256SUMS.txt`, and
   publishes the GitHub Release "ValTrainer v1.1.0" with the changelog section as its notes (`tools\version.ps1 -Notes 1.1.0`).
   Versions with a `-` become prereleases.
   Once code signing is set up, the workflow stops twice to wait for you: **approve both signing requests in SignPath**
   (the exes, then the installer; see [Code signing](#code-signing)). Nothing is published until both are signed.
6. Update the winget package once the release is published: `wingetcreate update Valtrainer.ValTrainer --version X.Y.Z
   --urls "<Setup.exe URL>|x64|user" "<Setup.exe URL>|x64|machine" --token <token> --submit` (see `winget/README.md`).
7. Close the version's milestone and create the next one (`tools\setup-github.ps1` creates the next minor milestone).

Players who allowed updates get the release automatically (see [Automatic updates](#automatic-updates)): within 6 hours, running copies
download it and install it on their next restart. So **never replace the files of a published release with different
builds**. The updater checks every download against that release's `SHA256SUMS.txt`, and players who already
downloaded the old file would get a hash mismatch. Publish a new patch version instead. The asset names
(`ValTrainer-<version>-Setup.exe`, `ValTrainer-<version>-Portable.zip`, `SHA256SUMS.txt`) are part of the updater
contract: don't rename them.

If the workflow fails, fix the problem on `main` and move the tag: `git tag -d v1.1.0`,
`git push origin :refs/tags/v1.1.0`, then tag and push again. Re-running a failed release workflow is safe: it replaces
the files of an existing release.

## Code signing

Releases are signed through [SignPath Foundation](https://signpath.org/), which offers free code signing to open-source
projects: free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by SignPath Foundation.
Signing is switched on by two repository settings. Until both are set, the **Release** workflow builds unsigned releases
in one go, exactly as before.

**How it works** (`.github/workflows/release.yml`, artifact configurations in [`signpath/`](signpath/)):

1. `tools\build-release.ps1 -Stage app` exports both builds into `dist\app\`: `portable\ValTrainer.exe` (the single-file
   game) and `installer\` (`ValTrainer.exe`, `ValTrainer.pck`, the .NET folder). It checks that both exes say product
   name `ValTrainer` and product version `x.y.z.0` (from `project.godot`).
2. Both `ValTrainer.exe` files are uploaded as the workflow artifact `unsigned-app`, and
   `signpath/github-action-submit-signing-request` submits it to SignPath with the artifact configuration `app`
   ([`signpath/app.xml`](signpath/app.xml)). SignPath checks where it came from (this repository, a GitHub-hosted
   runner, the workflow run that built it) and the product name and version, then **waits for a manual approval**.
   The signed exes replace the ones in `dist\app\`.
3. `-Stage package` builds `Portable.zip` and `Setup.exe` from the signed exes.
4. `Setup.exe` is uploaded as `unsigned-setup` and signed with the artifact configuration `installer`
   ([`signpath/installer.xml`](signpath/installer.xml)): the **second approval**. SignPath can't open Inno Setup
   installers, which is why the exes are signed first and the installer is built from them.
5. `-Stage checksums` writes `SHA256SUMS.txt` over the signed files, and the release is published. If a signature
   is missing or invalid (also the exe inside the zip), the workflow fails before publishing anything.

Not signed: the uninstaller that Inno Setup generates, `ValTrainer.pck` and the Godot and .NET libraries in the
installer's .NET folder (upstream files, which SignPath Foundation doesn't sign for other projects). The asset names
don't change, so the in-app updater works the same for signed releases.

**One-time setup** (after SignPath Foundation accepted the project):

1. In SignPath (enable MFA for your account): in the project `ValTrainer` (repository URL
   `https://github.com/idoraz1/ValTrainer`), link the trusted build system **GitHub.com** and install the SignPath
   GitHub App on the repository. Create the artifact configurations `app` and `installer` and paste the contents of
   `signpath/app.xml` and `signpath/installer.xml`. In the signing policy `release-signing`, turn on origin
   verification, add yourself as approver and the CI user (below) as submitter. Releases are built from tags
   (`refs/tags/v1.2.0`), not from a branch: if the policy has a branch restriction, make sure it accepts them.
2. Create an API token for a CI user (Users > Add CI user, then add it as a submitter to the signing policy) and
   note your organization id (Settings).
3. In GitHub (repository Settings > Secrets and variables > Actions, or with the GitHub CLI):

   ```powershell
   gh variable set SIGNPATH_ORGANIZATION_ID --body "<organization id>"
   gh secret set SIGNPATH_API_TOKEN          # paste the token when asked
   # Only if your SignPath names differ from these defaults:
   gh variable set SIGNPATH_PROJECT_SLUG --body "ValTrainer"
   gh variable set SIGNPATH_POLICY_SLUG --body "release-signing"
   gh variable set SIGNPATH_APP_CONFIG --body "app"
   gh variable set SIGNPATH_INSTALLER_CONFIG --body "installer"
   ```

   `gh variable delete SIGNPATH_ORGANIZATION_ID` switches signing off again.

**Every release:** after you push the tag, the workflow waits at "Sign the exes" and later at "Sign the installer".
Open the signing request in SignPath (Signing requests, or the link in the job log), check that it comes from the
tag you just pushed, and approve it. Each step waits up to an hour. A denied or expired request fails the workflow
before anything is published: fix what's wrong and re-run it (that submits new signing requests).

**Local builds** are unsigned: `tools\build-release.ps1` (or the three stages one after the other) works as always.
To sign a local build with your own certificate, set `SIGNTOOL_CERT_THUMBPRINT` (and optionally `SIGNTOOL_PATH`,
`SIGNTOOL_TIMESTAMP_URL`): the app stage then signs both exes and the package stage signs `Setup.exe` and the
uninstaller with `signtool`. To try the staged build by hand:

```powershell
tools\build-release.ps1 -Stage app         # dist\app\portable\ValTrainer.exe, dist\app\installer\...
# (sign dist\app\portable\ValTrainer.exe and dist\app\installer\ValTrainer.exe here)
tools\build-release.ps1 -Stage package     # dist\ValTrainer-<version>-Portable.zip and -Setup.exe from dist\app
# (sign dist\ValTrainer-<version>-Setup.exe here)
tools\build-release.ps1 -Stage checksums   # dist\SHA256SUMS.txt
```

`dist\app\` is only used between stages; delete it when you're done. `-AppDir <folder>` uses another app folder.

## Automatic updates

How the updater works (`src/Core/UpdateCheck.cs`, `src/Core/Updater*.cs`, `src/UI/UpdatesPrompt.cs`,
`installer/ValTrainer.iss`):

- **Opt-in (from 1.4.1):** the program makes no network request until the player agrees (SignPath Foundation's
  privacy rule: "This program will not transfer any information to other networked systems unless specifically
  requested by the user"). On a fresh install, and once for settings from before 1.4.1, the menu asks "Check GitHub
  for new versions?" (after "What's new" when both are due). ENABLE UPDATES sets `UpdatesConsent` and
  `CheckUpdates` to true in `settings.json`; NOT NOW (or Esc) sets both to false. Old settings that already had
  "Check for updates" off count as a no. Settings → About "Check for updates" changes the same answer, and CHECK NOW
  there is the player's own request: it checks once even while automatic checks are off (nothing is downloaded in
  the background then). Every request is logged as `Update check: asking <url> (…)`.
  Dev: the prompt shows only in runs that behave like a normal launch (`--data-dir <temp> --write-data`) or with
  `--updates-prompt`. `--updates-consent yes|no` answers it up front, `--updates-answer yes|no` clicks a button after
  2 s, and `--update-check-now <s>` does CHECK NOW after s seconds. `--update-test` needs no answer (no network).
- **Check:** `GET api.github.com/repos/<repo>/releases/latest` at startup when the last answer is more than 6 hours
  old, and every 6 hours while the app runs. Prereleases and drafts are ignored.
- **Download:** a newer release that the player didn't skip is downloaded in the background on a low-priority thread
  into `%LOCALAPPDATA%\ValTrainer\updates`. Installed copies get `Setup.exe` and portable copies get `Portable.zip`.
  Only `https://github.com/<repo>/releases/download/…` links are accepted. Downloads resume with HTTP Range after a
  restart or a dropped connection, and back off (1, 5, 15, 30, 60 min) when offline. The file must match the release's
  `SHA256SUMS.txt` (and its size); otherwise it's deleted. After 2 bad downloads of a version the updater gives up on
  it, and the banner just opens the release page. `update.json` in that folder tracks the state.
- **Install:** RESTART TO UPDATE, or the next launch before the menu ("Updating ValTrainer…"), checks the file's
  SHA-256 again, then:
  - **Installed copies:** runs `Setup.exe /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP- /CURRENTUSER
    /RELAUNCH /RELAUNCHARGS=<args> /LOG=<updates>\install-<version>.log` and quits. `/RELAUNCH` is a custom switch in
    `ValTrainer.iss` that starts the app again after a silent install, with the same arguments. All-users installs
    use `/ALLUSERS`, which shows a UAC prompt, so they only update when the player clicks RESTART TO UPDATE.
  - **Portable copies:** rename `ValTrainer.exe` to `ValTrainer.exe.old` (Windows allows that for a running exe),
    move the verified new exe into place, and start it once the old process has exited. The next start deletes
    `.old`.
- **Never a loop:** each install attempt is counted in `update.json`. A launch that is still on the old version
  after 2 attempts gives up on that version and falls back to the release page. An attempt less than 3 minutes old
  is never repeated at launch (the installer may still be running).
- **Which copy is this?** Setup writes `install.ini` (AppId and `Mode=user|admin`) next to `ValTrainer.exe`. The app
  trusts it only when Windows' uninstall entry for that AppId points at its own folder. An exported exe with the game
  packed inside (no `.pck` next to it) is the portable build. Anything else, including the Godot editor and installs
  by an older Setup without `install.ini`, only gets the banner.

**Testing the updater end to end** (never against your real install: build the test installers from a private copy
of the repo with a different `AppId` and `AppName` in `installer\ValTrainer.iss`):

1. Build two releases from private copies (`tools\version.ps1 -Set 1.2.1` / `-Set 1.2.2`, then `tools\build-release.ps1`).
2. Serve the newer release's files with a local fake API on 127.0.0.1. It returns a releases/latest-style JSON whose
   `assets[].browser_download_url` point at the same server, and serves the files with Range support.
3. Install the older one silently into a temp folder (`/VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /DIR=<temp>
   /MERGETASKS="!desktopicon"`), then run it with `-- --dev --data-dir <temp> --write-data --update-allow-dev
   --update-source http://127.0.0.1:<port>/latest.json --updates-consent yes` (or click ENABLE UPDATES). Add `--update-throttle 3000` to watch the progress and
   `--update-apply-after 3` to click RESTART TO UPDATE automatically. Set `APPDATA` and `LOCALAPPDATA` to temp folders
   for the run, so Godot's logs and the portable build's .NET files stay out of your real profile.

## Website

The website <https://valtrainer.github.io/> lives in its own repository,
[Valtrainer/valtrainer.github.io](https://github.com/Valtrainer/valtrainer.github.io): a static site (plain HTML, CSS and
JavaScript, no build step, no frameworks, no trackers or cookies) that GitHub Pages publishes on every push to its
`main` branch.

- **Preview locally:** clone it, run `py -m http.server 8000` in it and open <http://127.0.0.1:8000/>.
- **Download links** ask `api.github.com` for this repository's latest release (cached per browser session), so a new
  release needs no website change.
- **Clips and screenshots** live in its `media/` folder; they're rendered by the game with Godot's Movie Maker mode
  (`--write-movie`) and encoded with ffmpeg.
- **Old address:** `site-redirect/` in this repository is published at <https://idoraz1.github.io/ValTrainer/> by
  `.github/workflows/pages.yml` and forwards every path to the new site.

## Feature management

- **Issues** come in through two forms: **Bug report** (version, Windows, GPU and renderer, installer or portable,
  steps, expected vs actual, log file) and **Feature request** (area, problem, proposal). Both get the `triage` label.
- **Labels** (created by [`tools/setup-github.ps1`](tools/setup-github.ps1)): type `bug` / `feature`; area `drill`,
  `coach`, `sens-finder`, `settings-import`, `graphics`, `compatibility`, `installer`, `documentation`; plus `breaking`
  (needs a MAJOR version), `skip-changelog`, `good first issue`, `help wanted`, `question`, `duplicate`, `wontfix`.
  Triage = add the type and area labels, then remove `triage`.
- **Milestones:** one per upcoming version (`v1.1.0`, `v1.0.1` for urgent fixes). An issue gets a milestone when it's
  planned for that release.
- **Projects board** "ValTrainer" with four columns:
  **Backlog** (triaged, not planned) → **Next** (planned for the next milestone) → **In progress** (someone is on it,
  a branch or draft PR exists) → **Done** (merged). The script prints how to set the board up once.
- **From idea to release notes:** issue → Backlog → Next + milestone → branch `feature/…` → PR that adds a line under
  `## [Unreleased]` → merged, Done → `tools\version.ps1 -Bump` turns the Unreleased lines into the version's section →
  the tag publishes them as the GitHub Release notes, and players see them in "What's new" after updating.
