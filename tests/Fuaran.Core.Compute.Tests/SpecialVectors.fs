namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 631 — the special-function law vectors: authored hard inputs, each
//  answer computed by calling Fuaran.Compute.Elementary's `Special`, appended
//  to the elementary family's document (`conformance/laws/elementary-laws.json`)
//  after every vector that was there, which this file leaves as it was
//  (DECISIONS.md D78).
//
//  The inputs are each function's zeros, poles and bounds, the boundaries
//  between its arms, tiny and huge arguments, the reflections, and every row
//  of its special-value table. A vector the routine computed carries
//  `reference`, the true value rounded once to the nearest double, taken at
//  fifty digits by SpecialReference/special_reference.py, and `ulps`, the
//  package's distance from it.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.Elementary

module SpecialVectors =

    type Case = ElementaryVectors.Case

    let private scalar (c: Case) (name: string) : float =
        match c.Input |> List.tryFind (fun (n, _) -> n = name) with
        | Some(_, x) -> x
        | None -> failwithf "special vector %s has no operand %s" c.Id name

    /// What the package answers for a case.
    let answer (c: Case) : float =
        let v = scalar c

        match c.Op with
        | "gammaLn" -> Special.gammaLn (v "x")
        | "gamma" -> Special.gamma (v "x")
        | "betaLn" -> Special.betaLn (v "a") (v "b")
        | "beta" -> Special.beta (v "a") (v "b")
        | "regularizedGammaP" -> Special.regularizedGammaP (v "a") (v "x")
        | "regularizedGammaQ" -> Special.regularizedGammaQ (v "a") (v "x")
        | "regularizedBeta" -> Special.regularizedBeta (v "a") (v "b") (v "x")
        | "inverseRegularizedBeta" -> Special.inverseRegularizedBeta (v "a") (v "b") (v "p")
        | "regularizedBetaComplement" -> Special.regularizedBetaComplement (v "a") (v "b") (v "x")
        | "inverseRegularizedBetaComplement" -> Special.inverseRegularizedBetaComplement (v "a") (v "b") (v "q")
        | "polygamma" -> Special.polygamma (v "n") (v "x")
        | "digamma" -> Special.digamma (v "x")
        | "trigamma" -> Special.trigamma (v "x")
        | other -> failwithf "special vector %s: %s is not a special function" c.Id other

    /// The most units in the last place a function's answer is from its reference on any vector here
    /// that carries one (DECISIONS.md D78 item 10; D81 item 10 for the incomplete beta function, its
    /// complement and their inverses). The figures are of these authored inputs, each a power of two
    /// at or above the worst vector: the incomplete gamma functions' and `beta`'s are set by the
    /// vectors with parameters in the tens of thousands and up, where a long series or a large
    /// exponent carries the error; the incomplete beta function's by a deep tail and by the vector just
    /// below the parameter sum of 128. The drawn samples' figures, by range, are in the decisions.
    let referenceUlps: (string * int) list =
        [ "gammaLn", 2
          "gamma", 4
          "betaLn", 2
          "beta", 256
          "regularizedGammaP", 256
          "regularizedGammaQ", 128
          "regularizedBeta", 64
          "inverseRegularizedBeta", 32
          "regularizedBetaComplement", 64
          "inverseRegularizedBetaComplement", 8
          "polygamma", 8
          "digamma", 2
          "trigamma", 2 ]

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

    let private one (id: string) (op: string) (x: float) : Case = case id op [ "x", x ]

    let private two (id: string) (op: string) (first: string) (a: float) (second: string) (b: float) : Case =
        case id op [ first, a; second, b ]

    let private ab (id: string) (op: string) (a: float) (b: float) : Case = two id op "a" a "b" b

    let private ax (id: string) (op: string) (a: float) (x: float) : Case = two id op "a" a "x" x

    let private abx (id: string) (a: float) (b: float) (x: float) : Case =
        case id "regularizedBeta" [ "a", a; "b", b; "x", x ]

    let private abp (id: string) (a: float) (b: float) (p: float) : Case =
        case id "inverseRegularizedBeta" [ "a", a; "b", b; "p", p ]

    let private nx (id: string) (n: float) (x: float) : Case = two id "polygamma" "n" n "x" x

    let private abc (id: string) (a: float) (b: float) (x: float) : Case =
        case id "regularizedBetaComplement" [ "a", a; "b", b; "x", x ]

    let private abq (id: string) (a: float) (b: float) (q: float) : Case =
        case id "inverseRegularizedBetaComplement" [ "a", a; "b", b; "q", q ]

    /// The digamma function's positive root, as the nearest double.
    let private digammaRoot = 1.4616321449683622

    /// Every authored case, in file order.
    let cases: Case list =
        [ // ---- gammaLn: its zeros at one and two, tiny and huge arguments, the arms, the reflection ----
          one "gammaLn-just-above-one" "gammaLn" (1.0 + pow2 -52)
          one "gammaLn-just-below-one" "gammaLn" (1.0 - pow2 -53)
          one "gammaLn-near-one" "gammaLn" 1.00000001
          one "gammaLn-just-below-two" "gammaLn" (2.0 - pow2 -52)
          one "gammaLn-just-above-two" "gammaLn" (2.0 + pow2 -51)
          one "gammaLn-near-two" "gammaLn" 1.99999999
          one "gammaLn-one" "gammaLn" 1.0
          one "gammaLn-two" "gammaLn" 2.0
          one "gammaLn-smallest-subnormal" "gammaLn" tiny
          one "gammaLn-tiny" "gammaLn" 1.0e-300
          one "gammaLn-small" "gammaLn" 1.0e-17
          one "gammaLn-below-half" "gammaLn" (0.5 - pow2 -54)
          one "gammaLn-half" "gammaLn" 0.5
          one "gammaLn-three-halves" "gammaLn" 1.5
          one "gammaLn-at-kernel-top" "gammaLn" 2.5
          one "gammaLn-above-kernel-top" "gammaLn" (2.5 + pow2 -51)
          one "gammaLn-seven-and-a-quarter" "gammaLn" 7.25
          one "gammaLn-below-stirling" "gammaLn" (10.0 - pow2 -50)
          one "gammaLn-at-stirling" "gammaLn" 10.0
          one "gammaLn-hundred" "gammaLn" 100.0
          one "gammaLn-below-two-to-54" "gammaLn" (pow2 54 - 2.0)
          one "gammaLn-at-two-to-54" "gammaLn" (pow2 54)
          one "gammaLn-huge" "gammaLn" 1.0e300
          one "gammaLn-overflows" "gammaLn" 1.0e306
          one "gammaLn-negative-quarter" "gammaLn" -0.25
          one "gammaLn-negative-tiny" "gammaLn" -1.0e-300
          one "gammaLn-negative-half" "gammaLn" -0.5
          one "gammaLn-negative-three-halves" "gammaLn" -1.5
          one "gammaLn-negative-near-pole" "gammaLn" (-3.0 + pow2 -30)
          one "gammaLn-negative-hundred-and-a-half" "gammaLn" -100.5
          one "gammaLn-negative-large" "gammaLn" -123456.75
          one "gammaLn-zero" "gammaLn" 0.0
          one "gammaLn-negative-integer" "gammaLn" -4.0
          one "gammaLn-infinity" "gammaLn" inf
          one "gammaLn-negative-infinity" "gammaLn" -inf
          one "gammaLn-nan" "gammaLn" nan
          // ---- gamma: the integers, the half-integers, the poles, the overflow point ----
          one "gamma-one" "gamma" 1.0
          one "gamma-two" "gamma" 2.0
          one "gamma-five" "gamma" 5.0
          one "gamma-last-exact-integer" "gamma" 23.0
          one "gamma-first-computed-integer" "gamma" 24.0
          one "gamma-largest-integer" "gamma" 171.0
          one "gamma-integer-overflows" "gamma" 172.0
          one "gamma-half" "gamma" 0.5
          one "gamma-three-halves" "gamma" 1.5
          one "gamma-five-halves" "gamma" 2.5
          one "gamma-ten-and-a-half" "gamma" 10.5
          one "gamma-below-stirling" "gamma" (10.0 - pow2 -50)
          one "gamma-largest-half-integer" "gamma" 170.5
          one "gamma-negative-half" "gamma" -0.5
          one "gamma-negative-three-halves" "gamma" -1.5
          one "gamma-negative-large-half-integer" "gamma" -170.5
          one "gamma-negative-subnormal-result" "gamma" -171.5
          one "gamma-negative-underflows" "gamma" -190.5
          one "gamma-tiny" "gamma" 1.0e-300
          one "gamma-negative-tiny" "gamma" -1.0e-300
          one "gamma-smallest-subnormal" "gamma" tiny
          one "gamma-above-pole-at-minus-one" "gamma" (-1.0 + pow2 -40)
          one "gamma-below-pole-at-minus-two" "gamma" (-2.0 - pow2 -40)
          one "gamma-at-overflow-point" "gamma" 171.62437695630272
          one "gamma-above-overflow-point" "gamma" 171.62437695630275
          one "gamma-zero" "gamma" 0.0
          one "gamma-negative-zero" "gamma" -0.0
          one "gamma-negative-integer" "gamma" -3.0
          one "gamma-infinity" "gamma" inf
          one "gamma-negative-infinity" "gamma" -inf
          one "gamma-nan" "gamma" nan
          // ---- beta and betaLn: one argument tiny, the integers, the arms, the symmetry ----
          ab "beta-one-argument-tiny" "beta" 1.0e-300 1.0
          ab "beta-both-small" "beta" 1.0e-10 1.0e-10
          ab "beta-small-and-moderate" "beta" 1.0e-17 5.5
          ab "beta-halves" "beta" 0.5 0.5
          ab "beta-ones" "beta" 1.0 1.0
          ab "beta-three-four" "beta" 3.0 4.0
          ab "beta-swapped" "beta" 7.25 2.5
          ab "beta-ordered" "beta" 2.5 7.25
          ab "beta-inexact-sum" "beta" 0.1 0.7
          ab "beta-sum-at-ratio-limit" "beta" 120.0 50.0
          ab "beta-sum-above-ratio-limit" "beta" 120.5 50.0
          ab "beta-hundreds" "beta" 100.0 100.0
          ab "beta-large-and-small" "beta" 1.0e5 2.5
          ab "beta-underflows" "beta" 1.0e4 1.0e4
          ab "beta-zero-argument" "beta" 0.0 1.0
          ab "beta-negative-argument" "beta" 2.0 -1.0
          ab "beta-infinite-argument" "beta" inf 1.0
          ab "beta-nan" "beta" nan 1.0
          ab "betaLn-one-argument-tiny" "betaLn" 1.0e-300 1.0
          ab "betaLn-halves" "betaLn" 0.5 0.5
          ab "betaLn-three-four" "betaLn" 3.0 4.0
          ab "betaLn-swapped" "betaLn" 7.25 2.5
          ab "betaLn-ordered" "betaLn" 2.5 7.25
          ab "betaLn-inexact-sum" "betaLn" 0.1 0.7
          ab "betaLn-both-at-stirling" "betaLn" 10.0 10.0
          ab "betaLn-one-below-stirling" "betaLn" 9.99 10.0
          ab "betaLn-both-below-stirling" "betaLn" 9.99 9.99
          ab "betaLn-small-and-large" "betaLn" 0.25 1.0e6
          ab "betaLn-both-large" "betaLn" 1.0e6 2.0e6
          ab "betaLn-huge" "betaLn" 1.0e300 1.0e300
          ab "betaLn-zero-argument" "betaLn" 1.0 0.0
          ab "betaLn-infinite-argument" "betaLn" 1.0 inf
          ab "betaLn-nan" "betaLn" 1.0 nan
          // ---- P and Q: a near zero, a very large, x = a, the boundaries, deep tails ----
          ax "gammaP-a-near-zero" "regularizedGammaP" 1.0e-10 1.0e-5
          ax "gammaQ-a-near-zero" "regularizedGammaQ" 1.0e-10 1.0e-5
          ax "gammaQ-a-near-zero-below-one" "regularizedGammaQ" 1.0e-10 (1.0 - pow2 -53)
          ax "gammaQ-a-near-zero-at-one" "regularizedGammaQ" 1.0e-10 1.0
          ax "gammaQ-a-near-zero-large-x" "regularizedGammaQ" 1.0e-10 5.0
          ax "gammaP-tiny-x" "regularizedGammaP" 0.5 1.0e-300
          ax "gammaQ-tiny-a-tiny-x" "regularizedGammaQ" 1.0e-10 1.0e-300
          ax "gammaP-half-below-boundary" "regularizedGammaP" 0.5 (1.0 - pow2 -53)
          ax "gammaP-half-at-boundary" "regularizedGammaP" 0.5 1.0
          ax "gammaQ-half-below-boundary" "regularizedGammaQ" 0.5 (1.0 - pow2 -53)
          ax "gammaQ-half-at-boundary" "regularizedGammaQ" 0.5 1.0
          ax "gammaP-x-is-a-half" "regularizedGammaP" 0.5 0.5
          ax "gammaP-x-is-a-one" "regularizedGammaP" 1.0 1.0
          ax "gammaQ-x-is-a-one" "regularizedGammaQ" 1.0 1.0
          ax "gammaP-x-is-a-ten" "regularizedGammaP" 10.0 10.0
          ax "gammaQ-x-is-a-ten" "regularizedGammaQ" 10.0 10.0
          ax "gammaP-below-stirling" "regularizedGammaP" (10.0 - pow2 -50) 10.0
          ax "gammaP-x-is-a-hundred" "regularizedGammaP" 100.0 100.0
          ax "gammaQ-x-is-a-hundred" "regularizedGammaQ" 100.0 100.0
          ax "gammaP-three-below-boundary" "regularizedGammaP" 3.0 (4.0 - pow2 -51)
          ax "gammaP-three-at-boundary" "regularizedGammaP" 3.0 4.0
          ax "gammaQ-three-below-boundary" "regularizedGammaQ" 3.0 (4.0 - pow2 -51)
          ax "gammaQ-three-at-boundary" "regularizedGammaQ" 3.0 4.0
          ax "gammaP-large-a-x-is-a" "regularizedGammaP" 1.0e5 1.0e5
          ax "gammaQ-large-a-x-is-a" "regularizedGammaQ" 1.0e5 1.0e5
          ax "gammaQ-large-a-at-boundary" "regularizedGammaQ" 1.0e5 100001.0
          ax "gammaP-very-large-a-below" "regularizedGammaP" 1.0e6 999000.0
          ax "gammaQ-very-large-a-above" "regularizedGammaQ" 1.0e6 1002000.0
          ax "gammaQ-upper-tail" "regularizedGammaQ" 5.0 40.0
          ax "gammaP-lower-tail" "regularizedGammaP" 20.0 2.0
          ax "gammaQ-upper-tail-underflows" "regularizedGammaQ" 2.0 800.0
          ax "gammaP-x-zero" "regularizedGammaP" 2.5 0.0
          ax "gammaQ-x-zero" "regularizedGammaQ" 2.5 0.0
          ax "gammaP-x-infinity" "regularizedGammaP" 2.5 inf
          ax "gammaQ-x-infinity" "regularizedGammaQ" 2.5 inf
          ax "gammaP-a-zero" "regularizedGammaP" 0.0 1.0
          ax "gammaQ-a-negative" "regularizedGammaQ" -1.5 1.0
          ax "gammaP-a-infinity" "regularizedGammaP" inf 1.0
          ax "gammaP-x-negative" "regularizedGammaP" 2.0 -1.0
          ax "gammaQ-nan" "regularizedGammaQ" nan 1.0
          // ---- I_x: both parameters below one, the symmetry switch, the tails, large parameters ----
          abx "beta-inc-halves" 0.5 0.5 0.3
          abx "beta-inc-both-below-one" 0.1 0.2 0.5
          abx "beta-inc-both-tiny" 1.0e-3 1.0e-3 0.5
          abx "beta-inc-at-switch" 2.0 3.0 (3.0 / 7.0)
          abx "beta-inc-above-switch" 2.0 3.0 (3.0 / 7.0 + pow2 -54)
          abx "beta-inc-lower-tail" 5.0 5.0 0.004
          abx "beta-inc-upper-tail" 5.0 5.0 0.996
          abx "beta-inc-skewed-upper" 2.0 50.0 0.9
          abx "beta-inc-skewed-lower" 50.0 2.0 0.6
          abx "beta-inc-small-a-tiny-x" 0.5 30.0 1.0e-12
          abx "beta-inc-tiny-x" 0.5 2.0 1.0e-300
          abx "beta-inc-x-near-one" 3.0 0.5 (1.0 - pow2 -53)
          abx "beta-inc-both-at-stirling" 10.0 10.0 0.5
          abx "beta-inc-one-below-stirling" 9.99 10.0 0.5
          abx "beta-inc-large-at-mean" 500.0 500.0 0.5
          abx "beta-inc-large-below-mean" 500.0 500.0 0.45
          abx "beta-inc-very-large-at-mean" 1.0e4 1.0e4 0.5
          abx "beta-inc-very-large-skewed" 1.0e4 2.0e4 0.33
          abx "beta-inc-small-and-large" 2.5 1.0e4 1.0e-4
          abx "beta-inc-uniform" 1.0 1.0 0.3
          abx "beta-inc-x-zero" 2.0 3.0 0.0
          abx "beta-inc-x-one" 2.0 3.0 1.0
          abx "beta-inc-x-negative" 2.0 3.0 -0.1
          abx "beta-inc-x-above-one" 2.0 3.0 1.5
          abx "beta-inc-a-zero" 0.0 3.0 0.5
          abx "beta-inc-b-negative" 2.0 -3.0 0.5
          abx "beta-inc-a-infinity" inf 3.0 0.5
          abx "beta-inc-nan" 2.0 3.0 nan
          // ---- the inverse: near zero and one, both halves, the starts ----
          abp "beta-inv-near-zero" 2.0 3.0 1.0e-12
          abp "beta-inv-near-one" 2.0 3.0 (1.0 - 1.0e-12)
          abp "beta-inv-halves-tail" 0.5 0.5 1.0e-10
          abp "beta-inv-halves-middle" 0.5 0.5 0.5
          abp "beta-inv-symmetric-middle" 5.0 5.0 0.5
          abp "beta-inv-small-a-tail" 0.05 30.0 1.0e-11
          abp "beta-inv-small-b" 30.0 0.05 0.7
          abp "beta-inv-upper-half" 10.0 2.0 0.9
          abp "beta-inv-large" 1000.0 1000.0 0.975
          abp "beta-inv-uniform" 1.0 1.0 0.3
          abp "beta-inv-one-five" 1.0 5.0 0.25
          abp "beta-inv-below-one-upper" 0.3 0.7 0.999
          abp "beta-inv-normal-start" 3.0 8.0 0.05
          abp "beta-inv-root-underflows" 1.0e-3 1.0 1.0e-300
          abp "beta-inv-p-zero" 2.0 3.0 0.0
          abp "beta-inv-p-one" 2.0 3.0 1.0
          abp "beta-inv-p-negative" 2.0 3.0 -0.5
          abp "beta-inv-p-above-one" 2.0 3.0 1.5
          abp "beta-inv-a-zero" 0.0 3.0 0.5
          abp "beta-inv-nan" 2.0 3.0 nan
          // ---- digamma: its root, the arms, the reflection ----
          one "digamma-at-root" "digamma" digammaRoot
          one "digamma-above-root" "digamma" (digammaRoot + pow2 -52)
          one "digamma-below-root" "digamma" (digammaRoot - pow2 -52)
          one "digamma-near-root" "digamma" 1.46
          one "digamma-one" "digamma" 1.0
          one "digamma-below-one" "digamma" (1.0 - pow2 -53)
          one "digamma-half" "digamma" 0.5
          one "digamma-below-two" "digamma" (2.0 - pow2 -52)
          one "digamma-two" "digamma" 2.0
          one "digamma-seven-and-a-quarter" "digamma" 7.25
          one "digamma-below-threshold" "digamma" (10.0 - pow2 -50)
          one "digamma-at-threshold" "digamma" 10.0
          one "digamma-tiny" "digamma" 1.0e-300
          one "digamma-large" "digamma" 1.0e17
          one "digamma-huge" "digamma" 1.0e300
          one "digamma-negative-half" "digamma" -0.5
          one "digamma-negative-three-halves" "digamma" -1.5
          one "digamma-negative-ten-and-a-quarter" "digamma" -10.25
          one "digamma-negative-tiny" "digamma" -1.0e-300
          one "digamma-zero" "digamma" 0.0
          one "digamma-negative-integer" "digamma" -2.0
          one "digamma-infinity" "digamma" inf
          one "digamma-negative-infinity" "digamma" -inf
          one "digamma-nan" "digamma" nan
          // ---- trigamma and the higher orders: near zero, the thresholds, large arguments ----
          one "trigamma-one" "trigamma" 1.0
          one "trigamma-near-zero" "trigamma" 1.0e-150
          one "trigamma-overflows" "trigamma" 1.0e-300
          one "trigamma-small" "trigamma" 1.0e-3
          one "trigamma-half" "trigamma" 0.5
          one "trigamma-below-threshold" "trigamma" (11.0 - pow2 -50)
          one "trigamma-at-threshold" "trigamma" 11.0
          one "trigamma-large" "trigamma" 1.0e5
          one "trigamma-huge" "trigamma" 1.0e300
          one "trigamma-zero" "trigamma" 0.0
          one "trigamma-negative" "trigamma" -1.5
          one "trigamma-infinity" "trigamma" inf
          one "trigamma-nan" "trigamma" nan
          nx "polygamma-zero-is-digamma" 0.0 7.25
          nx "polygamma-one-is-trigamma" 1.0 0.5
          nx "polygamma-two-near-zero" 2.0 1.0e-100
          nx "polygamma-two-half" 2.0 0.5
          nx "polygamma-two-below-threshold" 2.0 (12.0 - pow2 -50)
          nx "polygamma-two-at-threshold" 2.0 12.0
          nx "polygamma-two-large" 2.0 1.0e4
          nx "polygamma-three-small" 3.0 0.1
          nx "polygamma-three-at-threshold" 3.0 13.0
          nx "polygamma-three-large" 3.0 1.0e3
          nx "polygamma-four-near-zero" 4.0 1.0e-60
          nx "polygamma-four-below-threshold" 4.0 (14.0 - pow2 -50)
          nx "polygamma-four-at-threshold" 4.0 14.0
          nx "polygamma-four-large" 4.0 1.0e6
          nx "polygamma-ten" 10.0 2.5
          nx "polygamma-ten-at-threshold" 10.0 20.0
          nx "polygamma-largest-order" 170.0 5.0
          nx "polygamma-order-above-limit" 171.0 5.0
          nx "polygamma-order-negative" -1.0 2.0
          nx "polygamma-order-not-an-integer" 1.5 2.0
          nx "polygamma-two-at-zero" 2.0 0.0
          nx "polygamma-two-negative" 2.0 -0.5
          nx "polygamma-two-infinity" 2.0 inf
          nx "polygamma-zero-negative" 0.0 -0.5
          nx "polygamma-nan-order" nan 2.0
          nx "polygamma-nan" 2.0 nan
          // ---- Phase 636 (D81): the incomplete beta function where one parameter is large. The t's
          // tail at a square of 3.84, either side of the switch, a parameter far below one, the
          // other end, both parameters large, and either side of the parameter sum of 128 ----
          abx "beta-inc-corner-500" 500.0 0.5 (1000.0 / 1003.84)
          abx "beta-inc-corner-20000" 2.0e4 0.5 (4.0e4 / 40003.84)
          abx "beta-inc-corner-500000" 5.0e5 0.5 (1.0e6 / 1000003.84)
          abx "beta-inc-corner-million" 1.0e6 0.5 (1.0 - pow2 -19)
          abx "beta-inc-corner-above-switch" 5.0e5 0.5 (1.0 - pow2 -20)
          abx "beta-inc-corner-near-one" 5.0e5 0.5 (1.0 - pow2 -23)
          abx "beta-inc-corner-tiny-b" 1.0e5 1.0e-3 (1.0 - 1.0e-6)
          abx "beta-inc-corner-b-five" 1.0e5 5.0 (1.0 - 5.0e-5)
          abx "beta-inc-corner-mirror" 0.5 5.0e5 2.0e-6
          abx "beta-inc-corner-mirror-above-switch" 0.5 5.0e5 1.0e-5
          abx "beta-inc-million-at-mean" 1.0e6 1.0e6 0.5
          abx "beta-inc-million-skewed" 1.0e6 2.0e6 0.3333
          abx "beta-inc-below-contracted" 127.5 0.25 0.99
          abx "beta-inc-at-contracted" 127.75 0.25 0.99
          abp "beta-inv-corner-500" 500.0 0.5 0.05
          abp "beta-inv-corner-500000" 5.0e5 0.5 0.05
          abp "beta-inv-corner-mirror" 0.5 5.0e5 0.95
          // ---- the complement: both tails, each arm, the corner from both ends, the table ----
          abc "beta-comp-uniform" 1.0 1.0 0.3
          abc "beta-comp-halves" 0.5 0.5 0.3
          abc "beta-comp-upper-tail" 5.0 5.0 0.996
          abc "beta-comp-lower-tail" 5.0 5.0 0.004
          abc "beta-comp-skewed-upper" 2.0 50.0 0.9
          abc "beta-comp-below-half-restored" 2.0 50.0 0.3
          abc "beta-comp-small-a-series" 1.0e-3 3.0 0.3
          abc "beta-comp-x-near-one" 3.0 0.5 (1.0 - pow2 -53)
          abc "beta-comp-tiny-x" 0.5 2.0 1.0e-300
          abc "beta-comp-corner-500" 500.0 0.5 (1000.0 / 1003.84)
          abc "beta-comp-corner-500000" 5.0e5 0.5 (1.0e6 / 1000003.84)
          abc "beta-comp-corner-above-switch" 5.0e5 0.5 (1.0 - pow2 -20)
          abc "beta-comp-corner-mirror" 0.5 5.0e5 2.0e-6
          abc "beta-comp-corner-mirror-tail" 0.5 5.0e5 1.0e-5
          abc "beta-comp-corner-tiny-a" 1.0e-3 1.0e4 1.0e-5
          abc "beta-comp-small-and-large" 2.5 1.0e4 1.0e-4
          abc "beta-comp-million-at-mean" 1.0e6 1.0e6 0.5
          abc "beta-comp-x-zero" 2.0 3.0 0.0
          abc "beta-comp-x-one" 2.0 3.0 1.0
          abc "beta-comp-x-negative" 2.0 3.0 -0.1
          abc "beta-comp-x-above-one" 2.0 3.0 1.5
          abc "beta-comp-a-zero" 0.0 3.0 0.5
          abc "beta-comp-b-negative" 2.0 -3.0 0.5
          abc "beta-comp-a-infinity" inf 3.0 0.5
          abc "beta-comp-nan" 2.0 3.0 nan
          // ---- the complement's inverse: near zero and one, both halves, the corner, the table ----
          abq "beta-inv-comp-uniform" 1.0 1.0 0.3
          abq "beta-inv-comp-near-zero" 2.0 3.0 1.0e-12
          abq "beta-inv-comp-near-one" 2.0 3.0 (1.0 - 1.0e-12)
          abq "beta-inv-comp-tiny-q" 2.0 3.0 1.0e-300
          abq "beta-inv-comp-halves-middle" 0.5 0.5 0.5
          abq "beta-inv-comp-small-b" 30.0 0.05 0.3
          abq "beta-inv-comp-upper-half" 10.0 2.0 0.1
          abq "beta-inv-comp-corner-500" 500.0 0.5 0.05
          abq "beta-inv-comp-corner-500000" 5.0e5 0.5 0.05
          abq "beta-inv-comp-corner-mirror" 0.5 5.0e5 0.05
          abq "beta-inv-comp-q-zero" 2.0 3.0 0.0
          abq "beta-inv-comp-q-one" 2.0 3.0 1.0
          abq "beta-inv-comp-q-negative" 2.0 3.0 -0.5
          abq "beta-inv-comp-q-above-one" 2.0 3.0 1.5
          abq "beta-inv-comp-a-zero" 0.0 3.0 0.5
          abq "beta-inv-comp-nan" 2.0 3.0 nan ]

    /// The true value for every vector the routine computes to a finite, non-zero answer, rounded once
    /// to the nearest double: mpmath 1.4.1 at fifty digits, by `SpecialReference/special_reference.py
    /// references` over `referenceCases ()`. A vector the special-value table answers, and one whose
    /// answer overflows or underflows, carries none.
    let references: (string * string) list =
        [ "gammaLn-just-above-one", "bca2788cfc6fb617"
          "gammaLn-just-below-one", "3c92788cfc6fb61a"
          "gammaLn-near-one", "be38ca8da01572ee"
          "gammaLn-just-below-two", "bc9b0ee6072093cd"
          "gammaLn-just-above-two", "3cab0ee6072093d1"
          "gammaLn-near-two", "be32289017aa8541"
          "gammaLn-smallest-subnormal", "40874385446d71c3"
          "gammaLn-tiny", "4085963447f87fb5"
          "gammaLn-small", "4043926cd770aa67"
          "gammaLn-below-half", "3fe250d048e7a1be"
          "gammaLn-half", "3fe250d048e7a1bd"
          "gammaLn-three-halves", "bfbeeb95b094c191"
          "gammaLn-at-kernel-top", "3fd2383e809a67e8"
          "gammaLn-above-kernel-top", "3fd2383e809a67ed"
          "gammaLn-seven-and-a-quarter", "401c35701a50ff06"
          "gammaLn-below-stirling", "40299a8921a7f7cf"
          "gammaLn-at-stirling", "40299a8921a7f7cf"
          "gammaLn-hundred", "40767225b4879462"
          "gammaLn-below-two-to-54", "43a23708872320e1"
          "gammaLn-at-two-to-54", "43a23708872320e2"
          "gammaLn-huge", "7ed017f38e7a1ab5"
          "gammaLn-negative-quarter", "3ff96ee685defb2d"
          "gammaLn-negative-tiny", "4085963447f87fb5"
          "gammaLn-negative-half", "3ff43f89a3f0edd6"
          "gammaLn-negative-three-halves", "3feb858151820f86"
          "gammaLn-negative-near-pole", "403300ae0f6cfa94"
          "gammaLn-negative-hundred-and-a-half", "c076ce6a5dbefb91"
          "gammaLn-negative-large", "c1343387cb3e0f58"
          "gamma-one", "3ff0000000000000"
          "gamma-two", "3ff0000000000000"
          "gamma-five", "4038000000000000"
          "gamma-last-exact-integer", "444e77526159f06c"
          "gamma-first-computed-integer", "4495e5c335f8a4ce"
          "gamma-largest-integer", "7fa4ab7864418639"
          "gamma-half", "3ffc5bf891b4ef6b"
          "gamma-three-halves", "3fec5bf891b4ef6b"
          "gamma-five-halves", "3ff544fa6d47b390"
          "gamma-ten-and-a-half", "41314ade639225ca"
          "gamma-below-stirling", "4116260000000000"
          "gamma-largest-half-integer", "7f69589f849167a8"
          "gamma-negative-half", "c00c5bf891b4ef6b"
          "gamma-negative-three-halves", "4002e7fb0bcdf4f2"
          "gamma-negative-large-half-integer", "8017d2374dfcda7a"
          "gamma-negative-subnormal-result", "0000238ee05c879e"
          "gamma-tiny", "7e37e43c8800759b"
          "gamma-negative-tiny", "fe37e43c8800759b"
          "gamma-above-pole-at-minus-one", "c2700000000006c4"
          "gamma-below-pole-at-minus-two", "c25fffffffffe279"
          "gamma-at-overflow-point", "7feffffffffffe51"
          "beta-one-argument-tiny", "7e37e43c8800759b"
          "beta-both-small", "4212a05f20000000"
          "beta-small-and-moderate", "4376345785d89fff"
          "beta-halves", "400921fb54442d18"
          "beta-ones", "3ff0000000000000"
          "beta-three-four", "3f91111111111111"
          "beta-swapped", "3f7e56bfd32a7de5"
          "beta-ordered", "3f7e56bfd32a7de5"
          "beta-inexact-sum", "402536d120d28d2a"
          "beta-sum-at-ratio-limit", "3692237b31399173"
          "beta-sum-above-ratio-limit", "368e7838109bc7c8"
          "beta-hundreds", "3356b73dc3216f6d"
          "beta-large-and-small", "3d5d94a61f863cb1"
          "betaLn-one-argument-tiny", "4085963447f87fb5"
          "betaLn-halves", "3ff250d048e7a1bd"
          "betaLn-three-four", "c010609bdc65328b"
          "betaLn-swapped", "c0139f10900724e5"
          "betaLn-ordered", "c0139f10900724e5"
          "betaLn-inexact-sum", "4002e464b68956bb"
          "betaLn-both-at-stirling", "c02b78f30988f675"
          "betaLn-one-below-stirling", "c02b754493678d9b"
          "betaLn-both-below-stirling", "c02b7196c96608b7"
          "betaLn-small-and-large", "c00153abcc1a0e59"
          "betaLn-both-large", "c13d232c4a7ce9ed"
          "betaLn-huge", "fe408f73b99f1cd7"
          "gammaP-a-near-zero", "3fefffffff69b35b"
          "gammaQ-a-near-zero", "3e12c99496f9c485"
          "gammaQ-a-near-zero-below-one", "3db81f1bd9358c62"
          "gammaQ-a-near-zero-at-one", "3db81f1bd9358c60"
          "gammaQ-a-near-zero-large-x", "3d40292bbeb25729"
          "gammaP-tiny-x", "20cd8c8baaa00773"
          "gammaQ-tiny-a-tiny-x", "3e728701ab739da1"
          "gammaP-half-below-boundary", "3feaf767a741088b"
          "gammaP-half-at-boundary", "3feaf767a741088b"
          "gammaQ-half-below-boundary", "3fc4226162fbddd6"
          "gammaQ-half-at-boundary", "3fc4226162fbddd5"
          "gammaP-x-is-a-half", "3fe5d897a241a6fa"
          "gammaP-x-is-a-one", "3fe43a54e4e98864"
          "gammaQ-x-is-a-one", "3fd78b56362cef38"
          "gammaP-x-is-a-ten", "3fe158a3c88f41c2"
          "gammaQ-x-is-a-ten", "3fdd4eb86ee17c7d"
          "gammaP-below-stirling", "3fe158a3c88f41c2"
          "gammaP-x-is-a-hundred", "3fe06cf199f5ebe3"
          "gammaQ-x-is-a-hundred", "3fdf261ccc14283b"
          "gammaP-three-below-boundary", "3fe861752d327f3c"
          "gammaP-three-at-boundary", "3fe861752d327f3d"
          "gammaQ-three-below-boundary", "3fce7a2b4b360310"
          "gammaQ-three-at-boundary", "3fce7a2b4b36030d"
          "gammaP-large-a-x-is-a", "3fe00371e616c2ab"
          "gammaQ-large-a-x-is-a", "3fdff91c33d27aa9"
          "gammaQ-large-a-at-boundary", "3fdfe470d985fc2b"
          "gammaP-very-large-a-below", "3fc44ed064d21da3"
          "gammaQ-very-large-a-above", "3f9759f4f8667741"
          "gammaQ-upper-tail", "3d61aa08415894d6"
          "gammaP-lower-tail", "3d322332a38df20f"
          "beta-inc-halves", "3fd79ddc9edb5511"
          "beta-inc-both-below-one", "3fe57550e2dd44b2"
          "beta-inc-both-tiny", "3fe0000000000000"
          "beta-inc-at-switch", "3fe25a33d1834081"
          "beta-inc-above-switch", "3fe25a33d1834082"
          "beta-inc-lower-tail", "3de17f691da3a554"
          "beta-inc-upper-tail", "3fefffffffee8097"
          "beta-inc-skewed-upper", "3ff0000000000000"
          "beta-inc-skewed-lower", "3de754299bf48e00"
          "beta-inc-small-a-tiny-x", "3ed9d08c68dffc5e"
          "beta-inc-tiny-x", "20d3a3ed8fafaf58"
          "beta-inc-x-near-one", "3feffffff564b5c0"
          "beta-inc-both-at-stirling", "3fe0000000000000"
          "beta-inc-one-below-stirling", "3fe00776daa05fa6"
          "beta-inc-large-at-mean", "3fe0000000000000"
          "beta-inc-large-below-mean", "3f49063408f2d1ee"
          "beta-inc-very-large-at-mean", "3fe0000000000000"
          "beta-inc-very-large-skewed", "3fbc366dcc7db038"
          "beta-inc-small-and-large", "3fc350595b77cb60"
          "beta-inc-uniform", "3fd3333333333333"
          "beta-inv-near-zero", "3e9b65a7754d4e87"
          "beta-inv-near-one", "3fefff7be2f7f4a8"
          "beta-inv-halves-tail", "3bdd214322668f9e"
          "beta-inv-halves-middle", "3fe0000000000000"
          "beta-inv-symmetric-middle", "3fe0000000000000"
          "beta-inv-small-a-tail", "11e6e58fb8ba0a10"
          "beta-inv-small-b", "3fefffffffffe7b7"
          "beta-inv-upper-half", "3fee6ae3f5b5a938"
          "beta-inv-large", "3fe0b372b3dad80d"
          "beta-inv-uniform", "3fd3333333333333"
          "beta-inv-one-five", "3faca08fccda4c90"
          "beta-inv-below-one-upper", "3feffe3acc55a466"
          "beta-inv-normal-start", "3fb656f641c3e60a"
          "digamma-at-root", "bc9aa2d9b3ce29e0"
          "digamma-above-root", "3ca1a5beef0723bb"
          "digamma-below-root", "bcb6244c516aa6cf"
          "digamma-near-root", "bf59e55b82ce0406"
          "digamma-one", "bfe2788cfc6fb619"
          "digamma-below-one", "bfe2788cfc6fb61b"
          "digamma-half", "bfff6a897d3214fc"
          "digamma-below-two", "3fdb0ee6072093cc"
          "digamma-two", "3fdb0ee6072093ce"
          "digamma-seven-and-a-quarter", "3ffe9137b7a7e563"
          "digamma-below-threshold", "40020396dc85cc95"
          "digamma-at-threshold", "40020396dc85cc95"
          "digamma-tiny", "fe37e43c8800759b"
          "digamma-large", "4043926cd770aa67"
          "digamma-huge", "4085963447f87fb5"
          "digamma-negative-half", "3fa2aed059bd608a"
          "digamma-negative-three-halves", "3fe680425af12b5e"
          "digamma-negative-ten-and-a-quarter", "4016114355684b8d"
          "digamma-negative-tiny", "7e37e43c8800759b"
          "trigamma-one", "3ffa51a6625307d3"
          "trigamma-near-zero", "7e37e43c8800759c"
          "trigamma-small", "412e848348fa1c6d"
          "trigamma-half", "4013bd3cc9be45de"
          "trigamma-below-threshold", "3fb85cd22b6ed8f7"
          "trigamma-at-threshold", "3fb85cd22b6ed8f7"
          "trigamma-large", "3ee4f8bc681cdfb5"
          "trigamma-huge", "01a56e1fc2f8f359"
          "polygamma-zero-is-digamma", "3ffe9137b7a7e563"
          "polygamma-one-is-trigamma", "4013bd3cc9be45de"
          "polygamma-two-near-zero", "fe47e43c8800759b"
          "polygamma-two-half", "c030d42c0452055d"
          "polygamma-two-below-threshold", "bf7ee9d183c6d81b"
          "polygamma-two-at-threshold", "bf7ee9d183c6d81b"
          "polygamma-two-large", "be457a1ba0c9c0d3"
          "polygamma-three-small", "40ed4c90697c9621"
          "polygamma-three-at-threshold", "3f50b94cb5d9d0df"
          "polygamma-three-large", "3e2134a5e1f23849"
          "polygamma-four-near-zero", "fe81eb2d66005835"
          "polygamma-four-below-threshold", "bf2791c5f3a20e66"
          "polygamma-four-at-threshold", "bf2791c5f3a20e66"
          "polygamma-four-large", "bb1d03a7b40b4af3"
          "polygamma-ten", "c0638758daf466ae"
          "polygamma-ten-at-threshold", "be683703d87b7a32"
          "polygamma-largest-order", "e6d3f84577fb8d50"
          "polygamma-zero-negative", "3fa2aed059bd608a"
          "beta-inc-corner-500", "3fa9c3ac2c930386"
          "beta-inc-corner-20000", "3fa9a03699b493d7"
          "beta-inc-corner-500000", "3fa99f573ccca17d"
          "beta-inc-corner-million", "3faa0312fb57635c"
          "beta-inc-corner-above-switch", "3fd50ad395d9245d"
          "beta-inc-corner-near-one", "3fe75b4a14c8f995"
          "beta-inc-corner-tiny-b", "3f5dd9ff81c4573b"
          "beta-inc-corner-b-five", "3fdc306527bd3dbb"
          "beta-inc-corner-mirror", "3feaf767def80af1"
          "beta-inc-corner-mirror-above-switch", "3feff32d319922bf"
          "beta-inc-million-at-mean", "3fe0000000000000"
          "beta-inc-million-skewed", "3fdce2571285835a"
          "beta-inc-below-contracted", "3fa743afe60fe318"
          "beta-inc-at-contracted", "3fa72eaa0466b868"
          "beta-inv-corner-500", "3fefe0935186885a"
          "beta-inv-corner-500000", "3feffff7f1a2ab83"
          "beta-inv-corner-mirror", "3ed01cbaa8f913e4"
          "beta-comp-uniform", "3fe6666666666666"
          "beta-comp-halves", "3fe43111b0925577"
          "beta-comp-upper-tail", "3de17f691da3a569"
          "beta-comp-lower-tail", "3fefffffffee8097"
          "beta-comp-skewed-upper", "35e58367e5c8e413"
          "beta-comp-below-half-restored", "3e934f9543d6132e"
          "beta-comp-small-a-series", "3f30fba8c3a0d5e9"
          "beta-comp-x-near-one", "3e5536948017480f"
          "beta-comp-tiny-x", "3ff0000000000000"
          "beta-comp-corner-500", "3fee63c53d36cfc8"
          "beta-comp-corner-500000", "3fee660a8c3335e8"
          "beta-comp-corner-above-switch", "3fe57a9635136dd2"
          "beta-comp-corner-mirror", "3fc42260841fd43c"
          "beta-comp-corner-mirror-tail", "3f59a59ccdba8244"
          "beta-comp-corner-tiny-a", "3f5dda26be9803a2"
          "beta-comp-small-and-large", "3feb2be9a9220d28"
          "beta-comp-million-at-mean", "3fe0000000000000"
          "beta-inv-comp-uniform", "3fe6666666666666"
          "beta-inv-comp-near-zero", "3fefff7be2b81bee"
          "beta-inv-comp-near-one", "3e9b6593992ee1d6"
          "beta-inv-comp-halves-middle", "3fe0000000000000"
          "beta-inv-comp-small-b", "3fefffffffffe7b7"
          "beta-inv-comp-upper-half", "3fee6ae3f5b5a938"
          "beta-inv-comp-corner-500", "3feffff7bfe59336"
          "beta-inv-comp-corner-500000", "3feffffffde3920e"
          "beta-inv-comp-corner-mirror", "3ed01cbaa8f913e5" ]

    /// The lines `special_reference.py references` reads, one per vector whose answer is finite and not
    /// zero and that no special-value row answers: `<id> <op> <operand bits> …`, and for the inverse the
    /// package's own answer last (its reference is the root nearest that answer).
    let referenceCases () : string list =
        cases
        |> List.choose (fun c ->
            let value = answer c
            let finite = not (Double.IsNaN value) && not (Double.IsInfinity value)

            let exact =
                // The rows a table answers exactly: nothing was computed, so nothing is measured.
                (c.Op = "regularizedGammaP" || c.Op = "regularizedGammaQ")
                && (scalar c "x" = 0.0 || Double.IsInfinity(scalar c "x"))
                || ((c.Op = "regularizedBeta" || c.Op = "regularizedBetaComplement")
                    && (scalar c "x" = 0.0 || scalar c "x" = 1.0))
                || ((c.Op = "inverseRegularizedBeta" || c.Op = "inverseRegularizedBetaComplement")
                    && (value = 0.0 || value = 1.0))

            if finite && value <> 0.0 && not exact then
                let operands =
                    (c.Input |> List.map (snd >> ElementaryVectors.hex))
                    @ (if c.Op = "inverseRegularizedBeta" || c.Op = "inverseRegularizedBetaComplement" then
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

    let private special =
        "Phase 631 (DECISIONS.md D78): the vectors whose `op` is `gammaLn`, `gamma`, `betaLn`, `beta`, "
        + "`regularizedGammaP`, `regularizedGammaQ`, `regularizedBeta`, `inverseRegularizedBeta`, `polygamma`, "
        + "`digamma` or `trigamma` hold the pinned special functions of `Special`, over authored hard inputs: "
        + "each function's zeros, poles and bounds, the boundaries between its arms, tiny and huge arguments, "
        + "the reflections and every row of its special-value table. `input` names the operands in the "
        + "function's argument order (`x`; `a`, `b`; `a`, `x`; `a`, `b`, `x`; `a`, `b`, `p`; `n`, `x`). Each "
        + "answers `{ verdict: ok, value }`, the function's bits, compared byte for byte. A vector the routine "
        + "computed to a finite, non-zero answer also carries `reference`, the true value rounded once to the "
        + "nearest double (mpmath at fifty digits; for the inverse, the root nearest the answer), and `ulps`, "
        + "the package's distance from it in units in the last place, which is itself part of the document a "
        + "host must reproduce."

    let private complements =
        "Phase 636 (DECISIONS.md D81): the vectors whose `op` is `regularizedBetaComplement` or "
        + "`inverseRegularizedBetaComplement` hold the two complements `Special` publishes, `1 - I_x(a, b)` "
        + "and the `x` at which it is `q` (`input` is `a`, `b`, `x` and `a`, `b`, `q`), read as the vectors "
        + "above are. They follow the `regularizedBeta` and `inverseRegularizedBeta` vectors this phase adds "
        + "after `polygamma`'s, whose ids begin `beta-inc-corner`, `beta-inc-million`, `beta-inc-below-contracted`, "
        + "`beta-inc-at-contracted` and `beta-inv-corner`: the incomplete beta function where one parameter is "
        + "large and the argument within a few standard deviations of the mean. From a parameter sum of 128 "
        + "the function reads its continued fraction in another form, so every vector with a sum that large "
        + "answers the bits of that form."

    /// The elementary family's whole document: every vector `ElementaryVectors` holds, unchanged, then
    /// this module's, as the one corpus writer renders them.
    let render () : string =
        ElementaryVectors.renderWith
            [ "special", CorpusFile.str special; "complements", CorpusFile.str complements ]
            (cases |> List.map caseCell)
