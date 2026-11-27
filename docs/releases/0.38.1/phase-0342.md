### An evaluation whose result stays prepared (Phase 342) — additive, `surface`

**What changed.** `DataFrame.evalPrepared` takes a prepared source and always answers a `Table`, so a
consumer that feeds one pipeline's answer to the next (a sheet's nodes, chained dashboard bindings)
paid the boundary out and back in at every hop. `Fuaran.Compute.DataFrame` gains one function:

- **`DataFrame.evalToPrepared`** — the same resolver, env, pipeline and prepared source as
  `evalPrepared`, answering `Result<Prepared, EvalError>`. The table it stands for is built only if
  `DataFrame.toTable` is called, and is then exactly `evalPrepared`'s answer; it refuses exactly when
  `evalPrepared` refuses, with an equal error. The result is an ordinary prepared source: it goes
  straight back to `evalPrepared`, `evalToPrepared`, the `ColumnOps` forms over a prepared source and
  `Incremental.primePrepared`. Where the final step left a selection (a `Filter`, a `Sort`, a
  `Limit`) the result is gathered dense first, one typed gather per column, so it holds its own rows
  and not the source's vectors.

The law (`tests/.../PreparedResultLaw.fs`, run by the suite and by the node benchmark harness under
Fable) holds both clauses over every pipeline and source in the transform law vectors, and holds the
result to its purpose: fed every pipeline of that sample as a follow-on, it answers what its table
prepared afresh answers, refusals included. The suite also holds the committed vectors' answers
byte for byte, three-pipeline chains over the generated algebra sample against the chain through
the boundary, and a column op over a kept result against the op over its table prepared.

No existing function's answer moves: `evalPrepared` and every entry point that answers a `Table`
fold through the same driver, which now ends on the evaluator's frame and pays the boundary out at
the caller.

**Measured** (`benchmarks/results/2026-10-04-i7-9700-phase-342.md`): three pipelines in sequence over
the sheet's `orders` (`lines`; the big lines with a net amount; the tax beside it, projected to four
columns), Table-chained against prepared-chained. At 100,000 rows the prepared chain takes 0.38 of
the Table chain's time on .NET (29.9 ms against 79.5 ms) and allocates 0.48 of its bytes; under node
0.58 (112.5 ms against 193.0 ms). At 1,000 rows 0.54 on .NET and 0.72 under node.

**What a consumer does.** Nothing. One that chains pipelines prepares the first source once, calls
`evalToPrepared` for every hop but the last, and `evalPrepared` (or `toTable`) where it needs a
`Table`.
