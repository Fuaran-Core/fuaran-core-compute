/// The compute benchmark corpus (Phase 262): every input, pipeline and yardstick both timing
/// harnesses measure, in one file, so the .NET leg (BenchmarkDotNet) and the node leg (the Fable
/// compiled harness in Node/) time exactly the same work.
///
/// FSharp.Core and the dataframe package only, and Fable-clean: no reflection, no BCL beyond what
/// Fable maps (arrays, `Dictionary`, `ResizeArray`), and integer arithmetic that stays well inside
/// int32 so the generators produce the same tables under node as on .NET.
///
/// Four groups, one table per shape in the results:
///
///   * the SHEET — a spreadsheet-shaped workload: an `orders` table, a `targets` table and a scalar
///     `threshold`, with two nodes (`lines` and `byRegion`) at 1,000 / 10,000 / 100,000 rows, and a
///     hand-written arrays-and-loops full-recompute arm beside each node as the yardstick;
///   * the SCALING pipelines — the three `Scaling` shapes from the suite (`Filter > GroupBy`,
///     `Filter > Sort > Limit 10`, `Filter > GroupBy > Filter`) at 1,000 and 20,000 rows, as a full
///     evaluation and as a one-row-edit restricted refresh;
///   * the SHAPES the suite never times — an inner join, a high-cardinality group-by, a pivot with
///     50 on-values, a one-partition window and a two-key sort;
///   * the TYPED family (Phase 280) — filter, group-and-sum, sort and join over one value column
///     carried as a decimal, a float and an integer, at 1,000 / 10,000 / 100,000 rows.
module Fuaran.Core.Compute.Benchmarks.Corpus

open System.Collections.Generic
open Fuaran.Core
open Fuaran.Compute

/// Unwrap an evaluator result OUTSIDE a timed region; an error is a broken corpus, never a figure.
let orFail (what: string) (r: Result<'T, 'E>) : 'T =
    match r with
    | Ok v -> v
    | Error e -> failwithf "benchmark corpus: %s failed: %A" what e

let private col (name: string) (ty: ColumnType) (cells: Cell list) : Column = Column.create name ty cells

// ---- the sheet ------------------------------------------------------------------------------

/// The sheet's sizes.
let sheetSizes = [ 1_000; 10_000; 100_000 ]

/// The five values of `orders.region`.
let regions = [| "north"; "south"; "east"; "west"; "central" |]

/// The raw arrays one `orders` table is built from. They are also the hand arm's own input: the
/// hand arm is what a programmer writes with no algebra at all, so it holds plain arrays rather
/// than reading the evaluator's `Table`.
type OrdersArrays =
    { Id: int[]
      Region: string[]
      Qty: int[]
      Price: float[] }

/// `n` orders. Prices are whole quarters (`k * 0.25`) and quantities small integers, so every
/// `amount` and every per-region total is exactly representable in a double: the hand arm's sums
/// and the evaluator's are equal as values whatever order either adds in, and the equality check
/// below can be exact rather than toleranced.
let ordersArrays (n: int) : OrdersArrays =
    { Id = Array.init n id
      Region = Array.init n (fun i -> regions.[(i * 3 + i / 7) % regions.Length])
      Qty = Array.init n (fun i -> 1 + (i * 7 + i / 3) % 20)
      Price = Array.init n (fun i -> float (4 + (i * 13) % 397) * 0.25) }

/// The `orders` table: `id:int, region:string, qty:int, price:float`.
let ordersTable (a: OrdersArrays) : Table =
    { Schema = [ "id", IntType; "region", StringType; "qty", IntType; "price", FloatType ]
      Columns =
        [ col "id" IntType [ for v in a.Id -> Int v ]
          col "region" StringType [ for v in a.Region -> Str v ]
          col "qty" IntType [ for v in a.Qty -> Int v ]
          col "price" FloatType [ for v in a.Price -> Float v ] ] }

/// The sheet's second input, `targets(region, target)`: one row per region. No node in this corpus
/// reads it yet; it is part of the sheet's shape so that a later measurement which joins it into
/// `byRegion` times the same inputs as every earlier one.
let targetsTable: Table =
    { Schema = [ "region", StringType; "target", FloatType ]
      Columns =
        [ col "region" StringType [ for r in regions -> Str r ]
          col "target" FloatType [ for i in 0 .. regions.Length - 1 -> Float(250_000.0 * float (i + 1)) ] ] }

/// The sheet's scalar cell.
let threshold = 500.0

/// The binding environment the sheet evaluates in: the scalar reaches the pipeline as a `Param`.
let sheetEnv: Map<string, Cell> = Map.ofList [ "threshold", Float threshold ]

let private amountStep = Derive("amount", Binary(Mul, Col "qty", Col "price"))

/// Node `lines`: `amount = qty * price`, then `big = amount >= threshold`. A derive, not a filter,
/// so every row is kept.
let linesPipeline: Transform list =
    [ amountStep; Derive("big", Binary(Ge, Col "amount", Param "threshold")) ]

/// Node `byRegion`: `amount`, then one row per region with its total and its row count.
let byRegionPipeline: Transform list =
    [ amountStep
      GroupBy(
          [ "region" ],
          [ { Name = "total"
              Fn = Sum
              Of = "amount" }
            { Name = "n"
              Fn = Count
              Of = "amount" } ]
      ) ]

/// The evaluator's arm for `lines`.
let evalLines (orders: Table) : Result<Table, EvalError> =
    DataFrame.evalPipelineInEnv sheetEnv linesPipeline orders

/// The evaluator's arm for `byRegion`.
let evalByRegion (orders: Table) : Result<Table, EvalError> =
    DataFrame.evalPipelineInEnv sheetEnv byRegionPipeline orders

/// The hand arm's `lines`: the two derived columns, recomputed in full.
type HandLines = { Amount: float[]; Big: bool[] }

/// The hand arm's `byRegion`: regions in first-appearance order, with their totals and counts.
type HandByRegion =
    { Region: string[]
      Total: float[]
      N: int[] }

/// The hand arm for `lines`: one loop over the arrays, a full recompute.
let handLines (a: OrdersArrays) (threshold: float) : HandLines =
    let n = a.Qty.Length
    let amount: float[] = Array.zeroCreate n
    let big: bool[] = Array.zeroCreate n

    for i in 0 .. n - 1 do
        let v = float a.Qty.[i] * a.Price.[i]
        amount.[i] <- v
        big.[i] <- v >= threshold

    { Amount = amount; Big = big }

/// The hand arm for `byRegion`: one loop over the arrays, a dictionary from region to its slot,
/// a full recompute (the amount is recomputed here too, as a hand-written sheet would).
let handByRegion (a: OrdersArrays) : HandByRegion =
    let slot = Dictionary<string, int>()
    let keys = ResizeArray<string>()
    let totals = ResizeArray<float>()
    let counts = ResizeArray<int>()

    for i in 0 .. a.Qty.Length - 1 do
        let v = float a.Qty.[i] * a.Price.[i]
        let r = a.Region.[i]

        match slot.TryGetValue r with
        | true, g ->
            totals.[g] <- totals.[g] + v
            counts.[g] <- counts.[g] + 1
        | _ ->
            slot.[r] <- keys.Count
            keys.Add r
            totals.Add v
            counts.Add 1

    { Region = keys.ToArray()
      Total = totals.ToArray()
      N = counts.ToArray() }

let private cellsOf (name: string) (t: Table) : Cell list =
    match t.Columns |> List.tryFind (fun c -> c.Name = name) with
    | Some c -> c.Cells
    | None -> failwithf "benchmark corpus: the evaluated table has no column '%s'" name

let private expectCells (what: string) (expected: Cell list) (actual: Cell list) =
    if expected <> actual then
        failwithf "benchmark corpus: the hand arm and the evaluator disagree on %s" what

/// The yardstick is only a yardstick if it computes the same answer. Asserted for both nodes, at
/// the given size, before anything is timed; a disagreement throws.
let checkSheet (a: OrdersArrays) (orders: Table) : unit =
    let lines = evalLines orders |> orFail "lines"
    let hand = handLines a threshold
    expectCells "lines.id" [ for v in a.Id -> Int v ] (cellsOf "id" lines)
    expectCells "lines.amount" [ for v in hand.Amount -> Float v ] (cellsOf "amount" lines)
    expectCells "lines.big" [ for v in hand.Big -> Bool v ] (cellsOf "big" lines)

    let byRegion = evalByRegion orders |> orFail "byRegion"
    let handGroups = handByRegion a
    expectCells "byRegion.region" [ for v in handGroups.Region -> Str v ] (cellsOf "region" byRegion)
    expectCells "byRegion.total" [ for v in handGroups.Total -> Float v ] (cellsOf "total" byRegion)
    expectCells "byRegion.n" [ for v in handGroups.N -> Int v ] (cellsOf "n" byRegion)

// ---- the Scaling pipelines -------------------------------------------------------------------

/// The Scaling family's two sizes.
let scalingSizes = [ 1_000; 20_000 ]

/// The row identity the refresh keys on.
let scalingIdentity = RowIdentity.byColumn "id"

/// The Scaling family's table: a string identity, a grouping key over seventeen values and two
/// integer measures.
let scalingTable (n: int) : Table =
    { Schema = [ "id", StringType; "grp", StringType; "a", IntType; "b", IntType ]
      Columns =
        [ col "id" StringType [ for i in 0 .. n - 1 -> Str("r" + string i) ]
          col "grp" StringType [ for i in 0 .. n - 1 -> Str("g" + string (i % 17)) ]
          col "a" IntType [ for i in 0 .. n - 1 -> Int i ]
          col "b" IntType [ for i in 0 .. n - 1 -> Int(i % 7) ] ] }

/// The one-row edit the refresh answers: row `n / 2`'s `a` becomes -1.
let editOne (t: Table) : Table =
    let n = Table.rowCount t

    { t with
        Columns =
            t.Columns
            |> List.map (fun c ->
                if c.Name <> "a" then
                    c
                else
                    { c with
                        Cells = c.Cells |> List.mapi (fun i cell -> if i = n / 2 then Int -1 else cell) }) }

let private everyRow = Filter(Binary(Ge, Col "a", Lit(Int -10)))

let private twoAggs =
    GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ])

/// The three Scaling pipelines, by the name each result row carries.
let scalingPipelines: (string * Transform list) list =
    [ "filter-groupby", [ everyRow; twoAggs ]
      "filter-sort-limit", [ everyRow; Transform.sortBy [ "a", Desc ]; Transform.limit 10 0 ]
      "filter-groupby-filter", [ everyRow; twoAggs; Filter(Binary(Gt, Col "n", Lit(Int 0))) ] ]

/// A Scaling pipeline by name.
let scalingPipeline (name: string) : Transform list =
    match scalingPipelines |> List.tryFind (fun (n, _) -> n = name) with
    | Some(_, p) -> p
    | None -> failwithf "benchmark corpus: no Scaling pipeline named '%s'" name

/// Everything a one-row-edit refresh needs, prepared outside the timed region: the edited table,
/// the state primed on the original and the delta between them.
type RefreshInputs =
    { Pipeline: Transform list
      After: Table
      State: IncrementalEval
      Delta: TableDelta }

let refreshInputs (pipeline: Transform list) (n: int) : RefreshInputs =
    let before = scalingTable n
    let after = editOne before

    { Pipeline = pipeline
      After = after
      State = Incremental.primeOn scalingIdentity pipeline before |> orFail "prime"
      Delta = Delta.diff scalingIdentity before after |> orFail "diff" }

/// The full evaluation of the edited table.
let scalingFull (r: RefreshInputs) : Result<Table, EvalError> =
    DataFrame.evalPipeline r.Pipeline r.After

/// The restricted refresh over the one-row delta.
let scalingRefresh (r: RefreshInputs) =
    Incremental.refreshOn scalingIdentity r.Pipeline r.State r.Delta r.After

// ---- the shapes ------------------------------------------------------------------------------

/// Inner join, 10,000 x 10,000 rows on one int key. Both key columns are permutations of
/// 0..n-1 (multiplication by a prime coprime to n), so every left row matches exactly one right
/// row and the two sides arrive in different orders.
let joinRows = 10_000

let joinLeft () : Table =
    { Schema = [ "k", IntType; "a", IntType ]
      Columns =
        [ col "k" IntType [ for i in 0 .. joinRows - 1 -> Int((i * 7919) % joinRows) ]
          col "a" IntType [ for i in 0 .. joinRows - 1 -> Int i ] ] }

let joinRight () : Table =
    { Schema = [ "rk", IntType; "b", IntType ]
      Columns =
        [ col "rk" IntType [ for i in 0 .. joinRows - 1 -> Int((i * 104729) % joinRows) ]
          col "b" IntType [ for i in 0 .. joinRows - 1 -> Int i ] ] }

let joinPipeline () : Transform list =
    [ Join(Embedded(joinRight ()), [ "k", "rk" ], Inner) ]

/// Group-by over 10,000 distinct string keys: 20,000 rows, each key twice.
let groupKeys = 10_000

let groupRows = 2 * groupKeys

let groupTable () : Table =
    { Schema = [ "key", StringType; "v", IntType ]
      Columns =
        [ col "key" StringType [ for i in 0 .. groupRows - 1 -> Str("k" + string (i % groupKeys)) ]
          col "v" IntType [ for i in 0 .. groupRows - 1 -> Int(i % 100) ] ] }

let groupPipeline: Transform list =
    [ GroupBy([ "key" ], [ { Name = "s"; Fn = Sum; Of = "v" }; { Name = "n"; Fn = Count; Of = "v" } ]) ]

/// Pivot with 50 on-values: 100 index values x 50 on-values, one row per pair (5,000 rows).
let pivotOnValues = 50

let pivotIndexValues = 100

let pivotRows = pivotOnValues * pivotIndexValues

let pivotTable () : Table =
    { Schema = [ "idx", StringType; "on", StringType; "v", FloatType ]
      Columns =
        [ col "idx" StringType [ for i in 0 .. pivotRows - 1 -> Str("i" + string (i % pivotIndexValues)) ]
          col "on" StringType [ for i in 0 .. pivotRows - 1 -> Str("o" + string (i / pivotIndexValues)) ]
          col "v" FloatType [ for i in 0 .. pivotRows - 1 -> Float(float (i % 13) * 0.5) ] ] }

let pivotPipeline: Transform list =
    [ Pivot
          { Index = [ "idx" ]
            On = "on"
            Values = "v"
            Agg = Sum } ]

/// A window over one partition: a running sum over 100,000 rows in sequence order.
let windowRows = 100_000

let windowTable () : Table =
    { Schema = [ "seq", IntType; "v", IntType ]
      Columns =
        [ col "seq" IntType [ for i in 0 .. windowRows - 1 -> Int i ]
          col "v" IntType [ for i in 0 .. windowRows - 1 -> Int(i % 10) ] ] }

let windowPipeline: Transform list =
    [ Window
          { PartitionBy = []
            OrderBy = [ "seq", Asc ]
            Fn = CumulSum
            Of = "v"
            As = "cs" } ]

/// A two-key sort over 100,000 rows: a string key over 100 values ascending, then a permuted int
/// key descending.
let sortRows = 100_000

let sortTable () : Table =
    { Schema = [ "k1", StringType; "k2", IntType ]
      Columns =
        [ col "k1" StringType [ for i in 0 .. sortRows - 1 -> Str("c" + string (i % 100)) ]
          col "k2" IntType [ for i in 0 .. sortRows - 1 -> Int((i * 7919) % sortRows) ] ] }

let sortPipeline: Transform list = [ Transform.sortBy [ "k1", Asc; "k2", Desc ] ]

/// The five shapes, by the name each result row carries, with their input row count and a builder
/// for the input table and the pipeline. Builders rather than values, so a process that times one
/// shape never pays for building the others.
let shapes: (string * int * (unit -> Table * Transform list)) list =
    [ "inner join 10,000 x 10,000", joinRows, (fun () -> joinLeft (), joinPipeline ())
      "group-by, 10,000 distinct keys", groupRows, (fun () -> groupTable (), groupPipeline)
      "pivot, 50 on-values", pivotRows, (fun () -> pivotTable (), pivotPipeline)
      "window CumulSum, one partition", windowRows, (fun () -> windowTable (), windowPipeline)
      "sort on two keys", sortRows, (fun () -> sortTable (), sortPipeline) ]

/// A shape's input and pipeline, by name.
let shape (name: string) : Table * Transform list =
    match shapes |> List.tryFind (fun (n, _, _) -> n = name) with
    | Some(_, _, build) -> build ()
    | None -> failwithf "benchmark corpus: no shape named '%s'" name

/// The shapes' expected output row counts, asserted before timing so a shape that silently
/// evaluated to nothing cannot report a fast figure.
let shapeOutputRows (name: string) : int =
    match name with
    | "inner join 10,000 x 10,000" -> joinRows
    | "group-by, 10,000 distinct keys" -> groupKeys
    | "pivot, 50 on-values" -> pivotIndexValues
    | "window CumulSum, one partition" -> windowRows
    | "sort on two keys" -> sortRows
    | other -> failwithf "benchmark corpus: no shape named '%s'" other

/// Evaluate one shape and check its output row count.
let checkShape (name: string) (input: Table) (pipeline: Transform list) : unit =
    let out = DataFrame.evalPipeline pipeline input |> orFail name
    let want = shapeOutputRows name
    let got = Table.rowCount out

    if got <> want then
        failwithf "benchmark corpus: %s produced %d rows, expected %d" name got want

// ---- the typed family (Phase 280) --------------------------------------------------------------

/// The typed family's sizes.
let typedSizes = [ 1_000; 10_000; 100_000 ]

/// The three value types the family compares: the same values carried as an exact decimal, as a
/// float and as an integer count of hundredths.
let typedTypes = [ DecimalType; FloatType; IntType ]

/// The family's verbs, by the name each result row carries.
let typedVerbs = [ "filter"; "group-and-sum"; "sort"; "join" ]

/// The number of `grp` values the group-and-sum folds into.
let typedGroups = 50

/// The hundredths row `i` holds: a permutation of `0 .. 99,999` taken by multiplication with a prime
/// coprime to 100,000, so the values are distinct at every size and arrive unordered. Inside int32.
let typedHundredths (i: int) : int = (i * 7919) % 100_000

/// `h` hundredths as the canonical decimal text (`DecimalText`): no trailing fraction zero, no point
/// on a whole number.
let typedDecimalText (h: int) : string =
    let whole = h / 100
    let frac = h % 100

    if frac = 0 then
        string whole
    elif frac % 10 = 0 then
        string whole + "." + string (frac / 10)
    elif frac < 10 then
        string whole + ".0" + string frac
    else
        string whole + "." + string frac

/// The cell carrying `h` hundredths under `ty`.
let typedCell (ty: ColumnType) (h: int) : Cell =
    match ty with
    | DecimalType -> Decimal(typedDecimalText h)
    | FloatType -> Float(float h / 100.0)
    | _ -> Int h

/// The family's input: `id:int, grp:string, v:<ty>`, `n` rows.
let typedTable (ty: ColumnType) (n: int) : Table =
    { Schema = [ "id", IntType; "grp", StringType; "v", ty ]
      Columns =
        [ col "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          col "grp" StringType [ for i in 0 .. n - 1 -> Str("g" + string (i % typedGroups)) ]
          col "v" ty [ for i in 0 .. n - 1 -> typedCell ty (typedHundredths i) ] ] }

/// The join's right side: every left value once, in another order (a second prime coprime to every
/// size, reduced first so the product stays inside int32), so each left row matches exactly one
/// right row.
let typedRight (ty: ColumnType) (n: int) : Table =
    { Schema = [ "rv", ty; "b", IntType ]
      Columns =
        [ col "rv" ty [ for j in 0 .. n - 1 -> typedCell ty (typedHundredths ((j * (104729 % n)) % n)) ]
          col "b" IntType [ for j in 0 .. n - 1 -> Int j ] ] }

/// The filter's threshold, 500 in every carrier: about half the rows pass at every size.
let typedThreshold = 50_000

/// One verb's pipeline over `ty` at `n` rows.
let typedPipeline (verb: string) (ty: ColumnType) (n: int) : Transform list =
    match verb with
    | "filter" -> [ Filter(Binary(Gt, Col "v", Lit(typedCell ty typedThreshold))) ]
    | "group-and-sum" -> [ GroupBy([ "grp" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ]
    | "sort" -> [ Transform.sortBy [ "v", Asc ] ]
    | "join" -> [ Join(Embedded(typedRight ty n), [ "v", "rv" ], Inner) ]
    | other -> failwithf "benchmark corpus: no typed verb named '%s'" other

/// The output row count each verb must produce at `n` rows, asserted before timing.
let typedOutputRows (verb: string) (n: int) : int =
    match verb with
    | "filter" ->
        let mutable c = 0

        for i in 0 .. n - 1 do
            if typedHundredths i > typedThreshold then
                c <- c + 1

        c
    | "group-and-sum" -> min n typedGroups
    | _ -> n

/// The type's name as a result row prints it.
let typedName (ty: ColumnType) : string =
    match ty with
    | DecimalType -> "decimal"
    | FloatType -> "float"
    | _ -> "int"

/// Evaluate one case and check it: its row count, and for the group-and-sum the decimal arm's totals
/// equal to the integer arm's hundredths rendered as decimal text, so the decimal path is timed
/// computing the exact answer.
let checkTyped (verb: string) (ty: ColumnType) (n: int) (input: Table) (pipeline: Transform list) : unit =
    let out =
        DataFrame.evalPipeline pipeline input |> orFail (verb + " " + typedName ty)

    let want = typedOutputRows verb n
    let got = Table.rowCount out

    if got <> want then
        failwithf "benchmark corpus: %s over %s produced %d rows, expected %d" verb (typedName ty) got want

    if verb = "group-and-sum" && ty = DecimalType then
        let totals = Dictionary<string, int64>()

        for i in 0 .. n - 1 do
            let g = "g" + string (i % typedGroups)
            let prior = if totals.ContainsKey g then totals[g] else 0L
            totals[g] <- prior + int64 (typedHundredths i)

        let groups = cellsOf "grp" out
        let sums = cellsOf "s" out

        List.zip groups sums
        |> List.iter (fun (g, s) ->
            let key =
                match g with
                | Str k -> k
                | other -> failwithf "benchmark corpus: a group key %A" other

            let h = totals[key]
            let whole = h / 100L
            let frac = int (h % 100L)
            let text = typedDecimalText frac
            // `typedDecimalText` renders hundredths below 100 as `0.xx`; splice the whole part in.
            let expected =
                if frac = 0 then
                    string whole
                else
                    string whole + text.Substring 1

            if s <> Decimal expected then
                failwithf "benchmark corpus: group %s summed to %A, expected %s" key s expected)
