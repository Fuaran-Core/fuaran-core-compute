module Fuaran.Compute.Tests.SolversReproductionTests

// ============================================================================
//  Phase 633 — the reproduction law (DECISIONS.md D77 item 9): the solvers,
//  given the regression package's own step, test, objective and halving rule,
//  answer the bits its hand-written loops answer.
//
//  `Glm`'s loops are private and Phase 634 is the phase that rewires them, so
//  this file does not touch them. It TRANSCRIBES what the loops call — the
//  working response and weights, the solve, the admissibility test, the start,
//  the separation rule, θ's score, information and log-likelihood — beside the
//  package's public and internal functions, drives `Damped.iterate` and
//  `Root.bracketedNewton` with them, and compares the result with `Glm.fit`
//  bit for bit on every case of the generalised-linear-model reference corpus
//  (`fixtures/glm-reference/general.json`) under three controls, and on
//  authored cases that halve and that are refused. A transcription that
//  drifted from `Glm` fails here, as a solver that decided differently does.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute.Elementary
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression
open Fuaran.Compute.Solvers

let private bits (x: float) = BitConverter.DoubleToInt64Bits x

// ---- the transcription of what Glm's loops call (Glm.fs, private there) ------------------------

let private finite (v: float) : bool =
    not (Double.IsNaN v || Double.IsInfinity v)

let private positiveMean (family: GlmFamily) : bool =
    match family with
    | GlmFamily.Gaussian
    | GlmFamily.Binomial
    | GlmFamily.QuasiBinomial -> false
    | _ -> true

let private firstBad (family: GlmFamily) (link: GlmLink) (y: float[]) (w: float[]) (eta: float[]) (mu: float[]) =
    let mutable bad = None
    let mutable i = 0

    while Option.isNone bad && i < mu.Length do
        let m = mu[i]

        let outside =
            not (finite m)
            || (link = GlmLink.Log && m <= 0.0)
            || (positiveMean family && m <= 0.0)
            || ((family = GlmFamily.Binomial || family = GlmFamily.QuasiBinomial)
                && not (m > 0.0 && m < 1.0))
            || ((link = GlmLink.InverseSquare || link = GlmLink.Sqrt) && not (eta[i] > 0.0))
            || (w[i] > 0.0 && not (finite (Glm.unitDeviance family y[i] m)))

        if outside then
            bad <- Some i

        i <- i + 1

    bad

let private outOfRange (family: GlmFamily) (link: GlmLink) (row: int) : LinAlgError =
    match family, link with
    | _, GlmLink.Log
    | GlmFamily.Binomial, _
    | GlmFamily.QuasiBinomial, _ -> LinAlgError.Separation row
    | _ -> LinAlgError.MeanOutOfRange row

let private predictor (x: float[]) (n: int) (beta: float option[]) (offset: float[]) : float[] =
    Array.init n (fun i ->
        let mutable acc = 0.0

        for j in 0 .. beta.Length - 1 do
            match beta[j] with
            | Some b -> acc <- acc + x[j * n + i] * b
            | None -> ()

        acc + offset[i])

let private solve (x: Matrix) (z: float[]) (ww: float[]) : Result<LeastSquaresFit, LinAlgError> =
    QRAccumulator.empty x.Cols
    |> QRAccumulator.addRows x (Vector.adopt z) (Vector.adopt ww)
    |> Result.mapError (fun e ->
        match e with
        | LinAlgError.NotFinite(r, _)
        | LinAlgError.InvalidWeight r -> LinAlgError.Separation r
        | other -> other)
    |> Result.bind LeastSquares.ofAccumulator

let private separated (family: GlmFamily) (w: float[]) (mu: float[]) : int option =
    match family with
    | GlmFamily.Binomial
    | GlmFamily.QuasiBinomial ->
        let tau = 9.5367431640625e-07
        let mutable r = None
        let mutable i = 0

        while Option.isNone r && i < mu.Length do
            if w[i] > 0.0 && (mu[i] <= tau || mu[i] >= 1.0 - tau) then
                r <- Some i

            i <- i + 1

        r
    | _ -> None

let private start (family: GlmFamily) (link: GlmLink) (y: float[]) (w: float[]) : float[] * float[] =
    let mu0 =
        match family with
        | GlmFamily.Poisson
        | GlmFamily.QuasiPoisson -> y |> Array.map (fun v -> v + 0.1)
        | GlmFamily.Tweedie _ -> y |> Array.map (fun v -> if v = 0.0 then 0.1 else v)
        | GlmFamily.Binomial
        | GlmFamily.QuasiBinomial -> Array.init y.Length (fun i -> (w[i] * y[i] + 0.5) / (w[i] + 1.0))
        | GlmFamily.NegativeBinomial _ -> y |> Array.map (fun v -> if v = 0.0 then 1.0 / 6.0 else v)
        | _ -> Array.copy y

    mu0 |> Array.map (Glm.linkFunction link), mu0

let private countSum (f: float -> float) (theta: float) (y: float) : float =
    let mutable acc = 0.0
    let mutable k = 0.0

    while k < y do
        acc <- acc + f (theta + k)
        k <- k + 1.0

    acc

let private thetaScoreInfo (ya: float[]) (w: float[]) (mu: float[]) (theta: float) : float * float =
    let mutable score = 0.0
    let mutable info = 0.0
    let lnTheta = Elementary.ln theta

    for i in 0 .. ya.Length - 1 do
        if w[i] > 0.0 then
            let y = ya[i]
            let mt = mu[i] + theta
            let s1 = countSum (fun v -> 1.0 / v) theta y
            let s2 = countSum (fun v -> 1.0 / (v * v)) theta y
            score <- score + w[i] * (s1 + lnTheta + 1.0 - Elementary.ln mt - (y + theta) / mt)
            info <- info + w[i] * (s2 - 1.0 / theta + 2.0 / mt - (y + theta) / (mt * mt))

    score, info

let private thetaLogLik (ya: float[]) (w: float[]) (mu: float[]) (theta: float) : float =
    let mutable acc = 0.0
    let lnTheta = Elementary.ln theta

    for i in 0 .. ya.Length - 1 do
        if w[i] > 0.0 then
            let y = ya[i]
            let mz = if y = 0.0 then mu[i] + 1.0 else mu[i]

            acc <-
                acc
                + w[i]
                  * (countSum Elementary.ln theta y + theta * lnTheta + y * Elementary.ln mz
                     - (theta + y) * Elementary.ln (theta + mu[i]))

    acc

// ---- the loops, as calls of the solvers --------------------------------------------------------

/// What an iteration stands on: the coefficients (none at the start), the predictor and means they
/// give, the first row out of range, and the solve that proposed them.
type private Irls =
    { Beta: float option[] option
      Eta: float[]
      Mu: float[]
      Bad: int option
      Ww: float[]
      Ls: LeastSquaresFit option }

/// What is compared with a `GlmFit`: its coefficients, linear predictor, means, deviance and iteration
/// count, and the last solve's residual degrees of freedom.
type private Core =
    { Coefficients: float option list
      Eta: float[]
      Mu: float[]
      Deviance: float
      Iterations: int
      ResidualDf: int }

/// How many candidates were halved, over every run: the law is vacuous over a corpus that never halves.
let mutable private halvings = 0

/// `Glm`'s IRLS from `(η₀, μ₀)` as one call of `Damped.iterate`: its step is the weighted solve, its
/// admissibility `firstBad`, its objective the deviance, its halving the midpoint of the coefficients,
/// ten times at most, from a seed; its rule the relative deviance change against `0.1` and `MaxIter`.
let private irls
    (family: GlmFamily)
    (link: GlmLink)
    (control: GlmControl)
    (x: Matrix)
    (ya: float[])
    (w: float[])
    (o: float[])
    (eta0: float[])
    (mu0: float[])
    : Result<Core, LinAlgError> =
    let n = x.Rows
    let p = x.Cols
    let xs = Matrix.toColumnMajor x

    let evaluate (beta: float option[]) (from: Irls) : Irls =
        let eta = predictor xs n beta o
        let mu = eta |> Array.map (Glm.inverseLink link)

        { from with
            Beta = Some beta
            Eta = eta
            Mu = mu
            Bad = firstBad family link ya w eta mu }

    let step (s: Irls) : Result<Irls, LinAlgError> =
        let dmu =
            match link with
            | GlmLink.Log -> s.Mu
            | _ -> s.Eta |> Array.map (Glm.meanDerivative link)

        let z = Array.init n (fun i -> (s.Eta[i] - o[i]) + (ya[i] - s.Mu[i]) / dmu[i])

        let ww =
            match family, link with
            | (GlmFamily.Poisson | GlmFamily.QuasiPoisson), GlmLink.Log -> Array.init n (fun i -> w[i] * s.Mu[i])
            | GlmFamily.Tweedie pw, _ -> Array.init n (fun i -> w[i] * Elementary.pow s.Mu[i] (2.0 - pw))
            | GlmFamily.Gamma, GlmLink.Log -> Array.copy w
            | _ -> Array.init n (fun i -> w[i] * (dmu[i] * dmu[i] / Glm.variance family s.Mu[i]))

        solve x z ww
        |> Result.map (fun ls -> evaluate (List.toArray ls.Coefficients) { s with Ww = ww; Ls = Some ls })

    let problem: DampedProblem<Irls, LinAlgError> =
        { Step = step
          Inadmissible = fun s -> s.Bad |> Option.map (outOfRange family link)
          Objective = fun s -> Glm.deviance family ya w s.Mu
          Midpoint =
            fun standing candidate ->
                halvings <- halvings + 1
                let old = standing.Beta.Value
                let beta = candidate.Beta.Value

                let half =
                    Array.init p (fun j ->
                        match beta[j] with
                        | Some b -> Some(0.5 * (old[j] |> Option.defaultValue 0.0) + 0.5 * b)
                        | None -> None)

                evaluate half candidate
          Halvings = 10
          Seeded = true }

    let rule =
        StoppingRule.create control.MaxIter (StoppingTest.RelativeChange(control.Tol, 0.1))

    let seed =
        { Beta = None
          Eta = eta0
          Mu = mu0
          Bad = None
          Ww = [||]
          Ls = None }

    let core (s: Stopped<Irls>) : Core =
        let ls = s.State.Ls.Value

        { Coefficients = s.State.Beta.Value |> List.ofArray |> List.map (Option.map Kernels.canonical)
          Eta = s.State.Eta
          Mu = s.State.Mu
          Deviance = Kernels.canonical s.Value
          Iterations = s.Iterations
          ResidualDf = max 0 (ls.Count - ls.Rank) }

    match Damped.iterate rule problem seed with
    | Ok s ->
        match separated family w s.State.Mu with
        | Some r -> Error(LinAlgError.Separation r)
        | None -> Ok(core s)
    | Error(SolverError.NotConverged last) ->
        match separated family w last.State.Mu with
        | Some r -> Error(LinAlgError.Separation r)
        | None -> Error(LinAlgError.NotConverged last.Iterations)
    | Error(SolverError.StepRefused e) -> Error e
    | Error(SolverError.HalvingExhausted(_, Some e)) -> Error e
    | Error(SolverError.HalvingExhausted(k, None)) -> Error(LinAlgError.NotConverged k)
    | Error other -> failtestf "the solver refused the IRLS with %A" other

/// `2⁻¹³`, the step on θ below which it has converged, and the bracket the transcription keeps θ in.
let private thetaStepTol = 1.220703125e-4

let private thetaLow = 9.5367431640625e-07
let private thetaHigh = 1048576.0

/// `Glm`'s Newton on θ's score as one call of `Root.bracketedNewton`: `f` the score and `f′` minus the
/// observed information, so the Newton step `f/f′` is the loop's `score/info` negated exactly and
/// `x − f/f′` its `θ + score/info`; every narrowing admitted (`shrink = 1`), so no step is replaced
/// while θ stays inside the bracket; the rule the absolute step against `2⁻¹³`, at most `MaxIter − 1`
/// steps. The loop reflects a negative θ where the solver would bisect: no case here leaves the bracket.
let private thetaMl
    (control: GlmControl)
    (alternation: int)
    (ya: float[])
    (w: float[])
    (mu: float[])
    : Result<float * float, LinAlgError> =
    let mutable sw = 0.0
    let mutable sr = 0.0

    for i in 0 .. ya.Length - 1 do
        if w[i] > 0.0 then
            let r = ya[i] / mu[i] - 1.0
            sw <- sw + w[i]
            sr <- sr + w[i] * (r * r)

    let fdf (t: float) =
        let score, info = thetaScoreInfo ya w mu t
        score, -info

    if control.MaxIter < 2 then
        Error(LinAlgError.ThetaNotConverged alternation)
    else
        let rule =
            StoppingRule.create (control.MaxIter - 1) (StoppingTest.AbsoluteStep thetaStepTol)

        match Root.bracketedNewton rule 1.0 fdf thetaLow thetaHigh (sw / sr) with
        | Ok s ->
            let _, info = thetaScoreInfo ya w mu s.State
            Ok(s.State, info)
        | Error(_: SolverError<float, unit>) -> Error(LinAlgError.ThetaNotConverged alternation)

/// `Glm.fit`'s iteration, transcribed above the two solver calls: the fit's core, and θ, its standard
/// error and the alternation count under an estimated negative binomial.
let private fitBySolvers
    (family: GlmFamily)
    (link: GlmLink)
    (control: GlmControl)
    (x: Matrix)
    (ya: float[])
    (w: float[])
    (o: float[])
    : Result<Core * (float * float option * int) option, LinAlgError> =
    match family with
    | GlmFamily.NegativeBinomial None ->
        let eta0, mu0 = start GlmFamily.Poisson link ya w

        irls GlmFamily.Poisson link control x ya w o eta0 mu0
        |> Result.bind (fun poisson ->
            thetaMl control 0 ya w poisson.Mu
            |> Result.bind (fun (theta, info) ->
                let d1 = sqrt (2.0 * float (max 1 poisson.ResidualDf))
                let lm = thetaLogLik ya w poisson.Mu theta

                let rec alternate (k: int) (fit: Core) theta info lm0 lm step =
                    if not (abs (lm0 - lm) / d1 + abs step > control.Tol) then
                        let se =
                            if info > 0.0 then
                                Some(Kernels.canonical (sqrt (1.0 / info)))
                            else
                                None

                        Ok(fit, Some(Kernels.canonical theta, se, k))
                    elif k >= control.MaxIter then
                        Error(LinAlgError.ThetaNotConverged k)
                    else
                        irls (GlmFamily.NegativeBinomial(Some theta)) link control x ya w o fit.Eta fit.Mu
                        |> Result.bind (fun next ->
                            thetaMl control (k + 1) ya w next.Mu
                            |> Result.bind (fun (thetaN, infoN) ->
                                alternate
                                    (k + 1)
                                    next
                                    thetaN
                                    infoN
                                    lm
                                    (thetaLogLik ya w next.Mu thetaN)
                                    (theta - thetaN)))

                alternate 0 poisson theta info (lm + 2.0 * d1) lm 1.0))
    | _ ->
        let eta0, mu0 = start family link ya w
        irls family link control x ya w o eta0 mu0 |> Result.map (fun c -> c, None)

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

/// One comparison: `Glm.fit` against the solvers over the same case and control. Answers whether the
/// two fitted, having failed the test where they differ in a bit, a count or a refusal.
let private compare (c: Case) (label: string) (control: GlmControl) : bool =
    let what = sprintf "%s under %s" c.Name label
    let o = Array.zeroCreate c.X.Rows

    let glm =
        Glm.fit c.Family c.Link control c.X (Vector.adopt c.Y) (Vector.adopt c.W) (Vector.adopt o)

    match glm, fitBySolvers c.Family c.Link control c.X c.Y c.W o with
    | Error e, Error s ->
        Expect.equal s e (what + ": the refusal")
        false
    | Ok g, Ok(s, theta) ->
        let floats (xs: float seq) = xs |> Seq.map bits |> List.ofSeq

        Expect.equal
            (s.Coefficients |> List.map (Option.map bits))
            (g.Coefficients |> List.map (Option.map bits))
            (what + ": the coefficients' bits")

        Expect.equal (floats s.Mu) (floats (Vector.toArray g.Fitted)) (what + ": the means' bits")
        Expect.equal (floats s.Eta) (floats (Vector.toArray g.LinearPredictor)) (what + ": the predictor's bits")
        Expect.equal (bits s.Deviance) (bits g.Deviance) (what + ": the deviance's bits")
        Expect.equal s.Iterations g.Iterations (what + ": the iteration the loop stopped at")

        match theta with
        | Some(t, se, k) ->
            Expect.equal (Some(bits t)) (g.Theta |> Option.map bits) (what + ": θ's bits")
            Expect.equal (se |> Option.map bits) (g.ThetaStdError |> Option.map bits) (what + ": θ's error's bits")
            Expect.equal k g.Alternations (what + ": the alternation count")
        | None -> Expect.isTrue (g.Alternations = 0) (what + ": no alternation")

        true
    | g, s -> failtestf "%s: Glm.fit answered %A and the solvers %A" what g (s |> Result.map fst)

[<Tests>]
let tests =
    testList
        "Phase 633 - the solvers reproduce Glm's loops"
        [ testCase "Damped.iterate and Root.bracketedNewton answer Glm.fit's bits on the reference corpus"
          <| fun () ->
              let cases = referenceCases ()
              Expect.equal cases.Length 22 "the reference corpus is read whole"

              let outcomes =
                  [ for c in cases do
                        for label, control in controls -> c, compare c label control ]

              let fitted = outcomes |> List.filter snd |> List.length
              Expect.isGreaterThan fitted 40 "most comparisons are of two fits"
              Expect.isGreaterThan (outcomes.Length - fitted) 5 "the cut runs are compared as refusals"

              let estimated =
                  outcomes
                  |> List.filter (fun (c, ok) -> ok && c.Family = GlmFamily.NegativeBinomial None)

              Expect.isGreaterThanOrEqual estimated.Length 2 "the θ alternation is reproduced where θ is estimated"

          testCase "the same holds where a step is halved and where the fit is refused"
          <| fun () ->
              let before = halvings

              let outcomes =
                  [ for c in authoredCases () do
                        for label, control in controls -> c.Name, compare c label control ]

              Expect.isGreaterThan (halvings - before) 0 "at least one authored run halves a step"
              Expect.exists outcomes snd "at least one authored run fits"
              Expect.exists outcomes (snd >> not) "at least one authored run is refused"

          testCase
              "a rule that stops elsewhere moves the compared cells, so a solver deciding differently would be seen"
          <| fun () ->
              // The comparison is only worth its green if a different decision moves a compared cell.
              // A tighter rule stops a reference fit later, and the fit it stops at is another fit.
              let c = referenceCases () |> List.find (fun c -> c.Name = "dobson-poisson-sqrt")
              let o = Array.zeroCreate c.X.Rows

              let fit control =
                  fitBySolvers c.Family c.Link control c.X c.Y c.W o |> Result.map fst

              match fit (GlmControl.create 200 1e-2).Value, fit (GlmControl.create 200 1e-10).Value with
              | Ok loose, Ok tight ->
                  Expect.isGreaterThan tight.Iterations loose.Iterations "a tighter rule stops later"

                  Expect.notEqual
                      (tight.Coefficients |> List.map (Option.map bits))
                      (loose.Coefficients |> List.map (Option.map bits))
                      "and every bit after differs"
              | a, b -> failtestf "the two controls answered %A and %A" a b ]
