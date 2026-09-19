module Fuaran.Core.Tests.IncrementalGroupByTests

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 202 — the steps AFTER a maintained group-by.
//
// `Incremental.plan` declined every pipeline whose `GroupBy` was not its last step, reporting
// `AggregateStepNotLast`: "its output rows are groups, so a delta over the source rows says
// nothing about a delta over the group table". It no longer does. The premise was false in one
// word — the delta says nothing about the group table's VALUES, but `MaintainGroups` has always
// computed, and the state has always recorded, WHICH GROUPS IT TOUCHED. That is a delta over the
// group table, and it is the only thing the steps after the group-by need.
//
// So the tail is the SAME restricted walk one frame along: the group table is a frame of rows in
// first-appearance order, and a group whose aggregates were REUSED has a row byte-identical to the
// one the tail last read. A `Having` is a `Filter` after a `GroupBy` (there is no `Having` verb),
// which is what this admits.
//
// ---- the premise the shard carried, and what measuring it found --------------------------
//
// The phase was specified as "decomposable aggregates enumerated in code and doc; the rest declined
// explicitly" — sum, count, min/max with a tombstone re-scan, mean via sum+count. That design
// presumes the seam maintains a RUNNING ACCUMULATOR per group, which is where decomposability
// matters and where a deletion from a min/max group is hard.
//
// It does not. `groupStep` recomputes an affected group's aggregate FROM THAT GROUP'S MEMBER ROWS,
// in full, through the reference evaluator's own `DataFrame.aggregateCells`. There is no
// accumulator to repair, so there is no deletion problem, no tombstone, and no decomposability
// requirement: `Median` and `StdDev` — which are not decomposable in any running sense — are
// maintained exactly as `Sum` is. The decline class the shard asked for is EMPTY BY CONSTRUCTION.
//
// `every aggregate function is maintained past a tail` below is that finding as an assertion
// rather than as prose: it enumerates `AggFn` case by case and goes red the day one of them stops
// being maintained. Deleting it would silently restore the question this phase answered.
// ---------------------------------------------------------------------------

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

let private table (rows: (string * Cell * Cell) list) : Table =
    { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
      Columns =
        [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
          Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
          Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

/// Prime over `before`, diff to `after`, refresh — the delta is truthful by construction.
let private step (pipeline: Transform list) (before: Table) (after: Table) =
    let state = ok (Incremental.primeOn idw pipeline before)
    let delta = ok (Delta.diff idw before after)
    ok (Incremental.refreshOn idw pipeline state delta after)

/// The two assertions every behavioural case below makes: the maintained answer is the REFERENCE
/// answer (the reference semantics is the full evaluation, always), and the refresh was RESTRICTED
/// rather than a fall-back wearing the right result. A fall-back is always correct, so equality
/// alone would pass on a phase that shipped nothing.
let private expectRestrictedAndEqual (pipeline: Transform list) (after: Table) (s: IncrementalEval) =
    Expect.equal (Ok s.Output) (DataFrame.evalPipeline pipeline after) "maintained result = reference result"

    match s.Footprint.Recompute with
    | RowsRecomputed _
    | GroupsRecomputed _
    | ReusedPrior -> ()
    | other -> failtestf "expected a restricted refresh, got %A" other

// ---- the fixtures ----

/// A `Having`: count and sum per group `b`, keeping the groups of more than one row. This is the
/// shape the decline named, and pipeline `7` of the conformance corpus.
let private having =
    [ GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ])
      Filter(Binary(Gt, Col "n", Lit(Int 1))) ]

/// A group-by feeding a derived column over its own aggregates — the shape whose tail actually
/// EVALUATES something, so the tail cache is observable in the footprint.
let private groupThenDerive =
    [ GroupBy([ "b" ], [ agg "s" Sum "a" ])
      Derive("twice", Binary(Mul, Col "s", Lit(Int 2))) ]

/// A group-by feeding a top-N over the GROUPS — Phase 207's verb one frame along.
let private groupThenTopOne =
    [ GroupBy([ "b" ], [ agg "s" Sum "a" ])
      Transform.sortBy [ "s", Desc ]
      Transform.limit 1 0 ]

/// A second aggregating step: the one shape this phase declines.
let private twoGroupBys =
    [ GroupBy([ "b" ], [ agg "n" Count "a" ])
      GroupBy([ "n" ], [ agg "m" Count "n" ]) ]

/// Three groups: `b = 0` holds r0/r1, `b = 1` holds r2/r3, `b = 2` holds r4 alone.
let private baseRows =
    [ "r0", Int 1, Int 0
      "r1", Int 3, Int 0
      "r2", Int 5, Int 1
      "r3", Int 7, Int 1
      "r4", Int 9, Int 2 ]

// ---- a seeded generator, for the differential sweep ----

/// A small deterministic LCG. Local rather than drawn from the conformance layer: this file is a
/// unit suite and must not make a law family's generator part of its contract.
let private nextRand (s: int) : int * int =
    let s' = (s * 1103515245 + 12345) &&& 0x3FFFFFFF
    s', s'

let private drawRows (seed: int) (n: int) : (string * Cell * Cell) list * int =
    let rec go i s acc =
        if i >= n then
            List.rev acc, s
        else
            let a, s1 = nextRand s
            let b, s2 = nextRand s1
            go (i + 1) s2 (("r" + string i, Int(a % 11 - 5), Int(b % 3)) :: acc)

    go 0 seed []

// ---- the go-red falsifier, carried in the file rather than run once by hand ----

/// The reuse rule the tail walk actually applies: a group's cached tail cells are reusable only
/// when `groupStep` REUSED that group's aggregates. The obvious weaker rule — "reuse when the group
/// existed before" — is what this models, and `the tail's reuse condition is load-bearing` below
/// shows it produces the WRONG answer on a case the shipped rule gets right.
///
/// It is stated as a function of the prior result so the test can compute what that model would
/// have emitted, rather than asserting against a number someone typed.
let private reuseIfGroupExisted (priorOutput: Table) (groupKey: Cell) (column: string) : Cell option =
    let cellsOf name =
        priorOutput.Columns
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.map (fun c -> c.Cells)

    match cellsOf "b", cellsOf column with
    | Some keys, Some vals ->
        List.zip keys vals
        |> List.tryFind (fun (k, _) -> k = groupKey)
        |> Option.map snd
    | _ -> None

[<Tests>]
let groupByTailTests =
    testList
        "IncrementalGroupByTail"
        [

          // ================= the plan =================

          testCase "a group-by with a step after it is admitted, not declined"
          <| fun _ ->
              let p = Incremental.plan having

              Expect.equal
                  p.Steps
                  [ MaintainGroups([ "b" ], [ "n"; "s" ]); PropagateRows ]
                  "the group-by declares what it maintains and the Having propagates over the groups"

              Expect.equal
                  p.Strategy
                  RowLocalThenGroups
                  "a tail does not change the STRATEGY — it is still groups, then rows"

              Expect.isTrue (Incremental.isIncremental p) "isIncremental agrees"

          testCase "the position that used to decline is admitted at every length of tail"
          <| fun _ ->
              for pipeline in [ having; groupThenDerive; groupThenTopOne ] do
                  match (Incremental.plan pipeline).Strategy with
                  | ReferenceOnly r -> failtestf "%A declined: %s" pipeline (Incremental.reasonString r)
                  | _ -> ()

          testCase "a SECOND group-by declines by name, as data"
          <| fun _ ->
              let p = Incremental.plan twoGroupBys

              Expect.equal
                  p.Steps
                  [ MaintainGroups([ "b" ], [ "n" ]); FallBack(AggregateStepRepeated "groupBy") ]
                  "the first is maintained; the second is refused in its own case"

              Expect.equal
                  p.Strategy
                  (ReferenceOnly(AggregateStepRepeated "groupBy"))
                  "the pipeline's strategy carries the refusal"

              Expect.isFalse (Incremental.isIncremental p) "isIncremental agrees"

              Expect.stringContains
                  (Incremental.reasonString (AggregateStepRepeated "groupBy"))
                  "once per pipeline"
                  "the reason says what the limit is, not merely that there is one"

          testCase "the decline is a REFUSAL, never a silent full re-evaluation"
          <| fun _ ->
              // A declined pipeline still answers correctly — through the reference evaluator — and
              // the footprint names why. Silence would be the defect: the same right answer with no
              // way to tell it was not maintained.
              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 100, b else i, a, b)
                  )

              let s = step twoGroupBys before after

              Expect.equal
                  (Ok s.Output)
                  (DataFrame.evalPipeline twoGroupBys after)
                  "the answer is still the reference's"

              match s.Footprint.Recompute with
              | FullRecompute(_, AggregateStepRepeated "groupBy") -> ()
              | other -> failtestf "expected a NAMED fall-back, got %A" other

          testCase "the retired reason still renders, and nothing produces it"
          <| fun _ ->
              // `AggregateStepNotLast` is retained rather than removed — dropping a case from a
              // published union breaks every consumer that matches on it, and a footprint recorded
              // under `0.26.1` still has to read. What must be true now is that `plan` never mints
              // one again.
              Expect.stringContains
                  (Incremental.reasonString (AggregateStepNotLast "groupBy"))
                  "groupBy"
                  "a stored reason still renders"

              let everyShape =
                  [ having
                    groupThenDerive
                    groupThenTopOne
                    twoGroupBys
                    [ GroupBy([ "b" ], [ agg "n" Count "a" ]) ]
                    [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                      GroupBy([ "b" ], [ agg "n" Count "a" ])
                      Transform.sortBy [ "n", Asc ] ] ]

              for pipeline in everyShape do
                  for s in (Incremental.plan pipeline).Steps do
                      match s with
                      | FallBack(AggregateStepNotLast v) -> failtestf "`plan` still mints AggregateStepNotLast %s" v
                      | _ -> ()

          // ================= the tail against the reference =================

          testCase "a Having whose group crosses the predicate"
          <| fun _ ->
              // `b = 2` holds r4 alone, so it is filtered OUT by `n > 1`. Adding a second row to it
              // makes the group appear in the result — a group ENTERING the tail's frame, which a
              // tail reasoning only about the groups it already had would never emit.
              let before = table baseRows
              let after = table (baseRows @ [ "r5", Int 11, Int 2 ])
              let s = step having before after

              expectRestrictedAndEqual having after s

          testCase "a Having whose group leaves the predicate"
          <| fun _ ->
              // The mirror: `b = 1` drops to one row, so the group LEAVES the result while still
              // existing in the group table. A tail that cached a verdict per group and never
              // re-asked would keep emitting it.
              let before = table baseRows
              let after = table (baseRows |> List.filter (fun (i, _, _) -> i <> "r3"))
              let s = step having before after

              expectRestrictedAndEqual having after s

          testCase "a row moving BETWEEN groups moves both"
          <| fun _ ->
              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, a, Int 2 else i, a, b)
                  )

              let s = step having before after

              expectRestrictedAndEqual having after s

          testCase "a DELETION from a max group"
          <| fun _ ->
              // The case the shard named as needing "a tombstone re-scan". It needs none: the group
              // is recomputed from the members it has left, so a deleted maximum is simply not
              // among them. Asserted anyway, because the case is the one a running-accumulator
              // design gets wrong and a reader will look for it.
              let maxPipeline =
                  [ GroupBy([ "b" ], [ agg "mx" Max "a"; agg "mn" Min "a" ])
                    Filter(Binary(Gt, Col "mx", Lit(Int 2))) ]

              let before = table baseRows
              let after = table (baseRows |> List.filter (fun (i, _, _) -> i <> "r3")) // r3 is b=1's max
              let s = step maxPipeline before after

              expectRestrictedAndEqual maxPipeline after s

              // And the deletion genuinely moved the answer — otherwise the case asserts nothing.
              Expect.notEqual
                  (DataFrame.evalPipeline maxPipeline before)
                  (DataFrame.evalPipeline maxPipeline after)
                  "the deleted row WAS its group's maximum"

          testCase "a top-N over the GROUPS"
          <| fun _ ->
              // Phase 207's verb, one frame along: the tail sorts the group table and cuts it. The
              // edit re-orders the groups by sum, so the kept group changes identity.
              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 50, b else i, a, b)
                  )

              let s = step groupThenTopOne before after

              expectRestrictedAndEqual groupThenTopOne after s
              Expect.equal (Table.rowCount s.Output) 1 "the cut kept one group"

          testCase "a quiet delta over an unchanged source reuses the whole tail"
          <| fun _ ->
              let before = table baseRows
              let s = step having before before

              Expect.equal s.Footprint.Recompute ReusedPrior "nothing changed and nothing was recomputed"
              Expect.equal (Ok s.Output) (DataFrame.evalPipeline having before) "and the answer still stands"

          // ================= the empty decline class, as an assertion =================

          testCase "every aggregate function is maintained past a tail"
          <| fun _ ->
              // The shard's "decomposable aggregates; the rest declined explicitly" asks for a
              // decline class that does not exist. `groupStep` recomputes an affected group from its
              // members through the reference's own aggregator, so decomposability is not a property
              // the seam needs from an `AggFn` — `Median` and `StdDev` are maintained exactly as
              // `Sum` is. This enumerates the union and goes red if that stops being true.
              let everyFn =
                  [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]

              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 40, b else i, a, b)
                  )

              for fn in everyFn do
                  let pipeline =
                      [ GroupBy([ "b" ], [ agg "v" fn "a"; agg "n" Count "a" ])
                        Filter(Binary(Gt, Col "n", Lit(Int 0))) ]

                  match (Incremental.plan pipeline).Strategy with
                  | ReferenceOnly r -> failtestf "%A is declined: %s" fn (Incremental.reasonString r)
                  | _ -> ()

                  let s = step pipeline before after

                  Expect.equal
                      (Ok s.Output)
                      (DataFrame.evalPipeline pipeline after)
                      (sprintf "%A: maintained = reference" fn)

                  match s.Footprint.Recompute with
                  | GroupsRecomputed _
                  | RowsRecomputed _
                  | ReusedPrior -> ()
                  | other -> failtestf "%A: expected a restricted refresh, got %A" fn other

          // ================= the tail cache, and its go-red =================

          testCase "an unaffected group's tail is not re-evaluated"
          <| fun _ ->
              // The whole claim of the phase: a tail that re-evaluated every group on every refresh
              // would be a full evaluation of the tail wearing a restricted footprint. One row of
              // group `b = 0` moves; groups `1` and `2` must cost nothing in the tail.
              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 2, b else i, a, b)
                  )

              let primed = ok (Incremental.primeOn idw groupThenDerive before)
              let delta = ok (Delta.diff idw before after)
              let refreshed = ok (Incremental.refreshOn idw groupThenDerive primed delta after)

              Expect.equal (Ok refreshed.Output) (DataFrame.evalPipeline groupThenDerive after) "maintained = reference"

              // Three groups exist; the `Derive` is the pipeline's only evaluating step, so the
              // prime charges three and a refresh touching one group must charge exactly one.
              Expect.equal
                  (Incremental.rowsEvaluated primed.Footprint)
                  3
                  "the prime evaluated the tail for all three groups"

              Expect.equal
                  (Incremental.rowsEvaluated refreshed.Footprint)
                  1
                  "the refresh evaluated the tail for the ONE affected group"

          testCase "the tail's reuse condition is load-bearing"
          <| fun _ ->
              // The go-red half, carried in the file rather than run once by hand. The shipped rule
              // reuses a group's tail cells only when `groupStep` REUSED that group's aggregates.
              // The weaker rule a first draft reaches for — "reuse when the group existed before" —
              // is modelled by `reuseIfGroupExisted`, and this shows it answers WRONGLY on a group
              // that existed and whose aggregate moved.
              let before = table baseRows

              let after =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 2, b else i, a, b)
                  )

              let priorOut = ok (DataFrame.evalPipeline groupThenDerive before)
              let referenceOut = ok (DataFrame.evalPipeline groupThenDerive after)

              let weakModel = reuseIfGroupExisted priorOut (Int 0) "twice"

              let truth = reuseIfGroupExisted referenceOut (Int 0) "twice"

              Expect.isSome weakModel "group b=0 existed before, so the weak model would have reused it"

              Expect.notEqual
                  weakModel
                  truth
                  "and reusing it is WRONG — the group's sum moved, so its derived cell moved with it"

              // The shipped seam gets it right, which is the point of showing the model fail.
              let s = step groupThenDerive before after
              Expect.equal (Ok s.Output) (Ok referenceOut) "the shipped tail answers as the reference does"

          // ================= the generated differential =================

          testCase "generated: the maintained answer equals the reference for every drawn edit"
          <| fun _ ->
              // The reference semantics is the full evaluation, for every pipeline and every change
              // sequence — including a sequence, not only a single refresh, because the state a
              // refresh leaves behind is what the NEXT refresh reads.
              let pipelines =
                  [ "having", having
                    "derive", groupThenDerive
                    "top-1-of-groups", groupThenTopOne
                    "sort-of-groups",
                    [ GroupBy([ "b" ], [ agg "s" Sum "a"; agg "n" Count "a" ])
                      Transform.sortBy [ "s", Asc; "b", Asc ] ]
                    "project-of-groups",
                    [ GroupBy([ "b" ], [ agg "s" Sum "a"; agg "mx" Max "a" ])
                      Project [ "b", "b"; "mx", "mx" ] ]
                    "prefix-then-group-then-tail",
                    [ Filter(Binary(Gt, Col "a", Lit(Int -4)))
                      GroupBy([ "b" ], [ agg "n" Count "a"; agg "mn" Min "a" ])
                      Derive("bumped", Binary(Add, Col "mn", Lit(Int 1)))
                      Filter(Binary(Ge, Col "n", Lit(Int 1))) ]
                    // A sort on BOTH sides of the group-by — the composition this corpus otherwise
                    // lacks, with order-sensitive `First` / `Last` reading the position the prefix
                    // sort left. It is NOT a guard on the tail's sort ordinals continuing the
                    // prefix's: restarting them was measured green here, because the order-reuse
                    // condition is unsatisfiable across two disjoint token vocabularies except
                    // where it degenerates to a full sort. That is recorded beside the code in
                    // `split`; what this case does catch is the group table being sorted at all.
                    "sort-both-sides",
                    [ Transform.sortBy [ "a", Desc ]
                      GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "s" Sum "a" ])
                      Transform.sortBy [ "s", Asc; "b", Desc ] ] ]

              let mutable seed = 20260919

              for name, pipeline in pipelines do
                  for iteration in 0..39 do
                      let rows0, s1 = drawRows seed 7
                      let rows1, s2 = drawRows s1 6
                      let rows2, s3 = drawRows s2 8
                      seed <- s3

                      // A SEQUENCE: prime, then two refreshes, each carrying the previous state.
                      let t0 = table rows0
                      let t1 = table rows1
                      let t2 = table rows2

                      let st0 = ok (Incremental.primeOn idw pipeline t0)

                      let st1 = ok (Incremental.refreshOn idw pipeline st0 (ok (Delta.diff idw t0 t1)) t1)

                      let st2 = ok (Incremental.refreshOn idw pipeline st1 (ok (Delta.diff idw t1 t2)) t2)

                      Expect.equal
                          (Ok st1.Output)
                          (DataFrame.evalPipeline pipeline t1)
                          (sprintf "%s iter=%d refresh 1 = reference" name iteration)

                      Expect.equal
                          (Ok st2.Output)
                          (DataFrame.evalPipeline pipeline t2)
                          (sprintf "%s iter=%d refresh 2 = reference" name iteration)

          testCase "generated: the sweep actually restricted, rather than falling back throughout"
          <| fun _ ->
              // Without this, the sweep above passes on a build that declined every pipeline: a
              // fall-back is always equal to the reference. This is the falsifier for the sweep.
              let before = table (fst (drawRows 4242 8))
              let after = table (fst (drawRows 99 8))

              let restricted =
                  [ having; groupThenDerive; groupThenTopOne ]
                  |> List.filter (fun pipeline ->
                      match (step pipeline before after).Footprint.Recompute with
                      | GroupsRecomputed _
                      | RowsRecomputed _ -> true
                      | _ -> false)

              Expect.equal (List.length restricted) 3 "every tail-bearing pipeline refreshed under restriction" ]
