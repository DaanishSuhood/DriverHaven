; DriverHaven installer (Inno Setup 6). Build it with .\publish.ps1, which publishes the app first and passes the
; version from DriverUpdater.App.csproj:  ISCC /DAppVersion=1.2.3 installer\DriverHaven.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "DriverHaven"
#define AppExe "DriverHaven.exe"
#define AppDescription "Find and safely install driver updates from Windows Update"

[Setup]
; Never change AppId: it is how later versions find and upgrade this install.
AppId={{53EE3F46-C2D3-47EC-BD2C-7FB198DEAFC2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; The app itself needs admin rights, so it is installed for all users.
PrivilegesRequired=admin
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\DriverUpdater.App\Assets\DriverHaven.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Close a running copy when upgrading; don't relaunch it afterwards (the finish page offers that).
CloseApplications=yes
RestartApplications=no
OutputDir=..\artifacts\installer
OutputBaseFilename={#AppName}-Setup-{#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Both ticked by default.
Name: "startmenuicon"; Description: "Create a &Start menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppDescription}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppDescription}"; Tasks: desktopicon

[Run]
; runascurrentuser: the app requires admin, and Setup already is, so this avoids a second UAC prompt.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { Per-user window size and position: always removed. }
  DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);

  { Logs and driver backups: kept unless the user says otherwise, because the backups are what let them
    reinstall the drivers DriverHaven replaced. A silent uninstall always keeps them. }
  DataDir := ExpandConstant('{commonappdata}\{#AppName}');
  if DirExists(DataDir) and not UninstallSilent then
    if MsgBox('Also delete {#AppName}''s logs and driver backups?' + #13#10#13#10 + DataDir + #13#10#13#10 +
              'The backups let you reinstall the drivers {#AppName} replaced. Choose No to keep them.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
end;
