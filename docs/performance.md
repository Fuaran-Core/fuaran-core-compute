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
