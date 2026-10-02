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
//  tail) became a bounded LOSS rather than a win, under a loss bound that
//  Phases 269 and 270 raised again. The costly-expression cases still assert
//  the win.
//
//  Phase 272 added the table-fed caller's other half: `Delta.diff`, which such
//  a caller runs before every refresh and which cost up to thirty-six times the
//  evaluation it fed. It is held against its own floor (the keying of both
//  tables). Phase 274 rewrote the refresh's bookkeeping over columns and holds
//  the whole tick — diff plus refresh — within `tickBound` of the full
//  evaluation, which retired both loss bounds.
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

/// Phase 274 — what a table-fed caller's tick (`Delta.diff` of the prior source against the new one,
/// then the refresh) may cost against the full evaluation of the new source it replaces: one and a
/// half times, at every size this family measures.
///
/// It replaces two loss bounds, and neither survives as a threshold of its own. `cheapRefreshLossBound`
/// (Phase 265 at three, raised to four at Phase 270) and `topNRefreshLossBound` (Phase 269, five) held
/// the REFRESH of the one-comparison pipelines within a stated loss to the full evaluation, and were
/// raised each time the evaluator got faster and the refresh did not, because the refresh walked one
/// `Work` record per source row, copied a row per `Derive` and transposed the source in and the result
/// out. Phase 274 rewrote that bookkeeping over columns, and a tick is the refresh plus the diff, so a
/// tick within 1.5 is a refresh within 1.5 — a tighter statement than either bound made, and one that
/// no longer moves with the evaluator: it asks the incremental seam to cost no more than re-running,
/// which is the seam's whole claim.
///
/// Measured on the pre-phase tree, in this family's Debug build: red at every size (see the case).
/// After it the family reads about 1.0 at 20,000 rows. The Release tables for every Phase 262
/// corpus node, on .NET and on node, are in docs/incremental-evaluation.md ("Columnar refresh
/// bookkeeping"); they say where the bound is NOT met in a Release build on .NET, and why that is the
/// diff's keying floor rather than the refresh. That finding is the operator's to decide, not this
/// number's to absorb: do not raise it.
///
/// Phase 283 keyed the diff by the typed id, and the bound is now met on every corpus node in both
/// builds; the clock leg holds all ten at it ("the table-fed tick on every corpus node").
///
/// Operator ruling 2026-09-28: the clock leg runs a RELEASE build, and there the bound is 1.6, not
/// 1.5. Release readings on a machine another gate was loading reached 1.53 (`filter > sort > limit`
/// at 1,000 rows, driver run) and 1.55 / 1.56 (`lines` at 20,000, `window CumulSum` at 1,000, Phase
/// 284's first gate), red on all three attempts in the second case; quiet Release runs read at or
/// under 1.3. The seam's target is still 1.5. What absorbs machine load belongs to the clock leg, not
/// to this number (Phase 285's calibration).
///
/// Operator ruling 2026-10-02: the condition is `tick <= tickBound * full + tickFloorMs ()`. The
/// ratio bounds the tick's PER-ROW cost; `tickFloorMs` (below) bounds its PER-CALL cost, which no
/// optimisation can drive to zero. A ratio alone tends to an allowance of nothing as the full
/// evaluation gets faster, while a one-row tick always pays a call's worth of fixed work (the diff's
/// setup, the refresh's step walk), so every speed-up of the evaluator tightened the bound on the
/// seam until a fixed cost failed it. The ruling came when Phase 327 halved the full evaluation's
/// boundary cost and the tick, which never crosses that boundary, did not move. The floor is under
/// three percent of the allowance at 100,000 rows, so the ratio still decides there. Any further
/// move of either number is the operator's act.
let private tickBound = 1.6

/// Phase 327 — the per-call floor of the tick condition, in units of the clock leg's calibration
/// baseline (`Calibration.baselineMs`), so it scales with the machine as the saturation factor does.
/// Measured, not chosen: the one-row tick of every corpus node and every `Scaling` tick pipeline at
/// 10 rows, where the per-row work is ten rows' worth and what remains is the call, Release, three
/// runs on an i7-8650U (baseline 5.2 to 5.7 ms). The worst reading was 0.0157 units (the group tail,
/// 0.085 ms); the next 0.0140 (`filter > groupBy`, 0.072 ms); every corpus node read at most 0.0110.
/// At 100 rows the join, the pivot and `window CumulSum` already grow with the row count (0.025 to
/// 0.043 units, four to six times their 10-row tick), which is per-row cost the ratio bounds, so they
/// are not the floor. Twice the worst per-call reading, rounded: 0.03. The figures are in
/// docs/incremental-evaluation.md ("The tick bound gains a per-call floor").
let private tickFloorFactor = 0.03

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
///
/// **Phase 282 moved the bound from the clock to the allocator, and tightened it from three to two.**
/// The claim is about work — the keying plus a constant, not a token string per cell — so it is held
/// on the bytes the diff allocates against the bytes the keying allocates, on the calling thread,
/// where no other process can reach it. Measured: 1.50 times at 20,000 rows and 1.52 at 1,000, the
/// same on every run, where the clock read 1.8 to 2.1 and 4.1 under an injected token-per-cell
/// regression that allocates 3.23 times the floor (red here). The pre-272 row-token diff minted
/// strings per CELL, several times the keying's one per row. The diff's key count is asserted
/// beside it, exactly: one per row per table, so Phase 273's regression (a second minting, which
/// the clock scored at 2.14 against its bound of 3, green) is red too.
let private diffFloorAllocBound = 2.0

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
///
/// Phase 282 made it public so every clock figure in the suite uses the same estimator: `PlanTests`
/// timed with a MEAN of five, which carried exactly the noise this function was written to remove.
let bestMs (runs: int) (f: unit -> unit) : float =
    f ()

    [ for _ in 1..runs ->
          System.GC.Collect()
          System.GC.WaitForPendingFinalizers()
          let sw = Stopwatch.StartNew()
          f ()
          sw.Stop()
          sw.Elapsed.TotalMilliseconds ]
    |> List.min

/// Phase 282 — the bytes one call of `f` allocates on the calling thread: a COUNT of work, exact and
/// indifferent to what else the machine is doing.
///
/// Three calls first, so the JIT has settled the code: a method still running in its first tier can
/// allocate what the optimised tier keeps on the stack, and the figure should not depend on which
/// tier happened to run. Then the minimum of three samples, for the same reason `bestMs` takes one —
/// anything else on the thread can only ADD bytes. `GC.GetAllocatedBytesForCurrentThread` counts the
/// calling thread alone, so neither another test nor another process can move it; Phase 273 used the
/// same counter to settle a dispute its clock could not.
let allocatedBytes (f: unit -> unit) : int64 =
    f ()
    f ()
    f ()

    [ for _ in 1..3 ->
          let before = System.GC.GetAllocatedBytesForCurrentThread()
          f ()
          System.GC.GetAllocatedBytesForCurrentThread() - before ]
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

// ---------------------------------------------------------------------------
//  Phase 283 — the Phase 262 corpus, for the tick family.
//
//  The ten nodes of `benchmarks/Fuaran.Core.Compute.Benchmarks/Corpus.fs` as the
//  Phase 272 / 274 probe measured them: an input, the pipeline, the identity
//  column and a one-row edit. Copied rather than referenced, because the test
//  project does not (and should not) depend on the benchmark project; the doc's
//  tables (docs/incremental-evaluation.md) are over exactly these shapes.
// ---------------------------------------------------------------------------

/// One corpus node: the source at `n` rows, its identity column, the parameter environment, the
/// pipeline, and the one-row edit a tick answers.
type private CorpusNode =
    { Name: string
      IdCol: string
      Env: Map<string, Cell>
      Mk: int -> Table
      EditCol: string
      EditTo: Cell
      Pipe: int -> Transform list }

let private corpusRegions = [| "north"; "south"; "east"; "west"; "central" |]

let private corpusOrders n : Table =
    { Schema = [ "id", IntType; "region", StringType; "qty", IntType; "price", FloatType ]
      Columns =
        [ Column.create "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          Column.create "region" StringType [ for i in 0 .. n - 1 -> Str corpusRegions[(i * 3 + i / 7) % 5] ]
          Column.create "qty" IntType [ for i in 0 .. n - 1 -> Int(1 + (i * 7 + i / 3) % 20) ]
          Column.create "price" FloatType [ for i in 0 .. n - 1 -> Float(float (4 + (i * 13) % 397) * 0.25) ] ] }

let private corpusAmount = Derive("amount", Binary(Mul, Col "qty", Col "price"))

let private corpusTwoAggs =
    GroupBy([ "grp" ], [ { Name = "n"; Fn = Count; Of = "a" }; { Name = "s"; Fn = Sum; Of = "b" } ])

let private corpusEveryRow = Filter(Binary(Ge, Col "a", Lit(Int -10)))

let private corpusSheetEnv = Map.ofList [ "threshold", Float 500.0 ]

let private corpusNodes: CorpusNode list =
    let joinLeft n : Table =
        { Schema = [ "k", IntType; "a", IntType ]
          Columns =
            [ Column.create "k" IntType [ for i in 0 .. n - 1 -> Int((i * 7919) % n) ]
              Column.create "a" IntType [ for i in 0 .. n - 1 -> Int i ] ] }

    let joinRight n : Table =
        { Schema = [ "rk", IntType; "b", IntType ]
          Columns =
            [ Column.create "rk" IntType [ for i in 0 .. n - 1 -> Int((i * 104729) % n) ]
              Column.create "b" IntType [ for i in 0 .. n - 1 -> Int i ] ] }

    let groupTable n : Table =
        let keys = max 1 (n / 2)

        { Schema = [ "rid", IntType; "key", StringType; "v", IntType ]
          Columns =
            [ Column.create "rid" IntType [ for i in 0 .. n - 1 -> Int i ]
              Column.create "key" StringType [ for i in 0 .. n - 1 -> Str("k" + string (i % keys)) ]
              Column.create "v" IntType [ for i in 0 .. n - 1 -> Int(i % 100) ] ] }

    let pivotTable n : Table =
        { Schema = [ "rid", IntType; "idx", StringType; "on", StringType; "v", FloatType ]
          Columns =
            [ Column.create "rid" IntType [ for i in 0 .. n - 1 -> Int i ]
              Column.create "idx" StringType [ for i in 0 .. n - 1 -> Str("i" + string (i % 100)) ]
              Column.create "on" StringType [ for i in 0 .. n - 1 -> Str("o" + string ((i / 100) % 50)) ]
              Column.create "v" FloatType [ for i in 0 .. n - 1 -> Float(float (i % 13) * 0.5) ] ] }

    let windowTable n : Table =
        { Schema = [ "seq", IntType; "v", IntType ]
          Columns =
            [ Column.create "seq" IntType [ for i in 0 .. n - 1 -> Int i ]
              Column.create "v" IntType [ for i in 0 .. n - 1 -> Int(i % 10) ] ] }

    let sortTable n : Table =
        { Schema = [ "k1", StringType; "k2", IntType ]
          Columns =
            [ Column.create "k1" StringType [ for i in 0 .. n - 1 -> Str("c" + string (i % 100)) ]
              Column.create "k2" IntType [ for i in 0 .. n - 1 -> Int((i * 7919) % n) ] ] }

    [ { Name = "lines"
        IdCol = "id"
        Env = corpusSheetEnv
        Mk = corpusOrders
        EditCol = "qty"
        EditTo = Int 99
        Pipe = fun _ -> [ corpusAmount; Derive("big", Binary(Ge, Col "amount", Param "threshold")) ] }
      { Name = "byRegion"
        IdCol = "id"
        Env = corpusSheetEnv
        Mk = corpusOrders
        EditCol = "qty"
        EditTo = Int 99
        Pipe =
          fun _ ->
              [ corpusAmount
                GroupBy(
                    [ "region" ],
                    [ { Name = "total"
                        Fn = Sum
                        Of = "amount" }
                      { Name = "n"
                        Fn = Count
                        Of = "amount" } ]
                ) ] }
      { Name = "filter > groupBy"
        IdCol = "id"
        Env = Map.empty
        Mk = build
        EditCol = "a"
        EditTo = Int -1
        Pipe = fun _ -> [ corpusEveryRow; corpusTwoAggs ] }
      { Name = "filter > sort > limit"
        IdCol = "id"
        Env = Map.empty
        Mk = build
        EditCol = "a"
        EditTo = Int -1
        Pipe = fun _ -> [ corpusEveryRow; Transform.sortBy [ "a", Desc ]; Transform.limit 10 0 ] }
      { Name = "filter > groupBy > filter"
        IdCol = "id"
        Env = Map.empty
        Mk = build
        EditCol = "a"
        EditTo = Int -1
        Pipe = fun _ -> [ corpusEveryRow; corpusTwoAggs; Filter(Binary(Gt, Col "n", Lit(Int 0))) ] }
      { Name = "inner join"
        IdCol = "k"
        Env = Map.empty
        Mk = joinLeft
        EditCol = "a"
        EditTo = Int -1
        Pipe = fun n -> [ Join(Embedded(joinRight n), [ "k", "rk" ], Inner) ] }
      { Name = "group-by high-card"
        IdCol = "rid"
        Env = Map.empty
        Mk = groupTable
        EditCol = "v"
        EditTo = Int 777
        Pipe =
          fun _ -> [ GroupBy([ "key" ], [ { Name = "s"; Fn = Sum; Of = "v" }; { Name = "n"; Fn = Count; Of = "v" } ]) ] }
      { Name = "pivot"
        IdCol = "rid"
        Env = Map.empty
        Mk = pivotTable
        EditCol = "v"
        EditTo = Float 99.5
        Pipe =
          fun _ ->
              [ Pivot
                    { Index = [ "idx" ]
                      On = "on"
                      Values = "v"
                      Agg = Sum } ] }
      { Name = "window CumulSum"
        IdCol = "seq"
        Env = Map.empty
        Mk = windowTable
        EditCol = "v"
        EditTo = Int 55
        Pipe =
          fun _ ->
              [ Window
                    { PartitionBy = []
                      OrderBy = [ "seq", Asc ]
                      Fn = CumulSum
                      Of = "v"
                      As = "cs" } ] }
      { Name = "sort two keys"
        IdCol = "k2"
        Env = Map.empty
        Mk = sortTable
        EditCol = "k1"
        EditTo = Str "c7"
        Pipe = fun _ -> [ Transform.sortBy [ "k1", Asc; "k2", Desc ] ] } ]

/// Set one cell — the corpus's one-row edit, as the probe made it.
let private setCell (colName: string) (row: int) (v: Cell) (t: Table) : Table =
    { t with
        Columns =
            t.Columns
            |> List.map (fun c ->
                if c.Name <> colName then
                    c
                else
                    { c with
                        Cells = c.Cells |> List.mapi (fun i x -> if i = row then v else x) }) }

/// Best of `runs` BATCHED samples, in ms per call: each sample repeats `f` until it spans at least
/// 20 ms, so a 1,000-row node whose one call takes a tenth of a millisecond is timed over a window
/// the clock can resolve rather than over one call's jitter. The ratio is taken within one run, as
/// every clock figure here is.
let private batchedMs (runs: int) (f: unit -> unit) : float =
    f ()
    f ()

    let sample (calls: int) =
        let sw = Stopwatch.StartNew()

        for _ in 1..calls do
            f ()

        sw.Stop()
        sw.Elapsed.TotalMilliseconds

    let rec calibrate (calls: int) =
        if calls >= 100_000 || sample calls >= 20.0 then
            calls
        else
            calibrate (calls * 2)

    let calls = calibrate 1

    [ for _ in 1..runs ->
          System.GC.Collect()
          System.GC.WaitForPendingFinalizers()
          sample calls / float calls ]
    |> List.min

// ---------------------------------------------------------------------------
//  Phase 282 — the clock leg.
//
//  Every case below `clockTests` asserts TIME, and none of them runs in the
//  main suite. `clockTests` carries no `[<Tests>]` attribute, so the default
//  run cannot discover it (exclusion by construction, not by a filter that
//  could match nothing); the test entry point runs it alone, in its own
//  process, when asked: `dotnet run --project tests/Fuaran.Core.Compute.Tests
//  -- --clock-leg`. `verify.ps1` runs that after the main suite.
//
//  Three things make the leg measure the code rather than the machine:
//    - nothing else runs in the process (the main suite's parallel lists used
//      to run beside `testSequenced`, which orders its own cases only);
//    - the process asks for above-normal scheduling priority, so ordinary
//      work elsewhere on the machine yields to it for the seconds it runs;
//    - a case fails only if it fails on each of THREE attempts. A regression
//      fails every time; a loaded window rarely fails three times running.
//      Every attempt is printed.
//  The leg counts the cases whose bodies actually ran and the entry point
//  fails it on fewer than `clockInventory`: a filter that matches nothing
//  passes vacuously, and one with the wrong separator has done that here.
// ---------------------------------------------------------------------------

/// How many cases the clock leg holds: the CLOCK rows of the inventory in docs/incremental-evaluation.md
/// ("The gate measures work, not the machine"). A literal, deliberately, and pinned against the list
/// by a main-suite case: the leg's run count is checked against THIS number, so a case dropped from
/// the list without the inventory moving is red in both places.
let clockInventory = 15

let mutable private clockRuns = 0

/// The number of clock cases whose bodies have run in this process.
let clockCasesRun () = clockRuns

/// How many COUNTED attempts a clock case gets. A timing assertion is red only if it is red on every
/// one. Since Phase 285 an attempt counts only when its window was not saturated (`Calibration`).
let private clockAttempts = 3

/// Phase 285 — the calibration workload that tells the clock leg when the machine is busy.
///
/// Three attempts inside one loaded window are three samples of the same window: they cannot tell
/// "slower code" from "busier machine". So the leg times a FIXED workload that touches no code under
/// test, immediately before and after every clock case's measurement (and, in the two long tick
/// cases, between cells), and compares those readings with the leg's own quiet baseline.
///
/// The workload is an integer multiply-xor chain: every step depends on the one before, so it runs
/// at exactly the rate the core it is scheduled on retires dependent integer work, and it allocates
/// and touches no memory at all — it cannot move the collector the cases are timed against. One run
/// is a fixed 2^22 steps (about 6 ms on the reference machine); a READING is the best of nine runs,
/// the same minimum-of-runs estimator `bestMs` and `batchedMs` use, so a reading moves with
/// sustained load and not with one preemption.
///
/// A memory walk was measured and REJECTED (doc, "The clock leg knows when the machine is busy"):
/// a pointer chase over a 4 MB ring read 0.67x to 1.8x its own baseline on a quiet machine, and ran
/// FASTER under an all-core burner than quiet (the burner holds the clock frequency up), so it
/// could neither stay below k when quiet nor rise above it under load. The integer chain read
/// within 1.05x of its baseline quiet and 1.17x above it under the burner.
module Calibration =
    let private steps = 1 <<< 22
    let private runsPerReading = 9

    let mutable private sink = 0L

    let private runOnce () : float =
        let t0 = Stopwatch.GetTimestamp()
        let mutable acc = 0x5851F42DL

        for s in 1..steps do
            acc <- (acc ^^^ int64 s) * 0x100000001B3L

        let t1 = Stopwatch.GetTimestamp()
        // Published, so the loop cannot be removed as dead code.
        sink <- sink ^^^ acc
        float (t1 - t0) * 1000.0 / float Stopwatch.Frequency

    /// One calibration reading, in ms: the best of `runsPerReading` runs of the fixed workload.
    let reading () : float =
        let mutable best = runOnce ()

        for _ in 2..runsPerReading do
            best <- min best (runOnce ())

        best

    /// How many readings the leg-start baseline takes the best of.
    let baselineRuns = 15

    /// The saturation factor k: a window is saturated when any of its readings exceeds k times the
    /// baseline, or its highest reading exceeds k times its lowest. Measured, not chosen — the quiet
    /// and loaded distributions it sits between are in docs/incremental-evaluation.md ("The clock
    /// leg knows when the machine is busy").
    let k = 1.25

    /// A case gives up after this many saturated windows; the leg's verdict is then "machine
    /// saturated, no verdict" for it (exit 3), never red and never green.
    let maxDiscarded = 5

    /// The leg-wide budget for discarded windows and their back-off, in seconds. Once the leg has
    /// spent this long on windows it threw away, a further saturated window ends its case at once:
    /// the gate queue's run time stays bounded on a machine that never quietens.
    let legBudgetSeconds = 600.0

    /// The back-off before retrying a saturated window: 2 s, doubling, at most 16 s.
    let backoffMs (discarded: int) : int =
        min 16_000 (2_000 * (1 <<< (discarded - 1)))

    let mutable private baseline = nan

    /// The leg's quiet baseline: measured on first use (the leg's entry point forces it before any
    /// case runs), the best of `baselineRuns` readings after a warm-up, and then FIXED for the leg.
    /// It is not lowered by a later, faster reading: on a laptop-class CPU the integer chain alone
    /// reads across a 1.6x range as the clock frequency moves with the leg's own work, and a baseline
    /// ratcheted down to the fastest reading ever seen judged every ordinary window saturated (the
    /// measurement is in the doc). The guard that matters to a ratio verdict is the bracket
    /// disagreement below, which a frequency change and a load change both trip.
    let baselineMs () : float =
        if System.Double.IsNaN baseline then
            reading () |> ignore
            baseline <- List.min [ for _ in 1..baselineRuns -> reading () ]

        baseline

    let mutable private window: float list option = None

    /// A reading taken inside the current window and added to it; outside a window it does nothing.
    let checkpoint () =
        match window with
        | Some rs -> window <- Some(reading () :: rs)
        | None -> ()

    /// Open a window: the bracket's first reading.
    let openWindow () =
        baselineMs () |> ignore
        window <- Some [ reading () ]

    /// Close the window: its readings, in order, and the baseline they are judged against.
    let closeWindow () : float list * float =
        let c = reading ()
        let rs = List.rev (c :: defaultArg window [])
        window <- None
        rs, baseline

    /// Saturated: a reading above k times the baseline, or two CONSECUTIVE readings disagreeing by
    /// more than k. Consecutive, not highest-against-lowest: in a window with checkpoints between
    /// cells each cell's measurement sits between two consecutive readings, and it is the load
    /// change across THAT bracket that can move the cell's ratio. A long window's slow drift from
    /// one cell to the next does not touch any one cell's tick-against-full comparison.
    let saturated (baselineMs: float) (readings: float list) : bool =
        List.exists (fun r -> r > k * baselineMs) readings
        || readings |> List.pairwise |> List.exists (fun (a, b) -> max a b > k * min a b)

/// What a clock case ended as (Phase 285): a verdict from an unsaturated window, or none.
type ClockVerdict =
    | ClockGreen
    | ClockRed
    | ClockSaturated
    | ClockErrored

/// One clock case's account: attempts counted, windows discarded as saturated, and its verdict.
type ClockOutcome =
    { Name: string
      Counted: int
      Discarded: int
      Verdict: ClockVerdict }

let private clockOutcomeLog = System.Collections.Generic.List<ClockOutcome>()

/// Every clock case's outcome, in the order the cases ran.
let clockOutcomes () : ClockOutcome list = List.ofSeq clockOutcomeLog

/// The calibration baseline as it stands (the leg's entry point forces and prints it at leg start).
let clockBaselineMs () : float = Calibration.baselineMs ()

/// The tick condition's per-call floor, in ms on this machine: `tickFloorFactor` calibration
/// baselines (operator ruling 2026-10-02; see `tickBound`).
let private tickFloorMs () : float =
    tickFloorFactor * Calibration.baselineMs ()

/// Raised by a clock case that stayed saturated past its budget: NOT an assertion failure, so it is
/// never read as a timing red, and the entry point turns it into the leg's distinct exit code.
exception MachineSaturated of string

let mutable private legDiscardedSeconds = 0.0

/// A clock case (Phase 282, Phase 285): `body` runs in a WINDOW bracketed by calibration readings.
///
/// - A saturated window is INCONCLUSIVE: logged with its readings, retried after a back-off, and
///   never counted as an attempt, whatever the body said — a green under load is no more a verdict
///   than a red, because load can deflate a ratio's denominator as easily as inflate its numerator.
/// - An unsaturated window counts. The case fails only when all `clockAttempts` counted attempts
///   are red.
/// - Past `Calibration.maxDiscarded` saturated windows (or the leg-wide budget), the case raises
///   `MachineSaturated`: no verdict, and the gate is not green.
///
/// Only an assertion failure is a timing verdict; anything else (an evaluation error) is a defect and
/// fails at once. A correctness assertion inside `body` is retried with it, harmlessly: it is
/// deterministic, so it fails every counted attempt.
let private clockCase (name: string) (body: unit -> unit) : Test =
    testCase name
    <| fun _ ->
        clockRuns <- clockRuns + 1

        let record counted discarded verdict =
            clockOutcomeLog.Add
                { Name = name
                  Counted = counted
                  Discarded = discarded
                  Verdict = verdict }

        let rec window (counted: int) (discarded: int) =
            printfn "  [clock] %s: attempt %d of %d" name (counted + 1) clockAttempts
            let started = Stopwatch.StartNew()
            Calibration.openWindow ()

            let result =
                try
                    body ()
                    Ok()
                with
                | :? AssertException as e -> Error e
                | _ ->
                    Calibration.closeWindow () |> ignore
                    record counted discarded ClockErrored
                    reraise ()

            let readings, b = Calibration.closeWindow ()
            let shown = readings |> List.map (sprintf "%.3f") |> String.concat ", "

            if Calibration.saturated b readings then
                let discarded = discarded + 1

                let said =
                    match result with
                    | Ok() -> "green"
                    | Error _ -> "red"

                printfn
                    "  [clock] %s: window SATURATED, discarded (it read %s; not counted) - calibration [%s] ms against baseline %.3f ms, k %.2f"
                    name
                    said
                    shown
                    b
                    Calibration.k

                if
                    discarded >= Calibration.maxDiscarded
                    || legDiscardedSeconds >= Calibration.legBudgetSeconds
                then
                    record counted discarded ClockSaturated

                    raise (
                        MachineSaturated(
                            sprintf
                                "machine saturated, no verdict: %s - %d window(s) discarded as saturated, %d counted attempt(s)"
                                name
                                discarded
                                counted
                        )
                    )

                let pause = Calibration.backoffMs discarded
                printfn "  [clock] %s: backing off %d ms before the next window" name pause
                System.Threading.Thread.Sleep pause

                legDiscardedSeconds <- legDiscardedSeconds + started.Elapsed.TotalSeconds + float pause / 1000.0

                window counted discarded
            else
                let counted = counted + 1
                printfn "  [clock] %s: window counted - calibration [%s] ms against baseline %.3f ms" name shown b

                match result with
                | Ok() ->
                    if counted > 1 || discarded > 0 then
                        printfn "  [clock] %s: green on attempt %d" name counted

                    record counted discarded ClockGreen
                | Error e when counted < clockAttempts ->
                    printfn
                        "  [clock] %s: attempt %d red: %s (calibration [%s] ms)"
                        name
                        counted
                        (e.Message.Trim())
                        shown

                    window counted discarded
                | Error e ->
                    record counted discarded ClockRed
                    raise e

        window 0 0

// `testSequenced`, not `testList` alone: every case here measures the clock, and Expecto runs a
// suite in parallel by default, so an unsequenced timing family measures whatever else the runner
// happened to schedule beside it. That is not merely noise — it is noise that grows with the
// machine's core count, which is the one axis a gate must not be sensitive to. Since Phase 282 the
// leg is also its own process, so there is nothing else in it to schedule.
let clockTests =
    testSequenced
    <| testList
        "Clock"
        [ clockCase "the reference evaluator is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "DataFrame.evalPipeline" (fun t -> DataFrame.evalPipeline pipeline t |> ok |> ignore)

              Expect.isLessThan
                  r
                  ratioBound
                  "ten times the rows must not cost thirty times the time — a per-row list walk does"

          clockCase "Delta.diff is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  // Phase 282 — a FRESH `before` per sample. Since Phase 273 `Delta.diff` reads back the
                  // keys it minted for a table object it has met, so diffing the same `t` every sample
                  // timed the `after` side alone: a per-row list walk injected into the `before` loop
                  // scored a linear 18.8 here. A fresh record over the same columns is a table the
                  // diff has never keyed, so both sides are measured.
                  scaling "Delta.diff" (fun t ->
                      Delta.diff idw { t with Columns = t.Columns } (editOne t) |> ok |> ignore)

              Expect.isLessThan r ratioBound "the diff reads every row's cells — once each, not once per row"

          clockCase "the incremental refresh is linear in the row count"
          <| fun _ ->
              let _, _, r =
                  scaling "Incremental.refreshOn" (fun t ->
                      let after = editOne t
                      let state = ok (Incremental.primeOn idw pipeline t)
                      let delta = ok (Delta.diff idw t after)
                      Incremental.refreshOn idw pipeline state delta after |> ok |> ignore)

              Expect.isLessThan r ratioBound "the refresh inherits the source scan — it must inherit a linear one"

          clockCase "a restricted refresh beats the full evaluation on the clock"
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

              // Phase 208 asserted a WIN here for both pipelines; Phases 265 and 270 made the full
              // evaluation faster and the one-comparison case a bounded loss (`cheapRefreshLossBound`,
              // three and then four). Phase 274 retired that bound into the table-fed tick family
              // below, which holds the refresh AND the diff within `tickBound` of the full evaluation
              // for this pipeline; the one-comparison figure is still printed here.
              ignore (trivialRefresh, trivialFull)

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "one edited row of twenty thousand must cost less than re-evaluating all of them"

          // ================= Phase 207 — the top-N board =================

          clockCase "a top-N refresh is linear in the row count"
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

          clockCase "a top-N refresh beats the full evaluation on the clock"
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

              // Phase 208 asserted a win here; Phases 267 and 269 made the full evaluation (the fused
              // top-n since 269) faster and the one-comparison case a bounded loss
              // (`topNRefreshLossBound`, five). Phase 274 retired that bound into the table-fed tick
              // family below, which holds this pipeline's refresh AND diff within `tickBound` of the
              // fused top-n; the one-comparison figure is still printed here.
              ignore (trivialRefresh, trivialFull)

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "a top-10 refresh over one edited row of twenty thousand must cost less than recomputing the board"

          // ================= Phase 202 — the steps after a maintained group-by =================

          clockCase "a group-tail refresh is linear in the row count"
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

          clockCase "a group-tail refresh, measured against the full evaluation"
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

              // Phase 208 asserted a win here (pre-208 at 20,000 rows: refresh 69.8 ms vs full
              // 19.7 ms; after, 10.4 ms vs 16.7 ms). Phase 265 made the full evaluation faster and the
              // one-comparison case a bounded loss under `cheapRefreshLossBound`; Phase 274 retired
              // that bound into the table-fed tick family below, which holds this pipeline's refresh
              // AND diff within `tickBound` of the full evaluation. The figure is still printed here.
              ignore (trivialRefresh, trivialFull)

              Expect.isLessThan
                  costlyRefresh
                  costlyFull
                  "a Having over one edited row of twenty thousand must cost less than recomputing it"

          // ================= Phase 263 — resolved column indices =================

          clockCase "a step's cost does not depend on WHICH column it names"
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

          clockCase "a join is linear in the row count"
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

          clockCase "a pivot is linear in the row count"
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

          clockCase "a group-by over n distinct keys is linear in the row count"
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

          clockCase "a distinct is linear in the row count"
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

          // ================= Phase 274 — the table-fed tick, held (272.t3) =================

          clockCase
              "the table-fed tick costs at most 1.6 times the full evaluation it replaces plus a fixed per-call floor"
          <| fun _ ->
              // What a table-fed caller pays per tick — `Delta.diff` plus the refresh — against the
              // full evaluation of the new source, for a ONE-ROW edit, on the three one-comparison
              // pipelines above (the cheapest full evaluations this family has, so the hardest case
              // for the seam), at each size, best of five, the ratio taken within one run.
              //
              // Phase 272 printed this unasserted as the counter-example to the claim; Phase 273
              // removed two of the tick's three key mintings; Phase 274 rewrote the refresh's
              // bookkeeping over columns and asserts it. RED on the pre-274 tree in this Debug build
              // (log in the phase's run scratch, figures in docs/incremental-evaluation.md): the
              // one-`Work`-record-per-row refresh alone cost several full evaluations at 20,000 and
              // 100,000 rows. The Release tables, every corpus node, both hosts, are in the doc.
              for n in [ small; large; 100_000 ] do
                  for label, p in
                      [ "tick: filter > groupBy", pipeline
                        "tick: filter > sort > limit", topNPipeline
                        "tick: group tail", groupTailPipeline ] do
                      let before = build n
                      let after = editOne before
                      let state = ok (Incremental.primeOn idw p before)

                      // The tick a caller pays (Phase 273): the prior source is the one the state
                      // last evaluated (its keys were minted when it was, and are read back), the
                      // new one a table nothing has keyed yet — a fresh object per sample, so no
                      // sample reads the previous sample's keys for it.
                      let tick () =
                          let a = { after with Columns = after.Columns }
                          let d = ok (Delta.diff idw before a)
                          ok (Incremental.refreshOn idw p state d a)

                      Expect.equal
                          (Ok(Incremental.result (tick ())))
                          (DataFrame.evalPipeline p after)
                          "the tick answers what the reference answers"

                      let tickMs = bestMs 5 (fun () -> tick () |> ignore)
                      let fullMs = bestMs 5 (fun () -> DataFrame.evalPipeline p after |> ok |> ignore)
                      let floorMs = tickFloorMs ()

                      printfn
                          "  [scaling] %-28s diff+refresh %7.2f ms vs full %7.2f ms (x%.2f, bound x%.1f + floor %.3f ms) @ %d"
                          label
                          tickMs
                          fullMs
                          (tickMs / fullMs)
                          tickBound
                          floorMs
                          n

                      // Phase 285: a calibration reading between cells, so a load that arrives in the
                      // middle of this case's window is seen rather than only its two ends.
                      Calibration.checkpoint ()

                      Expect.isLessThanOrEqual
                          tickMs
                          (tickBound * fullMs + floorMs)
                          (sprintf
                              "%s @ %d: a one-row tick (diff + refresh) must cost at most %.1f times the full evaluation it replaces plus the %.3f ms per-call floor"
                              label
                              n
                              tickBound
                              floorMs)

          // ================= Phase 283 — the tick on every corpus node =================

          clockCase
              "the table-fed tick on every corpus node costs at most 1.6 times the full evaluation plus a fixed per-call floor"
          <| fun _ ->
              // The case above holds the three `Scaling` pipelines; this one holds EVERY Phase 262
              // corpus node (the shapes the doc's tables measure) at 1,000, 20,000 and 100,000 rows,
              // at `tickBound`. The tick is the one a caller pays: the prior source is the one the
              // state last evaluated, the new one a fresh table object nothing has keyed. Batched best
              // of five, both figures from one run, the ratio within it.
              //
              // Phase 283 landed this case under the operator's 2026-09-28 ruling: `lines`,
              // `filter > groupBy` and `filter > sort > limit` held at 2.5 on .NET, every other node
              // at 1.5. Green that way on the pre-phase tree (worst cell
              // `lines` at 100,000 rows, 1.60 in this Debug build) and red on all three attempts with
              // six extra key renders injected per row into the diff. The diff then paired rows by
              // the typed id and stopped rendering a key string per row, the three shapes came to at
              // most 1.25 here (1.3 in Release, see the doc), and the per-shape bound was retired:
              // every node is held to `tickBound`. Do not reinstate or raise a bound to pass this case;
              // a reading above it is an operator decision, reported with the figures.
              //
              // Every cell is measured and printed before any is asserted, so a red attempt still
              // leaves the whole table in the log.
              let failures = System.Collections.Generic.List<string>()

              for nd in corpusNodes do
                  for n in [ small; large; 100_000 ] do
                      let w = RowIdentity.byColumn nd.IdCol
                      let before = nd.Mk n
                      let after = setCell nd.EditCol (n / 2) nd.EditTo before
                      let p = nd.Pipe n
                      let state = ok (Incremental.prime DataFrame.noResolve nd.Env w p before)

                      let tick () =
                          let a = { after with Columns = after.Columns }
                          let d = ok (Delta.diff w before a)
                          ok (Incremental.refresh DataFrame.noResolve nd.Env w p state d a)

                      Expect.equal
                          (Ok(Incremental.result (tick ())))
                          (DataFrame.evalPipelineInEnv nd.Env p after)
                          (sprintf "%s @ %d: the tick answers what the reference answers" nd.Name n)

                      let fullMs =
                          batchedMs 5 (fun () -> DataFrame.evalPipelineInEnv nd.Env p after |> ok |> ignore)

                      let tickMs = batchedMs 5 (fun () -> tick () |> ignore)
                      let bound = tickBound
                      let floorMs = tickFloorMs ()
                      let ratio = tickMs / fullMs

                      printfn
                          "  [corpus tick] %-26s @ %6d: tick %8.3f ms vs full %8.3f ms (x%.2f, bound x%.1f + floor %.3f ms)"
                          nd.Name
                          n
                          tickMs
                          fullMs
                          ratio
                          bound
                          floorMs

                      // Phase 285: a calibration reading between cells (see the case above).
                      Calibration.checkpoint ()

                      if tickMs > bound * fullMs + floorMs then
                          failures.Add(sprintf "%s @ %d: x%.2f against x%.1f + %.3f ms" nd.Name n ratio bound floorMs)

              Expect.isEmpty
                  failures
                  "every corpus node's one-row tick (diff + refresh) must cost at most 1.6 times the full evaluation it replaces plus the per-call floor" ]

/// `byColumn "id"`, counting every key it mints — the witness Phase 273 counted with.
let private countingId (minted: int ref) : RowIdentity<Cell> =
    { idw with
        KeyString =
            fun c ->
                minted.Value <- minted.Value + 1
                idw.KeyString c }

/// The keying floor of a diff by identity: every row's key, minted once through the witness.
let private keying (w: RowIdentity<Cell>) (t: Table) : string[] =
    let keyAt = w.KeyOf t
    let n = Table.rowCount t
    let keys: string[] = Array.zeroCreate n

    for i in 0 .. n - 1 do
        match keyAt i with
        | Some k -> keys[i] <- w.KeyString k
        | None -> ()

    keys

// The main suite's half of the family: the claims a COUNT can state. Phase 282 moved every case
// whose claim is about time to `clockTests` above; what stays here asserts work, exactly, and prints
// the clock figure beside it so the gate log still carries the numbers.
[<Tests>]
let scalingTests =
    testList
        "Scaling"
        [ testCase "the clock leg holds exactly the inventory's CLOCK cases"
          <| fun _ ->
              // The leg's own run count is checked against `clockInventory` by the entry point; this
              // pins the inventory against the list, so neither can move without the other.
              Expect.equal
                  (Test.toTestCodeList clockTests |> Seq.length)
                  clockInventory
                  "the clock leg's case count is the inventory's — update both, and the doc's table"

          testCase "the clock leg's saturation rule: above k times the baseline, or a bracket disagreeing by k"
          <| fun _ ->
              // Phase 285. The rule is pure, so it is pinned here, in the main suite, where no clock
              // runs; the leg applies it to readings it times.
              let k = Calibration.k
              let b = 10.0
              Expect.isFalse (Calibration.saturated b [ b; b ]) "a window reading the baseline is not saturated"

              Expect.isFalse
                  (Calibration.saturated b [ b; 1.2 * b; 1.2 * b; b ])
                  "a steady window within k of the baseline is not saturated"

              Expect.isTrue
                  (Calibration.saturated b [ b; (k + 0.01) * b ])
                  "a reading above k times the baseline is saturated"

              Expect.isTrue
                  (Calibration.saturated b [ 0.7 * b; 0.7 * (k + 0.01) * b ])
                  "two consecutive readings disagreeing by more than k are saturated, below k times the baseline"

              // Consecutive, not highest-against-lowest: a slow drift across a long window's cells
              // touches no one cell's bracket.
              Expect.isFalse
                  (Calibration.saturated b [ 0.7 * b; 0.8 * b; 0.9 * b; b ])
                  "a drift whose every step is within k is not saturated"

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

          testCase "Delta.diff costs what keying the two tables costs, at either size"
          <| fun _ ->
              // A table-fed caller (the everyday one: a fresh `Table` per tick) pays `Delta.diff`
              // against the prior source and THEN the refresh, so the diff is half of what the seam
              // costs it. Until Phase 272 that half was the larger one by far: see `diffFloorAllocBound`.
              //
              // The instrument is the diff's own FLOOR rather than the evaluator, on purpose. A diff
              // by identity must ask the witness for every row's key in both tables — `KeyString` is
              // the only thing that can say what a key is — so minting those strings is work no
              // implementation of this signature avoids. Held against that, the bound says "the diff
              // costs the keying plus a constant", which is a statement about the diff alone: it does
              // not move when the evaluator gets faster, so it is not a threshold a later phase has
              // to loosen for someone else's improvement.
              //
              // Phase 282 — COUNTED, where Phases 272 and 273 timed it. Two counts, one per way the
              // claim can break: the keys the diff mints (exactly one per row per table — a diff
              // that mints a key twice is Phase 273's regression) and the bytes it allocates against
              // the bytes the keying allocates (a token string per cell is Phase 272's). The clock
              // figures are printed beside them, unasserted.
              //
              // Phase 273 — every call diffs FRESH table objects. `Delta.diff` remembers the keys it
              // minted for a table and reads them back when it meets that very object again, so
              // re-diffing the same two tables would measure a diff that mints nothing and hold it
              // against a floor it no longer pays. A fresh record over the same columns is what a
              // caller that has never keyed either table hands in.
              let fresh (t: Table) : Table = { t with Columns = t.Columns }

              for n in [ large; small ] do
                  let before = build n
                  let after = editOne before

                  let minted = ref 0

                  Delta.diff (countingId minted) (fresh before) (fresh after) |> ok |> ignore

                  let diffBytes =
                      allocatedBytes (fun () -> Delta.diff idw (fresh before) (fresh after) |> ok |> ignore)

                  let floorBytes =
                      allocatedBytes (fun () ->
                          keying idw before |> ignore
                          keying idw after |> ignore)

                  let diffMs =
                      bestMs 5 (fun () -> Delta.diff idw (fresh before) (fresh after) |> ok |> ignore)

                  let floorMs =
                      bestMs 5 (fun () ->
                          keying idw before |> ignore
                          keying idw after |> ignore)

                  let allocRatio = float diffBytes / float floorBytes

                  printfn
                      "  [scaling] %-28s keys %d for %d rows; diff %d B vs keying %d B (x%.2f); clock diff %.2f ms vs keying %.2f ms (x%.2f) @ %d"
                      "Delta.diff against its floor"
                      minted.Value
                      (2 * n)
                      diffBytes
                      floorBytes
                      allocRatio
                      diffMs
                      floorMs
                      (diffMs / floorMs)
                      n

                  Expect.equal
                      minted.Value
                      (2 * n)
                      "the diff mints each row's key once per table — a second minting is the work Phase 273 removed"

                  Expect.isLessThan
                      allocRatio
                      diffFloorAllocBound
                      "the diff must allocate the keying of both tables and a constant factor more, not a token string per cell" ]
