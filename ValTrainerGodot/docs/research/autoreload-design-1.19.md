# Auto reload on death / new round (1.19 design)

Discord request (Nov 5): "I'd really love if the weapons reload upon death. There's been so many situations where I simply
wasn't able to fight back at all."

Research only. Nothing here has been built or run. All `file:line` refs are against `main` at `bd24c8c` (v1.18.0 + winget
release step). Paths are relative to `ValTrainerGodot/`.

---

## 1. What happens today

### 1.1 The gun

- `src/Game/Weapon/WeaponController.cs:41` `public int Ammo, Reserve;` and `:43` `ReloadLeft`. The constructor (`:88-97`)
  is the only place that fills the magazine (`Ammo = def.Mag; Reserve = def.Reserve;` at `:91-92`).
- `ResetRecoil()` (`:108-117`) is documented as "New round / respawn: no leftover recoil". It clears heat, burst, alt chain
  and spin, but leaves the ammo alone.
- `StartReload(bool infinite)` (`:122-135`) and `TickReload` (`:137-159`): a whole-magazine reload, a partial one (Outlaw),
  or shell by shell (Bucky, Marshal: `roundsToLoad`/`roundsLoaded`, `:62`).
- An empty magazine starts a reload by itself (`:204`, `:217`, and `:238` after the last shot).
- `GameSession.RunFrame` ticks the gun every frame **even while the player is dead** (`src/Game/GameSession.cs:559-564`).
  Only the trigger is gated (`gunInHand = !Player.Dead && …`, `:562`). So a reload that started at death keeps running
  during the respawn wait.
- The manual reload key: `GameSession.cs:408-409` (only when `!Mode.InfiniteAmmo`).

### 1.2 Which drills have a real magazine

- `TrainingMode.InfiniteAmmo => true` and `InfiniteReserve => true` are the defaults (`src/Modes/TrainingMode.cs:46-49`).
  A bottomless magazine means `Ammo` never goes down (`WeaponController.cs:321`).
- `BotMode` overrides them to `InfiniteAmmo => false` and `InfiniteReserve => true` (`src/Modes/BotModes.cs:13-14`). That
  gives real magazines and reloads, but the reserve never runs out. Every bot, movement, map, tactical and agent drill
  inherits this.
- Drills that set it back to bottomless: `PeekMixMode` (`src/Modes/PeekMixMode.cs:18`) and `TargetSwitchMode`
  (`src/Modes/AimPlusSwitch.cs:21`).
- Drills with a real magazine that are not `BotMode`: `SprayControlMode` (`src/Modes/RecoilModes.cs:50-51`), and Chamber
  (`src/Modes/Agents/ChamberDrill.cs:25-26`, which is also the only drill with no reserve).

### 1.3 Death, respawn and new rounds

Every bot hit goes through `GameSession.DamagePlayer` (`GameSession.cs:957-971`). Fall damage goes through
`MovementSession.FallDamage` (`src/Game/MovementSession.cs:98-108`). Both call `Mode.OnPlayerDied()` (base is a no-op at
`TrainingMode.cs:134`). Every drill brings the player back through **`GameSession.Respawn`** (`GameSession.cs:986-1000`),
which is `IGame.Respawn` and documented as "Teleport the player (new round / new spot)" (`src/Game/IGame.cs:75-76`).
`Respawn` resets the mover, the view, the recoil (`:997` `Gun?.ResetRecoil()`) and HP (`:998` `Player.Reset()`), **but
not the magazine**. That one missing line is the bug.

| Drill (key) | Can the bots kill you? | Where you die | Where you come back | Magazine today |
|---|---|---|---|---|
| Deathmatch (`deathmatch`) | yes | `DeathmatchMode.cs:286-306` | `RespawnPlayer` `:261-282`: `G.Respawn` `:264`, then `ApplyNextLifeGun` `:273`, then **refill `:274`** | full (refill **added in v1.18.0**, commit `df76ec7`, 2026-10-31; not in CHANGELOG). Every kill also refills, `:318-319` (since Deathmatch shipped, `aee0565`). |
| Strafe Duels (`strafeduel`) | yes | `StrafeDuelMode.cs:157-164` | `G.Respawn` `:123` after 1.2 s; next duel `SpawnDuel` `:130-148` (HP reset `:132`) | **carries over**: after a death, and also from duel to duel |
| Peek Duels (`peekduel`) | yes | `MovementModes.cs:194-203` | `G.Respawn` `:186` after 1.2 s; next duel at `ToCover→Delay` (HP reset `:170`) | **carries over** |
| Site Clear (`siteclear`) | yes | `MapModes.cs:118-124` | `NewRound` `:63-87`, `G.Respawn` `:85` every round | **carries over** round to round |
| Flash Dodge (`flashmap`) | yes | `MapModes.cs:553-559` | `NewRound` `:196-214`, `G.Respawn` `:208` every rep | **carries over** |
| Anchor (`anchor`) | yes | `DefenseModes.cs:432-437` | `NewRound` `:188-218`, `G.Respawn` `:201` | **carries over** |
| Sound Lock (`sound`) | yes | `DefenseSound.cs:521-530` | `NewRep` `:260-266`: `G.Respawn` if dead (`:265`), else only `Player.Reset()` (`:266`) | **carries over** |
| Post-plant / Retake (`postplant`, `retake`) | yes | `TacticalModes.cs:376-384`, `:703-710` | `TacticalCore.NewRound` `:92-117` → `BeginRound` → `G.Respawn` `TacticalModes.cs:111` / `:563` | **carries over** |
| Jump / Jiggle Peek (`jumppeek`, `jigglepeek`) | yes (Op bot, `MovementPlusOp.cs:292`) | `MovementPlusOp.cs:480-488`, `MovementPlusModes.cs:85-91`, `:249-254` | `OpAngleMode.NewRound` `MovementPlusOp.cs:357-361` | **carries over** |
| Smoke Execute (`smokeexec:*`) | yes | `ControllerDrills.cs:319` | `G.Respawn` `:217` per round | **carries over** |
| Mobility Entry (`mobility:*`) | yes | `DuelistDrills.cs:153-159` | `G.Respawn` `:137` per round | **carries over** |
| Flash Peek / Recon (`flashpeek:*`, `recon:*`) | yes | `InitiatorDrill.cs:148-153` | `G.Respawn` `:122` per round | **carries over** |
| Chamber (`chamber:*`) | yes | `ChamberDrill.cs:265` | `NewRound`: `G.Respawn` `:75` then **own refill `:78`** | full every round (on purpose: "Ran out of bullets" is a stat, `:242`, `:339`) |
| Mixed Peeks (`peek_mix`) | yes | `PeekMixMode.cs:426` | next trial `:103` (no respawn) | bottomless: not affected |
| Sheriff One-Taps, Strafe Bots, Peek Practice, Operator, Counter-Strafe, Long Taps, Spray Transfer, Spray Control | **no** (no `BotBrain`, nothing calls `DamagePlayer`) | — | never respawned mid-run (only by `Restart`) | real magazine and reloads, **intended**: managing the 6-round Sheriff mag or a 25/30-round spray is part of these drills |
| Crosshair / Sens Finder | no | — | `G.Respawn` per block (`CrosshairFinderMode.cs:490/648/836`, `SensFinderMode.cs:313`) | bottomless: not affected |

How this feels in play (it matches the report): in Strafe Duels or Peek Duels you die at 1–3 rounds left, or with an
empty magazine whose 2.5 s auto-reload started at your last shot (`WeaponController.cs:238`). You respawn 1.2 s later,
and the next duel starts with those 1–3 rounds, or with the reload still running for about 1.3 s. A Sheriff (6 rounds,
2.25 s reload) or a shotgun makes it worse. The map and round drills do the same: rounds start with whatever was left in
the last one, even after a death.

Deathmatch is already fixed in v1.18.0. A reporter on 1.17 or earlier would still have seen it there, because DM only
refilled on kills back then.

### 1.4 Gun switches and the buy menu (v1.18)

- `GameSession.Equip` (`src/Game/AdsSession.cs:42-55`) always builds a **new** `WeaponController` (`:47`): full magazine
  and reserve, no reload. Ammo is never kept per gun. Switching away and back gives a full gun.
- `SwitchWeapon` (`AdsSession.cs:57-70`) adds the gun's draw time (`EquipLeft = def.Equip`, `:62`).
- Buy menu (`src/Game/BuySession.cs`):
  - Deathmatch (`GunOnRespawn`, `DeathmatchMode.cs:61`, `BuySession.cs:124`) queues `nextLifeGun` (`:162-170`).
    `ApplyNextLifeGun` (`:131-140`) swaps it in on respawn with a full magazine.
  - Free play swaps in place (`:171-180`).
  - A scored run restarts (`:183` → `Restart`, `GameSession.cs:253-329`, which calls `Equip` at `:306` and `Respawn` at `:324`).
  - None of these paths has an ammo problem.

---

## 2. Design

**Rule:** a new life or a new round starts on a full magazine. The same goes for a new duel or rep in the drills that
already reset your HP for it. That is VALORANT's behaviour: Deathmatch respawns are full, every DM kill is a full reload
(Patch 1.10, cited in `deathmatch-1.18.md:55`), and weapons are reloaded and refilled at the start of every round.
Mid-life ammo management stays exactly as it is.

### 2.1 `WeaponController.Refill()`: new, about 8 lines

Add it after `ResetRecoil` (`WeaponController.cs:117`):

```csharp
/// <summary>A full magazine and reserve now (new life, new round or duel, a Deathmatch kill): any reload in progress is
/// dropped, a shell-by-shell one included. Recoil is left alone (a DM kill mid-spray keeps it); a respawn also calls
/// <see cref="ResetRecoil"/>.</summary>
public void Refill()
{
    Ammo = Def.Mag;
    Reserve = Def.Reserve;
    ReloadLeft = reloadTotal = 0f;
    roundsToLoad = roundsLoaded = 0;
}
```

- `ReloadLeft = 0` makes `Reloading`/`ReloadProgress` false or -1. The viewmodel drops its reload pose on the next frame
  (`GameSession.cs:485`), and the HUD stops showing RELOADING (`src/UI/Hud.cs:336-343`).
- The `Tick` path doesn't change. The WeaponCheck equivalence test against the frozen pre-1.18 controller
  (`WeaponCheck.cs:545-554`) is unaffected.
- Setting `Reserve` back is a no-op in every drill today: they all have infinite reserve except Chamber, whose reserve is 0.
  It is still correct for VALORANT (respawn and round start give a full reserve).

### 2.2 `GameSession.Respawn`: the one central fix

At `GameSession.cs:997` replace `Gun?.ResetRecoil();` with:

```csharp
int ammoWas = Gun?.Ammo ?? 0; bool reloadingWas = Gun?.Reloading == true;
Gun?.Refill();       // every life and every round starts on a full magazine, like VALORANT (Discord, Nov 5)
Gun?.ResetRecoil();
if (Main.I.Dev && !Demo && Gun != null && (ammoWas < Gun.Def.Mag || reloadingWas))
    Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"[refill] {Mode.Key} t={Now:0.0}: respawn, {Gun.Def.Name} {ammoWas}/{Gun.Def.Mag}{(reloadingWas ? " reloading" : "")} -> full"));
```

The dev log is for the test plan, and uses the invariant culture like the `[died]` line at `:966`. Update the `IGame.Respawn`
doc (`IGame.cs:75`) to: "Teleport the player for a new life / round: full HP, a full magazine, no recoil."

This one change fixes every "carries over" row in the table in §1.3. It also leaves the Restart, Crosshair Finder and Sens
Finder callers alone: a fresh gun or a bottomless magazine makes `Refill` a no-op there.

### 2.3 New duel or rep without a death: `IGame.RefillGun()`

Strafe Duels, Peek Duels and Sound Lock reset HP for each new duel or rep **without** calling `Respawn` when you won the last one.
Drills only see `IGame` (Chamber reaches the gun with `G is GameSession { Gun: … }`, `ChamberDrill.cs:78`), so add:

- `IGame.cs` (after `Respawn`, `:76`):
  `/// <summary>A full magazine now (a new duel / rep while alive); no reload in progress, recoil untouched.</summary> void RefillGun();`
- `GameSession` (next to `Respawn`): `public void RefillGun() => Gun?.Refill();`

Call it where HP is already reset:

| File:line | Today | Change |
|---|---|---|
| `src/Modes/StrafeDuelMode.cs:132` (`SpawnDuel`) | `G.Player.Reset();` | `G.Player.Reset(); G.RefillGun(); // every duel starts at 100 + 50 and a full magazine` |
| `src/Modes/MovementModes.cs:170` (Peek Duels, back behind the corner) | `… G.Player.Reset(); }` | `… G.Player.Reset(); G.RefillGun(); }` |
| `src/Modes/DefenseSound.cs:266` (`NewRep`, alive) | `else G.Player.Reset();` | `else { G.Player.Reset(); G.RefillGun(); }` |

Why at the duel start and not on a win: it covers wins, losses and timeouts in one place. It is also the moment the
drill already treats as "fresh": HP resets there, and the DM rule "every kill reloads" would give the same magazine after
a win. In these drills, reloading isn't what is being trained. A player who was halfway through a manual reload just gets
it finished.

### 2.4 Clean-ups (same behaviour, optional)

- `DeathmatchMode.cs:274`: remove `if (s.Gun != null) { s.Gun.Ammo = …; s.Gun.ReloadLeft = 0; }`. `G.Respawn` at `:264`
  now refills, and `ApplyNextLifeGun` at `:273` builds a full gun anyway.
- `DeathmatchMode.cs:319` (kill reload): `G.RefillGun();` instead of setting `Ammo`/`ReloadLeft` by hand. The behaviour is
  the same, and the shell-by-shell counters are also reset.
- `ChamberDrill.cs:78`: now redundant after `G.Respawn` at `:75`, which refills and resets the recoil. Either delete it and
  keep the comment, or leave it. Chamber's per-round magazine lesson is unchanged either way.

### 2.5 Where it must NOT refill, and why it doesn't

- **Sheriff One-Taps, Spray Control / Transfer, Long Taps, Strafe Bots, Peek Practice, Operator, Counter-Strafe.** The
  player never dies and these drills never call `Respawn` mid-run, so they are untouched. Running dry and reloading
  (Sheriff 6 rounds / 2.25 s, a 25/30-round spray) stays part of them.
- **Mid-life.** No refill on a won round in the map drills, beyond what the next round's `Respawn` already does. No refill
  on kills outside Deathmatch.
- **Bottomless drills** (aim drills, Mixed Peeks, Target Switch, Finders): `Refill` is a no-op.
- **The Spray Control preview** keeps its own refill (`GameSession.cs:536-542`).

### 2.6 A setting?

**No.** VALORANT never respawns or starts a round on a partial magazine, and no drill scores ammo across lives (§3). A
toggle would only bring back the behaviour that was reported as a bug. Mention it in the CHANGELOG instead, e.g. *"You
now come back from a death, and start every round, duel and rep, with a full magazine (Strafe Duels, Peek Duels, map and
agent drills; Deathmatch already did) (suggested on Discord)."* Add the `Fixes-Discord:` trailer to the commit (see the
Discord-close-trailers memory).

---

## 3. Telemetry, Rewind and scoring

- **Run telemetry does not record ammo.** `FrameSample` (`GameSession.cs:610-628`) has view, mouse, speed, focus and flags
  (firing, ADS, accurate, crouch, blind, alive, airborne). There is no reload event (`Event` kinds: died, round_*, stop,
  …). The coach (`src/Analysis/`) never reads ammo. The format and analysis stay the same.
- **3D Rewind (.vtr).** It stores only the reload *progress* for the viewmodel: `ReplayRecorder.cs:211` → `PlayerSample.Reload`
  (`ReplayTypes.cs:24`, `ReplayFile.Impl.cs:121`). After a refill it reads 0 from the respawn frame on, which is what the
  game showed, so it is correct. There is no format change, and replays play back recorded samples (no re-simulation), so
  nothing can desync.
  - Pre-existing nit, out of scope: `:211` divides by `Def.Reload` rather than using `ReloadProgress`, so partial and
    shell-by-shell reloads show the wrong progress in the Rewind.
- **Scoring and badges.** No drill scores ammo, except Chamber's "Ran out of bullets" (`ChamberDrill.cs:242`, `:339`),
  which already refills every round. Duel and round win rates (Strafe and Peek Duels `BadgeWinRate`, map drills) can only
  go up for players who used to start rounds dry. The thresholds stay as they are. Old records stay comparable: the
  difference is the handicap being removed.
- **Sim and preview.** `--simaim` and the drill previews are unaffected, apart from more consistent duels.

---

## 4. Test plan

Setup (never touch real data): build `dotnet build ValTrainerGodot\ValTrainer.csproj`, then

```powershell
$t = Join-Path $env:TEMP "vt-autoreload"; $d = "$t\data"; $v = "$t\valorant"
New-Item -ItemType Directory -Force $d, $v | Out-Null
Copy-Item "$env:APPDATA\ValTrainer\*" $d -Recurse -ErrorAction SilentlyContinue   # temp copy of settings (optional)
$proj = (Resolve-Path ValTrainerGodot).Path
```

Every Godot run goes through `tools\gshot.ps1` or `tools\godot-run.ps1`. They hold the machine-wide Godot slot and only
stop their own process tree. Keep the paths free of spaces, because gshot splits `-Extra` on spaces.

1. **Unit-style weapon check (headless).** Add a "refill" check to `WeaponCheck.MagReload` (`src/Game/WeaponCheck.cs:376-415`)
   for every gun:
   - fire 2 volleys, `StartReload(true)`, step half the reload, `Refill()`;
   - expect `Ammo == Mag`, `!Reloading`, `ReloadProgress == -1`, and that the next trigger pull fires at once
     (`Ammo == Mag-1`);
   - for Bucky and Marshal, refill in the middle of a shell-by-shell reload, idle `2 × ReloadPerRound` and expect
     `Ammo == Mag` (no stale rounds).

   Run:
   `tools\godot-run.ps1 -GodotArgs "--headless","--fixed-fps","60","--path",$proj,"--","--dev","--data-dir",$d,"--valorant-dir",$v,"--weapon-check"`
   Expect no FAIL lines.
2. **Deaths respawn full (headless sims).** A slow simulated player at Pro tier loses duels. The Sheriff makes a partial
   magazine obvious.
   `tools\godot-run.ps1 -GodotArgs "--headless","--fixed-fps","120","--quit-after","9000","--path",$proj,"--","--dev","--data-dir",$d,"--valorant-dir",$v,"--mode","strafeduel","--tier","4","--simaim","slow","--gun","Sheriff","--gun-log","--duration","60"`
   - Expect `[died] strafeduel …`, then `[strafeduel] … respawned`, then `[refill] strafeduel …: respawn, Sheriff n/6 -> full`.
   - The first `[gun] … volley` after it shows `ammo 5/6`.
   - Before the fix the same run shows `ammo (n-1)/6`, or no volley for about 1.3 s while the auto-reload finishes.
   - Repeat with `peekduel`, `siteclear --map bind`, `flashmap`, `anchor`, `sound`, `postplant`, `jumppeek`,
     `mobility:jett`, `chamber:headhunter` (regression: still 8/8 every round and the "Ran out of bullets" stat still
     counts), and `deathmatch --gun Vandal` (a full 25 after every respawn, `[dm] spawn you …`).
3. **New duel while alive.** Same `strafeduel` and `peekduel` runs with `--simaim good` and `--tier 0`, so the player wins.
   The first volley of every duel shows `ammo 5/6` (Sheriff) or `24/25` (Vandal).
4. **Must NOT change.** Run `--mode sheriff --gun-log --simaim good --duration 30` and `--mode spray_vandal --simaim good --gun-log`.
   - Ammo still counts down to 0 and reloads, with no `[refill]` lines (the Sheriff mid-life, 25 per spray).
   - `--mode gridshot`: no ammo HUD, no refill lines.
5. **Buy menu and next-life gun.**
   - `--mode deathmatch --buy-test Phantom`: the Phantom comes out full on the next respawn.
   - `--keybind-test`, whose reload and next-life checks are at `KeybindTest.cs:225-240` and `:314-325`: all PASS.
6. **Screens (gshot, --dev).**
   `tools\gshot.ps1 -Mode strafeduel -At 15,25,35,45 -Name ar -Extra "--data-dir $d --valorant-dir $v --tier 4 --simaim slow --gun Sheriff"`
   - The HUD ammo (bottom centre, `Hud.cs:314-334`) reads `6 / 24` on the frames right after a "YOU DIED" banner, with no
     RELOADING bar.
   - Do the same for `-Mode siteclear` (round start).
   - Grep `%TEMP%\gshot\ar.out.log` for `[refill]`.
7. **Full regression.** `tools\selftest.ps1` (smoke list covers every drill above, plus the `--weapon-check`, Rewind and
   coach checks). Expect exit code 0 and an untouched `--data-dir`.
