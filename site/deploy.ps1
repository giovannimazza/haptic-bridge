<#
  Publishes the site to a Linux machine that has Docker, over SSH (key login, no password), and (re)starts the container.
  It only touches ~/haptic-site and the container called haptic-site on that machine.

    .\site\deploy.ps1 -Server user@host
    .\site\deploy.ps1 -Server user@host -Port 8090

  Or set the environment variable HAPTIC_SITE_SERVER once and run it without arguments. The address is deliberately not
  written in this file: the repository is public.

  The container listens on 127.0.0.1:<Port> of the server only. Making it reachable is a separate step
  (see docs/website.md), so running this never exposes anything by itself.
#>
param(
    [string]$Server = $env:HAPTIC_SITE_SERVER,
    [int]$Port = 8090
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Server)) { throw 'Say where to publish: -Server user@host, or set HAPTIC_SITE_SERVER.' }
if ($Server -notmatch '^[A-Za-z0-9._-]+@[A-Za-z0-9._:-]+$') { throw "'$Server' does not look like user@host." }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be between 1024 and 65535.' }
$site = $PSScriptRoot
foreach ($tool in 'ssh', 'scp') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool not found" } }
foreach ($f in 'index.html', 'it\index.html', 'assets\style.css', 'assets\app.js', 'nginx.conf') { if (-not (Test-Path (Join-Path $site $f))) { throw "missing $f in $site" } }

Write-Host "Copying the site to $Server ..."
ssh -o BatchMode=yes $Server 'mkdir -p ~/haptic-site/www'
# Replace the content instead of merging, so a file deleted here is deleted there too.
ssh -o BatchMode=yes $Server 'rm -rf ~/haptic-site/www/* && echo cleaned'
scp -q -r (Join-Path $site 'index.html') (Join-Path $site 'robots.txt') (Join-Path $site 'it') (Join-Path $site 'assets') "${Server}:haptic-site/www/"
scp -q (Join-Path $site 'nginx.conf') "${Server}:haptic-site/nginx.conf"

# The remote steps go in a script file (copied byte for byte) because quoting through ssh and PowerShell is fragile.
$remote = @"
set -e
cd ~/haptic-site
# Files copied from Windows arrive with private permissions; the container's user must be able to read them.
find www -type d -exec chmod 755 {} +
find www -type f -exec chmod 644 {} +
chmod 644 nginx.conf
IMG=nginxinc/nginx-unprivileged:alpine
docker image inspect "`$IMG" >/dev/null 2>&1 || docker pull -q "`$IMG" >/dev/null
docker rm -f haptic-site >/dev/null 2>&1 || true
docker run -d --name haptic-site --restart unless-stopped \
  --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges \
  --memory 64m --pids-limit 50 \
  -p 127.0.0.1:${Port}:8080 \
  -v "`$HOME/haptic-site/www:/usr/share/nginx/html:ro" \
  -v "`$HOME/haptic-site/nginx.conf:/etc/nginx/nginx.conf:ro" \
  "`$IMG" >/dev/null
sleep 2
code=`$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:${Port}/)
echo "container is up; home page answers `$code on 127.0.0.1:${Port}"
[ "`$code" = "200" ]
"@
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'hb-site-deploy.sh'
[IO.File]::WriteAllText($tmp, ($remote -replace "`r", ''), (New-Object System.Text.UTF8Encoding $false))
scp -q $tmp "${Server}:/tmp/hb-site-deploy.sh"
ssh -o BatchMode=yes $Server 'bash /tmp/hb-site-deploy.sh; rc=$?; rm -f /tmp/hb-site-deploy.sh; exit $rc'
Remove-Item $tmp -ErrorAction SilentlyContinue
Write-Host 'Done.'
