/// Phase 376 — what the worker pool costs and buys, on the evaluator itself: the two steps 346 and 375
/// timed — `filter` (`a + b > 500` over Layer 6's `rowTable`) and `derive` (`amount = qty * price` over
/// the corpus's `orders`) — sequentially, through the pool at 2, 4 and 8 threads (the caller and 1, 3
/// or 7 workers), and the costs around it reported apart: the pool's start-up (workers importing the
/// evaluator until each reports ready), the copy a hand-off makes of a frame allocated before the
/// opt-in (plan over a plain frame against plan over a shared one), and the steady state (a warm
/// pool, a frame already in shared memory). Every pooled answer is checked byte for byte against the
/// sequential one before it is timed.
///
/// One compiled module is every role. Under node: the main program (`node PoolBench.js [sizes]`), and
/// each pool worker it starts on its own URL. In a browser (`serve.mjs` beside this file): the page
/// (`?mode=main` measures on the page's thread, `?mode=host` in a dedicated worker that hosts the
/// evaluator and starts the pool from there, `?mode=probe` only reports what the page may do), the
/// host worker, and the pool's workers. Prints (or posts to `/result`) markdown table rows.
module Fuaran.Compute.Tests.PoolBench

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests.MorselHandOffLaw

[<Emit("performance.now()")>]
let private nowMs () : float = jsNative

[<Emit("(typeof process !== 'undefined' && !!(process.versions && process.versions.node))")>]
let private isNode () : bool = jsNative

[<Emit("(typeof WorkerGlobalScope !== 'undefined')")>]
let private inWebWorker () : bool = jsNative

[<Emit("self.name")>]
let private webWorkerName () : string = jsNative

[<Emit("import('node:worker_threads')")>]
let private nodeThreads () : JS.Promise<obj> = jsNative

[<Emit("new $0.Worker(new URL(import.meta.url), { workerData: 'pool' })")>]
let private nodeSpawn (wt: obj) : obj = jsNative

[<Emit("new Worker(new URL(import.meta.url), { type: 'module', name: $0 })")>]
let private webSpawn (name: string) : obj = jsNative

[<Emit("$0.postMessage($1)")>]
let private postTo (target: obj) (message: obj) : unit = jsNative

[<Emit("$0.on($1, $2)")>]
let private on (target: obj) (event: string) (handler: obj -> unit) : unit = jsNative

[<Emit("$0.onmessage = (e) => $1(e.data)")>]
let private onMessage (target: obj) (handler: obj -> unit) : unit = jsNative

[<Emit("$0.onerror = (e) => { console.error(String(e && e.message || e)); $1(null); }")>]
let private onError (target: obj) (handler: obj -> unit) : unit = jsNative

[<Emit("$0.terminate()")>]
let private terminate (worker: obj) : unit = jsNative

[<Emit("process.argv.slice(2)")>]
let private argv () : string[] = jsNative

[<Emit("new URLSearchParams(location.search).get($0) || ''")>]
let private query (name: string) : string = jsNative

[<Emit("fetch('/result', { method: 'POST', body: $0 })")>]
let private postResult (body: string) : JS.Promise<obj> = jsNative

[<Emit("(typeof crossOriginIsolated === 'undefined' ? 'undefined' : String(crossOriginIsolated)) + ', SharedArrayBuffer ' + typeof SharedArrayBuffer + ', ' + (typeof navigator !== 'undefined' ? navigator.hardwareConcurrency : '?') + ' logical processors'")>]
let private probe () : string = jsNative

[<Emit("self")>]
let private self: obj = jsNative

// ---- the measurement -----------------------------------------------------------------------------

let private median (xs: float list) : float =
    let a = xs |> List.sort |> List.toArray
    a[a.Length / 2]

let private runsFor (n: int) =
    if n <= 20_000 then 31
    elif n <= 100_000 then 15
    else 7

/// The median of `runs` timed calls of `f`, after three warm-ups.
let private timeSync (runs: int) (f: unit -> 'a) : float =
    for _ in 1..3 do
        f () |> ignore

    median
        [ for _ in 1..runs do
              let t0 = nowMs ()
              f () |> ignore
              nowMs () - t0 ]

/// The same for asynchronous work.
let private timeAsync (runs: int) (f: unit -> Async<'a>) : Async<float> =
    async {
        for _ in 1..3 do
            let! _ = f ()
            ()

        let times = ResizeArray<float>()

        for _ in 1..runs do
            let t0 = nowMs ()
            let! _ = f ()
            times.Add(nowMs () - t0)

        return median (List.ofSeq times)
    }

let private stepsAt (n: int) =
    [ "filter", Map.empty, rowTable n, Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500)))
      "derive", sheetEnv, orders n false, List.head lines ]

let private sequential env f t =
    DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve env f t

let private ms (x: float) = sprintf "%.2f" x

/// Every figure, over workers `start ()` starts, for `threadCounts` (the caller counted) and `sizes`.
let private measure
    (host: string)
    (start: unit -> MorselWorker)
    (threadCounts: int list)
    (sizes: int list)
    (log: string -> unit)
    : Async<unit> =
    async {
        WorkerPool.optOut ()
        log (sprintf "## %s — %s" host (probe ()))
        log ""
        log "| step | rows | sequential | hand-off, caller alone | sequential over shared memory |"
        log "|---|---|---|---|---|"
        // The tables once per size; a frame allocated before any opt-in is a plain one.
        let tables = [ for n in sizes -> n, stepsAt n ]

        let plain =
            [ for n, steps in tables ->
                  n, [ for name, env, table, t in steps -> name, env, table, t, Frame.ofTable table ] ]

        for n, steps in plain do
            for name, env, table, t, f in steps do
                let runs = runsFor n
                let seq = timeSync runs (fun () -> sequential env f t)

                let alone =
                    timeSync runs (fun () -> MorselHandOff.evalWith MorselHandOff.run Kernels.MorselRows env f t)

                // Over shared memory: the frame allocated after an opt-in, no worker started.
                WorkerPool.optIn start 1 |> ignore
                let fs = Frame.ofTable table
                let seqShared = timeSync runs (fun () -> sequential env fs t)
                WorkerPool.optOut ()
                log (sprintf "| %s | %d | %s | %s | %s |" name n (ms seq) (ms alone) (ms seqShared))

        log ""
        log "| threads | workers ready (cold start-up) |"
        log "|---|---|"

        let pooledRows = ResizeArray<string>()

        for threads in threadCounts do
            let workers = threads - 1
            let t0 = nowMs ()

            // Every size through the pool, the floor included: the floor is what this measures.
            if not (WorkerPool.optInWith start workers 0 Kernels.MorselRows MorselHandOff.runSpan true) then
                failwith "the opt-in was refused: no shared memory here"

            do! WorkerPool.warm ()
            log (sprintf "| %d | %s ms |" threads (ms (nowMs () - t0)))

            for n, steps in plain do
                for name, env, table, t, fPlain in steps do
                    let runs = runsFor n
                    let fShared = Frame.ofTable table
                    let planCopy = timeSync runs (fun () -> MorselHandOff.plan env fPlain t)
                    let planShared = timeSync runs (fun () -> MorselHandOff.plan env fShared t)
                    let expected = wire (sequential env fShared t)

                    let work () =
                        match DataFrame.pooledStep env fShared t with
                        | Some w -> w
                        | None -> failwith "the pool did not take the step"

                    let! first = work ()

                    if wire first <> expected then
                        failwithf "%s at %d rows: the pooled answer differs from the sequential one" name n

                    let! pooled = timeAsync runs work

                    pooledRows.Add(
                        sprintf
                            "| %s | %d | %d | %s | %s | %s |"
                            name
                            n
                            threads
                            (ms pooled)
                            (ms planShared)
                            (ms planCopy)
                    )

            WorkerPool.optOut ()

        log ""

        log
            "| step | rows | threads | pooled, steady state | of which plan, shared frame | plan, plain frame (the copy) |"

        log "|---|---|---|---|---|---|"
        pooledRows |> Seq.iter log
        log ""
    }

let private sizesFrom (args: string[]) =
    match args |> Array.tryHead with
    | Some s when s <> "" -> s.Split ',' |> Array.map int |> List.ofArray
    | _ -> [ 10_000; 20_000; 50_000; 100_000; 1_000_000 ]

let private threadCounts = [ 2; 4; 8 ]

// ---- the roles -----------------------------------------------------------------------------------

let private webWorker (name: string) () : MorselWorker =
    let w = webSpawn name

    { new MorselWorker with
        member _.Post message = postTo w message

        member _.Listen hear =
            onMessage w hear
            onError w hear

        member _.Stop() = terminate w }

let private runPage () =
    let mode = query "mode"
    let sizes = sizesFrom [| query "sizes" |]
    let lines = ResizeArray<string>()
    lines.Add(sprintf "# page, mode %s: crossOriginIsolated %s" mode (probe ()))

    let finish () =
        postResult (System.String.Join("\n", lines)) |> ignore

    match mode with
    | "main" ->
        Async.StartWithContinuations(
            measure "Edge, the evaluator on the page's thread" (webWorker "pool") threadCounts sizes lines.Add,
            (fun () -> finish ()),
            (fun e ->
                lines.Add("ERROR " + e.Message)
                finish ()),
            (fun _ -> finish ())
        )
    | "host" ->
        let host = webSpawn "host"

        onMessage host (fun reply ->
            lines.Add(string reply)
            finish ())

        postTo host (box (System.String.Join(",", sizes |> List.map string)))
    | _ ->
        // The opt-in as a host makes it, then the pool at a floor of no rows (so every eligible step
        // is offered to it) over the corpus's whole pipelines: where the opt-in is refused, the
        // asynchronous entry point is the sequential member; where it is taken, the pool's answers
        // are the sequential member's.
        let accepted = WorkerPool.optIn (webWorker "pool") 1
        lines.Add(sprintf "opt-in accepted: %b" accepted)

        if accepted then
            WorkerPool.optInWith (webWorker "pool") 3 0 Kernels.MorselRows MorselHandOff.runSpan true
            |> ignore

        let pipelines = corpusPipelines 20_000

        Async.StartWithContinuations(
            async {
                do! WorkerPool.warm ()
                let disagree = ResizeArray<string>()

                for name, env, table, pipeline in pipelines do
                    let! answer =
                        DataFrame.evalToPreparedAsync DataFrame.noResolve env pipeline (DataFrame.prepare table)

                    let expected =
                        DataFrame.evalToPrepared DataFrame.noResolve env pipeline (DataFrame.prepare table)

                    let text (r: Result<Prepared, EvalError>) =
                        match r with
                        | Ok p -> ColumnCodec.encode (Embedded(DataFrame.toTable p))
                        | Error e -> DataFrame.errorString e

                    if text answer <> text expected then
                        disagree.Add name

                return disagree.Count
            },
            (fun disagree ->
                lines.Add(
                    sprintf
                        "corpus pipelines through evalToPreparedAsync (pool %s): %d of %d disagree with evalToPrepared"
                        (if WorkerPool.isActive () then "active" else "inactive")
                        disagree
                        pipelines.Length
                )

                WorkerPool.optOut ()
                finish ()),
            (fun e ->
                lines.Add("ERROR " + e.Message)
                finish ()),
            (fun _ -> finish ())
        )

let private runHostWorker () =
    onMessage self (fun message ->
        let sizes = sizesFrom [| string message |]
        let lines = ResizeArray<string>()

        let reply () =
            postTo self (box (System.String.Join("\n", lines)))

        Async.StartWithContinuations(
            measure "Edge, the evaluator in a dedicated worker" (webWorker "pool") threadCounts sizes lines.Add,
            (fun () -> reply ()),
            (fun e ->
                lines.Add("ERROR " + e.Message)
                reply ()),
            (fun _ -> reply ())
        ))

let private runPoolWebWorker () =
    let handle = WorkerPool.serve (postTo self)
    onMessage self handle

let private runNode () =
    async {
        let! wt = Async.AwaitPromise(nodeThreads ())

        if not (unbox<bool> (wt?isMainThread)) then
            let port = wt?parentPort
            let handle = WorkerPool.serve (postTo port)
            on port "message" handle
        else
            let start () : MorselWorker =
                let w = nodeSpawn wt

                { new MorselWorker with
                    member _.Post message = postTo w message

                    member _.Listen hear =
                        on w "message" hear
                        on w "error" (fun _ -> hear null)

                    member _.Stop() = terminate w }

            do! measure "node" start threadCounts (sizesFrom (argv ())) (printfn "%s")
            JsInterop.emitJsStatement () "process.exit(0)"
    }
    |> Async.StartImmediate

if isNode () then
    runNode ()
elif inWebWorker () then
    (if webWorkerName () = "host" then
         runHostWorker ()
     else
         runPoolWebWorker ())
else
    runPage ()
#endif
