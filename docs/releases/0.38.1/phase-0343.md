### Independent pipelines evaluated concurrently over one prepared source (Phase 343) — additive, `surface`

**What changed.** One new entry point in `Fuaran.Compute.DataFrame`, and no answer moves:

- **`DataFrame.evalManyToPrepared`** — `resolve -> env -> Transform list list -> Prepared ->
  Result<Prepared, EvalError> list`. A batch of independent pipelines over one prepared source, the
  results in list order, each equal to `evalToPrepared resolve env p prepared` for its pipeline `p`,
  refusals included and each pipeline's own (the suite's law holds every member of the kernel pair to
  that over generated lists of the transform vectors' pipelines). The shape of a dashboard's bindings
  and a sheet's same-level nodes.
- **On .NET** a batch of two or more pipelines with at least 1,000 rows of work between them
  (pipelines times source rows) runs on the thread pool, at most one pipeline per logical processor
  at once; a pipeline running alongside others fans out no morsels or partitions of its own, so the
  batch never occupies more than that. Below the threshold, and under Fable, the pipelines run one
  after another. 1.5 to 3.0 times faster than calling `evalToPrepared` per pipeline at 1,000 rows and
  2.0 to 3.7 times from 10,000, on an 8-core x64
  (`benchmarks/results/2026-10-05-i7-9700-phase-343.md`).

**To adopt.** Optional. A consumer that evaluates several pipelines over one source in a loop can
hand them over as one list. `resolve` may then be called from several threads at once on .NET, so it
must be safe to call concurrently, as a lookup in an immutable map is.
