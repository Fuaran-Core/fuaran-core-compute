### A derived column is typed by its expression, not by its first cell (Phase 338, `DECISIONS.md` D5) — BREAKING, `behaviour`

**What changed.** `Derive` and `Unpivot` type the column they produce by ONE rule
(`Fuaran.Compute.DataFrame`), which the evaluator, the incremental seam (its row walk and its
chunked path), `SchemaWalk`, the planner and `PipelineQuery.check` all read:

- **Where the typer decides the expression, that is the column's type on every frame** — a full
  one, an empty one and one where every cell is null. `Derive("y", Col "x" + Lit 1)` over an int `x`
  is an `int` column over an empty frame; it was a `string` column. An expression that can produce
  no present value (`Lit Null`, a `Case` of nulls) is `string`, as before.
- **An `Unpivot`'s `value` column is the widening join of its value columns' DECLARED types** —
  an `int` beside a `float` is a `float` column, an `int` beside a `decimal` a `decimal` column, on
  every frame. It was the FIRST value column's declared type, so an `int` and a `float` value column
  melted into an `int` column holding floats.
- **Only the cells decide where the typer cannot:** a derive reading a `Param` or a `Now`, a
  column the schema does not carry, or a join the exact typer keeps apart (a `Case` of an `int` and
  a `float`, `D3`). There Phase 321's widening join of the present cells stands, `string` when there
  is none; and an unpivot of value columns no widening relates (a `string` beside an `int`).
- **A `float` beside a `decimal` in one derived column is REFUSED by name** — a `TypeError`
  `derived column '<name>' joins a float and a decimal: cast one to the other's type first …`.
  Where the expression's own arms (through `Case` and `Coalesce`) carry both, on EVERY frame, an
  empty one included, before any row is evaluated; where only the cells show it, over the whole
  column. An unpivot of a `float` and a `decimal` value column is refused the same way, naming
  `value`. It was typed by the earlier of the two types (Phase 321).
- **No cell value moves.** A present cell is stored as the expression produced it — an `Int` in a
  `float` or `decimal` column stays an `Int` (it widens into the column's type, as the substrate's
  `Table.validate` admits) — so every change above is a column TYPE tag, or a refusal.

**The static readers follow.** `SchemaWalk` states every decided derive's type (it stated only
string-valued ones) and the decided unpivot `value` type. `Plan.isTotal` calls a `Derive` total
only where its expression is total AND its column type is decided; the planner's
`FilterBeforeDerive` drops its string-only clause, so a filter now moves ahead of any total,
decided derive — an `int` or `float` one included — and a derive only its cells type is declined,
by that reason. `PipelineQuery.check` registers a result column of an `int`, `float`, `decimal` or
`bool` derive; `ResultDisagreement.TypeUndecidable` is now only the data-decided remainder above.

**The law vectors that moved.** `conformance/laws/transform-laws.json` is re-emitted at `0.37.0`:

- `transform-5-div-by-zero` and `transform-13-div-by-zero` — `Derive("q", v / 0)` answers `Null` on
  every row, and the column `q` is now `float` (it was `string`); the cells are unchanged.
- Eight vectors are APPENDED after the decimal ones, `transform-39` … `transform-46`, the typing
  shapes: a decided `int` and `decimal` derive over an empty and an all-null frame, a `Case` of an
  `int` and a `float` (the cells decide), an `int`/`float` unpivot over a full and an empty frame,
  and the `float`-beside-`decimal` refusal. `iterations` is `47`.
- Every other vector is byte-identical, the fourteen other base vectors included.

**Additive, beside it:** the `DeriveTypingConformance.laws` family in
`Fuaran.Compute.Conformance` — one type on every frame, the walk is the evaluator, refresh is full,
every present cell admitted by its column's type, and the refusal — with its go-red against the
first-present-cell rule in the suite.

**Migrating.** A consumer that read a derived column's type off an empty or all-null result reads
the expression's type now; one that relied on `string` there must say so with a `Cast`. A pipeline
that put a float beside a decimal in one derived column must `Cast` one to the other.
