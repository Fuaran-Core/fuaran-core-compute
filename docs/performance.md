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
