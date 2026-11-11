/// Phase 618 — the measurement behind DECISIONS.md D79: the pinned distribution functions of
/// `Fuaran.Compute.Distributions`, on this host.
///
/// For each function the harness prints nanoseconds per value over a pinned seeded sample (the same
/// draw on every host: Park and Miller's generator and the pinned `exp`, `ln` and `pow`), the
/// chi-square quantile's iteration counts under each candidate start, then the sample itself with the
/// package's answers as IEEE-754 bit patterns between `BEGIN distributions-samples` and `END` lines,
/// one `<op> <operands> <answer>` a line. Those lines are what
/// `tests/Fuaran.Core.Compute.Tests/DistributionsReference/distributions_reference.py check` reads to
/// state each function's error against the fifty-digit reference, and two hosts' lines are compared
/// byte for byte. A line whose op is `<function>:<label>` is scored under its own heading: a
/// candidate the decision was made against (`direct`, `split`, `near-one`, `two-inverses`), computed
/// here over the same inputs, and, after a `@`, the range of the parameters where they are large
/// (`nu-to-4e4`, `nu-to-1e6`, `k-to-2e5`, `d-to-4e4`). `node Program.js <runs> distributions`, or the
/// same program on .NET.
module Fuaran.Core.Compute.Benchmarks.DistributionsBench

open Fuaran.Compute.Solvers
open Fuaran.Compute.Elementary
open Fuaran.Compute.Distributions
open Fuaran.Core.Compute.Benchmarks.ElementaryBench
open Fuaran.Core.Compute.Benchmarks.SpecialBench

/// A probability: uniform six draws in ten, within twelve decades of zero or of one otherwise.
let private probability (d: Draw) : float =
    let r = d.Unit()

    let p =
        if r < 0.6 then d.Unit()
        elif r < 0.8 then Elementary.pow 10.0 (d.Range -12.0 0.0)
        else 1.0 - Elementary.pow 10.0 (d.Range -12.0 0.0)

    if p > 0.0 && p < 1.0 then p else 0.5

let private sign (d: Draw) : float = if d.Unit() < 0.5 then -1.0 else 1.0

/// `(ν, x)`: `ν` log-uniform over `[lo, hi)`; `x` uniform over `(−9, 9)` seven draws in ten, and of a
/// magnitude log-uniform over `[1e-8, 1e8]` otherwise.
let private tPairs (d: Draw) (n: int) (lo: float) (hi: float) : float[][] =
    draws n (fun () ->
        let nu = logUniform d lo hi

        let x =
            if d.Unit() < 0.7 then
                d.Range -9.0 9.0
            else
                sign d * logUniform d 1e-8 1e8

        [| nu; x |])

/// `(k, x)`: `k` log-uniform over `[lo, hi)`; `x` within seven standard deviations of `k` seven draws
/// in ten, across four and a half decades round it otherwise.
let private chiPairs (d: Draw) (n: int) (lo: float) (hi: float) : float[][] =
    draws n (fun () ->
        let k = logUniform d lo hi

        let x =
            if d.Unit() < 0.7 then
                let near = k + d.Range -7.0 7.0 * sqrt (2.0 * k)
                let floor = k * Elementary.pow 10.0 (d.Range -6.0 0.0)
                if near > floor then near else floor
            else
                k * Elementary.pow 10.0 (d.Range -3.0 1.5)

        [| k; x |])

/// `(d1, d2, x)`: each parameter log-uniform over its range; `x` log-uniform over `[1e-3, 1e3]` six
/// draws in ten and over `[1e-12, 1e12]` otherwise.
let private fTriples (d: Draw) (n: int) (lo1: float) (hi1: float) (lo2: float) (hi2: float) : float[][] =
    draws n (fun () ->
        let d1 = logUniform d lo1 hi1
        let d2 = logUniform d lo2 hi2

        let x =
            if d.Unit() < 0.6 then
                logUniform d 1e-3 1e3
            else
                logUniform d 1e-12 1e12

        [| d1; d2; x |])

/// The four parameter ranges an F sample is drawn over, as `(count, d1 from, to, d2 from, to)`: both
/// moderate, a small numerator against a large denominator, the reverse, and both large.
let private fRanges: (int * float * float * float * float) list =
    [ 500, 0.1, 1e3, 0.1, 1e3
      500, 0.1, 10.0, 100.0, 4e4
      500, 100.0, 4e4, 0.1, 10.0
      60, 1e3, 4e4, 1e3, 4e4 ]

// ---- the candidates the decision was made against, each over the public special functions ---------

/// `StudentT.cdf` with `z = ν / (ν + x²)` formed directly everywhere.
let private tCdfDirect (nu: float) (x: float) : float =
    let tail = 0.5 * Special.regularizedBeta (0.5 * nu) 0.5 (nu / (nu + x * x))
    if x < 0.0 then tail else 1.0 - tail

/// `StudentT.cdf` split at `2x² = ν`: the centre as `½ ∓ ½ I_w(½, ν/2)`, no correction.
let private tCdfSplit (nu: float) (x: float) : float =
    let x2 = x * x

    if 2.0 * x2 < nu then
        let c = 0.5 * Special.regularizedBeta 0.5 (0.5 * nu) (x2 / (nu + x2))
        if x < 0.0 then 0.5 - c else 0.5 + c
    else
        tCdfDirect nu x

/// `StudentT.cdf` with the tail read near one through the density's first-order correction from
/// `w = 2⁻²⁶` up, and no centre arm above that: what the package does, less its reading of the
/// centre as `½ − ½ I_w(½, ν/2)` up to `t² (ν + 2) = 3ν`.
let private tCdfNearOne (nu: float) (x: float) : float =
    let a = 0.5 * nu
    let x2 = x * x
    let w = x2 / (nu + x2)

    let tail =
        if 2.0 * x2 >= nu then
            0.5 * Special.regularizedBeta a 0.5 (nu / (nu + x2))
        elif w >= 1.4901161193847656e-08 then
            let z = 1.0 - w
            let u = 1.0 - z

            let density =
                Elementary.exp ((a - 1.0) * Elementary.ln z - 0.5 * Elementary.ln u - Special.betaLn a 0.5)

            0.5 * (Special.regularizedBeta a 0.5 z - (w - u) * density)
        else
            0.5 - 0.5 * Special.regularizedBeta 0.5 a w

    if x < 0.0 then tail else 1.0 - tail

/// `StudentT.quantile` from the one root `I_z(ν/2, ½) = 2p`.
let private tQuantileDirect (nu: float) (p: float) : float =
    let lower (p: float) =
        let z = Special.inverseRegularizedBeta (0.5 * nu) 0.5 (2.0 * p)
        -(sqrt (nu * (1.0 - z) / z))

    if p < 0.5 then lower p
    elif p > 0.5 then -(lower (1.0 - p))
    else 0.0

/// `StudentT.quantile` from two inverses and no Newton step: the root of `I_z(ν/2, ½) = 2p` while it
/// is at most one half, the root of `I_w(½, ν/2) = 1 − 2p` otherwise.
let private tQuantileTwoInverses (nu: float) (p: float) : float =
    let lower (p: float) =
        let z = Special.inverseRegularizedBeta (0.5 * nu) 0.5 (2.0 * p)

        if not (z > 0.5) then
            -(sqrt (nu * (1.0 - z) / z))
        else
            let w = Special.inverseRegularizedBeta 0.5 (0.5 * nu) (1.0 - 2.0 * p)
            -(sqrt (nu * w / (1.0 - w)))

    if p < 0.5 then lower p
    elif p > 0.5 then -(lower (1.0 - p))
    else 0.0

/// `FisherF.cdf` with `w = d1 x / (d1 x + d2)` formed directly everywhere.
let private fCdfDirect (d1: float) (d2: float) (x: float) : float =
    let n = d1 * x
    Special.regularizedBeta (0.5 * d1) (0.5 * d2) (n / (n + d2))

/// `FisherF.quantile` from the one root `I_w(d1/2, d2/2) = p`.
let private fQuantileDirect (d1: float) (d2: float) (p: float) : float =
    let w = Special.inverseRegularizedBeta (0.5 * d1) (0.5 * d2) p
    d2 * w / (d1 * (1.0 - w))

/// How one chi-square solve ended: the quantile is below the smallest double and no solve was run;
/// the solver's answer; or its refusal.
type private ChiRun =
    | Underflows
    | Solved of Stopped<float>
    | Refused

/// The chi-square quantile's solve, transcribed from `ChiSquare.quantile` so its iteration count can
/// be read: `startAtZero` and `wilsonHilferty` choose which of the two starts a candidate may use
/// (the package uses both), with half the bracket where neither applies.
let private chiSolve (startAtZero: bool) (wilsonHilferty: bool) (k: float) (p: float) : ChiRun =
    let rule =
        StoppingRule.create 100 (StoppingTest.RelativeChange(9.094947017729282e-13, 2.2250738585072014e-308))

    let a = 0.5 * k
    let q = 1.0 - p
    let l = if p <= 0.5 then 0.6931471805599453 else -(Elementary.ln q)
    let hi = a + sqrt (2.0 * a * l) + l
    let lnP = Elementary.ln p
    let small = a < -0.62 * lnP

    let residual (u: float) : float * float =
        if u = 0.0 then
            -p, 0.0
        else
            (if p <= 0.5 then
                 Special.regularizedGammaP a u - p
             else
                 q - Special.regularizedGammaQ a u),
            Elementary.exp ((a - 1.0) * Elementary.ln u - u - Special.gammaLn a)

    let atZero = Elementary.exp ((lnP + Special.gammaLn (a + 1.0)) / a)

    let start =
        if small && startAtZero then
            atZero
        elif wilsonHilferty then
            let c = 1.0 / (9.0 * a)
            let s = 1.0 - c + Elementary.normalQuantile p * sqrt c
            a * s * s * s
        else
            0.5 * hi

    if small && atZero = 0.0 then
        Underflows
    else
        let u0 = if start > 0.0 && start < hi then start else 0.5 * hi

        let solved: Result<Stopped<float>, SolverError<float, unit>> =
            Root.bracketedNewton rule 1.0 residual 0.0 hi u0

        match solved with
        | Ok at -> Solved at
        | Error _ -> Refused

let run (runs: int) : unit =
    let d = Draw 618

    let tInputs = Array.concat [ tPairs d 1200 0.05 100.0; tPairs d 800 100.0 1e6 ]

    let tProbabilities =
        Array.concat
            [ draws 1200 (fun () -> [| logUniform d 0.05 100.0; probability d |])
              draws 800 (fun () -> [| logUniform d 100.0 1e6; probability d |]) ]

    let chiInputs =
        Array.concat
            [ chiPairs d 600 1e-3 2.0
              chiPairs d 600 2.0 100.0
              chiPairs d 400 100.0 2e3
              chiPairs d 60 2e3 2e5 ]

    let chiProbabilities =
        Array.concat
            [ draws 600 (fun () -> [| logUniform d 1e-3 2.0; probability d |])
              draws 600 (fun () -> [| logUniform d 2.0 100.0; probability d |])
              draws 400 (fun () -> [| logUniform d 100.0 2e3; probability d |])
              draws 60 (fun () -> [| logUniform d 2e3 2e5; probability d |]) ]

    let fInputs =
        fRanges
        |> List.map (fun (n, lo1, hi1, lo2, hi2) -> fTriples d n lo1 hi1 lo2 hi2)
        |> Array.concat

    let fProbabilities =
        fRanges
        |> List.map (fun (n, lo1, hi1, lo2, hi2) ->
            draws n (fun () -> [| logUniform d lo1 hi1; logUniform d lo2 hi2; probability d |]))
        |> Array.concat

    let shipped =
        [ { Op = "Normal.cdf"
            Inputs = draws 600 (fun () -> [| d.Range -38.0 9.0 |])
            Apply = fun a -> Normal.cdf a[0] }
          { Op = "Normal.quantile"
            Inputs = draws 600 (fun () -> [| probability d |])
            Apply = fun a -> Normal.quantile a[0] }
          { Op = "StudentT.cdf"
            Inputs = tInputs
            Apply = fun a -> StudentT.cdf a[0] a[1] }
          { Op = "StudentT.quantile"
            Inputs = tProbabilities
            Apply = fun a -> StudentT.quantile a[0] a[1] }
          { Op = "ChiSquare.cdf"
            Inputs = chiInputs
            Apply = fun a -> ChiSquare.cdf a[0] a[1] }
          { Op = "ChiSquare.quantile"
            Inputs = chiProbabilities
            Apply = fun a -> ChiSquare.quantile a[0] a[1] }
          { Op = "FisherF.cdf"
            Inputs = fInputs
            Apply = fun a -> FisherF.cdf a[0] a[1] a[2] }
          { Op = "FisherF.quantile"
            Inputs = fProbabilities
            Apply = fun a -> FisherF.quantile a[0] a[1] a[2] } ]

    let candidates =
        [ { Op = "StudentT.cdf:direct"
            Inputs = tInputs
            Apply = fun a -> tCdfDirect a[0] a[1] }
          { Op = "StudentT.cdf:split"
            Inputs = tInputs
            Apply = fun a -> tCdfSplit a[0] a[1] }
          { Op = "StudentT.cdf:near-one"
            Inputs = tInputs
            Apply = fun a -> tCdfNearOne a[0] a[1] }
          { Op = "StudentT.quantile:direct"
            Inputs = tProbabilities
            Apply = fun a -> tQuantileDirect a[0] a[1] }
          { Op = "StudentT.quantile:two-inverses"
            Inputs = tProbabilities
            Apply = fun a -> tQuantileTwoInverses a[0] a[1] }
          { Op = "FisherF.cdf:direct"
            Inputs = fInputs
            Apply = fun a -> fCdfDirect a[0] a[1] a[2] }
          { Op = "FisherF.quantile:direct"
            Inputs = fProbabilities
            Apply = fun a -> fQuantileDirect a[0] a[1] a[2] } ]

    printfn ""
    printfn "### Distributions (Phase 618): ns per value over the pinned sample"
    printfn ""
    printfn "| function | sample | ns per value |"
    printfn "|---|---:|---:|"

    for s in shipped @ candidates do
        let ns, _ = perValue runs s.Inputs.Length (pass s.Inputs s.Apply)
        printfn "| %s | %d | %.1f |" s.Op s.Inputs.Length ns

    printfn ""

    printfn
        "| the chi-square quantile's start | solved | refused | below the smallest double | mean iterations | most |"

    printfn "|---|---:|---:|---:|---:|---:|"

    for name, startAtZero, wilsonHilferty in
        [ "the leading term at zero, else Wilson and Hilferty (shipped)", true, true
          "Wilson and Hilferty alone", false, true
          "half the bracket", false, false ] do
        let runsOf =
            chiProbabilities
            |> Array.map (fun a -> chiSolve startAtZero wilsonHilferty a[0] a[1])

        let solved =
            runsOf
            |> Array.choose (fun r ->
                match r with
                | Solved s -> Some s
                | _ -> None)

        let count (r: ChiRun) =
            runsOf |> Array.filter (fun x -> x = r) |> Array.length

        let total = solved |> Array.sumBy (fun s -> s.Iterations)

        let most =
            solved |> Array.fold (fun m s -> if s.Iterations > m then s.Iterations else m) 0

        printfn
            "| %s | %d | %d | %d | %.2f | %d |"
            name
            solved.Length
            (count Refused)
            (count Underflows)
            (float total / float solved.Length)
            most

    printfn ""
    printfn "BEGIN distributions-samples"

    for s in shipped @ candidates do
        for a in s.Inputs do
            // Large parameters are scored apart, under the shipped functions and the candidates alike:
            // up to the special functions' own measured range (D78), and beyond it.
            let range =
                if s.Op.StartsWith "StudentT." then
                    if a[0] >= 4e4 then "@nu-to-1e6"
                    elif a[0] >= 100.0 then "@nu-to-4e4"
                    else ""
                elif s.Op.StartsWith "ChiSquare." then
                    if a[0] >= 2e3 then "@k-to-2e5" else ""
                elif s.Op.StartsWith "FisherF." then
                    if a[0] >= 1e3 || a[1] >= 1e3 then "@d-to-4e4" else ""
                else
                    ""

            printfn "%s%s %s %s" s.Op range (a |> Array.map hex |> String.concat " ") (hex (s.Apply a))

    printfn "END distributions-samples"
