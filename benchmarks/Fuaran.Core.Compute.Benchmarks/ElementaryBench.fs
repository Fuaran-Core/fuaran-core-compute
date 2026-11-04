/// Phase 593 — the measurement behind DECISIONS.md D52: the two candidate builds of `exp`, `ln`,
/// `log10` and the float-exponent `pow` side by side, on this host.
///
///   * **Faithful-pinned** — the package's `Elementary` module: one argument reduction and one
///     polynomial, written with `+ − × ÷` in a pinned order.
///   * **Correctly rounded** — a Ziv two-phase prototype kept HERE and nowhere else: the same reduction
///     carried in double-double arithmetic to about `2^-95`, a rounding test on the result, and a
///     multiprecision slow path (fixed-point `BigInteger`, 320 fractional bits) for the inputs the test
///     cannot decide. Correctly rounded is host-independent by definition; what it costs is the point.
///
/// For each function and candidate the harness prints nanoseconds per value over a pinned seeded sample
/// (the same draw on every host), then the sample itself with both candidates' answers as IEEE-754 bit
/// patterns between `BEGIN elementary-samples` and `END` lines, so a checker with a high-precision
/// reference can state each candidate's error in units in the last place and compare two hosts' lines
/// byte for byte. `node Program.js <runs> elementary`, or the same program on .NET.
module Fuaran.Core.Compute.Benchmarks.ElementaryBench

open System
open System.Numerics
open Fuaran.Compute.LinearAlgebra

let private nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

let private hex (x: float) : string =
    if Double.IsNaN x then
        "nan"
    else
        let b = bits x
        let digits = "0123456789abcdef"

        Array.init 16 (fun k -> string digits[int ((b >>> (4 * (15 - k))) &&& 0xFL)])
        |> String.concat ""

// ---- the pinned sample ------------------------------------------------------------------------------

/// Park and Miller's minimal standard in float arithmetic, as `LinearAlgebraLaws` draws: exact on
/// every host.
type private Draw(seed: int) =
    let mutable state =
        ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

    member _.Next() : float =
        state <- (state * 48271.0) % 2147483647.0
        state

    /// A float in `[0, 1)`.
    member this.Unit() : float = this.Next() / 2147483647.0

    /// A float in `[lo, hi)`.
    member this.Range (lo: float) (hi: float) : float = lo + (hi - lo) * this.Unit()

/// `2^k` by exact doubling or halving.
let private twoTo (k: int) : float =
    let mutable p = 1.0

    if k >= 0 then
        for _ in 1..k do
            p <- p * 2.0
    else
        for _ in 1 .. -k do
            p <- p * 0.5

    p

/// The `exp` sample: uniform over the computed range, with clusters round zero, the half-ln2 reduction
/// boundary and both edges.
let private expSample (d: Draw) : float[] =
    [| for _ in 1..2000 -> d.Range -745.13 709.78
       for _ in 1..400 -> d.Range -1.0 1.0
       for k in 1..60 do
           yield twoTo (-k)
           yield -twoTo(-k)
       for _ in 1..200 -> 0.34657359027997264 + d.Range -1e-6 1e-6
       for _ in 1..200 -> 709.782712893384 - d.Range 0.0 1e-3
       for _ in 1..200 -> -745.1332191019412 + d.Range 0.0 40.0
       for _ in 1..200 -> -708.4 + d.Range -1.0 1.0 |]

/// The `ln` and `log10` sample: log-uniform across the positive doubles, a cluster round one, the
/// subnormals and the extremes.
let private lnSample (d: Draw) : float[] =
    [| for _ in 1..2000 -> twoTo (int (d.Range -1074.0 1024.0)) * d.Range 1.0 2.0
       for _ in 1..600 -> 1.0 + d.Range -1e-3 1e-3
       for k in 1..52 do
           yield 1.0 + twoTo (-k)
           yield 1.0 - twoTo (-k)
       for _ in 1..200 -> 1.4142135623730951 + d.Range -1e-6 1e-6
       for _ in 1..200 -> Double.Epsilon * float (1 + int (d.Range 0.0 1e6))
       yield Double.MaxValue
       yield Double.Epsilon
       yield 2.2250738585072014e-308 |]

/// The `pow` sample: bases across thirty decades and round one, exponents across the ranges the
/// base admits, every pair finite and inside the computed range; negative bases under integer
/// exponents too.
let private powSample (d: Draw) : (float * float)[] =
    let finite (x: float) (y: float) =
        let r = Elementary.pow x y
        not (Double.IsInfinity r) && r <> 0.0 && not (Double.IsNaN r)

    [| for _ in 1..3000 do
           let x = twoTo (int (d.Range -100.0 100.0)) * d.Range 1.0 2.0
           let y = d.Range -30.0 30.0

           if finite x y then
               yield x, y
       for _ in 1..600 do
           let x = 1.0 + d.Range -1e-6 1e-6
           let y = d.Range -1e8 1e8

           if finite x y then
               yield x, y
       for _ in 1..300 do
           let x = 1.0 + twoTo (-52) * float (int (d.Range -8.0 8.0))
           let y = d.Range -1e15 1e15

           if finite x y then
               yield x, y
       for _ in 1..300 do
           let x = d.Range 1.0 3.0
           let y = d.Range 1000.0 1023.9 / 1.0
           let y = y * (1.0 / 1.5849625007211563) // scale so that x^y stays near the overflow edge

           if finite x y then
               yield x, y
       for _ in 1..300 do
           let x = twoTo (int (d.Range -1074.0 1023.0)) * d.Range 1.0 2.0
           let y = d.Range 0.0 1.0

           if finite x y then
               yield x, y
       for _ in 1..200 do
           let x = -(twoTo (int (d.Range -40.0 40.0)) * d.Range 1.0 2.0)
           let y = float (int (d.Range -40.0 40.0))

           if finite x y then
               yield x, y |]

// ---- the correctly rounded candidate (a prototype) ----------------------------------------------------

/// Double-double arithmetic for the fast path.
module private DD =
    let splitter = 134217729.0

    let twoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        let bb = s - a
        struct (s, (a - (s - bb)) + (b - bb))

    let quickTwoSum (a: float) (b: float) : struct (float * float) =
        let s = a + b
        struct (s, b - (s - a))

    let twoProd (a: float) (b: float) : struct (float * float) =
        let p = a * b
        let ca = splitter * a
        let ah = ca - (ca - a)
        let al = a - ah
        let cb = splitter * b
        let bh = cb - (cb - b)
        let bl = b - bh
        struct (p, ((ah * bh - p) + ah * bl + al * bh) + al * bl)

    let add (ah: float) (al: float) (bh: float) (bl: float) : struct (float * float) =
        let struct (s, e) = twoSum ah bh
        quickTwoSum s (e + al + bl)

    let mul (ah: float) (al: float) (bh: float) (bl: float) : struct (float * float) =
        let struct (p, e) = twoProd ah bh
        quickTwoSum p (e + ah * bl + al * bh)

    /// `(ah + al) / n` for a small integer `n`.
    let divInt (ah: float) (al: float) (n: float) : struct (float * float) =
        let q = ah / n
        let struct (p, e) = twoProd q n
        quickTwoSum q ((((ah - p) - e) + al) / n)

    let ln2Hi = 0.6931471803691238
    let ln2Lo = 1.9082149292705877e-10
    let ln2Lo2 = 1.1612227229362532e-26
    let invLn2 = 1.4426950408889634

    /// `(bits, exponent, mantissa)` of a positive finite float: `x = m · 2^e`, `m` in `[1, 2)`.
    let frexp (x: float) : struct (int * float) =
        let b = bits x
        let e = int ((b >>> 52) &&& 0x7FFL)

        if e = 0 then
            // subnormal: scale by 2^54 first
            let b2 = bits (x * 18014398509481984.0)
            let e2 = int ((b2 >>> 52) &&& 0x7FFL)
            struct (e2 - 1023 - 54, BitConverter.Int64BitsToDouble((b2 &&& 0x000FFFFFFFFFFFFFL) ||| 0x3FF0000000000000L))
        else
            struct (e - 1023, BitConverter.Int64BitsToDouble((b &&& 0x000FFFFFFFFFFFFFL) ||| 0x3FF0000000000000L))

    let scalb (y: float) (k: int) : float =
        if k >= -1022 && k <= 1023 then
            y * BitConverter.Int64BitsToDouble(int64 (k + 1023) <<< 52)
        else
            Double.NaN

    /// `exp(xh + xl)` as a double-double, valid for a result in the normal range; `None` outside it.
    let exp (xh: float) (xl: float) : struct (float * float) voption =
        let k =
            if xh > 0.0 then
                int (invLn2 * xh + 0.5)
            else
                int (invLn2 * xh - 0.5)

        if k < -1020 || k > 1022 then
            ValueNone
        else
            let t = float k
            let r0 = xh - t * ln2Hi
            let struct (p1, e1) = twoProd t ln2Lo
            let struct (rh, rl) = twoSum r0 (-p1)
            let struct (rh, rl) = quickTwoSum rh (rl - e1 - t * ln2Lo2 + xl)
            // r / 256, exactly
            let sh = rh * 0.00390625
            let sl = rl * 0.00390625
            let mutable eh = 1.0
            let mutable el = 0.0

            for n in 10..-1..1 do
                let struct (qh, ql) = divInt sh sl (float n)
                let struct (mh, ml) = mul qh ql eh el
                let struct (h, l) = add 1.0 0.0 mh ml
                eh <- h
                el <- l

            for _ in 1..8 do
                let struct (h, l) = mul eh el eh el
                eh <- h
                el <- l

            ValueSome(struct (scalb eh k, scalb el k))

    /// `ln x` as a double-double: one Newton step from the pinned `ln` through the double-double `exp`.
    /// The mantissa is folded into `[√2/2, √2)` first, so an `x` just below one meets no cancellation
    /// between `e · ln 2` and `ln m` (the first run of this harness folded nothing, and lost up to
    /// forty bits there).
    let ln (x: float) : struct (float * float) voption =
        let struct (e, m) = frexp x
        let e, m = if m > 1.4142135623730951 then e + 1, m * 0.5 else e, m
        let a0 = Elementary.ln m

        match exp (-a0) 0.0 with
        | ValueNone -> ValueNone
        | ValueSome(struct (gh, gl)) ->
            let struct (th, tl) = mul gh gl m 0.0
            let struct (th, tl) = quickTwoSum (th - 1.0) tl
            let struct (h, l) = add a0 0.0 th tl
            let struct (h, l) = add h l (-(0.5 * th * th)) 0.0
            let de = float e
            let struct (kh, kl) = twoProd de ln2Lo
            ValueSome(add (de * ln2Hi) (kh + kl + de * ln2Lo2) h l)

    let ivLn10Hi = 0.4342944819032518
    let ivLn10Lo = 1.098319650216765e-17

    let log10 (x: float) : struct (float * float) voption =
        match ln x with
        | ValueNone -> ValueNone
        | ValueSome(struct (lh, ll)) -> ValueSome(mul lh ll ivLn10Hi ivLn10Lo)

    let pow (x: float) (y: float) : struct (float * float) voption =
        match ln x with
        | ValueNone -> ValueNone
        | ValueSome(struct (lh, ll)) ->
            let struct (ph, pe) = twoProd y lh
            let pl = pe + y * ll

            if ph > 709.0 || ph < -708.0 then ValueNone else exp ph pl

    /// The relative error bound the fast path is trusted to: `2^-95`.
    let errorBound = 2.5243548967072378e-29

    /// Whether `h` is the correctly rounded double of `h + l ± errorBound · |h|`: `l` lies clear of the
    /// midpoint between `h` and its neighbour. `None` when the test cannot decide.
    let rounds (h: float) (l: float) : float voption =
        if h = 0.0 || Double.IsInfinity h || Double.IsNaN h then
            ValueNone
        else
            let ah = if h < 0.0 then -h else h

            if ah < 2.2250738585072014e-308 || ah > 8.98846567431158e+307 then
                ValueNone
            else
                let ulp = BitConverter.Int64BitsToDouble(bits ah + 1L) - ah
                let al = if l < 0.0 then -l else l

                if al < 0.5 * ulp - ah * errorBound then
                    ValueSome h
                else
                    ValueNone

/// The slow path: fixed-point arithmetic over `BigInteger` with 320 fractional bits.
module private Big =
    let p = 320
    let one = BigInteger.One <<< p

    let mul (a: BigInteger) (b: BigInteger) : BigInteger = (a * b) >>> p

    let bitLength (v: BigInteger) : int =
        let mutable n = 0
        let mutable t = v

        while not t.IsZero do
            t <- t >>> 1
            n <- n + 1

        n

    /// `(m · 2^52)` as an integer for a mantissa `m` in `[1, 2)`, then shifted to the fixed point.
    let ofMantissa (m: float) : BigInteger =
        BigInteger(int64 (m * 4503599627370496.0)) <<< (p - 52)

    /// A finite float with `|x| ≥ 2^-268` exactly (smaller magnitudes lose their tail).
    let ofFloat (x: float) : BigInteger =
        if x = 0.0 then
            BigInteger.Zero
        else
            let ax = if x < 0.0 then -x else x
            let struct (e, m) = DD.frexp ax
            let v = BigInteger(int64 (m * 4503599627370496.0))
            let shift = p - 52 + e
            let v = if shift >= 0 then v <<< shift else v >>> (-shift)
            if x < 0.0 then -v else v

    /// `2 · atanh s` for a fixed-point `|s| < 1/2`.
    let atanh2 (s: BigInteger) : BigInteger =
        let z = mul s s
        let mutable term = s
        let mutable sum = s
        let mutable k = 1

        while not term.IsZero do
            term <- mul term z
            sum <- sum + term / BigInteger(2 * k + 1)
            k <- k + 1

        sum <<< 1

    let ln2: BigInteger = atanh2 (one / BigInteger 3)

    /// `ln m` for a fixed-point `m` in `[1, 2)`.
    let lnMantissa (m: BigInteger) : BigInteger = atanh2 (((m - one) <<< p) / (m + one))

    /// `ln x` for a positive finite float.
    let ln (x: float) : BigInteger =
        let struct (e, m) = DD.frexp x
        BigInteger e * ln2 + lnMantissa (ofMantissa m)

    /// `exp x` for a fixed-point `|x| ≤ 746`: `(mantissa, k)` with the value `mantissa · 2^k`, the
    /// mantissa a fixed-point number in `[1, 2)` or just below.
    let exp (x: BigInteger) : struct (BigInteger * int) =
        let twice = (x <<< 1) + ln2
        let k = BigInteger.Divide(twice, ln2 <<< 1)
        // BigInteger division truncates toward zero; floor it for a negative numerator.
        let k =
            if twice.Sign < 0 && not (twice - k * (ln2 <<< 1)).IsZero then
                k - BigInteger.One
            else
                k

        let r = x - k * ln2
        let s = r >>> 16
        let mutable e = one

        for n in 24..-1..1 do
            e <- one + mul (s / BigInteger n) e

        for _ in 1..16 do
            e <- mul e e

        struct (e, int k)

    /// The double nearest to `v · 2^(k − p)`, `v > 0`, with the subnormal precision honoured.
    let toDouble (v: BigInteger) (k: int) : float =
        let len = bitLength v
        let lead = len - 1 + k - p // the exponent of the leading bit

        if lead > 1023 then
            Double.PositiveInfinity
        else
            let keep = if lead < -1022 then 53 - (-1022 - lead) else 53

            if keep <= 0 then
                // below half the smallest subnormal rounds to zero; at or above, to it
                if keep = 0 && (v > (BigInteger.One <<< (len - 1))) then
                    Double.Epsilon
                else
                    0.0
            else
                let shift = len - keep
                let mant = v >>> shift
                let rem = v - (mant <<< shift)
                let half = BigInteger.One <<< (shift - 1)

                let mant =
                    if rem > half || (rem = half && not mant.IsEven) then
                        mant + BigInteger.One
                    else
                        mant

                let m = float (int64 mant)
                // m · 2^(shift + k − p), in exact steps
                let exponent = shift + k - p
                let mutable r = m
                let mutable e = exponent

                while e > 0 do
                    let step = min e 1000
                    r <- r * DD.scalb 1.0 step
                    e <- e - step

                while e < 0 do
                    let step = min (-e) 1000
                    r <- r * DD.scalb 1.0 (-step)
                    e <- e + step

                r

    let expDouble (x: float) : float =
        let struct (m, k) = exp (ofFloat x)
        toDouble m k

    let lnDouble (x: float) : float =
        let l = ln x
        if l.Sign < 0 then -(toDouble (-l) 0) else toDouble l 0

    let ln10: BigInteger = BigInteger 3 * ln2 + lnMantissa (ofMantissa 1.25)

    let log10Double (x: float) : float =
        let l = (ln x <<< p) / ln10
        if l.Sign < 0 then -(toDouble (-l) 0) else toDouble l 0

    let powDouble (x: float) (y: float) : float =
        let t = (ln x * ofFloat y) >>> p

        if t > ofFloat 710.0 then
            Double.PositiveInfinity
        elif t < ofFloat -746.0 then
            0.0
        else
            let struct (m, k) = exp t
            toDouble m k

/// The correctly rounded candidate: the table, the fast path, the slow path. It counts the slow-path
/// hits in two classes: the fast path WITHDREW (a result outside the normal range, which the
/// prototype's double-double scaling does not cover), and the rounding test could not DECIDE (the
/// result within the error bound of a midpoint — the class a production build would also pay).
module private CorrectlyRounded =
    let mutable withdrew = 0
    let mutable undecided = 0

    let private decide (fast: struct (float * float) voption) (slow: unit -> float) : float =
        match fast with
        | ValueSome(struct (h, l)) ->
            match DD.rounds h l with
            | ValueSome r -> r
            | ValueNone ->
                undecided <- undecided + 1
                slow ()
        | ValueNone ->
            withdrew <- withdrew + 1
            slow ()

    let exp (x: float) : float =
        match Elementary.expTable x with
        | SpecialValue.NaN
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Compute -> decide (DD.exp x 0.0) (fun () -> Big.expDouble x)

    let ln (x: float) : float =
        match Elementary.lnTable x with
        | SpecialValue.NaN
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Compute -> decide (DD.ln x) (fun () -> Big.lnDouble x)

    let log10 (x: float) : float =
        match Elementary.lnTable x with
        | SpecialValue.NaN
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Compute -> decide (DD.log10 x) (fun () -> Big.log10Double x)

    let pow (x: float) (y: float) : float =
        match Elementary.powTable x y with
        | SpecialValue.NaN
        | SpecialValue.Undefined -> Double.NaN
        | SpecialValue.Value v -> v
        | SpecialValue.Compute ->
            if x <= 0.0 || Double.IsInfinity x || Double.IsInfinity y then
                // the prototype measures the computed path; the IEEE edge rules are the package's
                Elementary.pow x y
            else
                decide (DD.pow x y) (fun () -> Big.powDouble x y)

// ---- timing -----------------------------------------------------------------------------------------

/// Nanoseconds per value: `f` over every input, the median of `runs` passes after a warm-up that
/// repeats the pass for at least half a second — a pass over three thousand values takes under a
/// millisecond, and the .NET runtime promotes a method to its optimised tier only after a delay, so a
/// shorter warm-up measures unoptimised code (the first run of this harness did). The checksum keeps
/// the work alive.
let private perValue (runs: int) (n: int) (f: unit -> float) : float * float =
    let mutable check = 0.0
    let start = nowMs ()

    while nowMs () - start < 500.0 do
        check <- f ()

    // A sample spans at least fifty milliseconds (passes are doubled until one does), so the
    // millisecond clock under node contributes a few per cent at most.
    let sample (passes: int) =
        let start = nowMs ()

        for _ in 1..passes do
            check <- f ()

        nowMs () - start

    let rec calibrate passes =
        if passes >= 1_000_000 || sample passes >= 50.0 then
            passes
        else
            calibrate (passes * 2)

    let passes = calibrate 1

    let samples = Array.init runs (fun _ -> sample passes / float passes)

    Array.sortInPlace samples
    samples[runs / 2] * 1e6 / float n, check

// The checksum is a float sum, not a sum of bit patterns: a 64-bit integer under node is a BigInt,
// and the conversion alone cost more than the function being timed (the second run of this harness).
let private passUnary (xs: float[]) (f: float -> float) () : float =
    let mutable c = 0.0

    for x in xs do
        c <- c + f x

    c

let private passBinary (xys: (float * float)[]) (f: float -> float -> float) () : float =
    let mutable c = 0.0

    for x, y in xys do
        c <- c + f x y

    c

let run (runs: int) : unit =
    let d = Draw 593
    let xs = expSample d
    let ls = lnSample d
    let ps = powSample d
    printfn ""
    printfn "### Elementary functions (Phase 593): ns per value, faithful-pinned against correctly rounded"
    printfn ""
    printfn "| function | sample | pinned ns | correctly rounded ns | fast path withdrew | rounding undecided |"
    printfn "|---|---:|---:|---:|---:|---:|"

    let row name n (pinned: unit -> float) (cr: unit -> float) =
        let pinnedNs, _ = perValue runs n pinned
        let crNs, _ = perValue runs n cr
        CorrectlyRounded.withdrew <- 0
        CorrectlyRounded.undecided <- 0
        cr () |> ignore

        printfn
            "| %s | %d | %.1f | %.1f | %d | %d |"
            name
            n
            pinnedNs
            crNs
            CorrectlyRounded.withdrew
            CorrectlyRounded.undecided

    row "exp" xs.Length (passUnary xs Elementary.exp) (passUnary xs CorrectlyRounded.exp)
    row "ln" ls.Length (passUnary ls Elementary.ln) (passUnary ls CorrectlyRounded.ln)
    row "log10" ls.Length (passUnary ls Elementary.log10) (passUnary ls CorrectlyRounded.log10)
    row "pow" ps.Length (passBinary ps Elementary.pow) (passBinary ps CorrectlyRounded.pow)

    // The slow path alone, forced, over the first values of each sample.
    let forced = 200
    let slowExp, _ = perValue 3 forced (passUnary xs[0 .. forced - 1] Big.expDouble)
    let slowLn, _ = perValue 3 forced (passUnary ls[0 .. forced - 1] Big.lnDouble)
    let slowLog10, _ = perValue 3 forced (passUnary ls[0 .. forced - 1] Big.log10Double)

    let slowPow, _ =
        perValue 3 forced (passBinary (ps[0 .. forced - 1] |> Array.filter (fun (x, _) -> x > 0.0)) Big.powDouble)

    printfn ""
    printfn "| slow path, forced | exp | ln | log10 | pow |"
    printfn "|---|---:|---:|---:|---:|"
    printfn "| ns per value | %.0f | %.0f | %.0f | %.0f |" slowExp slowLn slowLog10 slowPow
    printfn ""
    printfn "BEGIN elementary-samples"

    for x in xs do
        printfn "exp %s %s %s" (hex x) (hex (Elementary.exp x)) (hex (CorrectlyRounded.exp x))

    for x in ls do
        printfn "ln %s %s %s" (hex x) (hex (Elementary.ln x)) (hex (CorrectlyRounded.ln x))
        printfn "log10 %s %s %s" (hex x) (hex (Elementary.log10 x)) (hex (CorrectlyRounded.log10 x))

    for x, y in ps do
        printfn "pow %s %s %s %s" (hex x) (hex y) (hex (Elementary.pow x y)) (hex (CorrectlyRounded.pow x y))

    printfn "END elementary-samples"
