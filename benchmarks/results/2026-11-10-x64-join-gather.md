# A join's pairs written by left ranges and its columns by output ranges (D70)

Native x64, 32 logical processors, .NET 10, Release.

## Where the join's time was

`Partitioned.StringJoin`'s data, the step's stages timed apart (D69's code):

| Left rows | Keys | Step | Key coding | Four columns gathered | The rest |
|---:|---:|---:|---:|---:|---:|
| 1,000,000 | 1,000,000 | 52 ms | 28 ms | 8.3 ms | about 16 ms |
| 1,000,000 | 100,000 | 16 ms | 5.5 ms | 5.3 ms | about 5 ms |
| 100,000 | 100,000 | 5.9 ms | 3.2 ms | 0.5 ms | about 2 ms |

"The rest" is listing each slot's right rows, scanning the left side for matches and writing the pairs.

## BenchmarkDotNet, the shipped form

D69 and D70 in two legs of one run, then the gate:

| Case | D69 | D70 |
|---|---:|---:|
| `StringJoin`, all distinct, 1,000,000, native | 55.4 ms | 44.2 ms |
| `StringJoin`, one per ten, 1,000,000, native | 19.0 ms | 14.7 ms |
| `StringJoin`, all distinct, 100,000, native | 5.67 ms | 5.72 ms |
| `StringJoin`, all distinct, 1,000,000, portable | 150.6 ms | 151.8 ms |
| keyed join, int, 100,000 | 1,794 µs | 1,777 µs |
| keyed join, date, 100,000 | 1,825 µs | 1,875 µs |
| keyed join, string, 100,000 | 5,461 µs | 5,561 µs |

## The first form, and why the cap

The first form split from the kernel set's own threshold (32,768 rows) into one range per logical processor. It read
the million-row joins as well (55.2 to 44.7 ms) and the keyed joins at 100,000 rows 14 to 16 per cent slower (int
1,664 to 1,926 µs, date 1,738 to 1,987 µs): 32 ranges of 3,000 rows across six passes on the pool. Capped at 16
ranges from four times the threshold, as D66 found for the coding, the keyed joins run on one thread and read level
(one sitting: int 1.71 and 1.95 ms against 1.90 and 1.94, date 1.61 and 1.59 against 1.60 and 1.54), and the
million-row join on distinct keys read 66 ms before and 50 ms after.
