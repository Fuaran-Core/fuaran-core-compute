// Phase 725 (DECISIONS.md D84 item 9): a search for the largest error of the pinned `sin` and `cos`.
//
// A SCREEN, not a reference. It scores the package's answers against the sine and cosine in
// double-double arithmetic, which is good to about 1e-28 here and quick enough to ask hundreds of
// millions of arguments, and prints the arguments it finds worst in the form the exact reference scores:
//
//   dotnet build src/Fuaran.Compute.Elementary -c Release
//   dotnet fsi benchmarks/results/2026-11-23-i7-8650u-phase-725/error-search-circular.fsx <count> > found.txt
//   python tests/Fuaran.Core.Compute.Tests/SpecialReference/trig_reference.py check < found.txt
//
// It asks <count> arguments within 0.09 of the first two thousand odd multiples of pi/4, drawn by a
// seeded generator: there the argument less its nearest multiple of pi/2 is as large as it gets, which is
// where both kernels are least accurate, and the first of those multiples is pi/4 itself, where nothing
// is reduced. The summary goes to the error stream and the worst arguments to the output. A figure this
// program prints is the screen's; a figure the results file states is the exact reference's over the
// arguments printed here.
//
// The reference: the argument less its nearest multiple of pi/2, with pi/2 in three doubles and each
// product exact (a fused multiply-add gives each product's rounding error; this program is a measuring
// tool and is not held to the package's rule against one), then the two Taylor series to the thirtieth
// power, each coefficient a double-double.

#r "../../../src/Fuaran.Compute.Elementary/bin/Release/net10.0/Fuaran.Compute.Elementary.dll"

open System
open Fuaran.Compute.Elementary

let hex (x: float) =
    (BitConverter.DoubleToInt64Bits x).ToString "x16"

let inline twoSum (a: float) (b: float) =
    let s = a + b
    let bb = s - a
    struct (s, (a - (s - bb)) + (b - bb))

let inline quick (a: float) (b: float) =
    let s = a + b
    struct (s, b - (s - a))

let inline twoProd (a: float) (b: float) =
    let p = a * b
    struct (p, Math.FusedMultiplyAdd(a, b, -p))

let inline ddAdd (struct (ah: float, al: float)) (struct (bh: float, bl: float)) =
    let struct (s, e) = twoSum ah bh
    let struct (t, f) = twoSum al bl
    let struct (s2, e2) = quick s (e + t)
    quick s2 (e2 + f)

let inline ddNeg (struct (h: float, l: float)) = struct (-h, -l)

let inline ddMul (struct (ah: float, al: float)) (struct (bh: float, bl: float)) =
    let struct (p, e) = twoProd ah bh
    quick p (e + (ah * bl + al * bh))

let inline ddDiv (struct (ah: float, al: float)) (struct (bh: float, bl: float)) =
    let q1 = ah / bh
    let struct (p, e) = twoProd q1 bh
    let r = ((ah - p) - e) + al - q1 * bl
    let q2 = r / bh
    let struct (p2, e2) = twoProd q2 bh
    let r2 = ((r - p2) - e2) - q2 * bl
    let struct (s, t) = quick q1 q2
    quick s (t + r2 / bh)

let inline ofFloat (x: float) = struct (x, 0.0)

/// `π/2` in three doubles: the nearest, the nearest to what it leaves, and again.
let halfPi1, halfPi2, halfPi3 =
    1.5707963267948966, 6.123233995736766e-17, -1.4973849048591698e-33

/// `1 / n!` for `n` from 0 to 31.
let reciprocalFactorials =
    let table = Array.create 32 (ofFloat 1.0)

    for n in 1..31 do
        table[n] <- ddDiv table[n - 1] (ofFloat (float n))

    table

/// `(sin x, cos x)` for a positive `x` below `2²⁰`.
let truth (x: float) =
    let multiple = Math.Round(x * 0.6366197723675814)
    let struct (a, ae) = twoProd multiple halfPi1
    let struct (b, be) = twoProd multiple halfPi2

    let r =
        ddAdd (ddAdd (ddAdd (ofFloat x) (struct (-a, -ae))) (struct (-b, -be))) (ofFloat (-(multiple * halfPi3)))

    let r2 = ddMul r r
    let mutable s = reciprocalFactorials[29]

    for k in 13..-1..0 do
        s <- ddAdd reciprocalFactorials[2 * k + 1] (ddNeg (ddMul r2 s))

    let s = ddMul r s
    let mutable c = reciprocalFactorials[30]

    for k in 14..-1..0 do
        c <- ddAdd reciprocalFactorials[2 * k] (ddNeg (ddMul r2 c))

    match int (multiple % 4.0) with
    | 0 -> struct (s, c)
    | 1 -> struct (c, ddNeg s)
    | 2 -> struct (ddNeg s, ddNeg c)
    | _ -> struct (ddNeg c, s)

let count = int64 fsi.CommandLineArgs[1]
let rng = Random 20261123

/// Every answer here lies in `[1/2, 1)`, so one unit in the last place is `2⁻⁵³`.
let oneUnit = 1.1102230246251565e-16
let found = Collections.Generic.List<float * string>()
let mutable worstSin = 0.0
let mutable worstCos = 0.0
let mutable overSin = 0L
let mutable overCos = 0L
let mutable i = 0L

while i < count do
    let k = rng.Next(0, 2000)
    let x = (float k + 0.5) * halfPi1 + (rng.NextDouble() - 0.5) * 0.18

    if x > 0.0 then
        let struct (struct (sh, sl), struct (ch, cl)) = truth x
        let s = Elementary.sin x
        let c = Elementary.cos x
        let sinError = abs (((s - sh) - sl) / oneUnit)
        let cosError = abs (((c - ch) - cl) / oneUnit)

        if sinError > 1.0 then
            overSin <- overSin + 1L

        if cosError > 1.0 then
            overCos <- overCos + 1L

        if sinError > worstSin || (sinError > 1.0 && found.Count < 2000) then
            worstSin <- max worstSin sinError
            found.Add((sinError, sprintf "sin %s %s" (hex x) (hex s)))

        if cosError > worstCos || (cosError > 1.0 && found.Count < 2000) then
            worstCos <- max worstCos cosError
            found.Add((cosError, sprintf "cos %s %s" (hex x) (hex c)))

    i <- i + 1L

eprintfn "| function | screened | largest error (screen) | more than one unit away |"
eprintfn "|---|---:|---:|---:|"
eprintfn "| sin | %d | %.3f | %d |" count worstSin overSin
eprintfn "| cos | %d | %.3f | %d |" count worstCos overCos

for _, line in found |> Seq.sortByDescending fst |> Seq.truncate 60 do
    printfn "%s" line
