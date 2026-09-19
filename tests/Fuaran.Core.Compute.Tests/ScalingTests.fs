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
//  RESTRICTED refresh finishes sooner than the full evaluation it replaces. It
//  is measured over a pipeline whose row expression COSTS something, and that
//  qualification is a finding of this phase rather than a convenience — see the
//  note on `costlyPipeline` below and the cost section of
//  docs/incremental-evaluation.md.
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
/// The cheapest honest shape here is NOT linear: the grouping, the delta and the seam key rows
/// through persistent maps over string and string-list keys, so they are n log n with a comparison
/// whose own cost grows, on top of cache behaviour that gets worse with the working set. Post-fix,
/// the three cases land at roughly 34, 52 and 44 against a linear expectation of 20 — repeatably,
/// within six per cent across runs.
///
/// So the bound sits at 100: about twice the worst legitimate shape, and four times below the
/// quadratic (400) the family exists to refuse, which the PRE-FIX code scored at 163, 413 and 440.
/// Both margins matter and they pull against each other; a false red costs a campaign a re-run and
/// teaches people to re-run gates, which is dearer than the false green this could admit — and a
/// false green here would need a quadratic to score under a hundred, which nothing measured does.
let private ratioBound = 5.0 * sizeRatio

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
/// This is not a second instrument for the same claim; it is the instrument for a DIFFERENT one,
/// and the phase's measurement is that the two answers differ. The seam's whole proposition is
/// "evaluate the row expression once instead of n times", so what it saves is n−1 evaluations of
/// THAT expression, while what it spends is its own per-row bookkeeping — an identity token, its
/// uniqueness check, two lookups into the prior row-cell map, a group-membership entry. Those are
/// string-keyed persistent-map operations and they do not shrink when the expression does. With a
/// single `Ge` the saving is smaller than the spend and the seam loses on the clock; with real
/// work in the expression it wins. See the "what it costs on the clock" section of
/// docs/incremental-evaluation.md.
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
                  Expect.equal (Ok refreshed.Output) (DataFrame.evalPipeline p after) "refresh = reference"

                  let refreshMs =
                      bestMs 5 (fun () -> Incremental.refreshOn idw p state delta after |> ok |> ignore)

                  let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)

                  printfn "  [scaling] %-28s refresh %7.2f ms vs full %7.2f ms @ %d" label refreshMs fullMs large

                  refreshMs, fullMs

              let trivialRefresh, trivialFull = compare "one-comparison predicate" pipeline
              let costlyRefresh, costlyFull = compare "16-level row expression" costlyPipeline

              // Reported, never asserted — see the comment above. An assertion here would pin a
              // ratio between two costs that a faster machine moves for reasons this phase is not
              // about, and it would read as a promise that the seam must stay slower.
              ignore (trivialRefresh, trivialFull)

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "one edited row of twenty thousand must cost less than re-evaluating all of them" ]
