# fuaran-core-compute — the repository's verify gate: the "is the repo green" command.
# Format check, publication-boundary sweep, build and the Expecto suite (with its clock leg),
# in that order; a non-zero exit on the first failing stage. `-Proofs` adds the proof leg.
#
#   pwsh ./verify.ps1                    the gate
#   pwsh ./verify.ps1 -Proofs            the gate plus the F* proof leg (installs the pinned prover)
#   pwsh ./verify.ps1 -SkipFormatCheck   skip the Fantomas check
#   pwsh ./verify.ps1 -Configuration Release   the build and the main suite in Release
#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $SkipFormatCheck,
    # The proof leg (proofs/check.ps1): verify proofs/ColumnOps.fst and proofs/Pipeline.fst with the
    # pinned F*/Z3 and hold the committed oracle to a fresh extraction. Opt-in here because it
    # installs a prover; CI's proofs job runs it on every push. The oracle HOST (the Proofs.Oracle
    # differential family) needs no prover and runs in the ordinary suite below, always.
    [switch] $Proofs,
    # Phase 404 - the configuration the solution is built in and the main suite runs in. Debug stays
    # the default, so a contributor's command is unchanged; ci runs both, and publish-packages runs
    # `-Configuration Release` and then packs `--no-build` from that same output, so the bytes the
    # gate verified are the bytes that ship. A Release-only red (an optimiser-sensitive answer, as
    # the window law's NaN sign was) then shows on the commit that caused it, not on a tag push.
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Seeded because `$LASTEXITCODE` is $null until a native command runs, and seeded GLOBALLY: a plain
# `$LASTEXITCODE = 0` creates a script-scope copy that shadows the global one whenever this script
# runs in a child scope (invoked with `&`), and every stage check below would then read a stale 0.
$global:LASTEXITCODE = 0

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipFormatCheck) {
    dotnet fantomas --check src tests
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== verify: fantomas format-check FAILED (run ./run.ps1 to format)' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# The publication boundary: this repository is public, and no tracked file may name anything a
# reader outside it cannot look up. A grep over the tracked set that fails red on a hit.
pwsh -NoProfile -File ./gates/check-publication-boundary.ps1
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: the publication-boundary sweep FAILED' -ForegroundColor Red
    exit $LASTEXITCODE
}

$global:LASTEXITCODE = 0
dotnet build Fuaran.Core.Compute.slnx --nologo -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$global:LASTEXITCODE = 0
dotnet run --project tests/Fuaran.Core.Compute.Tests --no-build -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The clock leg (Phase 282): the cases whose claim is about TIME, alone in their own process after
# the main suite (which no longer contains them), each red only if red on three attempts. The leg
# fails if it ran fewer cases than its inventory, so a filter cannot pass it vacuously.
# It runs a RELEASE build (operator decision 2026-09-28): a bound on the clock is a claim about the
# code consumers run, and a Debug build's timings are not that code's. Under `-Configuration Release`
# that is the build above, reused; under Debug the test project is built in Release for this leg.
if ($Configuration -ne 'Release') {
    $global:LASTEXITCODE = 0
    dotnet build tests/Fuaran.Core.Compute.Tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$global:LASTEXITCODE = 0
dotnet run --project tests/Fuaran.Core.Compute.Tests -c Release --no-build -- --clock-leg
if ($LASTEXITCODE -eq 3) {
    # Phase 285: no verdict, not a red - a case found no unsaturated calibration window in its budget.
    Write-Host '==== verify: the clock leg reached NO VERDICT (machine saturated) - not a timing red; re-run when the machine is quieter' -ForegroundColor Yellow
    exit $LASTEXITCODE
}
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: the clock leg FAILED (a timing case red on all three counted attempts, or cases missing)' -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($Proofs) {
    pwsh ./proofs/check.ps1 -Runs 3 -SkipOracleHost
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== verify: the proof leg FAILED (see proofs/README.md)' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host "==== verify: fuaran-core-compute green ($Configuration)" -ForegroundColor Green
exit 0
