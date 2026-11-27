### A refresh no longer walks an embedded relation to learn its pipeline did not move (Phase 459, `DECISIONS.md` D33) — `none`, `performance`

**What changed.** A refresh asks whether the pipeline it is handed is the one the state was built for.
Since Phase 355 that question was answered by structural equality alone, so a pipeline that embeds a
relation (`Join(Embedded t, …)`, `Union(Embedded t)`) was compared cell by cell on every refresh, even
when the caller handed back the very pipeline object the state holds. It is now asked identity first,
through the test Phase 333 wrote for it, and structurally only when the objects differ. The answer is the
one structural equality gives in every case but one, where it is safer: a pipeline object holding a NaN
literal now reads as unchanged against itself rather than as moved, and either reading refreshes to the
same result.

**Measured** (`DECISIONS.md` D33, `docs/performance.md`, "A declined shape's tick is measured for what
it is"). The corpus `inner join` at 100,000 rows paid 1.2 ms a refresh for the walk, a fifth of its full
evaluation; its tick now costs its diff and its reference evaluation and nothing measurable beside them.

**The clock leg.** A shape the seam declines (`inner join`, `pivot`) has its edit and append ticks held
to its diff and reference evaluation plus the per-call floor, timed beside the tick, rather than to 1.6
times the one-thread full evaluation; no bound factor moved. This is the gate, not the package.

**The class.** `none`: no signature, no answer and no wire byte moves. It rides the `0.40.0` draft.

**What a consumer does.** Nothing.
