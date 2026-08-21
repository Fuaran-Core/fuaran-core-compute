module Fuaran.Core.Tests.IncrementalTests

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 99 — the incremental `Transform` evaluation seam.
//
//  Two kinds of test, and both are needed. The generated equivalence family
//  (`IncrementalDelta.laws`) certifies that the incremental answer IS the
//  reference answer across a corpus built to reach the awkward cases. The
//  hand-written cases below pin the two things a pure equality suite cannot
//  say: the exact FOOTPRINT of a refresh (an evaluator that quietly recomputed
//  everything would satisfy every equality here), and the exact fall-back
//  REASON for each way out of the incremental path (a seam that fell back for
//  the wrong reason would still return the right table).
// ---------------------------------------------------------------------------

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

let private table (rows: (string * Cell * Cell) list) : Table =
    { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
      Columns =
        [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
          Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
          Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

let private baseRows =
    [ "r0", Int 1, Int 0
      "r1", Int 2, Int 0
      "r2", Int 3, Int 1
      "r3", Int 4, Int 1
      "r4", Int 5, Int 2 ]

let private baseTable = table baseRows

/// Prime over `before`, diff to `after`, refresh — the everyday shape, with the diff as the delta
/// producer so the delta is truthful by construction.
let private step (pipeline: Transform list) (before: Table) (after: Table) =
    let state = ok (Incremental.primeOn idw pipeline before)
    let delta = ok (Delta.diff idw before after)
    let next = ok (Incremental.refreshOn idw pipeline state delta after)
    next, delta

let private expectMatchesReference (pipeline: Transform list) (after: Table) (s: IncrementalEval) =
    Expect.equal (Ok s.Output) (DataFrame.evalPipeline pipeline after) "incremental result = reference result"

let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

[<Tests>]
let tests =
    testList
        "Incremental"
        [

          // ================= the generated equivalence family =================

          testCase "the equivalence family is green across seeds"
          <| fun _ ->
              for seed in [ 1; 7; 99; 20260821 ] do
                  for r in IncrementalDelta.laws seed 60 do
                      Expect.isTrue r.Passed (sprintf "seed %d — %s: %A" seed r.Law r.Counterexample)

          testCase "the family's corpus reaches every case it claims to"
          <| fun _ ->
              // A generator that silently stopped producing a case would leave the equivalence
              // laws green and meaningless. These are the cases the family's header claims, so
              // they are asserted rather than assumed.
              let xs = List.collect (fun s -> IncrementalDelta.samples s 60) [ 1; 7; 99 ]

              let has f = xs |> List.exists f

              Expect.isTrue
                  (has (fun s ->
                      match s.Strategy with
                      | RowLocal -> true
                      | _ -> false))
                  "a row-local pipeline was generated"

              Expect.isTrue
                  (has (fun s ->
                      match s.Strategy with
                      | RowLocalThenGroups -> true
                      | _ -> false))
                  "a maintained-group pipeline was generated"

              Expect.isTrue
                  (has (fun s ->
                      match s.Strategy with
                      | ReferenceOnly _ -> true
                      | _ -> false))
                  "a declined pipeline was generated"

              Expect.isTrue
                  (has (fun s ->
                      match s.Refresh.Recompute with
                      | RowsRecomputed _ -> true
                      | _ -> false))
                  "a row-restricted refresh happened"

              Expect.isTrue
                  (has (fun s ->
                      match s.Refresh.Recompute with
                      | GroupsRecomputed _ -> true
                      | _ -> false))
                  "a group-restricted refresh happened"

              Expect.isTrue (has (fun s -> s.Edit = "reverse")) "the reordering edit was generated"
              Expect.isTrue (has (fun s -> s.Edit = "schemaWiden")) "the schema-change edit was generated"

          testCase "the family records a footprint line per sample"
          <| fun _ ->
              let xs = IncrementalDelta.samples 3 12
              let lines = IncrementalDelta.report xs
              Expect.equal (List.length lines) (List.length xs) "one recorded line per sample"

              Expect.isTrue
                  (lines |> List.forall (fun l -> l.Contains "prime:" && l.Contains "refresh:"))
                  "each line records both footprints"

          // ================= the plan (the boundary, declared as data) =================

          testCase "plan classifies every step, and the strategy follows the classifications"
          <| fun _ ->
              let rowLocal =
                  Incremental.plan
                      [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                        Derive("d", Col "a")
                        Project [ "id", "id"; "d", "d" ] ]

              Expect.equal rowLocal.Steps [ PropagateRows; PropagateRows; PropagateRows ] "three row-local steps"
              Expect.equal rowLocal.Strategy RowLocal "row-local strategy"

              let grouped =
                  Incremental.plan
                      [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                        GroupBy([ "b" ], [ agg "n" Count "a" ]) ]

              Expect.equal
                  grouped.Steps
                  [ PropagateRows; MaintainGroups([ "b" ], [ "n" ]) ]
                  "the final groupBy declares what it maintains"

              Expect.equal grouped.Strategy RowLocalThenGroups "row-local then groups"

              // The SAME groupBy, one step earlier, is not maintainable — what follows it would
              // need a delta over the group table, and no such delta was supplied.
              let midGrouped =
                  Incremental.plan
                      [ GroupBy([ "b" ], [ agg "n" Count "a" ])
                        Filter(Binary(Gt, Col "n", Lit(Int 0))) ]

              Expect.equal
                  midGrouped.Strategy
                  (ReferenceOnly(AggregateStepNotLast "groupBy"))
                  "a non-final groupBy is declined, naming why"

              Expect.equal
                  (Incremental.plan [ Sort [ "a", Asc ] ]).Strategy
                  (ReferenceOnly(StepNotRowLocal "sort"))
                  "a sort is declined, naming the verb"

              Expect.equal (Incremental.plan []).Strategy RowLocal "the empty pipeline is trivially row-local"

              Expect.isFalse
                  (Incremental.isIncremental (Incremental.plan [ Distinct ]))
                  "isIncremental agrees with the strategy"

          // ================= the footprint (the saving, measured) =================

          testCase "a one-row change re-evaluates one row, not five"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]

              // the row order is held stable, so the changed cell is the only difference
              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 30, b else i, a, b)
                  )

              let next, delta = step pipeline baseTable changed

              Expect.equal (Delta.rowsWith RowChanged delta) [ ByKey "s:r2" ] "the diff named exactly the changed row"
              Expect.equal next.Footprint.Recompute (RowsRecomputed 1) "one row re-evaluated"
              Expect.equal next.Footprint.SourceRows 5 "over a five-row source"
              expectMatchesReference pipeline changed next

          testCase "each row-local step that evaluates counts once per re-evaluated row"
          <| fun _ ->
              // Two evaluating steps (a filter and a derive) and one changed row that survives the
              // filter: two evaluations, not two-times-five.
              let pipeline =
                  [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                    Derive("d", Binary(Mul, Col "a", Lit(Int 2))) ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 9, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed
              Expect.equal next.Footprint.Recompute (RowsRecomputed 2) "two evaluations for one changed row"
              expectMatchesReference pipeline changed next

          testCase "a change confined to one group recomputes one group"
          <| fun _ ->
              let pipeline = [ GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ]) ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r3" then i, Int 40, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal
                  next.Footprint.Recompute
                  (GroupsRecomputed(0, 1))
                  "one group recomputed, no row expressions evaluated (the pipeline has none)"

              Expect.equal next.Footprint.ResultRows 3 "three groups in the result"
              expectMatchesReference pipeline changed next

          testCase "a quiet delta over an unchanged source reuses the prior result"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let next, delta = step pipeline baseTable baseTable
              Expect.isTrue (Delta.isQuiet delta) "the diff of a table with itself is quiet"
              Expect.equal next.Footprint.Recompute ReusedPrior "nothing was re-evaluated"
              expectMatchesReference pipeline baseTable next

          // ================= the traps =================

          testCase "a pure REORDERING is not treated as no change"
          <| fun _ ->
              // `Delta.diff` reports a reordering as quiet — every key is present at both ends with
              // identical content. But `First` / `Last` read position, so handing back the prior
              // result would be wrong. The source comparison is what catches it.
              let pipeline =
                  [ GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "n" Count "a" ]) ]

              let reversed = table (List.rev baseRows)
              let next, delta = step pipeline baseTable reversed
              Expect.isTrue (Delta.isQuiet delta) "an identity diff calls a reordering quiet"
              Expect.notEqual next.Footprint.Recompute ReusedPrior "the prior result was NOT reused"
              expectMatchesReference pipeline reversed next

              Expect.notEqual
                  (Ok next.Output)
                  (DataFrame.evalPipeline pipeline baseTable)
                  "and the answer genuinely moved — the test would pass vacuously otherwise"

          testCase "a DERIVED column's type follows the rows alive at that step"
          <| fun _ ->
              // The derived column is `Str` for positive `a` and `Null` otherwise, and the filter
              // after it keeps only the non-positive rows — so the column's inferred type is decided
              // by rows that the result never contains. Reading the type off the surviving rows (or
              // off a cache) types it differently from the reference.
              let pipeline =
                  [ Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 0)), Lit(Str "pos") ], Lit Null))
                    Filter(Binary(Lt, Col "a", Lit(Int 0))) ]

              let rows = [ "r0", Int 3, Int 0; "r1", Int -1, Int 0; "r2", Int -2, Int 1 ]
              let before = table rows
              // drop the only positive row: the derived column's type moves with it
              let after = table (rows |> List.filter (fun (i, _, _) -> i <> "r0"))
              let next, _ = step pipeline before after
              expectMatchesReference pipeline after next

              Expect.equal
                  (next.Output.Schema |> List.map fst)
                  [ "id"; "a"; "b"; "d" ]
                  "the derived column is present in the result"

          testCase "an append re-evaluates only the appended rows"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
              let after = table (baseRows @ [ "r5", Int 6, Int 2; "r6", Int 7, Int 2 ])
              let next, _ = step pipeline baseTable after
              Expect.equal next.Footprint.Recompute (RowsRecomputed 2) "only the two new rows"
              expectMatchesReference pipeline after next

          testCase "a removal recomputes the group it emptied"
          <| fun _ ->
              let pipeline = [ GroupBy([ "b" ], [ agg "n" Count "a" ]) ]
              let after = table (baseRows |> List.filter (fun (i, _, _) -> i <> "r4"))
              let next, _ = step pipeline baseTable after
              Expect.equal next.Footprint.ResultRows 2 "the b=2 group is gone"
              expectMatchesReference pipeline after next

          // ================= every way out, with its reason =================

          testCase "a declined verb falls back naming the verb"
          <| fun _ ->
              let pipeline = [ Sort [ "a", Asc ] ]
              let after = table (baseRows @ [ "r5", Int 0, Int 2 ])
              let next, _ = step pipeline baseTable after

              Expect.equal
                  next.Footprint.Recompute
                  (FullRecompute(StepNotRowLocal "sort"))
                  "the fall-back names the verb"

              expectMatchesReference pipeline after next

          testCase "a declined pipeline still reuses the prior result when the source did not move"
          <| fun _ ->
              // The reuse is sound for any strategy: a verb is declined because it cannot answer a
              // CHANGE, not because it must be re-run when nothing changed.
              let pipeline = [ Sort [ "a", Asc ] ]
              let next, _ = step pipeline baseTable baseTable
              Expect.equal next.Footprint.Recompute ReusedPrior "an unchanged source needs no re-sort"
              expectMatchesReference pipeline baseTable next

          testCase "a FullRefresh delta falls back, and still answers correctly"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let after = table (baseRows @ [ "r5", Int 8, Int 2 ])
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let next = ok (Incremental.refreshOn idw pipeline state FullRefresh after)
              Expect.equal next.Footprint.Recompute (FullRecompute DeltaIsFullRefresh) "the reason is the delta"
              expectMatchesReference pipeline after next

          testCase "an ordinal-addressed delta falls back rather than keying a cache by position"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let after = table (baseRows @ [ "r5", Int 8, Int 2 ])
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let delta = Delta.diffByOrdinal baseTable after
              let next = ok (Incremental.refreshOn idw pipeline state delta after)
              Expect.equal next.Footprint.Recompute (FullRecompute OrdinalAddressing) "the reason is the addressing"
              expectMatchesReference pipeline after next

          testCase "a schema change falls back as a schema change, not as a row change"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]

              let wide: Table =
                  { Schema = [ "id", StringType; "a", IntType; "b", IntType; "c", IntType ]
                    Columns =
                      baseTable.Columns
                      @ [ Column.create "c" IntType (baseRows |> List.map (fun _ -> Int 1)) ] }

              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let delta = ok (Delta.diff idw baseTable wide)
              Expect.equal delta FullRefresh "an identity diff across schemas is a full refresh"
              let next = ok (Incremental.refreshOn idw pipeline state delta wide)
              Expect.equal next.Footprint.Recompute (FullRecompute SourceSchemaMoved) "the schema is the reason"
              expectMatchesReference pipeline wide next

          testCase "a changed env falls back — a cached cell is no longer that row's value"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Param "t")) ]
              let env0 = Map.ofList [ "t", Int 0 ]
              let env1 = Map.ofList [ "t", Int 3 ]
              let state = ok (Incremental.prime DataFrame.noResolve env0 idw pipeline baseTable)

              let next =
                  ok (
                      Incremental.refresh DataFrame.noResolve env1 idw pipeline state (Delta.empty idw.Scheme) baseTable
                  )

              Expect.equal next.Footprint.Recompute (FullRecompute EnvChanged) "the env is the reason"

              Expect.equal
                  (Ok next.Output)
                  (DataFrame.evalPipelineInEnv env1 pipeline baseTable)
                  "and the answer is the reference answer under the NEW env"

          testCase "a changed pipeline falls back"
          <| fun _ ->
              let p0 = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
              let p1 = [ Filter(Binary(Gt, Col "a", Lit(Int 3))) ]
              let state = ok (Incremental.primeOn idw p0 baseTable)

              let next =
                  ok (Incremental.refreshOn idw p1 state (Delta.empty idw.Scheme) baseTable)

              Expect.equal next.Footprint.Recompute (FullRecompute PipelineChanged) "the pipeline is the reason"
              expectMatchesReference p1 baseTable next

          testCase "a column-invalidation delta re-evaluates every row (it names none)"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let delta = Delta.ofColumns idw.Scheme [ "a" ]
              let next = ok (Incremental.refreshOn idw pipeline state delta baseTable)

              Expect.equal
                  next.Footprint.Recompute
                  (RowsRecomputed 5)
                  "every row is suspect — column invalidation names no rows"

              expectMatchesReference pipeline baseTable next

          testCase "a witness that cannot key the source degrades instead of failing"
          <| fun _ ->
              // A null key cell is NOT an identity (Phase 98's rule), so this witness cannot key the
              // source. The seam still answers — through the reference evaluator, with the defect
              // carried in the reason.
              let rows = [ "r0", Int 1, Int 0; "r1", Int 2, Int 0 ]

              let nullKeyed: Table =
                  { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "id" StringType [ Null; Str "r1" ]
                        Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
                        Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
              let state = ok (Incremental.primeOn idw pipeline nullKeyed)

              Expect.equal
                  state.Footprint.Recompute
                  (FullRecompute(RowIdentityUnusable(MissingIdentity(idw.Scheme, 0))))
                  "the defect is carried, not swallowed"

              Expect.equal (Ok state.Output) (DataFrame.evalPipeline pipeline nullKeyed) "and the answer is correct"

          // ================= reporting =================

          testCase "the footprint string is stable and counts-only"
          <| fun _ ->
              let f =
                  { SourceRows = 5
                    ResultRows = 3
                    Recompute = RowsRecomputed 1 }

              Expect.equal
                  (Incremental.footprintString f)
                  "5 source rows -> 3 result rows: 1 rows re-evaluated"
                  "the recorded line"

              Expect.equal (Incremental.rowsEvaluated f) 1 "rows evaluated"

              Expect.equal
                  (Incremental.rowsEvaluated
                      { f with
                          Recompute = FullRecompute DeltaIsFullRefresh })
                  5
                  "a full evaluation touches every source row"

          testCase "a repeated refresh keeps restricting (the state advances, it does not decay)"
          <| fun _ ->
              let pipeline =
                  [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                    Derive("d", Binary(Add, Col "a", Col "b")) ]

              let s0 = ok (Incremental.primeOn idw pipeline baseTable)
              let t1 = table (baseRows @ [ "r5", Int 6, Int 2 ])

              let s1 =
                  ok (Incremental.refreshOn idw pipeline s0 (ok (Delta.diff idw baseTable t1)) t1)

              let t2 = table ((baseRows @ [ "r5", Int 6, Int 2 ]) @ [ "r6", Int 7, Int 2 ])
              let s2 = ok (Incremental.refreshOn idw pipeline s1 (ok (Delta.diff idw t1 t2)) t2)

              Expect.equal s1.Footprint.Recompute (RowsRecomputed 2) "one new row, two evaluating steps"
              Expect.equal s2.Footprint.Recompute (RowsRecomputed 2) "still two, over a larger source"
              Expect.equal s2.Footprint.SourceRows 7 "the source grew"
              expectMatchesReference pipeline t2 s2 ]
