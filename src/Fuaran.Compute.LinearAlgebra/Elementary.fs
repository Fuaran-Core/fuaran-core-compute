namespace Fuaran.Compute.LinearAlgebra

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

    let private twoTo52 = 4503599627370496.0
    let private twoTo53 = 9007199254740992.0

    /// Whether a finite `y` is an integer: at or above `2^52` every float is; below, adding and
    /// subtracting `2^52` rounds `|y|` to the nearest integer, and `y` is one exactly when that is `|y|`.
    let private isInteger (y: float) : bool =
        let ay = if y < 0.0 then -y else y
        ay >= twoTo52 || (ay + twoTo52) - twoTo52 = ay

    /// Whether a finite integer-valued `y` is odd: at or above `2^53` every integer is even; below,
    /// `|y| / 2` is exact and odd means it is not an integer.
    let private isOddInteger (y: float) : bool =
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
    let private twoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        let bb = s - a
        struct (s, (a - (s - bb)) + (b - bb))

    /// `a + b` as the rounded sum and its exact error, for `|a| ≥ |b|`.
    let private quickTwoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        struct (s, b - (s - a))

    /// `a · b` as the rounded product and its exact error, each factor split into two halves of
    /// twenty-six bits (no overflow for `|a|, |b|` below `2^996`, which every caller keeps).
    let private twoProd (a: float) (b: float) : struct (float * float) =
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
