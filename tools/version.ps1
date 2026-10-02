<#
.SYNOPSIS
  ValTrainer's version: read it, bump it, or print a version's release notes.

.DESCRIPTION
  The single source of truth is application/config/version in ValTrainerGodot\project.godot
  (SemVer x.y.z, optionally -prerelease such as 1.1.0-beta.1). The exe's file/product version, the installer and
  the in-game "v1.0.0" all come from it.

  -Bump / -Set also release the changelog (Keep a Changelog 1.1.0, CHANGELOG.md in the repo root):
  "## [Unreleased]" becomes "## [x.y.z] - YYYY-MM-DD", a fresh empty "## [Unreleased]" goes above it, and the
  compare links at the bottom ([Unreleased]: .../compare/vOLD...HEAD) are updated when present.

.EXAMPLE
  tools\version.ps1                      # prints 1.0.0
  tools\version.ps1 -Bump patch          # 1.0.0 -> 1.0.1 (project.godot + CHANGELOG.md), prints 1.0.1
  tools\version.ps1 -Set 1.1.0-beta.1
  tools\version.ps1 -Notes 1.0.0         # that version's CHANGELOG section (GitHub release notes)
  tools\version.ps1 -Notes v1.0.0 -OutFile notes.md

.NOTES
  Bumping a prerelease drops the prerelease tag the way npm does (1.1.0-beta.1 -Bump minor -> 1.1.0).
  Works in Windows PowerShell 5.1 and PowerShell 7. Exit code 1 on any error.
#>
[CmdletBinding(DefaultParameterSetName = 'Get')]
param(
  [Parameter(ParameterSetName = 'Bump', Mandatory = $true)][ValidateSet('major', 'minor', 'patch')][string]$Bump,
  [Parameter(ParameterSetName = 'Set', Mandatory = $true)][string]$Set,
  [Parameter(ParameterSetName = 'Notes', Mandatory = $true)][string]$Notes,
  [Parameter(ParameterSetName = 'Notes')][string]$OutFile,
  # Release date for the changelog heading (default: today, yyyy-MM-dd).
  [Parameter(ParameterSetName = 'Bump')][Parameter(ParameterSetName = 'Set')][string]$Date,
  # Overrides (tests / other checkouts). Defaults: <repo>\ValTrainerGodot\project.godot and <repo>\CHANGELOG.md.
  [string]$ProjectFile,
  [string]$Changelog
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $ProjectFile) { $ProjectFile = Join-Path $repo 'ValTrainerGodot\project.godot' }
if (-not $Changelog) { $Changelog = Join-Path $repo 'CHANGELOG.md' }
$utf8 = New-Object System.Text.UTF8Encoding($false)

# Official SemVer 2.0 grammar (https://semver.org), "v" prefix not allowed here.
$SemVerRx = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$'
$VersionLineRx = '(?m)^config/version="([^"]*)"'

function Read-Text([string]$path) { [System.IO.File]::ReadAllText($path) }
function Write-Text([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

function Get-ProjectVersion {
  if (-not (Test-Path -LiteralPath $ProjectFile)) { throw "project file not found: $ProjectFile" }
  $m = [regex]::Match((Read-Text $ProjectFile), $VersionLineRx)
  if (-not $m.Success) { throw "no config/version line in $ProjectFile" }
  return $m.Groups[1].Value
}

function Assert-SemVer([string]$v) {
  if ($v -notmatch $SemVerRx) { throw "'$v' is not a SemVer version (x.y.z or x.y.z-prerelease, e.g. 1.2.0 or 1.3.0-beta.1)" }
}

function Get-Bumped([string]$v, [string]$part) {
  $m = [regex]::Match($v, $SemVerRx)
  $maj = [int]$m.Groups[1].Value; $min = [int]$m.Groups[2].Value; $pat = [int]$m.Groups[3].Value
  $pre = $m.Groups[4].Success
  switch ($part) {
    'major' { if ($pre -and $min -eq 0 -and $pat -eq 0) { return "$maj.0.0" } return "$($maj + 1).0.0" }
    'minor' { if ($pre -and $pat -eq 0) { return "$maj.$min.0" } return "$maj.$($min + 1).0" }
    'patch' { if ($pre) { return "$maj.$min.$pat" } return "$maj.$min.$($pat + 1)" }
  }
}

function Set-ProjectVersion([string]$v) {
  $text = Read-Text $ProjectFile
  $new = [regex]::Replace($text, $VersionLineRx, ('config/version="' + $v + '"'), 1)
  if ($new -ne $text) { Write-Text $ProjectFile $new }
}

function Update-Changelog([string]$v, [string]$day) {
  if (-not (Test-Path -LiteralPath $Changelog)) {
    Write-Warning "$Changelog not found; only project.godot was updated"
    return
  }
  $text = Read-Text $Changelog
  $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $ev = [regex]::Escape($v)
  if ([regex]::IsMatch($text, "(?m)^##\s+\[v?$ev\]")) {
    Write-Host "CHANGELOG.md already has a [$v] section; left unchanged"
    return
  }
  $h = [regex]::Match($text, '(?mi)^##\s+\[Unreleased\][^\r\n]*')
  if (-not $h.Success) { throw "CHANGELOG.md has no '## [Unreleased]' section to release as $v" }
  $after = $text.Substring($h.Index + $h.Length)
  $next = [regex]::Match($after, '(?m)^(##\s|\[[^\]]+\]:\s)')
  $body = if ($next.Success) { $after.Substring(0, $next.Index) } else { $after }
  if ($body.Trim() -eq '') { Write-Warning "the [Unreleased] section is empty; releasing $v without notes" }
  $text = $text.Substring(0, $h.Index) + "## [Unreleased]$nl$nl## [$v] - $day" + $after

  # Compare links: "[Unreleased]: https://github.com/o/r/compare/v1.0.0...HEAD"
  $l = [regex]::Match($text, '(?mi)^\[Unreleased\]:[ \t]*([^\r\n]*)')
  if ($l.Success) {
    $url = $l.Groups[1].Value.Trim()
    $c = [regex]::Match($url, '^(?<base>.+?)/compare/(?<prev>[^/]+?)\.\.\.HEAD$')
    $label = $text.Substring($l.Index, $text.IndexOf(':', $l.Index) - $l.Index)   # keeps "[Unreleased]" / "[unreleased]"
    if ($c.Success) {
      $base = $c.Groups['base'].Value; $prev = $c.Groups['prev'].Value
      $prefix = if ($prev -match '^v') { 'v' } else { '' }
      $lines = "${label}: $base/compare/$prefix$v...HEAD$nl[$v]: $base/compare/$prev...$prefix$v"
    } else {
      $r = [regex]::Match($url, '^(?<base>https://github\.com/[^/\s]+/[^/\s]+)')
      if ($r.Success) {
        $base = $r.Groups['base'].Value
        $lines = "${label}: $base/compare/v$v...HEAD$nl[$v]: $base/releases/tag/v$v"
      } else { $lines = $null }
    }
    if ($lines) { $text = $text.Substring(0, $l.Index) + $lines + $text.Substring($l.Index + $l.Length) }
  }
  Write-Text $Changelog $text
  Write-Host "CHANGELOG.md: [Unreleased] -> [$v] - $day"
}

function Get-Notes([string]$v) {
  if (-not (Test-Path -LiteralPath $Changelog)) { throw "$Changelog not found" }
  $text = Read-Text $Changelog
  $v = $v.Trim()
  if ($v -match '^[vV]\d') { $v = $v.Substring(1) }
  $h = [regex]::Match($text, "(?mi)^##\s+\[v?$([regex]::Escape($v))\][^\r\n]*\r?\n?")
  if (-not $h.Success) { throw "CHANGELOG.md has no section for $v" }
  $rest = $text.Substring($h.Index + $h.Length)
  $next = [regex]::Match($rest, '(?m)^(##\s|\[[^\]]+\]:\s)')
  $body = if ($next.Success) { $rest.Substring(0, $next.Index) } else { $rest }
  return $body.Trim()
}

try {
  switch ($PSCmdlet.ParameterSetName) {
    'Get' {
      $v = Get-ProjectVersion
      Assert-SemVer $v
      Write-Output $v
    }
    'Notes' {
      $n = Get-Notes $Notes
      if ($OutFile) { Write-Text $OutFile ($n + "`n") } else { Write-Output $n }
    }
    default {
      $old = Get-ProjectVersion
      if ($PSCmdlet.ParameterSetName -eq 'Bump') {
        Assert-SemVer $old
        $new = Get-Bumped $old $Bump
      } else {
        $new = $Set.Trim()
        if ($new -match '^[vV]\d') { $new = $new.Substring(1) }
      }
      Assert-SemVer $new
      if (-not $Date) { $Date = (Get-Date).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture) }
      if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') { throw "-Date must be yyyy-MM-dd" }
      Set-ProjectVersion $new
      Write-Host "project.godot: $old -> $new"
      Update-Changelog $new $Date
      Write-Output $new
    }
  }
} catch {
  [Console]::Error.WriteLine("version.ps1: $($_.Exception.Message)")
  exit 1
}
