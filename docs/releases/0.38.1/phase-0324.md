### Window and sort through typed partition slots and order codes (Phase 324) — none, `performance`

**What changed.** No public surface moves, and no answer: every `Window` and `Sort` (and the top-n a
`Sort` then a `Limit` plans to) returns the cells it returned on `0.37.0`, ties broken by frame
order, nulls last in both directions, `NaN` the greatest float. Underneath, in
`Fuaran.Compute.DataFrame`:

- **A window gathers no rows.** Each row's partition is a slot from the typed key slots a `GroupBy`
  over the same columns forms, and one permutation lists the rows partition by partition in window
  order; each partition is a run of it, scanned in sequence. A positional or ranking window emits an
  `int` vector and a float running total or rolling window a `float` vector directly.
- **Sort keys are order codes**, read once per key: an `int` offset from its least (or, descending,
  greatest) value, `string` / `float` / `decimal` values dense-ranked once through their existing
  total order, `bool` 0 / 1, a null past every value. Where the keys' ranges allow, the codes and
  the row's position pack into one exact number and the sort compares plain numbers. A key whose
  column holds a cell outside its type keeps the comparator, so its answer is the one it was.
- **The incremental seam's window step** packs only the columns the window reads, shares the rest,
  and carries its column as the cell list the result hands back; a window is admitted to the
  in-place reading as a sort is.

A top-n (a `Sort` then a `Limit`) keeps its comparator: measured under codes it was at parity and
heavier, since its heap compares most rows only once.

**Measured** (`benchmarks/results/2026-10-02-i7-8650u-phase-324.md`): at 100,000 rows the
one-partition window step at 1/50 of its `8a39a42` cost on .NET and the two-key sort at 1/8; on node
the window 3.3 to 4.9 times faster and the two-key sort 3.0 to 3.9 times. A sort allocates more than
it did (its codes and packed keys); a window far less. The tick family stays inside its bound: a
window's refresh now costs what its full evaluation costs, so its tick is the diff more.

**What a consumer does.** Nothing.
