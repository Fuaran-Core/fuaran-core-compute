# Date comparisons on day counts (D59): the filter and the compiled path, before and after

x64, 32 logical processors, Windows 11, .NET 10, Release, `instrument: X64 process on a X64 machine`,
BenchmarkDotNet v0.15.8. The machine was not isolated, so the times are approximate. The allocations
are stable and are the stronger evidence. No figure here is comparable to an earlier file as a time.

## Verdict

| Question | Answer |
|---|---|
| What did a date-range filter cost? | At 100,000 rows, 43.5 MB of the 46.7 MB the dated pipeline allocated: a date comparison rendered each row's day to text |
| What does it cost now? | 1.86 MB at 100,000 rows, and about 0.46 ms against 3.0 ms, close to the int filter (0.35 ms, 1.85 MB) |
| The dated pipeline's bytes | 47.2 MB to 4.9 MB at 100,000 rows; 3.5 times the hand arm, from 30 |
| The dated pipeline's time | Better at every size, but the 100,000-row ratio stays high: see "Still open" |

## The dated pipeline (`*Suites+Dated*`)

| Rows | Hand arm | Before | After | Ratio before | Ratio after |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 5.3 µs | 171.6 µs | 19.9 µs | 29.2 | 3.7 |
| 10,000 | 52 µs | 1,099 µs | 131 µs | 16.8 | 2.5 |
| 100,000 | 517 µs | 6,747 µs | 4,774 µs | 12.6 | 9.2 |

The sheet's `lines` and `byRegion` read no date and did not move beyond noise (8.3 and 2.3 at 100,000
rows).

## The filter alone

A scratch harness outside this repository: `evalToPrepared` over the dated `orders`, the median of 41
runs after 15 warm-ups, allocation by `GC.GetTotalAllocatedBytes(true)`.

| Predicate | Rows | Before | Before, bytes | After | After, bytes |
|---|---:|---:|---:|---:|---:|
| `day >= from` | 100,000 | 2.7 ms | 24.0 MB | 0.47 ms | 2.7 MB |
| `from <= day < until` | 100,000 | 3.0 ms | 43.5 MB | 0.46 ms | 1.9 MB |
| `from <= day < until` | 1,000,000 | 33 ms | 435 MB | 4.9 ms | 18.5 MB |
| `qty > 5 and qty < 16`, for scale | 100,000 | 0.37 ms | 1.8 MB | 0.35 ms | 1.8 MB |

## A correction to the Phase 423 file

`2026-10-10-ultra9-386h-phase-423.md` reads the dated pipeline's ratio against the hand arm as "the
sheet's shape … not a temporal cost". Splitting the pipeline by step shows otherwise: the filter was
the cost. Filtering on an int column of the same day counts instead took the pipeline's bytes from
466 MB to 50 MB at 1,000,000 rows and its ratio from 7.1 to 1.1, which is what this change then did
without the extra column. That file is a dated record and stays as written; this note corrects it.

## Still open

- **The dated pipeline at 100,000 rows.** Under BenchmarkDotNet it takes 4.8 ms, while the scratch
  harness times the same pipeline at about 2.3 ms on all processors and 2.4 ms with
  `DOTNET_PROCESSOR_COUNT=1`, and the ratio at 10,000 rows is 2.5. The gap appears where the steps turn
  to the thread pool, and the two harnesses differ in how they warm up. Its cause is not established.
  Later the same day: the 4.8 ms is a mean over a tail, and warmed longer the median is 850-900 µs. See
  "Still open" in `2026-11-07-x64-join.md`.
- **The join.** Corrected: this bullet first read about 8.8 ms and 22.7 MB, about 9 times a hand-written
  hash join. That harness rebuilt the right table inside the timed run. See
  `2026-11-07-x64-join.md`: about 2.4 times at 100,000 rows.
- **The node leg.** The corpus and its laws ran under Fable and node (`run-node.ps1 -Runs 3`), but
  `DateCompareTests` runs on .NET only, and no node timing was taken here.
