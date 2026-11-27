### An int under a float or a decimal column reads as that type's value whatever the carrier — `Vec.packAt` widens it on the boxed form too (Phase 472, `DECISIONS.md` D40) — none, `behaviour`

**What changed.** `Vec.packAt`'s boxed fall-back — taken on a present cell the type does not carry, and for a
decimal column on texts `ScaledDecimal.scaleOf` cannot carry at one scale — kept every cell as it was, an
int included, where the typed carrier reads an int under a float column as the float of its value and
under a decimal column as the decimal of its value. So a column's VERDICT (typed or boxed), a function of
the whole column, decided how every int in it read, and one unrelated row — a decimal text past the width
arriving or leaving — changed the reading of every stable int with no delta naming those rows; Phases 464
and 470 each found the incremental walk diverging from the full evaluation on exactly that, and each fixed
it by tracking more of the verdict. The boxed form now widens an `Int` under a `FloatType` column to
`Float (float v)` and under a `DecimalType` column to `Decimal (string v)`, as the typed carrier does and as
Core's `Column.ofCells` holds it (Core has no boxed path; nothing moves there). A cell's reading is a
function of the cell and the column's type alone; the walk's verdict tracking (`typedPack`, the verdict
comparison in `retypedSince`) is deleted, the pack key is the type, and the proof models pack cell by cell
with `derive_then_filter` and `reorder_in_context` back on their pre-470 statements. `Cell.Int` is a 32-bit
int, exact in a float and in a decimal: no precision is lost.

**The class.** No public signature moves and no wire form moves (`api/`, the state codec and the law vectors
are unchanged). What a consumer can observe is a cell — a `Float` or a `Decimal` where a boxed column
answered an `Int` — and a refusal where arithmetic between that int and a float once answered over a
boxed decimal column (`arithmetic between a decimal and a float`, the typed carrier's answer): a
`behaviour` change on a degenerate (boxed) column, carried on the open `0.41.0` draft as an additive one
(the draft already carries a BREAKING class, so it rides).

**What a consumer does.** Nothing, unless a pipeline read an `Int` back out of a float or decimal column
the reference boxed and relied on it; it now reads the widened cell, as it always did from a typed one. A
consumer that wants an int arm read as a float or a decimal explicitly casts it (`Cast(float, …)`,
`Cast(decimal, …)`), as before.
