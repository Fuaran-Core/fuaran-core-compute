# Fuaran.Core.Compute — API stability

**Status:** pre-1.0. The version is single-sourced from `<Version>` in `Directory.Build.props`;
this document records what each version changed, by class.

## Versioning policy

Every package this repository produces ships at the one `<Version>`. A change to a package's
public contract ships on a version AHEAD of every version that has been tagged. The standing
`<Version>` is a **draft slot** while it is untagged: an additive change rides it (its entry grows
here, the number does not move), and a change of a higher class than the draft already carries
advances it, because the number is what tells a consumer what adopting it costs. The class of a
move is not an argument — the `Public surface` family computes it from the committed baselines under
[`api/`](api/) (below).

A release is a `v*` tag, which runs the publish workflow (see the README's release section). An
entry header here names every version this repository has released; the `Package roster` family
holds the document to the repository's release tags, and while every entry header below is a DRAFT
it reads a clone with no release tag as "nothing released yet" rather than as a clone missing its
tags.

**The history before this repository.** The four packages shipped from the
[Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core) repository at every version from `0.9.0`
(the first release that carried `Fuaran.Core.DataFrame`) through `0.32.0`, in step with the rest of
that repository's packages; `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
first shipped there at `0.32.0`, when Phase 257 cut them beside the other two. **Every one of those
versions is recorded in [that repository's `STABILITY.md`](https://github.com/Fuaran-Core/fuaran-core/blob/main/STABILITY.md)**,
and this document does not restate them: two copies of one record is how a record comes to
disagree with itself. This repository's first version is `0.33.0`, the next minor above the last
emission there (`DECISIONS.md` D1), so a consumer's version floor never moves backwards across the
change of producer.

## Public-surface baselines

Every packable package carries a committed baseline of its public contract at
[`api/<package>.txt`](api/), rendered from the built assembly's IL metadata; the wire-bearing
packages also carry a wire-surface record under [`api/wire/`](api/wire/). The `Public surface`
family renders each package afresh on every gate run and diffs it against its baseline, and refuses
a surface that moved while its baseline stood still — additive or breaking, a move whose baseline
moved with it passes. Regenerate with:

```
CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests
```

It rewrites EVERY drifted baseline, not only the one you were looking at: stage the baselines you
meant to move by name.

## 0.38.0 — DRAFT

`0.37.0` is tagged, so the change below ADVANCED the slot to `0.38.0`. It is a draft until it is
tagged: an additive change rides it, a breaking one advances it.

### Every float the evaluator computes carries one NaN, and the gate runs Release (Phase 404, `DECISIONS.md` D16) — none, `determinism`

**What changed.** No public surface moves, and no answer changes its value: a NaN answer is still
`NaN`, every other answer is the same to the bit. What moves is the NaN's own bits. One rule,
evaluator-wide: every float the evaluator computes — an expression's (`Derive`, `evalExprInRow`), an
aggregate's (`GroupBy`, `Pivot`, `DataFrame.aggregateCells`) and a window function's — leaves it with
`Double.NaN`'s bit pattern wherever it is a NaN, whatever the arithmetic produced: `-inf + +inf`,
`0 * inf`, a running total or a `Sum` over both infinities, a `Lag`, `First`, `Max` or `Col` carrying a
source NaN of another payload. The batch evaluator, the row form and the incremental refresh answer
through the same exits, so they agree to the bit. A step that computes nothing (`Project`, `Filter`,
`Sort`, a join) hands on the caller's cells as given. IEEE 754 leaves a NaN result's sign and payload
open, and the hosts and builds took that latitude: the hardware's default NaN has its sign clear on
Arm64 and set on x64, and a Release build commuted an addition the Debug build did not, so one window
answered two bit patterns from one input. On the canonical wire a NaN was already one token (`NaN`),
so no serialised byte moves; the bits are what an in-process comparison, a parity vector's .NET half
and a content address over raw values see.

`verify.ps1` takes `-Configuration Debug|Release` (Debug by default) and runs the build and the main
suite in it; the clock leg stays Release under either. `ci.yml` runs both configurations, and `publish-packages.yml`
verifies in Release and packs `--no-build` from what it verified.

**Nothing to adopt.** A consumer comparing answers by `DoubleToInt64Bits` now sees one NaN.

### The pipeline-query registry's lifecycle verbs (Phase 378, `DECISIONS.md` D15) — additive, `surface`

**What changed.** Four new functions in `Fuaran.Compute.PipelineQueryRegistry`, each the substrate's
`QueryRegistry` verb of the same name over the declarations, the bodies following only on success:

- **`unregister`** — `id -> PipelineQueryRegistry -> Result<PipelineQueryRegistry, PipelineQueryError>`;
  an unregistered id is `QueryRefused(NoSuchQuery …)`.
- **`replace`** — `PipelineQuery -> PipelineQueryRegistry -> Result<…>`; `QueryRefused` from the
  substrate, then the new pair held to `PipelineQuery.check` as `register` holds it. A refusal leaves
  the registry unchanged.
- **`restrict`** — `Set<string> -> PipelineQueryRegistry -> PipelineQueryRegistry`; never widens.
- **`union`** — `PipelineQueryRegistry -> PipelineQueryRegistry -> Result<…>`; a shared id is
  `QueryRefused(DuplicateQuery …)`. Associative.

**To adopt.** Optional; the registry was add-only before. Needs `Fuaran.Core` `0.35.1` or later, the
release that carries the substrate's `QueryRegistry` verbs; the pin has since moved to `0.36.0`.

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

### The opt-in worker pool and the asynchronous entry point (Phase 376, `DECISIONS.md` D14) — additive, `surface`

**What changed.** New in `Fuaran.Compute.DataFrame`, and no answer moves on any host:

- **`DataFrame.evalToPreparedAsync`** — `resolve -> env -> Transform list -> Prepared ->
  Async<Result<Prepared, EvalError>>`: `evalToPrepared`'s answer, byte for byte, asynchronously. The
  only route to the pool; `evalToPrepared` and every synchronous entry point are unchanged.
- **`WorkerPool`** — `optIn : (unit -> MorselWorker) -> int -> bool`, `optOut`, `isActive`, `warm :
  unit -> Async<unit>`, `serve : (obj -> unit) -> (obj -> unit)` and the literal `PoolRows` (32,768);
  **`MorselWorker`**, the interface a host wraps its workers in (`Post`, `Listen`, `Stop`).
- Under Fable the frame's masks are bytes (`Uint8Array`) rather than JavaScript arrays, and once a host
  opts in its vectors are allocated over shared memory. Internal; .NET's representation is unchanged.

**To adopt.** Optional, and only a JavaScript host gains: .NET already runs morsels across threads.

1. Serve the page with `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy:
   require-corp`, which constrains what cross-origin content it can embed. Without them `optIn` answers
   `false`, nothing changes, and `evalToPreparedAsync` answers as `evalToPrepared` does, so a host may
   call it unconditionally.
2. Ship a worker script that serves the pool (a module worker):
   `import { WorkerPool_serve } from "<the package's compiled DataFrame.js>";`
   `const handle = WorkerPool_serve(m => self.postMessage(m)); self.onmessage = e => handle(e.data);`
3. Opt in before preparing sources (a frame allocated earlier is copied into shared memory once, at
   the first step the pool takes), with a start function that wraps a new worker: `Post` is
   `postMessage`, `Listen` sets `onmessage` (passing `event.data`) and forwards `onerror` as `null`,
   `Stop` is `terminate`. `navigator.hardwareConcurrency - 1` workers is the natural count.
4. Evaluate through `evalToPreparedAsync`. **From the page's thread**, await it: the caller drains
   morsels and then yields to the page's loop until the workers report, never blocking. **From a
   dedicated worker** that hosts the whole evaluator (and starts the pool's workers itself), await it
   the same way; the page's thread then stays free throughout. `warm` starts the workers ahead of the
   first step; otherwise steps run on the caller's thread until the workers report ready.

Measured: at 100,000 rows and up, 3.4 to 5.2 times faster under node and 3.2 to 4.7 times in Edge
(`benchmarks/results/2026-10-06-i7-9700-phase-376.md`).

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

### The pivot aggregates per pair through streams, and the Table boundary fills its selection with a loop (Phase 353) — none, `performance`

**What changed.** No public surface moves, and no answer. The pivot aggregates each (group, on-value)
pair through Phase 323's streams, one stream per pivot column, and falls back to `Column.aggregate`
exactly where the streams do. `Frame.physical` fills the identity selection with a counted loop. Under
node the pivot reads 0.42 to 0.44 of its Phase 267 row-frame estimate (it read 1.08 to 1.17 before), and
`lines` reads 0.24 to 0.35 of 8a39a42. The answers are byte-identical on node and .NET over the
transform-law vectors and the corpus (`benchmarks/results/2026-10-05-i7-9700-phase-353.md`).

**Nothing to adopt.** The Core half of the phase (`Column.aggregate` reading its aggregate and column
type by pattern rather than union equality) rides Core's 0.35.0 draft; compute's `Median`, `StdDev` and
`CountDistinct` pairs pick it up when the substrate pin rises to 0.35.0.

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

### The substrate pin moves to `Fuaran.Core.*` `0.35.1` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.34.0` to the released
`0.35.1` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.38.0` carry a
`0.35.1` floor on the substrate. The substrate's slot is `breaking (source)` (its Phase 307:
`InvokeError`, `ApplyError` and `QueryError` widened, new refusals, a stricter reader); no exhaustive
match here met a widened union, so no source moved for it. One behavioural break reached this repository:
`Query.validateParams` now refuses a name bound twice (`DuplicateParam`), and a list-read parameter (the
`ColExpr.InParam` membership test) is bound by naming it once per element. `PipelineQueryRegistry.dispatch`
keeps that binding: the substrate's gate is handed the first binding of each list-read name, and every
further binding is held to the same gate in the first one's place (its declared type) before the resolver
runs. A scalar-read name bound twice is now refused `DuplicateParam` where it was resolved to its last
binding. The substrate's Phase 353 half (`Column.aggregate` by pattern) arrives with the raise; no test
here asserts a figure it moves. No `api/` baseline and no wire-surface record moved.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.35.1` with this version, and takes the
substrate's own `0.35.1` source breaks (its `STABILITY.md`) for any substrate type it uses directly. A
caller that bound a scalar pipeline-query parameter twice binds it once.

### The substrate pin moves to `Fuaran.Core.*` `0.36.0` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.35.1` to the released
`0.36.0` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.38.0` carry a
`0.36.0` floor on the substrate. The one substrate move that reached source here is the substrate's
`Query` record gaining `Where` and `OrderBy`: the two full `Query` literals (the reference pair in
`PipelineQueryConformance` and the suite's `query` helper) now name both, empty, which is the
declaration they described before. The substrate's `QueryError` gained an `UnknownColumn` case of the
same shape as `EvalError.UnknownColumn`; where both are in scope (the Fable compile of the node leg) the
three unqualified constructions in `Incremental` now name `EvalError.UnknownColumn`. No exhaustive match
here met a widened union (`QueryError`, `ResolveFault`, `PipelineEvalError`), no codec builder or
`Codec.write` call and no obsolete kit name is used, and the proof-leg kit and prover pin were re-copied
from the substrate as `copies.json` requires (the pin gains a Linux entry for the same prover release).
No `api/` baseline moved.

**The wire surface — `additive`.** A `pipelineQuery` document embeds the substrate's `query`, which now
carries `where` (with its `equalTo` discriminator) and `orderBy`; `api/wire/Fuaran.Compute.PipelineQuery.txt`
is regenerated and records the class `additive`. A reader of an older `pipelineQuery` document is
unaffected: both members are empty by default.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.36.0` with this version and takes the substrate's
own `0.36.0` source breaks (its `STABILITY.md`) for any substrate type it uses directly: a `PipelineQuery`
built from a full `Query` literal names `Where = []` and `OrderBy = []`.

## 0.37.0 — released 2026-10-02 as `v0.37.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-02: the four packages
at `0.37.0` compiled under Fable 5 over the substrate at `0.34.0` (a scratch project referencing
`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance` from the candidate feed, every transitive `Fuaran.Core.*` package from the
released `0.34.0` on nuget.org; `fable_modules` carried all four at `0.37.0`, their packaged `fable/` sources
under the `Fuaran.Compute` namespace, and every `Fuaran.Core.*` package at `0.34.0`; the emitted program ran
under node over a `GroupBy`, a decimal `Quotient` / `Rounded` derive, a sort with a limit, and an `int` derive
beside a decimal `Quotient` over an EMPTY table and a full one, and its prepared-source and incremental answers
— `refresh` and `refreshPrepared` after an edit and an insert — were byte-equal to the full evaluation's, and
the whole node output byte-equal to the same program's on .NET; over the empty table the derived columns were
typed `int` and `decimal` by their expressions, as on the full one). The full gate (`verify.ps1`) ran green on
the release commit through the dispatch queue. The proof leg is CI's, on the push of the release commit.

This version carries two changes over `0.36.0`, both entered below: a derived column is typed by its
expression, not by its first cell (Phase 338, `DECISIONS.md` D5) — BREAKING; and the substrate pin moves to
`Fuaran.Core.*` `0.34.0`, so a consumer raises `Fuaran.Core.*` with `Fuaran.Compute.*`.

`0.36.0` is tagged, so the breaking change below ADVANCED the slot to `0.37.0`. It is a draft until
it is tagged: an additive change rides it, a breaking one already has.

### A derived column is typed by its expression, not by its first cell (Phase 338, `DECISIONS.md` D5) — BREAKING, `behaviour`

**What changed.** `Derive` and `Unpivot` type the column they produce by ONE rule
(`Fuaran.Compute.DataFrame`), which the evaluator, the incremental seam (its row walk and its
chunked path), `SchemaWalk`, the planner and `PipelineQuery.check` all read:

- **Where the typer decides the expression, that is the column's type on every frame** — a full
  one, an empty one and one where every cell is null. `Derive("y", Col "x" + Lit 1)` over an int `x`
  is an `int` column over an empty frame; it was a `string` column. An expression that can produce
  no present value (`Lit Null`, a `Case` of nulls) is `string`, as before.
- **An `Unpivot`'s `value` column is the widening join of its value columns' DECLARED types** —
  an `int` beside a `float` is a `float` column, an `int` beside a `decimal` a `decimal` column, on
  every frame. It was the FIRST value column's declared type, so an `int` and a `float` value column
  melted into an `int` column holding floats.
- **Only the cells decide where the typer cannot:** a derive reading a `Param` or a `Now`, a
  column the schema does not carry, or a join the exact typer keeps apart (a `Case` of an `int` and
  a `float`, `D3`). There Phase 321's widening join of the present cells stands, `string` when there
  is none; and an unpivot of value columns no widening relates (a `string` beside an `int`).
- **A `float` beside a `decimal` in one derived column is REFUSED by name** — a `TypeError`
  `derived column '<name>' joins a float and a decimal: cast one to the other's type first …`.
  Where the expression's own arms (through `Case` and `Coalesce`) carry both, on EVERY frame, an
  empty one included, before any row is evaluated; where only the cells show it, over the whole
  column. An unpivot of a `float` and a `decimal` value column is refused the same way, naming
  `value`. It was typed by the earlier of the two types (Phase 321).
- **No cell value moves.** A present cell is stored as the expression produced it — an `Int` in a
  `float` or `decimal` column stays an `Int` (it widens into the column's type, as the substrate's
  `Table.validate` admits) — so every change above is a column TYPE tag, or a refusal.

**The static readers follow.** `SchemaWalk` states every decided derive's type (it stated only
string-valued ones) and the decided unpivot `value` type. `Plan.isTotal` calls a `Derive` total
only where its expression is total AND its column type is decided; the planner's
`FilterBeforeDerive` drops its string-only clause, so a filter now moves ahead of any total,
decided derive — an `int` or `float` one included — and a derive only its cells type is declined,
by that reason. `PipelineQuery.check` registers a result column of an `int`, `float`, `decimal` or
`bool` derive; `ResultDisagreement.TypeUndecidable` is now only the data-decided remainder above.

**The law vectors that moved.** `conformance/laws/transform-laws.json` is re-emitted at `0.37.0`:

- `transform-5-div-by-zero` and `transform-13-div-by-zero` — `Derive("q", v / 0)` answers `Null` on
  every row, and the column `q` is now `float` (it was `string`); the cells are unchanged.
- Eight vectors are APPENDED after the decimal ones, `transform-39` … `transform-46`, the typing
  shapes: a decided `int` and `decimal` derive over an empty and an all-null frame, a `Case` of an
  `int` and a `float` (the cells decide), an `int`/`float` unpivot over a full and an empty frame,
  and the `float`-beside-`decimal` refusal. `iterations` is `47`.
- Every other vector is byte-identical, the fourteen other base vectors included.

**Additive, beside it:** the `DeriveTypingConformance.laws` family in
`Fuaran.Compute.Conformance` — one type on every frame, the walk is the evaluator, refresh is full,
every present cell admitted by its column's type, and the refusal — with its go-red against the
first-present-cell rule in the suite.

**Migrating.** A consumer that read a derived column's type off an empty or all-null result reads
the expression's type now; one that relied on `string` there must say so with a `Cast`. A pipeline
that put a float beside a decimal in one derived column must `Cast` one to the other.

### The substrate pin moves to `Fuaran.Core.*` `0.34.0` — a dependency raise; no public surface moves

**What changed.** Every `Fuaran.Core.*` package this repository pins moves from `0.33.0` to the released
`0.34.0` (`FuaranCoreVersion` in `Directory.Packages.props`), so the four packages at `0.37.0` carry a
`0.34.0` floor on the substrate. One source break in the substrate reached this repository:
`QueryCodec.decode` now answers its refusal as a `string` rather than an `ExecutionFailed` (the
substrate's Phase 295), and `PipelineQueryCodec.decodeJson` reads it as one — a refused `query` member
is still reported as `query: <reason>`. No `api/` baseline and no wire-surface record moved.

**Migrating.** A consumer raises `Fuaran.Core.*` to `0.34.0` with this version, and takes the
substrate's own `0.34.0` source breaks (its `STABILITY.md`) for any substrate type it uses directly.

## 0.36.0 — released 2026-10-02 as `v0.36.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-10-02: the four packages
at `0.36.0` compiled under Fable 5 over the substrate at `0.33.0` (a scratch project referencing
`Fuaran.Compute.DataFrame`, `Fuaran.Compute.ColumnOps`, `Fuaran.Compute.PipelineQuery` and
`Fuaran.Compute.Conformance` from the candidate feed, every transitive `Fuaran.Core.*` package from the
released `0.33.0` on nuget.org; `fable_modules` carried all four at `0.36.0`, their packaged `fable/` sources
under the new paths and the `Fuaran.Compute` namespace; the emitted program ran under node over a `GroupBy`, a
decimal `Quotient` / `Rounded` derive and a sort with a limit, and its prepared-source and incremental answers
— `refresh` and `refreshPrepared` after an edit and an insert — were byte-equal to the full evaluation's, and
to the same program's answers on .NET). The full gate (`verify.ps1`) ran green on the release commit through
the dispatch queue. The proof leg is CI's, on the push of the release commit.

The first version under the packages' own ids (Phase 322, `DECISIONS.md` D4). The slot was opened as
`0.35.0` by Phase 268, which makes a prepared source a persistent VERSION — chunked columns that
successive edits share — and adds the op algebra and the refresh over it; `0.34.0` is tagged, so
those additions advanced the slot rather than ride it. Phase 322's rename is a change of a higher
class than anything the `0.35.0` draft carried as its own, so it ADVANCED the draft to `0.36.0`
rather than reclassing it: **`0.35.0` was never released under its own number, under either set of
ids**, and every entry it carried is folded in below. The four packages ship at this version
together.

**The entries after the first name each package by the id it carried when the change was made.**
Read them through the mapping in the first entry: every one of them ships under the new id at this
version, and `Fuaran.Core.DataFrame.PipelineQuery` (Phase 281) was never published under that id at
all — its first release is `Fuaran.Compute.PipelineQuery` `0.36.0`.

### The packages take their own ids: `Fuaran.Compute.*` package ids and the `Fuaran.Compute` namespace (Phase 322, `DECISIONS.md` D4) — BREAKING, `removal`

**What changed.** Every package this repository produces is renamed, and every public type and module
moves from the `Fuaran.Core` namespace to `Fuaran.Compute`. The module names inside — `DataFrame`,
`ColumnOps`, `Transform`, `Incremental`, `Delta`, `Plan`, `RowIdentity`, `DataFrameConformance`,
`PipelineQuery` and the rest — and every member, type shape and wire byte are unchanged.

| Old id (last published) | New id (first published) |
|---|---|
| `Fuaran.Core.DataFrame` (`0.34.0`) | `Fuaran.Compute.DataFrame` (`0.36.0`) |
| `Fuaran.Core.Column.Ops` (`0.34.0`) | `Fuaran.Compute.ColumnOps` (`0.36.0`) |
| `Fuaran.Core.DataFrame.Conformance` (`0.34.0`) | `Fuaran.Compute.Conformance` (`0.36.0`) |
| `Fuaran.Core.DataFrame.PipelineQuery` (never published) | `Fuaran.Compute.PipelineQuery` (`0.36.0`) |

**The old ids stop at `0.34.0`.** Nothing in this repository packs them any more, they are not
republished, and no deprecation package is published under them: nuget.org keeps `0.34.0` (and every
earlier version) restorable for a consumer that has not moved. A consumer that raises its pin past
`0.34.0` must move to the new ids — there is no `0.36.0` under the old ones.

**Why it is breaking, measured rather than asserted.** The `Public surface` family reads each new
id's baseline against `v0.34.0`'s baseline under the id it replaced (the predecessor map in
`PublicSurfaceTests.fs`) and classes all three published packages `removal`: every externally
visible type left `Fuaran.Core` for `Fuaran.Compute`. The wire record is UNCHANGED by the rename —
the canonical bytes name no CLR type, and each new `api/wire/` baseline's body is byte-identical to
its predecessor's — so the `breaking` its header states since `v0.34.0` is the decimal's (Phases 277
and 321, below), carried across the rename rather than lost to a first snapshot.

**What a consumer changes.**

- Each `PackageReference` / `PackageVersion` naming an old id names its new id, at `0.36.0`.
- Each source file that reaches a compute type adds `open Fuaran.Compute` beside its
  `open Fuaran.Core`; the substrate's types (`Table`, `Cell`, `ColumnType`, `AggFn`, the law kit's
  `LawResult`) stay in `Fuaran.Core`. A fully qualified `Fuaran.Core.DataFrame.evalPipeline` becomes
  `Fuaran.Compute.DataFrame.evalPipeline`.
- The pre-split `Conformance.<family>` spellings still resolve for a file that opens both
  namespaces (the forwarding module is now `Fuaran.Compute.Conformance`); a fully qualified
  `Fuaran.Core.Conformance.<family>` spelling of a dataframe family does not.
- *(Added 2026-10-05 from the consumer sweep, fuaran-core#361.)* The substrate pin moves with the
  version taken: `0.36.0` carries a `Fuaran.Core.*` floor of `0.33.0`, and `0.37.0` a floor of `0.34.0`.
  A direct `Fuaran.Core.*` pin below the floor is a package downgrade (NU1605), and where a build does
  not treat that warning as an error it runs the compute assemblies over a substrate older than the one
  they were compiled against. Raising the substrate pin brings the substrate's own source breaks
  between the two versions with it (its `STABILITY.md`), so the move is the substrate raise and the
  rename together.
- *(Added 2026-10-05, fuaran-core#361.)* An old id can stay in a consumer's graph after its own pins
  have moved, carried transitively by another package built on it. Both assemblies then load, and
  their types share simple names (`Transform`, `DataFrame`, `DataFrameCodec`): in a file that opens
  both namespaces an unqualified name resolves to the namespace opened LAST, so `open Fuaran.Compute`
  goes after `open Fuaran.Core`. The graph is clean only when the package carrying the old id is
  itself raised past it; `dotnet list package --include-transitive` names it.

**The projects, not only the ids, moved:** `src/Fuaran.Compute.DataFrame/`, `src/Fuaran.Compute.ColumnOps/`,
`src/Fuaran.Compute.PipelineQuery/` and `src/Fuaran.Compute.Conformance/`. The solution, the test,
benchmark and proof-oracle projects keep the repository's name (`Fuaran.Core.Compute.*`): the
repository is not renamed, and none of those projects ships.

- **Additive — a new package, `Fuaran.Core.DataFrame.PipelineQuery`: the registered pipeline query
  (Phase 281).** `PipelineQuery` (`Query`, `Pipeline`, `Sources`) pairs the substrate's `Query`
  declaration with the `Transform` pipeline that is its body, and declares the schema of every
  named source the pair reads. `PipelineQueryRegistry` (`empty`, `register`, `tryFind`,
  `enumerate`, `declarations`, `dispatch`) is opaque, so a pair whose pipeline disagrees with its
  declaration cannot be in one: `register` refuses a duplicate id with the substrate's own
  `DuplicateQuery`, then holds the pair to `PipelineQuery.check` — every named source declared
  (`SourceUndeclared`), the `SchemaWalk` output closed (`ResultSchemaOpen`) and equal to the
  declared `ResultSchema` in names, order and types (`ResultColumn` with a `ResultDisagreement`:
  `NotProduced`, `Undeclared`, `Duplicated`, `OutOfOrder`, `TypeDiffers`, `TypeUndecidable`), and
  the parameter reads (`PipelineQuery.paramReads`, each a `ParamRead.Scalar` or `.List` at the type
  its position decides) agreeing with the declared parameters both ways (`ParamUndeclared`,
  `ParamReadAs`, `ParamReadAsScalarAndList`, `ParamUnread`). `dispatch` IS the substrate's
  `QueryRegistry.dispatch` over the declarations — `NoSuchQuery`, `Query.validateParams`, the
  `Deferred` envelope's three outcomes and the unreachable `Ok(Failed _)` — and hands the resolver
  the pair with the arguments substituted (`PipelineQuery.substitute`). `PipelineQueryCodec`
  (`encode` / `decode`, `encodeJson` / `decodeJson`) is its canonical wire form, in `api/wire/`.
  **One limit worth knowing before adopting it:** a `Derive` of anything but a string has a type
  only the data decides (the evaluator types a derived column from its first present cell and falls
  back to `StringType` over an empty frame), so a pair whose declared result carries such a column
  is refused as `TypeUndecidable`; declare it through `Project` / `GroupBy` over a typed column.
  The package takes `Fuaran.Core.Query` by package (and `Fuaran.Core.Function` through it); the
  compute boundary test admits that widening for this package and the families' package only.
- **Additive — `Fuaran.Core.DataFrame.Conformance`: `PipelineQueryConformance.laws` (Phase 281).**
  On the substrate's `queryLaws` pattern: every agreement and parameter refusal built each
  iteration, the substrate's registry refusals, the three dispatch outcomes, the bound pipeline
  evaluated against the same pipeline written with the arguments as literals, and the codec
  round-trip. `Unconditional`, refusals `Built`. The package now also references
  `Fuaran.Core.DataFrame.PipelineQuery`. Additive over the untagged draft, so both ride `0.35.0`.

- **Additive — `Fuaran.Core.Column.Ops`: `ColumnOps.applyPrepared`, `canApplyPrepared`,
  `invertPrepared`, `deltaOfPrepared`.** `apply` / `canApply` / `invert` / `deltaOf` over a
  `Prepared`, with the same verdicts: `applyPrepared op p` answers a new version at the cost of what
  the op touches — a `SetCell` copies one chunk of one column, an `AppendRows` the partial last chunk
  of each, a `SetColumn` keeps every chunk whose cells it did not move — and leaves `p` untouched,
  which is what `invertPrepared` reads. On a coherent table, `apply op t` is
  `applyPrepared op (prepare t)` read back through `toTable`, and the suite holds it so op by op;
  the `Table` forms are unchanged and stay what the proved model and its oracle certify.
- **Additive — `Fuaran.Core.DataFrame`: `DataFrame.toTable : Prepared -> Table`.** The table a
  prepared source stands for: the very table it was prepared from, or — for a version an edit or a
  chunked refresh produced — built from its chunks on first read and kept, with every column an
  edit did not move handed back as the list it already was.
- **Additive — `Fuaran.Core.DataFrame`: `Incremental.refreshPrepared`, `refreshOnPrepared`,
  `resultPrepared`, `chunksTouched`.** `refresh` against a prepared version. Over a pipeline made
  only of `Derive`s it is the chunked path: a chunk the version shares with the one the state was
  last evaluated over — the same object, in every column — is recognised by identity and its output
  chunks reused, only the chunks the version moved are evaluated, and the output shares every chunk
  it did not; `primePrepared` builds the same shape. `chunksTouched` counts the chunks a state's
  evaluation touched (`None` off that path); `resultPrepared` hands the result on as a version, so
  a node feeding a node pays no boundary. Any other pipeline takes the row-local walk over the
  version's table, as `refresh` does. Every result still equals the reference's.
- **No surface change — the state's table and result are built on first read.** A state's
  `source` and `result` are the same tables they were; a chunked refresh no longer builds them to
  hand the state back, and `result` builds the output the first time a consumer asks.

- **Additive — `Fuaran.Core.DataFrame`: the planner (Phase 269).** `Plan.rewrite : Schema ->
  Transform list -> Transform list` (total, idempotent), `Plan.isTotal : Schema -> Transform -> bool`
  (the totality verdict over Phase 266's typer) and `Plan.explain : Schema -> Transform list ->
  PlanReport`, with the report vocabulary `RewriteClass` (`TopN` / `PruneColumns` /
  `FilterBeforeSort` / `FilterBeforeDerive`), `PlanRewrite`, `PlanDeclined` and `PlanReport`. Every
  evaluator entry point plans the pipeline before it folds it and runs `Sort` > `Limit` as one stable
  top-n; the answer is the reference's, errors included. `DataFrame.evalPipelineAsWritten` and
  `evalPipelineWithInEnvAsWritten` fold the pipeline exactly as given — the semantics the planner is
  held to (`Conformance.plannerLaws`) and what a host with a planner of its own certifies against.
  `Incremental.planOver : Schema -> Transform list -> IncrementalPlan * PlanReport` classifies the
  planned form, which is what the seam runs since this phase, and `Incremental.plannedOf` reports
  the form a state ran; `Incremental.plan` and `pipelineOf` are unchanged and read the pipeline as
  written. A `Filter` is moved ahead of a `Derive` only where the derive is total, unread by the
  filter, closed over the schema, AND the derived column's type is one the typer decides (an
  expression whose present values are all strings, or none): the evaluator infers a derived column's
  type from its cells, so any other reorder could change the output schema — a premise the phase
  corrected rather than implemented. Adjacent `Filter`s are not fused: the three-valued `And` reads
  both operands on every row, so a fused predicate would do more work than the two passes.
- **Additive — `Fuaran.Core.DataFrame.Conformance`: `DataFrameConformance.plannerLaws` (and its
  `Conformance.plannerLaws` forward).** The planned evaluation equals the reference as written over
  generated (schema, pipeline, table) triples, byte-for-byte on `Ok` and the same `EvalError` on
  `Error`; `Plan.rewrite` is idempotent; a step `isTotal` admits never errors over the drawn table.
  `Guarded` over the rewrite classes: fusion, pruning and a reorder must each be reached, a reorder
  declined, and the refused arm drawn.
- **No surface change — a tick mints each row's key once (Phase 273).** `Delta.diff` remembers the
  keys it mints for a table object, reads them back when it meets that object again under the same
  `RowIdentity.Scheme`, and the delta it returns carries the new table's keys to `Incremental.refresh`,
  which keys the source itself only when the delta carries none (hand-built, composed, decoded,
  ordinal, or diffed into another table object). Every answer, footprint, delta and wire byte is
  unchanged. The one sharpened reading: two witnesses sharing a `Scheme` must key every table
  identically, which is what a scheme naming its keying rule already meant.
- **Additive — `Fuaran.Core.DataFrame`: `RowIdentity.withKeyEquality`, `RowIdentity.checkKeyEquality`,
  `KeyEqualityDisagreement` (Phase 284).** `withKeyEquality equality w` returns a copy of `w` that
  declares the equality its `KeyString` agrees with, so `Delta.diff` pairs that witness's rows by the
  typed id (Phase 283's path, until now reachable only by `byColumn` / `byColumns`, which are declared
  through it too). `w` is left undeclared. The declaration is the caller's promise and is used as given:
  `Equals a b` exactly when `KeyString a = KeyString b`, equal ids hashed alike. A false one silently
  misses a `DuplicateIdentity` or mis-pairs rows. `checkKeyEquality w table` checks the promise over a
  table for a consumer's test suite and names the first disagreement (or `NotDeclared`); nothing in
  the library calls it. `RowIdentity`'s record shape, every delta, refusal and wire byte of an existing
  witness are unchanged. Additive over the untagged draft, so it rides `0.35.0`.

### The C# dataframe facade is removed, and the substrate pin rises to `0.33.0` (Fuaran.Core's Phase 231 and DECISIONS.md D28) — BREAKING, `removal`

**What changed.** `Fuaran.Core.DataFrame.CSharp` is no longer produced by this repository. It was cut
in the Fuaran.Core repository at `0.32.0` (its Phase 257) as the dataframe half of the C#-shaped
facade — `Expr`, `Step`, `Pipeline`, the slot types and the dataframe vocabularies in the
`Fuaran.Core.CSharp` namespace — built over that repository's `Fuaran.Core.CSharp`, and shipped here
at `0.33.0` and `0.34.0`. Fuaran.Core removed `Fuaran.Core.CSharp` at its `0.33.0` (its Phase 231,
re-measuring D28's premise: the consumer the facade was shipped for never adopted it), so a pin at
`0.33.0` cannot restore this package's dependency, and that repository's record names this removal as
the same change-set that raises the pin. So both halves go, and D28's other criterion — a C# veneer
generated from the IDL by a source generator — is the route by which one returns. With the package
went its proof project (`tests/Fuaran.Core.DataFrame.CSharp.Proof`, and the gate stage that ran it),
its baseline (`api/Fuaran.Core.DataFrame.CSharp.txt`), its entries in `fable-exclusions.json` (now
empty), `proofs/coverage-exclusions.json` and `copies.json` (the five proof legs copied from
Fuaran.Core's facade proof), its row in the README roster, and `Fuaran.Core.CSharp` from the
substrate the `Compute boundary` tests allow (four packages, where there were five).

| Package id | Last emitted here | Continues from |
|---|---|---|
| `Fuaran.Core.DataFrame.CSharp` | `0.34.0` | nowhere — removed, not moved |

**Class: `removal` — breaking, and it RIDES this slot.** A package id a consumer can pin stops being
produced. `0.35.0` is an untagged, publicly unpinned draft, and before `1.0` the minor position this
slot already advanced is the one a breaking change takes, so the number does not move. The surface
gate reads the baselines of the packages the tree still ships, so a package that leaves takes its
baseline with it and no class is printed for it: the class of this entry is its statement rather than
a gate output.

**The substrate pin (no class for a consumer of the three packages).** `FuaranCoreVersion` rises from
`0.32.0` to `0.33.0`, so the three packages now depend on `Fuaran.Core.Column`, `Wire`, `OpStream`
and `Conformance` at `0.33.0`. Their own public surfaces do not move for it: the adaptations are
internal (the codecs' `NotJson` message now comes from `Json.parseDetailed`, and each law family
built by `DataFrameFamilies` fills the adequacy class and refusal verdict that Core `0.33.0` carries
on `LawFamily`, read from the census and audit rows this package already declared).

**What adopting it costs.**

- **A consumer that pins `Fuaran.Core.DataFrame.CSharp`** keeps restoring what it pinned — `0.32.0`
  to `0.34.0` stay on nuget.org — and cannot raise it past `0.34.0`. To move its other pins to
  `0.35.0` it drops the reference and constructs `ColExpr` and `Transform` values through the F#
  surface, wrapping only what it authors.
- **A consumer that never referenced the package** changes nothing beyond taking the substrate at
  `0.33.0` or later.

### Decimal arithmetic in the transform evaluator, and rounding as a typed policy (Phase 277) — BREAKING, `union-widening`

**What changed.** The substrate at `0.33.0` carries an exact decimal (`ColumnType.DecimalType`,
`Cell.Decimal` of canonical text; Core `DECISIONS.md` D72), and the evaluator now computes over it.
Exact where the operation is closed, a STATED rounding where it is not, refused by name where a
pipeline has not said, and never through a float unless the pipeline says `Cast`:

- **Exact, and a `Decimal`:** `Add`, `Sub`, `Mul` and `Mod` over two decimals or a decimal and an
  `int` (an int promotes, losslessly); negation by `Sub`; `Abs`; `Least` and `Greatest`; the six
  comparisons, by the column layer's exact order (`Cell.compare`). Nothing overflows — the digits are
  strings. A decimal `Mod` by zero is `Null`, as an int's is.
- **Rounding is a typed policy (new types).** `RoundingMode` — `HalfEven`, `HalfUp`, `HalfDown`,
  `Up`, `Down`, `Ceiling`, `Floor`, the seven `java.math.RoundingMode` names, shipped whole — and
  `Rounding = { Scale: Slot<int>; Mode: RoundingMode }`. The scale is a literal or a named param, as
  `Limit`'s count is: it is reported by `ColExpr.paramsOf` / `Transform.paramsOf`, substituted by
  `substitute` (bound only to an `Int`), unbound is `UnboundParam`, a wrong shape a `TypeError`
  naming the rounding scale, and outside `0 .. 1000` a `TypeError` naming the range. An unknown mode
  cannot be written; its spelling exists only in the codec.
- **Two new `ColExpr` cases.** `Quotient(dividend, divisor, rounding)` — the exact quotient of two
  exact numbers, correctly rounded (long division to the scale, the remainder deciding the last
  digit under the mode: one rounding of the exact value, never two); a zero divisor is `Null`, as
  `Div`'s is. `Rounded(expr, rounding)` — an exact number brought to the scale. Both take `Decimal` or
  `Int` operands (an int promotes), answer a `Decimal`, propagate a null, and refuse a `Float` naming
  the `Cast`. Evaluation order: the scale, then the operands left to right.
- **`Round`, `Floor` and `Ceil` stay unary and are total over a decimal:** each is exactly a scale-0
  `Rounded` under its pinned mode — `HalfUp`, `Floor`, `Ceiling` — through the same rounding kernel,
  answering a `Decimal`. Over an int or a float they are unchanged.
- **Refused by name:** `Binary(Div, …)` over a decimal names `Quotient` and its rounding; `Sqrt` of
  a decimal and any decimal beside a `float` — in arithmetic, a comparison, `Quotient` or `Rounded` —
  name the `Cast` that resolves them. `ColumnType.widens` refuses that retype in either direction,
  so the evaluator does too.
- **`Cast`:** to `decimal` from an `int` (exact), a string (the decimal grammar) or a `float` — the
  one place an approximation enters a decimal: the float's shortest round-trip digits, laid out
  without an exponent. From `decimal` to `float` (the nearest float; past the float range a
  refusal), to `int` (truncated toward zero; past `int32` an `OverflowError`) and to `string`.
- **Keys:** `GroupBy`, `Distinct`, `Intersect`, `Except`, `Pivot` and `Window` partitions key a
  decimal on its canonical token (`Cell.token`'s `m:` spelling, so `1.50` and `1.5` are one value).
  A `Join` key and a pivot value match a decimal to a decimal of the same value and to nothing
  else: not to a float, and not to an int either, because an int already matches the float of its
  value there and one hash token cannot hold both. `InList` / `InParam` compare by the exact order,
  and a float item beside a decimal subject is a type error. `Sort` is the exact order.
- **Windows:** `CumulSum` and `RollingSum` over a decimal column are exact and `decimal`;
  `RollingMean` stays a `float`, as `Mean` over a decimal column is (D72 K7), each value read at its
  nearest float. `SchemaWalk` types a running total as the evaluator does — `decimal` over a decimal
  column, `float` over another, and unknown where the source column's type is unknown (it was
  `float` there; the new answer is the honest one).
- **The typer and the planner.** The typer gives every decimal operand's result type, and `Decimal`
  for `Quotient` / `Rounded` over exact operands. The totality verdict admits decimal `Add` / `Sub` /
  `Mul` / `Mod`, an exact comparison, `Abs`, `Round` / `Floor` / `Ceil` of a decimal, a `Cast` to
  `decimal` from an `int` or a decimal, and `Quotient` / `Rounded` over exact (or null) operands at
  a literal scale in range; it declines a decimal `Div`, a decimal beside a float, a param scale and
  a literal scale out of range. `proofs/Pipeline.fst` carries the decimal in `column_type` and
  `cell`, the two nodes and the rounding types, their two primitives in `prims`, and the clauses in
  its typer and verdict; `verdict_sound` verifies (every query 3/3 under `--quake`), and the
  committed extraction `proofs/oracle/Pipeline.fs` is byte-identical to a fresh one.
- **The dense frame:** a decimal column is packed boxed and every kernel over it reads the
  reference arm; its typed vector is Phase 280's.
- **The wire:** a decimal literal is `{"$type":"Decimal","value":"<decimal text>"}` in a pipeline
  and `{"$type":"Decimal","v":…}` in a columnar op — a JSON string, canonicalised on read, an
  integer token read as exact, a fractional number token refused (D72 K5). The nodes are
  `{"$type":"quotient","dividend":…,"divisor":…,"rounding":…}` and
  `{"$type":"rounded","expr":…,"rounding":…}`, with `rounding` =
  `{"mode":"half-even"|"half-up"|"half-down"|"up"|"down"|"ceiling"|"floor","scale":<int slot>}`; an
  unknown mode is an `UnknownType` decode error naming the seven.
- **The law vectors.** `conformance/laws/transform-laws.json` gains 23 decimal vectors after the 16
  it carried, which are byte-for-byte as they were: a decimal column through every verb, the exact
  arithmetic, `Quotient`, `Rounded`, `Round` / `Floor` / `Ceil`, the casts, and four refusals.
  `plannerLaws` draws a decimal column and guards a `decimal sample`; `incrementalLaws` evaluates a
  decimal derive and a decimal sum through the seam.

**Class: `union-widening` — breaking, and it RIDES this slot.** `ColExpr` gains `Quotient` and
`Rounded`, so an exhaustive `match` over `ColExpr` stops compiling; `RoundingMode` and `Rounding` are
new (`api/Fuaran.Core.DataFrame.txt`). The wire baselines of `Fuaran.Core.DataFrame` and
`Fuaran.Core.Column.Ops` gain the decimal cell, the decimal column type, the two nodes and the seven
modes. The wire gate classes the move `breaking` for one reason worth naming: the documents it names
for `Slot<Int32>.Lit` and `.Param` are now first reached through a rounding's scale rather than
through `Limit`, so those two specimens' bytes moved; no byte any existing pipeline emits changed,
and the `Transform.Limit` document is as it was. `0.35.0` is an untagged, publicly unpinned draft
that already carries a breaking move (the entry above), so the number does not move.

**What adopting it costs.** A consumer that matches `ColExpr` exhaustively adds the two arms. A host
evaluator certifying against the law vectors meets the decimal ones: until it computes over decimals
and the two nodes it refuses where the reference answers, and the parity law names the vector.

### The decimal across the strand beyond the evaluator (Phase 321) — BREAKING, behaviour

**What changed.** No public signature moved (the `api/` baselines and the wire-surface records are
as they were); four behaviours did, each recorded in `DECISIONS.md` D2 and D3.

- **`ColumnOps.apply` / `canApply` / `applyPrepared` accept a cell whose type widens into its
  column's** (`ColumnType.widens`): an `Int` in a `FloatType` or `DecimalType` column, stored as
  given. `SetCell`, `SetColumn`, `InsertColumn` and `AppendRows` that the strict check refused with
  `CellTypeMismatch` now apply; a `Float` in a decimal column and a decimal in a float or int column
  are still refused by name. `invert` is unchanged.
- **The columnar op wire writes a decimal's canonical text** (`1.50` is written `1.5`), the spelling
  the decoder already read; no canonical cell's bytes moved.
- **The evaluator's float order puts `NaN` above every value** (the substrate's `Cell.compare`), on
  every host: a `Sort` puts `NaN` after `+Inf` and before the nulls (on .NET it sorted first), and a
  float comparison `x > k` / `x >= k` holds for a `NaN` row while `x < k` / `x <= k` does not (on
  .NET the reverse). Under Fable the old order was not an order at all.
- **A derived column's type is the join of its present cells' types** under `ColumnType.widens`:
  a derive answering an `Int` and a `Float` types its column `FloatType`, an `Int` and a `Decimal`
  `DecimalType`, where the first present cell used to decide (`DataFrame.inferCellType` and the
  incremental seam's typing agree with it). A pair no widening relates keeps the earlier type, as
  before. `DataFrame.typeOf` is unchanged: the static typer stays exact.

Also: FS0025 is a compile error in every project (`Directory.Build.props`); the conformance families
`aggregateParityLaws`, `columnarOpLaws`, `schemaWalkLaws`, `paramLaws` and `IncrementalDelta.laws`
draw decimals and each carries a `sample adequacy` guard that goes red on a sample with none (so
`aggregateParityLaws` and `paramLaws` move from `Unconditional` to `Guarded`, and four families
report one more law each); the `ColumnOps` and `Pipeline` proof models carry the decimal column type
and the widening join. `conformance/laws/transform-laws.json` gains 13 vectors after the 39 it carried
(which are byte-identical): nine `columnOp` vectors and four `delta` vectors over decimal columns, two
case kinds a host meets when it next raises its copy. `0.35.0` is an untagged, publicly unpinned draft
that already carries a breaking move, so the number does not move.

**What adopting it costs.** A consumer that relied on the strict `cellFits` to refuse an int in a
float or decimal column adds its own check. A consumer that sorted or filtered floats holding `NaN`
on .NET sees `NaN` move from first to last. A consumer that counted a family's `LawResult` list by
length counts one more. A derive mixing ints with floats or decimals now yields a column the
substrate's validator accepts, where it yielded one it refused.

## 0.34.0 — released 2026-09-27 as `v0.34.0`

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-09-27: the three F#
packages at `0.34.0` compiled under Fable 5 over the substrate at `0.32.0` (a scratch project referencing
`Fuaran.Core.DataFrame`, `Fuaran.Core.Column.Ops` and `Fuaran.Core.DataFrame.Conformance` from the candidate
feed, every transitive `Fuaran.Core.*` package from the released `0.32.0`; `fable_modules` carried all three at
`0.34.0`; the emitted program ran under node and its prepared-source and incremental answers were byte-equal to
the full evaluation's). The C# facade package is excused from Fable as its parent is. The full gate
(`verify.ps1`) ran green on the release commit through the dispatch queue, and the proof leg verified
`ColumnOps` and `Pipeline` on the Phase 267 tree. The law corpus copy was re-emitted at this version with every
vector unchanged.


Opened by Phase 267, which makes the evaluator's working frame column-major and typed, and adds the
one public surface that earns its place over it. The four packages ship at this version together;
`0.33.0` is tagged, so the additions advance the slot rather than ride it.

- **Additive — `Fuaran.Core.DataFrame`: `Prepared`, `DataFrame.prepare`, `DataFrame.evalPrepared`.**
  A source prepared once for many evaluations: `prepare : Table -> Prepared` pays the `Table`
  boundary — one typed unpack per column — once, and `evalPrepared resolve env pipeline prepared`
  evaluates over it with the same parameter shapes, the same cells and the same errors as
  `evalPipelineWithInEnv`, which now delegates to it. `Prepared` is opaque (no public member), so
  the working form behind it stays free to move, as the incremental state did behind its type in
  `0.27.0`.
- **Additive — `Fuaran.Core.DataFrame`: `Incremental.primePrepared`, `Incremental.primeOnPrepared`.**
  `prime` / `primeOn` over a `Prepared` — named rather than overloaded, since a let-bound function
  cannot overload. The state built is the state the table form builds (result, footprint, plan,
  source — the prepared table itself), with the reference path evaluating over the prepared form
  and that form held in the state. `refresh` / `refreshOn` take a `Table` as before.
- **Additive — `Fuaran.Core.DataFrame`: `DataFrame.typeOf : Schema -> ColExpr -> ColumnType option`.**
  Phase 266's static typer, internal until this cut: the type of an expression's present values
  where the schema alone decides it, or `None`.
- **No surface change — the dense columnar frame (Phase 267).** The evaluator's frame is one vector
  per column — `int[]`, `float[]`, `bool[]` or `string[]` beside a validity mask where the column's
  cells agree with its declared type, boxed cells where they do not — a selection vector `Filter`
  produces, `Limit` slices and `Sort` permutes, projection as metadata, a `Derive` that adds one
  vector and shares the rest, and gathering verbs that read rows through the selection and emit
  fresh vectors. The compiled expression tree reads typed vectors unboxed where the node's type is
  decided. The transform law vectors are byte-identical: `conformance/laws/transform-laws.json` is
  re-emitted with its `kitVersion` stamp at `0.34.0` and no other byte moved.
- **No surface change — Phases 263 to 266.** Each landed on the `0.33.0` sources after the tag
  without moving a public member. Phase 263: array rows and column indices resolved once per step
  in place of a name lookup per reference per row. Phase 264: a hash join, a one-pass pivot, linear
  rolling windows and a scatter order-restore. Phase 265: grouping, distinct, the set operations,
  pivot's index groups and window partitions on one cell comparer equal to `cellToken`, and the
  seam's maintained grouping re-keyed on it. Phase 266: each step's expression compiled once into a
  closure tree specialised by a static typer, errors through one slot per row.

## 0.33.0 — released 2026-09-26 as `v0.33.0`

**This is the first slot this repository emits (Phase 259), and it is released.** It opens one minor above
the last version the four ids shipped from the substrate repository (`0.32.0`), so every consumer's floor
stays monotone across the change of producer.

**Release record.** The cut-time Fable gate ran green against the candidate on 2026-09-26: the three F#
packages at `0.33.0` compiled under Fable 5 over the substrate at `0.32.0` (a scratch project referencing
`Fuaran.Core.DataFrame`, `Fuaran.Core.Column.Ops` and `Fuaran.Core.DataFrame.Conformance` from the candidate
feed, every transitive `Fuaran.Core.*` package from the released `0.32.0`; `fable_modules` carried all three at
`0.33.0`; the emitted program ran under node). The C# facade package is excused from Fable as its parent is.
The full gate (`verify.ps1`) ran green on the release commit, and the proof leg verified `ColumnOps` and
`Pipeline` once at Phase 259.

- **The producer changed; the contract did not (no class).** `Fuaran.Core.DataFrame`,
  `Fuaran.Core.Column.Ops`, `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`
  are built here from the sources they shipped with at `0.32.0`, under the same package ids and the
  same namespaces, and their public surfaces are the `0.32.0` surfaces unchanged — the committed
  baselines under `api/` were carried with their history and the gate holds each package to its
  own. Adopting `0.33.0` from `0.32.0` is a version raise and nothing else.
- **The substrate is taken by package (no class for a consumer).** The four packages now depend on
  `Fuaran.Core.Column`, `Wire`, `OpStream`, `Conformance` and `CSharp` at `0.32.0` as package
  dependencies, where at `0.32.0` those were built in the same tree at the same number. A consumer
  that pins the substrate packages itself keeps them at `0.32.0` or later.
- **`conformance/laws/transform-laws.json` is emitted here (no class).** The reference answers the
  other hosts certify against are rendered from this repository's evaluator, and the file's
  `kitVersion` stamp reads this repository's `<Version>`. The vectors themselves are byte-identical
  to `0.32.0`'s.
