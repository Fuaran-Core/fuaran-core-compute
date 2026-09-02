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

// ---- Phase 120 fixtures ----

/// A bounded frame: the value of the row before this one in its partition's order.
let private lagOverB =
    Window
        { PartitionBy = [ "b" ]
          OrderBy = [ "a", Asc ]
          Fn = Lag
          Of = "a"
          As = "prev" }

/// The relation the filtering joins match against: two of the three `b` values the base table
/// carries, so both sides of the verdict are live in every case below.
let private lookup: Table =
    { Schema = [ "k", IntType ]
      Columns = [ Column.create "k" IntType [ Int 0; Int 2 ] ] }

let private semiOnLookup = Join(Embedded lookup, [ "b", "k" ], Semi)
let private antiOnLookup = Join(Embedded lookup, [ "b", "k" ], Anti)

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

              // A sort is admitted (Phase 115) and says so in its own case: it is not row-local,
              // and calling it `PropagateRows` would be a wrong answer to "does this step's output
              // for a row depend only on that row".
              let sorted =
                  Incremental.plan [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Sort [ "a", Asc ] ]

              Expect.equal
                  sorted.Steps
                  [ PropagateRows; MergeOrder [ "a", Asc ] ]
                  "a sort declares the ordering it merges by"

              Expect.equal sorted.Strategy RowLocal "a sort-bearing pipeline is not declined"

              // A sort carries no position condition, unlike a groupBy: every step admitted after
              // it reads the order it produced exactly as it would have read the reference's.
              Expect.equal
                  (Incremental.plan [ Sort [ "b", Asc ]; GroupBy([ "b" ], [ agg "n" Count "a" ]) ]).Strategy
                  RowLocalThenGroups
                  "a sort before a maintained groupBy is still incremental"

              Expect.equal
                  (Incremental.plan [ Limit(2, 0) ]).Strategy
                  (ReferenceOnly(StepNotRowLocal "limit"))
                  "an order-dependent verb that is NOT a sort is still declined, naming the verb"

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
              let pipeline = [ Limit(2, 0) ]
              let after = table (baseRows @ [ "r5", Int 0, Int 2 ])
              let next, _ = step pipeline baseTable after

              // A `Limit` evaluates no per-row expression, so a full evaluation of this pipeline is
              // charged nothing at all — which is the point of counting row evaluations at steps
              // rather than source rows. Six source rows, zero row evaluations.
              Expect.equal
                  next.Footprint.Recompute
                  (FullRecompute(0, StepNotRowLocal "limit"))
                  "the fall-back names the verb, and carries what the reference actually evaluated"

              Expect.equal next.Footprint.SourceRows 6 "`SourceRows` stays its own field"
              expectMatchesReference pipeline after next

          testCase "a declined pipeline's PRIME is `Primed`, not the decline"
          <| fun _ ->
              // A prime avoids nothing whatever the plan says, so there is no fall-back to report —
              // the decline attaches to the REFRESH, where it actually happens. The plan is what a
              // consumer asks beforehand, and it still says the pipeline is declined.
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Limit(2, 0) ]
              let primed = ok (Incremental.primeOn idw pipeline baseTable)

              Expect.equal
                  (Incremental.plan pipeline).Strategy
                  (ReferenceOnly(StepNotRowLocal "limit"))
                  "the pipeline is declined"

              Expect.equal primed.Footprint.Recompute (Primed 5) "and its prime evaluated the filter over every row"

              let after = table (baseRows @ [ "r5", Int 8, Int 2 ])
              let delta = ok (Delta.diff idw baseTable after)
              let next = ok (Incremental.refreshOn idw pipeline primed delta after)

              Expect.equal
                  next.Footprint.Recompute
                  (FullRecompute(6, StepNotRowLocal "limit"))
                  "the refresh is where the decline is reported"

              expectMatchesReference pipeline after next

          testCase "a declined pipeline still reuses the prior result when the source did not move"
          <| fun _ ->
              // The reuse is sound for any strategy: a verb is declined because it cannot answer a
              // CHANGE, not because it must be re-run when nothing changed.
              let pipeline = [ Project [ "b", "b" ]; Distinct ]
              let next, _ = step pipeline baseTable baseTable
              Expect.equal next.Footprint.Recompute ReusedPrior "an unchanged source needs no re-run"
              expectMatchesReference pipeline baseTable next

          testCase "a FullRefresh delta falls back, and still answers correctly"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let after = table (baseRows @ [ "r5", Int 8, Int 2 ])
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let next = ok (Incremental.refreshOn idw pipeline state FullRefresh after)
              Expect.equal next.Footprint.Recompute (FullRecompute(6, DeltaIsFullRefresh)) "the reason is the delta"
              expectMatchesReference pipeline after next

          testCase "an ordinal-addressed delta falls back rather than keying a cache by position"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 2))) ]
              let after = table (baseRows @ [ "r5", Int 8, Int 2 ])
              let state = ok (Incremental.primeOn idw pipeline baseTable)
              let delta = Delta.diffByOrdinal baseTable after
              let next = ok (Incremental.refreshOn idw pipeline state delta after)
              Expect.equal next.Footprint.Recompute (FullRecompute(6, OrdinalAddressing)) "the reason is the addressing"
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
              Expect.equal next.Footprint.Recompute (FullRecompute(5, SourceSchemaMoved)) "the schema is the reason"
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

              Expect.equal next.Footprint.Recompute (FullRecompute(5, EnvChanged)) "the env is the reason"

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

              Expect.equal next.Footprint.Recompute (FullRecompute(5, PipelineChanged)) "the pipeline is the reason"
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
                  (FullRecompute(2, RowIdentityUnusable(MissingIdentity(idw.Scheme, 0))))
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

              // One scale (Phase 117): a full evaluation reports what it evaluated, not the source
              // row count. Here the two are deliberately DIFFERENT — five source rows, eleven row
              // evaluations, because the pipeline had more than one evaluating step — which is the
              // whole distinction the old projection could not express.
              let full =
                  { f with
                      Recompute = FullRecompute(11, DeltaIsFullRefresh) }

              Expect.equal (Incremental.rowsEvaluated full) 11 "a full evaluation reports its own count"
              Expect.equal full.SourceRows 5 "and `SourceRows` stays its own field"

              Expect.equal
                  (Incremental.footprintString full)
                  "5 source rows -> 3 result rows: full evaluation, 11 rows evaluated (the delta is a full refresh)"
                  "the recorded line carries the count too"

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
              expectMatchesReference pipeline t2 s2

          // ================= the merged order (Phase 115) =================

          testCase "a sort re-evaluates only the named rows, and the sort itself costs none"
          <| fun _ ->
              // The shape the estate's recompute fixture family carries. The saving is NOT in the
              // sorting — it is that the filter before it stops running over every row, which is
              // exactly what this pipeline cost while `sort` was declined.
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Sort [ "a", Asc ] ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 30, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal next.Footprint.Recompute (RowsRecomputed 1) "one filter predicate, not five"
              expectMatchesReference pipeline changed next

              // A lone sort evaluates no expression at all, so a changed row costs nothing to
              // re-evaluate and the whole of the work is the merge, which the footprint does not
              // charge for — the same accounting a groupBy gets, and for the same reason.
              let lone, _ = step [ Sort [ "a", Asc ] ] baseTable changed
              Expect.equal lone.Footprint.Recompute (RowsRecomputed 0) "a sort evaluates nothing"
              expectMatchesReference [ Sort [ "a", Asc ] ] changed lone

          testCase "the merge breaks a tie the way a stable sort does, not the way a cache would"
          <| fun _ ->
              // `b` ties r0 with r1 and r2 with r3. The changed row (r2) is lifted out of the
              // cached order and merged back; at its tie with r3 the answer is decided by ARRIVAL
              // position, which is what `List.sortWith`'s stability means. A merge that compared
              // keys alone would put r3 first here and be correctly sorted and wrong.
              let pipeline = [ Sort [ "b", Asc ] ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 99, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal
                  (next.Output |> Table.tryColumn "id" |> Option.map (fun c -> c.Cells))
                  (Some [ Str "r0"; Str "r1"; Str "r2"; Str "r3"; Str "r4" ])
                  "the tie keeps arrival order"

              expectMatchesReference pipeline changed next

          testCase "a reordering that names no row does not answer from the cached order"
          <| fun _ ->
              // `Delta.diff` reports a pure reordering as QUIET — every key is present at both ends
              // with identical content — so nothing in the delta says the frame moved. The cached
              // order is reusable only for rows that ARRIVED in the same relative order, and here
              // none did. This is the one case a merge gets wrong silently.
              let pipeline = [ Sort [ "b", Asc ] ]
              let reversed = table (List.rev baseRows)
              let next, delta = step pipeline baseTable reversed

              Expect.isTrue (Delta.isQuiet delta) "the diff named nothing"
              Expect.equal next.Footprint.Recompute (RowsRecomputed 0) "and nothing was re-evaluated"

              Expect.equal
                  (next.Output |> Table.tryColumn "id" |> Option.map (fun c -> c.Cells))
                  (Some [ Str "r1"; Str "r0"; Str "r3"; Str "r2"; Str "r4" ])
                  "the order is the reference's over the REVERSED frame, not the cached one"

              expectMatchesReference pipeline reversed next

          testCase "a sort feeding a maintained group still maintains it"
          <| fun _ ->
              // The group's `First` / `Last` read the position the sort put each row in, so the
              // ordered-member condition and the arrival-order condition are both live here. A
              // group whose members did not move is still reused.
              let pipeline =
                  [ Sort [ "b", Asc; "a", Desc ]
                    GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "n" Count "a" ]) ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 50, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              match next.Footprint.Recompute with
              | GroupsRecomputed(_, g) -> Expect.equal g 1 "only the group the changed row is in"
              | other -> failtestf "expected a maintained-group refresh, got %A" other

              expectMatchesReference pipeline changed next

          testCase "a sort before a filter and a type-inferring derive stays equal to the reference"
          <| fun _ ->
              // A sort that is NOT last: the steps after it read the order it produced, including a
              // derived column whose TYPE is inferred over the frame at that step.
              let pipeline =
                  [ Sort [ "b", Asc ]
                    Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 3)), Lit(Str "hi") ], Lit Null))
                    Filter(Binary(Lt, Col "a", Lit(Int 5))) ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 9, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed
              expectMatchesReference pipeline changed next

          // ============ the bounded frame and the filtering join (Phase 120) ============

          testCase "a bounded-frame window re-evaluates only the named rows, and the window costs none"
          <| fun _ ->
              // The `MergeOrder` accounting, one verb along. A window evaluates no expression, so
              // what the admission buys is that the FILTER before it stops running over every row —
              // which is the whole cost this pipeline paid while `window` was declined.
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))); lagOverB ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 30, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal next.Footprint.Recompute (RowsRecomputed 1) "one filter predicate, not five"
              expectMatchesReference pipeline changed next

              let lone, _ = step [ lagOverB ] baseTable changed
              Expect.equal lone.Footprint.Recompute (RowsRecomputed 0) "a window evaluates nothing"
              expectMatchesReference [ lagOverB ] changed lone

          testCase "a bounded frame moves a row the delta did NOT name, and the column follows it"
          <| fun _ ->
              // The case a per-row cache of the window column would get wrong silently. `lag` reads
              // the row BEFORE this one in the partition's order, so changing r0's `a` moves r1's
              // `prev` — and the delta names r0 alone. The column is recomputed over the walked
              // frame for exactly this reason.
              let pipeline = [ lagOverB ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 99, b else i, a, b)
                  )

              let primed = ok (Incremental.primeOn idw pipeline baseTable)
              let next, delta = step pipeline baseTable changed

              Expect.equal (Delta.rowsWith RowChanged delta |> List.length) 1 "the delta named one row"

              Expect.equal
                  (primed.Output |> Table.tryColumn "prev" |> Option.map (fun c -> c.Cells))
                  (Some [ Null; Int 1; Null; Int 3; Null ])
                  "before: partition b=0 orders r0 (1) then r1 (2), so r1's predecessor is r0's value"

              // Partition b=0 now orders r1 (2) before r0 (99), so the two swap places: r1 becomes
              // the partition's first row and loses its predecessor, and r0 gains r1's value. BOTH
              // cells move, and the delta named only r0.
              Expect.equal
                  (next.Output |> Table.tryColumn "prev" |> Option.map (fun c -> c.Cells))
                  (Some [ Int 2; Null; Null; Int 3; Null ])
                  "after: the unnamed neighbour's cell moved too"

              expectMatchesReference pipeline changed next

          testCase "an unbounded frame declines by type, naming the FUNCTION and not the verb"
          <| fun _ ->
              // `window` alone would not say which frame declined, now that some of them do not.
              let pipeline =
                  [ Window
                        { PartitionBy = [ "b" ]
                          OrderBy = [ "a", Asc ]
                          Fn = CumulSum
                          Of = "a"
                          As = "run" } ]

              Expect.equal
                  (Incremental.plan pipeline).Strategy
                  (ReferenceOnly(WindowFrameUnbounded "cumulSum"))
                  "a cumulative aggregate reads every preceding row of its partition"

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r2" then i, Int 30, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal
                  next.Footprint.Recompute
                  (FullRecompute(0, WindowFrameUnbounded "cumulSum"))
                  "the refresh falls back, carrying the declared reason"

              Expect.equal
                  (Incremental.reasonString (WindowFrameUnbounded "cumulSum"))
                  "the window function 'cumulSum' reads the whole partition, not a bounded frame"
                  "and the reason renders as a stable line"

              expectMatchesReference pipeline changed next

          testCase "a filtering join re-evaluates only the named rows, and the join costs none"
          <| fun _ ->
              // The edit moves r2's key OUT of the relation, so the verdict cached for r2 is
              // exactly what has to be recomputed and every other row's is exactly what may be
              // reused. `b` = 1 is not in the lookup, so r2 leaves the result.
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))); semiOnLookup ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, a, Int 1 else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              Expect.equal next.Footprint.Recompute (RowsRecomputed 1) "one filter predicate, not five"
              Expect.equal next.Output.Schema baseTable.Schema "a filtering join keeps the LEFT schema only"
              expectMatchesReference pipeline changed next

              let lone, _ = step [ semiOnLookup ] baseTable changed
              Expect.equal lone.Footprint.Recompute (RowsRecomputed 0) "a join evaluates nothing"
              expectMatchesReference [ semiOnLookup ] changed lone

          testCase "the anti join is the semi join's complement, row for row"
          <| fun _ ->
              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, a, Int 1 else i, a, b)
                  )

              let semi, _ = step [ semiOnLookup ] baseTable changed
              let anti, _ = step [ antiOnLookup ] baseTable changed

              expectMatchesReference [ semiOnLookup ] changed semi
              expectMatchesReference [ antiOnLookup ] changed anti

              let ids (t: Table) =
                  t
                  |> Table.tryColumn "id"
                  |> Option.map (fun c -> c.Cells)
                  |> Option.defaultValue []

              Expect.equal
                  (ids semi.Output @ ids anti.Output |> List.sort)
                  (ids changed |> List.sort)
                  "together they partition the rows"

              Expect.isEmpty
                  (Set.intersect (Set.ofList (ids semi.Output)) (Set.ofList (ids anti.Output)))
                  "and they overlap nowhere"

          testCase "a filtering join feeding a maintained group still maintains it"
          <| fun _ ->
              // What the join decides here is which rows are in the group at all, so a group whose
              // membership did not move is still reused.
              let pipeline =
                  [ semiOnLookup; GroupBy([ "b" ], [ agg "n" Count "a"; agg "f" First "id" ]) ]

              let changed =
                  table (
                      baseRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 50, b else i, a, b)
                  )

              let next, _ = step pipeline baseTable changed

              match next.Footprint.Recompute with
              | GroupsRecomputed(_, g) -> Expect.equal g 1 "only the group the changed row is in"
              | other -> failtestf "expected a maintained-group refresh, got %A" other

              expectMatchesReference pipeline changed next

          testCase "a combining join declines by type, naming the KIND and not the verb"
          <| fun _ ->
              for how in [ Inner; Left; Right; Outer ] do
                  let pipeline = [ Join(Embedded lookup, [ "b", "k" ], how) ]

                  Expect.equal
                      (Incremental.plan pipeline).Strategy
                      (ReferenceOnly(JoinNotRowPreserving(Incremental.joinKindName how)))
                      (sprintf "a '%s' join's output rows are not its left rows" (Incremental.joinKindName how))

              Expect.equal
                  (Incremental.reasonString (JoinNotRowPreserving "inner"))
                  "a 'inner' join's output rows are not its left rows"
                  "and the reason renders as a stable line"

          testCase "a cached verdict is NOT reused when the joined relation moved"
          <| fun _ ->
              // The condition the whole `JoinKeys` cache exists for. The delta describes the SOURCE,
              // so a row it did not name can still have a different verdict — the relation may have
              // gained or lost the key that row matched on. Here the source does not move AT ALL and
              // the delta is quiet; only the relation changes, and the answer must change with it.
              let pipeline = [ Join(Ref "lookup", [ "b", "k" ], Semi) ]

              let resolveTo (ks: int list) =
                  fun (name: string) ->
                      if name = "lookup" then
                          Ok
                              { Schema = [ "k", IntType ]
                                Columns = [ Column.create "k" IntType (ks |> List.map Int) ] }
                      else
                          Error(UnresolvedSource name)

              let before = resolveTo [ 0; 2 ]
              let after = resolveTo [ 1 ]

              let state = ok (Incremental.prime before Map.empty idw pipeline baseTable)
              let quiet = ok (Delta.diff idw baseTable baseTable)
              Expect.isTrue (Delta.isQuiet quiet) "the delta names nothing, and the source is the same table"

              let next =
                  ok (Incremental.refresh after Map.empty idw pipeline state quiet baseTable)

              Expect.equal
                  (Ok next.Output)
                  (DataFrame.evalPipelineWith after pipeline baseTable)
                  "the answer is the reference's over the RELATION AS IT NOW STANDS"

              Expect.equal
                  (next.Output |> Table.tryColumn "id" |> Option.map (fun c -> c.Cells))
                  (Some [ Str "r2"; Str "r3" ])
                  "the rows matching the new relation, not the old one"

              // And the reuse is not thrown away when the relation did NOT move: the same refresh
              // against the same relation answers from the cache and evaluates nothing.
              let same =
                  ok (Incremental.refresh before Map.empty idw pipeline state quiet baseTable)

              Expect.equal same.Footprint.Recompute (RowsRecomputed 0) "an unmoved relation costs nothing"
              Expect.equal same.Output state.Output "and answers exactly as before" ]
