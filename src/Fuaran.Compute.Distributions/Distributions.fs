namespace Fuaran.Compute.Distributions

open System
open Fuaran.Compute.Solvers
open Fuaran.Compute.Elementary

// ============================================================================
//  Phase 618 — the pinned distribution functions: the cumulative distribution
//  function and the quantile of the standard normal, Student's t, the
//  chi-square and Fisher's F distributions (DECISIONS.md D79).
//
//  Every function is `+ − × ÷`, comparisons, the correctly rounded square root
//  and the pinned functions of Fuaran.Compute.Elementary (`exp`, `ln`, the
//  normal pair, and `Special`'s incomplete gamma and beta functions and the
//  beta function's inverse), in one order, with no fused multiply-add and no
//  host mathematics call, so a host that transcribes them answers the same
//  bits. No special function is written here. The one iteration, the
//  chi-square quantile, is `Root.bracketedNewton` under a stated
//  `StoppingRule`; the t and F quantiles read `Special.inverseRegularizedBeta`.
//
//  The accuracy stated for each function is measured against a fifty-digit
//  reference (`tests/Fuaran.Core.Compute.Tests/DistributionsReference/
//  distributions_reference.py`, D79 and benchmarks/results/), not proved, and
//  it inherits the special functions' own (D78).
//
//  FSharp.Core, the solvers and Elementary only; Fable-clean.
// ============================================================================

/// What the four families share: the float tests, and the incomplete beta function read at an
/// argument near one.
module internal Kernel =

    /// `2⁻¹⁰²²`, the smallest normal double.
    let minNormal = 2.2250738585072014e-308

    /// `2⁻²⁶`: the smallest distance from one at which `betaNearOne` is read.
    let nearOneFrom = 1.4901161193847656e-08

    /// Neither an infinity nor a NaN.
    let finite (x: float) : bool = x - x = 0.0

    /// A parameter a distribution admits: above zero and finite.
    let positiveFinite (a: float) : bool = a > 0.0 && finite a

    /// A probability a quantile refuses: a NaN, or outside `[0, 1]`.
    let notProbability (p: float) : bool = Double.IsNaN p || p < 0.0 || p > 1.0

    /// The density of the beta distribution at `x` with `y = 1 − x`, both inside `(0, 1)`:
    /// `exp ((a − 1) ln x + (b − 1) ln y − ln B(a, b))`. A derivative and a first-order correction read
    /// it; neither needs its last digits.
    let betaDensity (a: float) (b: float) (x: float) (y: float) : float =
        Elementary.exp ((a - 1.0) * Elementary.ln x + (b - 1.0) * Elementary.ln y - Special.betaLn a b)

    /// `I_{1−y}(a, b)` for `y` in `[2⁻²⁶, ½)`, where the caller holds `y` and not `1 − y`.
    ///
    /// The incomplete beta function takes its argument as one double, and `1 − y` is not one: the
    /// subtraction rounds away up to `2⁻⁵⁴`, which a large `a` multiplies into the answer. So the
    /// function is read at `x = fl(1 − y)`, whose distance from one, `u = 1 − x`, is exact; what the
    /// subtraction rounded away, `d = y − u`, is exact too; and the answer is restored to first order
    /// through the density, `I_x − d · xᵃ⁻¹ uᵇ⁻¹ / B(a, b)`. The second-order term is below
    /// `(d / u)² ≤ 2⁻⁵⁶` of the answer's own scale. This is the correction `Special.regularizedBeta`
    /// makes inside itself for an argument below one half (DECISIONS.md D78 item 7), made here for a
    /// caller that holds the complement.
    let betaNearOne (a: float) (b: float) (y: float) : float =
        let x = 1.0 - y
        let u = 1.0 - x
        let d = y - u
        Special.regularizedBeta a b x - d * betaDensity a b x u

    /// The odds `x / (1 − x)` at the root of `I_x(a, b) = p`, for `p` in `(0, 1)` given exactly and
    /// `q = 1 − p` as the caller formed it (`qExact`: that subtraction rounded nothing away).
    ///
    /// `Special.inverseRegularizedBeta a b p` is read first. A root at or below one half is accurate to
    /// its own last place and so are the odds. Above one half the function has answered `1 − y` for a
    /// small `y`, and the odds divide by a `y` the subtraction rounded to the last place of one. Then:
    /// with `q` exact the mirrored problem `I_y(b, a) = q` is read instead, whose root is `y` itself;
    /// with `q` rounded it is read only where the density of that problem at `y` is above one, which is
    /// where the rounding of `q` moves `y` by less than the rounding of `1 − y` did. No iteration is
    /// written here: each root is the special function's own.
    let betaOdds (a: float) (b: float) (p: float) (q: float) (qExact: bool) : float =
        let x = Special.inverseRegularizedBeta a b p

        if not (x > 0.5) then
            x / (1.0 - x)
        else
            let y0 = 1.0 - x

            if qExact || betaDensity b a y0 x > 1.0 then
                let y = Special.inverseRegularizedBeta b a q
                (1.0 - y) / y
            else
                x / y0

/// The standard normal distribution (DECISIONS.md D58, D79). Both functions ARE
/// `Fuaran.Compute.Elementary`'s `Elementary.normalCdf` and `Elementary.normalQuantile`, read under
/// the distribution's name: one implementation, the same bits.
[<RequireQualifiedAccess>]
module Normal =

    /// `Φ(x)`, the probability that a standard normal variate is at most `x`. `Φ(NaN)` is NaN; `0` at
    /// `−∞` and `1` at `+∞`.
    let cdf (x: float) : float = Elementary.normalCdf x

    /// `Φ⁻¹(p)`. `−∞` at `p = 0` and `+∞` at `p = 1`; NaN for a NaN and for a `p` outside `[0, 1]`.
    let quantile (p: float) : float = Elementary.normalQuantile p

/// Student's t distribution with `ν` degrees of freedom, any `ν` above zero (DECISIONS.md D79).
[<RequireQualifiedAccess>]
module StudentT =

    open Kernel

    /// `2³⁰`: from `|t| = 2³⁰ √ν` the tail is its leading term.
    let private farFrom = 1073741824.0

    /// `P(T ≤ −t)` for `t ≥ 0` (which is `P(T ≥ t)`): half the incomplete beta function
    /// `I_z(ν/2, ½)` at `z = ν / (ν + t²)`, in four arms by the size of `w = t² / (ν + t²) = 1 − z`.
    ///
    ///   - `t ≥ 2³⁰ √ν`: `z` is below `2⁻⁶⁰`, and the function is its leading term to the last place,
    ///     `(√ν / t)^ν / (ν B(ν/2, ½))`, the power the pinned `pow`. Nothing overflows or underflows
    ///     before the tail itself does, and the answer does not carry the error of an exponential of a
    ///     large exponent, as the function's own prefix does this far out.
    ///   - `t² (ν + 2) ≤ 3ν` (a `t²` below about three), or `w < 2⁻²⁶`: the centre, `½ − ½ I_w(½, ν/2)`.
    ///     This is where `Special.regularizedBeta ½ (ν/2)` reads its continued fraction at `w` itself
    ///     (`w ≤ (½ + 1) / (½ + ν/2 + 2)`, DECISIONS.md D78 item 7), and the tail is at least `0.04`, so
    ///     the subtraction costs nothing. `betaNearOne` measures the same error here and pays an
    ///     exponential, two logarithms and a `betaLn` for it (D79 item 4).
    ///   - `2t² ≥ ν`: `z` is formed directly and is at most two thirds.
    ///   - otherwise `z` is near one and only `w` is known to its last place, so the function is read
    ///     through `betaNearOne` at `w`. This keeps a small tail's relative accuracy at a large `ν`,
    ///     where `ν / (ν + t²)` formed directly rounds away most of `t`.
    let private upperTail (nu: float) (t: float) : float =
        let a = 0.5 * nu
        let t2 = t * t
        let w = t2 / (nu + t2)

        if t >= farFrom * sqrt nu then
            Elementary.pow (sqrt nu / t) nu / (nu * Special.beta a 0.5)
        elif t2 * (nu + 2.0) <= 3.0 * nu || w < nearOneFrom then
            0.5 - 0.5 * Special.regularizedBeta 0.5 a w
        elif 2.0 * t2 >= nu then
            0.5 * Special.regularizedBeta a 0.5 (nu / (nu + t2))
        else
            0.5 * betaNearOne a 0.5 w

    /// The density at `t`, `(1 + t²/ν)^(−(ν+1)/2) / (√ν B(ν/2, ½))`, as the quantile's one Newton step
    /// reads it: a derivative, accurate to a few digits more than it needs.
    let private density (nu: float) (t: float) : float =
        let z = nu / (nu + t * t)

        Elementary.exp (
            0.5 * (nu + 1.0) * Elementary.ln z
            - 0.5 * Elementary.ln nu
            - Special.betaLn (0.5 * nu) 0.5
        )

    /// `P(T ≤ x)`.
    ///
    /// The tail beyond `|x|` is computed once (see `upperTail`) and is the answer for `x < 0`; for
    /// `x ≥ 0` the answer is one minus it. So a lower tail keeps its relative accuracy, and
    /// `cdf ν (−x)` and `cdf ν x` sum to one within a rounding.
    ///
    /// `0` at `−∞` and `1` at `+∞`. NaN for a NaN and for a `ν` not above zero or not finite.
    let cdf (nu: float) (x: float) : float =
        if Double.IsNaN x || not (positiveFinite nu) then Double.NaN
        elif x < 0.0 then upperTail nu -x
        else 1.0 - upperTail nu x

    /// The quantile at a `p` in `(0, ½)`: negative.
    ///
    /// `t² / ν` is the odds of the root of an incomplete beta function, and the two ways of writing it
    /// take `2p` and `1 − 2p`. From `p = ¼` both are exact, and `betaOdds` reads whichever root is the
    /// small one. Below `¼` only `2p` is exact: `I_z(ν/2, ½) = 2p` is read, and its root `z` is accurate
    /// while it is at most one half (`t² ≥ ν`). Beyond that `1 − z` has lost `1 / (1 − z)` units:
    ///
    ///   - `1 − z ≥ 1/16`: at most eight units, and the answer stands;
    ///   - `2⁻²⁶ ≤ 1 − z < 1/16`: ONE Newton step on the distribution function restores it, the
    ///     residual read through `upperTail`, which is accurate to the tail's own last places there;
    ///   - below `2⁻²⁶` (a `ν` beyond `10⁷`): `I_w(½, ν/2) = 1 − 2p` is read, `1 − 2p` rounded.
    ///
    /// An answer at or beyond `−2³⁰ √ν`, where `ν / t²` is below `2⁻⁶⁰` (and underflows long before
    /// the quantile overflows), is replaced by the leading term of the tail solved for `t`,
    /// `−√ν / (p ν B(ν/2, ½))^(1/ν)`, the power the pinned `pow`.
    let private lowerQuantile (nu: float) (p: float) : float =
        let a = 0.5 * nu

        let t =
            if p >= 0.25 then
                -(sqrt (nu * betaOdds 0.5 a (1.0 - 2.0 * p) (2.0 * p) true))
            else
                let z = Special.inverseRegularizedBeta a 0.5 (2.0 * p)

                if not (z > 0.5) then
                    -(sqrt (nu * (1.0 - z) / z))
                else
                    let y = 1.0 - z
                    let t = -(sqrt (nu * y / z))

                    if y >= 0.0625 then
                        t
                    elif y >= nearOneFrom then
                        t - (upperTail nu -t - p) / density nu t
                    else
                        let w = Special.inverseRegularizedBeta 0.5 a (1.0 - 2.0 * p)
                        -(sqrt (nu * w / (1.0 - w)))

        if -t >= farFrom * sqrt nu then
            -(sqrt nu / Elementary.pow (p * nu * Special.beta a 0.5) (1.0 / nu))
        else
            t

    /// The `t` with `P(T ≤ t) = p`.
    ///
    /// Below one half it is `lowerQuantile`; above, the negation of the quantile at `1 − p`, a
    /// subtraction that is exact, so the function is odd about one half bit for bit. It reads
    /// `Special.inverseRegularizedBeta` and iterates nothing of its own.
    ///
    /// `0` at `p = ½`, `−∞` at `p = 0` and `+∞` at `p = 1`; `−∞` or `+∞` also where the quantile is
    /// beyond the largest double. NaN for a NaN, for a `ν` not above zero or not finite, for a `p`
    /// outside `[0, 1]`, and where the special function answers NaN.
    let quantile (nu: float) (p: float) : float =
        if notProbability p || not (positiveFinite nu) then
            Double.NaN
        elif p = 0.0 then
            Double.NegativeInfinity
        elif p = 1.0 then
            Double.PositiveInfinity
        elif p = 0.5 then
            0.0
        elif p < 0.5 then
            lowerQuantile nu p
        else
            -(lowerQuantile nu (1.0 - p))

/// The chi-square distribution with `k` degrees of freedom, any `k` above zero (DECISIONS.md D79).
[<RequireQualifiedAccess>]
module ChiSquare =

    open Kernel

    /// `P(X ≤ x) = P(k/2, x/2)`, the regularized lower incomplete gamma function.
    ///
    /// `0` for every `x ≤ 0` and `1` at `+∞`. NaN for a NaN, for a `k` not above zero or not finite, and
    /// where the special function answers NaN (a `k` beyond about `10⁸` with `x` near it).
    let cdf (k: float) (x: float) : float =
        if Double.IsNaN x || not (positiveFinite k) then Double.NaN
        elif x <= 0.0 then 0.0
        else Special.regularizedGammaP (0.5 * k) (0.5 * x)

    /// The quantile's rule: Newton stops when a step moved the point by less than `2⁻⁴⁰` of itself, or
    /// at one hundred iterations, where the function answers NaN.
    let private rule: StoppingRule =
        StoppingRule.create 100 (StoppingTest.RelativeChange(9.094947017729282e-13, minNormal))

    /// `ln 2`.
    let private ln2 = 0.6931471805599453

    /// `(f u, f′ u)` for the root of `P(a, u) = p`, with `q = 1 − p`: `P − p`, read as `q − Q` where
    /// `p > ½` so an upper tail keeps its precision, and the density `uᵃ⁻¹ e⁻ᵘ / Γ(a)`. At `u = 0` the
    /// value is `−p`.
    let private residual (a: float) (p: float) (q: float) (u: float) : float * float =
        if u = 0.0 then
            -p, 0.0
        else
            (if p <= 0.5 then
                 Special.regularizedGammaP a u - p
             else
                 q - Special.regularizedGammaQ a u),
            Elementary.exp ((a - 1.0) * Elementary.ln u - u - Special.gammaLn a)

    /// The `x` with `P(X ≤ x) = p`: twice the root `u` of `P(k/2, u) = p`, found by
    /// `Root.bracketedNewton`.
    ///
    ///   - The residual is `P(a, u) − p`, read as `(1 − p) − Q(a, u)` where `p > ½`; the derivative is
    ///     the density.
    ///   - The bracket is `[0, a + √(2aL) + L]` with `L = ln 2` where `p ≤ ½` and `−ln (1 − p)` above:
    ///     the gamma distribution's tail beyond that point is at most `e⁻ᴸ`, so the residual there is
    ///     not negative.
    ///   - The start is the leading term of the function at zero solved for `u`,
    ///     `exp ((ln p + ln Γ(a + 1)) / a)`, where `a < −0.62 ln p`; otherwise Wilson and Hilferty's
    ///     `a (1 − 1/(9a) + z √(1/(9a)))³` over the pinned normal quantile `z`; and half the bracket
    ///     where that lands outside it.
    ///   - The shrink fraction is one, and the rule: stop when a step moved the point by less than
    ///     `2⁻⁴⁰` of itself, at most one hundred iterations.
    ///
    /// `0` at `p = 0` and `+∞` at `p = 1`; `0` where the start at zero underflows. NaN for a NaN, for a
    /// `k` not above zero or not finite, for a `p` outside `[0, 1]`, and where the solver refuses (the
    /// cap, or a special function that answered NaN).
    let quantile (k: float) (p: float) : float =
        if notProbability p || not (positiveFinite k) then
            Double.NaN
        elif p = 0.0 then
            0.0
        elif p = 1.0 then
            Double.PositiveInfinity
        else
            let a = 0.5 * k
            let q = 1.0 - p
            let l = if p <= 0.5 then ln2 else -(Elementary.ln q)
            let hi = a + sqrt (2.0 * a * l) + l
            let lnP = Elementary.ln p
            let small = a < -0.62 * lnP

            let start =
                if small then
                    Elementary.exp ((lnP + Special.gammaLn (a + 1.0)) / a)
                else
                    let c = 1.0 / (9.0 * a)
                    let s = 1.0 - c + Elementary.normalQuantile p * sqrt c
                    a * s * s * s

            if small && start = 0.0 then
                0.0
            else
                let u0 = if start > 0.0 && start < hi then start else 0.5 * hi

                let solved: Result<Stopped<float>, SolverError<float, unit>> =
                    Root.bracketedNewton rule 1.0 (residual a p q) 0.0 hi u0

                match solved with
                | Ok at -> 2.0 * at.State
                | Error _ -> Double.NaN

/// Fisher's F distribution with `d1` and `d2` degrees of freedom, each above zero (DECISIONS.md D79).
[<RequireQualifiedAccess>]
module FisherF =

    open Kernel

    /// `P(F ≤ x) = I_w(d1/2, d2/2)` at `w = d1 x / (d1 x + d2)`.
    ///
    /// Where `d1 x ≤ d2` the function is read at `w`, formed directly. Beyond, `w` is near one and it is
    /// `z = d2 / (d1 x + d2)` that is known to its last place: the function is read through
    /// `betaNearOne` at `z` (so a small probability at a large `d1` keeps its relative accuracy), and
    /// as `1 − I_z(d2/2, d1/2)` where `z` is below `2⁻²⁶`.
    ///
    /// `0` for every `x ≤ 0` and `1` at `+∞`. NaN for a NaN, for a `d1` or `d2` not above zero or not
    /// finite, and where the special function answers NaN.
    let cdf (d1: float) (d2: float) (x: float) : float =
        if Double.IsNaN x || not (positiveFinite d1 && positiveFinite d2) then
            Double.NaN
        elif x <= 0.0 then
            0.0
        else
            let a = 0.5 * d1
            let b = 0.5 * d2
            let n = d1 * x

            if n <= d2 then
                Special.regularizedBeta a b (n / (n + d2))
            else
                let z = d2 / (n + d2)

                if z >= nearOneFrom then
                    betaNearOne a b z
                else
                    1.0 - Special.regularizedBeta b a z

    /// The `x` with `P(F ≤ x) = p`: `(d2 / d1) · w / (1 − w)` at the root `w` of
    /// `I_w(d1/2, d2/2) = p`.
    ///
    /// Up to `p = ½` the odds are `betaOdds` of that problem, `1 − p` rounded. Above, `1 − p` is exact
    /// and the mirrored problem `I_z(d2/2, d1/2) = 1 − p` is the one read, the answer the reciprocal of
    /// its odds. It reads `Special.inverseRegularizedBeta` and iterates nothing of its own.
    ///
    /// `0` at `p = 0` and `+∞` at `p = 1`; `0` or `+∞` also where the root is nearer an end than the
    /// smallest double. NaN for a NaN, for a `d1` or `d2` not above zero or not finite, for a `p`
    /// outside `[0, 1]`, and where the special function answers NaN.
    let quantile (d1: float) (d2: float) (p: float) : float =
        if notProbability p || not (positiveFinite d1 && positiveFinite d2) then
            Double.NaN
        elif p = 0.0 then
            0.0
        elif p = 1.0 then
            Double.PositiveInfinity
        else
            let a = 0.5 * d1
            let b = 0.5 * d2

            if p <= 0.5 then
                d2 * betaOdds a b p (1.0 - p) (p = 0.5) / d1
            else
                d2 / (d1 * betaOdds b a (1.0 - p) p true)
