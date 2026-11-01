# The website

A small static site in [`site/`](../site) (English at `/`, Italian at `/it/`): what the app is, a download button, how to check the download, a FAQ, the **Buy me a Ko-fi** button and the link to GitHub. It has no build step, no framework and no external fonts, scripts or images.

## How the download button stays current

The page does not hard-code a version. `assets/app.js` asks `api.github.com` for the latest release **from the visitor's browser** and fills in the version, date, size, SHA-256 and the links to the installer, the plugin and `SHA256SUMS.txt`. What it receives is validated first: the tag must be `vX.Y.Z`, and every download address must be exactly `https://github.com/giovannimazza/haptic-bridge/releases/download/<tag>/<file>`. It is written into the page with `textContent`, never as HTML.

Without JavaScript, or if GitHub cannot be reached, the buttons simply point at the releases page. So publishing a release is all it takes to update the site: nothing has to be redeployed.

The installer is **not** copied onto the server. It is always downloaded from GitHub, which is also where the checksum comes from.

## What the pages promise, and what stops them from breaking it

- **A strict Content-Security-Policy** (a `<meta>` tag in every page, and a header from the server that also adds `frame-ancestors 'none'`): scripts and styles only from the site itself, images only from the site itself, and the only outside connection allowed is `https://api.github.com`. That is why the pages have no inline `style=` attributes or inline scripts; spacing helpers live in `style.css`.
- No cookies, no analytics, no third-party requests. The footer says that the browser asks GitHub for the latest release, so GitHub sees the visitor's address.
- `tests/HapticGamingBridge.Tests/SiteTests.cs` checks all of this on every test run: no inline styles, scripts or event handlers, nothing loaded from another site, outgoing links only to this project's GitHub and the donation page, the policy in the pages equal to the one in `nginx.conf` without `unsafe-inline`, the English and Italian pages with the same structure, every referenced file present, and a script that never writes HTML from network data.

## Running it

`site/deploy.ps1` copies the site to a Linux machine over SSH and runs it in a locked-down Docker container (`nginxinc/nginx-unprivileged`): read-only filesystem, no capabilities, no privileges, limited memory and processes, no access log. It only touches `~/haptic-site` and the container `haptic-site`, and it listens on `127.0.0.1:8090` of that machine only.

```powershell
.\site\deploy.ps1 -Server user@host
.\site\deploy.ps1 -Server user@host -Port 8090
$env:HAPTIC_SITE_SERVER = 'user@host'; .\site\deploy.ps1      # or set the server once
```

The server address is not stored in the repository, which is public.

Running it is safe to repeat and never exposes anything by itself. Useful commands on the server:

```sh
docker logs haptic-site          # errors only; successful requests are not logged
docker restart haptic-site
docker rm -f haptic-site         # stop and remove (the files stay in ~/haptic-site)
```

## Making it reachable

The deploy step leaves the site reachable only on the server itself. How it is exposed is a separate choice, because it decides who can see it:

- **Only your devices** (Tailscale Serve): reachable from machines in the same tailnet.
- **The whole Internet** (Tailscale Funnel, or any reverse proxy): anyone with the address can open it.
- **GitHub Pages**: `.github/workflows/pages.yml` publishes the pages (not `nginx.conf` or the deploy script) on every change to `site/` on `main`. The pages use only relative links and a meta policy, so no server is needed. Once, in the repository settings, Pages must be set to use "GitHub Actions" as its source.

Check what a machine already exposes before adding anything (`tailscale serve status`), and never put a service on a public port that another service already uses.

## Checks after a deploy

```sh
curl -sI http://127.0.0.1:8090/            # 200, with Content-Security-Policy and the other headers
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8090/it/
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8090/assets/style.css
```

Files copied from Windows arrive with private permissions and the container cannot read them; `deploy.ps1` fixes the permissions, and a `403` on `/it/` or `/assets/...` after a manual copy means that step was skipped.
