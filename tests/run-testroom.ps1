<#
  Test room: runs every check for the project and prints a summary.

    .\tests\run-testroom.ps1            # everything
    .\tests\run-testroom.ps1 -SkipLive  # without the tests that talk to the running plugin
    .\tests\run-testroom.ps1 -SkipNetwork  # without the read-only tests against the real GitHub

  The live tests need Logi Options+ running with the plugin loaded. They close the desktop app first (it holds a pipe
  connection) and make the mouse buzz briefly. Exit code is 0 only when nothing failed.
#>
param([switch]$SkipLive, [switch]$SkipNetwork)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$results = New-Object System.Collections.Generic.List[object]

function Add-Result($name, $status, $detail) {
    $results.Add([pscustomobject]@{ Check = $name; Status = $status; Detail = $detail })
    $color = @{ PASS = 'Green'; FAIL = 'Red'; SKIP = 'Yellow' }[$status]
    Write-Host ("[{0}] {1}" -f $status, $name) -ForegroundColor $color
    if ($detail) { Write-Host "       $detail" -ForegroundColor DarkGray }
}

function Run-DotnetTest($name, $filter) {
    $out = & dotnet test tests\HapticGamingBridge.Tests --filter $filter --nologo 2>&1 | Out-String
    $summary = ($out -split "`r?`n" | Where-Object { $_ -match 'Superati|Passed!|Failed!|Non superati' } | Select-Object -Last 1)
    if ($LASTEXITCODE -eq 0) { Add-Result $name 'PASS' $summary.Trim() }
    else {
        $failed = ($out -split "`r?`n" | Where-Object { $_ -match '\[FAIL\]' } | ForEach-Object { $_.Trim() }) -join "`n       "
        Add-Result $name 'FAIL' ($summary.Trim() + "`n       " + $failed)
    }
}

function Source-Files($patterns) {
    git ls-files --cached --others --exclude-standard -- $patterns 2>$null | Where-Object { $_ -notmatch '(^|/)(bin|obj)/' }
}

# 1. Logic and input handling (no dependencies).
Run-DotnetTest 'Unit tests: engine, profiles, converter, updater, settings' 'Category!=Live&Category!=Network'

# 2. Security tests against the real plugin.
if ($SkipLive) { Add-Result 'Live plugin security tests' 'SKIP' 'skipped with -SkipLive' }
else {
    Get-Process HapticGamingBridge -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $out = & dotnet test tests\HapticGamingBridge.Tests --filter 'Category=Live' --nologo 2>&1 | Out-String
    $line = ($out -split "`r?`n" | Where-Object { $_ -match 'Superati|Passed!|Failed!|Non superati' } | Select-Object -Last 1)
    if ($out -match 'Ignorati:\s+[1-9]|Skipped:\s+[1-9]') { Add-Result 'Live plugin security tests' 'SKIP' 'plugin pipe not reachable: start Options+ with the plugin loaded' }
    elseif ($LASTEXITCODE -eq 0) { Add-Result 'Live plugin security tests' 'PASS' $line.Trim() }
    else {
        $failed = ($out -split "`r?`n" | Where-Object { $_ -match '\[FAIL\]' } | ForEach-Object { $_.Trim() }) -join "`n       "
        Add-Result 'Live plugin security tests' 'FAIL' ($line.Trim() + "`n       " + $failed)
    }
}

# 2b. The update service against the real GitHub (read only; skipped when offline or rate limited).
if ($SkipNetwork) { Add-Result 'Update service against the real GitHub' 'SKIP' 'skipped with -SkipNetwork' }
else {
    $out = & dotnet test tests\HapticGamingBridge.Tests --filter 'Category=Network' --nologo 2>&1 | Out-String
    $line = ($out -split "`r?`n" | Where-Object { $_ -match 'Superati|Passed!|Failed!|Non superati' } | Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0) {
        $failed = ($out -split "`r?`n" | Where-Object { $_ -match '\[FAIL\]' } | ForEach-Object { $_.Trim() }) -join "`n       "
        Add-Result 'Update service against the real GitHub' 'FAIL' ($line.Trim() + "`n       " + $failed)
    }
    elseif ($out -cmatch 'Superati:\s+0\.') { Add-Result 'Update service against the real GitHub' 'SKIP' 'GitHub not reachable from here' }
    else { Add-Result 'Update service against the real GitHub' 'PASS' $line.Trim() }
}

# 3. Known vulnerabilities in NuGet dependencies (needs network).
$vulnerable = @()
foreach ($proj in 'app\app.csproj', 'client\client.csproj', 'tests\HapticGamingBridge.Tests\HapticGamingBridge.Tests.csproj', 'HapticGamingBridgePlugin\src\HapticGamingBridgePlugin.csproj') {
    $json = & dotnet list $proj package --vulnerable --include-transitive --format json 2>$null | Out-String
    try {
        $data = $json | ConvertFrom-Json
        foreach ($p in $data.projects) { foreach ($f in $p.frameworks) { foreach ($pkg in @($f.topLevelPackages) + @($f.transitivePackages)) {
            if ($pkg -and $pkg.vulnerabilities) { $vulnerable += "$($pkg.id) $($pkg.resolvedVersion) in $proj" }
        } } }
    } catch { $vulnerable += "could not read result for $proj" }
}
if ($vulnerable.Count -eq 0) { Add-Result 'NuGet vulnerability scan' 'PASS' 'no known vulnerable packages' }
else { Add-Result 'NuGet vulnerability scan' 'FAIL' ($vulnerable -join "`n       ") }

# 4. Secrets committed to the repository.
$secretPattern = 'gh[pousr]_[A-Za-z0-9]{36,}|AKIA[0-9A-Z]{16}|-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,}|(password|passwd|secret|api[_-]?key|token)\s*[:=]\s*[''"][^''"]{6,}'
$hits = Source-Files '*' | Where-Object { $_ -ne 'tests/run-testroom.ps1' -and $_ -notmatch '\.(png|ico|exe|dll)$' } |
    ForEach-Object { Select-String -Path $_ -Pattern $secretPattern -CaseSensitive:$false | ForEach-Object { "$($_.Path):$($_.LineNumber)" } }
if (-not $hits) { Add-Result 'Secret scan of tracked files' 'PASS' 'nothing that looks like a credential' }
else { Add-Result 'Secret scan of tracked files' 'FAIL' ($hits -join "`n       ") }

# 5. Shipped code must not reach the network or launch other programs, except in two files:
#    app/Updates.cs  the update check and download from GitHub (network, and it starts the downloaded installer)
#    app/Links.cs    opens an allow-listed set of pages in the browser (starts a process, no network)
#    Nothing may switch off certificate checks. Both files have their own unit tests.
$netApis = 'System\.Net\.(Http|Sockets|WebClient|WebSockets)|HttpClient|WebClient|WebRequest|TcpClient|UdpClient|SocketsHttpHandler'
$launchApis = 'Process\.Start\(|ProcessStartInfo|ShellExecute|cmd\.exe|powershell'
$tlsBypass = 'ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator|ServicePointManager|CheckCertificateRevocationList\s*=\s*false|SslProtocols\.None'
$code = Source-Files @('app/*.cs', 'HapticGamingBridgePlugin/src/*.cs', 'HapticGamingBridgePlugin/src/**/*.cs')
$hits = $code | ForEach-Object {
    $pattern = $netApis + '|' + $launchApis + '|' + $tlsBypass
    if ($_ -eq 'app/Updates.cs') { $pattern = $tlsBypass }
    elseif ($_ -eq 'app/Links.cs') { $pattern = $netApis + '|' + $tlsBypass }
    Select-String -Path $_ -Pattern $pattern | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
}
if (-not $hits) { Add-Result 'Network and process launching only in the updater and the link opener' 'PASS' "$(@($code).Count) source files checked; no certificate checks switched off" }
else { Add-Result 'Network and process launching only in the updater and the link opener' 'FAIL' ($hits -join "`n       ") }

# 5b. Every web address written in the app or plugin belongs to a short list of hosts.
$allowedHosts = 'github.com', 'api.github.com', 'paypal.me', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com', 'github-releases.githubusercontent.com'
$urlFiles = Source-Files @('app/*.cs', 'app/*.xaml', 'HapticGamingBridgePlugin/src/*.cs', 'HapticGamingBridgePlugin/src/**/*.cs')
$strange = foreach ($f in $urlFiles) {
    Select-String -Path $f -Pattern 'https?://[^\s"''<>)\]}]+' -AllMatches | ForEach-Object {
        if ($_.Line -match 'xmlns|schemas\.microsoft\.com|opensource\.org') { return }
        foreach ($m in $_.Matches) {
            $h = ([uri]($m.Value -replace '\{.*$', '')).Host
            if ($h -and ($allowedHosts -notcontains $h)) { "$($_.Path):$($_.LineNumber): $($m.Value)" }
        }
    }
}
if (-not $strange) { Add-Result 'Every web address in the code is on the allowed list' 'PASS' "hosts: $($allowedHosts -join ', ')" }
else { Add-Result 'Every web address in the code is on the allowed list' 'FAIL' ($strange -join "`n       ") }

# 6. Repository hygiene: no build output or oversized files tracked.
$tracked = git ls-files --cached --others --exclude-standard
$bad = @($tracked | Where-Object { $_ -match '(^|/)(bin|obj|publish|dist)/' })
$big = @($tracked | Where-Object { (Test-Path $_) -and (Get-Item $_).Length -gt 5MB })
if ($bad.Count -eq 0 -and $big.Count -eq 0) { Add-Result 'Repository hygiene' 'PASS' "$(@($tracked).Count) files in the repo, none are build output or larger than 5 MB" }
else { Add-Result 'Repository hygiene' 'FAIL' ((@($bad) + @($big)) -join "`n       ") }

Write-Host ""
$results | Format-Table Check, Status -AutoSize
$failed = @($results | Where-Object Status -eq 'FAIL').Count
if ($failed -gt 0) { Write-Host "$failed check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "No failures." -ForegroundColor Green
