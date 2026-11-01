# Changelog

All notable changes. Versions are the GitHub releases. A section marked "not released yet" is work on `main` that has no release.
The version number is set in `app/app.csproj` and `installer/HapticGamingBridge.iss`; the release workflow refuses a tag that does not match it.

## Not released yet

- **Ko-fi**: a second, separate support button next to *Buy me a coffee* (PayPal), in the app header, the website and the README. The app may open only this project's GitHub pages and the maintainer's PayPal and Ko-fi pages.

## 1.2.1

- **New app icon**: a mouse in the centre with controller grips at the sides and vibration waves, in the app, the installer and the Logi plugin package. The plugin package was rebuilt for the new icon, so it is no longer identical to the one in 1.2.0. Its code is unchanged.
- The build no longer shows a warning about an obsolete .NET call.
- This is the first release that reaches people through the in-app updater: a 1.2.0 installation offers it in the banner, downloads it, checks its checksum and restarts into it.

## 1.2.0

- **Automatic updates**: the app looks for a newer release on GitHub and offers it in a banner. *Update now* downloads the installer, checks its SHA-256 against the checksum GitHub published for that release file, and installs it; the app restarts by itself. *Skip this version* and *What's new* are there too. Under **Info > Updates** you can check by hand, turn the automatic check off (the app then makes no network connections at all), or let it install updates when it starts. See [docs/updates.md](docs/updates.md).
- The privacy notes now say what the update check does on the network.
- Release builds also publish a `SHA256SUMS.txt`, and the release workflow refuses a tag that does not match the version in `app/app.csproj`.
- The app accepts a close request from an installer or Windows instead of hiding to the tray, so installing over a running copy no longer needs the app to be killed.
- To update from 1.1.0 or older, install this version once by hand: those versions cannot update themselves. After that, updates arrive through the app. The Logi plugin is unchanged since 1.0.1.

## 1.1.0

- **Fire rate converter**: type a weapon's rounds per minute in the action editor and the cooldown is set for you, and the other way round. Quick buttons for 300, 600, 900 and 1200.
- **More exact held fire**: shots are scheduled on a fixed grid and the repeat timer is finer, so the average rate matches the configured one. Before, an 80 ms cooldown fired about every 100 ms.
- **Info tab** with a short guide, privacy note and links to the project on GitHub.
- **Buy me a coffee** button in the header and a banner in the README.
- **Per-profile switch** to turn a game off without deleting it.
- **Rename profiles**: pencil next to the name, double-click on the name, or F2. "Change app" keeps a name you chose.
- New app icon in the app, dark scrollbars, and the header lined up with the sidebar.
- Without the plugin the app drops haptic events instead of queueing them, and shows **Plugin ready** / **Plugin not found**.

## 1.0.1

- Renamed to Haptic Gaming Bridge; new gamepad icon.
- Plugin pipe restricted to the current Windows user, with limits on event names; profile file validated (unknown events, cooldown range, network paths, size) and saved atomically.
- Test room with unit tests, live security tests, dependency and secret scans.
- Installer built by GitHub Actions; not code-signed.

## 1.0.0

- First release: desktop app with per-game profiles and actions, Logi Options+ plugin, Windows installer.
