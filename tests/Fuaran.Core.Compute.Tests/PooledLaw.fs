/// Phase 376 — the laws of the worker pool, as plain functions over whatever workers a host can start.
/// With the pool opted in, every step the pool takes answers what the sequential member
/// (`Kernels.oneThread`) answers, byte for byte as canonical wire (refusals included), and
/// `evalToPreparedAsync` answers what `evalToPrepared` answers over whole pipelines: over the
/// transform vectors' sample at 1, 2, 3 and 8,192 rows a morsel, and over the benchmark corpus's
/// row-local pipelines at 20,000 rows; with the caller draining morsels beside the workers, and with
/// the workers alone. A pool whose runners start every morsel after the first a row late (a shifted
/// boundary), or put each full morsel's kept rows in its partner's slot (out-of-order concatenation),
/// goes red. Without shared memory the opt-in answers `false` and the sequential member answers.
///
/// FSharp.Core, the dataframe package and the conformance kit's generator only, Fable-clean: the
/// suite runs it on .NET over workers on the thread pool (`PooledLawTests`), and the node leg
/// (`Node/PooledLawNode.fsproj`, compiled with Fable and run by the same suite) runs it over
/// `worker_threads`, every job and reply through a structured clone.
module internal Fuaran.Compute.Tests.PooledLaw

open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests.MorselHandOffLaw

// ---- the runners' spans ----------------------------------------------------------------------

/// PERTURBED: every morsel after the first starts one row late, so its first row is never run.
let shifted: WorkerPool.Span =
    fun f resolved h j lo hi -> MorselHandOff.runSpan f resolved h j (if j = 0 then lo else min hi (lo + 1)) hi

/// PERTURBED: a `Filter`'s full morsels in pairs — 0 and 1, 2 and 3, … — each put its kept rows, and
/// their count, in its partner's slot, so the caller concatenates the pair out of morsel order.
let swapped: WorkerPool.Span =
    fun f resolved h j lo hi ->
        let n = h.Rows.Length
        let r = h.MorselRows
        let m = MorselHandOff.morselCount h
        let full = if n % r = 0 then m else m - 1
        let partner = j ^^^ 1

        if h.Root <> 0 || j >= full || partner >= full then
            MorselHandOff.runSpan f resolved h j lo hi
        else
            let rows: int[] = Array.zeroCreate n
            let struct (count, error) = DataFrame.filterMorsel f resolved h.Rows rows lo hi
            Array.blit rows lo h.OutRows (partner * r) count
            h.OutCounts[partner] <- count
            h.OutFailed[j] <- if Option.isSome error then 1uy else 0uy

/// The span a runner named `name` runs: `honest`, `shifted` or `swapped`.
let spanOf (name: string) : WorkerPool.Span =
    match name with
    | "shifted" -> shifted
    | "swapped" -> swapped
    | _ -> MorselHandOff.runSpan

// ---- the comparisons ---------------------------------------------------------------------------

/// A whole pipeline's answer as the text the law compares.
let preparedWire (r: Result<Prepared, EvalError>) : string =
    match r with
    | Ok p -> "ok " + ColumnCodec.encode (Embedded(DataFrame.toTable p))
    | Error e -> "error " + DataFrame.errorString e

/// A pipeline the law runs whole: its name, environment, source and steps.
type Pipeline = string * Map<string, Cell> * Table * Transform list

/// The transform vectors' sample, whole.
let vectorPipelines () : Pipeline list =
    PreparedResultLaw.sample ()
    |> List.mapi (fun i (table, pipeline) -> sprintf "vector %d" i, Map.empty, table, pipeline)

/// The steps the opted-in pool takes over `cases`, each against the sequential member: the
/// disagreements, and how many steps the pool took.
let stepDisagreements (cases: Case list) : Async<string list * int> =
    async {
        let failures = ResizeArray<string>()
        let handed = ref 0

        for c in cases do
            match DataFrame.pooledStep c.Env c.Frame c.Step with
            | None -> ()
            | Some work ->
                handed.Value <- handed.Value + 1
                let! answer = work

                let expected =
                    DataFrame.evalStepWith Kernels.oneThread DataFrame.noResolve c.Env c.Frame c.Step

                if wire answer <> wire expected then
                    failures.Add(sprintf "%s: %s / %s" c.Name (wire answer) (wire expected))

        return List.ofSeq failures, handed.Value
    }

/// `evalToPreparedAsync` against `evalToPrepared` over whole pipelines: the disagreements.
let pipelineDisagreements (pipelines: Pipeline list) : Async<string list> =
    async {
        let failures = ResizeArray<string>()

        for name, env, table, pipeline in pipelines do
            let! answer = DataFrame.evalToPreparedAsync DataFrame.noResolve env pipeline (DataFrame.prepare table)

            let expected =
                DataFrame.evalToPrepared DataFrame.noResolve env pipeline (DataFrame.prepare table)

            if preparedWire answer <> preparedWire expected then
                failures.Add(sprintf "%s: %s / %s" name (preparedWire answer) (preparedWire expected))

        return List.ofSeq failures
    }

/// Opt in with the span named `span` (the host's `start span` starts one worker running it), wait
/// for every worker, run `body`, and opt out.
let private pooled
    (start: string -> unit -> MorselWorker)
    (workers: int)
    (span: string)
    (morselRows: int)
    (callerDrains: bool)
    (body: Async<'a>)
    : Async<'a> =
    async {
        if not (WorkerPool.optInWith (start span) workers 0 morselRows (spanOf span) callerDrains) then
            failwith "the pool refused to opt in where shared memory exists"

        do! WorkerPool.warm ()

        try
            return! body
        finally
            WorkerPool.optOut ()
    }

/// Every law, over workers `start` starts (`start span ()` starts one running the span named
/// `span`), `workers` at a time; each line of the account goes to `log`. Answers whether every law
/// held.
let laws (start: string -> unit -> MorselWorker) (workers: int) (log: string -> unit) : Async<bool> =
    async {
        let ok = ref true

        let fail (failures: string list) =
            ok.Value <- false
            failures |> List.truncate 3 |> List.iter (fun s -> log ("  " + s))

        let vectors = vectorCases ()
        let corpus = corpusCases 20_000
        let answered = corpus |> List.filter (refuses >> not)

        // Not opted in: the asynchronous entry point is the synchronous one.
        WorkerPool.optOut ()
        let! unpooled = pipelineDisagreements (vectorPipelines () @ corpusPipelines 20_000)
        log (sprintf "pool: not opted in, whole pipelines: %d disagree" unpooled.Length)

        if not unpooled.IsEmpty then
            fail unpooled

        for callerDrains in [ true; false ] do
            let who = if callerDrains then "caller+workers" else "workers alone"

            for morselRows in [ 1; 2; 3; Kernels.MorselRows ] do
                let before = WorkerPool.morselsByWorkers ()

                let! failures, handed, wholeFailures =
                    pooled
                        start
                        workers
                        "honest"
                        morselRows
                        callerDrains
                        (async {
                            let! failures, handed = stepDisagreements vectors
                            let! whole = pipelineDisagreements (vectorPipelines ())
                            return failures, handed, whole
                        })

                let ran = WorkerPool.morselsByWorkers () - before

                log (
                    sprintf
                        "pool: %-14s vectors at %5d rows a morsel: %d steps pooled, %d disagree; whole pipelines %d disagree; workers ran %d morsels"
                        who
                        morselRows
                        handed
                        failures.Length
                        wholeFailures.Length
                        ran
                )

                if handed = 0 || (not callerDrains && ran = 0) then
                    fail [ "the pool took no step, or its workers ran nothing" ]

                if not failures.IsEmpty then
                    fail failures

                if not wholeFailures.IsEmpty then
                    fail wholeFailures

            let before = WorkerPool.morselsByWorkers ()

            let! failures, handed, wholeFailures =
                pooled
                    start
                    workers
                    "honest"
                    Kernels.MorselRows
                    callerDrains
                    (async {
                        let! failures, handed = stepDisagreements corpus
                        let! whole = pipelineDisagreements (corpusPipelines 20_000)
                        return failures, handed, whole
                    })

            let ran = WorkerPool.morselsByWorkers () - before

            log (
                sprintf
                    "pool: %-14s corpus at 20,000 rows: %d of %d steps pooled, %d disagree; whole pipelines %d disagree; workers ran %d morsels"
                    who
                    handed
                    corpus.Length
                    failures.Length
                    wholeFailures.Length
                    ran
            )

            if handed <> corpus.Length || (not callerDrains && ran = 0) then
                fail [ "the pool did not take every corpus step, or its workers ran nothing" ]

            if not failures.IsEmpty then
                fail failures

            if not wholeFailures.IsEmpty then
                fail wholeFailures

            for span in [ "shifted"; "swapped" ] do
                let! failures, _ =
                    pooled start workers span Kernels.MorselRows callerDrains (stepDisagreements answered)

                log (
                    sprintf
                        "pool: %-14s %s (PERTURBED) corpus: %d of %d disagree"
                        who
                        span
                        failures.Length
                        answered.Length
                )

                if failures.IsEmpty then
                    fail [ sprintf "the %s pool went green" span ]

        return ok.Value
    }
