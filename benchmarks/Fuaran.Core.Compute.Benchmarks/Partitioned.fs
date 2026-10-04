/// Phase 344: the gathering verbs through both members of the kernel pair — the sort's parallel
/// merge sort, the top-n a range of rows at a time, and the window's partitions scanned across
/// threads — and the grouping BY KEY across threads that was built and not kept, against the
/// shipped sequential grouping. At 10,000, 100,000 and 1,000,000 rows, at ten keys and at one key
/// per ten rows (`benchmarks/results/2026-10-04-i7-9700-phase-344.md`).
///
/// .NET only. Each verb runs one step through `DataFrame.evalStepWith` over a frame prepared once,
/// so the figure is the step's; the top-n runs `Sort` then `Limit 10` through the planned driver,
/// which fuses the pair. Every setup holds the member's answer equal to the portable member's, and
/// the step's output size to what the corpus says it must be, before anything is timed. The join
/// probe over ranges of left rows was measured from the evaluator and removed; its figures are in
/// the results file and `DECISIONS.md` (D8).
module Fuaran.Core.Compute.Benchmarks.Partitioned

open BenchmarkDotNet.Attributes
open Fuaran.Core
open Fuaran.Compute

let sizes = [ 10_000; 100_000; 1_000_000 ]

/// Ten keys, and one key per ten rows — the two cardinalities Phase 270 and Phase 341 measured.
let cardinalities = [ "ten"; "tenth" ]

let cardinality (name: string) (n: int) : int =
    match name with
    | "ten" -> 10
    | "tenth" -> n / 10
    | _ -> failwithf "benchmark corpus: no cardinality named '%s'" name

let private col (name: string) (ty: ColumnType) (cells: Cell list) : Column = Column.create name ty cells

/// The left table: `k`, an int key over `card` values visited in a scrambled order; `s`, an int
/// that is a permutation of `0 .. n - 1` (distinct, unordered); `v`, a float in whole quarters.
let table (n: int) (card: int) : Table =
    { Schema = [ "k", IntType; "s", IntType; "v", FloatType ]
      Columns =
        [ col "k" IntType [ for i in 0 .. n - 1 -> Int(((i % card) * 7919) % card) ]
          col "s" IntType [ for i in 0 .. n - 1 -> Int(int ((int64 i * 104729L) % int64 n)) ]
          col "v" FloatType [ for i in 0 .. n - 1 -> Float(float ((i * 13) % 397) * 0.25) ] ] }

/// The step a verb runs. The cardinality is the table's; no step reads it.
let step (verb: string) (_card: int) : Transform list =
    match verb with
    | "group-by" ->
        [ GroupBy([ "k" ], [ { Name = "total"; Fn = Sum; Of = "v" }; { Name = "n"; Fn = Count; Of = "v" } ]) ]
    | "sort" -> [ Transform.sortBy [ "k", Asc; "s", Asc ] ]
    | "top-n" -> [ Transform.sortBy [ "k", Asc; "s", Asc ]; Transform.limit 10 0 ]
    | "window" ->
        [ Window
              { PartitionBy = [ "k" ]
                OrderBy = [ "s", Asc ]
                Fn = CumulSum
                Of = "v"
                As = "cs" } ]
    | other -> failwithf "benchmark corpus: no verb named '%s'" other

/// The rows the step answers.
let expectedRows (verb: string) (n: int) (card: int) : int =
    match verb with
    | "group-by" -> card
    | "top-n" -> 10
    | _ -> n

let internal kernelSet (name: string) : KernelSet =
    match name with
    | "portable" -> Kernels.portable
    | "native" -> Kernels.native
    | _ -> failwithf "benchmark corpus: no kernel member named '%s'" name

/// One verb through one member: the step's frame (the top-n's through the planned driver).
let internal run (k: KernelSet) (verb: string) (pipeline: Transform list) (prepared: Prepared) : int =
    match verb, pipeline with
    | "top-n", _ ->
        DataFrame.evalPreparedCountedWith k DataFrame.noResolve Map.empty pipeline prepared
        |> Corpus.orFail verb
        |> fst
        |> Table.rowCount
    | _, [ t ] ->
        (DataFrame.evalStepWith k DataFrame.noResolve Map.empty prepared.Frame.Value t
         |> Corpus.orFail verb)
            .Count
    | _ -> failwith "benchmark corpus: a step case is one step"

/// The member's answer, as a table, for the setup's agreement check.
let internal answer (k: KernelSet) (pipeline: Transform list) (prepared: Prepared) : Table =
    DataFrame.evalPreparedCountedWith k DataFrame.noResolve Map.empty pipeline prepared
    |> Corpus.orFail "answer"
    |> fst

/// One verb through both members, at every size and cardinality — a class per verb, so a run can
/// select one (`--filter '*Partitioned.GroupBy*'`).
[<AbstractClass>]
[<MemoryDiagnoser>]
type Verb(verb: string) =
    let mutable prepared = Unchecked.defaultof<Prepared>
    let mutable pipeline: Transform list = []
    let mutable set = Kernels.portable

    static member Members = [ "portable"; "native" ]
    static member Sizes = sizes
    static member Cardinalities = cardinalities

    [<ParamsSource("Cardinalities")>]
    member val Keys = "" with get, set

    [<ParamsSource("Members")>]
    member val Member = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let card = cardinality this.Keys this.Rows
        prepared <- DataFrame.prepare (table this.Rows card)
        pipeline <- step verb card
        set <- kernelSet this.Member

        if answer set pipeline prepared <> answer Kernels.portable pipeline prepared then
            failwithf "benchmark corpus: %s at %d rows disagrees with the portable member" verb this.Rows

        let rows = run set verb pipeline prepared

        if rows <> expectedRows verb this.Rows card then
            failwithf "benchmark corpus: %s at %d rows answered %d rows" verb this.Rows rows

    [<Benchmark>]
    member _.Evaluate() = run set verb pipeline prepared

type Sort() =
    inherit Verb("sort")

type TopN() =
    inherit Verb("top-n")

type Window() =
    inherit Verb("window")

// ---- the grouping BY KEY, measured and not kept ---------------------------------------------

/// The grouping partitioned by key across threads, as Phase 344 built it in the evaluator and then
/// removed (`DECISIONS.md`, D8): the logical rows hashed into one partition a logical processor
/// (every row of one key in one partition), each partition's rows grouped by the shipped row
/// hasher and folded by the shipped streamed aggregates in logical order on its own thread, and
/// the partitions' groups merged on their first logical rows. No aggregate is combined from two
/// partitions, so every answer, a float `Sum` included, is the sequential grouping's bit for bit;
/// the setup holds it so. Specialised to this corpus' step: an int key `k`, `Sum` and `Count` of
/// the float `v`.
let internal byKeyGroupBy (f: Frame) : Frame =
    let phys = Frame.physical f
    let n = phys.Length
    let parts = max 2 System.Environment.ProcessorCount
    let keyVec = f.Vecs[0]
    let valVec = f.Vecs[2]

    let keyOf =
        match keyVec with
        | Ints(a, m) -> fun p -> if m[p] then a[p] else 0x2f6b9d43
        | _ -> failwith "benchmark corpus: the key is an int vector"

    // Hash and count a morsel at a time, then place each row in its morsel's reserved range of its
    // partition, so a partition's rows stay in logical order.
    let m = Kernels.morselCount n
    let part: byte[] = Array.zeroCreate n
    let counts = Array.init m (fun _ -> Array.zeroCreate<int> parts)

    Kernels.native.RunMorsels m (fun j ->
        for i in Kernels.morselStart j .. Kernels.morselEnd n j - 1 do
            let mutable h = keyOf phys[i]
            h <- h ^^^ (h >>> 15)
            h <- h * 0x2c1b3c6d
            h <- h ^^^ (h >>> 12)
            let t = int (uint32 h % uint32 parts)
            part[i] <- byte t
            counts[j][t] <- counts[j][t] + 1

        true)

    let bounds: int[] = Array.zeroCreate (parts + 1)
    let mutable at = 0

    for t in 0 .. parts - 1 do
        bounds[t] <- at

        for j in 0 .. m - 1 do
            let c = counts[j][t]
            counts[j][t] <- at
            at <- at + c

    bounds[parts] <- at
    let rows: int[] = Array.zeroCreate n
    let rowsPhys: int[] = Array.zeroCreate n

    Kernels.native.RunMorsels m (fun j ->
        for i in Kernels.morselStart j .. Kernels.morselEnd n j - 1 do
            let d = counts[j][int part[i]]
            rows[d] <- i
            rowsPhys[d] <- phys[i]
            counts[j][int part[i]] <- d + 1

        true)

    // Each partition grouped and folded on its own thread.
    let firsts: int[][] = Array.zeroCreate parts
    let totals: Vec[] = Array.zeroCreate parts
    let ns: Vec[] = Array.zeroCreate parts
    let opener: byte[] = Array.zeroCreate n

    Kernels.native.RunMorsels parts (fun t ->
        let lo = bounds[t]
        let physT = Array.sub rowsPhys lo (bounds[t + 1] - lo)
        let slotT, firstT = DataFrame.RowHash.slots [| keyVec |] physT
        let groups = firstT.Count

        firsts[t] <-
            Array.init groups (fun s ->
                let i = rows[lo + firstT[s]]
                opener[i] <- byte (t + 1)
                i)

        let fold (fn: AggFn) (ty: ColumnType) =
            let s =
                DataFrame.GroupAgg.Stream(fn, FloatType, valVec, groups, DataFrame.GroupAgg.Exact)

            s.FeedAll(slotT, physT)
            let out = DataFrame.GroupAgg.Output(ty, groups)

            for g in 0 .. groups - 1 do
                if not (s.Emit(g, out)) then
                    failwith "benchmark corpus: an aggregate deferred"

            out.ToVec()

        totals[t] <- fold Sum FloatType
        ns[t] <- fold Count IntType
        true)

    // The groups in first-appearance order: the logical rows that open one, in order.
    let groups = firsts |> Array.sumBy (fun a -> a.Length)
    let keys: int[] = Array.zeroCreate groups
    let sums: float[] = Array.zeroCreate groups
    let counted: int[] = Array.zeroCreate groups
    let heads: int[] = Array.zeroCreate parts
    let mutable g = 0
    let mutable i = 0

    while g < groups do
        let o = int opener[i]

        if o > 0 then
            let t = o - 1
            let l = heads[t]

            match keyVec, totals[t], ns[t] with
            | Ints(ka, _), Floats(ta, _), Ints(ca, _) ->
                keys[g] <- ka[phys[firsts[t][l]]]
                sums[g] <- ta[l]
                counted[g] <- ca[l]
            | _ -> failwith "benchmark corpus: a partition's outputs are not int, float, int vectors"

            heads[t] <- l + 1
            g <- g + 1

        i <- i + 1

    let present = Array.create groups true

    { Cols = [ "k", IntType; "total", FloatType; "n", IntType ]
      Vecs = [| Ints(keys, present); Floats(sums, present); Ints(counted, present) |]
      Origins = Array.create 3 None
      Sel = None
      Count = groups }

/// The shipped, sequential grouping of the corpus' step.
let internal sequentialGroupBy (f: Frame) : Frame =
    DataFrame.evalStepWith Kernels.native DataFrame.noResolve Map.empty f (List.head (step "group-by" 10))
    |> Corpus.orFail "group-by"

/// The shipped grouping against the grouping by key, at both cardinalities.
[<MemoryDiagnoser>]
type GroupByKey() =
    let mutable frame = Unchecked.defaultof<Frame>
    let mutable arm: Frame -> Frame = sequentialGroupBy

    static member Arms = [ "sequential"; "by-key" ]
    static member Sizes = sizes
    static member Cardinalities = cardinalities

    [<ParamsSource("Arms")>]
    member val Arm = "" with get, set

    [<ParamsSource("Cardinalities")>]
    member val Keys = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        frame <- Frame.ofTable (table this.Rows (cardinality this.Keys this.Rows))

        arm <-
            if this.Arm = "by-key" then
                byKeyGroupBy
            else
                sequentialGroupBy

        if Frame.toTable (arm frame) <> Frame.toTable (sequentialGroupBy frame) then
            failwithf "benchmark corpus: the %s grouping disagrees with the sequential step" this.Arm

    [<Benchmark>]
    member _.Evaluate() = (arm frame).Count
