# Adopting incremental `Transform` evaluation

A one-page on-ramp for a consumer that already evaluates a `Transform` pipeline with
`DataFrame.evalPipeline` and wants a refresh to cost the rows that changed rather than the rows it
has. Read it alongside the substrate's [`ADOPTION.md`](https://github.com/Fuaran-Core/fuaran-core/blob/main/docs/ADOPTION.md); nothing here replaces the reference evaluator,
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

Where the source was prepared once for many pipelines (`DataFrame.prepare`; `0.34.0`), prime over
it with `Incremental.primeOnPrepared idw pipeline prepared` (or `Incremental.primePrepared` with a
resolver and an env): the state it builds is the state `primeOn` builds over the same table, with
the reference path evaluating over the prepared form instead of unpacking the table again, and the
prepared form held in the state. A refresh takes the new source as a `Table`, exactly as before.

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

What has NOT changed is the floor OF THIS ENTRY POINT, and it is worth stating because it says
where the next saving had to come from. `refresh` is handed the whole new source as a `Table` and
must read every row of it to know what moved, so one pass over the frame is proportional to the
table and nothing reachable from a table removes it. What is proportional to the delta is
everything else: the keyed lookups, the identity derivations and the cache writes.
`IncrementalRefreshCostTests` counts exactly that, clock-free, and holds the shape it replaced to
the opposite result. Phase 268 added the entry point that gets under the floor — `refreshPrepared`,
handed a VERSION whose chunks it can compare with the last one's rather than a table it must read —
and it is measured in [its own section below](#the-chunked-path--a-version-not-a-table-phase-268):
a one-cell edit refreshes one chunk, at 100,000 rows as at 2,048.

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
38 ms. That op-side cost comes before any evaluation, and it was the input Phase 268 started from:
the next section re-measures this node's cell edit through a version rather than a table.

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

### The chunked path — a version, not a table (Phase 268)

The floor above is a property of the ENTRY POINT: `refresh` is handed a `Table`, and a table shares
nothing with the table it replaced, so the only way to learn what moved is to read it. Phase 268
adds the entry point that does not have that floor. A source prepared once (`DataFrame.prepare`,
Phase 267) is now a persistent VERSION: each column a rope of chunks of 1,024 rows, and an edit
through `ColumnOps.applyPrepared` copies the one chunk it lands in and shares every other chunk
with the version before it, by reference. `Incremental.refreshPrepared` is handed the new version,
compares its chunks with the version the state was last evaluated over — one pointer comparison
per chunk per column — evaluates the chunks that moved, and hands back an output that shares every
chunk that did not.

```fsharp
let v0 = DataFrame.prepare source
let state = Incremental.primePrepared resolve env idw pipeline v0 |> ok

// On each edit: the op costs a chunk, the delta costs one row, the refresh costs the chunks that moved.
let v1 = ColumnOps.applyPrepared op v0 |> ok
let delta = ColumnOps.deltaOfPrepared idw v0 op
let next = Incremental.refreshPrepared resolve env idw pipeline state delta v1 |> ok

Incremental.chunksTouched next   // Some 1 for a one-cell edit, at any row count
Incremental.resultPrepared next  // the result as a version, to feed the next node without a boundary
Incremental.result next          // the result as a table, built on first read
```

**What it admits.** A pipeline made only of `Derive`s — every row kept where it is, each row's value
a function of that row — is evaluated chunk by chunk through the reference's own `evalStep`, so the
cells are the reference's cells and the transform law vectors hold them so. Anything else
(`Filter`, `GroupBy`, `Sort`, a join) takes the row-local walk over the version's table from the same
call, exactly as `refresh` would: the entry point is total and its answer always equals the
reference's; only the cost changes with the shape. The delta still decides staleness (a changed
pipeline or env, a moved schema) but never what moved — that is read off the chunks, so a delta
that names more rows than moved costs nothing extra, and `FullRefresh` over a one-chunk edit costs
one chunk. A derived column's TYPE is fixed over the whole rope by the reference's rule (the first
present cell, `StringType` when there is none), because a chunk with no present cell would type
itself differently on its own.

**Measured, on the `lines` node above — `amount = qty * price`, then `big = amount >= threshold` — at
one edited cell.** Release build, .NET 10, one Windows 11 Arm64 machine with other sessions on it;
each figure the median of 21 to 41 timed repetitions after a warm-up, the chunked answer checked
equal to the full evaluation outside every timed region:

| rows | full evaluation | `refresh` (walk, `Table` form) | `refreshPrepared` | edit + delta + `refreshPrepared` | `refreshPrepared` + `result` as a table | chunks touched |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 0.50 ms | 1.52 ms | 0.19 ms | 0.21 ms | 0.31 ms | 1 |
| 10,000 | 4.9 ms | 22.0 ms | 0.10 ms | 0.09 ms | 0.48 ms | 1 |
| 100,000 | 17.8 ms | 367 ms | **0.094 ms** | 0.099 ms | 17.1 ms | 1 |

And the op on its own, at 100,000 rows:

| op | `apply`, `Table` form | `applyPrepared` |
|---|---:|---:|
| `SetCell` | 1.34 ms | 0.009 ms |
| `AppendRows` (10 rows) | 22.7 ms | 0.054 ms |
| `SetColumn` (one cell differs) | 1.79 ms | 3.25 ms |

**Read it in three parts.** The refresh itself is under a tenth of a millisecond at 100,000 rows
and does not grow with the table — it is 1,024 rows evaluated twice, whatever the row count, which
`IncrementalRefreshCostTests` asserts as a count at 2,048 and at 20,000 rows. The edit and the delta
cost what they touch: a `SetCell` copies one chunk (a hundred and fifty times cheaper than rebuilding
the column list), an `AppendRows` copies the partial last chunk of each column (four hundred times
cheaper than appending to every list), and a `SetColumn` compares its cells chunk by chunk to keep
the chunks it did not move, which is a pass over the column either way and here a slower one — the
op carries every cell, so its cost is the column's, and what the comparison buys is the refresh
after it touching two chunks rather than a hundred. And the table boundary is where the remaining
time is: reading `result` back as a `Table` at 100,000 rows costs as much as a full evaluation,
because it builds the three columns the edit moved (the edited one and the two derived) as `Cell`
lists — 300,000 cells — while the full evaluation, whose evaluator is now a fraction of its own
cost, hands the consumer's four untouched lists back and builds two. The columns an edit did not
move ARE handed back as the lists they were (a rope remembers its list once it has built one), so
the boundary costs the moved columns and no others; a node that feeds another node reads
`resultPrepared` and pays no boundary at all. Under one millisecond, then, is the refresh and the
edit; a consumer that reads every cell back as a table each tick has bought the difference between
367 ms and 17 ms, not between 17 and 0.1.

**The walk's figure is worth a sentence of its own.** At 100,000 rows the row-local `refresh` over
this pipeline now costs twenty times the full evaluation it replaces, not the 0.88× the consumer
measured on `0.30.0`: Phases 263 to 267 made the reference evaluator six times faster and left the
walk — one `Work` record, one cached-cell lookup and one fresh row per source row, then a table
assembled from rows — where it was. A row-preserving pipeline over a bare table should use the
full evaluation; the walk earns its keep on the shrinking shapes the `Scaling` family asserts, and
the chunked path is now the answer for the shape the walk lost on.

**The falsifiers, each asserted.** `chunksTouched` is `Some n` only on the chunked path; a version
prepared afresh from a table (`DataFrame.prepare` of the edited table rather than `applyPrepared`
of the edit) shares no chunk and touches every one; a state the walk built has no chunks to share,
so the first chunked refresh over it touches every chunk and the next touches one; and a version
whose chunks are all the prior's touches none and reports `ReusedPrior`. The model behind
`ColumnOps` (`proofs/ColumnOps.fst`) proves the representation: the edit through the rope is the
flat edit `apply` performs, and every chunk before and after the one holding the row is the chunk
it was.

### A table-fed tick — the diff and the refresh against the evaluation they replace (Phase 272)

Everything above prices the REFRESH. A caller that is handed a fresh `Table` each tick — a
server-driven transform over a binding's resolved source, a renderer's binding resolver, the Fable
smoke host — pays more than that: it runs `Delta.diff` against the prior source to learn what moved,
then `Incremental.refreshOn`. Its alternative is not a refresh, it is one full evaluation of the new
source. So the figure that decides whether the seam is worth adopting from a table is **(diff +
refresh) ÷ full evaluation**, and until Phase 272 nothing measured it.

**How it was measured.** Every node of the Phase 262 benchmark corpus, driven through the seam: the
sheet's `lines` and `byRegion` over `orders`, the three `Scaling` pipelines over their table, and the
five shapes (an inner join on a permuted key, a group-by over n/2 distinct keys, a pivot over 50
on-values, a one-partition `CumulSum` window, a two-key sort) over generated tables of the same
build at the same sizes, with an integer `rid` identity column added where the shape's own table
has no unique column. One cell of the middle row is edited (a measure, never the key); the state is
primed on the unedited table; each figure is the best of five batched samples of at least 40 ms,
after two warm-up calls, taken by one program compiled for .NET 10 (Release) and by Fable 5 for node
24, on one Windows 11 Arm64 machine shared with other sessions. The full evaluation is the PLANNED
one (`DataFrame.evalPipelineInEnv`, which plans since Phase 269), because that is what the caller
would otherwise run. Every refresh was checked equal to the full evaluation before it was timed.

**The baseline, and the dense diff.** "Tick" is (diff + refresh) ÷ full. The refresh column is the
row-local walk, unchanged by this phase; the "before" diff is the row-token diff this phase replaced,
the "after" one the dense form it shipped. .NET, Release:

| node | rows | full | refresh | diff, before | tick, before | diff, after | tick, after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `lines` | 1,000 | 0.15 ms | 0.65 ms | 2.96 ms | 24.0× | 0.33 ms | 7.5× |
| `lines` | 20,000 | 3.25 ms | 38.6 ms | 109 ms | 45.5× | 10.4 ms | 13.4× |
| `lines` | 100,000 | 27.8 ms | 436 ms | 634 ms | 38.5× | 62.4 ms | 19.7× |
| `byRegion` | 1,000 | 0.30 ms | 0.51 ms | 3.06 ms | 11.7× | 0.15 ms | 3.1× |
| `byRegion` | 20,000 | 4.40 ms | 23.5 ms | 127 ms | 34.1× | 10.0 ms | 9.0× |
| `byRegion` | 100,000 | 28.8 ms | 271 ms | 685 ms | 33.2× | 63.0 ms | 12.8× |
| filter > groupBy | 1,000 | 0.18 ms | 0.36 ms | 2.58 ms | 16.4× | 0.16 ms | 2.9× |
| filter > groupBy | 20,000 | 5.18 ms | 19.3 ms | 115 ms | 25.9× | 9.53 ms | 5.0× |
| filter > groupBy | 100,000 | 26.2 ms | 163 ms | 679 ms | 32.1× | 62.2 ms | 9.5× |
| filter > sort > limit 10 | 1,000 | 0.16 ms | 0.66 ms | 2.61 ms | 21.1× | 0.16 ms | 2.4× |
| filter > sort > limit 10 | 20,000 | 3.61 ms | 25.4 ms | 115 ms | 38.8× | 8.99 ms | 11.5× |
| filter > sort > limit 10 | 100,000 | 18.6 ms | 209 ms | 663 ms | 47.0× | 61.8 ms | 16.3× |
| filter > groupBy > filter | 1,000 | 0.18 ms | 0.40 ms | 2.38 ms | 15.4× | 0.15 ms | 3.1× |
| filter > groupBy > filter | 20,000 | 4.67 ms | 17.1 ms | 118 ms | 28.9× | 6.81 ms | 3.7× |
| filter > groupBy > filter | 100,000 | 22.1 ms | 160 ms | 702 ms | 39.1× | 64.1 ms | 9.2× |
| inner join (declined) | 1,000 | 0.81 ms | 0.83 ms | 2.19 ms | 3.7× | 0.16 ms | 1.3× |
| inner join (declined) | 20,000 | 42.4 ms | 45.7 ms | 113 ms | 3.8× | 15.9 ms | 1.3× |
| inner join (declined) | 100,000 | 187 ms | 211 ms | 1,048 ms | 6.7× | 67.9 ms | 1.3× |
| group-by, n/2 keys | 1,000 | 0.39 ms | 0.97 ms | 2.56 ms | 9.1× | 0.18 ms | 3.1× |
| group-by, n/2 keys | 20,000 | 13.9 ms | 42.4 ms | 106 ms | 10.7× | 12.3 ms | 3.4× |
| group-by, n/2 keys | 100,000 | 85.5 ms | 255 ms | 594 ms | 9.9× | 98.3 ms | 3.8× |
| pivot, 50 on-values (declined) | 1,000 | 0.50 ms | 0.50 ms | 2.68 ms | 6.3× | 0.19 ms | 1.3× |
| pivot, 50 on-values (declined) | 20,000 | 16.9 ms | 17.6 ms | 115 ms | 7.8× | 10.9 ms | 1.6× |
| pivot, 50 on-values (declined) | 100,000 | 73.3 ms | 76.2 ms | 639 ms | 9.8× | 72.3 ms | 1.7× |
| window `CumulSum` | 1,000 | 0.47 ms | 0.76 ms | 2.20 ms | 6.3× | 0.16 ms | 1.8× |
| window `CumulSum` | 20,000 | 27.3 ms | 45.8 ms | 98.6 ms | 5.3× | 11.4 ms | 1.9× |
| window `CumulSum` | 100,000 | 170 ms | 280 ms | 650 ms | 5.5× | 67.8 ms | 2.1× |
| sort on two keys | 1,000 | 0.33 ms | 0.51 ms | 2.15 ms | 8.2× | 0.17 ms | 1.4× |
| sort on two keys | 20,000 | 11.3 ms | 31.6 ms | 113 ms | 12.8× | 10.8 ms | 4.1× |
| sort on two keys | 100,000 | 76.6 ms | 199 ms | 927 ms | 14.7× | 62.3 ms | 2.6× |

The "after" ticks are each run's own ratio; the machine was loaded and the second run's full
evaluations came in up to 1.5 times slower than the first's, so read the "after" tick as the
post-phase figure rather than dividing the first columns afresh. node 24, the same program through
Fable:

| node | rows | full | refresh | diff, before | tick, before | diff, after | tick, after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `lines` | 1,000 | 1.38 ms | 1.84 ms | 10.0 ms | 8.6× | 0.45 ms | 1.7× |
| `lines` | 20,000 | 31.0 ms | 77.0 ms | 249 ms | 10.5× | 15.3 ms | 2.7× |
| `lines` | 100,000 | 119 ms | 399 ms | 1,285 ms | 14.2× | 52.0 ms | 3.6× |
| `byRegion` | 1,000 | 1.94 ms | 1.69 ms | 8.88 ms | 5.5× | 0.47 ms | 1.2× |
| `byRegion` | 20,000 | 44.0 ms | 42.0 ms | 253 ms | 6.7× | 19.5 ms | 1.4× |
| `byRegion` | 100,000 | 184 ms | 301 ms | 1,304 ms | 8.7× | 70.0 ms | 1.9× |
| filter > groupBy | 1,000 | 2.16 ms | 1.47 ms | 8.37 ms | 4.6× | 0.46 ms | 1.1× |
| filter > groupBy | 20,000 | 35.5 ms | 34.5 ms | 207 ms | 6.8× | 14.5 ms | 1.4× |
| filter > groupBy | 100,000 | 171 ms | 253 ms | 1,138 ms | 8.1× | 49.0 ms | 1.7× |
| filter > sort > limit 10 | 1,000 | 3.13 ms | 2.31 ms | 8.37 ms | 3.4× | 0.44 ms | **0.86×** |
| filter > sort > limit 10 | 20,000 | 70.0 ms | 82.0 ms | 224 ms | 4.4× | 11.5 ms | 1.4× |
| filter > sort > limit 10 | 100,000 | 286 ms | 403 ms | 1,145 ms | 5.4× | 51.0 ms | 1.5× |
| filter > groupBy > filter | 1,000 | 1.84 ms | 1.50 ms | 7.88 ms | 5.1× | 0.45 ms | **0.97×** |
| filter > groupBy > filter | 20,000 | 42.0 ms | 41.0 ms | 242 ms | 6.7× | 21.5 ms | 1.6× |
| filter > groupBy > filter | 100,000 | 175 ms | 217 ms | 1,150 ms | 7.8× | 60.0 ms | 1.7× |
| inner join (declined) | 1,000 | 4.88 ms | 4.88 ms | 6.38 ms | 2.3× | 0.45 ms | 1.1× |
| inner join (declined) | 20,000 | 125 ms | 144 ms | 222 ms | 2.9× | 16.5 ms | 1.2× |
| inner join (declined) | 100,000 | 697 ms | 864 ms | 1,511 ms | 3.4× | 76.0 ms | 1.2× |
| group-by, n/2 keys | 1,000 | 3.06 ms | 3.25 ms | 8.12 ms | 3.7× | 0.48 ms | 1.2× |
| group-by, n/2 keys | 20,000 | 69.0 ms | 87.0 ms | 234 ms | 4.7× | 18.0 ms | 1.6× |
| group-by, n/2 keys | 100,000 | 322 ms | 492 ms | 1,254 ms | 5.4× | 62.0 ms | 1.8× |
| pivot, 50 on-values (declined) | 1,000 | 3.44 ms | 3.50 ms | 9.25 ms | 3.7× | 0.48 ms | 1.1× |
| pivot, 50 on-values (declined) | 20,000 | 68.0 ms | 68.0 ms | 250 ms | 4.7× | 14.5 ms | 1.2× |
| pivot, 50 on-values (declined) | 100,000 | 335 ms | 339 ms | 1,447 ms | 5.3× | 65.0 ms | 1.2× |
| window `CumulSum` | 1,000 | 2.19 ms | 2.00 ms | 7.25 ms | 4.2× | 0.45 ms | 1.1× |
| window `CumulSum` | 20,000 | 45.0 ms | 48.0 ms | 198 ms | 5.5× | 11.0 ms | 1.3× |
| window `CumulSum` | 100,000 | 240 ms | 296 ms | 1,082 ms | 5.7× | 58.0 ms | 1.5× |
| sort on two keys | 1,000 | 2.63 ms | 1.56 ms | 6.38 ms | 3.0× | 0.46 ms | **0.73×** |
| sort on two keys | 20,000 | 89.0 ms | 43.0 ms | 201 ms | 2.7× | 16.8 ms | **0.71×** |
| sort on two keys | 100,000 | 553 ms | 317 ms | 1,233 ms | 2.8× | 57.0 ms | **0.64×** |

**Read the baseline first.** Before this phase no node, at any size, on either host, had a tick
below the full evaluation, and on .NET the median node paid about twenty-five full evaluations per tick
at 100,000 rows. The diff was most of it — four to thirty-six times the evaluation on .NET, one to
eleven on node — and the refresh the rest. On .NET the walk lost to the evaluation on every node at
every size, down to 8 rows (a separate run at 8, 32, 128 and 512 rows found no size where it won);
on node it won on six of the ten nodes at 1,000 rows and on the two-key sort at every size, because
the JavaScript evaluator's sort and hash grouping cost more relative to the walk's bookkeeping than
.NET's do.

**The outcome is A — by the phase's own definition, and only on node.** After the dense diff the
table-fed tick is CHEAPER than the evaluation on node for the two-key sort at every size (0.64× to
0.73×) and for the top-10 board and the group tail at 1,000 rows. On .NET it is cheaper nowhere.
So the entry point stays, and no deprecation is proposed.

**The 1.5× bound the phase set out to enforce cannot be met from this entry point on .NET, and that
is a finding about the signature, not about the implementation.** A diff by identity must ask the
witness for every row's key in BOTH tables — `RowIdentity.KeyString` is opaque, and it is the only
thing that can say what a key is — and must prove each table's keys unique, which is a hash pass.
Measured on its own, in Release, over `orders` (an integer key, so the cheapest `cellToken` there
is): minting one table's keys cost 2.7 ms at 20,000 rows and 20.7 ms at 100,000, and minting and
indexing it 5.4 ms and 21.9 ms, against a full evaluation of `lines` of 3.4 ms and 25 to 37 ms. The
diff's floor — both tables keyed, one indexed — is therefore 1.2 to 2.4 full evaluations of the
cheap nodes before the refresh has done anything, and the dense diff sits at about twice its
keying, which is where the new family holds it. The refresh adds its own floor on top: `tokensOf`
mints one identity token per source row (`"k:" + KeyString`), 5.5 ms at 20,000 rows and 27 ms at
100,000, already about one full evaluation, and the walk then builds one `Work` record per row. No
rearrangement inside `Delta.fs` and `Incremental.fs` reaches 1.5× for `lines` at 100,000 rows on
.NET while the caller computes the diff itself and hands the refresh a table.

**What this phase therefore did, and did not do.**

- **Route (a), on the diff: done.** `Delta.diff` is dense (see its doc comment): each table keyed
  once into an array, a row that sits where it sat paired by one string comparison, "changed"
  decided cell by cell under token equality, a column whose cell list is shared between the two
  tables not read for those rows. Ten times cheaper at 100,000 rows on .NET (634 ms to 62 ms) and
  twenty-five times on node (1,285 ms to 52 ms); the per-tick cost of every table-fed caller falls
  by that much with no change on its side. The answer is the old one: the suite holds the dense diff
  equal to the row-token diff it replaced, refusals and their order included, over 4,000 drawn pairs
  that reach moved rows, in-place edits, shared column lists, ragged columns, `-0.0` and `NaN` cells
  and both refusals, under a single-column and a composite witness.
- **Route (a), on the walk: not attempted.** Its floor (`tokensOf`, above) is already about one
  evaluation on .NET, so making the rest of the walk dense would move `lines` at 100,000 rows from
  about seventeen evaluations to something above two, and could not reach the bound. It is worth
  doing for its own sake, and it is a rewrite of the walk's row-at-a-time `Work` frame into
  columns, which is a phase of its own.
- **Route (b), degrading by plan: not taken, and left for a decision.** Three findings stand against
  it. Degrading every table-fed refresh changes what `refresh` REPORTS as well as what it costs:
  every restricted footprint becomes `FullRecompute`, which moves the incremental-recompute corpus's
  control vector (whose footprints must reproduce exactly), breaks the conformance family's law that
  an incrementalisable pipeline under a well-formed identity delta is not answered by a full
  evaluation, and empties the refresh classes its sample-adequacy demands require. It would make
  node callers SLOWER on the shapes where the walk wins there, since no static rule is
  host-specific. And it still would not meet the bound on .NET, because the diff's floor alone
  exceeds it. A degrade that fires only above a row count would sidestep the first finding without
  resting on any measurement (the walk loses on .NET at 8 rows too), so it was not written either.
  No degrade reason was added, so the decline list is unchanged.
- **The loss bounds** `cheapRefreshLossBound` and `topNRefreshLossBound` in `ScalingTests.fs` stand
  as they were: they bound the refresh, and nothing here moved the refresh. (Phase 274 retired both
  into the tick family: see "Columnar refresh bookkeeping" below.)

**The invariant the new family enforces.** `Scaling`'s "Delta.diff costs what keying the two tables
costs" holds the diff to at most **three times its floor** — the minting of both tables' keys
through the same witness — at 1,000 and 20,000 rows, best of five, no absolute time. Held against
the floor rather than the evaluator, it cannot need loosening when the evaluator gets faster, which
is the failure this phase was cut to stop. It was red on the pre-phase tree by a factor of eighteen
(the row-token diff at 53 times its floor: 154.2 ms against 2.89 ms at 20,000 rows, Debug) and is
green at 1.7 to 2.0 times after. Beside it, "the table-fed tick" prints (diff + refresh) ÷ full for
the three `Scaling` pipelines on every gate run, unasserted, so the counter-example to the 1.5×
claim travels with the claim the family does make. (Phase 282 moved that bound from the clock to
the allocator and tightened it to two times: see "The gate measures work, not the machine" below.)

### A tick mints each row's key once (Phase 273)

Phase 272 found that a diff by identity cannot avoid asking the witness for every row's key, and
measured that minting as a floor of about one full evaluation per table on the cheap nodes. It did not
ask how many times one tick paid that floor. The answer was **three**. `Delta.diff prior next` keyed
both tables, then the refresh keyed `next` again. The state had already keyed `prior` when it last
evaluated it, and `Delta.diff` had just keyed `next`.

**The baseline, counted rather than timed.** The instrument is a witness that counts its `KeyString`
calls, so its figures are exact and identical on every host and under any load. The probe primes
over a table, then runs two ticks on each corpus node: a one-row edit, `Delta.diff` against the prior
source, and the refresh over the new one. On the pre-phase tree it gave the same count at 1,000 and at
100,000 rows, on .NET and on node:

| node | prime | tick: diff | tick: refresh | keys per row per tick |
|---|---:|---:|---:|---:|
| lines, byRegion, filter > groupBy, filter > sort > limit, filter > groupBy > filter, group-by high-card, window CumulSum, sort two keys | n | 2n | n | **3** |
| inner join, pivot (the plan declines both, so the refresh evaluates in full and keys nothing) | 0 | 2n | 0 | **2** |

**What changed.** `Delta.diff` now remembers the keys it mints. Each table's keys are held in an
internal `KeyedIndex`: the keys in row order, already proved unique, and a key-to-row index that is
built the first time something looks a key up. The index is stored against the table object, and
the `TableDelta` the diff returns carries it for the table it diffed into. The incremental seam does
the same for every source it keys, so a state still knows the keys of the source it last evaluated.
A tick then works like this:

- the diff reads `prior`'s keys back instead of minting them, because `prior` is the source the
  state last keyed or the previous tick's `next`;
- the diff mints `next`'s keys once;
- the refresh takes `next`'s keys from the delta. It does not mint them, and it does not check
  their uniqueness again. A row that has not moved gets the prior evaluation's token instance back
  by comparing the token's key part in place, so it allocates nothing.

After the phase the tick mints **1** key per row on every node on both hosts, from the first tick
onwards. One exception: when the plan declines a pipeline (join, pivot), the prime keys nothing, so
that state's FIRST tick still keys `prior` in the diff (2 per row). Every tick after it keys 1 per
row, because the diff remembered the table that becomes the next `prior`.

**What the index is not.** It is internal and opaque. It is not a field of any public type. It is held
in two `ConditionalWeakTable`s, one keyed by the table object and one by the delta's `RowSetDelta`
record. On .NET these tables are ephemerons; Fable compiles them to a JavaScript `WeakMap`. An entry
lasts exactly as long as the table or delta it describes, and it is keyed by object identity, never
by value. So:

- a delta's equality and its wire form are unchanged, byte for byte. `IncrementalDelta` and the
  `DataFrame.Conformance` law family see the same deltas they always did, and their control vector
  does not move;
- only the record that `Delta.diff` returned carries keys. A delta that was hand-built, composed,
  normalised, decoded from its wire or produced by the ordinal diff carries none. The same goes for
  `FullRefresh`, and for the diff's delta when it is handed a table object other than the one it
  was diffed into. In all these cases the refresh mints exactly as it did before, and it returns the
  same answer and the same footprint;
- reuse is licensed by `RowIdentity.Scheme`. Keys remembered under one scheme are never read back
  under another. Two witnesses that share a scheme must key every table identically, which is what
  a scheme naming the keying rule already meant (`Delta.compose` combines deltas' keys on the same
  promise). The `RowIdentity` doc comment now says so.

The suite holds all of this:

- "a tick mints each row's key once" counts one key per row on each of three ticks, for a row-local,
  a maintained-group and a top-N pipeline. It is red on the pre-phase tree: the tick-1 diff alone
  minted 2n. The same family checks every carrier-less delta shape listed above, a table object
  that differs from the one diffed into, the ordinal diff and a second scheme over the same tables.
  Each one mints as before and answers as before.
- The 4,000-pair dense-diff equivalence now diffs each pair twice more with the keys remembered:
  once with both sides known and once with only `before` known. It holds both answers equal to the
  row-token model.

**The Phase 272 family, re-read.** Before this phase, the "Delta.diff costs what keying the two
tables costs" case in `ScalingTests` diffed the same two table objects on every sample. After it,
every sample after the first would have read both tables' keys back. The case would then have
timed a diff that mints nothing and held it against a floor it no longer pays: green, and vacuous
(x0.62 in the first run after the change). Its samples now diff fresh table objects over the same
columns, which is the cold diff the floor describes. It stays green at 1.7 to 1.9 times its floor,
the figure it held after Phase 272. The unasserted "table-fed tick" case now times the caller's
tick: the state's own prior source against a fresh `next` per sample. Neither threshold moved.

**The tick on the clock, after.** The probe is Phase 272's (Release; best of five batched samples of
at least 40 ms; a one-row edit on every Phase 262 corpus node). It now reports two ticks in the same
run:

- **three mints** reproduces the pre-phase tick's work: a cold diff of two fresh tables, then the
  refresh handed the normalised delta, which carries no keys;
- **one mint** is the caller's tick after this phase: the prior source the state last saw, against
  a fresh `next`.

"Pre-phase tick" is the same probe run on the pre-phase tree earlier in the session, so compare it
to the other two columns loosely. Ratios are only compared within one run. The machine was heavily
shared during every run: the full evaluation of `lines` at 20,000 rows read anywhere from 4 to 16 ms
across runs. A second complete .NET run agreed in direction but not in detail. Treat single cells as
noisy and the columns as the result.

.NET 10:

| node | rows | full (ms) | pre-phase tick ÷ full | three mints ÷ full | one mint ÷ full |
|---|---:|---:|---:|---:|---:|
| lines | 1,000 | 0.175 | 6.10× | 7.31× | 4.92× |
| lines | 20,000 | 6.025 | 14.07× | 14.82× | 12.40× |
| lines | 100,000 | 35.172 | 12.64× | 15.55× | 15.60× |
| byRegion | 1,000 | 0.391 | 3.06× | 2.12× | 1.54× |
| byRegion | 20,000 | 4.989 | 6.96× | 11.10× | 6.39× |
| byRegion | 100,000 | 36.938 | 13.68× | 11.34× | 9.29× |
| filter-groupby | 1,000 | 0.232 | 2.95× | 3.41× | 2.50× |
| filter-groupby | 20,000 | 6.389 | 5.74× | 7.00× | 4.26× |
| filter-groupby | 100,000 | 31.301 | 9.96× | 13.19× | 7.58× |
| filter-sort-limit | 1,000 | 0.190 | 5.16× | 5.97× | 5.50× |
| filter-sort-limit | 20,000 | 5.652 | 14.39× | 11.81× | 9.14× |
| filter-sort-limit | 100,000 | 25.852 | 14.58× | 17.25× | 15.41× |
| filter-groupby-filter | 1,000 | 0.438 | 3.00× | 1.85× | 1.45× |
| filter-groupby-filter | 20,000 | 7.174 | 4.60× | 8.06× | 5.22× |
| filter-groupby-filter | 100,000 | 39.180 | 12.54× | 11.77× | 6.11× |
| inner join | 1,000 | 1.426 | 1.11× | 0.99× | 1.01× |
| inner join | 20,000 | 67.211 | 1.11× | 1.38× | 0.97× |
| inner join | 100,000 | 324.094 | 1.34× | 1.16× | 0.91× |
| group-by high-card | 1,000 | 0.553 | 3.00× | 3.49× | 2.70× |
| group-by high-card | 20,000 | 24.281 | 2.89× | 5.76× | 2.77× |
| group-by high-card | 100,000 | 171.469 | 3.48× | 2.73× | 2.25× |
| pivot | 1,000 | 0.792 | 1.31× | 1.10× | 1.61× |
| pivot | 20,000 | 21.285 | 1.83× | 1.84× | 1.30× |
| pivot | 100,000 | 115.977 | 1.73× | 4.14× | 1.19× |
| window CumulSum | 1,000 | 0.645 | 2.06× | 1.77× | 2.29× |
| window CumulSum | 20,000 | 37.008 | 1.98× | 2.34× | 2.27× |
| window CumulSum | 100,000 | 214.289 | 2.25× | 2.16× | 1.61× |
| sort two keys | 1,000 | 0.394 | 1.98× | 2.20× | 1.66× |
| sort two keys | 20,000 | 12.488 | 3.35× | 4.40× | 2.34× |
| sort two keys | 100,000 | 98.820 | 3.25× | 3.06× | 2.59× |

Node 24 via Fable 5:

| node | rows | full (ms) | pre-phase tick ÷ full | three mints ÷ full | one mint ÷ full |
|---|---:|---:|---:|---:|---:|
| lines | 1,000 | 1.438 | 1.73× | 1.74× | 1.56× |
| lines | 20,000 | 28.500 | 2.50× | 2.81× | 2.53× |
| lines | 100,000 | 135.000 | 3.91× | 4.53× | 4.13× |
| byRegion | 1,000 | 2.812 | 1.07× | 1.16× | 1.22× |
| byRegion | 20,000 | 60.008 | 1.75× | 1.75× | 1.33× |
| byRegion | 100,000 | 274.992 | 1.82× | 1.72× | 1.24× |
| filter-groupby | 1,000 | 1.875 | 1.08× | 1.08× | 0.92× |
| filter-groupby | 20,000 | 41.000 | 1.31× | 1.39× | 1.12× |
| filter-groupby | 100,000 | 175.000 | 1.76× | 1.74× | 1.44× |
| filter-sort-limit | 1,000 | 3.125 | 0.85× | 0.90× | 0.78× |
| filter-sort-limit | 20,000 | 60.992 | 1.55× | 1.30× | 1.28× |
| filter-sort-limit | 100,000 | 302.000 | 1.61× | 1.49× | 1.43× |
| filter-groupby-filter | 1,000 | 1.938 | 0.97× | 1.06× | 0.92× |
| filter-groupby-filter | 20,000 | 39.984 | 1.43× | 1.30× | 1.00× |
| filter-groupby-filter | 100,000 | 182.000 | 1.61× | 1.96× | 1.91× |
| inner join | 1,000 | 8.000 | 1.10× | 0.95× | 0.84× |
| inner join | 20,000 | 214.000 | 1.18× | 0.93× | 0.84× |
| inner join | 100,000 | 834.000 | 1.36× | 1.11× | 1.64× |
| group-by high-card | 1,000 | 4.000 | 1.13× | 1.13× | 1.13× |
| group-by high-card | 20,000 | 115.992 | 1.51× | 1.56× | 1.07× |
| group-by high-card | 100,000 | 456.000 | 1.95× | 1.57× | 1.54× |
| pivot | 1,000 | 4.498 | 1.03× | 1.11× | 1.00× |
| pivot | 20,000 | 88.016 | 1.22× | 1.66× | 1.27× |
| pivot | 100,000 | 395.000 | 0.86× | 1.05× | 0.97× |
| window CumulSum | 1,000 | 2.875 | 1.57× | 1.22× | 0.96× |
| window CumulSum | 20,000 | 51.008 | 1.29× | 1.29× | 1.06× |
| window CumulSum | 100,000 | 395.992 | 1.42× | 2.28× | 1.74× |
| sort two keys | 1,000 | 4.248 | 0.69× | 2.77× | 0.62× |
| sort two keys | 20,000 | 137.000 | 0.64× | 0.74× | 0.92× |
| sort two keys | 100,000 | 994.992 | 1.42× | 1.23× | 0.82× |

**Reading it.** The removed work is two mintings of a table's keys per tick, plus one uniqueness
pass. Phase 272 measured that at about 5 ms at 20,000 rows and 40 ms at 100,000 on .NET, about one
full evaluation of the cheap nodes. The tables show that much. On .NET the one-mint tick sits
below the three-mint tick on most cheap-node cells: at 20,000 rows `byRegion` 6.4× against 11.1×,
filter > groupBy 4.3× against 7.0×, the top-10 board 9.1× against 11.8×. Where the remaining cost
is the refresh walk, which is large, the drop reads as small: `lines` and the top-10 board at
100,000 rows. Isolated in the same probe, `Delta.diff` against a known prior costs between a quarter and
two thirds of the cold diff on every node at 20,000 and 100,000 rows, typically about half.

This phase does not reach the 1.5× bound. On .NET, what is left of the tick is the one minting that
cannot be removed and the row walk. Phase 274 carries that bar.

**What the tick still costs.** One `KeyString` per row of the new table per tick. That is the floor
for this signature: a caller that hands in a whole new table has not said which rows it touched, and
only the witness can say what a key is.

### The gate measures work, not the machine (Phase 282)

**The rule.** A new performance claim in this suite is a COUNT if a count exists, and a clock
assertion only in the isolated clock leg. A count is exact and indifferent to load: the keys a
witness mints, the chunks a refresh touches, the rows the footprint evaluated, the bytes a call
allocates on its own thread (`ScalingTests.allocatedBytes`). The clock is for a claim that is about
time, and there it runs alone, estimated with `bestMs`, and red only if red on three attempts.

**Why.** On 2026-09-27 the gate went red five times with no regression behind any of them. The timing
cases ran inside the whole suite, in one process with every parallel test list, on a machine other
sessions shared. The same flake reproduced on demand: the pre-phase tree under a CPU burner on every
core went red on two `Scaling` cases, "a restricted refresh beats the full evaluation" (one comparison,
refresh 69.4 ms against a full 12.4 ms, 5.6 times against the bound of 4) and the group-tail case.
Load inflated the refresh, which allocates, more than the full pass, so the ratio did not cancel.

**How the leg runs.** `ScalingTests.clockTests` has no `[<Tests>]` attribute, so the main suite
cannot discover it. It is excluded by construction, not by a filter. `verify.ps1` runs it after the
main suite, in its own process:

```
dotnet run --project tests/Fuaran.Core.Compute.Tests --no-build -- --clock-leg
```

The leg raises its own scheduling priority to above normal (on a host that refuses, it says so and
runs anyway). Each case gets three attempts and prints every one. The entry point counts the cases
whose bodies ran and fails the leg (exit 2) on fewer than `clockInventory`: 0 of 14 with a filter that
matches nothing, 1 of 14 with one that matches one case. A main-suite case pins `clockInventory`
against the list's length, so neither moves alone.

**The inventory.** Seventeen wall-clock assertion sites in `ScalingTests.fs` (one of them asserted at
two sizes) and one in `PlanTests.fs`, not the 21 and 2 the phase was written against. The
Phase 207 limit-step case was already counted (element visits), and "the table-fed tick" asserted no
time. It printed figures and moved to the leg only so that the main suite ran no timing work.
(Phase 274 made it assert; the rows below carry that phase's changes.)

| Case | Claim | Class | Runs in |
|---|---|---|---|
| the reference evaluator is linear | complexity; guards a per-row list walk | CLOCK — no counter sees the walk | leg |
| `Delta.diff` is linear | complexity; guards a per-row list walk | CLOCK — same | leg |
| the incremental refresh is linear | complexity of prime + diff + refresh | CLOCK — same | leg |
| a restricted refresh beats the full evaluation | time: the win (the loss bound retired by Phase 274) | CLOCK | leg |
| a top-N refresh is linear | complexity | CLOCK | leg |
| a top-N refresh beats the full evaluation | time: the win (the loss bound retired by Phase 274) | CLOCK | leg |
| a group-tail refresh is linear | complexity | CLOCK | leg |
| a group-tail refresh against the full evaluation | time: the win (the loss bound retired by Phase 274) | CLOCK | leg |
| a step's cost does not depend on which column | time ratio, guards a per-row name lookup | CLOCK — no counter sees the lookup | leg |
| a join / a pivot / a group-by over n keys / a distinct is linear | complexity; guard quadratic scans | CLOCK — same | leg |
| the table-fed tick at most 1.5× the full evaluation (Phase 274; 1,000 / 20,000 / 100,000 rows) | time: the seam costs no more than re-running | CLOCK; its countable part is the Phase 274 row below | leg |
| the table-fed tick on every Phase 262 corpus node at most 1.5× the full evaluation (Phase 283; 1,000 / 20,000 / 100,000 rows) | time: the seam costs no more than re-running, on every corpus shape | CLOCK; its countable part is the Phase 283 `KeyString` count | leg |
| `Delta.diff` costs what keying costs (1,000 and 20,000 rows) | work: the keying and a constant | COUNTABLE — keys minted, bytes allocated | main suite |
| `Filter > Sort > Limit 10`: the fused pair against the full sort (`PlanTests`) | work: the top-n does less than the sort | COUNTABLE — bytes allocated | main suite |
| the top-N step is a single pass (Phase 207) | work | already counted (visits) | main suite |
| a one-row refresh allocates a bounded few words per source row (Phase 274) | work: the bookkeeping per row | COUNTABLE — bytes allocated | main suite |

Fourteen cases in the leg (fifteen since Phase 283 added the corpus tick case). The rest of the premise needed correcting too. The seam's counters see
the work a refresh does: rows evaluated, chunks touched (Phase 268's regression, an untouched chunk
evaluated again, is already a count in `IncrementalRefreshCostTests`), and keys minted. None of them
sees a walk inside the evaluator or the diff, and allocation does not either, because walking a list
allocates nothing. Measured: a per-row `List.item` walk injected into `Delta.diff`'s row loop left
the counted diff case green, with 2n keys and 1.50 times the keying's bytes, the same as without it.
The same injection scored 252 to 451 on the clock. So every linearity case stays on the clock, and
only the two work claims whose regression a count can see were converted.

**The conversions, each shown red by an injected regression** (the logs are kept with the phase record):

- `Delta.diff` against its floor now asserts the key count (exactly one per row per table) and
  the diff's allocation against the keying's, at most `diffFloorAllocBound` = 2 times. Measured, 1.50 at
  20,000 rows and 1.52 at 1,000, where the clock read 1.8 to 2.1 against its bound of 3. A second
  minting per row (Phase 273's regression) gives 3n keys and is red, although the clock scored it
  at 2.14 and would have passed it. A token string minted per cell (Phase 272's regression) is
  3.23 times the floor's bytes and red. Bound tightened from 3 to 2.
- The fused top-n in `PlanTests` allocates 579 KB against the full sort's 11.2 MB (19.3 times), and
  must allocate at least four times fewer bytes. A top-n that sorts everything allocates 9.4 MB and
  is red, while its clock (33.4 ms against 38.8 ms) would have passed the old `planned < written`.
  That case also used a mean of five; its printed clock figures now use `bestMs`.

**The leg still catches a regression.** With the per-row walk injected into the diff, four leg cases
(the diff, the refresh, the top-N refresh and the group-tail refresh, all linear) were red on all three
attempts, both quiet and under the full-core burner. They scored 144 to 451 against the bound of 100.

**The acceptance under load.** The patched gate's two test stages, run five times back to back under
the burner (one busy process per logical core, 8): main suite 436 of 436 green each time, clock leg
14 of 14 green each time, no second attempt needed. Leg figures under that load: `Filter > GroupBy`
refresh 37.7 to 41.3 ms against a full 11.0 to 11.7 ms (3.4 to 3.6 times against the bound of 4), and
top-N 54.4 to 57.6 against 16.0 to 18.4 ms (3.1 to 3.6 against 5). No threshold was loosened.
`cheapRefreshLossBound` and `topNRefreshLossBound` moved to the leg unchanged, and Phase 274 owns
them. The margin under the first one is thin: quiet, it reads about 3.3. (Phase 274 retired both into
the tick family; the refresh they bounded now reads about 0.6 of the full evaluation.)

### Columnar refresh bookkeeping, and the tick held at 1.5× (Phase 274)

**The verdict first.** After Phase 273 a tick minted each key once, and what was left of the refresh's
cost was its own bookkeeping. This phase rewrote that bookkeeping over columns. The refresh of a
one-row edit went from 3 to 15 full evaluations on the cheap corpus nodes on .NET to 0.5 to 0.9, and
the table-fed tick (diff + refresh) went from 2.4 to 16.9 to about 1 to 2.4. On **node**, every
corpus node at every size now ticks at **1.25× the full evaluation or less**. On **.NET, in a Release
build, the 1.5× bar is still not met** on the cheapest nodes at 20,000 and 100,000 rows: `lines` (1.4
to 2.2×), `filter > groupBy` (1.4 to 1.8×) and `filter > sort > limit` (1.7 to 2.4×), with
`byRegion`, `filter > groupBy > filter` and the `CumulSum` window at 1.4 to 1.5×, inside the noise of
the bar. What stops them is the diff, not the refresh: `Delta.diff` alone costs **0.9 to 1.3 full
evaluations** of those nodes in Release, because it must mint one `KeyString` per row of the new
table and pair the rows. A refresh that cost nothing at all would still leave `lines` at 1.27× at
20,000 rows in the run below. So the shard's stop rule applies, and **what to do about those shapes is
an operator decision**, recorded as open below. The bar was not loosened.

**What the gate asserts.** "The table-fed tick costs at most 1.5 times the full evaluation it
replaces" (`ScalingTests`, clock leg) holds the tick of the three one-comparison `Scaling` pipelines
(`filter > groupBy`, the top-10 board, the group tail) at 1,000, 20,000 and 100,000 rows, best of
five, with the ratio taken within one run. In the gate's own Debug build it reads 0.59 to 0.95 at
1,000 and 20,000 rows and 0.72 to 1.24 at 100,000 rows. It was red on the pre-phase tree at 1.70×
(the first cell, 1,000 rows, on all three attempts). It replaces `cheapRefreshLossBound` (4.0) and
`topNRefreshLossBound` (5.0). A tick is the refresh plus the diff, so a tick within 1.5 is a refresh
within 1.5: tighter than either bound, and it does not move when the evaluator gets faster. Neither
bound is left as a threshold of its own. The three "beats the full evaluation" cases keep their
costly-expression assertions and still print the one-comparison figures.

Where the claim is countable, it is counted in the main suite. "A one-row refresh allocates a
bounded few words per source row" (`IncrementalRefreshCostTests`) holds the refresh's allocation to
at most 200 bytes per source row at 20,000 rows, on a row-local, a maintained-group, a top-N and a
group-tail pipeline. It measures 65 to 98 bytes per row. On the pre-phase tree the row-local refresh
allocated 896 bytes per row, so the case was red by four and a half times. Two correctness cases sit
beside it: every pipeline refreshed after 1, 5, 40 and all rows moved and back again, and a ragged
source column (see below).

**What changed.** The walk held one `Work` record per source row. Every step rebuilt the records,
every `Derive` copied every row's array to append one cell, and the source was transposed into rows
and the result back into columns. Now:

- **The frame is column-major.** Each column is an array indexed by *slot* (the row's index in the
  frame the walk started from), and the rows alive at a step are an `int[]` of slots in the
  reference frame's order. `Filter` and `Limit` write a new order (a `Limit` is one array slice),
  `Sort` permutes it, `Project` permutes the column arrays, and `Derive` adds one array: the step's
  own cells. The cells a step evaluated are the cache the next refresh reads (`StepCells`, one array
  per evaluating step, `null` where a row did not reach the step), so the per-row `Cell list` cache
  and its rebuild are gone.
- **Source columns are unpacked lazily.** A column no step reads (an identity or a label carried to
  the result) is never unpacked. When the slot order is still every row in order, the result hands
  back the consumer's own `Cell list`: the same move the reference frame's `Origins` make. The
  length is checked, so a ragged column is padded with `Null` exactly as `RowAccess.columns` pads it
  and is never handed back.
- **A re-evaluated row fills only the columns its expression reads** into one scratch array per step,
  and is evaluated by the reference's own `DataFrame.evalResolved`.
- **A sort merges moved rows by bisection.** Each moved row finds its place in the held order by
  binary search under the same comparator and arrival tiebreak, and the runs in between are copied
  whole: O(m log n) comparisons for m moved rows, where the list merge compared up to n times to place
  one. The held orders are slot arrays, and the reuse test is one lockstep pass that allocates nothing.
- **The maintained group-by's caches are positional.** Each row carries its group's index rather than
  its token, and members and aggregates are arrays aligned with the group order. That removes a
  persistent `Map.add` per group per refresh, which cost 2.5 to 3 full evaluations on the
  high-cardinality group-by (n/2 groups) and now costs 0.5 to 0.75.

**What did not change.** Every footprint and every degrade reason. `Stable` is still the condition
at every reuse site (Phases 208 and 215), and `Window` still clears it for every row. Cached orders
are still reused only for rows whose relative arrival has not moved. Groups are still reused only
when every member is stable and the ordered member list is identical. The incremental law family,
the `IncrementalDelta` corpus and its control vector, and every other case in the suite pass
unchanged. The state is a private record, so no public signature moved, and the API baseline is
byte-identical.

**The baseline and the result, both hosts.** Measured with Phase 272's probe: every Phase 262 corpus
node, a one-row edit, and the best of five batched samples of at least 40 ms. The full evaluation is
the planned one (`DataFrame.evalPipelineInEnv`). "Diff" is `Delta.diff` against the prior source the
state last saw, into a fresh table, so it mints the new table's keys once. "Refresh" is the refresh
handed that diff's delta. "Tick" is the two together, timed as one. Before is the post-273 tree and
after is this phase's, each in one run. The machine was shared, so read ratios within a row, not
milliseconds across tables. Cells above 1.5× are in bold.

.NET 10 (Release):

| node | rows | before: full (ms) | before: diff ÷ full | before: refresh ÷ full | before: tick ÷ full | after: full (ms) | after: diff ÷ full | after: refresh ÷ full | after: tick ÷ full |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| lines | 1,000 | 0.187 | 0.77× | 2.98× | 3.43× | 0.172 | 0.37× | 0.49× | 0.84× |
| lines | 20,000 | 3.255 | 1.46× | 11.07× | 11.78× | 3.215 | 1.27× | 0.89× | **2.18×** |
| lines | 100,000 | 25.559 | 0.99× | 14.62× | 16.90× | 20.320 | 0.91× | 0.65× | **1.55×** |
| byRegion | 1,000 | 0.171 | 0.43× | 2.35× | 3.30× | 0.152 | 0.43× | 0.57× | 1.06× |
| byRegion | 20,000 | 4.094 | 1.11× | 5.08× | 5.70× | 4.787 | 0.89× | 0.51× | 1.43× |
| byRegion | 100,000 | 23.320 | 1.00× | 10.99× | 10.86× | 21.172 | 0.99× | 0.49× | 1.46× |
| filter-groupby | 1,000 | 0.197 | 0.37× | 1.81× | 2.36× | 0.154 | 0.42× | 0.59× | 1.18× |
| filter-groupby | 20,000 | 5.885 | 0.90× | 3.36× | 4.43× | 7.456 | 0.47× | 0.27× | 0.82× |
| filter-groupby | 100,000 | 23.188 | 0.96× | 6.26× | 7.21× | 21.891 | 0.75× | 0.57× | **1.64×** |
| filter-sort-limit | 1,000 | 0.140 | 0.52× | 4.22× | 4.86× | 0.134 | 0.49× | 0.67× | 1.22× |
| filter-sort-limit | 20,000 | 3.573 | 0.83× | 7.10× | 9.92× | 2.961 | 1.26× | 0.78× | **2.38×** |
| filter-sort-limit | 100,000 | 17.996 | 1.20× | 10.63× | 12.81× | 16.822 | 1.16× | 0.75× | **1.70×** |
| filter-groupby-filter | 1,000 | 0.168 | 0.43× | 2.06× | 2.81× | 0.152 | 0.42× | 0.60× | 1.14× |
| filter-groupby-filter | 20,000 | 7.744 | 0.51× | 1.83× | 2.69× | 4.796 | 0.81× | 0.70× | 1.41× |
| filter-groupby-filter | 100,000 | 22.531 | 1.11× | 8.40× | 8.36× | 20.867 | 1.08× | 0.61× | 1.43× |
| inner join | 1,000 | 0.893 | 0.09× | 1.00× | 1.14× | 0.724 | 0.09× | 1.06× | 1.23× |
| inner join | 20,000 | 42.703 | 0.08× | 0.96× | 1.26× | 37.453 | 0.10× | 1.05× | 1.12× |
| inner join | 100,000 | 200.742 | 0.12× | 0.96× | 1.14× | 173.016 | 0.11× | 0.98× | 1.09× |
| group-by high-card | 1,000 | 0.355 | 0.20× | 2.46× | 2.64× | 0.309 | 0.21× | 0.50× | 0.78× |
| group-by high-card | 20,000 | 17.287 | 0.26× | 2.59× | 2.68× | 13.594 | 0.26× | 0.75× | 0.65× |
| group-by high-card | 100,000 | 85.172 | 0.30× | 3.00× | 3.23× | 77.492 | 0.24× | 0.61× | 0.91× |
| pivot | 1,000 | 0.495 | 0.16× | 1.03× | 1.28× | 0.418 | 0.16× | 1.05× | 1.29× |
| pivot | 20,000 | 15.992 | 0.32× | 1.55× | 1.47× | 17.203 | 0.24× | 0.82× | 1.16× |
| pivot | 100,000 | 73.969 | 0.33× | 1.02× | 1.36× | 66.328 | 0.29× | 1.02× | 1.25× |
| window CumulSum | 1,000 | 0.469 | 0.15× | 1.55× | 1.59× | 0.406 | 0.16× | 1.30× | **1.52×** |
| window CumulSum | 20,000 | 28.055 | 0.18× | 1.64× | 1.49× | 23.312 | 0.20× | 1.25× | 1.33× |
| window CumulSum | 100,000 | 150.570 | 0.17× | 1.80× | 1.76× | 138.648 | 0.15× | 1.17× | 1.35× |
| sort two keys | 1,000 | 0.320 | 0.24× | 1.40× | 1.77× | 0.287 | 0.24× | 0.24× | 0.52× |
| sort two keys | 20,000 | 9.504 | 0.37× | 2.44× | 3.59× | 10.164 | 0.52× | 0.19× | 0.62× |
| sort two keys | 100,000 | 78.672 | 0.34× | 2.04× | 2.34× | 77.859 | 0.26× | 0.16× | 0.44× |

node 24 via Fable 5:

| node | rows | before: full (ms) | before: diff ÷ full | before: refresh ÷ full | before: tick ÷ full | after: full (ms) | after: diff ÷ full | after: refresh ÷ full | after: tick ÷ full |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| lines | 1,000 | 1.187 | 0.23× | 1.26× | 1.42× | 1.094 | 0.21× | 0.49× | 0.69× |
| lines | 20,000 | 24.508 | 0.30× | 2.33× | 2.33× | 20.996 | 0.23× | 0.54× | 0.77× |
| lines | 100,000 | 123.008 | 0.22× | 2.93× | 3.15× | 110.000 | 0.24× | 0.51× | 0.75× |
| byRegion | 1,000 | 1.968 | 0.14× | 0.83× | 0.92× | 1.594 | 0.13× | 0.49× | 0.62× |
| byRegion | 20,000 | 38.000 | 0.15× | 0.89× | 1.00× | 32.000 | 0.16× | 0.55× | 0.70× |
| byRegion | 100,000 | 193.000 | 0.18× | 1.41× | 1.66× | 158.000 | 0.21× | 0.53× | 0.75× |
| filter-groupby | 1,000 | 1.781 | 0.13× | 0.72× | 0.84× | 1.531 | 0.14× | 0.60× | 0.73× |
| filter-groupby | 20,000 | 33.004 | 0.16× | 1.00× | 0.97× | 32.500 | 0.16× | 0.69× | 0.85× |
| filter-groupby | 100,000 | 161.992 | 0.14× | 1.32× | 1.52× | 153.000 | 0.22× | 0.65× | 0.86× |
| filter-sort-limit | 1,000 | 2.750 | 0.08× | 0.73× | 0.82× | 2.344 | 0.09× | 0.40× | 0.49× |
| filter-sort-limit | 20,000 | 53.984 | 0.09× | 1.26× | 1.33× | 50.992 | 0.12× | 0.45× | 0.58× |
| filter-sort-limit | 100,000 | 276.000 | 0.09× | 1.28× | 1.46× | 244.992 | 0.10× | 0.43× | 0.52× |
| filter-groupby-filter | 1,000 | 1.813 | 0.12× | 0.74× | 0.86× | 1.625 | 0.14× | 0.63× | 0.73× |
| filter-groupby-filter | 20,000 | 37.500 | 0.18× | 0.95× | 1.01× | 32.000 | 0.18× | 0.66× | 0.81× |
| filter-groupby-filter | 100,000 | 165.008 | 0.15× | 1.27× | 1.59× | 154.000 | 0.21× | 0.68× | 0.90× |
| inner join | 1,000 | 4.626 | 0.06× | 1.00× | 1.05× | 4.125 | 0.07× | 1.00× | 1.11× |
| inner join | 20,000 | 122.992 | 0.05× | 0.98× | 1.04× | 107.992 | 0.06× | 1.03× | 1.09× |
| inner join | 100,000 | 680.000 | 0.06× | 1.01× | 1.11× | 596.992 | 0.07× | 1.02× | 1.14× |
| group-by high-card | 1,000 | 2.688 | 0.10× | 0.98× | 1.09× | 2.312 | 0.11× | 0.46× | 0.58× |
| group-by high-card | 20,000 | 58.008 | 0.12× | 1.26× | 1.41× | 50.000 | 0.12× | 0.50× | 0.65× |
| group-by high-card | 100,000 | 294.000 | 0.11× | 1.56× | 1.68× | 262.008 | 0.14× | 0.53× | 0.68× |
| pivot | 1,000 | 3.125 | 0.10× | 0.98× | 1.10× | 2.688 | 0.10× | 1.00× | 1.14× |
| pivot | 20,000 | 65.000 | 0.17× | 1.05× | 1.15× | 51.008 | 0.13× | 1.02× | 1.20× |
| pivot | 100,000 | 321.000 | 0.16× | 1.01× | 1.15× | 251.992 | 0.12× | 1.02× | 1.15× |
| window CumulSum | 1,000 | 1.844 | 0.15× | 0.92× | 1.10× | 1.656 | 0.16× | 1.06× | 1.25× |
| window CumulSum | 20,000 | 44.000 | 0.15× | 1.02× | 1.07× | 37.000 | 0.17× | 1.00× | 1.19× |
| window CumulSum | 100,000 | 227.000 | 0.14× | 1.21× | 1.39× | 198.000 | 0.15× | 1.07× | 1.24× |
| sort two keys | 1,000 | 2.625 | 0.10× | 0.57× | 0.69× | 2.438 | 0.11× | 0.38× | 0.47× |
| sort two keys | 20,000 | 88.000 | 0.08× | 0.47× | 0.56× | 79.000 | 0.07× | 0.22× | 0.28× |
| sort two keys | 100,000 | 547.000 | 0.06× | 0.54× | 0.66× | 486.000 | 0.05× | 0.20× | 0.25× |

A second .NET run of the four shapes that failed or came close, the same day on the same tree
(tick ÷ full at 1,000 / 20,000 / 100,000 rows): `lines` 1.05 / 1.43 / 1.56, `filter > groupBy`
0.97 / 1.81 / 1.44, `filter > sort > limit` 1.05 / 2.40 / 1.94, `CumulSum` window 0.97 / 1.47 / 1.49.
The cells move by a third between runs. The diff's share does not: it stays at 0.9 to 1.3 of the full
evaluation on every one of these nodes at 20,000 and 100,000 rows.

**Why .NET and not node.** On node the diff costs 0.05 to 0.25 of a full evaluation, because the
evaluator there is slower relative to string minting. On .NET the typed kernels (Phases 266 to 270)
make a one-comparison or two-`Derive` evaluation about as cheap as minting one string per row. The
remaining refresh cost is structural: it builds the result's `Cell list`s, which the full evaluation
builds too, and it reads the new table's keys. Making the refresh cheaper cannot take a tick below
its diff.

**Open — for the operator (the shard's stop rule).** On .NET, Release, the table-fed tick of `lines`,
`filter > groupBy` and `filter > sort > limit` stays above 1.5× at 20,000 and 100,000 rows, as
measured above (up to 2.4×). The shard names three options: degrade those shapes by plan on .NET,
accept a stated per-shape bound, or deprecate the table-fed path on .NET. For the third, a caller
that edits through `ColumnOps.applyPrepared` and refreshes with `refreshPrepared` already pays neither
the diff nor the keying for a `Derive`-only pipeline (Phase 268). Until that decision, the gate holds
1.5× on the three `Scaling` pipelines in its own build, and nothing was loosened.

**Resolved.** The operator ruled on 2026-09-28 for a stated per-shape bound on .NET (2.5× for the
three shapes, 1.5× for everything else) until the diff keyed by the typed id. Phase 283 enforced
that ruling across every corpus node, keyed the diff by the typed id, and retired the per-shape bound.
Every node is now held to 1.5×: see "The diff pairs rows by the typed id" below.

### The diff pairs rows by the typed id (Phase 283)

**The verdict first.** On .NET the table-fed tick of every Phase 262 corpus node now costs at most
1.5 times the full evaluation it replaces, and the per-shape bound the operator set on 2026-09-28
is retired. `Delta.diff` pairs the new table's rows with the prior's by the witness's typed id
instead of by key string. It renders a key string only for a row the delta carries, where it used to
render one for every row. In the gate's Debug build the three shapes the ruling singled out (`lines`,
`filter > groupBy`, `filter > sort > limit`) read 0.53 to 1.25. In Release the median of three runs
is 0.75 to 1.30. A table-fed caller sees no change on its side: the witness, the delta, its wire and
every refusal are the same.

**The ruling, enforced first.** "The table-fed tick on every corpus node costs at most 1.5 times the
full evaluation" (`ScalingTests`, clock leg) holds all ten corpus nodes at 1,000, 20,000 and 100,000
rows. The input, pipeline, identity column and one-row edit are the probe's own (`Corpus.fs`, copied
into the test). It uses a batched best of five, so a 1,000-row node is timed over a window the clock
can resolve. It landed in this phase's first commit with the ruling's bounds: 2.5 for the three
shapes, 1.5 for every other node. It was green on the pre-phase tree (worst cell `lines` at 100,000
rows, 1.60 in the Debug build). With six extra key renders injected per row into the diff it was red
on all three attempts: `lines` read 2.49 to 2.70 and `byRegion` and `filter > groupBy > filter`
1.61 to 1.90. Once the change below landed, the case was tightened to 1.5 for every node.

**Measured before building: where the diff's time went.** This is the warm diff a tick pays. Its
prior source was keyed by the state and its new table was a fresh object. .NET 10, Release, best of
five batched samples. "Rendering ÷ diff" is (`KeyOf` + `KeyString` − `KeyOf`) over the diff's own
time. The two hash columns are what a cold diff's uniqueness check costs over string keys and over
typed ids.

| node | rows | full (ms) | diff, warm (ms) | `KeyOf` | `KeyOf` + `KeyString` | rendering ÷ diff | string hash + uniqueness | typed pairing (`KeyOf` ×2 + equality) | typed hash + uniqueness |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| lines | 20,000 | 3.505 | 3.340 | 0.525 | 3.418 | 0.87 | 0.379 | 0.824 | 0.235 |
| lines | 100,000 | 28.730 | 20.172 | 1.745 | 17.379 | 0.78 | 2.310 | 3.391 | 1.318 |
| byRegion | 20,000 | 4.298 | 4.588 | 0.324 | 3.513 | 0.70 | 0.373 | 1.116 | 0.234 |
| byRegion | 100,000 | 23.426 | 19.668 | 1.872 | 17.605 | 0.80 | 2.788 | 4.293 | 1.736 |
| filter-groupby | 20,000 | 4.838 | 3.899 | 0.539 | 2.617 | 0.53 | 0.418 | 1.079 | 0.639 |
| filter-groupby | 100,000 | 26.156 | 22.547 | 3.230 | 12.023 | 0.39 | 2.958 | 5.734 | 4.247 |
| filter-sort-limit | 20,000 | 3.546 | 4.594 | 0.550 | 2.409 | 0.40 | 0.428 | 1.118 | 0.576 |
| filter-sort-limit | 100,000 | 18.189 | 19.609 | 2.869 | 10.039 | 0.37 | 3.060 | 5.699 | 4.227 |
| filter-groupby-filter | 20,000 | 5.261 | 3.930 | 0.531 | 3.361 | 0.72 | 0.416 | 1.044 | 0.597 |
| filter-groupby-filter | 100,000 | 22.227 | 27.480 | 3.422 | 15.371 | 0.43 | 3.059 | 7.220 | 5.138 |
| inner join | 20,000 | 39.656 | 5.326 | 0.506 | 4.109 | 0.68 | 0.524 | 1.031 | 0.242 |
| inner join | 100,000 | 185.984 | 21.414 | 1.738 | 16.672 | 0.70 | 2.909 | 4.784 | 1.869 |
| group-by high-card | 20,000 | 17.238 | 4.992 | 0.571 | 3.516 | 0.59 | 0.400 | 1.250 | 0.238 |
| group-by high-card | 100,000 | 86.391 | 15.922 | 1.631 | 15.910 | 0.90 | 2.559 | 3.927 | 1.279 |
| pivot | 20,000 | 15.711 | 4.048 | 0.564 | 3.987 | 0.85 | 0.422 | 1.153 | 0.270 |
| pivot | 100,000 | 85.773 | 26.750 | 2.016 | 21.793 | 0.74 | 2.693 | 4.127 | 1.413 |
| window CumulSum | 20,000 | 31.230 | 3.561 | 0.394 | 4.282 | 1.09 | 0.477 | 1.016 | 0.254 |
| window CumulSum | 100,000 | 165.367 | 26.535 | 2.567 | 21.984 | 0.73 | 3.078 | 4.733 | 1.471 |
| sort two keys | 20,000 | 12.242 | 4.023 | 0.357 | 4.474 | 1.02 | 0.510 | 0.724 | 0.261 |
| sort two keys | 100,000 | 86.477 | 27.328 | 1.649 | 18.340 | 0.61 | 3.407 | 5.332 | 1.612 |

Rendering was the largest single term on every node. On the integer-keyed nodes it was 0.59 to
1.09 of the diff (the batched estimates overlap). On the string-keyed `Scaling` nodes it was 0.37 to
0.72, where the rest is mostly the content pass: unpacking the edited column in both tables and
comparing it cell by cell. Pairing by id is `KeyOf` over both tables plus one equality test per
row. It costs 0.18 to 0.42 of rendering on the integer keys, and 0.37 to 0.79 on the string keys,
whose `KeyOf` is dearer. The prior side's ids are recorded, so a tick pays `KeyOf` on its new table
only. So the phase went ahead. A diff over typed ids keeps `KeyOf` and the content pass and drops
the rendering.

**What changed.**

- **A witness DECLARES its key equality.** Pairing by id answers what pairing by key string answers
  only if the equality agrees exactly with `KeyString`: `Equals a b` holds exactly when
  `KeyString a = KeyString b`. F# structural equality on `'Id` does not promise that. A witness may
  render two structurally distinct ids to one string (a case-folding key, a rounding one), and pairing
  those structurally would miss a `DuplicateIdentity` the string path reports. So the equality is
  declared, never inferred. The declaration is internal (`KeyEqualities`) and is keyed by the witness
  RECORD object. `RowIdentity.byColumn` and `byColumns` declare `CellKey` token equality, which a law
  in the suite already holds equal to `cellToken` equality (for `byColumns`, cell by cell, because
  `rowTokenString` is length-prefixed and so injective). Any copy of a witness (`{ w with … }`,
  a counting wrapper, a renamed scheme) is a new record with no declaration. It takes the string
  path, which is unchanged and always correct. The key is the record and not the `KeyString` function
  because the Release compiler re-created a function value it could see the definition of. A
  declaration keyed on the function silently never matched, and the allocation counter was what
  showed it: typed and string diffs allocated the same bytes.
- **The diff pairs by id for a declared witness.** A row whose id equals the prior row's at the same
  index is paired in place by one equality test. A row that moved is looked up in a typed index, built
  only once something has moved. Uniqueness runs on a typed set. A paired row's key string is the
  prior source's own instance. A key string is rendered only for an added row, or for a refusal's
  payload, which is rendered exactly as the string path renders it. Every decision is the string
  path's decision, because the equality agrees with the strings: the same deltas, the same refusals
  in the same order (every `before` defect before any `after` defect, the first offending row in row
  order), the same `DuplicateIdentity` key string.
- **The keyed index carries the typed ids** (Phase 273's `KeyedIndex`, still internal). The diff
  records them for both tables, and the seam's `tokensOf` records them when it keys a source. So a
  tick's prior side, which the state or the previous tick keyed, is read back and the witness is not
  asked for it again. They are read back only for the witness record that recorded them. That is what
  makes unboxing the non-generic index safe, because one record has one `'Id`. A prior side keyed by
  a string-path caller has no ids; the diff asks the witness for them once and records them.
- **Rendering is O(changes) on the tick path.** A counted main-suite case ("a typed-key tick renders a
  key string at most once per row the delta carries", `IncrementalRefreshCostTests`) counts the
  witness's renders across four ticks of three pipelines: two edits in place, and two in which rows
  move, one is deleted and one is inserted. It renders exactly the added rows' keys, 0 or 1 per tick
  against 1,000 rows, and at most one per carried row. It was red with the typed path switched off, at
  1,000 renders per in-place tick. Each tick's delta and wire bytes equal the string path's, and the
  refresh equals the reference.

**What did not change.** The public surface (the API baseline is byte-identical), `RowIdentity`'s
shape, every delta and its equality and wire bytes, the order and payloads of refusals, and the
incremental law family and its vectors. The Phase 272 equivalence test (4,000 drawn pairs) now diffs
every pair on both paths: typed and string, cold, with the prior side known, and typed over a prior
keyed by the string path. It holds all of them equal to the row-token reference and to each other,
wire bytes included. It was red (iteration 296) with the typed path's moved-key duplicate check
removed. A witness nothing declares for, and every diff whose new table is already keyed, takes the
string path exactly as before. The cold diff (neither table keyed) still renders every prior row's
key: its prior side has no strings to reuse, and the next tick reads them. It records the typed ids
beside them, so it allocates 1.57 times the keying floor where it allocated 1.50, inside
`diffFloorAllocBound`.

**The result, .NET 10 (Release).** Every Phase 262 corpus node, a one-row edit, and the same probe
and columns as Phase 274's table. Three runs on a machine other sessions were loading (about a third
of its cores busy). Cells move by up to 0.5 between runs, so the last column gives all three ratios
and their median. Cells above 1.5× are in bold. Measured on the same tree, the typed warm diff costs
0.32 to 0.65 of the string path's at 20,000 and 100,000 rows.

| node | rows | full (ms) | diff ÷ full | refresh ÷ full | tick ÷ full | tick ÷ full, three runs (median) |
|---|---:|---:|---:|---:|---:|---:|
| lines | 1,000 | 0.173 | 0.32× | 0.69× | 0.75× | 0.94 / 1.03 / 0.75 (0.94×) |
| lines | 20,000 | 3.109 | 0.48× | 0.96× | 1.25× | 0.75 / 1.80 / 1.25 (1.25×) |
| lines | 100,000 | 21.652 | 0.33× | 0.79× | 1.09× | 1.19 / 0.91 / 1.09 (1.09×) |
| byRegion | 1,000 | 0.158 | 0.38× | 0.60× | 1.03× | 0.94 / 0.52 / 1.03 (0.94×) |
| byRegion | 20,000 | 4.716 | 0.48× | 0.55× | 1.08× | 1.08 / 1.06 / 1.08 (1.08×) |
| byRegion | 100,000 | 22.309 | 0.34× | 0.59× | 0.88× | 1.01 / 0.83 / 0.88 (0.88×) |
| filter-groupby | 1,000 | 0.164 | 0.41× | 0.61× | 1.05× | 1.03 / 1.02 / 1.05 (1.03×) |
| filter-groupby | 20,000 | 4.388 | 0.50× | 0.57× | 1.13× | 1.13 / 0.94 / 1.13 (1.13×) |
| filter-groupby | 100,000 | 21.547 | 0.45× | 0.58× | 1.04× | 0.97 / 1.00 / 1.04 (1.00×) |
| filter-sort-limit | 1,000 | 0.146 | 0.45× | 0.68× | 1.14× | 1.16 / 1.17 / 1.14 (1.16×) |
| filter-sort-limit | 20,000 | 3.198 | 0.56× | 0.95× | **1.67×** | 1.30 / 1.20 / 1.67 (1.30×) |
| filter-sort-limit | 100,000 | 17.807 | 0.54× | 0.79× | 1.30× | 1.26 / 1.31 / 1.30 (1.30×) |
| filter-groupby-filter | 1,000 | 0.164 | 0.40× | 0.64× | 1.09× | 0.97 / 1.00 / 1.09 (1.00×) |
| filter-groupby-filter | 20,000 | 6.244 | 0.44× | 0.78× | 1.25× | 0.61 / 1.24 / 1.25 (1.24×) |
| filter-groupby-filter | 100,000 | 22.336 | 0.45× | 0.57× | 1.03× | 1.02 / 1.02 / 1.03 (1.02×) |
| inner join | 1,000 | 1.187 | 0.05× | 0.75× | 0.84× | 1.11 / 1.12 / 0.84 (1.11×) |
| inner join | 20,000 | 51.430 | 0.04× | 0.93× | 0.70× | 1.17 / 0.98 / 0.70 (0.98×) |
| inner join | 100,000 | 209.000 | 0.04× | 1.06× | 1.05× | 1.15 / 1.02 / 1.05 (1.05×) |
| group-by high-card | 1,000 | 0.408 | 0.17× | 0.48× | 0.70× | 1.01 / 0.74 / 0.70 (0.74×) |
| group-by high-card | 20,000 | 21.043 | 0.09× | 0.56× | 0.58× | 0.43 / 0.55 / 0.58 (0.55×) |
| group-by high-card | 100,000 | 100.602 | 0.08× | 0.90× | 0.78× | 0.59 / 0.67 / 0.78 (0.67×) |
| pivot | 1,000 | 0.605 | 0.12× | 0.90× | 1.14× | 0.80 / 1.18 / 1.14 (1.14×) |
| pivot | 20,000 | 24.250 | 0.09× | 0.79× | 1.01× | 1.10 / 1.08 / 1.01 (1.08×) |
| pivot | 100,000 | 101.109 | 0.10× | 0.94× | 1.05× | 0.97 / 1.26 / 1.05 (1.05×) |
| window CumulSum | 1,000 | 0.528 | 0.13× | 1.32× | 1.49× | 1.45 / 1.52 / 1.49 (1.49×) |
| window CumulSum | 20,000 | 31.328 | 0.07× | 1.14× | 0.99× | 1.06 / 1.33 / 0.99 (1.06×) |
| window CumulSum | 100,000 | 180.250 | 0.05× | 1.16× | 1.12× | 1.08 / 1.22 / 1.12 (1.12×) |
| sort two keys | 1,000 | 0.352 | 0.20× | 0.24× | 0.47× | 0.44 / 0.44 / 0.47 (0.44×) |
| sort two keys | 20,000 | 13.041 | 0.17× | 0.20× | 0.40× | 0.33 / 0.58 / 0.40 (0.40×) |
| sort two keys | 100,000 | 107.891 | 0.10× | 0.21× | 0.21× | 0.23 / 0.30 / 0.21 (0.23×) |

Two single cells went above 1.5 (`lines` at 20,000 rows, 1.80 in one run, and `filter > sort >
limit` at 20,000 rows, 1.67 in one run). The other two runs of each read 0.75 and 1.25, and 1.30
and 1.20. The diff is now 0.3 to 0.56 of the full evaluation on the three shapes, where it was 0.9 to
1.3. What is left of their tick is the refresh.

The `CumulSum` window at 1,000 rows reads 1.45 to 1.52 in Release. It is not one of the ruled shapes,
and it is not the diff's (0.13 of the full evaluation): its refresh alone is 1.3 of the full
evaluation at that size. Phase 274 recorded 1.52 for the same cell. In the gate's Debug build the
clock leg reads it at 1.03 to 1.08. That is a refresh cost this phase did not touch, and it is
recorded here rather than absorbed.

node 24 via Fable 5 (one run, the typed and string paths on the same tree):

| node | rows | full (ms) | diff, typed (ms) | diff, string path (ms) | tick ÷ full, typed | tick ÷ full, string path |
|---|---:|---:|---:|---:|---:|---:|
| lines | 1,000 | 1.219 | 0.262 | 0.277 | 0.70× | 0.73× |
| lines | 20,000 | 25.004 | 6.000 | 6.749 | 0.86× | 0.84× |
| lines | 100,000 | 122.992 | 26.000 | 28.000 | 0.71× | 0.75× |
| byRegion | 1,000 | 1.781 | 0.250 | 0.273 | 0.68× | 0.68× |
| byRegion | 20,000 | 36.008 | 5.875 | 7.000 | 0.78× | 0.82× |
| byRegion | 100,000 | 185.000 | 28.000 | 31.004 | 0.67× | 0.68× |
| filter-groupby | 1,000 | 1.875 | 0.242 | 0.234 | 0.72× | 0.72× |
| filter-groupby | 20,000 | 34.504 | 6.500 | 5.749 | 0.78× | 0.80× |
| filter-groupby | 100,000 | 153.992 | 24.000 | 24.000 | 0.82× | 0.85× |
| filter-sort-limit | 1,000 | 2.750 | 0.238 | 0.258 | 0.52× | 0.50× |
| filter-sort-limit | 20,000 | 62.000 | 6.624 | 6.875 | 0.52× | 0.51× |
| filter-sort-limit | 100,000 | 291.992 | 37.504 | 36.992 | 0.57× | 0.60× |
| filter-groupby-filter | 1,000 | 1.750 | 0.266 | 0.266 | 0.84× | 0.82× |
| filter-groupby-filter | 20,000 | 35.004 | 6.501 | 6.750 | 0.86× | 0.81× |
| filter-groupby-filter | 100,000 | 175.000 | 29.996 | 29.984 | 0.81× | 0.91× |
| inner join | 1,000 | 10.000 | 0.469 | 0.609 | 0.90× | 0.80× |
| inner join | 20,000 | 145.000 | 7.750 | 8.124 | 1.00× | 1.01× |
| inner join | 100,000 | 771.008 | 39.000 | 43.000 | 1.01× | 1.08× |
| group-by high-card | 1,000 | 3.248 | 0.305 | 0.328 | 0.55× | 0.53× |
| group-by high-card | 20,000 | 65.016 | 8.376 | 9.376 | 0.63× | 0.58× |
| group-by high-card | 100,000 | 396.000 | 32.000 | 32.000 | 0.47× | 0.50× |
| pivot | 1,000 | 3.562 | 0.320 | 0.313 | 1.11× | 1.12× |
| pivot | 20,000 | 75.008 | 15.750 | 12.000 | 1.07× | 1.23× |
| pivot | 100,000 | 349.992 | 48.992 | 50.992 | 1.08× | 1.17× |
| window CumulSum | 1,000 | 2.375 | 0.320 | 0.344 | 1.13× | 1.11× |
| window CumulSum | 20,000 | 48.000 | 7.249 | 7.748 | 1.13× | 1.50× |
| window CumulSum | 100,000 | 280.000 | 34.992 | 34.504 | 1.10× | 1.19× |
| sort two keys | 1,000 | 3.125 | 0.305 | 0.340 | 0.44× | 0.46× |
| sort two keys | 20,000 | 105.000 | 7.127 | 8.000 | 0.26× | 0.27× |
| sort two keys | 100,000 | 635.992 | 30.500 | 32.992 | 0.21× | 0.21× |

On node every tick stays within 1.13× on the typed path (1.50× at most on the string path). The
typed diff there costs about what the string path's does (0.77 to 1.31 across cells, 0.9 to 1.0 on
most), because JavaScript mints a short string about as cheaply as it hashes a cell. The
typed path's answers were checked against the string path's on every node under Fable in the same
run.

### A consumer's own witness takes the typed path: `RowIdentity.withKeyEquality` (Phase 284)

Phase 283's typed pairing was reachable only by the two reference witnesses, because the declaration
was internal. A consumer whose identity is its own type — a composite of typed fields, a domain id —
paid the string path on every tick. Phase 284 makes the declaration public, and it is the only route
in: `byColumn` and `byColumns` are now declared through it too.

**How a consumer opts in.** Build the witness as before, then declare the equality its `KeyString`
agrees with, and keep the witness the call RETURNS:

```fsharp
let orderLine : RowIdentity<string * int> =
    { Scheme = "order-line"
      KeyOf = fun t -> (* staged per table, as byColumn is *) …
      KeyString = fun (order, line) -> string order.Length + ":" + order + "|" + string line }
    |> RowIdentity.withKeyEquality HashIdentity.Structural
```

`withKeyEquality equality w` returns a fresh COPY of `w` carrying the declaration. `w` itself is left
undeclared, so declaring never changes a witness someone else holds, and a later copy
(`{ w with … }`) is, as in Phase 283, a new witness that takes the string path. The record shape of
`RowIdentity` did not change. The registry is still keyed by the record object, not by the
`KeyString` function (the Release compiler re-creates function values it can see the definition of).
The counted case below was run in a Release build for this phase, as well as in the gate's Debug
suite.

**What the consumer promises, and what breaks if the promise is false.** For every two ids the witness
produces, `equality.Equals(a, b)` holds exactly when `KeyString a = KeyString b`, and ids it calls
equal it hashes alike. The library uses the equality as given. It cannot prove agreement, and it does
not check it on any path it runs: a check would render the key strings the typed path exists to
avoid. Structural equality is not automatically such an equality. A `KeyString` that case-folds,
rounds or truncates renders distinct ids to one string, and a key string that is not injective (two
fields joined with no length prefix) does the same. A broken declaration is wrong silently:

- an equality **finer** than the strings misses the `DuplicateIdentity` refusal the string path
  reports, and the diff answers a delta over keys that are not identities;
- an equality **coarser** than the strings pairs two identities as one row, so one is reported as
  changed where the string path reports one removed and one added;
- the keys a wrong diff rendered are remembered for the tables it keyed (Phase 273), so a later diff of
  those same table objects under that scheme reads the wrong answer back, even on the string path.

**The checking mode: `RowIdentity.checkKeyEquality w table`.** It is a helper for the consumer's own
test suite, and nothing in the library calls it. It answers `Ok ()` or the first
`KeyEqualityDisagreement` it finds on that table: `EqualIdsDistinctKeys`, `DistinctIdsEqualKey`,
`UnequalHashes`, or `NotDeclared` for a witness that carries no declaration (usually the undeclared
original was kept instead of the returned copy). Every keyed row is checked in one linear pass, by key
string and by id. The first 256 keyed rows are also compared pairwise, which needs no hash and so
catches an equality whose hash disagrees with it for ids it calls equal. `Ok ()` is evidence over the
table it was given, not a proof. Feed it tables that reach the ids the witness renders alike or apart:
case variants, rounding boundaries, composite components that swap.

**How it is held.** The Phase 272/283 equivalence test (4,000 drawn pairs) adds a consumer-style
composite witness over `(id, v)` as a `string * int`, with an injective key string and structural
equality, declared through `withKeyEquality`. Each pair is diffed typed against the row-token
reference, and against the witness's own undeclared copy over fresh tables, with wire bytes compared.
`checkKeyEquality` passes on both tables of every pair. The draws reach more than 20 answered and more
than 20 refused pairs for that witness. A second case declares three wrong equalities (finer, coarser,
and one with a hash that is not its own) and shows `checkKeyEquality` naming each. It also shows the
damage: the finer declaration answers `Ok` where the string path refuses with `DuplicateIdentity`, and
the coarser one answers one `RowChanged` where the string path answers a `RowAdded` and a `RowRemoved`.
The Phase 283 counted case now runs for a composite `(id, grp)` witness declared through
`withKeyEquality` as well as for `byColumn`. Across four ticks of three pipelines it renders exactly the
added rows' keys (0 per in-place tick of 1,000 rows, 1 per reshaping tick) in both the Debug and the
Release build. An undeclared witness would render 1,000.

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
point of the widening and it is described for adopters under `0.28.1` in the Fuaran.Core repository's `STABILITY.md`, where versions before `0.33.0` are recorded.

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
| `cachedAt` (read by `evalStep`; `cellAt` before Phase 274) | the cell an evaluating step (`Filter` predicate, `Derive` expression) computed for this row | `Stable` | the cell is a function of the row's cells; a window moves them without the delta naming the row. **Fixed by Phase 208** — it read `not Affected` to `0.26.0`. |
| `walk`, `WJoin` arm (`verdictOf` before Phase 274) | the cached `Semi`/`Anti` verdict | `Stable` **and** the right relation unmoved | the verdict is a function of the row's key cells *and* of the relation, and the delta describes neither the second nor a window's effect on the first. **Fixed by Phase 208.** |
| `walk`, `WSort` arm — the reusable set | this row's cached POSITION in the merged order | `Stable` | a cached order is a cached answer: it is a function of every row's sort-key cells, which a window moves. **Fixed by Phase 215** — it read `not Affected` to `0.28.0`. |
| `walk`, `WWindow` arm | nothing — it CLEARS `Stable`, for live rows and dead ones alike | — | it is the producer the other rows are about. A dead row's cells are not recomputed, so its cache is cleared rather than refreshed: the conservative reading, and the only one available. |
| `runIncremental`'s row-cache write-back (until Phase 274) | the prior `Cached` list **as a list**, in place of rebuilding it from `Fresh` | `Stable` **and** the two lists the same length | an IDENTITY claim rather than a reuse of a computation. Gone since Phase 274: each evaluating step writes its own cell array once, so there is no per-row list to reuse. |
| `groupStep` — the carried group (its token until Phase 274, its index since) | this row's group identity from the prior evaluation | `Stable` **and** `Prior >= 0` | the token is a pure function of the key cells. A row the prior evaluation did not reach mints as before, so a carried token is never the only derivation. |
| `groupStep` — `cellsFor`'s `allStable` | a group's cached aggregate cells | every member `Stable`, **and** the ordered member list unchanged, **and** a cached aggregate to reuse | an aggregate is a function of its members' cells, so one unstable member is enough to invalidate it; the ordered-member test is what makes a pure reordering — which `Delta.diff` reports as quiet — not reusable. |
| the group table's own frame (the maintained-`GroupBy` tail) | the tail's cached cells for a group row | the group's aggregates were not recomputed | sound for the reason a source row's `not Affected` is not: a group row's cells are a function of its members alone, `groupStep` has just recomputed exactly the groups whose members moved, and **no window runs over the group table**. This is the one site where the delta-shaped condition is the right one, and it is right because it is not the delta's statement — it is the group step's. |

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

The equivalence family `IncrementalDelta.laws` (in `Fuaran.Core.DataFrame.Conformance` since Phase 257) certifies the seam
itself against the reference evaluator over a generated corpus. A consumer does not need to re-run
it, but the pattern is worth copying for your own pipelines: evaluate both ways and assert equality,
then assert the footprint. The first catches a wrong answer; the second catches the subtler
regression where the answer stays right and the saving quietly disappears.
