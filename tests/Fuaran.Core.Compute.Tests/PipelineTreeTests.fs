module Fuaran.Compute.Tests.PipelineTreeTests

// ============================================================================
//  Phase 603 — a pipeline is a Core tree and is edited by ops
//  (Fuaran.Compute.PipelineTree, DECISIONS.md D162).
//
//  The round trip over every `Transform` case, enumerated by reflection so a
//  case added tomorrow is in tomorrow's sample; the pipeline hash unchanged
//  from the list's; the substrate's own tree, containment, diff, footprint
//  and stream laws over the pipeline witness; edits validated by the static
//  column census, held sound against the evaluator for every class of read;
//  the op-stream replaying to the tree and its hash; derive; the codec; and
//  the committed law vectors.
// ============================================================================

open System.IO
open Microsoft.FSharp.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", PipelineTreeVectors.fileName))

/// One pipeline per document the wire surface derives for a `Transform list`: a document routed to
/// every case of every union a pipeline reaches, so every `Transform` case and every case below one.
let private everyCasePipelines () : Transform list list =
    WireSurfaceTests.documentsOf (
        WireSurfaceTests.WireRoot.Derived(
            "Fuaran.Compute.DataFrame",
            "pipeline",
            typeof<Transform list>,
            fun o -> DataFrameCodec.encodePipeline (unbox<Transform list> o)
        )
    )
    |> fst
    // An exemplar the codec refuses (a decimal literal under a non-decimal reading, say) is the wire
    // surface's concern, not this one; the coverage check below still asks for every Transform case.
    |> List.choose (fun (_, wire) ->
        match DataFrameCodec.decodePipeline wire with
        | Ok p -> Some p
        | Error _ -> None)

let private caseName (t: Transform) : string =
    (fst (FSharpValue.GetUnionFields(t, typeof<Transform>))).Name

let private schema = PipelineTreeVectors.schema
let private context = PipelineTreeVectors.context

let private table (rows: int) : Table =
    { Schema = schema
      Columns =
        [ KitColumn.create "id" StringType [ for i in 1..rows -> Str("r" + string i) ]
          KitColumn.create "region" StringType [ for i in 1..rows -> Str(if i % 2 = 0 then "north" else "south") ]
          KitColumn.create "x" FloatType [ for i in 1..rows -> Float(float i) ]
          KitColumn.create "y" FloatType [ for i in 1..rows -> Float(float (i * i)) ] ] }

/// One step per class of read the census names, each naming the absent column `nope`: the
/// evaluator must refuse every one as `UnknownColumn "nope"`, or the census refuses an edit the
/// evaluator would run.
let private absentReads: (string * Transform) list =
    let nope = Col "nope"
    let agg = { Name = "a"; Fn = Sum; Of = "nope" }

    let window fn ofCol partition =
        Window
            { PartitionBy = partition
              OrderBy = [ "x", Asc ]
              Fn = fn
              Of = ofCol
              As = "w" }

    [ "filter", Filter(Binary(Gt, nope, Lit(Float 0.0)))
      "project", Project [ "nope", "p" ]
      "derive", Derive("d", Binary(Add, nope, Lit(Float 1.0)))
      "derive through a call argument", Derive("d", Call("f", [ nope ]))
      "group key", GroupBy([ "nope" ], [])
      "group aggregate", GroupBy([ "region" ], [ agg ])
      "join left key", Join(Embedded(table 2), [ "nope", "id" ], Inner)
      "join right key", Join(Embedded(table 2), [ "id", "nope" ], Inner)
      "window column", window CumulSum "nope" []
      "window partition", window RowNumber "x" [ "nope" ]
      "pivot",
      Pivot
          { Index = [ "region" ]
            On = "nope"
            Values = "y"
            Agg = Sum }
      "unpivot", Unpivot([ "id" ], [ "nope" ])
      "sort", Sort [ Slot.Lit "nope", Asc ]
      "fit response", Fit(FitSpec.ols "nope" [ "x", Col "x" ])
      "fit predictor", Fit(FitSpec.ols "y" [ "n", nope ])
      "fit weights",
      Fit
          { FitSpec.ols "y" [ "x", Col "x" ] with
              Weights = Some "nope" }
      "predict predictor", Predict(PredictSpec.ofTraining (FitSpec.ols "y" [ "n", nope ]) (Embedded(table 4)))
      "kmeans feature", KMeans(KMeansSpec.create [ "n", nope ] 2 7L)
      "bootstrap aggregate", Bootstrap(BootstrapSpec.create [ agg ] 3 7L) ]

/// The census's soundness, per class: the step reads `nope`, the census says so, and the evaluator
/// over rows of the schema refuses it as `UnknownColumn "nope"`.
let private censusAgreesWithEvaluator (label: string) (step: Transform) =
    let pipeline =
        match step with
        | Derive(_, Call _) ->
            [ Define
                  { Name = "f"
                    Params = [ { Name = "a"; Default = None } ]
                    Body = Arg "a" }
              step ]
        | _ -> [ step ]

    let reported = PipelineTree.unknownReads context (PipelineTree.lift pipeline)

    Expect.isTrue
        (reported |> List.exists (fun (_, c, _) -> c = "nope"))
        (sprintf "%s: the census names the absent column" label)

    match DataFrame.evalPipeline pipeline (table 4) with
    | Error(UnknownColumn("nope", _)) -> ()
    | other ->
        failtestf
            "%s: the evaluator answers %A, not UnknownColumn nope — the census would refuse a step that runs"
            label
            other

/// A generator of pipeline trees for the substrate's tree laws: lifts of short pipelines over steps
/// that read the vectors' schema, and fresh steps with ids the tree does not hold.
let private pool: Transform list =
    [ Derive("z", Binary(Mul, Col "x", Lit(Float 2.0)))
      Filter(Binary(Gt, Col "x", Lit(Float 1.0)))
      Sort [ Slot.Lit "region", Asc ]
      Distinct
      Limit(Slot.Lit 3, Slot.Lit 0)
      Derive("w", Col "y") ]

let private gen: OpGen<PipelineNode, string> =
    let draw (r: ConfRng.T) =
        let i, r = ConfRng.intBelow pool.Length r
        pool[i], r

    { Tree =
        fun r ->
            let n, r = ConfRng.intBelow 5 r

            let steps, r =
                List.fold
                    (fun (acc, r) _ ->
                        let t, r = draw r
                        acc @ [ t ], r)
                    ([], r)
                    [ 1..n ]

            let illegal, r = ConfRng.intBelow 6 r
            let tree = PipelineTree.lift steps

            // One drawn tree in six holds a pipeline among its steps: the illegal tree the containment
            // laws hold the diff and the engine to.
            (if illegal = 0 then
                 { tree with
                     Children = tree.Children @ [ { PipelineTree.empty with Id = "inner" } ] }
             else
                 tree),
            r
      FreshNode =
        fun taken r ->
            let t, r = draw r
            let kind, r = ConfRng.intBelow 4 r
            let id = FreshIds.sequential PipelineTree.ids "fresh" "" taken

            // One fresh node in four is a pipeline, which the grammar refuses anywhere in a tree, so the
            // containment laws reach their refusals.
            (if kind = 0 then
                 { PipelineTree.empty with Id = id }
             else
                 PipelineTree.step id t),
            r
      CanHold = Some(fun n -> n.Content = PipelineNodeContent.Pipeline) }

let private held (results: LawResult list) =
    Expect.isNonEmpty results "the family ran laws"

    for r in results do
        Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

[<Tests>]
let tests =
    testList
        "Phase 603 - pipeline tree"
        [ testCase "every Transform case lifts and lowers back to itself byte for byte"
          <| fun () ->
              // The set operations, the join and a prediction from training rows carry a source: the
              // reflected exemplar's embedded table is not one the codec reads back, so these five are
              // written here, over each source form.
              let sources =
                  [ for source in [ Embedded(table 2); Ref "other" ] do
                        [ Join(source, [ "id", "id" ], Left)
                          Union source
                          Intersect source
                          Except source
                          Predict(PredictSpec.ofTraining (FitSpec.ols "y" [ "x", Col "x" ]) source) ] ]

              let pipelines = everyCasePipelines () @ sources
              let covered = pipelines |> List.concat |> List.map caseName |> Set.ofList

              let all =
                  FSharpType.GetUnionCases(typeof<Transform>) |> Array.map _.Name |> Set.ofArray

              Expect.equal (Set.difference all covered) Set.empty "the sample reaches every Transform case"

              for p in pipelines do
                  let tree = PipelineTree.lift p

                  Expect.equal
                      (DataFrameCodec.encodePipeline (PipelineTree.lower tree))
                      (DataFrameCodec.encodePipeline p)
                      "lower (lift p) = p, byte for byte"

                  Expect.equal
                      (PipelineTree.pipelineHash tree)
                      (IncrementalCodec.pipelineHash p)
                      "the pipeline hash is the list's"

                  Expect.equal
                      (PipelineTreeWire.decode (PipelineTreeWire.encode tree)
                       |> ok
                       |> PipelineTreeWire.encode)
                      (PipelineTreeWire.encode tree)
                      "the tree's wire round-trips"

                  for op in PipelineTree.script tree do
                      Expect.equal
                          (PipelineTreeCodec.decodeOp (PipelineTreeCodec.encodeOp op)
                           |> ok
                           |> PipelineTreeCodec.encodeOp)
                          (PipelineTreeCodec.encodeOp op)
                          "an op's wire round-trips"

          testCase "a lift mints ids from the position and the step, and two lifts of one list agree"
          <| fun () ->
              let a = PipelineTree.lift PipelineTreeVectors.pipeline
              let b = PipelineTree.lift PipelineTreeVectors.pipeline
              Expect.equal (Tree.ids PipelineTree.nodes a) (Tree.ids PipelineTree.nodes b) "deterministic"
              Expect.isTrue (Tree.isWellFormed PipelineTree.nodes PipelineTree.ids a) "ids unique"

              let twice = PipelineTree.lift [ Distinct; Distinct ]
              Expect.isTrue (Tree.isWellFormed PipelineTree.nodes PipelineTree.ids twice) "one step twice, two ids"

          testCase "the pipeline hash excludes the ids, the content address includes them"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline

              let renamed =
                  { tree with
                      Children = tree.Children |> List.mapi (fun i n -> { n with Id = "other-" + string i }) }

              Expect.equal (PipelineTree.pipelineHash renamed) (PipelineTree.pipelineHash tree) "one pipeline, one hash"

              Expect.notEqual
                  (PipelineTree.contentAddress renamed)
                  (PipelineTree.contentAddress tree)
                  "two trees, two addresses"

          testCase "the census agrees with the evaluator for every class of read"
          <| fun () ->
              for label, step in absentReads do
                  censusAgreesWithEvaluator label step

          testCase "a pipeline whose reads all resolve has no unknown read, and the evaluator runs it"
          <| fun () ->
              let p = PipelineTreeVectors.pipeline
              Expect.isEmpty (PipelineTree.unknownReads context (PipelineTree.lift p)) "none"
              Expect.isOk (DataFrame.evalPipeline p (table 4)) "the evaluator runs it"

          testCase "an edit that breaks the schema walk is refused by name and changes nothing"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline
              let derive = tree.Children[0].Id
              let filter = tree.Children[1].Id

              match PipelineTree.apply context (RemoveNode derive) tree with
              | Error(PipelineEditRefusal.UnknownColumn(step, "z", available)) ->
                  Expect.equal step filter "the reading step is named"
                  Expect.equal available [ "id"; "region"; "x"; "y" ] "the walk's columns"
              | other -> failtestf "expected UnknownColumn, got %A" other

              // A broken pipeline is repaired one edit at a time: an edit adding no new unknown read lands.
              let broken = PipelineTree.lift [ Filter(Col "nope"); Distinct ]

              Expect.isOk
                  (PipelineTree.apply context (RemoveNode broken.Children[1].Id) broken)
                  "an edit elsewhere in a broken pipeline lands"

          testCase "an ill-shaped tree is refused before the engine is asked"
          <| fun () ->
              let stepRoot = PipelineTree.step "pipeline" Distinct

              match PipelineTree.apply context (RemoveNode "x") stepRoot with
              | Error(PipelineEditRefusal.IllShaped("pipeline", _)) -> ()
              | other -> failtestf "expected IllShaped, got %A" other

              let nested =
                  { PipelineTree.empty with
                      Children =
                          [ { PipelineTree.step "a" Distinct with
                                Children = [ PipelineTree.step "b" Distinct ] } ] }

              Expect.isError
                  (PipelineTreeWire.decode (PipelineTreeWire.encode nested))
                  "the tree decoder holds the shape"

          testCase "the op-stream replays to the tree and its pipeline hash"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline
              let sw = PipelineTree.streamWitness context
              let stream = OpStream.canonical OpStream.defaultHash sw

              let edits =
                  PipelineTree.script tree
                  @ [ PipelineTree.insertAfter tree (Some tree.Children[0].Id) (PipelineTree.step "step-1" Distinct)
                      UpdateNode(PipelineTree.step tree.Children[1].Id (Filter(Binary(Gt, Col "x", Lit(Float 2.0)))))
                      RemoveNode tree.Children[3].Id ]

              let live, records =
                  edits
                  |> List.fold
                      (fun (state, recs) op ->
                          match OpStream.append stream (Human "author") op state recs with
                          | Ok(s, r) -> s, r
                          | Error e -> failtestf "the edit %s was refused: %A" (PipelineTreeCodec.encodeOp op) e)
                      (PipelineTree.empty, OpStream.empty)

              Expect.isTrue (OpStream.verifyChain stream records) "the chain verifies"
              let replayed = OpStream.replay sw PipelineTree.empty records |> ok

              Expect.equal
                  (PipelineTreeWire.encode replayed)
                  (PipelineTreeWire.encode live)
                  "replay reproduces the tree"

              Expect.equal (PipelineTree.pipelineHash replayed) (PipelineTree.pipelineHash live) "and its pipeline hash"

          testCase "a refused edit appends nothing to the stream"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline

              let stream =
                  OpStream.canonical OpStream.defaultHash (PipelineTree.streamWitness context)

              match OpStream.append stream (Human "author") (RemoveNode tree.Children[0].Id) tree OpStream.empty with
              | Error(PipelineEditRefusal.UnknownColumn _) -> ()
              | other -> failtestf "expected the schema refusal, got %A" other

          testCase "derive lands on the later version, and replaces every step between unrelated lifts"
          <| fun () ->
              let before = PipelineTree.lift PipelineTreeVectors.pipeline
              let after = PipelineTree.lift PipelineTreeVectors.unrelated
              let ops = PipelineTree.derive before after |> ok
              let landed = Ops.applyAll PipelineTree.policy PipelineTree.scope ops before |> ok
              Expect.equal (PipelineTreeWire.encode landed) (PipelineTreeWire.encode after) "the script lands on after"

              let removed =
                  ops
                  |> List.choose (function
                      | RemoveNode id -> Some id
                      | _ -> None)

              let inserted =
                  ops
                  |> List.choose (function
                      | InsertChild(_, n) -> Some n.Id
                      | _ -> None)

              Expect.equal
                  (Set.ofList removed)
                  (before.Children |> List.map _.Id |> Set.ofList)
                  "every old step removed"

              Expect.equal
                  (Set.ofList inserted)
                  (after.Children |> List.map _.Id |> Set.ofList)
                  "every new step inserted"

              Expect.isEmpty (PipelineTree.derive before before |> ok) "a version derives nothing from itself"

          testCase "two rewrites of different steps are independent; edits of one step list are not"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline

              let rewrite i t =
                  UpdateNode(PipelineTree.step tree.Children[i].Id t)

              let fp = PipelineTree.footprint tree
              Expect.isTrue (Ops.independent (fp [ rewrite 0 Distinct ]) (fp [ rewrite 1 Distinct ])) "different steps"

              Expect.isTrue
                  (Ops.independent (fp [ rewrite 0 Distinct ]) (fp [ RemoveNode tree.Children[3].Id ]))
                  "a rewrite and another step's removal"

              // Without the base the substrate cannot name an update's parent, and every pair is dependent.
              Expect.isFalse
                  (Ops.independent
                      (Ops.footprint PipelineTree.policy PipelineTree.scope [ rewrite 0 Distinct ])
                      (Ops.footprint PipelineTree.policy PipelineTree.scope [ rewrite 1 Distinct ]))
                  "the pure footprint serialises two rewrites"

              Expect.isFalse (Ops.independent (fp [ rewrite 1 Distinct ]) (fp [ rewrite 1 Distinct ])) "one step"

              Expect.isFalse
                  (Ops.independent
                      (fp [ InsertChild(PipelineTree.rootId, PipelineTree.step "a" Distinct) ])
                      (fp [ RemoveNode tree.Children[3].Id ]))
                  "both write the step list"

          testCase "a fresh id is free, and a mid-pipeline insert lands where it was asked"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline
              let id = PipelineTree.freshId tree
              Expect.equal id "step-1" "the first free"
              let anchor = tree.Children[1].Id

              let edited =
                  PipelineTree.apply
                      context
                      (PipelineTree.insertAfter tree (Some anchor) (PipelineTree.step id Distinct))
                      tree
                  |> ok

              Expect.equal
                  (edited.Children |> List.map _.Id)
                  [ tree.Children[0].Id; anchor; id; tree.Children[2].Id; tree.Children[3].Id ]
                  "after the anchor"

          testCase "the substrate's tree laws hold over the pipeline witness"
          <| fun () ->
              held (Conformance.witnessLaws PipelineTree.nodes PipelineTree.ids gen 603 120)
              held (Conformance.containmentLaws PipelineTree.grammar PipelineTree.nodes PipelineTree.ids gen 603 120)
              held (Conformance.diffContainedLaws PipelineTree.nodes PipelineTree.ids gen 603 120)
              // `loweringLaws` is not run: its adequacy guard asks for a filled container below the root,
              // and a pipeline tree has none by construction (the pipeline holds steps, a step holds
              // nothing). `PipelineTree.script` is held by the replay case above instead.
              held (
                  Conformance.footprintLaws PipelineTree.nodes PipelineTree.ids gen PipelineTreeCodec.encodeNode 603 120
              )

          testCase "the substrate's stream laws hold over the pipeline stream witness"
          <| fun () ->
              let tree = PipelineTree.lift PipelineTreeVectors.pipeline

              let streamGen: StreamGen<PipelineOp, PipelineNode> =
                  { State0 = tree
                    Op =
                      fun r ->
                          let i, r = ConfRng.intBelow 4 r
                          let id = tree.Children[i].Id

                          let op =
                              match i with
                              | 0 -> UpdateNode(PipelineTree.step id (Derive("z", Col "y")))
                              | 1 -> UpdateNode(PipelineTree.step id (Filter(Binary(Gt, Col "z", Lit(Float 3.0)))))
                              | 2 -> RemoveNode id
                              | _ -> UpdateNode(PipelineTree.step id Distinct)

                          op, r }

              held (Conformance.streamLaws (PipelineTree.streamWitness context) streamGen OpStream.defaultHash 603 60)

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (PipelineTreeVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      PipelineTreeVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered ]
