### The evaluator propagates and checks units (Phase 429, `DECISIONS.md` D29) — BREAKING, `surface` + `behaviour`

**What changed.** A column's unit (`Field.Unit`, Phase 427) is now READ. Every verb that keeps a column
keeps its field — unit, label, description and extension metadata — and every verb that produces one
states its unit by rule: a `Derive` the unit `DataFrame.resolveUnits` derives for its expression, an
aggregate and a window their source's (a count or a rank none), a pivot's value columns their
aggregate's, an unpivot's `value` the one unit its columns share. Addition, subtraction, comparison,
`Coalesce`, a `Case`'s arms, `Least` / `Greatest` and `InList` require ONE unit among the operands that
state one — a different scale of one dimension included — and a join key pair and a set-op column pair
are held to the same rule; multiplication and division compose through Core's algebra; a column, a
literal, a param or a `now` that states no unit is UNCONSTRAINED and composes with anything, so a
pipeline over unitless columns answers exactly as it did (every pre-429 law vector is byte-identical,
and `proofs/Pipeline.fst`'s `unitless_unchanged` proves the check invisible there).

The public surface moves in four places:

- `ColExpr` gains `Convert of ColExpr * unit: UnitOfMeasure` — the one explicit conversion, resolved by
  substitution before evaluation into `Cast(FloatType, operand)` under the target unit, times the exact
  factor as a float literal where the operand's unit differs; a conversion across dimensions is
  refused. Wire: `{"$type":"convert","expr":…,"unit":"<canonical text>"}`.
- `EvalError` gains `UnitMismatch of step: string * expr: ColExpr * left: string * right: string` — the
  typed, static refusal: the step (`derive 'speed'`, `filter`, `join key 'd' = 'k'`, `union column
  'd'`), the expression the two units met in, and both units by canonical text. It is decided over the
  step's schema before any row, so an empty frame is refused as a full one is.
- `ColumnKnowledge` gains `Unit`, `Label`, `Description` and `Ext`, and a `ColumnKnowledge` module of
  builders (`named`, `ofField`, `withUnit`, `renamed`, `toField`, `toFieldAs`); `SchemaWalk.unitOf`
  reads a column's unit as `typeOf` reads its type. The walk now states every output column's metadata
  exactly as the evaluator gives it (`schemaWalkLaws`).
- `DataFrame.resolveUnits`, `unitOf`, `unitOfColumn`, `aggregateUnit`, `windowUnit` and `unpivotUnit`
  expose the check and its rules to a reader; `ResultDisagreement.UnitDiffers` is the registered
  query's refusal of a declared field whose unit is not the produced one (in either direction).

Behind the surface: the planner's schema carries fields, so a planned pipeline's output schema is the
reference's (`plannerLaws`); the incremental seam carries metadata through a refresh and through its
state wire (a field stating metadata crosses as Core's field document, one stating none as the
`[name, type]` pair it always was); the registry wire writes a field through `ColumnCodec.fieldJson`
(byte-identical for a field stating none); and the worker pool's hand-off carries the schema's field
documents, so the parallel path's answer is the sequential path's.

**The class.** `ColExpr` and `EvalError` gain a case and `ColumnKnowledge` gains four members: a
consumer that matches either union exhaustively, or constructs `ColumnKnowledge` as a full literal,
stops compiling (`surface`). And a pipeline the evaluator accepted at `0.39.0` can be refused at
`0.40.0` — only where it mixes two STATED units that are not one unit, which no pipeline over the
unitless schemas every consumer declared before Phase 427 does (`behaviour`).

**What a consumer does.** Add the `Convert` arm and the `UnitMismatch` arm; construct `ColumnKnowledge`
through `ColumnKnowledge.named` / `ofField`. Where a schema now states units, convert explicitly where
two scales of one dimension meet (`Convert(Col "dk", m)`), and read the refusal — it names the step, the
expression and both units.

**Proof and evidence.** `proofs/Pipeline.fst` section 13 (`resolve_units` clause for clause over the
algebra as a parameter; `resolved_convert_free`, `unitless_unchanged`, `refused_is_rejected`, the rule
lemmas, `checked_step_unitless`), the rows `pipeline-unit-check-unitless-unchanged`,
`pipeline-unit-check-accepted-is-derived`, `pipeline-unit-check-refused-is-rejected` (proved),
`pipeline-unit-differential` (tested) and `pipeline-unit-algebra-is-core` (assumed) in `proofs.json`;
`UnitPropagationTests` (one test per rule, every refusal class by name, the conformance go-red); the
twenty `unit-*` vectors appended to `conformance/laws/transform-laws.json` (seven refusals among them);
the unit laws in `DeriveTypingConformance.laws`, `schemaWalkLaws` and `plannerLaws`; `docs/units.md` is
the consumer note.
