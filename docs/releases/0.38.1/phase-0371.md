### The top-n runs a range of rows at a time across threads (Phase 371, `DECISIONS.md` D8 item 3) — none, `performance`

**What changed.** No public surface moves, and no answer. On the native member (every .NET host), a
top-n — a `Sort` then a `Limit` the planner fuses — over at least `PartitionRows` (32,768) rows keeps
a heap of the window's least rows per range of rows, each range on its own thread, and sorts the
candidates under the same total order; the rows and their order are the sequential heap's, which the
cross-member laws hold byte for byte. Under Fable, and below the threshold, nothing changes. 2.7 to
3.0 times faster at 100,000 rows and 3.8 to 4.3 at a million on an 8-core x64
(`benchmarks/results/2026-10-04-i7-9700-phase-371.md`).

**Nothing to adopt.** The suite's tick clock cases now time the full evaluation at one thread
(operator ruling 2026-10-04); that is a test's footing, not a contract a consumer reads.
