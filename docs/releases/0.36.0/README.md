## 0.36.0 — released 2026-10-02 as `v0.36.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-02: the four packages
at `0.36.0` compiled under Fable 5 over the substrate at `0.33.0` (a scratch project referencing
`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance` from the candidate feed, every transitive `Fuaran.Core.*` package from the
released `0.33.0` on nuget.org; `fable_modules` carried all four at `0.36.0`, their packaged `fable/` sources
under the new paths and the `Fuaran.Compute` namespace; the emitted program ran under node over a `GroupBy`, a
decimal `Quotient` / `Rounded` derive and a sort with a limit, and its prepared-source and incremental answers
— `refresh` and `refreshPrepared` after an edit and an insert — were byte-equal to the full evaluation's, and
to the same program's answers on .NET). The full gate (`verify.ps1`) ran green on the release commit through
the dispatch queue. The proof leg is CI's, on the push of the release commit.

The first version under the packages' own ids (Phase 322, `DECISIONS.md` D4). The slot was opened as
`0.35.0` by Phase 268, which makes a prepared source a persistent VERSION — chunked columns that
successive edits share — and adds the op algebra and the refresh over it; `0.34.0` is tagged, so
those additions advanced the slot rather than ride it. Phase 322's rename is a change of a higher
class than anything the `0.35.0` draft carried as its own, so it ADVANCED the draft to `0.36.0`
rather than reclassing it: **`0.35.0` was never released under its own number, under either set of
ids**, and every entry it carried is folded in below. The four packages ship at this version
together.

**The entries after the first name each package by the id it carried when the change was made.**
Read them through the mapping in the first entry: every one of them ships under the new id at this
version, and `Fuaran.Core.DataFrame.PipelineQuery` (Phase 281) was never published under that id at
all — its first release is `Fuaran.Compute.PipelineQuery` `0.36.0`.
