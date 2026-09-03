namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame — the incremental `Transform` evaluation seam
//  (Phase 99). A pipeline evaluated against a DELTA (the Phase 98
//  representation) rather than from scratch: the rows a delta names are
//  re-evaluated, every other row's value is reused, and a step that cannot
//  answer a delta SAYS SO in the type rather than guessing.
//
//  Three properties are load-bearing.
//
//   * THE REFERENCE EVALUATOR STAYS THE ORACLE. Everything here is a
//     restriction of `DataFrame.evalPipeline`, never a second semantics: a
//     re-evaluated cell goes through `DataFrame.evalExprInRow`, a recomputed
//     aggregate through `DataFrame.aggregateCells`, a derived column's type
//     through `DataFrame.inferCellType`. One implementation, called on fewer
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
    /// A step that WOULD be maintainable, sitting somewhere other than last. Its output rows are
    /// groups, so a delta over the source rows says nothing about a delta over the group table, and
    /// the steps after it would be reasoning about a change nobody described.
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
/// The fields are public because the columnar strand keeps its data transparent, but they are
/// ENGINE-OWNED: build one with `Incremental.prime` and advance it with `Incremental.refresh`. A
/// hand-built state whose caches disagree with its source is a lie the evaluator cannot detect.
type IncrementalEval =
    {
        /// The classification the state was built under.
        Plan: IncrementalPlan
        /// The pipeline it was built for — a refresh with a different pipeline evaluates in full.
        Pipeline: Transform list
        /// The evaluation env it was built under — a refresh with a different env evaluates in full.
        Env: Map<string, Cell>
        /// The identity scheme its row tokens were minted under.
        Scheme: string
        /// The source it was last evaluated against.
        Source: Table
        /// The pipeline's result over that source.
        Output: Table
        /// Per row token, the cells the row-local steps evaluated for it, in step order. A row a
        /// `Filter` dropped keeps the (shorter) prefix it reached, so the drop verdict is cached too.
        RowCells: Map<string, Cell list>
        /// Per row token, the group token its row landed in (maintained-groups strategy only).
        RowGroup: Map<string, string>
        /// Per group token, its members' row tokens IN ORDER — what makes reusing a cached aggregate
        /// safe for the order-sensitive aggregates (maintained-groups only).
        GroupMembers: Map<string, string list>
        /// Per group token, the aggregate cells last computed for it (maintained-groups only).
        GroupAggs: Map<string, Cell list>
        /// Per `Sort` step (indexed by its ordinal among the pipeline's sorts), the token order the
        /// step's rows ARRIVED in and the token order it PRODUCED. Both halves are needed and
        /// neither is redundant: the produced order is what a merge reuses, and the arrival order is
        /// the only thing that says the reuse is still valid — a stable sort breaks ties by arrival
        /// position, so a cached order whose unnamed rows arrived in a different order now would
        /// merge those ties the wrong way round. `Delta.diff` reports a pure reordering as quiet, so
        /// this cannot be inferred from the delta.
        SortOrders: Map<int, string list * string list>
        /// Per admitted `Join` step (indexed by its ordinal among the pipeline's admitted joins),
        /// the joined relation's KEY INDEX as of this evaluation: its rows' key cells, projected
        /// through the step's `on` pairs, in the relation's own row order (Phase 120).
        ///
        /// It is what says a cached match verdict is still valid. A filtering join's verdict for a
        /// row depends on the row AND on the relation, and the delta describes only the source — so
        /// a verdict reused because "the delta did not name this row" would answer a changed
        /// relation with the previous relation's answer. The key index is the whole of what the
        /// verdict reads, so a relation that moved in some other column legitimately keeps the
        /// reuse; one whose keys moved does not.
        JoinKeys: Map<int, Cell list list>
        /// What producing `Output` cost.
        Footprint: RecomputeFootprint
    }

/// The incremental evaluation seam: classify a pipeline, prime a state over a source, then refresh
/// that state against a delta. Every result equals `DataFrame.evalPipelineWithInEnv` over the same
/// source — certified, not asserted.
[<RequireQualifiedAccess>]
module Incremental =

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
    let internal classifyStep (isLast: bool) (t: Transform) : StepIncrementality =
        match t with
        | Filter _
        | Project _
        | Derive _ -> PropagateRows
        | Sort by -> MergeOrder by
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
        | GroupBy(keys, aggs) ->
            if isLast then
                MaintainGroups(keys, aggs |> List.map (fun a -> a.Name))
            else
                FallBack(AggregateStepNotLast(verbName t))
        | other -> FallBack(StepNotRowLocal(verbName other))

    /// Classify a whole pipeline. An empty pipeline is `RowLocal` (the identity is trivially
    /// row-local). The FIRST fall-back reason in step order is the pipeline's reason — reporting
    /// the first is what keeps the answer stable as earlier steps are fixed.
    let plan (pipeline: Transform list) : IncrementalPlan =
        let n = List.length pipeline
        let steps = pipeline |> List.mapi (fun i t -> classifyStep (i = n - 1) t)

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

    let private traverse (f: 'a -> Result<'b, EvalError>) (xs: 'a list) : Result<'b list, EvalError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun y -> go (y :: acc) rest)

        go [] xs

    let private rowsOf (t: Table) : Cell list list =
        let n = Table.rowCount t

        [ for i in 0 .. n - 1 ->
              t.Schema
              |> List.map (fun (name, _) ->
                  match Table.tryColumn name t with
                  | Some c -> Column.cell i c
                  | None -> Null) ]

    let private tableOf (cols: Schema) (rows: Cell list list) : Table =
        { Schema = cols
          Columns =
            cols
            |> List.mapi (fun ci (name, ty) -> Column.create name ty (rows |> List.map (List.item ci))) }

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

    /// Split a pipeline into its propagating prefix and an optional final `GroupBy`. `None` when the
    /// pipeline is not of the incremental shape — exactly when `plan` says `ReferenceOnly`.
    let private split (pipeline: Transform list) : (PrefixStep list * (string list * Agg list) option) option =
        let rec go acc sorts joins =
            function
            | [] -> Some(List.rev acc, None)
            | [ GroupBy(keys, aggs) ] -> Some(List.rev acc, Some(keys, aggs))
            | Filter p :: rest -> go (WFilter p :: acc) sorts joins rest
            | Project pairs :: rest -> go (WProject pairs :: acc) sorts joins rest
            | Derive(n, e) :: rest -> go (WDerive(n, e) :: acc) sorts joins rest
            | Sort by :: rest -> go (WSort(sorts, by) :: acc) (sorts + 1) joins rest
            | Window spec :: rest -> go (WWindow spec :: acc) sorts joins rest
            | Join(src, on, Semi) :: rest -> go (WJoin(joins, src, on, true) :: acc) sorts (joins + 1) rest
            | Join(src, on, Anti) :: rest -> go (WJoin(joins, src, on, false) :: acc) sorts (joins + 1) rest
            | _ -> None

        go [] 0 0 pipeline

    /// One source row in flight: its identity token, whether the delta named it, whether it is still
    /// alive after the filters so far, its current cells, the cells cached for it by the previous
    /// evaluation, and the cells evaluated for it this time (reversed while accumulating).
    type private Work =
        { Token: string
          Affected: bool
          Alive: bool
          Cells: Cell list
          Cached: Cell list
          Fresh: Cell list }

    /// The value a row contributes at this evaluating step: the cache when the delta did not name
    /// the row and the cache reaches this far, otherwise a fresh evaluation. Returns the cell and
    /// whether it cost an evaluation — the footprint's unit of work.
    let private cellAt
        (env: Map<string, Cell>)
        (cols: Schema)
        (evalIdx: int)
        (expr: ColExpr)
        (w: Work)
        : Result<Cell * bool, EvalError> =
        let cached = if w.Affected then None else List.tryItem evalIdx w.Cached

        match cached with
        | Some c -> Ok(c, false)
        | None -> DataFrame.evalExprInRow env cols w.Cells expr |> Result.map (fun c -> c, true)

    /// Merge two already-ordered token sequences into one, under the reference comparator, breaking
    /// a tie by ARRIVAL position. That tiebreak is what makes the merge equal to `List.sortWith` over
    /// the whole frame: `List.sortWith` is stable, and a stable sort is exactly a sort by
    /// (key, arrival position). Dropping it would leave an order that is correctly sorted and
    /// differs from the reference's on the first tie.
    let private mergeOrders
        (cmp: string -> string -> int)
        (posOf: Map<string, int>)
        (xs: string list)
        (ys: string list)
        : string list =
        let posAt t =
            Map.tryFind t posOf |> Option.defaultValue System.Int32.MaxValue

        let rec go acc xs ys =
            match xs, ys with
            | [], rest
            | rest, [] -> List.rev acc @ rest
            | x :: xt, y :: yt ->
                let c = cmp x y
                let c = if c <> 0 then c else compare (posAt x) (posAt y)

                if c <= 0 then go (x :: acc) xt ys else go (y :: acc) xs yt

        go [] xs ys

    /// The per-step state the walk reads from the prior evaluation and writes for the next one,
    /// carried as one value rather than as a parameter per admitted step. It is the shape of every
    /// state-keeping step's cache: keyed by that step's ordinal in the pipeline, valid only while
    /// the pipeline is the one the state was built for.
    type private WalkCaches =
        { SortOrders: Map<int, string list * string list>
          JoinKeys: Map<int, Cell list list> }

    let private noCaches: WalkCaches =
        { SortOrders = Map.empty
          JoinKeys = Map.empty }

    /// Walk the propagating prefix, threading the schema and the rows. Dead rows are carried (so
    /// their cached prefix survives for the next refresh) but never evaluated and never counted —
    /// exactly as in the reference evaluator, which has already dropped them from its frame.
    ///
    /// `prior` is the state's caches and is read-only; `caches` accumulates the ones this walk
    /// produced, which is what the next refresh will read.
    let rec private walk
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (prior: WalkCaches)
        (cols: Schema)
        (works: Work list)
        (evalIdx: int)
        (evaluated: int)
        (caches: WalkCaches)
        (steps: PrefixStep list)
        : Result<Schema * Work list * int * WalkCaches, EvalError> =
        match steps with
        | [] -> Ok(cols, works, evaluated, caches)
        | WSort(sortIdx, by) :: rest ->
            // The rows the sort orders, in the order they arrived. Dead rows are not in the frame
            // at all — the reference dropped them at the filter that killed them — so they are
            // carried past the step (their cached prefix is still wanted) but take no part in it.
            let aliveWorks = works |> List.filter (fun w -> w.Alive)
            let deadWorks = works |> List.filter (fun w -> not w.Alive)
            let arrival = aliveWorks |> List.map (fun w -> w.Token)

            let byToken = (Map.empty, aliveWorks) ||> List.fold (fun m w -> Map.add w.Token w m)

            let posOf =
                (Map.empty, List.indexed arrival) ||> List.fold (fun m (i, t) -> Map.add t i m)

            // The reference's own comparator, over the reference's own cells. A second comparator
            // here would agree on every corpus anyone thought to write and disagree on the first
            // null, the first tie and the first misspelled key.
            let cmp (a: string) (b: string) =
                match Map.tryFind a byToken, Map.tryFind b byToken with
                | Some wa, Some wb -> DataFrame.rowCompareBy cols by wa.Cells wb.Cells
                | _ -> 0

            let unnamed =
                aliveWorks
                |> List.filter (fun w -> not w.Affected)
                |> List.map (fun w -> w.Token)
                |> Set.ofList

            // The cached order may be reused only for rows that arrived in the SAME relative order
            // as last time. A stable sort breaks ties by arrival position, so a reordering among
            // unnamed rows moves the answer while naming no row at all — and `Delta.diff` reports a
            // pure reordering as quiet, so nothing in the delta would have said so. This is the
            // ordered-member condition the maintained groups already carry, one verb along.
            let reusable =
                match Map.tryFind sortIdx prior.SortOrders with
                | None -> None
                | Some(prevArrival, prevOrder) ->
                    let keep = List.filter (fun t -> Set.contains t unnamed)

                    if keep prevArrival = keep arrival then
                        Some(keep prevOrder)
                    else
                        None

            let ordered =
                match reusable with
                | None -> arrival |> List.sortWith cmp
                | Some cachedUnnamed ->
                    let named =
                        arrival
                        |> List.filter (fun t -> not (Set.contains t unnamed))
                        |> List.sortWith cmp

                    mergeOrders cmp posOf cachedUnnamed named

            let works2 = (ordered |> List.map (fun t -> Map.find t byToken)) @ deadWorks

            walk
                resolve
                env
                prior
                cols
                works2
                evalIdx
                evaluated
                { caches with
                    SortOrders = Map.add sortIdx (arrival, ordered) caches.SortOrders }
                rest
        // Phase 120 — a bounded-frame `Window`. The column is recomputed over the rows alive AT
        // THIS STEP, in the order they are in, through the reference's own window step: the walked
        // frame IS the reference's frame here (that is the walk's invariant, and the same one the
        // maintained `GroupBy` and the type-inferring `Derive` read), so the appended column is the
        // reference's column by construction rather than by a second implementation agreeing with
        // it. `evalIdx` does not advance and `evaluated` does not move: a window evaluates no
        // expression, so it caches no cell and the reference's own counter charges it nothing.
        | WWindow spec :: rest ->
            let aliveWorks = works |> List.filter (fun w -> w.Alive)
            let deadWorks = works |> List.filter (fun w -> not w.Alive)

            DataFrame.windowStep cols (aliveWorks |> List.map (fun w -> w.Cells)) spec
            |> Result.bind (fun (cols2, rows2) ->
                let ws =
                    (List.map2 (fun (w: Work) row -> { w with Cells = row }) aliveWorks rows2)
                    @ deadWorks

                walk resolve env prior cols2 ws evalIdx evaluated caches rest)
        // Phase 120 — a filtering join (`Semi` / `Anti`): a `Filter` whose predicate reads a
        // relation instead of an expression. The verdict is cached in the same per-row cell list a
        // `Filter`'s is, so `evalIdx` advances; `evaluated` does not move, because the reference's
        // own counter charges a join nothing.
        //
        // Reuse is conditioned on the RELATION as well as on the delta. The delta describes the
        // source only, so a row it did not name can still have a different verdict — the joined
        // relation may have gained or lost the key it matched on. When the key index has moved,
        // every row's verdict is recomputed at this step and the prefix's reuse is untouched.
        | WJoin(joinIdx, src, on, keepMatched) :: rest ->
            DataFrame.evalSource resolve src
            |> Result.bind (fun rightTable ->
                DataFrame.joinKeyIndices cols rightTable.Schema on
                |> Result.bind (fun (li, ri) ->
                    let rightKeys =
                        rowsOf rightTable |> List.map (fun r -> ri |> List.map (fun j -> List.item j r))

                    let relationMoved = Map.tryFind joinIdx prior.JoinKeys <> Some rightKeys

                    let verdictOf (w: Work) =
                        let cached =
                            if w.Affected || relationMoved then
                                None
                            else
                                List.tryItem evalIdx w.Cached

                        match cached with
                        | Some c -> c
                        | None ->
                            let leftKeys = li |> List.map (fun i -> List.item i w.Cells)
                            let matched = rightKeys |> List.exists (DataFrame.joinKeysMatch leftKeys)
                            Bool(matched = keepMatched)

                    let ws =
                        works
                        |> List.map (fun w ->
                            if not w.Alive then
                                w
                            else
                                let v = verdictOf w

                                { w with
                                    Alive = (v = Bool true)
                                    Fresh = v :: w.Fresh })

                    walk
                        resolve
                        env
                        prior
                        cols
                        ws
                        (evalIdx + 1)
                        evaluated
                        { caches with
                            JoinKeys = Map.add joinIdx rightKeys caches.JoinKeys }
                        rest))
        | WFilter pred :: rest ->
            let rec go acc n =
                function
                | [] -> Ok(List.rev acc, n)
                | (w: Work) :: tail ->
                    if not w.Alive then
                        go (w :: acc) n tail
                    else
                        match cellAt env cols evalIdx pred w with
                        | Error e -> Error e
                        | Ok(c, didWork) ->
                            let w' =
                                { w with
                                    Alive = (c = Bool true)
                                    Fresh = c :: w.Fresh }

                            go (w' :: acc) (if didWork then n + 1 else n) tail

            go [] evaluated works
            |> Result.bind (fun (ws, n) -> walk resolve env prior cols ws (evalIdx + 1) n caches rest)
        | WProject pairs :: rest ->
            let resolveOne (src, out) =
                match colIndex cols src with
                | None -> Error(UnknownColumn(src, available cols))
                | Some i -> Ok(out, snd (List.item i cols), i)

            traverse resolveOne pairs
            |> Result.bind (fun resolved ->
                let cols2 = resolved |> List.map (fun (o, ty, _) -> o, ty)

                let ws =
                    works
                    |> List.map (fun w ->
                        if w.Alive then
                            { w with
                                Cells = resolved |> List.map (fun (_, _, i) -> List.item i w.Cells) }
                        else
                            w)

                walk resolve env prior cols2 ws evalIdx evaluated caches rest)
        | WDerive(name, expr) :: rest ->
            let rec go acc n =
                function
                | [] -> Ok(List.rev acc, n)
                | (w: Work) :: tail ->
                    if not w.Alive then
                        go (w :: acc) n tail
                    else
                        match cellAt env cols evalIdx expr w with
                        | Error e -> Error e
                        | Ok(c, didWork) ->
                            go ({ w with Fresh = c :: w.Fresh } :: acc) (if didWork then n + 1 else n) tail

            go [] evaluated works
            |> Result.bind (fun (ws, n) ->
                // A derived column's TYPE is a function of the whole column, not of one row, so it
                // is inferred over the cells of the rows alive AT THIS STEP in source order —
                // precisely the set the reference evaluator holds here. This is the one place a
                // row-local step is not row-local, and reading it off a cache would type the column
                // differently from the reference the moment a filter downstream dropped the only
                // typed row.
                let newCells =
                    ws |> List.filter (fun w -> w.Alive) |> List.map (fun w -> List.head w.Fresh)

                let ty = DataFrame.inferCellType newCells

                let cols2, ws2 =
                    match colIndex cols name with
                    | Some i ->
                        (cols |> List.mapi (fun j (n2, t) -> if j = i then n2, ty else n2, t)),
                        (ws
                         |> List.map (fun w ->
                             if w.Alive then
                                 let c = List.head w.Fresh

                                 { w with
                                     Cells = w.Cells |> List.mapi (fun j cell -> if j = i then c else cell) }
                             else
                                 w))
                    | None ->
                        (cols @ [ name, ty ]),
                        (ws
                         |> List.map (fun w ->
                             if w.Alive then
                                 { w with
                                     Cells = w.Cells @ [ List.head w.Fresh ] }
                             else
                                 w))

                walk resolve env prior cols2 ws2 (evalIdx + 1) n caches rest)

    // ---- the maintained-group step ----

    /// The outcome of a maintained `GroupBy`: the result schema and rows, the row-to-group map, the
    /// per-group ordered member tokens, the per-group aggregate cells, and how many groups were
    /// recomputed rather than reused.
    type private GroupOutcome =
        { Cols: Schema
          Rows: Cell list list
          RowGroup: Map<string, string>
          Members: Map<string, string list>
          Aggs: Map<string, Cell list>
          Recomputed: int }

    /// Recompute a final `GroupBy` over the walked rows, recomputing only the affected groups'
    /// aggregates and reusing the cached cells for the rest. Mirrors `evalGroupBy`'s order of
    /// operations exactly — keys resolved first, then aggregates, then groups in first-appearance
    /// order — because the FIRST error the reference reports is part of its answer.
    ///
    /// A group is reusable only when no named row is in it now or was in it before AND its ordered
    /// member list is byte-identical to the cached one. The second condition is not redundant:
    /// `First` / `Last` read position outright and a float `Sum` is order-sensitive in its last
    /// bits, so a pure reordering of unnamed rows can move a group's aggregate.
    ///
    /// An untouched group cannot error: its cached cells came from a successful evaluation over
    /// members that have not moved, so the first error among the recomputed groups (in group order)
    /// is the first error overall.
    let private groupStep
        (cols: Schema)
        (alive: (string * Cell list) list)
        (keys: string list)
        (aggs: Agg list)
        (priorRowGroup: Map<string, string>)
        (priorMembers: Map<string, string list>)
        (priorAggs: Map<string, Cell list>)
        (namedRows: Set<string> option)
        : Result<GroupOutcome, EvalError> =
        let keyIdx = keys |> List.map (fun k -> colIndex cols k, k)

        match keyIdx |> List.tryPick (fun (i, k) -> if Option.isNone i then Some k else None) with
        | Some missing -> Error(UnknownColumn(missing, available cols))
        | None ->
            let idxs = keyIdx |> List.map (fun (i, _) -> Option.get i)

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

                // Group, preserving first-appearance order, keyed by the pinned canonical token —
                // the same partition `evalGroupBy` builds, so the two never disagree about which
                // rows are one group.
                let order, groups, rowGroup =
                    (([], Map.empty, Map.empty), alive)
                    ||> List.fold
                        (fun (order, map: Map<string, Cell list * Cell list list * string list>, rg) (token, row) ->
                            let k = idxs |> List.map (fun i -> List.item i row)
                            let gt = DataFrame.rowTokenString k
                            let rg2 = Map.add token gt rg

                            match Map.tryFind gt map with
                            | Some(k0, rows, toks) ->
                                order, Map.add gt (k0, rows @ [ row ], toks @ [ token ]) map, rg2
                            | None -> order @ [ gt ], Map.add gt (k, [ row ], [ token ]) map, rg2)

                let touched =
                    match namedRows with
                    | None -> None // every group is suspect
                    | Some named ->
                        let nowIn =
                            alive
                            |> List.filter (fun (t, _) -> Set.contains t named)
                            |> List.choose (fun (t, _) -> Map.tryFind t rowGroup)
                            |> Set.ofList

                        let wasIn =
                            named
                            |> Set.toList
                            |> List.choose (fun t -> Map.tryFind t priorRowGroup)
                            |> Set.ofList

                        Some(Set.union nowIn wasIn)

                let recomputed = ref 0

                let cellsFor (gt: string) (grp: Cell list list) (toks: string list) =
                    let reusable =
                        match touched with
                        | None -> false
                        | Some t ->
                            not (Set.contains gt t)
                            && Map.tryFind gt priorMembers = Some toks
                            && Map.containsKey gt priorAggs

                    if reusable then
                        Ok(Map.find gt priorAggs)
                    else
                        recomputed.Value <- recomputed.Value + 1

                        resolvedAggs
                        |> traverse (fun (a, ty, ci) ->
                            DataFrame.aggregateCells a.Fn ty (grp |> List.map (List.item ci)))

                order
                |> traverse (fun gt ->
                    let k, grp, toks = Map.find gt groups

                    cellsFor gt grp toks
                    |> Result.map (fun aggVals -> gt, k @ aggVals, aggVals, toks))
                |> Result.map (fun built ->
                    { Cols = keyCols @ aggCols
                      Rows = built |> List.map (fun (_, row, _, _) -> row)
                      RowGroup = rowGroup
                      Members = (Map.empty, built) ||> List.fold (fun m (gt, _, _, toks) -> Map.add gt toks m)
                      Aggs = (Map.empty, built) ||> List.fold (fun m (gt, _, a, _) -> Map.add gt a m)
                      Recomputed = recomputed.Value }))

    // ---- identity tokens ----

    /// Every source row's identity token, in row order, refusing whole if the witness cannot key the
    /// source uniquely. The token format is `Delta.refToken`'s, so a delta's `ByKey` refs and these
    /// tokens are the same strings by construction rather than by a second convention.
    let private tokensOf (idw: RowIdentity<'Id>) (t: Table) : Result<string list, DeltaDefect> =
        let n = Table.rowCount t

        let rec go i acc (seen: Set<string>) =
            if i >= n then
                Ok(List.rev acc)
            else
                match idw.KeyOf t i with
                | None -> Error(MissingIdentity(idw.Scheme, i))
                | Some id ->
                    let k = idw.KeyString id
                    let token = Delta.refToken (ByKey k)

                    if Set.contains token seen then
                        Error(DuplicateIdentity(idw.Scheme, k))
                    else
                        go (i + 1) (token :: acc) (Set.add token seen)

        go 0 [] Set.empty

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
        (final: (string list * Agg list) option)
        (source: Table)
        (tokens: string list)
        (prior: IncrementalEval option)
        (named: Set<string> option)
        (recomputeOf: int -> int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        let priorCells =
            prior |> Option.map (fun s -> s.RowCells) |> Option.defaultValue Map.empty

        let works =
            List.map2
                (fun token cells ->
                    { Token = token
                      Affected =
                        match named with
                        | None -> true
                        | Some ns -> Set.contains token ns || not (Map.containsKey token priorCells)
                      Alive = true
                      Cells = cells
                      Cached = Map.tryFind token priorCells |> Option.defaultValue []
                      Fresh = [] })
                tokens
                (rowsOf source)

        let priorCaches =
            match prior with
            | Some s ->
                { SortOrders = s.SortOrders
                  JoinKeys = s.JoinKeys }
            | None -> noCaches

        walk resolve env priorCaches source.Schema works 0 0 noCaches prefix
        |> Result.bind (fun (cols, ws, evaluated, caches) ->
            let rowCells =
                (Map.empty, ws) ||> List.fold (fun m w -> Map.add w.Token (List.rev w.Fresh) m)

            let aliveRows =
                ws |> List.filter (fun w -> w.Alive) |> List.map (fun w -> w.Token, w.Cells)

            match final with
            | None ->
                Ok
                    { Plan = p
                      Pipeline = pipeline
                      Env = env
                      Scheme = scheme
                      Source = source
                      Output = tableOf cols (aliveRows |> List.map snd)
                      RowCells = rowCells
                      RowGroup = Map.empty
                      GroupMembers = Map.empty
                      GroupAggs = Map.empty
                      SortOrders = caches.SortOrders
                      JoinKeys = caches.JoinKeys
                      Footprint =
                        { SourceRows = List.length tokens
                          ResultRows = List.length aliveRows
                          Recompute = recomputeOf evaluated 0 } }
            | Some(keys, aggs) ->
                let priorRowGroup =
                    prior |> Option.map (fun s -> s.RowGroup) |> Option.defaultValue Map.empty

                let priorMembers =
                    prior |> Option.map (fun s -> s.GroupMembers) |> Option.defaultValue Map.empty

                let priorAggs =
                    prior |> Option.map (fun s -> s.GroupAggs) |> Option.defaultValue Map.empty

                groupStep cols aliveRows keys aggs priorRowGroup priorMembers priorAggs named
                |> Result.map (fun g ->
                    { Plan = p
                      Pipeline = pipeline
                      Env = env
                      Scheme = scheme
                      Source = source
                      Output = tableOf g.Cols g.Rows
                      RowCells = rowCells
                      RowGroup = g.RowGroup
                      GroupMembers = g.Members
                      GroupAggs = g.Aggs
                      SortOrders = caches.SortOrders
                      JoinKeys = caches.JoinKeys
                      Footprint =
                        { SourceRows = List.length tokens
                          ResultRows = List.length g.Rows
                          Recompute = recomputeOf evaluated g.Recomputed } }))

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
        (recompute: int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        DataFrame.evalPipelineWithInEnvCounted resolve env pipeline source
        |> Result.map (fun (output, evaluated) ->
            { Plan = p
              Pipeline = pipeline
              Env = env
              Scheme = scheme
              Source = source
              Output = output
              RowCells = Map.empty
              RowGroup = Map.empty
              GroupMembers = Map.empty
              GroupAggs = Map.empty
              SortOrders = Map.empty
              JoinKeys = Map.empty
              Footprint =
                { SourceRows = Table.rowCount source
                  ResultRows = Table.rowCount output
                  Recompute = recompute evaluated } })

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
        (prior: IncrementalEval option)
        (named: Set<string> option)
        (recomputeOf: int -> int -> Recompute)
        (onDeclined: FallBackReason -> int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        let p = plan pipeline

        match p.Strategy, split pipeline with
        | ReferenceOnly r, _ -> runReference resolve env idw.Scheme pipeline p source (onDeclined r)
        | _, None ->
            // Unreachable while `plan` and `split` agree; the reference path is the safe reading of
            // a disagreement, so it is taken rather than asserted away.
            runReference resolve env idw.Scheme pipeline p source (fun n -> FullRecompute(n, PipelineChanged))
        | _, Some(prefix, final) ->
            match tokensOf idw source with
            | Error defect ->
                runReference resolve env idw.Scheme pipeline p source (fun n ->
                    FullRecompute(n, RowIdentityUnusable defect))
            | Ok tokens ->
                runIncremental resolve env idw.Scheme pipeline p prefix final source tokens prior named recomputeOf

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
        run resolve env idw pipeline source None None (fun evaluated _ -> Primed evaluated) (fun _ n -> Primed n)

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
        let stale =
            if pipeline <> state.Pipeline then
                Some PipelineChanged
            elif env <> state.Env then
                Some EnvChanged
            elif idw.Scheme <> state.Scheme then
                Some(RowIdentityUnusable(SchemeMismatch(state.Scheme, idw.Scheme)))
            elif source.Schema <> state.Source.Schema then
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
                None
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
                && source = state.Source
                && not (readsExternalSource pipeline)
            then
                Ok
                    { state with
                        Footprint =
                            { SourceRows = Table.rowCount source
                              ResultRows = Table.rowCount state.Output
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
                    (Some state)
                    named
                    (fun evaluated groups ->
                        match state.Plan.Strategy with
                        | RowLocalThenGroups -> GroupsRecomputed(evaluated, groups)
                        | _ -> RowsRecomputed evaluated)
                    (fun declined n -> FullRecompute(n, declined))

    /// `prime` over embedded sources with no params — the everyday call.
    let primeOn
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        prime DataFrame.noResolve Map.empty idw pipeline source

    /// `refresh` over embedded sources with no params — the everyday call.
    let refreshOn
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (state: IncrementalEval)
        (delta: TableDelta)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        refresh DataFrame.noResolve Map.empty idw pipeline state delta source

    /// The result the state currently holds.
    let result (s: IncrementalEval) : Table = s.Output

    /// What producing that result cost.
    let footprint (s: IncrementalEval) : RecomputeFootprint = s.Footprint

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
