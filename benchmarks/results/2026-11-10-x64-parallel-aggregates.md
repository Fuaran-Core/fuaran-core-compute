# A grouping's aggregates fed by row ranges and emitted by group ranges (D68)

Native x64, 32 logical processors, .NET 10, Release. BenchmarkDotNet, D66 and D68 in two legs of one run, the gate after.

## The suites the change reaches

`Partitioned.GroupByInt` (new): an int key, an int `Sum`, a `Count` and a `Max`, all of which merge exactly.

| Keys | Member | Rows | D66 | D68 | Change |
|---|---|---:|---:|---:|---:|
| ten | native | 100,000 | 646 µs | 352 µs | −46% |
| ten | native | 1,000,000 | 6,458 µs | 2,592 µs | −60% |
| ten | portable | 1,000,000 | 7,173 µs | 6,913 µs | −4% |
| one per ten rows | native | 100,000 | 872 µs | 862 µs | −1% |
| one per ten rows | native | 1,000,000 | 9,780 µs | 10,351 µs | +6% |
| one per ten rows | portable | 1,000,000 | 10,037 µs | 10,274 µs | +2% |

At one key per ten rows the slots per range for sixteen ranges outnumber the rows, so the feed stays on one thread and
only the emit is split. A same-sitting profile of that case read it level (9.1 and 9.2 ms against 9.7 and 8.6).

`Partitioned.GroupBy`: a float `Sum`, which does not merge, and a `Count`, which does.

| Keys | Member | Rows | D66 | D68 |
|---|---|---:|---:|---:|
| ten | native | 1,000,000 | 3,702 µs | 3,373 µs |
| one per ten rows | native | 1,000,000 | 6,335 µs | 5,940 µs |

`Partitioned.StringGroupBy`, every key distinct: the emit across group ranges.

| Rows | Member | D66 | D68 |
|---:|---|---:|---:|
| 1,000,000 | native | 31.0 ms | 26.3 ms |
| 100,000 | native | 4.28 ms | 4.47 ms |

The keyed family's groupings (100,000 distinct keys, an int `Sum`) moved from −13 per cent (date) to flat (string);
its joins, which the change does not reach, moved within their spread.

## The portable member

Across the suites the portable member moved between −4 and +5 per cent in both directions on code the JIT compiles to
the same machine code as before (`FeedRange` at the sizes `FeedAll` had, every method at Tier 1, checked with
`DOTNET_JitDisasmSummary`). Read as the run's spread.

## A same-sitting profile

The whole step, `Sum`, `Count` and `Max` over an int column grouped by an int key, D66 and D68 alternated twice:

| Rows | Groups | D66 native | D68 native |
|---:|---:|---:|---:|
| 100,000 | 10 | 0.64, 0.65 ms | 0.39, 0.35 ms |
| 1,000,000 | 10 | 6.12, 6.10 ms | 2.94, 2.59 ms |
| 1,000,000 | 1,000 | 6.76, 8.98 ms | 3.39, 3.01 ms |
| 1,000,000 | 1,000,000 | 21.3, 21.5 ms | 17.4, 15.8 ms |
