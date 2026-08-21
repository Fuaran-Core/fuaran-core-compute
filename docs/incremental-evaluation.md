# Adopting incremental `Transform` evaluation

A one-page on-ramp for a consumer that already evaluates a `Transform` pipeline with
`DataFrame.evalPipeline` and wants a refresh to cost the rows that changed rather than the rows it
has. Read it alongside [`ADOPTION.md`](ADOPTION.md); nothing here replaces the reference evaluator,
and adopting it is reversible at any point.

Everything below is in `Fuaran.Core.DataFrame` (`0.11.0`), FSharp.Core-only and Fable-clean.

## The shape

Three calls, and the middle one is the whole of it.

```fsharp
open Fuaran.Core

// 1. Tell Core how to key a row. A per-call witness — Core never learns what a key MEANS.
let idw = RowIdentity.byColumn "id"

// 2. Prime once. This is a full evaluation; it also records what it computed.
let state = Incremental.primeOn idw pipeline source |> Result.defaultWith (fun _ -> failwith "…")

// 3. On each change, describe it and refresh.
let delta = Delta.diff idw source source' |> Result.defaultValue FullRefresh
let next = Incremental.refreshOn idw pipeline state delta source'
```

`Incremental.result next` is the new table, and it is **equal to `DataFrame.evalPipeline pipeline
source'`** — always, for every delta, whichever internal path ran. That equality is the contract;
the saving is the implementation detail. `Incremental.footprint next` says what the refresh cost.

Where the pipeline resolves `Ref` sources or reads `Param`s, use `Incremental.prime` /
`Incremental.refresh`, which take a resolver and an env exactly as
`DataFrame.evalPipelineWithInEnv` does.

## Ask before you adopt

`Incremental.plan pipeline` classifies every step **before any evaluation happens**, so a consumer
can decide what to wire without running anything:

```fsharp
match (Incremental.plan pipeline).Strategy with
| RowLocal | RowLocalThenGroups -> // a refresh will be restricted
| ReferenceOnly reason -> // it will not; `Incremental.reasonString reason` says why
```

- **`PropagateRows`** — `Filter`, `Project`, `Derive`. Output for a row is a function of that row,
  so only the named rows are re-evaluated.
- **`MaintainGroups`** — a `GroupBy` as the pipeline's **last** step. The group partition is
  maintained and only the affected groups' aggregates are recomputed. The same `GroupBy` earlier in
  the pipeline is declined, because what follows it would need a delta over the *group* table.
- **`FallBack`** — `Sort`, `Limit`, `Window`, `Pivot`, `Unpivot`, `Join`, `Union`, `Intersect`,
  `Except`. Their output for one row depends on rows a delta does not name, so the pipeline is
  evaluated in full and the footprint says so.

Adoption is therefore per pipeline, not per application: a declined pipeline costs exactly what it
costs today, and can sit beside an adopted one.

## Reading the footprint

`Incremental.footprint` returns `{ SourceRows; ResultRows; Recompute }`. `Recompute` is the account:

| Case | Meaning |
|---|---|
| `Primed n` | the first evaluation — `n` row expressions evaluated |
| `ReusedPrior` | nothing changed and the source did not move; the prior result stands |
| `RowsRecomputed n` | only the delta's rows were re-evaluated |
| `GroupsRecomputed (n, g)` | `n` rows re-evaluated, `g` groups' aggregates recomputed |
| `FullRecompute reason` | the pipeline was evaluated in full; `reason` says why |

`Incremental.rowsEvaluated` and `Incremental.footprintString` project it. The counts carry no clock,
so they are deterministic and identical on every host — which is what makes them safe to assert on
in a consumer's own tests, and what lets a regression ("this refresh started recomputing
everything") be a failing assertion rather than a stopwatch reading.

## The one obligation

**The delta must truthfully describe the change** from the source the state was last evaluated
against to the source now passed in. `Delta.diff` produces exactly that. A delta that under-reports
is a statement about the data that is false, and no evaluator can detect one without recomputing the
answer it was asked to avoid recomputing.

Everything else is handled for you and recorded rather than assumed: a changed pipeline, a changed
env, a moved schema, a `FullRefresh` delta, an ordinal-addressed delta, or an identity witness that
cannot key the source each degrade to a full evaluation carrying its reason. Degrading is always
available and always correct.

## What it does not do

- **It does not maintain a delta on the OUTPUT.** A refresh returns the new table, not a description
  of how it differs from the last one. A consumer that wants that runs `Delta.diff` over the two
  results — which is a real cost, so prefer it only where a downstream stage genuinely needs it.
- **It does not reason about column relevance.** "This column changed and the pipeline never reads
  it, so the prior result stands" is `DataFrame.evalFrom`'s job (the coarse `Change` vocabulary), and
  `Delta.toChange` bridges to it. The two compose: ask `evalFrom` whether the change matters at all,
  and this seam how much of it to recompute.
- **It does not persist.** `IncrementalEval` is in-memory state a consumer holds between refreshes.
  Losing it costs one full evaluation, never a wrong answer.

## Verifying your own adoption

The equivalence family `IncrementalDelta.laws` (in `Fuaran.Core.Conformance`) certifies the seam
itself against the reference evaluator over a generated corpus. A consumer does not need to re-run
it, but the pattern is worth copying for your own pipelines: evaluate both ways and assert equality,
then assert the footprint. The first catches a wrong answer; the second catches the subtler
regression where the answer stays right and the saving quietly disappears.
