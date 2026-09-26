# Adopting incremental `Transform` evaluation

A one-page on-ramp for a consumer that already evaluates a `Transform` pipeline with
`DataFrame.evalPipeline` and wants a refresh to cost the rows that changed rather than the rows it
has. Read it alongside [`ADOPTION.md`](ADOPTION.md); nothing here replaces the reference evaluator,
and adopting it is reversible at any point.

Everything below is in `Fuaran.Core.DataFrame` (`0.11.0`), FSharp.Core-only and Fable-clean.

## The shape

Three calls, and the middle one is the whole of it.

```fsharp
open Fuaran.Core

// 1. Tell Core how to key a row. A per-call witness — Core never learns what a key MEANS.
let idw = RowIdentity.byColumn "id"

// 2. Prime once. This is a full evaluation; it also records what it computed.
let state = Incremental.primeOn idw pipeline source |> Result.defaultWith (fun _ -> failwith "…")

// 3. On each change, describe it and refresh.
let delta = Delta.diff idw source source' |> Result.defaultValue FullRefresh
let next = Incremental.refreshOn idw pipeline state delta source'
```

`Incremental.result next` is the new table, and it is **equal to `DataFrame.evalPipeline pipeline
source'`** — always, for every delta, whichever internal path ran. That equality is the contract;
the saving is the implementation detail. `Incremental.footprint next` says what the refresh cost.

Where the pipeline resolves `Ref` sources or reads `Param`s, use `Incremental.prime` /
`Incremental.refresh`, which take a resolver and an env exactly as
`DataFrame.evalPipelineWithInEnv` does.

## Ask before you adopt

`Incremental.plan pipeline` classifies every step **before any evaluation happens**, so a consumer
can decide what to wire without running anything:

```fsharp
match (Incremental.plan pipeline).Strategy with
| RowLocal | RowLocalThenGroups -> // a refresh will be restricted
| ReferenceOnly reason -> // it will not; `Incremental.reasonString reason` says why
```

- **`PropagateRows`** — `Filter`, `Project`, `Derive`. Output for a row is a function of that row,
  so only the named rows are re-evaluated.
- **`MaintainGroups`** — a `GroupBy`, at **any** position. The group partition is maintained and
  only the affected groups' aggregates are recomputed; the steps **after** it walk the group table
  the same way the steps before it walk the source rows, so a `Having` — which is a `Filter` after a
  `GroupBy`, there being no `Having` verb — is restricted too. This was declined until Phase 202, on
  the reasoning that what follows a group-by would need a delta over the group table. It does, and
  there is one: the set of groups whose aggregates were recomputed, which this step has always
  computed and the state has always recorded. A group whose aggregates were *reused* has a row
  identical to the one the tail last read, so the tail reuses its cached cells for it.

  **Every aggregate function is maintained** — `sum`, `mean`, `min`, `max`, `count`, `median`,
  `stdDev`, `first`, `last`, `countDistinct` — and none is declined. That is worth stating plainly
  because the opposite is the usual expectation: an incremental group-by normally maintains a
  *running accumulator* per group, where only the decomposable aggregates work, a deletion from a
  `min`/`max` group needs a tombstone and a re-scan, and `median` is out of reach. This seam keeps
  no accumulator. It recomputes an affected group **from that group's own rows**, through the same
  aggregator the reference evaluator calls, so decomposability is not a property it needs and a
  deletion is not a special case. What it trades for that is the obvious thing: a group with a
  thousand members costs a thousand-row aggregation when one of them moves, where a running `sum`
  would cost an addition. The saving is that the *other* groups cost nothing, and that the steps on
  both sides of the group-by stop re-evaluating every row.

  The one decline is a **second** `GroupBy` in the same pipeline, reported as `AggregateStepRepeated`:
  grouping the group table needs a second level of row-to-group, ordered-membership and per-group
  aggregate state. It refuses as data, never by a silent full re-evaluation.
- **`MergeOrder`** — a `Sort`, at **any** position. It computes nothing and moves everything, so the
  new order is the previous order with the named rows merged back into it. The saving is not in the
  sorting — a sort evaluates no expression and is charged none — it is that the steps *before* the
  sort stop re-evaluating every row.
- **`RecomputeFrame`** — a `Window`, **any** window function, at **any** position. It appends a
  column computed over each row's partition and emits the rows it was handed one for one, which is
  all a restricted walk needs: what admits it is that it *preserves the row set*, not that its frame
  is bounded. The column is recomputed over the walked frame rather than read from a cache — a
  window evaluates no expression, so it is charged none either way, and a row's output moves when
  another row in its partition moves. As with a sort, the saving is that the steps *before* it stop
  re-evaluating every row; a `rank` refresh still sorts its partitions, and that work is not on the
  `rowsEvaluated` scale.
- **`FilterByRelation`** — a `Join` whose kind is **filtering** (`semi`, `anti`). It keeps or drops
  each row on whether it matches the joined relation and emits the row it kept unchanged, so a delta
  propagates through it exactly as through a `Filter`. The cached verdict for a row the delta did not
  name is reused only while the relation's **key index** is unchanged: the delta describes the
  source, so it cannot say the relation moved.
- **`TruncateOrder`** — a `Limit`, at **any** position, over **any** order the walk produced. It
  computes nothing and keeps a slice of the rows it was handed, in the order it was handed them, so
  a row outside the window leaves exactly as a filtered row leaves and only the rows that *enter* or
  *leave* the window change the output. There is no condition on which order it reads, and the
  absence is deliberate: every step admitted above preserves the reference's row set and order, and
  a step that does not declines the whole pipeline before the limit is reached — so a `Limit` the
  walk reaches is over a maintained order by construction. As with a sort, a limit evaluates no
  expression and is charged none; the saving is that the steps *before* it stop re-evaluating every
  row, which on a `Filter > Sort > Limit` board is the whole of the pipeline's cost. A `Limit` whose
  count or offset is still a `Slot.Param` declines as `UnresolvedSlotParam` — substitute the params
  (`Transform.substitute`) and the plan is computable again.
- **`FallBack`** — `Distinct`, `Pivot`, `Unpivot`, `Union`, `Intersect`, `Except`; and a
  **combining** `Join` (`inner`, `left`, `right`, `outer`), which the reason names by kind. Their
  output for one row depends on rows a delta does not name — or is not one row at all — so the
  pipeline is evaluated in full and the footprint says so. A **second** `GroupBy` joins them, as
  `AggregateStepRepeated`. Two reasons are **retained and no longer produced by any plan**, because
  removing a case from a published union breaks every consumer that matches on it and a stored
  footprint still has to read: `WindowFrameUnbounded` (every window function is admitted) and
  `AggregateStepNotLast` (a group-by is admitted at any position).

Adoption is therefore per pipeline, not per application: a declined pipeline costs exactly what it
costs today, and can sit beside an adopted one.

## Reading the footprint

`Incremental.footprint` returns `{ SourceRows; ResultRows; Recompute }`. `Recompute` is the account:

| Case | Meaning |
|---|---|
| `Primed n` | the first evaluation — `n` row expressions evaluated |
| `ReusedPrior` | nothing changed and the source did not move; the prior result stands (not taken when a step names a `Ref` relation — `resolve` may answer differently, and no comparison the state holds would see it) |
| `RowsRecomputed n` | only the delta's rows were re-evaluated |
| `GroupsRecomputed (n, g)` | `n` rows re-evaluated, `g` groups' aggregates recomputed |
| `FullRecompute (n, reason)` | the pipeline was evaluated in full, `n` row expressions evaluated; `reason` says why |

`Incremental.rowsEvaluated` and `Incremental.footprintString` project it. The counts carry no clock,
so they are deterministic and identical on every host — which is what makes them safe to assert on
in a consumer's own tests, and what lets a regression ("this refresh started recomputing
everything") be a failing assertion rather than a stopwatch reading.

**Every `n` above is the same unit: one evaluation of one step's expression against one row.** A row
that passes three evaluating steps counts three times; a step that evaluates no expression — a
`Sort`, a `GroupBy`, a `Project` — counts none; and `SourceRows` is a separate field that never
stands in for the count. That matters because the comparison a consumer actually wants to make is
between a refresh and the full evaluation it replaced, and the two are only comparable on one scale:
a full evaluation reported as its source row count would charge a three-step pipeline for a single
pass, so a decline would read as *cheaper* than the baseline it fell back to.

**A prime is always `Primed`, even for a pipeline the plan declines.** Priming evaluates everything
whatever the plan says, so there is no fall-back to report; the decline and its reason attach to a
**refresh**, where the fall-back actually happens. Ask `Incremental.plan` whether refreshes will be
restricted — that is what it is for, and it answers before any evaluation. The one thing a prime's
footprint does report is a defect `plan` cannot see: an identity witness that could not key the
source arrives as `FullRecompute (n, RowIdentityUnusable …)`, because the footprint is its only
channel.

## The one obligation

**The delta must truthfully describe the change** from the source the state was last evaluated
against to the source now passed in. `Delta.diff` produces exactly that. A delta that under-reports
is a statement about the data that is false, and no evaluator can detect one without recomputing the
answer it was asked to avoid recomputing.

Everything else is handled for you and recorded rather than assumed: a changed pipeline, a changed
env, a moved schema, a `FullRefresh` delta, an ordinal-addressed delta, or an identity witness that
cannot key the source each degrade to a full evaluation carrying its reason. Degrading is always
available and always correct.

## What it costs on the clock

The footprint counts what a refresh RE-EVALUATES. It does not track time, and until Phase 206 the
two answers disagreed: the refresh really did evaluate one row expression instead of ten thousand,
and it still finished **after** the full evaluation it replaced.

The cause was not the seam. A table stores its cells column-major as `Cell list`, and every
consumer that wanted rows asked for them one index at a time through `Column.cell i c` — `List.item`
over a linked list, so each cell read walked its column from the head and reading an n-row table
cost O(n² × columns). The reference evaluator's frame, `Delta.diff`'s row comparison, the reference
identity witnesses and the seam's own transposes all did it, and four grouping folds compounded it
by appending to an accumulator once per row. Every one of those is a single pass now, and no public
signature moved to get there.

Measured on one machine over a `Filter > GroupBy` pipeline with one row of the source edited.
Both columns are the median of three timings after a warm-up — the same estimator on both sides,
which is what makes them comparable:

| | before, 1,000 | before, 20,000 | ratio | after, 1,000 | after, 20,000 | ratio |
|---|---|---|---|---|---|---|
| reference evaluation | 19.27 ms | 3,153.39 ms | 163.67 | 1.37 ms | 27.49 ms | 20.26 |
| `Delta.diff` | 18.62 ms | 7,689.30 ms | 412.85 | 4.06 ms | 107.20 ms | 26.91 |
| prime + diff + refresh | 36.36 ms | 16,015.97 ms | 440.48 | 6.45 ms | 246.03 ms | 38.43 |

A ratio of about twenty for twenty times the rows is the linear shape; four hundred is the
quadratic one. The `Scaling` family in this repository's suite holds that shape rather than any
absolute time, because Core owns no clock and an absolute threshold is a test that eventually fails
on a slow runner for a reason nobody can act on.

The family itself reports slightly **higher** post-fix ratios — about 34, 52 and 44 at Phase 206, and
28, 49 and 56 after Phase 208 lowered both legs' absolute cost — because it
uses the best of five timings rather than a median. Noise here is additive, so the minimum is the
better estimate of the true cost, and it is at the 1,000-row end that a median is most inflated by
fixed overhead: removing that inflation raises the ratio. The honest reading is that none of the
three is exactly linear, and none needs to be — they are n log n over keyed maps whose comparisons
are string and string-list shaped. The bound the family enforces is five times the linear
expectation, which clears those shapes twice over and still refuses a quadratic four times over.

The same figures hold under **Fable**, which is what you would expect and is worth having measured
rather than assumed: the access pattern was in shared code, so both pipelines carry the fix. On
node, the reference evaluation runs 1.47 ms → 20.91 ms and `Delta.diff` 3.78 ms → 95.69 ms over the
same 1,000 → 20,000 span — ratios of 14 and 25, which is the linear shape. That ratio IS the
evidence: a quadratic JS evaluator would score in the hundreds here whatever the machine. The
`Scaling` family gates the .NET leg only; the JS figures are recorded, not enforced, because a
wall-clock bound on a JS runtime is a test about the runtime.

### When the seam actually pays — it is about the row expression, not the row count

Removing the quadratic did not by itself make a restricted refresh cheaper than a full evaluation.
It made the seam's **own per-row bookkeeping** the dominant cost:

| row expression | restricted refresh | full evaluation | |
|---|---|---|---|
| one `Ge` comparison | 88 ms | 20 ms | the seam **loses** |
| 129 expression nodes | 99 ms | 224 ms | the seam **wins** |

Both rows are 20,000 rows with one row edited. Read them together: the refresh's cost barely moves
between them (88 → 99 ms) while the full evaluation's rises elevenfold. The refresh pays, per
source row, for an identity token, its uniqueness check, two lookups into the prior row-cell map
and a group-membership entry — string-keyed persistent-map operations that do not shrink when the
expression does. What it saves is n−1 evaluations of the row expression. So the seam is worth
taking when **the row expression costs more than that bookkeeping**, and the row count is not the
variable that decides it: both costs are linear in n, so a bigger table scales the saving and the
spend together.

**A top-N board narrows that gap but does not close it** (Phase 207, same machine, same 20,000 rows
and one edited row, over `Filter > Sort > Limit 10`):

| row expression | restricted refresh | full evaluation | |
|---|---|---|---|
| one `Ge` comparison | 89 ms | 56 ms | the seam **loses**, by 1.6× |
| 129 expression nodes | 87 ms | 245 ms | the seam **wins**, by 2.8× |

The narrowing is the measurable part and it is worth reading rather than glossing: a top-N *full*
evaluation sorts the whole frame every tick, where a restricted refresh merges the named rows into
the order it already holds — so the seam has a second saving here that `Filter > GroupBy` had no
equivalent of, and the trivial-predicate gap falls from 3.4× against it to 1.6×. It is still
against it. **Admitting the `Limit` did not change which variable decides the question**, and the
honest statement of what the admission bought is that a top-N pipeline can now be *refreshed at all*
rather than falling back — on the same terms as every other admitted pipeline, and with the same
row-expression threshold deciding whether that is worth doing.

**A `Having` behaves the same way, and the third measurement is the one that settles the pattern**
(Phase 202, same machine, same 20,000 rows and one edited row, over
`Filter > GroupBy > Filter`):

| row expression | restricted refresh | full evaluation | |
|---|---|---|---|
| one `Ge` comparison | 61 ms | 20 ms | the seam **loses**, by 3.1× |
| 129 expression nodes | 61 ms | 211 ms | the seam **wins**, by 3.5× |

Three admissions have now been measured on this axis and all three answer the same way, so the
threshold is a property of the seam rather than of any pipeline shape: the refresh's cost is
**61 ms in both rows** — it does not move at all with the expression — while the full evaluation's
rises tenfold. The tail itself is charged by the GROUP count, which is small and bounded, so
admitting it moved the row-expression threshold not at all. What it bought is the same thing the
`Limit` admission bought: the pipeline can be refreshed *at all*.

### Where the sixty milliseconds went — the profile (Phase 208)

Three phases measured the total and agreed on it; none of them measured the SPLIT, and the split is
what says whether the cost is reducible. Profiled at 20,000 rows with one row edited, on the three
pipelines above, with a timestamp around each stage inside the seam and an outer stopwatch over the
same call so the unaccounted remainder is visible rather than assumed (it was under 0.7 ms in every
case):

| stage | `Filter>GroupBy` | `Filter>Sort>Limit` | `Filter>GroupBy>Filter` |
|---|---|---|---|
| minting the row identity tokens | 14.2 ms (20%) | 26.3 ms (19%) | 11.9 ms (14%) |
| building the per-row work items | 5.4 (8%) | 7.7 (6%) | 5.2 (6%) |
| the row-local walk | 7.2 (10%) | **91.6 (66%)** | 7.0 (8%) |
| writing the row cache back | **19.0 (27%)** | 14.2 (10%) | **25.3 (29%)** |
| the maintained grouping | **25.2 (36%)** | — | **37.8 (43%)** |
| the steps after the grouping | — | — | 0.02 (0%) |
| assembling the output table | 0.005 | 0.011 | 0.004 |

Evaluation is nowhere in it. Assembling the answer costs five microseconds; the group tail over
seventeen groups costs twenty. What the sixty milliseconds bought was **string-keyed
persistent-map traffic, all of it proportional to the table and none of it to the delta**: the row
cache rebuilt from empty on every refresh (20,000 tree insertions, each a path copy, to serve a
delta naming one row), a group identity minted per source row, a row-to-group map of 20,000 entries
built to answer one row's question, and — on the top-N shape — three 20,000-entry string-keyed
structures built inside the sort step and then read through a comparator that looked up both sides.

### And after it — the refresh pays for the delta (Phase 208)

The representation the state publishes was the reason none of that could be fixed: it was a
consumer-visible contract, so each of the three phases left the keying as it found it. `0.27.0`
makes the state opaque and re-keys it. The per-row caches are **positional arrays** indexed by the
row's slot in the source's own row order, with the row's identity token carried alongside; a row
still sitting where it sat is recognised by one comparison, a row that moved costs a lookup into an
index built once on the first miss, a group identity is **carried** rather than re-minted for a row
whose cells have not moved, and the partition is accumulated in mutable locals that never escape the
function. Same machine, same 20,000 rows, same single edited row, same estimator:

| pipeline, one `Ge` comparison per row | refresh before | refresh after | full evaluation | |
|---|---|---|---|---|
| `Filter > GroupBy` | 72.3 ms | **13.0 ms** | 26.3 ms | the seam **wins**, by 2.0× |
| `Filter > Sort > Limit 10` | 102.5 ms | **25.8 ms** | 55.7 ms | the seam **wins**, by 2.2× |
| `Filter > GroupBy > Filter` | 69.8 ms | **10.4 ms** | 16.7 ms | the seam **wins**, by 1.6× |

**So the answer to "when does the seam pay" has changed, and the three tables above are kept as the
record of what it used to be rather than as current advice.** A refresh is now cheaper than the full
evaluation it replaces for a row expression of ONE COMPARISON, in a pipeline that shrinks the table
(the next section measures the opposite for one that keeps every row), which is the shape a live
board actually has, and the `Scaling` family asserts it on all three pipelines — the case those three
phases each printed and deliberately did not assert. A costlier row expression widens the margin
further; it is no longer what decides the question.

What has NOT changed is the floor, and it is worth stating because it bounds what a later phase can
buy. The refresh is handed the whole new source and must read every row of it to know what moved, so
one pass over the frame is proportional to the table and nothing reachable from this entry point
removes it. What is proportional to the delta is everything else: the keyed lookups, the identity
derivations and the cache writes. `IncrementalRefreshCostTests` counts exactly that, clock-free, and
holds the shape it replaced to the opposite result.

The profile after the change says where the remaining time is, for whoever comes next: the row-local
walk, which is now 40-60% of a refresh (and 86% of the top-N one, where it is the merge). Per row it
allocates an option for the cache read, a tuple and a `Result` for the cell, and a fresh work record
— four allocations to reuse one cached cell. That is the next thing to measure, not the next thing to
assume.

### Row-preserving pipelines — the opposite, measured by a consumer (Phase 250)

**The paragraph above holds for the pipelines it measured, and not for a pipeline that keeps every
row.** Those three shapes all SHRINK the table: a `Filter`, a `GroupBy`, a `Limit`. A pipeline made
only of row-local steps (`Derive`, `Case`) outputs a whole n-row table, which the refresh still has
to assemble. Its per-row bookkeeping then costs more than the row expression it saves, when that
expression is one `Mul` and one `Ge`.

A downstream spreadsheet-shaped consumer measured it (Phase 250). The sheet is an `orders` source
feeding two table nodes: `lines`, which keeps every row (`Derive amount = qty * price`, then
`Derive big = amount >= threshold`), and `byRegion`, which is `Derive amount` then a `GroupBy` over
five regions. Then cell nodes over both. **Provenance, stated because the numbers depend on it:**
measured by a consumer, on `0.30.0`, in a Debug build, on one Windows 11 ARMv8 laptop (12 logical
processors, .NET 10.0.11). Each figure is the median of three process runs, each the median of five
timed repetitions after a warm-up. A ratio is taken within a run, with its range over the three
runs in brackets. Every timed refresh was checked equal to the full evaluation outside the timed
region, and the footprints confirm the restricted path was taken. Cell and append edits reached
`Incremental` as row-addressed deltas, the shape `ColumnOps.deltaOf` now builds. Column edits
reached it as a column invalidation.

**Each table node alone** — `DataFrame.evalPipelineInEnv` over the node's source, divided by
`Incremental.refresh` over the same source and delta; above 1 the refresh wins:

| rows | edit | `lines` (keeps every row) | `byRegion` (group-by) |
|---|---|---|---|
| 1,000 | cell | 1.02 (1.01 to 1.08) | 1.26 (1.25 to 1.28) |
| 1,000 | column | 0.71 (0.71 to 0.72) | 0.87 (0.85 to 0.89) |
| 1,000 | row append | 1.03 (0.98 to 1.18) | 1.27 (1.26 to 1.31) |
| 10,000 | cell | 0.78 (0.68 to 0.79) | 1.91 (1.83 to 2.08) |
| 10,000 | column | 0.48 (0.34 to 0.63) | 0.81 (0.79 to 0.87) |
| 10,000 | row append | 0.80 (0.76 to 0.82) | 1.65 (1.48 to 1.74) |
| 100,000 | cell | 0.88 (0.77 to 1.04) | 1.09 (1.06 to 1.13) |
| 100,000 | column | 0.67 (0.61 to 0.71) | 0.77 (0.73 to 0.81) |
| 100,000 | row append | 0.78 (0.78 to 0.79) | 1.08 (1.05 to 1.09) |

**The whole sheet** — `Propagation.eval` of every node, divided by the node-level refresh with
`Incremental.refresh` inside each dirty table node; above 1 the incremental path wins:

| rows | cell | column | row append |
|---|---|---|---|
| 1,000 | 1.34 (1.11 to 1.34) | 0.76 (0.76 to 0.82) | 1.09 (1.07 to 1.14) |
| 10,000 | 0.94 (0.91 to 1.45) | 0.37 (0.34 to 0.63) | 0.67 (0.66 to 1.00) |
| 100,000 | 0.79 (0.71 to 0.84) | 0.52 (0.50 to 0.67) | 0.87 (0.81 to 0.91) |

The 10,000-row column edit was the noisiest cell (its full evaluation ranged from 25.5 to 50.4 ms
over the three runs), so read its range as the reading.

**Where full evaluation wins, on this measurement:**

- **A node that keeps every row loses above 1,000 rows, on every edit** (0.48× to 0.88×). At 1,000
  rows it breaks even on a cell or an append edit (1.02×, 1.03×) and loses on a column edit (0.71×).
- **A group-by node wins on cell and append edits at every measured size, and the margin shrinks
  with size**: 1.91× at 10,000 rows on a cell edit, 1.09× at 100,000. It loses on a column edit
  at every size (0.77× to 0.87×).
- **End to end, the sheet's incremental refresh never paid for itself above 1,000 rows.** It lost
  at 10,000 and 100,000 rows on every edit, and on a column edit at every size. At 1,000 rows it
  won only on the cell and append edits.

**So qualify the advice above by the pipeline's shape.** A refresh is cheaper than a full
evaluation for a pipeline that SHRINKS the table. For one that keeps every row, measure before
adopting, and expect a full evaluation to win once the table is past a few thousand rows unless
the row expression is expensive. The 16-level expression in the `Scaling` family is the shape
where the seam wins by an order of magnitude. The `Scaling` family asserts the shrinking shapes and
not this one, because one consumer's sheet is evidence for a qualification, not a rule. The same
consumer measured a `SetColumn` at 100,000 rows at 210 ms to apply and chain, and an `AppendRows` at
38 ms. That op-side cost comes before any evaluation, and it is the input a later phase on the
refresh's O(n) floor should start from.

**Two changes since this measurement, neither re-measured here.** A cell edit reached this sheet as
one row only because the consumer built the delta by hand. `ColumnOps.deltaOf` now builds it: one
row for a cell edit, the changed rows for a column edit. The column edits above went through a
column invalidation instead, which is not what `deltaOf` produces. And `Propagation.evalFromWith`
now carries each table node's incremental state through the driver instead of beside it. That
closes a correctness gap, not a cost gap. The consumer's next measurement cycle is where both
are re-read.

One practical consequence remains. `Incremental.plan` tells you whether a refresh will be restricted;
it does not tell you whether it will be faster than a full evaluation for YOUR pipeline, and on a
table small enough that a full evaluation is already a millisecond the question does not arise.
Measure your own pipeline rather than reading any row above as a rule.

## What it does not do

- **It does not maintain a delta on the OUTPUT.** A refresh returns the new table, not a description
  of how it differs from the last one. A consumer that wants that runs `Delta.diff` over the two
  results — which is a real cost, so prefer it only where a downstream stage genuinely needs it.
- **It does not reason about column relevance.** "This column changed and the pipeline never reads
  it, so the prior result stands" is `DataFrame.evalFrom`'s job (the coarse `Change` vocabulary), and
  `Delta.toChange` bridges to it. The two compose: ask `evalFrom` whether the change matters at all,
  and this seam how much of it to recompute.
- **It does not persist.** `IncrementalEval` is in-memory state a consumer holds between refreshes.
  Losing it costs one full evaluation, never a wrong answer.
- **It does not publish its caches.** From `0.27.0` the state's representation is private and a
  consumer reads it through `Incremental.result` (the table), `Incremental.footprint` (what producing
  it cost), `Incremental.strategy` and `Incremental.plan'` (how the next refresh will be answered),
  `Incremental.source` (the table the next delta must describe the change FROM) and
  `Incremental.pipelineOf` (the pipeline it was built for). The caches were never something to build
  or edit — a hand-built state whose caches disagree with its source is a lie the evaluator cannot
  detect — and publishing their shape made every change to HOW they are keyed a breaking change,
  which is what kept a refresh paying for the whole table for three phases running. There is no wire
  form and nothing to migrate: a state is in-memory and is rebuilt by one `prime`.

## A row-local step reading a cross-row column — the census (Phase 212)

**Read this section before adopting `0.28.1` if you certify your own evaluator against
`IncrementalDelta.laws`.** The corpus grew by ten shapes, every one of them red against the cache
condition `0.26.0` shipped, so a host carrying that condition goes red at this pin. That is the
point of the widening and it is described for adopters under `0.28.1` in `STABILITY.md`.

### What the class is

Phase 208 found a wrong answer in a published release. The per-row cache was keyed on *the delta did
not name this row*, which is not the same statement as *this row's cells have not moved*: a `Window`
recomputes its column over the whole frame it is handed, so a row nobody edited comes out of it with
a different cell whenever another row in its partition moved. A step after the window then reused an
answer to a question that had changed.

The class is therefore **a row-local step (`Filter`, `Derive`, `Project`) reading a column whose
value for row *r* depends on rows other than *r***. Three phases read this code without seeing it,
because the family that exists to see it could not: `IncrementalDelta.laws` runs the shapes its
corpus enumerates, and the corpus had no such shape.

### The census, measured on `0.28.0` before anything was added

Ten of the thirty-eight enumerated shapes carried a `Window`. In **eight** the window was the last
step. In the two that continued (`23`, `37`) the next step was a `GroupBy`. **In none did a row-local
step read a column a window appended.**

Which cross-row steps can even produce a column for a later step to read:

| producer | appends | status before Phase 212 |
|---|---|---|
| partition-global window (`cumulSum`, `rank`) | its output column | **reached by no row-local consumer** |
| bounded-frame window (`lag`) | its output column | **reached by no row-local consumer** |
| maintained group aggregate | the group table's aggregate columns | reached (`7`, `32`, `33`, `36`, `37`) — and **non-discriminating**, see below |
| truncated order (`Limit`) | nothing | **absent by construction** — a `Limit` decides survival and appends no column, so there is nothing for a later step to read |
| join-appended column | the right relation's schema | **absent by construction** — only a *combining* join appends one, and the seam declines combining joins (`JoinNotRowPreserving`), so the pipeline answers through the reference evaluator and the row cache is never consulted |

And the (producer × consumer) matrix over the two producers that remain. Each cell names the shape
that carries it:

| | `Filter` on it | `Derive` over it | `Derive` overwriting it | `Project` renaming it, then read |
|---|---|---|---|---|
| **partition-global window** | `38`, `42`, `45` | `40`, `44` | `39`, `47` | `41` |
| **bounded-frame window** | `39`, `43` | `38`, `45`, `46` | `41`, `47` | `40` |

All eight cells were absent; all eight now have a shape, and **every one of the ten shapes
discriminates**. With the pre-`0.28.0` predicate reintroduced at the two sites Phase 208 changed,
those ten are exactly the pipelines that disagree with the reference evaluator — 22 to 84 samples of
roughly 500 each — and the other thirty-eight stay green. No shape was dropped for failing to
discriminate.

Two cells are worth their own sentence because they are the ones an implementer gets wrong.
A `Project` evaluates nothing, so it cannot itself return a stale answer; what its cell tests is that
the taint **survives a rename**, which is why `40` and `41` read the renamed column with a later
step. And the group-aggregate row of the first table is reached-but-non-discriminating on purpose:
the group table's own stability condition is *this group's aggregates were recomputed*, which is
sound, so those cells cannot carry the defect. Measured rather than assumed — of Phase 208's three
regression pipelines, the `Filter` and the `Derive` go red under the old predicate and the maintained
`GroupBy` stays green.

### The adequacy demand, and what it cost

`IncrementalDelta.demands` gains a third dimension, **`cross-row column read`**, with one verdict per
producer class, conditioned on a restricted refresh like every class beside it. It follows a
`Project`'s rename and a `Derive`'s propagation, because a renamed column is the same column.

Measured over the sweep the suite pins — 2 row bounds × 300 seeds × 100 iterations, 60,000 samples —
the two new verdicts are reached by **8.88%** and **8.93%** of samples, and the adequacy guard fires
on 0 of 600 runs. Ten new pipelines dilute every class that did not grow, so the existing verdicts
were re-measured too:

| refresh class | over 38 shapes | over 48 shapes |
|---|---|---|
| `declined` | 10.45% | 8.23% |
| `row-restricted` | 30.42% | 33.13% |
| `group-restricted` | 16.85% | 15.45% |
| `merged-order-restricted` | 13.87% | 13.35% |
| `window-restricted` | 13.92% | 22.01% |
| `partition-global-window-restricted` | 11.11% | 17.60% |
| `relation-filtered-restricted` | 11.44% | 9.78% |
| `top-n-restricted` | 11.03% | 11.04% |
| `group-tail-restricted` | 8.37% | 8.88% |

Every one clears the 7% floor the suite holds them to. `group-tail-restricted` is the one the
widening would otherwise have pushed under it — a projected **6.62%** on dilution alone — so `42` and
`46` were given a maintained group and a tail, which is the same trade the corpus's note on shapes
`20`–`26` predicts: a thin class rises when the new draws answer it as well, rather than instead.

### A second live defect, found here and FIXED by Phase 215

Adding these shapes surfaced a second wrong answer, on `0.28.0`, in a neighbouring class. Phase 212
reported it and deliberately left it standing — a conformance-corpus phase that quietly patches the
seam is how a finding stops being a finding. **Phase 215 fixed it**, and this section is now the
record of what was wrong, which releases carry it, and how each half was measured.

**A merged order whose sort key read a column a window appended returned the wrong rows.** Phase 208
replaced `not Affected` with `Stable` at the two sites it found — `cellAt` and the join's cached
verdict — and left a third standing: the `WSort` arm built its reusable set from `not w.Affected`, so
a merge reused the cached POSITION of a row whose sort key a window had moved. The fix is that arm's
reusable set reading `w.Stable`, which is the one-token completion of 208's own substitution.

Probed in both directions, at `0.28.0` and again after the fix:

| pipeline | `0.28.0` | after Phase 215 |
|---|---|---|
| `window(rank) > sort(rk) > limit` | **red** | green |
| `window(rank) > derive(d = rk + a) > sort(d) > limit` | **red** | green |
| `window(cumulSum) > sort(run) > limit` | **red** | green |
| any of the three with the `limit` removed | **red** — the order itself is wrong; the cut is not needed | green |
| `derive(d = a + b) > sort(d) > limit` (no window) | green | green |
| `window(rank) > sort(b) > limit` (sort key is a source column) | green | green |
| `sort(b, a) > window(rank)` (shape `21`) | green | green |

So the window was load-bearing and the sort key had to read the column it appended; neither the derive
nor the truncation was required. All six pipelines are the regression case in
`IncrementalRefreshCostTests`, which asserted the *presence* of the defect until this phase and
asserts its absence now; the whole 48-shape family is green at every pinned seed.

#### Which releases answer wrongly — measured against the released packages, not inferred

The merged order (`mergeOrders` and the cached-order reuse it serves) arrived in `0.18.0`, and the
reuse set was keyed on `not Affected` from that release to `0.28.0` inclusive. That span was measured
rather than read off the source: a probe pinned to one published `Fuaran.Core.DataFrame` at a time
ran `window(rank) > sort(rk)` through that package's own incremental seam and compared it with that
same package's reference evaluator.

| release | `window(rank) > sort(rk)` | `window(lag) > sort(prev)` | `window(rank) > sort(b)` — control |
|---|---|---|---|
| `0.16.0`, `0.17.0` | agrees | agrees | agrees |
| `0.18.0` | *not measured — no package published to measure; see below* | | |
| `0.19.0` – `0.28.0` (every released version) | **disagrees** | **disagrees** | agrees |

The two green rows are the probe's falsifier: `0.16.0` and `0.17.0` predate the merged order
entirely, so a probe that reported red there would be measuring something other than this defect.
`0.18.0` carries the same reuse condition but admits **bounded-frame** windows only
(`when DataFrame.windowFrameBounded spec.Fn`), so `window(rank)` is declined there and answers
through the reference evaluator; the bounded-frame column of the table is why the release is still
affected — `window(lag) > sort(prev)` is the shape that reaches it, and it is red from the first
release that can be measured.

**`0.26.0` is NOT where this began.** Phase 212's census, and the successor phase filed from it,
both read the span as "`0.26.0` onward" by analogy with the `cellAt` defect Phase 208 fixed. That is
wrong by seven releases: the two defects are siblings in kind and not in age, because `cellAt`'s
predicate and the sort's are independent pieces of code that happened to be written the same way.

#### Corpus shape `44` sorts on the window's column now, and it discriminates

Phase 212 had to key shape `44`'s order on `b`, a source column, because sorting on `d` was red on
the seam as shipped. Phase 215 moved it to `d`, and measured both directions over 40,000 generated
samples (2 row bounds × 200 seeds × 100 iterations):

| arm | shape `44` drawn | shape `44` not equivalent | any other shape not equivalent |
|---|---|---|---|
| pre-fix (`not Affected`, reintroduced for the measurement) | 842 | **6** | 0 |
| shipped (`Stable`) | 842 | 0 | 0 |

The six are `(bound 9, seed 33, iteration 3, changeFirstA)`, `(9, 40, 36, nullFirstA)`,
`(9, 77, 8, nullFirstA)`, `(9, 114, 35, append)`, `(9, 122, 78, removeFirst)` and
`(5, 144, 61, changeFirstA)`. The first of them is the eight-row table the regression case in
`IncrementalRefreshCostTests` is built from.

**The rate is worth stating rather than rounding away: 6 draws in 842 is 0.7%, and the four seeds the
suite pins (`1`, `7`, `99`, `20260821`) are not among them.** A conformance family names the class
for every host that runs it; it does not promise to reach an instance at any particular seed. That is
the whole reason the fixed eight-row regression case stays in the suite beside the corpus — it
reaches the defect on every run of every seed — and it is the reason a corpus shape is not, by
itself, a regression test.

## Which condition each reuse reads — the per-site audit (Phase 215)

Two sessions found two sites of one substitution and a third was still standing, so this table is the
census that stops a fourth. Every place the walk reuses something the prior evaluation computed is a
row here, with the condition it reads and why that condition is the right one.

The two conditions are **`not Affected`** — "the delta did not name this row" — and **`Stable`** —
"this row's cells are byte-identical to the ones the prior evaluation held for it AT THIS POINT in
the pipeline". They agree at the start of the walk (`Stable` is seeded as `not affected`) and part
company at a `Window`, which clears `Stable` for every row because it recomputes its column over the
whole frame.

The rule the table makes concrete: **anything computed FROM a row's cells may be reused only on
`Stable`.** `not Affected` is sound only where the thing reused is not a function of the row's cells
at all.

| site (`Incremental.fs`) | what it reuses | condition | why that one |
|---|---|---|---|
| `cellAt` | the cell an evaluating step (`Filter` predicate, `Derive` expression) computed for this row | `Stable` | the cell is a function of the row's cells; a window moves them without the delta naming the row. **Fixed by Phase 208** — it read `not Affected` to `0.26.0`. |
| `walk`, `WJoin` arm — `verdictOf` | the cached `Semi`/`Anti` verdict | `Stable` **and** the right relation unmoved | the verdict is a function of the row's key cells *and* of the relation, and the delta describes neither the second nor a window's effect on the first. **Fixed by Phase 208.** |
| `walk`, `WSort` arm — the reusable set | this row's cached POSITION in the merged order | `Stable` | a cached order is a cached answer: it is a function of every row's sort-key cells, which a window moves. **Fixed by Phase 215** — it read `not Affected` to `0.28.0`. |
| `walk`, `WWindow` arm | nothing — it CLEARS `Stable`, for live rows and dead ones alike | — | it is the producer the other rows are about. A dead row's cells are not recomputed, so its cache is cleared rather than refreshed: the conservative reading, and the only one available. |
| `runIncremental`'s row-cache write-back | the prior `Cached` list **as a list**, in place of rebuilding it from `Fresh` | `Stable` **and** the two lists the same length | this one is an IDENTITY claim rather than a reuse of a computation — `Fresh` reversed *is* `Cached` when every step read the cache — so it needs the same condition each of those steps needed, plus the length test for a row that died earlier this time. |
| `groupStep` — the carried group token | this row's group identity from the prior evaluation | `Stable` **and** `Prior >= 0` | the token is a pure function of the key cells. A row the prior evaluation did not reach mints as before, so a carried token is never the only derivation. |
| `groupStep` — `cellsFor`'s `allStable` | a group's cached aggregate cells | every member `Stable`, **and** the ordered member list unchanged, **and** a cached aggregate to reuse | an aggregate is a function of its members' cells, so one unstable member is enough to invalidate it; the ordered-member test is what makes a pure reordering — which `Delta.diff` reports as quiet — not reusable. |
| the group table's own `Work` frame (the maintained-`GroupBy` tail) | the tail's cached cells for a group row | the group's aggregates were not recomputed | sound for the reason a source row's `not Affected` is not: a group row's cells are a function of its members alone, `groupStep` has just recomputed exactly the groups whose members moved, and **no window runs over the group table**. This is the one site where the delta-shaped condition is the right one, and it is right because it is not the delta's statement — it is the group step's. |

**There is no `Affected` field any more.** Once the `WSort` arm moved to `Stable` it had no reader,
and a never-read field whose meaning is the discredited condition is how a fourth site gets written.
What the delta contributes is the seed of `Stable` at construction, and nothing else. The type is
private to the module, so nothing public moved with it.

**The one site that is not in this table is `evalIdx`-keyed positional reading itself** — every cache
above is read by the row's `Prior` slot or by its token, never by its current position, which is what
makes a `Sort` or a `Limit` in the middle of the pipeline safe to walk past. A cache read by the
row's CURRENT index would be wrong at every row of every shape here, and would be caught by the first
corpus draw rather than after three releases.

## Verifying your own adoption

The equivalence family `IncrementalDelta.laws` (in `Fuaran.Core.Conformance`) certifies the seam
itself against the reference evaluator over a generated corpus. A consumer does not need to re-run
it, but the pattern is worth copying for your own pipelines: evaluate both ways and assert equality,
then assert the footprint. The first catches a wrong answer; the second catches the subtler
regression where the answer stays right and the saving quietly disappears.
