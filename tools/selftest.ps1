<#
.SYNOPSIS
  Headless self-tests for ValTrainer: the coach self-test (with the .vtr replay round trip), the locale (culture) tests, the bug report
  checks, a short smoke test of the drills with the simulated player, the 3D Rewind sidecar check of those runs and the Rewind insights
  check (flawed simulated players must show their own problem moments).
.DESCRIPTION
  Runs the same checks as CI (.github/workflows/ci.yml). Every run uses --dev with a throw-away --data-dir and an empty
  --valorant-dir, so your real settings, stats, telemetry and VALORANT config are never read or written; the script
  also fails if anything was written to the throw-away data folder.
  Build the C# assembly first:  dotnet build ValTrainerGodot\ValTrainer.csproj
  Exit code: the number of failed checks (0 = all passed).
.EXAMPLE
  tools\selftest.ps1
.EXAMPLE
  tools\selftest.ps1 -Godot D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe -Modes flick,tracking
#>
param(
  # Godot .NET console executable. Default: $env:GODOT, else the editor unpacked under tools\godot\.
  [string]$Godot = $env:GODOT,
  [string]$Project = (Join-Path $PSScriptRoot "..\ValTrainerGodot"),
  # Drills for the smoke test (keys from src\Modes). Reaction Test and Sens Finder need real input and are left out.
  [string[]]$Modes = @("peek_mix", "flick", "gridshot", "spider", "tracking", "strafebots", "peek", "operator", "spray_vandal",
                       "spray_phantom", "spray_transfer", "counterstrafe", "peekduel", "siteclear", "flashmap", "deathmatch",
                       "microshot", "switch", "popup", "longtaps", "jumppeek", "jigglepeek", "postplant", "retake",
                       "anchor", "sound", "flashpeek:phoenix", "recon:sova", "smokeexec:omen", "mobility:jett", "chamber:headhunter"),
  # Extra cultures for the locale test (the invariant culture always runs).
  [string[]]$Cultures = @("de-DE", "tr-TR"),
  [switch]$SkipSmoke,
  [int]$TimeoutSec = 300,
  # Where to keep the logs. Default: a temp folder that is deleted when every check passes.
  [string]$LogDir = ""
)
$ErrorActionPreference = "Stop"

if (-not $Godot) {
  $Godot = Get-ChildItem (Join-Path $PSScriptRoot "godot") -Recurse -Filter "Godot_v*_mono_win64_console.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Godot -or -not (Test-Path $Godot)) {
  Write-Error "Godot not found. Set `$env:GODOT to Godot_v4.x-stable_mono_win64_console.exe or unpack it under tools\godot\."
}
$Project = [System.IO.Path]::GetFullPath($Project)
if (-not (Test-Path (Join-Path $Project "project.godot"))) { Write-Error "No project.godot in $Project" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("vt-selftest-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$data = Join-Path $work "data"        # --data-dir: must stay empty (dev runs never write data)
$noVal = Join-Path $work "valorant"   # --valorant-dir: empty, i.e. "VALORANT not installed"
$empty = Join-Path $work "empty"      # nothing to analyse
$tel = Join-Path $work "telemetry"    # --telemetry-out of the smoke runs
$keepLogs = $LogDir -ne ""
if (-not $keepLogs) { $LogDir = Join-Path $work "logs" }
foreach ($d in @($data, $noVal, $empty, $tel, $LogDir)) { New-Item -ItemType Directory -Force $d | Out-Null }
$LogDir = (Resolve-Path $LogDir).Path

$failures = New-Object System.Collections.Generic.List[string]
$sandbox = @("--dev", "--data-dir", $data, "--valorant-dir", $noVal)

function Format-Arg([string]$a) {
  if ($a -eq "") { return '""' }
  if ($a -notmatch '[\s"]') { return $a }
  return '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

# Runs Godot, waits (killing only its own process tree on timeout) and returns the exit code and combined output.
function Invoke-Godot([string]$Label, [string[]]$GodotArgs) {
  # One Godot at a time on this PC (shared with gshot.ps1 / godot-run.ps1: several Godot instances at once froze the PC).
  $slot = New-Object System.Threading.Mutex($false, "Global\ValTrainerGodotSlot")
  try { [void]$slot.WaitOne() } catch [System.Threading.AbandonedMutexException] { }
  try { return Invoke-GodotUnlocked $Label $GodotArgs }
  finally { try { $slot.ReleaseMutex() } catch { }; $slot.Dispose() }
}

function Invoke-GodotUnlocked([string]$Label, [string[]]$GodotArgs) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $Godot
  $psi.Arguments = ($GodotArgs | ForEach-Object { Format-Arg $_ }) -join " "
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
  $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
  $p = [System.Diagnostics.Process]::Start($psi)
  $out = $p.StandardOutput.ReadToEndAsync()
  $err = $p.StandardError.ReadToEndAsync()
  $timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
  if ($timedOut) { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null; $p.WaitForExit() }
  $p.WaitForExit()
  $text = $out.Result + $err.Result
  $log = Join-Path $LogDir (($Label -replace '[:\/*?"<>|]', '-') + ".log")   # agent drill keys contain ':'
  [System.IO.File]::WriteAllText($log, "> $($psi.FileName) $($psi.Arguments)`r`n`r`n$text", (New-Object System.Text.UTF8Encoding $false))
  return [pscustomobject]@{ Code = $(if ($timedOut) { -1 } else { $p.ExitCode }); Text = $text; Log = $log; TimedOut = $timedOut }
}

function Report([string]$Name, [bool]$Ok, [string]$Why, $Run) {
  if ($Ok) { Write-Host "[PASS] $Name"; return }
  Write-Host "[FAIL] $Name - $Why"
  if ($Run) {
    Write-Host "       log: $($Run.Log)"
    ($Run.Text -split "`r?`n" | Select-Object -Last 30) | ForEach-Object { Write-Host "       | $_" }
  }
  $script:failures.Add($Name)
}

function Get-Exceptions([string]$Text) {
  return @($Text -split "`r?`n" | Where-Object { $_ -match 'Exception|Unhandled|SCRIPT ERROR' })
}

Write-Host "Godot:   $Godot"
Write-Host "Project: $Project"
Write-Host "Work:    $work"
Write-Host ""

# ---- 1. Coach self-test: synthetic cases for the coach's rules and the sens advice guardrails ----
$r = Invoke-Godot "coach-selftest" (@("--headless", "--path", $Project, "--") + $sandbox + @("--coach-analyze", $empty, "--coach-selftest"))
$lines = $r.Text -split "`r?`n"
$pass = @($lines | Where-Object { $_ -match '^\s+PASS ' }).Count
$fail = @($lines | Where-Object { $_ -match '^\s+FAIL ' })
$why = if ($r.Code -ne 0) { "exit code $($r.Code)" } elseif ($r.Text -notmatch '##### SELF-TEST') { "no SELF-TEST output" } `
       elseif ($fail.Count -gt 0) { "$($fail.Count) case(s) failed: " + ($fail -join "; ").Trim() } elseif ($pass -lt 1) { "no PASS lines" } else { "" }
Report "coach self-test ($pass cases)" ($why -eq "") $why $r

# ---- 2. Locale tests: parsers and number formatting in the invariant culture and comma-decimal cultures ----
foreach ($c in @("") + $Cultures) {
  $cArgs = if ($c) { @("--culture", $c) } else { @() }
  $name = if ($c) { $c } else { "invariant" }
  $r = Invoke-Godot "culture-$name" (@("--headless", "--path", $Project, "--") + $sandbox + $cArgs + @("--culture-test"))
  $res = ($r.Text -split "`r?`n" | Where-Object { $_ -match '\[culture-test\] RESULT:' } | Select-Object -Last 1)
  $why = if ($r.Code -ne 0) { "exit code $($r.Code) ($res)" } elseif ($res -notmatch 'RESULT: PASS') { "no PASS result" } else { "" }
  Report "culture test: $name" ($why -eq "") $why $r
}

# ---- 3. Bug report: redaction, JSON shape and size limits; then a dry run from the panel (dev runs never send) ----
$r = Invoke-Godot "bugreport-test" (@("--headless", "--path", $Project, "--") + $sandbox + @("--bugreport-test"))
$res = ($r.Text -split "`r?`n" | Where-Object { $_ -match '\[bugreport-test\] RESULT:' } | Select-Object -Last 1)
$fail = @($r.Text -split "`r?`n" | Where-Object { $_ -match '\[bugreport-test\] FAIL ' })
$why = if ($r.Code -ne 0) { "exit code $($r.Code) ($res) " + ($fail -join "; ") } elseif ($res -notmatch 'RESULT: PASS') { "no PASS result" } else { "" }
Report "bug report test" ($why -eq "") $why $r
$r = Invoke-Godot "bugreport-dryrun" (@("--headless", "--fixed-fps", "120", "--quit-after", "600", "--path", $Project, "--") + $sandbox +
                                      @("--screen", "bugreport", "--bugreport-demo", "--bugreport-answer", "send"))
$lines = $r.Text -split "`r?`n"
$at = [array]::FindIndex($lines, [Predicate[string]]{ param($l) $l -match '\[bug-report\] dev dry run, not sent' })
$body = if ($at -ge 0 -and $at + 1 -lt $lines.Count) { try { $lines[$at + 1] | ConvertFrom-Json } catch { $null } } else { $null }
$ex = Get-Exceptions $r.Text
$why = if ($r.Code -ne 0) { "exit code $($r.Code)" } elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } elseif ($at -lt 0) { "no dry-run output" } `
       elseif ($null -eq $body -or $body.schema -ne 1 -or -not $body.report.title -or $null -eq $body.app.ram_gb) { "the printed JSON doesn't match the contract" } else { "" }
Report "bug report dry run (nothing sent)" ($why -eq "") $why $r

# ---- 3b. Drill previews: every drill's live demo (menu + agent drills) plays a few seconds, nothing is saved ----
$r = Invoke-Godot "preview-check" (@("--headless", "--fixed-fps", "60", "--path", $Project, "--") + $sandbox + @("--preview-check", "--preview-check-secs", "6"))
$res = ($r.Text -split "`r?`n" | Where-Object { $_ -match '\[preview-check\] RESULT:' } | Select-Object -Last 1)
$ex = Get-Exceptions $r.Text
$why = if ($r.Code -ne 0) { "exit code $($r.Code) ($res)" } elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } elseif ($res -notmatch 'RESULT: PASS') { "no PASS result" } else { "" }
$detail = if ($res -match '\((.+)\)') { " ($($Matches[1]))" } else { "" }
Report "drill previews$detail" ($why -eq "") $why $r

# ---- 4. Smoke test: each drill runs 5 s with the simulated player, then its telemetry must load in the coach ----
if (-not $SkipSmoke) {
  foreach ($m in $Modes) {
    # 3 s countdown + 5 s run = 8 s = 960 fixed frames; quit after 1500 frames (12.5 s of game time).
    $r = Invoke-Godot "smoke-$m" (@("--headless", "--fixed-fps", "120", "--quit-after", "1500", "--path", $Project, "--") + $sandbox +
                                  @("--mode", $m, "--simaim", "good", "--duration", "5", "--telemetry-out", $tel))
    $safe = $m -replace '[:\/*?"<>|]', '-'   # telemetry file names replace ':' (agent drills) with '-'
    $vtt = @(Get-ChildItem $tel -Filter "*_$safe.vtt" -ErrorAction SilentlyContinue)
    $ex = Get-Exceptions $r.Text
    $why = if ($r.TimedOut) { "timed out after $TimeoutSec s" } elseif ($r.Code -ne 0) { "exit code $($r.Code)" } `
           elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } elseif ($vtt.Count -eq 0) { "no telemetry written (the run didn't finish)" } else { "" }
    Report "smoke: $m" ($why -eq "") $why $r
  }
  $r = Invoke-Godot "coach-analyze-smoke" (@("--headless", "--path", $Project, "--") + $sandbox + @("--coach-analyze", $tel, "--brief", "--insights"))
  $analysed = @($r.Text -split "`r?`n" | Where-Object { $_ -match '^=== .*\.vtt\s+mode=' }).Count
  $files = @(Get-ChildItem $tel -Filter "*.vtt").Count
  $ex = Get-Exceptions $r.Text
  $why = if ($r.Code -ne 0) { "exit code $($r.Code)" } elseif ($r.Text -match 'unreadable') { "a telemetry file was unreadable" } `
         elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } elseif ($analysed -ne $files) { "analysed $analysed of $files runs" } else { "" }
  Report "coach analysis of $files smoke runs" ($why -eq "") $why $r

  # 3D Rewind sidecars (.vtr) of the smoke runs: present for every 3D drill, 60 Hz player track, one PlayerShot per shot,
  # BotDie >= kills, monotonic time, < 0.5 MB per 120 s.
  $r = Invoke-Godot "replay-check-smoke" (@("--headless", "--path", $Project, "--") + $sandbox + @("--coach-analyze", $tel, "--brief", "--replay-check"))
  $rl = @($r.Text -split "`r?`n" | Where-Object { $_ -match '^\s+replay-check: (PASS|FAIL|SKIP)' })
  $rf = @($rl | Where-Object { $_ -match 'replay-check: FAIL' })
  $vtr = @(Get-ChildItem $tel -Filter "*.vtr").Count
  $ex = Get-Exceptions $r.Text
  $why = if ($r.Code -ne 0) { "exit code $($r.Code)" } elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } `
         elseif ($rl.Count -ne $files) { "checked $($rl.Count) of $files runs" } elseif ($rf.Count -gt 0) { "$($rf.Count) run(s) failed: " + ($rf -join "; ").Trim() } else { "" }
  Report "3D Rewind sidecars of $files smoke runs ($vtr .vtr)" ($why -eq "") $why $r
}

# ---- 4b. Rewind insights: each flawed simulated player must produce many moments of its own problem, the good one few ----
if (-not $SkipSmoke) {
  $insRuns = @(
    @{ Mode = "flick"; Sim = "good" }, @{ Mode = "flick"; Sim = "overshoot"; Code = "OVERFLICK" }, @{ Mode = "flick"; Sim = "premature"; Code = "PREMATURE" },
    @{ Mode = "flick"; Sim = "hesitate"; Code = "HESITATE" }, @{ Mode = "peek"; Sim = "good" }, @{ Mode = "peek"; Sim = "lowxhair"; Code = "XHAIR_LOW" }
  )
  $counts = @{}
  foreach ($x in $insRuns) {
    $key = "$($x.Mode)-$($x.Sim)"
    $dir = Join-Path $work "insights\$key"
    New-Item -ItemType Directory -Force $dir | Out-Null
    # 3 s countdown + 16 s run = 2280 fixed frames; quit after 2900.
    $r = Invoke-Godot "insights-run-$key" (@("--headless", "--fixed-fps", "120", "--quit-after", "2900", "--path", $Project, "--") + $sandbox +
                                           @("--mode", $x.Mode, "--simaim", $x.Sim, "--duration", "16", "--telemetry-out", $dir))
    $a = Invoke-Godot "insights-analyze-$key" (@("--headless", "--path", $Project, "--") + $sandbox + @("--coach-analyze", $dir, "--brief", "--insights"))
    $c = @{}
    $line = ($a.Text -split "`r?`n" | Where-Object { $_ -match '^\s+moment-counts:' } | Select-Object -First 1)
    if ($line) { foreach ($m in [regex]::Matches($line, '([A-Z_]+)=(\d+)')) { $c[$m.Groups[1].Value] = [int]$m.Groups[2].Value } }
    $trials = if ($a.Text -match 'insights \(\d+ ms\): trials=(\d+)') { [int]$Matches[1] } else { 0 }
    $c["_trials"] = [math]::Max(1, $trials)
    $counts[$key] = $c
    $ttk = ($a.Text -split "`r?`n" | Where-Object { $_ -match '^\s+ttk: n=(\d+).* sum=([\d.]+) total=([\d.]+)' } | Select-Object -First 1)
    $ex = Get-Exceptions ($r.Text + $a.Text)
    $why = if ($r.Code -ne 0 -or $a.Code -ne 0) { "exit code $($r.Code)/$($a.Code)" } elseif ($ex.Count -gt 0) { "exception: $($ex[0].Trim())" } `
           elseif (-not $line) { "no insights output" } else { "" }
    if ($why -eq "" -and $x.Mode -eq "flick") {
      # The time-to-hit split: every engagement's timestamps in order (appear ≤ onset ≤ flick end ≤ on target ≤ hit), and
      # the sum of the part medians near the median total (medians of a mixed sample needn't add up exactly: ±40%).
      if ($ttk -match 'ttk: n=(\d+).* sum=([\d.]+) total=([\d.]+) trials=(\d+) unordered=(\d+)') {
        $sum = [double]$Matches[2]; $tot = [double]$Matches[3]
        if ([int]$Matches[1] -lt 5) { $why = "too few hits for the ttk split: $($ttk.Trim())" }
        elseif ([int]$Matches[5] -gt 0) { $why = "ttk split out of order in $($Matches[5]) engagements: $($ttk.Trim())" }
        elseif ([math]::Abs($sum - $tot) -gt 0.4 * $tot) { $why = "ttk split far from the total: $($ttk.Trim())" }
      } else { $why = "no ttk breakdown" }
    }
    if ($why -eq "" -and $x.Code) {
      # Per engagement: the flaw shows in at least 40% of them, and at most half as often for the good player.
      $g = $counts["$($x.Mode)-good"]
      $n = [int]$c[$x.Code]; $good = [int]$g[$x.Code]
      $rate = $n / $c["_trials"]; $goodRate = $good / $g["_trials"]
      if ($n -lt 3 -or $rate -lt 0.4 -or $goodRate -gt 0.5 * $rate) { $why = "$($x.Code): $n moments in $($c["_trials"]) engagements vs $good in $($g["_trials"]) for the good player" }
    }
    $detail = if ($x.Code) { " ($($x.Code)=$([int]$c[$x.Code]), good=$([int]$counts["$($x.Mode)-good"][$x.Code]))" } else { "" }
    Report "insights: $key$detail" ($why -eq "") $why $a
  }
}

# ---- 5. Dev runs must never write user data ----
$written = @(Get-ChildItem $data -Recurse -File -ErrorAction SilentlyContinue)
Report "dev runs wrote no data" ($written.Count -eq 0) ("wrote: " + (($written | ForEach-Object { $_.FullName.Substring($data.Length) }) -join ", ")) $null

Write-Host ""
if ($failures.Count -eq 0) {
  Write-Host "All checks passed."
  if ($keepLogs) { Write-Host "Logs: $LogDir" }
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
} else {
  Write-Host "$($failures.Count) check(s) failed: $($failures -join ', ')"
  Write-Host "Logs: $LogDir"
}
exit $failures.Count
