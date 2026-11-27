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
