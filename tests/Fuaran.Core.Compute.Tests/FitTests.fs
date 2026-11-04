module Fuaran.Compute.Tests.FitTests

// ============================================================================
//  Phase 592 — a pipeline fits an OLS regression with the `Fit` verb (`Transform.Fit`)
//  (`DECISIONS.md` D53).
//
//  The estimates are the linear-algebra package's own least squares over the
//  design the bridge builds, byte for byte; listwise deletion, one-hot
//  categorical coding against the first level, aliased columns named with a
//  null estimate, weights, ridge rows and offsets; units composed as
//  unit(y) / unit(x); a decimal response's fitted values, residuals and
//  deviances computed in decimal; the four closed output schemas, as the
//  static walk states them and as `PipelineQueryRegistry` admits them; the
//  refusals by name; the wire round trip; and the incremental law — an
//  append refresh is byte-equal to a full evaluation, an edit refits only the
//  group it touched, and the state codec carries the factorizations.
//
//  Phase 595 — the log-link families (`DECISIONS.md` D55): Poisson and Gamma
//  through `LinearAlgebra.Glm`, held to the package byte for byte and to R's
//  published Dobson example; the statistics' units under the log link; the
//  refusals; and a maintained GLM refitting every group from the pinned start.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.LinearAlgebra

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private unitOf (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok u -> u
    | Error e -> failtestf "not a unit: %s (%A)" text e

let private dec (text: string) : Cell =
    match Cell.decimal text with
    | Some c -> c
    | None -> failtestf "not decimal text: %s" text

let private table (cols: (Field * Cell list) list) : Table =
    { Schema = cols |> List.map fst
      Columns = cols |> List.map (fun (f, cells) -> KitColumn.create f.Name f.Type cells) }

/// Eight rows, two regions, a float predictor, an int predictor, a categorical one and a weight.
let private xs = [ 1.0; 2.0; 3.0; 4.0; 5.0; 6.0; 7.0; 8.0 ]
let private ys = [ 3.1; 4.9; 7.2; 8.8; 11.3; 12.7; 15.1; 17.2 ]

let private sales: Table =
    table
        [ Field.create "id" StringType, [ for i in 0..7 -> Str("r" + string i) ]
          Field.create "region" StringType, [ for i in 0..7 -> Str(if i % 2 = 0 then "north" else "south") ]
          Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ for x in xs -> Float x ]
          Field.create "k" IntType, [ for i in 0..7 -> Int((i * 5) % 3) ]
          Field.create "colour" StringType,
          [ for i in 0..7 ->
                Str(
                    match i % 3 with
                    | 0 -> "red"
                    | 1 -> "blue"
                    | _ -> "green"
                ) ]
          Field.create "w" FloatType, [ for i in 0..7 -> Float(if i = 3 then 0.0 else 1.0 + float i / 4.0) ]
          Field.create "y" FloatType |> Field.withUnit (unitOf "s"), [ for y in ys -> Float y ] ]

let private fit (spec: FitSpec) (t: Table) = DataFrame.evalPipeline [ Fit spec ] t

let private column (name: string) (t: Table) : Cell list =
    match Table.tryColumn name t with
    | Some c -> Column.toCells c
    | None -> failtestf "no column %s in %A" name (t.Schema |> List.map _.Name)

let private floatOf (c: Cell) : float =
    match c with
    | Float v -> v
    | other -> failtestf "expected a float cell, got %A" other

let private bits (v: float) = System.BitConverter.DoubleToInt64Bits v

let private ols = FitSpec.ols "y" [ "x", Col "x" ]

/// The same fit through the linear-algebra package directly: the design `[1, x]` over the rows.
let private direct (rows: (float * float) list) : LeastSquaresFit =
    let n = rows.Length

    let x = Matrix.init n 2 (fun i j -> if j = 0 then 1.0 else fst rows[i])

    ok (LeastSquares.solve x (Vector.adopt (rows |> List.map snd |> List.toArray)))

let private nearly (a: float) (b: float) (what: string) =
    Expect.isTrue (abs (a - b) <= 1e-9 * (1.0 + abs b)) (sprintf "%s: %.17g against %.17g" what a b)

[<Tests>]
let tests =
    testList
        "Fit (Phase 592)"
        [ testCase "the estimates are the linear-algebra package's least squares, byte for byte"
          <| fun _ ->
              let out = ok (fit ols sales)
              Expect.equal (column "term" out) [ Str "(Intercept)"; Str "x" ] "intercept first, then the terms"
              let ls = direct (List.zip xs ys)

              let expected = ls.Coefficients |> List.map (fun c -> bits (Option.get c))

              Expect.equal (column "estimate" out |> List.map (floatOf >> bits)) expected "the estimates' bits"

          testCase "the standard errors, t and the summary are the textbook statistics"
          <| fun _ ->
              let coefs = ok (fit ols sales)
              let summary = ok (fit { ols with Output = FitOutput.Summary } sales)
              let n = 8.0
              let mx = List.average xs
              let my = List.average ys
              let sxx = xs |> List.sumBy (fun x -> (x - mx) ** 2.0)
              let sxy = List.zip xs ys |> List.sumBy (fun (x, y) -> (x - mx) * (y - my))
              let b = sxy / sxx
              let a = my - b * mx
              let rss = List.zip xs ys |> List.sumBy (fun (x, y) -> (y - a - b * x) ** 2.0)
              let tss = ys |> List.sumBy (fun y -> (y - my) ** 2.0)
              let s2 = rss / (n - 2.0)
              let est = column "estimate" coefs |> List.map floatOf
              let se = column "std_error" coefs |> List.map floatOf
              let t = column "t" coefs |> List.map floatOf
              nearly est[0] a "intercept"
              nearly est[1] b "slope"
              nearly se[1] (sqrt (s2 / sxx)) "slope SE"
              nearly se[0] (sqrt (s2 * (1.0 / n + mx * mx / sxx))) "intercept SE"
              nearly t[1] (b / sqrt (s2 / sxx)) "slope t"
              Expect.equal (column "n" summary) [ Int 8 ] "n"
              Expect.equal (column "df" summary) [ Int 6 ] "df"
              Expect.equal (column "rank" summary) [ Int 2 ] "rank"
              nearly (column "deviance" summary |> List.head |> floatOf) rss "deviance"
              nearly (column "null_deviance" summary |> List.head |> floatOf) tss "null deviance"
              nearly (column "r2" summary |> List.head |> floatOf) (1.0 - rss / tss) "r2"
              nearly (column "sigma" summary |> List.head |> floatOf) (sqrt s2) "sigma"
              nearly (column "dispersion" summary |> List.head |> floatOf) s2 "dispersion"
              Expect.equal (column "iterations" summary) [ Int 1 ] "one solve"
              Expect.equal (column "converged" summary) [ Bool true ] "converged"

          testCase "units compose as unit(y) / unit(x), and the summary states the response's"
          <| fun _ ->
              let coefs = ok (fit ols sales)
              Expect.equal (column "unit" coefs) [ Str "s"; Str "s/m" ] "per coefficient"
              let summary = ok (fit { ols with Output = FitOutput.Summary } sales)

              let unitAt name =
                  summary.Schema
                  |> List.find (fun f -> f.Name = name)
                  |> _.Unit
                  |> Option.map Unit.render

              Expect.equal (unitAt "sigma") (Some "s") "sigma in the response's unit"
              Expect.equal (unitAt "deviance") (Some "s2") "deviance in its square"
              let fitted = ok (fit { ols with Output = FitOutput.Fitted } sales)
              let unitOfFitted = fitted.Schema |> List.last |> _.Unit |> Option.map Unit.render
              Expect.equal unitOfFitted (Some "s") "fitted values in the response's unit"

          testCase "a group fits alone: each group's estimates are its own rows' fit"
          <| fun _ ->
              let out = ok (fit { ols with GroupBy = [ "region" ] } sales)

              Expect.equal
                  (column "region" out)
                  [ Str "north"; Str "north"; Str "south"; Str "south" ]
                  "groups in first-appearance order, terms within"

              let north =
                  List.zip xs ys
                  |> List.indexed
                  |> List.filter (fun (i, _) -> i % 2 = 0)
                  |> List.map snd

              let ls = direct north

              Expect.equal
                  (column "estimate" out |> List.take 2 |> List.map (floatOf >> bits))
                  (ls.Coefficients |> List.map (Option.get >> bits))
                  "north's estimates are north's fit"

          testCase
              "a categorical term is one-hot coded against its first level; without an intercept the first keeps all"
          <| fun _ ->
              let spec = FitSpec.ols "y" [ "x", Col "x"; "colour", Col "colour" ]
              let out = ok (fit spec sales)

              Expect.equal
                  (column "term" out)
                  [ Str "(Intercept)"; Str "x"; Str "colour[green]"; Str "colour[red]" ]
                  "blue is the reference: first in the cell order"

              let noIntercept = ok (fit { spec with Intercept = false } sales)

              Expect.equal
                  (column "term" noIntercept)
                  [ Str "x"; Str "colour[blue]"; Str "colour[green]"; Str "colour[red]" ]
                  "every level kept"

          testCase "an aliased column is named, with a null estimate, and the rank says so"
          <| fun _ ->
              let spec =
                  FitSpec.ols "y" [ "x", Col "x"; "twice", Binary(Mul, Col "x", Lit(Float 2.0)) ]

              let out = ok (fit spec sales)
              Expect.equal (column "term" out) [ Str "(Intercept)"; Str "x"; Str "twice" ] "every term is named"
              Expect.equal (column "estimate" out |> List.last) Null "the aliased column's estimate is null"
              Expect.equal (column "std_error" out |> List.last) Null "and its standard error"
              let summary = ok (fit { spec with Output = FitOutput.Summary } sales)
              Expect.equal (column "rank" summary) [ Int 2 ] "rank two of three"
              Expect.equal (column "df" summary) [ Int 6 ] "df counts the kept columns"

          testCase "listwise deletion: a null anywhere the row is read leaves it out, and its fitted value null"
          <| fun _ ->
              let holed =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Null; Float 3.0; Float 4.0; Float 5.0 ]
                        Field.create "y" FloatType, [ Float 2.0; Float 3.0; Null; Float 8.1; Float 9.9 ] ]

              let summary = ok (fit { ols with Output = FitOutput.Summary } holed)
              Expect.equal (column "n" summary) [ Int 3 ] "two rows deleted"
              let fitted = ok (fit { ols with Output = FitOutput.Fitted } holed)
              let cells = column "fitted" fitted
              Expect.equal cells[1] Null "a deleted row has no fitted value"
              Expect.equal cells[2] Null "nor does one with a null response"
              Expect.equal (column "x" fitted) (column "x" holed) "the input rows pass through unchanged"

          testCase "a zero weight is in the design, not counted; weights change the estimates"
          <| fun _ ->
              let weighted =
                  { ols with
                      Weights = Some "w"
                      Output = FitOutput.Summary }

              let summary = ok (fit weighted sales)
              Expect.equal (column "n" summary) [ Int 7 ] "the zero-weight row is not counted"

              let fitted =
                  ok (
                      fit
                          { weighted with
                              Output = FitOutput.Fitted }
                          sales
                  )

              Expect.notEqual (column "fitted" fitted |> List.item 3) Null "but it has a fitted value"
              let plain = ok (fit ols sales)

              let w =
                  ok (
                      fit
                          { weighted with
                              Output = FitOutput.Coefficients }
                          sales
                  )

              Expect.notEqual (column "estimate" w) (column "estimate" plain) "weights move the fit"

          testCase "ridge: a zero penalty is no penalty; a positive one shrinks the slope, never the intercept's row"
          <| fun _ ->
              let plain = ok (fit ols sales)
              let zero = ok (fit { ols with Ridge = Some(Slot.Lit 0.0) } sales)
              Expect.equal zero plain "λ = 0 is byte-equal to no ridge"
              let ridged = ok (fit { ols with Ridge = Some(Slot.Lit 10.0) } sales)

              let slope t =
                  column "estimate" t |> List.item 1 |> floatOf

              Expect.isLessThan (abs (slope ridged)) (abs (slope plain)) "the slope shrinks"

              let bound =
                  ok (
                      DataFrame.evalPipelineWithInEnv
                          DataFrame.noResolve
                          (Map.ofList [ "lambda", Float 10.0 ])
                          [ Fit
                                { ols with
                                    Ridge = Some(Slot.Param "lambda") } ]
                          sales
                  )

              Expect.equal bound ridged "a bound penalty is the literal one"

          testCase "an offset enters the linear predictor with coefficient one"
          <| fun _ ->
              let shifted =
                  table
                      [ Field.create "x" FloatType, [ for x in xs -> Float x ]
                        Field.create "y" FloatType, [ for (x, y) in List.zip xs ys -> Float(y + 3.0 * x) ] ]

              let out =
                  ok (
                      fit
                          { ols with
                              Offset = Some(Binary(Mul, Col "x", Lit(Float 3.0))) }
                          shifted
                  )

              let plain = ok (fit ols sales)

              for a, b in List.zip (column "estimate" out) (column "estimate" plain) do
                  nearly (floatOf a) (floatOf b) "the offset is removed exactly"

          testCase "a decimal response: estimates through the recorded cast, every output against it in decimal"
          <| fun _ ->
              let money =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Float 2.0; Float 3.0; Float 4.0 ]
                        Field.create "amount" DecimalType, [ dec "10.10"; dec "19.95"; dec "30.40"; dec "39.99" ] ]

              let spec = FitSpec.ols "amount" [ "x", Col "x" ]
              Expect.equal (FitSpec.estimationResponse spec) (Cast(FloatType, Col "amount")) "the recorded cast"
              let fitted = ok (fit { spec with Output = FitOutput.Fitted } money)

              let resid =
                  ok (
                      fit
                          { spec with
                              Output = FitOutput.Residuals }
                          money
                  )

              Expect.equal (fitted.Schema |> List.last |> _.Type) DecimalType "fitted is decimal"
              Expect.equal (resid.Schema |> List.last |> _.Type) DecimalType "residual is decimal"

              // y = fitted + residual, exactly, in decimal.
              for (y, f), r in
                  List.zip (List.zip (column "amount" money) (column "fitted" fitted)) (column "residual" resid) do
                  match y, f, r with
                  | Decimal y, Decimal f, Decimal r ->
                      Expect.equal
                          (DecimalText.add f r |> Option.map (fun s -> DecimalText.compare s y))
                          (Some(Some 0))
                          "exact"
                  | _ -> failtest "decimal cells"

              let summary = ok (fit { spec with Output = FitOutput.Summary } money)

              match column "deviance" summary, column "null_deviance" summary with
              | [ Decimal _ ], [ Decimal _ ] -> ()
              | other -> failtestf "the deviances are decimal: %A" other

              Expect.equal
                  (column "estimate" (ok (fit spec money)) |> List.map Cell.typeOf)
                  [ Some FloatType; Some FloatType ]
                  "estimates are float statistics"

          testCase "the refusals, each by name"
          <| fun _ ->
              let refused (spec: FitSpec) = fit spec sales |> Result.isError

              Expect.equal
                  (fit
                      { ols with
                          Family = FitFamily.Poisson
                          Link = FitLink.Identity }
                      sales)
                  (Error(UnsupportedFit("poisson", "identity")))
                  "a pairing this version does not evaluate"

              Expect.equal
                  (fit
                      { ols with
                          Family = FitFamily.Tweedie 1.5 }
                      sales)
                  (Error(UnsupportedFit("tweedie", "identity")))
                  "tweedie under identity"

              Expect.isTrue (refused { ols with Ridge = Some(Slot.Lit -1.0) }) "a negative penalty"

              Expect.isTrue
                  (refused
                      { ols with
                          Control =
                              { ols.Control with
                                  MaxIter = Slot.Lit 0 } })
                  "maxIter below one"

              Expect.isTrue
                  (refused
                      { ols with
                          Control = { ols.Control with Tol = Slot.Lit 0.0 } })
                  "a tolerance of zero"

              Expect.isTrue (refused (FitSpec.ols "y" [ "x", Col "x"; "x", Col "k" ])) "a term named twice"
              Expect.isTrue (refused (FitSpec.ols "region" [ "x", Col "x" ])) "a string response"

              Expect.isTrue
                  (refused
                      { (FitSpec.ols "y" []) with
                          Intercept = false })
                  "no design column"

              match fit (FitSpec.ols "nope" [ "x", Col "x" ]) sales with
              | Error(UnknownColumn("nope", _)) -> ()
              | other -> failtestf "an unknown response: %A" other

              let withFitted = ok (DataFrame.evalPipeline [ Derive("fitted", Lit(Int 1)) ] sales)

              Expect.isTrue
                  (fit { ols with Output = FitOutput.Fitted } withFitted |> Result.isError)
                  "an existing output column"

              let nan =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Float nan; Float 3.0 ]
                        Field.create "y" FloatType, [ Float 1.0; Float 2.0; Float 3.0 ] ]

              match fit ols nan with
              | Error(FitError detail) -> Expect.stringContains detail "row 1" "the row is named"
              | other -> failtestf "a NaN term: %A" other

          testCase "the wire round trip, every member"
          <| fun _ ->
              let full =
                  { ols with
                      Predictors = [ "x", Col "x"; "colour", Col "colour" ]
                      GroupBy = [ "region" ]
                      Weights = Some "w"
                      Offset = Some(Lit(Float 0.5))
                      Ridge = Some(Slot.Param "lambda")
                      Family = FitFamily.Tweedie 1.5
                      Link = FitLink.Log
                      Control =
                          { MaxIter = Slot.Param "iters"
                            Tol = Slot.Lit 1e-6 }
                      Output = FitOutput.Summary }

              for spec in
                  [ ols
                    full
                    { ols with
                        Intercept = false
                        Output = FitOutput.Residuals } ] do
                  let p = [ Fit spec ]
                  Expect.equal (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p)) (Ok p) "round trip"

              Expect.stringContains (DataFrameCodec.encodePipeline [ Fit ols ]) "\"$type\":\"fit\"" "the wire tag"
              Expect.equal (Transform.paramsOf [ Fit full ]) [ "lambda"; "iters" ] "the slots' params"

          testCase "the spec's derivations: its expressions, its slots' params, and binding them"
          <| fun _ ->
              let spec =
                  { ols with
                      Offset = Some(Param "shift")
                      Ridge = Some(Slot.Param "lambda")
                      Control =
                          { MaxIter = Slot.Param "iters"
                            Tol = Slot.Lit 1e-6 } }

              Expect.equal (FitSpec.exprs spec) [ Col "x"; Param "shift" ] "the terms, then the offset"
              Expect.equal (FitSpec.slotParams spec) [ "lambda"; "iters" ] "the ridge, then the control"

              Expect.equal
                  (FitSpec.mapExprs (fun _ -> Lit(Int 0)) spec |> FitSpec.exprs)
                  [ Lit(Int 0); Lit(Int 0) ]
                  "every expression mapped"

              let bound =
                  FitSpec.substituteSlots (Map.ofList [ "lambda", Int 2; "iters", Int 9 ]) spec

              Expect.equal bound.Ridge (Some(Slot.Lit 2.0)) "an int cell binds a float slot as its float"
              Expect.equal bound.Control.MaxIter (Slot.Lit 9) "an int slot"

              Expect.equal
                  (FitSpec.substituteSlots (Map.ofList [ "iters", Str "nine" ]) spec).Control.MaxIter
                  (Slot.Param "iters")
                  "a cell of the wrong shape is left unbound"

          testCase "the static walk states every output's schema as the evaluator builds it, exactly"
          <| fun _ ->
              for output in
                  [ FitOutput.Coefficients
                    FitOutput.Summary
                    FitOutput.Fitted
                    FitOutput.Residuals ] do
                  let p =
                      [ Fit
                            { ols with
                                GroupBy = [ "region" ]
                                Output = output } ]

                  let out = ok (DataFrame.evalPipeline p sales)

                  match SchemaWalk.ofPipeline sales.Schema p with
                  | SchemaKnowledge.Closed cols ->
                      Expect.equal
                          (cols |> List.map (fun c -> c.Name, c.Type, c.Unit |> Option.map Unit.render))
                          (out.Schema
                           |> List.map (fun f -> f.Name, Some f.Type, f.Unit |> Option.map Unit.render))
                          (sprintf "%A: names, types and units" output)

                      Expect.isTrue
                          (cols |> List.forall (fun c -> c.Exactness = Fuaran.Compute.Exactness.Pinned))
                          "every column pinned"
                  | other -> failtestf "%A: the walk is closed, got %A" output other

          testCase "a registered regression query is admitted: its result schema is closed"
          <| fun _ ->
              let source: Schema = sales.Schema

              let pq: PipelineQuery =
                  { Query =
                      { Id = "slope-by-region"
                        Params = []
                        ResultSchema =
                          [ Field.create "region" StringType
                            Field.create "term" StringType
                            Field.create "estimate" FloatType
                            Field.create "std_error" FloatType
                            Field.create "t" FloatType
                            Field.create "unit" StringType ]
                        Effect =
                          { Host = ReadsHost
                            Determinism = Effect.network }
                        Source = Ref "sales"
                        TimeoutMs = None
                        PageSize = None
                        Where = []
                        OrderBy = [] }
                    Pipeline = [ Fit { ols with GroupBy = [ "region" ] } ]
                    Sources = Map.ofList [ "sales", source ] }

              match PipelineQueryRegistry.register pq PipelineQueryRegistry.empty with
              | Ok _ -> ()
              | Error e -> failtestf "registration refused: %A" e

          testCase
              "incremental: an append refresh is byte-equal to a full evaluation, and continues the stored factorization"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"

              for output in [ FitOutput.Coefficients; FitOutput.Summary; FitOutput.Fitted ] do
                  let p =
                      [ Fit
                            { ols with
                                GroupBy = [ "region" ]
                                Output = output } ]

                  let before = DataFrame.evalPipeline [ Transform.limit 6 0 ] sales |> ok
                  let state = ok (Incremental.primeOn idw p before)
                  Expect.equal (Incremental.plan' state).Strategy RowLocalThenGroups "maintained"
                  let delta = ok (Delta.diff idw before sales)
                  let refreshed = ok (Incremental.refreshOn idw p state delta sales)
                  let full = ok (DataFrame.evalPipeline p sales)

                  Expect.equal
                      (ColumnCodec.encode (Embedded(Incremental.result refreshed)))
                      (ColumnCodec.encode (Embedded full))
                      (sprintf "%A: byte-equal" output)

                  match (Incremental.footprint refreshed).Recompute with
                  | GroupsRecomputed(_, 0) -> ()
                  | other -> failtestf "%A: no group refit from its first row, got %A" output other

          testCase "incremental: an edit refits only the group it touched; the codec carries the factorizations"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"
              let p = [ Fit { ols with GroupBy = [ "region" ] } ]
              let state = ok (Incremental.primeOn idw p sales)

              let edited =
                  { sales with
                      Columns =
                          sales.Columns
                          |> List.map (fun c ->
                              if c.Name = "y" then
                                  KitColumn.create
                                      "y"
                                      FloatType
                                      (Column.toCells c |> List.mapi (fun i v -> if i = 2 then Float 99.0 else v))
                              else
                                  c) }

              let delta = ok (Delta.diff idw sales edited)
              let decoded = ok (IncrementalCodec.decode (ok (IncrementalCodec.encode state)))

              for s in [ state; decoded ] do
                  let refreshed = ok (Incremental.refreshOn idw p s delta edited)
                  Expect.equal (Incremental.result refreshed) (ok (DataFrame.evalPipeline p edited)) "equal to full"

                  match (Incremental.footprint refreshed).Recompute with
                  | GroupsRecomputed(_, 1) -> ()
                  | other -> failtestf "one group refit, got %A" other

          testCase "incremental: a fit is maintained only last, after no aggregate and no window"
          <| fun _ ->
              let reason p =
                  match (Incremental.plan p).Strategy with
                  | ReferenceOnly r -> Some r
                  | _ -> None

              Expect.equal (reason [ Filter(Binary(Gt, Col "x", Lit(Float 0.0))); Fit ols ]) None "a row-local prefix"

              Expect.equal
                  (reason [ Fit ols; Limit(Slot.Lit 1, Slot.Lit 0) ])
                  (Some(AggregateStepNotLast "fit"))
                  "not last"

              Expect.equal
                  (reason
                      [ GroupBy([ "region" ], [ { Name = "n"; Fn = Count; Of = "x" } ])
                        Fit(FitSpec.ols "n" []) ])
                  (Some(AggregateStepRepeated "fit"))
                  "after a group-by"

              let window =
                  Window
                      { PartitionBy = []
                        OrderBy = [ "x", Asc ]
                        Fn = Lag
                        Of = "x"
                        As = "prev" }

              Expect.equal (reason [ window; Fit ols ]) (Some(FitInputNotMaintained "window")) "after a window" ]

/// Dobson's (1990) randomised controlled trial, R's `?glm` example: nine counts over two
/// three-level factors.
let private dobson: Table =
    let counts = [ 18; 17; 15; 20; 10; 20; 25; 13; 12 ]

    table
        [ Field.create "id" StringType, [ for i in 0..8 -> Str("d" + string i) ]
          Field.create "outcome" StringType, [ for i in 0..8 -> Str(string (i % 3 + 1)) ]
          Field.create "treatment" StringType, [ for i in 0..8 -> Str(string (i / 3 + 1)) ]
          Field.create "counts" IntType, [ for c in counts -> Int c ] ]

let private poisson (response: string) (terms: (string * ColExpr) list) : FitSpec =
    { FitSpec.ols response terms with
        Family = FitFamily.Poisson
        Link = FitLink.Log }

let private gamma (response: string) (terms: (string * ColExpr) list) : FitSpec =
    { FitSpec.ols response terms with
        Family = FitFamily.Gamma
        Link = FitLink.Log }

let private floats (name: string) (t: Table) : float list = column name t |> List.map floatOf

[<Tests>]
let glmTests =
    testList
        "Fit, the log-link families (Phase 595)"
        [ testCase "Poisson reproduces R's Dobson example: estimates, errors, deviances and four iterations"
          <| fun _ ->
              let spec =
                  poisson "counts" [ "outcome", Col "outcome"; "treatment", Col "treatment" ]

              let coef = ok (fit spec dobson)

              Expect.equal
                  (column "term" coef)
                  [ Str "(Intercept)"
                    Str "outcome[2]"
                    Str "outcome[3]"
                    Str "treatment[2]"
                    Str "treatment[3]" ]
                  "treatment coding against the first level"

              // R 4.x: summary(glm(counts ~ outcome + treatment, family = poisson())).
              let estimates = floats "estimate" coef

              List.iter2
                  (fun a b -> Expect.isTrue (abs (a - b) <= 1e-6) (sprintf "estimate %.17g against R's %.17g" a b))
                  estimates
                  [ 3.044522; -0.4542553; -0.2929871; 0.0; 0.0 ]

              List.iter2
                  (fun a b -> Expect.isTrue (abs (a - b) <= 1e-6) (sprintf "error %.17g against R's %.17g" a b))
                  (floats "std_error" coef)
                  [ 0.1708987; 0.2021708; 0.1927423; 0.2; 0.2 ]

              let summary = ok (fit { spec with Output = FitOutput.Summary } dobson)

              let near (a: float) (b: float) what =
                  Expect.isTrue (abs (a - b) <= 1e-4) (sprintf "%s: %.17g against R's %.17g" what a b)

              near (floatOf (List.head (column "deviance" summary))) 5.1291 "the residual deviance"
              near (floatOf (List.head (column "null_deviance" summary))) 10.5814 "the null deviance"
              Expect.equal (column "iterations" summary) [ Int 4 ] "R's four Fisher scoring iterations"
              Expect.equal (column "converged" summary) [ Bool true ] "converged"
              Expect.equal (column "dispersion" summary) [ Float 1.0 ] "Poisson's dispersion is one"
              Expect.equal (column "df" summary) [ Int 4 ] "nine counts, five columns"

          testCase "the estimates are Glm.fit's over the bridge's design, byte for byte"
          <| fun _ ->
              let n = xs.Length
              let x = Matrix.init n 2 (fun i j -> if j = 0 then 1.0 else xs[i])
              let y = Vector.adopt (List.toArray ys)
              let ones = Vector.adopt (Array.create n 1.0)
              let zeros = Vector.adopt (Array.zeroCreate n)

              for family, glmFamily in [ FitFamily.Poisson, GlmFamily.Poisson; FitFamily.Gamma, GlmFamily.Gamma ] do
                  let spec =
                      { poisson "y" [ "x", Col "x" ] with
                          Family = family }

                  let out = ok (fit spec sales)
                  let g = ok (Glm.fit glmFamily GlmLink.Log GlmControl.defaults x y ones zeros)

                  Expect.equal
                      (column "estimate" out |> List.map (floatOf >> bits))
                      (g.Coefficients |> List.map (Option.get >> bits))
                      (sprintf "%A: the estimates' bits" family)

                  Expect.equal
                      (column "std_error" out |> List.map (floatOf >> bits))
                      (g.StdErrors |> List.map (Option.get >> bits))
                      (sprintf "%A: the errors' bits" family)

                  let fitted = ok (fit { spec with Output = FitOutput.Fitted } sales)

                  Expect.equal
                      (floats "fitted" fitted |> List.map bits)
                      (g.Fitted |> Vector.toArray |> Array.toList |> List.map bits)
                      (sprintf "%A: the means' bits" family)

          testCase "Gaussian under Identity through Glm.fit is the pipeline's least squares, byte for byte"
          <| fun _ ->
              let n = xs.Length
              let x = Matrix.init n 2 (fun i j -> if j = 0 then 1.0 else xs[i])

              let w =
                  Vector.adopt [| for i in 0..7 -> if i = 3 then 0.0 else 1.0 + float i / 4.0 |]

              let g =
                  ok (
                      Glm.fit
                          GlmFamily.Gaussian
                          GlmLink.Identity
                          GlmControl.defaults
                          x
                          (Vector.adopt (List.toArray ys))
                          w
                          (Vector.adopt (Array.zeroCreate n))
                  )

              let spec = { ols with Weights = Some "w" }
              let out = ok (fit spec sales)

              Expect.equal
                  (column "estimate" out |> List.map (floatOf >> bits))
                  (g.Coefficients |> List.map (Option.get >> bits))
                  "the estimates' bits"

              Expect.equal
                  (column "std_error" out |> List.map (floatOf >> bits))
                  (g.StdErrors |> List.map (Option.get >> bits))
                  "the errors' bits"

              let summary = ok (fit { spec with Output = FitOutput.Summary } sales)
              Expect.equal (column "deviance" summary) [ Float g.Deviance ] "the deviance's bits"
              Expect.equal (column "iterations" summary) [ Int g.Iterations ] "one iteration"

          testCase "Gamma: the dispersion is Pearson's statistic over the residual degrees of freedom"
          <| fun _ ->
              let spec = gamma "y" [ "x", Col "x" ]
              let mu = floats "fitted" (ok (fit { spec with Output = FitOutput.Fitted } sales))
              let summary = ok (fit { spec with Output = FitOutput.Summary } sales)

              let pearson =
                  List.fold2 (fun acc y m -> let r = (y - m) / m in acc + r * r) 0.0 ys mu

              Expect.equal (column "dispersion" summary) [ Float(pearson / 6.0) ] "Σ((y − μ)/μ)² / (n − p)"
              Expect.equal (column "sigma" summary) [ Float(sqrt (pearson / 6.0)) ] "sigma is its root"

          testCase "an offset ln(exposure) is a rate model: the intercept moves, the slope stays"
          <| fun _ ->
              let plain = ok (fit (poisson "y" [ "k", Col "k" ]) sales)

              let exposed =
                  ok (
                      fit
                          { poisson "y" [ "k", Col "k" ] with
                              Offset = Some(ApplyFn(Ln, [ Lit(Float 2.0) ])) }
                          sales
                  )

              let a, b = floats "estimate" plain, floats "estimate" exposed
              nearly b[0] (a[0] - log 2.0) "the intercept shifted by ln 2"
              nearly b[1] a[1] "the slope unchanged"

          testCase "units under the log link: a slope per unit of x, a pure intercept, the deviances by family"
          <| fun _ ->
              let coef = ok (fit (poisson "y" [ "x", Col "x" ]) sales)
              Expect.equal (column "unit" coef) [ Null; Str "/m" ] "the intercept states none, the slope 1/m"

              let unitOfColumn (name: string) (t: Table) =
                  (t.Schema |> List.find (fun f -> f.Name = name)).Unit |> Option.map Unit.render

              let summaryOf spec =
                  ok (fit { spec with Output = FitOutput.Summary } sales)

              let ps = summaryOf (poisson "y" [ "x", Col "x" ])
              Expect.equal (unitOfColumn "deviance" ps) (Some "s") "a Poisson deviance is in y's unit"
              Expect.equal (unitOfColumn "dispersion" ps) None "a pure dispersion"
              Expect.equal (unitOfColumn "sigma" ps) None "a pure sigma"
              let gs = summaryOf (gamma "y" [ "x", Col "x" ])
              Expect.equal (unitOfColumn "deviance" gs) None "a Gamma deviance is a pure number"

          testCase "the static walk states every log-link output's schema as the evaluator builds it"
          <| fun _ ->
              for spec in [ poisson "y" [ "x", Col "x" ]; gamma "y" [ "x", Col "x" ] ] do
                  for output in
                      [ FitOutput.Coefficients
                        FitOutput.Summary
                        FitOutput.Fitted
                        FitOutput.Residuals ] do
                      let p =
                          [ Fit
                                { spec with
                                    GroupBy = [ "region" ]
                                    Output = output } ]

                      let out = ok (DataFrame.evalPipeline p sales)

                      match SchemaWalk.ofPipeline sales.Schema p with
                      | SchemaKnowledge.Closed cols ->
                          Expect.equal
                              (cols |> List.map (fun c -> c.Name, c.Type, c.Unit |> Option.map Unit.render))
                              (out.Schema
                               |> List.map (fun f -> f.Name, Some f.Type, f.Unit |> Option.map Unit.render))
                              (sprintf "%A %A: names, types and units" spec.Family output)
                      | other -> failtestf "%A: the walk is closed, got %A" output other

          testCase "a decimal response: the fitted mean and the residual in decimal, the deviances float"
          <| fun _ ->
              let money =
                  table
                      [ Field.create "x" FloatType, [ for x in xs -> Float x ]
                        Field.create "y" DecimalType, [ for y in ys -> dec (sprintf "%.2f" y) ] ]

              let spec = gamma "y" [ "x", Col "x" ]
              let fitted = ok (fit { spec with Output = FitOutput.Fitted } money)
              Expect.isTrue (column "fitted" fitted |> List.forall (Cell.typeOf >> (=) (Some DecimalType))) "decimal"
              let summary = ok (fit { spec with Output = FitOutput.Summary } money)
              Expect.equal (column "deviance" summary |> List.map Cell.typeOf) [ Some FloatType ] "a float deviance"

          testCase "the log-link refusals, each by name and row"
          <| fun _ ->
              let negative =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Float 2.0; Float 3.0 ]
                        Field.create "y" FloatType, [ Float 1.0; Float -1.0; Float 2.0 ] ]

              Expect.equal
                  (fit (poisson "y" [ "x", Col "x" ]) negative)
                  (Error(FitError "fit: row 1: the response is outside the poisson family's range"))
                  "a negative count"

              let zero =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Float 2.0; Float 3.0 ]
                        Field.create "y" FloatType, [ Float 1.0; Float 2.0; Float 0.0 ] ]

              Expect.equal
                  (fit (gamma "y" [ "x", Col "x" ]) zero)
                  (Error(FitError "fit: row 2: the response is outside the gamma family's range"))
                  "a zero under Gamma"

              // A zero-weight row far along x: its mean overflows (the separation refusal).
              let far =
                  table
                      [ Field.create "x" FloatType, [ Float 1.0; Float 2.0; Float 3.0; Float 1e6 ]
                        Field.create "w" FloatType, [ Float 1.0; Float 1.0; Float 1.0; Float 0.0 ]
                        Field.create "y" FloatType, [ Float 1.0; Float 3.0; Float 7.0; Float 1.0 ] ]

              Expect.equal
                  (fit
                      { poisson "y" [ "x", Col "x" ] with
                          Weights = Some "w" }
                      far)
                  (Error(OverflowError "fit: row 3: the mean left its range (separation)"))
                  "separation"

              Expect.equal
                  (fit
                      { poisson "y" [ "x", Col "x" ] with
                          Control =
                              { MaxIter = Slot.Lit 1
                                Tol = Slot.Lit 1e-8 } }
                      sales)
                  (Error(FitError "fit: not converged after 1 iterations"))
                  "not converged"

              Expect.equal
                  (fit
                      { poisson "y" [ "x", Col "x" ] with
                          Ridge = Some(Slot.Lit 0.5) }
                      sales)
                  (Error(FitError "fit: a ridge penalty is fitted under the gaussian family only"))
                  "a positive penalty under Poisson"

              Expect.isOk
                  (fit
                      { poisson "y" [ "x", Col "x" ] with
                          Ridge = Some(Slot.Lit 0.0) }
                      sales)
                  "a zero penalty is no penalty"

          testCase "incremental: a GLM refits every group from the pinned start, equal to a full evaluation"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"

              let p =
                  [ Fit
                        { poisson "y" [ "x", Col "x" ] with
                            GroupBy = [ "region" ]
                            Output = FitOutput.Summary } ]

              let before = DataFrame.evalPipeline [ Transform.limit 6 0 ] sales |> ok
              let state = ok (Incremental.primeOn idw p before)
              Expect.equal (Incremental.plan' state).Strategy RowLocalThenGroups "maintained"
              let delta = ok (Delta.diff idw before sales)
              let decoded = ok (IncrementalCodec.decode (ok (IncrementalCodec.encode state)))

              for s in [ state; decoded ] do
                  let refreshed = ok (Incremental.refreshOn idw p s delta sales)

                  Expect.equal
                      (ColumnCodec.encode (Embedded(Incremental.result refreshed)))
                      (ColumnCodec.encode (Embedded(ok (DataFrame.evalPipeline p sales))))
                      "byte-equal"

                  match (Incremental.footprint refreshed).Recompute with
                  | GroupsRecomputed(_, 2) -> ()
                  | other -> failtestf "both groups refit from the start, got %A" other

              Expect.isFalse
                  ((ok (IncrementalCodec.encode state)).Contains "\"acc\"")
                  "a GLM leaves no factorization in the state"

              let olsState =
                  ok (Incremental.primeOn idw [ Fit { ols with GroupBy = [ "region" ] } ] before)

              Expect.isTrue
                  ((ok (IncrementalCodec.encode olsState)).Contains "\"acc\"")
                  "where least squares leaves one" ]
