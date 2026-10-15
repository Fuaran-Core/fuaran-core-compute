# Proofs

Seven F\* models of this repository's code, each checked by a pinned prover, extracted to F#, and run
beside the production code it models. The claims ladder at the repository root,
[`../proofs.json`](../proofs.json), says row by row what is **proved**, what is only **tested**,
what is **assumed** and what is **policy**; this page says how the leg works. A claim that is not a
row in the ladder is not claimed.

| Model | What it models | What it proves |
|---|---|---|
| [`ColumnOps.fst`](ColumnOps.fst) | `ColumnOps.apply` / `canApply` / `invert` / `Diff.toOps` over a table with a validity mask, clause for clause; and, since Phase 268, a column held as a rope of chunks with `Chunked.setCell` over it | totality and its rejection characterisation, all-or-nothing rejection, the dry run's agreement, well-formedness preserved by every accepted structural operation, `invert`'s round trip and its partial cases, `Diff.toOps`'s script reconstructing its target, and chunk sharing — the edit through the rope is `apply`'s flat edit, and every chunk before and after the one holding the row is the chunk it was |
| [`Pipeline.fst`](Pipeline.fst) | `DataFrame.evalPipelineWithInEnvCounted`: the closed `ColExpr` / `Transform` algebra, the expression evaluator with its four loops, `evalFilter`, `evalDerive` — its column typed by the typer or by the cells (refusing a float beside a decimal, and since Phase 470 two types no column holds together, D18) and PACKED as `Vec.packAt` carries it (`pack_cells`, Phase 470, D38: an int under a float or a decimal column as that type's value while the typed pass holds, every cell as it was where it is abandoned), the step dispatch and the counted fold | totality, budget monotonicity in the pipeline prefix, the evaluator's visits bounded by the expression's nodes, the work bounded by the count under the section-21.8 expression limit taken as a hypothesis — and the finding that the limit is enforced nowhere; since Phase 269 also the planner's reorder (`derive_then_filter`: a total derive whose column type the schema decides — string-typed until Phase 338 — and whose cells that type carries as they are (the packing premise `packs_as_is`, Phase 470, which the planner's corollary `planner_reorder_sound` discharges from the verdict, `derive_cells_fit`) removed from a filtered row's path preserves the driver's result; and, since Phase 338, `derive_type_rows_free`: that type is the same over any two frames of a schema) and the totality verdict's soundness over the modelled evaluator (`verdict_sound`), under the one assumption `admits` names |
| [`Incremental.fst`](Incremental.fst) | `Incremental.prime` / `Incremental.refresh` on the row-local path (Phase 433): the per-step cell caches `IncrementalEval` retains, the walk that reads a cached cell for every row the delta does not name and evaluates the rest, over pipelines of `Filter` and `Derive` — the two verbs `Pipeline.fst` models as clauses; since Phase 470 (D38) the derived column packed through `Pipeline.pack_cells` (`pack_cache`), the cache kept raw, the pack KEY (type and verdict) recorded per derive and reuse switched off past a derive whose key moved (`retypedSince`, D37); the other verb classes are declared out in `DECISIONS.md` D31 | priming is full evaluation (`prime_is_full`); a refresh against a primed state under a truthful delta is the prime over the new source, output and caches (`refresh_is_prime`); and so the refreshed answer is the full evaluation over the updated source (`refresh_agrees`) — under the one premise the ladder names (`incremental-delta-truthful`), and on the lemma that the evaluator reads a schema's names and never its types (`eval_expr_names`) |
| [`Relational.fst`](Relational.fst) | the twelve relational verbs `Pipeline.fst` takes as a parameter (Phase 434): each verb's OUTPUT SCHEMA and every refusal that reads the schema or the pipeline text (`step_schema` — `evalProject`'s pairs, `evalGroupBy`'s keys and aggregates, the join's `joinRightNames` suffix rule and `joinKeyIdx`, `windowRefusal` and the window's type rule, the pivot's data-named columns, the unpivot's `variable` and `value`, the sort's and the limit's slots, the union's type join and the set operations' name agreement), the source seam `evalSource`, and the static walk `SchemaWalk.ofTransform` over `SchemaKnowledge` (`walk`, all fourteen arms); row content is read only where a schema reads it (the pivot's `on` values, the unpivot's melted cells), and `other_of` builds Pipeline's twelve-verb parameter from the schema model and a ROW FUNCTION, which is all that stays assumed | the walk is sound for the evaluator's schema for every verb (`walk_sound`: a closed walk reads the evaluator's names and types, an open one reads names and types it produces; since Phase 469, D36, the union too — the walk and the evaluator read one per-column rule, `union_ty`, and re-introducing the left-type arm fails `union_sound`); a combining join over distinct-named operands answers distinct names (`join_no_dups`, D20 — re-introducing 411's defect fails the leg) and so does a window (`window_no_dups`, D28), and a `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` over any input (`project_no_dups`, …, Phase 468, D35: the one check `repeated_output` refuses a repeated output name — removing it from the group-by arm fails the leg); every unknown column is refused with `UnknownColumn` in each arm's order (`sort_unknown_refused` — re-introducing 412's defect fails the leg — and seven more); and D32's two findings are closed: four verbs answering a repeated name (`*_repeat_refused`, D35) and the walk keeping a union's left type (`union_walk_widens`, D36) |
| [`Frame.fst`](Frame.fst) | the evaluator's dense frame and its kernels (Phase 435): `Vec` and `Vec.cellAt` at every column type, the boundary in (`maskOf` — a null-free column's `AllValid` read as every row present — `ofColumn`, `ofDecimalTexts`, `pack`, `Frame.ofTable`, `Frame.ofRows`, `Frame.rowsOf`) and out (`gather`, `toColumn`, `Frame.toTable`), `DataFrame.prepare` / `prepareRows` / `toTable`, and the portable comparison kernels with `filterBits` and the filter's kernel branch; the native member, the morsel runner, the sort kernels and the rope are declared out in `DECISIONS.md` D34 | the frame means exactly its rows: over a table whose columns hold their fields' types and the table's length, cell `(i, j)` of `Frame.ofTable` is Core's `Column.cell i` of column `j`, with or without a mask (`of_table_reads_columns`; `view_reads`, `mask_of_reads`); the read-back is total (`rows_of_reads`); `ofRows` reads every cell as its type carries it and rows carried exactly come back as they went in (`pack_reads`, `of_rows_exact`); `Frame.toTable` writes exactly the logical rows (`to_table_reads`); and a filter the kernels answer is `Pipeline.fst`'s `Filter` step over the read-back, never an error (`filter_kernel_is_step`) — under the kernel premise the ladder names (`frame-kernel-primitives`) |
| [`IncrementalMaintained.fst`](IncrementalMaintained.fst) | `Incremental.prime` / `Incremental.refresh` past the row-local class (Phase 464): the walk over `Project`, `Limit`, `Window`, the filtering joins (`Semi` / `Anti`) and, since Phase 471, `Sort` (`MergeOrder`: the step's arm, its record of the arrival and the produced order, the reference's stable sort as `MaintainedSort.sort_rows`), and the maintained `GroupBy` — the groups of the rows alive at the step in first-appearance order, each group's aggregates reused exactly when every member is reusable and its member tokens are the prior group's (`groupStable`, `sameMembers`) and recomputed over its members in order otherwise, the group table walked on under one token per key; the derived column packed through `Incremental.pack_cache` and its pack key recorded per evaluating step, a derive whose key moved reusing nothing downstream (D37, D38); the reference is `Pipeline.fst`'s driver over `Relational.fst`'s `other_of` with this module's row functions, parameterised by the aggregate function, the join key match, the window column and the group token — `Sort`, the resumed prefix-fold window, the fast paths, the guards, the chunked path and the codec are declared out in `DECISIONS.md` D37 | priming is full evaluation (`mprime_is_full`); a refresh against a primed state under a truthful delta — read one row at a time, `wk`, weaker than Phase 433's premise — over a prior source of distinct tokens is the prime over the new source, output, every entry, the group table and the tail (`mrefresh_is_prime`); and so the refreshed answer is the full evaluation over the updated source (`mrefresh_agrees`) — under the premises the ladder names (`incremental-maintained-primitives`) |
| [`MaintainedSort.fst`](MaintainedSort.fst) | the maintained `Sort` (Phase 471): the full stable sort as the insertion sort under the comparator with the arrival position as the tie-break (`sort_alive`, F#: `sortSlots` / `evalSort`), the merge of the moved rows into the prior produced order by binary insertion (`merge_sort`, F#: `mergeOrders`), the reuse condition `arrived_as_before` (F#: `arrivedAsBefore`) and the fall-back to a sort from scratch; and the comparator concretely — `compare_resolved` over `compare_cells`, `DataFrame.compareResolved` and `compareCells` arm for arm | `sort_step_reuse`: a sort refreshed against the record a prime left answers the full stable sort of the rows that arrived, under the delta premise and a comparator that is a total preorder — through the uniqueness of a sorted permutation (`sorted_unique`), the binary-insertion merge being sorted (`merge_bin_sorted`, `bisect_cut`) and the cached run being sorted now (`lookup_sorted`, `relabel_lt`); `compare_resolved_preorder`: over the rows a typed frame holds the comparator is a total preorder, from the primitives' laws (`sprims_laws`), and `compare_cells_none` names the pairs it reads as a tie — the premise check of D39
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
| `Fuaran.Compute.DataFrame` | `DataFrame` | 42 | 11 | 2 | 7 | 0 | 0 | 22 |
| `Fuaran.Compute.DataFrame` | `DataFrameCodec` | 7 | 0 | 0 | 0 | 0 | 0 | 7 |
| `Fuaran.Compute.DataFrame` | `Delta` | 21 | 0 | 0 | 0 | 0 | 0 | 21 |
| `Fuaran.Compute.DataFrame` | `DeltaCodec` | 4 | 0 | 0 | 0 | 0 | 0 | 4 |
| `Fuaran.Compute.DataFrame` | `Incremental` | 25 | 3 | 0 | 0 | 0 | 0 | 22 |
| `Fuaran.Compute.DataFrame` | `IncrementalCodec` | 7 | 0 | 0 | 0 | 0 | 0 | 7 |
| `Fuaran.Compute.DataFrame` | `MorselWorker` | 3 | 0 | 0 | 0 | 0 | 3 | 0 |
| `Fuaran.Compute.DataFrame` | `NowGrain` | 2 | 0 | 2 | 0 | 0 | 0 | 0 |
| `Fuaran.Compute.DataFrame` | `Plan` | 3 | 0 | 0 | 0 | 0 | 0 | 3 |
| `Fuaran.Compute.DataFrame` | `RowIdentity` | 4 | 0 | 0 | 0 | 0 | 0 | 4 |
| `Fuaran.Compute.DataFrame` | `SchemaWalk` | 14 | 2 | 0 | 0 | 0 | 0 | 12 |
| `Fuaran.Compute.DataFrame` | `Slot` | 3 | 0 | 3 | 0 | 0 | 0 | 0 |
| `Fuaran.Compute.DataFrame` | `Transform` | 22 | 14 | 0 | 0 | 0 | 0 | 8 |
| `Fuaran.Compute.DataFrame` | `WorkerPool` | 5 | 0 | 0 | 0 | 0 | 2 | 3 |
| **Total** |  | 190 | 35 | 14 | 7 | 0 | 5 | 129 |
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
