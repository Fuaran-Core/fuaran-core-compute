/// Phase 631 — the measurement behind DECISIONS.md D78: the pinned special functions of
/// `Fuaran.Compute.Elementary`'s `Special`, on this host.
///
/// For each function the harness prints nanoseconds per value over a pinned seeded sample (the same
/// draw on every host: Park and Miller's generator and the pinned `exp` and `ln`), then a few single
/// inputs at each function's longest iteration, then the sample itself with the package's answers as
/// IEEE-754 bit patterns between `BEGIN special-samples` and `END` lines, one `<op> <operands> <answer>`
/// a line. Those lines are what `tests/Fuaran.Core.Compute.Tests/SpecialReference/special_reference.py
/// check` reads to state each function's error against the fifty-digit reference, and two hosts' lines
/// are compared byte for byte. A negative argument of `gammaLn`, `gamma` or `digamma` is printed under
/// `<op>:reflection`, which the checker scores under its own heading. `node Program.js <runs> special`, or the same program on .NET.
module Fuaran.Core.Compute.Benchmarks.SpecialBench

open Fuaran.Compute.Elementary
open Fuaran.Core.Compute.Benchmarks.ElementaryBench

/// One function's sample: its name, the operands of each input, and the function over them.
type Sample =
    { Op: string
      Inputs: float[][]
      Apply: float[] -> float }

let logUniform (d: Draw) (lo: float) (hi: float) : float =
    let l = Elementary.ln lo
    Elementary.exp (l + d.Unit() * (Elementary.ln hi - l))

/// `n` draws of `f`.
let draws (n: int) (f: unit -> float[]) : float[][] = Array.init n (fun _ -> f ())

/// The digamma function's positive root, as the nearest double.
let private digammaRoot = 1.4616321449683622

let private gammaLnSample (d: Draw) : float[][] =
    Array.concat
        [ draws 1500 (fun () -> [| d.Range 0.0 3.5 |])
          // Either side of the zeros at one and two, down to the last place.
          [| for k in 1..3..52 do
                 yield [| 1.0 + twoTo -k |]
                 yield [| 1.0 - 0.5 * twoTo -k |]
                 yield [| 2.0 + 2.0 * twoTo -k |]
                 yield [| 2.0 - twoTo -k |] |]
          draws 200 (fun () -> [| logUniform d 1e-300 1e-3 |])
          draws 1000 (fun () -> [| d.Range 2.5 30.0 |])
          draws 500 (fun () -> [| logUniform d 30.0 1e300 |])
          draws 1000 (fun () -> [| -(d.Range 0.0 30.0) |])
          draws 200 (fun () -> [| -(logUniform d 30.0 1e6) |]) ]

let private gammaSample (d: Draw) : float[][] =
    Array.concat
        [ draws 2000 (fun () -> [| d.Range 0.0 171.6 |])
          draws 300 (fun () -> [| logUniform d 1e-300 1.0 |])
          draws 1000 (fun () -> [| -(d.Range 0.0 170.0) |])
          [| for k in -20 .. 170 -> [| float k + 0.5 |] |]
          [| for k in 24..171 -> [| float k |] |] ]

let private betaSample (d: Draw) : float[][] =
    Array.concat
        [ draws 1500 (fun () -> [| logUniform d 1e-6 1e6; logUniform d 1e-6 1e6 |])
          draws 1500 (fun () -> [| logUniform d 1e-3 85.0; logUniform d 1e-3 85.0 |])
          [| for i in 1..2..39 do
                 for j in 1..3..39 do
                     yield [| float i; float j |] |] ]

/// `(a, x)` with `a` log-uniform over `[lo, hi)` and `x` within `spread` standard deviations of `a`
/// seven draws in ten, across four and a half decades round it otherwise.
let private gammaPairs (d: Draw) (n: int) (lo: float) (hi: float) (spread: float) : float[][] =
    draws n (fun () ->
        let a = logUniform d lo hi

        let x =
            if d.Unit() < 0.7 then
                let near = a + d.Range -spread spread * sqrt a
                let floor = a * Elementary.pow 10.0 (d.Range -6.0 0.0)
                if near > floor then near else floor
            else
                a * Elementary.pow 10.0 (d.Range -3.0 1.5)

        [| a; x |])

let private regularizedGammaSample (d: Draw) : float[][] =
    Array.concat
        [ gammaPairs d 600 1e-10 1.0 6.0
          gammaPairs d 600 1.0 10.0 6.0
          gammaPairs d 500 10.0 1e3 7.0
          gammaPairs d 60 1e3 3e5 7.0 ]

/// `(a, b, x)` with both parameters log-uniform over `[lo, hi)` and `x` within six standard deviations
/// of the mean six draws in ten, uniform two in ten, and within twelve decades of an end otherwise.
let betaTriples (d: Draw) (n: int) (lo: float) (hi: float) : float[][] =
    draws n (fun () ->
        let a = logUniform d lo hi
        let b = logUniform d lo hi
        let mean = a / (a + b)
        let sd = sqrt (a * b / ((a + b) * (a + b) * (a + b + 1.0)))
        let r = d.Unit()

        let x =
            if r < 0.6 then
                mean + d.Range -6.0 6.0 * sd
            elif r < 0.8 then
                d.Unit()
            elif d.Unit() < 0.5 then
                Elementary.pow 10.0 (d.Range -12.0 0.0)
            else
                1.0 - Elementary.pow 10.0 (d.Range -12.0 0.0)

        [| a; b; (if x > 0.0 && x < 1.0 then x else 0.5) |])

let private regularizedBetaSample (d: Draw) : float[][] =
    Array.concat
        [ betaTriples d 500 1e-3 1.0
          betaTriples d 500 1e-2 10.0
          betaTriples d 400 10.0 1e3
          betaTriples d 60 1e3 2e4
          draws 200 (fun () ->
              let a = logUniform d 1e-2 5.0
              let b = logUniform d 50.0 1e4
              let x = a / (a + b) * Elementary.pow 10.0 (d.Range -2.0 1.0)
              [| a; b; (if x < 0.999 then x else 0.999) |]) ]

let inverseSample (d: Draw) : float[][] =
    draws 1200 (fun () ->
        let a = logUniform d 0.05 1e3
        let b = logUniform d 0.05 1e3
        let r = d.Unit()

        let p =
            if r < 0.6 then d.Unit()
            elif r < 0.8 then Elementary.pow 10.0 (d.Range -12.0 0.0)
            else 1.0 - Elementary.pow 10.0 (d.Range -12.0 0.0)

        [| a; b; (if p > 0.0 && p < 1.0 then p else 0.5) |])

let private digammaSample (d: Draw) : float[][] =
    Array.concat
        [ draws 1500 (fun () -> [| d.Range 0.0 2.2 |])
          // Either side of the root, down to the last place.
          [| for k in 2..2..52 do
                 yield [| digammaRoot + twoTo -k |]
                 yield [| digammaRoot - twoTo -k |] |]
          draws 100 (fun () -> [| logUniform d 1e-300 1e-2 |])
          draws 1500 (fun () -> [| d.Range 2.0 40.0 |])
          draws 400 (fun () -> [| logUniform d 40.0 1e300 |])
          draws 1000 (fun () -> [| -(d.Range 0.0 40.0) |]) ]

/// `(n, x)` for the order `n`: half the draws log-uniform over six decades, half uniform up to twice
/// the order's shift threshold.
let private orderSample (d: Draw) (n: int) (count: int) : float[][] =
    draws count (fun () ->
        let x =
            if d.Unit() < 0.5 then
                logUniform d 1e-3 1e3
            else
                d.Range 0.0 (2.0 * (10.0 + float n))

        [| float n; x |])

let private polygammaSample (d: Draw) : float[][] =
    Array.concat
        [ for n in 2..8 do
              yield orderSample d n 400
          for n in [ 12; 20 ] do
              yield orderSample d n 200 ]

let pass (inputs: float[][]) (f: float[] -> float) () : float =
    let mutable c = 0.0

    for x in inputs do
        let v = f x

        // A NaN or an infinity would poison the checksum; the checksum only keeps the work alive.
        if v - v = 0.0 then
            c <- c + v

    c

let run (runs: int) : unit =
    let d = Draw 631
    let betas = betaSample d
    let gammas = regularizedGammaSample d

    let samples =
        [ { Op = "gammaLn"
            Inputs = gammaLnSample d
            Apply = fun a -> Special.gammaLn a[0] }
          { Op = "gamma"
            Inputs = gammaSample d
            Apply = fun a -> Special.gamma a[0] }
          { Op = "betaLn"
            Inputs = betas
            Apply = fun a -> Special.betaLn a[0] a[1] }
          { Op = "beta"
            Inputs = betas
            Apply = fun a -> Special.beta a[0] a[1] }
          { Op = "regularizedGammaP"
            Inputs = gammas
            Apply = fun a -> Special.regularizedGammaP a[0] a[1] }
          { Op = "regularizedGammaQ"
            Inputs = gammas
            Apply = fun a -> Special.regularizedGammaQ a[0] a[1] }
          { Op = "regularizedBeta"
            Inputs = regularizedBetaSample d
            Apply = fun a -> Special.regularizedBeta a[0] a[1] a[2] }
          { Op = "inverseRegularizedBeta"
            Inputs = inverseSample d
            Apply = fun a -> Special.inverseRegularizedBeta a[0] a[1] a[2] }
          { Op = "digamma"
            Inputs = digammaSample d
            Apply = fun a -> Special.digamma a[0] }
          { Op = "trigamma"
            Inputs = orderSample d 1 800 |> Array.map (fun a -> [| a[1] |])
            Apply = fun a -> Special.trigamma a[0] }
          { Op = "polygamma"
            Inputs = polygammaSample d
            Apply = fun a -> Special.polygamma a[0] a[1] } ]

    printfn ""
    printfn "### Special functions (Phase 631): ns per value over the pinned sample"
    printfn ""
    printfn "| function | sample | ns per value |"
    printfn "|---|---:|---:|"

    for s in samples do
        let ns, _ = perValue runs s.Inputs.Length (pass s.Inputs s.Apply)
        printfn "| %s | %d | %.1f |" s.Op s.Inputs.Length ns

    // One input each at the longest iteration a function runs inside the measured range.
    let single (name: string) (f: unit -> float) =
        let ns, _ = perValue runs 1 f
        printfn "| %s | %.0f |" name ns

    printfn ""
    printfn "| one input at a long iteration | ns |"
    printfn "|---|---:|"
    single "regularizedGammaP 1e4 1e4 (the series, some 800 terms)" (fun () -> Special.regularizedGammaP 1e4 1e4)
    single "regularizedGammaP 1e6 1e6 (the series, some 7600 terms)" (fun () -> Special.regularizedGammaP 1e6 1e6)

    single "regularizedGammaQ 1e6 1000001 (the fraction, some 900 iterations)" (fun () ->
        Special.regularizedGammaQ 1e6 1000001.0)

    single "regularizedBeta 1e4 1e4 0.5 (the fraction)" (fun () -> Special.regularizedBeta 1e4 1e4 0.5)
    single "regularizedBeta 1e6 1e6 0.5 (the fraction)" (fun () -> Special.regularizedBeta 1e6 1e6 0.5)

    single "regularizedBeta 5 1e-4 0.99 (Gauss's series, some 3600 terms)" (fun () ->
        Special.regularizedBeta 5.0 1e-4 0.99)

    single "inverseRegularizedBeta 0.05 30 1e-11" (fun () -> Special.inverseRegularizedBeta 0.05 30.0 1e-11)
    single "polygamma 20 0.5 (thirty shifts)" (fun () -> Special.polygamma 20.0 0.5)
    printfn ""
    printfn "BEGIN special-samples"

    for s in samples do
        for a in s.Inputs do
            // The reflections are scored apart: each function has zeros on the negative axis, where a
            // distance in units in the last place of the answer is unbounded.
            let label =
                if (s.Op = "gammaLn" || s.Op = "gamma" || s.Op = "digamma") && a[0] < 0.0 then
                    s.Op + ":reflection"
                else
                    s.Op

            printfn "%s %s %s" label (a |> Array.map hex |> String.concat " ") (hex (s.Apply a))

    printfn "END special-samples"
