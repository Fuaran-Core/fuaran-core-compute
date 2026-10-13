module Fuaran.Compute.Tests.DeriveTypingTests

// Phase 338 — a derived column is typed by its expression, not by its first cell. The law family
// (`DeriveTypingConformance.laws`) is held green here and shown to go red against the rule it
// replaced; the rope-level typing of the chunked path is pinned over more than one chunk, where a
// chunk alone cannot see the column's type. Phase 413 — an evaluation decides a derive its params
// decide, and the incremental seam makes the same decision as the full evaluator.

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

let private ok r =
    match r with
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got %A" e

let private typeOf (name: string) (t: Table) : ColumnType option =
    t.Schema |> List.tryFind (fun f -> f.Name = name) |> Option.map _.Type

let private failures (rs: LawResult list) =
    rs |> List.filter (fun r -> not r.Passed) |> List.map _.Law

/// The rule this phase replaced, restored over an evaluator's answer: a derived column (`d`,
/// `value`, `x`) typed by its FIRST present cell, `StringType` when there is none.
let private firstCellRule
    (eval: Map<string, Cell> -> Transform list -> Table -> Result<Table, EvalError>)
    : Map<string, Cell> -> Transform list -> Table -> Result<Table, EvalError> =
    fun env pipeline t ->
        eval env pipeline t
        |> Result.map (fun out ->
            let retype (f: Field) =
                let name = f.Name

                if List.contains name [ "d"; "value"; "x" ] then
                    let cells =
                        out.Columns
                        |> List.find (fun c -> c.Name = name)
                        |> (fun c -> (Column.toCells c))

                    Field.create name (cells |> List.tryPick Cell.typeOf |> Option.defaultValue StringType)
                else
                    f

            let schema = out.Schema |> List.map retype

            { Schema = schema
              Columns =
                out.Columns
                |> List.map (fun c ->
                    let ty = schema |> List.find (fun f -> f.Name = c.Name) |> _.Type
                    KitColumn.create c.Name ty (Column.toCells c)) })

/// A perturbation the cells-admitted law exists for: every derived column declared `int`.
let private allInt
    (eval: Map<string, Cell> -> Transform list -> Table -> Result<Table, EvalError>)
    : Map<string, Cell> -> Transform list -> Table -> Result<Table, EvalError> =
    fun env pipeline t ->
        eval env pipeline t
        // The schema entry is retyped and the column left as the evaluator built it (Phase 423): a
        // column that disagrees with its schema entry wholly is the one disagreement a Core `1.0.0`
        // table still holds, and it is what the admission law must catch.
        |> Result.map (fun out ->
            { out with
                Schema =
                    out.Schema
                    |> List.map (fun f -> if f.Name = "d" then Field.create "d" IntType else f) })

let private rows (n: int) (cell: int -> Cell) : Cell list = [ for i in 0 .. n - 1 -> cell i ]

[<Tests>]
let tests =
    testList
        "DeriveTyping"
        [ testCase "the law family is green, and the same seed gives the same report"
          <| fun _ ->
              let results = DeriveTypingConformance.laws 3380 200
              Expect.isEmpty (failures results) "derive-typing laws"
              Expect.equal (DeriveTypingConformance.laws 3380 200) results "same seed => identical report"

          testCase "the family goes red against the first-present-cell rule restored"
          <| fun _ ->
              let red =
                  failures (DeriveTypingConformance.lawsWith (firstCellRule DataFrame.evalPipelineInEnv) 3380 200)

              Expect.contains
                  red
                  "a decided derive or unpivot types its column identically over a full, an empty and an all-null frame"
                  "the empty and all-null frames type it String"

              Expect.contains
                  red
                  "a derive the evaluation's params decide types its column identically over a full, an empty and an all-null frame"
                  "an empty frame types a param-decided column String under the restored rule"

              Expect.contains
                  red
                  "prime, refresh and the full evaluation type a derived column identically"
                  "the seam reads the rule, the restored evaluator does not"

          testCase "the cells-admitted law goes red against a column declared a type its cells are not"
          <| fun _ ->
              let red =
                  failures (DeriveTypingConformance.lawsWith (allInt DataFrame.evalPipelineInEnv) 3380 200)

              Expect.contains red "every present derived cell is of its column's type or widens into it" "admission"

              Expect.contains
                  red
                  "SchemaWalk states exactly the type the evaluator gives a decided derive"
                  "and the walk disagrees with it"

          testCase "an empty and an all-null frame keep a decided derive's type"
          <| fun _ ->
              let t: Table =
                  { Schema = [ Field.create "i" IntType; Field.create "m" DecimalType ]
                    Columns =
                      [ KitColumn.create "i" IntType [ Null; Null ]
                        KitColumn.create "m" DecimalType [ Null; Null ] ] }

              for e, ty in
                  [ Binary(Add, Col "i", Lit(Int 1)), IntType
                    Binary(Div, Col "i", Lit(Int 2)), FloatType
                    Binary(Mul, Col "m", Col "i"), DecimalType
                    Binary(Gt, Col "i", Lit(Int 0)), BoolType
                    Lit Null, StringType ] do
                  for pipeline in [ [ Derive("d", e) ]; [ Filter(Lit(Bool false)); Derive("d", e) ] ] do
                      Expect.equal
                          (typeOf "d" (ok (DataFrame.evalPipeline pipeline t)))
                          (Some ty)
                          (sprintf "%A" pipeline)

          testCase "an unpivot's value column is the widening join of its value columns' declared types"
          <| fun _ ->
              let t: Table =
                  { Schema =
                      [ Field.create "k" StringType
                        Field.create "i" IntType
                        Field.create "f" FloatType
                        Field.create "m" DecimalType
                        Field.create "s" StringType ]
                    Columns =
                      [ KitColumn.create "k" StringType [ Str "a" ]
                        KitColumn.create "i" IntType [ Int 1 ]
                        KitColumn.create "f" FloatType [ Float 0.5 ]
                        KitColumn.create "m" DecimalType [ Cell.decimal "1.5" |> Option.get ]
                        KitColumn.create "s" StringType [ Str "x" ] ] }

              let value values pipeline =
                  DataFrame.evalPipeline (pipeline @ [ Unpivot([ "k" ], values) ]) t
                  |> Result.map (typeOf "value")

              for values, expected in
                  [ [ "i"; "f" ], Some FloatType
                    [ "m"; "i" ], Some DecimalType
                    [ "i" ], Some IntType
                    [], Some StringType ] do
                  Expect.equal (value values []) (Ok expected) (sprintf "%A" values)
                  Expect.equal (value values [ Filter(Lit(Bool false)) ]) (Ok expected) (sprintf "%A, empty" values)

                  Expect.equal
                      (SchemaWalk.typeOf "value" (SchemaWalk.ofPipeline t.Schema [ Unpivot([ "k" ], values) ]))
                      expected
                      (sprintf "%A, walked" values)

              // A string beside an int: no widening relates them, so the cells decide — and since
              // Core `1.0.0` no column holds the two together, so a value column that would is
              // refused by name (Phase 423, operator ruling 2026-10-10, `DECISIONS.md` D18). Phase 321
              // typed it by the earlier cell and carried the other as it was; a typed vector cannot.
              // With no row there is no cell beside another, and the empty column is a string.
              Expect.equal
                  (value [ "s"; "i" ] [])
                  (Error(
                      TypeError
                          "derived column 'value' holds a string beside a int, which no column type holds together: cast one to the other's type first"
                  ))
                  "refused by name"

              Expect.equal (value [ "i"; "s" ] [ Filter(Lit(Bool false)) ]) (Ok(Some StringType)) "no cell: string"

              Expect.equal
                  (SchemaWalk.typeOf "value" (SchemaWalk.ofPipeline t.Schema [ Unpivot([ "k" ], [ "s"; "i" ]) ]))
                  None
                  "the walk does not state a type the cells decide"

              // A float beside a decimal is refused by name.
              match DataFrame.evalPipeline [ Unpivot([ "k" ], [ "f"; "m" ]) ] t with
              | Error(TypeError msg) -> Expect.stringContains msg "'value' joins a float and a decimal" "named"
              | other -> failtestf "expected the refusal, got %A" other

          testCase "the chunked path types a cells-decided column over the whole rope, and a derive reading it"
          <| fun _ ->
              // Two chunks (1024 rows each): `i > 1500` only in the second, so `y` holds ints in the
              // first chunk and floats in the second — a float column over the rope, which neither
              // chunk alone can see; `z` reads `y` and is typed over the rope's schema.
              let n = 2100
              let idw = RowIdentity.byColumn "id"

              let src: Table =
                  { Schema = [ Field.create "id" IntType; Field.create "i" IntType ]
                    Columns =
                      [ KitColumn.create "id" IntType (rows n Int)
                        KitColumn.create "i" IntType (rows n Int) ] }

              let pipeline =
                  [ Derive("y", Case([ Binary(Gt, Col "i", Lit(Int 1500)), Lit(Float 0.5) ], Lit(Int 1)))
                    Derive("z", Binary(Add, Col "y", Lit(Int 1))) ]

              let reference = ok (DataFrame.evalPipeline pipeline src)
              Expect.equal (typeOf "y" reference) (Some FloatType) "the reference joins the cells"
              Expect.equal (typeOf "z" reference) (Some FloatType) "and types z over that"

              let primed =
                  ok (Incremental.primePrepared DataFrame.noResolve Map.empty idw pipeline (DataFrame.prepare src))

              Expect.equal (Incremental.chunksTouched primed |> Option.isSome) true "the chunked path answered"
              Expect.equal (Incremental.result primed).Schema reference.Schema "prime agrees"

              // An edit to the first chunk only: the second is reused, and the rope's types stand.
              let edited: Table =
                  { src with
                      Columns =
                          [ KitColumn.create "id" IntType (rows n Int)
                            KitColumn.create "i" IntType (rows n (fun k -> Int(if k = 3 then 7 else k))) ] }

              let delta = ok (Delta.diff idw src edited)

              let refreshed =
                  ok (
                      Incremental.refreshPrepared
                          DataFrame.noResolve
                          Map.empty
                          idw
                          pipeline
                          primed
                          delta
                          (DataFrame.prepare edited)
                  )

              Expect.equal
                  (Incremental.result refreshed)
                  (ok (DataFrame.evalPipeline pipeline edited))
                  "refresh is full"

          testCase "a float beside a decimal across two chunks is refused, through the row path"
          <| fun _ ->
              let n = 2100
              let idw = RowIdentity.byColumn "id"

              let src: Table =
                  { Schema = [ Field.create "id" IntType ]
                    Columns = [ KitColumn.create "id" IntType (rows n Int) ] }

              let pipeline =
                  [ Derive("x", Case([ Binary(Gt, Col "id", Lit(Int 1500)), Param "f" ], Param "d")) ]

              let env = Map.ofList [ "f", Float 0.5; "d", Cell.decimal "2.5" |> Option.get ]

              let expected = DataFrame.evalPipelineInEnv env pipeline src

              match expected with
              | Error(TypeError msg) ->
                  Expect.stringContains msg "'x' joins a float and a decimal" "the reference refuses"
              | other -> failtestf "expected the refusal, got %A" other

              let primed =
                  Incremental.primePrepared DataFrame.noResolve env idw pipeline (DataFrame.prepare src)

              Expect.equal (primed |> Result.map ignore) (expected |> Result.map ignore) "the seam refuses the same"
          testCase
              "a bound param decides its derive on every frame, and prime and refresh decide it the same (Phase 413)"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"
              let dec (t: string) = Cell.decimal t |> Option.get

              let table (cells: Cell list) : Table =
                  { Schema = [ Field.create "id" IntType; Field.create "i" IntType ]
                    Columns =
                      [ KitColumn.create "id" IntType (rows cells.Length Int)
                        KitColumn.create "i" IntType cells ] }

              let full = table [ Int 1; Int -2; Int 3 ]
              let allNull = table [ Null; Null; Null ]
              let gone = table []

              for bound, e, ty in
                  [ Int 4, Param "p", IntType
                    dec "2.5", Param "p", DecimalType
                    dec "2.5", ApplyFn(Round, [ Param "p" ]), DecimalType
                    Float 2.5, ApplyFn(Round, [ Param "p" ]), FloatType
                    Float 0.5, Binary(Add, Col "i", Param "p"), FloatType ] do
                  let env = Map.ofList [ "p", bound ]

                  Expect.equal
                      (SchemaWalk.typeOf "d" (SchemaWalk.ofPipeline full.Schema [ Derive("d", e) ]))
                      None
                      "the walk holds no env"

                  for pipeline in [ [ Derive("d", e) ]; [ Filter(Lit(Bool false)); Derive("d", e) ] ] do
                      for label, t in [ "full", full; "all-null", allNull ] do
                          let reference = ok (DataFrame.evalPipelineInEnv env pipeline t)
                          Expect.equal (typeOf "d" reference) (Some ty) (sprintf "%A %A over %s" e bound label)

                          for prepared in [ false; true ] do
                              let primed =
                                  if prepared then
                                      Incremental.primePrepared
                                          DataFrame.noResolve
                                          env
                                          idw
                                          pipeline
                                          (DataFrame.prepare t)
                                  else
                                      Incremental.prime DataFrame.noResolve env idw pipeline t

                              let primed = ok primed

                              Expect.equal
                                  (Incremental.result primed).Schema
                                  reference.Schema
                                  (sprintf "prime (prepared %b) %A %A over %s" prepared e bound label)

                              // Every row deleted, then every cell nulled: the refreshed column keeps
                              // the type the params decided, as the full evaluation of `after` does.
                              for after in [ gone; allNull ] do
                                  let delta = ok (Delta.diff idw t after)

                                  let refreshed =
                                      if prepared then
                                          Incremental.refreshPrepared
                                              DataFrame.noResolve
                                              env
                                              idw
                                              pipeline
                                              primed
                                              delta
                                              (DataFrame.prepare after)
                                      else
                                          Incremental.refresh DataFrame.noResolve env idw pipeline primed delta after

                                  Expect.equal
                                      (Incremental.result (ok refreshed)).Schema
                                      (ok (DataFrame.evalPipelineInEnv env pipeline after)).Schema
                                      (sprintf "refresh (prepared %b) %A %A from %s" prepared e bound label)

          testCase
              "a Round over an undecided operand is left to the cells, and prime and refresh read them the same (Phase 413)"
          <| fun _ ->
              // Until Phase 413 the typer called this a float on every frame, and a row taking the
              // decimal arm put a decimal cell in a float column. Now the cells decide: a float over
              // int rows, a decimal over decimal rows, and the float-beside-decimal refusal by name
              // where they hold both.
              let idw = RowIdentity.byColumn "id"
              let dec (t: string) = Cell.decimal t |> Option.get

              let e =
                  ApplyFn(Round, [ Case([ Binary(Gt, Col "i", Lit(Int 0)), Col "i" ], Col "m") ])

              let pipeline = [ Derive("d", e) ]

              let table (cells: (Cell * Cell) list) : Table =
                  { Schema =
                      [ Field.create "id" IntType
                        Field.create "i" IntType
                        Field.create "m" DecimalType ]
                    Columns =
                      [ KitColumn.create "id" IntType (rows cells.Length Int)
                        KitColumn.create "i" IntType (cells |> List.map fst)
                        KitColumn.create "m" DecimalType (cells |> List.map snd) ] }

              Expect.equal (SchemaWalk.typeOf "d" (SchemaWalk.ofPipeline (table []).Schema pipeline)) None "undecided"

              let ints = table [ Int 1, dec "2.5"; Int 2, dec "3.5" ]
              let decs = table [ Int -1, dec "2.5"; Int -2, dec "3.5" ]
              let both = table [ Int 1, dec "2.5"; Int -2, dec "3.5" ]

              Expect.equal (typeOf "d" (ok (DataFrame.evalPipeline pipeline ints))) (Some FloatType) "int rows"
              Expect.equal (typeOf "d" (ok (DataFrame.evalPipeline pipeline decs))) (Some DecimalType) "decimal rows"

              match DataFrame.evalPipeline pipeline both with
              | Error(TypeError msg) -> Expect.stringContains msg "'d' joins a float and a decimal" "refused by name"
              | other -> failtestf "expected the refusal, got %A" other

              for prepared in [ false; true ] do
                  let primed =
                      if prepared then
                          Incremental.primePrepared DataFrame.noResolve Map.empty idw pipeline (DataFrame.prepare ints)
                      else
                          Incremental.prime DataFrame.noResolve Map.empty idw pipeline ints

                  let primed = ok primed

                  Expect.equal
                      (Incremental.result primed)
                      (ok (DataFrame.evalPipeline pipeline ints))
                      (sprintf "prime (prepared %b)" prepared)

                  for after in [ decs; both ] do
                      let delta = ok (Delta.diff idw ints after)

                      let refreshed =
                          if prepared then
                              Incremental.refreshPrepared
                                  DataFrame.noResolve
                                  Map.empty
                                  idw
                                  pipeline
                                  primed
                                  delta
                                  (DataFrame.prepare after)
                          else
                              Incremental.refresh DataFrame.noResolve Map.empty idw pipeline primed delta after

                      Expect.equal
                          (refreshed |> Result.map Incremental.result)
                          (DataFrame.evalPipeline pipeline after)
                          (sprintf "refresh (prepared %b) to %A" prepared after) ]
