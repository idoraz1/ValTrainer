<#
.SYNOPSIS
  One-time GitHub setup for ValTrainer: issue labels and the milestone for the next version.
.DESCRIPTION
  Run it once after creating the GitHub repository (and again whenever you like: it is idempotent). Existing labels are
  updated to the colours and descriptions below, existing milestones are left alone, nothing is ever deleted.
  Needs the GitHub CLI (https://cli.github.com), logged in with write access:  gh auth login
.EXAMPLE
  tools\setup-github.ps1
  Uses the repository of the git remote "origin" and creates the milestone for the next minor version.
.EXAMPLE
  tools\setup-github.ps1 -Repo yourname/ValTrainer -Milestone v1.1.0
.EXAMPLE
  tools\setup-github.ps1 -DryRun
  Prints what it would do without changing anything.
#>
param(
  # owner/name on GitHub. Default: the repository of the current git remote.
  [string]$Repo = "",
  # Milestone title. Default: the next minor version after the current one, e.g. v1.1.0 after 1.0.0.
  [string]$Milestone = "",
  [switch]$DryRun
)
$ErrorActionPreference = "Stop"

# name, colour, description
$labels = @(
  @("bug",              "d73a4a", "Something doesn't work"),
  @("feature",          "a2eeef", "New feature or request"),
  @("triage",           "ededed", "Needs a first look from a maintainer"),
  @("drill",            "1d76db", "Drills and training modes"),
  @("coach",            "5319e7", "Aim coach: skill ranks, problems and fixes, run review"),
  @("sens-finder",      "0e8a16", "Sens Finder and sensitivity advice"),
  @("settings-import",  "fbca04", "Reading VALORANT settings: sens, crosshair, keybinds, display"),
  @("graphics",         "c5def5", "Rendering, quality presets, performance"),
  @("compatibility",    "f9d0c4", "Windows versions, GPUs, drivers, renderer fallback, locales"),
  @("installer",        "bfd4f2", "Installer, portable zip, update check"),
  @("documentation",    "0075ca", "README, CONTRIBUTING, docs"),
  @("breaking",         "b60205", "Breaks saved settings or stats compatibility (MAJOR version)"),
  @("skip-changelog",   "eeeeee", "PR doesn't need a CHANGELOG entry (CI, refactoring, docs)"),
  @("good first issue", "7057ff", "Good for newcomers"),
  @("help wanted",      "008672", "Extra attention is needed"),
  @("question",         "d876e3", "Further information is requested"),
  @("duplicate",        "cfd3d7", "This issue or pull request already exists"),
  @("wontfix",          "ffffff", "This will not be worked on")
)

function Get-BoardHelp { @"

Projects board (one-time, in the browser):
  1. GitHub -> your profile -> Projects -> New project -> "Board", name it "ValTrainer".
  2. Rename the Status columns to: Backlog, Next, In progress, Done.
  3. Project settings -> Manage access / Link a repository -> link $(if ($Repo) { $Repo } else { 'the repository' }).
  4. Project -> Workflows: turn on "Item added to project" (Status: Backlog), "Item closed" and
     "Pull request merged" (Status: Done). Optionally "Auto-add to project" for new issues.
  5. Move what you plan for the next release to "Next" and give those issues the milestone.
"@ }

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
  Write-Host "The GitHub CLI (gh) isn't installed, so nothing was changed."
  Write-Host ""
  Write-Host "Install it and log in, then run this script again:"
  Write-Host "  winget install --id GitHub.cli"
  Write-Host "  gh auth login"
  Write-Host "  tools\setup-github.ps1"
  Write-Host ""
  Write-Host "Or set things up by hand on GitHub (Issues -> Labels / Milestones):"
  foreach ($l in $labels) { Write-Host ("  label  {0,-17} #{1}  {2}" -f $l[0], $l[1], $l[2]) }
  Write-Host "  milestone for the next version, e.g. v1.1.0"
  Write-Host (Get-BoardHelp)
  exit 1
}

function Invoke-Gh {
  # Runs gh with the given arguments (or only prints them with -DryRun); throws when gh fails.
  # A simple function on purpose: $args passes "-X", "-f" etc. through untouched.
  $GhArgs = [string[]]$args
  $ErrorActionPreference = "Continue"   # Windows PowerShell 5.1 would otherwise stop on any gh output to stderr
  if ($DryRun) { Write-Host ("  [dry run] gh " + ($GhArgs -join " ")); return }
  $out = & gh @GhArgs 2>&1 | ForEach-Object { "$_" }
  if ($LASTEXITCODE -ne 0) { throw ("gh " + ($GhArgs -join " ") + " failed:`n" + ($out | Out-String)) }
  return $out
}

if (-not $Repo) {
  $ErrorActionPreference = "Continue"
  $Repo = (& gh repo view --json nameWithOwner --jq .nameWithOwner 2>$null | Out-String).Trim()
  $ErrorActionPreference = "Stop"
  if ($LASTEXITCODE -ne 0 -or -not $Repo) {
    Write-Error "Couldn't find the GitHub repository. Add the remote first (git remote add origin https://github.com/<you>/ValTrainer.git) or pass -Repo <you>/ValTrainer."
  }
}
if ($Repo -match '(^|/)OWNER(/|$)' -or $Repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
  Write-Error "'$Repo' isn't a real owner/name. Replace the OWNER placeholder first (see CONTRIBUTING.md)."
}
Write-Host "Repository: $Repo$(if ($DryRun) { '  (dry run: nothing is changed)' })"

# ---- Labels (gh label create --force creates or updates) ----
Write-Host ""
Write-Host "Labels:"
foreach ($l in $labels) {
  Invoke-Gh label create $l[0] --repo $Repo --color $l[1] --description $l[2] --force | Out-Null
  Write-Host "  $($l[0])"
}

# ---- Milestone for the next version ----
if (-not $Milestone) {
  $v = ""
  try { $v = ((& (Join-Path $PSScriptRoot "version.ps1") 6>&1 | Out-String) -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim() } catch { }
  if ($v -notmatch '^\d+\.\d+\.\d+') {
    $pg = Get-Content (Join-Path $PSScriptRoot "..\ValTrainerGodot\project.godot") -Raw
    if ($pg -match 'config/version="([^"]+)"') { $v = $Matches[1] }
  }
  if ($v -notmatch '^(\d+)\.(\d+)\.\d+') { Write-Error "Couldn't read the app version; pass -Milestone v<x.y.z>." }
  $Milestone = "v{0}.{1}.0" -f $Matches[1], ([int]$Matches[2] + 1)
}
Write-Host ""
Write-Host "Milestone:"
$existing = @()
if (-not $DryRun) {
  $existing = @(Invoke-Gh api "repos/$Repo/milestones" --method GET -f state=all -f per_page=100 --jq ".[].title")
}
if ($existing -contains $Milestone) {
  Write-Host "  $Milestone (already exists)"
} else {
  Invoke-Gh api "repos/$Repo/milestones" -X POST -f "title=$Milestone" -f "description=Everything planned for ValTrainer $Milestone. Move issues here from the Backlog." | Out-Null
  Write-Host "  $Milestone ($(if ($DryRun) { 'would be created' } else { 'created' }))"
}

Write-Host (Get-BoardHelp)
Write-Host "Done."
