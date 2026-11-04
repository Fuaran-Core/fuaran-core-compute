module Fuaran.Compute.Tests.FitFamiliesTests

// ============================================================================
//  Phase 598 (DECISIONS.md D57) — Tweedie and quasi-Poisson under the log link,
//  and the heteroskedasticity-consistent (sandwich) standard errors HC0 to HC3.
//
//  The estimates, dispersions, deviances and every kind of standard error are
//  held to an independent high-precision reference computation: the script and
//  its output under `fixtures/glm-reference/` (Python's `decimal` at sixty
//  digits, the normal equations by Gauss-Jordan elimination, IRLS run to the
//  maximum-likelihood estimate). Then the default variant, the wire member, the
//  refusals, the units, and the maintained fit with robust errors.
// ============================================================================

open System
open System.IO
open System.Text.Json
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

let private fit (spec: FitSpec) (t: Table) = DataFrame.evalPipeline [ Fit spec ] t

let private column (name: string) (t: Table) : Cell list =
    match Table.tryColumn name t with
    | Some c -> Column.toCells c
    | None -> failtestf "no column %s in %A" name (t.Schema |> List.map _.Name)

let private floatOf (c: Cell) : float =
    match c with
    | Float v -> v
    | other -> failtestf "expected a float cell, got %A" other

let private floats (name: string) (t: Table) : float list = column name t |> List.map floatOf

// ---- the reference computation ----

/// The test project's directory, found by climbing from the working directory or the binaries.
let private projectDir () : string =
    let rec climb (dir: string) (budget: int) =
        if budget = 0 then
            None
        elif File.Exists(Path.Combine(dir, "FitFamiliesTests.fs")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start ->
        climb start 12
        |> Option.orElse (climb (Path.Combine(start, "tests", "Fuaran.Core.Compute.Tests")) 1))
    |> Option.defaultWith (fun () -> failtest "the test project's directory is not reachable")

type private Reference =
    { Name: string
      Coefficients: float[]
      Dispersion: float option
      Deviance: float option
      Errors: Map<string, float[]> }

let private references: Lazy<Map<string, Reference>> =
    lazy
        (let path =
            Path.Combine(projectDir (), "fixtures", "glm-reference", "reference.json")

         use doc = JsonDocument.Parse(File.ReadAllText path)

         let nums (e: JsonElement) =
             [| for v in e.EnumerateArray() -> Double.Parse(v.GetString(), Globalization.CultureInfo.InvariantCulture) |]

         let num (c: JsonElement) (name: string) =
             match c.TryGetProperty name with
             | true, v -> Some(Double.Parse(v.GetString(), Globalization.CultureInfo.InvariantCulture))
             | _ -> None

         [ for c in doc.RootElement.GetProperty("cases").EnumerateArray() ->
               let name = c.GetProperty("name").GetString()

               name,
               { Name = name
                 Coefficients = nums (c.GetProperty "coefficients")
                 Dispersion = num c "dispersion"
                 Deviance = num c "deviance"
                 Errors =
                   [ "classical", "stdErrors"
                     "hc0", "hc0"
                     "hc1", "hc1"
                     "hc2", "hc2"
                     "hc3", "hc3" ]
                   |> List.map (fun (k, m) -> k, nums (c.GetProperty m))
                   |> Map.ofList } ]
         |> Map.ofList)

/// Within a relative `1e-7` of the reference, or `1e-9` of it absolutely (an estimate whose true value
/// is zero).
let private near (what: string) (actual: float) (expected: float) =
    Expect.isTrue
        (abs (actual - expected) <= 1e-7 * abs expected + 1e-9)
        (sprintf "%s: %.17g against the reference's %.17g" what actual expected)

/// A control tight enough that the comparison measures the estimator, not where the default stopping
/// rule leaves it (the reference is the maximum-likelihood estimate itself).
let private tight: FitControl =
    { MaxIter = Slot.Lit 100
      Tol = Slot.Lit 1e-13 }

let private variants =
    [ "classical", FitStdErrors.Classical
      "hc0", FitStdErrors.HC0
      "hc1", FitStdErrors.HC1
      "hc2", FitStdErrors.HC2
      "hc3", FitStdErrors.HC3 ]

/// Hold one spec over one table to its reference: the estimates, every kind of standard error, and
/// the dispersion and deviance where the reference states them.
let private holdToReference (caseName: string) (spec: FitSpec) (t: Table) =
    let r = references.Value[caseName]

    for key, stdErrors in variants do
        let coef =
            ok (
                fit
                    { spec with
                        Control = tight
                        StdErrors = stdErrors }
                    t
            )

        List.iteri (fun j a -> near (sprintf "%s estimate %d" caseName j) a r.Coefficients[j]) (floats "estimate" coef)

        let expected: float[] = Map.find key r.Errors

        List.iteri (fun j a -> near (sprintf "%s %s error %d" caseName key j) a expected[j]) (floats "std_error" coef)

    let summary =
        ok (
            fit
                { spec with
                    Control = tight
                    Output = FitOutput.Summary }
                t
        )

    r.Dispersion
    |> Option.iter (near (caseName + " dispersion") (floatOf (List.head (column "dispersion" summary))))

    r.Deviance
    |> Option.iter (near (caseName + " deviance") (floatOf (List.head (column "deviance" summary))))

// ---- the fixtures (the reference script builds the same rows) ----

/// Dobson's (1990) randomised controlled trial, R's `?glm` example.
let private dobson: Table =
    let counts = [ 18; 17; 15; 20; 10; 20; 25; 13; 12 ]

    table
        [ Field.create "outcome" StringType, [ for i in 0..8 -> Str(string (i % 3 + 1)) ]
          Field.create "treatment" StringType, [ for i in 0..8 -> Str(string (i / 3 + 1)) ]
          Field.create "counts" IntType, [ for c in counts -> Int c ] ]

/// Twelve rows, four of them zeros: a Tweedie compound Poisson–gamma response.
let private zeros: Table =
    let ys = [ 0.0; 1.2; 0.0; 3.4; 2.2; 0.0; 5.1; 4.4; 0.0; 7.9; 6.3; 9.0 ]

    table
        [ Field.create "id" StringType, [ for i in 0..11 -> Str("z" + string i) ]
          Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ for i in 0..11 -> Float(float (i + 1)) ]
          Field.create "y" FloatType |> Field.withUnit (unitOf "s"), [ for v in ys -> Float v ] ]

/// Ten rows whose noise grows along `x`, with prior weights, one of them zero.
let private hetero: Table =
    table
        [ Field.create "id" StringType, [ for i in 0..9 -> Str("h" + string i) ]
          Field.create "g" StringType, [ for i in 0..9 -> Str(if i % 2 = 0 then "a" else "b") ]
          Field.create "x" FloatType, [ for i in 0..9 -> Float(float (i + 1)) ]
          Field.create "y" FloatType,
          [ for i in 0..9 ->
                Float(
                    2.0
                    + float (i + 1) / 2.0
                    + float ((37 * i) % 11 - 5) / 10.0 * (1.0 + float i / 3.0)
                ) ]
          Field.create "w" FloatType, [ for i in 0..9 -> Float(if i = 6 then 0.0 else 1.0 + float (i % 4) / 2.0) ] ]

let private logLink (family: FitFamily) (response: string) (terms: (string * ColExpr) list) : FitSpec =
    { FitSpec.ols response terms with
        Family = family
        Link = FitLink.Log }

let private dobsonTerms = [ "outcome", Col "outcome"; "treatment", Col "treatment" ]

[<Tests>]
let tests =
    testList
        "Fit, Tweedie, quasi-Poisson and the sandwich (Phase 598)"
        [ testCase "Poisson on Dobson's trial: estimates and HC0 to HC3 against the reference computation"
          <| fun _ -> holdToReference "dobson-poisson" (logLink FitFamily.Poisson "counts" dobsonTerms) dobson

          testCase "quasi-Poisson on Dobson's trial: Poisson's estimates, Pearson's dispersion, the reference's errors"
          <| fun _ -> holdToReference "dobson-quasipoisson" (logLink FitFamily.QuasiPoisson "counts" dobsonTerms) dobson

          testCase "Tweedie p = 1.5 on Dobson's trial against the reference computation"
          <| fun _ -> holdToReference "dobson-tweedie-1.5" (logLink (FitFamily.Tweedie 1.5) "counts" dobsonTerms) dobson

          testCase "Tweedie p = 1.5 and p = 1.3 over a response with zeros against the reference computation"
          <| fun _ ->
              holdToReference "zeros-tweedie-1.5" (logLink (FitFamily.Tweedie 1.5) "y" [ "x", Col "x" ]) zeros
              holdToReference "zeros-tweedie-1.3" (logLink (FitFamily.Tweedie 1.3) "y" [ "x", Col "x" ]) zeros

          testCase "least squares, unweighted and weighted with a zero weight: HC0 to HC3 against the reference"
          <| fun _ ->
              holdToReference "hetero-ols" (FitSpec.ols "y" [ "x", Col "x" ]) hetero

              holdToReference
                  "hetero-wls"
                  { FitSpec.ols "y" [ "x", Col "x" ] with
                      Weights = Some "w" }
                  hetero

          testCase "quasi-Poisson's estimates are Poisson's byte for byte under the default control"
          <| fun _ ->
              let p = ok (fit (logLink FitFamily.Poisson "counts" dobsonTerms) dobson)
              let q = ok (fit (logLink FitFamily.QuasiPoisson "counts" dobsonTerms) dobson)
              Expect.equal (column "estimate" q) (column "estimate" p) "the same bits"

          testCase "the default robust variant is HC3, in the API and on the wire"
          <| fun _ ->
              Expect.equal FitStdErrors.Robust FitStdErrors.HC3 "the API's default"

              let spec =
                  { FitSpec.ols "y" [ "x", Col "x" ] with
                      StdErrors = FitStdErrors.HC3 }

              let wire = DataFrameCodec.encodePipeline [ Fit spec ]
              Expect.stringContains wire "\"stdErrors\":\"hc3\"" "HC3 is spelled hc3"

              let asRobust = wire.Replace("\"stdErrors\":\"hc3\"", "\"stdErrors\":\"robust\"")
              Expect.equal (DataFrameCodec.decodePipeline asRobust) (Ok [ Fit spec ]) "robust decodes to HC3"

              let classical =
                  DataFrameCodec.encodePipeline [ Fit(FitSpec.ols "y" [ "x", Col "x" ]) ]

              Expect.isFalse (classical.Contains "stdErrors") "classical errors emit no member"

              let explicitClassical =
                  classical.Replace("\"output\":", "\"stdErrors\":\"classical\",\"output\":")

              Expect.equal
                  (DataFrameCodec.decodePipeline explicitClassical)
                  (Ok [ Fit(FitSpec.ols "y" [ "x", Col "x" ]) ])
                  "classical decodes to classical"

              Expect.isError
                  (DataFrameCodec.decodePipeline (wire.Replace("\"hc3\"", "\"hc4\"")))
                  "an unknown variant is refused"

              for v in [ FitStdErrors.HC0; FitStdErrors.HC1; FitStdErrors.HC2; FitStdErrors.HC3 ] do
                  let p =
                      [ Fit
                            { spec with
                                StdErrors = v
                                Family = FitFamily.Tweedie 1.5
                                Link = FitLink.Log } ]

                  Expect.equal (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p)) (Ok p) "round trip"

          testCase "robust errors leave the estimates and the summary alone"
          <| fun _ ->
              let spec = logLink (FitFamily.Tweedie 1.5) "y" [ "x", Col "x" ]
              let classical = ok (fit spec zeros)

              let robust =
                  ok (
                      fit
                          { spec with
                              StdErrors = FitStdErrors.HC1 }
                          zeros
                  )

              Expect.equal (column "estimate" robust) (column "estimate" classical) "the estimates"
              Expect.equal (column "unit" robust) (column "unit" classical) "the units"
              Expect.notEqual (column "std_error" robust) (column "std_error" classical) "the errors differ"

              Expect.equal
                  (ok (
                      fit
                          { spec with
                              Output = FitOutput.Summary
                              StdErrors = FitStdErrors.HC3 }
                          zeros
                  ))
                  (ok (fit { spec with Output = FitOutput.Summary } zeros))
                  "the summary"

          testCase "the refusals: a Tweedie power, a family's range, a pairing, a ridge with robust errors, Predict"
          <| fun _ ->
              let tw p =
                  logLink (FitFamily.Tweedie p) "y" [ "x", Col "x" ]

              for p in [ 0.5; nan; infinity ] do
                  match fit (tw p) zeros with
                  | Error(FitError detail) -> Expect.stringContains detail "tweedie power" (sprintf "power %g" p)
                  | other -> failtestf "power %g: %A" p other

              for p in [ 0.0; 1.0; 1.5; 2.0 ] do
                  Expect.isFalse
                      (match fit (tw p) zeros with
                       | Error(FitError detail) -> detail.Contains "tweedie power"
                       | _ -> false)
                      (sprintf "power %g is a power Tweedie has" p)

              match fit (tw 2.5) zeros with
              | Error(FitError detail) -> Expect.stringContains detail "row 0" "a zero under p > 2 names its row"
              | other -> failtestf "a zero response at p = 2.5: %A" other

              Expect.equal
                  (fit
                      { FitSpec.ols "y" [ "x", Col "x" ] with
                          Family = FitFamily.QuasiPoisson }
                      zeros)
                  (Error(UnsupportedFit("quasiPoisson", "identity")))
                  "quasi-Poisson under identity"

              match
                  fit
                      { FitSpec.ols "y" [ "x", Col "x" ] with
                          Ridge = Some(Slot.Lit 0.5)
                          StdErrors = FitStdErrors.HC3 }
                      hetero
              with
              | Error(FitError detail) -> Expect.stringContains detail "ridge" "a ridge fit with robust errors"
              | other -> failtestf "a ridge fit with robust errors: %A" other

              Expect.isOk
                  (fit
                      { FitSpec.ols "y" [ "x", Col "x" ] with
                          Ridge = Some(Slot.Lit 0.0)
                          StdErrors = FitStdErrors.HC3 }
                      hetero)
                  "a zero penalty folds nothing"

              let model =
                  { FitSpec.ols "y" [ "x", Col "x" ] with
                      StdErrors = FitStdErrors.HC0 }

              for m in
                  [ PredictModel.Training(Embedded hetero)
                    PredictModel.Coefficients(Embedded hetero) ] do
                  match
                      DataFrame.evalPipeline
                          [ Predict
                                { PredictSpec.ofTraining model (Embedded hetero) with
                                    Model = m } ]
                          hetero
                  with
                  | Error(FitError detail) -> Expect.stringContains detail "robust" "Predict states classical errors"
                  | other -> failtestf "Predict over a robust spec: %A" other

          testCase
              "units: a log-link coefficient is 1 / unit(x); the deviance is unit(y)^(2 - p) where that is integral"
          <| fun _ ->
              let units (family: FitFamily) =
                  let coef = ok (fit (logLink family "y" [ "x", Col "x" ]) zeros)

                  let summary =
                      ok (
                          fit
                              { logLink family "y" [ "x", Col "x" ] with
                                  Output = FitOutput.Summary }
                              zeros
                      )

                  column "unit" coef, (summary.Schema |> List.find (fun f -> f.Name = "deviance")).Unit

              let perMetre = [ Null; Str "/m" ]

              let coefUnits, devQuasi = units FitFamily.QuasiPoisson
              Expect.equal coefUnits perMetre "quasi-Poisson's coefficients"
              Expect.equal (devQuasi |> Option.map Unit.render) (Some "s") "quasi-Poisson's deviance is unit(y)"

              let coefUnits, devTweedie = units (FitFamily.Tweedie 1.5)
              Expect.equal coefUnits perMetre "Tweedie's coefficients"
              Expect.equal devTweedie None "a fractional power of a unit is not stated"

              let _, devOne = units (FitFamily.Tweedie 1.0)
              Expect.equal (devOne |> Option.map Unit.render) (Some "s") "Tweedie at p = 1 is unit(y)"

          testCase "incremental: a robust least-squares fit is still maintained, byte-equal to a full evaluation"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"

              for spec in
                  [ { FitSpec.ols "y" [ "x", Col "x" ] with
                        GroupBy = [ "g" ]
                        Weights = Some "w"
                        StdErrors = FitStdErrors.HC3 }
                    { logLink FitFamily.QuasiPoisson "y" [ "x", Col "x" ] with
                        GroupBy = [ "g" ]
                        StdErrors = FitStdErrors.HC2 } ] do
                  let p = [ Fit spec ]
                  let before = DataFrame.evalPipeline [ Transform.limit 7 0 ] hetero |> ok
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before hetero)
                  let decoded = ok (IncrementalCodec.decode (ok (IncrementalCodec.encode state)))

                  for s in [ state; decoded ] do
                      let refreshed = ok (Incremental.refreshOn idw p s delta hetero)

                      Expect.equal
                          (ColumnCodec.encode (Embedded(Incremental.result refreshed)))
                          (ColumnCodec.encode (Embedded(ok (DataFrame.evalPipeline p hetero))))
                          (sprintf "%A: byte-equal" spec.Family) ]
