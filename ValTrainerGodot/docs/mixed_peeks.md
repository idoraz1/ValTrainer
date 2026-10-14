# Mixed Peeks

Select **Mixed Peeks** (Movement) in the menu, or launch with `--mode peek_mix`.
The opponent approaches behind a wall, then uses one of eight randomized exits:
short, wide, crouched, double, jiggle, shoulder bait, silent walking, or repeek.
Running approaches give a positional audio cue; walking and crouching stay silent.
Baits respond to shots, then commit or jump during the player's recovery window.
The drill records per-type attempts, wins, losses and first-hit/transfer timing.

## Validation

Build the project, import it in Godot, then run `tools/selftest.ps1`.
For physics and landing-cue regression checks:

```powershell
godot --headless --path ValTrainerGodot -- --dev --mode peek_mix --movecheck
```

Dev-only deterministic peek coverage uses `--peek-forcekind 0` through `7` and
`--peek-forcebite` for bait-to-punish branches. These flags are ignored outside dev mode.
