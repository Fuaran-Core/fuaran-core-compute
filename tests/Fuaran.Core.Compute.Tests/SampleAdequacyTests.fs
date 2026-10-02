module Fuaran.Core.Tests.SampleAdequacyTests

// Phase 121 — the sample-adequacy guard at the families THIS repository ships, and the
// census-completeness check over their share of the roster (Phase 259: the instances below and the
// census half were carried from the Fuaran.Core repository's suite; the guard's own behaviour is
// the kit's, and is certified there).
//
// Every test below that asserts green is paired with one that perturbs the sample and asserts red,
// because a coverage guard which cannot go red is exactly the thing it exists to detect.

open System
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------

let private cx (r: LawResult) =
    match r.Counterexample with
    | Some c -> c
    | None -> failtestf "law %s carried no counterexample" r.Law

let private adequacyLaws (rs: LawResult list) =
    rs |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

[<Tests>]
let motivatingInstanceTests =
    testList
        "SampleAdequacy.instances"
        [

          testCase "reverting Phase 115's table widening turns the equivalence family's guard red"
          <| fun _ ->
              // Phase 115 raised the equivalence family's generated tables from one-to-five rows to
              // one-to-nine, having measured that most held ONE row, so no tie between a named and
              // an unnamed row ever arose and a merge with no stability tiebreak passed every seed.
              // Nothing was watching the table width; this is that watch, and this is its teeth.
              let span (rs: LawResult list) =
                  rs |> List.find (fun r -> r.Law.Contains "spans the source rows range")

              for seed in [ 1; 7; 99; 20260821 ] do
                  Expect.isTrue
                      (span (IncrementalDelta.lawsWith 9 seed 60)).Passed
                      (sprintf "seed %d: the shipped bound spans the width the order laws read" seed)

                  let narrowed = span (IncrementalDelta.lawsWith 5 seed 60)

                  Expect.isFalse narrowed.Passed (sprintf "seed %d: the pre-115 bound must fail the span demand" seed)

                  Expect.stringContains (cx narrowed) "at least 7" "and it names the width the laws need"

          testCase "the equivalence family's guard is green at the shipped bound, across seeds"
          <| fun _ ->
              for seed in [ 1; 7; 99; 20260821 ] do
                  for r in adequacyLaws (IncrementalDelta.laws seed 60) do
                      Expect.isTrue r.Passed (sprintf "seed %d — %s: %A" seed r.Law r.Counterexample)

          testCase "the guard reports the refresh classes the equivalence family's laws branch on"
          <| fun _ ->
              // A guard that passed while demanding nothing would be worse than none, so the
              // dimension's own vocabulary is pinned here rather than left to the demand list.
              let verdicts =
                  IncrementalDelta.demands
                  |> List.tryPick (function
                      | ReachesEvery("refresh class", vs, _) -> Some vs
                      | _ -> None)

              Expect.equal
                  verdicts
                  (Some
                      [ "declined"
                        "row-restricted"
                        "group-restricted"
                        "merged-order-restricted"
                        "window-restricted"
                        "partition-global-window-restricted"
                        "relation-filtered-restricted"
                        "top-n-restricted"
                        "group-tail-restricted" ])
                  "every class the family's laws distinguish is demanded"

          // ---- Phase 212: a ROW-LOCAL step reading a CROSS-ROW column ----

          testCase "the guard demands the cross-row column read, per producer class"
          <| fun _ ->
              // Pinned here for the same reason the refresh-class vocabulary above is: a demand
              // whose verdicts quietly collapsed to one would pass while demanding less. The axis is
              // the PRODUCER class because that is where the defect lives — a bounded frame moves
              // one neighbour's cell, a partition-global one moves every cell in the partition, and
              // an evaluator can be right about the first and wrong about the second.
              let verdicts =
                  IncrementalDelta.demands
                  |> List.tryPick (function
                      | ReachesEvery("cross-row column read", vs, _) -> Some vs
                      | _ -> None)

              Expect.equal
                  verdicts
                  (Some
                      [ "partition-global window read row-locally"
                        "bounded-frame window read row-locally" ])
                  "both cross-row producer classes are demanded"

          testCase "the corpus as it stood BEFORE Phase 212 fails the cross-row-read demand"
          <| fun _ ->
              // The go-red, stated against the actual history rather than against a perturbation.
              // Every window-bearing pipeline the corpus carried at `0.28.0` either ENDED with the
              // window or handed it to a `GroupBy`, and neither reads the per-row cache — which is
              // why three phases studied this code and none saw the defect `v0.26.0` published. The
              // samples below are those two shapes; the demand must refuse them.
              let footprint =
                  { SourceRows = 8
                    ResultRows = 8
                    Recompute = RowsRecomputed 3 }

              let sample (p: Transform list) =
                  { Seed = 1
                    Iteration = 0
                    Pipeline = p
                    Strategy = RowLocal
                    Prime = footprint
                    Full = footprint
                    Refresh = footprint
                    Equivalent = true
                    PrimeEquivalent = true
                    Edit = "changeFirstA" }

              let cumul: WindowSpec =
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }

              let lag = { cumul with Fn = Lag; As = "prev" }

              let demand =
                  IncrementalDelta.demands
                  |> List.filter (function
                      | ReachesEvery("cross-row column read", _, _) -> true
                      | _ -> false)

              let law xs =
                  match SampleAdequacy.check "IncrementalDelta" 1 demand xs with
                  | [ r ] -> r
                  | rs -> failtestf "expected exactly one cross-row-read law, got %d" (List.length rs)

              // The pre-212 shapes: a window LAST, and a window handed to a `GroupBy`.
              let pre212 =
                  [ sample [ Filter(Binary(Gt, Col "a", Lit(Int -5))); Window cumul ]
                    sample [ Window lag ]
                    sample
                        [ Window cumul
                          GroupBy([ "b" ], [ { Name = "mx"; Fn = Max; Of = "run" } ])
                          Derive("mxn", Binary(Add, Col "mx", Lit(Int 1))) ] ]

              let before = law pre212
              Expect.isFalse before.Passed "the pre-212 corpus reaches neither producer class"

              Expect.stringContains
                  (cx before)
                  "partition-global window read row-locally"
                  "and the counterexample names the class it never reached"

              Expect.stringContains (cx before) "WIDEN THE GENERATOR" "with the standing remedy"

              // One shape per producer class is enough to satisfy it — and the partition-global one
              // is reached THROUGH A RENAME, which is the case a demand matching the window's own
              // output name would report as missing.
              let after =
                  law (
                      pre212
                      @ [ sample [ Window lag; Filter(Binary(Ge, Col "prev", Lit(Int -3))) ]
                          sample
                              [ Window cumul
                                Project [ "id", "id"; "run", "v" ]
                                Derive("d", Binary(Add, Col "v", Lit(Int 1))) ] ]
                  )

              Expect.isTrue after.Passed "a row-local read of each producer's column satisfies it"

          testCase "and a GROUP aggregate read row-locally does not satisfy it"
          <| fun _ ->
              // The boundary, because it is the one an over-eager classifier gets wrong. The group
              // table's own stability condition is "this group's aggregates were recomputed", which
              // is sound, so a row-local step over a group table cannot carry this defect — and a
              // demand that counted it would report the class reached by shapes `33` and `37`, which
              // were in the corpus throughout the whole life of the bug.
              let footprint =
                  { SourceRows = 8
                    ResultRows = 3
                    Recompute = GroupsRecomputed(5, 2) }

              let sample (p: Transform list) =
                  { Seed = 1
                    Iteration = 0
                    Pipeline = p
                    Strategy = RowLocalThenGroups
                    Prime = footprint
                    Full = footprint
                    Refresh = footprint
                    Equivalent = true
                    PrimeEquivalent = true
                    Edit = "changeFirstA" }

              let demand =
                  IncrementalDelta.demands
                  |> List.filter (function
                      | ReachesEvery("cross-row column read", _, _) -> true
                      | _ -> false)

              let r =
                  SampleAdequacy.check
                      "IncrementalDelta"
                      1
                      demand
                      [ sample
                            [ GroupBy([ "b" ], [ { Name = "s"; Fn = Sum; Of = "a" } ])
                              Derive("mean2", Binary(Mul, Col "s", Lit(Int 2)))
                              Filter(Binary(Ge, Col "mean2", Lit(Int -20))) ] ]
                  |> List.head

              Expect.isFalse r.Passed "a group aggregate read by the group tail reaches neither class"

          testCase "the cross-row-read classes are reached by a share that is not a coin flip"
          <| fun _ ->
              // The figure, measured rather than asserted — the same discipline `IncrementalTests`
              // applies to the refresh classes, and the same 7% floor, for the same reason: the two
              // classes that once sat at 5.2% and 5.4% made the guard's verdict a coin flip, and the
              // remedy the guard's own counterexample forbids is re-seeding. Measured over this
              // sweep when Phase 212 landed: 8.88% and 8.93%.
              //
              // A narrower sweep than `IncrementalTests`' 300 seeds, deliberately — this is a margin
              // check on a share, and the firing check over the full seed range is that file's.
              let samples =
                  [ for bound in [ 9; 12 ] do
                        for seed in 1..40 do
                            yield! IncrementalDelta.samplesWith bound seed 100 ]

              let verdicts, classify =
                  IncrementalDelta.demands
                  |> List.tryPick (function
                      | ReachesEvery("cross-row column read", vs, f) -> Some(vs, f)
                      | _ -> None)
                  |> Option.defaultWith (fun () -> failtest "the family no longer declares a cross-row-read demand")

              let tagged = samples |> List.map classify
              let total = List.length samples

              for v in verdicts do
                  let n = tagged |> List.filter (List.contains v) |> List.length
                  let share = 100.0 * float n / float total

                  Expect.isGreaterThan
                      share
                      7.0
                      (sprintf "cross-row column read %s was reached by only %.2f%% of %d samples" v share total)

          // The Phase 100 instance — 150 halting trials out of 150, the folding branch never
          // executed — has its go-red proof in `FoldConfluenceTests`: an order-sensitive witness
          // with a blind footprint never conflicts, so the guard reports `halted=0` and refuses to
          // let a vacuous halt law read as a certification. It is not duplicated here.

          testCase "a family whose sample misses a verdict fails even though its laws all hold"
          <| fun _ ->
              // The whole claim, in one assertion: green laws plus a red guard. The blind-footprint
              // witness's halt-determinism law is TRUE (nothing halted, so nothing halted wrongly)
              // and the guard is what stops that reading as evidence.
              let narrowed = IncrementalDelta.lawsWith 1 7 60

              let core =
                  narrowed |> List.filter (fun r -> not (r.Law.StartsWith "sample adequacy"))

              Expect.isTrue
                  (core |> List.forall (fun r -> r.Passed))
                  (sprintf "the laws themselves still hold over one-row tables: %A" core)

              Expect.isFalse
                  (adequacyLaws narrowed |> List.forall (fun r -> r.Passed))
                  "and the guard refuses to certify the sample they held over" ]

// ---------------------------------------------------------------------------
//  Phase 321 — the decimal reach of every kit-drawn family that ranges over cells
// ---------------------------------------------------------------------------

[<Tests>]
let decimalReachTests =
    testList
        "SampleAdequacy.decimal"
        [ testCase "every kit-drawn family that ranges over cells reaches a decimal, and says so"
          <| fun _ ->
              let decimalGuards (rs: LawResult list) =
                  adequacyLaws rs |> List.filter (fun r -> r.Law.Contains "decimal")

              for family, rs in
                  [ "aggregateParityLaws", Conformance.aggregateParityLaws 4242 200
                    "columnarOpLaws", Conformance.columnarOpLaws 4242 200
                    "schemaWalkLaws", Conformance.schemaWalkLaws 1121 300
                    "paramLaws", Conformance.paramLaws 4242 200
                    "IncrementalDelta.laws", IncrementalDelta.laws 7 60 ] do
                  let guards = decimalGuards rs
                  Expect.isNonEmpty guards (sprintf "%s carries a decimal guard" family)

                  for g in guards do
                      Expect.isTrue g.Passed (sprintf "%s: %s — %A" family g.Law g.Counterexample)

          testCase "the incremental family's decimal guard goes red over the corpus as it stood before Phase 321"
          <| fun _ ->
              // The go-red, stated against the history: the same seed's samples with every pipeline
              // that reads the decimal column dropped are the sample the family drew before this
              // phase, and the guard must refuse it rather than read green by omission.
              let demand =
                  IncrementalDelta.demands
                  |> List.filter (function
                      | ReachesEvery("decimal column", _, _) -> true
                      | _ -> false)

              let readsMoney (s: IncrementalSample) =
                  s.Pipeline
                  |> List.exists (function
                      | GroupBy(_, aggs) -> aggs |> List.exists (fun a -> a.Of = "m")
                      | Sort keys -> keys |> List.exists (fun (k, _) -> k = Slot.Lit "m")
                      | _ -> false)

              let xs = IncrementalDelta.samples 7 60

              match SampleAdequacy.check "IncrementalDelta" 7 demand xs with
              | [ r ] -> Expect.isTrue r.Passed "the shipped sample reaches both decimal verdicts"
              | rs -> failtestf "expected one decimal-column law, got %d" (List.length rs)

              match SampleAdequacy.check "IncrementalDelta" 7 demand (xs |> List.filter (readsMoney >> not)) with
              | [ r ] ->
                  Expect.isFalse r.Passed "a sample with no decimal read is refused"
                  Expect.stringContains (cx r) "decimal aggregate" "and it names the verdict it never reached"
              | rs -> failtestf "expected one decimal-column law, got %d" (List.length rs)

          testCase "a kit family's decimal guard goes red on a sample that drew no decimal"
          <| fun _ ->
              for family, dimension in
                  [ "Conformance.aggregateParityLaws", "decimal column"
                    "Conformance.columnarOpLaws", "decimal cell"
                    "Conformance.schemaWalkLaws", "decimal step"
                    "Conformance.paramLaws", "decimal param" ] do
                  let hollow = SampleAdequacy.reached family dimension 4242 [ "decimal", 0 ]
                  Expect.isFalse hollow.Passed (sprintf "%s: a zero count is red" family)
                  Expect.stringContains (cx hollow) "decimal" "and it names what was never drawn" ]

// ---------------------------------------------------------------------------
//  census completeness — the half a declaration cannot check about itself
// ---------------------------------------------------------------------------

/// Every law family the kit ships — the roster `Fuaran.Core.Families` declares, which is itself
/// held to REFLECTION OVER RETURN TYPE across the shipped assembly by `ConformanceFamiliesTests`.
///
/// Phase 184 replaced what stood here, and the replacement is the point rather than a tidy-up.
/// This function used to do its own reflection over method NAMES — anything ending in `Laws` /
/// `LawsWith`, plus the two bare `laws` spellings — over four named modules. A naming convention
/// is not what a law family IS, and three families are not spelled that way: `opAlgebra`,
/// `reducer` and `compositionPilot` were invisible here, and therefore absent from the census,
/// and therefore absent from the roster a consumer's conformance projection quantifies over. Two
/// of the three are the families `certify` and `certifyStream` are built from.
///
/// Reading the roster keeps this file's completeness claim exactly as strong as it was and moves
/// the derivation to one place: a family answers with `LawResult list` or it is not a family, and
/// no module list is restated here for a new module to fall outside of.
let private shippedFamilies () : string list = KitRoster.ids

[<Tests>]
let censusTests =
    testList
        "SampleAdequacy.census"
        [

          testCase "every law family the kit ships is classified in the census"
          <| fun _ ->
              // This is the half the census structurally cannot do for itself. A declaration
              // quantifies over what it names, so a family nobody enrolled produces no finding at
              // any grade — which is how a store can hold nine files while the class holds twelve.
              // The kit's declared roster closes it — itself held to reflection over the shipped
              // assembly BY RETURN TYPE — so a family added without answering the adequacy
              // question fails to ship rather than passing silently.
              let declared = KitRoster.census |> List.map fst |> Set.ofList
              let shipped = shippedFamilies ()
              let unclassified = shipped |> List.filter (fun f -> not (Set.contains f declared))

              Expect.isEmpty
                  unclassified
                  (sprintf
                      "these law families are not in any census share (SampleAdequacy.census, DataFrameFamilies.census) — declare each as Guarded or Unconditional (with the reason): %A"
                      unclassified)

          testCase "the census names no family the kit no longer ships"
          <| fun _ ->
              // The other direction, and it matters for the same reason: a row for a family that
              // was renamed or removed reads as coverage while covering nothing.
              let shipped = shippedFamilies () |> Set.ofList

              let stale =
                  KitRoster.census
                  |> List.map fst
                  |> List.filter (fun f -> not (Set.contains f shipped))

              Expect.isEmpty stale (sprintf "these census rows name no shipped law family: %A" stale)

          testCase "the census carries no duplicate row and no empty reason"
          <| fun _ ->
              let names = KitRoster.census |> List.map fst

              Expect.equal
                  (List.length (List.distinct names))
                  (List.length names)
                  "a family classified twice could be classified two ways"

              for name, cls in KitRoster.census do
                  match cls with
                  | Guarded dims ->
                      Expect.isNonEmpty dims (sprintf "%s is Guarded but names no dimension" name)

                      for d in dims do
                          Expect.isTrue (d.Trim() <> "") (sprintf "%s names an empty dimension" name)
                  | Unconditional why ->
                      Expect.isTrue
                          (why.Trim().Length > 10)
                          (sprintf
                              "%s is Unconditional with no usable reason — the reason is what lets the next reader CHECK the classification rather than trust it"
                              name)

          testCase "every family the census calls Guarded actually emits an adequacy law"
          <| fun _ ->
              // The classification is a claim about the code, so it is checked against the code for
              // the families that can be run without a domain witness. The witness-taking ones are
              // checked by their own suites, whose `expectGreen` now covers the guard they gained.
              let emits (rs: LawResult list) = not (List.isEmpty (adequacyLaws rs))

              Expect.isTrue (emits (IncrementalDelta.laws 7 20)) "IncrementalDelta.laws"

          testCase "no family the census calls Unconditional quietly emits one instead"
          <| fun _ ->
              // The inverse claim, over the seed/iteration-only families — a family that gained a
              // guard without moving its census row would leave the census describing the old code.
              let emits (rs: LawResult list) = not (List.isEmpty (adequacyLaws rs))

              for name, run in
                  [ "aggregateParityLaws", Conformance.aggregateParityLaws
                    "columnarOpLaws", Conformance.columnarOpLaws
                    "incrementalLaws", Conformance.incrementalLaws
                    "paramLaws", Conformance.paramLaws ] do
                  match KitRoster.census |> List.tryFind (fun (n, _) -> n = "Conformance." + name) with
                  | Some(_, Unconditional _) ->
                      Expect.isFalse
                          (emits (run 4242 20))
                          (sprintf "%s emits an adequacy law but the census calls it Unconditional" name)
                  | Some(_, Guarded _) ->
                      Expect.isTrue
                          (emits (run 4242 20))
                          (sprintf "%s is censused Guarded but emits no adequacy law" name)
                  | None -> failtestf "%s is missing from the census" name ]
