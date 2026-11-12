# Weapons research for 1.18: all 18 VALORANT guns

Research date 2026-10-31. Game build checked: **VALORANT 13.09** (valorant-api `/v1/version`: `release-13.09`, built 2026-10-23).
Scope: numbers for a `WeaponDef` entry for every gun, using the same semantics as `src/Core/Weapons.cs` (degrees, cumulative recoil tables, Riot "Gun Recovery Time" / "Tap Efficiency" meanings). This document is research only. No code was changed.

## Sources and tags

| Tag | Source |
|---|---|
| **API** | `https://valorant-api.com/v1/weapons` (`weaponStats`: fireRate, magazineSize, reloadTimeSeconds, equipTimeSeconds, firstBulletAccuracy, runSpeedMultiplier, wallPenetration, shotgunPelletCount, adsStats, altShotgunStats, airBurstStats, damageRanges), fetched 2026-10-31 at build 13.09. These values come from the game files. |
| **Wiki:Gun** | `https://valorant.fandom.com/wiki/<Gun>`: infobox, *Stats* (multipliers), *Spread values* table (first-shot / max spread, standing and crouched; movement penalties) and *Update History*, read through the MediaWiki API on 2026-10-31. |
| **PN x.xx** | Official patch notes. 12.10 through 13.09 were read on playvalorant.com (`/news/game-updates/valorant-patch-notes-x-xx/`, and `...-1309/` for 13.09). Older notes were read through the Wiki's `Patch Notes/x.xx` mirror pages. There were no 13.03 or 13.07 patches. |
| **EST** | Estimate: Riot does not publish it. Includes every per-bullet recoil/spread table not taken from Weapons.cs, which I rebuilt from documented rules (see the method below). |

**Weapon patch changes since 12.09 (checked every patch through 13.09):** Judge console min spread 2.5 (12.11). Bandit tuning (13.00). **Outlaw nerf (13.01)**. Warden added (13.06). Warden buffs (13.09). None of these touch the Vandal, Phantom, Sheriff or Operator values in `Weapons.cs`.

**Heads-up:** the live game now has **20 guns**, not 18. The **Bandit** sidearm (PN 12.00) and the **Warden** scoped rifle (PN 13.06) are in the 13.09 API. Their numbers are in the appendix, in case 1.18 wants the full arsenal.

## 1. Summary table

Values that Riot publishes (API, Wiki or patch notes). Move error is the Wiki's "Movement penalties (+deg)" (added to the shot error): run / walk / crouch-walk / air. Run speed is 6.75 m/s (knife) x API `runSpeedMultiplier`: 0.85 -> 5.73, 0.80 -> 5.40, 0.76 -> 5.13, 0.75 -> 5.06. "ADS move" is API `adsStats.runSpeedMultiplier`, a fraction of the run speed. Shotgun damage is **per pellet**.

| Gun | Class | Cost | Mode | Rps (ADS) | Mag / Res | Reload | Equip | 1st shot hip / ADS | Max spread | Move err run/walk/cw/air | Run m/s | Zoom (ADS move) | Pen | Dmg H/B/L (0 m) | Special |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Classic | Sidearm | 0 | Semi | 6.75 (-) | 12 / 36 | 1.75 | 0.75 | 0.4 / - | 1.8 | 2.3 / 1.1 / 0.5 / 7 | 5.73 | - | Low | 78 / 26 / 22.1 | Alt: 3-pellet blast |
| Shorty | Sidearm (shotgun) | 300 | Semi | 3.0 (-) | 2 / 6 | 1.75 | 0.75 | 4.0 / - | 4.0 | 2.0 / 1.0 / 0.5 / 4.0 | 5.4 | - | Low | 22 / 11 / 9.35 | 15 pellets |
| Frenzy | Sidearm | 450 | Auto | 10 (-) | 15 / 45 | 1.5 | 1.0 | 0.65 / - | 1.7 | 2.0 / 0.8 / 0.5 / 7 | 5.73 | - | Low | 78 / 26 / 22.1 | - |
| Ghost | Sidearm | 500 | Semi | 6.75 (-) | 13 / 39 | 1.5 | 0.75 | 0.3 / - | 1.65 | 2.3 / 1.1 / 0.5 / 7 | 5.73 | - | Medium | 105 / 30 / 25.5 | Silenced |
| Sheriff | Sidearm | 800 | Semi | 4 (-) | 6 / 24 | 2.25 | 1.0 | 0.25 / - | 2.75 | 3 / 1.2 / 0.5 / 7 | 5.4 | - | High | 159.5 / 55 / 46.75 | - |
| Stinger | SMG | 1100 | Auto | 16 (8.47) | 20 / 60 | 2.25 | 0.75 | 0.65 / 0.35 | 1.5 | 2.5 / 1.0 / 0.15 / 10 | 5.73 | 1.15x (0.76) | Low | 67.5 / 27 / 22.95 | ADS 4-round burst |
| Spectre | SMG | 1600 | Auto | 13.333 (12.0) | 30 / 90 | 2.25 | 0.75 | 0.4 / 0.25 | 1.4 | 2.5 / 1.0 / 0.15 / 10 | 5.73 | 1.15x (0.76) | Low | 78 / 26 / 22.1 | Silenced |
| Bucky | Shotgun | 850 | Semi | 1.1 (-) | 5 / 10 | 2.5 | 1.0 | 3.0 / - | 3.0 | 2.0 / 1.0 / 0.5 / 4.0 | 5.06 | - | Low | 34 / 17 / 14 | 15 pellets; alt air-burst 5 |
| Judge | Shotgun | 1850 | Auto | 3.5 (-) | 5 / 15 | 2.2 | 1.0 | 2.5 / - | 4.0 | 2.0 / 1.0 / 0.5 / 4.0 | 5.06 | - | Low | 34 / 17 / 14.45 | 12 pellets, auto |
| Bulldog | Rifle | 2050 | Auto | 10 (6.316) | 24 / 72 | 2.5 | 1.0 | 0.3 / 0.1 | 1.25 | 6 / 3 / 0.8 / 10 | 5.4 | 1.25x (0.76) | Medium | 115.5 / 35 / 29.75 | ADS 3-round burst |
| Guardian | Rifle | 2250 | Semi | 5.25 (5.25) | 12 / 36 | 2.5 | 1.0 | 0.1 / 0.0 | 1.58 | 6 / 3 / 0.8 / 10 | 5.4 | 1.5x (0.76) | High | 195 / 65 / 48.75 | - |
| Phantom | Rifle | 2900 | Auto | 11 (9.9) | 30 / 60 | 2.5 | 1.0 | 0.2 / 0.11 | 0.9 | 6 / 3 / 0.8 / 10 | 5.4 | 1.25x (0.76) | Medium | 156 / 39 / 33 | Silenced |
| Vandal | Rifle | 2900 | Auto | 9.75 (8.775) | 25 / 50 | 2.5 | 1.0 | 0.25 / 0.157 | 1.0 | 6 / 3 / 0.8 / 10 | 5.4 | 1.25x (0.76) | Medium | 160 / 40 / 34 | - |
| Marshal | Sniper | 950 | Semi | 1.5 (1.2) | 5 / 15 | 2.5 | 1.25 | 1.0 / 0.0 | 1.0 | 15 / 10 / 7.5 / 20 | 5.4 | 3.5x (0.9) | Medium | 202 / 101 / 85.85 | Lever, 1 zoom |
| Outlaw | Sniper | 2400 | Semi | 2.75 (2.75) | 2 / 10 | 3.8 | 1.25 | 3.5 / 0.0 | 3.5 | 15 / 10 / 7.5 / 20 | 5.4 | 3.5x (0.8) | High | 238 / 140 / 119 | Double-shot, stays zoomed |
| Operator | Sniper | 4700 | Semi | 0.6 (0.6) | 5 / 10 | 3.7 | 1.5 | 5.0 / 0.0 | 5.0 | 15 / 10 / 7.5 / 15 | 5.13 | 2.5x (0.72) | High | 255 / 150 / 120 | Bolt, 2.5x/5x |
| Ares | Machine gun | 1600 | Auto | 13 (13) | 50 / 100 | 3.25 | 1.25 | 1.0 / 0.9 | 1.0 | 6.5 / 3 / 0.4 / 10 | 5.13 | 1.15x (0.76) | High | 75 / 30 / 25.5 | Reverse spread |
| Odin | Machine gun | 3200 | Auto | 12 (15.6) | 100 / 200 | 5.0 | 1.25 | 0.8 / 0.79 | 1.3 | 6.5 / 3 / 0.4 / 10 | 5.13 | 1.15x (0.76) | High | 95 / 38 / 32.3 | Fire-rate ramp 12->15.6 |

## 2. What the current `WeaponDef` cannot express (proposed fields)

The current record can express all 18 guns' primary fire. These mechanics need new data. All of the fields below can be **init-only properties with defaults**, so the existing positional constructor calls (and the 6 existing guns) compile unchanged. The per-gun snippets below use an object initializer `new(...) { ... }`.

```csharp
public enum AltFireKind { None, Ads, Burst, Shotgun, AirBurst }
public enum Penetration { Low, Medium, High }

// on WeaponDef (all optional, defaults = today's behaviour)
public int Cost { get; init; }                         // credits, display only
public Penetration Penetration { get; init; } = Penetration.Medium;
public float CrouchSpreadMul { get; init; } = 0.85f;  // replaces the hard-coded 0.85 in WeaponController.CurrentError
public float CrouchRecoilMul { get; init; } = 0.85f;  // replaces the hard-coded crouchMul (Ares / Odin 0.6)
public float RunPitchMul { get; init; } = 1.8f;       // replaces the hard-coded runMul 1.8 (Frenzy 1.5)
public float RunYawMul { get; init; } = 1f;           // Spectre 1.5 (PN 4.0)
public float AdsMoveMul { get; init; } = 0.76f;       // replaces the computed property (Marshal 0.9, Outlaw 0.8, Operator 0.72)
public float DeadzoneMul { get; init; } = 0.275f;     // Mover.DeadzoneMul per gun (Operator 0.15, PN 1.09)
public bool Silenced { get; init; }                   // Ghost / Spectre / Phantom: audio + tracer only

// shotguns
public int Pellets { get; init; } = 1;                // Shorty 15, Bucky 15, Judge 12: each pellet sampled in the Spread cone
public float PelletMaxRange { get; init; } = 0f;      // 50 m cap for Shorty / Bucky (0 = none)

// alt-fire
public AltFireKind AltFire { get; init; }             // default: Ads when Zoom > 1, else None
public int AltPellets { get; init; }                  // Classic 3, Bucky 5
public float AltRps { get; init; }                    // Classic 2.22 blasts/s
public int AltAmmoPerShot { get; init; } = 1;         // Classic 3
public float[] AltSpread { get; init; } = [];         // Classic per consecutive blast {1.9, 2.5, 6.0}; Bucky pellet cone {2.0}
public float AltRecoveryDelay { get; init; }          // Classic 0.1 s
public float AltRecoveryTime { get; init; }           // Stinger burst 0.4, Bulldog burst 0.35
public float[] AltMoveErr { get; init; } = [];        // Classic {cw, walk, run, air} = {0, 0.6, 1.5, 2.25}
public float AltCrouchSpreadMul { get; init; } = 0f;  // Classic 0.9, Stinger 0.75, Bulldog 0.75 (0 = CrouchSpreadMul)
public float AltBurstDistance { get; init; }          // Bucky canister splits at 7.5 m
public int BurstCount { get; init; } = 1;             // ADS burst: Stinger 4, Bulldog 3
public float BurstRps { get; init; }                  // rate inside a burst: Stinger 18, Bulldog 13.333
public float BurstCycle { get; init; }                // burst start to burst start: Stinger 0.472 s, Bulldog 0.475 s
public float[] AdsSpread { get; init; } = [];         // own ADS spread curve when "hip minus offset" is wrong (Stinger, Bulldog)
public float[] AdsPitch { get; init; } = [];          // own ADS pitch curve (Stinger burst)

// snipers
public bool IsSniper { get; init; }                   // replaces Family == Operator for Marshal / Outlaw
public bool UnscopeAfterShot { get; init; }           // Operator, Marshal: true; Outlaw: false (stays zoomed)
public float Zoom2 { get; init; }                     // Operator second scope level 5x
public float[] ScopedSpread { get; init; } = [];      // Outlaw second shot {0, 2.25} (scoped error is otherwise forced to 0)
public float RecoveryDelay { get; init; }             // Outlaw: recovery starts after the shot interval
public float ReloadPartial { get; init; }             // Outlaw 2.3 s with one round left
public float ReloadPerRound { get; init; }            // shell-by-shell: Bucky 0.5, Marshal 0.5 (interruptible)

// heavies
public float SpinUpTime { get; init; }                // Odin: seconds from 12 to 15.6 rps (EST 1.0)
public float SpinUpStartMul { get; init; } = 1f;      // Odin 12 / 15.6 = 0.769 (Rps = start rate)
public bool AdsSkipsSpinUp { get; init; }             // Odin ADS fires at max rate from bullet 1
```

Controller changes these imply (for the implementer): `CurrentError` should use `CrouchSpreadMul` and the ADS tables. The `AdsRpsMul` skip for `Sniper` should be dropped, because the Marshal fires at x0.8 when scoped. The bolt/unscope logic should read `UnscopeAfterShot`. `Sniper` should read `IsSniper`. The bloom `SpreadNow - FirstShotHip` must clamp at 0 for the Ares.

## 3. Recoil and spread reconstruction method (all EST)

Riot publishes rules, not per-bullet tables. I built every table not already in `Weapons.cs` like this:

1. **Shape templates come from the existing Vandal reconstruction.** Pitch: Vandal's `Pitch[]` normalised to 0..1 (slow start, steepest around bullets 4-7, plateau). Spread: Vandal's `Spread[]` normalised. Yaw: tiny protected drift (<= 0.12 deg x scale) up to `ProtectedBullets`, then Vandal's post-protection ramp normalised to the gun's max yaw. Re-running the templates with Vandal's parameters reproduces its tables exactly. With Phantom's parameters they come within 0.05 deg of its existing table.
2. **Lengths come from documented "bullets to max" rules where Riot gives them**: Frenzy max spread and recoil at bullet 5 (PN 6.11), Stinger max spread at bullet 6 (PN 11.08), Spectre error steps at bullets 3, 6, 8 (PN 4.0; built as an explicit stepped table), Ares spread from 1.0 down to 0.7 by bullet 13 (PN 4.01; reverse curve), Guardian flat for the first 3 shots before its recovery curve (PN 1.08 + 4.0), Outlaw recoil 4.0 / scoped spread 2.25 on the second shot (PN 13.01).
3. **End values**: first-shot and max spread are sourced (API / Wiki spread table). Plateau pitch and max yaw are estimates, ranked against the existing Vandal (4.5 deg) and Phantom (3.6 deg) from the community's usual wall-spray comparisons: Stinger (4.0, very steep after bullet 3, PN 2.03/11.08) ~ Bulldog (3.9) > Odin (3.8) > Ares (3.2, "pitch recoil increased" PN 4.01) ~ Guardian spam (3.2) > Classic spam (2.8) > Spectre (2.6) ~ Frenzy (2.6, "total recoil lowered" PN 6.11) > Ghost (2.4). Sheriff keeps its existing 3.7. The only sidearm pitch Riot ever published is the Bandit's "pitch recoil at max 4 -> 3" (PN 13.00), which supports pistol plateaus of about 3-4 deg.
4. **Recovery / tap efficiency** are sourced only for Vandal, Phantom, Bulldog, Guardian, Spectre (TE), Stinger/Bulldog bursts, Outlaw, Bandit. The rest are estimates: sidearms 0.3 s / TE 3, SMGs 0.3-0.35 s, machine guns 0.5 s / TE 2, shotguns 0.35-0.8 s / TE 1.
5. `HeatCap` = the bullet index where the tables plateau, as in Weapons.cs. Auto guns are `Pad`-ded to the magazine size, as for Vandal and Phantom.

The generator script (template resampling) is in the session scratchpad, not in the repo. The tables printed below are its exact output.

## 4. Per gun

Each table lists every `WeaponDef` field with its source, followed by the special mechanics and ready-to-paste C#. The object-initializer block `{ ... }` uses the proposed fields from section 2. Drop it if you paste before those fields exist. The `WeaponKind` enum needs the new members: `Classic, Shorty, Frenzy, Ghost, Stinger, Spectre, Bucky, Judge, Bulldog, Guardian, Marshal, Outlaw, Ares, Odin`.

### Classic

| Field | Value | Source |
|---|---|---|
| Cost (display) | 0 credits | API |
| Wall penetration | Low | API |
| Auto | no (semi) | API |
| Rps | 6.75 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 12 / 36 | mag: API; reserve: Wiki:Classic |
| Reload (s) | 1.75 | API |
| Equip (s) | 0.75 | API |
| FirstShotHip (deg) | 0.4 | API |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 1.8 | Wiki:Classic spread table |
| Crouch spread mul | 0.75 | Wiki:Classic Stats |
| RecoveryTime (s) | 0.3 | EST |
| TapEfficiency | 3 | EST |
| ProtectedBullets | 2 | EST |
| YawSwitchTime (s) | 0.6 | EST |
| Zoom | 1 (no ADS) |  |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.25, 0.71, 1.3, 1.88, 2.37, 2.67, 2.8` | EST (reconstructed): Vandal curve, 2.8 deg over 7 shots |
| Yaw[] (deg, cumulative) | `0.0, 0.01, 0.08, 0.19, 0.3, 0.39, 0.47, 0.5` | EST (reconstructed) |
| Spread[] (deg) | `0.4, 0.52, 0.75, 1.03, 1.31, 1.58, 1.8` | EST (reconstructed): 0.4 -> 1.8 (Wiki) over 6 shots (count EST) |
| HeatCap | 7 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.3 / 1.1 / 0.5 / 7 | Wiki:Classic + PN 9.10 (walk 1.1, run 2.3, crouch-walk 0.5) |
| RunSpeed (m/s) | 5.73 | API runSpeedMultiplier 0.85 x 6.75 |
| Damage head / body / legs | 0-30 m: 78 / 26 / 22.1<br>30-50 m: 66 / 22 / 18.7 | API |

**Special mechanics / notes**

- Right-click (valorant-api `altFireType: Shotgun`, `altShotgunStats {shotgunPelletCount: 3, burstRate: 2.22}`): fires **3 pellets at once** (a shotgun blast, not a sequential burst), 2.22 blasts/s (0.45 s apart), uses 3 rounds per blast (well-known behaviour, EST: the API does not state ammo per blast). Same per-pellet damage and range bands as primary (Wiki alt-fire TTK table: 0-30 m / 30-50 m).
- Alt-fire error (PN 2.0): pellet cone 1.9 deg on the 1st blast, 2.5 deg on the 2nd, ~6.0 deg on the 3rd/4th (Wiki lists max 5.78 standing / 5.21 crouched); recovery curve starts after 0.1 s (PN 2.0). Crouched alt-fire spread x0.9 (Wiki).
- Alt-fire movement error adds (Wiki table, PN 3.09/9.10): crouch-walk 0, walk 0.6, run 1.5, air 2.25 deg.
- Crouch spread multiplier x0.75 primary (Wiki Stats); the controller currently hard-codes 0.85.

```csharp
public static readonly WeaponDef Classic = new(
    WeaponKind.Classic, "Classic", Auto: false, Rps: 6.75f, AdsRpsMul: 1f, Mag: 12, Reserve: 36, Reload: 1.75f, Equip: 0.75f,
    FirstShotHip: 0.4f, FirstShotAds: 0.4f, MaxSpread: 1.8f,
    RecoveryTime: 0.3f, TapEfficiency: 3, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: new[] { 0f, 0.25f, 0.71f, 1.3f, 1.88f, 2.37f, 2.67f, 2.8f },
    Yaw: new[] { 0f, 0.01f, 0.08f, 0.19f, 0.3f, 0.39f, 0.47f, 0.5f },
    Spread: new[] { 0.4f, 0.52f, 0.75f, 1.03f, 1.31f, 1.58f, 1.8f }, HeatCap: 7,
    RunErr: 2.3f, WalkErr: 1.1f, CrouchWalkErr: 0.5f, AirErr: 7f, RunSpeed: 5.73f,
    DamageHead: new[] { 78f, 66f }, DamageBody: new[] { 26f, 22f }, DamageLegs: new[] { 22.1f, 18.7f }, DamageRangeEnd: new[] { 30f, 999f })
{
    Cost = 0,
    Penetration = Penetration.Low,
    CrouchSpreadMul = 0.75f,
    AltFire = AltFireKind.Shotgun,
    AltPellets = 3,
    AltRps = 2.22f,
    AltAmmoPerShot = 3,
    AltSpread = new[] { 1.9f, 2.5f, 6.0f },
    AltRecoveryDelay = 0.1f,
    AltMoveErr = new[] { 0f, 0.6f, 1.5f, 2.25f } /* crouch-walk, walk, run, air */,
    AltCrouchSpreadMul = 0.9f,
};
```

### Shorty

| Field | Value | Source |
|---|---|---|
| Cost (display) | 300 credits | API |
| Wall penetration | Low | API |
| Auto | no (semi) | API / PN 12.09 (3.33 -> 3.0) |
| Rps | 3.0 | API / PN 12.09 (3.33 -> 3.0) |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 2 / 6 | mag: API; reserve: Wiki:Shorty / PN 6.11 |
| Reload (s) | 1.75 | API |
| Equip (s) | 0.75 | API |
| FirstShotHip (deg) | 4.0 | API (pellet cone) |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 4.0 | Wiki:Shorty |
| Crouch spread mul | 0.85 | Wiki:Shorty Stats + PN 12.09 |
| RecoveryTime (s) | 0.35 | EST |
| TapEfficiency | 1 | EST |
| ProtectedBullets | 1 | n/a |
| YawSwitchTime (s) | 0.6 | n/a |
| Zoom | 1 (no ADS) |  |
| Pitch[] (deg, cumulative) | `0.0, 2.0` | EST (visual kick only) |
| Yaw[] (deg, cumulative) | `0.0, 0.3` | EST |
| Spread[] (deg) | `4.0, 4.0` | Wiki:Shorty (constant cone) |
| HeatCap | 1 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.0 / 1.0 / 0.5 / 4.0 | PN 12.09 (all shotguns: walk 1, run 2, crouch-walk 0.5, jump 4) |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-7 m: 22 / 11 / 9.35<br>7-15 m: 12 / 6 / 5.1<br>15-50 m: 6 / 3 / 2.55 | API (per pellet) |

**Special mechanics / notes**

- **15 pellets** per shot (valorant-api `shotgunPelletCount: 15`). Damage values are per pellet. Pellets do not travel past 50 m (Wiki).
- Crouch spread multiplier is x0.85 since PN 12.09 ("Crouching now provides a 15% accuracy multiplier for shotguns, matching rifles"). The Wiki Stats line (x0.75) is stale.
- Beyond 10 m (PN 1.07) a hit applies a weaker tag (30% slow for 0.5 s) instead of the normal tag - not relevant to the trainer.

```csharp
public static readonly WeaponDef Shorty = new(
    WeaponKind.Shorty, "Shorty", Auto: false, Rps: 3f, AdsRpsMul: 1f, Mag: 2, Reserve: 6, Reload: 1.75f, Equip: 0.75f,
    FirstShotHip: 4f, FirstShotAds: 4f, MaxSpread: 4f,
    RecoveryTime: 0.35f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: new[] { 0f, 2f },
    Yaw: new[] { 0f, 0.3f },
    Spread: new[] { 4f, 4f }, HeatCap: 1,
    RunErr: 2f, WalkErr: 1f, CrouchWalkErr: 0.5f, AirErr: 4f, RunSpeed: 5.4f,
    DamageHead: new[] { 22f, 12f, 6f }, DamageBody: new[] { 11f, 6f, 3f }, DamageLegs: new[] { 9.35f, 5.1f, 2.55f }, DamageRangeEnd: new[] { 7f, 15f, 999f })
{
    Cost = 300,
    Penetration = Penetration.Low,
    Pellets = 15,
    PelletMaxRange = 50f,
};
```

### Frenzy

| Field | Value | Source |
|---|---|---|
| Cost (display) | 450 credits | API |
| Wall penetration | Low | API |
| Auto | yes | API |
| Rps | 10 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 15 / 45 | mag: API / PN 9.10 (13 -> 15); reserve: Wiki:Frenzy |
| Reload (s) | 1.5 | API |
| Equip (s) | 1.0 | API (1.0; Wiki fast/instant 0.45/0.15) |
| FirstShotHip (deg) | 0.65 | API / PN 6.11 |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 1.7 | Wiki:Frenzy |
| Crouch spread mul | 0.85 | Wiki:Frenzy Stats |
| RecoveryTime (s) | 0.3 | EST |
| TapEfficiency | 2 | EST |
| ProtectedBullets | 3 | EST |
| YawSwitchTime (s) | 0.3 | EST |
| Zoom | 1 (no ADS) |  |
| Run vertical-recoil mul | 1.5 | PN 6.11 |
| Pitch[] (deg, cumulative) | `0.0, 0.38, 1.1, 1.85, 2.39, 2.6` | EST (reconstructed): max recoil reached at bullet 5 (PN 6.11), 2.6 deg EST |
| Yaw[] (deg, cumulative) | `0.0, 0.01, 0.02, 0.19, 0.45, 0.71, 0.94, 1.12, 1.2` | EST (reconstructed) |
| Spread[] (deg) | `0.65, 0.78, 0.99, 1.25, 1.49, 1.7` | EST (reconstructed): max spread at bullet 5 (PN 6.11) |
| HeatCap | 5 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.0 / 0.8 / 0.5 / 7 | Wiki:Frenzy / PN 3.0, 9.10 |
| RunSpeed (m/s) | 5.73 | API 0.85 x 6.75 |
| Damage head / body / legs | 0-20 m: 78 / 26 / 22.1<br>20-50 m: 63 / 21 / 17.85 | API |

**Special mechanics / notes**

- Full-auto only; no alt-fire / ADS (valorant-api `altFireType: null`).
- Vertical recoil multiplier while running: **1.5** (PN 6.11). The controller hard-codes 1.8 (correct for Vandal/Phantom/Spectre only).

```csharp
public static readonly WeaponDef Frenzy = new(
    WeaponKind.Frenzy, "Frenzy", Auto: true, Rps: 10f, AdsRpsMul: 1f, Mag: 15, Reserve: 45, Reload: 1.5f, Equip: 1f,
    FirstShotHip: 0.65f, FirstShotAds: 0.65f, MaxSpread: 1.7f,
    RecoveryTime: 0.3f, TapEfficiency: 2, ProtectedBullets: 3, YawSwitchTime: 0.3f, Zoom: 1f,
    Pitch: Pad(new[] { 0f, 0.38f, 1.1f, 1.85f, 2.39f, 2.6f }, 15),
    Yaw: Pad(new[] { 0f, 0.01f, 0.02f, 0.19f, 0.45f, 0.71f, 0.94f, 1.12f, 1.2f }, 15),
    Spread: Pad(new[] { 0.65f, 0.78f, 0.99f, 1.25f, 1.49f, 1.7f }, 15), HeatCap: 5,
    RunErr: 2f, WalkErr: 0.8f, CrouchWalkErr: 0.5f, AirErr: 7f, RunSpeed: 5.73f,
    DamageHead: new[] { 78f, 63f }, DamageBody: new[] { 26f, 21f }, DamageLegs: new[] { 22.1f, 17.85f }, DamageRangeEnd: new[] { 20f, 999f })
{
    Cost = 450,
    Penetration = Penetration.Low,
    RunPitchMul = 1.5f,
};
```

### Ghost

| Field | Value | Source |
|---|---|---|
| Cost (display) | 500 credits | API |
| Wall penetration | Medium | API |
| Auto | no (semi) | API |
| Rps | 6.75 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 13 / 39 | mag: API / PN 9.10 (15 -> 13); reserve: Wiki:Ghost |
| Reload (s) | 1.5 | API |
| Equip (s) | 0.75 | API |
| FirstShotHip (deg) | 0.3 | API |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 1.65 | Wiki:Ghost |
| Crouch spread mul | 0.75 | Wiki:Ghost Stats |
| RecoveryTime (s) | 0.3 | EST |
| TapEfficiency | 3 | EST |
| ProtectedBullets | 2 | EST |
| YawSwitchTime (s) | 0.6 | EST |
| Zoom | 1 (no ADS) |  |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.21, 0.61, 1.12, 1.61, 2.03, 2.29, 2.4` | EST (reconstructed): 2.4 deg over 7 shots |
| Yaw[] (deg, cumulative) | `0.0, 0.0, 0.06, 0.15, 0.23, 0.31, 0.37, 0.4` | EST (reconstructed) |
| Spread[] (deg) | `0.3, 0.39, 0.57, 0.79, 1.02, 1.25, 1.47, 1.65` | EST (reconstructed): 0.3 -> 1.65 (Wiki) over 7 shots (count EST) |
| HeatCap | 7 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.3 / 1.1 / 0.5 / 7 | Wiki:Ghost / PN 9.10 |
| RunSpeed (m/s) | 5.73 | API 0.85 x 6.75 |
| Damage head / body / legs | 0-30 m: 105 / 30 / 25.5<br>30-50 m: 87.5 / 25 / 21.25 | API |

**Special mechanics / notes**

- Silenced (valorant-api `feature: Silenced`): no enemy-visible tracers, quieter shot - audio only.
- No alt-fire. Crouch spread x0.75 (Wiki).

```csharp
public static readonly WeaponDef Ghost = new(
    WeaponKind.Ghost, "Ghost", Auto: false, Rps: 6.75f, AdsRpsMul: 1f, Mag: 13, Reserve: 39, Reload: 1.5f, Equip: 0.75f,
    FirstShotHip: 0.3f, FirstShotAds: 0.3f, MaxSpread: 1.65f,
    RecoveryTime: 0.3f, TapEfficiency: 3, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: new[] { 0f, 0.21f, 0.61f, 1.12f, 1.61f, 2.03f, 2.29f, 2.4f },
    Yaw: new[] { 0f, 0f, 0.06f, 0.15f, 0.23f, 0.31f, 0.37f, 0.4f },
    Spread: new[] { 0.3f, 0.39f, 0.57f, 0.79f, 1.02f, 1.25f, 1.47f, 1.65f }, HeatCap: 7,
    RunErr: 2.3f, WalkErr: 1.1f, CrouchWalkErr: 0.5f, AirErr: 7f, RunSpeed: 5.73f,
    DamageHead: new[] { 105f, 87.5f }, DamageBody: new[] { 30f, 25f }, DamageLegs: new[] { 25.5f, 21.25f }, DamageRangeEnd: new[] { 30f, 999f })
{
    Cost = 500,
    Penetration = Penetration.Medium,
    CrouchSpreadMul = 0.75f,
    Silenced = true,
};
```

### Sheriff (existing - verified)

| Field | Value | Source |
|---|---|---|
| Cost (display) | 800 credits | API |
| Wall penetration | High | API |
| Auto | no (semi) | API |
| Rps | 4 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 6 / 24 | mag: API; reserve: Wiki:Sheriff |
| Reload (s) | 2.25 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 0.25 | API |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 2.75 | Wiki:Sheriff |
| Crouch spread mul | 0.75 | Wiki:Sheriff Stats |
| RecoveryTime (s) | 0.4 | EST (unchanged from Weapons.cs) |
| TapEfficiency | 3 | EST |
| ProtectedBullets | 1 | EST |
| YawSwitchTime (s) | 0.6 | EST |
| Zoom | 1 (no ADS) |  |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0, 1.2, 2.2, 2.9, 3.4, 3.7` | EST (reconstructed) (existing table) |
| Yaw[] (deg, cumulative) | `0, 0.2, 0.4, 0.6, 0.7, 0.8` | EST (reconstructed) (existing) |
| Spread[] (deg) | `0.25, 1.0, 2.0, 2.75, 2.75, 2.75` | EST (reconstructed) (existing) |
| HeatCap | 5 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 3 / 1.2 / 0.5 / 7 | Wiki:Sheriff / PN 3.0, 9.10 |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-30 m: 159.5 / 55 / 46.75<br>30-50 m: 145 / 50 / 42.5 | API |

**Special mechanics / notes**

- All current values verified against valorant-api 13.09 and the Wiki; no data changes since 9.10. Only gap: crouch spread multiplier is x0.75 (Wiki), the controller uses 0.85 for every gun.

```csharp
public static readonly WeaponDef Sheriff = new(
    WeaponKind.Sheriff, "Sheriff", Auto: false, Rps: 4f, AdsRpsMul: 1f, Mag: 6, Reserve: 24, Reload: 2.25f, Equip: 1f,
    FirstShotHip: 0.25f, FirstShotAds: 0.25f, MaxSpread: 2.75f,
    RecoveryTime: 0.4f, TapEfficiency: 3, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: new[] { 0f, 1.2f, 2.2f, 2.9f, 3.4f, 3.7f },
    Yaw: new[] { 0f, 0.2f, 0.4f, 0.6f, 0.7f, 0.8f },
    Spread: new[] { 0.25f, 1f, 2f, 2.75f, 2.75f, 2.75f }, HeatCap: 5,
    RunErr: 3f, WalkErr: 1.2f, CrouchWalkErr: 0.5f, AirErr: 7f, RunSpeed: 5.4f,
    DamageHead: new[] { 159.5f, 145f }, DamageBody: new[] { 55f, 50f }, DamageLegs: new[] { 46.75f, 42.5f }, DamageRangeEnd: new[] { 30f, 999f })
{
    Cost = 800,
    Penetration = Penetration.High,
    CrouchSpreadMul = 0.75f,
};
```

### Stinger

| Field | Value | Source |
|---|---|---|
| Cost (display) | 1100 credits | API |
| Wall penetration | Low | API |
| Auto | yes | API |
| Rps | 16 | API |
| AdsRpsMul | 0.5294 | API adsStats.fireRate 8.4706 / 16 (average incl. burst gaps) |
| Mag / Reserve | 20 / 60 | mag: API; reserve: Wiki:Stinger |
| Reload (s) | 2.25 | API |
| Equip (s) | 0.75 | API |
| FirstShotHip (deg) | 0.65 | API |
| FirstShotAds (deg) | 0.35 | API / PN 5.06 |
| MaxSpread (deg) | 1.5 | Wiki:Stinger / PN 11.08 (1.3 -> 1.5) |
| Crouch spread mul | 0.85 | Wiki:Stinger Stats |
| RecoveryTime (s) | 0.35 | EST (burst mode 0.4 is sourced, PN 2.03) |
| TapEfficiency | 2 | EST |
| ProtectedBullets | 3 | EST |
| YawSwitchTime (s) | 0.25 | EST |
| Zoom | 1.15 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.44, 1.29, 2.28, 3.17, 3.76, 4.0` | EST (reconstructed): climbs hard after bullet 3 (PN 2.03, 11.08), 4.0 deg EST |
| Yaw[] (deg, cumulative) | `0.0, 0.02, 0.03, 0.18, 0.4, 0.65, 0.89, 1.11, 1.31, 1.44, 1.5` | EST (reconstructed) |
| Spread[] (deg) | `0.65, 0.73, 0.87, 1.03, 1.2, 1.36, 1.5` | EST (reconstructed): max at bullet 6 (PN 11.08) |
| HeatCap | 7 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.5 / 1.0 / 0.15 / 10 | Wiki:Stinger / PN 3.0 |
| RunSpeed (m/s) | 5.73 | API 0.85 x 6.75 |
| Damage head / body / legs | 0-15 m: 67.5 / 27 / 22.95<br>15-50 m: 57 / 23 / 19 | API / PN 6.02 |

**Special mechanics / notes**

- ADS = **4-round burst** (valorant-api `adsStats.burstCount: 4`; Wiki: bullets in a burst at 18 rps, 2.118 bursts/s). Alt-fire spread: first 0.35 (PN 5.06), max 2.74 standing / 2.06 crouched (Wiki) - i.e. ADS max spread is *higher* than hip max, so the controller's "ADS = hip minus offset" rule does not fit; needs `AdsSpread[]`. Bursts after the first add more error and more pitch (PN 2.03). Burst recovery 0.4 s (PN 2.03). Crouched alt-fire spread x0.75 (Wiki).
- PN 4.07: firing input is delayed while raising ADS (no burst accuracy before the raise completes).
- Suggested ADS spread table (EST): `{ 0.35f, 0.6f, 0.9f, 1.2f, 1.7f, 2.2f, 2.74f }` indexed by heat; suggested ADS pitch table per burst bullet (EST): `{ 0f, 0.35f, 0.8f, 1.3f, 2.0f, 2.6f, 3.1f, 3.4f }`.

```csharp
public static readonly WeaponDef Stinger = new(
    WeaponKind.Stinger, "Stinger", Auto: true, Rps: 16f, AdsRpsMul: 0.5294f, Mag: 20, Reserve: 60, Reload: 2.25f, Equip: 0.75f,
    FirstShotHip: 0.65f, FirstShotAds: 0.35f, MaxSpread: 1.5f,
    RecoveryTime: 0.35f, TapEfficiency: 2, ProtectedBullets: 3, YawSwitchTime: 0.25f, Zoom: 1.15f,
    Pitch: Pad(new[] { 0f, 0.44f, 1.29f, 2.28f, 3.17f, 3.76f, 4f }, 20),
    Yaw: Pad(new[] { 0f, 0.02f, 0.03f, 0.18f, 0.4f, 0.65f, 0.89f, 1.11f, 1.31f, 1.44f, 1.5f }, 20),
    Spread: Pad(new[] { 0.65f, 0.73f, 0.87f, 1.03f, 1.2f, 1.36f, 1.5f }, 20), HeatCap: 7,
    RunErr: 2.5f, WalkErr: 1f, CrouchWalkErr: 0.15f, AirErr: 10f, RunSpeed: 5.73f,
    DamageHead: new[] { 67.5f, 57f }, DamageBody: new[] { 27f, 23f }, DamageLegs: new[] { 22.95f, 19f }, DamageRangeEnd: new[] { 15f, 999f })
{
    Cost = 1100,
    Penetration = Penetration.Low,
    AltFire = AltFireKind.Burst,
    BurstCount = 4,
    BurstRps = 18f,
    BurstCycle = 0.472f /* 2.118 bursts/s */,
    AdsSpread = new[] { 0.35f, 0.6f, 0.9f, 1.2f, 1.7f, 2.2f, 2.74f },
    AdsPitch = new[] { 0f, 0.35f, 0.8f, 1.3f, 2.0f, 2.6f, 3.1f, 3.4f },
    AltRecoveryTime = 0.4f,
    AltCrouchSpreadMul = 0.75f,
};
```

### Spectre

| Field | Value | Source |
|---|---|---|
| Cost (display) | 1600 credits | API |
| Wall penetration | Low | API |
| Auto | yes | API |
| Rps | 13.333 | API |
| AdsRpsMul | 0.9 | API 11.9997 / 13.333 |
| Mag / Reserve | 30 / 90 | mag: API; reserve: Wiki:Spectre |
| Reload (s) | 2.25 | API |
| Equip (s) | 0.75 | API |
| FirstShotHip (deg) | 0.4 | API |
| FirstShotAds (deg) | 0.25 | API |
| MaxSpread (deg) | 1.4 | Wiki:Spectre / PN 11.08 (1.5 -> 1.4) |
| Crouch spread mul | 0.85 | Wiki:Spectre Stats |
| RecoveryTime (s) | 0.3 | EST |
| TapEfficiency | 3 | PN 11.08 (2 -> 3) |
| ProtectedBullets | 5 | PN 11.08 (4 -> 5) |
| YawSwitchTime (s) | 0.28 | PN 11.08 (0.18 -> 0.28 s) |
| Zoom | 1.15 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.8 | PN 6.11 |
| Pitch[] (deg, cumulative) | `0.0, 0.19, 0.53, 1.0, 1.48, 1.94, 2.28, 2.5, 2.6` | EST (reconstructed): 2.6 deg over 8 bullets |
| Yaw[] (deg, cumulative) | `0.0, 0.02, 0.03, 0.05, 0.06, 0.17, 0.35, 0.55, 0.76, 0.96, 1.15, 1.32, 1.46, 1.55, 1.6` | EST (reconstructed) |
| Spread[] (deg) | `0.4, 0.42, 0.7, 0.74, 0.78, 1.05, 1.1, 1.4` | EST (reconstructed): error steps at bullets 3, 6, 8 (PN 4.0), 0.4 -> 1.4 |
| HeatCap | 9 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.5 / 1.0 / 0.15 / 10 | Wiki:Spectre / PN 3.0 |
| RunSpeed (m/s) | 5.73 | API 0.85 x 6.75 |
| Damage head / body / legs | 0-15 m: 78 / 26 / 22.1<br>15-30 m: 66 / 22 / 18.7<br>30-50 m: 60 / 20 / 17 | API / PN 5.12 |

**Special mechanics / notes**

- Silenced (valorant-api `feature: Silenced`).
- ADS max spread 1.25 (Wiki) = hip max 1.4 minus the 0.15 first-shot offset, so the existing ADS rule fits.
- Running/jumping recoil multipliers: pitch 1.8 (PN 6.11), yaw 1.5 (PN 4.0). Yaw-switch chance is not published (rifles use 10%; EST same).

```csharp
public static readonly WeaponDef Spectre = new(
    WeaponKind.Spectre, "Spectre", Auto: true, Rps: 13.333f, AdsRpsMul: 0.9f, Mag: 30, Reserve: 90, Reload: 2.25f, Equip: 0.75f,
    FirstShotHip: 0.4f, FirstShotAds: 0.25f, MaxSpread: 1.4f,
    RecoveryTime: 0.3f, TapEfficiency: 3, ProtectedBullets: 5, YawSwitchTime: 0.28f, Zoom: 1.15f,
    Pitch: Pad(new[] { 0f, 0.19f, 0.53f, 1f, 1.48f, 1.94f, 2.28f, 2.5f, 2.6f }, 30),
    Yaw: Pad(new[] { 0f, 0.02f, 0.03f, 0.05f, 0.06f, 0.17f, 0.35f, 0.55f, 0.76f, 0.96f, 1.15f, 1.32f, 1.46f, 1.55f, 1.6f }, 30),
    Spread: Pad(new[] { 0.4f, 0.42f, 0.7f, 0.74f, 0.78f, 1.05f, 1.1f, 1.4f }, 30), HeatCap: 9,
    RunErr: 2.5f, WalkErr: 1f, CrouchWalkErr: 0.15f, AirErr: 10f, RunSpeed: 5.73f,
    DamageHead: new[] { 78f, 66f, 60f }, DamageBody: new[] { 26f, 22f, 20f }, DamageLegs: new[] { 22.1f, 18.7f, 17f }, DamageRangeEnd: new[] { 15f, 30f, 999f })
{
    Cost = 1600,
    Penetration = Penetration.Low,
    Silenced = true,
    RunYawMul = 1.5f,
};
```

### Bucky

| Field | Value | Source |
|---|---|---|
| Cost (display) | 850 credits | API |
| Wall penetration | Low | API |
| Auto | no (semi) | API |
| Rps | 1.1 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 5 / 10 | mag: API; reserve: Wiki:Bucky |
| Reload (s) | 2.5 | API (Wiki: 0.5 s per shell) |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 3.0 | API / PN 12.09 (2.6 -> 3.0) |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 3.0 | PN 12.09 (Wiki 2.6 is stale) |
| Crouch spread mul | 0.85 | Wiki:Bucky Stats + PN 12.09 |
| RecoveryTime (s) | 0.8 | EST |
| TapEfficiency | 1 | EST |
| ProtectedBullets | 1 | n/a |
| YawSwitchTime (s) | 0.6 | n/a |
| Zoom | 1 (no ADS) |  |
| Pitch[] (deg, cumulative) | `0.0, 2.0` | EST |
| Yaw[] (deg, cumulative) | `0.0, 0.3` | EST |
| Spread[] (deg) | `3.0, 3.0` | constant cone |
| HeatCap | 1 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.0 / 1.0 / 0.5 / 4.0 | PN 12.09 (Wiki table "run 0.2" is stale) |
| RunSpeed (m/s) | 5.06 | API 0.75 x 6.75 |
| Damage head / body / legs | 0-8 m: 34 / 17 / 14<br>8-12 m: 26 / 13 / 11.05<br>12-50 m: 18 / 9 / 7.65 | API / PN 12.09 (per pellet) |

**Special mechanics / notes**

- **15 pellets** primary (valorant-api `shotgunPelletCount: 15`), damage per pellet.
- Alt-fire = **air-burst canister** (valorant-api `altFireType: AirBurst`, `airBurstStats {shotgunPelletCount: 5, burstDistance: 7.5}`): one hitscan canister (PN 0.49) that splits into **5 pellets** at 7.5 m; pellet cone 2.0 deg (PN 2.06); a target hit before 7.5 m takes 1 pellet of damage (Wiki: 40/20/15 - pre-12.09 numbers, EST now equal to the new 0-8 m pellet 34/17/14). Pellet range bands for alt-fire are measured from the burst point (Wiki).
- Pump action: reloads one shell at a time (0.5 s each), can be interrupted by firing (Wiki).
- Crouch spread x0.85 since PN 12.09 (was 0.9).

```csharp
public static readonly WeaponDef Bucky = new(
    WeaponKind.Bucky, "Bucky", Auto: false, Rps: 1.1f, AdsRpsMul: 1f, Mag: 5, Reserve: 10, Reload: 2.5f, Equip: 1f,
    FirstShotHip: 3f, FirstShotAds: 3f, MaxSpread: 3f,
    RecoveryTime: 0.8f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: new[] { 0f, 2f },
    Yaw: new[] { 0f, 0.3f },
    Spread: new[] { 3f, 3f }, HeatCap: 1,
    RunErr: 2f, WalkErr: 1f, CrouchWalkErr: 0.5f, AirErr: 4f, RunSpeed: 5.06f,
    DamageHead: new[] { 34f, 26f, 18f }, DamageBody: new[] { 17f, 13f, 9f }, DamageLegs: new[] { 14f, 11.05f, 7.65f }, DamageRangeEnd: new[] { 8f, 12f, 999f })
{
    Cost = 850,
    Penetration = Penetration.Low,
    Pellets = 15,
    PelletMaxRange = 50f,
    AltFire = AltFireKind.AirBurst,
    AltPellets = 5,
    AltBurstDistance = 7.5f,
    AltSpread = new[] { 2.0f },
    ReloadPerRound = 0.5f,
};
```

### Judge

| Field | Value | Source |
|---|---|---|
| Cost (display) | 1850 credits | API |
| Wall penetration | Low | API |
| Auto | yes | API |
| Rps | 3.5 | API |
| AdsRpsMul | 1 (no ADS) |  |
| Mag / Reserve | 5 / 15 | mag: API / PN 7.09 (Wiki infobox 7 is stale); reserve: PN 7.09 |
| Reload (s) | 2.2 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 2.5 | API / PN 12.09 + 12.11 (2.25 -> 2.5, PC and console) |
| FirstShotAds (deg) | n/a (= hip) |  |
| MaxSpread (deg) | 4.0 | Wiki:Judge |
| Crouch spread mul | 0.85 | Wiki:Judge Stats + PN 12.09 |
| RecoveryTime (s) | 0.45 | EST |
| TapEfficiency | 1 | EST |
| ProtectedBullets | 2 | EST |
| YawSwitchTime (s) | 0.6 | EST |
| Zoom | 1 (no ADS) |  |
| Pitch[] (deg, cumulative) | `0.0, 1.29, 3.17, 4.0` | EST (reconstructed): 4.0 deg over 3 shots |
| Yaw[] (deg, cumulative) | `0.0, 0.01, 0.3, 0.63, 0.8` | EST (reconstructed) |
| Spread[] (deg) | `2.5, 2.88, 3.47, 4.0` | EST (reconstructed): 2.5 -> 4.0 (Wiki) over 3 shots (count EST) |
| HeatCap | 3 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 2.0 / 1.0 / 0.5 / 4.0 | PN 12.09 (Wiki table "run 1" is stale) |
| RunSpeed (m/s) | 5.06 | API 0.75 x 6.75 |
| Damage head / body / legs | 0-10 m: 34 / 17 / 14.45<br>10-15 m: 20 / 10 / 8.5<br>15-50 m: 14 / 7 / 5.95 | API (per pellet) |

**Special mechanics / notes**

- **12 pellets** per shot (valorant-api `shotgunPelletCount: 12`), damage per pellet. Full-auto. No 50 m pellet cap (Wiki).
- Crouch spread x0.85 since PN 12.09 (was 0.75; Wiki Stats stale).

```csharp
public static readonly WeaponDef Judge = new(
    WeaponKind.Judge, "Judge", Auto: true, Rps: 3.5f, AdsRpsMul: 1f, Mag: 5, Reserve: 15, Reload: 2.2f, Equip: 1f,
    FirstShotHip: 2.5f, FirstShotAds: 2.5f, MaxSpread: 4f,
    RecoveryTime: 0.45f, TapEfficiency: 1, ProtectedBullets: 2, YawSwitchTime: 0.6f, Zoom: 1f,
    Pitch: Pad(new[] { 0f, 1.29f, 3.17f, 4f }, 5),
    Yaw: new[] { 0f, 0.01f, 0.3f, 0.63f, 0.8f },
    Spread: Pad(new[] { 2.5f, 2.88f, 3.47f, 4f }, 5), HeatCap: 3,
    RunErr: 2f, WalkErr: 1f, CrouchWalkErr: 0.5f, AirErr: 4f, RunSpeed: 5.06f,
    DamageHead: new[] { 34f, 20f, 14f }, DamageBody: new[] { 17f, 10f, 7f }, DamageLegs: new[] { 14.45f, 8.5f, 5.95f }, DamageRangeEnd: new[] { 10f, 15f, 999f })
{
    Cost = 1850,
    Penetration = Penetration.Low,
    Pellets = 12,
};
```

### Bulldog

| Field | Value | Source |
|---|---|---|
| Cost (display) | 2050 credits | API |
| Wall penetration | Medium | API |
| Auto | yes | API / PN 4.0 |
| Rps | 10 | API / PN 4.0 |
| AdsRpsMul | 0.6316 | API 6.3157 / 10 (average incl. burst gaps) |
| Mag / Reserve | 24 / 72 | mag: API; reserve: Wiki:Bulldog |
| Reload (s) | 2.5 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 0.3 | API |
| FirstShotAds (deg) | 0.1 | API |
| MaxSpread (deg) | 1.25 | Wiki:Bulldog |
| Crouch spread mul | 0.85 | Wiki:Bulldog Stats |
| RecoveryTime (s) | 0.35 | PN 0.50 (auto 0.35); burst 0.35 (PN 4.0) |
| TapEfficiency | 3 | EST |
| ProtectedBullets | 4 | EST (not in PN 11.08) |
| YawSwitchTime (s) | 0.6 | PN 11.08 (0.37 -> 0.6 s; swap chance 10%) |
| Zoom | 1.25 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.2, 0.56, 1.07, 1.64, 2.23, 2.77, 3.24, 3.58, 3.8, 3.9` | EST (reconstructed): 3.9 deg over 10 bullets |
| Yaw[] (deg, cumulative) | `0.0, 0.02, 0.04, 0.06, 0.17, 0.34, 0.55, 0.77, 0.98, 1.19, 1.4, 1.57, 1.74, 1.87, 1.96, 2.0` | EST (reconstructed) |
| Spread[] (deg) | `0.3, 0.36, 0.46, 0.59, 0.72, 0.87, 1.01, 1.14, 1.25` | EST (reconstructed): 0.3 -> 1.25 over 8 bullets (count EST) |
| HeatCap | 10 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6 / 3 / 0.8 / 10 | Wiki:Bulldog / PN 9.10 (global rifle) |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-50 m: 115.5 / 35 / 29.75 | API |

**Special mechanics / notes**

- ADS = **3-round burst** (valorant-api `adsStats.burstCount: 3`; Wiki: 13.333 rps inside a burst, 2.105 bursts/s). ADS first shot 0.1, ADS max **1.5** (above hip max 1.25, Wiki) - needs `AdsSpread[]`. Crouched burst spread x0.75 (Wiki). Burst recovery 0.35 s (PN 4.0). PN 4.07 input delay while raising ADS.
- Suggested ADS spread table (EST): `{ 0.1f, 0.25f, 0.5f, 0.8f, 1.1f, 1.3f, 1.5f }`.

```csharp
public static readonly WeaponDef Bulldog = new(
    WeaponKind.Bulldog, "Bulldog", Auto: true, Rps: 10f, AdsRpsMul: 0.6316f, Mag: 24, Reserve: 72, Reload: 2.5f, Equip: 1f,
    FirstShotHip: 0.3f, FirstShotAds: 0.1f, MaxSpread: 1.25f,
    RecoveryTime: 0.35f, TapEfficiency: 3, ProtectedBullets: 4, YawSwitchTime: 0.6f, Zoom: 1.25f,
    Pitch: Pad(new[] { 0f, 0.2f, 0.56f, 1.07f, 1.64f, 2.23f, 2.77f, 3.24f, 3.58f, 3.8f, 3.9f }, 24),
    Yaw: Pad(new[] { 0f, 0.02f, 0.04f, 0.06f, 0.17f, 0.34f, 0.55f, 0.77f, 0.98f, 1.19f, 1.4f, 1.57f, 1.74f, 1.87f, 1.96f, 2f }, 24),
    Spread: Pad(new[] { 0.3f, 0.36f, 0.46f, 0.59f, 0.72f, 0.87f, 1.01f, 1.14f, 1.25f }, 24), HeatCap: 10,
    RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
    DamageHead: new[] { 115.5f }, DamageBody: new[] { 35f }, DamageLegs: new[] { 29.75f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 2050,
    Penetration = Penetration.Medium,
    AltFire = AltFireKind.Burst,
    BurstCount = 3,
    BurstRps = 13.333f,
    BurstCycle = 0.475f /* 2.105 bursts/s */,
    AdsSpread = new[] { 0.1f, 0.25f, 0.5f, 0.8f, 1.1f, 1.3f, 1.5f },
    AltRecoveryTime = 0.35f,
    AltCrouchSpreadMul = 0.75f,
};
```

### Guardian

| Field | Value | Source |
|---|---|---|
| Cost (display) | 2250 credits | API |
| Wall penetration | High | API / PN 1.03 |
| Auto | no (semi) | API |
| Rps | 5.25 | API |
| AdsRpsMul | 1 | API / PN 4.0 (ADS penalty removed) |
| Mag / Reserve | 12 / 36 | mag: API; reserve: Wiki:Guardian |
| Reload (s) | 2.5 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 0.1 | API |
| FirstShotAds (deg) | 0.0 | Wiki:Guardian (API -1 = not applicable) |
| MaxSpread (deg) | 1.58 | Wiki:Guardian |
| Crouch spread mul | 0.85 | Wiki:Guardian Stats |
| RecoveryTime (s) | 0.35 | PN 0.50 (0.35); PN 1.08 "0.2925 after 3 bullets" |
| TapEfficiency | 3 | EST |
| ProtectedBullets | 3 | EST |
| YawSwitchTime (s) | 0.6 | EST |
| Zoom | 1.5 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.28, 0.81, 1.49, 2.15, 2.71, 3.05, 3.2` | EST (reconstructed): 3.2 deg over 7 shots |
| Yaw[] (deg, cumulative) | `0.0, 0.01, 0.01, 0.1, 0.22, 0.35, 0.47, 0.56, 0.6` | EST (reconstructed) |
| Spread[] (deg) | `0.1, 0.1, 0.1, 0.45, 0.85, 1.2, 1.45, 1.58` | EST (reconstructed): flat for 3 shots (PN 1.08 + PN 4.0 "extra bullet before it enters a recovery curve"), then to 1.58 |
| HeatCap | 7 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6 / 3 / 0.8 / 10 | Wiki:Guardian / PN 9.10 |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-50 m: 195 / 65 / 48.75 | API |

**Special mechanics / notes**

- Semi-auto. ADS first shot is a true 0 (Wiki); the controller clamps ADS error to >= 0.05 deg - fine for gameplay, but note it. ADS max 1.48 = hip max minus 0.1 offset, so the existing ADS rule fits.
- Leg multiplier is x0.75 (Wiki), not the usual 0.85 - already reflected in the 48.75 leg damage.

```csharp
public static readonly WeaponDef Guardian = new(
    WeaponKind.Guardian, "Guardian", Auto: false, Rps: 5.25f, AdsRpsMul: 1f, Mag: 12, Reserve: 36, Reload: 2.5f, Equip: 1f,
    FirstShotHip: 0.1f, FirstShotAds: 0f, MaxSpread: 1.58f,
    RecoveryTime: 0.35f, TapEfficiency: 3, ProtectedBullets: 3, YawSwitchTime: 0.6f, Zoom: 1.5f,
    Pitch: new[] { 0f, 0.28f, 0.81f, 1.49f, 2.15f, 2.71f, 3.05f, 3.2f },
    Yaw: new[] { 0f, 0.01f, 0.01f, 0.1f, 0.22f, 0.35f, 0.47f, 0.56f, 0.6f },
    Spread: new[] { 0.1f, 0.1f, 0.1f, 0.45f, 0.85f, 1.2f, 1.45f, 1.58f }, HeatCap: 7,
    RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
    DamageHead: new[] { 195f }, DamageBody: new[] { 65f }, DamageLegs: new[] { 48.75f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 2250,
    Penetration = Penetration.High,
};
```

### Phantom (existing - verified)

| Field | Value | Source |
|---|---|---|
| Cost (display) | 2900 credits | API |
| Wall penetration | Medium | API |
| Auto | yes | API |
| Rps | 11 | API |
| AdsRpsMul | 0.9 | API 9.9 / 11 |
| Mag / Reserve | 30 / 60 | mag: API; reserve: PN 6.11 |
| Reload (s) | 2.5 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 0.2 | API |
| FirstShotAds (deg) | 0.11 | API |
| MaxSpread (deg) | 0.9 | Wiki:Phantom |
| Crouch spread mul | 0.85 | Wiki:Phantom Stats |
| RecoveryTime (s) | 0.35 | PN 0.50 |
| TapEfficiency | 4 | PN 0.50 |
| ProtectedBullets | 8 | PN 11.08 |
| YawSwitchTime (s) | 0.6 | PN 11.08 |
| Zoom | 1.25 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.8 | PN 6.11 |
| Pitch[] (deg, cumulative) | `0, 0.15, 0.4, 0.75, 1.15, 1.57, 1.99, 2.39, 2.75, 3.05, 3.29, 3.46, 3.56, 3.6` | EST (reconstructed) (existing) |
| Yaw[] (deg, cumulative) | `0, 0.01, 0.03, 0.05, 0.07, 0.09, 0.11, 0.13, 0.25, 0.42, 0.6, 0.8, 1.0, 1.2, 1.38, 1.55, 1.7, 1.82, 1.92, 2.0` | EST (reconstructed) (existing) |
| Spread[] (deg) | `0.2, 0.23, 0.3, 0.38, 0.47, 0.56, 0.65, 0.73, 0.8, 0.86, 0.9` | EST (reconstructed) (existing) |
| HeatCap | 14 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6 / 3 / 0.8 / 10 | Wiki:Phantom / PN 9.10 |
| RunSpeed (m/s) | 5.4 | API |
| Damage head / body / legs | 0-20 m: 156 / 39 / 33<br>20-50 m: 140 / 35 / 29.75 | API / PN 9.10 (legs 33.15 -> 33) |

**Special mechanics / notes**

- All current values verified (valorant-api 13.09, Wiki, PN 11.08). Silenced. Minor: Wiki ADS max spread is 0.91, the controller's ADS rule gives 0.81.

```csharp
public static readonly WeaponDef Phantom = new(
    WeaponKind.Phantom, "Phantom", Auto: true, Rps: 11f, AdsRpsMul: 0.9f, Mag: 30, Reserve: 60, Reload: 2.5f, Equip: 1f,
    FirstShotHip: 0.2f, FirstShotAds: 0.11f, MaxSpread: 0.9f,
    RecoveryTime: 0.35f, TapEfficiency: 4, ProtectedBullets: 8, YawSwitchTime: 0.6f, Zoom: 1.25f,
    Pitch: Pad(new[] { 0f, 0.15f, 0.4f, 0.75f, 1.15f, 1.57f, 1.99f, 2.39f, 2.75f, 3.05f, 3.29f, 3.46f, 3.56f, 3.6f }, 30),
    Yaw: Pad(new[] { 0f, 0.01f, 0.03f, 0.05f, 0.07f, 0.09f, 0.11f, 0.13f, 0.25f, 0.42f, 0.6f, 0.8f, 1f, 1.2f, 1.38f, 1.55f, 1.7f, 1.82f, 1.92f, 2f }, 30),
    Spread: Pad(new[] { 0.2f, 0.23f, 0.3f, 0.38f, 0.47f, 0.56f, 0.65f, 0.73f, 0.8f, 0.86f, 0.9f }, 30), HeatCap: 14,
    RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
    DamageHead: new[] { 156f, 140f }, DamageBody: new[] { 39f, 35f }, DamageLegs: new[] { 33f, 29.75f }, DamageRangeEnd: new[] { 20f, 999f })
{
    Cost = 2900,
    Penetration = Penetration.Medium,
    Silenced = true,
};
```

### Vandal (existing - verified)

| Field | Value | Source |
|---|---|---|
| Cost (display) | 2900 credits | API |
| Wall penetration | Medium | API |
| Auto | yes | API |
| Rps | 9.75 | API |
| AdsRpsMul | 0.9 | API 8.775 / 9.75 |
| Mag / Reserve | 25 / 50 | mag: API; reserve: PN 6.11 |
| Reload (s) | 2.5 | API |
| Equip (s) | 1.0 | API |
| FirstShotHip (deg) | 0.25 | API |
| FirstShotAds (deg) | 0.157 | API 0.1575 |
| MaxSpread (deg) | 1.0 | Wiki:Vandal |
| Crouch spread mul | 0.85 | Wiki:Vandal Stats |
| RecoveryTime (s) | 0.375 | PN 0.50 |
| TapEfficiency | 6 | PN 0.50 |
| ProtectedBullets | 6 | PN 11.08 |
| YawSwitchTime (s) | 0.6 | PN 11.08 |
| Zoom | 1.25 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.8 | PN 6.11 |
| Pitch[] (deg, cumulative) | `0, 0.2, 0.55, 1.05, 1.65, 2.27, 2.87, 3.42, 3.87, 4.2, 4.4, 4.5` | EST (reconstructed) (existing) |
| Yaw[] (deg, cumulative) | `0, 0.02, 0.05, 0.08, 0.1, 0.12, 0.25, 0.45, 0.7, 0.95, 1.2, 1.45, 1.7, 1.9, 2.1, 2.25, 2.35, 2.4` | EST (reconstructed) (existing) |
| Spread[] (deg) | `0.25, 0.3, 0.4, 0.52, 0.65, 0.78, 0.9, 1.0` | EST (reconstructed) (existing) |
| HeatCap | 12 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6 / 3 / 0.8 / 10 | Wiki:Vandal / PN 9.10 |
| RunSpeed (m/s) | 5.4 | API |
| Damage head / body / legs | 0-50 m: 160 / 40 / 34 | API |

**Special mechanics / notes**

- All current values verified. Minor: Wiki ADS max spread is 1.02, the controller's ADS rule gives ~0.91. Crouched + stationary yaw x0.85 (PN 0.50) matches the controller.

```csharp
public static readonly WeaponDef Vandal = new(
    WeaponKind.Vandal, "Vandal", Auto: true, Rps: 9.75f, AdsRpsMul: 0.9f, Mag: 25, Reserve: 50, Reload: 2.5f, Equip: 1f,
    FirstShotHip: 0.25f, FirstShotAds: 0.157f, MaxSpread: 1f,
    RecoveryTime: 0.375f, TapEfficiency: 6, ProtectedBullets: 6, YawSwitchTime: 0.6f, Zoom: 1.25f,
    Pitch: Pad(new[] { 0f, 0.2f, 0.55f, 1.05f, 1.65f, 2.27f, 2.87f, 3.42f, 3.87f, 4.2f, 4.4f, 4.5f }, 25),
    Yaw: Pad(new[] { 0f, 0.02f, 0.05f, 0.08f, 0.1f, 0.12f, 0.25f, 0.45f, 0.7f, 0.95f, 1.2f, 1.45f, 1.7f, 1.9f, 2.1f, 2.25f, 2.35f, 2.4f }, 25),
    Spread: Pad(new[] { 0.25f, 0.3f, 0.4f, 0.52f, 0.65f, 0.78f, 0.9f, 1f }, 25), HeatCap: 12,
    RunErr: 6f, WalkErr: 3f, CrouchWalkErr: 0.8f, AirErr: 10f, RunSpeed: 5.4f,
    DamageHead: new[] { 160f }, DamageBody: new[] { 40f }, DamageLegs: new[] { 34f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 2900,
    Penetration = Penetration.Medium,
};
```

### Marshal

| Field | Value | Source |
|---|---|---|
| Cost (display) | 950 credits | API |
| Wall penetration | Medium | API |
| Auto | no (semi) | API |
| Rps | 1.5 | API |
| AdsRpsMul | 0.8 | API 1.2 / 1.5 |
| Mag / Reserve | 5 / 15 | mag: API; reserve: Wiki:Marshal |
| Reload (s) | 2.5 | API (Wiki: 0.5 s per bullet) |
| Equip (s) | 1.25 | API |
| FirstShotHip (deg) | 1.0 | API |
| FirstShotAds (deg) | 0.0 | Wiki:Marshal (0 scoped) |
| MaxSpread (deg) | 1.0 | Wiki:Marshal |
| Crouch spread mul | 0.9 | Wiki:Marshal Stats |
| RecoveryTime (s) | 0.65 | EST |
| TapEfficiency | 1 | n/a |
| ProtectedBullets | 1 | n/a |
| YawSwitchTime (s) | 0.6 | n/a |
| Zoom | 3.5 | API / PN 2.03 |
| ADS/scoped move mul | 0.9 | API adsStats.runSpeedMultiplier |
| Pitch[] (deg, cumulative) | `0.0` | as Operator (no per-shot table) |
| Yaw[] (deg, cumulative) | `0.0` | - |
| Spread[] (deg) | `1.0` | Wiki:Marshal |
| HeatCap | 1 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 15 / 10 / 7.5 / 20 | Wiki:Marshal spread table |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-50 m: 202 / 101 / 85.85 | API |

**Special mechanics / notes**

- Scoped move speed **x0.90** (valorant-api `adsStats.runSpeedMultiplier 0.9`, PN 2.03) = 4.86 m/s. Single zoom 3.5x.
- Scoped fire rate 1.2 rps (x0.8); the controller skips `AdsRpsMul` for snipers (`Scoped && !Def.Sniper`), so that rule needs to apply to the Marshal.
- Lever action, unscopes after each shot like the Operator (EST from in-game behaviour; the Wiki logs "auto re-scope" fixes for it, PN 6.01/9.03). Reloads one bullet at a time, 0.5 s each (Wiki).
- Hip-fire is only 1.0 deg (much tighter than other snipers) - the Sniper path in the controller uses `FirstShotHip` unscoped, which is right.

```csharp
public static readonly WeaponDef Marshal = new(
    WeaponKind.Marshal, "Marshal", Auto: false, Rps: 1.5f, AdsRpsMul: 0.8f, Mag: 5, Reserve: 15, Reload: 2.5f, Equip: 1.25f,
    FirstShotHip: 1f, FirstShotAds: 0f, MaxSpread: 1f,
    RecoveryTime: 0.65f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 3.5f,
    Pitch: new[] { 0f },
    Yaw: new[] { 0f },
    Spread: new[] { 1f }, HeatCap: 1,
    RunErr: 15f, WalkErr: 10f, CrouchWalkErr: 7.5f, AirErr: 20f, RunSpeed: 5.4f,
    DamageHead: new[] { 202f }, DamageBody: new[] { 101f }, DamageLegs: new[] { 85.85f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 950,
    Penetration = Penetration.Medium,
    CrouchSpreadMul = 0.9f,
    AdsMoveMul = 0.9f,
    IsSniper = true,
    UnscopeAfterShot = true,
    ReloadPerRound = 0.5f,
};
```

### Outlaw

| Field | Value | Source |
|---|---|---|
| Cost (display) | 2400 credits | API |
| Wall penetration | High | API |
| Auto | no (semi) | API |
| Rps | 2.75 | API |
| AdsRpsMul | 1 | API |
| Mag / Reserve | 2 / 10 | mag: API; reserve: Wiki:Outlaw |
| Reload (s) | 3.8 | API (empty; 2.3 s with one round left, Wiki/PN 8.0) |
| Equip (s) | 1.25 | API |
| FirstShotHip (deg) | 3.5 | API |
| FirstShotAds (deg) | 0.0 | Wiki:Outlaw |
| MaxSpread (deg) | 3.5 | Wiki:Outlaw |
| Crouch spread mul | 0.9 | Wiki:Outlaw Stats |
| RecoveryTime (s) | 0.51 | PN 13.01 says 0.15 s; 0.51 = interval + 0.15 (EST, see notes) |
| TapEfficiency | 1 | n/a |
| ProtectedBullets | 1 | n/a |
| YawSwitchTime (s) | 0.6 | n/a |
| Zoom | 3.5 | API |
| ADS/scoped move mul | 0.8 | API adsStats.runSpeedMultiplier |
| Pitch[] (deg, cumulative) | `0.0, 4.0` | PN 13.01: recoil 0 -> 4.0 after the first shot |
| Yaw[] (deg, cumulative) | `0.0, 0.0` | - |
| Spread[] (deg) | `3.5, 3.5` | Wiki:Outlaw (hip) |
| HeatCap | 1 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 15 / 10 / 7.5 / 20 | Wiki:Outlaw spread table |
| RunSpeed (m/s) | 5.4 | API 0.8 x 6.75 |
| Damage head / body / legs | 0-50 m: 238 / 140 / 119 | API |

**Special mechanics / notes**

- **Double-shot, stays zoomed** (PN 8.0 "Stay Zoomed", 2 rounds): both barrels can be fired back to back (2.75 rps) without unscoping. Scoped move speed x0.80 (valorant-api).
- PN 13.01 nerf: after the first shot, **spread 0 -> 2.25 deg** (scoped) and **recoil 0 -> 4.0**, recovery 0.1 -> 0.15 s. The controller forces 0 error whenever a sniper is scoped, so the second shot needs a `ScopedSpread[]` table: `{ 0f, 2.25f }`.
- Recovery caveat: with this controller's model a 0.15 s recovery fully resets before the earliest second shot (0.364 s), which would erase the nerf. Riot's 0.15 is most likely measured after the shot interval (recovery delay). Proposed: add `RecoveryDelay` (= interval) or use the effective 0.51 s shown in the code below (EST).
- Two reload lengths: 3.8 s from empty, 2.3 s with one round left (Wiki, PN 8.0) - proposed `ReloadPartial`.

```csharp
public static readonly WeaponDef Outlaw = new(
    WeaponKind.Outlaw, "Outlaw", Auto: false, Rps: 2.75f, AdsRpsMul: 1f, Mag: 2, Reserve: 10, Reload: 3.8f, Equip: 1.25f,
    FirstShotHip: 3.5f, FirstShotAds: 0f, MaxSpread: 3.5f,
    RecoveryTime: 0.51f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 3.5f,
    Pitch: new[] { 0f, 4f },
    Yaw: new[] { 0f, 0f },
    Spread: new[] { 3.5f, 3.5f }, HeatCap: 1,
    RunErr: 15f, WalkErr: 10f, CrouchWalkErr: 7.5f, AirErr: 20f, RunSpeed: 5.4f,
    DamageHead: new[] { 238f }, DamageBody: new[] { 140f }, DamageLegs: new[] { 119f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 2400,
    Penetration = Penetration.High,
    CrouchSpreadMul = 0.9f,
    AdsMoveMul = 0.8f,
    IsSniper = true,
    UnscopeAfterShot = false,
    ScopedSpread = new[] { 0f, 2.25f },
    RecoveryDelay = 0.364f /* if added; then RecoveryTime = 0.15f */,
    ReloadPartial = 2.3f,
};
```

### Operator (existing - verified)

| Field | Value | Source |
|---|---|---|
| Cost (display) | 4700 credits | API |
| Wall penetration | High | API |
| Auto | no (semi) | API / PN 1.09 |
| Rps | 0.6 | API / PN 1.09 |
| AdsRpsMul | 1 | API |
| Mag / Reserve | 5 / 10 | mag: API; reserve: Wiki:Operator |
| Reload (s) | 3.7 | API |
| Equip (s) | 1.5 | API |
| FirstShotHip (deg) | 5.0 | API |
| FirstShotAds (deg) | 0.0 | Wiki:Operator |
| MaxSpread (deg) | 5.0 | Wiki:Operator |
| Crouch spread mul | 0.9 | Wiki:Operator Stats |
| RecoveryTime (s) | 1.6 | EST (existing) |
| TapEfficiency | 1 | n/a |
| ProtectedBullets | 1 | n/a |
| YawSwitchTime (s) | 0.6 | n/a |
| Zoom | 2.5 | API (2.5x; second level 5x, Wiki/feature DualZoom) |
| ADS/scoped move mul | 0.72 | API adsStats.runSpeedMultiplier |
| Pitch[] (deg, cumulative) | `0.0` | - |
| Yaw[] (deg, cumulative) | `0.0` | - |
| Spread[] (deg) | `5.0` | Wiki:Operator |
| HeatCap | 1 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 15 / 10 / 7.5 / 15 | Wiki:Operator spread table (CHANGED vs Weapons.cs) |
| RunSpeed (m/s) | 5.13 | API 0.76 x 6.75 |
| Damage head / body / legs | 0-50 m: 255 / 150 / 120 | API / PN 1.09 |

**Special mechanics / notes**

- **Flag - movement error is wrong in Weapons.cs**: code has run 10 / walk 6 / crouch-walk 3 / air 15; the Wiki spread table gives **run 15 / walk 10 / crouch-walk 7.5 / air 15** (the same numbers Weapons.cs already uses for Tour De Force). Snipers share 7.5/10/15 (Marshal/Outlaw air 20).
- Dual zoom 2.5x / 5x (valorant-api `feature: DualZoom`, Wiki) - only 2.5x is modelled. Scoped move x0.72 (valorant-api, PN 1.09) matches.
- Crouch spread x0.9 (Wiki: crouched hip 4.5 deg); the controller applies 0.85 (gives 4.25).
- Accuracy deadzone: PN 1.09 made the Operator inaccurate above 15% of max speed (vs the general deadzone the controller uses, 27.5%). Whether PN 3.0's global deadzone change overrode this is unclear (EST: keep 0.15 for Operator).

```csharp
public static readonly WeaponDef Operator = new(
    WeaponKind.Operator, "Operator", Auto: false, Rps: 0.6f, AdsRpsMul: 1f, Mag: 5, Reserve: 10, Reload: 3.7f, Equip: 1.5f,
    FirstShotHip: 5f, FirstShotAds: 0f, MaxSpread: 5f,
    RecoveryTime: 1.6f, TapEfficiency: 1, ProtectedBullets: 1, YawSwitchTime: 0.6f, Zoom: 2.5f,
    Pitch: new[] { 0f },
    Yaw: new[] { 0f },
    Spread: new[] { 5f }, HeatCap: 1,
    RunErr: 15f, WalkErr: 10f, CrouchWalkErr: 7.5f, AirErr: 15f, RunSpeed: 5.13f,
    DamageHead: new[] { 255f }, DamageBody: new[] { 150f }, DamageLegs: new[] { 120f }, DamageRangeEnd: new[] { 999f })
{
    Cost = 4700,
    Penetration = Penetration.High,
    CrouchSpreadMul = 0.9f,
    AdsMoveMul = 0.72f,
    IsSniper = true,
    UnscopeAfterShot = true,
    Zoom2 = 5f,
    DeadzoneMul = 0.15f,
};
```

### Ares

| Field | Value | Source |
|---|---|---|
| Cost (display) | 1600 credits | API |
| Wall penetration | High | API |
| Auto | yes | API / PN 4.0 (spin-up removed, 10 -> 13) |
| Rps | 13 | API / PN 4.0 (spin-up removed, 10 -> 13) |
| AdsRpsMul | 1 | API |
| Mag / Reserve | 50 / 100 | mag: API; reserve: Wiki:Ares |
| Reload (s) | 3.25 | API |
| Equip (s) | 1.25 | API |
| FirstShotHip (deg) | 1.0 | API / PN 4.01 |
| FirstShotAds (deg) | 0.9 | API |
| MaxSpread (deg) | 1.0 | Wiki:Ares (largest error is the first shot) |
| Crouch spread mul | 0.6 | Wiki:Ares Stats |
| RecoveryTime (s) | 0.5 | EST |
| TapEfficiency | 2 | EST |
| ProtectedBullets | 8 | EST (PN 0.50: yaw reduced "after the first 8 bullets") |
| YawSwitchTime (s) | 0.5 | EST |
| Zoom | 1.15 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.17, 0.46, 0.87, 1.35, 1.83, 2.28, 2.66, 2.94, 3.11, 3.2` | EST (reconstructed): 3.2 deg over 10 bullets (PN 4.01 "pitch recoil increased") |
| Yaw[] (deg, cumulative) | `0.0, 0.02, 0.04, 0.07, 0.09, 0.11, 0.11, 0.11, 0.23, 0.41, 0.64, 0.87, 1.1, 1.33, 1.56, 1.74, 1.93, 2.06, 2.15, 2.2` | EST (reconstructed) |
| Spread[] (deg) | `1.0, 0.98, 0.95, 0.93, 0.9, 0.88, 0.85, 0.83, 0.8, 0.78, 0.76, 0.74, 0.72, 0.7` | PN 4.01: 1.0 down to 0.7 after 13 bullets (linear EST in between) |
| HeatCap | 13 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6.5 / 3 / 0.4 / 10 | Wiki:Ares / PN 9.10 (MG: walk 3, run 6.5) |
| RunSpeed (m/s) | 5.13 | API 0.76 x 6.75 |
| Damage head / body / legs | 0-30 m: 75 / 30 / 25.5<br>30-50 m: 70 / 28 / 23.8 | API / PN 9.05 (HS x2.5) |

**Special mechanics / notes**

- **Reverse spread**: the Ares gets *more* accurate while held (1.0 -> 0.7 hip, 0.9 -> 0.55 ADS, Wiki/PN 4.01). Use the Spread table as-is; `MaxSpread` = 1.0 because that is the largest value (the crosshair bloom rule `SpreadNow - FirstShotHip` would go negative - clamp at 0).
- No spin-up since PN 4.0. ADS fires at the same 13 rps (valorant-api).
- Crouch benefit 40%: spread **and recoil** x0.6 (PN 9.05, Wiki). The controller uses 0.85.

```csharp
public static readonly WeaponDef Ares = new(
    WeaponKind.Ares, "Ares", Auto: true, Rps: 13f, AdsRpsMul: 1f, Mag: 50, Reserve: 100, Reload: 3.25f, Equip: 1.25f,
    FirstShotHip: 1f, FirstShotAds: 0.9f, MaxSpread: 1f,
    RecoveryTime: 0.5f, TapEfficiency: 2, ProtectedBullets: 8, YawSwitchTime: 0.5f, Zoom: 1.15f,
    Pitch: Pad(new[] { 0f, 0.17f, 0.46f, 0.87f, 1.35f, 1.83f, 2.28f, 2.66f, 2.94f, 3.11f, 3.2f }, 50),
    Yaw: Pad(new[] { 0f, 0.02f, 0.04f, 0.07f, 0.09f, 0.11f, 0.11f, 0.11f, 0.23f, 0.41f, 0.64f, 0.87f, 1.1f, 1.33f, 1.56f, 1.74f, 1.93f, 2.06f, 2.15f, 2.2f }, 50),
    Spread: Pad(new[] { 1f, 0.98f, 0.95f, 0.93f, 0.9f, 0.88f, 0.85f, 0.83f, 0.8f, 0.78f, 0.76f, 0.74f, 0.72f, 0.7f }, 50), HeatCap: 13,
    RunErr: 6.5f, WalkErr: 3f, CrouchWalkErr: 0.4f, AirErr: 10f, RunSpeed: 5.13f,
    DamageHead: new[] { 75f, 70f }, DamageBody: new[] { 30f, 28f }, DamageLegs: new[] { 25.5f, 23.8f }, DamageRangeEnd: new[] { 30f, 999f })
{
    Cost = 1600,
    Penetration = Penetration.High,
    CrouchSpreadMul = 0.6f,
    CrouchRecoilMul = 0.6f,
};
```

### Odin

| Field | Value | Source |
|---|---|---|
| Cost (display) | 3200 credits | API |
| Wall penetration | High | API |
| Auto | yes | API (12 at the start of a spray) |
| Rps | 12 | API (12 at the start of a spray) |
| AdsRpsMul | 1.3 | API 15.6 / 12 |
| Mag / Reserve | 100 / 200 | mag: API; reserve: Wiki:Odin |
| Reload (s) | 5.0 | API |
| Equip (s) | 1.25 | API |
| FirstShotHip (deg) | 0.8 | API |
| FirstShotAds (deg) | 0.79 | API |
| MaxSpread (deg) | 1.3 | Wiki:Odin |
| Crouch spread mul | 0.6 | Wiki:Odin Stats |
| RecoveryTime (s) | 0.5 | EST |
| TapEfficiency | 2 | EST |
| ProtectedBullets | 8 | EST (PN 0.50: yaw reduced "after the first 8 bullets") |
| YawSwitchTime (s) | 0.5 | EST |
| Zoom | 1.15 | API |
| ADS/scoped move mul | 0.76 | API adsStats.runSpeedMultiplier |
| Run vertical-recoil mul | 1.5 | EST (pre-6.11 default 1.5) |
| Pitch[] (deg, cumulative) | `0.0, 0.15, 0.42, 0.78, 1.22, 1.7, 2.17, 2.62, 3.01, 3.34, 3.57, 3.72, 3.8` | EST (reconstructed): 3.8 deg over 12 bullets |
| Yaw[] (deg, cumulative) | `0.0, 0.02, 0.05, 0.07, 0.1, 0.12, 0.12, 0.12, 0.25, 0.45, 0.7, 0.95, 1.2, 1.45, 1.7, 1.9, 2.1, 2.25, 2.35, 2.4` | EST (reconstructed) |
| Spread[] (deg) | `0.8, 0.82, 0.86, 0.91, 0.96, 1.02, 1.08, 1.14, 1.2, 1.25, 1.3` | EST (reconstructed): 0.8 -> 1.3 (Wiki) over 10 bullets (count EST) |
| HeatCap | 12 | EST (bullets until the tables plateau) |
| Move error run / walk / crouch-walk / air | 6.5 / 3 / 0.4 / 10 | Wiki:Odin / PN 9.10 |
| RunSpeed (m/s) | 5.13 | API 0.76 x 6.75 |
| Damage head / body / legs | 0-30 m: 95 / 38 / 32.3<br>30-50 m: 77.5 / 31 / 26.35 | API |

**Special mechanics / notes**

- **Fire-rate ramp** (valorant-api `feature: ROFIncrease`): hip fire starts at 12 rps and ramps to **15.6 rps** while held; ADS fires at 15.6 from the first bullet (Wiki infobox "Starts firing at maximum fire rate", valorant-api adsStats.fireRate 15.6). Ramp duration is **not published**: EST linear over ~1.0 s (~13 bullets). Proposed `SpinUpTime = 1.0f`, `SpinUpStartMul = 12/15.6`, with `AdsSkipsSpinUp = true`.
- ADS max spread 1.36 (Wiki) is slightly above hip max 1.3; the existing ADS rule (hip minus 0.01) is close enough.
- Crouch benefit x0.6 spread and recoil (Wiki).

```csharp
public static readonly WeaponDef Odin = new(
    WeaponKind.Odin, "Odin", Auto: true, Rps: 12f, AdsRpsMul: 1.3f, Mag: 100, Reserve: 200, Reload: 5f, Equip: 1.25f,
    FirstShotHip: 0.8f, FirstShotAds: 0.79f, MaxSpread: 1.3f,
    RecoveryTime: 0.5f, TapEfficiency: 2, ProtectedBullets: 8, YawSwitchTime: 0.5f, Zoom: 1.15f,
    Pitch: Pad(new[] { 0f, 0.15f, 0.42f, 0.78f, 1.22f, 1.7f, 2.17f, 2.62f, 3.01f, 3.34f, 3.57f, 3.72f, 3.8f }, 100),
    Yaw: Pad(new[] { 0f, 0.02f, 0.05f, 0.07f, 0.1f, 0.12f, 0.12f, 0.12f, 0.25f, 0.45f, 0.7f, 0.95f, 1.2f, 1.45f, 1.7f, 1.9f, 2.1f, 2.25f, 2.35f, 2.4f }, 100),
    Spread: Pad(new[] { 0.8f, 0.82f, 0.86f, 0.91f, 0.96f, 1.02f, 1.08f, 1.14f, 1.2f, 1.25f, 1.3f }, 100), HeatCap: 12,
    RunErr: 6.5f, WalkErr: 3f, CrouchWalkErr: 0.4f, AirErr: 10f, RunSpeed: 5.13f,
    DamageHead: new[] { 95f, 77.5f }, DamageBody: new[] { 38f, 31f }, DamageLegs: new[] { 32.3f, 26.35f }, DamageRangeEnd: new[] { 30f, 999f })
{
    Cost = 3200,
    Penetration = Penetration.High,
    CrouchSpreadMul = 0.6f,
    SpinUpTime = 1.0f /* EST */,
    SpinUpStartMul = 0.769f /* 12 / 15.6 */,
    AdsSkipsSpinUp = true,
    CrouchRecoilMul = 0.6f,
};
```


## 5. Checking the 4 existing guns against 13.09

| Gun | Verdict | Details |
|---|---|---|
| Vandal | **OK** | Every field matches API 13.09, the Wiki and PN 11.08 (protected 6, yaw switch 0.6 s / 10%). Minor: the Wiki ADS max spread is 1.02; the controller's ADS rule yields ~0.91. |
| Phantom | **OK** | Matches (damage 156/39/33 to 20 m, then 140/35/29.75; protected 8; reserve 60). Minor: ADS max 0.91 vs ~0.81. |
| Sheriff | **OK (data)** | Matches API/Wiki. Gap: crouch spread multiplier is **x0.75** (Wiki); the controller uses 0.85 for every gun. Recovery 0.4 / TE 3 remain estimates. |
| Operator | **Fix movement error** | Code: run 10 / walk 6 / crouch-walk 3 / air 15. Wiki spread table: **run 15 / walk 10 / crouch-walk 7.5 / air 15** (the same values Weapons.cs already uses for Tour De Force). Also not modelled: 5x second zoom, crouch x0.9 (hip 4.5 deg crouched), and the 15% movement deadzone (PN 1.09). Everything else matches (0.6 rps, 5+10, 3.7 s reload, 1.5 s equip, 255/150/120, scoped move 0.72, run 5.13). |

Also worth fixing globally: the running vertical-recoil multiplier is hard-coded at 1.8. Riot documents 1.8 only for Vandal, Phantom and Spectre, and 1.5 for Frenzy (PN 6.11).

## 6. Estimated vs sourced

**Sourced (API 13.09 / Wiki game-file tables / patch notes) for all 18 guns:** cost, fire mode, fire rate, ADS fire rate (so `AdsRpsMul`), magazine, reserve, reload, equip, first-shot error (hip and ADS), max spread, crouch spread multiplier, zoom, ADS move multiplier, movement error (run/walk/crouch-walk/air), run speed, wall penetration, damage per zone per range band, pellet counts, Classic alt-fire pellets / rate / spread steps / movement error, Bucky air-burst pellets / distance / cone, Stinger and Bulldog burst counts and in-burst rates, Odin start/max fire rate, Outlaw second-shot spread / recoil / recovery, Marshal/Outlaw/Operator scope levels.

**Sourced for some guns only:**
- RecoveryTime: Vandal 0.375, Phantom 0.35, Bulldog 0.35 (auto and burst), Guardian 0.35, Stinger burst 0.4, Outlaw 0.15 (see caveat), Bandit 0.4.
- TapEfficiency: Vandal 6, Phantom 4, Spectre 3, Bandit 4.
- ProtectedBullets: Vandal 6, Phantom 8, Spectre 5.
- YawSwitchTime: Vandal / Phantom / Bulldog 0.6 s, Spectre 0.28 s.
- Yaw swap chance: 10% for Vandal / Phantom / Bulldog.
- Running pitch multiplier: Vandal / Phantom / Spectre 1.8, Frenzy 1.5.

**Estimated (EST):**
- Every `Pitch[]`, `Yaw[]` and `Spread[]` table beyond the first/max endpoints, for every gun. The Vandal, Phantom and Sheriff tables are also reconstructions, kept from Weapons.cs.
- RecoveryTime / TapEfficiency / ProtectedBullets / YawSwitchTime / HeatCap where not listed above.
- Stinger and Bulldog ADS burst spread and pitch tables (endpoints sourced).
- Odin spin-up duration (1.0 s).
- Bucky canister direct-hit damage after PN 12.09 (assumed 34/17/14).
- Classic ammo per alt blast (3).
- Marshal unscoping after each shot.
- Outlaw effective recovery (0.51 s).
- Run vertical-recoil multiplier for guns not named in PN 6.11 (1.5).
- Operator deadzone still being 15% after PN 3.0.

**Known stale Wiki values (corrected here from the API and patch notes):** Judge magazine (7 -> 5, PN 7.09). Bucky min spread (2.6 -> 3.0) and shotgun movement errors (Bucky run 0.2, Judge run 1 -> both 2, PN 12.09). Shotgun crouch multipliers (-> 0.85, PN 12.09). Bandit run speed (Wiki 5.4 vs API 0.85 = 5.73).

## Main uncertainties

1. **Per-bullet recoil**: all magnitudes beyond Vandal/Phantom are estimates. They are worth eyeballing against in-game wall sprays in the Range before shipping.
2. **Odin ramp duration**: not published anywhere I could find. Riot only says the rate "increases over time" from 12 to 15.6.
3. **Outlaw 13.01 recovery**: in this controller's model, Riot's "0.15 s" would reset before the second barrel can fire, so it is probably a post-interval delay. I modelled it as an effective 0.51 s.
4. **Recovery/tap efficiency** for sidearms, SMGs (except the Spectre's TE), shotguns and heavies are guesses.
5. **Bucky canister** damage after 12.09, and whether the Classic alt-fire's ~6 deg third-blast cone (PN 2.0) or the Wiki's 5.78 is current.

## Appendix: guns outside the 18

- **Bandit** (Sidearm, 600 credits, Medium penetration, PN 12.00 (13.00 tuning)): Semi-auto, 5.1 rps, 8 rounds (+24), reload 1.5 s, equip 0.75 s, first shot 0.275 deg, max 1.97 (crouched 0.21/1.48), movement crouch-walk 0.5 / walk 1.2 / run 2.7 / air 7, crouch x0.75. Damage head 152 / 128 / 112, body 39 / 39 / 34, legs 33 / 33 / 28 at 0-10 / 10-30 / 30-50 m (headshot x3.9 inside 10 m). PN 13.00: recovery 0.45 -> **0.4**, tap efficiency 3 -> **4**, pitch recoil at max 4 -> **3** (one of the few published recoil magnitudes). Run speed: valorant-api runSpeedMultiplier 0.85 (5.73 m/s) vs Wiki 5.4 - prefer the API.
- **Warden** (Rifle, 2900 credits, Medium penetration, PN 13.06 (13.09 buffs)): Full-auto scoped rifle: 6.5 rps (ADS same), 18 rounds (+36), reload 2.5 s, equip 1.0 s, 50 body / 200 head / 42 legs at every range, 2x scope, first shot 0.15 hip / 0.04 ADS, run multiplier 0.8 (5.4 m/s), ADS move x0.76 (valorant-api; PN 13.06). PN 13.09: "pitch recoil smoothed out for the first few bullets", "reduced spread on follow-up shots" (no numbers). No Wiki page yet.
