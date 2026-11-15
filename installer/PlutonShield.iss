; Pluton Shield installer (Inno Setup 6). Built by tools\make-installer.ps1, which passes:
;   /DAppVersion=0.4.0 (or 0.4.0-rc.1)  /DNumericVersion=0.4.0.0  /DArch=x64|arm64
;   /DSourceDir=<dist\x64>              /DOutputDir=<release>
; Optional: /DFast=1 compresses quickly (CI test builds).
; Installs for the current Windows user only (no administrator rights): the app changes this user's proxy setting
; and trusted certificates, and keeps its data in %LOCALAPPDATA%\PlutonShield.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif

[Setup]
AppId={{4AAFF650-CA66-4983-9952-17EF4B6653CB}
AppName=Pluton Shield
AppVersion={#AppVersion}
AppVerName=Pluton Shield {#AppVersion}
AppPublisher=Pluton Labs
AppPublisherURL=https://github.com/plutonlabs/shield
AppSupportURL=https://github.com/plutonlabs/shield/issues
AppUpdatesURL=https://github.com/plutonlabs/shield/releases
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=Pluton Shield
VersionInfoDescription=Pluton Shield Setup
DefaultDirName={autopf}\PlutonShield
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
SetupIconFile=..\src\PlutonShield.App\Assets\plutonshield.ico
UninstallDisplayIcon={app}\PlutonShield.exe
UninstallDisplayName=Pluton Shield
OutputDir={#OutputDir}
OutputBaseFilename=PlutonShield-v{#AppVersion}-win-{#Arch}-Setup
#ifdef Fast
Compression=lzma2/fast
#else
Compression=lzma2/max
#endif
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
; The app is closed by "PlutonShield.exe --quit" (see PrepareToInstall), which also gives the proxy setting back.
CloseApplications=no
RestartIfNeededByRun=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; "cli" (the command-line tool) needs the .NET runtime to be installed, so it is only in the .zip.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "cli\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Pluton Shield"; Filename: "{app}\PlutonShield.exe"
Name: "{autodesktop}\Pluton Shield"; Filename: "{app}\PlutonShield.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PlutonShield.exe"; Description: "{cm:LaunchProgram,Pluton Shield}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; In this order: close the app (it gives the Windows proxy setting back), then remove what is left outside its folder
; (the proxy setting of a copy that was killed, and the trusted Pluton Shield certificate; Windows asks to confirm that).
Filename: "{app}\PlutonShield.exe"; Parameters: "--quit"; Flags: waituntilterminated; RunOnceId: "PlutonShieldQuit"
Filename: "{app}\PlutonShield.exe"; Parameters: "--cleanup"; Flags: waituntilterminated; RunOnceId: "PlutonShieldCleanup"

[InstallDelete]
; Updating an install made under the old name (NeatShield): remove its program files and shortcuts.
Type: files; Name: "{app}\NeatShield*"
Type: files; Name: "{autoprograms}\NeatShield.lnk"
Type: files; Name: "{autodesktop}\NeatShield.lnk"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExePath: String;
  ResultCode: Integer;
begin
  Result := '';
  // Updating over a running copy: ask it to close first, or its files cannot be replaced.
  ExePath := ExpandConstant('{app}\PlutonShield.exe');
  if FileExists(ExePath) then
    Exec(ExePath, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // The same for a copy installed under the old name (NeatShield).
  ExePath := ExpandConstant('{app}\NeatShield.exe');
  if FileExists(ExePath) then
    Exec(ExePath, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // "Start with Windows", if the user turned it on in the app's settings.
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PlutonShield');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NeatShield');

    // The user's own data is only deleted when they say so (never in a silent uninstall).
    if not UninstallSilent then
    begin
      if MsgBox('Also delete Pluton Shield''s settings, your own filters, statistics and logs?' + #13#10 + #13#10 +
                'Choose No to keep them (for example before installing again).',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ExpandConstant('{localappdata}\PlutonShield'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\NeatShield'), True, True, True);
      end;
    end;
  end;
end;
