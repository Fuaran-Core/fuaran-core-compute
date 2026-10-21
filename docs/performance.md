# Compute performance notes

Measurements that decided a design question in this repository, each with the bar it was held to.
The raw benchmark runs of earlier phases live under [`../benchmarks/results/`](../benchmarks/results/);
this document records the decisions they fed.

## Decimal in the dense frame (Phase 280)

A `Decimal` cell carries exact canonical text (Core `DECISIONS.md` D72 K1). The dense frame packs a
decimal column boxed: no typed vector, every kernel reading cells through the reference arm, and
`Sum` adding text digit by digit. The candidate was a scaled-integer vector behind the `Table`
boundary: one unscaled integer per row and one scale per column, carried in a float64 and guarded at
2^53 so it is exact and fast on both hosts, falling back to the text path by name when a value does
not fit.

### The bar, stated before any vector was written

The question is whether the text carrier costs, on a hot path, what a typed vector would remove. The
float arm of the same pipeline over the same values is the reference: a float64 carrier is exactly
what the candidate vector holds, so the float arm is what the decimal arm could at best become.

- **Premium** = the decimal arm's median divided by the float arm's, per verb, size and host.
- **Build** if, on either host, the premium is at least **2.0** for at least **two of the four
  verbs** (filter, group-and-sum, sort, join) at **both** 10,000 and 100,000 rows.
- **Decline** otherwise, with the numbers recorded here.

The 1,000-row figures are recorded and read by no rule: at that size a fixed per-call cost, not the
carrier, decides the figure.

The benchmark is the typed family in [`../benchmarks/Fuaran.Core.Compute.Benchmarks/Corpus.fs`](../benchmarks/Fuaran.Core.Compute.Benchmarks/Corpus.fs):
`id:int, grp:string, v:<type>` with the same values (`h` hundredths, a permutation of `0 .. 99,999`)
carried as a decimal (`"791.19"`), a float (`791.19`) and an integer (`79119`). Filter is `v > 500`,
about half the rows; group-and-sum folds 50 `grp` values; sort is ascending on `v`; join is inner on
`v` against a reordered copy, one match per row. Every case is checked before it is timed: its output
row count, and for the decimal group-and-sum every total equal to the integer arm's exact sum.

### The baseline: the text path

Milliseconds per `DataFrame.evalPipeline` call over the `Table` (the boundary paid on every call), the
median of ten samples after two warm-up calls, each sample calibrated to span at least 50 ms. The
.NET figures are the best of two paired, interleaved runs (baseline tree, this tree, baseline, this
tree) on a machine other work was also using; the node figures are one run each. The premium is
the decimal arm over the float arm of the same run.

| Verb | Rows | .NET decimal | .NET float | .NET int | .NET premium | node decimal | node float | node int | node premium |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| filter | 1,000 | 0.718 | 0.113 | 0.109 | 6.4 | 10.0 | 1.12 | 1.72 | 9.0 |
| filter | 10,000 | 10.43 | 1.30 | 1.49 | **8.0** | 142.0 | 18.4 | 12.8 | **7.7** |
| filter | 100,000 | 50.8 | 36.1 | 24.8 | 1.4 | 1,401 | 244 | 240 | **5.7** |
| group-and-sum | 1,000 | 1.35 | 0.110 | 0.114 | 12.3 | 19.9 | 1.92 | 1.42 | 10.3 |
| group-and-sum | 10,000 | 10.12 | 1.19 | 0.90 | **8.5** | 188.5 | 10.4 | 15.1 | **18.2** |
| group-and-sum | 100,000 | 96.6 | 15.6 | 12.4 | **6.2** | 1,831 | 89.0 | 98.5 | **20.6** |
| sort | 1,000 | 6.21 | 0.354 | 0.344 | 17.6 | 74.5 | 3.75 | 5.19 | 19.9 |
| sort | 10,000 | 107.4 | 5.47 | 8.00 | **19.6** | 1,566 | 95.5 | 98.5 | **16.4** |
| sort | 100,000 | 1,404 | 129 | 204 | **10.9** | 21,133 | 1,380 | 1,548 | **15.3** |
| join | 1,000 | 1.64 | 2.10 | 1.69 | 0.8 | 18.4 | 17.6 | 17.2 | 1.0 |
| join | 10,000 | 50.6 | 60.0 | 58.3 | 0.8 | 202 | 199 | 258 | 1.0 |
| join | 100,000 | 603 | 594 | 614 | 1.0 | 2,179 | 1,924 | 1,968 | 1.1 |

**Decision: build.** The bar is met on both hosts: group-and-sum and sort clear 2.0 at 10,000 and
100,000 rows on .NET, and filter, group-and-sum and sort do on node. Join does not. Its decimal key
is hashed and compared as canonical text, which is already string equality, so no join kernel was
written. Where the time went on the text path: a sort compared two texts by `DecimalText.compare`,
re-reading both, at every comparison; a sum added text digit by digit per row; a filter compared
text with the constant per row.

Found while measuring: on node, every decimal `Sum` over the streamed `GroupBy` answered the empty
text. Its per-group totals were a string array made by `Array.zeroCreate`, which Fable fills with
`""`, not `null`, so the "no decimal yet" test never held. Fixed in this phase (`Array.create … null`).
The benchmark corpus's own check caught it; the suite runs on .NET only.

### What was built

`Decs` in `Frame.fs`: a decimal column whose every present cell is well-formed decimal text, and
whose values all fit **fifteen significant digits at the column's one scale** (the largest fraction
length in the column), carries each value as an unscaled integer in a `float[]`. That is exact,
because every such integer is below 10^15 < 2^53. The cells it was read from ride beside it with a
validity mask, so every read of a cell is the cell the text path reads, byte for byte, and nothing is
rendered back. Any other decimal column is packed boxed, exactly as before: the fall-back is a path,
not a refusal.

The kernels that read the integers:

- **filter**: a comparison of a carried column with a decimal constant that reads at the column's
  scale, or with an int constant whose scaled value stays within 2^53, is the float comparison kernel
  (`CmpFloats`, both members of the kernel pair). A constant finer than the column's scale, or one
  too large at it, is handed back to the compiled path.
- **sort**: the key comparator compares the integers.
- **sum**: the streamed `GroupBy` `Sum` keeps each group's total in the float carrier while its
  magnitude stays at most 2^53 - 1, a test that cannot pass in error: an exact sum within the bound
  computes exactly, and one past it computes past it. The first addition that would leave the bound
  moves that group to `DecimalText` addition for the rest of the fold.

Everything else over a decimal column (expressions, joins, grouping on a decimal key, windows,
`Min` / `Max` / `Mean`) reads the cells, as the boxed vector did. No public signature and no wire
byte moved. `Vec.setAt`, `append` and `concat` keep the vector typed, packing again from the cells
where two scales meet or an edit does not fit.

The law (`tests/Fuaran.Core.Compute.Tests/DecimalVectorLaw.fs`) runs over 400 generated tables, 21
pipelines each (8,400 comparisons). The pipelines are filters with decimal and int constants on
either side (finer constants and int constants past 2^53 among them), a grouped `Sum` / `Count` /
`Min` / `Max` / `First` / `Last`, an ungrouped sum, sorts with ties, a top-n, a join on the decimal,
a group-by on it, a derived decimal sorted, and a running total. For each, the answer over a carried
column equals the answer over the same values on the text path as canonical wire bytes. Values reach
fifteen significant digits, so group totals spill past 2^53 mid-group, and about one seed in six puts
the column past the width, so both sides are text. The law runs in the suite on .NET, which also
asserts it is not vacuous (the carried column is a `Decs`, its twin is boxed). It also runs in the
node benchmark harness under Fable before anything is timed: 8,400 pipelines, equal, on both hosts.
Perturbed once to check it has teeth (a sum rendered one unit high, a filter constant one unit off),
it fails.

### The re-run: the vector path

| Verb | Rows | .NET decimal | .NET float | .NET premium | .NET speed-up | node decimal | node float | node premium | node speed-up |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| filter | 1,000 | 0.148 | 0.103 | 1.4 | 4.9x | 1.70 | 0.98 | 1.7 | 5.9x |
| filter | 10,000 | 2.38 | 1.35 | 1.8 | 4.4x | 20.9 | 17.7 | 1.2 | 6.8x |
| filter | 100,000 | 33.6 | 35.1 | 1.0 | 1.5x | 241 | 253 | 1.0 | 5.8x |
| group-and-sum | 1,000 | 0.186 | 0.107 | 1.7 | 7.3x | 2.38 | 1.50 | 1.6 | 8.4x |
| group-and-sum | 10,000 | 1.93 | 1.16 | 1.7 | 5.2x | 27.0 | 18.9 | 1.4 | 7.0x |
| group-and-sum | 100,000 | 28.6 | 16.8 | 1.7 | 3.4x | 195 | 126 | 1.6 | 9.4x |
| sort | 1,000 | 0.754 | 0.632 | 1.2 | 8.2x | 7.38 | 5.88 | 1.3 | 10.1x |
| sort | 10,000 | 10.78 | 6.49 | 1.7 | 10.0x | 99.5 | 94.0 | 1.1 | 15.7x |
| sort | 100,000 | 148 | 171 | 0.9 | 9.5x | 1,247 | 1,202 | 1.0 | 16.9x |
| join | 1,000 | 1.99 | 1.66 | 1.2 | 0.8x | 14.9 | 10.8 | 1.4 | 1.2x |
| join | 10,000 | 58.0 | 57.9 | 1.0 | 0.9x | 185 | 145 | 1.3 | 1.1x |
| join | 100,000 | 708 | 660 | 1.1 | 0.9x | 2,135 | 2,153 | 1.0 | 1.0x |

The speed-up is the decimal arm's baseline figure over its figure here. Every premium is now under
2.0, on both hosts, at every size. What remains on filter and group-and-sum is the boundary scan (a
`Table` handed to `evalPipeline` is read once per call, and a decimal column's texts are parsed
there) plus, for the sum, one rendering per group. The join is unchanged within the machine's noise
(its float arm moved as much): it reads cells as before, and pays the scan on both sides.

The tick family has no decimal column; a non-decimal vector meets one more pattern case and nothing
else. Measured on this tree, Release, at 1,000 rows: `filter > groupBy` 0.92x, `filter > sort > limit`
1.02x and `group tail` 0.88x against the standing 1.6x bound, and every corpus node's 1,000-row tick
at most 1.25x (`window CumulSum` and `pivot`).

Instrument: .NET 10 (SDK 10.0.401) Release, and node v22.20.0 running the harness compiled by Fable
5.0.0 as `run-node.ps1` compiles it; the node harness's own timer
(`benchmarks/Fuaran.Core.Compute.Benchmarks/Node/Program.fs`, second argument `typed`) on both hosts;
a Snapdragon X Elite (X1E80100). Core D72 K1 (the carrier is text) stands: the hot-path cost the
measurement found is one a typed vector behind the `Table` boundary absorbs.

## The tick bound after the boundary became a view (Phase 439)

Phase 423 made the `Table` boundary a view, and the clock leg's tick bound (`tick <= 1.6 x full + floor`)
went red on ticks that had not got dearer. Before choosing a new bound, the corpus ticks and full
evaluations were re-measured on the trees either side of 423. The measurement confirmed 423's figures
for the edit tick and refuted them for the append tick. It also found that the refresh now costs more
than the full evaluation on three shapes.

**Instrument.**

- The gate's own clock leg (`--clock-leg`), .NET 10, Release, on a Snapdragon X Elite (X1E80100, Arm64),
  calibration baseline 4.91 ms in every leg.
- The post-423 tree (`bb368ab`) was measured in two full legs, and the pre-423 tree (`ccbddc7`, exported
  and built in Release against Core 0.36.0) in three. The trees were interleaved.
- Each figure is the best counted reading over the legs, in ms per call. A window the saturation guard
  discarded is not a reading.
- The split into `Delta.diff` and the refresh comes from two further legs per tree, with the two timed
  apart in the corpus cases (the counted windows only).

**The edit tick: the full fell, the tick did not rise.**

| Node | Rows | Tick pre → post | Full pre → post | Ratio post |
|---|---:|---:|---:|---:|
| `lines` | 20,000 | 1.51 → 1.67 | 1.60 → 0.54 | ×3.1–4.5 |
| `lines` | 100,000 | 8.22 → 6.84 | 8.22 → 2.43 | ×2.6–3.5 |
| `window CumulSum` | 20,000 | 1.24 → 1.45 | 1.23 → 0.42 | ×3.3–4.3 |
| `window CumulSum` | 100,000 | 6.10 → 4.92 | 10.42 → 2.06 | ×2.1–3.3 |
| `group-by high-card` | 100,000 | 8.14 → 8.89 | 10.44 → 3.56 | ×2.4–2.5 |
| `inner join` | 20,000 | 3.60 → 1.31 | 5.75 → 0.80 | ×1.6–1.7 |
| `filter > groupBy` | 100,000 | 4.18 → 2.16 | 3.69 → 1.68 | ×1.2–1.3 |
| `sort two keys` | 100,000 | 10.32 → 2.96 | 13.42 → 6.96 | ×0.4–0.5 |

On each cell's best reading, every edit tick was within 1.17× of its pre-423 cost, and most were well
below it. Every pre-423 ratio was at most 1.53. The best reading hides one cell. Across all the Phase 439
legs, `lines` at 20,000 rows read 1.30 to 1.59 ms before 423 and 1.39 to 5.27 ms after (median 2.4 ms
over 34 counted readings, in two clusters around 1.6–2.1 and 2.6–3.6 ms): a real 1.3–2× regression with
a tail. It is the one cell the operator excepted from the baseline arm, held to its own post-423 cost
until Phase 448 (`docs/incremental-evaluation.md`).

**The append tick: the diff doubled.** The append ticks of the grouped nodes were 1.4 to 2.0 times
their pre-423 cost:

| Node | Rows | Pre-423 → post-423 |
|---|---:|---:|
| `byRegion` | 100,000 | 3.33 → 5.31 ms |
| `filter > groupBy` | 20,000 | 0.64 → 0.96 ms |
| `group-by high-card` | 100,000 | 8.07 → 11.77 ms |

Timed apart, the append's `Delta.diff` was 1.6 to 2.9 times its pre-423 cost on every node (`byRegion`
at 100,000: 1.72 → 4.21 ms). Its refresh was at or below its own.

The cause was the prefix walk. It had tried a cell reference check before the value comparison, and
under 423 it compared every row through a closure. Phase 439 restored a one-pass typed comparison of
the prefix (`ColumnRead.samePrefix`), and the grouped append ticks went to 0.46 to 0.88 of their
pre-423 cost. `docs/incremental-evaluation.md`, "The tick bound reads its own baseline", has the bound
the operator ruled.

**The finding left open: on three shapes a refresh now costs more than recomputing.** The refresh
alone, against the one-thread full evaluation it replaces:

| Node | Tick | Rows | Refresh | Full | Refresh / full | Pre-423 refresh / full |
|---|---|---:|---:|---:|---:|---:|
| `lines` | edit | 20,000 | 1.12 ms | 0.54 ms | 2.09 | 0.76 |
| `lines` | edit | 100,000 | 5.45 ms | 2.43 ms | 2.24 | 0.90 |
| `lines` | append | 100,000 | 5.27 ms | 2.46 ms | 2.15 | 0.77 |
| `window CumulSum` | edit | 20,000 | 1.42 ms | 0.42 ms | 3.41 | 0.95 |
| `window CumulSum` | edit | 100,000 | 5.61 ms | 2.06 ms | 2.72 | 0.57 |
| `group-by high-card` | edit | 100,000 | 7.64 ms | 3.56 ms | 2.15 | 0.89 |
| `group-by high-card` | append | 100,000 | 8.52 ms | 3.75 ms | 2.27 | 0.75 |

The refresh did not get dearer on these shapes; the full evaluation became a few passes over typed
vectors while the refresh still assembles its result through the cell-level path. For a cheap shape,
the table-fed diff alone is a fixed share of the full evaluation: about 1.44 ms at 100,000 rows on every
edit node, 0.6 to 0.85 of the full evaluation of `lines`, `byRegion`, `filter > groupBy` and
`window CumulSum`. No refresh, however cheap, keeps a whole-tick ratio near 1.6 there.

The bound the operator ruled admits these three shapes on their recorded tick, and `lines` at 20,000
rows on its recorded post-423 tick, not on the seam's claim. Making them cheaper than recomputing again
is Phase 448's work.

**The append tick at 20,000 rows joins it (Phase 449).** `lines`'s APPEND tick at 20,000 rows is the same
noisy shape. An alternating A/B on the Arm64 machine read it at 2.52, 2.36 and 2.39 ms on main and
2.02, 2.38 and 2.62 ms with two later phases on top. The baseline arm allowed 2.255 ms, and Phase 439's
landing gate had read it at 1.854 ms. The operator ruled it a second named exception on the first's
terms (`DECISIONS.md` D22). Its ceiling is its post-423 cost, recorded from `12a825d` as the median
over three clean full legs of each leg's best: 1.794, 1.915 and 2.196 ms, so 1.915 ms (0.388911
calibration units). The allowance is 1.5 × that plus the floor, about 3.02 ms. Making it cheaper is
Phase 448's work.

**`window CumulSum` at 20,000 rows, edit tick, and the per-cell rule (Phase 449, the sixth ruling).** The
first gate run with the append exception in place was red in its edit case on all three counted
attempts, with a different cell each time:

| Attempt | Red cell | Tick | Allowed |
|---|---|---:|---:|
| 1 | `lines` edit @ 20,000 | 3.155 ms | 2.822 ms |
| 2 | `window CumulSum` edit @ 20,000 | 2.351 ms | 1.949 ms |
| 3 | `window CumulSum` edit @ 20,000 | 1.991 ms | 1.949 ms |

No single cell was red on all three attempts. The operator ruled two changes (`DECISIONS.md` D22):

- **A case is red only when the SAME cell is red on all three counted attempts.** Several noisy cells
  can no longer redden a case together. A real regression is red every time in the cell it reached, so
  it is still caught.
- **`window CumulSum` edit at 20,000 rows is a third named exception**, one of the shapes Phase 448
  owns. Its pre-423 median is 1.201 ms, and post-423 it reads 1.45 to 2.35 ms against a 1.95 ms
  allowance. Its ceiling was recorded from `12a825d` by the same method: per-leg bests of 1.733, 1.840
  and 1.825 ms, median 1.825 ms (0.371086 calibration units), an allowance of about 2.88 ms with the
  floor.

## A refresh assembles its result from the prior one (Phase 448)

Phase 439 left one finding open: on `lines`, `window CumulSum` and `group-by high-card` the refresh
alone cost 2.1 to 3.4 times the one-thread full evaluation it replaces. Phase 448 measured where it
went, made each of the three cheaper than recomputing, and retired the three named tick exceptions
(`DECISIONS.md` D27).

**Where the refresh spent its time.** The refresh was timed apart from the diff and split by stage
(Release, Arm64, the Phase 439 tree `fc0ef74`, 20,000 rows, the corpus's one-row edit). It was not the
refreshed span: a `lines` edit re-evaluates two cells, a `group-by high-card` edit recomputes one group.
It was result assembly.

| Node | Refresh | Assembling the result | What the assembly did |
|---|---:|---:|---|
| `lines` | 0.56 ms | 0.46 ms (82%) | packed both derived columns from 20,000 boxed cells |
| `group-by high-card` | 0.82 ms | 0.60 ms (73%) | packed the 10,000-row group table from its row arrays |
| `window CumulSum` | 2.73 ms | 1.12 ms (41%), the resume 1.42 ms | built the window column as a 20,000-cell list, then packed it |

Re-keying cost nothing: the delta carries the keys the diff minted, and the refresh reads them.

**What changed.**

- **A computed column is the prior result's column, written at the rows that moved.** When a refresh
  runs in place through row-local steps alone, every other row of its result is the prior result's
  row. The column is then the prior column's typed vector, copied and written at the changed and
  appended rows (`patchedColumn`), rather than packed from every cell.
- **A maintained group table is the prior one, written at the recomputed groups** and grown by the new
  ones, by the same function.
- **A prefix-fold window's column is read straight off the typed carrier the scan wrote**
  (`runColumn`). No cell is boxed for it. The cells the state's encoding carries are read off the
  column only when the state is encoded, so no wire byte moves. The resume also writes its scanned
  suffix with one loop per carrier rather than a match per row, and builds no position table when
  the run is already in arrival order.
- **A filter that keeps every row hands its order on** rather than copying it.

Every answer is the one packing would have built. Where that cannot be shown (a carrier the copy does
not handle, or a written cell the carrier does not hold), the refresh packs the cells as before.
Making either new path write one row wrongly reddens the incremental and window law families.

**Refresh against the full evaluation, before and after.** Each "before" figure is a batched best
from a standalone probe on `fc0ef74`. Each "after" figure is the range over three clean clock legs of
the new case (15 readings).

| Node | Tick | Rows | Before | After |
|---|---|---:|---:|---:|
| `lines` | edit | 20,000 | 0.37–2.09 | 0.31–0.96 |
| `lines` | edit | 100,000 | 1.01–2.24 | 0.23–0.44 |
| `lines` | append | 20,000 | 1.04–1.12 | 0.42–0.58 |
| `window CumulSum` | edit | 20,000 | 3.03–3.45 | 0.65–0.89 |
| `window CumulSum` | edit | 100,000 | 2.11–2.72 | 0.74–0.94 |
| `group-by high-card` | edit | 100,000 | 1.55–2.15 | 0.20–0.33 |
| `group-by high-card` | append | 100,000 | 1.53–2.27 | 0.51–0.78 |

The three cells that had been excepted now pass the tick bound itself, on the ratio arm alone. On the
same three legs the `lines` edit tick at 20,000 rows read 0.49 to 0.50 ms (it had read 1.39 to
5.27 ms), its append tick 0.43 to 0.44 ms, and the `window CumulSum` edit tick 0.61 to 0.62 ms, each
within 1.6 times the full evaluation it replaces.

**Where it is not yet cheaper: the append at 20,000 rows on the small-group grouped nodes.** On
`byRegion`, `filter > groupBy` and `filter > groupBy > filter` the full evaluation at 20,000 rows costs
0.24 to 0.45 ms, and the appends refresh timed alone read 0.8 to 2.5 times it (it read 1.0 to 2.0
before this phase). The refreshs own work is not the cost. An in-place append grows the row tokens and
a steps cell cache, about 160 KB each at that size, just past the runtimes large-object threshold, and
the gen-2 collections those allocations trigger decide the reading: with the threshold raised past them
the same refresh read 0.43 times the full and the collector ran no gen-2 collection. The clock legs
refresh case therefore times the append at 1,000 and 100,000 rows; the append tick case holds the
20,000-row cells, diff and refresh together, inside its bound (`DECISIONS.md` D27).

Phase 451 closed it: the caches are chunked and the case times the 20,000-row append on every node
("A 20,000-row append refresh is held against the full evaluation", below).

## A 20,000-row append refresh is held against the full evaluation (Phase 451)

Phase 448 left the refresh-against-full case short of one size: the append at 20,000 rows, where the
small-group grouped nodes read 0.8 to 2.5 times a full evaluation of a quarter of a millisecond. It
named two causes, and both held when checked on this tree (`DECISIONS.md` D42).

**The per-row caches were large objects.** An in-place append re-allocated the row tokens, a step's
cells and the rows' groups, each one array per source row. At 20,000 rows each reference array is
160 KB, past the runtime's large-object threshold of 85,000 bytes, and is collected only with
generation 2. A probe counting the runtime's large-object allocation events over 500 appends at
20,000 rows found one 160 KB `string[]` and one 160 KB `Cell[]` per refresh on `filter > groupBy`,
`filter > groupBy > filter` and `byRegion`, and nothing else on the large-object heap. The caches are
now held in chunks of 8,192 entries (`RowChunks`, 64 KB of references). An append shares every chunk
of the prior state's caches and copies only the one it extends. An edit copies only the chunks it
writes. A step's derived column is read off its chunks by the rows a refresh walks, and is made one
array only for a step that reads it whole. The same probe then found no large-object allocation on
those three nodes. On `lines` it found one 160 KB `double[]` per refresh: the result's own computed
column, which the consumer receives, not a cache.

**The instrument measured the heap budget.** The case re-refreshed one (state, delta, source) triple,
and the collections that drew decided the reading. It now refreshes a source of its own on each call,
drawn in turn from a pool of prepared (source, delta) pairs (`ScalingTests.refreshPool`: 128 at 1,000
and 20,000 rows, 16 at 100,000). The full evaluation it is held against is unchanged.

**The A/B.** The refresh alone, the one-row append at 20,000 rows, in ms, on Arm64 in Release. The
pre-451 library is `5cd9ecc`. Each arm ran the case narrowed to the append at 1,000 and then 20,000
rows, twice, and a range includes the attempts the clock leg repeats on a red cell. The heap budget is
moved two ways: the generation-0 budget raised to 256 MB (`DOTNET_GCgen0size=0x10000000`) and the
large-object threshold raised past the caches (`DOTNET_GCLOHThreshold=0x30000`).

| Library | Inputs | Heap budget | `byRegion` | `filter > groupBy` | `filter > groupBy > filter` | gen-2 per cell |
|---|---|---|---:|---:|---:|---:|
| pre-451 | one source | default | 0.30–0.61 | 0.21–0.43 | 0.20–0.52 | 7–23 |
| pre-451 | one source | gen-0 256 MB | 0.24–0.29 | 0.17–0.45 | 0.21–0.30 | 1–14 |
| pre-451 | one source | LOH threshold raised | 0.19–0.20 | 0.11 | 0.11 | 0 |
| pre-451 | pool | default | 0.26–0.29 | 0.16–0.17 | 0.16–0.17 | 0 |
| pre-451 | pool | gen-0 256 MB | 0.21–0.22 | 0.12 | 0.12–0.13 | 0 |
| pre-451 | pool | LOH threshold raised | 0.20 | 0.11 | 0.11–0.12 | 0 |
| 451 | pool | default | 0.19–0.25 | 0.09–0.10 | 0.09–0.10 | 0 |
| 451 | pool | gen-0 256 MB | 0.18 | 0.09 | 0.09 | 0 |
| 451 | pool | LOH threshold raised | 0.18–0.19 | 0.09 | 0.09–0.10 | 0 |

One source on the pre-451 library read 0.55 to 1.88 times the full evaluation, red on a counted
attempt in every default run, and moved with either knob. The pool took the generation-2 count to
zero, but the reading still moved with the budget, 0.11 to 0.17 ms on `filter > groupBy`, because the
caches were still re-allocated whole. On this phase's library and the pool, the three budgets read
within 11% of one another on the two `filter` nodes, against 51% before. On `byRegion` they read
within 7%, but for one default reading of 0.25 ms. With one source on this phase's library the three grouped nodes held
steady too. `lines` did not: it read 0.18 to 0.56 ms, with 7 to 23 gen-2 collections, because its
refresh still allocates the result's 160 KB column. That is why the case keeps the pool.

**The case, at every size.** One clock leg of the case on this tree (Arm64, Release), the 20,000-row
append:

| Node | Refresh | Full | Ratio |
|---|---:|---:|---:|
| `lines` | 0.128 ms | 0.528 ms | 0.24 |
| `byRegion` | 0.186 ms | 0.461 ms | 0.40 |
| `filter > groupBy` | 0.092 ms | 0.266 ms | 0.35 |
| `filter > groupBy > filter` | 0.095 ms | 0.266 ms | 0.36 |
| `group-by high-card` | 0.344 ms | 0.800 ms | 0.43 |

Every edit and append cell at 1,000, 20,000 and 100,000 rows read at most 0.85 of its full evaluation
on that leg. The highest was `window CumulSum`'s edit at 20,000 rows. The main suite pins the representation itself: over every node the seam accepts, a prime and
two chained appends at 20,000 rows leave no per-row array of 85,000 bytes or more, and each append
shares every chunk of the prior caches it does not write. Making `RowChunks.grow` copy the whole cache
reddens that case, and so does copying the chunks it shares.

## A decimal vector holds no boxed cells (Phase 440)

Phase 423 made the frame a view over Core's typed vectors, but a decimal column still entered it
boxed. `Vec.ofColumn` built a `Decimal` cell per row from Core's text vector and packed them, and `Decs`
carried those cells beside its scaled integers. On the way out, `Vec.toColumn` unboxed every cell back
into a text array. It was the one place the frame held a `Cell` per row.

**The premise, checked.** These read the boxed cells:

- every cell read (`Vec.cellAt`), and the expression reader, which hands a decimal to the exact text
  arithmetic;
- the key coder (`codesOf`, joins and grouping on a decimal), through the cell's token;
- the copies (`gather`, `gatherOrNull`, `slice`, `append`, `concat`), and the edits (`setAt`,
  `sameCells`);
- the boundary out.

Every one needs only the row's text. A cell is `Decimal text`, and a token or an equality is a function
of that text. Core holds a decimal column as text in a `Vector<string>` under a validity (Phase 417),
canonical wherever `Table.validate` admits it, so the frame can hold Core's vector itself. Nothing
depends on the text being canonical. The vector holds each text exactly as it was handed in, so a
non-canonical text packed from cells reads back as itself, as before.

**What changed.** `Decs` is `scaled: float[] * scale: int * texts: string[] * mask: Mask`. Over a Core
column the texts and the mask are borrowed (`Vec.ofDecimalTexts`), and `toColumn` adopts the texts.
No text is copied and no cell is built either way, and the only allocation is the scaled-integer
vector. A cell is the `Decimal` of its row's text, made on read, as a string vector's cell is. The text
path is unchanged: a column past the width, or holding a malformed text, is boxed as before. No public
signature and no wire byte moved. A test holds the view by reference in both directions. Changing the
borrow to a copy turns it red.

**Byte-identity.** On .NET the suite's decimal law family is unchanged and green (639 of 639). That
covers the decimal-vector law's 8,400 pipelines, the transform-law vectors and the strand. Under node,
the benchmark harness runs the decimal-vector law (8,400 pipelines, vector path equal to text path)
before it times anything. It held on the tree before and the tree after.

**Instrument.**

- An Intel Core Ultra 9 386H, .NET 10 (SDK 10.0.300) Release, and node v25.9.0 running Fable 5.0.0
  output.
- The trees were `d77786a` (before) and `b56ba6c` (after), each exported and built on its own.
- The typed family is the node harness's (`Node/Program.fs`, second argument `typed`), run on both
  hosts. It was run twice per tree per host, interleaved, and the best of the two is reported.
- Held bytes and boundary cost come from a probe over the same corpus table (`id:int, grp:string,
  v:decimal|float`, 100,000 rows) through the public surface alone. It reads the heap after a full
  collection, before and after `DataFrame.prepare`, and takes the median of seven: on .NET
  `GC.GetTotalMemory(true)`, under node `heapUsed + arrayBuffers` after a forced `gc()`. The cost
  figures are the harness's median-of-ten wall clock and the best of the interleaved runs (three before,
  two after on .NET; two each under node).

**Held bytes at 100,000 rows** (the bytes a prepared frame holds beyond its table):

| Arm | .NET before | .NET after | node before | node after |
|---|---:|---:|---:|---:|
| decimal | 5,100,728 | 1,100,704 | 11,522,800 | 1,111,480 |
| float (reference) | 300,672 | 300,672 | 303,440 | 303,440 |

The float arm's figure is the three masks the frame builds for the table's `AllValid` columns. The
decimal arm now holds exactly 800,000 bytes more: its scaled integers. Before, it also held a fresh mask,
a cell array, and a boxed `Decimal` per row, about 31 bytes each on .NET.

**Decimal read cost at 100,000 rows, ms per call.** The boundary in (`prepare`) and in and out
(`toTable (prepare t)`); the two kernels that read the column's cells, a `Derive` copying it (the
expression reader) and a `GroupBy` on it as the key (the key coder); and the typed family's four verbs
through `evalPipeline`, boundary included:

| Case | .NET before | .NET after | node before | node after |
|---|---:|---:|---:|---:|
| `prepare` | 8.17 | 1.82 | 10.37 | 5.94 |
| `toTable (prepare t)` | 5.42 | 1.85 | 10.62 | 6.00 |
| derive copying `v` | 13.86 | 13.56 | 28.00 | 19.75 |
| group on `v` | 41.13 | 29.31 | 105.99 | 102.01 |
| filter `v > 500` | 14.11 | 1.95 | 16.38 | 9.63 |
| group-and-sum | 10.66 | 2.43 | 15.62 | 10.81 |
| sort on `v` | 34.40 | 19.34 | 61.00 | 54.98 |
| join on `v` | 101.23 | 47.71 | 220.50 | 201.00 |

On .NET the float arm of the same runs read 0.46 to 0.50 (filter), 0.80 to 0.89 (group-and-sum), 16.97
to 22.16 (sort) and 7.81 to 8.79 ms (join). Under node it read 3.09, 2.50 to 2.53, 51.0 and 51.0 to
51.5 ms.

**Where the time went, and what is left.** Every decimal figure fell or held. The boundary in no longer
builds 100,000 cells: it scans the texts once for the scale and once for the integers, so filter and
group-and-sum, which are mostly boundary, fell 4 to 7 times on .NET. A cell read now makes its cell. A
kernel that reads every row's cell pays one small allocation per read where it paid none, and at the
boundary saves one per row. The two even out on the derive copy on .NET and on the grouping under node, and the saving wins elsewhere.

What remains above the float arm is the text scan at the boundary. Under node that is about 5.6 ms of
the 9.6 ms filter, and on both hosts the join, whose decimal key is still coded by its canonical token
per row. That join cost is Phase 280's finding and is unchanged in kind.

## A declined shape's tick is measured for what it is (Phase 459)

Compute `v0.39.0`'s publish run read `append pivot @ 100000` at 1.79 to 1.82 times the one-thread full
evaluation on all three counted attempts of its first try, against a bound of 1.6, and passed on its
second. Both cells that flap on the corpus tick cases belong to the two shapes the seam declines,
`inner join` and `pivot`, whose refresh is the full evaluation by design. Phase 459 measured the
declined path, removed the one avoidable cost it found, and changed how a declined shape's tick is
judged (`DECISIONS.md` D33).

**Where a declined tick's time goes.** Release, Arm64, 100,000 rows, the corpus's one-row edit and
append, before the change, in ms. The refresh is the boundary plus the reference evaluation on the
default kernel set plus a preamble; the full evaluation is the clock leg's denominator, on
`Kernels.oneThread`.

| Cell | Tick | Diff | Refresh | Reference evaluation | Full, one thread | Pipeline compared |
|---|---:|---:|---:|---:|---:|---:|
| `inner join` edit | 6.7–9.1 | 1.31–1.39 | 6.66–6.79 | 4.7–5.5 | 5.6–5.8 | 1.19–1.22 |
| `inner join` append | 7.4–8.6 | 0.60–0.91 | 6.2–7.1 | 5.0–5.8 | 5.6 | 1.19–1.23 |
| `pivot` edit | 13.9–16.0 | 1.75–2.08 | 13.9–15.7 | 13.3–16.7 | 13.9–16.3 | 0 |
| `pivot` append | 15.1–17.1 | 1.09–1.22 | 14.4–15.1 | 13.7–14.5 | 13.5–15.4 | 0 |

- **The kernel set is not the cost.** `evalJoin` and `evalPivot` take no kernel set, so the default set
  and `oneThread` run the same code for both shapes; their measured evaluations agree within noise.
- **The boundary is walked once,** and since Phase 423 it is a view: a second `prepare` injected into the
  declined path moved no cell of a clock leg (every declined cell x0.97 to x1.02).
- **The join paid for comparing its pipeline.** The refresh's staleness test compared the pipeline it was
  handed with the state's by structural equality, which walked the join's embedded 100,000-row right
  relation cell by cell on every refresh: 1.2 ms, a fifth of the full evaluation. The test now asks
  identity first (`samePipeline`, Phase 333's test, which Phase 355 had stopped calling). Timed beside
  "diff plus reference evaluation" in one window, the join's excess fell from 0.95 to 1.42 ms at 100,000
  rows (0.23 to 0.28 ms at 20,000) to within 0.13 ms either way.

**The rule.** A declined shape's edit and append ticks are held to `diff + reference evaluation + floor`,
the reference evaluation on the kernel set the declined refresh runs on, over the same fresh source
object the tick is handed, both timed in one interleaved window (`pairedBatchedMs`: one call a sample at
this size, the best of 25 rounds each). What remains above that is the refresh's own preamble, which the
floor bounds. Every other shape keeps `max(1.6 x full, 1.5 x baseline) + floor`; no factor moved.

**On the clock leg** (Arm64, Release, the fixed tree), every declined cell read x0.93 to x1.04 of its diff
and reference evaluation on two legs at calibration 6.20 ms; a third leg at 7.67 ms (a loaded machine)
read the `pivot` edit cells x1.09 to x1.17 on two attempts and x1.00 on the third, so green under the
same-cell rule. With the pipeline walk put back, the `inner join` cells at 20,000 and 100,000 rows read
x1.24 to x1.33 and were red on all three attempts of both cases, where the old ratio arm had passed them.
