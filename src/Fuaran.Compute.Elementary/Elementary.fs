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
