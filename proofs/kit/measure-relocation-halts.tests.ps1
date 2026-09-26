# measure-relocation-halts.tests.ps1 — the go-red proof for the instrument beside it.
#
# A measurement whose zero cannot be distinguished from a broken probe is not a measurement. So
# before `measure-relocation-halts.ps1`'s zero over a real ledger is worth anything, it has to be
# shown finding a halting pair that IS there — and shown NOT finding one that is there but is of the
# kind a wider record could never free. Both directions, on planted synthetic ledgers.
#
# Five cases:
#   1. PLANTED MOVE     — a move in one lane, a structure-write in another: C >= 1 AND M >= 1.
#                         This is the case the widening would free; if it does not register, a zero
#                         from a real ledger means nothing at all.
#   2. PLANTED REMOVE    — the same shape with a remove: C >= 1 AND M = 0. The discriminator run the
#                         OTHER way. Section 18's fact 2 proves the remove does not commute, so a
#                         kind-aware record still refuses it and it must NOT be counted as freeable.
#   3. CLEAN             — two lanes of structure-writes and content edits, no relocation at all:
#                         C = 0 and M = 0, with T > 0 so the zero is a measured zero rather than an
#                         empty run.
#   4. UNCLASSIFIED      — a ledger whose op kinds the map does not carry: reported SKIPPED and its
#                         kinds NAMED. The failure mode this exists for is a ledger format the
#                         script walks past while printing a reassuring 0.
#   5. SINGLE CHAIN      — a linear (`seq`/`prevHash`) ledger: T = 0 by construction, and the
#                         verdict SAYS so rather than reading as "no halts found".
#
# Read-only apart from a scratch directory it creates and removes under the OS temp path.
# `pwsh -NoProfile -File measure-relocation-halts.tests.ps1`; exit 0 = every case held.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$script:failures = 0
$script:cases = 0

function Assert-That {
    param([string] $What, [bool] $Holds, [string] $Detail = '')
    $script:cases++
    if ($Holds) { Write-Host "  PASS  $What" -ForegroundColor Green }
    else {
        $script:failures++
        Write-Host "  FAIL  $What" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor Red }
    }
}

$instrument = Join-Path $PSScriptRoot 'measure-relocation-halts.ps1'
if (-not (Test-Path -LiteralPath $instrument)) { throw "instrument not found: $instrument" }

$root = Join-Path ([IO.Path]::GetTempPath()) ("mrh-selftest-" + [Guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $root -Force | Out-Null

try {
    # ---- fixture writers ------------------------------------------------------------------------

    # One DAG lane record. `parents` is what marks the shape as a lane; the ids need only be
    # distinct — the instrument's cross-lane bound never walks ancestry (see its over-approximation 1).
    function New-LaneRecord {
        param([string] $Id, [string] $Parent, [string] $OpJson)
        '{"node":true,"id":"' + $Id + '","parents":["' + $Parent + '"],"actor":{"kind":"human","id":"t"},"op":' + $OpJson + '}'
    }

    function New-Store {
        param([string] $Name, [hashtable] $Lanes)   # lane name -> array of op JSON strings
        $dir = Join-Path $root $Name
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        foreach ($lane in $Lanes.Keys) {
            $lines = [System.Collections.Generic.List[string]]::new()
            $i = 0
            foreach ($op in $Lanes[$lane]) {
                $i++
                $lines.Add((New-LaneRecord "$lane-n$i" "$lane-n$($i-1)" $op))
            }
            [IO.File]::WriteAllLines((Join-Path $dir "$lane.jsonl"), $lines)
        }
        $dir
    }

    $move = '{"kind":"moveNode","target":"x","newParent":"q"}'
    $remove = '{"kind":"removeNode","target":"x"}'
    $insert = '{"kind":"insertChild","parent":"p","node":{"id":"n"}}'
    $reorder = '{"kind":"reorderChildren","parent":"q","order":[]}'

    function Measure-Store {
        param([string] $Dir, [string] $MapPath = '')
        $json = Join-Path $root ((Split-Path -Leaf $Dir) + '.report.json')
        $psArgs = @('-NoProfile', '-File', $instrument, $Dir, '-Json', $json)
        if ($MapPath) { $psArgs += @('-Map', $MapPath) }
        $out = & pwsh @psArgs 2>&1
        if (-not (Test-Path -LiteralPath $json)) {
            throw "instrument produced no report for $Dir`n$($out -join "`n")"
        }
        [IO.File]::ReadAllText($json) | ConvertFrom-Json
    }

    # ---- case 1: a planted MOVE versus a structure write must be FOUND, and counted freeable ----

    Write-Host ''
    Write-Host 'case 1 — planted move-versus-structural-write (the pair the widening would free)'
    $d1 = New-Store 'planted-move' @{ 'lane-a' = @($move); 'lane-b' = @($insert) }
    $r1 = Measure-Store $d1
    Assert-That 'T > 0 (the cross-lane pair was examined)' ($r1.totals.pairsT -ge 1) "T=$($r1.totals.pairsT)"
    Assert-That 'C >= 1 (the pinned clause could refuse it)' ($r1.totals.candidateC -ge 1) "C=$($r1.totals.candidateC)"
    Assert-That 'M >= 1 (a kind-aware record could free it)' ($r1.totals.freeableM -ge 1) "M=$($r1.totals.freeableM)"

    # ---- case 2: a planted REMOVE must be found as a candidate but NOT as freeable --------------

    Write-Host ''
    Write-Host 'case 2 — planted remove-versus-structural-write (section 18 fact 2: never freeable)'
    $d2 = New-Store 'planted-remove' @{ 'lane-a' = @($remove); 'lane-b' = @($insert) }
    $r2 = Measure-Store $d2
    Assert-That 'C >= 1 (the pinned clause refuses it — correctly)' ($r2.totals.candidateC -ge 1) "C=$($r2.totals.candidateC)"
    Assert-That 'M = 0 (no width of record frees a remove)' ($r2.totals.freeableM -eq 0) "M=$($r2.totals.freeableM)"

    # ---- case 3: a clean two-lane store must measure a real zero --------------------------------

    Write-Host ''
    Write-Host 'case 3 — no relocation anywhere: a MEASURED zero, not an empty run'
    $d3 = New-Store 'clean' @{ 'lane-a' = @($insert, $reorder); 'lane-b' = @($insert, $reorder) }
    $r3 = Measure-Store $d3
    Assert-That 'T > 0 (pairs were actually examined)' ($r3.totals.pairsT -ge 1) "T=$($r3.totals.pairsT)"
    Assert-That 'C = 0' ($r3.totals.candidateC -eq 0) "C=$($r3.totals.candidateC)"
    Assert-That 'M = 0' ($r3.totals.freeableM -eq 0) "M=$($r3.totals.freeableM)"
    Assert-That 'every ledger MEASURED (nothing skipped)' `
    (@($r3.ledgers | Where-Object { $_.verdict -notlike 'MEASURED*' }).Count -eq 0) `
    (($r3.ledgers | ForEach-Object { $_.verdict }) -join ' | ')

    # ---- case 4: an unclassified vocabulary must be SKIPPED and NAMED, never zero ---------------

    Write-Host ''
    Write-Host 'case 4 — a vocabulary the map does not carry is SKIPPED and named, never read as zero'
    $d4 = New-Store 'unknown-vocab' @{
        'lane-a' = @('{"kind":"somethingTheMapNeverHeardOf","id":"a"}')
        'lane-b' = @('{"kind":"anotherUnknownKind","id":"b"}')
    }
    $r4 = Measure-Store $d4
    Assert-That 'every ledger SKIPPED (vocabulary wholly unclassified)' `
    (@($r4.ledgers | Where-Object { $_.verdict -like 'SKIPPED*' }).Count -eq $r4.ledgers.Count) `
    (($r4.ledgers | ForEach-Object { $_.verdict }) -join ' | ')
    $named = @($r4.ledgers | ForEach-Object { $_.unclassified } | ForEach-Object { $_.kind })
    Assert-That 'the unclassified kinds are reported BY NAME' `
    (($named -contains 'somethingTheMapNeverHeardOf') -and ($named -contains 'anotherUnknownKind')) `
    ($named -join ',')

    # And the same ledger WITH a map that classifies those kinds must measure — proving the SKIP was
    # about the map and not about the file being unreadable.
    $mapPath = Join-Path $root 'vocab.map.json'
    [IO.File]::WriteAllText($mapPath, '{"kinds":{"somethingTheMapNeverHeardOf":"relocation-move","anotherUnknownKind":"structure-write"},"batchKinds":["batch"]}')
    $r4b = Measure-Store $d4 $mapPath
    Assert-That 'the same ledger MEASURES under a map that classifies it (the skip was the map, not the file)' `
    (($r4b.totals.candidateC -ge 1) -and ($r4b.totals.freeableM -ge 1)) `
    "C=$($r4b.totals.candidateC) M=$($r4b.totals.freeableM)"

    # ---- case 5: a single linear chain has no concurrency, and says so --------------------------

    Write-Host ''
    Write-Host 'case 5 — a linear chain reports a STRUCTURAL zero, not "no halts found"'
    $d5 = Join-Path $root 'linear'
    New-Item -ItemType Directory -Path $d5 -Force | Out-Null
    [IO.File]::WriteAllLines((Join-Path $d5 'ops.jsonl'), @(
            '{"seq":0,"actor":{"kind":"human","id":"t"},"op":' + $move + ',"prevHash":"","hash":"h0"}'
            '{"seq":1,"actor":{"kind":"human","id":"t"},"op":' + $insert + ',"prevHash":"h0","hash":"h1"}'
        ))
    $r5 = Measure-Store $d5
    Assert-That 'T = 0 (a single chain has no concurrent pairs)' ($r5.totals.pairsT -eq 0) "T=$($r5.totals.pairsT)"
    Assert-That 'the verdict NAMES the single-chain reason' `
    (@($r5.ledgers | Where-Object { $_.verdict -like '*single chain*' }).Count -ge 1) `
    (($r5.ledgers | ForEach-Object { $_.verdict }) -join ' | ')
    Assert-That 'the relocation was still SEEN (the chain was read, not skipped)' `
    ((($r5.ledgers | ForEach-Object { $_.relocationMove }) | Measure-Object -Sum).Sum -ge 1) `
    'relocationMove total'

    # ---- verdict --------------------------------------------------------------------------------

    Write-Host ''
    if ($script:failures -eq 0) {
        Write-Host "==== measure-relocation-halts self-test: $($script:cases) assertions, all held" -ForegroundColor Green
        exit 0
    }
    else {
        Write-Host "==== measure-relocation-halts self-test: $($script:failures) of $($script:cases) assertions FAILED" -ForegroundColor Red
        exit 1
    }
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
