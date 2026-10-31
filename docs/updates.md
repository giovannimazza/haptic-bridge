# Automatic updates

Haptic Gaming Bridge can look for a newer release of itself on GitHub, show it in a banner, and install it. This page says what exactly happens, what is checked, and what is **not** protected.

## What you see

- A few seconds after the app starts, and then every six hours while it is open, the app asks GitHub for the latest release of this project. If it is newer than the version you run, a banner appears under the header: **Version X is available**, with **Update now**, **What's new** (opens the release page in your browser), **Skip this version** and a close button.
- **Update now** downloads the installer, shows the progress, checks it, starts it, and closes the app. The installer upgrades the app in place and starts it again when it is done. Your profiles and settings are not touched.
- **Info > Updates** has a **Check for updates** button, the result of the last check, and two switches:
  - *Check for updates automatically* (on by default). Turn it off and the app makes **no network connections at all**; the button still works when you press it.
  - *Install updates automatically when the app starts* (off by default). With it on, an update found at startup is downloaded and installed without asking. It is only offered for a copy that was installed with the installer.

A copy that was not put there by the installer (for example one run from a build folder) is never replaced in place: for it, **Update now** becomes **Open release page**.

## What is sent

One HTTPS request to `api.github.com` for the latest release of this repository, and, when you update, one to `github.com` that is redirected to GitHub's release storage for the installer. The request carries the app name and version in the User-Agent and nothing else: no profiles, no input, no identifier, no cookies, no login. GitHub sees your IP address, as any website does.

## How a download is checked

Everything that comes from the network is treated as untrusted. In order:

1. **Only allowed hosts.** The check uses `api.github.com`. The download may only start at `github.com` and may only be redirected to GitHub's release storage hosts. Every address, including every redirect, must be `https`, on the default port, without credentials, on the list. Redirects are followed by hand and capped at five. Anything else is refused before a request is sent.
2. **Only this project's release asset.** The installer must be named exactly `HapticGamingBridge-Setup-<version>.exe` and its address must be exactly `https://github.com/giovannimazza/haptic-bridge/releases/download/<tag>/<that name>`.
3. **A checksum is required.** GitHub publishes a SHA-256 digest for every release asset. A release without one is offered, but the app will not install it: it opens the release page instead.
4. **Size.** The size GitHub lists must be sensible (1 MB to 300 MB). The download must announce that size, never deliver more (reading stops at the limit, so a server cannot fill the disk) and never less.
5. **SHA-256.** The downloaded bytes must hash to the published digest, or the file is deleted.
6. **Signature.** If the installer carries a Windows signature it must be valid (a tampered or broken signature is refused). An unsigned installer is accepted today, because releases are not signed yet; see below.
7. **A safe place to run it.** It is saved in its own new folder under the temp directory, and the app only starts a file that sits exactly there with exactly that name. It is started silently, with a progress window, without administrator rights (the installer is per-user), without reinstalling the Logi plugin, and with `/relaunch=1` so it restarts the app afterwards.

If any step fails, nothing is installed, the folder is removed, and the banner says why, with **Try again** and the release page.

## What this does not protect against

- **The release itself.** The checksum comes from GitHub, so it proves the download is the file GitHub holds, not that the file is honest. Someone who gets into the GitHub account of the project could publish a bad release and its checksum. Code signing is the answer to that, and it is not active yet: the SignPath Foundation application is pending ([code-signing.md](code-signing.md)). Until then, updating means trusting this repository, like installing from it by hand.
- **Your machine and your network.** HTTPS and the host list protect against interception on the way; they do not help if the PC is already compromised.
- **The Logi plugin.** An update replaces the app only. The plugin is a separate package; when a release needs a new one, its notes say so and the installer on the release page includes it.

## When releases are signed

Set the policy in `app/Updates.cs`:

```csharp
public static readonly UpdatePolicy Default = new(RequireSignature: true, SignerContains: "SignPath Foundation");
```

Then an installer without a valid signature from that signer is refused. Check the certificate's exact subject text on a signed build first (`Authenticode.SignerSubject(path)`) and use a string that appears in it. Until then `RequireSignature` stays `false`.

## For maintainers

- The release workflow publishes `SHA256SUMS.txt` next to the installer. It is for people who want to check a download by hand (`Get-FileHash`); the app itself uses the digest GitHub attaches to each asset.
- A release is only offered if its tag is a version newer than the running app. Drafts and prereleases are ignored. Publish a normal release with the installer attached, as the workflow does.
- The installer script has a `/relaunch=1` option used only by the updater, and does not reinstall the plugin when run with `/TASKS=""`.
- Settings are in `%APPDATA%\HapticGamingBridge\settings.json`. Leftover installers from an update that did not finish are removed on the next start.

## How it is tested

- **Unit tests with a fake GitHub** (`UpdateServiceTests`): newer, same and older versions, drafts and prereleases, missing or malformed digests, wrong asset names and addresses, implausible sizes, broken JSON, errors and timeouts, redirects to hosts that are not allowed (never requested), redirect loops, tampered or oversized downloads, signature rules. Breaking a defence on purpose makes these fail.
- **Signature tests on real files**: a Microsoft-signed file is valid, a copy with one changed byte is invalid, an unsigned file is unsigned.
- **Network tests against the real GitHub** (read only, `Category=Network`): the real release is understood, and a real download passes the redirect checks. With `HB_NETWORK_DOWNLOAD=1` the whole installer is downloaded and its SHA-256 compared. They skip themselves when offline.
- **Static checks in the test room**: network code only in `app/Updates.cs`, no switched-off certificate checks, every web address in the code on a short list of hosts.
- **By hand, before the first release with the updater**: install an older build, open it, press *Check for updates* and *Update now*, and confirm the version changes and the app restarts. This was done end to end with the real GitHub release.
