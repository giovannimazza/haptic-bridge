; Build with: ISCC installer\HapticBridge.iss  (after `dotnet publish` into .\publish and packing the plugin into .\dist)
#define AppName "Haptic Bridge"
#define AppVersion "1.0.0"
#define PluginFile "Haptic_1_0.lplug4"

[Setup]
AppId={{6E1266AA-120C-4A08-90C4-B432667A9CE4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=giovannimazza
AppPublisherURL=https://github.com/giovannimazza/haptic-bridge
AppSupportURL=https://github.com/giovannimazza/haptic-bridge/issues
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Per-user install, no admin prompt.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=HapticBridge-Setup-{#AppVersion}
SetupIconFile=..\app\icon.ico
UninstallDisplayIcon={app}\HapticBridge.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "installplugin"; Description: "Install the Logi Options+ plugin (opens the Logi plugin installer)"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\dist\{#PluginFile}"; DestDir: "{app}\plugin"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\HapticBridge.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\HapticBridge.exe"; Tasks: desktopicon

[Run]
; The Logi Plugin Service registers .lplug4, so opening the file starts its own install flow.
Filename: "{app}\plugin\{#PluginFile}"; Tasks: installplugin; Flags: shellexec
Filename: "{app}\HapticBridge.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
