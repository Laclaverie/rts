<#
.SYNOPSIS
    Runs the project's tests and returns a non-zero exit code if any fail.

.DESCRIPTION
    Two axes, and they are independent.

    WHERE a test runs (CONTRIBUTING §2):
      dotnet/RTS.Headless.slnx    Sim and Content, outside Unity. The working loop.
      Assets/Game/Tests/EditMode  Only what needs to be inside the editor. Needs Unity running.

    WHAT KIND it is (ARCHITECTURE §8.1, §8.2):
      Unit         no I/O, no clock, no environment. Red means the code is wrong.
      Functional   touches the filesystem, a shipped balance file, later a replay corpus.
                   Red usually means the code is fine and the world around it changed.
      Flaky        real signal, not trustworthy enough to gate: timing, performance, anything
                   that depends on what else the machine was doing. Opt-in only.

    With no switches both kinds run headlessly, because both are milliseconds today and a
    complete fast loop is worth more than a marginally faster one. Name a kind to run it
    alone; that is the point of the split.

.EXAMPLE
    .\Run-Tests.ps1                  # both kinds, headless
    .\Run-Tests.ps1 -Unit            # unit only
    .\Run-Tests.ps1 -Functional      # functional only
    .\Run-Tests.ps1 -Flaky           # the opt-in ones; never part of a default or -All run
    .\Run-Tests.ps1 -All             # both kinds, plus the Unity suite
    .\Run-Tests.ps1 -Unity -Launch   # editor suite, starting Unity if it is closed
#>
[CmdletBinding()]
param(
    [switch]$Unit,
    [switch]$Functional,
    [switch]$Flaky,
    [switch]$Unity,
    [switch]$All,
    [switch]$Launch,
    [int]$UnityTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Solution = Join-Path $RepoRoot 'dotnet/RTS.Headless.slnx'

# A kind named explicitly narrows the run; naming none runs both gating kinds.
$kindNamed = $Unit -or $Functional -or $Flaky
$runUnit = $All -or -not $kindNamed -or $Unit
$runFunctional = $All -or -not $kindNamed -or $Functional
$runUnity = $Unity -or $All

# Flaky is never part of a default or -All run. It is real signal that is not trustworthy
# enough to gate a build, so it is opt-in only and cannot turn the tree red by accident.
$runFlaky = $Flaky

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

function Get-TestCount($category) {
    # Locale-independent: count the fully qualified names the runner lists, rather than
    # matching an English or French "no tests matched" sentence.
    $listed = & dotnet test $Solution --nologo --list-tests --filter "TestCategory=$category" 2>&1
    return ([string[]]($listed | Select-String -Pattern '^\s+RTS\.' -AllMatches)).Count
}

function Invoke-Headless($category, [switch]$AllowEmpty) {
    Write-Section "Headless - $category"

    if ($AllowEmpty -and (Get-TestCount $category) -eq 0) {
        # An opt-in category is allowed to be empty; a gating one is not.
        Write-Host "  no $category tests" -ForegroundColor DarkGray
        return
    }

    & dotnet test $Solution --nologo --filter "TestCategory=$category"

    # For a gating category, a filter that matches nothing exits 1 and that is a real failure:
    # it means the category was renamed or every fixture in it lost its tag.
    if ($LASTEXITCODE -ne 0) { $script:failures += $category.ToLowerInvariant() }
}

# --------------------------------------------------------------- headless

if ($runUnit) { Invoke-Headless 'Unit' }
if ($runFunctional) { Invoke-Headless 'Functional' }
if ($runFlaky) { Invoke-Headless 'Flaky' -AllowEmpty }

# ------------------------------------------------------------------ unity

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
