#Requires -Version 7.0
<#
.SYNOPSIS
    Records the clock leg's tick-baseline table for THIS machine's architecture (Phase 439).

.DESCRIPTION
    The corpus tick cases on the clock leg hold every one-row tick (edit and append, every corpus
    node, 1,000 / 20,000 / 100,000 rows) to

        tick <= max(1.6 x full, 1.5 x baseline) + floor

    where `baseline` is the tick the same cell cost on the tree BEFORE Phase 423 (`ccbddc7`, the
    last tree whose full evaluation paid the `Table` boundary), recorded per CPU architecture in
    calibration units (`ScalingTests.Calibration`), so that a faster or slower machine of the same
    architecture reads the same figure. The table lives in `ScalingTests.fs` (`tickBaselines`).

    This script records one. It exports the baseline commit with `git archive` into a directory
    BESIDE this repository (so `nuget.config`'s relative local feed resolves exactly as it does
    here), builds its test project in Release, runs that tree's whole clock leg (as the gate runs
    it) `-Runs` times (at least three), refuses any run in which the saturation guard discarded a
    window, takes each cell's best reading WITHIN each run in units of that run's calibration
    baseline, and records the MEDIAN of those per-run bests (operator ruling 2026-10-12, the
    second: the best over all runs is a reading the gate's own runs rarely reach on a noisy cell).
    It prints the F# block to paste into `tickBaselines` under the architecture it names, and it
    is the only way that table is written. A cell not measured in every run fails the script: a
    table with a hole would hold that cell to the ratio arm alone without anyone having decided so.

    Run it on a quiet machine. Refreshing a table that exists is a decision recorded in
    DECISIONS.md (D19), not a routine re-run: see there for who decides and when.

.EXAMPLE
    pwsh ./gates/record-tick-baselines.ps1
    pwsh ./gates/record-tick-baselines.ps1 -Runs 5 -Keep
#>
[CmdletBinding()]
param(
    # The tree whose ticks are the baseline (operator ruling 2026-10-12): the last pre-423 commit.
    [string] $Commit = 'ccbddc7',
    # Clock legs to take each cell's best reading over.
    [int] $Runs = 3,
    # Where the generated block is written, besides being printed.
    [string] $OutFile = '',
    # Keep the exported tree after the run (it is removed by default).
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot/..

$arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
$repo = (Get-Location).Path
$export = Join-Path (Split-Path $repo -Parent) ("{0}-tick-baseline-{1}" -f (Split-Path $repo -Leaf), $Commit)
if ($OutFile -eq '') { $OutFile = Join-Path ([System.IO.Path]::GetTempPath()) "tick-baselines-$arch.fs.txt" }

Write-Host "==== tick baselines: architecture $arch, commit $Commit, $Runs run(s), tree $export"

if (Test-Path $export) { Remove-Item -Recurse -Force $export }
New-Item -ItemType Directory -Force $export | Out-Null
$archive = Join-Path (Split-Path $export -Parent) ("{0}.zip" -f (Split-Path $export -Leaf))
$global:LASTEXITCODE = 0
git archive --format=zip -o $archive $Commit
if ($LASTEXITCODE -ne 0) { throw "git archive $Commit failed (exit $LASTEXITCODE)" }
Expand-Archive -Path $archive -DestinationPath $export -Force
Remove-Item $archive

$global:LASTEXITCODE = 0
dotnet build (Join-Path $export 'tests/Fuaran.Core.Compute.Tests') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "building $Commit's tests failed (exit $LASTEXITCODE)" }
$dll = Join-Path $export 'tests/Fuaran.Core.Compute.Tests/bin/Release/net10.0/Fuaran.Core.Compute.Tests.dll'
if (-not (Test-Path $dll)) { throw "no test assembly at $dll" }

# kind|node|rows -> one best reading per run, in calibration units (operator ruling 2026-10-12, the
# second: a cell's baseline is the MEDIAN over the runs of each run's best reading, not the best over
# all runs - the overall minimum of a noisy cell is a reading the gate's own runs rarely reach).
$perRun = @{}
$tick = [regex]'\[(corpus|append) tick\] (.+?) @ +(\d+): tick +([0-9.]+) ms vs full'
$calibration = [regex]'calibration baseline ([0-9.]+) ms \(best of'

if ($Runs -lt 3) { throw "-Runs ${Runs}: the ruling asks for at least three full legs" }

foreach ($run in 1..$Runs) {
    $log = Join-Path $export "clock-run$run.log"
    Write-Host "==== tick baselines: run $run of $Runs -> $log"
    $global:LASTEXITCODE = 0
    # The WHOLE leg, as the gate runs it: measured alone, the two corpus cases read 10 to 25 percent
    # lower than they do after the leg's other cases (measured on Arm64 in Phase 439), and a baseline
    # recorded in a context the gate never runs in is not the tick the gate compares.
    dotnet $dll --clock-leg *> $log
    $code = $LASTEXITCODE
    if ($code -notin 0, 1, 3) { throw "the clock leg exited $code (see $log)" }

    # A run with ANY discarded window is not a recording run: the ruling asks for legs with none.
    $discarded = @(Select-String -Path $log -Pattern '\[clock\] .*: window SATURATED').Count
    if ($discarded -gt 0) {
        $global:LASTEXITCODE = 1
        throw "run $run discarded $discarded window(s) as saturated - re-run the script on a quieter machine (log $log)"
    }

    $unit = $null
    $best = @{}
    $pending = [System.Collections.Generic.List[object]]::new()
    foreach ($line in Get-Content $log) {
        if ($null -eq $unit) {
            $m = $calibration.Match($line)
            if ($m.Success) { $unit = [double]::Parse($m.Groups[1].Value, [cultureinfo]::InvariantCulture) }
        }
        if ($line -match '\[clock\] .*: attempt \d+ of') { $pending.Clear(); continue }
        $m = $tick.Match($line)
        if ($m.Success) { $pending.Add($m); continue }
        if ($line -match '\[clock\] .*: window counted') {
            foreach ($p in $pending) {
                $kind = if ($p.Groups[1].Value -eq 'corpus') { 'edit' } else { 'append' }
                $key = '{0}|{1}|{2}' -f $kind, $p.Groups[2].Value.Trim(), $p.Groups[3].Value
                $u = [double]::Parse($p.Groups[4].Value, [cultureinfo]::InvariantCulture) / $unit
                if (-not $best.ContainsKey($key) -or $u -lt $best[$key]) { $best[$key] = $u }
            }
            $pending.Clear()
        }
    }
    if ($null -eq $unit) { throw "run $run printed no calibration baseline (see $log)" }
    foreach ($key in $best.Keys) {
        if (-not $perRun.ContainsKey($key)) { $perRun[$key] = [System.Collections.Generic.List[double]]::new() }
        $perRun[$key].Add($best[$key])
    }
}

# Ten nodes x three sizes x two kinds, each measured in EVERY run. A hole is a failure, never a
# silently shorter table or a median over fewer runs.
$expected = 60
$complete = @($perRun.Keys | Where-Object { $perRun[$_].Count -eq $Runs }).Count
if ($perRun.Count -ne $expected -or $complete -ne $expected) {
    $global:LASTEXITCODE = 1
    throw "only $complete of $expected cells were measured in every run - re-run on a quieter machine (logs in $export)"
}

# The median of the per-run bests (the mean of the middle two for an even run count).
$median = @{}
foreach ($key in $perRun.Keys) {
    $s = @($perRun[$key] | Sort-Object)
    $median[$key] = if ($s.Count % 2 -eq 1) { $s[($s.Count - 1) / 2] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 }
}
$best = $median

$inv = [cultureinfo]::InvariantCulture
$rows = @($best.Keys | Sort-Object { ($_ -split '\|')[0] }, { ($_ -split '\|')[1] }, { [int](($_ -split '\|')[2]) } | ForEach-Object {
    $k, $node, $n = $_ -split '\|'
    '                ("{0}", "{1}", {2}), {3}' -f $k, $node, $n, $best[$_].ToString('0.000000', $inv)
})
$rows[0] = '              [ ' + $rows[0].TrimStart()
$rows[-1] = $rows[-1] + ' ]'
$block = @(
    "          // $arch - recorded $(Get-Date -Format 'yyyy-MM-dd') from $Commit, the median of $Runs runs' bests, by gates/record-tick-baselines.ps1"
    "          `"$arch`","
    '          Map.ofList'
) + $rows
$block | Set-Content -Encoding utf8 $OutFile

Write-Host "==== tick baselines: $expected cells; paste this block into ScalingTests.fs (tickBaselines) under $arch"
$block | ForEach-Object { Write-Host $_ }
Write-Host "==== tick baselines: also written to $OutFile"
if (-not $Keep) { Remove-Item -Recurse -Force $export }
$global:LASTEXITCODE = 0
exit 0
