## 0.33.0 — released 2026-09-26 as `v0.33.0`

**This is the first slot this repository emits (Phase 259), and it is released.** It opens one minor above
the last version the four ids shipped from the substrate repository (`0.32.0`), so every consumer's floor
stays monotone across the change of producer.

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-09-26: the three F#
packages at `0.33.0` compiled under Fable 5 over the substrate at `0.32.0` (a scratch project referencing
`Fuaran.Core.DataFrame`, `Fuaran.Core.Column.Ops` and `Fuaran.Core.DataFrame.Conformance` from the candidate
feed, every transitive `Fuaran.Core.*` package from the released `0.32.0`; `fable_modules` carried all three at
`0.33.0`; the emitted program ran under node). The C# facade package is excused from Fable as its parent is.
The full gate (`verify.ps1`) ran green on the release commit, and the proof leg verified `ColumnOps` and
`Pipeline` once at Phase 259.

- **The producer changed; the contract did not (no class).** `Fuaran.Core.DataFrame`,
  `Fuaran.Core.Column.Ops`, `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
  are built here from the sources they shipped with at `0.32.0`, under the same package ids and the
  same namespaces, and their public surfaces are the `0.32.0` surfaces unchanged — the committed
  baselines under `api/` were carried with their history and the gate holds each package to its
  own. Adopting `0.33.0` from `0.32.0` is a version raise and nothing else.
- **The substrate is taken by package (no class for a consumer).** The four packages now depend on
  `Fuaran.Core.Column`, `Wire`, `OpStream`, `Conformance` and `CSharp` at `0.32.0` as package
  dependencies, where at `0.32.0` those were built in the same tree at the same number. A consumer
  that pins the substrate packages itself keeps them at `0.32.0` or later.
- **`conformance/laws/transform-laws.json` is emitted here (no class).** The reference answers the
  other hosts certify against are rendered from this repository's evaluator, and the file's
  `kitVersion` stamp reads this repository's `<Version>`. The vectors themselves are byte-identical
  to `0.32.0`'s.
