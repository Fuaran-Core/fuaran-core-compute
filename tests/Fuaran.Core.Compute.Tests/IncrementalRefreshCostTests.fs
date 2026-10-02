module Fuaran.Compute.Tests.IncrementalRefreshCostTests

// ---------------------------------------------------------------------------
//  Phase 208 — what a restricted refresh PAYS FOR, counted rather than timed,
//  and the two correctness cases the re-keying fixed.
//
//  ScalingTests holds the wall-clock half: a restricted refresh finishes sooner
//  than the full evaluation it replaces, now for a ONE-COMPARISON row expression
//  as well as for a costly one. This file holds the half a clock cannot state,
//  and it is counted for the reason Phase 207 recorded: a TIMED comparison of two
//  small loops does not discriminate, because the small size is measured in
//  tier-0 JIT code and the large one in tier-1 after the loop has been promoted.
//  Counting is exact, clock-free and identical on every host, which is what the
//  footprint instrument (Phase 117) already does and what GP6 asks of this
//  library.
//
//  The claim is stated precisely, because the loose form is false and Phase 207
//  measured why. A refresh is handed the WHOLE new source and must read every row
//  of it to know what moved — that pass is proportional to the table and there is
//  no way through it from this entry point. What this file proves is that
//  everything ELSE is proportional to the DELTA: the keyed lookups, the identity
//  mints and the cache writes that used to dominate a refresh are now performed
//  for the rows the delta names and for no others.
// ---------------------------------------------------------------------------

open Expecto
open Fuaran.Core
open Fuaran.Compute

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

/// The two sizes and the bound, on ScalingTests' own terms: twenty times the rows.
let private small = 1_000
let private large = 20_000
let private sizeRatio = float large / float small

let private build (n: int) : Table =
    { Schema = [ "id", StringType; "grp", StringType; "a", IntType ]
      Columns =
        [ Column.create "id" StringType [ for i in 0 .. n - 1 -> Str("r" + string i) ]
          Column.create "grp" StringType [ for i in 0 .. n - 1 -> Str("g" + string (i % 17)) ]
          Column.create "a" IntType [ for i in 0 .. n - 1 -> Int i ] ] }

/// Edit `d` rows, spread through the table so the edit is not a prefix.
let private editSome (d: int) (t: Table) : Table =
    let n = Table.rowCount t
    let stride = max 1 (n / d)

    let edited i = i % stride = 0 && i / stride < d

    { t with
        Columns =
            t.Columns
            |> List.map (fun c ->
                if c.Name <> "a" then
                    c
                else
                    { c with
                        Cells = c.Cells |> List.mapi (fun i cell -> if edited i then Int -1 else cell) }) }

let private pipeline: Transform list =
    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
      GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" } ]) ]

// ---------------------------------------------------------------------------
//  The two models of the refresh's per-row bookkeeping, each COUNTING the keyed
//  operations it performs — a hash or tree lookup, a tree insertion, or an
//  identity mint. They are MODELS rather than a second evaluator, and they are
//  here for the reason Phase 207's two `Limit` shapes were: "the refresh pays for
//  the delta" is a statement about a cost curve, and a cost curve is only a
//  finding if the shape it excludes can be shown to FAIL the same instrument.
//
//  `keyedShipped` mirrors what `runIncremental` and `groupStep` now do: the row
//  cache is an array indexed by the row's slot, a row still sitting where it sat
//  is recognised by a pointer comparison, and a group identity is CARRIED for a
//  row whose cells have not moved. `keyedByMap` mirrors what they did until
//  `0.27.0`: every row looked up in a string-keyed map, written back into a
//  freshly built one, and its group identity minted again.
//
//  Both also report the FRAME VISITS they make, which are `n` for both and are
//  what Phase 207's finding is about: the pass over the source is not what this
//  phase removes, and a proof that pretended otherwise would be measuring
//  something the seam does not do.
// ---------------------------------------------------------------------------

/// `(keyed operations, frame visits)` for a refresh of `n` rows whose delta names `d`.
let private keyedShipped (n: int) (d: int) : int * int =
    let mutable keyed = 0
    let mutable visits = 0

    for i in 0 .. n - 1 do
        visits <- visits + 1
        let named = i % (max 1 (n / d)) = 0 && i / (max 1 (n / d)) < d

        if named then
            // A named row's identity is re-derived and its group re-minted; the array store for
            // every row is O(1), allocates nothing and is not a keyed operation.
            keyed <- keyed + 2

    keyed, visits

let private keyedByMap (n: int) (d: int) : int * int =
    let mutable keyed = 0
    let mutable visits = 0
    ignore d

    for _ in 0 .. n - 1 do
        visits <- visits + 1
        // Two lookups into the prior cache map, one insertion into the new one, one group-identity
        // mint, one insertion into the row-to-group map. Per row, whatever the delta says.
        keyed <- keyed + 5

    keyed, visits

[<Tests>]
let refreshCostTests =
    testList
        "RefreshCost"
        [ testCase "the refresh's keyed work is counted by the DELTA, and the shape it replaced counted by the TABLE"
          <| fun _ ->
              let shippedSmall, visitsSmall = keyedShipped small 1
              let shippedLarge, visitsLarge = keyedShipped large 1
              let mapSmall, _ = keyedByMap small 1
              let mapLarge, _ = keyedByMap large 1

              printfn
                  "  [cost] keyed operations, one row edited:  shipped %d @ %d -> %d @ %d   map-keyed %d -> %d   (size ratio %.0f)"
                  shippedSmall
                  small
                  shippedLarge
                  large
                  mapSmall
                  mapLarge
                  sizeRatio

              // The shipped shape: EXACTLY equal at both sizes. Not "within a factor" — the count
              // does not depend on `n` at all, which is the whole claim.
              Expect.equal
                  shippedLarge
                  shippedSmall
                  "twenty times the rows, the SAME keyed work — the refresh pays for the delta"

              // The go-red half. If this ever stops holding, the bound above has stopped
              // discriminating and the case proves nothing.
              Expect.equal
                  (float mapLarge / float mapSmall)
                  sizeRatio
                  "the shape this replaced counted the TABLE — twenty times the rows, twenty times the keyed work"

              Expect.isGreaterThan
                  mapLarge
                  (shippedLarge * 1000)
                  "and at twenty thousand rows the difference is three orders of magnitude, not a constant factor"

              // Stated rather than hidden (Phase 207's finding): the pass over the frame is NOT
              // what this phase removes. Both shapes visit every row, because the refresh is handed
              // the whole new source and the output contains every surviving row.
              Expect.equal visitsSmall small "both shapes visit every row of the frame"
              Expect.equal visitsLarge large "at both sizes — the source scan is the floor, not the target"

          testCase "and it grows with the delta, proportionally"
          <| fun _ ->
              let one, _ = keyedShipped large 1
              let ten, _ = keyedShipped large 10
              let hundred, _ = keyedShipped large 100

              printfn "  [cost] keyed operations @ %d rows:  d=1 %d   d=10 %d   d=100 %d" large one ten hundred

              Expect.equal (ten / one) 10 "ten times the delta, ten times the keyed work"
              Expect.equal (hundred / one) 100 "a hundred times the delta, a hundred times the keyed work"

          // ================= the real seam, counted through its own footprint =================

          testCase "the real refresh re-evaluates the delta's rows and no others, at either size"
          <| fun _ ->
              // The footprint is the shipped instrument and it is a COUNT. What it adds to the
              // models above is that it is measured on the real walk: the same delta against a
              // twentyfold larger table recomputes the same number of rows and the same number of
              // groups.
              let measure (n: int) =
                  let before = build n
                  let after = editSome 1 before
                  let state = ok (Incremental.primeOn idw pipeline before)
                  let delta = ok (Delta.diff idw before after)
                  let refreshed = ok (Incremental.refreshOn idw pipeline state delta after)

                  Expect.equal
                      (Ok(Incremental.result refreshed))
                      (DataFrame.evalPipeline pipeline after)
                      "refresh = reference"

                  Incremental.footprint refreshed

              let fSmall = measure small
              let fLarge = measure large

              printfn "  [cost] footprint @ %d: %A" small fSmall.Recompute
              printfn "  [cost] footprint @ %d: %A" large fLarge.Recompute

              Expect.equal
                  fLarge.Recompute
                  fSmall.Recompute
                  "twenty times the rows, the same recompute — and the same group count"

              Expect.equal fSmall.SourceRows small "the footprint still reports the source it ran over"
              Expect.equal fLarge.SourceRows large "at both sizes"

          // ================= the moved-row paths the positional cache falls back to =================

          testCase "a row that MOVED is found by the fallback index, not by its slot"
          <| fun _ ->
              // The positional cache is exact only for a row still sitting where it sat. These are
              // the three ways that fails, and each must still answer as the reference does — a
              // wrong answer here is what a positional cache gets wrong if its validity condition
              // is loose.
              let before = build 200

              let reordered =
                  { before with
                      Columns = before.Columns |> List.map (fun c -> { c with Cells = List.rev c.Cells }) }

              let inserted =
                  { before with
                      Columns =
                          before.Columns
                          |> List.map (fun c ->
                              let head =
                                  match c.Name with
                                  | "id" -> Str "rNEW"
                                  | "grp" -> Str "g0"
                                  | _ -> Int 7

                              { c with Cells = head :: c.Cells }) }

              let removed =
                  { before with
                      Columns = before.Columns |> List.map (fun c -> { c with Cells = List.tail c.Cells }) }

              for label, after in
                  [ "every row reordered", reordered
                    "a row inserted at the FRONT, so every slot shifts", inserted
                    "a row removed from the front, so every slot shifts the other way", removed ] do
                  let state = ok (Incremental.primeOn idw pipeline before)
                  let delta = ok (Delta.diff idw before after)
                  let refreshed = ok (Incremental.refreshOn idw pipeline state delta after)

                  Expect.equal
                      (Ok(Incremental.result refreshed))
                      (DataFrame.evalPipeline pipeline after)
                      (label + ": refresh = reference")

                  // And the state that comes out of it still answers the NEXT delta correctly —
                  // a positional cache written against the wrong slots would surface one tick later.
                  let after2 = editSome 1 after
                  let delta2 = ok (Delta.diff idw after after2)
                  let refreshed2 = ok (Incremental.refreshOn idw pipeline refreshed delta2 after2)

                  Expect.equal
                      (Ok(Incremental.result refreshed2))
                      (DataFrame.evalPipeline pipeline after2)
                      (label + ": and the refresh AFTER it = reference")

          // ================= the correctness defect the re-keying fixed =================

          testCase "a step after a Window does not read a cache the window moved"
          <| fun _ ->
              // MEASURED WRONG BEFORE THIS PHASE, on the shipped code, and this is the case that
              // caught it. A `Window` recomputes its column over the whole frame it is handed, so a
              // row the delta never named comes out of it with a different cell whenever another row
              // in its partition moved. The cache condition was "the delta did not name this row",
              // which is not the same statement — and a step after the window then reused an answer
              // to a question that had changed.
              //
              // **Phase 212 put this class in the generated corpus (`IncrementalDelta`, shapes
              // `38`–`47`) and this case STAYS, because the two answer different questions.** The
              // corpus shapes are drawn: they reach the defect on a share of samples under some of
              // the generator's edits, and what they certify is that the CLASS is covered for every
              // host that runs the family. These three are a fixed eight-row table with one cell
              // edited, so they reach it on every run of every seed, and they are the pipelines the
              // wrong answer was measured on in `v0.26.0` — a regression test names the instance,
              // a conformance family names the class, and losing either would lose something.
              //
              // The third case earns its place on a further measurement Phase 212 made: with the
              // pre-`0.28.0` predicate reintroduced, the `Filter` and the `Derive` cases go RED and
              // the maintained `GroupBy` stays GREEN. A group re-aggregates from its member rows and
              // never consults the per-row cache, so it could not have carried this defect — which
              // is exactly why the corpus's two window→`GroupBy` shapes (`23`, `37`) sat through the
              // whole life of the bug without seeing it. It is kept as the recorded NEGATIVE: the
              // boundary of the class, asserted rather than assumed.
              //
              // On ten rows with one edited, `Filter > Window(cumulSum) > Filter(on the window
              // column)` and the same with a `Derive` both DISAGREED with the reference evaluator.
              // The condition is now `Stable` — "this row's cells are byte-identical to the ones the
              // prior evaluation held for it" — which a window clears for every row.
              let win: WindowSpec =
                  { PartitionBy = [ "grp" ]
                    OrderBy = [ "id", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }

              let before =
                  { Schema = [ "id", StringType; "grp", StringType; "a", IntType ]
                    Columns =
                      [ Column.create "id" StringType [ for i in 0..9 -> Str("r" + string i) ]
                        Column.create "grp" StringType [ for _ in 0..9 -> Str "g" ]
                        Column.create "a" IntType [ for i in 0..9 -> Int(i % 3) ] ] }

              let after =
                  { before with
                      Columns =
                          before.Columns
                          |> List.map (fun c ->
                              if c.Name <> "a" then
                                  c
                              else
                                  { c with
                                      Cells = c.Cells |> List.mapi (fun i cell -> if i = 2 then Int 50 else cell) }) }

              let cases =
                  [ "a Filter reading the window's column",
                    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
                      Window win
                      Filter(Binary(Gt, Col "run", Lit(Int 3))) ]
                    "a Derive reading the window's column",
                    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
                      Window win
                      Derive("d", Binary(Add, Col "run", Lit(Int 1))) ]
                    "a maintained GroupBy aggregating the window's column",
                    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
                      Window win
                      GroupBy([ "grp" ], [ { Name = "s"; Fn = Sum; Of = "run" } ]) ] ]

              for label, p in cases do
                  // The pipeline is ADMITTED — that is what makes this a live case rather than a
                  // hypothetical. A declined pipeline would answer through the reference evaluator
                  // and could not be wrong this way.
                  match (Incremental.plan p).Strategy with
                  | ReferenceOnly r -> failtestf "%s: expected an admitted pipeline, got ReferenceOnly %A" label r
                  | _ -> ()

                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)
                  let refreshed = ok (Incremental.refreshOn idw p state delta after)

                  Expect.equal
                      (Ok(Incremental.result refreshed))
                      (DataFrame.evalPipeline p after)
                      (label + " after a Window: refresh = reference")

          testCase "a merged order does not reuse the position of a row a window moved"
          <| fun _ ->
              // ===================================================================================
              //  Phase 215 — the THIRD site of Phase 208's substitution, and the regression test
              //  this file carried as a PENDING FINDING until it was fixed.
              //
              //  WHAT WAS WRONG. Phase 208 replaced the per-row cache condition `not Affected` ("the
              //  delta did not name this row") with `Stable` ("this row's cells are byte-identical
              //  to the ones the prior evaluation held"), because a `Window` recomputes its column
              //  over the whole frame and moves rows the delta never named. It changed the two sites
              //  it had found — `cellAt`, and the join's cached verdict — and left a THIRD standing:
              //  `walk`'s `WSort` arm built its reusable set from `not w.Affected`, so a merged
              //  order reused the cached POSITION of a row whose sort key a window had moved. Phase
              //  212's new corpus shapes found it; this phase fixed it.
              //
              //  IT SHIPPED. Measured at Phase 215 against the RELEASED packages themselves — a
              //  probe pinned to one published `Fuaran.Compute.DataFrame` at a time —
              //  `window(rank) > sort(rk)` disagrees with that same package's reference evaluator on
              //  every release from `0.19.0` (the first that admits a partition-global window)
              //  through `0.28.0`, and agrees on `0.16.0` and `0.17.0`, which predate the merged
              //  order and are the probe's falsifier. `0.18.0` carries the same reuse condition and
              //  admits bounded-frame windows only; `window(lag) > sort(prev)` is red from `0.19.0`
              //  too, which is what makes that release reachable as well.
              //
              //  WHY THESE PIPELINES AND NOT THE CORPUS. `IncrementalDelta` shape `44` now sorts on
              //  the window's own column and covers the CLASS for every host that runs the family;
              //  its draws reach the defect on a share of samples. These are a fixed eight-row table
              //  with one cell edited, so they reach it on every run — a regression test names the
              //  instance, a conformance family names the class.
              // ===================================================================================
              let rankWin: WindowSpec =
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }

              let sumWin: WindowSpec =
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }

              let mk (aCells: Cell list) =
                  { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "id" StringType [ for i in 0..7 -> Str("r" + string i) ]
                        Column.create "a" IntType aCells
                        Column.create "b" IntType [ Int 1; Int 0; Int 0; Int 1; Int 1; Int 0; Int 1; Int 2 ] ] }

              // The generator's own counterexample, `IncrementalDelta` seed 33 iteration 3, with its
              // `changeFirstA` edit: one cell of one row moves, and the ranks of five others move
              // with it.
              let before = mk [ Int -3; Int 1; Int 3; Int 6; Int 6; Int 4; Int 0; Int -1 ]
              let after = mk [ Int 42; Int 1; Int 3; Int 6; Int 6; Int 4; Int 0; Int -1 ]

              let cases =
                  [ "a sort keyed on a rank the window appended", [ Window rankWin; Transform.sortBy [ "rk", Asc ] ]
                    "a sort keyed on a column DERIVED from the window's",
                    [ Window rankWin
                      Derive("d", Binary(Add, Col "rk", Col "a"))
                      Transform.sortBy [ "d", Asc ] ]
                    "a sort keyed on a running total the window appended",
                    [ Window sumWin; Transform.sortBy [ "run", Asc ] ] ]

              // Each of the three both WITH and WITHOUT the cut. The truncation is not what is
              // wrong — the ORDER is — and a regression test that only ever looked through a `Limit`
              // would keep passing if the merge were fixed only in the rows the cut happens to keep.
              for label, p in cases do
                  for cut, suffix in [ [], " (no cut)"; [ Transform.limit 3 0 ], " > limit" ] do
                      let p = p @ cut

                      // The pipeline is ADMITTED — that is what makes this a live case rather than a
                      // hypothetical. A declined pipeline would answer through the reference
                      // evaluator and could not be wrong this way.
                      match (Incremental.plan p).Strategy with
                      | ReferenceOnly r ->
                          failtestf "%s%s: expected an admitted pipeline, got ReferenceOnly %A" label suffix r
                      | _ -> ()

                      let state = ok (Incremental.primeOn idw p before)
                      let delta = ok (Delta.diff idw before after)
                      let refreshed = ok (Incremental.refreshOn idw p state delta after)

                      Expect.equal
                          (Ok(Incremental.result refreshed))
                          (DataFrame.evalPipeline p after)
                          (label + suffix + ": refresh = reference")

          testCase "the merged order's boundary: what was always sound, and what was not"
          <| fun _ ->
              // The other direction of the same probe, kept in the suite rather than in a session's
              // memory, because without it the case above is a claim about one pipeline. Two of
              // these three were sound under the OLD condition too — a row the delta did not name
              // genuinely has the same key when the key reads source columns alone — and the third
              // was the defect. All three agree now; the record of which was which is the comment,
              // and it is what says why shape `44`'s key had to move rather than its window.
              let mk (aCells: Cell list) =
                  { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "id" StringType [ for i in 0..7 -> Str("r" + string i) ]
                        Column.create "a" IntType aCells
                        Column.create "b" IntType [ Int 1; Int 0; Int 0; Int 1; Int 1; Int 0; Int 1; Int 2 ] ] }

              let before = mk [ Int -3; Int 1; Int 3; Int 6; Int 6; Int 4; Int 0; Int -1 ]
              let after = mk [ Int 42; Int 1; Int 3; Int 6; Int 6; Int 4; Int 0; Int -1 ]

              let win: WindowSpec =
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }

              let agrees (p: Transform list) =
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)
                  let refreshed = ok (Incremental.refreshOn idw p state delta after)
                  Ok(Incremental.result refreshed) = DataFrame.evalPipeline p after

              Expect.isTrue
                  (agrees
                      [ Derive("d", Binary(Add, Col "a", Col "b"))
                        Transform.sortBy [ "d", Asc ]
                        Transform.limit 3 0 ])
                  "a merged order on a key derived from source columns alone — sound under both conditions"

              Expect.isTrue
                  (agrees [ Window win; Transform.sortBy [ "b", Asc ]; Transform.limit 3 0 ])
                  "a merged order on a SOURCE column behind a window — sound under both conditions"

              Expect.isTrue
                  (agrees [ Window win; Transform.sortBy [ "rk", Asc ]; Transform.limit 3 0 ])
                  "a merged order on the window's OWN column — the defect Phase 215 fixed; no derive and no truncation required"

          testCase "a state's caches are engine-owned, and the accessors are what a consumer reads"
          <| fun _ ->
              // The surface half of the phase, pinned so a later widening is a deliberate act: the
              // representation is private, and these five accessors are the whole of what a state
              // answers. A consumer that needs something else needs a new accessor and the entry in
              // STABILITY.md that comes with it.
              let before = build 50
              let after = editSome 1 before
              let state = ok (Incremental.primeOn idw pipeline before)
              let delta = ok (Delta.diff idw before after)
              let refreshed = ok (Incremental.refreshOn idw pipeline state delta after)

              Expect.equal
                  (Ok(Incremental.result refreshed))
                  (DataFrame.evalPipeline pipeline after)
                  "result: the table the state holds"

              Expect.equal (Incremental.footprint refreshed).SourceRows 50 "footprint: what producing it cost"
              Expect.equal (Incremental.strategy refreshed) RowLocalThenGroups "strategy: how the next refresh answers"

              // Phase 269 — the seam classifies the PLANNED form (here the planner prunes the two
              // columns the group-by never reads, a row-local `Project` ahead of the filter), so
              // the classification `plan` computes from the written pipeline alone is one step
              // short of it; `planOver` computes what the seam classified.
              Expect.equal
                  (Incremental.plan' refreshed)
                  (fst (Incremental.planOver before.Schema pipeline))
                  "plan': the classification `planOver` computes from the schema and the pipeline"

              Expect.notEqual
                  (Incremental.plan' refreshed)
                  (Incremental.plan pipeline)
                  "and it is not the written form's: the planner rewrote this pipeline"

              Expect.equal
                  (Incremental.plannedOf refreshed)
                  (Plan.rewrite before.Schema pipeline)
                  "plannedOf: the form the seam ran"

              Expect.equal (Incremental.source refreshed) after "source: the table the next delta must describe FROM"
              Expect.equal (Incremental.pipelineOf refreshed) pipeline "pipelineOf: the pipeline it was built for" ]


// ---------------------------------------------------------------------------
//  Phase 268 — the chunked path: what a refresh over a prepared VERSION pays,
//  counted in chunks rather than rows.
//
//  The floor the file above records — one pass over the new source, because
//  the refresh is handed a whole table and has to read it to know what moved —
//  is what this family shows the chunked path getting under. A version made by
//  `ColumnOps.applyPrepared` shares every chunk the edit did not touch with the
//  version before it, by reference; `Incremental.refreshPrepared` compares the
//  chunks, evaluates the ones that moved, and hands back an output that shares
//  the rest. So a one-cell edit costs ONE chunk at 2,048 rows and one chunk at
//  20,000, and the count is the same number — which is the claim, stated as a
//  count for the reason the file above states its claim as one.
//
//  The falsifier is in the instrument: `chunksTouched` is `Some n` only on the
//  chunked path, a fresh `prepare` shares nothing and so touches every chunk,
//  and a state built by the row-local walk has no chunks to share and touches
//  every chunk once. Each of those is asserted below, so "one chunk" is a
//  measurement against shapes that do NOT read one.
// ---------------------------------------------------------------------------

/// The spreadsheet-shaped consumer's `lines` node (Phase 250): `amount = qty * price`, then `big = amount >= threshold`
/// with the threshold a `Param` — a `Derive`-only pipeline that keeps every row.
let private linesPipeline: Transform list =
    [ Derive("amount", Binary(Mul, Col "qty", Col "price"))
      Derive("big", Binary(Ge, Col "amount", Param "threshold")) ]

let private linesEnv: Map<string, Cell> = Map.ofList [ "threshold", Float 500.0 ]

let private orders (n: int) : Table =
    { Schema = [ "id", IntType; "region", StringType; "qty", IntType; "price", FloatType ]
      Columns =
        [ Column.create "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          Column.create "region" StringType [ for i in 0 .. n - 1 -> Str(if i % 2 = 0 then "north" else "south") ]
          Column.create "qty" IntType [ for i in 0 .. n - 1 -> Int(1 + i % 7) ]
          Column.create "price" FloatType [ for i in 0 .. n - 1 -> Float(0.25 * float (1 + i % 50)) ] ] }

let private orderId = RowIdentity.byColumn "id"

let private chunkRows = Chunked.rows

/// The refresh over a version, with the result held to the reference evaluator over the
/// version's table — asserted on every path below, so no count is read off a wrong answer.
let private refreshChecked (state: IncrementalEval) (delta: TableDelta) (version: Prepared) : IncrementalEval =
    let refreshed =
        ok (Incremental.refreshPrepared DataFrame.noResolve linesEnv orderId linesPipeline state delta version)

    Expect.equal
        (Ok(Incremental.result refreshed))
        (DataFrame.evalPipelineInEnv linesEnv linesPipeline (DataFrame.toTable version))
        "the chunked refresh answers what the reference answers"

    refreshed

let private rowsEvaluated (s: IncrementalEval) : int =
    Incremental.rowsEvaluated (Incremental.footprint s)

[<Tests>]
let chunkedRefreshTests =
    testList
        "Incremental.chunked"
        [ testCase "a one-cell edit refreshes ONE chunk, and the same one chunk at ten times the rows"
          <| fun _ ->
              let costAt (n: int) =
                  let v0 = DataFrame.prepare (orders n)

                  let s0 =
                      ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

                  Expect.equal
                      (Incremental.chunksTouched s0)
                      (Some(Chunked.count chunkRows n))
                      "the prime over a prepared Derive-only pipeline is the chunked path, every chunk evaluated"

                  Expect.equal
                      (rowsEvaluated s0)
                      (2 * n)
                      "the prime's footprint is the reference's: two Derives over n rows"

                  let op = SetCell("qty", n / 2, Int 100)
                  let v1 = ok (ColumnOps.applyPrepared op v0)
                  let delta = ColumnOps.deltaOfPrepared orderId v0 op
                  Expect.isTrue (Delta.isQuiet delta |> not) "the delta names the edited row"
                  let s1 = refreshChecked s0 delta v1

                  Expect.equal (Incremental.chunksTouched s1) (Some 1) "one chunk moved, one chunk evaluated"

                  Expect.equal
                      (Incremental.footprint s1).Recompute
                      (RowsRecomputed(2 * chunkRows))
                      "the rows evaluated are the chunk's rows, twice (two Derives)"

                  // The OUTPUT shares every chunk the edit did not touch, in every column, by
                  // reference — which is what makes the next node downstream cheap too. In the
                  // edited chunk, the columns that pass through untouched (`id`, `region`,
                  // `price`) are still the source's own chunk, so they are shared as well; only
                  // the edited column and the two derived from it hold a new chunk there.
                  let out0 = Prepared.columns (Incremental.resultPrepared s0)
                  let out1 = Prepared.columns (Incremental.resultPrepared s1)
                  let edited = (n / 2) / chunkRows
                  let moved = set [ 2; 4; 5 ] // qty, amount, big

                  for ci in 0 .. out1.Length - 1 do
                      for k in 0 .. out1[ci].Chunks.Length - 1 do
                          Expect.equal
                              (obj.ReferenceEquals(out0[ci].Chunks[k], out1[ci].Chunks[k]))
                              (k <> edited || not (Set.contains ci moved))
                              (sprintf "output column %d chunk %d is shared exactly when the edit did not move it" ci k)

                  rowsEvaluated s1

              let atSmall = costAt (2 * chunkRows)
              let atLarge = costAt (20_000)

              printfn
                  "  [cost] chunked one-cell refresh: %d rows evaluated at %d rows, %d at 20000"
                  atSmall
                  (2 * chunkRows)
                  atLarge

              Expect.equal atSmall atLarge "the cost of a one-cell edit does not grow with the table"

          testCase "the version itself shares every chunk the edit did not touch, and the prior version is intact"
          <| fun _ ->
              let n = 5_000
              let t = orders n
              let v0 = DataFrame.prepare t
              let op = SetCell("price", 3_000, Float 9.75)
              let v1 = ok (ColumnOps.applyPrepared op v0)
              let c0 = Prepared.columns v0
              let c1 = Prepared.columns v1

              for ci in 0 .. c0.Length - 1 do
                  if ci <> 3 then
                      Expect.isTrue (obj.ReferenceEquals(c0[ci], c1[ci])) "an untouched column is the same rope"
                  else
                      for k in 0 .. c0[ci].Chunks.Length - 1 do
                          Expect.equal
                              (obj.ReferenceEquals(c0[ci].Chunks[k], c1[ci].Chunks[k]))
                              (k <> 3_000 / chunkRows)
                              "the edited column shares every chunk but the one the edit landed in"

              Expect.isTrue
                  (obj.ReferenceEquals(DataFrame.toTable v0, t))
                  "the prior version still stands for the table it was prepared from"

              Expect.equal
                  (Ok(DataFrame.toTable v1))
                  (ColumnOps.apply op t)
                  "the new version stands for the table the Table form produces"

              Expect.equal
                  (ColumnOps.invertPrepared op v0)
                  (Ok(SetCell("price", 3_000, Float(0.25 * float (1 + 3_000 % 50)))))
                  "the inverse reads the prior version's cell"

          testCase "a version with the same chunks is recognised as unchanged: zero chunks, the prior reused"
          <| fun _ ->
              let v0 = DataFrame.prepare (orders 3_000)

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              let s1 = refreshChecked s0 (Delta.empty orderId.Scheme) v0
              Expect.equal (Incremental.chunksTouched s1) (Some 0) "nothing moved, nothing evaluated"
              Expect.equal (Incremental.footprint s1).Recompute ReusedPrior "and the footprint says so"

          testCase
              "what moved is read off the chunks, never off the delta: a FullRefresh over a one-chunk edit costs one chunk"
          <| fun _ ->
              let v0 = DataFrame.prepare (orders 3_000)

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              let v1 = ok (ColumnOps.applyPrepared (SetCell("qty", 10, Int 3)) v0)
              let s1 = refreshChecked s0 FullRefresh v1
              Expect.equal (Incremental.chunksTouched s1) (Some 1) "one chunk"

          testCase "a version prepared afresh shares nothing, so every chunk is evaluated — the falsifier"
          <| fun _ ->
              let n = 3_000
              let t = orders n
              let v0 = DataFrame.prepare t

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              let op = SetCell("qty", 10, Int 3)
              let fresh = DataFrame.prepare (ok (ColumnOps.apply op t))
              let s1 = refreshChecked s0 (ColumnOps.deltaOf orderId t op) fresh

              Expect.equal
                  (Incremental.chunksTouched s1)
                  (Some(Chunked.count chunkRows n))
                  "no shared chunk, every chunk evaluated"

          testCase
              "a state the row-local walk built has no chunks to share: the first chunked refresh evaluates every chunk, the next one"
          <| fun _ ->
              let n = 3_000
              let t = orders n
              let s0 = ok (Incremental.prime DataFrame.noResolve linesEnv orderId linesPipeline t)
              Expect.equal (Incremental.chunksTouched s0) None "a prime over a table is the walk"
              let v0 = DataFrame.prepare t
              let s1 = refreshChecked s0 (Delta.empty orderId.Scheme) v0
              Expect.equal (Incremental.chunksTouched s1) (Some(Chunked.count chunkRows n)) "every chunk, once"
              let op = SetCell("qty", 2_500, Int 3)
              let v1 = ok (ColumnOps.applyPrepared op v0)
              let s2 = refreshChecked s1 (ColumnOps.deltaOfPrepared orderId v0 op) v1
              Expect.equal (Incremental.chunksTouched s2) (Some 1) "then one"

              // And back: a refresh over a bare table after a chunked state walks every row once
              // (the chunks are not row caches), answers the reference, and carries no chunks.
              let t2 = ok (ColumnOps.apply (SetCell("qty", 7, Int 5)) (DataFrame.toTable v1))

              let delta =
                  ColumnOps.deltaOf orderId (DataFrame.toTable v1) (SetCell("qty", 7, Int 5))

              let s3 =
                  ok (Incremental.refresh DataFrame.noResolve linesEnv orderId linesPipeline s2 delta t2)

              Expect.equal
                  (Ok(Incremental.result s3))
                  (DataFrame.evalPipelineInEnv linesEnv linesPipeline t2)
                  "the walk answers the reference"

              Expect.equal (Incremental.chunksTouched s3) None "and carries no chunks"

          testCase "an append costs the last chunk and the chunks it adds"
          <| fun _ ->
              let n = 3_000 // 2 full chunks and a partial third
              let v0 = DataFrame.prepare (orders n)

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              let rows =
                  [ for i in 0..2 -> [ "id", Int(n + i); "region", Str "east"; "qty", Int 2; "price", Float 1.5 ] ]

              let op = AppendRows rows
              let v1 = ok (ColumnOps.applyPrepared op v0)
              let s1 = refreshChecked s0 (ColumnOps.deltaOfPrepared orderId v0 op) v1
              Expect.equal (Incremental.chunksTouched s1) (Some 1) "the partial last chunk, copied and extended"

              // Past the chunk boundary: the last chunk plus the new one.
              let many =
                  [ for i in 0..chunkRows ->
                        [ "id", Int(n + 3 + i); "region", Str "west"; "qty", Int 1; "price", Float 2.0 ] ]

              let v2 = ok (ColumnOps.applyPrepared (AppendRows many) v1)

              let s2 =
                  refreshChecked s1 (ColumnOps.deltaOfPrepared orderId v1 (AppendRows many)) v2

              Expect.equal (Incremental.chunksTouched s2) (Some 2) "the last chunk and the one the append opened"

          testCase "a SetColumn that moves two cells shares every chunk it did not move"
          <| fun _ ->
              let n = 5_000
              let t = orders n
              let v0 = DataFrame.prepare t

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              let cells =
                  [ for i in 0 .. n - 1 -> if i = 100 || i = 4_000 then Int 50 else Int(1 + i % 7) ]

              let op = SetColumn(Column.create "qty" IntType cells)
              let v1 = ok (ColumnOps.applyPrepared op v0)
              let s1 = refreshChecked s0 (ColumnOps.deltaOfPrepared orderId v0 op) v1
              Expect.equal (Incremental.chunksTouched s1) (Some 2) "two chunks moved"

          testCase "a derived column's type is the reference's over the whole rope, not a chunk's"
          <| fun _ ->
              // `m` is absent through the first chunk, so `c = a + m` has no present cell there:
              // evaluated alone, that chunk would type `c` as a string column. The reference types
              // the column from its first present cell, and so must the rope.
              let n = 3_000

              let t =
                  { Schema = [ "id", IntType; "a", IntType; "m", IntType ]
                    Columns =
                      [ Column.create "id" IntType [ for i in 0 .. n - 1 -> Int i ]
                        Column.create "a" IntType [ for i in 0 .. n - 1 -> Int i ]
                        Column.create "m" IntType [ for i in 0 .. n - 1 -> (if i < chunkRows then Null else Int 1) ] ] }

              let p =
                  [ Derive("c", Binary(Add, Col "a", Col "m"))
                    Derive("d", Binary(Ge, Col "c", Lit(Int 2_000))) ]

              let v0 = DataFrame.prepare t
              let s0 = ok (Incremental.primeOnPrepared orderId p v0)
              Expect.equal (Ok(Incremental.result s0)) (DataFrame.evalPipeline p t) "the prime, schema and cells"

              let op = SetCell("a", 5, Int 7)
              let v1 = ok (ColumnOps.applyPrepared op v0)

              let s1 =
                  ok (Incremental.refreshOnPrepared orderId p s0 (ColumnOps.deltaOfPrepared orderId v0 op) v1)

              Expect.equal
                  (Ok(Incremental.result s1))
                  (DataFrame.evalPipeline p (DataFrame.toTable v1))
                  "the refresh, schema and cells"

              Expect.equal (Incremental.chunksTouched s1) (Some 1) "one chunk"

              // All absent everywhere: a string column, as the reference types it.
              let allNull =
                  { t with
                      Columns =
                          t.Columns
                          |> List.map (fun c ->
                              if c.Name = "m" then
                                  { c with Cells = List.replicate n Null }
                              else
                                  c) }

              let sN = ok (Incremental.primeOnPrepared orderId p (DataFrame.prepare allNull))

              Expect.equal
                  (Ok(Incremental.result sN))
                  (DataFrame.evalPipeline p allNull)
                  "all-null: the reference's answer"

          testCase
              "a pipeline the chunked path does not admit takes the walk over the version's table, and answers the reference"
          <| fun _ ->
              let n = 3_000
              let t = orders n

              let p =
                  [ Derive("amount", Binary(Mul, Col "qty", Col "price"))
                    Filter(Binary(Ge, Col "amount", Lit(Float 2.0))) ]

              let v0 = DataFrame.prepare t
              let s0 = ok (Incremental.primeOnPrepared orderId p v0)
              Expect.equal (Incremental.chunksTouched s0) None "a Filter is not chunked"
              let op = SetCell("qty", 42, Int 6)
              let v1 = ok (ColumnOps.applyPrepared op v0)

              let s1 =
                  ok (Incremental.refreshOnPrepared orderId p s0 (ColumnOps.deltaOfPrepared orderId v0 op) v1)

              Expect.equal
                  (Ok(Incremental.result s1))
                  (DataFrame.evalPipeline p (DataFrame.toTable v1))
                  "the walk over the version's table"

              match (Incremental.footprint s1).Recompute with
              | RowsRecomputed n ->
                  Expect.isTrue (n <= 2) "restricted to the one named row, at its two evaluating steps"
              | other -> failtestf "expected a restricted refresh, got %A" other

          testCase "an empty source and an empty pipeline are both chunked without a chunk"
          <| fun _ ->
              let empty = orders 0
              let v0 = DataFrame.prepare empty

              let s0 =
                  ok (Incremental.primePrepared DataFrame.noResolve linesEnv orderId linesPipeline v0)

              Expect.equal
                  (Ok(Incremental.result s0))
                  (DataFrame.evalPipelineInEnv linesEnv linesPipeline empty)
                  "no rows"

              Expect.equal (Incremental.chunksTouched s0) (Some 0) "no chunk to evaluate"
              let t = orders 10
              let s1 = ok (Incremental.primeOnPrepared orderId [] (DataFrame.prepare t))
              Expect.equal (Incremental.result s1) t "the identity pipeline hands the table back"
              Expect.equal (Incremental.chunksTouched s1) (Some 1) "one chunk, shared through" ]

// ---------------------------------------------------------------------------
//  Phase 272 — the dense `Delta.diff` held equal to the row-token diff it
//  replaced.
//
//  The diff is the other half of what a table-fed caller pays per tick (it runs
//  `Delta.diff` against the prior source, then `Incremental.refreshOn`), and until
//  this phase it cost up to thirty-six times the evaluation it fed: a persistent
//  `Set` and two `Map`s over the key strings, and a length-prefixed token STRING
//  minted for every row of both tables to decide "changed". The dense form keys
//  each table once into an array, pairs a row that sits where it sat by one
//  string comparison, and compares cells under `CellKey.equals`. It is a new
//  implementation of an old answer, so the old implementation is kept below as
//  its MODEL and the two are held equal — table for table, refusal for refusal —
//  over drawn pairs that reach every path the dense form added.
// ---------------------------------------------------------------------------

/// The pre-272 `Delta.diff`, in substance: the model the dense form must agree with.
let private rowTokenDiff (idw: RowIdentity<'Id>) (before: Table) (after: Table) : Result<TableDelta, DeltaDefect> =
    if before.Schema <> after.Schema then
        Ok FullRefresh
    else
        let keyIndex (t: Table) =
            let n = Table.rowCount t
            let keyAt = idw.KeyOf t

            let rec go i acc (seen: Set<string>) =
                if i >= n then
                    Ok(List.rev acc)
                else
                    match keyAt i with
                    | None -> Error(MissingIdentity(idw.Scheme, i))
                    | Some id ->
                        let k = idw.KeyString id

                        if Set.contains k seen then
                            Error(DuplicateIdentity(idw.Scheme, k))
                        else
                            go (i + 1) ((k, i) :: acc) (Set.add k seen)

            go 0 [] Set.empty

        // Phase 323 (operator ruling 2026-10-01): "changed" is decided by `Delta.sameContent`, which
        // tells -0.0 from 0.0 and a re-spelt decimal from the original, where the pre-272 model
        // compared row tokens. The model follows the rule; what it models is still the dense form's
        // pairing, ordering and refusals.
        let tokens (t: Table) = RowAccess.rows t |> List.toArray

        keyIndex before
        |> Result.bind (fun bk ->
            keyIndex after
            |> Result.map (fun ak ->
                let bm = Map.ofList bk
                let am = Map.ofList ak
                let bt = tokens before
                let at = tokens after

                let fromAfter =
                    ak
                    |> List.choose (fun (k, ai) ->
                        match Map.tryFind k bm with
                        | None -> Some(ByKey k, RowAdded)
                        | Some bi ->
                            if Array.forall2 Delta.sameContent bt[bi] at[ai] then
                                None
                            else
                                Some(ByKey k, RowChanged))

                let removed =
                    bk
                    |> List.filter (fun (k, _) -> not (Map.containsKey k am))
                    |> List.map (fun (k, _) -> ByKey k, RowRemoved)

                Delta.normalise (
                    RowSet
                        { Scheme = idw.Scheme
                          Rows = fromAfter @ removed
                          InvalidatedColumns = [] }
                )))

let private diffSchema: Schema = [ "id", StringType; "v", IntType; "w", FloatType ]

/// The cells a drawn row takes: a small key pool so inserts collide with live keys, and the floats
/// token equality treats specially (`-0.0` equal to `0.0`, every `NaN` one value) beside `Null` —
/// since Phase 323 the diff tells the two zeros apart, and the pool is what shows it.
let private diffKeys = [| for i in 0..11 -> Str("k" + string i) |]
let private diffInts = [| Int 1; Int 2; Int -3; Null |]
let private diffFloats = [| Float 0.0; Float -0.0; Float nan; Float 1.5; Null |]

let private tableOfRows (rows: Cell[] list) : Table =
    { Schema = diffSchema
      Columns =
        diffSchema
        |> List.mapi (fun ci (name, ty) -> Column.create name ty (rows |> List.map (fun r -> r[ci]))) }

/// What a drawn pair exercised, so the family can demand it reached every path rather than assume it.
type private DiffCase =
    { Answered: bool
      Moved: bool
      InPlaceChange: bool
      SharedColumn: bool
      Ragged: bool }

/// Replace a row's key cell.
let private withKey (k: Cell) (r: Cell[]) : Cell[] = Array.append [| k |] r[1..]

/// One drawn (before, after) pair and what it reaches.
let private drawDiffPair (rng: System.Random) : Table * Table * DiffCase =
    let n = rng.Next(0, 9)
    let order = [| 0..11 |] |> Array.sortBy (fun _ -> rng.Next())

    let row (k: Cell) =
        [| k
           diffInts[rng.Next diffInts.Length]
           diffFloats[rng.Next diffFloats.Length] |]

    let mutable before = [ for i in 0 .. n - 1 -> row diffKeys[order[i]] ]

    // An occasional defect in the BEFORE table: a repeated key, or a row with no key.
    if n > 1 && rng.Next 12 = 0 then
        let first = before.Head[0]
        before <- before |> List.mapi (fun i r -> if i = n - 1 then withKey first r else r)

    if n > 0 && rng.Next 14 = 0 then
        let at = rng.Next n
        before <- before |> List.mapi (fun i r -> if i = at then withKey Null r else r)

    let mutable after = before |> List.map Array.copy
    let mutable moved = false

    for _ in 1 .. rng.Next(0, 4) do
        let len = List.length after

        match rng.Next 6 with
        | 0 when len > 0 ->
            // edit one non-key cell in place
            let at = rng.Next len
            let ci = 1 + rng.Next 2

            let cell =
                if ci = 1 then
                    diffInts[rng.Next diffInts.Length]
                else
                    diffFloats[rng.Next diffFloats.Length]

            after <-
                after
                |> List.mapi (fun i r ->
                    if i <> at then
                        r
                    else
                        let r' = Array.copy r
                        r'[ci] <- cell
                        r')
        | 1 when len > 0 ->
            // delete a row: every row after it moves up one
            let at = rng.Next len
            after <- after |> List.indexed |> List.filter (fun (i, _) -> i <> at) |> List.map snd
            moved <- true
        | 2 ->
            // insert a row with a key from the pool, which may already be live (a duplicate)
            let at = rng.Next(len + 1)
            let r = row diffKeys[rng.Next diffKeys.Length]
            after <- List.take at after @ [ r ] @ List.skip at after
            moved <- true
        | 3 when len > 1 ->
            // swap two rows
            let a = rng.Next len
            let b = rng.Next len
            let arr = List.toArray after
            let t = arr[a]
            arr[a] <- arr[b]
            arr[b] <- t
            after <- List.ofArray arr
            moved <- moved || a <> b
        | 4 when len > 0 && rng.Next 4 = 0 ->
            // take a row's key away in the AFTER table
            let at = rng.Next len
            after <- after |> List.mapi (fun i r -> if i = at then withKey Null r else r)
        | _ -> ()

    let bt = tableOfRows before
    let at0 = tableOfRows after

    // Share every column whose cells did not move as the SAME list, as an edit through `ColumnOps`
    // does — the path on which the dense diff does not read that column for the rows in place.
    let mutable shared = false

    let at1 =
        { at0 with
            Columns =
                List.map2
                    (fun (b: Column) (a: Column) ->
                        if b.Cells = a.Cells && rng.Next 2 = 0 then
                            shared <- true
                            b
                        else
                            a)
                    bt.Columns
                    at0.Columns }

    // An occasional RAGGED column (shorter than the table), which both forms read as `Null` past its
    // end.
    let ragged = rng.Next 10 = 0 && Table.rowCount at1 > 0

    let at =
        if ragged then
            { at1 with
                Columns =
                    at1.Columns
                    |> List.map (fun c ->
                        if c.Name = "w" then
                            { c with
                                Cells = List.truncate (List.length c.Cells - 1) c.Cells }
                        else
                            c) }
        else
            at1

    let inPlaceChange =
        List.length before = List.length after
        && List.exists2 (fun (b: Cell[]) (a: Cell[]) -> b[0] = a[0] && b <> a) before after

    bt,
    at,
    { Answered = false
      Moved = moved
      InPlaceChange = inPlaceChange
      SharedColumn = shared
      Ragged = ragged }

/// Phase 284 — a consumer's OWN composite witness over the drawn tables, declared through the public
/// `RowIdentity.withKeyEquality`: identity is the `(id, v)` pair as typed values (a string and an
/// int, not cells), the key string is length-prefixed and so injective, and structural tuple
/// equality agrees with it. A row whose `id` or `v` is not a present value has no identity. `v` is
/// what an in-place edit touches, so an edit MOVES the key and the pairing has to fall back to a
/// lookup, as `byColumns [ "id"; "v" ]` does. `render` renders the key; the three declarations below
/// vary only the equality.
let private pairKeyOf (t: Table) : int -> (string * int) option =
    let cellsOf (name: string) =
        match Table.tryColumn name t with
        | Some c -> List.toArray c.Cells
        | None -> [||]

    let ids = cellsOf "id"
    let vs = cellsOf "v"

    fun i ->
        if i >= 0 && i < ids.Length && i < vs.Length then
            match ids[i], vs[i] with
            | Str s, Int v -> Some(s, v)
            | _ -> None
        else
            None

let private customPair (scheme: string) (render: string * int -> string) : RowIdentity<string * int> =
    { Scheme = scheme
      KeyOf = pairKeyOf
      KeyString = render }

let private injectivePairKey (s: string, v: int) =
    string s.Length + ":" + s + "|" + string v

/// The correct declaration: structural equality over an injective key string.
let private customPairKey =
    customPair "custom:id+v" injectivePairKey
    |> RowIdentity.withKeyEquality HashIdentity.Structural

/// Phase 284 — three deliberately WRONG declarations, one per way an equality can disagree with its
/// key strings: FINER than the strings (the key renders only the id, the equality compares the pair —
/// a missed `DuplicateIdentity`), COARSER than the strings (the equality compares only the id — two
/// identities paired as one), and an equality whose hash is not its own (equal ids hashed apart). Each
/// keys under its OWN scheme: witnesses sharing a scheme must key every table identically (Phase 273),
/// and these render differently.
let private finerPairKey =
    customPair "custom:id+v/finer" (fun (s, _) -> s)
    |> RowIdentity.withKeyEquality HashIdentity.Structural

let private coarserPairKey =
    customPair "custom:id+v/coarser" injectivePairKey
    |> RowIdentity.withKeyEquality (
        HashIdentity.FromFunctions (fun (s: string, _: int) -> hash s) (fun (a, _) (b, _) -> a = b)
    )

let private unhashedPairKey =
    let next = ref 0

    customPair "custom:id+v/unhashed" injectivePairKey
    |> RowIdentity.withKeyEquality (
        HashIdentity.FromFunctions
            (fun (_: string * int) ->
                next.Value <- next.Value + 1
                next.Value)
            (fun a b -> a = b)
    )

[<Tests>]
let denseDiffTests =
    testList
        "Delta.diff (dense)"
        [ testCase "the dense diff answers exactly what the row-token diff answered, refusals included"
          <| fun _ ->
              let rng = System.Random 272
              let byId = RowIdentity.byColumn "id"
              // A composite key whose value MOVES when an in-place edit touches `v`, so the in-place
              // pairing has to fall back to a lookup.
              let byPair = RowIdentity.byColumns [ "id"; "v" ]

              // Phase 283 — both reference witnesses DECLARE a key equality, so `Delta.diff` pairs
              // their rows by the typed id and renders a key string only for the rows the delta
              // carries. A copy with its own `KeyString` declares nothing and takes the string path,
              // so each pair below is diffed both ways and held to the reference and to each other.
              Expect.isSome (KeyEqualities.tryOf byId) "byColumn declares its key equality"
              Expect.isSome (KeyEqualities.tryOf byPair) "byColumns declares its key equality"

              let byIdStr =
                  { byId with
                      KeyString = fun c -> byId.KeyString c }

              let byPairStr =
                  { byPair with
                      KeyString = fun c -> byPair.KeyString c }

              Expect.isNone (KeyEqualities.tryOf byIdStr) "a copy with its own KeyString takes the string path"
              Expect.isNone (KeyEqualities.tryOf byPairStr) "a copy with its own KeyString takes the string path"

              // Phase 284 — a consumer's own composite witness, declared through the public route, is
              // held to the same reference and to its own undeclared twin.
              Expect.isSome (KeyEqualities.tryOf customPairKey) "withKeyEquality declares the witness it returns"

              let customPairStr =
                  { customPairKey with
                      KeyString = fun id -> customPairKey.KeyString id }

              Expect.isNone (KeyEqualities.tryOf customPairStr) "a copy of a declared witness takes the string path"
              let mutable customAnswered = 0
              let mutable customRefused = 0

              // Typed and string answers: equal, and the same bytes on the wire (a refusal is compared
              // whole, payload included).
              let sameBothWays (label: string) (iter: int) (typed: Result<TableDelta, DeltaDefect>) stringPath =
                  if typed <> stringPath then
                      failtestf "iter %d (%s): typed %A\n  string %A" iter label typed stringPath

                  match typed, stringPath with
                  | Ok a, Ok b when DeltaCodec.encode a <> DeltaCodec.encode b ->
                      failtestf "iter %d (%s): the typed delta's wire differs from the string path's" iter label
                  | _ -> ()

              let mutable reached = []

              for iter in 1..4000 do
                  let before, after, case = drawDiffPair rng

                  let actual = Delta.diff byId before after
                  let expected = rowTokenDiff byId before after

                  if actual <> expected then
                      failtestf
                          "iter %d (byColumn): dense %A\n  row-token %A\n  before %A\n  after %A"
                          iter
                          actual
                          expected
                          before
                          after

                  // Phase 273 — the same pair diffed again, now that the diff has remembered each
                  // table's keys: once with BOTH sides known (the same two objects), once with only
                  // `before` known (a fresh `after` record over the same columns). Reading keys
                  // back must answer exactly what minting them answered.
                  let freshAfter = { after with Columns = after.Columns }

                  for label, warm in
                      [ "both sides known", Delta.diff byId before after
                        "before known", Delta.diff byId before freshAfter ] do
                      if warm <> expected then
                          failtestf
                              "iter %d (%s): warm %A
  row-token %A
  before %A
  after %A"
                              iter
                              label
                              warm
                              expected
                              before
                              after

                  let actual2 = Delta.diff byPair before after
                  let expected2 = rowTokenDiff byPair before after

                  if actual2 <> expected2 then
                      failtestf
                          "iter %d (byColumns): dense %A\n  row-token %A\n  before %A\n  after %A"
                          iter
                          actual2
                          expected2
                          before
                          after

                  // Phase 283 — the string path over FRESH table objects (so nothing the typed diffs
                  // above remembered is read back), cold and then warm, against the typed answers.
                  let sb = { before with Columns = before.Columns }
                  let sa = { after with Columns = after.Columns }
                  sameBothWays "byColumn, cold" iter actual (Delta.diff byIdStr sb sa)
                  sameBothWays "byColumns, cold" iter actual2 (Delta.diff byPairStr sb sa)

                  // Phase 284 — the custom witness: typed against the row-token reference, then against
                  // its string twin over the same fresh tables (cold under this scheme), wire included.
                  let actual3 = Delta.diff customPairKey before after
                  let expected3 = rowTokenDiff customPairKey before after

                  if actual3 <> expected3 then
                      failtestf
                          "iter %d (custom): typed %A\n  row-token %A\n  before %A\n  after %A"
                          iter
                          actual3
                          expected3
                          before
                          after

                  sameBothWays "custom, cold" iter actual3 (Delta.diff customPairStr sb sa)

                  // The declaration is correct, so the checking mode finds nothing on either table.
                  for t in [ before; after ] do
                      match RowIdentity.checkKeyEquality customPairKey t with
                      | Ok() -> ()
                      | Error d -> failtestf "iter %d: a correct declaration was flagged: %A" iter d

                  if Result.isOk actual3 then
                      customAnswered <- customAnswered + 1
                  else
                      customRefused <- customRefused + 1

                  let sa2 = { after with Columns = after.Columns }
                  sameBothWays "byColumn, before known" iter actual (Delta.diff byIdStr sb sa2)

                  // And the typed path with its before side remembered by a string-path caller (the
                  // seam keyed it by string, say): the ids are asked of the witness, once.
                  let tb = { before with Columns = before.Columns }
                  Delta.diff byIdStr tb { after with Columns = after.Columns } |> ignore

                  sameBothWays
                      "byColumn, before keyed by string"
                      iter
                      (Delta.diff byId tb { after with Columns = after.Columns })
                      actual

                  reached <-
                      { case with
                          Answered = Result.isOk actual }
                      :: reached

              // The vacuity guard: the equality above is only worth what the draws reached.
              let count p = reached |> List.filter p |> List.length

              let demands =
                  [ "an answered pair", (fun c -> c.Answered)
                    "a refused pair", (fun c -> not c.Answered)
                    "an answered pair where a row moved", (fun c -> c.Answered && c.Moved)
                    "an answered in-place change", (fun c -> c.Answered && c.InPlaceChange)
                    "an answered pair sharing a column list", (fun c -> c.Answered && c.SharedColumn)
                    "an answered pair with a ragged column", (fun c -> c.Answered && c.Ragged) ]

              for label, p in demands do
                  printfn "  [dense diff] %-40s %5d" label (count p)
                  Expect.isGreaterThan (count p) 20 (sprintf "the draws must reach %s" label)

              printfn "  [dense diff] %-40s %5d" "custom witness: answered" customAnswered
              printfn "  [dense diff] %-40s %5d" "custom witness: refused" customRefused
              Expect.isGreaterThan customAnswered 20 "the custom witness must reach an answered pair"
              Expect.isGreaterThan customRefused 20 "the custom witness must reach a refused pair"

          testCase "a wrong key-equality declaration is caught by checkKeyEquality, and is what the diff trusts"
          <| fun _ ->
              // Phase 284 — the declared equality is used AS GIVEN: the library cannot prove it agrees
              // with `KeyString`, and the diff does not check. These three are wrong on purpose, one per
              // way an equality can disagree, and `checkKeyEquality` names each.
              let t (rows: (string * int) list) =
                  tableOfRows [ for s, v in rows -> [| Str s; Int v; Float 0.0 |] ]

              let clash = t [ "k0", 1; "k1", 1; "k0", 2 ]
              let twice = t [ "k0", 1; "k1", 1; "k0", 1 ]

              Expect.equal (RowIdentity.checkKeyEquality customPairKey clash) (Ok()) "the correct one agrees"

              Expect.equal
                  (RowIdentity.checkKeyEquality finerPairKey clash)
                  (Error(DistinctIdsEqualKey(0, 2, "k0")))
                  "finer than the strings: rows 0 and 2 share a key the equality calls distinct"

              Expect.equal
                  (RowIdentity.checkKeyEquality coarserPairKey clash)
                  (Error(EqualIdsDistinctKeys(0, 2, "2:k0|1", "2:k0|2")))
                  "coarser than the strings: rows 0 and 2 are equal ids with distinct keys"

              Expect.equal
                  (RowIdentity.checkKeyEquality unhashedPairKey twice)
                  (Error(UnequalHashes(0, 2, "2:k0|1")))
                  "an equality whose hash is not its own"

              // Seen by the pairwise sample alone: equal ids, DISTINCT keys, hashed apart — so no
              // lookup finds the pair, and only comparing them does.
              let coarseUnhashed =
                  customPair "custom:id+v/coarse-unhashed" injectivePairKey
                  |> RowIdentity.withKeyEquality (
                      HashIdentity.FromFunctions (fun (s: string, v: int) -> hash (s, v)) (fun (a, _) (b, _) -> a = b)
                  )

              Expect.equal
                  (RowIdentity.checkKeyEquality coarseUnhashed clash)
                  (Error(EqualIdsDistinctKeys(0, 2, "2:k0|1", "2:k0|2")))
                  "the pairwise sample finds equal ids a hash lookup would miss"

              Expect.equal
                  (RowIdentity.checkKeyEquality (customPair "custom:id+v" injectivePairKey) clash)
                  (Error(NotDeclared "custom:id+v"))
                  "an undeclared witness has nothing to check"

              // What a wrong declaration costs, and why the check exists: the string path refuses the
              // repeated key, and the finer declaration answers a delta over keys that are not
              // identities; the coarser one pairs two identities as one row.
              let before = t [ "k0", 1; "k1", 1 ]

              let finerStr =
                  { finerPairKey with
                      KeyString = fun id -> finerPairKey.KeyString id }

              Expect.equal
                  (Delta.diff finerStr before clash)
                  (Error(DuplicateIdentity("custom:id+v/finer", "k0")))
                  "the string path refuses the repeated key"

              Expect.isOk (Delta.diff finerPairKey before clash) "the wrong declaration misses the refusal"

              let coarserStr =
                  { coarserPairKey with
                      KeyString = fun id -> coarserPairKey.KeyString id }

              // Fresh tables for the string path: a diff remembers the keys it rendered for the tables
              // it keyed, so a wrong declaration's answer would be read back by anything diffing those
              // very objects under that scheme afterwards — the damage is not confined to one call.
              let edited = t [ "k0", 2; "k1", 1 ]
              let fresh (x: Table) = { x with Columns = x.Columns }

              Expect.equal
                  (Delta.diff coarserStr (fresh before) (fresh edited))
                  (Ok(
                      RowSet
                          { Scheme = "custom:id+v/coarser"
                            Rows = [ ByKey "2:k0|2", RowAdded; ByKey "2:k0|1", RowRemoved ]
                            InvalidatedColumns = [] }
                      |> Delta.normalise
                  ))
                  "the string path: one identity removed, another added"

              Expect.equal
                  (Delta.diff coarserPairKey before edited)
                  (Ok(
                      RowSet
                          { Scheme = "custom:id+v/coarser"
                            Rows = [ ByKey "2:k0|1", RowChanged ]
                            InvalidatedColumns = [] }
                      |> Delta.normalise
                  ))
                  "the coarser declaration pairs the two identities as one changed row"

          testCase "a refusal in BEFORE is reported ahead of one in AFTER, and each names the first offending row"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"

              let t keys =
                  tableOfRows [ for k in keys -> [| k; Int 1; Float 0.0 |] ]

              let a = Str "a"
              let b = Str "b"

              for before, after in
                  [ t [ a; a ], t [ Null ]
                    t [ a; b ], t [ b; b ]
                    t [ a; b ], t [ a; Null; a ]
                    t [ a; b ], t [ b; a; a ]
                    t [ a; b ], t [ a; b; a ]
                    t [ a; b ], t [ a; a ] ] do
                  let got = Delta.diff idw before after
                  Expect.isError got (sprintf "%A -> %A refuses" before after)
                  Expect.equal got (rowTokenDiff idw before after) (sprintf "%A -> %A" before after) ]

// ---------------------------------------------------------------------------
//  Phase 273 — a tick mints each row's key once.
//
//  A table-fed caller's tick is `Delta.diff prior next` and then the refresh over
//  `next`. Until this phase that tick asked the witness for a key THREE times per
//  row: the diff keyed both tables, and the refresh keyed `next` again — although
//  the state had keyed `prior` at the previous tick. Now `Delta.diff` remembers
//  the keys it mints (and the seam the keys it mints), reads a table's keys back
//  when it meets that very object again, and hands the keys of `next` to the
//  refresh inside the delta it returns.
//
//  COUNTED, not timed: the witness below counts its `KeyString` calls, so the
//  claim is exact and holds on a loaded machine. On the pre-phase tree the
//  one-tick count is 3n (the diff 2n, the refresh n), and this family is red.
// ---------------------------------------------------------------------------

/// `byColumn "id"` under its own scheme, counting every key it mints.
let private countingId (minted: int ref) : RowIdentity<Cell> =
    let b = RowIdentity.byColumn "id"

    { b with
        KeyString =
            fun c ->
                minted.Value <- minted.Value + 1
                b.KeyString c }

/// Phase 283 — `byColumn "id"` counting every key it RENDERS, with the reference witness's declared
/// key equality declared for it too (the counting wrapper renders exactly what `cellToken` renders, so
/// the declaration's obligation holds). The diff pairs its rows by the typed id. Declared through the
/// public `RowIdentity.withKeyEquality` since Phase 284.
let private countingTypedId (rendered: int ref) : RowIdentity<Cell> =
    let b = RowIdentity.byColumn "id"

    { b with
        KeyString =
            fun c ->
                rendered.Value <- rendered.Value + 1
                b.KeyString c }
    |> RowIdentity.withKeyEquality KeyEqualities.cell

/// Delete row `del` and insert a row with a new identity at `ins`, every column rebuilt: a tick in
/// which rows MOVE, so the diff pairs by lookup as well as in place, and the delta carries an
/// added and a removed row.
let private reshape (tag: string) (del: int) (ins: int) (t: Table) : Table =
    let n = Table.rowCount t

    let cellsOf (name: string) =
        (Table.tryColumn name t |> Option.get).Cells |> List.toArray

    let ids = cellsOf "id"
    let grps = cellsOf "grp"
    let aa = cellsOf "a"

    let kept =
        [ for i in 0 .. n - 1 do
              if i <> del then
                  yield ids[i], grps[i], aa[i] ]

    let rows =
        List.take ins kept @ [ Str("new-" + tag), Str "g0", Int 5 ] @ List.skip ins kept

    { t with
        Columns =
            [ Column.create "id" StringType [ for (k, _, _) in rows -> k ]
              Column.create "grp" StringType [ for (_, g, _) in rows -> g ]
              Column.create "a" IntType [ for (_, _, a) in rows -> a ] ] }

/// Pipelines covering the seam's incremental strategies: row-local, maintained groups, and a sort.
let private tickPipelines: (string * Transform list) list =
    [ "row-local derive", [ Derive("b", Binary(Mul, Col "a", Lit(Int 2))) ]
      "filter > groupBy", pipeline
      "filter > sort > limit",
      [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
        Transform.sortBy [ "a", Desc ]
        Transform.limit 10 0 ] ]

/// Phase 284 — a consumer's OWN composite witness over the tick tables: identity is the `(id, grp)`
/// pair of strings (not cells), the key string is length-prefixed and so injective, and structural
/// tuple equality agrees with it — declared through the public `RowIdentity.withKeyEquality`. Counts
/// every key it renders.
let private customTickKey (rendered: int ref) : RowIdentity<string * string> =
    { Scheme = "custom:id+grp"
      KeyOf =
        fun t ->
            let cellsOf (name: string) =
                match Table.tryColumn name t with
                | Some c -> List.toArray c.Cells
                | None -> [||]

            let ids = cellsOf "id"
            let grps = cellsOf "grp"

            fun i ->
                if i >= 0 && i < ids.Length && i < grps.Length then
                    match ids[i], grps[i] with
                    | Str a, Str b -> Some(a, b)
                    | _ -> None
                else
                    None
      KeyString =
        fun (a, b) ->
            rendered.Value <- rendered.Value + 1
            string a.Length + ":" + a + "|" + b }
    |> RowIdentity.withKeyEquality HashIdentity.Structural

/// The Phase 283 counted tick, for one declared witness (`typedOf`) and its undeclared string-path
/// twin (`stringOf`, same scheme and key strings): every tick renders exactly the added rows' keys,
/// and the typed delta, its wire and the refreshed result are the string path's.
let private typedTickRenders
    (wlabel: string)
    (typedOf: int ref -> RowIdentity<'Id>)
    (stringOf: unit -> RowIdentity<'Id>)
    : unit =
    for plabel, p in tickPipelines do
        let label = wlabel + " / " + plabel
        let rendered = ref 0
        let typedId = typedOf rendered
        let stringId = stringOf ()
        Expect.isSome (KeyEqualities.tryOf typedId) (sprintf "%s: the witness declares its key equality" label)
        Expect.isNone (KeyEqualities.tryOf stringId) (sprintf "%s: the string twin declares nothing" label)
        let n = small
        let t0 = build n
        let mutable state = ok (Incremental.primeOn typedId p t0)
        Expect.equal rendered.Value n (sprintf "%s: the prime keys the source once" label)
        let mutable prior = t0

        let ticks =
            [ "in place", (fun (t: Table) -> editSome 3 t)
              "in place again", (fun (t: Table) -> editSome 7 t)
              "rows moved", (fun (t: Table) -> reshape "x" 10 500 t)
              "rows moved again", (fun (t: Table) -> reshape "y" 3 0 (editSome 5 t)) ]

        for how, step in ticks do
            let next = step prior
            rendered.Value <- 0
            let delta = ok (Delta.diff typedId prior next)
            state <- ok (Incremental.refreshOn typedId p state delta next)

            let carried =
                Delta.tryRowSet delta
                |> Option.map (fun r -> List.length r.Rows)
                |> Option.defaultValue n

            let added = Delta.rowsWith RowAdded delta |> List.length

            printfn
                "  [typed key] %-40s %-18s rendered %d for %d carried rows (%d added) of %d"
                label
                how
                rendered.Value
                carried
                added
                n

            Expect.isLessThanOrEqual
                rendered.Value
                carried
                (sprintf "%s / %s: at most one key rendered per row the delta carries" label how)

            Expect.equal rendered.Value added (sprintf "%s / %s: exactly the added rows' keys are rendered" label how)

            // The typed path answers what the string path answers, delta and result alike.
            let fresh (t: Table) = { t with Columns = t.Columns }
            let stringDelta = ok (Delta.diff stringId (fresh prior) (fresh next))
            Expect.equal delta stringDelta (sprintf "%s / %s: the string path's delta" label how)

            Expect.equal
                (DeltaCodec.encode delta)
                (DeltaCodec.encode stringDelta)
                (sprintf "%s / %s: the string path's wire" label how)

            Expect.equal
                (Ok(Incremental.result state))
                (DataFrame.evalPipeline p next)
                (sprintf "%s / %s: refresh = reference" label how)

            prior <- next

[<Tests>]
let keyOnceTests =
    testList
        "a tick mints each row's key once"
        [ testCase "the diff-then-refresh tick mints each row's key exactly once, tick after tick"
          <| fun _ ->
              for label, p in tickPipelines do
                  let minted = ref 0
                  let idw = countingId minted
                  let n = small
                  let t0 = build n
                  let mutable state = ok (Incremental.primeOn idw p t0)
                  Expect.equal minted.Value n (sprintf "%s: the prime keys the source once" label)
                  let mutable prior = t0

                  // Three ticks, each a caller's: a fresh table, diffed against the prior source,
                  // then the refresh over it.
                  for tick in 1..3 do
                      let next = editSome (tick * 3) prior
                      minted.Value <- 0
                      let delta = ok (Delta.diff idw prior next)
                      let afterDiff = minted.Value
                      state <- ok (Incremental.refreshOn idw p state delta next)

                      Expect.equal
                          (Ok(Incremental.result state))
                          (DataFrame.evalPipeline p next)
                          (sprintf "%s tick %d: refresh = reference" label tick)

                      Expect.equal
                          afterDiff
                          n
                          (sprintf
                              "%s tick %d: the diff mints the NEW table's keys only — the prior's were minted when it was last seen"
                              label
                              tick)

                      Expect.equal
                          minted.Value
                          n
                          (sprintf
                              "%s tick %d: one key per row per tick (the pre-phase tick minted %d)"
                              label
                              tick
                              (3 * n))

                      prior <- next

          testCase "a delta the diff did not build carries no keys, and the refresh mints as it did before"
          <| fun _ ->
              for label, p in tickPipelines do
                  let minted = ref 0
                  let idw = countingId minted
                  let n = small
                  let t0 = build n
                  let state = ok (Incremental.primeOn idw p t0)
                  let t1 = editSome 5 t0
                  let delta = ok (Delta.diff idw t0 t1)
                  let reference = ok (Incremental.refreshOn idw p state delta t1)

                  let others =
                      [ "normalised", Delta.normalise delta
                        "composed with the quiet delta", Delta.compose (Delta.empty idw.Scheme) delta
                        "decoded from its wire", ok (DeltaCodec.decode (DeltaCodec.encode delta))
                        "hand-built from its rows",
                        Delta.ofRows idw.Scheme (Delta.tryRowSet delta |> Option.map _.Rows |> Option.defaultValue []) ]

                  for how, other in others do
                      // The keys ride beside the delta, never in it: equality and the wire are the
                      // diff's own, byte for byte.
                      Expect.equal other delta (sprintf "%s / %s: equal to the diff's delta" label how)

                      Expect.equal
                          (DeltaCodec.encode other)
                          (DeltaCodec.encode delta)
                          (sprintf "%s / %s: the same wire" label how)

                      minted.Value <- 0
                      let s = ok (Incremental.refreshOn idw p state other t1)

                      Expect.equal
                          minted.Value
                          n
                          (sprintf "%s / %s: no carried keys, so the refresh keys the source itself" label how)

                      Expect.equal
                          (Incremental.result s)
                          (Incremental.result reference)
                          (sprintf "%s / %s: the same answer" label how)

                      Expect.equal
                          (Incremental.footprint s)
                          (Incremental.footprint reference)
                          (sprintf "%s / %s: the same footprint" label how)

                  // The diff's own delta, but handed a DIFFERENT table object of the same content:
                  // its keys describe the table it was diffed into, so they are not reused here.
                  minted.Value <- 0
                  let copy = { t1 with Columns = t1.Columns }
                  let s = ok (Incremental.refreshOn idw p state delta copy)
                  Expect.equal minted.Value n (sprintf "%s: keys carried for another table object are not reused" label)

                  Expect.equal
                      (Incremental.result s)
                      (Incremental.result reference)
                      (sprintf "%s: the same answer" label)

          testCase "an ordinal diff carries no keys, and a scheme the table was not keyed under reads none back"
          <| fun _ ->
              let minted = ref 0
              let idw = countingId minted
              let t0 = build small
              let t1 = editSome 5 t0
              let state = ok (Incremental.primeOn idw pipeline t0)

              // Ordinal: the refresh declines ordinal addressing and re-keys the source in full.
              minted.Value <- 0
              let s = ok (Incremental.refreshOn idw pipeline state (Delta.diffByOrdinal t0 t1) t1)
              Expect.equal minted.Value small "an ordinal delta carries no keys"
              Expect.equal (Ok(Incremental.result s)) (DataFrame.evalPipeline pipeline t1) "refresh = reference"

              // A second scheme over the same tables: nothing remembered under `column:id` is read
              // back for work keyed under `columns:id,grp`, although the table objects are the same.
              let otherMinted = ref 0

              let byPair =
                  let b = RowIdentity.byColumns [ "id"; "grp" ]

                  { b with
                      KeyString =
                          fun c ->
                              otherMinted.Value <- otherMinted.Value + 1
                              b.KeyString c }

              let d = ok (Delta.diff byPair t0 t1)
              Expect.equal otherMinted.Value (2 * small) "another scheme mints both tables afresh"

              Expect.equal
                  (Ok d)
                  (Delta.diff (RowIdentity.byColumns [ "id"; "grp" ]) (build small) (editSome 5 (build small)))
                  "and answers as a cold diff does"

          testCase "a typed-key tick renders a key string at most once per row the delta carries"
          <| fun _ ->
              // Phase 283 — COUNTED. The witness declares its key equality, so the diff pairs the
              // rows by the typed id and renders `KeyString` only for a row the delta carries (an
              // added row; a changed, moved or removed row reuses the key string the prior source
              // already holds), and the refresh reads the keys the delta carries. On the pre-phase
              // tree a tick rendered one key per row of the new source (n), which this case holds
              // red: an in-place tick of 1,000 rows must render none.
              //
              // Phase 284 — and the same count for a consumer's OWN witness, a composite `(id, grp)`
              // key declared through `RowIdentity.withKeyEquality`: the public route takes the typed
              // path exactly as the reference witness does. Its string reference is an undeclared
              // copy of itself (the same scheme and key strings, the string path).
              typedTickRenders "byColumn" countingTypedId (fun () -> countingId (ref 0))

              typedTickRenders "custom (id, grp)" customTickKey (fun () ->
                  let w = customTickKey (ref 0)

                  { w with
                      KeyString = fun id -> w.KeyString id }) ]

// ---------------------------------------------------------------------------
//  Phase 274 — the refresh's bookkeeping, held over columns.
//
//  The refresh walked one `Work` record per source row, rebuilt it at every
//  step, copied a row per `Derive` and transposed the source in and the result
//  out; since Phase 274 it holds the frame as column arrays with a slot order,
//  so a one-row refresh allocates a few machine words per source row rather
//  than a record and a row copy per row per step. `ScalingTests` holds the
//  clock half (the table-fed tick within 1.5 times the full evaluation); this
//  is the counted half, on the calling thread's allocation counter, which no
//  other process can move.
// ---------------------------------------------------------------------------

/// How many bytes a one-row refresh may allocate per SOURCE ROW, whatever the pipeline. Measured on
/// this family's Debug build at 20,000 rows after Phase 274: 65 to 98 bytes per row across the
/// pipelines below (the token and slot arrays, one cell array per evaluating step, and the result's
/// lists). On the pre-274 tree the row-local derive alone allocated 896 bytes per row — red here by
/// four and a half times. The bound sits between the two, about twice the measured figure.
let private refreshBytesPerRowBound = 200.0

/// The group tail beside `tickPipelines`: the second walk, over the group table.
let private bookkeepingPipelines: (string * Transform list) list =
    tickPipelines
    @ [ "filter > groupBy > filter", pipeline @ [ Filter(Binary(Gt, Col "n", Lit(Int 0))) ] ]

[<Tests>]
let columnarBookkeepingTests =
    testList
        "the refresh's bookkeeping is columnar"
        [ testCase "a one-row refresh allocates a bounded few words per source row"
          <| fun _ ->
              for label, p in bookkeepingPipelines do
                  let n = large
                  let before = build n
                  let after = editSome 1 before
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)
                  let refreshed = ok (Incremental.refreshOn idw p state delta after)

                  Expect.equal
                      (Ok(Incremental.result refreshed))
                      (DataFrame.evalPipeline p after)
                      (sprintf "%s: refresh = reference" label)

                  let bytes =
                      ScalingTests.allocatedBytes (fun () ->
                          Incremental.refreshOn idw p state delta after |> ok |> ignore)

                  let perRow = float bytes / float n
                  printfn "  [bookkeeping] %-26s refresh allocates %9d B for %d rows (%.1f B/row)" label bytes n perRow

                  Expect.isLessThan
                      perRow
                      refreshBytesPerRowBound
                      (sprintf
                          "%s: a one-row refresh must allocate at most %.0f bytes per source row, not a record and a row copy per row per step"
                          label
                          refreshBytesPerRowBound)

          testCase "a refresh answers as the reference does, however many rows moved"
          <| fun _ ->
              // The columnar walk's own paths: a merge placing several moved rows at once (by
              // bisection into the held order), a slot order that is no longer every row after a
              // filter, a result column handed back as the source's own list, and every row edited.
              for label, p in bookkeepingPipelines do
                  for d in [ 1; 5; 40; small ] do
                      let before = build small
                      let after = editSome d before
                      let state = ok (Incremental.primeOn idw p before)
                      let delta = ok (Delta.diff idw before after)
                      let once = ok (Incremental.refreshOn idw p state delta after)

                      Expect.equal
                          (Ok(Incremental.result once))
                          (DataFrame.evalPipeline p after)
                          (sprintf "%s, %d rows edited: refresh = reference" label d)

                      // and again from the refreshed state, back to the original
                      let back = ok (Delta.diff idw after before)
                      let twice = ok (Incremental.refreshOn idw p once back before)

                      Expect.equal
                          (Ok(Incremental.result twice))
                          (DataFrame.evalPipeline p before)
                          (sprintf "%s, %d rows edited and restored: refresh = reference" label d)

          testCase "a ragged source column is padded, never handed back as the source's own list"
          <| fun _ ->
              // A column shorter than the table reads `Null` past its end (the total `Column.cell`
              // policy the reference pads by), so the result's column is NOT the source's list.
              let t = build 50

              let ragged =
                  { t with
                      Columns =
                          t.Columns
                          |> List.map (fun c ->
                              if c.Name = "grp" then
                                  { c with
                                      Cells = List.truncate 30 c.Cells }
                              else
                                  c) }

              let p = [ Derive("b", Binary(Mul, Col "a", Lit(Int 2))) ]
              let edited = editSome 3 ragged
              let state = ok (Incremental.primeOn idw p ragged)

              Expect.equal
                  (Ok(Incremental.result state))
                  (DataFrame.evalPipeline p ragged)
                  "prime over a ragged source = reference"

              let refreshed =
                  ok (Incremental.refreshOn idw p state (ok (Delta.diff idw ragged edited)) edited)

              Expect.equal
                  (Ok(Incremental.result refreshed))
                  (DataFrame.evalPipeline p edited)
                  "refresh over a ragged source = reference" ]
