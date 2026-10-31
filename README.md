# Haptic Bridge

Haptic feedback for mouse-and-keyboard games on the **Logitech MX Master 4**.

Pick a game, map keys and mouse buttons to haptic patterns, and the mouse vibrates when you press them. Mappings are stored per game, and the right profile is selected automatically when the game is in the foreground.

> Independent project. Not affiliated with or endorsed by Logitech. It uses the public [Logi Actions SDK](https://logitech.github.io/actions-sdk-docs/).

## How it works

The Actions SDK lets a plugin raise haptic events, but only from the plugin's own code, and plugins cannot see global keyboard and mouse input. So the project is two parts that talk over a local named pipe:

```
 keyboard / mouse
        |
        v
 +---------------+   event name    +------------------+   haptic event   +---------------+
 | Desktop app   | --------------> | Logi plugin      | ---------------> | MX Master 4   |
 | (WPF, tray)   |  named pipe     | (Logi Plugin     |  RaiseEvent      | (via Logi     |
 | global hooks, |  HapticBridge| Service, C#)     |                  | Options+)     |
 | profiles      |                 |                  |                  |               |
 +---------------+                 +------------------+                  +---------------+
```

- **Desktop app** (`app/`): installs global low-level input hooks, detects the foreground game by executable name, applies the active profile (trigger, modifier, cooldown, hold-to-repeat) and sends an event name down the pipe.
- **Plugin** (`HapticPlugin/`): declares a fixed set of haptic events and maps each one to a built-in Logitech waveform. It reads event names from the pipe and calls `PluginEvents.RaiseEvent`.
- **Test client** (`client/`): a tiny console tool that sends one event repeatedly, used to measure timing.

## Features

- Profiles per game, with icon, running indicator and a foreground marker
- Actions with trigger (key or mouse button), haptic pattern, required modifier (Shift, Control, Alt, Left Mouse, Right Mouse), cooldown and hold-to-repeat
- Global on/off switch, test button for each pattern, tray icon, dark UI
- Profiles stored as JSON in `%APPDATA%\HapticBridge\profiles.json`

## Requirements

- Windows 11 (the app uses Windows-only APIs; custom title bar colors need Windows 11)
- Logitech MX Master 4
- Logi Options+ 1.95 or later (Plugin API 6.2.1+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the installed `PluginApi.dll` targets .NET 10)
- Logi Plugin Tool: `dotnet tool install --global LogiPluginTool --version 6.1.4.22672` (version 6.4.2 failed to install on the SDK used here)

## Install

Download `HapticBridge-Setup-1.0.0.exe` from the [latest release](https://github.com/giovannimazza/haptic-bridge/releases/latest) and run it. It installs the desktop app for the current user (no administrator prompt) and, if you leave the task ticked, opens the Logi installer for the Options+ plugin. The plugin package is also attached to the release as `Haptic_1_0.lplug4` if you prefer to install it by double-clicking.

The installer is not code-signed, so Windows SmartScreen may warn before running it.

## Build and run

Plugin (building also links it into the Logi Plugin Service and reloads it):

```powershell
cd HapticPlugin
dotnet build src
```

Desktop app:

```powershell
cd app
dotnet build
.\bin\Debug\net10.0-windows\HapticBridge.exe
```

Quick check without a game: run the app, create a profile for any open program, add an action, then press the mapped input in that program. The status pill in the header shows whether the plugin has received events.

Logic self-check (cooldown, repeat, modifiers, global switch):

```powershell
.\app\bin\Debug\net10.0-windows\HapticBridge.exe --selftest
Get-Content $env:TEMP\haptic-bridge-selftest.txt   # prints "ok"
```

## Haptic patterns

Patterns are fixed. The SDK only offers predefined waveforms, so adding a pattern means adding an event.

| Pattern | Event id | Logitech waveform |
|---|---|---|
| Kick | `recoil_1` | `sharp_collision` |
| Thump | `recoil_2` | `damp_collision` |
| Chime | `interact_1` | `happy_alert` |
| Click | `interact_2` | `sharp_state_change` |
| Step | `move` | `subtle_collision` |
| Buzz | `alert` | `angry_alert` |
| Success | `done` | `completed` |
| Knock | `knock` | `knock` |

To add one: declare it in `HapticPlugin/src/package/events/DefaultEventSource.yaml`, map it in `events/extra/eventMapping.yaml`, and add it to `Events.Patterns` in `app/Models.cs`.

## Limits and known issues

- **Timing.** The mouse merges or drops events that arrive too fast. In a raw test, events 600 ms apart all arrived, while at 400 ms only 3 of 5 did. Hold-to-repeat with a 75 ms cooldown felt natural by ear, but it is a perceptual choice, not a measured guarantee. The first event after a pause arrives slightly late. The repeat timer ticks every 50 ms, so that is the practical minimum.
- **Not continuous.** Each trigger is a short pulse. The SDK has no call to keep the motor running, so a held button cannot feel like a constant vibration.
- **Fast weapons.** Automatic fire cannot be reproduced shot by shot; use hold-to-repeat with a cooldown.
- **Elevated games.** Windows does not deliver input to a normal process's hooks while an administrator-level window has focus. Run the app as administrator for games started that way.
- **Anti-cheat.** The app uses global input hooks. Anti-cheat software may object to that. Not tested.
- **Pipe security.** The pipe accepts one client and has no access control; any local process can make the mouse vibrate. Restrict it to the current user before distributing.
- **Plugin needs the app.** The plugin alone does nothing.
- **Windows only.** There is no macOS version.

## Packaging

```powershell
cd HapticPlugin
dotnet build src -c Release
logiplugintool pack .\bin\Release\ ..\dist\Haptic_1_0.lplug4
logiplugintool verify ..\dist\Haptic_1_0.lplug4
```

Building in Release repoints the development link to `bin\Release`; run `dotnet build src` again to restore the Debug link.

Before submitting to the Logi Marketplace, the package metadata still needs real values (display name, description, author, support and home page URLs). The Marketplace guidelines require your own EULA, MIT or Apache-2.0 for open source dependencies, and manual approval of each submission. They do not say whether a plugin that depends on a companion app is allowed; ask marketplace@logitech.com.

## Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet publish app -c Release -r win-x64 --self-contained true -o publish
# pack the plugin into dist\Haptic_1_0.lplug4 (see Packaging), then:
ISCC installer\HapticBridge.iss
```

The output is `dist\HapticBridge-Setup-1.0.0.exe`. The app is published self-contained, so users do not need to install .NET.

## Layout

```
app/            WPF desktop app (profiles, input hooks, pipe client)
HapticPlugin/   Logi plugin (pipe server, event declarations)
client/         console tool that sends a test event
installer/      Inno Setup script
```

## License

No license file has been added yet. The plugin's package metadata declares MIT.
