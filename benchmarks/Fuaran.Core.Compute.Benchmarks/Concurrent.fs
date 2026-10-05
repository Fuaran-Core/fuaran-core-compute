/// Phase 343: a batch of independent pipelines over one prepared source — N = 1, 4, 16 and 64
/// pipelines over the sheet's `orders` at 1,000, 10,000 and 100,000 rows — run three ways:
///
/// - `sequential`: one after another on the caller's thread through the native member, each
///   pipeline free to run its own morsels and partitions (what a consumer calling
///   `DataFrame.evalToPrepared` per pipeline gets);
/// - `alongside`: on the thread pool, at most one per logical processor at once, each through
///   `Kernels.oneThread` (the shipped bound: nothing nested fans out);
/// - `nested`: on the thread pool likewise, each through the native member, so a pipeline over
///   enough rows fans out its own morsels inside the batch (the bound NOT applied — measured to
///   price the bound, not kept);
/// - `host`: the shipped entry point, `DataFrame.evalManyToPrepared`, which picks `sequential` or
///   `alongside` at `Kernels.PipelineRows` of work.
///
/// The batch cycles four of the sheet's node shapes: `lines` (two derives), `byRegion` (a derive
/// and a grouping), the big lines (a derive and a filter) and the top ten by amount (a derive and
/// a fused sort-and-limit). Every result stays prepared, as the entry point answers it. Every setup
/// holds each arm's answers equal, pipeline for pipeline, to `evalToPrepared`'s before anything is
/// timed. .NET only; results in `benchmarks/results/`.
module Fuaran.Core.Compute.Benchmarks.Concurrent

open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Fuaran.Core
open Fuaran.Compute

let private amountStep = Derive("amount", Binary(Mul, Col "qty", Col "price"))

/// The four node shapes a batch cycles through.
let shapes: Transform list list =
    [ Corpus.linesPipeline
      Corpus.byRegionPipeline
      [ amountStep; Filter(Binary(Ge, Col "amount", Param "threshold")) ]
      [ amountStep
        Transform.sortBy [ "amount", Desc; "id", Asc ]
        Transform.limit 10 0 ] ]

/// `n` pipelines, cycling the shapes.
let batch (n: int) : Transform list list =
    List.init n (fun i -> shapes[i % shapes.Length])

/// The native member running every batch in order on the caller's thread.
let internal sequentialSet: KernelSet =
    { Kernels.native with
        RunPipelines = Kernels.Native.runPipelinesAt System.Int32.MaxValue }

/// The native member running every batch of two or more alongside, whatever its work.
let internal alongsideSet: KernelSet =
    { Kernels.native with
        RunPipelines = Kernels.Native.runPipelinesAt 0 }

/// The bound not applied: the batch on the pool, each pipeline through the native member.
let internal nested (pipelines: Transform list list) (prepared: Prepared) : Result<Prepared, EvalError> list =
    let ps = Array.ofList pipelines
    let results: Result<Prepared, EvalError>[] = Array.zeroCreate ps.Length
    prepared.Frame.Value |> ignore

    let options =
        ParallelOptions(MaxDegreeOfParallelism = System.Environment.ProcessorCount)

    Parallel.For(
        0,
        ps.Length,
        options,
        fun i -> results[i] <- DataFrame.evalToPrepared DataFrame.noResolve Corpus.sheetEnv ps[i] prepared
    )
    |> ignore

    List.ofArray results

let internal run (arm: string) (pipelines: Transform list list) (prepared: Prepared) =
    match arm with
    | "sequential" ->
        DataFrame.evalManyToPreparedWith sequentialSet DataFrame.noResolve Corpus.sheetEnv pipelines prepared
    | "alongside" ->
        DataFrame.evalManyToPreparedWith alongsideSet DataFrame.noResolve Corpus.sheetEnv pipelines prepared
    | "nested" -> nested pipelines prepared
    | "host" -> DataFrame.evalManyToPrepared DataFrame.noResolve Corpus.sheetEnv pipelines prepared
    | other -> failwithf "benchmark corpus: no arm named '%s'" other

/// Each answer as canonical wire, for the setup's agreement check.
let private wires (results: Result<Prepared, EvalError> list) : string list =
    results
    |> List.map (fun r ->
        match r with
        | Ok p -> ColumnCodec.encode (Embedded(DataFrame.toTable p))
        | Error e -> "error " + DataFrame.errorString e)

/// The arm's answers equal `evalToPrepared`'s, pipeline for pipeline, and none refused.
let check (arm: string) (n: int) (rows: int) : unit =
    let prepared = DataFrame.prepare (Corpus.ordersTable (Corpus.ordersArrays rows))
    let pipelines = batch n

    let reference =
        pipelines
        |> List.map (fun p -> DataFrame.evalToPrepared DataFrame.noResolve Corpus.sheetEnv p prepared)

    if reference |> List.exists Result.isError then
        failwith "benchmark corpus: a batch pipeline refused"

    if wires (run arm pipelines prepared) <> wires reference then
        failwithf "benchmark corpus: the %s arm disagrees with evalToPrepared at %d pipelines, %d rows" arm n rows

[<MemoryDiagnoser>]
type Batch() =
    let mutable prepared = Unchecked.defaultof<Prepared>
    let mutable pipelines: Transform list list = []

    static member Arms = [ "sequential"; "alongside"; "nested"; "host" ]
    static member Counts = [ 1; 4; 16; 64 ]
    static member Sizes = Corpus.sheetSizes

    [<ParamsSource("Arms")>]
    member val Arm = "" with get, set

    [<ParamsSource("Counts")>]
    member val Pipelines = 0 with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        check this.Arm this.Pipelines this.Rows
        prepared <- DataFrame.prepare (Corpus.ordersTable (Corpus.ordersArrays this.Rows))
        pipelines <- batch this.Pipelines

    [<Benchmark>]
    member this.Evaluate() = run this.Arm pipelines prepared

/// Where running alongside stops paying: two and four pipelines over small sources, the two arms
/// the threshold chooses between. Sets `Kernels.PipelineRows`.
[<MemoryDiagnoser>]
type Floor() =
    let mutable prepared = Unchecked.defaultof<Prepared>
    let mutable pipelines: Transform list list = []

    static member Arms = [ "sequential"; "alongside" ]
    static member Counts = [ 2; 4 ]
    static member Sizes = [ 50; 100; 250; 500; 1_000 ]

    [<ParamsSource("Arms")>]
    member val Arm = "" with get, set

    [<ParamsSource("Counts")>]
    member val Pipelines = 0 with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        check this.Arm this.Pipelines this.Rows
        prepared <- DataFrame.prepare (Corpus.ordersTable (Corpus.ordersArrays this.Rows))
        pipelines <- batch this.Pipelines

    [<Benchmark>]
    member this.Evaluate() = run this.Arm pipelines prepared
