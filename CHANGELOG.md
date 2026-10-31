# Changelog

All notable changes. Versions are the GitHub releases; the section at the top is not released yet.

## Unreleased

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
