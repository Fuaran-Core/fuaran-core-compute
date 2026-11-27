### A pipeline is a substrate tree edited by ops, in `Fuaran.Compute.PipelineTree` (Phase 603, `DECISIONS.md` D82) — additive

The draft already carries breaking moves, so this change RIDES `0.45.0`.

**What changed.** A new package, `Fuaran.Compute.PipelineTree`, above `Fuaran.Compute.DataFrame`: `PipelineNode` and
`PipelineNodeContent` (the pipeline at the root, one node per step), `PipelineOp` (the substrate's `SkeletonOp` over
them), `PipelineEditRefusal`, `PipelineContext` with `ofSchema` and `ofSource`, the module `PipelineTree` (`lift`,
`lower`, `pipelineHash`, `contentAddress`, `shape`, `unknownReads`, `apply`, `applyScript`, `freshId`, `insertAfter`,
`script`, `streamWitness`, `footprint`, `derive`, and the witnesses `ids`, `nodes`, `scope`, `grammar`, `policy`),
the codec `PipelineTreeCodec` and `PipelineTreeWire`. Its baseline is the new `api/Fuaran.Compute.PipelineTree.txt`.
It takes `Fuaran.Core.Tree` and `Fuaran.Core.Ops`, and a consumer of it restores both. No other package's surface moves:
every other file under `api/` and `api/wire/` is unchanged.

**The wire.** New, in `api/wire/Fuaran.Compute.PipelineTree.txt`: the tree (`pipeline` and `step` nodes, a step carrying
the dataframe codec's own `Transform`) and the six ops and a script (D82 item 9). Nothing an existing package emits
moves.

**The law vectors.** `conformance/laws/pipeline-tree-laws.json` is new: lift, lower, the build script, each op kind
accepted and refused, the ill-typed edits refused by name, `derive` and the footprint verdict, rendered on .NET and under
node. `dataframe-parity.json`, `distributions-laws.json`, `elementary-laws.json`, `linear-algebra-laws.json`,
`regression-laws.json`, `solvers-laws.json` and `transform-laws.json` are byte-identical.

**What a consumer must do.** Nothing. The pipeline hash of a list is unchanged, and `PipelineTree.pipelineHash` of its
lift is that hash. A host that ports the tree certifies against `pipeline-tree-laws.json`, whose head declares the
input schema every edit vector is checked against.
