## 0.37.0 — released 2026-10-02 as `v0.37.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-02: the four packages
at `0.37.0` compiled under Fable 5 over the substrate at `0.34.0` (a scratch project referencing
`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance` from the candidate feed, every transitive `Fuaran.Core.*` package from the
released `0.34.0` on nuget.org; `fable_modules` carried all four at `0.37.0`, their packaged `fable/` sources
under the `Fuaran.Compute` namespace, and every `Fuaran.Core.*` package at `0.34.0`; the emitted program ran
under node over a `GroupBy`, a decimal `Quotient` / `Rounded` derive, a sort with a limit, and an `int` derive
beside a decimal `Quotient` over an EMPTY table and a full one, and its prepared-source and incremental answers
— `refresh` and `refreshPrepared` after an edit and an insert — were byte-equal to the full evaluation's, and
the whole node output byte-equal to the same program's on .NET; over the empty table the derived columns were
typed `int` and `decimal` by their expressions, as on the full one). The full gate (`verify.ps1`) ran green on
the release commit through the dispatch queue. The proof leg is CI's, on the push of the release commit.

This version carries two changes over `0.36.0`, both entered below: a derived column is typed by its
expression, not by its first cell (Phase 338, `DECISIONS.md` D5) — BREAKING; and the substrate pin moves to
`Fuaran.Core.*` `0.34.0`, so a consumer raises `Fuaran.Core.*` with `Fuaran.Compute.*`.

`0.36.0` is tagged, so the breaking change below ADVANCED the slot to `0.37.0`. It is a draft until
it is tagged: an additive change rides it, a breaking one already has.
