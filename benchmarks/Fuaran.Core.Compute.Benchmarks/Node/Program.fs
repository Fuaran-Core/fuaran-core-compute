/// The node leg's timing harness (Phase 262). The same corpus as the BenchmarkDotNet suites
/// (../Corpus.fs), timed with nothing but the wall clock, so the program means the same thing
/// compiled by Fable and run under node as it does on .NET. Every agreement the .NET suites assert
/// is asserted here before a case is timed, and it prints one median table per shape in the shape
/// the results files use. A case the host cannot run is reported in its row, never skipped.
///
/// Usage: `node Program.js [runs]` once compiled (see run-node.ps1), or
/// `dotnet run -c Release --project benchmarks/Fuaran.Core.Compute.Benchmarks/Node -- [runs]`.
/// `runs` is the number of measured samples per case (default 10), after two warm-up calls; a second
/// argument `typed` times only the typed family (`node Program.js 10 typed`).
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

    if not onlyTyped then
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

    typedTable ()
    0
