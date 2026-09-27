module Fuaran.Core.Tests.ScalingTests

// ---------------------------------------------------------------------------
//  Phase 206 — the wall-clock scaling gate, beside the footprint laws.
//
//  The footprint instrument (Phase 117) counts what a refresh RE-EVALUATES and
//  is correct about it; it does not track time, so an evaluator could evaluate
//  one row expression out of ten thousand and still be slower than the full
//  evaluation it replaces. It was. `toFrame` read the frame by calling
//  `Column.cell i c` per row per column over a linked list, so every cell read
//  walked the list from the head and the evaluator was QUADRATIC in the row
//  count; `Delta.diff` and the refresh reached the same pattern through their
//  own per-row readers.
//
//  What this family asserts is a RATIO between two sizes measured in one
//  process on one machine, never an absolute time. Core owns no clock (GP6) and
//  an absolute threshold is a flaky test that eventually fails on a slow runner
//  for no reason a reader can act on; a ratio is a statement about the SHAPE of
//  the cost curve, and a quadratic cannot meet it on any machine while a linear
//  pass meets it comfortably on every one. Twenty times the rows: linear costs
//  about twenty times the time, quadratic about four hundred.
//
//  The bound is deliberately loose — five times the linear expectation, which
//  clears the n-log-n shapes the seam's keyed maps really have and still refuses
//  a quadratic by a factor of four. A gate that is occasionally red for no
//  reason is one people learn to re-run, which is worth more than tightness.
//
//  The last case is the claim the incremental seam exists to make and the one
//  the footprint could never state: with one row of twenty thousand edited, a
//  RESTRICTED refresh finishes sooner than the full evaluation it replaces.
//
//  Phase 206 could assert that only for a pipeline whose row expression COSTS
//  something, and recorded the qualification as a finding: with a single
//  comparison the seam's own per-source-row bookkeeping outweighed the expression
//  it avoided, so the refresh LOST, and 207 and 202 each re-measured the same
//  answer one verb along. Phase 208 removed that bookkeeping and the
//  one-comparison case is asserted here too, on all three pipelines. The pre-208
//  figures are kept in each case's comment: they are what the assertion would
//  have scored, which is the only evidence that it discriminates.
//
//  Phase 265 made the full evaluation of the group-by pipelines about twice as
//  fast, and two of those one-comparison cases (the plain grouping and the group
//  tail) became a bounded LOSS rather than a win: see `cheapRefreshLossBound`.
//  The costly-expression cases and the top-N case still assert the win.
//
//  Phase 272 added the table-fed caller's other half: `Delta.diff`, which such
//  a caller runs before every refresh and which cost up to thirty-six times the
//  evaluation it fed. It is held against its own floor (the keying of both
//  tables), and the whole tick is measured and printed beside it, unasserted,
//  with the reason in that case's comment.
// ---------------------------------------------------------------------------

open System.Diagnostics
open Expecto
open Fuaran.Core

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private idw = RowIdentity.byColumn "id"

/// The small size and the large one. Twenty times the rows rather than ten, measured: at ten the
/// PRE-FIX reference evaluator scored 31.18 against a bound of 30, because at a thousand rows the
/// quadratic term has not yet swamped the fixed costs — a four-per-cent red is not a discriminator,
/// it is a coin toss on a different machine. At twenty the quadratic signature is unmistakable
/// (a pure quadratic scores four hundred) and a linear pass still lands near twenty.
let private small = 1_000
let private large = 20_000

let private sizeRatio = float large / float small

/// Twenty times the rows may cost at most FIVE times the linear expectation.
///
/// The number is measured rather than chosen, and the measurement is the reason it is not tighter.
/// The cheapest honest shape here is NOT linear: the grouping and the delta key rows through
/// persistent maps over string and string-list keys, so they are n log n with a comparison whose own
/// cost grows, on top of cache behaviour that gets worse with the working set. Post-206 the three
/// cases landed at roughly 34, 52 and 44 against a linear expectation of 20.
///
/// **Phase 208 moved them UP rather than down, and the direction is worth understanding before
/// anyone reads it as a regression.** Measured after: 28, 49 and 56, with the top-N at 42 and the
/// group tail at 51. Each of these cases times prime + diff + refresh, and what 208 made cheaper is
/// the refresh's FIXED per-row bookkeeping — which is proportionally a larger share of the 1,000-row
/// leg than of the 20,000-row one, so removing it lowers both legs and RAISES their ratio. A ratio
/// is a shape, not a cost: the absolute figures all fell (the refresh-versus-full cases below record
/// by how much). The headroom to the bound of 100 is what has narrowed, which is the thing to watch
/// if a later phase improves the small leg again.
///
/// So the bound sits at 100: about twice the worst legitimate shape, and four times below the
/// quadratic (400) the family exists to refuse, which the PRE-FIX code scored at 163, 413 and 440.
/// Both margins matter and they pull against each other; a false red costs a re-run and
/// teaches people to re-run gates, which is dearer than the false green this could admit — and a
/// false green here would need a quadratic to score under a hundred, which nothing measured does.
let private ratioBound = 5.0 * sizeRatio

/// Phase 265 — how far a restricted refresh may fall BEHIND the full evaluation on a pipeline whose
/// row expression is a single comparison. Until Phase 265 those cases asserted that the refresh WINS;
/// that phase made the full evaluation of `Filter > GroupBy` about twice as fast (a hash partition in
/// place of a persistent map over minted token strings) while the refresh, whose grouping was
/// already carried, kept its per-source-row bookkeeping, so on the cheapest pipeline the full pass
/// now finishes first. BenchmarkDotNet medians at 20,000 rows, Release, before and after the phase:
/// `Filter > GroupBy` refresh 11.7 ms against a full 14.4 ms, then 12.5 ms against 8.3 ms; the group
/// tail 12.0 ms against 18.8 ms, then 11.0 ms against 7.8 ms. This family's own Release runs after it
/// scored the refresh at 1.5 to 2.1 times the full evaluation, and a Debug build (the gate's) at
/// about 1.0.
///
/// The bound was three times. What the Phase 208 cases were written to refuse is the pre-208 seam,
/// whose bookkeeping cost about 70 ms at this size — some ten times today's full evaluation, so it
/// is refused by a wide margin — and a refresh that regresses by half again is refused too. Whether
/// the seam should win this case outright again is a question for its per-row bookkeeping, not for
/// this bound.
///
/// Phase 267 brought the one-comparison top-N case under the same bound. The columnar frame made a
/// full `Filter > Sort > Limit` a selection and a permutation over shared vectors, with the sort
/// comparing typed carriers, while the refresh still merges one row into an order it holds over
/// boxed rows: this family's Debug runs at 20,000 rows, quiet machine, before and after the phase —
/// refresh 57.1 ms against a full 91.1 ms, then 43.3 ms against 21.7 ms — so the refresh now trails
/// by about two times, and the bound refuses the same regressions here as above.
///
/// Phase 270 made the full pass faster again and raised the bound to four. The comparison kernels
/// answer a one-comparison `Filter` over a typed column as a bitmap, where the refresh still walks
/// its maintained groups: the group-tail case's Debug runs at 20,000 rows, the tree before and after
/// the phase interleaved three times on one machine — refresh 36.9, 36.4, 38.1 ms against a full
/// 15.3, 16.6, 15.4 ms (2.2 to 2.5 times), then 30.8, 32.1, 35.4 ms against 12.8, 11.5, 12.3 ms (2.4
/// to 2.9 times), and 3.1 once inside the whole suite. The refresh did not move; the full pass did.
/// Four still refuses the pre-208 seam (about six times today's full evaluation) and a refresh that
/// regresses by half again.
let private cheapRefreshLossBound = 4.0

/// Phase 269 — the same bound for the one-comparison TOP-N case, which that phase moved out from
/// under `cheapRefreshLossBound`. The planner runs `Sort` > `Limit` as one stable top-n, so a full
/// `Filter > Sort > Limit 10` no longer sorts twenty thousand rows: Debug figures on this family
/// before the phase were about 43 ms full against a 41 to 56 ms refresh, and after it 15 to 17 ms
/// full against the same refresh — the refresh, whose merge into a held order was the saving, now
/// LOSES to the fused full evaluation by about three times, and at three the bound was a coin toss
/// on a loaded machine (56.0 against 52.3 on one run, 41.1 against 44.3 on the next). Five is above
/// the loss measured and below the ten times the pre-208 seam lost by. Whether the seam should win
/// this case again is a question for its per-row bookkeeping, which is where the 41 ms goes.
let private topNRefreshLossBound = 5.0

// Phase 272 was asked to retire the bound above and `cheapRefreshLossBound` into one family holding
// `Delta.diff` + refresh to 1.5 times the full evaluation, and measured why that family cannot be
// green from the table-fed entry point: see the "table-fed tick" case below and "What it costs on
// the clock" in docs/incremental-evaluation.md. Both bounds therefore stand as they were; neither
// was loosened.

/// Phase 272 — how much `Delta.diff` may cost beyond minting both tables' keys through the witness,
/// which is the floor of any diff by identity (`KeyString` is the only thing that can say what a key
/// is, and a diff must know every row's key in both tables).
///
/// Measured on this family's Debug build, 20,000 rows, the same machine and the same hour: the
/// row-token diff this phase replaced cost 154.2 ms against a keying floor of 2.89 ms — 53 times its
/// floor, and fourteen times the full evaluation of `pipeline` (10.9 ms) — so this bound is red on
/// the pre-phase tree by a factor of eighteen. The dense diff costs 5.6 ms against 3.0 to 3.3 ms, 1.7
/// to 2.0 times, at both sizes. Three leaves half as much again for a loaded machine; it is not a
/// number to raise when the evaluator moves, because the evaluator is not in it.
let private diffFloorBound = 3.0

/// A table of `n` rows over four columns — a string identity, a grouping key of bounded cardinality,
/// and two integer measures. The identity is what `RowIdentity.byColumn` keys on; the grouping key
/// is bounded so the `GroupBy` produces a small result whatever the source size, which keeps the
/// measurement about the SOURCE scan rather than about the output.
let private build (n: int) : Table =
    { Schema = [ "id", StringType; "grp", StringType; "a", IntType; "b", IntType ]
      Columns =
        [ Column.create "id" StringType [ for i in 0 .. n - 1 -> Str("r" + string i) ]
          Column.create "grp" StringType [ for i in 0 .. n - 1 -> Str("g" + string (i % 17)) ]
          Column.create "a" IntType [ for i in 0 .. n - 1 -> Int i ]
          Column.create "b" IntType [ for i in 0 .. n - 1 -> Int(i % 7) ] ]

    }

/// Edit ONE row of the table — the delta the incremental seam is meant to answer cheaply.
let private editOne (t: Table) : Table =
    let n = Table.rowCount t

    { t with
        Columns =
            t.Columns
            |> List.map (fun c ->
                if c.Name <> "a" then
                    c
                else
                    { c with
                        Cells = c.Cells |> List.mapi (fun i cell -> if i = n / 2 then Int -1 else cell) }) }

/// The probe's pipeline: a row-local predicate every row satisfies (so the evaluator really does
/// evaluate one expression per row — a filter that discarded rows would flatter the larger size),
/// then a grouping with two aggregates.
let private pipeline: Transform list =
    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
      GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ]) ]

/// The same pipeline with a row expression that costs something to evaluate — sixteen nesting
/// levels, 129 expression nodes, rather than one comparison.
///
/// It was the instrument for a DIFFERENT claim from the one-comparison case, and Phase 206's finding
/// was that the two answers differed: the seam's whole proposition is "evaluate the row expression
/// once instead of n times", so what it saves is n−1 evaluations of THAT expression, while what it
/// spent was its own per-source-row bookkeeping — an identity token, its uniqueness check, two
/// lookups into the prior row-cell map, a group-membership entry, all string-keyed
/// persistent-map operations that did not shrink when the expression did.
///
/// **Phase 208 removed that spend, so both cases now assert.** What this pipeline still measures that
/// the trivial one does not is the MARGIN: the full evaluation's cost rises with the expression while
/// the refresh's barely moves, so this is where the seam's saving is largest and where a regression in
/// the expression-avoidance half (rather than in the bookkeeping half) would show first. See the
/// "what it costs on the clock" section of docs/incremental-evaluation.md.
let private costlyPipeline: Transform list =
    // Addition and subtraction only, alternating, so the running value stays inside int32 whatever
    // the depth. Core refuses a silent wrap (Phase 39), so a multiplying chain overflows and the
    // test would be measuring an error path rather than an expression.
    let rec nest n e =
        if n = 0 then
            e
        else
            nest
                (n - 1)
                (Binary(Sub, Binary(Add, e, Binary(Add, Col "b", Lit(Int 2))), Binary(Add, Col "b", Lit(Int 1))))

    [ Filter(Binary(Ge, nest 16 (Col "a"), Lit(Int -1)))
      GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ]) ]

/// Phase 207 — a top-10 board: a row-local predicate every row satisfies, a sort, and the cut.
/// This is the shape the `Limit` admission was asked for, and it differs from `pipeline` above in
/// what the FULL evaluation costs as well as in what the refresh does: a full evaluation sorts the
/// whole frame every tick, while a restricted refresh merges the named rows into the order it
/// already holds. So the top-N case is the one where the seam has a second saving to show — and
/// whether that is enough to beat the baseline with a ONE-COMPARISON predicate is a question this
/// phase measured rather than assumed, because Phase 206 measured the same question for
/// `Filter > GroupBy` and the answer was no.
let private topNPipeline: Transform list =
    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
      Transform.sortBy [ "a", Desc ]
      Transform.limit 10 0 ]

/// The same top-N board with the 129-node row expression, for the same reason `costlyPipeline`
/// exists: it is the instrument for the claim that is actually the seam's, which is about the size
/// of the ROW EXPRESSION rather than the size of the table.
let private costlyTopNPipeline: Transform list =
    let rec nest n e =
        if n = 0 then
            e
        else
            nest
                (n - 1)
                (Binary(Sub, Binary(Add, e, Binary(Add, Col "b", Lit(Int 2))), Binary(Add, Col "b", Lit(Int 1))))

    [ Filter(Binary(Ge, nest 16 (Col "a"), Lit(Int -1)))
      Transform.sortBy [ "a", Desc ]
      Transform.limit 10 0 ]

/// Phase 202 — a `Having`: the same grouping as `pipeline`, with a filter over the GROUP table
/// after it. This is the shape the seam declined until this phase, and it is the one whose tail
/// work is bounded by the group count rather than by the row count — `build` holds the grouping key
/// to seventeen values whatever the source size, so the tail evaluates seventeen predicates where
/// the prefix evaluates `n`.
let private groupTailPipeline: Transform list =
    [ Filter(Binary(Ge, Col "a", Lit(Int -10)))
      GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ])
      Filter(Binary(Gt, Col "n", Lit(Int 0))) ]

/// The same `Having` with the 129-node row expression, for the reason `costlyPipeline` exists: the
/// seam's proposition is about the size of the ROW EXPRESSION, not the size of the table, and the
/// two shapes give different answers.
let private costlyGroupTailPipeline: Transform list =
    let rec nest n e =
        if n = 0 then
            e
        else
            nest
                (n - 1)
                (Binary(Sub, Binary(Add, e, Binary(Add, Col "b", Lit(Int 2))), Binary(Add, Col "b", Lit(Int 1))))

    [ Filter(Binary(Ge, nest 16 (Col "a"), Lit(Int -1)))
      GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ])
      Filter(Binary(Gt, Col "n", Lit(Int 0))) ]

/// The two ways to decide which rows a `Limit` keeps, as functions of the frame alone — the
/// shipped shape and the one it was deliberately not written as, each COUNTING the element visits
/// it makes.
///
/// These are MODELS of the step rather than a second evaluator, and they are here because the
/// claim they prove is otherwise unfalsifiable: "the maintenance adds no third quadratic class" is
/// a statement about a cost curve, and a cost curve is only a finding if the shape it excludes can
/// be shown to fail the same instrument. The naive form is the obvious one — decide each row's
/// membership by asking where it sits in the ordered frame — and a list index lookup walks from the
/// head every time, which is exactly the access pattern Phase 206 spent itself removing from three
/// files.
///
/// **They are COUNTED and not timed, and that is a finding of this phase rather than a preference.**
/// A timed version was written first and measured 12 against 62 on the 1,000 → 20,000 span where a
/// pure quadratic scores 400 — the naive shape passing the very bound it exists to fail. The cause
/// is that both functions are small and tight, so the SMALL size is measured in tier-0 JIT code and
/// the large one in tier-1 after the loop has been promoted, which deflates the ratio by roughly the
/// factor observed; carrying it as a string-keyed frame added a second confound, since string
/// equality rejects on length and the two sizes do not draw their lengths from the same
/// distribution. Neither confound touches the family's other cases, which time work heavy enough to
/// reach tier-1 during their own warm-up. Counting removes both: the numbers below are exact,
/// identical on every host, and carry no clock — which is what the rest of this seam's instruments
/// already do (GP6), and the reason the footprint was built that way in the first place.
let private keepPositional (steps: int ref) (lo: int) (keep: int) (frame: int list) : int list =
    let rec go acc i =
        function
        | [] -> List.rev acc
        | t :: tail ->
            steps.Value <- steps.Value + 1

            if i >= lo && i - lo < keep then
                go (t :: acc) (i + 1) tail
            else
                go acc (i + 1) tail

    go [] 0 frame

let private keepByIndexLookup (steps: int ref) (lo: int) (keep: int) (frame: int list) : int list =
    let indexOf t =
        let rec find i =
            function
            | [] -> -1
            | x :: tail ->
                steps.Value <- steps.Value + 1
                if x = t then i else find (i + 1) tail

        find 0 frame

    frame
    |> List.filter (fun t ->
        steps.Value <- steps.Value + 1
        let i = indexOf t
        i >= lo && i - lo < keep)

/// The BEST of `runs` timings, after one discarded warm-up and with the heap settled first.
///
/// The minimum, not the median or the mean, and that is the one methodological choice in this file
/// worth defending. Measurement noise here is strictly ADDITIVE — a collection, a scheduler steal,
/// another core waking — so no sample can come in under the true cost and the smallest sample is
/// the best estimate of it. A median still carries whatever contention was present for half the
/// run, which at these sizes is most of the variance: the first cut of this family used one and the
/// same measurement scored 38 standalone and 65 at the end of the full suite, against a bound of 60.
/// A ratio of two noisy numbers is twice as noisy again, so the estimator is what decides whether
/// this family is a gate or a coin toss.
///
/// The collection before each sample is for the same reason in the other direction: a collection
/// triggered by the PREVIOUS sample's garbage must not be billed to this one.
let private bestMs (runs: int) (f: unit -> unit) : float =
    f ()

    [ for _ in 1..runs ->
          System.GC.Collect()
          System.GC.WaitForPendingFinalizers()
          let sw = Stopwatch.StartNew()
          f ()
          sw.Stop()
          sw.Elapsed.TotalMilliseconds ]
    |> List.min

/// Measure `f` at both sizes and report `(smallMs, largeMs, ratio)`, printing the row so a gate log
/// carries the numbers a reader would otherwise have to re-measure.
let private scaling (label: string) (f: Table -> unit) : float * float * float =
    let ts = build small
    let tl = build large
    let a = bestMs 5 (fun () -> f ts)
    let b = bestMs 5 (fun () -> f tl)
    let r = if a <= 0.0 then infinity else b / a
    printfn "  [scaling] %-28s %7.2f ms @ %d -> %8.2f ms @ %d   ratio %6.2f" label a small b large r
    a, b, r

/// `scaling` over caller-built inputs at two caller-chosen sizes (Phase 264), returning the ratio. The
/// join and pivot cases build their own tables, and the shape they refuse was quadratic at sizes a
/// test could not afford to reach with `build`'s twenty thousand rows: the pre-phase join at that
/// size is four hundred million key comparisons.
let private scalingAt (label: string) (lo: int) (hi: int) (mk: int -> (unit -> unit)) : float =
    let fLo = mk lo
    let fHi = mk hi
    // The LARGE size first: its runs promote the evaluator's code to the JIT's optimised tier before
    // the small leg is timed. Timed small-first, the small leg ran partly unoptimised and its inflated
    // figure deflated the ratio — the pre-phase join scored 126 that way rather than a quadratic's
    // four hundred, too close to the bound to discriminate.
    let b = bestMs 5 fHi
    let a = bestMs 5 fLo
    let r = if a <= 0.0 then infinity else b / a
    printfn "  [scaling] %-28s %7.2f ms @ %d -> %8.2f ms @ %d   ratio %6.2f" label a lo b hi r
    r

/// The join and pivot cases' two sizes: the same twenty-fold step as `small` to `large`, so the same
/// `ratioBound` applies, at a quarter of the rows.
let private joinSmall = 250
let private joinLarge = 5_000

// `testSequenced`, not `testList` alone: every case here measures the clock, and Expecto runs a
// suite in parallel by default, so an unsequenced timing family measures whatever else the runner
// happened to schedule beside it. That is not merely noise — it is noise that grows with the
// machine's core count, which is the one axis a gate must not be sensitive to.
[<Tests>]
let scalingTests =
    testSequenced
    <| testList
        "Scaling"
        [ testCase "the reference evaluator is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "DataFrame.evalPipeline" (fun t -> DataFrame.evalPipeline pipeline t |> ok |> ignore)

              Expect.isLessThan
                  r
                  ratioBound
                  "ten times the rows must not cost thirty times the time — a per-row list walk does"

          testCase "Delta.diff is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "Delta.diff" (fun t -> Delta.diff idw t (editOne t) |> ok |> ignore)

              Expect.isLessThan r ratioBound "the diff reads every row's cells — once each, not once per row"

          testCase "the incremental refresh is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "Incremental.refreshOn" (fun t ->
                      let after = editOne t
                      let state = ok (Incremental.primeOn idw pipeline t)
                      let delta = ok (Delta.diff idw t after)
                      Incremental.refreshOn idw pipeline state delta after |> ok |> ignore)

              Expect.isLessThan r ratioBound "the refresh inherits the source scan — it must inherit a linear one"

          testCase "a restricted refresh beats the full evaluation on the clock"
          <| fun _ ->
              // The claim the seam exists to make, and the one the footprint instrument cannot
              // state: evaluating one row expression instead of twenty thousand must SHOW as time.
              // The priming and the diff are excluded deliberately — they are what a caller pays
              // once and on the change respectively, not what the refresh itself costs.
              //
              // Measured on the COSTLY pipeline. That is the phase's finding rather than a
              // convenience: with a one-comparison predicate the seam is still slower than the
              // full evaluation after the quadratic is gone, because its per-row bookkeeping
              // outweighs the row expression it avoids. The trivial-pipeline figure is measured
              // and printed below beside this one, so the gate carries the counter-example it
              // deliberately does not assert.
              let compare (label: string) (p: Transform list) =
                  let before = build large
                  let after = editOne before
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)

                  // The answers must agree before their costs are worth comparing: a refresh that
                  // returned something else would be cheap for an uninteresting reason.
                  let refreshed = ok (Incremental.refreshOn idw p state delta after)
                  Expect.equal (Ok(Incremental.result refreshed)) (DataFrame.evalPipeline p after) "refresh = reference"

                  let refreshMs =
                      bestMs 5 (fun () -> Incremental.refreshOn idw p state delta after |> ok |> ignore)

                  let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)

                  printfn "  [scaling] %-28s refresh %7.2f ms vs full %7.2f ms @ %d" label refreshMs fullMs large

                  refreshMs, fullMs

              let trivialRefresh, trivialFull = compare "one-comparison predicate" pipeline
              let costlyRefresh, costlyFull = compare "16-level row expression" costlyPipeline

              // Phase 208 asserted a WIN here, and the change was the point of that phase. Phase 206
              // measured this case losing 3.4x and printed it unasserted; the cause was the seam's
              // own per-source-row bookkeeping, which did not shrink when the row expression did.
              // Measured on the pre-208 tree at 20,000 rows: refresh 72.3 ms vs full 21.1 ms — red.
              // Measured after: 13.0 ms vs 26.3 ms. Phase 265 made the full evaluation faster rather
              // than the refresh slower, and the case is now a bounded LOSS: see
              // `cheapRefreshLossBound` for the figures and what the bound still refuses.
              Expect.isLessThan
                  trivialRefresh
                  (cheapRefreshLossBound * trivialFull)
                  "ONE COMPARISON per row: the refresh must stay within the bounded loss to the full evaluation — the pre-208 bookkeeping would lose by about ten times"

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "one edited row of twenty thousand must cost less than re-evaluating all of them"

          // ================= Phase 207 — the top-N board =================

          testCase "the top-N step itself is a single pass, and the obvious shape is not"
          <| fun _ ->
              // The go-red half of the claim below, and the reason it is a finding rather than an
              // assertion of the status quo: BOTH shapes are measured on the same instrument over
              // the same two sizes, and the naive one is required to FAIL the bound the shipped one
              // passes. Without that, "the maintenance is linear" is a sentence no run can refute.
              //
              // The instrument is a COUNT of element visits, exact and clock-free — see the note on
              // the two models above for the two confounds that made the timed form report the
              // naive shape as passing.
              let visits (f: int ref -> int list -> int list) (n: int) =
                  let c = ref 0
                  f c [ 0 .. n - 1 ] |> ignore
                  float c.Value

              let ratioOf f = visits f large / visits f small

              let shipped = ratioOf (fun c -> keepPositional c 0 10)
              let naive = ratioOf (fun c -> keepByIndexLookup c 0 10)

              printfn
                  "  [scaling] %-28s positional %6.2f   index-lookup %8.2f   (bound %.0f, linear %.0f)"
                  "limit step (visits)"
                  shipped
                  naive
                  ratioBound
                  sizeRatio

              // The answers agree — a cost comparison between two functions that compute different
              // things is not a finding about cost.
              Expect.equal
                  (keepPositional (ref 0) 0 10 [ 0 .. large - 1 ])
                  (keepByIndexLookup (ref 0) 0 10 [ 0 .. large - 1 ])
                  "both shapes keep the same rows"

              Expect.equal
                  (visits (fun c -> keepPositional c 0 10) large)
                  (float large)
                  "the shipped shape visits each element exactly once — one pass, by count and not by inspection"

              Expect.isGreaterThan
                  naive
                  ratioBound
                  "the index-lookup shape is QUADRATIC — if this ever passes the bound, the bound has stopped discriminating and the case below proves nothing"

              Expect.equal
                  shipped
                  sizeRatio
                  "and the shipped shape is EXACTLY linear: twenty times the rows, twenty times the visits"

          testCase "a top-N refresh is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "Incremental top-N refresh" (fun t ->
                      let after = editOne t
                      let state = ok (Incremental.primeOn idw topNPipeline t)
                      let delta = ok (Delta.diff idw t after)
                      Incremental.refreshOn idw topNPipeline state delta after |> ok |> ignore)

              // It lands at about 46 against the bound of 100, which is the highest figure in this
              // family and is structurally explained rather than slack: the measurement primes as
              // well as refreshes, and a prime SORTS the whole frame, so the expectation here is
              // n log n and not n — 20,000 log 20,000 over 1,000 log 1,000 is 28.6 before the keyed
              // maps' own growth is counted. Do not tighten the bound to fit it; the separation
              // this family exists to make is from FOUR HUNDRED.
              Expect.isLessThan r ratioBound "admitting the limit must not add a third quadratic class to the walk"

          testCase "a top-N refresh beats the full evaluation on the clock"
          <| fun _ ->
              // The claim the admission was asked for: a top-10 board over a live table should not
              // re-sort and re-filter twenty thousand rows because one of them moved.
              //
              // It is asserted on the COSTLY pipeline for the reason Phase 206 measured and this
              // phase re-measured rather than inherited: the seam's per-row bookkeeping does not
              // shrink when the row expression does. The trivial-predicate figure is measured and
              // printed beside it, unasserted, so the gate carries its own counter-example — and on
              // THIS shape the two figures are worth reading together, because a top-N full
              // evaluation sorts the whole frame while the refresh merges into an order it already
              // holds, which is a saving `Filter > GroupBy` had no equivalent of.
              let compare (label: string) (p: Transform list) =
                  let before = build large
                  let after = editOne before
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)

                  let refreshed = ok (Incremental.refreshOn idw p state delta after)
                  Expect.equal (Ok(Incremental.result refreshed)) (DataFrame.evalPipeline p after) "refresh = reference"

                  Expect.equal (Table.rowCount (Incremental.result refreshed)) 10 "and it is a top-10 board"

                  let refreshMs =
                      bestMs 5 (fun () -> Incremental.refreshOn idw p state delta after |> ok |> ignore)

                  let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)

                  printfn "  [scaling] %-28s refresh %7.2f ms vs full %7.2f ms @ %d" label refreshMs fullMs large

                  refreshMs, fullMs

              let trivialRefresh, trivialFull = compare "top-N, one comparison" topNPipeline

              let costlyRefresh, costlyFull =
                  compare "top-N, 16-level expression" costlyTopNPipeline

              // Phase 208 — asserted then. Pre-208 at 20,000 rows: refresh 102.5 ms vs full 70.0 ms
              // — red. After: 25.8 ms vs 55.7 ms. The saving the top-N shape has over
              // `Filter > GroupBy` (a full evaluation re-sorts the whole frame) was never enough on
              // its own while the refresh paid for the table. Phase 267 made the full evaluation
              // faster rather than the refresh slower (57.1 ms vs 91.1 ms before it, 43.3 ms vs
              // 21.7 ms after), and the case is now a bounded LOSS: see `cheapRefreshLossBound`.
              // Phase 269 — the full evaluation is the fused top-n now: see `topNRefreshLossBound`.
              Expect.isLessThan
                  trivialRefresh
                  (topNRefreshLossBound * trivialFull)
                  "ONE COMPARISON per row: a top-10 refresh must stay within the bounded loss to the fused top-n over twenty thousand rows — the pre-208 seam would lose by about ten times"

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "a top-10 refresh over one edited row of twenty thousand must cost less than recomputing the board"

          // ================= Phase 202 — the steps after a maintained group-by =================

          testCase "a group-tail refresh is linear in the row count"
          <| fun _ ->
              // The obligation this phase inherits rather than chooses: Phase 206 cleared two
              // quadratic classes out of this seam and recorded a third it did not close, so a
              // widening that routes MORE pipelines onto the restricted path must not add a fourth.
              //
              // The tail's own work is bounded by the GROUP count, which `build` holds at seventeen
              // whatever the source size, so this case is measuring that the tail did not make the
              // source scan worse — which is the only way it could go quadratic.
              let _, _, r =
                  scaling "Incremental.refreshOn (tail)" (fun t ->
                      let after = editOne t
                      let state = ok (Incremental.primeOn idw groupTailPipeline t)
                      let delta = ok (Delta.diff idw t after)
                      Incremental.refreshOn idw groupTailPipeline state delta after |> ok |> ignore)

              Expect.isLessThan r ratioBound "the group tail must not put a third quadratic class back"

          testCase "a group-tail refresh, measured against the full evaluation"
          <| fun _ ->
              // Measured and reported UNFLATTERINGLY, on the terms Phase 206 set: the trivial
              // predicate is the shape where this seam LOSES, because its per-source-row
              // string-keyed bookkeeping does not shrink when the row expression does. That figure
              // is printed and NOT asserted on — asserting it would be asserting a claim the
              // measurement does not support, and hiding it would be worse.
              //
              // What IS asserted is the costly shape, which is the claim the seam actually makes.
              let compare (label: string) (p: Transform list) =
                  let before = build large
                  let after = editOne before
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)

                  let refreshed = ok (Incremental.refreshOn idw p state delta after)
                  Expect.equal (Ok(Incremental.result refreshed)) (DataFrame.evalPipeline p after) "refresh = reference"

                  match (Incremental.footprint refreshed).Recompute with
                  | GroupsRecomputed _ -> ()
                  | other -> failtestf "%s: expected a maintained-group refresh, got %A" label other

                  let refreshMs =
                      bestMs 5 (fun () -> Incremental.refreshOn idw p state delta after |> ok |> ignore)

                  let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)

                  printfn "  [scaling] %-28s refresh %7.2f ms vs full %7.2f ms @ %d" label refreshMs fullMs large

                  refreshMs, fullMs

              let trivialRefresh, trivialFull =
                  compare "group-tail, one comparison" groupTailPipeline

              let costlyRefresh, costlyFull =
                  compare "group-tail, 16-level expression" costlyGroupTailPipeline

              // Phase 208 asserted a win here, and this was the thinnest of the three margins, so the
              // measured figures are recorded rather than left to be re-derived. Pre-208 at 20,000
              // rows: refresh 69.8 ms vs full 19.7 ms — red, losing 3.5x. After: 10.4 ms vs 16.7 ms,
              // winning 1.6x. The estimator is `bestMs`'s MINIMUM of five, so measurement noise here
              // is strictly additive and no sample comes in under the true cost. Phase 265 made the
              // full evaluation faster and the case a bounded LOSS — `cheapRefreshLossBound` has the
              // figures (BenchmarkDotNet after it: refresh 11.0 ms vs full 7.8 ms).
              Expect.isLessThan
                  trivialRefresh
                  (cheapRefreshLossBound * trivialFull)
                  "ONE COMPARISON per row: a Having over a live table must stay within the bounded loss to re-grouping twenty thousand rows — the pre-208 seam would lose by about ten times"

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "a Having over one edited row of twenty thousand must cost less than recomputing it"

          // ================= Phase 263 — resolved column indices =================

          testCase "a step's cost does not depend on WHICH column it names"
          <| fun _ ->
              // The evaluator used to look a `Col` up by NAME on every row it evaluated, then walk
              // the row list to that index — and the sort comparator did both for every key on
              // every comparison. Each is linear in the column's POSITION, so on a wide table the
              // same pipeline cost more the further right the column it named sat. Phase 263
              // resolves each step's names to indices once and holds rows as arrays, so the
              // position is a single load. This case states that as a ratio between the SAME
              // pipeline over the first column and over the last one of a wide table: every other
              // cost (the transposes, the filter's arithmetic, the sort's comparisons) is identical
              // between the two, so the ratio isolates what the name lookup costs.
              //
              // Measured on this table in a Release build, the pre-263 evaluator against this one:
              // before, first 67.5 ms and last 264.0 ms, ratio 3.9 (red); after, first 40.6 ms and
              // last 34.5 ms, ratio 0.85. The bound of 2 sits about twice below the first figure and
              // twice above the second.
              let width = 200
              let rows = 2_000
              let names = [ for c in 0 .. width - 1 -> "c" + string c ]

              let wide: Table =
                  { Schema = names |> List.map (fun n -> n, IntType)
                    Columns =
                      names
                      |> List.map (fun n ->
                          Column.create n IntType [ for i in 0 .. rows - 1 -> Int((i * 7919) % rows) ]) }

              // A row expression that names the column 129 times (the costly cases' chain, sixty-four
              // levels deep and rebuilt over `name`) and a sort keyed on it: the two places the lookup
              // was paid per row and per comparison. Each level adds one, so every row passes. The
              // depth is what makes the lookup, rather than the transposes, the measured term.
              let over (name: string) : Transform list =
                  let rec nest n e =
                      if n = 0 then
                          e
                      else
                          nest
                              (n - 1)
                              (Binary(
                                  Sub,
                                  Binary(Add, e, Binary(Add, Col name, Lit(Int 2))),
                                  Binary(Add, Col name, Lit(Int 1))
                              ))

                  [ Filter(Binary(Ge, nest 64 (Col name), Lit(Int -1)))
                    Transform.sortBy [ name, Asc ] ]

              let first = List.head names
              let last = List.last names

              // Same answer shape either way — the case is about cost, but a pipeline that errored
              // out early would flatter whichever side it hit.
              for name in [ first; last ] do
                  Expect.equal
                      (Table.rowCount (ok (DataFrame.evalPipeline (over name) wide)))
                      rows
                      "the pipeline keeps every row"

              let firstMs =
                  bestMs 5 (fun () -> DataFrame.evalPipeline (over first) wide |> ok |> ignore)

              let lastMs =
                  bestMs 5 (fun () -> DataFrame.evalPipeline (over last) wide |> ok |> ignore)

              let r = if firstMs <= 0.0 then infinity else lastMs / firstMs

              printfn
                  "  [scaling] %-28s first %7.2f ms vs last %7.2f ms @ %d x %d   ratio %6.2f"
                  "column position"
                  firstMs
                  lastMs
                  rows
                  width
                  r

              Expect.isLessThan
                  r
                  2.0
                  "naming the last of two hundred columns must not cost several times naming the first"

          // ================= Phase 264 — the hash join and the one-pass pivot =================

          testCase "a join is linear in the row count"
          <| fun _ ->
              // Two tables of `n` rows on one integer key, overlapping by half, joined Outer: half the
              // left rows match one right row each, the other half match none, and half the right
              // rows are unmatched — so both the probe and the right-only pass are exercised and the
              // output stays linear (one and a half times `n` rows). The nested loop the hash join
              // replaced filtered the whole right frame per left row and then scanned the whole left
              // frame per right row: quadratic, whatever the output size.
              //
              // Measured in a Release build, the pre-264 evaluator against this one: before, 9.14 ms
              // at 250 rows and 3806 ms at 5,000, ratio 416.5 (red, a quadratic's signature); after,
              // 0.40 ms and 9.7 ms, ratio 24.5.
              let side (n: int) (offset: int) (payload: string) : Table =
                  { Schema = [ "k", IntType; payload, IntType ]
                    Columns =
                      [ Column.create "k" IntType [ for i in 0 .. n - 1 -> Int(i + offset) ]
                        Column.create payload IntType [ for i in 0 .. n - 1 -> Int i ] ] }

              let joinOf (n: int) =
                  let left = side n 0 "l"
                  let pipeline = [ Join(Embedded(side n (n / 2) "r"), [ "k", "k" ], Outer) ]
                  fun () -> DataFrame.evalPipeline pipeline left |> ok

              Expect.equal
                  (Table.rowCount (joinOf joinSmall ()))
                  (joinSmall + joinSmall / 2)
                  "matched, left-only and right-only rows all present"

              let r =
                  scalingAt "DataFrame join (outer)" joinSmall joinLarge (fun n -> joinOf n >> ignore)

              Expect.isLessThan r ratioBound "a join must not compare every left row with every right row"

          testCase "a pivot is linear in the row count"
          <| fun _ ->
              // `n` rows over `n / 10` index groups and ten on-values: the group count grows with
              // the table, which is exactly where the per-pair scan the one-pass pivot replaced was
              // quadratic — it filtered all `n` rows for each of the (n / 10) x 10 pairs.
              //
              // Measured in a Release build, the pre-264 evaluator against this one: before, 2.00 ms
              // at 250 rows and 768 ms at 5,000, ratio 384.8 (red); after, 0.26 ms and 4.4 ms, ratio
              // about 17.
              let src (n: int) : Table =
                  { Schema = [ "g", IntType; "o", StringType; "v", IntType ]
                    Columns =
                      [ Column.create "g" IntType [ for i in 0 .. n - 1 -> Int(i % (n / 10)) ]
                        Column.create "o" StringType [ for i in 0 .. n - 1 -> Str("o" + string (i % 10)) ]
                        Column.create "v" IntType [ for i in 0 .. n - 1 -> Int i ] ] }

              let pivotOf (n: int) =
                  let t = src n

                  let pipeline =
                      [ Pivot
                            { Index = [ "g" ]
                              On = "o"
                              Values = "v"
                              Agg = Sum } ]

                  fun () -> DataFrame.evalPipeline pipeline t |> ok

              Expect.equal (Table.rowCount (pivotOf joinSmall ())) (joinSmall / 10) "one row per index group"

              let r =
                  scalingAt "DataFrame pivot" joinSmall joinLarge (fun n -> pivotOf n >> ignore)

              Expect.isLessThan r ratioBound "a pivot must not scan the frame once per (group, on-value) pair"

          // ================= Phase 265 — grouping and distinct on one cell comparer =================

          testCase "a group-by over n distinct keys is linear in the row count"
          <| fun _ ->
              // Every row its own group: the high-cardinality end, where the partition's own cost is
              // the whole cost. The pre-265 partition minted two strings per key cell and inserted the
              // token list into a persistent map (n log n, each comparison walking two string lists);
              // the comparer keys a hash table on the key cells themselves.
              //
              // Measured in a Release build, the pre-265 evaluator against this one: before, 1.04 ms
              // at 1,000 rows and 60.7 ms at 20,000, ratio 58.5; after, about 0.27 ms and 16 to 19 ms,
              // ratio 60 to 73 (a Debug build, the gate's, 46 to 61). The shape was already n log n
              // and passes either way, so this case is a guard rather than a discriminator of the
              // change. The ratio sits ABOVE a pure linear 20 for a reason worth knowing: with every
              // row its own group, the per-group output and aggregate garbage outgrows the young
              // generation between the two sizes and is promoted. Run with a young generation large
              // enough to hold it, the same build costs about 360 ns a row at 5,000 and at 20,000
              // rows alike, and 20,000 to 40,000 rows doubles the time: linear, with a collector
              // constant that only the larger leg pays.
              let src (n: int) : Table =
                  { Schema = [ "k", StringType; "v", IntType ]
                    Columns =
                      [ Column.create "k" StringType [ for i in 0 .. n - 1 -> Str("k" + string i) ]
                        Column.create "v" IntType [ for i in 0 .. n - 1 -> Int(i % 100) ] ] }

              let groupOf (n: int) =
                  let t = src n

                  let pipeline =
                      [ GroupBy([ "k" ], [ { Name = "s"; Fn = Sum; Of = "v" }; { Name = "n"; Fn = Count; Of = "v" } ]) ]

                  fun () -> DataFrame.evalPipeline pipeline t |> ok

              Expect.equal (Table.rowCount (groupOf small ())) small "one row per distinct key"

              let r =
                  scalingAt "DataFrame group-by (n keys)" small large (fun n -> groupOf n >> ignore)

              Expect.isLessThan r ratioBound "a group-by must stay linear or n-log-n in its key count"

          testCase "a distinct is linear in the row count"
          <| fun _ ->
              // Every distinct row twice, over a string and an integer column, so half the rows are
              // found already seen and half are new: both halves of the membership test are timed.
              //
              // Measured in a Release build, the pre-265 evaluator against this one: before, 0.44 ms
              // at 1,000 rows and 19.7 ms at 20,000, ratio 45.0 (a persistent set of token lists);
              // after, 0.07 ms and 1.6 ms, ratio about 23.
              let src (n: int) : Table =
                  { Schema = [ "k", StringType; "v", IntType ]
                    Columns =
                      [ Column.create "k" StringType [ for i in 0 .. n - 1 -> Str("k" + string (i % (n / 2))) ]
                        Column.create "v" IntType [ for i in 0 .. n - 1 -> Int((i % (n / 2)) % 100) ] ] }

              let distinctOf (n: int) =
                  let t = src n
                  fun () -> DataFrame.evalPipeline [ Distinct ] t |> ok

              Expect.equal (Table.rowCount (distinctOf small ())) (small / 2) "each distinct row kept once"

              let r = scalingAt "DataFrame distinct" small large (fun n -> distinctOf n >> ignore)

              Expect.isLessThan r ratioBound "a distinct must stay linear or n-log-n in its row count"

          // ================= Phase 272 — the table-fed caller's other half =================

          testCase "Delta.diff costs what keying the two tables costs, at either size"
          <| fun _ ->
              // A table-fed caller (the everyday one: a fresh `Table` per tick) pays `Delta.diff`
              // against the prior source and THEN the refresh, so the diff is half of what the seam
              // costs it. Until Phase 272 that half was the larger one by far: see `diffFloorBound`.
              //
              // The instrument is the diff's own FLOOR rather than the evaluator, on purpose. A diff
              // by identity must ask the witness for every row's key in both tables — `KeyString` is
              // the only thing that can say what a key is — so minting those strings is work no
              // implementation of this signature avoids. Held against that, the bound says "the diff
              // costs the keying plus a constant", which is a statement about the diff alone: it does
              // not move when the evaluator gets faster, so it is not a threshold a later phase has
              // to loosen for someone else's improvement.
              let keying (t: Table) =
                  let keyAt = idw.KeyOf t
                  let n = Table.rowCount t
                  let keys: string[] = Array.zeroCreate n

                  for i in 0 .. n - 1 do
                      match keyAt i with
                      | Some k -> keys[i] <- idw.KeyString k
                      | None -> ()

                  keys

              // The LARGE size first, for the reason `scalingAt` gives: its runs promote the code to
              // the optimised tier before the small leg is timed.
              //
              // Phase 273 — every sample diffs FRESH table objects. `Delta.diff` remembers the keys it
              // minted for a table and reads them back when it meets that very object again, so
              // re-diffing the same two tables would time a diff that mints nothing and hold it
              // against a floor it no longer pays. A fresh record over the same columns is what a
              // caller that has never keyed either table hands in.
              let fresh (t: Table) : Table = { t with Columns = t.Columns }

              for n in [ large; small ] do
                  let before = build n
                  let after = editOne before

                  let diffMs =
                      bestMs 5 (fun () -> Delta.diff idw (fresh before) (fresh after) |> ok |> ignore)

                  let floorMs =
                      bestMs 5 (fun () ->
                          keying before |> ignore
                          keying after |> ignore)

                  let fullMs =
                      bestMs 5 (fun () -> DataFrame.evalPipeline pipeline after |> ok |> ignore)

                  printfn
                      "  [scaling] %-28s diff %7.2f ms vs keying %7.2f ms (x%.2f) vs full %7.2f ms @ %d"
                      "Delta.diff against its floor"
                      diffMs
                      floorMs
                      (diffMs / floorMs)
                      fullMs
                      n

                  Expect.isLessThan
                      diffMs
                      (diffFloorBound * floorMs)
                      "the diff must cost the keying of both tables and a constant factor more, not a token string per cell"

          testCase "the table-fed tick, measured against the full evaluation it would replace"
          <| fun _ ->
              // What a table-fed caller pays per tick — `Delta.diff` plus the refresh — against the
              // full evaluation of the new source, printed for the three pipelines above and NOT
              // asserted. Phase 272 set out to assert this at 1.5 times and measured why it cannot be
              // from this entry point. In a Release build the diff's floor alone — the keying above,
              // plus the one hash pass that proves the keys unique — is one and a half to two and a
              // half full evaluations of a one-comparison pipeline at 20,000 and 100,000 rows, before
              // the refresh has done anything; and the refresh itself walks one `Work` record per
              // source row, which the vectorised evaluator does not. (This Debug build flatters the
              // diff, whose cost is allocation, against an evaluator whose loops are unoptimised, so
              // read the Release tables in the doc rather than these lines for the verdict.) The
              // figures are printed so the gate log carries the counter-example beside the claim the
              // family does make; see "What it costs on the clock" in docs/incremental-evaluation.md.
              for label, p in
                  [ "tick: filter > groupBy", pipeline
                    "tick: filter > sort > limit", topNPipeline
                    "tick: group tail", groupTailPipeline ] do
                  let before = build large
                  let after = editOne before
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before after)

                  // Phase 273 — the tick a caller pays: the prior source is the one the state last
                  // evaluated (its keys were minted when it was, and are read back), the new one is
                  // a table nothing has keyed yet — a fresh object per sample, so no sample reads the
                  // previous sample's keys for it.
                  let tickMs =
                      bestMs 5 (fun () ->
                          let a = { after with Columns = after.Columns }
                          let d = ok (Delta.diff idw before a)
                          Incremental.refreshOn idw p state d a |> ok |> ignore)

                  let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)

                  Expect.equal
                      (Incremental.refreshOn idw p state delta after |> Result.map Incremental.result)
                      (DataFrame.evalPipeline p after)
                      "the tick answers what the reference answers"

                  printfn
                      "  [scaling] %-28s diff+refresh %7.2f ms vs full %7.2f ms (x%.2f) @ %d"
                      label
                      tickMs
                      fullMs
                      (tickMs / fullMs)
                      large ]
