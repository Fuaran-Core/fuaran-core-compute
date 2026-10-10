module Fuaran.Compute.Tests.IncrementalWindowTests

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

// ---------------------------------------------------------------------------
//  Phase 333 — the running windows resumed from their earliest change.
//
//  A prefix fold (`DataFrame.windowIsPrefixFold`: the row number, the running
//  total, the running extremes) is a function, at each row, of the rows at or
//  before it in its partition's window order. So a refresh reuses each
//  partition's prior run up to its earliest changed position and resumes the
//  reference's own scan there, seeded with the cell at the position before it
//  (`Incremental.windowResume`). The law below holds that refresh equal to the
//  full evaluation TO THE BIT over generated deltas of every class the shard
//  names — an append, a mid-partition value edit, an order-key move, a
//  partition-key move, a delete, a partition emptied or created, and a
//  partition whose values are all null — over int, mixed int-and-float and
//  decimal value columns, with and without a partition key, ascending and
//  descending, with a filter ahead of the window and a second window after it,
//  and over a group table.
//
//  Each class is also run against a PERTURBED seed (`refreshSeedShifted`: every
//  resumed run starts one position late, the earliest changed row taking the
//  seed unchanged), and must go red there on some draw: a law that held under
//  the perturbation would not be reaching the resumed path at all.
// ---------------------------------------------------------------------------

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failwithf "not decimal text: %s" text

/// Two tables equal to the bit: equal schemas, every float cell by its IEEE bits, every other cell
/// structurally (a decimal by its text).
let private sameBits (a: Table) (b: Table) : bool =
    let cellEq (x: Cell) (y: Cell) =
        match x, y with
        | Float p, Float q -> System.BitConverter.DoubleToInt64Bits p = System.BitConverter.DoubleToInt64Bits q
        | _ -> x = y

    a.Schema = b.Schema
    && List.length a.Columns = List.length b.Columns
    && List.forall2
        (fun (c: Column) (d: Column) ->
            c.Name = d.Name
            && Column.length c = List.length (Column.toCells d)
            && List.forall2 cellEq (Column.toCells c) (Column.toCells d))
        a.Columns
        b.Columns

/// Two evaluation results equal to the bit, or the same error.
let private sameResult (a: Result<Table, EvalError>) (b: Result<Table, EvalError>) : bool =
    match a, b with
    | Ok x, Ok y -> sameBits x y
    | Error x, Error y -> x = y
    | _ -> false

/// The value column's flavour.
type private Flavour =
    /// An int column, nulls among the values.
    | IntValues
    /// A float column holding ints and floats — the order-sensitive sums (0.1 + 0.2 is not 0.2 + 0.1
    /// to the bit), both zeros, a NaN — and nulls.
    | MixedValues
    /// A decimal column (an exact running total, Phase 277), non-canonical spellings among the
    /// values, and nulls.
    | DecimalValues

type private Row = { Id: int; P: Cell; O: Cell; V: Cell }

/// The partition every row of which has a null value.
let private allNull = Str "z"

let private valueOf (flavour: Flavour) (rng: System.Random) : Cell =
    if rng.Next 6 = 0 then
        Null
    else
        match flavour with
        | IntValues -> Int(rng.Next(-5, 20))
        | MixedValues ->
            [| Int 1
               Int -3
               Float 0.1
               Float 0.2
               Float 0.7
               Float -0.0
               Float 0.0
               Float nan
               Int 4 |][rng.Next 9]
        | DecimalValues -> [| dec "1.50"; dec "1.5"; dec "-2.25"; dec "0.10"; dec "7"; dec "0.333" |][rng.Next 6]

let private partitionOf (rng: System.Random) : Cell =
    match rng.Next 9 with
    | 0 -> Null
    | 1 -> allNull
    | k -> [| Str "a"; Str "b"; Str "c" |][k % 3]

let private rowOf (flavour: Flavour) (rng: System.Random) (id: int) : Row =
    let p = partitionOf rng

    { Id = id
      P = p
      O = (if rng.Next 8 = 0 then Null else Int(rng.Next 10))
      V = (if p = allNull then Null else valueOf flavour rng) }

let private tableOf (flavour: Flavour) (rows: Row list) : Table =
    let vType =
        match flavour with
        | IntValues -> IntType
        | MixedValues -> FloatType
        | DecimalValues -> DecimalType

    { Schema =
        [ Field.create "id" IntType
          Field.create "p" StringType
          Field.create "o" IntType
          Field.create "v" vType ]
      Columns =
        [ KitColumn.create "id" IntType (rows |> List.map (fun r -> Int r.Id))
          KitColumn.create "p" StringType (rows |> List.map (fun r -> r.P))
          KitColumn.create "o" IntType (rows |> List.map (fun r -> r.O))
          KitColumn.create "v" vType (rows |> List.map (fun r -> r.V)) ] }

/// A class of delta, as the shard names them.
type private DeltaClass =
    | Append
    | MidPartitionUpdate
    | OrderKeyMove
    | PartitionKeyMove
    | Delete
    | EmptyPartition
    | AllNullPartition

let private deltaClasses =
    [ Append
      MidPartitionUpdate
      OrderKeyMove
      PartitionKeyMove
      Delete
      EmptyPartition
      AllNullPartition ]

/// Apply one delta of class `cls` to `rows` (`next` the first unused id).
let private applyDelta (flavour: Flavour) (rng: System.Random) (cls: DeltaClass) (next: int) (rows: Row list) =
    let arr = List.toArray rows
    let pick () = rng.Next arr.Length

    match cls with
    | Append -> rows @ [ for k in 0 .. rng.Next(1, 4) - 1 -> rowOf flavour rng (next + k) ]
    | _ when arr.Length = 0 -> [ rowOf flavour rng next ]
    | MidPartitionUpdate ->
        for _ in 1 .. rng.Next(1, 3) do
            let i = pick ()

            if arr[i].P <> allNull then
                arr[i] <- { arr[i] with V = valueOf flavour rng }

        List.ofArray arr
    | OrderKeyMove ->
        let i = pick ()

        arr[i] <-
            { arr[i] with
                O = (if rng.Next 6 = 0 then Null else Int(rng.Next 10)) }

        List.ofArray arr
    | PartitionKeyMove ->
        let i = pick ()
        let p = partitionOf rng

        arr[i] <-
            { arr[i] with
                P = p
                V = (if p = allNull then Null else arr[i].V) }

        List.ofArray arr
    | Delete ->
        let drop = set [ for _ in 1 .. rng.Next(1, 4) -> arr[pick ()].Id ]
        rows |> List.filter (fun r -> not (drop.Contains r.Id))
    | EmptyPartition ->
        // Empty one partition, or open a new one.
        if rng.Next 2 = 0 then
            let p = arr[pick ()].P
            rows |> List.filter (fun r -> r.P <> p)
        else
            rows
            @ [ { rowOf flavour rng next with
                    P = Str "fresh" } ]
    | AllNullPartition ->
        // An edit inside the all-null partition: a row added to it, or one of its rows moved in order.
        match rows |> List.tryFindIndex (fun r -> r.P = allNull) with
        | Some i when rng.Next 2 = 0 ->
            arr[i] <- { arr[i] with O = Int(rng.Next 10) }
            List.ofArray arr
        | _ ->
            rows
            @ [ { rowOf flavour rng next with
                    P = allNull
                    V = Null } ]

let private prefixFolds = [ CumulSum; CumulMax; CumulMin; RowNumber ]

let private windowOf (fn: WindowFn) (partitioned: bool) (dir: SortDir) (name: string) =
    Window
        { PartitionBy = (if partitioned then [ "p" ] else [])
          OrderBy = [ "o", dir ]
          Fn = fn
          Of = "v"
          As = name }

/// A pipeline holding a prefix-fold window, drawn from the shapes the seam walks.
let private pipelineOf (rng: System.Random) : Transform list =
    let fn = prefixFolds[rng.Next prefixFolds.Length]
    let partitioned = rng.Next 3 > 0
    let dir = if rng.Next 2 = 0 then Asc else Desc
    let w = windowOf fn partitioned dir "w"

    match rng.Next 5 with
    | 0 -> [ Filter(Not(IsNull(Col "o"))); w ]
    | 1 ->
        [ w
          windowOf prefixFolds[rng.Next prefixFolds.Length] (rng.Next 2 = 0) Asc "w2" ]
    | 2 -> [ w; Filter(Not(IsNull(Col "w"))) ]
    | 3 ->
        [ GroupBy([ "p" ], [ { Name = "s"; Fn = Sum; Of = "v" }; { Name = "o"; Fn = Max; Of = "o" } ])
          Window
              { PartitionBy = []
                OrderBy = [ "o", dir ]
                Fn = fn
                Of = "s"
                As = "w" } ]
    | _ -> [ w ]

/// One generated case: a base, the base after a delta of class `cls`, and a pipeline.
let private caseOf (cls: DeltaClass) (seed: int) =
    let rng = System.Random seed
    let flavour = [| IntValues; MixedValues; DecimalValues |][rng.Next 3]
    let n = rng.Next 30
    let rows = [ for i in 0 .. n - 1 -> rowOf flavour rng i ]
    let after = applyDelta flavour rng cls n rows
    tableOf flavour rows, tableOf flavour after, pipelineOf rng

/// The refresh `refreshWith` makes of the delta from `before` to `after`, as a result.
let private refreshed refreshWith (pipeline: Transform list) (before: Table) (after: Table) =
    Incremental.prime DataFrame.noResolve Map.empty idw pipeline before
    |> Result.bind (fun state ->
        let delta = ok (Delta.diff idw before after)

        refreshWith DataFrame.noResolve Map.empty idw pipeline state delta after
        |> Result.map Incremental.result)

let private draws = 120

[<Tests>]
let incrementalWindowTests =
    testList
        "IncrementalWindow"
        [ for cls in deltaClasses do
              testCase (sprintf "a resumed prefix fold equals the full evaluation to the bit: %A" cls)
              <| fun _ ->
                  let mutable perturbedRed = 0

                  for seed in 1..draws do
                      let before, after, pipeline =
                          caseOf cls (seed * 7919 + int (hash (string cls)) % 1000)

                      let expected = DataFrame.evalPipeline pipeline after
                      let actual = refreshed Incremental.refresh pipeline before after

                      if not (sameResult actual expected) then
                          failtestf
                              "%A, seed %d: the refresh differs from the full evaluation\npipeline %A\nbefore %A\nafter %A\nrefresh %A\nfull %A"
                              cls
                              seed
                              pipeline
                              before
                              after
                              actual
                              expected

                      if not (sameResult (refreshed Incremental.refreshSeedShifted pipeline before after) expected) then
                          perturbedRed <- perturbedRed + 1

                  // The law is not vacuous: started one position late, the resumed run is wrong on
                  // some draw of this very class.
                  Expect.isGreaterThan
                      perturbedRed
                      0
                      (sprintf "%A: the perturbed seed must turn the law red on some draw" cls)

          testCase "a chain of refreshes stays equal to the full evaluation, every delta class in turn"
          <| fun _ ->
              // Each refresh resumes the run the PREVIOUS refresh recorded, not only a primed one.
              for seed in 1..40 do
                  let rng = System.Random(333 + seed)
                  let flavour = [| IntValues; MixedValues; DecimalValues |][rng.Next 3]
                  let mutable rows = [ for i in 0 .. rng.Next(0, 25) - 1 -> rowOf flavour rng i ]
                  let mutable next = 100
                  let pipeline = pipelineOf rng
                  let mutable table = tableOf flavour rows

                  let mutable state =
                      ok (Incremental.prime DataFrame.noResolve Map.empty idw pipeline table)

                  for step in 1..8 do
                      let cls = deltaClasses[rng.Next deltaClasses.Length]
                      rows <- applyDelta flavour rng cls next rows
                      next <- next + 5
                      let after = tableOf flavour rows
                      let delta = ok (Delta.diff idw table after)
                      state <- ok (Incremental.refresh DataFrame.noResolve Map.empty idw pipeline state delta after)

                      Expect.isTrue
                          (sameResult (Ok(Incremental.result state)) (DataFrame.evalPipeline pipeline after))
                          (sprintf "seed %d, step %d (%A): the refresh equals the full evaluation" seed step cls)

                      table <- after

          testCase "the prefix folds are named, and every other window function is recomputed whole"
          <| fun _ ->
              // The four functions whose value at a row is a fold over its partition's prefix, and
              // every other one by name: a rank reads the previous row's order key, `ntile` the
              // partition's length, `lead` the next row, the lag and the rolling pair a fixed run of
              // preceding values — none of which the output at the preceding row carries.
              let folds, others =
                  [ RowNumber
                    Rank
                    DenseRank
                    CompetitionRank
                    NTile 3
                    Lag
                    Lead
                    CumulSum
                    CumulMax
                    CumulMin
                    RollingMean
                    RollingSum ]
                  |> List.partition DataFrame.windowIsPrefixFold

              Expect.equal folds [ RowNumber; CumulSum; CumulMax; CumulMin ] "the prefix folds"

              Expect.equal
                  (others |> List.map Incremental.windowFnName)
                  [ "rank"
                    "denseRank"
                    "competitionRank"
                    "ntile"
                    "lag"
                    "lead"
                    "rollingMean"
                    "rollingSum" ]
                  "the functions the seam recomputes whole, by type"

              // Recomputed whole, the perturbation cannot reach them: the perturbed refresh still
              // equals the full evaluation on every draw.
              for seed in 1..60 do
                  let before, after, _ = caseOf MidPartitionUpdate seed
                  let fn = others[seed % others.Length]
                  let pipeline = [ windowOf fn (seed % 2 = 0) Asc "w" ]
                  let expected = DataFrame.evalPipeline pipeline after

                  Expect.isTrue
                      (sameResult (refreshed Incremental.refreshSeedShifted pipeline before after) expected)
                      (sprintf "%A, seed %d: a function the seam recomputes whole is not resumed" fn seed)

          testCase "an exact decimal running total resumes from its seed exactly, across a null"
          <| fun _ ->
              // Phase 277's exact `CumulSum` and its null-skipping rule are the seed contract: the
              // running total at the row before the change, a `Decimal`, continued exactly.
              let rows =
                  [ for i in 0..9 ->
                        { Id = i
                          P = Str "a"
                          O = Int i
                          V = (if i = 4 then Null else dec (string (i + 1) + ".10")) } ]

              let before = tableOf DecimalValues rows

              let after =
                  tableOf DecimalValues (rows |> List.map (fun r -> if r.Id = 6 then { r with V = dec "0.005" } else r))

              let pipeline = [ windowOf CumulSum true Asc "w" ]
              let actual = refreshed Incremental.refresh pipeline before after

              Expect.isTrue (sameResult actual (DataFrame.evalPipeline pipeline after)) "exact, across the null"

              let w =
                  (ok actual).Columns
                  |> List.find (fun c -> c.Name = "w")
                  |> (fun c -> (Column.toCells c))

              Expect.equal w[4] w[3] "a null carries the total forward"

              Expect.isTrue
                  (Cell.decimal "16.505"
                   |> Option.exists (fun c -> DataFrame.cellToken c = DataFrame.cellToken w[6]))
                  "resumed exactly from the seed: 10.40 + 6.10 + 0.005"

              Expect.isFalse
                  (sameResult
                      (refreshed Incremental.refreshSeedShifted pipeline before after)
                      (DataFrame.evalPipeline pipeline after))
                  "a seed one position late is wrong"

          testCase
              "an append, alone or beside an edit, resumes from the appended rows through the append diff (Phase 359)"
          <| fun _ ->
              // The append classes above already run through `Delta.diff`'s append path (the state's
              // prime keyed the prior source); this holds that they DO, so the laws above are laws of
              // that path, and adds the append made beside a mid-partition edit in one delta.
              let mutable appendsTaken = 0
              let mutable perturbedRed = 0

              for seed in 1..draws do
                  let rng = System.Random(359 * seed)
                  let flavour = [| IntValues; MixedValues; DecimalValues |][rng.Next 3]
                  let n = 1 + rng.Next 30
                  let rows = [ for i in 0 .. n - 1 -> rowOf flavour rng i ]

                  let edited =
                      if rng.Next 2 = 0 then
                          applyDelta flavour rng MidPartitionUpdate n rows
                      else
                          rows

                  let after' = applyDelta flavour rng Append n edited
                  let pipeline = pipelineOf rng
                  let before = tableOf flavour rows
                  let after = tableOf flavour after'
                  let state = ok (Incremental.prime DataFrame.noResolve Map.empty idw pipeline before)
                  let delta = ok (Delta.diff idw before after)

                  if (KeyedIndexes.appendOf delta after).IsSome then
                      appendsTaken <- appendsTaken + 1

                  let expected = DataFrame.evalPipeline pipeline after

                  let actual =
                      Incremental.refresh DataFrame.noResolve Map.empty idw pipeline state delta after
                      |> Result.map Incremental.result

                  Expect.isTrue
                      (sameResult actual expected)
                      (sprintf "seed %d: the refresh equals the full evaluation to the bit" seed)

                  let shifted =
                      Incremental.refreshSeedShifted DataFrame.noResolve Map.empty idw pipeline state delta after
                      |> Result.map Incremental.result

                  if not (sameResult shifted expected) then
                      perturbedRed <- perturbedRed + 1

              Expect.equal appendsTaken draws "every draw's delta is the append diff's"
              Expect.isGreaterThan perturbedRed 0 "the perturbed seed turns the law red on some draw" ]
