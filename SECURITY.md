# Security policy

## Supported versions

Only the [latest release](https://github.com/idoraz1/ValTrainer/releases/latest) gets fixes. Please update before
reporting.

## Reporting a problem

Please **don't open a public issue** for security problems. Report them privately on GitHub:
**Security → Report a vulnerability** (<https://github.com/idoraz1/ValTrainer/security/advisories/new>).

Include the ValTrainer version (Settings → About), what an attacker could do, and the steps to reproduce it. You'll get
an answer within a week, and credit in the release notes if you want it.

## What's in scope

- The ValTrainer app: reading VALORANT's config files, saving settings, stats and recordings in `%APPDATA%\ValTrainer`,
  the update check (it only contacts `api.github.com` and only opens `https://github.com/` links) and the automatic
  updater (it only downloads this project's GitHub release files, and only runs a file whose SHA-256 matches the
  release's `SHA256SUMS.txt`).
- The installer, the portable zip and the release process (checksums in `SHA256SUMS.txt`).

ValTrainer never interacts with the VALORANT process or Riot's anti-cheat, so problems in VALORANT or Riot Vanguard
themselves are out of scope; report those to Riot.
