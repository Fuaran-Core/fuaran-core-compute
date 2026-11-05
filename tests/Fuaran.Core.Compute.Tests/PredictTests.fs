module Fuaran.Compute.Tests.PredictTests

// ============================================================================
//  Phase 597 — a fitted model scores rows with prediction intervals
//  (`Transform.Predict`, `DECISIONS.md` D56).
//
//  A training row's prediction is its fitted value, byte for byte; the
//  standard errors are the textbook ones, se_pred² = se_fit² + σ̂²; an
//  interval at a tabled df uses the tabled quantile exactly, and the table
//  is read through `Interpolate` with the normal row at 1/df = 0; new rows
//  are coded against the fit's own reference level; the log link answers
//  exp η through `Elementary.exp`; a coefficient table scores point
//  estimates; the units; the static walk; the wire; the refusals by name.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private unitOf (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok u -> u
    | Error e -> failtestf "not a unit: %s (%A)" text e

let private table (cols: (Field * Cell list) list) : Table =
    { Schema = cols |> List.map fst
      Columns = cols |> List.map (fun (f, cells) -> KitColumn.create f.Name f.Type cells) }

let private xs = [ 1.0; 2.0; 3.0; 4.0; 5.0; 6.0; 7.0; 8.0 ]
let private ys = [ 3.1; 4.9; 7.2; 8.8; 11.3; 12.7; 15.1; 17.2 ]

/// Eight rows: two regions, a float predictor in metres, a categorical one, a count response and a
/// response in seconds.
let private sales: Table =
    table
        [ Field.create "region" StringType, [ for i in 0..7 -> Str(if i % 2 = 0 then "north" else "south") ]
          Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ for x in xs -> Float x ]
          Field.create "colour" StringType,
          [ for i in 0..7 ->
                Str(
                    match i % 3 with
                    | 0 -> "red"
                    | 1 -> "blue"
                    | _ -> "green"
                ) ]
          Field.create "count" IntType, [ for i in 0..7 -> Int(2 + (i * 7) % 5 + i) ]
          Field.create "y" FloatType |> Field.withUnit (unitOf "s"), [ for y in ys -> Float y ] ]

let private ols = FitSpec.ols "y" [ "x", Col "x" ]

let private poisson =
    { FitSpec.ols "count" [ "x", Col "x" ] with
        Family = FitFamily.Poisson
        Link = FitLink.Log }

let private predict (fit: FitSpec) =
    PredictSpec.ofTraining fit (Embedded sales)

let private withInterval (kind: IntervalKind) (level: float) (spec: PredictSpec) =
    { spec with
        Interval = Some { Kind = kind; Level = Slot.Lit level } }

let private column (name: string) (t: Table) : Cell list =
    match Table.tryColumn name t with
    | Some c -> Column.toCells c
    | None -> failtestf "no column %s in %A" name (t.Schema |> List.map _.Name)

let private floatOf (c: Cell) : float =
    match c with
    | Float v -> v
    | other -> failtestf "expected a float cell, got %A" other

let private bits (v: float) = System.BitConverter.DoubleToInt64Bits v

let private run (pipeline: Transform list) (t: Table) = DataFrame.evalPipeline pipeline t

let private refusedWith (fragment: string) (r: Result<Table, EvalError>) =
    match r with
    | Error e -> Expect.stringContains (DataFrame.errorString e) fragment "the refusal names it"
    | Ok t -> failtestf "expected a refusal naming %s, got %A" fragment t.Schema

let private nearly (a: float) (b: float) (what: string) =
    Expect.isTrue (abs (a - b) <= 1e-9 * (1.0 + abs b)) (sprintf "%s: %.17g against %.17g" what a b)

/// One new row to score: `x` and the colour.
let private newRows (rows: (float * string * string) list) : Table =
    table
        [ Field.create "region" StringType, [ for (_, _, r) in rows -> Str r ]
          Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ for (x, _, _) in rows -> Float x ]
          Field.create "colour" StringType, [ for (_, c, _) in rows -> Str c ] ]

[<Tests>]
let tests =
    testList
        "Predict (Phase 597)"
        [ testCase "a training row's prediction is its fitted value, byte for byte — every family, groups and levels"
          <| fun _ ->
              let grouped =
                  { ols with
                      Predictors = [ "x", Col "x"; "colour", Col "colour" ]
                      GroupBy = [ "region" ] }

              for fit in
                  [ ols
                    grouped
                    { ols with
                        Offset = Some(Lit(Float 0.25)) }
                    poisson ] do
                  let fitted = ok (run [ Fit { fit with Output = FitOutput.Fitted } ] sales)
                  let predicted = ok (run [ Predict(predict fit) ] sales)

                  Expect.equal
                      (column "predicted" predicted |> List.map (floatOf >> bits))
                      (column "fitted" fitted |> List.map (floatOf >> bits))
                      (sprintf "%A: the bits" fit.Family)

          testCase "se_fit and se_pred are the textbook errors, and se_pred² = se_fit² + σ̂²"
          <| fun _ ->
              let out =
                  ok (run [ Predict(predict ols) ] (newRows [ 2.5, "red", "north"; 10.0, "red", "north" ]))

              let summary = ok (run [ Fit { ols with Output = FitOutput.Summary } ] sales)
              let sigma = floatOf (column "sigma" summary).Head
              let n = 8.0
              let mx = List.average xs
              let sxx = xs |> List.sumBy (fun x -> (x - mx) ** 2.0)

              for (x0, sf, sp) in
                  List.zip3
                      [ 2.5; 10.0 ]
                      (column "se_fit" out |> List.map floatOf)
                      (column "se_pred" out |> List.map floatOf) do
                  let expected = sigma * sqrt (1.0 / n + (x0 - mx) ** 2.0 / sxx)
                  nearly sf expected (sprintf "se_fit at %g" x0)
                  nearly (sp * sp) (sf * sf + sigma * sigma) (sprintf "se_pred² at %g" x0)

          testCase "an interval at a tabled df uses the tabled quantile exactly"
          <| fun _ ->
              // Eight rows, two coefficients: six residual degrees of freedom.
              let q6 = 2.44691185114497
              Expect.equal (StudentT.quantile 0.95 (Some 6) |> Option.map bits) (Some(bits q6)) "the table's row"

              let out =
                  ok (
                      run
                          [ Predict(predict ols |> withInterval IntervalKind.Prediction 0.95) ]
                          (newRows [ 4.5, "red", "north" ])
                  )

              let eta = floatOf (column "predicted" out).Head
              let sp = floatOf (column "se_pred" out).Head
              Expect.equal (bits (floatOf (column "lower" out).Head)) (bits (eta - q6 * sp)) "lower"
              Expect.equal (bits (floatOf (column "upper" out).Head)) (bits (eta + q6 * sp)) "upper"

              let conf =
                  ok (
                      run
                          [ Predict(predict ols |> withInterval IntervalKind.Confidence 0.95) ]
                          (newRows [ 4.5, "red", "north" ])
                  )

              let sf = floatOf (column "se_fit" conf).Head
              Expect.equal (bits (floatOf (column "upper" conf).Head)) (bits (eta + q6 * sf)) "a confidence bound"

          testCase "the t table: every row exact at its knot, the normal row at infinity, and between rows in 1/df"
          <| fun _ ->
              for (v, qs) in StudentT.rows do
                  for c in 0 .. StudentT.levels.Length - 1 do
                      let df = if v = 0 then None else Some v

                      Expect.equal
                          (StudentT.quantile StudentT.levels[c] df |> Option.map bits)
                          (Some(bits qs[c]))
                          (sprintf "df %d, level %g" v StudentT.levels[c])

              // Spot values against the published tables (to their printed digits).
              nearly (Option.get (StudentT.quantile 0.95 (Some 10))) 2.228138851964938 "t(0.975, 10)"
              nearly (Option.get (StudentT.quantile 0.95 None)) 1.959963984540054 "z(0.975)"
              nearly (Option.get (StudentT.quantile 0.99 (Some 1))) 63.65674116287399 "t(0.995, 1)"

              let between = Option.get (StudentT.quantile 0.95 (Some 2000))
              let t1000 = Option.get (StudentT.quantile 0.95 (Some 1000))
              let z = Option.get (StudentT.quantile 0.95 None)
              Expect.isTrue (between < t1000 && between > z) "above the table: between the 1000 row and the normal"
              nearly between ((t1000 + z) / 2.0) "halfway in 1/df"

              let mid = Option.get (StudentT.quantile 0.95 (Some 35))
              let t30 = Option.get (StudentT.quantile 0.95 (Some 30))
              let t40 = Option.get (StudentT.quantile 0.95 (Some 40))
              Expect.isTrue (mid < t30 && mid > t40) "an untabled df reads between its neighbours"
              Expect.isTrue (abs (mid - 2.0301079282503425) < 2e-4) "linear in 1/df is close to the exact t"
              Expect.isNone (StudentT.quantile 0.97 (Some 6)) "an untabled level"
              Expect.isNone (StudentT.quantile 0.95 (Some 0)) "no residual df"

          testCase "a new row is coded against the fit's reference level; the reference scores no indicator"
          <| fun _ ->
              let fit =
                  { ols with
                      Predictors = [ "x", Col "x"; "colour", Col "colour" ] }

              let coefs = ok (run [ Fit fit ] sales)

              let estimate (term: string) =
                  List.zip (column "term" coefs) (column "estimate" coefs)
                  |> List.pick (fun (t, e) -> if t = Str term then Some(floatOf e) else None)

              Expect.equal
                  (column "term" coefs)
                  [ Str "(Intercept)"; Str "x"; Str "colour[green]"; Str "colour[red]" ]
                  "blue is the reference level"

              let out =
                  ok (run [ Predict(predict fit) ] (newRows [ 3.0, "blue", "north"; 3.0, "red", "north" ]))

              let p = column "predicted" out |> List.map floatOf
              Expect.equal (bits p[0]) (bits (0.0 + 1.0 * estimate "(Intercept)" + 3.0 * estimate "x")) "blue"

              Expect.equal
                  (bits p[1])
                  (bits (
                      0.0
                      + 1.0 * estimate "(Intercept)"
                      + 3.0 * estimate "x"
                      + 0.0 * estimate "colour[green]"
                      + 1.0 * estimate "colour[red]"
                  ))
                  "red"

          testCase "a null term leaves the row's every appended cell null"
          <| fun _ ->
              let rows =
                  table [ Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ Float 1.0; Null ] ]

              let out =
                  ok (run [ Predict(predict ols |> withInterval IntervalKind.Confidence 0.9) ] rows)

              for name in [ "predicted"; "se_fit"; "se_pred"; "lower"; "upper" ] do
                  let cells = column name out
                  Expect.equal cells[1] Null (name + " at the null row")
                  Expect.notEqual cells[0] Null (name + " at the present row")

          testCase "the log link: the response scale is exp η through Elementary, and so are its bounds"
          <| fun _ ->
              let rows = newRows [ 2.0, "red", "north"; 9.0, "red", "north" ]

              let link =
                  ok (
                      run
                          [ Predict(
                                { predict poisson with
                                    Scale = PredictScale.Link }
                                |> withInterval IntervalKind.Confidence 0.95
                            ) ]
                          rows
                  )

              let response =
                  ok (run [ Predict(predict poisson |> withInterval IntervalKind.Confidence 0.95) ] rows)

              for (l, r) in [ "predicted", "predicted"; "lower", "lower"; "upper", "upper" ] do
                  Expect.equal
                      (column r response |> List.map (floatOf >> bits))
                      (column l link |> List.map (floatOf >> LinearAlgebra.Elementary.exp >> bits))
                      (l + ": exp of the link scale")

              // Phase 600 (D58 item 7): the response scale's error is the delta method's, `|dμ/dη| · se(η)`,
              // and `dμ/dη` is `exp η` under the log link.
              Expect.equal
                  (column "se_fit" response |> List.map (floatOf >> bits))
                  (List.zip (column "predicted" link) (column "se_fit" link)
                   |> List.map (fun (e, s) -> bits (floatOf s * abs (LinearAlgebra.Elementary.exp (floatOf e)))))
                  "the errors are the delta method's"

              Expect.equal (column "se_pred" response) [ Null; Null ] "no predictive error outside the gaussian family"

              // Poisson fixes its dispersion, so the interval reads the normal row.
              let z = Option.get (StudentT.quantile 0.95 None)
              let eta = floatOf (column "predicted" link).Head
              let sf = floatOf (column "se_fit" link).Head
              Expect.equal (bits (floatOf (column "upper" link).Head)) (bits (eta + z * sf)) "the normal quantile"

          testCase "a coefficient table scores the same point estimates as the training rows"
          <| fun _ ->
              let coefs = ok (run [ Fit ols ] sales)
              let rows = newRows [ 2.5, "red", "north"; 10.0, "red", "north" ]
              let fromTraining = ok (run [ Predict(predict ols) ] rows)

              let fromTable =
                  ok (
                      run
                          [ Predict
                                { predict ols with
                                    Model = PredictModel.Coefficients(Embedded coefs) } ]
                          rows
                  )

              Expect.equal
                  (fromTable.Schema |> List.map _.Name)
                  [ "region"; "x"; "colour"; "predicted" ]
                  "a point estimate only"

              Expect.equal
                  (column "predicted" fromTable |> List.map (floatOf >> bits))
                  (column "predicted" fromTraining |> List.map (floatOf >> bits))
                  "the bits"

          testCase "units: η and μ in unit(y) under the identity link, η a pure number under the log link"
          <| fun _ ->
              let rows = newRows [ 2.5, "red", "north" ]

              let unitsOf (t: Table) =
                  t.Schema
                  |> List.skip 3
                  |> List.map (fun f -> f.Name, f.Unit |> Option.map Unit.render)

              Expect.equal
                  (unitsOf (ok (run [ Predict(predict ols |> withInterval IntervalKind.Confidence 0.9) ] rows)))
                  [ "predicted", Some "s"
                    "se_fit", Some "s"
                    "se_pred", Some "s"
                    "lower", Some "s"
                    "upper", Some "s" ]
                  "identity"

              Expect.equal
                  (unitsOf (
                      ok (
                          run
                              [ Predict(
                                    { predict poisson with
                                        Scale = PredictScale.Link }
                                ) ]
                              rows
                      )
                  ))
                  [ "predicted", None; "se_fit", None; "se_pred", None ]
                  "log, link scale"

          testCase "the static walk states the appended schema as the evaluator builds it, pinned"
          <| fun _ ->
              let rows = newRows [ 2.5, "red", "north" ]
              let coefs = ok (run [ Fit ols ] sales)

              for p in
                  [ [ Predict(predict ols |> withInterval IntervalKind.Prediction 0.99) ]
                    [ Predict(
                          { predict poisson with
                              Scale = PredictScale.Link }
                      ) ]
                    [ Predict
                          { predict ols with
                              Model = PredictModel.Coefficients(Embedded coefs) } ] ] do
                  let out = ok (run p rows)

                  match SchemaWalk.ofPipeline rows.Schema p with
                  | SchemaKnowledge.Closed cols ->
                      Expect.equal
                          (cols |> List.map (fun c -> c.Name, c.Type, c.Unit |> Option.map Unit.render))
                          (out.Schema
                           |> List.map (fun f -> f.Name, Some f.Type, f.Unit |> Option.map Unit.render))
                          "names, types and units"

                      Expect.isTrue
                          (cols |> List.forall (fun c -> c.Exactness = Fuaran.Compute.Exactness.Pinned))
                          "every column pinned"
                  | other -> failtestf "the walk is closed, got %A" other

          testCase "the wire round trip, every member, and the level's param"
          <| fun _ ->
              let full =
                  { predict
                        { ols with
                            GroupBy = [ "region" ]
                            Ridge = Some(Slot.Param "lambda") } with
                      Model = PredictModel.Coefficients(Ref "coefs")
                      Scale = PredictScale.Link
                      Interval =
                          Some
                              { Kind = IntervalKind.Prediction
                                Level = Slot.Param "level" } }

              for spec in [ predict ols; full; predict ols |> withInterval IntervalKind.Confidence 0.8 ] do
                  let p = [ Predict spec ]
                  Expect.equal (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p)) (Ok p) "round trip"

              Expect.stringContains
                  (DataFrameCodec.encodePipeline [ Predict(predict ols) ])
                  "\"$type\":\"predict\""
                  "the wire tag"

              Expect.equal (Transform.paramsOf [ Predict full ]) [ "lambda"; "level" ] "the slots' params"

              let bound =
                  Transform.substitute (Map.ofList [ "level", Float 0.9 ]) [ Predict full ]

              match bound with
              | [ Predict b ] -> Expect.equal (b.Interval |> Option.map _.Level) (Some(Slot.Lit 0.9)) "bound"
              | other -> failtestf "one step, got %A" other

          testCase "the spec's derivations: its expressions, its slots' params, and binding them"
          <| fun _ ->
              let spec =
                  { predict
                        { ols with
                            Offset = Some(Param "shift")
                            Ridge = Some(Slot.Param "lambda") } with
                      Interval =
                          Some
                              { Kind = IntervalKind.Confidence
                                Level = Slot.Param "level" } }

              Expect.equal (PredictSpec.slotParams spec) [ "lambda"; "level" ] "the model's, then the level"

              Expect.equal
                  (PredictSpec.mapExprs (fun _ -> Lit(Int 0)) spec).Fit
                  (FitSpec.mapExprs (fun _ -> Lit(Int 0)) spec.Fit)
                  "every expression of the model mapped"

              let bound =
                  PredictSpec.substituteSlots (Map.ofList [ "lambda", Int 2; "level", Float 0.99 ]) spec

              Expect.equal bound.Fit.Ridge (Some(Slot.Lit 2.0)) "the model's slot"
              Expect.equal (bound.Interval |> Option.map _.Level) (Some(Slot.Lit 0.99)) "the level"

              Expect.equal
                  ((PredictSpec.substituteSlots (Map.ofList [ "level", Str "high" ]) spec).Interval
                   |> Option.map _.Level)
                  (Some(Slot.Param "level"))
                  "a cell of the wrong shape is left unbound"

          testCase "the refusals, each by name"
          <| fun _ ->
              let rows = newRows [ 2.5, "red", "north" ]

              run [ Predict(predict ols |> withInterval IntervalKind.Confidence 0.97) ] rows
              |> refusedWith "the level 0.97 is not tabled"

              run [ Predict(predict poisson |> withInterval IntervalKind.Prediction 0.95) ] rows
              |> refusedWith "a prediction interval is stated for the gaussian family only"

              run
                  [ Predict(
                        predict
                            { ols with
                                Predictors = [ "x", Col "x"; "colour", Col "colour" ] }
                    ) ]
                  (newRows [ 2.5, "purple", "north" ])
              |> refusedWith "the term 'colour' has the level 'purple'"

              run [ Predict(predict { ols with GroupBy = [ "region" ] }) ] (newRows [ 2.5, "red", "east" ])
              |> refusedWith "the model has no group (east)"

              run
                  [ Predict(
                        predict
                            { ols with
                                Predictors = [ "z", Col "z" ] }
                    ) ]
                  rows
              |> refusedWith "z"

              run [ Predict(predict ols) ] (ok (run [ Derive("predicted", Lit(Int 1)) ] rows))
              |> refusedWith "the input already has a column 'predicted'"

              let unitless = table [ Field.create "x" FloatType, [ Float 1.0 ] ]

              run [ Predict(predict ols) ] unitless
              |> refusedWith "the term 'x' is in no unit; the fit's was in m"

              let coefs = ok (run [ Fit ols ] sales)

              let fromTable (fit: FitSpec) =
                  { predict fit with
                      Model = PredictModel.Coefficients(Embedded coefs) }

              run
                  [ Predict(
                        fromTable
                            { ols with
                                Predictors = [ "x", Col "x"; "w", Col "x" ] }
                    ) ]
                  rows
              |> refusedWith "the coefficient table has no term 'w'"

              run [ Predict(fromTable { ols with Intercept = false }) ] rows
              |> refusedWith "the coefficient table's term '(Intercept)' is not a term of the model"

              run
                  [ Predict(
                        fromTable
                            { ols with
                                Predictors = [ "x", Col "x"; "colour", Col "colour" ] }
                    ) ]
                  rows
              |> refusedWith "categorical"

              run [ Predict(fromTable ols |> withInterval IntervalKind.Confidence 0.95) ] rows
              |> refusedWith "an interval needs the training rows"

          testCase "a prediction is not maintained: the incremental seam re-evaluates it whole, by name"
          <| fun _ ->
              match Incremental.classifyStep true (Predict(predict ols)) with
              | FallBack(StepNotRowLocal verb) -> Expect.equal verb "predict" "the verb's name"
              | other -> failtestf "expected the named fall-back, got %A" other ]
