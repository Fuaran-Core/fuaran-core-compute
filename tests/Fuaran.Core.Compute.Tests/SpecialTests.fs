module Fuaran.Compute.Tests.SpecialTests

// ============================================================================
//  Phase 631 — the suite over Fuaran.Compute.Elementary's `Special`
//  (DECISIONS.md D78):
//
//   * every vector the routines computed carries a reference, and is within
//     its function's recorded bound of it;
//   * the vectors answer what was worked by hand;
//   * the source reaches no host mathematics call;
//   * `Special` is declared once across the packages.
//
//  The identity laws are `ElementaryLaws.laws`, which ElementaryTests runs on
//  .NET and the node leg runs under Fable; the byte comparison of the document
//  on both hosts is ElementaryTests' and LinearAlgebraTests'.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Compute.Elementary

let private lineOf (id: string) : string =
    SpecialVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

let private caseOf (id: string) : SpecialVectors.Case =
    SpecialVectors.cases |> List.find (fun c -> c.Id = id)

let private ulpsFrom (id: string) (want: float) : int64 =
    ElementaryVectors.ulpsApart (SpecialVectors.answer (caseOf id)) want

[<Tests>]
let tests =
    testList
        "Phase 631 - special functions"
        [ testCase "every computed special vector carries a reference and is within its function's bound of it"
          <| fun () ->
              let referenced = SpecialVectors.references |> List.map fst

              let computed =
                  SpecialVectors.referenceCases ()
                  |> List.map (fun line -> line.Substring(0, line.IndexOf ' '))

              Expect.equal referenced computed "the references are the computed vectors, in file order"
              Expect.isTrue (referenced.Length > 150) "the family is not empty"

              let ids = SpecialVectors.cases |> List.map (fun c -> c.Id)
              Expect.equal (List.distinct ids) ids "no vector id is used twice"

              let elementary = ElementaryVectors.cases |> List.map (fun c -> c.Id) |> Set.ofList

              Expect.isEmpty
                  (ids |> List.filter elementary.Contains)
                  "no special vector takes an elementary vector's id"

              for id, h in SpecialVectors.references do
                  let c = caseOf id

                  let bound =
                      SpecialVectors.referenceUlps |> List.find (fun (op, _) -> op = c.Op) |> snd

                  let apart =
                      ElementaryVectors.ulpsApart (SpecialVectors.answer c) (ElementaryVectors.ofHex h)

                  Expect.isLessThanOrEqual
                      apart
                      (int64 bound)
                      (sprintf "%s is %d ulps from its reference; %s's bound is %d" id apart c.Op bound)

                  Expect.stringContains
                      (lineOf id)
                      (sprintf "\"reference\": \"%s\", \"ulps\": %d" h apart)
                      (sprintf "%s states its reference and its distance" id)

              // Every function has a referenced vector, so no bound is vacuous.
              for op, _ in SpecialVectors.referenceUlps do
                  Expect.isTrue
                      (SpecialVectors.references |> List.exists (fun (id, _) -> (caseOf id).Op = op))
                      (sprintf "%s has a referenced vector" op)

          testCase "the special vectors answer what was worked by hand"
          <| fun () ->
              let h = ElementaryVectors.hex

              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "vector %s" id)

              let value (x: float) = sprintf "\"value\": \"%s\"" (h x)
              // The exact rows.
              has "gammaLn-one" (value 0.0)
              has "gammaLn-two" (value 0.0)
              has "gammaLn-zero" (value Double.PositiveInfinity)
              has "gammaLn-negative-integer" (value Double.PositiveInfinity)
              has "gamma-five" (value 24.0)
              has "gamma-last-exact-integer" (value 1124000727777607680000.0)
              has "gamma-integer-overflows" (value Double.PositiveInfinity)
              has "gamma-zero" "\"value\": \"nan\""
              has "gamma-negative-integer" "\"value\": \"nan\""
              has "beta-zero-argument" "\"value\": \"nan\""
              has "gammaP-x-zero" (value 0.0)
              has "gammaQ-x-zero" (value 1.0)
              has "gammaP-x-infinity" (value 1.0)
              has "gammaQ-x-infinity" (value 0.0)
              has "beta-inc-x-zero" (value 0.0)
              has "beta-inc-x-one" (value 1.0)
              has "beta-inc-x-above-one" "\"value\": \"nan\""
              has "beta-inv-p-zero" (value 0.0)
              has "beta-inv-p-one" (value 1.0)
              has "beta-comp-x-zero" (value 1.0)
              has "beta-comp-x-one" (value 0.0)
              has "beta-comp-x-above-one" "\"value\": \"nan\""
              has "beta-inv-comp-q-zero" (value 1.0)
              has "beta-inv-comp-q-one" (value 0.0)
              has "beta-inv-comp-q-negative" "\"value\": \"nan\""
              has "digamma-zero" "\"value\": \"nan\""
              has "trigamma-infinity" (value 0.0)
              has "polygamma-order-not-an-integer" "\"value\": \"nan\""
              // Closed forms, each within a stated distance of the constant.
              let sqrtPi = 1.7724538509055159
              Expect.isLessThanOrEqual (ulpsFrom "gamma-half" sqrtPi) 2L "gamma (1/2) is the square root of pi"
              Expect.isLessThanOrEqual (ulpsFrom "gamma-three-halves" (0.5 * sqrtPi)) 2L "gamma (3/2)"
              Expect.isLessThanOrEqual (ulpsFrom "gamma-five-halves" (0.75 * sqrtPi)) 2L "gamma (5/2)"
              Expect.isLessThanOrEqual (ulpsFrom "gamma-negative-half" (-2.0 * sqrtPi)) 2L "gamma (-1/2)"
              Expect.isLessThanOrEqual (ulpsFrom "beta-halves" Math.PI) 2L "B (1/2, 1/2) is pi"
              Expect.isLessThanOrEqual (ulpsFrom "beta-three-four" (1.0 / 60.0)) 2L "B (3, 4) is 1/60"
              Expect.isLessThanOrEqual (ulpsFrom "beta-ones" 1.0) 0L "B (1, 1) is one"
              Expect.isLessThanOrEqual (ulpsFrom "beta-inc-uniform" 0.3) 2L "I_x (1, 1) is x"
              Expect.isLessThanOrEqual (ulpsFrom "beta-inv-uniform" 0.3) 2L "the inverse of I_x (1, 1) is p"
              Expect.isLessThanOrEqual (ulpsFrom "beta-comp-uniform" 0.7) 2L "1 - I_x (1, 1) is 1 - x"
              Expect.isLessThanOrEqual (ulpsFrom "beta-inv-comp-uniform" 0.7) 2L "its inverse is 1 - q"
              Expect.isLessThanOrEqual (ulpsFrom "beta-inv-halves-middle" 0.5) 16L "the median of B (1/2, 1/2)"
              Expect.isLessThanOrEqual (ulpsFrom "beta-inv-symmetric-middle" 0.5) 16L "the median of B (5, 5)"
              Expect.isLessThanOrEqual (ulpsFrom "gammaQ-x-is-a-one" 0.36787944117144233) 2L "Q (1, 1) is 1/e"
              Expect.isLessThanOrEqual (ulpsFrom "digamma-one" -0.5772156649015329) 1L "digamma 1 is -gamma"
              Expect.isLessThanOrEqual (ulpsFrom "trigamma-one" 1.6449340668482264) 1L "trigamma 1 is pi^2/6"
              // The named entry points are the general function.
              Expect.equal
                  (h (Special.polygamma 0.0 7.25))
                  (h (Special.digamma 7.25))
                  "polygamma 0 is digamma, bit for bit"

              Expect.equal
                  (h (Special.polygamma 1.0 0.5))
                  (h (Special.trigamma 0.5))
                  "polygamma 1 is trigamma, bit for bit"
              // The symmetry is exact.
              has "beta-swapped" (value (Special.beta 2.5 7.25))
              has "betaLn-swapped" (value (Special.betaLn 2.5 7.25))

          testCase
              "the incomplete beta function keeps its accuracy where one parameter is large, up to a million (Phase 636)"
          <| fun () ->
              // The vectors of the repaired region: the t's tail at a square of 3.84 and its neighbours,
              // from the large parameter's end and from the other. Each is within 32 units of its
              // reference; before the repair the function was 247 units out at a = 500 and 176,900
              // at a = 500,000 on the first of these inputs (DECISIONS.md D81).
              let corner =
                  SpecialVectors.references
                  |> List.filter (fun (id, _) ->
                      id.StartsWith "beta-inc-corner"
                      || id.StartsWith "beta-comp-corner"
                      || id.StartsWith "beta-inc-million"
                      || id.StartsWith "beta-comp-million")

              Expect.isTrue (corner.Length >= 18) "the corner has its vectors"

              for id, h in corner do
                  let apart = ulpsFrom id (ElementaryVectors.ofHex h)
                  Expect.isLessThanOrEqual apart 32L (sprintf "%s is %d ulps from its reference" id apart)

              // Either side of the parameter sum at which the fraction changes form: the same function,
              // and the two answers are neighbours' answers, not two functions'.
              let below = SpecialVectors.answer (caseOf "beta-inc-below-contracted")
              let at = SpecialVectors.answer (caseOf "beta-inc-at-contracted")
              Expect.isTrue (at < below && below - at < 1.0e-3) "the function is continuous across the sum of 128"

          testCase "the complement is computed and not subtracted from one, and so is its inverse (Phase 636)"
          <| fun () ->
              // Go-red: one minus the function, on the vectors where the complement is small, is far
              // from the reference the complement meets.
              for id in
                  [ "beta-comp-upper-tail"
                    "beta-comp-skewed-upper"
                    "beta-comp-x-near-one"
                    "beta-comp-corner-tiny-a" ] do
                  let c = caseOf id

                  let v (name: string) =
                      c.Input |> List.find (fun (n, _) -> n = name) |> snd

                  let reference = SpecialVectors.references |> List.find (fun (i, _) -> i = id) |> snd
                  let subtracted = 1.0 - Special.regularizedBeta (v "a") (v "b") (v "x")

                  Expect.isGreaterThan
                      (ElementaryVectors.ulpsApart subtracted (ElementaryVectors.ofHex reference))
                      100L
                      (sprintf "%s: one minus the function is not the complement" id)

                  Expect.isLessThanOrEqual
                      (ulpsFrom id (ElementaryVectors.ofHex reference))
                      64L
                      (sprintf "%s: the complement is" id)

              // A caller that holds `y = 1 - x` reads the mirrored function, and the two agree.
              let y = 1.0 / 1048576.0
              let mirrored = Special.regularizedBeta 0.5 5.0e5 y
              let complement = Special.regularizedBetaComplement 5.0e5 0.5 (1.0 - y)

              Expect.isLessThanOrEqual
                  (ElementaryVectors.ulpsApart mirrored complement)
                  16L
                  "I_y(b, a) is 1 - I_(1 - y)(a, b)"

              // The inverse reads a small upper probability as it is: at `1 - q` rounded to one the
              // plain inverse can only answer one.
              let q = 1.0e-20
              Expect.equal (Special.inverseRegularizedBeta 2.0 3.0 (1.0 - q)) 1.0 "1 - q is one"
              let x = Special.inverseRegularizedBetaComplement 2.0 3.0 q
              Expect.isTrue (x < 1.0 && x > 0.999) "the complement's inverse is short of one"

              Expect.isLessThanOrEqual
                  (abs (Special.regularizedBetaComplement 2.0 3.0 x - q))
                  (q * 1.0e-6)
                  "and the complement there is q, as nearly as an x beside one can say"

              // From a half up the two inverses are one function, bit for bit.
              for q in [ 0.5; 0.625; 0.999 ] do
                  Expect.equal
                      (ElementaryVectors.hex (Special.inverseRegularizedBetaComplement 7.5 0.75 q))
                      (ElementaryVectors.hex (Special.inverseRegularizedBeta 7.5 0.75 (1.0 - q)))
                      (sprintf "q = %g" q)

          testCase
              "the host's own gamma function answers other bits on at least one special vector, so the byte comparison sees a port that cheats"
          <| fun () ->
              // .NET has no gamma function of its own; the nearest host route is exp of a host-libm
              // Stirling sum. A port that swapped the pinned exp, ln or pow for the host's would be
              // caught the same way: some vector moves.
              let hostStirling (x: float) =
                  Math.Exp((x - 0.5) * Math.Log x - x + 0.5 * Math.Log(2.0 * Math.PI) + 1.0 / (12.0 * x))

              let differing =
                  SpecialVectors.cases
                  |> List.filter (fun c -> c.Op = "gamma")
                  |> List.choose (fun c ->
                      let x = c.Input |> List.head |> snd
                      let pinned = Special.gamma x

                      if x > 10.0 && not (Double.IsInfinity pinned) then
                          Some(pinned, hostStirling x)
                      else
                          None)
                  |> List.filter (fun (pinned, host) ->
                      BitConverter.DoubleToInt64Bits pinned <> BitConverter.DoubleToInt64Bits host)

              Expect.isNonEmpty differing "the host route and the pinned routine differ somewhere on the vectors"

          testCase "Special's source reaches no host mathematics call"
          <| fun () ->
              let source =
                  File.ReadAllText(Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.Elementary", "Special.fs")))

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.\w+|(?<![\w.])(exp|log|log10|log2|floor|ceil|round|truncate|abs|sin|cos|tan|sinh|cosh|tanh|atan|atan2|asin|acos)\s+[\w(]|\*\*"
                  )

              // Comment lines describe the mathematics in words; only code is held to the rule.
              let code =
                  source.Split('\n')
                  |> Array.filter (fun l -> not (l.TrimStart().StartsWith "//"))
                  |> String.concat "\n"

              let hits =
                  hostCall.Matches code
                  |> Seq.map (fun m -> m.Value)
                  |> Seq.distinct
                  |> List.ofSeq

              Expect.isEmpty
                  hits
                  "no host mathematics call in Special.fs (the square root is the F# operator over the premise's correctly rounded root; exp, ln and pow are Elementary's)"

          testCase "Special is declared once across the packages, in Fuaran.Compute.Elementary"
          <| fun () ->
              // Callers write `open Fuaran.Compute.Elementary` and `Special.gammaLn`; a second module of
              // the name in another package would shadow this one member by member (DECISIONS.md D74).
              let declaring =
                  Directory.GetFiles(Snapshots.repoFile "src", "*.fs", SearchOption.AllDirectories)
                  |> Array.filter (fun path ->
                      not (path.Contains(sprintf "%cobj%c" Path.DirectorySeparatorChar Path.DirectorySeparatorChar))
                      && not (path.Contains(sprintf "%cbin%c" Path.DirectorySeparatorChar Path.DirectorySeparatorChar)))
                  |> Array.filter (fun path ->
                      File.ReadAllLines path
                      |> Array.exists (fun l ->
                          Text.RegularExpressions.Regex.IsMatch(
                              l,
                              @"^\s*(module|type)\s+(private\s+|internal\s+)?Special\b"
                          )))
                  |> Array.map (fun path -> Path.GetFileName(Path.GetDirectoryName path) + "/" + Path.GetFileName path)
                  |> List.ofArray

              Expect.equal declaring [ "Fuaran.Compute.Elementary/Special.fs" ] "one declaration of Special" ]
