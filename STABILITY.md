# Fuaran.Core.Compute — API stability

**Status:** pre-1.0. The version is single-sourced from `<Version>` in `Directory.Build.props`;
this document records what each version changed, by class.

## Versioning policy

Every package this repository produces ships at the one `<Version>`. A change to a package's
public contract ships on a version AHEAD of every version that has been tagged. The standing
`<Version>` is a **draft slot** while it is untagged: an additive change rides it (its entry grows
here, the number does not move), and a change of a higher class than the draft already carries
advances it, because the number is what tells a consumer what adopting it costs. The class of a
move is not an argument — the `Public surface` family computes it from the committed baselines under
[`api/`](api/) (below).

A release is a `v*` tag, which runs the publish workflow (see the README's release section). An
entry header here names every version this repository has released; the `Package roster` family
holds the document to the repository's release tags, and while every entry header below is a DRAFT
it reads a clone with no release tag as "nothing released yet" rather than as a clone missing its
tags.

**The history before this repository.** The four packages shipped from the
[Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) repository at every version from `0.9.0`
(the first release that carried `Fuaran.Core.DataFrame`) through `0.32.0`, in step with the rest of
that repository's packages; `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
first shipped there at `0.32.0`, when Phase 257 cut them beside the other two. **Every one of those
versions is recorded in [that repository's `STABILITY.md`](https://github.com/Fuaran-Core/fuaran-core/blob/main/STABILITY.md)**,
and this document does not restate them: two copies of one record is how a record comes to
disagree with itself. This repository's first version is `0.33.0`, the next minor above the last
emission there (`DECISIONS.md` D1), so a consumer's version floor never moves backwards across the
change of producer.

## Public-surface baselines

Every packable package carries a committed baseline of its public contract at
[`api/<package>.txt`](api/), rendered from the built assembly's IL metadata; the wire-bearing
packages also carry a wire-surface record under [`api/wire/`](api/wire/). The `Public surface`
family renders each package afresh on every gate run and diffs it against its baseline, and refuses
a surface that moved while its baseline stood still — additive or breaking, a move whose baseline
moved with it passes. Regenerate with:

```
CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests
```

It rewrites EVERY drifted baseline, not only the one you were looking at: stage the baselines you
meant to move by name.

## 0.35.0 — DRAFT

Opened by Phase 268, which makes a prepared source a persistent VERSION — chunked columns that
successive edits share — and adds the op algebra and the refresh over it. The four packages ship at
this version together; `0.34.0` is tagged, so the additions advance the slot rather than ride it.

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

### Decimal arithmetic in the transform evaluator, and rounding as a typed policy (Phase 277) — BREAKING, `union-widening`

**What changed.** The substrate at `0.33.0` carries an exact decimal (`ColumnType.DecimalType`,
`Cell.Decimal` of canonical text; Core `DECISIONS.md` D72), and the evaluator now computes over it.
Exact where the operation is closed, a STATED rounding where it is not, refused by name where a
pipeline has not said, and never through a float unless the pipeline says `Cast`:

- **Exact, and a `Decimal`:** `Add`, `Sub`, `Mul` and `Mod` over two decimals or a decimal and an
  `int` (an int promotes, losslessly); negation by `Sub`; `Abs`; `Least` and `Greatest`; the six
  comparisons, by the column layer's exact order (`Cell.compare`). Nothing overflows — the digits are
  strings. A decimal `Mod` by zero is `Null`, as an int's is.
- **Rounding is a typed policy (new types).** `RoundingMode` — `HalfEven`, `HalfUp`, `HalfDown`,
  `Up`, `Down`, `Ceiling`, `Floor`, the seven `java.math.RoundingMode` names, shipped whole — and
  `Rounding = { Scale: Slot<int>; Mode: RoundingMode }`. The scale is a literal or a named param, as
  `Limit`'s count is: it is reported by `ColExpr.paramsOf` / `Transform.paramsOf`, substituted by
  `substitute` (bound only to an `Int`), unbound is `UnboundParam`, a wrong shape a `TypeError`
  naming the rounding scale, and outside `0 .. 1000` a `TypeError` naming the range. An unknown mode
  cannot be written; its spelling exists only in the codec.
- **Two new `ColExpr` cases.** `Quotient(dividend, divisor, rounding)` — the exact quotient of two
  exact numbers, correctly rounded (long division to the scale, the remainder deciding the last
  digit under the mode: one rounding of the exact value, never two); a zero divisor is `Null`, as
  `Div`'s is. `Rounded(expr, rounding)` — an exact number brought to the scale. Both take `Decimal` or
  `Int` operands (an int promotes), answer a `Decimal`, propagate a null, and refuse a `Float` naming
  the `Cast`. Evaluation order: the scale, then the operands left to right.
- **`Round`, `Floor` and `Ceil` stay unary and are total over a decimal:** each is exactly a scale-0
  `Rounded` under its pinned mode — `HalfUp`, `Floor`, `Ceiling` — through the same rounding kernel,
  answering a `Decimal`. Over an int or a float they are unchanged.
- **Refused by name:** `Binary(Div, …)` over a decimal names `Quotient` and its rounding; `Sqrt` of
  a decimal and any decimal beside a `float` — in arithmetic, a comparison, `Quotient` or `Rounded` —
  name the `Cast` that resolves them. `ColumnType.widens` refuses that retype in either direction,
  so the evaluator does too.
- **`Cast`:** to `decimal` from an `int` (exact), a string (the decimal grammar) or a `float` — the
  one place an approximation enters a decimal: the float's shortest round-trip digits, laid out
  without an exponent. From `decimal` to `float` (the nearest float; past the float range a
  refusal), to `int` (truncated toward zero; past `int32` an `OverflowError`) and to `string`.
- **Keys:** `GroupBy`, `Distinct`, `Intersect`, `Except`, `Pivot` and `Window` partitions key a
  decimal on its canonical token (`Cell.token`'s `m:` spelling, so `1.50` and `1.5` are one value).
  A `Join` key and a pivot value match a decimal to a decimal of the same value and to nothing
  else: not to a float, and not to an int either, because an int already matches the float of its
  value there and one hash token cannot hold both. `InList` / `InParam` compare by the exact order,
  and a float item beside a decimal subject is a type error. `Sort` is the exact order.
- **Windows:** `CumulSum` and `RollingSum` over a decimal column are exact and `decimal`;
  `RollingMean` stays a `float`, as `Mean` over a decimal column is (D72 K7), each value read at its
  nearest float. `SchemaWalk` types a running total as the evaluator does — `decimal` over a decimal
  column, `float` over another, and unknown where the source column's type is unknown (it was
  `float` there; the new answer is the honest one).
- **The typer and the planner.** The typer gives every decimal operand's result type, and `Decimal`
  for `Quotient` / `Rounded` over exact operands. The totality verdict admits decimal `Add` / `Sub` /
  `Mul` / `Mod`, an exact comparison, `Abs`, `Round` / `Floor` / `Ceil` of a decimal, a `Cast` to
  `decimal` from an `int` or a decimal, and `Quotient` / `Rounded` over exact (or null) operands at
  a literal scale in range; it declines a decimal `Div`, a decimal beside a float, a param scale and
  a literal scale out of range. `proofs/Pipeline.fst` carries the decimal in `column_type` and
  `cell`, the two nodes and the rounding types, their two primitives in `prims`, and the clauses in
  its typer and verdict; `verdict_sound` verifies (every query 3/3 under `--quake`), and the
  committed extraction `proofs/oracle/Pipeline.fs` is byte-identical to a fresh one.
- **The dense frame:** a decimal column is packed boxed and every kernel over it reads the
  reference arm; its typed vector is Phase 280's.
- **The wire:** a decimal literal is `{"$type":"Decimal","value":"<decimal text>"}` in a pipeline
  and `{"$type":"Decimal","v":…}` in a columnar op — a JSON string, canonicalised on read, an
  integer token read as exact, a fractional number token refused (D72 K5). The nodes are
  `{"$type":"quotient","dividend":…,"divisor":…,"rounding":…}` and
  `{"$type":"rounded","expr":…,"rounding":…}`, with `rounding` =
  `{"mode":"half-even"|"half-up"|"half-down"|"up"|"down"|"ceiling"|"floor","scale":<int slot>}`; an
  unknown mode is an `UnknownType` decode error naming the seven.
- **The law vectors.** `conformance/laws/transform-laws.json` gains 23 decimal vectors after the 16
  it carried, which are byte-for-byte as they were: a decimal column through every verb, the exact
  arithmetic, `Quotient`, `Rounded`, `Round` / `Floor` / `Ceil`, the casts, and four refusals.
  `plannerLaws` draws a decimal column and guards a `decimal sample`; `incrementalLaws` evaluates a
  decimal derive and a decimal sum through the seam.

**Class: `union-widening` — breaking, and it RIDES this slot.** `ColExpr` gains `Quotient` and
`Rounded`, so an exhaustive `match` over `ColExpr` stops compiling; `RoundingMode` and `Rounding` are
new (`api/Fuaran.Core.DataFrame.txt`). The wire baselines of `Fuaran.Core.DataFrame` and
`Fuaran.Core.Column.Ops` gain the decimal cell, the decimal column type, the two nodes and the seven
modes. The wire gate classes the move `breaking` for one reason worth naming: the documents it names
for `Slot<Int32>.Lit` and `.Param` are now first reached through a rounding's scale rather than
through `Limit`, so those two specimens' bytes moved; no byte any existing pipeline emits changed,
and the `Transform.Limit` document is as it was. `0.35.0` is an untagged, publicly unpinned draft
that already carries a breaking move (the entry above), so the number does not move.

**What adopting it costs.** A consumer that matches `ColExpr` exhaustively adds the two arms. A host
evaluator certifying against the law vectors meets the decimal ones: until it computes over decimals
and the two nodes it refuses where the reference answers, and the parity law names the vector.

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
