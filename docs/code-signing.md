# Code signing with SignPath Foundation (free)

[SignPath Foundation](https://signpath.org) signs open source projects for free. The certificate belongs to the Foundation, so Windows shows **SignPath Foundation** as the publisher. This document covers what is already set up in the repository and the steps only the maintainer can do.

## Already in the repository

| Requirement | Where |
|---|---|
| OSI-approved license, no dual licensing | `LICENSE` (MIT) |
| Automated, verifiable build | `.github/workflows/release.yml` |
| Same product name and version in every signed file | `app/app.csproj` (`Product`, `Version`) and the `VersionInfo*` lines in `installer/HapticGamingBridge.iss`; both are checked by the artifact configurations |
| Artifact configurations | `docs/signpath/app.xml`, `docs/signpath/installer.xml` |
| Code signing policy and privacy policy on the project page | `README.md` |
| Only own binaries are signed | `app.xml` lists `HapticGamingBridge.exe` and `HapticGamingBridge.dll`; Microsoft runtime files are already signed by Microsoft |

## Steps only you can do

1. **Check you meet the criteria.** The Foundation requires an actively maintained project that has already been released, with a verifiable reputation for executables. This project is new, so acceptance is not guaranteed. Read the current [terms](https://signpath.org/terms) first.
2. **Enable multi-factor authentication** on your GitHub account (and on SignPath once you have an account). All team members need it.
3. **Apply** at <https://signpath.org/apply>. Link the repository and the release page.
4. **After approval, in SignPath:**
   - Create a project with the slug `haptic-bridge`.
   - Add the trusted build system **GitHub.com** to your organization and link it to the project.
   - Create two artifact configurations from the XML files in `docs/signpath/`, with the slugs `app` and `installer`.
   - Create a signing policy with the slug `release-signing`, with **manual approval** required for each request. The Foundation requires this.
   - Create an API token for a user who may submit to that policy.
5. **In GitHub** (repository settings, Secrets and variables, Actions):
   - Secret `SIGNPATH_API_TOKEN` = the API token.
   - Variable `SIGNPATH_ORGANIZATION_ID` = your SignPath organization ID.
6. **Release:** push a tag such as `v1.0.1`. The workflow runs the tests, builds the app, submits the app files for signing, builds the installer with the signed files, submits the installer, and creates the GitHub release. You approve each signing request in SignPath.
7. **Update `README.md`**: change the status line under "Code signing policy" once the first signed release is out.

If you chose different slugs in SignPath, change them in the workflow (`project-slug`, `signing-policy-slug`, `artifact-configuration-slug`).

## Notes and limits

- The workflow only signs when the secret exists and the run is for a tag. Without the secret it still builds, and a tag still creates a release, with an **unsigned** installer.
- The Logi plugin package (`installer/plugin/HapticGamingBridge_1_0.lplug4`) is built locally and committed, because the plugin needs Logi's `PluginApi.dll` and cannot be built on a CI runner. Rebuild and recommit it whenever the plugin source changes:
  ```powershell
  dotnet build HapticGamingBridgePlugin\src -c Release
  logiplugintool pack .\HapticGamingBridgePlugin\bin\Release\ .\installer\plugin\HapticGamingBridge_1_0.lplug4
  logiplugintool verify .\installer\plugin\HapticGamingBridge_1_0.lplug4
  dotnet build HapticGamingBridgePlugin\src    # restores the Debug development link
  ```
  The package is not a Windows executable and is not signed by SignPath.
- I could not check the attribute names in the artifact configurations or the workflow inputs against a live SignPath account. They follow SignPath's published documentation for the GitHub action (v3) and the artifact configuration syntax; if SignPath rejects a request, its error message names the field to fix.
- A global keyboard and mouse hook can look like a keylogger to a reviewer. The README says plainly what the hook does and does not do, and the test room checks that the app contains no network code.
