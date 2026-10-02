namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Plan (Phase 269) — the planner over a `Transform` pipeline: fusion, projection
//  pruning and reordering, with reordering admitted only by a totality verdict.
//
//  Pipelines are data, so they can be rewritten before execution. The reference
//  semantics are strict and first-error — a `Derive` evaluates its expression over
//  every live row before the next step runs, and the pipeline reports the first
//  step's first `EvalError` — so a `Filter` moved ahead of a `Derive` can REMOVE an
//  error the reference reports, by dropping the erroring row before it is derived.
//  A rewrite is therefore admissible only where the evaluation it removes from a
//  row's path is provably total, and the verdict that decides it is `isTotal`.
//
//  THREE CLASSES, AND THE RULE THAT ADMITS EACH.
//
//   * FUSION — `Sort` then `Limit n` runs as one stable top-n over the pinned
//     comparator with the arrival index as the tie-break, which reproduces
//     `List.sortWith` then the slice exactly. It changes which rows each step
//     evaluates not at all and is always admissible; the pipeline keeps both steps
//     and the driver runs the pair as the kernel (`RewriteClass.TopN` names it).
//     Adjacent `Filter`s are NOT fused into one predicate: the three-valued `And`
//     reads both operands on every row (for their errors), so a fused predicate
//     would evaluate the second filter on the rows the first already dropped —
//     more work than the two passes, not less, and a first error the reference
//     never reaches. Adjacent `Derive`s have no algebraic form to fuse into and
//     already share every vector but the one each adds.
//
//   * PROJECTION PRUNING — a column whose last read is behind it is dropped at the
//     earliest step after that read, by a `Project` keeping the live columns in
//     their order; only ahead of a step that drops the column anyway (`Project`,
//     `GroupBy`, `Pivot`), so nothing the pipeline emits changes; never across
//     `Distinct`, `Intersect`, `Except`, `Union`, `Window`, `Join` or `Unpivot`,
//     which read the whole row; and never where a step reads a column the schema
//     does not carry, because its refusal names the schema and a pruned schema
//     would be a different refusal.
//
//   * REORDERING — a `Filter` moves ahead of the `Sort` or `Derive` it follows,
//     when every evaluation it removes from a row's path is total:
//       - ahead of a `Sort`, the filter's own predicate must be total (ahead of
//         the sort it meets the rows in another order, so a first error could be
//         another row's) and the sort's keys literal;
//       - ahead of a `Derive`, the derive's expression must be total over the
//         schema before it (it no longer evaluates the rows the filter drops),
//         the filter must not read the derived column, every column the filter
//         reads must exist before the derive, and the derived column's type must
//         be one the typer decides statically (an expression whose present values
//         are all strings, or none): the evaluator infers a derived column's type
//         from the cells it produced, so over fewer rows it can differ, and a
//         schema that differs is a different answer.
//     A move the rule declines is reported by `explain`, with the rule.
//
//  THE TOTALITY VERDICT reads Phase 266's typer: a step is total over a schema
//  when evaluating it over ANY table of that schema — any cells, nulls included —
//  answers `Ok`. `Sort`, `Project`, `Limit` and `Distinct` are (with literal slots
//  and known sources); a `Filter` or a `Derive` is iff its expression is, and an
//  expression is not where an arm of the evaluator can refuse it: a `Param` or a
//  `Now` (bound only by an env or a clock the schema does not have), a column the
//  schema does not type, an integer `Add` / `Sub` / `Mul` (overflow), a `Mod`, a
//  `Div` whose operands the typer cannot call numeric, a comparison over types the
//  pinned ordering does not compare, a `Cast` that parses, a scalar function over
//  an argument it would refuse. `proofs/Pipeline.fst` proves the verdict sound over
//  the modelled evaluator (`verdict_sound`), under the one assumption it names.
//
//  WHAT THE PLANNER IS HELD TO. `Conformance.plannerLaws`: over generated
//  (schema, pipeline, table) triples the planned evaluation equals the reference
//  as written, errors included, with every rewrite class reached and at least one
//  reorder declined. Every evaluator entry point plans before it folds, and so
//  does the incremental seam (`Incremental.plannedOf` reports the form it ran);
//  `DataFrame.evalPipelineAsWritten` folds the pipeline as given.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// The planner (Phase 269): `rewrite` a pipeline before evaluation, `isTotal` the verdict that
/// admits a reorder, `explain` the report of what was rewritten and what was declined and why.
/// Every evaluator entry point runs `rewrite` itself; a host calls it to see the form that runs.
[<RequireQualifiedAccess>]
module Plan =

    /// The planned pipeline over a source of schema `cols`: total, and idempotent (planning the
    /// planned form changes nothing). A pipeline the planner cannot read past a step is emitted as
    /// written from that step on.
    let rewrite (cols: Schema) (pipeline: Transform list) : Transform list = DataFrame.Planner.rewrite cols pipeline

    /// The totality verdict: `true` only where evaluating the step over ANY table of schema `cols`
    /// returns `Ok`. Conservative by construction — `false` names an arm that can refuse, `true`
    /// is backed by the arm's own code.
    let isTotal (cols: Schema) (step: Transform) : bool = DataFrame.Planner.isTotal cols step

    /// Every rewrite the planner applied and every one it declined, with the rule, for a host that
    /// wants to know why its pipeline runs as it does. `Planned` is what `rewrite` answers.
    let explain (cols: Schema) (pipeline: Transform list) : PlanReport = DataFrame.Planner.explain cols pipeline
