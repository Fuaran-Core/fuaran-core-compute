### A row-local step as plain data, for a worker to compile (Phase 375, `DECISIONS.md` D13) — none, `performance`

**What changed.** No public surface moves, and no answer. Inside `Fuaran.Compute.DataFrame`, the
`Filter` and `Derive` row loops are lifted into one loop each that the sequential evaluator and an
internal hand-off (`MorselHandOff`: the step and its environment on the canonical wire, the vectors it
reads as plain arrays, the result slots) both run, so a future worker compiles the step through the
evaluator's own path. A `Filter`'s kept rows land in one buffer rather than a growable list per morsel:
under node the sequential `filter` is 1.6 to 1.8 times faster at 100,000 and 1,000,000 rows, and
nothing is slower on either host (`benchmarks/results/2026-10-06-i7-9700-phase-375.md`). The suite
holds every handed-off step byte-identical to the sequential member over the transform-law vectors and
the corpus, on .NET and under node.

**Nothing to adopt.** The opt-in worker path D10 rules for was not reachable from this change alone;
Phase 376 (above, in the same draft) ships the pool and the asynchronous entry point.
