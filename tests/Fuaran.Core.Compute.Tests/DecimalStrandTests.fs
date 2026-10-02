module Fuaran.Compute.Tests.DecimalStrandTests

// ---------------------------------------------------------------------------
// Phase 321 — the decimal across the strand beyond the evaluator.
//
// Phase 277 carried the decimal through the transform evaluator; this file holds the rest of the
// strand to it: the float order the evaluator compares in (one order, the substrate's, on every
// host), the column op algebra and its wire, the delta seam and the incremental refresh over a
// decimal key, a derived column's type, and every public entry point walked with a decimal
// column. The FS0025 escalation in `Directory.Build.props` is the compile-time half: an arm blind
// to `Decimal` no longer builds; these are the run-time half.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failwithf "not decimal text: %s" text

let private table (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, t, _) -> n, t)
      Columns = cols |> List.map (fun (n, t, cells) -> Column.create n t cells) }

let private cellsOf (name: string) (t: Table) : Cell list =
    match Table.tryColumn name t with
    | Some c -> c.Cells
    | None -> failtestf "no column %s" name

let private typeOfCol (name: string) (t: Table) : ColumnType =
    match Table.tryColumn name t with
    | Some c -> c.Type
    | None -> failtestf "no column %s" name

/// Cells compared by token, so a NaN is equal to itself and `-0.0` reads as `0`.
let private tokens (cells: Cell list) : string list = cells |> List.map DataFrame.cellToken

// ---- the float order --------------------------------------------------------------------------

let private floats =
    table [ "f", FloatType, [ Float 2.0; Float nan; Float 1.0; Null; Float -0.5; Float infinity ] ]

[<Tests>]
let floatOrderTests =
    testList
        "DecimalStrand.floatOrder"
        [ testCase "a sort puts NaN above every value and below the nulls, in both directions"
          <| fun _ ->
              let asc = ok (DataFrame.evalPipeline [ Transform.sortBy [ "f", Asc ] ] floats)
              let desc = ok (DataFrame.evalPipeline [ Transform.sortBy [ "f", Desc ] ] floats)

              Expect.equal
                  (tokens (cellsOf "f" asc))
                  [ "f:-0.5"; "f:1"; "f:2"; "f:Inf"; "f:NaN"; "n:" ]
                  "ascending: NaN after +Inf, the null last"

              Expect.equal
                  (tokens (cellsOf "f" desc))
                  [ "f:NaN"; "f:Inf"; "f:2"; "f:1"; "f:-0.5"; "n:" ]
                  "descending: NaN first, the null still last"

          testCase "a filter compares a NaN as the largest value, through the kernel and the reference arm alike"
          <| fun _ ->
              let over (e: ColExpr) =
                  tokens (cellsOf "f" (ok (DataFrame.evalPipeline [ Filter e ] floats)))

              // A literal on the right is the comparison kernel; a column against a column is the
              // compiled expression's own arm. Both must read the one order.
              Expect.equal (over (Binary(Gt, Col "f", Lit(Float 1.5)))) [ "f:2"; "f:NaN"; "f:Inf" ] "f > 1.5"
              Expect.equal (over (Binary(Lt, Col "f", Lit(Float 1.5)))) [ "f:1"; "f:-0.5" ] "f < 1.5"

              let twin =
                  table
                      [ "f", FloatType, [ Float nan; Float 1.0; Float nan ]
                        "g", FloatType, [ Float 1.0; Float nan; Float nan ] ]

              let gt = ok (DataFrame.evalPipeline [ Filter(Binary(Gt, Col "f", Col "g")) ] twin)

              Expect.equal (tokens (cellsOf "f" gt)) [ "f:NaN" ] "NaN > 1, not 1 > NaN, not NaN > NaN"

          testCase "the sort's first value is the aggregate's Max, NaN included"
          <| fun _ ->
              let desc = ok (DataFrame.evalPipeline [ Transform.sortBy [ "f", Desc ] ] floats)
              let col = Column.create "f" FloatType (cellsOf "f" floats)

              Expect.equal
                  (DataFrame.cellToken (List.head (cellsOf "f" desc)))
                  (DataFrame.cellToken (ok (Column.aggregate Max col)))
                  "one order: the sort and the substrate's Max agree about where a NaN sits" ]

// ---- the column op algebra --------------------------------------------------------------------

let private ledger =
    table
        [ "id", StringType, [ Str "a"; Str "b" ]
          "amount", DecimalType, [ dec "1.5"; dec "-0.25" ]
          "rate", FloatType, [ Float 0.5; Float 1.5 ]
          "qty", IntType, [ Int 1; Int 2 ] ]

let private rejected (op: ColumnOp) : ColumnRejection =
    match ColumnOps.apply op ledger with
    | Error e -> e
    | Ok _ -> failtestf "expected %A to be refused" op

[<Tests>]
let columnOpsTests =
    testList
        "DecimalStrand.columnOps"
        [ testCase "a decimal cell in a decimal column, through every op that writes one"
          <| fun _ ->
              let after =
                  ok (
                      ColumnOps.applyAll
                          [ SetCell("amount", 0, dec "9.99")
                            SetColumn(Column.create "amount" DecimalType [ dec "0.1"; dec "0.2" ])
                            InsertColumn(4, Column.create "fee" DecimalType [ dec "0.05"; Null ])
                            AppendRows(
                                [ [ "id", Str "c"
                                    "amount", dec "100"
                                    "rate", Float 2.0
                                    "qty", Int 3
                                    "fee", dec "0.01" ] ]
                            ) ]
                          ledger
                  )

              Expect.equal (cellsOf "amount" after) [ dec "0.1"; dec "0.2"; dec "100" ] "the amounts"
              Expect.equal (cellsOf "fee" after) [ dec "0.05"; Null; dec "0.01" ] "the fees"
              Expect.equal (Table.validate after) (Ok()) "and the substrate's validator accepts the table"

          testCase "cellFits widens exactly as ColumnType.widens does, and stores the cell as given"
          <| fun _ ->
              // The ruling (DECISIONS.md D2): an int fits a decimal column and a float column, as
              // the substrate's validator and codec already say; nothing else widens.
              let intInDecimal = ok (ColumnOps.apply (SetCell("amount", 1, Int 7)) ledger)
              Expect.equal (cellsOf "amount" intInDecimal) [ dec "1.5"; Int 7 ] "stored verbatim, not converted"
              Expect.equal (Table.validate intInDecimal) (Ok()) "a table Core's validator accepts"

              let intInFloat = ok (ColumnOps.apply (SetCell("rate", 0, Int 3)) ledger)
              Expect.equal (cellsOf "rate" intInFloat) [ Int 3; Float 1.5 ] "an int in a float column"

              for op, expected in
                  [ SetCell("amount", 0, Float 1.5), CellTypeMismatch("amount", "decimal", "float")
                    SetCell("rate", 0, dec "1.5"), CellTypeMismatch("rate", "float", "decimal")
                    SetCell("qty", 0, dec "1"), CellTypeMismatch("qty", "int", "decimal")
                    SetCell("qty", 0, Float 1.0), CellTypeMismatch("qty", "int", "float") ] do
                  Expect.equal (rejected op) expected (sprintf "%A is refused by name" op)

          testCase "invert restores the previous cell verbatim under the widened rule"
          <| fun _ ->
              for op in
                  [ SetCell("amount", 0, Int 7)
                    SetCell("amount", 1, dec "3.125")
                    SetCell("rate", 1, Int 4)
                    SetColumn(Column.create "amount" DecimalType [ Int 1; dec "2.5" ]) ] do
                  let inverse = ok (ColumnOps.invert op ledger)
                  let after = ok (ColumnOps.apply op ledger)
                  Expect.equal (ok (ColumnOps.apply inverse after)) ledger (sprintf "%A round-trips" op)

          testCase "the columnar wire carries the canonical decimal text and round-trips byte for byte"
          <| fun _ ->
              for op in
                  [ SetCell("amount", 0, dec "1234567890.0001")
                    SetCell("amount", 1, Int 7)
                    SetColumn(Column.create "amount" DecimalType [ dec "-0.5"; Null ])
                    AppendRows([ [ "id", Str "c"; "amount", dec "0.001" ] ]) ] do
                  let wire = ColumnOps.encode op
                  let back = ok (ColumnOps.decode wire)
                  Expect.equal back op (sprintf "%s decodes to the op" wire)
                  Expect.equal (ColumnOps.encode back) wire "and re-encodes to the same bytes"

              // A cell built by hand with a trailing zero encodes canonically, as the decoder reads.
              let wire = ColumnOps.encode (SetCell("amount", 0, Decimal "1.50"))
              Expect.stringContains wire "\"1.5\"" "the canonical text on the wire"
              Expect.isFalse (wire.Contains "1.50") "never the non-canonical spelling"
              Expect.equal (ok (ColumnOps.decode wire)) (SetCell("amount", 0, dec "1.5")) "read back canonical" ]

// ---- the delta seam and the incremental refresh -----------------------------------------------

let private byAmount = RowIdentity.byColumn "k"

let private keyed (amounts: Cell list) (vals: Cell list) : Table =
    table
        [ "k", DecimalType, [ dec "0.1"; dec "2"; dec "3.25" ]
          "v", DecimalType, amounts @ vals ]

let private orders (amounts: string list) : Table =
    let n = List.length amounts

    table
        [ "id", DecimalType, [ for i in 0 .. n - 1 -> dec (string i + ".5") ]
          "grp", DecimalType, [ for i in 0 .. n - 1 -> dec (if i % 2 = 0 then "0.1" else "0.25") ]
          "amt", DecimalType, amounts |> List.map dec ]

let private sumByGroup: Transform list =
    [ Filter(IsNull(Col "amt") |> Not)
      GroupBy(
          [ "grp" ],
          [ { Name = "total"; Fn = Sum; Of = "amt" }
            { Name = "n"; Fn = Count; Of = "amt" } ]
      ) ]

[<Tests>]
let deltaTests =
    testList
        "DecimalStrand.delta"
        [ testCase "a decimal identity renders canonically on the delta and its wire"
          <| fun _ ->
              let before = keyed [] [ dec "1"; dec "2"; dec "3" ]
              let after = keyed [] [ dec "1"; dec "2.01"; dec "3" ]
              let d = ok (Delta.diff byAmount before after)

              Expect.equal (Delta.rowsWith RowChanged d) [ ByKey "m:2" ] "the decimal key's canonical token"

              let wire = DeltaCodec.encode d
              Expect.equal (ok (DeltaCodec.decode wire)) d "the delta round-trips its wire"
              Expect.equal (DeltaCodec.encode (ok (DeltaCodec.decode wire))) wire "byte for byte"

              // The op-induced delta is the diffed one, and the op names its column.
              let op = SetCell("v", 1, dec "2.01")
              Expect.equal (ColumnOps.deltaOf byAmount before op) d "deltaOf a decimal edit = the diff"
              Expect.equal (ColumnOps.changedColumns op) (Some(Set.singleton "v")) "the edited column"

          testCase "a decimal re-spelt, or an int replaced by the decimal of its value, is an edit"
          <| fun _ ->
              // Content is compared by what the source holds (Phase 323): an int cell and a decimal
              // cell of the same value are two cells, so a delta reports the move.
              let before = keyed [] [ Int 1; dec "2"; dec "3" ]
              let after = keyed [] [ dec "1"; dec "2"; dec "3" ]
              Expect.equal (Delta.rowsWith RowChanged (ok (Delta.diff byAmount before after))) [ ByKey "m:0.1" ] "seen"

          testCase
              "a refresh over a decimal GroupBy sum keyed by a decimal identity returns the full evaluation's digits"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"
              let before = orders [ "0.1"; "0.2"; "0.1"; "0.2"; "1000000000.01" ]
              let after = orders [ "0.1"; "0.2"; "0.3"; "0.2"; "1000000000.01" ]
              let state = ok (Incremental.primeOn idw sumByGroup before)
              let d = ok (Delta.diff idw before after)
              let refreshed = ok (Incremental.refreshOn idw sumByGroup state d after)
              let full = ok (DataFrame.evalPipeline sumByGroup after)

              Expect.equal (Incremental.result refreshed) full "the refresh is the reference"
              Expect.equal (cellsOf "total" full) [ dec "1000000000.41"; dec "0.4" ] "exact digits, no float"

              match (Incremental.footprint refreshed).Recompute with
              | FullRecompute _ -> failtest "expected a restricted refresh over the decimal key"
              | _ -> () ]

// ---- a derived column's type ------------------------------------------------------------------

let private mixed (thenCell: Cell) : Transform list =
    [ Derive("x", Case([ Binary(Gt, Col "i", Lit(Int 1)), Lit thenCell ], Col "i")) ]

let private ints = table [ "i", IntType, [ Int 1; Int 2; Int 3 ] ]

[<Tests>]
let derivedTypeTests =
    testList
        "DecimalStrand.derivedType"
        [ testCase "an int beside a float types the column float, and its Sum is a float sum"
          <| fun _ ->
              let t = ok (DataFrame.evalPipeline (mixed (Float 2.5)) ints)
              Expect.equal (typeOfCol "x" t) FloatType "Int ⊔ Float = Float (the first cell is an int)"
              Expect.equal (cellsOf "x" t) [ Int 1; Float 2.5; Float 2.5 ] "the cells as derived"
              Expect.equal (Table.validate t) (Ok()) "a table the substrate's validator accepts"

              let summed =
                  ok (
                      DataFrame.evalPipeline
                          (mixed (Float 2.5) @ [ GroupBy([], [ { Name = "s"; Fn = Sum; Of = "x" } ]) ])
                          ints
                  )

              Expect.equal (cellsOf "s" summed) [ Float 6.0 ] "summed, not refused as a float in an int column"

          testCase "an int beside a decimal types the column decimal, and its Sum is exact"
          <| fun _ ->
              let t = ok (DataFrame.evalPipeline (mixed (dec "0.1")) ints)
              Expect.equal (typeOfCol "x" t) DecimalType "Int ⊔ Decimal = Decimal"
              Expect.equal (Table.validate t) (Ok()) "valid"

              let summed =
                  ok (
                      DataFrame.evalPipeline
                          (mixed (dec "0.1") @ [ GroupBy([], [ { Name = "s"; Fn = Sum; Of = "x" } ]) ])
                          ints
                  )

              Expect.equal (cellsOf "s" summed) [ dec "1.2" ] "1 + 0.1 + 0.1, exactly"

          testCase "a float beside a decimal is refused by name, never widened (Phase 338, carried from 321)"
          <| fun _ ->
              let floatsFirst = table [ "i", IntType, [ Int 2; Int 1 ] ]
              let empty = table [ "i", IntType, [] ]

              let mix =
                  Derive("x", Case([ Binary(Gt, Col "i", Lit(Int 1)), Lit(Float 0.5) ], Lit(dec "0.5")))

              // The arms carry both families: refused statically, over an empty frame too.
              for t in [ floatsFirst; empty ] do
                  match DataFrame.evalPipeline [ mix ] t with
                  | Error(TypeError msg) ->
                      Expect.stringContains msg "derived column 'x' joins a float and a decimal" "named"
                  | other -> failtestf "expected the static refusal, got %A" other

              // Only the cells show it: refused where they hold both, typed where they hold one.
              let byParams =
                  Derive("x", Case([ Binary(Gt, Col "i", Lit(Int 1)), Param "f" ], Param "d"))

              let env = Map.ofList [ "f", Float 0.5; "d", dec "0.5" ]

              match DataFrame.evalPipelineInEnv env [ byParams ] floatsFirst with
              | Error(TypeError msg) -> Expect.stringContains msg "joins a float and a decimal" "named"
              | other -> failtestf "expected the cells' refusal, got %A" other

              let onlyFloats = table [ "i", IntType, [ Int 2; Int 3 ] ]
              let t = ok (DataFrame.evalPipelineInEnv env [ byParams ] onlyFloats)
              Expect.equal (typeOfCol "x" t) FloatType "one family present: the cells type it"

              // And the verdict declines both shapes.
              Expect.isFalse (Plan.isTotal [ "i", IntType ] mix) "the static mix is not total"

              Expect.isFalse
                  (Plan.isTotal
                      [ "i", IntType ]
                      (Derive("x", Case([ Binary(Gt, Col "i", Lit(Int 1)), Lit(Float 0.5) ], Lit(Int 1)))))
                  "a cells-typed derive is not total"

          testCase "the incremental walk types a derived column by the same join"
          <| fun _ ->
              let src =
                  table
                      [ "id", StringType, [ Str "a"; Str "b"; Str "c" ]
                        "i", IntType, [ Int 1; Int 2; Int 3 ] ]

              let edited =
                  table
                      [ "id", StringType, [ Str "a"; Str "b"; Str "c" ]
                        "i", IntType, [ Int 1; Int 0; Int 3 ] ]

              for p in [ mixed (Float 2.5); mixed (dec "0.1") ] do
                  let idw = RowIdentity.byColumn "id"
                  let state = ok (Incremental.primeOn idw p src)

                  let refreshed =
                      ok (Incremental.refreshOn idw p state (ok (Delta.diff idw src edited)) edited)

                  Expect.equal (Incremental.result refreshed) (ok (DataFrame.evalPipeline p edited)) "refresh = full"

          testCase "the static typer stays exact: an int beside a float is not typed float (DECISIONS.md D3)"
          <| fun _ ->
              // `Of FloatType` would let `x + x` read as total while both are ints that can overflow.
              let e = Case([ Binary(Gt, Col "i", Lit(Int 1)), Lit(Float 2.5) ], Col "i")
              Expect.equal (DataFrame.typeOf [ "i", IntType ] e) None "undecided, not widened" ]

// ---- every public entry point, walked with a decimal column -----------------------------------

let private wide =
    table
        [ "id", StringType, [ Str "a"; Str "b"; Str "c"; Str "d" ]
          "g", DecimalType, [ dec "1.5"; dec "2"; dec "1.5"; Null ]
          "m", DecimalType, [ dec "0.1"; dec "-2.25"; Int 3; dec "1000000.001" ]
          "i", IntType, [ Int 1; Int 2; Int 3; Int 4 ] ]

let private everyStep: Transform list list =
    let aggs =
        [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]
        |> List.mapi (fun j fn ->
            { Name = sprintf "a%d" j
              Fn = fn
              Of = "m" })

    let windows =
        [ RowNumber
          Rank
          Lag
          Lead
          CumulSum
          RollingMean
          DenseRank
          CompetitionRank
          NTile 2
          CumulMax
          CumulMin
          RollingSum ]
        |> List.map (fun fn ->
            Window
                { PartitionBy = [ "g" ]
                  OrderBy = [ "m", Asc ]
                  Fn = fn
                  Of = "m"
                  As = "w" })

    [ [ Filter(Binary(Gt, Col "m", Lit(dec "0.05"))) ]
      [ Filter(Binary(Eq, Col "g", Col "m")) ]
      [ Derive("d", Binary(Add, Col "m", Col "i")) ]
      [ Derive("d", Coalesce [ Col "m"; Lit(Int 0) ]) ]
      [ Project [ "m", "money" ] ]
      [ GroupBy([ "g" ], aggs) ]
      [ GroupBy([ "m" ], [ { Name = "n"; Fn = Count; Of = "i" } ]) ]
      [ Join(Embedded wide, [ "m", "m" ], Inner) ]
      [ Join(Embedded wide, [ "g", "g" ], Left) ]
      [ Join(Embedded wide, [ "m", "m" ], Anti) ]
      [ Pivot
            { Index = [ "id" ]
              On = "g"
              Values = "m"
              Agg = Sum } ]
      [ Unpivot([ "id" ], [ "m"; "g" ]) ]
      [ Transform.sortBy [ "m", Desc; "g", Asc ] ]
      [ Distinct ]
      [ Transform.limit 2 1 ]
      [ Union(Embedded wide) ]
      [ Intersect(Embedded wide) ]
      [ Except(Embedded wide) ] ]
    @ (windows |> List.map List.singleton)

[<Tests>]
let entryPointTests =
    testList
        "DecimalStrand.entryPoints"
        [ testCase "no step, no refresh, no diff and no codec meets a decimal column with a MatchFailureException"
          <| fun _ ->
              let edited = ok (ColumnOps.apply (SetCell("m", 1, dec "7.77")) wide)
              let idw = RowIdentity.byColumn "id"
              let delta = ok (Delta.diff idw wide edited)

              for p in everyStep do
                  // Each answers — a table or a named error, never an escaped exception.
                  let full =
                      try
                          DataFrame.evalPipeline p wide
                      with e ->
                          failtestf "%A threw %s" p (e.GetType().Name)

                  match Incremental.primeOn idw p wide with
                  | Error _ -> ()
                  | Ok state ->
                      match Incremental.refreshOn idw p state delta edited with
                      | Ok refreshed ->
                          Expect.equal
                              (Ok(Incremental.result refreshed))
                              (DataFrame.evalPipeline p edited)
                              (sprintf "%A: the refresh is the reference" p)
                      | Error _ -> ()

                  ignore full

              // The codecs: the table, the columnar ops over it, and the delta.
              Expect.equal
                  (ColumnOps.toOps wide edited
                   |> List.map (ColumnOps.encode >> ColumnOps.decode >> ok))
                  (ColumnOps.toOps wide edited)
                  "the diff script round-trips"

              Expect.equal (ok (DeltaCodec.decode (DeltaCodec.encode delta))) delta "the delta round-trips" ]
