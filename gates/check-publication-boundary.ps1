#Requires -Version 7.0
<#
.SYNOPSIS
    Publication-boundary sweep: this repository is public, and no tracked file may name anything
    a reader outside it cannot look up.

.DESCRIPTION
    "Written to a public standard" is a claim, and a claim without a check decays one convenient
    reference at a time — invisibly, because nothing breaks. So the standard is a gate. Every
    tracked file is swept for the vocabulary a reader outside this repository could not look up
    and should not learn from it: the names of private neighbouring projects and tools, the
    operator command surface they are driven by, and the names of private build scripts.

    THE SCOPE IS THE TRACKED SET (`git ls-files`), which is exactly what a publication carries.
    Build output is not swept: it is not published, and it is full of absolute paths.

    PATTERNS ARE WORD-BOUNDARY ANCHORED where the term has common-English collisions. A gate that
    cries wolf gets turned off, which costs more than never having built it.

    WHAT IS DELIBERATELY ALLOWED: phase citations (`Phase 259`, `fuaran-core#259`) — a number is
    not vocabulary, and it is what ties a change to its record; the public Fuaran.Core and
    Fuaran.UI package, repository and specification names; the local folder feed path the
    development loop packs into (nuget.config, pack.ps1); and the copyright holder's registered
    name, which the licence and the package metadata must carry. The last is the one carried
    exception, and it is PRINTED on every run, including a green one, rather than filtered out
    silently: a suppression nobody sees is how a boundary rots.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$global:LASTEXITCODE = 0

# ── The banned vocabulary ────────────────────────────────────────────────────
# Each entry: the regex (case-insensitive, PowerShell's -match default), and what a reader should
# do about a hit. The "why" is in the message, because a gate that only says "forbidden" teaches
# nothing.
$banned = @(
    @{ Pattern = '\bDiametrical\b(?! Ltd\b)'
       Say     = 'names a private strategy project; only the copyright holder''s registered name is carried' }
    @{ Pattern = '\bToolUp\b'
       Say     = 'names a product family outside this repository' }
    @{ Pattern = '\bFuaran[-.](Build|Roadmap|Dispatch|Judge|Witness|App)\b'
       Say     = 'names a component of a wider private system' }
    @{ Pattern = '\broadmapctl\b|\bfuaran-build\b'
       Say     = 'names a private tool; say "the copy registry" or describe the check' }
    @{ Pattern = '\bRefine Roadmap\b|\bAccept suggestions\b|\bSuggest features\b|\bPrompt next\b|\bMake it so\b|\bSync All\b'
       Say     = 'names an operator command surface this repository is not driven by' }
    @{ Pattern = '\bcookbook\b'
       Say     = 'names a private project' }
    @{ Pattern = '\bcampaigns?\b|\bdispatch (take|fold|gate)\b'
       Say     = 'names the private work-scheduling vocabulary this repository is not driven by' }
    @{ Pattern = '\bpack-all\.ps1\b|\binit\.ps1\b|\bpull-all\.ps1\b|\bpush-all\.ps1\b|\bcheck-roadmaps\.ps1\b'
       Say     = 'names a private build script; describe what it does instead' }
    @{ Pattern = '\beval-suite\b|\borchestrator-demo\b|Fuaran\.UI\.Orchestration'
       Say     = 'names a private neighbouring project or package' }
    @{ Pattern = '\bsibling repo\b|\bprivate sibling\b|\bestate\b'
       Say     = 'implies a wider private ecosystem; name the public artefact, or say "a consumer"' }
)

# ── The carried exception ────────────────────────────────────────────────────
# Not a suppression: the pattern above already excludes it, and it is printed on every run.
$carried = @(
    @{ What = 'the copyright holder''s registered name (Diametrical Ltd)'
       Where = 'LICENSE, NOTICE, Directory.Build.props (Company, Copyright)'
       Why  = 'the Apache-2.0 licence and the package metadata name the copyright holder; that is a legal fact, not a reference' }
    @{ What = 'the word "estate" in two sentences of the proof-leg kit''s README'
       Where = 'proofs/kit/README.md (the two lines that say "the estate''s")'
       Why  = 'the file is a VERBATIM copy declared in copies.json; editing the copy would put it in permanent drift from its source, so the wording is fixed at the source (the Fuaran.Core repository) and re-copied' }
)

# A carried hit is exempt only when BOTH its file and its text match: a new hit in the same file,
# or the same word somewhere else, is still a failure. A file-level allowlist would let the next
# reference through on the strength of a decision that was about something else.
function Test-Carried($hit) {
    ($hit.File -eq 'proofs/kit/README.md' -and $hit.Text -match "\bthe estate's\b")
}

$files = @(git ls-files) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) }
if (-not $files) {
    # A fresh clone before the first commit tracks nothing. Sweeping zero files and reporting
    # success would be a vacuous green, so say which it is.
    Write-Host 'publication boundary: no tracked files yet — nothing swept (not a pass).'
    exit 0
}

$hits = @()
foreach ($file in $files) {
    # Skip this gate itself: it necessarily spells out every banned term.
    if ($file -eq 'gates/check-publication-boundary.ps1') { continue }
    # Skip binary files by extension; the tracked set holds none today, and a binary match would
    # be noise rather than vocabulary.
    if ($file -match '\.(png|jpg|jpeg|gif|ico|dll|exe|pdb|nupkg|zip)$') { continue }

    $lines = Get-Content -LiteralPath $file -ErrorAction SilentlyContinue
    if (-not $lines) { continue }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($rule in $banned) {
            if ($lines[$i] -match $rule.Pattern) {
                $hits += [pscustomobject]@{
                    File = $file
                    Line = $i + 1
                    Text = $lines[$i].Trim()
                    Say  = $rule.Say
                }
            }
        }
    }
}

Write-Host "publication boundary: swept $($files.Count) tracked file(s)."
Write-Host 'carried exception (knowingly held):'
foreach ($c in $carried) {
    Write-Host ("  {0}" -f $c.What)
    Write-Host ("    in:  {0}" -f $c.Where)
    Write-Host ("    why: {0}" -f $c.Why)
}

$exempt = @($hits | Where-Object { Test-Carried $_ })
$hits = @($hits | Where-Object { -not (Test-Carried $_) })
foreach ($h in $exempt) {
    Write-Host "  carried: $($h.File):$($h.Line)"
}

if ($hits.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($hits.Count) publication-boundary violation(s)."
    foreach ($h in $hits) {
        Write-Host "  $($h.File):$($h.Line)  — $($h.Say)"
        Write-Host "      $($h.Text)"
    }
    exit 1
}

Write-Host 'OK: no tracked file names anything outside this repository''s public boundary.'
exit 0
