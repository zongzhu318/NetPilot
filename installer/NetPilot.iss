; NetPilot installer (Inno Setup 6)
; Build: powershell -ExecutionPolicy Bypass -File installer\build.ps1

#define AppName "NetPilot"
#define AppDisplayName "NetPilot 网络调试工具"
#define AppVersion "1.0.0"
#define AppPublisher "NetPilot"
#define AppExeName "NetPilot.exe"

[Setup]
AppId={{9FF75882-0074-47BE-82D9-8F3EC8F172FA}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\NetPilot
DefaultGroupName=NetPilot
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=NetPilot-Setup-{#AppVersion}
SetupIconFile=..\src\NetPilot.App\Assets\netpilot-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppDisplayName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppDisplayName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppDisplayName}"; Flags: nowait postinstall skipifsilent
