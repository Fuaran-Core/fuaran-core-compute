# Fuaran.Core.Compute — decisions (newest first)

## 2026-10-04 — D9: whether float `Sum` becomes exactly rounded is the operator's ruling; the operator ruled NO (2026-10-05): the fold stays the default

**Measured (fuaran-core#345); ruled NO on 2026-10-05 (below).** Phase 345 opens with an operator ruling — whether the
float `Sum`, and through it `Mean`, `StdDev`, `CumulSum`, `RollingSum` and `RollingMean`, moves from
the left-to-right fold (D3; `Kernels.fs` header) to an exactly-rounded sum, whose answer is the double
nearest the true sum and so cannot depend on the order or the partitioning of the additions. A
dispatched session cannot take that ruling (the side's doctrine escalates it), so this entry records
what the ruling needs and stops. Nothing in the evaluator moved. The figures are in
[`benchmarks/results/2026-10-04-i7-9700-phase-345.md`](benchmarks/results/2026-10-04-i7-9700-phase-345.md),
with the harness beside them; native x64 (i7-9700) and node only — the Arm64 leg was not run.

**1. The cost, single-threaded.** The fold costs 1.3 ns a value on both hosts. CPython's `math.fsum`
algorithm (Shewchuk's partials, correctly rounded collapse) costs 3.4 to 3.9 times the fold where
every partial sum is exact (whole quarters — the benchmark corpus's shape), 5.5 to 8 times at
ordinary dynamic range (uniform in `[0, 1)`) and 25 to 34 times at wide range (mixed signs across
sixteen decades), on .NET and node alike; the multiple is set by the live partial count (1, 2, 7 to
9), which the input's spread of magnitudes decides, not `n`. Against D7's whole streamed `GroupBy`
(3.94 ms, a million rows, ten keys) that is 7 ms more at ordinary range (a 2.8x slower step) and 42 ms
at wide range. Split into eight ranges on the pool it is 1.9x the fold at a million ordinary values,
below the fold where the inputs are exact, 8.5x at wide range.

**2. The invariance and the cross-host bytes hold.** On every input the exact sum of five shuffles,
of 3 / 5 / 8 / 64 / 1,000-way partitions (each range's partials fed through the same algorithm) and of
the eight-thread run has the sequential exact sum's bit pattern (45 checks); the shuffled fold differs
from the in-order fold on every non-exact input. `.NET`, node and CPython's own `math.fsum` return the
same 64 bits on all nine inputs. The parallel member the shard asks for is therefore sound by
construction: partials are a range's exact sum, and merging partial lists is the same algorithm.

**3. No transform law vector changes bytes.** The corpus's three float summations (`mean` over `w` in
`transform-3` and `transform-11`) are over one or two exactly-summable members; the `sum`s are int or
decimal. The fold and the exact sum agree on every vector, so the corpus cannot distinguish them — a
yes needs new vectors with inexact members (three or more `0.1`-class values) before any host is held
to the new sum.

**4. The twins, corrected from the shard.** Per D6, `fuaran-ts` is the only host running the
transform laws, and it still lacks the decimal strand (fuaran#2001) that blocks the corpus copy today;
`fuaran-rs` and `fuaran-go` carry evaluator twins with no laws leg; `fuaran-py` has no twin (and has
`math.fsum`). The algorithm is ~40 lines per language (`bench.mjs` is the TypeScript body).

**5. What a yes touches — wider than this repository.** The one aggregate semantics is
`Column.aggregate` in `fuaran-core` (`src/Fuaran.Core.Column/Column.fs`, `Sum` / `Mean` / `StdDev`
over `total` and `kept`), consumed here as a package: the exact sum lands THERE first, under a Core
cut, before this evaluator's streamed `MSumFloat` / `MMean` slots, the window's `CumulSum` seed and
running total and the rolling sums (`DataFrame.fs`) and the incremental seam's rescan rule
(`Incremental.fs`: "a float `Sum` is never maintained") can follow. Three consequences worth the
operator's eye: a `CumulSum` continued from a prior run must seed from the prior PARTIALS, not the
rounded float, or the continuation's bits differ from a fresh run's; Phase 306's finite-input overflow
refusal meets the algorithm's own intermediate overflow (CPython raises there); and an exactly-rounded
`Sum` becomes MAINTAINABLE under a delta (adding a member and adding a removed member's negation are
both exact over the partials), which the seam today rescans.

**6. What a no keeps.** D3's order, every byte today's vectors pin, the fold's cost, and the rule in
the `Kernels.fs` header that no float reduction is ever split — with D8's grouping-by-key and this
phase's eight-range figures as the measured price of that rule.

**The ruling — NO, taken by the operator on 2026-10-05.** The float `Sum` and every aggregate above
stay on D3's left-to-right fold; item 6 is what is kept, and no float reduction is split. The cost in
item 1 (a 2.8x slower `GroupBy` step at ordinary range, more at wide range) and the breaking change
in every non-exact answer are not paid for a property nothing today needs. An OPT-IN exactly-rounded
sum (a distinct aggregate beside `Sum`, landing in `Column.aggregate` first per item 5) is left to a
later Suggest-features candidate, not filed here. This entry and the figures are the phase; no
`STABILITY.md` entry is owed.

## 2026-10-04 — D8: the sort, the window and (since fuaran-core#371) the top-n run across threads; the grouping by key and the ranged join probe were measured and not kept

**Decided (fuaran-core#344), on measurement.** Native x64 only (i7-9700, 8 cores); the figures are
in [`benchmarks/results/2026-10-04-i7-9700-phase-344.md`](benchmarks/results/2026-10-04-i7-9700-phase-344.md).
The Arm64 leg was not run.

**1. Kept: three kernels in the pair.** `Partitions` (how many ranges a verb runs: one on the
portable member, one below `PartitionRows` = 32,768 on the native), `SortFinite` and `SortPositions`
(a merge sort, runs sorted on the pool and merged pairwise). The sort keys are distinct under a
total order — the packed keys end in the position, and the unpacked comparison ends in it — so any
correct sort answers the same array, and the portable member's sorts are the engine's own. Through
them: the sort, 2.0 times at 100,000 rows and 2.2 at a million, 4.7 where the keys do not pack; the
window's ordering through the sorts and its partitions scanned in chunks of whole partitions
across threads (1.6 to 3.9 times). No fold is ever split: a window partition's running total is one
sequential scan, so no float byte moves.

**2. Not kept: the grouping by key across threads.** D7 item 5 pointed here: partition by group so
that each thread owns whole groups and no table is merged. Built in the evaluator and measured, it
is 1.59 times faster at ten keys and a million rows and 1.16 at one key per ten rows, but 1.46 and
2.09 times SLOWER at 100,000 rows and slower still at 10,000, and it allocates 2.3 to 2.8 times as
much. Run on one thread it costs 22 ns a row against the sequential grouping's 10: each partition
reads its rows scattered across the whole vector, and the hashing and placing passes are work the
sequential path never does. A grouping that pays only above some size between 100,000 and a million
rows needs that crossover measured before a threshold can be set, so it is not shipped on a guess;
the prototype stays in the harness (`Partitioned.GroupByKey`) for whoever measures it.

**3. Held for the operator: the top-n a range of rows at a time.** Each range's heap on its own
thread, the candidates sorted under the same total order, answers the sequential heap's bytes and
is 2.8 to 4.4 times faster at 100,000 and a million rows. It is not shipped because the verify
gate's corpus tick case (`ScalingTests`, Phase 283) holds the incremental tick to 1.6 times the
full evaluation it replaces, and the full evaluation is what it speeds up: `filter > sort > limit`
at 100,000 rows read 1.75, 1.74 and 2.04 on the three attempts, against a sequential tick (diff and
refresh) of 14 to 16 ms. That case's own text makes a reading above the bound an operator decision
and forbids raising the bound to pass it, so the kernel waits on that decision: either the bound
is restated against the sequential evaluation (what the incremental seam replaces on every host),
or the tick gains threads of its own. The sort and the window, which the same case also times,
stayed within the bound (1.27 and 0.97 at 100,000 rows).

**Item 3 closed (fuaran-core#371) — operator ruling 2026-10-04, option A: the bound is restated
against the evaluation at one thread, it stays 1.6, and the parallel top-n ships.** What the tick
bound protects is that maintaining a result incrementally is not dearer than recomputing it on the
same footing; once the full evaluation ran on every core, the ratio measured the core count, not
the seam. Both tick clock cases (`ScalingTests`, Phases 274 and
283) now time the full evaluation on `oneThread` — the native member's vector kernels, its morsels
run in order on the caller's thread, never partitioned — and hold it equal to the reference answer
before timing it; the tick arm, `tickBound` (1.6) and `tickFloorMs` are unchanged. The ranged heap
from `fb25f9a` is back in the evaluator as it was, and the cross-member law that holds it ("over
frames thousands of rows long, ...") was checked to go red with one range's heap dropped. Figures
(Release, i7-9700, `benchmarks/results/2026-10-04-i7-9700-phase-371.md`): the corpus case's
`filter > sort > limit` cell at 100,000 rows read 1.54, 1.50 and 1.33 quiet and 1.31 under four
CPU burners against the parallel evaluation, and 0.63, 0.67 and 0.67 quiet and 0.66 loaded against
the one-thread evaluation; the worst cell of either case after the change was 1.36 (`inner join` at
1,000 rows, loaded). The top-n re-measured: 2.96 and 2.67 times at 100,000 rows, 4.33 and 3.78 at a
million (ten keys and one key per ten rows).

**4. Not kept: the join probe over ranges of left rows.** No consistent gain at either size (one
sitting, the machine under load): the probe is a small part of the join, whose cost is emitting
the matched pairs and gathering every output column.

**5. The shard's premise, checked: "boxing no longer dominates the grouping" held only at low
cardinality.** At one key per ten rows the grouping spent most of its time boxing the KEY cells
(a `Cell[]` per group, packed back into a vector): 20 of 32 ms at a million rows. The key column is
now read from each group's first row straight into the output vector (`DataFrame.keyColumn`), on
both members: 14.6 ms at a million rows, 1.23 ms at 100,000 (from 2.21). The window's partitioning
paid the same boxing for nothing and no longer does.

**Supporting change.** `windowOrder`, `windowOrderOf`, `windowColumnOf` and `windowColumnOver` keep
their signatures and run the portable member (the incremental seam's callers); the evaluator calls
their `...With` forms with the host kernels.

## 2026-10-03 — D7: the host-kernel verdicts are taken on a native JIT; Phase 270's are restated on Arm64 and x64, and two of them move

**Decided (fuaran-core#341), on measurement.** Phase 270 shipped the host kernels and removed an
order-preserving parallel `GroupBy`. Every timing behind it came from the x64 build running under
emulation on an Arm64 machine. Emulation penalises the intrinsics and the thread scheduling that were
being judged. The benchmark harness now refuses a run whose process architecture is not the
machine's (`Program.main`), and Layer 6 has committed suites (`benchmarks/.../Layer6.fs`). The Arm64
figures are in [`benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md`](benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md).
The x64 figures, taken natively on a 4-core i7-8650U with the machine quiet, are in
[`benchmarks/results/2026-10-03-i7-8650u-phase-341.md`](benchmarks/results/2026-10-03-i7-8650u-phase-341.md).
Each item below gives both. x64 agrees on every decision. One Arm64 reading does not reproduce: at
100,000 rows the morsel `Derive` favours larger morsels on x64 (item 4).

**1. Held: the comparison and bitmap kernels.** 270: 1M rows 37.84 to 1.447 ms (one comparison) and
34.49 to 1.901 ms (two under `And`). Native Arm64, portable over native: `cmp` 4.36 to 0.82 ms (5.3x),
`cmp-and` 6.69 to 1.22 ms (5.5x), and 4.2x to 5.1x at 10,000 and 100,000 rows. The ratio is smaller
because the portable loop is no longer emulated, not because the kernel is weaker. **x64 agrees:**
`cmp` 6.56 to 1.24 ms (5.3x), `cmp-and` 11.0 to 2.30 ms (4.8x) at 1M rows, and 3.4x to 5.1x at
10,000 and 100,000 rows.

**2. Held: the morsel-parallel compiled `Filter`.** 270: 1M rows 55.2 to 10.3 ms. Native: 15.2 to
5.29 ms (2.9x), with 2.9x at 100,000 rows and none at 10,000 (two morsels). **x64 agrees, with a
smaller gain:** 25.0 to 9.82 ms (2.5x) at 1M rows, 1.4x at 100,000 and none at 10,000, on four
physical cores against twelve.

**3. Overturned: the morsel `Derive`.** 270 found it at parity at 1M rows (36.9 to 38.2 ms, "within
noise"). Native: 22.5 to 7.37 ms (3.1x), and 2.4x at 100,000 rows. On a quiet machine it is 2.7x at
both sizes (21.1 to 7.93 ms, 1.94 to 0.717 ms). The morsel runner pays for both row-local verbs.
**x64 agrees:** 34.9 to 18.3 ms (1.9x) at 1M rows and 1.4x at 100,000 (a second sitting: 2.2x and
1.5x), so the verdict is overturned on both architectures.

**4. Held at 100,000 rows, qualified below, unsettled above: `MorselRows` = 8,192.** Swept twice,
once with the machine busy and once quiet, all four sizes per sitting. Both sittings agree on two
points:

- at 10,000 rows 4,096 leads (quiet: compiled `Filter` 0.078 against 0.123 ms, `Derive` 0.112
  against 0.167 ms), because it makes three morsels where 8,192 makes two;
- at 100,000 rows 8,192 is best or tied, and 4,096 loses 9% and 24%.

At 1,000,000 rows the compiled `Filter` is within 6% at every size. The `Derive` favoured 8,192 in the
busy sitting and 16,384 by about 20% in the quiet one, so nothing is decided there. No single constant
wins at every size. The constant does not move in this phase. A morsel size that depends on the row
count (finer for small inputs) is open to the phases that build on this record (343, 344).

**x64 (one quiet sitting of all four sizes, and a second of 8,192 and 32,768):**

- at 10,000 rows it agrees: 4,096 leads (compiled `Filter` 0.136 against 0.223 ms, `Derive` 0.236
  against 0.296 ms);
- at 100,000 rows the compiled `Filter` is tied (within 9% at every size), but **the `Derive` does
  not reproduce "8,192 best or tied"**: 32,768 beats it by 21% to 27% in both sittings (1.80 and
  1.79 against 2.46 and 2.25 ms). That is four morsels, one per physical core, against thirteen. On
  this machine 4,096 loses 15%, as it loses on Arm64;
- at 1,000,000 rows the compiled `Filter` favours 8,192 by about 10% in both sittings. The `Derive`
  read 32,768 ahead by 20% in one sitting and tied in the other (15.2 against 15.4 ms), so it stays
  unsettled on both architectures. Larger-than-8,192 morsels led the `Derive` in Arm64's quiet sitting
  and in one of x64's.

The decision holds and the x64 data strengthens it: no fixed constant wins everywhere. The best
morsel size depends on the core count as well as the row count, which is one more input for the
phases that may make it adaptive.

**5. Overturned in its reason, held in its decision: the parallel `GroupBy`.** 270: parity at 10 keys
(14.1 against 13.8 ms at 100k, 388 against 408 ms at 1M), and 2.3x and 1.8x slower at one key per
ten rows. Its stated cause was that "a grouping over this evaluator is dominated by boxing the
aggregated column". Phase 323 removed that boxing. Rebuilt from 270's description over the streamed
aggregates and measured natively, the per-morsel-tables-merged-on-first-seen shape is:

- **1.55x faster** at 10 keys and 1M rows (quiet: 3.94 against 6.10 ms; busy: 3.85 against 6.15);
- at parity at 100,000 rows (1.06x slower quiet);
- **4.0x and 7.2x slower** at one key per ten rows (quiet: 92.7 against 23.0 ms at 1M, 8.63 against
  1.20 ms at 100k; the busy sitting read 3.0x and 6.4x, because load slowed the sequential arm),
  allocating 177 MB against 20 MB.

**x64 agrees** on four physical cores: **1.63x faster** at 10 keys and 1M rows (7.86 against
12.8 ms); 1.38x slower at 10 keys and 100,000 rows, where Arm64 was at parity; and **3.7x and 6.2x
slower** at one key per ten rows (140.0 against 37.7 ms at 1M, 23.0 against 3.68 ms at 100k), with
the same allocation.

So threads now buy something, but this shape spends it on merging tables that hold nearly every row.
It also reassociates a float `Sum`, which moves wire bytes. It stays unshipped. What carries forward
to Phase 344 is the shape the numbers point to: partition by group, so each thread owns whole groups,
folds them in row order, and no per-morsel table is ever merged.

**6. Held: vectorised integer reductions are not built.** It was never a timing. Every aggregate
still folds scattered physical rows into per-slot accumulators (`GroupAgg.Stream.FeedAll`), so no step
reduces a contiguous typed range. The architecture does not bear on it.

**Supporting change.** The benchmark assembly is the third that may see the dataframe package's
internals (`InternalsVisibleTo`), so the harness can run both members of the kernel pair through
`evalPreparedCountedWith` / `evalStepWith`. It is never packed, and no public surface moves.

## 2026-10-03 — D6: the corpus copy of the transform laws is held at its published stamp until it lands with the first host that reads the new vectors

**Decided (fuaran-core#356), measured rather than assumed.** The shared wire-format corpus carries a
declared copy of `conformance/laws/transform-laws.json` (D1 (6)). That copy is stale: `kitVersion`
0.35.0, 16 `evalPipeline` vectors, in the pre-rename names. The copy is NOT re-published on its own.
It lands in the same change-set as the first host that reads the new vectors, which is fuaran#2001
(the host twins learn the exact decimal and the rounding vocabulary). Until then the record in
[`copies.json`](copies.json) carries a `lag` naming that phase, so the workspace copy registry
reports a lag with an owner rather than an unowned stale copy.

**1. What was measured (2026-10-03).** Emitted from a detached checkout of the `v0.37.0` tag, the
file is byte-identical to the one committed at that tag: 60 vectors, of which 47 are `evalPipeline`
(16 drawn shapes, the decimal shapes from iteration 16, the derive-typing shapes from D5), 9 are
`columnOp` and 4 are `delta`. The released 0.37.0 file and this repository's 0.38.0 draft differ in
the `kitVersion` stamp alone. Run against that file, the one host that certifies against the family
(fuaran-ts, `packages/ops/test/transformLaws.test.ts`) passed 15 vectors, failed 21 and could not
decode the pipeline of 26:

- it has no `decimal` column type, so every decimal source is refused at decode;
- it has no `quotient`/`rounded` expressions and no `intersect`, `except` or `countDistinct`, so
  those pipelines do not decode;
- it has no `ColumnOp` or delta codec, so the `columnOp` and `delta` cases cannot be run at all;
- two vectors that predate the decimal (`transform-5-div-by-zero`, `transform-13-div-by-zero`) now
  expect D5's typing of an all-null derived column, a numeric column of nulls rather than a string
  one, and the host still produces the old answer.

So the work the host needs is the decimal strand itself (D72 of the substrate, carried here by
Phases 276, 277, 321 and 338), not two new case kinds. fuaran#2001 already owns that port for all
four language hosts, and its own first task is this re-emit.

**2. Why the copy is not published ahead of the host.** Publishing the copy alone turns a public
host's gate red for a change that host did not make, 21 failures at once. Marking the vectors the
host cannot evaluate as skipped would make that gate green while certifying none of them. Neither
is acceptable, so the copy waits for the host.

**3. Who reads the family today.** fuaran-ts only. fuaran-go, fuaran-py and fuaran-rs each carry an
evaluator twin but no transform-laws leg (fuaran#2001 adds one to each). The .NET host evaluates
with this repository's packages, so it has no twin to certify. The bundled corpus snapshots in
fuaran-ts and fuaran-py do not carry `laws/`, so a laws-only corpus commit changes neither payload.

**4. Not decided here: release stamp or draft stamp.** fuaran-core#356 asked for the copy to be
emitted at a released version, never an untagged draft, and the measurement in (1) was taken that
way. D1 (6) and `version-derives.json` say a version cut re-emits the copy with the stamp, and the
copy registry compares the copy with this repository's committed file. Those two rules disagree
whenever a draft is ahead of the last release, as `0.38.0` is ahead of `v0.37.0` today. Then a copy
emitted at the release differs from `conformance/` in the stamp alone. This suite's freshness leg
reports that as `STAMP ONLY`, and the registry reports it as stale. The vectors are identical either
way. Which stamp the published copy follows is left to fuaran#2001's landing, and is recorded here
so that the choice is made deliberately.

**4 decided (2026-10-03, operator ruling): the published copy follows the RELEASE stamp.** The shared
corpus is public, and the hosts that certify against it should certify against a contract a consumer
can actually restore; an untagged draft's stamp names a version nobody can obtain. So:

- the copy is re-emitted when a version is **released** (tagged and published), not when a draft is
  cut, and carries that release's `kitVersion`;
- a copy whose stamp is this repository's **latest release** and whose vectors match is **fresh**, even
  while a draft is ahead of that release; the copy registry and this suite's freshness leg are to read
  it that way, rather than comparing with the committed draft file;
- the declared derivation in `version-derives.json` moves from the cut to the release accordingly, once
  the workspace copy registry and version tooling support it.

Until that support ships, a release-stamped copy still reads `STAMP ONLY` here and stale in the
registry; the `lag` in `copies.json` covers the present gap, owned by fuaran#2001.
The lag is kept deliberately (operator, 2026-10-03): it turns an unowned stale record into an honest one
with an owner.

## 2026-10-02 — D5: a derived column is typed by its expression; the cells decide only where the typer cannot; a float beside a decimal is refused

**Decided (Phase 338, carrying D3's last paragraph).** One rule types every column the dataframe
strand derives — a `Derive`'s, and an `Unpivot`'s `value` — and every reader takes it from one
place (`DataFrame.derivedTyping`, `unpivotTyping`, `columnTypeBy`): the evaluator, the incremental
seam's walk and its chunked path, `SchemaWalk`, the planner and `PipelineQuery.check`. So refresh
and full, the walk and the evaluator, cannot disagree about a type by construction.

**1. The rule.** Over the step's schema, before any row:

- the expression's own arms (through `Case` and `Coalesce`, the nodes whose answer IS an operand's)
  carry a decided `float` and a decided `decimal` → **refused**, on every frame, by name;
- the typer decides it (`Of ty`) → `ty`, on every frame — an empty one, an all-null one;
- the typer knows it produces no present value (`Absent`) → `string`, the type an all-null
  column has always had;
- the typer cannot decide (`Unknown`) → **the cells decide**: Phase 321's widening join of the
  present cells' types (D3 rule 2), `string` where none is present, and a refusal over the whole
  column where the cells hold a `float` beside a `decimal`.

An unpivot reads the same rule with its value columns as the arms, joined by the widening join of
their DECLARED types: no totality verdict reads an unpivot's type as an exactness claim, so the
schema decides an `int`/`float` or `int`/`decimal` melt, and only a pair no widening relates (a
`string` beside an `int`) is left to the cells.

**2. A cell is admitted, never converted.** Under a decided type every present cell is of that type
or widens into it by `ColumnType.widens` (an `Int` in a `float` or `decimal` column); it is stored as
produced, as D2 stores an edited cell. A present cell the decided type does not admit would be the
typer and the evaluator disagreeing: `DeriveTypingConformance.laws` goes red on it (its go-red is in
the suite), and the evaluator keeps the cell as produced rather than coerce it.

**3. The one data-decided remainder.** A `Param` (its cell is the arguments'), a `Now` (the clock
witness's), a column the schema in hand does not carry (an open schema's), and a join the EXACT
typer keeps apart — a `Case` of an `int` and a `float`, a `Coalesce` of a `decimal` and an `int`.
The last is wider than the phase's shard assumed: it was written expecting the typer to join `int`
and `float` at `float`, which D3 had declined (the join is an exactness claim the totality verdict
reads). The rule here does not widen the typer either; those shapes stay the cells', named as such
in `PipelineQuery`'s `TypeUndecidable`, and the planner declines to move a filter past them.

**4. The verdict follows.** `Plan.isTotal` calls a derive total only where its expression is total
AND its column is decided: a cells-typed derive can refuse a float beside a decimal over the whole
column, and a statically refused one always does, so neither may be called total. That is also what
makes a decided derive of ANY type safe for `FilterBeforeDerive` (its type reads no row), so the
planner's string-only clause is gone. `proofs/Pipeline.fst` models the rule clause for clause —
the typer is now extracted, because `eval_derive` reads it — and `verdict_sound`, `step_total`,
`derive_then_filter` (now over a decided type) and the new `derive_type_rows_free` (a decided
derive's column has one type over any two frames of a schema) verify.

**Declined: converting a widened cell.** Writing an `Int` into a float column as `Float` would
make the column's carrier uniform, and would make the type change a VALUE change every host must
mirror; storing as produced keeps the vectors' cells byte-identical and matches the substrate's own
admission.

## 2026-10-02 — D4: the packages take their own ids — `Fuaran.Compute.*` and the `Fuaran.Compute` namespace — opening at `0.36.0`; the `Fuaran.Core.*` ids stop at `0.34.0`

**Decided (Phase 322, superseding D1's first ruling).** D1 kept the ids and namespaces the strand
shipped with, on the argument that a rename is a source change in every consumer for no gain. The
gain turned out to be real and the cost of not paying it recurring: a reader of `Fuaran.Core.DataFrame`
is told by its name that the dataframe is a property of the substrate, which the substrate's own
boundary (its D51: the compute layer is the witness-free strand, not a Core property) says it is not,
and every review of the split had to explain the mismatch again. A pin is a bad reason to keep a name
that says the wrong thing. The name follows the producer.

**1. The mapping.**

| Through `0.34.0` | From `0.36.0` |
|---|---|
| `Fuaran.Core.DataFrame` | `Fuaran.Compute.DataFrame` |
| `Fuaran.Core.Column.Ops` | `Fuaran.Compute.ColumnOps` |
| `Fuaran.Core.DataFrame.Conformance` | `Fuaran.Compute.Conformance` |
| `Fuaran.Core.DataFrame.PipelineQuery` (Phase 281; never published) | `Fuaran.Compute.PipelineQuery` |

The fourth row is the package Phase 281 added after D1; it is renamed with the others, so its first
publication is under the new id. The C# facade (`Fuaran.Core.DataFrame.CSharp`) has no row: it was
deleted outright beside the substrate raise to `0.33.0` (the `0.36.0` STABILITY entry on its removal),
so there is no `Fuaran.Compute.CSharp`.

**2. One namespace, `Fuaran.Compute`; the module names inside are unchanged.** Every source file
declares `namespace Fuaran.Compute` and opens `Fuaran.Core` for the substrate types it is built over.
`DataFrame`, `ColumnOps`, `Transform`, `Incremental`, `Delta`, `Plan`, `DataFrameConformance` and the
rest keep their names, so a consumer's change is its package references and one `open` per file.
The pre-split forwarding module moves with its namespace: `Conformance.<family>` still resolves for a
file opening both namespaces, the fully qualified `Fuaran.Core.Conformance.<family>` does not.

**3. The new ids open at `0.36.0`, the `0.35.0` draft ADVANCED rather than reclassed.** `0.35.0` was
untagged and unpinned, so it could have been reclassed in place; but the draft rule advances a slot
that takes a change of a higher class than it carries, and the first version under new ids should be
a fresh contract rather than a number whose entry already described additions to the old ones. The
`0.35.0` entry's content is folded into the `0.36.0` entry, which says `0.35.0` was never released.

**4. The old ids stop at `0.34.0`, with no deprecation package.** They are not republished; nuget.org
keeps every published version restorable, which is all a consumer that has not moved needs.

**5. What does NOT change.** The repository (`fuaran-core-compute`), its organisation, the
solution and the unshipped test, benchmark and proof-oracle projects keep the repository's name
(`Fuaran.Core.Compute.*`) — the repository is not renamed and none of those projects is a package —
and so does the claims ladder's subject. The wire format names no CLR type, so no canonical byte moves:
each `api/wire/` baseline's body is byte-identical to its predecessor's.

**6. How the move is classed.** The `Public surface` and `Wire surface` families compare each
package with its baseline at the newest tag; a tag cut before the rename holds the baseline under the
old id, so both read through a declared predecessor map (`PublicSurfaceTests.predecessorIds`) rather
than reporting a first snapshot. That is what makes the rename read as the `removal` it is, and what
keeps the decimal's wire `breaking` since `v0.34.0` stated across the rename instead of reset.

**7. The registration is an operator act.** nuget.org Trusted Publishing policies are scoped to
package ids, so the policy for this repository must be extended to the `Fuaran.Compute.*` ids before
the first `v*` tag under them; a failed `NuGet/login` on that tag is the missing registration, not an
authentication fault (the publish workflow's header says so where the failure is met).

## 2026-10-02 — D3: one float order, the substrate's; a derived column's type is the join its cells widen into, and the static typer stays exact

**Decided (Phase 321).** Three rulings about how the evaluator types and orders what it computes,
taken together because each is the decimal's arrival exposing an older looseness.

**1. The evaluator orders floats as the substrate does: `NaN` one value ABOVE every other.** The
evaluator compared floats with the host's `compare`, which puts `NaN` below every value on .NET and,
under Fable, answers `1` for both `compare nan 1.0` and `compare 1.0 nan` — an order on one host and
not an order on the other. The substrate's `Cell.compare` (its Phase 315) puts `NaN` last, and
`Column.aggregate`'s `Min` / `Max` already read that order, so a descending sort and a `Max` named
two different largest values. `Kernels.compareFloat` now states the order once; the sort kernel,
the compiled comparison arm and both comparison kernels (the portable loop and the vector one, whose
`NaN` lanes moved from `<` / `<=` to `>` / `>=`) read it. A float filter `x > k` now keeps a `NaN`
row and `x < k` drops it; a sort puts `NaN` after `+Inf` and before the nulls. Recorded as breaking.

**2. A derived column's type is the join of its present cells' types under `ColumnType.widens`.**
It was the type of the first present cell, so a `Case` answering `Int 1` on one row and `Float 2.5`
on the next built an int column holding a float — a table the substrate's `Table.validate` refuses,
and whose `Sum` its Phase 299 turns into an error. Now `Int` and `Float` join at `Float`, `Int` and
`Decimal` at `Decimal` (the common money shape: `Coalesce(amount, Lit(Int 0))`). A pair no widening
relates keeps the EARLIER type, which is the pre-321 answer for that pair, `Float` beside `Decimal`
included. **Declined: refusing `Float ⊔ Decimal` by name at a derive.** It would add a whole-column
refusal to `Derive`, which the planner's totality verdict (proved sound over the evaluator in
`proofs/Pipeline.fst`) does not model: a derive the verdict calls total could then fail, and the
reorder that verdict licenses could change which error a pipeline reports. That is a change to the
verdict and its proof, and it is left to a phase of its own rather than half-made here. The model
moved with the rule: `Pipeline.fst`'s `infer_type` is the same fold, re-verified and re-extracted.

**3. The static typer's join stays EXACT.** The phase that brought rule 2 also asked for
`Typing.join` to widen. It does not, because `Of t` is an exactness claim the totality verdict reads:
`Of IntType + Of IntType` is not total (two ints can overflow), and a `Case` of an int and a float
typed `Of FloatType` would make the same addition read as total while both operands are ints at run
time. So `Int ⊔ Float` and `Int ⊔ Decimal` stay `Unknown` in the typer, and `DataFrame.typeOf` answers
`None` for them — undecided, never wrong.

**Also recorded: the law vectors carry the two wires (operator ruling, 2026-10-02).**
`conformance/laws/transform-laws.json` is this repository's derived file, and Phase 277 had already
appended its decimal `evalPipeline` vectors to it, so decimal rows through the columnar op wire and
the delta wire are the same act. They are appended AFTER the 39 `evalPipeline` vectors, which are
byte-identical, as two further case kinds: `columnOp` (a source table and an op; the result table or
a refusal, and the op's re-encoded wire string) and `delta` (two tables and a key column; the
encoded delta). `iterations` still counts the `evalPipeline` vectors only. The hosts' copies of the
file follow the 2026-09-20 ruling: they stay as they are until each host raises, and meet the two
kinds then. None of the existing vectors moved under the rulings above.

**Carried to Phase 338, not built here (operator ruling, 2026-10-02).** Refusing a `Float` beside
a `Decimal` at a derive, and typing a derive by its expression rather than by its cells, move with
the rewrite of the totality verdict and `Pipeline.fst` that phase makes; rule 2 above is what this
phase ships in the meantime. (Built by Phase 338: D5. Rule 2 still types a column only its cells
decide, and its "earlier type" for a float beside a decimal is replaced there by the refusal.)

## 2026-10-02 — D2: FS0025 is an error here, and `cellFits` widens exactly as `ColumnType.widens` does

**Decided (Phase 321; the `cellFits` half is an operator ruling of 2026-09-30).**

**1. An incomplete match is a compile error in every project but the proof oracle.**
`Directory.Build.props` escalates FS0025. The strand matches over the substrate's closed unions
(`Cell`, `ColumnType`), which gain cases on a raise; as a warning, an arm blind to the new case
compiled and threw `MatchFailureException` on the first value of it. As an error, the raise itself
names every arm that must move. The proof oracle is exempt by project name: it is extracted F\*
code, total by proof rather than by syntax, and it keeps its own `NoWarn` set. **The dry run this
phase was asked to record:** with FS0025 an error, the repository at Core `0.33.0` built with ZERO
errors — the substrate's raise (Phase 277) had already given every exhaustive arm its `Decimal` case,
so the escalation now guards the next raise rather than closing a list on this one.

**2. `ColumnOps.cellFits` accepts a cell whose type widens into the column's.** It was exact type
equality. The substrate's codec decodes a JSON int into a decimal (and a float) column, and its
`Table.validate` checks every cell through `widens`, so a strict edit check refused cells the
substrate's own wire produces and its own validator accepts: a table valid on one side of the
boundary failed an edit on the other. Now an `Int` fits a `FloatType` or `DecimalType` column, and
nothing else widens (`Float` into decimal and decimal into float are refused by name). **The cell is
stored as given, never converted**, so `invert` restores the previous cell verbatim and its round
trip is exact as before. **The strict reading is declined**, for the reason above. `proofs/ColumnOps.fst`
moved with it: the model gained `DecimalType` and a `widens`, `cell_fits` reads it, every theorem
re-verified unchanged, and the oracle differential now draws decimal columns (it could not before).
`cellToJson` writes the canonical decimal text, so a cell built by hand as `1.50` encodes as `1.5`,
the spelling the decoder reads.

## 2026-09-26 — D1: the compute strand is cut from Fuaran.Core into this repository under the same ids; it opens at `0.33.0`, and the forwarding module goes when the substrate removes the strand

**Decided (Phase 259, carrying out the Fuaran.Core repository's D66).** The substrate's D66 ruled
that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` are produced by a repository of their own,
together with the dataframe law families, the dataframe half of the C# facade, the two proof models
that cover them and ownership of the transform law vectors, while `Fuaran.Core.Column` and every
other spine package stay. Its reason, in one line: the substrate's programme is stability, laws and
proofs, and it should break almost never; the compute layer's is a performance programme, and it
will break repeatedly — one `<Version>` over both would force either false breaks on the spine or
held-back compute. That entry, and the substrate's D68 (how the boundary was prepared inside it
first, so this cut is a copy of whole assemblies), are the record of WHY. This entry records what the
cut is.

**1. Same ids, same namespaces.** The four packages keep the ids and the CLR namespaces they shipped
with. A consumer raises a version and changes nothing else; nothing here renames anything, and a
rename would be its own breaking decision.

**2. History carried, not re-created.** The moved paths were carried with `git filter-repo` from a
clone of the substrate at `v0.32.0`, so `git log --follow` on a moved file reads its whole history.
Files this repository needed but the substrate keeps — the test infrastructure, the C# proof legs
shared with the substrate's facade proof, the proof-leg kit — are copies; the kit files are declared
in [`copies.json`](copies.json) so a drift between the two is named rather than discovered.

**3. `0.33.0`, the next minor above the substrate's last emission of the ids.** The substrate
released the four ids at `0.32.0`. Opening here at `0.33.0` keeps every consumer's version floor
monotone across the change of producer: no version number ever names two contracts. It is a DRAFT
until it is tagged.

**4. The substrate by package, at one pin.** The five substrate packages the strand stands on
(`Column`, `Wire`, `OpStream`, `Conformance`, `CSharp`) are package references at ONE property,
`FuaranCoreVersion`, restored from the public registry; the `Compute boundary` family refuses a
sixth, a project reference to the substrate, and any package outside the allowed set.

**5. The pre-split spellings go when the substrate removes the strand (Phase 258).**
`Fuaran.Core.DataFrame.Conformance` carries `Forwards.fs`: a module named `Fuaran.Core.Conformance`
whose members forward to `DataFrameConformance`, so code written against the pre-split
`Conformance.<family>` spellings kept compiling after the substrate's Phase 257. It exists for the
window in which both producers can ship the ids. It is removed in the change-set that answers the
substrate's Phase 258 — the phase that removes the strand from the substrate, on a breaking draft
whose entry names this repository and the version the ids continue at. Removing it is a breaking
change here too, so it advances this repository's draft slot rather than riding it.

**6. The transform law vectors are this repository's derived file.** `conformance/laws/transform-laws.json`
is emitted by `--emit-laws` from this repository's evaluator and stamped with this repository's
`<Version>`, and [`version-derives.json`](version-derives.json) declares the shared wire-format
corpus copy as derived from it. Until the substrate removes the strand, both repositories can emit
the file; the vectors are byte-identical and only the `kitVersion` stamp differs (`0.32.0` there,
this repository's version here), so the corpus copy can agree with only one of them. For that window
this repository's CI compares against the corpus and PRINTS a drift rather than failing on it (it
does not set `FUARAN_CORE_CORPUS_FRESHNESS`): made fatal on both sides, one producer's CI would be
red on every push for a byte neither can change. The corpus copy is re-emitted from here, and the
flag set in this repository's CI, in the change-set that answers the substrate's Phase 258 — the
point at which this repository becomes the only producer.
