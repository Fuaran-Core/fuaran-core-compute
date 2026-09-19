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

The family itself reports slightly **higher** post-fix ratios — about 34, 52 and 44 — because it
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

Two practical consequences. A pipeline of cheap row-local predicates is better evaluated in full,
and `Incremental.plan` will still say the refresh is restricted — correctly, because the footprint
claim is about expression evaluations and is true. And a pipeline whose per-row work is real — a
long derived expression, a `Case` ladder, string work — is exactly where the seam was designed to
be used. Measure your own pipeline rather than reading either row as a rule: the threshold is a
property of your expressions, and the two figures above bracket it.

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

## Verifying your own adoption

The equivalence family `IncrementalDelta.laws` (in `Fuaran.Core.Conformance`) certifies the seam
itself against the reference evaluator over a generated corpus. A consumer does not need to re-run
it, but the pattern is worth copying for your own pipelines: evaluate both ways and assert equality,
then assert the footprint. The first catches a wrong answer; the second catches the subtler
regression where the answer stays right and the saving quietly disappears.
