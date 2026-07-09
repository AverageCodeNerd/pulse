; Inno Setup script for Pulse — builds Pulse-Setup.exe from the self-contained publish output.
; Build: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Pulse.iss

#define MyAppName "Pulse"
#define MyAppVersion "0.3.0"
#define MyAppPublisher "AverageCodeNerd"
#define MyAppURL "https://github.com/AverageCodeNerd/pulse"
#define MyAppExeName "Pulse.exe"
#define PublishDir "..\src\Pulse\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#define IconFile "..\src\Pulse\assets\pulse.ico"

[Setup]
AppId={{A7F3C2E1-9B4D-4E6A-8C1F-2D5E7A9B0C34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\Programs\Pulse
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Pulse-Setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Pulse"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Pulse"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Pulse"; Flags: nowait postinstall skipifsilent
