#Requires -Version 7.0
# THE EXTRACTION POST-PASS — Phase 169. Dot-sourced by `check-proof-leg.ps1`'s EXTRACT stage and
# by `extraction-post-pass.tests.ps1` beside it. It defines two functions and runs nothing.
#
# WHAT IT REPAIRS. F*'s F# backend emits a mutual TYPE group by breaking the group at the space
# before each `and`, so the previous declaration's last line gains a trailing space and the `and`
# line begins with ONE leading space:
#
#     type a =
#     | A0
#     | A1 of b <-- trailing space
#      and b =   <-- one leading space
#     | B0
#     | B1 of a
#
# F# 10's parser rejects that outright — `error FS0010: Unexpected keyword 'and' in member
# definition` — and it rejects it EVEN UNDER the oracle project's `--strict-indentation-`, which
# is what makes this a separate defect from the pre-F#-8 match-arm layout that flag was relaxed
# for. Re-indenting the `and` to column 0 and changing nothing else compiles.
#
# WHAT IT DELIBERATELY IS NOT. A general F# formatter over extracted code, and not Fantomas. The
# oracle is generated text held to a byte diff against a fresh extraction, so any wider rewrite
# would make that diff measure the formatter rather than the extraction. This pass moves the
# leading whitespace of a line whose first token is the keyword `and`, and touches nothing else —
# not the trailing space on the line above it, which is legal F# and is present in the committed
# oracles for other reasons.
#
# WHY MATCHING ANY INDENTED `and` IS SAFE, measured on the pinned prover rather than assumed:
#   - the backend HOISTS local mutual recursion to the top level, so a value `and` is emitted at
#     column 0 and is never a candidate (an F* `let rec f … and g …` written inside a function
#     body extracts as two top-level bindings joined by a column-0 `and`);
#   - it emits string literals escaped on a single line (`"tab\tand\nnewline"`), so no line of
#     output begins inside a literal — a model carrying a raw multi-line literal does not extract
#     at all, the extractor fails first;
#   - an identifier such as `and_then` is not matched, because the match requires whitespace or
#     end of line after the keyword.
# Every indented `and` the backend emits is therefore a mutual type group's. The match is
# nevertheless written wider than the one space observed, because a wider indent could only ever
# be the same defect, while a narrower match could miss it.
#
# THE PIN, AND THE RETIREMENT CONDITION. Observed on F* v2026.09.06 / Z3 4.13.3 — the version
# `proofs/fstar-pin.json` declares, and the only version this leg will run. RETIRE THE PASS when
# a pin bump makes `Test-ExtractionMutualTypeDefect` report nothing over a fresh extraction of
# `templates/MutualTypes.fst`: that is the whole condition, it is checked by
# `extraction-post-pass.tests.ps1`, and that script SAYS SO by name rather than going quietly
# green — a go-red fixture that can no longer go red is the signal to delete the machinery it
# guards, not a passing test.

# THIS FILE IS DOT-SOURCED, so it runs in its caller's scope and must define nothing but its two
# functions: no `Set-StrictMode`, no script variables, no `$ErrorActionPreference`. A dot-sourced
# `Set-StrictMode -Version Latest` would apply to the whole of `check-proof-leg.ps1` from the line
# that sourced it onwards, which is a change to the leg's behaviour smuggled in by a helper.

function Test-ExtractionMutualTypeDefect {
    <#
    .SYNOPSIS
    The 1-based line numbers carrying the defect, or an empty array. An empty result over a fresh
    extraction of a KNOWN mutual-type model is the retirement condition above.
    #>
    [CmdletBinding()]
    [OutputType([int[]])]
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Text)

    $found = [System.Collections.Generic.List[int]]::new()
    $n = 0
    foreach ($line in ($Text -split "`r?`n")) {
        $n++
        if ($line -match '^[ \t]+and([ \t]|$)') { $found.Add($n) }
    }
    return , $found.ToArray()
}

function Repair-ExtractionMutualTypeGroup {
    <#
    .SYNOPSIS
    The pass. Returns $Text with every mutual type group's `and` re-indented to column 0, and
    every other byte — line endings and trailing whitespace included — exactly as it was.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Text)

    # The line shape the backend emits for a mutual type group's continuation: leading whitespace,
    # then the keyword `and`, then whitespace or the end of the line. `(?m)` so `^` and `$` are
    # line anchors; `\r` is in the trailing class so a CRLF checkout matches exactly as an LF one
    # does, and the `\r` itself is outside the capture so the line ending is preserved.
    return [regex]::Replace($Text, '(?m)^[ \t]+(and(?=[ \t\r]|$))', '$1')
}
