/// The node leg's timing harness (Phase 262). The same corpus as the BenchmarkDotNet suites
/// (../Corpus.fs), timed with nothing but the wall clock, so the program means the same thing
/// compiled by Fable and run under node as it does on .NET. Every agreement the .NET suites assert
/// is asserted here before a case is timed, and it prints one median table per shape in the shape
/// the results files use. A case the host cannot run is reported in its row, never skipped.
///
/// Usage: `node Program.js [runs]` once compiled (see run-node.ps1), or
/// `dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks/Node -- [runs]`.
/// `runs` is the number of measured samples per case (default 10), after two warm-up calls; a second
/// argument `typed` times only the typed family (`node Program.js 10 typed`), and `state` measures only the
/// incremental state's wire form (`node Program.js 3 state`), and `chain` runs only the prepared-result
/// law and the chain (`node Program.js 10 chain`, Phase 342).
module Fuaran.Core.Compute.Benchmarks.Node.Program

open System
open Fuaran.Core.Compute.Benchmarks

/// The wall clock in milliseconds. `DateTime`, not `Stopwatch`: Fable maps no `Stopwatch` member,
/// and a JavaScript timer binding would need a package beyond FSharp.Core. The clock's coarse
/// resolution under node (a millisecond) is why every sample is batched below.
let private nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

/// The shortest a timed sample may be, so a millisecond clock contributes at most a few per cent.
let private minSampleMs = 50.0

/// Two warm-up calls; then the number of calls per sample is doubled until one sample spans at
/// least `minSampleMs`; then `runs` samples are taken. The median sample, divided by the calls per
/// sample: milliseconds per call.
let private medianMs (runs: int) (f: unit -> 'T) : float =
    f () |> ignore
    f () |> ignore

    let sample (calls: int) =
        let start = nowMs ()

        for _ in 1..calls do
            f () |> ignore

        nowMs () - start

    let rec calibrate calls =
        if calls >= 1_000_000 || sample calls >= minSampleMs then
            calls
        else
            calibrate (calls * 2)

    let calls = calibrate 1
    let times = Array.init runs (fun _ -> sample calls / float calls)
    Array.sortInPlace times
    let mid = runs / 2

    if runs % 2 = 1 then
        times.[mid]
    else
        (times.[mid - 1] + times.[mid]) / 2.0

/// The measured samples per case, set once from the command line.
let private runsRef = ref 10

/// Thousands separated with commas, the way the tables print row counts.
let private rows (n: int) : string =
    let s = string n
    let len = s.Length

    [ for i in 0 .. len - 1 do
          if i > 0 && (len - i) % 3 = 0 then
              yield ","

          yield string s.[i] ]
    |> String.concat ""

/// One measured cell: the median, or why there is none. A case the HOST cannot run (the evaluator
/// exceeding node's call stack on a large table is the recorded instance) is reported in its row
/// rather than aborting every table after it; a corpus disagreement is never swallowed, because
/// that would turn a wrong answer into a missing figure.
let private notMeasured (e: exn) : string =
    "not measured: " + e.Message.Split([| char 10 |]).[0].Trim()

let private attempt (measure: unit -> float) : string =
    try
        sprintf "%.3f" (measure ())
    with e when not (e.Message.StartsWith "benchmark corpus:") ->
        notMeasured e

/// Which arm a case times. An evaluator case is measured only once the corpus check has run on this
/// host, since that check is what makes its figure a figure about the right answer. A hand-arm case
/// needs nothing from the evaluator, so it is measured whatever the check did; if the check could not
/// run on this host its figure says so, and its agreement stands on the .NET leg, which checks the
/// same generated inputs.
type private Arm =
    | Evaluator
    | HandArm

/// One size's rows: build the inputs, run the corpus check (which evaluates, and so can hit a host
/// limit), then time each case.
let private measured
    (prepare: unit -> 'I)
    (check: 'I -> unit)
    (cases: (string * Arm * ('I -> unit -> obj)) list)
    (n: int)
    =
    let guarded (f: unit -> 'T) : Result<'T, string> =
        try
            Ok(f ())
        with e when not (e.Message.StartsWith "benchmark corpus:") ->
            Error(notMeasured e)

    let inputs = guarded prepare

    let checkedOk = inputs |> Result.bind (fun i -> guarded (fun () -> check i))

    [ for name, arm, run in cases ->
          match inputs, checkedOk, arm with
          | Error why, _, _ -> name, n, why
          | Ok i, Ok(), _ -> name, n, attempt (fun () -> medianMs runsRef.Value (run i))
          | Ok _, Error why, Evaluator -> name, n, why
          | Ok i, Error _, HandArm ->
              name, n, attempt (fun () -> medianMs runsRef.Value (run i)) + " (unchecked on this host)" ]

let private table (title: string) (lines: (string * int * string) list) =
    printfn ""
    printfn "### %s" title
    printfn ""
    printfn "| Case | Rows | Median (ms) |"
    printfn "|---|---:|---:|"

    for case, n, cell in lines do
        printfn "| %s | %s | %s |" case (rows n) cell

/// The typed family (Phase 280): each verb over the same values carried as a decimal, a float and
/// an integer, at three sizes.
let private typedTable () =
    // The decimal-vector law first, on this host: a decimal figure is only a figure about the right
    // answer once the vector path has answered the text path's bytes here.
    let failures, compared = Fuaran.Compute.Tests.DecimalVectorLaw.checkAll 1 400

    if compared = 0 || not (List.isEmpty failures) then
        failwith (
            "benchmark corpus: the decimal-vector law failed on this host ("
            + string (List.length failures)
            + " of "
            + string compared
            + "): "
            + String.concat " | " (List.truncate 3 failures)
        )

    printfn ""
    printfn "The decimal-vector law: %d pipelines over 400 seeds, the vector path equal to the text path" compared

    table
        "Typed values (Phase 280): decimal beside float and int"
        [ for verb in Corpus.typedVerbs do
              for n in Corpus.typedSizes do
                  for ty in Corpus.typedTypes do
                      yield!
                          measured
                              (fun () -> Corpus.typedTable ty n, Corpus.typedPipeline verb ty n)
                              (fun (input, pipeline) -> Corpus.checkTyped verb ty n input pipeline)
                              [ verb + ", " + Corpus.typedName ty,
                                Evaluator,
                                (fun (input, pipeline) () -> box (Fuaran.Compute.DataFrame.evalPipeline pipeline input)) ]
                              n ]

/// The chain (Phase 342): the prepared-result law on this host first, then the three pipelines in
/// sequence over `orders`, through the `Table` boundary at every hop and kept prepared between hops.
let private chainTable () =
    let failures, compared = Fuaran.Compute.Tests.PreparedResultLaw.check ()

    if compared = 0 || not (List.isEmpty failures) then
        failwith (
            "benchmark corpus: the prepared-result law failed on this host ("
            + string (List.length failures)
            + " of "
            + string compared
            + "): "
            + String.concat " | " (List.truncate 3 failures)
        )

    printfn ""
    printfn "The prepared-result law: %d comparisons over the transform vectors' sample, all equal" compared

    table
        "Chain (Phase 342): three pipelines over orders, Table-chained against prepared-chained"
        [ for n in Corpus.sheetSizes do
              yield!
                  measured
                      (fun () -> Corpus.ordersTable (Corpus.ordersArrays n))
                      Corpus.checkChain
                      [ "chain: Table-chained", Evaluator, (fun o () -> box (Corpus.chainViaTables o))
                        "chain: prepared-chained", Evaluator, (fun o () -> box (Corpus.chainViaPrepared o)) ]
                      n ]

/// The sizes the state's wire form is measured at (Phase 355).
let private stateSizes = [ 1_000; 100_000; 1_000_000 ]

/// The largest size this host is asked for. Under node the million-row case exhausts the default
/// heap while the encoding is assembled, which ends the process rather than raising, so it is
/// reported in its row without being attempted.
let private stateCeiling =
#if FABLE_COMPILER
    100_000
#else
    1_000_000
#endif

/// The best of `runs` single calls, in milliseconds. A single call, not a calibrated batch: at a
/// million rows one call is seconds, and the figure wanted is what one run of a job pays.
let private bestMs (runs: int) (f: unit -> 'T) : float =
    let mutable best = Double.PositiveInfinity

    for _ in 1..runs do
        let start = nowMs ()
        f () |> ignore
        let took = nowMs () - start

        if took < best then
            best <- took

    best

/// The incremental state's wire form (Phase 355): the state-codec laws on this host first, then,
/// per Scaling pipeline and size, the encoded state against the source it was built over and what
/// encoding, decoding and resuming cost beside a full evaluation.
let private stateTable () =
    for seed in [ 1; 7; 99 ] do
        for r in Fuaran.Compute.IncrementalDelta.stateLaws seed 60 do
            if not r.Passed then
                failwith (
                    "benchmark corpus: the state-codec law failed on this host (seed "
                    + string seed
                    + "): "
                    + r.Law
                    + (match r.Counterexample with
                       | Some c -> " | " + c
                       | None -> "")
                )

    printfn ""
    printfn "The state-codec laws: green over 3 seeds of 60 draws on this host"
    printfn ""
    printfn "### The incremental state's wire form (Phase 355)"
    printfn ""

    // Phase 357 adds the per-row reading: what resuming costs a row (the decode, or the decode over
    // a supplied source, plus the diff and refresh), beside what the full evaluation costs one.
    // Resuming beats the full evaluation exactly where the second figure is the larger.
    printfn
        "| Pipeline | Rows | Source (chars) | State (chars) | x source | Detached (chars) | x source | Encode (ms) | Decode (ms) | Decode over source (ms) | Diff + refresh (ms) | Full evaluation (ms) | Resume (us/row) | Resume detached (us/row) | Full (us/row) |"

    printfn "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"

    let idw = Corpus.scalingIdentity
    let runs = runsRef.Value

    for name, pipeline in Corpus.scalingPipelines do
        for n in stateSizes do
            if n > stateCeiling then
                printfn "| %s | %s | not measured: past this host's default heap |" name (rows n)
            else

                try
                    let before = Corpus.scalingTable n
                    let after = Corpus.editOne before

                    let state =
                        Fuaran.Compute.Incremental.primeOn idw pipeline before |> Corpus.orFail "prime"

                    let source = Fuaran.Core.ColumnCodec.encode (Fuaran.Core.Embedded before)

                    let carrying =
                        Fuaran.Compute.IncrementalCodec.encode state |> Corpus.orFail "encode"

                    let detached =
                        Fuaran.Compute.IncrementalCodec.encodeDetached state
                        |> Corpus.orFail "encodeDetached"

                    let decoded =
                        Fuaran.Compute.IncrementalCodec.decode carrying |> Corpus.orFail "decode"

                    // What a second process does: measure the change against the decoded state's own
                    // source, then refresh from the decoded state.
                    let resume (s: Fuaran.Compute.IncrementalEval) =
                        let delta =
                            Fuaran.Compute.Delta.diff idw (Fuaran.Compute.Incremental.source s) after
                            |> Corpus.orFail "diff"

                        Fuaran.Compute.Incremental.refreshOn idw pipeline s delta after
                        |> Corpus.orFail "refresh"

                    let original = resume state
                    let resumed = resume decoded

                    let over =
                        Fuaran.Compute.IncrementalCodec.decodeOver before detached
                        |> Corpus.orFail "decodeOver"
                        |> resume

                    for what, s in [ "decoded", resumed; "decoded over its source", over ] do
                        if
                            Fuaran.Compute.Incremental.result s
                            <> Fuaran.Compute.Incremental.result original
                            || Fuaran.Compute.Incremental.footprint s
                               <> Fuaran.Compute.Incremental.footprint original
                        then
                            failwithf
                                "benchmark corpus: the refresh from the %s state of %s at %d rows disagrees with the original's"
                                what
                                name
                                n

                    let per (chars: int) =
                        sprintf "%.2f" (float chars / float source.Length)

                    let encodeMs = bestMs runs (fun () -> Fuaran.Compute.IncrementalCodec.encode state)

                    let decodeMs =
                        bestMs runs (fun () -> Fuaran.Compute.IncrementalCodec.decode carrying)

                    let overMs =
                        bestMs runs (fun () -> Fuaran.Compute.IncrementalCodec.decodeOver before detached)

                    let resumeMs = bestMs runs (fun () -> resume decoded)

                    let fullMs =
                        bestMs runs (fun () -> Fuaran.Compute.DataFrame.evalPipeline pipeline after)

                    let perRow (ms: float) = ms * 1000.0 / float n

                    printfn
                        "| %s | %s | %s | %s | %s | %s | %s | %.1f | %.1f | %.1f | %.1f | %.1f | %.2f | %.2f | %.2f |"
                        name
                        (rows n)
                        (rows source.Length)
                        (rows carrying.Length)
                        (per carrying.Length)
                        (rows detached.Length)
                        (per detached.Length)
                        encodeMs
                        decodeMs
                        overMs
                        resumeMs
                        fullMs
                        (perRow (decodeMs + resumeMs))
                        (perRow (overMs + resumeMs))
                        (perRow fullMs)
                with e when not (e.Message.StartsWith "benchmark corpus:") ->
                    printfn "| %s | %s | %s |" name (rows n) (notMeasured e)

[<EntryPoint>]
let main argv =
    let runs =
        match argv |> Array.tryHead with
        | Some a -> int a
        | None -> 10

    if runs < 1 then
        failwith "runs must be at least 1"

    runsRef.Value <- runs

    // `typed` as the second argument times the typed family alone (Phase 280).
    let onlyTyped = argv.Length > 1 && argv.[1] = "typed"
    // `state` measures the incremental state's wire form alone (Phase 355).
    let onlyState = argv.Length > 1 && argv.[1] = "state"
    // `chain` runs the prepared-result law and times the chain alone (Phase 342).
    let onlyChain = argv.Length > 1 && argv.[1] = "chain"

    if onlyChain then
        chainTable ()
    elif not onlyTyped && not onlyState then
        table
            "Sheet"
            [ for n in Corpus.sheetSizes do
                  yield!
                      measured
                          (fun () ->
                              let a = Corpus.ordersArrays n
                              a, Corpus.ordersTable a)
                          (fun (a, orders) -> Corpus.checkSheet a orders)
                          [ "lines: hand arm", HandArm, (fun (a, _) () -> box (Corpus.handLines a Corpus.threshold))
                            "lines: evaluator", Evaluator, (fun (_, o) () -> box (Corpus.evalLines o))
                            "byRegion: hand arm", HandArm, (fun (a, _) () -> box (Corpus.handByRegion a))
                            "byRegion: evaluator", Evaluator, (fun (_, o) () -> box (Corpus.evalByRegion o)) ]
                          n ]

        chainTable ()

        table
            "Scaling pipelines"
            [ for name, pipeline in Corpus.scalingPipelines do
                  for n in Corpus.scalingSizes do
                      yield!
                          measured
                              (fun () -> Corpus.refreshInputs pipeline n)
                              (fun inputs ->
                                  let full = Corpus.scalingFull inputs |> Corpus.orFail "full evaluation"
                                  let refreshed = Corpus.scalingRefresh inputs |> Corpus.orFail "refresh"

                                  if Fuaran.Compute.Incremental.result refreshed <> full then
                                      failwithf
                                          "benchmark corpus: the refresh of %s at %d rows disagrees with the full evaluation"
                                          name
                                          n)
                              [ name + ": full", Evaluator, (fun i () -> box (Corpus.scalingFull i))
                                name + ": refresh", Evaluator, (fun i () -> box (Corpus.scalingRefresh i)) ]
                              n ]

        table
            "Shapes"
            [ for name, n, _ in Corpus.shapes do
                  yield!
                      measured
                          (fun () -> Corpus.shape name)
                          (fun (input, pipeline) -> Corpus.checkShape name input pipeline)
                          [ name,
                            Evaluator,
                            (fun (input, pipeline) () -> box (Fuaran.Compute.DataFrame.evalPipeline pipeline input)) ]
                          n ]

    if onlyChain then ()
    elif onlyState then stateTable ()
    else typedTable ()

    0
