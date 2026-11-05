namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

/// The error distribution a generalised linear model assumes (Phase 595, DECISIONS.md D49 items 14 to
/// 19 and D55; Phase 598, D57; Phase 600, D58). Which links each family is fitted under is
/// `Glm.admitted`'s table.
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
    /// Variance `V(μ) = μ(1 − μ)`, dispersion fixed at one (Phase 600). The response is a proportion in
    /// `[0, 1]` — a 0/1 outcome, or successes over trials with the trials carried as prior weights.
    | Binomial
    /// The binomial mean model and iterations, the dispersion estimated by Pearson's statistic (Phase 600).
    | QuasiBinomial
    /// Variance `V(μ) = μ³`, dispersion estimated by Pearson's statistic (Phase 600).
    | InverseGaussian
    /// Variance `V(μ) = μ + μ²/θ`, dispersion fixed at one (Phase 600): `Some θ` fits at that θ, which
    /// must be finite and positive (`LinAlgError.InvalidTheta`); `None` estimates θ by maximum likelihood,
    /// alternating with the coefficients (DECISIONS.md D58).
    | NegativeBinomial of theta: float option

/// The link a generalised linear model relates the linear predictor `η` to the mean through. Every
/// `g`, `g⁻¹` and `dμ/dη` is built from pinned operations (`Glm.linkFunction`, `Glm.inverseLink`,
/// `Glm.meanDerivative`).
[<RequireQualifiedAccess>]
type GlmLink =
    /// `μ = η`.
    | Identity
    /// `μ = exp η`, through the pinned `Elementary.exp`.
    | Log
    /// `μ = 1 / (1 + exp(−η))` (Phase 600).
    | Logit
    /// `μ = Φ(η)`, through the pinned `Elementary.normalCdf` (Phase 600).
    | Probit
    /// `μ = 1 − exp(−exp η)`, the complementary log-log (Phase 600).
    | Cloglog
    /// `μ = 1/η` (Phase 600).
    | Inverse
    /// `μ = 1/√η` (Phase 600).
    | InverseSquare
    /// `μ = η²` (Phase 600).
    | Sqrt

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
        workingResiduals: Vector<float>,
        theta: float option,
        thetaStdError: float option,
        alternations: int
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

    /// The dispersion `φ`: one for Poisson, binomial and negative binomial; Pearson's statistic over
    /// the residual degrees of freedom (`Σ w·(y − μ)²/V(μ)`) for Gamma, inverse Gaussian, the quasi
    /// families and Tweedie, and the deviance over them for Gaussian; `None` where those are zero.
    member _.Dispersion: float option = dispersion

    /// The iterations run by the final IRLS; one for Gaussian under Identity.
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
    /// under Gaussian and Identity, otherwise `w · (dμ/dη)² / V(μ)` at the point the final iteration
    /// started from — R's `weights(fit, "working")`.
    member _.WorkingWeights: Vector<float> = workingWeights

    /// The working residuals at the final mean (Phase 598): `(y − μ) / (dμ/dη)` — `y − μ` under the
    /// identity link, `(y − μ)/μ` under the log link — R's `residuals(fit, "working")`.
    member _.WorkingResiduals: Vector<float> = workingResiduals

    /// The negative binomial's θ (Phase 600): the caller's under `NegativeBinomial (Some θ)`; the
    /// maximum-likelihood estimate under `NegativeBinomial None`; `None` for every other family.
    member _.Theta: float option = theta

    /// The standard error of an estimated θ, `1/√I(θ)` with `I` the observed information at the final
    /// mean (MASS's `theta.ml`); `None` unless θ was estimated.
    member _.ThetaStdError: float option = thetaStdError

    /// The alternations between the coefficients and θ an estimated θ took; zero otherwise.
    member _.Alternations: int = alternations

/// Generalised linear models by iteratively reweighted least squares over the streaming Givens QR
/// (Phase 595, DECISIONS.md D49 items 14 to 19; Phase 600, D58). Every step is a pinned operation —
/// `+ − × ÷ √` and the `Elementary` functions — in one stated order, so the coefficients, the
/// iteration count and the convergence decision are the same bits on every host.
[<RequireQualifiedAccess>]
module Glm =

    let private familyText (f: GlmFamily) : string =
        match f with
        | GlmFamily.Gaussian -> "gaussian"
        | GlmFamily.Poisson -> "poisson"
        | GlmFamily.Gamma -> "gamma"
        | GlmFamily.Tweedie _ -> "tweedie"
        | GlmFamily.QuasiPoisson -> "quasiPoisson"
        | GlmFamily.Binomial -> "binomial"
        | GlmFamily.QuasiBinomial -> "quasiBinomial"
        | GlmFamily.InverseGaussian -> "inverseGaussian"
        | GlmFamily.NegativeBinomial _ -> "negativeBinomial"

    let private linkText (l: GlmLink) : string =
        match l with
        | GlmLink.Identity -> "identity"
        | GlmLink.Log -> "log"
        | GlmLink.Logit -> "logit"
        | GlmLink.Probit -> "probit"
        | GlmLink.Cloglog -> "cloglog"
        | GlmLink.Inverse -> "inverse"
        | GlmLink.InverseSquare -> "inverseSquare"
        | GlmLink.Sqrt -> "sqrt"

    let private finite (v: float) : bool =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    /// The pairs this package fits (D58 item 1): R's `family` table restricted to this link set.
    /// Gaussian under Identity, Log and Inverse; binomial and quasi-binomial under Logit, Probit and
    /// Cloglog; Poisson and quasi-Poisson under Log, Identity and Sqrt; Gamma under Inverse, Identity
    /// and Log; inverse Gaussian under InverseSquare, Inverse, Identity and Log; negative binomial under
    /// Log, Sqrt and Identity; Tweedie under Log.
    let admitted (family: GlmFamily) (link: GlmLink) : bool =
        match family, link with
        | GlmFamily.Gaussian, (GlmLink.Identity | GlmLink.Log | GlmLink.Inverse)
        | (GlmFamily.Binomial | GlmFamily.QuasiBinomial), (GlmLink.Logit | GlmLink.Probit | GlmLink.Cloglog)
        | (GlmFamily.Poisson | GlmFamily.QuasiPoisson), (GlmLink.Log | GlmLink.Identity | GlmLink.Sqrt)
        | GlmFamily.Gamma, (GlmLink.Inverse | GlmLink.Identity | GlmLink.Log)
        | GlmFamily.InverseGaussian, (GlmLink.InverseSquare | GlmLink.Inverse | GlmLink.Identity | GlmLink.Log)
        | GlmFamily.NegativeBinomial _, (GlmLink.Log | GlmLink.Sqrt | GlmLink.Identity)
        | GlmFamily.Tweedie _, GlmLink.Log -> true
        | _ -> false

    /// The family's default link: its canonical link where the set has it (Gaussian Identity, binomial
    /// Logit, Poisson Log, Gamma Inverse, inverse Gaussian InverseSquare), and Log for the negative
    /// binomial and Tweedie families, whose canonical links are not in the set.
    let defaultLink (family: GlmFamily) : GlmLink =
        match family with
        | GlmFamily.Gaussian -> GlmLink.Identity
        | GlmFamily.Binomial
        | GlmFamily.QuasiBinomial -> GlmLink.Logit
        | GlmFamily.Gamma -> GlmLink.Inverse
        | GlmFamily.InverseGaussian -> GlmLink.InverseSquare
        | GlmFamily.Poisson
        | GlmFamily.QuasiPoisson
        | GlmFamily.NegativeBinomial _
        | GlmFamily.Tweedie _ -> GlmLink.Log

    /// `ε = 2⁻⁵²`, the clamp of the binomial links' means and derivatives (D58 item 2).
    let private eps = 2.220446049250313e-16

    /// `μ` held to `[ε, 1 − ε]`; a NaN passes through.
    let private clampUnit (m: float) : float =
        if m < eps then eps
        elif m > 1.0 - eps then 1.0 - eps
        else m

    /// `1 − exp(−t)` for `t ≥ 0`: below one half the alternating series `t(1 − t/2(1 − t/3(…)))` to its
    /// eighteenth term, Horner from the highest, so a small `t` keeps its relative precision; otherwise
    /// `1 − exp(−t)`.
    let private oneMinusExpNeg (t: float) : float =
        if t < 0.5 then
            let mutable a = 0.0

            for k in 18..-1..2 do
                a <- (t / float k) * (1.0 - a)

            t * (1.0 - a)
        else
            1.0 - Elementary.exp (-t)

    /// `η` held at or below `700`, the cloglog link's guard against `exp η` overflowing; a NaN passes.
    let private capEta (eta: float) : float = if eta > 700.0 then 700.0 else eta

    /// The link `η = g(μ)`: Identity `μ`; Log `ln μ`; Logit `ln(μ / (1 − μ))`; Probit `Φ⁻¹(μ)`; Cloglog
    /// `ln(−ln(1 − μ))`; Inverse `1/μ`; InverseSquare `1/(μ·μ)`; Sqrt `√μ` — each through the pinned
    /// `Elementary` functions.
    let linkFunction (link: GlmLink) (mu: float) : float =
        match link with
        | GlmLink.Identity -> mu
        | GlmLink.Log -> Elementary.ln mu
        | GlmLink.Logit -> Elementary.ln (mu / (1.0 - mu))
        | GlmLink.Probit -> Elementary.normalQuantile mu
        | GlmLink.Cloglog -> Elementary.ln (-(Elementary.ln (1.0 - mu)))
        | GlmLink.Inverse -> 1.0 / mu
        | GlmLink.InverseSquare -> 1.0 / (mu * mu)
        | GlmLink.Sqrt -> sqrt mu

    /// The mean `μ = g⁻¹(η)`: Identity `η`; Log `exp η`; Logit `1 / (1 + exp(−η))`; Probit `Φ(η)`;
    /// Cloglog `1 − exp(−exp η)` with `η` capped at `700`; Inverse `1/η`; InverseSquare `1/√η`; Sqrt
    /// `η·η`. The three binomial links' means are held to `[ε, 1 − ε]`, `ε = 2⁻⁵²` (D58 item 2).
    let inverseLink (link: GlmLink) (eta: float) : float =
        match link with
        | GlmLink.Identity -> eta
        | GlmLink.Log -> Elementary.exp eta
        | GlmLink.Logit -> clampUnit (1.0 / (1.0 + Elementary.exp (-eta)))
        | GlmLink.Probit -> clampUnit (Elementary.normalCdf eta)
        | GlmLink.Cloglog -> clampUnit (oneMinusExpNeg (Elementary.exp (capEta eta)))
        | GlmLink.Inverse -> 1.0 / eta
        | GlmLink.InverseSquare -> 1.0 / sqrt eta
        | GlmLink.Sqrt -> eta * eta

    /// `dμ/dη` at `η`: Identity `1`; Log `exp η`; Logit `μ(1 − μ)` at the held mean; Probit `φ(η)`;
    /// Cloglog `t · exp(−t)` with `t = exp η`, `η` capped at `700`; Inverse `−1/(η·η)`; InverseSquare
    /// `−1/(2·η·√η)`; Sqrt `2η`. The three binomial links' derivatives are held at or above `ε`.
    let meanDerivative (link: GlmLink) (eta: float) : float =
        let floor (d: float) = if d < eps then eps else d

        match link with
        | GlmLink.Identity -> 1.0
        | GlmLink.Log -> Elementary.exp eta
        | GlmLink.Logit ->
            let m = inverseLink GlmLink.Logit eta
            floor (m * (1.0 - m))
        | GlmLink.Probit -> floor (Elementary.normalDensity eta)
        | GlmLink.Cloglog ->
            let t = Elementary.exp (capEta eta)
            floor (t * Elementary.exp (-t))
        | GlmLink.Inverse -> -1.0 / (eta * eta)
        | GlmLink.InverseSquare -> -1.0 / (2.0 * eta * sqrt eta)
        | GlmLink.Sqrt -> 2.0 * eta

    /// The variance function `V(μ)`.
    let internal variance (family: GlmFamily) (mu: float) : float =
        match family with
        | GlmFamily.Gaussian -> 1.0
        | GlmFamily.Poisson
        | GlmFamily.QuasiPoisson -> mu
        | GlmFamily.Gamma -> mu * mu
        | GlmFamily.Tweedie p -> Elementary.pow mu p
        | GlmFamily.Binomial
        | GlmFamily.QuasiBinomial -> mu * (1.0 - mu)
        | GlmFamily.InverseGaussian -> mu * mu * mu
        | GlmFamily.NegativeBinomial(Some theta) -> mu + mu * mu / theta
        | GlmFamily.NegativeBinomial None -> nan

    /// Whether the family's mean must be positive.
    let private positiveMean (family: GlmFamily) : bool =
        match family with
        | GlmFamily.Gaussian
        | GlmFamily.Binomial
        | GlmFamily.QuasiBinomial -> false
        | _ -> true

    /// Whether `η` is a linear predictor the pair has a mean for (D58 item 3): `μ = g⁻¹(η)` finite and
    /// inside the family's range — positive for every family but Gaussian and the binomials, inside
    /// `(0, 1)` for the binomials — and `η` positive under InverseSquare and Sqrt, where the link's
    /// inverse is defined on the positive half-line only.
    let validPredictor (family: GlmFamily) (link: GlmLink) (eta: float) : bool =
        let m = inverseLink link eta

        finite eta
        && finite m
        && (match link with
            | GlmLink.InverseSquare
            | GlmLink.Sqrt -> eta > 0.0
            | _ -> true)
        && (match family with
            | GlmFamily.Binomial
            | GlmFamily.QuasiBinomial -> m > 0.0 && m < 1.0
            | GlmFamily.Gaussian -> link <> GlmLink.Log || m > 0.0
            | _ -> m > 0.0)

    /// An interval `[lo, hi]` on the linear predictor's scale carried to the mean's (D58 item 7): each
    /// end mapped through `g⁻¹`, not a symmetric interval about the mean. Where the link meets the
    /// boundary of the mean's range at `η = 0` — Identity under a family with a positive mean, Sqrt,
    /// InverseSquare, and Inverse, on the side of zero `centre` lies — an end beyond zero is first held
    /// at zero. The ends are ordered (Inverse and InverseSquare decrease), and an end the link maps to no
    /// finite mean is `None`: unbounded on that side.
    let responseInterval
        (family: GlmFamily)
        (link: GlmLink)
        (centre: float)
        (lo: float)
        (hi: float)
        : float option * float option =
        let positiveSide =
            match link with
            | GlmLink.Identity -> if positiveMean family then Some true else None
            | GlmLink.Sqrt
            | GlmLink.InverseSquare -> Some true
            | GlmLink.Inverse -> Some(centre > 0.0)
            | _ -> None

        let hold (v: float) =
            match positiveSide with
            | Some true when v < 0.0 -> 0.0
            | Some false when v > 0.0 -> -0.0
            | _ -> v

        let decreasing =
            match link with
            | GlmLink.Inverse
            | GlmLink.InverseSquare -> true
            | _ -> false

        let map (v: float) =
            let m = inverseLink link (hold v)
            if finite m then Some m else None

        if decreasing then map hi, map lo else map lo, map hi

    /// A Tweedie power some Tweedie distribution has: finite, and not strictly between zero and one
    /// (Phase 598).
    let private invalidPower (family: GlmFamily) : float option =
        match family with
        | GlmFamily.Tweedie p when not (finite p) || (p > 0.0 && p < 1.0) -> Some p
        | _ -> None

    /// A caller's θ the negative binomial has: finite and positive (Phase 600).
    let private invalidTheta (family: GlmFamily) : float option =
        match family with
        | GlmFamily.NegativeBinomial(Some t) when not (finite t && t > 0.0) -> Some t
        | _ -> None

    /// Whether a finite `v` is a whole number.
    let private whole (v: float) : bool = v % 1.0 = 0.0

    /// A response the pair admits (D49 item 15; Phase 598; Phase 600, D58 item 4): finite; not
    /// negative under Poisson, quasi-Poisson and the negative binomial, and a whole count where θ is
    /// estimated (its likelihood is the count's); positive under Gamma and inverse Gaussian; in `[0, 1]`
    /// under the binomials; under Tweedie, positive at a power of two or more and not negative below
    /// it; under Gaussian, positive under the log link and not zero under the inverse link, so the start
    /// `g(y)` exists.
    let private admits (family: GlmFamily) (link: GlmLink) (y: float) : bool =
        finite y
        && (match family with
            | GlmFamily.Gaussian ->
                match link with
                | GlmLink.Log -> y > 0.0
                | GlmLink.Inverse -> y <> 0.0
                | _ -> true
            | GlmFamily.Poisson
            | GlmFamily.QuasiPoisson
            | GlmFamily.NegativeBinomial(Some _) -> y >= 0.0
            | GlmFamily.NegativeBinomial None -> y >= 0.0 && whole y
            | GlmFamily.Gamma
            | GlmFamily.InverseGaussian -> y > 0.0
            | GlmFamily.Binomial
            | GlmFamily.QuasiBinomial -> y >= 0.0 && y <= 1.0
            | GlmFamily.Tweedie p -> if p >= 2.0 then y > 0.0 else y >= 0.0)

    /// `ln(1 + u)` for `u > −1` by Goldberg's correction (What Every Computer Scientist Should Know About
    /// Floating-Point Arithmetic, 1991, theorem 4): `ln w · u / (w − 1)` with `w = 1 + u`, and `u` itself
    /// where `w` rounds to one, so a `u` far below one keeps its relative precision.
    let private ln1p (u: float) : float =
        let w = 1.0 + u
        if w = 1.0 then u else Elementary.ln w * u / (w - 1.0)

    /// `y·ln(y/μ)`, pinned to zero at `y = 0`.
    let private yLogY (y: float) (mu: float) : float =
        if y = 0.0 then 0.0 else y * Elementary.ln (y / mu)

    /// One row's unit deviance (D49 item 18; Phase 598, D57; Phase 600, D58): Gaussian `(y − μ)²`;
    /// Poisson and quasi-Poisson `2 (y·ln(y/μ) − (y − μ))`; Gamma `2 (−ln(y/μ) + (y − μ)/μ)`; Tweedie at
    /// `p = 1` Poisson's and at `p = 2` Gamma's, and otherwise `2 (y·(y₁^(1−p) − μ^(1−p))/(1 − p) −
    /// (y^(2−p) − μ^(2−p))/(2 − p))` with `y₁ = y`, or `1` at `y = 0` (R's `statmod::tweedie`); the
    /// binomials `2 (y·ln(y/μ) + (1 − y)·ln((1 − y)/(1 − μ)))`; inverse Gaussian `(y − μ)²/(y·μ²)`; the
    /// negative binomial `2 (y·ln(y/μ) − (y + θ)·ln(1 + (y − μ)/(μ + θ)))`. Each `y·ln(y/μ)` is pinned to
    /// zero at `y = 0`, and the binomial's second term to zero at `y = 1`.
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
        | GlmFamily.Binomial
        | GlmFamily.QuasiBinomial ->
            let other =
                if y = 1.0 then
                    0.0
                else
                    (1.0 - y) * Elementary.ln ((1.0 - y) / (1.0 - mu))

            2.0 * (yLogY y mu + other)
        | GlmFamily.InverseGaussian ->
            let r = y - mu
            r * r / (y * mu * mu)
        | GlmFamily.NegativeBinomial(Some theta) ->
            // `ln((y + θ)/(μ + θ))` as `ln(1 + (y − μ)/(μ + θ))`, which keeps its precision as θ grows.
            2.0 * (yLogY y mu - (y + theta) * ln1p ((y - mu) / (mu + theta)))
        | GlmFamily.NegativeBinomial None -> nan

    /// The first row whose mean leaves the pair's range (`validPredictor`, and not positive under the
    /// log link) or, at a row of positive weight, whose unit deviance is not finite.
    let private firstBad
        (family: GlmFamily)
        (link: GlmLink)
        (y: float[])
        (w: float[])
        (eta: float[])
        (mu: float[])
        : int option =
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
                || (w[i] > 0.0 && not (finite (unitDeviance family y[i] m)))

            if outside then
                bad <- Some i

            i <- i + 1

        bad

    /// The refusal of a mean out of range at `row`: `Separation` under the log link (its overflow, Phase
    /// 306) and under the binomials; `MeanOutOfRange` otherwise (D58 item 3).
    let private outOfRange (family: GlmFamily) (link: GlmLink) (row: int) : LinAlgError =
        match family, link with
        | _, GlmLink.Log
        | GlmFamily.Binomial, _
        | GlmFamily.QuasiBinomial, _ -> LinAlgError.Separation row
        | _ -> LinAlgError.MeanOutOfRange row

    /// The deviance: `Σ w · unit deviance` over the rows of positive weight, in row order, from `+0`. A
    /// zero-weight row is not counted (D49 item 13), and leaving it out of the fold is the same bits as
    /// adding its `+0` wherever its unit deviance is finite.
    let internal deviance (family: GlmFamily) (y: float[]) (w: float[]) (mu: float[]) : float =
        let mutable acc = 0.0

        for i in 0 .. y.Length - 1 do
            if w[i] > 0.0 then
                acc <- acc + w[i] * unitDeviance family y[i] mu[i]

        acc

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
    /// the pairing, the Tweedie power, the negative binomial's θ, the weights, the responses, the
    /// design, the offset (D49 item 15; D58 item 4).
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
        elif not (admitted family link) then
            Error(LinAlgError.UnsupportedModel(familyText family, linkText link))
        else
            match invalidPower family, invalidTheta family with
            | Some p, _ -> Error(LinAlgError.InvalidPower p)
            | None, Some t -> Error(LinAlgError.InvalidTheta t)
            | None, None ->
                match firstIndex (fun v -> not (finite v) || v < 0.0) w with
                | Some r -> Error(LinAlgError.InvalidWeight r)
                | None ->
                    match firstIndex (fun v -> not (admits family link v)) y with
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

    /// The binomials' separation rule (D58 item 5): at the end of the iteration, the first row of
    /// positive weight whose fitted probability lies within `2⁻²⁰` of zero or one.
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

    /// The start of an iteration (D49 item 14; Phase 598; D58 item 6): `μ₀ = y + 0.1` for Poisson and
    /// quasi-Poisson; `y` with `0.1` in place of a zero for Tweedie; `(w·y + 0.5)/(w + 1)` for the
    /// binomials; `y` with `1/6` in place of a zero for the negative binomial; `y` otherwise; and
    /// `η₀ = g(μ₀)`.
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

        mu0 |> Array.map (linkFunction link), mu0

    /// IRLS from `(η₀, μ₀)` under a family whose variance is known (every family but an estimated
    /// negative binomial), with `observe iteration workingWeights` called once per iteration before its
    /// solve.
    let private irls
        (observe: int -> float[] -> unit)
        (family: GlmFamily)
        (link: GlmLink)
        (control: GlmControl)
        (x: Matrix)
        (ya: float[])
        (w: float[])
        (o: float[])
        (eta0: float[])
        (mu0: float[])
        : Result<GlmFit, LinAlgError> =
        let n = x.Rows
        let p = x.Cols
        let xs = Matrix.toColumnMajor x

        let finish (beta: float option[]) (eta: float[]) (mu: float[]) (dev: float) (iterations: int) (ww: float[]) ls =
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
            let pearsonTerm (v: float -> float) (i: int) =
                let d = ya[i] - mu[i]
                w[i] * (d * d / v mu[i])

            let phi =
                match family with
                | GlmFamily.Poisson
                | GlmFamily.Binomial
                | GlmFamily.NegativeBinomial _ -> Some 1.0
                | GlmFamily.Gaussian -> if df > 0 then Some(dev / float df) else None
                | GlmFamily.Gamma when link = GlmLink.Log ->
                    // Σ w·r², r = (y − μ)/μ the working residual of the log link.
                    pearson (fun i ->
                        let r = (ya[i] - mu[i]) / mu[i]
                        w[i] * (r * r))
                | GlmFamily.Gamma -> pearson (pearsonTerm (fun m -> m * m))
                | GlmFamily.QuasiPoisson -> pearson (pearsonTerm id)
                | GlmFamily.Tweedie pw -> pearson (pearsonTerm (fun m -> Elementary.pow m pw))
                | GlmFamily.QuasiBinomial -> pearson (pearsonTerm (fun m -> m * (1.0 - m)))
                | GlmFamily.InverseGaussian -> pearson (pearsonTerm (fun m -> m * m * m))

            let residuals =
                match link with
                | GlmLink.Identity -> Array.init n (fun i -> ya[i] - mu[i])
                | GlmLink.Log -> Array.init n (fun i -> (ya[i] - mu[i]) / mu[i])
                | _ -> Array.init n (fun i -> (ya[i] - mu[i]) / meanDerivative link eta[i])

            let theta =
                match family with
                | GlmFamily.NegativeBinomial t -> t
                | _ -> None

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
                Kernels.adopt residuals,
                theta,
                None,
                0
            )

        match family, link with
        | GlmFamily.Gaussian, GlmLink.Identity ->
            // One solve: the working response is `y − offset` and the working weight the prior
            // weight, independent of `μ`, so the first solve is the fixed point (Phase 595's entry).
            observe 1 w
            let z = Array.init n (fun i -> ya[i] - o[i])

            solve x z w
            |> Result.map (fun ls ->
                let beta = ls.Coefficients |> List.toArray
                let eta = predictor xs n beta o
                let mu = eta |> Array.map (inverseLink link)
                finish beta eta mu (deviance family ya w mu) 1 w ls)
        | _ ->
            let evaluate (beta: float option[]) =
                let eta = predictor xs n beta o
                let mu = eta |> Array.map (inverseLink link)
                eta, mu, firstBad family link ya w eta mu

            let rec iterate
                (iteration: int)
                (betaOld: float option[] option)
                (eta: float[])
                (mu: float[])
                (devPrev: float)
                : Result<GlmFit, LinAlgError> =
                // The working response `z = (η − offset) + (y − μ)/(dμ/dη)` and weight
                // `w·(dμ/dη)²/V(μ)`. Under the log link `dμ/dη` is the mean itself, so `z` divides by
                // `μ`, and the weight keeps Phase 595's and 598's spellings: `w·μ` under Poisson and
                // quasi-Poisson, the prior weight under Gamma, `w·μ^(2−p)` under Tweedie.
                let dmu =
                    match link with
                    | GlmLink.Log -> mu
                    | _ -> eta |> Array.map (meanDerivative link)

                let z = Array.init n (fun i -> (eta[i] - o[i]) + (ya[i] - mu[i]) / dmu[i])

                let ww =
                    match family, link with
                    | (GlmFamily.Poisson | GlmFamily.QuasiPoisson), GlmLink.Log -> Array.init n (fun i -> w[i] * mu[i])
                    | GlmFamily.Tweedie pw, _ -> Array.init n (fun i -> w[i] * Elementary.pow mu[i] (2.0 - pw))
                    | GlmFamily.Gamma, GlmLink.Log -> Array.copy w
                    | _ -> Array.init n (fun i -> w[i] * (dmu[i] * dmu[i] / variance family mu[i]))

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
                            // with μ₀'s, which is the start, not a point of the model.
                            match badN with
                            | Some r -> Error(outOfRange family link r)
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
                                    | Some r -> Error(outOfRange family link r)
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
                        // D49 item 17, in that order, the comparison strict; the binomials' separation
                        // rule (D58 item 5) is read wherever the iteration ends.
                        if abs (dev - devPrev) / (abs dev + 0.1) < control.Tol then
                            match separated family w mu' with
                            | Some r -> Error(LinAlgError.Separation r)
                            | None -> Ok(finish beta eta' mu' dev iteration ww ls)
                        elif iteration >= control.MaxIter then
                            match separated family w mu' with
                            | Some r -> Error(LinAlgError.Separation r)
                            | None -> Error(LinAlgError.NotConverged iteration)
                        else
                            iterate (iteration + 1) (Some beta) eta' mu' dev))

            iterate 1 None eta0 mu0 (deviance family ya w mu0)

    // ---- the negative binomial's θ (D58 item 8, after MASS's glm.nb and theta.ml) -----------------

    /// `Σ_{k<y} f(θ + k)`, `k` ascending from `+0`, over a whole count `y` — the difference of digamma,
    /// trigamma or log-gamma functions at `θ + y` and `θ`, exactly as a finite sum.
    let private countSum (f: float -> float) (theta: float) (y: float) : float =
        let mutable acc = 0.0
        let mutable k = 0.0

        while k < y do
            acc <- acc + f (theta + k)
            k <- k + 1.0

        acc

    /// The θ score `Σ w·(ψ(θ + y) − ψ(θ) + ln θ + 1 − ln(θ + μ) − (y + θ)/(μ + θ))` and the observed
    /// information `Σ w·(ψ′(θ) − ψ′(θ + y) − 1/θ + 2/(μ + θ) − (y + θ)/(μ + θ)²)` over the rows of positive
    /// weight, in row order.
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

    /// `2⁻¹³`, MASS's `.Machine$double.eps^0.25`: the Newton step on θ below which θ has converged.
    let private thetaStepTol = 1.220703125e-4

    /// `2⁵⁰`: a θ estimate beyond it is the Poisson boundary, not a negative binomial.
    let private thetaBound = 1125899906842624.0

    /// θ by Newton's method on its score at fixed means (MASS's `theta.ml`): from
    /// `θ₀ = Σw / Σ w·(y/μ − 1)²`, `θ ← |θ| + score(|θ|)/info(|θ|)` until a step is at most `2⁻¹³` in
    /// size, at most `MaxIter − 1` steps; answers θ and the information at it. A start or a step that is
    /// not finite, a θ that is not positive, a θ above `2⁵⁰` — data no more dispersed than Poisson's,
    /// whose likelihood rises without bound towards the Poisson limit — or a last step still above the
    /// tolerance is `ThetaNotConverged alternation`.
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

        let mutable t = sw / sr
        let mutable step = 1.0
        let mutable it = 1

        while finite t && it < control.MaxIter && abs step > thetaStepTol do
            t <- abs t
            let score, info = thetaScoreInfo ya w mu t
            step <- score / info
            t <- t + step
            it <- it + 1

        if
            not (finite t && finite step)
            || t <= 0.0
            || t > thetaBound
            || abs step > thetaStepTol
        then
            Error(LinAlgError.ThetaNotConverged alternation)
        else
            let _, info = thetaScoreInfo ya w mu t
            Ok(t, info)

    /// The negative binomial log-likelihood at θ and the means, without its `−Σ w·ln Γ(y + 1)` term, which
    /// no comparison between alternations reads: `Σ w·(Σ_{k<y} ln(θ + k) + θ·ln θ + y·ln(μ + [y = 0]) −
    /// (θ + y)·ln(θ + μ))` over the rows of positive weight, in row order.
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

    /// The negative binomial with θ estimated (D58 item 8): Poisson under the same link from its own
    /// start; θ by `thetaMl` at its means; then alternations, each an IRLS at the current θ from the
    /// previous fit's linear predictor and a fresh θ at the new means, while
    /// `|ℓ₀ − ℓ|/d₁ + |Δθ|/d₂ > tol` (MASS's `glm.nb`: `d₁ = √(2·max(1, df))`, `d₂ = 1`, `ℓ` the
    /// log-likelihood), at most `MaxIter` of them. The coefficients are the last alternation's, fitted at
    /// the θ before its re-estimate; `Theta` is the re-estimate and `ThetaStdError` `1/√I` at it.
    let private fitNegativeBinomial
        (observe: int -> float[] -> unit)
        (link: GlmLink)
        (control: GlmControl)
        (x: Matrix)
        (ya: float[])
        (w: float[])
        (o: float[])
        : Result<GlmFit, LinAlgError> =
        let eta0, mu0 = start GlmFamily.Poisson link ya w

        irls observe GlmFamily.Poisson link control x ya w o eta0 mu0
        |> Result.bind (fun poisson ->
            let mu = Vector.toArray poisson.Fitted

            thetaMl control 0 ya w mu
            |> Result.bind (fun (theta, info) ->
                let d1 = sqrt (2.0 * float (max 1 poisson.ResidualDf))
                let lm = thetaLogLik ya w mu theta

                let rec alternate
                    (k: int)
                    (fit: GlmFit)
                    (theta: float)
                    (info: float)
                    (lm0: float)
                    (lm: float)
                    (step: float)
                    : Result<GlmFit, LinAlgError> =
                    if not (abs (lm0 - lm) / d1 + abs step > control.Tol) then
                        let se =
                            if info > 0.0 then
                                Some(Kernels.canonical (sqrt (1.0 / info)))
                            else
                                None

                        Ok(
                            GlmFit(
                                fit.Coefficients,
                                fit.StdErrors,
                                fit.Fitted,
                                fit.LinearPredictor,
                                fit.Deviance,
                                fit.Dispersion,
                                fit.Iterations,
                                fit.LeastSquares,
                                fit.WorkingWeights,
                                fit.WorkingResiduals,
                                Some(Kernels.canonical theta),
                                se,
                                k
                            )
                        )
                    elif k >= control.MaxIter then
                        Error(LinAlgError.ThetaNotConverged k)
                    else
                        let family = GlmFamily.NegativeBinomial(Some theta)
                        let eta = Vector.toArray fit.LinearPredictor
                        let mu = Vector.toArray fit.Fitted

                        irls observe family link control x ya w o eta mu
                        |> Result.bind (fun next ->
                            let muN = Vector.toArray next.Fitted

                            thetaMl control (k + 1) ya w muN
                            |> Result.bind (fun (thetaN, infoN) ->
                                alternate
                                    (k + 1)
                                    next
                                    thetaN
                                    infoN
                                    lm
                                    (thetaLogLik ya w muN thetaN)
                                    (theta - thetaN)))

                alternate 0 poisson theta info (lm + 2.0 * d1) lm 1.0))

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
            match family with
            | GlmFamily.NegativeBinomial None -> fitNegativeBinomial observe link control x ya w o
            | _ ->
                let eta0, mu0 = start family link ya w
                irls observe family link control x ya w o eta0 mu0)

    /// The generalised linear model of `y` on the columns of `x` under `family` and `link`, with prior
    /// `weights` and an `offset` added to the linear predictor (Phase 595, D49 items 14 to 19; Phase
    /// 598, D57; Phase 600, D58).
    ///
    /// `Gaussian` under `Identity` is one weighted least-squares solve of `y − offset` — the same fold as
    /// `QRAccumulator.addRows` into an empty accumulator and `LeastSquares.ofAccumulator`, byte for byte
    /// — and reports one iteration. Every other admitted pair (`admitted`) iterates from `η₀ = g(μ₀)`
    /// (`μ₀` per family: `y + 0.1` for Poisson, `(w·y + 0.5)/(w + 1)` for the binomials, `y` with `1/6`
    /// for a zero under the negative binomial, `y` with `0.1` for a zero under Tweedie, `y` otherwise),
    /// solving for `z = (η − offset) + (y − μ)/(dμ/dη)` under working weights `w·(dμ/dη)²/V(μ)`. Each
    /// halves a step at most ten times on a mean out of range or a deviance rise the stopping rule
    /// would see (`(dev − dev_prev) / (|dev| + 0.1) ≥ tol`, from the second iteration), and stops when
    /// `|dev − dev_prev| / (|dev| + 0.1) < tol`. `NegativeBinomial None` alternates with θ (D58 item 8).
    ///
    /// Refused before any iteration, each naming the first offending row: a shape mismatch, a pairing
    /// not admitted (`UnsupportedModel`), a Tweedie power strictly between zero and one or not finite
    /// (`InvalidPower`), a θ not finite and positive (`InvalidTheta`), a negative or non-finite weight
    /// (`InvalidWeight`), a response outside the pair (`InvalidResponse`), a non-finite design cell
    /// (`NotFinite (row, column)`) or offset (`NotFinite (row, cols x)`). Refused while iterating: a
    /// mean no halving brings into range (`Separation` under the log link, its overflow; `MeanOutOfRange`
    /// otherwise), a binomial fitted probability within `2⁻²⁰` of zero or one where the iteration ends
    /// (`Separation`), a deviance no halving brings down, or no convergence by `MaxIter` (`NotConverged`,
    /// naming the iteration), and a θ that does not converge (`ThetaNotConverged`).
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
    /// `μᵢ = m + offsetᵢ` with `m = Σw(y − offset) / Σw`; under any other link with every offset zero the
    /// weighted mean `Σwy / Σw`, and with an offset the model fitted by `fit`, whose refusals it shares.
    /// Each sum is folded in row order; the deviance over the rows of positive weight. An estimated
    /// negative binomial has no null model of its own: pass the fit's `Theta` (`NegativeBinomial None`
    /// is refused as `InvalidTheta NaN`).
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

        match family with
        | GlmFamily.NegativeBinomial None -> Error(LinAlgError.InvalidTheta nan)
        | _ ->
            validate family link ones ya w o
            |> Result.bind (fun () ->
                let devOf (eta: float[]) (mu: float[]) =
                    match firstBad family link ya w eta mu with
                    | Some r -> Error(outOfRange family link r)
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
                    devOf o (o |> Array.map (inverseLink link))
                else
                    match link with
                    | GlmLink.Identity ->
                        let m = weightedMean ()
                        let mu = o |> Array.map (fun v -> m + v)
                        devOf mu mu
                    | _ when o |> Array.forall (fun v -> v = 0.0) ->
                        let m = weightedMean ()
                        devOf (Array.create n (linkFunction link m)) (Array.create n m)
                    | _ ->
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
