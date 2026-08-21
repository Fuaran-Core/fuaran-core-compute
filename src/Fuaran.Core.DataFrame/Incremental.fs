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
//     `PropagateRows`, `MaintainGroups`, or `FallBack` with a typed reason —
//     before any evaluation happens, so a consumer can ASK whether a pipeline
//     is incrementalisable and see why it is not. A step whose output for one
//     row depends on rows the delta does not name (a sort, a window, a
//     whole-relation set op, a join) is not approximated: the reference
//     evaluator re-runs, and the footprint records that it did.
//   * NEVER A WRONG ANSWER, ONLY A LARGER ONE. Every condition the incremental
//     path cannot honour — a schema that moved, an env that changed, a
//     pipeline that changed, a delta that says `FullRefresh`, an identity
//     witness that cannot key the source — degrades to a full evaluation with
//     the reason recorded. Degrading is always available and always correct,
//     which is what makes the seam safe to adopt one pipeline at a time.
//
//  Two order-sensitivities are handled explicitly rather than assumed away,
//  because both are silent when got wrong. A `Derive`d column's TYPE is
//  inferred from the whole column, so it is recomputed from the rows alive at
//  that step even when no cell moved. And a group's aggregate depends on its
//  members' ORDER (`First` / `Last` outright, a float `Sum` in its last bits),
//  so a cached aggregate is reused only when the group's ordered member list is
//  unchanged — never merely because no row in it was named.
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
    /// Not incrementalisable — the reference evaluator answers this pipeline.
    | FallBack of reason: FallBackReason

/// The strategy a whole pipeline's classification induces.
type IncrementalStrategy =
    /// Every step is row-local.
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
type Recompute =
    /// The first evaluation: there was no prior state, so every row was evaluated.
    | Primed of rowsEvaluated: int
    /// The delta asserted that nothing changed and the source was unchanged; the prior result was
    /// returned as it stood.
    | ReusedPrior
    /// Only the delta's rows were re-evaluated; every other row's value came from the cache.
    | RowsRecomputed of rowsEvaluated: int
    /// Only the affected groups' aggregates were recomputed, over the re-evaluated rows.
    | GroupsRecomputed of rowsEvaluated: int * groupsRecomputed: int
    /// The pipeline was evaluated in full, for the named reason.
    | FullRecompute of reason: FallBackReason

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
    let verbName (t: Transform) : string =
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

    /// Classify one step. `isLast` matters only for the maintainable verbs: a `GroupBy` at the end
    /// of a pipeline has maintainable state, the same `GroupBy` in the middle does not, because
    /// what follows it would need a delta over the GROUP table and no such delta was supplied.
    let classifyStep (isLast: bool) (t: Transform) : StepIncrementality =
        match t with
        | Filter _
        | Project _
        | Derive _ -> PropagateRows
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

    /// The rows whose expressions were evaluated. A full evaluation touches every source row, which
    /// is the honest reading of that outcome even though the evaluator does not itself count.
    let rowsEvaluated (f: RecomputeFootprint) : int =
        match f.Recompute with
        | Primed n
        | RowsRecomputed n -> n
        | GroupsRecomputed(n, _) -> n
        | ReusedPrior -> 0
        | FullRecompute _ -> f.SourceRows

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

    /// A stable human string for a footprint — counts only, so two hosts print the same line.
    let footprintString (f: RecomputeFootprint) : string =
        let what =
            match f.Recompute with
            | Primed n -> "primed, " + string n + " rows evaluated"
            | ReusedPrior -> "reused the prior result, 0 rows evaluated"
            | RowsRecomputed n -> string n + " rows re-evaluated"
            | GroupsRecomputed(n, g) -> string n + " rows re-evaluated, " + string g + " groups recomputed"
            | FullRecompute r -> "full evaluation (" + reasonString r + ")"

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

    /// The three row-local verbs, as the closed shape the walk consumes. Splitting the pipeline into
    /// this form (rather than re-matching `Transform` inside the walk) is what removes the
    /// "unreachable case" a catch-all would otherwise need: a step that is not one of these three
    /// never reaches the walk, by construction.
    type private RowLocalStep =
        | WFilter of ColExpr
        | WProject of (string * string) list
        | WDerive of string * ColExpr

    /// Split a pipeline into its row-local prefix and an optional final `GroupBy`. `None` when the
    /// pipeline is not of the incremental shape — exactly when `plan` says `ReferenceOnly`.
    let private split (pipeline: Transform list) : (RowLocalStep list * (string list * Agg list) option) option =
        let rec go acc =
            function
            | [] -> Some(List.rev acc, None)
            | [ GroupBy(keys, aggs) ] -> Some(List.rev acc, Some(keys, aggs))
            | Filter p :: rest -> go (WFilter p :: acc) rest
            | Project pairs :: rest -> go (WProject pairs :: acc) rest
            | Derive(n, e) :: rest -> go (WDerive(n, e) :: acc) rest
            | _ -> None

        go [] pipeline

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

    /// Walk the row-local prefix, threading the schema and the rows. Dead rows are carried (so their
    /// cached prefix survives for the next refresh) but never evaluated and never counted — exactly
    /// as in the reference evaluator, which has already dropped them from its frame.
    let rec private walk
        (env: Map<string, Cell>)
        (cols: Schema)
        (works: Work list)
        (evalIdx: int)
        (evaluated: int)
        (steps: RowLocalStep list)
        : Result<Schema * Work list * int, EvalError> =
        match steps with
        | [] -> Ok(cols, works, evaluated)
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
            |> Result.bind (fun (ws, n) -> walk env cols ws (evalIdx + 1) n rest)
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

                walk env cols2 ws evalIdx evaluated rest)
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

                walk env cols2 ws2 (evalIdx + 1) n rest)

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

    // ---- evaluation ----

    /// Run the incremental path over `source`, re-evaluating the rows in `named` (`None` = all).
    /// `prior` supplies the caches; an absent prior is the primed case. `tokens` is the source's
    /// identity tokens, already computed by the caller (which had to compute them to know the
    /// witness could key the source at all).
    let private runIncremental
        (env: Map<string, Cell>)
        (scheme: string)
        (pipeline: Transform list)
        (p: IncrementalPlan)
        (prefix: RowLocalStep list)
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

        walk env source.Schema works 0 0 prefix
        |> Result.bind (fun (cols, ws, evaluated) ->
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
                      Footprint =
                        { SourceRows = List.length tokens
                          ResultRows = List.length g.Rows
                          Recompute = recomputeOf evaluated g.Recomputed } }))

    /// Evaluate through the reference evaluator and wrap the answer in a cache-free state — the
    /// always-available degradation. A refresh over such a state re-primes, so a fall-back costs a
    /// full evaluation and never corrupts what follows it.
    let private runReference
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (scheme: string)
        (pipeline: Transform list)
        (p: IncrementalPlan)
        (source: Table)
        (recompute: Recompute)
        : Result<IncrementalEval, EvalError> =
        DataFrame.evalPipelineWithInEnv resolve env pipeline source
        |> Result.map (fun output ->
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
              Footprint =
                { SourceRows = Table.rowCount source
                  ResultRows = Table.rowCount output
                  Recompute = recompute } })

    /// The shared entry: run the incremental path when the shape and the witness allow it, and the
    /// reference path otherwise. `named` is `None` for "every row".
    let private run
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        (prior: IncrementalEval option)
        (named: Set<string> option)
        (recomputeOf: int -> int -> Recompute)
        : Result<IncrementalEval, EvalError> =
        let p = plan pipeline

        match p.Strategy, split pipeline with
        | ReferenceOnly r, _ -> runReference resolve env idw.Scheme pipeline p source (FullRecompute r)
        | _, None ->
            // Unreachable while `plan` and `split` agree; the reference path is the safe reading of
            // a disagreement, so it is taken rather than asserted away.
            runReference resolve env idw.Scheme pipeline p source (FullRecompute PipelineChanged)
        | _, Some(prefix, final) ->
            match tokensOf idw source with
            | Error defect ->
                runReference resolve env idw.Scheme pipeline p source (FullRecompute(RowIdentityUnusable defect))
            | Ok tokens -> runIncremental env idw.Scheme pipeline p prefix final source tokens prior named recomputeOf

    /// Evaluate `pipeline` over `source` from scratch, building the state a later `refresh`
    /// restricts. Equal to `DataFrame.evalPipelineWithInEnv resolve env pipeline source` — priming
    /// is a full evaluation that also records what it computed.
    ///
    /// A witness that cannot key the source uniquely does not fail the call: the pipeline is
    /// evaluated through the reference path and the state carries no caches, so a later refresh
    /// re-primes. Identity is what the SEAM needs, not what the ANSWER needs.
    let prime
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (source: Table)
        : Result<IncrementalEval, EvalError> =
        run resolve env idw pipeline source None None (fun evaluated _ -> Primed evaluated)

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
            run resolve env idw pipeline source None None (fun _ _ -> FullRecompute r)
        | None ->
            // A quiet delta over a source that is byte-identical to the one the state was evaluated
            // against is the one case where the prior result can be handed back untouched. The
            // source comparison is not ceremony: a delta built by `Delta.diff` is quiet for a pure
            // ROW REORDERING too, and the reference evaluator's output order would have moved.
            //
            // This reuse is sound for EVERY strategy, declined pipelines included — a verb is
            // declined because it cannot answer a change, not because it must be re-run when
            // nothing changed. It is reached before the strategy dispatch for exactly that reason.
            if Delta.isQuiet delta && source = state.Source then
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

                run resolve env idw pipeline source (Some state) named (fun evaluated groups ->
                    match state.Plan.Strategy with
                    | RowLocalThenGroups -> GroupsRecomputed(evaluated, groups)
                    | _ -> RowsRecomputed evaluated)

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
    let evalDelta
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

    /// `evalDelta` over embedded sources with no params.
    let evalDeltaOn
        (idw: RowIdentity<'Id>)
        (pipeline: Transform list)
        (before: Table)
        (delta: TableDelta)
        (after: Table)
        : Result<IncrementalEval, EvalError> =
        evalDelta DataFrame.noResolve Map.empty idw pipeline before delta after
