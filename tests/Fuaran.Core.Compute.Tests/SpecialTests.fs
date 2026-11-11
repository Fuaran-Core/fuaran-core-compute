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
