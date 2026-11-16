# fuaran-core-compute — "drop into the repository, run one command, the thing works".
# Formats (rather than checks) the F# sources, then sweeps, builds and tests. ./verify.ps1 is the
# gate; this is the inner loop.
#
#   pwsh ./run.ps1                 full pass
#   pwsh ./run.ps1 -SkipFormat     skip the Fantomas pass
#   pwsh ./run.ps1 -SkipBuild      skip the build (implies a prior build)
#   pwsh ./run.ps1 -SkipTests      skip the suite
#   pwsh ./run.ps1 -Proofs         also run the F* proof leg once (installs the pinned prover)
#   pwsh ./run.ps1 -Configuration Release   build and test in Release (the gate's Release leg)
#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $Proofs,
    # Phase 404 - the configuration built and tested, as `./verify.ps1 -Configuration` takes it.
    # Debug by default; Release is what ships, and an optimiser-sensitive answer shows only there.
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Seeded GLOBALLY, for the reason verify.ps1 gives: a skipped stage must not read as green, and a
# script-scope copy would shadow the exit code a native command sets.
$global:LASTEXITCODE = 0

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipFormat) {
    dotnet fantomas src tests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

pwsh -NoProfile -File ./gates/check-publication-boundary.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Phase 604 (DECISIONS.md D163) - the pipeline wire specification's corpus is a build input: the suite
# certifies against it and FAILS without it. FUARAN_PIPELINE_SPEC names a checkout; unset, it is the
# directory `Fuaran-Pipeline-Specification` beside this repository's MAIN working tree (git's common
# dir, so a linked worktree resolves it too). Checked here, before the build, so a missing clone is one
# line naming the remedy rather than a red suite twenty minutes later.
$global:LASTEXITCODE = 0
$pipelineSpec = $env:FUARAN_PIPELINE_SPEC
if ([string]::IsNullOrWhiteSpace($pipelineSpec)) {
    $commonDir = git rev-parse --path-format=absolute --git-common-dir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $pipelineSpec = Join-Path (Split-Path -Parent (Split-Path -Parent $commonDir)) 'Fuaran-Pipeline-Specification'
}
if (-not (Test-Path (Join-Path $pipelineSpec 'wire-fixtures/manifest.json'))) {
    Write-Host "==== the pipeline wire specification's corpus is ABSENT at '$pipelineSpec'" -ForegroundColor Red
    Write-Host '     git clone https://github.com/Fuaran-Core/fuaran-pipeline-specification <that path>, or set FUARAN_PIPELINE_SPEC' -ForegroundColor Red
    exit 1
}
$env:FUARAN_PIPELINE_SPEC = $pipelineSpec

if (-not $SkipBuild) {
    $global:LASTEXITCODE = 0
    dotnet build Fuaran.Core.Compute.slnx --nologo -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    $global:LASTEXITCODE = 0
    dotnet run --project tests/Fuaran.Core.Compute.Tests --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Proofs) {
    pwsh ./proofs/check.ps1 -Runs 1 -SkipOracleHost
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
