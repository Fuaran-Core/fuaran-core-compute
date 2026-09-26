#Requires -Version 7.0
# THE PROOF LEG, parameterised — the reusable half of `proofs/check.ps1` (Phase 155).
#
# This file is the KIT's engine. It knows how to run a proof leg; it knows nothing about which
# models a repository has, where its oracle host lives, or what its project is called. A
# repository's own `proofs/check.ps1` is the thin caller that supplies those (see
# `templates/check.ps1` beside this file), and it is the caller — not this script — that holds the
# module list, so the list stays where a reader and a sibling session expect to find it.
#
# EVERY MODULE in -Modules goes through the same three steps, and each step can fail on its own.
#   1. CHECK  — <ProofsDir>/<Module>.fst is verified by the PINNED F*/Z3 (-PinFile) with every SMT
#               query proved -Quake times over varying seeds and every escape hatch (assume /
#               admit) reported as an error. -Runs N repeats the whole check N times from a cold
#               cache — CI asks for 3, which is exit criterion 1 made literal. A run checks EVERY
#               module before the next run starts, so -Runs still means "N cold-cache
#               verifications of everything", as it did when there was one model.
#               Each module's wall clock is MEASURED AGAINST A DECLARED BUDGET (Phase 148):
#               -BudgetFile says what each module is expected to cost, every green line prints
#               the measured seconds beside that budget, and an overshoot is a named COST warning
#               rather than a failure — prover time varies by machine and by load, so the budget
#               is a smoke detector and not a gate. -Strict promotes every UNLABELLED cost finding
#               to a red leg, for a session that wants one — see the contention factor below for
#               what a labelled one is. A fixed CI job timeout is deliberately NOT what
#               this is: a timeout says a run died and nothing about which module.
#               The clock is also measured against a declared FLOOR (Phase 164), and that one
#               IS a gate: a module that verifies in less than its floorSeconds FAILS the leg on
#               the spot, naming the module and the time. The two directions are not symmetric.
#               An overshoot is a real measurement of a real cost; an undershoot means the
#               measuring apparatus is broken — almost always a second writer in the cache — so
#               everything after it would be measured with the same broken apparatus. -NoFloor
#               is the deliberate opt-out for a machine genuinely that fast.
#               The cache the cold runs use is PER INVOCATION (<WorkDir>/cache-<pid>, or
#               -CacheDir), created and removed by this script, so that "cold cache" cannot be
#               quietly falsified by another run in the same worktree. See "Running it" in the
#               adopting repository's proofs README for the 2026-09-14 incident that bought both.
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (<OracleDir>/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them). A model named in
#               -ProofOnly is EXEMPT and says so on its own line: an oracle exists so a
#               differential can run the extracted model beside the production code, so a model
#               with no production code to run beside earns no oracle, and committing one would
#               commit generated F# that nothing compiles, calls or compares. The exemption is
#               the caller's to declare and to justify — what step 2 buys for the other models
#               ("the artefact is the model, byte for byte") an exempt one must get some other
#               way, one level further up, and the caller says where.
#   3. HOST   — the -HostFilters the caller declared, each its own invocation of the host test
#               project (-HostProject / -HostProjectFile) with its own failure message. Separate
#               invocations rather than one prefix filter, so two failures read as what they are
#               rather than as one red suite. -SkipOracleHost leaves them all to the repository's
#               own gate, which already runs the whole suite.
#
# THE THREE VERDICT CLASSES (Phase 166). A leg that reports every lost pass as "did NOT verify" is
# not an evidence instrument in either direction: on 2026-09-14/15 three different things all read
# as a refutation, and each cost a session twenty minutes of reading a whole log to establish that
# nothing had been refuted. So a non-zero prover exit is now CLASSIFIED before it is reported, and
# the class decides the words, the exit code and whether anything is retried.
#
#   REFUTATION — the prover exited non-zero AND printed a diagnostic: `(Error NNN)`, `Failed to
#               prove`, `Unexpected`, or a quake line reporting a failure. Something was refuted,
#               or an escape hatch was reported by --report_assumes error. Reported as
#               `<module>.fst did NOT verify`, exactly as before, with the PROVER's exit code.
#               NEVER retried: a refutation is a result, and re-running it to see whether it goes
#               away is the habit this leg exists to make impossible.
#   ABORT     — the prover exited non-zero and printed NO such diagnostic. Nothing was refuted;
#               the prover died. (Phase 162's worker watched WireDecode.fst and JsonParse.fst —
#               modules it never touched — fail with no error, warning or exception anywhere in
#               the log, and both retried green. Phase 149's saw fstar.exe killed mid-Preservation
#               at a different lemma each time, with free memory under 3 GB and six provers on the
#               machine.) Reported as `<module>.fst ABORTED (no diagnostic)` and RETRIED ONCE in
#               the same run — once per module per run, bounded, and logged AS a retry so its
#               timing is never read as a cold measurement. A second abort of the same module in
#               the same run fails the leg with exit $ExitAbort, which is not a refutation's code.
#   APPARATUS — the leg's own machinery failed, not the model: at EXTRACT, a dependency's
#               `.checked` file missing from the cache (F* error 317 — Phase 155's worker watched
#               the per-invocation cache empty mid-run), or the cache directory gone. Reported as
#               an APPARATUS fault NAMING the file, with exit $ExitApparatus. Not retried: the
#               thing it needs is gone, so a second attempt asks the same broken apparatus the
#               same question.
#
# The discriminator between the first two is the LOG, not the exit code, because a killed process
# and a refuted lemma are both "non-zero" and nothing about the number tells them apart. It is
# stated where it is computed — see `Test-ProverDiagnostic` in section 1b — and its whole content
# is: did the prover SAY anything about an undischarged query. F* prints no per-query line for a
# query it discharged, so a log with no diagnostic in it is one in which every query the module
# printed was discharged; that is the same statement, read off the only evidence there is.
#
# THE CONTENTION FACTOR (Phase 171). A budget overshoot has two entirely different causes — this
# module got more expensive, or this machine was busy — and until now the log said nothing about
# which one a reader was looking at. So at the end of every RUN the leg reports one number: the
# median, over the modules this working tree did NOT change, of what each cost divided by the
# `measuredSeconds` its budget entry records. An untouched module's cost is a fact about the
# machine, so a run in which all of them came in at 1.7x their recorded measurements is a run in
# which the machine was 1.7x slower, whatever any single line says. Above the threshold declared in
# the budget file's `contentionSeeding` block, every cost finding from that run is LABELLED a
# contended pass; a labelled finding still prints, still warns, and is explicitly NOT a re-seed
# obligation, while -Strict promotes only the UNLABELLED ones. Nothing is multiplied into a
# measurement: the seconds a green line prints stay the wall clock, and the factor sits beside
# them. Section 2b carries the argument and the limits; section 3c computes it.
#
# WHAT THE NUMBER'S SCALE ACTUALLY IS, because it is not the obvious one and the threshold depends
# on it (measured, Phase 171, on the pinned prover). `measuredSeconds` is not a typical cost — by
# the budget file's own seeding rule it is the SLOWEST cold run ever observed for that module, and
# most of this repository's were seeded under several concurrent sessions. So the ratio's neutral
# point sits well BELOW one: a quiet pass of this leg measured x0.29, not x1. A threshold picked as
# though 1.0 meant "normal" would therefore sit above any contention this leg can experience and
# would never fire — the "detector that cannot fire" the budget file's own TreeOps note warns
# about. The threshold is seeded from a measured quiet pass and a measured contended one, in that
# block, and re-seeding it is the same recorded act as bumping a budget.
#
# Every prover invocation's whole output is also TEED to <WorkDir>/logs/, so a post-mortem reads
# the classification's own evidence rather than a scrollback. And every run prints a PRE-FLIGHT
# line — concurrent fstar process count, free physical memory — so a contended machine can be told
# from a broken model without asking anyone who was there.
#
# The prover is resolved from $env:FSTAR_HOME (a release directory holding bin/fstar.exe), else
# from <ProofsDir>/.fstar/ (a previous install by this script), else DOWNLOADED from the pinned
# GitHub release, hash-verified, and unpacked there. Both directories, and <WorkDir>, are expected
# to be gitignored by the adopting repository.
# Only the Windows release is pinned by the pin file's shape — the leg runs on the Windows CI
# runner and the Windows dev machines; on another OS set FSTAR_HOME to a matching release and the
# pin's version check still applies.
#
# Public behaviour is the contract. Every line this script prints, and every exit code it returns,
# is what the pre-kit `check.ps1` printed and returned — a repository's tests may parse them.
[CmdletBinding()]
param(
    # The models, in the order they are to be checked. The CALLER owns this list.
    [Parameter(Mandatory)][string[]] $Modules,
    # Models that are CHECKED but not EXTRACTED — see step 2 in the header. A narrow, declared
    # exemption, never a default.
    [string[]] $ProofOnly = @(),
    # The directory holding the .fst models. Everything else defaults relative to it.
    [Parameter(Mandatory)][string] $ProofsDir,
    # The repository root the host step runs from. Defaults to the parent of -ProofsDir.
    [string] $RepoRoot,
    # The pinned prover declaration. Defaults to <ProofsDir>/fstar-pin.json.
    [string] $PinFile,
    # The per-module cost budgets and time floors. Defaults to <ProofsDir>/modules.json.
    [string] $BudgetFile,
    # The committed extractions the fresh ones are diffed against. Defaults to <ProofsDir>/oracle.
    [string] $OracleDir,
    # Where the checked-module cache and the fresh extractions go. Defaults to <ProofsDir>/obj.
    [string] $WorkDir,
    # The host test project, as a path relative to -RepoRoot: the directory `dotnet run --project`
    # takes, and the .fsproj `dotnet build` takes.
    [string] $HostProject,
    [string] $HostProjectFile,
    # One entry per host invocation: @{ Filter = '<Expecto filter>'; Failure = '<message on red>' }.
    # An empty list means there is no host step, which is a legitimate shape for a repository whose
    # models have no differential yet.
    [hashtable[]] $HostFilters = @(),
    # The prover flags the leg is defined by. Defaults are the values the leg was cut with.
    [int] $Quake = 3,
    [int] $ZRlimit = 40,
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

# The leg's OWN exit codes, for the two verdicts that are not a refutation (Phase 166). A
# refutation returns the PROVER's code, which is 1 in practice; these two are the leg's, so a
# caller — a CI job, a wrapper, a post-mortem grep — can tell "the model is wrong" from "the
# prover died" and from "the leg's machinery broke" without reading a line of output. 2 is already
# taken (no FSTAR_HOME on a non-Windows machine), so these start at 3.
$ExitAbort = 3
$ExitApparatus = 4

$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not (Test-Path $ProofsDir)) { throw "the proofs directory '$ProofsDir' does not exist" }
if (-not $RepoRoot) { $RepoRoot = Split-Path $ProofsDir -Parent }
if (-not $PinFile) { $PinFile = Join-Path $ProofsDir 'fstar-pin.json' }
if (-not $BudgetFile) { $BudgetFile = Join-Path $ProofsDir 'modules.json' }
if (-not $OracleDir) { $OracleDir = Join-Path $ProofsDir 'oracle' }
if (-not $WorkDir) { $WorkDir = Join-Path $ProofsDir 'obj' }

# The budget file's own name, so that every message about it names the file the caller declared
# rather than a name baked in here.
$budgetName = Split-Path $BudgetFile -Leaf

Set-Location $ProofsDir

# NEVER ASSIGN $LASTEXITCODE IN THIS SCRIPT (Phase 221). It is an automatic variable that a native
# command sets in the GLOBAL scope. An assignment here — this line used to read `$LASTEXITCODE = 0`,
# carried over from the pre-kit `check.ps1` — creates a SCRIPT-scope variable of the same name, and
# every later read in this script and in its functions finds that one first. Run as `pwsh -File`,
# the script's scope happens to be the one a native command writes, so the seed was harmless in
# the pre-kit script. Run as `& check-proof-leg.ps1`, which is how every caller's `check.ps1`
# invokes it (Phase 155), the seed SHADOWS the real code: every `$LASTEXITCODE` read below saw 0
# whatever the command did. Measured 2026-09-24 against the pinned prover: a host build of a
# project that does not exist (MSB1009) printed `==== proofs: green` and exited 0, and so did a
# model with a type error — the check step's exit code read 0 too, so a refutation was reported
# as a verification. The failure was DIAGNOSED (MSBuild and F* both said so) and not REFUSED.
# `check-proof-leg.tests.ps1` beside this file runs the leg the way a caller does and holds every
# step to a non-zero exit; it goes red if this line ever comes back.

# The per-invocation cache this run owns, once section 3 has resolved one. Named here, above
# Fail, so that EVERY exit path removes it: PowerShell resolves a function body at call time, so
# Fail can call Remove-InvocationCache from before its definition. A run killed outright (a turn
# boundary, Ctrl-C) still leaves its directory behind — nothing inside a process can promise
# otherwise, which is why section 3 sweeps dead runs' directories at startup instead of trusting
# this. Leaving one behind is harmless in any case: it belongs to a pid, so it poisons nobody.
$script:invocationCache = $null
$script:invocationCacheIsOurs = $false

function Remove-InvocationCache {
    if ($script:invocationCacheIsOurs -and $script:invocationCache -and (Test-Path $script:invocationCache)) {
        Remove-Item $script:invocationCache -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Fail([string] $message, [int] $code = 1) {
    Remove-InvocationCache
    Write-Host "==== proofs: $message" -ForegroundColor Red
    exit $code
}

# A terminating error under $ErrorActionPreference = 'Stop' bypasses Fail; this does not.
trap { Remove-InvocationCache; break }

if (-not (Test-Path $PinFile)) { Fail "the pin file '$PinFile' is missing — it declares the prover this leg is defined by" }
$pin = Get-Content $PinFile -Raw | ConvertFrom-Json
$pinnedVersion = $pin.fstar.TrimStart('v')

# ---- 1. resolve the prover ---------------------------------------------------------------------

function Resolve-FStar {
    if ($env:FSTAR_HOME) {
        $exe = Join-Path $env:FSTAR_HOME 'bin/fstar.exe'
        if (-not (Test-Path $exe)) { Fail "FSTAR_HOME is set to '$env:FSTAR_HOME' but bin/fstar.exe is not there" }
        return $exe
    }

    $local = Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe'
    if (Test-Path $local) { return $local }

    if (-not $IsWindows) {
        Fail "no FSTAR_HOME and this is not Windows — only the Windows release is pinned ($(Split-Path $PinFile -Leaf)); set FSTAR_HOME to an F* $($pin.fstar) release" 2
    }

    $asset = $pin.windows.asset
    $dir = Join-Path $ProofsDir '.fstar'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir $asset

    if (-not (Test-Path $zip)) {
        Write-Host "==== proofs: downloading the pinned prover $($pin.fstar) ($asset)" -ForegroundColor Cyan
        Invoke-WebRequest -Uri $pin.windows.url -OutFile $zip
    }

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $pin.windows.sha256) {
        Remove-Item $zip -Force
        Fail "the downloaded $asset does not match the pinned sha256 (got $hash, pinned $($pin.windows.sha256)); it was deleted — re-run to fetch again"
    }

    Write-Host "==== proofs: unpacking $asset" -ForegroundColor Cyan
    Expand-Archive -Path $zip -DestinationPath $dir -Force
    if (-not (Test-Path $local)) { Fail "unpacked $asset but found no fstar/bin/fstar.exe under $dir" }
    return $local
}

$fstar = Resolve-FStar
$versionLine = (& $fstar --version 2>&1 | Select-Object -First 1)
if ($versionLine -ne "F* $pinnedVersion") {
    Fail "the resolved prover reports '$versionLine' but the pin is 'F* $pinnedVersion' ($fstar)"
}

$fstarHome = Split-Path (Split-Path $fstar -Parent) -Parent
$z3dir = Get-ChildItem (Join-Path $fstarHome 'lib/fstar') -Directory -Filter 'z3-*' | Select-Object -First 1
if ($null -eq $z3dir) { Fail "no bundled Z3 under $fstarHome/lib/fstar" }
if ($z3dir.Name -ne "z3-$($pin.z3)") { Fail "the bundled Z3 is $($z3dir.Name); the pin is z3-$($pin.z3)" }
Write-Host "==== proofs: $versionLine, bundled $($z3dir.Name), at $fstarHome" -ForegroundColor Cyan

# ---- 1b. the three verdicts (Phase 166) ----------------------------------------------------------
#
# See the header for what each class means and what it obliges. What lives here is HOW the class is
# decided, and it is deliberately one small readable function per question.

# THE DISCRIMINATOR between a refutation and an abort. A log carrying any of these is one in which
# the prover SAID something was wrong; a log carrying none of them is one in which it said nothing,
# so whatever ended the process, it was not a refutation. Line by line, and each alternative is
# here for a reason:
#
#   `* Error NNN`        — F*'s own diagnostic header, and the FORM THE PINNED PROVER ACTUALLY
#                          PRINTS: `* Error 19 at Foo.fst(8,39-8,41):`, `* Error 325 …`,
#                          `* Error 129:` for a file it cannot open. Measured, not assumed — see
#                          the note below, which is the whole reason this line is first.
#   `(Error NNN)`        — the parenthesised spelling. Kept beside the one above rather than
#                          instead of it: it is what F*'s other message formats emit, and a leg
#                          whose discriminator only knows one of two spellings is a leg that reads
#                          a refutation as an abort the day the format is switched.
#   `N error(s) … reported` — F*'s end-of-run summary. The most unambiguous line in the log, and
#                          the one that survives any change to how an individual diagnostic reads.
#   `Failed to prove`    — the SMT solver's own sentence, which appears inside a numbered
#                          diagnostic and also on its own.
#   `Unexpected`         — `Unexpected error` / `Unexpected exception`, which can reach the log
#                          without a number at all.
#   a failing quake line — a query that survived some seeds and not others; `--quake` reports it,
#                          and it is a refutation even though the module part-verified.
#
# WHAT WAS MEASURED, AND WHAT THE SHARD ASSUMED (Phase 166). This phase was specified against
# `(Error NNN)` as "the F* error line". On the pinned prover it is not: the probe that staged a
# missing model file printed `* Error 129:` and the leg — with only the parenthesised spelling —
# classified a plain diagnostic as an ABORT and RETRIED it, which is the exact inversion this
# verdict exists to prevent. The deliberately-false-lemma probe had passed a moment earlier, but
# only through `Failed to prove`, so the green probe was agreeing for the wrong reason. Both
# spellings are matched now and the summary line is matched as a third, independent witness.
#
# What is deliberately NOT here: warnings. A warning is not an undischarged query, and the one
# warning class that matters to this leg (an assume) is promoted to an error by the flags above, so
# matching warnings would only make a green-but-chatty module read as refuted.
$diagnosticPattern = [regex]::new(
    '^\s*\*\s*Error\b' +
    '|\(Error\s+\d+\)' +
    '|\berrors?\s+(were|was)\s+reported\b' +
    '|Failed to prove' +
    '|Unexpected' +
    # A quake line is a failure unless it reads `proved N/N goals` with equal counts. Matching
    # `fail` anywhere refused TreeOps.fst over a query NAMED `..._fails_...` (2026-09-26). The
    # whitespace sits INSIDE the lookahead: outside it, `\s+` backtracks to a shorter match and
    # the lookahead then sees `\tproved`, never `proved`.
    '|^\s*Quake:\s*query\s*\([^)]*\)(?!\s*proved\s+(\d+)/\2\s+goals\b)')

function Test-ProverDiagnostic([System.Collections.Generic.List[string]] $lines) {
    foreach ($line in $lines) {
        if ($diagnosticPattern.IsMatch($line)) { return $true }
    }
    return $false
}

# The APPARATUS discriminator, at the extraction step: a dependency's checked-module file missing
# from the cache. Returns the file's name when the log carries one, so the fault is reported
# NAMING it rather than as a bare error number nobody can act on.
#
# Asked of the WHOLE log rather than line by line, because the two sentences that say this happened
# are wrapped across several lines and the path sits on one of its own:
#
#     * Warning 241 at PDep.fst(0,0-0,0):
#       - Unable to load
#         …\cache-16348\PDep.fst.checked
#         since checked file
#         …\cache-16348\PDep.fst.checked
#         does not exist; will recheck PDep.fst
#     * Error 317:
#       - Cross-module inlining expects all modules to be checked first.
#
# It is only ever CONSULTED about an extraction that already failed (see section 4), and that
# ordering is load-bearing: Warning 241 on its own means F* re-checked the module and carried on,
# which is a working leg, so treating the warning as the fault would redden a run that succeeded.
function Get-MissingCheckedDependency([System.Collections.Generic.List[string]] $lines) {
    $text = $lines -join "`n"
    $fault = ($text -match 'Error\s+317\b') -or ($text -match 'checked file' -and $text -match 'does not exist')
    if ($fault) {
        $named = [regex]::Match($text, '[^\s"'']+\.checked')
        if ($named.Success) { return $named.Value }
        return 'a checked-module file the log does not name — read the extraction log'
    }
    # The cache directory going away entirely is the same fault one level up, and it prints nothing
    # recognisable, so it is asked about rather than parsed for.
    if ($script:invocationCache -and -not (Test-Path $script:invocationCache)) {
        return "the cache directory $script:invocationCache itself, which is gone"
    }
    return $null
}

# A refutation returns the prover's own code — that is the pre-kit behaviour and a repository's
# tests may depend on it. The two lines below are what make "distinct from a refutation's" a fact
# rather than an expectation: a prover that ever exited 3 or 4 would otherwise make the leg's own
# codes ambiguous, so those two are remapped to the generic 1 and the remap says so.
function Get-RefutationExitCode([int] $code) {
    if ($code -eq $ExitAbort -or $code -eq $ExitApparatus -or $code -eq 0) {
        Write-Host "==== proofs: the prover exited $code, which is one of this leg's own verdict codes; reporting the refutation as exit 1 so the codes stay distinct" -ForegroundColor Yellow
        return 1
    }
    return $code
}

# The PRE-FLIGHT snapshot. A post-mortem asking "was the machine contended?" has, today, no way to
# find out — the run is over and the processes are gone. Two numbers at the head of every run
# answer it: how many provers were running (this one included), and how much physical memory was
# free. Phase 149's aborts happened under six concurrent provers with free memory below 3 GB.
# It must never be able to fail the leg, so every reading is guarded and an unavailable one reads
# as `unknown` rather than throwing. Note where each reading is taken from: at the HEAD of a run
# this run's own prover has not started yet, so the count is of the OTHER provers on the machine,
# which is the number a contention question is actually about; at an ABORT it includes whatever is
# running at that instant.
function Get-ResourceSnapshot {
    $provers = 0
    try { $provers = @(Get-Process -Name 'fstar' -ErrorAction SilentlyContinue).Count } catch { $provers = -1 }
    $free = 'unknown'
    try {
        if ($IsWindows) {
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
            $free = '{0:N1} GB' -f ($os.FreePhysicalMemory / 1MB)   # FreePhysicalMemory is in KB
        }
        elseif (Test-Path '/proc/meminfo') {
            $kb = [regex]::Match((Get-Content '/proc/meminfo' -Raw), 'MemAvailable:\s+(\d+)')
            if ($kb.Success) { $free = '{0:N1} GB' -f ([double]$kb.Groups[1].Value / 1MB) }
        }
    }
    catch { $free = 'unknown' }
    $proverText = if ($provers -lt 0) { 'an unreadable number of' } else { "$provers" }
    "$proverText fstar process(es) on this machine, $free free physical memory"
}

# Every prover invocation goes through here, so that (a) its whole output is available to the
# classifiers above rather than only to a human reading scrollback, and (b) the same bytes are teed
# to a file a post-mortem can open. The output is re-emitted line by line as it arrives, so what a
# watcher sees is what it saw before.
#
# $ErrorActionPreference is lowered for the invocation and restored after: with `2>&1` on a native
# command, PowerShell surfaces stderr as ErrorRecords, and under 'Stop' the prover's first stderr
# line would terminate the leg before anything could be classified — which is precisely the failure
# mode this section exists to remove.
function Invoke-Prover([string[]] $arguments, [string] $logPath) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $previous = $ErrorActionPreference
    $code = 0
    try {
        $ErrorActionPreference = 'Continue'
        & $fstar @arguments 2>&1 | ForEach-Object {
            $line = if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { [string]$_ }
            $lines.Add($line)
            Write-Host $line
        }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($logPath) {
        try { [System.IO.File]::WriteAllLines($logPath, $lines) } catch { }
    }
    [pscustomobject]@{ ExitCode = $code; Lines = $lines; LogPath = $logPath }
}

# ---- 2. the declared cost budgets ----------------------------------------------------------------

# The budget file says what each module is expected to cost on a cold run. It is a committed
# declared artefact, so its ABSENCE is a defect and fails here; a mismatch between it and -Modules
# is a cost finding rather than a failure, because a sibling adding a model should not have their
# leg go red for a budget nobody could have measured yet — the finding names the module and what
# to do.
# A finding is an OBJECT rather than a string since Phase 171, because a ceiling finding acquires
# one more fact after it is printed: the contention factor of the run it fired on, which is not
# known until that run has measured every module. `Run` is the run it belongs to (0 for the
# coverage and shape findings below, which belong to no run and are never labelled), and `Label` is
# filled in at the end of that run by section 3c. The line printed HERE is byte-identical to the
# one this leg has always printed — the label reaches the reader on the run's own contention line
# and on the closing verdict, which are the two places that can carry it honestly.
$costFindings = [System.Collections.Generic.List[object]]::new()

function Add-CostFinding([string] $message, [int] $run = 0) {
    $script:costFindings.Add([pscustomobject]@{ Text = $message; Run = $run; Label = '' })
    Write-Host "==== proofs: COST — $message" -ForegroundColor Yellow
}

if (-not (Test-Path $BudgetFile)) {
    Fail "$budgetName is missing — it declares each module's cost budget (see the README's 'Running it')"
}

# The SHAPE is a failure where a mismatch is a finding, and the difference is whether the file can
# still be read as a budget at all. An entry with no `budgetSeconds` would otherwise arrive as 0 and
# every run would be infinitely over it — a flood of findings, and a division by zero rendering the
# percentage. A declared artefact is held to its shape by the code that consumes it.
# The FLOOR beside it (Phase 164) is held to its shape the same way WHEN IT IS THERE, and is a
# cost finding when it is ABSENT — a sibling adding a model should no more go red for a floor
# nobody has measured than for a budget nobody has measured. An absent floor degrades to exactly
# the pre-164 behaviour for that module, which is the safe direction; a floor of 0 is legal and
# means "this module genuinely checks in about a second", which is NOT the same statement as an
# absent one and reads differently in the file.
#
# Two more per-entry numbers are READ here since Phase 171, and neither is required. The
# `measuredSeconds` this file has always recorded beside a budget — the observation the budget was
# seeded from — becomes the DENOMINATOR of the contention factor in section 3c, so it is held to
# its shape when it is there and its absence simply takes that module out of the factor. The
# optional `contentionFactor` beside it is PROVENANCE and nothing else: it records what the
# machine was doing when that measurement was taken, so a later reader can tell a number seeded on
# a quiet machine from one seeded on a busy one. Nothing multiplies it into anything — see the
# note on section 3c for why the measurement stays the wall clock.
$budgets = @{}
$floors = @{}
$measurements = @{}
$budgetDocument = Get-Content $BudgetFile -Raw | ConvertFrom-Json
foreach ($entry in $budgetDocument.modules) {
    $name = $entry.module
    if ([string]::IsNullOrWhiteSpace($name)) { Fail "$budgetName carries an entry with no module name" }
    if ($budgets.ContainsKey($name)) { Fail "$budgetName declares '$name' twice" }

    $declaredBudget = $entry.budgetSeconds
    if ($declaredBudget -isnot [int] -and $declaredBudget -isnot [long] -and $declaredBudget -isnot [double]) {
        Fail "$budgetName entry '$name' has no numeric budgetSeconds"
    }
    if ([int]$declaredBudget -lt 1) { Fail "$budgetName entry '$name' has a budgetSeconds of $declaredBudget — a budget is a positive number of seconds" }

    $budgets[$name] = [int]$declaredBudget

    $declaredFloor = $entry.floorSeconds
    if ($null -ne $declaredFloor) {
        if ($declaredFloor -isnot [int] -and $declaredFloor -isnot [long] -and $declaredFloor -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric floorSeconds"
        }
        if ([int]$declaredFloor -lt 0) { Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor — a floor is a non-negative number of seconds" }
        if ([int]$declaredFloor -ge [int]$declaredBudget) {
            Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor at or above its budgetSeconds of $([int]$declaredBudget) — no run could satisfy both"
        }
        $floors[$name] = [int]$declaredFloor
    }

    $declaredMeasured = $entry.measuredSeconds
    if ($null -ne $declaredMeasured) {
        if ($declaredMeasured -isnot [int] -and $declaredMeasured -isnot [long] -and $declaredMeasured -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric measuredSeconds"
        }
        if ([double]$declaredMeasured -lt 0) { Fail "$budgetName entry '$name' has a measuredSeconds of $declaredMeasured — a measurement is a non-negative number of seconds" }
        $measurements[$name] = [double]$declaredMeasured
    }

    $declaredContention = $entry.contentionFactor
    if ($null -ne $declaredContention) {
        if ($declaredContention -isnot [int] -and $declaredContention -isnot [long] -and $declaredContention -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric contentionFactor"
        }
        if ([double]$declaredContention -le 0) { Fail "$budgetName entry '$name' has a contentionFactor of $declaredContention — a factor is a positive multiple" }
    }
}

foreach ($module in $Modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
    elseif (-not $floors.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no floorSeconds for it — nothing can tell an implausibly fast run of it from a real one; seed one per the file's floorSeeding rule and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($Modules -notcontains $declared) {
        Add-CostFinding "$budgetName budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
    }
}

# ---- 2b. the contention factor's declarations (Phase 171) ----------------------------------------
#
# WHY THIS EXISTS. A budget overshoot has two completely different causes and the log said nothing
# about which one it was looking at. On 2026-09-14 `Chain` overshot twice in seven runs and came in
# at 16-25s on the other five, untouched by any phase since 145. Phase 162 measured `TreeOps` at
# 145s in a pass that inflated three untouched modules by the same factor, and had to depart from
# the seeding rule by hand and write a paragraph explaining why. Phase 182 measured `Capability` at
# 75s against a 20s budget on a run contended by three sibling gates. Every one of those ran beside
# other provers, and a reader of any of those logs today cannot tell the afternoon from the module.
#
# THE MEASUREMENT. At the end of each run the leg takes, for every module the run's tree did NOT
# change, the ratio of what that module just cost to the `measuredSeconds` its entry records — and
# reports the MEDIAN of those ratios as the run's contention factor. An untouched module's cost is
# a property of the machine and not of the tree, so a pass in which all of them ran 1.7x their
# recorded measurements is a pass in which the machine was 1.7x slower, whatever any one module's
# line says. The median rather than the mean, because one module aborting-and-retrying or hitting a
# pathological query is exactly the outlier a mean would launder into the number.
#
# WHAT IT IS DELIBERATELY NOT. It is never multiplied into a measurement: the seconds a green line
# prints stay the wall clock the module actually took, and the factor sits BESIDE them. A
# normalised measurement would be a number nobody observed, and the whole value of this leg's cost
# half is that every figure in it is one somebody's machine really produced.
#
# THE THRESHOLD is declared, in this file's own `contentionSeeding` block, for the same reason the
# budget and floor rules are: a number the engine baked in would be a number no repository could
# re-seed from its own machine. An ABSENT block is NOT a finding — unlike an absent budget or floor,
# which fire per module when a model is added, this one is per FILE and one-off, and a finding that
# is present on every run of an unseeded repository is one people learn to scroll past. The factor
# is still computed and still printed; nothing is labelled, and the line says so and names the
# block to seed. That is exactly the pre-171 behaviour plus one informative number, which is the
# safe direction for an adopter.
$contentionThreshold = $null
$contentionMinimumSamples = 3
# A module whose recorded measurement is a second or two contributes noise rather than signal: the
# clock is whole seconds, so 0s against a recorded 2s is a ratio of 0 and 1s is a ratio of 0.5, and
# neither says anything about the machine. `floorSeeding.zeroBelowSeconds` already carries this
# repository's answer to "below what is a reading process-start noise" — reused here rather than
# minted again, so there is one number and one argument for it.
$contentionMinimumSeconds = 5
if ($null -ne $budgetDocument.floorSeeding -and $null -ne $budgetDocument.floorSeeding.zeroBelowSeconds) {
    $contentionMinimumSeconds = [double]$budgetDocument.floorSeeding.zeroBelowSeconds
}
if ($null -ne $budgetDocument.contentionSeeding) {
    $block = $budgetDocument.contentionSeeding
    if ($null -ne $block.threshold) {
        if ($block.threshold -isnot [int] -and $block.threshold -isnot [long] -and $block.threshold -isnot [double]) {
            Fail "$budgetName contentionSeeding.threshold is not numeric"
        }
        if ([double]$block.threshold -le 0) { Fail "$budgetName contentionSeeding.threshold is $($block.threshold) — a threshold is a positive multiple" }
        $contentionThreshold = [double]$block.threshold
    }
    if ($null -ne $block.minimumSamples) {
        if ([int]$block.minimumSamples -lt 1) { Fail "$budgetName contentionSeeding.minimumSamples is $($block.minimumSamples) — a median needs at least one sample" }
        $contentionMinimumSamples = [int]$block.minimumSamples
    }
    if ($null -ne $block.minimumMeasuredSeconds) { $contentionMinimumSeconds = [double]$block.minimumMeasuredSeconds }
}

# THE UNTOUCHED SET, derived rather than declared. A module this working tree has changed is one
# whose cost may have moved for a reason that IS about the module, so it must not vote on whether
# the machine was slow. `git status --porcelain` answers modified, staged and brand-new in one call
# and needs no branch name, which matters for a kit an adopter drops into a repository whose
# default branch this script cannot know.
#
# The LIMIT is worth stating rather than leaving to be discovered: a session that has already
# COMMITTED its model edits has a clean tree, so its module reads as untouched and votes. That is
# what the median absorbs — one or two skewed ratios out of a dozen move it very little — and it is
# the honest boundary of what a working-tree question can answer. Where git cannot answer at all
# (no repository, no git on PATH) every module counts as untouched, and the line below says so:
# "I could not tell" must never be printed as "nothing is touched".
function Get-TouchedModules {
    try {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $porcelain = & git -C $ProofsDir status --porcelain --untracked-files=all -- . 2>&1 }
        finally { $ErrorActionPreference = $previous }
        if ($LASTEXITCODE -ne 0) { return $null }
    }
    catch { return $null }

    $touched = [System.Collections.Generic.List[string]]::new()
    foreach ($module in $Modules) {
        foreach ($line in @($porcelain)) {
            if ([string]$line -match "(^|[/\\""\s])$([regex]::Escape($module))\.fst(""|\s|$)") {
                $touched.Add($module)
                break
            }
        }
    }
    , $touched.ToArray()
}

$touchedModules = Get-TouchedModules
if ($null -eq $touchedModules) {
    Write-Host "==== proofs: contention — the touched set could not be derived from git here, so EVERY module counts as untouched and votes on the contention factor" -ForegroundColor Yellow
}
elseif ($touchedModules.Count -gt 0) {
    Write-Host "==== proofs: contention — this working tree changes $($touchedModules -join ', ') — excluded from the contention factor, since their cost may have moved for a reason that is about the module" -ForegroundColor Cyan
}

# The median of a run's untouched ratios. Separate, small and total: a median of an empty set is
# not a number and the caller is the one that knows what to print instead.
function Get-Median([double[]] $values) {
    $sorted = @($values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 0) { return $null }
    if ($n % 2 -eq 1) { return [double]$sorted[($n - 1) / 2] }
    return ([double]$sorted[$n / 2 - 1] + [double]$sorted[$n / 2]) / 2
}

# ---- 3. check, -Runs times from a cold cache -------------------------------------------------------
#
# THE CACHE IS PER INVOCATION (Phase 164). Until then it was one constant directory, obj/cache,
# and clearing it at the head of a run only makes that run cold if nothing else is writing there.
# On 2026-09-14 something was: an orphaned background check.ps1 in the same worktree kept writing
# .checked files, and the replacement run reported TreeOps 0s, Skeleton 0s, Chain 0s and printed
# `==== proofs: green`. Nothing in the script could see it — it cleared the directory it was about
# to use, which a second writer defeats a moment later. A directory named for this process cannot
# be written into by another invocation at all, so the property the -Runs loop needs holds by
# construction rather than by nobody else running.

$out = Join-Path $WorkDir 'out'
$cacheRoot = $WorkDir

# The prover transcripts (Phase 166). NOT under the cache directory: a `-CacheDir` the caller named
# is refused at the next invocation if it holds anything that is not a `*.checked` file, so writing
# logs there would turn the flag into a one-shot.
$logsDir = Join-Path $WorkDir 'logs'
if (Test-Path $logsDir) { Remove-Item $logsDir -Recurse -Force }
New-Item -ItemType Directory -Force $logsDir | Out-Null

# Aborts that were retried and then passed. The leg is GREEN when that happens — the model verified
# — but a run that lost a prover and got it back is not the same evidence as one that did not, so
# the verdict at the end says so rather than leaving it to whoever scrolls far enough.
$abortFindings = [System.Collections.Generic.List[string]]::new()

if ($CacheDir) {
    # A caller-named directory is CLEARED at each run head, exactly as the default one is —
    # otherwise -CacheDir would silently mean "warm", which is the opposite of what this section
    # is for. So refuse one holding anything that is not a checked-module file: the flag is for
    # naming where the cache goes, and pointing it at a directory with other contents in it would
    # delete them. It is not removed at exit; the caller named it, so the caller keeps it.
    # `[IO.Path]::Combine` and not `Join-Path`: PowerShell's Join-Path CONCATENATES a rooted second
    # argument, so `-CacheDir C:\somewhere` resolved to `<proofs>\C:\somewhere` and the run died a
    # second later with a path nobody would recognise as their own argument. .NET's Combine returns
    # the second path when it is rooted and joins when it is not, so the relative case — which is
    # what the flag's "cleared before every run" contract is written against — is unchanged.
    # (Inherited from Phase 164 and found by using the flag; a kit must not ship it.)
    $cache = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $CacheDir))
    if (Test-Path $cache) {
        $foreign = Get-ChildItem $cache -Force | Where-Object { $_.PSIsContainer -or $_.Name -notlike '*.checked*' }
        if ($foreign) {
            Fail "-CacheDir '$cache' holds $($foreign.Count) entry/entries that are not checked-module files (first: $($foreign[0].Name)) — this script CLEARS its cache directory before every run, so it will only use one that is empty or holds nothing but *.checked files"
        }
    }
    $script:invocationCacheIsOurs = $false
}
else {
    # Sweep the directories left by runs that were killed before they could remove their own. Only
    # ones whose pid is gone, so a concurrent invocation's cache is never touched — which is the
    # whole point of the naming. A pid that has since been reused just leaves a directory behind;
    # that costs nothing, where deleting a live run's cache would cost the exact incident above.
    if (Test-Path $cacheRoot) {
        foreach ($stale in (Get-ChildItem $cacheRoot -Directory -Filter 'cache-*' -ErrorAction SilentlyContinue)) {
            $stalePid = 0
            if (-not [int]::TryParse($stale.Name.Substring('cache-'.Length), [ref] $stalePid)) { continue }
            if ($stalePid -eq $PID) { continue }
            if (Get-Process -Id $stalePid -ErrorAction SilentlyContinue) { continue }
            Write-Host "==== proofs: sweeping $($stale.Name), left by a run that did not finish" -ForegroundColor DarkGray
            Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $cache = Join-Path $cacheRoot "cache-$PID"
    $script:invocationCacheIsOurs = $true
}
$script:invocationCache = $cache
Write-Host "==== proofs: cache $cache$(if (-not $script:invocationCacheIsOurs) { ' (-CacheDir; left in place at exit)' })" -ForegroundColor Cyan

if ($NoFloor) {
    Write-Host "==== proofs: -NoFloor — the per-module time floors in $budgetName are NOT enforced on this run" -ForegroundColor Yellow
}

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null

    # The PRE-FLIGHT line (Phase 166), at the head of every run rather than once per invocation:
    # contention is what changes between run 1 and run 3, so a number taken once says nothing about
    # the run that actually went wrong.
    Write-Host "==== proofs: pre-flight — run $run of $Runs, $(Get-ResourceSnapshot)" -ForegroundColor Cyan

    # This run's contention sample (Phase 171): one ratio per untouched module with a recorded
    # measurement worth dividing by. Per RUN and not per invocation, for the pre-flight line's own
    # reason — contention is what changes between run 1 and run 3, so a number taken once says
    # nothing about the run that actually went wrong.
    $runRatios = [System.Collections.Generic.List[double]]::new()

    foreach ($module in $Modules) {
        # ONE bounded retry per module per run (Phase 166). `$attempt` is the bound, and it is a
        # number rather than a flag so that the log can say which attempt a line is about.
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $isRetry = $attempt -gt 1
            $suffix = if ($isRetry) { ".run$run.retry" } else { ".run$run" }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $checked = Invoke-Prover @(
                '--z3rlimit', $ZRlimit, '--quake', $Quake, '--report_assumes', 'error',
                '--cache_checked_modules', '--cache_dir', $cache, "$module.fst"
            ) (Join-Path $logsDir "$module$suffix.check.log")
            $sw.Stop()
            $seconds = [int]$sw.Elapsed.TotalSeconds

            if ($checked.ExitCode -ne 0) {
                # REFUTATION — the prover said something was wrong. Reported in the words the leg
                # has always used, with the prover's own code, and never retried.
                if (Test-ProverDiagnostic $checked.Lines) {
                    Fail "$module.fst did NOT verify (run $run of $Runs)" (Get-RefutationExitCode $checked.ExitCode)
                }

                # ABORT — the prover exited non-zero having reported nothing. Whatever happened,
                # nothing was refuted.
                Write-Host "==== proofs: $module.fst ABORTED (no diagnostic) — exit $($checked.ExitCode) after ${seconds}s on run $run of $Runs, attempt $attempt of 2. Nothing was refuted: the prover printed no error line (neither the '* Error NNN' nor the '(Error NNN)' spelling), no 'N errors were reported' summary, no 'Failed to prove', no 'Unexpected' and no failing-quake line. Transcript: $($checked.LogPath)" -ForegroundColor Yellow
                Write-Host "==== proofs: at the abort — $(Get-ResourceSnapshot)" -ForegroundColor Yellow

                if ($isRetry) {
                    Fail ("$module.fst ABORTED (no diagnostic) TWICE on run $run of $Runs — the bounded retry is spent. " +
                        'This is NOT a refutation and the model is not implicated: the prover died with nothing to say, twice. ' +
                        "Read $($checked.LogPath) and the attempt before it, and the pre-flight lines above for what else was on the machine.") $ExitAbort
                }

                Write-Host "==== proofs: retrying $module.fst once — the retry is BOUNDED (one per module per run) and its timing is a WARM measurement, so it feeds neither the budget nor the floor" -ForegroundColor Yellow
                continue
            }

            # AN EXIT OF 0 IS NOT A VERDICT ON ITS OWN (recorded 2026-09-25). The green line
            # below is printed only when the prover exited 0 AND said nothing was wrong: the pinned
            # prover is measured to exit 0 over `* Error 317` at extraction (section 4), and a leg
            # that trusted the exit code alone printed `verified` over an Error 12 in a sibling
            # copy of this kit. A diagnostic on a zero exit is a REFUTATION, never retried.
            if (Test-ProverDiagnostic $checked.Lines) {
                Write-Host "==== proofs: $module.fst — the prover exited 0 but reported an error; see $($checked.LogPath)" -ForegroundColor Red
                Fail "$module.fst did NOT verify (run $run of $Runs)" 1
            }

            $budget = if ($budgets.ContainsKey($module)) { $budgets[$module] } else { $null }

            # A RETRY's clock measures a cache the aborted attempt had already half-filled, so it
            # is not a cold run and must not be read as one — by a person or by either gate. It is
            # printed, marked, and recorded as a finding; it is compared to nothing.
            if ($isRetry) {
                $finding = "$module.fst ABORTED once on run $run of $Runs and verified on the bounded retry in ${seconds}s — a WARM measurement, compared to neither its budget nor its floor"
                $abortFindings.Add($finding)
                Write-Host "==== proofs: $module.fst verified ON RETRY — run $run of $Runs, ${seconds}s (warm cache: NOT a cold measurement), every query $Quake/$Quake under --quake" -ForegroundColor Yellow
                break
            }

            $cost = if ($null -eq $budget) { "${seconds}s (no budget)" } else { "${seconds}s/${budget}s" }
            Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $cost, every query $Quake/$Quake under --quake" -ForegroundColor Green

            # The contention sample (Phase 171). A COLD attempt only — the retry path above breaks
            # before it reaches here, which is the same reason it is compared to neither gate.
            if ($measurements.ContainsKey($module) -and
                $measurements[$module] -ge $contentionMinimumSeconds -and
                ($null -eq $touchedModules -or $touchedModules -notcontains $module)) {
                $runRatios.Add($seconds / $measurements[$module])
            }

            # The CEILING. Restored by Phase 155: Phase 164 deleted this block when it added the floor
            # gate below, so from a27afbc until now an overshoot printed nothing, `$costFindings` was
            # never populated from a measured time, and `-Strict` had nothing to promote — while the
            # budget file's comments and the README both went on describing a ceiling that fired. A
            # measured 32s against a 30s budget said nothing at all. It is a WARNING and the run
            # continues, which is the half the floor below is deliberately not.
            if ($null -ne $budget -and $seconds -gt $budget) {
                Add-CostFinding "$module.fst took ${seconds}s against its ${budget}s budget on run $run of $Runs — $($seconds - $budget)s over, $([int](100 * $seconds / $budget))% of budget" $run
            }

            # The floor fails HERE rather than joining the cost findings at the end, and the asymmetry
            # with the ceiling just above it is deliberate. An overshoot is a true measurement of a
            # true cost, so the run should continue and produce the rest of the evidence. An
            # undershoot says the measurement itself is not to be believed — the cache was not cold —
            # and every module after it is measured by the same apparatus, so carrying on would print
            # more green lines that a reader is entitled to read as evidence and that are not.
            if (-not $NoFloor -and $floors.ContainsKey($module) -and $seconds -lt $floors[$module]) {
                Fail ("$module.fst verified in ${seconds}s on run $run of $Runs, under its $($floors[$module])s floor — that is not a cold verification. " +
                    'Almost always a second writer in the cache directory (see section 3). Check for another check.ps1 or fstar process against this worktree; ' +
                    "if this machine really is that fast, re-seed the floor per $budgetName floorSeeding and cite your phase, or pass -NoFloor for this run.")
            }

            break
        }
    }

    # ---- 3c. the run's contention factor (Phase 171) ---------------------------------------------
    #
    # Here rather than at the head of the run, because the number is read off the run's own
    # measurements and does not exist until they are all in. That ordering is why a ceiling finding
    # is printed unlabelled at the moment it fires and labelled here and in the closing verdict: at
    # the instant a module goes over budget the leg genuinely does not yet know what kind of
    # afternoon it is having, and printing a label it could not have computed would be a worse lie
    # than printing the measurement alone.
    $factor = Get-Median $runRatios.ToArray()
    $thresholdText = if ($null -eq $contentionThreshold) { '' } else { 'x{0:0.00}' -f $contentionThreshold }
    if ($null -eq $factor -or $runRatios.Count -lt $contentionMinimumSamples) {
        Write-Host ("==== proofs: contention — run $run of $Runs, NOT COMPUTED: $($runRatios.Count) untouched module(s) carried a recorded " +
            "measuredSeconds of ${contentionMinimumSeconds}s or more and the factor needs $contentionMinimumSamples. No cost finding on this run is labelled.") -ForegroundColor Yellow
    }
    else {
        $rendered = 'x{0:0.00}' -f $factor
        if ($null -eq $contentionThreshold) {
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s). $budgetName declares no " +
                "contentionSeeding.threshold, so nothing on this run is labelled — seed one per that block's rule and this leg can tell a contended pass from a regression.") -ForegroundColor Cyan
        }
        elseif ($factor -gt $contentionThreshold) {
            $label = " — CONTENDED PASS ($rendered against a $thresholdText threshold): the modules this tree did not change ran $rendered of their recorded measurements on this run, so this figure measures the afternoon and not the module"
            $labelled = 0
            foreach ($f in $costFindings) { if ($f.Run -eq $run) { $f.Label = $label; $labelled++ } }
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s), ABOVE the $thresholdText threshold: this was a CONTENDED pass. " +
                "$labelled cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.") -ForegroundColor Yellow
        }
        else {
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s), at or under the $thresholdText threshold: " +
                'an ordinary pass. A cost finding on this run is about its module.') -ForegroundColor Cyan
        }
    }
}

# ---- 3b. the extraction post-pass -----------------------------------------------------------------
#
# Phase 169. F*'s F# backend emits a mutual TYPE group with the `and` indented one space, which
# F# 10's parser rejects even under the oracle project's `--strict-indentation-`, so an extraction
# carrying one does not compile. The pass below re-indents exactly those lines and touches nothing
# else; the committed oracles are the NORMALISED text, so step 4's contract ("byte-identical to a
# fresh extraction") is unchanged in meaning and every oracle standing today is unchanged in bytes
# — the pass is the identity on all of them, which `kit/extraction-post-pass.tests.ps1` checks
# rather than asserts. That script also carries the go-red fixture and the retirement condition;
# the defect, the pinned prover it was observed on and the reasoning are in the helper's header.
. (Join-Path $PSScriptRoot 'extraction-post-pass.ps1')

# ---- 4. extract, and hold the committed oracle to the model ---------------------------------------

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($module in $Modules) {
    if ($ProofOnly -contains $module) {
        Write-Host "==== proofs: $module is checked, not extracted — no oracle runs it (see `$proofOnly)" -ForegroundColor Cyan
        continue
    }

    $extracted = Invoke-Prover @(
        '--cache_checked_modules', '--cache_dir', $cache, '--codegen', 'FSharp',
        '--extract', $module, '--odir', $out, "$module.fst"
    ) (Join-Path $logsDir "$module.extract.log")

    $fresh = Join-Path $out "$module.fs"
    $committed = Join-Path $OracleDir "$module.fs"

    # A FAILED EXTRACTION IS NOT ALWAYS A NON-ZERO EXIT, and finding that out is most of what this
    # block is for (Phase 166, measured against the pinned prover). Staging Phase 155's incident —
    # removing a dependency's `.checked` underneath the extraction — makes F* print `* Error 317:
    # Cross-module inlining expects all modules to be checked first` and `1 error was reported`,
    # and then EXIT 0. The pre-166 code asked only about the exit code, so the fault fell past it
    # and surfaced two lines later as `extraction produced no PMain.fs under <obj/out>` — a true
    # sentence that names the symptom and not one thing about the cause. So the failure is decided
    # by "the prover failed OR the file is not there", and only then classified.
    $extractionFailed = ($extracted.ExitCode -ne 0) -or (-not (Test-Path $fresh))

    if ($extractionFailed) {
        # APPARATUS — the leg's own machinery, not the model. Read as a proof failure, this costs a
        # session the whole log before it can establish that nothing was refuted. Named, so the next
        # reader starts from the file rather than from the model.
        $missing = Get-MissingCheckedDependency $extracted.Lines
        if ($missing) {
            Fail ("extraction of $module hit an APPARATUS fault, not a proof failure: the checked-module file it needs is missing — $missing. " +
                'The model is not implicated and nothing was refuted. The cache is per invocation and this script owns it, so something removed it ' +
                "underneath this run: check for another check.ps1 against this worktree, or a cleaner over $WorkDir. Transcript: $($extracted.LogPath)") $ExitApparatus
        }

        # An extraction that died with nothing to say is the same ABORT class as a check that did.
        # It is NOT retried — the retry is the check step's, where the module is expensive and the
        # cache is still whole; here the thing extraction needs may be exactly what went away, so a
        # second attempt asks the same broken apparatus the same question.
        if (-not (Test-ProverDiagnostic $extracted.Lines)) {
            Fail ("extraction of $module ABORTED (no diagnostic) — exit $($extracted.ExitCode), and the prover printed nothing about an undischarged query. " +
                "Nothing was refuted. Transcript: $($extracted.LogPath)") $ExitAbort
        }

        if ($extracted.ExitCode -ne 0) {
            Fail "extraction of $module to F# failed" (Get-RefutationExitCode $extracted.ExitCode)
        }

        Fail "extraction produced no $module.fs under $out"
    }

    # A file on disk and an exit of 0 are still not a clean extraction when the prover reported an
    # error beside them (recorded 2026-09-25): the diff below would compare a file the prover
    # itself said is wrong.
    if (Test-ProverDiagnostic $extracted.Lines) {
        Fail "extraction of $module to F# failed — the prover exited 0 but reported an error. Transcript: $($extracted.LogPath)"
    }

    # Compare LF-normalised: the extractor writes LF and the repository pins LF, but a checkout
    # with autocrlf on would otherwise fail this for a reason that is not the model.
    $freshText = (Get-Content $fresh -Raw).Replace("`r`n", "`n")

    # THE POST-PASS (Phase 169, section 3b) — between the extraction and the byte diff, so the
    # committed oracle is held to text F# can parse. It fires only on a mutual TYPE group, which no
    # model in this directory has yet, so this line prints nothing and moves nothing today. The next
    # mutually recursive model is what it is here for, and when it fires it SAYS SO: a silent
    # rewrite between an extraction and the artefact it is diffed against is exactly the kind of
    # step that should never be invisible.
    $postPassLines = Test-ExtractionMutualTypeDefect $freshText
    if ($postPassLines.Count -gt 0) {
        $freshText = Repair-ExtractionMutualTypeGroup $freshText
        Write-Host ("==== proofs: the extraction post-pass re-indented $($postPassLines.Count) mutual-type-group " +
            "``and`` line(s) in $module (F* $pinnedVersion emits them one space in, which F# rejects — " +
            'see kit/extraction-post-pass.ps1)') -ForegroundColor Cyan
    }

    $committedText = if (Test-Path $committed) { (Get-Content $committed -Raw).Replace("`r`n", "`n") } else { '' }

    if ($Extract) {
        [System.IO.File]::WriteAllText($committed, $freshText, [System.Text.UTF8Encoding]::new($false))
        Write-Host "==== proofs: wrote the fresh extraction to oracle/$module.fs — commit it" -ForegroundColor Yellow
    }
    elseif ($freshText -ne $committedText) {
        Write-Host "==== proofs: the committed oracle (oracle/$module.fs) is NOT the extraction of $module.fst." -ForegroundColor Red
        Write-Host "     Fresh extraction: $fresh" -ForegroundColor Red
        Write-Host "     Re-extract with: pwsh ./proofs/check.ps1 -Extract   (then commit the result)" -ForegroundColor Red
        Fail "oracle drift"
    }
    else {
        Write-Host "==== proofs: oracle/$module.fs is byte-identical to a fresh extraction" -ForegroundColor Green
    }
}

# ---- 5. the oracle host --------------------------------------------------------------------------

if (-not $SkipOracleHost -and $HostFilters.Count -gt 0) {
    if (-not $HostProject -or -not $HostProjectFile) {
        Fail 'the caller declared host filters but no -HostProject / -HostProjectFile to run them in'
    }
    Push-Location $RepoRoot
    try {
        dotnet build $HostProjectFile --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build — HOST step, $HostProjectFile, exit $LASTEXITCODE" $LASTEXITCODE }

        # `$hostStep` and not `$host`: `$Host` is a PowerShell automatic variable and a foreach
        # over it is a hard error, which is the kind of thing that only shows up on the first red
        # run rather than on the first green one.
        foreach ($hostStep in $HostFilters) {
            $filter = $hostStep.Filter
            dotnet run --project $HostProject --no-build -- --filter $filter
            if ($LASTEXITCODE -ne 0) { Fail "$($hostStep.Failure) — HOST step, filter '$filter', exit $LASTEXITCODE" $LASTEXITCODE }
        }
    }
    finally { Pop-Location }
}
else {
    # Said out loud, because the verdict line below is the same word either way (Phase 221): a
    # green with no host step is a statement about the check and extract steps only, and a reader
    # citing it must be able to see that from the log rather than from the invocation.
    $why = if ($SkipOracleHost) { '-SkipOracleHost' } else { 'the caller declared no -HostFilters' }
    Write-Host "==== proofs: the HOST step did NOT run ($why) — this leg's green covers the check and extract steps only" -ForegroundColor Yellow
}

# ---- 6. the cost verdict ---------------------------------------------------------------------------
#
# Last, so that every finding is in hand and none of them can stop the evidence being produced: a
# leg that went red on the clock before running the differential would hide a real disagreement
# behind a slow afternoon.

if ($abortFindings.Count -gt 0) {
    Write-Host "==== proofs: $($abortFindings.Count) ABORT(s) were retried and passed on this leg:" -ForegroundColor Yellow
    foreach ($f in $abortFindings) { Write-Host "     $f" -ForegroundColor Yellow }
    Write-Host '     An abort is the prover dying, not a lemma failing, so the leg is GREEN: every model verified.' -ForegroundColor Yellow
    Write-Host '     What it is NOT is a clean measurement — read the pre-flight lines above for what else was on' -ForegroundColor Yellow
    Write-Host "     the machine, and the transcripts under $logsDir. A module that aborts repeatedly across legs is" -ForegroundColor Yellow
    Write-Host "     a finding about this machine or about that model's memory appetite, and is worth a phase." -ForegroundColor Yellow
}

if ($costFindings.Count -gt 0) {
    $labelledFindings = @($costFindings | Where-Object { $_.Label })
    $unlabelledFindings = @($costFindings | Where-Object { -not $_.Label })

    Write-Host "==== proofs: $($costFindings.Count) cost finding(s) against the budgets in ${budgetName}:" -ForegroundColor Yellow
    foreach ($f in $costFindings) { Write-Host "     $($f.Text)$($f.Label)" -ForegroundColor Yellow }
    Write-Host '     A budget is a smoke detector, not a gate: prover time varies by machine and by load,' -ForegroundColor Yellow
    Write-Host '     so one overshoot on a busy machine is noise and a persistent one is a regression.' -ForegroundColor Yellow
    Write-Host '     Bumping a budget is deliberate: new budgetSeconds + measuredSeconds + your phase in' -ForegroundColor Yellow
    Write-Host "     $budgetName, and a note saying what grew. See the README, ""Running it""." -ForegroundColor Yellow

    # The label's WHOLE consequence, in one place (Phase 171). A labelled finding has been shown,
    # by the modules this tree did not change, to be a measurement of the machine — so it is not a
    # re-seed obligation, and re-seeding a budget from it would raise a ceiling to fit a slow
    # afternoon, which is precisely how a budget stops meaning anything. That is the same judgement
    # Phase 162 had to make by hand, in prose, after departing from the seeding rule; what is new
    # is that the leg makes it and says so.
    if ($labelledFindings.Count -gt 0) {
        Write-Host "     $($labelledFindings.Count) finding(s) above are labelled CONTENDED PASS: the untouched modules on that run were slow too," -ForegroundColor Yellow
        Write-Host '     so those findings measure the machine. They are NOT a re-seed obligation — re-seeding from one' -ForegroundColor Yellow
        Write-Host '     raises a ceiling to fit a slow afternoon. Re-measure on a quiet run before touching a number.' -ForegroundColor Yellow
    }

    # -Strict promotes the UNLABELLED findings only. A session that asked for a red leg on cost
    # asked to be stopped by a regression, and a contended pass is not one; reddening on it would
    # make -Strict a coin toss on a shared machine, which is how a flag gets passed once and never
    # again. Coverage and shape findings belong to no run, are never labelled, and so always
    # promote — which is the half of -Strict's power this must not quietly remove.
    if ($Strict) {
        if ($unlabelledFindings.Count -gt 0) {
            Fail "the cost budget is exceeded and -Strict is on ($($unlabelledFindings.Count) unlabelled finding(s) above)"
        }
        Write-Host "     -Strict is on and the leg stays GREEN: every finding above is labelled CONTENDED PASS, which is a" -ForegroundColor Yellow
        Write-Host '     measurement of the machine rather than of a module. Re-run on a quiet machine to promote a real one.' -ForegroundColor Yellow
    }
}

Remove-InvocationCache
Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
