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
