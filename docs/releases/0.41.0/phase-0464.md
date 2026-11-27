### A refresh reads a derived column the cells retype as the reference packs it (Phase 464, `DECISIONS.md` D37) — none, `behaviour`

**What changed.** A derived column typed by its cells — an int arm beside a float arm in a `Case`, a
`Coalesce` of an int column and a float literal — is typed over the whole column, so one changed row can
retype it from int to float while every other row stands. The reference packs the column under its
decided type (an int under a float column is the float of its value, Core `1.0.0`); the incremental walk
held the step's cells as evaluated. A step downstream that tells an int from a float — a `Mod`, a later
column the cells type, a group aggregate — read a cell the reference does not hold: `Incremental.prime`
and `Incremental.refresh` answered where `DataFrame.evalPipelineWithInEnv` refused `mod requires
integer operands`, and a reused group's `Sum` stood at `Int 3` beside the reference's `Float 3.0`. The
walk now carries the column widened to its decided type, a derive whose type is not the prior
evaluation's clears every cached cell and every reused group downstream of it, and the in-place group
path declines a frame that is no longer stable. The result equals the full evaluation on these shapes,
as the seam has always claimed; the cache keeps the cell as evaluated, so a column typed back on a later
refresh is typed as the reference types it.

**The class.** No public signature moves and no wire form moves (`api/`, the state codec and the
law vectors are unchanged). What a consumer can observe is a result — a refusal where a value was
answered, a float cell where an int was — on a pipeline whose derived column retypes between refreshes,
which is the full evaluation's answer: a `behaviour` change, carried on the open `0.41.0` draft as an
additive one (the draft already carries a BREAKING class, so it rides).

**What a consumer does.** Nothing, unless a pipeline relied on the walk's raw reading; a consumer that
wants an int column to stay an int casts the float arm (`Cast(int, …)`) or the whole derive, as the
reference has always required.
