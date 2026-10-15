# Proofs

Two F\* models of this repository's code, each checked by a pinned prover, extracted to F#, and run
beside the production code it models. The claims ladder at the repository root,
[`../proofs.json`](../proofs.json), says row by row what is **proved**, what is only **tested**,
what is **assumed** and what is **policy**; this page says how the leg works. A claim that is not a
row in the ladder is not claimed.

| Model | What it models | What it proves |
|---|---|---|
| [`ColumnOps.fst`](ColumnOps.fst) | `ColumnOps.apply` / `canApply` / `invert` / `Diff.toOps` over a table with a validity mask, clause for clause; and, since Phase 268, a column held as a rope of chunks with `Chunked.setCell` over it | totality and its rejection characterisation, all-or-nothing rejection, the dry run's agreement, well-formedness preserved by every accepted structural operation, `invert`'s round trip and its partial cases, `Diff.toOps`'s script reconstructing its target, and chunk sharing — the edit through the rope is `apply`'s flat edit, and every chunk before and after the one holding the row is the chunk it was |
| [`Pipeline.fst`](Pipeline.fst) | `DataFrame.evalPipelineWithInEnvCounted`: the closed `ColExpr` / `Transform` algebra, the expression evaluator with its four loops, `evalFilter`, `evalDerive`, the step dispatch and the counted fold | totality, budget monotonicity in the pipeline prefix, the evaluator's visits bounded by the expression's nodes, the work bounded by the count under the section-21.8 expression limit taken as a hypothesis — and the finding that the limit is enforced nowhere; since Phase 269 also the planner's reorder (`derive_then_filter`: a total derive whose column type the schema decides — string-typed until Phase 338 — removed from a filtered row's path preserves the driver's result; and, since Phase 338, `derive_type_rows_free`: that type is the same over any two frames of a schema) and the totality verdict's soundness over the modelled evaluator (`verdict_sound`), under the one assumption `admits` names |
| [`Limits.fst`](Limits.fst) | the wire format's section-21 resource limits as named premises | the relations between them; copied from the Fuaran.Core repository because `Pipeline` opens it |

## The leg

```powershell
pwsh ./proofs/check.ps1            # check every model once from a cold cache, run the host families
pwsh ./proofs/check.ps1 -Runs 3    # what CI runs: three cold runs, so a check is reproducible
pwsh ./proofs/check.ps1 -Extract   # rewrite oracle/*.fs from a fresh extraction, then commit them
```

[`check.ps1`](check.ps1) declares the models, the host families and where the host lives; the engine
is [`kit/check-proof-leg.ps1`](kit/check-proof-leg.ps1). It resolves the prover pinned in
[`fstar-pin.json`](fstar-pin.json) (downloading and hash-checking the release on first use, under the
git-ignored `.fstar/`), verifies each model from a cold cache, extracts each to F# and fails when the
committed [`oracle/`](oracle/) file and the fresh extraction differ — which is what makes "the oracle
IS the model" a checked claim — and then runs the host families in the test suite:

- **`Proofs.Oracle`** runs each extracted model beside production over generated inputs (and, for
  the pipeline, over the transform law vectors), with a go-red per model showing the comparison can
  fail;
- **`Proofs.Ladder`** holds [`../proofs.json`](../proofs.json) to the tree: a proved row's theorem is
  a top-level declaration of a model this leg checks, every checked model has a proved row, and a
  tested row's cases exist in `Proofs.Oracle`;
- **`Proofs.Coverage`** holds the coverage declaration: every packable package is named by a model
  in [`modules.json`](modules.json) or excluded, with a reason, in
  [`coverage-exclusions.json`](coverage-exclusions.json); and, since Phase 432, every public
  operation of a modelled package is named by a ladder row or recorded in that file's `operations`
  census (below).

[`modules.json`](modules.json) declares what each model COSTS: a budget whose overshoot is a named
warning, and a floor under which a run fails, because a module that verified faster than it can is
evidence of a warm cache rather than of speed.

## What is not proved

A package with a model is not a package whose every operation is proved. The `operations` census in
[`coverage-exclusions.json`](coverage-exclusions.json) names, operation by operation, what no model
covers: every public method of a modelled package (from its `../api/*.txt` baseline) and every
relational verb (a case of `Transform`) is either named by a `proved` or `tested` row's
`evidence.operations` in [`../proofs.json`](../proofs.json) or carries an entry with a class from the
Fuaran.Core repository's vocabulary (`trivial`, `forward`, `obsolete`, `host-seam`,
`measured-elsewhere`), a one-line reason and, for `measured-elsewhere`, the test file that stands in.
`Proofs.Coverage` fails on an operation in neither, and on an entry for an operation that is no longer
published or that a ladder row now names — so the phase that models an operation names it on a row and
deletes its entry in the same commit. A `measured-elsewhere` entry is weaker than a theorem and says
so: tests sample the contract, nothing states it.

The table is generated from the census and the ladder by that family (`CORE_APPROVE_LADDER=1`
rewrites it); no count here is written by hand.

<!-- operation-coverage:begin — generated from ../api/*.txt, ../proofs.json and coverage-exclusions.json by the Proofs.Coverage family; CORE_APPROVE_LADDER=1 rewrites it -->
| Package | Owner | Operations | Ladder | trivial | forward | obsolete | host-seam | measured-elsewhere |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `Fuaran.Compute.ColumnOps` | `ColumnOps` | 17 | 5 | 1 | 0 | 0 | 0 | 11 |
| `Fuaran.Compute.DataFrame` | `ColExpr` | 5 | 0 | 0 | 0 | 0 | 0 | 5 |
| `Fuaran.Compute.DataFrame` | `ColumnKnowledge` | 6 | 0 | 6 | 0 | 0 | 0 | 0 |
| `Fuaran.Compute.DataFrame` | `DataFrame` | 42 | 4 | 2 | 7 | 0 | 0 | 29 |
| `Fuaran.Compute.DataFrame` | `DataFrameCodec` | 7 | 0 | 0 | 0 | 0 | 0 | 7 |
| `Fuaran.Compute.DataFrame` | `Delta` | 21 | 0 | 0 | 0 | 0 | 0 | 21 |
| `Fuaran.Compute.DataFrame` | `DeltaCodec` | 4 | 0 | 0 | 0 | 0 | 0 | 4 |
| `Fuaran.Compute.DataFrame` | `Incremental` | 25 | 0 | 0 | 0 | 0 | 0 | 25 |
| `Fuaran.Compute.DataFrame` | `IncrementalCodec` | 7 | 0 | 0 | 0 | 0 | 0 | 7 |
| `Fuaran.Compute.DataFrame` | `MorselWorker` | 3 | 0 | 0 | 0 | 0 | 3 | 0 |
| `Fuaran.Compute.DataFrame` | `NowGrain` | 2 | 0 | 2 | 0 | 0 | 0 | 0 |
| `Fuaran.Compute.DataFrame` | `Plan` | 3 | 0 | 0 | 0 | 0 | 0 | 3 |
| `Fuaran.Compute.DataFrame` | `RowIdentity` | 4 | 0 | 0 | 0 | 0 | 0 | 4 |
| `Fuaran.Compute.DataFrame` | `SchemaWalk` | 14 | 0 | 0 | 0 | 0 | 0 | 14 |
| `Fuaran.Compute.DataFrame` | `Slot` | 3 | 0 | 3 | 0 | 0 | 0 | 0 |
| `Fuaran.Compute.DataFrame` | `Transform` | 22 | 2 | 0 | 0 | 0 | 0 | 20 |
| `Fuaran.Compute.DataFrame` | `WorkerPool` | 5 | 0 | 0 | 0 | 0 | 2 | 3 |
| **Total** |  | 190 | 11 | 14 | 7 | 0 | 5 | 153 |
<!-- operation-coverage:end -->

## Adopted by copy

The leg is the proof-leg kit from the [Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core)
repository, adopted by COPY: [`kit/`](kit/) is copied verbatim, `check.ps1`, `modules.json`, the
oracle project and the root `proofs.json` are its templates filled in, and the pin, the two
hand-written runtime shims under `oracle/` and `Limits.fst` are copies of that repository's live
files. [`../copies.json`](../copies.json) declares every copied file, so a copy that has drifted
from its source is named rather than discovered; the remedy is always to re-copy from the source,
never to edit the copy. The two models and their committed extractions are not copies — they moved
here, with their history (Phase 259).
