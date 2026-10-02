<#
.SYNOPSIS
  Builds a ValTrainer release: the installer, the portable zip and their SHA-256 sums.

.DESCRIPTION
  Output (in -OutDir, default dist\ in the repo root):
    ValTrainer-<version>-Setup.exe      Inno Setup installer (per-user, no admin prompt)
    ValTrainer-<version>-Portable.zip   single-file ValTrainer.exe + LICENSE.txt + THIRD-PARTY-NOTICES.txt + README.txt
    SHA256SUMS.txt                      "<sha256>  <file>" lines (sha256sum -c compatible)

  Steps:
    1. Version from ValTrainerGodot\project.godot (tools\version.ps1).
    2. Copies the Godot project to a private work folder - the tracked tree is never modified - adds the repo's
       CHANGELOG.md (the in-game "What's new" reads res://CHANGELOG.md) and stamps the Windows file version.
    3. Imports assets, builds the C# project, exports the "Windows Installer" preset (folder: exe + pck + .NET
       data folder) and the "Windows Desktop" preset (one self-contained exe).
    4. THIRD-PARTY-NOTICES.txt: ValTrainer (GPL-3.0), Godot and its third-party components (printed by the exported
       game itself, so they match the shipped engine), the .NET runtime's license and notices, CC0 asset credits.
    5. Optional code signing, the portable zip, the installer (Inno Setup 6), SHA256SUMS.txt.

  Works locally and on CI (GitHub windows-latest): pass -Godot / -Iscc or set $env:GODOT. Needs the .NET 8 SDK and
  the Godot 4.7.2 .NET export templates. Never commits anything; exits non-zero on any failure.

  Code signing (optional): set SIGNTOOL_CERT_THUMBPRINT (SHA-1 thumbprint of a code-signing certificate in the
  Windows certificate store) and optionally SIGNTOOL_PATH (signtool.exe) and SIGNTOOL_TIMESTAMP_URL. Then both
  ValTrainer.exe files, Setup.exe and the uninstaller are signed. Without it, signing is skipped.

.PARAMETER Godot
  Godot 4.7.2 .NET console exe. Default: $env:GODOT, else tools\godot\Godot_v4.7.2-stable_mono_win64\..._console.exe.
.PARAMETER OutDir
  Output folder (relative paths are relative to the repo root). Default: dist.
.PARAMETER Iscc
  Inno Setup 6 compiler. Default: tools\innosetup\ISCC.exe, then Program Files, then PATH.
.PARAMETER SkipInstaller
  Only build the portable zip (no Inno Setup needed).
.PARAMETER WorkDir
  Private build folder. Default: $env:RUNNER_TEMP or %TEMP%, \ValTrainer-build. Deleted afterwards unless -KeepWork.
.PARAMETER KeepWork
  Keep the work folder (later builds then only re-copy changed files and skip most of the asset import).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
.EXAMPLE
  pwsh tools\build-release.ps1 -Godot C:\godot\Godot_v4.7.2-stable_mono_win64_console.exe -Iscc "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
#>
[CmdletBinding()]
param(
  [string]$Godot = "",
  [string]$OutDir = "dist",
  [string]$Iscc = "",
  [switch]$SkipInstaller,
  [string]$WorkDir = "",
  [switch]$KeepWork
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Repo = Split-Path -Parent $PSScriptRoot
$Proj = Join-Path $Repo 'ValTrainerGodot'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

function Step([string]$msg) { Write-Host ("[{0,5:0}s] {1}" -f $sw.Elapsed.TotalSeconds, $msg) -ForegroundColor Cyan }
function Fail([string]$msg) { throw $msg }

function Invoke-Native([string]$exe, [string[]]$arguments, [string]$what) {
  & $exe @arguments
  if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit code $LASTEXITCODE)" }
}

function Write-Utf8([string]$path, [string]$text, [bool]$bom = $false) {
  [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($bom)))
}

# Multi-size .ico (16-256 px) from the 256 px app icon: BMP entries up to 48 px, PNG entries above (Vista+ format).
function New-Icon([string]$png, [string]$ico) {
  Add-Type -AssemblyName System.Drawing
  $src = [System.Drawing.Image]::FromFile($png)
  try {
    $images = New-Object System.Collections.Generic.List[byte[]]
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    foreach ($s in $sizes) {
      $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
      $g.DrawImage($src, 0, 0, $s, $s)
      $g.Dispose()
      $ms = New-Object System.IO.MemoryStream
      if ($s -ge 64) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
      } else {
        $bw = New-Object System.IO.BinaryWriter($ms)
        $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
        $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]0); $bw.Write([int]($s * $s * 4 + $maskRow * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
        $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $row = New-Object byte[] ($s * 4)
        for ($y = $s - 1; $y -ge 0; $y--) {   # bottom-up BGRA rows
          [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
          $bw.Write($row)
        }
        $bmp.UnlockBits($data)
        $bw.Write((New-Object byte[] ($maskRow * $s)))   # AND mask unused (alpha channel)
        $bw.Flush()
      }
      $images.Add($ms.ToArray())
      $bmp.Dispose()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
      $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
      $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
      $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
      $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($ico, $out.ToArray())
  } finally { $src.Dispose() }
}

function Find-SignTool {
  if ($env:SIGNTOOL_PATH) { return $env:SIGNTOOL_PATH }
  $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
  if (Test-Path $kits) {
    $st = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($st) { return $st.FullName }
  }
  $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  Fail "SIGNTOOL_CERT_THUMBPRINT is set but signtool.exe wasn't found (set SIGNTOOL_PATH)"
}

$work = $null
try {
  # ---------------------------------------------------------------- 1. version & tools
  $global:LASTEXITCODE = 0
  $Version = (& (Join-Path $PSScriptRoot 'version.ps1')) | Select-Object -Last 1
  if ($LASTEXITCODE -ne 0 -or -not $Version) { Fail "couldn't read the version (tools\version.ps1)" }
  $Version = $Version.Trim()
  $numeric = ($Version -split '[-+]')[0]
  $FileVersion = "$numeric.0"          # Windows needs 4 numeric parts; prerelease tags can't go in there
  Write-Host ""
  Write-Host "ValTrainer $Version  (file version $FileVersion)" -ForegroundColor Green

  if (-not $Godot) { $Godot = $env:GODOT }
  if (-not $Godot) { $Godot = Join-Path $Repo 'tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' }
  if (-not (Test-Path -LiteralPath $Godot)) { Fail "Godot not found: $Godot (pass -Godot or set `$env:GODOT)" }
  $godotVer = (& $Godot --version 2>$null | Select-Object -Last 1)
  if ("$godotVer" -notmatch 'mono') { Fail "$Godot is not a Godot .NET (mono) build ('$godotVer')" }
  Write-Host "Godot $godotVer"

  if (-not $SkipInstaller) {
    if (-not $Iscc) {
      $candidates = @(
        (Join-Path $Repo 'tools\innosetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
      $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
      if (-not $Iscc) { $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($c) { $Iscc = $c.Source } }
    }
    if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) { Fail "Inno Setup 6 (ISCC.exe) not found: pass -Iscc, install Inno Setup 6, or use -SkipInstaller" }
    Write-Host "Inno Setup: $Iscc"
  }

  $license = Join-Path $Repo 'LICENSE'
  if (-not (Test-Path -LiteralPath $license)) { Fail "LICENSE not found in the repo root" }

  if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $Repo $OutDir }
  New-Item -ItemType Directory -Force $OutDir | Out-Null
  $OutDir = (Resolve-Path -LiteralPath $OutDir).Path

  if (-not $WorkDir) {
    $tmp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
    $WorkDir = Join-Path $tmp 'ValTrainer-build'
  }
  $work = $WorkDir
  $stage = Join-Path $work 'project'
  $outInst = Join-Path $work 'installer'
  $outPort = Join-Path $work 'portable'
  foreach ($d in @($outInst, $outPort, (Join-Path $work 'zip'), (Join-Path $work 'appdata'))) {
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
  }
  New-Item -ItemType Directory -Force $stage, $outInst, $outPort | Out-Null

  # ---------------------------------------------------------------- 2. private copy of the project
  Step "Copying the project to $stage"
  # .godot\imported is copied too (saves re-importing every asset); .godot\mono (build output) is rebuilt.
  & robocopy $Proj $stage /MIR /XD (Join-Path $Proj '.godot\mono') (Join-Path $stage '.godot\mono') /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { Fail "robocopy failed (exit code $LASTEXITCODE)" }
  $global:LASTEXITCODE = 0

  $changelog = Join-Path $Repo 'CHANGELOG.md'
  $stageChangelog = Join-Path $stage 'CHANGELOG.md'
  if (Test-Path -LiteralPath $changelog) { Copy-Item -LiteralPath $changelog $stageChangelog -Force }
  else {
    Write-Warning "CHANGELOG.md not found in the repo root: the in-game ""What's new"" panel will be skipped"
    if (Test-Path -LiteralPath $stageChangelog) { Remove-Item -LiteralPath $stageChangelog -Force }
  }

  # Windows file/product version: Godot falls back to config/version when these are empty, but only for purely
  # numeric versions (1.1.0-beta.1 would become 1.0.0.0), so stamp x.y.z.0 into the private copy's presets.
  $presets = Join-Path $stage 'export_presets.cfg'
  $p = [System.IO.File]::ReadAllText($presets)
  $p = [regex]::Replace($p, '(?m)^application/(file_version|product_version)=".*"', ('application/$1="' + $FileVersion + '"'))
  Write-Utf8 $presets $p

  # ---------------------------------------------------------------- 3. import, build, export
  Step "Importing assets"
  Invoke-Native $Godot @('--headless', '--path', $stage, '--import') 'Godot asset import'

  Step "Building C#"
  Invoke-Native 'dotnet' @('build', (Join-Path $stage 'ValTrainer.csproj'), '-c', 'Debug', '-nologo', '-v', 'q') 'dotnet build'

  Step "Exporting ""Windows Installer"" -> $outInst"
  $instExe = Join-Path $outInst 'ValTrainer.exe'
  Invoke-Native $Godot @('--headless', '--path', $stage, '--export-release', 'Windows Installer', $instExe) 'Godot export (Windows Installer)'
  $dataDir = Join-Path $outInst 'data_ValTrainer_windows_x86_64'
  foreach ($f in @($instExe, (Join-Path $outInst 'ValTrainer.pck'), $dataDir)) {
    if (-not (Test-Path -LiteralPath $f)) { Fail "export output missing: $f" }
  }

  Step "Exporting ""Windows Desktop"" (single exe) -> $outPort"
  $portExe = Join-Path $outPort 'ValTrainer.exe'
  Invoke-Native $Godot @('--headless', '--path', $stage, '--export-release', 'Windows Desktop', $portExe) 'Godot export (Windows Desktop)'
  if (-not (Test-Path -LiteralPath $portExe) -or (Get-Item -LiteralPath $portExe).Length -lt 50MB) { Fail "portable export missing or too small: $portExe" }
  foreach ($exe in @($instExe, $portExe)) {
    $fv = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    if ($fv -ne $FileVersion) { Fail "$exe has file version '$fv', expected $FileVersion" }
  }

  # ---------------------------------------------------------------- 4. notices
  Step "Writing THIRD-PARTY-NOTICES.txt"
  $engineNotices = Join-Path $work 'licenses-from-game.txt'
  if (Test-Path -LiteralPath $engineNotices) { Remove-Item -LiteralPath $engineNotices -Force }
  # The exported game prints its own notices (Engine.GetLicenseText / GetCopyrightInfo / GetLicenseInfo).
  # --dev + --data-dir: never touches the real settings; --log-file keeps the user's Godot log folder clean.
  $argLine = "--headless --log-file `"$(Join-Path $work 'print-licenses.log')`" -- --dev --data-dir `"$(Join-Path $work 'appdata')`" --print-licenses `"$engineNotices`""
  $proc = Start-Process -FilePath $instExe -ArgumentList $argLine -Wait -PassThru -WindowStyle Hidden
  if ($proc.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $engineNotices) -or (Get-Item -LiteralPath $engineNotices).Length -lt 20KB) {
    Fail "the exported game couldn't write its license notices (exit code $($proc.ExitCode)); see $(Join-Path $work 'print-licenses.log')"
  }

  # .NET runtime notices from the runtime pack the export was published with.
  $deps = Get-ChildItem -LiteralPath $dataDir -Recurse -Filter 'ValTrainer.deps.json' | Select-Object -First 1
  if (-not $deps) { Fail "ValTrainer.deps.json not found in $dataDir" }
  $m = [regex]::Match([System.IO.File]::ReadAllText($deps.FullName), 'runtimepack\.Microsoft\.NETCore\.App\.Runtime\.win-x64/([0-9][^"]*)"')
  if (-not $m.Success) { Fail "couldn't find the .NET runtime pack version in $($deps.FullName)" }
  $netVer = $m.Groups[1].Value
  $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
  $pack = Join-Path $nuget "microsoft.netcore.app.runtime.win-x64\$netVer"
  $netNotices = Get-ChildItem -LiteralPath $pack -Filter 'THIRD-PARTY-NOTICES.TXT' -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $netNotices) { Fail ".NET runtime notices not found in $pack" }

  $sep = '=' * 64
  $notices = New-Object System.Text.StringBuilder
  [void]$notices.Append("ValTrainer $Version - third-party notices`r`n")
  [void]$notices.Append("Generated by tools\build-release.ps1. Also shown in the game: Settings > About > Licenses.`r`n`r`n")
  [void]$notices.Append("ValTrainer is free software under the GNU General Public License v3 (LICENSE.txt). It is built with the`r`n")
  [void]$notices.Append("Godot Engine (MIT) and runs on the .NET runtime (MIT); their licenses and the notices of the components`r`n")
  [void]$notices.Append("they include follow, then the credits for the CC0 models, textures and sounds.`r`n`r`n")
  [void]$notices.Append([System.IO.File]::ReadAllText($engineNotices).TrimEnd())
  [void]$notices.Append("`r`n`r`n$sep`r`n.NET RUNTIME $netVer - THIRD-PARTY NOTICES`r`n$sep`r`n`r`n")
  [void]$notices.Append(([System.IO.File]::ReadAllText($netNotices.FullName) -replace "`r?`n", "`r`n").TrimEnd())
  [void]$notices.Append("`r`n`r`n$sep`r`nINSTALLER`r`n$sep`r`n`r`n")
  [void]$notices.Append("The Windows installer is made with Inno Setup (https://jrsoftware.org/isinfo.php).`r`n")
  [void]$notices.Append("Copyright (C) 1997-2026 Jordan Russell. Portions Copyright (C) 2000-2026 Martijn Laan.`r`n")
  $noticesText = $notices.ToString()
  $licenseText = [System.IO.File]::ReadAllText($license) -replace "`r?`n", "`r`n"

  foreach ($dir in @($outInst, $outPort)) {
    Write-Utf8 (Join-Path $dir 'THIRD-PARTY-NOTICES.txt') $noticesText $true
    Write-Utf8 (Join-Path $dir 'LICENSE.txt') $licenseText $true
  }

  # ---------------------------------------------------------------- 5a. optional signing
  $signArgs = @()
  if ($env:SIGNTOOL_CERT_THUMBPRINT) {
    $signtool = Find-SignTool
    $ts = if ($env:SIGNTOOL_TIMESTAMP_URL) { $env:SIGNTOOL_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    Step "Signing ValTrainer.exe (installer and portable builds)"
    foreach ($exe in @($instExe, $portExe)) {
      Invoke-Native $signtool @('sign', '/sha1', $env:SIGNTOOL_CERT_THUMBPRINT, '/fd', 'sha256', '/tr', $ts, '/td', 'sha256', '/d', 'ValTrainer', $exe) "signtool ($exe)"
    }
    # Inno Setup signs Setup.exe and the uninstaller with this command ($q = quote, $f = file).
    $signArgs = @('/DSIGN', ('/Ssigntool=$q' + $signtool + '$q sign /sha1 ' + $env:SIGNTOOL_CERT_THUMBPRINT + ' /fd sha256 /tr ' + $ts + ' /td sha256 /d $qValTrainer Setup$q $f'))
  } else {
    Write-Host "Code signing: skipped (SIGNTOOL_CERT_THUMBPRINT not set)"
  }

  # ---------------------------------------------------------------- 5b. portable zip
  $zipName = "ValTrainer-$Version-Portable.zip"
  $zipPath = Join-Path $OutDir $zipName
  Step "Packing $zipName"
  $zipRoot = Join-Path $work "zip\ValTrainer-$Version"
  New-Item -ItemType Directory -Force $zipRoot | Out-Null
  Copy-Item -Path (Join-Path $outPort '*') -Destination $zipRoot -Recurse -Force
  $readme = @"
ValTrainer $Version - portable version
=====================================

Free, fan-made VALORANT-style aim trainer. Not affiliated with or endorsed by Riot Games.

Run ValTrainer.exe - no installation needed. It reads your VALORANT settings (sensitivity, crosshair,
keybinds, display) from %LOCALAPPDATA%\VALORANT\Saved\Config and never changes them.

Files it creates:
  %APPDATA%\ValTrainer                              settings, stats and recordings
  %APPDATA%\Godot\app_userdata\ValTrainer           logs (logs\godot.log) and shader cache
  %LOCALAPPDATA%\data_ValTrainer_windows_x86_64     .NET runtime, unpacked on first launch
To remove ValTrainer completely, delete this folder and those three.

Black screen or crash at startup? Start it with the OpenGL renderer:
  ValTrainer.exe --rendering-method gl_compatibility

The installer version (ValTrainer-$Version-Setup.exe) adds Start menu shortcuts and an uninstaller.
License: GNU GPL v3 (LICENSE.txt). Third-party licenses: THIRD-PARTY-NOTICES.txt.
"@
  Write-Utf8 (Join-Path $zipRoot 'README.txt') ($readme -replace "`r?`n", "`r`n") $true
  if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory($zipRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

  # ---------------------------------------------------------------- 5c. installer
  $setupName = "ValTrainer-$Version-Setup.exe"
  $setupPath = Join-Path $OutDir $setupName
  if (-not $SkipInstaller) {
    Step "Compiling $setupName (Inno Setup)"
    $ico = Join-Path $work 'ValTrainer.ico'
    New-Icon (Join-Path $Proj 'icon.png') $ico
    $repoMatch = [regex]::Match([System.IO.File]::ReadAllText((Join-Path $Proj 'src\Core\AppInfo.cs')), 'GitHubRepo\s*=\s*"([^"]+)"')
    $ghRepo = if ($repoMatch.Success) { $repoMatch.Groups[1].Value } else { 'idoraz1/ValTrainer' }
    if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
    $isccArgs = @('/Qp', "/DAppVersion=$Version", "/DAppFileVersion=$FileVersion", "/DSourceDir=$outInst", "/DIconFile=$ico",
      "/DOutputDir=$OutDir", "/DGitHubRepo=$ghRepo") + $signArgs + @((Join-Path $Repo 'installer\ValTrainer.iss'))
    Invoke-Native $Iscc $isccArgs 'Inno Setup'
    if (-not (Test-Path -LiteralPath $setupPath)) { Fail "installer not produced: $setupPath" }
  } else {
    Write-Host "Installer: skipped (-SkipInstaller)"
  }

  # ---------------------------------------------------------------- 5d. checksums
  $sums = Join-Path $OutDir 'SHA256SUMS.txt'
  $lines = foreach ($f in @($setupPath, $zipPath)) {
    if (Test-Path -LiteralPath $f) { '{0}  {1}' -f (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $f) }
  }
  Write-Utf8 $sums ((@($lines) -join "`n") + "`n")

  Write-Host ""
  Write-Host ("ValTrainer {0} built in {1:0} s:" -f $Version, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
  foreach ($f in @($setupPath, $zipPath, $sums)) {
    if (Test-Path -LiteralPath $f) { Write-Host ("  {0,-48} {1,10:N1} MB" -f $f, ((Get-Item -LiteralPath $f).Length / 1MB)) }
  }
  $exitCode = 0
} catch {
  Write-Host ""
  Write-Host "build-release.ps1 FAILED: $($_.Exception.Message)" -ForegroundColor Red
  if ($_.InvocationInfo) { Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor DarkGray }
  $exitCode = 1
} finally {
  if ($work -and -not $KeepWork -and (Test-Path -LiteralPath $work)) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
  }
}
exit $exitCode
