#Requires -Version 7.0
# <REPO> — the proof leg. TEMPLATE: copy to <repo>/proofs/check.ps1 and edit the three declarations
# below. Nothing else in this file needs changing, and nothing in `kit/check-proof-leg.ps1` does.
#
# THE ENGINE IS `kit/check-proof-leg.ps1` and this file is the caller: it declares what THIS
# repository has — the models, where the oracle host lives, which test families the host step runs
# — and the kit runs the leg. Read the kit script's header for what the three steps are and why
# each is shaped the way it is; read `kit/README.md` for how a repository adopts the kit.
#
# The flags this file forwards are the leg's public surface:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the host families to the repository's own gate, which runs the suite
#   -Strict          promote every cost finding to a red leg
#   -NoFloor         do not enforce the per-module time floors declared in modules.json
#   -CacheDir <dir>  put the checked-module cache somewhere you name
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

# ---- DECLARATION 1: the models -------------------------------------------------------------------
# Each is proofs/<name>.fst with its committed extraction at proofs/oracle/<name>.fs. Order matters
# only where one model `open`s another: a model must follow what it opens. Give each one a comment
# saying what it models and what it proves — the list is the first thing a reader of the leg meets.
#
# Adding a model is adding its name here AND a budget entry to modules.json: nothing else is
# per-module. Keep this as ONE literal line if your repository parses it (see the note below).
$modules = @('FirstModel')

# ---- DECLARATION 2: the host families --------------------------------------------------------------
# One entry per test-runner invocation, each with its own failure message. Separate invocations
# rather than one prefix filter, so two failures read as what they are rather than as one red suite.
# An EMPTY list is a legitimate shape for a repository whose models have no differential yet: the
# leg then checks, extracts and diffs, and says so.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Oracle'
        Failure = 'the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree'
    }
)

# ---- DECLARATION 3: where the host lives ---------------------------------------------------------
# Paths relative to the repository root (the parent of proofs/, unless you pass -RepoRoot).
$hostProject = 'tests/<Your>.Tests'
$hostProjectFile = 'tests/<Your>.Tests/<Your>.Tests.fsproj'

# ---- nothing below here is per-repository ----------------------------------------------------------

$legArgs = @{
    Modules         = $modules
    ProofsDir       = $PSScriptRoot
    HostProject     = $hostProject
    HostProjectFile = $hostProjectFile
    HostFilters     = $hostFilters
    Runs            = $Runs
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($NoFloor) { $legArgs.NoFloor = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof. Measured
# both ways before this template was written; do not "simplify" it to a dot-source.
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.ps1') @legArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Then the engine's own refusals, run the same way (Phase 221): a scratch leg over a failed host
# build, a host filter that cannot run and a refuted model must each exit non-zero, beside a green
# control. A few seconds, and after the leg so the prover is already installed. Needs nothing from
# this file's declarations — keep it as it is.
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.tests.ps1') -ProofsDir $PSScriptRoot
exit $LASTEXITCODE
