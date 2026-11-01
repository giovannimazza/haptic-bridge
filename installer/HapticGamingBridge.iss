; Build with: ISCC installer\HapticGamingBridge.iss  (after `dotnet publish` into .\publish). The plugin package in installer\plugin is built locally
; with the Logi Plugin Tool, because the plugin needs Logi's PluginApi.dll and cannot be built on CI.
#define AppName "Haptic Gaming Bridge"
; CI overrides the version with /DAppVersion=<tag>
#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif
#define PluginFile "HapticGamingBridge_1_0.lplug4"

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
OutputBaseFilename=HapticGamingBridge-Setup-{#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany=giovannimazza
VersionInfoDescription={#AppName} Setup
SetupIconFile=..\app\icon.ico
UninstallDisplayIcon={app}\HapticGamingBridge.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; The app is started again by the [Run] entry below when an update asks for it; do not let Windows do it as well.
RestartApplications=no

[Tasks]
Name: "installplugin"; Description: "Install the Logi Options+ plugin (opens the Logi plugin installer)"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "plugin\{#PluginFile}"; DestDir: "{app}\plugin"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\HapticGamingBridge.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\HapticGamingBridge.exe"; Tasks: desktopicon

[Run]
; The Logi Plugin Service registers .lplug4, so opening the file starts its own install flow.
Filename: "{app}\plugin\{#PluginFile}"; Tasks: installplugin; Flags: shellexec
Filename: "{app}\HapticGamingBridge.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
; The in-app updater runs this installer silently with /relaunch=1 and expects the app to come back by itself.
Filename: "{app}\HapticGamingBridge.exe"; Flags: nowait runasoriginaluser; Check: IsRelaunch

[Code]
// True when the installer was started with /relaunch=1, which only the in-app updater does.
function IsRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;
