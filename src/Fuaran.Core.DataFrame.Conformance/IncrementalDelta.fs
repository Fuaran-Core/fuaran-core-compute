namespace Fuaran.Core

// ============================================================================
//  The incremental-evaluation equivalence family (Phase 99).
//
//  `Incremental` evaluates a `Transform` pipeline against a delta instead of
//  from scratch. That is only ever an OPTIMISATION, so the claim it has to earn
//  is an equality: for every (base, delta) pair, the incremental result is the
//  reference result — the same table, or the same `EvalError`, with no
//  exception for the pairs that are awkward to get right.
//
//  The family is built so a defect cannot hide in a case the generator never
//  reaches. Its edits deliberately include the three that a first
//  implementation gets wrong and that no equality-of-happy-path would catch:
//  a pure REORDERING (which no identity diff reports as a row change, and which
//  moves `First` / `Last` and a float `Sum`), a filter that drops the only
//  typed row of a DERIVED column (which moves that column's inferred type), and
//  a SCHEMA change (which is not a row change at all). Its pipelines include
//  the ones the seam declines, because "falls back correctly" is a claim about
//  the answer as much as "propagates correctly" is.
//
//  Phase 115 admitted `Sort`, and the corpus grew to meet it rather than
//  merely to cover it. A merged order is wrong in exactly one way that an
//  equality over a tie-free corpus cannot see, so the generated tables draw
//  their sort keys from a range of three over up to six rows — ties are the
//  common case, not the edge — and the sort-bearing pipelines put a sort in
//  every position that matters: last (the shape the estate's recompute fixture
//  family carries), before a type-inferring `Derive` and a `Filter`, and
//  feeding an order-sensitive maintained `GroupBy` whose `First` / `Last`
//  aggregates read the order the sort produced. The `reverse` edit is what
//  makes those load-bearing: an identity diff reports it as quiet, so a merge
//  that trusted its cached order without checking arrival order would answer
//  a delta that named nothing with a table in the wrong order.
//
//  Phase 120 admitted two more classes and the corpus grew to meet each of
//  them. A BOUNDED-FRAME window (`lag` here) reads the row before this one in
//  its partition's order, so an edit moves its neighbour's output as well as
//  its own. A FILTERING join (`semi` / `anti`) is a filter whose predicate
//  reads a relation, so it appears both with a row-local step after it and
//  feeding an order-sensitive maintained group; a combining join (`inner`) is
//  present as its decline, because a fall-back that returns the wrong answer
//  is the worse failure.
//
//  `0.19.0` admitted the rest of the window family, on row-set preservation
//  rather than frame boundedness, and the corpus grew again — a bare
//  `cumulSum` (which was this family's window decline) and a `rank` behind a
//  filter. Both are PARTITION-GLOBAL: every row's output reads every other row
//  of its partition, and the column is recomputed wholesale over the walked
//  frame, exactly as the bounded ones already were. The demands below insist
//  on that narrower class being reached, because "a window was restricted" is
//  satisfied by the `lag` alone and would have gone on passing had the
//  relaxation been reverted.
//
//  Then a consumer swept the family over independent seeds and found that
//  demand MARGINAL: the two classes admitted last were each carried by two
//  pipelines out of twenty and reached by about one sample in twenty, so a
//  hundred-iteration run missed one of them often enough for the guard's
//  verdict to be a coin flip rather than a finding. The corpus grew a seventh
//  time, and this time for a reason internal to the guard rather than to the
//  seam: `20`–`26` are compositions of the classes the corpus already held —
//  a relation verdict feeding a running total, a merged order, a bounded frame;
//  a window feeding a maintained group; a filter feeding a relation verdict —
//  chosen so one draw answers two demands at once. The measured rates and the
//  before/after guard firings are recorded on `pipelineOf` below, because a
//  claim about a generator's reach is only worth what its measurement is.
//
//  Every sample also records its FOOTPRINT — what the prime evaluated and what
//  the refresh evaluated — so the family certifies not only that the answer is
//  right but that the seam did less work to reach it. An incremental evaluator
//  that silently recomputed everything would pass an equality suite perfectly.
//
//  Phase 117 made that instrument comparable across the boundary. Its
//  work law used to quantify over the restricted classes alone, because a full
//  evaluation was reported as its SOURCE ROW COUNT while a restricted one
//  counted row evaluations at steps — two scales, so the declined samples could
//  not be measured at all and a declined refresh read as cheaper than the
//  baseline it was measured against. Both sides now count the same unit, and
//  the law runs over every sample the corpus generates.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// One generated (base, delta) pair and what it cost — the record the family both judges and
/// reports. Public so a consumer can run the generator and print the footprints itself.
type IncrementalSample =
    {
        Seed: int
        Iteration: int
        /// The pipeline evaluated.
        Pipeline: Transform list
        /// Its declared strategy.
        Strategy: IncrementalStrategy
        /// What the initial full evaluation over the BASE cost.
        Prime: RecomputeFootprint
        /// What a full evaluation over the CHANGED source would have cost — the baseline the refresh
        /// has to beat, and the only honest one, since the changed source may be the larger table.
        Full: RecomputeFootprint
        /// What advancing the primed state against the delta cost.
        Refresh: RecomputeFootprint
        /// Did the incremental answer equal the reference answer?
        Equivalent: bool
        /// Did priming equal a full reference evaluation over the base?
        PrimeEquivalent: bool
        /// A short tag naming the edit that produced the delta — what a counterexample cites.
        Edit: string
    }

/// The incremental-evaluation equivalence family: generate (base, delta) pairs, run both
/// evaluators, and certify the answers identical while recording the work each did.
module IncrementalDelta =

    // ---- the corpus ----

    let private idw = RowIdentity.byColumn "id"

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    /// The generated table: a string identity column and two int columns, `b` drawn from a small
    /// range so groups have several members (a one-row-per-group corpus would never exercise a
    /// maintained aggregate at all).
    let private mkTable (rows: (string * Cell * Cell) list) : Table =
        { Schema = [ "id", StringType; "a", IntType; "b", IntType ]
          Columns =
            [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
              Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
              Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b)) ] }

    /// The same rows with a fourth column — a SCHEMA change, which is not a row change and which
    /// the seam must recognise as such rather than diff its way through.
    let private mkWideTable (rows: (string * Cell * Cell) list) : Table =
        { Schema = [ "id", StringType; "a", IntType; "b", IntType; "c", IntType ]
          Columns =
            [ Column.create "id" StringType (rows |> List.map (fun (i, _, _) -> Str i))
              Column.create "a" IntType (rows |> List.map (fun (_, a, _) -> a))
              Column.create "b" IntType (rows |> List.map (fun (_, _, b) -> b))
              Column.create "c" IntType (rows |> List.map (fun _ -> Int 1)) ] }

    /// The relation the filtering-join pipelines match against (Phase 120): a two-row lookup on
    /// `k`, holding two of the three values `b` is drawn from. A semi join over it therefore keeps
    /// roughly two thirds of any generated table and an anti join the rest, so both sides of the
    /// verdict arise in every table rather than in the lucky ones.
    let private lookup: Table =
        { Schema = [ "k", IntType ]
          Columns = [ Column.create "k" IntType [ Int 0; Int 2 ] ] }

    /// The pipelines. The DECLINED ones are `6`, `18`, `31`, `35` and the `_` arm; every other
    /// index is incrementalisable. They are present because a fall-back that returns the wrong
    /// answer is the worse failure.
    ///
    /// **That enumeration is maintained here and asserted nowhere, so read it as a reading aid and
    /// not as a fact** — it was already wrong when Phase 202 arrived (Phase 207 admitted `8` and
    /// added `31` without amending the sentence, which said `6`–`8` and `18`). The load-bearing
    /// statement is each arm's own comment, which cannot drift from the arm it sits on; `declined`
    /// is an adequacy class below, so the *existence* of declines is guarded even when this
    /// sentence is stale.
    ///
    /// `11`–`13` are the sort-bearing shapes (Phase 115): a sort last, a sort before the
    /// steps that read the order it produced, and a sort feeding an order-sensitive maintained
    /// group. `14`–`18` are Phase 120: a bounded-frame window, a partition-global one, a filtering
    /// join with a step after it, another feeding a maintained group, and a combining join.
    ///
    /// `15` and `19` are the PARTITION-GLOBAL windows, and they are why the demand below can insist
    /// on that class rather than on "a window" (`0.19.0`): `15` is a bare cumulative aggregate, so
    /// the refresh's restriction is visible with nothing else in the pipeline to attribute it to,
    /// and `19` is a `rank` behind a filter, which is the shape the saving is actually claimed on —
    /// the filter stops re-evaluating every row while the ranked column is recomputed as the
    /// reference computes it. `15` was this family's window DECLINE until the row-set-preserving
    /// relaxation; keeping it and adding `19` is what makes the widening measurable here.
    ///
    /// `20`–`26` widen the two NARROWEST demands. Measured over 400 seeds × 100 iterations at both
    /// the shipped row bound and a wider one, the partition-global-window class was reached by 5.4%
    /// of samples and the relation-filtered class by 5.2% — each carried by exactly two pipelines
    /// out of twenty, against four or more for every other class — and the adequacy guard reported
    /// one of them missing on 8 of those 800 runs. That is a guard on a coin flip, and a coin flip
    /// is what gets a guard re-seeded rather than believed, which is the one remedy
    /// `SampleAdequacy` exists to forbid. So the generator is what moved: the same sweep now
    /// reaches those classes on 13.5% and 13.7% of samples and the guard fires on 0 of 800.
    ///
    /// The seven are not filler for a count. `classify` names EVERY class a sample reached, so a
    /// pipeline that both filters by a relation and frames a partition-global window is one draw
    /// answering two demands — which is how the thin classes rise without the thick ones being
    /// crowded out of the draw as hard as seven unrelated pipelines would crowd them. Each is also
    /// a composition the corpus did not hold: a relation verdict feeding a running total (`20` its
    /// `semi` side, `25` its `anti` side, where an emptied partition is the common case), a merged
    /// order feeding a ranked column, whose OUTPUT order is the sort's even though its values are
    /// not (`21`), a relation verdict feeding a bounded frame, so the join decides who a row's
    /// neighbour IS (`22`), a partition-global window feeding an order-sensitive maintained group
    /// that reads the window's own appended column (`23`), a relation verdict feeding a merged
    /// order (`24`), and a row-local filter BEFORE a relation verdict rather than after one (`26`).
    ///
    /// Spreading the draw over 27 pipelines instead of 20 necessarily costs the classes that did
    /// not grow: `declined` falls from 20.3% of samples to 14.9% and `group-restricted` from 10.6%
    /// to 9.9%. Both stay well clear of the margin — neither was missed once in the same 800 runs —
    /// and that trade is the point rather than a side-effect: an adequacy demand is answered by
    /// whether a class can be MISSED, not by how comfortably the comfortable classes lead.
    let private pipelineOf (k: int) : Transform list =
        match k with
        | 0 -> [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
        | 1 -> [ Derive("d", Binary(Add, Col "a", Col "b")); Project [ "id", "id"; "d", "d" ] ]
        | 2 ->
            [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
              Derive("d", Binary(Mul, Col "a", Lit(Int 2))) ]
        | 3 -> [ GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ]) ]
        | 4 ->
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              GroupBy([ "b" ], [ agg "mx" Max "a"; agg "f" First "id"; agg "l" Last "id" ]) ]
        | 5 -> [ Transform.sortBy [ "b", Asc ] ] // merged order over the TIE-HEAVY key (Phase 115)
        | 6 -> [ Project [ "b", "b" ]; Distinct ] // declined: whole-relation
        // Phase 202 — this was the family's "maintainable step that is not last" DECLINE until the
        // steps after a group-by were admitted. It is kept and re-read rather than replaced, on the
        // same argument Phase 207 kept `8`: a bare `Having` is the shape whose restriction is
        // visible with nothing else in the pipeline to attribute it to. The decline it used to
        // carry is now `35`'s.
        | 7 ->
            [ GroupBy([ "b" ], [ agg "n" Count "a" ])
              Filter(Binary(Gt, Col "n", Lit(Int 0))) ]
        // Phase 207 — this was the family's `limit` DECLINE until a `Limit` was admitted. It is kept
        // and re-read rather than replaced: a bare top-N is the shape whose restriction is visible
        // with nothing else in the pipeline to attribute it to, exactly as `15` is for the
        // partition-global windows. The decline it used to carry is now `18`'s and `6`'s.
        | 8 -> [ Transform.limit 2 0 ]
        | 9 ->
            // a derived column whose TYPE depends on which rows survive, followed by a filter that
            // can drop the only typed row — the inferred-type trap.
            [ Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 0)), Lit(Str "pos") ], Lit Null))
              Filter(Binary(Lt, Col "a", Lit(Int 0))) ]
        | 10 -> [ Derive("a", Binary(Add, Col "a", Lit(Int 1))) ] // Derive OVERWRITING a column
        | 11 ->
            // the shape the estate's recompute fixture family carries: a filter, then a sort.
            [ Filter(Binary(Gt, Col "a", Lit(Int 0))); Transform.sortBy [ "a", Asc ] ]
        | 12 ->
            // a sort that is NOT last, followed by the two steps that read the order it produced —
            // a derive whose column TYPE is inferred over the frame, and a filter.
            [ Transform.sortBy [ "b", Asc ]
              Derive("d", Case([ Binary(Gt, Col "a", Lit(Int 0)), Lit(Str "pos") ], Lit Null))
              Filter(Binary(Lt, Col "a", Lit(Int 3))) ]
        | 13 ->
            // a sort feeding an order-sensitive maintained group: `First` and `Last` read the
            // position the sort put each row in, and `b` ties heavily, so the sort's STABILITY is
            // what decides the answer rather than its comparator alone.
            [ Transform.sortBy [ "b", Asc; "a", Desc ]
              GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "n" Count "a" ]) ]
        | 14 ->
            // Phase 120 — a BOUNDED-frame window: a filter, then a lag over the tie-heavy partition
            // key. The lag reads the row before this one in the partition's order, so an edit to any
            // row moves its neighbour's output as well as its own.
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" } ]
        | 15 ->
            // A PARTITION-GLOBAL window, alone: a cumulative aggregate reads every preceding row of
            // its partition, so it is precisely the shape frame boundedness declined and row-set
            // preservation admits. It emits the rows it was handed, one for one, with the running
            // total appended — which is all the walk needs.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" } ]
        | 16 ->
            // Phase 120 — a filtering join with a row-local step AFTER it, so the verdict is not the
            // last thing the walk does and the derived column is typed over the rows it left alive.
            [ Join(Embedded lookup, [ "b", "k" ], Semi)
              Derive("d", Binary(Mul, Col "a", Lit(Int 2))) ]
        | 17 ->
            // Phase 120 — the ANTI verdict feeding an order-sensitive maintained group: what the
            // join decides is which rows are in the group at all.
            [ Join(Embedded lookup, [ "b", "k" ], Anti)
              GroupBy([ "b" ], [ agg "n" Count "a"; agg "f" First "id" ]) ]
        | 19 ->
            // A partition-global window BEHIND A FILTER — the shape the saving is claimed on, and
            // the one the widening's named consumer wants: a ranked column over a live grid. The
            // rank is recomputed over the walked frame; what the admission buys is that the filter
            // stops running over every row.
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" } ]
        | 20 ->
            // A relation verdict feeding a PARTITION-GLOBAL window: the join decides which rows are
            // in the partition at all, and the running total then reads every one of them. An edit
            // the join's verdict flips therefore moves an output the edited row does not appear in.
            [ Join(Embedded lookup, [ "b", "k" ], Semi)
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" } ]
        | 21 ->
            // A merged order feeding a partition-global window. The window carries its own
            // `OrderBy`, so the ranked VALUES do not read the sort — but a window emits its rows in
            // the order it was handed them, so the output ORDER does. A merge that got the sort
            // right and then rebuilt the frame from a cached arrival order would answer with the
            // right numbers against the wrong rows.
            [ Transform.sortBy [ "b", Asc; "a", Desc ]
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" } ]
        | 22 ->
            // A relation verdict feeding a BOUNDED frame — the pairing `14` and `16` each hold one
            // half of. What the anti join decides here is not merely whether a row survives but who
            // the surviving row's NEIGHBOUR is, so one flipped verdict moves the lagged output of a
            // row whose own columns did not change.
            [ Join(Embedded lookup, [ "b", "k" ], Anti)
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" } ]
        | 23 ->
            // A partition-global window feeding an order-sensitive maintained group, aggregating
            // the window's OWN appended column: the group reads a value no source row carries, and
            // `First` / `Last` read the position the window emitted its rows in. A maintained group
            // that cached its members' contributions would answer from a `rk` computed over the
            // partition as it stood before the delta.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }
              GroupBy([ "b" ], [ agg "mx" Max "rk"; agg "f" First "id"; agg "n" Count "a" ]) ]
        | 24 ->
            // A relation verdict feeding a MERGED ORDER — the third of the three restricted classes
            // a filtering join can hand its output to, and the one that pairs the two demands this
            // widening had to move furthest apart. The merge has to place the surviving rows among
            // the cached order's ties without ever seeing the rows the join removed.
            [ Join(Embedded lookup, [ "b", "k" ], Semi); Transform.sortBy [ "b", Asc ] ]
        | 25 ->
            // The ANTI verdict feeding a partition-global window. `20` is its `semi` mirror, and the
            // pair is not redundant: a semi join keeps roughly two thirds of a generated table and an
            // anti join the remaining third, so the partition a running total walks is a different
            // size and a different shape under each, and an empty partition — which only the anti
            // side reaches often — is where a frame recompute is most easily wrong.
            [ Join(Embedded lookup, [ "b", "k" ], Anti)
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" } ]
        | 26 ->
            // A row-local filter BEFORE a relation verdict, then a ranked column: the only shape in
            // the corpus where a restricted row-filter feeds a filtering join rather than following
            // one. The two verdicts compose in that order and not the other — the join sees only the
            // rows the filter left, so a refresh that re-evaluated the join over the cached
            // pre-filter frame would keep rows the filter had already dropped.
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              Join(Embedded lookup, [ "b", "k" ], Semi)
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" } ]
        | 27 ->
            // Phase 207 — the headline top-N shape: a filter, a merged order over the TIE-HEAVY key,
            // then the cut. `b` is drawn from three values over up to nine rows, so the cut lands
            // inside a run of ties in most draws, and which of the tying rows is kept is decided by
            // the arrival-position tiebreak alone. The `reverse` edit is what makes that
            // load-bearing here as it is for `11`–`13`: an identity diff reports it as quiet, so a
            // truncation taken over a cached order that was not re-checked against arrival order
            // answers a delta naming nothing with the wrong rows in the window.
            [ Filter(Binary(Gt, Col "a", Lit(Int -5)))
              Transform.sortBy [ "b", Asc ]
              Transform.limit 2 0 ]
        | 28 ->
            // A top-N with NO sort in front of it — the second shape the phase's acceptance names,
            // and the one that shows the admission is not conditioned on a preceding `Sort`. The
            // order it truncates is ARRIVAL order, which the walk maintains exactly as it maintains
            // a merged one, so a reordering edit moves this window with nothing in the delta to say
            // so and a filter edit moves it by changing who arrives at the cut.
            [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
              Project [ "id", "id"; "a", "a" ]
              Transform.limit 3 0 ]
        | 29 ->
            // A non-zero OFFSET: the window skips before it keeps, so a change in the SKIPPED
            // prefix moves both kept rows while appearing in neither. An implementation that
            // truncated before it skipped, or that clamped the skip against the wrong length, is
            // right on every `offset = 0` draw in the corpus and wrong on every one of these.
            [ Transform.sortBy [ "a", Desc ]; Transform.limit 2 1 ]
        | 30 ->
            // A truncated order feeding an ORDER-SENSITIVE maintained group: `First` and `Last` read
            // the position the window left each row in, and the group partition is built from the
            // rows the cut left alive. A maintained group that recomputed its members from the
            // pre-truncation frame would aggregate rows the limit had already dropped — the same
            // defect shape `13` catches one verb earlier.
            [ Transform.sortBy [ "b", Asc; "a", Desc ]
              Transform.limit 4 0
              GroupBy([ "b" ], [ agg "f" First "id"; agg "l" Last "id"; agg "n" Count "a" ]) ]
        | 31 ->
            // Declined: one source row becomes one row PER value column, so the output rows are not
            // the input rows at all. It joins the corpus with Phase 207, which admitted `Limit` and
            // left the declined set a pipeline short — and the class matters more than the count,
            // since a fall-back that returns the wrong answer is the worse failure.
            [ Unpivot([ "id" ], [ "a"; "b" ]) ]
        | 32 ->
            // Phase 202 — a `Having` over a group table whose groups can EMPTY: the filter on the
            // prefix side can take a group's last row away, so a group leaves the group table
            // entirely while a sibling group's aggregate merely moves. A tail that carried its
            // cached verdicts forward by group would keep emitting the departed one.
            [ Filter(Binary(Gt, Col "a", Lit(Int -4)))
              GroupBy([ "b" ], [ agg "n" Count "a"; agg "mn" Min "a" ])
              Filter(Binary(Gt, Col "n", Lit(Int 1)))
              Transform.limit 2 0 ]
        | 33 ->
            // Phase 202 — a tail that EVALUATES: the only shape in the corpus where the per-group
            // cell cache is observable at all, since every other tail step here evaluates no
            // expression and is charged none. A group whose aggregates were reused must not have
            // this expression re-evaluated, and one whose aggregates moved must.
            [ GroupBy([ "b" ], [ agg "s" Sum "a"; agg "n" Count "a" ])
              Derive("mean2", Binary(Mul, Col "s", Lit(Int 2)))
              Filter(Binary(Ge, Col "mean2", Lit(Int -20))) ]
        | 34 ->
            // Phase 202 × Phase 207 — the two admissions composed, with the limit on the GROUP
            // table rather than on the source rows: a sort of the groups by a maintained aggregate,
            // then a cut. Which group survives the cut is decided by an aggregate the delta moved
            // indirectly, so a stale group row changes the window's membership rather than one
            // cell of it.
            [ GroupBy([ "b" ], [ agg "s" Sum "a"; agg "f" First "id" ])
              Transform.sortBy [ "s", Desc ]
              Transform.limit 2 0 ]
        | 35 ->
            // Phase 202 — declined: a SECOND aggregating step. The first group-by is maintained and
            // its tail walks the group table; grouping THAT table needs a second level of
            // row-to-group, ordered-membership and aggregate state. It joins the declined set as
            // `7` leaves it, and for the reason that set exists at all — a fall-back that returns
            // the wrong answer is the worse failure.
            [ GroupBy([ "b" ], [ agg "n" Count "a" ])
              GroupBy([ "n" ], [ agg "m" Count "n" ]) ]
        // `36` and `37` widen the group-tail class the way `20`–`26` widened the two narrowest
        // before it, and for the same measured reason rather than for a count: four tail-bearing
        // pipelines out of thirty-six reached `group-tail-restricted` on 5.80% of 60,000 samples,
        // under the 7% floor, and the remedy that file's own note forbids is re-seeding. Each of
        // these carries a SECOND thin class as well, so the draw they take back is one the thin
        // classes get returned — which is how `group-tail-restricted` rises without
        // `relation-filtered-restricted` and the partition-global windows paying for it.
        //
        // The measurement that forced the second pass, recorded because it is the trade the note on
        // `20`–`26` predicts rather than a surprise: widening to thirty-eight pipelines cleared the
        // 7% floor for the new class and cost `top-n-restricted` its reliability — the seed sweep
        // fired on `bound=12 seed=243`, where a hundred-iteration draw reached it zero times. Four
        // of the five pipelines this phase adds therefore end in a `limit`, which is a shape the
        // seam genuinely newly admits (a top-N over the GROUP table) rather than filler: the
        // carrier count for `top-n-restricted` rises from five to eight while the draw is spread
        // over more pipelines, and the sweep fires on 0 of 600 again.
        | 36 ->
            // A relation verdict feeding a maintained group feeding a tail: the join decides who is
            // IN each group, so an unchanged relation and an unchanged row can still move a group's
            // aggregate and therefore the tail's verdict on it.
            [ Join(Embedded lookup, [ "b", "k" ], Semi)
              GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ])
              Filter(Binary(Gt, Col "s", Lit(Int -20))) ]
        | 37 ->
            // A partition-global window feeding a maintained group feeding a tail that reads the
            // aggregate OF the window's own appended column — three frames deep, which is the
            // furthest composition the seam admits.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              GroupBy([ "b" ], [ agg "mx" Max "run"; agg "n" Count "a" ])
              Derive("mxn", Binary(Add, Col "mx", Col "n"))
              Transform.limit 3 0 ]
        // `38`–`47` close the class Phase 208 found the hard way — a ROW-LOCAL step (`Filter`,
        // `Derive`, `Project`) reading a column whose value for row r depends on rows OTHER than r.
        // The corpus had ten window-bearing shapes and not one of them: in eight the window is the
        // last step, and in the two that continue (`23`, `37`) the next step is a `GroupBy`, which
        // re-aggregates a group from its member rows and never consults the per-row cache. So the
        // family that exists to see that defect could not, and an evaluator carrying it passed.
        //
        // The census is in `docs/incremental-evaluation.md`. Two cross-row PRODUCERS append a column
        // a later row-local step can read — a partition-global window (`cumulSum` / `rank`) and a
        // bounded-frame one (`lag`) — against four row-local CONSUMER forms: a `Filter` on the
        // column (row SURVIVAL moves when another row moves), a `Derive` over it (a cell moves), a
        // `Derive` OVERWRITING it, and a `Project` RENAMING it before a later step reads it. Eight
        // cells, all eight absent, and every one of them is shown RED under the pre-`0.28.0` cache
        // predicate and green on the shipped one.
        //
        // The third producer the census names — a maintained group's aggregate column read by the
        // group tail — was already REACHED (`7`, `32`, `33`, `36`, `37`) and is deliberately NOT
        // regenerated here: the group table's own `Stable` is "this group's aggregates were
        // recomputed", which is the same statement as the pre-208 predicate, so those cells cannot
        // discriminate and adding shapes for them would grow the corpus without growing what it
        // catches. The other two candidate producers are absent BY CONSTRUCTION: a `Limit` appends
        // no column at all, and the only join that appends one is a combining join, which the seam
        // declines (`31`'s neighbour, the `_` arm).
        //
        // Each shape carries BOTH producer classes where it can, for the reason the note on
        // `20`–`26` records: one draw answering two demands is what lets a thin class rise without
        // the classes that did not grow paying for it. Measured over the same 2 bounds × 300 seeds ×
        // 100 iterations sweep `IncrementalTests` pins the 7% floor on — 60,000 samples — the two
        // new demands are reached by 8.88% and 8.93% of samples, and every pre-existing refresh
        // class still clears the floor, the narrowest being `declined` at 8.23% (was 10.45%).
        // `group-tail-restricted` is the one this widening would otherwise have pushed UNDER it:
        // 8.37% over 38 pipelines, a projected 6.62% over 48, and 8.88% as it stands, because `42`
        // and `46` give it two new carriers. That is the same trade the note on `20`–`26` predicts,
        // taken deliberately rather than discovered.
        //
        // Every one of the ten DISCRIMINATES, which is the bar a shape is admitted on: with the
        // pre-`0.28.0` predicate reintroduced at the two sites Phase 208 changed, these ten are
        // exactly the pipelines that disagree with the reference evaluator, 22 to 84 samples of
        // roughly 500 each, and the other thirty-eight stay green. No shape was dropped.
        | 38 ->
            // A partition-global running total read by a FILTER, then a bounded frame read by a
            // DERIVE. The headline pair, and the shape closest to the two pipelines Phase 208
            // measured wrong: the filter's verdict on a row the delta never named moves whenever any
            // other row of its partition does, and the lagged cell behind it moves for the same
            // reason one row further out.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              Filter(Binary(Gt, Col "run", Lit(Int 2)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Derive("d", Binary(Add, Col "prev", Col "a")) ]
        | 39 ->
            // The mirror: a BOUNDED frame read by a filter, then a partition-global rank read by a
            // derive that OVERWRITES it. `prev` is null for the first row of each partition, so the
            // survival verdict here is three-valued as well as stale-able — and the overwrite takes
            // the `Derive` arm's in-place replacement branch rather than its append branch, which is
            // the one no other shape reaches behind a window.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Filter(Binary(Ge, Col "prev", Lit(Int -5)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }
              Derive("rk", Binary(Mul, Col "rk", Lit(Int 2))) ]
        | 40 ->
            // A running total read by a derive, then a lagged column RENAMED by a projection and
            // read under its new name. The rename is the point: an implementation that answered
            // Phase 208 by invalidating the expressions that NAME a window's output column — the
            // obvious narrower fix — is right on every other shape here and wrong on this one.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              Derive("e", Binary(Add, Col "run", Col "a"))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Project [ "id", "id"; "prev", "v"; "e", "e" ]
              Filter(Binary(Lt, Col "v", Lit(Int 4))) ]
        | 41 ->
            // The rename on the OTHER producer, and a projection that DROPS columns the later steps
            // do not name: a rank renamed and filtered on, then a bounded frame whose own column is
            // overwritten in place. The dropped columns matter — the cached prefix is positional, so
            // a projection that narrows the frame is where a cache read by index rather than by row
            // identity goes wrong quietly.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }
              Project [ "id", "id"; "a", "a"; "b", "b"; "rk", "v" ]
              Filter(Binary(Le, Col "v", Lit(Int 2)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Derive("prev", Binary(Add, Col "prev", Lit(Int 1))) ]
        | 42 ->
            // The stale SURVIVOR SET where it does the most damage: a filter on a running total
            // deciding who is in a maintained group at all, with a tail reading the group's own
            // count. A row wrongly kept or wrongly dropped here does not move one cell — it moves a
            // group's membership, its aggregate, and the tail's verdict on it.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              Filter(Binary(Gt, Col "run", Lit(Int 0)))
              GroupBy([ "b" ], [ agg "n" Count "a"; agg "s" Sum "a" ])
              Filter(Binary(Gt, Col "n", Lit(Int 0))) ]
        | 43 ->
            // The same survivor set feeding a MERGED ORDER and then a CUT — the two cross-row steps
            // `13` and `27` put behind a row-local filter, now behind one whose verdict a window can
            // move. Which rows reach the cut is decided by a filter reading a lagged neighbour, so a
            // stale verdict changes the window's MEMBERSHIP rather than one cell of it.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Filter(Binary(Ge, Col "prev", Lit(Int -3)))
              Transform.sortBy [ "b", Asc ]
              Transform.limit 2 0 ]
        | 44 ->
            // A ranked column read by a derive, with a merged order and a cut BEHIND the consumer:
            // the derived cell is carried through two cross-row steps that decide membership, so a
            // stale one is not merely a wrong cell in place — it is a wrong cell inside the window
            // the cut keeps.
            //
            // The order is keyed on `d` — the DERIVED column that reads the window's own output —
            // and that is what makes this shape discriminate rather than merely exercise. Phase 212
            // had to withhold it: sorting on `d` was red on the shipped seam, whose `WSort` arm
            // built its reusable set from "the delta did not name this row" instead of "this row's
            // cells have not moved", so a merged order reused the cached POSITION of a row whose
            // sort key a window had moved. Phase 215 finished Phase 208's substitution at that third
            // site and moved the key here, where it belonged. Measured both ways at that phase: with
            // the pre-fix predicate reintroduced this shape goes RED and sorting on `b` (a source
            // column) does not, so the key is the whole of what discriminates.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Rank
                    Of = "a"
                    As = "rk" }
              Derive("d", Binary(Add, Col "rk", Col "a"))
              Transform.sortBy [ "d", Asc ]
              Transform.limit 3 0 ]
        | 45 ->
            // A relation verdict deciding the FRAME the window walks, then both producers read
            // row-locally. The join is what makes this different from `38`: a row the delta never
            // named can leave the partition because the RELATION moved, so the running total the
            // filter reads moves for a reason the delta cannot describe at all.
            [ Join(Embedded lookup, [ "b", "k" ], Semi)
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              Filter(Binary(Gt, Col "run", Lit(Int 1)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Derive("d", Binary(Add, Col "prev", Lit(Int 1))) ]
        | 46 ->
            // A lagged column read by a derive whose column is then AGGREGATED by a maintained group
            // and read by its tail. `23` and `37` aggregate a window's column directly; here a
            // row-local step stands between the two, so a stale cell reaches the group as a wrong
            // contribution from a row the group's own cache considers settled.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Derive("d", Binary(Add, Col "prev", Col "a"))
              GroupBy([ "b" ], [ agg "mx" Max "d"; agg "n" Count "a" ])
              Filter(Binary(Gt, Col "n", Lit(Int 0))) ]
        | 47 ->
            // Both producers' columns OVERWRITTEN in place, one after the other, with the second
            // window framed over a table the first overwrite has already rewritten. It is the only
            // shape where a window runs over a frame whose earlier window column is no longer the
            // window's own value, so a cache that keyed reuse on "this step appended what it
            // appended last time" rather than on the row's cells is wrong here and nowhere else.
            [ Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = CumulSum
                    Of = "a"
                    As = "run" }
              Derive("run", Binary(Add, Col "run", Lit(Int 1)))
              Window
                  { PartitionBy = [ "b" ]
                    OrderBy = [ "a", Asc ]
                    Fn = Lag
                    Of = "a"
                    As = "prev" }
              Derive("prev", Binary(Mul, Col "prev", Lit(Int 2))) ]
        | _ ->
            // Phase 120 — declined by KIND: a combining join fans a left row out across its matches
            // and appends the right schema, so one source row is no longer one output row.
            [ Join(Embedded lookup, [ "b", "k" ], Inner) ]

    let private pipelineCount = 48

    /// Apply one edit to the base rows, returning the new table and a tag naming the edit.
    let private editOf (k: int) (rows: (string * Cell * Cell) list) (n: int) : Table * string =
        match k with
        | 0 -> mkTable rows, "none"
        | 1 -> mkTable (rows @ [ "z1", Int 4, Int 1; "z2", Int -4, Int 2 ]), "append"
        | 2 ->
            (match rows with
             | [] -> mkTable rows, "remove(empty)"
             | _ :: rest -> mkTable rest, "removeFirst")
        | 3 ->
            (match rows with
             | [] -> mkTable rows, "change(empty)"
             | (i, _, b) :: rest -> mkTable ((i, Int 42, b) :: rest), "changeFirstA")
        | 4 -> mkTable (List.rev rows), "reverse"
        | 5 ->
            (match rows with
             | [] -> mkTable rows, "null(empty)"
             | (i, _, b) :: rest -> mkTable ((i, Null, b) :: rest), "nullFirstA")
        | 6 ->
            (match List.rev rows with
             | [] -> mkTable rows, "changeLast(empty)"
             | (i, a, _) :: revRest -> mkTable (List.rev ((i, a, Int((n + 1) % 3)) :: revRest)), "regroupLast")
        | 7 -> mkWideTable rows, "schemaWiden"
        | 8 ->
            // A MIDDLE row's non-key column. Under a sort keyed on `b` this is the tie scenario a
            // merge gets wrong silently: the changed row is lifted out of the cached order and put
            // back among rows it ties with, and only the arrival-position tiebreak decides where.
            // Changing the FIRST row (edit 3) reaches it far less often — the earliest arrival wins
            // its ties under a merge that has no tiebreak at all.
            (match rows with
             | [] -> mkTable rows, "changeMiddle(empty)"
             | _ ->
                 let m = List.length rows / 2

                 mkTable (rows |> List.mapi (fun j (i, a, b) -> if j = m then i, Int 77, b else i, a, b)),
                 "changeMiddleA")
        | _ -> mkTable (rows @ [ "z3", Int 7, Int 0 ]), "appendOne"

    let private editCount = 10

    /// The delta the refresh is driven by. The identity diff is the normal producer; the other
    /// three are the honest coarse answers a source may give instead, and each must still yield the
    /// reference result.
    let private deltaOf (k: int) (before: Table) (after: Table) : TableDelta =
        match k with
        | 0
        | 1
        | 2 ->
            match Delta.diff idw before after with
            | Ok d -> d
            | Error _ -> FullRefresh
        | 3 -> FullRefresh
        | 4 -> Delta.diffByOrdinal before after
        | _ -> Delta.ofColumns idw.Scheme [ "a" ]

    let private deltaCount = 6

    // ---- generation ----

    /// Generate the samples for a seed, drawing tables of one to `rowBound` rows.
    ///
    /// The bound is a parameter for one reason: it is the exact lever Phase 115 had to move (from 5
    /// to 9, because most tables held ONE row and no tie between a named and an unnamed row ever
    /// arose), so the go-red proof for this family's span demand is to narrow it again and watch the
    /// guard fail. A probe that lives in the suite is a proof; a probe that lived in one session's
    /// memory is a claim. `samples` pins the shipped bound.
    let samplesWith (rowBound: int) (seed: int) (iterations: int) : IncrementalSample list =
        let mutable rng = ConfRng.ofSeed seed

        [ for i in 0 .. iterations - 1 do
              let nRows, r0 = ConfRng.intBelow rowBound rng
              let mutable r = r0

              let rows =
                  [ for j in 0..nRows do
                        let a, r1 = ConfRng.intBelow 12 r
                        r <- r1
                        let b, r2 = ConfRng.intBelow 3 r
                        r <- r2
                        "r" + string j, Int(a - 5), Int b ]

              let pk, r3 = ConfRng.intBelow pipelineCount r
              r <- r3
              let ek, r4 = ConfRng.intBelow editCount r
              r <- r4
              let dk, r5 = ConfRng.intBelow deltaCount r
              r <- r5
              rng <- r

              let before = mkTable rows
              let after, edit = editOf ek rows i
              let pipeline = pipelineOf pk
              let delta = deltaOf dk before after
              let p = Incremental.plan pipeline

              let reference = DataFrame.evalPipeline pipeline after

              match Incremental.primeOn idw pipeline before with
              | Error _ ->
                  // The base itself does not evaluate; there is no incremental claim to make, and
                  // the sample is dropped rather than counted as a pass.
                  ()
              | Ok primed ->
                  let refreshed = Incremental.refreshOn idw pipeline primed delta after

                  let full =
                      match Incremental.primeOn idw pipeline after with
                      | Ok s -> Incremental.footprint s
                      | Error _ -> Incremental.footprint primed

                  yield
                      { Seed = seed
                        Iteration = i
                        Pipeline = pipeline
                        Strategy = p.Strategy
                        Prime = Incremental.footprint primed
                        Full = full
                        Refresh =
                          (match refreshed with
                           | Ok s -> Incremental.footprint s
                           | Error _ -> Incremental.footprint primed)
                        Equivalent = (refreshed |> Result.map Incremental.result) = reference
                        PrimeEquivalent = (Ok(Incremental.result primed) = DataFrame.evalPipeline pipeline before)
                        Edit = edit } ]

    /// The table width this family's laws need, and the reason the number is what it is.
    ///
    /// The tie-heavy sort key `b` is drawn from three values, so a table has to hold more than twice
    /// that before an arrival-position tiebreak decides anything more than once — which is the only
    /// way the merged-order laws can see a stability defect at all. Phase 115 measured the cost of
    /// getting this wrong: at one-to-five rows most tables held ONE row, and a merge with no
    /// stability tiebreak passed every seed.
    let internal rowsTheLawsNeed = 7

    /// The shipped bound: one to nine rows (Phase 115).
    let samples (seed: int) (iterations: int) : IncrementalSample list = samplesWith 9 seed iterations

    /// The columns an expression NAMES. The (producer × consumer) census cannot be read off a
    /// step's shape alone: a row-local step is a consumer of a cross-row column only when its
    /// expression names that column, and `Filter` / `Derive` are the only two steps that consult the
    /// per-row cache at all.
    let rec private colsNamed (e: ColExpr) : string list =
        match e with
        | Col c -> [ c ]
        | Lit _
        | Param _
        | Now _ -> []
        | Binary(_, x, y) -> colsNamed x @ colsNamed y
        | Not x
        | IsNull x
        | Cast(_, x) -> colsNamed x
        | Coalesce xs -> xs |> List.collect colsNamed
        | Case(cs, e2) -> (cs |> List.collect (fun (c, v) -> colsNamed c @ colsNamed v)) @ colsNamed e2
        | ApplyFn(_, xs) -> xs |> List.collect colsNamed
        | InList(x, xs) -> colsNamed x @ (xs |> List.collect colsNamed)
        | InParam(x, _) -> colsNamed x

    /// Phase 212 — every cross-row PRODUCER class whose appended column a later ROW-LOCAL step
    /// READS: the shape whose absence let `v0.26.0` publish a wrong answer.
    ///
    /// It walks the pipeline carrying the set of columns whose value for row r depends on rows other
    /// than r, and follows that set through the two steps that move it: a `Project` carries a column
    /// forward under a NEW NAME, and a `Derive` over a tainted column produces another one. Both are
    /// load-bearing rather than thorough — a renamed column is the same column, so a demand that
    /// matched the window's own output name would report `40` and `41` as not reaching the class
    /// they exist for, and a guard that cannot see the shape it names is the failure mode this whole
    /// module was written to stop.
    ///
    /// A `GroupBy` clears the set: the group table's columns are the group's own aggregates, read
    /// through the group cache rather than the per-row one, and that cache's stability condition is
    /// "this group's aggregates were recomputed" — which is sound, and is why the census marks those
    /// cells reached-but-non-discriminating rather than giving them shapes.
    let private crossRowColumnsReadRowLocally (pipeline: Transform list) : string list =
        let rec go (tainted: (string * string) list) (acc: string list) (steps: Transform list) =
            match steps with
            | [] -> acc
            | Window spec :: rest ->
                let kind =
                    if DataFrame.windowFrameBounded spec.Fn then
                        "bounded-frame window read row-locally"
                    else
                        "partition-global window read row-locally"

                go ((spec.As, kind) :: (tainted |> List.filter (fun (c, _) -> c <> spec.As))) acc rest
            | Filter e :: rest ->
                let named = colsNamed e

                let hit =
                    tainted |> List.filter (fun (c, _) -> List.contains c named) |> List.map snd

                go tainted (acc @ hit) rest
            | Derive(n, e) :: rest ->
                let named = colsNamed e

                let hit =
                    tainted |> List.filter (fun (c, _) -> List.contains c named) |> List.map snd

                let kept = tainted |> List.filter (fun (c, _) -> c <> n)

                let tainted2 =
                    match hit with
                    | [] -> kept
                    | kind :: _ -> (n, kind) :: kept

                go tainted2 (acc @ hit) rest
            | Project pairs :: rest ->
                let carried =
                    pairs
                    |> List.choose (fun (src, out) ->
                        tainted |> List.tryPick (fun (c, k) -> if c = src then Some(out, k) else None))

                go carried acc rest
            | GroupBy _ :: rest -> go [] acc rest
            | _ :: rest -> go tainted acc rest

        go [] [] pipeline |> List.distinct

    /// What this family's sample must contain for its laws to have been tested — the verdicts the
    /// laws branch on, and the table width the order-sensitive ones read.
    let demands: AdequacyDemand<IncrementalSample> list =
        [ ReachesEvery(
              "refresh class",
              [ "declined"
                "row-restricted"
                "group-restricted"
                "merged-order-restricted"
                "window-restricted"
                "partition-global-window-restricted"
                "relation-filtered-restricted"
                "top-n-restricted"
                "group-tail-restricted" ],
              fun s ->
                  let restricted =
                      match s.Refresh.Recompute with
                      | RowsRecomputed _
                      | GroupsRecomputed _ -> true
                      | _ -> false

                  let steps = (Incremental.plan s.Pipeline).Steps

                  let carries f = steps |> List.exists f

                  let mergesOrder =
                      carries (function
                          | MergeOrder _ -> true
                          | _ -> false)

                  let framesWindow =
                      carries (function
                          | RecomputeFrame _ -> true
                          | _ -> false)

                  // `0.19.0` — the narrower demand, and the one that would go vacuous if the
                  // relaxation were reverted. "A window was restricted" is satisfied by a `lag`
                  // alone, which was already true at `0.18.0`; the claim this widening makes is
                  // about the PARTITION-GLOBAL family, so that is what the sample has to reach.
                  // Read off `DataFrame.windowFrameBounded`, which is no longer the seam's
                  // admission predicate but is still the true statement about which frames those
                  // are — the one place the two questions still meet.
                  let framesPartitionGlobalWindow =
                      s.Pipeline
                      |> List.exists (function
                          | Window spec -> not (DataFrame.windowFrameBounded spec.Fn)
                          | _ -> false)

                  let filtersByRelation =
                      carries (function
                          | FilterByRelation _ -> true
                          | _ -> false)

                  // Phase 207 — the demand that would go vacuous if the admission were reverted.
                  // `8` is in this corpus as a bare `limit 2 0` and was its DECLINE until this
                  // phase, so "a limit was drawn" is satisfied by a sample that fell back; what
                  // the admission claims is that a top-N refresh is RESTRICTED, and that is what
                  // the sample has to reach.
                  let truncatesOrder =
                      carries (function
                          | TruncateOrder _ -> true
                          | _ -> false)

                  // Phase 202 — the demand that would go vacuous if the admission were reverted.
                  // "A group was maintained" is `group-restricted` and was already reached at
                  // `0.26.1` by a group-by that ENDED its pipeline; what this widening claims is
                  // that the steps AFTER one are maintained too. So the class insists on a
                  // maintained group with at least one step following it — which is precisely the
                  // shape `plan` declined until this phase, and which nothing else in the corpus
                  // can satisfy by accident.
                  let carriesGroupTail =
                      let rec afterGroup =
                          function
                          | [] -> false
                          | MaintainGroups _ :: rest -> not (List.isEmpty rest)
                          | _ :: rest -> afterGroup rest

                      afterGroup steps

                  [ match s.Strategy with
                    | ReferenceOnly _ -> "declined"
                    | _ -> ()
                    match s.Refresh.Recompute with
                    | RowsRecomputed _ -> "row-restricted"
                    | GroupsRecomputed _ -> "group-restricted"
                    | _ -> ()
                    if mergesOrder && restricted then
                        "merged-order-restricted"
                    if framesWindow && restricted then
                        "window-restricted"
                    if framesPartitionGlobalWindow && restricted then
                        "partition-global-window-restricted"
                    if filtersByRelation && restricted then
                        "relation-filtered-restricted"
                    if truncatesOrder && restricted then
                        "top-n-restricted"
                    if carriesGroupTail && restricted then
                        "group-tail-restricted" ]
          )
          // Phase 212 — the demand that would go vacuous if the `Window` clause of the cache
          // condition were reverted, stated PER PRODUCER CLASS because that is the axis the defect
          // lives on: a bounded frame moves one neighbour's cell, a partition-global one moves every
          // cell in the partition, and an implementation can get the first right and the second
          // wrong. It is restricted-conditioned like every class above it — a declined or fully
          // recomputed refresh reaches the SHAPE without ever consulting the row cache, so counting
          // it would make the demand answerable by samples that cannot carry the defect.
          //
          // It is a dimension of its own rather than two more `refresh class` verdicts because it is
          // not a refresh class: the laws do not branch on it, and the pipeline SHAPE is what it
          // measures. Keeping it separate also keeps the 7% margin floor in the suite reading the
          // classes it was measured for.
          ReachesEvery(
              "cross-row column read",
              [ "partition-global window read row-locally"
                "bounded-frame window read row-locally" ],
              fun s ->
                  let restricted =
                      match s.Refresh.Recompute with
                      | RowsRecomputed _
                      | GroupsRecomputed _ -> true
                      | _ -> false

                  if restricted then
                      crossRowColumnsReadRowLocally s.Pipeline
                  else
                      []
          )
          Spans("source rows", rowsTheLawsNeed, fun s -> s.Prime.SourceRows) ]

    /// The recorded footprint lines — one per sample, counts only, so two hosts print the same
    /// report. This is the instrument: it is what shows the seam did less work, which an equality
    /// suite alone cannot say.
    let report (xs: IncrementalSample list) : string list =
        xs
        |> List.map (fun s ->
            let strategy =
                match s.Strategy with
                | RowLocal -> "rowLocal"
                | RowLocalThenGroups -> "rowLocal+groups"
                | ReferenceOnly r -> "reference(" + Incremental.reasonString r + ")"

            "seed="
            + string s.Seed
            + " iter="
            + string s.Iteration
            + " "
            + strategy
            + " edit="
            + s.Edit
            + " | prime: "
            + Incremental.footprintString s.Prime
            + " | full: "
            + Incremental.footprintString s.Full
            + " | refresh: "
            + Incremental.footprintString s.Refresh)

    // ---- the laws ----

    /// The incremental-evaluation equivalence laws (Phase 99):
    ///
    ///  - **oracle equivalence** — the incremental result equals the reference result (table for
    ///    table, error for error) for every generated (base, delta) pair;
    ///  - **priming is a full evaluation** — a primed state's result equals `evalPipeline` over the
    ///    base, so the seam's entry point is the reference answer and nothing else;
    ///  - **the declared boundary is honoured** — a pipeline `plan` declares `ReferenceOnly`
    ///    reports a `FullRecompute` footprint carrying that same reason (or `ReusedPrior`, which is
    ///    sound for any strategy when the source did not move), and an incrementalisable pipeline
    ///    given a well-formed identity delta does NOT report one;
    ///  - **a decline is reported by the REFRESH, never by the prime** (Phase 117) — priming a
    ///    declined pipeline reports `Primed`, because a prime avoids nothing whatever the plan says
    ///    and had no state to fall back from. A prime that reported the pipeline's declared decline
    ///    would be describing a fall-back that did not happen;
    ///  - **NO evaluation does more work than a full evaluation** (Phase 117 widened this from
    ///    "restricted" to every sample) — the measured rows-evaluated of a refresh, declined or
    ///    restricted, never exceeds what priming over the SAME (changed) source costs. The baseline
    ///    is the changed source deliberately: measuring against the BASE is wrong, because an append
    ///    makes the new source larger and the appended row legitimately passes through more steps
    ///    than the rows it joined. This is the claim an equality suite cannot make, and the one an
    ///    evaluator that quietly recomputed everything would fail. It could only be stated over the
    ///    restricted classes while a full evaluation was projected onto its SOURCE ROW COUNT and a
    ///    restricted one counted evaluations at steps: the two were not comparable, so a declined
    ///    refresh read as cheaper than the baseline it was measured against. One scale is what makes
    ///    the law total;
    ///  - **plan is pure and total** — every step is classified, recomputing agrees, and
    ///    `isIncremental` agrees with the strategy;
    /// and, alongside them, this family's **sample-adequacy demands** (Phase 121, `demands` above):
    /// every refresh class the laws branch on was reached, and the tables were wide enough for the
    /// order-sensitive laws to read. Both were hand-rolled coverage laws here until Phase 121 moved
    /// them onto the kit's shared guard — the boundary-coverage one since Phase 99, the merged-order
    /// one since Phase 115 — and the width demand did not exist at all, which is precisely how the
    /// family passed every seed over one-row tables.
    ///
    /// `rowBound` is the table-width lever, parameterised so the span demand has a go-red proof in
    /// the suite rather than in a session's memory; `laws` pins the shipped bound.
    let lawsWith (rowBound: int) (seed: int) (iterations: int) : LawResult list =
        let xs = samplesWith rowBound seed iterations

        let cite (s: IncrementalSample) (what: string) =
            "seed="
            + string s.Seed
            + " iter="
            + string s.Iteration
            + " edit="
            + s.Edit
            + ": "
            + what

        let equivalence =
            xs
            |> List.tryFind (fun s -> not s.Equivalent)
            |> Option.map (fun s -> cite s "incremental result <> reference result")

        let priming =
            xs
            |> List.tryFind (fun s -> not s.PrimeEquivalent)
            |> Option.map (fun s -> cite s "priming <> a full reference evaluation over the base")

        let mutable boundary = None
        let mutable primeClass = None
        let mutable work = None
        let mutable planPurity = None

        for s in xs do
            let p = Incremental.plan s.Pipeline

            if p <> Incremental.plan s.Pipeline && planPurity.IsNone then
                planPurity <- Some(cite s "plan is not a pure function of the pipeline")

            if List.length p.Steps <> List.length s.Pipeline && planPurity.IsNone then
                planPurity <- Some(cite s "plan did not classify every step")

            let agrees =
                match p.Strategy with
                | ReferenceOnly _ -> not (Incremental.isIncremental p)
                | RowLocal
                | RowLocalThenGroups -> Incremental.isIncremental p

            if not agrees && planPurity.IsNone then
                planPurity <- Some(cite s "isIncremental disagrees with the strategy")

            match p.Strategy, s.Refresh.Recompute with
            | ReferenceOnly r, FullRecompute(_, r2) ->
                if r <> r2 && boundary.IsNone then
                    boundary <- Some(cite s "a declined pipeline reported a different reason")
            | ReferenceOnly _, ReusedPrior ->
                // Sound for ANY strategy: the source is byte-identical and the pipeline and env
                // have not moved, so the prior result still stands. A declined verb is declined
                // because it cannot answer a CHANGE, not because it must be re-run when nothing
                // changed.
                ()
            | ReferenceOnly _, _ ->
                if boundary.IsNone then
                    boundary <- Some(cite s "a declined pipeline did not report a full evaluation")
            | _ -> ()

            // A PRIME never reports the pipeline's declared decline (Phase 117). Stated against
            // that specific reason rather than as "a prime is always `Primed`", because a prime
            // legitimately reports `RowIdentityUnusable` — a defect `plan` cannot see, whose only
            // channel is the footprint.
            match p.Strategy, s.Prime.Recompute with
            | ReferenceOnly r, FullRecompute(_, r2) when r = r2 ->
                if primeClass.IsNone then
                    primeClass <- Some(cite s "a declined pipeline's prime reported the decline instead of `Primed`")
            | _ -> ()

            // Every sample, declined or restricted: one scale, so the comparison is meaningful for
            // all of them rather than only for the classes that already counted the same unit.
            if
                Incremental.rowsEvaluated s.Refresh > Incremental.rowsEvaluated s.Full
                && work.IsNone
            then
                work <- Some(cite s "a refresh evaluated more rows than a full evaluation would")

        [ { Law = "every generated pair produced a sample (no base failed to evaluate)"
            Passed = List.length xs = iterations
            Counterexample =
              if List.length xs = iterations then
                  None
              else
                  Some(
                      "seed="
                      + string seed
                      + ": "
                      + string (List.length xs)
                      + " of "
                      + string iterations
                      + " samples"
                  ) }
          { Law = "incremental evaluation equals the reference evaluator for every (base, delta) pair"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "priming a state is a full reference evaluation"
            Passed = priming.IsNone
            Counterexample = priming }
          { Law = "a declined pipeline reports a full evaluation carrying its declared reason"
            Passed = boundary.IsNone
            Counterexample = boundary }
          { Law = "a declined pipeline's PRIME reports `Primed`, not the decline"
            Passed = primeClass.IsNone
            Counterexample = primeClass }
          { Law = "a refresh evaluates no more rows than a full evaluation would — declined or restricted"
            Passed = work.IsNone
            Counterexample = work }
          { Law = "plan is pure, total, and agrees with isIncremental"
            Passed = planPurity.IsNone
            Counterexample = planPurity } ]
        @ SampleAdequacy.check "IncrementalDelta" seed demands xs

    /// The incremental-evaluation equivalence laws at the shipped table-width bound.
    let laws (seed: int) (iterations: int) : LawResult list = lawsWith 9 seed iterations
