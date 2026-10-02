module Fuaran.Compute.Tests.ConformanceVacuityTests

// Phase 196 — vacuity measured, per law family, at the reference witness: the families THIS
// repository ships (Phase 259 carried this share of the Fuaran.Core repository's census here with
// the families; the kit's own share, and the census's pure derivation checks, stayed there).
//
// A `LawResult` records that a law HELD, not that it was REACHED. So every family the share ships is
// run once here, at the witnesses this repository certifies with, with the measurement kept: a
// family with no reference run is red, both directions, against the roster; every family reaches a
// NON-ZERO, non-starved count, which is what lets a consumer read a zero in its own census as a fact
// about its own witness rather than about the kit; and the families whose refusals a run could
// silently miss are held to a guard that reports it.
//
// The runs use the same seeds and iteration counts as the suites that certify each family, so this
// file measures the run the repository actually stands behind rather than a cheaper one.

open Expecto
open Fuaran.Core
open Fuaran.Compute

// ---------------------------------------------------------------------------
//  the reference run
// ---------------------------------------------------------------------------

/// One family's reference run: its roster id, the sample size it was driven over, and what it
/// answered. `Iterations` is the count the family's own sample is sized by — the iteration
/// argument for a drawn family, the corpus length for `constructThenEncodeLaws`, the search budget
/// for `hashFnAdversarialLaws`. The family knows which; this record carries the number.
type Run =
    { Id: string
      Iterations: int
      Results: LawResult list }

let private run id iterations results =
    { Id = id
      Iterations = iterations
      Results = results }

/// Every law family this share ships, run once. Evaluated at most once per process: several of
/// these are two- and three-hundred-iteration property runs.
let private runs =
    lazy
        ([ // Phase 281 — the registered pipeline query, at the size its own suite runs it.
           run "PipelineQueryConformance.laws" 100 (PipelineQueryConformance.laws 2810 100)
           // Phase 338 — the derived-column typing rule.
           run "DeriveTypingConformance.laws" 200 (DeriveTypingConformance.laws 3380 200)
           run
               "Conformance.transformLaws"
               LawVectorExport.iterations
               (Conformance.transformLaws
                   DataFrame.evalPipeline
                   (LawVectorExport.lawGen ())
                   LawVectorExport.seed
                   LawVectorExport.iterations)
           run "Conformance.aggregateParityLaws" 200 (Conformance.aggregateParityLaws 4242 200)
           run "Conformance.columnarOpLaws" 200 (Conformance.columnarOpLaws 4242 200)
           run
               "Conformance.columnarOpLawsWith"
               200
               (Conformance.columnarOpLawsWith ColumnOps.invert Conformance.columnarOpStreamGen 4242 200)
           run "Conformance.incrementalLaws" 200 (Conformance.incrementalLaws 4242 200)
           run
               "Conformance.incrementalLawsWith"
               200
               (Conformance.incrementalLawsWith
                   WitnessTakingFamiliesTests.incrementalPipelines
                   Conformance.columnarOpStreamGen
                   4242
                   200)
           run "Conformance.paramLaws" 200 (Conformance.paramLaws 7714 200)
           run "Conformance.schemaWalkLaws" 300 (Conformance.schemaWalkLaws 1121 300)
           run "Conformance.nowLaws" 150 (Conformance.nowLaws 1250 150)
           run "Conformance.slotParamLaws" 120 (Conformance.slotParamLaws 12500 120)
           // Phase 269 — the planner held to the reference as written.
           run "Conformance.plannerLaws" 200 (Conformance.plannerLaws 2690 200)
           run "IncrementalDelta.laws" 60 (IncrementalDelta.laws 7 60)
           // The SHIPPED row bound (9) and the sample size its own suite sweeps at. A narrower
           // bound is the family's documented go-red, not a census run.
           run "IncrementalDelta.lawsWith" 100 (IncrementalDelta.lawsWith 9 7 100) ]
        : Run list)


/// The family's own adequacy class, which is what decides how its run is read. Looked up rather
/// than passed, because the census is the single declaration and this file must not become a
/// second one.
let private classOf (id: string) : AdequacyClass =
    match KitRoster.census |> List.tryFind (fun (k, _) -> k = id) with
    | Some(_, k) -> k
    | None -> failwithf "%s has no census row (DataFrameFamilies.census) — the roster and the census disagree" id

/// The measured census: one `CaseCount` per family, sorted by id.
let cases () : (string * CaseCount) list =
    runs.Value
    |> List.map (fun r -> r.Id, SampleAdequacy.cases r.Id (classOf r.Id) r.Iterations r.Results)
    |> List.sortBy fst

/// Phase 223 — the drawn-refusal family in this share (one of the six that audit found), with the
/// dimensions it is `Guarded` over.
let private drawnRefusal: (string * string list) list =
    [ "Conformance.transformLaws", [ "accepted"; "refused" ] ]

/// ... and its reference run as a function of the seed, at the size the census runs it.
let private drawnRefusalRuns: (string * (int * (int -> LawResult list))) list =
    [ "Conformance.transformLaws",
      (LawVectorExport.iterations,
       fun seed ->
           Conformance.transformLaws DataFrame.evalPipeline (LawVectorExport.lawGen ()) seed LawVectorExport.iterations) ]

// ---------------------------------------------------------------------------

[<Tests>]
let vacuityTests =
    testList
        "Conformance.Vacuity"
        [

          testCase "every law family this share ships has a reference run, and no run names a phantom"
          <| fun _ ->
              // Both directions, for the reason the roster's own completeness check runs both: a
              // missing run is a family measured by nobody, and a run naming nothing is a census
              // cell for a family that no longer exists.
              let ran = runs.Value |> List.map (fun r -> r.Id) |> Set.ofList
              let rostered = Set.ofList KitRoster.ids

              Expect.isEmpty
                  (Set.difference rostered ran |> Set.toList)
                  "these law families have no reference run in ConformanceVacuityTests — add one in the commit that ships the family, or its census cell reads `unmeasured` forever"

              Expect.isEmpty (Set.difference ran rostered |> Set.toList) "these reference runs name no roster family"

          testCase "the reference run is green — a red law makes its count meaningless"
          <| fun _ ->
              // The counts below are only evidence if the laws they count held. A failing law here
              // is not this file's finding to report (the owning suite reports it properly); it is
              // the reason to stop trusting the census.
              let failed =
                  [ for r in runs.Value do
                        for l in r.Results do
                            if not l.Passed then
                                yield sprintf "%s — %s: %A" r.Id l.Law l.Counterexample ]

              Expect.isEmpty failed (sprintf "the reference run is not green:\n%s" (String.concat "\n" failed))

          testCase "no family is vacuous at the reference witness"
          <| fun _ ->
              // The claim the whole file exists to make, and the one that gives a CONSUMER's zero
              // its meaning: a family that reads `vacuous` over there is that consumer's witness,
              // never this kit.
              let vacuous =
                  cases ()
                  |> List.filter (snd >> SampleAdequacy.isVacuous)
                  |> List.map (fun (id, c) -> id + " → " + SampleAdequacy.renderCases c)

              Expect.isEmpty
                  vacuous
                  (sprintf
                      "these families certify nothing at this repository's own reference witness:\n%s"
                      (String.concat "\n" vacuous))

          // ---- Phase 220: the refusable-family audit ----

          testCase "the refusal audit covers the roster, both directions, once per family, with its evidence"
          <| fun _ ->
              // The audit is data so the next audit can diff it; a family missing from it is a
              // family nobody asked the question of, and a row naming nothing is a verdict about a
              // family that no longer exists.
              let audited = KitRoster.refusalAudit |> List.map (fun a -> a.Family)
              let rostered = Set.ofList KitRoster.ids

              Expect.isEmpty
                  (Set.difference rostered (Set.ofList audited) |> Set.toList)
                  "these law families have no refusal-audit row (Families.refusalAudit or DataFrameFamilies.refusalAudit) — audit each in the commit that ships it"

              Expect.isEmpty
                  (Set.difference (Set.ofList audited) rostered |> Set.toList)
                  "these refusal-audit rows name no roster family"

              Expect.equal (List.length audited) (Set.count (Set.ofList audited)) "one row per family"

              for a in KitRoster.refusalAudit do
                  Expect.isTrue (a.Why.Trim().Length > 10) (sprintf "%s carries no usable evidence" a.Family)

          testCase "every family whose refusals a run can silently miss is Guarded"
          <| fun _ ->
              // The PROPERTY, read off the audit and the census rather than off a list of families:
              // a `Drawn` row is a refusal population a run can miss while every law stays green, and
              // a `Guarded` census class is what reports that. So a family added later with a drawn
              // refusal and no guard fails here without anyone having to remember to list it.
              //
              // Phase 220 shipped this as a ratchet naming six permitted violators; Phase 223 guarded
              // all six and emptied it, so there are no exceptions left to name.
              let unguardedDrawn =
                  KitRoster.refusalAudit
                  |> List.filter (fun a -> a.Population = Families.Drawn)
                  |> List.filter (fun a ->
                      match classOf a.Family with
                      | Guarded _ -> false
                      | Unconditional _ -> true)
                  |> List.map (fun a -> a.Family)

              Expect.isEmpty
                  unguardedDrawn
                  "these families have a refusal population a run can silently miss (audited `Drawn`) and no adequacy guard — guard them, or the census reports an unguarded pass"


          // ---- Phase 223: the drawn-refusal family in this share ----

          testCase "the drawn-refusal family is Guarded, and reached at the reference witness"
          <| fun _ ->
              let measured = cases ()

              for id, dims in drawnRefusal do
                  Expect.equal
                      (KitRoster.tryRefusal id |> Option.map (fun a -> a.Population))
                      (Some Families.Drawn)
                      (sprintf "%s is audited Drawn" id)

                  Expect.equal (classOf id) (Guarded dims) (sprintf "%s is censused Guarded" id)

                  Expect.equal
                      (KitRoster.adequacyToken measured id)
                      "guarded-reached"
                      (sprintf "%s reached every guarded dimension at the reference witness" id)

          testCase "each kit reference generator reaches the refused branch on every seed tried, at the default size"
          <| fun _ ->
              // The stratification half, proven rather than asserted: the same run the census is
              // measured from, over twenty seeds that are not the reference seed. A guard that fired
              // here would be the intermittent failure the stratification exists to rule out, so
              // every seed must read `guarded-reached` — never `guarded-starved`.
              let seeds = [ 1..20 ] |> List.map (fun k -> k * 7919 + 3)

              for id, (iterations, runAt) in drawnRefusalRuns do
                  for seed in seeds do
                      let measured = [ id, SampleAdequacy.cases id (classOf id) iterations (runAt seed) ]

                      Expect.equal
                          (KitRoster.adequacyToken measured id)
                          "guarded-reached"
                          (sprintf "%s at seed %d, %d iterations" id seed iterations) ]
