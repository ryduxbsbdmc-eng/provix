; Provix installer script for Inno Setup 6
; Build: build-setup.cmd
; WinGet: silent install /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-

#define MyAppName "Provix"
#define MyAppVersion "1.4.6"
#define MyAppVersionInfo "1.4.6.0"
#define MyAppPublisher "Provix"
#define MyAppExeName "FileExplorer.exe"
#define MyAppSourceDir "..\publish"
#define MyAppOutputDir "..\installer"
#define MyAppUrl "https://github.com/ryduxbsbdmc-eng/provix"

[Setup]
AppId={{8F4E2A91-6C3D-4B8E-9F1A-2D7E5B4C9013}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
OutputDir={#MyAppOutputDir}
OutputBaseFilename=Provix-Setup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=force
RestartIfNeededByRun=no
ChangesAssociations=no
CreateUninstallRegKey=yes
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Provix file manager for Windows
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersionInfo}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceDir}\Locales\*"; DestDir: "{app}\Locales"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppSourceDir}\Themes\Packs\*"; DestDir: "{app}\Themes\Packs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppSourceDir}\IconPacks\*"; DestDir: "{app}\IconPacks"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.ru.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Messages]
russian.BeveledLabel=Provix — файловый менеджер для Windows
english.BeveledLabel=Provix — file manager for Windows
