### The decimal across the strand beyond the evaluator (Phase 321) — BREAKING, behaviour

**What changed.** No public signature moved (the `api/` baselines and the wire-surface records are
as they were); four behaviours did, each recorded in `DECISIONS.md` D2 and D3.

- **`ColumnOps.apply` / `canApply` / `applyPrepared` accept a cell whose type widens into its
  column's** (`ColumnType.widens`): an `Int` in a `FloatType` or `DecimalType` column, stored as
  given. `SetCell`, `SetColumn`, `InsertColumn` and `AppendRows` that the strict check refused with
  `CellTypeMismatch` now apply; a `Float` in a decimal column and a decimal in a float or int column
  are still refused by name. `invert` is unchanged.
- **The columnar op wire writes a decimal's canonical text** (`1.50` is written `1.5`), the spelling
  the decoder already read; no canonical cell's bytes moved.
- **The evaluator's float order puts `NaN` above every value** (the substrate's `Cell.compare`), on
  every host: a `Sort` puts `NaN` after `+Inf` and before the nulls (on .NET it sorted first), and a
  float comparison `x > k` / `x >= k` holds for a `NaN` row while `x < k` / `x <= k` does not (on
  .NET the reverse). Under Fable the old order was not an order at all.
- **A derived column's type is the join of its present cells' types** under `ColumnType.widens`:
  a derive answering an `Int` and a `Float` types its column `FloatType`, an `Int` and a `Decimal`
  `DecimalType`, where the first present cell used to decide (`DataFrame.inferCellType` and the
  incremental seam's typing agree with it). A pair no widening relates keeps the earlier type, as
  before. `DataFrame.typeOf` is unchanged: the static typer stays exact.

Also: FS0025 is a compile error in every project (`Directory.Build.props`); the conformance families
`aggregateParityLaws`, `columnarOpLaws`, `schemaWalkLaws`, `paramLaws` and `IncrementalDelta.laws`
draw decimals and each carries a `sample adequacy` guard that goes red on a sample with none (so
`aggregateParityLaws` and `paramLaws` move from `Unconditional` to `Guarded`, and four families
report one more law each); the `ColumnOps` and `Pipeline` proof models carry the decimal column type
and the widening join. `conformance/laws/transform-laws.json` gains 13 vectors after the 39 it carried
(which are byte-identical): nine `columnOp` vectors and four `delta` vectors over decimal columns, two
case kinds a host meets when it next raises its copy. `0.35.0` is an untagged, publicly unpinned draft
that already carries a breaking move, so the number does not move.

**What adopting it costs.** A consumer that relied on the strict `cellFits` to refuse an int in a
float or decimal column adds its own check. A consumer that sorted or filtered floats holding `NaN`
on .NET sees `NaN` move from first to last. A consumer that counted a family's `LawResult` list by
length counts one more. A derive mixing ints with floats or decimals now yields a column the
substrate's validator accepts, where it yielded one it refused.
