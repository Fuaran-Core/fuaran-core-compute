module Fuaran.Compute.Tests.GlmSolversTests

// ============================================================================
//  Phase 634 (DECISIONS.md D80): `Glm` calls the solvers. Its IRLS is one
//  call of `Damped.iterate` and θ's Newton one call of `Root.bracketedNewton`;
//  the hand-written loops are gone.
//
//  Phase 633's reproduction law (D77 item 9) held the solvers to those loops
//  bit for bit by transcribing what they called. With the loops deleted, the
//  transcription would only repeat `Glm`'s own calls, so it is replaced by
//  what it established: the bits `Glm.fit` answered BEFORE the swap, on every
//  case of the reference corpus (`fixtures/glm-reference/general.json`) and on
//  the authored cases that halve a step and that are refused, under three
//  controls, recorded in `fixtures/glm-reference/glm-bits.txt` by the
//  pre-swap build and compared line for line. The θ cases where the bracketed
//  Newton decides differently from the old loop are held here too.
//
//  The record is regenerated only with a decision that moves those bits:
//    CORE_APPROVE_GLM_BITS=1 dotnet run --project tests/Fuaran.Core.Compute.Tests -- --filter "Phase 634 - Glm calls the solvers"
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression

let private hex (x: float) =
    BitConverter.DoubleToInt64Bits(x).ToString("x16")

// ---- the cases ---------------------------------------------------------------------------------

type private Case =
    { Name: string
      Family: GlmFamily
      Link: GlmLink
      X: Matrix
      Y: float[]
      W: float[] }

let private parse (s: string) : float =
    Double.Parse(s, Globalization.CultureInfo.InvariantCulture)

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

/// Every case of the reference corpus `FitGeneralTests` holds `Glm.fit` to R with.
let private referenceCases () : Case list =
    let path =
        Snapshots.repoFile (
            Path.Combine("tests", "Fuaran.Core.Compute.Tests", "fixtures", "glm-reference", "general.json")
        )

    use doc = JsonDocument.Parse(File.ReadAllText path)

    let strings (e: JsonElement) =
        [| for v in e.EnumerateArray() -> parse (v.GetString()) |]

    [ for c in doc.RootElement.GetProperty("cases").EnumerateArray() ->
          let rows = [| for r in c.GetProperty("x").EnumerateArray() -> strings r |]

          let theta =
              match c.GetProperty("theta").ValueKind with
              | JsonValueKind.Null -> None
              | _ -> Some(c.GetProperty("theta").GetDouble())

          { Name = c.GetProperty("name").GetString()
            Family = family (c.GetProperty("family").GetString()) theta
            Link = link (c.GetProperty("link").GetString())
            X = Matrix.init rows.Length rows[0].Length (fun i j -> rows[i][j])
            Y = strings (c.GetProperty "y")
            W = strings (c.GetProperty "weights") } ]

/// Authored cases for the paths the reference corpus does not reach: Poisson fits under the identity
/// link with a step that is halved back (found by a seeded search over small designs, then written
/// down), fits whose first step leaves the mean's range, a separated binomial and counts no more
/// dispersed than Poisson's. No case here exhausts its ten halvings: that refusal is held by the solver's
/// own vectors and laws, not against `Glm`.
let private authoredCases () : Case list =
    let design (xs: float list) =
        let a = List.toArray xs
        Matrix.init a.Length 2 (fun i j -> if j = 0 then 1.0 else a[i])

    let ones (n: int) = Array.create n 1.0

    [ { Name = "authored-poisson-identity-halved"
        Family = GlmFamily.Poisson
        Link = GlmLink.Identity
        X = design [ 0.0; 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 ]
        Y = [| 15.0; 7.0; 17.0; 2.0; 24.0; 1.0; 37.0 |]
        W = ones 7 }
      { Name = "authored-poisson-identity-halved-late"
        Family = GlmFamily.Poisson
        Link = GlmLink.Identity
        X = design [ 0.0; 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 ]
        Y = [| 6.0; 6.0; 7.0; 5.0; 1.0; 40.0; 34.0 |]
        W = ones 7 }
      { Name = "authored-poisson-identity-steep"
        Family = GlmFamily.Poisson
        Link = GlmLink.Identity
        X = design [ 0.0; 1.0; 2.0; 3.0; 4.0; 5.0; 6.0; 7.0 ]
        Y = [| 0.0; 0.0; 0.0; 1.0; 0.0; 9.0; 30.0; 80.0 |]
        W = ones 8 }
      { Name = "authored-poisson-identity-falling"
        Family = GlmFamily.Poisson
        Link = GlmLink.Identity
        X = design [ 0.0; 1.0; 2.0; 3.0; 4.0; 5.0 ]
        Y = [| 40.0; 12.0; 3.0; 0.0; 0.0; 0.0 |]
        W = ones 6 }
      { Name = "authored-gamma-inverse-wide"
        Family = GlmFamily.Gamma
        Link = GlmLink.Inverse
        X = design [ 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 ]
        Y = [| 0.01; 50.0; 0.02; 80.0; 0.5; 200.0 |]
        W = ones 6 }
      { Name = "authored-binomial-separated"
        Family = GlmFamily.Binomial
        Link = GlmLink.Logit
        X = design [ 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 ]
        Y = [| 0.0; 0.0; 0.0; 1.0; 1.0; 1.0 |]
        W = ones 6 }
      { Name = "authored-negative-binomial-underdispersed"
        Family = GlmFamily.NegativeBinomial None
        Link = GlmLink.Log
        X = design [ 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 ]
        Y = [| 2.0; 2.0; 2.0; 2.0; 2.0; 2.0 |]
        W = ones 6 } ]

let private controls: (string * GlmControl) list =
    [ "defaults", GlmControl.defaults
      "tight", (GlmControl.create 200 1e-13).Value
      "three iterations", (GlmControl.create 3 1e-8).Value ]

/// One line per case and control: the outcome, and for a fit the bits of every compared cell.
let private render (c: Case) (label: string) (control: GlmControl) : string =
    let o = Array.zeroCreate c.X.Rows

    match Glm.fit c.Family c.Link control c.X (Vector.adopt c.Y) (Vector.adopt c.W) (Vector.adopt o) with
    | Error e -> sprintf "%s | %s | error %A" c.Name label e
    | Ok g ->
        let floats (xs: float seq) = xs |> Seq.map hex |> String.concat ","

        let coefficients =
            g.Coefficients
            |> List.map (Option.map hex >> Option.defaultValue "aliased")
            |> String.concat ","

        let optional (v: float option) =
            v |> Option.map hex |> Option.defaultValue "-"

        sprintf
            "%s | %s | ok coefficients=%s means=%s predictor=%s deviance=%s iterations=%d theta=%s thetaError=%s alternations=%d"
            c.Name
            label
            coefficients
            (floats (Vector.toArray g.Fitted))
            (floats (Vector.toArray g.LinearPredictor))
            (hex g.Deviance)
            g.Iterations
            (optional g.Theta)
            (optional g.ThetaStdError)
            g.Alternations

let private recordPath () =
    Snapshots.repoFile (Path.Combine("tests", "Fuaran.Core.Compute.Tests", "fixtures", "glm-reference", "glm-bits.txt"))

let private rendered () : string list =
    [ for c in referenceCases () @ authoredCases () do
          for label, control in controls -> render c label control ]

/// The negative binomial with θ estimated over `counts` at `x_i = i / 10`.
let private negbin (counts: float list) =
    let y = List.toArray counts
    let n = y.Length
    let x = Matrix.init n 2 (fun i j -> if j = 0 then 1.0 else float i / 10.0)

    Glm.fit
        (GlmFamily.NegativeBinomial None)
        GlmLink.Log
        GlmControl.defaults
        x
        (Vector.adopt y)
        (Vector.adopt (Array.create n 1.0))
        (Vector.adopt (Array.zeroCreate n))

[<Tests>]
let tests =
    testList
        "Phase 634 - Glm calls the solvers"
        [ testCase "Glm.fit answers the bits it answered before its loops became calls of the solvers"
          <| fun () ->
              let lines = rendered ()
              let path = recordPath ()

              if Environment.GetEnvironmentVariable "CORE_APPROVE_GLM_BITS" = "1" then
                  File.WriteAllText(path, String.concat "\n" lines + "\n")

              let recorded =
                  File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                  |> List.ofArray

              Expect.equal lines.Length recorded.Length "one line per case and control"

              for now, was in List.zip lines recorded do
                  Expect.equal now was "the fit's bits, its iteration count and its refusal are unchanged"

              // The record is worth its green only if it holds fits, refusals, halvings and estimated θ.
              let count (s: string) =
                  recorded |> List.filter (fun l -> l.Contains s) |> List.length

              Expect.isGreaterThan (count "| ok ") 40 "most lines are fits"
              Expect.isGreaterThan (count "| error ") 5 "the cut runs are recorded as refusals"

              Expect.isGreaterThanOrEqual
                  (recorded
                   |> List.filter (fun l -> l.Contains "alternations=" && not (l.Contains "alternations=0"))
                   |> List.length)
                  2
                  "θ is estimated"

              Expect.isGreaterThan (count "authored-poisson-identity-halved") 0 "a run that halves is recorded"

          testCase "a rule that stops elsewhere moves the recorded cells, so the comparison sees a different decision"
          <| fun () ->
              let c = referenceCases () |> List.find (fun c -> c.Name = "dobson-poisson-sqrt")
              let loose = render c "control" (GlmControl.create 200 1e-2).Value
              let tight = render c "control" (GlmControl.create 200 1e-10).Value
              Expect.notEqual loose tight "a tighter rule stops at another fit"

          testCase "θ on data the old loop's reflection could not settle is estimated (D80)"
          <| fun () ->
              // Fifteen overdispersed counts whose θ lies near 0.4: the unbracketed Newton overshot below
              // zero, reflected and reached its cap, so the fit was refused at the first alternation.
              match negbin [ 0.0; 8.0; 0.0; 6.0; 16.0; 4.0; 0.0; 7.0; 9.0; 0.0; 0.0; 0.0; 0.0; 5.0; 2.0 ] with
              | Ok g ->
                  let theta = g.Theta.Value
                  Expect.isTrue (theta > 0.3 && theta < 0.6) (sprintf "θ near 0.4, not %g" theta)
                  Expect.isGreaterThan g.Alternations 0 "the alternation ran"
              | Error e -> failtestf "the fit was refused: %A" e

          testCase "counts the means reproduce exactly have no θ: a score of zero at the bracket's end is refused"
          <| fun () ->
              match negbin (List.replicate 12 3.0) with
              | Error(LinAlgError.ThetaNotConverged 0) -> ()
              | other -> failtestf "expected ThetaNotConverged 0, got %A" (other |> Result.map (fun g -> g.Theta)) ]
