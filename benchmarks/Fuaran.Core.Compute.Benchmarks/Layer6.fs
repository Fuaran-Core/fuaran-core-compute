/// Layer 6 re-measured (Phase 341): the host kernels against their portable members, the morsel
/// runner behind `Filter` and `Derive`, and Phase 270's order-preserving parallel `GroupBy` rebuilt
/// from its recorded description over the post-323 streamed aggregates.
///
/// Phase 270 took every one of these verdicts with the x64 build running under emulation on an Arm64
/// machine. `Program.main` now refuses a run whose process architecture is not the machine's, so a
/// figure from this file is a native figure.
///
/// .NET only. The kernel pair is selected per benchmark (`Member`), through the evaluator's internal
/// `evalPreparedCountedWith` / `evalStepWith`, so both members run on one host over one prepared
/// source. The morsel size is the compiled `Kernels.MorselRows`; the sweep rebuilds the harness at
/// each size (the README's "Benchmarks" section).
module Fuaran.Core.Compute.Benchmarks.Layer6

open System.Collections.Generic
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Fuaran.Core
open Fuaran.Compute

/// The two members of the kernel pair, by the name each result row carries.
let members = [ "portable"; "native" ]

let internal kernelSet (name: string) : KernelSet =
    match name with
    | "portable" -> Kernels.portable
    | "native" -> Kernels.native
    | _ -> failwithf "benchmark corpus: no kernel member named '%s'" name

/// The sizes 270 measured, and 10,000 rows, the size of a typical spreadsheet-shaped input.
let sizes = [ 10_000; 100_000; 1_000_000 ]

let private col (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failwithf "%s: %A" name e

// ---- the kernels alone ---------------------------------------------------------------------

/// `n` ints spread over 0 .. 999, one in 97 absent.
let kernelInts (n: int) : int[] * bool[] =
    Array.init n (fun i -> (i * 7919) % 1000), Array.init n (fun i -> i % 97 <> 0)

/// `n` floats in whole quarters over 0 .. 249.75, one in 97 absent.
let kernelFloats (n: int) : float[] * bool[] =
    Array.init n (fun i -> float ((i * 104729) % 1000) * 0.25), Array.init n (fun i -> i % 97 <> 0)

/// Each kernel of the pair over `Rows` rows: two comparisons into bitmaps (about half the rows
/// set), their conjunction and disjunction, and the conjunction read back as a selection.
[<MemoryDiagnoser>]
type KernelPair() =
    let mutable set = Kernels.portable
    let mutable ints: int[] = [||]
    let mutable intMask: bool[] = [||]
    let mutable floats: float[] = [||]
    let mutable floatMask: bool[] = [||]
    let mutable a: uint32[] = [||]
    let mutable b: uint32[] = [||]
    let mutable both: uint32[] = [||]

    static member Members = members
    static member Sizes = sizes

    [<ParamsSource("Members")>]
    member val Member = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        set <- kernelSet this.Member
        let iv, im = kernelInts this.Rows
        let fv, fm = kernelFloats this.Rows
        ints <- iv
        intMask <- im
        floats <- fv
        floatMask <- fm
        a <- set.CmpInts CGe ints intMask 500 this.Rows
        b <- set.CmpFloats CLt floats floatMask 125.0 this.Rows
        both <- set.And a b

        // The member under test answers what the portable member answers, before anything is timed.
        let p = Kernels.portable

        if
            a <> p.CmpInts CGe ints intMask 500 this.Rows
            || b <> p.CmpFloats CLt floats floatMask 125.0 this.Rows
            || both <> p.And a b
            || set.Or a b <> p.Or a b
            || set.Selection both <> p.Selection both
        then
            failwithf "benchmark corpus: the %s kernels disagree with the portable member" this.Member

    [<Benchmark>]
    member this.CmpInts() =
        set.CmpInts CGe ints intMask 500 this.Rows

    [<Benchmark>]
    member this.CmpFloats() =
        set.CmpFloats CLt floats floatMask 125.0 this.Rows

    [<Benchmark>]
    member _.And() = set.And a b

    [<Benchmark>]
    member _.Or() = set.Or a b

    [<Benchmark>]
    member _.Selection() = set.Selection both

// ---- the row-local verbs through the evaluator ---------------------------------------------

/// The table the row-local cases read: `a` and `b` ints over 0 .. 999 and `x` a float in quarters.
let rowTable (n: int) : Table =
    { Schema =
        [ Field.create "a" IntType
          Field.create "b" IntType
          Field.create "x" FloatType ]
      Columns =
        [ col "a" IntType [ for i in 0 .. n - 1 -> Int((i * 7919) % 1000) ]
          col "b" IntType [ for i in 0 .. n - 1 -> Int((i * 104729) % 1000) ]
          col "x" FloatType [ for i in 0 .. n - 1 -> Float(float ((i * 31) % 1000) * 0.25) ] ] }

/// Every case ends in `Limit 10`, so the `Table` boundary on the way out costs the same at every
/// size and the figure is the step's. The setup asserts the step still ran over every row.
let private limit10 = Transform.limit 10 0

/// The cases Phase 270 timed, by the name each result row carries:
///
///   * `cmp` — `a >= -10` (every row kept), answered by the bitmap kernels;
///   * `cmp-and` — `a >= 100 and x < 200.0`, two bitmaps and their conjunction;
///   * `compiled` — `a + b > 500`, no kernel predicate, so the compiled tree runs per morsel;
///   * `derive` — the sheet's `lines` node (two `Derive`s), per morsel.
let rowCases: (string * Transform list) list =
    [ "cmp", [ Filter(Binary(Ge, Col "a", Lit(Int -10))); limit10 ]
      "cmp-and",
      [ Filter(Binary(And, Binary(Ge, Col "a", Lit(Int 100)), Binary(Lt, Col "x", Lit(Float 200.0))))
        limit10 ]
      "compiled", [ Filter(Binary(Gt, Binary(Add, Col "a", Col "b"), Lit(Int 500))); limit10 ]
      "derive", Corpus.linesPipeline @ [ limit10 ] ]

let rowCase (name: string) : Transform list =
    match rowCases |> List.tryFind (fun (n, _) -> n = name) with
    | Some(_, p) -> p
    | None -> failwithf "benchmark corpus: no row case named '%s'" name

/// The source a row case reads, prepared: `derive` reads the sheet's `orders`, the rest `rowTable`.
let rowSource (name: string) (n: int) : Prepared =
    if name = "derive" then
        DataFrame.prepare (Corpus.ordersTable (Corpus.ordersArrays n))
    else
        DataFrame.prepare (rowTable n)

/// One row case through one member of the pair.
let internal evalRowCase (k: KernelSet) (pipeline: Transform list) (source: Prepared) : Result<Table * int, EvalError> =
    DataFrame.evalPreparedCountedWith k DataFrame.noResolve Corpus.sheetEnv pipeline source

/// The members agree on a row case, and the step really ran over every row: the evaluation count
/// is at least the row count, so no planner rewrite moved the `Limit` in front of the work.
let internal checkRowCase (name: string) (n: int) (k: KernelSet) (pipeline: Transform list) (source: Prepared) : unit =
    let mine = evalRowCase k pipeline source |> Corpus.orFail name
    let portable = evalRowCase Kernels.portable pipeline source |> Corpus.orFail name

    if fst mine <> fst portable then
        failwithf "benchmark corpus: %s at %d rows disagrees with the portable member" name n

    if snd mine < n then
        failwithf "benchmark corpus: %s at %d rows evaluated %d rows; the step did not run" name n (snd mine)

/// The comparison and bitmap path: `Filter`s whose predicate the kernels answer.
[<MemoryDiagnoser>]
type Comparisons() =
    let mutable source = Unchecked.defaultof<Prepared>
    let mutable pipeline: Transform list = []
    let mutable set = Kernels.portable

    static member Members = members
    static member Sizes = sizes
    static member Cases = [ "cmp"; "cmp-and" ]

    [<ParamsSource("Cases")>]
    member val Case = "" with get, set

    [<ParamsSource("Members")>]
    member val Member = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        source <- rowSource this.Case this.Rows
        pipeline <- rowCase this.Case
        set <- kernelSet this.Member
        checkRowCase this.Case this.Rows set pipeline source

    [<Benchmark>]
    member _.Evaluate() = evalRowCase set pipeline source

/// The morsel runner: a compiled `Filter` and the sheet's `Derive`s, in morsels of the compiled
/// `Kernels.MorselRows` — the suite the sweep rebuilds at each size.
[<MemoryDiagnoser>]
type Morsels() =
    let mutable source = Unchecked.defaultof<Prepared>
    let mutable pipeline: Transform list = []
    let mutable set = Kernels.portable

    static member Members = members
    static member Sizes = sizes
    static member Cases = [ "compiled"; "derive" ]
    static member MorselSizes = [ Kernels.MorselRows ]

    [<ParamsSource("Cases")>]
    member val Case = "" with get, set

    [<ParamsSource("Members")>]
    member val Member = "" with get, set

    [<ParamsSource("Sizes")>]
    member val Rows = 0 with get, set

    /// The morsel size this build was compiled at, as a column of the results.
    [<ParamsSource("MorselSizes")>]
    member val MorselRows = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        source <- rowSource this.Case this.Rows
        pipeline <- rowCase this.Case
        set <- kernelSet this.Member
        checkRowCase this.Case this.Rows set pipeline source

    [<Benchmark>]
    member _.Evaluate() = evalRowCase set pipeline source

// ---- Phase 270's parallel GroupBy, rebuilt ---------------------------------------------------

/// The grouping table: an int key `k` over `cardinality` values, visited in a scrambled order, and a
/// float `v` in whole quarters, so every total is exact whatever order it is added in.
let groupTable (n: int) (cardinality: int) : Table =
    { Schema = [ Field.create "k" IntType; Field.create "v" FloatType ]
      Columns =
        [ col "k" IntType [ for i in 0 .. n - 1 -> Int(((i % cardinality) * 7919) % cardinality) ]
          col "v" FloatType [ for i in 0 .. n - 1 -> Float(float ((i * 13) % 397) * 0.25) ] ] }

/// The two cardinalities 270 measured: ten keys, and one key per ten rows.
let cardinalities = [ "ten"; "tenth" ]

let cardinality (name: string) (n: int) : int =
    match name with
    | "ten" -> 10
    | "tenth" -> n / 10
    | _ -> failwithf "benchmark corpus: no cardinality named '%s'" name

let groupStep: Transform =
    GroupBy([ "k" ], [ { Name = "total"; Fn = Sum; Of = "v" }; { Name = "n"; Fn = Count; Of = "v" } ])

/// The shipped, sequential grouping: one step over the frame.
let internal sequentialGroupBy (f: Frame) : Frame =
    DataFrame.evalStepWith Kernels.native DataFrame.noResolve Map.empty f groupStep
    |> Corpus.orFail "group-by"

/// Phase 270's prototype, from its recorded description: "per-morsel tables merged on first-seen
/// index". Each morsel of `Kernels.MorselRows` logical rows is grouped by the shipped step on its own
/// thread (since Phase 323, streamed aggregates over a slot per row), and the per-morsel tables are
/// merged in morsel order: a key opens a slot the first time any morsel's table names it, so the
/// output order is first appearance in the frame, and a key met again adds its partial `total` and
/// `n`. A float `Sum` merged so is REASSOCIATED, which is why it was never a shipping shape (Phase
/// 344 partitions by group instead); the corpus's quarter values keep it exact here, and the setup
/// holds it equal to the sequential answer.
let internal morselMergeGroupBy (f: Frame) : Frame =
    let phys = Frame.physical f
    let n = phys.Length
    let m = Kernels.morselCount n
    let parts: Frame[] = Array.zeroCreate m

    Parallel.For(
        0,
        m,
        fun j ->
            let sel = phys[Kernels.morselStart j .. Kernels.morselEnd n j - 1]
            parts[j] <- sequentialGroupBy (Frame.select f sel)
    )
    |> ignore

    let slotOf = Dictionary<int, int>()
    let keys = ResizeArray<int>()
    let totals = ResizeArray<float>()
    let counts = ResizeArray<int>()

    for part in parts do
        match part.Vecs with
        | [| Ints(ks, _); Floats(ts, _); Ints(cs, _) |] ->
            for g in 0 .. part.Count - 1 do
                match slotOf.TryGetValue ks[g] with
                | true, s ->
                    totals[s] <- totals[s] + ts[g]
                    counts[s] <- counts[s] + cs[g]
                | _ ->
                    slotOf[ks[g]] <- keys.Count
                    keys.Add ks[g]
                    totals.Add ts[g]
                    counts.Add cs[g]
        | _ -> failwith "benchmark corpus: a morsel's grouping did not come back as int, float, int vectors"

    let present = Array.create keys.Count true

    { Cols = parts[0].Cols
      Vecs =
        [| Ints(keys.ToArray(), present)
           Floats(totals.ToArray(), present)
           Ints(counts.ToArray(), present) |]
      Sel = None
      Count = keys.Count }

let groupArms = [ "sequential"; "morsel-merge" ]

let internal groupArm (name: string) : Frame -> Frame =
    match name with
    | "sequential" -> sequentialGroupBy
    | "morsel-merge" -> morselMergeGroupBy
    | _ -> failwithf "benchmark corpus: no group-by arm named '%s'" name

/// The shipped grouping against the rebuilt prototype, at 270's cardinalities.
[<MemoryDiagnoser>]
type ParallelGroupBy() =
    let mutable frame = Unchecked.defaultof<Frame>
    let mutable arm: Frame -> Frame = sequentialGroupBy

    static member Arms = groupArms
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
        frame <- Frame.ofTable (groupTable this.Rows (cardinality this.Keys this.Rows))
        arm <- groupArm this.Arm
        let mine = Frame.toTable (arm frame)

        if mine <> Frame.toTable (sequentialGroupBy frame) then
            failwithf "benchmark corpus: the %s group-by disagrees with the sequential step" this.Arm

        if Table.rowCount mine <> cardinality this.Keys this.Rows then
            failwithf "benchmark corpus: the %s group-by made %d groups" this.Arm (Table.rowCount mine)

    [<Benchmark>]
    member _.Evaluate() = (arm frame).Count
