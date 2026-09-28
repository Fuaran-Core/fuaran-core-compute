# fuaran-core-compute — the repository's verify gate: the "is the repo green" command.
# Format check, publication-boundary sweep, build, the Expecto suite and the C# facade proof,
# in that order; a non-zero exit on the first failing stage. `-Proofs` adds the proof leg.
#
#   pwsh ./verify.ps1                    the gate
#   pwsh ./verify.ps1 -Proofs            the gate plus the F* proof leg (installs the pinned prover)
#   pwsh ./verify.ps1 -SkipFormatCheck   skip the Fantomas check
#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $SkipFormatCheck,
    # The proof leg (proofs/check.ps1): verify proofs/ColumnOps.fst and proofs/Pipeline.fst with the
    # pinned F*/Z3 and hold the committed oracle to a fresh extraction. Opt-in here because it
    # installs a prover; CI's proofs job runs it on every push. The oracle HOST (the Proofs.Oracle
    # differential family) needs no prover and runs in the ordinary suite below, always.
    [switch] $Proofs
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

dotnet build Fuaran.Core.Compute.slnx --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project tests/Fuaran.Core.Compute.Tests --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The clock leg (Phase 282): the cases whose claim is about TIME, alone in their own process after
# the main suite (which no longer contains them), each red only if red on three attempts. The leg
# fails if it ran fewer cases than its inventory, so a filter cannot pass it vacuously.
# It runs a RELEASE build (operator decision 2026-09-28): a bound on the clock is a claim about the
# code consumers run, and a Debug build's timings are not that code's.
dotnet build tests/Fuaran.Core.Compute.Tests -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project tests/Fuaran.Core.Compute.Tests -c Release --no-build -- --clock-leg
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: the clock leg FAILED (a timing case red on all three attempts, or cases missing)' -ForegroundColor Red
    exit $LASTEXITCODE
}

# The C# facade's conformance report over the dataframe half (`ColExpr`, `Transform`): a C#
# consumer constructs and reads both through Fuaran.Core.DataFrame.CSharp alone, the
# read-then-rebuild round trip is the identity over a generated sample, that sample reaches every
# case of every union it covers, and no public facade member mentions an F# type outside the
# declared bridge. Run from here rather than from the Expecto suite because the claim is about what
# a C# CONSUMER can express, and only C# consumer code can make it.
dotnet run --project tests/Fuaran.Core.DataFrame.CSharp.Proof --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: C# dataframe facade proof FAILED its conformance report' -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($Proofs) {
    pwsh ./proofs/check.ps1 -Runs 3 -SkipOracleHost
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== verify: the proof leg FAILED (see proofs/README.md)' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host '==== verify: fuaran-core-compute green' -ForegroundColor Green
exit 0
