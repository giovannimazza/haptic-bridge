<p align="center">
  <a href="https://paypal.me/giovanbattistamazza"><img src="docs/assets/buy-me-a-coffee.svg" alt="Buy me a coffee" height="48"></a>
</p>

# Haptic Gaming Bridge

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
 | global hooks, |  HapticGamingBridge| Service, C#)     |                  | Options+)     |
 | profiles      |                 |                  |                  |               |
 +---------------+                 +------------------+                  +---------------+
```

- **Desktop app** (`app/`): installs global low-level input hooks, detects the foreground game by executable name, applies the active profile (trigger, modifier, cooldown, hold-to-repeat) and sends an event name down the pipe.
- **Plugin** (`HapticGamingBridgePlugin/`): declares a fixed set of haptic events and maps each one to a built-in Logitech waveform. It reads event names from the pipe and calls `PluginEvents.RaiseEvent`.
- **Test client** (`client/`): a tiny console tool that sends one event repeatedly, used to measure timing.

## Features

- Profiles per game, with icon, running indicator and a foreground marker
- Actions with trigger (key or mouse button), haptic pattern, required modifier (Shift, Control, Alt, Left Mouse, Right Mouse), cooldown and hold-to-repeat
- Global on/off switch and a per-profile switch to turn a game off without deleting it, test button for each pattern, tray icon, dark UI, an Info tab with a short guide
- Profiles stored as JSON in `%APPDATA%\HapticGamingBridge\profiles.json`

## Requirements

- Windows 11 (the app uses Windows-only APIs; custom title bar colors need Windows 11)
- Logitech MX Master 4
- Logi Options+ 1.95 or later (Plugin API 6.2.1+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the installed `PluginApi.dll` targets .NET 10)
- Logi Plugin Tool: `dotnet tool install --global LogiPluginTool --version 6.1.4.22672` (version 6.4.2 failed to install on the SDK used here)

## Install

Download `HapticGamingBridge-Setup-1.0.0.exe` from the [latest release](https://github.com/giovannimazza/haptic-bridge/releases/latest) and run it. It installs the desktop app for the current user (no administrator prompt) and, if you leave the task ticked, opens the Logi installer for the Options+ plugin. The plugin package is also attached to the release as `HapticGamingBridge_1_0.lplug4` if you prefer to install it by double-clicking.

The installer is not code-signed, so Windows SmartScreen may warn before running it.

## Build and run

Plugin (building also links it into the Logi Plugin Service and reloads it):

```powershell
cd HapticGamingBridgePlugin
dotnet build src
```

Desktop app:

```powershell
cd app
dotnet build
.\bin\Debug\net10.0-windows\HapticGamingBridge.exe
```

Quick check without a game: run the app, create a profile for any open program, add an action, then press the mapped input in that program. The status pill in the header shows whether the plugin has received events.

Logic self-check (cooldown, repeat, modifiers, global switch):

```powershell
.\app\bin\Debug\net10.0-windows\HapticGamingBridge.exe --selftest
Get-Content $env:TEMP\haptic-gaming-bridge-selftest.txt   # prints "ok"
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

To add one: declare it in `HapticGamingBridgePlugin/src/package/events/DefaultEventSource.yaml`, map it in `events/extra/eventMapping.yaml`, and add it to `Events.Patterns` in `app/Models.cs`.

## Limits and known issues

- **Timing.** The mouse merges or drops events that arrive too fast. In a raw test, events 600 ms apart all arrived, while at 400 ms only 3 of 5 did. Hold-to-repeat with a 75 ms cooldown felt natural by ear, but it is a perceptual choice, not a measured guarantee. The first event after a pause arrives slightly late. The repeat timer ticks every 50 ms, so that is the practical minimum.
- **Not continuous.** Each trigger is a short pulse. The SDK has no call to keep the motor running, so a held button cannot feel like a constant vibration.
- **Fast weapons.** Automatic fire cannot be reproduced shot by shot; use hold-to-repeat with a cooldown.
- **Elevated games.** Windows does not deliver input to a normal process's hooks while an administrator-level window has focus. Run the app as administrator for games started that way.
- **Anti-cheat.** The app uses global input hooks. Anti-cheat software may object to that. Not tested.
- **Pipe security.** Only the current Windows user can open the pipe, event names are limited to 64 identifier characters, and up to 4 clients can connect at once. Any program running as you can still make the mouse vibrate.
- **Plugin needs the app.** The plugin alone does nothing.
- **Windows only.** There is no macOS version.

## Packaging

```powershell
cd HapticGamingBridgePlugin
dotnet build src -c Release
logiplugintool pack .\bin\Release\ ..\dist\HapticGamingBridge_1_0.lplug4
logiplugintool verify ..\dist\HapticGamingBridge_1_0.lplug4
```

Building in Release repoints the development link to `bin\Release`; run `dotnet build src` again to restore the Debug link.

Before submitting to the Logi Marketplace, the package metadata still needs real values (display name, description, author, support and home page URLs). The Marketplace guidelines require your own EULA, MIT or Apache-2.0 for open source dependencies, and manual approval of each submission. They do not say whether a plugin that depends on a companion app is allowed; ask marketplace@logitech.com.

## With and without the plugin

The app and the plugin are separate. The app never needs the plugin to run, and it does not queue anything up when the plugin is missing.

| | Plugin running in Logi Options+ | Plugin missing (not installed, or Options+ not running) |
|---|---|---|
| Header pill | green, **Plugin ready** | orange, **Plugin not found** |
| Profiles, actions, editor | work | work |
| Pressing a mapped key in the game | the mouse vibrates | nothing happens, no error, no lag |
| Test button in the editor | the mouse vibrates | nothing happens |
| Holding a button for a long time | one pulse per cooldown | nothing is stored: events are dropped, not queued |
| Plugin starts while you play | the next press vibrates; old presses are **not** replayed | |
| Plugin restarts (Options+ restarted) | the app reconnects on its own, the first press after the restart may be lost | |

The pill only says a plugin is listening. It does not know whether the mouse is connected and has haptics enabled in Options+.

### Trying both cases by hand

1. **With the plugin**: install both (the installer does), start Logi Options+, start the app. The pill turns green. Open the editor, press and hold **Test**: the mouse buzzes.
2. **Without the plugin**: remove or stop it. For a development link, delete `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\HapticGamingBridge.link` and end the `LogiPluginService` process (Options+ restarts it). Within about two seconds the pill turns orange. Map a key, play, hold it down: nothing vibrates and the game does not stutter.
3. **Back with the plugin**: put the plugin back (`dotnet build HapticGamingBridgePlugin\src` recreates the link). The pill turns green again and the next press vibrates immediately, without a burst of old events.

The unit tests in `PipeSenderTests` check the same behaviour automatically on a private pipe, so they never touch your real plugin.

## Tests

```powershell
.	ests
un-testroom.ps1            # everything
.	ests
un-testroom.ps1 -SkipLive  # without the tests that talk to the running plugin
```

The test room runs:

- **Unit tests** (`tests/HapticGamingBridge.Tests`): input engine (cooldown, hold-to-repeat, modifiers, global switch, extreme values), the pipe sender with and without a plugin (nothing blocks, nothing piles up, no replay of old events, reconnect), and loading of `profiles.json`, which is treated as untrusted input: malformed or oversized files, unknown event names, out-of-range cooldowns, and executable paths pointing at network shares.
- **Live security tests** against the plugin running in Logi Options+: malformed and oversized event names, binary garbage, an undeclared event name, a silent connection that must not block other clients, a burst of events, and the pipe's access list (current user only). They need Options+ running with the plugin loaded and close the desktop app first. They make the mouse buzz briefly.
- **NuGet vulnerability scan** of all projects.
- **Secret scan** of the files in the repository.
- **Static check** that the app and plugin contain no network or process-launching APIs.
- **Repository hygiene**: no build output or oversized files.

Exit code is 0 only when nothing failed.

## Code signing policy

**Status: not signed yet.** The application to SignPath Foundation is still to be submitted, so current releases are unsigned and Windows SmartScreen may warn. Once the project is accepted, the policy below applies to every signed release.

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Only binaries built from this repository by the [release workflow](.github/workflows/release.yml) are signed, and each signing request needs manual approval. Setup notes for maintainers are in [docs/code-signing.md](docs/code-signing.md).

| Role | Member |
|---|---|
| Authors | [giovannimazza](https://github.com/giovannimazza) |
| Reviewers | [giovannimazza](https://github.com/giovannimazza) |
| Approvers | [giovannimazza](https://github.com/giovannimazza) |

### Privacy policy

This program does not transfer any information to other networked systems. It contains no network code. To work, it installs a global keyboard and mouse hook so it can see key presses and mouse buttons; it only compares them with the actions you configured, and does not record, store or send them. It reads the name and path of the program in the foreground to select a profile. The only thing it saves is your profiles, in `%APPDATA%\HapticGamingBridge\profiles.json`. It talks to the Logi Options+ plugin over a local named pipe that only your Windows account can open. The Info tab has links to this repository and to a PayPal donation page; they open in your browser, and the app never contacts those sites itself. The Logi Options+ software and the haptic hardware have their own privacy policies.

## Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet publish app -c Release -r win-x64 --self-contained true -o publish
ISCC installer\HapticGamingBridge.iss
```

The output is `dist\HapticGamingBridge-Setup-1.0.0.exe`. The app is published self-contained, so users do not need to install .NET. The plugin package it bundles is `installer/plugin/HapticGamingBridge_1_0.lplug4`; rebuild it after plugin changes (see [docs/code-signing.md](docs/code-signing.md)). The same build runs on GitHub Actions when you push a tag.

## Layout

```
app/            WPF desktop app (profiles, input hooks, pipe client)
HapticGamingBridgePlugin/   Logi plugin (pipe server, event declarations)
client/         console tool that sends a test event
installer/      Inno Setup script and the bundled plugin package
docs/           maintainer notes and SignPath artifact configurations
.github/        release workflow
tests/          unit and security tests, and run-testroom.ps1
```

## License

[MIT](LICENSE).
