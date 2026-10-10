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
open Fuaran.Compute.Tests

let private schema: Schema =
    [ Field.create "i" IntType
      Field.create "f" FloatType
      Field.create "s" StringType
      Field.create "b" BoolType ]

let private table (rows: (int option * float option * string option * bool option) list) : Table =
    let cell f v =
        match v with
        | Some x -> f x
        | None -> Null

    { Schema = schema
      Columns =
        [ KitColumn.create "i" IntType (rows |> List.map (fun (i, _, _, _) -> cell Int i))
          KitColumn.create "f" FloatType (rows |> List.map (fun (_, f, _, _) -> cell Float f))
          KitColumn.create "s" StringType (rows |> List.map (fun (_, _, s, _) -> cell Str s))
          KitColumn.create "b" BoolType (rows |> List.map (fun (_, _, _, b) -> cell Bool b)) ] }

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

/// Phase 282 — how many times fewer rows the fused top-n must hand to an ordering kernel than the
/// full sort does (Phase 423 moved the bound from bytes to rows; see the case).
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

          testCase "a filter moves ahead of a derive only when the derive is total, unread, and decided"
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

              // Phase 338: a total derive the typer DECIDES is admitted whatever its type. Phase 269
              // declined `f * 2` here because the evaluator typed the column from its cells, so a
              // table whose surviving rows are all null made it `string` after the filter and
              // `float` before. The column is now `float` on every frame, so the two orders agree on
              // exactly the table that used to expose the difference.
              let typed = [ floatDerive; totalPred ]
              let r = Plan.explain schema typed
              Expect.equal r.Planned [ totalPred; floatDerive ] "moved"
              Expect.equal (classes r) [ RewriteClass.FilterBeforeDerive ] "reported"
              agree typed sample

              let nullsSurvive =
                  table [ Some 1, None, Some "a", Some true; Some -1, Some 2.0, Some "b", Some false ]

              let written = ok (DataFrame.evalPipelineAsWritten typed nullsSurvive)

              let reordered =
                  ok (DataFrame.evalPipelineAsWritten [ totalPred; floatDerive ] nullsSurvive)

              Expect.equal written.Schema reordered.Schema "the reorder no longer changes the schema"
              Expect.equal written reordered "nor anything else"
              agree typed nullsSurvive

              // ... and a derive only its CELLS type is still declined, by that reason: a `Case` of
              // an int and a float, which the exact typer keeps apart (D3).
              let mixedArms =
                  [ Derive("g", Case([ Binary(Gt, Col "i", Lit(Int 0)), Lit(Int 1) ], Lit(Float 2.5)))
                    totalPred ]

              let d = Plan.explain schema mixedArms
              Expect.equal d.Planned mixedArms "declined"
              Expect.stringContains d.Declined[0].Reason "decided by its cells" "the rule"
              Expect.isFalse (Plan.isTotal schema mixedArms[0]) "and the verdict does not call it total"
              agree mixedArms sample

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
              // top-n keeps ten rows in order where the sort orders all of them — so it is held on
              // the ROWS each form hands to an ordering kernel (operator ruling 2026-10-10, Phase 423,
              // `DECISIONS.md` D18): the kernel set is wrapped so that every `SortFinite` and
              // `SortPositions` call adds the rows it was asked to order. The bytes each form
              // allocates, which Phase 282 gated on, are PRINTED beside it and not gated: that bound
              // was propped up by the `Table` boundary's conversion, which Phase 423 removed — with
              // the boundary a view, the full sort allocates less (1.49 MB against the fused form's
              // 2.31 MB at 20,000 rows) while the clock still favours the fused plan by an order of
              // magnitude. The clock figures, on `bestMs`, are printed too.
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

              // The rows handed to an ordering kernel by the run under way.
              let mutable ordered = 0

              let counting: KernelSet =
                  { Kernels.host with
                      SortFinite =
                          fun keys ->
                              ordered <- ordered + keys.Length
                              Kernels.host.SortFinite keys
                      SortPositions =
                          fun positions cmp ->
                              ordered <- ordered + positions.Length
                              Kernels.host.SortPositions positions cmp }

              let plannedRun () =
                  DataFrame.evalPreparedCountedWith counting DataFrame.noResolve Map.empty pipeline prepared
                  |> ok
                  |> ignore

              // As written: the steps folded one by one over the frame, with no rewrite and no fused
              // kernel — the `Sort` orders every row the `Filter` kept, the `Limit` then cuts.
              let writtenRun () =
                  (Ok(Frame.ofTable t), pipeline)
                  ||> List.fold (fun acc step ->
                      acc
                      |> Result.bind (fun f -> DataFrame.evalStepWith counting DataFrame.noResolve Map.empty f step))
                  |> ok
                  |> ignore

              let rowsOrderedBy (run: unit -> unit) : int =
                  ordered <- 0
                  run ()
                  ordered

              let plannedRows = rowsOrderedBy plannedRun
              let writtenRows = rowsOrderedBy writtenRun
              let plannedBytes = ScalingTests.allocatedBytes plannedRun
              let writtenBytes = ScalingTests.allocatedBytes writtenRun
              let plannedMs = ScalingTests.bestMs 5 plannedRun
              let writtenMs = ScalingTests.bestMs 5 writtenRun

              printfn
                  "Phase 269 — Filter > Sort > Limit 10 at 20,000 rows: rows ordered planned %d, as written %d; planned %d B, as written %d B (x%.2f, not gated); clock planned %.2f ms, as written %.2f ms"
                  plannedRows
                  writtenRows
                  plannedBytes
                  writtenBytes
                  (float writtenBytes / float plannedBytes)
                  plannedMs
                  writtenMs

              agree pipeline t

              // Not vacuous: the full sort orders the rows the filter kept, which is most of them.
              Expect.isGreaterThan writtenRows 15_000 "the full sort orders the filtered rows"

              Expect.isLessThan
                  (float plannedRows * fusedTopNWorkMargin)
                  (float writtenRows)
                  "the top-n orders fewer rows than the full sort" ]
