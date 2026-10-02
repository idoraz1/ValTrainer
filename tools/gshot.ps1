param(
  [string]$Mode = "",
  [double[]]$At = @(6),
  [string]$Name = "g",
  [string]$Exe = "",
  [string]$Extra = "",
  [string]$ProjectPath = (Join-Path $PSScriptRoot "..\ValTrainerGodot"),
  [switch]$Client   # save only the client area (no title bar / borders) at its native size, e.g. 1600x900
)
# Runs ValTrainer (Godot) in --dev mode (windowed 1600x900, no mouse capture, uncapped FPS, never writes stats)
# and saves 960x540 screenshots of ITS OWN WINDOW CONTENTS at the given times (seconds).
# Uses PrintWindow(PW_RENDERFULLCONTENT) so other windows covering the game are never captured.
# Example: & tools\gshot.ps1 -Mode gridshot -At 5,8 -Name grid -Extra "--tier 2 --map bind"
# Paths: -ProjectPath defaults to ..\ValTrainerGodot next to this script; screenshots and logs go to $env:GSHOT_OUT
# (default %TEMP%\gshot); Godot is $env:GODOT (the .NET console exe) or the editor unpacked under tools\godot\.
$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$sp = if ($env:GSHOT_OUT) { $env:GSHOT_OUT } else { Join-Path $env:TEMP "gshot" }
New-Item -ItemType Directory -Force $sp | Out-Null
$godot = if ($env:GODOT) { $env:GODOT } else {
  Get-ChildItem (Join-Path $PSScriptRoot "godot") -Recurse -Filter "Godot_v*_mono_win64_console.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if ($Exe -eq "" -and -not $godot) { throw "Godot not found: set `$env:GODOT to Godot_v4.x-stable_mono_win64_console.exe or unpack it under tools\godot\" }
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W3 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public struct RECT { public int L,T,R,B; }
}
"@ -ErrorAction SilentlyContinue
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W4 {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  public struct RECT { public int L,T,R,B; }
  public struct POINT { public int X,Y; }
}
"@ -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Drawing
$argsList = @()
if ($Exe -eq "") { $argsList += @("--path", "`"$ProjectPath`"") }   # quoted: Start-Process joins arguments with spaces
$argsList += @("--", "--dev")
if ($Mode -ne "") { $argsList += @("--mode", $Mode) }
if ($Extra -ne "") { $argsList += $Extra.Split(" ") }
$file = if ($Exe -ne "") { $Exe } else { $godot }
function Get-TreeIds([int]$root) {
  # Real children only: a process whose ParentProcessId points at our PID but that was created BEFORE our process
  # is an orphan of an older process that had the same (reused) PID (e.g. Discord helpers) - never capture/kill those.
  $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, CreationDate, Name)
  $byId = @{}; foreach ($x in $all) { $byId[[int]$x.ProcessId] = $x }
  if (-not $byId.ContainsKey($root)) { return @() }
  # The root must still be the process we started (not a later process that reused its PID).
  if ([math]::Abs(($byId[$root].CreationDate - $script:rootStart).TotalSeconds) -gt 3) { return @() }
  $ids = @($root); $frontier = @($root)
  while ($frontier.Count -gt 0) {
    $next = @()
    foreach ($f in $frontier) {
      $fp = $byId[$f]
      foreach ($c in $all) {
        $cid = [int]$c.ProcessId
        if ([int]$c.ParentProcessId -eq $f -and $c.CreationDate -ge $fp.CreationDate -and $ids -notcontains $cid) { $next += $cid }
      }
    }
    $ids += $next; $frontier = $next
  }
  # Only Godot / ValTrainer executables (conhost etc. exit on their own).
  return @($ids | Where-Object { $byId.ContainsKey($_) -and $byId[$_].Name -match '^(Godot|ValTrainer)' })
}
$p = Start-Process $file -ArgumentList $argsList -PassThru -RedirectStandardOutput "$sp\$Name.out.log" -RedirectStandardError "$sp\$Name.err.log"
$script:rootStart = $p.StartTime
$prev = 0; $i = 0
foreach ($t in $At) {
  Start-Sleep -Milliseconds ([int](($t - $prev) * 1000)); $prev = $t
  # The console launcher starts the game as a child process: look only at the process WE launched and its
  # descendants (concurrent runs by other agents/tools are never captured or killed).
  $ids = Get-TreeIds $p.Id
  # Belt and braces: only a ValTrainer-titled window of a Godot/ValTrainer process from our own tree is ever captured.
  $w = Get-Process -Id $ids -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like "ValTrainer*" } | Select-Object -First 1
  if (-not $w) { "[$Name] no window at ${t}s"; continue }
  $h = $w.MainWindowHandle
  $r = New-Object W3+RECT; [W3]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  $ok = [W3]::PrintWindow($h, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT: the window's own content, even if covered
  $g.ReleaseHdc($hdc)
  if ($ok) {
    if ($Client) {
      $cr = New-Object W4+RECT; [W4]::GetClientRect($h, [ref]$cr) | Out-Null
      $pt = New-Object W4+POINT; [W4]::ClientToScreen($h, [ref]$pt) | Out-Null
      $crop = New-Object System.Drawing.Rectangle ($pt.X - $r.L), ($pt.Y - $r.T), ($cr.R - $cr.L), ($cr.B - $cr.T)
      $small = $bmp.Clone($crop, $bmp.PixelFormat)
    } else { $small = New-Object System.Drawing.Bitmap $bmp, 960, 540 }
    $small.Save("$sp\${Name}_$i.png"); $small.Dispose()
    "[$Name] shot $i at ${t}s -> $sp\${Name}_$i.png"; $i++
  } else { "[$Name] PrintWindow failed at ${t}s" }
  $g.Dispose(); $bmp.Dispose()
}
$ids = Get-TreeIds $p.Id
foreach ($id in $ids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 500
Get-Content "$sp\$Name.err.log" -ErrorAction SilentlyContinue | Where-Object { $_ -match "ERROR|Exception" } | Select-Object -First 15
Get-Content "$sp\$Name.out.log" -ErrorAction SilentlyContinue | Where-Object { $_ -match "ERROR|Exception" } | Select-Object -First 15
