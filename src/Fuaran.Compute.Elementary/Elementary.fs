namespace Fuaran.Compute.Elementary

open System

// ============================================================================
//  Phase 593 — the pinned elementary functions: `exp`, `ln`, `log10` and the
//  float-exponent `pow`, each one argument reduction and one polynomial written
//  with `+ − × ÷` and comparisons alone, in one order, with no fused
//  multiply-add and no host mathematics call on any path a value reaches
//  (DECISIONS.md D52). Transcribed exactly into another host, the routines
//  answer the same bits there, which is what D43's host-rounded tier could not
//  promise and what a log-link GLM needs.
//
//  The arithmetic beyond the four operations is the IEEE-754 binary64 format
//  itself: a power of two is read from a table built by exact doubling and
//  halving, and an argument's exponent is found by comparing against that
//  table — nothing reads a bit pattern. D43's special-value table is answered
//  FIRST, unchanged, and is published as data so the evaluator and every twin
//  read the one table.
//
//  The routines: `exp`, `ln` and `log10` follow fdlibm's `e_exp.c`, `e_log.c`
//  and `e_log10.c` (Sun Microsystems, 1993; the algorithms every libm since
//  descends from) with their published coefficients, each within one unit in
//  the last place of the true value. `pow` is `exp(y · ln x)` with `ln x` and
//  the product carried in double-double arithmetic (Dekker's error-free
//  transformations), so the reduced exponent reaches the same core `exp`
//  with the error a plain product would have lost; the accuracy measured for
//  each is in D52 and benchmarks/results/.
//
//  Phase 725 — `sin`, `cos`, `sinCos`, `atan` and `atan2` (DECISIONS.md D84),
//  each behind its own table. The argument of `sin` and `cos` is reduced
//  exactly at every finite double: by three pieces of `π/2` up to `2²⁰`, and
//  beyond by the digits of `2/π`, multiplied in integers a double holds
//  exactly. The two kernels are the Taylor polynomials `Special`'s reflections
//  read; `atan` is fdlibm's `s_atan.c` with its published coefficients, and
//  `atan2` carries its quotient's remainder. Every constant is printed by
//  tests/Fuaran.Core.Compute.Tests/SpecialReference/trig_reference.py, which
//  also computes every reference the trigonometric vectors carry; the accuracy
//  measured for each is in D84 and benchmarks/results/.
//
//  FSharp.Core only; Fable-clean.
// ============================================================================

/// What D43's special-value table answers for an input before any pinned routine is reached
/// (DECISIONS.md D43 item 3, carried unchanged by D52).
[<RequireQualifiedAccess>]
type SpecialValue =
    /// A NaN operand: the answer is D16's one NaN.
    | NaN
    /// The table's own answer — `1` for `pow(x, 0)`, `0` for `ln 1`, the overflow limit of `exp`.
    | Value of float
    /// Outside the reals — `ln 0`, `ln(−1)`, `pow(0, −1)`, a negative base under a non-integer
    /// exponent: the evaluator answers `Null`; the functions here answer NaN.
    | Undefined
    /// The pinned routine answers.
    | Compute

/// The pinned elementary functions (DECISIONS.md D52).
[<RequireQualifiedAccess>]
module Elementary =

    // ---- the powers of two, and the exponent of a float -------------------------------------------

    /// `2^k` for `k` in `−1074 .. 1023`, at index `k + 1074`: built by exact doubling and halving, so
    /// every host holds the same table, the subnormal powers included.
    let private pow2: float[] =
        let t = Array.zeroCreate 2098
        t[1074] <- 1.0

        for k in 1..1023 do
            t[1074 + k] <- t[1073 + k] * 2.0

        for k in 1..1074 do
            t[1074 - k] <- t[1075 - k] * 0.5

        t

    /// `2^k`, `k` in `−1074 .. 1023`.
    let private twoTo (k: int) : float = pow2[k + 1074]

    let private twoTo54 = 18014398509481984.0
    let private minNormal = 2.2250738585072014e-308

    /// A positive finite `x` as `m · 2^e` with `m` in `[1, 2)`: the exponent by binary search over
    /// the table (the largest `e` with `2^e ≤ x`), the mantissa by one exact product. A subnormal is
    /// first scaled by `2^54`, exactly.
    let private frexp (x: float) : struct (int * float) =
        let x, shift = if x < minNormal then x * twoTo54, 54 else x, 0

        let mutable lo = -1022
        let mutable hi = 1023

        while lo < hi do
            let mid = (lo + hi + 1) / 2

            if twoTo mid <= x then lo <- mid else hi <- mid - 1

        struct (lo - shift, x * twoTo (-lo))

    /// `y · 2^k` for `y` near one and `k` in `−1075 .. 1024`: one exact product into the normal
    /// range, or two products where the answer is subnormal or beyond `2^1023`, the second of which
    /// rounds once (fdlibm's `scalbn` route).
    let private scalb (y: float) (k: int) : float =
        if k > 1023 then
            (y * twoTo 1023) * twoTo (min (k - 1023) 1023)
        elif k < -1021 then
            (y * twoTo (k + 1000)) * twoTo (-1000)
        else
            y * twoTo k

    // ---- integer-valued floats, with + and − only -------------------------------------------------

    let internal twoTo52 = 4503599627370496.0
    let private twoTo53 = 9007199254740992.0

    /// Whether a finite `y` is an integer: at or above `2^52` every float is; below, adding and
    /// subtracting `2^52` rounds `|y|` to the nearest integer, and `y` is one exactly when that is `|y|`.
    let internal isInteger (y: float) : bool =
        let ay = if y < 0.0 then -y else y
        ay >= twoTo52 || (ay + twoTo52) - twoTo52 = ay

    /// Whether a finite integer-valued `y` is odd: at or above `2^53` every integer is even; below,
    /// `|y| / 2` is exact and odd means it is not an integer.
    let internal isOddInteger (y: float) : bool =
        let ay = if y < 0.0 then -y else y
        ay < twoTo53 && not (isInteger (ay * 0.5))

    // ---- D43's special-value table, as data -------------------------------------------------------

    /// `exp`'s table (DECISIONS.md D43 item 3): a NaN propagates; above the natural log of the largest
    /// double the answer is `Inf`, below the natural log of half the smallest subnormal it is `0`; in
    /// between the routine answers.
    let expTable (x: float) : SpecialValue =
        if Double.IsNaN x then
            SpecialValue.NaN
        elif x > 709.782712893384 then
            SpecialValue.Value Double.PositiveInfinity
        elif x < -745.1332191019412 then
            SpecialValue.Value 0.0
        else
            SpecialValue.Compute

    /// `ln`'s and `log10`'s table (D43 item 3): a NaN propagates, zero and below is outside the reals,
    /// one is exactly `0`, `Inf` is `Inf`; in between the routine answers.
    let lnTable (x: float) : SpecialValue =
        if Double.IsNaN x then
            SpecialValue.NaN
        elif x <= 0.0 then
            SpecialValue.Undefined
        elif x = 1.0 then
            SpecialValue.Value 0.0
        elif Double.IsPositiveInfinity x then
            SpecialValue.Value Double.PositiveInfinity
        else
            SpecialValue.Compute

    /// A float `pow`'s table (D43 item 3): a NaN operand propagates (`pow(1, NaN)` and `pow(NaN, 0)`
    /// included — the hosts disagree on both, and D16's one NaN is the answer); a zero exponent and a
    /// base of one answer `1`, and so does a base of minus one under an infinite exponent; a zero
    /// base under a negative exponent and a finite negative base under a finite non-integer exponent
    /// are outside the reals; everything else the routine answers.
    let powTable (x: float) (y: float) : SpecialValue =
        if Double.IsNaN x || Double.IsNaN y then
            SpecialValue.NaN
        elif y = 0.0 || x = 1.0 then
            SpecialValue.Value 1.0
        elif x = 0.0 && y < 0.0 then
            SpecialValue.Undefined
        elif x = -1.0 && Double.IsInfinity y then
            SpecialValue.Value 1.0
        elif
            x < 0.0
            && not (Double.IsInfinity x)
            && not (Double.IsInfinity y)
            && not (isInteger y)
        then
            SpecialValue.Undefined
        else
            SpecialValue.Compute

    // ---- exp (fdlibm e_exp.c) ---------------------------------------------------------------------

    /// `ln 2` split so that `k · ln2Hi` is exact for every `k` this module forms (the high part has
    /// twenty-one trailing zero bits), with the next fifty-three bits in `ln2Lo`.
    let private ln2Hi = 0.6931471803691238
    let private ln2Lo = 1.9082149292705877e-10
    let private invLn2 = 1.4426950408889634
    let private halfLn2 = 0.34657359027997264
    let private twoToMinus28 = 3.725290298461914e-09
    let private p1 = 0.16666666666666602
    let private p2 = -0.0027777777777015593
    let private p3 = 6.613756321437934e-05
    let private p4 = -1.6533902205465252e-06
    let private p5 = 4.1381367970572385e-08

    /// `exp(hi − lo) · 2^k` for a reduced argument `hi − lo` within `±(ln 2)/2`: the polynomial for
    /// `x − expm1(x)` over `x = hi − lo` and the compensated assembly, in fdlibm's order.
    let private expCore (hi: float) (lo: float) (k: int) : float =
        let x = hi - lo
        let t = x * x
        let c = x - t * (p1 + t * (p2 + t * (p3 + t * (p4 + t * p5))))
        let y = 1.0 - ((lo - (x * c) / (2.0 - c)) - hi)
        if k = 0 then y else scalb y k

    /// The nearest integer to `x / ln 2`, rounding half away from zero (fdlibm's `k`).
    let private nearestK (x: float) : int =
        if x > 0.0 then
            int (invLn2 * x + 0.5)
        else
            int (invLn2 * x - 0.5)

    /// `exp x` for a finite `x` the table did not answer.
    let private expCompute (x: float) : float =
        let ax = if x < 0.0 then -x else x

        if ax > halfLn2 then
            let k = nearestK x
            let t = float k
            expCore (x - t * ln2Hi) (t * ln2Lo) k
        elif ax < twoToMinus28 then
            1.0 + x
        else
            expCore x 0.0 0

    /// The pinned `exp`: D43's table, then one argument reduction and fdlibm's polynomial, within one
    /// unit in the last place of the true value. `exp NaN` is NaN.
    let exp (x: float) : float =
        match expTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute -> expCompute x

    // ---- ln (fdlibm e_log.c) and log10 (e_log10.c) ------------------------------------------------

    let private sqrt2 = 1.4142135623730951
    let private twoToMinus20 = 9.5367431640625e-07
    let private lg1 = 0.6666666666666735
    let private lg2 = 0.3999999999940942
    let private lg3 = 0.2857142874366239
    let private lg4 = 0.22222198432149784
    let private lg5 = 0.1818357216161805
    let private lg6 = 0.15313837699209373
    let private lg7 = 0.14798198605116586

    /// A positive finite `x` as `k` and `f` with `x = 2^k · (1 + f)` and `1 + f` in `[√2/2, √2)`: the
    /// fold that keeps `|f|` below `0.415`, so the series below converges in seven terms.
    let private reduce (x: float) : struct (int * float) =
        let struct (e, m) = frexp x

        if m > sqrt2 then
            struct (e + 1, m * 0.5 - 1.0)
        else
            struct (e, m - 1.0)

    /// `ln x` for a positive finite `x` the table did not answer: `k · ln 2 + ln(1 + f)` with
    /// `ln(1 + f) = 2s + s · R(z)`, `s = f / (2 + f)`, `z = s²`, assembled in fdlibm's compensated
    /// order (the `hfsq` form throughout; fdlibm's narrow band without it is not taken here).
    let private lnCompute (x: float) : float =
        let struct (k, f) = reduce x
        let dk = float k
        let af = if f < 0.0 then -f else f

        if af < twoToMinus20 then
            if f = 0.0 then
                if k = 0 then 0.0 else dk * ln2Hi + dk * ln2Lo
            else
                let r = f * f * (0.5 - 0.3333333333333333 * f)
                if k = 0 then f - r else dk * ln2Hi - ((r - dk * ln2Lo) - f)
        else
            let s = f / (2.0 + f)
            let z = s * s
            let w = z * z
            let t1 = w * (lg2 + w * (lg4 + w * lg6))
            let t2 = z * (lg1 + w * (lg3 + w * (lg5 + w * lg7)))
            let r = t2 + t1
            let hfsq = 0.5 * f * f

            if k = 0 then
                f - (hfsq - s * (hfsq + r))
            else
                dk * ln2Hi - ((hfsq - (s * (hfsq + r) + dk * ln2Lo)) - f)

    /// The pinned natural logarithm: D43's table, then fdlibm's reduction and series, within one unit
    /// in the last place of the true value. Zero and below answer NaN (`lnTable` says `Undefined`,
    /// which is what the evaluator reads before calling).
    let ln (x: float) : float =
        match lnTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute -> lnCompute x

    let private ivLn10 = 0.4342944819032518
    let private log10Of2Hi = 0.30102999566361177
    let private log10Of2Lo = 3.694239077158931e-13

    /// The pinned common logarithm, fdlibm's `e_log10.c`: `x = 2^n · m` with `m` in `[1, 2)`, or in
    /// `[1/2, 1)` with `n + 1` when `n` is negative, so the two terms carry one sign; then
    /// `(n · log10(2)_lo + ln(m) / ln 10) + n · log10(2)_hi`. The same table as `ln`.
    let log10 (x: float) : float =
        match lnTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            let struct (e, m) = frexp x
            let i = if e < 0 then 1 else 0
            let n = float (e + i)
            let m = if i = 1 then m * 0.5 else m
            let z = n * log10Of2Lo + ivLn10 * lnCompute m
            z + n * log10Of2Hi

    // ---- double-double arithmetic for pow (Dekker, 1971) ------------------------------------------

    /// `2^27 + 1`: Veltkamp's splitting constant for binary64.
    let private splitter = 134217729.0

    /// `a + b` as the rounded sum and its exact error.
    let internal twoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        let bb = s - a
        struct (s, (a - (s - bb)) + (b - bb))

    /// `a + b` as the rounded sum and its exact error, for `|a| ≥ |b|`.
    let private quickTwoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        struct (s, b - (s - a))

    /// `a · b` as the rounded product and its exact error, each factor split into two halves of
    /// twenty-six bits (no overflow for `|a|, |b|` below `2^996`, which every caller keeps).
    let internal twoProd (a: float) (b: float) : struct (float * float) =
        let p = a * b
        let ca = splitter * a
        let ah = ca - (ca - a)
        let al = a - ah
        let cb = splitter * b
        let bh = cb - (cb - b)
        let bl = b - bh
        struct (p, ((ah * bh - p) + ah * bl + al * bh) + al * bl)

    /// `(ah + al) + (bh + bl)`, normalised.
    let private ddAdd (ah: float) (al: float) (bh: float) (bl: float) : struct (float * float) =
        let struct (s, e) = twoSum ah bh
        quickTwoSum s (e + al + bl)

    /// `(ah + al) · (bh + bl)`, normalised.
    let private ddMul (ah: float) (al: float) (bh: float) (bl: float) : struct (float * float) =
        let struct (p, e) = twoProd ah bh
        quickTwoSum p (e + ah * bl + al * bh)

    /// `a / (bh + bl)` for a plain `a`: one quotient and one corrected remainder.
    let private ddDivD (a: float) (bh: float) (bl: float) : struct (float * float) =
        let q = a / bh
        let struct (p, e) = twoProd q bh
        quickTwoSum q ((((a - p) - e) - q * bl) / bh)

    let private thirdHi = 0.3333333333333333
    let private thirdLo = 1.850371707708594e-17
    let private fifthHi = 0.2
    let private fifthLo = -1.1102230246251566e-17
    let private r7 = 1.0 / 7.0
    let private r9 = 1.0 / 9.0
    let private r11 = 1.0 / 11.0
    let private r13 = 1.0 / 13.0
    let private r15 = 1.0 / 15.0
    let private r17 = 1.0 / 17.0
    let private r19 = 1.0 / 19.0
    let private r21 = 1.0 / 21.0
    let private r23 = 1.0 / 23.0
    let private r25 = 1.0 / 25.0

    /// `ln x` for a positive finite `x` as a double-double, to about `2^-80` of itself: the same fold
    /// as `ln`, then `ln(1 + f) = 2s · (1 + z · (1/3 + z · (1/5 + T)))` with `s = f / (2 + f)` and
    /// `z = s²` in double-double and the tail `T = z/7 + … + z^10/25` in plain arithmetic, where it
    /// weighs below `2^-66` of the whole; `k · ln 2` is added from the split constant.
    let private lnDD (x: float) : struct (float * float) =
        let struct (k, f) = reduce x
        let struct (dh, dl) = twoSum 2.0 f
        let struct (sh, sl) = ddDivD f dh dl
        let struct (zh, zl) = ddMul sh sl sh sl

        let tail =
            zh
            * (r7
               + zh
                 * (r9
                    + zh
                      * (r11
                         + zh
                           * (r13 + zh * (r15 + zh * (r17 + zh * (r19 + zh * (r21 + zh * (r23 + zh * r25)))))))))

        let struct (fh, fl) = twoSum fifthHi tail
        let struct (ph, pl) = ddMul zh zl fh (fl + fifthLo)
        let struct (uh, ul) = ddAdd thirdHi thirdLo ph pl
        let struct (qh, ql) = ddMul zh zl uh ul
        let struct (vh, vl) = ddAdd 1.0 0.0 qh ql
        let struct (lh, ll) = ddMul (2.0 * sh) (2.0 * sl) vh vl

        if k = 0 then
            struct (lh, ll)
        else
            let dk = float k
            ddAdd (dk * ln2Hi) (dk * ln2Lo) lh ll

    let private overflowThreshold = 709.782712893384
    let private underflowThreshold = -745.1332191019412

    /// `x^y` for a positive finite `x` and a finite non-zero `y` the table did not answer:
    /// `exp(y · ln x)` with the product carried to double-double, so the `exp` core reads the part a
    /// plain product would have rounded away. The one-ulp bound of `expCore` carries over.
    let private powCore (x: float) (y: float) : float =
        let struct (lh, ll) = lnDD x
        let estimate = y * lh

        if estimate > overflowThreshold + 1.0 then
            Double.PositiveInfinity
        elif estimate < underflowThreshold - 1.0 then
            0.0
        else
            let struct (ph, e) = twoProd y lh
            let pl = e + y * ll
            let p = ph + pl

            if p > overflowThreshold then
                Double.PositiveInfinity
            elif p < underflowThreshold then
                0.0
            else
                let ap = if p < 0.0 then -p else p

                if ap > halfLn2 then
                    let k = nearestK p
                    let t = float k
                    expCore (ph - t * ln2Hi) (t * ln2Lo - pl) k
                else
                    expCore ph (-pl) 0

    /// The pinned float-exponent power: D43's table, then IEEE-754's rules for an infinite or zero
    /// operand and for a negative base under an integer exponent (the sign by the exponent's parity),
    /// the four exact cases `y = 1`, `y = −1`, `y = 2` and `y = 1/2` (the operand, its reciprocal,
    /// its square, its square root — each one correctly rounded operation), and otherwise
    /// `exp(y · ln x)` with the product in double-double. A negative base under a non-integer
    /// exponent answers NaN (`powTable` says `Undefined`). An integer-cell exponent is not this
    /// function's: the evaluator's binary-exponentiation routine (D43 item 2) keeps it.
    let pow (x: float) (y: float) : float =
        match powTable x y with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            let ax = if x < 0.0 then -x else x

            if Double.IsInfinity y then
                if (ax < 1.0) = (y > 0.0) then
                    0.0
                else
                    Double.PositiveInfinity
            elif Double.IsInfinity x then
                let magnitude = if y > 0.0 then Double.PositiveInfinity else 0.0
                if x < 0.0 && isOddInteger y then -magnitude else magnitude
            elif x = 0.0 then
                // y > 0 here: a zero base under a negative exponent is the table's.
                if isOddInteger y then x else 0.0
            else
                let magnitude =
                    if y = 1.0 then ax
                    elif y = -1.0 then 1.0 / ax
                    elif y = 2.0 then ax * ax
                    elif y = 0.5 then sqrt ax
                    else powCore ax y

                if x < 0.0 && isOddInteger y then -magnitude else magnitude

    // ---- erfc (fdlibm s_erf.c) and the standard normal distribution (Phase 600) -------------------

    let private erx = 8.45062911510467529297e-01
    let private pp0 = 1.28379167095512558561e-01
    let private pp1 = -3.25042107247001499370e-01
    let private pp2 = -2.84817495755985104766e-02
    let private pp3 = -5.77027029648944159157e-03
    let private pp4 = -2.37630166566501626084e-05
    let private qq1 = 3.97917223959155352819e-01
    let private qq2 = 6.50222499887672944485e-02
    let private qq3 = 5.08130628187576562776e-03
    let private qq4 = 1.32494738004321644526e-04
    let private qq5 = -3.96022827877536812320e-06
    let private pa0 = -2.36211856075265944077e-03
    let private pa1 = 4.14856118683748331666e-01
    let private pa2 = -3.72207876035701323847e-01
    let private pa3 = 3.18346619901161753674e-01
    let private pa4 = -1.10894694282396677476e-01
    let private pa5 = 3.54783043256182359371e-02
    let private pa6 = -2.16637559486879084300e-03
    let private qa1 = 1.06420880400844228286e-01
    let private qa2 = 5.40397917702171048937e-01
    let private qa3 = 7.18286544141962662868e-02
    let private qa4 = 1.26171219808761642112e-01
    let private qa5 = 1.36370839120290507362e-02
    let private qa6 = 1.19844998467991074170e-02
    let private ra0 = -9.86494403484714822705e-03
    let private ra1 = -6.93858572707181764372e-01
    let private ra2 = -1.05586262253232909814e+01
    let private ra3 = -6.23753324503260060396e+01
    let private ra4 = -1.62396669462573470355e+02
    let private ra5 = -1.84605092906711035994e+02
    let private ra6 = -8.12874355063065934246e+01
    let private ra7 = -9.81432934416914548592e+00
    let private sa1 = 1.96512716674392571292e+01
    let private sa2 = 1.37657754143519042600e+02
    let private sa3 = 4.34565877475229228821e+02
    let private sa4 = 6.45387271733267880336e+02
    let private sa5 = 4.29008140027567833386e+02
    let private sa6 = 1.08635005541779435134e+02
    let private sa7 = 6.57024977031928170135e+00
    let private sa8 = -6.04244152148580987438e-02
    let private rb0 = -9.86494292470009928597e-03
    let private rb1 = -7.99283237680523006574e-01
    let private rb2 = -1.77579549177547519889e+01
    let private rb3 = -1.60636384855821916062e+02
    let private rb4 = -6.37566443368389627722e+02
    let private rb5 = -1.02509513161107724954e+03
    let private rb6 = -4.83519191608651397019e+02
    let private sb1 = 3.03380607434824582924e+01
    let private sb2 = 3.25792512996573918826e+02
    let private sb3 = 1.53672958608443695994e+03
    let private sb4 = 3.19985821950859553908e+03
    let private sb5 = 2.55305040643316442583e+03
    let private sb6 = 4.74528541206955367215e+02
    let private sb7 = -2.24409524465858183362e+01

    /// fdlibm's split between its two outer erfc branches, `1/0.35` cut to its high word: the double
    /// whose high word is `0x4006db6d` and whose low word is zero.
    let private erfcSplit = 2.8571414947509766
    let private twoToMinus56 = 1.3877787807814457e-17

    /// `⌊v⌋` for `0 ≤ v < 2^52`, with `+` and `−` only: `v + 2^52 − 2^52` rounds to the nearest integer.
    let private floorSmall (v: float) : float =
        let r = (v + twoTo52) - twoTo52
        if r > v then r - 1.0 else r

    /// `x` in `[1.25, 28)` with its significand cut to its leading twenty-one bits (fdlibm's
    /// `SET_LOW_WORD(z, 0)`), by one exact product, one exact floor and one exact quotient.
    let private highWord (x: float) : float =
        let e =
            if x >= 16.0 then 4
            elif x >= 8.0 then 3
            elif x >= 4.0 then 2
            elif x >= 2.0 then 1
            else 0

        let scale = twoTo (20 - e)
        floorSmall (x * scale) / scale

    /// `erfc |x|` for `|x|` in `[0.84375, 28)` (fdlibm's `erfc1` and `erfc2`).
    let private erfcTail (ax: float) : float =
        if ax < 1.25 then
            let s = ax - 1.0
            let p = pa0 + s * (pa1 + s * (pa2 + s * (pa3 + s * (pa4 + s * (pa5 + s * pa6)))))
            let q = 1.0 + s * (qa1 + s * (qa2 + s * (qa3 + s * (qa4 + s * (qa5 + s * qa6)))))
            1.0 - erx - p / q
        else
            let s = 1.0 / (ax * ax)

            let r, q =
                if ax < erfcSplit then
                    ra0
                    + s * (ra1 + s * (ra2 + s * (ra3 + s * (ra4 + s * (ra5 + s * (ra6 + s * ra7)))))),
                    1.0
                    + s
                      * (sa1
                         + s * (sa2 + s * (sa3 + s * (sa4 + s * (sa5 + s * (sa6 + s * (sa7 + s * sa8)))))))
                else
                    rb0 + s * (rb1 + s * (rb2 + s * (rb3 + s * (rb4 + s * (rb5 + s * rb6))))),
                    1.0
                    + s * (sb1 + s * (sb2 + s * (sb3 + s * (sb4 + s * (sb5 + s * (sb6 + s * sb7))))))

            let z = highWord ax
            exp (-z * z - 0.5625) * exp ((z - ax) * (z + ax) + r / q) / ax

    /// The pinned complementary error function `erfc x = 1 − erf x` (Phase 600, DECISIONS.md D58):
    /// fdlibm's `s_erf.c` (Sun Microsystems, 1993) transcribed with its published coefficients, its two
    /// exponentials through the pinned `exp` and its low-word cut done by exact arithmetic, so it
    /// answers the same bits on every host. `erfc NaN` is NaN, `erfc +∞` is `0`, `erfc −∞` is `2`.
    let erfc (x: float) : float =
        if Double.IsNaN x then
            Double.NaN
        elif Double.IsPositiveInfinity x then
            0.0
        elif Double.IsNegativeInfinity x then
            2.0
        else
            let ax = if x < 0.0 then -x else x

            if ax < 0.84375 then
                if ax < twoToMinus56 then
                    1.0 - x
                else
                    let z = x * x
                    let r = pp0 + z * (pp1 + z * (pp2 + z * (pp3 + z * pp4)))
                    let s = 1.0 + z * (qq1 + z * (qq2 + z * (qq3 + z * (qq4 + z * qq5))))
                    let y = r / s

                    if x < 0.25 then
                        1.0 - (x + x * y)
                    else
                        0.5 - (x - 0.5 + x * y)
            elif ax < 28.0 then
                let v = erfcTail ax
                if x < 0.0 then 2.0 - v else v
            elif x < 0.0 then
                2.0
            else
                0.0

    /// `1/√2` as a double and the double nearest its remainder; `2/√π` and `1/√(2π)`, each the double
    /// nearest.
    let private invSqrt2Hi = 0.7071067811865476
    let private invSqrt2Lo = -4.833646656726457e-17
    let private twoOverSqrtPi = 1.1283791670955126
    let private invSqrt2Pi = 0.3989422804014327

    /// The pinned standard normal distribution function `Φ(x) = erfc(−x/√2) / 2` (Phase 600, D58): the
    /// argument carried as a double `t` and its rounding error `t'`, folded back by the first-order term
    /// `− t' · (2/√π) · exp(−t²)`, so the tail does not amplify the rounding of `x/√2`. `Φ(NaN)` is NaN;
    /// below `x = −40` it is `0`, above `40` it is `1`.
    let normalCdf (x: float) : float =
        if Double.IsNaN x then
            Double.NaN
        elif x < -40.0 then
            0.0
        elif x > 40.0 then
            1.0
        else
            let struct (p, e) = twoProd x invSqrt2Hi
            let th = -p
            let tl = -(e + x * invSqrt2Lo)
            0.5 * (erfc th - tl * twoOverSqrtPi * exp (-(th * th)))

    /// The pinned standard normal density `φ(x) = exp(−x²/2)/√(2π)` (Phase 600, D58): `x²` as a double
    /// and its exact error, the error folded back by a first-order term. `φ(NaN)` is NaN; beyond
    /// `|x| = 40` it is `0`.
    let normalDensity (x: float) : float =
        if Double.IsNaN x then
            Double.NaN
        elif x < -40.0 || x > 40.0 then
            0.0
        else
            let struct (h, l) = twoProd x x
            exp (-0.5 * h) * (1.0 - 0.5 * l) * invSqrt2Pi

    /// The pinned standard normal quantile `Φ⁻¹(p)` (Phase 600, D58): Wichura's AS 241 (`PPND16`,
    /// Applied Statistics 37, 1988), three rational functions with their published coefficients, the
    /// tails' `√(−ln p)` through the pinned `ln` and the correctly rounded square root. `Φ⁻¹(0)` is `−∞`
    /// and `Φ⁻¹(1)` is `+∞`; outside `[0, 1]`, or at NaN, it is NaN. Below `p = e^−729`, a subnormal, the
    /// third function is used beyond the range it was fitted on.
    let normalQuantile (p: float) : float =
        if Double.IsNaN p || p < 0.0 || p > 1.0 then
            Double.NaN
        elif p = 0.0 then
            Double.NegativeInfinity
        elif p = 1.0 then
            Double.PositiveInfinity
        else
            let q = p - 0.5

            if (if q < 0.0 then -q else q) <= 0.425 then
                let r = 0.180625 - q * q

                let num =
                    ((((((r * 2509.0809287301226727 + 33430.575583588128105) * r + 67265.770927008700853)
                        * r
                        + 45921.953931549871457)
                       * r
                       + 13731.693765509461125)
                      * r
                      + 1971.5909503065514427)
                     * r
                     + 133.14166789178437745)
                    * r
                    + 3.387132872796366608

                let den =
                    ((((((r * 5226.495278852854561 + 28729.085735721942674) * r + 39307.89580009271061)
                        * r
                        + 21213.794301586595867)
                       * r
                       + 5394.1960214247511077)
                      * r
                      + 687.1870074920579083)
                     * r
                     + 42.313330701600911252)
                    * r
                    + 1.0

                q * num / den
            else
                let tail = if q > 0.0 then 1.0 - p else p
                let r = sqrt (-(ln tail))

                let v =
                    if r <= 5.0 then
                        let r = r - 1.6

                        let num =
                            ((((((r * 7.7454501427834140764e-4 + 0.0227238449892691845833) * r
                                 + 0.24178072517745061177)
                                * r
                                + 1.27045825245236838258)
                               * r
                               + 3.64784832476320460504)
                              * r
                              + 5.7694972214606914055)
                             * r
                             + 4.6303378461565452959)
                            * r
                            + 1.42343711074968357734

                        let den =
                            ((((((r * 1.05075007164441684324e-9 + 5.475938084995344946e-4) * r
                                 + 0.0151986665636164571966)
                                * r
                                + 0.14810397642748007459)
                               * r
                               + 0.68976733498510000455)
                              * r
                              + 1.6763848301838038494)
                             * r
                             + 2.05319162663775882187)
                            * r
                            + 1.0

                        num / den
                    else
                        let r = r - 5.0

                        let num =
                            ((((((r * 2.01033439929228813265e-7 + 2.71155556874348757815e-5) * r
                                 + 0.0012426609473880784386)
                                * r
                                + 0.026532189526576123093)
                               * r
                               + 0.29656057182850489123)
                              * r
                              + 1.7848265399172913358)
                             * r
                             + 5.4637849111641143699)
                            * r
                            + 6.6579046435011037772

                        let den =
                            ((((((r * 2.04426310338993978564e-15 + 1.4215117583164458887e-7) * r
                                 + 1.8463183175100546818e-5)
                                * r
                                + 7.868691311456132591e-4)
                               * r
                               + 0.0148753612908506148525)
                              * r
                              + 0.13692988092273580531)
                             * r
                             + 0.59983220655588793769)
                            * r
                            + 1.0

                        num / den

                if q < 0.0 then -v else v

    // ---- sin, cos, atan and atan2 (Phase 725, DECISIONS.md D84) -----------------------------------

    /// The doubles nearest `π`, `π/2`, `π/4` and `3π/4`, and the doubles nearest what `π` and `π/2`
    /// leave past their own.
    let private pi = 3.141592653589793
    let private piLo = 1.2246467991473532e-16
    let private halfPi = 1.5707963267948966
    let private halfPiLo = 6.123233995736766e-17
    let private quarterPi = 0.7853981633974483
    let private threeQuarterPi = 2.356194490192345

    /// Whether `x` carries a minus sign, a zero's included: `1 / −0` is `−∞`.
    let private isNegative (x: float) : bool = x < 0.0 || (x = 0.0 && 1.0 / x < 0.0)

    /// `sin`'s table: a NaN propagates; an infinity is outside the reals; a zero answers itself, its
    /// sign kept; everything else the routine answers.
    let sinTable (x: float) : SpecialValue =
        if Double.IsNaN x then SpecialValue.NaN
        elif Double.IsInfinity x then SpecialValue.Undefined
        elif x = 0.0 then SpecialValue.Value x
        else SpecialValue.Compute

    /// `cos`'s table: a NaN propagates; an infinity is outside the reals; everything else the routine
    /// answers (a zero included: the routine answers exactly `1`).
    let cosTable (x: float) : SpecialValue =
        if Double.IsNaN x then SpecialValue.NaN
        elif Double.IsInfinity x then SpecialValue.Undefined
        else SpecialValue.Compute

    /// `atan`'s table: a NaN propagates; a zero answers itself, its sign kept; `±∞` answers `±π/2`;
    /// everything else the routine answers.
    let atanTable (x: float) : SpecialValue =
        if Double.IsNaN x then
            SpecialValue.NaN
        elif x = 0.0 then
            SpecialValue.Value x
        elif Double.IsPositiveInfinity x then
            SpecialValue.Value halfPi
        elif Double.IsNegativeInfinity x then
            SpecialValue.Value -halfPi
        else
            SpecialValue.Compute

    /// `atan2`'s table, IEEE 754's in full; every answer takes the sign of `y`, a zero's included. A
    /// NaN operand propagates. A zero `y` answers a zero where `x` is positive or `+0` and `π` where
    /// `x` is negative or `−0`. A zero `x` under a non-zero `y` answers `π/2`. An infinite `y` answers
    /// `π/4` against `+∞`, `3π/4` against `−∞` and `π/2` against a finite `x`. A finite `y` against
    /// `+∞` answers a zero, and against `−∞`, `π`. Everything else the routine answers.
    let atan2Table (y: float) (x: float) : SpecialValue =
        let signed (magnitude: float) : SpecialValue =
            SpecialValue.Value(if isNegative y then -magnitude else magnitude)

        if Double.IsNaN y || Double.IsNaN x then
            SpecialValue.NaN
        elif y = 0.0 then
            signed (if isNegative x then pi else 0.0)
        elif x = 0.0 then
            signed halfPi
        elif Double.IsInfinity y then
            signed (
                if Double.IsPositiveInfinity x then quarterPi
                elif Double.IsNegativeInfinity x then threeQuarterPi
                else halfPi
            )
        elif Double.IsPositiveInfinity x then
            signed 0.0
        elif Double.IsNegativeInfinity x then
            signed pi
        else
            SpecialValue.Compute

    /// `sin (y + tail)` for `|y| ≤ π/4` and a `tail` below half a unit in the last place of `y`: the
    /// Taylor polynomial to `y¹⁹`, its coefficients the doubles nearest `(−1)ᵏ / (2k + 1)!`, by
    /// Horner's rule in `y²`, with the tail's first-order term `tail · cos y` joined before the one
    /// last addition. `Special`'s reflections read it with a zero tail.
    let internal sinKernel (y: float) (tail: float) : float =
        let z = y * y

        let p =
            ((((((((-8.22063524662433e-18 * z + 2.8114572543455206e-15) * z
                   + -7.647163731819816e-13)
                  * z
                  + 1.6059043836821613e-10)
                 * z
                 + -2.505210838544172e-08)
                * z
                + 2.7557319223985893e-06)
               * z
               + -0.0001984126984126984)
              * z
              + 0.008333333333333333)
             * z
             + -0.16666666666666666)

        y + (y * z * p + tail * (1.0 - 0.5 * z))

    /// `cos y − correction` for `|y| ≤ π/4`: the Taylor polynomial to `y¹⁸`, its coefficients the
    /// doubles nearest `(−1)ᵏ / (2k)!`, by Horner's rule in `y²`, added to one; then the exact
    /// rounding error of that addition (Dekker's, for a first term the larger) less the correction is
    /// added back. Under a zero correction the last sum is the addition's true value, which rounds to
    /// the addition's own answer, so `Special`'s reflections, which pass zero, read `1 + y² · p`
    /// rounded once, as they always did; a correction of a fraction of a unit in the last place moves
    /// the answer where the one rounding would have lost it.
    let internal cosKernel (y: float) (correction: float) : float =
        let z = y * y

        let p =
            ((((((((-1.5619206968586225e-16 * z + 4.779477332387385e-14) * z
                   + -1.1470745597729725e-11)
                  * z
                  + 2.08767569878681e-09)
                 * z
                 + -2.755731922398589e-07)
                * z
                + 2.48015873015873e-05)
               * z
               + -0.001388888888888889)
              * z
              + 0.041666666666666664)
             * z
             + -0.5)

        let w = z * p
        let c = 1.0 + w
        c + (((1.0 - c) + w) - correction)

    /// `cos (y + tail)` for `|y| ≤ π/4`: the kernel, corrected by the tail's first-order term
    /// `tail · y` and by what the rounding error `e` of `y²` moves the cosine by, `e · sin y / 2y`
    /// read as `e · (½ − y²/12)`. The kernel squares `y` itself and cannot see that error, which near
    /// `π/4` is a quarter of a unit in the last place of the answer.
    let private cosTailed (y: float) (tail: float) : float =
        let struct (z, e) = twoProd y y
        cosKernel y (y * tail + e * (0.5 - 0.08333333333333333 * z))

    /// The double nearest `2/π`, and `π/2` in three pieces of thirty-three bits and the double nearest
    /// what they leave: a multiple below `2²⁰` of each piece is exact.
    let private twoOverPi = 0.6366197723675814
    let private halfPi1 = 1.5707963267341256
    let private halfPi2 = 6.077100506303966e-11
    let private halfPi3 = 2.0222662487111665e-21
    let private halfPiTail = 8.4784276603689e-32
    let private twoTo20 = 1048576.0

    /// `x − n · π/2` for `π/4 < x ≤ 2²⁰` and `n` the nearest integer to `x · 2/π`, as `n mod 4`, the
    /// remainder and its tail: the three exact multiples subtracted in turn, each rounding error
    /// kept, and the last piece's multiple taken from their sum. `π/2` is read to 152 bits.
    let private reduceByPieces (x: float) : struct (int * float * float) =
        let n = int (x * twoOverPi + 0.5)
        let fn = float n
        let struct (s2, e2) = twoSum (x - fn * halfPi1) (-(fn * halfPi2))
        let struct (s3, e3) = twoSum s2 (-(fn * halfPi3))
        let struct (r, tail) = twoSum s3 ((e2 + e3) - fn * halfPiTail)
        struct (n % 4, r, tail)

    /// `2/π` in base `2²⁴`, fifty digits: digit `i` weighs `2^(−24(i + 1))`.
    let private twoOverPiDigits: float[] =
        [| 10680707.0
           7228996.0
           1387004.0
           2578385.0
           16069853.0
           12639074.0
           9804092.0
           4427841.0
           16666979.0
           11263675.0
           12935607.0
           2387514.0
           4345298.0
           14681673.0
           3074569.0
           13734428.0
           16653803.0
           1880361.0
           10960616.0
           8533493.0
           3062596.0
           8710556.0
           7349940.0
           6258241.0
           3772886.0
           3769171.0
           3798172.0
           8675211.0
           12450088.0
           3874808.0
           9961438.0
           366607.0
           15675153.0
           9132554.0
           7151469.0
           3571407.0
           2607881.0
           12013382.0
           4155038.0
           6285869.0
           7677882.0
           13102053.0
           15825725.0
           473591.0
           9065106.0
           15363067.0
           6271263.0
           9264392.0
           5636912.0
           4652155.0 |]

    let private twoTo24 = 16777216.0
    let private twoTo48 = 281474976710656.0
    let private twoToMinus24 = 5.960464477539063e-08
    let private twoToMinus48 = 3.552713678800501e-15

    /// Digit `j` of the nine read from digit `first` of `2/π`, and zero outside them.
    let private digitFrom (first: int) (j: int) : float =
        if j >= 0 && j <= 8 then twoOverPiDigits[first + j] else 0.0

    /// `x − n · π/2` for a finite `x > 2²⁰`, as `n mod 4`, the remainder and its tail. `x` is an
    /// integer below `2⁵³` times a power of two; the integer, in three limbs of twenty-four bits, is
    /// multiplied by nine digits of `2/π`, the first of them the first whose products are not all
    /// multiples of eight. Every product and every carry is an integer below `2⁵⁰`, so each is exact
    /// and the product is known to `2⁻¹³⁷`. Of its digits, the first that weighs below four and the
    /// seven after it are kept, each an exact double: the first two give `n` and the head of the
    /// fraction, taken to the nearer integer, and the rest its tail in three exact pairs; the fraction
    /// is summed to a double and a tail and multiplied by `π/2`.
    let private reduceByDigits (x: float) : struct (int * float * float) =
        let struct (e, m) = frexp x
        let whole = m * twoTo52
        let exponent = e - 52
        let m2 = floorSmall (whole * twoToMinus48)
        let rest = whole - m2 * twoTo48
        let m1 = floorSmall (rest * twoToMinus24)
        let m0 = rest - m1 * twoTo24
        let first = if exponent <= 26 then 0 else (exponent - 3) / 24
        // Position `i` of the product weighs 2^(top + 72 − 24i) and is fed by digit `i − 3`.
        let top = exponent - 24 * (first + 1)
        let a = (top + 70) / 24 + 1
        let mutable carry = 0.0
        let mutable leading = 0.0
        let mutable next = 0.0
        let mutable pair1 = 0.0
        let mutable pair2 = 0.0
        let mutable pair3 = 0.0

        for i = 11 downto 1 do
            let k = i - 3

            let c =
                m0 * digitFrom first k
                + m1 * digitFrom first (k + 1)
                + m2 * digitFrom first (k + 2)
                + carry

            let q = floorSmall (c * twoToMinus24)
            carry <- q
            let j = i - a

            if j >= 0 && j <= 7 then
                let t = (c - q * twoTo24) * twoTo (top + 72 - 24 * i)

                if j = 0 then leading <- t
                elif j = 1 then next <- t
                elif j <= 3 then pair1 <- pair1 + t
                elif j <= 5 then pair2 <- pair2 + t
                else pair3 <- pair3 + t

        let head = (leading - 4.0 * floorSmall (leading * 0.25)) + next
        let quadrant = floorSmall head
        let fraction = head - quadrant
        let up = fraction >= 0.5
        let struct (s1, e1) = twoSum (if up then fraction - 1.0 else fraction) pair1
        let struct (s2, e2) = twoSum s1 pair2
        let struct (s3, e3) = twoSum s2 pair3
        let struct (fh, fl) = twoSum s3 (e1 + e2 + e3)
        let struct (r, tail) = ddMul fh fl halfPi halfPiLo
        struct ((int quadrant + (if up then 1 else 0)) % 4, r, tail)

    /// A finite `x ≥ 0` less its nearest multiple of `π/2`: as it stands up to `π/4`, by the three
    /// pieces up to `2²⁰`, by the digits of `2/π` beyond, so the remainder is exact to its tail at
    /// every finite double.
    let private reduceHalfPi (x: float) : struct (int * float * float) =
        if x <= quarterPi then struct (0, x, 0.0)
        elif x <= twoTo20 then reduceByPieces x
        else reduceByDigits x

    /// `(sin x, cos x)` for a finite non-zero `x`, the argument reduced once.
    let private sinCosCompute (x: float) : struct (float * float) =
        let struct (n, r, tail) = reduceHalfPi (if x < 0.0 then -x else x)
        let s = sinKernel r tail
        let c = cosTailed r tail

        let struct (s, c) =
            if n = 0 then struct (s, c)
            elif n = 1 then struct (c, -s)
            elif n = 2 then struct (-s, -c)
            else struct (-c, s)

        struct ((if x < 0.0 then -s else s), c)

    /// The pinned sine (Phase 725, DECISIONS.md D84): the table, then the argument less its nearest
    /// multiple of `π/2` and one of the two kernels by the multiple's quadrant. The reduction is exact
    /// to its tail at every finite double, so a large argument is answered as accurately as a small
    /// one: the measured distance from the true value is below one unit in the last place over every
    /// range D84 states. `sin NaN` and `sin ±∞` are NaN; `sin −0` is `−0`.
    let sin (x: float) : float =
        match sinTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            let struct (n, r, tail) = reduceHalfPi (if x < 0.0 then -x else x)

            let v =
                if n = 0 then sinKernel r tail
                elif n = 1 then cosTailed r tail
                elif n = 2 then -(sinKernel r tail)
                else -(cosTailed r tail)

            if x < 0.0 then -v else v

    /// The pinned cosine (Phase 725, D84): the table, then the same reduction and kernels as `sin`,
    /// and the same accuracy at every finite double. `cos NaN` and `cos ±∞` are NaN.
    let cos (x: float) : float =
        match cosTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            let struct (n, r, tail) = reduceHalfPi (if x < 0.0 then -x else x)

            if n = 0 then cosTailed r tail
            elif n = 1 then -(sinKernel r tail)
            elif n = 2 then -(cosTailed r tail)
            else sinKernel r tail

    /// The pinned sine and cosine of one argument, reduced once (Phase 725, D84): `sin x` and `cos x`
    /// bit for bit, each answered by its own table first.
    let sinCos (x: float) : struct (float * float) =
        match sinTable x with
        | SpecialValue.Compute -> sinCosCompute x
        | _ -> struct (sin x, cos x)

    let private twoTo66 = 7.378697629483821e+19
    let private twoToMinus29 = 1.862645149230957e-09
    let private atanHalfHi = 0.4636476090008061
    let private atanHalfLo = 2.2698777452961687e-17
    let private atanOneLo = 3.061616997868383e-17
    let private atanThreeHalvesHi = 0.982793723247329
    let private atanThreeHalvesLo = 1.3903311031230998e-17
    let private piLessAtanHalfHi = 2.677945044588987
    let private piLessAtanHalfLo = 1.5527705369303147e-16
    let private threeQuarterPiLo = 9.184850993605148e-17
    let private piLessAtanThreeHalvesHi = 2.158798930342464
    let private piLessAtanThreeHalvesLo = 2.1958367134601997e-16
    let private at0 = 0.3333333333333293
    let private at1 = -0.19999999999876483
    let private at2 = 0.14285714272503466
    let private at3 = -0.11111110405462356
    let private at4 = 0.09090887133436507
    let private at5 = -0.0769187620504483
    let private at6 = 0.06661073137387531
    let private at7 = -0.058335701337905735
    let private at8 = 0.049768779946159324
    let private at9 = -0.036531572744216916
    let private at10 = 0.016285820115365782

    /// `atan (x + tail)` for a finite `x > 0` and a `tail` below half a unit in the last place of `x`,
    /// or `π` less it where `fromPi` is set (fdlibm's `s_atan.c`, Sun Microsystems, 1993, with its
    /// published coefficients): below `7/16` the argument stands; up to `11/16`, `19/16` and `39/16`
    /// it is moved against `atan ½`, `atan 1` and `atan 3/2`, and beyond against `π/2`, each of those
    /// a double and its remainder; then one odd polynomial, its even and odd halves summed apart as
    /// fdlibm sums them. The tail joins each arm's numerator, which is otherwise exact. Taken from
    /// `π`, each arm reads `π` less its constant as a double and its remainder, so the answer is
    /// rounded once and not twice. Below `2⁻²⁹` the arc tangent is the argument, and from `2⁶⁶` it
    /// is `π/2`.
    let private atanCompute (x: float) (tail: float) (fromPi: bool) : float =
        if x >= twoTo66 then
            halfPi
        elif x < twoToMinus29 then
            if fromPi then pi - ((x + tail) - piLo) else x + tail
        else
            let arm =
                if x < 0.4375 then 0
                elif x < 0.6875 then 1
                elif x < 1.1875 then 2
                elif x < 2.4375 then 3
                else 4

            let t =
                if arm = 0 then x
                elif arm = 1 then ((2.0 * x - 1.0) + 2.0 * tail) / (2.0 + x)
                elif arm = 2 then ((x - 1.0) + tail) / (x + 1.0)
                elif arm = 3 then ((x - 1.5) + tail) / (1.0 + 1.5 * x)
                else -1.0 / x

            let z = t * t
            let w = z * z
            let s1 = z * (at0 + w * (at2 + w * (at4 + w * (at6 + w * (at8 + w * at10)))))
            let s2 = w * (at1 + w * (at3 + w * (at5 + w * (at7 + w * at9))))
            let s = t * (s1 + s2)

            if fromPi then
                if arm = 0 then
                    pi + ((s + (piLo - tail * (1.0 - z))) - t)
                elif arm = 1 then
                    piLessAtanHalfHi + ((s + piLessAtanHalfLo) - t)
                elif arm = 2 then
                    threeQuarterPi + ((s + threeQuarterPiLo) - t)
                elif arm = 3 then
                    piLessAtanThreeHalvesHi + ((s + piLessAtanThreeHalvesLo) - t)
                else
                    halfPi + ((s + (halfPiLo - tail * z)) - t)
            elif arm = 0 then
                t - (s - tail * (1.0 - z))
            elif arm = 1 then
                atanHalfHi - ((s - atanHalfLo) - t)
            elif arm = 2 then
                quarterPi - ((s - atanOneLo) - t)
            elif arm = 3 then
                atanThreeHalvesHi - ((s - atanThreeHalvesLo) - t)
            else
                halfPi - ((s - (halfPiLo + tail * z)) - t)

    /// The pinned arc tangent (Phase 725, D84): the table, then fdlibm's reduction and polynomial,
    /// odd in its argument bit for bit. The measured distance from the true value is below one unit in
    /// the last place (D84). `atan NaN` is NaN, `atan ±0` is `±0` and `atan ±∞` is `±π/2`.
    let atan (x: float) : float =
        match atanTable x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            if x < 0.0 then
                -(atanCompute -x 0.0 false)
            else
                atanCompute x 0.0 false

    let private twoTo400 = 2.5822498780869086e+120
    let private twoToMinus400 = 3.8725919148493183e-121
    let private twoTo600 = 4.149515568880993e+180
    let private twoToMinus600 = 2.409919865102884e-181

    /// The pinned two-argument arc tangent, the angle of the point `(x, y)` in `[−π, π]` (Phase 725,
    /// D84): IEEE 754's table, then the arc tangent of `|y| / |x|` with the quotient's remainder
    /// carried as the tail, so the division's rounding does not reach the answer, taken from zero
    /// where `x` is positive and from `π` where it is negative, and given the sign of `y`. A quotient
    /// below `2⁻²⁹` or from `2⁶⁶` needs no tail: its arc tangent is the quotient or `π/2`. The
    /// measured distance from the true value is below one unit in the last place in every quadrant
    /// (D84). `atan2 y 1` is `atan y` bit for bit.
    let atan2 (y: float) (x: float) : float =
        match atan2Table y x with
        | SpecialValue.NaN -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Compute ->
            let ay = if y < 0.0 then -y else y
            let ax = if x < 0.0 then -x else x
            let q = ay / ax

            let tail =
                if q < twoToMinus29 || q >= twoTo66 then
                    0.0
                else
                    // Both operands by one power of two, so the product's halves neither overflow
                    // nor fall into the subnormals; the quotient is the same.
                    let scale =
                        if ax > twoTo400 then twoToMinus600
                        elif ax < twoToMinus400 then twoTo600
                        else 1.0

                    let sx = ax * scale
                    let struct (p, e) = twoProd q sx
                    ((ay * scale - p) - e) / sx

            let z = atanCompute q tail (x < 0.0)
            if y < 0.0 then -z else z
