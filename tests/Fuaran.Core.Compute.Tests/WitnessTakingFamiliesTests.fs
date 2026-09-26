module Fuaran.Core.Tests.WitnessTakingFamiliesTests

// Phase 246 — the witness-taking law families, at the columnar pair this repository ships
// (`columnarOpLawsWith`, `incrementalLawsWith`). Each takes the DOMAIN'S generator rather than the
// kit's fixtures, so a domain certifies its own stream; the tests below run each over the kit's
// reference generator, show the guard that starves when a generator never reaches a dimension,
// and hold the roster's witness column to what the entry points take. Carried from the Fuaran.Core
// repository's suite with the families (Phase 259); the seam families of the same phase
// (`capabilityLawsWith`, `queryLawsWith`, `capabilityPipelineLawsWith`) stayed there.
//
// The reference pipelines here are also what `ConformanceVacuityTests` runs `incrementalLawsWith`
// over for the roster's `cases` column, which is why this file compiles before it.

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  the columnar pair at a caller's generator
// ---------------------------------------------------------------------------

/// The pipelines a domain might run over the kit's reference table: a filter, a derive, a
/// projection that drops `b`, and a group-by.
let incrementalPipelines: Transform list list =
    [ [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
      [ Derive("d", Binary(Add, Col "a", Lit(Int 1))) ]
      [ Project [ "a", "a" ] ]
      [ GroupBy([ "a" ], [ { Name = "s"; Fn = Sum; Of = "b" } ]) ] ]

let private failing (rs: LawResult list) =
    rs |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law)

let private allGreen (what: string) (rs: LawResult list) =
    for r in rs do
        Expect.isTrue r.Passed (sprintf "%s — %s: %A" what r.Law r.Counterexample)

let private guardLaw (family: string) (dimension: string) =
    SampleAdequacy.lawPrefix family
    + "the sample reached every "
    + dimension
    + " the laws distinguish"

[<Tests>]
let tests =
    testList
        "Phase 246 — witness-taking law families"
        [

          testCase "columnarOpLawsWith certifies the kit's reference StreamGen green"
          <| fun _ ->
              allGreen
                  "columnarOpLawsWith"
                  (Conformance.columnarOpLawsWith ColumnOps.invert Conformance.columnarOpStreamGen 2463 200)

          testCase "incrementalLawsWith certifies the reference pipelines green at the kit's StreamGen"
          <| fun _ ->
              allGreen
                  "incrementalLawsWith"
                  (Conformance.incrementalLawsWith incrementalPipelines Conformance.columnarOpStreamGen 2464 200)

          testCase "a generator that never edits a value starves incrementalLawsWith"
          <| fun _ ->
              let appendsOnly: StreamGen<ColumnOp, Table> =
                  { State0 = Conformance.columnarOpStreamGen.State0
                    Op = fun r -> AppendRows [ [ "a", Int 1; "b", Int 2 ] ], r }

              Expect.equal
                  (failing (Conformance.incrementalLawsWith incrementalPipelines appendsOnly 2464 50))
                  [ guardLaw "Conformance.incrementalLawsWith" "value edit" ]
                  "exactly the value-edit guard is red"

          testCase "the witness-taking families take the domain's witness — the roster says so"
          <| fun _ ->
              for id, witness in
                  [ "Conformance.columnarOpLawsWith", [ "StreamGen" ]
                    "Conformance.incrementalLawsWith", [ "StreamGen" ] ] do
                  Expect.equal
                      (KitRoster.tryFind id |> Option.map (fun f -> f.Witness))
                      (Some witness)
                      (sprintf "%s names the witness it takes" id) ]
