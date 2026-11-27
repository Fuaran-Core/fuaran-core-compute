### A derive its params decide is typed by them on every frame; `Round`, `Floor` and `Ceil` over an undecided operand are no longer typed `float` (Phase 413, `DECISIONS.md` D23) — BREAKING, `behaviour`

**What changed.**

- *Round, Floor and Ceil over an undecided operand.* Over an operand the typer cannot decide (a
  `Param`, a join of an int and a decimal, a column the schema lacks), they are undecided, where the
  typer called them `float`. Over a decimal they answer a decimal, so the old typing let a `float`
  column hold a decimal cell. A frame whose rows took both an int and a decimal arm threw at the
  `Table` boundary. `DataFrame.typeOf` and `SchemaWalk` now name no type for such an expression, and
  `PipelineQuery` answers `TypeUndecidable` for a declared one. The evaluator types the column by its
  cells (or its params, below): a float over int rows, a decimal over decimal rows, and the
  float-beside-decimal refusal by name where they hold both. Over a decided operand nothing changed.
- *Derives a bound param decides.* Where the schema leaves a derive to the cells but its expression
  with the evaluation's params substituted decides it (`Param "p"`, `i + p`, `Round(p)`), every
  evaluating path now types the column by that decision. That covers the reference, the morsel
  runner, and the incremental seam's prime, refresh and chunked path. An empty or an all-null frame
  is typed by the bound cell where it was typed `string`. A full frame is typed as before. A bound
  float beside a decimal stays the cells' to refuse, as it was. The static readers hold no env and are
  unchanged.

**The class.** No public signature moves; the member that carries the evaluation's rule is internal.
What a consumer can observe is a column's type: a `behaviour` change. The `0.39.0` draft is untagged,
publicly unpinned and already carries a BREAKING class (Phase 423), so this change RIDES it.

**The law vectors.** No vector in `conformance/laws/transform-laws.json` derives a rounding function
of an undecided operand or a param over an empty frame, so none moves. The laws gained here are kit
laws, not corpus vectors: `DeriveTypingConformance.laws`' sixth law and the suite's generated-
expression law. The corpus copy follows the release rule (D6 (4)) unchanged.

**The model.** `proofs/Pipeline.fst` changes `typing_apply_fn`'s three rounding arms and adds
`substitute` and `derived_typing_in`, which `eval_derive` now reads. `derive_type_rows_free` covers a
derive that `derived_typing_in` decides. `oracle/Pipeline.fs` is re-extracted.
