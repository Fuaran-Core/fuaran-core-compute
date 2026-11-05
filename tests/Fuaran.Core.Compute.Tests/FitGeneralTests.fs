module Fuaran.Compute.Tests.FitGeneralTests

// ============================================================================
//  Phase 600 (DECISIONS.md D58) — the general generalised linear model: the
//  binomial, quasi-binomial, inverse Gaussian and negative binomial families,
//  the logit, probit, cloglog, inverse, inverse-square and square-root links,
//  the pinned erfc and normal functions behind them, and Predict on every link.
//
//  The estimates, standard errors, deviances, dispersions and the negative
//  binomial's θ are held to an independent high-precision reference
//  computation, and the special functions to it in units in the last place:
//  `fixtures/glm-reference/general.py` and its output `general.json` (Python's
//  `decimal` at eighty digits, IRLS run to the maximum-likelihood estimate).
//  Then the wire, the refusals, the Summary's θ columns and Predict.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.LinearAlgebra

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private parse (s: string) : float =
    Double.Parse(s, Globalization.CultureInfo.InvariantCulture)

/// The test project's directory, found by climbing from the working directory or the binaries.
let private projectDir () : string =
    let rec climb (dir: string) (budget: int) =
        if budget = 0 then
            None
        elif File.Exists(Path.Combine(dir, "FitGeneralTests.fs")) then
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

let private document: Lazy<JsonDocument> =
    lazy
        (JsonDocument.Parse(File.ReadAllText(Path.Combine(projectDir (), "fixtures", "glm-reference", "general.json"))))

let private strings (e: JsonElement) : string[] =
    [| for v in e.EnumerateArray() -> v.GetString() |]

let private family (name: string) (theta: float option) : GlmFamily =
    match name with
    | "gaussian" -> GlmFamily.Gaussian
    | "poisson" -> GlmFamily.Poisson
    | "quasiPoisson" -> GlmFamily.QuasiPoisson
    | "gamma" -> GlmFamily.Gamma
    | "binomial" -> GlmFamily.Binomial
    | "quasiBinomial" -> GlmFamily.QuasiBinomial
    | "inverseGaussian" -> GlmFamily.InverseGaussian
    | "negativeBinomial" -> GlmFamily.NegativeBinomial theta
    | other -> failtestf "no family %s" other

let private link (name: string) : GlmLink =
    match name with
    | "identity" -> GlmLink.Identity
    | "log" -> GlmLink.Log
    | "logit" -> GlmLink.Logit
    | "probit" -> GlmLink.Probit
    | "cloglog" -> GlmLink.Cloglog
    | "inverse" -> GlmLink.Inverse
    | "inverseSquare" -> GlmLink.InverseSquare
    | "sqrt" -> GlmLink.Sqrt
    | other -> failtestf "no link %s" other

/// One reference case: its model, its data, and the reference's answers.
type private Case =
    { Name: string
      Family: GlmFamily
      Link: GlmLink
      X: Matrix
      Y: Vector<float>
      W: Vector<float>
      Coefficients: float[]
      StdErrors: float[]
      Deviance: float
      Dispersion: float
      Theta: (float * float) option }

let private cases: Lazy<Case list> =
    lazy
        [ for c in document.Value.RootElement.GetProperty("cases").EnumerateArray() ->
              let rows =
                  [| for r in c.GetProperty("x").EnumerateArray() -> strings r |> Array.map parse |]

              let theta =
                  match c.GetProperty("theta").ValueKind with
                  | JsonValueKind.Null -> None
                  | _ -> Some(c.GetProperty("theta").GetDouble())

              let num (name: string) = parse (c.GetProperty(name).GetString())

              { Name = c.GetProperty("name").GetString()
                Family = family (c.GetProperty("family").GetString()) theta
                Link = link (c.GetProperty("link").GetString())
                X = Matrix.init rows.Length rows[0].Length (fun i j -> rows[i][j])
                Y = Vector.adopt (strings (c.GetProperty "y") |> Array.map parse)
                W = Vector.adopt (strings (c.GetProperty "weights") |> Array.map parse)
                Coefficients = strings (c.GetProperty "coefficients") |> Array.map parse
                StdErrors = strings (c.GetProperty "stdErrors") |> Array.map parse
                Deviance = num "deviance"
                Dispersion = num "dispersion"
                Theta =
                  match c.TryGetProperty "thetaEstimate" with
                  | true, t -> Some(parse (t.GetString()), num "thetaStdError")
                  | _ -> None } ]

/// Within a relative `1e-6` of the reference, or `1e-9` of it absolutely: the deviance stopping rule
/// leaves an estimate within about the square root of its tolerance of the optimum.
let private near (what: string) (actual: float) (expected: float) =
    Expect.isTrue
        (abs (actual - expected) <= 1e-6 * abs expected + 1e-9)
        (sprintf "%s: %.17g against the reference's %.17g" what actual expected)

/// A control tight enough that the comparison measures the estimator, not where the default stopping
/// rule leaves it.
let private tight = (GlmControl.create 200 1e-13).Value

let private fitCase (control: GlmControl) (c: Case) : Result<GlmFit, LinAlgError> =
    Glm.fit c.Family c.Link control c.X c.Y c.W (Vector.adopt (Array.zeroCreate c.X.Rows))

// ---- the special functions, in units in the last place ----

let private ordered (x: float) : int64 =
    let b = BitConverter.DoubleToInt64Bits x
    if b < 0L then Int64.MinValue - b else b

let private ulps (a: float) (b: float) : int64 = abs (ordered a - ordered b)

/// The largest distance, in units in the last place, between `f` and the reference over its table.
let private worst (table: string) (f: float -> float) : int64 * string =
    [ for pair in document.Value.RootElement.GetProperty("special").GetProperty(table).EnumerateArray() ->
          let s = strings pair
          let x = parse s[0]
          ulps (f x) (parse s[1]), s[0] ]
    |> List.maxBy fst

// ---- the verb ----

let private table (cols: (Field * Cell list) list) : Table =
    { Schema = cols |> List.map fst
      Columns = cols |> List.map (fun (f, cells) -> KitColumn.create f.Name f.Type cells) }

let private column (name: string) (t: Table) : Cell list =
    match Table.tryColumn name t with
    | Some c -> Column.toCells c
    | None -> failtestf "no column %s in %A" name (t.Schema |> List.map _.Name)

let private floatOf (c: Cell) : float =
    match c with
    | Float v -> v
    | other -> failtestf "expected a float cell, got %A" other

/// Twelve rows: `x` from 1 to 12, a 0/1 outcome `b` whose classes overlap along `x`, counts `k`, and a
/// positive `y` falling along `x`.
let private rows: Table =
    let xs = [ for i in 1..12 -> float i ]
    let bs = [ 0.0; 0.0; 1.0; 0.0; 0.0; 1.0; 0.0; 1.0; 1.0; 0.0; 1.0; 1.0 ]
    let ks = [ 0; 2; 1; 4; 0; 7; 3; 1; 9; 2; 12; 6 ]

    table
        [ Field.create "x" FloatType, [ for x in xs -> Float x ]
          Field.create "b" FloatType, [ for b in bs -> Float b ]
          Field.create "k" IntType, [ for k in ks -> Int k ]
          Field.create "y" FloatType, [ for x in xs -> Float(10.0 / x + 0.3 * float ((int x * 7) % 5)) ] ]

let private spec (response: string) (family: FitFamily) (link: FitLink) : FitSpec =
    { FitSpec.ols response [ "x", Col "x" ] with
        Family = family
        Link = link }

let private verbTests =
    testList
        "the verb"
        [ test "a pair outside the admitted table is refused by name when the wire decodes" {
              let wire =
                  DataFrameCodec.encodePipeline [ Fit(spec "b" FitFamily.Binomial FitLink.Log) ]

              match DataFrameCodec.decodePipeline wire with
              | Error e ->
                  Expect.stringContains
                      (ColumnCodec.errorString e)
                      "the binomial family is not fitted under the log link"
                      "the refusal names the pair"
              | Ok p -> failtestf "decoded a pair the estimator does not fit: %A" p

              // In memory the step refuses it too.
              Expect.equal
                  (DataFrame.evalPipeline [ Fit(spec "b" FitFamily.Binomial FitLink.Log) ] rows)
                  (Error(UnsupportedFit("binomial", "log")))
                  "the step's refusal"
          }

          test "every admitted family and link round-trips on the wire, a fixed theta as a member" {
              let families =
                  [ FitFamily.Gaussian
                    FitFamily.Poisson
                    FitFamily.Gamma
                    FitFamily.Tweedie 1.5
                    FitFamily.QuasiPoisson
                    FitFamily.Binomial
                    FitFamily.QuasiBinomial
                    FitFamily.InverseGaussian
                    FitFamily.NegativeBinomial None
                    FitFamily.NegativeBinomial(Some 2.5) ]

              let links =
                  [ FitLink.Identity
                    FitLink.Log
                    FitLink.Logit
                    FitLink.Probit
                    FitLink.Cloglog
                    FitLink.Inverse
                    FitLink.InverseSquare
                    FitLink.Sqrt ]

              let mutable admitted = 0

              for f in families do
                  for l in links do
                      let p = [ Fit(spec "k" f l) ]
                      let wire = DataFrameCodec.encodePipeline p

                      match DataFrameCodec.decodePipeline wire with
                      | Ok back ->
                          admitted <- admitted + 1
                          Expect.equal back p (sprintf "%A under %A round-trips" f l)
                      | Error _ -> ()

                      Expect.equal
                          (wire.Contains "\"theta\"")
                          (f = FitFamily.NegativeBinomial(Some 2.5))
                          "only a fixed theta is a member"

              // Gaussian 3, Poisson, quasi-Poisson and Gamma 3 each, Tweedie 1, the binomials 3 each, inverse
              // Gaussian 4, and the negative binomial 3 for each of its two forms.
              Expect.equal admitted 29 "the admitted table"
          }

          test "an estimated negative binomial states theta in its Summary, and no other family does" {
              let summary (f: FitFamily) =
                  match
                      DataFrame.evalPipeline
                          [ Fit
                                { spec "k" f FitLink.Log with
                                    Output = FitOutput.Summary } ]
                          rows
                  with
                  | Ok t -> t
                  | Error e -> failtestf "%A refused: %A" f e

              let nb = summary (FitFamily.NegativeBinomial None)
              let names = nb.Schema |> List.map _.Name

              Expect.equal
                  (List.rev names |> List.take 3)
                  [ "alternations"; "theta_std_error"; "theta" ]
                  "the theta columns, last"

              Expect.isGreaterThan (floatOf (column "theta" nb).Head) 0.0 "a positive theta"

              let fixedTheta = summary (FitFamily.NegativeBinomial(Some 2.0))
              Expect.equal (column "theta" fixedTheta) [ Float 2.0 ] "a fixed theta is stated as given"
              Expect.equal (column "theta_std_error" fixedTheta) [ Null ] "with no error"

              let poisson = summary FitFamily.Poisson
              Expect.isFalse (poisson.Schema |> List.exists (fun f -> f.Name = "theta")) "Poisson states none"
          }

          test "Predict answers inside the mean's range on every link, its interval's ends mapped" {
              for f, l, response in
                  [ FitFamily.Binomial, FitLink.Logit, "b"
                    FitFamily.Binomial, FitLink.Probit, "b"
                    FitFamily.Binomial, FitLink.Cloglog, "b"
                    FitFamily.Gamma, FitLink.Inverse, "y"
                    FitFamily.InverseGaussian, FitLink.InverseSquare, "y"
                    FitFamily.Poisson, FitLink.Sqrt, "k"
                    FitFamily.Gaussian, FitLink.Log, "y" ] do
                  let p =
                      { PredictSpec.ofTraining (spec response f l) (Embedded rows) with
                          Interval =
                              Some
                                  { Kind = IntervalKind.Confidence
                                    Level = Slot.Lit 0.95 } }

                  match DataFrame.evalPipeline [ Predict p ] rows with
                  | Error e -> failtestf "%A %A refused: %A" f l e
                  | Ok t ->
                      // A null end is unbounded on its side (D58 item 7).
                      let cells (name: string) (unbounded: float) =
                          column name t
                          |> List.map (fun c ->
                              match c with
                              | Float v -> v
                              | _ -> unbounded)

                      let mu = cells "predicted" nan
                      let lo = cells "lower" -infinity
                      let hi = cells "upper" infinity

                      for i in 0 .. mu.Length - 1 do
                          Expect.isTrue (lo[i] <= mu[i] && mu[i] <= hi[i]) (sprintf "%A %A row %d: ordered" f l i)

                          if f = FitFamily.Binomial then
                              Expect.isTrue (lo[i] > 0.0 && hi[i] < 1.0) (sprintf "%A row %d: inside (0, 1)" l i)
                          else
                              Expect.isTrue (lo[i] >= 0.0) (sprintf "%A %A row %d: not negative" f l i)
          }

          test "a prediction interval is the identity link's alone" {
              let p =
                  { PredictSpec.ofTraining (spec "y" FitFamily.Gaussian FitLink.Log) (Embedded rows) with
                      Interval =
                          Some
                              { Kind = IntervalKind.Prediction
                                Level = Slot.Lit 0.95 } }

              match DataFrame.evalPipeline [ Predict p ] rows with
              | Error(FitError detail) -> Expect.stringContains detail "identity link only" "named"
              | other -> failtestf "a prediction interval under the log link: %A" other
          } ]

[<Tests>]
let tests =
    testList
        "Phase 600: the general GLM (D58)"
        [ testList
              "the pinned special functions, against the 80-digit reference"
              [ test "erfc is within one ulp" {
                    let u, at = worst "erfc" Elementary.erfc
                    Expect.isLessThanOrEqual u 1L (sprintf "erfc: %d ulps at %s" u at)
                }
                test "the normal distribution function is within two ulps" {
                    let u, at = worst "normalCdf" Elementary.normalCdf
                    Expect.isLessThanOrEqual u 2L (sprintf "normalCdf: %d ulps at %s" u at)
                }
                test "the normal density is within two ulps" {
                    let u, at = worst "normalDensity" Elementary.normalDensity
                    Expect.isLessThanOrEqual u 2L (sprintf "normalDensity: %d ulps at %s" u at)
                }
                test "the normal quantile is within four ulps" {
                    let u, at = worst "normalQuantile" Elementary.normalQuantile
                    Expect.isLessThanOrEqual u 4L (sprintf "normalQuantile: %d ulps at %s" u at)
                } ]

          testList
              "every admitted family and link pair agrees with the reference"
              [ for c in cases.Value ->
                    test c.Name {
                        let g = ok (fitCase tight c)
                        let coef = g.Coefficients |> List.map Option.get |> List.toArray
                        let se = g.StdErrors |> List.map Option.get |> List.toArray

                        for j in 0 .. coef.Length - 1 do
                            near (sprintf "%s coefficient %d" c.Name j) coef[j] c.Coefficients[j]
                            near (sprintf "%s standard error %d" c.Name j) se[j] c.StdErrors[j]

                        near (c.Name + " deviance") g.Deviance c.Deviance
                        near (c.Name + " dispersion") g.Dispersion.Value c.Dispersion

                        match c.Theta with
                        | Some(theta, thetaSe) ->
                            near (c.Name + " theta") g.Theta.Value theta
                            near (c.Name + " theta standard error") g.ThetaStdError.Value thetaSe
                            Expect.isGreaterThan g.Alternations 0 "an estimated theta alternates"
                        | None -> Expect.equal g.ThetaStdError None "a fixed or absent theta has no error"
                    } ]

          testList
              "the default control lands near the reference"
              [ for c in cases.Value ->
                    test c.Name {
                        let g = ok (fitCase GlmControl.defaults c)

                        g.Coefficients
                        |> List.iteri (fun j b ->
                            let e = c.Coefficients[j]

                            Expect.isTrue
                                (abs (b.Value - e) <= 1e-4 * abs e + 1e-6)
                                (sprintf "%s coefficient %d: %.17g against %.17g" c.Name j b.Value e))
                    } ]

          verbTests ]
