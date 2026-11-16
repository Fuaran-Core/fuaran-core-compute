namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 603 — the pipeline-tree law vectors: lift, lower, each skeleton op
//  applied to a pipeline tree, a refused ill-typed edit, the op script that
//  builds a tree, the footprint verdict and `derive`, each answer computed by
//  calling Fuaran.Compute.PipelineTree and rendered through the substrate's
//  one corpus writer (`CorpusFile`) into
//  `conformance/laws/pipeline-tree-laws.json` (DECISIONS.md D82).
//
//  Fable-clean: the node leg (Node/PooledLawNode.fsproj) compiles this file
//  and prints the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute

module PipelineTreeVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "pipeline-tree-laws.json"

    /// The input every edit vector is checked against.
    let schema: Schema =
        [ Field.create "id" StringType
          Field.create "region" StringType
          Field.create "x" FloatType
          Field.create "y" FloatType ]

    let context: PipelineContext = PipelineContext.ofSchema schema

    let private derive = Derive("z", Binary(Mul, Col "x", Lit(Float 2.0)))
    let private filter = Filter(Binary(Gt, Col "z", Lit(Float 1.0)))

    let private group =
        GroupBy([ "region" ], [ { Name = "total"; Fn = Sum; Of = "y" } ])

    let private sort = Sort [ Slot.Lit "region", Asc ]

    /// The pipeline every edit vector starts from: a derive, a filter reading it, a group and a sort.
    let pipeline: Transform list = [ derive; filter; group; sort ]

    /// An unrelated pipeline: no step of it sits where the same step sits in `pipeline`.
    let unrelated: Transform list =
        [ Filter(Binary(Lt, Col "x", Lit(Float 9.0)))
          Distinct
          Limit(Slot.Lit 10, Slot.Lit 0) ]

    let tree: PipelineNode = PipelineTree.lift pipeline

    let private idOf (i: int) : string = tree.Children[i].Id

    let private str = CorpusFile.str

    let private treeCell (t: PipelineNode) : (string * CorpusCell) list =
        [ "tree", str (PipelineTreeWire.encode t)
          "pipelineHash", str (PipelineTree.pipelineHash t)
          "contentAddress", str (PipelineTree.contentAddress t) ]

    let private refusalCell (r: PipelineEditRefusal) : CorpusCell =
        match r with
        | PipelineEditRefusal.Rejected rej ->
            CorpusCell.Row
                [ "verdict", str "refused"
                  "error", str "Rejected"
                  "code", str (Rejection.code rej) ]
        | PipelineEditRefusal.IllShaped(node, reason) ->
            CorpusCell.Row
                [ "verdict", str "refused"
                  "error", str "IllShaped"
                  "node", str node
                  "reason", str reason ]
        | PipelineEditRefusal.UnknownColumn(step, column, available) ->
            CorpusCell.Row
                [ "verdict", str "refused"
                  "error", str "UnknownColumn"
                  "step", str step
                  "column", str column
                  "available", CorpusCell.List(available |> List.map str) ]

    /// One authored case: an id, the operation, its input and its answer.
    type Case =
        { Id: string
          Op: string
          Input: (string * CorpusCell) list
          Run: unit -> CorpusCell }

    let private applyCase (id: string) (op: PipelineOp) : Case =
        { Id = id
          Op = "apply"
          Input =
            [ "tree", str (PipelineTreeWire.encode tree)
              "op", str (PipelineTreeCodec.encodeOp op) ]
          Run =
            fun () ->
                match PipelineTree.apply context op tree with
                | Ok t -> CorpusCell.Row([ "verdict", str "ok" ] @ treeCell t)
                | Error r -> refusalCell r }

    let private inserted =
        PipelineTree.step (PipelineTree.freshId tree) (Filter(Binary(Lt, Col "x", Lit(Float 9.0))))

    /// The tree after inserting `inserted` after the derive: the `after` of the first derive vector.
    let private edited: PipelineNode =
        match PipelineTree.apply context (PipelineTree.insertAfter tree (Some(idOf 0)) inserted) tree with
        | Ok t -> t
        | Error r -> failwithf "the insert vector's edit was refused: %A" r

    let private deriveCase (id: string) (before: PipelineNode) (after: PipelineNode) : Case =
        { Id = id
          Op = "derive"
          Input =
            [ "before", str (PipelineTreeWire.encode before)
              "after", str (PipelineTreeWire.encode after) ]
          Run =
            fun () ->
                match PipelineTree.derive before after with
                | Ok ops ->
                    let lands =
                        match Ops.applyAll PipelineTree.policy PipelineTree.scope ops before with
                        | Ok t -> PipelineTreeWire.encode t = PipelineTreeWire.encode after
                        | Error _ -> false

                    CorpusCell.Row
                        [ "verdict", str "ok"
                          "script", str (PipelineTreeCodec.encodeScript ops)
                          "lands", CorpusCell.Value(JBool lands) ]
                | Error r -> refusalCell (PipelineEditRefusal.Rejected r) }

    let private independentCase (id: string) (a: PipelineOp list) (b: PipelineOp list) : Case =
        { Id = id
          Op = "independent"
          Input =
            [ "tree", str (PipelineTreeWire.encode tree)
              "left", str (PipelineTreeCodec.encodeScript a)
              "right", str (PipelineTreeCodec.encodeScript b) ]
          Run =
            fun () ->
                CorpusCell.Row
                    [ "verdict", str "ok"
                      "independent",
                      CorpusCell.Value(
                          JBool(Ops.independent (PipelineTree.footprint tree a) (PipelineTree.footprint tree b))
                      ) ] }

    let private rewrite (i: int) (t: Transform) : PipelineOp =
        UpdateNode(PipelineTree.step (idOf i) t)

    let cases: Case list =
        [ { Id = "lift"
            Op = "lift"
            Input = [ "pipeline", str (DataFrameCodec.encodePipeline pipeline) ]
            Run = fun () -> CorpusCell.Row([ "verdict", str "ok" ] @ treeCell (PipelineTree.lift pipeline)) }
          { Id = "lift-empty"
            Op = "lift"
            Input = [ "pipeline", str (DataFrameCodec.encodePipeline []) ]
            Run = fun () -> CorpusCell.Row([ "verdict", str "ok" ] @ treeCell (PipelineTree.lift [])) }
          { Id = "lower"
            Op = "lower"
            Input = [ "tree", str (PipelineTreeWire.encode edited) ]
            Run =
              fun () ->
                  CorpusCell.Row
                      [ "verdict", str "ok"
                        "pipeline", str (DataFrameCodec.encodePipeline (PipelineTree.lower edited)) ] }
          { Id = "script"
            Op = "script"
            Input = [ "tree", str (PipelineTreeWire.encode tree) ]
            Run =
              fun () ->
                  CorpusCell.Row
                      [ "verdict", str "ok"
                        "script", str (PipelineTreeCodec.encodeScript (PipelineTree.script tree)) ] }
          applyCase "insert-mid-pipeline" (PipelineTree.insertAfter tree (Some(idOf 0)) inserted)
          applyCase "insert-first" (PipelineTree.insertAfter tree None inserted)
          applyCase "insert-after-an-unknown-step-refused" (PipelineTree.insertAfter tree (Some "nowhere") inserted)
          applyCase "insert-child-appends" (InsertChild(PipelineTree.rootId, PipelineTree.step "appended" Distinct))
          applyCase "insert-after-the-group-refused-ill-typed" (InsertChild(PipelineTree.rootId, inserted))
          applyCase
              "insert-a-pipeline-refused"
              (InsertChild(PipelineTree.rootId, { PipelineTree.empty with Id = "inner" }))
          applyCase
              "insert-a-repeated-id-refused"
              (InsertChild(PipelineTree.rootId, PipelineTree.step (idOf 1) Distinct))
          applyCase "remove-the-sort" (RemoveNode(idOf 3))
          applyCase "remove-the-root-refused" (RemoveNode PipelineTree.rootId)
          applyCase "remove-the-derive-refused-ill-typed" (RemoveNode(idOf 0))
          applyCase "move-to-the-end" (MoveNode(idOf 2, PipelineTree.rootId))
          applyCase "move-under-a-step-refused" (MoveNode(idOf 3, idOf 0))
          applyCase "reorder" (ReorderChildren(PipelineTree.rootId, [ idOf 0; idOf 1; idOf 3; idOf 2 ]))
          applyCase "reorder-not-a-permutation-refused" (ReorderChildren(PipelineTree.rootId, [ idOf 0; idOf 1 ]))
          applyCase
              "reorder-ill-typed-refused"
              (ReorderChildren(PipelineTree.rootId, [ idOf 1; idOf 0; idOf 2; idOf 3 ]))
          applyCase "rewrite-the-filter" (rewrite 1 (Filter(Binary(Gt, Col "x", Lit(Float 1.0)))))
          applyCase "rewrite-a-group-key-refused-ill-typed" (rewrite 2 (GroupBy([ "nation" ], [])))
          applyCase "rewrite-a-step-as-a-pipeline-refused" (UpdateNode { PipelineTree.empty with Id = idOf 1 })
          applyCase "rewrite-an-unknown-step-refused" (UpdateNode(PipelineTree.step "nowhere" Distinct))
          applyCase "batch-remove-then-rewrite" (Batch [ RemoveNode(idOf 1); rewrite 0 (Derive("w", Col "x")) ])
          applyCase "batch-refused-whole" (Batch [ RemoveNode(idOf 3); RemoveNode(idOf 0) ])
          deriveCase "derive-after-an-insert" tree edited
          deriveCase "derive-identity" tree tree
          deriveCase "derive-unrelated-replaces-every-step" tree (PipelineTree.lift unrelated)
          deriveCase
              "derive-reordered"
              tree
              (PipelineTree.lift pipeline
               |> fun t ->
                   { t with
                       Children = List.rev t.Children })
          independentCase "independent-rewrites-of-two-steps" [ rewrite 0 derive ] [ rewrite 1 filter ]
          independentCase "rewrites-of-one-step-interfere" [ rewrite 1 filter ] [ rewrite 1 Distinct ]
          independentCase
              "a-rewrite-and-the-removal-of-another-step-are-independent"
              [ rewrite 1 filter ]
              [ RemoveNode(idOf 3) ]
          independentCase
              "an-insert-and-a-removal-interfere"
              [ InsertChild(PipelineTree.rootId, inserted) ]
              [ RemoveNode(idOf 3) ] ]

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row
            [ "id", str c.Id
              "op", str c.Op
              "input", CorpusCell.Row c.Input
              "expected", c.Run() ]

    let private description =
        "The reference answers for Fuaran.Compute.PipelineTree (Phase 603, DECISIONS.md D82): a Transform "
        + "pipeline lifted to a Core tree, edited by the skeleton ops, and lowered back. `lift` answers the "
        + "tree's canonical wire, its `pipelineHash` (the lowered list's, step ids excluded) and its "
        + "`contentAddress` (SHA-256 of the tree's wire, ids included); `lower` answers the pipeline's wire; "
        + "`script` the op script that builds the tree from the empty pipeline. `apply` applies `op` to "
        + "`tree` under the containment grammar and the schema check against `schema`, answering `{ verdict: "
        + "ok, tree, pipelineHash, contentAddress }` or `{ verdict: refused, error }`: `Rejected` with the "
        + "engine's refusal `code`, `IllShaped` with the `node` and `reason`, or `UnknownColumn` with the "
        + "`step` id, the `column` it reads and the columns `available` there. `derive` answers the op "
        + "script from `before` to `after` through Core's content-aware Diff and whether applying it lands "
        + "on `after`; `independent` answers whether the two scripts' footprints at `tree` are independent. Every "
        + "cell is compared byte for byte."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", str "pipelineTree"
                  "description", str description
                  "schema", CorpusCell.Value(JArr(schema |> List.map ColumnCodec.fieldJson)) ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
