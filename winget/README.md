# winget package (Valtrainer.ValTrainer)

`manifests/v/Valtrainer/ValTrainer/<version>/` holds the manifest set for the
[Windows Package Manager community repository](https://github.com/microsoft/winget-pkgs)
(schema 1.12.0, the same folder layout winget-pkgs uses). Once it is merged there, users install with:

```
winget install Valtrainer.ValTrainer          # per-user, no admin prompt (/CURRENTUSER)
winget install Valtrainer.ValTrainer --scope machine   # all users, Setup asks for admin itself (/ALLUSERS)
```

## The manifests at a glance

- **Installer:** the release's `ValTrainer-<version>-Setup.exe` (Inno Setup, `InstallerType: inno`), with `InstallerSha256` taken from the release's
  `SHA256SUMS.txt`. The same file is listed twice:
  - `Scope: user` with the custom switch `/CURRENTUSER`;
  - `Scope: machine` with `/ALLUSERS` and `ElevationRequirement: elevatesSelf`.
- **ProductCode:** `{8BC1A72E-9D6C-4649-AE20-032F14D7A1F5}_is1`. Inno names the uninstall key after the AppId in
  `installer\ValTrainer.iss` plus `_is1`; the `{{` in `AppId={{...}` is only Inno's escape for a literal `{`. Never change the AppId.
- **UpgradeBehavior:** `install` (Inno upgrades in place over the previous version).

## Check before submitting

```
winget validate --manifest winget\manifests\v\Valtrainer\ValTrainer\<version>
```

If `winget` is not on PATH, run `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`. `PrivacyUrl` points at
https://valtrainer.github.io/code-signing-policy.html. That page must be live before you submit, because the winget-pkgs
pipeline checks every URL.

## First submission (once)

Install [wingetcreate](https://github.com/microsoft/winget-create) (`winget install Microsoft.WingetCreate`, or the
release's `wingetcreate.exe`). Create a GitHub token with `public_repo` scope; wingetcreate uses it to fork
microsoft/winget-pkgs and open the pull request. Then:

```
wingetcreate submit --token <token> winget\manifests\v\Valtrainer\ValTrainer\1.4.0
```

A Microsoft moderator reviews the pull request, and automated validation installs the package in a sandbox. Answer any
bot comments on the PR. Once it's merged, `winget install Valtrainer.ValTrainer` works.

## Every release after that

After CI has published `vX.Y.Z` (Setup.exe and SHA256SUMS.txt are on the release), run:

```
wingetcreate update Valtrainer.ValTrainer --version X.Y.Z ^
  --urls "https://github.com/idoraz1/ValTrainer/releases/download/vX.Y.Z/ValTrainer-X.Y.Z-Setup.exe|x64|user" ^
         "https://github.com/idoraz1/ValTrainer/releases/download/vX.Y.Z/ValTrainer-X.Y.Z-Setup.exe|x64|machine" ^
  --release-notes-url https://github.com/idoraz1/ValTrainer/releases/tag/vX.Y.Z ^
  --token <token> --submit
```

The `|x64|user` and `|x64|machine` overrides map the one Setup.exe onto both installer entries. wingetcreate
downloads the file, computes the SHA-256 and keeps the switches, ProductCode and scopes from the previous version.
Check that the hash it writes matches `SHA256SUMS.txt`.

To keep a copy here as well, run without `--submit` and with `--out winget` (wingetcreate creates `manifests\v\...` under it). Then validate the result and
submit it with `wingetcreate submit --token <token> <dir>`. Delete the old version's folder here at that point; only
the newest version needs to be kept in this repository.
