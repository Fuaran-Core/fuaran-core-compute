### The packages take their own ids: `Fuaran.Compute.*` package ids and the `Fuaran.Compute` namespace (Phase 322, `DECISIONS.md` D4) — BREAKING, `removal`

**What changed.** Every package this repository produces is renamed, and every public type and module
moves from the `Fuaran.Core` namespace to `Fuaran.Compute`. The module names inside — `DataFrame`,
`ColumnOps`, `Transform`, `Incremental`, `Delta`, `Plan`, `RowIdentity`, `DataFrameConformance`,
`PipelineQuery` and the rest — and every member, type shape and wire byte are unchanged.

| Old id (last published) | New id (first published) |
|---|---|
| `Fuaran.Core.DataFrame` (`0.34.0`) | `Fuaran.Compute.DataFrame` (`0.36.0`) |
| `Fuaran.Core.Column.Ops` (`0.34.0`) | `Fuaran.Compute.ColumnOps` (`0.36.0`) |
| `Fuaran.Core.DataFrame.Conformance` (`0.34.0`) | `Fuaran.Compute.Conformance` (`0.36.0`) |
| `Fuaran.Core.DataFrame.PipelineQuery` (never published) | `Fuaran.Compute.PipelineQuery` (`0.36.0`) |

**The old ids stop at `0.34.0`.** Nothing in this repository packs them any more, they are not
republished, and no deprecation package is published under them: nuget.org keeps `0.34.0` (and every
earlier version) restorable for a consumer that has not moved. A consumer that raises its pin past
`0.34.0` must move to the new ids — there is no `0.36.0` under the old ones.

**Why it is breaking, measured rather than asserted.** The `Public surface` family reads each new
id's baseline against `v0.34.0`'s baseline under the id it replaced (the predecessor map in
`PublicSurfaceTests.fs`) and classes all three published packages `removal`: every externally
visible type left `Fuaran.Core` for `Fuaran.Compute`. The wire record is UNCHANGED by the rename —
the canonical bytes name no CLR type, and each new `api/wire/` baseline's body is byte-identical to
its predecessor's — so the `breaking` its header states since `v0.34.0` is the decimal's (Phases 277
and 321, below), carried across the rename rather than lost to a first snapshot.

**What a consumer changes.**

- Each `PackageReference` / `PackageVersion` naming an old id names its new id, at `0.36.0`.
- Each source file that reaches a compute type adds `open Fuaran.Compute` beside its
  `open Fuaran.Core`; the substrate's types (`Table`, `Cell`, `ColumnType`, `AggFn`, the law kit's
  `LawResult`) stay in `Fuaran.Core`. A fully qualified `Fuaran.Core.DataFrame.evalPipeline` becomes
  `Fuaran.Compute.DataFrame.evalPipeline`.
- The pre-split `Conformance.<family>` spellings still resolve for a file that opens both
  namespaces (the forwarding module is now `Fuaran.Compute.Conformance`); a fully qualified
  `Fuaran.Core.Conformance.<family>` spelling of a dataframe family does not.
- *(Added 2026-10-05 from the consumer sweep, fuaran-core#361.)* The substrate pin moves with the
  version taken: `0.36.0` carries a `Fuaran.Core.*` floor of `0.33.0`, and `0.37.0` a floor of `0.34.0`.
  A direct `Fuaran.Core.*` pin below the floor is a package downgrade (NU1605), and where a build does
  not treat that warning as an error it runs the compute assemblies over a substrate older than the one
  they were compiled against. Raising the substrate pin brings the substrate's own source breaks
  between the two versions with it (its `STABILITY.md`), so the move is the substrate raise and the
  rename together.
- *(Added 2026-10-05, fuaran-core#361.)* An old id can stay in a consumer's graph after its own pins
  have moved, carried transitively by another package built on it. Both assemblies then load, and
  their types share simple names (`Transform`, `DataFrame`, `DataFrameCodec`): in a file that opens
  both namespaces an unqualified name resolves to the namespace opened LAST, so `open Fuaran.Compute`
  goes after `open Fuaran.Core`. The graph is clean only when the package carrying the old id is
  itself raised past it; `dotnet list package --include-transitive` names it.

**The projects, not only the ids, moved:** `src/Fuaran.Compute.DataFrame/`, `src/Fuaran.Compute.ColumnOps/`,
`src/Fuaran.Compute.PipelineQuery/` and `src/Fuaran.Compute.Conformance/`. The solution, the test,
benchmark and proof-oracle projects keep the repository's name (`Fuaran.Core.Compute.*`): the
repository is not renamed, and none of those projects ships.

- **Additive — a new package, `Fuaran.Core.DataFrame.PipelineQuery`: the registered pipeline query
  (Phase 281).** `PipelineQuery` (`Query`, `Pipeline`, `Sources`) pairs the substrate's `Query`
  declaration with the `Transform` pipeline that is its body, and declares the schema of every
  named source the pair reads. `PipelineQueryRegistry` (`empty`, `register`, `tryFind`,
  `enumerate`, `declarations`, `dispatch`) is opaque, so a pair whose pipeline disagrees with its
  declaration cannot be in one: `register` refuses a duplicate id with the substrate's own
  `DuplicateQuery`, then holds the pair to `PipelineQuery.check` — every named source declared
  (`SourceUndeclared`), the `SchemaWalk` output closed (`ResultSchemaOpen`) and equal to the
  declared `ResultSchema` in names, order and types (`ResultColumn` with a `ResultDisagreement`:
  `NotProduced`, `Undeclared`, `Duplicated`, `OutOfOrder`, `TypeDiffers`, `TypeUndecidable`), and
  the parameter reads (`PipelineQuery.paramReads`, each a `ParamRead.Scalar` or `.List` at the type
  its position decides) agreeing with the declared parameters both ways (`ParamUndeclared`,
  `ParamReadAs`, `ParamReadAsScalarAndList`, `ParamUnread`). `dispatch` IS the substrate's
  `QueryRegistry.dispatch` over the declarations — `NoSuchQuery`, `Query.validateParams`, the
  `Deferred` envelope's three outcomes and the unreachable `Ok(Failed _)` — and hands the resolver
  the pair with the arguments substituted (`PipelineQuery.substitute`). `PipelineQueryCodec`
  (`encode` / `decode`, `encodeJson` / `decodeJson`) is its canonical wire form, in `api/wire/`.
  **One limit worth knowing before adopting it:** a `Derive` of anything but a string has a type
  only the data decides (the evaluator types a derived column from its first present cell and falls
  back to `StringType` over an empty frame), so a pair whose declared result carries such a column
  is refused as `TypeUndecidable`; declare it through `Project` / `GroupBy` over a typed column.
  The package takes `Fuaran.Core.Query` by package (and `Fuaran.Core.Function` through it); the
  compute boundary test admits that widening for this package and the families' package only.
- **Additive — `Fuaran.Core.DataFrame.Conformance`: `PipelineQueryConformance.laws` (Phase 281).**
  On the substrate's `queryLaws` pattern: every agreement and parameter refusal built each
  iteration, the substrate's registry refusals, the three dispatch outcomes, the bound pipeline
  evaluated against the same pipeline written with the arguments as literals, and the codec
  round-trip. `Unconditional`, refusals `Built`. The package now also references
  `Fuaran.Core.DataFrame.PipelineQuery`. Additive over the untagged draft, so both ride `0.35.0`.

- **Additive — `Fuaran.Core.Column.Ops`: `ColumnOps.applyPrepared`, `canApplyPrepared`,
  `invertPrepared`, `deltaOfPrepared`.** `apply` / `canApply` / `invert` / `deltaOf` over a
  `Prepared`, with the same verdicts: `applyPrepared op p` answers a new version at the cost of what
  the op touches — a `SetCell` copies one chunk of one column, an `AppendRows` the partial last chunk
  of each, a `SetColumn` keeps every chunk whose cells it did not move — and leaves `p` untouched,
  which is what `invertPrepared` reads. On a coherent table, `apply op t` is
  `applyPrepared op (prepare t)` read back through `toTable`, and the suite holds it so op by op;
  the `Table` forms are unchanged and stay what the proved model and its oracle certify.
- **Additive — `Fuaran.Core.DataFrame`: `DataFrame.toTable : Prepared -> Table`.** The table a
  prepared source stands for: the very table it was prepared from, or — for a version an edit or a
  chunked refresh produced — built from its chunks on first read and kept, with every column an
  edit did not move handed back as the list it already was.
- **Additive — `Fuaran.Core.DataFrame`: `Incremental.refreshPrepared`, `refreshOnPrepared`,
  `resultPrepared`, `chunksTouched`.** `refresh` against a prepared version. Over a pipeline made
  only of `Derive`s it is the chunked path: a chunk the version shares with the one the state was
  last evaluated over — the same object, in every column — is recognised by identity and its output
  chunks reused, only the chunks the version moved are evaluated, and the output shares every chunk
  it did not; `primePrepared` builds the same shape. `chunksTouched` counts the chunks a state's
  evaluation touched (`None` off that path); `resultPrepared` hands the result on as a version, so
  a node feeding a node pays no boundary. Any other pipeline takes the row-local walk over the
  version's table, as `refresh` does. Every result still equals the reference's.
- **No surface change — the state's table and result are built on first read.** A state's
  `source` and `result` are the same tables they were; a chunked refresh no longer builds them to
  hand the state back, and `result` builds the output the first time a consumer asks.

- **Additive — `Fuaran.Core.DataFrame`: the planner (Phase 269).** `Plan.rewrite : Schema ->
  Transform list -> Transform list` (total, idempotent), `Plan.isTotal : Schema -> Transform -> bool`
  (the totality verdict over Phase 266's typer) and `Plan.explain : Schema -> Transform list ->
  PlanReport`, with the report vocabulary `RewriteClass` (`TopN` / `PruneColumns` /
  `FilterBeforeSort` / `FilterBeforeDerive`), `PlanRewrite`, `PlanDeclined` and `PlanReport`. Every
  evaluator entry point plans the pipeline before it folds it and runs `Sort` > `Limit` as one stable
  top-n; the answer is the reference's, errors included. `DataFrame.evalPipelineAsWritten` and
  `evalPipelineWithInEnvAsWritten` fold the pipeline exactly as given — the semantics the planner is
  held to (`Conformance.plannerLaws`) and what a host with a planner of its own certifies against.
  `Incremental.planOver : Schema -> Transform list -> IncrementalPlan * PlanReport` classifies the
  planned form, which is what the seam runs since this phase, and `Incremental.plannedOf` reports
  the form a state ran; `Incremental.plan` and `pipelineOf` are unchanged and read the pipeline as
  written. A `Filter` is moved ahead of a `Derive` only where the derive is total, unread by the
  filter, closed over the schema, AND the derived column's type is one the typer decides (an
  expression whose present values are all strings, or none): the evaluator infers a derived column's
  type from its cells, so any other reorder could change the output schema — a premise the phase
  corrected rather than implemented. Adjacent `Filter`s are not fused: the three-valued `And` reads
  both operands on every row, so a fused predicate would do more work than the two passes.
- **Additive — `Fuaran.Core.DataFrame.Conformance`: `DataFrameConformance.plannerLaws` (and its
  `Conformance.plannerLaws` forward).** The planned evaluation equals the reference as written over
  generated (schema, pipeline, table) triples, byte-for-byte on `Ok` and the same `EvalError` on
  `Error`; `Plan.rewrite` is idempotent; a step `isTotal` admits never errors over the drawn table.
  `Guarded` over the rewrite classes: fusion, pruning and a reorder must each be reached, a reorder
  declined, and the refused arm drawn.
- **No surface change — a tick mints each row's key once (Phase 273).** `Delta.diff` remembers the
  keys it mints for a table object, reads them back when it meets that object again under the same
  `RowIdentity.Scheme`, and the delta it returns carries the new table's keys to `Incremental.refresh`,
  which keys the source itself only when the delta carries none (hand-built, composed, decoded,
  ordinal, or diffed into another table object). Every answer, footprint, delta and wire byte is
  unchanged. The one sharpened reading: two witnesses sharing a `Scheme` must key every table
  identically, which is what a scheme naming its keying rule already meant.
- **Additive — `Fuaran.Core.DataFrame`: `RowIdentity.withKeyEquality`, `RowIdentity.checkKeyEquality`,
  `KeyEqualityDisagreement` (Phase 284).** `withKeyEquality equality w` returns a copy of `w` that
  declares the equality its `KeyString` agrees with, so `Delta.diff` pairs that witness's rows by the
  typed id (Phase 283's path, until now reachable only by `byColumn` / `byColumns`, which are declared
  through it too). `w` is left undeclared. The declaration is the caller's promise and is used as given:
  `Equals a b` exactly when `KeyString a = KeyString b`, equal ids hashed alike. A false one silently
  misses a `DuplicateIdentity` or mis-pairs rows. `checkKeyEquality w table` checks the promise over a
  table for a consumer's test suite and names the first disagreement (or `NotDeclared`); nothing in
  the library calls it. `RowIdentity`'s record shape, every delta, refusal and wire byte of an existing
  witness are unchanged. Additive over the untagged draft, so it rides `0.35.0`.
