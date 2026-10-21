#Requires -Version 7.0
# fuaran-core-compute — the proof leg, on the proof kit's pinned release asset (Phase 259; the
# asset since the kit's 0.40.0). The three declarations below are this repository's; nothing else
# in this file, and nothing in the kit, is per-repository.
#
# THE KIT IS A PINNED RELEASE ASSET. `proofs-kit.json` names it (kit, asset, url, sha256); the block
# below the declarations fetches it into proofs/.kit/ (or takes the local file -KitAsset /
# FUARAN_PROOFS_KIT_ASSET names: the offline path), REFUSES it unless its sha256 is the pin's, unpacks
# it fresh, and runs the engine from it with the asset's prover pin, its read-only models (`Limits`,
# and the shared `Twin` it opens) and its runtime floor, which `oracle/` commits and the leg holds to
# the asset byte for byte. Nothing of the kit is copied into this repository.
#
# THE ENGINE IS `kit/check-proof-leg.ps1` in the asset and this file is the caller: it declares what THIS
# repository has — the models, where the oracle host lives, which test families the host step runs
# — and the kit runs the leg. Read the kit script's header for what the three steps are and why
# each is shaped the way it is; read `kit/README.md` for how a repository adopts the kit.
#
# The flags this file forwards are the leg's public surface:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the host families to the repository's own gate, which runs the suite
#   -Strict          the recorded run: a declaration defect is red (a cost overrun stays a finding)
#   -CacheDir <dir>  put the checked-module cache somewhere you name
#   -KitAsset <file> the kit asset as a local file instead of the pin's url (verified all the same)
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [string] $CacheDir,
    [int]    $Runs = 1,
    [string] $KitAsset
)

$ErrorActionPreference = 'Stop'

# ---- DECLARATION 1: the models -------------------------------------------------------------------
# Each is proofs/<name>.fst with its committed extraction at proofs/oracle/<name>.fs. Order matters
# only where one model `open`s another: a model must follow what it opens. Give each one a comment
# saying what it models and what it proves — the list is the first thing a reader of the leg meets.
#
# Adding a model is adding its name here AND a budget entry to modules.json: nothing else is
# per-module. Keep this as ONE literal line if your repository parses it (see the note below).
# Limits: WIRE_FORMAT section 21's resource limits as named premises (copied from the kit's
#   repository; Pipeline opens it). ColumnOps: the columnar op algebra — totality, all-or-nothing
#   rejection, the dry run's agreement, well-formedness preserved, invert's round trip, Diff.toOps
#   reconstructs. Pipeline: the counted pipeline driver and its expression evaluator — totality,
#   budget monotonicity, the work bounded by the expression size under the section-21.8 limit.
#   Incremental: the row-local incremental refresh over Pipeline's frame — priming is full
#   evaluation, a refresh against a primed state under a truthful delta is the prime over the new
#   source, and so agrees with full evaluation (opens Pipeline).
#   Relational: the twelve relational verbs' output schema and refusals, and the static schema
#   walk — the walk is sound for the evaluator's schema, a join and a window over distinct names
#   answer distinct names, every unknown column is refused in the arm's order, and the four
#   verbs that can repeat a name are the finding (opens Pipeline).
#   Frame: the dense frame and its kernels — read back cell by cell it is the table's logical
#   rows at every column type, with and without a validity mask, and a filter the comparison
#   kernels answer is Pipeline's row-major Filter step over the read-back (opens Pipeline).
#   MaintainedSort: the maintained Sort — the binary-insertion merge of the moved rows into the
#   prior produced order, under the comparator with the arrival position as the tie-break, is the
#   full stable sort (the unique sorted permutation); and the comparator is a total preorder over
#   the cells a typed column holds (opens Pipeline and Incremental).
#   IncrementalMaintained: the incremental refresh past the row-local class — the maintained
#   GroupBy, Project, Limit, Window, Sort and the filtering joins — priming is full evaluation over
#   the relational model's reference, a refresh against a primed state is the prime, output and
#   state, and so agrees with full evaluation (opens Pipeline, Incremental, Relational and
#   MaintainedSort).
$modules = @('Limits', 'ColumnOps', 'Pipeline', 'Incremental', 'Relational', 'Frame', 'MaintainedSort', 'IncrementalMaintained')
# The kit's shared twin record, which `Limits` opens: checked first and extracted like any model, but
# no model's subject, so it carries no ladder row and is not on the `$modules` line the ladder reads.
$kitShared = @('Twin')

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
    @{
        Filter  = 'Proofs.Ladder'
        Failure = 'the claims ladder (Proofs.Ladder) is RED — ../proofs.json and this tree disagree; the failing row is named above'
    }
    @{
        Filter  = 'Proofs.Coverage'
        Failure = 'the coverage declaration (Proofs.Coverage) is RED — a packable package is neither modelled nor excluded, or an exclusion outlived its reason'
    }
)

# ---- DECLARATION 3: where the host lives ---------------------------------------------------------
# Paths relative to the repository root (the parent of proofs/, unless you pass -RepoRoot).
$hostProject = 'tests/Fuaran.Core.Compute.Tests'
$hostProjectFile = 'tests/Fuaran.Core.Compute.Tests/Fuaran.Core.Compute.Tests.fsproj'

# ---- nothing below here is per-repository ----------------------------------------------------------

# ---- the kit: the pinned release asset, verified before anything runs ------------------------------
$kitPinFile = Join-Path $PSScriptRoot 'proofs-kit.json'
$kitPin = if (Test-Path $kitPinFile) { Get-Content $kitPinFile -Raw | ConvertFrom-Json } else { $null }
if ($null -eq $kitPin -or $kitPin.kind -ne 'proofsKitPin' -or -not $kitPin.asset -or -not $kitPin.url -or $kitPin.sha256 -notmatch '^[0-9a-f]{64}$') {
    Write-Host "==== proofs: $kitPinFile is not a kit pin (kind proofsKitPin, kit, asset, url, a 64-hex sha256)" -ForegroundColor Red
    exit 2
}
$kitRoot = Join-Path $PSScriptRoot '.kit'
New-Item -ItemType Directory -Force $kitRoot | Out-Null
$archive = Join-Path $kitRoot $kitPin.asset
if (-not $KitAsset) { $KitAsset = $env:FUARAN_PROOFS_KIT_ASSET }
if ($KitAsset) {
    $kitSource = [System.IO.Path]::GetFullPath($KitAsset)
    Copy-Item $kitSource $archive -Force
}
else {
    $kitSource = $kitPin.url
    $held = (Test-Path $archive) -and (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $kitPin.sha256
    if (-not $held) { Invoke-WebRequest -Uri $kitPin.url -OutFile $archive -UseBasicParsing }
}
$kitSha = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($kitSha -ne $kitPin.sha256) {
    Remove-Item $archive -Force
    Write-Host "==== proofs: REFUSED the kit asset from $kitSource — its sha256 is $kitSha, the pin ($kitPinFile) is $($kitPin.sha256). Nothing ran." -ForegroundColor Red
    exit 2
}
# Unpacked fresh per invocation, so this run reads exactly the verified bytes; dead runs' trees are swept.
foreach ($stale in @(Get-ChildItem $kitRoot -Directory -Filter 'run-*' -ErrorAction SilentlyContinue)) {
    $stalePid = 0
    if ([int]::TryParse($stale.Name.Substring(4), [ref] $stalePid) -and $stalePid -ne $PID -and -not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
        Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$kit = Join-Path $kitRoot "run-$PID"
if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
New-Item -ItemType Directory -Force $kit | Out-Null
[System.Formats.Tar.TarFile]::ExtractToDirectory($archive, $kit, $false)
Write-Host "==== proofs: kit $($kitPin.kit) ($($kitPin.asset), sha256 $kitSha) from $kitSource" -ForegroundColor Cyan
# The host step inherits this: a ladder family holding a row about a kit model reads that model's text
# from the verified asset here, since the model is not in this tree (kit/README.md, "Importing a theorem").
$env:FUARAN_PROOFS_KIT_DIR = $kit


$legArgs = @{
    Modules         = $kitShared + $modules
    ProofsDir       = $PSScriptRoot
    PinFile         = Join-Path $kit 'fstar-pin.json'
    ModelDirs       = @(Join-Path $kit 'models')
    FloorDir        = Join-Path $kit 'oracle'
    HostProject     = $hostProject
    HostProjectFile = $hostProjectFile
    HostFilters     = $hostFilters
    Runs            = $Runs
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof. Measured
# both ways before this template was written; do not "simplify" it to a dot-source.
& (Join-Path $kit 'kit/check-proof-leg.ps1') @legArgs
if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE; Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue; exit $code }

# Then the engine's own refusals, run the same way (Phase 221): a scratch leg over a failed host
# build, a host filter that cannot run and a refuted model must each exit non-zero, beside a green
# control. A few seconds, and after the leg so the prover is already installed. Needs nothing from
# this file's declarations — keep it as it is.
& (Join-Path $kit 'kit/check-proof-leg.tests.ps1') -ProofsDir $PSScriptRoot -PinFile (Join-Path $kit 'fstar-pin.json')
$testsExit = $LASTEXITCODE
Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue
exit $testsExit
