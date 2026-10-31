<#
  Test room: runs every check for the project and prints a summary.

    .\tests\run-testroom.ps1            # everything
    .\tests\run-testroom.ps1 -SkipLive  # without the tests that talk to the running plugin

  The live tests need Logi Options+ running with the plugin loaded. They close the desktop app first (it holds a pipe
  connection) and make the mouse buzz briefly. Exit code is 0 only when nothing failed.
#>
param([switch]$SkipLive)

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
    $out = & dotnet test tests\HapticBridge.Tests --filter $filter --nologo 2>&1 | Out-String
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
Run-DotnetTest 'Unit tests: engine and profile loading' 'Category!=Live'

# 2. Security tests against the real plugin.
if ($SkipLive) { Add-Result 'Live plugin security tests' 'SKIP' 'skipped with -SkipLive' }
else {
    Get-Process HapticBridge -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $out = & dotnet test tests\HapticBridge.Tests --filter 'Category=Live' --nologo 2>&1 | Out-String
    $line = ($out -split "`r?`n" | Where-Object { $_ -match 'Superati|Passed!|Failed!|Non superati' } | Select-Object -Last 1)
    if ($out -match 'Ignorati:\s+[1-9]|Skipped:\s+[1-9]') { Add-Result 'Live plugin security tests' 'SKIP' 'plugin pipe not reachable: start Options+ with the plugin loaded' }
    elseif ($LASTEXITCODE -eq 0) { Add-Result 'Live plugin security tests' 'PASS' $line.Trim() }
    else {
        $failed = ($out -split "`r?`n" | Where-Object { $_ -match '\[FAIL\]' } | ForEach-Object { $_.Trim() }) -join "`n       "
        Add-Result 'Live plugin security tests' 'FAIL' ($line.Trim() + "`n       " + $failed)
    }
}

# 3. Known vulnerabilities in NuGet dependencies (needs network).
$vulnerable = @()
foreach ($proj in 'app\app.csproj', 'client\client.csproj', 'tests\HapticBridge.Tests\HapticBridge.Tests.csproj', 'HapticPlugin\src\HapticPlugin.csproj') {
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

# 5. Shipped code must not reach the network or launch other programs.
$risky = 'System\.Net\.(Http|Sockets|WebClient|WebSockets)|HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Process\.Start\(|ProcessStartInfo|ShellExecute|cmd\.exe|powershell'
$code = Source-Files @('app/*.cs', 'HapticPlugin/src/*.cs', 'HapticPlugin/src/**/*.cs')
$hits = $code | ForEach-Object { Select-String -Path $_ -Pattern $risky | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" } }
if (-not $hits) { Add-Result 'No network or process-launch APIs in app and plugin' 'PASS' "$(@($code).Count) source files checked" }
else { Add-Result 'No network or process-launch APIs in app and plugin' 'FAIL' ($hits -join "`n       ") }

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
