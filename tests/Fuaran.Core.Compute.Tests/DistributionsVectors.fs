namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 618 — the distribution law vectors: authored hard inputs, each answer
//  computed by calling Fuaran.Compute.Distributions, rendered through the
//  substrate's one corpus writer (`CorpusFile`) into
//  `conformance/laws/distributions-laws.json` (DECISIONS.md D79).
//
//  The inputs are the Cauchy distribution (`ν = 1`), the normal limit (`ν` to a
//  million), tail probabilities down to `1e-10` and below, the chi-square with
//  one degree of freedom near zero, the F with one numerator degree of freedom,
//  probabilities at the last place from zero, one half and one, both sides of
//  every boundary between a function's arms, and every row of the special-value
//  table. A vector the routine computed to a finite, non-zero answer carries
//  `reference`, the true value rounded once to the nearest double, taken at
//  fifty digits by DistributionsReference/distributions_reference.py, and
//  `ulps`, the package's distance from it.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.Distributions

module DistributionsVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "distributions-laws.json"

    type Case = ElementaryVectors.Case

    let private scalar (c: Case) (name: string) : float =
        match c.Input |> List.tryFind (fun (n, _) -> n = name) with
        | Some(_, x) -> x
        | None -> failwithf "distribution vector %s has no operand %s" c.Id name

    /// What the package answers for a case.
    let answer (c: Case) : float =
        let v = scalar c

        match c.Op with
        | "Normal.cdf" -> Normal.cdf (v "x")
        | "Normal.quantile" -> Normal.quantile (v "p")
        | "StudentT.cdf" -> StudentT.cdf (v "nu") (v "x")
        | "StudentT.quantile" -> StudentT.quantile (v "nu") (v "p")
        | "ChiSquare.cdf" -> ChiSquare.cdf (v "k") (v "x")
        | "ChiSquare.quantile" -> ChiSquare.quantile (v "k") (v "p")
        | "FisherF.cdf" -> FisherF.cdf (v "d1") (v "d2") (v "x")
        | "FisherF.quantile" -> FisherF.quantile (v "d1") (v "d2") (v "p")
        | other -> failwithf "distribution vector %s: %s is not a distribution function" c.Id other

    /// Whether the op is a quantile: its reference is the root nearest the package's answer.
    let isQuantile (op: string) : bool = op.EndsWith ".quantile"

    let private tiny = Double.Epsilon
    let private inf = Double.PositiveInfinity
    let private nan = Double.NaN

    /// `2ᵏ`, by exact doublings or halvings.
    let private pow2 (k: int) : float =
        let mutable p = 1.0

        for _ in 1 .. abs k do
            p <- if k > 0 then p * 2.0 else p * 0.5

        p

    let private case (id: string) (op: string) (input: (string * float) list) : Case =
        { Id = id; Op = op; Input = input }

    let private phi (id: string) (x: float) : Case = case id "Normal.cdf" [ "x", x ]
    let private probit (id: string) (p: float) : Case = case id "Normal.quantile" [ "p", p ]

    let private tCdf (id: string) (nu: float) (x: float) : Case =
        case id "StudentT.cdf" [ "nu", nu; "x", x ]

    let private tQuantile (id: string) (nu: float) (p: float) : Case =
        case id "StudentT.quantile" [ "nu", nu; "p", p ]

    let private chiCdf (id: string) (k: float) (x: float) : Case =
        case id "ChiSquare.cdf" [ "k", k; "x", x ]

    let private chiQuantile (id: string) (k: float) (p: float) : Case =
        case id "ChiSquare.quantile" [ "k", k; "p", p ]

    let private fCdf (id: string) (d1: float) (d2: float) (x: float) : Case =
        case id "FisherF.cdf" [ "d1", d1; "d2", d2; "x", x ]

    let private fQuantile (id: string) (d1: float) (d2: float) (p: float) : Case =
        case id "FisherF.quantile" [ "d1", d1; "d2", d2; "p", p ]

    /// Every authored case, in file order.
    let cases: Case list =
        [ // ---- the normal distribution: Elementary's functions under the distribution's name ----
          phi "normal-cdf-zero" 0.0
          phi "normal-cdf-lower-2.5-percent" -1.959963984540054
          phi "normal-cdf-one" 1.0
          phi "normal-cdf-tail-1e-10" -6.5
          phi "normal-cdf-deep-tail" -38.0
          phi "normal-cdf-upper" 8.25
          phi "normal-cdf-below-range" -40.5
          phi "normal-cdf-above-range" 40.5
          phi "normal-cdf-negative-infinity" -inf
          phi "normal-cdf-infinity" inf
          phi "normal-cdf-nan" nan
          probit "normal-quantile-half" 0.5
          probit "normal-quantile-2.5-percent" 0.025
          probit "normal-quantile-97.5-percent" 0.975
          probit "normal-quantile-1e-10" 1e-10
          probit "normal-quantile-upper-1e-10" (1.0 - 1e-10)
          probit "normal-quantile-1e-300" 1e-300
          probit "normal-quantile-zero" 0.0
          probit "normal-quantile-one" 1.0
          probit "normal-quantile-below-zero" -0.125
          probit "normal-quantile-above-one" 1.125
          probit "normal-quantile-nan" nan

          // ---- StudentT.cdf: the Cauchy, the arms' boundaries, the normal limit, deep tails, the table ----
          tCdf "t-cdf-cauchy-minus-one" 1.0 -1.0
          tCdf "t-cdf-cauchy-one" 1.0 1.0
          tCdf "t-cdf-cauchy-near-zero" 1.0 -0.001
          tCdf "t-cdf-cauchy-1e-8" 1.0 1e-8
          tCdf "t-cdf-cauchy-far-tail" 1.0 -1e8
          tCdf "t-cdf-cauchy-square-overflows" 1.0 -1e160
          tCdf "t-cdf-cauchy-square-overflows-upper" 1.0 1e200
          tCdf "t-cdf-cauchy-far-arm-from" 1.0 -1073741824.0
          tCdf "t-cdf-cauchy-below-far-arm" 1.0 -1073741823.9999999
          tCdf "t-cdf-three-far" 3.0 -1e60
          tCdf "t-cdf-thirty-far" 30.0 -1e10
          tCdf "t-cdf-zero" 3.0 0.0
          tCdf "t-cdf-negative-zero" 3.0 -0.0
          tCdf "t-cdf-half-degree" 0.5 -2.0
          tCdf "t-cdf-two-degrees" 2.0 -1.4142135623730951
          tCdf "t-cdf-ten-lower" 10.0 -2.2281388519862748
          tCdf "t-cdf-ten-upper" 10.0 2.2281388519862748
          tCdf "t-cdf-thirty" 30.0 2.5
          tCdf "t-cdf-arm-boundary" 8.0 -2.0
          tCdf "t-cdf-below-arm-boundary" 8.0 -(2.0 - pow2 -51)
          tCdf "t-cdf-near-one-arm-from" 4.0 -(pow2 -12)
          tCdf "t-cdf-below-near-one-arm" 4.0 -(pow2 -12 - pow2 -40)
          tCdf "t-cdf-centre-tiny" 4.0 -1e-5
          tCdf "t-cdf-deep-tail" 5.0 -1e5
          tCdf "t-cdf-hundred-deep-tail" 100.0 -40.0
          tCdf "t-cdf-thousand-2.5-percent" 1000.0 -1.96
          tCdf "t-cdf-thousand-three" 1000.0 -3.0
          tCdf "t-cdf-thousand-tail-1e-10" 1000.0 -6.5
          tCdf "t-cdf-ten-thousand-2.5-percent" 1e4 -1.96
          tCdf "t-cdf-ten-thousand-tail-1e-10" 1e4 -6.5
          tCdf "t-cdf-forty-thousand-three" 4e4 -3.0
          tCdf "t-cdf-forty-thousand-tail-1e-10" 4e4 -6.5
          tCdf "t-cdf-million-three" 1e6 -3.0
          tCdf "t-cdf-million-tail-1e-10" 1e6 -6.5
          tCdf "t-cdf-million-centre" 1e6 -0.01
          tCdf "t-cdf-million-upper" 1e6 1.96
          tCdf "t-cdf-negative-infinity" 3.0 -inf
          tCdf "t-cdf-infinity" 3.0 inf
          tCdf "t-cdf-nan" 3.0 nan
          tCdf "t-cdf-nu-zero" 0.0 1.0
          tCdf "t-cdf-nu-negative" -2.0 1.0
          tCdf "t-cdf-nu-infinite" inf 1.0
          tCdf "t-cdf-nu-nan" nan 1.0

          // ---- StudentT.quantile ----
          tQuantile "t-quantile-cauchy-upper-quartile" 1.0 0.75
          tQuantile "t-quantile-cauchy-lower-quartile" 1.0 0.25
          tQuantile "t-quantile-cauchy-below-quartile" 1.0 (0.25 - pow2 -54)
          tQuantile "t-quantile-cauchy-97.5-percent" 1.0 0.975
          tQuantile "t-quantile-cauchy-99.5-percent" 1.0 0.995
          tQuantile "t-quantile-cauchy-1e-10" 1.0 1e-10
          tQuantile "t-quantile-cauchy-upper-1e-10" 1.0 (1.0 - 1e-10)
          tQuantile "t-quantile-cauchy-just-below-half" 1.0 (0.5 - pow2 -54)
          tQuantile "t-quantile-cauchy-near-half" 1.0 (0.5 - pow2 -30)
          tQuantile "t-quantile-cauchy-far" 1.0 1e-200
          tQuantile "t-quantile-cauchy-far-upper" 1.0 (1.0 - 1e-16)
          tQuantile "t-quantile-three-far" 3.0 1e-300
          tQuantile "t-quantile-half-degree-overflows" 0.5 1e-300
          tQuantile "t-quantile-half" 3.0 0.5
          tQuantile "t-quantile-two-90-percent" 2.0 0.9
          tQuantile "t-quantile-six-97.5-percent" 6.0 0.975
          tQuantile "t-quantile-ten-97.5-percent" 10.0 0.975
          tQuantile "t-quantile-ten-2.5-percent" 10.0 0.025
          tQuantile "t-quantile-thirty-95-percent" 30.0 0.95
          tQuantile "t-quantile-thirty-five-97.5-percent" 35.0 0.975
          tQuantile "t-quantile-tenth-degree" 0.1 0.3
          tQuantile "t-quantile-twentieth-degree-tail" 0.05 1e-6
          tQuantile "t-quantile-sixteenth-boundary" 15.0 0.0001
          tQuantile "t-quantile-hundred-twenty-99.5-percent" 120.0 0.995
          tQuantile "t-quantile-thousand-2.5-percent" 1000.0 0.025
          tQuantile "t-quantile-thousand-99.95-percent" 1000.0 0.9995
          tQuantile "t-quantile-thousand-1e-10" 1000.0 1e-10
          tQuantile "t-quantile-thousand-40-percent" 1000.0 0.4
          tQuantile "t-quantile-ten-thousand-2.5-percent" 1e4 0.025
          tQuantile "t-quantile-ten-thousand-1e-10" 1e4 1e-10
          tQuantile "t-quantile-forty-thousand-0.1-percent" 4e4 0.001
          tQuantile "t-quantile-forty-thousand-near-half" 4e4 0.49999
          tQuantile "t-quantile-million-2.5-percent" 1e6 0.025
          tQuantile "t-quantile-million-1e-10" 1e6 1e-10
          tQuantile "t-quantile-million-upper-1e-10" 1e6 (1.0 - 1e-10)
          tQuantile "t-quantile-zero" 3.0 0.0
          tQuantile "t-quantile-one" 3.0 1.0
          tQuantile "t-quantile-below-zero" 3.0 -0.125
          tQuantile "t-quantile-above-one" 3.0 1.125
          tQuantile "t-quantile-nan" 3.0 nan
          tQuantile "t-quantile-nu-zero" 0.0 0.25
          tQuantile "t-quantile-nu-negative" -1.0 0.25
          tQuantile "t-quantile-nu-infinite" inf 0.25
          tQuantile "t-quantile-nu-nan" nan 0.25

          // ---- ChiSquare.cdf: one degree of freedom near zero, the median, large k, the table ----
          chiCdf "chi-cdf-one-smallest" 1.0 tiny
          chiCdf "chi-cdf-one-1e-300" 1.0 1e-300
          chiCdf "chi-cdf-one-1e-10" 1.0 1e-10
          chiCdf "chi-cdf-one-thousandth" 1.0 0.001
          chiCdf "chi-cdf-one-one" 1.0 1.0
          chiCdf "chi-cdf-one-95-percent" 1.0 3.841458820694124
          chiCdf "chi-cdf-one-thirty" 1.0 30.0
          chiCdf "chi-cdf-one-thousand" 1.0 1000.0
          chiCdf "chi-cdf-two-median" 2.0 1.3862943611198906
          chiCdf "chi-cdf-hundredth-tiny" 0.01 1e-100
          chiCdf "chi-cdf-hundred-mean" 100.0 100.0
          chiCdf "chi-cdf-hundred-lower" 100.0 50.0
          chiCdf "chi-cdf-hundred-upper" 100.0 200.0
          chiCdf "chi-cdf-ten-thousand-mean" 1e4 1e4
          chiCdf "chi-cdf-ten-thousand-lower" 1e4 9500.0
          chiCdf "chi-cdf-hundred-thousand-mean" 1e5 1e5
          chiCdf "chi-cdf-zero" 3.0 0.0
          chiCdf "chi-cdf-negative" 3.0 -1.0
          chiCdf "chi-cdf-negative-infinity" 3.0 -inf
          chiCdf "chi-cdf-infinity" 3.0 inf
          chiCdf "chi-cdf-nan" 3.0 nan
          chiCdf "chi-cdf-k-zero" 0.0 1.0
          chiCdf "chi-cdf-k-negative" -1.0 1.0
          chiCdf "chi-cdf-k-infinite" inf 1.0
          chiCdf "chi-cdf-k-nan" nan 1.0

          // ---- ChiSquare.quantile ----
          chiQuantile "chi-quantile-one-1e-300" 1.0 1e-300
          chiQuantile "chi-quantile-one-1e-10" 1.0 1e-10
          chiQuantile "chi-quantile-one-5-percent" 1.0 0.05
          chiQuantile "chi-quantile-one-median" 1.0 0.5
          chiQuantile "chi-quantile-one-95-percent" 1.0 0.95
          chiQuantile "chi-quantile-one-upper-1e-10" 1.0 (1.0 - 1e-10)
          chiQuantile "chi-quantile-one-last-place-below-one" 1.0 (1.0 - pow2 -53)
          chiQuantile "chi-quantile-two-median" 2.0 0.5
          chiQuantile "chi-quantile-two-just-above-half" 2.0 (0.5 + pow2 -53)
          chiQuantile "chi-quantile-hundredth-median" 0.01 0.5
          chiQuantile "chi-quantile-hundredth-1-percent" 0.01 0.01
          chiQuantile "chi-quantile-hundredth-upper" 0.01 0.999999
          chiQuantile "chi-quantile-thousandth-underflows" 0.001 0.3
          chiQuantile "chi-quantile-three-99-percent" 3.0 0.99
          chiQuantile "chi-quantile-hundred-2.5-percent" 100.0 0.025
          chiQuantile "chi-quantile-hundred-97.5-percent" 100.0 0.975
          chiQuantile "chi-quantile-hundred-1e-10" 100.0 1e-10
          chiQuantile "chi-quantile-ten-thousand-median" 1e4 0.5
          chiQuantile "chi-quantile-ten-thousand-1e-10" 1e4 1e-10
          chiQuantile "chi-quantile-ten-thousand-upper-1e-10" 1e4 (1.0 - 1e-10)
          chiQuantile "chi-quantile-hundred-thousand-median" 1e5 0.5
          chiQuantile "chi-quantile-zero" 3.0 0.0
          chiQuantile "chi-quantile-one" 3.0 1.0
          chiQuantile "chi-quantile-below-zero" 3.0 -0.125
          chiQuantile "chi-quantile-above-one" 3.0 1.125
          chiQuantile "chi-quantile-nan" 3.0 nan
          chiQuantile "chi-quantile-k-zero" 0.0 0.25
          chiQuantile "chi-quantile-k-negative" -1.0 0.25
          chiQuantile "chi-quantile-k-infinite" inf 0.25
          chiQuantile "chi-quantile-k-nan" nan 0.25

          // ---- FisherF.cdf: one numerator degree of freedom, the arms, large parameters, the table ----
          fCdf "f-cdf-one-ten-t-squared" 1.0 10.0 4.964602743730711
          fCdf "f-cdf-one-one-median" 1.0 1.0 1.0
          fCdf "f-cdf-one-thousand-near-zero" 1.0 1000.0 1e-10
          fCdf "f-cdf-one-five-far-tail" 1.0 5.0 1e10
          fCdf "f-cdf-two-two-median" 2.0 2.0 1.0
          fCdf "f-cdf-five-seven" 5.0 7.0 1.3
          fCdf "f-cdf-ten-one-lower" 10.0 1.0 0.01
          fCdf "f-cdf-arm-boundary" 4.0 8.0 2.0
          fCdf "f-cdf-above-arm-boundary" 4.0 8.0 (2.0 + pow2 -51)
          fCdf "f-cdf-hundred-two" 100.0 2.0 0.5
          fCdf "f-cdf-thousand-two-small" 1000.0 2.0 0.042
          fCdf "f-cdf-three-ten-thousand" 3.0 1e4 2.6
          fCdf "f-cdf-ten-thousand-both-median" 1e4 1e4 1.0
          fCdf "f-cdf-ten-thousand-both-upper" 1e4 1e4 1.05
          fCdf "f-cdf-tenth-both-near-zero" 0.1 0.1 1e-12
          fCdf "f-cdf-tenth-both-far" 0.1 0.1 1e12
          fCdf "f-cdf-product-overflows" 1e200 3.0 1e200
          fCdf "f-cdf-zero" 3.0 4.0 0.0
          fCdf "f-cdf-negative" 3.0 4.0 -1.0
          fCdf "f-cdf-infinity" 3.0 4.0 inf
          fCdf "f-cdf-nan" 3.0 4.0 nan
          fCdf "f-cdf-d1-zero" 0.0 4.0 1.0
          fCdf "f-cdf-d2-negative" 3.0 -4.0 1.0
          fCdf "f-cdf-d1-infinite" inf 4.0 1.0
          fCdf "f-cdf-d2-nan" 3.0 nan 1.0

          // ---- FisherF.quantile ----
          fQuantile "f-quantile-one-ten-95-percent" 1.0 10.0 0.95
          fQuantile "f-quantile-one-one-median" 1.0 1.0 0.5
          fQuantile "f-quantile-two-two-median" 2.0 2.0 0.5
          fQuantile "f-quantile-five-seven-95-percent" 5.0 7.0 0.95
          fQuantile "f-quantile-five-seven-5-percent" 5.0 7.0 0.05
          fQuantile "f-quantile-ten-one-1-percent" 10.0 1.0 0.01
          fQuantile "f-quantile-one-thousand-1e-10" 1.0 1000.0 1e-10
          fQuantile "f-quantile-one-five-upper-1e-10" 1.0 5.0 (1.0 - 1e-10)
          fQuantile "f-quantile-hundred-two-40-percent" 100.0 2.0 0.4
          fQuantile "f-quantile-thousand-one-1e-10" 1000.0 1.0 1e-10
          fQuantile "f-quantile-two-tenth-30-percent" 2.0 0.1 0.3
          fQuantile "f-quantile-three-ten-thousand-95-percent" 3.0 1e4 0.95
          fQuantile "f-quantile-ten-thousand-both-median" 1e4 1e4 0.5
          fQuantile "f-quantile-ten-thousand-both-97.5-percent" 1e4 1e4 0.975
          fQuantile "f-quantile-tenth-both-median" 0.1 0.1 0.5
          fQuantile "f-quantile-tenth-both-1e-10" 0.1 0.1 1e-10
          fQuantile "f-quantile-just-above-half" 5.0 7.0 (0.5 + pow2 -53)
          fQuantile "f-quantile-zero" 3.0 4.0 0.0
          fQuantile "f-quantile-one" 3.0 4.0 1.0
          fQuantile "f-quantile-below-zero" 3.0 4.0 -0.125
          fQuantile "f-quantile-above-one" 3.0 4.0 1.125
          fQuantile "f-quantile-nan" 3.0 4.0 nan
          fQuantile "f-quantile-d1-zero" 0.0 4.0 0.25
          fQuantile "f-quantile-d2-negative" 3.0 -4.0 0.25
          fQuantile "f-quantile-d1-infinite" inf 4.0 0.25
          fQuantile "f-quantile-d2-nan" 3.0 nan 0.25

          // ---- Phase 636 (DECISIONS.md D81): a degree of freedom of a million ----
          fCdf "f-cdf-three-million" 3.0 1e6 2.6
          fCdf "f-cdf-million-three-upper" 1e6 3.0 5.0
          fQuantile "f-quantile-three-million-95-percent" 3.0 1e6 0.95
          fQuantile "f-quantile-million-three-5-percent" 1e6 3.0 0.05 ]

    /// Whether a case has a parameter (a degrees-of-freedom operand) above forty thousand: beyond the
    /// range the special functions' own accuracy was measured over (an incomplete beta parameter of
    /// twenty thousand, DECISIONS.md D78 item 10), where their error grows with the parameter.
    let beyond (c: Case) : bool =
        c.Input |> List.exists (fun (name, x) -> name <> "x" && name <> "p" && x > 4e4)

    /// The most units in the last place a function's answer is from its reference on any vector here
    /// that carries one, as `(function, the bound with every parameter at most forty thousand, the
    /// bound beyond)` (DECISIONS.md D79 item 9, re-measured by D81). The figures are of these authored
    /// inputs, each a power of two at or above the worst vector (`0`: the function has no such
    /// vector). Since Phase 636 the t's and the F's no longer grow with the degrees of freedom: their
    /// largest are far tails and a quantile where the mass is at zero. The drawn sample's figures, by
    /// range, are in the decisions.
    let referenceUlps: (string * int * int) list =
        [ "Normal.cdf", 1, 0
          "Normal.quantile", 2, 0
          "StudentT.cdf", 64, 8
          "StudentT.quantile", 128, 1
          "ChiSquare.cdf", 16, 32
          "ChiSquare.quantile", 16, 1
          "FisherF.cdf", 16, 2
          "FisherF.quantile", 256, 4 ]

    /// The bound a case's answer is held to.
    let boundOf (c: Case) : int =
        let _, within, past = referenceUlps |> List.find (fun (op, _, _) -> op = c.Op)
        if beyond c then past else within

    /// The true value for every vector the routine computes to a finite, non-zero answer, rounded once
    /// to the nearest double: mpmath 1.4.1 at fifty digits, by
    /// `DistributionsReference/distributions_reference.py references` over `referenceCases ()`. A
    /// vector the special-value table answers, and one whose answer is zero, one or infinite, carries
    /// none.
    let references: (string * string) list =
        [ "normal-cdf-zero", "3fe0000000000000"
          "normal-cdf-lower-2.5-percent", "3f9999999999999d"
          "normal-cdf-one", "3feaec4bd120d37d"
          "normal-cdf-tail-1e-10", "3dc61404b2da0191"
          "normal-cdf-deep-tail", "00000000037b23b8"
          "normal-cdf-upper", "3fefffffffffffff"
          "normal-quantile-2.5-percent", "bfff5c0331eeff85"
          "normal-quantile-97.5-percent", "3fff5c0331eeff83"
          "normal-quantile-1e-10", "c0197203597a2155"
          "normal-quantile-upper-1e-10", "40197203589fd4f6"
          "normal-quantile-1e-300", "c04286074064c26e"
          "t-cdf-cauchy-minus-one", "3fd0000000000000"
          "t-cdf-cauchy-one", "3fe8000000000000"
          "t-cdf-cauchy-near-zero", "3fdffac8e979e443"
          "t-cdf-cauchy-1e-8", "3fe0000001b57b56"
          "t-cdf-cauchy-far-tail", "3e2b57b55b2347b1"
          "t-cdf-cauchy-square-overflows", "1e9ca416ebf67610"
          "t-cdf-cauchy-far-arm-from", "3df45f306dc9c883"
          "t-cdf-cauchy-below-far-arm", "3df45f306dc9c883"
          "t-cdf-three-far", "1a924d4ebcaae950"
          "t-cdf-thirty-far", "0602d0530fe900ce"
          "t-cdf-zero", "3fe0000000000000"
          "t-cdf-negative-zero", "3fe0000000000000"
          "t-cdf-half-degree", "3fcc8350e2cdecd5"
          "t-cdf-two-degrees", "3fc2bec333018866"
          "t-cdf-ten-lower", "3f99999999999999"
          "t-cdf-ten-upper", "3fef333333333333"
          "t-cdf-thirty", "3fefb5cc5d7b4f38"
          "t-cdf-arm-boundary", "3fa49cb650d2ca26"
          "t-cdf-below-arm-boundary", "3fa49cb650d2ca2a"
          "t-cdf-near-one-arm-from", "3fdffe8000005000"
          "t-cdf-below-near-one-arm", "3fdffe8000006800"
          "t-cdf-centre-tiny", "3fdffff04577d957"
          "t-cdf-deep-tail", "3af25b4d9e854b00"
          "t-cdf-hundred-deep-tail", "32e034a4a26027db"
          "t-cdf-thousand-2.5-percent", "3f99bd682a805bbf"
          "t-cdf-thousand-three", "3f56aa359b9a0369"
          "t-cdf-thousand-tail-1e-10", "3dd164547121644e"
          "t-cdf-ten-thousand-2.5-percent", "3f999caeceda6079"
          "t-cdf-ten-thousand-tail-1e-10", "3dc7219106095c21"
          "t-cdf-forty-thousand-three", "3f56215e5ea7241d"
          "t-cdf-forty-thousand-tail-1e-10", "3dc6565f28bca2b5"
          "t-cdf-million-three", "3f561e05aa361588"
          "t-cdf-million-tail-1e-10", "3dc616a8cf280740"
          "t-cdf-million-centre", "3fdfbea36e58584f"
          "t-cdf-million-upper", "3fef333752ccaebd"
          "t-quantile-cauchy-upper-quartile", "3ff0000000000000"
          "t-quantile-cauchy-lower-quartile", "bff0000000000000"
          "t-quantile-cauchy-below-quartile", "bff0000000000002"
          "t-quantile-cauchy-97.5-percent", "40296993aacc4d1e"
          "t-quantile-cauchy-99.5-percent", "404fd410182c3c30"
          "t-quantile-cauchy-1e-10", "c1e7b7477dbad022"
          "t-quantile-cauchy-upper-1e-10", "41e7b7475ccef2e0"
          "t-quantile-cauchy-just-below-half", "bca921fb54442d18"
          "t-quantile-cauchy-near-half", "be2921fb54442d18"
          "t-quantile-cauchy-far", "e95a9d3a760747a9"
          "t-quantile-cauchy-far-upper", "43245f306dc9c883"
          "t-quantile-three-far", "d4b2e4b0c9627b32"
          "t-quantile-two-90-percent", "3ffe2b7dddfefa67"
          "t-quantile-six-97.5-percent", "400393468546e653"
          "t-quantile-ten-97.5-percent", "4001d33a7661d303"
          "t-quantile-ten-2.5-percent", "c001d33a7661d304"
          "t-quantile-thirty-95-percent", "3ffb27fb080b3758"
          "t-quantile-thirty-five-97.5-percent", "40003da939b97bfb"
          "t-quantile-tenth-degree", "c03b2b8d65a0ab71"
          "t-quantile-twentieth-degree-tail", "d7669cb85dc9598f"
          "t-quantile-sixteenth-boundary", "c013851e457546a1"
          "t-quantile-hundred-twenty-99.5-percent", "4004f07a7f50be2d"
          "t-quantile-thousand-2.5-percent", "bfff65bda9fcfe77"
          "t-quantile-thousand-99.95-percent", "400a66fa96d44bfe"
          "t-quantile-thousand-1e-10", "c019b625334b2e93"
          "t-quantile-thousand-40-percent", "bfd037f184773044"
          "t-quantile-ten-thousand-2.5-percent", "bfff5cfbf9ae8ad1"
          "t-quantile-ten-thousand-1e-10", "c01978c59e1ddbd5"
          "t-quantile-forty-thousand-0.1-percent", "c008b9368c12b1e0"
          "t-quantile-forty-thousand-near-half", "befa48b899a31ecd"
          "t-quantile-million-2.5-percent", "bfff5c05aebc6e01"
          "t-quantile-million-1e-10", "c0197214a311e773"
          "t-quantile-million-upper-1e-10", "40197214a237995f"
          "chi-cdf-one-1e-300", "20c4e4f1043a39ed"
          "chi-cdf-one-1e-10", "3ee0bb9c30b521a0"
          "chi-cdf-one-thousandth", "3f99d5236850253d"
          "chi-cdf-one-one", "3fe5d897a241a6fa"
          "chi-cdf-one-95-percent", "3fee666666666666"
          "chi-cdf-one-thirty", "3fefffffe8ce0034"
          "chi-cdf-two-median", "3fe0000000000000"
          "chi-cdf-hundredth-tiny", "3fd439f795bd00c8"
          "chi-cdf-hundred-mean", "3fe09a13e57b0c82"
          "chi-cdf-hundred-lower", "3edd2a0dfe28a0f3"
          "chi-cdf-hundred-upper", "3feffffff9ac59be"
          "chi-cdf-ten-thousand-mean", "3fe00f67f9b578c0"
          "chi-cdf-ten-thousand-lower", "3f258be26650e845"
          "chi-cdf-hundred-thousand-mean", "3fe004df317b8b4b"
          "chi-quantile-one-1e-10", "3bd28b70caa3e9f9"
          "chi-quantile-one-5-percent", "3f701b25cb41088c"
          "chi-quantile-one-median", "3fdd1dada8c3b2b9"
          "chi-quantile-one-95-percent", "400ebb4ec31e7ef6"
          "chi-quantile-one-upper-1e-10", "4044e9257a137426"
          "chi-quantile-one-last-place-below-one", "405130d91fcdedcb"
          "chi-quantile-two-median", "3ff62e42fefa39ef"
          "chi-quantile-two-just-above-half", "3ff62e42fefa39f1"
          "chi-quantile-hundredth-median", "33720a622df892a2"
          "chi-quantile-hundredth-upper", "402a1ae51d6b4b7a"
          "chi-quantile-three-99-percent", "4026b0925f3ee5a5"
          "chi-quantile-hundred-2.5-percent", "40528e340f4bb867"
          "chi-quantile-hundred-97.5-percent", "406031f553ccf8d0"
          "chi-quantile-hundred-1e-10", "4041332d6e0aeccc"
          "chi-quantile-ten-thousand-median", "40c387aaaaecf3ad"
          "chi-quantile-ten-thousand-1e-10", "40c1d34182bde6f3"
          "chi-quantile-ten-thousand-upper-1e-10", "40c5570d7a2adf39"
          "chi-quantile-hundred-thousand-median", "40f869f55556296f"
          "f-cdf-one-ten-t-squared", "3fee666666666666"
          "f-cdf-one-one-median", "3fe0000000000000"
          "f-cdf-one-thousand-near-zero", "3ee0ba8a12d2ba93"
          "f-cdf-two-two-median", "3fe0000000000000"
          "f-cdf-five-seven", "3fe46896d2b5d281"
          "f-cdf-ten-one-lower", "3ebaab143314d350"
          "f-cdf-arm-boundary", "3fea000000000000"
          "f-cdf-above-arm-boundary", "3fea000000000001"
          "f-cdf-hundred-two", "3fc202def87ed593"
          "f-cdf-thousand-two-small", "3dd5bfd712647504"
          "f-cdf-three-ten-thousand", "3fee633298f477f4"
          "f-cdf-ten-thousand-both-median", "3fe0000000000000"
          "f-cdf-ten-thousand-both-upper", "3fefc3ba8b1c8640"
          "f-cdf-tenth-both-near-zero", "3fc02348535e56e7"
          "f-cdf-tenth-both-far", "3febf72deb286a46"
          "f-quantile-one-ten-95-percent", "4013dbc0d257d234"
          "f-quantile-one-one-median", "3ff0000000000000"
          "f-quantile-two-two-median", "3ff0000000000000"
          "f-quantile-five-seven-95-percent", "400fc5adedf975f5"
          "f-quantile-five-seven-5-percent", "3fca4070791aa2ba"
          "f-quantile-ten-one-1-percent", "3fb97cb3db64970a"
          "f-quantile-one-thousand-1e-10", "3bd28dd09da52ad3"
          "f-quantile-one-five-upper-1e-10", "40dfb16ca3388598"
          "f-quantile-hundred-two-40-percent", "3ff14d5cad0a8aa6"
          "f-quantile-thousand-one-1e-10", "3f97f6e3c39c8bd2"
          "f-quantile-two-tenth-30-percent", "404f4e6d6d8354e0"
          "f-quantile-three-ten-thousand-95-percent", "4004d8ac22ac03d5"
          "f-quantile-ten-thousand-both-median", "3ff0000000000000"
          "f-quantile-ten-thousand-both-97.5-percent", "3ff0a3c21a947230"
          "f-quantile-tenth-both-median", "3ff0000000000000"
          "f-quantile-tenth-both-1e-10", "17a6afc01481df69"
          "f-quantile-just-above-half", "3feeba729d50505f"
          "f-cdf-three-million", "3fee63aec87b221c"
          "f-cdf-million-three-upper", "3fecaf92e312eac4"
          "f-quantile-three-million-95-percent", "4004d6df5756f2ba"
          "f-quantile-million-three-5-percent", "3fd891a4145c4e53" ]

    /// The lines `distributions_reference.py references` reads, one per vector whose answer is finite,
    /// not zero and not exactly one: `<id> <op> <operand bits> …`, and for a quantile the package's own
    /// answer last (its reference is the root nearest that answer).
    let referenceCases () : string list =
        cases
        |> List.choose (fun c ->
            let value = answer c
            let finite = not (Double.IsNaN value) && not (Double.IsInfinity value)

            // A distribution function that answers exactly one has rounded, or a table row answered:
            // nothing a reference would measure in units of the last place.
            let saturated = not (isQuantile c.Op) && value = 1.0

            if finite && value <> 0.0 && not saturated then
                let operands =
                    (c.Input |> List.map (snd >> ElementaryVectors.hex))
                    @ (if isQuantile c.Op then
                           [ ElementaryVectors.hex value ]
                       else
                           [])

                Some(String.concat " " (c.Id :: c.Op :: operands))
            else
                None)

    let private referenceCells (c: Case) (value: float) : (string * CorpusCell) list =
        match references |> List.tryFind (fun (id, _) -> id = c.Id) with
        | None -> []
        | Some(_, h) ->
            [ "reference", CorpusFile.str h
              "ulps", CorpusFile.num (int (ElementaryVectors.ulpsApart value (ElementaryVectors.ofHex h))) ]

    let private caseCell (c: Case) : CorpusCell =
        let value = answer c

        CorpusCell.Row(
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row(c.Input |> List.map (fun (n, x) -> n, CorpusFile.str (ElementaryVectors.hex x)))
              "expected",
              CorpusCell.Row
                  [ "verdict", CorpusFile.str "ok"
                    "value", CorpusFile.str (ElementaryVectors.hex value) ] ]
            @ referenceCells c value
        )

    let private description =
        "The reference answers for Fuaran.Compute.Distributions over AUTHORED hard inputs (Phase 618, "
        + "DECISIONS.md D79): the cumulative distribution function and the quantile of the standard normal "
        + "(`Normal`), Student's t (`StudentT`), the chi-square (`ChiSquare`) and Fisher's F (`FisherF`) "
        + "distributions, at the Cauchy distribution, the normal limit, tail probabilities to `1e-10` and "
        + "below, one degree of freedom near zero, probabilities at the last place from zero, one half and "
        + "one, both sides of every boundary between a function's arms, and every row of the special-value "
        + "table. A float is its IEEE-754 binary64 bit pattern as sixteen lowercase hex digits, or `nan` for "
        + "any NaN (whose pattern is the host's own canonical NaN and is not compared). `op` is "
        + "`<Module>.<function>` and `input` names the operands in the function's argument order (`x`; `p`; "
        + "`nu`, `x`; `nu`, `p`; `k`, `x`; `k`, `p`; `d1`, `d2`, `x`; `d1`, `d2`, `p`). Each vector answers "
        + "`{ verdict: ok, value }`, the function's bits, compared byte for byte. A vector the routine "
        + "computed to a finite answer that is neither zero nor, for a distribution function, exactly one "
        + "also carries `reference`, the true value rounded once to the nearest double (mpmath at fifty "
        + "digits; for a quantile, the root nearest the answer), and `ulps`, the package's distance from it "
        + "in units in the last place, which is itself part of the document a host must reproduce."

    /// The family's document, as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "distributions"
                  "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
