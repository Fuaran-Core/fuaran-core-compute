namespace Fuaran.Compute.Elementary

open System
open Fuaran.Compute.Solvers

// ============================================================================
//  Phase 631 — the pinned special functions: the gamma function and its
//  logarithm, the beta function and its logarithm, the regularized incomplete
//  gamma and beta functions, the inverse of the regularized incomplete beta,
//  and the polygamma functions (DECISIONS.md D78).
//
//  Every function is written with `+ − × ÷`, comparisons, the correctly
//  rounded square root and this package's own `exp`, `ln` and `pow`, in one
//  order, with no fused multiply-add and no host mathematics call, so a host
//  that transcribes them answers the same bits. Every iteration stops on a
//  `StoppingRule` of the solvers package, at a stated cap; the inverse is
//  `Root.bracketedNewton`.
//
//  The two kernels' coefficients (`ln Γ` round its zeros at one and two, the
//  digamma function round its positive root) are Chebyshev fits generated at
//  fifty digits by `tests/Fuaran.Core.Compute.Tests/SpecialReference/
//  special_reference.py`, which also computes every reference the vectors
//  carry. The accuracy stated for each function is measured against that
//  reference (D78 and benchmarks/results/), not proved.
//
//  FSharp.Core and the solvers only; Fable-clean.
// ============================================================================

/// The pinned special functions (DECISIONS.md D78). A NaN argument answers NaN everywhere; what each
/// function answers outside its domain and at its bounds is stated on it.
[<RequireQualifiedAccess>]
module Special =

    // ---- constants --------------------------------------------------------------------------------

    /// `2⁻⁵²`.
    let private eps = 2.220446049250313e-16

    /// The smallest normal double, `2⁻¹⁰²²`.
    let private minNormal = 2.2250738585072014e-308

    /// The floor a continued fraction's denominator is held to (Lentz's method as Thompson and Barnett
    /// modify it).
    let private tiny = 1.0e-300

    let private pi = 3.141592653589793
    let private twoPi = 6.283185307179586

    /// `½ ln 2π`.
    let private halfLnTwoPi = 0.9189385332046728

    /// `½ ln 2π − ½`.
    let private stirlingConstant = 0.4189385332046727

    /// `√(2π)`.
    let private sqrtTwoPi = 2.5066282746310007

    /// At and above this argument `ln Γ`, `Γ`, `B` and the incomplete functions' prefixes read
    /// Stirling's series; below it, the kernel.
    let private stirlingFrom = 10.0

    /// The largest argument whose `Γ` is a finite double.
    let private gammaOverflow = 171.62437695630272

    let private magnitude (x: float) : float = if x < 0.0 then -x else x

    /// Neither an infinity nor a NaN.
    let private finite (x: float) : bool = x - x = 0.0

    // ---- the stopping rules -----------------------------------------------------------------------

    /// Every series and continued fraction here stops when its running value moved by less than
    /// `2⁻⁵²` of itself, `|current − previous| / (|current| + 2⁻¹⁰²²) < 2⁻⁵²`, or at 65536 terms, where
    /// the function answers NaN.
    let private seriesRule: StoppingRule =
        StoppingRule.create 65536 (StoppingTest.RelativeChange(eps, minNormal))

    let inline private settled (previous: float) (current: float) : bool =
        StoppingRule.stops seriesRule.Test previous current (current - previous) 0.0 0.0

    // ---- fixed-length series ----------------------------------------------------------------------

    /// `Σ_{j=1..18} s2^(j−1) / (2j + 1)`: the tail of `atanh s / s` past its first term, for `s2 ≤ 1/9`.
    let private atanhTail (s2: float) : float =
        let mutable r = 1.0 / 37.0
        let mutable j = 17

        while j >= 1 do
            r <- r * s2 + 1.0 / float (2 * j + 1)
            j <- j - 1

        r

    /// `ln (1 + m)` for `m > −1`: within `[−½, ½]` it is `2 atanh (m / (2 + m))` by the fixed series,
    /// outside it the pinned `ln`.
    let private ln1p (m: float) : float =
        if m >= -0.5 && m <= 0.5 then
            let s = m / (2.0 + m)
            let s2 = s * s
            2.0 * s * (1.0 + s2 * atanhTail s2)
        else
            Elementary.ln (1.0 + m)

    /// `m − ln (1 + m)`, which is never negative. `ratio` is `1 + m` as the caller holds it most
    /// exactly, read outside `[−½, ½]` only.
    let private mMinusLn1p (m: float) (ratio: float) : float =
        if m >= -0.5 && m <= 0.5 then
            let s = m / (2.0 + m)
            let s2 = s * s
            m * s - 2.0 * s * s2 * atanhTail s2
        else
            m - Elementary.ln ratio

    /// `1 / k!` for `k = 1 .. 13`, highest first: `exp y − 1` within `[−½, ½]` is `y` times their
    /// polynomial.
    let private inverseFactorials: float[] =
        [| 1.0 / 6227020800.0
           1.0 / 479001600.0
           1.0 / 39916800.0
           1.0 / 3628800.0
           1.0 / 362880.0
           1.0 / 40320.0
           1.0 / 5040.0
           1.0 / 720.0
           1.0 / 120.0
           1.0 / 24.0
           1.0 / 6.0
           1.0 / 2.0
           1.0 |]

    /// Horner's rule over coefficients held highest degree first.
    let private horner (coefficients: float[]) (u: float) : float =
        let mutable r = coefficients[0]

        for i in 1 .. coefficients.Length - 1 do
            r <- r * u + coefficients[i]

        r

    /// `exp y − 1`: the Taylor polynomial within `[−½, ½]`, the pinned `exp` outside it.
    let private expm1 (y: float) : float =
        if y >= -0.5 && y <= 0.5 then
            y * horner inverseFactorials y
        else
            Elementary.exp y - 1.0

    // ---- sin πx and cos πx ------------------------------------------------------------------------

    /// `(−1)ᵏ / (2k + 1)!` for `k = 9 .. 1`.
    let private sinTaylor: float[] =
        [| -1.0 / 121645100408832000.0
           1.0 / 355687428096000.0
           -1.0 / 1307674368000.0
           1.0 / 6227020800.0
           -1.0 / 39916800.0
           1.0 / 362880.0
           -1.0 / 5040.0
           1.0 / 120.0
           -1.0 / 6.0 |]

    /// `(−1)ᵏ / (2k)!` for `k = 9 .. 1`.
    let private cosTaylor: float[] =
        [| -1.0 / 6402373705728000.0
           1.0 / 20922789888000.0
           -1.0 / 87178291200.0
           1.0 / 479001600.0
           -1.0 / 3628800.0
           1.0 / 40320.0
           -1.0 / 720.0
           1.0 / 24.0
           -1.0 / 2.0 |]

    /// `sin y` for `|y| ≤ π/4`, its Taylor polynomial to `y¹⁹`.
    let private sinKernel (y: float) : float =
        let z = y * y
        y + y * z * horner sinTaylor z

    /// `cos y` for `|y| ≤ π/4`, its Taylor polynomial to `y¹⁸`.
    let private cosKernel (y: float) : float =
        let z = y * y
        1.0 + z * horner cosTaylor z

    /// `(sin πx, cos πx)` for a finite `|x| < 2⁵²`. `x` is split exactly into its nearest integer and a
    /// remainder in `[−½, ½]`; the remainder's sine and cosine are a kernel at `πr` within a quarter, and
    /// the other kernel at `π(½ − |r|)` beyond it, so the answer near a zero keeps its relative accuracy.
    let private trigPi (x: float) : struct (float * float) =
        let ax = magnitude x
        let n = (ax + Elementary.twoTo52) - Elementary.twoTo52
        let r = ax - n
        let ar = magnitude r

        let struct (s, c) =
            if ar <= 0.25 then
                struct (sinKernel (pi * r), cosKernel (pi * r))
            else
                let t = pi * (0.5 - ar)
                let s = cosKernel t
                struct ((if r < 0.0 then -s else s), sinKernel t)

        let struct (s, c) =
            if Elementary.isOddInteger n then
                struct (-s, -c)
            else
                struct (s, c)

        struct ((if x < 0.0 then -s else s), c)

    // ---- Stirling's series ------------------------------------------------------------------------

    /// `B₂ₖ / (2k (2k − 1))` for `k = 8 .. 1`.
    let private stirlingTerms: float[] =
        [| -3617.0 / 122400.0
           1.0 / 156.0
           -691.0 / 360360.0
           1.0 / 1188.0
           -1.0 / 1680.0
           1.0 / 1260.0
           -1.0 / 360.0
           1.0 / 12.0 |]

    /// The tail of Stirling's series, `ln Γ(x) − ((x − ½) ln x − x + ½ ln 2π)`, for `x ≥ 10`: eight
    /// terms in `1 / x`.
    let private stirlingTail (x: float) : float =
        let z = 1.0 / x
        z * horner stirlingTerms (z * z)

    // ---- the ln Γ kernel --------------------------------------------------------------------------

    /// The Chebyshev fit of `ln Γ(2 + u) / (u (1 + u))` over `u` in `[−½, ½]`, degree seventeen,
    /// highest degree first (generated; see the file header).
    let private gammaLnKernelTerms: float[] =
        [| -2.489029529650396e-07
           5.236568534841362e-07
           -8.247688082905913e-07
           1.749054030806819e-06
           -3.854622136251583e-06
           8.23747667463625e-06
           -1.7660586231652296e-05
           3.816660593339992e-05
           -8.309522146915126e-05
           0.0001825528383907226
           -0.00040570739285611585
           0.0009153769125087831
           -0.002108130834069472
           0.0049986411649353725
           -0.012384192193370211
           0.03296500062115359
           -0.10031730167435392
           0.42278433509846713 |]

    /// `ln Γ(2 + u)` for `u` in `[−½, ½]`: the two zeros of `ln Γ` are the factors `u` and `1 + u`, so the
    /// answer keeps its relative accuracy at them.
    let private gammaLnKernel (u: float) : float =
        u * (1.0 + u) * horner gammaLnKernelTerms u

    /// `x − 1`, `x − 2`, … `x − k` multiplied left to right, for `x` above `2.5` and below the Stirling
    /// threshold: what `Γ(x)` is of `Γ(x − k)`.
    let private fallingProduct (x: float) (k: int) : float =
        let mutable p = 1.0

        for j in 1..k do
            p <- p * (x - float j)

        p

    /// `ln Γ(x)` for `x > 0`.
    let private gammaLnPositive (x: float) : float =
        if x >= 18014398509481984.0 then
            x * (Elementary.ln x - 1.0)
        elif x >= stirlingFrom then
            (x - 0.5) * (Elementary.ln x - 1.0) + (stirlingConstant + stirlingTail x)
        elif x > 2.5 then
            let k = int (x - 1.5)
            gammaLnKernel (x - float (k + 2)) + Elementary.ln (fallingProduct x k)
        elif x >= 1.5 then
            gammaLnKernel (x - 2.0)
        elif x >= 0.5 then
            gammaLnKernel (x - 1.0) - Elementary.ln x
        else
            gammaLnKernel x - Elementary.ln (x * (1.0 + x))

    /// `ln |Γ(x)|`.
    ///
    /// Above zero: Stirling's series from `10` (eight terms; `x (ln x − 1)` from `2⁵⁴`), and below it the
    /// kernel over `[1.5, 2.5]`, reached by the recurrence `Γ(x + 1) = x Γ(x)` with the reduced argument
    /// formed exactly. Below zero: the same kernel down to `−½`, and the reflection
    /// `ln (π / |x sin πx|) − ln Γ(−x)` beyond it.
    ///
    /// `+∞` at `+∞`, at `−∞`, at zero and at every negative integer (the poles, where `|Γ|` grows without
    /// bound from both sides).
    let gammaLn (x: float) : float =
        if Double.IsNaN x then
            Double.NaN
        elif not (finite x) then
            Double.PositiveInfinity
        elif x > 0.0 then
            gammaLnPositive x
        elif Elementary.isInteger x then
            Double.PositiveInfinity
        elif x > -0.5 then
            gammaLnKernel x - Elementary.ln (-x * (1.0 + x))
        else
            let struct (s, _) = trigPi x
            Elementary.ln (pi / magnitude (x * s)) - gammaLnPositive (-x)

    /// `Γ(x)` for `0 < x ≤ gammaOverflow`.
    let private gammaPositive (x: float) : float =
        if x >= stirlingFrom then
            let v = Elementary.pow x (0.5 * x - 0.25)
            sqrtTwoPi * (v * Elementary.exp -x) * v * Elementary.exp (stirlingTail x)
        elif x > 2.5 then
            let k = int (x - 1.5)
            Elementary.exp (gammaLnKernel (x - float (k + 2))) * fallingProduct x k
        elif x >= 1.5 then
            Elementary.exp (gammaLnKernel (x - 2.0))
        elif x >= 0.5 then
            Elementary.exp (gammaLnKernel (x - 1.0)) / x
        else
            Elementary.exp (gammaLnKernel x) / (x * (1.0 + x))

    /// `Γ(x)`.
    ///
    /// An integer from `1` to `23` is the exact product `(x − 1)!`. Otherwise, above zero: from `10`,
    /// Stirling's formula with the power taken in two halves by the pinned `pow`; below it, `exp` of the
    /// kernel times the recurrence's product. Below zero: the kernel down to `−½` and the reflection
    /// `−π / (x sin πx Γ(−x))` beyond it.
    ///
    /// `+∞` at `+∞` and above `171.62437695630272`, where the value passes the largest double. NaN at
    /// `−∞`, at zero and at every negative integer, where the sign of the pole differs by side.
    let gamma (x: float) : float =
        if Double.IsNaN x || x = Double.NegativeInfinity then
            Double.NaN
        elif x > gammaOverflow then
            Double.PositiveInfinity
        elif x > 0.0 then
            if x <= 23.0 && Elementary.isInteger x then
                let mutable p = 1.0

                for j in 2 .. int x - 1 do
                    p <- p * float j

                p
            else
                gammaPositive x
        elif Elementary.isInteger x then
            Double.NaN
        elif x > -0.5 then
            Elementary.exp (gammaLnKernel x) / (x * (1.0 + x))
        else
            let struct (s, _) = trigPi x

            let g =
                if -x > gammaOverflow then
                    Double.PositiveInfinity
                else
                    gammaPositive -x

            (-pi / (x * s)) / g

    // ---- the digamma kernel and the polygamma functions -------------------------------------------

    /// The positive root of the digamma function, `1.46163214496836234126…`, as a double and what the
    /// double leaves out.
    let private digammaRootHigh = 1.4616321449683622

    let private digammaRootLow = 9.549995429965697e-17

    /// The Chebyshev fit of `ψ(x) / (x − root)` over `x − 1.5` in `[−½, ½]`, degree twenty-three,
    /// highest degree first (generated; see the file header).
    let private digammaKernelTerms: float[] =
        [| -8.153978973309418e-05
           0.00012230983055406747
           -6.11554261750751e-05
           9.173383289174792e-05
           -0.0002178682145501451
           0.0003268068044141764
           -0.0004599625144612576
           0.0006899717801166841
           -0.001042261556504223
           0.0015635679757533885
           -0.002344650082351321
           0.0035180800751066694
           -0.00528001782089874
           0.007927024067961237
           -0.011908215172584002
           0.017907248195969127
           -0.02697579684427536
           0.04076083394462657
           -0.06192213327165706
           0.09498872445283
           -0.14840492305391761
           0.24054248424078736
           -0.42362742128146047
           0.9510558760318328 |]

    /// `B₂ₖ / (2k)` for `k = 7 .. 1`.
    let private digammaTerms: float[] =
        [| 1.0 / 12.0
           -691.0 / 32760.0
           1.0 / 132.0
           -1.0 / 240.0
           1.0 / 252.0
           -1.0 / 120.0
           1.0 / 12.0 |]

    /// `ψ(x)` for `x > 0`.
    let private digammaPositive (x: float) : float =
        if x >= 10.0 then
            let z = 1.0 / x
            let z2 = z * z
            (Elementary.ln x - 0.5 * z) - z2 * horner digammaTerms z2
        elif x >= 2.0 then
            let k = int x - 1
            let reduced = x - float k
            let mutable s = 0.0

            for j in 1..k do
                s <- s + 1.0 / (x - float j)

            ((reduced - digammaRootHigh) - digammaRootLow)
            * horner digammaKernelTerms (reduced - 1.5)
            + s
        elif x >= 1.0 then
            ((x - digammaRootHigh) - digammaRootLow) * horner digammaKernelTerms (x - 1.5)
        else
            ((x - (digammaRootHigh - 1.0)) - digammaRootLow)
            * horner digammaKernelTerms (x - 0.5)
            - 1.0 / x

    /// The digamma function `ψ(x)`, the derivative of `ln Γ`.
    ///
    /// Above zero: from `10`, the asymptotic series (seven Bernoulli terms); below it, the kernel over
    /// `[1, 2]`, whose factor `x − root` keeps the answer's relative accuracy at the root near `1.4616`,
    /// reached by the recurrence `ψ(x + 1) = ψ(x) + 1 / x`. Below zero: the reflection
    /// `ψ(1 − x) − π cos πx / sin πx`.
    ///
    /// `+∞` at `+∞`. NaN at `−∞`, at zero and at every negative integer (the poles).
    let digamma (x: float) : float =
        if Double.IsNaN x || x = Double.NegativeInfinity then
            Double.NaN
        elif x = Double.PositiveInfinity then
            Double.PositiveInfinity
        elif x > 0.0 then
            digammaPositive x
        elif Elementary.isInteger x then
            Double.NaN
        else
            let struct (s, c) = trigPi x
            digammaPositive (1.0 - x) - pi * c / s

    /// The Bernoulli numbers `B₂ₖ` for `k = 1 .. 10`.
    let private bernoulli: float[] =
        [| 1.0 / 6.0
           -1.0 / 30.0
           1.0 / 42.0
           -1.0 / 30.0
           5.0 / 66.0
           -691.0 / 2730.0
           7.0 / 6.0
           -3617.0 / 510.0
           43867.0 / 798.0
           -174611.0 / 330.0 |]

    /// The polygamma function `ψ⁽ⁿ⁾(x)`, the `n`-th derivative of the digamma function, for an integer
    /// `n` from `0` to `170`.
    ///
    /// `n = 0` is `digamma`. For `n ≥ 1` and `x > 0`: the recurrence
    /// `ψ⁽ⁿ⁾(x) = ψ⁽ⁿ⁾(x + 1) − (−1)ⁿ n! / xⁿ⁺¹` shifts `x` up by ones until it is at least `10 + n`, each
    /// power by `n` products left to right; there the asymptotic series
    /// `(−1)ⁿ⁺¹ (n − 1)! / xⁿ · (1 + n / (2x) + Σ B₂ₖ C(2k + n − 1, 2k) / x²ᵏ)` is read to ten Bernoulli
    /// terms.
    ///
    /// NaN for an `n` that is negative, not an integer or above `170` (where `n!` is not a finite
    /// double), and for `n ≥ 1` at any `x ≤ 0`. `0` for `n ≥ 1` at `+∞`.
    let polygamma (n: float) (x: float) : float =
        if
            Double.IsNaN n
            || Double.IsNaN x
            || n < 0.0
            || n > 170.0
            || not (Elementary.isInteger n)
        then
            Double.NaN
        elif n = 0.0 then
            digamma x
        elif not (x > 0.0) then
            Double.NaN
        elif x = Double.PositiveInfinity then
            0.0
        else
            let order = int n
            let threshold = 10.0 + n
            let mutable shifted = x
            let mutable shifts = 0
            let mutable sum = 0.0

            while shifted < threshold do
                let mutable p = shifted

                for _ in 1..order do
                    p <- p * shifted

                sum <- sum + 1.0 / p
                shifts <- shifts + 1
                shifted <- x + float shifts

            // (n − 1)!
            let mutable factorial = 1.0

            for j in 2 .. order - 1 do
                factorial <- factorial * float j

            let z = 1.0 / shifted
            let z2 = z * z
            let mutable zn = z

            for _ in 2..order do
                zn <- zn * z

            let mutable series = 0.0
            let mutable z2k = 1.0
            let mutable binomial = 1.0

            for k in 1..10 do
                let twoK = 2.0 * float k
                z2k <- z2k * z2
                binomial <- binomial * (((twoK + n - 2.0) * (twoK + n - 1.0)) / ((twoK - 1.0) * twoK))
                series <- series + bernoulli[k - 1] * binomial * z2k

            let r = factorial * zn * ((1.0 + 0.5 * n * z) + series) + (factorial * n) * sum
            if order % 2 = 1 then r else -r

    /// The trigamma function `ψ′(x)`: `polygamma 1 x`.
    let trigamma (x: float) : float = polygamma 1.0 x

    // ---- the beta function ------------------------------------------------------------------------

    let private positiveFinite (a: float) : bool = a > 0.0 && finite a

    /// `ln B(a, b)` for `0 < a ≤ b`, both finite.
    let private betaLnOrdered (a: float) (b: float) : float =
        if a >= stirlingFrom then
            let r = a / (a + b)

            (-0.5 * Elementary.ln b + halfLnTwoPi)
            + (stirlingTail a + (stirlingTail b - stirlingTail (a + b)))
            + (a - 0.5) * Elementary.ln r
            + b * ln1p -r
        elif b >= stirlingFrom then
            let r = a / (a + b)

            gammaLnPositive a
            + ((stirlingTail b - stirlingTail (a + b))
               + (a - a * Elementary.ln (a + b))
               + (b - 0.5) * ln1p -r)
        else
            let struct (s, e) = Elementary.twoSum a b
            let whole = gammaLnPositive s

            let whole = if e <> 0.0 then whole + e * digammaPositive s else whole

            (gammaLnPositive a + gammaLnPositive b) - whole

    /// `ln B(a, b)`, the logarithm of the beta function, for `a > 0` and `b > 0`.
    ///
    /// The arguments are ordered first, so the function is symmetric bit for bit. Both at least `10`:
    /// Stirling's series for all three gamma functions, combined so that no large terms cancel. One at
    /// least `10`: the same for the two large ones, and `ln Γ` of the small one. Both below `10`:
    /// `ln Γ(a) + ln Γ(b) − ln Γ(a + b)`, with what the sum `a + b` rounded away restored through the
    /// digamma function.
    ///
    /// NaN where either argument is not above zero or is not finite.
    let betaLn (a: float) (b: float) : float =
        if not (positiveFinite a && positiveFinite b) then
            Double.NaN
        elif a <= b then
            betaLnOrdered a b
        else
            betaLnOrdered b a

    /// `B(a, b)`, the beta function, for `a > 0` and `b > 0`.
    ///
    /// The arguments are ordered first. Where `a + b ≤ 170` it is `Γ(a) / Γ(a + b) · Γ(b)` (what the sum
    /// rounded away restored through the digamma function); beyond, `exp (betaLn a b)`.
    ///
    /// NaN where either argument is not above zero or is not finite.
    let beta (a: float) (b: float) : float =
        if not (positiveFinite a && positiveFinite b) then
            Double.NaN
        else
            let struct (a, b) = if a <= b then struct (a, b) else struct (b, a)
            let struct (s, e) = Elementary.twoSum a b

            if s <= 170.0 then
                let whole = gamma s

                let whole =
                    if e <> 0.0 then
                        whole * (1.0 + e * digammaPositive s)
                    else
                        whole

                gamma a / whole * gamma b
            else
                Elementary.exp (betaLnOrdered a b)

    // ---- the regularized incomplete gamma functions -----------------------------------------------

    /// `xᵃ e⁻ˣ / Γ(a)` for `a > 0` and `x > 0`, both finite. Below `10` the three factors are computed
    /// apart, the power by the pinned `pow`; from `10` it is
    /// `√(a / 2π) exp (−a (μ − ln (1 + μ)) − tail a)` with `μ = (x − a) / a`, which loses nothing where
    /// `x` is near `a`.
    let private gammaPrefix (a: float) (x: float) : float =
        if a < stirlingFrom then
            let e = Elementary.exp -x

            if e = 0.0 then
                0.0
            else
                Elementary.pow x a * e / gammaPositive a
        else
            let m = (x - a) / a
            sqrt (a / twoPi) * Elementary.exp (-a * mMinusLn1p m (x / a) - stirlingTail a)

    /// `Σ_{n ≥ 0} xⁿ / (a (a + 1) … (a + n))`: `P(a, x)` over the prefix.
    let private lowerSeries (a: float) (x: float) : float =
        let mutable term = 1.0 / a
        let mutable sum = term
        let mutable ap = a
        let mutable n = 0
        let mutable answer = Double.NaN
        let mutable running = true

        while running do
            n <- n + 1
            ap <- ap + 1.0
            term <- term * (x / ap)
            let previous = sum
            sum <- sum + term

            if settled previous sum then
                answer <- sum
                running <- false
            elif n >= seriesRule.Cap then
                running <- false

        answer

    /// Legendre's continued fraction for `Q(a, x)` over the prefix, by the modified Lentz method.
    let private upperFraction (a: float) (x: float) : float =
        let mutable b = x + 1.0 - a
        let mutable c = 1.0 / tiny
        let mutable d = 1.0 / b
        let mutable h = d
        let mutable i = 0
        let mutable answer = Double.NaN
        let mutable running = true

        while running do
            i <- i + 1
            let an = -(float i) * (float i - a)
            b <- b + 2.0
            d <- an * d + b

            if magnitude d < tiny then
                d <- tiny

            c <- b + an / c

            if magnitude c < tiny then
                c <- tiny

            d <- 1.0 / d
            let previous = h
            h <- h * (d * c)

            if settled previous h then
                answer <- h
                running <- false
            elif i >= seriesRule.Cap then
                running <- false

        answer

    /// `ln Γ(1 + a)` for `0 < a < 1`, accurate relative to itself as `a` falls to zero.
    let private gammaLnOnePlus (a: float) : float =
        if a <= 0.5 then
            gammaLnKernel a - ln1p a
        else
            gammaLnKernel (a - 1.0)

    /// `Q(a, x)` for `0 < a < 1` and `0 < x < 1`, directly:
    /// `−expm1 L − exp L · a · Σ_{n ≥ 1} (−x)ⁿ / ((a + n) n!)` with `L = a ln x − ln Γ(1 + a)`.
    let private upperSmall (a: float) (x: float) : float =
        let l = a * Elementary.ln x - gammaLnOnePlus a
        let mutable term = 1.0
        let mutable sum = 0.0
        let mutable n = 0
        let mutable answer = Double.NaN
        let mutable running = true

        while running do
            n <- n + 1
            term <- term * (-x / float n)
            let previous = sum
            sum <- sum + term / (a + float n)

            if settled previous sum then
                answer <- sum
                running <- false
            elif n >= seriesRule.Cap then
                running <- false

        -(expm1 l) - Elementary.exp l * a * answer

    /// `(P(a, x), Q(a, x))` for finite `a > 0` and `x > 0`.
    let private regularizedGammaBoth (a: float) (x: float) : struct (float * float) =
        let boundary = if a < 1.0 then 1.0 else a + 1.0

        if x >= boundary then
            let q = gammaPrefix a x * upperFraction a x
            struct (1.0 - q, q)
        else
            let p = gammaPrefix a x * lowerSeries a x

            if a < 1.0 then
                struct (p, upperSmall a x)
            else
                struct (p, 1.0 - p)

    /// What both regularized gamma functions answer before any series: NaN for a NaN, for an `a` not
    /// above zero or not finite, and for a negative `x`.
    let private regularizedGammaRefused (a: float) (x: float) : bool =
        Double.IsNaN a || Double.IsNaN x || not (positiveFinite a) || x < 0.0

    /// `P(a, x)`, the regularized lower incomplete gamma function `γ(a, x) / Γ(a)`, for `a > 0` and
    /// `x ≥ 0`.
    ///
    /// Below the boundary (`a + 1`, or `1` where `a < 1`) it is the series `Σ xⁿ / (a (a + 1) … (a + n))`
    /// times the prefix `xᵃ e⁻ˣ / Γ(a)`; from the boundary it is `1 − Q(a, x)`.
    ///
    /// `0` at `x = 0` and `1` at `x = +∞`. NaN for an `a` not above zero or not finite, for a negative
    /// `x`, and where the series has not settled in 65536 terms (an `a` beyond about `5 × 10⁷` with `x`
    /// near it).
    let regularizedGammaP (a: float) (x: float) : float =
        if regularizedGammaRefused a x then
            Double.NaN
        elif x = 0.0 then
            0.0
        elif x = Double.PositiveInfinity then
            1.0
        else
            let struct (p, _) = regularizedGammaBoth a x
            p

    /// `Q(a, x) = 1 − P(a, x)`, the regularized upper incomplete gamma function, computed directly
    /// wherever it is the smaller of the two.
    ///
    /// From the boundary (`a + 1`, or `1` where `a < 1`) it is Legendre's continued fraction by the
    /// modified Lentz method, times the prefix. Below it: for `a < 1`, its own series (see `upperSmall`),
    /// so an `a` near zero keeps its accuracy; for `a ≥ 1`, `1 − P(a, x)`, where `P` is at most about
    /// `0.87`.
    ///
    /// `1` at `x = 0` and `0` at `x = +∞`. NaN as `regularizedGammaP`.
    let regularizedGammaQ (a: float) (x: float) : float =
        if regularizedGammaRefused a x then
            Double.NaN
        elif x = 0.0 then
            1.0
        elif x = Double.PositiveInfinity then
            0.0
        else
            let struct (_, q) = regularizedGammaBoth a x
            q

    // ---- the regularized incomplete beta function and its inverse ---------------------------------

    /// `xᵃ yᵇ / B(a, b)` for `y = 1 − x`, `0 < x < 1`. Both parameters at least `10`: the form
    /// `√(ab / (2π (a + b))) exp (−a φ(n / a) − b φ(−n / b) − tails)` with `φ(m) = m − ln (1 + m)` and
    /// `n = x (a + b) − a` formed in double-double arithmetic, which loses nothing where `x` is near the
    /// mean. Otherwise `exp (a ln x + b ln y − betaLn a b)`, each logarithm taken of the smaller of `x`
    /// and `y` directly and of the larger through `ln1p`.
    let private betaPrefix (a: float) (b: float) (x: float) (y: float) : float =
        if a >= stirlingFrom && b >= stirlingFrom then
            let struct (s, e) = Elementary.twoSum a b
            let struct (ph, pl) = Elementary.twoProd x s
            let n = ((ph - a) + pl) + x * e
            let ma = n / a
            let mb = -n / b

            let exponent = -a * mMinusLn1p ma (x * s / a) - b * mMinusLn1p mb (y * s / b)

            sqrt (a * b / (twoPi * s))
            * Elementary.exp (exponent - (stirlingTail a + (stirlingTail b - stirlingTail s)))
        else
            let lx = if x <= 0.5 then Elementary.ln x else ln1p -y
            let ly = if y <= 0.5 then Elementary.ln y else ln1p -x
            Elementary.exp (a * lx + b * ly - betaLn a b)

    /// The continued fraction of the incomplete beta function (Abramowitz and Stegun 26.5.8) by the
    /// modified Lentz method, two partial quotients an iteration.
    let private betaFraction (a: float) (b: float) (x: float) : float =
        let qab = a + b
        let qap = a + 1.0
        let qam = a - 1.0
        let mutable c = 1.0
        let mutable d = 1.0 - qab * x / qap

        if magnitude d < tiny then
            d <- tiny

        d <- 1.0 / d
        let mutable h = d
        let mutable m = 0
        let mutable answer = Double.NaN
        let mutable running = true

        while running do
            m <- m + 1
            let fm = float m
            let m2 = 2.0 * fm
            let even = fm * (b - fm) * x / ((qam + m2) * (a + m2))
            d <- 1.0 + even * d

            if magnitude d < tiny then
                d <- tiny

            c <- 1.0 + even / c

            if magnitude c < tiny then
                c <- tiny

            d <- 1.0 / d
            h <- h * (d * c)
            let odd = -(a + fm) * (qab + fm) * x / ((a + m2) * (qap + m2))
            d <- 1.0 + odd * d

            if magnitude d < tiny then
                d <- tiny

            c <- 1.0 + odd / c

            if magnitude c < tiny then
                c <- tiny

            d <- 1.0 / d
            let previous = h
            h <- h * (d * c)

            if settled previous h then
                answer <- h
                running <- false
            elif m >= seriesRule.Cap then
                running <- false

        answer

    /// `Σ_{n ≥ 0} (a + b)ₙ / (a + 1)ₙ xⁿ`, Gauss's series `₂F₁(a + b, 1; a + 1; x)`: `I_x(a, b)` over
    /// `prefix / a`, every term positive. Read only where `b < 1` and `x ≤ 0.99`, where each term is
    /// less than `x` times the one before.
    let private betaSeries (a: float) (b: float) (x: float) : float =
        let mutable term = 1.0
        let mutable sum = 1.0
        let mutable n = 0
        let mutable answer = Double.NaN
        let mutable running = true

        while running do
            n <- n + 1
            let fn = float n
            term <- term * (x * ((a + b + fn - 1.0) / (a + fn)))
            let previous = sum
            sum <- sum + term

            if settled previous sum then
                answer <- sum
                running <- false
            elif n >= seriesRule.Cap then
                running <- false

        answer

    /// Up to this argument the series above stands in for a complement (some 3600 terms at the most).
    let private betaSeriesTo = 0.99

    /// `(I_x(a, b), 1 − I_x(a, b), prefix)` for `0 < x < 1`. The one on the fraction's side of the
    /// switch `x = (a + 1) / (a + b + 2)` is the continued fraction, directly. The other is its
    /// complement, except where that complement is below the fraction's own argument and the other
    /// side's own parameter is below one (the density is unbounded at that end, and the small value
    /// would be a difference of two near ones): there it is Gauss's series, up to an argument of
    /// `0.99`. The series loses at most about `3 / (1 − x)` units in the last place of its sum to its
    /// length, so under that condition its error stays below three units in the last place of one.
    ///
    /// The upper value is a function of `y = 1 − x`, which a double holds exactly only from `x = ½`.
    /// What the subtraction rounded away, `yLow = (1 − y) − x`, is restored to first order through the
    /// density `prefix / (x y)`: without it a large `b` multiplies that rounding into the answer.
    let private regularizedBetaBoth (a: float) (b: float) (x: float) : struct (float * float * float) =
        let y = 1.0 - x
        let yLow = (1.0 - y) - x
        let prefix = betaPrefix a b x y
        let restored = yLow * (prefix / (x * y))

        if x > (a + 1.0) / (a + b + 2.0) then
            let upper = prefix * betaFraction b a y / b + restored

            if b < 1.0 && x <= betaSeriesTo && 1.0 - upper < y then
                struct (prefix * betaSeries a b x / a, upper, prefix)
            else
                struct (1.0 - upper, upper, prefix)
        else
            let lower = prefix * betaFraction a b x / a

            if a < 1.0 && y <= betaSeriesTo && 1.0 - lower < x then
                struct (lower, prefix * betaSeries b a y / b + restored, prefix)
            else
                struct (lower, 1.0 - lower, prefix)

    /// What the incomplete beta function and its inverse refuse: a NaN, a parameter not above zero or
    /// not finite, or a third argument outside `[0, 1]`.
    let private regularizedBetaRefused (a: float) (b: float) (x: float) : bool =
        Double.IsNaN x
        || not (positiveFinite a && positiveFinite b)
        || x < 0.0
        || x > 1.0

    /// `I_x(a, b)`, the regularized incomplete beta function, for `a > 0`, `b > 0` and `x` in `[0, 1]`.
    ///
    /// The continued fraction by the modified Lentz method times the prefix `xᵃ (1 − x)ᵇ / (a B(a, b))`,
    /// read at `x` where `x ≤ (a + 1) / (a + b + 2)` and, beyond that switch, as
    /// `1 − I_{1 − x}(b, a)`. Where `b < 1` and that complement is below `1 − x`, the function is
    /// Gauss's series instead, for `x` up to `0.99`; beyond `0.99` it stays the complement, accurate to
    /// a unit in the last place of one and not of itself.
    ///
    /// `0` at `x = 0` and `1` at `x = 1`. NaN for a parameter not above zero or not finite, for an `x`
    /// outside `[0, 1]`, and where the fraction has not settled in 65536 iterations.
    let regularizedBeta (a: float) (b: float) (x: float) : float =
        if regularizedBetaRefused a b x then
            Double.NaN
        elif x = 0.0 then
            0.0
        elif x = 1.0 then
            1.0
        else
            let struct (lower, _, _) = regularizedBetaBoth a b x
            lower

    /// The inverse's start for the tails and for a parameter below one: the leading term of the
    /// incomplete beta function at whichever end `p` is nearer, solved for `x`.
    let private inverseStartTail (a: float) (b: float) (p: float) : float =
        let t = Elementary.exp (a * Elementary.ln (a / (a + b))) / a
        let u = Elementary.exp (b * Elementary.ln (b / (a + b))) / b
        let w = t + u

        if p < t / w then
            Elementary.pow (a * w * p) (1.0 / a)
        else
            1.0 - Elementary.pow (b * w * (1.0 - p)) (1.0 / b)

    /// The inverse's start: for both parameters at least one, Abramowitz and Stegun 26.5.22 over the
    /// pinned normal quantile; otherwise, and wherever that lands outside `(0, 1)`, the tail start.
    let private inverseStart (a: float) (b: float) (p: float) : float =
        let normal =
            if a >= 1.0 && b >= 1.0 then
                let z = -(Elementary.normalQuantile p)
                let al = (z * z - 3.0) / 6.0
                let ra = 1.0 / (2.0 * a - 1.0)
                let rb = 1.0 / (2.0 * b - 1.0)
                let h = 2.0 / (ra + rb)

                let w = z * sqrt (al + h) / h - (rb - ra) * (al + 5.0 / 6.0 - 2.0 / (3.0 * h))

                a / (a + b * Elementary.exp (2.0 * w))
            else
                Double.NaN

        if normal > 0.0 && normal < 1.0 then
            normal
        else
            inverseStartTail a b p

    /// The inverse's rule: Newton stops when a step moved the point by less than `2⁻⁴⁰` of itself, or at
    /// one hundred iterations, where the function answers NaN.
    let private inverseRule: StoppingRule =
        StoppingRule.create 100 (StoppingTest.RelativeChange(9.094947017729282e-13, minNormal))

    /// `(f x, f′ x)` for the root of `I_x(a, b) = p`, with `q = 1 − p`: `I_x − p`, read as
    /// `q − (1 − I_x)` where `p > ½` so an upper tail keeps its precision, and the density
    /// `xᵃ⁻¹ (1 − x)ᵇ⁻¹ / B(a, b)`. At `x = 0` the value is `−p` and at `x = 1` it is `q`.
    let private inverseResidual (a: float) (b: float) (p: float) (q: float) (x: float) : float * float =
        if x = 0.0 then
            -p, 0.0
        elif x = 1.0 then
            q, 0.0
        else
            let struct (lower, upper, prefix) = regularizedBetaBoth a b x
            (if p <= 0.5 then lower - p else q - upper), prefix / (x * (1.0 - x))

    /// The root of `I_x(a, b) = p` known to lie in the lower half: the start (replaced by `¼` where it
    /// does not land there), then `Root.bracketedNewton` over `[0, 1]`. The bracket is the whole interval
    /// and not the half: a root within rounding of `½` would otherwise send a Newton step just past the
    /// bracket's end, and the bisection that replaces it stops the rule on a step that is half a bracket
    /// and not a convergence.
    let private inverseLowerHalf (a: float) (b: float) (p: float) (q: float) : float =
        let x0 = inverseStart a b p

        if Double.IsNaN x0 then
            Double.NaN
        elif not (x0 > 0.0) then
            0.0
        else
            let solved: Result<Stopped<float>, SolverError<float, unit>> =
                Root.bracketedNewton inverseRule 1.0 (inverseResidual a b p q) 0.0 1.0 (if x0 < 0.5 then x0 else 0.25)

            match solved with
            | Ok at -> at.State
            | Error _ -> Double.NaN

    /// The `x` in `[0, 1]` with `I_x(a, b) = p`, for `a > 0`, `b > 0` and `p` in `[0, 1]`.
    ///
    /// The residual `I_x(a, b) − p` is read at `x = ½` first (as `(1 − p) − (1 − I_x)` where `p > ½`, so
    /// an upper tail keeps its precision). Not negative there, the root is in the lower half and is
    /// solved for directly; negative, the mirrored problem `I_y(b, a) = 1 − p` is solved for
    /// `y = 1 − x`, so the solver always works on the small one of `x` and `1 − x` and its relative
    /// stopping test measures that. Each solve is a pinned start (see `inverseStart`), then
    /// `Root.bracketedNewton` over `[0, 1]` with the density `xᵃ⁻¹ (1 − x)ᵇ⁻¹ / B(a, b)` for a
    /// derivative, a shrink fraction of one, and the rule: stop when a step moved the point by less
    /// than `2⁻⁴⁰` of itself, at most one hundred iterations.
    ///
    /// `0` at `p = 0` and `1` at `p = 1`; `0` or `1` where the start itself underflows to that end; `½`
    /// where neither half's residual has the sign of a root inside it. NaN for a parameter not above
    /// zero or not finite, for a `p` outside `[0, 1]`, and where the solver refuses (the cap, or a
    /// fraction that did not settle).
    let inverseRegularizedBeta (a: float) (b: float) (p: float) : float =
        if regularizedBetaRefused a b p then
            Double.NaN
        elif p = 0.0 then
            0.0
        elif p = 1.0 then
            1.0
        else
            let q = 1.0 - p
            let middle, _ = inverseResidual a b p q 0.5

            if Double.IsNaN middle then
                Double.NaN
            elif middle = 0.0 then
                0.5
            elif middle > 0.0 then
                inverseLowerHalf a b p q
            else
                let mirrored, _ = inverseResidual b a q p 0.5

                if Double.IsNaN mirrored then Double.NaN
                elif mirrored <= 0.0 then 0.5
                else 1.0 - inverseLowerHalf b a q p
