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
/// incremental state's wire form (`node Program.js 3 state`), with an optional third argument, the
/// largest size it measures on this host (`node Program.js 3 state 1000000`).
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

/// The sizes the state's wire form is measured at (Phase 355).
let private stateSizes = [ 1_000; 100_000; 1_000_000 ]

/// The largest size this host is asked for, unless the third argument names another
/// (`node Program.js 3 state 1000000`). Under node the million-row case can exhaust the default heap,
/// which ends the process rather than raising, so by default it is reported in its row without being
/// attempted. Phase 358 measured what it needs: one encode at 1,000,000 rows completes in a 1,280 MB
/// heap beside the 833 MB its table and state hold, one decode in 2,048 MB (its result is 817 MB of
/// that), and this harness, which holds a row's states, encodings and refreshes at once, in
/// 3,072 MB and not in 2,048. A host whose default heap is smaller than that keeps the ceiling; one
/// with more (node's default on a machine with enough memory is about 4 GB) can pass `1000000`.
let private stateCeiling =
    ref (
#if FABLE_COMPILER
        100_000
#else
        1_000_000
#endif
    )

/// The frame `IncrementalCodec` writes around a body: the body's canonical text, then the 64 hex
/// digits of its SHA-256, then the close.
let private framePrefix = "{\"$type\":\"incrementalState\",\"body\":"

let private frameDigest = ",\"digest\":\""

let private frameSuffix = "\"}"

/// The body of an encoding, and the digest it carries.
let private bodyAndDigest (text: string) : string * string =
    let tail = frameDigest.Length + 64 + frameSuffix.Length

    text.Substring(framePrefix.Length, text.Length - framePrefix.Length - tail),
    text.Substring(text.Length - frameSuffix.Length - 64, 64)

/// The digest under this host, held to the substrate's `Hash.sha256Hex` before anything is timed
/// (Phase 358): the suite's corpus, which crosses the block boundary and holds every UTF-8 width and
/// both lone surrogates, plus strings that cross the managed copy's 16,384-byte chunk. The package's
/// digest is internal, so it is reached through `decode`, which checks it before it parses: a frame
/// around `s` carrying the substrate's digest of `s` must pass that check, and one carrying the
/// digest of another string must fail it, so the probe is seen to be able to fail.
let private digestCorpus () =
    let lone = string (char 0xD800)
    let low = string (char 0xDC00)

    let corpus =
        [ ""
          "abc"
          "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"
          String.replicate 55 "a"
          String.replicate 56 "a"
          String.replicate 63 "a"
          String.replicate 64 "a"
          String.replicate 65 "a"
          String.replicate 1000 "a"
          String.replicate 100000 "ab"
          "é — ✓ 😀 \u0001\u0010"
          String.replicate 21 "😀é"
          lone
          low + lone
          "x" + lone + "y" + low
          String.replicate 16_383 "a" + "😀"
          String.replicate 16_382 "a" + lone + "b"
          String.replicate 16_383 "a" + "é" + "b"
          String.replicate 10_000 "😀é✓"
          String.replicate 65_535 "a" + "😀" + "b"
          String.replicate (3 * 65_536 + 7) "é" ]

    let damaged (body: string) (digest: string) =
        match Fuaran.Compute.IncrementalCodec.decode (framePrefix + body + frameDigest + digest + frameSuffix) with
        | Error(Fuaran.Core.ColumnError.Malformed m) -> m.Contains "the digest is not the digest of the body"
        | _ -> false

    for s in corpus do
        if damaged s (Fuaran.Core.Hash.sha256Hex s) then
            failwithf
                "benchmark corpus: the package's digest of a %d-unit string is not the substrate's on this host"
                s.Length

        if not (damaged s (Fuaran.Core.Hash.sha256Hex (s + "x"))) then
            failwithf "benchmark corpus: a wrong digest over a %d-unit string was not refused" s.Length

    List.length corpus

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

    let digests = digestCorpus ()

    printfn
        "The digest: the substrate's over the %d strings of the suite's corpus on this host, and over every encoding below"
        digests

    printfn ""
    printfn "### The incremental state's wire form (Phase 355)"
    printfn ""

    // Phase 357 adds the per-row reading: what resuming costs a row (the decode, or the decode over
    // a supplied source, plus the diff and refresh), beside what the full evaluation costs one.
    // Resuming beats the full evaluation exactly where the second figure is the larger.
    printfn
        "| Pipeline | Rows | Source (chars) | State (chars) | x source | Detached (chars) | x source | Encode (ms) | Decode (ms) | Decode over source (ms) | Diff + refresh (ms) | Full evaluation (ms) | Resume (us/row) | Resume detached (us/row) | Full (us/row) | Digest (carrying / detached) |"

    printfn "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"

    let idw = Corpus.scalingIdentity
    let runs = runsRef.Value

    for name, pipeline in Corpus.scalingPipelines do
        for n in stateSizes do
            if n > stateCeiling.Value then
                printfn
                    "| %s | %s | not measured: past this harness's ceiling on this host (Phase 358: it needs a 3 GB heap) |"
                    name
                    (rows n)
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

                    // Phase 358: each encoding's digest is the substrate's digest of its body, and the
                    // table prints the digests' first twelve digits, so the .NET and node runs of this
                    // program show the encodings are the same bytes on both hosts.
                    let digestOf (what: string) (text: string) =
                        let body, digest = bodyAndDigest text

                        if Fuaran.Core.Hash.sha256Hex body <> digest then
                            failwithf
                                "benchmark corpus: the %s encoding of %s at %d rows carries a digest that is not the substrate's"
                                what
                                name
                                n

                        digest.Substring(0, 12)

                    let digests = digestOf "carrying" carrying + " / " + digestOf "detached" detached

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
                        "| %s | %s | %s | %s | %s | %s | %s | %.1f | %.1f | %.1f | %.1f | %.1f | %.2f | %.2f | %.2f | %s |"
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
                        digests
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

    // A third argument after `state` is the largest size to measure on this host (Phase 358).
    if onlyState && argv.Length > 2 then
        stateCeiling.Value <- int argv.[2]

    if not onlyTyped && not onlyState then
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

    if onlyState then stateTable () else typedTable ()

    0
