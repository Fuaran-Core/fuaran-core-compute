# A key column of many distinct keys coded by partitions of its values (D66)

Native x64, 32 logical processors, .NET 10, Release. Two instruments, for a reason given at the end.

## The grouping step, one sitting

`DataFrame.evalStepWith` over a `GroupBy` on a string key with a float `Sum` and a `Count` (the data of
`Partitioned.StringGroupBy`: keys `cust-` and seven digits), before (D65) and after, each the median of 41 timed runs
after 60 untimed ones, both builds in one sitting, run twice in alternation (two readings each):

| Rows | Keys | Member | D65 | D66 |
|---:|---|---|---:|---:|
| 100,000 | all distinct | native | 3.66, 4.55 ms | 3.68, 4.03 ms |
| 100,000 | all distinct | portable | 5.12, 5.10 ms | 5.07, 5.03 ms |
| 1,000,000 | all distinct | native | 68.4, 73.4 ms | 35.1, 33.9 ms |
| 1,000,000 | all distinct | portable | 82.4, 72.0 ms | 68.6, 64.8 ms |
| 1,000,000 | one per ten rows | native | 20.1, 19.7 ms | 20.9, 18.6 ms |
| 1,000,000 | one per ten rows | portable | 19.1, 19.8 ms | 20.3, 18.6 ms |

The slot step alone (`RowHash.slotsWith`), same instrument, earlier sitting: a million distinct strings 43 ms
sequential and 14.8 ms across threads; a million rows over 100,000 strings 15 and 10.5 to 12.5 ms; 200,000 distinct
strings 5.0 and 3.0 ms; a million distinct ints 5.0 and 5.3 ms (a direct table: not partitioned).

At 100,000 rows the column is not partitioned (from 131,072 rows on the native member) and the step is level. At one key
per ten rows the coding gains (15 to about 11 ms in the slot step) and the step barely moves: the rest of the step,
aggregates and output over 100,000 groups, is most of it.

## BenchmarkDotNet

The same cases through `Partitioned.StringGroupBy`, and the existing `Partitioned.GroupBy` and keyed family as checks,
before and after in two legs of one run, before the probe's sizing was corrected:

| Case | D65 | D66 |
|---|---:|---:|
| string, all distinct, 1,000,000, native | 57.1 ms | 31.3 ms |
| string, one per ten, 1,000,000, native | 16.9 ms | 17.2 ms |
| string, all distinct, 100,000, native | 4.11 ms | 4.34 ms |
| int, ten keys, 1,000,000, native (`Partitioned.GroupBy`) | 3.39 ms | 3.41 ms |
| int, one per ten, 1,000,000, native | 5.71 ms | 5.82 ms |

The keyed family (100,000 rows) did not take the new path and moved within its usual spread, except a decimal join
(4.05 to 3.56 ms) and a string group-by (3.22 to 3.53 ms) in opposite directions on unchanged paths.

**Why the one-sitting table leads.** In the same run the PORTABLE member's string cases read 30 to 42 per cent slower
after (56.7 to 79.4 ms at a million distinct keys), on code the change does not reach. The two legs ran an hour apart;
these cases allocate and collect over a million string objects, and their timings move with the process's heap. The
one-sitting alternation above reads the portable member level, and that is what the code says it should be.
