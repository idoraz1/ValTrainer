## What and why

<!-- What does this change, and why? Link the issue: "Fixes #123". -->

## How I tested it

<!-- e.g. "played Vandal Spray on Veteran with --dev", "tools\selftest.ps1 passes", before/after screenshots -->

## Checklist

- [ ] **CHANGELOG updated**: one line under `## [Unreleased]` in `CHANGELOG.md`, written for players (or the PR is labelled `skip-changelog`)
- [ ] **Tested with `--dev`** (dev runs never write my real settings or stats) and `tools\selftest.ps1` passes
- [ ] **No Riot assets**: no VALORANT art, models, sounds, maps or logos; new assets are CC0 or self-made and listed in `ValTrainerGodot/CREDITS.md`
- [ ] Doesn't touch the VALORANT process, its memory or input; only reads its config files
- [ ] Saved settings and stats from older versions still load (or this is marked as a breaking change)
