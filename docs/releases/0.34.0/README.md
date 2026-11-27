## 0.34.0 — released 2026-09-27 as `v0.34.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-09-27: the three F#
packages at `0.34.0` compiled under Fable 5 over the substrate at `0.32.0` (a scratch project referencing
`Fuaran.Core.DataFrame`, `Fuaran.Core.Column.Ops` and `Fuaran.Core.DataFrame.Conformance` from the candidate
feed, every transitive `Fuaran.Core.*` package from the released `0.32.0`; `fable_modules` carried all three at
`0.34.0`; the emitted program ran under node and its prepared-source and incremental answers were byte-equal to
the full evaluation's). The C# facade package is excused from Fable as its parent is. The full gate
(`verify.ps1`) ran green on the release commit through the dispatch queue, and the proof leg verified
`ColumnOps` and `Pipeline` on the Phase 267 tree. The law corpus copy was re-emitted at this version with every
vector unchanged.


Opened by Phase 267, which makes the evaluator's working frame column-major and typed, and adds the
one public surface that earns its place over it. The four packages ship at this version together;
`0.33.0` is tagged, so the additions advance the slot rather than ride it.

- **Additive — `Fuaran.Core.DataFrame`: `Prepared`, `DataFrame.prepare`, `DataFrame.evalPrepared`.**
  A source prepared once for many evaluations: `prepare : Table -> Prepared` pays the `Table`
  boundary — one typed unpack per column — once, and `evalPrepared resolve env pipeline prepared`
  evaluates over it with the same parameter shapes, the same cells and the same errors as
  `evalPipelineWithInEnv`, which now delegates to it. `Prepared` is opaque (no public member), so
  the working form behind it stays free to move, as the incremental state did behind its type in
  `0.27.0`.
- **Additive — `Fuaran.Core.DataFrame`: `Incremental.primePrepared`, `Incremental.primeOnPrepared`.**
  `prime` / `primeOn` over a `Prepared` — named rather than overloaded, since a let-bound function
  cannot overload. The state built is the state the table form builds (result, footprint, plan,
  source — the prepared table itself), with the reference path evaluating over the prepared form
  and that form held in the state. `refresh` / `refreshOn` take a `Table` as before.
- **Additive — `Fuaran.Core.DataFrame`: `DataFrame.typeOf : Schema -> ColExpr -> ColumnType option`.**
  Phase 266's static typer, internal until this cut: the type of an expression's present values
  where the schema alone decides it, or `None`.
- **No surface change — the dense columnar frame (Phase 267).** The evaluator's frame is one vector
  per column — `int[]`, `float[]`, `bool[]` or `string[]` beside a validity mask where the column's
  cells agree with its declared type, boxed cells where they do not — a selection vector `Filter`
  produces, `Limit` slices and `Sort` permutes, projection as metadata, a `Derive` that adds one
  vector and shares the rest, and gathering verbs that read rows through the selection and emit
  fresh vectors. The compiled expression tree reads typed vectors unboxed where the node's type is
  decided. The transform law vectors are byte-identical: `conformance/laws/transform-laws.json` is
  re-emitted with its `kitVersion` stamp at `0.34.0` and no other byte moved.
- **No surface change — Phases 263 to 266.** Each landed on the `0.33.0` sources after the tag
  without moving a public member. Phase 263: array rows and column indices resolved once per step
  in place of a name lookup per reference per row. Phase 264: a hash join, a one-pass pivot, linear
  rolling windows and a scatter order-restore. Phase 265: grouping, distinct, the set operations,
  pivot's index groups and window partitions on one cell comparer equal to `cellToken`, and the
  seam's maintained grouping re-keyed on it. Phase 266: each step's expression compiled once into a
  closure tree specialised by a static typer, errors through one slot per row.
