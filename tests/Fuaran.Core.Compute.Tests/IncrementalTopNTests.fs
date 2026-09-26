module Fuaran.Core.Tests.IncrementalTopNTests

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 207 — a `Limit` admitted to the incremental seam.
//
//  The generated equivalence family (`IncrementalDelta.laws`) certifies that the
//  incremental answer IS the reference answer over a corpus that now draws
//  top-N pipelines, and its adequacy demand insists the corpus REACHES a
//  restricted top-N refresh rather than only a declined one. What it cannot say
//  is that the four cases a top-N maintenance gets wrong were each reached ON
//  PURPOSE: a random draw that happens to hit a tie at the cut is evidence, but
//  the next seed decides whether it is evidence again.
//
//  So the four are pinned here, deterministically, one case each, and each one
//  ASSERTS THE CONDITION IT IS NAMED FOR before it asserts the answer — a row
//  that enters the window, a row that leaves it, a tie AT the cut, and a
//  non-zero offset. A case that stopped reaching its own condition would
//  otherwise keep passing while testing nothing, which is exactly how the
//  merged-order family passed every seed over one-row tables before Phase 115
//  measured it.
//
//  THE TIE-BREAK THESE CASES RELY ON, stated once because the whole family
//  rests on it. `DataFrame.evalSort` is `List.sortWith`, which is STABLE, so a
//  sort is a sort by (key, arrival position) and a tie at the cut is decided by
//  which tying row arrived first. The seam reproduces it rather than
//  re-deriving it: `Incremental.mergeOrders` breaks an equal comparison by
//  arrival position, and the cached order is reused only for rows whose
//  relative ARRIVAL order has not moved. A `Limit` after that sort therefore
//  keeps the earlier-arriving of two tying rows, and TWO cases below hold it
//  there rather than one, because they reach different code: `tie at the cut`
//  reorders the whole frame and lands on `List.sortWith`'s own stability, while
//  `a merged order's arrival tiebreak` is built so the cached order is REUSABLE
//  and the promoted row is merged back into a run of ties — which is the only
//  shape where `mergeOrders`' tiebreak decides the answer. Each was shown red
//  against the perturbation it is named for before it was believed; the first
//  two drafts of this file passed both perturbations, which is what the second
//  case and the interleaved-dead-rows case below exist to fix.
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

/// Prime over `before`, diff to `after`, refresh — the delta is truthful by construction.
let private step (pipeline: Transform list) (before: Table) (after: Table) =
    let state = ok (Incremental.primeOn idw pipeline before)
    let delta = ok (Delta.diff idw before after)
    ok (Incremental.refreshOn idw pipeline state delta after)

/// The reference answer, as the identity column of the rows it kept, in order. The window's
/// MEMBERSHIP is what a top-N maintenance gets wrong, and reading it as ids is what lets a case
/// state "r3 entered and r0 left" rather than compare two opaque tables.
let private idsOf (t: Table) : string list =
    match t.Columns |> List.tryFind (fun c -> c.Name = "id") with
    | None -> failtest "the result carries no `id` column"
    | Some c ->
        c.Cells
        |> List.map (function
            | Str s -> s
            | other -> failtestf "expected a string identity, got %A" other)

let private referenceIds (pipeline: Transform list) (t: Table) : string list =
    idsOf (ok (DataFrame.evalPipeline pipeline t))

/// The two assertions every case below makes: the incremental answer is the reference answer,
/// and the refresh was RESTRICTED rather than a fall-back wearing the right result.
let private expectRestrictedAndEqual (pipeline: Transform list) (after: Table) (s: IncrementalEval) =
    Expect.equal
        (Ok(Incremental.result s))
        (DataFrame.evalPipeline pipeline after)
        "incremental result = reference result"

    match (Incremental.footprint s).Recompute with
    | RowsRecomputed _
    | GroupsRecomputed _
    | ReusedPrior -> ()
    | other -> failtestf "expected a restricted refresh, got %A" other

// ---- the fixtures ----

/// Descending by `a`, keep the top two. `a` is distinct here, so the window's membership is decided
/// by the key alone and `enter` / `leave` are unambiguous.
let private topTwoByA = [ Transform.sortBy [ "a", Desc ]; Transform.limit 2 0 ]

/// Five rows whose `a` values are distinct and whose top two are `r4` (9) then `r3` (7).
let private distinctRows =
    [ "r0", Int 1, Int 0
      "r1", Int 3, Int 0
      "r2", Int 5, Int 1
      "r3", Int 7, Int 1
      "r4", Int 9, Int 2 ]

[<Tests>]
let topNTests =
    testList
        "IncrementalTopN"
        [

          // ================= the plan =================

          testCase "a limit after a sort plans as a truncated order, not a decline"
          <| fun _ ->
              let p = Incremental.plan topTwoByA

              Expect.equal p.Steps [ MergeOrder [ "a", Desc ]; TruncateOrder(2, 0) ] "both steps are admitted, by name"

              Expect.equal p.Strategy RowLocal "`Filter > Sort > Limit`'s shape is restricted, not `ReferenceOnly`"

              Expect.isTrue (Incremental.isIncremental p) "isIncremental agrees"

          testCase "a limit with NO sort in front of it is admitted too"
          <| fun _ ->
              // The shard's second acceptance shape, and the one that shows the admission is not
              // conditioned on a preceding `Sort`: arrival order is an order the walk maintains
              // exactly as a merged one is. A `Limit` the walk REACHES is over a maintained order
              // by construction — every step this walk admits preserves the reference's row set and
              // order — so there is no second decline class here to test.
              let p =
                  Incremental.plan
                      [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                        Project [ "id", "id"; "a", "a" ]
                        Transform.limit 2 0 ]

              Expect.equal p.Strategy RowLocal "`Filter > Project > Limit` is restricted"

              Expect.equal
                  (Incremental.plan [ Transform.limit 3 1 ]).Strategy
                  RowLocal
                  "a bare limit, with nothing in front of it at all, is restricted"

              Expect.equal
                  (Incremental.plan
                      [ Transform.limit 2 0
                        GroupBy([ "b" ], [ { Name = "n"; Fn = Count; Of = "a" } ]) ])
                      .Strategy
                  RowLocalThenGroups
                  "a limit feeding a maintained group is restricted"

          testCase "a limit whose window is still a param declines, naming the param"
          <| fun _ ->
              // `0.23.0`'s rule, applied to the second verb that carries scalar slots. It is the
              // ONLY way a `Limit` still declines, and the reason is the accurate one: the window
              // is not known without an env, not that the step's output depends on rows the delta
              // does not name. Substitute the param and the plan is computable again — which is
              // the second half of the assertion, because a decline that could not be lifted would
              // be a different claim.
              let take = Limit(Slot.Param "take", Slot.Lit 0)

              Expect.equal
                  (Incremental.plan [ Transform.sortBy [ "a", Desc ]; take ]).Strategy
                  (ReferenceOnly(UnresolvedSlotParam("limit", "take")))
                  "the decline names the verb and the unresolved param"

              Expect.equal
                  (Incremental.plan [ Limit(Slot.Lit 2, Slot.Param "skip") ]).Strategy
                  (ReferenceOnly(UnresolvedSlotParam("limit", "skip")))
                  "an unresolved OFFSET declines on the same terms"

              Expect.equal
                  (Incremental.plan (Transform.substitute (Map.ofList [ "take", Int 2 ]) [ take ])).Strategy
                  RowLocal
                  "substituting the param makes the plan computable again"

          // ================= the four boundary cases =================

          testCase "a row ENTERS the window"
          <| fun _ ->
              let before = table distinctRows
              // `r0` climbs from 1 to 99: it enters the top two, and `r3` is pushed out.
              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 99, b else i, a, b)
                  )

              let wasIn = referenceIds topTwoByA before
              let nowIn = referenceIds topTwoByA after

              Expect.isFalse (List.contains "r0" wasIn) "the case reaches its own condition: r0 was OUTSIDE the window"
              Expect.isTrue (List.contains "r0" nowIn) "and is INSIDE it after the edit"

              expectRestrictedAndEqual topTwoByA after (step topTwoByA before after)

          testCase "a row LEAVES the window"
          <| fun _ ->
              let before = table distinctRows
              // `r4` falls from 9 to -1: it drops out of the top two and `r2` takes its place.
              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int -1, b else i, a, b)
                  )

              let wasIn = referenceIds topTwoByA before
              let nowIn = referenceIds topTwoByA after

              Expect.isTrue (List.contains "r4" wasIn) "the case reaches its own condition: r4 was INSIDE the window"
              Expect.isFalse (List.contains "r4" nowIn) "and is OUTSIDE it after the edit"

              expectRestrictedAndEqual topTwoByA after (step topTwoByA before after)

          testCase "a row that leaves the window and comes BACK is re-evaluated, not read from a stale prefix"
          <| fun _ ->
              // The one shape a cache makes tempting to get wrong. A row outside the window stops
              // accumulating cached cells at the limit, exactly as a filtered row does; when it
              // returns, the steps after the limit must evaluate it afresh rather than read a
              // prefix that stops short. Two refreshes over one state, out and back.
              let before = table distinctRows

              let out =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int -1, b else i, a, b)
                  )

              let back =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 50, b else i, a, b)
                  )

              // A derive AFTER the limit: its cells are cached per row at an `evalIdx` past the
              // truncation, so a returning row's prefix cannot reach it.
              let pipeline =
                  [ Transform.sortBy [ "a", Desc ]
                    Transform.limit 2 0
                    Derive("d", Binary(Mul, Col "a", Lit(Int 10))) ]

              let state = ok (Incremental.primeOn idw pipeline before)
              let d1 = ok (Delta.diff idw before out)
              let s1 = ok (Incremental.refreshOn idw pipeline state d1 out)

              Expect.equal
                  (Ok(Incremental.result s1))
                  (DataFrame.evalPipeline pipeline out)
                  "the refresh that evicts r4 is right"

              let d2 = ok (Delta.diff idw out back)
              let s2 = ok (Incremental.refreshOn idw pipeline s1 d2 back)

              Expect.isTrue
                  (List.contains "r4" (idsOf (Incremental.result s2)))
                  "the case reaches its own condition: r4 came back"

              expectRestrictedAndEqual pipeline back s2

          testCase "a limit with dead rows IN FRONT of it counts the live frame, not the carried one"
          <| fun _ ->
              // The walk carries a filtered row past the step that killed it, so its cached prefix
              // survives for the next refresh — which means the list the limit walks is NOT the
              // frame the reference holds. A `Sort` hides this, because it moves the dead rows to
              // the tail; a `Filter > Limit` with no sort between them does not, and the dead row
              // here arrives FIRST. An implementation that advanced its position counter over
              // carried rows keeps one row too few, and is right on every sort-bearing case.
              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Transform.limit 2 0 ]

              let rows =
                  [ "r0", Int -1, Int 0
                    "r1", Int 3, Int 0
                    "r2", Int 5, Int 1
                    "r3", Int 7, Int 1
                    "r4", Int 9, Int 2 ]

              let before = table rows

              let after =
                  table (rows |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 2, b else i, a, b))

              Expect.equal
                  (referenceIds pipeline before)
                  [ "r1"; "r2" ]
                  "the case reaches its own condition: a dropped row arrives before both kept ones"

              expectRestrictedAndEqual pipeline after (step pipeline before after)

              // And with the dead row arriving INSIDE the window's span rather than before it.
              let mid =
                  [ "r0", Int 3, Int 0
                    "r1", Int -1, Int 0
                    "r2", Int 5, Int 1
                    "r3", Int 7, Int 1
                    "r4", Int 9, Int 2 ]

              Expect.equal (referenceIds pipeline (table mid)) [ "r0"; "r2" ] "the dropped row is skipped, not counted"

              expectRestrictedAndEqual
                  pipeline
                  (table (mid |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 2, b else i, a, b)))
                  (step
                      pipeline
                      (table mid)
                      (table (mid |> List.map (fun (i, a, b) -> if i = "r4" then i, Int 2, b else i, a, b))))

          testCase "a MERGED order's arrival tiebreak decides who is at the cut"
          <| fun _ ->
              // The tie case that reaches the merge rather than a re-sort. Every row ties on the
              // sort key except the one the edit promotes, and the promoted row ARRIVED FIRST — so
              // the cached order is reusable (no unnamed row's arrival position moved), the named
              // row is merged back into a run of ties, and the arrival-position tiebreak is the
              // only thing that puts it at the head. Drop that tiebreak and the merge produces an
              // order that is correctly sorted, differs from the reference's on the first tie, and
              // hands the limit two rows that are not the reference's two.
              let tiedButR0 =
                  [ "r0", Int 0, Int 0
                    "r1", Int 4, Int 0
                    "r2", Int 4, Int 1
                    "r3", Int 4, Int 1
                    "r4", Int 4, Int 2 ]

              let before = table tiedButR0

              let after =
                  table (
                      tiedButR0
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 4, b else i, a, b)
                  )

              Expect.equal (referenceIds topTwoByA before) [ "r1"; "r2" ] "before: r0 sorts last"

              Expect.equal
                  (referenceIds topTwoByA after)
                  [ "r0"; "r1" ]
                  "the case reaches its own condition: r0 ties with everything and wins on arrival"

              expectRestrictedAndEqual topTwoByA after (step topTwoByA before after)

          testCase "a TIE at the cut is broken by arrival position, as the reference's stable sort breaks it"
          <| fun _ ->
              // Every row ties on the sort key, so the window's membership is decided ENTIRELY by
              // the arrival-position tiebreak a stable sort applies — and the edit moves a row that
              // is not in the window and does not change the key, so nothing the delta names says
              // anything about who is at the cut. A merge that had no tiebreak, or one that reused
              // its cached order without checking arrival order, answers this with the wrong rows.
              let tied =
                  [ "r0", Int 4, Int 0
                    "r1", Int 4, Int 0
                    "r2", Int 4, Int 1
                    "r3", Int 4, Int 1
                    "r4", Int 4, Int 2 ]

              let before = table tied

              let after =
                  table (tied |> List.map (fun (i, a, b) -> if i = "r4" then i, a, Int 9 else i, a, b))

              let kept = referenceIds topTwoByA before

              Expect.equal kept [ "r0"; "r1" ] "the case reaches its own condition: the cut sits inside a run of ties"

              expectRestrictedAndEqual topTwoByA after (step topTwoByA before after)

              // And the same tie under a REORDERING, which an identity diff reports as quiet: the
              // arrival positions move while no row is named, so the rows at the cut change with
              // nothing in the delta to say so.
              let reversed = table (List.rev tied)

              Expect.equal (referenceIds topTwoByA reversed) [ "r4"; "r3" ] "reversing arrival moves the cut's rows"

              expectRestrictedAndEqual topTwoByA reversed (step topTwoByA before reversed)

          testCase "an OFFSET window skips before it keeps, and an edit inside the skipped part still moves it"
          <| fun _ ->
              let offsetPipeline = [ Transform.sortBy [ "a", Desc ]; Transform.limit 2 2 ]
              let before = table distinctRows

              Expect.equal
                  (referenceIds offsetPipeline before)
                  [ "r2"; "r1" ]
                  "the case reaches its own condition: the offset skips the top two"

              // The edit is to `r4`, which sits in the SKIPPED prefix. Its key falls below the
              // window entirely, so every row shifts up by one and both kept rows change — an
              // offset window is moved by a change that never appears in its own output.
              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r4" then i, Int -1, b else i, a, b)
                  )

              Expect.equal (referenceIds offsetPipeline after) [ "r1"; "r0" ] "both kept rows moved"

              expectRestrictedAndEqual offsetPipeline after (step offsetPipeline before after)

          // ================= the degenerate windows =================

          testCase "a zero, a negative and an over-long window are the reference's own clamps"
          <| fun _ ->
              // `evalLimit` clamps both slots with `max 0` and skips no further than the frame, so
              // the seam must reproduce all three rather than trip on them. The reference is the
              // oracle for each; what is asserted is that the restricted walk agrees with it.
              let before = table distinctRows

              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 99, b else i, a, b)
                  )

              for p in
                  [ [ Transform.sortBy [ "a", Desc ]; Transform.limit 0 0 ]
                    [ Transform.sortBy [ "a", Desc ]; Transform.limit (-3) 0 ]
                    [ Transform.sortBy [ "a", Desc ]; Transform.limit 2 (-3) ]
                    [ Transform.sortBy [ "a", Desc ]; Transform.limit 99 0 ]
                    [ Transform.sortBy [ "a", Desc ]; Transform.limit 2 99 ]
                    [ Transform.sortBy [ "a", Desc ]
                      Transform.limit System.Int32.MaxValue (System.Int32.MaxValue - 1) ] ] do
                  expectRestrictedAndEqual p after (step p before after)

          testCase "a limit over an EMPTY frame, and a limit whose input a filter emptied"
          <| fun _ ->
              let before = table distinctRows
              let empty = table []

              expectRestrictedAndEqual topTwoByA empty (step topTwoByA before empty)

              let filtered =
                  [ Filter(Binary(Gt, Col "a", Lit(Int 1000)))
                    Transform.sortBy [ "a", Desc ]
                    Transform.limit 2 0 ]

              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 99, b else i, a, b)
                  )

              expectRestrictedAndEqual filtered after (step filtered before after)

          // ================= the footprint =================

          testCase "a limit evaluates nothing, so the saving is the steps in front of it"
          <| fun _ ->
              // What the admission actually buys, stated as the instrument reads it. The pipeline
              // evaluates one expression per row at the filter; a full evaluation over the changed
              // source charges five, and the restricted refresh charges one — the row the delta
              // named. The limit itself contributes nothing to either side, exactly as a `Sort`
              // does, which is why the saving has to be read off the steps before it.
              let pipeline =
                  [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
                    Transform.sortBy [ "a", Desc ]
                    Transform.limit 2 0 ]

              let before = table distinctRows

              let after =
                  table (
                      distinctRows
                      |> List.map (fun (i, a, b) -> if i = "r0" then i, Int 99, b else i, a, b)
                  )

              let full = ok (Incremental.primeOn idw pipeline after)

              Expect.equal
                  (Incremental.footprint full).Recompute
                  (Primed 5)
                  "a full evaluation charges the filter over every row"

              let next = step pipeline before after

              Expect.equal
                  (Incremental.footprint next).Recompute
                  (RowsRecomputed 1)
                  "the refresh charges the one row the delta named"

              Expect.equal (Incremental.footprint next).SourceRows 5 "`SourceRows` stays its own field"
              Expect.equal (Incremental.footprint next).ResultRows 2 "and the result is the window"

              expectRestrictedAndEqual pipeline after next

          testCase "the reason string for a param-slotted limit reads"
          <| fun _ ->
              let s = Incremental.reasonString (UnresolvedSlotParam("limit", "take"))
              Expect.stringContains s "limit" "it names the verb"
              Expect.stringContains s "take" "and the param" ]
