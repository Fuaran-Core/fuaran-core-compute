namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.PipelineTree (Phase 603, DECISIONS.md D162) — a `Transform`
//  pipeline as a `Fuaran.Core` tree, edited by the substrate's skeleton ops.
//
//  A pipeline is an ordered `Transform list`, and its canonical hash says THAT
//  two pipelines differ and never HOW. Lifted to a tree — the pipeline at the
//  root, one node per step — it takes the edit model every tree domain has:
//  path-addressed `SkeletonOp`s through `Ops.apply`, a hash-chained op-stream
//  whose replay reproduces the tree, `Ops.footprint` naming what an edit
//  touches, and `Diff` deriving the script between two versions. So a new
//  version of a pipeline can carry the edit that produced it.
//
//  Nothing evaluates a tree: `lower` hands the evaluator the `Transform list`
//  it already runs, and the pipeline hash stays that list's hash, with the
//  step ids excluded. FSharp.Core only, Fable-clean.
// ============================================================================

/// What a node of a pipeline tree holds (Phase 603).
[<RequireQualifiedAccess>]
type PipelineNodeContent =
    /// The root: the pipeline itself. Its children are its steps, in order.
    | Pipeline
    /// One step: one `Transform` case. A step holds no children.
    | Step of Transform

/// A node of a pipeline tree (Phase 603, DECISIONS.md D162): its permanent id, what it holds, and its
/// children. Only the root holds children, and every child of the root is a step; the containment
/// grammar the edits run under (`PipelineTree.policy`) keeps it so, and `PipelineTree.shape` names the
/// first node of a tree built by hand that breaks it.
type PipelineNode =
    {
        /// The node's identity, stable across edits. The root's is `PipelineTree.rootId`.
        Id: string
        /// The pipeline, or one step of it.
        Content: PipelineNodeContent
        /// The steps, in order, for the root; `[]` for a step.
        Children: PipelineNode list
    }

/// An edit to a pipeline tree: the substrate's skeleton op over pipeline nodes, addressed by id.
type PipelineOp = SkeletonOp<PipelineNode, string>

/// Why an edit to a pipeline tree was refused (Phase 603). Every refusal leaves the tree unchanged.
[<RequireQualifiedAccess>]
type PipelineEditRefusal =
    /// The substrate's engine refused the op: an unknown or repeated id, a step placed under a step
    /// (`IllegalChild` or `NotAContainer`), a reorder that is not a permutation, the root removed.
    | Rejected of Rejection<string>
    /// The tree offered is not a pipeline tree: `node` breaks the shape, for the reason given — a root
    /// that is not the pipeline, a step under a step, a pipeline under the root.
    | IllShaped of node: string * reason: string
    /// The edited pipeline has a step that reads a column the static schema walk shows absent at that
    /// step, and the tree before the edit did not: `step` is the step's id, `column` the name it reads,
    /// `available` the columns the walk names there.
    | UnknownColumn of step: string * column: string * available: string list

/// What an edit is checked against (Phase 603): the knowledge of the pipeline's input, and the schemas
/// of the named sources a `Join` or a set operation reads. A pipeline carries neither: the source is
/// the caller's, as it is to the evaluator.
[<NoEquality; NoComparison>]
type PipelineContext =
    {
        /// What is known about the input before the first step.
        Input: SchemaKnowledge
        /// The declared schemas of the `Ref` sources the steps name.
        Sources: string -> Schema option
    }

/// Building a `PipelineContext` (Phase 603).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PipelineContext =

    /// An input of a concrete schema, and no named source declared.
    let ofSchema (schema: Schema) : PipelineContext =
        { Input = SchemaWalk.ofSchema schema
          Sources = SchemaWalk.noSources }

    /// The input a source declares, with the named sources `sources` declares.
    let ofSource (sources: string -> Schema option) (source: DataSource) : PipelineContext =
        { Input = SchemaWalk.ofSource sources source
          Sources = sources }

/// The canonical wire codec of a pipeline tree and its op scripts (Phase 603). A node is
/// `{"$type":"pipeline"|"step","id":…,"children":[…]}`, a step carrying its `Transform` as `"step"` in
/// the dataframe codec's own encoding; an op is `{"$type":"insertChild","parent":…,"node":…}`,
/// `{"$type":"removeNode","target":…}`, `{"$type":"moveNode","target":…,"newParent":…}`,
/// `{"$type":"reorderChildren","parent":…,"order":[…]}`, `{"$type":"batch","ops":[…]}` or
/// `{"$type":"updateNode","node":…}`; a script is a JSON array of ops. Rendered through
/// `Canonical.write Rendering.CrossHost`, so the bytes are identical on every host.
module PipelineTreeCodec =

    // The string-error readers this codec is written against: the substrate's `Decoder` combinators,
    // their refusal in its one sentence per code.
    let private getProp (name: string) (el: JVal) : Result<JVal, string> =
        Decoder.field name Decoder.json el |> Result.mapError DecodeError.describe

    let private strField (name: string) (el: JVal) : Result<string, string> =
        getProp name el
        |> Result.bind (Decoder.str >> Result.mapError DecodeError.describe)

    let private mapList (f: JVal -> Result<'a, string>) (el: JVal) : Result<'a list, string> =
        Decoder.items el
        |> Result.mapError DecodeError.describe
        |> Result.bind (fun items ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest -> f x |> Result.bind (fun y -> go (y :: acc) rest)

            go [] items)

    let private canonical (j: JVal) : string = Canonical.write Rendering.CrossHost j

    /// A node, its subtree included, as a JSON value.
    let rec encodeNodeJson (n: PipelineNode) : JVal =
        let children = "children", JArr(n.Children |> List.map encodeNodeJson)

        match n.Content with
        | PipelineNodeContent.Pipeline -> JObj [ "$type", JStr "pipeline"; "id", JStr n.Id; children ]
        | PipelineNodeContent.Step t ->
            JObj
                [ "$type", JStr "step"
                  "id", JStr n.Id
                  "step", DataFrameCodec.encodeTransform t
                  children ]

    /// A node as its canonical wire string.
    let encodeNode (n: PipelineNode) : string = canonical (encodeNodeJson n)

    /// A node from its JSON value. Reads any node the wire can carry, whatever its shape; a tree is
    /// read by `decode`, which also holds the shape.
    let rec decodeNodeJson (el: JVal) : Result<PipelineNode, string> =
        let children () =
            getProp "children" el |> Result.bind (mapList decodeNodeJson)

        strField "id" el
        |> Result.bind (fun id ->
            strField "$type" el
            |> Result.bind (fun tag ->
                match tag with
                | "pipeline" ->
                    children ()
                    |> Result.map (fun cs ->
                        { Id = id
                          Content = PipelineNodeContent.Pipeline
                          Children = cs })
                | "step" ->
                    getProp "step" el
                    |> Result.bind (fun j ->
                        DataFrameCodec.decodeTransform j
                        |> Result.mapError (fun e -> "step " + id + ": " + ColumnCodec.errorString e))
                    |> Result.bind (fun t ->
                        children ()
                        |> Result.map (fun cs ->
                            { Id = id
                              Content = PipelineNodeContent.Step t
                              Children = cs }))
                | other -> Error("expected $type pipeline or step, got " + other)))

    /// A node from its wire string.
    let decodeNode (s: string) : Result<PipelineNode, string> =
        Json.parse s |> Result.bind decodeNodeJson

    /// An op as a JSON value.
    let rec encodeOpJson (op: PipelineOp) : JVal =
        match op with
        | InsertChild(parent, node) ->
            JObj
                [ "$type", JStr "insertChild"
                  "parent", JStr parent
                  "node", encodeNodeJson node ]
        | RemoveNode target -> JObj [ "$type", JStr "removeNode"; "target", JStr target ]
        | MoveNode(target, newParent) ->
            JObj [ "$type", JStr "moveNode"; "target", JStr target; "newParent", JStr newParent ]
        | ReorderChildren(parent, order) ->
            JObj
                [ "$type", JStr "reorderChildren"
                  "parent", JStr parent
                  "order", JArr(order |> List.map JStr) ]
        | Batch ops -> JObj [ "$type", JStr "batch"; "ops", JArr(ops |> List.map encodeOpJson) ]
        | UpdateNode node -> JObj [ "$type", JStr "updateNode"; "node", encodeNodeJson node ]

    /// An op as its canonical wire string — the `Encode` of the op-stream witness, so it is
    /// deterministic: one op, one string.
    let encodeOp (op: PipelineOp) : string = canonical (encodeOpJson op)

    /// An op from its JSON value.
    let rec decodeOpJson (el: JVal) : Result<PipelineOp, string> =
        let node (name: string) =
            getProp name el |> Result.bind decodeNodeJson

        strField "$type" el
        |> Result.bind (fun tag ->
            match tag with
            | "insertChild" ->
                strField "parent" el
                |> Result.bind (fun p -> node "node" |> Result.map (fun n -> InsertChild(p, n)))
            | "removeNode" -> strField "target" el |> Result.map RemoveNode
            | "moveNode" ->
                strField "target" el
                |> Result.bind (fun t -> strField "newParent" el |> Result.map (fun p -> MoveNode(t, p)))
            | "reorderChildren" ->
                strField "parent" el
                |> Result.bind (fun p ->
                    getProp "order" el
                    |> Result.bind (mapList (Decoder.str >> Result.mapError DecodeError.describe))
                    |> Result.map (fun order -> ReorderChildren(p, order)))
            | "batch" -> getProp "ops" el |> Result.bind (mapList decodeOpJson) |> Result.map Batch
            | "updateNode" -> node "node" |> Result.map UpdateNode
            | other ->
                Error(
                    "expected $type insertChild, removeNode, moveNode, reorderChildren, batch or updateNode, got "
                    + other
                ))

    /// An op from its wire string — the `Decode` of the op-stream witness.
    let decodeOp (s: string) : Result<PipelineOp, string> =
        Json.parse s |> Result.bind decodeOpJson

    /// An op script (an ordered op list) as its canonical wire string.
    let encodeScript (ops: PipelineOp list) : string =
        canonical (JArr(ops |> List.map encodeOpJson))

    /// An op script from its wire string.
    let decodeScript (s: string) : Result<PipelineOp list, string> =
        Json.parse s |> Result.bind (mapList decodeOpJson)

/// A pipeline as a `Fuaran.Core` tree (Phase 603, DECISIONS.md D162): the witnesses Core's engine sees
/// it through, the lossless round trip to and from `Transform list`, validated edits, the op-stream
/// witness, the footprint, and the derivation between two versions.
module PipelineTree =

    /// The root's id, the same in every pipeline tree: Core's `Diff` derives a script only between two
    /// trees whose roots carry one id.
    [<Literal>]
    let rootId = "pipeline"

    /// The id witness: a node's id is its own string key.
    let ids: IdWitness<string> =
        { ToString = id
          OfString = id
          Equals = fun (a: string) b -> a = b }

    let private isPipeline (n: PipelineNode) : bool =
        match n.Content with
        | PipelineNodeContent.Pipeline -> true
        | PipelineNodeContent.Step _ -> false

    /// The node witness: the kind is `pipeline` or `step`, the children are the node's own.
    let nodes: NodeWitness<PipelineNode, string> =
        { Id = _.Id
          KindTag = fun n -> if isPipeline n then "pipeline" else "step"
          Children = _.Children
          ReplaceChildren = fun n cs -> { n with Children = cs } }

    /// How the engine sees a pipeline tree: only the pipeline can hold children, and no node holds a
    /// keyed position (a step's operands are its content, not nodes).
    let scope: OpScope<PipelineNode, string> = OpScope.contained isPipeline nodes ids

    /// The containment grammar: the pipeline holds steps, and a step holds nothing.
    let grammar (kind: string) : string list option =
        match kind with
        | "pipeline" -> Some [ "step" ]
        | "step" -> Some []
        | _ -> None

    /// The policy every edit runs under: the engine's checks, then the grammar.
    let policy: Policy<PipelineNode, string> = Policy.grammar grammar

    /// The pipeline with no steps.
    let empty: PipelineNode =
        { Id = rootId
          Content = PipelineNodeContent.Pipeline
          Children = [] }

    /// A step node.
    let step (id: string) (t: Transform) : PipelineNode =
        { Id = id
          Content = PipelineNodeContent.Step t
          Children = [] }

    /// The id `lift` mints for the step at `index`: `s<index>-` and the first twelve hex digits of the
    /// SHA-256 of the step's canonical encoding. Deterministic from the position and the step, so two
    /// lifts of one list mint one set of ids, and two lifts share an id only where they hold the same
    /// step at the same position — the one alignment that is not a guess.
    let liftedId (index: int) (t: Transform) : string =
        let digest =
            Hash.sha256Hex (Canonical.write Rendering.CrossHost (DataFrameCodec.encodeTransform t))

        "s" + string index + "-" + digest.Substring(0, 12)

    /// Lift a pipeline to its tree: the root, and one step node per step in order, each id minted by
    /// `liftedId`. Total.
    let lift (pipeline: Transform list) : PipelineNode =
        { empty with
            Children = pipeline |> List.mapi (fun i t -> step (liftedId i t) t) }

    /// Lower a tree to its pipeline: the steps the root holds, in order. `lower (lift p) = p`, and the
    /// two encode to the same bytes. A node that is not a step is not a step of the pipeline; no tree
    /// the engine, `lift` or `PipelineTreeCodec.decode` answers holds one (`shape`).
    let lower (tree: PipelineNode) : Transform list =
        tree.Children
        |> List.choose (fun n ->
            match n.Content with
            | PipelineNodeContent.Step t -> Some t
            | PipelineNodeContent.Pipeline -> None)

    /// The pipeline hash: `IncrementalCodec.pipelineHash` of the lowered list — the hash a pipeline has
    /// always had, so a tree and the list it lowers to have one, and the same pipeline reached by two
    /// edit histories (different step ids) has one identity.
    let pipelineHash (tree: PipelineNode) : string =
        IncrementalCodec.pipelineHash (lower tree)

    /// The tree's content address: SHA-256 (lowercase hex) of its canonical wire string, step ids
    /// included. The identity of a tree on an op-stream, where an id is what an op addresses.
    let contentAddress (tree: PipelineNode) : string =
        Hash.sha256Hex (PipelineTreeCodec.encodeNode tree)

    /// The first node that breaks a pipeline tree's shape, with the reason, or `Ok`: the root is the
    /// pipeline, each of its children is a step, and a step holds no children.
    let shape (tree: PipelineNode) : Result<unit, string * string> =
        if not (isPipeline tree) then
            Error(tree.Id, "the root is not the pipeline")
        else
            let offender =
                tree.Children
                |> List.tryPick (fun n ->
                    if isPipeline n then
                        Some(n.Id, "a pipeline is a child of the pipeline")
                    elif not (List.isEmpty n.Children) then
                        Some(n.Id, "a step holds children")
                    else
                        None)

            match offender with
            | Some fault -> Error fault
            | None -> Ok()

    // ---- the static column census ----------------------------------------------------------------

    /// Every column an expression reads. A call reads what its arguments read: a function's body reads
    /// no column (`FnDef.expand` refuses one that does).
    let rec private exprCols (e: ColExpr) : string list =
        match e with
        | Col n -> [ n ]
        | Lit _
        | Param _
        | Now _
        | Arg _ -> []
        | Binary(_, a, b)
        | Quotient(a, b, _) -> exprCols a @ exprCols b
        | Not x
        | Cast(_, x)
        | IsNull x
        | InParam(x, _)
        | Rounded(x, _)
        | Convert(x, _)
        | Interpolate(x, _, _, _) -> exprCols x
        | Coalesce xs
        | ApplyFn(_, xs)
        | Call(_, xs) -> xs |> List.collect exprCols
        | Case(cases, els) -> (cases |> List.collect (fun (w, t) -> exprCols w @ exprCols t)) @ exprCols els
        | InList(x, items) -> exprCols x @ (items |> List.collect exprCols)

    /// Whether a window function reads its `Of` column: the ranking family reads only the keys.
    let private windowReads (fn: WindowFn) : bool =
        match fn with
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _ -> false
        | Lag
        | Lead
        | CumulSum
        | CumulMax
        | CumulMin
        | RollingMean
        | RollingSum -> true

    /// The columns a step names from its INPUT, each of which the evaluator refuses as `UnknownColumn`
    /// when the input lacks it. A sort key bound to a param is the param's, not a name. Every case is
    /// listed, so a new `Transform` case does not compile until it is classified here.
    let private reads (t: Transform) : string list =
        match t with
        | Filter p -> exprCols p
        | Project pairs -> pairs |> List.map fst
        | Derive(_, e) -> exprCols e
        | GroupBy(keys, aggs) -> keys @ (aggs |> List.map _.Of)
        | Join(_, pairs, _) -> pairs |> List.map fst
        | Window w ->
            (if windowReads w.Fn then [ w.Of ] else [])
            @ w.PartitionBy
            @ (w.OrderBy |> List.map fst)
        | Pivot spec -> spec.Index @ [ spec.On; spec.Values ]
        | Unpivot(idVars, valueVars) -> idVars @ valueVars
        | Sort by -> by |> List.choose (fun (c, _) -> Slot.tryLit c)
        | Fit spec ->
            spec.GroupBy
            @ [ spec.Response ]
            @ Option.toList spec.Weights
            @ (FitSpec.exprs spec |> List.collect exprCols)
        | Predict spec -> spec.Fit.GroupBy @ (FitSpec.exprs spec.Fit |> List.collect exprCols)
        | KMeans spec -> spec.GroupBy @ (spec.Features |> List.collect (snd >> exprCols))
        | Bootstrap spec -> spec.GroupBy @ Option.toList spec.Cluster @ (spec.Aggregates |> List.map _.Of)
        | Distinct
        | Limit _
        | Define _
        | Union _
        | Intersect _
        | Except _ -> []

    /// Every column a step reads that the static schema walk shows absent there: `(step id, column,
    /// the columns the walk names)`, in step order and, within a step, in the order the step names them;
    /// a join's right keys are read against the right source's knowledge. Only CLOSED knowledge refutes
    /// a name (`SchemaKnowledge`): past a step the walk cannot close, nothing is reported. Evaluates
    /// nothing.
    let unknownReads (context: PipelineContext) (tree: PipelineNode) : (string * string * string list) list =
        let absent (knowledge: SchemaKnowledge) (names: string list) : (string * string list) list =
            match knowledge with
            | SchemaKnowledge.Closed _ ->
                let available = SchemaWalk.names knowledge

                names
                |> List.distinct
                |> List.filter (fun n -> not (List.contains n available))
                |> List.map (fun n -> n, available)
            | SchemaKnowledge.AtLeast _ -> []

        let folder (knowledge: SchemaKnowledge, found) (n: PipelineNode) =
            match n.Content with
            | PipelineNodeContent.Pipeline -> knowledge, found
            | PipelineNodeContent.Step t ->
                let right =
                    match t with
                    | Join(source, pairs, _) ->
                        absent (SchemaWalk.ofSource context.Sources source) (pairs |> List.map snd)
                    | _ -> []

                let here =
                    (absent knowledge (reads t) @ right)
                    |> List.map (fun (column, available) -> n.Id, column, available)

                SchemaWalk.ofTransform context.Sources knowledge t, found @ here

        tree.Children |> List.fold folder (context.Input, []) |> snd

    // ---- edits -----------------------------------------------------------------------------------

    /// Apply one op (Phase 603): through `Ops.apply` under `policy` and `scope`, then the schema check.
    /// Refused, with the tree unchanged: a tree that is not a pipeline tree (`IllShaped`), the engine's
    /// refusal (`Rejected`), then the first column a step of the edited pipeline reads that the static
    /// walk shows absent there and that the tree before the edit did not read absent at that step
    /// (`UnknownColumn`). An edit is held to "no new unknown read", not to "none at all", so a pipeline
    /// that was already broken can be repaired one edit at a time. A `Batch` is all-or-nothing and is
    /// checked once, on the tree it lands on.
    let apply
        (context: PipelineContext)
        (op: PipelineOp)
        (tree: PipelineNode)
        : Result<PipelineNode, PipelineEditRefusal> =
        match shape tree with
        | Error(node, reason) -> Error(PipelineEditRefusal.IllShaped(node, reason))
        | Ok() ->
            match Ops.apply policy scope op tree with
            | Error r -> Error(PipelineEditRefusal.Rejected r)
            | Ok edited ->
                let before =
                    unknownReads context tree |> List.map (fun (s, c, _) -> s, c) |> Set.ofList

                match
                    unknownReads context edited
                    |> List.tryFind (fun (s, c, _) -> not (Set.contains (s, c) before))
                with
                | Some(s, c, available) -> Error(PipelineEditRefusal.UnknownColumn(s, c, available))
                | None -> Ok edited

    /// Apply an op script all-or-nothing: `apply` of the script as one `Batch`.
    let applyScript
        (context: PipelineContext)
        (ops: PipelineOp list)
        (tree: PipelineNode)
        : Result<PipelineNode, PipelineEditRefusal> =
        apply context (Batch ops) tree

    /// A step id the tree does not carry: `step-1`, `step-2`, … the first free (`FreshIds.sequential`),
    /// so the same tree mints the same id on every host and every replay.
    let freshId (tree: PipelineNode) : string =
        FreshIds.sequential ids "step" "" (Tree.ids nodes tree |> Set.ofList)

    /// Insert a step mid-pipeline (Phase 603): `InsertChild` appends, so the op is the `Batch` of the
    /// insert and the `ReorderChildren` that places the step after `after` (`None`: first). The order
    /// is stated against `tree`, so an `after` the tree does not hold is the reorder's refusal and the
    /// batch refuses whole.
    let insertAfter (tree: PipelineNode) (after: string option) (node: PipelineNode) : PipelineOp =
        let current = tree.Children |> List.map _.Id

        let order =
            match after with
            | None -> node.Id :: current
            | Some anchor -> current |> List.collect (fun i -> if i = anchor then [ i; node.Id ] else [ i ])

        Batch [ InsertChild(tree.Id, node); ReorderChildren(tree.Id, order) ]

    /// The op script that builds `tree` from `empty` (`Ops.lower`): one insert per step, in order — the
    /// history a pipeline that was never edited starts its op-stream with.
    let script (tree: PipelineNode) : PipelineOp list = Ops.lower nodes tree

    /// The op-stream witness under `context`: `apply` is the reducer, the codec the op encoding. With
    /// it `OpStream.append` chains an edit, and `OpStream.replay` from `empty` reproduces the tree, and
    /// therefore its pipeline hash.
    let streamWitness (context: PipelineContext) : StreamWitness<PipelineOp, PipelineNode, PipelineEditRefusal> =
        { Apply = apply context
          Encode = PipelineTreeCodec.encodeOp
          Decode = PipelineTreeCodec.decodeOp }

    /// The footprint of an op script (`Ops.footprint` under `policy` and `scope`): the steps it rewrites
    /// and the parents whose children it changes. Two rewrites of different steps are independent; two
    /// edits that change the pipeline's step list (an insert, a removal, a reorder) both write the root
    /// and are not.
    let footprint (ops: PipelineOp list) : Footprint = Ops.footprint policy scope ops

    /// The op script taking `before` to `after` (Phase 603), through Core's content-aware `Diff` over the
    /// canonical node encoding: a step present in both by id is kept, reordered or rewritten; a step in
    /// only one is inserted or removed. Ids are what align the two, never a guess: two lifts share an id
    /// only where they hold one step at one position, so two unrelated lists derive the honest worst
    /// case — every step removed and every step inserted. Where the edit that made `after` was applied
    /// and recorded, that script is the better record; this is the one for versions that carry none.
    let derive (before: PipelineNode) (after: PipelineNode) : Result<PipelineOp list, Rejection<string>> =
        Diff.toOpsWith PipelineTreeCodec.encodeNode policy scope before after

/// The tree's own wire form (Phase 603): `PipelineTreeCodec`'s node encoding of the root, read back
/// only when it is a pipeline tree.
[<RequireQualifiedAccess>]
module PipelineTreeWire =

    /// A tree as its canonical wire string.
    let encode (tree: PipelineNode) : string = PipelineTreeCodec.encodeNode tree

    /// A tree from its wire string: a node, then its shape (`PipelineTree.shape`).
    let decode (s: string) : Result<PipelineNode, string> =
        PipelineTreeCodec.decodeNode s
        |> Result.bind (fun tree ->
            match PipelineTree.shape tree with
            | Ok() -> Ok tree
            | Error(node, reason) -> Error("not a pipeline tree at " + node + ": " + reason))
