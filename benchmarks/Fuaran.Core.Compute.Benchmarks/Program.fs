/// Entry point. `--check` builds every corpus input and asserts every agreement (the hand arm
/// against the evaluator, each refresh against its full evaluation, each shape's output size)
/// without timing anything; any other arguments go to BenchmarkDotNet's switcher, for example
/// `--filter '*Sheet*'`.
module Fuaran.Core.Compute.Benchmarks.Program

open System
open System.IO
open System.Runtime.InteropServices
open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Reports
open BenchmarkDotNet.Running

let private checkAll () =
    for n in Corpus.sheetSizes do
        let a = Corpus.ordersArrays n
        Corpus.checkSheet a (Corpus.ordersTable a)
        printfn "sheet %d rows: the hand arm agrees with the evaluator" n

    for name, pipeline in Corpus.scalingPipelines do
        for n in Corpus.scalingSizes do
            let inputs = Corpus.refreshInputs pipeline n
            let full = Corpus.scalingFull inputs |> Corpus.orFail "full evaluation"
            let refreshed = Corpus.scalingRefresh inputs |> Corpus.orFail "refresh"

            if Fuaran.Compute.Incremental.result refreshed <> full then
                failwithf "the refresh of %s at %d rows disagrees with the full evaluation" name n

            printfn "%s %d rows: the refresh agrees with the full evaluation" name n

    for name, _, _ in Corpus.shapes do
        let t, p = Corpus.shape name
        Corpus.checkShape name t p
        printfn "%s: %d output rows, as expected" name (Corpus.shapeOutputRows name)

    for verb in Corpus.typedVerbs do
        for n in Corpus.typedSizes do
            for ty in Corpus.typedTypes do
                Corpus.checkTyped verb ty n (Corpus.typedTable ty n) (Corpus.typedPipeline verb ty n)
                printfn "%s over %s, %d rows: as expected" verb (Corpus.typedName ty) n

/// Phase 341 — the instrument check. Phase 270's verdicts were taken with the x64 build under
/// emulation on an Arm64 machine, which penalises exactly the intrinsics and the thread scheduling
/// being judged. A process whose architecture is not the machine's is refused, so every figure this
/// harness produces comes from the native JIT.
let private nativeOrRefuse () : bool =
    let proc = RuntimeInformation.ProcessArchitecture
    let os = RuntimeInformation.OSArchitecture

    if proc <> os then
        eprintfn
            "refused: this process is %O on a %O machine (an emulated runtime); run the %O dotnet so the figures are native"
            proc
            os
            os

        false
    else
        printfn "instrument: %O process on a %O machine, %s" proc os RuntimeInformation.FrameworkDescription
        true

[<EntryPoint>]
let main argv =
    if not (nativeOrRefuse ()) then
        2
    elif argv = [| "--check" |] then
        checkAll ()
        0
    else
        // One launch, three warm-ups, ten measured iterations: a fixed, bounded run per benchmark
        // whose median is the figure the results files record. The artifacts land under the build
        // output, never in the source tree.
        let config =
            DefaultConfig.Instance
                .AddJob(Job.Default.WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(10))
                .AddColumn(StatisticColumn.Median)
                .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40))
                .WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"))

        BenchmarkSwitcher.FromAssembly(typeof<Suites.Sheet>.Assembly).Run(argv, config)
        |> ignore

        0
