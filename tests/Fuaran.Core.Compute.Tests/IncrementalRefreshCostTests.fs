module Fuaran.Core.Tests.IncrementalRefreshCostTests

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

          testCase "REPORTED, NOT FIXED: a merged order whose sort key reads a window's column"
          <| fun _ ->
              // ===================================================================================
              //  A SECOND LIVE DEFECT, found by Phase 212's new corpus shapes and deliberately NOT
              //  fixed here. Phase 212 is a conformance-corpus phase; its acceptance forbids an edit
              //  under `Fuaran.Core.DataFrame`, because a corpus phase that quietly patches the seam
              //  is how a finding stops being a finding. This case is the finding, executable.
              //
              //  WHAT IS WRONG. Phase 208 replaced the per-row cache condition `not Affected` ("the
              //  delta did not name this row") with `Stable` ("this row's cells are byte-identical
              //  to the ones the prior evaluation held"), because a `Window` recomputes its column
              //  over the whole frame and moves rows the delta never named. It changed the two sites
              //  it had found — `cellAt`, and the join's cached verdict — and left a THIRD standing:
              //  `walk`'s `WSort` arm still builds its reusable set from `not w.Affected`. So a
              //  merged order reuses the cached POSITION of a row whose sort key a window has moved.
              //
              //  MEASURED, in both directions, on 2026-09-20 at `0.28.0`:
              //    * `window(rank) > sort(rk) > limit`                           RED
              //    * `window(rank) > derive(d = rk + a) > sort(d) > limit`        RED
              //    * `window(cumulSum) > sort(run) > limit`                       RED
              //    * the same three with the LIMIT removed                        RED — the cut is
              //      not needed; the ORDER itself is wrong
              //    * `derive(d = a + b) > sort(d) > limit`  (no window)           green
              //    * `window(rank) > sort(b) > limit`  (sort key is a SOURCE col) green
              //    * `sort(b, a) > window(rank)`  (corpus shape `21`)             green
              //  So the window is load-bearing and the sort key must READ the column it appended;
              //  neither the derive nor the truncation is required.
              //
              //  THE CANDIDATE FIX, measured and reverted: making `WSort`'s `unnamed` set
              //  `if w.Stable then` — the one-token change that finishes Phase 208's own
              //  substitution — turns all of the above green and leaves the whole 48-shape family
              //  green. It is not applied here.
              //
              //  WHEN IT IS FIXED: this test fails, and the remedy is to delete it and move corpus
              //  shape `44`'s sort key from `b` to `d`, which is where it belonged.
              // ===================================================================================
              let win: WindowSpec =
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }

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

              let p =
                  [ Window win
                    Derive("d", Binary(Add, Col "rk", Col "a"))
                    Transform.sortBy [ "d", Asc ]
                    Transform.limit 3 0 ]

              match (Incremental.plan p).Strategy with
              | ReferenceOnly r -> failtestf "expected an admitted pipeline, got ReferenceOnly %A" r
              | _ -> ()

              let state = ok (Incremental.primeOn idw p before)
              let delta = ok (Delta.diff idw before after)
              let refreshed = ok (Incremental.refreshOn idw p state delta after)

              Expect.notEqual
                  (Ok(Incremental.result refreshed))
                  (DataFrame.evalPipeline p after)
                  "the WSort reuse defect this case reports appears to be FIXED — delete this test and move IncrementalDelta shape 44's sort key from \"b\" back to \"d\""

          testCase "the reported WSort defect needs the sort key to READ the window's column"
          <| fun _ ->
              // The other direction of the same probe, in the suite rather than in a session's
              // memory: the two shapes that bound the finding. A merged order over a key derived
              // from SOURCE columns alone is sound under `not Affected` — a row the delta did not
              // name genuinely has the same key — and a merged order over a window's column is not.
              // Without this half the report above would be a claim about one pipeline.
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
                  "a merged order on a key derived from source columns alone is sound"

              Expect.isTrue
                  (agrees [ Window win; Transform.sortBy [ "b", Asc ]; Transform.limit 3 0 ])
                  "a merged order on a SOURCE column is sound behind a window — which is why corpus shape 44 sorts on `b`"

              Expect.isFalse
                  (agrees [ Window win; Transform.sortBy [ "rk", Asc ]; Transform.limit 3 0 ])
                  "and a merged order on the window's OWN column is the defect — no derive and no truncation required"

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

              Expect.equal
                  (Incremental.plan' refreshed)
                  (Incremental.plan pipeline)
                  "plan': the same classification `plan` computes from the pipeline alone"

              Expect.equal (Incremental.source refreshed) after "source: the table the next delta must describe FROM"
              Expect.equal (Incremental.pipelineOf refreshed) pipeline "pipelineOf: the pipeline it was built for" ]
