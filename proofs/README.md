# Proofs

Two F\* models of this repository's code, each checked by a pinned prover, extracted to F#, and run
beside the production code it models. The claims ladder at the repository root,
[`../proofs.json`](../proofs.json), says row by row what is **proved**, what is only **tested**,
what is **assumed** and what is **policy**; this page says how the leg works. A claim that is not a
row in the ladder is not claimed.

| Model | What it models | What it proves |
|---|---|---|
| [`ColumnOps.fst`](ColumnOps.fst) | `ColumnOps.apply` / `canApply` / `invert` / `Diff.toOps` over a table with a validity mask, clause for clause | totality and its rejection characterisation, all-or-nothing rejection, the dry run's agreement, well-formedness preserved by every accepted structural operation, `invert`'s round trip and its partial cases, and `Diff.toOps`'s script reconstructing its target |
| [`Pipeline.fst`](Pipeline.fst) | `DataFrame.evalPipelineWithInEnvCounted`: the closed `ColExpr` / `Transform` algebra, the expression evaluator with its four loops, `evalFilter`, `evalDerive`, the step dispatch and the counted fold | totality, budget monotonicity in the pipeline prefix, the evaluator's visits bounded by the expression's nodes, the work bounded by the count under the section-21.8 expression limit taken as a hypothesis — and the finding that the limit is enforced nowhere |
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
  [`coverage-exclusions.json`](coverage-exclusions.json).

[`modules.json`](modules.json) declares what each model COSTS: a budget whose overshoot is a named
warning, and a floor under which a run fails, because a module that verified faster than it can is
evidence of a warm cache rather than of speed.

## Adopted by copy

The leg is the proof-leg kit from the [Fuaran.Core](https://github.com/Fuaran-Core/fuaran-core)
repository, adopted by COPY: [`kit/`](kit/) is copied verbatim, `check.ps1`, `modules.json`, the
oracle project and the root `proofs.json` are its templates filled in, and the pin, the two
hand-written runtime shims under `oracle/` and `Limits.fst` are copies of that repository's live
files. [`../copies.json`](../copies.json) declares every copied file, so a copy that has drifted
from its source is named rather than discovered; the remedy is always to re-copy from the source,
never to edit the copy. The two models and their committed extractions are not copies — they moved
here, with their history (Phase 259).
