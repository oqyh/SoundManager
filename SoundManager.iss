#define MyApp "SoundManager"
#define MyVersion "1.0.0"
#define MyPublisher "GoldKingZ"
#define MyExe "SoundManager.exe"

#include "CodeDependencies.iss"

[Setup]
AppId={{B7E4D9A2-3F6C-4E81-A5D0-9C2F8B14E7A3}}
AppName={#MyApp}
AppVersion={#MyVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\{#MyApp}
DefaultGroupName={#MyApp}
UninstallDisplayIcon={app}\{#MyExe}
OutputDir=release\SoundManager_Setup
OutputBaseFilename={#MyApp}-Setup
SetupIconFile=icon.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=admin

[Files]
Source: "release\SoundManager_Portable\SoundManager.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyApp}";            Filename: "{app}\{#MyExe}"
Name: "{group}\Uninstall {#MyApp}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyApp}";      Filename: "{app}\{#MyExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyExe}"; Description: "Launch {#MyApp} now"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  Dependency_AddDotNet80Desktop;
  Result := True;
end;