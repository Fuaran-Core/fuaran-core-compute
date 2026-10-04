/// The BenchmarkDotNet suites over the corpus (Phase 262). Every class carries `MemoryDiagnoser`,
/// so allocations are a tracked number beside every time. Inputs are built and the hand arm's
/// agreement with the evaluator is asserted in `GlobalSetup`, outside every timed region.
module Fuaran.Core.Compute.Benchmarks.Suites

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open Fuaran.Core
open Fuaran.Compute

/// The sheet: each node's evaluator arm beside the hand arm, which is the category's baseline, so
/// the `Ratio` column reads "times slower than the hand arm".
[<MemoryDiagnoser>]
[<GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)>]
[<CategoriesColumn>]
type Sheet() =
    let mutable arrays = Unchecked.defaultof<Corpus.OrdersArrays>
    let mutable orders = Unchecked.defaultof<Table>

    static member Sizes = Corpus.sheetSizes

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        arrays <- Corpus.ordersArrays this.Rows
        orders <- Corpus.ordersTable arrays
        Corpus.checkSheet arrays orders

    [<Benchmark(Baseline = true); BenchmarkCategory("lines")>]
    member _.LinesHandArm() =
        Corpus.handLines arrays Corpus.threshold

    [<Benchmark; BenchmarkCategory("lines")>]
    member _.LinesEvaluator() = Corpus.evalLines orders

    [<Benchmark(Baseline = true); BenchmarkCategory("byRegion")>]
    member _.ByRegionHandArm() = Corpus.handByRegion arrays

    [<Benchmark; BenchmarkCategory("byRegion")>]
    member _.ByRegionEvaluator() = Corpus.evalByRegion orders

/// The chain (Phase 342): three pipelines in sequence over `orders`, through the `Table` boundary at
/// every hop (the category's baseline) and kept prepared between hops, so the `Ratio` column reads
/// "the prepared chain's share of the Table chain's time".
[<MemoryDiagnoser>]
type Chain() =
    let mutable orders = Unchecked.defaultof<Table>

    static member Sizes = Corpus.sheetSizes

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        orders <- Corpus.ordersTable (Corpus.ordersArrays this.Rows)
        Corpus.checkChain orders

    [<Benchmark(Baseline = true)>]
    member _.TableChained() = Corpus.chainViaTables orders

    [<Benchmark>]
    member _.PreparedChained() = Corpus.chainViaPrepared orders

/// The three Scaling pipelines: the full evaluation of the edited table and the restricted refresh
/// over its one-row delta. The priming and the diff are setup, as they are in the suite's own
/// refresh-versus-full cases.
[<MemoryDiagnoser>]
type Scaling() =
    let mutable inputs = Unchecked.defaultof<Corpus.RefreshInputs>

    static member Sizes = Corpus.scalingSizes
    static member Names = Corpus.scalingPipelines |> List.map fst

    [<ParamsSource("Names")>]
    member val Pipeline = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        inputs <- Corpus.refreshInputs (Corpus.scalingPipeline this.Pipeline) this.Rows
        let full = Corpus.scalingFull inputs |> Corpus.orFail "full evaluation"
        let refreshed = Corpus.scalingRefresh inputs |> Corpus.orFail "refresh"

        if Incremental.result refreshed <> full then
            failwithf "benchmark corpus: the refresh of %s disagrees with the full evaluation" this.Pipeline

    [<Benchmark>]
    member _.Full() = Corpus.scalingFull inputs

    [<Benchmark>]
    member _.Refresh() = Corpus.scalingRefresh inputs

/// The shapes the suite never times, one benchmark per shape.
[<MemoryDiagnoser>]
type Shapes() =
    let mutable input = Unchecked.defaultof<Table>
    let mutable pipeline: Transform list = []

    static member Names = Corpus.shapes |> List.map (fun (n, _, _) -> n)

    [<ParamsSource("Names")>]
    member val Shape = "" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let t, p = Corpus.shape this.Shape
        input <- t
        pipeline <- p
        Corpus.checkShape this.Shape input pipeline

    [<Benchmark>]
    member _.Evaluate() = DataFrame.evalPipeline pipeline input

/// The typed family (Phase 280): each verb over the same values carried as a decimal, a float and
/// an integer, at three sizes.
[<MemoryDiagnoser>]
type Typed() =
    let mutable input = Unchecked.defaultof<Table>
    let mutable pipeline: Transform list = []

    static member Verbs = Corpus.typedVerbs
    static member Types = Corpus.typedTypes |> List.map Corpus.typedName
    static member Sizes = Corpus.typedSizes

    [<ParamsSource("Verbs")>]
    member val Verb = "" with get, set

    [<ParamsSource("Types")>]
    member val Type = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let ty = Corpus.typedTypes |> List.find (fun t -> Corpus.typedName t = this.Type)
        input <- Corpus.typedTable ty this.Rows
        pipeline <- Corpus.typedPipeline this.Verb ty this.Rows
        Corpus.checkTyped this.Verb ty this.Rows input pipeline

    [<Benchmark>]
    member _.Evaluate() = DataFrame.evalPipeline pipeline input
