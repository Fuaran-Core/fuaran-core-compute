/// Phase 376 — the node leg of the worker pool's laws: `PooledLaw.laws` over node's `worker_threads`,
/// every job and reply through the structured clone `postMessage` makes, the frame's vectors in shared
/// memory. The same module is the main program and every worker: on the main thread it runs the laws,
/// starting workers on its own URL; in a worker it serves the pool with the span its `workerData`
/// names. It also holds the fallback: with `crossOriginIsolated` false, or no `SharedArrayBuffer`,
/// the opt-in answers `false` and the asynchronous entry point answers as the synchronous one does.
///
///   dotnet fable PooledLawNode.fsproj -o <out>; node <out>/PooledLawNode.js
///
/// Prints "pooled laws under node: held" and exits 0 when every law held; exits 1 otherwise.
module Fuaran.Compute.Tests.PooledLawNode

#if FABLE_COMPILER
open Fable.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

[<Import("isMainThread", "node:worker_threads")>]
let private isMainThread: bool = jsNative

[<Import("parentPort", "node:worker_threads")>]
let private parentPort: obj = jsNative

[<Import("workerData", "node:worker_threads")>]
let private workerData: obj = jsNative

[<Import("Worker", "node:worker_threads")>]
let private workerClass: obj = jsNative

[<Emit("new $0(new URL(import.meta.url), { workerData: $1 })")>]
let private spawn (cls: obj) (data: string) : obj = jsNative

[<Emit("$0.postMessage($1)")>]
let private postTo (target: obj) (message: obj) : unit = jsNative

[<Emit("$0.on($1, $2)")>]
let private on (target: obj) (event: string) (handler: obj -> unit) : unit = jsNative

[<Emit("$0.terminate()")>]
let private terminate (worker: obj) : unit = jsNative

[<Emit("process.exit($0)")>]
let private exit (code: int) : unit = jsNative

[<Emit("String($0)")>]
let private text (x: obj) : string = jsNative

/// Make this realm look like a page without cross-origin isolation, or without shared memory, run
/// `body`, and restore it.
[<Emit("(() => { const s = globalThis.SharedArrayBuffer; if ($0) { globalThis.crossOriginIsolated = false; } else { delete globalThis.SharedArrayBuffer; } try { return $1(); } finally { delete globalThis.crossOriginIsolated; globalThis.SharedArrayBuffer = s; } })()")>]
let private withoutSharedMemory (byIsolation: bool) (body: unit -> 'a) : 'a = jsNative

/// A worker on node's `worker_threads`, running this module with the span named `span`; its error
/// event retires it.
let private nodeWorker (span: string) () : MorselWorker =
    let w = spawn workerClass span

    { new MorselWorker with
        member _.Post message = postTo w message

        member _.Listen hear =
            on w "message" hear

            on w "error" (fun e ->
                printfn "worker error: %s" (text e)
                hear null)

        member _.Stop() = terminate w }

/// Without isolation, and without shared memory: the opt-in answers `false`, no pool is active, and
/// the asynchronous entry point answers the corpus as the synchronous one does.
let private fallback () : Async<bool> =
    async {
        let ok = ref true

        for byIsolation, what in [ true, "crossOriginIsolated false"; false, "no SharedArrayBuffer" ] do
            let accepted, active =
                withoutSharedMemory byIsolation (fun () ->
                    let accepted = WorkerPool.optIn (nodeWorker "honest") 2
                    accepted, WorkerPool.isActive ())

            WorkerPool.optOut ()
            let! failures = PooledLaw.pipelineDisagreements (MorselHandOffLaw.corpusPipelines 20_000)

            printfn
                "fallback: %-25s opt-in %s, pool %s, corpus pipelines %d disagree"
                what
                (if accepted then "ACCEPTED" else "refused")
                (if active then "ACTIVE" else "inactive")
                failures.Length

            if accepted || active || not failures.IsEmpty then
                ok.Value <- false

        return ok.Value
    }

if isMainThread then
    Async.StartWithContinuations(
        async {
            let! fallbackHeld = fallback ()
            let! held = PooledLaw.laws nodeWorker 3 (printfn "%s")
            return held && fallbackHeld
        },
        (fun held ->
            printfn "pooled laws under node: %s" (if held then "held" else "FAILED")
            exit (if held then 0 else 1)),
        (fun e ->
            printfn "pooled laws under node: FAILED with %s" e.Message
            exit 1),
        (fun _ -> exit 1)
    )
else
    let handle =
        WorkerPool.serveWith (PooledLaw.spanOf (text workerData)) (postTo parentPort)

    on parentPort "message" handle
#endif
