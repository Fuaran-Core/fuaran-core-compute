# The dated pipeline after D59, D60 and the allocation-free date parts, on Core 0.41.0

x64, 32 logical processors, Windows 11, .NET 10, Release, BenchmarkDotNet v0.15.8, `--memory`. The machine was
not isolated, so the times are approximate. The allocations are stable and are the stronger evidence. Measured at
the commit that raised `FuaranCoreVersion` to `0.41.0`, where `DatePart` reads `TemporalText.yearOfDays`,
`monthOfDays` and `dayOfDays` (Core D244) and a constant part is read once (`3b406ad`).

## Verdict

| Question | Answer |
|---|---|
| Where does the dated pipeline stand against the hand arm? | 1.56 times at 100,000 rows, from 9.2 in `2026-11-07-x64-date-comparison.md`; 3.6 at 1,000 and 2.3 at 10,000 |
| Its bytes? | 2.36 MB at 100,000 rows, from 4.9 MB, and 47.2 MB before D59: 1.5 times the hand arm |
| The tail? | Gone under BenchmarkDotNet: mean 740 µs and median 749 µs, where the mean was 4.8 ms over a median near 1 ms |

## The dated pipeline (`*Suites+Dated*`)

| Rows | Hand arm | Evaluator | Ratio | Hand, bytes | Evaluator, bytes | Before (2026-11-07) |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 4.9 µs | 17.6 µs | 3.58 | 16.7 KB | 65.1 KB | 19.9 µs, 3.7 |
| 10,000 | 47.9 µs | 109.6 µs | 2.29 | 158 KB | 271 KB | 131 µs, 2.5 |
| 100,000 | 474 µs | 740 µs | 1.56 | 1,568 KB | 2,355 KB | 4,774 µs, 9.2 |

The 100,000-row figure was the one the scratch harness traced to background gen2 collections triggered by large
arrays on a small heap (`2026-11-07-x64-join.md`, "The dated pipeline's tail, traced"). Removing about 2.5 MB of
per-row `DatePart` garbage per run is what moved it: the run still collects gen2 (250 per 1,000 operations), but
the evaluator's mean now sits on its median, so the remaining ratio is code cost, not collection wait. That reading
is inferred from the agreement of mean and median; a per-run gen2 trace was not repeated at this commit.

## The typed suites, for the record (`*Suites+Typed*`, Evaluate)

| Verb | Type | 1,000 | 10,000 | 100,000 | 100,000, bytes |
|---|---|---:|---:|---:|---:|
| filter | int | 3.5 µs | 25.1 µs | 369 µs | 1,434 KB |
| filter | decimal | 16.9 µs | 160 µs | 1,850 µs | 2,803 KB |
| group-and-sum | int | 8.9 µs | 69.9 µs | 785 µs | 1,089 KB |
| group-and-sum | decimal | 25.6 µs | 212 µs | 2,230 µs | 1,889 KB |
| join | int | 21.7 µs | 294 µs | 2,159 µs | 7,334 KB |
| join | float | 22.4 µs | 347 µs | 4,922 µs | 15,978 KB |
| join | decimal | 156 µs | 2,575 µs | 35,889 µs | 79,259 KB |
| sort | int | 60.6 µs | 1,569 µs | 1,941 µs | 6,105 KB |
| sort | decimal | 74.5 µs | 1,742 µs | 17,101 µs | 20,309 KB |

The int join matches `2026-11-07-x64-join.md`'s after column (2,413 µs then, 2,159 µs now; 7,334 KB both).

## Still open

- **The decimal join is 17 times the int join at 100,000 rows** (35.9 ms, 79 MB, against 2.2 ms, 7.3 MB), and the
  float join 2.3 times. D60 removed the join's overhead on the int path; keys that do not code through the direct
  table were not examined. Not traced.
- **Sort at 10,000 rows costs nearly what it does at 100,000 for int** (1.57 ms against 1.94 ms), and decimal sort
  is 8.8 times int at 100,000. Not traced.
- **The 1,000-row ratio, 3.6, is the pipeline's fixed cost** over a 5 µs hand loop; not examined.
