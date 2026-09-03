<#
.SYNOPSIS
    Runs the project's tests and returns a non-zero exit code if any fail.

.DESCRIPTION
    Two suites answer two different questions (CONTRIBUTING §2):

      dotnet/RTS.Headless.slnx   Sim and Content, outside Unity, ~100ms. The working loop.
      Assets/Game/Tests/EditMode Only what needs to be inside the editor. Seconds, and
                                 requires a running Unity.

    Headless runs by default because it is the one worth running constantly. -Unity adds
    the editor suite; -All runs both and is what CI should call.

.EXAMPLE
    .\Run-Tests.ps1                 # headless only
    .\Run-Tests.ps1 -All            # both suites
    .\Run-Tests.ps1 -Unity -Launch  # editor suite, starting Unity if it is closed
#>
[CmdletBinding()]
param(
    [switch]$Unity,
    [switch]$All,
    [switch]$Launch,
    [int]$UnityTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Solution = Join-Path $RepoRoot 'dotnet/RTS.Headless.slnx'

$runHeadless = -not $Unity -or $All
$runUnity = $Unity -or $All

$failures = @()

function Write-Section($text) {
    Write-Host ''
    Write-Host "== $text " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(0, 60 - $text.Length)) -ForegroundColor DarkCyan
}

function Resolve-UnityCli {
    $onPath = Get-Command unity -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $fallback = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
    if (Test-Path $fallback) { return $fallback }

    return $null
}

# ---------------------------------------------------------------- headless

if ($runHeadless) {
    Write-Section 'Headless (Sim, Content)'

    & dotnet test $Solution --nologo
    if ($LASTEXITCODE -ne 0) { $failures += 'headless' }
}

# ------------------------------------------------------------------- unity

if ($runUnity) {
    Write-Section 'Unity EditMode'

    $cli = Resolve-UnityCli
    if (-not $cli) {
        Write-Host 'Unity CLI not found. Skipping the editor suite.' -ForegroundColor Yellow
        $failures += 'unity-cli-missing'
    }
    else {
        $status = & $cli status --json --no-banner 2>$null | ConvertFrom-Json
        $ready = $status.data.instances |
            Where-Object { $_.project -eq $RepoRoot -and $_.state -eq 'ready' }

        if (-not $ready -and $Launch) {
            Write-Host 'Starting Unity...' -ForegroundColor Yellow
            & $cli open $RepoRoot --no-banner | Out-Null

            $deadline = (Get-Date).AddSeconds($UnityTimeoutSeconds)
            while (-not $ready -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 5
                $status = & $cli status --json --no-banner 2>$null | ConvertFrom-Json
                $ready = $status.data.instances |
                    Where-Object { $_.project -eq $RepoRoot -and $_.state -eq 'ready' }
            }
        }

        if (-not $ready) {
            # Not a pass. The editor suite covers what the headless one structurally cannot,
            # so silently skipping it would report green on an untested half.
            Write-Host 'No Unity editor is running for this project.' -ForegroundColor Yellow
            Write-Host 'Open it, or re-run with -Launch.' -ForegroundColor Yellow
            $failures += 'unity-not-running'
        }
        else {
            $raw = & $cli cmd run_tests --mode EditMode --timeout $UnityTimeoutSeconds --json --no-banner
            $result = ($raw | ConvertFrom-Json).data.result
            $summary = $result.Summary

            foreach ($test in $result.Results | Where-Object { $_.Status -ne 'Passed' }) {
                Write-Host ("  {0}: {1}" -f $test.Status, $test.FullName) -ForegroundColor Red
                if ($test.Message) { Write-Host "    $($test.Message)" -ForegroundColor DarkGray }
            }

            $colour = if ($summary.Failed -gt 0) { 'Red' } else { 'Green' }
            Write-Host ("  total: {0}  passed: {1}  failed: {2}  skipped: {3}  ({4:N2}s)" -f `
                    $summary.Total, $summary.Passed, $summary.Failed, $summary.Skipped, $result.Duration) `
                -ForegroundColor $colour

            if ($summary.Failed -gt 0 -or $summary.Total -eq 0) { $failures += 'unity' }
        }
    }
}

# ------------------------------------------------------------------ result

Write-Host ''
$failed = $failures.Count -gt 0

if ($failed) { Write-Host "FAILED: $($failures -join ', ')" -ForegroundColor Red }
else { Write-Host 'All suites passed.' -ForegroundColor Green }

# Explorer runs test.cmd as: cmd /c ""C:\path\test.cmd" ". Those doubled quotes mean the
# console closes the instant we exit, so hold it open — but only then. A terminal or CI
# invocation never has them and must not block.
if ($env:RTS_CMDLINE -and $env:RTS_CMDLINE.Contains('""')) {
    Write-Host ''
    Read-Host 'Press Enter to close'
}

if ($failed) { exit 1 }
exit 0
