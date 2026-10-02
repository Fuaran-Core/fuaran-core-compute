module Fuaran.Compute.Tests.PlanTests

// ---------------------------------------------------------------------------
//  Phase 269 — the planner, pinned case by case.
//
//  `Conformance.plannerLaws` certifies the planned evaluation against the
//  reference as written over generated triples, and its adequacy guard insists
//  every rewrite class is reached. What the family cannot say is WHICH pipeline
//  reached which class, or that a rule declines exactly the shape it was written
//  to decline: a generated sample that happened to admit a reorder is evidence,
//  and the next seed decides whether it is evidence again. So each rule is
//  pinned here on the shape that exercises it, and each decline on the shape
//  the rule names — a premise the phase found false is pinned as a decline, so
//  that a later "improvement" admitting it goes red with the reason in front of
//  it.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private schema: Schema =
    [ "i", IntType; "f", FloatType; "s", StringType; "b", BoolType ]

let private table (rows: (int option * float option * string option * bool option) list) : Table =
    let cell f v =
        match v with
        | Some x -> f x
        | None -> Null

    { Schema = schema
      Columns =
        [ Column.create "i" IntType (rows |> List.map (fun (i, _, _, _) -> cell Int i))
          Column.create "f" FloatType (rows |> List.map (fun (_, f, _, _) -> cell Float f))
          Column.create "s" StringType (rows |> List.map (fun (_, _, s, _) -> cell Str s))
          Column.create "b" BoolType (rows |> List.map (fun (_, _, _, b) -> cell Bool b)) ] }

let private sample =
    table
        [ Some 3, Some 1.5, Some "x", Some true
          Some -1, None, Some "12", None
          None, Some 0.25, None, Some false
          Some 7, Some 1.5, Some "Abc", Some true
          Some 3, Some -2.0, Some "7", Some false ]

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private classes (r: PlanReport) : RewriteClass list = r.Applied |> List.map _.Class

let private declinedClasses (r: PlanReport) : RewriteClass list = r.Declined |> List.map _.Class

let private agree (pipeline: Transform list) (t: Table) =
    let planned = DataFrame.evalPipeline pipeline t
    let written = DataFrame.evalPipelineAsWritten pipeline t

    match planned, written with
    | Ok a, Ok b ->
        Expect.equal
            (ColumnCodec.encode (Embedded a))
            (ColumnCodec.encode (Embedded b))
            (sprintf "planned ≠ as written for %A" pipeline)
    | Error a, Error b -> Expect.equal a b (sprintf "planned error ≠ as-written error for %A" pipeline)
    | _ -> failtestf "planned and as-written disagree on Ok/Error for %A: %A vs %A" pipeline planned written

let private totalPred = Filter(Binary(Gt, Col "i", Lit(Int 0)))
let private partialPred = Filter(Binary(Gt, Cast(IntType, Col "s"), Lit(Int 5)))
let private strDerive = Derive("u", ApplyFn(Upper, [ Col "s" ]))
let private intDerive = Derive("d", Binary(Add, Col "i", Col "i"))
let private floatDerive = Derive("g", Binary(Mul, Col "f", Lit(Float 2.0)))

/// Phase 282 — how many times fewer bytes the fused top-n must allocate than the full sort.
let private fusedTopNWorkMargin = 4.0

[<Tests>]
let planTests =
    testList
        "Plan"
        [

          // ---- the totality verdict ----

          testCase "isTotal: the row-set verbs with literal slots are total, the gathering verbs are not"
          <| fun _ ->
              Expect.isTrue (Plan.isTotal schema (Transform.sortBy [ "i", Asc ])) "a literal sort"

              Expect.isTrue
                  (Plan.isTotal schema (Transform.sortBy [ "zzz", Asc ]))
                  "an unknown key is dropped, not refused"

              Expect.isFalse (Plan.isTotal schema (Sort [ Slot.Param "k", Asc ])) "a param key needs an env"
              Expect.isTrue (Plan.isTotal schema (Transform.limit 3 1)) "a literal limit"
              Expect.isFalse (Plan.isTotal schema (Limit(Slot.Param "n", Slot.Lit 0))) "a param limit"
              Expect.isTrue (Plan.isTotal schema Distinct) "distinct"
              Expect.isTrue (Plan.isTotal schema (Project [ "i", "j" ])) "a project over a known source"
              Expect.isFalse (Plan.isTotal schema (Project [ "zzz", "j" ])) "a project over an unknown source"
              Expect.isFalse (Plan.isTotal schema (GroupBy([ "b" ], []))) "an aggregate"
              Expect.isFalse (Plan.isTotal schema (Union(Ref "other"))) "a resolver"

          testCase "isTotal: the expression verdict names the arms that can refuse"
          <| fun _ ->
              let total e = Plan.isTotal schema (Filter e)
              Expect.isTrue (total (Binary(Gt, Col "i", Lit(Int 0)))) "an integer comparison"
              Expect.isTrue (total (Binary(Add, Col "f", Col "i"))) "float arithmetic never overflows"
              Expect.isFalse (total (Binary(Add, Col "i", Col "i"))) "integer add can overflow"
              Expect.isFalse (total (Binary(Mod, Col "i", Lit(Int 2)))) "mod is declined whole"
              Expect.isTrue (total (Binary(Div, Col "f", Col "i"))) "div by zero is null, not an error"
              Expect.isFalse (total (Binary(Gt, Col "s", Col "i"))) "a string against a number does not compare"
              Expect.isFalse (total (Binary(And, Col "i", Col "b"))) "a logical operator over a non-bool"
              Expect.isTrue (total (Binary(And, Col "b", IsNull(Col "s")))) "a logical operator over bools"
              Expect.isFalse (total (Cast(IntType, Col "s"))) "a cast that parses"
              Expect.isTrue (total (Cast(StringType, Col "f"))) "a cast to string accepts every cell"
              Expect.isFalse (total (Cast(IntType, Col "f"))) "a float to int cast can overflow"
              Expect.isTrue (total (Cast(FloatType, Col "i"))) "an int to float cast is exact"
              Expect.isFalse (total (Col "zzz")) "an unknown column"
              Expect.isFalse (total (Param "p")) "a param"
              Expect.isFalse (total (Now NowGrain.Date)) "an unpinned clock"
              Expect.isTrue (total (ApplyFn(Upper, [ Col "s" ]))) "upper over a string"
              Expect.isFalse (total (ApplyFn(Upper, [ Col "i" ]))) "upper over an int"
              Expect.isFalse (total (ApplyFn(Abs, [ Col "i" ]))) "abs of Int32.MinValue throws"
              Expect.isTrue (total (ApplyFn(Abs, [ Col "f" ]))) "abs of a float"

              Expect.isFalse
                  (total (ApplyFn(Substr, [ Col "s"; Col "i"; Lit(Int 2) ])))
                  "a substr start that may be null"

              Expect.isTrue
                  (total (ApplyFn(Substr, [ Col "s"; Lit(Int 0); Lit(Int 2) ])))
                  "a substr with literal bounds"

              Expect.isFalse (total (ApplyFn(DatePart, [ Lit(Str "year"); Col "s" ]))) "a date that parses"
              Expect.isTrue (total (ApplyFn(Concat, [ Col "s"; Col "i" ]))) "concat stringifies anything"
              Expect.isFalse (total (ApplyFn(Concat, []))) "concat of nothing is an arity error"
              Expect.isTrue (total (InList(Col "i", [ Lit(Int 1); Lit(Float 2.0) ]))) "membership over numbers"
              Expect.isFalse (total (InList(Col "i", [ Lit(Str "1") ]))) "membership across types"
              Expect.isTrue (total (Case([ Binary(Gt, Col "i", Lit(Int 0)), Lit(Str "p") ], Col "s"))) "a case"

          // ---- fusion ----

          testCase "Sort > Limit is reported as a top-n and answers what the sort then the limit answer"
          <| fun _ ->
              let pipeline = [ Transform.sortBy [ "f", Desc; "i", Asc ]; Transform.limit 2 1 ]
              let report = Plan.explain schema pipeline
              Expect.equal report.Planned pipeline "the pair stays in the pipeline; the driver fuses it"
              Expect.equal (classes report) [ RewriteClass.TopN ] "reported as the fusion"
              agree pipeline sample

          testCase "the top-n kernel equals sort then limit over every window, ties and nulls included"
          <| fun _ ->
              // Ties on every key, nulls in the key, and windows from empty to past the end.
              let rows =
                  [ for k in 0..39 ->
                        (if k % 5 = 0 then None else Some(k % 4)),
                        (if k % 7 = 0 then None else Some(float (k % 3))),
                        Some(string (k % 6)),
                        Some(k % 2 = 0) ]

              let t = table rows

              for by in
                  [ [ "i", Asc ]
                    [ "f", Desc ]
                    [ "i", Desc; "f", Asc ]
                    [ "s", Asc; "i", Desc ] ] do
                  for n in [ -1; 0; 1; 3; 10; 39; 40; 100 ] do
                      for offset in [ -2; 0; 1; 5; 38; 40 ] do
                          agree [ Transform.sortBy by; Transform.limit n offset ] t

          testCase "the slots of a fused pair resolve through the env, first error first"
          <| fun _ ->
              let pipeline = [ Sort [ Slot.Param "k", Asc ]; Limit(Slot.Param "n", Slot.Lit 0) ]
              let env = Map.ofList [ "k", Str "i"; "n", Int 2 ]

              Expect.equal
                  (DataFrame.evalPipelineInEnv env pipeline sample)
                  (DataFrame.evalPipelineWithInEnvAsWritten DataFrame.noResolve env pipeline sample)
                  "bound slots"

              Expect.equal
                  (DataFrame.evalPipelineInEnv Map.empty pipeline sample)
                  (DataFrame.evalPipelineWithInEnvAsWritten DataFrame.noResolve Map.empty pipeline sample)
                  "unbound: the sort's param is the first error"

          // ---- projection pruning ----

          testCase "a dead column is dropped ahead of the project that would drop it, at its last read"
          <| fun _ ->
              let pipeline = [ strDerive; totalPred; Project [ "i", "i"; "u", "u" ] ]
              let report = Plan.explain schema pipeline

              // The filter moves ahead of the string derive first (a reorder), then f and b die
              // at the start (nothing reads them); s dies after the derive reads it, but the step
              // after the derive is the project that drops it, so nothing is inserted there.
              Expect.equal
                  report.Planned
                  [ Project [ "i", "i"; "s", "s" ]
                    totalPred
                    strDerive
                    Project [ "i", "i"; "u", "u" ] ]
                  "f and b die at the start; the project drops s itself"

              Expect.equal
                  (classes report)
                  [ RewriteClass.FilterBeforeDerive; RewriteClass.PruneColumns ]
                  "one reorder, one prune"

              agree pipeline sample

          testCase "pruning stops at a step that reads the whole row, and at one that reads an unknown column"
          <| fun _ ->
              let acrossDistinct = [ totalPred; Distinct; Project [ "i", "i" ] ]
              Expect.equal (Plan.rewrite schema acrossDistinct) acrossDistinct "never across Distinct"

              let unknown = [ Filter(Col "zzz"); Project [ "i", "i" ] ]
              Expect.equal (Plan.rewrite schema unknown) unknown "an UnknownColumn refusal names the schema it saw"
              agree unknown sample

          testCase "pruning ahead of a groupBy keeps exactly the keys and the aggregated columns"
          <| fun _ ->
              let pipeline =
                  [ totalPred; GroupBy([ "b" ], [ { Name = "n"; Fn = Count; Of = "i" } ]) ]

              let report = Plan.explain schema pipeline

              Expect.equal
                  report.Planned
                  [ Project [ "i", "i"; "b", "b" ]; totalPred; pipeline[1] ]
                  "f and s are dead from the start"

              agree pipeline sample

          // ---- reordering ----

          testCase "a total filter moves ahead of a literal sort; a filter that may error does not"
          <| fun _ ->
              let admitted = [ Transform.sortBy [ "i", Asc ]; totalPred ]
              let r = Plan.explain schema admitted
              Expect.equal r.Planned [ totalPred; Transform.sortBy [ "i", Asc ] ] "moved"
              Expect.equal (classes r) [ RewriteClass.FilterBeforeSort ] "reported"
              agree admitted sample

              let declined = [ Transform.sortBy [ "i", Asc ]; partialPred ]
              let d = Plan.explain schema declined
              Expect.equal d.Planned declined "as written"
              Expect.equal (declinedClasses d) [ RewriteClass.FilterBeforeSort ] "declined, with the reason"
              Expect.stringContains d.Declined[0].Reason "may error" "the rule"
              agree declined sample

          testCase "a filter moves ahead of a derive only when the derive is total, unread, and string-typed"
          <| fun _ ->
              let admitted = [ strDerive; totalPred ]
              let r = Plan.explain schema admitted
              Expect.equal r.Planned [ totalPred; strDerive ] "moved"
              Expect.equal (classes r) [ RewriteClass.FilterBeforeDerive ] "reported"
              agree admitted sample

              let notTotal = [ intDerive; totalPred ]
              Expect.equal (Plan.rewrite schema notTotal) notTotal "an integer add may overflow on a dropped row"
              Expect.stringContains (Plan.explain schema notTotal).Declined[0].Reason "may error" "the rule"

              let reads = [ strDerive; Filter(Binary(Eq, Col "u", Lit(Str "X"))) ]
              Expect.equal (Plan.rewrite schema reads) reads "the filter reads the derived column"

              // THE PREMISE THE PHASE FOUND FALSE, pinned: a total derive whose type the evaluator
              // infers from its cells is NOT safe to filter ahead of. Over the sample, `f * 2` is a
              // float column; over the two rows `i > 0` keeps... it still is, but over a table
              // whose surviving rows are all null it would be a string column, and the planner
              // cannot know which table it will meet. The rule declines by TYPE, so this pins the
              // decline and the reason rather than a table that happens to expose it.
              let typed = [ floatDerive; totalPred ]
              let d = Plan.explain schema typed
              Expect.equal d.Planned typed "declined"
              Expect.stringContains d.Declined[0].Reason "inferred from its cells" "the rule"

              // And the table that exposes it, for the record: every surviving row's derived cell
              // is null, so the derived column is `string` after the filter and `float` before.
              let nullsSurvive =
                  table [ Some 1, None, Some "a", Some true; Some -1, Some 2.0, Some "b", Some false ]

              let written = ok (DataFrame.evalPipelineAsWritten typed nullsSurvive)

              let reordered =
                  ok (DataFrame.evalPipelineAsWritten [ totalPred; floatDerive ] nullsSurvive)

              Expect.notEqual written.Schema reordered.Schema "the reorder would change the schema"
              agree typed nullsSurvive

          testCase "a filter bubbles past several steps, and stops at the first the rule declines"
          <| fun _ ->
              let past = [ Transform.sortBy [ "i", Asc ]; strDerive; totalPred ]
              let r = Plan.explain schema past
              Expect.equal r.Planned [ totalPred; Transform.sortBy [ "i", Asc ]; strDerive ] "two hops"
              Expect.equal (classes r) [ RewriteClass.FilterBeforeDerive; RewriteClass.FilterBeforeSort ] "in hop order"
              agree past sample

              let stopped = [ Transform.sortBy [ "i", Asc ]; strDerive; intDerive; totalPred ]
              let d = Plan.explain schema stopped
              Expect.equal d.Planned stopped "the int derive declines; the string derive and the sort are never reached"
              Expect.equal (declinedClasses d) [ RewriteClass.FilterBeforeDerive ] "one decline, at the int derive"
              agree stopped sample

          testCase "every rewrite composes: reorder, then prune, then the fused pair"
          <| fun _ ->
              let pipeline =
                  [ Transform.sortBy [ "i", Asc ]
                    totalPred
                    Transform.limit 3 0
                    strDerive
                    Project [ "u", "u" ] ]

              let r = Plan.explain schema pipeline

              Expect.equal
                  (classes r |> List.distinct |> List.sort)
                  [ RewriteClass.TopN; RewriteClass.PruneColumns; RewriteClass.FilterBeforeSort ]
                  "all three classes"

              Expect.isTrue
                  (r.Planned
                   |> List.pairwise
                   |> List.exists (fun pair ->
                       match pair with
                       | Sort _, Limit _ -> true
                       | _ -> false))
                  "pruning never splits the fused pair"

              Expect.equal (Plan.rewrite schema r.Planned) r.Planned "idempotent"
              agree pipeline sample

          testCase "the planner is total over an empty pipeline and an empty schema"
          <| fun _ ->
              Expect.equal (Plan.rewrite [] []) [] "empty"

              Expect.equal
                  (Plan.rewrite [] [ totalPred; Transform.sortBy [ "i", Asc ] ])
                  [ totalPred; Transform.sortBy [ "i", Asc ] ]
                  "unknown schema: as written"

          // ---- the seam ----

          testCase "the incremental seam runs the planned form and says so"
          <| fun _ ->
              let pipeline = [ Transform.sortBy [ "i", Asc ]; totalPred; Transform.limit 2 0 ]
              let idw = RowIdentity.byColumn "s"
              let state = ok (Incremental.prime DataFrame.noResolve Map.empty idw pipeline sample)
              Expect.equal (Incremental.pipelineOf state) pipeline "the written form is what a refresh compares against"
              Expect.equal (Incremental.plannedOf state) (Plan.rewrite schema pipeline) "the planned form ran"
              let p, report = Incremental.planOver schema pipeline
              Expect.equal p (Incremental.plan' state) "planOver classifies what the state classified"
              Expect.equal report.Planned (Incremental.plannedOf state) "and reports the same form"

              Expect.equal
                  (Incremental.result state)
                  (ok (DataFrame.evalPipelineAsWritten pipeline sample))
                  "the reference's answer"

          // ---- the acceptance: Filter > Sort > Limit 10 at 20,000 rows ----

          testCase "Filter > Sort > Limit 10 at 20,000 rows: the fused pair does less work than the full sort"
          <| fun _ ->
              // Phase 282 — COUNTED, where Phase 269 timed it (with a mean of five, which carried the
              // noise the family's `bestMs` minimum exists to remove). The claim is about WORK — the
              // top-n keeps ten rows in order where the sort orders all of them — so it is held on the
              // bytes each form allocates on this thread, which no other process can move. The clock
              // figures, on `bestMs`, are printed beside it.
              let rows =
                  [ for k in 0..19_999 ->
                        Some((k * 7919) % 10_007),
                        Some(float ((k * 104_729) % 1_000)),
                        Some(string (k % 97)),
                        Some(k % 2 = 0) ]

              let t = table rows
              let prepared = DataFrame.prepare t

              let pipeline =
                  [ Filter(Binary(Gt, Col "i", Lit(Int 100)))
                    Transform.sortBy [ "f", Desc; "i", Asc ]
                    Transform.limit 10 0 ]

              let plannedRun () =
                  DataFrame.evalPrepared DataFrame.noResolve Map.empty pipeline prepared
                  |> ok
                  |> ignore

              let writtenRun () =
                  DataFrame.evalPipelineWithInEnvAsWritten
                      DataFrame.noResolve
                      Map.empty
                      pipeline
                      (DataFrame.toTable prepared)
                  |> ok
                  |> ignore

              let plannedBytes = ScalingTests.allocatedBytes plannedRun
              let writtenBytes = ScalingTests.allocatedBytes writtenRun
              let plannedMs = ScalingTests.bestMs 5 plannedRun
              let writtenMs = ScalingTests.bestMs 5 writtenRun

              printfn
                  "Phase 269 — Filter > Sort > Limit 10 at 20,000 rows: planned %d B, as written %d B (x%.2f); clock planned %.2f ms, as written %.2f ms"
                  plannedBytes
                  writtenBytes
                  (float writtenBytes / float plannedBytes)
                  plannedMs
                  writtenMs

              agree pipeline t

              Expect.isLessThan
                  (float plannedBytes * fusedTopNWorkMargin)
                  (float writtenBytes)
                  "the top-n does less than the full sort" ]
