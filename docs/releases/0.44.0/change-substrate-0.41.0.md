### The Core floor rises to `0.41.0`, and the evaluator is faster (`DECISIONS.md` D59 to D73) — no contract change

**What changed.** Every package now pins `Fuaran.Core.*` `0.41.0` (from `0.40.2`), so a consumer restoring `0.44.0`
restores Core `0.41.0` with it. The evaluator's internals moved: a date compared on its day count and a cheaper join
(D59, D60); a date, timestamp and decimal key coded by value (D61 to D63); key dictionaries sized from a sample (D64); a
grouping's and a join's key coding, aggregates, emit, pairs and gather spread across threads on the native kernel set
where the rows are many (D65 to D71); and range bounds computed without an emulated integer under JavaScript (D73). D72
records the parts measured and not kept.

**What a consumer must do.** Nothing beyond adopting Core `0.41.0`. No baseline under `api/` moved for this work, no wire
shape moved, and every answer is the answer it was: each parallel path is held byte for byte to the sequential one by the
suite's laws, on every member.
