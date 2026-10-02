namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.DataFrame — the incremental `Transform` evaluation seam
//  (Phase 99). A pipeline evaluated against a DELTA (the Phase 98
//  representation) rather than from scratch: the rows a delta names are
//  re-evaluated, every other row's value is reused, and a step that cannot
//  answer a delta SAYS SO in the type rather than guessing.
//
//  Three properties are load-bearing.
//
//   * THE REFERENCE EVALUATOR STAYS THE ORACLE. Everything here is a
//     restriction of `DataFrame.evalPipeline`, never a second semantics: a
//     re-evaluated cell goes through `DataFrame.evalExprInRow`'s array twin
//     (`resolveExpr` once per step, `evalResolved` per row), a recomputed
//     aggregate through the evaluator's own streams (`DataFrame.GroupAgg`,
//     Phase 323) or `DataFrame.aggregateCells`, a derived column's type
//     through `DataFrame.derivedTyping` and `DataFrame.columnTypeBy` (Phase
//     338). One implementation, called on fewer
//     rows. The conformance family certifies the two results identical for
//     every (base, delta) pair, so a divergence is a failing law rather than a
//     wrong number in a consumer.
//   * THE BOUNDARY IS DECLARED AS DATA. `plan` classifies every step —
//     `PropagateRows`, `MergeOrder`, `RecomputeFrame`, `FilterByRelation`,
//     `MaintainGroups`, or `FallBack` with a typed reason — before any
//     evaluation happens, so a consumer can ASK whether a pipeline is
//     incrementalisable and see why it is not. A step whose output for one row
//     depends on rows the delta does not name and cannot recover (an unbounded
//     window frame, a whole-relation set op, a combining join, a limit) is not
//     approximated: the reference evaluator re-runs, and the footprint records
//     that it did.
//   * NEVER A WRONG ANSWER, ONLY A LARGER ONE. Every condition the incremental
//     path cannot honour — a schema that moved, an env that changed, a
//     pipeline that changed, a delta that says `FullRefresh`, an identity
//     witness that cannot key the source — degrades to a full evaluation with
//     the reason recorded. Degrading is always available and always correct,
//     which is what makes the seam safe to adopt one pipeline at a time.
//
//  THE FOOTPRINT IS ONE SCALE (Phase 117). `rowsEvaluated` counts row
//  evaluations AT STEPS in every case, a full evaluation included: the
//  reference evaluator reports its own count rather than being projected onto
//  the source row count, so a decline and a restricted refresh are two readings
//  of one instrument. Projecting the full case onto `SourceRows` charged a
//  multi-step pipeline for a single pass, which made the baseline read as the
//  cheaper answer — an instrument that reads backwards is worse than none, and
//  every measurement of this seam is taken through it.
//
//  A `Sort` is the first admitted step that is not row-local (Phase 115). It
//  computes nothing and moves everything: the delta names rows whose POSITION
//  may have changed, so the new order is the previous order with those rows
//  lifted out and merged back in under the reference's own comparator. The
//  saving it earns is not in the sorting — it is that the steps BEFORE the sort
//  stop re-evaluating every row, which is what a declined pipeline costs today.
//
//  Phase 120 admitted two more on the same argument, and the argument is worth
//  stating once for all three. NONE of them evaluates an expression, so none of
//  them costs anything on this seam's instrument in either path — what the
//  admission buys, every time, is that the steps BEFORE them stop re-evaluating
//  every row. A BOUNDED-FRAME `Window` (`lag` / `lead` / the rolling pair)
//  appends a column computed from a fixed neighbourhood of each row in its
//  partition's order, emitting the rows it was handed one for one; it is
//  recomputed over the walked frame through the reference's own window step,
//  because the frame of a row the delta did NOT name moves when its neighbour
//  moves. An unbounded frame reads the whole partition and stays declined, by
//  type, naming the function. A FILTERING `Join` (`semi` / `anti`) keeps or
//  drops each row on whether it matches a joined relation and emits the row it
//  kept unchanged — a `Filter` whose predicate reads a relation — so its
//  verdict is cached per row exactly as a filter's cell is, and reused only
//  while the relation's key index has not moved. A combining join fans a row
//  out across its matches and stays declined, by type, naming the kind.
//
//  Phase 207 admitted a `Limit` on the SAME argument once more, and it is the
//  argument's limiting case: the step computes nothing, evaluates nothing, and
//  keeps a slice of the rows it was handed in the order it was handed them. The
//  walk is already holding the reference's own frame at every step — that is the
//  invariant the maintained `GroupBy`, the type-inferring `Derive` and the
//  recomputed window frame all read — so the slice is one positional pass over
//  it, and a row outside the window leaves exactly as a filtered row leaves.
//  What the admission buys is again that the steps BEFORE it stop re-evaluating
//  every row, which on the shapes that motivated it (`Filter > Sort > Limit`, a
//  top-N board over a live table) is the whole of the pipeline's cost. There is
//  no condition on WHERE the limit sits or on WHICH order it reads, because
//  every step this walk admits preserves the reference's row set and order and
//  a step that does not declines the pipeline before the limit is reached — so
//  the second decline class the design anticipated is empty by construction, and
//  saying so is more honest than a predicate that cannot be false.
//
//  Three order-sensitivities are handled explicitly rather than assumed away,
//  because all three are silent when got wrong. A `Derive`d column's TYPE is
//  inferred from the whole column, so it is recomputed from the rows alive at
//  that step even when no cell moved. A group's aggregate depends on its
//  members' ORDER (`First` / `Last` outright, a float `Sum` in its last bits),
//  so a cached aggregate is reused only when the group's ordered member list is
//  unchanged — never merely because no row in it was named. And a stable sort
//  breaks ties by ARRIVAL position, so a cached ORDER is reused only for rows
//  that arrived in the same relative order as before — one condition, stated
//  three times, because reuse of order-sensitive state is the whole risk here
//  and `Delta.diff` reports a pure reordering as quiet.
//
//  The caller's one obligation: the delta must TRUTHFULLY describe the change
//  from the source the state was last evaluated against to the source now
//  passed in. `Delta.diff` produces exactly that. A delta that under-reports is
//  a lie about the data, and no evaluator can detect one without recomputing
//  the answer it was asked to avoid recomputing.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// Why an incremental refresh did not propagate the delta and evaluated in full instead.
/// Recoverable + enumerated (GP4/GP5): a fall-back is a normal outcome carrying its reason, never
/// an error and never silent.
type FallBackReason =
    /// A verb whose output for one row depends on rows the delta does not name — a sort or limit
    /// (order-dependent), a window, a pivot/unpivot, a whole-relation set op, or a join.
    | StepNotRowLocal of verb: string
    /// A step that WOULD be maintainable, sitting somewhere other than last.
    ///
    /// **RETAINED, and no longer produced by `plan` (Phase 202).** The premise was that a delta over
    /// the source rows says nothing about a delta over the group table. It said one thing: WHICH
    /// GROUPS MOVED — which is exactly what `MaintainGroups` already computes and records, and what
    /// the steps after the group-by need. A maintained group-by is now admitted at ANY position and
    /// the steps after it walk the group table, so nothing constructs this case. It is kept for the
    /// reason `WindowFrameUnbounded` below is: removing a case from a published DU breaks every
    /// consumer that matches on it, and `reasonString` still renders it, so a stored reason from
    /// `0.26.1` still reads.
    | AggregateStepNotLast of verb: string
    /// The delta is the top element — everything may have changed, so there is nothing to restrict.
    | DeltaIsFullRefresh
    /// The delta addresses rows by ordinal (the reserved identity-free scheme). A cache keyed by
    /// position is invalidated wholesale by any insert, so the seam declines rather than treating a
    /// positional delta as an identity delta.
    | OrdinalAddressing
    /// The source's schema moved between refreshes — a structural change, not a row change.
    | SourceSchemaMoved
    /// The evaluation env changed, so a cached per-row value is no longer that row's value.
    | EnvChanged
    /// The pipeline changed, so the cached values answer a different question.
    | PipelineChanged
    /// The identity witness could not key the source, or the delta's scheme is not the witness's —
    /// carries the delta layer's own defect.
    | RowIdentityUnusable of defect: DeltaDefect
    /// `0.23.0` — a step's scalar `Slot` still names a PARAMETER, so the step's static shape (which
    /// column it orders by, how many rows it keeps) is not known without an evaluation env. The
    /// walk declines rather than guessing: substitute the params (`Transform.substitute`) and the
    /// plan is computable again. Names the verb and the unresolved param.
    | UnresolvedSlotParam of verb: string * param: string
    /// Phase 120 — a `Window` whose frame is not bounded, named by its window function.
    ///
    /// **RETAINED, and no longer produced by `plan` (`0.19.0`).** Frame boundedness turned out not
    /// to be what admits a `Window` to the restricted walk: the admitted column is recomputed
    /// wholesale over the walked frame, which is correct for every window function, and what the
    /// walk actually needs is that the step PRESERVES THE ROW SET — which every member does. So
    /// every `Window` is `RecomputeFrame` now and nothing constructs this case. It is kept because
    /// removing a case from a published DU is a breaking change for every consumer that matches on
    /// it, and `reasonString` still renders it, so a stored reason from `0.18.0` still reads.
    | WindowFrameUnbounded of fn: string
    /// Phase 120 — a `Join` whose output rows are not its LEFT rows. A combining join (`inner` /
    /// `left`) fans one left row out across every right row it matches and appends the right
    /// schema, so one source row no longer corresponds to one output row; the right-outer kinds
    /// (`right` / `outer`) additionally emit a row for each right row NO left row matched, which is
    /// a function of the whole left relation. Names the kind, because the filtering joins (`semi` /
    /// `anti`) — which emit each left row at most once, unchanged — are admitted.
    | JoinNotRowPreserving of kind: string
    /// Phase 202 — a SECOND aggregating step in one pipeline. The first `GroupBy` is maintained and
    /// the steps after it walk the group table (which is why `AggregateStepNotLast` above is no
    /// longer produced); a second one would group THAT table, which needs a second level of
    /// row-to-group, ordered-membership and per-group-aggregate state, keyed by group token rather
    /// than by source-row token. The seam holds one level and declines the second by name rather
    /// than falling back silently.
    ///
    /// **Declared LAST, deliberately.** A case's declaration order is its `Tags` number, so
    /// inserting one beside the group-shaped reason above would retype every case after it — a
    /// second, gratuitous breakage beside the union-widening this already is (Phase 207's finding).
    | AggregateStepRepeated of verb: string

/// How ONE pipeline step responds to a delta — the per-node incrementality, declared as data.
type StepIncrementality =
    /// Row-local: the step's output for a row is a function of that row (and the env) alone, so a
    /// delta propagates straight through and only the named rows are re-evaluated. `Filter`,
    /// `Project`, `Derive`.
    | PropagateRows
    /// The step keeps maintainable state: a partition of the rows whose affected groups are
    /// recomputed and whose untouched groups are reused. Names the grouping keys and the aggregate
    /// output names, so a consumer can see what is maintained rather than infer it.
    | MaintainGroups of keys: string list * aggregates: string list
    /// The step reorders rows and computes none: a `Sort`. Its output for one row is that row's
    /// cells unchanged; what the delta moves is the row's POSITION, and a position is recoverable by
    /// merging the named rows into the order the previous evaluation already produced. Names the
    /// ordering keys, so a consumer can see what is maintained rather than infer it.
    ///
    /// A sort is therefore not row-local — `PropagateRows` would be a wrong answer to "does this
    /// step's output for a row depend only on that row" — and not a fall-back either.
    | MergeOrder of by: (string * SortDir) list
    /// Phase 120 — the step APPENDS a column computed over each row's partition in that partition's
    /// order, and emits the rows it was handed, in the order it was handed them, one for one — so a
    /// delta propagates through it exactly as it does through a `Sort`, and every step admitted
    /// after it reads what the reference would have handed it. Names the partition and ordering
    /// keys, so a consumer can see what the frame is scoped by rather than infer it.
    ///
    /// **Every window function is admitted (`0.19.0`), not only the bounded frames.** What the walk
    /// needs from a step is that it PRESERVES THE ROW SET — one row in, one row out, in input order,
    /// plus an appended column — and every member has that: a rank, a bucket and a running total
    /// append a column exactly as a `lag` does. Frame boundedness, which `0.18.0` used as the
    /// discriminator, describes a distinction this evaluator does not make, since the column is
    /// recomputed wholesale over the walked frame in either case (below). It remains a true
    /// statement about the frames — `DataFrame.windowFrameBounded` still answers it — and it is the
    /// line a LATER phase restricting the recompute to displaced rows would draw; it is not the line
    /// that admits a step to this walk.
    ///
    /// The appended column is recomputed over the walked frame through the reference's own
    /// `DataFrame.windowStep`; it is not read from a cache. That is not a shortcut but the honest
    /// accounting: a `Window` evaluates no expression, so it costs nothing on this seam's
    /// instrument in EITHER path (`DataFrame.evalPipelineWithInEnvCounted` charges only `Filter` and
    /// `Derive`), and knowing which rows a delta DISPLACED would mean recomputing the partitions and
    /// their orders anyway. What the admission buys is the same thing `MergeOrder` buys: the steps
    /// BEFORE it stop re-evaluating every row.
    ///
    /// That wholesale recompute is also why the widening is not a claim about cumulative aggregates:
    /// the seam does not say it can answer a `cumulSum` from a delta, it says the STEPS BEFORE the
    /// window stop re-evaluating every row while the column is recomputed as the reference computes
    /// it. That sentence was already true of a `lag`.
    | RecomputeFrame of partitionBy: string list * orderBy: (string * SortDir) list
    /// Phase 120 — the step keeps or drops each row on whether it matches a JOINED RELATION, and
    /// emits the row it kept unchanged: a filtering join (`Semi` / `Anti`). Its output for a row is
    /// that row's own cells; what it decides is the row's SURVIVAL, and that decision is a function
    /// of the row and of the joined relation alone — so a delta propagates through it exactly as it
    /// does through a `Filter`, and the verdict cached for a row the delta does not name is reusable
    /// whenever the joined relation's key index has not moved. Names the kind and the key pairs, so
    /// a consumer can see what is matched rather than infer it.
    ///
    /// `PropagateRows` would be a wrong answer: the step reads a relation the delta says nothing
    /// about, and reuse is conditioned on that relation as well as on the delta. `FallBack` would be
    /// a wrong answer too: it answers a delta perfectly well.
    | FilterByRelation of kind: string * on: (string * string) list
    /// Not incrementalisable — the reference evaluator answers this pipeline.
    | FallBack of reason: FallBackReason
    /// Phase 207 — the step KEEPS A SLICE of the rows it was handed, in the order it was handed
    /// them, and computes nothing: a `Limit`.
    ///
    /// **It is declared AFTER `FallBack` rather than beside the other admitted classes, and the
    /// position is the contract rather than the reading order.** A union case's declaration order
    /// is its `Tags` number, so inserting this one where it belongs thematically renumbered
    /// `FallBack` from `5` to `6` — which the Phase 183 surface classifier reports as a `retype`
    /// beside the `union-widening`, and which costs a consumer a second, entirely gratuitous
    /// breakage on a case that did not change. Appending costs a reader one paragraph; inserting
    /// costs every consumer that reads a tag. `FallBackReason` above is ordered the same way, for
    /// the same reason: `0.23.0`'s case sits before Phase 120's because that is when each arrived.
    ///
    /// Its output for a row is that row's cells unchanged;
    /// what it decides is the row's SURVIVAL, and that decision is a function of the row's POSITION
    /// in the frame at this step. The walk already holds that frame — the walked frame IS the
    /// reference's frame, which is the invariant every other admitted step reads — so the slice is a
    /// single positional pass over it and the rows outside the window leave exactly as a `Filter`'s
    /// dropped rows leave. Names the count and the offset, so a consumer can see what is kept rather
    /// than infer it.
    ///
    /// **It carries no position condition and no order condition, and the absence of both is a
    /// finding rather than an omission.** The obvious shape — admit a `Limit` only where the order
    /// it reads is one the seam maintains, and decline the rest by type — describes a distinction
    /// this walk cannot draw: EVERY step the walk admits (`PropagateRows`, `MergeOrder`,
    /// `RecomputeFrame`, `FilterByRelation`) preserves the reference's row set and the reference's
    /// order, and a step that does not is declined, which declines the whole pipeline before this
    /// one is reached. So a `Limit` the walk reaches is over a maintained order by construction,
    /// and a predicate saying so would be a branch that cannot be taken — the same call `0.19.0`
    /// made for `Window`, for the same reason. The one `Limit` that still declines is one whose
    /// count or offset is an unresolved `Slot.Param`: there is no static window to take, and it
    /// declines as `UnresolvedSlotParam "limit"` exactly as a `Sort` on a param key does.
    ///
    /// `PropagateRows` would be a wrong answer — the step's verdict for a row reads every row ahead
    /// of it in the frame, not that row alone. `FallBack` would be a wrong answer too: it answers a
    /// delta perfectly well. What the admission buys is what `MergeOrder` and `RecomputeFrame` buy —
    /// a `Limit` evaluates no expression, so it costs nothing on this seam's instrument in either
    /// path, and the saving is that the steps BEFORE it stop re-evaluating every row.
    | TruncateOrder of n: int * offset: int

/// The strategy a whole pipeline's classification induces.
type IncrementalStrategy =
    /// Every step propagates the delta: the row-local three, and `Sort`, whose order is merged
    /// rather than recomputed. (The name predates `Sort`'s admission and is kept: `isIncremental`
    /// and every law key off `ReferenceOnly`-versus-not, and a third case would change every
    /// consumer's match without carrying a new decision. `Steps` carries the per-step truth.)
    | RowLocal
    /// A row-local prefix followed by a final maintained `GroupBy`.
    | RowLocalThenGroups
    /// The reference evaluator, always — the pipeline contains a step that cannot answer a delta.
    | ReferenceOnly of reason: FallBackReason

/// A pipeline's incrementality, computed before any evaluation: one classification per step, plus
/// the strategy they induce. Pure and total — `plan` never fails and never evaluates anything.
type IncrementalPlan =
    { Steps: StepIncrementality list
      Strategy: IncrementalStrategy }

/// What one evaluation actually recomputed — the honest account of the work done.
///
/// Every case that did work carries `rowsEvaluated` in the SAME unit: one evaluation of one step's
/// expression against one row (Phase 117). A row that passes through three evaluating steps is
/// counted three times, a step that evaluates no expression — a `Sort`, a `GroupBy`, a `Project` —
/// contributes none, and a full evaluation is counted by the reference evaluator itself rather than
/// projected onto the source row count. One scale is what makes a decline and a restricted refresh
/// comparable at all.
type Recompute =
    /// The first evaluation: there was no prior state, so every row was evaluated. A pipeline the
    /// plan DECLINES primes to this too — a prime avoids nothing whatever the plan says, so there is
    /// no fall-back to report; the decline and its reason attach to a REFRESH, where the fall-back
    /// actually happens, and `Incremental.plan` is what a consumer asks beforehand.
    | Primed of rowsEvaluated: int
    /// The delta asserted that nothing changed and the source was unchanged; the prior result was
    /// returned as it stood.
    | ReusedPrior
    /// Only the delta's rows were re-evaluated; every other row's value came from the cache.
    | RowsRecomputed of rowsEvaluated: int
    /// Only the affected groups' aggregates were recomputed, over the re-evaluated rows.
    | GroupsRecomputed of rowsEvaluated: int * groupsRecomputed: int
    /// The pipeline was evaluated in full, for the named reason — carrying what the reference
    /// evaluator itself evaluated, on the same scale as every other case.
    | FullRecompute of rowsEvaluated: int * reason: FallBackReason

/// The measured cost of one evaluation — the instrument a consumer records to show the incremental
/// path is doing less work than a full one, and the one the conformance family records beside each
/// (base, delta) pair. Counts only, no clock, so it is deterministic and identical on every host.
type RecomputeFootprint =
    {
        /// Rows in the source this evaluation ran against.
        SourceRows: int
        /// Rows in the result it produced.
        ResultRows: int
        /// What was recomputed to produce it.
        Recompute: Recompute
    }

/// The state an incremental evaluation carries between refreshes: the result, and the caches that
/// let the next delta be answered without revisiting the unchanged rows.
///
/// **The representation is PRIVATE (Phase 208), and that is the one deliberately breaking act of
/// this phase.** It was public until `0.27.0` because the columnar strand keeps its data
/// transparent — but the fields were always ENGINE-OWNED (a hand-built state whose caches disagree
/// with its source is a lie the evaluator cannot detect), and publishing them meant that every
/// change to HOW the caches are keyed was a breaking change. Three phases in a row (206, 207, 202)
/// each met that wall and each left the keying as it found it, and 202 measured that even a field's
/// POSITION in the record is published surface. The cost of the transparency was a refresh that
/// paid for the whole TABLE on every tick: the per-row caches were string-keyed persistent maps,
/// rebuilt from scratch, because that was the shape a consumer had been shown.
///
/// A consumer reads the state through `Incremental.result`, `Incremental.footprint`,
/// `Incremental.strategy`, `Incremental.plan'` and `Incremental.source` — what consumers actually
/// read, measured rather than guessed. The next representation change is a patch and not a release.
type IncrementalEval =
    private
        {
            /// The classification the state was built under.
            Plan: IncrementalPlan
            /// The pipeline it was built for — a refresh with a different pipeline evaluates in full.
            Pipeline: Transform list
            /// Phase 269 — the PLANNED form of `Pipeline` (`Plan.rewrite` over the source's schema):
            /// the pipeline the state's steps were classified over and its evaluation ran.
            Planned: Transform list
            /// The evaluation env it was built under — a refresh with a different env evaluates in
            /// full.
            Env: Map<string, Cell>
            /// The identity scheme its row tokens were minted under.
            Scheme: string
            /// The source it was last evaluated against. Lazy since Phase 268: a chunked refresh
            /// hands back a version whose table is built from its chunks the first time a reader
            /// asks, and a refresh that only compares chunks never asks.
            Source: Lazy<Table>
            /// Phase 267 — the prepared form of `Source`, where the state was primed over one
            /// (`primePrepared`): the frame the reference path evaluates over, held so a refresh
            /// whose source IS that table again (the same object) pays the boundary no second time.
            /// `None` on a state primed over a bare table.
            Prepared: Prepared option
            /// The pipeline's result over that source — lazy for the same reason `Source` is.
            Output: Lazy<Table>
            /// Phase 208 — every source row's identity token, in the SOURCE's own row order. It is
            /// what makes the arrays below positional: a row still sitting at the index it
            /// sat at last time is recognised by one pointer comparison, and only a row that moved
            /// costs a lookup. Empty on a state the reference path built (which caches nothing).
            Tokens: string[]
            /// Phase 274 — the cells the row-local steps evaluated, one array per evaluating step
            /// in step order, each aligned with `Tokens`; `null` where the row did not reach that
            /// step. A row a `Filter` dropped keeps the prefix it reached, so the drop verdict is
            /// cached too.
            ///
            /// Column-major since `0.35.0`. It was one `Cell list` per row (an array of them since
            /// `0.27.0`, a `Map<string, Cell list>` before that), which a refresh rebuilt row by row;
            /// a step's cells are now one array the walk writes once.
            StepCells: Cell[][]
            /// Phase 208 — aligned with `Tokens`: the index into `GroupOrder` of the group that
            /// row's cells landed in, or `-1` for a row that no longer reached the grouping (it was
            /// filtered away); empty on every pipeline without a maintained `GroupBy`. It is what
            /// lets the next refresh CARRY a group identity rather than re-minting it: a group is a
            /// pure function of the key cells, so a row whose cells have not moved is in the group
            /// it was in. (The group's token until Phase 274; the index is what the next refresh
            /// reads without hashing.)
            RowGroups: int[]
            /// Aligned with `GroupOrder`: each group's members' row tokens IN ORDER — what makes
            /// reusing a cached aggregate safe for the order-sensitive aggregates (maintained-groups
            /// only). Positional since Phase 274; a map keyed by group token before, which a refresh
            /// rebuilt one `Map.add` per group.
            GroupMembers: string list[]
            /// Aligned with `GroupOrder`: the aggregate cells last computed for each group.
            GroupAggs: Cell list[]
            /// Per `Sort` step (indexed by its ordinal among the pipeline's sorts), the order the
            /// step's rows ARRIVED in and the order it PRODUCED, as slots of the frame the sort ran
            /// over (Phase 274: source slots in the prefix, `GroupOrder` slots in the tail). Both halves are needed
            /// and neither is redundant: the produced order is what a merge reuses, and the arrival
            /// order is the only thing that says the reuse is still valid — a stable sort breaks
            /// ties by arrival position, so a cached order whose unnamed rows arrived in a different
            /// order now would merge those ties the wrong way round. `Delta.diff` reports a pure
            /// reordering as quiet, so this cannot be inferred from the delta.
            SortOrders: Map<int, int[] * int[]>
            /// Per admitted `Join` step (indexed by its ordinal among the pipeline's admitted
            /// joins), the joined relation's KEY INDEX as of this evaluation: its rows' key cells,
            /// projected through the step's `on` pairs, in the relation's own row order (Phase 120).
            ///
            /// It is what says a cached match verdict is still valid. A filtering join's verdict for
            /// a row depends on the row AND on the relation, and the delta describes only the source
            /// — so a verdict reused because "the delta did not name this row" would answer a
            /// changed relation with the previous relation's answer. The key index is the whole of
            /// what the verdict reads, so a relation that moved in some other column legitimately
            /// keeps the reuse; one whose keys moved does not.
            JoinKeys: Map<int, Cell list list>
            /// What producing `Output` cost.
            Footprint: RecomputeFootprint
            /// Phase 202 — the maintained `GroupBy`'s group tokens, in the group table's order (the
            /// order the steps after it walked); empty when the pipeline has no maintained group-by.
            GroupOrder: string[]
            /// Phase 202 — the cells the tail steps evaluated, one array per evaluating step, each
            /// aligned with `GroupOrder` (column-major since Phase 274, a map of per-group lists
            /// before). The group-table twin of `StepCells`, and separate from it because the tail's
            /// step index restarts at 0 and the two are keyed by different token vocabularies — a
            /// source row's `Delta.refToken` and a group's `DataFrame.rowTokenString` — which must
            /// not be allowed to meet in one keyspace where a collision would silently answer a row
            /// with a group's cells.
            ///
            /// A group's cells are reusable exactly when that group's aggregates were REUSED rather
            /// than recomputed: its key cells and its aggregate cells are then byte-identical to the
            /// row the tail last read, so every tail step's output for it is too.
            TailCells: Cell[][]
            /// Phase 268 — the result as a chunked version, where the state was produced by the
            /// chunked path (`primePrepared` / `refreshPrepared` over a pipeline of `Derive`s): its
            /// output columns share every chunk the source did not move, and the next chunked
            /// refresh reuses them by identity. `None` on a state the row-local walk or the
            /// reference path built.
            ChunkedOutput: Prepared option
            /// Phase 268 — how many chunks the chunked path evaluated to produce this state, or
            /// `None` where it was not the path taken. The instrument `IncrementalRefreshCostTests`
            /// reads: a one-cell edit is one chunk, whatever the row count.
            ChunksTouched: int option
            /// Phase 323 — per source schema column, whether its cell list was exactly the source's
            /// row count (unpadded, uncut), as the walk found it; empty where no walk ran.
            SourceExact: bool[]
            /// Phase 323 — the group table's rows (key cells, then aggregate cells) as the
            /// maintained `GroupBy` last computed them, aligned with `GroupOrder`; empty otherwise.
            GroupRows: Cell[][]
        }

/// The incremental evaluation seam: classify a pipeline, prime a state over a source, then refresh
/// that state against a delta. Every result equals `DataFrame.evalPipelineWithInEnv` over the same
/// source — certified, not asserted.
[<RequireQualifiedAccess>]
module Incremental =

    /// `List.map` over an option-returning projection, short-circuiting to `None` — used to read a
    /// list of scalar slots as literals, or decline the whole list when any is still a param.
    let private mapM (f: 'a -> 'b option) (xs: 'a list) : 'b list option =
        (Some [], xs)
        ||> List.fold (fun acc x ->
            match acc, f x with
            | Some vs, Some v -> Some(v :: vs)
            | _ -> None)
        |> Option.map List.rev

    // ---- classification (pure, total, no evaluation) ----

    /// The stable verb name of a step — what a `FallBackReason` names, and what a consumer prints.
    let internal verbName (t: Transform) : string =
        match t with
        | Filter _ -> "filter"
        | Project _ -> "project"
        | Derive _ -> "derive"
        | GroupBy _ -> "groupBy"
        | Join _ -> "join"
        | Window _ -> "window"
        | Pivot _ -> "pivot"
        | Unpivot _ -> "unpivot"
        | Sort _ -> "sort"
        | Distinct -> "distinct"
        | Limit _ -> "limit"
        | Union _ -> "union"
        | Intersect _ -> "intersect"
        | Except _ -> "except"

    /// The stable name of a window function — the spelling `WindowFrameUnbounded` carries (Phase
    /// 120). These are the canonical wire tags, spelled here for the same reason `verbName` spells
    /// the verbs: the codec that also knows them is compiled after this module, and a
    /// `FallBackReason` a consumer prints must not depend on which of the two it happened to reach.
    ///
    /// Retained on the same terms as the reason case it names (`0.19.0`): nothing here constructs
    /// that reason any more, and this is still the published spelling for a `0.18.0` record and for
    /// a consumer printing a window function of its own.
    let windowFnName (fn: WindowFn) : string =
        match fn with
        | RowNumber -> "rowNumber"
        | Rank -> "rank"
        | Lag -> "lag"
        | Lead -> "lead"
        | CumulSum -> "cumulSum"
        | RollingMean -> "rollingMean"
        | DenseRank -> "denseRank"
        | CompetitionRank -> "competitionRank"
        | NTile _ -> "ntile"
        | CumulMax -> "cumulMax"
        | CumulMin -> "cumulMin"
        | RollingSum -> "rollingSum"

    /// The stable name of a join kind — what `JoinNotRowPreserving` and `FilterByRelation` name
    /// (Phase 120). The canonical wire tags, on the same terms as `windowFnName`.
    let joinKindName (how: JoinKind) : string =
        match how with
        | Inner -> "inner"
        | Left -> "left"
        | Right -> "right"
        | Outer -> "outer"
        | Semi -> "semi"
        | Anti -> "anti"

    /// Classify one step. `isLast` matters only for the maintainable verbs: a `GroupBy` at the end
    /// of a pipeline has maintainable state, the same `GroupBy` in the middle does not, because
    /// what follows it would need a delta over the GROUP table and no such delta was supplied.
    ///
    /// A `Sort` carries no such position condition, which is why it takes none. It emits the rows it
    /// was handed, reordered, so every step admitted after it — the row-local three, and a final
    /// `GroupBy` — reads the order it produced exactly as it would have read the reference's, and
    /// the two order-sensitive readers in the seam (a `Derive`d column's whole-column type inference
    /// and a group's ordered member list) are computed from the walked frame rather than a cache.
    /// Classify ONE step. `isFirstAggregate` is true for the pipeline's first aggregating step and
    /// false for every later one — position among the aggregates, not position in the pipeline.
    ///
    /// Phase 202 changed what this parameter MEANS: it was `isLast`, because a maintained group-by
    /// had to be the pipeline's last step. It no longer does, so the question a `GroupBy` is asked
    /// is whether it is the one the seam maintains.
    let internal classifyStep (isFirstAggregate: bool) (t: Transform) : StepIncrementality =
        match t with
        | Filter _
        | Project _
        | Derive _ -> PropagateRows
        | Sort by ->
            // `0.23.0` — the merge needs the literal key columns. An unresolved slot param means
            // there is no static ordering to merge against, so this declines by name.
            match by |> mapM (fun (c, d) -> Slot.tryLit c |> Option.map (fun n -> n, d)) with
            | Some resolved -> MergeOrder resolved
            | None ->
                let p = by |> List.pick (fun (c, _) -> Slot.paramName c |> List.tryHead)

                FallBack(UnresolvedSlotParam("sort", p))
        // Phase 120, relaxed in `0.19.0` — EVERY window is admitted, at any position, on the same
        // argument a `Sort` is: the step emits the rows it was handed, in the order it was handed
        // them, so every step after it reads what the reference would have handed it. There is no
        // predicate here because there is no distinction to draw — a predicate that is constantly
        // true would be a branch that cannot be taken, and `WindowFrameUnbounded` is retained
        // (above) rather than produced.
        | Window spec -> RecomputeFrame(spec.PartitionBy, spec.OrderBy)
        // Phase 120 — the FILTERING joins emit each left row at most once and unchanged, which is a
        // `Filter` whose predicate reads a relation. The combining ones do not: they fan a left row
        // out across its matches and append the right schema, and the right-outer pair emits rows
        // no left row produced at all.
        | Join(_, on, how) ->
            match how with
            | Semi
            | Anti -> FilterByRelation(joinKindName how, on)
            | Inner
            | Left
            | Right
            | Outer -> FallBack(JoinNotRowPreserving(joinKindName how))
        // Phase 202 — a `GroupBy` at ANY position, maintained exactly as it was when it had to be
        // last. What made the old restriction look necessary was reading the delta as describing
        // only source ROWS; but `MaintainGroups` already computes, and the state already records,
        // which GROUPS a delta touched — so the group table has a delta of its own, and the steps
        // after the group-by are the same restricted walk one frame along. The condition is on the
        // step's position among the AGGREGATES, not in the pipeline.
        | GroupBy(keys, aggs) ->
            if isFirstAggregate then
                MaintainGroups(keys, aggs |> List.map (fun a -> a.Name))
            else
                FallBack(AggregateStepRepeated(verbName t))
        // Phase 207 — a `Limit`, at ANY position, over ANY order the walk produced. There is no
        // predicate here for the reason there is none on `Window`: every step this walk admits
        // preserves the reference's row set and order, so the frame a `Limit` slices is the
        // reference's frame, and a condition saying so could never be false where it is asked.
        // What DOES decline is a window that is not statically known — the `0.23.0` rule a `Sort`
        // on a param key already follows, reported against the same reason and in the same slot
        // order `Transform.paramsOf` reports a limit's params in (count, then offset).
        | Limit(n, offset) ->
            match Slot.tryLit n, Slot.tryLit offset with
            | Some count, Some off -> TruncateOrder(count, off)
            | _ ->
                let p = (Slot.paramName n @ Slot.paramName offset) |> List.head

                FallBack(UnresolvedSlotParam("limit", p))
        | other -> FallBack(StepNotRowLocal(verbName other))

    /// Classify a whole pipeline. An empty pipeline is `RowLocal` (the identity is trivially
    /// row-local). The FIRST fall-back reason in step order is the pipeline's reason — reporting
    /// the first is what keeps the answer stable as earlier steps are fixed.
    ///
    /// Classifies the pipeline AS GIVEN. Since Phase 269 the seam evaluates the PLANNED form
    /// (`Plan.rewrite` over the source's schema) and classifies that, so a state's `plan'` is the
    /// classification of `plannedOf`, not of the pipeline as written; `planOver` answers the
    /// seam's question from a schema and a pipeline, and says which form it classified.
    let plan (pipeline: Transform list) : IncrementalPlan =
        // Phase 202 — the flag each step is classified under is "is this the FIRST aggregating
        // step", carried by a fold rather than computed from the index: only a `GroupBy` consumes
        // it, and only the first one gets it.
        let steps =
            ((true, []), pipeline)
            ||> List.fold (fun (firstAggAvailable, acc) t ->
                let isAgg =
                    match t with
                    | GroupBy _ -> true
                    | _ -> false

                let step = classifyStep firstAggAvailable t

                (firstAggAvailable && not isAgg), step :: acc)
            |> snd
            |> List.rev

        let firstFallBack =
            steps
            |> List.tryPick (function
                | FallBack r -> Some r
                | _ -> None)

        let strategy =
            match firstFallBack with
            | Some r -> ReferenceOnly r
            | None ->
                if
                    steps
                    |> List.exists (function
                        | MaintainGroups _ -> true
                        | _ -> false)
                then
                    RowLocalThenGroups
                else
                    RowLocal

        { Steps = steps; Strategy = strategy }

    /// Phase 269 — the classification the seam will run under, and the pipeline it will run: the
    /// PLANNED form of `pipeline` over a source of schema `cols`, classified by `plan`. The report
    /// says what was rewritten and what was declined, so a consumer can see why the seam's
    /// classification differs from `plan` over the written form, when it does.
    let planOver (cols: Schema) (pipeline: Transform list) : IncrementalPlan * PlanReport =
        let report = Plan.explain cols pipeline
        plan report.Planned, report

    /// Is this pipeline incrementalisable at all?
    let isIncremental (p: IncrementalPlan) : bool =
        match p.Strategy with
        | ReferenceOnly _ -> false
        | RowLocal
        | RowLocalThenGroups -> true

    // ---- footprint projections ----

    /// The row evaluations at steps this evaluation performed — ONE scale across every case
    /// (Phase 117), so a decline and a restricted refresh are comparable and `SourceRows` stays its
    /// own field. A full evaluation reports the reference evaluator's own count, not the source row
    /// count: projecting it onto `SourceRows` charged a multi-step pipeline for one pass and made
    /// the full baseline read as the cheaper answer.
    let rowsEvaluated (f: RecomputeFootprint) : int =
        match f.Recompute with
        | Primed n
        | RowsRecomputed n -> n
        | GroupsRecomputed(n, _) -> n
        | ReusedPrior -> 0
        | FullRecompute(n, _) -> n

    /// A stable human string for a fall-back reason.
    let reasonString (r: FallBackReason) : string =
        match r with
        | StepNotRowLocal v -> "'" + v + "' output depends on rows the delta does not name"
        | AggregateStepNotLast v -> "'" + v + "' is maintainable only as the pipeline's last step"
        | DeltaIsFullRefresh -> "the delta is a full refresh"
        | OrdinalAddressing -> "the delta addresses rows by ordinal (no identity to key a cache by)"
        | SourceSchemaMoved -> "the source schema moved"
        | EnvChanged -> "the evaluation env changed"
        | PipelineChanged -> "the pipeline changed"
        | RowIdentityUnusable d -> "the identity witness is unusable: " + Delta.defectString d
        | WindowFrameUnbounded fn ->
            "the window function '"
            + fn
            + "' reads the whole partition, not a bounded frame"
        | JoinNotRowPreserving kind -> "a '" + kind + "' join's output rows are not its left rows"
        | AggregateStepRepeated v ->
            "'"
            + v
            + "' is maintainable once per pipeline; a second one would group the group table"
        | UnresolvedSlotParam(verb, param) ->
            "'"
            + verb
            + "' names the unresolved slot param '"
            + param
            + "', so its static shape is not known without an env (substitute it and the plan is computable)"

    /// A stable human string for a footprint — counts only, so two hosts print the same line.
    let footprintString (f: RecomputeFootprint) : string =
        let what =
            match f.Recompute with
            | Primed n -> "primed, " + string n + " rows evaluated"
            | ReusedPrior -> "reused the prior result, 0 rows evaluated"
            | RowsRecomputed n -> string n + " rows re-evaluated"
            | GroupsRecomputed(n, g) -> string n + " rows re-evaluated, " + string g + " groups recomputed"
            | FullRecompute(n, r) -> "full evaluation, " + string n + " rows evaluated (" + reasonString r + ")"

        string f.SourceRows
        + " source rows -> "
        + string f.ResultRows
        + " result rows: "
        + what

    // ---- shared helpers ----

    let private colIndex (cols: Schema) (name: string) : int option =
        cols |> List.tryFindIndex (fun (n, _) -> n = name)

    let private colType (cols: Schema) (name: string) : ColumnType option =
        cols |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    let private available (cols: Schema) : string list = cols |> List.map fst

    /// A loop rather than a recursion through `Result.bind` (Phase 265), for the reason
    /// `DataFrame`'s own traverse gives: the maintained grouping traverses one element per group, and
    /// without tail calls the recursion spent a stack frame on each. Same order, same first error.
    let private traverse (f: 'a -> Result<'b, EvalError>) (xs: 'a list) : Result<'b list, EvalError> =
        let mutable acc = []
        let mutable rest = xs
        let mutable failed = None

        while Option.isNone failed && not (List.isEmpty rest) do
            match f (List.head rest) with
            | Ok y ->
                acc <- y :: acc
                rest <- List.tail rest
            | Error e -> failed <- Some e

        match failed with
        | Some e -> Error e
        | None -> Ok(List.rev acc)

    // The seam's own transposes (Phase 206). These read and wrote the source column-by-column
    // through per-index list access, so the seam inherited the reference evaluator's quadratic even
    // when it evaluated a single row expression — and that is why a restricted refresh used to
    // finish AFTER the full evaluation it replaces. Same rows, same table, one traversal each.
    //
    // The rows are arrays (Phase 263), as the reference evaluator's frame's are: the walk reads a
    // cell by a column index it resolved once for the step, and appends or replaces one by copy.
    let private rowsOf (t: Table) : Cell[] list = RowAccess.rows t

    let private tableOf (cols: Schema) (rows: Cell[] list) : Table =
        { Schema = cols
          Columns = RowAccess.toColumns cols rows }

    // ---- the row-local walk ----

    /// The propagating verbs, as the closed shape the walk consumes. Splitting the pipeline into
    /// this form (rather than re-matching `Transform` inside the walk) is what removes the
    /// "unreachable case" a catch-all would otherwise need: a step that is not one of these
    /// never reaches the walk, by construction.
    ///
    /// `WSort` and `WJoin` carry their ORDINAL among the pipeline's sorts / admitted joins, which is
    /// what keys the cached order and the cached key index in the state. An ordinal is enough
    /// because `refresh` refuses a pipeline that is not the one the state was built for
    /// (`PipelineChanged`), so the numbering a state was written under is the numbering it is read
    /// under.
    ///
    /// `WJoin` carries `keepMatched` rather than the `JoinKind` itself: `Semi` and `Anti` differ in
    /// exactly that bit, and the walk should not be able to be handed a kind it cannot answer.
    type private PrefixStep =
        | WFilter of ColExpr
        | WProject of (string * string) list
        | WDerive of string * ColExpr
        | WSort of int * (string * SortDir) list
        | WWindow of WindowSpec
        | WJoin of int * DataSource * (string * string) list * bool
        /// Phase 207 — a `Limit`, carrying its already-resolved count and offset. It keys no cache
        /// and takes no ordinal: the window it keeps is read off the frame the walk is holding, so
        /// there is nothing about it for a prior evaluation to have recorded.
        | WLimit of n: int * offset: int

    /// Split a pipeline into its propagating prefix and, when it has one, the maintained `GroupBy`
    /// plus the steps that follow it. `None` when the pipeline is not of the incremental shape —
    /// exactly when `plan` says `ReferenceOnly`.
    ///
    /// Phase 202 — the third component is the TAIL, and it is a `PrefixStep list` because it is
    /// literally the same walk over a different frame: the group table rather than the source rows.
    /// The `sorts` and `joins` ordinals CONTINUE through it rather than restarting, which is what
    /// lets the tail's cached orders and key indexes live in the state's existing `SortOrders` /
    /// `JoinKeys` maps with no second keyspace to keep disjoint. (The per-row CELL caches cannot
    /// share that way — see `IncrementalEval.TailCells`.)
    ///
    /// **A collision would be SAFE today, and the continuation is not what makes it so — measured,
    /// because the first version of this comment claimed the opposite.** Restarting the tail's
    /// ordinals at 0 passes every test in `IncrementalGroupByTests`, and it has to: the order-reuse
    /// condition in `walk`'s `WSort` requires the cached ARRIVAL list, filtered to the current
    /// step's unaffected tokens, to equal the current arrival list filtered the same way — and the
    /// two steps' token vocabularies are disjoint by construction (`Delta.refToken` renders `k:` /
    /// `o:`, `DataFrame.rowTokenString` a length prefix, so a digit), which leaves that condition
    /// satisfiable only when BOTH sides are empty, where the merge degenerates to a full sort. So a
    /// collision costs each sort its reuse and never its correctness.
    ///
    /// The continuation is kept for the two reasons that survive that: a cache silently disabled by
    /// another step's writes is a performance defect nothing would report, and a correctness
    /// argument that rests on two token formats never coinciding is one an unrelated change to
    /// either format can retire without noticing. Keying them apart costs one threaded counter.
    let private split
        (pipeline: Transform list)
        : (PrefixStep list * (string list * Agg list * PrefixStep list) option) option =
        let rec go acc sorts joins =
            function
            | [] -> Some(List.rev acc, None)
            | GroupBy(keys, aggs) :: rest ->
                // The tail is parsed by the same walker, seeded with the ordinals the prefix
                // reached. A SECOND `GroupBy` in `rest` falls to this function's `| _ -> None`
                // through its own recursion, which is the `AggregateStepRepeated` decline `plan`
                // reports — the two agree by construction rather than by two copies of the rule.
                go [] sorts joins rest
                |> Option.bind (fun (tail, inner) ->
                    match inner with
                    | Some _ -> None
                    | None -> Some(List.rev acc, Some(keys, aggs, tail)))
            | Filter p :: rest -> go (WFilter p :: acc) sorts joins rest
            | Project pairs :: rest -> go (WProject pairs :: acc) sorts joins rest
            | Derive(n, e) :: rest -> go (WDerive(n, e) :: acc) sorts joins rest
            // `0.23.0` — an unresolved slot param has no static ordering, so the walk refuses the
            // pipeline outright (`None`) and the seam falls back to the reference evaluator, which
            // is where the `UnboundParam` will honestly surface.
            | Sort by :: rest ->
                match by |> mapM (fun (c, d) -> Slot.tryLit c |> Option.map (fun n -> n, d)) with
                | None -> None
                | Some resolved -> go (WSort(sorts, resolved) :: acc) (sorts + 1) joins rest
            | Window spec :: rest -> go (WWindow spec :: acc) sorts joins rest
            // Phase 207 — same shape as the `Sort` case above and for the same reason: an
            // unresolved slot param has no static window, so the walk refuses the pipeline outright
            // and the seam falls back to the reference evaluator, which is where the `UnboundParam`
            // will honestly surface.
            | Limit(n, offset) :: rest ->
                match Slot.tryLit n, Slot.tryLit offset with
                | Some count, Some off -> go (WLimit(count, off) :: acc) sorts joins rest
                | _ -> None
            | Join(src, on, Semi) :: rest -> go (WJoin(joins, src, on, true) :: acc) sorts (joins + 1) rest
            | Join(src, on, Anti) :: rest -> go (WJoin(joins, src, on, false) :: acc) sorts (joins + 1) rest
            | _ -> None

        go [] 0 0 pipeline

    /// Phase 274 — the frame the walk holds, COLUMN-major. It was a list of per-row `Work` records
    /// until `0.35.0`: every step rebuilt one record per row, a `Derive` copied every row's array to
    /// append one cell, and the rows were transposed in from the source and out into the result, so
    /// a refresh that re-evaluated one row paid three allocations per row per step to say so. The
    /// cost the gate measured was that bookkeeping, not the evaluation it saves (11 to 15 full
    /// evaluations at 20,000 to 100,000 rows on .NET for a two-`Derive` pipeline).
    ///
    /// A column is an array indexed by SLOT — the row's index in the frame the walk started from
    /// (the source for the prefix, the group table for the tail) — and `Order` is the slots alive at
    /// this step, in the order the reference frame holds them. So `Filter` and `Limit` write a new
    /// `Order`, `Sort` permutes it, `Project` permutes the column arrays, and `Derive` adds ONE array.
    /// A slot outside `Order` is dead at this step: its cells are never read again this walk.
    ///
    /// `Origins` co-indexes with `Data`: the source `Cell list` an array was unpacked from, while the
    /// column is still exactly that list, unpadded — the frame's own twin of the reference frame's
    /// `Origins`, and for the same reason: an output column the walk did not touch, over a frame whose
    /// `Order` is still every slot in slot order, IS the consumer's list, and handing it back costs
    /// nothing. No array in a frame is ever written after the step that made it; a step that changes
    /// a column makes a new array.
    type private WalkFrame =
        { Cols: Schema
          Data: Cell[][]
          Origins: Cell list option[]
          Order: int[] }

    /// Phase 274 — the per-slot facts the walk reads beside the frame, and the cells it records. The
    /// arrays are owned by one evaluation and never escape into the state except `Steps`, which is
    /// written once per evaluating step and not touched after.
    ///
    /// `Stable` is the successor of the per-row flag of the same name (Phase 208): the slot's cells
    /// are byte-identical to the ones the prior evaluation held for it AT THIS POINT in the
    /// pipeline, so anything the prior evaluation computed FROM them is still that computation's
    /// answer. It starts as "the delta did not name the row and the prior evaluation held it", and
    /// every admitted step preserves it except `Window`, which CLEARS IT FOR EVERY SLOT: it
    /// recomputes its column over the whole frame, so a row that did not move can still get a
    /// different cell when another row did. That clause is a correctness fix (Phase 208; the
    /// go-red cases are in `IncrementalRefreshCostTests`), and so is reading `Stable` rather than
    /// "the delta named it" at every reuse site (Phase 215; the per-site census is in
    /// `docs/incremental-evaluation.md`). There is still no field saying "the delta named this row":
    /// it is the discredited condition, and a field nobody should read is how a site comes to read it.
    ///
    /// `Prior` is where each slot's cached cells are read from — the slot the row occupied in the
    /// PRIOR evaluation's frame, `-1` when that frame did not hold it — and `PriorSteps` is that
    /// evaluation's cells, one array per evaluating step, indexed by ITS slots (`null` where the row
    /// did not reach the step). `Steps` accumulates this evaluation's, one array per evaluating step,
    /// indexed by this frame's slots: the walk's `evalIdx` is always `Steps.Count`.
    type private WalkRows =
        {
            Tokens: string[]
            Prior: int[]
            Stable: bool[]
            PriorSteps: Cell[][]
            PriorCount: int
            Steps: ResizeArray<Cell[]>
            /// Phase 323 — when not `null`, every slot holds the row the PRIOR evaluation held at that
            /// slot (the delta found every row in place and no prefix step reorders), the slots not
            /// listed here are `Stable`, and these are the changed slots, ascending. A step may then
            /// start from the prior step's cells and evaluate only these.
            InPlace: int[]
        }

    /// The value the prior evaluation computed at evaluating step `evalIdx` for the row now at slot
    /// `s`, where it may still be reused — the row is `Stable` and the prior evaluation reached that
    /// step for it — or `null`. The one reuse test every evaluating step makes, so the condition has
    /// one statement.
    let private cachedAt (r: WalkRows) (evalIdx: int) (s: int) : Cell =
        if not r.Stable[s] || evalIdx >= r.PriorSteps.Length then
            Unchecked.defaultof<Cell>
        else
            let p = r.Prior[s]
            let a = r.PriorSteps[evalIdx]

            if p >= 0 && p < a.Length then
                a[p]
            else
                Unchecked.defaultof<Cell>

    /// The column array at `c`. A source column the frame was built over is unpacked from its list
    /// the first time a step reads it (Phase 274): a column no step reads — an identity or a label
    /// carried through to the result — is never unpacked at all, and the result hands its list back.
    /// The write is a memo of the array the list already determines, never a change of contents.
    let private column (f: WalkFrame) (c: int) : Cell[] =
        let a = f.Data[c]

        if not (isNull a) then
            a
        else
            let unpacked = List.toArray f.Origins[c].Value
            f.Data[c] <- unpacked
            unpacked

    /// The columns an expression reads, each once.
    let private columnsRead (e: DataFrame.ResolvedExpr) : int[] =
        let seen = System.Collections.Generic.HashSet<int>()

        let rec go e =
            match e with
            | DataFrame.RCol i -> seen.Add i |> ignore
            | DataFrame.RConst _
            | DataFrame.RFail _ -> ()
            | DataFrame.RBinary(_, a, b)
            | DataFrame.RQuotient(_, _, a, b) ->
                go a
                go b
            | DataFrame.RNot a
            | DataFrame.RCast(_, a)
            | DataFrame.RIsNull a
            | DataFrame.RRounded(_, _, a) -> go a
            | DataFrame.RCoalesce xs
            | DataFrame.RApplyFn(_, xs) -> List.iter go xs
            | DataFrame.RCase(arms, otherwise) ->
                arms
                |> List.iter (fun (w, t) ->
                    go w
                    go t)

                go otherwise
            | DataFrame.RInList(a, xs) ->
                go a
                List.iter go xs

        go e
        Seq.toArray seen

    /// The row at slot `s` written into `into` at the columns `cols` — the array form
    /// `DataFrame.evalResolved` reads. A scratch array per step, refilled per evaluated row at just
    /// the columns the step's expression reads: the evaluator reads cells out of it and keeps none of
    /// it, so a row costs no allocation of its own.
    /// Phase 323 — how many changed rows an in-place step reads one cell at a time from a source
    /// column's list (each read walks the list to the row) before unpacking the column whole is the
    /// cheaper reading.
    [<Literal>]
    let private sparseRowLimit = 16

    /// Does the ASCENDING order `order` hold slot `s`? A binary search, written out: Fable maps no
    /// `System.Array.BinarySearch` (Phase 323).
    let private reaches (order: int[]) (s: int) : bool =
        let mutable lo = 0
        let mutable hi = order.Length - 1
        let mutable found = false

        while not found && lo <= hi do
            let mid = lo + (hi - lo) / 2
            let v = order[mid]

            if v = s then found <- true
            elif v < s then lo <- mid + 1
            else hi <- mid - 1

        found

    /// The cell at slot `s` of column `c`: from the column's array where it has one, else by walking
    /// the source list to it — one row's read, without unpacking the column.
    let private cellAtSlot (f: WalkFrame) (c: int) (s: int) : Cell =
        let a = f.Data[c]

        if not (isNull a) then
            a[s]
        else
            let mutable rest = f.Origins[c].Value
            let mutable i = 0

            while i < s do
                rest <- rest.Tail
                i <- i + 1

            rest.Head

    let private fillRow (f: WalkFrame) (cols: int[]) (s: int) (into: Cell[]) =
        for c in cols do
            into[c] <- (column f c)[s]

    /// Per prior slot, the slot that row holds now (`-1` where it is gone) — the inverse of
    /// `WalkRows.Prior`, which is what translates an order the prior evaluation recorded in its own
    /// numbering into this one.
    let private currentOf (r: WalkRows) : int[] =
        let cur = Array.create r.PriorCount -1

        for s in 0 .. r.Prior.Length - 1 do
            let p = r.Prior[s]

            if p >= 0 && p < cur.Length then
                cur[p] <- s

        cur

    /// Merge two already-ordered slot sequences into one, under the reference comparator, breaking a
    /// tie by ARRIVAL position. That tiebreak is what makes the merge equal to a stable sort of the
    /// whole frame: a stable sort is exactly a sort by (key, arrival position). Dropping it would
    /// leave an order that is correctly sorted and differs from the reference's on the first tie.
    let private mergeOrders (cmp: int -> int -> int) (posOf: int[]) (xs: int[]) (ys: int[]) : int[] =
        // `x` goes out before `y` exactly when the linear merge would take it first.
        let before (x: int) (y: int) =
            let c = cmp x y
            if c <> 0 then c < 0 else posOf[x] < posOf[y]

        // Phase 274 — each `y` finds its place in `xs` by bisection, and the runs of `xs` between
        // are copied whole: `xs` is sorted under that same total order (it is the cached order of
        // rows whose keys and relative arrival have not moved), so "goes out before `y`" holds for a
        // prefix of it. A merge of m moved rows into n cached ones compares O(m log n) times, where
        // walking both lists compared up to n times to place one row.
        let out: int[] = Array.zeroCreate (xs.Length + ys.Length)
        let mutable lo = 0
        let mutable k = 0

        for y in ys do
            let mutable a = lo
            let mutable b = xs.Length

            while a < b do
                let m = (a + b) / 2

                if before xs[m] y then a <- m + 1 else b <- m

            Array.blit xs lo out k (a - lo)
            k <- k + (a - lo)
            lo <- a
            out[k] <- y
            k <- k + 1

        Array.blit xs lo out k (xs.Length - lo)
        out

    /// The per-step state the walk reads from the prior evaluation and writes for the next one,
    /// carried as one value rather than as a parameter per admitted step. It is the shape of every
    /// state-keeping step's cache: keyed by that step's ordinal in the pipeline, valid only while
    /// the pipeline is the one the state was built for.
    ///
    /// Phase 274 — a sort's two orders are SLOT arrays in the numbering of the frame the sort ran
    /// over (source slots in the prefix, group slots in the tail); `WalkRows.Prior` translates them.
    type private WalkCaches =
        { SortOrders: Map<int, int[] * int[]>
          JoinKeys: Map<int, Cell list list> }

    let private noCaches: WalkCaches =
        { SortOrders = Map.empty
          JoinKeys = Map.empty }

    /// One evaluating step's pass over the rows alive at it: the cached cell where `cachedAt` allows
    /// it, a fresh evaluation through the reference's own `DataFrame.evalResolved` otherwise (the
    /// expression resolved once for the step, Phase 263, so a re-evaluated cell is still the
    /// reference's cell). Returns the step's cells by slot and the running count of row
    /// evaluations — the footprint's unit of work, charged only for a cell actually evaluated.
    /// The first error in frame order is the answer, as it is the reference's.
    let private evalStep
        (r: WalkRows)
        (f: WalkFrame)
        (evalIdx: int)
        (resolved: DataFrame.ResolvedExpr)
        (evaluated: int)
        : Result<Cell[] * int, EvalError> =
        let scratch: Cell[] = Array.zeroCreate f.Data.Length
        let reads = columnsRead resolved
        let mutable n = evaluated
        let mutable failed = None
        let mutable k = 0

        // Phase 323 — in place, the step starts from the prior evaluation's cells for this very step
        // (every stable row's value, and `null` where the prior walk did not reach the row, which a
        // stable row's walk does not reach now either) and evaluates the changed rows alone. A
        // changed row the walk no longer reaches is cleared, as the full walk would leave it. The
        // changed rows are read cell by cell from the source's lists when they are few, so a column
        // the step reads is not unpacked whole to read one row of it.
        let inPlace =
            not (isNull r.InPlace)
            && evalIdx < r.PriorSteps.Length
            && r.PriorSteps[evalIdx].Length = r.Stable.Length

        let step: Cell[] =
            if inPlace then
                Array.copy r.PriorSteps[evalIdx]
            else
                Array.zeroCreate r.Stable.Length

        if inPlace then
            let sparse = r.InPlace.Length <= sparseRowLimit
            let mutable j = 0

            while failed.IsNone && j < r.InPlace.Length do
                let s = r.InPlace[j]

                if reaches f.Order s then
                    for c in reads do
                        scratch[c] <- if sparse then cellAtSlot f c s else (column f c)[s]

                    match DataFrame.evalResolved scratch resolved with
                    | Ok c ->
                        step[s] <- c
                        n <- n + 1
                    | Error e -> failed <- Some e
                else
                    step[s] <- Unchecked.defaultof<Cell>

                j <- j + 1

            k <- f.Order.Length

        while failed.IsNone && k < f.Order.Length do
            let s = f.Order[k]
            let cached = cachedAt r evalIdx s

            if not (isNull (box cached)) then
                step[s] <- cached
            else
                fillRow f reads s scratch

                match DataFrame.evalResolved scratch resolved with
                | Ok c ->
                    step[s] <- c
                    n <- n + 1
                | Error e -> failed <- Some e

            k <- k + 1

        match failed with
        | Some e -> Error e
        | None -> Ok(step, n)

    /// Walk the propagating prefix, threading the frame. Dead rows leave `Order` and take no further
    /// part — exactly as in the reference evaluator, which has already dropped them — while what
    /// they reached stays recorded in `Steps`, so their cached prefix survives for the next refresh.
    ///
    /// `prior` is the state's caches and is read-only; `caches` accumulates the ones this walk
    /// produced, which is what the next refresh will read.
    let rec private walk
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (prior: WalkCaches)
        (r: WalkRows)
        (f: WalkFrame)
        (evaluated: int)
        (caches: WalkCaches)
        (steps: PrefixStep list)
        : Result<WalkFrame * int * WalkCaches, EvalError> =
        let evalIdx = r.Steps.Count

        match steps with
        | [] -> Ok(f, evaluated, caches)
        | WSort(sortIdx, by) :: rest ->
            // The rows the sort orders, in the order they arrived.
            let arrival = f.Order
            let posOf: int[] = Array.zeroCreate r.Stable.Length

            for k in 0 .. arrival.Length - 1 do
                posOf[arrival[k]] <- k

            // The reference's own comparator, over the reference's own cells, keys resolved once for
            // the step (Phase 263). A second comparator would agree on every corpus anyone thought to
            // write and disagree on the first null, the first tie and the first misspelled key — so
            // the two rows are written into scratch arrays at the key columns only (the comparator
            // reads nothing else) and compared by `DataFrame.compareResolved` itself.
            let keys = DataFrame.resolveSortKeys f.Cols by
            let ra: Cell[] = Array.zeroCreate f.Data.Length
            let rb: Cell[] = Array.zeroCreate f.Data.Length

            let cmp (a: int) (b: int) =
                for (i, _) in keys do
                    let col = column f i
                    ra[i] <- col[a]
                    rb[i] <- col[b]

                DataFrame.compareResolved keys ra rb

            // A total order: the comparator, then arrival position — a stable sort by construction,
            // whichever algorithm the host's array sort is.
            let sortSlots (xs: int[]) =
                xs
                |> Array.sortWith (fun a b ->
                    let c = cmp a b
                    if c <> 0 then c else compare posOf[a] posOf[b])

            // **Phase 215 — the reuse condition is `Stable`.** A cached ORDER is a cached answer
            // like any other: a function of every row's SORT-KEY CELLS, and a window recomputes its
            // column over the whole frame, so a row the delta never named can arrive here with a
            // different key. Keyed on "the delta did not name it", `window(rank) > sort(rk)` and
            // `window(lag) > sort(prev)` disagreed with the reference on every release from `0.19.0`
            // to `0.28.0`; `IncrementalRefreshCostTests` holds them and `IncrementalDelta` shape `44`
            // covers the class.
            //
            // The cached order may be reused only for rows that arrived in the SAME relative order as
            // last time. A stable sort breaks ties by arrival position, so a reordering among reused
            // rows moves the answer while naming no row at all — and `Delta.diff` reports a pure
            // reordering as quiet, so nothing in the delta would have said so.
            let reusable =
                match Map.tryFind sortIdx prior.SortOrders with
                | None -> None
                | Some(prevArrival, prevOrder) ->
                    let cur = currentOf r

                    // A prior slot's row, now, where it is still stable; -1 otherwise.
                    let stableNow (p: int) =
                        let s = if p >= 0 && p < cur.Length then cur[p] else -1
                        if s >= 0 && r.Stable[s] then s else -1

                    // The stable rows' prior arrival, read in step with their arrival now: equal
                    // sequences, compared without building either.
                    let mutable i = 0
                    let mutable j = 0
                    let mutable same = true

                    while same && (i < prevArrival.Length || j < arrival.Length) do
                        if i < prevArrival.Length && stableNow prevArrival[i] < 0 then
                            i <- i + 1
                        elif j < arrival.Length && not r.Stable[arrival[j]] then
                            j <- j + 1
                        elif i < prevArrival.Length && j < arrival.Length then
                            same <- stableNow prevArrival[i] = arrival[j]
                            i <- i + 1
                            j <- j + 1
                        else
                            same <- false

                    if same then
                        let kept = ResizeArray<int>(prevOrder.Length)

                        for p in prevOrder do
                            let s = stableNow p

                            if s >= 0 then
                                kept.Add s

                        Some(kept.ToArray())
                    else
                        None

            let ordered =
                match reusable with
                | None -> sortSlots arrival
                | Some cachedStable ->
                    let moved = arrival |> Array.filter (fun s -> not r.Stable[s]) |> sortSlots
                    mergeOrders cmp posOf cachedStable moved

            // Phase 327 — after a sort the frame's order is no longer ascending, which is what an
            // in-place step's `reaches` reads, so the steps after it take the general reading. The
            // slot numbering, `Prior` and `Stable` are unchanged, and that is all it reads.
            walk
                resolve
                env
                prior
                { r with InPlace = null }
                { f with Order = ordered }
                evaluated
                { caches with
                    SortOrders = Map.add sortIdx (arrival, ordered) caches.SortOrders }
                rest
        // Phase 120 — a bounded-frame `Window`. The column is recomputed over the rows alive AT
        // THIS STEP, in the order they are in, through the reference's own window step: the walked
        // frame IS the reference's frame here (the walk's invariant), so the appended column is the
        // reference's column by construction. `evaluated` does not move and no step is recorded: a
        // window evaluates no expression, so it caches no cell and the reference charges it nothing.
        //
        // Phase 324 — the column is the reference's `windowColumnOf` over the columns the window
        // READS, each packed once from its slot array in the walk's order (exactly as the public row
        // form packs its own), so no row is materialised: the partition slots, the coded order and
        // the scan are the frame path's own. The other columns are SHARED, array and origin, as a
        // `Derive` shares them: no array is written after the step that made it, and a column the
        // window does not read is never unpacked.
        | WWindow spec :: rest ->
            let order = f.Order
            let types = f.Cols |> List.map snd |> List.toArray
            let packed = System.Collections.Generic.Dictionary<int, Vec>()

            // Every slot alive, in slot order: a column no step unpacked is then its source list.
            let identity =
                lazy
                    (order.Length = r.Stable.Length
                     && (let mutable same = true
                         let mutable i = 0

                         while same && i < order.Length do
                             same <- order[i] = i
                             i <- i + 1

                         same))

            let vecOf (ci: int) : Vec =
                match packed.TryGetValue ci with
                | true, v -> v
                | _ ->
                    let fromColumn () =
                        let a = column f ci
                        Vec.pack types[ci] (order |> Array.map (fun s -> a[s]))

                    // A column still its source list packs straight from the list, as `Frame.ofTable`
                    // packs it (Phase 327), rather than through an unpacked array.
                    let v =
                        match f.Origins[ci] with
                        | Some origin when isNull f.Data[ci] && identity.Value ->
                            match Vec.packList types[ci] order.Length origin with
                            | ValueSome v -> v
                            | ValueNone -> fromColumn ()
                        | _ -> fromColumn ()

                    packed[ci] <- v
                    v

            DataFrame.windowColumnOf DataFrame.Ordering.Exact f.Cols vecOf (Array.init order.Length id) spec
            |> Result.bind (fun (ty, appended) ->
                let cols2 = f.Cols @ [ spec.As, ty ]
                let last: Cell[] = Array.zeroCreate r.Stable.Length

                for k in 0 .. order.Length - 1 do
                    last[order[k]] <- DataFrame.windowCellAt appended k

                // `Stable` is cleared for EVERY slot, dead ones too: the appended column is a
                // function of the whole frame (see `WalkRows`).
                Array.fill r.Stable 0 r.Stable.Length false

                walk
                    resolve
                    env
                    prior
                    { r with InPlace = null }
                    { Cols = cols2
                      Data = Array.append f.Data [| last |]
                      Origins = Array.append f.Origins [| None |]
                      Order = f.Order }
                    evaluated
                    caches
                    rest)
        // Phase 207 — a `Limit`: keep `[offset, offset + n)` of the rows alive AT THIS STEP, which is
        // exactly what `DataFrame.evalLimit` does to that frame — the same `max 0` clamps, written
        // as a membership test so the arithmetic cannot overflow at `System.Int32.MaxValue`. A row
        // outside the window leaves as a `Filter`'s dropped row leaves, and a row RE-ENTERING it on a
        // later refresh finds no cached cell past the step it died at, so it is evaluated afresh.
        | WLimit(n, offset) :: rest ->
            let lo = max 0 offset
            let keep = max 0 n

            // The positions `i` with `i >= lo && i - lo < keep`, as one slice: the window's end is
            // taken as `lo + keep` only where that cannot pass the frame's length, so it cannot
            // overflow either.
            let len = f.Order.Length

            let stop =
                if lo >= len then lo
                elif keep > len - lo then len
                else lo + keep

            let kept = if stop > lo then Array.sub f.Order lo (stop - lo) else [||]

            // Phase 327 — a window over the frame lets a row the delta did not name enter or leave
            // it, which an in-place step (evaluating the changed rows alone) would miss, so the steps
            // after a limit take the general reading, as the steps after a sort do.
            walk resolve env prior { r with InPlace = null } { f with Order = kept } evaluated caches rest
        // Phase 120 — a filtering join (`Semi` / `Anti`): a `Filter` whose predicate reads a
        // relation instead of an expression. The verdict is recorded as a `Filter`'s is, so it is an
        // evaluating step; `evaluated` does not move, because the reference charges a join nothing.
        //
        // Reuse is conditioned on the RELATION as well as on `Stable`: the delta describes the
        // source only, so a row it did not name can still have a different verdict when the joined
        // relation gained or lost the key it matched on. When the key index has moved, every row's
        // verdict is recomputed at this step.
        | WJoin(joinIdx, src, on, keepMatched) :: rest ->
            DataFrame.evalSource resolve src
            |> Result.bind (fun rightTable ->
                DataFrame.joinKeyIndices f.Cols rightTable.Schema on
                |> Result.bind (fun (li, ri) ->
                    let rightKeys =
                        rowsOf rightTable |> List.map (fun row -> ri |> List.map (fun j -> row[j]))

                    let relationMoved = Map.tryFind joinIdx prior.JoinKeys <> Some rightKeys
                    let step: Cell[] = Array.zeroCreate r.Stable.Length
                    let kept = ResizeArray<int>(f.Order.Length)

                    for s in f.Order do
                        let cached =
                            if relationMoved then
                                Unchecked.defaultof<Cell>
                            else
                                cachedAt r evalIdx s

                        let v =
                            if not (isNull (box cached)) then
                                cached
                            else
                                let leftKeys = li |> List.map (fun i -> (column f i)[s])
                                let matched = rightKeys |> List.exists (DataFrame.joinKeysMatch leftKeys)
                                Bool(matched = keepMatched)

                        step[s] <- v

                        if v = Bool true then
                            kept.Add s

                    r.Steps.Add step

                    walk
                        resolve
                        env
                        prior
                        r
                        { f with Order = kept.ToArray() }
                        evaluated
                        { caches with
                            JoinKeys = Map.add joinIdx rightKeys caches.JoinKeys }
                        rest))
        | WFilter pred :: rest ->
            let resolved = DataFrame.resolveExpr env f.Cols pred

            evalStep r f evalIdx resolved evaluated
            |> Result.bind (fun (step, n) ->
                r.Steps.Add step
                // Matched, not compared: `=` on a cell is structural equality through the generic
                // comparer, once per row (Phase 323).
                let kept =
                    f.Order
                    |> Array.filter (fun s ->
                        match step[s] with
                        | Bool true -> true
                        | _ -> false)

                walk resolve env prior r { f with Order = kept } n caches rest)
        | WProject pairs :: rest ->
            let resolveOne (src, out) =
                match colIndex f.Cols src with
                | None -> Error(UnknownColumn(src, available f.Cols))
                | Some i -> Ok(out, snd (List.item i f.Cols), i)

            traverse resolveOne pairs
            |> Result.bind (fun resolved ->
                let idx = resolved |> List.map (fun (_, _, i) -> i) |> List.toArray

                walk
                    resolve
                    env
                    prior
                    r
                    { f with
                        Cols = resolved |> List.map (fun (o, ty, _) -> o, ty)
                        Data = idx |> Array.map (fun i -> f.Data[i])
                        Origins = idx |> Array.map (fun i -> f.Origins[i]) }
                    evaluated
                    caches
                    rest)
        | WDerive(name, expr) :: rest ->
            // The derived column's typing is the reference's own rule over the same schema
            // (`DataFrame.derivedTyping`, Phase 338), so refresh and full agree by construction. A
            // derive refused statically is refused before any row, as the reference refuses it.
            match DataFrame.derivedTyping f.Cols expr with
            | DataFrame.Refused -> Error(DataFrame.floatBesideDecimal name)
            | dt ->
                let resolved = DataFrame.resolveExpr env f.Cols expr

                evalStep r f evalIdx resolved evaluated
                |> Result.bind (fun (step, n) ->
                    r.Steps.Add step

                    // A decided column's TYPE is its typing's, whatever the rows. One the cells
                    // decide is a function of the whole column, not of one row: its present cells'
                    // types joined among the rows alive AT THIS STEP in frame order
                    // (`DataFrame.columnTypeBy`, Phase 321's widening join, refusing a float beside a
                    // decimal), over precisely the set the reference evaluator holds here. Reading
                    // it off a cache would type the column differently from the reference the
                    // moment a filter downstream dropped the only typed row.
                    DataFrame.columnTypeBy dt name f.Order.Length (fun i -> step[f.Order[i]])
                    |> Result.bind (fun ty ->
                        // The step's cells ARE the derived column: live at every slot in `Order`,
                        // and a slot outside it is never read again this walk.
                        let f2 =
                            match colIndex f.Cols name with
                            | Some i ->
                                { f with
                                    Cols = f.Cols |> List.mapi (fun j (n2, t) -> if j = i then n2, ty else n2, t)
                                    Data = f.Data |> Array.mapi (fun j a -> if j = i then step else a)
                                    Origins = f.Origins |> Array.mapi (fun j o -> if j = i then None else o) }
                            | None ->
                                { f with
                                    Cols = f.Cols @ [ name, ty ]
                                    Data = Array.append f.Data [| step |]
                                    Origins = Array.append f.Origins [| None |] }

                        walk resolve env prior r f2 n caches rest))

    /// The frame's rows alive at its end, as a table under its schema — the transpose `tableOf`
    /// would do over row arrays, one column at a time. A column whose array is still the source's
    /// own list, over a frame whose `Order` is every slot in slot order, hands that list back.
    let private tableOfFrame (f: WalkFrame) (slotCount: int) : Table =
        let identity =
            f.Order.Length = slotCount
            && (let mutable same = true
                let mutable i = 0

                while same && i < f.Order.Length do
                    same <- f.Order[i] = i
                    i <- i + 1

                same)

        { Schema = f.Cols
          Columns =
            f.Cols
            |> List.mapi (fun ci (name, ty) ->
                let cells =
                    match f.Origins[ci] with
                    | Some origin when identity -> origin
                    // Phase 327 — a few rows (a top-N board) of a column no step unpacked are read
                    // by one walk of its list in slot order, rather than by unpacking the column
                    // whole to read them.
                    | Some origin when isNull f.Data[ci] && f.Order.Length <= sparseRowLimit ->
                        let order = f.Order
                        let bySlot = Array.init order.Length id |> Array.sortBy (fun k -> order[k])
                        let out: Cell[] = Array.zeroCreate order.Length
                        let mutable rest = origin
                        let mutable pos = 0

                        for k in bySlot do
                            let s = order[k]

                            while pos < s do
                                rest <- rest.Tail
                                pos <- pos + 1

                            out[k] <- rest.Head

                        List.ofArray out
                    | _ ->
                        let a = column f ci
                        let mutable acc = []

                        for k in f.Order.Length - 1 .. -1 .. 0 do
                            acc <- a[f.Order[k]] :: acc

                        acc

                Column.create name ty cells) }

    // ---- the maintained-group step ----

    /// The outcome of a maintained `GroupBy`: the result schema and rows, and the group caches the
    /// state records — all POSITIONAL, aligned with `Order` (Phase 274; they were maps keyed by
    /// group token, which a refresh rebuilt by one `Map.add` per group — a persistent tree of every
    /// group, per refresh, on a high-cardinality key).
    type private GroupOutcome =
        {
            Cols: Schema
            Rows: Cell[] list
            /// Phase 202 — the group tokens in the same order as `Rows`, which is what lets the tail
            /// walk pair each group row with the identity its cache is keyed by. Carried rather than
            /// recomputed: re-deriving it would mean re-tokenising the key cells, and a second
            /// derivation of an identity is a second thing that can disagree.
            Order: string[]
            /// Phase 208 — the index into `Order` of the group each SOURCE ROW landed in, by that
            /// row's slot in the source's row order (`-1` for a row the prefix filtered away). It is
            /// what lets the next refresh CARRY a group identity instead of re-minting one per row.
            RowGroups: int[]
            /// Per group, its members' row tokens IN ORDER.
            Members: string list[]
            /// Per group, the aggregate cells computed or reused for it.
            Aggs: Cell list[]
            Recomputed: int
            /// Phase 202 — per group, whether its aggregates were RECOMPUTED; `Recomputed` counts the
            /// `true`s. The count is the footprint's; this is what the tail walk reads to decide which
            /// group rows it must re-evaluate, and the two cannot disagree because both are written by
            /// the same branch. A group not recomputed has key cells and aggregate cells
            /// byte-identical to the ones the tail last saw.
            RecomputedAt: bool[]
        }

    /// The prior evaluation's group caches, positional as `GroupOutcome` records them.
    type private PriorGroups =
        {
            Order: string[]
            RowGroups: int[]
            Members: string list[]
            Aggs: Cell list[]
            /// Phase 323 — the group rows, aligned with `Order`.
            Rows: Cell[][]
        }

    /// Recompute a final `GroupBy` over the walked frame, recomputing only the affected groups'
    /// aggregates and reusing the cached cells for the rest. Mirrors `evalGroupBy`'s order of
    /// operations exactly — keys resolved first, then aggregates, then groups in first-appearance
    /// order — because the FIRST error the reference reports is part of its answer.
    ///
    /// **Phase 208 — a group is reusable exactly when every member's cells are STABLE and its ordered
    /// member list is byte-identical to the cached one.** That is the whole of what a cached
    /// aggregate depends on: the aggregate is a function of its members' cells in order. Both
    /// conditions are load-bearing — `First` / `Last` read position outright and a float `Sum` is
    /// order-sensitive in its last bits, so a pure reordering of unnamed rows can move a group's
    /// aggregate — and a group a named row LEFT has a different member list, so it is recomputed.
    ///
    /// An untouched group cannot error: its cached cells came from a successful evaluation over
    /// members that have not moved, so the first error among the recomputed groups (in group order)
    /// is the first error overall.
    let private groupStep
        (f: WalkFrame)
        (r: WalkRows)
        (keys: string list)
        (aggs: Agg list)
        (prior: PriorGroups)
        : Result<GroupOutcome, EvalError> =
        let cols = f.Cols
        let keyIdx = keys |> List.map (fun k -> colIndex cols k, k)

        match keyIdx |> List.tryPick (fun (i, k) -> if Option.isNone i then Some k else None) with
        | Some missing -> Error(UnknownColumn(missing, available cols))
        | None ->
            let idxs = keyIdx |> List.map (fun (i, _) -> Option.get i) |> List.toArray

            let resolveAgg (a: Agg) =
                match colType cols a.Of with
                | Some ty -> Ok(a, ty, colIndex cols a.Of |> Option.get)
                | None -> Error(UnknownColumn(a.Of, available cols))

            traverse resolveAgg aggs
            |> Result.bind (fun resolvedAggs ->
                let keyCols = keys |> List.map (fun k -> k, colType cols k |> Option.get)

                let aggCols =
                    resolvedAggs
                    |> List.map (fun (a, ty, _) -> a.Name, DataFrame.aggregateType a.Fn ty)

                // Group, preserving first-appearance order — the same partition `evalGroupBy`
                // builds, so the two never disagree about which rows are one group. The accumulators
                // are MUTABLE and strictly local (Phase 208); nothing mutable escapes.
                //
                // The group is CARRIED for a row whose cells have not moved (Phase 208): a group is a
                // pure function of the key cells, so a `Stable` row the prior evaluation placed in a
                // group is in that group. Since Phase 274 what is carried is the prior group's INDEX,
                // and `current` translates it to this evaluation's, so a carried row costs two array
                // reads where it cost a hash of its group token. A row with no carried group finds
                // its group by TOKEN equality over its key cells (`DataFrame.CellKey`, Phase 265), and
                // a group's token is minted once, by the row that opens it — or carried from the prior
                // group when that row carries one. The two routes cannot disagree about a group: a
                // carried group's token is the token of its row's key cells (it was minted from them,
                // and a `Stable` row's cells have not moved), and `CellKey` equality is token
                // equality, which a law in the suite holds.
                let rowGroups: int[] = Array.create r.Stable.Length -1
                let current: int[] = Array.create prior.Order.Length -1
                let bySlotCells = DataFrame.CellKey.slots ()
                let probe: Cell[] = Array.zeroCreate idxs.Length

                let readKey (s: int) =
                    for j in 0 .. idxs.Length - 1 do
                        probe[j] <- (column f idxs[j])[s]

                let order = ResizeArray<string>()
                let priorIdx = ResizeArray<int>()
                let groupKeys = ResizeArray<Cell list>()
                let groupSlots = ResizeArray<ResizeArray<int>>()
                let groupStable = ResizeArray<bool>()

                // Open the next group for slot `s`, whose key cells are in `probe`, under token `gt`;
                // `pg` is the prior group it continues, where the opener carried one.
                let openGroup (gt: string) (pg: int) (s: int) =
                    let gi = order.Count
                    order.Add gt
                    priorIdx.Add pg
                    groupKeys.Add(List.ofArray probe)
                    let slots = ResizeArray<int>()
                    slots.Add s
                    groupSlots.Add slots
                    groupStable.Add r.Stable[s]
                    gi

                let join (gi: int) (s: int) =
                    groupSlots[gi].Add s

                    if not r.Stable[s] then
                        groupStable[gi] <- false

                for s in f.Order do
                    let carried =
                        let p = r.Prior[s]

                        if r.Stable[s] && p >= 0 && p < prior.RowGroups.Length then
                            prior.RowGroups[p]
                        else
                            -1

                    let carriedGroup = if carried >= 0 then current[carried] else -1

                    if carriedGroup >= 0 then
                        rowGroups[s] <- carriedGroup
                        join carriedGroup s
                    else
                        readKey s

                        let gi =
                            match DataFrame.CellKey.slotOf bySlotCells probe order.Count with
                            | gi, false ->
                                join gi s
                                gi
                            | _, true ->
                                if carried >= 0 then
                                    openGroup prior.Order[carried] carried s
                                else
                                    openGroup (DataFrame.rowTokenStringOfArray probe) -1 s

                        if carried >= 0 then
                            current[carried] <- gi

                        rowGroups[s] <- gi

                // The prior index of a group no carried row opened — a lookup by its token, through an
                // index built on the first such group that needs one and never otherwise.
                let mutable priorAt: System.Collections.Generic.Dictionary<string, int> = null

                let priorOf (gi: int) =
                    if priorIdx[gi] >= 0 then
                        priorIdx[gi]
                    elif prior.Order.Length = 0 then
                        -1
                    else
                        if isNull priorAt then
                            let d = System.Collections.Generic.Dictionary<string, int>(prior.Order.Length)

                            for j in 0 .. prior.Order.Length - 1 do
                                d[prior.Order[j]] <- j

                            priorAt <- d

                        match priorAt.TryGetValue order[gi] with
                        | true, j -> j
                        | _ -> -1

                // Is a group's member list NOW exactly `members`, in order? Compared token by token
                // against the growable slot list, so a reused group builds no second copy.
                let sameMembers (members: string list) (now: ResizeArray<int>) =
                    let mutable rest = members
                    let mutable i = 0
                    let mutable same = true

                    while same && i < now.Count do
                        match rest with
                        | t :: tail when System.String.Equals(t, r.Tokens[now[i]]) ->
                            rest <- tail
                            i <- i + 1
                        | _ -> same <- false

                    same && List.isEmpty rest

                // One aggregate's source cells over a group's members, in member order, built from
                // the back — only for a group that is recomputed.
                let columnOf (members: ResizeArray<int>) (ci: int) : Cell list =
                    let a = column f ci
                    let mutable acc = []

                    for j in members.Count - 1 .. -1 .. 0 do
                        acc <- a[members[j]] :: acc

                    acc

                let groupCount = order.Count
                let members: string list[] = Array.zeroCreate groupCount
                let aggCells: Cell list[] = Array.zeroCreate groupCount
                let recomputedAt: bool[] = Array.zeroCreate groupCount
                let mutable recomputed = 0
                let mutable failed = None
                let mutable gi = 0

                // A group's aggregates and the member list the state records for it: reused (with
                // the prior list itself) when every member is stable, the members are the prior
                // members in the prior order, and the prior aggregates are cached; recomputed
                // otherwise.
                //
                // Phase 323 — which groups are reused is decided first (it reads nothing an
                // aggregate computes), and the recomputed groups' aggregates are then STREAMED: one
                // `DataFrame.GroupAgg` accumulator per aggregate, fed each recomputed group's
                // members in member order — the evaluator's own streams, so a recomputed group's
                // cells are the cells the full evaluation computes, to the bit. A group-aggregate
                // the stream defers is computed by `DataFrame.aggregateCells` over its members, as
                // every recomputed group's was before.
                let reusedFrom: int[] = Array.create groupCount -1

                for g in 0 .. groupCount - 1 do
                    let pg = if groupStable[g] then priorOf g else -1

                    if
                        pg >= 0
                        && pg < prior.Members.Length
                        && pg < prior.Aggs.Length
                        && sameMembers prior.Members[pg] groupSlots[g]
                    then
                        reusedFrom[g] <- pg

                let aggArr = List.toArray resolvedAggs

                let streams =
                    if reusedFrom |> Array.forall (fun pg -> pg >= 0) then
                        [||]
                    else
                        aggArr
                        |> Array.map (fun (a, ty, ci) ->
                            let s =
                                DataFrame.GroupAgg.Stream(
                                    a.Fn,
                                    ty,
                                    Cells(column f ci),
                                    groupCount,
                                    DataFrame.GroupAgg.Exact
                                )

                            for g in 0 .. groupCount - 1 do
                                if reusedFrom[g] < 0 then
                                    for slot in groupSlots[g] do
                                        s.Feed(g, slot)

                            s)

                while failed.IsNone && gi < groupCount do
                    let pg = reusedFrom[gi]

                    if pg >= 0 then
                        members[gi] <- prior.Members[pg]
                        aggCells[gi] <- prior.Aggs[pg]
                    else
                        recomputed <- recomputed + 1
                        recomputedAt[gi] <- true

                        // In aggregate order, stopping at the first error — the traverse this replaced.
                        let vals: Cell[] = Array.zeroCreate aggArr.Length
                        let mutable j = 0

                        while failed.IsNone && j < aggArr.Length do
                            match streams[j].TryCell gi with
                            | ValueSome c -> vals[j] <- c
                            | ValueNone ->
                                let a, ty, ci = aggArr[j]

                                match DataFrame.aggregateCells a.Fn ty (columnOf groupSlots[gi] ci) with
                                | Ok c -> vals[j] <- c
                                | Error e -> failed <- Some e

                            j <- j + 1

                        if failed.IsNone then
                            aggCells[gi] <- List.ofArray vals
                            let slots = groupSlots[gi]
                            members[gi] <- List.init slots.Count (fun j -> r.Tokens[slots[j]])

                    gi <- gi + 1

                match failed with
                | Some e -> Error e
                | None ->
                    Ok
                        { Cols = keyCols @ aggCols
                          Rows = List.init groupCount (fun gi -> List.toArray (groupKeys[gi] @ aggCells[gi]))
                          Order = order.ToArray()
                          RowGroups = rowGroups
                          Members = members
                          Aggs = aggCells
                          Recomputed = recomputed
                          RecomputedAt = recomputedAt })

    /// The cells of column `c` at the ASCENDING slots `slots`: from the column's array where it has
    /// one, else in one walk down the source list — without unpacking the column (Phase 323).
    let private cellsAtSlots (f: WalkFrame) (c: int) (slots: ResizeArray<int>) : Cell[] =
        let out: Cell[] = Array.zeroCreate slots.Count
        let a = f.Data[c]

        if not (isNull a) then
            for j in 0 .. slots.Count - 1 do
                out[j] <- a[slots[j]]
        else
            let mutable rest = f.Origins[c].Value
            let mutable at = 0

            for j in 0 .. slots.Count - 1 do
                while at < slots[j] do
                    rest <- rest.Tail
                    at <- at + 1

                out[j] <- rest.Head

        out

    /// Phase 323 — the maintained `GroupBy` of an IN-PLACE refresh (`WalkRows.InPlace`), paying for
    /// the changed rows rather than for the table: `Some` where it applies, `None` to take
    /// `groupStep`'s general walk, whose answer it equals.
    ///
    /// It applies when every changed row is still in the group it was in (the walk reaches it
    /// exactly when it reached it before, and its key cells name the same group) and is not the
    /// first member of that group (whose cells are the group row's key cells). Then the partition,
    /// the group order and every member list are the prior evaluation's, row for row, and the
    /// groups `groupStep` would recompute are exactly the changed rows' groups — every other group
    /// is stable with the same members, which is `groupStep`'s reuse condition. Those are
    /// recomputed over their members in member order by the evaluator's streams (deferring to
    /// `DataFrame.aggregateCells` where a stream defers), in group order, and the first error is
    /// `groupStep`'s first error. The rest is the prior state's: its rows, its member lists, its
    /// aggregate cells.
    ///
    /// A `Count`, and an int `Sum` whose changed members all hold an int now, is MAINTAINED rather
    /// than recomputed: the prior cell, less what each changed member contributed before, plus what
    /// it contributes now — exact, so the same cell `Column.aggregate` answers over the members. It
    /// needs each changed member's prior cell, which an in-place walk has: a derived column's in the
    /// prior evaluation's step cells, a source column's in the prior source's column of the same
    /// schema position (the frame column's cell list must be exactly ONE current source column's
    /// list, which names the position). Anything it cannot
    /// read, or a cell `Column.aggregate` would refuse, is recomputed. A float `Sum` is never
    /// maintained: its bits are the left fold over the members in order, so it is rescanned.
    let private groupStepInPlace
        (f: WalkFrame)
        (r: WalkRows)
        (keys: string list)
        (aggs: Agg list)
        (prior: PriorGroups)
        (source: Table)
        (priorSource: Table)
        (priorExact: bool[])
        : Result<GroupOutcome, EvalError> option =
        let cols = f.Cols
        let groupCount = prior.Order.Length

        let keyIdx = keys |> List.map (colIndex cols)

        let aggIdx =
            aggs
            |> List.map (fun a ->
                match colType cols a.Of, colIndex cols a.Of with
                | Some ty, Some ci -> Some(a, ty, ci)
                | _ -> None)

        if
            isNull r.InPlace
            || groupCount = 0
            || prior.RowGroups.Length <> r.Stable.Length
            || prior.Members.Length <> groupCount
            || prior.Aggs.Length <> groupCount
            || prior.Rows.Length <> groupCount
            || keyIdx |> List.exists Option.isNone
            || aggIdx |> List.exists Option.isNone
        then
            None
        else
            let idxs = keyIdx |> List.map Option.get |> List.toArray
            let aggArr = aggIdx |> List.map Option.get |> List.toArray
            let probe: Cell[] = Array.zeroCreate idxs.Length
            let dirty: bool[] = Array.zeroCreate groupCount
            let changedIn = System.Collections.Generic.Dictionary<int, ResizeArray<int>>()
            let mutable applies = true
            let mutable j = 0

            while applies && j < r.InPlace.Length do
                let s = r.InPlace[j]
                let pg = prior.RowGroups[s]
                let reached = reaches f.Order s

                if reached <> (pg >= 0) then
                    applies <- false
                elif pg >= 0 then
                    for k in 0 .. idxs.Length - 1 do
                        probe[k] <- cellAtSlot f idxs[k] s

                    let opener =
                        match prior.Members[pg] with
                        | t :: _ -> System.String.Equals(t, r.Tokens[s])
                        | [] -> true

                    if
                        opener
                        || not (System.String.Equals(DataFrame.rowTokenStringOfArray probe, prior.Order[pg]))
                    then
                        applies <- false
                    else
                        dirty[pg] <- true

                        match changedIn.TryGetValue pg with
                        | true, xs -> xs.Add s
                        | _ ->
                            let xs = ResizeArray<int>()
                            xs.Add s
                            changedIn[pg] <- xs

                j <- j + 1

            if not applies then
                None
            else
                // The dirty groups' member slots, in member order: the walk's order is ascending in
                // place, and the partition is the prior one.
                //
                // Built on the first aggregate that is rescanned rather than maintained (Phase 323): a
                // refresh whose every aggregate is maintained reads no member list at all.
                let mutable slotsOf: System.Collections.Generic.Dictionary<int, ResizeArray<int>> =
                    null

                let slotsFor (g: int) : ResizeArray<int> =
                    if isNull slotsOf then
                        let d = System.Collections.Generic.Dictionary<int, ResizeArray<int>>()

                        for h in 0 .. groupCount - 1 do
                            if dirty[h] then
                                d[h] <- ResizeArray<int>()

                        for s in f.Order do
                            let h = prior.RowGroups[s]

                            if h >= 0 && dirty[h] then
                                d[h].Add s

                        slotsOf <- d

                    slotsOf[g]

                let nk = idxs.Length

                // The cell column `c` held at slot `s` in the PRIOR evaluation, where it can be read.
                let priorCellAt (c: int) (s: int) : Cell voption =
                    let a = f.Data[c]
                    let mutable stepIdx = -1

                    if not (isNull a) then
                        for idx in 0 .. r.Steps.Count - 1 do
                            if obj.ReferenceEquals(r.Steps[idx], a) then
                                stepIdx <- idx

                    if stepIdx >= 0 then
                        if stepIdx < r.PriorSteps.Length && s < r.PriorSteps[stepIdx].Length then
                            let v = r.PriorSteps[stepIdx][s]
                            if isNull (box v) then ValueNone else ValueSome v
                        else
                            ValueNone
                    elif not (isNull (box priorSource)) && f.Origins[c].IsSome then
                        // The current source column this frame column IS: the one schema position
                        // whose cell list is the frame column's list.
                        let list = f.Origins[c].Value
                        let mutable at = -1
                        let mutable matches = 0

                        source.Schema
                        |> List.iteri (fun i (name, _) ->
                            match Table.tryColumn name source with
                            | Some sc when obj.ReferenceEquals(sc.Cells, list) ->
                                at <- i
                                matches <- matches + 1
                            | _ -> ())

                        let priorColumn =
                            if matches = 1 && at < priorExact.Length && priorExact[at] then
                                Table.tryColumn (fst (List.item at priorSource.Schema)) priorSource
                            else
                                None

                        match priorColumn with
                        | Some pc ->
                            let mutable rest = pc.Cells
                            let mutable i = 0

                            while i < s && not rest.IsEmpty do
                                rest <- rest.Tail
                                i <- i + 1

                            if rest.IsEmpty then ValueNone else ValueSome rest.Head
                        | None -> ValueNone
                    else
                        ValueNone

                // A maintained `Count` or int `Sum` (see above), or `ValueNone` to recompute.
                let maintained (fn: AggFn) (ty: ColumnType) (c: int) (priorCell: Cell) (changed: ResizeArray<int>) =
                    if not (fn = Count || (fn = Sum && ty = IntType)) then
                        ValueNone
                    else
                        let mutable ok = true
                        let mutable allNewInt = true
                        let mutable delta = 0L
                        let mutable i = 0

                        while ok && i < changed.Count do
                            let s = changed[i]

                            match priorCellAt c s, DataFrame.GroupAgg.admitted ty (cellAtSlot f c s) with
                            | ValueSome before, ValueSome now ->
                                if fn = Count then
                                    let present (x: Cell) =
                                        match x with
                                        | Null -> 0L
                                        | _ -> 1L

                                    delta <- delta + present now - present before
                                else
                                    match before with
                                    | Int x -> delta <- delta - int64 x
                                    | Null -> ()
                                    | _ -> ok <- false

                                    match now with
                                    | Int x -> delta <- delta + int64 x
                                    | _ -> allNewInt <- false
                            | _ -> ok <- false

                            i <- i + 1

                        if not ok then
                            ValueNone
                        elif fn = Count then
                            match priorCell with
                            | Int n -> ValueSome(Int(int (int64 n + delta)))
                            | _ -> ValueNone
                        elif not allNewInt then
                            ValueNone
                        else
                            let before =
                                match priorCell with
                                | Int x -> ValueSome(int64 x)
                                | Null -> ValueSome 0L
                                | _ -> ValueNone

                            match before with
                            | ValueSome b ->
                                let total = b + delta

                                if total >= int64 System.Int32.MinValue && total <= int64 System.Int32.MaxValue then
                                    ValueSome(Int(int total))
                                else
                                    ValueNone
                            | ValueNone -> ValueNone

                let members = Array.copy prior.Members
                let aggCells = Array.copy prior.Aggs
                let rows = Array.copy prior.Rows
                let recomputedAt: bool[] = Array.zeroCreate groupCount
                let mutable recomputed = 0
                let mutable failed = None
                let mutable g = 0

                while failed.IsNone && g < groupCount do
                    if dirty[g] then
                        recomputed <- recomputed + 1
                        recomputedAt[g] <- true
                        let vals: Cell[] = Array.zeroCreate aggArr.Length
                        let priorVals = List.toArray prior.Aggs[g]
                        let read = System.Collections.Generic.Dictionary<int, Cell[]>()
                        let mutable k = 0

                        while failed.IsNone && k < aggArr.Length do
                            let a, ty, ci = aggArr[k]

                            match
                                (if k < priorVals.Length then
                                     maintained a.Fn ty ci priorVals[k] changedIn[g]
                                 else
                                     ValueNone)
                            with
                            | ValueSome c -> vals[k] <- c
                            | ValueNone ->
                                let cells =
                                    match read.TryGetValue ci with
                                    | true, cs -> cs
                                    | _ ->
                                        let cs = cellsAtSlots f ci (slotsFor g)
                                        read[ci] <- cs
                                        cs

                                let stream =
                                    DataFrame.GroupAgg.Stream(a.Fn, ty, Cells cells, 1, DataFrame.GroupAgg.Exact)

                                for i in 0 .. cells.Length - 1 do
                                    stream.Feed(0, i)

                                match stream.TryCell 0 with
                                | ValueSome c -> vals[k] <- c
                                | ValueNone ->
                                    match DataFrame.aggregateCells a.Fn ty (List.ofArray cells) with
                                    | Ok c -> vals[k] <- c
                                    | Error e -> failed <- Some e

                            k <- k + 1

                        if failed.IsNone then
                            aggCells[g] <- List.ofArray vals
                            rows[g] <- Array.append (Array.sub prior.Rows[g] 0 nk) vals

                    g <- g + 1

                match failed with
                | Some e -> Some(Error e)
                | None ->
                    let keyCols = keys |> List.map (fun k -> k, colType cols k |> Option.get)

                    let aggCols =
                        aggArr
                        |> Array.toList
                        |> List.map (fun (a, ty, _) -> a.Name, DataFrame.aggregateType a.Fn ty)

                    Some(
                        Ok
                            { Cols = keyCols @ aggCols
                              Rows = List.ofArray rows
                              Order = prior.Order
                              RowGroups = prior.RowGroups
                              Members = members
                              Aggs = aggCells
                              Recomputed = recomputed
                              RecomputedAt = recomputedAt }
                    )

    // ---- identity tokens ----

    /// Every source row's identity token, in row order, refusing whole if the witness cannot key the
    /// source uniquely. The token format is `Delta.refToken`'s, so a delta's `ByKey` refs and these
    /// tokens are the same strings by construction rather than by a second convention.
    /// Phase 208 — two changes, both measured. The uniqueness check is a HASH set rather than a
    /// persistent `Set<string>`: it is written n times and read n times and never escapes this
    /// function, so the tree's log-n string comparisons and its node allocations bought nothing. And
    /// a token equal to the one the PRIOR evaluation held at the same row index is returned as that
    /// prior STRING INSTANCE rather than as the freshly minted equal copy — which is what lets
    /// `runIncremental` recognise a row that has not moved with one pointer comparison instead of a
    /// keyed lookup, and lets every later comparison take `String.Equals`'s reference fast path.
    ///
    /// The minting itself is NOT avoided, and cannot be while the seam is handed the whole new source:
    /// a row's token is a function of its identity cell, and the only way to know a row's identity is
    /// to read it. That is 207's finding, and it is the floor this phase works down to rather than
    /// through.
    ///
    /// Phase 273 — the keys minted here are REMEMBERED for `t` (see `KeyedIndexes`), so the next
    /// tick's `Delta.diff`, whose `before` is this source, reads them rather than minting them again.
    /// Where the delta carries the new source's keys already, `tokensOfKnown` below replaces this
    /// function altogether.
    let private tokensOf (idw: RowIdentity<'Id>) (priorTokens: string[]) (t: Table) : Result<string[], DeltaDefect> =
        let n = Table.rowCount t
        // Hoisted for the reason `Delta.keyIndex` hoists it (Phase 206): the witness's per-table
        // work belongs in the first application, not in every iteration of this loop.
        let keyAt = idw.KeyOf t
        let tokens: string[] = Array.zeroCreate n
        let keys: string[] = Array.zeroCreate n
        // Phase 283 — for a witness that declares a key equality, the typed ids ride beside the keys,
        // so the next tick's `Delta.diff` pairs this source's rows by id without asking the witness
        // for them again.
        let typed = (KeyEqualities.tryOf idw).IsSome
        let ids: 'Id[] = Array.zeroCreate (if typed then n else 0)
        let seen = System.Collections.Generic.HashSet<string>(n)
        let mutable defect = None
        let mutable i = 0

        while defect.IsNone && i < n do
            match keyAt i with
            | None -> defect <- Some(MissingIdentity(idw.Scheme, i))
            | Some id ->
                let k = idw.KeyString id
                keys[i] <- k

                if typed then
                    ids[i] <- id

                let minted = Delta.refToken (ByKey k)

                let token =
                    if i < priorTokens.Length && System.String.Equals(priorTokens[i], minted) then
                        priorTokens[i]
                    else
                        minted

                if seen.Add token then
                    tokens[i] <- token
                else
                    defect <- Some(DuplicateIdentity(idw.Scheme, k))

            i <- i + 1

        match defect with
        | Some d -> Error d
        | None ->
            let known = KeyedIndex(idw.Scheme, keys, null)

            if typed then
                known.SetIds(box idw, box ids)

            KeyedIndexes.remember t known
            Ok tokens

    /// Phase 273 — the source's tokens from keys `Delta.diff` already minted for this very table and
    /// proved unique (the delta carried them), so no key is minted and no uniqueness is re-checked.
    /// The answer is `tokensOf`'s: the same strings, and the PRIOR instance wherever the row sits
    /// where it sat — recognised by comparing the prior token's key part in place, so an unmoved
    /// row allocates nothing either.
    let private tokensOfKnown (known: KeyedIndex) (priorTokens: string[]) : string[] =
        let keys = known.Keys
        let tokens: string[] = Array.zeroCreate keys.Length

        for i in 0 .. keys.Length - 1 do
            let k = keys[i]

            tokens[i] <-
                if i < priorTokens.Length then
                    let p = priorTokens[i]

                    if
                        p.Length = k.Length + 2
                        && p.StartsWith("k:", System.StringComparison.Ordinal)
                        && System.String.CompareOrdinal(p, 2, k, 0, k.Length) = 0
                    then
                        p
                    else
                        Delta.refToken (ByKey k)
                else
                    Delta.refToken (ByKey k)

        tokens

    /// The tokens a delta names as present-and-changed (`RowAdded` / `RowChanged`) — the rows an
    /// incremental evaluation must re-evaluate.
    let private namedTokens (d: TableDelta) : Set<string> =
        (Delta.rowsWith RowAdded d @ Delta.rowsWith RowChanged d)
        |> List.map Delta.refToken
        |> Set.ofList

    /// Does this pipeline read a relation from OUTSIDE its own definition — a `Ref` source, which
    /// `resolve` answers and which the state therefore cannot pin (Phase 120)?
    ///
    /// It conditions the wholesale reuse of a prior result. That reuse asks whether anything the
    /// answer depends on has moved, and it can only ask about the three things the state carries:
    /// the pipeline, the env, and the source. An `Embedded` relation is part of the pipeline, so a
    /// changed one is already `PipelineChanged`. A `Ref` one is whatever `resolve` returns at the
    /// moment it is called, and neither a quiet delta nor a byte-identical source says it returned
    /// the same table twice — so a pipeline naming one takes the ordinary path, where the relation
    /// is resolved and compared. That is a correctness condition, not a cost one: handing back the
    /// prior result there answers the previous relation's question with the previous relation's
    /// answer.
    let private readsExternalSource (pipeline: Transform list) : bool =
        let isRef =
            function
            | Ref _ -> true
            | Embedded _ -> false

        pipeline
        |> List.exists (function
            | Join(src, _, _)
            | Union src
            | Intersect src
            | Except src -> isRef src
            | _ -> false)

    // ---- evaluation ----

    /// The source's columns as the walk's frame: one array per schema column, padded with `Null`
    /// exactly as `RowAccess.columns` pads (a name the table does not carry, or a column shorter
    /// than the table, reads `Null` — the total `Column.cell` policy), and beside each the source's
    /// own list where the array is that list unpadded.
    let private frameOf (t: Table) (n: int) (priorSource: Table) (priorExact: bool[]) : WalkFrame =
        // Phase 323 — a column whose cell list IS the list of the prior source's column of that name
        // (an in-place refresh: the edit did not touch it) has the length the prior frame found, so
        // it is not walked again to count it. `priorSource` is `null` everywhere else; where it is
        // not, the caller has established that it held exactly `n` rows, row for row, under this
        // schema, and `priorExact` says which of its columns' lists were exactly `n` long.
        let unchanged (ci: int) (name: string) (c: Column) =
            not (isNull (box priorSource))
            && ci < priorExact.Length
            && priorExact[ci]
            && (match Table.tryColumn name priorSource with
                | Some pc -> System.Object.ReferenceEquals(pc.Cells, c.Cells)
                | None -> false)

        let unpacked =
            t.Schema
            |> List.mapi (fun ci (name, _) ->
                match Table.tryColumn name t with
                | Some c ->
                    if unchanged ci name c || List.length c.Cells = n then
                        null, Some c.Cells
                    else
                        let a = List.toArray c.Cells
                        Array.init n (fun i -> if i < a.Length then a[i] else Null), None
                | None -> Array.create n Null, None)
            |> List.toArray

        { Cols = t.Schema
          Data = unpacked |> Array.map fst
          Origins = unpacked |> Array.map snd
          Order = Array.init n id }

    /// Run the incremental path over `source`, re-evaluating the rows in `named` (`None` = all).
    /// `prior` supplies the caches; an absent prior is the primed case. `tokens` is the source's
    /// identity tokens, already computed by the caller (which had to compute them to know the
    /// witness could key the source at all).
    let private runIncremental
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (scheme: string)
        (pipeline: Transform list)
        (p: IncrementalPlan)
        (prefix: PrefixStep list)
        (final: (string list * Agg list * PrefixStep list) option)
        (source: Table)
        (prepared: Prepared option)
        (tokens: string[])
        (prior: IncrementalEval option)
        (named: Set<string> option)
        (inPlace: int[] option)
        (recomputeOf: int -> int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        let rowCount = tokens.Length

        // Phase 323 — the in-place reading holds only over a prefix that never reorders the rows.
        //
        // Phase 327 — or over one with a `Sort` or a `Limit` in it and no group step after: every
        // row still sits at its prior slot, so the frame is built without re-counting the unchanged
        // columns, and the walk clears the in-place reading at the first sort or limit (`walk`'s
        // `WSort` and `WLimit`), so only the row-local steps ahead of it read in place. A maintained
        // group step reads the in-place changed rows against the walked order, so a prefix that
        // sorts or limits ahead of one keeps the general reading.
        //
        // Phase 324 — a `Window` reads as a sort does: it moves no row, the walk clears the in-place
        // reading at it, and it recomputes its column over the whole frame (clearing `Stable`), so
        // only the frame's build and the row-local steps ahead of it read in place.
        let inPlaceRows =
            let rowLocal =
                function
                | WFilter _
                | WDerive _
                | WProject _ -> true
                | _ -> false

            let ordering =
                function
                | WSort _
                | WLimit _
                | WWindow _ -> true
                | step -> rowLocal step

            match inPlace, prior, named with
            | Some changed, Some s, Some _ when
                obj.ReferenceEquals(tokens, s.Tokens)
                && (prefix |> List.forall rowLocal
                    || (Option.isNone final && prefix |> List.forall ordering))
                ->
                changed
            | _ -> null

        let priorTokens =
            prior |> Option.map (fun s -> s.Tokens) |> Option.defaultValue [||]

        let priorSteps =
            prior |> Option.map (fun s -> s.StepCells) |> Option.defaultValue [||]

        // Phase 208 — where a row's cached results are read from. The common case is the whole
        // point: a delta that changed a row's VALUE leaves every row at the index it already sat
        // at, so the tokens come back as the prior evaluation's own string instances and the test is
        // one pointer comparison per row — no hashing, no tree, nothing keyed.
        //
        // A row that MOVED — inserted ahead of, removed from in front of, or reordered — costs a
        // lookup, and the index those lookups read is built ONCE, on the first miss, never on a
        // refresh that has no misses and never on a prime.
        let mutable priorIndex: System.Collections.Generic.Dictionary<string, int> = null

        let priorSlotOf (token: string) =
            if priorTokens.Length = 0 then
                -1
            else
                if isNull priorIndex then
                    let d = System.Collections.Generic.Dictionary<string, int>(priorTokens.Length)

                    for j in 0 .. priorTokens.Length - 1 do
                        d[priorTokens[j]] <- j

                    priorIndex <- d

                match priorIndex.TryGetValue token with
                | true, j -> j
                | _ -> -1

        let priorOf: int[] = Array.zeroCreate rowCount
        let stable: bool[] = Array.zeroCreate rowCount

        // Phase 323 — in place, every row's prior slot is its slot and every row but the changed
        // ones is stable: what the loop below computes, by construction rather than by lookup.
        if not (isNull inPlaceRows) then
            for i in 0 .. rowCount - 1 do
                priorOf[i] <- i
                stable[i] <- true

            for c in inPlaceRows do
                stable[c] <- false
        else
            for i in 0 .. rowCount - 1 do
                let token = tokens[i]

                // The reference test is an OPTIMISATION and is unobservable, which is what makes it safe
                // in a Fable-compiled library where strings are primitives and `ReferenceEquals` compares
                // by VALUE: tokens are unique within a frame, so the only `j` with `priorTokens[j] =
                // token` is `i` whenever `priorTokens[i] = token`.
                let ps =
                    if i < priorTokens.Length && System.Object.ReferenceEquals(token, priorTokens[i]) then
                        i
                    else
                        priorSlotOf token

                priorOf[i] <- ps

                stable[i] <-
                    match named with
                    | None -> false
                    | Some ns -> ps >= 0 && not (Set.contains token ns)

        let rows =
            { Tokens = tokens
              Prior = priorOf
              Stable = stable
              PriorSteps = priorSteps
              PriorCount = priorTokens.Length
              Steps = ResizeArray<Cell[]>()
              InPlace = inPlaceRows }

        let priorCaches =
            match prior with
            | Some s ->
                { SortOrders = s.SortOrders
                  JoinKeys = s.JoinKeys }
            | None -> noCaches

        let priorSource, priorExact =
            match prior with
            | Some s when not (isNull inPlaceRows) -> s.Source.Value, s.SourceExact
            | _ -> Unchecked.defaultof<Table>, [||]

        let frame0 = frameOf source rowCount priorSource priorExact
        let sourceExact = frame0.Origins |> Array.map Option.isSome

        walk resolve env priorCaches rows frame0 0 noCaches prefix
        |> Result.bind (fun (frame, evaluated, caches) ->
            let stepCells = rows.Steps.ToArray()

            match final with
            | None ->
                Ok
                    { Plan = p
                      Pipeline = pipeline
                      Planned = pipeline
                      Env = env
                      Scheme = scheme
                      Source = Prepared.ready source
                      Prepared = prepared
                      Output = Prepared.ready (tableOfFrame frame rowCount)
                      Tokens = tokens
                      StepCells = stepCells
                      RowGroups = [||]
                      GroupMembers = [||]
                      GroupAggs = [||]
                      SortOrders = caches.SortOrders
                      JoinKeys = caches.JoinKeys
                      Footprint =
                        { SourceRows = rowCount
                          ResultRows = frame.Order.Length
                          Recompute = recomputeOf evaluated 0 }
                      GroupOrder = [||]
                      TailCells = [||]
                      ChunkedOutput = None
                      ChunksTouched = None
                      SourceExact = sourceExact
                      GroupRows = [||] }
            | Some(keys, aggs, tail) ->
                let priorGroups =
                    match prior with
                    | Some s ->
                        { Order = s.GroupOrder
                          RowGroups = s.RowGroups
                          Members = s.GroupMembers
                          Aggs = s.GroupAggs
                          Rows = s.GroupRows }
                    | None ->
                        { Order = [||]
                          RowGroups = [||]
                          Members = [||]
                          Aggs = [||]
                          Rows = [||] }

                (match groupStepInPlace frame rows keys aggs priorGroups source priorSource priorExact with
                 | Some outcome -> outcome
                 | None -> groupStep frame rows keys aggs priorGroups)
                |> Result.bind (fun g ->
                    // Phase 202 — one state shape whichever side of the branch below built it, so
                    // the tail cannot quietly record a different kind of answer from the no-tail
                    // case it generalises.
                    let finish output resultRows tailCells evaluated' (caches': WalkCaches) =
                        { Plan = p
                          Pipeline = pipeline
                          Planned = pipeline
                          Env = env
                          Scheme = scheme
                          Source = Prepared.ready source
                          Prepared = prepared
                          Output = Prepared.ready output
                          Tokens = tokens
                          StepCells = stepCells
                          RowGroups = g.RowGroups
                          GroupMembers = g.Members
                          GroupAggs = g.Aggs
                          SortOrders = caches'.SortOrders
                          JoinKeys = caches'.JoinKeys
                          Footprint =
                            { SourceRows = rowCount
                              ResultRows = resultRows
                              Recompute = recomputeOf evaluated' g.Recomputed }
                          GroupOrder = g.Order
                          TailCells = tailCells
                          ChunkedOutput = None
                          ChunksTouched = None
                          SourceExact = sourceExact
                          GroupRows = List.toArray g.Rows }

                    if List.isEmpty tail then
                        // The pipeline every pre-202 state was built for. Taken as its own branch
                        // rather than as `walk … []` so a maintained group-by that ends the
                        // pipeline pays nothing at all for a cache it has nothing to put in.
                        Ok(finish (tableOf g.Cols g.Rows) (List.length g.Rows) [||] evaluated caches)
                    else
                        // The group table as a frame of its own, slot = group index. A group is
                        // STABLE exactly when `groupStep` reused its aggregates — its row is then the
                        // row the tail last read — and the prior tail frame held it; a prime, a group
                        // that has just come into existence and a recomputed group are evaluated. The
                        // statement is sound here for the reason a source row's needs more care: a
                        // group row's cells are a function of its members alone, and `groupStep` has
                        // just recomputed exactly the groups whose members moved. No window runs over
                        // the group table.
                        //
                        // Its cache is the prior evaluation's `TailCells`, indexed by that
                        // evaluation's group slots (`GroupOrder`), and a separate cache from
                        // `StepCells` because the two are keyed by different token vocabularies — a
                        // source row's `Delta.refToken` and a group's `DataFrame.rowTokenString` —
                        // which must not meet in one keyspace. The step index restarts at 0;
                        // `evaluated` does not, because the footprint counts row evaluations on ONE
                        // scale across the pipeline.
                        let priorOrder = priorGroups.Order

                        let priorTail =
                            prior |> Option.map (fun s -> s.TailCells) |> Option.defaultValue [||]

                        let priorAt = System.Collections.Generic.Dictionary<string, int>(priorOrder.Length)

                        for j in 0 .. priorOrder.Length - 1 do
                            priorAt[priorOrder[j]] <- j

                        let groupOrder = g.Order
                        let groupCount = groupOrder.Length

                        let groupPrior =
                            groupOrder
                            |> Array.map (fun token ->
                                match priorAt.TryGetValue token with
                                | true, j -> j
                                | _ -> -1)

                        let groupRows =
                            { Tokens = groupOrder
                              Prior = groupPrior
                              Stable = Array.init groupCount (fun gi -> groupPrior[gi] >= 0 && not g.RecomputedAt[gi])
                              PriorSteps = priorTail
                              PriorCount = priorOrder.Length
                              Steps = ResizeArray<Cell[]>()
                              InPlace = null }

                        let rowArrays = List.toArray g.Rows
                        let width = List.length g.Cols

                        let groupFrame =
                            { Cols = g.Cols
                              Data = Array.init width (fun c -> rowArrays |> Array.map (fun row -> row[c]))
                              Origins = Array.create width None
                              Order = Array.init groupCount id }

                        // The SAME walk as the prefix, one frame along: its invariant is that the
                        // frame it holds is the frame the reference evaluator would have handed the
                        // next step, and `groupStep` mirrors `evalGroupBy` exactly.
                        walk resolve env priorCaches groupRows groupFrame evaluated caches tail
                        |> Result.map (fun (tailFrame, evaluated', caches') ->
                            finish
                                (tableOfFrame tailFrame groupCount)
                                tailFrame.Order.Length
                                (groupRows.Steps.ToArray())
                                evaluated'
                                caches')))

    /// Evaluate through the reference evaluator and wrap the answer in a cache-free state — the
    /// always-available degradation. A refresh over such a state re-primes, so a fall-back costs a
    /// full evaluation and never corrupts what follows it.
    ///
    /// `recompute` is handed the reference evaluator's OWN count of row evaluations at steps
    /// (Phase 117), so a footprint recorded here is on the same scale as one recorded by the
    /// restricted walk — the caller decides which case carries it.
    let private runReference
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (scheme: string)
        (pipeline: Transform list)
        (p: IncrementalPlan)
        (source: Table)
        (prepared: Prepared option)
        (recompute: int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        // Over the prepared frame where there is one (Phase 267), the `Table` boundary otherwise.
        (match prepared with
         | Some p -> DataFrame.evalPreparedCounted resolve env pipeline p
         | None -> DataFrame.evalPipelineWithInEnvCounted resolve env pipeline source)
        |> Result.map (fun (output, evaluated) ->
            { Plan = p
              Pipeline = pipeline
              Planned = pipeline
              Env = env
              Scheme = scheme
              Source = Prepared.ready source
              Prepared = prepared
              Output = Prepared.ready output
              Tokens = [||]
              StepCells = [||]
              RowGroups = [||]
              GroupMembers = [||]
              GroupAggs = [||]
              SortOrders = Map.empty
              JoinKeys = Map.empty
              Footprint =
                { SourceRows = Table.rowCount source
                  ResultRows = Table.rowCount output
                  Recompute = recompute evaluated }
              GroupOrder = [||]
              TailCells = [||]
              ChunkedOutput = None
              ChunksTouched = None
              SourceExact = [||]
              GroupRows = [||] })

    /// The shared entry: run the incremental path when the shape and the witness allow it, and the
    /// reference path otherwise. `named` is `None` for "every row".
    ///
    /// `onDeclined` says what a PLAN-LEVEL decline is reported as, and it differs by caller
    /// (Phase 117): a refresh reports `FullRecompute` with the declared reason, because a fall-back
    /// is exactly what happened; a prime reports `Primed`, because a prime avoids nothing whatever
    /// the plan says and had no state to fall back FROM. The other two reference branches take no
    /// such parameter, deliberately — a witness that cannot key the source is not discoverable from
    /// `plan`, so the footprint is the only place it can be reported, and a `plan`/`split`
    /// disagreement is a defect that should be visible wherever it happens.
    let private run
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        (prepared: Prepared option)
        (prior: IncrementalEval option)
        (named: Set<string> option)
        (known: KeyedIndex option)
        (inPlace: int[] option)
        (recomputeOf: int -> int -> Recompute)
        (onDeclined: FallBackReason -> int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        // Phase 269 — the seam runs the PLANNED pipeline: classified, split and evaluated in the
        // form `Plan.rewrite` gives it over the source's schema. The state records both forms —
        // the written one is what a refresh compares against (`pipelineOf`), the planned one is
        // what ran (`plannedOf`).
        let written = pipeline

        // Phase 323 — an in-place refresh is over the state's own pipeline, env and source schema
        // (`refreshWith` found nothing stale), so the planned form and its plan are the state's.
        let pipeline, p =
            match inPlace, prior with
            | Some _, Some s -> s.Planned, s.Plan
            | _ ->
                let planned = Plan.rewrite source.Schema pipeline
                planned, plan planned

        (match p.Strategy, split pipeline with
         | ReferenceOnly r, _ -> runReference resolve env idw.Scheme pipeline p source prepared (onDeclined r)
         | _, None ->
             // Unreachable while `plan` and `split` agree; the reference path is the safe reading of
             // a disagreement, so it is taken rather than asserted away.
             runReference resolve env idw.Scheme pipeline p source prepared (fun n -> FullRecompute(n, PipelineChanged))
         | _, Some(prefix, final) ->
             // Phase 208 — the prior evaluation's token array is handed to the minting so an unmoved
             // row's token comes back as the prior STRING INSTANCE; `runIncremental`'s positional
             // cache lookup is a pointer comparison off the back of that.
             let priorTokens =
                 prior |> Option.map (fun s -> s.Tokens) |> Option.defaultValue [||]

             // Phase 273 — keys the delta carried for this very source are reused, not re-minted.
             //
             // Phase 323 — in place (see `refreshWith`), the source's tokens ARE the prior's, row for
             // row: `tokensOfKnown` would return each prior instance, so the array is shared.
             let tokens =
                 match inPlace, known with
                 | Some _, Some k when priorTokens.Length = k.Keys.Length -> Ok priorTokens
                 | _, Some k -> Ok(tokensOfKnown k priorTokens)
                 | _, None -> tokensOf idw priorTokens source

             match tokens with
             | Error defect ->
                 runReference resolve env idw.Scheme pipeline p source prepared (fun n ->
                     FullRecompute(n, RowIdentityUnusable defect))
             | Ok tokens ->
                 runIncremental
                     resolve
                     env
                     idw.Scheme
                     pipeline
                     p
                     prefix
                     final
                     source
                     prepared
                     tokens
                     prior
                     named
                     inPlace
                     recomputeOf)
        |> Result.map (fun s ->
            { s with
                Pipeline = written
                Planned = pipeline })

    /// Evaluate `pipeline` over `source` from scratch, building the state a later `refresh`
    /// restricts. Equal to `DataFrame.evalPipelineWithInEnv resolve env pipeline source` — priming
    /// is a full evaluation that also records what it computed.
    ///
    /// A witness that cannot key the source uniquely does not fail the call: the pipeline is
    /// evaluated through the reference path and the state carries no caches, so a later refresh
    /// re-primes, and the footprint carries the defect. Identity is what the SEAM needs, not what
    /// the ANSWER needs.
    ///
    /// A pipeline the plan DECLINES primes to `Primed n` like any other (Phase 117). Priming a
    /// declined pipeline is not a fall-back — there was no prior state to fall back from, and a
    /// prime evaluates everything whatever the plan says. Ask `Incremental.plan` whether a refresh
    /// will be restricted; the prime's footprint answers what the prime cost.
    let prime
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        run resolve env idw pipeline source None None None None None (fun evaluated _ -> Primed evaluated) (fun _ n ->
            Primed n)

    // ---- Phase 268 — the chunked path ----
    //
    // A pipeline made only of `Derive`s keeps every row where it is, so its output is the source's
    // columns plus one column per step, and the value of a row depends on that row alone. Over a
    // prepared source that is a rope of chunks, such a pipeline is evaluated CHUNK BY CHUNK — the
    // steps folded over a frame of one chunk through the reference's own `evalStep`, so the cells
    // are the reference's cells — and a chunk the source shares with the version the state was
    // last evaluated over (the same object, in every column) is not evaluated at all: its output
    // chunks are the prior output's, by reference. A one-cell edit through
    // `ColumnOps.applyPrepared` moves one chunk of one column, so the refresh evaluates one chunk
    // and shares every other, and the output shares every unchanged chunk too — the floor the
    // row-local walk could not get under, because it is handed a whole table and has to read it.
    //
    // The delta is still required to be a true description of the change, and it still decides
    // staleness; but WHAT MOVED is read off the chunks, never off the delta, so a delta that names
    // more rows than moved costs nothing and one that names fewer cannot make the answer wrong.
    //
    // What the path must get right that a chunk cannot see on its own: a derived column's TYPE.
    // The reference decides it from the schema where the typer decides it, and from the whole
    // column's present cells where it does not (Phase 338) — so a chunk types a cells-decided
    // column by its own cells alone, and a later derive reading that column over the chunk's
    // schema. The types are therefore fixed here over the whole rope, after the chunks are in
    // hand, by replaying the same rule; and a chunk holding no present cell is repacked under the
    // column's kind, so the rope's view is one typed vector rather than a boxed one.

    /// The `Derive` steps of a pipeline made only of them, in order; `None` for any other pipeline.
    let private deriveSteps (pipeline: Transform list) : (string * ColExpr) list option =
        pipeline
        |> mapM (function
            | Derive(name, expr) -> Some(name, expr)
            | _ -> None)

    /// The cells' half of the derived-column rule read off a rope (`DataFrame.typeFromCells`,
    /// Phase 338): the present cells' types joined over the chunks in row order (Phase 321's
    /// widening join), `StringType` where every cell is absent, and the named refusal where the
    /// rope holds a float beside a decimal. A typed chunk holds one type, so it folds as that type
    /// once (a mask scan that stops at its first present row); a boxed chunk folds cell by cell, in
    /// row order, because the fold keeps the EARLIER type for a pair no widening relates and so
    /// cannot combine per-chunk summaries.
    let private ropeType (column: string) (chunks: Vec[]) : Result<ColumnType, EvalError> =
        let mutable acc: ColumnType option = None
        let mutable sawFloat = false
        let mutable sawDecimal = false

        let fold (t: ColumnType) =
            match t with
            | FloatType -> sawFloat <- true
            | DecimalType -> sawDecimal <- true
            | IntType
            | BoolType
            | StringType
            | DateType
            | TimestampType -> ()

            acc <-
                match acc with
                | None -> Some t
                | Some a -> Some(DataFrame.widenColumnType a t)

        for v in chunks do
            match v with
            | Ints(_, m) ->
                if Array.exists id m then
                    fold IntType
            | Floats(_, m) ->
                if Array.exists id m then
                    fold FloatType
            | Bools(_, m) ->
                if Array.exists id m then
                    fold BoolType
            | Strs(ty, _, m) ->
                if Array.exists id m then
                    fold ty
            | Decs(_, _, _, m) ->
                if Array.exists id m then
                    fold DecimalType
            | Cells a ->
                for c in a do
                    match Cell.typeOf c with
                    | Some t -> fold t
                    | None -> ()

        if sawFloat && sawDecimal then
            Error(DataFrame.floatBesideDecimal column)
        else
            Ok(acc |> Option.defaultValue StringType)

    /// Does the vector hold no present cell at all?
    let private nonePresent (v: Vec) : bool =
        match v with
        | Ints(_, m)
        | Floats(_, m)
        | Bools(_, m)
        | Strs(_, _, m)
        | Decs(_, _, _, m) -> not (Array.exists id m)
        | Cells a -> a |> Array.forall (fun c -> c = Null)

    /// Evaluate the `Derive` steps over a prepared source chunk by chunk, reusing the prior
    /// output's chunks wherever the source's chunk is the prior source's own object in every
    /// column. Answers the output as a prepared version, the row evaluations it cost (one per row
    /// per step, the reference's own unit) and the chunks it evaluated. An error from any chunk is
    /// answered as the error — the caller re-runs the reference path, whose error is the one to
    /// report.
    let private evalChunked
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (steps: (string * ColExpr) list)
        (prepared: Prepared)
        (prior: (Prepared * Prepared) option)
        : Result<Prepared * int * int, EvalError> =
        let columns = Prepared.columns prepared
        let w = columns.Length
        let count = prepared.Count
        let size = Chunked.rows
        let chunkCount = Chunked.count size count
        let transforms = steps |> List.map Derive

        let priorColumns =
            prior |> Option.map (fun (ps, po) -> Prepared.columns ps, Prepared.columns po)

        // Chunk `k` of every source column is the prior source's own object.
        let unchanged (k: int) : bool =
            match priorColumns with
            | Some(ps, po) when ps.Length = w && po.Length > 0 ->
                Array.forall2
                    (fun (c: Chunked) (pc: Chunked) ->
                        pc.Size = size
                        && k < pc.Chunks.Length
                        && obj.ReferenceEquals(c.Chunks[k], pc.Chunks[k]))
                    columns
                    ps
            | _ -> false

        let evalOver (f0: Frame) : Result<Frame, EvalError> =
            transforms
            |> List.fold (fun acc step -> acc |> Result.bind (fun f -> DataFrame.evalStep resolve env f step)) (Ok f0)

        let evalChunk (k: int) : Result<Frame, EvalError> =
            evalOver
                { Cols = prepared.Cols
                  Vecs = columns |> Array.map (fun c -> c.Chunks[k])
                  Origins = Array.create w None
                  Sel = None
                  Count = Chunked.lengthOf size count k }

        let fresh: Frame option[] = Array.create chunkCount None
        let mutable failed = None
        let mutable touched = 0
        let mutable evaluated = 0
        let mutable k = 0

        while Option.isNone failed && k < chunkCount do
            if not (unchanged k) then
                match evalChunk k with
                | Error e -> failed <- Some e
                | Ok f ->
                    fresh[k] <- Some f
                    touched <- touched + 1
                    evaluated <- evaluated + Chunked.lengthOf size count k * List.length steps

            k <- k + 1

        match failed, prior with
        | Some e, _ -> Error e
        | None, Some(_, po) when touched = 0 && chunkCount > 0 -> Ok(po, 0, 0)
        | None, _ ->
            // The output's names and order — the same for every chunk, so read off any evaluated
            // one; off the prior output where none was; off an evaluation over no rows where the
            // source has none.
            let names =
                match fresh |> Array.tryPick id with
                | Some f -> Ok f.Cols
                | None ->
                    match prior with
                    | Some(_, po) when chunkCount > 0 -> Ok po.Cols
                    | _ ->
                        evalOver
                            { Cols = prepared.Cols
                              Vecs = columns |> Array.map (fun c -> Vec.pack c.Type [||])
                              Origins = Array.create w None
                              Sel = None
                              Count = 0 }
                        |> Result.map (fun f -> f.Cols)

            names
            |> Result.bind (fun cols ->
                let derived = steps |> List.map fst |> Set.ofList
                let priorOut = priorColumns |> Option.map snd

                let chunksOf (oi: int) (tyLocal: ColumnType) : Vec[] =
                    Array.init chunkCount (fun k ->
                        match fresh[k], priorOut with
                        | Some f, _ -> f.Vecs[oi]
                        | None, Some po -> po[oi].Chunks[k]
                        | None, None -> Vec.pack tyLocal [||]) // unreachable: an unshared chunk was evaluated

                // The derived columns' types over the WHOLE rope (Phase 338): the reference's rule
                // replayed over the rope's schema step by step, because a chunk typed a column the
                // cells decide by its own cells alone, and a later derive reading that column was
                // typed over the chunk's schema. A decided column takes its typing's type, one the
                // cells decide takes the rope's cells (refusing a float beside a decimal). An answer
                // the rope cannot give — the cells of a column a later step derives again, which the
                // output no longer holds — is declined, and the caller takes the row path.
                let lastAt = steps |> List.mapi (fun i (name, _) -> name, i) |> Map.ofList

                let rec replay (schema: Schema) (i: int) (acc: Map<string, ColumnType>) rest =
                    match rest with
                    | [] -> Ok acc
                    | (name, e) :: tail ->
                        let typed =
                            match DataFrame.derivedTyping schema e with
                            | DataFrame.Decided ty -> Ok ty
                            | DataFrame.Refused -> Error(DataFrame.floatBesideDecimal name)
                            | DataFrame.ByCells ->
                                match cols |> List.tryFindIndex (fun (n, _) -> n = name) with
                                | Some oi when lastAt[name] = i -> ropeType name (chunksOf oi (snd cols[oi]))
                                | _ ->
                                    Error(
                                        TypeError(
                                            "the chunked path cannot type '"
                                            + name
                                            + "' over the rope; the row path does"
                                        )
                                    )

                        typed
                        |> Result.bind (fun ty ->
                            let schema' =
                                if schema |> List.exists (fun (n, _) -> n = name) then
                                    schema |> List.map (fun (n, t) -> if n = name then n, ty else n, t)
                                else
                                    schema @ [ name, ty ]

                            replay schema' (i + 1) (Map.add name ty acc) tail)

                replay prepared.Cols 0 Map.empty steps
                |> Result.map (fun ropeTypes ->

                    let outColumns =
                        cols
                        |> List.mapi (fun oi (name, tyLocal) ->
                            let chunks = chunksOf oi tyLocal

                            let ty =
                                if Set.contains name derived then
                                    ropeTypes[name]
                                else
                                    tyLocal

                            let chunks =
                                chunks
                                |> Array.map (fun v ->
                                    if Vec.declaredType v <> Some ty && nonePresent v then
                                        Vec.pack ty (Array.create (Vec.length v) Null)
                                    else
                                        v)

                            // A column the pipeline passes through untouched IS the source's rope —
                            // the same object, its list memo included — so the output's table hands
                            // the consumer's own list back for it, as the frame boundary does.
                            let passThrough =
                                not (Set.contains name derived)
                                && oi < w
                                && fst (List.item oi prepared.Cols) = name
                                && columns[oi].Chunks.Length = chunkCount

                            (name, ty),
                            (if passThrough then
                                 columns[oi]
                             else
                                 { Type = ty
                                   Size = size
                                   Length = count
                                   Chunks = chunks
                                   Cells = ref None }))

                    let out =
                        Prepared.ofChunks
                            (outColumns |> List.map fst)
                            count
                            (outColumns |> List.map snd |> List.toArray)

                    out, evaluated, touched))

    /// The state the chunked path hands back: the prepared source and its chunked output, no
    /// row caches (the chunks ARE the cache), and the count of chunks it evaluated.
    let private chunkedState
        (pipeline: Transform list)
        (env: Map<string, Cell>)
        (scheme: string)
        (prepared: Prepared)
        (out: Prepared)
        (touched: int)
        (recompute: Recompute)
        : IncrementalEval =
        { Plan = plan pipeline
          Pipeline = pipeline
          Planned = pipeline
          Env = env
          Scheme = scheme
          Source = prepared.Source
          Prepared = Some prepared
          Output = out.Source
          Tokens = [||]
          StepCells = [||]
          RowGroups = [||]
          GroupMembers = [||]
          GroupAggs = [||]
          SortOrders = Map.empty
          JoinKeys = Map.empty
          Footprint =
            { SourceRows = prepared.Count
              ResultRows = out.Count
              Recompute = recompute }
          GroupOrder = [||]
          TailCells = [||]
          ChunkedOutput = Some out
          ChunksTouched = Some touched
          SourceExact = [||]
          GroupRows = [||] }

    /// `prime` over a source prepared once (`DataFrame.prepare`; Phase 267): the state `prime`
    /// builds over the prepared table — equal to it in every field a consumer can read — with the
    /// reference path evaluating over the prepared frame rather than paying the `Table` boundary
    /// again, and the prepared form held in the state.
    ///
    /// Phase 268 — over a pipeline made only of `Derive`s the state is built by the chunked path
    /// instead: the same result and the same footprint, with the output held as a chunked version
    /// beside the source so that `refreshPrepared` can recognise, chunk by chunk, what the next
    /// version did not move. Such a state carries no row caches; a `refresh` over a bare table
    /// after it walks every row once and rebuilds them.
    let primePrepared
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<IncrementalEval, EvalError> =
        let rowPath () =
            run
                resolve
                env
                idw
                pipeline
                (Prepared.table prepared)
                (Some prepared)
                None
                None
                None
                None
                (fun evaluated _ -> Primed evaluated)
                (fun _ n -> Primed n)

        match deriveSteps pipeline with
        | None -> rowPath ()
        | Some steps ->
            match evalChunked resolve env steps prepared None with
            | Ok(out, evaluated, touched) ->
                Ok(chunkedState pipeline env idw.Scheme prepared out touched (Primed evaluated))
            | Error _ -> rowPath ()

    /// The schema the state's source carries — read off the prepared form where there is one, so
    /// a version built from chunks is never forced into a table just to compare its schema.
    let private sourceSchema (state: IncrementalEval) : Schema =
        match state.Prepared with
        | Some p -> p.Cols
        | None -> state.Source.Value.Schema

    /// The refresh over a table, with the source's prepared form where the caller holds one
    /// (`refreshPrepared` over a pipeline the chunked path does not admit) — the shared body of
    /// `refresh` and `refreshPrepared`.
    let private refreshWith
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (source: Table)
        (given: Prepared option)
        : Result<IncrementalEval, EvalError> =
        let stale =
            if pipeline <> state.Pipeline then
                Some PipelineChanged
            elif env <> state.Env then
                Some EnvChanged
            elif idw.Scheme <> state.Scheme then
                Some(RowIdentityUnusable(SchemeMismatch(state.Scheme, idw.Scheme)))
            elif source.Schema <> sourceSchema state then
                Some SourceSchemaMoved
            else
                match delta with
                | FullRefresh -> Some DeltaIsFullRefresh
                | RowSet r ->
                    if r.Scheme = RowIdentity.ordinalScheme then
                        Some OrdinalAddressing
                    elif r.Scheme <> idw.Scheme then
                        Some(RowIdentityUnusable(SchemeMismatch(r.Scheme, idw.Scheme)))
                    else
                        None

        // Phase 267 — the state's prepared form is reused only where this refresh's source IS the
        // table it was prepared from: the same object, so the same cells.
        let reusable =
            match given, state.Prepared with
            | Some p, _ -> Some p
            | None, Some p when Prepared.isFrom p source -> Some p
            | _ -> None

        // Phase 273 — the new source's keys, when the delta is the one `Delta.diff` built into this
        // very source under this scheme. Anything else (hand-built, composed, decoded, ordinal, a
        // full refresh, or a diff into some other table) carries none and the refresh mints.
        let known = KeyedIndexes.carried idw.Scheme delta source

        // Phase 323 — every row in place against the very table this state was evaluated over: the
        // refresh may then pay for the changed rows rather than for the table (`runIncremental`).
        // Anything else — another `before`, a hand-built delta, a moved, added or removed row —
        // takes the general walk.
        let inPlace =
            match KeyedIndexes.inPlaceOf delta source with
            | Some(from, changed) when
                state.Source.IsValueCreated
                && obj.ReferenceEquals(from, state.Source.Value)
                && state.Tokens.Length > 0
                ->
                Some changed
            | _ -> None

        match stale with
        | Some r ->
            // The answer is a full evaluation either way; taking it through the incremental path
            // (with every row named) re-primes the caches, so the NEXT refresh can restrict again.
            // Every row is named, so the count the walk returns is what a full evaluation costs.
            run
                resolve
                env
                idw
                pipeline
                source
                reusable
                None
                None
                known
                None
                (fun evaluated _ -> FullRecompute(evaluated, r))
                (fun declined n -> FullRecompute(n, declined))
        | None ->
            // A quiet delta over a source that is byte-identical to the one the state was evaluated
            // against is the one case where the prior result can be handed back untouched. The
            // source comparison is not ceremony: a delta built by `Delta.diff` is quiet for a pure
            // ROW REORDERING too, and the reference evaluator's output order would have moved.
            //
            // This reuse is sound for EVERY strategy, declined pipelines included — a verb is
            // declined because it cannot answer a change, not because it must be re-run when
            // nothing changed. It is reached before the strategy dispatch for exactly that reason.
            //
            // The third condition is Phase 120's. "Nothing changed" is a claim about everything the
            // answer depends on, and a pipeline naming a `Ref` relation depends on what `resolve`
            // returns — which is not the pipeline, not the env and not the source, so none of the
            // state's comparisons can see it move. Such a pipeline takes the ordinary path instead,
            // where the relation is resolved and its key index compared against the cached one; a
            // relation that did not move still costs nothing there, because a join evaluates no
            // expression.
            if
                Delta.isQuiet delta
                && source = state.Source.Value
                && not (readsExternalSource pipeline)
            then
                Ok
                    { state with
                        Footprint =
                            { SourceRows = Table.rowCount source
                              ResultRows = Table.rowCount state.Output.Value
                              Recompute = ReusedPrior } }
            else
                let named =
                    match delta with
                    | FullRefresh -> None
                    | RowSet r ->
                        // Column invalidation names no rows, so every row is suspect — the honest
                        // reading of "these columns' values can no longer be trusted".
                        if List.isEmpty r.InvalidatedColumns then
                            Some(namedTokens delta)
                        else
                            None

                run
                    resolve
                    env
                    idw
                    pipeline
                    source
                    reusable
                    (Some state)
                    named
                    known
                    inPlace
                    (fun evaluated groups ->
                        match state.Plan.Strategy with
                        | RowLocalThenGroups -> GroupsRecomputed(evaluated, groups)
                        | _ -> RowsRecomputed evaluated)
                    (fun declined n -> FullRecompute(n, declined))

    /// Advance a state against a delta describing the change from the state's source to `source`.
    /// The result equals a full `DataFrame.evalPipelineWithInEnv` over `source` — always, for every
    /// delta, whichever path was taken.
    ///
    /// The delta must truthfully describe the change (`Delta.diff` produces exactly that). Anything
    /// the incremental path cannot honour degrades to a full evaluation with the reason recorded in
    /// the returned footprint.
    let refresh
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        refreshWith resolve env idw pipeline state delta source None

    /// `refresh` against a prepared version of the source (Phase 268) — the shape a consumer that
    /// edits through `ColumnOps.applyPrepared` holds. The result equals `refresh` over
    /// `DataFrame.toTable prepared`, for every delta.
    ///
    /// Over a pipeline made only of `Derive`s this is the chunked path: a chunk the version shares
    /// with the one the state was last evaluated over — the same object, in every column — is
    /// recognised by identity and its output chunks reused; only the chunks the version moved are
    /// evaluated, and the output shares every chunk it did not. The footprint counts the rows those
    /// chunks hold, `chunksTouched` the chunks. A state another path built (a `prime` over a table,
    /// a `refresh` over one) has no chunked output to share, so the first chunked refresh over it
    /// evaluates every chunk and the next reuses them. A pipeline the chunked path does not admit
    /// takes the row-local walk over the version's table, exactly as `refresh` would, with the
    /// prepared form handed through so the reference path pays no boundary.
    let refreshPrepared
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (prepared: Prepared)
        : Result<IncrementalEval, EvalError> =
        let rowPath () =
            refreshWith resolve env idw pipeline state delta (Prepared.table prepared) (Some prepared)

        match deriveSteps pipeline with
        | None -> rowPath ()
        | Some steps ->
            // The four conditions that make the prior output unusable, in `refresh`'s order and
            // reported as it reports them. A delta that is a full refresh, or that addresses rows
            // the state cannot key, is NOT one of them here: what moved is read off the chunks,
            // and a delta saying "everything may have" costs only what actually did.
            let stale =
                if pipeline <> state.Pipeline then
                    Some PipelineChanged
                elif env <> state.Env then
                    Some EnvChanged
                elif idw.Scheme <> state.Scheme then
                    Some(RowIdentityUnusable(SchemeMismatch(state.Scheme, idw.Scheme)))
                elif prepared.Cols <> sourceSchema state then
                    Some SourceSchemaMoved
                else
                    None

            let prior =
                match stale, state.Prepared, state.ChunkedOutput with
                | None, Some ps, Some po -> Some(ps, po)
                | _ -> None

            match evalChunked resolve env steps prepared prior with
            | Ok(out, evaluated, touched) ->
                let recompute =
                    match stale with
                    | Some r -> FullRecompute(evaluated, r)
                    | None ->
                        if touched = 0 then
                            ReusedPrior
                        else
                            RowsRecomputed evaluated

                Ok(chunkedState pipeline env idw.Scheme prepared out touched recompute)
            | Error _ -> rowPath ()

    /// `prime` over embedded sources with no params — the everyday call.
    let primeOn
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        prime DataFrame.noResolve Map.empty idw pipeline source

    /// `primePrepared` over embedded sources with no params.
    let primeOnPrepared
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<IncrementalEval, EvalError> =
        primePrepared DataFrame.noResolve Map.empty idw pipeline prepared

    /// `refresh` over embedded sources with no params — the everyday call.
    let refreshOn
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        refresh DataFrame.noResolve Map.empty idw pipeline state delta source

    /// `refreshPrepared` over embedded sources with no params.
    let refreshOnPrepared
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (prepared: Prepared)
        : Result<IncrementalEval, EvalError> =
        refreshPrepared DataFrame.noResolve Map.empty idw pipeline state delta prepared

    // ---- reading a state (Phase 208) ----
    //
    // The state's representation is private, so these are the whole of what a consumer can read from
    // one. The set is MEASURED rather than designed: every in-repo reader of an `IncrementalEval`
    // read `Output` or `Footprint` and nothing else, and the two accessors for those predate this
    // phase. `strategy` and `plan'` are here because a consumer that holds a state and wants to know
    // whether its next refresh will be restricted should not have to re-run `plan` over the pipeline
    // to find out, and `source` because the state pins the table the next delta must be measured
    // against.

    /// The result the state currently holds — built from its chunks on the first read where the
    /// chunked path produced it (Phase 268), and kept.
    let result (s: IncrementalEval) : Table = s.Output.Value

    /// Phase 268 — the result as a prepared source: the chunked version the chunked path produced,
    /// sharing every chunk the source did not move, or the result prepared afresh where another
    /// path built the state. What a node feeding another node hands on, so the boundary is never
    /// paid between them.
    let resultPrepared (s: IncrementalEval) : Prepared =
        match s.ChunkedOutput with
        | Some p -> p
        | None -> DataFrame.prepare s.Output.Value

    /// Phase 268 — how many chunks the chunked path evaluated to produce this state; `None` where
    /// the state was built by the row-local walk or the reference path. The count the cost tests
    /// hold a one-cell edit to: one chunk, at any row count.
    let chunksTouched (s: IncrementalEval) : int option = s.ChunksTouched

    /// What producing that result cost.
    let footprint (s: IncrementalEval) : RecomputeFootprint = s.Footprint

    /// The classification the state was built under — the same value `plan` returns for the state's
    /// own pipeline.
    ///
    /// Named `plan'` because `plan` is this module's pipeline classifier and the two are deliberately
    /// the same answer read two ways: a consumer that has a state reads it here, and one that has
    /// only a pipeline computes it there.
    let plan' (s: IncrementalEval) : IncrementalPlan = s.Plan

    /// Phase 269 — the PLANNED pipeline the state ran: `Plan.rewrite` over the source's schema of
    /// the pipeline `pipelineOf` reports. It is the form `plan'` classified; `planOver` computes
    /// the same pair from a schema and a pipeline with no state in hand.
    let plannedOf (s: IncrementalEval) : Transform list = s.Planned

    /// How the state's next refresh will be answered: row-local propagation, a maintained grouping,
    /// or the reference evaluator.
    let strategy (s: IncrementalEval) : IncrementalStrategy = s.Plan.Strategy

    /// The source the state was last evaluated against — the `before` table a delta handed to the
    /// next `refresh` must describe the change FROM.
    let source (s: IncrementalEval) : Table = s.Source.Value

    /// The pipeline the state was built for. A refresh with any other pipeline evaluates in full
    /// (`PipelineChanged`), so this is what a consumer holding a state compares against.
    let pipelineOf (s: IncrementalEval) : Transform list = s.Pipeline

    // ---- the one-shot form ----

    /// Prime over `before`, then refresh against `delta` and `after`, in one call — the shape a
    /// conformance check and a first adoption both want. The returned state's `Output` is the new
    /// result and its `Footprint` accounts for the REFRESH, not for the prime.
    let internal evalDelta
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (before: Table)
        (delta: TableDelta)
        (after: Table)
        : Result<IncrementalEval, EvalError> =
        prime resolve env idw pipeline before
        |> Result.bind (fun state -> refresh resolve env idw pipeline state delta after)
