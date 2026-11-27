### The C# dataframe facade is removed, and the substrate pin rises to `0.33.0` (Fuaran.Core's Phase 231 and DECISIONS.md D28) — BREAKING, `removal`

**What changed.** `Fuaran.Core.DataFrame.CSharp` is no longer produced by this repository. It was cut
in the Fuaran.Core repository at `0.32.0` (its Phase 257) as the dataframe half of the C#-shaped
facade — `Expr`, `Step`, `Pipeline`, the slot types and the dataframe vocabularies in the
`Fuaran.Core.CSharp` namespace — built over that repository's `Fuaran.Core.CSharp`, and shipped here
at `0.33.0` and `0.34.0`. Fuaran.Core removed `Fuaran.Core.CSharp` at its `0.33.0` (its Phase 231,
re-measuring D28's premise: the consumer the facade was shipped for never adopted it), so a pin at
`0.33.0` cannot restore this package's dependency, and that repository's record names this removal as
the same change-set that raises the pin. So both halves go, and D28's other criterion — a C# veneer
generated from the IDL by a source generator — is the route by which one returns. With the package
went its proof project (`tests/Fuaran.Core.DataFrame.CSharp.Proof`, and the gate stage that ran it),
its baseline (`api/Fuaran.Core.DataFrame.CSharp.txt`), its entries in `fable-exclusions.json` (now
empty), `proofs/coverage-exclusions.json` and `copies.json` (the five proof legs copied from
Fuaran.Core's facade proof), its row in the README roster, and `Fuaran.Core.CSharp` from the
substrate the `Compute boundary` tests allow (four packages, where there were five).

| Package id | Last emitted here | Continues from |
|---|---|---|
| `Fuaran.Core.DataFrame.CSharp` | `0.34.0` | nowhere — removed, not moved |

**Class: `removal` — breaking, and it RIDES this slot.** A package id a consumer can pin stops being
produced. `0.35.0` is an untagged, publicly unpinned draft, and before `1.0` the minor position this
slot already advanced is the one a breaking change takes, so the number does not move. The surface
gate reads the baselines of the packages the tree still ships, so a package that leaves takes its
baseline with it and no class is printed for it: the class of this entry is its statement rather than
a gate output.

**The substrate pin (no class for a consumer of the three packages).** `FuaranCoreVersion` rises from
`0.32.0` to `0.33.0`, so the three packages now depend on `Fuaran.Core.Column`, `Wire`, `OpStream`
and `Conformance` at `0.33.0`. Their own public surfaces do not move for it: the adaptations are
internal (the codecs' `NotJson` message now comes from `Json.parseDetailed`, and each law family
built by `DataFrameFamilies` fills the adequacy class and refusal verdict that Core `0.33.0` carries
on `LawFamily`, read from the census and audit rows this package already declared).

**What adopting it costs.**

- **A consumer that pins `Fuaran.Core.DataFrame.CSharp`** keeps restoring what it pinned — `0.32.0`
  to `0.34.0` stay on nuget.org — and cannot raise it past `0.34.0`. To move its other pins to
  `0.35.0` it drops the reference and constructs `ColExpr` and `Transform` values through the F#
  surface, wrapping only what it authors.
- **A consumer that never referenced the package** changes nothing beyond taking the substrate at
  `0.33.0` or later.
