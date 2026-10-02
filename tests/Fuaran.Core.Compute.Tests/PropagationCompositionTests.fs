module Fuaran.Compute.Tests.PropagationCompositionTests

// Phase 250 — propagation composes with incremental, carried here by Phase 261. A downstream
// spreadsheet-shaped consumer's measurement (Phase 250) built a sheet over `Column.Ops`,
// `DataFrame.Incremental` and `Propagation` and found five places where the three strands met only
// through glue it kept by hand. Each was closed inside the contract, and the sheet at the bottom of
// this file composes them and is certified by `Conformance.propagationEvaluatorLawsWith`.
//
// Three of the five are facts about `Propagation` and `Ops` alone and stay with them in the
// Fuaran.Core repository (https://github.com/Fuaran-Core/fuaran-core), whose suite still carries
// `UpdateNode`, `changedForOp` and the part-map rules. The composition that needs THIS repository's
// packages lives here, as it stood in that repository's suite before fuaran-core#258 moved the
// compute strand out:
//
//   3. Column-granular reads — `Propagation.dirtyFromChangedParts` with `ColumnOps.changedColumns`
//      supplied as the changed-parts function (the two cases that call it).
//   4. `ColumnOps.deltaOf` — a one-cell edit reaches `Incremental` as one row.
//   5. The sheet — `Propagation.evalFromWith` over a tree edited by `UpdateNode` and a source edited
//      by `ColumnOps`, a table node's incremental state carried by the driver as its prior.
//
// `Propagation`, `Ops` and `Tree` are the substrate's, taken by package at the one pin; this suite
// references them and no packable project does (the `Compute boundary` tests hold that).

open Expecto
open Fuaran.Core
open Fuaran.Compute

/// The string id witness the sheet is walked with (the substrate suite's reference witness).
let private idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = (fun a b -> a = b) }

// =============================================================================================
//  3. column-granular reads
// =============================================================================================

let private partTests =
    let parts (xs: string list) = Some(Set.ofList xs)

    // orders is read by `lines` for {qty, price}, by `ids` for {id}, and whole by `raw`; `total`
    // reads `lines`.
    let partDeps: Map<string, Map<string, Set<string> option>> =
        Map.ofList
            [ "orders", Map.empty
              "lines", Map.ofList [ "orders", parts [ "qty"; "price" ] ]
              "ids", Map.ofList [ "orders", parts [ "id" ] ]
              "raw", Map.ofList [ "orders", None ]
              "total", Map.ofList [ "lines", None ] ]

    testList
        "column-granular reads"
        [ testCase
              "a price edit dirties the readers of price and everything downstream of them, and not the reader of id"
          <| fun () ->
              let op = SetCell("price", 0, Float 2.0)

              let dirty =
                  Propagation.dirtyFromChangedParts
                      partDeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              Expect.equal dirty (Set.ofList [ "orders"; "lines"; "total"; "raw" ]) "ids reads only the id column"

          testCase "an op whose moved columns are unknown dirties exactly what dirtyFromChangedIds does"
          <| fun () ->
              let op = AppendRows [ [ "id", Int 9 ] ]
              Expect.isNone (ColumnOps.changedColumns op) "an append moves every column"

              let byParts =
                  Propagation.dirtyFromChangedParts
                      partDeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              let byNodes =
                  Propagation.dirtyFromChangedIds (Propagation.nodeDependencies partDeps) (Set.singleton "orders")

              Expect.equal byParts byNodes "the part map degrades to the node map" ]

// =============================================================================================
//  4. ColumnOps.deltaOf
// =============================================================================================

let private orders (n: int) : Table =
    let rows = [ 0 .. n - 1 ]

    { Schema = [ "id", IntType; "qty", IntType; "price", FloatType ]
      Columns =
        [ Column.create "id" IntType (rows |> List.map (fun i -> Int(i + 1)))
          Column.create "qty" IntType (rows |> List.map (fun i -> Int(1 + i % 7)))
          Column.create "price" FloatType (rows |> List.map (fun i -> Float(0.25 * float (1 + i % 40)))) ] }

let private rid = RowIdentity.byColumn "id"

let private linesPipeline =
    [ Derive("amount", Binary(Mul, Cast(FloatType, Col "qty"), Col "price")) ]

let private deltaTests =
    let rowsOf (d: TableDelta) =
        match d with
        | FullRefresh -> None
        | RowSet r -> Some r.Rows

    testList
        "ColumnOps.deltaOf"
        [ testCase "a cell edit off the key is its one row, RowChanged"
          <| fun () ->
              let d = ColumnOps.deltaOf rid (orders 5) (SetCell("price", 2, Float 9.0))
              Expect.equal (rowsOf d) (Some [ ByKey "i:3", RowChanged ]) "row 2 carries key 3"

          testCase "a cell edit that writes the value already there is the empty delta"
          <| fun () ->
              let t = orders 5
              let d = ColumnOps.deltaOf rid t (SetCell("qty", 0, Int 1))
              Expect.equal d (Delta.empty rid.Scheme) "nothing moved"

          testCase "a key edit is the old key removed and the new key added"
          <| fun () ->
              let d = ColumnOps.deltaOf rid (orders 5) (SetCell("id", 0, Int 99))
              Expect.equal (rowsOf d) (Some [ ByKey "i:1", RowRemoved; ByKey "i:99", RowAdded ]) "identity moved"

          testCase "a column edit names every row whose cell moved; an append names every new row"
          <| fun () ->
              let t = orders 4
              let col = Column.create "qty" IntType [ Int 1; Int 5; Int 3; Int 4 ]
              let d = ColumnOps.deltaOf rid t (SetColumn col)
              Expect.equal (rowsOf d) (Some [ ByKey "i:2", RowChanged ]) "only row 1's qty moved"

              let d2 = ColumnOps.deltaOf rid t (AppendRows [ [ "id", Int 10 ]; [ "id", Int 11 ] ])
              Expect.equal (rowsOf d2) (Some [ ByKey "i:10", RowAdded; ByKey "i:11", RowAdded ]) "two new keys"

          testCase "FullRefresh wherever identity or the op cannot say more"
          <| fun () ->
              let t = orders 4
              Expect.equal (ColumnOps.deltaOf rid t (RemoveColumn "qty")) FullRefresh "a schema change"

              Expect.equal
                  (ColumnOps.deltaOf rid t (ApplyTransform linesPipeline))
                  FullRefresh
                  "a whole-table transform"

              Expect.equal
                  (ColumnOps.deltaOf rid t (SetCell("price", 99, Float 1.0)))
                  FullRefresh
                  "an op that does not apply"

              Expect.equal
                  (ColumnOps.deltaOf rid t (AppendRows [ [ "id", Int 1 ] ]))
                  FullRefresh
                  "an append reusing a key"

              Expect.equal
                  (ColumnOps.deltaOf rid t (AppendRows [ [ "qty", Int 1 ] ]))
                  FullRefresh
                  "an append with no key"

          testCase "the delta is true: refreshing with it equals evaluating the edited table"
          <| fun () ->
              let t = orders 50

              let ops =
                  [ SetCell("price", 7, Float 3.5)
                    SetCell("id", 3, Int 1000)
                    SetColumn(Column.create "qty" IntType [ for i in 0..49 -> Int(i % 3) ])
                    AppendRows [ [ "id", Int 500; "qty", Int 2; "price", Float 1.0 ] ] ]

              for op in ops do
                  let t' = ColumnOps.apply op t |> Result.defaultWith (fun e -> failwithf "%A" e)

                  let s0 =
                      Incremental.prime DataFrame.noResolve Map.empty rid linesPipeline t
                      |> Result.defaultWith (fun e -> failwithf "%A" e)

                  let d = ColumnOps.deltaOf rid t op

                  let s1 =
                      Incremental.refresh DataFrame.noResolve Map.empty rid linesPipeline s0 d t'
                      |> Result.defaultWith (fun e -> failwithf "%A" e)

                  Expect.equal
                      (Incremental.result s1)
                      (DataFrame.evalPipeline linesPipeline t'
                       |> Result.defaultWith (fun e -> failwithf "%A" e))
                      (sprintf "%A" op)

          testCase "a one-cell edit reaches Incremental as ONE row, where the column invalidation reaches every row"
          <| fun () ->
              let n = 1000
              let t = orders n
              let op = SetCell("price", 500, Float 7.25)
              let t' = ColumnOps.apply op t |> Result.defaultWith (fun e -> failwithf "%A" e)

              let s0 =
                  Incremental.prime DataFrame.noResolve Map.empty rid linesPipeline t
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let refreshWith d =
                  Incremental.refresh DataFrame.noResolve Map.empty rid linesPipeline s0 d t'
                  |> Result.defaultWith (fun e -> failwithf "%A" e)
                  |> Incremental.footprint
                  |> Incremental.rowsEvaluated

              Expect.equal (refreshWith (ColumnOps.deltaOf rid t op)) 1 "one row evaluated"

              let coarse = Delta.ofChange rid.Scheme (ColumnOps.changeOf op)
              Expect.isGreaterThanOrEqual (refreshWith coarse) n "the column invalidation re-evaluates every row" ]

// =============================================================================================
//  5. the sheet: evalFromWith over a tree edited by UpdateNode, sources edited by ColumnOps
// =============================================================================================

/// A table node's value: the result a node MEANS, plus the incremental state that let it be
/// computed cheaply and the source version that state is in step with. Equality is over the result
/// alone — the state is a cache, and the theorem's equality is the value type's.
[<CustomEquality; NoComparison>]
type TableSnap =
    { Result: Table
      State: IncrementalEval option
      BuiltAt: int }

    override this.Equals(o) =
        match o with
        | :? TableSnap as t -> this.Result = t.Result
        | _ -> false

    override this.GetHashCode() = hash this.Result

/// A source node's value: its table, the version it is at, and the delta from the version before.
type SourceSnap =
    { Table: Table
      Version: int
      Delta: TableDelta }

type SheetValue =
    | CellV of Cell
    | SourceV of SourceSnap
    | TableV of TableSnap

type SheetDef =
    | Root
    | Source
    | TableFormula of source: string * pipeline: Transform list * columns: string list
    | CellSum of table: string * column: string

type SheetNode =
    { Id: string
      Def: SheetDef
      Children: SheetNode list }

type Sheet =
    { Tree: SheetNode
      Sources: Map<string, Table>
      Versions: Map<string, int>
      Deltas: Map<string, TableDelta> }

let private sheetw: NodeWitness<SheetNode, string> =
    { Id = fun n -> n.Id
      KindTag =
        fun n ->
            match n.Def with
            | Root -> "root"
            | Source -> "source"
            | TableFormula _ -> "table"
            | CellSum _ -> "cell"
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

let private sheetReads (n: SheetNode) : Propagation.PartRead<string> seq =
    match n.Def with
    | Root
    | Source -> Seq.empty
    | TableFormula(src, _, cols) ->
        Seq.singleton
            { Propagation.Read = src
              Parts = Some(Set.ofList cols) }
    | CellSum(t, col) ->
        Seq.singleton
            { Propagation.Read = t
              Parts = Some(Set.singleton col) }

let private nodeReads (n: SheetNode) : string seq =
    sheetReads n |> Seq.map (fun r -> r.Read)

let private depsOf (s: Sheet) =
    Propagation.nodeDependencies (Propagation.partDependencyMap sheetw idw sheetReads s.Tree)

let private defOf (s: Sheet) (id: string) =
    Tree.tryFind sheetw idw id s.Tree |> Option.map (fun n -> n.Def)

let private sumColumn (t: Table) (col: string) : Cell =
    match Table.tryColumn col t with
    | None -> Null
    | Some c ->
        c.Cells
        |> List.sumBy (function
            | Int i -> float i
            | Float f -> f
            | _ -> 0.0)
        |> Float

let private tableOf (v: SheetValue option) =
    match v with
    | Some(SourceV s) -> Some s.Table
    | Some(TableV t) -> Some t.Result
    | _ -> None

/// The REFERENCE evaluator: what each node means, computed from scratch.
let private reference (s: Sheet) (resolve: string -> SheetValue option) (id: string) : Result<SheetValue, string> =
    match defOf s id with
    | None -> Error("no such node " + id)
    | Some Root -> Ok(CellV Null)
    | Some Source ->
        Ok(
            SourceV
                { Table = s.Sources[id]
                  Version = s.Versions[id]
                  Delta = s.Deltas[id] }
        )
    | Some(TableFormula(src, pipeline, _)) ->
        match resolve src with
        | Some(SourceV snap) ->
            DataFrame.evalPipeline pipeline snap.Table
            |> Result.map (fun t ->
                TableV
                    { Result = t
                      State = None
                      BuiltAt = snap.Version })
            |> Result.mapError DataFrame.errorString
        | _ -> Ok(CellV Null)
    | Some(CellSum(t, col)) ->
        match tableOf (resolve t) with
        | Some tbl -> Ok(CellV(sumColumn tbl col))
        | None -> Ok(CellV Null)

/// The PRIOR-AWARE evaluator: a table node refreshes its prior state against its source's delta
/// when that state is one version behind (or at) the source, and primes otherwise. Everything it
/// needs is an argument — the prior from the driver, the delta from the source's value.
let withPrior
    (s: Sheet)
    (resolve: string -> SheetValue option)
    (prior: SheetValue option)
    (id: string)
    : Result<SheetValue, string> =
    match defOf s id with
    | Some(TableFormula(src, pipeline, _)) ->
        match resolve src with
        | Some(SourceV snap) ->
            let result =
                match prior with
                | Some(TableV { State = Some st; BuiltAt = at }) when at = snap.Version - 1 ->
                    Incremental.refresh DataFrame.noResolve Map.empty rid pipeline st snap.Delta snap.Table
                | Some(TableV { State = Some st; BuiltAt = at }) when at = snap.Version ->
                    Incremental.refresh
                        DataFrame.noResolve
                        Map.empty
                        rid
                        pipeline
                        st
                        (Delta.empty rid.Scheme)
                        snap.Table
                | _ -> Incremental.prime DataFrame.noResolve Map.empty rid pipeline snap.Table

            result
            |> Result.map (fun st ->
                TableV
                    { Result = Incremental.result st
                      State = Some st
                      BuiltAt = snap.Version })
            |> Result.mapError DataFrame.errorString
        | _ -> Ok(CellV Null)
    | _ -> reference s resolve id

let private initialSheet (rows: int) : Sheet =
    let node id def = { Id = id; Def = def; Children = [] }

    { Tree =
        { Id = "root"
          Def = Root
          Children =
            [ node "orders" Source
              node "lines" (TableFormula("orders", linesPipeline, [ "qty"; "price" ]))
              node "total" (CellSum("lines", "amount"))
              node "idSum" (CellSum("orders", "id")) ] }
      Sources = Map.ofList [ "orders", orders rows ]
      Versions = Map.ofList [ "orders", 0 ]
      Deltas = Map.ofList [ "orders", Delta.empty rid.Scheme ] }

/// A data edit through `ColumnOps`: the source moves one version, and its delta is `deltaOf`.
let private dataEdit (op: ColumnOp) (s: Sheet) : Sheet * Set<string> =
    let t = s.Sources["orders"]

    match ColumnOps.apply op t with
    | Error _ -> s, Set.empty
    | Ok t' ->
        { s with
            Sources = Map.add "orders" t' s.Sources
            Versions = Map.add "orders" (s.Versions["orders"] + 1) s.Versions
            Deltas = Map.add "orders" (ColumnOps.deltaOf rid t op) s.Deltas },
        Set.singleton "orders"

/// A structural edit through `Ops`, its change set from `changedForOp`.
let private sheetEdit (op: SkeletonOp<SheetNode, string>) (s: Sheet) : Sheet * Set<string> =
    match Ops.apply sheetw idw op s.Tree with
    | Error _ -> s, Set.empty
    | Ok tree' -> { s with Tree = tree' }, Propagation.changedForOp sheetw idw nodeReads s.Tree tree' op

let private redefineLines (factor: float) =
    UpdateNode
        { Id = "lines"
          Def =
            TableFormula(
                "orders",
                [ Derive("amount", Binary(Mul, Binary(Mul, Cast(FloatType, Col "qty"), Col "price"), Lit(Float factor))) ],
                [ "qty"; "price" ]
            )
          Children = [] }

let evaluatorWitness: EvaluatorWitness<Sheet, SheetValue> =
    { Surface = "the Phase 250 composition sheet"
      Model =
        fun r ->
            let n, r1 = ConfRng.intBelow 8 r
            initialSheet (3 + n), r1
      Deps = depsOf
      EvalNode = reference
      Change =
        fun s r ->
            let rows = Table.rowCount s.Sources["orders"]
            let kind, r1 = ConfRng.intBelow 8 r
            let row, r2 = ConfRng.intBelow rows r1
            let v, r3 = ConfRng.intBelow 40 r2

            let edited =
                match kind with
                | 0 -> dataEdit (SetCell("price", row, Float(0.25 * float (v + 1)))) s
                | 1 -> dataEdit (SetCell("qty", row, Int(v + 1))) s
                | 2 -> dataEdit (SetCell("id", row, Int(1000 + v))) s
                | 3 -> dataEdit (AppendRows [ [ "id", Int(2000 + v); "qty", Int 1; "price", Float 1.0 ] ]) s
                | 4 ->
                    dataEdit (SetColumn(Column.create "qty" IntType [ for i in 0 .. rows - 1 -> Int((i + v) % 5) ])) s
                | 5 -> sheetEdit (redefineLines (float (v % 3 + 1))) s
                // A redefinition the pipeline evaluator refuses (a column the source does not have),
                // so the failing-evaluator arm of the agreement law is reached.
                | 6 ->
                    sheetEdit
                        (UpdateNode
                            { Id = "lines"
                              Def = TableFormula("orders", [ Derive("amount", Col "missing") ], [ "missing" ])
                              Children = [] })
                        s
                | _ -> sheetEdit (Batch [ RemoveNode "idSum" ]) s

            edited, r3 }

let private sheetTests =
    testList
        "the composed sheet"
        [ testCase "a one-cell edit refreshes the table node with ONE row, carried by evalFromWith's prior"
          <| fun () ->
              let s0 = initialSheet 1000

              let full0 =
                  Propagation.evalWith (withPrior s0) (depsOf s0)
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let s1, changed = dataEdit (SetCell("price", 500, Float 7.25)) s0

              match Propagation.evalFromWith (withPrior s1) full0.Values changed (depsOf s1) with
              | Ok o ->
                  match o.Values["lines"] with
                  | TableV { State = Some st } ->
                      Expect.equal (Incremental.footprint st).Recompute (RowsRecomputed 1) "one row"
                  | other -> failtestf "lines is not a refreshed table: %A" other

                  Expect.equal
                      (Ok o)
                      (Propagation.eval (reference s1) (depsOf s1))
                      "and the refresh equals a full reference evaluation"
              | Error e -> failtestf "%A" e

          testCase "a price edit reaches, at column granularity, every node but the one reading ids"
          <| fun () ->
              let s0 = initialSheet 10
              let op = SetCell("price", 3, Float 1.5)
              let pdeps = Propagation.partDependencyMap sheetw idw sheetReads s0.Tree

              let dirty =
                  Propagation.dirtyFromChangedParts
                      pdeps
                      (fun _ -> ColumnOps.changedColumns op)
                      (Set.singleton "orders")

              Expect.equal dirty (Set.ofList [ "orders"; "lines"; "total" ]) "idSum reads only id"

          testCase "a formula redefinition is one UpdateNode, and dirties one node plus its readers"
          <| fun () ->
              let s0 = initialSheet 10
              let s1, changed = sheetEdit (redefineLines 2.0) s0
              Expect.equal changed (Set.ofList [ "lines"; "total" ]) "lines and the node that reads it"

              let dirtyByOp = Propagation.touchedBy sheetw idw s0.Tree (redefineLines 2.0)

              Expect.equal dirtyByOp (Set.singleton "lines") "touched: the node alone, not the root"

              let prior =
                  Propagation.evalWith (withPrior s0) (depsOf s0)
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              Expect.equal
                  (Propagation.evalFromWith (withPrior s1) prior.Values changed (depsOf s1))
                  (Propagation.evalWith (withPrior s1) (depsOf s1))
                  "and the refresh agrees"

          testCase "the sheet's prior-aware evaluator is certified by propagationEvaluatorLawsWith"
          <| fun () ->
              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness withPrior 2500 120

              match results |> List.filter (fun r -> not r.Passed) with
              | [] -> ()
              | failed ->
                  failed
                  |> List.map (fun r -> r.Law + ": " + defaultArg r.Counterexample "")
                  |> String.concat "\n"
                  |> failtestf "the sheet failed propagationEvaluatorLawsWith:\n%s"

          testCase "the family has teeth: an evaluator that trusts a stale prior fails the prior discipline"
          <| fun () ->
              // Returns the prior's table unconditionally when handed one: fast, and wrong after an edit.
              let trusting (s: Sheet) resolve (prior: SheetValue option) id =
                  match prior, defOf s id with
                  | Some(TableV t), Some(TableFormula _) -> Ok(TableV t)
                  | _ -> withPrior s resolve prior id

              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness trusting 2500 120

              let law = results |> List.find (fun r -> r.Law.Contains "prior discipline")

              Expect.isFalse law.Passed "the stale prior is caught"

          testCase "the family has teeth: an evaluator whose prior-blind reading is not the reference fails"
          <| fun () ->
              let drifting (s: Sheet) resolve (prior: SheetValue option) id =
                  match prior, defOf s id with
                  | None, Some(CellSum _) -> Ok(CellV(Float -1.0))
                  | _ -> withPrior s resolve prior id

              let results =
                  Conformance.propagationEvaluatorLawsWith evaluatorWitness drifting 2500 40

              let law = results |> List.find (fun r -> r.Law.Contains "prior-blind reading")
              Expect.isFalse law.Passed "the drift is caught" ]

[<Tests>]
let tests = testList "PropagationComposition" [ partTests; deltaTests; sheetTests ]
