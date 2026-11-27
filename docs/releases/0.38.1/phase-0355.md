### A wire form for the incremental state (Phase 355) — additive, `surface`

**What changed.** An `IncrementalEval` lived as long as the process that built it, so a consumer that
runs in fresh processes (a scheduled job, a serverless invocation, a host restarted between runs)
paid a full evaluation every run. `Fuaran.Compute.DataFrame` gains a module and a record:

- **`IncrementalCodec.encode` / `decode`** — the canonical wire string for a state and its reader.
  The encoding carries everything a refresh reads: the pipeline and its planned form, the env, the
  identity scheme, the source, the result, the row, group, order, relation and window caches and the
  footprint. Cells are written exactly (their own case, their own text, the sign of a zero), where
  the column codec returns a table to a normal form, because a cached cell stands in for an
  evaluation. The classification is recomputed from the planned form rather than carried.
- **`IncrementalCodec.encodeDetached` / `decodeOver`** — the same without the source: the encoding
  carries the source's fingerprint and the consumer supplies the table again.
- **`IncrementalStateKey`**, `IncrementalCodec.keyOf`, `pipelineHash` and `sourceFingerprint` — the
  key a state is stored under: SHA-256 of the pipeline's canonical wire string and of the source's
  exact encoding.

A refresh from a decoded state equals a refresh from the original, in its cells and in its
footprint, for every pair the incremental family draws (`IncrementalDelta.stateLaws`, new in
`Fuaran.Compute.Conformance`, on .NET and under Fable), and for the state a refresh built as well as
the one a prime did.

**The key decides whether a state may answer.** A refresh over a decoded state compares the
pipeline by its canonical hash: a decoded state holds the codec's normal form of its pipeline, which
is not always the value a consumer builds, so two pipelines with one wire string are one pipeline to
it and any other is a full evaluation reporting `PipelineChanged`. `decodeOver` holds the supplied
source to the fingerprint: over a table that is not the one the state was built for, the state holds
no caches and its next refresh evaluates in full, reporting `DeltaIsFullRefresh` (a state built over
another table can vouch for no row of this one, which is what the top delta says; a reason of its
own would widen `FallBackReason`, which is a breaking move this entry does not make). A stale or
foreign state makes a run slower and does not make it wrong.

**A damaged encoding is refused.** The document carries a SHA-256 digest of its body; a truncated or
altered encoding, another version, and a detached encoding read without its source each decode to a
`ColumnError`. The digest detects damage and not forgery: an encoding is the consumer's own stored
state, as trusted as a state held in memory, and not an untrusted input.

**What is not carried.** The prepared and chunked working forms share by object identity, which no
wire holds. A decoded state answers `refreshPrepared` over a pipeline of derives as any state another
path built does: every chunk once, then by identity again.

No existing function's answer moves. A state built in this process compares its pipeline
structurally, as before.

**The wire's form is packed (Phase 357).** The state's large members are typed runs rather than
one JSON value per cell: a column or a cache is one string of packed values naming the one case its
cells hold, with a mask for the absent cell and for the slot a row never reached; tokens carry their
shared prefix once; indexes are packed ints; row-shaped caches are a run per position. A run of more
than one case is written a value a cell, so every cell still comes back under its own case and with
its own text. The document is **version 2** and the reader refuses version 1, the per-cell form this
entry first shipped: the slot is an untagged draft, so that form was never released and is replaced
rather than kept beside the new one. The source's fingerprint is taken over the packed encoding, so
its value is not the one the per-cell form gave; no released value moves. On .NET the digest is the
platform's SHA-256, value-identical to the managed copy that remains the Fable path. No public
signature moves.

**Measured** (`benchmarks/results/2026-10-03-i7-8650u-phase-357.md`, beside
`2026-10-03-i7-8650u-phase-355.md`), over the three Scaling pipelines at 1,000, 100,000 and 1,000,000
rows: an encoding that carries its source is 0.82 to 1.02 times the source's own column wire, a
detached one 0.38 to 0.51 times it, and the bytes are the same length on .NET and under node. At
1,000,000 rows on .NET a decode takes 3.4 to 4.8 s (2.0 to 2.1 s detached), where the per-cell form
took 11.8 to 12.6 s (6.4 to 7.3 s), and an encode 1.8 to 2.7 s where it took 7.0 to 8.4; side by
side under one load the decode is three to three and a half times cheaper. Under node a decode at
100,000 rows is unchanged. **On these shapes resuming still does not beat a full evaluation**:
resuming costs about 2.5 microseconds per row over a supplied source and about 5 carrying it, against
0.14 to 0.45 s for the full evaluation of a million rows; the pipelines evaluate one comparison per
row. The wire form pays where an evaluation costs more per row than that. node does not reach
1,000,000 rows on its default heap.

**What a consumer does.** Nothing. One that runs in fresh processes, over a pipeline whose evaluation
is dearer than the read above, encodes the state after a run, stores it under `keyOf`, and at the
next run decodes it, measures the delta against `Incremental.source` of the decoded state, and
refreshes.
