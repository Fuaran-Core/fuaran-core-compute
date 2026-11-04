namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

/// The error distribution a generalised linear model assumes (Phase 595, DECISIONS.md D49 items 14 to
/// 19 and D55; Phase 598, D57). `Gaussian` under `GlmLink.Identity` is weighted least squares; every
/// other family is fitted under `GlmLink.Log`.
[<RequireQualifiedAccess>]
type GlmFamily =
    /// Variance `V(μ) = 1`.
    | Gaussian
    /// Variance `V(μ) = μ`, dispersion fixed at one.
    | Poisson
    /// Variance `V(μ) = μ²`, dispersion estimated by Pearson's statistic.
    | Gamma
    /// Variance `V(μ) = μ^power` (Phase 598), dispersion estimated by Pearson's statistic. A power
    /// strictly between zero and one, or not finite, is refused (`LinAlgError.InvalidPower`).
    | Tweedie of power: float
    /// Poisson's mean model and iterations (`V(μ) = μ`), the dispersion estimated by Pearson's statistic
    /// rather than fixed at one (Phase 598).
    | QuasiPoisson

/// The link a generalised linear model relates the linear predictor `η` to the mean through.
[<RequireQualifiedAccess>]
type GlmLink =
    /// `μ = η`.
    | Identity
    /// `μ = exp η`, through the pinned `Elementary.exp`.
    | Log

/// The iteration control of an IRLS fit: the most iterations and the convergence tolerance, checked
/// when it is made (`GlmControl.create`), so a fit never meets an out-of-range control.
[<Sealed>]
type GlmControl internal (maxIter: int, tol: float) =

    /// The most iterations, at least one.
    member _.MaxIter: int = maxIter

    /// The convergence tolerance, finite and positive.
    member _.Tol: float = tol

/// Construction of a `GlmControl`.
[<RequireQualifiedAccess>]
module GlmControl =

    /// R's `glm.control`: at most 25 iterations, tolerance `1e-8` (D49 item 17).
    let defaults: GlmControl = GlmControl(25, 1e-8)

    /// A control of at most `maxIter` iterations under tolerance `tol`; `None` unless `maxIter ≥ 1`
    /// and `tol` is finite and positive.
    let create (maxIter: int) (tol: float) : GlmControl option =
        if
            maxIter >= 1
            && not (System.Double.IsNaN tol || System.Double.IsInfinity tol)
            && tol > 0.0
        then
            Some(GlmControl(maxIter, tol))
        else
            None

/// A generalised linear model fitted by IRLS (Phase 595): the coefficients and their standard errors,
/// the mean and linear predictor at every row, the deviance, the dispersion, and the final iteration's
/// weighted least-squares fit, whose `R` the standard errors are read from.
[<Sealed>]
type GlmFit
    internal
    (
        coefficients: float option list,
        stdErrors: float option list,
        fitted: Vector<float>,
        linearPredictor: Vector<float>,
        deviance: float,
        dispersion: float option,
        iterations: int,
        last: LeastSquaresFit,
        workingWeights: Vector<float>,
        workingResiduals: Vector<float>
    ) =

    /// One coefficient per design column, in column order; `None` at a column the final iteration's
    /// solve aliased.
    member _.Coefficients: float option list = coefficients

    /// One standard error per design column, `√(φ · diag((RᵀR)⁻¹))` over the final iteration's `R`;
    /// `None` at an aliased column, and everywhere when the dispersion is unknown.
    member _.StdErrors: float option list = stdErrors

    /// The mean `μ` at every row, zero-weight rows included.
    member _.Fitted: Vector<float> = fitted

    /// The linear predictor `η = Xβ + offset` at every row.
    member _.LinearPredictor: Vector<float> = linearPredictor

    /// `Σ w · unit deviance` over the rows of positive weight, in row order.
    member _.Deviance: float = deviance

    /// The dispersion `φ`: one for Poisson; Pearson's statistic over the residual degrees of freedom
    /// for Gamma, quasi-Poisson and Tweedie (`Σ w·(y − μ)²/V(μ)`), and the deviance over them for
    /// Gaussian; `None` where those are zero.
    member _.Dispersion: float option = dispersion

    /// The iterations run; one for Gaussian under Identity.
    member _.Iterations: int = iterations

    /// The rows of positive weight, `n`.
    member _.Count: int = last.Count

    /// The columns kept by the final iteration's solve.
    member _.Rank: int = last.Rank

    /// `n − rank`, never below zero.
    member _.ResidualDf: int = max 0 (last.Count - last.Rank)

    /// The final iteration's weighted least-squares fit: its `R`, `R⁻¹` and aliased columns.
    member _.LeastSquares: LeastSquaresFit = last

    /// The working weights the final iteration's solve was folded under (Phase 598): the prior weights
    /// under Gaussian, `w · μ²/V(μ)` at the mean the final iteration started from under the log link —
    /// R's `weights(fit, "working")`.
    member _.WorkingWeights: Vector<float> = workingWeights

    /// The working residuals at the final mean (Phase 598): `y − μ` under the identity link,
    /// `(y − μ)/μ` under the log link — R's `residuals(fit, "working")`.
    member _.WorkingResiduals: Vector<float> = workingResiduals

/// Generalised linear models by iteratively reweighted least squares over the streaming Givens QR
/// (Phase 595, DECISIONS.md D49 items 14 to 19). Every step is a pinned operation — `+ − × ÷ √`,
/// `Elementary.exp` and `Elementary.ln` — in one stated order, so the coefficients, the iteration count
/// and the convergence decision are the same bits on every host.
[<RequireQualifiedAccess>]
module Glm =

    let private familyText (f: GlmFamily) : string =
        match f with
        | GlmFamily.Gaussian -> "gaussian"
        | GlmFamily.Poisson -> "poisson"
        | GlmFamily.Gamma -> "gamma"
        | GlmFamily.Tweedie _ -> "tweedie"
        | GlmFamily.QuasiPoisson -> "quasiPoisson"

    let private linkText (l: GlmLink) : string =
        match l with
        | GlmLink.Identity -> "identity"
        | GlmLink.Log -> "log"

    let private finite (v: float) : bool =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    /// The pairs this package fits.
    let private supported (family: GlmFamily) (link: GlmLink) : bool =
        match family, link with
        | GlmFamily.Gaussian, GlmLink.Identity
        | GlmFamily.Poisson, GlmLink.Log
        | GlmFamily.Gamma, GlmLink.Log
        | GlmFamily.Tweedie _, GlmLink.Log
        | GlmFamily.QuasiPoisson, GlmLink.Log -> true
        | _ -> false

    /// A Tweedie power some Tweedie distribution has: finite, and not strictly between zero and one
    /// (Phase 598).
    let private invalidPower (family: GlmFamily) : float option =
        match family with
        | GlmFamily.Tweedie p when not (finite p) || (p > 0.0 && p < 1.0) -> Some p
        | _ -> None

    /// A response the family admits (D49 item 15; Phase 598): finite; not negative under Poisson and
    /// quasi-Poisson; positive under Gamma; under Tweedie, positive at a power of two or more and not
    /// negative below it (the log link's start `ln μ₀` needs `μ₀ > 0`, and `y^(2 − p)` is infinite at
    /// `y = 0` once `p > 2`).
    let private admits (family: GlmFamily) (y: float) : bool =
        finite y
        && (match family with
            | GlmFamily.Gaussian -> true
            | GlmFamily.Poisson
            | GlmFamily.QuasiPoisson -> y >= 0.0
            | GlmFamily.Gamma -> y > 0.0
            | GlmFamily.Tweedie p -> if p >= 2.0 then y > 0.0 else y >= 0.0)

    /// One row's unit deviance (D49 item 18; Phase 598, D57): Gaussian `(y − μ)²`; Poisson and
    /// quasi-Poisson `2 (y·ln(y/μ) − (y − μ))` with `y·ln(y/μ)` pinned to zero at `y = 0`; Gamma
    /// `2 (−ln(y/μ) + (y − μ)/μ)`; Tweedie at `p = 1` Poisson's and at `p = 2` Gamma's, and otherwise
    /// `2 (y·(y₁^(1−p) − μ^(1−p))/(1 − p) − (y^(2−p) − μ^(2−p))/(2 − p))` with `y₁ = y`, or `1` at
    /// `y = 0` (R's `statmod::tweedie`), every power through `Elementary.pow`.
    let rec internal unitDeviance (family: GlmFamily) (y: float) (mu: float) : float =
        match family with
        | GlmFamily.Gaussian ->
            let r = y - mu
            r * r
        | GlmFamily.Poisson
        | GlmFamily.QuasiPoisson ->
            let term = if y = 0.0 then 0.0 else y * Elementary.ln (y / mu)
            2.0 * (term - (y - mu))
        | GlmFamily.Gamma -> 2.0 * (-(Elementary.ln (y / mu)) + (y - mu) / mu)
        | GlmFamily.Tweedie p when p = 1.0 -> unitDeviance GlmFamily.Poisson y mu
        | GlmFamily.Tweedie p when p = 2.0 -> unitDeviance GlmFamily.Gamma y mu
        | GlmFamily.Tweedie p ->
            let y1 = if y = 0.0 then 1.0 else y
            let a = 1.0 - p
            let b = 2.0 - p
            let theta = (Elementary.pow y1 a - Elementary.pow mu a) / a
            let kappa = (Elementary.pow y b - Elementary.pow mu b) / b
            2.0 * (y * theta - kappa)

    /// The first row whose mean leaves the link's range — not finite, or not positive under the log
    /// link — or, at a row of positive weight, whose unit deviance is not finite.
    let private firstBad (family: GlmFamily) (link: GlmLink) (y: float[]) (w: float[]) (mu: float[]) : int option =
        let mutable bad = None
        let mutable i = 0

        while Option.isNone bad && i < mu.Length do
            let m = mu[i]

            let outside =
                not (finite m)
                || (link = GlmLink.Log && m <= 0.0)
                || (w[i] > 0.0 && not (finite (unitDeviance family y[i] m)))

            if outside then
                bad <- Some i

            i <- i + 1

        bad

    /// The deviance: `Σ w · unit deviance` over the rows of positive weight, in row order, from `+0`. A
    /// zero-weight row is not counted (D49 item 13), and leaving it out of the fold is the same bits as
    /// adding its `+0` wherever its unit deviance is finite.
    let internal deviance (family: GlmFamily) (y: float[]) (w: float[]) (mu: float[]) : float =
        let mutable acc = 0.0

        for i in 0 .. y.Length - 1 do
            if w[i] > 0.0 then
                acc <- acc + w[i] * unitDeviance family y[i] mu[i]

        acc

    /// `μ = g⁻¹(η)`.
    let private inverseLink (link: GlmLink) (eta: float) : float =
        match link with
        | GlmLink.Identity -> eta
        | GlmLink.Log -> Elementary.exp eta

    /// `η = Σⱼ xᵢⱼ·βⱼ + offsetᵢ`: the kept columns `j` ascending, from `+0`, the offset added last — the
    /// order a fitted value of a least-squares fit is formed in.
    let private predictor (x: float[]) (n: int) (beta: float option[]) (offset: float[]) : float[] =
        Array.init n (fun i ->
            let mutable acc = 0.0

            for j in 0 .. beta.Length - 1 do
                match beta[j] with
                | Some b -> acc <- acc + x[j * n + i] * b
                | None -> ()

            acc + offset[i])

    /// One weighted least-squares solve of `z` on the columns of `x` under working weights `ww`,
    /// through a fresh accumulator. A scaled row that overflowed is the separation refusal at that row.
    let private solve (x: Matrix) (z: float[]) (ww: float[]) : Result<LeastSquaresFit, LinAlgError> =
        QRAccumulator.empty x.Cols
        |> QRAccumulator.addRows x (Vector.adopt z) (Vector.adopt ww)
        |> Result.mapError (fun e ->
            match e with
            | LinAlgError.NotFinite(r, _)
            | LinAlgError.InvalidWeight r -> LinAlgError.Separation r
            | other -> other)
        |> Result.bind LeastSquares.ofAccumulator

    /// The checks before any iteration, in this order, each naming the first offending row: the shapes,
    /// the pairing, the weights, the responses, the design, the offset (D49 item 15).
    let private validate
        (family: GlmFamily)
        (link: GlmLink)
        (x: Matrix)
        (y: float[])
        (w: float[])
        (o: float[])
        : Result<unit, LinAlgError> =
        let n = x.Rows
        let vd (len: int) : Dims = { Rows = len; Cols = 1 }

        let firstIndex (bad: float -> bool) (a: float[]) =
            let mutable r = None
            let mutable i = 0

            while Option.isNone r && i < a.Length do
                if bad a[i] then
                    r <- Some i

                i <- i + 1

            r

        if y.Length <> n then
            Error(LinAlgError.ShapeMismatch("Glm.fit", x.Dims, vd y.Length))
        elif w.Length <> n then
            Error(LinAlgError.ShapeMismatch("Glm.fit", x.Dims, vd w.Length))
        elif o.Length <> n then
            Error(LinAlgError.ShapeMismatch("Glm.fit", x.Dims, vd o.Length))
        elif not (supported family link) then
            Error(LinAlgError.UnsupportedModel(familyText family, linkText link))
        else
            match invalidPower family with
            | Some p -> Error(LinAlgError.InvalidPower p)
            | None ->
                match firstIndex (fun v -> not (finite v) || v < 0.0) w with
                | Some r -> Error(LinAlgError.InvalidWeight r)
                | None ->
                    match firstIndex (fun v -> not (admits family v)) y with
                    | Some r -> Error(LinAlgError.InvalidResponse r)
                    | None ->
                        let xs = Matrix.toColumnMajor x
                        let mutable bad = None
                        let mutable i = 0

                        while Option.isNone bad && i < n do
                            let mutable j = 0

                            while Option.isNone bad && j < x.Cols do
                                if not (finite xs[j * n + i]) then
                                    bad <- Some(LinAlgError.NotFinite(i, j))

                                j <- j + 1

                            i <- i + 1

                        match bad with
                        | Some e -> Error e
                        | None ->
                            match firstIndex (fun v -> not (finite v)) o with
                            | Some r -> Error(LinAlgError.NotFinite(r, x.Cols))
                            | None -> Ok()

    /// The standard errors over a solve's `R` under dispersion `phi` (D53 item 8).
    let private standardErrors (phi: float option) (ls: LeastSquaresFit) (p: int) : float option list =
        match phi with
        | Some phi -> LeastSquares.standardErrors phi ls
        | None -> List.replicate p None

    /// The fit, with `observe iteration workingWeights` called once per iteration before its solve —
    /// the seam `LinearAlgebraLaws` reads the working weights through.
    let internal fitObserved
        (observe: int -> float[] -> unit)
        (family: GlmFamily)
        (link: GlmLink)
        (control: GlmControl)
        (x: Matrix)
        (y: Vector<float>)
        (weights: Vector<float>)
        (offset: Vector<float>)
        : Result<GlmFit, LinAlgError> =
        let ya = Vector.toArray y
        let w = Vector.toArray weights
        let o = Vector.toArray offset

        validate family link x ya w o
        |> Result.bind (fun () ->
            let n = x.Rows
            let p = x.Cols
            let xs = Matrix.toColumnMajor x

            let finish
                (beta: float option[])
                (eta: float[])
                (mu: float[])
                (dev: float)
                (iterations: int)
                (ww: float[])
                ls
                =
                let df = max 0 ((ls: LeastSquaresFit).Count - ls.Rank)

                // Pearson's statistic over the rows of positive weight, in row order, from `+0`, over
                // the residual degrees of freedom; null where those are zero.
                let pearson (term: int -> float) =
                    if df > 0 then
                        let mutable acc = 0.0

                        for i in 0 .. n - 1 do
                            if w[i] > 0.0 then
                                acc <- acc + term i

                        Some(acc / float df)
                    else
                        None

                // `w·(y − μ)²/V(μ)`, `V` the variance function.
                let pearsonTerm (variance: float -> float) (i: int) =
                    let d = ya[i] - mu[i]
                    w[i] * (d * d / variance mu[i])

                let phi =
                    match family with
                    | GlmFamily.Poisson -> Some 1.0
                    | GlmFamily.Gaussian -> if df > 0 then Some(dev / float df) else None
                    | GlmFamily.Gamma ->
                        // Σ w·r², r = (y − μ)/μ the working residual of the log link.
                        pearson (fun i ->
                            let r = (ya[i] - mu[i]) / mu[i]
                            w[i] * (r * r))
                    | GlmFamily.QuasiPoisson -> pearson (pearsonTerm id)
                    | GlmFamily.Tweedie p -> pearson (pearsonTerm (fun m -> Elementary.pow m p))

                let residuals =
                    match link with
                    | GlmLink.Identity -> Array.init n (fun i -> ya[i] - mu[i])
                    | GlmLink.Log -> Array.init n (fun i -> (ya[i] - mu[i]) / mu[i])

                GlmFit(
                    List.ofArray beta |> List.map (Option.map Kernels.canonical),
                    standardErrors phi ls p,
                    Kernels.adopt (Array.copy mu),
                    Kernels.adopt (Array.copy eta),
                    Kernels.canonical dev,
                    phi |> Option.map Kernels.canonical,
                    iterations,
                    ls,
                    Kernels.adopt (Array.copy ww),
                    Kernels.adopt residuals
                )

            match family, link with
            | GlmFamily.Gaussian, _ ->
                // One solve: the working response is `y − offset` and the working weight the prior
                // weight, independent of `μ`, so the first solve is the fixed point (this phase's entry).
                observe 1 w
                let z = Array.init n (fun i -> ya[i] - o[i])

                solve x z w
                |> Result.map (fun ls ->
                    let beta = ls.Coefficients |> List.toArray
                    let eta = predictor xs n beta o
                    let mu = eta |> Array.map (inverseLink link)
                    finish beta eta mu (deviance family ya w mu) 1 w ls)
            | _ ->
                // D49 item 14: μ₀ = y + 0.1 for Poisson and quasi-Poisson, y for Gamma, and for Tweedie
                // y with 0.1 in place of a zero (R's `statmod::tweedie`, Phase 598); η₀ = ln μ₀; the
                // deviance of μ₀ is the first iteration's previous deviance.
                let mu0 =
                    match family with
                    | GlmFamily.Poisson
                    | GlmFamily.QuasiPoisson -> ya |> Array.map (fun v -> v + 0.1)
                    | GlmFamily.Tweedie _ -> ya |> Array.map (fun v -> if v = 0.0 then 0.1 else v)
                    | _ -> Array.copy ya

                let eta0 = mu0 |> Array.map Elementary.ln

                let evaluate (beta: float option[]) =
                    let eta = predictor xs n beta o
                    let mu = eta |> Array.map (inverseLink link)
                    eta, mu, firstBad family link ya w mu

                let rec iterate
                    (iteration: int)
                    (betaOld: float option[] option)
                    (eta: float[])
                    (mu: float[])
                    (devPrev: float)
                    : Result<GlmFit, LinAlgError> =
                    // The working response and weight of the log link: z = (η − offset) + (y − μ)/μ, and
                    // w·μ²/V(μ) — w·μ under Poisson and quasi-Poisson, exactly the prior weight under
                    // Gamma, w·μ^(2−p) under Tweedie (`Elementary.pow`, exact at p = 1 and p = 2).
                    let z = Array.init n (fun i -> (eta[i] - o[i]) + (ya[i] - mu[i]) / mu[i])

                    let ww =
                        match family with
                        | GlmFamily.Poisson
                        | GlmFamily.QuasiPoisson -> Array.init n (fun i -> w[i] * mu[i])
                        | GlmFamily.Tweedie p -> Array.init n (fun i -> w[i] * Elementary.pow mu[i] (2.0 - p))
                        | _ -> Array.copy w

                    observe iteration ww

                    solve x z ww
                    |> Result.bind (fun ls ->
                        let bNew = ls.Coefficients |> List.toArray
                        let etaN, muN, badN = evaluate bNew

                        let stepped =
                            match betaOld with
                            | None ->
                                // The first iteration has no previous coefficients to halve towards: a
                                // mean out of range is refused at once, and its deviance is not compared
                                // with μ₀'s, which is the saturated start, not a point of the model.
                                match badN with
                                | Some r -> Error(LinAlgError.Separation r)
                                | None -> Ok(bNew, etaN, muN, deviance family ya w muN)
                            | Some bOld ->
                                // D49 item 16: halve towards the previous coefficients on a mean out of
                                // range or a deviance rise, at most ten times. A column the new solve
                                // aliased stays aliased.
                                let rec halve (k: int) (beta: float option[]) eta mu bad =
                                    let dev =
                                        match bad with
                                        | Some _ -> nan
                                        | None -> deviance family ya w mu

                                    // A rise is one the stopping rule would see: `(dev − dev_prev) / (|dev| +
                                    // 0.1) ≥ tol` (glm2's test). A rise below it is rounding at the optimum,
                                    // which no halving removes and the stopping rule accepts.
                                    let rise =
                                        Option.isSome bad
                                        || not (finite dev)
                                        || not ((dev - devPrev) / (abs dev + 0.1) < control.Tol)

                                    if not rise then
                                        Ok(beta, eta, mu, dev)
                                    elif k = 10 then
                                        match bad with
                                        | Some r -> Error(LinAlgError.Separation r)
                                        | None -> Error(LinAlgError.NotConverged iteration)
                                    else
                                        let half =
                                            Array.init p (fun j ->
                                                match beta[j] with
                                                | Some b ->
                                                    let old = bOld[j] |> Option.defaultValue 0.0
                                                    Some(0.5 * old + 0.5 * b)
                                                | None -> None)

                                        let e, m, b = evaluate half
                                        halve (k + 1) half e m b

                                halve 0 bNew etaN muN badN

                        stepped
                        |> Result.bind (fun (beta, eta', mu', dev) ->
                            // D49 item 17, in that order, the comparison strict.
                            if abs (dev - devPrev) / (abs dev + 0.1) < control.Tol then
                                Ok(finish beta eta' mu' dev iteration ww ls)
                            elif iteration >= control.MaxIter then
                                Error(LinAlgError.NotConverged iteration)
                            else
                                iterate (iteration + 1) (Some beta) eta' mu' dev))

                iterate 1 None eta0 mu0 (deviance family ya w mu0))

    /// The generalised linear model of `y` on the columns of `x` under `family` and `link`, with prior
    /// `weights` and an `offset` added to the linear predictor (Phase 595, D49 items 14 to 19).
    ///
    /// `Gaussian` under `Identity` is one weighted least-squares solve of `y − offset` — the same fold as
    /// `QRAccumulator.addRows` into an empty accumulator and `LeastSquares.ofAccumulator`, byte for byte
    /// — and reports one iteration. `Poisson` and `Gamma` under `Log` iterate from `μ₀ = y + 0.1` and
    /// `μ₀ = y`, solving for `z = (η − offset) + (y − μ)/μ` under working weights `w·μ` and `w`; under
    /// `Log` too, `QuasiPoisson` iterates exactly as `Poisson` (its coefficients are Poisson's, bit for
    /// bit) and `Tweedie p` from `μ₀ = y` (`0.1` at a zero) under working weights `w·μ^(2−p)` (Phase
    /// 598, D57); each of the three estimates its dispersion by Pearson's statistic. Each family halves
    /// a step at most ten times on a mean out of range or a deviance rise the stopping rule would see
    /// (`(dev − dev_prev) / (|dev| + 0.1) ≥ tol`, from the second iteration),
    /// and stop when `|dev − dev_prev| / (|dev| + 0.1) < tol`.
    ///
    /// Refused before any iteration, each naming the first offending row: a shape mismatch, a pairing
    /// not fitted (`UnsupportedModel`), a Tweedie power strictly between zero and one or not finite
    /// (`InvalidPower`), a negative or non-finite weight (`InvalidWeight`), a response
    /// outside the family (`InvalidResponse`), a non-finite design cell (`NotFinite (row, column)`) or
    /// offset (`NotFinite (row, cols x)`). Refused while iterating: a mean no halving brings into range
    /// (`Separation`), a deviance no halving brings down, or no convergence by `MaxIter`
    /// (`NotConverged`, naming the iteration).
    let fit
        (family: GlmFamily)
        (link: GlmLink)
        (control: GlmControl)
        (x: Matrix)
        (y: Vector<float>)
        (weights: Vector<float>)
        (offset: Vector<float>)
        : Result<GlmFit, LinAlgError> =
        fitObserved (fun _ _ -> ()) family link control x y weights offset

    /// The deviance of the null model (R's `glm.fit`): without an intercept the mean is `g⁻¹(offset)`;
    /// with one, the intercept-only model with the same offset — under the identity link its closed form,
    /// `μᵢ = m + offsetᵢ` with `m = Σw(y − offset) / Σw`; under the log link with every offset zero the
    /// weighted mean `Σwy / Σw`, and with an offset the model fitted by `fit`, whose refusals it shares.
    /// Each sum is folded in row order; the deviance over the rows of positive weight.
    let nullDeviance
        (family: GlmFamily)
        (link: GlmLink)
        (control: GlmControl)
        (intercept: bool)
        (y: Vector<float>)
        (weights: Vector<float>)
        (offset: Vector<float>)
        : Result<float, LinAlgError> =
        let n = y.Length
        let ones = Matrix.init n 1 (fun _ _ -> 1.0)
        let ya = Vector.toArray y
        let w = Vector.toArray weights
        let o = Vector.toArray offset

        validate family link ones ya w o
        |> Result.bind (fun () ->
            let devOf (mu: float[]) =
                match firstBad family link ya w mu with
                | Some r -> Error(LinAlgError.Separation r)
                | None -> Ok(Kernels.canonical (deviance family ya w mu))

            // `Σw(y − offset) / Σw`, each sum a fold in row order.
            let weightedMean () =
                let mutable sw = 0.0
                let mutable swy = 0.0

                for i in 0 .. n - 1 do
                    sw <- sw + w[i]
                    swy <- swy + w[i] * (ya[i] - o[i])

                swy / sw

            if not intercept then
                devOf (o |> Array.map (inverseLink link))
            else
                match link with
                | GlmLink.Identity ->
                    let m = weightedMean ()
                    devOf (o |> Array.map (fun v -> m + v))
                | GlmLink.Log when o |> Array.forall (fun v -> v = 0.0) -> devOf (Array.create n (weightedMean ()))
                | GlmLink.Log ->
                    fit family link control ones y weights offset
                    |> Result.map (fun f -> f.Deviance))

    /// The heteroskedasticity-consistent standard errors of `fit` (Phase 598, DECISIONS.md D57): the
    /// sandwich `LeastSquares.robustStandardErrors` forms over the final iteration's `R`, with the
    /// scores stated in the fit's working weights and working residuals — R's `sandwich::vcovHC` on a
    /// `glm`, which is free of the dispersion. `x` is the design `fit` was fitted on; refused as
    /// `robustStandardErrors` refuses.
    let robustStandardErrors
        (variant: RobustVariant)
        (x: Matrix)
        (fit: GlmFit)
        : Result<float option list, LinAlgError> =
        LeastSquares.robustStandardErrors variant x fit.WorkingWeights fit.WorkingResiduals fit.LeastSquares
