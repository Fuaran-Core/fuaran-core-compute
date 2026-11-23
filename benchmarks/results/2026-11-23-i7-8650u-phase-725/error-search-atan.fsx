// Phase 725 (DECISIONS.md D84 item 9): a search for the largest error of the pinned `atan` and `atan2`.
//
// A SCREEN, not a reference. It scores the package's answers against the arc tangent in double-double
// arithmetic, which is good to about 1e-28 here and quick enough to ask hundreds of millions of inputs,
// and prints the inputs it finds worst in the form the exact reference scores:
//
//   dotnet build src/Fuaran.Compute.Elementary -c Release
//   dotnet fsi benchmarks/results/2026-11-23-i7-8650u-phase-725/error-search-atan.fsx <mode> <count> > found.txt
//   python tests/Fuaran.Core.Compute.Tests/SpecialReference/trig_reference.py check < found.txt
//
// <mode> is `atan`, `atan2` (x positive), `atan2-left` (x negative) or `atan2-focus` (x positive, the
// quotient in [11/16, 0.72], where the error is largest). The first three ask <count> quotients in each
// sixteenth from 1/4 to 4, drawn by a seeded generator; the last asks <count> in its one band. The table
// goes to the error stream and the worst inputs to the output. A figure this program prints is the
// screen's; a figure the results file states is the exact reference's over the inputs printed here.
//
// The reference: atan (y / x) = atan c + atan ((y - c x) / (x + c y)) with c the sixteenth nearest the
// quotient, the products exact (a fused multiply-add gives each product's rounding error; this program
// is a measuring tool and is not held to the package's rule against one), atan c built by the same
// formula from one sixteenth to the next, and the small arc tangent by its series to the 27th power.

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

/// `1 / (2k + 1)` for `k` from 0 to 13.
let oddReciprocals =
    [| for k in 0..13 -> ddDiv (ofFloat 1.0) (ofFloat (float (2 * k + 1))) |]

/// `atan u` for a double-double `u` of at most 1/16 in magnitude.
let atanSmall (u: struct (float * float)) =
    let u2 = ddMul u u
    let mutable acc = oddReciprocals[13]

    for k in 12..-1..0 do
        acc <- ddAdd oddReciprocals[k] (ddNeg (ddMul u2 acc))

    ddMul u acc

/// `atan (j / 16)` for `j` from 0 to 64: each from the one before, by the addition formula.
let atanSixteenths =
    let table = Array.create 65 (ofFloat 0.0)

    for j in 1..64 do
        // atan (j/16) - atan ((j-1)/16) = atan (16 / (256 + j (j - 1)))
        let step = ddDiv (ofFloat 16.0) (ofFloat (256.0 + float (j * (j - 1))))
        table[j] <- ddAdd table[j - 1] (atanSmall step)

    table

let pi = struct (3.141592653589793, 1.2246467991473532e-16)

/// `atan (y / x)` for positive `y` and `x` whose quotient lies in `[1/4, 4]`.
let truth (y: float) (x: float) =
    let j = int (Math.Round(16.0 * (y / x)))
    let c = float j / 16.0
    let struct (cx, cxe) = twoProd c x
    let struct (cy, cye) = twoProd c y

    let u =
        ddDiv (ddAdd (ofFloat y) (struct (-cx, -cxe))) (ddAdd (ofFloat x) (struct (cy, cye)))

    ddAdd atanSixteenths[j] (atanSmall u)

let unitOf (value: float) =
    if value >= 2.0 then 4.440892098500626e-16
    elif value >= 1.0 then 2.220446049250313e-16
    elif value >= 0.5 then 1.1102230246251565e-16
    else 5.551115123125783e-17

let mode = fsi.CommandLineArgs[1]
let count = int64 fsi.CommandLineArgs[2]

/// The inputs worth the exact reference: every one more than a unit away, and each band's worst.
let found = Collections.Generic.List<float * string>()

let search (label: string) (seed: int) (lo: float) (hi: float) =
    let rng = Random seed
    let mutable worst = 0.0
    let mutable worstLine = ""
    let mutable over = 0L
    let mutable i = 0L

    while i < count do
        let q = lo + rng.NextDouble() * (hi - lo)
        let x = if mode = "atan" then 1.0 else 1.0 + rng.NextDouble()
        let y = q * x

        let answer =
            if mode = "atan" then Elementary.atan y
            elif mode = "atan2-left" then Elementary.atan2 y -x
            else Elementary.atan2 y x

        let struct (th, tl) =
            if mode = "atan2-left" then
                ddAdd pi (ddNeg (truth y x))
            else
                truth y x

        let error = abs (((answer - th) - tl) / unitOf th)

        if error > worst || error > 1.0 then
            let line =
                if mode = "atan" then
                    sprintf "atan %s %s" (hex y) (hex answer)
                else
                    sprintf "atan2 %s %s %s" (hex y) (hex (if mode = "atan2-left" then -x else x)) (hex answer)

            if error > 1.0 then
                over <- over + 1L

                if found.Count < 2000 then
                    found.Add((error, line))

            if error > worst then
                worst <- error
                worstLine <- line

        i <- i + 1L

    found.Add((worst, worstLine))
    eprintfn "| %s | %s | %d | %.3f | %d |" mode label count worst over

eprintfn "| function | quotient | screened | largest error (screen) | more than one unit away |"
eprintfn "|---|---|---:|---:|---:|"

if mode = "atan2-focus" then
    search "11/16 to 0.72" 20261123 0.6875 0.72
else
    let index =
        match mode with
        | "atan" -> 0
        | "atan2" -> 1
        | "atan2-left" -> 2
        | _ -> failwith "mode: atan, atan2, atan2-left or atan2-focus"

    for k in 4..63 do
        search (sprintf "%d/16 to %d/16" k (k + 1)) (725000 + 1000 * index + k) (float k / 16.0) (float (k + 1) / 16.0)

for _, line in found |> Seq.distinctBy snd |> Seq.sortByDescending fst |> Seq.truncate 400 do
    printfn "%s" line
