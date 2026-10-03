<#
.SYNOPSIS
  Runs ONE Godot command (headless test, import, export, CLI tool) while holding the machine-wide Godot slot.
.DESCRIPTION
  Only one Godot process may run at a time on this PC: gshot.ps1, selftest.ps1 and this script all share the named
  mutex "Global\ValTrainerGodotSlot" and wait for each other. (Several agents starting Godot at once once froze the PC.)
  The run is stopped after -Seconds (if given) or when Godot exits, whichever comes first; only the process tree this
  script started is ever stopped. Prints the exit code and any ERROR/Exception lines from the log.
.EXAMPLE
  tools\godot-run.ps1 -Seconds 15 -GodotArgs "--path","C:\tmp\copy","--headless","--fixed-fps","120","--","--dev","--mode","flick","--simaim","good","--duration","8"
.EXAMPLE
  tools\godot-run.ps1 -GodotArgs "--headless","--path","C:\tmp\copy","--import"
#>
param(
  [Parameter(Mandatory = $true)][Alias("Args")][string[]]$GodotArgs,
  [double]$Seconds = 0,            # 0 = wait for Godot to exit (hard limit: -MaxSeconds)
  [double]$MaxSeconds = 600,
  [string]$Log = "",               # stdout log file (default: a temp file); stderr goes to "<log>.err"
  [string]$Godot = $env:GODOT
)
if (-not $Godot) {
  $Godot = Get-ChildItem (Join-Path $PSScriptRoot "godot") -Recurse -Filter "Godot_v*_mono_win64_console.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Godot -or -not (Test-Path $Godot)) { Write-Error "Godot not found (set `$env:GODOT)."; exit 2 }
if (-not $Log) { $Log = Join-Path $env:TEMP ("godot-run-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".log") }

$slot = New-Object System.Threading.Mutex($false, "Global\ValTrainerGodotSlot")
$waited = [Diagnostics.Stopwatch]::StartNew()
try { [void]$slot.WaitOne() } catch [System.Threading.AbandonedMutexException] { }   # previous holder died: the slot is ours
if ($waited.Elapsed.TotalSeconds -gt 2) { "[godot-run] waited {0:0}s for the Godot slot" -f $waited.Elapsed.TotalSeconds }
try {
  # Quote arguments with spaces: Start-Process joins them with spaces.
  $quoted = $GodotArgs | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^".*"$') { "`"$_`"" } else { $_ } }
  $p = Start-Process $Godot -ArgumentList $quoted -PassThru -WindowStyle Hidden -RedirectStandardOutput $Log -RedirectStandardError "$Log.err"
  $start = $p.StartTime
  $limit = if ($Seconds -gt 0) { $Seconds } else { $MaxSeconds }
  $exited = $p.WaitForExit([int]($limit * 1000))
  if (-not $exited) {
    # Stop only our own tree: children created after our process, named Godot*/ValTrainer*.
    $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, CreationDate, Name)
    $ids = @($p.Id); $frontier = @($p.Id)
    while ($frontier.Count -gt 0) {
      $kids = @($all | Where-Object { $frontier -contains [int]$_.ParentProcessId -and $_.CreationDate -ge $start } | ForEach-Object { [int]$_.ProcessId })
      $kids = @($kids | Where-Object { $ids -notcontains $_ }); $ids += $kids; $frontier = $kids
    }
    foreach ($id in $ids) {
      $proc = $all | Where-Object { [int]$_.ProcessId -eq $id }
      if ($id -eq $p.Id -or ($proc -and $proc.Name -match '^(Godot|ValTrainer)')) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Milliseconds 400
    "[godot-run] stopped after {0:0.0}s" -f $limit
  } else { "[godot-run] exit code $($p.ExitCode)" }
}
finally {
  try { $slot.ReleaseMutex() } catch { }
  $slot.Dispose()
}
"[godot-run] log: $Log"
Get-Content $Log, "$Log.err" -ErrorAction SilentlyContinue | Where-Object { $_ -match "ERROR|Exception" -and $_ -notmatch "resources still in use" } | Select-Object -First 20
