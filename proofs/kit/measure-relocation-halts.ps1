# measure-relocation-halts.ps1 — how much would a relocation-KIND-aware footprint buy?
#
# WHAT THIS MEASURES, AND WHY IT EXISTS
#
# `proofs/TreeOps.fst` section 18 (`relocation_clause_is_necessary`) proves a PRECISION CEILING:
# `Ops.independent`'s last two clauses — a non-empty `UnknownParentWrites` refuses independence
# against any structural write — cannot be tightened over the `Footprint` record as it stands,
# because a `MoveNode` and a remove-shaped `Batch` present byte-identical footprints while only the
# move commutes with a structural write inside the relocated subtree. The named remedy is a WIDER
# record: one that says WHICH KIND of relocation it carries (move versus remove) and, for a move,
# its target parent. That is a wire-shape change to a public record.
#
# Before paying for it, one wants the number it would buy. This script computes an UPPER BOUND on it
# over recorded op-stream ledgers:
#
#   T — cross-lane op pairs examined (the total the two numbers below sit beside)
#   C — pairs the pinned clause could POSSIBLY refuse: a relocation-shaped op on one side and a
#       structure-writing op on the other. Halts attributable to the clause are a subset of C.
#   M — of those, the ones a kind-aware record could POSSIBLY free: every relocation involved is a
#       MOVE. A remove is the case section 18's fact 2 proves does NOT commute, so a kind-aware
#       record still refuses it; freeing it is not on the table at any width.
#
# M is therefore the ceiling on the widening's value over the measured ledgers, and `M = 0` is
# decisive: it says the widening frees nothing there, whatever else is true.
#
# THE TWO OVER-APPROXIMATIONS, both stated because both bound the answer in the SAME direction
# (they can only inflate C and M, never deflate them — which is what makes a zero conclusive and a
# non-zero merely a reason to look closer):
#
#   1. CONCURRENCY. Any two ops in DIFFERENT lane files are treated as concurrent. Real concurrency
#      is narrower (a pair is concurrent only when neither op's DAG node reaches the other), so T, C
#      and M are all upper bounds. A single-chain ledger has no lanes and contributes T = 0 — and
#      that is a STRUCTURAL zero, reported as such, not as "no halts found".
#   2. INDEPENDENCE. C asks only whether the pinned clause COULD fire on a pair. The other four
#      clauses of `Ops.independent` may already refuse the pair for an unrelated reason, in which
#      case widening the record frees nothing even though the pair is counted here.
#
# THE FALSIFIER — what would show a zero is an artefact of the method rather than a fact about the
# ledgers. A zero is only worth anything if the script can find a halting pair that IS there. Three
# guards, all exercised by `measure-relocation-halts.tests.ps1` beside this file:
#
#   * A PLANTED move-versus-insert pair in a synthetic two-lane ledger must be found (C >= 1,
#     M >= 1). If the planted pair does not register, a zero from a real ledger means nothing.
#   * A PLANTED remove-versus-insert pair must register as C >= 1 and M = 0 — the discriminator run
#     in the OTHER direction. A script that reported M >= 1 for a remove would be measuring
#     "relocations" and not "relocations a wider record could free".
#   * An op kind the projection map does not classify is counted as UNCLASSIFIED, reported BY NAME,
#     and the ledger's verdict is PARTIAL. A ledger whose vocabulary is wholly unrecognised is
#     SKIPPED. Neither is ever rendered as "zero halts" — the failure mode this guard exists for is
#     a ledger format the script silently walks past while printing a reassuring 0.
#
# THE PROJECTION MAP IS AN ARGUMENT, NOT A BUILT-IN. A footprint is not a property of an op's name:
# it is whatever the host's own `'Op -> Footprint` projection computes. So the caller supplies
# `-Map <file.json>` mapping each op kind to the footprint SHAPE that host's projection gives it:
#
#   { "kinds": { "<op kind>": "relocation-move" | "relocation-remove" | "structure-write"
#                             | "content-only" | "none" }, "batchKinds": ["batch"] }
#
#   relocation-move   — non-empty `UnknownParentWrites`, and the op names its destination parent
#                       (`Ops.footprint`'s `MoveNode` clause)
#   relocation-remove — non-empty `UnknownParentWrites` with no destination (`RemoveNode`)
#   structure-write   — writes a named parent's child-list (`InsertChild`, `ReorderChildren`)
#   content-only      — a content-write and/or read, no structural write
#   none              — addresses nothing (an empty footprint)
#
# With no `-Map` the skeleton vocabulary `Ops.footprint` itself produces is assumed, which is right
# for a ledger of skeleton ops and wrong for any domain ledger — hence the UNCLASSIFIED report.
#
# Read-only, offline, dependency-free: it parses committed bytes, runs no build, resolves no
# network, and writes nothing outside the path given to `-Json`.
#
# Usage:
#   pwsh -NoProfile -File measure-relocation-halts.ps1 <ledger-or-directory> [more…] [-Map m.json]
#   pwsh -NoProfile -File measure-relocation-halts.ps1 -SelfTest
#
# Every ledger location is an ARGUMENT. This script names no repository, store or path of its own.

[CmdletBinding()]
param(
    # Ledger files (`*.jsonl`) or directories to search for them. Any number.
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Ledger,

    # The op-kind -> footprint-shape map described above. Omitted: the skeleton vocabulary.
    [string] $Map,

    # Write the whole per-ledger report as JSON to this path, as well as printing the summary.
    [string] $Json,

    # Run the go-red self-test beside this file instead of measuring anything.
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

if ($SelfTest) {
    $t = Join-Path $PSScriptRoot 'measure-relocation-halts.tests.ps1'
    if (-not (Test-Path -LiteralPath $t)) {
        Write-Host "self-test script not found beside this one: $t" -ForegroundColor Red
        exit 2
    }
    & pwsh -NoProfile -File $t
    exit $LASTEXITCODE
}

if (-not $Ledger -or $Ledger.Count -eq 0) {
    Write-Host 'nothing to measure: pass one or more ledger files or directories (or -SelfTest).' -ForegroundColor Yellow
    exit 2
}

# ---- the projection map -------------------------------------------------------------------------

# The skeleton vocabulary, as `Ops.footprint` projects it. Used when the caller supplies no map.
# KIND LOOKUP IS CASE-INSENSITIVE (a PowerShell hashtable's is), so one spelling per shape covers
# both the lowerCamel wire form and a PascalCase rendering of the same case name.
$defaultKinds = @{
    'moveNode'        = 'relocation-move'
    'removeNode'      = 'relocation-remove'
    'insertChild'     = 'structure-write'
    'reorderChildren' = 'structure-write'
}
$batchKinds = @('batch', 'Batch')

$kindMap = $defaultKinds
$mapSource = 'built-in skeleton vocabulary (no -Map given)'

if ($Map) {
    $mapPath = [IO.Path]::GetFullPath($Map)
    if (-not (Test-Path -LiteralPath $mapPath)) { throw "map file not found: $mapPath" }
    $parsed = [IO.File]::ReadAllText($mapPath) | ConvertFrom-Json
    if (-not $parsed.kinds) { throw "map file has no 'kinds' member: $mapPath" }
    $kindMap = @{}
    foreach ($p in $parsed.kinds.PSObject.Properties) {
        $v = [string] $p.Value
        if ($v -notin @('relocation-move', 'relocation-remove', 'structure-write', 'content-only', 'none')) {
            throw "map: kind '$($p.Name)' has unrecognised shape '$v'"
        }
        $kindMap[$p.Name] = $v
    }
    if ($parsed.batchKinds) { $batchKinds = @($parsed.batchKinds) }
    $mapSource = $mapPath
}

# ---- collecting the ledgers ---------------------------------------------------------------------

$files = [System.Collections.Generic.List[string]]::new()
foreach ($l in $Ledger) {
    $full = [IO.Path]::GetFullPath($l)
    if (Test-Path -LiteralPath $full -PathType Container) {
        Get-ChildItem -LiteralPath $full -Filter '*.jsonl' -File -Recurse |
            ForEach-Object { $files.Add($_.FullName) }
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) { $files.Add($full) }
    else { Write-Host "  not found, skipped: $full" -ForegroundColor Yellow }
}
if ($files.Count -eq 0) { Write-Host 'no ledger files found.' -ForegroundColor Yellow; exit 2 }

# ---- op extraction ------------------------------------------------------------------------------

# One record's ops, flattened. A record wraps its op at `.op`; some hosts wrap it again in an
# attribution envelope (`{actor, session, at, op}`), so descend while the object has NO `kind` of its
# own but DOES carry an `op` — an envelope is exactly the thing that names no op kind. Descending on
# `op` alone would walk past a real op that happened to carry a member of that name.
# A `batch`-shaped op is flattened into its members.
function Get-RecordOps {
    param($record)
    $out = [System.Collections.Generic.List[object]]::new()

    $op = $record.op
    while ($null -ne $op -and $null -eq $op.kind -and $null -ne $op.op) { $op = $op.op }
    if ($null -eq $op) { return $out }

    $stack = [System.Collections.Generic.Stack[object]]::new()
    $stack.Push($op)
    while ($stack.Count -gt 0) {
        $o = $stack.Pop()
        if ($null -eq $o) { continue }
        $k = [string] $o.kind
        if ($k -and ($batchKinds -contains $k) -and $null -ne $o.ops) {
            foreach ($m in @($o.ops)) { $stack.Push($m) }
        }
        else { $out.Add($o) }
    }
    $out
}

# ---- per-ledger census --------------------------------------------------------------------------

$reports = [System.Collections.Generic.List[object]]::new()

foreach ($f in $files) {
    $shapeDag = 0
    $shapeLinear = 0
    $shapeOther = 0
    $malformed = 0
    $nOps = 0
    $nMove = 0
    $nRemove = 0
    $nStruct = 0
    $unclassified = @{}

    foreach ($line in [IO.File]::ReadLines($f)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $rec = $null
        try { $rec = $line | ConvertFrom-Json } catch { $malformed++; continue }
        if ($null -eq $rec) { $malformed++; continue }

        if ($null -ne $rec.parents -or $rec.node) { $shapeDag++ }
        elseif ($null -ne $rec.seq -or $null -ne $rec.prevHash) { $shapeLinear++ }
        else { $shapeOther++ }

        foreach ($o in (Get-RecordOps $rec)) {
            $nOps++
            $k = [string] $o.kind
            if (-not $k) { $k = '<no kind member>' }
            if (-not $kindMap.ContainsKey($k)) {
                $unclassified[$k] = 1 + ($unclassified[$k] ?? 0)
                continue
            }
            switch ($kindMap[$k]) {
                'relocation-move' { $nMove++; $nStruct++ }      # a move writes its destination too
                'relocation-remove' { $nRemove++ }
                'structure-write' { $nStruct++ }
                default { }
            }
        }
    }

    # A ledger of DAG records is one LANE; the cross-lane pairing happens across files below. A
    # ledger that is a single linear chain carries no concurrency at all and is marked so.
    $isLane = ($shapeDag -gt 0)
    $nKnown = $nOps - (($unclassified.Values | Measure-Object -Sum).Sum ?? 0)

    $verdict =
    if ($nOps -eq 0) { 'SKIPPED (no ops parsed)' }
    elseif ($nKnown -eq 0) { 'SKIPPED (vocabulary wholly unclassified by the map)' }
    elseif ($unclassified.Count -gt 0) { 'PARTIAL (some kinds unclassified — see below)' }
    elseif (-not $isLane) { 'MEASURED (single chain — no concurrency by construction)' }
    else { 'MEASURED' }

    $reports.Add([pscustomobject]@{
            path            = $f
            shape           = if ($isLane) { 'lane (DAG)' } elseif ($shapeLinear -gt 0) { 'linear chain' } else { 'unrecognised' }
            records         = $shapeDag + $shapeLinear + $shapeOther
            malformedLines  = $malformed
            ops             = $nOps
            classifiedOps   = $nKnown
            relocationMove  = $nMove
            relocationRemove= $nRemove
            structureWrites = $nStruct
            unclassified    = ($unclassified.GetEnumerator() | Sort-Object -Property Value -Descending |
                ForEach-Object { [pscustomobject]@{ kind = $_.Key; count = $_.Value } })
            isLane          = $isLane
            verdict         = $verdict
        })
}

# ---- the cross-lane bound -----------------------------------------------------------------------

# Lanes are grouped by their containing directory: two files in one directory are two lanes of ONE
# store, and only lanes of the same store are ever folded against each other.
$byStore = $reports | Where-Object isLane | Group-Object { [IO.Path]::GetDirectoryName($_.path) }

$storeRows = [System.Collections.Generic.List[object]]::new()
$totT = [long] 0; $totC = [long] 0; $totM = [long] 0

foreach ($g in $byStore) {
    $lanes = @($g.Group)
    $T = [long] 0; $C = [long] 0; $M = [long] 0
    for ($i = 0; $i -lt $lanes.Count; $i++) {
        for ($j = $i + 1; $j -lt $lanes.Count; $j++) {
            $a = $lanes[$i]; $b = $lanes[$j]
            $T += [long] $a.ops * [long] $b.ops
            # a relocation on one side against a structure-write on the other, both directions
            $C += [long] ($a.relocationMove + $a.relocationRemove) * [long] $b.structureWrites
            $C += [long] ($b.relocationMove + $b.relocationRemove) * [long] $a.structureWrites
            $M += [long] $a.relocationMove * [long] $b.structureWrites
            $M += [long] $b.relocationMove * [long] $a.structureWrites
        }
    }
    $storeRows.Add([pscustomobject]@{
            store = $g.Name; lanes = $lanes.Count; pairsT = $T; candidateC = $C; freeableM = $M
        })
    $totT += $T; $totC += $C; $totM += $M
}

# ---- report -------------------------------------------------------------------------------------

Write-Host ''
Write-Host '== measure-relocation-halts — an upper bound on what a relocation-kind-aware footprint frees'
Write-Host "   projection map: $mapSource"
Write-Host ''

foreach ($r in $reports) {
    Write-Host ("  {0}" -f $r.path)
    Write-Host ("    {0} · records {1} · ops {2} · reloc(move/remove) {3}/{4} · structure-writes {5} · {6}" -f `
            $r.shape, $r.records, $r.ops, $r.relocationMove, $r.relocationRemove, $r.structureWrites, $r.verdict)
    if ($r.malformedLines -gt 0) {
        Write-Host ("    WARNING: {0} line(s) did not parse as JSON and were NOT measured" -f $r.malformedLines) -ForegroundColor Yellow
    }
    if ($r.unclassified.Count -gt 0) {
        $top = ($r.unclassified | Select-Object -First 8 | ForEach-Object { "$($_.kind)=$($_.count)" }) -join ', '
        Write-Host ("    unclassified kinds ({0} distinct): {1}" -f $r.unclassified.Count, $top) -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '  per store (lanes folded against each other):'
foreach ($s in $storeRows) {
    Write-Host ("    {0}  lanes {1}  T {2}  C {3}  M {4}" -f $s.store, $s.lanes, $s.pairsT, $s.candidateC, $s.freeableM)
}

Write-Host ''
Write-Host ("  TOTAL   cross-lane op pairs T = {0}" -f $totT)
Write-Host ("          pinned-clause candidates C = {0}   (upper bound on halts the clause caused)" -f $totC)
Write-Host ("          kind-aware-freeable    M = {0}   (upper bound on what the widening buys)" -f $totM)
Write-Host ''

$skipped = @($reports | Where-Object { $_.verdict -like 'SKIPPED*' })
$partial = @($reports | Where-Object { $_.verdict -like 'PARTIAL*' })
if ($skipped.Count -gt 0) {
    Write-Host ("  {0} ledger(s) SKIPPED — their vocabulary is not in the map, so they are NOT counted as zero." -f $skipped.Count) -ForegroundColor Yellow
}
if ($partial.Count -gt 0) {
    Write-Host ("  {0} ledger(s) PARTIAL — some op kinds unclassified; C and M are bounds over the classified part only." -f $partial.Count) -ForegroundColor Yellow
}

if ($Json) {
    $out = [pscustomobject]@{
        mapSource = $mapSource
        totals    = [pscustomobject]@{ pairsT = $totT; candidateC = $totC; freeableM = $totM }
        stores    = $storeRows
        ledgers   = $reports
    }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Json), ($out | ConvertTo-Json -Depth 8))
    Write-Host "  report written: $([IO.Path]::GetFullPath($Json))"
}

# Exit 0 always on a completed measurement — this is an instrument, not a gate. A caller that wants
# a gate compares M against its own threshold.
exit 0
