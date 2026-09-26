#Requires -Version 7.0
# THE EXTRACTION POST-PASS'S GO-RED PROOF — Phase 169.
#
#     pwsh ./proofs/kit/extraction-post-pass.tests.ps1
#
# It is deliberately NOT part of the default leg: `check.ps1 -Runs 1` prints exactly what it
# printed before this phase, and a proof about the pass is not a proof about the models. Run it
# when the pass changes, when the prover pin moves, or when you want the retirement condition
# answered.
#
# FOUR ARMS, and the point of each is a direction it can FAIL in:
#
#   A. UNIT      — the pass repairs the shape the backend emits, and leaves everything else alone
#                  (a column-0 `and`, an indented `and_then`, an `and` inside a string literal,
#                  CRLF line endings, trailing whitespace). Needs nothing.
#   B. IDENTITY  — the pass is the identity on EVERY committed oracle. This is the acceptance
#                  criterion "every committed oracle is byte-identical before and after the pass",
#                  recorded as a check rather than as a claim. Needs nothing.
#   C. GO RED    — the RAW extraction of `templates/MutualTypes.fst` does not compile under the
#                  oracle project's own flags, and fails with FS0010 at the `and` line. An arm
#                  that cannot go red proves nothing, so this one is checked for the RIGHT
#                  failure and not merely for failure.
#   D. GO GREEN  — the same extraction, with the pass run over it, compiles.
#
# C and D need the pinned prover and `dotnet`. Where either is absent they are reported NOT RUN,
# naming the remedy — never skipped quietly, because "nothing to check" must not read as
# "everything checked".
#
# THE RETIREMENT CONDITION IS ARM C's OTHER OUTCOME. If a fresh extraction of the fixture no
# longer carries the defect, the upstream F* defect has been fixed under the current pin and the
# post-pass has outlived its reason. That is reported BY NAME as a finding, not as a pass: a
# go-red fixture that can no longer go red is the signal to delete the machinery it guards.
[CmdletBinding()]
param(
    # The directory holding the .fst models. Everything else defaults relative to it.
    [string] $ProofsDir,
    # The committed extractions. Its hand-written FLOOR files (those with no matching .fst beside
    # the models) are what the scratch project compiles the fixture against.
    [string] $OracleDir,
    # The oracle project. The scratch project is DERIVED from it — same target framework, same
    # NoWarn, same OtherFlags, same package references — so the two cannot drift.
    [string] $OracleProject,
    # The mutual-type model arms C and D extract.
    [string] $Fixture,
    # The pinned prover declaration, so a prover that is not the pinned one is reported rather
    # than silently believed.
    [string] $PinFile,
    # Scratch. Expected to be gitignored by the adopting repository (it is under obj/).
    [string] $WorkDir,
    # Run A and B only.
    [switch] $SkipEndToEnd
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'extraction-post-pass.ps1')

if (-not $ProofsDir) { $ProofsDir = Split-Path $PSScriptRoot -Parent }
$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not $OracleDir) { $OracleDir = Join-Path $ProofsDir 'oracle' }
if (-not $Fixture) { $Fixture = Join-Path $PSScriptRoot 'templates/MutualTypes.fst' }
if (-not $PinFile) { $PinFile = Join-Path $ProofsDir 'fstar-pin.json' }
if (-not $WorkDir) { $WorkDir = Join-Path $ProofsDir 'obj/post-pass-selftest' }
if (-not $OracleProject) {
    $found = @(Get-ChildItem -Path $OracleDir -Filter '*.fsproj' -File -ErrorAction SilentlyContinue)
    if ($found.Count -eq 1) { $OracleProject = $found[0].FullName }
}

$script:failures = [System.Collections.Generic.List[string]]::new()
$script:notRun = [System.Collections.Generic.List[string]]::new()

function Pass([string] $what) { Write-Host "  PASS    $what" -ForegroundColor Green }
function Fail([string] $what) { $script:failures.Add($what); Write-Host "  FAIL    $what" -ForegroundColor Red }
function Skip([string] $what) { $script:notRun.Add($what); Write-Host "  NOT RUN $what" -ForegroundColor Yellow }
function Check([bool] $ok, [string] $what) { if ($ok) { Pass $what } else { Fail $what } }

# ---- A. the pass repairs what the backend emits, and nothing else -------------------------------

Write-Host '==== post-pass: A. unit' -ForegroundColor Cyan

# The bytes F* v2026.09.06 emits for a two-case mutual DU group, trailing space and all.
$emitted = "type a =`n| A0`n| A1 of b `n and b =`n| B0`n| B1 of a`n"
$repaired = Repair-ExtractionMutualTypeGroup $emitted
Check ($repaired -ceq "type a =`n| A0`n| A1 of b `nand b =`n| B0`n| B1 of a`n") `
    'the emitted mutual type group is re-indented to column 0, trailing space untouched'
Check ((Test-ExtractionMutualTypeDefect $emitted).Count -eq 1) 'the defect is detected before the pass'
Check ((Test-ExtractionMutualTypeDefect $repaired).Count -eq 0) 'no defect remains after the pass'

# The controls. Each is a line the pass MUST leave alone, and each is a way a wider pass would be
# wrong: a mutual let-rec is already at column 0; `and_then` merely starts with the three letters;
# an `and` inside a string literal is data. (The backend cannot in fact emit a line that begins
# inside a literal — it escapes them onto one line — but a pass that relied on that would be one
# measurement away from being wrong, so the control is cheap insurance rather than a claim.)
$controls = @(
    "let rec f = 1`nand g = 2`n"
    "let h = (`n  and_then x y)`n"
    "let s : Prims.string = `" and b`"`n"
    "let t = 1`n    andrew`n"
)
foreach ($c in $controls) {
    Check ((Repair-ExtractionMutualTypeGroup $c) -ceq $c) ("untouched: " + ($c -replace "`n", '\n'))
}

# Line endings are content here: the committed oracles are LF and the leg compares LF-normalised,
# but this function is also reachable from a CRLF checkout and must not silently rewrite either.
$crlf = "type a =`r`n| A1 of b `r`n and b =`r`n| B0`r`n"
Check ((Repair-ExtractionMutualTypeGroup $crlf) -ceq "type a =`r`n| A1 of b `r`nand b =`r`n| B0`r`n") `
    'CRLF line endings survive the pass unchanged'
Check ((Repair-ExtractionMutualTypeGroup '') -ceq '') 'the empty extraction is unchanged'

# ---- B. the pass is the identity on every committed oracle --------------------------------------

Write-Host '==== post-pass: B. identity over the committed oracles' -ForegroundColor Cyan

$oracles = @(Get-ChildItem -Path $OracleDir -Filter '*.fs' -File -ErrorAction SilentlyContinue)
if ($oracles.Count -eq 0) {
    Skip "no committed oracle under $OracleDir to hold the pass to"
}
else {
    $moved = @()
    foreach ($o in $oracles) {
        $text = [System.IO.File]::ReadAllText($o.FullName)
        if ((Repair-ExtractionMutualTypeGroup $text) -cne $text) { $moved += $o.Name }
    }
    Check ($moved.Count -eq 0) ("the pass is the identity on all $($oracles.Count) committed oracle file(s)" +
        $(if ($moved.Count) { " — IT MOVED: $($moved -join ', ')" } else { '' }))
}

if ($SkipEndToEnd) {
    Skip 'C and D (extract + compile) — -SkipEndToEnd was passed'
}
else {

    # ---- resolve the prover and dotnet ----------------------------------------------------------

    $pinnedVersion = (Get-Content $PinFile -Raw | ConvertFrom-Json).fstar.TrimStart('v')
    $fstar = $null
    if ($env:FSTAR_HOME) { $fstar = Join-Path $env:FSTAR_HOME 'bin/fstar.exe' }
    elseif (Test-Path (Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe')) {
        $fstar = Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe'
    }

    $proverReady = $false
    if (-not $fstar -or -not (Test-Path $fstar)) {
        Skip ("C and D — no pinned prover. Set FSTAR_HOME to an F* $pinnedVersion release, or run " +
            "`pwsh ./proofs/check.ps1` once to install it under proofs/.fstar/.")
    }
    else {
        $reported = (& $fstar --version 2>&1) -join ' '
        if ($reported -notmatch [regex]::Escape($pinnedVersion)) {
            Skip ("C and D — the prover at $fstar is not the pinned F* $pinnedVersion; the layout this " +
                'pass repairs is a property of a particular prover, so another one proves nothing here.')
        }
        else { $proverReady = $true }
    }

    $dotnetReady = [bool](Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)
    if ($proverReady -and -not $dotnetReady) { Skip 'C and D — `dotnet` is not on PATH, so nothing can be compiled' }
    if ($proverReady -and $dotnetReady -and -not $OracleProject) {
        Skip "C and D — no oracle project resolved under $OracleDir; pass -OracleProject"
        $dotnetReady = $false
    }

    if ($proverReady -and $dotnetReady) {

        # ---- extract the fixture with the pinned prover ------------------------------------------

        Write-Host '==== post-pass: C. the raw extraction does not compile' -ForegroundColor Cyan

        if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
        $src = Join-Path $WorkDir 'src'
        $cache = Join-Path $WorkDir 'cache'
        $out = Join-Path $WorkDir 'out'
        $proj = Join-Path $WorkDir 'proj'
        foreach ($d in @($src, $cache, $out, $proj)) { New-Item -ItemType Directory -Force $d | Out-Null }

        $fixtureName = [System.IO.Path]::GetFileNameWithoutExtension($Fixture)
        Copy-Item $Fixture (Join-Path $src "$fixtureName.fst")

        Push-Location $src
        try {
            & $fstar --cache_checked_modules --cache_dir $cache "$fixtureName.fst" 2>&1 | Out-Null
            & $fstar --cache_checked_modules --cache_dir $cache --codegen FSharp `
                --extract $fixtureName --odir $out "$fixtureName.fst" 2>&1 | Out-Null
        }
        finally { Pop-Location }

        $rawPath = Join-Path $out "$fixtureName.fs"
        if (-not (Test-Path $rawPath)) {
            Fail "the fixture did not extract — no $fixtureName.fs under $out (check the prover and the model)"
        }
        else {
            $raw = [System.IO.File]::ReadAllText($rawPath)
            $defectLines = Test-ExtractionMutualTypeDefect $raw

            if ($defectLines.Count -eq 0) {
                # NOT a pass. See the header: this is the retirement condition, reported by name.
                Fail ("RETIREMENT CONDITION REACHED — a fresh extraction of $fixtureName under the pinned " +
                    "F* $pinnedVersion no longer carries the mutual-type-group defect. The upstream defect " +
                    'is fixed: delete the post-pass (kit/extraction-post-pass.ps1), its call in ' +
                    "check-proof-leg.ps1's EXTRACT stage, this fixture and this script, and re-extract every " +
                    'oracle. Nothing below this line can be measured, so C and D did not run.')
            }
            else {
                Pass "the raw extraction carries the defect at line(s) $($defectLines -join ', ')"

                # ---- the scratch project, DERIVED from the oracle project ------------------------
                #
                # Copied rather than re-declared so that the target framework, the NoWarn set, the
                # --strict-indentation- relaxation and the package references are the oracle
                # project's own and cannot drift from it. Only the Compile list is rewritten: the
                # hand-written FLOOR files (an oracle .fs with no model beside it — Prims.fs and
                # the option shim) in the order the oracle project declares them, then the fixture.
                $xml = [xml](Get-Content $OracleProject -Raw)
                $compiles = @($xml.SelectNodes('//Compile'))
                $floor = @()
                foreach ($c in $compiles) {
                    $inc = $c.GetAttribute('Include')
                    if (-not (Test-Path (Join-Path $ProofsDir ([System.IO.Path]::GetFileNameWithoutExtension($inc) + '.fst')))) {
                        $floor += $inc
                    }
                }
                $group = if ($compiles.Count) { $compiles[0].ParentNode } else { $xml.Project.AppendChild($xml.CreateElement('ItemGroup')) }
                foreach ($c in $compiles) { [void]$c.ParentNode.RemoveChild($c) }
                foreach ($f in ($floor + @("$fixtureName.fs"))) {
                    $e = $xml.CreateElement('Compile')
                    $e.SetAttribute('Include', $f)
                    [void]$group.AppendChild($e)
                }
                $projFile = Join-Path $proj 'PostPassSelfTest.fsproj'
                $xml.Save($projFile)
                foreach ($f in $floor) { Copy-Item (Join-Path $OracleDir $f) (Join-Path $proj $f) }

                function Invoke-ScratchBuild([string] $text) {
                    [System.IO.File]::WriteAllText((Join-Path $proj "$fixtureName.fs"), $text, [System.Text.UTF8Encoding]::new($false))
                    $log = & dotnet build $projFile --nologo -v q 2>&1 | Out-String
                    return @{ Exit = $LASTEXITCODE; Log = $log }
                }

                $redRun = Invoke-ScratchBuild $raw
                if ($redRun.Exit -eq 0) {
                    Fail ("the RAW extraction compiled — the go-red arm cannot go red, so arm D proves nothing. " +
                        'Either the oracle project has gained a flag that tolerates the layout, or the pinned ' +
                        'prover no longer emits it (see the retirement condition in the header).')
                }
                elseif ($redRun.Log -notmatch 'FS0010' -or $redRun.Log -notmatch [regex]::Escape("$fixtureName.fs")) {
                    Fail ("the RAW extraction failed to build, but NOT with FS0010 in $fixtureName.fs — a failure " +
                        "for the wrong reason is not evidence. Build output:`n$($redRun.Log)")
                }
                else {
                    Pass "the raw extraction fails to compile with FS0010 in $fixtureName.fs"
                }

                Write-Host '==== post-pass: D. the extraction the pass has run over compiles' -ForegroundColor Cyan
                $greenRun = Invoke-ScratchBuild (Repair-ExtractionMutualTypeGroup $raw)
                if ($greenRun.Exit -eq 0) { Pass 'the normalised extraction compiles under the oracle project''s flags' }
                else { Fail ("the normalised extraction did NOT compile. Build output:`n$($greenRun.Log)") }
            }
        }
    }
}

# ---- verdict ------------------------------------------------------------------------------------

Write-Host ''
if ($script:notRun.Count -gt 0) {
    Write-Host "==== post-pass: $($script:notRun.Count) arm(s) NOT RUN:" -ForegroundColor Yellow
    foreach ($s in $script:notRun) { Write-Host "     $s" -ForegroundColor Yellow }
}
if ($script:failures.Count -gt 0) {
    Write-Host "==== post-pass: RED — $($script:failures.Count) failure(s)" -ForegroundColor Red
    exit 1
}
Write-Host '==== post-pass: green' -ForegroundColor Green
exit 0
