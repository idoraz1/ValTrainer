; ValTrainer - Inno Setup 6 script.
; Built by tools\build-release.ps1, which passes everything below with /D... (see its header). By hand:
;   ISCC.exe /DAppVersion=1.0.0 /DAppFileVersion=1.0.0.0 /DSourceDir=<"Windows Installer" export folder with
;            LICENSE.txt and THIRD-PARTY-NOTICES.txt added> /DIconFile=<ValTrainer.ico> /DOutputDir=<dist> installer\ValTrainer.iss
; Optional: /DGitHubRepo=owner/repo (publisher / support / update links; omitted while it is the OWNER placeholder),
;           /DSIGN with /Ssigntool=... (signs Setup.exe and the uninstaller).
; Per-user install by default (no admin prompt); "Install for all users" is offered in a dialog / with /ALLUSERS.
;
; Automatic updates (ValTrainerGodot\src\Core\Updater*.cs): the app downloads the next release's Setup.exe, checks its
; SHA-256 against the release's SHA256SUMS.txt and runs
;   Setup.exe /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP- /CURRENTUSER|/ALLUSERS /RELAUNCH
;             [/RELAUNCHARGS=arg1|arg2|...] /LOG=<file>
; /RELAUNCH (silent installs only) starts ValTrainer again when Setup is done, with the '|'-separated arguments of
; /RELAUNCHARGS. install.ini next to ValTrainer.exe tells the app it was installed by this Setup (AppId and per-user or
; all-users mode); the app also checks that Windows' uninstall entry for that AppId points at its own folder.
; Interactive installs behave exactly as before.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<folder with ValTrainer.exe, ValTrainer.pck, data_ValTrainer_windows_x86_64, LICENSE.txt, THIRD-PARTY-NOTICES.txt>
#endif
#ifndef IconFile
  #define IconFile AddBackslash(SourcePath) + "ValTrainer.ico"
#endif
#ifndef OutputDir
  #define OutputDir AddBackslash(SourcePath) + "..\dist"
#endif
#ifndef GitHubRepo
  #define GitHubRepo "idoraz1/ValTrainer"
#endif
#define HasRepo (Pos("OWNER", GitHubRepo) == 0 && Pos("/", GitHubRepo) > 0)

; Never change: identifies the installed app for upgrades and uninstall.
#define AppGuid "8BC1A72E-9D6C-4649-AE20-032F14D7A1F5"
#define AppExe "ValTrainer.exe"

[Setup]
AppId={{{#AppGuid}}
AppName=ValTrainer
AppVersion={#AppVersion}
AppVerName=ValTrainer {#AppVersion}
AppPublisher=ValTrainer contributors
AppCopyright=Copyright (C) 2026 ValTrainer contributors. Licensed under the GNU GPL v3.
#if HasRepo
AppPublisherURL=https://github.com/{#GitHubRepo}
AppSupportURL=https://github.com/{#GitHubRepo}/issues
AppUpdatesURL=https://github.com/{#GitHubRepo}/releases
#endif
AppComments=Free, fan-made VALORANT-style aim trainer. Not affiliated with Riot Games.
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppFileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCompany=ValTrainer contributors
VersionInfoDescription=ValTrainer Setup
VersionInfoProductName=ValTrainer
VersionInfoCopyright=Copyright (C) 2026 ValTrainer contributors

; Per-user by default: no UAC prompt, installs to %LOCALAPPDATA%\Programs\ValTrainer.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\ValTrainer
DefaultGroupName=ValTrainer
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; 64-bit Windows 10 1607+ (also runs on Windows 11 on ARM through x64 emulation).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393

#if Ver >= EncodeVer(6, 6, 0)
WizardStyle=modern dynamic
#else
WizardStyle=modern
#endif
LicenseFile={#SourceDir}\LICENSE.txt
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=ValTrainer
ShowLanguageDialog=no

Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputDir={#OutputDir}
OutputBaseFilename=ValTrainer-{#AppVersion}-Setup

; Close a running ValTrainer (Restart Manager) before replacing its files.
CloseApplications=yes
; One Setup at a time (e.g. an update started by the app and one started by hand).
SetupMutex=ValTrainerSetup{#AppGuid}
CloseApplicationsFilter=*.exe,*.dll,*.pck
RestartApplications=no
SetupLogging=yes

#ifdef SIGN
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Upgrades: drop the previous version's .NET assemblies so no stale DLL survives next to the new ones.
Type: filesandordirs; Name: "{app}\data_ValTrainer_windows_x86_64"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; Marker for the in-app updater (see the header); removed by the uninstaller.
Filename: "{app}\install.ini"; Section: "ValTrainer"; Key: "AppId"; String: "{#AppGuid}"; Flags: uninsdeletesection
Filename: "{app}\install.ini"; Section: "ValTrainer"; Key: "Mode"; String: "{code:InstallModeName}"; Flags: uninsdeletesection

[UninstallDelete]
Type: files; Name: "{app}\install.ini"

[Icons]
Name: "{group}\ValTrainer"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Aim trainer with your own VALORANT settings"
Name: "{group}\ValTrainer (safe graphics)"; Filename: "{app}\{#AppExe}"; Parameters: "--rendering-method gl_compatibility"; WorkingDir: "{app}"; Comment: "Starts ValTrainer with the OpenGL Compatibility renderer, for graphics drivers that crash or show a black screen"
Name: "{autodesktop}\ValTrainer"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,ValTrainer}"; Flags: nowait postinstall skipifsilent
; In-app update (silent + /RELAUNCH): start the new version again, as the user (never elevated).
Filename: "{app}\{#AppExe}"; Parameters: "{code:RelaunchParams}"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch

[Code]
{ ---------- Semantic Versioning precedence (1.2.0 > 1.2.0-rc.1 > 1.2.0-beta.2 > 1.1.9) ---------- }

function IsDigits(const S: String): Boolean;
var
  I: Integer;
begin
  Result := Length(S) > 0;
  for I := 1 to Length(S) do
    if (S[I] < '0') or (S[I] > '9') then
    begin
      Result := False;
      Exit;
    end;
end;

{ Removes and returns the text before the first '.' of S (all of S when there is none). }
function CutPart(var S: String): String;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P = 0 then
  begin
    Result := S;
    S := '';
  end
  else
  begin
    Result := Copy(S, 1, P - 1);
    S := Copy(S, P + 1, MaxInt);
  end;
end;

function Sgn(N: Integer): Integer;
begin
  if N < 0 then Result := -1
  else if N > 0 then Result := 1
  else Result := 0;
end;

{ Splits "v1.2.3-beta.1+build" into core "1.2.3" and prerelease "beta.1". }
procedure SplitVersion(V: String; var Core, Pre: String);
var
  P: Integer;
begin
  V := Trim(V);
  if (Length(V) > 0) and ((V[1] = 'v') or (V[1] = 'V')) then Delete(V, 1, 1);
  P := Pos('+', V);
  if P > 0 then V := Copy(V, 1, P - 1);
  P := Pos('-', V);
  if P > 0 then
  begin
    Core := Copy(V, 1, P - 1);
    Pre := Copy(V, P + 1, MaxInt);
  end
  else
  begin
    Core := V;
    Pre := '';
  end;
end;

function CompareVersions(const A, B: String): Integer;
var
  CA, PA, CB, PB, XA, XB: String;
  I: Integer;
begin
  SplitVersion(A, CA, PA);
  SplitVersion(B, CB, PB);
  for I := 1 to 3 do
  begin
    Result := Sgn(StrToIntDef(CutPart(CA), 0) - StrToIntDef(CutPart(CB), 0));
    if Result <> 0 then Exit;
  end;
  if (PA = '') and (PB = '') then begin Result := 0; Exit; end;
  if PA = '' then begin Result := 1; Exit; end;   { a release ranks above its prereleases }
  if PB = '' then begin Result := -1; Exit; end;
  while (PA <> '') or (PB <> '') do
  begin
    if PA = '' then begin Result := -1; Exit; end;
    if PB = '' then begin Result := 1; Exit; end;
    XA := CutPart(PA);
    XB := CutPart(PB);
    if IsDigits(XA) and IsDigits(XB) then Result := Sgn(StrToIntDef(XA, 0) - StrToIntDef(XB, 0))
    else if IsDigits(XA) then Result := -1
    else if IsDigits(XB) then Result := 1
    else Result := Sgn(CompareStr(XA, XB));
    if Result <> 0 then Exit;
  end;
  Result := 0;
end;

{ ---------- upgrade / downgrade ---------- }

function InstalledVersion(): String;
var
  Key: String;
begin
  Result := '';
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppGuid}' + '}_is1';
  if RegQueryStringValue(HKCU, Key, 'DisplayVersion', Result) then Exit;
  if IsWin64 and RegQueryStringValue(HKLM64, Key, 'DisplayVersion', Result) then Exit;
  if RegQueryStringValue(HKLM, Key, 'DisplayVersion', Result) then Exit;
  Result := '';
end;

function InitializeSetup(): Boolean;
var
  Old: String;
begin
  Result := True;
  Old := InstalledVersion();
  { Same or older version installed: Setup simply installs over it (settings and stats live elsewhere and are kept). }
  if (Old <> '') and (CompareVersions(Old, '{#AppVersion}') > 0) then
    Result := SuppressibleMsgBox(
      'ValTrainer ' + Old + ' is installed, which is newer than this setup (' + '{#AppVersion}' + ').' + #13#10#13#10 +
      'Replace it with the older version ' + '{#AppVersion}' + '? Your settings and stats are kept.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDYES) = IDYES;
end;

{ ---------- in-app updates ---------- }

function InstallModeName(Param: String): String;
begin
  if IsAdminInstallMode then Result := 'admin' else Result := 'user';
end;

{ True for a silent install started with /RELAUNCH (the app's updater): start ValTrainer again at the end. }
function ShouldRelaunch(): Boolean;
var
  I: Integer;
begin
  Result := False;
  if not WizardSilent then Exit;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

{ /RELAUNCHARGS=a|b|c -> a b c (each quoted when it contains a space): the arguments the app was started with. }
function RelaunchParams(Param: String): String;
var
  S, A: String;
  P: Integer;
begin
  Result := '';
  S := ExpandConstant('{param:RELAUNCHARGS|}');
  while S <> '' do
  begin
    P := Pos('|', S);
    if P = 0 then
    begin
      A := S;
      S := '';
    end
    else
    begin
      A := Copy(S, 1, P - 1);
      S := Copy(S, P + 1, MaxInt);
    end;
    if A <> '' then
    begin
      if Result <> '' then Result := Result + ' ';
      Result := Result + AddQuotes(A);
    end;
  end;
end;

{ ---------- uninstall: optionally remove the user's data ---------- }

var
  DeleteUserData: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Default NO; a silent uninstall never deletes user data. }
    DeleteUserData := False;
    if not UninstallSilent then
      DeleteUserData := MsgBox(
        'Also delete your ValTrainer settings, stats and recordings?' + #13#10#13#10 +
        'This removes:' + #13#10 +
        '  ' + ExpandConstant('{userappdata}\ValTrainer') + #13#10 +
        '      settings, stats and recordings' + #13#10 +
        '  ' + ExpandConstant('{userappdata}\Godot\app_userdata\ValTrainer') + #13#10 +
        '      logs and shader cache' + #13#10 +
        '  ' + ExpandConstant('{localappdata}\data_ValTrainer_windows_x86_64') + #13#10 +
        '      runtime unpacked by the portable version' + #13#10#13#10 +
        'Choose No to keep them (for example to reinstall later).',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { Updates the app downloaded (not user data): always removed. }
    DelTree(ExpandConstant('{localappdata}\ValTrainer\updates'), True, True, True);
    RemoveDir(ExpandConstant('{localappdata}\ValTrainer'));
  end;
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    DelTree(ExpandConstant('{userappdata}\ValTrainer'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\Godot\app_userdata\ValTrainer'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\data_ValTrainer_windows_x86_64'), True, True, True);
  end;
end;
