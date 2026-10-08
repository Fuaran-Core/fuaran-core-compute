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
# FOUR ARMS (and, since Phase 309, three TWIN arms, F-H, since Phase 393 the PIN-RESOLUTION arms,
# R, which need no prover and run first, and since Phase 402 the FLOOR-OS arms, O, and the CACHE
# PROVENANCE arms, P). The first prover arm is the control that makes the other three mean something:
#
#   A. GREEN CONTROL — a true model, no host step: exit 0 AND `proofs: green`. If this is red, the
#                      scratch apparatus is broken and a red B–D would prove nothing.
#   B. HOST BUILD    — a -HostProjectFile that does not exist: exit non-zero, no green.
#   C. HOST RUN      — a host project that builds and cannot run the filter: exit non-zero, no green.
#   D. CHECK         — a model with a type error: exit non-zero, no green.
#
# The R arms run anywhere. The rest need the pinned prover; where there is none it says NOT RUN
# and exits 2 — never 0, because "nothing was checked" must not read as "everything held". `proofs/check.ps1` runs it after a
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

# ---- R. PIN RESOLUTION, PER OPERATING SYSTEM (Phase 393) -------------------------------------------

# These arms need NO prover: `-ResolveOnly` resolves the pin entry the download path would fetch and
# stops, so they run first and run everywhere. The committed pin must resolve for both OSes it
# declares — and for the host's OS — and a pin that cannot serve an OS must be REFUSED NAMING it,
# exit 2, rather than fetching the wrong release or reading as green.
$resolveDir = Join-Path $WorkDir 'resolve'
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $resolveDir | Out-Null
$committedPin = Get-Content $pinFile -Raw | ConvertFrom-Json

function Invoke-Resolve([string] $pinPath, [string] $platform) {
    $resolveArgs = @{ Modules = @('Resolve'); ProofsDir = $resolveDir; PinFile = $pinPath; ResolveOnly = $true }
    if ($platform) { $resolveArgs.Platform = $platform }
    Push-Location $WorkDir
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit @resolveArgs *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch { $lines = @($_.ToString()); $code = 1 }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Text = ($lines -join ' ') }
}

# A copy of the committed pin with one edit applied, written to the scratch directory.
function New-ScratchPin([string] $name, [scriptblock] $edit) {
    $copy = Get-Content $pinFile -Raw | ConvertFrom-Json
    & $edit $copy
    $path = Join-Path $resolveDir $name
    $copy | ConvertTo-Json -Depth 4 | Set-Content $path
    $path
}

foreach ($os in 'windows', 'linux') {
    $r = Invoke-Resolve $pinFile $os
    $asset = $committedPin.$os.asset
    Assert-That "R. RESOLVE — the committed pin resolves '$os' to its own entry" ($r.Exit -eq 0 -and $asset -and $r.Text.Contains($asset)) "exit $($r.Exit): $($r.Text)"
}
$hostOs = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } elseif ($IsMacOS) { 'macos' } else { '' }
$r = Invoke-Resolve $pinFile ''
if ($hostOs -and $committedPin.$hostOs) {
    Assert-That "R. RESOLVE — with no -Platform the HOST's OS ($hostOs) is resolved" ($r.Exit -eq 0 -and $r.Text.Contains($committedPin.$hostOs.asset)) "exit $($r.Exit): $($r.Text)"
}
else {
    Assert-That "R. REFUSE — a host OS the pin does not declare is refused by name" ($r.Exit -eq 2) "exit $($r.Exit): $($r.Text)"
}

$r = Invoke-Resolve $pinFile 'macos'
Assert-That "R. REFUSE — an OS the pin has no entry for exits 2, naming it" ($r.Exit -eq 2 -and $r.Text.Contains("pins no 'macos' release")) "exit $($r.Exit): $($r.Text)"

$noLinux = New-ScratchPin 'no-linux.json' { param($p) $p.PSObject.Properties.Remove('linux') }
$r = Invoke-Resolve $noLinux 'linux'
Assert-That "R. REFUSE — a pin without a linux entry refuses linux by name" ($r.Exit -eq 2 -and $r.Text.Contains("pins no 'linux' release")) "exit $($r.Exit): $($r.Text)"
$r = Invoke-Resolve $noLinux 'windows'
Assert-That "R. RESOLVE — and the same pin still resolves windows" ($r.Exit -eq 0) "exit $($r.Exit): $($r.Text)"

$noHash = New-ScratchPin 'no-hash.json' { param($p) $p.linux.PSObject.Properties.Remove('sha256') }
$r = Invoke-Resolve $noHash 'linux'
Assert-That "R. REFUSE — an entry with no sha256 is refused, naming the field" ($r.Exit -eq 2 -and $r.Text.Contains('incomplete') -and $r.Text.Contains('sha256')) "exit $($r.Exit): $($r.Text)"

$stale = New-ScratchPin 'stale.json' { param($p) $p.linux.asset = $p.linux.asset.Replace($p.fstar, 'v2000.01.01') }
$r = Invoke-Resolve $stale 'linux'
Assert-That "R. REFUSE — an entry naming a different release than the pin is refused" ($r.Exit -eq 2 -and $r.Text.Contains('different release')) "exit $($r.Exit): $($r.Text)"

# The prover, resolved the way the leg resolves it, WITHOUT the leg's download: a test that fetched
# a 100 MB release as a side effect would be a surprise, and `check.ps1` has already fetched it.
$fstarHome = $null
if ($env:FSTAR_HOME) { $fstarHome = $env:FSTAR_HOME }
elseif (Test-Path (Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe')) { $fstarHome = Join-Path $ProofsDir '.fstar/fstar' }
if (-not $fstarHome -or -not (Test-Path (Join-Path $fstarHome 'bin/fstar.exe'))) {
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($script:failures.Count -gt 0) {
        Write-Host "==== leg-tests: RED — $($script:failures.Count) of $script:cases pin-resolution assertion(s) failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "==== leg-tests: NOT RUN — no pinned prover ($script:cases pin-resolution assertions held; the prover arms need it). Set FSTAR_HOME to an F* $pinnedVersion release, or run ``pwsh ./proofs/check.ps1`` once to install it under proofs/.fstar/." -ForegroundColor Yellow
    exit 2
}

# ---- the scratch proofs directory ----------------------------------------------------------------

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
$scratch = Join-Path $WorkDir 'proofs'
New-Item -ItemType Directory -Force $scratch | Out-Null
Copy-Item $pinFile (Join-Path $scratch 'fstar-pin.json')

# Two models: one true, one refuted by a plain type error (F* Error 19).
Set-Content (Join-Path $scratch 'LegGood.fst') "module LegGood`n`nlet one : nat = 1`n"
Set-Content (Join-Path $scratch 'LegBad.fst') "module LegBad`n`nlet minus_one : nat = -1`n"
# A TRUE model whose query name contains "fails": a success line naming it must not read as a failure.
Set-Content (Join-Path $scratch 'LegFailsName.fst') "module LegFailsName`n`nlet this_never_fails (x: nat) : nat = x + 1`n"

# Budgets for both, with floors of 0 — a module that checks in a second must not trip the floor.
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegBad'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; floorSeconds = 0 }
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
# Recorded 2026-09-25: a sibling copy of this kit printed `<module>.fst verified` over a refused
# model and went red only later. The per-module line is held too, not only the closing verdict.
Assert-That 'D. CHECK — and prints NO LegBad.fst verified line' (-not [bool](@($d.Lines -match 'LegBad\.fst verified').Count)) (Show-Tail $d)
Assert-That 'D. CHECK — and fails at the CHECK step, naming the module' ([bool](@($d.Lines -match '==== proofs: LegBad\.fst did NOT verify').Count)) (Show-Tail $d)

# ---- E. A SUCCESS LINE THAT NAMES A FAILURE ------------------------------------------------------

# Recorded 2026-09-26: the zero-exit diagnostic check matched `Quake[^\n]*fail` and refused TreeOps.fst,
# whose query `relocation_diamond_fails_for_a_remove` had `proved 8/8 goals`. A quake line is a failure
# unless it reads `proved N/N goals`; a query's NAME is not a verdict.
$e = Invoke-Leg ($base + @{ Modules = @('LegFailsName'); ProofOnly = @('LegFailsName') })
Assert-That 'E. NAMES — a true model whose query name contains "fails" exits 0' ($e.Exit -eq 0) "exit $($e.Exit): $(Show-Tail $e)"
Assert-That 'E. NAMES — and prints proofs: green' $e.Green (Show-Tail $e)

# ---- F. TWINS (Phase 309) --------------------------------------------------------------------------

# With -Twins, an EXTRACTED model must carry a normalised `twins` list. LegTwinned does and is green
# (extracted under -Extract into the scratch oracle); LegGood, extracted and twinless, is refused at
# the TWIN step before the prover runs; a -ProofOnly model needs none.
Set-Content (Join-Path $scratch 'LegTwinned.fst') @"
module LegTwinned

let double (x: nat) : nat = x + x

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l: list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

let twins : list twin = [ { tname = "double-two"; tholds = (fun () -> double 2 = 4) } ]

let _ = assert_norm (twins_hold twins == true)
"@
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegBad'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegTwinned'; budgetSeconds = 60; floorSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.json')

New-Item -ItemType Directory -Force (Join-Path $scratch 'oracle') | Out-Null
$f = Invoke-Leg ($base + @{ Modules = @('LegTwinned'); Twins = $true; Extract = $true })
Assert-That 'F. TWINS — an extracted model with normalised twins exits 0' ($f.Exit -eq 0) "exit $($f.Exit): $(Show-Tail $f)"
Assert-That 'F. TWINS — and says every extracted model is covered' ([bool](@($f.Lines -match 'twin evaluation covers all 1 extracted model').Count)) (Show-Tail $f)

$g = Invoke-Leg ($base + @{ Modules = @('LegGood'); Twins = $true; Extract = $true })
Assert-That 'G. TWINS — an extracted model with no twins exits NON-ZERO' ($g.Exit -ne 0) "exit $($g.Exit): $(Show-Tail $g)"
Assert-That 'G. TWINS — and names it at the TWIN step' ([bool](@($g.Lines -match 'twin evaluation does not cover every extracted model: LegGood').Count)) (Show-Tail $g)
Assert-That 'G. TWINS — and does not print proofs: green' (-not $g.Green) (Show-Tail $g)

$h = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); Twins = $true })
Assert-That 'H. TWINS — a -ProofOnly model needs no twins' ($h.Exit -eq 0) "exit $($h.Exit): $(Show-Tail $h)"

# ---- O. THE FLOORS' OS (Phase 402) -----------------------------------------------------------------

# A floor is enforced on the OS `floorSeeding.os` names and on no other. LegGood checks in about a
# second, so a 50s floor is a breach wherever it is enforced: red on the host's own OS, and on any
# other OS not enforced, the leg green and saying why.
$otherOs = if ($hostOs -eq 'linux') { 'windows' } else { 'linux' }
function Set-FloorBudget([string] $os) {
    @{
        kind         = 'proofModules'
        floorSeeding = @{ os = $os }
        modules      = @(@{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 50 })
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.floor.json')
}

Set-FloorBudget $hostOs
$o1 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.floor.json') })
Assert-That "O. FLOOR — a floor seeded on this OS ($hostOs) is enforced: a breach exits NON-ZERO" ($o1.Exit -ne 0 -and -not $o1.Green) "exit $($o1.Exit): $(Show-Tail $o1)"
Assert-That 'O. FLOOR — and names the floor it broke' ([bool](@($o1.Lines -match 'under its 50s floor').Count)) (Show-Tail $o1)

Set-FloorBudget $otherOs
$o2 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.floor.json') })
Assert-That "O. FLOOR — a floor seeded on $otherOs is not enforced on $($hostOs): exit 0 and green" ($o2.Exit -eq 0 -and $o2.Green) "exit $($o2.Exit): $(Show-Tail $o2)"
Assert-That 'O. FLOOR — and says the floors are not enforced here, and why' ([bool](@($o2.Lines -match "seeded on $otherOs and are NOT enforced").Count)) (Show-Tail $o2)

Set-FloorBudget 'solaris'
$o3 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.floor.json') })
Assert-That 'O. FLOOR — a floorSeeding.os naming no OS is refused, naming the key' ($o3.Exit -ne 0 -and -not $o3.Green -and [bool](@($o3.Lines -match 'floorSeeding.os').Count)) "exit $($o3.Exit): $(Show-Tail $o3)"

# ---- P. CACHE PROVENANCE (Phase 402) ----------------------------------------------------------------

# The second writer, caught directly. LegUses depends on LegGood; LegThird depends on nothing.
Set-Content (Join-Path $scratch 'LegUses.fst') "module LegUses`n`nopen LegGood`n`nlet two : nat = one + one`n"
Set-Content (Join-Path $scratch 'LegThird.fst') "module LegThird`n`nlet three : nat = 3`n"
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegUses'; budgetSeconds = 60; floorSeconds = 0 }
        @{ module = 'LegThird'; budgetSeconds = 60; floorSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.provenance.json')
$provenance = $base + @{ BudgetFile = (Join-Path $scratch 'modules.provenance.json') }

# The CONTROL: a dependency checked first leaves its own `.checked` file for the dependent to read,
# which is this run's own write and no second writer.
$p0 = Invoke-Leg ($provenance + @{ Modules = @('LegGood', 'LegUses'); ProofOnly = @('LegGood', 'LegUses') })
Assert-That 'P. PROVENANCE CONTROL — a dependency checked before its dependent exits 0 and green' ($p0.Exit -eq 0 -and $p0.Green) "exit $($p0.Exit): $(Show-Tail $p0)"

# The second CONTROL, dependent first. The pinned prover writes a module's `.checked` file only when
# it checks that module itself, never for a dependency it checks on the way (measured 2026-10-07), so
# checking LegUses leaves LegGood's own file absent and LegGood's check after it is still cold. If a
# prover release ever starts caching dependencies, this arm goes red, and the rule's premise with it.
$p1 = Invoke-Leg ($provenance + @{ Modules = @('LegUses', 'LegGood'); ProofOnly = @('LegUses', 'LegGood') })
Assert-That 'P. PROVENANCE CONTROL — a dependent checked before its dependency leaves the dependency cold: exit 0 and green' ($p1.Exit -eq 0 -and $p1.Green) "exit $($p1.Exit): $(Show-Tail $p1)"

# A SECOND WRITER, planted SYNCHRONOUSLY through the leg's -AfterInvocation seam: after LegGood's
# invocation, at the one point between invocations where a foreign writer acts. Until Phase 402's
# rework this was a concurrent runspace polling for LegGood's `.checked` file; on a Linux runner,
# where these models check in well under a second, it could start after the leg had already reached
# LegThird, and the arm read green on some runs. Nothing here depends on scheduling now: the plant
# runs on the leg's own thread, before the leg reads the cache again.

# 1. A file APPEARS — LegThird's own `.checked`, before LegThird's turn.
$p2 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName', 'LegThird'); ProofOnly = @('LegGood', 'LegFailsName', 'LegThird')
        AfterInvocation = {
            param($module, $run, $cacheDir)
            if ($module -eq 'LegGood') { Set-Content (Join-Path $cacheDir 'LegThird.fst.checked') 'forged by a second writer' }
        }
    })
Assert-That 'P. SECOND WRITER — a file another writer put in the cache is refused' ($p2.Exit -ne 0 -and -not $p2.Green) "exit $($p2.Exit): $(Show-Tail $p2)"
Assert-That 'P. SECOND WRITER — before the next module is checked, naming the file' ([bool](@($p2.Lines -match 'SECOND WRITER.*before LegFailsName\.fst.*LegThird\.fst\.checked appeared').Count)) (Show-Tail $p2)
Assert-That 'P. SECOND WRITER — and prints no LegFailsName.fst or LegThird.fst verified line' (-not [bool](@($p2.Lines -match '(LegFailsName|LegThird)\.fst verified').Count)) (Show-Tail $p2)

# 2. A file is REWRITTEN with the same length and its old timestamp restored — invisible to any
# check that reads the clock, which is why the state is the bytes' hash.
$p3 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName'); ProofOnly = @('LegGood', 'LegFailsName')
        AfterInvocation = {
            param($module, $run, $cacheDir)
            if ($module -eq 'LegGood') {
                $path = Join-Path $cacheDir 'LegGood.fst.checked'
                $stamp = (Get-Item -LiteralPath $path).LastWriteTimeUtc
                $bytes = [System.IO.File]::ReadAllBytes($path)
                $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
                [System.IO.File]::WriteAllBytes($path, $bytes)
                (Get-Item -LiteralPath $path).LastWriteTimeUtc = $stamp
            }
        }
    })
Assert-That 'P. SECOND WRITER — a same-length rewrite with its timestamp restored is refused' ($p3.Exit -ne 0 -and -not $p3.Green) "exit $($p3.Exit): $(Show-Tail $p3)"
Assert-That 'P. SECOND WRITER — naming the rewritten file' ([bool](@($p3.Lines -match 'SECOND WRITER.*LegGood\.fst\.checked was rewritten').Count)) (Show-Tail $p3)

# 3. The seam itself changes nothing: a plant that writes nothing leaves the leg green.
$p4 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName'); ProofOnly = @('LegGood', 'LegFailsName')
        AfterInvocation = { param($module, $run, $cacheDir) }
    })
Assert-That 'P. SEAM CONTROL — an -AfterInvocation that writes nothing leaves the leg green' ($p4.Exit -eq 0 -and $p4.Green) "exit $($p4.Exit): $(Show-Tail $p4)"

Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue

if ($script:failures.Count -gt 0) {
    Write-Host "==== leg-tests: RED — $($script:failures.Count) of $script:cases assertion(s) failed; the leg reported a step it could not run as green" -ForegroundColor Red
    exit 1
}
Write-Host "==== leg-tests: $script:cases assertions held — every step's failure is REFUSED, not merely printed" -ForegroundColor Green
exit 0
