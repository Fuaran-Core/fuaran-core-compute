#Requires -Version 7.0
# THE PROOF LEG'S REFUSALS, HELD TO THEIR EXIT CODES — Phase 221.
#
#     pwsh ./proofs/kit/check-proof-leg.tests.ps1
#
# `check-proof-leg.ps1` printed `==== proofs: green` and exited 0 over a host build that did not
# exist (MSB1009), and over a model with a type error. Both failures were DIAGNOSED — MSBuild and F*
# each said so in the log — and neither was REFUSED. The cause was one line (`$LASTEXITCODE = 0` at
# script scope, which shadows the automatic variable once the leg is invoked with `&`); the header
# of `check-proof-leg.ps1` says where it was and why it must not come back.
#
# So this script runs the leg THE WAY A CALLER DOES — `& check-proof-leg.ps1 @args`, in this
# process, exactly as every `proofs/check.ps1` does — against a scratch proofs directory holding
# models small enough to check in a second, and holds every step to its EXIT CODE. The message is
# read too, but only to assert that `proofs: green` is ABSENT: the finding was that the message and
# the exit code both disagreed with reality, so pinning the text alone would let the pair drift
# apart again.
#
# FOUR ARMS. The first is the control that makes the other three mean something:
#
#   A. GREEN CONTROL — a true model, no host step: exit 0 AND `proofs: green`. If this is red, the
#                      scratch apparatus is broken and a red B–D would prove nothing.
#   B. HOST BUILD    — a -HostProjectFile that does not exist: exit non-zero, no green.
#   C. HOST RUN      — a host project that builds and cannot run the filter: exit non-zero, no green.
#   D. CHECK         — a model with a type error: exit non-zero, no green.
#
# It needs the pinned prover. Where there is none it says NOT RUN and exits 2 — never 0, because
# "nothing was checked" must not read as "everything held". `proofs/check.ps1` runs it after a
# green leg, when the prover is by construction present.
[CmdletBinding()]
param(
    # The adopter's proofs directory: where the pin, and the prover `check.ps1` installed, live.
    [string] $ProofsDir,
    # The leg under test. Defaults to the engine beside this file; pointing it at an older copy is
    # how the go-red half of the acceptance is demonstrated.
    [string] $Kit,
    # Scratch. Created fresh and removed at exit.
    [string] $WorkDir
)

$ErrorActionPreference = 'Stop'

if (-not $ProofsDir) { $ProofsDir = Split-Path $PSScriptRoot -Parent }
$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not $Kit) { $Kit = Join-Path $PSScriptRoot 'check-proof-leg.ps1' }
$Kit = [System.IO.Path]::GetFullPath($Kit)
if (-not $WorkDir) { $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) "check-proof-leg-tests-$PID" }

$pinFile = Join-Path $ProofsDir 'fstar-pin.json'
$pinnedVersion = (Get-Content $pinFile -Raw | ConvertFrom-Json).fstar.TrimStart('v')

# The prover, resolved the way the leg resolves it, WITHOUT the leg's download: a test that fetched
# a 100 MB release as a side effect would be a surprise, and `check.ps1` has already fetched it.
$fstarHome = $null
if ($env:FSTAR_HOME) { $fstarHome = $env:FSTAR_HOME }
elseif (Test-Path (Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe')) { $fstarHome = Join-Path $ProofsDir '.fstar/fstar' }
if (-not $fstarHome -or -not (Test-Path (Join-Path $fstarHome 'bin/fstar.exe'))) {
    Write-Host "==== leg-tests: NOT RUN — no pinned prover. Set FSTAR_HOME to an F* $pinnedVersion release, or run ``pwsh ./proofs/check.ps1`` once to install it under proofs/.fstar/." -ForegroundColor Yellow
    exit 2
}

$script:failures = [System.Collections.Generic.List[string]]::new()
$script:cases = 0

function Assert-That([string] $what, [bool] $holds, [string] $detail = '') {
    $script:cases++
    if ($holds) { Write-Host "  PASS  $what$(if ($detail) { " — $detail" })" -ForegroundColor Green }
    else {
        $script:failures.Add($what)
        Write-Host "  FAIL  $what$(if ($detail) { " — $detail" })" -ForegroundColor Red
    }
}

# ---- the scratch proofs directory ----------------------------------------------------------------

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
$scratch = Join-Path $WorkDir 'proofs'
New-Item -ItemType Directory -Force $scratch | Out-Null
Copy-Item $pinFile (Join-Path $scratch 'fstar-pin.json')

# Two models: one true, one refuted by a plain type error (F* Error 19).
Set-Content (Join-Path $scratch 'LegGood.fst') "module LegGood`n`nlet one : nat = 1`n"
Set-Content (Join-Path $scratch 'LegBad.fst') "module LegBad`n`nlet minus_one : nat = -1`n"

# Budgets for both, with floors of 0 — a module that checks in a second must not trip the floor.
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegBad'; budgetSeconds = 60; floorSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.json')

# A host project that BUILDS (two empty targets, no SDK, no restore) and cannot RUN — `dotnet run`
# refuses a project with no runnable output type, exit 1. Arm C needs exactly that pair.
$hostDir = Join-Path $WorkDir 'host'
New-Item -ItemType Directory -Force $hostDir | Out-Null
Set-Content (Join-Path $hostDir 'Host.proj') '<Project><Target Name="Restore" /><Target Name="Build" /></Project>'

$hostFilters = @(@{ Filter = 'Leg.Host'; Failure = 'the scratch host is RED' })

# ---- running the leg the way a caller does -------------------------------------------------------

# In THIS process, with `&`, which is the shape that shadowed the exit code. Every stream is
# captured so the transcript can be searched; the exit code is read from the GLOBAL automatic
# variable, which is the only one this script never assigns.
function Invoke-Leg([hashtable] $legArgs) {
    $env:FSTAR_HOME = $fstarHome
    Push-Location $WorkDir
    $threw = $null
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit @legArgs *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch {
        # A terminating error is a refusal too — as `pwsh -File` it is exit 1 — but it is named,
        # so a leg that has started THROWING where it used to exit is visible here.
        $threw = $_.ToString()
        $lines = @($threw)
        $code = 1
    }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Green = [bool]($lines -match '==== proofs: green'); Lines = $lines; Threw = $threw }
}

function Show-Tail($result) {
    $verdict = @($result.Lines -match '^==== proofs: ') | Select-Object -Last 1
    if ($result.Threw) { "threw: $($result.Threw)" } elseif ($verdict) { $verdict } else { '(no verdict line)' }
}

$base = @{ ProofsDir = $scratch; RepoRoot = $WorkDir; Runs = 1 }

# ---- A. GREEN CONTROL ----------------------------------------------------------------------------

$a = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood') })
Assert-That 'A. GREEN CONTROL — a true model with no host step exits 0' ($a.Exit -eq 0) "exit $($a.Exit): $(Show-Tail $a)"
Assert-That 'A. GREEN CONTROL — and prints proofs: green' $a.Green (Show-Tail $a)
if ($a.Exit -ne 0 -or -not $a.Green) {
    Write-Host '==== leg-tests: the GREEN CONTROL is red, so the scratch apparatus is broken and arms B–D would prove nothing. Stopping.' -ForegroundColor Red
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

# ---- B. HOST BUILD -------------------------------------------------------------------------------

$b = Invoke-Leg ($base + @{
        Modules = @('LegGood'); ProofOnly = @('LegGood')
        HostProject = 'nosuch'; HostProjectFile = 'nosuch/NoSuchProject.fsproj'; HostFilters = $hostFilters
    })
Assert-That 'B. HOST BUILD — a host project that does not exist exits NON-ZERO' ($b.Exit -ne 0) "exit $($b.Exit): $(Show-Tail $b)"
Assert-That 'B. HOST BUILD — and does not print proofs: green' (-not $b.Green) (Show-Tail $b)

# ---- C. HOST RUN ---------------------------------------------------------------------------------

$c = Invoke-Leg ($base + @{
        Modules = @('LegGood'); ProofOnly = @('LegGood')
        HostProject = 'host'; HostProjectFile = 'host/Host.proj'; HostFilters = $hostFilters
    })
Assert-That 'C. HOST RUN — a host filter that cannot run exits NON-ZERO' ($c.Exit -ne 0) "exit $($c.Exit): $(Show-Tail $c)"
Assert-That 'C. HOST RUN — and does not print proofs: green' (-not $c.Green) (Show-Tail $c)

# ---- D. CHECK ------------------------------------------------------------------------------------

$d = Invoke-Leg ($base + @{ Modules = @('LegBad'); ProofOnly = @('LegBad') })
Assert-That 'D. CHECK — a model with a type error exits NON-ZERO' ($d.Exit -ne 0) "exit $($d.Exit): $(Show-Tail $d)"
Assert-That 'D. CHECK — and does not print proofs: green' (-not $d.Green) (Show-Tail $d)

Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue

if ($script:failures.Count -gt 0) {
    Write-Host "==== leg-tests: RED — $($script:failures.Count) of $script:cases assertion(s) failed; the leg reported a step it could not run as green" -ForegroundColor Red
    exit 1
}
Write-Host "==== leg-tests: $script:cases assertions held — every step's failure is REFUSED, not merely printed" -ForegroundColor Green
exit 0
